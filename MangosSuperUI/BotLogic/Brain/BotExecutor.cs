using MangosSuperUI.BotLogic.Core;
using MangosSuperUI.BotLogic.Data;
using MangosSuperUI.BotLogic.Tracking;
using MangosSuperUI.Services;

namespace MangosSuperUI.BotLogic.Brain;

/// <summary>
/// F2 (2026-08-30): separates corpse proof from level relevance without
/// changing Stage-1 kill behavior. The pure seam makes the later policy flip
/// independently testable after the unconfirmed population is measured.
/// </summary>
internal enum KillCreditKind { Progress, Unconfirmed, TrashOrGrey }

// ============================================================================
// BotExecutor — the hand of the brain (§4).
//
// Owns command issue, the WAIT (ctx.Pending), and ack matching. A planner says
// WHAT to send (StepResult.Issue / StepResult.Dispatch); the executor SENDS it,
// and — for WAIT commands — records the outstanding command on the context and,
// when the matching outcome echoes back, clears the WAIT and stamps progress.
// Nothing here is goal-specific; this is the single piece of plumbing every goal
// shares.
//
// cbt (§5.2): every real WAIT owns a protocol correlation id. It is allocated
// BEFORE the socket write, stamped on ctx.Pending, sent as top-level cbt, and
// echoed on the terminal EVENT. Event type says what kind of outcome this is;
// exact cbt equality says which command it belongs to. A stale or missing cbt
// can therefore never release a newer same-type WAIT.
// ============================================================================
public sealed class BotExecutor
{
    private readonly BotBridgeService _bridge;
    private readonly ZoneSafetyMap _safety;
    private readonly ILogger<BotExecutor> _logger;

    // Progress-extending objective deadline (§6B.2). A kill-objective grind is
    // SET_TASK {kill_count=N} + a WAIT on TASK_COMPLETE. Each KILL while that WAIT is
    // pending pushes its deadline to now + this, so the objective fails only on a
    // NO-kill gap (like GrindPlanner's KILL-recency), never on wall-clock. Matches the
    // 120s grind liveness window. Tunable; must comfortably exceed mob respawn (Echo
    // Ridge workers = 180s, but pass-2 'kill anything' keeps the gap far under this).
    private static readonly TimeSpan ObjectiveKillGrace = TimeSpan.FromSeconds(120);

    // Premature-arrival guard (npc_not_found fix). Exact cbt matching is the structural stale-ack
    // defense; this remains as a secondary semantic check against a core falsely reporting success for
    // the CURRENT leg. We reject a TASK_COMPLETE only when it is BOTH implausibly young and far from
    // dest. We do NOT reject on distance alone:
    // ctx.Pos is refreshed only on the 5s STATE heartbeat, so a legitimately-walked long leg can read up
    // to ~one STATE-cycle stale (≈ 35yd at 7yd/s) at the instant C++'s real arrival lands. C++ only ever
    // emits "arrived" within its own 3yd 2D gate (AiBotAIMain), so once a leg is older than
    // PrematureArrivalSec its TASK_COMPLETE is real and is trusted regardless of the stale-position read.
    // Objective grinds are EXEMPT (IsObjectiveGrind): their "GRIND finished" completes away from dest.
    private const float ArrivalGateYards = 20f;
    private const double PrematureArrivalSec = 3.0;

    // First-rescan lead-in for an interruptible objective leg. Matches BotBrain's RescanInterval (10s,
    // which re-arms each subsequent rescan), so the en-route soft re-plan cadence is uniform. The 50yd
    // movement throttle inside QuestPlanner.Rescan is the real gate, so the exact value is non-critical.
    private static readonly TimeSpan RescanLeadIn = TimeSpan.FromSeconds(10);

    public BotExecutor(BotBridgeService bridge, ZoneSafetyMap safety, ILogger<BotExecutor> logger)
    {
        _bridge = bridge;
        _safety = safety;
        _logger = logger;
    }

    /// <summary>
    /// Issue a WAIT command for a bot: record the outstanding command on the
    /// context, then send it. ExpectedEvent + deadline come from the planner (the
    /// §5b.1 wire table). After this returns, ctx.Pending is armed and the
    /// Supervisor's deadline rule is watching it. The matching event (by type)
    /// clears the WAIT in OnEvent.
    /// </summary>
    public async Task IssueAsync(BotContext ctx, BridgeCommand cmd, string expectedEvent, TimeSpan deadline)
    {
        // [CONSCRIPTED] The commander owns this bot; no planner traffic. The C++
        // bridge fence would drop the command anyway — refusing here keeps
        // Pending unarmed so nothing waits on an event that can never come.
        if (ctx.Conscripted || ctx.Possessed)
        {
            CircuitTrace.HitNote(ctx.Guid, "issue: refused (externally controlled)", cmd.Type);
            _logger.LogDebug("[EXEC] {Name} refuse {Type} (externally controlled)", ctx.Name, cmd.Type);
            return;
        }

        var now = DateTime.UtcNow;
        NoWaitCommandOwner? priorNoWaitCommand = ctx.LatestNoWaitCommand;
        NoWaitCommandOwner? priorNoWaitTask = ctx.NoWaitTaskOwner;
        bool supersedesTaskMotion = cmd.Type.Equals("MOVE_TO", StringComparison.OrdinalIgnoreCase)
            || cmd.Type.Equals("SET_TASK", StringComparison.OrdinalIgnoreCase);

        // A newer waited command supersedes control-drop ownership. A waited
        // task/motion command also supersedes the prior no-WAIT task owner. If
        // the socket write is definitely not attempted, both are restored below
        // because the core is still running the old command.
        ctx.LatestNoWaitCommand = null;
        if (supersedesTaskMotion)
            ctx.NoWaitTaskOwner = null;   // cb:fold task-domain owner detail; send outcome probes the transition

        // An ENRICHED objective MOVE_TO (§4) carries creature_entry/kill_count: C++ travels
        // then grinds in place under this one WAIT. Flag it so the KILL-push in OnEvent rolls
        // its deadline forward once the grind starts (the travel deadline only covers travel).
        bool objectiveGrind = cmd.Type == "MOVE_TO" && cmd.Payload.ContainsKey("creature_entry");

        // 2026-06-30: cache the quest id off a QUEST_INTERACT's payload (accept OR complete) so
        // the ack handler can stamp durable bookkeeping synchronously, goal-bounce-proof. Mirrors
        // moveTgt below for MOVE_TO.
        int? questId = ExtractQuestId(cmd);

        // Allocate BEFORE arming/sending. If the core replies immediately, the
        // EVENT can already find this exact WAIT while the socket write is still
        // unwinding; allocating after await would create a lost-ack race.
        long correlationId = BridgeCorrelation.NextId();
        var outstanding = new Outstanding
        {
            CorrelationId = correlationId,
            CommandType = cmd.Type,
            ExpectedEvent = expectedEvent,
            SentUtc = now,
            DeadlineUtc = now + deadline,
            IsObjectiveGrind = objectiveGrind,
            // Interruptible: an objective leg (incl. the phase-4b lone-far trek) carries a rescan clock so
            // BotBrain step 3c re-evaluates it on a cadence while the WAIT is pending — QuestPlanner.Rescan
            // re-gathers around the bot's CURRENT position and PREEMPTS the trek if a closer quest folds in
            // en route (the "scan a 100yd radius along the whole path" behavior). Non-objective WAITs leave
            // this null (not interruptible). First rescan is one interval out; the 50yd movement throttle
            // inside Rescan no-ops it cheaply when the bot is grinding in place rather than travelling.
            RescanAtUtc = objectiveGrind ? now + RescanLeadIn : (DateTime?)null,
            QuestId = questId
        };
        ctx.Pending = outstanding;

        // Capture the destination so FleetReport can show distance-to-target.
        Vec4? moveTgt = null;
        if (cmd.Type == "MOVE_TO")
        {
            CircuitTrace.Hit(ctx.Guid, "issue: MOVE_TO destination captured");
            moveTgt = ExtractTarget(cmd);
            if (moveTgt != null) ctx.Target = moveTgt;   // cb:fold outcome carried by the capture probe
        }
        CircuitTrace.Hit(ctx.Guid, "issue: WAIT armed with correlation", correlationId);
        CircuitTrace.HitNote(ctx.Guid, "issue: WAIT armed + command sent", cmd.Type);

        // Instrumentation: a travel MOVE_TO logs dest + bot pos + distance; a QUEST_INTERACT logs the
        // npc_entry + bot pos. A premature/stale arrival ack (interact fired far from the NPC) is then a
        // one-line grep: "issue MOVE_TO -> (X,Y)" followed by "issue QUEST_INTERACT npc=N from (px,py)"
        // with (px,py) nowhere near (X,Y). Everything else logs exactly as before.
        if (cmd.Type == "MOVE_TO" && moveTgt is { } mt)
            _logger.LogDebug("[EXEC] {Name} issue MOVE_TO map={Map} -> ({X:F0},{Y:F0}) from ({PX:F0},{PY:F0}) d={D:F0} expect={Expect} deadline={Sec}s",   // cb:fold logging only
                ctx.Name, ctx.MapId, mt.X, mt.Y, ctx.Pos.X, ctx.Pos.Y, ctx.DistToTarget, expectedEvent, deadline.TotalSeconds);
        else if (cmd.Type == "QUEST_INTERACT")
            _logger.LogDebug("[EXEC] {Name} issue QUEST_INTERACT map={Map} npc={Npc} from ({PX:F0},{PY:F0}) expect={Expect} deadline={Sec}s",   // cb:fold logging only
                ctx.Name, ctx.MapId, cmd.Payload.TryGetValue("npc_entry", out var ne) ? ne : "?", ctx.Pos.X, ctx.Pos.Y, expectedEvent, deadline.TotalSeconds);
        else
            _logger.LogDebug("[EXEC] {Name} issue {Type} expect={Expect} deadline={Sec}s",   // cb:fold logging only
                ctx.Name, cmd.Type, expectedEvent, deadline.TotalSeconds);

        CorrelatedSendStatus sendStatus = await _bridge.TrySendCorrelatedAsync(
            ctx.Guid, cmd.Type, cmd.Payload, correlationId, ctx.BridgeSessionId);

        // The bridge's legacy send API is intentionally best-effort, but a WAIT
        // must not remain armed when no bytes were written. Only clear the exact
        // object we installed: an immediate ACK may already have resolved it.
        if (sendStatus == CorrelatedSendStatus.SessionSuperseded
            && ReferenceEquals(ctx.Pending, outstanding))
        {
            // A socket handoff is infrastructure churn, not a failed route or
            // action. Retire all old-session ownership without poisoning planner
            // failure state; a fresh STATE will drive the next decision.
            ctx.Pending = null;
            ctx.LatestNoWaitCommand = null;
            if (supersedesTaskMotion)
                ctx.NoWaitTaskOwner = null;   // cb:fold task-domain owner detail; session-retired probe carries outcome
            ctx.Failure = null;
            CircuitTrace.Hit(ctx.Guid, "issue: session replaced before send, WAIT retired", correlationId);
        }
        else if (sendStatus == CorrelatedSendStatus.DefinitelyNotSent
            && ReferenceEquals(ctx.Pending, outstanding))
        {
            ctx.LatestNoWaitCommand = priorNoWaitCommand;
            if (supersedesTaskMotion)
                ctx.NoWaitTaskOwner = priorNoWaitTask;   // cb:fold task-domain owner detail; send-failed probe carries outcome
            CircuitTrace.Hit(ctx.Guid, "issue: correlated send failed, WAIT released", correlationId);
            ctx.Pending = null;
            ctx.Failure = new WaitFailure
            {
                CommandType = cmd.Type,
                Reason = "send_failed",
                Dest = moveTgt,
                QuestId = questId,
                Utc = DateTime.UtcNow
            };
            ctx.ConsecutiveFailures++;
            _logger.LogWarning("[EXEC] {Name} failed to send {Type} cbt={Cbt}; WAIT released",
                ctx.Name, cmd.Type, correlationId);
        }
        else if (sendStatus == CorrelatedSendStatus.OutcomeUnknown
                 && ReferenceEquals(ctx.Pending, outstanding))
        {
            // A partial socket write may already have committed in the core.
            // Keep the bounded correlated waiter; retrying here could duplicate
            // a destructive/non-idempotent action.
            CircuitTrace.Hit(ctx.Guid, "issue: send outcome unknown, WAIT retained", correlationId);
            _logger.LogWarning("[EXEC] {Name} send outcome unknown for {Type} cbt={Cbt}; retaining WAIT to deadline",
                ctx.Name, cmd.Type, correlationId);
        }
    }

    /// <summary>
    /// Issue a no-WAIT command: send it and arm NO Pending. For indefinite,
    /// unacked tasks (SET_TASK GRIND kill_count=0, SET_TASK IDLE) whose liveness is
    /// owned by the planner's IsProgressing, not a one-shot ack. Nothing for the
    /// Supervisor's deadline rule to expire (§6.3).
    /// </summary>
    public async Task IssueNoWaitAsync(BotContext ctx, BridgeCommand cmd)
    {
        // [CONSCRIPTED] Same refusal as IssueAsync — the army answers to its
        // commander, not the planner.
        if (ctx.Conscripted || ctx.Possessed)
        {
            CircuitTrace.HitNote(ctx.Guid, "fire: refused (externally controlled)", cmd.Type);
            _logger.LogDebug("[EXEC] {Name} refuse {Type} (externally controlled)", ctx.Name, cmd.Type);
            return;
        }

        // Allocate and retain an owner even without a deadline WAIT. Protocol-v4
        // negative outcomes and control drops are still terminal feedback; exact
        // ownership stops a delayed A outcome from mutating a newer B task.
        long correlationId = BridgeCorrelation.NextId();
        bool ownsTaskMotion = cmd.Type.Equals("MOVE_TO", StringComparison.OrdinalIgnoreCase)
            || cmd.Type.Equals("SET_TASK", StringComparison.OrdinalIgnoreCase);
        bool canGrindBlock = cmd.Type.Equals("MOVE_TO", StringComparison.OrdinalIgnoreCase)
                && cmd.Payload.ContainsKey("creature_entry")
            || cmd.Type.Equals("SET_TASK", StringComparison.OrdinalIgnoreCase)
                && cmd.Payload.TryGetValue("task", out object? task)
                && string.Equals(task?.ToString(), "GRIND", StringComparison.OrdinalIgnoreCase);
        var owner = new NoWaitCommandOwner
        {
            CorrelationId = correlationId,
            CommandType = cmd.Type,
            OwnsTaskMotion = ownsTaskMotion,
            CanGrindBlock = canGrindBlock
        };
        NoWaitCommandOwner? priorNoWaitCommand = ctx.LatestNoWaitCommand;
        NoWaitCommandOwner? priorNoWaitTask = ctx.NoWaitTaskOwner;
        ctx.LatestNoWaitCommand = owner;
        if (ownsTaskMotion)
            ctx.NoWaitTaskOwner = owner;   // cb:fold task-domain owner detail; correlated send probe carries issue

        // Parity with IssueAsync: keep distance-to-target live for MOVE_TO fires.
        if (cmd.Type == "MOVE_TO")
        {
            CircuitTrace.Hit(ctx.Guid, "fire: MOVE_TO destination captured");
            var tgt = ExtractTarget(cmd);
            if (tgt != null) ctx.Target = tgt;   // cb:fold outcome carried by the capture probe
        }
        CircuitTrace.HitNote(ctx.Guid, "fire: no-wait command sent", cmd.Type);

        _logger.LogDebug("[EXEC] {Name} fire {Type} (no-wait)", ctx.Name, cmd.Type);

        CorrelatedSendStatus sendStatus = await _bridge.TrySendCorrelatedAsync(
            ctx.Guid, cmd.Type, cmd.Payload, correlationId, ctx.BridgeSessionId);
        if (sendStatus == CorrelatedSendStatus.SessionSuperseded)
        {
            ctx.TryReplaceLatestNoWaitOwner(owner, null);
            if (ownsTaskMotion)
                ctx.TryReplaceNoWaitTaskOwner(owner, null);   // cb:fold task-domain owner detail; session-retired probe carries outcome
            CircuitTrace.Hit(ctx.Guid, "fire: session replaced before send, owner retired", correlationId);
        }
        else if (sendStatus == CorrelatedSendStatus.DefinitelyNotSent)
        {
            ctx.TryReplaceLatestNoWaitOwner(owner, priorNoWaitCommand);
            if (ownsTaskMotion)
                ctx.TryReplaceNoWaitTaskOwner(owner, priorNoWaitTask);   // cb:fold task-domain owner detail; send-failed probe carries outcome
            CircuitTrace.Hit(ctx.Guid, "fire: correlated send failed, owner restored", correlationId);
        }
        else if (sendStatus == CorrelatedSendStatus.OutcomeUnknown)
        {
            CircuitTrace.Hit(ctx.Guid, "fire: send outcome unknown, owner retained", correlationId);
        }
    }

    /// <summary>
    /// Feed an inbound bridge event. Stamps the specific progress clocks
    /// (kill / quest / level) from unsolicited signals, and — if the event type
    /// satisfies the outstanding WAIT — clears the WAIT and stamps generic
    /// progress. Returns true if this event resolved the pending command.
    /// </summary>
    internal static KillCreditKind ClassifyKillCredit(bool killConfirmed, bool isRealKill)
        => !isRealKill ? KillCreditKind.TrashOrGrey
         : killConfirmed ? KillCreditKind.Progress
         : KillCreditKind.Unconfirmed;

    public bool OnEvent(BotContext ctx, BotEvent evt)
    {
        switch (evt.EventType)
        {
            case "KILL":
                CircuitTrace.Hit(ctx.Guid, "event: KILL received", evt.CreatureEntry);
                KillCreditKind credit = ClassifyKillCredit(
                    evt.KillConfirmed, _safety.IsRealKill(evt.CreatureEntry, ctx.Level));
                if (credit == KillCreditKind.Unconfirmed)
                    CircuitTrace.Hit(ctx.Guid, "event: unconfirmed kill, corpse never found", evt.CreatureEntry);
                // Only a REAL kill is progress. A critter/grey kill (e.g. a chicken in a farmyard) must
                // NOT advance LastKillUtc or reset the stall nets — counting it masked the no-kills
                // reselect AND the no-progress breaker (the farmyard-grind-forever bug). Server-side
                // quest kill credit is unaffected (TASK_COMPLETE is authoritative in C++).
                // Stage 1 admits Unconfirmed exactly as before; only the split is new.
                if (credit != KillCreditKind.TrashOrGrey)
                {
                    CircuitTrace.Hit(ctx.Guid, "event: real kill, progress stamped");
                    ctx.LastKillUtc = DateTime.UtcNow;
                    ctx.MarkProgress();
                    ctx.OnGrindProgress();   // a real kill: clear the fail streak + dead-cell history
                    // A no-WAIT coordinator objective has no MOVE_TO arrival ack. A real kill is its
                    // positive proof that this route/field works, so reset the destination streak;
                    // otherwise old failures are not actually consecutive and can quarantine later.
                    var directive = ctx.GroupOrder.Objective;
                    bool creditsDirective = directive.IsActive
                        && (evt.CreatureEntry == directive.CreatureEntry
                            || (directive.Alt1 != 0 && evt.CreatureEntry == directive.Alt1)
                            || (directive.Alt2 != 0 && evt.CreatureEntry == directive.Alt2)
                            || (directive.Alt3 != 0 && evt.CreatureEntry == directive.Alt3));
                    if (creditsDirective
                        && ctx.Held is { Source: ObjectiveSource.Coordinator } groupObjective
                        && groupObjective.QuestId == directive.QuestId)
                    {
                        CircuitTrace.Hit(ctx.Guid, "event: kill credits group objective, no-path streak cleared");
                        ctx.Identity?.ClearNoPathStreak(directive.Map, directive.X, directive.Y);
                    }
                    // Refresh the objective-grind deadline on progress so a slow-but-killing bot is
                    // never false-failed mid-grind (enriched MOVE_TO or SET_TASK {kill_count=N}).
                    if (ctx.Pending is { } objWait && (objWait.CommandType == "SET_TASK" || objWait.IsObjectiveGrind))
                    {
                        CircuitTrace.Hit(ctx.Guid, "event: objective-grind deadline pushed by kill");
                        objWait.DeadlineUtc = DateTime.UtcNow + ObjectiveKillGrace;
                    }
                }
                else
                {
                    CircuitTrace.Hit(ctx.Guid, "event: trash kill ignored (critter/grey)");
                    _logger.LogDebug("[EXEC] {Name} trash kill entry={Entry} (critter/grey) — not progress",
                        ctx.Name, evt.CreatureEntry);
                }
                break;
            case "QUEST_UPDATE":
                CircuitTrace.Hit(ctx.Guid, "event: quest advance stamped");
                ctx.LastQuestAdvanceUtc = DateTime.UtcNow;
                ctx.MarkProgress();
                break;
            case "QUEST_ACCEPT_ACK":
                CircuitTrace.Hit(ctx.Guid, "event: quest accept terminal candidate");
                // No cache seed anymore. ctx.QuestLog is fed exclusively by STATE (the retired pull), so the
                // just-accepted quest appears on the next 5s heartbeat as C++ ground truth. The batch entry is
                // already flipped Accepted=true by QuestPlanner's "accept" step this same tick, so the in-flight
                // accept never depended on the cache. (Trade-off: a quest accepted <5s before a goal bounce can
                // be re-gathered+re-accepted on return until STATE catches up — bounded, and the C++ accept is
                // idempotent, so it's one wasted interact at worst. Strictly better than the old stale-cache class.)
                // Progress is stamped only AFTER type+cbt validation below.
                break;
            case "LEVEL_UP":
                CircuitTrace.Hit(ctx.Guid, "event: level-up stamped");
                ctx.LastLevelUtc = DateTime.UtcNow;
                ctx.MarkProgress();
                ctx.OnGrindProgress();
                break;
            // QUEST_STATUS_ALL is RETIRED. The full quest log now rides on every STATE message and is set onto
            // ctx.QuestLog in BotContext.Sense (the single, tick-thread writer). There is no longer a
            // request/reply cache to overwrite, under-report, empty-wipe, or race — so the old handler (and its
            // empty-payload guard and dropped-held instrumentation) is gone with it. C++ no longer emits this
            // event because nothing sends QUERY_QUEST_STATUS.
            case "TELEPORT_ACK":
                CircuitTrace.Hit(ctx.Guid, "event: teleport terminal candidate");
                // Position is snapped only AFTER type+cbt validation below. A
                // delayed ACK from an older hop must not mutate the current leg.
                break;
            case "MOVE_POINT_REFUSED":
                {
                    // This is unsolicited evidence from a C++-owned candidate hop, never a
                    // terminal command outcome. Accept only the protocol's cbt=0 shape and
                    // return before WAIT matching so even an in-flight command is untouched.
                    if (evt.CorrelationId.GetValueOrDefault() != 0)
                    {
                        CircuitTrace.Hit(ctx.Guid,
                            "event: autonomous move refusal rejected (nonzero cbt)",
                            evt.CorrelationId ?? 0);
                        return false;
                    }

                    if (!TryParseUniquePipe(evt.Data, out var refusal))
                    {
                        CircuitTrace.Hit(ctx.Guid,
                            "event: autonomous move refusal rejected (malformed/duplicate fields)");
                        return false;
                    }
                    if (!refusal.TryGetValue("reason", out string? refusalReason)
                        || !refusalReason.Equals("no_path", StringComparison.OrdinalIgnoreCase)
                        || !refusal.TryGetValue("source", out string? refusalSource)
                        || !refusalSource.Equals("move_point", StringComparison.OrdinalIgnoreCase))
                    {
                        CircuitTrace.Hit(ctx.Guid,
                            "event: autonomous move refusal rejected (reason/source contract)");
                        return false;
                    }

                    if (!refusal.TryGetValue("point_id", out string? pointText)
                        || !int.TryParse(pointText,
                            System.Globalization.NumberStyles.None,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out int pointId)
                        || !IsAutonomousMovePoint(pointId))
                    {
                        CircuitTrace.HitNote(ctx.Guid,
                            "event: autonomous move refusal rejected (invalid point id)",
                            pointText ?? "missing");
                        return false;
                    }

                    if (!refusal.TryGetValue("dest_x", out string? destXText)
                        || !refusal.TryGetValue("dest_y", out string? destYText)
                        || !refusal.TryGetValue("dest_z", out string? destZText)
                        || !TryParseFiniteFloat(destXText, out float destX)
                        || !TryParseFiniteFloat(destYText, out float destY)
                        || !TryParseFiniteFloat(destZText, out float destZ))
                    {
                        CircuitTrace.Hit(ctx.Guid,
                            "event: autonomous move refusal rejected (invalid destination)");
                        return false;
                    }

                    CircuitTrace.Hit(ctx.Guid,
                        "event: autonomous move refusal accepted as transient evidence",
                        pointId);
                    ctx.RecordAutonomousMoveRefusal(
                        pointId,
                        new Vec3(destX, destY, destZ),
                        DateTime.UtcNow);
                    return true;
                }
            case "GRIND_BLOCKED":
                // C++ froze on a grind (over-cap field OR no valid target) for AIBOT_GRIND_FREEZE_DWELL
                // ticks and handed back. There is NO pending MOVE_TO WAIT at grind time (the enriched
                // MOVE_TO already handed off to grind-in-place and its WAIT resolved), so this CANNOT route
                // through TryNegate — set ctx.Failure DIRECTLY and let QuestPlanner.Recover break the freeze
                // with the unstick detour. Carries the center (x|y|z) + reason. C++ currently emits only
                // reason=no_target (the objective-grind overpull_dwell handback was retired 2026-06-30 when
                // C++ began self-unsticking dense fields in place); the parse below stays reason-generic so a
                // future reason flows through untouched — it's the planner that decides what to act on.
                // Not a WAIT ack and not progress. It deliberately does not bump
                // the hard-failure streak; the recovery detour's own WAIT is the
                // bounded backstop (details beside the assignment below).
                {
                    if (ctx.Pending != null)
                    {
                        // Protocol v4 gives this task-owned cbt. Defer to the
                        // exact matcher below: a current MOVE_TO/SET_TASK is
                        // negated promptly; a delayed/wrong-cbt handback cannot
                        // touch the replacement WAIT.
                        CircuitTrace.Hit(ctx.Guid, "event: GRIND_BLOCKED deferred to correlated WAIT matcher");
                        break;
                    }
                    NoWaitCommandOwner? owner = ctx.NoWaitTaskOwner;
                    var ownerDisposition = owner == null
                        ? WaitOutcomeMatcher.Disposition.NotForPending
                        : WaitOutcomeMatcher.Classify(owner, evt);
                    if (ownerDisposition != WaitOutcomeMatcher.Disposition.Negative)
                    {
                        CircuitTrace.Hit(ctx.Guid, "event: no-WAIT GRIND_BLOCKED rejected (owner/cbt mismatch)", evt.CorrelationId ?? 0);
                        return false;
                    }
                    ctx.TryReplaceNoWaitTaskOwner(owner!, null);
                    ctx.TryReplaceLatestNoWaitOwner(owner!, null);
                    CircuitTrace.Hit(ctx.Guid, "event: GRIND_BLOCKED handback -> failure for detour");
                    var gb = ParsePipe(evt.Data);
                    Vec4? dead = null;
                    if (gb.TryGetValue("x", out var gxs) && gb.TryGetValue("y", out var gys))
                    {   // cb:fold parse detail, outcome carried in the failure record
                        float gz = gb.TryGetValue("z", out var gzs) ? ParseF(gzs) : ctx.Pos.Z;
                        dead = new Vec4(ParseF(gxs), ParseF(gys), gz, ctx.MapId);
                    }
                    string grsn = gb.TryGetValue("reason", out var rr) ? rr : "grind_blocked";
                    ctx.Failure = new WaitFailure
                    {
                        CommandType = "GRIND",
                        Reason = grsn,
                        Dest = dead,
                        DangerLevel = 0,
                        QuestId = null,
                        Utc = DateTime.UtcNow
                    };
                    // This handback belongs to an unacked/no-WAIT grind. Never
                    // clear a later, unrelated correlated WAIT if the event was
                    // delayed in the socket.
                    // NB: do NOT bump ConsecutiveFailures. GRIND_BLOCKED is a self-healing handback (C# answers
                    // with the unstick detour), not a hard failure — counting it tripped the wedge-breaker
                    // (cap 8) within ~24s of metronoming, which parked the bot into a Goal.Grinding filler
                    // (entry=0 goal=0) that can't unstick itself, starving the detour. The detour's OWN 60s
                    // WAIT deadline is the real backstop if a kill never lands; the wedge stays a backstop for
                    // genuine no-progress (TimeSinceProgressSec > 150s), which a detouring bot never hits.
                    _logger.LogDebug("[EXEC] {Name} GRIND_BLOCKED ({Reason}) @ {Dead} — unstick detour", ctx.Name, grsn, dead);
                    return true;                // handled; no WAIT-matching needed
                }
        }

        var pending = ctx.Pending;

        if (pending == null
            && (evt.EventType.Equals("POSSESSED_DROP", StringComparison.OrdinalIgnoreCase)
                || evt.EventType.Equals("CONSCRIPTED_DROP", StringComparison.OrdinalIgnoreCase)))
        {
            NoWaitCommandOwner? owner = ctx.LatestNoWaitCommand;
            var ownerDisposition = owner == null
                ? WaitOutcomeMatcher.Disposition.NotForPending
                : WaitOutcomeMatcher.Classify(owner, evt);
            if (ownerDisposition != WaitOutcomeMatcher.Disposition.Negative)
            {
                CircuitTrace.Hit(ctx.Guid, "event: no-WAIT control drop rejected (owner/cbt mismatch)", evt.CorrelationId ?? 0);
                return false;
            }

            ctx.TryReplaceLatestNoWaitOwner(owner!, null);
            if (ReferenceEquals(ctx.NoWaitTaskOwner, owner))
                ctx.TryReplaceNoWaitTaskOwner(owner!, null);   // cb:fold domain-owner cleanup; fence probe carries accepted outcome
            ctx.Failure = null;
            bool possessedDrop = evt.EventType.Equals("POSSESSED_DROP", StringComparison.OrdinalIgnoreCase);
            ctx.ControlFenceReason = possessedDrop ? "possessed" : "conscripted";
            ctx.ControlFenceObservedUtc = DateTime.UtcNow;
            if (possessedDrop) ctx.Possessed = true;   // cb:fold fence kind carried by ControlFenceReason probe
            else ctx.Conscripted = true;   // cb:fold fence kind carried by ControlFenceReason probe
            CircuitTrace.HitNote(ctx.Guid, "event: correlated no-WAIT control fence latched", ctx.ControlFenceReason);
            return true;
        }

        NoWaitCommandOwner? noWaitTaskOwner = ctx.NoWaitTaskOwner;
        WaitOutcomeMatcher.Disposition noWaitDisposition = noWaitTaskOwner == null
            ? WaitOutcomeMatcher.Disposition.NotForPending
            : WaitOutcomeMatcher.Classify(noWaitTaskOwner, evt);

        if (pending == null && noWaitDisposition == WaitOutcomeMatcher.Disposition.Positive)
        {
            ctx.TryReplaceNoWaitTaskOwner(noWaitTaskOwner!, null);
            ctx.TryReplaceLatestNoWaitOwner(noWaitTaskOwner!, null);
            if (ctx.Target is { } noWaitReached && ctx.Identity is { } noWaitMoveId)
            {
                CircuitTrace.Hit(ctx.Guid, "event: no-WAIT arrival clears the no-path streak");
                noWaitMoveId.ClearNoPathStreak(noWaitReached.Map, noWaitReached.X, noWaitReached.Y);
            }
            ctx.Failure = null;
            ctx.MarkProgress();
            ctx.ConsecutiveFailures = 0;
            CircuitTrace.Hit(ctx.Guid, "event: correlated no-WAIT task completed, owner retired");
            return true;
        }

        if (pending == null
            && evt.EventType.Equals("PATH_UNSAFE", StringComparison.OrdinalIgnoreCase))
        {
            if (noWaitDisposition != WaitOutcomeMatcher.Disposition.Negative)
            {
                CircuitTrace.Hit(ctx.Guid, "event: no-WAIT PATH_UNSAFE rejected (owner/cbt mismatch)", evt.CorrelationId ?? 0);
                return false;
            }

            ctx.TryReplaceNoWaitTaskOwner(noWaitTaskOwner!, null);
            ctx.TryReplaceLatestNoWaitOwner(noWaitTaskOwner!, null);
            var unsafeData = ParsePipe(evt.Data);
            Vec4? unsafeDest = null;
            if (unsafeData.TryGetValue("dest_x", out var udx)
                && unsafeData.TryGetValue("dest_y", out var udy))
            {   // cb:fold parse detail, outcome carried in the failure record
                float udz = unsafeData.TryGetValue("dest_z", out var udzs) ? ParseF(udzs) : ctx.Pos.Z;
                unsafeDest = new Vec4(ParseF(udx), ParseF(udy), udz, ctx.MapId);
            }
            int danger = unsafeData.TryGetValue("danger_level", out var dangerText)
                && int.TryParse(dangerText, out int parsedDanger)
                    ? parsedDanger
                    : 0;
            ctx.Failure = new WaitFailure
            {
                CommandType = "MOVE_TO",
                Reason = "path_unsafe",
                Dest = unsafeDest,
                DangerLevel = danger,
                Utc = DateTime.UtcNow
            };
            ctx.ConsecutiveFailures++;
            CircuitTrace.Hit(ctx.Guid, "event: correlated no-WAIT PATH_UNSAFE stamped for recovery", danger);
            return true;
        }

        // Fix 3 (2026-07-04): the durable no_path streak must count EVERY MOVE_FAILED reason=no_path,
        // WAIT or no WAIT. Group objective legs and every reconcile re-issue are fire-and-forget
        // (Dispatch) — no Pending, so TryNegate below never sees their failures, no Failure is
        // stamped, the streak never grows, and the 5-fail group quarantine is structurally blind
        // to the exact loop that needs it most (Oyic, 2026-07-04:
        // 10,033 uncounted no_paths against one coordinate at ~1/s for 10 hours while the waited
        // path's identical rescue saved Xoz in 5 fails). Recorded HERE, unconditionally; the old
        // duplicate recorder inside TryNegate is removed so a waited fail doesn't double-count.
        // When another WAIT is already active, however, a delayed MOVE_FAILED
        // may not poison its destination history unless cbt proves it belongs to
        // that exact MOVE_TO. With no WAIT this remains the fire-and-forget path.
        bool moveFailureBelongsHere = pending == null
            ? noWaitTaskOwner != null
                && WaitOutcomeMatcher.Classify(noWaitTaskOwner, evt) == WaitOutcomeMatcher.Disposition.Negative
            : (pending.CorrelationId > 0
                && evt.CorrelationId == pending.CorrelationId
                && WaitOutcomeMatcher.Classify(pending, evt) == WaitOutcomeMatcher.Disposition.Negative);
        if (evt.EventType == "MOVE_FAILED" && moveFailureBelongsHere && ctx.Identity is { } idNoPath)
        {
            CircuitTrace.Hit(ctx.Guid, "event: MOVE_FAILED durable bookkeeping");
            var mfk = ParsePipe(evt.Data);
            if (mfk.TryGetValue("reason", out var mfr) && mfr == "no_path"
                && mfk.TryGetValue("dest_x", out var mfx) && mfk.TryGetValue("dest_y", out var mfy))
            {
                CircuitTrace.Hit(ctx.Guid, "event: no_path streak recorded");
                idNoPath.RecordNoPath(ctx.MapId, ParseF(mfx), ParseF(mfy));
            }

            // [FINDING_020] Island streak. The core tags a MOVE_FAILED start_isolated=1 when the bot's
            // OWN start cannot path ~20yd in any direction (navmesh island / WMO pocket / harbour water).
            // Post-FINDING_011 such a bot has no move that succeeds, so count consecutive isolated fails
            // from the SAME spot (WAIT or fire-and-forget alike — same reasoning as the no_path streak
            // above) and let BotBrain.TryEscapeIslandAsync port it out. Moving >10yd resets the streak.
            if (mfk.TryGetValue("start_isolated", out var iso) && iso == "1")
            {
                CircuitTrace.Hit(ctx.Guid, "event: start-isolated fail counted");
                float sdx = ctx.Pos.X - idNoPath.IslandStreakX, sdy = ctx.Pos.Y - idNoPath.IslandStreakY;
                if (idNoPath.IslandStreak == 0 || (sdx * sdx + sdy * sdy) > 10f * 10f)
                {
                    CircuitTrace.Hit(ctx.Guid, "event: island streak restarted (new spot)");
                    idNoPath.IslandStreak = 0;
                    idNoPath.IslandStreakX = ctx.Pos.X;
                    idNoPath.IslandStreakY = ctx.Pos.Y;
                }
                idNoPath.IslandStreak++;
            }
            else if (idNoPath.IslandStreak > 0)
            {
                CircuitTrace.Hit(ctx.Guid, "event: island streak cleared (non-isolated fail)");
                // a non-isolated failure means the start CAN path somewhere — not an island
                idNoPath.IslandStreak = 0;
            }
        }

        if (pending == null
            && evt.EventType.Equals("MOVE_FAILED", StringComparison.OrdinalIgnoreCase)
            && moveFailureBelongsHere)
        {
            // MOVE_FAILED is a one-shot terminal outcome in the core. Retire the
            // exact fire owner even when this bot has no durable identity record,
            // so a replay cannot count the same destination/island failure twice.
            ctx.TryReplaceNoWaitTaskOwner(noWaitTaskOwner!, null);
            ctx.TryReplaceLatestNoWaitOwner(noWaitTaskOwner!, null);
            CircuitTrace.Hit(ctx.Guid, "event: correlated no-WAIT MOVE_FAILED owner retired");
            return true;
        }

        if (pending == null) { CircuitTrace.Hit(ctx.Guid, "event: no WAIT outstanding"); return false; }

        var disposition = WaitOutcomeMatcher.Classify(pending, evt);
        if (disposition == WaitOutcomeMatcher.Disposition.NotForPending)
        {
            CircuitTrace.Hit(ctx.Guid, "event: does not match the outstanding WAIT");
            return false;
        }
        if (disposition == WaitOutcomeMatcher.Disposition.CorrelationMismatch)
        {
            CircuitTrace.Hit(ctx.Guid, "event: terminal outcome rejected (cbt mismatch)", evt.CorrelationId ?? 0);
            _logger.LogWarning(
                "[EXEC] {Name} ignored {Event} for {Command}: outcome cbt={EventCbt}, pending cbt={PendingCbt}",
                ctx.Name, evt.EventType, pending.CommandType, evt.CorrelationId, pending.CorrelationId);
            return false;
        }

        // Negative outcome: type identifies a failure for this command and cbt
        // proves it belongs to this exact WAIT. Clear it promptly rather than
        // burning the generous deadline. A failure is not progress.
        if (disposition == WaitOutcomeMatcher.Disposition.Negative)
        {
            bool possessedDrop = evt.EventType.Equals("POSSESSED_DROP", StringComparison.OrdinalIgnoreCase);
            bool conscriptedDrop = evt.EventType.Equals("CONSCRIPTED_DROP", StringComparison.OrdinalIgnoreCase);
            if (possessedDrop || conscriptedDrop)
            {
                // Human/RTS ownership is not a failed route or quest. Release
                // only the exactly-correlated WAIT and briefly stand the planner
                // down; STATE's durable conscripted flag takes over when present.
                if (!ctx.TryClearPending(pending))
                {
                    CircuitTrace.Hit(ctx.Guid, "event: control fence lost exact-WAIT race, replacement preserved");
                    return false;
                }
                ctx.Failure = null;
                ctx.ControlFenceReason = possessedDrop
                    ? "possessed"
                    : "conscripted";
                ctx.ControlFenceObservedUtc = DateTime.UtcNow;
                CircuitTrace.HitNote(ctx.Guid, "event: correlated control fence, WAIT released", ctx.ControlFenceReason);
                if (possessedDrop) ctx.Possessed = true;   // cb:fold fence kind carried by ControlFenceReason probe
                if (conscriptedDrop) ctx.Conscripted = true;   // cb:fold fence kind carried by ControlFenceReason probe
                _logger.LogInformation("[EXEC] {Name} {Fence} rejected {Command}; planner held until fresh STATE",
                    ctx.Name, ctx.ControlFenceReason, pending.CommandType);
                return true;
            }

            if (!Negate(ctx, pending, evt))
                return false;   // cb:fold defensive CAS loss; replacement-preservation probe lives in Negate
            CircuitTrace.Hit(ctx.Guid, "event: WAIT negated by correlated failure event");
            return true;
        }

        // Semantic TASK_COMPLETE guard (premature-arrival fix). A PLAIN travel MOVE_TO only
        // truly completes when the bot is AT the dest. cbt already excludes previous legs; this catches
        // a false success attributed to the current leg. Objective grinds are EXEMPT: their "GRIND finished"
        // TASK_COMPLETE legitimately fires away from the dest (C++ grinds at the mouth/scan hit).
        if (pending.CommandType == "MOVE_TO" && !pending.IsObjectiveGrind
            && string.Equals(evt.EventType, "TASK_COMPLETE", StringComparison.OrdinalIgnoreCase)
            && pending.AgeSec < PrematureArrivalSec
            && ctx.DistToTarget >= 0 && ctx.DistToTarget > ArrivalGateYards)
        {
            CircuitTrace.Hit(ctx.Guid, "event: premature TASK_COMPLETE ignored (stale duplicate)", ctx.DistToTarget);
            _logger.LogDebug("[EXEC] {Name} ignoring premature TASK_COMPLETE — {D:F0}yd out, leg only {A:F1}s old (stale duplicate)",
                ctx.Name, ctx.DistToTarget, pending.AgeSec);
            return false;   // a too-young far arrival is a previous-leg duplicate; wait for the real one
        }

        if (!ctx.TryClearPending(pending))
        {
            CircuitTrace.Hit(ctx.Guid, "event: positive outcome lost exact-WAIT race, replacement preserved");
            return false;
        }

        _logger.LogDebug("[EXEC] {Name} ack {Type} via {Evt}",
            ctx.Name, pending.CommandType, evt.EventType);

        if (pending.CommandType.Equals("SELL_ITEMS", StringComparison.OrdinalIgnoreCase)
            && evt.EventType.Equals("SELL_ACK", StringComparison.OrdinalIgnoreCase))
        {
            var sell = ParsePipe(evt.Data);
            if (sell.TryGetValue("free_slots", out string? freeText)
                && int.TryParse(freeText, out int freeSlots)
                && freeSlots >= 0)
            {
                ctx.FreeSlots = freeSlots;
            }

            bool nothingToSell = sell.TryGetValue("nothing_to_sell", out string? nothingText)
                && nothingText == "1";
            if (ctx.Service is { } vendor)
                vendor.NothingToSell = nothingToSell;

            CircuitTrace.Hit(
                ctx.Guid,
                nothingToSell
                    ? "event: correlated SELL_ACK says bags full with nothing sellable"
                    : "event: correlated SELL_ACK projected free slots",
                ctx.FreeSlots);
        }

        bool combatStillResetAck = pending.CommandType.Equals(
                BotBrain.CombatStillResetCommandType,
                StringComparison.OrdinalIgnoreCase)
            && evt.EventType.Equals(
                BotBrain.CombatStillResetAckEvent,
                StringComparison.OrdinalIgnoreCase);
        if (combatStillResetAck)
        {
            CircuitTrace.Hit(ctx.Guid, "event: correlated combat-reset ACK boundary stamped");
            // This ACK proves only that the core admitted the reset. It is not
            // world progress, and it cannot make a STATE that arrived before
            // the ACK eligible for the post-reset escape gate.
            ctx.CombatStillResetAckReceivedUtc = DateTime.UtcNow;
        }

        if (evt.EventType is "QUEST_ACCEPT_ACK" or "QUEST_COMPLETE_ACK")
        {
            CircuitTrace.Hit(ctx.Guid, "event: correlated quest outcome stamped");
            ctx.LastQuestAdvanceUtc = DateTime.UtcNow;
        }

        if (evt.EventType == "TELEPORT_ACK")
        {
            CircuitTrace.Hit(ctx.Guid, "event: correlated teleport ack, position snapped");
            // Teleport-assist: update Pos immediately so the planner does not
            // wait for the next STATE heartbeat before interacting.
            var tk = ParsePipe(evt.Data);
            if (tk.TryGetValue("x", out var txs) && tk.TryGetValue("y", out var tys))
            {   // cb:fold parse detail, outcome carried by the ack probe
                float tz = tk.TryGetValue("z", out var tzs) ? ParseF(tzs) : ctx.Pos.Z;
                ctx.Pos = new Vec3(ParseF(txs), ParseF(tys), tz);
                if (tk.TryGetValue("map", out var tms) && int.TryParse(tms, out var tmap))
                    ctx.MapId = tmap;   // cb:fold parse detail
            }
        }

        // 2026-06-30: ack-driven, goal-bounce-proof durable completion. A QUEST_COMPLETE_ACK for a
        // QUEST_INTERACT WAIT means the server just rewarded this quest — stamp CompletedQuestIds
        // (+ clear its fail/defer/overflow counters) HERE, synchronous with the ack, BEFORE this tick's
        // TickAsync (and GoalSelector) ever runs. This is the one piece of bookkeeping that cannot be
        // deferred to QuestPlanner's "turnin" step: if a goal bounce (e.g. Training, off the SAME
        // LEVEL_UP this reward granted) wipes ctx.Quest before that step runs, the quest's only durable
        // record of completion is lost and it gets re-offered as brand new — and any future quest gated
        // on it as a prereq stays locked forever. QuestPlanner's own CompletedQuestIds.Add (case
        // "turnin") is now a harmless idempotent duplicate on the happy path — left in place, still
        // needed for the batch-removal/follow-up-drain/log-line it also does.
        if (pending.CommandType == "QUEST_INTERACT"
            && string.Equals(evt.EventType, "QUEST_COMPLETE_ACK", StringComparison.OrdinalIgnoreCase)
            && pending.QuestId is int rewardedId
            && ctx.Identity is { } rid)
        {
            CircuitTrace.Hit(ctx.Guid, "event: quest rewarded, completion stamped durable", rewardedId);
            rid.CompletedQuestIds.Add(rewardedId);
            rid.QuestDeferralCounts.Remove(rewardedId);
            rid.QuestOverflowGrinds.Remove(rewardedId);
            rid.QuestFailStreak.Remove(rewardedId);
            _logger.LogInformation("[EXEC] {Name} quest {Id} rewarded — CompletedQuestIds stamped (ack-driven)",
                ctx.Name, rewardedId);
        }

        if (pending.CommandType == "MOVE_TO" && ctx.Target is { } reached && ctx.Identity is { } moveId)
        {
            CircuitTrace.Hit(ctx.Guid, "event: arrival clears the no-path streak");
            moveId.ClearNoPathStreak(reached.Map, reached.X, reached.Y);
        }

        if (combatStillResetAck)
        {
            CircuitTrace.Hit(ctx.Guid, "event: combat reset ACK admitted, generic progress deferred");
            return true;
        }

        CircuitTrace.HitNote(ctx.Guid, "event: WAIT acked, progress stamped", pending.CommandType);
        ctx.MarkProgress();
        ctx.ConsecutiveFailures = 0;   // a real ack breaks any fail streak
        return true;
    }

    /// <summary>Drop the outstanding WAIT without an ack (Supervisor abandoned the step).</summary>
    public void ClearPending(BotContext ctx) => ctx.Pending = null;

    // ------------------------------------------------------------------------
    // Negative-ack: a failure event that negates the matching WAIT (§3.5b).
    // ------------------------------------------------------------------------
    private bool Negate(BotContext ctx, Outstanding pending, BotEvent evt)
    {
        if (!ctx.TryClearPending(pending))
        {
            CircuitTrace.Hit(ctx.Guid, "negate: exact-WAIT race lost, replacement preserved");
            return false;
        }

        var kv = ParsePipe(evt.Data);
        bool grindBlocked = evt.EventType.Equals("GRIND_BLOCKED", StringComparison.OrdinalIgnoreCase);

        // PATH_UNSAFE carries no reason= field; QUEST_INTERACT_FAIL leads with a bare
        // reason segment (no key); TRAIN_FAIL is a flat fail; MOVE_FAILED uses reason=<code>.
        string reason =
            evt.EventType == "PATH_UNSAFE" ? "path_unsafe"
            : evt.EventType == "TRAIN_FAIL" ? "train_fail"
            : kv.TryGetValue("reason", out var r) ? r
            : FirstBareSegment(evt.Data);

        Vec4? dest = null;
        if (kv.TryGetValue("dest_x", out var dxs) && kv.TryGetValue("dest_y", out var dys))
        {   // cb:fold parse detail, outcome carried in the failure record
            float dz = kv.TryGetValue("dest_z", out var dzs) ? ParseF(dzs) : ctx.Pos.Z;
            dest = new Vec4(ParseF(dxs), ParseF(dys), dz, ctx.MapId);
        }
        else if (grindBlocked && kv.TryGetValue("x", out dxs) && kv.TryGetValue("y", out dys))
        {   // cb:fold GRIND_BLOCKED coordinate normalization is asserted by the integrity test
            float dz = kv.TryGetValue("z", out var dzs) ? ParseF(dzs) : ctx.Pos.Z;
            dest = new Vec4(ParseF(dxs), ParseF(dys), dz, ctx.MapId);
        }

        int danger = kv.TryGetValue("danger_level", out var dls) && int.TryParse(dls, out var dl) ? dl : 0;
        int? qid = kv.TryGetValue("quest_id", out var qs) && int.TryParse(qs, out var q) ? q : null;

        ctx.Failure = new WaitFailure
        {
            // Preserve the established self-healing handback contract: planners
            // recognize GRIND/no_target regardless of whether the owned task was
            // originally adopted through MOVE_TO or SET_TASK.
            CommandType = grindBlocked ? "GRIND" : pending.CommandType,
            Reason = reason,
            Dest = dest,
            DangerLevel = danger,
            QuestId = qid,
            StartIsolated = kv.TryGetValue("start_isolated", out var isoS) && isoS == "1",   // [FINDING_020]
            Utc = DateTime.UtcNow
        };

        CircuitTrace.HitNote(ctx.Guid, "negate: failure stamped, WAIT cleared", reason);
        _logger.LogDebug("[EXEC] {Name} WAIT negated: {Cmd} ← {Evt} reason={Reason}",
            ctx.Name, pending.CommandType, evt.EventType, reason);

        // Durable no_path streak: recorded UPSTREAM in OnEvent for EVERY MOVE_FAILED reason=no_path
        // (Fix 3, 2026-07-04 — WAIT or fire-and-forget alike), so nothing to record here; recording
        // in both places would double-count a waited fail.

        if (!grindBlocked)
            ctx.ConsecutiveFailures++;   // cb:fold ordinary hard-failure streak; GRIND_BLOCKED exemption is asserted by integrity test
        return true;   // NB: no MarkProgress — a failure is not progress.
    }

    // Pipe-delimited key=value parse (the bridge event-data format). Segments with
    // no '=' (e.g. QUEST_INTERACT_FAIL's leading bare reason) are dropped here and
    // recovered via FirstBareSegment.
    private static Dictionary<string, string> ParsePipe(string? data)
        => string.IsNullOrEmpty(data)
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : data.Split('|')
                  .Select(s => s.Split('=', 2))
                  .Where(p => p.Length == 2)
                  .ToDictionary(p => p[0].Trim(), p => p[1].Trim(), StringComparer.OrdinalIgnoreCase);

    // Strict parser for unsolicited telemetry. Duplicate keys are ambiguous and
    // Dictionary.ToDictionary would throw, escaping the per-line JSON catch and
    // recycling the bot socket. Reject them (including casing variants) in-band.
    private static bool TryParseUniquePipe(
        string? data,
        out Dictionary<string, string> values)
    {
        values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(data)) return true;   // cb:fold pure telemetry parser; caller probes admission/rejection

        foreach (string segment in data.Split('|'))
        {
            string[] pair = segment.Split('=', 2);
            if (pair.Length != 2) return false;   // cb:fold pure telemetry parser; caller probes malformed rejection
            string key = pair[0].Trim();
            if (key.Length == 0 || !values.TryAdd(key, pair[1].Trim())) return false;   // cb:fold pure telemetry parser; caller probes duplicate/empty-key rejection
        }

        return true;
    }

    private static string FirstBareSegment(string? data)
    {
        if (string.IsNullOrEmpty(data)) return "";   // cb:fold pure helper
        var first = data.Split('|', 2)[0].Trim();
        return first.Contains('=') ? "" : first;
    }

    private static float ParseF(string s)
        => float.TryParse(s, System.Globalization.NumberStyles.Float,
                          System.Globalization.CultureInfo.InvariantCulture, out var f) ? f : 0f;

    private static bool TryParseFiniteFloat(string text, out float value)
    {
        bool parsed = float.TryParse(
            text,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out value);
        return parsed && float.IsFinite(value);
    }

    // Only short C++-owned hops may use MOVE_POINT_REFUSED. Task destination, taxi,
    // and NPC-interaction points own correlated terminal contracts instead.
    private static bool IsAutonomousMovePoint(int pointId)
        => pointId is 100    // wander
            or 102           // grind patrol
            or 104           // stalemate nudge
            or 105           // overpull flee
            or 106;          // pull retreat

    private static Vec4? ExtractTarget(BridgeCommand cmd)
    {
        if (!cmd.Payload.TryGetValue("x", out var xo) ||
            !cmd.Payload.TryGetValue("y", out var yo) ||
            !cmd.Payload.TryGetValue("z", out var zo))
            return null;   // cb:fold pure helper
        int map = cmd.Payload.TryGetValue("mapId", out var mo) ? ToInt(mo) : 0;
        return new Vec4(ToFloat(xo), ToFloat(yo), ToFloat(zo), map);
    }

    // Pull quest_id off a QUEST_INTERACT payload (both Interact and GroupInteract send it as an
    // anonymous-object int). Null for every other command type, or if the key is somehow absent.
    private static int? ExtractQuestId(BridgeCommand cmd)
    {
        if (cmd.Type != "QUEST_INTERACT") return null;   // cb:fold pure helper
        if (!cmd.Payload.TryGetValue("quest_id", out var qo)) return null;   // cb:fold pure helper
        return qo is IConvertible ? Convert.ToInt32(qo) : (int?)null;
    }

    private static float ToFloat(object o) => o is IConvertible ? Convert.ToSingle(o) : 0f;
    private static int ToInt(object o) => o is IConvertible ? Convert.ToInt32(o) : 0;
}
