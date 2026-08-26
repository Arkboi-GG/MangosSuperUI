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

    /// <summary>Lane by key. Blank falls back to TBC (the original lane, and what the pre-lane callers
    /// meant); an unrecognised key throws.
    ///
    /// The fallback used to swallow EVERY unknown key, "vanilla" included — and because the 2.4.3
    /// client contains almost every vanilla item, a vanilla entry id routed here resolved against the
    /// TBC archive and produced plausible art from the wrong client with no error anywhere. The vanilla
    /// clone lane has no client archive at all: it reads the world database. Callers that can receive
    /// "vanilla" must branch on it before asking for a lane, and this throw is what makes forgetting
    /// that a visible 500 in the log instead of silently wrong textures on a preview.</summary>
    public ArmorImportLane Get(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return Tbc;
        if (string.Equals(key, WotlkMpqSource.SourceKey, StringComparison.OrdinalIgnoreCase)) return Wotlk;
        if (string.Equals(key, TbcMpqSource.SourceKey, StringComparison.OrdinalIgnoreCase)) return Tbc;
        throw new ArgumentOutOfRangeException(nameof(key), key,
            $"'{key}' is not an Armor Forge import lane. Import lanes are '{TbcMpqSource.SourceKey}' and " +
            $"'{WotlkMpqSource.SourceKey}'; the vanilla clone lane reads the world database and has no client archive.");
    }
}
