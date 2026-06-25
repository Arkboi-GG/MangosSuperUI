using MangosSuperUI.BotLogic.Core;
using MangosSuperUI.BotLogic.Data;

namespace MangosSuperUI.BotLogic.Brain;

// ============================================================================
// GroupCoordinator -- the "god bot" pre-pass (grouping §3.2).
//
// A stateless stamping pre-pass the host runs ONCE per tick (BotBrainService
// .RunBrainTicksAsync), BEFORE the per-bot TickAsync loop -- NOT an IBotPlanner.
// It aggregates live member state and STAMPS each grouped member's BotContext;
// it never issues a command. There is still exactly ONE place where intent
// becomes commands (the spine), so the "second decision layer" that deadlocked
// every prior grouping attempt never exists. The coordinator is an INPUT to
// selection, not a decider (§3.2).
//
// v1 (this build) stamps the COMBAT DIRECTIVE only -- the half wired end-to-end
// through the seam: BotContext.CombatDirective -> BridgeContracts.Combat ->
// COMBAT_DIRECTIVE -> the C++ TeamPlay assist resolver. Each tick, per group, it
// elects an anchor and stamps every present member Assist(anchor); ungrouped /
// sub-2 bots get None. The spine (BotBrain step 1a) emits COMBAT_DIRECTIVE only
// when a bot's stamp CHANGES, so the wire stays brain-cadence (§3.8.4 / §1).
//
// NOT YET (the union-pool half, the next increment): the EXECUTION directive --
// the union quest pool, min-headroom batch sizing, one god-chosen objective
// (creature_entry + coords + kill_count), the phase machine (Questing /
// HoldAtAnchor / GroupVendor / GroupTrain / TurnIn) as live-poll gates (§3.3 /
// §3.5), and the member-consults-stamp hook in GoalSelector/QuestPlanner. That
// lands without re-plumbing this seam.
//
// Stateless by design: the per-bot "last emitted" memory lives on BotContext, the
// stamp lives on BotContext, group membership lives on GroupManager. Nothing to
// miscount here, recomputed fresh from ground truth every tick.
// ============================================================================
public static class GroupCoordinator
{
    /// <summary>
    /// Stamp every context's combat directive for this tick. Pure: reads member state +
    /// group membership, writes only BotContext.CombatDirective, issues nothing.
    /// </summary>
    public static void Update(IReadOnlyDictionary<int, BotContext> contexts, GroupManager groups, QuestGraphLoader quests)
    {
        // Default EVERY bot to None (BOTH seams) first, then overwrite grouped members below. A bot that
        // left a group this tick (or the whole mode going Off) therefore reverts to solo for combat AND
        // execution -> the spine emits one combat mode=none, and the QuestPlanner consult falls back to
        // the bot's own batch. Idempotent for the rest.
        foreach (var ctx in contexts.Values)
        {
            ctx.CombatDirective = CombatDirective.None;
            ctx.ExecDirective = ExecDirective.None;
        }

        // Mode Off disbands all groups (GroupManager), so GetAllGroups() is empty and the None pass
        // above is the whole story. The explicit guard just makes the off-switch obvious + cheap.
        if (groups.Mode == GroupingMode.Off)
            return;

        foreach (var group in groups.GetAllGroups())
        {
            // Resolve member guids -> live contexts; skip any without a connected context.
            var members = new List<BotContext>(group.MemberGuids.Count);
            foreach (var guid in group.MemberGuids)
                if (contexts.TryGetValue(guid, out var ctx))
                    members.Add(ctx);

            // Need >=2 PRESENT members to act as a team; otherwise leave None (solo).
            if (members.Count < 2)
                continue;

            // Anchor election (v1 = the §3.8 #3 fallback): highest-level present member, lowest
            // guid breaking ties so the choice is STABLE tick-to-tick. The anchor is NOT a leader to
            // follow -- it is only (a) whose live victim the team focus-fires, and (b) the stable
            // "nearest" origin for objective selection. When the union execution directive feeds quest
            // credit this becomes the holder of the active objective.
            int anchorGuid = ElectAnchor(members);

            // -- Combat seam (focus-fire): every member assists the anchor's live victim. --
            var combat = CombatDirective.Assist(anchorGuid);
            foreach (var ctx in members)
                ctx.CombatDirective = combat;

            // -- Execution seam (the union "god bot"): pick ONE shared kill objective the whole group
            //    works together, gated on ALL ELIGIBLE HOLDERS finishing it (§3.2 / §3.3). The objective
            //    stays stamped -- the team keeps killing it together -- until no present, live holder
            //    still owes kills; a member ineligible for the quest helps but never gates it (the
            //    2-priest-1-warrior case). Acceptance stays per-member (each bot's own QuestPlanner
            //    accepts what it's eligible for during the None gaps), so the warrior simply never holds
            //    the priest quest. None this tick -> members accept / turn in / pick on their own.
            if (!quests.IsLoaded)
                continue;
            var objective = PickGroupObjective(members, anchorGuid, quests);
            if (!objective.IsActive)
                continue;
            foreach (var ctx in members)
                ctx.ExecDirective = objective;
        }
    }

    // Highest level wins; lowest guid breaks ties (stable ordering). Members is non-empty.
    private static int ElectAnchor(List<BotContext> members)
    {
        var anchor = members[0];
        foreach (var ctx in members)
        {
            if (ctx.Level > anchor.Level ||
                (ctx.Level == anchor.Level && ctx.Guid < anchor.Guid))
                anchor = ctx;
        }
        return anchor.Guid;
    }

    // No-progress window after which a stuck/away holder STOPS gating the objective -- the liveness
    // escape (§3.5): one frozen member must never freeze the group. It reads the member's own progress
    // clock (kills / quest / level / ack reset it), so it is stateless. A holder that is merely
    // dead/recovering holds the team for up to this long; past it the group moves on and re-picks the
    // objective once the holder is live again (the quest is still in its log -- nothing is lost).
    private const double GateLivenessSec = 90;

    // The union "god bot": across the present members' in-log quests, return the nearest objective (to
    // the anchor) that at least one present, LIVE HOLDER still owes kills on. "Holder" = a member whose
    // log contains the quest -- so eligibility is implicit (a member that couldn't accept the quest is
    // never a holder and never gates it). Returns ExecDirective.None when the team has nothing to grind
    // together this tick (everything met / only accepts + turn-ins left), handing those back to each
    // member's own QuestPlanner.
    private static ExecDirective PickGroupObjective(List<BotContext> members, int anchorGuid, QuestGraphLoader quests)
    {
        // Anchor pos = the stable "nearest" reference.
        BotContext anchor = members[0];
        foreach (var m in members)
            if (m.Guid == anchorGuid) { anchor = m; break; }

        // Union of every quest id any present member holds (its log).
        var questIds = new HashSet<int>();
        foreach (var m in members)
            foreach (var qid in m.QuestLog.Keys)
                questIds.Add(qid);

        ExecDirective best = ExecDirective.None;
        float bestD = float.MaxValue;

        foreach (int qid in questIds)
        {
            var node = quests.GetQuest(qid);
            if (node == null) continue;

            foreach (var o in node.Objectives)
            {
                if (!o.IsCreature || o.Count <= 0) continue;
                if (o.GrindMap != anchor.MapId) continue;   // same-map team objective (cross-map is per-bot travel, later)

                // Eligibility-gated completion: does ANY present, LIVE holder still owe kills here?
                bool anyOwes = false;
                foreach (var m in members)
                {
                    if (!m.QuestLog.TryGetValue(qid, out var e)) continue;      // not a holder -> does not gate
                    if (m.TimeSinceProgressSec > GateLivenessSec) continue;     // stuck/away -> liveness escape, does not gate
                    int have = (o.Slot >= 1 && o.Slot <= e.MobCounts.Length) ? e.MobCounts[o.Slot - 1] : 0;
                    if (o.Count - have > 0) { anyOwes = true; break; }
                }
                if (!anyOwes) continue;   // every eligible holder is done -> this objective is group-complete

                float d = Dist2(anchor.Pos.X, anchor.Pos.Y, o.GrindX, o.GrindY);
                if (d < bestD)
                {
                    bestD = d;
                    best = ExecDirective.Objective(qid, o.Slot, o.CreatureEntry,
                        o.GrindX, o.GrindY, o.GrindZ, o.GrindMap, anchorGuid);
                }
            }
        }
        return best;
    }

    private static float Dist2(float ax, float ay, float bx, float by)
    {
        float dx = ax - bx, dy = ay - by;
        return MathF.Sqrt(dx * dx + dy * dy);
    }
}