using Microsoft.AspNetCore.Mvc;
using MangosSuperUI.Models;
using MangosSuperUI.Services;
using Dapper;
using System.Text.Json;
using MySqlConnector;

namespace MangosSuperUI.Controllers;

// ══════════════════════════════════════════════════════════════════════════
//  CRAFTING LOOTIFIER
//
//  Additive-only twin of the Quest/ARPG Lootifiers. For a crafted GEAR output
//  it generates variants that PRESERVE the base item's stats (never reduced,
//  never dropped) and layer a tier-sized bonus on top. A C++ hook in
//  Spell::DoCreateItem swaps the plain craft for a rolled variant at create
//  time. Stat-less white gear MINTS a green/blue set from item level instead.
//
//  Storage: reuses lootifier_generated_items with the CRAFTING SENTINEL
//  creature_entry = -1. Entries come from the shared GetNextLootifierId, so
//  they never collide with quest/ARPG rows.
//
//  Batching: professions are enumerated from DBC (SkillLineAbility.dbc → recipe
//  spells → Spell.dbc effect 24 → output item) via DbcService, then filtered to
//  EQUIPPABLE gear only (weapon/armor with an inventory slot — shirts and tabards
//  included, sharpening stones / oils / reagents excluded).
// ══════════════════════════════════════════════════════════════════════════

public class CraftingLootifierController : Controller
{
    private readonly ConnectionFactory _db;
    private readonly DbcService _dbc;
    private readonly AuditService _audit;

    private const int CRAFT_SENTINEL_CREATURE = -1;  // creature_entry sentinel for crafting variants
    private const int LOOTIFIER_ID_START = 950000;   // shared allocator floor (matches Quest/ARPG)

    private const float MIN_DELTA_BUDGET = 2.0f;     // additive floor: always a real upgrade

    // White (stat-less) mint magnitude: budget ≈ itemLevel * factor, floored at 1.
    // Tuned so a level ~4 white lands at ~+1 green / ~+2 blue. The two dials.
    private const float MINT_GREEN_FACTOR = 0.20f;   // Improved / of Power → green
    private const float MINT_BLUE_FACTOR = 0.40f;   // of Glory            → blue

    // ── Stat mapping (identical to LootifierController — kept in sync) ──
    private static readonly Dictionary<int, string> STAT_NAMES = new()
    {
        [0] = "None",
        [1] = "Health",
        [3] = "Agility",
        [4] = "Strength",
        [5] = "Intellect",
        [6] = "Spirit",
        [7] = "Stamina"
    };
    private static readonly Dictionary<string, HashSet<int>> STAT_FAMILIES = new()
    {
        ["physical"] = new HashSet<int> { 3, 4, 7 },
        ["caster"] = new HashSet<int> { 5, 6, 7 },
        ["hybrid"] = new HashSet<int> { 3, 4, 5, 6, 7 },
    };
    private static readonly Dictionary<int, float> DEFAULT_STAT_WEIGHTS = new()
    {
        [3] = 1.0f,
        [4] = 1.0f,
        [5] = 1.0f,
        [6] = 1.0f,
        [7] = 0.6667f
    };

    private const int SPELLTRIGGER_USE = 0;
    private const int SPELLTRIGGER_EQUIP = 1;
    private const int SPELLTRIGGER_CHANCE_ON_HIT = 2;

    private const int ITEM_CLASS_WEAPON = 2;
    private const int ITEM_CLASS_ARMOR = 4;

    public CraftingLootifierController(ConnectionFactory db, DbcService dbc, AuditService audit)
    {
        _db = db;
        _dbc = dbc;
        _audit = audit;
    }

    public IActionResult Index() => View();

    [HttpGet]
    public IActionResult Meta()
    {
        return Json(new
        {
            sentinel = CRAFT_SENTINEL_CREATURE,
            defaultRuleset = DefaultRuleset(),
            bandThresholds = new { improvedMax = 20, powerMax = 30, gloryMax = 40 }
        });
    }

    // ══════════════════════════════════════════════════════════════════════
    //  ITEM LOOKUP
    // ══════════════════════════════════════════════════════════════════════

    [HttpGet]
    public async Task<IActionResult> SearchItem(string q)
    {
        if (string.IsNullOrWhiteSpace(q))
            return Json(Array.Empty<object>());

        using var conn = _db.Mangos();
        bool isId = int.TryParse(q.Trim(), out int idQ);

        // class 2/4 + inventory_type > 0 = equippable gear (weapons, armor, trinkets,
        // jewelry, shirts, tabards). entry < LOOTIFIER_ID_START excludes our own
        // generated variants so they never show up as pickable base gear.
        var rows = (await conn.QueryAsync(@"
            SELECT entry, name, quality, class, subclass, inventory_type, display_id, item_level
            FROM item_template
            WHERE patch = (SELECT MAX(patch) FROM item_template it2 WHERE it2.entry = item_template.entry)
              AND entry < @LootStart
              AND class IN (@W, @A) AND inventory_type > 0 AND quality > 0
              AND (@IsId = 1 AND entry = @Id OR @IsId = 0 AND name LIKE @Like)
            ORDER BY quality DESC, name ASC
            LIMIT 50",
            new
            {
                W = ITEM_CLASS_WEAPON,
                A = ITEM_CLASS_ARMOR,
                LootStart = LOOTIFIER_ID_START,
                IsId = isId ? 1 : 0,
                Id = idQ,
                Like = $"%{q.Trim()}%"
            }))
            .ToList();

        using var adminConn = _db.Admin();
        var lootified = new HashSet<int>(await adminConn.QueryAsync<int>(
            "SELECT DISTINCT base_entry FROM lootifier_generated_items WHERE creature_entry = @CE",
            new { CE = CRAFT_SENTINEL_CREATURE }));

        var result = rows.Select(r => new
        {
            entry = (int)r.entry,
            name = (string)r.name,
            quality = (int)r.quality,
            iconPath = _dbc.GetItemIconPath((uint)(int)r.display_id),
            itemLevel = (int)r.item_level,
            lootified = lootified.Contains((int)r.entry)
        });
        return Json(result);
    }

    [HttpGet]
    public async Task<IActionResult> ItemInfo(int entry)
    {
        using var conn = _db.Mangos();
        var item = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
            SELECT * FROM item_template
            WHERE entry = @E AND patch = (SELECT MAX(patch) FROM item_template it2 WHERE it2.entry = @E)",
            new { E = entry });
        if (item == null) return Json(new { found = false });

        var analysis = AnalyzeItemStats(item);

        using var adminConn = _db.Admin();
        int variantCount = await adminConn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM lootifier_generated_items WHERE creature_entry = @CE AND base_entry = @B",
            new { CE = CRAFT_SENTINEL_CREATURE, B = entry });

        return Json(new
        {
            found = true,
            entry,
            name = (string)item.name,
            quality = (int)analysis.itemQuality,
            eligible = IsLootifiable(analysis),
            stats = ((List<object>)analysis.stats),
            weightedBudget = (float)analysis.weightedBudget,
            detectedFamily = (string)analysis.detectedFamily,
            variantCount,
            lootified = variantCount > 0
        });
    }

    // ══════════════════════════════════════════════════════════════════════
    //  PROFESSIONS (browse + batch)
    // ══════════════════════════════════════════════════════════════════════

    // List gear professions with recipe / equippable-output / lootified counts.
    // Also surfaces the DBC create-item count as a health check — if it's ~0 the
    // Spell.dbc effect offset is wrong.
    [HttpGet]
    public async Task<IActionResult> Professions()
    {
        using var mangosConn = _db.Mangos();
        using var adminConn = _db.Admin();

        var lootified = new HashSet<int>(await adminConn.QueryAsync<int>(
            "SELECT DISTINCT base_entry FROM lootifier_generated_items WHERE creature_entry = @CE",
            new { CE = CRAFT_SENTINEL_CREATURE }));

        var outList = new List<object>();
        foreach (var (id, name) in _dbc.GetProfessions())
        {
            var outputs = _dbc.GetProfessionOutputs(id);
            var itemIds = outputs.Select(o => (int)o.itemEntry).ToList();
            var meta = await FetchItemMeta(mangosConn, itemIds);

            int equippable = 0, done = 0;
            foreach (var (itemEntry, _) in outputs)
            {
                if (!meta.TryGetValue((int)itemEntry, out var m)) continue;
                if (IsEquippableGear(m.cls, m.inv)) equippable++;
                if (lootified.Contains((int)itemEntry)) done++;
            }

            outList.Add(new
            {
                id,
                name,
                totalRecipes = outputs.Count,
                equippableOutputs = equippable,
                lootified = done
            });
        }

        int dbcCreateItems = _dbc.LoadedCounts.TryGetValue("SpellCreatedItem", out var c) ? c : 0;
        return Json(new { professions = outList, dbcCreateItems });
    }

    // Ordered recipe list for one profession — for browsing what would be lootified.
    [HttpGet]
    public async Task<IActionResult> ProfessionRecipes(int skillLineId)
    {
        using var mangosConn = _db.Mangos();
        using var adminConn = _db.Admin();

        var outputs = _dbc.GetProfessionOutputs((uint)skillLineId);   // rank-ordered
        var itemIds = outputs.Select(o => (int)o.itemEntry).ToList();
        var meta = await FetchItemMeta(mangosConn, itemIds);

        var lootified = new HashSet<int>(await adminConn.QueryAsync<int>(
            "SELECT DISTINCT base_entry FROM lootifier_generated_items WHERE creature_entry = @CE",
            new { CE = CRAFT_SENTINEL_CREATURE }));

        // Order by the output item's level (low → high) — real SQL data, unlike the
        // SkillLineAbility rank which comes back constant.
        var ordered = outputs
            .Where(o => meta.ContainsKey((int)o.itemEntry))
            .OrderBy(o => meta[(int)o.itemEntry].itemLevel)
            .ThenBy(o => (int)o.itemEntry)
            .ToList();

        var recipes = new List<object>();
        foreach (var (itemEntry, minRank) in ordered)
        {
            var m = meta[(int)itemEntry];
            recipes.Add(new
            {
                entry = (int)itemEntry,
                name = m.name,
                quality = m.quality,
                iconPath = _dbc.GetItemIconPath((uint)m.displayId),
                itemClass = m.cls,
                invType = m.inv,
                itemLevel = m.itemLevel,
                equippable = IsEquippableGear(m.cls, m.inv),
                lootified = lootified.Contains((int)itemEntry),
                minRank
            });
        }

        return Json(new { skillLineId, name = _dbc.GetProfessionName((uint)skillLineId), recipes });
    }

    [HttpPost]
    public async Task<IActionResult> ProfessionBatchCommit([FromBody] CraftingProfessionRequest request)
    {
        if (request == null || request.skillLineId <= 0)
            return Json(new { success = false, error = "No profession selected" });

        using var mangosConn = _db.Mangos();
        using var adminConn = _db.Admin();
        await EnsureTrackingTables(adminConn);
        var columns = await GetItemColumns(mangosConn);
        var ruleset = request.ruleset ?? DefaultRuleset();

        var outputs = _dbc.GetProfessionOutputs((uint)request.skillLineId);
        var itemIds = outputs.Select(o => (int)o.itemEntry).ToList();
        var meta = await FetchItemMeta(mangosConn, itemIds);

        // EQUIPPABLE gear only — no sharpening stones, oils, reagents, consumables.
        var bases = outputs
            .Select(o => (int)o.itemEntry)
            .Where(e => meta.TryGetValue(e, out var m) && IsEquippableGear(m.cls, m.inv))
            .Distinct()
            .ToList();

        var (itemsCreated, basesProcessed) = await RunGeneration(mangosConn, adminConn, columns, ruleset, bases);

        await _audit.LogAsync(new AuditEntry
        {
            Operator = "admin",
            OperatorIp = HttpContext.Connection.RemoteIpAddress?.ToString(),
            Category = "content",
            Action = "crafting_lootifier_profession_commit",
            TargetType = "crafting_lootifier",
            TargetName = _dbc.GetProfessionName((uint)request.skillLineId),
            StateBefore = "{}",
            StateAfter = JsonSerializer.Serialize(new { itemsCreated, basesProcessed }),
            IsReversible = true,
            Success = true,
            Notes = $"Crafting Lootifier profession batch ({_dbc.GetProfessionName((uint)request.skillLineId)}): {itemsCreated} variants across {basesProcessed} items"
        });

        return Json(new
        {
            success = true,
            profession = _dbc.GetProfessionName((uint)request.skillLineId),
            equippableOutputs = bases.Count,
            itemsCreated,
            basesProcessed,
            reloadHint = "Restart mangosd (new prototypes) before crafting; '.reload crafting_variants' only refreshes the mapping."
        });
    }

    // ══════════════════════════════════════════════════════════════════════
    //  PREVIEW / COMMIT
    // ══════════════════════════════════════════════════════════════════════

    [HttpPost]
    public async Task<IActionResult> Preview([FromBody] CraftingGenerateRequest request)
    {
        if (request?.itemEntries == null || request.itemEntries.Length == 0)
            return Json(new { success = false, error = "No items selected" });

        var ruleset = request.ruleset ?? DefaultRuleset();
        using var conn = _db.Mangos();

        var outItems = new List<object>();
        foreach (var entry in request.itemEntries.Distinct())
        {
            var item = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
                SELECT * FROM item_template
                WHERE entry = @E AND patch = (SELECT MAX(patch) FROM item_template it2 WHERE it2.entry = @E)",
                new { E = entry });
            if (item == null) continue;

            var analysis = AnalyzeItemStats(item);
            if (!IsLootifiable(analysis)) continue;

            var variants = GenerateVariants(item, analysis, ruleset);
            outItems.Add(new
            {
                entry,
                name = (string)item.name,
                quality = (int)analysis.itemQuality,
                variants = VariantsToJson(variants, (int)analysis.itemQuality)
            });
        }

        return Json(new { success = true, items = outItems });
    }

    [HttpPost]
    public async Task<IActionResult> Commit([FromBody] CraftingGenerateRequest request)
    {
        var baseItems = (request?.itemEntries ?? Array.Empty<int>()).Distinct().ToList();
        if (baseItems.Count == 0)
            return Json(new { success = false, error = "No items selected" });

        using var mangosConn = _db.Mangos();
        using var adminConn = _db.Admin();
        await EnsureTrackingTables(adminConn);
        var columns = await GetItemColumns(mangosConn);
        var ruleset = request!.ruleset ?? DefaultRuleset();

        var (itemsCreated, basesProcessed) = await RunGeneration(mangosConn, adminConn, columns, ruleset, baseItems);

        await _audit.LogAsync(new AuditEntry
        {
            Operator = "admin",
            OperatorIp = HttpContext.Connection.RemoteIpAddress?.ToString(),
            Category = "content",
            Action = "crafting_lootifier_commit",
            TargetType = "crafting_lootifier",
            TargetName = $"{baseItems.Count} items",
            StateBefore = "{}",
            StateAfter = JsonSerializer.Serialize(new { itemsCreated, basesProcessed }),
            IsReversible = true,
            Success = true,
            Notes = $"Crafting Lootifier: {itemsCreated} variants across {basesProcessed} items"
        });

        return Json(new
        {
            success = true,
            itemsCreated,
            basesProcessed,
            reloadHint = "Restart mangosd (new prototypes) or '.reload crafting_variants' if only the mapping changed."
        });
    }

    // Shared generation loop. Flushes tracking PER BASE so a crash mid-run orphans
    // at most one item's variants (cleanable by the orphan sweep).
    private async Task<(int itemsCreated, int basesProcessed)> RunGeneration(
        MySqlConnector.MySqlConnection mangosConn, MySqlConnector.MySqlConnection adminConn,
        List<string> columns, CraftingRulesetDto ruleset, List<int> baseItems)
    {
        var existing = new HashSet<int>(await adminConn.QueryAsync<int>(
            "SELECT DISTINCT base_entry FROM lootifier_generated_items WHERE creature_entry = @CE",
            new { CE = CRAFT_SENTINEL_CREATURE }));

        int itemsCreated = 0, basesProcessed = 0;

        foreach (var baseEntry in baseItems)
        {
            var item = await mangosConn.QueryFirstOrDefaultAsync<dynamic>(@"
                SELECT * FROM item_template
                WHERE entry = @E AND patch = (SELECT MAX(patch) FROM item_template it2 WHERE it2.entry = @E)",
                new { E = baseEntry });
            if (item == null) continue;

            var analysis = AnalyzeItemStats(item);
            if (!IsLootifiable(analysis)) continue;

            if (existing.Contains(baseEntry))
                await PurgeCraftingVariantsForBase(mangosConn, adminConn, baseEntry);

            var variants = GenerateVariants(item, analysis, ruleset);
            int nextId = await GetNextLootifierId(adminConn);

            var trackingRows = new List<(int genEntry, int baseEntry, int creatureEntry, float budgetPct, string tierName)>();
            foreach (var v in variants)
            {
                int newEntry = nextId++;
                var roll = VariantToCommitRoll(v);
                await InsertCraftingVariant(mangosConn, columns, item, newEntry, roll, (int)analysis.itemQuality, ruleset.goldValueScalePct,
                    ResolveBandGoldBump(ruleset.bands, roll.tierLabel, roll.budgetPct), ruleset.legendaryGoldBumpPct);
                trackingRows.Add((newEntry, baseEntry, CRAFT_SENTINEL_CREATURE, roll.budgetPct, CanonicalTier(roll.tierLabel, roll.budgetPct)));
                itemsCreated++;
            }
            await FlushTrackingItems(adminConn, trackingRows);   // per-base flush
            basesProcessed++;
        }

        return (itemsCreated, basesProcessed);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  ROLLBACK / STATUS / BROWSE
    // ══════════════════════════════════════════════════════════════════════

    [HttpPost]
    public async Task<IActionResult> Rollback([FromBody] CraftingRollbackRequest request)
    {
        using var mangosConn = _db.Mangos();
        using var adminConn = _db.Admin();

        int removed, orphans = 0;
        if (request != null && request.baseEntry > 0)
        {
            removed = await PurgeCraftingVariantsForBase(mangosConn, adminConn, request.baseEntry);
        }
        else
        {
            var genIds = (await adminConn.QueryAsync<int>(
                "SELECT generated_entry FROM lootifier_generated_items WHERE creature_entry = @CE",
                new { CE = CRAFT_SENTINEL_CREATURE })).ToList();

            foreach (var chunk in genIds.Chunk(500))
                await mangosConn.ExecuteAsync("DELETE FROM item_template WHERE entry IN @Ids", new { Ids = chunk });

            removed = await adminConn.ExecuteAsync(
                "DELETE FROM lootifier_generated_items WHERE creature_entry = @CE",
                new { CE = CRAFT_SENTINEL_CREATURE });

            orphans = await SweepOrphansInternal(mangosConn);
        }

        await _audit.LogAsync(new AuditEntry
        {
            Operator = "admin",
            OperatorIp = HttpContext.Connection.RemoteIpAddress?.ToString(),
            Category = "content",
            Action = "crafting_lootifier_rollback",
            TargetType = "crafting_lootifier",
            TargetName = request?.baseEntry > 0 ? $"item {request.baseEntry}" : "all",
            StateBefore = "{}",
            StateAfter = JsonSerializer.Serialize(new { removed, orphans }),
            IsReversible = false,
            Success = true,
            Notes = $"Crafting Lootifier rollback: removed {removed} variants, swept {orphans} orphans"
        });

        return Json(new { success = true, removed, orphans, reloadHint = "Run '.reload crafting_variants' so the core drops the removed variants." });
    }

    // ═════════════════════════ REVALUE ═════════════════════════

    /// <summary>
    /// Lists the tiers that ACTUALLY EXIST in tracking, with variant counts and
    /// the price multiplier each tier is MEASURED to be sitting at right now
    /// (variant sell_price / base sell_price — read from the DB, not assumed).
    /// This is what the Revalue dialog renders: your tier names, your numbers.
    /// Note crafting stores CANONICAL tier names ("improved"/"power"/"glory"/
    /// "gods"), so those are the strings you'll see and set.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> RevalueTiers()
    {
        using var mangosConn = _db.Mangos();
        using var adminConn = _db.Admin();

        if (!await TableExists(adminConn, "lootifier_generated_items"))
            return Json(new { success = true, tiers = Array.Empty<object>() });

        var tracked = (await adminConn.QueryAsync<dynamic>(
            "SELECT generated_entry, base_entry, tier_name FROM lootifier_generated_items WHERE creature_entry = @CE",
            new { CE = CRAFT_SENTINEL_CREATURE })).ToList();

        if (tracked.Count == 0)
            return Json(new { success = true, tiers = Array.Empty<object>() });

        var prices = await FetchItemPrices(mangosConn,
            tracked.Select(t => (int)t.base_entry).Concat(tracked.Select(t => (int)t.generated_entry)));

        var tiers = tracked
            .GroupBy(t => (string)(t.tier_name ?? ""))
            .Select(g =>
            {
                var measured = new List<double>();
                string sampleName = "";
                long sampleBase = 0, sampleCur = 0;
                foreach (var t in g)
                {
                    if (!prices.TryGetValue((int)t.base_entry, out var bp)) continue;
                    if (!prices.TryGetValue((int)t.generated_entry, out var vp)) continue;
                    if (bp.sell <= 0) continue;
                    measured.Add((double)vp.sell / bp.sell);
                    if (sampleName.Length == 0)
                    {
                        sampleName = vp.name;
                        sampleBase = bp.sell;
                        sampleCur = vp.sell;
                    }
                }
                return new
                {
                    tier = g.Key,
                    count = g.Count(),
                    currentMult = measured.Count > 0 ? (double?)Math.Round(measured.Average(), 3) : null,
                    sampleName,
                    sampleBaseSell = sampleBase,
                    sampleCurrentSell = sampleCur
                };
            })
            .OrderBy(x => x.currentMult ?? 0)
            .ToList();

        return Json(new { success = true, tiers });
    }

    /// <summary>
    /// Sets prices IN PLACE for the tiers you explicitly name, using EXACTLY the
    /// bump you typed: new_price = base_price × (1 + goldBumpPct/100). No curve,
    /// no master scale, no hidden legendary stack — the number you enter is the
    /// number that lands.
    ///
    /// Absolute recompute from the BASE item, so it never compounds; run it as
    /// often as you like. A tier you leave blank is NOT TOUCHED. Entries, names,
    /// display IDs and stats are untouched, so retexture mappings survive.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Revalue([FromBody] CraftingRevalueRequest? request)
    {
        var wanted = (request?.tiers ?? Array.Empty<CraftingTierBumpDto>())
            .Where(t => t.goldBumpPct.HasValue)
            .GroupBy(t => t.tier ?? "", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last().goldBumpPct!.Value, StringComparer.OrdinalIgnoreCase);

        if (wanted.Count == 0)
            return Json(new { success = false, error = "No tier was given a value — nothing to do." });

        using var mangosConn = _db.Mangos();
        using var adminConn = _db.Admin();

        if (!await TableExists(adminConn, "lootifier_generated_items"))
            return Json(new { success = false, error = "No lootifier data found" });

        var tracked = (await adminConn.QueryAsync<dynamic>(
            "SELECT generated_entry, base_entry, tier_name FROM lootifier_generated_items WHERE creature_entry = @CE",
            new { CE = CRAFT_SENTINEL_CREATURE })).ToList();

        // Only variants whose tier you actually gave a number. Everything else: untouched.
        var targets = tracked.Where(t => wanted.ContainsKey((string)(t.tier_name ?? ""))).ToList();
        if (targets.Count == 0)
            return Json(new { success = true, updated = 0, perTier = Array.Empty<object>() });

        var basePrices = await FetchItemPrices(mangosConn, targets.Select(t => (int)t.base_entry));

        var perTier = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int updated = 0;

        foreach (var chunk in targets.Chunk(500))
        {
            var buyCase = new System.Text.StringBuilder("CASE entry ");
            var sellCase = new System.Text.StringBuilder("CASE entry ");
            var ids = new List<int>();

            foreach (var t in chunk)
            {
                int gen = (int)t.generated_entry;
                if (!basePrices.TryGetValue((int)t.base_entry, out var bp)) continue;

                string tier = (string)(t.tier_name ?? "");
                double mult = 1.0 + wanted[tier] / 100.0;   // exactly what you typed

                long newBuy = (long)Math.Round(bp.buy * mult);
                long newSell = (long)Math.Round(bp.sell * mult);
                buyCase.Append("WHEN ").Append(gen).Append(" THEN ").Append(newBuy).Append(' ');
                sellCase.Append("WHEN ").Append(gen).Append(" THEN ").Append(newSell).Append(' ');
                ids.Add(gen);
                perTier[tier] = perTier.GetValueOrDefault(tier) + 1;
            }

            if (ids.Count == 0) continue;
            buyCase.Append("ELSE buy_price END");
            sellCase.Append("ELSE sell_price END");

            await mangosConn.ExecuteAsync(
                $"UPDATE item_template SET buy_price = {buyCase}, sell_price = {sellCase} WHERE entry IN ({string.Join(",", ids)})");
            updated += ids.Count;
        }

        return Json(new
        {
            success = true,
            updated,
            perTier = perTier.Select(kv => new { tier = kv.Key, count = kv.Value }).ToList(),
            reloadHint = "Prices changed in item_template — restart or clear the item cache so live clients see them."
        });
    }

    // Entry -> (buy, sell, name) at max patch, chunked.
    private async Task<Dictionary<int, (long buy, long sell, string name)>> FetchItemPrices(
        MySqlConnector.MySqlConnection conn, IEnumerable<int> entries)
    {
        var map = new Dictionary<int, (long buy, long sell, string name)>();
        foreach (var chunk in entries.Distinct().Chunk(500))
        {
            var rows = await conn.QueryAsync<dynamic>(@"
                SELECT t.entry, t.buy_price, t.sell_price, t.name
                FROM item_template t
                JOIN (SELECT entry, MAX(patch) mp FROM item_template WHERE entry IN @Ids GROUP BY entry) m
                  ON m.entry = t.entry AND m.mp = t.patch
                WHERE t.entry IN @Ids",
                new { Ids = chunk });
            foreach (var r in rows)
                map[(int)r.entry] = (Convert.ToInt64(r.buy_price), Convert.ToInt64(r.sell_price), (string)(r.name ?? ""));
        }
        return map;
    }

    // Deletes generated-range item_template rows that no lootifier tracks (leftovers
    // from a crashed run). Never touches tracked quest/ARPG/crafting rows.
    [HttpPost]
    public async Task<IActionResult> SweepOrphans()
    {
        using var mangosConn = _db.Mangos();
        int orphans = await SweepOrphansInternal(mangosConn);
        return Json(new { success = true, orphans });
    }

    private async Task<int> SweepOrphansInternal(MySqlConnector.MySqlConnection mangosConn)
    {
        return await mangosConn.ExecuteAsync(@"
            DELETE it FROM item_template it
            LEFT JOIN vmangos_admin.lootifier_generated_items g ON g.generated_entry = it.entry
            WHERE it.entry >= @Start AND g.id IS NULL",
            new { Start = LOOTIFIER_ID_START });
    }

    [HttpGet]
    public async Task<IActionResult> Status()
    {
        using var adminConn = _db.Admin();
        using var mangosConn = _db.Mangos();
        int total = await adminConn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM lootifier_generated_items WHERE creature_entry = @CE",
            new { CE = CRAFT_SENTINEL_CREATURE });
        int baseItems = await adminConn.ExecuteScalarAsync<int>(
            "SELECT COUNT(DISTINCT base_entry) FROM lootifier_generated_items WHERE creature_entry = @CE",
            new { CE = CRAFT_SENTINEL_CREATURE });
        int orphans = await mangosConn.ExecuteScalarAsync<int>(@"
            SELECT COUNT(*) FROM item_template it
            LEFT JOIN vmangos_admin.lootifier_generated_items g ON g.generated_entry = it.entry
            WHERE it.entry >= @Start AND g.id IS NULL", new { Start = LOOTIFIER_ID_START });

        return Json(new { totalVariants = total, baseItems, orphans });
    }

    [HttpGet]
    public async Task<IActionResult> Browse(string? q, int page = 1, int pageSize = 40)
    {
        using var adminConn = _db.Admin();
        using var mangosConn = _db.Mangos();

        var grouped = (await adminConn.QueryAsync(@"
            SELECT base_entry, COUNT(*) AS variantCount
            FROM lootifier_generated_items
            WHERE creature_entry = @CE
            GROUP BY base_entry
            ORDER BY base_entry",
            new { CE = CRAFT_SENTINEL_CREATURE })).ToList();

        var baseIds = grouped.Select(g => (int)g.base_entry).ToList();
        var meta = await FetchItemMeta(mangosConn, baseIds);

        var q2 = (q ?? "").Trim().ToLowerInvariant();
        var all = grouped.Select(g =>
        {
            int be = (int)g.base_entry;
            meta.TryGetValue(be, out var m);
            return new
            {
                baseEntry = be,
                name = m.name ?? $"#{be}",
                quality = m.name != null ? m.quality : 1,
                iconPath = m.name != null ? _dbc.GetItemIconPath((uint)m.displayId) : "",
                variantCount = (int)(long)g.variantCount
            };
        })
        .Where(x => q2.Length == 0 || x.name.ToLowerInvariant().Contains(q2) || x.baseEntry.ToString().Contains(q2))
        .ToList();

        int totalCount = all.Count;
        var paged = all.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return Json(new { total = totalCount, page, pageSize, items = paged });
    }

    [HttpGet]
    public async Task<IActionResult> ItemVariants(int baseEntry)
    {
        using var adminConn = _db.Admin();
        using var mangosConn = _db.Mangos();

        var tracked = (await adminConn.QueryAsync(@"
            SELECT generated_entry, budget_pct, tier_name
            FROM lootifier_generated_items
            WHERE creature_entry = @CE AND base_entry = @B
            ORDER BY budget_pct ASC",
            new { CE = CRAFT_SENTINEL_CREATURE, B = baseEntry })).ToList();

        var baseRow = await mangosConn.QueryFirstOrDefaultAsync<dynamic>(@"
            SELECT quality FROM item_template
            WHERE entry = @E AND patch = (SELECT MAX(patch) FROM item_template it2 WHERE it2.entry = @E)",
            new { E = baseEntry });
        int baseQuality = baseRow != null ? (int)baseRow.quality : 2;

        var genIds = tracked.Select(t => (int)t.generated_entry).ToList();
        var itemRows = new Dictionary<int, dynamic>();
        if (genIds.Count > 0)
        {
            foreach (var chunk in genIds.Chunk(500))
            {
                var rows = await mangosConn.QueryAsync(@"
                    SELECT entry, name, quality, display_id,
                           stat_type1, stat_value1, stat_type2, stat_value2, stat_type3, stat_value3,
                           stat_type4, stat_value4, stat_type5, stat_value5, stat_type6, stat_value6,
                           stat_type7, stat_value7, stat_type8, stat_value8, stat_type9, stat_value9,
                           stat_type10, stat_value10
                    FROM item_template
                    WHERE entry IN @Ids AND patch = (SELECT MAX(patch) FROM item_template it2 WHERE it2.entry = item_template.entry)",
                    new { Ids = chunk });
                foreach (var r in rows) itemRows[(int)r.entry] = r;
            }
        }

        var odds = ComputeAwardOdds(tracked, baseQuality);

        var outVariants = new List<object>();
        foreach (var t in tracked)
        {
            int gen = (int)t.generated_entry;
            if (!itemRows.TryGetValue(gen, out var r)) continue;

            var stats = new List<object>();
            for (int i = 1; i <= 10; i++)
            {
                int st = GetPropInt(r, $"stat_type{i}");
                int sv = GetPropInt(r, $"stat_value{i}");
                if (st > 0 && sv != 0)
                    stats.Add(new { statType = st, statValue = sv, name = STAT_NAMES.GetValueOrDefault(st, $"Type{st}") });
            }

            outVariants.Add(new
            {
                entry = gen,
                name = (string)r.name,
                quality = (int)r.quality,
                iconPath = _dbc.GetItemIconPath((uint)(int)r.display_id),
                boostPct = (float)t.budget_pct,
                tier = (string)(t.tier_name ?? ""),
                awardPct = odds.GetValueOrDefault(gen, 0f),
                stats
            });
        }

        return Json(new { baseEntry, baseQuality, basePct = 20f, variants = outVariants });
    }

    // ══════════════════════════════════════════════════════════════════════
    //  GENERATION
    // ══════════════════════════════════════════════════════════════════════

    private List<VariantData> GenerateVariants(dynamic baseItem, dynamic analysis, CraftingRulesetDto ruleset)
    {
        var rng = new Random();

        float baseWeighted = (float)analysis.weightedBudget;
        int[] presentTypes = (int[])analysis.presentStatTypes;
        string family = (string)analysis.detectedFamily;
        bool hasStats = (int)analysis.totalStats > 0;
        int baseQuality = (int)analysis.itemQuality;
        bool whiteBase = baseQuality <= 1;
        var baseStats = ((List<object>)analysis.stats);

        if (baseWeighted <= 0f)
            baseWeighted = EstimateBudgetFromItemLevel(GetPropInt(baseItem, "item_level"));

        var eligible = new HashSet<int>(presentTypes);
        foreach (var s in STAT_FAMILIES.GetValueOrDefault(family, STAT_FAMILIES["hybrid"])) eligible.Add(s);
        var eligibleList = eligible.ToList();
        if (eligibleList.Count == 0) eligibleList = STAT_FAMILIES["hybrid"].ToList();

        var bands = (ruleset.bands != null && ruleset.bands.Length > 0)
            ? ruleset.bands.ToList()
            : DefaultBands(ruleset.includeLegendaryBand);

        // White caps at green/blue: drop the legendary (of the Gods) band.
        if (whiteBase)
            bands = bands.Where(b =>
            {
                var l = (b.label ?? "").ToLowerInvariant();
                return !(l.Contains("gods") || l.Contains("legend"));
            }).ToList();

        var fingerprints = new HashSet<string>();
        var variants = new List<VariantData>();

        foreach (var band in bands)
        {
            int slots = Math.Max(0, band.slots);
            for (int s = 0; s < slots; s++)
            {
                VariantData cand = RollBandVariant(rng, baseItem, hasStats, baseStats, baseWeighted, eligibleList, band, ruleset);

                var fp = BuildVariantFingerprint(cand);
                if (fingerprints.Contains(fp))
                {
                    bool found = false;
                    for (int retry = 0; retry < 10; retry++)
                    {
                        cand = RollBandVariant(rng, baseItem, hasStats, baseStats, baseWeighted, eligibleList, band, ruleset);
                        fp = BuildVariantFingerprint(cand);
                        if (!fingerprints.Contains(fp)) { found = true; break; }
                    }
                    if (!found) continue;
                }

                fingerprints.Add(fp);
                variants.Add(cand);
            }
        }

        return variants.OrderBy(v => v.budgetPct).ToList();
    }

    private VariantData RollBandVariant(Random rng, dynamic baseItem, bool hasStats, List<object> baseStats,
        float baseWeighted, List<int> eligibleList, CraftingBandDto band, CraftingRulesetDto ruleset)
    {
        float boostPct = band.minBoostPct + (float)rng.NextDouble() * (band.maxBoostPct - band.minBoostPct);

        List<StatRoll> stats = hasStats
            ? RollStatsAdditive(rng, baseStats, baseWeighted, boostPct, eligibleList, ruleset)
            : RollStatsMinted(rng, GetPropInt(baseItem, "item_level"), CanonicalTier(band.label, boostPct), eligibleList);

        string name = ApplyTierName((string)baseItem.name, band.label, band.position);
        return new VariantData { name = name, budgetPct = boostPct, tierLabel = band.label, tierPosition = band.position, stats = stats };
    }

    // Statted bases: preserve all base lines, layer a boostPct% bonus on top, split
    // between bumps and new affixes by existingBumpBias. Bump-only fallback.
    private List<StatRoll> RollStatsAdditive(Random rng, List<object> baseStats, float baseWeighted,
        float boostPct, List<int> eligibleList, CraftingRulesetDto ruleset)
    {
        var lines = new Dictionary<int, int>();
        foreach (var s in baseStats)
        {
            int st = (int)((dynamic)s).statType;
            int sv = (int)((dynamic)s).statValue;
            if (st > 0 && sv != 0) lines[st] = sv;
        }

        float delta = Math.Max(MIN_DELTA_BUDGET, baseWeighted * (boostPct / 100f));

        int slotRoom = Math.Max(0, 10 - lines.Count);
        var newCandidates = eligibleList.Where(t => !lines.ContainsKey(t)).ToList();
        bool canAddNew = ruleset.allowNewAffixes && slotRoom > 0 && newCandidates.Count > 0;

        float bias = Math.Clamp(ruleset.existingBumpBias, 0f, 1f);
        float split = (float)Math.Clamp(bias + (rng.NextDouble() * 0.4 - 0.2), 0.0, 1.0);
        if (lines.Count == 0) split = 0f;
        if (!canAddNew) split = 1f;

        float existingPortion = delta * split;
        float newPortion = delta - existingPortion;

        if (existingPortion > 0f && lines.Count > 0)
        {
            var keys = lines.Keys.ToList();
            float totalW = keys.Sum(k => DEFAULT_STAT_WEIGHTS.GetValueOrDefault(k, 1.0f));
            foreach (var k in keys)
            {
                float w = DEFAULT_STAT_WEIGHTS.GetValueOrDefault(k, 1.0f);
                float share = existingPortion * (w / totalW);
                int add = (int)Math.Round(share / w);
                if (add > 0) lines[k] += add;
            }
        }

        if (canAddNew && newPortion > 0f)
        {
            int maxNew = Math.Min(Math.Max(1, ruleset.maxAffixCountChange), Math.Min(slotRoom, newCandidates.Count));
            int newCount = 1 + rng.Next(maxNew);
            var picks = newCandidates.OrderBy(_ => rng.Next()).Take(newCount).ToList();

            float totalW = picks.Sum(k => DEFAULT_STAT_WEIGHTS.GetValueOrDefault(k, 1.0f));
            foreach (var k in picks)
            {
                float w = DEFAULT_STAT_WEIGHTS.GetValueOrDefault(k, 1.0f);
                float share = newPortion * (w / totalW);
                int val = Math.Max(1, (int)Math.Round(share / w));
                lines[k] = lines.GetValueOrDefault(k, 0) + val;
            }
        }
        else if (existingPortion < delta && lines.Count > 0)
        {
            var keys = lines.Keys.ToList();
            float totalW = keys.Sum(k => DEFAULT_STAT_WEIGHTS.GetValueOrDefault(k, 1.0f));
            float leftover = delta - existingPortion;
            foreach (var k in keys)
            {
                float w = DEFAULT_STAT_WEIGHTS.GetValueOrDefault(k, 1.0f);
                int add = (int)Math.Round(leftover * (w / totalW) / w);
                if (add > 0) lines[k] += add;
            }
        }

        return lines.Select(kv => new StatRoll
        {
            statType = kv.Key,
            statValue = kv.Value,
            name = STAT_NAMES.GetValueOrDefault(kv.Key, $"Type{kv.Key}")
        }).ToList();
    }

    // Stat-less white: mint a modest set sized from item level, green vs blue by band.
    // A level 2-4 white lands at ~+1 green / ~+2 blue.
    private List<StatRoll> RollStatsMinted(Random rng, int itemLevel, string tier, List<int> eligibleList)
    {
        if (itemLevel <= 0) itemLevel = 5;

        bool blueTier = (tier == "glory" || tier == "gods");  // tier-driven, not boost
        float factor = blueTier ? MINT_BLUE_FACTOR : MINT_GREEN_FACTOR;
        float budget = Math.Max(1f, itemLevel * factor);

        int count = (budget >= 3f && rng.NextDouble() < 0.5) ? 2 : 1;
        count = Math.Min(count, Math.Max(1, eligibleList.Count));
        var picks = eligibleList.OrderBy(_ => rng.Next()).Take(count).ToList();
        if (picks.Count == 0) picks.Add(7);

        float totalW = picks.Sum(k => DEFAULT_STAT_WEIGHTS.GetValueOrDefault(k, 1.0f));
        var outStats = new List<StatRoll>();
        foreach (var k in picks)
        {
            float w = DEFAULT_STAT_WEIGHTS.GetValueOrDefault(k, 1.0f);
            int val = Math.Max(1, (int)Math.Round(budget * (w / totalW) / w));
            outStats.Add(new StatRoll { statType = k, statValue = val, name = STAT_NAMES.GetValueOrDefault(k, $"Type{k}") });
        }
        return outStats;
    }

    // ── Award-odds mirror of the C++ roll (keep in sync) ───────────────────
    private const float CRAFT_P_BASE = 0.20f;
    private const float CRAFT_P_IMPROVED = 0.50f;
    private const float CRAFT_P_TOPBAND = 0.30f;
    private const float CRAFT_POWER_SHARE = 0.60f;

    private static float LegendaryChanceForQuality(int q) => q switch
    {
        2 => 0.001f,
        3 => 0.03f,
        4 => 0.15f,
        _ => q >= 5 ? 0.20f : 0.0f
    };

    // Canonical tier for a band. Recognized names (Improved/Power/Glory/Gods) map
    // by NAME, so their boost % is pure magnitude and any range is legal. Only an
    // unrecognized custom name falls back to the boost bucket. This token is what
    // gets stored in tier_name and what the C++ store buckets by.
    private static string CanonicalTier(string label, float boostPct)
    {
        var l = (label ?? "").ToLowerInvariant();
        if (l.Contains("god") || l.Contains("legend")) return "gods";
        if (l.Contains("glory")) return "glory";
        if (l.Contains("power")) return "power";
        if (l.Contains("improv")) return "improved";
        if (boostPct >= 40f) return "gods";
        if (boostPct >= 30f) return "glory";
        if (boostPct >= 20f) return "power";
        return "improved";
    }

    private static int TierIndex(string tier) => tier switch { "gods" => 3, "glory" => 2, "power" => 1, _ => 0 };

    // Variant colour anchored at the BASE quality. Improved/Power keep the base
    // colour; of Glory is +1 (capped at purple, so only Gods reaches orange); of the
    // Gods is +2 (the legendary). White floors to green — white + stats can't stay
    // white. Result per base:
    //   white  → green / green / blue           (Gods band is dropped for white)
    //   green  → green / green / blue / purple
    //   blue   → blue  / blue  / purple / orange
    //   purple → purple/ purple/ purple / orange
    private static int VariantQuality(string tier, int baseQuality)
    {
        int b = baseQuality <= 1 ? 2 : baseQuality;      // white + stats floors at green
        int q;
        if (tier == "gods") q = Math.Min(b + 2, 5);      // legendary
        else if (tier == "glory") q = Math.Min(b + 1, 4);// +1, capped at purple
        else q = b;                                      // improved / power keep base colour
        return Math.Clamp(q, 2, 5);
    }

    private static int BandIndexForBudget(float budgetPct)
    {
        if (budgetPct >= 40f) return 3;
        if (budgetPct >= 30f) return 2;
        if (budgetPct >= 20f) return 1;
        return 0;
    }

    private Dictionary<int, float> ComputeAwardOdds(List<dynamic> tracked, int baseQuality)
    {
        var byBand = new List<int>[4];
        for (int i = 0; i < 4; i++) byBand[i] = new List<int>();
        foreach (var t in tracked)
            byBand[TierIndex(CanonicalTier((string)(t.tier_name ?? ""), (float)t.budget_pct))].Add((int)t.generated_entry);

        float leg = Math.Min(CRAFT_P_TOPBAND, LegendaryChanceForQuality(baseQuality));
        float rest = CRAFT_P_TOPBAND - leg;
        float power = rest * CRAFT_POWER_SHARE;
        float glory = rest - power;
        float[] bandProb = { CRAFT_P_IMPROVED, power, glory, leg };

        for (int i = 3; i >= 1; i--)
        {
            if (byBand[i].Count == 0 && bandProb[i] > 0f)
            {
                int t = i - 1;
                while (t > 0 && byBand[t].Count == 0) t--;
                bandProb[t] += bandProb[i];
                bandProb[i] = 0f;
            }
        }

        var odds = new Dictionary<int, float>();
        for (int i = 0; i < 4; i++)
        {
            if (byBand[i].Count == 0) continue;
            float per = bandProb[i] / byBand[i].Count;
            foreach (var gen in byBand[i]) odds[gen] = per * 100f;
        }
        return odds;
    }

    // ══════════════════════════════════════════════════════════════════════
    //  DB WRITE
    // ══════════════════════════════════════════════════════════════════════

    private async Task InsertCraftingVariant(MySqlConnector.MySqlConnection conn, List<string> columns,
        dynamic baseItem, int newEntry, CommitRoll roll, int baseQuality, float goldScalePct = 100f,
        float? goldBumpPct = null, float legendaryGoldBumpPct = 400f)
    {
        int baseEntry = (int)baseItem.entry;
        int basePatch = GetPropInt(baseItem, "patch");

        var statTypes = new int[10];
        var statValues = new int[10];
        for (int i = 0; i < Math.Min(roll.stats.Length, 10); i++)
        {
            statTypes[i] = roll.stats[i].statType;
            statValues[i] = roll.stats[i].statValue;
        }

        string tierLabel = roll.tierLabel ?? "";
        string tierPosition = roll.tierPosition ?? "suffix";

        var selectParts = new List<string>();
        foreach (var col in columns)
        {
            if (col == "entry")
                selectParts.Add($"{newEntry} AS `entry`");
            else if (col == "name")
            {
                if (tierPosition == "prefix" && !string.IsNullOrEmpty(tierLabel))
                    selectParts.Add("CONCAT(@TierLabel, ' ', name) AS `name`");
                else if (!string.IsNullOrEmpty(tierLabel))
                    selectParts.Add("CONCAT(name, ' ', @TierLabel) AS `name`");
                else
                    selectParts.Add("`name`");
            }
            else if (col.StartsWith("stat_type") && col.Length <= 11)
            {
                int idx = int.Parse(col.Replace("stat_type", "")) - 1;
                selectParts.Add($"@ST{idx} AS `{col}`");
            }
            else if (col.StartsWith("stat_value") && col.Length <= 12)
            {
                int idx = int.Parse(col.Replace("stat_value", "")) - 1;
                selectParts.Add($"@SV{idx} AS `{col}`");
            }
            else
                selectParts.Add($"`{col}`");
        }

        var sql = $"INSERT IGNORE INTO item_template SELECT {string.Join(", ", selectParts)} FROM item_template WHERE entry = @BaseEntry AND patch = @BasePatch";

        await conn.ExecuteAsync(sql, new
        {
            BaseEntry = baseEntry,
            BasePatch = basePatch,
            TierLabel = tierLabel,
            ST0 = statTypes[0],
            SV0 = statValues[0],
            ST1 = statTypes[1],
            SV1 = statValues[1],
            ST2 = statTypes[2],
            SV2 = statValues[2],
            ST3 = statTypes[3],
            SV3 = statValues[3],
            ST4 = statTypes[4],
            SV4 = statValues[4],
            ST5 = statTypes[5],
            SV5 = statValues[5],
            ST6 = statTypes[6],
            SV6 = statValues[6],
            ST7 = statTypes[7],
            SV7 = statValues[7],
            ST8 = statTypes[8],
            SV8 = statValues[8],
            ST9 = statTypes[9],
            SV9 = statValues[9]
        });

        string tier = CanonicalTier(tierLabel, roll.budgetPct);
        int variantQuality = VariantQuality(tier, baseQuality);

        bool isLegendary = variantQuality >= 5;

        // Legendary -> explicit legendary bump; band variants -> per-band Gold +%
        // when configured, legacy boost curve otherwise. Master-scaled.
        float goldMult;
        if (isLegendary) goldMult = 1f + legendaryGoldBumpPct / 100f;
        else if (goldBumpPct.HasValue) goldMult = 1f + goldBumpPct.Value / 100f;
        else goldMult = GetGoldMultiplier(roll.budgetPct);
        goldMult = ScaleGoldMult(goldMult, goldScalePct);

        if (goldMult > 1.05f)
            await conn.ExecuteAsync(
                "UPDATE item_template SET buy_price = ROUND(buy_price * @Mult), sell_price = ROUND(sell_price * @Mult) WHERE entry = @Entry",
                new { Mult = goldMult, Entry = newEntry });

        if (isLegendary)
            await conn.ExecuteAsync(
                "UPDATE item_template SET quality = @Q, disenchant_id = 0 WHERE entry = @Entry",
                new { Q = variantQuality, Entry = newEntry });
        else
            await conn.ExecuteAsync(
                "UPDATE item_template SET quality = @Q WHERE entry = @Entry",
                new { Q = variantQuality, Entry = newEntry });
    }

    private float GetGoldMultiplier(float boostPct)
    {
        float t = Math.Clamp(boostPct / 60f, 0f, 1f);
        return 1.0f + t * 0.8f;
    }

    // Scales only the MARKUP portion of a gold multiplier, so the tier-graded
    // curve keeps its shape: 100% = stock curve, 0% = prices untouched,
    // 200% = double markup. Result never drops below 1× (base price).
    private static float ScaleGoldMult(float mult, float scalePct) =>
        1f + (mult - 1f) * (Math.Max(0f, scalePct) / 100f);

    // Finds the Gold +% for a variant. Tracking stores CANONICAL tier names
    // ("gods"/"glory"/"power"/"improved"), band labels are display names
    // ("of the Gods"), so match exact label first, then canonical tier, then
    // boost-range containment. Null = no explicit bump -> legacy curve.
    private static float? ResolveBandGoldBump(CraftingBandDto[]? bands, string? tierLabel, float budgetPct)
    {
        if (bands == null || bands.Length == 0) return null;
        if (!string.IsNullOrEmpty(tierLabel))
        {
            foreach (var b in bands)
                if (string.Equals(b.label, tierLabel, StringComparison.OrdinalIgnoreCase))
                    return b.goldBumpPct;
            string canon = CanonicalTier(tierLabel, budgetPct);
            foreach (var b in bands)
                if (CanonicalTier(b.label, (b.minBoostPct + b.maxBoostPct) / 2f) == canon)
                    return b.goldBumpPct;
        }
        foreach (var b in bands)
            if (budgetPct >= b.minBoostPct && budgetPct <= b.maxBoostPct)
                return b.goldBumpPct;
        return null;
    }

    // ══════════════════════════════════════════════════════════════════════
    //  TRACKING + SCHEMA HELPERS
    // ══════════════════════════════════════════════════════════════════════

    private async Task<int> PurgeCraftingVariantsForBase(MySqlConnector.MySqlConnection mangosConn,
        MySqlConnector.MySqlConnection adminConn, int baseEntry)
    {
        var genIds = (await adminConn.QueryAsync<int>(
            "SELECT generated_entry FROM lootifier_generated_items WHERE creature_entry = @CE AND base_entry = @B",
            new { CE = CRAFT_SENTINEL_CREATURE, B = baseEntry })).ToList();

        foreach (var chunk in genIds.Chunk(500))
            await mangosConn.ExecuteAsync("DELETE FROM item_template WHERE entry IN @Ids", new { Ids = chunk });

        return await adminConn.ExecuteAsync(
            "DELETE FROM lootifier_generated_items WHERE creature_entry = @CE AND base_entry = @B",
            new { CE = CRAFT_SENTINEL_CREATURE, B = baseEntry });
    }

    // Read-only guard for endpoints that shouldn't create tables (Revalue) —
    // same helper as the Quest Lootifier's.
    private async Task<bool> TableExists(MySqlConnector.MySqlConnection conn, string tableName)
    {
        return await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @T",
            new { T = tableName }) > 0;
    }

    private async Task EnsureTrackingTables(MySqlConnector.MySqlConnection adminConn)
    {
        await adminConn.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS lootifier_generated_items (
                id INT AUTO_INCREMENT PRIMARY KEY,
                generated_entry INT NOT NULL,
                base_entry INT NOT NULL,
                creature_entry INT NOT NULL,
                budget_pct FLOAT DEFAULT 0,
                tier_name VARCHAR(64) DEFAULT '',
                created_at DATETIME NOT NULL,
                INDEX idx_creature (creature_entry),
                INDEX idx_generated (generated_entry),
                INDEX idx_base (base_entry)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");
    }

    private async Task FlushTrackingItems(MySqlConnector.MySqlConnection adminConn,
        List<(int genEntry, int baseEntry, int creatureEntry, float budgetPct, string tierName)> rows)
    {
        if (rows.Count == 0) return;
        var sb = new System.Text.StringBuilder();
        sb.Append("INSERT INTO lootifier_generated_items (generated_entry, base_entry, creature_entry, budget_pct, tier_name, created_at) VALUES ");
        var pars = new DynamicParameters();
        for (int i = 0; i < rows.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append($"(@g{i}, @b{i}, @c{i}, @p{i}, @t{i}, NOW())");
            pars.Add($"g{i}", rows[i].genEntry);
            pars.Add($"b{i}", rows[i].baseEntry);
            pars.Add($"c{i}", rows[i].creatureEntry);
            pars.Add($"p{i}", rows[i].budgetPct);
            pars.Add($"t{i}", rows[i].tierName);
        }
        await adminConn.ExecuteAsync(sb.ToString(), pars);
    }

    private async Task<int> GetNextLootifierId(MySqlConnector.MySqlConnection adminConn)
    {
        int fromTracking = LOOTIFIER_ID_START;
        var maxTracked = await adminConn.ExecuteScalarAsync<int?>("SELECT MAX(generated_entry) FROM lootifier_generated_items");
        if (maxTracked.HasValue) fromTracking = maxTracked.Value + 1;

        using var mangosConn = _db.Mangos();
        var maxInItems = await mangosConn.ExecuteScalarAsync<int?>(
            "SELECT MAX(entry) FROM item_template WHERE entry >= @Start", new { Start = LOOTIFIER_ID_START });
        int fromItems = maxInItems.HasValue ? maxInItems.Value + 1 : LOOTIFIER_ID_START;
        return Math.Max(fromTracking, fromItems);
    }

    private async Task<List<string>> GetItemColumns(MySqlConnector.MySqlConnection conn)
    {
        var cols = await conn.QueryAsync<string>(@"
            SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'item_template'
            ORDER BY ORDINAL_POSITION");
        return cols.ToList();
    }

    // entry -> (name, quality, class, inventory_type, display_id), max-patch resolved.
    private async Task<Dictionary<int, (string name, int quality, int cls, int inv, int displayId, int itemLevel)>> FetchItemMeta(
        MySqlConnector.MySqlConnection conn, List<int> itemIds)
    {
        var map = new Dictionary<int, (string, int, int, int, int, int)>();
        foreach (var chunk in itemIds.Distinct().Chunk(500))
        {
            var rows = await conn.QueryAsync(@"
                SELECT entry, name, quality, class, inventory_type, display_id, item_level
                FROM item_template
                WHERE entry IN @Ids AND patch = (SELECT MAX(patch) FROM item_template it2 WHERE it2.entry = item_template.entry)",
                new { Ids = chunk });
            foreach (var r in rows)
                map[(int)r.entry] = ((string)r.name, (int)r.quality, (int)r.@class, (int)r.inventory_type, (int)r.display_id, (int)r.item_level);
        }
        return map;
    }

    // ══════════════════════════════════════════════════════════════════════
    //  ANALYSIS + SMALL HELPERS
    // ══════════════════════════════════════════════════════════════════════

    // Equippable gear = weapon/armor that occupies an inventory slot. Includes
    // shirts (inv 4) and tabards (inv 19); excludes non-equip stones/oils/reagents
    // (inv 0) and bags/quivers (other classes).
    private static bool IsEquippableGear(int itemClass, int inventoryType)
        => (itemClass == ITEM_CLASS_WEAPON || itemClass == ITEM_CLASS_ARMOR) && inventoryType > 0;

    private dynamic AnalyzeItemStats(dynamic item)
    {
        var stats = new List<object>();
        int totalStats = 0;
        float weightedBudget = 0;
        var presentTypes = new HashSet<int>();

        for (int i = 1; i <= 10; i++)
        {
            int statType = GetPropInt(item, $"stat_type{i}");
            int statValue = GetPropInt(item, $"stat_value{i}");
            if (statType > 0 && statValue != 0)
            {
                string name = STAT_NAMES.GetValueOrDefault(statType, $"Type{statType}");
                float weight = DEFAULT_STAT_WEIGHTS.GetValueOrDefault(statType, 1.0f);
                stats.Add(new { slot = i, statType, statValue, name, weight, weightedCost = statValue * weight });
                totalStats += Math.Abs(statValue);
                weightedBudget += Math.Abs(statValue) * weight;
                presentTypes.Add(statType);
            }
        }

        string family = "hybrid";
        if (presentTypes.Count > 0 && presentTypes.IsSubsetOf(STAT_FAMILIES["physical"])) family = "physical";
        else if (presentTypes.Count > 0 && presentTypes.IsSubsetOf(STAT_FAMILIES["caster"])) family = "caster";

        int itemClass = GetPropInt(item, "class");
        int invType = GetPropInt(item, "inventory_type");
        bool isEquippable = IsEquippableGear(itemClass, invType);

        var spellEffects = new List<SpellEffectInfo>();
        for (int i = 1; i <= 5; i++)
        {
            int spellId = GetPropInt(item, $"spellid_{i}");
            int spellTrigger = GetPropInt(item, $"spelltrigger_{i}");
            if (spellId > 0)
            {
                string triggerName = spellTrigger switch
                {
                    SPELLTRIGGER_USE => "Use",
                    SPELLTRIGGER_EQUIP => "Equip",
                    SPELLTRIGGER_CHANCE_ON_HIT => "Chance on Hit",
                    _ => $"Trigger {spellTrigger}"
                };
                spellEffects.Add(new SpellEffectInfo { slot = i, spellId = spellId, triggerType = spellTrigger, triggerName = triggerName });
            }
        }

        return new
        {
            stats,
            totalStats,
            weightedBudget,
            detectedFamily = family,
            presentStatTypes = presentTypes.ToArray(),
            spellEffects,
            hasSpellEffects = spellEffects.Count > 0,
            isEquippable,
            itemQuality = GetPropInt(item, "quality")
        };
    }

    // Eligibility gate. Stats are OPTIONAL at every quality: a stat-less base has
    // no budget to layer onto, so the engine derives one from item_level instead
    // (EstimateBudgetFromItemLevel / RollStatsMinted) — and that path branches on
    // hasStats, never on quality. The old rule (`quality == 1 ? true : hasStats`)
    // let stat-less WHITES through but silently dropped stat-less GREEN+ items,
    // even though the engine handles them identically. That skipped a whole class
    // of vanilla quest rewards: pure-DPS weapons with no stat lines
    // (Vanquisher's Sword and friends). Now the only bars are: it must be gear,
    // and it must not be grey.
    private bool IsLootifiable(dynamic analysis)
    {
        bool equippable = (bool)analysis.isEquippable;  // class 2/4 with inventory_type > 0
        int quality = (int)analysis.itemQuality;
        if (!equippable) return false;
        if (quality <= 0) return false;                 // grey excluded
        return true;                                    // stats optional — minted from item_level if absent
    }

    private int GetPropInt(dynamic obj, string name)
    {
        var dict = obj as IDictionary<string, object>;
        if (dict != null && dict.TryGetValue(name, out var val))
            return val == null ? 0 : Convert.ToInt32(val);
        return 0;
    }

    private string ApplyTierName(string baseName, string label, string position)
    {
        if (string.IsNullOrEmpty(label)) return baseName;
        return position == "prefix" ? $"{label} {baseName}" : $"{baseName} {label}";
    }

    private string BuildVariantFingerprint(VariantData v)
    {
        var parts = v.stats.Select(s => $"{s.statType}:{s.statValue}").OrderBy(x => x);
        return v.tierLabel + "|" + string.Join("|", parts);
    }

    private float EstimateBudgetFromItemLevel(int itemLevel) => Math.Max(5f, itemLevel * 0.7f);

    private CommitRoll VariantToCommitRoll(VariantData v) => new CommitRoll
    {
        budgetPct = v.budgetPct,
        tierLabel = v.tierLabel,
        tierPosition = v.tierPosition,
        stats = v.stats.Select(s => new CommitStat { statType = s.statType, statValue = s.statValue }).ToArray()
    };

    private List<object> VariantsToJson(List<VariantData> variants, int baseQuality)
    {
        var byBand = new List<int>[4];
        for (int i = 0; i < 4; i++) byBand[i] = new List<int>();
        for (int i = 0; i < variants.Count; i++) byBand[TierIndex(CanonicalTier(variants[i].tierLabel, variants[i].budgetPct))].Add(i);

        float leg = Math.Min(CRAFT_P_TOPBAND, LegendaryChanceForQuality(baseQuality));
        float rest = CRAFT_P_TOPBAND - leg;
        float power = rest * CRAFT_POWER_SHARE;
        float glory = rest - power;
        float[] bandProb = { CRAFT_P_IMPROVED, power, glory, leg };
        for (int i = 3; i >= 1; i--)
        {
            if (byBand[i].Count == 0 && bandProb[i] > 0f)
            {
                int t = i - 1;
                while (t > 0 && byBand[t].Count == 0) t--;
                bandProb[t] += bandProb[i];
                bandProb[i] = 0f;
            }
        }
        var awardByIndex = new float[variants.Count];
        for (int i = 0; i < 4; i++)
        {
            if (byBand[i].Count == 0) continue;
            float per = bandProb[i] / byBand[i].Count;
            foreach (var idx in byBand[i]) awardByIndex[idx] = per * 100f;
        }

        var outList = new List<object>();
        for (int i = 0; i < variants.Count; i++)
        {
            var v = variants[i];
            outList.Add(new
            {
                name = v.name,
                boostPct = v.budgetPct,
                tier = v.tierLabel,
                quality = VariantQuality(CanonicalTier(v.tierLabel, v.budgetPct), baseQuality),
                awardPct = awardByIndex[i],
                stats = v.stats.Select(s => new { s.statType, s.statValue, s.name })
            });
        }
        return outList;
    }

    // ══════════════════════════════════════════════════════════════════════
    //  DEFAULTS
    // ══════════════════════════════════════════════════════════════════════

    private CraftingRulesetDto DefaultRuleset() => new CraftingRulesetDto
    {
        variantsPerItem = 10,
        allowNewAffixes = true,
        maxAffixCountChange = 1,
        existingBumpBias = 0.5f,
        includeLegendaryBand = true,
        goldValueScalePct = 100f,
        bands = DefaultBands(true).ToArray()
    };

    private List<CraftingBandDto> DefaultBands(bool includeLegendary)
    {
        var bands = new List<CraftingBandDto>
        {
            new() { label = "Improved", position = "prefix", minBoostPct = 10f, maxBoostPct = 20f, slots = 5, goldBumpPct = 25f },
            new() { label = "of Power", position = "suffix", minBoostPct = 20f, maxBoostPct = 30f, slots = 2, goldBumpPct = 50f },
            new() { label = "of Glory", position = "suffix", minBoostPct = 30f, maxBoostPct = 40f, slots = 2, goldBumpPct = 100f },
        };
        if (includeLegendary)
            bands.Add(new() { label = "of the Gods", position = "suffix", minBoostPct = 40f, maxBoostPct = 60f, slots = 1, goldBumpPct = 200f });
        return bands;
    }
}

// ══════════════════════════════════════════════════════════════════════════
//  Crafting-specific customization DTOs
// ══════════════════════════════════════════════════════════════════════════

public class CraftingRulesetDto
{
    public int variantsPerItem { get; set; } = 10;
    public bool allowNewAffixes { get; set; } = true;
    public int maxAffixCountChange { get; set; } = 1;
    public float existingBumpBias { get; set; } = 0.5f;
    public bool includeLegendaryBand { get; set; } = true;
    public float goldValueScalePct { get; set; } = 100f;  // master scale on all gold bumps: 100 = as entered, 0 = prices untouched, 200 = double
    public float legendaryGoldBumpPct { get; set; } = 400f; // legendary (quality 5) price bump above base (%); ~ the old curve x3 stock stack
    public CraftingBandDto[]? bands { get; set; }
}

public class CraftingBandDto
{
    public string label { get; set; } = "";
    public string position { get; set; } = "suffix";
    public float minBoostPct { get; set; }
    public float maxBoostPct { get; set; }
    public int slots { get; set; } = 1;
    public float? goldBumpPct { get; set; }   // price bump above base (%); null = legacy boost curve
}

public class CraftingGenerateRequest
{
    public int[] itemEntries { get; set; } = Array.Empty<int>();
    public CraftingRulesetDto? ruleset { get; set; }
}

public class CraftingProfessionRequest
{
    public int skillLineId { get; set; }
    public CraftingRulesetDto? ruleset { get; set; }
}

public class CraftingRollbackRequest
{
    public int baseEntry { get; set; }
}

public class CraftingTierBumpDto
{
    public string tier { get; set; } = "";      // tier_name exactly as stored in lootifier_generated_items
    public float? goldBumpPct { get; set; }     // null / omitted = leave this tier's prices ALONE
}

public class CraftingRevalueRequest
{
    public CraftingTierBumpDto[]? tiers { get; set; }
}