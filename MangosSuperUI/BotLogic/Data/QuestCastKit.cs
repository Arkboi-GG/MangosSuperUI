namespace MangosSuperUI.BotLogic.Data;

// =============================================================================
// QuestCastKit — spell kits for SCRIPT-CREDITED cast-a-spell class quests.
//
// Some class quests are completed by casting specific spells on a target NPC, but the required
// spells live ONLY in the quest's Objectives TEXT, not in the quest_template ReqSpellCast column
// (so QuestObjective.RequiredSpellId is 0). The target NPC's creature script watches for those
// exact casts and credits the objective itself. Because the spells aren't in the DB, the planner
// can't read them from quest_template — they come from here.
//
// COLUMN-encoded casts (ReqSpellCast != 0 -> RequiredSpellId != 0) do NOT need an entry here; the
// planner casts RequiredSpellId directly. This registry is only for the script-credited case.
//
// Extension point: add more script-credited class quests (paladin, etc.) by mapping questId ->
// the ordered spell ids the target's script expects. No core or DB change needed. Spells are cast
// in list order, one per QUEST_CAST, and the objective's counter ticks once the script has seen
// all of them.
// =============================================================================
public static class QuestCastKit
{
    private static readonly Dictionary<int, int[]> Kits = new()
    {
        // Priest "Garments of the Light" — heal + fortify the target NPC, whose script credits on both.
        //   5624 (human): Guard Roberts (12423, Elwynn, map 0, ~-9515/-136/60)
        //   5625 (dwarf): Mountaineer Dolf (12427, Dun Morogh, map 0, ~-5669/-454/394)
        // Lesser Heal Rank 2 = 2053, Power Word: Fortitude Rank 1 = 1243.
        { 5624, new[] { 2053, 1243 } },
        { 5625, new[] { 2053, 1243 } },
    };

    /// <summary>True if this quest has a script-credited cast kit.</summary>
    public static bool Has(int questId) => Kits.ContainsKey(questId);

    /// <summary>The ordered spell ids to cast for this quest, or null if it has no kit.</summary>
    public static int[]? For(int questId) => Kits.TryGetValue(questId, out var s) ? s : null;
}