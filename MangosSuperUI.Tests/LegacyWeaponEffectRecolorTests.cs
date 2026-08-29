using System.Numerics;
using MangosSuperUI.Services.WeaponForge;
using SkiaSharp;
using Xunit;

namespace MangosSuperUI.Tests;

public sealed class LegacyWeaponEffectRecolorTests
{
    [Fact]
    public void Selector_IncludesWarglaiveEnvironmentBlend4_WithoutChangingItsPass()
    {
        var envBinding = new WeaponTextureBinding
        {
            TextureSlot = 1,
            TextureCoordinate = ushort.MaxValue,
            TextureTransform = ushort.MaxValue,
        };
        var envPass = Pass(blend: 4, textureSlot: 1, envBinding);
        RigidWeaponMesh mesh = Mesh(envPass);

        IReadOnlyList<int> selected =
            LegacyWeaponEffectRecolor.SelectEligibleTextureSlots(mesh, effectTextureCount: 1);

        Assert.Equal([1], selected);
        Assert.Same(envPass, mesh.Passes![0]);
        Assert.Equal(ushort.MaxValue, mesh.Passes[0].TextureBindings![0].TextureCoordinate);
        Assert.Equal((ushort)4, mesh.Passes[0].BlendMode);
    }

    [Fact]
    public void Selector_InspectsSecondaryBindings_AndRejectsAnyOpaqueUseOfTheSameSlot()
    {
        var composite = Pass(3, 0,
            new WeaponTextureBinding { TextureSlot = 0 },
            new WeaponTextureBinding { TextureSlot = 1, TextureCoordinate = ushort.MaxValue });
        RigidWeaponMesh compositeOnly = Mesh(composite);

        Assert.Equal([1],
            LegacyWeaponEffectRecolor.SelectEligibleTextureSlots(compositeOnly, 1));

        var opaqueReuse = Pass(0, 1,
            new WeaponTextureBinding { TextureSlot = 1 });
        RigidWeaponMesh mixedUse = Mesh(composite, opaqueReuse);

        Assert.Empty(LegacyWeaponEffectRecolor.SelectEligibleTextureSlots(mixedUse, 1));
    }

    [Fact]
    public void Apply_RecolorsOnlyEligibleSheet_PreservesAlphaAndSuppressesOnlyItsSourceBlp()
    {
        var envPass = Pass(4, 1,
            new WeaponTextureBinding
            {
                TextureSlot = 1,
                TextureCoordinate = ushort.MaxValue,
                TextureTransform = ushort.MaxValue,
            });
        var opaquePass = Pass(0, 2,
            new WeaponTextureBinding { TextureSlot = 2 });
        RigidWeaponMesh mesh = Mesh(envPass, opaquePass);

        byte[] sourceEnvPng = Png(new SKColor(0, 255, 0, 91));
        byte[] sourceOpaquePng = Png(new SKColor(220, 40, 20, 255));
        byte[] sourceEnvSnapshot = sourceEnvPng.ToArray();
        byte[] sourceEnvBlp = [1, 2, 3];
        byte[] sourceOpaqueBlp = [4, 5, 6];

        LegacyWeaponEffectTint result = LegacyWeaponEffectRecolor.Apply(
            mesh,
            [sourceEnvPng, sourceOpaquePng],
            [sourceEnvBlp, sourceOpaqueBlp],
            targetHueDegrees: 270f,
            targetSaturation: 1f);

        Assert.Equal([1], result.TextureSlots);
        Assert.NotNull(result.Pngs);
        Assert.NotNull(result.Blps);
        Assert.Equal(sourceEnvSnapshot, sourceEnvPng); // source bytes were not mutated
        Assert.False(sourceEnvPng.SequenceEqual(result.Pngs![0]));
        Assert.Same(sourceOpaquePng, result.Pngs[1]);
        Assert.Empty(result.Blps![0]); // builder must re-encode this recolored PNG
        Assert.Same(sourceOpaqueBlp, result.Blps[1]);

        using SKBitmap? tinted = SKBitmap.Decode(result.Pngs[0]);
        Assert.NotNull(tinted);
        Assert.Equal(1, tinted!.Width);
        Assert.Equal(1, tinted.Height);
        SKColor pixel = tinted.GetPixel(0, 0);
        Assert.Equal((byte)91, pixel.Alpha);
        Assert.True(pixel.Blue > pixel.Red && pixel.Red > pixel.Green,
            $"Expected a violet hue, got #{pixel.Red:X2}{pixel.Green:X2}{pixel.Blue:X2}.");

        // Pixel replacement did not touch the render graph that produces the movement.
        Assert.Same(envPass, mesh.Passes![0]);
        Assert.Equal(ushort.MaxValue, mesh.Passes[0].TextureBindings![0].TextureCoordinate);
    }

    private static WeaponPass Pass(ushort blend, int textureSlot,
        params WeaponTextureBinding[] bindings) => new()
    {
        SubmeshSlot = 0,
        RenderFlags = blend >= 3 ? (ushort)0x10 : (ushort)0,
        BlendMode = blend,
        Layer = blend >= 3 ? 1 : 0,
        TextureSlot = textureSlot,
        TextureBindings = bindings,
    };

    private static RigidWeaponMesh Mesh(params WeaponPass[] passes) => new()
    {
        Positions = [Vector3.Zero, Vector3.UnitX, Vector3.UnitY],
        Normals = [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ],
        Uv0 = [Vector2.Zero, Vector2.UnitX, Vector2.UnitY],
        Indices = [0, 1, 2],
        Material = new WeaponMaterial(),
        SubmeshRanges =
        [
            new WeaponSubmeshRange
            {
                IndexStart = 0,
                IndexCount = 3,
                VertexStart = 0,
                VertexCount = 3,
            },
        ],
        Passes = passes,
    };

    private static byte[] Png(SKColor color)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(
            1, 1, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        bitmap.SetPixel(0, 0, color);
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
