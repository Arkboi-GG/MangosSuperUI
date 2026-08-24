using MangosSuperUI.Services.WeaponForge;

namespace MangosSuperUI.Services.ArmorForge;

/// <summary>One Armor Forge import lane: a later-client browse catalog plus the importer that turns
/// its entries into forge sources. Keyed "tbc" / "wotlk" like the weapon lanes.</summary>
public sealed class ArmorImportLane
{
    public ArmorImportLane(LegacyArmorCatalog catalog, LegacyArmorImporter importer)
    {
        Catalog = catalog;
        Importer = importer;
    }

    public string Key => Catalog.Key;
    public string Label => Catalog.Label;
    public LegacyArmorCatalog Catalog { get; }
    public LegacyArmorImporter Importer { get; }
}

/// <summary>Registry of the Armor Forge's import lanes (TBC and WotLK). Mirrors
/// <see cref="LegacyImportSources"/> on the weapon side: the controller, build service and UI pick a
/// lane by key and everything past resolution is lane-agnostic.</summary>
public sealed class ArmorImportSources
{
    public ArmorImportSources(TbcArmorCatalog tbcCatalog, TbcArmorImporter tbcImporter,
        WotlkArmorCatalog wotlkCatalog, WotlkArmorImporter wotlkImporter)
    {
        Tbc = new ArmorImportLane(tbcCatalog, tbcImporter);
        Wotlk = new ArmorImportLane(wotlkCatalog, wotlkImporter);
        All = new[] { Tbc, Wotlk };
    }

    public ArmorImportLane Tbc { get; }
    public ArmorImportLane Wotlk { get; }
    public IReadOnlyList<ArmorImportLane> All { get; }

    /// <summary>Lane by key; unknown/blank keys fall back to TBC (the original lane).</summary>
    public ArmorImportLane Get(string? key) =>
        string.Equals(key, WotlkMpqSource.SourceKey, StringComparison.OrdinalIgnoreCase) ? Wotlk : Tbc;
}
