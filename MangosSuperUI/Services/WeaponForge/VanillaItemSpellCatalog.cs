using Dapper;
using MangosSuperUI.Models;

namespace MangosSuperUI.Services.WeaponForge;

/// <summary>
/// Cached set of complete item-spell slots from the immutable original-item baseline. Spell.dbc
/// proves an ID is installed; this catalog additionally preserves the native trigger, charges,
/// proc rate, cooldowns, and category used by a stock item.
/// </summary>
public sealed class VanillaItemSpellCatalog
{
    private const int CustomItemFloor = 900000;

    private readonly ConnectionFactory _db;
    private readonly SemaphoreSlim _cacheGate = new(1, 1);
    private IReadOnlyList<NativeItemSpellUsage>? _usageCache;

    public VanillaItemSpellCatalog(ConnectionFactory db) => _db = db;

    public async Task<IReadOnlyList<NativeItemSpellUsage>> GetUsageAsync(
        CancellationToken cancellationToken = default)
    {
        var cached = _usageCache;
        if (cached is not null) return cached;

        await _cacheGate.WaitAsync(cancellationToken);
        try
        {
            cached = _usageCache;
            if (cached is not null) return cached;

            using var conn = _db.Admin();
            var command = new CommandDefinition(StockItemSpellUsageSql,
                new { CustomItemFloor }, cancellationToken: cancellationToken);
            cached = (await conn.QueryAsync<NativeItemSpellUsage>(command)).ToList();
            _usageCache = cached;
            return cached;
        }
        finally
        {
            _cacheGate.Release();
        }
    }

    // The public catalog deliberately exposes full slot rows only.

    private const string StockItemSpellUsageSql = """
        SELECT spell_id AS SpellId,
               trigger_value AS TriggerValue,
               charges AS Charges,
               ppm_rate AS PpmRate,
               cooldown_ms AS CooldownMs,
               category AS Category,
               category_cooldown_ms AS CategoryCooldownMs,
               COUNT(DISTINCT entry) AS UsedByCount
        FROM (
            SELECT item.entry, item.spellid_1 AS spell_id, item.spelltrigger_1 AS trigger_value,
                   item.spellcharges_1 AS charges, item.spellppmrate_1 AS ppm_rate,
                   item.spellcooldown_1 AS cooldown_ms, item.spellcategory_1 AS category,
                   item.spellcategorycooldown_1 AS category_cooldown_ms
              FROM og_item_template item
             WHERE item.entry < @CustomItemFloor
               AND item.patch = (SELECT MAX(i2.patch) FROM og_item_template i2 WHERE i2.entry = item.entry)
            UNION ALL
            SELECT item.entry, item.spellid_2, item.spelltrigger_2, item.spellcharges_2, item.spellppmrate_2,
                   item.spellcooldown_2, item.spellcategory_2, item.spellcategorycooldown_2
              FROM og_item_template item
             WHERE item.entry < @CustomItemFloor
               AND item.patch = (SELECT MAX(i2.patch) FROM og_item_template i2 WHERE i2.entry = item.entry)
            UNION ALL
            SELECT item.entry, item.spellid_3, item.spelltrigger_3, item.spellcharges_3, item.spellppmrate_3,
                   item.spellcooldown_3, item.spellcategory_3, item.spellcategorycooldown_3
              FROM og_item_template item
             WHERE item.entry < @CustomItemFloor
               AND item.patch = (SELECT MAX(i2.patch) FROM og_item_template i2 WHERE i2.entry = item.entry)
            UNION ALL
            SELECT item.entry, item.spellid_4, item.spelltrigger_4, item.spellcharges_4, item.spellppmrate_4,
                   item.spellcooldown_4, item.spellcategory_4, item.spellcategorycooldown_4
              FROM og_item_template item
             WHERE item.entry < @CustomItemFloor
               AND item.patch = (SELECT MAX(i2.patch) FROM og_item_template i2 WHERE i2.entry = item.entry)
            UNION ALL
            SELECT item.entry, item.spellid_5, item.spelltrigger_5, item.spellcharges_5, item.spellppmrate_5,
                   item.spellcooldown_5, item.spellcategory_5, item.spellcategorycooldown_5
              FROM og_item_template item
             WHERE item.entry < @CustomItemFloor
               AND item.patch = (SELECT MAX(i2.patch) FROM og_item_template i2 WHERE i2.entry = item.entry)
        ) native_effects
        WHERE spell_id > 0 AND trigger_value IN (0, 1, 2)
        GROUP BY spell_id, trigger_value, charges, ppm_rate, cooldown_ms, category, category_cooldown_ms
        """;

    // Skill and faction requirement IDs are validated against their installed DBC tables.
}

public sealed class NativeItemSpellUsage
{
    public uint SpellId { get; set; }
    public int TriggerValue { get; set; }
    public int Charges { get; set; }
    public float PpmRate { get; set; }
    public int CooldownMs { get; set; }
    public int Category { get; set; }
    public int CategoryCooldownMs { get; set; }
    public int UsedByCount { get; set; }
}

// Generic requirement ID catalogs are exposed by DbcService.
