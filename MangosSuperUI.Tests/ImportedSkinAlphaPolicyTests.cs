using MangosSuperUI.Services.ArmorForge;
using MangosSuperUI.Services.WeaponForge;
using Xunit;

namespace MangosSuperUI.Tests;

public sealed class ImportedSkinAlphaPolicyTests
{
    private static WeaponPass Pass(int submesh, ushort blend, int layer, params int[] slots) => new()
    {
        SubmeshSlot = submesh,
        RenderFlags = blend == 2 ? (ushort)0x10 : (ushort)0,
        BlendMode = blend,
        Layer = layer,
        TextureSlot = slots[0],
        TextureBindings = slots.Select(s => new WeaponTextureBinding { TextureSlot = s }).ToArray(),
    };

    /// <summary>Onslaught Greathelm, as LegacyWeaponMeshExtractor hands it over: the layer-1 mask pass
    /// first (skin only, blend 2), the layer-0 opaque diffuse+reflect base second.</summary>
    private static WeaponPass[] OnslaughtHelm() => new[]
    {
        Pass(0, blend: 2, layer: 1, 0),
        Pass(0, blend: 0, layer: 0, 0, 1),
    };

    [Fact]
    public void OnslaughtMaskOverlay_IsDropped_AndSkinAlphaBecomesUnneeded()
    {
        var result = ImportedSkinAlphaPolicy.Apply(OnslaughtHelm());

        Assert.Equal(1, result.StrippedMaskPasses);
        var survivor = Assert.Single(result.Passes);
        Assert.Equal(0, survivor.Layer);
        Assert.Equal(0, (int)survivor.BlendMode);
        Assert.Equal(2, survivor.TextureBindings!.Count);   // diffuse + env map both kept
        Assert.False(result.SkinAlphaRequired);
    }

    [Fact]
    public void AlphaKeyCutout_KeepsSkinAlpha()
    {
        var result = ImportedSkinAlphaPolicy.Apply(new[] { Pass(0, blend: 1, layer: 0, 0) });

        Assert.Equal(0, result.StrippedMaskPasses);
        Assert.True(result.SkinAlphaRequired);
    }

    [Fact]
    public void TranslucentPieceWithoutOpaqueBase_IsNotAMask()
    {
        // A veil: its only pass is alpha-blended. Nothing underneath it to reveal, so it stays.
        var result = ImportedSkinAlphaPolicy.Apply(new[] { Pass(1, blend: 2, layer: 1, 0) });

        Assert.Equal(0, result.StrippedMaskPasses);
        Assert.True(result.SkinAlphaRequired);
    }

    [Fact]
    public void OverlayOnADifferentSubmesh_IsNotAMask()
    {
        var result = ImportedSkinAlphaPolicy.Apply(new[]
        {
            Pass(0, blend: 0, layer: 0, 0),
            Pass(1, blend: 2, layer: 1, 0),
        });

        Assert.Equal(0, result.StrippedMaskPasses);
        Assert.True(result.SkinAlphaRequired);
    }

    [Fact]
    public void OverlaySamplingAnEffectTexture_IsNotAMask()
    {
        var result = ImportedSkinAlphaPolicy.Apply(new[]
        {
            Pass(0, blend: 0, layer: 0, 0),
            Pass(0, blend: 2, layer: 1, 1),
        });

        Assert.Equal(0, result.StrippedMaskPasses);
        Assert.False(result.SkinAlphaRequired);   // slot 1 is not the skin
    }

    [Fact]
    public void BlpHeader_AlphaDepthByte_DecidesAlphaChannel()
    {
        var opaque = new byte[160];
        var dxt5 = new byte[160];
        dxt5[9] = 8;
        Assert.False(ImportedSkinAlphaPolicy.BlpHasAlphaChannel(opaque));
        Assert.True(ImportedSkinAlphaPolicy.BlpHasAlphaChannel(dxt5));
        Assert.False(ImportedSkinAlphaPolicy.BlpHasAlphaChannel(null));
    }
}
