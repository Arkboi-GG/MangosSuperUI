using MangosSuperUI.Services;
using MangosSuperUI.Services.WeaponForge;

namespace MangosSuperUI.Services.ArmorForge;

/// <summary>The WotLK (3.3.5a) armor importer — the same lanes over the WotLK catalog/mount. The only
/// expansion-specific step is model parsing (v264 + .skin), which <see cref="LegacyArmorCatalog.LoadM2"/>
/// hides; re-emission onto the vanilla helm/shoulder donors is the same proven chain.</summary>
public sealed class WotlkArmorImporter : LegacyArmorImporter
{
    public WotlkArmorImporter(WotlkArmorCatalog catalog, MpqReaderService vanilla, ILogger<WotlkArmorImporter> logger)
        : base(catalog, vanilla, logger) { }
}
