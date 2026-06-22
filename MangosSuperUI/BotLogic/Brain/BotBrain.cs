using MangosSuperUI.BotLogic.Core;
using MangosSuperUI.BotLogic.Planners;

namespace MangosSuperUI.BotLogic.Brain;

// ============================================================================
// BotBrain — the live thread / driver (§4).
//
// Owns one bot's per-tick control flow WHOLLY: read the snapshot, select a goal,
// ask the goal's planner what/where, then itself issue the command (via
// BotExecutor) and record the WAIT, then run the Supervisor. The inversion at
// the heart of the rebuild lives here — the brain drives, planners advise.
// Nothing about a bot's control flow lives outside this class.
//
// Phase 2 — Grinding. The driver refreshes sensory, selects a goal (GoalSelector),
// dispatches the goal planner's next step, and runs the Supervisor's deadline
// rule. On goal change it stops a leaving grind patrol (SET_TASK IDLE), resets
// the goal scratch, and clears any WAIT. A grind carries no WAIT, so it can never
// false-stall (§6.3); the planner's KILL-recency owns "no mobs → reselect."
//
// Soft re-plan (§ batching trek): step 3c lets an INTERRUPTIBLE leg (a quest
// trek — Outstanding.RescanAtUtc set) be re-evaluated on a cadence while its WAIT
// is still pending, so quests discovered en route can preempt a long journey
// without a re-path stutter. Default legs (RescanAtUtc null) are untouched.
// ============================================================================
public sealed class BotBrain
{
    private readonly BotExecutor _executor;
    private readonly BotSupervisor _supervisor;
    private readonly GoalSelector _selector;
    private readonly IReadOnlyDictionary<Goal, IBotPlanner> _planners;
    private readonly ILogger<BotBrain> _logger;

    // Cadence for the step-3c soft re-plan of an interruptible leg. This is just the
    // "look again" interval — the real cost gate is the planner's own moved-≥Nyd throttle
    // inside Rescan, so a stationary grind that happens to carry RescanAtUtc no-ops cheaply.
    // BotTuning candidate.
    private static readonly TimeSpan RescanInterval = TimeSpan.FromSeconds(10);

    public BotBrain(
        BotExecutor executor,
        BotSupervisor supervisor,
        GoalSelector selector,
        IEnumerable<IBotPlanner> planners,
        ILogger<BotBrain> logger)
    {
        _executor = executor;
        _supervisor = supervisor;
        _selector = selector;
        _planners = planners.ToDictionary(p => p.Handles);
        _logger = logger;
    }

    /// <summary>The executor, exposed so the host can route bridge events through ack matching.</summary>
    public BotExecutor Executor => _executor;

    /// <summary>
    /// One tick for one bot. The host has already read the snapshot from the bridge.
    /// </summary>
    public async Task TickAsync(BotContext ctx, BotStateSnapshot snap)
    {
        // 1. Read snapshot → refresh sensory.
        ctx.Sense(snap);

        // 2. Select the goal. On a change: stop a leaving grind patrol, reset the
        //    goal scratch, clear any WAIT, stamp the new goal.
        var goal = _selector.Select(ctx, snap);
        if (goal != ctx.Goal)
            await EnterGoalAsync(ctx, goal);

        // 3. Resolve the planner for the active goal. No planner (e.g. Idle) → run
        //    the deadline rule and stop.
        if (!_planners.TryGetValue(ctx.Goal, out var planner))
        {
            _supervisor.Check(ctx, snap);
            return;
        }

        // 3b. Expired WAIT → recovery. The Supervisor's deadline rule (step 5) flags
        //     a stall but does NOT clear Pending; without this an expired quest WAIT
        //     would wedge the bot (Pending != null ⇒ the act block is skipped forever).
        //     Surface it as a failure the planner resolves below (deadline → Recover →
        //     defer/force/repick). Grind never arms a WAIT, so this is inert for it.
        if (ctx.Pending != null && ctx.Pending.Expired)
        {
            ctx.Failure ??= new WaitFailure
            {
                CommandType = ctx.Pending.CommandType,
                Reason = "deadline",
                Dest = ctx.Target,
                Utc = DateTime.UtcNow
            };
            _executor.ClearPending(ctx);
        }

        // 3c. Soft re-plan for an interruptible in-flight leg (a quest trek). While a
        //     WAIT is still pending and the planner asked to be re-looked-at on a cadence
        //     (RescanAtUtc due), peek WITHOUT clearing it. If the planner PREEMPTS
        //     (Issue/Dispatch — it folded in closer work), swap the WAIT to the new
        //     command; if it keeps waiting (Continue), leave the journey running — no
        //     re-path stutter — and push the next rescan. Skipped when a failure is
        //     already pending (3b owns that) or the leg isn't interruptible (RescanAtUtc null).
        var p = ctx.Pending;
        if (p != null && ctx.Failure == null && p.RescanAtUtc is DateTime due && DateTime.UtcNow >= due)
        {
            var rescan = planner.Rescan(ctx, snap);
            if (rescan is StepResult.Issue or StepResult.Dispatch)
            {
                _executor.ClearPending(ctx);
                await DispatchStepAsync(ctx, rescan);
                _supervisor.Check(ctx, snap);
                return;
            }
            if (ctx.Pending != null)
                ctx.Pending.RescanAtUtc = DateTime.UtcNow + RescanInterval;
        }

        // 4. Act only when nothing is outstanding. A pending failure (a negated or
        //    expired WAIT) ALWAYS goes to the planner to recover — never to reselect,
        //    or an unreachable quest would be re-picked on a loop instead of deferred.
        //    Otherwise: progressing → advance one step; genuinely wedged with no
        //    failure signal → OnStall. A Blocked step (e.g. no_quests) routes to OnStall.
        if (ctx.Pending == null)
        {
            if (ctx.Failure == null && !planner.IsProgressing(ctx, snap))
            {
                await HandleStallAsync(ctx, planner.OnStall(ctx));
            }
            else
            {
                var step = planner.PlanNext(ctx, snap);
                if (step is StepResult.Blocked)
                    await HandleStallAsync(ctx, planner.OnStall(ctx));
                else
                    await DispatchStepAsync(ctx, step);
            }
        }

        // 5. Supervisor — the universal deadline rule.
        _supervisor.Check(ctx, snap);
    }

    /// <summary>Route an inbound bridge event for this bot through the executor's ack matching.</summary>
    public void OnEvent(BotContext ctx, BotEvent evt)
    {
        _executor.OnEvent(ctx, evt);
    }

    // ----------------------------------------------------------------------

    /// <summary>
    /// Transition into a new goal: stop a leaving C++ grind patrol (SET_TASK IDLE),
    /// reset the (now-stale) goal scratch, clear any WAIT, then stamp the new goal.
    /// </summary>
    private async Task EnterGoalAsync(BotContext ctx, Goal goal)
    {
        // Death attribution: if we're leaving Questing because the bot DIED, stamp the quest it was
        // working so MaintenancePlanner can count this death against it (and shelve it at the cap —
        // the macro-loop exit). MUST read ctx.Quest BEFORE ResetScratch wipes it. Active is the
        // quest whose leg armed the in-flight WAIT — set throughout a to_objective trek — so it's
        // the killer. No Active (died between legs) → no blame, never a false attribution.
        if (goal == Goal.Maintenance && ctx.Dead && ctx.Goal == Goal.Questing && ctx.Quest?.Active is { } dying)
            ctx.DeathBlameQuestId = dying.QuestId;

        // Stop a leaving C++ grind patrol so the next goal can actually drive the bot. BOTH
        // Grinding AND Questing run the autonomous C++ grind/objective patrol (an enriched
        // MOVE_TO that travels then grinds in place). A fresh PLAIN MOVE_TO — e.g. the vendor
        // route — does NOT cancel that in-place grind on the C++ side, so the bot keeps fighting
        // its grind pocket and never travels (observed: a vendor route from Questing moved ~24yd
        // in 120s while killing the same mobs, then tripped its leg deadline → giveup). SET_TASK
        // IDLE clears the patrol; the new goal re-arms its own task in PlanNext.
        if (ctx.Goal == Goal.Grinding || ctx.Goal == Goal.Questing)
            await _executor.IssueNoWaitAsync(ctx, IdleTask());   // stop the autonomous patrol

        ctx.SetGoal(goal, "enter");
        ResetScratch(ctx);                                       // each goal re-arms its own scratch in PlanNext
        _executor.ClearPending(ctx);
        ctx.Failure = null;                                      // stale negative outcome doesn't carry across goals
    }

    /// <summary>Act on the planner's chosen step.</summary>
    private async Task DispatchStepAsync(BotContext ctx, StepResult step)
    {
        switch (step)
        {
            case StepResult.Issue issue:
                await _executor.IssueAsync(ctx, issue.Command, issue.ExpectedEvent, issue.Deadline);
                break;

            case StepResult.Dispatch dispatch:
                await _executor.IssueNoWaitAsync(ctx, dispatch.Command);
                break;

            case StepResult.Done:
                // Goal achieved — drop to Idle so the next tick reselects.
                await EnterGoalAsync(ctx, Goal.Idle);
                break;

            case StepResult.Continue:
            default:
                break;   // Continue → nothing this tick. Blocked is intercepted in TickAsync → OnStall.
        }
    }

    /// <summary>Enforce the planner's stall verdict.</summary>
    private async Task HandleStallAsync(BotContext ctx, StallAction action)
    {
        switch (action.Kind)
        {
            case StallActionKind.ReselectGoal:
                _logger.LogDebug("[BRAIN] {Name} {Goal} reselect: {Detail}", ctx.Name, ctx.Goal, action.Detail);
                // Stop the current patrol and drop to Idle; next tick reselects and
                // re-arms a fresh grind wherever the bot now stands (no phantom STUCK —
                // a grind never armed a Pending).
                await EnterGoalAsync(ctx, Goal.Idle);
                break;

            default:
                // Reroute/Defer/Abandon/ForceInteract/EscalateRez/GiveUpStop land with
                // their planners in P3+. No Phase-2 planner emits them.
                _logger.LogDebug("[BRAIN] {Name} {Goal} stall {Kind}: {Detail} (no Phase-2 handler)",
                    ctx.Name, ctx.Goal, action.Kind, action.Detail);
                break;
        }
    }

    private static void ResetScratch(BotContext ctx)
    {
        ctx.Grind = null;
        ctx.Quest = null;
        ctx.Service = null;
        ctx.Maintenance = null;   // Phase 4 — re-armed by MaintenancePlanner on each fresh death
        ctx.Train = null;         // re-armed by TrainingPlanner on each trainer trip
    }

    /// <summary>SET_TASK IDLE — stops the C++ grind patrol (keeps the follow; §4.3).</summary>
    private static BridgeCommand IdleTask() => new("SET_TASK", new { task = "IDLE" });
}