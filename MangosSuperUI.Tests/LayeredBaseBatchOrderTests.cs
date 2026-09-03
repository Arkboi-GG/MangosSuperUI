using MangosSuperUI.Services;
using Xunit;

namespace MangosSuperUI.Tests;

/// <summary>
/// TBC Tier 5/6 plate helms and spaulders (Onslaught, Lightbringer, ...) list their layer-1
/// alpha-blended reflection-mask pass BEFORE the layer-0 opaque base. GlbWriter must pick the base
/// by material layer, not by file order, or it exports only the mask pass and the helm renders
/// see-through in the previewer.
/// </summary>
public sealed class LayeredBaseBatchOrderTests
{
    /// <summary>The Onslaught Greathelm (Helm_Plate_RaidWarrior_F_01_HuM) batch table, verbatim:
    /// one submesh, mask pass first (material 1 = flags 0x10, blend 2, layer 1, one unit), opaque
    /// diffuse+reflect base second (material 0 = blend 0, layer 0, two units).</summary>
    private static M2Model OnslaughtHelm() => new()
    {
        Textures =
        [
            new M2TextureRef { Type = 2 },
            new M2TextureRef { Type = 0, Filename = @"ITEM\OBJECTCOMPONENTS\WEAPON\ARMORREFLECT4.BLP" },
        ],
        TextureLookup = [0, 1],
        TextureCoordinateLookup = [0, ushort.MaxValue],
        RenderFlags =
        [
            new M2RenderFlag { Flags = 0x0, BlendingMode = 0 },
            new M2RenderFlag { Flags = 0x10, BlendingMode = 2 },
        ],
        Submeshes = [new M2Submesh()],
        Batches =
        [
            new M2Batch { SubmeshIndex = 0, MaterialIndex = 1, MaterialLayer = 1, TextureCount = 1, TextureIndex = 0, TextureCoordinateIndex = 0, ColorIndex = -1 },
            new M2Batch { SubmeshIndex = 0, MaterialIndex = 0, MaterialLayer = 0, TextureCount = 2, TextureIndex = 0, TextureCoordinateIndex = 0, ColorIndex = -1 },
        ],
    };

    [Fact]
    public void DrawOrder_PutsLayerZeroBaseFirst_WhenFileListsMaskPassFirst()
    {
        var ordered = GlbWriter.BatchesInDrawOrder(OnslaughtHelm());

        Assert.Equal(2, ordered.Count);
        Assert.Equal(0, (int)ordered[0].MaterialLayer);
        Assert.Equal(0, (int)ordered[0].MaterialIndex);   // the opaque diffuse+reflect base
        Assert.Equal(1, (int)ordered[1].MaterialLayer);
        Assert.Equal(1, (int)ordered[1].MaterialIndex);   // the blend-2 shininess mask overlay
    }

    [Fact]
    public void DrawOrder_IsStableWithinALayer_AndLeavesVanillaOrderAlone()
    {
        // Vanilla-shaped: base at layer 0 first, two additive overlays at layer 1 in source order.
        var model = new M2Model
        {
            Submeshes = [new M2Submesh()],
            RenderFlags = [new M2RenderFlag { BlendingMode = 0 }, new M2RenderFlag { BlendingMode = 3 }],
            Batches =
            [
                new M2Batch { SubmeshIndex = 0, MaterialIndex = 0, MaterialLayer = 0, TextureIndex = 0 },
                new M2Batch { SubmeshIndex = 0, MaterialIndex = 1, MaterialLayer = 1, TextureIndex = 1 },
                new M2Batch { SubmeshIndex = 0, MaterialIndex = 1, MaterialLayer = 1, TextureIndex = 2 },
            ],
        };

        var ordered = GlbWriter.BatchesInDrawOrder(model);

        Assert.Equal(new[] { 0, 1, 2 }, ordered.Select(b => (int)b.TextureIndex).ToArray());
    }

    [Fact]
    public void SampledTextureIndices_ComeFromTheLayerZeroBase()
    {
        // Both passes sample slot 0 here, so this pins the chain rather than the value: the base
        // batch's TextureIndex → TextureLookup resolves to the DBC skin slot.
        Assert.Equal(new[] { 0 }, GlbWriter.SampledTextureIndices(OnslaughtHelm()).ToArray());
    }
}
