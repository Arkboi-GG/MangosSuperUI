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
    private const int GroupTrainLevelGap = 2;         // §4: every present member must clear TrainBaselineLevel + this before a training round opens

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
        ZoneSafetyMap safety,
        CreatureSpawnLoader spawns,   // Scatter Build 2: real-spawn anchor sampling for the shared objective
        QuestPlanner questPlanner)    // §Option A (2026-07-01): drives the group's shared decisions through
                                      // the REAL solo decision machinery instead of a hand-rolled parallel one
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

            var order = DriveGroup(group.Plan, members, anchorGuid, quests, safety, spawns, questPlanner);
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
            case GroupPhase.GroupTrain:
                // No NPC target (each trainee routes to its OWN class trainer) -- unlike HoldAtAnchor,
                // there's no anchor coord to fall back to when nothing's latched, so a member with no
                // mob to grind gets no committed objective at all (the reconcile has nothing to defend,
                // exactly like Forming/None below).
                if (o.Objective.IsActive)
                {
                    var t = o.Objective;
                    ctx.SetObjective(Objective.Grind(ObjectiveSource.Coordinator, t.CreatureEntry,
                        t.X, t.Y, t.Z, t.Map, 0, t.QuestId, t.Slot));
                }
                else
                {
                    ctx.ClearObjective();
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
        QuestGraphLoader quests, ZoneSafetyMap safety, CreatureSpawnLoader spawns, QuestPlanner questPlanner)
    {
        var anchor = AnchorOf(members, anchorGuid);
        var prevPhase = plan.Phase;   // instrumentation: phase BEFORE this tick's decision

        // ── Peel preemption (§4) ──
        // A peeled (recovering) member: the REST hold on the same target at the anchor. The
        // recovering member's own GoalSelector routes it to Maintenance/Training regardless of
        // this stamp (recovery/upkeep-first is non-negotiable, §4) -- so stamping HoldAtAnchor on
        // all is correct. AnyRecovering catches death, a vendor/repair errand, AND a training trip
        // (§4) -- any of the three means this member is off running its OWN planner right now.
        if (AnyRecovering(members))
        {
            var peeled = string.Join(",", members
                .Where(m => m.Dead || m.Goal == Goal.Maintenance || m.Goal == Goal.Training)
                .Select(m => $"{m.Guid}({(m.Dead ? $"dead,hp{(int)(m.HpPct * 100)}" : m.Goal.ToString().ToLowerInvariant())})"));
            Emit(anchorGuid, prevPhase, GroupPhase.HoldAtAnchor, $"DOOR1 peel recovering={peeled}", members);
            return HoldAtAnchor(plan, anchor);
        }

        // ── Group-gated training window (§4) ──
        // "Every 2 levels, together" -- not the per-bot spawn reflex the individual trigger normally
        // is. Reached only when AnyRecovering is false, i.e. nobody is CURRENTLY mid-trip -- so this
        // only meaningfully fires on the tick a round OPENS (the first trainee's very next tick flips
        // Goal.Training, which flips AnyRecovering true and this block is skipped on every subsequent
        // tick until the whole party is back).
        //
        // Lazy-seed the baseline the FIRST time this group is ever evaluated (0 = genuinely never
        // seeded -- real levels start at 1, so 0 is a safe "unset" sentinel, and it only reads 0 until
        // the first round below sets it for real). Without this a fresh L1 party would ding to L2
        // together and read "everyone already clears baseline(0)+2" on its very FIRST level-up -- the
        // exact per-bot-spawn-reflex bum-rush this phase exists to prevent, just moved from "on
        // connect" to "on first ding." Seeding to the CURRENT min level means the clock always starts
        // from wherever the party actually is, whether that's a fresh L1 spawn (next round needs L3)
        // or an already-leveled group squadded up for the first time (next round needs current+2, not
        // an immediate trip on tick one). This composes for free with the L1 case specifically: a
        // seeded baseline is always >= 1, so the gate (baseline+2) can never be satisfied by a L1
        // member -- no separate "skip at level 1" special-case needed.
        if (plan.TrainBaselineLevel == 0)
            plan.TrainBaselineLevel = members.Min(m => m.Level);

        // Every present member must have cleared TrainBaselineLevel + GroupTrainLevelGap; if nobody
        // actually owes a visit (HasUnlearnedSpells) the level bar is met for nothing to do, so just
        // advance the baseline without forcing a trip.
        if (members.All(m => m.Level >= plan.TrainBaselineLevel + GroupTrainLevelGap))
        {
            if (members.Any(m => m.Identity is { HasUnlearnedSpells: true }))
            {
                plan.TrainBaselineLevel = members.Min(m => m.Level);   // lock the floor for this round
                plan.SetPhase(GroupPhase.GroupTrain);
                Emit(anchorGuid, prevPhase, GroupPhase.GroupTrain,
                    $"train-window open baseline={plan.TrainBaselineLevel} minLvl={members.Min(m => m.Level)}", members);
                return GroupOrder.Train(anchorGuid, plan.LatchedObjective);
            }
            // Level bar cleared but nobody has anything new to learn (e.g. no rank at this bracket
            // for any present class) -- reset the cadence clock so we don't re-derive this every tick,
            // but don't force a pointless trainer trip.
            plan.TrainBaselineLevel = members.Min(m => m.Level);
        }

        // v1 SCOPE: this coordinator drives grouped QUESTING (via the virtual member, §Option A below)
        // plus the §4 group-gated TRAINING window above. Group-coordinated MAINTENANCE -- the
        // whole-group vendor/repair errand of §4 -- is still a documented FOLLOW-ON: routing GroupVendor
        // through MaintenancePlanner, above GoalSelector's solo durability gate, is OUTSIDE this scope.
        // Until then a grouped member that needs a vendor peels via its own GoalSelector trigger, and
        // the rest of the team HOLDS for it via AnyRecovering above (same as a death or a training trip).

        return DriveGroupViaVirtual(plan, members, anchorGuid, anchor, quests, safety, questPlanner, prevPhase);
    }

    // ════════════════════════════════════════════════════════════════════════════════════════
    // §Option A (2026-07-01) — the virtual member.
    //
    // Replaces the old hand-rolled Forming/GatherLocalPool/NextGiver/NextObjective/NextEnder/
    // StillOwed/ComputeAvail chain (all removed). Every one of those was GroupCoordinator's OWN
    // re-derivation of a fragment of what QuestPlanner's solo machinery already does correctly --
    // PriorityLeg's started>level>band>dist ordering, TagOutliers' red-deprioritize, GatherLocals'
    // R40 co-located-follow-up drain, the turn-in-yield check, overflow-grind, the grind-lock
    // invariant -- and every one of those fragments has independently drifted from solo at some
    // point this session (that's the actual root cause behind every "group behaves differently
    // from solo" symptom found so far). The fix: stop re-deriving. Drive a persistent SYNTHETIC
    // BotContext (GroupPlan.Virtual) through QuestPlanner.PlanNext directly -- the exact same
    // Derive/BuildBatch/GatherLocals/PriorityLeg/TagOutliers/Recover a real solo bot runs, zero
    // reimplementation, zero drift possible by construction.
    //
    // The virtual bot never has a real bridge connection. Each tick its sensory state (Pos/
    // QuestLog/Level) is refreshed from the UNION of present real members (RefreshVirtualSensory,
    // via ctx.Sense -- reused unchanged), and its durable exclusion state (CompletedQuestIds/
    // DeferredQuestIds/AbandonedGreyQuestIds/PathBlacklist) is unioned from every present member's
    // own BotIdentity ("defer for all", 2026-07-01: any ONE member's exclusion applies group-wide --
    // the conservative default). GroupOrder is NEVER set on the virtual ctx, which is what keeps
    // QuestPlanner.PlanNext routing it through the solo decision path instead of recursing back into
    // this file's own DriveGroup.
    //
    // The ONE genuinely new piece of logic -- because it has no solo analog -- is eligibility: which
    // NEW quests are offerable to AT LEAST ONE present member (GatherLocals is hardcoded to one
    // ctx.Identity and can't see this on its own), and which REAL members still owe a specific
    // accept/turn-in/kill before the virtual bot's WAIT is allowed to resolve. That's exactly the
    // "checked during quest accepts and eligibility for which group members are actually able" carve-
    // out. Everything else -- what to accept, what order to work it in, when to abandon a grey quest,
    // when to overflow-grind a stale count, when to grind-lock -- is the real QuestPlanner deciding,
    // not this file.
    //
    // A StepResult.Issue never becomes a real bridge send for the virtual bot: BuildGroupOrderFromVirtual
    // translates it into the SAME GroupOrder stamps this coordinator already produced before (ToNpc /
    // Engage / Hold) -- real per-member eligibility is then checked exactly where it always was,
    // untouched, in QuestPlanner's own GroupAccept/GroupTurnIn/GroupObjective/GroupHold executor.
    // ════════════════════════════════════════════════════════════════════════════════════════

    private const int GroupInjectCap = 8;          // mirrors QuestPlanner.BatchCap -- ceiling on how many freshly-eligible quests RefreshVirtualEligibility injects per tick
    private const float GroupInjectRadiusYards = 300f;   // loose locality gate for injection only; PriorityLeg (inside Derive) does the real near/far ordering once a candidate is in the batch

    private static GroupOrder DriveGroupViaVirtual(
        GroupPlan plan, List<BotContext> members, int anchorGuid, BotContext anchor,
        QuestGraphLoader quests, ZoneSafetyMap safety, QuestPlanner questPlanner, GroupPhase prevPhase)
    {
        var vctx = GetOrCreateVirtual(plan);
        var vsnap = BuildVirtualSnapshot(anchor, members);
        vctx.Sense(vsnap);                                   // Pos/MapId/ZoneId/Level/QuestLog -- exactly what Derive reads
        RefreshVirtualEligibility(vctx, members, quests);    // union exclusions + inject newly-eligible-for-someone content

        // Resolve any in-flight virtual WAIT against REAL group state first. Still outstanding ->
        // re-stamp the SAME order (idempotent; real members' own LastGroupOrder change-guard no-ops on
        // an unchanged stamp) and stop here without asking Derive for a fresh decision this tick.
        if (vctx.Pending != null)
        {
            if (!TryResolveVirtualWait(plan, vctx, members, quests))
                return BuildGroupOrderFromVirtual(plan, vctx, anchor, anchorGuid, members, safety, prevPhase);

            if (vctx.Pending != null && vctx.Pending.Expired)   // universal deadline backstop, mirrors BotBrain step 3b
            {
                vctx.Failure ??= new WaitFailure { CommandType = vctx.Pending.CommandType, Reason = "deadline", Utc = DateTime.UtcNow };
                vctx.Pending = null;
            }
        }

        var step = questPlanner.PlanNext(vctx, vsnap);

        switch (step)
        {
            case StepResult.Issue issue:
                ArmVirtualPending(plan, vctx, issue.Command, issue.ExpectedEvent, issue.Deadline);
                break;
            case StepResult.Blocked:
            case StepResult.Done:
                plan.LatchedObjective = ExecDirective.None;
                plan.SetPhase(GroupPhase.None);
                Emit(anchorGuid, prevPhase, GroupPhase.None, "virtual: no_quests -> solo-grind", members);
                return GroupOrder.None;
                // Dispatch (fire-and-forget, e.g. ABANDON_QUEST grey-drop) and Continue: nothing to arm;
                // fall through and let BuildGroupOrderFromVirtual read whatever vctx.Step/Quest.Active
                // Derive left behind (a grey-drop mutates the batch/Identity directly, no group translation
                // needed -- open item, see the note at ABANDON_QUEST below).
        }

        return BuildGroupOrderFromVirtual(plan, vctx, anchor, anchorGuid, members, safety, prevPhase);
    }

    // Lazily create the persistent virtual member. Deliberately NOT reset by anything in this file --
    // its accrued state (deferrals, overflow-grind attempts, the in-flight leg) is exactly as durable
    // as a real bot's own BotIdentity/QuestScratch.
    private static BotContext GetOrCreateVirtual(GroupPlan plan)
    {
        if (plan.Virtual == null)
        {
            var vctx = new BotContext { Guid = -1, Name = "<virtual>" };
            vctx.Identity = new BotIdentity { Guid = -1, Name = "<virtual>" };
            plan.Virtual = vctx;
            // GroupOrder is left at its default None forever -- that is what routes
            // QuestPlanner.PlanNext through the solo path instead of back into DriveGroup.
        }
        return plan.Virtual;
    }

    // Sensory snapshot for the virtual bot: Pos = anchor (the group's reference point, same as every
    // other reach/safety gate in this file already uses), Level = WEAKEST present member (so
    // grey/red/reach checks protect the low member, matching PathSafeForWeakest's existing bias),
    // QuestLog = union of every present member's own log (a quest is "in the log" if ANY holder has
    // it; per-slot MobCounts/ItemCounts = MAX across holders -- shared kill-credit should keep these
    // roughly equal in practice, MAX is the safe read if it doesn't). Health/mana always full and
    // never dead -- the virtual bot itself is never the reason a leg stalls; real member health is
    // MaintenancePlanner's job via the normal AnyRecovering peel above.
    private static BotStateSnapshot BuildVirtualSnapshot(BotContext anchor, List<BotContext> members)
    {
        var merged = new Dictionary<int, QuestLogEntry>();
        foreach (var m in members)
        {
            foreach (var kv in m.QuestLog)
            {
                if (!merged.TryGetValue(kv.Key, out var e))
                {
                    merged[kv.Key] = new QuestLogEntry
                    {
                        Status = kv.Value.Status,
                        MobCounts = (int[])kv.Value.MobCounts.Clone(),
                        ItemCounts = (int[])kv.Value.ItemCounts.Clone()
                    };
                }
                else
                {
                    if (kv.Value.Status > e.Status) e.Status = kv.Value.Status;   // COMPLETE(1) beats INCOMPLETE(3)? see note below
                    for (int i = 0; i < 4 && i < e.MobCounts.Length && i < kv.Value.MobCounts.Length; i++)
                        e.MobCounts[i] = Math.Max(e.MobCounts[i], kv.Value.MobCounts[i]);
                    for (int i = 0; i < 4 && i < e.ItemCounts.Length && i < kv.Value.ItemCounts.Length; i++)
                        e.ItemCounts[i] = Math.Max(e.ItemCounts[i], kv.Value.ItemCounts[i]);
                }
            }
        }
        // Status merge note: VMaNGOS's enum is NOT ordered by "more done" (COMPLETE=1, INCOMPLETE=3,
        // UNAVAILABLE=2) -- ">" is meaningless across them. What we actually want is "COMPLETE wins if
        // ANY holder reads COMPLETE", so fix the merge explicitly rather than trust numeric ordering.
        foreach (var m in members)
            foreach (var kv in m.QuestLog)
                if (kv.Value.Status == 1 && merged.TryGetValue(kv.Key, out var e))
                    e.Status = 1;

        int weakestLevel = members.Min(m => m.Level);
        return new BotStateSnapshot
        {
            Health = 100,
            MaxHealth = 100,
            Mana = 100,
            MaxMana = 100,
            Level = weakestLevel,
            MapId = anchor.MapId,
            ZoneId = anchor.ZoneId,
            X = anchor.Pos.X,
            Y = anchor.Pos.Y,
            Z = anchor.Pos.Z,
            InCombat = false,
            IsDead = false,
            FreeSlots = (uint)Math.Max(0, members.Min(m => QuestLogCap - m.QuestLog.Count)),
            TotalSlots = (uint)QuestLogCap,
            QuestLog = merged,
            StateUtc = DateTime.UtcNow
        };
    }

    // Union the durable exclusion state ("defer for all") and inject newly-eligible-for-someone
    // content. This is the one place group-specific eligibility logic belongs (§ above).
    private static void RefreshVirtualEligibility(BotContext vctx, List<BotContext> members, QuestGraphLoader quests)
    {
        var vid = vctx.Identity!;
        vid.Level = vctx.Level;

        foreach (var m in members)
        {
            var id = m.Identity;
            if (id == null) continue;
            foreach (var qid in id.CompletedQuestIds) vid.CompletedQuestIds.Add(qid);
            foreach (var qid in id.AbandonedGreyQuestIds) vid.AbandonedGreyQuestIds.Add(qid);
            foreach (var kv in id.DeferredQuestIds)
                if (!vid.DeferredQuestIds.ContainsKey(kv.Key)) vid.DeferredQuestIds[kv.Key] = kv.Value;
            foreach (var kv in id.PathBlacklist)
                if (!vid.PathBlacklist.TryGetValue(kv.Key, out var d) || kv.Value > d)
                    vid.PathBlacklist[kv.Key] = kv.Value;
        }
        vid.PruneExpiredDeferrals();
        vid.PrunePathBlacklist();

        if (!quests.IsLoaded) return;
        var q = vctx.Quest;
        if (q == null) return;   // BuildBatch hasn't run yet this cycle (first-ever tick) -- nothing to inject into
        var have = q.Batch.Select(b => b.QuestId).ToHashSet();
        int injected = 0;

        foreach (var m in members)
        {
            if (q.Batch.Count >= GroupInjectCap || injected >= GroupInjectCap) break;
            var id = m.Identity;
            if (id == null) continue;
            int raceBit = QuestGraphLoader.RaceToBitmask(id.Race);
            int classBit = QuestGraphLoader.ClassToBitmask(id.ClassId);
            var active = new HashSet<int>(m.QuestLog.Keys);
            foreach (var node in quests.GetAvailableQuests(raceBit, classBit, id.Level, id.CompletedQuestIds, active))
            {
                if (q.Batch.Count >= GroupInjectCap || injected >= GroupInjectCap) break;
                if (have.Contains(node.QuestId)) continue;
                if (!QuestPlanner.IsPickable(node, id)) continue;               // THIS member's own eligibility (grey/red/blacklist/etc for them)
                if (vid.DeferredQuestIds.ContainsKey(node.QuestId)) continue;    // group-level defer (any member) still applies
                if (vid.AbandonedGreyQuestIds.Contains(node.QuestId)) continue;
                if (node.Giver == null || node.Giver.Map != vctx.MapId) continue;
                if (Dist2(vctx.Pos.X, vctx.Pos.Y, node.Giver.X, node.Giver.Y) > GroupInjectRadiusYards) continue;

                q.Batch.Add(new BatchQuest { QuestId = node.QuestId, Node = node, Accepted = false });
                have.Add(node.QuestId);
                injected++;
            }
        }
    }

    // Given the virtual bot's CURRENT in-flight WAIT (vctx.Pending), decide whether REAL group state
    // now satisfies it. True = resolved (Pending cleared, MarkProgress'd, safe to ask Derive for the
    // next step this same tick). False = still outstanding (caller re-stamps the same order).
    private static bool TryResolveVirtualWait(GroupPlan plan, BotContext vctx, List<BotContext> members, QuestGraphLoader quests)
    {
        var p = vctx.Pending;
        if (p == null) return true;
        var active = vctx.Quest?.Active;

        if (p.CommandType == "MOVE_TO" && p.IsObjectiveGrind)
        {
            if (active == null || plan.LastVirtualCommand == null) { vctx.Pending = null; return true; }
            if (!TryExtractCoords(plan.LastVirtualCommand, out _, out _, out _, out _, out int creatureEntry))
            { vctx.Pending = null; return true; }

            // Which objective/item slot(s) does this creature_entry actually satisfy? (ActiveSlot is
            // NOT usable here -- DispatchObjectiveLeg hardcodes it to 0 for the normal dispatch path;
            // "legs aren't slot-routed" per its own comment. Match on creature_entry directly instead,
            // exactly what the leg was dispatched on.)
            var killSlots = active.Node.Objectives.Where(o => o.IsCreature && o.CreatureEntry == creatureEntry)
                                                   .Select(o => o.Slot).ToList();
            var itemSlots = active.Node.ItemObjectives.Where(it =>
                                    it.BestDropSource?.CreatureEntry == creatureEntry ||
                                    (it.AltDropEntries?.Contains(creatureEntry) ?? false))
                                                   .Select(it => it.Slot).ToList();
            if (killSlots.Count == 0 && itemSlots.Count == 0) { vctx.Pending = null; return true; }   // can't identify the leg -- don't wedge

            bool anyoneStillOwes = members.Any(m =>
            {
                if (m.Dead || m.TimeSinceProgressSec > GateLivenessSec) return false;   // liveness escape, mirrors §6
                if (!m.QuestLog.TryGetValue(active.QuestId, out var e)) return false;   // not a holder
                if (e.Status == QuestStatusComplete) return false;
                foreach (var slot in killSlots)
                {
                    if (slot < 1 || slot > e.MobCounts.Length) continue;
                    var obj = active.Node.Objectives.First(o => o.Slot == slot);
                    if (obj.Count > e.MobCounts[slot - 1]) return true;
                }
                foreach (var slot in itemSlots)
                {
                    if (slot < 1 || slot > e.ItemCounts.Length) continue;
                    var it = active.Node.ItemObjectives.First(x => x.Slot == slot);
                    if (it.Count > e.ItemCounts[slot - 1]) return true;
                }
                return false;
            });
            if (anyoneStillOwes) return false;
            vctx.Pending = null;
            vctx.MarkProgress();
            return true;
        }

        if (p.CommandType == "MOVE_TO")
        {
            // Plain travel (to_giver / to_turnin): resolved once the WHOLE present group has arrived.
            var npc = vctx.Step == "to_giver" ? active?.Node.Giver : (active?.Node.TurnIn ?? active?.Node.Giver);
            if (npc == null) { vctx.Pending = null; return true; }
            if (!AllWithinReach(members, npc, ArrivalReachYards)) return false;
            vctx.Pending = null;
            vctx.MarkProgress();
            return true;
        }

        if (p.CommandType == "QUEST_INTERACT")
        {
            int? qid = p.QuestId;
            if (qid == null) { vctx.Pending = null; return true; }
            bool accept = p.ExpectedEvent == "QUEST_ACCEPT_ACK";
            bool anyoneStillOwes = members.Any(m =>
            {
                var id = m.Identity;
                if (id == null) return false;
                if (accept)
                {
                    if (m.QuestLog.ContainsKey(qid.Value)) return false;
                    int raceBit = QuestGraphLoader.RaceToBitmask(id.Race);
                    int classBit = QuestGraphLoader.ClassToBitmask(id.ClassId);
                    var activeIds = new HashSet<int>(m.QuestLog.Keys);
                    // NOTE: intentionally NOT gated on QuestPlanner.IsPickable here -- once the virtual
                    // bot has committed to accepting this quest, a member who's simply eligible per the
                    // graph (race/class/level/prereqs) should accept it too, even if some OTHER solo-only
                    // pick filter would have deprioritized it for them individually.
                    return quests.GetAvailableQuests(raceBit, classBit, id.Level, id.CompletedQuestIds, activeIds)
                                 .Any(n => n.QuestId == qid.Value);
                }
                return m.QuestLog.TryGetValue(qid.Value, out var e) && e.Status == QuestStatusComplete;
            });
            if (anyoneStillOwes) return false;
            vctx.Pending = null;
            vctx.MarkProgress();
            return true;
        }

        vctx.Pending = null;   // an unhandled command type (shouldn't happen) -- don't wedge forever
        return true;
    }

    // Mirrors BotExecutor.IssueAsync's Outstanding construction, minus the actual bridge send (the
    // virtual bot has no real connection). LastVirtualCommand carries the payload BuildGroupOrderFromVirtual
    // needs for the enriched-objective case (x/y/z/creature_entry aren't retained on Outstanding itself).
    private static void ArmVirtualPending(GroupPlan plan, BotContext vctx, BridgeCommand cmd, string expectedEvent, TimeSpan deadline)
    {
        var now = DateTime.UtcNow;
        bool objectiveGrind = cmd.Type == "MOVE_TO" && cmd.Payload.ContainsKey("creature_entry");
        int? questId = null;
        if (cmd.Type == "QUEST_INTERACT" && cmd.Payload.TryGetValue("quest_id", out var qo) && qo is IConvertible)
            questId = Convert.ToInt32(qo);

        vctx.Pending = new Outstanding
        {
            CommandType = cmd.Type,
            ExpectedEvent = expectedEvent,
            SentUtc = now,
            DeadlineUtc = now + deadline,
            IsObjectiveGrind = objectiveGrind,
            QuestId = questId
        };
        plan.LastVirtualCommand = cmd;
    }

    // Translate the virtual bot's CURRENT state (Step / Pending / Quest.Active) into the GroupOrder to
    // stamp on real members this tick. Called both right after a fresh Issue (arms + translates the
    // same tick) and when re-stamping an unresolved WAIT (reads the same state, produces the same
    // order -- idempotent by construction). This is where the group-only travel-safety gate applies:
    // solo has no concept of "protect the weakest teammate", so PathSafeForWeakest is checked HERE,
    // and an unsafe target is fed back into the virtual ctx as a path_unsafe Failure -- letting the
    // REAL Recover() (blacklist + level-gated defer) handle it next tick, exactly like solo.
    private static GroupOrder BuildGroupOrderFromVirtual(
        GroupPlan plan, BotContext vctx, BotContext anchor, int anchorGuid, List<BotContext> members,
        ZoneSafetyMap safety, GroupPhase prevPhase)
    {
        var active = vctx.Quest?.Active;

        switch (vctx.Step)
        {
            case "to_giver" when active?.Node.Giver != null:
                {
                    var npc = active.Node.Giver;
                    if (!PathSafeForWeakest(members, anchor, npc, safety))
                    {
                        RouteVirtualUnsafe(vctx, npc, anchor, safety);
                        Emit(anchorGuid, prevPhase, GroupPhase.HoldAtAnchor, $"virtual: giver={npc.NpcEntry} unsafe -> path_unsafe defer", members);
                        return HoldAtAnchor(plan, anchor);
                    }
                    // Two-phase, matching the old design: TravelToGiver (StampHeld mirrors this as a
                    // RECONCILABLE Objective.Travel -- the self-heal catches a C++ task silently dropping
                    // mid-walk) until everyone's actually there, THEN Accept (passive Hold; GroupAccept's
                    // own per-member AtNpc/MoveTo already covers the last few individual yards regardless).
                    if (AllWithinReach(members, npc, ArrivalReachYards))
                    {
                        Emit(anchorGuid, prevPhase, GroupPhase.Accept, $"virtual: giver={npc.NpcEntry} q=[{active.QuestId}] allInReach=T", members);
                        return ToNpc(plan, GroupPhase.Accept, anchorGuid, npc);
                    }
                    Emit(anchorGuid, prevPhase, GroupPhase.TravelToGiver, $"virtual: giver={npc.NpcEntry} q=[{active.QuestId}] traveling", members);
                    return ToNpc(plan, GroupPhase.TravelToGiver, anchorGuid, npc);
                }

            case "to_turnin" when active != null && (active.Node.TurnIn ?? active.Node.Giver) != null:
                {
                    var npc = active.Node.TurnIn ?? active.Node.Giver!;
                    if (!PathSafeForWeakest(members, anchor, npc, safety))
                    {
                        RouteVirtualUnsafe(vctx, npc, anchor, safety);
                        Emit(anchorGuid, prevPhase, GroupPhase.HoldAtAnchor, $"virtual: ender={npc.NpcEntry} unsafe -> path_unsafe defer", members);
                        return HoldAtAnchor(plan, anchor);
                    }
                    if (AllWithinReach(members, npc, ArrivalReachYards))
                    {
                        Emit(anchorGuid, prevPhase, GroupPhase.TurnIn, $"virtual: ender={npc.NpcEntry} q=[{active.QuestId}] allInReach=T", members);
                        return ToNpc(plan, GroupPhase.TurnIn, anchorGuid, npc);
                    }
                    Emit(anchorGuid, prevPhase, GroupPhase.TravelToTurnIn, $"virtual: ender={npc.NpcEntry} q=[{active.QuestId}] traveling", members);
                    return ToNpc(plan, GroupPhase.TravelToTurnIn, anchorGuid, npc);
                }

            case "to_objective" when active != null && plan.LastVirtualCommand != null
                                      && TryExtractCoords(plan.LastVirtualCommand, out float x, out float y, out float z, out int map, out int creatureEntry):
                {
                    var dest = new QuestNpcLocation { NpcEntry = 0, X = x, Y = y, Z = z, Map = map };
                    if (!PathSafeForWeakest(members, anchor, dest, safety))
                    {
                        RouteVirtualUnsafe(vctx, dest, anchor, safety);
                        Emit(anchorGuid, prevPhase, GroupPhase.HoldAtAnchor, $"virtual: objective cre={creatureEntry} unsafe -> path_unsafe defer", members);
                        return HoldAtAnchor(plan, anchor);
                    }
                    var directive = ExecDirective.Objective(active.QuestId, 0, creatureEntry, x, y, z, map, anchorGuid);
                    plan.LatchedObjective = directive;
                    plan.SetPhase(GroupPhase.Objective);
                    Emit(anchorGuid, prevPhase, GroupPhase.Objective, $"virtual: quest={active.QuestId} cre={creatureEntry}", members);
                    return GroupOrder.Engage(anchorGuid, directive);
                }

            // 2026-07-01 BUG FIX: "accept" and "turnin" are the step names PlanNext's OWN switch sets
            // — inside its "to_giver"/"to_turnin" cases — in the SAME call that returns the
            // QUEST_INTERACT Issue, once AtNpc(vctx, ...) (checked against the anchor's position) is
            // already true. That Issue was falling into `default` below, completely untranslated: no
            // GroupOrder ever told a real member to actually fire the accept/turn-in, so
            // TryResolveVirtualWait's "does anyone still owe this" check could never resolve --
            // permanent stall, and silently at that (no Emit in the old default branch either). This
            // is what a "totally stalled immediately" fresh group was hitting on the very first accept.
            case "accept" when active?.Node.Giver != null:
                {
                    var npc = active.Node.Giver;
                    Emit(anchorGuid, prevPhase, GroupPhase.Accept, $"virtual: accept giver={npc.NpcEntry} q=[{active.QuestId}]", members);
                    return ToNpc(plan, GroupPhase.Accept, anchorGuid, npc);
                }

            case "turnin" when active != null && (active.Node.TurnIn ?? active.Node.Giver) != null:
                {
                    var npc = active.Node.TurnIn ?? active.Node.Giver!;
                    Emit(anchorGuid, prevPhase, GroupPhase.TurnIn, $"virtual: turnin ender={npc.NpcEntry} q=[{active.QuestId}]", members);
                    return ToNpc(plan, GroupPhase.TurnIn, anchorGuid, npc);
                }

            default:
                // Genuine between-leg transients (obj_sync / detour / grind_obj / plan -- PlanNext
                // returned Continue and will re-derive once external state catches up, e.g. obj_sync
                // waiting for the next STATE heartbeat) or an ABANDON_QUEST grey-drop (open item -- no
                // group translation yet; real members holding the same grey quest drop it via their own
                // independent solo-side grey-drop). Hold at the anchor rather than going fully idle, so
                // a latched objective (if any) keeps the rest productive -- but SAY so, so a stall here
                // is visible in the log instead of silent (the gap that hid the accept/turnin bug above).
                Emit(anchorGuid, prevPhase, GroupPhase.HoldAtAnchor, $"virtual: step={vctx.Step} unhandled -> hold", members);
                return HoldAtAnchor(plan, anchor);
        }
    }

    // Feed a path_unsafe failure back into the virtual ctx -- Recover() (the REAL solo logic) picks
    // this up on the NEXT PlanNext call and blacklists + level-defers it, exactly like a real bot.
    private static void RouteVirtualUnsafe(BotContext vctx, QuestNpcLocation target, BotContext anchor, ZoneSafetyMap safety)
    {
        int danger = safety.IsLoaded
            ? safety.GetMaxCreatureLevelOnPath(anchor.MapId, anchor.Pos.X, anchor.Pos.Y, target.X, target.Y)
            : 0;
        vctx.Pending = null;   // the leg never really committed -- nothing to ack, just fail it now
        vctx.Failure = new WaitFailure
        {
            CommandType = "MOVE_TO",
            Reason = "path_unsafe",
            Dest = new Vec4(target.X, target.Y, 0, target.Map),
            DangerLevel = danger,
            Utc = DateTime.UtcNow
        };
    }

    // Same extraction pattern BotExecutor.ExtractTarget uses -- BridgeCommand.Payload is a flat
    // key/value bag built from an anonymous object, so this is the only way back to the raw
    // coordinates once a StepResult.Issue has been produced.
    private static bool TryExtractCoords(BridgeCommand cmd, out float x, out float y, out float z, out int map, out int creatureEntry)
    {
        x = y = z = 0; map = 0; creatureEntry = 0;
        if (!cmd.Payload.TryGetValue("x", out var xo) || !cmd.Payload.TryGetValue("y", out var yo) || !cmd.Payload.TryGetValue("z", out var zo))
            return false;
        x = ToFloat(xo); y = ToFloat(yo); z = ToFloat(zo);
        if (cmd.Payload.TryGetValue("mapId", out var mo)) map = ToInt(mo);
        if (cmd.Payload.TryGetValue("creature_entry", out var ceo)) creatureEntry = ToInt(ceo);
        return true;
    }

    private static float ToFloat(object o) => o is IConvertible ? Convert.ToSingle(o) : 0f;
    private static int ToInt(object o) => o is IConvertible ? Convert.ToInt32(o) : 0;

    // ── Whole-group errands (§4) ──

    // The rest hold the latched objective at the anchor while a peeled member recovers.
    private static GroupOrder HoldAtAnchor(GroupPlan plan, BotContext anchor)
    {
        plan.SetPhase(GroupPhase.HoldAtAnchor);
        var anchorPos = new Vec4(anchor.Pos.X, anchor.Pos.Y, anchor.Pos.Z, anchor.MapId);
        return GroupOrder.Hold(anchor.Guid, plan.LatchedObjective, anchorPos);
    }

    // ── Predicates / helpers ──

    // A peeled member -- dead (death recovery), or ALIVE but off on its own survival/upkeep
    // errand (a vendor/repair trip under Goal.Maintenance, or a training trip under Goal.Training).
    // All three are solo trips this bot's OWN planner drives outside the group's stamp; without
    // catching the alive cases here the coordinator kept advancing the shared objective as if
    // every member were still present, so a bot mid-vendor-run or mid-trainer-run got left behind
    // ungated (until the §6 liveness escape eventually stopped counting it) instead of the team
    // holding for it the same way it already holds for a death.
    //
    // 2026-07-01: the two ALIVE cases (Maintenance / Training) carry the SAME liveness escape every
    // other gate in this file already uses (GateLivenessSec) -- a vendor/trainer errand that's
    // genuinely wedged (unreachable NPC, dead-end pocket, stuck cooldown loop) must not freeze the
    // whole team FOREVER; its own planner's give-up backstop (VendorRouteGiveupSec / TrainingPlanner's
    // RouteDeadline) still owns actually resolving the wedge -- this only stops it from ALSO
    // deadlocking the group in the meantime. DEATH is deliberately left unconditional, matching the
    // original (pre-this-session) behavior: a slow multi-phase heal-to-full can legitimately run
    // TimeSinceProgressSec past 90s without being stuck, and MaxDeadSec (300s) is already death's own
    // backstop -- narrowing the escape to just the two NEW cases avoids regressing a working path to
    // chase a bug that may not even be there.
    private static bool AnyRecovering(List<BotContext> members)
        => members.Any(m => m.Dead)
           || members.Any(m => (m.Goal == Goal.Maintenance || m.Goal == Goal.Training)
                                && m.TimeSinceProgressSec <= GateLivenessSec);

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