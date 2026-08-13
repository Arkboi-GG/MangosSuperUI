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
    private readonly ILogger<ChangesController> _logger;

    public ChangesController(ChangeGraphService graph, ILogger<ChangesController> logger)
    {
        _graph = graph;
        _logger = logger;
    }

    public IActionResult Index() => View();

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

    public class RevertEntryRequest
    {
        public long Id { get; set; }
    }

    public class RevertBatchRequest
    {
        public string? Batch { get; set; }
    }
}
