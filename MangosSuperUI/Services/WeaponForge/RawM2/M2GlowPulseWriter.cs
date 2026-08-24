using System.Buffers.Binary;

namespace MangosSuperUI.Services.WeaponForge.RawM2;

/// <summary>
/// Makes a forged model's <b>additive glow passes breathe</b> instead of sitting dead.
///
/// === The case this exists for ===
///
/// Not every later-client effect is a particle emitter. Plenty are baked into the model as an extra
/// additive render pass — a glow shell over the geometry. Axe of the Nexus-Kings is the clean
/// example: zero emitters, zero ribbons, no ItemVisual, and its whole look is two additive passes
/// (an unlit <c>Ammo\Blue_Glow2.blp</c> shell plus an <c>ArmorReflect_Rainbow.blp</c> layer). The
/// import already carries those passes across faithfully — but the later client ANIMATES them, and a
/// glow that does not move reads as a painted-on decal.
///
/// === The mechanism (measured on 1.12's own data) ===
///
/// 1.12 animates exactly this way itself. <c>Spells\Enchantments\Sparkle_A.m2</c> is four vertices,
/// two triangles, ONE texture, zero emitters — and it looks alive because it carries a colour track
/// driven by a <b>global sequence</b> (durations 4000 and 1333 ms). Global sequences loop
/// independently of whatever the character is doing, which is exactly what an always-on glow needs;
/// 2,935 of 1.12's 5,146 weapon models use them.
///
/// === Why this is a safe edit ===
///
/// An <c>M2Track</c> is a FIXED 28 bytes — <c>uint16 interp; uint16 globalSequence;</c> then three
/// <c>M2Array{count, offset}</c> for ranges/timestamps/values. So converting a colour record's
/// constant alpha track into an animated one is an <b>in-place overwrite of 28 bytes</b> plus appended
/// keyframe data at EOF. Nothing moves, no offsets are rebuilt, and the colour table, the batches
/// that index it and the geometry are all untouched. Appending the new global sequence at the END of
/// the existing array also preserves every existing global-sequence index.
/// </summary>
public static class M2GlowPulseWriter
{
    private const int HdrGlobalSequences = 0x014;   // count, offset at +4
    private const int HdrColors = 0x054;            // count, offset at +4
    private const int ColorRecordSize = 56;         // M2Track<Vector3> colour + M2Track<int16> alpha
    private const int AlphaTrackOffset = 28;        // the alpha half of the record
    private const int TrackSize = 28;

    /// <summary>Loop length. Slow enough to read as a breath rather than a flicker; 1.12's own
    /// Sparkle_A uses 1333 and 4000 ms, so this sits inside Blizzard's own range.</summary>
    public const uint DefaultPeriodMs = 1800;

    /// <summary>How far the glow dips at the bottom of the breath. 1.0 would be no pulse at all;
    /// too low and an additive pass visibly blinks out.</summary>
    public const float DefaultFloor = 0.55f;

    public sealed record Result(byte[] M2, int Pulsed, IReadOnlyList<string> Notes);

    /// <summary>Animate the alpha of every colour record listed in <paramref name="colorIndices"/>.
    /// Returns the input unchanged when there is nothing to do — a pulse is a finishing touch and
    /// must never fail a build.</summary>
    public static Result Apply(byte[] m2, IReadOnlyCollection<int> colorIndices,
        uint periodMs = DefaultPeriodMs, float floor = DefaultFloor)
    {
        var notes = new List<string>();
        if (colorIndices.Count == 0) return new Result(m2, 0, notes);
        if (m2.Length < 0x148) return new Result(m2, 0, new[] { "target is not a v256 M2" });

        uint colorCount = U32(m2, HdrColors), colorOfs = U32(m2, HdrColors + 4);
        if (colorCount == 0 || colorOfs == 0)
            return new Result(m2, 0, new[] { "model carries no colour records to animate" });
        if (colorOfs + (long)colorCount * ColorRecordSize > m2.Length)
            return new Result(m2, 0, new[] { "colour table runs past EOF" });

        var wanted = colorIndices.Where(i => i >= 0 && i < colorCount).Distinct().ToList();
        if (wanted.Count == 0) return new Result(m2, 0, new[] { "no valid colour record indices" });

        var outp = new List<byte>(m2);
        Align4(outp);

        // 1) Global sequences: copy the existing durations and append ours. Appending at the END
        //    keeps every index already referenced by other tracks valid.
        uint gsCount = U32(m2, HdrGlobalSequences), gsOfs = U32(m2, HdrGlobalSequences + 4);
        int newGsOfs = outp.Count;
        for (uint i = 0; i < gsCount; i++)
        {
            uint dur = (gsOfs != 0 && gsOfs + (i + 1) * 4 <= m2.Length) ? U32(m2, (int)(gsOfs + i * 4)) : 0u;
            AddU32(outp, dur);
        }
        int pulseSequence = (int)gsCount;
        AddU32(outp, periodMs);

        // 2) Three keyframes: dim, bright, dim. Linear interpolation between them is the breath.
        Align4(outp);
        int timesOfs = outp.Count;
        AddU32(outp, 0); AddU32(outp, periodMs / 2); AddU32(outp, periodMs);

        Align4(outp);
        int keysOfs = outp.Count;
        short lo = Fixed16(Math.Clamp(floor, 0f, 1f));
        short hi = Fixed16(1f);
        AddI16(outp, lo); AddI16(outp, hi); AddI16(outp, lo);

        var final = outp.ToArray();

        // 3) Overwrite each target record's alpha track in place — same 28 bytes, new contents.
        foreach (int i in wanted)
        {
            int track = (int)(colorOfs + (long)i * ColorRecordSize) + AlphaTrackOffset;
            if (track + TrackSize > final.Length) continue;
            U16W(final, track + 0, 1);                       // linear interpolation
            U16W(final, track + 2, (ushort)pulseSequence);   // driven by the global sequence
            U32W(final, track + 4, 0); U32W(final, track + 8, 0);        // ranges: none
            U32W(final, track + 12, 3); U32W(final, track + 16, (uint)timesOfs);
            U32W(final, track + 20, 3); U32W(final, track + 24, (uint)keysOfs);
        }

        U32W(final, HdrGlobalSequences, gsCount + 1);
        U32W(final, HdrGlobalSequences + 4, (uint)newGsOfs);
        notes.Add($"{wanted.Count} additive glow pass(es) now breathe on a {periodMs} ms global sequence " +
                  $"(alpha {floor:0.##}→1.0→{floor:0.##}), global sequence index {pulseSequence}.");
        return new Result(final, wanted.Count, notes);
    }

    /// <summary>Which colour records belong to ADDITIVE batches — i.e. the glow passes. Read back off
    /// the emitted model rather than mirrored from the build inputs, so the mapping cannot drift from
    /// what was actually written.</summary>
    public static List<int> AdditiveColorIndices(M2Model parsed)
    {
        var indices = new List<int>();
        foreach (var b in parsed.Batches)
        {
            if (b.ColorIndex < 0) continue;
            if (b.MaterialIndex >= parsed.RenderFlags.Count) continue;
            // Blend 3/4 are the additive modes; 2 (alpha) glow shells read as decals too, but tinting
            // an alpha-blended pass down makes it TRANSPARENT rather than dimmer, so leave those.
            ushort blend = parsed.RenderFlags[b.MaterialIndex].BlendingMode;
            if (blend is 3 or 4 && !indices.Contains(b.ColorIndex)) indices.Add(b.ColorIndex);
        }
        return indices;
    }

    private static short Fixed16(float v) => (short)Math.Clamp((int)MathF.Round(Math.Clamp(v, 0f, 1f) * 32767f), 0, 32767);
    private static void Align4(List<byte> l) { while (l.Count % 4 != 0) l.Add(0); }
    private static void AddU32(List<byte> l, uint v) { Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(b, v); foreach (var x in b) l.Add(x); }
    private static void AddI16(List<byte> l, short v) { Span<byte> b = stackalloc byte[2]; BinaryPrimitives.WriteInt16LittleEndian(b, v); foreach (var x in b) l.Add(x); }
    private static uint U32(byte[] b, int o) => o + 4 > b.Length ? 0u : BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(o));
    private static void U32W(byte[] b, int o, uint v) { if (o + 4 <= b.Length) BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(o), v); }
    private static void U16W(byte[] b, int o, ushort v) { if (o + 2 <= b.Length) BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(o), v); }
}
