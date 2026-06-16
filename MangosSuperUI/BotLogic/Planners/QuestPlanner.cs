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

    private static readonly TimeSpan TravelDeadline   = TimeSpan.FromMinutes(8);   // continuation travel can be long (§4.11)
    private static readonly TimeSpan ObjectiveDeadline = TimeSpan.FromMinutes(4);   // grind kill_count; depleted → escape
    private static readonly TimeSpan InteractDeadline  = TimeSpan.FromSeconds(20);  // accept/turn-in acks are near-instant

    private const float GrindRadius = 60f;
    private const float ForceRadius = 150f;   // bot within this of a failed giver/turn-in ⇒ WMO last leg → force_*
    private const int   SafetyMargin = 3;     // level-gate = danger_level − margin
    private const int   DeferMinutes = 15;
    private const int   AbandonAfterDefers = 3;

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
                return GrindObjective(obj);
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

        return Pickable(_quests, id)
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

        foreach (var q in graph.GetAvailableQuests(raceBit, classBit, id.Level, id.CompletedQuestIds))
        {
            if (q.Giver == null) continue;
            if (q.ItemObjectives.Length > 0) continue;             // gather quests: later layer
            if (!q.Objectives.All(o => o.IsCreature)) continue;     // GO objectives: later layer
            if (id.DeferredQuestIds.ContainsKey(q.QuestId)) continue;
            if (id.IsPathBlacklisted(q.Giver.X, q.Giver.Y)) continue;
            yield return q;
        }
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

    private static StepResult GrindObjective(QuestObjective obj)
        => StepResult.Send(
            new BridgeCommand("SET_TASK", new
            {
                task = "GRIND",
                x = obj.GrindX,
                y = obj.GrindY,
                z = obj.GrindZ,
                radius = GrindRadius,
                creature_entry = obj.CreatureEntry,
                kill_count = obj.Count
            }),
            "TASK_COMPLETE", ObjectiveDeadline);   // kill_count>0 ⇒ C++ acks at N kills

    private static float Dist2(float ax, float ay, float bx, float by)
    {
        float dx = ax - bx, dy = ay - by;
        return MathF.Sqrt(dx * dx + dy * dy);
    }
}
