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
//  Generation is BAND-BASED and ADDITIVE-ONLY, mirroring the Crafting Lootifier.
//  The admin configures BANDS (QuestRulesetDto/QuestBandDto) — each a named tier
//  with a min/max boost % and a slot count — and the tool rolls `slots` variants
//  per band. Every variant PRESERVES the base reward's stats verbatim (never
//  reduced, never dropped) and layers a boost-sized bonus on top; a stat-less
//  white base mints a modest green/blue set instead. Band labels drive the colour
//  ladder and the boost % is stored as budget_pct so the C++ weighted roll keeps
//  higher bands rarer. Shares SpellEffectInfo/VariantData/StatRoll/CommitRoll/
//  CommitStat from LootifierController; the quest-flavoured legendary is separate.
// ══════════════════════════════════════════════════════════════════════════

public class QuestLootifierController : Controller
{
    private readonly ConnectionFactory _db;
    private readonly DbcService _dbc;
    private readonly AuditService _audit;
    private readonly ILogger<QuestLootifierController> _logger;

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

    // ── Weapon DPS by tier (damage-only; weapon SPEED/delay is never touched) ──
    // Vanilla (pre-TBC) weapon DPS = Green(iLevel) × qualityMult, so at a FIXED
    // iLevel the only lever between qualities is a flat multiplier on the damage
    // range: blue (Superior) ×1.105, epic ×1.215 over the green line (Vanilla WoW
    // Wiki item-DPS budget). A tier bump keeps the weapon's iLevel, hand type and
    // delay, so scaling min+max damage by (1+p) raises DPS by exactly p%. These are
    // DEFAULT proposals; legendary had no vanilla formula slot (Sulfuras ~+7% over
    // an epic 2H, Thunderfury far below), so 1.30 is a nominal, override-me ceiling.
    // Numbers live in DefaultBands (as %) and Meta.dpsReference (for the UI).
    private const float DPS_GREEN_SLOPE = 0.6f;
    private const float DPS_GREEN_INTERCEPT = 26.6f;
    private const int DPS_GREEN_ILVL_FLOOR = 45;   // Green1H = (iLevel-45)*0.6 + 26.6 (valid ~iLevel 45-65)

    // ── Additive-generation dials (mirror CraftingLootifier) ──
    private const float MIN_DELTA_BUDGET = 2.0f;   // additive floor: every variant is a real upgrade over the base reward
    private const float QUEST_BUMP_BIAS = 0.5f;    // delta split: existing-line bumps vs new affixes (0 = all new, 1 = all bumps)

    // ── Player-item reroll (regenerate without orphaning owned copies) ──
    // Character data lives in a separate schema on the same MySQL server; the
    // mangos user has rights on it, so we reach it with schema-qualified names
    // through the existing connection rather than a second ConnectionFactory entry.
    private const string CHARACTERS_DB = "characters";
    // Tracked legendaries carry budget_pct = 150 (see BuildQuestLegendary).
    private const float LEGENDARY_BUDGET_MARK = 145f;

    public QuestLootifierController(ConnectionFactory db, DbcService dbc, AuditService audit,
        ILogger<QuestLootifierController> logger)
    {
        _db = db;
        _dbc = dbc;
        _audit = audit;
        _logger = logger;
    }

    public IActionResult Index() => View();

    [HttpGet]
    public IActionResult Meta()
    {
        return Json(new
        {
            statNames = STAT_NAMES,
            defaultRuleset = DefaultRuleset(),
            // Vanilla weapon-DPS reference so the UI can propose per-tier damage
            // bumps relative to blue/purple/legendary at the reward's level.
            dpsReference = new
            {
                qualityMult = new { green = 1.000f, blue = 1.105f, purple = 1.215f, legendary = 1.300f },
                greenOneHand = new { slope = DPS_GREEN_SLOPE, intercept = DPS_GREEN_INTERCEPT, ilvlFloor = DPS_GREEN_ILVL_FLOOR },
                note = "blue +10.5% / purple +21.5% over green at the same level; legendary 1.30 is nominal (vanilla legendaries were hand-tuned)."
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
        var ruleset = request.ruleset ?? DefaultRuleset();
        bool wantLegendary = ruleset.generateLegendary;
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

            var wpn = WeaponDpsInfo(item);
            var variants = VariantsToJson(GenerateVariants(item, analysis, ruleset), (int)analysis.itemQuality,
                (bool)wpn.isWeapon, (float)wpn.baseDps, ruleset);
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

                float legDpsBump = (bool)wpn.isWeapon ? ruleset.legendaryDpsBumpPct : 0f;
                variants.Add(new
                {
                    variantIndex = 999,
                    name = legName,
                    budgetPct = 150.0,
                    tierLabel = "Legendary",
                    tierPosition = "full",
                    isLegendary = true,
                    dpsBumpPct = (bool)wpn.isWeapon ? (float?)legDpsBump : null,
                    dps = (bool)wpn.isWeapon ? (double?)Math.Round((float)wpn.baseDps * (1.0 + legDpsBump / 100.0), 1) : null,
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
                    iconPath = _dbc.GetItemIconPath(displayId),
                    weapon = wpn
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
        // Quest tool includes a per-item legendary by default (DefaultRuleset sets
        // generateLegendary = true); the UI toggle disables it by sending false.
        var ruleset = request.ruleset ?? DefaultRuleset();

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
        int itemsRemapped = 0;                  // player-owned copies repointed at new variants
        var rerollRng = new Random();
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

            // Regenerate: capture the prior variants but DON'T delete yet — owned
            // copies get repointed at the new set first (see below), and deferring
            // the delete also keeps GetNextLootifierId above the old ids.
            bool isRegen = regenerate && existing.Contains(baseEntry);
            var oldTierByEntry = isRegen
                ? await GetQuestVariantTierMap(adminConn, baseEntry)
                : new Dictionary<int, string>();
            var oldEntries = oldTierByEntry.Keys.ToList();

            var variants = GenerateVariants(item, analysis, ruleset);
            int nextId = await GetNextLootifierId(adminConn);

            var rerollPool = new List<int>();                    // any-tier fallback pool
            var newByTier = new Dictionary<string, List<int>>(); // tier -> new variants (same-tier reroll)

            foreach (var v in variants)
            {
                int newEntry = nextId++;
                var roll = VariantToCommitRoll(v);
                await InsertVariantItemFast(mangosConn, columns, item, newEntry, roll, ruleset.goldValueScalePct,
                    ResolveBandGoldBump(ruleset.bands, roll.tierLabel, roll.budgetPct),
                    ResolveBandDpsBump(ruleset.bands, roll.tierLabel, roll.budgetPct));
                string tierKey = roll.tierLabel ?? "";
                trackingRows.Add((newEntry, baseEntry, QUEST_SENTINEL_CREATURE, roll.budgetPct, tierKey));
                rerollPool.Add(newEntry);
                AddToTierPool(newByTier, tierKey, newEntry);
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
                    if (leg.HasValue)
                    {
                        itemsCreated++;
                        // The legendary is only a reroll target if explicitly opted in —
                        // otherwise a regen would hand out legendaries for free.
                        if (ruleset.rerollIncludeLegendary)
                        {
                            rerollPool.Add(leg.Value.entry);
                            AddToTierPool(newByTier, "Legendary", leg.Value.entry);
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Was Debug.WriteLine — compiled out in Release, so a legendary
                    // that failed to build (and, critically, failed to register its
                    // tracking row) reported NOTHING. Log it loudly.
                    _logger.LogError(ex,
                        "Quest legendary FAILED for base item {BaseEntry} — the variant may exist " +
                        "in item_template but be absent from lootifier_generated_items, which makes " +
                        "it invisible to regen cleanup, owned-item remap, and the retexture queue.",
                        baseEntry);
                }
            }

            // Regenerate: repoint every player-owned copy of the OLD variants at a
            // NEW variant of the SAME tier (per-item roll), then drop the old
            // templates. Owned copies are never orphaned; if the tier no longer
            // exists they fall back to any new variant, then the plain base item.
            if (isRegen && oldEntries.Count > 0)
            {
                itemsRemapped += await RemapOwnedVariants(mangosConn, oldEntries, rerollPool, baseEntry, rerollRng, oldTierByEntry, newByTier);
                await DeleteQuestVariants(mangosConn, adminConn, oldEntries);
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
            StateAfter = JsonSerializer.Serialize(new { itemsCreated, basesProcessed, basesSkipped, itemsRemapped }),
            IsReversible = true,
            Success = true,
            Notes = $"Quest Lootifier: {itemsCreated} variants across {basesProcessed} reward items ({basesSkipped} already done, {itemsRemapped} player-owned items rerolled)"
        });

        return Json(new
        {
            success = true,
            itemsCreated,
            basesProcessed,
            basesSkipped,
            itemsRemapped,
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
            // Remap owned copies back to their plain base item BEFORE deleting the
            // templates, grouped by base so each copy lands on its own original.
            var tracked = (await adminConn.QueryAsync<dynamic>(
                "SELECT generated_entry, base_entry FROM lootifier_generated_items WHERE creature_entry = @CE",
                new { CE = QUEST_SENTINEL_CREATURE })).ToList();

            var byBase = tracked
                .GroupBy(t => (int)t.base_entry)
                .ToDictionary(g => g.Key, g => g.Select(t => (int)t.generated_entry).ToList());

            var rng = new Random();
            foreach (var kv in byBase)
                await RemapOwnedVariants(mangosConn, kv.Value, new List<int>(), kv.Key, rng);

            var all = tracked.Select(t => (int)t.generated_entry).ToList();
            if (all.Count > 0)
                await mangosConn.ExecuteAsync("DELETE FROM item_template WHERE entry IN @E", new { E = all });

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

    // ═════════════════════════ REVALUE ═════════════════════════

    /// <summary>
    /// Lists the tiers that ACTUALLY EXIST in tracking, with variant counts and
    /// the price multiplier each tier is MEASURED to be sitting at right now
    /// (variant sell_price / base sell_price — read from the DB, not assumed).
    /// This is what the Revalue dialog renders: your tier names, your numbers.
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
            new { CE = QUEST_SENTINEL_CREATURE })).ToList();

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
    public async Task<IActionResult> Revalue([FromBody] QuestRevalueRequest? request)
    {
        var wanted = (request?.tiers ?? Array.Empty<QuestTierBumpDto>())
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
            new { CE = QUEST_SENTINEL_CREATURE })).ToList();

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
        List<string> columns, dynamic item, int baseItemEntry, string questTitle, QuestRulesetDto ruleset,
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
        // goldBumpPct: 0 suppresses the band/curve multiplier on insert — the
        // legendary's price is set in ONE place below, from legendaryGoldBumpPct.
        // dpsBumpPct: the quest legendary carries its own damage bump (parallel to
        // its own gold bump), independent of any band.
        await InsertVariantItemFast(mangosConn, columns, item, newEntry, roll, ruleset.goldValueScalePct, 0f,
            ruleset.legendaryDpsBumpPct);

        // Full-name override + legendary quality + explicit gold bump above base
        // (default 500% = the old x6 stock stack), master-scaled.
        //
        // The disenchant column is resolved from the LIVE schema, never hardcoded.
        // This line used to read `DisenchantID = 0`; VMaNGOS names the column
        // `disenchant_id`, so every legendary threw "Unknown column 'DisenchantID'"
        // here. The caller wraps this whole method in a catch that only did a
        // System.Diagnostics.Debug.WriteLine — a NO-OP in a Release build — so the
        // throw was completely silent, and, worse, it unwound past the
        // trackingRows.Add() at the bottom of this method.
        //
        // The legendary therefore existed in item_template (InsertVariantItemFast
        // had already run, name and quality included) but was never recorded in
        // lootifier_generated_items. Everything downstream keys off that table, so
        // the legendary became invisible to:
        //   • DeleteQuestVariants  → each regen piled up ANOTHER orphan legendary
        //   • RemapOwnedVariants   → players holding one got orphaned on regen
        //   • BuildRetextureQueue  → never recolored, kept the vanilla display
        // One typo, three symptoms, zero log lines.
        string? disCol = columns.FirstOrDefault(c =>
            c.Equals("disenchant_id", StringComparison.OrdinalIgnoreCase) ||
            c.Equals("DisenchantID", StringComparison.OrdinalIgnoreCase));
        string disSet = disCol != null ? $", `{disCol}` = 0" : "";

        await mangosConn.ExecuteAsync(
            $"UPDATE item_template SET name = @Name, quality = 5{disSet} WHERE entry = @Entry",
            new { Name = legendaryName, Entry = newEntry });
        float legGoldMult = ScaleGoldMult(1f + ruleset.legendaryGoldBumpPct / 100f, ruleset.goldValueScalePct);
        if (legGoldMult > 1.001f)
            await mangosConn.ExecuteAsync(
                "UPDATE item_template SET buy_price = ROUND(buy_price * @Mult), sell_price = ROUND(sell_price * @Mult) WHERE entry = @Entry",
                new { Mult = legGoldMult, Entry = newEntry });

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

    /// <summary>Tracked variant entries for a quest base item.</summary>
    private async Task<List<int>> GetQuestVariantEntries(MySqlConnector.MySqlConnection adminConn, int baseEntry) =>
        (await adminConn.QueryAsync<int>(
            "SELECT generated_entry FROM lootifier_generated_items WHERE creature_entry = @CE AND base_entry = @B",
            new { CE = QUEST_SENTINEL_CREATURE, B = baseEntry })).ToList();

    // generated_entry -> tier_name for one base, so a regen can reroll each owned
    // copy into a NEW variant of the tier it already had.
    private async Task<Dictionary<int, string>> GetQuestVariantTierMap(MySqlConnector.MySqlConnection adminConn, int baseEntry) =>
        (await adminConn.QueryAsync(
            "SELECT generated_entry, tier_name FROM lootifier_generated_items WHERE creature_entry = @CE AND base_entry = @B",
            new { CE = QUEST_SENTINEL_CREATURE, B = baseEntry }))
        .ToDictionary(r => (int)r.generated_entry, r => (string)(r.tier_name ?? ""));

    /// <summary>
    /// Delete a specific set of variant entries (templates + tracking rows).
    /// Keyed by explicit entry list rather than base_entry so it can't collide
    /// with freshly-generated rows that may already have been flushed.
    /// </summary>
    private async Task<int> DeleteQuestVariants(MySqlConnector.MySqlConnection mangosConn,
        MySqlConnector.MySqlConnection adminConn, List<int> entries)
    {
        if (entries.Count == 0) return 0;
        await mangosConn.ExecuteAsync("DELETE FROM item_template WHERE entry IN @E", new { E = entries });
        await adminConn.ExecuteAsync(
            "DELETE FROM lootifier_generated_items WHERE generated_entry IN @E", new { E = entries });
        return entries.Count;
    }

    private async Task<int> PurgeQuestVariantsForBase(MySqlConnector.MySqlConnection mangosConn,
        MySqlConnector.MySqlConnection adminConn, int baseEntry)
    {
        var gen = await GetQuestVariantEntries(adminConn, baseEntry);
        // Owned copies fall back to the plain base item — never left pointing at
        // a template we're about to delete.
        await RemapOwnedVariants(mangosConn, gen, new List<int>(), baseEntry, new Random());
        return await DeleteQuestVariants(mangosConn, adminConn, gen);
    }

    // ══════════════════════════════════════════════════════════════
    //  PLAYER-ITEM REROLL
    //
    //  Regenerating deletes the old variant templates. Any copy a player has
    //  equipped, bagged, mailed or listed still points at those entries and would
    //  be orphaned. Instead of protecting them, we REPOINT each owned copy at a
    //  randomly-chosen NEW variant of the SAME base item (rolled per item, so two
    //  pieces don't land on the same variant). If there is no new set (rollback),
    //  they fall back to the plain base item.
    //
    //  item_instance.item_id is the authoritative entry. Redundant entry columns
    //  on character_inventory / mail_items / auction are discovered at runtime and
    //  kept in sync; anything absent is skipped.
    // ══════════════════════════════════════════════════════════════

    private async Task<bool> CharactersDbAvailable(MySqlConnector.MySqlConnection conn) =>
        await conn.ExecuteScalarAsync<int>(@"
            SELECT COUNT(*) FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = @Db AND TABLE_NAME = 'item_instance' AND COLUMN_NAME = 'item_id'",
            new { Db = CHARACTERS_DB }) > 0;

    /// <summary>
    /// Auxiliary character tables that redundantly store the item entry alongside
    /// the item GUID. Discovered at runtime so a schema without them still works.
    /// </summary>
    private async Task<List<(string table, string guidCol, string entryCol)>> GetCharacterItemRefTables(
        MySqlConnector.MySqlConnection conn)
    {
        var candidates = new (string table, string[] guidCols, string[] entryCols)[]
        {
            ("character_inventory", new[] { "item", "item_guid" }, new[] { "item_template", "item_id", "itemEntry" }),
            ("mail_items",          new[] { "item_guid", "itemguid", "item" }, new[] { "item_template", "item_id", "itemEntry" }),
            ("auction",             new[] { "item_guid", "itemguid" }, new[] { "item_template", "item_id", "itemEntry" }),
        };

        var found = new List<(string, string, string)>();
        foreach (var c in candidates)
        {
            var cols = (await conn.QueryAsync<string>(@"
                SELECT COLUMN_NAME FROM information_schema.COLUMNS
                WHERE TABLE_SCHEMA = @Db AND TABLE_NAME = @T",
                new { Db = CHARACTERS_DB, T = c.table })).ToList();
            if (cols.Count == 0) continue;

            string? g = c.guidCols.FirstOrDefault(x => cols.Contains(x, StringComparer.OrdinalIgnoreCase));
            string? e = c.entryCols.FirstOrDefault(x => cols.Contains(x, StringComparer.OrdinalIgnoreCase));
            if (g != null && e != null) found.Add((c.table, g, e));
        }
        return found;
    }

    /// <summary>
    /// Repoint every player-owned copy of <paramref name="oldEntries"/> at a random
    /// entry from <paramref name="newPool"/> (same base item), or at
    /// <paramref name="fallbackEntry"/> when the pool is empty. Returns the number
    /// of item instances remapped.
    /// </summary>
    private async Task<int> RemapOwnedVariants(MySqlConnector.MySqlConnection mangosConn,
        List<int> oldEntries, List<int> newPool, int fallbackEntry, Random rng,
        Dictionary<int, string>? oldTierByEntry = null,
        Dictionary<string, List<int>>? newByTier = null)
    {
        if (oldEntries == null || oldEntries.Count == 0) return 0;
        if (!await CharactersDbAvailable(mangosConn)) return 0;

        // guid + the OLD variant entry each copy currently holds, so it can be
        // repointed at a NEW variant of the SAME tier (see PickRerollTarget).
        var owned = (await mangosConn.QueryAsync(
            $"SELECT guid, item_id FROM `{CHARACTERS_DB}`.item_instance WHERE item_id IN @Old",
            new { Old = oldEntries })).ToList();
        if (owned.Count == 0) return 0;

        var aux = await GetCharacterItemRefTables(mangosConn);
        int remapped = 0;

        foreach (var row in owned)
        {
            var d = (IDictionary<string, object>)row;
            int itemGuid = Convert.ToInt32(d["guid"]);
            int oldId = Convert.ToInt32(d["item_id"]);
            int target = PickRerollTarget(oldId, newPool, fallbackEntry, rng, oldTierByEntry, newByTier);
            if (target <= 0) continue;

            await mangosConn.ExecuteAsync(
                $"UPDATE `{CHARACTERS_DB}`.item_instance SET item_id = @New WHERE guid = @G",
                new { New = target, G = itemGuid });

            foreach (var t in aux)
                await mangosConn.ExecuteAsync(
                    $"UPDATE `{CHARACTERS_DB}`.`{t.table}` SET `{t.entryCol}` = @New WHERE `{t.guidCol}` = @G",
                    new { New = target, G = itemGuid });

            remapped++;
        }
        return remapped;
    }

    // Choose the replacement variant for one owned copy. Same tier as the copy
    // held wins; otherwise any new variant of this base; otherwise the plain base.
    private static int PickRerollTarget(int oldId, List<int> newPool, int fallbackEntry, Random rng,
        Dictionary<int, string>? oldTierByEntry, Dictionary<string, List<int>>? newByTier)
    {
        if (oldTierByEntry != null && newByTier != null &&
            oldTierByEntry.TryGetValue(oldId, out var tier) &&
            newByTier.TryGetValue(tier, out var sameTier) && sameTier.Count > 0)
            return sameTier[rng.Next(sameTier.Count)];
        if (newPool.Count > 0) return newPool[rng.Next(newPool.Count)];
        return fallbackEntry;
    }

    // Append a generated entry to its tier bucket (for same-tier reroll pools).
    private static void AddToTierPool(Dictionary<string, List<int>> byTier, string tier, int entry)
    {
        if (!byTier.TryGetValue(tier, out var lst)) byTier[tier] = lst = new List<int>();
        lst.Add(entry);
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
        dynamic baseItem, int newEntry, CommitRoll roll, float goldScalePct = 100f, float? goldBumpPct = null,
        float? dpsBumpPct = null)
    {
        int baseEntry = (int)baseItem.entry;
        int basePatch = GetPropInt(baseItem, "patch");

        // Weapon DAMAGE bump for this variant (weapons only; speed/delay untouched,
        // so DPS scales 1:1 with the damage range). Null/0 -> damage copied verbatim.
        int itemClass = GetPropInt(baseItem, "class");
        double dmgMult = (itemClass == ITEM_CLASS_WEAPON && dpsBumpPct.HasValue && dpsBumpPct.Value > 0f)
            ? 1.0 + dpsBumpPct.Value / 100.0
            : 1.0;

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
            else if (dmgMult != 1.0 && (col.StartsWith("dmg_min") || col.StartsWith("dmg_max")))
            {
                // dmg_min1..5 / dmg_max1..5 — scale every present damage band by the
                // same factor (keeps min:max feel; delay/dmg_type untouched).
                selectParts.Add($"ROUND(`{col}` * @DmgMult, 2) AS `{col}`");
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
            DmgMult = dmgMult,
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

        // Explicit per-band bump (Gold +% column) beats the legacy budget curve.
        float goldMult = ScaleGoldMult(
            goldBumpPct.HasValue ? 1f + goldBumpPct.Value / 100f : GetGoldMultiplier(roll.budgetPct),
            goldScalePct);
        if (goldMult > 1.05f)
            await conn.ExecuteAsync(
                "UPDATE item_template SET buy_price = ROUND(buy_price * @Mult), sell_price = ROUND(sell_price * @Mult) WHERE entry = @Entry",
                new { Mult = goldMult, Entry = newEntry });

        // Variant quality tracks the band's naming tier so colour matches name
        // (shared with the preview via VariantQualityForTier):
        //   Improved / of Power → blue   of Glory → purple   of the Gods / legendary → orange
        // White bases shift one band down (green / blue / purple) — white never mints orange.
        int baseQuality = GetPropInt(baseItem, "quality");
        int variantQuality = VariantQualityForTier(roll.tierLabel ?? "", roll.budgetPct, baseQuality);

        await conn.ExecuteAsync(
            "UPDATE item_template SET quality = @Q WHERE entry = @Entry",
            new { Q = variantQuality, Entry = newEntry });
    }

    // Scales only the MARKUP portion of a gold multiplier, so the tier-graded
    // curve keeps its shape: 100% = stock curve, 0% = prices untouched,
    // 200% = double markup. Result never drops below 1× (base price).
    private static float ScaleGoldMult(float mult, float scalePct) =>
        1f + (mult - 1f) * (Math.Max(0f, scalePct) / 100f);

    // Finds the Gold +% for a variant: exact band-label match first (that's what
    // generation stamps into tier_name), then boost-range containment. Null means
    // no explicit bump configured -> legacy budget curve.
    private static float? ResolveBandGoldBump(QuestBandDto[]? bands, string? tierLabel, float budgetPct)
    {
        if (bands == null || bands.Length == 0) return null;
        if (!string.IsNullOrEmpty(tierLabel))
            foreach (var b in bands)
                if (string.Equals(b.label, tierLabel, StringComparison.OrdinalIgnoreCase))
                    return b.goldBumpPct;
        foreach (var b in bands)
            if (budgetPct >= b.minBoostPct && budgetPct <= b.maxBoostPct)
                return b.goldBumpPct;
        return null;
    }

    // Per-band weapon DAMAGE bump (%), resolved like the gold bump: exact label
    // first, then boost-range containment. Null = the band left DPS +% blank, so
    // the weapon's damage is copied verbatim.
    private static float? ResolveBandDpsBump(QuestBandDto[]? bands, string? tierLabel, float budgetPct)
    {
        if (bands == null || bands.Length == 0) return null;
        if (!string.IsNullOrEmpty(tierLabel))
            foreach (var b in bands)
                if (string.Equals(b.label, tierLabel, StringComparison.OrdinalIgnoreCase))
                    return b.dpsBumpPct;
        foreach (var b in bands)
            if (budgetPct >= b.minBoostPct && budgetPct <= b.maxBoostPct)
                return b.dpsBumpPct;
        return null;
    }

    // Current melee DPS of a base item (weapons only): avg of all damage bands over
    // delay (ms). Returns isWeapon=false for non-weapons / zero-delay rows.
    private dynamic WeaponDpsInfo(dynamic item)
    {
        int itemClass = GetPropInt(item, "class");
        int delay = GetPropInt(item, "delay");
        if (itemClass != ITEM_CLASS_WEAPON || delay <= 0)
            return new { isWeapon = false, baseDps = 0f, delay = 0, twoHand = false };

        float avg = 0f;
        for (int i = 1; i <= 5; i++)
        {
            float lo = GetPropFloat(item, $"dmg_min{i}");
            float hi = GetPropFloat(item, $"dmg_max{i}");
            avg += (lo + hi) / 2f;
        }
        float dps = avg / (delay / 1000f);
        int invType = GetPropInt(item, "inventory_type");
        bool twoHand = invType == 17;   // INVTYPE_2HWEAPON
        return new { isWeapon = true, baseDps = (float)Math.Round(dps, 1), delay, twoHand };
    }

    private float GetGoldMultiplier(float budgetPct)
    {
        if (budgetPct >= 98) return 2.0f;
        if (budgetPct >= 90) return 1.8f;
        if (budgetPct >= 80) return 1.6f;
        float t = Math.Clamp(budgetPct / 79f, 0f, 1f);
        return 1.01f + t * 0.58f;
    }

    // BAND-DRIVEN generation (like the Crafting Lootifier). Instead of a single
    // budget ceiling + variant count spread across percentage tiers, the admin
    // configures explicit BANDS — each with a label/position, a min/max boost %,
    // and a slot count — and the tool rolls `slots` variants per band. Each roll's
    // boost % scales the ADDITIVE bonus (RollStats preserves the base lines and
    // layers baseBudget × boost% on top). Band labels drive the colour ladder
    // (Improved/of Power → blue, of Glory → purple, of the Gods → orange), and the
    // boost % is stored as budget_pct so the C++ weighted roll (105 − budget_pct)
    // keeps higher bands rarer. The quest-flavoured legendary is separate (added
    // per item in Commit when generateLegendary is set) and remains the rarest.
    private List<VariantData> GenerateVariants(dynamic baseItem, dynamic analysis, QuestRulesetDto ruleset)
    {
        var rng = new Random();

        float baseBudget = (float)analysis.weightedBudget;
        int[] presentTypes = (int[])analysis.presentStatTypes;
        string family = (string)analysis.detectedFamily;
        bool hasStats = (int)analysis.totalStats > 0;
        int quality = (int)analysis.itemQuality;
        bool whiteBase = quality <= 1;

        // Stat-less bases (white gear, or a spell-only item) have no stat budget to
        // layer onto, so derive one from item level — the crafting mint uses the
        // same source. The boost % then sizes the minted set.
        if (!hasStats)
            baseBudget = EstimateBudgetFromItemLevel(GetPropInt(baseItem, "item_level"));

        var eligible = new HashSet<int>(presentTypes);
        if (ruleset.allowNewAffixes)
            foreach (var s in STAT_FAMILIES.GetValueOrDefault(family, STAT_FAMILIES["hybrid"])) eligible.Add(s);
        var eligibleList = eligible.ToList();
        if (!hasStats) eligibleList = STAT_FAMILIES["hybrid"].ToList();

        var bands = (ruleset.bands != null && ruleset.bands.Length > 0)
            ? ruleset.bands.ToList()
            : DefaultBands(ruleset.includeGodsBand);

        // White cap (preserves the locked white ladder): a white reward never mints
        // purple/orange, so drop the of Glory and of the Gods bands — white keeps
        // only Improved / of Power (green).
        if (whiteBase)
            bands = bands.Where(b =>
            {
                var t = CanonicalTier(b.label ?? "", b.maxBoostPct);
                return t != "glory" && t != "gods";
            }).ToList();

        // Fingerprint of the BASE item's stat line — no band may mint a variant
        // identical to the item it replaces. This one stays global.
        string baseFp = hasStats ? BuildFingerprint(analysis) : "";
        var variants = new List<VariantData>();

        foreach (var band in bands)
        {
            // Dedup is scoped to THE BAND, not the whole item.
            //
            // This used to be a single item-wide HashSet keyed on the stat set
            // alone (BuildVariantFingerprint ignores the tier). So an "of Glory"
            // that happened to roll the same stat line as an already-minted
            // "Improved" was discarded as a duplicate — even though it is a
            // different item entirely: different name, different quality colour,
            // different gold value.
            //
            // On a low-item-level base the distinct roll space is tiny (few
            // eligible stats, small integer values, maxExtraStatSlots = 1), and
            // integer rounding collapses a 20% boost and a 45% boost onto the
            // same numbers. The cheap, high-slot bands therefore EXHAUSTED the
            // space and the expensive bands silently produced nothing — all 10
            // retries collided, `continue` fired, and the tier vanished.
            //
            // Bingles' Flying Gloves (ilvl 15) minted 5 Improved / 1 of Power /
            // 0 of Glory / 1 of the Gods against a configured 5 / 2 / 2 / 1 —
            // the purple tier disappeared completely, and with it the whole
            // reason the colour ladder exists.
            //
            // Two variants in the SAME band with identical stats are genuinely
            // redundant, so intra-band dedup is kept.
            var bandFingerprints = new HashSet<string>();
            if (!string.IsNullOrEmpty(baseFp)) bandFingerprints.Add(baseFp);

            int slots = Math.Max(0, band.slots);
            for (int s = 0; s < slots; s++)
            {
                var cand = RollBandVariant(rng, baseItem, analysis, hasStats, baseBudget, presentTypes, eligibleList, band, ruleset);

                var fp = BuildVariantFingerprint(cand);
                if (bandFingerprints.Contains(fp))
                {
                    bool found = false;
                    for (int retry = 0; retry < 10; retry++)
                    {
                        cand = RollBandVariant(rng, baseItem, analysis, hasStats, baseBudget, presentTypes, eligibleList, band, ruleset);
                        fp = BuildVariantFingerprint(cand);
                        if (!bandFingerprints.Contains(fp)) { found = true; break; }
                    }
                    // This band genuinely cannot produce another distinct roll —
                    // drop THIS slot only. Later bands are unaffected.
                    if (!found) continue;
                }

                bandFingerprints.Add(fp);
                variants.Add(cand);
            }
        }

        return variants.OrderBy(v => v.budgetPct).ToList();
    }

    // Roll one variant for a band: pick a boost % inside the band, turn it into an
    // additive total budget (base × (1 + boost%)) so RollStats layers the bonus on
    // top of the preserved base lines, and store the boost % as budgetPct.
    private VariantData RollBandVariant(Random rng, dynamic baseItem, dynamic analysis, bool hasStats,
        float baseBudget, int[] presentTypes, List<int> eligibleList, QuestBandDto band, QuestRulesetDto ruleset)
    {
        float span = Math.Max(0f, band.maxBoostPct - band.minBoostPct);
        float boostPct = band.minBoostPct + (float)rng.NextDouble() * span;

        float budgetRoll = baseBudget * (1f + boostPct / 100f);

        List<StatRoll> stats = hasStats
            ? RollStats(rng, budgetRoll, presentTypes, eligibleList, analysis, ruleset)
            : RollStatsForSpellItem(rng, budgetRoll, eligibleList);

        // High-rarity bases can't wear the low-tier names. Resolve the band's
        // displayed label/position from the base quality + the band's magnitude
        // tier (purple → of Fury/of Azeroth, legendary → Immortal).
        int baseQuality = (int)analysis.itemQuality;
        string canon = CanonicalTier(band.label ?? "", band.maxBoostPct);
        var (label, position) = ResolveBandNaming(baseQuality, canon, band.label ?? "", band.position ?? "suffix");

        string name = ApplyTierName((string)baseItem.name, label, position);
        return new VariantData
        {
            name = name,
            budgetPct = boostPct,
            tierLabel = label,
            tierPosition = position,
            stats = stats
        };
    }

    // Per-base-quality band naming. Low/plain names only fit low-rarity bases;
    // a purple or legendary reward can never carry an "Improved"/"of Power" name
    // (nor drop to their colour). Green/blue/white keep the configured labels.
    //   Purple base : Improved/of Power → "of Fury" (purple)
    //                 of Glory          → "of Glory" (purple)
    //                 of the Gods       → "of Azeroth" (the purple→legendary step, orange)
    //   Legendary   : every tier        → "Immortal" (prefix, orange) — it can only stay legendary
    private (string label, string position) ResolveBandNaming(int baseQuality, string canonicalTier,
        string defaultLabel, string defaultPosition)
    {
        if (baseQuality >= 5)
            return ("Immortal", "prefix");

        if (baseQuality == 4)
        {
            return canonicalTier switch
            {
                "improved" => ("of Fury", "suffix"),
                "power" => ("of Fury", "suffix"),
                "glory" => ("of Glory", "suffix"),
                "gods" => ("of Azeroth", "suffix"),
                _ => (defaultLabel, defaultPosition)
            };
        }

        return (defaultLabel, defaultPosition);
    }

    // Canonical tier token for a band, by name first (so any boost range is legal
    // under a recognized label) then by boost bucket for custom labels. Used for
    // the white-band cap and mirrors the Crafting Lootifier's mapping.
    private static string CanonicalTier(string label, float boostPct)
    {
        var l = (label ?? "").ToLowerInvariant();
        if (l.Contains("god") || l.Contains("legend") || l.Contains("immortal") || l.Contains("azeroth")) return "gods";
        if (l.Contains("glory") || l.Contains("fury")) return "glory";
        if (l.Contains("power")) return "power";
        if (l.Contains("improv")) return "improved";
        if (boostPct >= 40f) return "gods";
        if (boostPct >= 30f) return "glory";
        if (boostPct >= 20f) return "power";
        return "improved";
    }

    // Colour ladder shared by the preview and the DB write.
    //
    // The ladder is RELATIVE to the base item's quality, not absolute. The old
    // version hardcoded every non-glory/non-gods band to blue:
    //
    //     else laddered = whiteBase ? 2 : 3;   // blue
    //
    // which swept up BOTH "improved" and "power" — so a green quest reward could
    // not produce a green variant. Math.Max(2, 3) is blue, always. Five Improved
    // variants off a green base all came out blue, and the floor rule (never drop
    // below base) was quietly doubling as a promotion rule.
    //
    // Now each band is an OFFSET from the base:
    //
    //     improved → base + 0     (inherits the base's colour — it IS the base
    //                              item, marginally better; promoting it was the bug)
    //     power    → base + 1
    //     glory    → base + 2
    //     gods     → 5            (the only step that may reach orange)
    //
    // Non-legendary bands are capped at purple, so a blue reward can't mint an
    // orange "of Glory". The hard floor stays: a variant is NEVER rarer-down than
    // the item it replaces.
    //
    // Green base (2) now yields: improved=green, power=blue, glory=purple,
    // gods=orange, quest legendary=orange. Purple base (4) still collapses to
    // of Fury/of Glory (purple) + of Azeroth (orange) exactly as ResolveBandNaming
    // already names it, and a legendary base stays legendary throughout.
    //
    // Tier identification is delegated to CanonicalTier (label first, boost bucket
    // as fallback), so the 150%-budget quest legendary and any custom band label
    // resolve the same way here as they do at naming time.
    private int VariantQualityForTier(string tierLabel, float budgetPct, int baseQuality)
    {
        bool whiteBase = baseQuality <= 1;
        string canon = CanonicalTier(tierLabel ?? "", budgetPct);

        // White/grey bases have no meaningful colour to inherit, so they keep the
        // legacy fixed ladder (white shifts one step down at every rung).
        if (whiteBase)
        {
            int w = canon switch
            {
                "gods" => 4,        // purple
                "glory" => 3,       // blue
                _ => 2,             // green (improved + power)
            };
            return Math.Max(baseQuality, w);
        }

        int laddered = canon switch
        {
            "gods" => 5,                // orange — the legendary step, the only rung that reaches it
            "glory" => 4,               // purple floor  (unchanged)
            "power" => 3,               // blue floor    (unchanged)
            _ => baseQuality,           // improved: INHERIT the base — this is the fix
        };

        return Math.Clamp(Math.Max(baseQuality, laddered), 0, 5);
    }

    // Default band ladder — mirrors the Crafting Lootifier's defaults so the two
    // tools feel identical. "of the Gods" (orange) is optional via includeGodsBand;
    // the quest-flavoured legendary is added separately in Commit.
    private QuestRulesetDto DefaultRuleset() => new QuestRulesetDto
    {
        allowNewAffixes = true,
        maxAffixCountChange = 1,
        existingBumpBias = QUEST_BUMP_BIAS,
        generateLegendary = true,
        includeGodsBand = true,
        goldValueScalePct = 100f,
        bands = DefaultBands(true).ToArray()
    };

    private List<QuestBandDto> DefaultBands(bool includeGods)
    {
        // dpsBumpPct: default DAMAGE bump per tier, from the vanilla same-level
        // quality deltas — green is "just better" (no true analog), blue +10.5%,
        // purple +21.5%, legendary a nominal +30% over the green line.
        var bands = new List<QuestBandDto>
        {
            new() { label = "Improved", position = "prefix", minBoostPct = 10f, maxBoostPct = 20f, slots = 5, goldBumpPct = 25f, dpsBumpPct = 8f },
            new() { label = "of Power", position = "suffix", minBoostPct = 20f, maxBoostPct = 30f, slots = 2, goldBumpPct = 50f, dpsBumpPct = 10.5f },
            new() { label = "of Glory", position = "suffix", minBoostPct = 30f, maxBoostPct = 40f, slots = 2, goldBumpPct = 100f, dpsBumpPct = 21.5f },
        };
        if (includeGods)
            bands.Add(new() { label = "of the Gods", position = "suffix", minBoostPct = 40f, maxBoostPct = 60f, slots = 1, goldBumpPct = 200f, dpsBumpPct = 30f });
        return bands;
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

    // Statful bases: ADDITIVE ONLY (crafting-lootifier rule). Preserve every base
    // stat line verbatim, then layer a bonus on top — never reduced, never dropped.
    // The bonus size is this roll's headroom above the base budget (budgetRoll is
    // the total target for the tier; GenerateVariants floors it at base×1.02, the
    // legendary path uses base×1.5), floored at MIN_DELTA_BUDGET so even the lowest
    // tier is a real upgrade over the plain reward. The bonus is split between
    // bumping the existing lines and adding new affixes, gated by allowNewAffixes /
    // maxAffixCountChange / open slots. `presentTypes` is retained for signature
    // parity with the other call sites; base lines now come from analysis.stats.
    private List<StatRoll> RollStats(Random rng, float budgetRoll, int[] presentTypes,
        List<int> eligibleList, dynamic analysis, QuestRulesetDto ruleset)
    {
        float baseWeighted = (float)analysis.weightedBudget;

        // Seed the variant with the base reward's exact stat lines (preserved).
        var lines = new Dictionary<int, int>();
        foreach (var s in (List<object>)analysis.stats)
        {
            int st = (int)((dynamic)s).statType;
            int sv = (int)((dynamic)s).statValue;
            if (st > 0 && sv != 0) lines[st] = sv;
        }

        // Additive bonus = the roll's budget above what the base already occupies,
        // floored so the worst variant still upgrades the reward.
        float delta = Math.Max(MIN_DELTA_BUDGET, budgetRoll - baseWeighted);

        int slotRoom = Math.Max(0, 10 - lines.Count);
        var newCandidates = eligibleList.Where(t => !lines.ContainsKey(t)).ToList();
        bool canAddNew = ruleset.allowNewAffixes && slotRoom > 0 && newCandidates.Count > 0;

        float split = (float)Math.Clamp(ruleset.existingBumpBias + (rng.NextDouble() * 0.4 - 0.2), 0.0, 1.0);
        if (lines.Count == 0) split = 0f;   // no base lines to bump → all bonus goes to new affixes
        if (!canAddNew) split = 1f;         // no room/permission for new affixes → all bonus to bumps

        float existingPortion = delta * split;
        float newPortion = delta - existingPortion;

        // Bump the preserved lines (weight-distributed).
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

        // Add new affixes with the remaining bonus, or (if none allowed) pour the
        // whole delta back into the existing lines so the tier budget still lands.
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

    private string BuildFingerprint(dynamic analysis)
    {
        var stats = (List<object>)analysis.stats;
        return string.Join("|", stats.Select(s => $"{((dynamic)s).statType}:{((dynamic)s).statValue}").OrderBy(x => x));
    }

    private string BuildVariantFingerprint(VariantData v) =>
        string.Join("|", v.stats.Select(s => $"{s.statType}:{s.statValue}").OrderBy(x => x));

    private string ApplyTierName(string baseName, string tierLabel, string tierPosition)
    {
        if (string.IsNullOrEmpty(tierLabel)) return baseName;
        return tierPosition == "prefix" ? tierLabel + " " + baseName : baseName + " " + tierLabel;
    }

    private List<object> VariantsToJson(List<VariantData> variants, int baseQuality,
        bool isWeapon = false, float baseDps = 0f, QuestRulesetDto? ruleset = null) =>
        variants.Select((v, idx) =>
        {
            float? dpsBump = null;
            double? dps = null;
            if (isWeapon)
            {
                float b = ResolveBandDpsBump(ruleset?.bands, v.tierLabel, v.budgetPct) ?? 0f;
                dpsBump = b;
                dps = Math.Round(baseDps * (1.0 + b / 100.0), 1);
            }
            return (object)new
            {
                variantIndex = idx,
                name = v.name,
                budgetPct = Math.Round(v.budgetPct, 1),
                tierLabel = v.tierLabel,
                tierPosition = v.tierPosition,
                quality = VariantQualityForTier(v.tierLabel ?? "", v.budgetPct, baseQuality),
                dpsBumpPct = dpsBump,
                dps,
                stats = v.stats.Select(s => (object)new { s.statType, s.statValue, s.name }).ToList()
            };
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

    private float GetPropFloat(dynamic obj, string name)
    {
        var dict = obj as IDictionary<string, object>;
        if (dict != null && dict.TryGetValue(name, out var val))
            return val == null ? 0f : Convert.ToSingle(val);
        return 0f;
    }

    /// <summary>
    /// Central eligibility rule for a quest reward item, given its analysis.
    /// Must be equippable gear (Weapon/Armor in a slot) and NOT grey (quality 0).
    /// Green+ items need existing stats; WHITE (quality 1) items are allowed even
    /// with no stats — they get stats added, but only up to the green/blue tier.
    /// </summary>
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

}

// ══════════════════════════════════════════════════════════════
//  DTOs specific to the quest controller.
//  The variant-engine types (SpellEffectInfo, VariantData, StatRoll) and the
//  commit DTOs (CommitRoll, CommitStat) are declared in LootifierController.cs
//  and shared here. Tier selection is BAND-BASED (QuestRulesetDto/QuestBandDto,
//  mirroring the Crafting Lootifier) rather than the shared RulesetDto's
//  budget-ceiling + naming-tier model.
// ══════════════════════════════════════════════════════════════

// Band-based ruleset (twin of CraftingRulesetDto). Each band names a tier and
// carries a min/max additive boost % plus a slot count.
public class QuestRulesetDto
{
    public bool allowNewAffixes { get; set; } = true;
    public int maxAffixCountChange { get; set; } = 1;
    public float existingBumpBias { get; set; } = 0.5f;   // delta split: existing-line bumps vs new affixes
    public bool generateLegendary { get; set; } = true;   // quest-flavoured legendary, one per reward item
    public bool includeGodsBand { get; set; } = true;     // whether the default set offers an "of the Gods" band
    public bool rerollIncludeLegendary { get; set; } = false; // on regen, may an owned copy reroll INTO the legendary?
    public float goldValueScalePct { get; set; } = 100f;  // master scale on all gold bumps: 100 = as entered, 0 = prices untouched, 200 = double
    public float legendaryGoldBumpPct { get; set; } = 500f; // quest legendary price bump above base (%); 500 = the old x6 stock behavior
    public float legendaryDpsBumpPct { get; set; } = 30f;   // quest legendary weapon DAMAGE bump above base (%); nominal — vanilla legendaries were hand-tuned
    public QuestBandDto[]? bands { get; set; }
}

public class QuestBandDto
{
    public string label { get; set; } = "";
    public string position { get; set; } = "suffix";     // "prefix" or "suffix"
    public float minBoostPct { get; set; }
    public float maxBoostPct { get; set; }
    public int slots { get; set; } = 1;                   // variants to roll in this band
    public float? goldBumpPct { get; set; }               // price bump above base (%); null = legacy budget curve
    public float? dpsBumpPct { get; set; }                // weapon DAMAGE bump above base (%) for this tier; null = damage copied verbatim
}

public class QuestGenerateRequest
{
    public int[]? itemEntries { get; set; }
    public QuestRulesetDto? ruleset { get; set; }
}

public class QuestCommitRequest
{
    public bool allQuests { get; set; } = false;
    public int[]? itemEntries { get; set; }
    public bool regenerate { get; set; } = false;
    public QuestRulesetDto? ruleset { get; set; }
}

public class QuestRollbackRequest
{
    public int baseEntry { get; set; } = 0; // 0 = all quest variants
}

public class QuestTierBumpDto
{
    public string tier { get; set; } = "";      // tier_name exactly as stored in lootifier_generated_items
    public float? goldBumpPct { get; set; }     // null / omitted = leave this tier's prices ALONE
}

public class QuestRevalueRequest
{
    public QuestTierBumpDto[]? tiers { get; set; }
}