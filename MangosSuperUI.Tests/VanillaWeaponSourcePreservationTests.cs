using System.Reflection;
using MangosSuperUI.Controllers;
using MangosSuperUI.Services;
using MangosSuperUI.Services.WeaponForge;
using Xunit;

namespace MangosSuperUI.Tests;

public sealed class VanillaWeaponSourcePreservationTests
{
    [Fact]
    public void AnimatedItemRig_BypassesPassAwareRigidPreview()
    {
        M2Model model = CameraFacingItemModel();

        bool canUsePassAware = InvokePreviewPredicate("CanUsePassAwarePreview", model);

        Assert.False(canUsePassAware);
    }

    [Fact]
    public void OrdinaryRigidWeapon_CanUsePassAwarePreview()
    {
        var model = new M2Model
        {
            Vertices = TriangleVertices(boneWeight: 0),
            Indices = [0, 1, 2],
            Submeshes =
            [
                new M2Submesh { IndexStart = 0, IndexCount = 3 },
            ],
        };

        bool canUsePassAware = InvokePreviewPredicate("CanUsePassAwarePreview", model);

        Assert.True(canUsePassAware);
    }

    [Theory]
    [InlineData(2u, true)]   // OBJECT_SKIN: filled from ItemDisplayInfo.TextureName1
    [InlineData(0u, false)]  // hardcoded filename in the M2
    [InlineData(1u, false)]
    [InlineData(3u, false)]  // weapon reflection/environment map
    [InlineData(7u, false)]  // character facial hair
    [InlineData(8u, false)]  // character skin-extra
    public void OnlyObjectSkinTexture_UsesDbcDisplayTexture(uint textureType, bool expected)
    {
        var texture = new M2TextureRef { Type = textureType };

        bool usesDisplayTexture = InvokePreviewPredicate("UsesDisplayTexture", texture);

        Assert.Equal(expected, usesDisplayTexture);
    }

    [Fact]
    public void PreservedPreview_DoesNotSubstituteDisplaySkinForMissingRuntimeSlot()
    {
        bool maySubstitute = InvokeStatic<bool>(typeof(GlbWriter), "MaySubstituteMissingTexture",
            false, true, 1);

        Assert.False(maySubstitute);
        Assert.True(InvokeStatic<bool>(typeof(GlbWriter), "MaySubstituteMissingTexture",
            false, false, 1));
    }

    [Fact]
    public void WeaponBladeSlot_UsesStockRuntimePreviewTexture()
    {
        string? path = InvokeStatic<string?>(typeof(WeaponPreviewService), "StockPreviewTexturePath",
            new M2TextureRef { Type = 3 });

        Assert.Equal(@"ITEM\ObjectComponents\WEAPON\ArmorReflect4.BLP", path);
    }

    [Fact]
    public void DisplaySkinMustBeReachedByABatchToBeRecolorable()
    {
        var model = new M2Model
        {
            Textures =
            [
                new M2TextureRef { Type = 2 },
                new M2TextureRef { Type = 0, Filename = "effect.blp" },
            ],
            TextureLookup = [1],
            Batches = [new M2Batch { TextureIndex = 0, TextureCount = 1 }],
        };

        Assert.False(InvokeStatic<bool>(typeof(WeaponPreviewService), "SamplesDisplayTexture", model));

        model.TextureLookup[0] = 0;
        Assert.True(InvokeStatic<bool>(typeof(WeaponPreviewService), "SamplesDisplayTexture", model));
    }

    [Fact]
    public void SampledSlots_IncludeEveryTextureUnit()
    {
        var model = new M2Model
        {
            Textures =
            [
                new M2TextureRef { Type = 0 },
                new M2TextureRef { Type = 2 },
                new M2TextureRef { Type = 3 },
            ],
            TextureLookup = [1, 2],
            Batches = [new M2Batch { TextureIndex = 0, TextureCount = 2 }],
        };

        var sampled = InvokeStatic<HashSet<int>>(typeof(WeaponPreviewService),
            "SampledTextureSlots", model);

        Assert.Equal([1, 2], sampled.OrderBy(i => i));
    }

    [Theory]
    [InlineData(0u, -1, 0u)]
    [InlineData(17u, -1, 17u)]
    [InlineData(17u, 0, 0u)]
    [InlineData(0u, 7, 7u)]
    public void VanillaAuto_PreservesExactSourceItemVisual(
        uint sourceItemVisual, int requestedItemVisual, uint expected)
    {
        uint chosen = InvokeStatic<uint>(typeof(WeaponForgeController),
            "ChoosePreservedItemVisual", sourceItemVisual, requestedItemVisual);

        Assert.Equal(expected, chosen);
    }

    [Fact]
    public void SavedVanillaRecolor_ReopensWithSourceGraph()
    {
        Assert.True(InvokeStatic<bool>(typeof(WeaponForgeController),
            "IsSourcePreservingBuild", "vanilla_recolor"));
        Assert.False(InvokeStatic<bool>(typeof(WeaponForgeController),
            "IsSourcePreservingBuild", "glb_import"));
    }

    [Fact]
    public void SourceGraphPlacement_RejectsOtherwiseIgnoredShapeControls()
    {
        var shape = new GlbShapeControls { WidthPercent = 125 };

        Assert.False(InvokeStatic<bool>(typeof(WeaponForgeController),
            "IsSourceGraphPlacementIdentity", shape, false));
    }

    [Theory]
    [InlineData("base.MPQ", true)]
    [InlineData("model.MPQ", true)]
    [InlineData("patch.MPQ", true)]
    [InlineData("patch-2.MPQ", true)]
    [InlineData("patch-3.MPQ", false)]
    [InlineData("patch-5.MPQ", false)]
    [InlineData("patch-Z.MPQ", false)]
    [InlineData("patch-M.MPQ", false)]
    [InlineData("patch-custom-19019.MPQ", false)]
    [InlineData("renamed-stock-copy.MPQ", false)]
    public void VanillaSourceMount_StopsAtStockPatchCeiling(string archive, bool expected)
    {
        bool actual = InvokeStatic<bool>(typeof(VanillaMpqSource), "IsStockArchive", archive);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void StockPreviewTexturePath_CanonicalizesHardcodedM2Member()
    {
        var texture = new M2TextureRef
        {
            Type = 0,
            Filename = "  item/objectcomponents/weapon/Glow.BLP  ",
        };

        Assert.Equal(@"item\objectcomponents\weapon\Glow.BLP",
            WeaponPreviewService.StockPreviewTexturePath(texture));
    }

    [Fact]
    public void VanillaRecolorBuild_UsesExactSourceM2InsteadOfRigidMesh()
    {
        byte[] sourceM2 = [1, 2, 3, 4];
        byte[] displayBlp = [5, 6, 7];
        var overrides = new Dictionary<string, string> { ["sheath"] = "1" };
        MethodInfo? method = typeof(WeaponForgeController).GetMethod(
            "CreatePreservedVanillaBuildRequest",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.NotNull(method);
        var request = Assert.IsType<CustomWeaponBuildRequest>(method.Invoke(null,
        [
            "Recolored Thunderfury", "sword2h", overrides, sourceM2, displayBlp,
            "INV_Sword_39", 7u, 9u, 0u, false, "{}",
        ]));

        Assert.Null(request.Mesh);
        Assert.Same(sourceM2, request.PrecompiledM2);
        Assert.Same(displayBlp, request.PrecompiledBlp);
        Assert.Same(sourceM2, request.SourceBlob);
        Assert.Same(overrides, request.ItemOverrides);
        Assert.Equal("vanilla_recolor", request.SourceKind);
        Assert.Equal("vanilla-source-v1", request.WriterVersion);
        Assert.Equal(9u, request.DisplayGroupSoundIndex);
        Assert.Equal(0u, request.DisplaySpellVisualId);
        Assert.False(request.DisplayMirrorModelName2);
    }

    [Fact]
    public void VanillaEffectRecolorBuild_PreservesOriginalProvenanceAndCarriesTintedAssets()
    {
        byte[] sourceM2 = [1, 2, 3, 4];
        byte[] tintedM2 = [1, 9, 3, 4];
        byte[] displayBlp = [5, 6, 7];
        byte[] effectBlp = [8, 9, 10];
        var effects = new List<PrecompiledWeaponEffectTexture>
        {
            new([1, 3], @"Item\ObjectComponents\Weapon\ArmorReflect3.blp", effectBlp),
        };

        CustomWeaponBuildRequest request =
            WeaponForgeController.CreatePreservedVanillaEffectBuildRequest(
                "Purple Warglaive", "sword1h", null, sourceM2, tintedM2, displayBlp,
                "INV_Weapon_Glaive_01", 25u, 9u, 0u, false, "{}", effects);

        Assert.Null(request.Mesh);
        Assert.Same(tintedM2, request.PrecompiledM2);
        Assert.Same(sourceM2, request.SourceBlob);
        Assert.Same(displayBlp, request.PrecompiledBlp);
        Assert.Same(effects, request.PrecompiledEffectTextures);
        Assert.Same(effectBlp, request.PrecompiledEffectTextures![0].Blp);
        Assert.Equal([1, 3], request.PrecompiledEffectTextures[0].TextureSlots);
        Assert.Equal("vanilla_recolor", request.SourceKind);
        Assert.Equal("vanilla-source-effects-v2", request.WriterVersion);
    }

    [Fact]
    public void SourceDisplayFields_OverrideDonor_WhileGenericBuildsRetainFallbacks()
    {
        var preserved = new CustomWeaponBuildRequest
        {
            SourceKind = "vanilla_recolor",
            DisplayGroupSoundIndex = 9,
            DisplaySpellVisualId = 0,
            DisplayMirrorModelName2 = false,
        };
        var generic = new CustomWeaponBuildRequest { SourceKind = "glb_import" };

        var sourceFields = InvokeStatic<(uint GroupSoundIndex, uint SpellVisualId, bool MirrorModelName2)>(
            typeof(CustomWeaponBuildService), "ResolveDisplayFields", preserved, 3u, 44u, true);
        var donorFields = InvokeStatic<(uint GroupSoundIndex, uint SpellVisualId, bool MirrorModelName2)>(
            typeof(CustomWeaponBuildService), "ResolveDisplayFields", generic, 3u, 44u, true);

        Assert.Equal((9u, 0u, false), sourceFields);
        Assert.Equal((3u, 44u, true), donorFields);
    }

    private static M2Model CameraFacingItemModel()
    {
        return new M2Model
        {
            Bones =
            [
                new M2Bone
                {
                    ParentBone = -1,
                    // Vanilla M2 billboard mode. Visible vertices influenced by this bone require
                    // the item skin; flattening them into RigidWeaponMesh freezes the authored rig.
                    Flags = 0x08,
                },
            ],
            Vertices = TriangleVertices(boneWeight: 255),
            Indices = [0, 1, 2],
            Submeshes =
            [
                new M2Submesh { IndexStart = 0, IndexCount = 3 },
            ],
        };
    }

    private static List<M2Vertex> TriangleVertices(byte boneWeight)
    {
        M2Vertex Vertex(float x, float y) => new()
        {
            PosX = x,
            PosY = y,
            NormY = 1,
            BoneWeight0 = boneWeight,
            BoneIndex0 = 0,
        };

        return [Vertex(0, 0), Vertex(1, 0), Vertex(0, 1)];
    }

    private static bool InvokePreviewPredicate(string name, object argument)
    {
        MethodInfo? method = typeof(WeaponPreviewService).GetMethod(
            name,
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.NotNull(method);
        object? result = method.Invoke(null, [argument]);
        return Assert.IsType<bool>(result);
    }

    private static T InvokeStatic<T>(Type owner, string name, params object?[] arguments)
    {
        MethodInfo? method = owner.GetMethod(name,
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.NotNull(method);
        object? result = method.Invoke(null, arguments);
        if (result is null) return default!;
        return Assert.IsAssignableFrom<T>(result);
    }
}
