using SkiaSharp;

namespace MangosSuperUI.Services;

/// <summary>
/// ComfyUI model-upscale helper with two jobs that share one workflow:
///
///   1. CleanupAsync  — post-render polish for the AI/segmented modes. Runs a
///      PNG through a 4x ComfyUI upscaler (4x-PBRify_UpscalerV4 by default) then
///      downscales BACK to the source dims via Lanczos/mipmap. Net resolution
///      change is zero; the round-trip just launders DXT compression bleed.
///      (Variations no longer uses this — its recolor is luminance-preserving
///      and already sharp, so the round-trip was a pure no-op there.)
///
///   2. UpscaleSourceAsync — genuine super-resolution for the recolor SOURCE.
///      Runs the same 4x model but downscales only to an INTEGER MULTIPLE of the
///      source (2x by default), KEEPING the enhanced detail. Recolor the result
///      and you get a sharp 2x texture without hand-painting — the model invents
///      plausible high-frequency detail on the real hand-painted source, and the
///      chroma-only recolor rides on top of it. Run once per source, cache, then
///      recolor many variants from it.
///
/// Both fail SOFT: any error (model missing, ComfyUI offline, timeout) returns
/// the ORIGINAL input path unchanged, so the pipeline degrades cleanly to the
/// native-resolution behavior and never breaks.
///
/// Config:
///   "SpellCreator:ComfyUI:UpscalerModel"        : "4x-PBRify_UpscalerV4.pth"
///       (empty / unset = disabled, pass-through mode)
///   "SpellCreator:Recolor:ResolutionMultiplier" : 2
///       (source super-res factor; 1 = off, clamped to 1..4)
///
/// Reading model:
///   PBRify V4: https://openmodeldb.info/models/4x-PBRify-UpscalerV4
///   - DAT2 architecture, trained on 2000s-era game textures
///   - Compression Removal + General Upscaler + Restoration, CC0 licensed
///   - Download → ComfyUI/models/upscale_models/4x-PBRify_UpscalerV4.pth
/// </summary>
public class ComfyUIUpscaler
{
    private readonly ILogger<ComfyUIUpscaler> _logger;
    private readonly ComfyUIDispatcher _comfy;
    private readonly IWebHostEnvironment _env;
    private readonly string _modelName;
    private readonly int _sourceMultiplier;
    private readonly int _chromaDenoiseRadius;

    public ComfyUIUpscaler(IConfiguration config, ComfyUIDispatcher comfy,
        IWebHostEnvironment env, ILogger<ComfyUIUpscaler> logger)
    {
        _logger = logger;
        _comfy = comfy;
        _env = env;
        _modelName = config["SpellCreator:ComfyUI:UpscalerModel"] ?? "";

        _sourceMultiplier =
            int.TryParse(config["SpellCreator:Recolor:ResolutionMultiplier"], out var m)
                ? Math.Clamp(m, 1, 4)
                : 2;

        // Radius (px) of the chroma-only denoise applied to a super-res SOURCE.
        // The 4x model invents high-frequency detail that includes chroma jitter;
        // the luminance-preserving recolor then amplifies that jitter into vivid
        // speckles for high-saturation targets. Smoothing the source CHROMA while
        // keeping luminance fully sharp removes the speckles for every palette at
        // no cost to edge sharpness. 0 = off. Default 2.
        _chromaDenoiseRadius =
            int.TryParse(config["SpellCreator:Recolor:ChromaDenoiseRadius"], out var cd)
                ? Math.Clamp(cd, 0, 8)
                : 2;

        if (string.IsNullOrEmpty(_modelName))
            _logger.LogInformation(
                "ComfyUIUpscaler: No upscaler model configured " +
                "(SpellCreator:ComfyUI:UpscalerModel) — upscale disabled.");
        else
            _logger.LogInformation(
                "ComfyUIUpscaler: Enabled with model '{Model}', source multiplier {Mult}x",
                _modelName, _sourceMultiplier);
    }

    /// <summary>True if a model is configured. Cheap, sync. Use to gate UI hints.</summary>
    public bool IsEnabled => !string.IsNullOrEmpty(_modelName);

    /// <summary>Configured source super-res factor (1 = off). Used by callers to
    /// build cache keys and to decide whether to bother calling.</summary>
    public int SourceMultiplier => _sourceMultiplier;

    /// <summary>Chroma-denoise radius applied to super-res sources (0 = off).
    /// Exposed so callers fold it into cache keys — changing it busts the cache.</summary>
    public int ChromaDenoiseRadius => _chromaDenoiseRadius;

    /// <summary>
    /// Cleanup round-trip: upscale 4x then DOWNSCALE BACK to source dims. Net
    /// resolution unchanged. Returns the cleaned PNG path (new file) or the
    /// original input path on any failure / when disabled. Never throws.
    /// </summary>
    public async Task<string> CleanupAsync(string inputPngPath, string label,
        CancellationToken ct = default)
    {
        if (!IsEnabled) return inputPngPath;

        var (srcW, srcH) = ProbeDims(inputPngPath);
        if (srcW <= 0) return inputPngPath;

        return await RunUpscaleResizeAsync(
            inputPngPath, label, srcW, srcH, "upscale_cleaned", "clean", chromaDenoise: false, ct);
    }

    /// <summary>
    /// Genuine source super-res: upscale 4x then downscale to MULTIPLIER × source
    /// (keeping the extra resolution + invented detail). Returns the upscaled PNG
    /// path (new file), or the original input path on failure / when the model is
    /// disabled / when multiplier is 1. Never throws. Intended to be cached by
    /// the caller (run once per source, recolor many variants from the result).
    /// </summary>
    public async Task<string> UpscaleSourceAsync(string inputPngPath, string label,
        CancellationToken ct = default)
    {
        if (!IsEnabled || _sourceMultiplier <= 1) return inputPngPath;

        var (srcW, srcH) = ProbeDims(inputPngPath);
        if (srcW <= 0) return inputPngPath;

        int outW = srcW * _sourceMultiplier;
        int outH = srcH * _sourceMultiplier;

        return await RunUpscaleResizeAsync(
            inputPngPath, label, outW, outH, "source_upscaled_raw", "src", chromaDenoise: true, ct);
    }

    /// <summary>Decode just the dimensions of a PNG. Returns (0,0) on failure.</summary>
    private (int W, int H) ProbeDims(string path)
    {
        if (!File.Exists(path))
        {
            _logger.LogWarning("ComfyUIUpscaler: input PNG not found — {Path}", path);
            return (0, 0);
        }
        try
        {
            using var probe = SKBitmap.Decode(path);
            if (probe == null) { _logger.LogWarning("ComfyUIUpscaler: couldn't decode input PNG"); return (0, 0); }
            return (probe.Width, probe.Height);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ComfyUIUpscaler: couldn't probe input PNG");
            return (0, 0);
        }
    }

    /// <summary>
    /// Shared core: upload → 4x model upscale → resize to (outW,outH) → save.
    /// Returns the new PNG path, or the original input path on any failure.
    /// The resize uses Linear+mipmap, which behaves like a proper area filter on
    /// the integer-ratio reductions we do here (4x→1x for cleanup, 4x→2x for
    /// source) and avoids the aliasing cubic resamplers leave on downscales.
    /// </summary>
    private async Task<string> RunUpscaleResizeAsync(
        string inputPngPath, string label, int outW, int outH,
        string outSubdir, string filePrefix, bool chromaDenoise, CancellationToken ct)
    {
        try
        {
            if (!await _comfy.IsAnyNodeOnlineAsync(ct))
            {
                _logger.LogInformation("ComfyUIUpscaler: no ComfyUI nodes online, skipping");
                return inputPngPath;
            }

            string? uploadedName = await _comfy.UploadImageFileAsync(inputPngPath, ct);
            if (uploadedName == null)
            {
                _logger.LogWarning("ComfyUIUpscaler: upload failed for '{Label}', skipping", label);
                return inputPngPath;
            }

            var workflow = BuildUpscaleWorkflow(uploadedName, label);
            var rawDir = Path.Combine(_env.WebRootPath, "item_textures_cache", "upscale_raw");
            Directory.CreateDirectory(rawDir);

            string? upscaledPng = await _comfy.GenerateAsync(
                workflow, $"upscale_{label}", rawDir, ct);
            if (upscaledPng == null)
            {
                _logger.LogWarning("ComfyUIUpscaler: upscale failed for '{Label}', returning original", label);
                return inputPngPath;
            }

            var outDir = Path.Combine(_env.WebRootPath, "item_textures_cache", outSubdir);
            Directory.CreateDirectory(outDir);
            string outPng = Path.Combine(outDir, $"{filePrefix}_{label}_{Guid.NewGuid():N}.png");

            SKBitmap? final = null;
            try
            {
                using var upscaledBmp = SKBitmap.Decode(upscaledPng);
                if (upscaledBmp == null)
                {
                    _logger.LogWarning("ComfyUIUpscaler: couldn't decode upscaled PNG, returning original");
                    return inputPngPath;
                }

                // Land at the requested dimensions (model scale may differ from
                // the multiplier; for cleanup outW/outH == source).
                if (upscaledBmp.Width == outW && upscaledBmp.Height == outH)
                {
                    final = upscaledBmp.Copy();
                }
                else
                {
                    final = upscaledBmp.Resize(
                        new SKImageInfo(outW, outH, SKColorType.Rgba8888, SKAlphaType.Unpremul),
                        new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
                }
                if (final == null)
                {
                    _logger.LogWarning("ComfyUIUpscaler: resize failed, returning original");
                    return inputPngPath;
                }

                // Chroma-only denoise (source path): smooth Cb/Cr, keep luma. The
                // recolor keys entirely off source chroma and preserves luminance,
                // so this kills the per-palette edge speckles without softening
                // any real (luminance) detail.
                if (chromaDenoise && _chromaDenoiseRadius > 0)
                    ChromaDenoiseKeepLuma(final, _chromaDenoiseRadius);

                using var outStream = File.Create(outPng);
                final.Encode(outStream, SKEncodedImageFormat.Png, 100);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ComfyUIUpscaler: resize/save step failed, returning original");
                return inputPngPath;
            }
            finally
            {
                final?.Dispose();
                // Drop the 4x intermediate — huge and useless once resized.
                try { File.Delete(upscaledPng); } catch { /* best-effort */ }
            }

            _logger.LogInformation(
                "ComfyUIUpscaler: '{Label}' upscaled → {W}×{H}{Cd}", label, outW, outH,
                (chromaDenoise && _chromaDenoiseRadius > 0) ? $" (chroma denoise r{_chromaDenoiseRadius})" : "");
            return outPng;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "ComfyUIUpscaler: unexpected error for '{Label}', returning original", label);
            return inputPngPath;
        }
    }

    /// <summary>
    /// Smooth a bitmap's CHROMA (Cb/Cr) while leaving LUMA (Y) untouched. Edges
    /// and detail live in luma, so this removes color speckles/jitter without any
    /// loss of sharpness — exactly what the luminance-preserving recolor needs as
    /// input. BT.601 YCbCr; separable box blur on the two chroma planes.
    /// </summary>
    private static void ChromaDenoiseKeepLuma(SKBitmap bmp, int radius)
    {
        if (radius < 1) return;
        int w = bmp.Width, h = bmp.Height, n = w * h;
        if (n == 0) return;

        var px = bmp.Pixels;            // SKColor[] (channel-order independent)
        var Y = new float[n];
        var Cb = new float[n];
        var Cr = new float[n];
        var alpha = new byte[n];

        for (int i = 0; i < n; i++)
        {
            var c = px[i];
            float r = c.Red, g = c.Green, b = c.Blue;
            alpha[i] = c.Alpha;
            Y[i] = 0.299f * r + 0.587f * g + 0.114f * b;
            Cb[i] = -0.168736f * r - 0.331264f * g + 0.5f * b + 128f;
            Cr[i] = 0.5f * r - 0.418688f * g - 0.081312f * b + 128f;
        }

        BoxBlur(Cb, w, h, radius);
        BoxBlur(Cr, w, h, radius);

        for (int i = 0; i < n; i++)
        {
            float yy = Y[i], cb = Cb[i] - 128f, cr = Cr[i] - 128f;
            int r = (int)MathF.Round(yy + 1.402f * cr);
            int g = (int)MathF.Round(yy - 0.344136f * cb - 0.714136f * cr);
            int b = (int)MathF.Round(yy + 1.772f * cb);
            px[i] = new SKColor(ClampByte(r), ClampByte(g), ClampByte(b), alpha[i]);
        }

        bmp.Pixels = px;
    }

    /// <summary>Separable box blur over a single float plane, in place.</summary>
    private static void BoxBlur(float[] data, int w, int h, int radius)
    {
        if (radius < 1) return;
        var tmp = new float[data.Length];

        for (int y = 0; y < h; y++)            // horizontal pass
        {
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                float sum = 0; int cnt = 0;
                int lo = Math.Max(0, x - radius), hi = Math.Min(w - 1, x + radius);
                for (int xx = lo; xx <= hi; xx++) { sum += data[row + xx]; cnt++; }
                tmp[row + x] = sum / cnt;
            }
        }
        for (int x = 0; x < w; x++)            // vertical pass
        {
            for (int y = 0; y < h; y++)
            {
                float sum = 0; int cnt = 0;
                int lo = Math.Max(0, y - radius), hi = Math.Min(h - 1, y + radius);
                for (int yy = lo; yy <= hi; yy++) { sum += tmp[yy * w + x]; cnt++; }
                data[y * w + x] = sum / cnt;
            }
        }
    }

    private static byte ClampByte(int v) => (byte)(v < 0 ? 0 : v > 255 ? 255 : v);

    /// <summary>
    /// Minimal ComfyUI upscale workflow (all built-in nodes):
    ///   LoadImage[1] → ImageUpscaleWithModel[3] ← UpscaleModelLoader[2]
    ///   ImageUpscaleWithModel[3] → SaveImage[4]
    /// The .pth must live in ComfyUI/models/upscale_models/ on every pool node.
    /// </summary>
    private Dictionary<string, object> BuildUpscaleWorkflow(string uploadedImageName, string label)
    {
        return new Dictionary<string, object>
        {
            ["1"] = new Dictionary<string, object>
            {
                ["class_type"] = "LoadImage",
                ["inputs"] = new Dictionary<string, object> { ["image"] = uploadedImageName }
            },
            ["2"] = new Dictionary<string, object>
            {
                ["class_type"] = "UpscaleModelLoader",
                ["inputs"] = new Dictionary<string, object> { ["model_name"] = _modelName }
            },
            ["3"] = new Dictionary<string, object>
            {
                ["class_type"] = "ImageUpscaleWithModel",
                ["inputs"] = new Dictionary<string, object>
                {
                    ["upscale_model"] = new object[] { "2", 0 },
                    ["image"] = new object[] { "1", 0 }
                }
            },
            ["4"] = new Dictionary<string, object>
            {
                ["class_type"] = "SaveImage",
                ["inputs"] = new Dictionary<string, object>
                {
                    ["images"] = new object[] { "3", 0 },
                    ["filename_prefix"] = $"upscale_{label}"
                }
            }
        };
    }
}