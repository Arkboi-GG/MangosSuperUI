// PaletteSwapService.Value.cs
//
// THE VALUE AXIS — a global, family-agnostic lightness pass for the seeded
// recolor engine. Partial-class extension of PaletteSwapService.
//
// Spec: VALUE_INVERSION_VARIANT_2026-07-15.md. This is the C# port of that
// spec's §2 reference. It owns the L channel when active: it BYPASSES the
// theory's per-material LIFT/DROP and the post-tent tier L stage (see
// ApplySmoothMap), because "light blade flats vs dark centre" is a value
// composition, not any colour the family detector can name.
//
//   value = keep   → identity (today's behaviour); the engine's normal L path runs.
//   value = invert → the item's value composition is flipped (light↔dark) while
//                    fine detail (inscriptions, engraving, edge highlights) stays
//                    legible with its polarity inverted, and internal shading
//                    gradients are preserved.
//
// ── Verified before porting (synthetic 128x64 blade, numpy/scipy) ──────────
//   * Mechanics hold: composition light<->dark swap; inscription |dL| preserved,
//     polarity flipped; writing the inverted colour to ALL texels (alpha left
//     verbatim) roughly halves the transparent-decal dark fraction vs the
//     where(vis,.,src) lock (spec §4.1); dark-desat pulls darkened metal toward
//     neutral.
//   * The histogram mirror is COMPOSITION-RELATIVE — its fixed point is the
//     MEDIAN of the visible low field, so absolute output L depends on the
//     item's own value distribution. The §6 numbers are the blade's; other
//     items land elsewhere. The UI must show the actual result, never promise
//     specific L values.
//   * Blur radius MUST be 4*sigma (matches the scipy reference the §6 numbers
//     were validated against; a 3*sigma kernel measurably shifts the aggregate).
//     Exact per-texel parity with scipy is neither achievable nor needed — the
//     rank mirror amplifies sub-0.001 blur differences at cluster edges — but
//     region aggregates hold (mean|dL| ~ 0.014). The mirror therefore uses a
//     fully deterministic (ascending, ties broken by index) sort: the SAME
//     input always yields the SAME output, which is the whole point of a
//     seeded engine.
//   * Sub-threshold (transparent) texels take lowf ALONE (spec §4.4 option),
//     not the divide-by-~0 masked low, which is garbage in a transparent void.
//
// Edge handling: half-sample symmetric ("reflect about the edge of the last
// pixel"), matching scipy.ndimage's default 'reflect' mode. NOTE this differs
// from numpy.pad('reflect'); it is numpy.pad('symmetric'). At 4*sigma on a
// 128px surface the difference is confined to the outermost texels.

namespace MangosSuperUI.Services;

/// <summary>Value axis switch. Keep = identity (today). Invert = flip the value composition.</summary>
public enum ValueMode
{
    Keep = 0,
    Invert = 1,
}

/// <summary>
/// Knobs for the value axis (spec §3). Construct via <see cref="Keep"/> or
/// <see cref="Invert"/> — a raw default(ValueSettings) is Keep with zeroed
/// knobs, which is only safe BECAUSE Keep short-circuits. Never flip Mode on a
/// defaulted struct; use the factory so the knobs carry their spec defaults.
/// </summary>
public readonly record struct ValueSettings
{
    /// <summary>The axis switch.</summary>
    public ValueMode Mode { get; init; }

    /// <summary>Low/high split. Tuned at 128px width; scaled by ScaleSigmaToWidth. ~1.0–5.0.</summary>
    public float BlurSigma { get; init; }

    /// <summary>Coefficient on the high-pass detail. 0 = flatten detail; 1 = full inversion (up to ~1.5).</summary>
    public float DetailStrength { get; init; }

    /// <summary>Lightness below which chroma starts dropping. 0–0.6.</summary>
    public float DarkDesatKnee { get; init; }

    /// <summary>Chroma multiplier at L=0. 0–1. Set 1.0 to disable dark-desat.</summary>
    public float DarkDesatFloor { get; init; }

    /// <summary>outL = lerp(srcL, invertedL, Blend). 0 = identity, 1 = full inversion. Partial for tiering/convergent.</summary>
    public float Blend { get; init; }

    /// <summary>Visibility cutoff for the histogram/blur mask ONLY (not the colour write). 0–64.</summary>
    public int AlphaThreshold { get; init; }

    /// <summary>Scale BlurSigma by width/128 so a fixed knob behaves across atlas slot sizes (spec §7).</summary>
    public bool ScaleSigmaToWidth { get; init; }

    /// <summary>Identity — the engine's normal L path runs untouched.</summary>
    public static ValueSettings Keep => new() { Mode = ValueMode.Keep };

    /// <summary>Value inversion with the spec's default knobs; override as needed.</summary>
    public static ValueSettings Invert(
        float blurSigma = 2.5f,
        float detailStrength = 1.0f,
        float darkDesatKnee = 0.40f,
        float darkDesatFloor = 0.25f,
        float blend = 1.0f,
        int alphaThreshold = 16,
        bool scaleSigmaToWidth = true)
        => new()
        {
            Mode = ValueMode.Invert,
            BlurSigma = blurSigma,
            DetailStrength = detailStrength,
            DarkDesatKnee = darkDesatKnee,
            DarkDesatFloor = darkDesatFloor,
            Blend = blend,
            AlphaThreshold = alphaThreshold,
            ScaleSigmaToWidth = scaleSigmaToWidth,
        };

    /// <summary>True when the invert pass should actually run (Blend 0 is identity even if Invert).</summary>
    public bool IsInvert => Mode == ValueMode.Invert && Blend > 0f;
}

public partial class PaletteSwapService
{
    /// <summary>
    /// Global value inversion of a lightness plane. Family-agnostic; owns L.
    /// Returns a NEW L plane already blended toward the source per Value.Blend.
    /// See the file header + spec §2.
    ///
    ///   srcL  : source lightness [0,1], row-major, length w*h.
    ///   alpha : per-texel alpha 0..255, row-major, length w*h.
    ///
    /// The histogram/blur use only VISIBLE texels (alpha >= AlphaThreshold); the
    /// returned plane covers ALL texels (transparent ones take lowf alone). The
    /// caller writes this L to every texel and leaves the alpha channel verbatim.
    /// </summary>
    internal static float[] ValueInvert(float[] srcL, byte[] alpha, int w, int h, in ValueSettings v)
    {
        int n = w * h;
        var outL = new float[n];

        float sigma = v.ScaleSigmaToWidth ? MathF.Max(0.5f, v.BlurSigma * (w / 128f)) : v.BlurSigma;
        int thr = v.AlphaThreshold;

        // ── (1) masked low-pass: blur(L·vis) / blur(vis). Normalizing by the
        //         blurred visibility mask stops transparent texels bleeding
        //         dark into the opaque art. (spec §2 step 1)
        var vis = new bool[n];
        var lw = new float[n];
        var visf = new float[n];
        for (int i = 0; i < n; i++)
        {
            bool ok = alpha[i] >= thr;
            vis[i] = ok;
            lw[i] = ok ? srcL[i] : 0f;
            visf[i] = ok ? 1f : 0f;
        }
        var lowNum = SeparableGaussianReflect(lw, w, h, sigma);
        var lowDen = SeparableGaussianReflect(visf, w, h, sigma);
        var low = new float[n];
        var high = new float[n];
        for (int i = 0; i < n; i++)
        {
            low[i] = lowNum[i] / MathF.Max(lowDen[i], 1e-6f);
            high[i] = srcL[i] - low[i];               // (2) high-pass = detail
        }

        // ── (3) histogram-reverse the low field over VISIBLE texels: an exact
        //         mirror of the low-frequency distribution (fixed point = its
        //         median). Deterministic: ascending by low, ties broken by
        //         index, so the same input always maps the same way.
        var visIdx = new List<int>(n);
        for (int i = 0; i < n; i++) if (vis[i]) visIdx.Add(i);
        int N = visIdx.Count;

        var lowf = (float[])low.Clone();
        if (N > 0)
        {
            var sortedLow = new float[N];              // visible low values, ascending
            for (int r = 0; r < N; r++) sortedLow[r] = low[visIdx[r]];
            Array.Sort(sortedLow);

            var order = new int[N];                    // positions into visIdx, ascending by low
            for (int r = 0; r < N; r++) order[r] = r;
            Array.Sort(order, (a, b) =>
            {
                int c = low[visIdx[a]].CompareTo(low[visIdx[b]]);
                return c != 0 ? c : a.CompareTo(b);    // deterministic tie-break
            });

            // rank r (r-th smallest low) receives the r-th LARGEST value
            for (int r = 0; r < N; r++)
                lowf[visIdx[order[r]]] = sortedLow[N - 1 - r];
        }

        // ── (4) recombine with INVERTED detail (lowf − detail·high, spec §2
        //         step 4), then (5) blend toward source per Value.Blend.
        //         Visible: lowf − detail·high. Transparent: lowf alone (§4.4).
        float detail = v.DetailStrength;
        float blend = Math.Clamp(v.Blend, 0f, 1f);
        for (int i = 0; i < n; i++)
        {
            float inv = vis[i]
                ? Math.Clamp(lowf[i] - detail * high[i], 0f, 1f)
                : Math.Clamp(lowf[i], 0f, 1f);
            outL[i] = srcL[i] + (inv - srcL[i]) * blend;   // lerp(srcL, inv, blend)
        }
        return outL;
    }

    /// <summary>
    /// Dark-desat tent (spec §2 step 5): a saturation multiplier that falls to
    /// DarkDesatFloor as L → 0 below DarkDesatKnee, mirroring the engine's
    /// existing high-L tent. floor >= 1 (or knee <= 0) disables it. Callers
    /// pass the FINAL (inverted) L so the darkened metals — not the ones that
    /// merely started dark — lose their chroma.
    /// </summary>
    internal static float DarkDesatGate(float outL, in ValueSettings v)
    {
        if (v.DarkDesatFloor >= 1f || v.DarkDesatKnee <= 0f) return 1f;
        float t = Math.Clamp(outL / v.DarkDesatKnee, 0f, 1f);
        return v.DarkDesatFloor + (1f - v.DarkDesatFloor) * t;
    }

    /// <summary>
    /// The engine's existing asymmetric high-L tent, extracted so the value axis
    /// can evaluate it against the FINAL (possibly inverted) lightness in
    /// ApplySmoothMap PASS 2. Brutal gate on the bright end (specular stays
    /// white-hot), gentle on the dark end (shadows keep >= 55% chroma). Same
    /// curve PASS 1 applies in keep mode, so keep-mode output is unchanged.
    /// </summary>
    private static float HighLTentGate(float outL)
    {
        if (outL >= 0.5f)
        {
            float t = (1f - outL) / 0.5f;                 // 1 at mid -> 0 at white
            return 0.10f + 0.90f * MathF.Pow(t, 0.9f);
        }
        else
        {
            float t = outL / 0.5f;                        // 0 at black -> 1 at mid
            return 0.55f + 0.45f * MathF.Pow(t, 0.6f);    // shadows keep >= 55%
        }
    }

    /// <summary>
    /// Separable Gaussian blur of a float plane, half-sample symmetric edges,
    /// radius = round(4*sigma) to match the scipy reference the §6 numbers were
    /// validated against (a 3*sigma kernel measurably shifts the aggregate).
    /// Two 1-D passes; O(n·r).
    /// </summary>
    internal static float[] SeparableGaussianReflect(float[] src, int w, int h, float sigma)
    {
        if (sigma <= 0f) return (float[])src.Clone();

        int r = (int)(4f * sigma + 0.5f);
        if (r < 1) r = 1;

        // normalized 1-D kernel
        var k = new float[2 * r + 1];
        float sum = 0f, inv2s2 = 0.5f / (sigma * sigma);
        for (int t = -r; t <= r; t++)
        {
            float e = MathF.Exp(-(t * t) * inv2s2);
            k[t + r] = e;
            sum += e;
        }
        for (int t = 0; t < k.Length; t++) k[t] /= sum;

        int n = w * h;
        var tmp = new float[n];
        var outp = new float[n];

        // horizontal pass
        for (int y = 0; y < h; y++)
        {
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                float acc = 0f;
                for (int t = -r; t <= r; t++)
                    acc += k[t + r] * src[row + Reflect(x + t, w)];
                tmp[row + x] = acc;
            }
        }

        // vertical pass
        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                float acc = 0f;
                for (int t = -r; t <= r; t++)
                    acc += k[t + r] * tmp[Reflect(y + t, h) * w + x];
                outp[y * w + x] = acc;
            }

        return outp;
    }

    /// <summary>Half-sample symmetric index reflection (scipy 'reflect'): -1 → 0, len → len-1.</summary>
    private static int Reflect(int i, int len)
    {
        if (len == 1) return 0;
        while (i < 0 || i >= len)
        {
            if (i < 0) i = -i - 1;
            if (i >= len) i = 2 * len - i - 1;
        }
        return i;
    }
}
