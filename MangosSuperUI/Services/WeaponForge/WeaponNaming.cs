namespace MangosSuperUI.Services.WeaponForge;

/// <summary>
/// The deliberately asymmetric naming convention for generated weapon content (WEAPON_GEN.md §2.2).
/// Every generated model/texture gets a unique <c>SUI_W_*</c> identity so nothing ever shadows a
/// stock embedded path. The DBC logical model name carries the <c>.mdx</c> extension while the
/// physical MPQ member is <c>.m2</c>; the texture DBC name is a bare stem while the MPQ BLP member
/// carries the directory and <c>.blp</c>. Centralising this here keeps the DBC writer, the MPQ
/// builder, and the validators from drifting apart.
///
///   modelIndex 1 →
///     DBC ModelName1     : SUI_W_0001.mdx
///     MPQ model member   : Item\ObjectComponents\Weapon\SUI_W_0001.m2
///     DBC TextureName1   : SUI_W_0001_V01
///     MPQ BLP member     : Item\ObjectComponents\Weapon\SUI_W_0001_V01.blp
///
/// The directory is the family's component folder: weapons and every ranged family live in
/// <see cref="WeaponDir"/>, shields in <see cref="ShieldDir"/> (the client picks the folder by item
/// type; the DBC names stay bare). Callers pass <c>WeaponTypeProfile.ComponentDir</c>; the default
/// keeps the original weapon folder.
/// </summary>
public static class WeaponNaming
{
    /// <summary>The weapon object-components directory, backslash-separated as MPQ members require.</summary>
    public const string WeaponDir = @"Item\ObjectComponents\Weapon";

    /// <summary>The shield object-components directory.</summary>
    public const string ShieldDir = @"Item\ObjectComponents\Shield";

    /// <summary>Bare model stem, e.g. "SUI_W_0001". modelIndex is 1-based.</summary>
    public static string ModelStem(int modelIndex) => $"SUI_W_{modelIndex:D4}";

    /// <summary>Texture stem for a variant, e.g. "SUI_W_0001_V01". variant is 1-based.</summary>
    public static string TextureStem(int modelIndex, int variant = 1) => $"{ModelStem(modelIndex)}_V{variant:D2}";

    /// <summary>DBC ModelName1 value (logical, carries .mdx), e.g. "SUI_W_0001.mdx".</summary>
    public static string DbcModelName(int modelIndex) => $"{ModelStem(modelIndex)}.mdx";

    /// <summary>DBC TextureName1 value (bare stem, no dir/ext), e.g. "SUI_W_0001_V01".</summary>
    public static string DbcTextureName(int modelIndex, int variant = 1) => TextureStem(modelIndex, variant);

    /// <summary>Physical MPQ model member path, e.g. Item\ObjectComponents\Weapon\SUI_W_0001.m2.</summary>
    public static string ModelMpqPath(int modelIndex, string? componentDir = null) =>
        $@"{componentDir ?? WeaponDir}\{ModelStem(modelIndex)}.m2";

    /// <summary>Physical MPQ BLP member path, e.g. Item\ObjectComponents\Weapon\SUI_W_0001_V01.blp.</summary>
    public static string TextureMpqPath(int modelIndex, int variant = 1, string? componentDir = null) =>
        $@"{componentDir ?? WeaponDir}\{TextureStem(modelIndex, variant)}.blp";

    /// <summary>Effect-texture stem for multi-pass weapons (glow layers), e.g. "SUI_W_0001_E01".
    /// Unlike the display texture these are referenced by HARDCODED (Type-0) filenames inside the
    /// M2 — the way stock glowing weapons bind their effect layers. slot is 1-based.</summary>
    public static string EffectTextureStem(int modelIndex, int slot) => $"{ModelStem(modelIndex)}_E{slot:D2}";

    /// <summary>Physical MPQ member for an effect texture, e.g. Item\ObjectComponents\Weapon\SUI_W_0001_E01.blp.</summary>
    public static string EffectTextureMpqPath(int modelIndex, int slot, string? componentDir = null) =>
        $@"{componentDir ?? WeaponDir}\{EffectTextureStem(modelIndex, slot)}.blp";

    /// <summary>The canonical DBC file member path inside the patch MPQ.</summary>
    public const string ItemDisplayInfoMember = @"DBFilesClient\ItemDisplayInfo.dbc";
}
