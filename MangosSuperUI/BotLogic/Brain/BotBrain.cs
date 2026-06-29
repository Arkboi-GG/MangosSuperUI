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

    // ── No-progress circuit breaker (the universal silent/fast-stall net) ──
    // LastProgressUtc advances ONLY on a real ack/kill/quest/level. A silent no-kill grind and a fast
    // fail-loop (negated <1s, never marks progress) both FREEZE it; the Supervisor's deadline rule only
    // catches a slow hang (a WAIT past its deadline), so these slip through. This fires every tick,
    // regardless of Pending/Failure/goal.
    private const double WedgeCeilingSec = 150;   // no real progress this long → wedged
    private const int WedgeFailCap = 8;     // …or this many back-to-back negated WAITs (fast fail-loop)
    private const double WedgeBackoffSec = 5;    // park this long, then resume + relocate to a fresh cell
    private const double TrainWedgeCooldownSec = 300;   // a trainer-route wedge defers Training this long (mirrors TrainingPlanner give-up) so the bot quests instead of re-bee-lining

    // ── Held-objective reconcile (Held-Objective build §3) ──
    // Grace after a held objective is (re)committed before the reconcile may re-issue it: C++ needs a
    // STATE tick (~5s) to adopt the task and echo it back, so a just-assigned objective whose echo still
    // reads the OLD task is NOT a real mismatch. The time analog of BotExecutor.ArrivalGateYards.
    private const double ReconcileGraceSec = 7;

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

        // 1a. Combat-directive overlay (grouping §3.6). The GroupCoordinator pre-pass already
        //     stamped ctx.CombatDirective this tick (Assist(anchor) / None). Emit COMBAT_DIRECTIVE
        //     to C++ ONLY when the stamp changed since we last told it -- the coordinator re-stamps
        //     every tick (idempotent) but the wire stays brain-cadence, not per-tick traffic
        //     (§3.8.4 / §1). Fire-and-forget, no WAIT (like SET_TASK), and orthogonal to Pending --
        //     so it runs ahead of the wedge/goal machinery and regardless of any in-flight leg.
        if (ctx.CombatDirective != ctx.LastEmittedCombat)
        {
            await _executor.IssueNoWaitAsync(ctx, BridgeCommand.Combat(ctx.CombatDirective));
            ctx.LastEmittedCombat = ctx.CombatDirective;
        }

        // 1b. No-progress circuit breaker. Fires before goal/plan so a wedged bot is parked, not driven.
        if (await TryBreakWedgeAsync(ctx)) return;

        // 1c. Reconcile the held objective against C++'s reported task (Held-Objective build §3).
        //     ctx.Held is the committed strategic objective; ctx.HeldTask is what C++ says it is
        //     ACTUALLY running. If C++ has dropped / never adopted it (echo known, past the adoption
        //     grace, and not matching), knock out the in-flight WAIT + the group change-guard so the
        //     active planner RE-ISSUES the realizing leg this tick — the self-heal for the SET_TASK IDLE
        //     strand. Degrades safe: Held null or echo Unknown (no readback yet) → no-op, today's
        //     behavior byte-for-byte. Runs before goal selection so the re-issue happens in this tick's
        //     Act block; orthogonal to the wedge/goal machinery (it only clears guards, never drives).
        ReconcileHeldObjective(ctx);

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

    /// <summary>
    /// No-progress circuit breaker. Trips when the bot has made no REAL progress for WedgeCeilingSec
    /// (LastProgressUtc — kills/acks only) OR is in a fast fail-loop (WedgeFailCap back-to-back negated
    /// WAITs, e.g. relocate MOVE_FAILED no_path at tick speed when the bot is off the navmesh). On trip:
    /// stop whatever's in flight, record the current grind cell as DEAD so the next relocation goes
    /// somewhere new, and PARK on a backoff (GoalSelector holds Idle) so it stops thrashing. A real kill
    /// clears the streak + dead-cell history (BotContext.OnGrindProgress). There is no live-teleport in
    /// the bridge, so a genuinely off-mesh bot can only be parked + probed slowly here — un-wedging it is
    /// the C++ snap-to-poly job. Returns true if it acted (the tick is spent).
    /// </summary>
    private async Task<bool> TryBreakWedgeAsync(BotContext ctx)
    {
        var id = ctx.Identity;
        if (id?.WedgeBackoffUntil is DateTime parked && DateTime.UtcNow < parked)
            return false;   // already parked — let the backoff hold; GoalSelector keeps it Idle

        bool wedged = ctx.TimeSinceProgressSec > WedgeCeilingSec || ctx.ConsecutiveFailures >= WedgeFailCap;
        if (!wedged) return false;

        double noProg = ctx.TimeSinceProgressSec;
        int fails = ctx.ConsecutiveFailures;

        _executor.ClearPending(ctx);
        ctx.Failure = null;
        ctx.ConsecutiveFailures = 0;
        id?.ClearGrindRelocate();
        if (ctx.Goal == Goal.Grinding)
            ctx.RecordDeadGrindCell(ctx.Pos.X, ctx.Pos.Y);   // don't drop back onto this dead spot

        // A wedge while routing to a trainer is the L1 bum-rush loop: with HasUnlearnedSpells set, the
        // GoalSelector training trigger re-fires on every reselect and the bot bee-lines the (unreachable /
        // crowded / interior-pocket) trainer again the instant the backoff lapses — never questing, never
        // levelling. Stamp a give-up cooldown (same window as TrainingPlanner's own give-up) so the trigger
        // is gated and the bot falls through to questing; it re-attempts the trainer after the cooldown, by
        // then questing-travelled to a possibly-reachable one. HasUnlearnedSpells is left SET on purpose —
        // the bot still owes the training, it's deferred, not abandoned. This also destaggers a crowd: each
        // bot cools down at a slightly different time and drifts off to quest, thinning the trainer pileup.
        if (ctx.Goal == Goal.Training && id != null)
            id.TrainCooldownUntil = DateTime.UtcNow.AddSeconds(TrainWedgeCooldownSec);

        if (id != null)
        {
            id.WedgeBackoffUntil = DateTime.UtcNow.AddSeconds(WedgeBackoffSec);
            // Future-stamp the progress clock to the park END so the bot gets a fresh full window on
            // resume (the idle park itself isn't "no progress" to be punished for).
            ctx.LastProgressUtc = id.WedgeBackoffUntil.Value;
        }

        _logger.LogWarning(
            "[BRAIN] {Name} WEDGE (noProg={T:F0}s fails={F}) — park {P}s then relocate fresh (goal {G} @ {Pos})",
            ctx.Name, noProg, fails, WedgeBackoffSec, ctx.Goal, ctx.Pos);

        await EnterGoalAsync(ctx, Goal.Idle);   // next tick reselects; parks while the backoff holds
        return true;
    }

    /// <summary>Route an inbound bridge event for this bot through the executor's ack matching.</summary>
    public void OnEvent(BotContext ctx, BotEvent evt)
    {
        _executor.OnEvent(ctx, evt);
    }

    // ----------------------------------------------------------------------

    /// <summary>
    /// Reconcile the held strategic objective (ctx.Held) against C++'s reported task (ctx.HeldTask,
    /// §3). The held objective OUTLIVES the leg WAIT and the goal bounce; the echo says what C++ is
    /// ACTUALLY running. When they disagree past the adoption grace — C++ idle or on a stale/different
    /// task while we still hold a Grind/Travel objective — the realizing leg was lost (the classic case:
    /// EnterGoalAsync fired SET_TASK IDLE on a Questing exit and a change-guard then suppressed the
    /// re-issue). Clear the in-flight commitment guards so the active planner re-commits the leg THIS
    /// tick (PlanNext runs below with Pending == null). This never builds wire itself; it only invalidates
    /// the guards and lets the owning planner re-issue, reusing the established paths.
    ///
    /// No-ops (today's behavior, byte-for-byte) when: no objective held, the echo is Unknown (no C++
    /// readback yet — pre-Session-3), the objective was (re)committed within the adoption grace, the
    /// kind is passive (Hold/Idle), or C++ is already on it.
    /// </summary>
    private void ReconcileHeldObjective(BotContext ctx)
    {
        if (ctx.Held is not { } held || !held.NeedsActuation) return;   // nothing reconcilable held

        // Only reconcile while the bot is actually PURSUING the objective. Both a solo quest objective and a
        // group shared objective are worked under Goal.Questing (GoalSelector routes an active GroupOrder there,
        // and the solo enriched-objective leg is a Questing leg); a fallback grind clears Held on arm. So if the
        // bot has peeled to Maintenance / Training / Idle (death, heal, vendor, wedge-park), the held objective is
        // stale-by-context — re-issuing into another planner would clobber its in-flight WAIT (e.g. knock out a
        // RESURRECT mid-rez). Gate it. The strand case is untouched: the parked bot this exists to rescue is
        // still Goal.Questing (the 31,043 Questing/enter park).
        if (ctx.Goal != Goal.Questing) return;

        var echo = ctx.HeldTask;
        if (!echo.IsKnown) return;                                       // no readback → degrade to ctx.Pending inference
        if (ctx.TimeSinceObjectiveSec < ReconcileGraceSec) return;       // just (re)committed — let C++ adopt it first
        if (held.MatchedBy(echo)) return;                                // C++ is on it → §5 progress checks own the rest

        // Mismatch: C++ is idle / on a stale or different task while we hold this objective. Clear the in-flight
        // WAIT + any stale failure, then force the OWNING planner to re-issue the realizing leg this tick. The
        // mechanism differs by provenance: a Coordinator objective re-issues via DriveGroup's change-guard (clear
        // LastGroupOrder); a SelfSolo objective re-issues via a fresh QuestPlanner derive (step → "plan", the
        // established re-derive sentinel). NOT a raw advance: the step-apply switch would read a cleared
        // to_objective WAIT as COMPLETION and move on, and Recover would DEFER the quest — both wrong for
        // "C++ dropped it, put it back".
        _executor.ClearPending(ctx);
        ctx.Failure = null;
        if (held.Source == ObjectiveSource.Coordinator)
            ctx.LastGroupOrder = GroupOrder.None;   // force QuestPlanner.DriveGroup to re-issue the group leg
        else
            ctx.SetStep("plan");                    // force QuestPlanner to RE-DERIVE the solo leg (not advance/defer)
        _logger.LogInformation(
            "[BRAIN] {Name} RECONCILE — C++ task {Echo} != held {Held} (cre={Cre}, src={Src}) — re-issuing",
            ctx.Name, echo.Kind, held.Kind, held.CreatureEntry, held.Source);
    }

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
        if (goal != Goal.Grinding) ctx.Identity?.ClearGrindRelocate();   // a half-done relocate doesn't survive a goal change
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
        ctx.Teleport = null;      // abandon any in-flight teleport-assist round-trip on a goal change (death/preempt owns the bot)
    }

    /// <summary>SET_TASK IDLE — stops the C++ grind patrol (keeps the follow; §4.3).</summary>
    private static BridgeCommand IdleTask() => new("SET_TASK", new { task = "IDLE" });
}