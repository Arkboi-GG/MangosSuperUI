using MangosSuperUI.Services;
using MangosSuperUI.Services.WeaponForge;

namespace MangosSuperUI.Services.ArmorForge;

/// <summary>The TBC (2.4.3) armor importer. Lane-specific only in which catalog/mount it is handed —
/// everything else lives in <see cref="LegacyArmorImporter"/>, which both lanes derive from as
/// siblings rather than one inheriting from the other.</summary>
public sealed class TbcArmorImporter : LegacyArmorImporter
{
    public TbcArmorImporter(TbcArmorCatalog catalog, MpqReaderService vanilla, ILogger<TbcArmorImporter> logger)
        : base(catalog, vanilla, logger) { }
}
