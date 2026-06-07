using SkiaSharp;
using Microsoft.Extensions.Configuration;

namespace MangosSuperUI.Services;

/// <summary>
/// Per-pixel palette swap. No clustering, no LLMs in the recolor path —
/// just hand-written HSL predicates per color family and a static color
/// dictionary for resolving target color names.
///
/// PIPELINE
/// ────────
/// 1. Parse instruction → { family: targetName }
///    e.g. "grey for cream, gold for blue, brown for obsidian"
///       → { "grey": "cream", "gold": "blue", "brown": "obsidian" }
///    Slash-grouped families ("grey/blue/steel") expand to multiple keys.
///
/// 2. Resolve each target name → (H, S). Dictionary, longest-match-wins.
///
/// 3. For each pixel:
///      a. Convert RGB → HSL.
///      b. Walk the user's swap families in instruction order.
///         For each, test the pixel against THAT family's predicate.
///         First match wins.
///      c. If matched: replace H and S with the target's; keep L exactly.
///      d. If no match: leave pixel unchanged.
///
/// FAMILY PREDICATES
/// ─────────────────
/// One function per family name. Hand-written, deliberately broad so
/// they catch what humans mean rather than strict color-theory ranges.
/// "grey" matches both true grays AND the slightly-tinted steel that's
/// all over WoW textures. "gold" matches the full sat range from dull
/// shadow-gold to bright spec-gold. They overlap intentionally —
/// instruction order resolves ambiguity.
/// </summary>
public class PaletteSwapService
{
    private readonly ILogger<PaletteSwapService> _logger;

    // Recolor mode (config-driven, A/B in-engine without a recompile):
    //   SpellCreator:Recolor:Mode  "smooth" (default) — continuous distance-
    //                              preserving chroma map; no classification.
    //                              "region"  — v3 segment + core-classify.
    //                              "perpixel"— legacy MatchesFamily.
    //   SpellCreator:Recolor:Sigma          (float, default 0.18) smooth-map knob:
    //                              small = materials stay distinct, large = blend.
    //   SpellCreator:Recolor:SegmentThreshold (float, default 0.08) region-mode cut.
    private readonly string _recolorMode;
    private readonly float _sigma;
    private readonly float _segThreshold;

    public PaletteSwapService(ILogger<PaletteSwapService> logger, IConfiguration config)
    {
        _logger = logger;
        // Indexer + manual parse (no ConfigurationBinder dependency).
        _recolorMode = (config["SpellCreator:Recolor:Mode"] ?? "smooth").Trim().ToLowerInvariant();
        if (_recolorMode != "region" && _recolorMode != "perpixel") _recolorMode = "smooth";
        _sigma = float.TryParse(
            config["SpellCreator:Recolor:Sigma"],
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var sg) ? sg : 0.18f;
        _segThreshold = float.TryParse(
            config["SpellCreator:Recolor:SegmentThreshold"],
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var segT) ? segT : 0.08f;
        _logger.LogInformation(
            "PaletteSwap: recolor mode = {Mode} (sigma={Sig}, segThreshold={T})",
            _recolorMode, _sigma, _segThreshold);
    }

    /// <summary>Always available — no external dependencies.</summary>
    public bool IsAvailable => true;

    /// <summary>
    /// Public color-name → (Hue, Saturation) resolver. Single source of truth
    /// for color names, shared with the segmentation recolor path
    /// (TextureSegmentationService.RecolorByUnits needs H/S targets, but the
    /// LLM/recipe layer speaks color NAMES like "obsidian black"). Delegates to
    /// the same private ColorDictionary / longest-match logic the family swap
    /// uses, so names resolve identically everywhere.
    ///
    /// Returns null if the name can't be resolved. Lightness behavior is
    /// intentionally dropped here — the segmentation recolor preserves the
    /// source pixel's lightness per-pixel, so only H/S are needed.
    /// </summary>
    public (float H, float S)? ResolveToHs(string colorName)
    {
        var tc = ResolveColorName(colorName);
        return tc == null ? null : (tc.H, tc.S);
    }

    // ═══════════════════════════════════════════════════════════════════
    // PUBLIC API
    // ═══════════════════════════════════════════════════════════════════

    public Task<string?> RecolorAndSaveAsync(
        string sourcePngPath, string instruction, string outputPath, CancellationToken ct = default)
        => RecolorAndSaveAsync(sourcePngPath, instruction, outputPath, null, ct);

    /// <summary>
    /// Recolor with optional box overrides. Boxes are in TEXTURE pixel
    /// coordinates and are applied BEFORE the global family match:
    ///   - rule "leave": pixels in the box are left untouched
    ///   - rule "force": pixels in the box become the box's target color
    /// Everything outside all boxes goes through the normal family swap.
    /// </summary>
    public async Task<string?> RecolorAndSaveAsync(
        string sourcePngPath, string instruction, string outputPath,
        List<BoxOverride>? boxes, CancellationToken ct = default)
    {
        await Task.Yield();

        try
        {
            if (!File.Exists(sourcePngPath))
            {
                _logger.LogWarning("PaletteSwap: Source PNG not found: {Path}", sourcePngPath);
                return null;
            }

            using var source = SKBitmap.Decode(sourcePngPath);
            if (source == null)
            {
                _logger.LogWarning("PaletteSwap: Failed to decode source PNG");
                return null;
            }

            var swaps = ParseInstruction(instruction);

            // Resolve global swaps with the rich fallback chain — never skip.
            // (May 2026) Old behavior: when an LLM creative name like "byssus
            // silk" or "wet peat" failed dictionary lookup, we logged
            // "skipping" and dropped the entire family from the recolor. The
            // result: the family stayed at its original color while other
            // families got recolored — visually MUSHY because half the texture
            // wasn't actually changed. Fix: qualifier-strip → per-word →
            // family-default floor, guaranteeing 100% family coverage.
            var resolved = new List<(string Family, TargetColor Target)>();
            foreach (var (fam, targetName) in swaps)
            {
                var tc = ResolveColorWithFallback(targetName, fam);
                if (tc == null)
                {
                    // Reaching here means even the family-default floor failed,
                    // which can only happen if a family name isn't itself in
                    // the dictionary. That's an internal config bug, log it.
                    _logger.LogWarning(
                        "PaletteSwap: Family '{Fam}' has no dictionary entry, can't even fall back — skipping",
                        fam);
                    continue;
                }
                resolved.Add((fam, tc));
            }

            // Resolve box overrides (force boxes need their target resolved).
            // Boxes are user-typed at a literal target name, so they DO skip
            // on failure (no fallback) — a typed-target failure means the user
            // typed something we don't know, not LLM creativity.
            var resolvedBoxes = new List<ResolvedBox>();
            if (boxes != null)
            {
                foreach (var b in boxes)
                {
                    TargetColor? tc = null;
                    if (b.Rule == "force" && !string.IsNullOrWhiteSpace(b.TargetName))
                        tc = ResolveColorName(b.TargetName);
                    resolvedBoxes.Add(new ResolvedBox(b.X1, b.Y1, b.X2, b.Y2, b.Rule, tc));
                }
            }

            _logger.LogInformation("PaletteSwap: {N} swaps, {B} box override(s): {List}",
                resolved.Count, resolvedBoxes.Count,
                string.Join(", ", resolved.Select(s => $"{s.Family}→(H={s.Target.H:F0}° S={s.Target.S:F2})")));

            if (resolved.Count == 0 && resolvedBoxes.Count == 0)
            {
                _logger.LogWarning("PaletteSwap: Nothing to do (no swaps, no boxes)");
                return null;
            }

            using var remapped = ApplyPerPixel(source, resolved, resolvedBoxes);

            using var outStream = File.Create(outputPath);
            remapped.Encode(outStream, SKEncodedImageFormat.Png, 100);
            _logger.LogInformation("PaletteSwap: Wrote remapped PNG to {Path}", outputPath);
            return outputPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PaletteSwap: RecolorAndSaveAsync failed");
            return null;
        }
    }

    /// <summary>
    /// Scan a source texture and return the color families actually present,
    /// ordered by pixel count (most common first). Deterministic — no model.
    /// Used by the variation mode so the LLM knows which families exist to
    /// assign colors to (e.g. ["grey","gold","brown"]) WITHOUT needing vision.
    /// </summary>
    public List<DetectedFamily> DetectFamilies(string sourcePngPath)
    {
        var result = new List<DetectedFamily>();
        if (!File.Exists(sourcePngPath)) return result;
        using var source = SKBitmap.Decode(sourcePngPath);
        if (source == null) return result;

        var counts = new Dictionary<string, int>();
        var satSum = new Dictionary<string, float>();
        var lSum = new Dictionary<string, float>();
        int total = 0;

        // All family names we can detect (the predicate set)
        string[] families = { "grey", "gold", "brown", "blue", "red", "green", "orange", "purple" };

        for (int y = 0; y < source.Height; y++)
        {
            for (int x = 0; x < source.Width; x++)
            {
                var px = source.GetPixel(x, y);
                if (px.Alpha < 16) continue;
                RgbToHsl(px.Red, px.Green, px.Blue, out float h, out float s, out float l);
                total++;
                // First family that claims it (same order semantics as apply)
                foreach (var fam in families)
                {
                    if (MatchesFamily(fam, h, s, l))
                    {
                        counts[fam] = counts.GetValueOrDefault(fam, 0) + 1;
                        satSum[fam] = satSum.GetValueOrDefault(fam, 0) + s;
                        lSum[fam] = lSum.GetValueOrDefault(fam, 0) + l;
                        break;
                    }
                }
            }
        }

        foreach (var (fam, n) in counts.OrderByDescending(kv => kv.Value))
        {
            // Ignore trivial families (< 2% of pixels) as noise
            if (n < total * 0.02) continue;
            result.Add(new DetectedFamily(
                fam, n, 100f * n / Math.Max(1, total),
                satSum[fam] / n, lSum[fam] / n));
        }

        _logger.LogInformation("PaletteSwap: Detected families: {List}",
            string.Join(", ", result.Select(f => $"{f.Family} {f.Percent:F0}%")));
        return result;
    }
    // LIGHTNESS BEHAVIORS
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// How a target color treats lightness when applied to a source pixel.
    /// Most palette swaps preserve the source's lightness exactly so all the
    /// hand-painted gradient detail (shadows, highlights) carries over.
    ///
    /// Lift/Drop apply a small CONSTANT OFFSET to lightness — they shift the
    /// whole gradient up or down uniformly rather than lerping toward a target
    /// (which compresses the gradient and flattens the sculpting). The offset
    /// is gentle by design; Flux polish does the heavy lifting on appearance.
    /// </summary>
    private enum LBehavior { Preserve, LiftTo, DropTo }

    /// <summary>
    /// A resolved target: hue, saturation, and a lightness behavior.
    ///   Preserve: keep source L exactly.
    ///   LiftTo(offset): output L = clamp(sourceL + offset).  Brightens uniformly.
    ///   DropTo(offset): output L = clamp(sourceL - offset).  Darkens uniformly.
    /// The offset is small (0.05-0.15) — just enough to nudge the family toward
    /// its expected brightness range without destroying the gradient.
    /// </summary>
    // Internal alias used throughout the engine.
    private record TargetColor(float H, float S, LBehavior Behavior, float Offset = 0.0f);

    /// <summary>Internal: a BoxOverride with its target color resolved.</summary>
    private record ResolvedBox(int X1, int Y1, int X2, int Y2, string Rule, TargetColor? Target);

    private static float ApplyLightnessBehavior(float sourceL, TargetColor t)
    {
        return t.Behavior switch
        {
            LBehavior.LiftTo => Math.Clamp(sourceL + t.Offset, 0f, 1f),
            LBehavior.DropTo => Math.Clamp(sourceL - t.Offset, 0f, 1f),
            _ => sourceL,
        };
    }

    // ═══════════════════════════════════════════════════════════════════
    // STEP 3 — SPATIALLY-COHERENT REMAP  (label map → consolidate → blend)
    //
    //   The classify-and-recolor-per-pixel design had no spatial awareness, so
    //   on low-res DXT-decoded vanilla textures it produced three artifacts:
    //     • speckle   — lone noisy pixels cross a family threshold and get the
    //                   wrong colour (grainy handle).
    //     • fringe    — anti-aliased boundary pixels flip-flop between families,
    //                   turning a clean edge into a noisy stipple (soft symbol).
    //     • gaps/leak — colour spills 1px past a painted boundary; shadowed
    //                   pixels drop below a predicate and stay un-recolored.
    //
    //   Root cause, measured on real output: the label map fragments into
    //   HUNDREDS of tiny components (one Ironfoe variant: 266 components, 210 of
    //   them <=4px — scattered confetti of minority families). The fix is to
    //   separate "what material is this pixel" from "how bright is it":
    //     Pass 1  classify into an int[] LABEL MAP (swap index / -1 unmatched).
    //             Boxes + transparent pixels are LOCKED (never touched).
    //     Pass 2  CONSOLIDATE — dissolve every connected component below
    //             MinRegionPx into the dominant label bordering it. Kills
    //             scattered specks regardless of cluster size, while a thin but
    //             LONG feature (rune stroke, gold line) is one large-area
    //             component and survives. Lightness is preserved, so a dissolved
    //             spec-highlight pixel just becomes a bright pixel of the RIGHT
    //             material — the sculpting stays, only the wrong colour leaves.
    //     Pass 3  recolor from the CLEANED labels with each pixel's ORIGINAL
    //             lightness, then a 1px chroma-only blend across CLEAN two-label
    //             seams only (multi-label junctions stay hard — blending those
    //             is what made the fragmented map look muddy). Lightness is never
    //             blurred and interiors are never touched — detail stays sharp.
    // ═══════════════════════════════════════════════════════════════════

    // Pass-2/3 tunables.
    //   MinRegionPx       — connected components <= this many pixels are dissolved
    //                       into the region around them. Validated on the real
    //                       Ironfoe SOURCE family map (MatchesFamily classify):
    //                       the map has only ~93 components; 12 already collapses
    //                       it to ~13, and 24 also clears the 13–30px DXT-block
    //                       confetti (DXT compresses in 4×4 blocks, so noise
    //                       clusters rather than sitting as single pixels) while
    //                       staying well below the 31px+ real features (rune
    //                       strokes, X arms). Raise toward 40 for more aggressive
    //                       cleanup, lower for finer detail.
    //   ConsolidateMaxIters — cascade cap (merging one speck can expose another).
    //   SeamBlend         — OFF by default. It was meant to smooth boundary AA,
    //                       but on this fine-weave texture ~25% of pixels sit on
    //                       a seam, so blending webs intermediate colours across
    //                       the whole texture and SOFTENS it — the opposite of
    //                       the goal. Head-to-head on the real source, blend-off
    //                       is visibly sharper and cleaner. Set true only if a
    //                       given texture has long, smooth, high-contrast borders
    //                       that read as aliased with hard edges.
    private const int MinRegionPx = 24;
    private const int ConsolidateMaxIters = 8;
    private const bool SeamBlend = false;

    private const int LabelUnmatched = -1;     // passthrough (keep original)
    private const int LabelLocked = -1000;  // box/transparent — never touch

    // v3 region-classification tunables. A region is named by its saturated CORE;
    // with no real core it resolves to grey/steel — this is what stops a lone
    // specular highlight from dragging a whole steel panel to its colour.
    private const float CoreMinSat = 0.45f; // a "core" pixel must be at least this saturated
    private const float CoreMaxL = 0.68f; // ...and not a blown-out specular highlight
    private const float CoreMinFraction = 0.14f; // core must be at least this fraction of the region
    private const int CoreMinAbs = 8;     // ...and at least this many pixels
    private const int RegionMergeMinPx = 6;    // regions <= this merge into nearest neighbour

    // Smooth-map: a family contributes an anchor only if it has at least this many
    // source pixels (else its centroid is noise).
    private const int SmoothAnchorMinPx = 20;

    /// <summary>
    /// Dissolve every connected component (8-connectivity, equal label) whose
    /// area is &lt;= minPx into the label that most borders it. Operates in place
    /// on `labels`. Locked pixels are walls: never flooded, never merged into,
    /// not counted as a border target. Iterates so cascades settle. Returns the
    /// number of pixels reassigned (for diagnostics).
    /// </summary>
    private static int ConsolidateSmallRegions(int[] labels, bool[] locked,
        int W, int H, int minPx, int maxIters)
    {
        int n = W * H;
        var comp = new int[n];
        var stack = new int[n];
        int totalReassigned = 0;

        for (int iter = 0; iter < maxIters; iter++)
        {
            for (int k = 0; k < n; k++) comp[k] = -1;
            bool changed = false;

            for (int start = 0; start < n; start++)
            {
                if (locked[start] || comp[start] != -1) continue;
                int lbl = labels[start];

                // Flood-fill this component (8-conn, equal label, non-locked).
                int sp = 0; stack[sp++] = start; comp[start] = 1;
                var members = new List<int>();
                while (sp > 0)
                {
                    int cur = stack[--sp];
                    members.Add(cur);
                    int cx = cur % W, cy = cur / W;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int yy = cy + dy; if (yy < 0 || yy >= H) continue;
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            int xx = cx + dx; if (xx < 0 || xx >= W) continue;
                            int j = yy * W + xx;
                            if (locked[j] || comp[j] != -1 || labels[j] != lbl) continue;
                            comp[j] = 1; stack[sp++] = j;
                        }
                    }
                }

                if (members.Count > minPx) continue;   // big enough — keep

                // Dominant bordering OTHER label.
                var border = new Dictionary<int, int>();
                foreach (int m in members)
                {
                    int mx = m % W, my = m / W;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int yy = my + dy; if (yy < 0 || yy >= H) continue;
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            int xx = mx + dx; if (xx < 0 || xx >= W) continue;
                            int j = yy * W + xx;
                            if (locked[j]) continue;
                            int lj = labels[j];
                            if (lj == lbl) continue;
                            border[lj] = border.GetValueOrDefault(lj, 0) + 1;
                        }
                    }
                }
                if (border.Count == 0) continue;        // island with no other-label border

                int dom = lbl, dn = -1;
                foreach (var kv in border) if (kv.Value > dn) { dn = kv.Value; dom = kv.Key; }
                foreach (int m in members) labels[m] = dom;
                totalReassigned += members.Count;
                changed = true;
            }

            if (!changed) break;
        }
        return totalReassigned;
    }

    // ═══════════════════════════════════════════════════════════════════
    // v3 REGION CLASSIFICATION  (segment by similarity → name by core)
    //
    //   Replaces per-pixel MatchesFamily when SpellCreator:Recolor:UseRegionClassify
    //   is true. Two stages:
    //     1) SEGMENT the source into regions by 4-neighbour colour similarity —
    //        union pixels across smooth transitions, cut at hard shifts. Chroma is
    //        encoded as a vector (a,b)=(s·cosH, s·sinH) so low-sat pixels sit near
    //        the origin and lightness is weighted low (shading varies WITHIN a
    //        material). Small regions merge into their nearest neighbour.
    //     2) NAME each region by its saturated, non-highlight CORE. No real core ⇒
    //        grey/steel. This is the key property: a material's shadow inherits the
    //        region's name, and a lone specular highlight can't speak for a panel.
    // ═══════════════════════════════════════════════════════════════════

    private int[] RegionClassify(float[] srcH, float[] srcS, float[] srcL, bool[] locked,
        int W, int H, List<(string Family, TargetColor Target)> swaps, out int regionCount)
    {
        int n = W * H;
        var labels = new int[n];
        for (int i = 0; i < n; i++) labels[i] = LabelUnmatched;

        // Chroma-vector features.
        var fa = new float[n]; var fb = new float[n];
        for (int i = 0; i < n; i++)
        {
            if (locked[i]) continue;
            double rad = srcH[i] * Math.PI / 180.0;
            fa[i] = (float)(srcS[i] * Math.Cos(rad));
            fb[i] = (float)(srcS[i] * Math.Sin(rad));
        }

        // Union-find over non-locked 4-neighbours; union across smooth transitions.
        var parent = new int[n];
        for (int i = 0; i < n; i++) parent[i] = i;

        float T = _segThreshold;
        float Diff(int i, int j)
        {
            float dL = srcL[i] - srcL[j], da = fa[i] - fa[j], db = fb[i] - fb[j];
            return (float)Math.Sqrt(0.2025f * dL * dL + da * da + db * db); // 0.45^2 = 0.2025
        }
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int i = y * W + x;
                if (locked[i]) continue;
                if (x + 1 < W) { int j = i + 1; if (!locked[j] && Diff(i, j) <= T) UfUnion(parent, i, j); }
                if (y + 1 < H) { int j = i + W; if (!locked[j] && Diff(i, j) <= T) UfUnion(parent, i, j); }
            }

        MergeSmallRegions(parent, fa, fb, srcL, locked, W, H, RegionMergeMinPx);

        // Gather final region members.
        var members = new Dictionary<int, List<int>>();
        for (int i = 0; i < n; i++)
        {
            if (locked[i]) continue;
            int r = UfFind(parent, i);
            if (!members.TryGetValue(r, out var list)) { list = new List<int>(); members[r] = list; }
            list.Add(i);
        }
        regionCount = members.Count;

        // Name each region by its core, then map family → swap index.
        foreach (var kv in members)
        {
            string fam = ClassifyRegionFamily(kv.Value, srcS, srcL, fa, fb);
            int swapIdx = MapFamilyToSwap(fam, swaps);
            foreach (int i in kv.Value) labels[i] = swapIdx;
        }
        return labels;
    }

    private static int UfFind(int[] parent, int x)
    {
        while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; }
        return x;
    }
    private static void UfUnion(int[] parent, int x, int y)
    {
        int rx = UfFind(parent, x), ry = UfFind(parent, y);
        if (rx != ry) parent[ry] = rx;
    }

    // Map a classified family to a swap index. Exact match first; if the recipe
    // has no swap for that exact family, fall back within the warm or cool group
    // (e.g. a "red" region uses the "brown"/"gold" swap when there's no "red"),
    // so a region is never left un-recolored just because the LLM named the warm
    // accent "brown" rather than "red". Returns -1 only when no related swap exists.
    private static int MapFamilyToSwap(string fam, List<(string Family, TargetColor Target)> swaps)
    {
        for (int si = 0; si < swaps.Count; si++)
            if (string.Equals(swaps[si].Family, fam, StringComparison.OrdinalIgnoreCase)) return si;
        string[] warm = { "red", "orange", "brown", "gold", "yellow" };
        string[] cool = { "green", "blue", "purple" };
        string[]? group = Array.IndexOf(warm, fam) >= 0 ? warm
                        : Array.IndexOf(cool, fam) >= 0 ? cool : null;
        if (group != null)
            foreach (var f in group)
                for (int si = 0; si < swaps.Count; si++)
                    if (string.Equals(swaps[si].Family, f, StringComparison.OrdinalIgnoreCase)) return si;
        return LabelUnmatched;
    }

    /// <summary>
    /// Merge any region of size &lt;= minPx into the adjacent region whose mean
    /// colour is closest. Iterates so chains of small regions settle.
    /// </summary>
    private static void MergeSmallRegions(int[] parent, float[] fa, float[] fb, float[] srcL,
        bool[] locked, int W, int H, int minPx)
    {
        int n = W * H;
        int[] dx = { 1, -1, 0, 0 }, dy = { 0, 0, 1, -1 };
        for (int iter = 0; iter < 6; iter++)
        {
            var sumA = new Dictionary<int, float>();
            var sumB = new Dictionary<int, float>();
            var sumL = new Dictionary<int, float>();
            var cnt = new Dictionary<int, int>();
            for (int i = 0; i < n; i++)
            {
                if (locked[i]) continue;
                int r = UfFind(parent, i);
                sumA[r] = sumA.GetValueOrDefault(r) + fa[i];
                sumB[r] = sumB.GetValueOrDefault(r) + fb[i];
                sumL[r] = sumL.GetValueOrDefault(r) + srcL[i];
                cnt[r] = cnt.GetValueOrDefault(r) + 1;
            }
            bool changed = false;
            foreach (var (r, c) in cnt)
            {
                if (c > minPx) continue;
                float ma = sumA[r] / c, mb = sumB[r] / c, ml = sumL[r] / c;
                int best = -1; float bd = float.MaxValue;
                for (int i = 0; i < n; i++)
                {
                    if (locked[i] || UfFind(parent, i) != r) continue;
                    int x = i % W, y = i / W;
                    for (int d = 0; d < 4; d++)
                    {
                        int nx = x + dx[d], ny = y + dy[d];
                        if (nx < 0 || nx >= W || ny < 0 || ny >= H) continue;
                        int j = ny * W + nx; if (locked[j]) continue;
                        int rj = UfFind(parent, j);
                        if (rj == r || !cnt.ContainsKey(rj)) continue;
                        int oc = cnt[rj];
                        float oa = sumA[rj] / oc, ob = sumB[rj] / oc, ol = sumL[rj] / oc;
                        float dd = (float)Math.Sqrt(0.2025f * (ml - ol) * (ml - ol)
                                                  + (ma - oa) * (ma - oa) + (mb - ob) * (mb - ob));
                        if (dd < bd) { bd = dd; best = rj; }
                    }
                }
                if (best >= 0) { parent[UfFind(parent, r)] = UfFind(parent, best); changed = true; }
            }
            if (!changed) break;
        }
    }

    /// <summary>
    /// Name a region's family by its saturated, non-highlight CORE. No real core
    /// ⇒ "grey" (steel). Prevents a lone specular highlight from colouring a panel.
    /// </summary>
    private static string ClassifyRegionFamily(List<int> px, float[] srcS, float[] srcL,
        float[] fa, float[] fb)
    {
        int c = px.Count;
        float coreA = 0, coreB = 0; int coreN = 0;
        foreach (int i in px)
            if (srcS[i] >= CoreMinSat && srcL[i] <= CoreMaxL) { coreA += fa[i]; coreB += fb[i]; coreN++; }
        if (coreN < Math.Max(CoreMinAbs, (int)(CoreMinFraction * c))) return "grey";
        coreA /= coreN; coreB /= coreN;
        double hue = Math.Atan2(coreB, coreA) * 180.0 / Math.PI; if (hue < 0) hue += 360;
        if (hue < 10 || hue >= 345) return "red";
        if (hue < 35) return "brown";
        if (hue < 70) return "gold";
        if (hue < 150) return "green";
        if (hue < 260) return "blue";
        return "purple";
    }

    // ═══════════════════════════════════════════════════════════════════
    // SMOOTH MAP  (continuous distance-preserving chroma transform)
    //
    //   The default recolor path. No classification: every pixel is remapped by
    //   one smooth function of its ORIGINAL chroma, so colours that were close in
    //   the source stay close in the result — the original's internal geometry
    //   (its highlight→shadow gradients, its "where to bleed") is preserved.
    //   The recipe places ANCHORS (each family's mean source chroma → that
    //   family's target chroma); each pixel's new chroma is the Gaussian
    //   distance-weighted blend of the target chromas. Lightness is preserved
    //   exactly — all the sculpting lives there. sigma is the one knob: small =
    //   each pixel pulled toward its nearest anchor (materials stay distinct),
    //   large = everything averages together (warmth bleeds across).
    // ═══════════════════════════════════════════════════════════════════

    private SKBitmap ApplySmoothMap(SKBitmap result,
        float[] srcH, float[] srcS, float[] srcL, byte[] alpha, bool[] locked, SKColor[] lockedColor,
        int W, int H, List<(string Family, TargetColor Target)> swaps,
        int totalPixels, int boxLeave, int boxForce)
    {
        int n = W * H;

        // Source chroma plane (a, b) = (s·cosH, s·sinH).
        var fa = new float[n]; var fb = new float[n];
        for (int i = 0; i < n; i++)
        {
            if (locked[i]) continue;
            double rad = srcH[i] * Math.PI / 180.0;
            fa[i] = (float)(srcS[i] * Math.Cos(rad));
            fb[i] = (float)(srcS[i] * Math.Sin(rad));
        }

        // Anchors: original family-mean chroma → target chroma. MatchesFamily is
        // used ONLY to locate each anchor's centroid — never to classify a pixel
        // for output. Families with too few source pixels contribute no anchor.
        var oa = new List<float>(); var ob = new List<float>();
        var ta = new List<float>(); var tb = new List<float>();
        var atc = new List<TargetColor>();   // per-anchor target, for its lightness behavior
        var anchorNames = new List<string>();
        foreach (var (fam, target) in swaps)
        {
            // White/black are LIGHTNESS families, not chroma materials. In a
            // chroma-only map their anchor lands at the origin (where grey is) but
            // with a far target, which would drag neutrals toward that colour.
            // Lightness is preserved per pixel already, so they don't belong here.
            string fl = fam.ToLowerInvariant();
            if (fl == "white" || fl == "black") continue;
            double sa = 0, sb = 0; int cnt = 0;
            for (int i = 0; i < n; i++)
            {
                if (locked[i]) continue;
                if (MatchesFamily(fam, srcH[i], srcS[i], srcL[i])) { sa += fa[i]; sb += fb[i]; cnt++; }
            }
            if (cnt < SmoothAnchorMinPx) continue;
            double trad = target.H * Math.PI / 180.0;
            oa.Add((float)(sa / cnt)); ob.Add((float)(sb / cnt));
            ta.Add((float)(target.S * Math.Cos(trad))); tb.Add((float)(target.S * Math.Sin(trad)));
            atc.Add(target);
            anchorNames.Add(fam);
        }
        int A = oa.Count;

        if (A == 0)
        {
            // No anchors — pass the source through untouched.
            for (int i = 0; i < n; i++)
            {
                int x0 = i % W, y0 = i / W;
                if (locked[i]) { result.SetPixel(x0, y0, lockedColor[i]); continue; }
                HslToRgb(srcH[i], srcS[i], srcL[i], out byte r0, out byte g0, out byte b0);
                result.SetPixel(x0, y0, new SKColor(r0, g0, b0, alpha[i]));
            }
            _logger.LogWarning("PaletteSwap: [smooth] no anchors resolved from {N} swaps — source unchanged", swaps.Count);
            return result;
        }

        float sigma = _sigma;
        double twoSigSq = 2.0 * sigma * sigma;
        int touched = 0;

        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int i = y * W + x;
                if (locked[i]) { result.SetPixel(x, y, lockedColor[i]); continue; }

                float pa = fa[i], pb = fb[i];
                double na = 0, nb = 0, nl = 0, wsum = 0;
                for (int k = 0; k < A; k++)
                {
                    double da = pa - oa[k], db = pb - ob[k];
                    double w = Math.Exp(-(da * da + db * db) / twoSigSq);
                    na += w * ta[k]; nb += w * tb[k];
                    nl += w * ApplyLightnessBehavior(srcL[i], atc[k]);   // blend lightness transform alongside chroma
                    wsum += w;
                }

                float outH, outS, outL;
                if (wsum < 1e-9)
                {
                    outH = srcH[i]; outS = srcS[i]; outL = srcL[i];   // beyond every anchor's reach — keep original
                }
                else
                {
                    na /= wsum; nb /= wsum;
                    outS = (float)Math.Min(1.0, Math.Sqrt(na * na + nb * nb));
                    double ang = Math.Atan2(nb, na) * 180.0 / Math.PI; if (ang < 0) ang += 360.0;
                    outH = (float)ang;
                    outL = (float)(nl / wsum);   // weighted lightness — all-Preserve anchors give exactly srcL
                    touched++;
                }

                HslToRgb(outH, outS, outL, out byte r, out byte g, out byte b);   // lightness now anchor-driven (tonal targets)
                result.SetPixel(x, y, new SKColor(r, g, b, alpha[i]));
            }

        _logger.LogInformation(
            "PaletteSwap: [smooth] Touched {T}/{N} pixels ({Pct:F1}%) | {A} anchors ({Names}) | sigma={Sig}"
            + (boxLeave > 0 || boxForce > 0 ? $" [box: {boxLeave} left, {boxForce} forced]" : ""),
            touched, totalPixels, 100.0 * touched / Math.Max(1, totalPixels), A,
            string.Join(",", anchorNames), sigma);
        return result;
    }

    private SKBitmap ApplyPerPixel(SKBitmap source,
        List<(string Family, TargetColor Target)> swaps,
        List<ResolvedBox>? boxes = null)
    {
        int W = source.Width, H = source.Height;
        var result = new SKBitmap(W, H, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        bool hasBoxes = boxes != null && boxes.Count > 0;
        bool smoothMap = _recolorMode == "smooth";
        bool useRegion = _recolorMode == "region";

        int n = W * H;
        var labels = new int[n];
        var srcH = new float[n];
        var srcS = new float[n];
        var srcL = new float[n];
        var alpha = new byte[n];
        var locked = new bool[n];
        var lockedColor = new SKColor[n];

        var stats = new Dictionary<string, int>();
        int totalPixels = 0, boxLeave = 0, boxForce = 0;
        var unmatchedBins = new Dictionary<(int HueBin, int SatBin), int>();
        var familySubBins = new Dictionary<(string Fam, int HueBin, int SatBin), int>();

        // ── Pass 1: classify into the label map ──
        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                int i = y * W + x;
                var px = source.GetPixel(x, y);
                alpha[i] = px.Alpha;

                if (px.Alpha < 16)
                {
                    labels[i] = LabelLocked; locked[i] = true; lockedColor[i] = px;
                    continue;
                }
                totalPixels++;

                RgbToHsl(px.Red, px.Green, px.Blue, out float h, out float s, out float l);
                srcH[i] = h; srcS[i] = s; srcL[i] = l;

                // Box overrides take priority and are LOCKED (user intent).
                if (hasBoxes)
                {
                    bool handled = false;
                    foreach (var box in boxes!)
                    {
                        if (x < box.X1 || x > box.X2 || y < box.Y1 || y > box.Y2) continue;
                        if (box.Rule == "leave")
                        {
                            locked[i] = true; labels[i] = LabelLocked; lockedColor[i] = px;
                            boxLeave++; handled = true; break;
                        }
                        if (box.Rule == "force" && box.Target != null)
                        {
                            float bl = ApplyLightnessBehavior(l, box.Target);
                            HslToRgb(box.Target.H, box.Target.S, bl, out byte br, out byte bg, out byte bb);
                            locked[i] = true; labels[i] = LabelLocked;
                            lockedColor[i] = new SKColor(br, bg, bb, px.Alpha);
                            boxForce++; handled = true; break;
                        }
                    }
                    if (handled) continue;
                }

                if (smoothMap || useRegion)
                {
                    labels[i] = LabelUnmatched;   // placeholder — smooth path / region pass fills it
                }
                else
                {
                    int matchIdx = LabelUnmatched;
                    for (int si = 0; si < swaps.Count; si++)
                    {
                        if (MatchesFamily(swaps[si].Family, h, s, l)) { matchIdx = si; break; }
                    }
                    labels[i] = matchIdx;

                    if (matchIdx == LabelUnmatched)
                    {
                        int hueBin = ((int)(h / 30)) % 12;
                        int satBin = Math.Min(9, (int)(s * 10));
                        var k = (hueBin, satBin);
                        unmatchedBins[k] = unmatchedBins.GetValueOrDefault(k, 0) + 1;
                    }
                }
            }
        }

        // ── Smooth map (default): continuous distance-preserving chroma transform.
        // Short-circuits the label/consolidate/recolor passes entirely — every
        // pixel is remapped by one smooth function of its original chroma. ──
        if (smoothMap && swaps.Count > 0)
            return ApplySmoothMap(result, srcH, srcS, srcL, alpha, locked, lockedColor,
                                  W, H, swaps, totalPixels, boxLeave, boxForce);

        // ── Pass 1b (region): segment SOURCE by neighbour similarity, name each
        // region by its saturated core, override the per-pixel labels. ──
        int segRegions = 0;
        if (useRegion && swaps.Count > 0)
        {
            var rlabels = RegionClassify(srcH, srcS, srcL, locked, W, H, swaps, out segRegions);
            for (int i = 0; i < n; i++) if (!locked[i]) labels[i] = rlabels[i];
        }

        // ── Pass 2: consolidate — dissolve tiny components into their host region ──
        int consolidated = 0;
        if (swaps.Count > 0 && !useRegion)   // region mode already produces clean regions
            consolidated = ConsolidateSmallRegions(labels, locked, W, H, MinRegionPx, ConsolidateMaxIters);

        // ── Pass 3a: hard recolour from cleaned labels (lightness preserved) ──
        var fH = new float[n]; var fS = new float[n]; var fL = new float[n];
        int touched = 0;
        for (int i = 0; i < n; i++)
        {
            if (locked[i])
            {
                var c = lockedColor[i];
                RgbToHsl(c.Red, c.Green, c.Blue, out float lh, out float ls, out float ll);
                fH[i] = lh; fS[i] = ls; fL[i] = ll;
                continue;
            }
            int lbl = labels[i];
            if (lbl == LabelUnmatched)
            {
                fH[i] = srcH[i]; fS[i] = srcS[i]; fL[i] = srcL[i];   // passthrough
            }
            else
            {
                var (fam, target) = swaps[lbl];
                float outL = ApplyLightnessBehavior(srcL[i], target);
                fH[i] = target.H; fS[i] = target.S; fL[i] = outL;
                touched++;
                stats[fam] = stats.GetValueOrDefault(fam, 0) + 1;
                var sb = (fam, (int)(srcH[i] / 10), Math.Min(9, (int)(srcS[i] * 10)));
                familySubBins[sb] = familySubBins.GetValueOrDefault(sb, 0) + 1;
            }
        }

        // ── Pass 3b: 1px chroma-only blend across CLEAN two-label seams only ──
        // A pixel blends iff its 8-neighbourhood contains exactly ONE distinct
        // non-locked label different from its own (a clean boundary between two
        // regions). Zero → interior (copy hard). Two or more → junction (copy
        // hard; averaging multiple target hues there is what looked muddy).
        int blended = 0;
        for (int y = 0; y < H; y++)
        {
            for (int x = 0; x < W; x++)
            {
                int i = y * W + x;
                if (locked[i]) { result.SetPixel(x, y, lockedColor[i]); continue; }
                int li = labels[i];

                int otherA = int.MinValue; bool multi = false;
                for (int dy = -1; dy <= 1 && !multi; dy++)
                {
                    int yy = y + dy; if (yy < 0 || yy >= H) continue;
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int xx = x + dx; if (xx < 0 || xx >= W) continue;
                        int j = yy * W + xx;
                        if (locked[j]) continue;
                        int lj = labels[j];
                        if (lj == li) continue;
                        if (otherA == int.MinValue) otherA = lj;
                        else if (lj != otherA) { multi = true; break; }
                    }
                }

                float outH = fH[i], outS = fS[i];
                bool doBlend = SeamBlend && !multi && otherA != int.MinValue;
                if (doBlend)
                {
                    double cs = Math.Cos(fH[i] * Math.PI / 180.0) * 2.0;
                    double sn = Math.Sin(fH[i] * Math.PI / 180.0) * 2.0;
                    double ssum = fS[i] * 2.0, wsum = 2.0;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int yy = y + dy; if (yy < 0 || yy >= H) continue;
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            int xx = x + dx; if (xx < 0 || xx >= W) continue;
                            int j = yy * W + xx;
                            if (locked[j]) continue;
                            cs += Math.Cos(fH[j] * Math.PI / 180.0);
                            sn += Math.Sin(fH[j] * Math.PI / 180.0);
                            ssum += fS[j]; wsum += 1.0;
                        }
                    }
                    double ang = Math.Atan2(sn, cs) * 180.0 / Math.PI;
                    if (ang < 0) ang += 360.0;
                    outH = (float)ang;
                    outS = (float)(ssum / wsum);
                    blended++;
                }

                HslToRgb(outH, outS, fL[i], out byte r, out byte g, out byte b);
                result.SetPixel(x, y, new SKColor(r, g, b, alpha[i]));
            }
        }

        // ── Diagnostics (kept — reading logs beats hypothesising) ──
        _logger.LogInformation(
            "PaletteSwap: [{Mode}] Touched {T}/{N} pixels ({Pct:F1}%) | regions {R} | consolidated {C} px | seam-blended {B}"
            + (boxLeave > 0 || boxForce > 0 ? $" [box: {boxLeave} left, {boxForce} forced]" : ""),
            useRegion ? "region" : "per-pixel",
            touched, totalPixels, 100.0 * touched / Math.Max(1, totalPixels), segRegions, consolidated, blended);
        foreach (var (fam, count) in stats.OrderByDescending(kv => kv.Value))
            _logger.LogInformation("    {Fam}: {N} pixels ({Pct:F1}%)",
                fam, count, 100.0 * count / Math.Max(1, totalPixels));
        if (unmatchedBins.Count > 0)
        {
            int unmatchedTotal = totalPixels - touched;
            _logger.LogInformation("PaletteSwap: {N} unmatched pixels — top zones:", unmatchedTotal);
            foreach (var (key, cnt) in unmatchedBins.OrderByDescending(kv => kv.Value).Take(8))
            {
                int hLo = key.HueBin * 30;
                float sLo = key.SatBin * 0.1f;
                _logger.LogInformation("    H={HLo}-{HHi}° S={SLo:F1}-{SHi:F1}: {N} pixels",
                    hLo, hLo + 30, sLo, sLo + 0.1f, cnt);
            }
        }
        var goldBins = familySubBins
            .Where(kv => kv.Key.Fam == "gold")
            .OrderBy(kv => kv.Key.HueBin).ThenBy(kv => kv.Key.SatBin).ToList();
        if (goldBins.Count > 0)
        {
            _logger.LogInformation("PaletteSwap: GOLD family fine breakdown (H 10° × S 0.1):");
            foreach (var kv in goldBins)
            {
                int hLo = kv.Key.HueBin * 10;
                float sLo = kv.Key.SatBin * 0.1f;
                _logger.LogInformation("    H={HLo}-{HHi}° S={SLo:F1}-{SHi:F1}: {N} px",
                    hLo, hLo + 10, sLo, sLo + 0.1f, kv.Value);
            }
        }
        return result;
    }

    // ═══════════════════════════════════════════════════════════════════
    // FAMILY PREDICATES
    //   Hand-written. Broad on purpose. Order doesn't matter inside the
    //   predicate — the function just asks "is this pixel of this family?"
    //   Order is set by the user's instruction (first match wins).
    // ═══════════════════════════════════════════════════════════════════

    private static bool MatchesFamily(string family, float h, float s, float l)
    {
        return family switch
        {
            // "grey" includes anything humans would describe as gray, silver,
            // steel, or metallic. Low sat overall, plus the cool zone
            // (H 150-260) up to moderate saturation. Widened to include
            // cyan/teal accents at H ~160 which Ironfoe uses for spec highlights.
            "grey" or "silver" or "steel" =>
                s < 0.30f
                || (s < 0.55f && h >= 150 && h <= 260),

            // "white" = very high lightness, very low saturation.
            "white" =>
                l >= 0.75f && s < 0.20f,

            // "black" = very low lightness regardless of hue/sat.
            "black" =>
                l <= 0.20f,

            // "gold" / "yellow" — warm hues, broad sat range to catch dull
            // shadow gold (s≈0.30) and bright spec gold (s≈0.90) alike.
            "gold" or "yellow" =>
                h >= 35 && h <= 70 && s >= 0.25f,

            // "brown" — warm low-mid hues, low-to-mid saturation. Covers
            // leather, wood, rust. Don't claim the desaturated zone (that's grey).
            "brown" =>
                h >= 10 && h < 45 && s >= 0.20f && s < 0.80f,

            // "orange" — warm, high saturation. Distinct from brown.
            "orange" =>
                h >= 15 && h < 45 && s >= 0.70f,

            // "red" — true reds.
            "red" =>
                (h >= 345 || h < 15) && s >= 0.30f,

            // "green" — broad green range.
            "green" =>
                h >= 75 && h < 150 && s >= 0.20f,

            // "blue" — true blues (NOT including the cool-grey zone, which
            // belongs to "grey"; this is intentional and rarely conflicts
            // because grey/blue/steel are typically swapped as one group).
            "blue" =>
                h >= 150 && h < 260 && s >= 0.55f,

            // "purple" — violets, magentas.
            "purple" =>
                h >= 260 && h < 330 && s >= 0.20f,

            _ => false,
        };
    }

    // ═══════════════════════════════════════════════════════════════════
    // STEP 1 — INSTRUCTION PARSING
    // ═══════════════════════════════════════════════════════════════════

    private static readonly HashSet<string> KnownFamilies = new()
    {
        "grey", "gray", "blue", "red", "green", "yellow",
        "orange", "purple", "brown", "gold", "silver", "steel",
        "black", "white",
    };

    /// <summary>
    /// Parse the user's instruction into an ORDERED list of (from-family, target-name).
    /// Order is preserved because predicates may overlap; first match wins per-pixel.
    /// Slash-grouped sources ("grey/blue/steel for cream") expand to multiple entries.
    /// </summary>
    private List<(string Family, string TargetName)> ParseInstruction(string instruction)
    {
        var result = new List<(string, string)>();
        if (string.IsNullOrWhiteSpace(instruction)) return result;

        // Split into sentences, then within each sentence into clauses on
        // ", " or " and " when the next segment looks like a new "X for Y".
        var sentences = instruction.Split(new[] { '.', ';', '\n' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var clauses = new List<string>();
        foreach (var sent in sentences) clauses.AddRange(SplitOnSecondarySwaps(sent));

        string[] connectors = { " for ", " to ", " with ", " into ", " → ", " => ", "->" };
        string[] leadingFluff = { "a more ", "more ", "a ", "an ", "the ", "be " };

        foreach (var raw in clauses)
        {
            var clause = raw.Trim();
            var lower = clause.ToLowerInvariant();

            // Find ALL source families in the first half of the clause
            // (so "grey/blue/steel for X" picks up all three).
            int connectorIdx = -1;
            int connectorLen = 0;
            foreach (var conn in connectors)
            {
                int ci = lower.IndexOf(conn, StringComparison.Ordinal);
                if (ci >= 0 && (connectorIdx < 0 || ci < connectorIdx))
                {
                    connectorIdx = ci; connectorLen = conn.Length;
                }
            }
            if (connectorIdx < 0) continue;

            // Source portion = everything BEFORE the connector
            string sourcePart = lower.Substring(0, connectorIdx);
            var sourcesInClause = FindAllFamilies(sourcePart);
            if (sourcesInClause.Count == 0) continue;

            // Target portion
            string target = clause.Substring(connectorIdx + connectorLen).Trim();
            target = target.TrimEnd('.', ',', ';', ' ', '\t');
            foreach (var lf in leadingFluff)
            {
                if (target.StartsWith(lf, StringComparison.OrdinalIgnoreCase))
                {
                    target = target.Substring(lf.Length).Trim();
                    break;
                }
            }
            if (string.IsNullOrEmpty(target)) continue;

            foreach (var src in sourcesInClause)
                result.Add((src, target));
        }

        return result;
    }

    /// <summary>
    /// Find every known family name in a text snippet, in order, deduped.
    /// Handles plurals ("browns" → "brown") and slash-grouped lists.
    /// </summary>
    private static List<string> FindAllFamilies(string lowerText)
    {
        var found = new List<(int Pos, string Fam)>();
        foreach (var fam in KnownFamilies)
        {
            int searchFrom = 0;
            while (searchFrom < lowerText.Length)
            {
                int idx = lowerText.IndexOf(fam, searchFrom, StringComparison.Ordinal);
                if (idx < 0) break;
                bool leftOk = idx == 0 || !(char.IsLetterOrDigit(lowerText[idx - 1]) || lowerText[idx - 1] == '_');
                int afterIdx = idx + fam.Length;
                bool rightOk;
                if (afterIdx >= lowerText.Length) rightOk = true;
                else if (lowerText[afterIdx] == 's')
                {
                    int afterS = afterIdx + 1;
                    rightOk = afterS >= lowerText.Length || !char.IsLetterOrDigit(lowerText[afterS]);
                }
                else rightOk = !char.IsLetterOrDigit(lowerText[afterIdx]);

                if (leftOk && rightOk) found.Add((idx, fam));
                searchFrom = idx + 1;
            }
        }

        var result = new List<string>();
        var seen = new HashSet<string>();
        foreach (var (_, fam) in found.OrderBy(x => x.Pos))
        {
            string key = fam == "gray" ? "grey" : fam;
            if (seen.Add(key)) result.Add(key);
        }
        return result;
    }

    /// <summary>
    /// Split a sentence on ", " or " and " when the next segment starts a
    /// new "X for Y" clause. Keeps "grey/blue/steel" intact.
    /// </summary>
    private static IEnumerable<string> SplitOnSecondarySwaps(string sentence)
    {
        string[] seps = { ", ", " and ", " AND " };
        var result = new List<string>();
        int start = 0; int i = 0;
        string[] connectors = { " for ", " to ", " with ", " into ", " → ", " => ", "->" };

        while (i < sentence.Length)
        {
            int sepLen = 0;
            foreach (var sep in seps)
            {
                if (i + sep.Length <= sentence.Length &&
                    sentence.Substring(i, sep.Length).Equals(sep, StringComparison.OrdinalIgnoreCase))
                {
                    sepLen = sep.Length; break;
                }
            }
            if (sepLen == 0) { i++; continue; }

            string after = sentence.Substring(i + sepLen);
            string afterLower = after.ToLowerInvariant();

            string[] fluff = { "a more ", "more ", "a ", "an ", "the ", "be ", "make " };
            int probeStart = 0;
            foreach (var lf in fluff)
            {
                if (afterLower.StartsWith(lf, StringComparison.Ordinal)) { probeStart = lf.Length; break; }
            }

            var fams = FindAllFamilies(afterLower.Substring(probeStart, Math.Min(40, afterLower.Length - probeStart)));
            bool startsNew = false;
            if (fams.Count > 0)
            {
                int searchStart = probeStart;
                int maxSearch = Math.Min(searchStart + 60, afterLower.Length);
                foreach (var conn in connectors)
                {
                    int ci = afterLower.IndexOf(conn, searchStart, StringComparison.Ordinal);
                    if (ci >= 0 && ci < maxSearch) { startsNew = true; break; }
                }
            }

            if (startsNew)
            {
                result.Add(sentence.Substring(start, i - start));
                start = i + sepLen; i = start;
            }
            else i++;
        }

        if (start < sentence.Length) result.Add(sentence.Substring(start));
        return result;
    }

    // ═══════════════════════════════════════════════════════════════════
    // STEP 2 — COLOR NAME RESOLUTION (LONGEST MATCH WINS)
    // ═══════════════════════════════════════════════════════════════════

    // Shorthand constructors to keep the dictionary readable
    private static TargetColor TC(float h, float s)
        => new(h, s, LBehavior.Preserve);
    private static TargetColor TCLift(float h, float s, float offset = 0.10f)
        => new(h, s, LBehavior.LiftTo, offset);
    private static TargetColor TCDrop(float h, float s, float offset = 0.10f)
        => new(h, s, LBehavior.DropTo, offset);

    private static readonly Dictionary<string, TargetColor> ColorDictionary = new(StringComparer.OrdinalIgnoreCase)
    {
        // Whites / creams / stones — gentle brightening via small offset, NOT
        // a lerp-to-bright (which compresses the gradient and blows out detail).
        // The brute-force draft stays faithful; Flux polish handles the rest.
        ["white"] = TCLift(0, 0.00f, 0.12f),
        ["whitish"] = TCLift(40, 0.12f, 0.10f),
        ["cream"] = TCLift(35, 0.18f, 0.10f),
        ["ivory"] = TCLift(45, 0.14f, 0.10f),
        ["bone"] = TCLift(40, 0.12f, 0.08f),
        ["stone"] = TCLift(30, 0.10f, 0.06f),
        ["polished stone"] = TCLift(30, 0.10f, 0.08f),
        ["pale"] = TCLift(40, 0.08f, 0.10f),

        // STRONG tonal BODY targets (May 2026). The gentle entries above only
        // nudge brightness; turning mid-grey vanilla steel into a convincing
        // marble-white or obsidian-black BODY needs a big offset. These are the
        // names the Variations tonal schemes emit. Longest-substring match picks
        // these over "marble"/"white"/"obsidian"/"black". Near-zero saturation so
        // the body reads neutral; the offset does the work. Offsets leave some
        // gradient (not a full clamp) so veining/sculpt survives. Tune the offset
        // if marble blows out / obsidian crushes: SpellCreator-side, just here.
        ["marble white"] = TCLift(210, 0.05f, 0.34f),
        ["obsidian black"] = TCDrop(235, 0.06f, 0.42f),
        ["alabaster white"] = TCLift(45, 0.06f, 0.30f),
        ["snow white"] = TCLift(0, 0.00f, 0.38f),
        ["graphite black"] = TCDrop(220, 0.05f, 0.36f),

        // Blacks / dark — gentle darkening offset.
        ["black"] = TCDrop(0, 0.00f, 0.15f),
        ["obsidian"] = TCDrop(235, 0.15f, 0.15f),
        ["onyx"] = TCDrop(240, 0.08f, 0.15f),
        ["charcoal"] = TCDrop(220, 0.05f, 0.10f),

        // Greys / steels — tonal swaps, preserve lightness exactly.
        ["grey"] = TC(210, 0.04f),
        ["gray"] = TC(210, 0.04f),
        ["silver"] = TC(210, 0.04f),
        ["steel"] = TC(210, 0.10f),

        // Blues — chromatic, preserve lightness (so steel→blue keeps gradient).
        ["blue"] = TC(215, 0.70f),
        ["shiny blue"] = TC(215, 0.85f),
        ["azure"] = TC(210, 0.80f),
        ["navy"] = TC(220, 0.60f),
        ["sapphire"] = TC(220, 0.75f),
        ["ice blue"] = TC(200, 0.50f),
        ["frost blue"] = TC(205, 0.55f),
        ["frost"] = TC(200, 0.45f),
        ["cobalt"] = TC(220, 0.80f),
        ["dusk"] = TC(230, 0.45f),
        ["midnight"] = TCDrop(225, 0.55f, 0.12f),

        // Teals / cyans — the steel/cyan zone (H ~175-195). The segmentation
        // descriptor uses "teal" for vanilla steel, and the LLM theming echoes
        // it ("frost teal", "ocean teal", "lunar teal"…). These MUST resolve or
        // the dominant steel material is silently skipped — the exact bug that
        // made every variant render identically. "teal" + common modifiers:
        ["teal"] = TC(185, 0.55f),
        ["cyan"] = TC(185, 0.70f),
        ["aqua"] = TC(180, 0.60f),
        ["ocean"] = TC(195, 0.60f),
        ["ocean teal"] = TC(195, 0.60f),
        ["lunar"] = TC(195, 0.30f),
        ["lunar teal"] = TC(190, 0.35f),
        ["frost teal"] = TC(188, 0.45f),
        ["silt"] = TC(185, 0.25f),
        ["silt teal"] = TC(185, 0.28f),
        ["wet teal"] = TC(190, 0.40f),
        ["mint"] = TC(165, 0.45f),
        ["turquoise"] = TC(175, 0.65f),

        // Reds
        ["red"] = TC(0, 0.75f),
        ["ruby"] = TC(350, 0.80f),
        ["ruby red"] = TC(350, 0.80f),
        ["crimson"] = TC(350, 0.85f),
        ["ember"] = TC(10, 0.80f),
        ["ember red"] = TC(8, 0.82f),
        ["blood"] = TC(355, 0.80f),
        ["blood crimson"] = TC(352, 0.85f),

        // Greens
        ["green"] = TC(120, 0.65f),
        ["emerald"] = TC(140, 0.70f),
        ["forest"] = TC(130, 0.55f),
        ["jade"] = TC(150, 0.50f),
        ["moss"] = TC(95, 0.45f),
        ["fel"] = TC(95, 0.85f),
        ["fel green"] = TC(95, 0.90f),
        ["sickly green"] = TC(85, 0.80f),
        ["blossom"] = TC(110, 0.40f),
        ["veil green"] = TC(135, 0.45f),
        ["damp"] = TC(120, 0.35f),

        // Yellows / golds
        ["yellow"] = TC(55, 0.85f),
        ["gold"] = TC(45, 0.80f),
        ["bronze"] = TC(35, 0.65f),
        ["amber"] = TC(40, 0.75f),
        ["sulfur"] = TC(55, 0.70f),
        ["molten"] = TC(30, 0.90f),
        ["molten gold"] = TC(42, 0.85f),
        ["gilded"] = TC(46, 0.78f),
        ["moonlit"] = TCLift(50, 0.40f, 0.08f),
        ["dewy"] = TC(52, 0.55f),
        ["straw"] = TC(48, 0.40f),
        ["pale gold"] = TCLift(48, 0.55f, 0.08f),

        // Purples
        ["purple"] = TC(280, 0.65f),
        ["violet"] = TC(270, 0.70f),
        ["royal"] = TC(260, 0.70f),
        ["void"] = TCDrop(285, 0.65f, 0.10f),
        ["void purple"] = TCDrop(283, 0.68f, 0.10f),
        ["wisteria"] = TC(275, 0.40f),
        ["lilac"] = TCLift(285, 0.35f, 0.06f),
        ["magenta"] = TC(315, 0.70f),

        // Oranges / browns
        ["orange"] = TC(25, 0.85f),
        ["brown"] = TC(28, 0.55f),
        ["copper"] = TC(20, 0.70f),
        ["rust"] = TC(15, 0.65f),
        ["leather"] = TC(28, 0.40f),
        ["ashen"] = TC(30, 0.15f),
        ["ashen brown"] = TC(28, 0.25f),
        ["dusty"] = TC(30, 0.30f),
        ["dusty brown"] = TC(28, 0.35f),
        ["oak"] = TC(30, 0.45f),
        ["oak brown"] = TC(30, 0.48f),
        ["smolder"] = TCDrop(20, 0.55f, 0.08f),
        ["smolder brown"] = TCDrop(22, 0.55f, 0.08f),
        ["char"] = TCDrop(25, 0.20f, 0.12f),
        ["char black"] = TCDrop(25, 0.15f, 0.14f),
        ["cinder"] = TCDrop(18, 0.45f, 0.10f),

        // ═══════════════════════════════════════════════════════════════
        // GENERATED COLOR TABLE — broad CSS/X11 + fantasy vocabulary so the
        // LLM's creative color names resolve instead of reverting a unit to
        // its original color. Longest-substring-match means multi-word names
        // like "deep burgundy" resolve via "burgundy". TC=preserve lightness,
        // TCLift/TCDrop=gentle brighten/darken for very light/dark targets.
        // ═══════════════════════════════════════════════════════════════
        // fantasy / evocative
        ["slate"] = TC(216, 0.15f),
        ["ash"] = TC(36, 0.02f),
        ["ivory white"] = TCLift(60, 1.0f, 0.10f),
        ["marble"] = TCLift(45, 0.21f, 0.10f),
        ["alabaster"] = TCLift(48, 0.33f, 0.10f),
        ["pearl"] = TCLift(46, 0.28f, 0.10f),
        ["frostblue"] = TC(205, 0.68f),
        ["ice"] = TCLift(193, 0.69f, 0.10f),
        ["glacier"] = TC(198, 0.62f),
        ["arctic"] = TCLift(197, 0.64f, 0.10f),
        ["azure sky"] = TC(209, 0.77f),
        ["cerulean"] = TC(224, 0.64f),
        ["twilight"] = TC(252, 0.29f),
        ["indigo"] = TC(275, 1.0f),
        ["amethyst"] = TC(270, 0.5f),
        ["orchid"] = TC(289, 0.5f),
        ["plum"] = TC(315, 0.4f),
        ["mauve"] = TC(315, 0.21f),
        ["lavender"] = TC(264, 0.42f),
        ["royal purple"] = TC(267, 0.58f),
        ["shadow"] = TCDrop(240, 0.11f, 0.10f),
        ["olive"] = TC(60, 0.47f),
        ["sage"] = TC(86, 0.16f),
        ["verdant"] = TC(131, 0.58f),
        ["fern"] = TC(120, 0.3f),
        ["toxic"] = TC(85, 0.68f),
        ["venom"] = TC(94, 0.58f),
        ["lime"] = TC(93, 0.72f),
        ["seafoam"] = TC(163, 0.44f),
        ["abyss"] = TCDrop(208, 0.68f, 0.10f),
        ["goldenrod"] = TC(42, 0.72f),
        ["honey"] = TC(43, 0.72f),
        ["brass"] = TC(44, 0.46f),
        ["ochre"] = TC(36, 0.6f),
        ["sepia"] = TC(34, 0.33f),
        ["tan"] = TC(34, 0.44f),
        ["sand"] = TC(40, 0.56f),
        ["wheat"] = TC(43, 0.58f),
        ["mahogany"] = TC(15, 0.5f),
        ["walnut"] = TC(27, 0.38f),
        ["chestnut"] = TC(20, 0.47f),
        ["umber"] = TC(27, 0.33f),
        ["scarlet"] = TC(3, 0.76f),
        ["blood red"] = TC(355, 0.76f),
        ["burgundy"] = TC(349, 0.57f),
        ["maroon"] = TC(353, 0.63f),
        ["sanguine"] = TC(352, 0.7f),
        ["vermilion"] = TC(6, 0.76f),
        ["flame"] = TC(18, 0.8f),
        ["pyro"] = TC(16, 0.88f),
        ["magma"] = TC(13, 0.75f),
        ["lava"] = TC(11, 0.8f),
        ["inferno"] = TC(9, 0.8f),
        ["coral"] = TC(16, 1.0f),
        ["salmon"] = TC(14, 0.93f),
        ["peach"] = TCLift(25, 1.0f, 0.10f),
        ["apricot"] = TC(28, 0.93f),
        ["rose"] = TC(350, 0.63f),
        ["blush"] = TC(352, 0.73f),
        ["pink"] = TCLift(341, 1.0f, 0.10f),
        ["fuchsia"] = TC(325, 0.77f),
        ["hotpink"] = TC(333, 1.0f),
        ["cerise"] = TC(337, 0.72f),
        ["wine"] = TC(340, 0.6f),
        ["gunmetal"] = TC(210, 0.11f),
        ["iron"] = TC(220, 0.07f),
        ["pewter"] = TC(210, 0.05f),
        ["platinum"] = TCLift(216, 0.08f, 0.10f),
        ["chrome"] = TCLift(210, 0.13f, 0.10f),
        ["titanium"] = TC(210, 0.07f),
        ["smoke"] = TC(240, 0.02f),
        ["storm"] = TC(216, 0.12f),
        ["granite"] = TC(60, 0.01f),
        ["moonblue"] = TC(214, 0.5f),
        ["moonlight"] = TCLift(228, 0.38f, 0.10f),
        ["starlight"] = TCLift(228, 0.33f, 0.10f),
        ["holy"] = TCLift(49, 1.0f, 0.10f),
        ["radiant"] = TCLift(51, 1.0f, 0.10f),
        ["divine"] = TCLift(48, 1.0f, 0.10f),
        ["celestial"] = TCLift(212, 0.76f, 0.10f),
        ["corrupted"] = TC(280, 0.23f),
        ["necrotic"] = TC(90, 0.22f),
        ["plague"] = TC(73, 0.43f),
        ["decay"] = TC(48, 0.29f),
        ["frostfire"] = TC(210, 0.67f),
        ["arcane"] = TC(265, 0.57f),
        ["runic"] = TC(213, 0.5f),
        // CSS / X11 named
        ["aliceblue"] = TCLift(208, 1.0f, 0.10f),
        ["antiquewhite"] = TCLift(34, 0.78f, 0.10f),
        ["aquamarine"] = TC(160, 1.0f),
        ["beige"] = TCLift(60, 0.56f, 0.10f),
        ["bisque"] = TCLift(33, 1.0f, 0.10f),
        ["blanchedalmond"] = TCLift(36, 1.0f, 0.10f),
        ["blueviolet"] = TC(271, 0.76f),
        ["burlywood"] = TC(34, 0.57f),
        ["cadetblue"] = TC(182, 0.25f),
        ["chartreuse"] = TC(90, 1.0f),
        ["chocolate"] = TC(25, 0.75f),
        ["cornflowerblue"] = TC(219, 0.79f),
        ["cornsilk"] = TCLift(48, 1.0f, 0.10f),
        ["darkblue"] = TC(240, 1.0f),
        ["darkcyan"] = TC(180, 1.0f),
        ["darkgoldenrod"] = TC(43, 0.89f),
        ["darkgray"] = TC(0, 0.0f),
        ["darkgreen"] = TCDrop(120, 1.0f, 0.10f),
        ["darkkhaki"] = TC(56, 0.38f),
        ["darkmagenta"] = TC(300, 1.0f),
        ["darkolivegreen"] = TC(82, 0.39f),
        ["darkorange"] = TC(33, 1.0f),
        ["darkorchid"] = TC(280, 0.61f),
        ["darkred"] = TC(0, 1.0f),
        ["darksalmon"] = TC(15, 0.72f),
        ["darkseagreen"] = TC(120, 0.25f),
        ["darkslateblue"] = TC(248, 0.39f),
        ["darkslategray"] = TC(180, 0.25f),
        ["darkturquoise"] = TC(181, 1.0f),
        ["darkviolet"] = TC(282, 1.0f),
        ["deeppink"] = TC(328, 1.0f),
        ["deepskyblue"] = TC(195, 1.0f),
        ["dimgray"] = TC(0, 0.0f),
        ["dodgerblue"] = TC(210, 1.0f),
        ["firebrick"] = TC(0, 0.68f),
        ["floralwhite"] = TCLift(40, 1.0f, 0.10f),
        ["forestgreen"] = TC(120, 0.61f),
        ["gainsboro"] = TCLift(0, 0.0f, 0.10f),
        ["ghostwhite"] = TCLift(240, 1.0f, 0.10f),
        ["greenyellow"] = TC(84, 1.0f),
        ["honeydew"] = TCLift(120, 1.0f, 0.10f),
        ["indianred"] = TC(0, 0.53f),
        ["khaki"] = TC(54, 0.77f),
        ["lavenderblush"] = TCLift(340, 1.0f, 0.10f),
        ["lawngreen"] = TC(90, 1.0f),
        ["lemonchiffon"] = TCLift(54, 1.0f, 0.10f),
        ["lightblue"] = TC(195, 0.53f),
        ["lightcoral"] = TC(0, 0.79f),
        ["lightcyan"] = TCLift(180, 1.0f, 0.10f),
        ["lightgoldenrodyellow"] = TCLift(60, 0.8f, 0.10f),
        ["lightgreen"] = TC(120, 0.73f),
        ["lightgray"] = TCLift(0, 0.0f, 0.10f),
        ["lightpink"] = TCLift(351, 1.0f, 0.10f),
        ["lightsalmon"] = TC(17, 1.0f),
        ["lightseagreen"] = TC(177, 0.7f),
        ["lightskyblue"] = TC(203, 0.92f),
        ["lightslategray"] = TC(210, 0.14f),
        ["lightsteelblue"] = TC(214, 0.41f),
        ["lightyellow"] = TCLift(60, 1.0f, 0.10f),
        ["limegreen"] = TC(120, 0.61f),
        ["linen"] = TCLift(30, 0.67f, 0.10f),
        ["mediumaquamarine"] = TC(160, 0.51f),
        ["mediumblue"] = TC(240, 1.0f),
        ["mediumorchid"] = TC(288, 0.59f),
        ["mediumpurple"] = TC(260, 0.6f),
        ["mediumseagreen"] = TC(147, 0.5f),
        ["mediumslateblue"] = TC(249, 0.8f),
        ["mediumspringgreen"] = TC(157, 1.0f),
        ["mediumturquoise"] = TC(178, 0.6f),
        ["mediumvioletred"] = TC(322, 0.81f),
        ["midnightblue"] = TC(240, 0.64f),
        ["mintcream"] = TCLift(150, 1.0f, 0.10f),
        ["mistyrose"] = TCLift(6, 1.0f, 0.10f),
        ["moccasin"] = TCLift(38, 1.0f, 0.10f),
        ["navajowhite"] = TCLift(36, 1.0f, 0.10f),
        ["oldlace"] = TCLift(39, 0.85f, 0.10f),
        ["olivedrab"] = TC(80, 0.6f),
        ["orangered"] = TC(16, 1.0f),
        ["palegoldenrod"] = TCLift(55, 0.67f, 0.10f),
        ["palegreen"] = TC(120, 0.93f),
        ["paleturquoise"] = TCLift(180, 0.65f, 0.10f),
        ["palevioletred"] = TC(340, 0.6f),
        ["papayawhip"] = TCLift(37, 1.0f, 0.10f),
        ["peachpuff"] = TCLift(28, 1.0f, 0.10f),
        ["peru"] = TC(30, 0.59f),
        ["powderblue"] = TCLift(187, 0.52f, 0.10f),
        ["rebeccapurple"] = TC(270, 0.5f),
        ["rosybrown"] = TC(0, 0.25f),
        ["royalblue"] = TC(225, 0.73f),
        ["saddlebrown"] = TC(25, 0.76f),
        ["sandybrown"] = TC(28, 0.87f),
        ["seagreen"] = TC(146, 0.5f),
        ["seashell"] = TCLift(25, 1.0f, 0.10f),
        ["sienna"] = TC(19, 0.56f),
        ["skyblue"] = TC(197, 0.71f),
        ["slateblue"] = TC(248, 0.53f),
        ["slategray"] = TC(210, 0.13f),
        ["snow"] = TCLift(0, 1.0f, 0.10f),
        ["springgreen"] = TC(150, 1.0f),
        ["steelblue"] = TC(207, 0.44f),
        ["thistle"] = TCLift(300, 0.24f, 0.10f),
        ["tomato"] = TC(9, 1.0f),
        ["whitesmoke"] = TCLift(0, 0.0f, 0.10f),
        ["yellowgreen"] = TC(80, 0.61f),

        // ═══════════════════════════════════════════════════════════════
        // GENERATED MASSIVE COLOR TABLE (~1000+ names): XKCD color survey +
        // CSS4 + fantasy/game vocabulary, so the LLM's color names resolve
        // locally instead of needing a round-trip or reverting a unit to its
        // original color. Longest-substring-match resolves compounds; entries
        // already defined above are skipped (no duplicate keys).
        // ═══════════════════════════════════════════════════════════════
        // fantasy / game
        ["emberglow"] = TC(17, 0.72f),
        ["tarnished"] = TC(45, 0.16f),
        ["shadowsteel"] = TC(216, 0.15f),
        ["dragonscale"] = TC(156, 0.38f),
        ["infernal"] = TC(7, 0.8f),
        ["spectral"] = TC(192, 0.42f),
        ["phantom"] = TC(216, 0.14f),
        // XKCD color survey
        ["cloudy blue"] = TC(211, 0.37f),
        ["dark pastel green"] = TC(121, 0.35f),
        ["dust"] = TC(38, 0.31f),
        ["electric lime"] = TC(81, 1.0f),
        ["fresh green"] = TC(109, 0.64f),
        ["light eggplant"] = TC(304, 0.33f),
        ["nasty green"] = TC(94, 0.48f),
        ["really light blue"] = TCLift(180, 1.0f, 0.10f),
        ["tea"] = TC(140, 0.29f),
        ["warm purple"] = TC(303, 0.53f),
        ["yellowish tan"] = TC(60, 0.95f),
        ["cement"] = TC(54, 0.1f),
        ["dark grass green"] = TC(95, 0.94f),
        ["dusty teal"] = TC(170, 0.31f),
        ["grey teal"] = TC(163, 0.24f),
        ["macaroni and cheese"] = TC(41, 0.85f),
        ["pinkish tan"] = TC(17, 0.53f),
        ["spruce"] = TC(152, 0.81f),
        ["strong blue"] = TC(241, 0.95f),
        ["toxic green"] = TC(102, 0.73f),
        ["windows blue"] = TC(211, 0.55f),
        ["blue blue"] = TC(228, 0.71f),
        ["blue with a hint of purple"] = TC(250, 0.55f),
        ["booger"] = TC(73, 0.5f),
        ["bright sea green"] = TC(159, 1.0f),
        ["dark green blue"] = TC(169, 0.52f),
        ["deep turquoise"] = TC(181, 0.98f),
        ["green teal"] = TC(158, 0.88f),
        ["strong pink"] = TC(329, 1.0f),
        ["bland"] = TC(48, 0.18f),
        ["deep aqua"] = TC(184, 0.88f),
        ["lavender pink"] = TC(304, 0.56f),
        ["light moss green"] = TC(85, 0.43f),
        ["light seafoam green"] = TCLift(130, 1.0f, 0.10f),
        ["olive yellow"] = TC(56, 0.91f),
        ["pig pink"] = TC(344, 0.65f),
        ["deep lilac"] = TC(270, 0.37f),
        ["desert"] = TC(43, 0.51f),
        ["dusty lavender"] = TC(306, 0.19f),
        ["purpley grey"] = TC(300, 0.09f),
        ["purply"] = TC(286, 0.48f),
        ["candy pink"] = TC(308, 1.0f),
        ["light pastel green"] = TCLift(111, 0.91f, 0.10f),
        ["boring green"] = TC(121, 0.34f),
        ["kiwi green"] = TC(91, 0.76f),
        ["light grey green"] = TC(99, 0.52f),
        ["orange pink"] = TC(10, 1.0f),
        ["tea green"] = TCLift(102, 0.86f, 0.10f),
        ["very light brown"] = TC(38, 0.48f),
        ["egg shell"] = TCLift(57, 1.0f, 0.10f),
        ["eggplant purple"] = TCDrop(302, 0.86f, 0.10f),
        ["powder pink"] = TCLift(337, 1.0f, 0.10f),
        ["reddish grey"] = TC(7, 0.17f),
        ["baby shit brown"] = TC(49, 0.86f),
        ["liliac"] = TC(269, 0.97f),
        ["stormy blue"] = TC(206, 0.32f),
        ["ugly brown"] = TC(54, 0.95f),
        ["custard"] = TC(59, 1.0f),
        ["darkish pink"] = TC(338, 0.67f),
        ["deep brown"] = TCDrop(2, 1.0f, 0.10f),
        ["greenish beige"] = TC(65, 0.49f),
        ["manilla"] = TC(58, 1.0f),
        ["off blue"] = TC(209, 0.35f),
        ["battleship grey"] = TC(201, 0.11f),
        ["browny green"] = TC(58, 0.83f),
        ["bruise"] = TC(313, 0.33f),
        ["kelley green"] = TC(142, 1.0f),
        ["sickly yellow"] = TC(66, 0.78f),
        ["sunny yellow"] = TC(58, 1.0f),
        ["azul"] = TC(221, 0.84f),
        ["green yellow"] = TC(68, 0.93f),
        ["lichen"] = TC(100, 0.29f),
        ["light light green"] = TCLift(102, 1.0f, 0.10f),
        ["sun yellow"] = TC(51, 1.0f),
        ["tan green"] = TC(76, 0.38f),
        ["burple"] = TC(258, 0.76f),
        ["butterscotch"] = TC(35, 0.98f),
        ["toupe"] = TC(38, 0.4f),
        ["dark cream"] = TCLift(53, 1.0f, 0.10f),
        ["indian red"] = TC(5, 0.94f),
        ["light lavendar"] = TCLift(285, 0.97f, 0.10f),
        ["poison green"] = TC(109, 0.98f),
        ["baby puke green"] = TC(64, 0.94f),
        ["bright yellow green"] = TC(83, 1.0f),
        ["charcoal grey"] = TC(190, 0.05f),
        ["squash"] = TC(41, 0.89f),
        ["cinnamon"] = TC(26, 0.93f),
        ["light pea green"] = TC(88, 0.98f),
        ["radioactive green"] = TC(116, 0.96f),
        ["raw sienna"] = TC(38, 1.0f),
        ["baby purple"] = TC(271, 0.85f),
        ["cocoa"] = TC(25, 0.34f),
        ["light royal blue"] = TC(243, 0.99f),
        ["orangeish"] = TC(23, 0.98f),
        ["rust brown"] = TC(20, 0.96f),
        ["sand brown"] = TC(39, 0.51f),
        ["swamp"] = TC(81, 0.39f),
        ["tealish green"] = TC(150, 0.9f),
        ["burnt siena"] = TC(26, 0.97f),
        ["camo"] = TC(75, 0.29f),
        ["dusk blue"] = TC(214, 0.58f),
        ["old rose"] = TC(352, 0.4f),
        ["pale light green"] = TC(105, 0.94f),
        ["peachy pink"] = TC(8, 1.0f),
        ["rosy pink"] = TC(344, 0.89f),
        ["light bluish green"] = TC(142, 0.97f),
        ["light bright green"] = TC(123, 0.99f),
        ["light neon green"] = TC(122, 0.98f),
        ["light seafoam"] = TCLift(140, 0.98f, 0.10f),
        ["tiffany blue"] = TC(168, 0.82f),
        ["washed out green"] = TCLift(103, 0.8f, 0.10f),
        ["browny orange"] = TC(32, 0.98f),
        ["nice blue"] = TC(200, 0.83f),
        ["greyish teal"] = TC(162, 0.19f),
        ["orangey yellow"] = TC(42, 0.98f),
        ["parchment"] = TCLift(58, 0.98f, 0.10f),
        ["very dark brown"] = TCDrop(4, 1.0f, 0.10f),
        ["terracota"] = TC(16, 0.57f),
        ["ugly blue"] = TC(204, 0.48f),
        ["clear blue"] = TC(216, 0.98f),
        ["creme"] = TCLift(60, 1.0f, 0.10f),
        ["foam green"] = TC(134, 0.96f),
        ["grey green"] = TC(105, 0.16f),
        ["light gold"] = TC(48, 0.98f),
        ["seafoam blue"] = TC(162, 0.49f),
        ["topaz"] = TC(176, 0.82f),
        ["violet pink"] = TC(300, 0.96f),
        ["wintergreen"] = TC(148, 0.95f),
        ["yellow tan"] = TC(48, 1.0f),
        ["dark fuchsia"] = TC(327, 0.91f),
        ["indigo blue"] = TC(253, 0.76f),
        ["light yellowish green"] = TC(91, 1.0f),
        ["pale magenta"] = TC(322, 0.58f),
        ["rich purple"] = TC(314, 1.0f),
        ["sunflower yellow"] = TC(51, 1.0f),
        ["green blue"] = TC(164, 0.99f),
        ["racing green"] = TCDrop(119, 1.0f, 0.10f),
        ["vivid purple"] = TC(277, 1.0f),
        ["dark royal blue"] = TC(238, 0.96f),
        ["hazel"] = TC(48, 0.71f),
        ["muted pink"] = TC(344, 0.5f),
        ["booger green"] = TC(70, 0.97f),
        ["canary"] = TC(61, 1.0f),
        ["cool grey"] = TC(191, 0.09f),
        ["dark taupe"] = TC(32, 0.24f),
        ["darkish purple"] = TC(301, 0.65f),
        ["true green"] = TC(118, 0.95f),
        ["coral pink"] = TC(359, 1.0f),
        ["dark sage"] = TC(116, 0.21f),
        ["dark slate blue"] = TC(204, 0.49f),
        ["flat blue"] = TC(209, 0.47f),
        ["mushroom"] = TC(26, 0.27f),
        ["rich blue"] = TC(234, 0.98f),
        ["dirty purple"] = TC(320, 0.22f),
        ["greenblue"] = TC(159, 0.7f),
        ["icky green"] = TC(73, 0.67f),
        ["light khaki"] = TC(69, 0.75f),
        ["warm blue"] = TC(235, 0.67f),
        ["dark hot pink"] = TC(332, 0.99f),
        ["deep sea blue"] = TC(201, 0.98f),
        ["carmine"] = TC(352, 0.97f),
        ["dark yellow green"] = TC(72, 0.97f),
        ["pale peach"] = TCLift(41, 1.0f, 0.10f),
        ["plum purple"] = TCDrop(298, 0.88f, 0.10f),
        ["golden rod"] = TC(45, 0.95f),
        ["neon red"] = TC(348, 1.0f),
        ["old pink"] = TC(350, 0.41f),
        ["very pale blue"] = TCLift(179, 1.0f, 0.10f),
        ["blood orange"] = TC(17, 0.99f),
        ["grapefruit"] = TC(1, 0.98f),
        ["sand yellow"] = TC(49, 0.96f),
        ["clay brown"] = TC(27, 0.49f),
        ["dark blue grey"] = TC(203, 0.43f),
        ["flat green"] = TC(99, 0.35f),
        ["light green blue"] = TC(147, 0.97f),
        ["warm pink"] = TC(344, 0.95f),
        ["dodger blue"] = TC(219, 0.97f),
        ["gross green"] = TC(71, 0.79f),
        ["metallic blue"] = TC(206, 0.29f),
        ["pale salmon"] = TCLift(14, 1.0f, 0.10f),
        ["sap green"] = TC(84, 0.74f),
        ["algae"] = TC(134, 0.35f),
        ["bluey grey"] = TC(205, 0.2f),
        ["greeny grey"] = TC(114, 0.17f),
        ["highlighter green"] = TC(115, 0.98f),
        ["light light blue"] = TCLift(175, 1.0f, 0.10f),
        ["light mint"] = TCLift(124, 1.0f, 0.10f),
        ["raw umber"] = TC(32, 0.9f),
        ["vivid blue"] = TC(234, 1.0f),
        ["deep lavender"] = TC(272, 0.38f),
        ["dull teal"] = TC(166, 0.25f),
        ["light greenish blue"] = TC(153, 0.9f),
        ["mud green"] = TCDrop(64, 0.96f, 0.10f),
        ["pinky"] = TC(342, 0.95f),
        ["red wine"] = TC(338, 1.0f),
        ["shit green"] = TC(65, 1.0f),
        ["tan brown"] = TC(32, 0.38f),
        ["rosa"] = TC(345, 0.98f),
        ["lipstick"] = TC(343, 0.81f),
        ["pale mauve"] = TCLift(303, 0.96f, 0.10f),
        ["claret"] = TCDrop(346, 1.0f, 0.10f),
        ["dandelion"] = TC(52, 0.99f),
        ["poop green"] = TC(66, 1.0f),
        ["dark"] = TCDrop(215, 0.29f, 0.10f),
        ["greenish turquoise"] = TC(162, 1.0f),
        ["pastel red"] = TC(1, 0.65f),
        ["piss yellow"] = TC(58, 0.8f),
        ["bright cyan"] = TC(180, 0.99f),
        ["dark coral"] = TC(2, 0.57f),
        ["algae green"] = TC(149, 0.71f),
        ["darkish red"] = TC(358, 0.97f),
        ["reddy brown"] = TC(6, 0.91f),
        ["blush pink"] = TC(355, 0.98f),
        ["camouflage green"] = TC(77, 0.67f),
        ["lawn green"] = TC(94, 0.9f),
        ["putty"] = TC(42, 0.29f),
        ["vibrant blue"] = TC(227, 0.98f),
        ["dark sand"] = TC(41, 0.31f),
        ["purple blue"] = TC(261, 0.73f),
        ["saffron"] = TC(41, 0.99f),
        ["warm brown"] = TC(31, 0.97f),
        ["bluegrey"] = TC(200, 0.23f),
        ["bubble gum pink"] = TC(332, 1.0f),
        ["duck egg blue"] = TCLift(173, 0.88f, 0.10f),
        ["greenish cyan"] = TC(160, 0.99f),
        ["petrol"] = TC(186, 1.0f),
        ["butter"] = TC(60, 1.0f),
        ["dusty orange"] = TC(24, 0.86f),
        ["off yellow"] = TC(61, 0.88f),
        ["pale olive green"] = TC(83, 0.49f),
        ["orangish"] = TC(19, 0.97f),
        ["leaf"] = TC(89, 0.53f),
        ["light blue grey"] = TCLift(215, 0.43f, 0.10f),
        ["dried blood"] = TCDrop(0, 0.97f, 0.10f),
        ["lightish purple"] = TC(274, 0.75f),
        ["rusty red"] = TC(13, 0.86f),
        ["lavender blue"] = TC(242, 0.89f),
        ["light grass green"] = TC(98, 0.9f),
        ["light mint green"] = TCLift(128, 0.91f, 0.10f),
        ["sunflower"] = TC(45, 1.0f),
        ["velvet"] = TC(320, 0.87f),
        ["brick orange"] = TC(21, 0.91f),
        ["lightish red"] = TC(352, 0.99f),
        ["pure blue"] = TC(240, 0.98f),
        ["twilight blue"] = TC(209, 0.85f),
        ["violet red"] = TC(329, 1.0f),
        ["yellowy brown"] = TC(47, 0.87f),
        ["carnation"] = TC(350, 0.97f),
        ["muddy yellow"] = TC(54, 0.95f),
        ["dark seafoam green"] = TC(150, 0.48f),
        ["deep rose"] = TC(345, 0.53f),
        ["dusty red"] = TC(357, 0.45f),
        ["grey blue"] = TC(204, 0.17f),
        ["lemon lime"] = TC(78, 0.99f),
        ["purple pink"] = TC(298, 0.74f),
        ["brown yellow"] = TC(51, 0.95f),
        ["purple brown"] = TC(353, 0.28f),
        ["banana yellow"] = TC(61, 0.99f),
        ["lipstick red"] = TC(346, 0.98f),
        ["water blue"] = TC(202, 0.87f),
        ["brown grey"] = TC(45, 0.15f),
        ["vibrant purple"] = TC(287, 0.97f),
        ["baby green"] = TC(129, 1.0f),
        ["barf green"] = TC(68, 0.98f),
        ["eggshell blue"] = TCLift(172, 1.0f, 0.10f),
        ["sandy yellow"] = TC(53, 0.97f),
        ["cool green"] = TC(142, 0.57f),
        ["blue grey"] = TC(209, 0.2f),
        ["hot magenta"] = TC(311, 0.97f),
        ["greyblue"] = TC(199, 0.3f),
        ["purpley"] = TC(261, 0.72f),
        ["baby shit green"] = TC(67, 0.74f),
        ["brownish pink"] = TC(4, 0.37f),
        ["dark aquamarine"] = TC(179, 0.98f),
        ["diarrhea"] = TC(49, 0.96f),
        ["light mustard"] = TC(46, 0.9f),
        ["pale sky blue"] = TCLift(187, 0.97f, 0.10f),
        ["turtle green"] = TC(98, 0.43f),
        ["bright olive"] = TC(70, 0.96f),
        ["dark grey blue"] = TC(205, 0.38f),
        ["greeny brown"] = TC(55, 0.89f),
        ["lemon green"] = TC(78, 0.98f),
        ["light periwinkle"] = TCLift(235, 0.91f, 0.10f),
        ["seaweed green"] = TC(147, 0.53f),
        ["sunshine yellow"] = TC(59, 1.0f),
        ["ugly purple"] = TC(302, 0.43f),
        ["medium pink"] = TC(338, 0.86f),
        ["puke brown"] = TC(48, 0.92f),
        ["very light pink"] = TCLift(9, 1.0f, 0.10f),
        ["viridian"] = TC(158, 0.66f),
        ["bile"] = TC(64, 0.94f),
        ["faded yellow"] = TC(60, 1.0f),
        ["very pale green"] = TCLift(102, 0.94f, 0.10f),
        ["vibrant green"] = TC(119, 0.93f),
        ["bright lime"] = TC(89, 0.98f),
        ["spearmint"] = TC(144, 0.94f),
        ["light aquamarine"] = TC(155, 0.97f),
        ["light sage"] = TCLift(105, 0.63f, 0.10f),
        ["baby poo"] = TC(50, 0.95f),
        ["dark seafoam"] = TC(156, 0.71f),
        ["deep teal"] = TCDrop(183, 1.0f, 0.10f),
        ["heather"] = TC(288, 0.19f),
        ["rust orange"] = TC(25, 0.92f),
        ["dirty blue"] = TC(197, 0.43f),
        ["fern green"] = TC(107, 0.35f),
        ["bright lilac"] = TC(281, 0.95f),
        ["weird green"] = TC(144, 0.77f),
        ["peacock blue"] = TC(199, 0.99f),
        ["avocado green"] = TC(75, 0.67f),
        ["faded orange"] = TC(26, 0.84f),
        ["grape purple"] = TC(310, 0.65f),
        ["hot green"] = TC(121, 1.0f),
        ["lime yellow"] = TC(72, 0.99f),
        ["mango"] = TC(35, 1.0f),
        ["shamrock"] = TC(145, 0.99f),
        ["bubblegum"] = TC(330, 1.0f),
        ["purplish brown"] = TC(353, 0.24f),
        ["vomit yellow"] = TC(58, 0.89f),
        ["pale cyan"] = TCLift(176, 1.0f, 0.10f),
        ["key lime"] = TC(94, 1.0f),
        ["tomato red"] = TC(11, 0.99f),
        ["merlot"] = TC(330, 1.0f),
        ["night blue"] = TCDrop(241, 0.92f, 0.10f),
        ["purpleish pink"] = TC(310, 0.69f),
        ["apple"] = TC(99, 0.58f),
        ["baby poop green"] = TC(64, 0.94f),
        ["green apple"] = TC(100, 0.75f),
        ["heliotrope"] = TC(290, 0.89f),
        ["yellow green"] = TC(77, 0.98f),
        ["almost black"] = TCDrop(180, 0.3f, 0.10f),
        ["cool blue"] = TC(208, 0.44f),
        ["leafy green"] = TC(109, 0.51f),
        ["mustard brown"] = TC(44, 0.95f),
        ["dull brown"] = TC(35, 0.29f),
        ["frog green"] = TC(93, 0.92f),
        ["vivid green"] = TC(112, 0.87f),
        ["bright light green"] = TC(131, 0.99f),
        ["fluro green"] = TC(118, 1.0f),
        ["kiwi"] = TC(89, 0.84f),
        ["seaweed"] = TC(152, 0.79f),
        ["navy green"] = TCDrop(85, 0.78f, 0.10f),
        ["ultramarine blue"] = TC(245, 0.96f),
        ["iris"] = TC(246, 0.48f),
        ["pastel orange"] = TC(24, 1.0f),
        ["yellowish orange"] = TC(39, 1.0f),
        ["perrywinkle"] = TC(242, 0.65f),
        ["tealish"] = TC(172, 0.68f),
        ["dark plum"] = TCDrop(318, 0.97f, 0.10f),
        ["pear"] = TC(78, 0.92f),
        ["pinkish orange"] = TC(13, 1.0f),
        ["midnight purple"] = TCDrop(283, 0.96f, 0.10f),
        ["light urple"] = TC(270, 0.88f),
        ["dark mint"] = TC(141, 0.49f),
        ["greenish tan"] = TC(71, 0.44f),
        ["light burgundy"] = TC(345, 0.44f),
        ["turquoise blue"] = TC(186, 0.94f),
        ["ugly pink"] = TC(350, 0.47f),
        ["sandy"] = TC(48, 0.81f),
        ["electric pink"] = TC(327, 1.0f),
        ["muted purple"] = TC(290, 0.19f),
        ["mid green"] = TC(114, 0.4f),
        ["greyish"] = TC(47, 0.1f),
        ["neon yellow"] = TC(71, 1.0f),
        ["banana"] = TC(60, 1.0f),
        ["carnation pink"] = TC(341, 1.0f),
        ["sea"] = TC(175, 0.44f),
        ["muddy brown"] = TC(45, 0.92f),
        ["turquoise green"] = TC(153, 0.97f),
        ["buff"] = TCLift(55, 0.98f, 0.10f),
        ["fawn"] = TC(37, 0.47f),
        ["muted blue"] = TC(208, 0.46f),
        ["pale rose"] = TCLift(356, 0.94f, 0.10f),
        ["dark mint green"] = TC(151, 0.71f),
        ["blue green"] = TC(174, 0.82f),
        ["sick green"] = TC(72, 0.62f),
        ["pea"] = TC(70, 0.71f),
        ["rusty orange"] = TC(24, 0.92f),
        ["rose red"] = TC(341, 0.99f),
        ["pale aqua"] = TCLift(163, 1.0f, 0.10f),
        ["deep orange"] = TC(21, 0.99f),
        ["earth"] = TC(23, 0.45f),
        ["mossy green"] = TC(84, 0.56f),
        ["grassy green"] = TC(96, 0.96f),
        ["pale lime green"] = TC(90, 1.0f),
        ["light grey blue"] = TC(206, 0.39f),
        ["pale grey"] = TCLift(240, 0.33f, 0.10f),
        ["asparagus"] = TC(97, 0.34f),
        ["blueberry"] = TC(244, 0.4f),
        ["purple red"] = TC(332, 0.99f),
        ["pale lime"] = TC(87, 0.97f),
        ["greenish teal"] = TC(155, 0.59f),
        ["caramel"] = TC(37, 0.9f),
        ["deep magenta"] = TC(326, 0.98f),
        ["light peach"] = TCLift(30, 1.0f, 0.10f),
        ["milk chocolate"] = TC(30, 0.62f),
        ["ocher"] = TC(48, 0.88f),
        ["off green"] = TC(102, 0.33f),
        ["purply pink"] = TC(305, 0.8f),
        ["dusky blue"] = TC(221, 0.35f),
        ["golden"] = TC(47, 0.98f),
        ["light beige"] = TCLift(59, 1.0f, 0.10f),
        ["butter yellow"] = TC(59, 1.0f),
        ["dusky purple"] = TC(318, 0.2f),
        ["french blue"] = TC(217, 0.44f),
        ["ugly yellow"] = TC(56, 0.99f),
        ["greeny yellow"] = TC(73, 0.94f),
        ["orangish red"] = TC(12, 0.96f),
        ["shamrock green"] = TC(144, 0.98f),
        ["orangish brown"] = TC(32, 0.97f),
        ["tree green"] = TC(110, 0.67f),
        ["deep violet"] = TCDrop(301, 0.85f, 0.10f),
        ["blue purple"] = TC(262, 0.95f),
        ["cherry"] = TC(345, 0.98f),
        ["sandy brown"] = TC(42, 0.46f),
        ["warm grey"] = TC(19, 0.08f),
        ["dark indigo"] = TCDrop(258, 0.81f, 0.10f),
        ["bluey green"] = TC(155, 0.61f),
        ["grey pink"] = TC(347, 0.3f),
        ["soft purple"] = TC(287, 0.32f),
        ["brown red"] = TC(16, 0.93f),
        ["medium grey"] = TC(100, 0.01f),
        ["berry"] = TC(334, 0.82f),
        ["poo"] = TC(48, 0.96f),
        ["purpley pink"] = TC(306, 0.56f),
        ["light salmon"] = TC(12, 0.98f),
        ["snot"] = TC(65, 0.87f),
        ["easter purple"] = TC(274, 0.99f),
        ["light yellow green"] = TC(83, 0.97f),
        ["dark navy blue"] = TCDrop(237, 1.0f, 0.10f),
        ["drab"] = TC(61, 0.32f),
        ["light rose"] = TCLift(354, 1.0f, 0.10f),
        ["rouge"] = TC(345, 0.81f),
        ["purplish red"] = TC(335, 0.94f),
        ["slime green"] = TC(75, 0.96f),
        ["baby poop"] = TC(51, 1.0f),
        ["irish green"] = TC(136, 0.99f),
        ["pink purple"] = TC(302, 0.87f),
        ["dark navy"] = TCDrop(235, 1.0f, 0.10f),
        ["greeny blue"] = TC(164, 0.46f),
        ["light plum"] = TC(322, 0.29f),
        ["pinkish grey"] = TC(6, 0.22f),
        ["dirty orange"] = TC(35, 0.94f),
        ["rust red"] = TC(13, 0.95f),
        ["pale lilac"] = TCLift(269, 1.0f, 0.10f),
        ["orangey red"] = TC(8, 0.96f),
        ["primary blue"] = TC(241, 0.97f),
        ["kermit green"] = TC(89, 1.0f),
        ["brownish purple"] = TC(346, 0.28f),
        ["murky green"] = TC(68, 0.79f),
        ["very dark purple"] = TCDrop(288, 0.96f, 0.10f),
        ["bottle green"] = TCDrop(121, 0.9f, 0.10f),
        ["watermelon"] = TC(354, 0.98f),
        ["deep sky blue"] = TC(213, 0.94f),
        ["fire engine red"] = TC(360, 1.0f),
        ["yellow ochre"] = TC(46, 0.94f),
        ["pumpkin orange"] = TC(29, 0.97f),
        ["pale olive"] = TC(75, 0.42f),
        ["light lilac"] = TCLift(280, 1.0f, 0.10f),
        ["lightish green"] = TC(120, 0.68f),
        ["carolina blue"] = TC(216, 0.98f),
        ["mulberry"] = TC(330, 0.87f),
        ["shocking pink"] = TC(322, 0.99f),
        ["auburn"] = TC(18, 0.99f),
        ["bright lime green"] = TC(97, 0.99f),
        ["celadon"] = TCLift(114, 0.95f, 0.10f),
        ["pinkish brown"] = TC(13, 0.34f),
        ["poo brown"] = TC(42, 0.99f),
        ["bright sky blue"] = TC(192, 0.99f),
        ["celery"] = TC(95, 0.96f),
        ["dirt brown"] = TC(36, 0.39f),
        ["strawberry"] = TC(353, 0.96f),
        ["dark lime"] = TC(77, 0.99f),
        ["medium brown"] = TC(35, 0.75f),
        ["muted green"] = TC(110, 0.32f),
        ["robins egg"] = TC(187, 0.97f),
        ["bright aqua"] = TC(176, 0.95f),
        ["bright lavender"] = TC(279, 1.0f),
        ["very light purple"] = TCLift(292, 0.88f, 0.10f),
        ["light navy"] = TC(208, 0.73f),
        ["pink red"] = TC(342, 0.96f),
        ["olive brown"] = TCDrop(50, 0.94f, 0.10f),
        ["poop brown"] = TC(44, 0.98f),
        ["mustard green"] = TC(64, 0.96f),
        ["ocean green"] = TC(155, 0.43f),
        ["very dark blue"] = TCDrop(239, 1.0f, 0.10f),
        ["dusty green"] = TC(117, 0.24f),
        ["light navy blue"] = TC(211, 0.49f),
        ["minty green"] = TC(149, 0.94f),
        ["adobe"] = TC(18, 0.47f),
        ["barney"] = TC(295, 0.73f),
        ["jade green"] = TC(149, 0.61f),
        ["bright light blue"] = TC(182, 0.98f),
        ["light lime"] = TC(93, 0.97f),
        ["dark khaki"] = TC(50, 0.29f),
        ["orange yellow"] = TC(41, 1.0f),
        ["ocre"] = TC(47, 0.96f),
        ["maize"] = TC(46, 0.88f),
        ["faded pink"] = TC(346, 0.5f),
        ["british racing green"] = TCDrop(127, 0.87f, 0.10f),
        ["sandstone"] = TC(41, 0.44f),
        ["mud brown"] = TC(41, 0.73f),
        ["light sea green"] = TC(135, 0.84f),
        ["robin egg blue"] = TC(187, 0.98f),
        ["aqua marine"] = TC(165, 0.8f),
        ["dark sea green"] = TC(159, 0.78f),
        ["soft pink"] = TCLift(348, 0.95f, 0.10f),
        ["orangey brown"] = TC(32, 0.98f),
        ["cherry red"] = TC(350, 0.98f),
        ["burnt yellow"] = TC(48, 0.92f),
        ["brownish grey"] = TC(37, 0.17f),
        ["camel"] = TC(39, 0.49f),
        ["purplish grey"] = TC(287, 0.1f),
        ["marine"] = TCDrop(213, 0.92f, 0.10f),
        ["greyish pink"] = TC(353, 0.35f),
        ["pale turquoise"] = TCLift(153, 0.91f, 0.10f),
        ["pastel yellow"] = TC(60, 1.0f),
        ["bluey purple"] = TC(255, 0.54f),
        ["canary yellow"] = TC(60, 1.0f),
        ["faded red"] = TC(358, 0.61f),
        ["coffee"] = TC(35, 0.37f),
        ["bright magenta"] = TC(306, 1.0f),
        ["mocha"] = TC(29, 0.32f),
        ["ecru"] = TCLift(61, 1.0f, 0.10f),
        ["purpleish"] = TC(310, 0.28f),
        ["cranberry"] = TC(338, 1.0f),
        ["darkish green"] = TC(131, 0.51f),
        ["brown orange"] = TC(34, 0.98f),
        ["dusky rose"] = TC(352, 0.37f),
        ["melon"] = TC(12, 1.0f),
        ["purply blue"] = TC(262, 0.86f),
        ["purpleish blue"] = TC(251, 0.85f),
        ["hospital green"] = TC(132, 0.59f),
        ["shit brown"] = TC(42, 0.94f),
        ["mid blue"] = TC(211, 0.64f),
        ["easter green"] = TC(113, 0.97f),
        ["soft blue"] = TC(224, 0.76f),
        ["cerulean blue"] = TC(213, 0.96f),
        ["golden brown"] = TC(41, 0.99f),
        ["bright turquoise"] = TC(179, 0.99f),
        ["red pink"] = TC(348, 0.95f),
        ["red purple"] = TC(329, 0.9f),
        ["greyish brown"] = TC(38, 0.21f),
        ["vermillion"] = TC(10, 0.91f),
        ["russet"] = TC(20, 0.94f),
        ["steel grey"] = TC(198, 0.11f),
        ["lighter purple"] = TC(269, 0.88f),
        ["bright violet"] = TC(280, 0.98f),
        ["prussian blue"] = TC(205, 1.0f),
        ["slate green"] = TC(132, 0.17f),
        ["dirty pink"] = TC(356, 0.43f),
        ["dark blue green"] = TCDrop(173, 1.0f, 0.10f),
        ["pine"] = TC(131, 0.37f),
        ["yellowy green"] = TC(75, 0.88f),
        ["dark gold"] = TC(48, 0.84f),
        ["bluish"] = TC(208, 0.64f),
        ["darkish blue"] = TC(210, 0.98f),
        ["dull red"] = TC(0, 0.5f),
        ["pinky red"] = TC(351, 0.97f),
        ["pale teal"] = TC(159, 0.41f),
        ["military green"] = TC(81, 0.33f),
        ["barbie pink"] = TC(329, 0.99f),
        ["bubblegum pink"] = TC(324, 0.98f),
        ["pea soup green"] = TC(68, 0.76f),
        ["dark mustard"] = TC(49, 0.94f),
        ["shit"] = TC(45, 1.0f),
        ["medium purple"] = TC(297, 0.41f),
        ["very dark green"] = TCDrop(116, 0.88f, 0.10f),
        ["dirt"] = TC(36, 0.33f),
        ["dusky pink"] = TC(348, 0.45f),
        ["red violet"] = TC(321, 0.99f),
        ["lemon yellow"] = TC(61, 1.0f),
        ["pistachio"] = TC(91, 0.92f),
        ["dull yellow"] = TC(53, 0.81f),
        ["dark lime green"] = TC(80, 0.99f),
        ["denim blue"] = TC(218, 0.42f),
        ["teal blue"] = TC(189, 0.99f),
        ["lightish blue"] = TC(221, 0.98f),
        ["purpley blue"] = TC(254, 0.79f),
        ["light indigo"] = TC(250, 0.55f),
        ["swamp green"] = TC(68, 1.0f),
        ["brown green"] = TC(57, 0.74f),
        ["dark maroon"] = TCDrop(352, 1.0f, 0.10f),
        ["hot purple"] = TC(290, 1.0f),
        ["dark forest green"] = TCDrop(125, 1.0f, 0.10f),
        ["faded blue"] = TC(213, 0.39f),
        ["drab green"] = TC(89, 0.3f),
        ["light lime green"] = TC(87, 1.0f),
        ["snot green"] = TC(71, 1.0f),
        ["yellowish"] = TC(55, 0.94f),
        ["light blue green"] = TC(145, 0.94f),
        ["bordeaux"] = TC(339, 1.0f),
        ["light mauve"] = TC(341, 0.28f),
        ["marigold"] = TC(45, 0.98f),
        ["muddy green"] = TC(74, 0.4f),
        ["dull orange"] = TC(29, 0.67f),
        ["electric purple"] = TC(277, 1.0f),
        ["fluorescent green"] = TC(120, 1.0f),
        ["yellowish brown"] = TC(47, 0.99f),
        ["soft green"] = TC(125, 0.4f),
        ["bright orange"] = TC(21, 1.0f),
        ["lemon"] = TC(61, 1.0f),
        ["purple grey"] = TC(303, 0.09f),
        ["acid green"] = TC(87, 0.99f),
        ["pale lavender"] = TCLift(280, 0.96f, 0.10f),
        ["violet blue"] = TC(262, 0.91f),
        ["light forest green"] = TC(124, 0.29f),
        ["burnt red"] = TC(12, 0.94f),
        ["khaki green"] = TC(76, 0.4f),
        ["faded purple"] = TC(289, 0.17f),
        ["dark olive green"] = TCDrop(74, 0.92f, 0.10f),
        ["grey brown"] = TC(40, 0.21f),
        ["green grey"] = TC(106, 0.14f),
        ["true blue"] = TC(236, 0.99f),
        ["pale violet"] = TCLift(265, 0.88f, 0.10f),
        ["periwinkle blue"] = TC(234, 0.93f),
        ["light sky blue"] = TCLift(183, 1.0f, 0.10f),
        ["blurple"] = TC(251, 0.59f),
        ["green brown"] = TCDrop(56, 0.93f, 0.10f),
        ["bluegreen"] = TC(180, 0.98f),
        ["bright teal"] = TC(168, 0.99f),
        ["brownish yellow"] = TC(52, 0.97f),
        ["pea soup"] = TC(63, 0.99f),
        ["barney purple"] = TC(303, 0.95f),
        ["ultramarine"] = TC(251, 1.0f),
        ["purplish"] = TC(308, 0.26f),
        ["puke yellow"] = TC(59, 0.87f),
        ["bluish grey"] = TC(201, 0.14f),
        ["dark periwinkle"] = TC(244, 0.55f),
        ["dark lilac"] = TC(290, 0.24f),
        ["reddish"] = TC(1, 0.53f),
        ["light maroon"] = TC(350, 0.38f),
        ["dusty purple"] = TC(292, 0.17f),
        ["terra cotta"] = TC(17, 0.57f),
        ["avocado"] = TC(76, 0.55f),
        ["marine blue"] = TC(209, 0.98f),
        ["teal green"] = TC(155, 0.63f),
        ["slate grey"] = TC(204, 0.1f),
        ["lighter green"] = TC(113, 0.97f),
        ["electric green"] = TC(115, 0.98f),
        ["dusty blue"] = TC(208, 0.34f),
        ["golden yellow"] = TC(46, 0.99f),
        ["bright yellow"] = TC(60, 1.0f),
        ["light lavender"] = TCLift(267, 0.97f, 0.10f),
        ["poop"] = TC(44, 1.0f),
        ["dark peach"] = TC(15, 0.66f),
        ["jungle green"] = TC(150, 0.94f),
        ["eggshell"] = TCLift(60, 1.0f, 0.10f),
        ["denim"] = TC(210, 0.41f),
        ["yellow brown"] = TC(49, 1.0f),
        ["dull purple"] = TC(308, 0.19f),
        ["chocolate brown"] = TCDrop(23, 1.0f, 0.10f),
        ["wine red"] = TC(344, 0.95f),
        ["neon blue"] = TC(189, 1.0f),
        ["dirty green"] = TC(78, 0.48f),
        ["light tan"] = TCLift(50, 0.91f, 0.10f),
        ["cadet blue"] = TC(208, 0.32f),
        ["dark mauve"] = TC(338, 0.28f),
        ["very light blue"] = TCLift(180, 1.0f, 0.10f),
        ["grey purple"] = TC(281, 0.12f),
        ["pastel pink"] = TCLift(343, 1.0f, 0.10f),
        ["very light green"] = TCLift(102, 1.0f, 0.10f),
        ["dark sky blue"] = TC(212, 0.75f),
        ["evergreen"] = TCDrop(154, 0.87f, 0.10f),
        ["dull pink"] = TC(343, 0.48f),
        ["aubergine"] = TCDrop(310, 0.79f, 0.10f),
        ["reddish orange"] = TC(12, 0.94f),
        ["deep green"] = TCDrop(129, 0.96f, 0.10f),
        ["vomit green"] = TC(69, 0.96f),
        ["dusty pink"] = TC(352, 0.47f),
        ["faded green"] = TC(113, 0.29f),
        ["camo green"] = TC(78, 0.46f),
        ["pinky purple"] = TC(305, 0.54f),
        ["brownish red"] = TC(9, 0.64f),
        ["dark rose"] = TC(348, 0.43f),
        ["mud"] = TC(46, 0.73f),
        ["brownish"] = TC(19, 0.28f),
        ["emerald green"] = TC(132, 0.97f),
        ["pale brown"] = TC(31, 0.3f),
        ["dull blue"] = TC(208, 0.36f),
        ["burnt umber"] = TC(23, 0.84f),
        ["medium green"] = TC(128, 0.5f),
        ["clay"] = TC(15, 0.41f),
        ["light aqua"] = TC(161, 1.0f),
        ["light olive green"] = TC(76, 0.43f),
        ["brownish orange"] = TC(30, 0.71f),
        ["dark aqua"] = TC(181, 0.91f),
        ["purplish pink"] = TC(317, 0.54f),
        ["dark salmon"] = TC(4, 0.52f),
        ["greenish grey"] = TC(104, 0.17f),
        ["ugly green"] = TC(72, 0.96f),
        ["dark beige"] = TC(40, 0.31f),
        ["pale red"] = TC(3, 0.65f),
        ["light magenta"] = TC(301, 0.94f),
        ["sky"] = TC(205, 0.95f),
        ["light cyan"] = TCLift(178, 1.0f, 0.10f),
        ["yellow orange"] = TC(42, 0.99f),
        ["reddish purple"] = TC(328, 0.88f),
        ["reddish pink"] = TC(349, 0.99f),
        ["dirty yellow"] = TC(58, 0.91f),
        ["orange red"] = TC(9, 0.98f),
        ["deep red"] = TC(1, 1.0f),
        ["orange brown"] = TC(32, 1.0f),
        ["cobalt blue"] = TC(237, 0.96f),
        ["neon pink"] = TC(324, 0.99f),
        ["rose pink"] = TC(350, 0.88f),
        ["greyish purple"] = TC(283, 0.13f),
        ["raspberry"] = TC(335, 0.99f),
        ["aqua green"] = TC(157, 0.85f),
        ["salmon pink"] = TC(360, 0.98f),
        ["tangerine"] = TC(34, 1.0f),
        ["brownish green"] = TC(62, 0.85f),
        ["red brown"] = TC(12, 0.73f),
        ["greenish brown"] = TC(54, 0.71f),
        ["pumpkin"] = TC(32, 0.99f),
        ["pine green"] = TCDrop(139, 0.76f, 0.10f),
        ["baby pink"] = TCLift(341, 1.0f, 0.10f),
        ["cornflower"] = TC(234, 0.9f),
        ["blue violet"] = TC(263, 0.95f),
        ["greyish green"] = TC(113, 0.19f),
        ["dark olive"] = TCDrop(67, 0.94f, 0.10f),
        ["pastel purple"] = TCLift(267, 1.0f, 0.10f),
        ["terracotta"] = TC(16, 0.56f),
        ["aqua blue"] = TC(184, 0.98f),
        ["sage green"] = TC(104, 0.28f),
        ["deep pink"] = TC(331, 0.99f),
        ["grass"] = TC(98, 0.59f),
        ["pastel blue"] = TCLift(221, 0.98f, 0.10f),
        ["bluish green"] = TC(160, 0.82f),
        ["dark tan"] = TC(37, 0.41f),
        ["greenish blue"] = TC(178, 0.85f),
        ["pale orange"] = TC(29, 1.0f),
        ["vomit"] = TC(61, 0.77f),
        ["forrest green"] = TCDrop(105, 0.84f, 0.10f),
        ["dark lavender"] = TC(277, 0.19f),
        ["dark violet"] = TCDrop(289, 0.97f, 0.10f),
        ["dark cyan"] = TC(181, 0.86f),
        ["olive drab"] = TC(66, 0.4f),
        ["pinkish"] = TC(349, 0.55f),
        ["neon purple"] = TC(283, 0.99f),
        ["light turquoise"] = TC(160, 0.84f),
        ["apple green"] = TC(91, 0.69f),
        ["dull green"] = TC(104, 0.28f),
        ["powder blue"] = TCLift(214, 0.93f, 0.10f),
        ["off white"] = TCLift(60, 1.0f, 0.10f),
        ["electric blue"] = TC(222, 1.0f),
        ["dark turquoise"] = TCDrop(179, 0.92f, 0.10f),
        ["bright red"] = TC(357, 1.0f),
        ["pinkish red"] = TC(345, 0.91f),
        ["cornflower blue"] = TC(226, 0.63f),
        ["light olive"] = TC(73, 0.4f),
        ["grape"] = TC(312, 0.35f),
        ["greyish blue"] = TC(207, 0.25f),
        ["purplish blue"] = TC(258, 0.95f),
        ["yellowish green"] = TC(74, 0.82f),
        ["greenish yellow"] = TC(71, 0.98f),
        ["medium blue"] = TC(212, 0.62f),
        ["dusty rose"] = TC(355, 0.38f),
        ["light violet"] = TCLift(268, 0.92f, 0.10f),
        ["midnight blue"] = TCDrop(242, 1.0f, 0.10f),
        ["bluish purple"] = TC(258, 0.78f),
        ["red orange"] = TC(13, 0.98f),
        ["dark magenta"] = TC(326, 1.0f),
        ["greenish"] = TC(144, 0.44f),
        ["ocean blue"] = TC(197, 0.96f),
        ["reddish brown"] = TC(17, 0.85f),
        ["burnt sienna"] = TC(23, 0.84f),
        ["brick"] = TC(9, 0.64f),
        ["robins egg blue"] = TC(186, 0.89f),
        ["moss green"] = TC(87, 0.43f),
        ["steel blue"] = TC(207, 0.26f),
        ["eggplant"] = TCDrop(304, 0.75f, 0.10f),
        ["light yellow"] = TC(60, 1.0f),
        ["leaf green"] = TC(88, 0.95f),
        ["light grey"] = TCLift(100, 0.08f, 0.10f),
        ["puke"] = TC(60, 0.98f),
        ["pinkish purple"] = TC(300, 0.64f),
        ["sea blue"] = TC(194, 0.95f),
        ["pale purple"] = TC(274, 0.44f),
        ["slate blue"] = TC(208, 0.25f),
        ["hunter green"] = TCDrop(117, 0.78f, 0.10f),
        ["pale yellow"] = TC(60, 1.0f),
        ["mustard yellow"] = TC(54, 0.91f),
        ["light red"] = TC(358, 1.0f),
        ["pale pink"] = TCLift(344, 1.0f, 0.10f),
        ["deep blue"] = TC(241, 0.97f),
        ["light teal"] = TC(155, 0.61f),
        ["dark yellow"] = TC(51, 0.91f),
        ["dark grey"] = TC(180, 0.01f),
        ["army green"] = TC(75, 0.62f),
        ["puce"] = TC(32, 0.34f),
        ["spring green"] = TC(95, 0.92f),
        ["dark orange"] = TC(24, 0.98f),
        ["pastel green"] = TCLift(108, 1.0f, 0.10f),
        ["light orange"] = TC(32, 0.98f),
        ["bright pink"] = TC(318, 0.99f),
        ["deep purple"] = TCDrop(291, 0.97f, 0.10f),
        ["dark brown"] = TCDrop(31, 0.93f, 0.10f),
        ["taupe"] = TC(35, 0.29f),
        ["pea green"] = TC(71, 0.81f),
        ["puke green"] = TC(67, 0.92f),
        ["kelly green"] = TC(136, 0.98f),
        ["seafoam green"] = TC(143, 0.91f),
        ["dark teal"] = TCDrop(181, 0.97f, 0.10f),
        ["brick red"] = TC(8, 0.97f),
        ["mint green"] = TC(129, 1.0f),
        ["baby blue"] = TCLift(211, 0.98f, 0.10f),
        ["bright purple"] = TC(285, 0.98f),
        ["dark red"] = TC(0, 1.0f),
        ["pale blue"] = TCLift(180, 0.96f, 0.10f),
        ["grass green"] = TC(98, 0.87f),
        ["burnt orange"] = TC(24, 0.99f),
        ["neon green"] = TC(120, 1.0f),
        ["bright blue"] = TC(216, 0.99f),
        ["light pink"] = TCLift(342, 1.0f, 0.10f),
        ["mustard"] = TC(52, 0.99f),
        ["sea green"] = TC(148, 0.97f),
        ["periwinkle"] = TC(246, 0.98f),
        ["dark pink"] = TC(342, 0.57f),
        ["olive green"] = TC(70, 0.94f),
        ["pale green"] = TCLift(105, 0.95f, 0.10f),
        ["light brown"] = TC(32, 0.37f),
        ["hot pink"] = TC(327, 1.0f),
        ["navy blue"] = TCDrop(225, 1.0f, 0.10f),
        ["royal blue"] = TC(240, 0.95f),
        ["bright green"] = TC(121, 1.0f),
        ["dark purple"] = TCDrop(290, 0.82f, 0.10f),
        ["forest green"] = TCDrop(126, 0.84f, 0.10f),
        ["dark blue"] = TCDrop(238, 1.0f, 0.10f),
        ["dark green"] = TCDrop(117, 1.0f, 0.10f),
        ["light purple"] = TC(274, 0.88f),
        ["lime green"] = TC(88, 0.99f),
        ["sky blue"] = TC(209, 0.97f),
        ["light green"] = TC(107, 0.91f),
        ["light blue"] = TC(206, 0.94f),
        // CSS4 named
        ["darkgrey"] = TC(0, 0.0f),
        ["darkslategrey"] = TC(180, 0.25f),
        ["dimgrey"] = TC(0, 0.0f),
        ["lightgrey"] = TCLift(0, 0.0f, 0.10f),
        ["lightslategrey"] = TC(210, 0.14f),
        ["slategrey"] = TC(210, 0.13f),
    };

    /// <summary>
    /// Resolve a color name to a TargetColor. For phrases like "obsidian black
    /// textured", returns the LONGEST dictionary key that appears as a whole
    /// word — so "obsidian" wins over "black", "polished stone" wins over
    /// "stone". Tolerates plurals ("blues" matches "blue").
    /// </summary>
    private TargetColor? ResolveColorName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var lower = name.Trim().ToLowerInvariant();

        if (ColorDictionary.TryGetValue(lower, out var direct))
        {
            _logger.LogInformation("PaletteSwap: Resolved '{Name}' (exact) → (H={H:F0} S={S:F2} {Beh})",
                name, direct.H, direct.S, direct.Behavior);
            return direct;
        }

        (string Key, TargetColor Value)? best = null;
        foreach (var (key, value) in ColorDictionary)
        {
            int idx = lower.IndexOf(key, StringComparison.Ordinal);
            if (idx < 0) continue;
            bool leftOk = idx == 0 || !char.IsLetterOrDigit(lower[idx - 1]);
            int afterIdx = idx + key.Length;
            bool rightOk;
            if (afterIdx >= lower.Length) rightOk = true;
            else if (lower[afterIdx] == 's')
            {
                int afterS = afterIdx + 1;
                rightOk = afterS >= lower.Length || !char.IsLetterOrDigit(lower[afterS]);
            }
            else rightOk = !char.IsLetterOrDigit(lower[afterIdx]);
            if (!leftOk || !rightOk) continue;
            if (best == null || key.Length > best.Value.Key.Length)
                best = (key, value);
        }

        if (best != null)
        {
            _logger.LogInformation("PaletteSwap: Resolved '{Name}' → '{Key}' (H={H:F0} S={S:F2} {Beh})",
                name, best.Value.Key, best.Value.Value.H, best.Value.Value.S, best.Value.Value.Behavior);
            return best.Value.Value;
        }
        return null;
    }

    /// <summary>
    /// Rich resolver for LLM-generated target names. Walks four steps until
    /// one succeeds (returns null only if ALL fail, which is a config bug):
    ///   1. Exact / longest-substring match (the original ResolveColorName).
    ///   2. Strip leading qualifier words ("deep blue" → "blue"), retry.
    ///   3. Last-ditch per-word lookup ("bioluminescent teal" → "teal").
    ///   4. Family-default floor — every known family name is itself a
    ///      dictionary entry (grey, brown, gold, blue, white, etc.), so
    ///      falling back to the family name guarantees a valid TargetColor.
    ///
    /// This kills the "skipping" failure mode that left whole regions of the
    /// texture untouched on creative variants (e.g. "byssus silk" → grey
    /// skipped → 57% of pixels unchanged → mushy result).
    ///
    /// Mirror of ItemsController.ResolveColorWithFallback for the SEGMENTED
    /// path; kept here (rather than reused) because PaletteSwapService is the
    /// lower layer and shouldn't depend on the controller.
    /// </summary>
    private TargetColor? ResolveColorWithFallback(string colorName, string family)
    {
        // 1. Direct resolution (handles most named entries + compounds).
        var direct = ResolveColorName(colorName);
        if (direct != null) return direct;

        if (string.IsNullOrWhiteSpace(colorName))
        {
            // Defensive: no name at all → straight to family floor.
            return ResolveColorName(family);
        }

        // 2. Strip leading qualifier words and retry.
        //    LLM patterns: "deep/dark/bright/pale/muted/rich/soft/vivid X",
        //    "molten X", "cooling X", "ashen X", etc.
        string[] qualifiers =
        {
            "deep", "dark", "bright", "pale", "light", "muted", "rich", "soft",
            "vivid", "dull", "faded", "burnished", "tarnished", "polished",
            "glowing", "glow", "shadowed", "ancient", "frosted", "icy", "warm",
            "cool", "wet", "dry", "moldy", "rotten", "stained", "smoky",
            "molten", "cooling", "ashen", "rusty", "sickly", "blinding",
            "shimmering", "neon", "toxic", "bioluminescent", "antique",
            "abyssal", "brilliant", "cherry", "moonstone", "starlight",
            "driftwood", "white-hot", "confectioner",
        };
        var words = colorName.ToLowerInvariant()
            .Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries)
            .ToList();
        while (words.Count > 1 && qualifiers.Contains(words[0]))
        {
            words.RemoveAt(0);
            var core = string.Join(" ", words);
            var byCore = ResolveColorName(core);
            if (byCore != null)
            {
                _logger.LogInformation(
                    "PaletteSwap: '{Name}' resolved via core '{Core}' for family '{Fam}'",
                    colorName, core, family);
                return byCore;
            }
        }

        // 3. Per-word last-ditch — find any known color word anywhere in
        //    the name. ("blinding acid green" → "green" wins via this step
        //    even if "acid green" isn't a dictionary entry.)
        foreach (var w in colorName.ToLowerInvariant()
                     .Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var byWord = ResolveColorName(w);
            if (byWord != null)
            {
                _logger.LogInformation(
                    "PaletteSwap: '{Name}' resolved via word '{Word}' for family '{Fam}'",
                    colorName, w, family);
                return byWord;
            }
        }

        // 4. Family-default floor. Every parser-known family ("grey", "brown",
        //    "gold", "blue", "red", "green", "white", "black", "yellow",
        //    "orange", "purple", "silver", "steel") is itself a dictionary
        //    entry — falling back to the family name guarantees a valid
        //    TargetColor and never silently drops coverage.
        var familyDefault = ResolveColorName(family);
        if (familyDefault != null)
        {
            _logger.LogInformation(
                "PaletteSwap: '{Name}' UNRESOLVED — falling back to family default '{Fam}' (H={H:F0} S={S:F2})",
                colorName, family, familyDefault.H, familyDefault.S);
            return familyDefault;
        }

        // Only reachable if the family name itself isn't in the dictionary
        // (a config bug — caller will log loudly).
        return null;
    }


    // ═══════════════════════════════════════════════════════════════════
    // HSL ↔ RGB
    // ═══════════════════════════════════════════════════════════════════

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
}

// ═══════════════════════════════════════════════════════════════════════════
// DTO — kept compatible with the controller's PaletteSwapRequest
// ═══════════════════════════════════════════════════════════════════════════

public class PaletteSwapRequest
{
    public uint DisplayId { get; set; }
    public string OriginalMpqPath { get; set; } = "";
    public string OriginalBlpFilename { get; set; } = "";
    public string Instruction { get; set; } = "";
    public string ItemName { get; set; } = "";

    /// <summary>If true, feed the recolored result into Flux img2img for polish.</summary>
    public bool ChainToAI { get; set; }

    /// <summary>Style direction for the AI pass (only used if ChainToAI=true).</summary>
    public string StyleDirection { get; set; } = "";

    /// <summary>Denoise strength for the AI pass (only used if ChainToAI=true).</summary>
    public float AIDenoise { get; set; } = 0.3f;

    /// <summary>
    /// TEST MODE: skip the brute-force palette draft and send the ORIGINAL
    /// texture straight to Flux img2img with a region-aware prompt.
    /// </summary>
    public bool SkipBruteForce { get; set; }

    /// <summary>
    /// If true, commit the brute-force draft directly (no Flux). Used by the
    /// variation "Apply" path so the committed texture matches the previewed
    /// brute-force variant exactly, and is fast.
    /// </summary>
    public bool BruteForceOnly { get; set; }

    /// <summary>
    /// Optional box overrides in TEXTURE pixel coordinates. Applied before the
    /// global family match: "leave" keeps the region untouched, "force" paints
    /// it the box's target color.
    /// </summary>
    public List<BoxOverride>? Boxes { get; set; }
}

/// <summary>
/// A user-drawn rectangle override (texture pixel coords). Rule is "leave"
/// (exclude from swaps) or "force" (paint TargetName color regardless of source).
/// </summary>
public class BoxOverride
{
    public int X1 { get; set; }
    public int Y1 { get; set; }
    public int X2 { get; set; }
    public int Y2 { get; set; }
    public string Rule { get; set; } = "leave";   // "leave" | "force"
    public string TargetName { get; set; } = "";   // color name for "force"
}

/// <summary>A color family detected in a source texture, with aggregate stats.</summary>
public record DetectedFamily(string Family, int PixelCount, float Percent, float MeanSat, float MeanLightness);