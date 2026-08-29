using Dapper;
using MangosSuperUI.Models;

namespace MangosSuperUI.Services.WeaponForge;

/// <summary>
/// The vanilla lane's item list. Where the TBC and WotLK catalogs read a SHIPPED json of a foreign
/// world database — necessary because the user has that client's files but not its database — the
/// vanilla lane reads the live <c>item_template</c> it is forging against. That is strictly better
/// here: no file to ship, no drift, and custom items the operator has already made show up as source
/// material like anything else.
///
/// Loaded once and cached for the life of the process, matching the shipped catalogs. A restart
/// picks up new items; that is the same freshness the other two lanes give.
/// </summary>
public sealed class VanillaItemCatalog : LegacyItemCatalog
{
    private readonly ConnectionFactory _db;
    private readonly ILogger<VanillaItemCatalog> _logger;

    public VanillaItemCatalog(IWebHostEnvironment env, ConnectionFactory db, ILogger<VanillaItemCatalog> logger)
        // No web path: Load() is overridden and never touches wwwroot.
        : base(env, logger, catalogWebPath: "", label: VanillaMpqSource.SourceLabel)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>Every weapon and shield in the world DB, in the shape the import lanes expect.
    /// Class 2 = weapon, class 4 subclass 6 = shield — the same filter the clone lane applies, so
    /// both vanilla lanes offer the same items and differ only in what they do with them.
    ///
    /// Custom entries are excluded: forged items already have a display of their own, and offering
    /// them here would invite importing our own output back through the pipeline.</summary>
    protected override IReadOnlyList<LegacyItemInfo> Load()
    {
        try
        {
            using var conn = _db.Mangos();
            conn.Open();

            // ONE row per entry, the highest patch. vmangos item_template is keyed on
            // (entry, patch), so a plain SELECT returns an item several times over — and the join
            // that resolves a model runs off whichever copy happened to load first. Measured: asking
            // for Thunderfury (19019) came back with Ashbringer\'s model, because a lower-patch row
            // for that entry carried a different display_id. The clone lane has always taken
            // "ORDER BY patch DESC LIMIT 1" for exactly this reason; this is the set-wide form.
            var rows = conn.Query(
                @"SELECT t.entry, t.name, t.class AS ItemClass, t.subclass AS Subclass,
                         t.display_id AS DisplayId, t.quality, t.inventory_type AS InventoryType,
                         t.item_level AS ItemLevel, t.required_level AS RequiredLevel,
                         t.delay, t.dmg_min1 AS DmgMin, t.dmg_max1 AS DmgMax, t.sheath
                  FROM item_template t
                  INNER JOIN (SELECT entry, MAX(patch) AS patch FROM item_template GROUP BY entry) newest
                          ON newest.entry = t.entry AND newest.patch = t.patch
                  WHERE (t.class = 2 OR (t.class = 4 AND t.subclass = 6))
                    AND t.display_id > 0
                    AND t.entry < @floor
                  ORDER BY t.name",
                new { floor = WeaponIdReservationService.ItemEntryFloor });

            var items = new List<LegacyItemInfo>();
            foreach (var r in rows)
            {
                items.Add(new LegacyItemInfo
                {
                    Entry = (uint)Convert.ToUInt32(r.entry),
                    Name = (string?)r.name ?? "",
                    ItemClass = Convert.ToInt32(r.ItemClass),
                    Subclass = Convert.ToInt32(r.Subclass),
                    DisplayId = (uint)Convert.ToUInt32(r.DisplayId),
                    Quality = Convert.ToInt32(r.quality),
                    InventoryType = Convert.ToInt32(r.InventoryType),
                    ItemLevel = Convert.ToInt32(r.ItemLevel),
                    RequiredLevel = Convert.ToInt32(r.RequiredLevel),
                    DelayMs = Convert.ToInt32(r.delay),
                    DmgMin = Convert.ToSingle(r.DmgMin),
                    DmgMax = Convert.ToSingle(r.DmgMax),
                    Sheath = Convert.ToInt32(r.sheath),
                });
            }

            _logger.LogInformation("VanillaItemCatalog: loaded {Count} stock weapon(s)/shield(s) from item_template", items.Count);
            return items;
        }
        catch (Exception ex)
        {
            // Same contract as the shipped catalogs: a load failure degrades the browse to raw model
            // stems from the mounted archives rather than taking the lane down.
            _logger.LogWarning(ex, "VanillaItemCatalog: could not read item_template — browse degrades to model stems");
            return Array.Empty<LegacyItemInfo>();
        }
    }
}
