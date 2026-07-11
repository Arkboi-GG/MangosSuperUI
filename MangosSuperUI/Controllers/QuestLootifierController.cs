using Microsoft.AspNetCore.Mvc;
using MangosSuperUI.Models;
using MangosSuperUI.Services;
using Dapper;
using System.Text.Json;
using MySqlConnector;

namespace MangosSuperUI.Controllers;

// ══════════════════════════════════════════════════════════════════════════
//  QUEST LOOTIFIER
//
//  Generates N stat-rerolled variants for QUEST REWARD items (both guaranteed
//  RewItemId1-4 and choice RewChoiceItemId1-6 on quest_template), so a C++
//  hook in Player::RewardQuest can swap the plain base for a rolled variant at
//  award time. Unlike the loot Lootifier there are NO pools, references, or
//  drop-chance math — quest rewards aren't loot-rolled. This tool only:
//    1. creates variant item_template rows (same engine as the loot Lootifier),
//    2. records the base→variant mapping in lootifier_generated_items with the
//       SENTINEL creature_entry = 0 (so the existing tracking + rollback code
//       is reused verbatim; 0 means "quest-reward context, no creature").
//
//  The C++ QuestRewardVariantStore loads:
//    SELECT base_entry, generated_entry, budget_pct
//    FROM lootifier_generated_items WHERE creature_entry = 0
//  into an in-memory base→[{variant, weight}] map at boot, and rolls one
//  variant (weighted by budget: low tier common, legendary rare) at award time.
//  At award time it is ALWAYS a variant — the plain base is never handed out
//  once a quest reward has been lootified.
//
//  Reuses the public DTOs from LootifierController (RulesetDto, NamingTierDto,
//  CommitRoll, CommitStat) and mirrors its variant-generation math so the
//  produced item rows are identical in shape.
// ══════════════════════════════════════════════════════════════════════════

public class QuestLootifierController : Controller
{
    private readonly ConnectionFactory _db;
    private readonly DbcService _dbc;
    private readonly AuditService _audit;

    private const int QUEST_SENTINEL_CREATURE = 0;   // creature_entry sentinel for quest variants
    private const int LOOTIFIER_ID_START = 950000;

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

    // Item classes — only Weapon/Armor are stat-rerollable gear. Consumables
    // (potions, food, scrolls), recipes, quest items, etc. must be excluded even
    // though they carry a spell effect (a Use: spell is not equip stats).
    private const int ITEM_CLASS_WEAPON = 2;
    private const int ITEM_CLASS_ARMOR = 4;

    public QuestLootifierController(ConnectionFactory db, DbcService dbc, AuditService audit)
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
            statNames = STAT_NAMES,
            defaultRuleset = new
            {
                budgetCeilingPct = 35,
                variantsPerItem = 10,
                allowNewAffixes = true,
                maxAffixCountChange = 1
            },
            defaultNamingTiers = new[]
            {
                new { minPct = 0, maxPct = 79, label = "Improved", position = "prefix" },
                new { minPct = 80, maxPct = 89, label = "of Power", position = "suffix" },
                new { minPct = 90, maxPct = 97, label = "of Glory", position = "suffix" },
                new { minPct = 98, maxPct = 100, label = "of the Gods", position = "suffix" }
            }
        });
    }

    // ═════════════════════════ QUEST SEARCH ═════════════════════════

    [HttpGet]
    public async Task<IActionResult> SearchQuest(string q)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Length < 2)
            return Json(new { results = Array.Empty<object>() });

        using var conn = _db.Mangos();

        // Numeric → treat as quest id; otherwise title search. Only quests that
        // actually have at least one item reward are useful here.
        bool isId = int.TryParse(q, out int qid);
        string where = isId
            ? "qt.entry = @Qid"
            : "qt.Title LIKE @Q";

        var results = await conn.QueryAsync<dynamic>($@"
            SELECT qt.entry, qt.Title AS title, qt.MinLevel AS minLevel, qt.QuestLevel AS questLevel,
                   qt.RewItemId1, qt.RewItemId2, qt.RewItemId3, qt.RewItemId4,
                   qt.RewChoiceItemId1, qt.RewChoiceItemId2, qt.RewChoiceItemId3,
                   qt.RewChoiceItemId4, qt.RewChoiceItemId5, qt.RewChoiceItemId6
            FROM quest_template qt
            WHERE {where}
              AND (qt.RewItemId1 > 0 OR qt.RewItemId2 > 0 OR qt.RewItemId3 > 0 OR qt.RewItemId4 > 0
                OR qt.RewChoiceItemId1 > 0 OR qt.RewChoiceItemId2 > 0 OR qt.RewChoiceItemId3 > 0
                OR qt.RewChoiceItemId4 > 0 OR qt.RewChoiceItemId5 > 0 OR qt.RewChoiceItemId6 > 0)
            ORDER BY qt.QuestLevel DESC, qt.entry
            LIMIT 25",
            new { Qid = qid, Q = $"%{q}%" });

        return Json(new { results });
    }

    // ═════════════════════════ REWARD ITEMS FOR A QUEST ═════════════════════════

    /// <summary>All reward item entries (guaranteed + choice) for a quest, with
    /// stat analysis so the UI can show which are lootify-eligible.</summary>
    [HttpGet]
    public async Task<IActionResult> QuestRewards(int questEntry)
    {
        using var conn = _db.Mangos();

        var quest = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
            SELECT entry, Title AS title,
                   RewItemId1, RewItemId2, RewItemId3, RewItemId4,
                   RewChoiceItemId1, RewChoiceItemId2, RewChoiceItemId3,
                   RewChoiceItemId4, RewChoiceItemId5, RewChoiceItemId6
            FROM quest_template WHERE entry = @E", new { E = questEntry });

        if (quest == null)
            return Json(new { success = false, error = "Quest not found" });

        var dict = (IDictionary<string, object>)quest;
        var rewardItems = new List<(int entry, string kind)>();
        for (int i = 1; i <= 4; i++)
        {
            int id = Convert.ToInt32(dict[$"RewItemId{i}"] ?? 0);
            if (id > 0) rewardItems.Add((id, "guaranteed"));
        }
        for (int i = 1; i <= 6; i++)
        {
            int id = Convert.ToInt32(dict[$"RewChoiceItemId{i}"] ?? 0);
            if (id > 0) rewardItems.Add((id, "choice"));
        }

        var items = await ResolveRewardItems(conn, rewardItems);
        var iconMap = new Dictionary<uint, string>();
        foreach (var it in items)
        {
            uint did = (uint)((dynamic)it).displayId;
            if (did > 0 && !iconMap.ContainsKey(did))
                iconMap[did] = _dbc.GetItemIconPath(did);
        }

        return Json(new
        {
            success = true,
            quest = new { entry = (int)quest.entry, title = (string)quest.title },
            items,
            icons = iconMap
        });
    }

    private async Task<List<object>> ResolveRewardItems(MySqlConnector.MySqlConnection conn,
        List<(int entry, string kind)> rewardItems)
    {
        // Variant counts per base item (from the admin tracking table), so the
        // UI can show which rewards are already lootified and how many variants.
        var counts = new Dictionary<int, int>();
        var entries = rewardItems.Select(r => r.entry).Distinct().ToList();
        if (entries.Count > 0)
        {
            using var adminConn = _db.Admin();
            var rows = await adminConn.QueryAsync<dynamic>(@"
                SELECT base_entry, COUNT(*) AS n FROM lootifier_generated_items
                WHERE creature_entry = @CE AND base_entry IN @Ids
                GROUP BY base_entry",
                new { CE = QUEST_SENTINEL_CREATURE, Ids = entries });
            foreach (var r in rows) counts[(int)r.base_entry] = (int)(long)r.n;
        }

        var result = new List<object>();
        foreach (var (entry, kind) in rewardItems.DistinctBy(r => r.entry))
        {
            var item = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
                SELECT * FROM item_template
                WHERE entry = @E AND patch = (SELECT MAX(patch) FROM item_template it2 WHERE it2.entry = @E)",
                new { E = entry });
            if (item == null) continue;

            var analysis = AnalyzeItemStats(item);
            bool hasStats = (int)analysis.totalStats > 0;
            bool hasSpell = ((List<SpellEffectInfo>)analysis.spellEffects).Count > 0;
            int variantCount = counts.GetValueOrDefault(entry, 0);

            result.Add(new
            {
                entry = (int)item.entry,
                name = (string)item.name,
                quality = (int)item.quality,
                displayId = (uint)item.display_id,
                requiredLevel = GetPropInt(item, "required_level"),
                kind,
                eligible = IsLootifiable(analysis),
                lootified = variantCount > 0,
                variantCount,
                totalStats = (int)analysis.totalStats,
                hasSpellEffects = hasSpell,
                detectedFamily = (string)analysis.detectedFamily
            });
        }
        return result;
    }

    // ═════════════════════════ PREVIEW ═════════════════════════

    [HttpPost]
    public async Task<IActionResult> Preview([FromBody] QuestGenerateRequest request)
    {
        if (request.itemEntries == null || request.itemEntries.Length == 0)
            return Json(new { success = false, error = "No items selected" });

        using var conn = _db.Mangos();
        var ruleset = request.ruleset ?? new RulesetDto();
        bool wantLegendary = request.ruleset == null ? true : ruleset.generateLegendary;
        var results = new List<object>();

        foreach (var itemEntry in request.itemEntries.Distinct())
        {
            var item = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
                SELECT * FROM item_template
                WHERE entry = @E AND patch = (SELECT MAX(patch) FROM item_template it2 WHERE it2.entry = @E)",
                new { E = itemEntry });
            if (item == null) continue;

            var analysis = AnalyzeItemStats(item);
            bool hasStats = (int)analysis.totalStats > 0;
            bool hasSpell = ((List<SpellEffectInfo>)analysis.spellEffects).Count > 0;
            if (!IsLootifiable(analysis)) continue;

            var variants = VariantsToJson(GenerateVariants(item, analysis, ruleset));
            uint displayId = (uint)item.display_id;

            // Sample legendary (preview only — not persisted). Same 150% budget +
            // quest-flavored name the Commit would write. Skipped for white items.
            if (wantLegendary && (int)analysis.itemQuality > 1)
            {
                var rng = new Random();
                float baseBudget = (float)analysis.weightedBudget;
                if (!hasStats) baseBudget = EstimateBudgetFromItemLevel(GetPropInt(item, "item_level"));
                float legBudget = baseBudget * 1.50f;
                int[] present = (int[])analysis.presentStatTypes;
                string fam = (string)analysis.detectedFamily;
                var elig = new HashSet<int>(present);
                foreach (var s in STAT_FAMILIES.GetValueOrDefault(fam, STAT_FAMILIES["hybrid"])) elig.Add(s);
                List<StatRoll> legStats = hasStats
                    ? RollStats(rng, legBudget, present, elig.ToList(), analysis, ruleset)
                    : RollStatsForSpellItem(rng, legBudget, elig.ToList());
                string qTitle = await GetQuestTitleForRewardItem(conn, itemEntry);
                string legName = BuildQuestLegendaryName(qTitle, (string)item.name);

                variants.Add(new
                {
                    variantIndex = 999,
                    name = legName,
                    budgetPct = 150.0,
                    tierLabel = "Legendary",
                    tierPosition = "full",
                    isLegendary = true,
                    stats = legStats.Select(s => (object)new { s.statType, s.statValue, s.name }).ToList()
                });
            }

            results.Add(new
            {
                baseItem = new
                {
                    entry = (int)item.entry,
                    name = (string)item.name,
                    quality = (int)item.quality,
                    displayId,
                    iconPath = _dbc.GetItemIconPath(displayId)
                },
                analysis,
                variants
            });
        }

        return Json(new { success = true, items = results });
    }

    // ═════════════════════════ COMMIT (all-quests or selected) ═════════════════════════

    /// <summary>
    /// Generate + persist variants for the given reward items. Variants are
    /// tracked with creature_entry = 0. Idempotent per base item: if a base
    /// already has quest variants, it's skipped (pass regenerate=true to replace).
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Commit([FromBody] QuestCommitRequest request)
    {
        using var mangosConn = _db.Mangos();
        using var adminConn = _db.Admin();

        await EnsureTrackingTables(adminConn);
        var columns = await GetItemColumns(mangosConn);
        var ruleset = request.ruleset ?? new RulesetDto();
        // Quest tool includes a per-item legendary by default; the UI toggle can
        // disable it by sending generateLegendary=false explicitly.
        if (request.ruleset == null) ruleset.generateLegendary = true;

        // Resolve the target base-item set.
        List<int> baseItems;
        bool regenerate;
        if (request.allQuests)
        {
            baseItems = await GetAllQuestRewardItems(mangosConn);
            // Bulk run: honor the UI's regenerate flag so it can skip already-done
            // items (the point of chunked/resumable All-Quests runs).
            regenerate = request.regenerate;
        }
        else
        {
            baseItems = (request.itemEntries ?? Array.Empty<int>()).Distinct().ToList();
            // Explicit single-quest selection: the user deliberately picked these
            // items, so ALWAYS (re)build them — replace any existing variants so a
            // re-click reliably produces a fresh set (and the legendary).
            regenerate = true;
        }

        if (baseItems.Count == 0)
            return Json(new { success = false, error = "No quest reward items to process" });

        // Already-lootified base items (creature_entry = 0), for idempotency.
        var existing = new HashSet<int>(await adminConn.QueryAsync<int>(
            "SELECT DISTINCT base_entry FROM lootifier_generated_items WHERE creature_entry = @CE",
            new { CE = QUEST_SENTINEL_CREATURE }));

        int itemsCreated = 0, basesProcessed = 0, basesSkipped = 0;
        var trackingRows = new List<(int genEntry, int baseEntry, int creatureEntry, float budgetPct, string tierName)>();

        foreach (var baseEntry in baseItems)
        {
            if (existing.Contains(baseEntry) && !regenerate)
            {
                basesSkipped++;
                continue;
            }

            var item = await mangosConn.QueryFirstOrDefaultAsync<dynamic>(@"
                SELECT * FROM item_template
                WHERE entry = @E AND patch = (SELECT MAX(patch) FROM item_template it2 WHERE it2.entry = @E)",
                new { E = baseEntry });
            if (item == null) continue;

            var analysis = AnalyzeItemStats(item);
            if (!IsLootifiable(analysis)) continue;

            // Regenerate: purge prior quest variants for this base first.
            if (regenerate && existing.Contains(baseEntry))
                await PurgeQuestVariantsForBase(mangosConn, adminConn, baseEntry);

            var variants = GenerateVariants(item, analysis, ruleset);
            int nextId = await GetNextLootifierId(adminConn);

            foreach (var v in variants)
            {
                int newEntry = nextId++;
                var roll = VariantToCommitRoll(v);
                await InsertVariantItemFast(mangosConn, columns, item, newEntry, roll);
                trackingRows.Add((newEntry, baseEntry, QUEST_SENTINEL_CREATURE, roll.budgetPct, roll.tierLabel ?? ""));
                itemsCreated++;
            }

            // One quest-flavored legendary per reward item (rarest in the C++
            // weighted roll via its 150% budget → floor weight).
            if (ruleset.generateLegendary)
            {
                try
                {
                    string questTitle = await GetQuestTitleForRewardItem(mangosConn, baseEntry);
                    var leg = await BuildQuestLegendary(mangosConn, adminConn, columns, item, baseEntry, questTitle, ruleset, trackingRows);
                    if (leg.HasValue) itemsCreated++;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Quest legendary failed for item {baseEntry}: {ex.Message}");
                }
            }

            basesProcessed++;

            if (trackingRows.Count >= 500)
            {
                await FlushTrackingItems(adminConn, trackingRows);
                trackingRows.Clear();
            }
        }

        if (trackingRows.Count > 0) await FlushTrackingItems(adminConn, trackingRows);

        await _audit.LogAsync(new AuditEntry
        {
            Operator = "admin",
            OperatorIp = HttpContext.Connection.RemoteIpAddress?.ToString(),
            Category = "content",
            Action = "quest_lootifier_commit",
            TargetType = "quest_lootifier",
            TargetName = request.allQuests ? "all-quests" : $"{baseItems.Count} items",
            StateBefore = "{}",
            StateAfter = JsonSerializer.Serialize(new { itemsCreated, basesProcessed, basesSkipped }),
            IsReversible = true,
            Success = true,
            Notes = $"Quest Lootifier: {itemsCreated} variants across {basesProcessed} reward items ({basesSkipped} already done)"
        });

        return Json(new
        {
            success = true,
            itemsCreated,
            basesProcessed,
            basesSkipped,
            reloadHint = "Run '.reload quest_variants' (or restart) so the core picks up the new variants."
        });
    }

    /// <summary>
    /// Returns the full list of eligible quest-reward base items and which are
    /// already lootified. The UI uses this to drive a CHUNKED "all quests" run:
    /// it feeds the remaining entries to Commit in small batches, showing real
    /// progress per batch, instead of one long blocking request.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> PlanAllQuests()
    {
        using var mangosConn = _db.Mangos();
        using var adminConn = _db.Admin();

        var allEligible = await GetAllQuestRewardItems(mangosConn);

        var done = new HashSet<int>();
        if (await TableExists(adminConn, "lootifier_generated_items"))
            done = new HashSet<int>(await adminConn.QueryAsync<int>(
                "SELECT DISTINCT base_entry FROM lootifier_generated_items WHERE creature_entry = @CE",
                new { CE = QUEST_SENTINEL_CREATURE }));

        var remaining = allEligible.Where(e => !done.Contains(e)).ToList();

        return Json(new
        {
            success = true,
            eligibleTotal = allEligible.Count,
            alreadyDone = allEligible.Count(done.Contains),
            remaining,               // entries still needing generation
            remainingCount = remaining.Count
        });
    }

    // ═════════════════════════ ROLLBACK ═════════════════════════

    /// <summary>
    /// Remove quest-reward variants. With baseEntry &gt; 0, only that base's
    /// variants; otherwise ALL quest variants (creature_entry = 0). Deletes the
    /// generated item_template rows and their tracking rows. The C++ store must
    /// be reloaded afterwards so it stops handing out the deleted variants.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Rollback([FromBody] QuestRollbackRequest request)
    {
        using var mangosConn = _db.Mangos();
        using var adminConn = _db.Admin();

        if (!await TableExists(adminConn, "lootifier_generated_items"))
            return Json(new { success = false, error = "No lootifier data found" });

        int removed;
        if (request.baseEntry > 0)
        {
            removed = await PurgeQuestVariantsForBase(mangosConn, adminConn, request.baseEntry);
        }
        else
        {
            var all = (await adminConn.QueryAsync<int>(
                "SELECT generated_entry FROM lootifier_generated_items WHERE creature_entry = @CE",
                new { CE = QUEST_SENTINEL_CREATURE })).ToList();

            foreach (var genEntry in all)
                await mangosConn.ExecuteAsync("DELETE FROM item_template WHERE entry = @E", new { E = genEntry });

            await adminConn.ExecuteAsync(
                "DELETE FROM lootifier_generated_items WHERE creature_entry = @CE",
                new { CE = QUEST_SENTINEL_CREATURE });
            removed = all.Count;
        }

        await _audit.LogAsync(new AuditEntry
        {
            Operator = "admin",
            OperatorIp = HttpContext.Connection.RemoteIpAddress?.ToString(),
            Category = "content",
            Action = "quest_lootifier_rollback",
            TargetType = "quest_lootifier",
            TargetName = request.baseEntry > 0 ? $"item:{request.baseEntry}" : "all-quest-variants",
            StateBefore = JsonSerializer.Serialize(new { removed }),
            StateAfter = "{}",
            IsReversible = false,
            Success = true,
            Notes = $"Quest Lootifier rollback: {removed} variants removed"
        });

        return Json(new { success = true, removed, reloadHint = "Run '.reload quest_variants' so the core drops the removed variants." });
    }

    // ═════════════════════════ STATUS ═════════════════════════

    [HttpGet]
    public async Task<IActionResult> Status()
    {
        using var adminConn = _db.Admin();
        if (!await TableExists(adminConn, "lootifier_generated_items"))
            return Json(new { active = false, totalVariants = 0, baseItems = 0, eligibleTotal = 0, coveragePct = 0.0 });

        var totalVariants = await adminConn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM lootifier_generated_items WHERE creature_entry = @CE",
            new { CE = QUEST_SENTINEL_CREATURE });
        var baseItems = await adminConn.ExecuteScalarAsync<int>(
            "SELECT COUNT(DISTINCT base_entry) FROM lootifier_generated_items WHERE creature_entry = @CE",
            new { CE = QUEST_SENTINEL_CREATURE });

        // Coverage: how many of ALL eligible quest reward items are done.
        using var mangosConn = _db.Mangos();
        int eligibleTotal = (await GetAllQuestRewardItems(mangosConn)).Count;
        double coveragePct = eligibleTotal > 0 ? Math.Round(100.0 * baseItems / eligibleTotal, 1) : 0.0;

        return Json(new { active = totalVariants > 0, totalVariants, baseItems, eligibleTotal, coveragePct });
    }

    /// <summary>
    /// Paged, searchable list of every lootified base reward item with its
    /// variant count, a sample quest title, and whether it has a legendary.
    /// Gives per-item visibility into what an "all quests" run produced.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Browse(string? q, int page = 1, int pageSize = 40)
    {
        using var adminConn = _db.Admin();
        using var mangosConn = _db.Mangos();
        if (!await TableExists(adminConn, "lootifier_generated_items"))
            return Json(new { total = 0, items = Array.Empty<object>() });

        // Base items + variant counts + legendary flag, from tracking.
        var grouped = (await adminConn.QueryAsync<dynamic>(@"
            SELECT base_entry,
                   COUNT(*) AS variantCount,
                   SUM(tier_name = 'Legendary') AS legendaryCount
            FROM lootifier_generated_items
            WHERE creature_entry = @CE
            GROUP BY base_entry
            ORDER BY base_entry",
            new { CE = QUEST_SENTINEL_CREATURE })).ToList();

        int total = grouped.Count;
        if (total == 0) return Json(new { total = 0, items = Array.Empty<object>() });

        // Resolve names (and honor a text/id filter). Page AFTER filtering.
        var baseIds = grouped.Select(g => (int)g.base_entry).ToList();
        var nameRows = (await mangosConn.QueryAsync<dynamic>(@"
            SELECT entry, name, quality, display_id FROM item_template
            WHERE entry IN @Ids AND patch = (SELECT MAX(patch) FROM item_template it2 WHERE it2.entry = item_template.entry)",
            new { Ids = baseIds })).ToList();
        var nameMap = nameRows.ToDictionary(r => (int)r.entry, r => r);

        bool isId = int.TryParse(q, out int qid);
        var merged = grouped.Select(g =>
        {
            int be = (int)g.base_entry;
            nameMap.TryGetValue(be, out var nm);
            return new
            {
                baseEntry = be,
                name = nm != null ? (string)nm.name : $"Item #{be}",
                quality = nm != null ? (int)nm.quality : 1,
                displayId = nm != null ? (uint)nm.display_id : 0u,
                variantCount = (int)(long)g.variantCount,
                hasLegendary = (long)(g.legendaryCount ?? 0L) > 0
            };
        })
        .Where(x => string.IsNullOrWhiteSpace(q)
            || (isId && x.baseEntry == qid)
            || x.name.Contains(q, StringComparison.OrdinalIgnoreCase))
        .ToList();

        int filteredTotal = merged.Count;
        var pageItems = merged.Skip((Math.Max(1, page) - 1) * pageSize).Take(pageSize).ToList();

        var iconMap = new Dictionary<uint, string>();
        foreach (var it in pageItems)
            if (it.displayId > 0 && !iconMap.ContainsKey(it.displayId))
                iconMap[it.displayId] = _dbc.GetItemIconPath(it.displayId);

        return Json(new
        {
            total = filteredTotal,
            page = Math.Max(1, page),
            pageSize,
            items = pageItems,
            icons = iconMap
        });
    }

    /// <summary>
    /// Return the ACTUAL committed variants for one base reward item, read from
    /// item_template (not freshly rolled). Used to expand a lootified reward in
    /// the single-quest view so you can see exactly what was generated: name,
    /// quality, stats, and budget/tier. Ordered by budget so tiers read low→high.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> ItemVariants(int baseEntry)
    {
        using var adminConn = _db.Admin();
        using var mangosConn = _db.Mangos();
        if (!await TableExists(adminConn, "lootifier_generated_items"))
            return Json(new { success = true, variants = Array.Empty<object>() });

        // (generated_entry, budget_pct, tier_name) for this base, quest-scoped.
        var tracked = (await adminConn.QueryAsync<dynamic>(@"
            SELECT generated_entry, budget_pct, tier_name
            FROM lootifier_generated_items
            WHERE creature_entry = @CE AND base_entry = @B
            ORDER BY budget_pct ASC",
            new { CE = QUEST_SENTINEL_CREATURE, B = baseEntry })).ToList();

        if (tracked.Count == 0)
            return Json(new { success = true, variants = Array.Empty<object>() });

        var genIds = tracked.Select(t => (int)t.generated_entry).ToList();
        var budgetMap = tracked.ToDictionary(t => (int)t.generated_entry, t => (float)t.budget_pct);
        var tierMap = tracked.ToDictionary(t => (int)t.generated_entry, t => (string)(t.tier_name ?? ""));

        // Drop chance: mirror the C++ QuestRewardVariantStore weighting exactly —
        // weight = max(1, 105 - budget_pct), and RollVariant picks weighted. So a
        // variant's award probability = its weight / sum of the family's weights.
        var weightMap = new Dictionary<int, float>();
        float weightSum = 0f;
        foreach (var t in tracked)
        {
            int gen = (int)t.generated_entry;
            float w = Math.Max(1f, 105f - (float)t.budget_pct);
            weightMap[gen] = w;
            weightSum += w;
        }

        // Resolve the actual item rows (names carry the applied tier prefix/suffix).
        var rows = (await mangosConn.QueryAsync<dynamic>(@"
            SELECT entry, name, quality, display_id,
                   stat_type1, stat_value1, stat_type2, stat_value2, stat_type3, stat_value3,
                   stat_type4, stat_value4, stat_type5, stat_value5, stat_type6, stat_value6,
                   stat_type7, stat_value7, stat_type8, stat_value8, stat_type9, stat_value9,
                   stat_type10, stat_value10
            FROM item_template
            WHERE entry IN @Ids AND patch = (SELECT MAX(patch) FROM item_template it2 WHERE it2.entry = item_template.entry)",
            new { Ids = genIds })).ToList();
        var rowMap = rows.ToDictionary(r => (int)r.entry, r => r);

        var iconMap = new Dictionary<uint, string>();
        var variants = new List<object>();
        foreach (var t in tracked) // already ordered by budget
        {
            int gen = (int)t.generated_entry;
            if (!rowMap.TryGetValue(gen, out var r)) continue;

            var stats = new List<object>();
            for (int i = 1; i <= 10; i++)
            {
                int st = GetPropInt(r, $"stat_type{i}");
                int sv = GetPropInt(r, $"stat_value{i}");
                if (st > 0 && sv != 0)
                    stats.Add(new { statType = st, statValue = sv, name = STAT_NAMES.GetValueOrDefault(st, $"Type{st}") });
            }

            uint did = (uint)r.display_id;
            if (did > 0 && !iconMap.ContainsKey(did)) iconMap[did] = _dbc.GetItemIconPath(did);

            variants.Add(new
            {
                entry = gen,
                name = (string)r.name,
                quality = (int)r.quality,
                displayId = did,
                budgetPct = Math.Round(budgetMap.GetValueOrDefault(gen, 0f), 1),
                dropChance = weightSum > 0 ? Math.Round(100f * weightMap.GetValueOrDefault(gen, 1f) / weightSum, 2) : 0.0,
                isLegendary = tierMap.GetValueOrDefault(gen, "") == "Legendary",
                stats
            });
        }

        return Json(new { success = true, baseEntry, variants, icons = iconMap });
    }

    // ═════════════════════════ HELPERS ═════════════════════════

    /// <summary>
    /// Find a quest title that rewards this item, for legendary naming. An item
    /// can be a reward on multiple quests; we take the lowest-id quest that
    /// grants it (stable + deterministic). Returns "" if none (shouldn't happen
    /// for a reward item, but callers guard against it).
    /// </summary>
    private async Task<string> GetQuestTitleForRewardItem(MySqlConnector.MySqlConnection conn, int itemEntry)
    {
        var title = await conn.ExecuteScalarAsync<string?>(@"
            SELECT Title FROM quest_template
            WHERE RewItemId1 = @E OR RewItemId2 = @E OR RewItemId3 = @E OR RewItemId4 = @E
               OR RewChoiceItemId1 = @E OR RewChoiceItemId2 = @E OR RewChoiceItemId3 = @E
               OR RewChoiceItemId4 = @E OR RewChoiceItemId5 = @E OR RewChoiceItemId6 = @E
            ORDER BY entry ASC LIMIT 1",
            new { E = itemEntry });
        return title ?? "";
    }

    /// <summary>
    /// Build ONE quest-flavored legendary for a reward item and return its
    /// (entry, roll), mirroring the loot Lootifier's BuildLegendaryItem but
    /// named after the QUEST rather than a boss. 150% budget → PoolWeight floor
    /// in the C++ store, so it rolls rarest of all. No loot writes here.
    /// Returns null if the item can't be built (no stats/spell).
    /// </summary>
    private async Task<(int entry, CommitRoll roll)?> BuildQuestLegendary(
        MySqlConnector.MySqlConnection mangosConn, MySqlConnector.MySqlConnection adminConn,
        List<string> columns, dynamic item, int baseItemEntry, string questTitle, RulesetDto ruleset,
        List<(int genEntry, int baseEntry, int creatureEntry, float budgetPct, string tierName)> trackingRows)
    {
        var rng = new Random();
        var analysis = AnalyzeItemStats(item);
        if (!IsLootifiable(analysis)) return null;
        // White items cap at green/blue — no legendary for them.
        if ((int)analysis.itemQuality <= 1) return null;
        bool hasStats = (int)analysis.totalStats > 0;
        bool hasSpell = ((List<SpellEffectInfo>)analysis.spellEffects).Count > 0;

        float baseBudget = (float)analysis.weightedBudget;
        if (!hasStats)
            baseBudget = EstimateBudgetFromItemLevel(GetPropInt(item, "item_level"));

        float legendaryBudget = baseBudget * 1.50f;
        int[] presentTypes = (int[])analysis.presentStatTypes;
        string family = (string)analysis.detectedFamily;

        var eligible = new HashSet<int>(presentTypes);
        foreach (var s in STAT_FAMILIES.GetValueOrDefault(family, STAT_FAMILIES["hybrid"])) eligible.Add(s);
        var eligibleList = eligible.ToList();

        List<StatRoll> stats = hasStats
            ? RollStats(rng, legendaryBudget, presentTypes, eligibleList, analysis, ruleset)
            : RollStatsForSpellItem(rng, legendaryBudget, eligibleList);

        string itemName = (string)item.name;
        string legendaryName = BuildQuestLegendaryName(questTitle, itemName);

        var roll = new CommitRoll
        {
            budgetPct = 150f,
            tierLabel = legendaryName.Contains(itemName) ? legendaryName.Replace(itemName, "").Trim() : legendaryName,
            tierPosition = "full",
            stats = stats.Select(s => new CommitStat { statType = s.statType, statValue = s.statValue }).ToArray()
        };

        int newEntry = await GetNextLootifierId(adminConn);
        await InsertVariantItemFast(mangosConn, columns, item, newEntry, roll);

        // Full-name override + legendary quality + 3× gold (as the loot legendary).
        await mangosConn.ExecuteAsync(
            "UPDATE item_template SET name = @Name, quality = 5, DisenchantID = 0 WHERE entry = @Entry",
            new { Name = legendaryName, Entry = newEntry });
        await mangosConn.ExecuteAsync(
            "UPDATE item_template SET buy_price = ROUND(buy_price * 3), sell_price = ROUND(sell_price * 3) WHERE entry = @Entry",
            new { Entry = newEntry });

        trackingRows.Add((newEntry, baseItemEntry, QUEST_SENTINEL_CREATURE, 150f, "Legendary"));
        return (newEntry, roll);
    }

    /// <summary>
    /// Quest-flavored legendary name: "Quest Title's Item" unless the quest name
    /// already overlaps the item name (avoid "Westfall's ... of Westfall"), in
    /// which case fall back to a suffix. Strips a leading "The " from the quest.
    /// </summary>
    private string BuildQuestLegendaryName(string questTitle, string itemName)
    {
        if (string.IsNullOrWhiteSpace(questTitle))
            return itemName + " of Legend";

        string q = questTitle.Trim();
        if (q.StartsWith("The ", StringComparison.OrdinalIgnoreCase)) q = q.Substring(4).Trim();

        // Overlap guard: any 4+ char word shared between quest and item name.
        var qWords = q.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Trim('\'', '\u2018', '\u2019', ',', '.').ToLowerInvariant())
            .Where(w => w.Length >= 4).ToHashSet();
        var iWords = itemName.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Trim('\'', '\u2018', '\u2019', ',', '.').ToLowerInvariant())
            .Where(w => w.Length >= 4).ToHashSet();

        if (qWords.Overlaps(iWords))
            return itemName + " of Legend";

        string possessive = q.EndsWith("s", StringComparison.OrdinalIgnoreCase) ? q + "'" : q + "'s";
        return possessive + " " + itemName;
    }

    private async Task<List<int>> GetAllQuestRewardItems(MySqlConnector.MySqlConnection conn)
    {
        var rows = await conn.QueryAsync<dynamic>(@"
            SELECT RewItemId1, RewItemId2, RewItemId3, RewItemId4,
                   RewChoiceItemId1, RewChoiceItemId2, RewChoiceItemId3,
                   RewChoiceItemId4, RewChoiceItemId5, RewChoiceItemId6
            FROM quest_template
            WHERE RewItemId1 > 0 OR RewItemId2 > 0 OR RewItemId3 > 0 OR RewItemId4 > 0
               OR RewChoiceItemId1 > 0 OR RewChoiceItemId2 > 0 OR RewChoiceItemId3 > 0
               OR RewChoiceItemId4 > 0 OR RewChoiceItemId5 > 0 OR RewChoiceItemId6 > 0");

        var set = new HashSet<int>();
        foreach (var r in rows)
        {
            var d = (IDictionary<string, object>)r;
            foreach (var kv in d)
            {
                int id = kv.Value == null ? 0 : Convert.ToInt32(kv.Value);
                if (id > 0) set.Add(id);
            }
        }

        // Keep only items that actually have stats or a spell effect — the rest
        // can't be meaningfully rerolled.
        var eligible = new List<int>();
        foreach (var id in set)
        {
            var item = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
                SELECT * FROM item_template
                WHERE entry = @E AND patch = (SELECT MAX(patch) FROM item_template it2 WHERE it2.entry = @E)",
                new { E = id });
            if (item == null) continue;
            var a = AnalyzeItemStats(item);
            if (IsLootifiable(a))
                eligible.Add(id);
        }
        return eligible;
    }

    private async Task<int> PurgeQuestVariantsForBase(MySqlConnector.MySqlConnection mangosConn,
        MySqlConnector.MySqlConnection adminConn, int baseEntry)
    {
        var gen = (await adminConn.QueryAsync<int>(
            "SELECT generated_entry FROM lootifier_generated_items WHERE creature_entry = @CE AND base_entry = @B",
            new { CE = QUEST_SENTINEL_CREATURE, B = baseEntry })).ToList();

        foreach (var g in gen)
            await mangosConn.ExecuteAsync("DELETE FROM item_template WHERE entry = @E", new { E = g });

        await adminConn.ExecuteAsync(
            "DELETE FROM lootifier_generated_items WHERE creature_entry = @CE AND base_entry = @B",
            new { CE = QUEST_SENTINEL_CREATURE, B = baseEntry });

        return gen.Count;
    }

    // ── Tracking table (shared schema with the loot Lootifier) ──

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
        for (int i = 0; i < rows.Count; i++)
        {
            if (i > 0) sb.Append(',');
            var r = rows[i];
            sb.Append($"({r.genEntry},{r.baseEntry},{r.creatureEntry},{r.budgetPct:F2},'{MySqlHelper.EscapeString(r.tierName)}',NOW())");
        }
        await adminConn.ExecuteAsync(sb.ToString());
    }

    private async Task<int> GetNextLootifierId(MySqlConnector.MySqlConnection adminConn)
    {
        int fromTracking = LOOTIFIER_ID_START;
        if (await TableExists(adminConn, "lootifier_generated_items"))
        {
            var maxTracked = await adminConn.ExecuteScalarAsync<int?>("SELECT MAX(generated_entry) FROM lootifier_generated_items");
            if (maxTracked.HasValue) fromTracking = maxTracked.Value + 1;
        }
        using var mangosConn = _db.Mangos();
        var maxInItems = await mangosConn.ExecuteScalarAsync<int?>(
            "SELECT MAX(entry) FROM item_template WHERE entry >= @Start", new { Start = LOOTIFIER_ID_START });
        int fromItems = maxInItems.HasValue ? maxInItems.Value + 1 : LOOTIFIER_ID_START;
        return Math.Max(fromTracking, fromItems);
    }

    private async Task<bool> TableExists(MySqlConnector.MySqlConnection conn, string tableName)
    {
        return await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @T",
            new { T = tableName }) > 0;
    }

    // ── Column cache (per-process) ──
    private static List<string>? _cachedItemColumns;
    private static readonly SemaphoreSlim _columnCacheLock = new(1, 1);

    private async Task<List<string>> GetItemColumns(MySqlConnector.MySqlConnection conn)
    {
        if (_cachedItemColumns != null) return _cachedItemColumns;
        await _columnCacheLock.WaitAsync();
        try
        {
            if (_cachedItemColumns != null) return _cachedItemColumns;
            _cachedItemColumns = (await conn.QueryAsync<string>(
                "SELECT COLUMN_NAME FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = 'mangos' AND TABLE_NAME = 'item_template' ORDER BY ORDINAL_POSITION"
            )).ToList();
            return _cachedItemColumns;
        }
        finally { _columnCacheLock.Release(); }
    }

    // ══════════════════════════════════════════════════════════════
    //  VARIANT ENGINE (mirrors LootifierController — kept in sync)
    // ══════════════════════════════════════════════════════════════

    private async Task InsertVariantItemFast(MySqlConnector.MySqlConnection conn, List<string> columns,
        dynamic baseItem, int newEntry, CommitRoll roll)
    {
        int baseEntry = (int)baseItem.entry;
        int basePatch = GetPropInt(baseItem, "patch");

        var statTypes = new int[10];
        var statValues = new int[10];
        for (int i = 0; i < 10; i++)
        {
            statTypes[i] = GetPropInt(baseItem, $"stat_type{i + 1}");
            statValues[i] = GetPropInt(baseItem, $"stat_value{i + 1}");
        }
        for (int i = 0; i < Math.Min(roll.stats.Length, 10); i++)
        {
            statTypes[i] = roll.stats[i].statType;
            statValues[i] = roll.stats[i].statValue;
        }
        for (int i = roll.stats.Length; i < 10; i++)
        {
            statTypes[i] = 0;
            statValues[i] = 0;
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

        float goldMult = GetGoldMultiplier(roll.budgetPct);
        if (goldMult > 1.05f)
            await conn.ExecuteAsync(
                "UPDATE item_template SET buy_price = ROUND(buy_price * @Mult), sell_price = ROUND(sell_price * @Mult) WHERE entry = @Entry",
                new { Mult = goldMult, Entry = newEntry });

        // Variant quality tracks the NAMING TIER so color matches name:
        //   Improved / of Power → blue  (3)
        //   of Glory            → purple(4)
        //   of the Gods         → orange(5)  ← this IS the legendary tier
        // Yields ~7 blue / ~2 purple / 1 orange on a normal item. White base
        // shifts down a band (green/blue/purple) since white shouldn't mint orange.
        string tl = (roll.tierLabel ?? "").ToLowerInvariant();
        int baseQuality = GetPropInt(baseItem, "quality");
        bool whiteBase = baseQuality <= 1;

        int variantQuality;
        if (roll.budgetPct >= 145f || tl.Contains("legend") || tl.Contains("gods"))
            variantQuality = whiteBase ? 4 : 5;                   // of the Gods / legendary → orange
        else if (tl.Contains("glory"))
            variantQuality = whiteBase ? 3 : 4;                   // of Glory → purple
        else
            variantQuality = whiteBase ? 2 : 3;                   // Improved / of Power → blue

        await conn.ExecuteAsync(
            "UPDATE item_template SET quality = @Q WHERE entry = @Entry",
            new { Q = variantQuality, Entry = newEntry });
    }

    private float GetGoldMultiplier(float budgetPct)
    {
        if (budgetPct >= 98) return 2.0f;
        if (budgetPct >= 90) return 1.8f;
        if (budgetPct >= 80) return 1.6f;
        float t = Math.Clamp(budgetPct / 79f, 0f, 1f);
        return 1.01f + t * 0.58f;
    }

    private List<VariantData> GenerateVariants(dynamic baseItem, dynamic analysis, RulesetDto ruleset)
    {
        var rng = new Random();

        float baseBudget = (float)analysis.weightedBudget;
        int[] presentTypes = (int[])analysis.presentStatTypes;
        string family = (string)analysis.detectedFamily;
        bool hasStats = (int)analysis.totalStats > 0;
        bool hasSpell = ((List<SpellEffectInfo>)analysis.spellEffects).Count > 0;
        int quality = (int)analysis.itemQuality;

        int numVariants = Math.Clamp(ruleset.variantsPerItem, 1, 50);
        // Quality-aware tiers: white items cap at green/blue (no epic tier).
        var tiers = TiersForQuality(quality, ruleset);

        // Budget source: statful items use their stat budget; anything without
        // stats (white gear, or a spell-only item) derives budget from item level
        // so there's something to allocate.
        if (!hasStats)
            baseBudget = EstimateBudgetFromItemLevel(GetPropInt(baseItem, "item_level"));

        // White items have little/no base budget, so a base-anchored floor would
        // compress every roll into a razor-thin band at the top of the scale
        // (making all variants read as ~95%). Instead, white uses an ABSOLUTE
        // floor derived from item level with a generous span, so its tiers spread
        // out properly (green Improved → blue of Power). Non-white keeps the
        // base-anchored floor (never below base, since it replaces the reward).
        float maxBudget, floorBudget;
        if (quality == 1)
        {
            // A modest green/blue-worthy budget from item level, spread from a
            // low floor so Improved/of Power tiers are distinguishable.
            float ilvlBudget = EstimateBudgetFromItemLevel(GetPropInt(baseItem, "item_level"));
            maxBudget = Math.Max(baseBudget, ilvlBudget) * 0.6f;   // cap ~blue-tier
            floorBudget = maxBudget * 0.15f;                        // low floor → real spread
        }
        else
        {
            maxBudget = baseBudget * (1 + ruleset.budgetCeilingPct / 100f);
            // Quest variants REPLACE the base, so the worst variant must still be
            // an upgrade — anchor the floor a hair above base (1.02×). Tiers then
            // subdivide [floor, maxBudget] rather than [0, maxBudget].
            floorBudget = Math.Min(baseBudget * 1.02f, maxBudget);
        }
        float budgetSpan = Math.Max(0f, maxBudget - floorBudget);
        var tierAllocations = AllocateTierSlots(tiers, numVariants);

        var eligible = new HashSet<int>(presentTypes);
        if (ruleset.allowNewAffixes)
            foreach (var s in STAT_FAMILIES.GetValueOrDefault(family, STAT_FAMILIES["hybrid"])) eligible.Add(s);
        var eligibleList = eligible.ToList();
        if (!hasStats) eligibleList = STAT_FAMILIES["hybrid"].ToList();

        var fingerprints = new HashSet<string> { hasStats ? BuildFingerprint(analysis) : "" };
        var variants = new List<VariantData>();

        for (int tierIdx = 0; tierIdx < tiers.Count; tierIdx++)
        {
            var tier = tiers[tierIdx];
            int slots = tierAllocations[tierIdx];

            for (int s = 0; s < slots; s++)
            {
                float tierMin = floorBudget + budgetSpan * (tier.minPct / 100f);
                float tierMax = floorBudget + budgetSpan * (Math.Min(tier.maxPct, 100f) / 100f);
                float budgetRoll = tierMin + (float)rng.NextDouble() * (tierMax - tierMin);
                float budgetPct = maxBudget > 0 ? (budgetRoll / maxBudget) * 100f : 0;

                List<StatRoll> stats = hasStats
                    ? RollStats(rng, budgetRoll, presentTypes, eligibleList, analysis, ruleset)
                    : RollStatsForSpellItem(rng, budgetRoll, eligibleList);

                string name = ApplyTierName((string)baseItem.name, tier.label, tier.position);
                var cand = new VariantData { name = name, budgetPct = budgetPct, tierLabel = tier.label, tierPosition = tier.position, stats = stats };

                var fp = BuildVariantFingerprint(cand);
                if (fingerprints.Contains(fp))
                {
                    bool found = false;
                    for (int retry = 0; retry < 10; retry++)
                    {
                        budgetRoll = tierMin + (float)rng.NextDouble() * (tierMax - tierMin);
                        budgetPct = maxBudget > 0 ? (budgetRoll / maxBudget) * 100f : 0;
                        stats = hasStats
                            ? RollStats(rng, budgetRoll, presentTypes, eligibleList, analysis, ruleset)
                            : RollStatsForSpellItem(rng, budgetRoll, eligibleList);
                        cand = new VariantData { name = name, budgetPct = budgetPct, tierLabel = tier.label, tierPosition = tier.position, stats = stats };
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

    private int[] AllocateTierSlots(List<TierRange> tiers, int total)
    {
        var alloc = new int[tiers.Count];
        if (tiers.Count == 0) return alloc;
        if (tiers.Count == 1) { alloc[0] = total; return alloc; }

        int upper = 0;
        for (int i = 1; i < tiers.Count; i++)
        {
            alloc[i] = (i == tiers.Count - 1) ? 1 : 2;
            upper += alloc[i];
        }
        alloc[0] = Math.Max(1, total - upper);

        int sum = alloc.Sum();
        while (sum > total)
        {
            int maxIdx = 0;
            for (int i = 1; i < alloc.Length; i++) if (alloc[i] > alloc[maxIdx]) maxIdx = i;
            if (alloc[maxIdx] > 1) { alloc[maxIdx]--; sum--; } else break;
        }
        return alloc;
    }

    private float EstimateBudgetFromItemLevel(int itemLevel) => Math.Max(5f, itemLevel * 0.7f);

    private List<StatRoll> RollStatsForSpellItem(Random rng, float budgetRoll, List<int> eligibleList)
    {
        float statBudget = budgetRoll * 0.40f;
        int slotCount = statBudget < 10 ? 1 : (statBudget < 25 ? 2 : 3);
        var chosen = eligibleList.OrderBy(_ => rng.Next()).Take(slotCount).ToList();
        var weights = chosen.Select(t => DEFAULT_STAT_WEIGHTS.GetValueOrDefault(t, 1.0f)).ToArray();
        float totalWeight = weights.Sum();
        var rolled = new List<StatRoll>();
        float remaining = statBudget;
        for (int s = 0; s < chosen.Count; s++)
        {
            float share;
            if (s == chosen.Count - 1) share = remaining;
            else
            {
                float basePortion = statBudget * (weights[s] / totalWeight);
                float jitter = (float)(rng.NextDouble() * 0.2 - 0.1) * basePortion;
                share = Math.Max(1, basePortion + jitter);
            }
            int statValue = Math.Max(1, (int)Math.Round(share / weights[s]));
            remaining -= statValue * weights[s];
            rolled.Add(new StatRoll { statType = chosen[s], statValue = statValue, name = STAT_NAMES.GetValueOrDefault(chosen[s], $"Type{chosen[s]}") });
        }
        return rolled;
    }

    private List<StatRoll> RollStats(Random rng, float budgetRoll, int[] presentTypes,
        List<int> eligibleList, dynamic analysis, RulesetDto ruleset)
    {
        int baseSlotCount = ((List<object>)analysis.stats).Count;
        int slotCount = baseSlotCount;
        if (ruleset.allowNewAffixes && rng.NextDouble() < 0.2 && baseSlotCount < 5)
            slotCount = Math.Min(baseSlotCount + ruleset.maxAffixCountChange, 10);

        var chosen = new List<int>();
        chosen.AddRange(presentTypes.OrderBy(_ => rng.Next()).Take(slotCount));
        while (chosen.Count < slotCount)
        {
            var pool = eligibleList.Where(s => !chosen.Contains(s)).ToList();
            if (pool.Count == 0) break;
            chosen.Add(pool[rng.Next(pool.Count)]);
        }

        var weights = chosen.Select(t => DEFAULT_STAT_WEIGHTS.GetValueOrDefault(t, 1.0f)).ToArray();
        float totalWeight = weights.Sum();
        var rolled = new List<StatRoll>();
        float remaining = budgetRoll;
        for (int s = 0; s < chosen.Count; s++)
        {
            float share;
            if (s == chosen.Count - 1) share = remaining;
            else
            {
                float basePortion = budgetRoll * (weights[s] / totalWeight);
                float jitter = (float)(rng.NextDouble() * 0.3 - 0.15) * basePortion;
                share = Math.Max(1, basePortion + jitter);
            }
            int statValue = Math.Max(1, (int)Math.Round(share / weights[s]));
            remaining -= statValue * weights[s];
            rolled.Add(new StatRoll { statType = chosen[s], statValue = statValue, name = STAT_NAMES.GetValueOrDefault(chosen[s], $"Type{chosen[s]}") });
        }
        return rolled;
    }

    private string BuildFingerprint(dynamic analysis)
    {
        var stats = (List<object>)analysis.stats;
        return string.Join("|", stats.Select(s => $"{((dynamic)s).statType}:{((dynamic)s).statValue}").OrderBy(x => x));
    }

    private string BuildVariantFingerprint(VariantData v) =>
        string.Join("|", v.stats.Select(s => $"{s.statType}:{s.statValue}").OrderBy(x => x));

    private List<TierRange> GetRequiredTiers(RulesetDto ruleset)
    {
        if (ruleset.namingTiers != null && ruleset.namingTiers.Length > 0)
            return ruleset.namingTiers.Where(t => !string.IsNullOrEmpty(t.label))
                .Select(t => new TierRange { minPct = t.minPct, maxPct = t.maxPct, label = t.label ?? "", position = t.position ?? "suffix" })
                .ToList();

        return new List<TierRange>
        {
            new() { minPct = 0, maxPct = 79, label = "Improved", position = "prefix" },
            new() { minPct = 80, maxPct = 89, label = "of Power", position = "suffix" },
            new() { minPct = 90, maxPct = 97, label = "of Glory", position = "suffix" },
            new() { minPct = 98, maxPct = 100, label = "of the Gods", position = "suffix" }
        };
    }

    private string ApplyTierName(string baseName, string tierLabel, string tierPosition)
    {
        if (string.IsNullOrEmpty(tierLabel)) return baseName;
        return tierPosition == "prefix" ? tierLabel + " " + baseName : baseName + " " + tierLabel;
    }

    private List<object> VariantsToJson(List<VariantData> variants) =>
        variants.Select((v, idx) => (object)new
        {
            variantIndex = idx,
            name = v.name,
            budgetPct = Math.Round(v.budgetPct, 1),
            tierLabel = v.tierLabel,
            tierPosition = v.tierPosition,
            stats = v.stats.Select(s => (object)new { s.statType, s.statValue, s.name }).ToList()
        }).ToList();

    private CommitRoll VariantToCommitRoll(VariantData v) => new()
    {
        budgetPct = v.budgetPct,
        tierLabel = v.tierLabel ?? "",
        tierPosition = v.tierPosition ?? "suffix",
        stats = v.stats.Select(s => new CommitStat { statType = s.statType, statValue = s.statValue }).ToArray()
    };

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
        if (presentTypes.IsSubsetOf(STAT_FAMILIES["physical"])) family = "physical";
        else if (presentTypes.IsSubsetOf(STAT_FAMILIES["caster"])) family = "caster";

        // Equippability gate: only Weapon/Armor that occupy an equip slot are
        // stat-rerollable gear. A Use:/spell effect alone (potions, food, scrolls,
        // recipes) does NOT make something lootifiable.
        int itemClass = GetPropInt(item, "class");
        int invType = GetPropInt(item, "inventory_type");
        bool isEquippable = (itemClass == ITEM_CLASS_WEAPON || itemClass == ITEM_CLASS_ARMOR) && invType > 0;

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

    private int GetPropInt(dynamic obj, string name)
    {
        var dict = obj as IDictionary<string, object>;
        if (dict != null && dict.TryGetValue(name, out var val))
            return val == null ? 0 : Convert.ToInt32(val);
        return 0;
    }

    /// <summary>
    /// Central eligibility rule for a quest reward item, given its analysis.
    /// Must be equippable gear (Weapon/Armor in a slot) and NOT grey (quality 0).
    /// Green+ items need existing stats; WHITE (quality 1) items are allowed even
    /// with no stats — they get stats added, but only up to the green/blue tier.
    /// </summary>
    private bool IsLootifiable(dynamic analysis)
    {
        bool equippable = (bool)analysis.isEquippable;
        int quality = (int)analysis.itemQuality;
        if (!equippable) return false;
        if (quality <= 0) return false;                 // grey excluded
        bool hasStats = (int)analysis.totalStats > 0;
        if (quality == 1) return true;                  // white: allowed even without stats
        return hasStats;                                // green+ : must already have stats
    }

    /// <summary>
    /// Naming tiers permitted for an item of the given quality. White (1) items
    /// only reach green/blue-equivalent budget tiers, so their variants never
    /// exceed the mid ladder — no purple "of the Gods" on a white base.
    /// Non-white items use the full ruleset tier list.
    /// </summary>
    private List<TierRange> TiersForQuality(int quality, RulesetDto ruleset)
    {
        var full = GetRequiredTiers(ruleset);
        if (quality != 1) return full;

        // White cap: keep only tiers whose top budget stays at/under the
        // "of Glory" band (<= 89% here), i.e. Improved + of Power. This yields
        // green/blue-feeling upgrades without epic-tier rolls.
        var capped = full.Where(t => t.maxPct <= 89f).ToList();
        return capped.Count > 0 ? capped : new List<TierRange>
        {
            new() { minPct = 0, maxPct = 79, label = "Improved", position = "prefix" },
            new() { minPct = 80, maxPct = 89, label = "of Power", position = "suffix" }
        };
    }
}

// ══════════════════════════════════════════════════════════════
//  DTOs specific to the quest controller.
//  The variant-engine types (SpellEffectInfo, VariantData, StatRoll,
//  TierRange) and the commit DTOs (RulesetDto, CommitRoll, CommitStat,
//  NamingTierDto) are declared in LootifierController.cs and shared here,
//  since both controllers live in the same assembly.
// ══════════════════════════════════════════════════════════════

public class QuestGenerateRequest
{
    public int[]? itemEntries { get; set; }
    public RulesetDto? ruleset { get; set; }
}

public class QuestCommitRequest
{
    public bool allQuests { get; set; } = false;
    public int[]? itemEntries { get; set; }
    public bool regenerate { get; set; } = false;
    public RulesetDto? ruleset { get; set; }
}

public class QuestRollbackRequest
{
    public int baseEntry { get; set; } = 0; // 0 = all quest variants
}