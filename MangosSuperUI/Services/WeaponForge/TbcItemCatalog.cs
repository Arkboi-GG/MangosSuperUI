namespace MangosSuperUI.Services.WeaponForge;

/// <summary>The shipped TBC (2.4.3) catalog — <c>wwwroot/data/tbc-item-catalog.json</c>.</summary>
public sealed class TbcItemCatalog : LegacyItemCatalog
{
    public const string CatalogWebPathConst = "data/tbc-item-catalog.json";
    public TbcItemCatalog(IWebHostEnvironment env, ILogger<TbcItemCatalog> logger)
        : base(env, logger, CatalogWebPathConst, TbcMpqSource.SourceLabel) { }
}
