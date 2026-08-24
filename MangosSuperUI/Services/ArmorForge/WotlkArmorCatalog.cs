using MangosSuperUI.Services.WeaponForge;

namespace MangosSuperUI.Services.ArmorForge;

/// <summary>The WotLK (3.3.5a) armor browse — same catalog logic over the WotLK mount + shipped
/// WotLK catalog. 3.3.5a ItemDisplayInfo has 25 fields (second inventory icon at [6]) which the
/// shift logic above handles; ItemSet.dbc is the same 53-field layout as 2.4.3.</summary>
public sealed class WotlkArmorCatalog : LegacyArmorCatalog
{
    public WotlkArmorCatalog(WotlkItemCatalog catalog, WotlkMpqSource mpq, ILogger<WotlkArmorCatalog> logger)
        : base(catalog, mpq, logger) { }

    /// <summary>WotLK endgame starts at T7 (ilvl 200) — see <see cref="LegacyArmorCatalog.FeaturedMinItemLevel"/>.</summary>
    protected override int FeaturedMinItemLevel => 200;
}
