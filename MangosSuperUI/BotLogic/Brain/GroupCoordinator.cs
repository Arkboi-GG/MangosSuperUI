using MangosSuperUI.BotLogic.Core;
using MangosSuperUI.BotLogic.Data;
using MangosSuperUI.BotLogic.Planners;   // reuse QuestPlanner.ReachTier/InReach (match solo, don't reimplement)
using Microsoft.Extensions.Logging;

namespace MangosSuperUI.BotLogic.Brain;

// ============================================================================
// GroupCoordinator -- the "god bot" central driver (AIBOT_GROUPING_DESIGN).
//
// A STATIC stamping pre-pass the host runs ONCE per tick (BotBrainService
// .RunBrainTicksAsync) BEFORE the per-bot TickAsync loop -- NOT an IBotPlanner.
// It holds the union of every present member's objectives and drives the whole
// group through ONE TASK AT A TIME, TOGETHER (§0): accept together, grind
// together, turn in together, then advance. No leader, no follower -- members
// are PEERS that execute the god bot's stamped GroupOrder. It issues NO command;
// the spine (BotBrain) alone turns intent into wire, so there is exactly ONE
// decision layer (§1 / §8) -- the "second live decider" that deadlocked every
// prior attempt never exists.
//
// State lives on BotGroup.Plan (a transient GroupPlan, mutated here each tick;
// never persisted -- §7). The coordinator stays static and stateless-at-fleet-
// level: it recomputes from ground truth every tick and mutates group.Plan. Every
// phase gate is a LIVE POLL over member state with a timeout + a liveness escape
// (§3 / §6) -- never a stored boolean (a miscounted flag is what froze the old
// leader, §8). The only thing held across ticks is the LATCHED objective (so the
// focus-fire target does not thrash, §3).
//
// Two seams stamped per present grouped member each tick:
//   • CombatDirective.Assist(anchor)  -- the focus-fire half, already wired end-to-
//     end (BotContext.CombatDirective -> COMBAT_DIRECTIVE -> C++ TeamPlay). UNCHANGED.
//   • GroupOrder (the §3 phase + target NPC + embedded kill objective) -- consumed
//     IN-PROCESS by GoalSelector (route on Phase != None) and QuestPlanner.DriveGroup
//     (branch on Phase). Not a wire command.
//
// v1 boundaries (deliberate, documented; the design defers them): the pool is the
// union of fully-LOCAL quests (giver + turn-in on the anchor's map, and a grindable
// on-map objective -- a creature kill OR a kill-for-loot item whose best drop source is
// an on-map creature -- or an instant-complete quest). GAME-OBJECT-sourced items (herbs
// / chests) and cross-map travel are deferred (matching the solo planner's own drop-
// source phase gate); they are accepted opportunistically for breadth but not driven as
// group objectives. Group-coordinated MAINTENANCE (the §4 whole-group vendor / repair /
// 2-level training errands) is also a follow-on -- see the note in DriveGroup; this
// driver scopes to grouped QUESTING.
// ============================================================================
public static class GroupCoordinator
{
    // ── Tunables ──
    private const int QuestLogCap = 20;               // 1.12 quest-log size (min-headroom sizing, §6)
    private const int QuestStatusComplete = 1;        // VMaNGOS QUEST_STATUS: COMPLETE=1 (INCOMPLETE=3)
    private const int TravelSafetyMargin = 3;         // §5.1: weakest may face up to weakest+3 on a travel leg
    private const float ArrivalReachYards = 15f;      // "the group has arrived at the NPC together" gate
    private const double GateLivenessSec = 90;        // §6 liveness escape: a stuck/away member stops gating after this

    // ── Instrumentation (logic-neutral): make the decider SAY which door it took. ──
    // One [GROUP] line per group on a phase CHANGE, plus a ~15s heartbeat while parked in a
    // "stuck" phase (Hold/None/Forming) so a persistent park keeps reporting LIVE gate values.
    // Falls back to Console (→ journald) when no logger is attached, so this is a single-file
    // drop with zero wiring. Set GroupCoordinator.Log to route through ILogger if preferred.
    public static ILogger? Log;
    private static readonly Dictionary<int, DateTime> _lastEmit = new();
    private const double EmitHeartbeatSec = 15;

    private static void Emit(int anchorGuid, GroupPhase prev, GroupPhase now, string detail, List<BotContext> members)
    {
        bool changed = prev != now;
        bool stuck = now == GroupPhase.HoldAtAnchor || now == GroupPhase.None || now == GroupPhase.Forming;
        if (!changed)
        {
            if (!stuck) return;
            if (_lastEmit.TryGetValue(anchorGuid, out var last)
                && (DateTime.UtcNow - last).TotalSeconds < EmitHeartbeatSec) return;
        }
        _lastEmit[anchorGuid] = DateTime.UtcNow;
        var who = string.Join(" ", members.Select(m =>
            $"[{m.Guid}:L{m.Level} hp{(int)(m.HpPct * 100)} dead={m.Dead} prog{(int)m.TimeSinceProgressSec}s]"));
        var line = $"[GROUP] anchor={anchorGuid} {prev}->{now} {detail} | {who}";
        if (Log != null) Log.LogInformation(line);
        else Console.WriteLine(line);
    }

    /// <summary>
    /// Stamp every context this tick. Pure side effect on BotContext (CombatDirective +
    /// GroupOrder) and BotGroup.Plan; issues nothing. Reads member state + group membership
    /// + the loaders (read-only).
    /// </summary>
    public static void Update(
        IReadOnlyDictionary<int, BotContext> contexts,
        GroupManager groups,
        QuestGraphLoader quests,
        ZoneSafetyMap safety)
    {
        // Default EVERY bot to None on BOTH seams, then overwrite grouped members below. A bot
        // that left a group this tick (or the whole mode going Off) reverts to solo for combat
        // AND execution: the spine emits one combat mode=none, and GoalSelector falls back to the
        // bot's own planner (GroupOrder.None -> Phase==None -> not routed to the group executor).
        foreach (var ctx in contexts.Values)
        {
            ctx.CombatDirective = CombatDirective.None;
            ctx.GroupOrder = GroupOrder.None;
        }

        // Mode Off disbands all groups, so GetAllGroups() is empty and the None pass is the whole
        // story. The explicit guard makes the off-switch obvious + cheap.
        if (groups.Mode == GroupingMode.Off)
        {
            // Grouping off → drop any coordinator-assigned held objective so each bot reverts fully to
            // solo (its own producers own Held). Self-solo objectives are left untouched. (§6.)
            foreach (var ctx in contexts.Values)
                if (ctx.Held is { Source: ObjectiveSource.Coordinator })
                    ctx.ClearObjective();
            return;
        }

        // Track who got an active group order this tick, so the post-pass can clear stale coordinator-
        // assigned objectives on bots that dropped out of an active group (ungrouped / sub-quorum /
        // graph-not-loaded) without disturbing the grace clock of bots still on the same order.
        var groupedGuids = new HashSet<int>();

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

            int anchorGuid = ElectAnchor(members);

            // ── Combat seam (focus-fire): every member assists the anchor's live victim. ──
            var combat = CombatDirective.Assist(anchorGuid);
            foreach (var ctx in members)
                ctx.CombatDirective = combat;

            // ── Execution seam: run the §3 machine, stamp every member the SAME GroupOrder. ──
            // The phase/target/objective are GROUP properties (the per-member differences --
            // which quests THIS bot accepts, whether IT still owes kills -- are read by the
            // executor from the bot's own log). Without the graph we can't drive questing, but
            // combat assist still stands; leave GroupOrder.None so members solo-grind.
            if (!quests.IsLoaded)
                continue;

            var order = DriveGroup(group.Plan, members, anchorGuid, quests, safety);
            foreach (var ctx in members)
            {
                ctx.GroupOrder = order;
                StampHeld(ctx, order);            // mirror the order as the reconcile/observability anchor (§3/§6)
                groupedGuids.Add(ctx.Guid);
            }
        }

        // Clear stale coordinator-assigned objectives on bots no longer in an active group this tick
        // (leaves self-solo objectives — the solo producers own those). A post-pass, NOT the default
        // None pass, so a bot still on the SAME order keeps its grace clock intact (SetObjective only
        // re-stamps the clock on a CHANGE). §6.
        foreach (var ctx in contexts.Values)
            if (!groupedGuids.Contains(ctx.Guid) && ctx.Held is { Source: ObjectiveSource.Coordinator })
                ctx.ClearObjective();
    }

    // Mirror the assigned GroupOrder as the bot's held strategic objective (§3/§6) — the reconcile /
    // observability anchor. Only the MOVING (Travel) and GRINDING (Grind) phases are reconcilable; the
    // at-NPC interact phases (Accept/TurnIn) and the anchor hold are PASSIVE (Hold), never re-issued by
    // the reconcile. SetObjective preserves the grace clock when the order is unchanged.
    private static void StampHeld(BotContext ctx, GroupOrder o)
    {
        switch (o.Phase)
        {
            case GroupPhase.Objective:
                var d = o.Objective;
                ctx.SetObjective(Objective.Grind(ObjectiveSource.Coordinator, d.CreatureEntry,
                    d.X, d.Y, d.Z, d.Map, 0, d.QuestId, d.Slot));   // killCount 0 = indefinite (coordinator gate owns completion)
                break;
            case GroupPhase.HoldAtAnchor:
                if (o.Objective.IsActive)
                {
                    var h = o.Objective;
                    ctx.SetObjective(Objective.Grind(ObjectiveSource.Coordinator, h.CreatureEntry,
                        h.X, h.Y, h.Z, h.Map, 0, h.QuestId, h.Slot));
                }
                else
                {
                    ctx.SetObjective(Objective.Hold(o.TargetPos));
                }
                break;
            case GroupPhase.TravelToGiver:
            case GroupPhase.TravelToTurnIn:
                ctx.SetObjective(Objective.Travel(ObjectiveSource.Coordinator,
                    o.TargetPos.X, o.TargetPos.Y, o.TargetPos.Z, o.TargetPos.Map, o.TargetNpcEntry));
                break;
            case GroupPhase.Accept:
            case GroupPhase.TurnIn:
                ctx.SetObjective(Objective.Hold(o.TargetPos));   // at the NPC interacting — passive, not reconciled
                break;
            default:
                ctx.ClearObjective();   // Forming (transient) / None / unhandled → no committed objective this tick
                break;
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    // The phase machine. Returns the single GroupOrder to stamp on every member.
    // ────────────────────────────────────────────────────────────────────────
    private static GroupOrder DriveGroup(
        GroupPlan plan, List<BotContext> members, int anchorGuid,
        QuestGraphLoader quests, ZoneSafetyMap safety)
    {
        var anchor = AnchorOf(members, anchorGuid);
        var prevPhase = plan.Phase;   // instrumentation: phase BEFORE this tick's decision

        // ── Peel preemption (§4) ──
        // A peeled (recovering) member: the REST hold on the same target at the anchor. The
        // recovering member's own GoalSelector routes it to Maintenance regardless of this stamp
        // (recovery-first is non-negotiable, §4) -- so stamping HoldAtAnchor on all is correct.
        if (AnyRecovering(members))
        {
            var dead = string.Join(",", members.Where(m => m.Dead).Select(m => $"{m.Guid}(hp{(int)(m.HpPct * 100)})"));
            Emit(anchorGuid, prevPhase, GroupPhase.HoldAtAnchor, $"DOOR1 peel recovering={dead}", members);
            return HoldAtAnchor(plan, anchor);
        }

        // v1 SCOPE: this coordinator drives grouped QUESTING (the design's 5-file build order:
        // GroupPlan -> BotContext -> GroupCoordinator -> QuestPlanner -> GoalSelector). Group-
        // coordinated MAINTENANCE -- the whole-group vendor / repair / 2-level training errands of
        // §4 -- is a documented FOLLOW-ON: realizing it correctly means routing the GroupVendor /
        // GroupTrain phases through the existing MaintenancePlanner (ctx.Service / VendorPhase) and
        // TrainingPlanner (ctx.Train), pointed at a group-stamped target, plus moving the group route
        // above GoalSelector's solo durability gate -- both OUTSIDE this scope. Until then a grouped
        // member that needs a vendor or a trainer peels via its own GoalSelector triggers in the gaps
        // between group quest cycles (functional; just not yet "together"). The GroupVendor /
        // GroupTrain enum values stay reserved for that follow-on; this driver never enters them.

        // Per-member acceptability -- one graph scan each (matches GoalSelector's per-bot cost).
        var avail = ComputeAvail(members, quests);

        // ── Pool lifecycle ──
        if (plan.Phase == GroupPhase.None || plan.Pool.Count == 0)
            Forming(plan, anchor, members, avail, quests);
        if (plan.Pool.Count == 0)
        {
            Emit(anchorGuid, prevPhase, GroupPhase.None, "pool=0 no-local-work", members);
            return GroupOrder.None;   // no shared, local quest work -> members solo-grind (combat-assist stays)
        }

        // ── Worklist walk (live-derived order; the objective is latched) ──

        // (a) Accepts -- visit each giver that still has a pending eligible accept, together.
        var giver = NextGiver(plan, anchor, members, avail, quests);
        if (giver != null)
        {
            if (AllWithinReach(members, giver, ArrivalReachYards))
            {
                Emit(anchorGuid, prevPhase, GroupPhase.Accept, $"giver={giver.NpcEntry} allInReach=T", members);
                return ToNpc(plan, GroupPhase.Accept, anchorGuid, giver);
            }
            if (PathSafeForWeakest(members, anchor, giver, safety))
            {
                int dg = safety.IsLoaded ? safety.GetMaxCreatureLevelOnPath(anchor.MapId, anchor.Pos.X, anchor.Pos.Y, giver.X, giver.Y) : -1;
                Emit(anchorGuid, prevPhase, GroupPhase.TravelToGiver, $"giver={giver.NpcEntry} pathSafe=T danger={dg} weakest={members.Min(m => m.Level)} safetyLoaded={safety.IsLoaded}", members);
                return ToNpc(plan, GroupPhase.TravelToGiver, anchorGuid, giver);
            }
            // unsafe for the weakest -> don't march in; fall through to a maybe-safer objective / turn-in.
        }

        // (b) Objective -- grind the nearest incomplete shared mob together (latched).
        var obj = NextObjective(plan, anchor, members, quests);
        if (obj.IsActive)
        {
            plan.LatchedObjective = obj;
            plan.Cursor = obj.QuestId;
            plan.SetPhase(GroupPhase.Objective);
            Emit(anchorGuid, prevPhase, GroupPhase.Objective, $"quest={obj.QuestId} cre={obj.CreatureEntry} slot={obj.Slot}", members);
            return GroupOrder.Engage(anchorGuid, obj);
        }

        // (c) Turn-ins -- visit each ender holding a complete pool quest, together.
        var ender = NextEnder(plan, anchor, members, quests);
        if (ender != null)
        {
            if (AllWithinReach(members, ender, ArrivalReachYards))
            {
                Emit(anchorGuid, prevPhase, GroupPhase.TurnIn, $"ender={ender.NpcEntry} allInReach=T", members);
                return ToNpc(plan, GroupPhase.TurnIn, anchorGuid, ender);
            }
            if (PathSafeForWeakest(members, anchor, ender, safety))
            {
                int de = safety.IsLoaded ? safety.GetMaxCreatureLevelOnPath(anchor.MapId, anchor.Pos.X, anchor.Pos.Y, ender.X, ender.Y) : -1;
                Emit(anchorGuid, prevPhase, GroupPhase.TravelToTurnIn, $"ender={ender.NpcEntry} pathSafe=T danger={de} weakest={members.Min(m => m.Level)}", members);
                return ToNpc(plan, GroupPhase.TravelToTurnIn, anchorGuid, ender);
            }
        }

        // (d) Nothing actionable this tick.
        //   • There IS work but it is gated unsafe for the weakest -> hold at the anchor (§5.1);
        //     the objective latch (if any) keeps the rest productive.
        if (giver != null || ender != null)
        {
            int gd = (giver != null && safety.IsLoaded) ? safety.GetMaxCreatureLevelOnPath(anchor.MapId, anchor.Pos.X, anchor.Pos.Y, giver.X, giver.Y) : -1;
            int ed = (ender != null && safety.IsLoaded) ? safety.GetMaxCreatureLevelOnPath(anchor.MapId, anchor.Pos.X, anchor.Pos.Y, ender.X, ender.Y) : -1;
            string gs = giver != null ? $"giver={giver.NpcEntry}@({giver.X:F0},{giver.Y:F0}) inReach={AllWithinReach(members, giver, ArrivalReachYards)} danger={gd}" : "giver=-";
            string es = ender != null ? $"ender={ender.NpcEntry} inReach={AllWithinReach(members, ender, ArrivalReachYards)} danger={ed}" : "ender=-";
            Emit(anchorGuid, prevPhase, GroupPhase.HoldAtAnchor, $"DOOR2 gated weakest={members.Min(m => m.Level)} safetyLoaded={safety.IsLoaded} anchor@({anchor.Pos.X:F0},{anchor.Pos.Y:F0}) {gs} {es}", members);
            return HoldAtAnchor(plan, anchor);
        }
        //   • Otherwise the pool is drained (all accepted, ground, and turned in) -> re-Form next
        //     tick to fold in freshly-unlocked follow-ups (or degrade to None if none remain).
        Emit(anchorGuid, prevPhase, GroupPhase.Forming, "pool drained -> reForm", members);
        plan.ResetForForming();
        return GroupOrder.Forming(anchorGuid);
    }

    // ── Forming: (re)build the union pool, lowest-first, min-headroom, fully-local ──
    private static void Forming(
        GroupPlan plan, BotContext anchor, List<BotContext> members,
        Dictionary<int, HashSet<int>> avail, QuestGraphLoader quests)
    {
        plan.ResetForForming();   // clears Pool / Cursor / LatchedObjective, sets Phase=Forming

        // Union of every present member's currently-acceptable quests PLUS everything already in
        // any member's log (so an in-progress quest stays a group objective, not just new picks).
        var union = new HashSet<int>();
        foreach (var m in members)
        {
            foreach (var qid in avail[m.Guid]) union.Add(qid);
            foreach (var qid in m.QuestLog.Keys) union.Add(qid);
        }

        // Keep only quests the group can FULLY drive on the anchor's current map this cycle:
        // giver + turn-in on-map, AND at least one on-map objective the group's grind can satisfy --
        // a creature KILL objective, OR a kill-for-LOOT objective (an item whose best drop source is a
        // creature on this map; grinding it auto-loots the drop, exactly as the solo planner does) --
        // or no objectives at all (instant-complete). GO-sourced items (herbs / chests) and cross-map
        // are deferred (v1 boundary; matches the solo planner's own BestDropSource phase gate).
        var local = new List<QuestNode>();
        var rejects = new List<string>();   // instrumentation: why each union quest was dropped
        foreach (var qid in union)
        {
            var q = quests.GetQuest(qid);
            if (q == null) { rejects.Add($"{qid}:null"); continue; }
            if (q.Giver == null || q.Giver.Map != anchor.MapId) { rejects.Add($"{qid}:giver-offmap"); continue; }
            if (q.TurnIn == null || q.TurnIn.Map != anchor.MapId) { rejects.Add($"{qid}:turnin-offmap"); continue; }
            bool hasLocalKill = false;
            foreach (var o in q.Objectives)
                if (o.IsCreature && o.Count > 0 && o.GrindMap == anchor.MapId) { hasLocalKill = true; break; }
            bool hasLocalLoot = false;
            if (!hasLocalKill)
                foreach (var it in q.ItemObjectives)
                    if (it.Count > 0 && it.BestDropSource is { } src && src.SpawnCount > 0 && src.GrindMap == anchor.MapId)
                    { hasLocalLoot = true; break; }
            if (!(hasLocalKill || hasLocalLoot || !q.HasObjectives))
            {
                rejects.Add($"{qid}:no-grindable-obj(hasObj={q.HasObjectives} giverDist={(int)Dist2(anchor.Pos.X, anchor.Pos.Y, q.Giver.X, q.Giver.Y)})");
                continue;
            }
            local.Add(q);
        }

        // ── Reach gate ── THE fix: admit only givers within the WEAKEST member's travel cap of the
        // anchor, widening the tier only if nothing is local -- byte-for-byte the same filter solo runs
        // (QuestPlanner.ReachTier + InReach + ZoneSafetyMap.GetMaxTravelDistance). Forming previously
        // admitted by MAP only, so givers 900-10,000+ yds out entered the pool and the lowest-first walk
        // wedged NextGiver on a far giver the §5.1 travel gate then refused. Gating on the weakest member
        // mirrors PathSafeForWeakest (don't drag the low alt across the map). Reusing solo's helpers means
        // the group and solo selectors can never diverge on "what's near" again.
        var weakest = members[0];
        foreach (var m in members) if (m.Level < weakest.Level) weakest = m;
        if (weakest.Identity is { } wid && local.Count > 0)
        {
            int tier = QuestPlanner.ReachTier(local, wid, anchor.Pos.X, anchor.Pos.Y, anchor.MapId, anchor.ZoneId);
            if (tier < 0)
            {
                foreach (var q in local)
                    rejects.Add($"{q.QuestId}:out-of-reach(dist={(int)Dist2(anchor.Pos.X, anchor.Pos.Y, q.Giver!.X, q.Giver!.Y)} noTier)");
                local.Clear();   // nothing reachable even at the widest tier -> empty pool -> members solo-grind (matches solo)
            }
            else
            {
                float cap = ZoneSafetyMap.GetMaxTravelDistance(wid.Level, anchor.ZoneId, tier);
                local.RemoveAll(q =>
                {
                    bool keep = QuestPlanner.InReach(q, anchor.Pos.X, anchor.Pos.Y, anchor.MapId, cap);
                    if (!keep)
                        rejects.Add($"{q.QuestId}:out-of-reach(dist={(int)Dist2(anchor.Pos.X, anchor.Pos.Y, q.Giver!.X, q.Giver!.Y)} cap={(int)cap} tier={tier})");
                    return !keep;
                });
            }
        }

        // Lowest-first (§2): order by quest level ascending so the group works the low member's
        // content first and the level delta shrinks (it composes with the §5.1 travel gate).
        local.Sort((a, b) =>
        {
            int la = a.QuestLevel == 0 ? a.MinLevel : a.QuestLevel;
            int lb = b.QuestLevel == 0 ? b.MinLevel : b.QuestLevel;
            int c = la.CompareTo(lb);
            return c != 0 ? c : a.QuestId.CompareTo(b.QuestId);
        });

        // Quests already held cost no new log slot -- always pool them. New accepts are bounded by
        // min-headroom (§6): the TIGHTEST member's free quest-log slots, never 20×N.
        var heldAny = new HashSet<int>();
        foreach (var m in members)
            foreach (var qid in m.QuestLog.Keys) heldAny.Add(qid);

        int freeSlots = members.Min(m => Math.Max(0, QuestLogCap - m.QuestLog.Count));
        int newAccepts = 0;
        foreach (var q in local)
        {
            if (heldAny.Contains(q.QuestId))
            {
                plan.Pool.Add(q.QuestId);                 // already accepted -> always worked
            }
            else if (newAccepts < freeSlots)
            {
                plan.Pool.Add(q.QuestId);                 // a new accept -> bounded by headroom
                newAccepts++;
            }
        }

        // instrumentation: what did Forming actually admit, and why was the rest dropped?
        var admitted = string.Join(" ", plan.Pool.Select(id =>
        {
            var qq = quests.GetQuest(id);
            int gd = qq?.Giver != null ? (int)Dist2(anchor.Pos.X, anchor.Pos.Y, qq.Giver.X, qq.Giver.Y) : -1;
            int gv = qq?.Giver?.NpcEntry ?? 0;
            int lvl = qq == null ? 0 : (qq.QuestLevel == 0 ? qq.MinLevel : qq.QuestLevel);
            return $"{id}(lvl{lvl},giver={gv},dist={gd})";
        }));
        var line = $"[GROUP-POOL] anchor={anchor.Guid} map={anchor.MapId} weakest={members.Min(m => m.Level)} "
                 + $"union={union.Count} admitted=[{admitted}] rejected=[{string.Join(" ", rejects)}]";
        if (Log != null) Log.LogInformation(line); else Console.WriteLine(line);
    }

    // ── (a) The next giver with a pending, eligible, in-team accept (pool order = lowest-first) ──
    private static QuestNpcLocation? NextGiver(
        GroupPlan plan, BotContext anchor, List<BotContext> members,
        Dictionary<int, HashSet<int>> avail, QuestGraphLoader quests)
    {
        foreach (int qid in plan.Pool)
        {
            var q = quests.GetQuest(qid);
            if (q?.Giver == null || q.Giver.Map != anchor.MapId) continue;

            // Accept-sync (§2): the giver stays a target while ANY present, LIVE, eligible member
            // still owes this accept. A stuck/away member doesn't gate (liveness escape). avail
            // already excludes completed + in-log, so membership == "eligible & unaccepted".
            foreach (var m in members)
            {
                if (IsStuck(m)) continue;
                if (m.QuestLog.ContainsKey(qid)) continue;
                if (avail.TryGetValue(m.Guid, out var a) && a.Contains(qid))
                    return q.Giver;
            }
        }
        return null;
    }

    // ── (b) The latched, else nearest, incomplete shared kill objective on the anchor's map ──
    private static ExecDirective NextObjective(
        GroupPlan plan, BotContext anchor, List<BotContext> members, QuestGraphLoader quests)
    {
        // Hold the latch while any present, live holder still owes kills on it (no per-tick re-pick).
        if (plan.LatchedObjective.IsActive && StillOwed(plan.LatchedObjective, members, quests))
            return plan.LatchedObjective;

        ExecDirective best = ExecDirective.None;
        float bestD = float.MaxValue;
        foreach (int qid in plan.Pool)
        {
            var q = quests.GetQuest(qid);
            if (q == null) continue;

            // Kill objectives: grind the required creature directly.
            foreach (var o in q.Objectives)
            {
                if (!o.IsCreature || o.Count <= 0 || o.GrindMap != anchor.MapId) continue;
                var cand = ExecDirective.Objective(qid, o.Slot, o.CreatureEntry,
                    o.GrindX, o.GrindY, o.GrindZ, o.GrindMap, anchor.Guid);
                if (!StillOwed(cand, members, quests)) continue;   // every live holder done -> skip
                float d = Dist2(anchor.Pos.X, anchor.Pos.Y, o.GrindX, o.GrindY);
                if (d < bestD) { bestD = d; best = cand; }
            }

            // Kill-for-loot objectives: grind the item's best drop creature (auto-loot credits the
            // drop to each holder). Same enriched leg as a kill; the completion gate keys on ItemCounts.
            foreach (var it in q.ItemObjectives)
            {
                if (it.Count <= 0) continue;
                var src = it.BestDropSource;
                if (src == null || src.SpawnCount <= 0 || src.GrindMap != anchor.MapId) continue;
                var cand = ExecDirective.Objective(qid, it.Slot, src.CreatureEntry,
                    src.GrindX, src.GrindY, src.GrindZ, src.GrindMap, anchor.Guid);
                if (!StillOwed(cand, members, quests)) continue;
                float d = Dist2(anchor.Pos.X, anchor.Pos.Y, src.GrindX, src.GrindY);
                if (d < bestD) { bestD = d; best = cand; }
            }
        }
        return best;
    }

    // A present, live holder still owes progress on this objective (eligibility implicit: a non-holder
    // never gates; a stuck holder is dropped by the liveness escape). The directive's creature may
    // satisfy a KILL objective (count from QuestObjective, tracked in MobCounts) and/or a kill-for-LOOT
    // objective (count from QuestItemReq, tracked in ItemCounts) at its slot -- resolve both from the
    // graph and report owed if any matching holder is short on either.
    private static bool StillOwed(ExecDirective o, List<BotContext> members, QuestGraphLoader quests)
    {
        var q = quests.GetQuest(o.QuestId);
        if (q == null) return false;

        QuestObjective? kill = null;
        foreach (var k in q.Objectives)
            if (k.Slot == o.Slot && k.IsCreature && k.CreatureEntry == o.CreatureEntry) { kill = k; break; }

        QuestItemReq? loot = null;
        foreach (var it in q.ItemObjectives)
            if (it.Slot == o.Slot && it.BestDropSource is { } src && src.CreatureEntry == o.CreatureEntry) { loot = it; break; }

        if (kill == null && loot == null) return false;

        foreach (var m in members)
        {
            if (IsStuck(m)) continue;
            if (!m.QuestLog.TryGetValue(o.QuestId, out var e)) continue;   // not a holder
            if (e.Status == QuestStatusComplete) continue;                 // quest fully done
            if (o.Slot < 1) continue;
            if (kill != null && o.Slot <= e.MobCounts.Length && kill.Count - e.MobCounts[o.Slot - 1] > 0)
                return true;
            if (loot != null && o.Slot <= e.ItemCounts.Length && loot.Count - e.ItemCounts[o.Slot - 1] > 0)
                return true;
        }
        return false;
    }

    // ── (c) The next ender holding a complete pool quest on the anchor's map ──
    private static QuestNpcLocation? NextEnder(
        GroupPlan plan, BotContext anchor, List<BotContext> members, QuestGraphLoader quests)
    {
        foreach (int qid in plan.Pool)
        {
            var q = quests.GetQuest(qid);
            if (q?.TurnIn == null || q.TurnIn.Map != anchor.MapId) continue;
            foreach (var m in members)
                if (m.QuestLog.TryGetValue(qid, out var e) && e.Status == QuestStatusComplete)
                    return q.TurnIn;
        }
        return null;
    }

    // ── Whole-group errands (§4) ──

    // The rest hold the latched objective at the anchor while a peeled member recovers.
    private static GroupOrder HoldAtAnchor(GroupPlan plan, BotContext anchor)
    {
        plan.SetPhase(GroupPhase.HoldAtAnchor);
        var anchorPos = new Vec4(anchor.Pos.X, anchor.Pos.Y, anchor.Pos.Z, anchor.MapId);
        return GroupOrder.Hold(anchor.Guid, plan.LatchedObjective, anchorPos);
    }

    // ── Predicates / helpers ──

    private static bool AnyRecovering(List<BotContext> members) => members.Any(m => m.Dead);

    // A member that is dead or hasn't made progress within the liveness window stops gating the
    // group's phases (§6): one frozen member must never freeze the team. Its own progress clock
    // (kill / quest / level / ack) resets this, so it is stateless.
    private static bool IsStuck(BotContext m) => m.Dead || m.TimeSinceProgressSec > GateLivenessSec;

    // Every present, LIVE member is within reach of the NPC on the same map (a stuck/away member is
    // waited-for only up to the liveness escape, then ignored so the group can advance).
    private static bool AllWithinReach(List<BotContext> members, QuestNpcLocation npc, float reach)
    {
        foreach (var m in members)
        {
            if (IsStuck(m)) continue;
            if (m.MapId != npc.Map) return false;
            if (Dist2(m.Pos.X, m.Pos.Y, npc.X, npc.Y) > reach) return false;
        }
        return true;
    }

    // §5.1 weakest-member travel gate: don't march the group to a target whose path (from the anchor)
    // runs through creatures above the WEAKEST present member's safe band. Acceptance stays per-member;
    // only the group's TRAVEL TARGET is gated. Degrades open if the grid isn't loaded.
    private static bool PathSafeForWeakest(List<BotContext> members, BotContext anchor, QuestNpcLocation target, ZoneSafetyMap safety)
    {
        if (target.Map != anchor.MapId) return false;   // cross-map travel is per-bot, later
        if (!safety.IsLoaded) return true;
        int weakest = members.Min(m => m.Level);
        int danger = safety.GetMaxCreatureLevelOnPath(anchor.MapId, anchor.Pos.X, anchor.Pos.Y, target.X, target.Y);
        return danger <= weakest + TravelSafetyMargin;
    }

    // Build, per present member, the set of quest ids it can accept RIGHT NOW (excludes completed +
    // in-log; respects race/class/level/prereqs/giver). One scan each -- same cost as GoalSelector.
    private static Dictionary<int, HashSet<int>> ComputeAvail(List<BotContext> members, QuestGraphLoader quests)
    {
        var byGuid = new Dictionary<int, HashSet<int>>(members.Count);
        foreach (var m in members)
        {
            var id = m.Identity;
            if (id == null) { byGuid[m.Guid] = new HashSet<int>(); continue; }
            int raceBit = QuestGraphLoader.RaceToBitmask(id.Race);
            int classBit = QuestGraphLoader.ClassToBitmask(id.ClassId);
            var active = new HashSet<int>(m.QuestLog.Keys);
            var avail = quests.GetAvailableQuests(raceBit, classBit, id.Level, id.CompletedQuestIds, active);
            byGuid[m.Guid] = avail.Select(q => q.QuestId).ToHashSet();
        }
        return byGuid;
    }

    // Stamp a travel-or-interact phase keyed to an NPC, recording the phase on the plan.
    private static GroupOrder ToNpc(GroupPlan plan, GroupPhase phase, int anchorGuid, QuestNpcLocation npc)
    {
        plan.SetPhase(phase);
        return GroupOrder.ToNpc(phase, anchorGuid, npc.NpcEntry, new Vec4(npc.X, npc.Y, npc.Z, npc.Map));
    }

    // Highest level wins; lowest guid breaks ties (stable tick-to-tick). Members is non-empty.
    private static int ElectAnchor(List<BotContext> members)
    {
        var anchor = members[0];
        foreach (var ctx in members)
            if (ctx.Level > anchor.Level || (ctx.Level == anchor.Level && ctx.Guid < anchor.Guid))
                anchor = ctx;
        return anchor.Guid;
    }

    private static BotContext AnchorOf(List<BotContext> members, int anchorGuid)
    {
        foreach (var m in members)
            if (m.Guid == anchorGuid) return m;
        return members[0];
    }

    private static float Dist2(float ax, float ay, float bx, float by)
    {
        float dx = ax - bx, dy = ay - by;
        return MathF.Sqrt(dx * dx + dy * dy);
    }
}