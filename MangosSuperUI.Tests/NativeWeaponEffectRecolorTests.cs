using System.Buffers.Binary;
using System.Numerics;
using MangosSuperUI.Services;
using MangosSuperUI.Services.WeaponForge;
using MangosSuperUI.Services.WeaponForge.RawM2;
using SkiaSharp;
using Xunit;

namespace MangosSuperUI.Tests;

public sealed class NativeWeaponEffectRecolorTests
{
    [Fact]
    public void Selector_UsesEveryTextureUnit_AndKeepsOnlyCompositeTypeZeroAssets()
    {
        var model = new M2Model
        {
            Textures =
            [
                new M2TextureRef { Type = 2 },
                new M2TextureRef { Type = 0, Filename = @"Item\ObjectComponents\Weapon\Glave_Effect.blp" },
                new M2TextureRef { Type = 3, Filename = @"Item\ObjectComponents\Weapon\ArmorReflect4.blp" },
                new M2TextureRef { Type = 0, Filename = @"Item\ObjectComponents\Weapon\OpaqueDetail.blp" },
                new M2TextureRef { Type = 0, Filename = @"Item\ObjectComponents\Weapon\UnusedEffect.blp" },
                new M2TextureRef { Type = 0, Filename = "   " },
            ],
            TextureLookup = [0, 1, 1, 2, 3],
            RenderFlags =
            [
                new M2RenderFlag { BlendingMode = 0 },
                new M2RenderFlag { BlendingMode = 4 },
                new M2RenderFlag { BlendingMode = 6 },
            ],
            Batches =
            [
                new M2Batch { TextureIndex = 0, TextureCount = 1, MaterialIndex = 0 },
                new M2Batch { TextureIndex = 1, TextureCount = 1, MaterialIndex = 2 },
                // The effect is unit zero and Type-3 reflect is unit one. The selector must walk
                // both units through TextureLookup rather than treating TextureIndex as a slot.
                new M2Batch { TextureIndex = 2, TextureCount = 2, MaterialIndex = 1 },
                new M2Batch { TextureIndex = 4, TextureCount = 1, MaterialIndex = 0 },
            ],
        };

        IReadOnlyList<NativeWeaponEffectTexture> selected =
            NativeWeaponEffectRecolor.SelectEligibleTextures(model);

        NativeWeaponEffectTexture effect = Assert.Single(selected);
        Assert.Equal(@"Item\ObjectComponents\Weapon\Glave_Effect.blp", effect.SourcePath);
        Assert.Equal([1], effect.TextureIndices);
    }

    [Fact]
    public void Selector_DisqualifiesARepeatedPathWhenAnyAliasHasAnOpaqueUse()
    {
        var model = new M2Model
        {
            Textures =
            [
                new M2TextureRef { Type = 0, Filename = @"Effects\BladeGlow.blp" },
                new M2TextureRef { Type = 0, Filename = "effects/bladeglow.BLP" },
            ],
            TextureLookup = [0, 1],
            RenderFlags =
            [
                new M2RenderFlag { BlendingMode = 6 },
                new M2RenderFlag { BlendingMode = 0 },
            ],
            Batches =
            [
                new M2Batch { TextureIndex = 0, TextureCount = 1, MaterialIndex = 0 },
                new M2Batch { TextureIndex = 1, TextureCount = 1, MaterialIndex = 1 },
            ],
        };

        Assert.Empty(NativeWeaponEffectRecolor.SelectEligibleTextures(model));
    }

    [Fact]
    public void Selector_FailsClosedWhenAUseHasNoValidRenderFlag()
    {
        var model = new M2Model
        {
            Textures = [new M2TextureRef { Type = 0, Filename = @"Effects\BladeGlow.blp" }],
            TextureLookup = [0],
            RenderFlags = [new M2RenderFlag { BlendingMode = 6 }],
            Batches = [new M2Batch { TextureIndex = 0, TextureCount = 1, MaterialIndex = 7 }],
        };

        Assert.Empty(NativeWeaponEffectRecolor.SelectEligibleTextures(model));
    }

    [Fact]
    public void Selector_CountsEmitterOnlyTypeZeroPathAsUsed_WithoutSynthesizingTypeThree()
    {
        var model = new M2Model
        {
            Textures =
            [
                new M2TextureRef { Type = 0, Filename = @"Effects\EmitterGlow.blp" },
                new M2TextureRef { Type = 3, Filename = @"Effects\TypeThreeOnly.blp" },
            ],
            ParticleEmitters =
            [
                new M2ParticleEmitterInfo(Vector3.Zero, @"effects/emitterglow.BLP", null, 1f, 4),
                new M2ParticleEmitterInfo(Vector3.Zero, @"Effects\TypeThreeOnly.blp", null, 1f, 4),
            ],
        };

        NativeWeaponEffectTexture selected = Assert.Single(
            NativeWeaponEffectRecolor.SelectEligibleTextures(model));
        Assert.Equal(@"Effects\EmitterGlow.blp", selected.SourcePath);
        Assert.Equal([0], selected.TextureIndices);
    }

    [Fact]
    public void Selector_CountsRawParticleOnlyTypeZeroSlotAsUsed()
    {
        RawUsageFixture fixture = BuildRawUsageM2([], includeParticle: true);
        var model = new M2Model
        {
            SourceBytes = fixture.Bytes,
            Textures = [new M2TextureRef { Type = 0, Filename = @"Effects\ParticleGlow.blp" }],
        };

        NativeWeaponEffectTexture selected = Assert.Single(
            NativeWeaponEffectRecolor.SelectEligibleTextures(model));
        Assert.Equal(@"Effects\ParticleGlow.blp", selected.SourcePath);
        Assert.Equal([0], selected.TextureIndices);
    }

    [Fact]
    public void Selector_ThrowsForUninspectableDeclaredParticleWithoutSourceBytes()
    {
        var model = new M2Model
        {
            ParticleEmitterCount = 1,
            Textures = [new M2TextureRef { Type = 0, Filename = @"Effects\ParticleGlow.blp" }],
            ParticleEmitters =
            [
                new M2ParticleEmitterInfo(Vector3.Zero, null, null, 1f, 4),
            ],
        };

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => NativeWeaponEffectRecolor.SelectEligibleTextures(model));
        Assert.Contains("particle", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Selector_UsesEveryRawView_AndDisqualifiesOpaqueAliasOutsideViewZero()
    {
        RawUsageFixture fixture = BuildRawUsageM2([4, 0]);
        var model = new M2Model
        {
            SourceBytes = fixture.Bytes,
            Textures = [new M2TextureRef { Type = 0, Filename = @"Effects\SharedGlow.blp" }],
            // If the selector accidentally falls back to the parsed view-0 list, this would pass.
            TextureLookup = [0],
            RenderFlags = [new M2RenderFlag { BlendingMode = 4 }],
            Batches = [new M2Batch { TextureIndex = 0, TextureCount = 1, MaterialIndex = 0 }],
        };

        Assert.Empty(NativeWeaponEffectRecolor.SelectEligibleTextures(model));
    }

    [Fact]
    public void Selector_CountsCompositeRibbonOnlyTypeZeroPathAsUsed()
    {
        // Texture/material arrays are independent: two texture entries share one material verdict.
        RawUsageFixture fixture = BuildRawUsageM2(
            [], ribbonBlendMode: 4, ribbonTextureEntryCount: 2);
        var model = new M2Model
        {
            SourceBytes = fixture.Bytes,
            RibbonEmitterCount = 1,
            Textures = [new M2TextureRef { Type = 0, Filename = @"SPELLS\ZAP1B.BLP" }],
        };

        NativeWeaponEffectTexture selected = Assert.Single(
            NativeWeaponEffectRecolor.SelectEligibleTextures(model));
        Assert.Equal(@"SPELLS\ZAP1B.BLP", selected.SourcePath);
        Assert.Equal([0], selected.TextureIndices);
    }

    [Fact]
    public void Selector_DisqualifiesRibbonPathWhenItsMaterialIsOpaque()
    {
        RawUsageFixture fixture = BuildRawUsageM2([], ribbonBlendMode: 0);
        var model = new M2Model
        {
            SourceBytes = fixture.Bytes,
            RibbonEmitterCount = 1,
            Textures = [new M2TextureRef { Type = 0, Filename = @"SPELLS\ZAP1B.BLP" }],
        };

        Assert.Empty(NativeWeaponEffectRecolor.SelectEligibleTextures(model));
    }

    [Fact]
    public void Selector_RejectsChromaticRibbonMultiplierInsteadOfClaimingTextureOnlySuccess()
    {
        RawUsageFixture fixture = BuildRawUsageM2([], ribbonBlendMode: 4);
        int colorKey = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
            fixture.Bytes.AsSpan(fixture.RibbonOffset + 60, 4)));
        WriteF32(fixture.Bytes, colorKey, 1f);
        WriteF32(fixture.Bytes, colorKey + 4, 0.2f);
        WriteF32(fixture.Bytes, colorKey + 8, 0.1f);
        var model = new M2Model
        {
            SourceBytes = fixture.Bytes,
            RibbonEmitterCount = 1,
            Textures = [new M2TextureRef { Type = 0, Filename = @"SPELLS\ZAP1B.BLP" }],
        };

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => NativeWeaponEffectRecolor.SelectEligibleTextures(model));

        Assert.Contains("chromatic", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Selector_RejectsChromaticCubicRibbonTangent()
    {
        RawUsageFixture fixture = BuildRawUsageM2([], ribbonBlendMode: 4);
        int colorTrack = fixture.RibbonOffset + 36;
        int colorKey = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
            fixture.Bytes.AsSpan(colorTrack + 24, 4)));
        WriteU16(fixture.Bytes, colorTrack, 2);
        WriteF32(fixture.Bytes, colorKey + 12, 1f);
        WriteF32(fixture.Bytes, colorKey + 16, 0f);
        WriteF32(fixture.Bytes, colorKey + 20, 0f);
        var model = new M2Model
        {
            SourceBytes = fixture.Bytes,
            RibbonEmitterCount = 1,
            Textures = [new M2TextureRef { Type = 0, Filename = @"SPELLS\ZAP1B.BLP" }],
        };

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => NativeWeaponEffectRecolor.SelectEligibleTextures(model));

        Assert.Contains("chromatic", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Selector_RejectsRibbonWhoseNeutralMultiplierCannotBeProven()
    {
        RawUsageFixture fixture = BuildRawUsageM2([], ribbonBlendMode: 4);
        int colorTrack = fixture.RibbonOffset + 36;
        WriteU32(fixture.Bytes, colorTrack + 12, 0);
        WriteU32(fixture.Bytes, colorTrack + 16, 0);
        WriteU32(fixture.Bytes, colorTrack + 20, 0);
        WriteU32(fixture.Bytes, colorTrack + 24, 0);
        var model = new M2Model
        {
            SourceBytes = fixture.Bytes,
            RibbonEmitterCount = 1,
            Textures = [new M2TextureRef { Type = 0, Filename = @"SPELLS\ZAP1B.BLP" }],
        };

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => NativeWeaponEffectRecolor.SelectEligibleTextures(model));

        Assert.Contains("cannot be proven", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Selector_ThrowsForMalformedRawRibbonTextureGraph()
    {
        RawUsageFixture fixture = BuildRawUsageM2([], ribbonBlendMode: 4);
        WriteU32(fixture.Bytes, fixture.RibbonOffset + 24, (uint)(fixture.Bytes.Length + 4));
        var model = new M2Model
        {
            SourceBytes = fixture.Bytes,
            RibbonEmitterCount = 1,
            Textures = [new M2TextureRef { Type = 0, Filename = @"SPELLS\ZAP1B.BLP" }],
        };

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => NativeWeaponEffectRecolor.SelectEligibleTextures(model));
        Assert.Contains("ribbon", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Selector_ThrowsWhenRibbonCountExistsWithoutSourceBytes()
    {
        var model = new M2Model
        {
            RibbonEmitterCount = 1,
            Textures = [new M2TextureRef { Type = 0, Filename = @"SPELLS\ZAP1B.BLP" }],
        };

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => NativeWeaponEffectRecolor.SelectEligibleTextures(model));
        Assert.Contains("ribbon", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TintPng_ChangesHueAndSaturation_WhileKeepingLightnessDimensionsAndAuthoredAlpha()
    {
        byte[] source = MakePng(
            new SKColor(0, 204, 0, 255),
            new SKColor(0, 102, 0, 8),
            new SKColor(0, 0, 0, 0),
            new SKColor(0, 0, 0, 0));
        using SKBitmap sourceBitmap = DecodeStraight(source);

        byte[]? tinted = NativeWeaponEffectRecolor.TintPng(
            source, targetHueDegrees: 300f, targetSaturation: 0.60f);

        Assert.NotNull(tinted);
        using SKBitmap result = DecodeStraight(tinted!);
        Assert.Equal(sourceBitmap.Width, result.Width);
        Assert.Equal(sourceBitmap.Height, result.Height);

        for (int x = 0; x < 2; x++)
        {
            SKColor before = sourceBitmap.GetPixel(x, 0);
            SKColor after = result.GetPixel(x, 0);
            ToHsl(before, out _, out _, out float beforeLightness);
            ToHsl(after, out float hue, out float saturation, out float afterLightness);

            Assert.Equal(before.Alpha, after.Alpha);
            Assert.InRange(CircularHueDistance(hue, 300f), 0f, 1.5f);
            Assert.InRange(MathF.Abs(saturation - 0.60f), 0f, 0.02f);
            Assert.InRange(MathF.Abs(afterLightness - beforeLightness), 0f, 0.01f);
        }
    }

    [Fact]
    public void TintPng_RebuildsTransparentEdgeBleed_WithOnlyTheDocumentedZeroToOneAlphaNudge()
    {
        byte[] source = MakePng(
            new SKColor(0, 220, 0, 255),
            new SKColor(0, 110, 0, 7),
            new SKColor(0, 0, 0, 0),
            new SKColor(0, 0, 0, 0));

        byte[]? tinted = NativeWeaponEffectRecolor.TintPng(
            source, targetHueDegrees: 25f, targetSaturation: 0.85f);

        Assert.NotNull(tinted);
        using SKBitmap result = DecodeStraight(tinted!);

        Assert.Equal((byte)255, result.GetPixel(0, 0).Alpha);
        Assert.Equal((byte)7, result.GetPixel(1, 0).Alpha);
        Assert.Equal((byte)1, result.GetPixel(2, 0).Alpha);
        Assert.Equal((byte)1, result.GetPixel(3, 0).Alpha);

        for (int x = 2; x < 4; x++)
        {
            SKColor edge = result.GetPixel(x, 0);
            Assert.True(edge.Red > 0 || edge.Green > 0 || edge.Blue > 0);
            ToHsl(edge, out float hue, out _, out _);
            Assert.InRange(CircularHueDistance(hue, 25f), 0f, 2f);
        }
    }

    [Fact]
    public void TintPng_ReturnsNullForInvalidInput()
    {
        Assert.Null(NativeWeaponEffectRecolor.TintPng([], 120f, 1f));
        Assert.Null(NativeWeaponEffectRecolor.TintPng([1, 2, 3], 120f, 1f));
        Assert.Null(NativeWeaponEffectRecolor.TintPng(MakePng(new SKColor(1, 2, 3)), float.NaN, 1f));
    }

    private static RawUsageFixture BuildRawUsageM2(
        ushort[] viewBlendModes,
        ushort? ribbonBlendMode = null,
        int ribbonTextureEntryCount = 1,
        bool includeParticle = false)
    {
        int viewCount = Math.Max(1, viewBlendModes.Length);
        int materialCount = viewBlendModes.Length + (ribbonBlendMode.HasValue ? 1 : 0);
        int viewOffset = RawM2Document.VanillaHeaderSize;
        int renderFlagOffset = Align4(viewOffset + viewCount * RawM2View.HeaderStride);
        int batchOffset = Align4(renderFlagOffset + materialCount * 4);
        int textureOffset = Align4(batchOffset + viewBlendModes.Length * 24);
        int textureLookupOffset = Align4(textureOffset + 16);
        int ribbonOffset = ribbonBlendMode.HasValue ? Align4(textureLookupOffset + 2) : 0;
        int ribbonTextureOffset = ribbonBlendMode.HasValue
            ? Align4(ribbonOffset + 220)
            : 0;
        int ribbonMaterialOffset = ribbonBlendMode.HasValue
            ? ribbonTextureOffset + checked(ribbonTextureEntryCount * 2)
            : 0;
        int graphEnd = ribbonBlendMode.HasValue
            ? ribbonMaterialOffset + 2
            : textureLookupOffset + 2;
        int particleOffset = includeParticle ? Align4(graphEnd) : 0;
        int fileLength = includeParticle ? particleOffset + 504 : graphEnd;
        var data = new byte[fileLength];

        data[0] = (byte)'M';
        data[1] = (byte)'D';
        data[2] = (byte)'2';
        data[3] = (byte)'0';
        WriteU32(data, 0x04, 256);
        WriteU32(data, 0x4C, (uint)viewCount);
        WriteU32(data, 0x50, (uint)viewOffset);
        WriteU32(data, 0x5C, 1);
        WriteU32(data, 0x60, (uint)textureOffset);
        WriteU32(data, 0x84, (uint)materialCount);
        WriteU32(data, 0x88, materialCount == 0 ? 0u : (uint)renderFlagOffset);
        WriteU32(data, 0x94, 1);
        WriteU32(data, 0x98, (uint)textureLookupOffset);
        WriteU16(data, textureLookupOffset, 0);
        if (includeParticle)
        {
            WriteU32(data, 0x13C, 1);
            WriteU32(data, 0x140, (uint)particleOffset);
            WriteU16(data, particleOffset + 22, 0);
        }

        for (int view = 0; view < viewCount; view++)
        {
            int header = viewOffset + view * RawM2View.HeaderStride;
            bool hasBatch = view < viewBlendModes.Length;
            WriteU32(data, header + 32, hasBatch ? 1u : 0u);
            WriteU32(data, header + 36, hasBatch ? (uint)(batchOffset + view * 24) : 0u);
            if (!hasBatch) continue;

            int batch = batchOffset + view * 24;
            WriteU16(data, batch + 10, (ushort)view);
            WriteU16(data, batch + 14, 1);
            WriteU16(data, batch + 16, 0);
            WriteU16(data, renderFlagOffset + view * 4 + 2, viewBlendModes[view]);
        }

        if (ribbonBlendMode is { } ribbonBlend)
        {
            int ribbonMaterial = viewBlendModes.Length;
            WriteU32(data, 0x134, 1);
            WriteU32(data, 0x138, (uint)ribbonOffset);
            WriteU32(data, ribbonOffset + 20, (uint)ribbonTextureEntryCount);
            WriteU32(data, ribbonOffset + 24, (uint)ribbonTextureOffset);
            WriteU32(data, ribbonOffset + 28, 1);
            WriteU32(data, ribbonOffset + 32, (uint)ribbonMaterialOffset);
            for (int texture = 0; texture < ribbonTextureEntryCount; texture++)
                WriteU16(data, ribbonTextureOffset + texture * 2, 0);
            WriteU16(data, ribbonMaterialOffset, (ushort)ribbonMaterial);
            WriteU16(data, renderFlagOffset + ribbonMaterial * 4 + 2, ribbonBlend);

            // One linear, neutral-white ribbon RGB key. The texture hue remains authoritative,
            // matching Thunderfury's stock multiplier while exercising the nested track parser.
            int colorTrack = ribbonOffset + 36;
            int colorTime = ribbonOffset + 176;
            int colorKey = ribbonOffset + 180;
            WriteU16(data, colorTrack, 1);
            WriteI16(data, colorTrack + 2, -1);
            WriteU32(data, colorTrack + 12, 1);
            WriteU32(data, colorTrack + 16, (uint)colorTime);
            WriteU32(data, colorTrack + 20, 1);
            WriteU32(data, colorTrack + 24, (uint)colorKey);
            WriteU32(data, colorTime, 0);
            WriteF32(data, colorKey, 1f);
            WriteF32(data, colorKey + 4, 1f);
            WriteF32(data, colorKey + 8, 1f);
        }

        return new RawUsageFixture(data, ribbonOffset);
    }

    private static int Align4(int value) => (value + 3) & ~3;

    private static void WriteU16(byte[] data, int offset, ushort value) =>
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset, 2), value);

    private static void WriteI16(byte[] data, int offset, short value) =>
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(offset, 2), value);

    private static void WriteU32(byte[] data, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset, 4), value);

    private static void WriteF32(byte[] data, int offset, float value) =>
        BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(offset, 4), value);

    private static byte[] MakePng(params SKColor[] pixels)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(
            pixels.Length, 1, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        for (int x = 0; x < pixels.Length; x++) bitmap.SetPixel(x, 0, pixels[x]);
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static SKBitmap DecodeStraight(byte[] png)
    {
        using var stream = new MemoryStream(png, writable: false);
        using SKCodec codec = Assert.IsType<SKCodec>(SKCodec.Create(stream));
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height,
            SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var bitmap = new SKBitmap(info);
        Assert.Equal(SKCodecResult.Success, codec.GetPixels(info, bitmap.GetPixels()));
        return bitmap;
    }

    private static void ToHsl(SKColor color, out float hue, out float saturation, out float lightness)
    {
        float r = color.Red / 255f, g = color.Green / 255f, b = color.Blue / 255f;
        float max = MathF.Max(r, MathF.Max(g, b));
        float min = MathF.Min(r, MathF.Min(g, b));
        float delta = max - min;
        lightness = (max + min) * 0.5f;
        if (delta < 0.0001f)
        {
            hue = 0f;
            saturation = 0f;
            return;
        }

        saturation = lightness > 0.5f
            ? delta / (2f - max - min)
            : delta / (max + min);
        if (max == r) hue = ((g - b) / delta + (g < b ? 6f : 0f)) * 60f;
        else if (max == g) hue = ((b - r) / delta + 2f) * 60f;
        else hue = ((r - g) / delta + 4f) * 60f;
    }

    private static float CircularHueDistance(float a, float b)
    {
        float distance = MathF.Abs(a - b) % 360f;
        return MathF.Min(distance, 360f - distance);
    }

    private sealed record RawUsageFixture(byte[] Bytes, int RibbonOffset);
}
