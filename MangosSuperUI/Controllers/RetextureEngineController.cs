// RetextureEngineController.cs
//
// Backend for the Retexture Engine section. A NEW, parallel controller — it does
// NOT touch ItemsController. Retexture LOGIC stays in C# (PaletteSwapService);
// this controller only exposes the seeded theory + tier + VALUE knobs to the new
// UI (retextureengine.js). Item browse, the batch queue, viewer-preview and
// commit reuse the proven /Items/ routes for now.
//
// Endpoints:
//   GET /RetextureEngine           -> the section view (Index)
//   GET /RetextureEngine/Preview   -> one cell, full knobs (theory+tier+value) -> PNG url  [live tuning]
//   GET /RetextureEngine/Sheet     -> theory x tier contact sheet, value-aware -> PNG url  [survey]
//
// Value knobs (query): value=keep|invert, vSigma, vDetail, vKnee, vFloor,
// vBlend, vAlpha, vScale. Omitted knobs fall back to the spec defaults.

using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using MangosSuperUI.Services;

namespace MangosSuperUI.Controllers;

public class RetextureEngineController : Controller
{
    private readonly PaletteSwapService _palette;
    private readonly RetextureSupport _support;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<RetextureEngineController> _logger;

    public RetextureEngineController(
        PaletteSwapService palette, RetextureSupport support,
        IWebHostEnvironment env, ILogger<RetextureEngineController> logger)
    {
        _palette = palette;
        _support = support;
        _env = env;
        _logger = logger;
    }

    // The section page. View lives at Views/RetextureEngine/Index.cshtml (next).
    public IActionResult Index() => View();

    private const string CacheDir = "retexture_engine";

    // Build ValueSettings from query knobs. mode=keep|invert; rest default to spec.
    private static ValueSettings ParseValue(
        string? mode, float? sigma, float? detail, float? knee, float? floor,
        float? blend, int? alpha, bool? scale) =>
        string.Equals(mode, "invert", StringComparison.OrdinalIgnoreCase)
            ? ValueSettings.Invert(
                sigma ?? 2.5f, detail ?? 1.0f, knee ?? 0.40f, floor ?? 0.25f,
                blend ?? 1.0f, alpha ?? 16, scale ?? true)
            : ValueSettings.Keep;

    /// <summary>
    /// GET /RetextureEngine/Preview?displayId=&theory=&tier=&ladder=&value=&vSigma=...
    /// One recolored cell with the full knob set — the live-tuning workhorse the
    /// JS calls on each knob change. Same pixels the queue would commit at these
    /// settings. Returns { success, url } (PNG under wwwroot, cache-busted).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Preview(
        uint displayId, string theory = "fan", string tier = "improved", bool ladder = false,
        string? value = null, float? vSigma = null, float? vDetail = null,
        float? vKnee = null, float? vFloor = null, float? vBlend = null,
        int? vAlpha = null, bool? vScale = null)
    {
        var ct = HttpContext.RequestAborted;
        var (srcPng, err) = await _support.ResolvePrimarySourceAsync(displayId, ct);
        if (srcPng == null) return Json(new { success = false, error = err });

        if (Array.IndexOf(PaletteSwapService.RecolorTheories, theory) < 0) theory = "fan";
        var (kd, ku, m, pop) = RetextureSupport.TierShape(tier);
        var (budget, leash) = RetextureSupport.TierPolicy(tier);
        int seed = ladder ? RetextureSupport.SeedFor((int)displayId, "")
                          : RetextureSupport.SeedFor((int)displayId, tier);
        var vset = ParseValue(value, vSigma, vDetail, vKnee, vFloor, vBlend, vAlpha, vScale);

        var outDir = Path.Combine(_env.WebRootPath, "item_textures_cache", CacheDir);
        Directory.CreateDirectory(outDir);
        string vTag = vset.IsInvert ? "inv" : "keep";
        string file = $"prev_{displayId}_{theory}_{tier}_{(ladder ? "L" : "R")}_{vTag}.png";
        string outPng = Path.Combine(outDir, file);

        var ok = await _palette.RecolorSeededAsync(
            srcPng, outPng, seed, 1.0f, 0.0f, false, ct,
            theory, kd, ku, m, pop, budget, leash, vset);
        if (ok == null) return Json(new { success = false, error = "recolor failed" });

        return Json(new
        {
            success = true,
            url = $"/item_textures_cache/{CacheDir}/{file}?t={DateTime.UtcNow.Ticks}",
            theory,
            tier,
            ladder,
            value = vTag,
        });
    }

    /// <summary>
    /// GET /RetextureEngine/Sheet?displayId=&cell=&ladder=&value=&vSigma=...
    /// The theory x tier contact sheet, value-aware. Same layout as the proven
    /// /Items/TheorySheet, but every cell honours the value knobs so you can
    /// survey theories under keep vs invert in one PNG.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Sheet(
        uint displayId, int cell = 128, bool ladder = false,
        string? value = null, float? vSigma = null, float? vDetail = null,
        float? vKnee = null, float? vFloor = null, float? vBlend = null,
        int? vAlpha = null, bool? vScale = null)
    {
        var ct = HttpContext.RequestAborted;
        var (srcPng, err) = await _support.ResolvePrimarySourceAsync(displayId, ct);
        if (srcPng == null) return Json(new { success = false, error = err });

        var vset = ParseValue(value, vSigma, vDetail, vKnee, vFloor, vBlend, vAlpha, vScale);
        string[] tiers = { "improved", "power", "glory", "gods" };
        var theories = PaletteSwapService.RecolorTheories;

        var outDir = Path.Combine(_env.WebRootPath, "item_textures_cache", CacheDir);
        Directory.CreateDirectory(outDir);

        int cols = tiers.Length + 1;               // +1 for the original
        int rows = theories.Length;
        int label = 84, pad = 6, header = 44;
        int W = label + cols * (cell + pad) + pad;
        int H = header + rows * (cell + pad) + pad;

        using var sheet = new SkiaSharp.SKBitmap(W, H);
        using var canvas = new SkiaSharp.SKCanvas(sheet);
        canvas.Clear(new SkiaSharp.SKColor(24, 24, 28));
        using var text = new SkiaSharp.SKPaint
        { Color = SkiaSharp.SKColors.White, TextSize = 13, IsAntialias = true };

        var fams = _palette.DetectFamilies(srcPng);
        var chromaticFams = fams.Where(f => f.Family != "white" && f.Family != "black").ToList();
        string famLine = $"families: {string.Join(", ", fams.Select(f => $"{f.Family} {f.Percent:F0}%"))}";
        if (chromaticFams.Count <= 1)
            famLine += "   [SINGLE CHROMATIC FAMILY - theories degenerate; judge on a multi-family item]";
        string vLine = vset.IsInvert
            ? $"   value: INVERT (sigma {(vSigma ?? 2.5f):0.0}, detail {(vDetail ?? 1.0f):0.0})"
            : "   value: keep";
        canvas.DrawText(famLine + vLine, label + pad + (cell + pad), 14, text);

        canvas.DrawText("original", label + pad, header - 8, text);
        for (int t = 0; t < tiers.Length; t++)
            canvas.DrawText(tiers[t], label + pad + (t + 1) * (cell + pad), header - 8, text);

        using (var orig = SkiaSharp.SKBitmap.Decode(srcPng))
        {
            for (int r = 0; r < rows; r++)
            {
                string theory = theories[r];
                canvas.DrawText(theory, pad, header + r * (cell + pad) + cell / 2, text);

                var oRect = SkiaSharp.SKRect.Create(label + pad, header + r * (cell + pad), cell, cell);
                if (orig != null) canvas.DrawBitmap(orig, oRect);

                for (int t = 0; t < tiers.Length; t++)
                {
                    var (kd, ku, m, pop) = RetextureSupport.TierShape(tiers[t]);
                    var (budget, leash) = RetextureSupport.TierPolicy(tiers[t]);
                    int seed = ladder ? RetextureSupport.SeedFor((int)displayId, "")
                                      : RetextureSupport.SeedFor((int)displayId, tiers[t]);
                    string vTag = vset.IsInvert ? "inv" : "keep";
                    string cellPng = Path.Combine(outDir,
                        $"cell_{displayId}_{theory}_{tiers[t]}_{(ladder ? "L" : "R")}_{vTag}.png");

                    var ok = await _palette.RecolorSeededAsync(
                        srcPng, cellPng, seed, 1.0f, 0.0f, false, ct,
                        theory, kd, ku, m, pop, budget, leash, vset);
                    if (ok == null) continue;

                    using var bmp = SkiaSharp.SKBitmap.Decode(cellPng);
                    if (bmp == null) continue;
                    var rect = SkiaSharp.SKRect.Create(
                        label + pad + (t + 1) * (cell + pad),
                        header + r * (cell + pad), cell, cell);
                    canvas.DrawBitmap(bmp, rect);
                }
            }
        }

        string vSuffix = vset.IsInvert ? "_inv" : "";
        string sheetFile = $"sheet_{displayId}{(ladder ? "_ladder" : "")}{vSuffix}.png";
        string sheetPath = Path.Combine(outDir, sheetFile);
        using (var fs = System.IO.File.Create(sheetPath))
            sheet.Encode(fs, SkiaSharp.SKEncodedImageFormat.Png, 95);

        return Json(new
        {
            success = true,
            url = $"/item_textures_cache/{CacheDir}/{sheetFile}?t={DateTime.UtcNow.Ticks}",
            chromaticFamilies = chromaticFams.Count,
            theories,
            value = vset.IsInvert ? "invert" : "keep",
            note = "rows = theories, columns = original + tiers; same seeds the queue would use",
        });
    }

    /// <summary>
    /// GET /RetextureEngine/PreviewOnModel?displayId=&theory=&tier=&ladder=&value=...
    /// Produces the RETEXTURED assets for the 3D viewer: recolored atlas slots
    /// (painted armor) OR a recolored + baked GLB (weapon/model). The JS feeds
    /// these to equip.equipBodyAtlasRetextureDirect / equipWeaponGlbDirect.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> PreviewOnModel(
        uint displayId, string theory = "fan", string tier = "improved", bool ladder = false,
        string? value = null, float? vSigma = null, float? vDetail = null,
        float? vKnee = null, float? vFloor = null, float? vBlend = null,
        int? vAlpha = null, bool? vScale = null)
    {
        var ct = HttpContext.RequestAborted;
        if (Array.IndexOf(PaletteSwapService.RecolorTheories, theory) < 0) theory = "fan";
        var shape = RetextureSupport.TierShape(tier);
        var policy = RetextureSupport.TierPolicy(tier);
        int seed = ladder ? RetextureSupport.SeedFor((int)displayId, "")
                          : RetextureSupport.SeedFor((int)displayId, tier);
        var vset = ParseValue(value, vSigma, vDetail, vKnee, vFloor, vBlend, vAlpha, vScale);
        string vTag = vset.IsInvert ? "invert" : "keep";

        // Painted armor: recolor every atlas slot with one seed (coherent piece).
        var slots = await _support.RecolorAtlasSlotsAsync(
            displayId, seed, theory, shape, policy, vset, "retexture_engine_model", ct);
        if (slots != null)
            return Json(new { success = true, kind = "atlas", slotUrls = slots, value = vTag });

        // Weapon / model item: recolor the DBC texture and bake a preview GLB.
        var glbUrl = await _support.RecolorModelGlbAsync(
            displayId, seed, theory, shape, policy, vset, "retexture_engine_model", ct);
        if (glbUrl != null)
            return Json(new { success = true, kind = "weapon", glbUrl, value = vTag });

        return Json(new { success = false, error = "no atlas slots and no recolorable model texture for this display" });
    }

    /// <summary>POST /RetextureEngine/ProcessQueue — drain the lootifier retexture
    /// queue under the CHOSEN theory + value. Body: { max, theory, value, vSigma... }.</summary>
    [HttpPost]
    public async Task<IActionResult> ProcessQueue([FromBody] JsonElement body)
    {
        int max = body.ValueKind == JsonValueKind.Object
                  && body.TryGetProperty("max", out var m) && m.TryGetInt32(out var mv) ? Math.Clamp(mv, 1, 25) : 3;
        var res = await _support.ProcessQueueAsync(TheoryFromBody(body), ValueFromBody(body), max, HttpContext.RequestAborted);
        return Json(res);
    }

    /// <summary>POST /RetextureEngine/RetextureSelection — retexture an ad-hoc
    /// selection (single or many) at the chosen theory + value, one tier or all,
    /// asSet=true for a shared colourway. Body: { items:[entry...], tiers:[...], theory, value, asSet }.</summary>
    [HttpPost]
    public async Task<IActionResult> RetextureSelection([FromBody] JsonElement body)
    {
        var items = new List<int>();
        if (body.ValueKind == JsonValueKind.Object
            && body.TryGetProperty("items", out var itEl) && itEl.ValueKind == JsonValueKind.Array)
            foreach (var e in itEl.EnumerateArray())
                if (e.TryGetInt32(out var v)) items.Add(v);

        var tiers = new List<string>();
        if (body.ValueKind == JsonValueKind.Object
            && body.TryGetProperty("tiers", out var tEl) && tEl.ValueKind == JsonValueKind.Array)
            foreach (var t in tEl.EnumerateArray())
            {
                var s = t.GetString();
                if (!string.IsNullOrWhiteSpace(s)) tiers.Add(s!);
            }

        bool asSet = body.ValueKind == JsonValueKind.Object
                     && body.TryGetProperty("asSet", out var a) && a.ValueKind == JsonValueKind.True;

        var res = await _support.RetextureSelectionAsync(
            items, tiers, TheoryFromBody(body), ValueFromBody(body), asSet, HttpContext.RequestAborted);
        return Json(res);
    }

    private static string TheoryFromBody(JsonElement body)
    {
        string theory = body.ValueKind == JsonValueKind.Object
                        && body.TryGetProperty("theory", out var th) && th.ValueKind == JsonValueKind.String
            ? (th.GetString() ?? "") : "";
        return Array.IndexOf(PaletteSwapService.RecolorTheories, theory) >= 0 ? theory : "fan";
    }

    private static ValueSettings ValueFromBody(JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object) return ValueSettings.Keep;
        string mode = body.TryGetProperty("value", out var vm) && vm.ValueKind == JsonValueKind.String ? (vm.GetString() ?? "") : "";
        if (!string.Equals(mode, "invert", StringComparison.OrdinalIgnoreCase)) return ValueSettings.Keep;
        float F(string k, float d) => body.TryGetProperty(k, out var e) && e.TryGetSingle(out var f) ? f : d;
        int I(string k, int d) => body.TryGetProperty(k, out var e) && e.TryGetInt32(out var i) ? i : d;
        bool B(string k, bool d) => body.TryGetProperty(k, out var e)
            ? (e.ValueKind == JsonValueKind.True ? true : e.ValueKind == JsonValueKind.False ? false : d) : d;
        return ValueSettings.Invert(F("vSigma", 2.5f), F("vDetail", 1.0f), F("vKnee", 0.40f), F("vFloor", 0.25f),
            F("vBlend", 1.0f), I("vAlpha", 16), B("vScale", true));
    }

    /// <summary>GET /RetextureEngine/GeneratedEntries — ids the UI hides so browse lists bases.</summary>
    [HttpGet]
    public async Task<IActionResult> GeneratedEntries()
        => Json(new { success = true, entries = await _support.GeneratedEntriesAsync() });

    /// <summary>GET /RetextureEngine/BaseVariants?entry= — a base's tier lineup for the strip.</summary>
    [HttpGet]
    public async Task<IActionResult> BaseVariants(int entry)
        => Json(await _support.BaseVariantsAsync(entry));

    /// <summary>GET /RetextureEngine/ItemSources?entry= — creature drops / vendors / quest rewards.</summary>
    [HttpGet]
    public async Task<IActionResult> ItemSources(int entry)
        => Json(await _support.ItemSourcesAsync(entry));

    /// <summary>GET /RetextureEngine/SourceTexture?displayId= — the display's current (committed) texture, no recolor.</summary>
    [HttpGet]
    public async Task<IActionResult> SourceTexture(uint displayId)
        => Json(await _support.SourceTextureAsync(displayId));
}