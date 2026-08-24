// IconController.cs
//
// Serves item/spell inventory icons by reading Interface\Icons\{name}.blp
// straight from the vanilla client MPQs and decoding to PNG in memory.
//
// MPQ-ONLY BY DESIGN — there is deliberately NO disk fallback. Icons are never
// read from wwwroot/icons/. This uses a distinct route (/Icon/Get) precisely so
// UseStaticFiles cannot shadow it: there is no wwwroot/Icon/ directory, so every
// request reaches this controller and actually exercises the MPQ path. On any
// miss (BLP absent from every archive, or a decode failure) it returns 404 so the
// failure is VISIBLE rather than masked by a stale on-disk PNG. That is the whole
// point — a silent disk/questionmark fallback makes the MPQ path untestable.
//
// Reached via conventional {controller}/{action} routing at:
//   GET /Icon/Get?name=INV_Sword_04
// The DBC-miss questionmark is itself just another name served from the MPQ
// (/Icon/Get?name=inv_misc_questionmark), so even the fallback stays MPQ-sourced.
//
// Decode + PNG encode live in BlpDecoder.ToPngBytes, shared with the /Home/Diagnose
// icon check so the diagnostic exercises this exact pipeline rather than a copy of
// it. BlpDecoder handles both palettized (comp=1) and DXT (comp=2) BLP2, which is
// every format vanilla icons use — so decode is not a limiting factor here.

using System.Collections.Concurrent;
using Microsoft.AspNetCore.Mvc;
using MangosSuperUI.Services;

namespace MangosSuperUI.Controllers;

public class IconController : Controller
{
    private readonly MpqReaderService _mpq;
    private readonly ILogger<IconController> _logger;

    // Decoded-PNG cache, keyed by lowercased icon name. SUCCESSES ONLY: a miss is
    // never cached, so the cache can never mask an MPQ read that would later fail
    // (or succeed). The source bytes are immutable game assets, so a cached hit is
    // always the same thing the MPQ would return.
    private static readonly ConcurrentDictionary<string, byte[]> _pngCache = new();

    public IconController(MpqReaderService mpq, ILogger<IconController> logger)
    {
        _mpq = mpq;
        _logger = logger;
    }

    // GET /Icon/Get?name=INV_Sword_04
    [HttpGet]
    public IActionResult Get(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return NotFound();

        // Sanitize to a bare filename stem. Drop any directory a caller included,
        // strip a trailing .blp/.png, then allow only icon-name characters. This
        // blocks path traversal and anything that is not a plain icon stem.
        name = name.Replace('/', '\\');
        int slash = name.LastIndexOf('\\');
        if (slash >= 0) name = name[(slash + 1)..];

        if (name.EndsWith(".blp", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];

        if (name.Length == 0) return NotFound();
        foreach (var c in name)
            if (!(char.IsLetterOrDigit(c) || c == '_' || c == '-'))
                return NotFound();

        var key = name.ToLowerInvariant();

        if (_pngCache.TryGetValue(key, out var hit))
            return Png(hit);

        var mpqPath = $"Interface\\Icons\\{name}.blp";

        byte[]? blp;
        try
        {
            blp = _mpq.ExtractFile(mpqPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Icon: ExtractFile threw for {Path}", mpqPath);
            return NotFound();
        }

        if (blp == null || blp.Length == 0)
        {
            _logger.LogDebug("Icon: not in MPQ: {Path}", mpqPath);
            return NotFound();
        }

        byte[] png;
        try
        {
            png = BlpDecoder.ToPngBytes(blp);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Icon: BLP decode failed for {Path}", mpqPath);
            return NotFound();
        }

        if (png.Length == 0) return NotFound();

        _pngCache[key] = png;
        return Png(png);
    }

    private FileContentResult Png(byte[] bytes)
    {
        // no-store during bring-up: a browser reload always re-hits this endpoint
        // (server still serves from _pngCache) so a stale browser image can never
        // hide a change. Swap to a long immutable cache once verified.
        Response.Headers["Cache-Control"] = "no-store";
        return File(bytes, "image/png");
    }
}
