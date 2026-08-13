using Microsoft.AspNetCore.Mvc;
using MangosSuperUI.Services;

namespace MangosSuperUI.Controllers;

/// <summary>
/// Change Graph — audit_log as a drillable tree (Domain → Batch → Entry → Field)
/// with undo at the batch and entry level. All logic lives in <see cref="ChangeGraphService"/>.
/// </summary>
public class ChangesController : Controller
{
    private readonly ChangeGraphService _graph;
    private readonly DivergenceService _divergence;
    private readonly ILogger<ChangesController> _logger;

    public ChangesController(ChangeGraphService graph, DivergenceService divergence, ILogger<ChangesController> logger)
    {
        _graph = graph;
        _divergence = divergence;
        _logger = logger;
    }

    public IActionResult Index() => View();

    // ===================== DIVERGENCE (state view) =====================

    /// <summary>GET /Changes/Drift — per-domain counts of what currently differs from stock.</summary>
    [HttpGet]
    public async Task<IActionResult> Drift([FromQuery] string? mode)
    {
        try
        {
            return Json(await _divergence.GetOverviewAsync(mode ?? "tracked"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Divergence overview failed");
            return Json(new { error = ex.Message });
        }
    }

    /// <summary>
    /// GET /Changes/DriftDomain — one level of the drill-down. `path` walks the facet tree
    /// (e.g. "lootified/instance/36"); the response says whether the level is another set
    /// of facets or the leaf list of entries.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> DriftDomain([FromQuery] string domain, [FromQuery] string? mode,
        [FromQuery] string? search, [FromQuery] string? path)
    {
        if (string.IsNullOrWhiteSpace(domain))
            return Json(new { error = "Missing domain" });

        try
        {
            return Json(await _divergence.GetTreeAsync(domain, mode ?? "tracked", path, search));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Divergence tree query failed for {Domain} at '{Path}'", domain, path);
            return Json(new { error = ex.Message });
        }
    }

    /// <summary>POST /Changes/Rescan — drop the deep-scan cache so the next read re-measures.</summary>
    [HttpPost]
    public IActionResult Rescan()
    {
        _divergence.InvalidateCache();
        return Json(new { success = true });
    }

    /// <summary>POST /Changes/Resolve — put one or more entries back to stock.</summary>
    [HttpPost]
    public async Task<IActionResult> Resolve([FromBody] ResolveRequest request)
    {
        if (string.IsNullOrWhiteSpace(request?.Domain))
            return Json(new { success = false, error = "Missing domain" });

        try
        {
            if (request.Entries is { Length: > 1 })
                return Json(await _divergence.ResolveManyAsync(request.Domain, request.Entries, Ip));

            var entry = request.Entries is { Length: 1 } ? request.Entries[0] : request.Entry;
            if (entry <= 0) return Json(new { success = false, error = "Missing entry" });

            var result = await _divergence.ResolveAsync(request.Domain, entry, Ip);
            return Json(new { success = result.Success, error = result.Error, summary = result.Summary });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Divergence resolve failed for {Domain}", request.Domain);
            return Json(new { success = false, error = ex.Message });
        }
    }

    /// <summary>GET /Changes/Overview — domain rollups plus grand totals.</summary>
    [HttpGet]
    public async Task<IActionResult> Overview([FromQuery] string? search, [FromQuery] string? op,
        [FromQuery] int? days, [FromQuery] string? show)
    {
        try
        {
            return Json(await _graph.GetOverviewAsync(Filter(search, op, days, show)));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Change graph overview failed");
            return Json(new { error = ex.Message });
        }
    }

    /// <summary>GET /Changes/Batches — the tool runs inside one domain.</summary>
    [HttpGet]
    public async Task<IActionResult> Batches([FromQuery] string domain, [FromQuery] string? search,
        [FromQuery] string? op, [FromQuery] int? days, [FromQuery] string? show,
        [FromQuery] int page = 1)
    {
        if (string.IsNullOrWhiteSpace(domain))
            return Json(new { error = "Missing domain" });

        try
        {
            return Json(await _graph.GetBatchesAsync(domain, Filter(search, op, days, show), page));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Change graph batches failed for domain {Domain}", domain);
            return Json(new { error = ex.Message });
        }
    }

    /// <summary>GET /Changes/Entries — the individual changes inside one batch.</summary>
    [HttpGet]
    public async Task<IActionResult> Entries([FromQuery] string batch, [FromQuery] int page = 1)
    {
        if (string.IsNullOrWhiteSpace(batch))
            return Json(new { error = "Missing batch" });

        try
        {
            return Json(await _graph.GetEntriesAsync(batch, page));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Change graph entries failed for batch {Batch}", batch);
            return Json(new { error = ex.Message });
        }
    }

    /// <summary>GET /Changes/Entry — one change with its field diff and undo description.</summary>
    [HttpGet]
    public async Task<IActionResult> Entry([FromQuery] long id)
    {
        try
        {
            return Json(await _graph.GetEntryAsync(id));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Change graph entry detail failed for {Id}", id);
            return Json(new { found = false, error = ex.Message });
        }
    }

    /// <summary>POST /Changes/RevertEntry — undo a single logged change.</summary>
    [HttpPost]
    public async Task<IActionResult> RevertEntry([FromBody] RevertEntryRequest request)
    {
        if (request?.Id is not > 0)
            return Json(new { success = false, error = "Missing id" });

        var result = await _graph.RevertEntryAsync(request.Id, Ip);
        return Json(new { success = result.Success, error = result.Error, rows = result.RowsAffected, summary = result.Summary });
    }

    /// <summary>POST /Changes/RevertBatch — undo everything revertable in one batch.</summary>
    [HttpPost]
    public async Task<IActionResult> RevertBatch([FromBody] RevertBatchRequest request)
    {
        if (string.IsNullOrWhiteSpace(request?.Batch))
            return Json(new { success = false, error = "Missing batch" });

        try
        {
            return Json(await _graph.RevertBatchAsync(request.Batch, Ip));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Change graph batch revert failed for {Batch}", request.Batch);
            return Json(new { success = false, error = ex.Message });
        }
    }

    private string? Ip => HttpContext.Connection.RemoteIpAddress?.ToString();

    private static ChangeGraphService.GraphFilter Filter(string? search, string? op, int? days, string? show) => new()
    {
        Search = search,
        Operator = op,
        Days = days,
        Show = show,
    };

    public class ResolveRequest
    {
        public string? Domain { get; set; }
        public int Entry { get; set; }
        public int[]? Entries { get; set; }
    }

    public class RevertEntryRequest
    {
        public long Id { get; set; }
    }

    public class RevertBatchRequest
    {
        public string? Batch { get; set; }
    }
}
