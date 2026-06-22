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

    // Premature-arrival guard (npc_not_found fix). A PLAIN travel MOVE_TO's TASK_COMPLETE is accepted
    // as an arrival only when the bot is within this of the dest. Acks match by event TYPE (no corr),
    // so a duplicate / previous-leg TASK_COMPLETE in the pipe would otherwise ack a just-issued travel
    // leg early — the bot "arrives" hundreds of yards out and QuestPlanner fires QUEST_INTERACT from the
    // wrong spot. Comfortably above the C++ 5yd arrival threshold, well under any real miss. Objective
    // grinds are EXEMPT (IsObjectiveGrind): their "GRIND finished" legitimately completes away from dest.
    private const float ArrivalGateYards = 20f;

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

        ctx.Pending = new Outstanding
        {
            CommandType = cmd.Type,
            ExpectedEvent = expectedEvent,
            SentUtc = now,
            DeadlineUtc = now + deadline,
            IsObjectiveGrind = objectiveGrind
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
            case "QUEST_ACCEPT_ACK":
            case "QUEST_COMPLETE_ACK":
                ctx.LastQuestAdvanceUtc = DateTime.UtcNow;
                ctx.MarkProgress();
                break;
            case "LEVEL_UP":
                ctx.LastLevelUtc = DateTime.UtcNow;
                ctx.MarkProgress();
                ctx.OnGrindProgress();
                break;
            case "QUEST_STATUS_ALL":
                // Authoritative quest-log snapshot (reply to QUERY_QUEST_STATUS). Not a
                // WAIT ack and not progress — just refresh the cache the QuestPlanner reads
                // to resume an in-log quest. Ref-swapped so a concurrent planner read is safe.
                ctx.QuestLog = ParseQuestLog(evt.Data);
                ctx.QuestLogStampUtc = DateTime.UtcNow;
                break;
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
            && ctx.DistToTarget >= 0 && ctx.DistToTarget > ArrivalGateYards)
        {
            _logger.LogDebug("[EXEC] {Name} ignoring stale TASK_COMPLETE — {D:F0}yd from dest, not arrived",
                ctx.Name, ctx.DistToTarget);
            return false;   // keep waiting for the real arrival
        }

        _logger.LogDebug("[EXEC] {Name} ack {Type} via {Evt}",
            ctx.Name, pending.CommandType, evt.EventType);

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
        if (!moveFail && !interactFail && !trainFail && !sellFail && !repairFail) return false;

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
    // QUEST_STATUS_ALL payload (C++ BridgeHandleQueryQuestStatus):
    //   questId:status:mob0,mob1,mob2,mob3:item0,item1,item2,item3 | questId:...
    // status: COMPLETE=1, INCOMPLETE=3 (VMaNGOS enum — counterintuitive). Empty payload = no active quests. Builds a
    // fresh dictionary and returns it (caller ref-swaps ctx.QuestLog atomically).
    private static Dictionary<int, QuestLogEntry> ParseQuestLog(string? data)
    {
        var log = new Dictionary<int, QuestLogEntry>();
        if (string.IsNullOrWhiteSpace(data)) return log;

        foreach (var part in data.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            var f = part.Split(':');
            if (f.Length < 2) continue;
            if (!int.TryParse(f[0].Trim(), out int qid)) continue;
            if (!int.TryParse(f[1].Trim(), out int status)) continue;

            var mob = new int[4];
            if (f.Length >= 3)
            {
                var mc = f[2].Split(',');
                for (int i = 0; i < 4 && i < mc.Length; i++)
                    int.TryParse(mc[i].Trim(), out mob[i]);
            }

            var item = new int[4];
            if (f.Length >= 4)
            {
                var ic = f[3].Split(',');
                for (int i = 0; i < 4 && i < ic.Length; i++)
                    int.TryParse(ic[i].Trim(), out item[i]);
            }

            log[qid] = new QuestLogEntry { Status = status, MobCounts = mob, ItemCounts = item };
        }
        return log;
    }

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

    private static float ToFloat(object o) => o is IConvertible ? Convert.ToSingle(o) : 0f;
    private static int ToInt(object o) => o is IConvertible ? Convert.ToInt32(o) : 0;
}