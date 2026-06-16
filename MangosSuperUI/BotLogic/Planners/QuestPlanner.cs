using MangosSuperUI.BotLogic.Core;
using MangosSuperUI.BotLogic.Data;

namespace MangosSuperUI.BotLogic.Planners;

// ============================================================================
// QuestPlanner — Goal.Questing (§ P3, the regression-killer).
//
// A clean redesign of QuestingDomain onto the stateless-advisor contract. Drives
// ONE quest at a time through a flat state machine (ctx.Step):
//
//   pick → to_giver → accept → to_objective → objective → to_turnin → turnin → ↺
//
// The regression was C# control-flow (deferring a quest forever on an unreachable
// NPC instead of escaping), NOT a missing C++ signal. So the spine matters here:
//   • completion is server-authoritative — a kill objective is SET_TASK GRIND with
//     kill_count=N, which C++ acks via TASK_COMPLETE at N kills (no QUEST_STATUS
//     parsing, no local kill-counting);
//   • a path/interact failure NEGATES the WAIT promptly (executor → ctx.Failure)
//     and is handled HERE in PlanNext — force_* for a WMO-interior last leg,
//     level-gated defer for PATH_UNSAFE danger, defer-and-repick otherwise. The
//     bot escapes in seconds; it never wedges, never plateaus.
//
// Durable quest state (completed / deferrals / blacklist) lives on BotIdentity,
// reached via ctx.Identity. Scratch (the working quest) lives in ctx.Quest.
//
// Scope (v1): kill-objective and no-objective quests — every pick is completable
// via kill_count + the turn-in gate. Item/GO/escort quests are filtered out at
// pick time (a later layer); batch + opportunistic objectives are a later layer.
// ============================================================================
public sealed class QuestPlanner : IBotPlanner
{
    private readonly QuestGraphLoader _quests;
    private readonly ILogger<QuestPlanner> _logger;

    private static readonly TimeSpan TravelDeadline = TimeSpan.FromMinutes(8);   // continuation travel can be long (§4.11)
    private static readonly TimeSpan ObjectiveDeadline = TimeSpan.FromMinutes(4);   // grind kill_count; depleted → escape
    private static readonly TimeSpan InteractDeadline = TimeSpan.FromSeconds(20);  // accept/turn-in acks are near-instant

    private const float GrindRadius = 60f;
    private const float ForceRadius = 150f;   // bot within this of a failed giver/turn-in ⇒ WMO last leg → force_*
    private const int SafetyMargin = 3;     // level-gate = danger_level − margin
    private const int DeferMinutes = 15;
    private const int AbandonAfterDefers = 3;
    private const int QuestStatusComplete = 3;   // VMaNGOS QUEST_STATUS_COMPLETE
    private const double LogSyncCapSec = 3;      // wait this long for QUEST_STATUS_ALL before picking blind

    public QuestPlanner(QuestGraphLoader quests, ILogger<QuestPlanner> logger)
    {
        _quests = quests;
        _logger = logger;
    }

    public Goal Handles => Goal.Questing;

    // ------------------------------------------------------------------------
    // PlanNext — the loop. Failure recovery first, then the happy path.
    // ------------------------------------------------------------------------
    public StepResult PlanNext(BotContext ctx, BotStateSnapshot snap)
    {
        // A negated/expired WAIT surfaced a failure → recover here (the no_path fix).
        if (ctx.Failure != null)
            return Recover(ctx);

        var q = ctx.Quest;

        // ── pick ──────────────────────────────────────────────────────────
        if (q == null || q.Node == null)
        {
            // Sync the C++ quest log once before picking, so we RESUME a quest a death
            // or restart interrupted instead of re-accepting it. Re-accepting an in-log
            // quest is rejected by C++ (CanTakeQuest=false → QUEST_INTERACT_FAIL), which
            // would strand it incomplete forever (the zombie). One QUERY per pick.
            if (ctx.Step != "sync_log")
            {
                ctx.SetStep("sync_log");
                return StepResult.Fire(new BridgeCommand("QUERY_QUEST_STATUS"));
            }

            // Wait for QUEST_STATUS_ALL (the executor stamps ctx.QuestLogStampUtc when it
            // lands), capped so a silent/empty log can't wedge the pick. "Stamped at or
            // after we entered sync_log" == (now − stamp) ≤ time-in-this-step.
            bool synced = (DateTime.UtcNow - ctx.QuestLogStampUtc).TotalSeconds <= ctx.TimeInStepSec;
            if (!synced && ctx.TimeInStepSec < LogSyncCapSec)
                return StepResult.Wait();

            // Resume an in-log quest if one is reachable; else pick a fresh one.
            var resume = TryResume(ctx);
            if (resume != null) return resume;

            var node = PickFor(ctx);
            if (node == null)
                return StepResult.Block("no_quests");   // → OnStall → ReselectGoal → grind

            var scratch = new QuestScratch { QuestId = node.QuestId, Node = node };
            scratch.ActiveQuestIds.Add(node.QuestId);
            ctx.Quest = scratch;
            _logger.LogInformation("[QUEST] {Name} picked [{Id}] \"{Title}\"", ctx.Name, node.QuestId, node.Title);
            ctx.SetStep("to_giver");
            return MoveTo(node.Giver!);
        }

        var node2 = q.Node;

        // ── accept ────────────────────────────────────────────────────────
        if (!q.Accepted)
        {
            switch (ctx.Step)
            {
                case "to_giver":     // arrived at the giver (TASK_COMPLETE cleared the WAIT)
                    ctx.SetStep("accept");
                    return Interact(q, accept: true);
                case "accept":       // QUEST_ACCEPT_ACK cleared the WAIT
                    q.Accepted = true;
                    return ToNextObjectiveOrTurnIn(ctx, q);
                default:
                    ctx.SetStep("to_giver");
                    return MoveTo(node2.Giver!);
            }
        }

        // ── objectives → turn-in ───────────────────────────────────────────
        switch (ctx.Step)
        {
            case "to_objective":     // arrived at the objective area
                {
                    var obj = ObjectiveAt(node2, q.ObjectiveSlot);
                    if (obj == null) return ToTurnIn(ctx, q);
                    ctx.SetStep("objective");
                    return GrindObjective(obj, RemainingKills(ctx, q.QuestId, obj));
                }
            case "objective":        // TASK_COMPLETE = kill_count reached
                q.ObjectiveSlot++;
                return ToNextObjectiveOrTurnIn(ctx, q);

            case "to_turnin":        // arrived at the turn-in NPC
                ctx.SetStep("turnin");
                return Interact(q, accept: false);

            case "turnin":           // QUEST_COMPLETE_ACK = rewarded
                ctx.Identity?.CompletedQuestIds.Add(q.QuestId);
                ctx.Identity?.QuestDeferralCounts.Remove(q.QuestId);
                _logger.LogInformation("[QUEST] {Name} completed [{Id}] \"{Title}\"", ctx.Name, q.QuestId, node2.Title);
                ctx.Quest = null;            // → pick the next quest
                return StepResult.Wait();

            default:
                return ToNextObjectiveOrTurnIn(ctx, q);
        }
    }

    // ------------------------------------------------------------------------
    // IsProgressing — lenient backstop. The real liveness is per-step: a WAIT
    // either acks or expires (Supervisor/brain → ctx.Failure → Recover), and a
    // failure negates promptly. This only catches a wedged bot with no WAIT.
    // ------------------------------------------------------------------------
    public bool IsProgressing(BotContext ctx, BotStateSnapshot snap)
    {
        if (ctx.TimeInGoalSec < 30) return true;                 // arm grace on entering Questing
        return ctx.TimeSinceProgressSec < 300;                   // 5 min with no progress + no WAIT ⇒ reselect
    }

    public StallAction OnStall(BotContext ctx)
        => StallAction.Of(StallActionKind.ReselectGoal, "quest:no_progress");

    // ========================================================================
    // Failure recovery (the regression-killer core)
    // ========================================================================
    private StepResult Recover(BotContext ctx)
    {
        var f = ctx.Failure!;
        ctx.Failure = null;
        var q = ctx.Quest;
        if (q == null || q.Node == null) return StepResult.Wait();   // no quest → re-pick next tick

        bool lastLeg = ctx.DistToTarget >= 0 && ctx.DistToTarget < ForceRadius;

        // no_path on the LAST LEG to a giver/turn-in = a WMO-interior NPC the navmesh
        // can't reach (Brother Neals' bell tower). force_* bypasses proximity (300 yd,
        // all eligibility gates intact, §7). Long-range no_path is a real routing gap →
        // defer instead, so force can't mask it.
        if (f.Reason == "no_path" && lastLeg && !q.ForceMode
            && (ctx.Step == "to_giver" || ctx.Step == "to_turnin"))
        {
            q.ForceMode = true;
            _logger.LogInformation("[QUEST] {Name} no_path last-leg → force {Step} [{Id}]",
                ctx.Name, ctx.Step, q.QuestId);
            if (ctx.Step == "to_giver") { ctx.SetStep("accept"); return Interact(q, accept: true); }
            ctx.SetStep("turnin"); return Interact(q, accept: false);
        }

        // PATH_UNSAFE: the route crosses too-high mobs. Blacklist the coord and
        // defer the quest UNTIL the bot out-levels the danger (no time retry).
        if (f.Reason == "path_unsafe")
        {
            if (f.Dest.HasValue)
                ctx.Identity?.AddPathBlacklist(f.Dest.Value.X, f.Dest.Value.Y, f.DangerLevel);
            return DeferAndDrop(ctx, q, levelGate: f.DangerLevel, "path_unsafe");
        }

        // Everything else (no_path far / no_progress / empty_path / cross_map /
        // deadline / interact requirements_not_met) → time-defer and re-pick.
        return DeferAndDrop(ctx, q, levelGate: 0, f.Reason);
    }

    private StepResult DeferAndDrop(BotContext ctx, QuestScratch q, int levelGate, string reason)
    {
        var id = ctx.Identity;
        int qid = q.QuestId;
        bool wasAccepted = q.Accepted;

        // QuestDeferralCounts is incremented by DeferQuest (time-based) only; level
        // gates (PATH_UNSAFE) are legitimate, not frustration, so they don't count.
        int priorDefers = id?.QuestDeferralCounts.GetValueOrDefault(qid, 0) ?? 0;
        bool valuable = q.Node != null && (q.Node.IsPartOfChain || q.Node.HasItemReward);
        bool frustrated = !valuable && levelGate == 0 && priorDefers + 1 >= AbandonAfterDefers;

        if (id != null)
        {
            if (levelGate > 0) id.DeferQuestUntilLevel(qid, levelGate, SafetyMargin);
            else id.DeferQuest(qid, TimeSpan.FromMinutes(frustrated ? 60 : DeferMinutes));
        }

        ctx.Quest = null;   // drop → re-pick next tick

        _logger.LogInformation("[QUEST] {Name} deferring [{Id}] ({Reason}{Gate}){Frus}",
            ctx.Name, qid, reason,
            levelGate > 0 ? $", until lvl {Math.Max(1, levelGate - SafetyMargin)}" : "",
            frustrated ? " [frustrated 60min]" : "");

        // If it was accepted, free the C++ quest-log slot. The C# deferral keeps it
        // out of the next pick; it becomes eligible again when the gate clears.
        return wasAccepted
            ? StepResult.Fire(new BridgeCommand("ABANDON_QUEST", new { quest_id = qid }))
            : StepResult.Wait();
    }

    // ========================================================================
    // Quest selection
    // ========================================================================
    private QuestNode? PickFor(BotContext ctx)
    {
        var id = ctx.Identity;
        if (id == null || !_quests.IsLoaded) return null;
        id.PruneExpiredDeferrals();

        // Range gate (OPEN #1): same map + within the level/zone cap. MUST be the same
        // filter + cap GoalSelector counts, or the bot bounces Questing↔Grinding.
        float cap = ZoneSafetyMap.GetMaxTravelDistance(id.Level, ctx.ZoneId, 0);

        return Pickable(_quests, id)
            .Where(q => InReach(q, ctx.Pos.X, ctx.Pos.Y, ctx.MapId, cap))
            .OrderBy(q => Dist2(ctx.Pos.X, ctx.Pos.Y, q.Giver!.X, q.Giver!.Y))
            .FirstOrDefault();
    }

    /// <summary>
    /// The quests this planner can actually take and complete right now: a known
    /// giver, kill-only (or no) objectives, not deferred, giver not blacklisted.
    /// Shared with GoalSelector so the arbitration matches what PlanNext can pick.
    /// Caller should PruneExpiredDeferrals() first.
    /// </summary>
    public static IEnumerable<QuestNode> Pickable(QuestGraphLoader graph, BotIdentity id)
    {
        int raceBit = QuestGraphLoader.RaceToBitmask(id.Race);
        int classBit = QuestGraphLoader.ClassToBitmask(id.ClassId);
        return graph.GetAvailableQuests(raceBit, classBit, id.Level, id.CompletedQuestIds)
                    .Where(q => IsPickable(q, id));
    }

    /// <summary>
    /// The per-quest pick predicate. Split out so GoalSelector can count it against an
    /// already-fetched available set (one graph scan) and report the funnel.
    /// </summary>
    public static bool IsPickable(QuestNode q, BotIdentity id)
        => q.Giver != null
           && q.ItemObjectives.Length == 0              // gather quests: later layer
           && q.Objectives.All(o => o.IsCreature)        // GO objectives: later layer
           && !id.DeferredQuestIds.ContainsKey(q.QuestId)
           && !id.IsPathBlacklisted(q.Giver.X, q.Giver.Y);

    /// <summary>
    /// The range gate (OPEN #1): the quest giver must be on the bot's CURRENT map and
    /// within the level/zone travel cap. Sibling to IsPickable (which has no bot
    /// position) — applied ALONGSIDE it by BOTH PickFor and GoalSelector so the
    /// arbitration and the pick can't disagree (the shared-filter invariant). Gating on
    /// the giver is enough for the kill-only scope: the loader scopes each quest's grind
    /// center to within ~500yd of its giver, so giver-in-reach ⇒ objective-in-reach.
    /// cap = ZoneSafetyMap.GetMaxTravelDistance(level, zoneId, 0) — tier 0, no escalation.
    /// Compute it once per tick and pass it in (it's the same for every quest).
    /// </summary>
    public static bool InReach(QuestNode q, float botX, float botY, int botMap, float cap)
    {
        if (q.Giver == null || q.Giver.Map != botMap) return false;
        float dx = botX - q.Giver.X, dy = botY - q.Giver.Y;
        return dx * dx + dy * dy <= cap * cap;
    }

    // ========================================================================
    // Objective sequencing
    // ========================================================================
    // Scan Node.Objectives from the cursor for the next completable kill objective.
    private static QuestObjective? ObjectiveAt(QuestNode node, int fromIndex)
    {
        for (int i = Math.Max(0, fromIndex); i < node.Objectives.Length; i++)
        {
            var o = node.Objectives[i];
            if (o.IsCreature && o.Count > 0 && o.GrindRadius > 0)
                return o;
        }
        return null;
    }

    private StepResult ToNextObjectiveOrTurnIn(BotContext ctx, QuestScratch q)
    {
        var node = q.Node!;
        for (int i = Math.Max(0, q.ObjectiveSlot); i < node.Objectives.Length; i++)
        {
            var o = node.Objectives[i];
            if (o.IsCreature && o.Count > 0 && o.GrindRadius > 0)
            {
                q.ObjectiveSlot = i;
                ctx.SetStep("to_objective");
                return MoveTo(o.GrindX, o.GrindY, o.GrindZ, o.GrindMap);
            }
        }
        return ToTurnIn(ctx, q);
    }

    private StepResult ToTurnIn(BotContext ctx, QuestScratch q)
    {
        var loc = q.Node!.TurnIn ?? q.Node!.Giver!;   // many quests turn in to the giver
        ctx.SetStep("to_turnin");
        return MoveTo(loc);
    }

    // ========================================================================
    // Resume (the zombie-killer): finish an in-log quest instead of re-accepting
    // ========================================================================
    // After a death/restart the scratch is gone but the quest is still ACCEPTED in the
    // C++ log with its kill progress (QUEST_STATUS_ALL told us which). Re-accepting it
    // fails (CanTakeQuest=false on an in-log quest), so instead pick up where the server
    // says we are: build the scratch as already-accepted and jump straight to the first
    // unsatisfied objective (or turn-in). Deferral/blacklist are ignored on purpose —
    // resuming an accepted quest is how we CLEAR the stuck state; a still-dangerous route
    // just re-defers via PATH_UNSAFE on the next leg.
    private StepResult? TryResume(BotContext ctx)
    {
        var id = ctx.Identity;
        var log = ctx.QuestLog;                       // stable snapshot (executor ref-swaps)
        if (id == null || !_quests.IsLoaded || log.Count == 0) return null;

        float cap = ZoneSafetyMap.GetMaxTravelDistance(id.Level, ctx.ZoneId, 0);

        QuestNode? best = null;
        int bestStatus = 0;
        float bestDist = float.MaxValue;
        foreach (var kv in log)
        {
            var node = _quests.GetQuest(kv.Key);
            if (node?.Giver == null) continue;
            if (id.CompletedQuestIds.Contains(node.QuestId)) continue;   // already rewarded
            if (node.ItemObjectives.Length != 0) continue;               // gather: later layer
            if (!node.Objectives.All(o => o.IsCreature)) continue;        // GO: later layer
            if (!InReach(node, ctx.Pos.X, ctx.Pos.Y, ctx.MapId, cap)) continue;

            float d = Dist2(ctx.Pos.X, ctx.Pos.Y, node.Giver.X, node.Giver.Y);
            if (d < bestDist) { best = node; bestStatus = kv.Value.Status; bestDist = d; }
        }
        if (best == null) return null;

        var scratch = new QuestScratch { QuestId = best.QuestId, Node = best, Accepted = true };
        scratch.ActiveQuestIds.Add(best.QuestId);
        ctx.Quest = scratch;

        // Server says COMPLETE → straight to turn-in.
        if (bestStatus == QuestStatusComplete)
        {
            _logger.LogInformation("[QUEST] {Name} resuming [{Id}] \"{Title}\" → turn-in (server COMPLETE)",
                ctx.Name, best.QuestId, best.Title);
            return ToTurnIn(ctx, scratch);
        }

        // Otherwise resume the first objective slot the server hasn't satisfied.
        var counts = log[best.QuestId].MobCounts;
        int slot = FirstUnsatisfiedObjective(best, counts);
        if (slot < 0)
        {
            _logger.LogInformation("[QUEST] {Name} resuming [{Id}] → turn-in (kills already met)",
                ctx.Name, best.QuestId);
            return ToTurnIn(ctx, scratch);
        }

        scratch.ObjectiveSlot = slot;
        ctx.SetStep("to_objective");
        var o = best.Objectives[slot];
        _logger.LogInformation("[QUEST] {Name} resuming [{Id}] \"{Title}\" → obj slot {Slot} ({Cur}/{Req} entry {Entry})",
            ctx.Name, best.QuestId, best.Title, slot, counts[o.Slot - 1], o.Count, o.CreatureEntry);
        return MoveTo(o.GrindX, o.GrindY, o.GrindZ, o.GrindMap);
    }

    // First index into node.Objectives whose server kill count is below the requirement.
    // Slot (1-4) maps to the C++ m_creatureOrGOcount[Slot-1]. -1 = all satisfied.
    private static int FirstUnsatisfiedObjective(QuestNode node, int[] counts)
    {
        for (int i = 0; i < node.Objectives.Length; i++)
        {
            var o = node.Objectives[i];
            if (!o.IsCreature || o.Count <= 0 || o.GrindRadius <= 0) continue;
            int done = (o.Slot >= 1 && o.Slot <= counts.Length) ? counts[o.Slot - 1] : 0;
            if (done < o.Count) return i;
        }
        return -1;
    }

    // Kills still needed for an objective per the synced log (full count if unknown).
    // Never returns 0 — kill_count=0 means "indefinite grind" to C++.
    private static int RemainingKills(BotContext ctx, int questId, QuestObjective obj)
    {
        if (ctx.QuestLog.TryGetValue(questId, out var e)
            && obj.Slot >= 1 && obj.Slot <= e.MobCounts.Length)
        {
            int remaining = obj.Count - e.MobCounts[obj.Slot - 1];
            return remaining > 0 ? remaining : 1;
        }
        return obj.Count;
    }

    // ========================================================================
    // Command builders
    // ========================================================================
    private static StepResult MoveTo(QuestNpcLocation loc) => MoveTo(loc.X, loc.Y, loc.Z, loc.Map);

    private static StepResult MoveTo(float x, float y, float z, int map)
        => StepResult.Send(
            new BridgeCommand("MOVE_TO", new { mapId = map, x, y, z }),
            "TASK_COMPLETE", TravelDeadline);

    private static StepResult Interact(QuestScratch q, bool accept)
    {
        var npc = accept ? q.Node!.Giver! : (q.Node!.TurnIn ?? q.Node!.Giver!);
        string action = accept
            ? (q.ForceMode ? "force_accept" : "accept")
            : (q.ForceMode ? "force_complete" : "complete");
        string expect = accept ? "QUEST_ACCEPT_ACK" : "QUEST_COMPLETE_ACK";
        return StepResult.Send(
            new BridgeCommand("QUEST_INTERACT", new { action, quest_id = q.QuestId, npc_entry = npc.NpcEntry }),
            expect, InteractDeadline);
    }

    private static StepResult GrindObjective(QuestObjective obj, int killCount)
        => StepResult.Send(
            new BridgeCommand("SET_TASK", new
            {
                task = "GRIND",
                x = obj.GrindX,
                y = obj.GrindY,
                z = obj.GrindZ,
                radius = GrindRadius,
                creature_entry = obj.CreatureEntry,
                kill_count = killCount
            }),
            "TASK_COMPLETE", ObjectiveDeadline);   // kill_count>0 ⇒ C++ acks at N kills

    private static float Dist2(float ax, float ay, float bx, float by)
    {
        float dx = ax - bx, dy = ay - by;
        return MathF.Sqrt(dx * dx + dy * dy);
    }
}