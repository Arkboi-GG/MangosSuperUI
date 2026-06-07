using SkiaSharp;

namespace MangosSuperUI.Services;

/// <summary>
/// REGION-AWARE texture segmentation — an alternative to the global
/// family-swap in PaletteSwapService.
///
/// THE PROBLEM IT SOLVES
/// ─────────────────────
/// PaletteSwapService buckets every pixel into one of ~8 broad HSL families
/// and gives each family ONE target color. That fuses materials that share a
/// family but are visually distinct — on Ironfoe the pale-straw handle, the
/// amber ring, and the deep-gold emblem all fall in "gold/yellow" and collapse
/// to one flat copper, because the swap replaces saturation (the very channel
/// that separated them). It also can't separate two regions that ARE the same
/// color but should recolor differently.
///
/// THE APPROACH
/// ────────────
/// Treat the texture spatially, the way a "magic wand" does:
///   1. QUANTIZE   — posterize into a few HSL bins. Collapses gradients (and
///                   their slivers) into a handful of bands.
///   2. LABEL      — connected-components (flood fill) on quantized pixels.
///                   Gives raw blobs: ring, handle, each frame patch, etc.
///   3. MERGE      — fuse blobs whose MEAN COLOR is near each other regardless
///                   of position (rejoins UV-island fragments of one material),
///                   but keep color-distinct neighbours apart even when they
///                   touch (ring stays separate from handle).
///   4. DESCRIBE   — give every surviving unit a human color name from its mean
///                   HSL, so the LLM can target units independently
///                   ("pale straw → bright copper, amber → dark copper").
///   5. APPLY      — recolor via the precomputed pixel→unit label map, NOT via
///                   family predicates. No first-match-wins boundary collisions.
///
/// This is intentionally a SEPARATE service. PaletteSwapService and the
/// AI-retexture / precision-recolor paths are untouched; variation mode opts
/// into this when region separation matters.
/// </summary>
public class TextureSegmentationService
{
    private readonly ILogger<TextureSegmentationService> _logger;

    public TextureSegmentationService(ILogger<TextureSegmentationService> logger)
    {
        _logger = logger;
    }

    public bool IsAvailable => true;

    // ── Tunables. Exposed on the options object so the UI / caller can sweep
    //    them per-texture without recompiling. Defaults chosen for vanilla WoW
    //    hand-painted item atlases (flat-ish regions, hard-ish edges). ──
    public class SegmentOptions
    {
        /// <summary>Hue quantization step in degrees (smaller = more bins).</summary>
        public float HueBinDegrees { get; set; } = 40f;
        /// <summary>Saturation quantization step (0-1).</summary>
        public float SatBin { get; set; } = 0.18f;
        /// <summary>Lightness quantization step (0-1).</summary>
        public float LightBin { get; set; } = 0.16f;

        /// <summary>
        /// Color distance under which a SMALLER unit is absorbed into its nearest
        /// LARGER neighbour. The merge is one-directional and large units are
        /// sinks (never fused), so this can't transitively collapse the whole
        /// texture into one blob. Validated at 0.10 on vanilla weapon atlases;
        /// raising it fuses adjacent materials (e.g. steel+grey), lowering it
        /// leaves more small units for the MinUnitFraction pass to clean up.
        /// </summary>
        public float MergeDistance { get; set; } = 0.10f;

        /// <summary>
        /// Minimum unit size as a fraction of opaque pixels. Units smaller than
        /// this are absorbed into their nearest-color neighbour (kills slivers).
        /// </summary>
        public float MinUnitFraction { get; set; } = 0.015f;

        /// <summary>Hard cap on number of units returned (largest kept).</summary>
        public int MaxUnits { get; set; } = 10;

        /// <summary>Alpha below this is treated as transparent and ignored.</summary>
        public byte AlphaThreshold { get; set; } = 16;
    }

    // ═══════════════════════════════════════════════════════════════════
    // PUBLIC API
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Segment a source PNG into labeled color regions. Returns the unit list
    /// (each with mean color + descriptor + pixel count) and the per-pixel
    /// label map needed to recolor. Returns null on decode failure.
    /// </summary>
    public SegmentationResult? Segment(string sourcePngPath, SegmentOptions? opts = null)
    {
        opts ??= new SegmentOptions();
        if (!File.Exists(sourcePngPath))
        {
            _logger.LogWarning("Segmentation: source PNG not found: {Path}", sourcePngPath);
            return null;
        }

        using var bmp = SKBitmap.Decode(sourcePngPath);
        if (bmp == null)
        {
            _logger.LogWarning("Segmentation: failed to decode {Path}", sourcePngPath);
            return null;
        }

        int w = bmp.Width, h = bmp.Height;
        int n = w * h;

        // Cache HSL + alpha per pixel once.
        var hsl = new (float H, float S, float L)[n];
        var alpha = new byte[n];
        // Quantized bin key per pixel (-1 = transparent).
        var qkey = new int[n];
        int opaqueCount = 0;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                var px = bmp.GetPixel(x, y);
                alpha[i] = px.Alpha;
                if (px.Alpha < opts.AlphaThreshold)
                {
                    qkey[i] = -1;
                    continue;
                }
                RgbToHsl(px.Red, px.Green, px.Blue, out float hh, out float ss, out float ll);
                hsl[i] = (hh, ss, ll);
                qkey[i] = QuantKey(hh, ss, ll, opts);
                opaqueCount++;
            }
        }

        if (opaqueCount == 0)
        {
            _logger.LogInformation("Segmentation: texture fully transparent — nothing to segment");
            return null;
        }

        // ── STEP 2: connected-components (4-neighbour flood fill) on qkey ──
        var label = new int[n];
        for (int i = 0; i < n; i++) label[i] = -1;
        int nextLabel = 0;
        var stack = new Stack<int>();

        for (int start = 0; start < n; start++)
        {
            if (qkey[start] < 0 || label[start] != -1) continue;
            int key = qkey[start];
            int lab = nextLabel++;
            stack.Push(start);
            label[start] = lab;

            while (stack.Count > 0)
            {
                int p = stack.Pop();
                int px = p % w, py = p / w;
                // 4-neighbourhood
                if (px > 0) TryGrow(p - 1, key, qkey, label, lab, stack);
                if (px < w - 1) TryGrow(p + 1, key, qkey, label, lab, stack);
                if (py > 0) TryGrow(p - w, key, qkey, label, lab, stack);
                if (py < h - 1) TryGrow(p + w, key, qkey, label, lab, stack);
            }
        }

        // Aggregate raw blobs: count + summed HSL (circular mean for hue).
        var blobs = new List<Unit>();
        for (int i = 0; i < nextLabel; i++) blobs.Add(new Unit { Id = i });
        for (int i = 0; i < n; i++)
        {
            int lab = label[i];
            if (lab < 0) continue;
            var b = blobs[lab];
            b.PixelCount++;
            b.SumS += hsl[i].S;
            b.SumL += hsl[i].L;
            // hue as unit vector for proper circular mean
            float rad = hsl[i].H * MathF.PI / 180f;
            b.SumHx += MathF.Cos(rad);
            b.SumHy += MathF.Sin(rad);
        }
        foreach (var b in blobs) b.ComputeMeans();
        blobs.RemoveAll(b => b.PixelCount == 0);

        _logger.LogInformation("Segmentation: {Raw} raw blobs from {Px} opaque px ({W}x{H})",
            blobs.Count, opaqueCount, w, h);

        // ── STEP 3: spatial-color-merge (de-chained, one-directional) ──
        // The earlier union-find "merge any two within distance" transitively
        // chained the WHOLE texture into one blob on real (anti-aliased) inputs:
        // 1940+ tiny blobs densely packed in color space always had a bridge
        // from straw→amber→gold→steel, collapsing to a single 100% unit with a
        // meaningless averaged hue. Instead:
        //   - repeatedly take the SMALLEST surviving unit
        //   - absorb it into its nearest LARGER-or-equal neighbour IF within
        //     MergeDistance; otherwise it survives on its own
        //   - a unit at/above minPx is a SINK: never absorbed, never fused into
        //     another. Two large materials therefore can NEVER merge.
        // This rejoins fragments/slivers of a material into the nearest real
        // region while structurally preventing whole-texture collapse.
        int minPx = Math.Max(1, (int)(opaqueCount * opts.MinUnitFraction));

        // union map over blob indices (root = surviving unit)
        var parent = Enumerable.Range(0, blobs.Count).ToArray();
        int Find(int a) { while (parent[a] != a) { parent[a] = parent[parent[a]]; a = parent[a]; } return a; }

        bool changed = true;
        int guard = 0;
        while (changed && guard++ < 8)
        {
            changed = false;

            // current size per surviving root
            var size = new Dictionary<int, int>();
            for (int bi = 0; bi < blobs.Count; bi++)
            {
                int r = Find(bi);
                size[r] = size.GetValueOrDefault(r, 0) + blobs[bi].PixelCount;
            }

            // survivors (distinct roots), smallest first
            var survivors = new List<int>(size.Keys);
            survivors.Sort((x, y) => size[x].CompareTo(size[y]));

            foreach (var s in survivors)
            {
                if (size[s] >= minPx) continue;          // sink — keep as its own unit
                // nearest OTHER survivor by mean color
                int best = -1; float bestD = float.MaxValue;
                foreach (var j in survivors)
                {
                    if (j == s || Find(j) == Find(s)) continue;
                    float d = ColorDistance(blobs[s], blobs[j]);
                    if (d < bestD) { bestD = d; best = j; }
                }
                if (best >= 0 && bestD < opts.MergeDistance)
                {
                    parent[Find(s)] = Find(best);
                    changed = true;
                }
            }
        }

        // Collapse roots into contiguous merged unit ids and remap pixels.
        var rootToUnit = new Dictionary<int, int>();
        for (int bi = 0; bi < blobs.Count; bi++)
        {
            int r = Find(bi);
            if (!rootToUnit.ContainsKey(r)) rootToUnit[r] = rootToUnit.Count;
        }
        var blobIdToIndex = new Dictionary<int, int>();
        for (int idx = 0; idx < blobs.Count; idx++) blobIdToIndex[blobs[idx].Id] = idx;

        var pixelUnit = new int[n];
        for (int i = 0; i < n; i++)
        {
            int lab = label[i];
            if (lab < 0 || !blobIdToIndex.TryGetValue(lab, out int bidx)) { pixelUnit[i] = -1; continue; }
            pixelUnit[i] = rootToUnit[Find(bidx)];
        }

        // ── STEP 3b: recompute means FROM PIXELS (no vector approximation) ──
        // Re-aggregate H (circular), S, L directly from the source pixels for
        // each merged unit. This is the second fix for the phantom-hue bug:
        // averaging unit-vectors of per-blob means could land a multi-hue blob
        // on a color that exists nowhere (blue+gold → "green"). A real per-pixel
        // circular mean plus the coherence magnitude (hueMag) is honest.
        int unitCount = rootToUnit.Count;
        var merged = new List<Unit>(unitCount);
        for (int i = 0; i < unitCount; i++) merged.Add(new Unit { Id = i });
        for (int i = 0; i < n; i++)
        {
            int u = pixelUnit[i];
            if (u < 0) continue;
            var m = merged[u];
            m.PixelCount++;
            m.SumS += hsl[i].S;
            m.SumL += hsl[i].L;
            float rad = hsl[i].H * MathF.PI / 180f;
            m.SumHx += MathF.Cos(rad);
            m.SumHy += MathF.Sin(rad);
        }
        foreach (var m in merged) m.ComputeMeans();

        // Keep the largest MaxUnits, reindex pixel map to contiguous ids.
        merged = merged.Where(u => u.PixelCount > 0)
                       .OrderByDescending(u => u.PixelCount)
                       .Take(opts.MaxUnits)
                       .ToList();

        var idRemap = new Dictionary<int, int>();
        for (int i = 0; i < merged.Count; i++) idRemap[merged[i].Id] = i;
        for (int i = 0; i < n; i++)
        {
            if (pixelUnit[i] < 0) continue;
            pixelUnit[i] = idRemap.TryGetValue(pixelUnit[i], out int r) ? r : -1;
        }
        for (int i = 0; i < merged.Count; i++) merged[i].Id = i;

        // ── STEP 4: describe each unit ──
        foreach (var u in merged)
        {
            u.Descriptor = DescribeColor(u.MeanH, u.MeanS, u.MeanL, u.HueMagnitude);
            u.Percent = 100f * u.PixelCount / opaqueCount;
        }

        _logger.LogInformation("Segmentation: {Raw} raw blobs -> {N} units: {List}",
            blobs.Count, merged.Count,
            string.Join(", ", merged.Select(u => $"{u.Descriptor} {u.Percent:F0}%(hm{u.HueMagnitude:F2})")));

        return new SegmentationResult
        {
            Width = w,
            Height = h,
            Units = merged,
            PixelUnit = pixelUnit,
            Alpha = alpha,
        };
    }

    /// <summary>
    /// Recolor using a precomputed segmentation + a unit-id → target-color map.
    /// Lightness is preserved per pixel (keeps sculpting); H and S come from the
    /// target. Pixels in units with no assigned target are left unchanged.
    /// Writes a PNG to outputPath; returns it, or null on failure.
    ///
    /// COMPAT OVERLOAD: takes the legacy (H,S) targets and preserves lightness,
    /// exactly as before. New callers should use the UnitTarget overload below
    /// to control lightness behavior (including INVERT) per unit.
    /// </summary>
    public string? RecolorByUnits(
        string sourcePngPath, SegmentationResult seg,
        IReadOnlyDictionary<int, (float H, float S)> targets, string outputPath)
    {
        var promoted = new Dictionary<int, UnitTarget>(targets.Count);
        foreach (var (id, t) in targets)
            promoted[id] = new UnitTarget(t.H, t.S, LMode.Preserve, 0f);
        return RecolorByUnits(sourcePngPath, seg, promoted, outputPath);
    }

    /// <summary>
    /// Recolor with full per-unit LIGHTNESS control. Each target carries an H, S,
    /// and a lightness behavior:
    ///   Preserve         — outL = sourceL (the original behavior; keeps shading).
    ///   Lift(p)/Drop(p)  — outL = sourceL ± p (uniform offset, gradient intact).
    ///   Invert(p)        — outL = (1 - sourceL) ± p. FLIPS the tonal range, so a
    ///                      bright region becomes dark and vice-versa while the
    ///                      sculpting INVERTS coherently (highlights become the
    ///                      deepest shadows). Set per-unit, so one material can
    ///                      flip while others don't (white blade + obsidian gold),
    ///                      or every unit can flip together (global tonal inversion).
    /// Inversion is what the hue+sat-only swap could never do: it changes a
    /// region's VALUE identity, not just its color.
    /// </summary>
    public string? RecolorByUnits(
        string sourcePngPath, SegmentationResult seg,
        IReadOnlyDictionary<int, UnitTarget> targets, string outputPath)
    {
        try
        {
            using var src = SKBitmap.Decode(sourcePngPath);
            if (src == null) return null;
            int w = seg.Width, h = seg.Height;
            if (src.Width != w || src.Height != h)
            {
                _logger.LogWarning("Segmentation/Recolor: source dims {SW}x{SH} != seg {W}x{H}",
                    src.Width, src.Height, w, h);
                return null;
            }

            using var outBmp = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    var px = src.GetPixel(x, y);
                    int unit = seg.PixelUnit[i];
                    if (unit < 0 || !targets.TryGetValue(unit, out var t))
                    {
                        outBmp.SetPixel(x, y, px);
                        continue;
                    }
                    RgbToHsl(px.Red, px.Green, px.Blue, out _, out _, out float l);
                    float outL = t.L switch
                    {
                        LMode.Lift => Math.Clamp(l + t.Param, 0f, 1f),
                        LMode.Drop => Math.Clamp(l - t.Param, 0f, 1f),
                        LMode.Invert => Math.Clamp((1f - l) + t.Param, 0f, 1f),
                        _ => l,
                    };
                    HslToRgb(t.H, t.S, outL, out byte r, out byte g, out byte b);
                    outBmp.SetPixel(x, y, new SKColor(r, g, b, px.Alpha));
                }
            }

            using var os = File.Create(outputPath);
            outBmp.Encode(os, SKEncodedImageFormat.Png, 100);
            return outputPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Segmentation/Recolor failed");
            return null;
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // INTERNALS
    // ═══════════════════════════════════════════════════════════════════


    private static void TryGrow(int neighbor, int key, int[] qkey, int[] label, int lab, Stack<int> stack)
    {
        if (qkey[neighbor] == key && label[neighbor] == -1)
        {
            label[neighbor] = lab;
            stack.Push(neighbor);
        }
    }

    private static int QuantKey(float h, float s, float l, SegmentOptions o)
    {
        // Desaturated pixels have unstable hue — null the hue bin for them so
        // greys cluster by lightness, not by noisy hue.
        int hb = s < 0.12f ? 0 : (int)(h / o.HueBinDegrees);
        int sb = (int)(s / o.SatBin);
        int lb = (int)(l / o.LightBin);
        // pack into a single int (ranges are small)
        return (hb << 16) | (sb << 8) | lb;
    }

    /// <summary>
    /// Weighted HSL distance. The hue weight has a FLOOR (0.35) plus a
    /// saturation-scaled bonus, rather than scaling purely by saturation. The
    /// old pure-sat scaling drove hue weight toward zero on low-saturation
    /// pixels — which on vanilla weapons fused low-sat WARM (straw/steel-gold)
    /// with low-sat COOL (steel-blue), the exact materials we need to keep
    /// apart. The floor guarantees warm-vs-cool always separates; the bonus
    /// adds extra hue sensitivity where saturation makes hue more reliable.
    /// </summary>
    private static float ColorDistance(Unit a, Unit b)
    {
        float dh = HueDelta(a.MeanH, b.MeanH) / 180f;          // 0..1
        float ds = MathF.Abs(a.MeanS - b.MeanS);               // 0..1
        float dl = MathF.Abs(a.MeanL - b.MeanL);               // 0..1
        float satWeight = MathF.Min(a.MeanS, b.MeanS);
        float wh = 0.35f + 0.45f * satWeight;                  // hue weight, floored
        return MathF.Sqrt(wh * dh * dh + 0.8f * ds * ds + 0.6f * dl * dl);
    }

    private static float HueDelta(float a, float b)
    {
        float d = MathF.Abs(a - b) % 360f;
        return d > 180f ? 360f - d : d;
    }

    /// <summary>
    /// Map a mean HSL to a descriptor the LLM and PaletteSwapService's
    /// ColorDictionary both understand. Kept deliberately small and aligned
    /// with the family vocabulary so recipes stay round-trippable.
    /// </summary>
    private static string DescribeColor(float h, float s, float l, float hueMagnitude)
    {
        if (s < 0.12f)
            return l > 0.78f ? "white"
                 : l < 0.18f ? "black"
                 : l > 0.55f ? "light grey"
                 : "grey";

        // Hue-coherence guard: if the unit's hue vectors largely cancel
        // (low magnitude), its mean hue is not a real color — it spans many
        // hues. Don't claim a specific hue name (that's how blue+gold became
        // "green"). Fall back to a warm/cool-by-lightness descriptor that the
        // ColorDictionary can still resolve to something sane.
        if (hueMagnitude < 0.40f)
        {
            bool warm = h < 90f || h >= 300f;
            string tone = l > 0.65f ? "light " : l < 0.30f ? "dark " : "";
            return (tone + (warm ? "brown" : "steel")).Trim();
        }

        // brightness qualifier
        string qual = l > 0.70f ? "pale " : l < 0.30f ? "dark " : "";

        string baseName =
            (h < 15 || h >= 345) ? "red" :
            (h < 45) ? (s < 0.55f ? (l > 0.55f ? "straw" : "brown") : "orange") :
            (h < 70) ? (s < 0.45f ? "straw" : "gold") :
            (h < 150) ? "green" :
            (h < 200) ? "teal" :
            (h < 255) ? "blue" :
            (h < 300) ? "purple" :
            "magenta";

        // "straw" already implies pale; avoid "pale straw dark" nonsense
        if (baseName == "straw") return l < 0.45f ? "amber" : "straw";
        return (qual + baseName).Trim();
    }

    // ── HSL helpers: identical math to PaletteSwapService for consistency ──

    private static void RgbToHsl(byte r, byte g, byte b, out float h, out float s, out float l)
    {
        float rf = r / 255f, gf = g / 255f, bf = b / 255f;
        float max = MathF.Max(rf, MathF.Max(gf, bf));
        float min = MathF.Min(rf, MathF.Min(gf, bf));
        float delta = max - min;
        l = (max + min) / 2f;
        if (delta < 0.001f) { h = 0; s = 0; return; }
        s = l > 0.5f ? delta / (2f - max - min) : delta / (max + min);
        if (max == rf) h = ((gf - bf) / delta + (gf < bf ? 6 : 0)) * 60f;
        else if (max == gf) h = ((bf - rf) / delta + 2) * 60f;
        else h = ((rf - gf) / delta + 4) * 60f;
    }

    private static void HslToRgb(float h, float s, float l, out byte r, out byte g, out byte b)
    {
        if (s < 0.001f)
        {
            byte v = (byte)Math.Clamp(l * 255f, 0, 255);
            r = g = b = v; return;
        }
        float q = l < 0.5f ? l * (1 + s) : l + s - l * s;
        float p = 2 * l - q;
        r = (byte)Math.Clamp(HueToChannel(p, q, h + 120) * 255f, 0, 255);
        g = (byte)Math.Clamp(HueToChannel(p, q, h) * 255f, 0, 255);
        b = (byte)Math.Clamp(HueToChannel(p, q, h - 120) * 255f, 0, 255);
    }

    private static float HueToChannel(float p, float q, float h)
    {
        h = ((h % 360) + 360) % 360;
        if (h < 60) return p + (q - p) * h / 60f;
        if (h < 180) return q;
        if (h < 240) return p + (q - p) * (240 - h) / 60f;
        return p;
    }

    // ── Mutable accumulator used during segmentation; promoted to SegmentUnit
    //    (the public DTO) on the way out via ToDto(). ──
    public class Unit
    {
        public int Id;
        public int PixelCount;
        public float SumS, SumL, SumHx, SumHy;
        public float MeanH, MeanS, MeanL;
        /// <summary>
        /// Resultant length of the summed hue unit-vectors / pixel count, 0..1.
        /// 1 = all pixels share a hue (coherent color); near 0 = hues cancel
        /// (the unit spans many hues, so MeanH is not a real color). Used by
        /// DescribeColor to avoid naming a phantom hue.
        /// </summary>
        public float HueMagnitude;
        public string Descriptor = "";
        public float Percent;

        public void ComputeMeans()
        {
            if (PixelCount == 0) return;
            MeanS = SumS / PixelCount;
            MeanL = SumL / PixelCount;
            MeanH = NormHue(MathF.Atan2(SumHy, SumHx) * 180f / MathF.PI);
            HueMagnitude = MathF.Sqrt(SumHx * SumHx + SumHy * SumHy) / PixelCount;
        }

        private static float NormHue(float h) => ((h % 360f) + 360f) % 360f;
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// DTOs
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>Result of segmenting a texture into recolorable color regions.</summary>
public class SegmentationResult
{
    public int Width { get; set; }
    public int Height { get; set; }

    /// <summary>The surviving units, largest first.</summary>
    public List<TextureSegmentationService.Unit> Units { get; set; } = new();

    /// <summary>
    /// Per-pixel unit id (row-major, y*Width+x). -1 = transparent / unassigned.
    /// </summary>
    public int[] PixelUnit { get; set; } = Array.Empty<int>();

    /// <summary>Per-pixel alpha (row-major), for callers that need it.</summary>
    public byte[] Alpha { get; set; } = Array.Empty<byte>();

    /// <summary>Project units to a lightweight serializable shape for JSON / LLM.</summary>
    public List<SegmentUnitDto> ToDtos() =>
        Units.Select(u => new SegmentUnitDto
        {
            Id = u.Id,
            Descriptor = u.Descriptor,
            Percent = MathF.Round(u.Percent, 1),
            MeanH = MathF.Round(u.MeanH, 0),
            MeanS = MathF.Round(u.MeanS, 2),
            MeanL = MathF.Round(u.MeanL, 2),
            HueMagnitude = MathF.Round(u.HueMagnitude, 2),
            PixelCount = u.PixelCount,
        }).ToList();
}

/// <summary>Serializable per-unit summary handed to the UI / recipe generator.</summary>
public class SegmentUnitDto
{
    public int Id { get; set; }
    public string Descriptor { get; set; } = "";
    public float Percent { get; set; }
    public float MeanH { get; set; }
    public float MeanS { get; set; }
    public float MeanL { get; set; }
    public float HueMagnitude { get; set; }
    public int PixelCount { get; set; }
}
/// <summary>
/// How a recolor target treats the source pixel's LIGHTNESS.
///   Preserve — keep source L (default; retains the original sculpting).
///   Lift     — outL = sourceL + Param (uniform brighten).
///   Drop     — outL = sourceL - Param (uniform darken).
///   Invert   — outL = (1 - sourceL) + Param. FLIPS the tonal range: bright
///              regions go dark and dark regions go bright, with the shading
///              coherently inverted. This is the dimension a hue+sat-only swap
///              can't reach — it changes a region's VALUE identity.
/// </summary>
public enum LMode { Preserve, Lift, Drop, Invert }

/// <summary>
/// A per-unit recolor target: hue, saturation, and a lightness behavior.
/// Replaces the bare (H,S) tuple so each material region can independently
/// preserve OR invert its lightness — enabling both whole-texture tonal flips
/// (every unit Invert) and selective flips (only the gold inverts).
/// </summary>
public readonly record struct UnitTarget(float H, float S, LMode L, float Param);