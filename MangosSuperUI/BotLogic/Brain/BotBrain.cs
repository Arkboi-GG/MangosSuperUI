using MangosSuperUI.BotLogic.Core;
using MangosSuperUI.BotLogic.Data;
using MangosSuperUI.BotLogic.Planners;
using MangosSuperUI.BotLogic.Tracking;

namespace MangosSuperUI.BotLogic.Brain;

internal enum StillObservationKind
{
    MissingState,
    StateNotAdvanced,
    Seeded,
    BridgeSessionChanged,
    ContinuityReset,
    MapChanged,
    Moved,
    Still
}

internal readonly record struct StillObservation(StillObservationKind Kind, double ElapsedSeconds);

internal enum CombatStillResetGate
{
    Eligible,
    ProtocolTooOld,
    WedgeStreakBelowCap,
    CooldownActive
}

internal enum CombatStillPostResetGate
{
    AwaitNewerState,
    SessionSuperseded,
    Dead,
    ExternalOwner,
    StillInCombat,
    SafeToEscape
}

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

    // [ESCAPE] (FINDING_010) Stranded escalation: this many consecutive wedge trips with zero real
    // kills in between (WedgeStreak — a kill clears it) means the park→local-relocate ladder is not
    // working: the bot has no killable/questable content in walking range (Everlook L18, Badlands
    // L21 — the 23-bot stray census). Escalate to a PORT_HOME (racial start) instead of another
    // ~50yd shuffle. At the observed wedge cadence (~1 per 2.5 min) the cap ≈ 15 min of proven
    // stranding, and a refused/failed port re-accrues the same window before retrying.
    private const int StrandedWedgeCap = 6;
    private const double TrainWedgeCooldownSec = 300;   // a trainer-route wedge defers Training this long (mirrors TrainingPlanner give-up) so the bot quests instead of re-bee-lining

    // ── Combat-still recovery ──
    // A STATE older than this is already rejected by the bridge's sensory-feed
    // wall. The independent continuity check here ensures a disconnected gap can
    // never be counted as proof that the bot remained fixed in place.
    private const double StillStateContinuitySec = 15;
    private const int CombatStillResetProtocol = 6;
    private const double CombatStillResetDeadlineSec = 15;
    private const double CombatStillResetCooldownSec = 600;
    internal const string CombatStillResetCommandType = "RESET_COMBAT_STUCK";
    internal const string CombatStillResetAckEvent = "COMBAT_RESET_ACK";

    // ── No-path group-leg quarantine ──
    private const int NoPathQuarantineStreakCount = 5;

    // ── Held-objective reconcile (Held-Objective build §3) ──
    // Grace after a held objective is (re)committed before the reconcile may re-issue it: C++ needs a
    // STATE tick (~5s) to adopt the task and echo it back, so a just-assigned objective whose echo still
    // reads the OLD task is NOT a real mismatch. The time analog of BotExecutor.ArrivalGateYards.
    private const double ReconcileGraceSec = 7;

    // 2026-07-03, the reconcile-storm fix. ReconcileGraceSec above is a ONE-TIME adoption grace after
    // a fresh commit; nothing previously stopped ReconcileHeldObjective re-firing on EVERY subsequent
    // tick for as long as the mismatch persisted (confirmed live: a sustained multi-minute burst of
    // re-issues at faster than 1Hz for a single bot). That's pure waste against BRIDGE_STATE_INTERVAL
    // (5000ms, AiBotAIMain.h) — the echo this check reads only refreshes every 5s, so any re-fire
    // faster than that is judging the exact same stale echo it already judged a mismatch on the
    // previous tick, and re-sending an identical wire command before C++ could possibly have acted on
    // the last one. Set to (at least) the STATE cadence so each fire gets to observe one fresh echo
    // before deciding to fire again.
    private const double ReconcileRefireCooldownSec = 7;

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

    /// <summary>The DI-resolved QuestPlanner singleton (§Option A, 2026-07-01) -- exposed so
    /// GroupCoordinator can drive the group's shared decisions through the EXACT SAME
    /// PlanNext/Derive/BuildBatch/GatherLocals/PriorityLeg/Recover machinery a solo bot runs,
    /// instead of a hand-rolled parallel reimplementation. Reuses the singleton already resolved
    /// for Goal.Questing -- no new DI registration, no risk of a second differently-configured
    /// instance drifting from the one solo bots actually use.</summary>
    public QuestPlanner QuestPlanner => (QuestPlanner)_planners[Goal.Questing];

    /// <summary>
    /// One tick for one bot. The host has already read the snapshot from the bridge.
    /// </summary>
    public async Task TickAsync(BotContext ctx, BotStateSnapshot snap)
    {
        // 1. Read snapshot → refresh sensory.
        ctx.Sense(snap);

        // 1a--1. A correlated command was rejected because a player/RTS
        // controller owns the bot. Preserve the goal, scratch, and held
        // objective, but issue no planner traffic during the bounded handoff.
        // This is control ownership, not work failure: do not defer a quest,
        // trip a trainer cooldown, or grow the wedge streak.
        if (ctx.ControlFenceObservedUtc != default
            && snap.StateUtc <= ctx.ControlFenceObservedUtc)
        {
            CircuitTrace.HitNote(ctx.Guid, "tick: control fence - planner stands down", ctx.ControlFenceReason);
            ctx.Pending = null;
            ctx.Failure = null;
            ctx.GoalReason = ctx.ControlFenceReason;
            ctx.MarkProgress();
            if (ctx.Identity is { } controlled)   // cb:fold control-hold timer hygiene; hold decision probed above
            {   // cb:fold control-hold timer hygiene; hold decision probed above
                ResetStillWindow(ctx, controlled, snap.StateUtc);
            }
            ResetCombatStillRecovery(ctx, clearCooldown: true);
            return;
        }
        if (ctx.ControlFenceObservedUtc != default)
        {
            CircuitTrace.Hit(ctx.Guid, "tick: fresh STATE releases transient control fence");
            // Only a newer STATE can release the transient fence. Sense has
            // already copied its durable Possessed/Conscripted truth below.
            ctx.ControlFenceObservedUtc = default;
            ctx.ControlFenceReason = "";
        }

        // 1a-0. [CONSCRIPTED] Enlisted in a player's RTS army: the planner stands
        //       down entirely. Park once (EnterGoalAsync(Idle) clears Pending,
        //       Failure and the goal scratch; ctx.Held is deliberately PRESERVED
        //       so dismissal resumes questing in place), then keep the progress
        //       and still-anchor clocks warm every tick so neither the wedge
        //       breaker nor the stuck ejector fires a stale verdict on the first
        //       free tick after dismissal. C++ owns the army — combat AI,
        //       formations and RTS orders run server-side, and the core's bridge
        //       fence independently drops planner commands — so this gate is
        //       politeness plus timer hygiene, not the only wall.
        if (ctx.Conscripted || ctx.Possessed)
        {
            string controlReason = ctx.Possessed ? "possessed" : "conscripted";
            CircuitTrace.HitNote(ctx.Guid, "tick: externally controlled - planner stands down", controlReason);
            if (ctx.Goal != Goal.Idle)
            {
                CircuitTrace.Hit(ctx.Guid, "tick: externally controlled park to idle");
                await EnterGoalAsync(ctx, Goal.Idle);
            }
            ctx.GoalReason = controlReason;
            ctx.MarkProgress();
            if (ctx.Identity is { } enlisted)
            {
                CircuitTrace.Hit(ctx.Guid, "tick: conscripted still-anchor refresh");
                ResetStillWindow(ctx, enlisted, snap.StateUtc);
            }
            ResetCombatStillRecovery(ctx, clearCooldown: true);
            return;
        }

        // 1a. Combat-directive overlay (grouping §3.6). The GroupCoordinator pre-pass already
        //     stamped ctx.CombatDirective this tick (Assist(anchor) / None). Emit COMBAT_DIRECTIVE
        //     to C++ when the stamp changed or a new bridge session has not received it -- the
        //     coordinator re-stamps every tick (idempotent) but the wire stays brain-cadence
        //     (§3.8.4 / §1). Fire-and-forget, no WAIT (like SET_TASK), and orthogonal to Pending --
        //     so it runs ahead of the wedge/goal machinery and regardless of any in-flight leg.
        if (ctx.NeedsCombatDirectiveEmission)
        {
            CircuitTrace.Hit(ctx.Guid, "tick: combat directive changed or session renewed, emit to C++");
            await _executor.IssueNoWaitAsync(ctx, BridgeCommand.Combat(ctx.CombatDirective));
            ctx.MarkCombatDirectiveEmitted();
        }

        // 1b. A repeated group no-path is quarantined until the coordinator
        //     changes the order. It never becomes a teleport-to-destination.
        if (await TryQuarantineUnreachableGroupLegAsync(ctx)) { CircuitTrace.Hit(ctx.Guid, "tick: spent by group-leg quarantine"); return; }

        // 1b-1. [STUCK-STILL] Ground truth: if the bot hasn't physically MOVED in the still-window,
        //       it is stuck — full stop — regardless of what goal/why it is cycling. A walking,
        //       questing or grinding bot always moves, so this never touches a bot mid-trek. Checked
        //       before the outcome-based wedge/streak machinery so a frozen bot ejects on the first
        //       window instead of after 6 wedges.
        if (await TryEjectIfPhysicallyStuckAsync(ctx)) { CircuitTrace.Hit(ctx.Guid, "tick: spent by physical-stuck eject"); return; }

        // 1b-2. [FINDING_020] Island escape. A bot whose OWN start cannot path anywhere (core-tagged
        //       start_isolated on N consecutive fails from one spot) has no move that can succeed —
        //       port it to its level-band home. Before the wedge breaker, which such a bot never trips.
        if (await TryEscapeIslandAsync(ctx)) { CircuitTrace.Hit(ctx.Guid, "tick: spent by island escape"); return; }

        // 1c. No-progress circuit breaker. Fires before goal/plan so a wedged bot is parked, not driven.
        if (await TryBreakWedgeAsync(ctx)) { CircuitTrace.Hit(ctx.Guid, "tick: spent by wedge breaker"); return; }

        // 1d. Reconcile the held objective against C++'s reported task (Held-Objective build §3).
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
        {
            CircuitTrace.HitNote(ctx.Guid, "tick: goal changed, entering new goal", ctx.GoalReason);
            await EnterGoalAsync(ctx, goal);
        }

        // 3. Resolve the planner for the active goal. No planner (e.g. Idle) → run
        //    the deadline rule and stop.
        if (!_planners.TryGetValue(ctx.Goal, out var planner))
        {
            CircuitTrace.Hit(ctx.Guid, "tick: no planner for goal (idle) - deadline check only");
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
            CircuitTrace.Hit(ctx.Guid, "tick: WAIT deadline expired -> failure for recovery");
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
            CircuitTrace.Hit(ctx.Guid, "tick: soft re-plan rescan due");
            var rescan = planner.Rescan(ctx, snap);
            if (rescan is StepResult.Issue or StepResult.Dispatch)
            {
                CircuitTrace.Hit(ctx.Guid, "tick: rescan preempted the in-flight leg");
                _executor.ClearPending(ctx);
                await DispatchStepAsync(ctx, rescan);
                _supervisor.Check(ctx, snap);
                return;
            }
            if (ctx.Pending != null)
            {
                CircuitTrace.Hit(ctx.Guid, "tick: rescan kept waiting, next rescan pushed");
                ctx.Pending.RescanAtUtc = DateTime.UtcNow + RescanInterval;
            }
        }

        // 4. Act only when nothing is outstanding. A pending failure (a negated or
        //    expired WAIT) ALWAYS goes to the planner to recover — never to reselect,
        //    or an unreachable quest would be re-picked on a loop instead of deferred.
        //    Otherwise: progressing → advance one step; genuinely wedged with no
        //    failure signal → OnStall. A Blocked step (e.g. no_quests) routes to OnStall.
        if (ctx.Pending == null)
        {
            CircuitTrace.Hit(ctx.Guid, "tick: act block entered (no WAIT outstanding)");
            if (ctx.Failure == null && !planner.IsProgressing(ctx, snap))
            {
                CircuitTrace.Hit(ctx.Guid, "tick: not progressing -> stall handler");
                await HandleStallAsync(ctx, planner.OnStall(ctx));
            }
            else
            {
                CircuitTrace.Hit(ctx.Guid, "tick: plan next step");
                var step = planner.PlanNext(ctx, snap);
                if (step is StepResult.Blocked)
                {
                    CircuitTrace.Hit(ctx.Guid, "tick: planner blocked -> stall handler");
                    await HandleStallAsync(ctx, planner.OnStall(ctx));
                }
                else
                {
                    CircuitTrace.Hit(ctx.Guid, "tick: dispatch planned step");
                    await DispatchStepAsync(ctx, step);
                }
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
        // [CONSCRIPTED] An enlisted bot idles by ORDER — never a wedge, never a
        // streak, never a stranded port. (Unreachable today via the TickAsync
        // gate; kept as a wall against call-order drift.)
        if (ctx.Conscripted || ctx.Possessed) { CircuitTrace.Hit(ctx.Guid, "wedge: external control exempt"); return false; }
        var id = ctx.Identity;
        if (id?.WedgeBackoffUntil is DateTime parked && DateTime.UtcNow < parked)
        {
            CircuitTrace.Hit(ctx.Guid, "wedge: already parked, backoff holds");
            return false;   // already parked — let the backoff hold; GoalSelector keeps it Idle
        }

        bool wedged = ctx.TimeSinceProgressSec > WedgeCeilingSec || ctx.ConsecutiveFailures >= WedgeFailCap;
        if (!wedged) { CircuitTrace.Hit(ctx.Guid, "wedge: no wedge (progress fresh)"); return false; }
        CircuitTrace.Hit(ctx.Guid, "wedge: TRIPPED", ctx.TimeSinceProgressSec);
        CircuitTrace.RequestDump(ctx.Guid, "wedge");   // R8: flush this bot's recent ring even if nobody armed it

        double noProg = ctx.TimeSinceProgressSec;
        int fails = ctx.ConsecutiveFailures;

        _executor.ClearPending(ctx);
        ctx.Failure = null;
        ctx.ConsecutiveFailures = 0;
        id?.ClearGrindRelocate();
        if (ctx.Goal == Goal.Grinding)
        {
            CircuitTrace.Hit(ctx.Guid, "wedge: dead grind cell recorded");
            ctx.RecordDeadGrindCell(ctx.Pos.X, ctx.Pos.Y);   // don't drop back onto this dead spot
        }

        // A wedge while routing to a trainer is the L1 bum-rush loop: with HasUnlearnedSpells set, the
        // GoalSelector training trigger re-fires on every reselect and the bot bee-lines the (unreachable /
        // crowded / interior-pocket) trainer again the instant the backoff lapses — never questing, never
        // levelling. Stamp a give-up cooldown (same window as TrainingPlanner's own give-up) so the trigger
        // is gated and the bot falls through to questing; it re-attempts the trainer after the cooldown, by
        // then questing-travelled to a possibly-reachable one. HasUnlearnedSpells is left SET on purpose —
        // the bot still owes the training, it's deferred, not abandoned. This also destaggers a crowd: each
        // bot cools down at a slightly different time and drifts off to quest, thinning the trainer pileup.
        if (ctx.Goal == Goal.Training && id != null)
        {
            CircuitTrace.Hit(ctx.Guid, "wedge: trainer-route wedge, training trigger cooled");
            id.TrainCooldownUntil = DateTime.UtcNow.AddSeconds(TrainWedgeCooldownSec);
        }

        if (id != null)
        {
            CircuitTrace.Hit(ctx.Guid, "wedge: parked with backoff", id.WedgeStreak + 1);
            id.WedgeBackoffUntil = DateTime.UtcNow.AddSeconds(WedgeBackoffSec);
            // Future-stamp the progress clock to the park END so the bot gets a fresh full window on
            // resume (the idle park itself isn't "no progress" to be punished for).
            ctx.LastProgressUtc = id.WedgeBackoffUntil.Value;

            // [ESCAPE] (FINDING_010) Stranded escalation — see StrandedWedgeCap. Alive + solo +
            // out-of-combat only (the death path already has FINDING_008's hearth; combat ports are
            // C++-refused anyway). Clears the grind-lock state so the bot quests fresh at home, and
            // blacklists the stranded cell until it has outleveled it (~7 levels of headroom).
            id.WedgeStreak++;
            if (id.WedgeStreak >= StrandedWedgeCap)
            {
                CircuitTrace.Hit(ctx.Guid, "wedge: stranded escalation cap reached", id.WedgeStreak);
                if (ctx.Dead)
                {
                    CircuitTrace.Hit(ctx.Guid, "wedge: stranded escape suppressed, death recovery owns bot");
                }
                else if (ctx.InPlayerParty)
                {
                    CircuitTrace.Hit(ctx.Guid, "wedge: stranded escape suppressed, player party owns movement");
                }
                else if (ctx.InCombat)
                {
                    // Active combat is never ported blindly. The independent
                    // physical-still timer decides whether this is a proven
                    // combat-still case and owns the correlated reset handshake.
                    CircuitTrace.Hit(ctx.Guid, "wedge: stranded escape suppressed, combat-reset gate owns recovery");
                }
                else
                {
                    CircuitTrace.Hit(ctx.Guid, "wedge: stranded escalation (streak at cap)", id.WedgeStreak);
                    // [ESCAPE-BANDS] Level-appropriate escape: the LOWEST band whose range holds the bot's
                    // level, rolling a same-faction spot that is NOT the pocket it's stuck in (so a bot
                    // stuck in its own starter rolls a different same-faction starter — valid at 1-5). This
                    // replaces the level-blind HomeFor + PickEscapeTown that could fling a L2 into Loch
                    // Modan. HomeFor is the defensive fallback only if the level lands outside every band.
                    (float X, float Y, float Z, int Map)? dest =
                        PickBandedEscape(ctx, id.Level, id.EscapeRotation, ZoneSafetyMap.TeamFromFaction(id.Faction));
                    if (dest is null)
                    {
                        CircuitTrace.Hit(ctx.Guid, "wedge: no banded escape, trying HomeFor fallback");
                        var home = BotIdentity.HomeFor(id.Race, id.Level);
                        if (home.Map >= 0) { CircuitTrace.Hit(ctx.Guid, "wedge: HomeFor fallback valid"); dest = home; }
                    }

                    if (dest is { } d)
                    {
                        CircuitTrace.Hit(ctx.Guid, "wedge: stranded escape port issued");
                        await IssueEscapePortAsync(ctx, id, d,
                            $"STRANDED (wedge streak {StrandedWedgeCap}, goal {ctx.Goal}, L{id.Level})");
                    }
                    else
                    {
                        CircuitTrace.Hit(ctx.Guid, "wedge: no escape town at all, streak reset + local park");
                        // No alternate town on this map at all (shouldn't happen for a live bot) — reset
                        // and let the wedge ladder keep shuffling it locally.
                        id.WedgeStreak = 0;
                        _logger.LogInformation(
                            "[ESCAPE] {Name} stranded @ {Pos} but no alt escape town on map{Map} — parking",
                            ctx.Name, ctx.Pos, ctx.MapId);
                    }
                }
            }
        }

        _logger.LogWarning(
            "[BRAIN] {Name} WEDGE (noProg={T:F0}s fails={F}) — park {P}s then relocate fresh (goal {G} @ {Pos})",
            ctx.Name, noProg, fails, WedgeBackoffSec, ctx.Goal, ctx.Pos);

        await EnterGoalAsync(ctx, Goal.Idle);   // next tick reselects; parks while the backoff holds
        return true;
    }

    // [FINDING_020] Island escape knobs. IslandEscapeCap consecutive core-tagged start_isolated
    // MOVE_FAILEDs from one ~10yd spot = the bot is on a navmesh island / WMO pocket / in water and
    // cannot path out (post-FINDING_011 the straight-line shortcut is gone). The 3-fail cap is well
    // above a single transient mis-probe; the cooldown bounds worst-case port churn if the bot walks
    // straight back into the same trap.
    private const int IslandEscapeCap = 3;
    private const double IslandEscapeCooldownSec = 600;

    // [FINDING_020 round 4] Escape destinations — vetted, guarded, on-mesh town spots (the same
    // coords HomeFor trusts), spread across each continent. A bot wedged AT its home town is ported
    // to a DIFFERENT same-map town from this pool. This replaces the old "skipping no-op port" trap
    // that pinned ~300 bots / 20 min in the Menethil gate-tower / Crossroads-inn / Auberdine pockets:
    // porting to the coord you're STUCK on is a no-op, but porting to another town is a real escape.
    // guid+rotation spread so a mass of stuck bots diffuses across towns instead of re-piling on one.

    // [STUCK-STILL] Physical-stuck detector knobs. A bot within StuckStillRadius of its anchor for
    // StuckStillSeconds (alive/solo and not currently landing kills) is physically frozen. Combat is
    // deliberately observed, not excluded: it suppresses a direct port and enters the bounded reset
    // handshake only after the independent wedge streak also proves repeated non-recovery.
    private const float StuckStillRadius = 3f;
    private const double StuckStillSeconds = 120;

    /// <summary>[STUCK-STILL] Ground-truth physical-stuck eject. If the bot hasn't physically moved
    /// more than StuckStillRadius in StuckStillSeconds (alive + solo), recover it through a friendly
    /// hub. Out of combat this preserves the first-window escape. In combat it first sends the protocol-6
    /// correlated RESET_COMBAT_STUCK command at the bounded wedge cap; only its exact ACK plus a newer
    /// out-of-combat STATE permits the existing escape port. A bot mid-trek moves, so this never fires on
    /// it; a bot still landing REAL kills (in-place grind) is productive, so a recent kill restarts it.</summary>
    private async Task<bool> TryEjectIfPhysicallyStuckAsync(BotContext ctx)
    {
        var id = ctx.Identity;

        if (id == null)
        {
            CircuitTrace.Hit(ctx.Guid, "stuck-still: ineligible, identity unavailable");
            return false;
        }

        // Once issued, this recovery owns the bot until a terminal outcome and
        // post-ACK STATE resolve it. In particular, the normal wedge breaker may
        // not clear its correlated WAIT out from underneath it.
        if (ctx.CombatStillRecoveryStage != CombatStillRecoveryStage.None)
        {
            CircuitTrace.Hit(ctx.Guid, "combat-still: in-flight recovery owns tick");
            return await ContinueCombatStillRecoveryAsync(ctx, id);
        }

        StillObservation observation = ObserveFreshStillPosition(ctx, id);
        switch (observation.Kind)
        {
            case StillObservationKind.MissingState:
                CircuitTrace.Hit(ctx.Guid, "stuck-still: suppressed, STATE timestamp missing");
                return false;
            case StillObservationKind.StateNotAdvanced:
                CircuitTrace.Hit(ctx.Guid, "stuck-still: suppressed, STATE not advanced");
                return false;
            case StillObservationKind.Seeded:
                CircuitTrace.Hit(ctx.Guid, "stuck-still: first fresh STATE, anchor seeded");
                ResetCombatStillRecovery(ctx, clearCooldown: true);
                return false;
            case StillObservationKind.BridgeSessionChanged:
                CircuitTrace.Hit(ctx.Guid, "stuck-still: bridge session changed, window reset");
                ResetCombatStillRecovery(ctx, clearCooldown: true);
                return false;
            case StillObservationKind.ContinuityReset:
                CircuitTrace.Hit(ctx.Guid, "stuck-still: telemetry continuity gap, window reset");
                ResetCombatStillRecovery(ctx, clearCooldown: true);
                return false;
            case StillObservationKind.MapChanged:
                CircuitTrace.Hit(ctx.Guid, "stuck-still: map changed, window reset");
                ResetCombatStillRecovery(ctx, clearCooldown: true);
                return false;
            case StillObservationKind.Moved:
                CircuitTrace.Hit(ctx.Guid, "stuck-still: moved, window restarted");
                ResetCombatStillRecovery(ctx, clearCooldown: true);
                return false;
        }

        // Death and real-player ownership each have their own recovery/intent.
        // Name the exact suppression and restart the physical proof window.
        if (ctx.Dead)
        {
            CircuitTrace.Hit(ctx.Guid, "stuck-still: ineligible dead, window reset");
            ResetStillWindow(ctx, id, ctx.LastStateReceivedUtc);
            ResetCombatStillRecovery(ctx, clearCooldown: true);
            return false;
        }
        if (ctx.InPlayerParty)
        {
            CircuitTrace.Hit(ctx.Guid, "stuck-still: ineligible player-party, window reset");
            ResetStillWindow(ctx, id, ctx.LastStateReceivedUtc);
            ResetCombatStillRecovery(ctx, clearCooldown: true);
            return false;
        }
        if (IsBotGroupOwned(ctx))
        {
            CircuitTrace.Hit(ctx.Guid, "stuck-still: ineligible bot-group, window reset");
            ResetStillWindow(ctx, id, ctx.LastStateReceivedUtc);
            ResetCombatStillRecovery(ctx, clearCooldown: true);
            return false;
        }
        if (ctx.Possessed)
        {
            CircuitTrace.Hit(ctx.Guid, "stuck-still: ineligible possessed, window reset");
            ResetStillWindow(ctx, id, ctx.LastStateReceivedUtc);
            ResetCombatStillRecovery(ctx, clearCooldown: true);
            return false;
        }
        if (ctx.Conscripted)
        {
            CircuitTrace.Hit(ctx.Guid, "stuck-still: ineligible conscripted, window reset");
            ResetStillWindow(ctx, id, ctx.LastStateReceivedUtc);
            ResetCombatStillRecovery(ctx, clearCooldown: true);
            return false;
        }

        // Still landing real kills → productive in-place grind, not stuck. (LastKillUtc advances only
        // on REAL kills — trash/grey kills don't count — so a chicken farmer still reads as stuck.) A
        // kill restarts the proof window; merely returning here would let an old anchor mature behind
        // productive combat and produce an instant false verdict when kill recency elapsed.
        if ((DateTime.UtcNow - ctx.LastKillUtc).TotalSeconds < StuckStillSeconds)
        {
            CircuitTrace.Hit(ctx.Guid, "stuck-still: productive in-place grind, window reset by real kill");
            ResetStillWindow(ctx, id, ctx.LastStateReceivedUtc);
            ResetCombatStillRecovery(ctx, clearCooldown: true);
            return false;
        }

        // Not yet frozen for the whole window.
        if (observation.ElapsedSeconds < StuckStillSeconds)
        {
            CircuitTrace.Hit(ctx.Guid, "stuck-still: fresh window remains below threshold", observation.ElapsedSeconds);
            if (ctx.InCombat)
                CircuitTrace.Hit(ctx.Guid, "stuck-still: combat observed, fixed-position timer continues", observation.ElapsedSeconds);
            else
                CircuitTrace.Hit(ctx.Guid, "stuck-still: window not yet elapsed", observation.ElapsedSeconds);
            return false;
        }

        if (ctx.InCombat)
        {
            CircuitTrace.Hit(ctx.Guid, "combat-still: fixed whole window while in combat", observation.ElapsedSeconds);
            CombatStillResetGate gate = ClassifyCombatStillResetGate(ctx, id, DateTime.UtcNow);
            switch (gate)
            {
                case CombatStillResetGate.ProtocolTooOld:
                    CircuitTrace.Hit(ctx.Guid, "combat-still: reset suppressed, bridge protocol below 6", ctx.BridgeProtocol);
                    return false;
                case CombatStillResetGate.WedgeStreakBelowCap:
                    CircuitTrace.Hit(ctx.Guid, "combat-still: reset suppressed, wedge streak below cap", id.WedgeStreak);
                    return false;
                case CombatStillResetGate.CooldownActive:
                    CircuitTrace.Hit(ctx.Guid, "combat-still: reset suppressed, retry cooldown active",
                        (ctx.CombatStillResetCooldownUntilUtc!.Value - DateTime.UtcNow).TotalSeconds);
                    return false;
            }

            CircuitTrace.Hit(ctx.Guid, "combat-still: bounded reset issued", id.WedgeStreak);
            CircuitTrace.RequestDump(ctx.Guid, "combat-still-reset");
            _executor.ClearPending(ctx);
            ctx.Failure = null;
            ctx.CombatStillRecoveryStage = CombatStillRecoveryStage.AwaitingResetOutcome;
            ctx.CombatStillResetIssuedStateUtc = ctx.LastStateReceivedUtc;
            ctx.CombatStillResetAckReceivedUtc = default;
            ctx.CombatStillResetBridgeSessionId = ctx.BridgeSessionId;

            await _executor.IssueAsync(
                ctx,
                CreateCombatStillResetCommand(ctx, id, observation.ElapsedSeconds),
                CombatStillResetAckEvent,
                TimeSpan.FromSeconds(CombatStillResetDeadlineSec));

            // EVENT handling is serialized behind this tick, so a missing WAIT
            // here is a definite send/session failure, never an ultra-fast ACK.
            if (ctx.Pending == null)
            {
                CircuitTrace.Hit(ctx.Guid, "combat-still: reset issue produced no WAIT");
                string reason = ctx.Failure?.Reason ?? "session_superseded";
                FailCombatStillReset(ctx, reason);
            }
            return true;
        }

        CircuitTrace.Hit(ctx.Guid, "stuck-still: FROZEN whole window out of combat", observation.ElapsedSeconds);

        // Physically frozen for the whole window → eject to a level-appropriate friendly hub.
        // [ESCAPE-BANDS] lowest band containing the level, same-faction, never the pocket we're in.
        (float X, float Y, float Z, int Map)? dest =
            PickBandedEscape(ctx, id.Level, id.EscapeRotation, ZoneSafetyMap.TeamFromFaction(id.Faction));
        if (dest is null)
        {
            CircuitTrace.Hit(ctx.Guid, "stuck-still: no banded escape, trying HomeFor fallback");
            var home = BotIdentity.HomeFor(id.Race, id.Level);
            if (home.Map >= 0) { CircuitTrace.Hit(ctx.Guid, "stuck-still: HomeFor fallback valid"); dest = home; }
        }

        if (dest is not { } d)
        {
            CircuitTrace.Hit(ctx.Guid, "stuck-still: nowhere to send, window restarted");
            // Nowhere friendly to send (shouldn't happen for a live bot) — restart the window so we
            // don't re-evaluate every tick, and let the wedge ladder remain the fallback.
            ResetStillWindow(ctx, id, ctx.LastStateReceivedUtc);
            return false;
        }

        _logger.LogWarning(
            "[STUCK] {Name} physically frozen {Sec:F0}s (<{R:F0}yd) @ {Pos} — ejecting to friendly hub ({X:F0},{Y:F0})@map{Map}",
            ctx.Name, StuckStillSeconds, StuckStillRadius, ctx.Pos, d.X, d.Y, d.Map);
        await IssueEscapePortAsync(ctx, id, d, $"STUCK — no movement {StuckStillSeconds:F0}s");
        id.StillAnchorX = d.X;
        id.StillAnchorY = d.Y;
        id.StillSinceUtc = ctx.LastStateReceivedUtc;
        ctx.StillAnchorMapId = d.Map;
        return true;
    }

    /// <summary>
    /// Advance the fixed-position proof using each STATE at most once. A stale
    /// duplicate tick cannot advance time, and a telemetry gap longer than the
    /// bridge freshness budget restarts the proof instead of counting an
    /// unobserved disconnect as stationary time.
    /// </summary>
    internal static StillObservation ObserveFreshStillPosition(BotContext ctx, BotIdentity id)
    {
        DateTime stateUtc = ctx.LastStateReceivedUtc;
        if (stateUtc == default)   // cb:fold observation classifier; caller probes MissingState
            return new(StillObservationKind.MissingState, 0);   // cb:fold observation classifier; caller probes MissingState

        DateTime priorStateUtc = ctx.LastStillObservationStateUtc;
        if (priorStateUtc != default
            && ctx.LastStillObservationBridgeSessionId != ctx.BridgeSessionId)
        {   // cb:fold observation classifier; caller probes BridgeSessionChanged
            ResetStillWindow(ctx, id, stateUtc);
            return new(StillObservationKind.BridgeSessionChanged, 0);
        }
        if (priorStateUtc != default && stateUtc <= priorStateUtc)   // cb:fold observation classifier; caller probes StateNotAdvanced
            return new(StillObservationKind.StateNotAdvanced, 0);   // cb:fold observation classifier; caller probes StateNotAdvanced

        if (priorStateUtc == default || id.StillSinceUtc == default)   // cb:fold observation classifier; caller probes Seeded
        {   // cb:fold observation classifier; caller probes Seeded
            ResetStillWindow(ctx, id, stateUtc);
            return new(StillObservationKind.Seeded, 0);
        }

        if ((stateUtc - priorStateUtc).TotalSeconds > StillStateContinuitySec)   // cb:fold observation classifier; caller probes ContinuityReset
        {   // cb:fold observation classifier; caller probes ContinuityReset
            ResetStillWindow(ctx, id, stateUtc);
            return new(StillObservationKind.ContinuityReset, 0);
        }

        ctx.LastStillObservationStateUtc = stateUtc;
        if (ctx.StillAnchorMapId != ctx.MapId)   // cb:fold observation classifier; caller probes MapChanged
        {   // cb:fold observation classifier; caller probes MapChanged
            ResetStillWindow(ctx, id, stateUtc);
            return new(StillObservationKind.MapChanged, 0);
        }

        float dx = ctx.Pos.X - id.StillAnchorX;
        float dy = ctx.Pos.Y - id.StillAnchorY;
        if (dx * dx + dy * dy > StuckStillRadius * StuckStillRadius)   // cb:fold observation classifier; caller probes Moved
        {   // cb:fold observation classifier; caller probes Moved
            ResetStillWindow(ctx, id, stateUtc);
            return new(StillObservationKind.Moved, 0);
        }

        return new(
            StillObservationKind.Still,
            Math.Max(0, (stateUtc - id.StillSinceUtc).TotalSeconds));
    }

    internal static CombatStillResetGate ClassifyCombatStillResetGate(
        BotContext ctx,
        BotIdentity id,
        DateTime utcNow)
    {
        if (id.WedgeStreak < StrandedWedgeCap)   // cb:fold pure gate classifier; caller switch probes result
            return CombatStillResetGate.WedgeStreakBelowCap;   // cb:fold pure gate classifier; caller switch probes result
        if (ctx.BridgeProtocol < CombatStillResetProtocol)   // cb:fold pure gate classifier; caller switch probes result
            return CombatStillResetGate.ProtocolTooOld;   // cb:fold pure gate classifier; caller switch probes result
        if (ctx.CombatStillResetCooldownUntilUtc is DateTime cooldown && utcNow < cooldown)   // cb:fold pure gate classifier; caller switch probes result
            return CombatStillResetGate.CooldownActive;   // cb:fold pure gate classifier; caller switch probes result
        return CombatStillResetGate.Eligible;
    }

    internal static CombatStillPostResetGate ClassifyCombatStillPostResetGate(BotContext ctx)
    {
        if (ctx.BridgeSessionId != ctx.CombatStillResetBridgeSessionId)   // cb:fold pure post-reset classifier; caller switch probes result
            return CombatStillPostResetGate.SessionSuperseded;   // cb:fold pure post-reset classifier; caller switch probes result
        if (ctx.CombatStillResetAckReceivedUtc == default
            || ctx.LastStateReceivedUtc <= ctx.CombatStillResetAckReceivedUtc)   // cb:fold pure post-reset classifier; caller switch probes result
            return CombatStillPostResetGate.AwaitNewerState;   // cb:fold pure post-reset classifier; caller switch probes result
        if (ctx.Dead)   // cb:fold pure post-reset classifier; caller switch probes result
            return CombatStillPostResetGate.Dead;   // cb:fold pure post-reset classifier; caller switch probes result
        if (ctx.InPlayerParty
            || IsBotGroupOwned(ctx)
            || ctx.Possessed
            || ctx.Conscripted)   // cb:fold pure post-reset classifier; caller switch probes result
            return CombatStillPostResetGate.ExternalOwner;   // cb:fold pure post-reset classifier; caller switch probes result
        if (ctx.InCombat)   // cb:fold pure post-reset classifier; caller switch probes result
            return CombatStillPostResetGate.StillInCombat;   // cb:fold pure post-reset classifier; caller switch probes result
        return CombatStillPostResetGate.SafeToEscape;
    }

    internal static bool IsBotGroupOwned(BotContext ctx)
        => ctx.GroupId.HasValue || ctx.GroupOrder.IsActive;

    internal static BridgeCommand CreateCombatStillResetCommand(
        BotContext ctx,
        BotIdentity id,
        double stillSeconds)
        => new(CombatStillResetCommandType, new
        {
            anchor_x = id.StillAnchorX,
            anchor_y = id.StillAnchorY,
            anchor_z = ctx.Pos.Z,
            anchor_map = ctx.MapId,
            radius = StuckStillRadius,
            still_seconds = Math.Max(0, (int)Math.Floor(stillSeconds)),
            wedge_streak = id.WedgeStreak
        });

    private async Task<bool> ContinueCombatStillRecoveryAsync(BotContext ctx, BotIdentity id)
    {
        if (ctx.CombatStillRecoveryStage == CombatStillRecoveryStage.AwaitingResetOutcome
            && ctx.Dead)
        {
            CircuitTrace.Hit(ctx.Guid, "combat-still: in-flight reset cancelled, bot died");
            _executor.ClearPending(ctx);
            ctx.Failure = null;
            ResetCombatStillRecovery(ctx, clearCooldown: true);
            ResetStillWindow(ctx, id, ctx.LastStateReceivedUtc);
            return false;
        }

        // A real kill after issuance disproves the premise that this combat is
        // inert. Retire the waiter and do not turn a recovered fight into a port.
        if (ctx.LastKillUtc > ctx.CombatStillResetIssuedStateUtc)
        {
            CircuitTrace.Hit(ctx.Guid, "combat-still: real kill cancelled reset recovery");
            _executor.ClearPending(ctx);
            ctx.Failure = null;
            ResetCombatStillRecovery(ctx, clearCooldown: true);
            ResetStillWindow(ctx, id, ctx.LastStateReceivedUtc);
            return false;
        }

        if (ctx.BridgeSessionId != ctx.CombatStillResetBridgeSessionId)
        {
            CircuitTrace.Hit(ctx.Guid, "combat-still: reset session superseded before proof");
            FailCombatStillReset(ctx, "session_superseded");
            return true;
        }

        if (ctx.CombatStillRecoveryStage == CombatStillRecoveryStage.AwaitingResetOutcome)
        {
            if (ctx.Pending is { } pending)
            {
                if (!pending.CommandType.Equals(CombatStillResetCommandType, StringComparison.OrdinalIgnoreCase))
                {
                    CircuitTrace.HitNote(ctx.Guid, "combat-still: reset ownership lost to another WAIT", pending.CommandType);
                    FailCombatStillReset(ctx, "wait_owner_replaced");
                    return true;
                }
                if (pending.Expired)
                {
                    CircuitTrace.Hit(ctx.Guid, "combat-still: reset ACK deadline expired");
                    _executor.ClearPending(ctx);
                    FailCombatStillReset(ctx, "deadline");
                    return true;
                }

                CircuitTrace.Hit(ctx.Guid, "combat-still: waiting for correlated reset outcome");
                return true;
            }

            if (ctx.Failure is { } failure
                && failure.CommandType.Equals(CombatStillResetCommandType, StringComparison.OrdinalIgnoreCase))
            {
                CircuitTrace.HitNote(ctx.Guid, "combat-still: correlated reset failed", failure.Reason);
                FailCombatStillReset(ctx, failure.Reason);
                return true;
            }
            // Only exact positive admission clears the WAIT without Failure.
            CircuitTrace.Hit(ctx.Guid, "combat-still: correlated reset ACK admitted");
            ctx.CombatStillRecoveryStage = CombatStillRecoveryStage.AwaitingPostResetState;
        }

        switch (ClassifyCombatStillPostResetGate(ctx))
        {
            case CombatStillPostResetGate.SessionSuperseded:
                CircuitTrace.Hit(ctx.Guid, "combat-still: post-reset STATE came from replacement session");
                FailCombatStillReset(ctx, "session_superseded");
                return true;
            case CombatStillPostResetGate.AwaitNewerState:
                CircuitTrace.Hit(ctx.Guid, "combat-still: reset ACK admitted, awaiting newer STATE");
                return true;
            case CombatStillPostResetGate.Dead:
                CircuitTrace.Hit(ctx.Guid, "combat-still: post-reset port suppressed, bot died");
                ResetCombatStillRecovery(ctx, clearCooldown: true);
                ResetStillWindow(ctx, id, ctx.LastStateReceivedUtc);
                return false;
            case CombatStillPostResetGate.ExternalOwner:
                string owner = ctx.Possessed
                    ? "possessed"
                    : ctx.Conscripted
                        ? "conscripted"
                        : IsBotGroupOwned(ctx)
                            ? "bot_group"
                            : "player_party";
                CircuitTrace.HitNote(ctx.Guid, "combat-still: post-reset port suppressed, external owner", owner);
                ResetCombatStillRecovery(ctx, clearCooldown: true);
                ResetStillWindow(ctx, id, ctx.LastStateReceivedUtc);
                return false;
            case CombatStillPostResetGate.StillInCombat:
                CircuitTrace.Hit(ctx.Guid, "combat-still: newer STATE remained in combat, port refused");
                FailCombatStillReset(ctx, "state_still_in_combat");
                return true;
        }

        (float X, float Y, float Z, int Map)? dest =
            PickBandedEscape(ctx, id.Level, id.EscapeRotation, ZoneSafetyMap.TeamFromFaction(id.Faction));
        if (dest is null)
        {
            CircuitTrace.Hit(ctx.Guid, "combat-still: no banded post-reset escape, trying HomeFor fallback");
            var home = BotIdentity.HomeFor(id.Race, id.Level);
            if (home.Map >= 0)
            {
                CircuitTrace.Hit(ctx.Guid, "combat-still: HomeFor fallback valid");
                dest = home;
            }
        }
        if (dest is not { } d)
        {
            CircuitTrace.Hit(ctx.Guid, "combat-still: reset succeeded but no safe escape exists");
            FailCombatStillReset(ctx, "no_safe_escape");
            return true;
        }

        CircuitTrace.Hit(ctx.Guid, "combat-still: newer STATE proved OOC, escape port issued");
        ResetCombatStillRecovery(ctx, clearCooldown: true);
        await IssueEscapePortAsync(ctx, id, d,
            $"COMBAT-STILL reset ACK + fresh OOC STATE (wedge streak {id.WedgeStreak})");
        id.StillAnchorX = d.X;
        id.StillAnchorY = d.Y;
        id.StillSinceUtc = ctx.LastStateReceivedUtc;
        ctx.StillAnchorMapId = d.Map;
        return true;
    }

    private static void ResetStillWindow(BotContext ctx, BotIdentity id, DateTime stateUtc)
    {
        id.StillAnchorX = ctx.Pos.X;
        id.StillAnchorY = ctx.Pos.Y;
        id.StillSinceUtc = stateUtc;
        ctx.StillAnchorMapId = ctx.MapId;
        ctx.LastStillObservationStateUtc = stateUtc;
        ctx.LastStillObservationBridgeSessionId = ctx.BridgeSessionId;
    }

    private static void ResetCombatStillRecovery(BotContext ctx, bool clearCooldown)
    {
        ctx.CombatStillRecoveryStage = CombatStillRecoveryStage.None;
        ctx.CombatStillResetIssuedStateUtc = default;
        ctx.CombatStillResetAckReceivedUtc = default;
        ctx.CombatStillResetBridgeSessionId = 0;
        if (clearCooldown)   // cb:fold reset-state hygiene; caller probes the reset reason
            ctx.CombatStillResetCooldownUntilUtc = null;   // cb:fold reset-state hygiene; caller probes the reset reason
    }

    private void FailCombatStillReset(BotContext ctx, string reason)
    {
        _executor.ClearPending(ctx);
        ctx.Failure = null;
        ctx.ConsecutiveFailures = 0;
        ctx.CombatStillRecoveryStage = CombatStillRecoveryStage.None;
        ctx.CombatStillResetIssuedStateUtc = default;
        ctx.CombatStillResetAckReceivedUtc = default;
        ctx.CombatStillResetBridgeSessionId = 0;
        ctx.CombatStillResetCooldownUntilUtc = DateTime.UtcNow.AddSeconds(CombatStillResetCooldownSec);
        CircuitTrace.HitNote(ctx.Guid, "combat-still: recovery cooled without port", reason);
        _logger.LogWarning(
            "[COMBAT-STILL] {Name} reset did not produce correlated ACK + fresh OOC STATE ({Reason}); no port, retry cooled {Sec:F0}s",
            ctx.Name, reason, CombatStillResetCooldownSec);
    }

    private const float EscapeSpreadYards = 500f;   // a candidate spot this close to the bot = the pocket it's in → skip it

    // ── [ESCAPE-BANDS] Level-appropriate escape destinations ───────────────────────────────────
    // The prior escape pool was level-BLIND (filtered only by map + faction + distance), so a stuck L2
    // human "at home" (Northshire) could roll Thelsamar (Loch Modan, 10-18) or Menethil (Wetlands,
    // 20-30) — dumped into a zone it cannot survive. These bands make the roll level-aware: each band is
    // a level range and a same-faction destination set whose OWN zone range
    // brackets that band, so a bot only ever ports to content it can handle:
    //   1-5   any same-faction STARTER zone (cross-continent OK — a gnome in Northshire is valid)
    //   6-10  the starter-zone hub towns (Goldshire / Kharanos / Dolanaar · Razor Hill / Brill / Bloodhoof)
    //   11-20 first real questing zones (Westfall / Loch Modan / Darkshore · Barrens / Silverpine / Stonetalon)
    //   21+   mid zones (Duskwood / Wetlands / Ashenvale · Hillsbrad / Ashenvale / Barrens)
    // Bands are non-overlapping and authored low→high; the selector takes the FIRST (lowest) band whose
    // range contains the level ("lowest safe band" — a L10 lands in the 6-10 tier, not 11-20). Coords are
    // canonical game_tele / playercreateinfo spots (verified 2026-08-21), all inside guard coverage.
    private static readonly (int Min, int Max, (float X, float Y, float Z, int Map, Team Team)[] Spots)[] EscapeBands =
    {
        (1, 5, new[]
        {
            (-8949.95f, -132.493f, 83.5312f, 0, Team.Alliance),  // Northshire (Elwynn)        1-6
            (-6240.32f, 331.033f,  382.758f, 0, Team.Alliance),  // Coldridge Valley (Dun Morogh) 1-6
            (10311.3f,  831.463f,  1326.41f, 1, Team.Alliance),  // Shadowglen (Teldrassil)    1-6
            (-618.518f, -4251.67f, 38.718f,  1, Team.Horde),     // Valley of Trials (Durotar) 1-6
            (1676.35f,  1677.45f,  121.67f,  0, Team.Horde),     // Deathknell (Tirisfal)      1-6
            (-2917.58f, -257.98f,  52.9968f, 1, Team.Horde),     // Camp Narache (Mulgore)     1-6
        }),
        (6, 10, new[]
        {
            (-9448.55f, 68.236f,   56.3225f, 0, Team.Alliance),  // Goldshire (Elwynn)         1-10
            (-5597.31f, -483.398f, 396.981f, 0, Team.Alliance),  // Kharanos (Dun Morogh)      1-10
            (9821.0f,   959.0f,    1314.0f,  1, Team.Alliance),  // Dolanaar (Teldrassil)      6-12
            (338.0f,    -4688.0f,  17.0f,    1, Team.Horde),     // Razor Hill (Durotar)       6-12
            (2247.0f,   252.0f,    34.0f,    0, Team.Horde),     // Brill (Tirisfal)           6-12
            (-2361.0f,  -349.0f,   -9.0f,    1, Team.Horde),     // Bloodhoof Village (Mulgore) 5-12
        }),
        (11, 20, new[]
        {
            (-10628.0f, 1036.0f,   33.0f,    0, Team.Alliance),  // Sentinel Hill (Westfall)   10-20
            (-5360.0f,  -2953.0f,  323.0f,   0, Team.Alliance),  // Thelsamar (Loch Modan)     10-18
            (6420.0f,   529.0f,    9.0f,     1, Team.Alliance),  // Auberdine (Darkshore)      10-20
            (-472.0f,   -2653.0f,  97.0f,    1, Team.Horde),     // The Crossroads (Barrens)   10-25
            (457.0f,    1548.0f,   132.0f,   0, Team.Horde),     // The Sepulcher (Silverpine) 10-20
            (966.147f,  926.499f,  104.649f, 1, Team.Horde),     // Sun Rock Retreat (Stonetalon) 15-27
        }),
        (21, 999, new[]
        {
            (-10559.0f, -1189.0f,  28.0f,    0, Team.Alliance),  // Darkshire (Duskwood)       18-30
            (-3688.0f,  -830.0f,   10.0f,    0, Team.Alliance),  // Menethil Harbor (Wetlands) 20-30
            (2676.19f,  -422.905f, 107.123f, 1, Team.Alliance),  // Astranaar (Ashenvale)      18-30
            (-34.1467f, -923.366f, 54.5576f, 0, Team.Horde),     // Tarren Mill (Hillsbrad)    20-30
            (2270.94f,  -2538.19f, 93.9198f, 1, Team.Horde),     // Splintertree Post (Ashenvale) 18-30
            (-472.0f,   -2653.0f,  97.0f,    1, Team.Horde),     // The Crossroads (Barrens)   10-25
        }),
    };

    /// <summary>[ESCAPE-BANDS] Level-appropriate escape destination. Picks the LOWEST band whose range
    /// contains the bot's level, then rolls a same-faction spot in it — excluding any spot within
    /// EscapeSpreadYards of where the bot stands (so it is never the pocket it is stuck in; a bot stuck
    /// IN its own starter therefore rolls a DIFFERENT same-faction starter, which is valid at 1-5).
    /// guid+rotation spread diffuses a mass of stuck bots across the band. Null only if the level is
    /// outside every band (unreachable: last band is open-ended) — caller falls back to HomeFor.</summary>
    private static (float X, float Y, float Z, int Map)? PickBandedEscape(BotContext ctx, int level, int rotation, Team team)
    {
        (float X, float Y, float Z, int Map, Team Team)[]? spots = null;
        foreach (var band in EscapeBands)
            if (level >= band.Min && level <= band.Max) { CircuitTrace.Hit(ctx.Guid, "escape-bands: band matched level", level); spots = band.Spots; break; }
        if (spots == null) { CircuitTrace.Hit(ctx.Guid, "escape-bands: level outside every band", level); return null; }

        var pool = new List<(float X, float Y, float Z, int Map)>(spots.Length);
        var near = new List<(float X, float Y, float Z, int Map)>(spots.Length);   // excluded "stuck pocket" spots
        foreach (var s in spots)
        {
            if (s.Team != team) { CircuitTrace.Hit(ctx.Guid, "escape-bands: spot skipped (enemy faction)"); continue; }
            float dx = s.X - ctx.Pos.X, dy = s.Y - ctx.Pos.Y;
            bool sameMapNear = s.Map == ctx.MapId && (dx * dx + dy * dy) < EscapeSpreadYards * EscapeSpreadYards;
            if (sameMapNear) { CircuitTrace.Hit(ctx.Guid, "escape-bands: spot excluded (the stuck pocket)"); near.Add((s.X, s.Y, s.Z, s.Map)); }
            else { CircuitTrace.Hit(ctx.Guid, "escape-bands: spot pooled"); pool.Add((s.X, s.Y, s.Z, s.Map)); }
        }
        if (pool.Count == 0) { CircuitTrace.Hit(ctx.Guid, "escape-bands: every spot was the pocket, using near pool"); pool = near; }
        if (pool.Count == 0) { CircuitTrace.Hit(ctx.Guid, "escape-bands: no candidate at all"); return null; }
        int idx = (int)(((uint)ctx.Guid + (uint)rotation) % (uint)pool.Count);
        return pool[idx];
    }

    /// <summary>[FINDING_020 round 4] Shared escape port: clear the wedge/island/grind-lock state and
    /// fire PORT_HOME to a chosen safe destination. Used by both the wedge stranded-escape and the
    /// island escape so "get the bot OUT" is one code path with one behaviour.</summary>
    private async Task IssueEscapePortAsync(BotContext ctx, BotIdentity id, (float X, float Y, float Z, int Map) dest, string kind)
    {
        id.WedgeStreak = 0;
        id.IslandStreak = 0;
        id.GrindLockUntil = null;
        id.GrindLockReleaseCooldownUntil = null;
        id.AddPathBlacklist(ctx.Pos.X, ctx.Pos.Y, id.Level + 10);
        ctx.Grind = null;
        _executor.ClearPending(ctx);
        ctx.Failure = null;
        ctx.ConsecutiveFailures = 0;
        id.EscapeRotation++;
        _logger.LogWarning("[ESCAPE] {Name} {Kind} @ {Pos} — PORT_HOME to ({X:F0},{Y:F0})@map{Map}",
            ctx.Name, kind, ctx.Pos, dest.X, dest.Y, dest.Map);
        await _executor.IssueNoWaitAsync(ctx, new BridgeCommand("SET_TASK", new
        {
            task = "PORT_HOME",
            home_x = dest.X,
            home_y = dest.Y,
            home_z = dest.Z,
            home_map = dest.Map
        }));
    }

    /// <summary>
    /// [FINDING_020] Port a bot whose OWN start is isolated (BotIdentity.IslandStreak ≥ cap) to its
    /// level-band home. Unlike the FINDING_010 stranded escape this deliberately does NOT apply the
    /// "already home" guard — the measured islands (Crossroads inn/graveyard, Menethil pier, Darkshire
    /// smithy) are all INSIDE their home town's 300yd box; the home coordinate itself is on-mesh and
    /// 30–70yd away, which is exactly the hop needed. Alive + out-of-combat + solo only (core refuses
    /// PORT_HOME in combat anyway). Clears the in-flight WAIT/failure so the planner re-plans from the
    /// new spot; keeps quest/grind state (the bot was mid-plan, nothing about its goals changed).
    /// </summary>
    private async Task<bool> TryEscapeIslandAsync(BotContext ctx)
    {
        var id = ctx.Identity;
        if (id == null || id.IslandStreak < IslandEscapeCap) { CircuitTrace.Hit(ctx.Guid, "island: no isolation streak"); return false; }
        if (ctx.Dead || ctx.InCombat || ctx.InPlayerParty || ctx.Conscripted || ctx.Possessed) { CircuitTrace.Hit(ctx.Guid, "island: ineligible state"); return false; }
        if (id.IslandEscapeCooldownUntil is DateTime cd && DateTime.UtcNow < cd) { CircuitTrace.Hit(ctx.Guid, "island: escape cooling down"); return false; }

        // [ESCAPE-BANDS] Level-appropriate destination: lowest band containing the level, same-faction,
        // never the pocket the bot is islanded in. HomeFor is the fallback if the level is off every band.
        (float X, float Y, float Z, int Map)? dest =
            PickBandedEscape(ctx, id.Level, id.EscapeRotation, ZoneSafetyMap.TeamFromFaction(id.Faction));
        if (dest is null)
        {
            CircuitTrace.Hit(ctx.Guid, "island: no banded escape, trying HomeFor fallback");
            var home = BotIdentity.HomeFor(id.Race, id.Level);
            if (home.Map < 0) { CircuitTrace.Hit(ctx.Guid, "island: no home either, streak reset"); id.IslandStreak = 0; return false; }
            dest = home;
        }

        id.IslandEscapeCooldownUntil = DateTime.UtcNow.AddSeconds(IslandEscapeCooldownSec);
        if (dest is not { } d)
        {
            CircuitTrace.Hit(ctx.Guid, "island: no destination, cooled + streak reset");
            // no alternate town on this map — cool down and let the wedge ladder handle it
            id.IslandStreak = 0;
            return false;
        }

        int streak = id.IslandStreak;
        CircuitTrace.Hit(ctx.Guid, "island: escape port issued", streak);
        await IssueEscapePortAsync(ctx, id, d, $"ISOLATED start (x{streak} start_isolated fails, goal {ctx.Goal})");
        return true;
    }

    /// <summary>
    /// Stop an unreachable group leg without converting path failure into an
    /// uncapped teleport. The coordinator sees the marker as a stuck member and
    /// may advance; the bot remains parked while the same structural order is
    /// stamped. Solo planners keep their existing bounded defer/give-up logic.
    /// </summary>
    private async Task<bool> TryQuarantineUnreachableGroupLegAsync(BotContext ctx)
    {
        var id = ctx.Identity;

        if (ctx.NoPathQuarantinedOrder is { } quarantined)
        {
            CircuitTrace.Hit(ctx.Guid, "quarantine: existing quarantine present");
            bool sameOrder = !ctx.Dead && ctx.GroupOrder.IsActive && ctx.GroupOrder == quarantined;
            if (sameOrder)
            {
                CircuitTrace.Hit(ctx.Guid, "quarantine: same order still stamped, holding");
                return true;
            }

            if (ctx.NoPathQuarantinedDest is { } oldDest)
            {
                CircuitTrace.Hit(ctx.Guid, "quarantine: order changed, quarantine lifted");
                id?.ClearNoPathStreak(oldDest.Map, oldDest.X, oldDest.Y);
            }
            ctx.NoPathQuarantinedOrder = null;
            ctx.NoPathQuarantinedDest = null;
        }

        if (id == null || ctx.Dead || ctx.Goal != Goal.Questing || !ctx.GroupOrder.IsActive)
        {
            CircuitTrace.Hit(ctx.Guid, "quarantine: not applicable this tick");
            return false;
        }

        Vec4? orderTarget = GroupOrderPathTarget(ctx.GroupOrder);
        if (orderTarget is not { } expected || !IsFinitePathDestination(expected))
        {
            CircuitTrace.Hit(ctx.Guid, "quarantine: order has no finite path target");
            return false;
        }

        Vec4? candidate = null;
        if (ctx.Failure is { CommandType: "MOVE_TO", Reason: "no_path", Dest: { } failed }
            && SamePathDestination(failed, expected))
        {
            CircuitTrace.Hit(ctx.Guid, "quarantine: no_path failure matches the order dest");
            candidate = failed;
        }
        else if (ctx.GroupOrder.Objective.IsActive
                 && id.GetNoPathStreak(expected.Map, expected.X, expected.Y) >= NoPathQuarantineStreakCount)
        {
            CircuitTrace.Hit(ctx.Guid, "quarantine: durable no-path streak on fire-and-forget objective");
            // Fire-and-forget group objectives have no WAIT/Failure; their
            // bridge-level durable streak is the only failure signal.
            candidate = expected;
        }

        if (candidate is not { } dest || !IsFinitePathDestination(dest))
        {
            CircuitTrace.Hit(ctx.Guid, "quarantine: no candidate destination");
            return false;
        }

        int streak = id.GetNoPathStreak(dest.Map, dest.X, dest.Y);
        if (streak < NoPathQuarantineStreakCount)
        {
            CircuitTrace.Hit(ctx.Guid, "quarantine: streak below cap", streak);
            return false;
        }
        CircuitTrace.Hit(ctx.Guid, "quarantine: ENGAGED, parking until order changes", streak);

        _logger.LogWarning(
            "[BRAIN] {Name} GROUP LEG QUARANTINED — {N} consecutive no_path to {Dest}; " +
            "holding until order changes (no teleport)",
            ctx.Name, streak, dest);

        ctx.NoPathQuarantinedOrder = ctx.GroupOrder;
        ctx.NoPathQuarantinedDest = dest;
        _executor.ClearPending(ctx);
        ctx.Failure = null;
        ctx.ConsecutiveFailures = 0;
        id.ClearGrindRelocate();
        await EnterGoalAsync(ctx, Goal.Idle);
        return true;
    }

    private static Vec4? GroupOrderPathTarget(GroupOrder order)
    {
        // Pure helper without a bot context — every arm folds into the caller's probes
        // (the quarantine's "no finite path target" probe reads the outcome).
        if (!order.IsActive)
            return null;   // cb:fold pure helper, outcome probed at caller

        if (order.Objective.IsActive)
            return new Vec4(order.Objective.X, order.Objective.Y, order.Objective.Z, order.Objective.Map);   // cb:fold pure helper, outcome probed at caller

        return order.Phase switch
        {
            GroupPhase.TravelToGiver or GroupPhase.Accept or   // cb:fold pure helper, outcome probed at caller
            GroupPhase.TravelToTurnIn or GroupPhase.TurnIn or
            GroupPhase.GroupVendor or GroupPhase.HoldAtAnchor or
            GroupPhase.GroupGrind or GroupPhase.GroupDefend => order.TargetPos,
            _ => null   // cb:fold pure helper, outcome probed at caller
        };
    }

    private static bool IsFinitePathDestination(Vec4 dest)
        => dest.Map >= 0
           && float.IsFinite(dest.X) && float.IsFinite(dest.Y) && float.IsFinite(dest.Z);

    private static bool SamePathDestination(Vec4 left, Vec4 right)
    {
        float dx = left.X - right.X;
        float dy = left.Y - right.Y;
        return left.Map == right.Map && dx * dx + dy * dy <= 25f;
    }

    /// <summary>Route an inbound bridge event for this bot through the executor's ack matching.</summary>
    public bool OnEvent(BotContext ctx, BotEvent evt)
    {
        return _executor.OnEvent(ctx, evt);
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
        if (ctx.Held is not { } held || !held.NeedsActuation) { CircuitTrace.Hit(ctx.Guid, "reconcile: nothing held"); return; }

        // Only reconcile while the bot is actually PURSUING the objective. Both a solo quest objective and a
        // group shared objective are worked under Goal.Questing (GoalSelector routes an active GroupOrder there,
        // and the solo enriched-objective leg is a Questing leg); a fallback grind clears Held on arm. So if the
        // bot has peeled to Maintenance / Training / Idle (death, heal, vendor, wedge-park), the held objective is
        // stale-by-context — re-issuing into another planner would clobber its in-flight WAIT (e.g. knock out a
        // RESURRECT mid-rez). Gate it. The strand case is untouched: the parked bot this exists to rescue is
        // still Goal.Questing (the 31,043 Questing/enter park).
        if (ctx.Goal != Goal.Questing) { CircuitTrace.Hit(ctx.Guid, "reconcile: not questing, held stale-by-context"); return; }

        var echo = ctx.HeldTask;
        if (!echo.IsKnown) { CircuitTrace.Hit(ctx.Guid, "reconcile: no C++ echo yet"); return; }                // no readback → degrade to ctx.Pending inference
        if (ctx.TimeSinceObjectiveSec < ReconcileGraceSec) { CircuitTrace.Hit(ctx.Guid, "reconcile: within adoption grace"); return; }   // just (re)committed — let C++ adopt it first
        if (held.MatchedBy(echo)) { CircuitTrace.Hit(ctx.Guid, "reconcile: C++ is on the held objective"); return; }   // C++ is on it → §5 progress checks own the rest

        // Re-fire cooldown (2026-07-03, the reconcile-storm fix — see ReconcileRefireCooldownSec's
        // docstring). LastReconcileUtc compared against ObjectiveSinceUtc, not used as a bare
        // timestamp, so a cooldown left over from a PRIOR objective can never suppress a legitimate
        // reconcile on a freshly-committed one (a genuinely new objective's own ReconcileGraceSec gate
        // above already governs its first fire).
        bool coolingDown = ctx.LastReconcileUtc >= ctx.ObjectiveSinceUtc
                            && (DateTime.UtcNow - ctx.LastReconcileUtc).TotalSeconds < ReconcileRefireCooldownSec;
        if (coolingDown) { CircuitTrace.Hit(ctx.Guid, "reconcile: refire cooling down"); return; }

        // A coordinator objective at the durable threshold is owned by the
        // step-1b quarantine. Do not metronome-reissue underneath it. Solo
        // planners retain their normal failure/defer recovery path.
        if (held.Source == ObjectiveSource.Coordinator && ctx.Identity is { } rid
            && rid.GetNoPathStreak(held.Target.Map, held.Target.X, held.Target.Y) >= NoPathQuarantineStreakCount)
        {
            CircuitTrace.Hit(ctx.Guid, "reconcile: owned by quarantine, no reissue");
            return;
        }

        ctx.LastReconcileUtc = DateTime.UtcNow;
        CircuitTrace.Hit(ctx.Guid, "reconcile: MISMATCH, re-issuing held leg");

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
        {
            CircuitTrace.Hit(ctx.Guid, "reconcile: cleared group change-guard for re-issue");
            ctx.LastGroupOrder = GroupOrder.None;   // force QuestPlanner.DriveGroup to re-issue the group leg
        }
        else
        {
            CircuitTrace.Hit(ctx.Guid, "reconcile: forced solo re-derive (step=plan)");
            ctx.SetStep("plan");                    // force QuestPlanner to RE-DERIVE the solo leg (not advance/defer)
        }
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
        {
            CircuitTrace.Hit(ctx.Guid, "goal-change: death blamed on active quest", dying.QuestId);
            ctx.DeathBlameQuestId = dying.QuestId;
        }

        // Stop a leaving C++ grind patrol so the next goal can actually drive the bot. BOTH
        // Grinding AND Questing run the autonomous C++ grind/objective patrol (an enriched
        // MOVE_TO that travels then grinds in place). A fresh PLAIN MOVE_TO — e.g. the vendor
        // route — does NOT cancel that in-place grind on the C++ side, so the bot keeps fighting
        // its grind pocket and never travels (observed: a vendor route from Questing moved ~24yd
        // in 120s while killing the same mobs, then tripped its leg deadline → giveup). SET_TASK
        // IDLE clears the patrol; the new goal re-arms its own task in PlanNext.
        if (ctx.Goal == Goal.Grinding || ctx.Goal == Goal.Questing)
        {
            CircuitTrace.Hit(ctx.Guid, "goal-change: SET_TASK IDLE stops the leaving patrol");
            await _executor.IssueNoWaitAsync(ctx, IdleTask());   // stop the autonomous patrol
        }

        ctx.SetGoal(goal, "enter");
        ResetScratch(ctx);                                       // each goal re-arms its own scratch in PlanNext
        if (goal != Goal.Grinding) { CircuitTrace.Hit(ctx.Guid, "goal-change: grind relocate cleared"); ctx.Identity?.ClearGrindRelocate(); }   // a half-done relocate doesn't survive a goal change
        _executor.ClearPending(ctx);
        ctx.Failure = null;                                      // stale negative outcome doesn't carry across goals
    }

    /// <summary>Act on the planner's chosen step.</summary>
    private async Task DispatchStepAsync(BotContext ctx, StepResult step)
    {
        switch (step)
        {
            case StepResult.Issue issue:
                CircuitTrace.Hit(ctx.Guid, "dispatch: issue command with WAIT");
                await _executor.IssueAsync(ctx, issue.Command, issue.ExpectedEvent, issue.Deadline);
                break;

            case StepResult.Dispatch dispatch:
                CircuitTrace.Hit(ctx.Guid, "dispatch: fire-and-forget command");
                await _executor.IssueNoWaitAsync(ctx, dispatch.Command);
                break;

            case StepResult.Done:
                CircuitTrace.Hit(ctx.Guid, "dispatch: goal done -> idle reselect");
                // Goal achieved — drop to Idle so the next tick reselects.
                await EnterGoalAsync(ctx, Goal.Idle);
                break;

            case StepResult.Continue:
            default:
                CircuitTrace.Hit(ctx.Guid, "dispatch: continue (nothing this tick)");
                break;   // Continue → nothing this tick. Blocked is intercepted in TickAsync → OnStall.
        }
    }

    /// <summary>Enforce the planner's stall verdict.</summary>
    private async Task HandleStallAsync(BotContext ctx, StallAction action)
    {
        switch (action.Kind)
        {
            case StallActionKind.ReselectGoal:
                CircuitTrace.Hit(ctx.Guid, "stall: reselect goal", ctx.ConsecutiveReselects + 1);
                // Track reselect churn: a reselect with no real progress (kill/quest/level) since the last
                // one is the bot recomputing the SAME dead answer. MarkProgress zeroes this, so a streak
                // only builds while the bot is genuinely stuck — the physical-stuck ejector reads it to
                // eject a dead-pocket bot on a short window instead of the full still window.
                ctx.ConsecutiveReselects++;
                ctx.LastReselectUtc = DateTime.UtcNow;
                _logger.LogDebug("[BRAIN] {Name} {Goal} reselect: {Detail} (churn={Churn})", ctx.Name, ctx.Goal, action.Detail, ctx.ConsecutiveReselects);
                // Stop the current patrol and drop to Idle; next tick reselects and
                // re-arms a fresh grind wherever the bot now stands (no phantom STUCK —
                // a grind never armed a Pending).
                await EnterGoalAsync(ctx, Goal.Idle);
                break;

            default:
                CircuitTrace.Hit(ctx.Guid, "stall: unhandled stall kind (no phase-2 handler)");
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
