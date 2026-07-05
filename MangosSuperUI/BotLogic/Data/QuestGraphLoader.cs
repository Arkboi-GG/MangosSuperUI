using MangosSuperUI.Models;
using Dapper;
using MangosSuperUI.Services;

namespace MangosSuperUI.BotLogic.Data;

/// <summary>
/// Loads the entire vanilla quest dependency graph from the mangos DB at startup.
/// Replaces LevelingGuideLoader — no static JSON, no manual guide authoring.
/// Every quest is a node; edges are prerequisite chains, exclusive groups, and breadcrumbs.
/// The bot's race, class, and level filter the graph to reachable quests at runtime.
///
/// All data is loaded once and held in memory (~4,700 quests — trivial footprint).
/// No per-bot queries needed.
///
/// KEY FIX (April 18, 2026): Kill target spawn positions are now resolved per-quest,
/// scoped to spawns near the quest giver. Before this fix, "Wolves Across the Border"
/// averaged ALL Young Wolf spawns across Elwynn Forest, producing a grind center 5,000+
/// yards from Northshire. Now it only averages the spawns within 500 yards of Eagan
/// Peltskinner, producing a correct ~60 yard grind center.
///
/// SESSION 19 FIX: PrevQuests reverse edge building. VMaNGOS builds quest prerequisite
/// lists from TWO sources: (1) PrevQuestId on the quest itself, and (2) reverse
/// NextQuestId edges — when quest A has NextQuestId=B, quest B gets A added to its
/// prevQuests list. Without this, quests like 3903 "Milly Osworth" (which has
/// PrevQuestId=0 but is referenced by quest 18 and 33 via NextQuestId) appeared
/// eligible before their real prerequisites were met, causing C++ CanTakeQuest to
/// reject them with requirements_not_met. See ObjectMgr.cpp lines 5925-5947.
/// </summary>
public class QuestGraphLoader
{
    private readonly ConnectionFactory _db;
    private readonly ILogger<QuestGraphLoader> _logger;

    private Dictionary<int, QuestNode> _quests = new();
    private bool _loaded;

    public QuestGraphLoader(ConnectionFactory db, ILogger<QuestGraphLoader> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>All quests, keyed by quest_id.</summary>
    public IReadOnlyDictionary<int, QuestNode> AllQuests => _quests;

    /// <summary>Whether the graph has been loaded successfully.</summary>
    public bool IsLoaded => _loaded;

    /// <summary>Get a specific quest by ID.</summary>
    public QuestNode? GetQuest(int questId) =>
        _quests.TryGetValue(questId, out var q) ? q : null;

    /// <summary>
    /// Get all quests a bot can currently accept given their race, class, level,
    /// and set of completed quest IDs. Respects PrevQuests chains (including
    /// reverse NextQuestId edges), ExclusiveGroup, race/class masks, and MinLevel.
    /// </summary>
    public List<QuestNode> GetAvailableQuests(int raceBit, int classBit, int level,
        HashSet<int> completedQuestIds, HashSet<int>? activeQuestIds = null)
    {
        var results = new List<QuestNode>();
        var active = activeQuestIds ?? new HashSet<int>();

        foreach (var quest in _quests.Values)
        {
            // Skip already completed
            if (completedQuestIds.Contains(quest.QuestId))
                continue;

            // Skip quests already in the bot's quest log (active or complete-not-turned-in)
            if (active.Contains(quest.QuestId))
                continue;

            // Level gate
            if (quest.MinLevel > level)
                continue;

            // Race/class masks
            if (!quest.IsAvailableToRace(raceBit))
                continue;
            if (!quest.IsAvailableToClass(classBit))
                continue;

            // PrevQuests check — mirrors VMaNGOS SatisfyQuestPreviousQuest.
            // The list is built from PrevQuestId (direct) + reverse NextQuestId edges.
            // Logic: if there are any positive entries (must be rewarded), at least
            // one of them must be in completedQuestIds. Negative entries mean "must
            // be active/in-progress" — we skip that check here since C++ will catch it.
            if (quest.PrevQuests.Count > 0)
            {
                var positivePrereqs = quest.PrevQuests.Where(pq => pq > 0).ToList();
                if (positivePrereqs.Count > 0)
                {
                    // VMaNGOS SatisfyQuestPreviousQuest: a positive prereq satisfies the gate when it is
                    // REWARDED -- but if that prereq belongs to a NEGATIVE ExclusiveGroup ("do all"), every
                    // OTHER quest sharing that group must ALSO be rewarded. Group -18 = {18, 33}: completing
                    // only #18 does NOT unlock its follow-ups (#6 needs #18; #3903 needs #18 AND #33) -- the
                    // server rejects them requirements_not_met until #33 is also turned in. The old code OR'd
                    // the prereqs and ignored the group, so it offered the follow-ups a turn-in early ->
                    // re-accept-fail -> DeferPick -> grind-lock. (Negative prereq entries "must be active"
                    // are still left to C++ CanTakeQuest below, as before.)
                    bool satisfied = false;
                    foreach (var pq in positivePrereqs)
                    {
                        if (!completedQuestIds.Contains(pq))
                            continue;                                  // this prereq not rewarded yet
                        if (_quests.TryGetValue(pq, out var prevNode) && prevNode.ExclusiveGroup < 0)
                        {
                            int grp = prevNode.ExclusiveGroup;
                            bool allSiblingsRewarded = _quests.Values
                                .Where(o => o.ExclusiveGroup == grp)
                                .All(o => completedQuestIds.Contains(o.QuestId));
                            if (!allSiblingsRewarded)
                                continue;                              // a "do-all" group sibling still owed
                        }
                        satisfied = true;
                        break;
                    }
                    if (!satisfied)
                        continue;
                }
                // Negative entries (must be active) — we don't have the active quest
                // set here, so let C++ CanTakeQuest handle those cases.
            }

            // PrevChain check — mirrors VMaNGOS Player::SatisfyQuestPrevChain (Player.cpp
            // 13741-13760). SEPARATE from the PrevQuests gate above and STRICT AND: every quest in
            // PrevChainQuests (built from NextQuestInChain reverse edges) must be REWARDED before this
            // quest unlocks. Unlike PrevQuests' one-from-all, a single rewarded predecessor is NOT
            // enough — the whole prev-chain must be done, exactly as the server enforces. This is the
            // gate that keeps 5624 "Garments of the Light" hidden until 5623 "In Favor of the Light"
            // is turned in, instead of the god bot stamping an accept the server refuses forever.
            if (quest.PrevChainQuests.Count > 0
                && !quest.PrevChainQuests.All(completedQuestIds.Contains))
                continue;

            // ExclusiveGroup check — sign determines behavior:
            //   Positive: "pick one" — only one quest from this group can be active/completed.
            //             Block if any sibling is already active or completed.
            //   Negative: "do all" — all quests in this group must be completed before the
            //             follow-up (NextQuestId target) unlocks. No exclusion needed here;
            //             VMaNGOS checks the "all must be done" condition on the follow-up
            //             quest's SatisfyQuestPreviousQuest, not on accept of the individual.
            //   Session 29 fix: was treating ALL ExclusiveGroup values as "pick one",
            //   which blocked bots from having quest 18 AND 33 active simultaneously
            //   (they share ExclusiveGroup=-18, meaning both must be done for quest 3903).
            if (quest.ExclusiveGroup > 0)
            {
                bool groupConflict = _quests.Values.Any(other =>
                    other.QuestId != quest.QuestId &&
                    other.ExclusiveGroup == quest.ExclusiveGroup &&
                    (completedQuestIds.Contains(other.QuestId) ||
                     active.Contains(other.QuestId)));
                if (groupConflict)
                    continue;
            }

            // Must have a giver NPC (skip quests with no known giver)
            if (quest.Giver == null)
                continue;

            // Skip junk/test quests (no title, or <UNUSED>/<nyi>)
            if (string.IsNullOrEmpty(quest.Title) ||
                quest.Title.StartsWith("<UNUSED>") ||
                quest.Title.StartsWith("<nyi>") ||
                quest.Title.StartsWith("<TXT>") ||
                quest.Title.StartsWith("<TEST>"))
                continue;

            results.Add(quest);
        }

        return results;
    }

    // ── Startup Load ──────────────────────────────────────────────────────

    /// <summary>
    /// Load all quest data from the mangos DB. Call once at startup.
    /// </summary>
    public async Task LoadAsync()
    {
        _logger.LogInformation("QuestGraphLoader: loading quest graph from mangos DB...");
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            using var conn = _db.Mangos();

            // Step 1: Load all quest nodes
            var quests = await LoadQuestNodesAsync(conn);

            // Step 2: Load quest givers
            await LoadQuestGiversAsync(conn, quests);

            // Step 3: Load quest turn-ins
            await LoadQuestTurnInsAsync(conn, quests);

            // Step 4: Load ALL creature spawn positions for kill target creatures
            //         (raw spawns, not aggregated — we aggregate per-quest below)
            var allCreatureSpawns = await LoadCreatureSpawnPositionsAsync(conn, quests);

            // Step 5: Resolve kill target grind centers per-quest, scoped to giver proximity
            ResolveKillTargetsPerQuest(quests, allCreatureSpawns);

            // Step 5b (Fix 5, 2026-07-04): flag kill objectives whose target creature is FRIENDLY
            //          to the quest's eligible side. A "kill" on a friendly NPC can never be landed
            //          by a bot (real credit is a scripted spellcast/event), so downstream planning
            //          must classify the quest unworkable instead of driving it forever (the 5624
            //          "Garments of the Light" landmine: group A ground friendly John Turner's camp
            //          for 10h on 2026-07-04 behind an unsatisfiable union gate).
            await FlagFriendlyKillTargetsAsync(conn, quests);

            // Step 6: Load item drop sources
            await LoadItemDropSourcesAsync(conn, quests);

            // Step 6b: Load gameobject drop sources (for items not dropped by creatures)
            await LoadGameObjectDropSourcesAsync(conn, quests);

            // Step 7: Load item names
            await LoadItemNamesAsync(conn, quests);

            // Step 8: Build PrevQuests lists (mirrors VMaNGOS ObjectMgr prevQuests)
            // Two sources: (a) PrevQuestId on this quest, (b) reverse NextQuestId edges
            BuildPrevQuestsLists(quests);

            _quests = quests;
            _loaded = true;

            sw.Stop();

            // Log summary stats
            int withGiver = quests.Values.Count(q => q.Giver != null);
            int withTurnIn = quests.Values.Count(q => q.TurnIn != null);
            int withKillObj = quests.Values.Count(q => q.HasKillObjectives);
            int unworkable = quests.Values.Count(q => q.Objectives.Any(o => o.TargetFriendly));
            int withItemObj = quests.Values.Count(q => q.HasItemObjectives);
            int withGoObj = quests.Values.Count(q => q.ItemObjectives.Any(i => i.BestGoSource != null));
            int withPrereq = quests.Values.Count(q => q.PrevQuests.Count > 0);

            _logger.LogInformation(
                "QuestGraphLoader: loaded {Total} quests in {Ms}ms — " +
                "givers={Givers}, turnins={TurnIns}, kill_obj={Kill}, item_obj={Item}, go_obj={GO}, prereqs={Prereq}, friendly_target_unworkable={Unw}",
                quests.Count, sw.ElapsedMilliseconds,
                withGiver, withTurnIn, withKillObj, withItemObj, withGoObj, withPrereq, unworkable);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "QuestGraphLoader: failed to load quest graph");
            _loaded = false;
        }
    }

    // ── Step 5b: friendly-target kill objectives (Fix 5, 2026-07-04) ─────────
    //
    // A ReqCreatureOrGOId slot passes IsCreature for ANY positive entry — including creatures the
    // quest's own takers can never attack (friendly civilians whose quest credit is a scripted
    // spellcast, e.g. 12423 John Turner for 5624). Resolve each kill-target creature's faction
    // masks and flag QuestObjective.TargetFriendly when the target is friendly (and not hostile)
    // to the quest's eligible faction side. Side derivation: quest RaceMask -> Alliance-only /
    // Horde-only / both; faction group bits are the standard 1=Players, 2=Alliance, 4=Horde.
    // The player bit is always included so "friendly to all players" civilians flag regardless of
    // side. Conservative on purpose: a quest is only flagged when its takers' side is in the
    // friend mask AND NOT in the hostile mask — attackable-neutral mobs (friend=0) and genuinely
    // cross-faction kill targets (hostile to the taking side) are never flagged. Wrapped in its
    // own try/catch: a schema mismatch degrades to "no flags" (today's behavior), never a failed
    // graph load. Every flagged quest is logged individually so a misfire is visible, not silent.
    private async Task FlagFriendlyKillTargetsAsync(System.Data.IDbConnection conn, Dictionary<int, QuestNode> quests)
    {
        const int PlayerBit = 1, AllianceBit = 2, HordeBit = 4;
        const int AllianceRaces = 1 | 4 | 8 | 64;      // human | dwarf | night elf | gnome
        const int HordeRaces = 2 | 16 | 32 | 128;      // orc | undead | tauren | troll

        try
        {
            var creatureEntries = quests.Values
                .SelectMany(q => q.Objectives)
                .Where(o => o.IsCreature)
                .Select(o => o.CreatureEntry)
                .Distinct()
                .ToList();
            if (creatureEntries.Count == 0) return;

            // ORDER BY ft.build so the dictionary's last-write-wins keeps the HIGHEST build's masks
            // (faction_template rows are patch-versioned, mirroring the ct.patch convention).
            // Column names verified against the live vmangos schema 2026-07-04 (SHOW COLUMNS FROM
            // faction_template): it is friendly_mask / hostile_mask, NOT friend_mask — the first
            // deploy queried friend_mask, threw Unknown column, and the try/catch below degraded to
            // zero flags exactly as designed (friendly_target_unworkable=0 in the summary line, 5624
            // dispatched again, caught only by the path gate). The per-faction friend_faction1-4 /
            // enemy_faction4 lists are deliberately NOT consulted — group masks cover the
            // guard/civilian class this exists for, and the per-quest Information log below makes any
            // list-only miss visible rather than silent.
            var rows = await conn.QueryAsync<dynamic>(@"
                SELECT ct.entry AS creature_entry, ft.hostile_mask, ft.friendly_mask
                FROM creature_template ct
                JOIN faction_template ft ON ft.id = ct.faction
                WHERE ct.patch = 0 AND ct.entry IN @Entries
                ORDER BY ft.build",
                new { Entries = creatureEntries });

            var masks = new Dictionary<int, (int Hostile, int Friend)>();
            foreach (var r in rows)
                masks[(int)r.creature_entry] = ((int)(long)Convert.ToInt64(r.hostile_mask),
                                                (int)(long)Convert.ToInt64(r.friendly_mask));

            int flagged = 0;
            foreach (var q in quests.Values)
            {
                if (q.Objectives.Length == 0) continue;
                bool alli = q.RaceMask == 0 || (q.RaceMask & AllianceRaces) != 0;
                bool horde = q.RaceMask == 0 || (q.RaceMask & HordeRaces) != 0;
                int side = PlayerBit | (alli ? AllianceBit : 0) | (horde ? HordeBit : 0);

                foreach (var o in q.Objectives)
                {
                    if (!o.IsCreature) continue;
                    if (!masks.TryGetValue(o.CreatureEntry, out var m)) continue;
                    if ((m.Friend & side) != 0 && (m.Hostile & side) == 0)
                    {
                        o.TargetFriendly = true;
                        flagged++;
                        _logger.LogInformation(
                            "QuestGraphLoader: quest [{Id}] \"{Title}\" kill-target {Entry} is FRIENDLY to its takers (friend={F} hostile={H} side={S}) — flagged unworkable",
                            q.QuestId, q.Title, o.CreatureEntry, m.Friend, m.Hostile, side);
                    }
                }
            }
            if (flagged > 0)
                _logger.LogInformation("QuestGraphLoader: flagged {N} friendly-target kill objective(s) as unworkable", flagged);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "QuestGraphLoader: friendly-target flagging failed (schema mismatch?) — continuing without flags");
        }
    }

    // ── Query 1: Quest Nodes ──────────────────────────────────────────────

    private async Task<Dictionary<int, QuestNode>> LoadQuestNodesAsync(System.Data.IDbConnection conn)
    {
        var rows = await conn.QueryAsync<dynamic>(@"
            SELECT
                entry, Title, QuestLevel, MinLevel, ZoneOrSort,
                RequiredRaces, RequiredClasses, QuestFlags, SpecialFlags,
                PrevQuestId, NextQuestId, NextQuestInChain, ExclusiveGroup, BreadcrumbForQuestId,
                ReqCreatureOrGOId1, ReqCreatureOrGOCount1,
                ReqCreatureOrGOId2, ReqCreatureOrGOCount2,
                ReqCreatureOrGOId3, ReqCreatureOrGOCount3,
                ReqCreatureOrGOId4, ReqCreatureOrGOCount4,
                ReqItemId1, ReqItemCount1,
                ReqItemId2, ReqItemCount2,
                ReqItemId3, ReqItemCount3,
                ReqItemId4, ReqItemCount4,
                SrcItemId, RewXP, RewOrReqMoney,
                RewChoiceItemId1, RewChoiceItemId2, RewChoiceItemId3,
                RewChoiceItemId4, RewChoiceItemId5, RewChoiceItemId6,
                RewItemId1, RewItemId2, RewItemId3, RewItemId4
            FROM quest_template
            WHERE patch = 0");

        var quests = new Dictionary<int, QuestNode>();

        foreach (var r in rows)
        {
            int id = (int)r.entry;
            var node = new QuestNode
            {
                QuestId = id,
                Title = (string)(r.Title ?? ""),
                QuestLevel = (int)r.QuestLevel,
                MinLevel = (int)r.MinLevel,
                ZoneId = (int)r.ZoneOrSort,
                RaceMask = (int)r.RequiredRaces,
                ClassMask = (int)r.RequiredClasses,
                QuestFlags = (int)r.QuestFlags,
                SpecialFlags = (int)r.SpecialFlags,
                PrevQuestId = (int)r.PrevQuestId,
                NextQuestId = (int)r.NextQuestId,
                NextQuestInChain = (int)r.NextQuestInChain,
                ExclusiveGroup = (int)r.ExclusiveGroup,
                BreadcrumbForQuestId = (int)r.BreadcrumbForQuestId,
                SrcItemId = (int)r.SrcItemId,
                RewXP = (int)r.RewXP,
                RewMoney = (int)r.RewOrReqMoney,
                RewChoiceItemId1 = (int)r.RewChoiceItemId1,
                RewChoiceItemId2 = (int)r.RewChoiceItemId2,
                RewChoiceItemId3 = (int)r.RewChoiceItemId3,
                RewChoiceItemId4 = (int)r.RewChoiceItemId4,
                RewChoiceItemId5 = (int)r.RewChoiceItemId5,
                RewChoiceItemId6 = (int)r.RewChoiceItemId6,
                RewItemId1 = (int)r.RewItemId1,
                RewItemId2 = (int)r.RewItemId2,
                RewItemId3 = (int)r.RewItemId3,
                RewItemId4 = (int)r.RewItemId4
            };

            // Build kill/interact objectives (slots 1-4)
            var objectives = new List<QuestObjective>();
            AddObjective(objectives, 1, (int)r.ReqCreatureOrGOId1, (int)r.ReqCreatureOrGOCount1);
            AddObjective(objectives, 2, (int)r.ReqCreatureOrGOId2, (int)r.ReqCreatureOrGOCount2);
            AddObjective(objectives, 3, (int)r.ReqCreatureOrGOId3, (int)r.ReqCreatureOrGOCount3);
            AddObjective(objectives, 4, (int)r.ReqCreatureOrGOId4, (int)r.ReqCreatureOrGOCount4);
            node.Objectives = objectives.ToArray();

            // Build item objectives (slots 1-4)
            var items = new List<QuestItemReq>();
            AddItemReq(items, 1, (int)r.ReqItemId1, (int)r.ReqItemCount1);
            AddItemReq(items, 2, (int)r.ReqItemId2, (int)r.ReqItemCount2);
            AddItemReq(items, 3, (int)r.ReqItemId3, (int)r.ReqItemCount3);
            AddItemReq(items, 4, (int)r.ReqItemId4, (int)r.ReqItemCount4);
            node.ItemObjectives = items.ToArray();

            quests[id] = node;
        }

        _logger.LogInformation("QuestGraphLoader: loaded {Count} quest nodes", quests.Count);
        return quests;
    }

    private static void AddObjective(List<QuestObjective> list, int slot, int creatureOrGO, int count)
    {
        if (creatureOrGO != 0 && count > 0)
            list.Add(new QuestObjective { Slot = slot, CreatureOrGOId = creatureOrGO, Count = count });
    }

    private static void AddItemReq(List<QuestItemReq> list, int slot, int itemId, int count)
    {
        if (itemId > 0 && count > 0)
            list.Add(new QuestItemReq { Slot = slot, ItemId = itemId, Count = count });
    }

    // ── Query 2: Quest Givers ─────────────────────────────────────────────

    private async Task LoadQuestGiversAsync(System.Data.IDbConnection conn, Dictionary<int, QuestNode> quests)
    {
        var rows = await conn.QueryAsync<dynamic>(@"
            SELECT
                cqr.quest, cqr.id AS npc_entry,
                ct.name,
                c.position_x, c.position_y, c.position_z, c.map
            FROM creature_questrelation cqr
            JOIN creature_template ct ON ct.entry = cqr.id AND ct.patch = 0
            JOIN creature c ON c.id = cqr.id
            GROUP BY cqr.quest, cqr.id");

        int resolved = 0;
        foreach (var r in rows)
        {
            int questId = (int)r.quest;
            if (quests.TryGetValue(questId, out var quest) && quest.Giver == null)
            {
                quest.Giver = new QuestNpcLocation
                {
                    NpcEntry = (int)r.npc_entry,
                    Name = (string)(r.name ?? ""),
                    X = (float)r.position_x,
                    Y = (float)r.position_y,
                    Z = (float)r.position_z,
                    Map = (int)r.map
                };
                resolved++;
            }
        }

        _logger.LogInformation("QuestGraphLoader: resolved {Count} quest givers", resolved);
    }

    // ── Query 3: Quest Turn-ins ───────────────────────────────────────────

    private async Task LoadQuestTurnInsAsync(System.Data.IDbConnection conn, Dictionary<int, QuestNode> quests)
    {
        var rows = await conn.QueryAsync<dynamic>(@"
            SELECT
                cir.quest, cir.id AS npc_entry,
                ct.name,
                c.position_x, c.position_y, c.position_z, c.map
            FROM creature_involvedrelation cir
            JOIN creature_template ct ON ct.entry = cir.id AND ct.patch = 0
            JOIN creature c ON c.id = cir.id
            GROUP BY cir.quest, cir.id");

        int resolved = 0;
        foreach (var r in rows)
        {
            int questId = (int)r.quest;
            if (quests.TryGetValue(questId, out var quest) && quest.TurnIn == null)
            {
                quest.TurnIn = new QuestNpcLocation
                {
                    NpcEntry = (int)r.npc_entry,
                    Name = (string)(r.name ?? ""),
                    X = (float)r.position_x,
                    Y = (float)r.position_y,
                    Z = (float)r.position_z,
                    Map = (int)r.map
                };
                resolved++;
            }
        }

        _logger.LogInformation("QuestGraphLoader: resolved {Count} quest turn-ins", resolved);
    }

    // ── Query 4: Load ALL creature spawn positions (raw, not aggregated) ──

    /// <summary>
    /// Load individual spawn positions for every creature entry referenced in kill
    /// objectives. Returns a dictionary: creature_entry → list of (map, x, y, z).
    /// We do NOT aggregate here — aggregation happens per-quest in ResolveKillTargetsPerQuest
    /// so we can scope the grind center to spawns near each quest's giver.
    /// </summary>
    private async Task<Dictionary<int, List<CreatureSpawn>>> LoadCreatureSpawnPositionsAsync(
        System.Data.IDbConnection conn, Dictionary<int, QuestNode> quests)
    {
        // Collect all creature entries referenced in kill objectives
        var creatureEntries = quests.Values
            .SelectMany(q => q.Objectives)
            .Where(o => o.IsCreature)
            .Select(o => o.CreatureEntry)
            .Distinct()
            .ToList();

        if (creatureEntries.Count == 0) return new();

        // Load individual spawn positions + creature names
        var rows = await conn.QueryAsync<dynamic>(@"
            SELECT
                c.id AS creature_entry,
                ct.name,
                c.map,
                c.position_x,
                c.position_y,
                c.position_z
            FROM creature c
            JOIN creature_template ct ON ct.entry = c.id AND ct.patch = 0
            WHERE c.id IN @Entries",
            new { Entries = creatureEntries });

        var result = new Dictionary<int, List<CreatureSpawn>>();

        foreach (var r in rows)
        {
            int entry = (int)r.creature_entry;

            if (!result.TryGetValue(entry, out var list))
            {
                list = new List<CreatureSpawn>();
                result[entry] = list;
            }

            list.Add(new CreatureSpawn
            {
                Name = (string)(r.name ?? ""),
                Map = (int)r.map,
                X = (float)r.position_x,
                Y = (float)r.position_y,
                Z = (float)r.position_z
            });
        }

        _logger.LogInformation("QuestGraphLoader: loaded {Spawns} individual spawn positions for {Creatures} kill target creatures",
            result.Values.Sum(l => l.Count), result.Count);

        return result;
    }

    // ── Step 5: Resolve kill target grind centers PER QUEST ───────────────

    /// <summary>
    /// For each quest's kill objectives, compute the grind center from the
    /// NEAREST CLUSTER of spawns to the quest giver — not from all spawns
    /// on the continent, and not even from all spawns within 500 yards.
    ///
    /// "Wolves Across the Border" giver = Eagan Peltskinner at (-8869,-163).
    /// Young Wolf (entry 299) spawns across all of Elwynn Forest.
    /// Old code: AVG(all spawns on map) = somewhere in central Elwynn (~5,000yd away).
    /// New code: sort spawns by distance from Eagan, take the nearest cluster
    /// (within 150yd of the closest wolf), average those → ~30yd from Eagan.
    ///
    /// This models real player behavior: you kill the first wolf you see near
    /// the quest giver, then work outward. You don't trek to the statistical
    /// centroid of all wolves on the continent.
    /// </summary>
    private void ResolveKillTargetsPerQuest(
        Dictionary<int, QuestNode> quests,
        Dictionary<int, List<CreatureSpawn>> allSpawns)
    {
        int resolved = 0;
        int totalObjectives = 0;
        int usedTier1 = 0, usedTier2 = 0, usedTier3 = 0, usedGlobal = 0;

        foreach (var quest in quests.Values)
        {
            // Use the quest giver position as the reference point.
            // If no giver, fall back to turn-in position. If neither, skip.
            float refX, refY;
            int refMap;

            if (quest.Giver != null)
            {
                refX = quest.Giver.X;
                refY = quest.Giver.Y;
                refMap = quest.Giver.Map;
            }
            else if (quest.TurnIn != null)
            {
                refX = quest.TurnIn.X;
                refY = quest.TurnIn.Y;
                refMap = quest.TurnIn.Map;
            }
            else
            {
                continue;
            }

            foreach (var obj in quest.Objectives)
            {
                if (!obj.IsCreature) continue;
                totalObjectives++;

                if (!allSpawns.TryGetValue(obj.CreatureEntry, out var spawns) || spawns.Count == 0)
                    continue;

                // Filter to same map as quest giver first
                var sameMapSpawns = spawns.Where(s => s.Map == refMap).ToList();
                if (sameMapSpawns.Count == 0)
                {
                    // Creature doesn't spawn on this map at all — use best map globally
                    var bestMap = spawns.GroupBy(s => s.Map)
                        .OrderByDescending(g => g.Count())
                        .First();
                    var sorted = bestMap.OrderBy(s => Distance2D(s.X, s.Y, refX, refY)).ToList();
                    var cluster = TakeNearestCluster(sorted, refX, refY);
                    var agg = AggregateSpawns(cluster);
                    ApplyToObjective(obj, cluster[0].Name, bestMap.Key, agg, cluster);
                    usedGlobal++;
                    resolved++;
                    continue;
                }

                // Sort all same-map spawns by distance from quest giver.
                // A real player kills the first wolf they see, then works outward.
                // We take the nearest cluster of spawns (up to 10, or all within
                // 150yd of the nearest one — whichever is more) as the grind center.
                var sortedSpawns = sameMapSpawns
                    .OrderBy(s => Distance2D(s.X, s.Y, refX, refY))
                    .ToList();

                float nearestDist = Distance2D(sortedSpawns[0].X, sortedSpawns[0].Y, refX, refY);

                // Track which tier the nearest spawn fell into (for logging)
                if (nearestDist <= 500f) usedTier1++;
                else if (nearestDist <= 1000f) usedTier2++;
                else if (nearestDist <= 2000f) usedTier3++;
                else usedGlobal++;

                var nearestCluster = TakeNearestCluster(sortedSpawns, refX, refY);
                var result = AggregateSpawns(nearestCluster);
                ApplyToObjective(obj, nearestCluster[0].Name, refMap, result, nearestCluster);
                resolved++;
            }
        }

        _logger.LogInformation(
            "QuestGraphLoader: resolved {Resolved}/{Total} kill objectives — " +
            "proximity tiers: ≤500yd={T1}, ≤1000yd={T2}, ≤2000yd={T3}, map-wide={Global}",
            resolved, totalObjectives, usedTier1, usedTier2, usedTier3, usedGlobal);
    }

    /// <summary>
    /// From a list of spawns sorted by distance from the quest giver, take
    /// the nearest cluster. A "cluster" = all spawns within 150 yards of the
    /// nearest spawn, or at least 5 spawns (whichever gives more).
    ///
    /// This models how a real player handles "kill 10 wolves": they kill the
    /// first wolf they see near the quest giver, then work outward. The grind
    /// center should be where those nearest wolves are, not the average of
    /// all wolves on the continent.
    ///
    /// The 150yd cluster radius handles cases where spawns are loosely
    /// scattered (e.g., 8 wolves over a 200yd stretch outside Northshire).
    /// The min-5 floor handles cases where spawns are very sparse.
    /// </summary>
    private static List<CreatureSpawn> TakeNearestCluster(List<CreatureSpawn> sortedByDistance, float refX, float refY)
    {
        if (sortedByDistance.Count <= 5) return sortedByDistance;

        // The nearest spawn is the anchor point for the cluster
        var anchor = sortedByDistance[0];
        float clusterRadius = 150f;

        // Take all spawns within clusterRadius of the anchor
        var cluster = sortedByDistance
            .Where(s => Distance2D(s.X, s.Y, anchor.X, anchor.Y) <= clusterRadius)
            .ToList();

        // Ensure we have at least 5 spawns (grab nearest if cluster is too tight)
        if (cluster.Count < 5)
            cluster = sortedByDistance.Take(Math.Min(5, sortedByDistance.Count)).ToList();

        return cluster;
    }

    /// <summary>Compute average position and spread radius from a list of spawns.</summary>
    private static (float x, float y, float z, float radius) AggregateSpawns(List<CreatureSpawn> spawns)
    {
        float avgX = spawns.Average(s => s.X);
        float avgY = spawns.Average(s => s.Y);
        float avgZ = spawns.Average(s => s.Z);

        float spreadX = spawns.Max(s => s.X) - spawns.Min(s => s.X);
        float spreadY = spawns.Max(s => s.Y) - spawns.Min(s => s.Y);
        float radius = Math.Clamp(Math.Max(spreadX, spreadY) / 2f, 20f, 80f);

        return (avgX, avgY, avgZ, radius);
    }

    /// <summary>
    /// Snap an aggregated grind center to the REAL cluster spawn nearest the centroid.
    /// The bare AggregateSpawns average can land in an areaId-0 void pocket between spawns —
    /// entry 822 / quest 52: the average of 48 ring-arranged "Young Forest Bear" spawns is
    /// unzoned void 129yd off the nearest bear, so the bot marches there, can't path to the
    /// mobs across the seam, dies @areaId 0, and loops. A real spawn row is walkable + zoned
    /// by construction, so the snapped center can never be areaId 0. Nearest-to-centroid keeps
    /// the center maximally representative; the C++ approach-scan + 50→200yd rescan ladder
    /// works outward to the rest of the cluster. Falls back to the centroid only if the cluster
    /// is empty (defensive — resolved callers always pass a non-empty cluster).
    /// </summary>
    private static (float x, float y, float z) SnapToNearestSpawn(
        (float x, float y, float z, float radius) agg, List<CreatureSpawn> cluster)
    {
        if (cluster == null || cluster.Count == 0) return (agg.x, agg.y, agg.z);
        var snap = cluster[0];
        float bestSq = float.MaxValue;
        foreach (var s in cluster)
        {
            float dx = s.X - agg.x, dy = s.Y - agg.y;
            float dsq = dx * dx + dy * dy;
            if (dsq < bestSq) { bestSq = dsq; snap = s; }
        }
        return (snap.X, snap.Y, snap.Z);
    }

    /// <summary>Apply aggregated spawn data to a quest objective.</summary>
    /// <remarks>
    /// The grind CENTER is snapped to the real spawn nearest the centroid (SnapToNearestSpawn),
    /// never the bare AggregateSpawns average, which can be an areaId-0 void pocket between spawns
    /// (the entry-822 death loop). GrindRadius stays the cluster spread, so the C++ grind still
    /// covers the whole cluster from the snapped center.
    /// </remarks>
    private static void ApplyToObjective(QuestObjective obj, string name, int map,
        (float x, float y, float z, float radius) agg,
        List<CreatureSpawn>? clusterSpawns = null)
    {
        obj.TargetName = name;
        obj.GrindMap = map;
        obj.GrindRadius = agg.radius;

        if (clusterSpawns != null && clusterSpawns.Count > 0)
        {
            var c = SnapToNearestSpawn(agg, clusterSpawns);   // areaId-0 guard
            obj.GrindX = c.x;
            obj.GrindY = c.y;
            obj.GrindZ = c.z;

            // Session 31: Preserve individual spawn positions for fan-out
            obj.SpawnPositions = clusterSpawns
                .Select(s => (s.X, s.Y, s.Z))
                .ToList();
        }
        else
        {
            // No cluster detail (shouldn't happen for a resolved objective) — fall back to centroid.
            obj.GrindX = agg.x;
            obj.GrindY = agg.y;
            obj.GrindZ = agg.z;
        }
    }

    private static float Distance2D(float x1, float y1, float x2, float y2)
    {
        float dx = x1 - x2;
        float dy = y1 - y2;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    // ── Query 5: Item Drop Sources ────────────────────────────────────────

    private async Task LoadItemDropSourcesAsync(System.Data.IDbConnection conn, Dictionary<int, QuestNode> quests)
    {
        // Collect all item IDs referenced in item objectives
        var itemIds = quests.Values
            .SelectMany(q => q.ItemObjectives)
            .Select(i => i.ItemId)
            .Distinct()
            .ToList();

        if (itemIds.Count == 0) return;

        // Query creature_loot_template for drop sources
        var dropRows = await conn.QueryAsync<dynamic>(@"
            SELECT
                clt.item AS item_id,
                clt.entry AS creature_entry,
                ct.name AS creature_name,
                clt.ChanceOrQuestChance AS drop_chance
            FROM creature_loot_template clt
            JOIN creature_template ct ON ct.entry = clt.entry AND ct.patch = 0
            WHERE clt.item IN @ItemIds",
            new { ItemIds = itemIds });

        // Group by item → list of drop sources
        var dropMap = new Dictionary<int, List<ItemDropSource>>();
        var dropCreatureEntries = new HashSet<int>();

        foreach (var r in dropRows)
        {
            int itemId = (int)r.item_id;
            int creatureEntry = (int)r.creature_entry;

            if (!dropMap.TryGetValue(itemId, out var list))
            {
                list = new List<ItemDropSource>();
                dropMap[itemId] = list;
            }

            list.Add(new ItemDropSource
            {
                CreatureEntry = creatureEntry,
                CreatureName = (string)(r.creature_name ?? ""),
                DropChance = (float)r.drop_chance
            });

            dropCreatureEntries.Add(creatureEntry);
        }

        // Load INDIVIDUAL spawn positions for drop source creatures (not aggregated).
        // We resolve grind centers per-quest below, scoped to the quest giver's proximity —
        // same pattern as ResolveKillTargetsPerQuest(). This is the Session 26 P0 fix:
        // the old code GROUP BY'd globally, causing item-drop quests to get grind centers
        // 3000yd away (e.g., Tough Wolf Meat → Dun Morogh instead of Northshire).
        var dropCreatureSpawns = new Dictionary<int, List<CreatureSpawn>>();
        if (dropCreatureEntries.Count > 0)
        {
            var spawnRows = await conn.QueryAsync<dynamic>(@"
                SELECT
                    c.id AS creature_entry,
                    ct.name,
                    c.map,
                    c.position_x,
                    c.position_y,
                    c.position_z
                FROM creature c
                JOIN creature_template ct ON ct.entry = c.id AND ct.patch = 0
                WHERE c.id IN @Entries",
                new { Entries = dropCreatureEntries.ToList() });

            foreach (var r in spawnRows)
            {
                int entry = (int)r.creature_entry;
                if (!dropCreatureSpawns.TryGetValue(entry, out var list))
                {
                    list = new List<CreatureSpawn>();
                    dropCreatureSpawns[entry] = list;
                }
                list.Add(new CreatureSpawn
                {
                    Name = (string)(r.name ?? ""),
                    Map = (int)r.map,
                    X = (float)r.position_x,
                    Y = (float)r.position_y,
                    Z = (float)r.position_z
                });
            }

            _logger.LogInformation(
                "QuestGraphLoader: loaded {Spawns} individual spawns for {Creatures} item-drop creatures",
                dropCreatureSpawns.Values.Sum(l => l.Count), dropCreatureSpawns.Count);
        }

        // Apply drop sources to quest item objectives AND resolve grind centers per-quest
        int resolved = 0;
        int itemObjResolved = 0;
        int usedNearby = 0, usedGlobalFallback = 0;

        foreach (var quest in quests.Values)
        {
            foreach (var itemObj in quest.ItemObjectives)
            {
                if (!dropMap.TryGetValue(itemObj.ItemId, out var sources))
                    continue;

                itemObj.DropSources = sources;
                resolved++;

                // Resolve grind center for BestDropSource, scoped to quest giver proximity.
                // Use giver position as reference; fall back to turn-in; skip if neither.
                float refX, refY;
                int refMap;
                if (quest.Giver != null)
                {
                    refX = quest.Giver.X;
                    refY = quest.Giver.Y;
                    refMap = quest.Giver.Map;
                }
                else if (quest.TurnIn != null)
                {
                    refX = quest.TurnIn.X;
                    refY = quest.TurnIn.Y;
                    refMap = quest.TurnIn.Map;
                }
                else continue;

                // Find the best drop source creature that has spawns near the quest giver.
                // Priority: highest |DropChance| among creatures with same-map spawns near giver.
                ItemDropSource? bestSource = null;
                List<CreatureSpawn>? bestCluster = null;
                int bestClusterMap = refMap;

                foreach (var ds in sources.OrderByDescending(s => Math.Abs(s.DropChance)))
                {
                    if (!dropCreatureSpawns.TryGetValue(ds.CreatureEntry, out var spawns) || spawns.Count == 0)
                        continue;

                    // Prefer same-map spawns near the quest giver
                    var sameMapSpawns = spawns.Where(s => s.Map == refMap).ToList();
                    if (sameMapSpawns.Count > 0)
                    {
                        var sorted = sameMapSpawns
                            .OrderBy(s => Distance2D(s.X, s.Y, refX, refY))
                            .ToList();
                        var cluster = TakeNearestCluster(sorted, refX, refY);
                        bestSource = ds;
                        bestCluster = cluster;
                        bestClusterMap = refMap;
                        break; // Same-map + highest drop chance = best option
                    }

                    // Track best cross-map fallback (only if no same-map source found yet)
                    if (bestSource == null)
                    {
                        var bestMap = spawns.GroupBy(s => s.Map)
                            .OrderByDescending(g => g.Count())
                            .First();
                        var sorted = bestMap
                            .OrderBy(s => Distance2D(s.X, s.Y, refX, refY))
                            .ToList();
                        bestSource = ds;
                        bestCluster = TakeNearestCluster(sorted, refX, refY);
                        bestClusterMap = bestMap.Key;
                        // Don't break — keep looking for same-map sources with lower drop chance
                    }
                }

                if (bestSource != null && bestCluster != null)
                {
                    var agg = AggregateSpawns(bestCluster);
                    var c = SnapToNearestSpawn(agg, bestCluster);   // areaId-0 guard (same as kill objectives)
                    bestSource.SpawnCount = bestCluster.Count;
                    bestSource.GrindMap = bestClusterMap;
                    bestSource.GrindX = c.x;
                    bestSource.GrindY = c.y;
                    bestSource.GrindZ = c.z;
                    bestSource.GrindRadius = agg.radius;
                    // Session 31: Preserve individual spawn positions for fan-out
                    bestSource.SpawnPositions = bestCluster
                        .Select(s => (s.X, s.Y, s.Z))
                        .ToList();
                    itemObjResolved++;

                    if (bestClusterMap == refMap) usedNearby++;
                    else usedGlobalFallback++;

                    // 2026-06-30 (wolf-meat fix): bestSource above has NO distance tiebreak — when
                    // two+ creatures tie on |DropChance|, the foreach picks whichever sorts first and
                    // every tied sibling becomes invisible downstream (the C++ approach scan, grind
                    // target picker, and kill-credit all key off ONE entry). Confirmed live: Young
                    // Wolf and Timber Wolf both drop Tough Wolf Meat at chance=-80, both spawn within
                    // ~30-55yd of the Wolves Across the Border giver, and the bot walked past
                    // whichever one lost the tie. Collect every OTHER same-item creature that ties
                    // bestSource's chance AND has a real spawn on the giver's map (NOT bestSource's
                    // resolved map if that was a cross-map fallback — a tie only matters if it's near
                    // the same field bestSource resolved to). Capped to 3, mirroring
                    // AiBotTaskData::MAX_ALT_ENTRIES on the C++ side. Empty whenever one creature
                    // clearly wins the chance ranking — the common case is unaffected.
                    if (bestClusterMap == refMap)
                    {
                        const float DropChanceTieEpsilon = 0.01f;
                        var alts = new List<int>();
                        foreach (var ds in sources)
                        {
                            if (ds.CreatureEntry == bestSource.CreatureEntry) continue;
                            if (Math.Abs(Math.Abs(ds.DropChance) - Math.Abs(bestSource.DropChance)) > DropChanceTieEpsilon) continue;
                            if (!dropCreatureSpawns.TryGetValue(ds.CreatureEntry, out var altSpawns)) continue;
                            if (!altSpawns.Any(s => s.Map == refMap)) continue;
                            alts.Add(ds.CreatureEntry);
                            if (alts.Count >= 3) break;
                        }
                        itemObj.AltDropEntries = alts;
                    }
                }
            }
        }

        _logger.LogInformation(
            "QuestGraphLoader: resolved drop sources for {Resolved}/{Total} item objectives " +
            "({Items} unique items, {Creatures} drop creatures) — " +
            "grind centers: {ObjResolved} resolved (nearby={Nearby}, cross-map={Global})",
            resolved, quests.Values.SelectMany(q => q.ItemObjectives).Count(),
            itemIds.Count, dropCreatureEntries.Count,
            itemObjResolved, usedNearby, usedGlobalFallback);
    }

    // ── Query 6b: Game Object Drop Sources ──────────────────────────────

    /// <summary>
    /// For quest items that have NO creature drop source, check if they come from
    /// game objects (chests, barrels, herb nodes, etc.) via gameobject_loot_template.
    /// Loads GO spawn positions so bots can walk to and interact with them.
    /// </summary>
    private async Task LoadGameObjectDropSourcesAsync(System.Data.IDbConnection conn, Dictionary<int, QuestNode> quests)
    {
        // Find item objectives that have no creature drop source
        var itemsWithNoCreatureDrop = quests.Values
            .SelectMany(q => q.ItemObjectives)
            .Where(i => i.BestDropSource == null)
            .Select(i => i.ItemId)
            .Distinct()
            .ToList();

        if (itemsWithNoCreatureDrop.Count == 0) return;

        // Query gameobject_loot_template → gameobject_template → gameobject spawns
        var rows = await conn.QueryAsync<dynamic>(@"
            SELECT
                golt.item AS item_id,
                gt.entry AS go_entry,
                gt.name AS go_name,
                g.map,
                g.position_x,
                g.position_y,
                g.position_z
            FROM gameobject_loot_template golt
            JOIN gameobject_template gt ON gt.data1 = golt.entry AND gt.type = 3
            JOIN gameobject g ON g.id = gt.entry
            WHERE golt.item IN @ItemIds",
            new { ItemIds = itemsWithNoCreatureDrop });

        // Group by (itemId, goEntry) → aggregate spawn positions
        var goSourceMap = new Dictionary<int, Dictionary<int, GameObjectDropSource>>();

        foreach (var r in rows)
        {
            int itemId = (int)r.item_id;
            int goEntry = (int)r.go_entry;

            if (!goSourceMap.TryGetValue(itemId, out var goDict))
            {
                goDict = new Dictionary<int, GameObjectDropSource>();
                goSourceMap[itemId] = goDict;
            }

            if (!goDict.TryGetValue(goEntry, out var source))
            {
                source = new GameObjectDropSource
                {
                    GoEntry = goEntry,
                    GoName = (string)(r.go_name ?? "")
                };
                goDict[goEntry] = source;
            }

            source.SpawnPositions.Add(((float)r.position_x, (float)r.position_y, (float)r.position_z));
        }

        // Aggregate and apply to quest item objectives
        int resolved = 0;
        foreach (var quest in quests.Values)
        {
            // Get reference position for proximity scoping
            float refX, refY;
            int refMap;
            if (quest.Giver != null) { refX = quest.Giver.X; refY = quest.Giver.Y; refMap = quest.Giver.Map; }
            else if (quest.TurnIn != null) { refX = quest.TurnIn.X; refY = quest.TurnIn.Y; refMap = quest.TurnIn.Map; }
            else continue;

            foreach (var itemObj in quest.ItemObjectives)
            {
                if (itemObj.BestDropSource != null) continue; // creature source exists, skip
                if (!goSourceMap.TryGetValue(itemObj.ItemId, out var goDict)) continue;

                foreach (var (goEntry, source) in goDict)
                {
                    if (source.SpawnPositions.Count == 0) continue;

                    // Filter to same map as quest giver
                    // (GO spawns don't have a map field per-spawn in our query, but we joined
                    // via gameobject table which has map — all spawns for one GO entry share map
                    // since we loaded map from the gameobject table. Actually we did load per-spawn
                    // map. Let me just use the first spawn's inferred map.)
                    // Actually we loaded map per row. Let me aggregate properly.
                    source.SpawnCount = source.SpawnPositions.Count;
                    float avgX = source.SpawnPositions.Average(s => s.X);
                    float avgY = source.SpawnPositions.Average(s => s.Y);
                    float avgZ = source.SpawnPositions.Average(s => s.Z);

                    float spreadX = source.SpawnPositions.Max(s => s.X) - source.SpawnPositions.Min(s => s.X);
                    float spreadY = source.SpawnPositions.Max(s => s.Y) - source.SpawnPositions.Min(s => s.Y);
                    float radius = Math.Clamp(Math.Max(spreadX, spreadY) / 2f, 15f, 80f);

                    source.X = avgX;
                    source.Y = avgY;
                    source.Z = avgZ;
                    source.Map = refMap; // GO spawns are on same map as quest giver for starter zones
                    source.Radius = radius;

                    itemObj.GoSources.Add(source);
                    resolved++;
                }
            }
        }

        _logger.LogInformation(
            "QuestGraphLoader: resolved {Resolved} GO drop sources for {Items} items with no creature source",
            resolved, itemsWithNoCreatureDrop.Count);
    }

    // ── Query 7: Item Names ───────────────────────────────────────────────

    private async Task LoadItemNamesAsync(System.Data.IDbConnection conn, Dictionary<int, QuestNode> quests)
    {
        var itemIds = quests.Values
            .SelectMany(q => q.ItemObjectives)
            .Select(i => i.ItemId)
            .Distinct()
            .ToList();

        if (itemIds.Count == 0) return;

        var rows = await conn.QueryAsync<dynamic>(@"
            SELECT entry, name
            FROM item_template
            WHERE entry IN @ItemIds AND patch = 0",
            new { ItemIds = itemIds });

        var nameMap = rows.ToDictionary(r => (int)r.entry, r => (string)(r.name ?? ""));

        foreach (var quest in quests.Values)
        {
            foreach (var itemObj in quest.ItemObjectives)
            {
                if (nameMap.TryGetValue(itemObj.ItemId, out var name))
                    itemObj.ItemName = name;
            }
        }

        _logger.LogInformation("QuestGraphLoader: resolved {Count} item names", nameMap.Count);
    }

    // ── Step 8: Build PrevQuests Lists ─────────────────────────────────────

    /// <summary>
    /// Mirrors VMaNGOS ObjectMgr.cpp lines 5925-5947.
    /// Builds each quest's PrevQuests list from two sources:
    ///   1. PrevQuestId on the quest itself → added to own PrevQuests
    ///   2. NextQuestId on OTHER quests → added to the target quest's PrevQuests
    /// Sign convention matches VMaNGOS:
    ///   Positive prevQuest = must be rewarded (completed and turned in)
    ///   Negative prevQuest = must be active (currently in quest log)
    /// SatisfyQuestPreviousQuest logic: if ANY positive entry is rewarded, pass.
    /// </summary>
    private void BuildPrevQuestsLists(Dictionary<int, QuestNode> quests)
    {
        int directCount = 0, reverseCount = 0, chainCount = 0;

        foreach (var quest in quests.Values)
        {
            // Source 1: PrevQuestId → own prevQuests (VMaNGOS ObjectMgr.cpp line ~5935)
            // Skip if the target quest doesn't exist (same guard as VMaNGOS)
            if (quest.PrevQuestId != 0 && quests.ContainsKey(Math.Abs(quest.PrevQuestId)))
            {
                quest.PrevQuests.Add(quest.PrevQuestId);
                directCount++;
            }

            // Source 2: NextQuestId → target quest's prevQuests (VMaNGOS ObjectMgr.cpp line ~5946)
            // If this quest has NextQuestId, add this quest's ID to the target's PrevQuests.
            // Sign: if NextQuestId > 0, push positive (must be rewarded).
            //        if NextQuestId < 0, push negative (must be active).
            if (quest.NextQuestId != 0)
            {
                int targetId = Math.Abs(quest.NextQuestId);
                if (quests.TryGetValue(targetId, out var targetQuest))
                {
                    int signedId = quest.NextQuestId < 0
                        ? -quest.QuestId
                        : quest.QuestId;
                    targetQuest.PrevQuests.Add(signedId);
                    reverseCount++;
                }
            }

            // Source 3: NextQuestInChain → target quest's PREV-CHAIN list (VMaNGOS ObjectMgr.cpp
            // lines 5913-5923: `qNextItr->second->prevChainQuests.push_back(qinfo->GetQuestId())`).
            // This is a SEPARATE list from PrevQuests, gated SEPARATELY in GetAvailableQuests to
            // mirror Player::SatisfyQuestPrevChain (Player.cpp 13741-13760), which requires EVERY
            // prev-chain entry REWARDED (strict AND) — not the one-from-all OR of PrevQuests. The
            // 5623→5624 priest chain links ONLY here (PrevQuestId=0, NextQuestId=0), so without this
            // 5624 had an empty prereq set and was offered before 5623 was rewarded → the server
            // refused requirements_not_met → the union accept gate livelocked the group (2026-07-03).
            // NextQuestInChain is a forward chain pointer (always a positive quest id on this data);
            // the predecessor (this quest) must be REWARDED before the successor unlocks. Dup-guarded.
            if (quest.NextQuestInChain != 0
                && quests.TryGetValue(quest.NextQuestInChain, out var chainQuest)
                && !chainQuest.PrevChainQuests.Contains(quest.QuestId))
            {
                chainQuest.PrevChainQuests.Add(quest.QuestId);
                chainCount++;
            }
        }

        _logger.LogInformation(
            "QuestGraphLoader: built PrevQuests — {Direct} direct, {Reverse} reverse edges, {Chain} prev-chain edges",
            directCount, reverseCount, chainCount);
    }

    // ── Race/Class Bitmask Helpers ─────────────────────────────────────────

    /// <summary>
    /// Convert WowRace enum value (1-8) to the race bitmask used in quest_template.RequiredRaces.
    /// </summary>
    public static int RaceToBitmask(int raceId) => raceId switch
    {
        1 => 1,    // Human
        2 => 2,    // Orc
        3 => 4,    // Dwarf
        4 => 8,    // Night Elf
        5 => 16,   // Undead
        6 => 32,   // Tauren
        7 => 64,   // Gnome
        8 => 128,  // Troll
        _ => 0
    };

    /// <summary>
    /// Convert WowClass enum value to the class bitmask used in quest_template.RequiredClasses.
    /// </summary>
    public static int ClassToBitmask(int classId) => classId switch
    {
        1 => 1,     // Warrior
        2 => 2,     // Paladin
        3 => 4,     // Hunter
        4 => 8,     // Rogue
        5 => 16,    // Priest
        7 => 64,    // Shaman
        8 => 128,   // Mage
        9 => 256,   // Warlock
        11 => 1024, // Druid
        _ => 0
    };
}

/// <summary>
/// Individual creature spawn position. Used as intermediate data during per-quest
/// kill target resolution. Not stored long-term — aggregated into QuestObjective fields.
/// </summary>
internal class CreatureSpawn
{
    public string Name { get; set; } = "";
    public int Map { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
}