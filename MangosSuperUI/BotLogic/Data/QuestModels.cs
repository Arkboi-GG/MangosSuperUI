namespace MangosSuperUI.BotLogic.Data;

// =============================================================================
// Quest Graph Data Models
// Used by QuestGraphLoader to hold the in-memory quest dependency graph.
// All data loaded once at startup from the mangos DB — no per-bot queries.
// =============================================================================

/// <summary>
/// A single quest node in the dependency graph. Populated from quest_template
/// with resolved giver/turnin NPC positions and objective grind coordinates.
/// </summary>
public class QuestNode
{
    // ── Identity ──
    public int QuestId { get; set; }
    public string Title { get; set; } = "";
    public int QuestLevel { get; set; }
    public int MinLevel { get; set; }
    public int ZoneId { get; set; }          // ZoneOrSort — negative = class/race sort

    // ── Gates ──
    public int RaceMask { get; set; }        // 0 = any race
    public int ClassMask { get; set; }       // 0 = any class
    public int QuestFlags { get; set; }
    public int SpecialFlags { get; set; }

    // ── Chain edges ──
    public int PrevQuestId { get; set; }     // >0: must complete first. <0: must NOT have completed
    public int NextQuestId { get; set; }
    public int NextQuestInChain { get; set; }
    public int ExclusiveGroup { get; set; }  // nonzero: only one quest from this group
    public int BreadcrumbForQuestId { get; set; }

    // ── Objectives (up to 4 kill/interact + 4 item slots) ──
    public QuestObjective[] Objectives { get; set; } = Array.Empty<QuestObjective>();
    public QuestItemReq[] ItemObjectives { get; set; } = Array.Empty<QuestItemReq>();

    // ── Source item (given on accept) ──
    public int SrcItemId { get; set; }

    // ── Rewards ──
    public int RewXP { get; set; }
    public int RewMoney { get; set; }        // copper; negative = required money

    // Session 33: Reward item IDs (for frustration-abandon evaluation)
    public int RewChoiceItemId1 { get; set; }
    public int RewChoiceItemId2 { get; set; }
    public int RewChoiceItemId3 { get; set; }
    public int RewChoiceItemId4 { get; set; }
    public int RewChoiceItemId5 { get; set; }
    public int RewChoiceItemId6 { get; set; }
    public int RewItemId1 { get; set; }
    public int RewItemId2 { get; set; }
    public int RewItemId3 { get; set; }
    public int RewItemId4 { get; set; }

    // ── Resolved world data (populated by loader from DB joins) ──
    public QuestNpcLocation? Giver { get; set; }
    public QuestNpcLocation? TurnIn { get; set; }

    // ── Helpers ──
    public bool HasKillObjectives => Objectives.Length > 0;
    public bool HasItemObjectives => ItemObjectives.Length > 0;
    public bool HasObjectives => HasKillObjectives || HasItemObjectives;
    public bool HasCastObjectives => Objectives.Any(o => o.IsCastObjective);
    public bool IsAvailableToRace(int raceBit) => RaceMask == 0 || (RaceMask & raceBit) != 0;
    public bool IsAvailableToClass(int classBit) => ClassMask == 0 || (ClassMask & classBit) != 0;

    /// <summary>
    /// Session 33: True if quest gives item rewards (choice or fixed).
    /// Quests with item rewards are considered "valuable" — bots won't abandon them
    /// even after repeated failures. They'll keep deferring and retrying.
    /// </summary>
    public bool HasItemReward =>
        RewChoiceItemId1 != 0 || RewChoiceItemId2 != 0 || RewChoiceItemId3 != 0 ||
        RewChoiceItemId4 != 0 || RewChoiceItemId5 != 0 || RewChoiceItemId6 != 0 ||
        RewItemId1 != 0 || RewItemId2 != 0 || RewItemId3 != 0 || RewItemId4 != 0;

    /// <summary>
    /// Session 33: True if quest is part of a chain (has prerequisites or leads to
    /// follow-up quests). Chain quests are never abandoned — they unlock future content.
    /// </summary>
    public bool IsPartOfChain =>
        PrevQuestId > 0 || NextQuestInChain > 0 || NextQuestId > 0 || ExclusiveGroup != 0;

    /// <summary>
    /// Runtime-built prerequisite list mirroring VMaNGOS prevQuests.
    /// Populated from PrevQuestId (direct) + reverse NextQuestId edges.
    /// Positive = must be rewarded. Negative = must be active (in progress).
    /// If ANY positive entry is rewarded, the check passes (one-from-all).
    /// </summary>
    public List<int> PrevQuests { get; set; } = new();

    /// <summary>
    /// Runtime-built PREV-CHAIN list mirroring VMaNGOS Quest::prevChainQuests
    /// (ObjectMgr.cpp: for each quest with NextQuestInChain=X, push its own id into X's
    /// prevChainQuests). This is a SEPARATE list from PrevQuests and a SEPARATE gate: it
    /// mirrors Player::SatisfyQuestPrevChain, which requires EVERY entry to be REWARDED
    /// (a strict AND across all predecessors), NOT the one-from-all OR that PrevQuests uses.
    /// Kept apart precisely so a branching chain resolves like the server instead of passing
    /// on any single rewarded predecessor. Empty for the vast majority of quests.
    ///
    /// Why it exists (2026-07-03): the 5623 "In Favor of the Light" -> 5624 "Garments of the
    /// Light" priest chain links ONLY via NextQuestInChain (PrevQuestId=0, NextQuestId=0), so
    /// with only PrevQuests built, 5624 had NO prereq and read as available before 5623 was
    /// turned in. The god bot stamped the accept, the server refused requirements_not_met, and
    /// the union accept gate livelocked the whole group (guid 6, ~1/s for 5h).
    /// </summary>
    public List<int> PrevChainQuests { get; set; } = new();
}

/// <summary>
/// A kill or interact objective (ReqCreatureOrGOId slots 1-4).
/// Positive CreatureOrGOId = creature entry, negative = gameobject entry.
/// </summary>
public class QuestObjective
{
    public int Slot { get; set; }            // 1-4
    public int CreatureOrGOId { get; set; }  // positive = creature, negative = gameobject
    public int Count { get; set; }
    public string? TargetName { get; set; }

    // Cast objective (quest_template ReqSpellCast). When non-zero, this objective is credited by
    // CASTING this spell on the target creature (CreatureOrGOId, positive) — the priest heal-an-NPC
    // class quests and the like — NOT by killing it. Populated by QuestGraphLoader.LoadCastSpellsAsync.
    // 0 = a normal kill/interact objective.
    public int RequiredSpellId { get; set; }

    // Resolved from creature spawn data (avg position of all spawns)
    public float GrindX { get; set; }
    public float GrindY { get; set; }
    public float GrindZ { get; set; }
    public int GrindMap { get; set; }
    public float GrindRadius { get; set; }   // derived from spawn spread, clamped [20, 80]

    /// <summary>
    /// Individual spawn positions within the grind cluster. Used by SendGrindTask
    /// to fan out bots — each bot picks a deterministic spawn as its personal grind
    /// center instead of all bots converging on the centroid.
    /// Session 31: Spawn Fan-Out.
    /// </summary>
    public List<(float X, float Y, float Z)> SpawnPositions { get; set; } = new();

    /// <summary>
    /// Fix 5 (2026-07-04): the target creature is FRIENDLY to the quest's eligible side, so a
    /// bot can never land the kill the objective encodes — real credit is a scripted spellcast /
    /// event (e.g. 5624 "Garments of the Light": ReqCreatureOrGOId=12423 John Turner, an
    /// Alliance-friendly civilian). Flagged by QuestGraphLoader.FlagFriendlyKillTargetsAsync from
    /// creature_template.faction -> faction_template friend/hostile masks vs the quest's RaceMask
    /// side. The planner treats a quest with any such objective as UNWORKABLE: BuildBatch skips it
    /// (unworkable-obj), IsPickable refuses it, WorkableInLog ignores the slot — so it can never
    /// become the eternal active pick (the 2026-07-04 group-A immortal-WAIT trigger).
    /// </summary>
    public bool TargetFriendly { get; set; }

    public bool IsCreature => CreatureOrGOId > 0;
    public bool IsGameObject => CreatureOrGOId < 0;
    public int CreatureEntry => Math.Max(0, CreatureOrGOId);
    public int GameObjectEntry => CreatureOrGOId < 0 ? Math.Abs(CreatureOrGOId) : 0;

    /// <summary>A "cast spell S on creature C" objective (positive creature + a required spell),
    /// driven by the QUEST_CAST bridge command rather than a grind. Friendliness is irrelevant here
    /// (you may be HEALING a friendly), so these must never be treated as unkillable-friendly kill
    /// targets.</summary>
    public bool IsCastObjective => CreatureOrGOId > 0 && RequiredSpellId != 0;

    /// <summary>A plain kill objective: a creature target with no required spell.</summary>
    public bool IsKillObjective => CreatureOrGOId > 0 && RequiredSpellId == 0;
}

/// <summary>
/// An item-gather objective (ReqItemId slots 1-4).
/// </summary>
public class QuestItemReq
{
    public int Slot { get; set; }            // 1-4
    public int ItemId { get; set; }
    public int Count { get; set; }
    public string? ItemName { get; set; }

    // Which creatures drop this item (from creature_loot_template)
    public List<ItemDropSource> DropSources { get; set; } = new();

    // Which game objects provide this item (from gameobject_loot_template)
    public List<GameObjectDropSource> GoSources { get; set; } = new();

    /// <summary>Best creature to grind for this item (highest drop chance with spawns).</summary>
    public ItemDropSource? BestDropSource => DropSources
        .Where(d => d.SpawnCount > 0)
        .OrderByDescending(d => Math.Abs(d.DropChance))
        .ThenByDescending(d => d.SpawnCount)
        .FirstOrDefault();

    /// <summary>
    /// Other creature entries TIED with BestDropSource on drop chance for this item (2026-06-30:
    /// the wolf-meat fix). BestDropSource's selection (highest drop chance among same-map
    /// creatures) has no distance tiebreak — when two+ creatures tie on chance, picking ONE
    /// makes its siblings invisible everywhere downstream (the C++ approach scan, the grind
    /// target picker, kill-credit), even when they're standing in the SAME field as the chosen
    /// one. Computed once by QuestGraphLoader alongside BestDropSource (LoadItemDropSourcesAsync)
    /// from creatures that tied on chance AND have a spawn on the giver's map. Capped to 3
    /// (mirrors AiBotTaskData::MAX_ALT_ENTRIES on the C++ side). Empty for every item where one
    /// creature clearly wins, and ALWAYS empty for kill objectives (QuestObjective) — a kill
    /// quest names one specific creature and the server only credits that exact entry, so this
    /// is deliberately item-drop-only.
    /// </summary>
    public List<int> AltDropEntries { get; set; } = new();

    /// <summary>Best game object to interact with for this item (most spawns).</summary>
    public GameObjectDropSource? BestGoSource => GoSources
        .Where(g => g.SpawnCount > 0)
        .OrderByDescending(g => g.SpawnCount)
        .FirstOrDefault();
}

/// <summary>
/// A game object (chest, barrel, herb, etc.) that provides a quest-required item.
/// </summary>
public class GameObjectDropSource
{
    public int GoEntry { get; set; }
    public string? GoName { get; set; }
    public int SpawnCount { get; set; }

    // Interaction center resolved from gameobject spawn positions
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public int Map { get; set; }
    public float Radius { get; set; }

    /// <summary>Individual spawn positions for walk-to-each interaction pattern.</summary>
    public List<(float X, float Y, float Z)> SpawnPositions { get; set; } = new();
}

/// <summary>
/// A creature that drops a quest-required item.
/// </summary>
public class ItemDropSource
{
    public int CreatureEntry { get; set; }
    public string? CreatureName { get; set; }
    public float DropChance { get; set; }    // ChanceOrQuestChance (negative = quest-only drop)
    public int SpawnCount { get; set; }

    // Grind center resolved from creature spawn positions
    public float GrindX { get; set; }
    public float GrindY { get; set; }
    public float GrindZ { get; set; }
    public int GrindMap { get; set; }
    public float GrindRadius { get; set; }

    /// <summary>Individual spawn positions within the grind cluster (Session 31: Spawn Fan-Out).</summary>
    public List<(float X, float Y, float Z)> SpawnPositions { get; set; } = new();
}

/// <summary>
/// A resolved NPC world location (for quest givers and turn-in NPCs).
/// </summary>
public class QuestNpcLocation
{
    public int NpcEntry { get; set; }
    public string Name { get; set; } = "";
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public int Map { get; set; }
}