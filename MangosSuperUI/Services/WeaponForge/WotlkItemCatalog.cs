namespace MangosSuperUI.Services.WeaponForge;

/// <summary>The shipped WotLK (3.3.5a) catalog — <c>wwwroot/data/wotlk-item-catalog.json</c>.</summary>
public sealed class WotlkItemCatalog : LegacyItemCatalog
{
    public const string CatalogWebPathConst = "data/wotlk-item-catalog.json";
    public WotlkItemCatalog(IWebHostEnvironment env, ILogger<WotlkItemCatalog> logger)
        : base(env, logger, CatalogWebPathConst, WotlkMpqSource.SourceLabel) { }
}
