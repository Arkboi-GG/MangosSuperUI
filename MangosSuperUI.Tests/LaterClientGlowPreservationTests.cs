using System.Numerics;
using MangosSuperUI.Services;
using MangosSuperUI.Services.WeaponForge;
using Xunit;

namespace MangosSuperUI.Tests;

public sealed class LaterClientGlowPreservationTests
{
    [Theory]
    [InlineData("tbc_import")]
    [InlineData("wotlk_import")]
    public void LaterClientFidelityImport_DoesNotInventGlowPulse(string sourceKind)
    {
        Assert.False(CustomWeaponBuildService.ShouldInventGlowPulse(sourceKind));
    }

    [Fact]
    public void Blend4Uv0Uv1Pass_RetainsSteadyGlowBeneathWave()
    {
        var uv0 = new WeaponTextureBinding { TextureSlot = 1, TextureCoordinate = 0 };
        var uv1 = new WeaponTextureBinding { TextureSlot = 1, TextureCoordinate = 1 };

        Assert.True(WeaponPreviewService.UsesSteadyModulatedGlow(4, uv0, uv1));
        Assert.False(WeaponPreviewService.UsesSteadyModulatedGlow(3, uv0, uv1));
        Assert.False(WeaponPreviewService.UsesSteadyModulatedGlow(4, uv0, uv0));
    }

    [Fact]
    public void ColorlessAdditiveSourcePass_RemainsColorless()
    {
        var model = new M2Model
        {
            Vertices =
            [
                Vertex(0f, 0f),
                Vertex(1f, 0f),
                Vertex(0f, 1f),
            ],
            Indices = [0, 1, 2],
            Submeshes =
            [
                new M2Submesh { VertexCount = 3, IndexCount = 3 },
            ],
            Textures = [new M2TextureRef { Type = 2 }],
            TextureLookup = [0],
            TextureCoordinateLookup = [0],
            RenderFlags = [new M2RenderFlag { Flags = 0x10, BlendingMode = 4 }],
            Batches =
            [
                new M2Batch
                {
                    SubmeshIndex = 0,
                    ColorIndex = -1,
                    MaterialIndex = 0,
                    TextureCount = 1,
                    TextureIndex = 0,
                    TextureCoordinateIndex = 0,
                    TextureWeightIndex = ushort.MaxValue,
                    TextureTransformIndex = ushort.MaxValue,
                },
            ],
        };

        LegacyExtractResult? result = LegacyWeaponMeshExtractor.Extract(
            model, new ForgeDiagnostics("test"), bakeEmitters: false);

        WeaponPass pass = Assert.Single(result!.Mesh.Passes!);
        Assert.Equal((short)-1, pass.ColorIndex);
        Assert.Null(pass.RestColor);
    }

    private static M2Vertex Vertex(float x, float y) => new()
    {
        PosX = x,
        PosY = y,
        NormZ = 1f,
        TexU = x,
        TexV = y,
        TexU2 = x,
        TexV2 = y,
    };

    [Theory]
    [InlineData("glb_import")]
    [InlineData("parametric")]
    [InlineData("sketch3d")]
    public void AuthoredForgeLane_RetainsExistingGlowPulsePolicy(string sourceKind)
    {
        Assert.True(CustomWeaponBuildService.ShouldInventGlowPulse(sourceKind));
    }
}