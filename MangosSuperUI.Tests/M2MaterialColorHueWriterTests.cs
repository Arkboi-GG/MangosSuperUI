using System.Buffers.Binary;
using System.Numerics;
using MangosSuperUI.Services.WeaponForge.RawM2;
using Xunit;

namespace MangosSuperUI.Tests;

public sealed class M2MaterialColorHueWriterTests
{
    [Fact]
    public void Apply_ShiftsEveryLinearRgbKey_AndChangesNoOtherBytes()
    {
        Fixture fixture = BuildFixture(
            [new ColorSpec([new Vector3(0.05f, 0.55f, 0.05f), new Vector3(0.2f, 0.9f, 0.2f)])],
            [new BatchSpec(0, 4, 0)]);
        byte[] before = (byte[])fixture.M2.Clone();

        M2MaterialColorHueWriter.Result result =
            M2MaterialColorHueWriter.Apply(fixture.M2, 300f, 0.65f);

        Assert.Equal(1, result.ColorsShifted);
        Assert.Equal(2, result.VectorKeysShifted);
        Assert.Equal(1, result.ColorsChanged);
        Assert.Equal(2, result.VectorKeysChanged);
        Assert.Equal([0], result.CandidateColorIndices);
        Assert.Equal([0], result.ShiftedColorIndices);
        Assert.Empty(result.SkippedCandidateColorIndices);
        Assert.True(result.IsComplete);
        Assert.NotSame(fixture.M2, result.M2);
        Assert.Equal(before.Length, result.M2.Length);
        AssertOnlyRgbValuesMayDiffer(before, result.M2, fixture.RgbValueOffsets[0]);

        foreach (int offset in fixture.RgbValueOffsets[0])
        {
            Vector3 source = ReadVector(before, offset);
            Vector3 shifted = ReadVector(result.M2, offset);
            ToHsl(source, out _, out _, out float sourceLightness);
            ToHsl(shifted, out float hue, out float saturation, out float shiftedLightness);
            Assert.InRange(CircularHueDistance(hue, 300f), 0f, 0.001f);
            Assert.InRange(MathF.Abs(saturation - 0.65f), 0f, 0.0001f);
            Assert.InRange(MathF.Abs(shiftedLightness - sourceLightness), 0f, 0.0001f);
        }
    }

    [Fact]
    public void Apply_FailsClosedWhenAnotherViewUsesTheColorInAnOpaqueBatch()
    {
        Fixture fixture = BuildFixture(
            [new ColorSpec([new Vector3(0.1f, 0.8f, 0.1f)])],
            [new BatchSpec(0, 4, 0), new BatchSpec(0, 0, 1)]);

        M2MaterialColorHueWriter.Result result =
            M2MaterialColorHueWriter.Apply(fixture.M2, 20f, 0.8f);

        Assert.Same(fixture.M2, result.M2);
        Assert.Equal(0, result.ColorsShifted);
        Assert.Equal(0, result.VectorKeysShifted);
        Assert.Equal(0, result.ColorsChanged);
        Assert.Equal(0, result.VectorKeysChanged);
        Assert.Equal([0], result.CandidateColorIndices);
        Assert.Empty(result.ShiftedColorIndices);
        Assert.Equal([0], result.SkippedCandidateColorIndices);
        Assert.False(result.IsComplete);
        Assert.Contains(result.Notes, note => note.Contains("opaque", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Apply_FailsClosedForMalformedAlphaTrack()
    {
        Fixture fixture = BuildFixture(
            [new ColorSpec([new Vector3(0.1f, 0.8f, 0.1f)], MalformedAlpha: true)],
            [new BatchSpec(0, 6, 0)]);

        M2MaterialColorHueWriter.Result result =
            M2MaterialColorHueWriter.Apply(fixture.M2, 210f, 0.75f);

        Assert.Same(fixture.M2, result.M2);
        Assert.Equal(0, result.ColorsShifted);
        Assert.Equal([0], result.CandidateColorIndices);
        Assert.Empty(result.ShiftedColorIndices);
        Assert.Equal([0], result.SkippedCandidateColorIndices);
        Assert.False(result.IsComplete);
        Assert.Contains(result.Notes, note => note.Contains("alpha", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Apply_StillShiftsAnIndependentSafeColorWhenAnotherCandidateIsMalformed()
    {
        Fixture fixture = BuildFixture(
            [
                new ColorSpec([new Vector3(0.1f, 0.8f, 0.1f)], MalformedAlpha: true),
                new ColorSpec([new Vector3(0.15f, 0.75f, 0.15f)]),
            ],
            [new BatchSpec(0, 4, 0), new BatchSpec(1, 6, 0)]);
        byte[] before = (byte[])fixture.M2.Clone();

        M2MaterialColorHueWriter.Result result =
            M2MaterialColorHueWriter.Apply(fixture.M2, 45f, 0.9f);

        Assert.Equal(1, result.ColorsShifted);
        Assert.Equal(1, result.VectorKeysShifted);
        Assert.Equal(1, result.ColorsChanged);
        Assert.Equal(1, result.VectorKeysChanged);
        Assert.Equal([0, 1], result.CandidateColorIndices);
        Assert.Equal([1], result.ShiftedColorIndices);
        Assert.Equal([0], result.SkippedCandidateColorIndices);
        Assert.False(result.IsComplete);
        Assert.Equal(
            ReadVector(before, fixture.RgbValueOffsets[0][0]),
            ReadVector(result.M2, fixture.RgbValueOffsets[0][0]));
        Assert.NotEqual(
            ReadVector(before, fixture.RgbValueOffsets[1][0]),
            ReadVector(result.M2, fixture.RgbValueOffsets[1][0]));
        AssertOnlyRgbValuesMayDiffer(before, result.M2, fixture.RgbValueOffsets[1]);
    }

    [Fact]
    public void Apply_RefusesMultiKeyCubicTrackWithoutChangingTangentsOrValues()
    {
        Fixture fixture = BuildFixture(
            [new ColorSpec(
                [new Vector3(0.1f, 0.7f, 0.1f), new Vector3(0.2f, 0.9f, 0.2f)],
                Interpolation: 2)],
            [new BatchSpec(0, 4, 0)]);

        M2MaterialColorHueWriter.Result result =
            M2MaterialColorHueWriter.Apply(fixture.M2, 275f, 0.7f);

        Assert.Same(fixture.M2, result.M2);
        Assert.Equal(0, result.ColorsShifted);
        Assert.Equal([0], result.CandidateColorIndices);
        Assert.Empty(result.ShiftedColorIndices);
        Assert.Equal([0], result.SkippedCandidateColorIndices);
        Assert.False(result.IsComplete);
        Assert.Contains(result.Notes, note => note.Contains("cubic", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Apply_ShiftsSingleKeyCubicValue_WhilePreservingItsTangentPayload()
    {
        Fixture fixture = BuildFixture(
            [new ColorSpec([new Vector3(0.1f, 0.7f, 0.1f)], Interpolation: 3)],
            [new BatchSpec(0, 4, 0)]);
        byte[] before = (byte[])fixture.M2.Clone();

        M2MaterialColorHueWriter.Result result =
            M2MaterialColorHueWriter.Apply(fixture.M2, 180f, 0.8f);

        Assert.Equal(1, result.ColorsShifted);
        Assert.Equal(1, result.VectorKeysShifted);
        Assert.Equal(1, result.ColorsChanged);
        Assert.Equal(1, result.VectorKeysChanged);
        Assert.Equal([0], result.CandidateColorIndices);
        Assert.Equal([0], result.ShiftedColorIndices);
        Assert.Empty(result.SkippedCandidateColorIndices);
        Assert.True(result.IsComplete);
        AssertOnlyRgbValuesMayDiffer(before, result.M2, fixture.RgbValueOffsets[0]);
    }

    [Fact]
    public void Apply_HandlesWhiteKeyCompletelyWithoutClaimingAByteChange()
    {
        Fixture fixture = BuildFixture(
            [new ColorSpec([Vector3.One])],
            [new BatchSpec(0, 4, 0)]);

        M2MaterialColorHueWriter.Result result =
            M2MaterialColorHueWriter.Apply(fixture.M2, 275f, 0.9f);

        Assert.Same(fixture.M2, result.M2);
        Assert.Equal([0], result.CandidateColorIndices);
        Assert.Equal([0], result.ShiftedColorIndices);
        Assert.Empty(result.SkippedCandidateColorIndices);
        Assert.Equal(1, result.ColorsHandled);
        Assert.Equal(0, result.ColorsShifted);
        Assert.Equal(0, result.ColorsChanged);
        Assert.Equal(0, result.VectorKeysChanged);
        Assert.Equal(0, result.VectorKeysShifted);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public void Apply_FailsClosedWhenRgbStorageAliasesAnUntargetedColor()
    {
        Fixture fixture = BuildFixture(
            [
                new ColorSpec([new Vector3(0.1f, 0.8f, 0.1f)]),
                new ColorSpec([new Vector3(0.2f, 0.7f, 0.2f)]),
            ],
            [new BatchSpec(0, 4, 0)]);
        int firstKeyOffset = fixture.RgbValueOffsets[0][0];
        WriteU32(fixture.M2, fixture.ColorRecordOffsets[1] + 24, (uint)firstKeyOffset);

        M2MaterialColorHueWriter.Result result =
            M2MaterialColorHueWriter.Apply(fixture.M2, 10f, 1f);

        Assert.Same(fixture.M2, result.M2);
        Assert.Equal(0, result.ColorsShifted);
        Assert.Equal([0], result.CandidateColorIndices);
        Assert.Empty(result.ShiftedColorIndices);
        Assert.Equal([0], result.SkippedCandidateColorIndices);
        Assert.False(result.IsComplete);
        Assert.Contains(result.Notes, note => note.Contains("alias", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Apply_RejectsNonCanonicalVersionWithoutMutation()
    {
        Fixture fixture = BuildFixture(
            [new ColorSpec([new Vector3(0.1f, 0.8f, 0.1f)])],
            [new BatchSpec(0, 4, 0)]);
        WriteU32(fixture.M2, 0x04, 264);

        M2MaterialColorHueWriter.Result result =
            M2MaterialColorHueWriter.Apply(fixture.M2, 10f, 1f);

        Assert.Same(fixture.M2, result.M2);
        Assert.Equal(0, result.ColorsShifted);
        Assert.Empty(result.CandidateColorIndices);
        Assert.Empty(result.ShiftedColorIndices);
        Assert.Empty(result.SkippedCandidateColorIndices);
        Assert.False(result.IsComplete);
    }

    [Fact]
    public void Apply_ReportsCompleteForCanonicalModelWithNoColorRecords()
    {
        Fixture fixture = BuildFixture([], [new BatchSpec(-1, 0, 0)]);

        M2MaterialColorHueWriter.Result result =
            M2MaterialColorHueWriter.Apply(fixture.M2, 120f, 0.8f);

        Assert.Same(fixture.M2, result.M2);
        Assert.Empty(result.CandidateColorIndices);
        Assert.Empty(result.ShiftedColorIndices);
        Assert.Empty(result.SkippedCandidateColorIndices);
        Assert.Equal(0, result.ColorsShifted);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public void Apply_ReportsCompleteWhenNoBatchReferencesACompositingColor()
    {
        Fixture fixture = BuildFixture(
            [new ColorSpec([new Vector3(0.1f, 0.8f, 0.1f)])],
            [new BatchSpec(-1, 0, 0)]);

        M2MaterialColorHueWriter.Result result =
            M2MaterialColorHueWriter.Apply(fixture.M2, 120f, 0.8f);

        Assert.Same(fixture.M2, result.M2);
        Assert.Empty(result.CandidateColorIndices);
        Assert.Empty(result.ShiftedColorIndices);
        Assert.Empty(result.SkippedCandidateColorIndices);
        Assert.Equal(0, result.ColorsShifted);
        Assert.True(result.IsComplete);
    }

    [Fact]
    public void Apply_WithNativeTextureBoundary_ShiftsTypeZeroColorButLeavesTypeThreeColorUntouched()
    {
        Fixture fixture = BuildFixture(
            [
                new ColorSpec([new Vector3(0.1f, 0.8f, 0.1f)]),
                new ColorSpec([new Vector3(0.2f, 0.7f, 0.2f)]),
            ],
            [
                new BatchSpec(0, 4, 0, TextureSlot: 0),
                new BatchSpec(1, 4, 0, TextureSlot: 1),
            ]);
        byte[] before = (byte[])fixture.M2.Clone();

        M2MaterialColorHueWriter.Result result =
            M2MaterialColorHueWriter.Apply(
                fixture.M2, 280f, 0.7f, eligibleTextureIndices: [0]);

        Assert.True(result.IsComplete);
        Assert.Equal([0], result.CandidateColorIndices);
        Assert.Equal([0], result.ShiftedColorIndices);
        Assert.NotEqual(
            ReadVector(before, fixture.RgbValueOffsets[0][0]),
            ReadVector(result.M2, fixture.RgbValueOffsets[0][0]));
        Assert.Equal(
            ReadVector(before, fixture.RgbValueOffsets[1][0]),
            ReadVector(result.M2, fixture.RgbValueOffsets[1][0]));
        AssertOnlyRgbValuesMayDiffer(before, result.M2, fixture.RgbValueOffsets[0]);
    }

    [Fact]
    public void Apply_WithNativeTextureBoundary_DoesNotAdmitAMixedTypeZeroTypeThreeBatch()
    {
        Fixture fixture = BuildFixture(
            [
                new ColorSpec([new Vector3(0.1f, 0.8f, 0.1f)]),
                new ColorSpec([new Vector3(0.2f, 0.7f, 0.2f)]),
            ],
            [
                new BatchSpec(0, 4, 0, TextureSlot: 0),
                new BatchSpec(1, 4, 0, TextureSlot: 1),
            ]);
        WriteU16(fixture.M2, fixture.BatchRecordOffsets[0] + 14, 2);

        M2MaterialColorHueWriter.Result result =
            M2MaterialColorHueWriter.Apply(
                fixture.M2, 280f, 0.7f, eligibleTextureIndices: [0]);

        Assert.True(result.IsComplete);
        Assert.Same(fixture.M2, result.M2);
        Assert.Empty(result.CandidateColorIndices);
        Assert.Empty(result.ShiftedColorIndices);
        Assert.Equal(0, result.ColorsChanged);
    }

    private static Fixture BuildFixture(ColorSpec[] colors, BatchSpec[] batches)
    {
        int viewCount = Math.Max(1, batches.Max(batch => batch.View) + 1);
        int viewOffset = RawM2Document.VanillaHeaderSize;
        int renderFlagOffset = Align4(viewOffset + viewCount * RawM2View.HeaderStride);
        int batchOffset = Align4(renderFlagOffset + batches.Length * 4);
        int afterBatches = batchOffset + batches.Length * 24;
        bool hasTextureGraph = batches.Any(batch => batch.TextureSlot.HasValue);
        int textureCount = hasTextureGraph
            ? batches.Where(batch => batch.TextureSlot.HasValue)
                .Max(batch => batch.TextureSlot!.Value) + 1
            : 0;
        int textureOffset = hasTextureGraph ? Align4(afterBatches) : 0;
        int textureLookupOffset = hasTextureGraph
            ? Align4(textureOffset + textureCount * 16)
            : 0;
        int colorOffset = Align4(hasTextureGraph
            ? textureLookupOffset + textureCount * sizeof(ushort)
            : afterBatches);
        var data = new List<byte>(new byte[colorOffset + colors.Length * 56]);

        data[0] = (byte)'M';
        data[1] = (byte)'D';
        data[2] = (byte)'2';
        data[3] = (byte)'0';
        WriteU32(data, 0x04, 256);
        WriteU32(data, 0x4C, (uint)viewCount);
        WriteU32(data, 0x50, (uint)viewOffset);
        WriteU32(data, 0x54, (uint)colors.Length);
        WriteU32(data, 0x58, (uint)colorOffset);
        WriteU32(data, 0x5C, (uint)textureCount);
        WriteU32(data, 0x60, hasTextureGraph ? (uint)textureOffset : 0u);
        WriteU32(data, 0x84, (uint)batches.Length);
        WriteU32(data, 0x88, (uint)renderFlagOffset);
        WriteU32(data, 0x94, (uint)textureCount);
        WriteU32(data, 0x98, hasTextureGraph ? (uint)textureLookupOffset : 0u);
        for (ushort textureIndex = 0; textureIndex < textureCount; textureIndex++)
            WriteU16(data, textureLookupOffset + textureIndex * sizeof(ushort), textureIndex);

        int materialIndex = 0;
        int nextBatch = batchOffset;
        var batchRecordOffsets = new List<int>(batches.Length);
        for (int view = 0; view < viewCount; view++)
        {
            BatchSpec[] viewBatches = batches.Where(batch => batch.View == view).ToArray();
            int header = viewOffset + view * RawM2View.HeaderStride;
            WriteU32(data, header + 32, (uint)viewBatches.Length);
            WriteU32(data, header + 36, viewBatches.Length == 0 ? 0u : (uint)nextBatch);

            foreach (BatchSpec batch in viewBatches)
            {
                batchRecordOffsets.Add(nextBatch);
                WriteI16(data, nextBatch + 8, batch.ColorIndex);
                WriteU16(data, nextBatch + 10, (ushort)materialIndex);
                if (batch.TextureSlot is { } textureSlot)
                {
                    WriteU16(data, nextBatch + 14, 1);
                    WriteU16(data, nextBatch + 16, textureSlot);
                }
                WriteU16(data, renderFlagOffset + materialIndex * 4 + 2, batch.BlendMode);
                materialIndex++;
                nextBatch += 24;
            }
        }

        var rgbOffsets = new Dictionary<int, int[]>();
        var records = new int[colors.Length];
        for (int colorIndex = 0; colorIndex < colors.Length; colorIndex++)
        {
            int record = colorOffset + colorIndex * 56;
            records[colorIndex] = record;
            ColorSpec color = colors[colorIndex];
            rgbOffsets[colorIndex] = WriteRgbTrack(data, record, color.Keys, color.Interpolation);
            WriteAlphaTrack(data, record + 28);
            if (color.MalformedRgb)
                WriteU32(data, record + 12, (uint)(color.Keys.Length + 1));
            if (color.MalformedAlpha)
                WriteU32(data, record + 28 + 12, 2);
        }

        return new Fixture(data.ToArray(), rgbOffsets, records, batchRecordOffsets);
    }

    private static int[] WriteRgbTrack(
        List<byte> data,
        int track,
        Vector3[] keys,
        ushort interpolation)
    {
        int rangeOffset = Append(data, 8);
        WriteU32(data, rangeOffset, 0);
        WriteU32(data, rangeOffset + 4, (uint)(keys.Length - 1));

        int timeOffset = Append(data, keys.Length * 4);
        for (int key = 0; key < keys.Length; key++)
            WriteU32(data, timeOffset + key * 4, (uint)(key * 500));

        int storedStride = interpolation is 2 or 3 ? 36 : 12;
        int keyOffset = Append(data, keys.Length * storedStride);
        var valueOffsets = new int[keys.Length];
        for (int key = 0; key < keys.Length; key++)
        {
            int value = keyOffset + key * storedStride;
            valueOffsets[key] = value;
            WriteVector(data, value, keys[key]);
            if (storedStride == 36)
            {
                WriteVector(data, value + 12, new Vector3(0.125f, 0.25f, 0.375f));
                WriteVector(data, value + 24, new Vector3(0.5f, 0.625f, 0.75f));
            }
        }

        WriteTrackHeader(data, track, interpolation, (uint)keys.Length,
            rangeOffset, timeOffset, keyOffset);
        return valueOffsets;
    }

    private static void WriteAlphaTrack(List<byte> data, int track)
    {
        int rangeOffset = Append(data, 8);
        WriteU32(data, rangeOffset, 0);
        WriteU32(data, rangeOffset + 4, 0);
        int timeOffset = Append(data, 4);
        WriteU32(data, timeOffset, 0);
        int keyOffset = Append(data, 2);
        WriteI16(data, keyOffset, 24576);
        WriteTrackHeader(data, track, 1, 1, rangeOffset, timeOffset, keyOffset);
    }

    private static void WriteTrackHeader(
        List<byte> data,
        int track,
        ushort interpolation,
        uint keyCount,
        int rangeOffset,
        int timeOffset,
        int keyOffset)
    {
        WriteU16(data, track, interpolation);
        WriteI16(data, track + 2, -1);
        WriteU32(data, track + 4, 1);
        WriteU32(data, track + 8, (uint)rangeOffset);
        WriteU32(data, track + 12, keyCount);
        WriteU32(data, track + 16, (uint)timeOffset);
        WriteU32(data, track + 20, keyCount);
        WriteU32(data, track + 24, (uint)keyOffset);
    }

    private static int Append(List<byte> data, int count)
    {
        while (data.Count % 4 != 0) data.Add(0);
        int offset = data.Count;
        for (int index = 0; index < count; index++) data.Add(0);
        return offset;
    }

    private static int Align4(int value) => (value + 3) & ~3;

    private static void AssertOnlyRgbValuesMayDiffer(
        byte[] before,
        byte[] after,
        IReadOnlyList<int> valueOffsets)
    {
        Assert.Equal(before.Length, after.Length);
        var mutable = new bool[before.Length];
        foreach (int offset in valueOffsets)
            for (int index = 0; index < 12; index++) mutable[offset + index] = true;

        for (int offset = 0; offset < before.Length; offset++)
        {
            if (!mutable[offset]) Assert.Equal(before[offset], after[offset]);
        }
    }

    private static Vector3 ReadVector(byte[] data, int offset) => new(
        BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset, 4)),
        BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset + 4, 4)),
        BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(offset + 8, 4)));

    private static void ToHsl(
        Vector3 color,
        out float hue,
        out float saturation,
        out float lightness)
    {
        float max = MathF.Max(color.X, MathF.Max(color.Y, color.Z));
        float min = MathF.Min(color.X, MathF.Min(color.Y, color.Z));
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
        if (max == color.X) hue = ((color.Y - color.Z) / delta + (color.Y < color.Z ? 6f : 0f)) * 60f;
        else if (max == color.Y) hue = ((color.Z - color.X) / delta + 2f) * 60f;
        else hue = ((color.X - color.Y) / delta + 4f) * 60f;
    }

    private static float CircularHueDistance(float a, float b)
    {
        float distance = MathF.Abs(a - b) % 360f;
        return MathF.Min(distance, 360f - distance);
    }

    private static void WriteVector(List<byte> data, int offset, Vector3 value)
    {
        WriteF32(data, offset, value.X);
        WriteF32(data, offset + 4, value.Y);
        WriteF32(data, offset + 8, value.Z);
    }

    private static void WriteF32(List<byte> data, int offset, float value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteSingleLittleEndian(bytes, value);
        for (int index = 0; index < bytes.Length; index++) data[offset + index] = bytes[index];
    }

    private static void WriteU16(List<byte> data, int offset, ushort value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
        for (int index = 0; index < bytes.Length; index++) data[offset + index] = bytes[index];
    }

    private static void WriteI16(List<byte> data, int offset, short value)
    {
        Span<byte> bytes = stackalloc byte[2];
        BinaryPrimitives.WriteInt16LittleEndian(bytes, value);
        for (int index = 0; index < bytes.Length; index++) data[offset + index] = bytes[index];
    }

    private static void WriteU32(List<byte> data, int offset, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        for (int index = 0; index < bytes.Length; index++) data[offset + index] = bytes[index];
    }

    private static void WriteU32(byte[] data, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, 4), value);

    private static void WriteU16(byte[] data, int offset, ushort value) =>
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset, 2), value);

    private sealed record ColorSpec(
        Vector3[] Keys,
        ushort Interpolation = 1,
        bool MalformedRgb = false,
        bool MalformedAlpha = false);

    private sealed record BatchSpec(
        short ColorIndex,
        ushort BlendMode,
        int View,
        ushort? TextureSlot = null);

    private sealed record Fixture(
        byte[] M2,
        IReadOnlyDictionary<int, int[]> RgbValueOffsets,
        IReadOnlyList<int> ColorRecordOffsets,
        IReadOnlyList<int> BatchRecordOffsets);
}
