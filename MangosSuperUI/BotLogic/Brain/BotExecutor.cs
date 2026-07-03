using MangosSuperUI.BotLogic.Core;
using MangosSuperUI.BotLogic.Data;
using MangosSuperUI.Services;

namespace MangosSuperUI.BotLogic.Brain;

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
// corr (§5.2): corr is written ONLY to the C++ story file for offline merge — it
// never rides a bridge event. So WAIT-matching is by EVENT TYPE, not corr. This
// is sufficient because the spine holds exactly one outstanding command per bot
// (ctx.Pending is singular) → an inbound event has at most one WAIT to satisfy.
// The old corr-stamp / corr-echo machinery was dead code and is gone.
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

    // Premature-arrival guard (npc_not_found fix). Acks match by event TYPE (no corr), so a duplicate /
    // previous-leg TASK_COMPLETE in the pipe can ack a just-issued travel leg early — the bot "arrives"
    // hundreds of yards out and QuestPlanner fires QUEST_INTERACT from the wrong spot. We reject a
    // TASK_COMPLETE only when it is BOTH implausibly young (a leg too new to have walked this far) AND
    // far from dest — that pair is the stale-duplicate signature. We do NOT reject on distance alone:
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
        var now = DateTime.UtcNow;

        // An ENRICHED objective MOVE_TO (§4) carries creature_entry/kill_count: C++ travels
        // then grinds in place under this one WAIT. Flag it so the KILL-push in OnEvent rolls
        // its deadline forward once the grind starts (the travel deadline only covers travel).
        bool objectiveGrind = cmd.Type == "MOVE_TO" && cmd.Payload.ContainsKey("creature_entry");

        // 2026-06-30: cache the quest id off a QUEST_INTERACT's payload (accept OR complete) so
        // the ack handler can stamp durable bookkeeping synchronously, goal-bounce-proof. Mirrors
        // moveTgt below for MOVE_TO.
        int? questId = ExtractQuestId(cmd);

        ctx.Pending = new Outstanding
        {
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

        // Capture the destination so FleetReport can show distance-to-target.
        Vec4? moveTgt = null;
        if (cmd.Type == "MOVE_TO")
        {
            moveTgt = ExtractTarget(cmd);
            if (moveTgt != null) ctx.Target = moveTgt;
        }

        // Instrumentation: a travel MOVE_TO logs dest + bot pos + distance; a QUEST_INTERACT logs the
        // npc_entry + bot pos. A premature/stale arrival ack (interact fired far from the NPC) is then a
        // one-line grep: "issue MOVE_TO -> (X,Y)" followed by "issue QUEST_INTERACT npc=N from (px,py)"
        // with (px,py) nowhere near (X,Y). Everything else logs exactly as before.
        if (cmd.Type == "MOVE_TO" && moveTgt is { } mt)
            _logger.LogDebug("[EXEC] {Name} issue MOVE_TO -> ({X:F0},{Y:F0}) from ({PX:F0},{PY:F0}) d={D:F0} expect={Expect} deadline={Sec}s",
                ctx.Name, mt.X, mt.Y, ctx.Pos.X, ctx.Pos.Y, ctx.DistToTarget, expectedEvent, deadline.TotalSeconds);
        else if (cmd.Type == "QUEST_INTERACT")
            _logger.LogDebug("[EXEC] {Name} issue QUEST_INTERACT npc={Npc} from ({PX:F0},{PY:F0}) expect={Expect} deadline={Sec}s",
                ctx.Name, cmd.Payload.TryGetValue("npc_entry", out var ne) ? ne : "?", ctx.Pos.X, ctx.Pos.Y, expectedEvent, deadline.TotalSeconds);
        else
            _logger.LogDebug("[EXEC] {Name} issue {Type} expect={Expect} deadline={Sec}s",
                ctx.Name, cmd.Type, expectedEvent, deadline.TotalSeconds);

        await _bridge.SendToBotAsync(ctx.Guid, cmd.Type, cmd.Payload);
    }

    /// <summary>
    /// Issue a no-WAIT command: send it and arm NO Pending. For indefinite,
    /// unacked tasks (SET_TASK GRIND kill_count=0, SET_TASK IDLE) whose liveness is
    /// owned by the planner's IsProgressing, not a one-shot ack. Nothing for the
    /// Supervisor's deadline rule to expire (§6.3).
    /// </summary>
    public async Task IssueNoWaitAsync(BotContext ctx, BridgeCommand cmd)
    {
        // Parity with IssueAsync: keep distance-to-target live for MOVE_TO fires.
        if (cmd.Type == "MOVE_TO")
        {
            var tgt = ExtractTarget(cmd);
            if (tgt != null) ctx.Target = tgt;
        }

        _logger.LogDebug("[EXEC] {Name} fire {Type} (no-wait)", ctx.Name, cmd.Type);

        await _bridge.SendToBotAsync(ctx.Guid, cmd.Type, cmd.Payload);
    }

    /// <summary>
    /// Feed an inbound bridge event. Stamps the specific progress clocks
    /// (kill / quest / level) from unsolicited signals, and — if the event type
    /// satisfies the outstanding WAIT — clears the WAIT and stamps generic
    /// progress. Returns true if this event resolved the pending command.
    /// </summary>
    public bool OnEvent(BotContext ctx, BotEvent evt)
    {
        switch (evt.EventType)
        {
            case "KILL":
                // Only a REAL kill is progress. A critter/grey kill (e.g. a chicken in a farmyard) must
                // NOT advance LastKillUtc or reset the stall nets — counting it masked the no-kills
                // reselect AND the no-progress breaker (the farmyard-grind-forever bug). Server-side
                // quest kill credit is unaffected (TASK_COMPLETE is authoritative in C++).
                if (_safety.IsRealKill(evt.CreatureEntry, ctx.Level))
                {
                    ctx.LastKillUtc = DateTime.UtcNow;
                    ctx.MarkProgress();
                    ctx.OnGrindProgress();   // a real kill: clear the fail streak + dead-cell history
                    // Refresh the objective-grind deadline on progress so a slow-but-killing bot is
                    // never false-failed mid-grind (enriched MOVE_TO or SET_TASK {kill_count=N}).
                    if (ctx.Pending is { } objWait && (objWait.CommandType == "SET_TASK" || objWait.IsObjectiveGrind))
                        objWait.DeadlineUtc = DateTime.UtcNow + ObjectiveKillGrace;
                }
                else
                {
                    _logger.LogDebug("[EXEC] {Name} trash kill entry={Entry} (critter/grey) — not progress",
                        ctx.Name, evt.CreatureEntry);
                }
                break;
            case "QUEST_UPDATE":
            case "QUEST_COMPLETE_ACK":
                ctx.LastQuestAdvanceUtc = DateTime.UtcNow;
                ctx.MarkProgress();
                break;
            case "QUEST_ACCEPT_ACK":
                ctx.LastQuestAdvanceUtc = DateTime.UtcNow;
                ctx.MarkProgress();
                // No cache seed anymore. ctx.QuestLog is fed exclusively by STATE (the retired pull), so the
                // just-accepted quest appears on the next 5s heartbeat as C++ ground truth. The batch entry is
                // already flipped Accepted=true by QuestPlanner's "accept" step this same tick, so the in-flight
                // accept never depended on the cache. (Trade-off: a quest accepted <5s before a goal bounce can
                // be re-gathered+re-accepted on return until STATE catches up — bounded, and the C++ accept is
                // idempotent, so it's one wasted interact at worst. Strictly better than the old stale-cache class.)
                break;
            case "LEVEL_UP":
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
                // Teleport-assist: the bot was relocated (NearTeleportTo). Update Pos from the ack
                // payload (x|y|z|map) IMMEDIATELY so the planner sees DistToTarget≈0 and fires the
                // interaction THIS cycle — the 5 s STATE cadence would otherwise lag the new pos and
                // the planner would re-issue a MOVE_TO from the stale position. Not progress-stamped
                // here; the generic positive-ack path below clears the TELEPORT_TO WAIT + MarkProgress.
                {
                    var tk = ParsePipe(evt.Data);
                    if (tk.TryGetValue("x", out var txs) && tk.TryGetValue("y", out var tys))
                    {
                        float tz = tk.TryGetValue("z", out var tzs) ? ParseF(tzs) : ctx.Pos.Z;
                        ctx.Pos = new Vec3(ParseF(txs), ParseF(tys), tz);
                        if (tk.TryGetValue("map", out var tms) && int.TryParse(tms, out var tmap))
                            ctx.MapId = tmap;
                    }
                }
                break;
            case "GRIND_BLOCKED":
                // C++ froze on a grind (over-cap field OR no valid target) for AIBOT_GRIND_FREEZE_DWELL
                // ticks and handed back. There is NO pending MOVE_TO WAIT at grind time (the enriched
                // MOVE_TO already handed off to grind-in-place and its WAIT resolved), so this CANNOT route
                // through TryNegate — set ctx.Failure DIRECTLY and let QuestPlanner.Recover break the freeze
                // with the unstick detour. Carries the center (x|y|z) + reason. C++ currently emits only
                // reason=no_target (the objective-grind overpull_dwell handback was retired 2026-06-30 when
                // C++ began self-unsticking dense fields in place); the parse below stays reason-generic so a
                // future reason flows through untouched — it's the planner that decides what to act on.
                // Not a WAIT ack and not progress; bump the fail streak so the wedge breaker stays a backstop
                // (a successful detour's TASK_COMPLETE resets it).
                {
                    var gb = ParsePipe(evt.Data);
                    Vec4? dead = null;
                    if (gb.TryGetValue("x", out var gxs) && gb.TryGetValue("y", out var gys))
                    {
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
                    ctx.Pending = null;            // drop any stale grind WAIT so the planner re-derives cleanly
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
        if (pending == null) return false;

        // Negative outcome FIRST: a failure event that NEGATES the matching WAIT
        // (the no_path-plateau fix). MOVE_FAILED/PATH_UNSAFE negate a MOVE_TO WAIT;
        // QUEST_INTERACT_FAIL negates a QUEST_INTERACT WAIT. Clears the WAIT + stamps
        // ctx.Failure so the planner escapes now, not after the (generous) deadline.
        // No MarkProgress — a failure is not progress.
        if (TryNegate(ctx, pending, evt)) return true;

        // Positive ack: match purely by event type (corr is story-file-only — §5.2).
        // One outstanding command per bot, so at most one WAIT to satisfy.
        bool typeMatch = !string.IsNullOrEmpty(pending.ExpectedEvent)
                         && string.Equals(evt.EventType, pending.ExpectedEvent, StringComparison.OrdinalIgnoreCase);
        if (!typeMatch) return false;

        // Stale/duplicate TASK_COMPLETE guard (premature-arrival fix). A PLAIN travel MOVE_TO only
        // truly completes when the bot is AT the dest. Because acks match by type only (no corr), a
        // duplicate or previous-leg TASK_COMPLETE in the pipe would otherwise ack a just-issued travel
        // leg early — the bot "arrives" hundreds of yards out and the QuestPlanner fires QUEST_INTERACT
        // from the wrong spot (npc_not_found). Objective grinds are EXEMPT: their "GRIND finished"
        // TASK_COMPLETE legitimately fires away from the dest (C++ grinds at the mouth/scan hit).
        if (pending.CommandType == "MOVE_TO" && !pending.IsObjectiveGrind
            && string.Equals(evt.EventType, "TASK_COMPLETE", StringComparison.OrdinalIgnoreCase)
            && pending.AgeSec < PrematureArrivalSec
            && ctx.DistToTarget >= 0 && ctx.DistToTarget > ArrivalGateYards)
        {
            _logger.LogDebug("[EXEC] {Name} ignoring premature TASK_COMPLETE — {D:F0}yd out, leg only {A:F1}s old (stale duplicate)",
                ctx.Name, ctx.DistToTarget, pending.AgeSec);
            return false;   // a too-young far arrival is a previous-leg duplicate; wait for the real one
        }

        _logger.LogDebug("[EXEC] {Name} ack {Type} via {Evt}",
            ctx.Name, pending.CommandType, evt.EventType);

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
            rid.CompletedQuestIds.Add(rewardedId);
            rid.QuestDeferralCounts.Remove(rewardedId);
            rid.QuestOverflowGrinds.Remove(rewardedId);
            rid.QuestFailStreak.Remove(rewardedId);
            _logger.LogInformation("[EXEC] {Name} quest {Id} rewarded — CompletedQuestIds stamped (ack-driven)",
                ctx.Name, rewardedId);
        }

        ctx.Pending = null;
        ctx.MarkProgress();
        ctx.ConsecutiveFailures = 0;   // a real ack breaks any fail streak
        return true;
    }

    /// <summary>Drop the outstanding WAIT without an ack (Supervisor abandoned the step).</summary>
    public void ClearPending(BotContext ctx) => ctx.Pending = null;

    // ------------------------------------------------------------------------
    // Negative-ack: a failure event that negates the matching WAIT (§3.5b).
    // ------------------------------------------------------------------------
    private bool TryNegate(BotContext ctx, Outstanding pending, BotEvent evt)
    {
        bool moveFail = (evt.EventType == "MOVE_FAILED" || evt.EventType == "PATH_UNSAFE")
                        && pending.CommandType == "MOVE_TO";
        bool interactFail = evt.EventType == "QUEST_INTERACT_FAIL"
                            && pending.CommandType == "QUEST_INTERACT";
        bool trainFail = evt.EventType == "TRAIN_FAIL"
                            && pending.CommandType == "TRAIN_AT_NPC";
        // Vendor errand fails: SELL_FAIL/REPAIR_FAIL negate the SELL_ITEMS/REPAIR_AT_NPC WAIT
        // instead of burning the full 30s SELL_ACK/REPAIR_ACK deadline. The cases are
        // vendor_not_found / npc_not_found (the chosen NPC isn't in the world — a runtime
        // despawn / pool rotation that slipped past ZoneDataLoader's load-time event-gate
        // filter) and not_enough_gold (the bot is broke). Both carry reason=<code>, so the
        // reason flows through the kv.TryGetValue("reason") branch below unchanged; the
        // MaintenancePlanner decides phantom-giveup vs finish from failure.Reason.
        bool sellFail = evt.EventType == "SELL_FAIL"
                            && pending.CommandType == "SELL_ITEMS";
        bool repairFail = evt.EventType == "REPAIR_FAIL"
                            && pending.CommandType == "REPAIR_AT_NPC";
        // Teleport-assist: TELEPORT_FAIL (bad_payload / dead / cross_map / too_far) negates the
        // TELEPORT_TO WAIT so the planner abandons the hop (Outbound) / completes anyway (Inbound)
        // immediately instead of burning the 10 s TELEPORT_ACK deadline. Carries reason=<code>.
        bool teleportFail = evt.EventType == "TELEPORT_FAIL"
                            && pending.CommandType == "TELEPORT_TO";
        if (!moveFail && !interactFail && !trainFail && !sellFail && !repairFail && !teleportFail) return false;

        var kv = ParsePipe(evt.Data);

        // PATH_UNSAFE carries no reason= field; QUEST_INTERACT_FAIL leads with a bare
        // reason segment (no key); TRAIN_FAIL is a flat fail; MOVE_FAILED uses reason=<code>.
        string reason =
            evt.EventType == "PATH_UNSAFE" ? "path_unsafe"
            : evt.EventType == "TRAIN_FAIL" ? "train_fail"
            : kv.TryGetValue("reason", out var r) ? r
            : FirstBareSegment(evt.Data);

        Vec4? dest = null;
        if (kv.TryGetValue("dest_x", out var dxs) && kv.TryGetValue("dest_y", out var dys))
        {
            float dz = kv.TryGetValue("dest_z", out var dzs) ? ParseF(dzs) : ctx.Pos.Z;
            dest = new Vec4(ParseF(dxs), ParseF(dys), dz, ctx.MapId);
        }

        int danger = kv.TryGetValue("danger_level", out var dls) && int.TryParse(dls, out var dl) ? dl : 0;
        int? qid = kv.TryGetValue("quest_id", out var qs) && int.TryParse(qs, out var q) ? q : null;

        ctx.Failure = new WaitFailure
        {
            CommandType = pending.CommandType,
            Reason = reason,
            Dest = dest,
            DangerLevel = danger,
            QuestId = qid,
            Utc = DateTime.UtcNow
        };

        _logger.LogDebug("[EXEC] {Name} WAIT negated: {Cmd} ← {Evt} reason={Reason}",
            ctx.Name, pending.CommandType, evt.EventType, reason);

        ctx.Pending = null;   // NB: no MarkProgress — a failure is not progress.
        ctx.ConsecutiveFailures++;   // feeds the brain's fast fail-loop breaker
        return true;
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

    private static string FirstBareSegment(string? data)
    {
        if (string.IsNullOrEmpty(data)) return "";
        var first = data.Split('|', 2)[0].Trim();
        return first.Contains('=') ? "" : first;
    }

    private static float ParseF(string s)
        => float.TryParse(s, System.Globalization.NumberStyles.Float,
                          System.Globalization.CultureInfo.InvariantCulture, out var f) ? f : 0f;

    private static Vec4? ExtractTarget(BridgeCommand cmd)
    {
        if (!cmd.Payload.TryGetValue("x", out var xo) ||
            !cmd.Payload.TryGetValue("y", out var yo) ||
            !cmd.Payload.TryGetValue("z", out var zo))
            return null;
        int map = cmd.Payload.TryGetValue("mapId", out var mo) ? ToInt(mo) : 0;
        return new Vec4(ToFloat(xo), ToFloat(yo), ToFloat(zo), map);
    }

    // Pull quest_id off a QUEST_INTERACT payload (both Interact and GroupInteract send it as an
    // anonymous-object int). Null for every other command type, or if the key is somehow absent.
    private static int? ExtractQuestId(BridgeCommand cmd)
    {
        if (cmd.Type != "QUEST_INTERACT") return null;
        if (!cmd.Payload.TryGetValue("quest_id", out var qo)) return null;
        return qo is IConvertible ? Convert.ToInt32(qo) : (int?)null;
    }

    private static float ToFloat(object o) => o is IConvertible ? Convert.ToSingle(o) : 0f;
    private static int ToInt(object o) => o is IConvertible ? Convert.ToInt32(o) : 0;
}