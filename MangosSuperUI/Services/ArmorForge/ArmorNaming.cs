using MangosSuperUI.Services.WeaponForge;

namespace MangosSuperUI.Services.ArmorForge;

/// <summary>
/// Naming + path convention for forged armor, the armor-side sibling of <see cref="WeaponNaming"/>.
/// Every generated model/texture gets a unique <c>SUI_A_*</c> identity so nothing ever shadows a
/// stock embedded path.
///
/// Armor renders in two fundamentally different ways (see ARMOR_FORGE.md §1):
///
///   • MODELLED pieces (helm, shoulder) attach a real object-component M2 to a character bone, the
///     same way weapons/shields do. They use <see cref="ModelStem"/>/<see cref="ModelMpqPath"/> and
///     live under <see cref="HeadDir"/> / <see cref="ShoulderDir"/>. Their DBC row fills
///     ModelName1/TextureName1 (+ ModelName2/TextureName2 for the mirrored shoulder), the geoset
///     groups, and the helmet hair/facial-hide visibility fields.
///
///   • PAINTED pieces (chest, legs, gloves, boots, bracers, belt, shirt, tabard) have NO model of
///     their own — they paint partial BLPs into the shared character body atlas. Those BLPs live
///     under <c>Item\TextureComponents\{subdir}\</c> and are referenced by the eight m_texture[]
///     stringrefs (ItemDisplayInfo fields 14..21). This mirrors the Retexture Engine's body-atlas
///     path exactly (ItemRetextureService body-atlas commit + BodyAtlasTextureService), so a forged
///     painted piece and a retextured one resolve identically in the client and in the preview.
/// </summary>
public static class ArmorNaming
{
    /// <summary>Head object-components directory (helm models), backslash-separated as MPQ members require.</summary>
    public const string HeadDir = @"Item\ObjectComponents\Head";

    /// <summary>Shoulder object-components directory (shoulder-pad models).</summary>
    public const string ShoulderDir = @"Item\ObjectComponents\Shoulder";

    /// <summary>Cape object-components directory (cloak texture lives here on some clients; vanilla
    /// paints the cape geoset from TextureName1). Kept for completeness.</summary>
    public const string CapeDir = @"Item\ObjectComponents\Cape";

    /// <summary>The eight body-atlas TextureComponents subdirs, slot-indexed 0..7. Identical to
    /// <c>BodyAtlasTextureService.SlotToSubdir</c> and <c>ItemRetextureService.AtlasSlotSubdir</c> —
    /// the single source of truth is kept in step across all three by construction.</summary>
    public static readonly IReadOnlyList<string> ComponentSubdirs = new[]
    {
        "ArmUpperTexture",   // 0 shoulders/biceps
        "ArmLowerTexture",   // 1 forearms
        "HandTexture",       // 2 hand/wrist
        "TorsoUpperTexture", // 3 chest
        "TorsoLowerTexture", // 4 belly/waist
        "LegUpperTexture",   // 5 thigh / robe upper
        "LegLowerTexture",   // 6 shin / robe lower
        "FootTexture",       // 7 foot
    };

    /// <summary>Bare model stem, e.g. "SUI_A_0001". modelIndex is 1-based.</summary>
    public static string ModelStem(int modelIndex) => $"SUI_A_{modelIndex:D4}";

    /// <summary>Texture stem for a variant, e.g. "SUI_A_0001_V01". variant is 1-based.</summary>
    public static string TextureStem(int modelIndex, int variant = 1) => $"{ModelStem(modelIndex)}_V{variant:D2}";

    /// <summary>DBC ModelName value (logical, carries .mdx), e.g. "SUI_A_0001.mdx".</summary>
    public static string DbcModelName(int modelIndex) => $"{ModelStem(modelIndex)}.mdx";

    /// <summary>DBC TextureName value (bare stem, no dir/ext), e.g. "SUI_A_0001_V01".</summary>
    public static string DbcTextureName(int modelIndex, int variant = 1) => TextureStem(modelIndex, variant);

    /// <summary>Physical MPQ model member path, e.g. Item\ObjectComponents\Head\SUI_A_0001.m2.</summary>
    public static string ModelMpqPath(int modelIndex, string componentDir) =>
        $@"{componentDir}\{ModelStem(modelIndex)}.m2";

    /// <summary>The vanilla race/gender suffixes the client appends to a helm's logical model name
    /// (measured on the 1.12 client: Helm_X.mdx → Helm_X_HuM.m2, _HuF, _OrM …). 8 races × 2 genders.
    /// A forged helm must ship ALL sixteen physical files, each re-emitted from the TBC variant of the
    /// same race/gender, or that race/gender sees nothing on its head.</summary>
    public static readonly IReadOnlyList<string> HelmVariantSuffixes = new[]
    {
        "HuM", "HuF", "DwM", "DwF", "NiM", "NiF", "GnM", "GnF",
        "OrM", "OrF", "ScM", "ScF", "TaM", "TaF", "TrM", "TrF",
    };

    /// <summary>Physical MPQ member for one helm race/gender variant, e.g.
    /// Item\ObjectComponents\Head\SUI_A_0001_HuM.m2.</summary>
    public static string HelmVariantMpqPath(int modelIndex, string suffix) =>
        $@"{HeadDir}\{ModelStem(modelIndex)}_{suffix}.m2";

    /// <summary>Shoulders are an L/R pair of distinct files. DBC ModelName1/2 carry these logical names.</summary>
    public static string ShoulderLeftDbcName(int modelIndex) => $"{ModelStem(modelIndex)}_L.mdx";
    public static string ShoulderRightDbcName(int modelIndex) => $"{ModelStem(modelIndex)}_R.mdx";
    public static string ShoulderLeftMpqPath(int modelIndex) => $@"{ShoulderDir}\{ModelStem(modelIndex)}_L.m2";
    public static string ShoulderRightMpqPath(int modelIndex) => $@"{ShoulderDir}\{ModelStem(modelIndex)}_R.m2";

    /// <summary>Physical MPQ BLP member path for a MODELLED piece, e.g.
    /// Item\ObjectComponents\Head\SUI_A_0001_V01.blp.</summary>
    public static string TextureMpqPath(int modelIndex, string componentDir, int variant = 1) =>
        $@"{componentDir}\{TextureStem(modelIndex, variant)}.blp";

    /// <summary>Bare component-texture stem for a PAINTED slot, e.g. "SUI_A_0001_s3". This is what
    /// goes into the ItemDisplayInfo m_texture[slot] field — bare, no dir, no gender suffix; the
    /// client prepends the subdir and appends _{M|F|U}.blp.</summary>
    public static string ComponentStem(int displayIndex, int slot) => $"{ModelStem(displayIndex)}_s{slot}";

    /// <summary>Physical MPQ member path for a PAINTED slot's BLP. The client resolves the bare DBC
    /// stem by appending a gender suffix, so we pack under the "_U" (unisex) name — the same choice
    /// the Retexture Engine makes (ItemRetextureService.COMPONENT_SUFFIXES).</summary>
    public static string ComponentMpqPath(int displayIndex, int slot) =>
        $@"Item\TextureComponents\{ComponentSubdirs[slot]}\{ComponentStem(displayIndex, slot)}_U.blp";

    /// <summary>The canonical DBC file member path inside the patch MPQ (shared with the Weapon Forge).</summary>
    public const string ItemDisplayInfoMember = WeaponNaming.ItemDisplayInfoMember;

    /// <summary>The canonical ItemSet.dbc member path inside the patch MPQ (tier set bonuses).</summary>
    public const string ItemSetMember = @"DBFilesClient\ItemSet.dbc";
}
