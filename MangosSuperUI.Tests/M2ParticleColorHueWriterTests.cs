using System.Buffers.Binary;
using System.Text;
using MangosSuperUI.Services.WeaponForge.RawM2;
using Xunit;

namespace MangosSuperUI.Tests;

public sealed class M2ParticleColorHueWriterTests
{
    private const int HeaderSize = 0x144;
    private const int RecordSize = 504;
    private const int RampOffset = 336;

    [Fact]
    public void Apply_RecolorsEveryEmitterRegardlessOfId_AndChangesOnlyRampRgbBytes()
    {
        uint[][] ramps =
        [
            [0xFF00CC00, 0x80006600, 0x20002200],
            [0xFF102030, 0x7F405060, 0x01607080],
        ];
        byte[] source = BuildM2(ramps, [-1, 999]);
        byte[] before = (byte[])source.Clone();

        M2ParticleColorHueWriter.Result result =
            M2ParticleColorHueWriter.Apply(source, 300f, 0.65f);

        Assert.True(result.IsComplete);
        Assert.Equal(2, result.CandidateEmitters);
        Assert.Equal(2, result.EmittersHandled);
        Assert.Equal(0, result.EmittersSkipped);
        Assert.Equal(2, result.EmittersChanged);
        Assert.Equal(6, result.ColorKeysHandled);
        Assert.Equal(6, result.ColorKeysChanged);
        Assert.Equal(before, source);
        Assert.NotSame(source, result.M2);
        Assert.Equal(before.Length, result.M2.Length);
        AssertOnlyRampRgbMayDiffer(before, result.M2, 2);

        for (int emitter = 0; emitter < ramps.Length; emitter++)
        {
            for (int key = 0; key < 3; key++)
            {
                uint original = ramps[emitter][key];
                uint recolored = ReadColor(result.M2, emitter, key);
                Assert.Equal(original >> 24, recolored >> 24);
                Assert.NotEqual(original & 0x00FFFFFF, recolored & 0x00FFFFFF);

                ToHsl(original, out _, out _, out float originalLightness);
                ToHsl(recolored, out float hue, out float saturation, out float recoloredLightness);
                Assert.InRange(CircularHueDistance(hue, 300f), 0f, 1.5f);
                Assert.InRange(MathF.Abs(saturation - 0.65f), 0f, 0.02f);
                Assert.InRange(MathF.Abs(recoloredLightness - originalLightness), 0f, 1f / 255f);
            }
        }
    }

    [Fact]
    public void Apply_AppliesRequestedSaturationToGrayscaleAndPastelKeys()
    {
        byte[] source = BuildM2(
            [[0xFF808080, 0xCCB0A0A8, 0x40808080]],
            [42]);

        M2ParticleColorHueWriter.Result result =
            M2ParticleColorHueWriter.Apply(source, 180f, 0.9f);

        Assert.True(result.IsComplete);
        Assert.Equal(1, result.EmittersChanged);
        Assert.Equal(3, result.ColorKeysChanged);
        for (int key = 0; key < 3; key++)
        {
            uint original = ReadColor(source, 0, key);
            uint recolored = ReadColor(result.M2, 0, key);
            ToHsl(original, out _, out _, out float originalLightness);
            ToHsl(recolored, out float hue, out float saturation, out float recoloredLightness);
            Assert.InRange(CircularHueDistance(hue, 180f), 0f, 1.5f);
            Assert.InRange(MathF.Abs(saturation - 0.9f), 0f, 0.02f);
            Assert.InRange(MathF.Abs(recoloredLightness - originalLightness), 0f, 1f / 255f);
            Assert.Equal(original >> 24, recolored >> 24);
        }
    }

    [Fact]
    public void Apply_WithEligibleTextures_RecolorsOnlyMatchingEmittersRegardlessOfId()
    {
        byte[] source = BuildM2(
            [
                [0xFF00CC00, 0x80006600, 0x20002200],
                [0xFF102030, 0x7F405060, 0x01607080],
            ],
            [999, -500],
            [0, 1],
            textureCount: 2);
        byte[] before = (byte[])source.Clone();

        M2ParticleColorHueWriter.Result result =
            M2ParticleColorHueWriter.Apply(source, 300f, 0.65f, [0]);

        Assert.True(result.IsComplete);
        Assert.Equal(1, result.CandidateEmitters);
        Assert.Equal(1, result.EmittersHandled);
        Assert.Equal(1, result.EmittersChanged);
        Assert.Equal(3, result.ColorKeysHandled);
        Assert.Equal(3, result.ColorKeysChanged);
        AssertOnlyRampRgbMayDiffer(before, result.M2, [0]);
        for (int key = 0; key < 3; key++)
        {
            Assert.NotEqual(ReadColor(before, 0, key), ReadColor(result.M2, 0, key));
            Assert.Equal(ReadColor(before, 1, key), ReadColor(result.M2, 1, key));
        }
    }

    [Fact]
    public void Apply_WithEligibleTextures_FailsClosedForMalformedTextureTable()
    {
        byte[] source = BuildM2(
            [[0xFF00CC00, 0x80006600, 0x20002200]],
            [999],
            [0],
            textureCount: 1);
        WriteU32(source, 0x60, uint.MaxValue);
        byte[] before = (byte[])source.Clone();

        M2ParticleColorHueWriter.Result result =
            M2ParticleColorHueWriter.Apply(source, 300f, 0.65f, [0]);

        Assert.False(result.IsComplete);
        Assert.Same(source, result.M2);
        Assert.Equal(before, source);
        Assert.Equal(0, result.EmittersHandled);
        Assert.Equal(0, result.EmittersChanged);
        Assert.Contains(result.Notes,
            note => note.Contains("texture table", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Apply_WithEligibleTextures_FailsClosedForInvalidSuppliedIndex()
    {
        byte[] source = BuildM2(
            [[0xFF00CC00, 0x80006600, 0x20002200]],
            [999],
            [0],
            textureCount: 1);

        M2ParticleColorHueWriter.Result result =
            M2ParticleColorHueWriter.Apply(source, 300f, 0.65f, [1]);

        Assert.False(result.IsComplete);
        Assert.Same(source, result.M2);
        Assert.Equal(0, result.EmittersHandled);
        Assert.Contains(result.Notes,
            note => note.Contains("eligible texture index", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Apply_WithEligibleTextures_FailsClosedForInvalidEmitterTextureReference()
    {
        byte[] source = BuildM2(
            [[0xFF00CC00, 0x80006600, 0x20002200]],
            [999],
            [1],
            textureCount: 1);

        M2ParticleColorHueWriter.Result result =
            M2ParticleColorHueWriter.Apply(source, 300f, 0.65f, [0]);

        Assert.False(result.IsComplete);
        Assert.Same(source, result.M2);
        Assert.Equal(0, result.EmittersHandled);
        Assert.Contains(result.Notes,
            note => note.Contains("particle emitter 0", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Apply_FailsClosedWhenParticleTableIsTruncated()
    {
        byte[] source = BuildM2([[0xFF00FF00, 0x80008800, 0x40004400]], [-1]);
        WriteU32(source, 0x13C, 2);
        byte[] before = (byte[])source.Clone();

        M2ParticleColorHueWriter.Result result =
            M2ParticleColorHueWriter.Apply(source, 25f, 0.75f);

        Assert.False(result.IsComplete);
        Assert.Same(source, result.M2);
        Assert.Equal(before, source);
        Assert.Equal(2, result.CandidateEmitters);
        Assert.Equal(0, result.EmittersHandled);
        Assert.Equal(2, result.EmittersSkipped);
        Assert.Equal(0, result.EmittersChanged);
        Assert.Equal(0, result.ColorKeysHandled);
        Assert.Equal(0, result.ColorKeysChanged);
        Assert.Contains(result.Notes,
            note => note.Contains("truncated", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Apply_FailsClosedWhenParticleTablePointsIntoHeader()
    {
        byte[] source = BuildM2([[0xFF00FF00, 0x80008800, 0x40004400]], [-1]);
        WriteU32(source, 0x140, 0x20);
        byte[] before = (byte[])source.Clone();

        M2ParticleColorHueWriter.Result result =
            M2ParticleColorHueWriter.Apply(source, 25f, 0.75f);

        Assert.False(result.IsComplete);
        Assert.Same(source, result.M2);
        Assert.Equal(before, source);
        Assert.Equal(0, result.EmittersHandled);
        Assert.Equal(0, result.EmittersChanged);
    }

    [Theory]
    [InlineData("bad magic", false, 256u)]
    [InlineData("bad version", true, 264u)]
    public void Apply_FailsClosedForNonCanonicalM2(string _, bool validMagic, uint version)
    {
        byte[] source = BuildM2([[0xFF00FF00, 0x80008800, 0x40004400]], [-1]);
        if (!validMagic) Encoding.ASCII.GetBytes("XX20").CopyTo(source, 0);
        WriteU32(source, 0x04, version);

        M2ParticleColorHueWriter.Result result =
            M2ParticleColorHueWriter.Apply(source, 90f, 0.8f);

        Assert.False(result.IsComplete);
        Assert.Same(source, result.M2);
        Assert.Equal(0, result.EmittersHandled);
        Assert.Equal(0, result.EmittersChanged);
    }

    [Fact]
    public void Apply_NoParticles_IsACompleteNoOp()
    {
        byte[] source = BuildM2([], []);

        M2ParticleColorHueWriter.Result result =
            M2ParticleColorHueWriter.Apply(source, -60f, 0.8f);

        Assert.True(result.IsComplete);
        Assert.Same(source, result.M2);
        Assert.Equal(0, result.CandidateEmitters);
        Assert.Equal(0, result.EmittersHandled);
        Assert.Equal(0, result.EmittersChanged);
        Assert.Equal(0, result.ColorKeysHandled);
        Assert.Equal(0, result.ColorKeysChanged);
    }

    [Fact]
    public void Apply_UnchangedColors_AreStillSafelyHandled()
    {
        byte[] source = BuildM2(
            [[0xFFFF0000, 0x80FF0000, 0x20FF0000]],
            [999]);

        M2ParticleColorHueWriter.Result result =
            M2ParticleColorHueWriter.Apply(source, 360f, 1f);

        Assert.True(result.IsComplete);
        Assert.Same(source, result.M2);
        Assert.Equal(1, result.CandidateEmitters);
        Assert.Equal(1, result.EmittersHandled);
        Assert.Equal(0, result.EmittersSkipped);
        Assert.Equal(0, result.EmittersChanged);
        Assert.Equal(3, result.ColorKeysHandled);
        Assert.Equal(0, result.ColorKeysChanged);
    }

    [Fact]
    public void Apply_PreservesBlackAndWhiteLightnessWhileHandlingTheirKeys()
    {
        byte[] source = BuildM2(
            [[0xFF000000, 0x80FFFFFF, 0x40808080]],
            [-500]);

        M2ParticleColorHueWriter.Result result =
            M2ParticleColorHueWriter.Apply(source, 210f, 1f);

        Assert.True(result.IsComplete);
        Assert.Equal(1, result.EmittersHandled);
        Assert.Equal(1, result.EmittersChanged);
        Assert.Equal(1, result.ColorKeysChanged);
        Assert.Equal(0xFF000000u, ReadColor(result.M2, 0, 0));
        Assert.Equal(0x80FFFFFFu, ReadColor(result.M2, 0, 1));
        Assert.Equal(0x40808080u >> 24, ReadColor(result.M2, 0, 2) >> 24);
    }

    private static byte[] BuildM2(
        uint[][] ramps,
        int[] emitterIds,
        ushort[]? textureSlots = null,
        int textureCount = 0)
    {
        Assert.Equal(ramps.Length, emitterIds.Length);
        if (textureSlots is not null) Assert.Equal(ramps.Length, textureSlots.Length);
        Assert.True(textureCount >= 0);
        int tableOffset = HeaderSize;
        int textureTableOffset = tableOffset + ramps.Length * RecordSize;
        var result = new byte[textureTableOffset + textureCount * 16];
        Encoding.ASCII.GetBytes("MD20").CopyTo(result, 0);
        WriteU32(result, 0x04, 256);
        WriteU32(result, 0x5C, (uint)textureCount);
        WriteU32(result, 0x60, textureCount == 0 ? 0u : (uint)textureTableOffset);
        WriteU32(result, 0x13C, (uint)ramps.Length);
        WriteU32(result, 0x140, ramps.Length == 0 ? 0u : (uint)tableOffset);

        for (int emitter = 0; emitter < ramps.Length; emitter++)
        {
            Assert.Equal(3, ramps[emitter].Length);
            int record = tableOffset + emitter * RecordSize;
            WriteU32(result, record, unchecked((uint)emitterIds[emitter]));
            for (int key = 0; key < 3; key++)
                WriteU32(result, record + RampOffset + key * 4, ramps[emitter][key]);

            // Pattern all non-ramp bytes so accidental writes are easy to detect.
            for (int offset = 4; offset < RecordSize; offset++)
            {
                if (offset >= RampOffset && offset < RampOffset + 12) continue;
                result[record + offset] = unchecked((byte)(offset * 29 + emitter * 17 + 3));
            }
            if (textureSlots is not null)
                BinaryPrimitives.WriteUInt16LittleEndian(
                    result.AsSpan(record + 22, 2), textureSlots[emitter]);
        }

        return result;
    }

    private static uint ReadColor(byte[] data, int emitterIndex, int keyIndex)
    {
        int offset = HeaderSize + emitterIndex * RecordSize + RampOffset + keyIndex * 4;
        return BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));
    }

    private static void AssertOnlyRampRgbMayDiffer(byte[] before, byte[] after, int emitterCount) =>
        AssertOnlyRampRgbMayDiffer(before, after, Enumerable.Range(0, emitterCount));

    private static void AssertOnlyRampRgbMayDiffer(
        byte[] before,
        byte[] after,
        IEnumerable<int> emitterIndices)
    {
        var allowed = new HashSet<int>();
        foreach (int emitter in emitterIndices)
        {
            int ramp = HeaderSize + emitter * RecordSize + RampOffset;
            for (int key = 0; key < 3; key++)
            {
                // On-disk little-endian AARRGGBB bytes are B, G, R, A. Alpha is excluded.
                allowed.Add(ramp + key * 4);
                allowed.Add(ramp + key * 4 + 1);
                allowed.Add(ramp + key * 4 + 2);
            }
        }

        Assert.Equal(before.Length, after.Length);
        for (int offset = 0; offset < before.Length; offset++)
        {
            if (!allowed.Contains(offset))
                Assert.Equal(before[offset], after[offset]);
        }
    }

    private static void ToHsl(uint argb, out float hue, out float saturation, out float lightness)
    {
        float red = ((argb >> 16) & 0xFF) / 255f;
        float green = ((argb >> 8) & 0xFF) / 255f;
        float blue = (argb & 0xFF) / 255f;
        float max = MathF.Max(red, MathF.Max(green, blue));
        float min = MathF.Min(red, MathF.Min(green, blue));
        float delta = max - min;
        lightness = (max + min) * 0.5f;
        if (delta < 0.000001f)
        {
            hue = 0f;
            saturation = 0f;
            return;
        }

        saturation = lightness > 0.5f
            ? delta / (2f - max - min)
            : delta / (max + min);
        if (max == red) hue = ((green - blue) / delta + (green < blue ? 6f : 0f)) * 60f;
        else if (max == green) hue = ((blue - red) / delta + 2f) * 60f;
        else hue = ((red - green) / delta + 4f) * 60f;
    }

    private static float CircularHueDistance(float first, float second)
    {
        float difference = MathF.Abs(first - second) % 360f;
        return MathF.Min(difference, 360f - difference);
    }

    private static void WriteU32(byte[] bytes, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset, 4), value);
}
