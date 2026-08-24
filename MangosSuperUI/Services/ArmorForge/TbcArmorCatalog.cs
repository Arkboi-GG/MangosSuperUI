using MangosSuperUI.Services.WeaponForge;

namespace MangosSuperUI.Services.ArmorForge;

/// <summary>The TBC (2.4.3) armor browse. Nothing here is TBC-specific beyond the mount it is given
/// and where its endgame starts — the logic lives in <see cref="LegacyArmorCatalog"/>, which both
/// lanes derive from as siblings. (WotLK used to inherit from the TBC class, which read as though one
/// expansion were a special case of the other.)</summary>
public sealed class TbcArmorCatalog : LegacyArmorCatalog
{
    public TbcArmorCatalog(TbcItemCatalog catalog, TbcMpqSource mpq, ILogger<TbcArmorCatalog> logger)
        : base(catalog, mpq, logger) { }

    /// <summary>TBC endgame starts at T4 (ilvl 120) — see <see cref="LegacyArmorCatalog.FeaturedMinItemLevel"/>.</summary>
    protected override int FeaturedMinItemLevel => 120;
}
