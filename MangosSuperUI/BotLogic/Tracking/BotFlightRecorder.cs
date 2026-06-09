using Dapper;
using MangosSuperUI.BotLogic.Core;
using MangosSuperUI.Models;
using MangosSuperUI.Services;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;

namespace MangosSuperUI.BotLogic.Tracking;

// ════════════════════════════════════════════════════════════════════════════
// BotFlightRecorder — the "black box" for the AiBot fleet
// ════════════════════════════════════════════════════════════════════════════
//
// PURPOSE
//   Reconstruct a single bot's full life cycle as a coherent, ordered story so we
//   can answer, for any stall: WHO owns the logic loop right now, WHAT is the bot
//   waiting on, HOW does it break free, and DID the notification ever arrive.
//
//   This complements BotFleetDiagnostics. Diagnostics is the ISSUE LEDGER (a list
//   of things that went wrong). This is the TIMELINE (everything that happened,
//   in order, with ownership made explicit).
//
// THE CORE IDEA — ownership as a logged, first-class thing
//   At any instant exactly one party is responsible for the bot's next move:
//     CS    — C# is about to act; the next tick will send a command.
//     CPP   — C++ is autonomously executing (grind/combat/move/eat/flight) and
//             will emit an event when it finishes.
//     WAIT  — C# sent a command and is blocked awaiting an ack/event from C++.
//     TIMER — gated by a C# timer (rez delay, vendor cooldown, GO throttle, eval).
//     GROUP — blocked on a group sync gate, waiting on PEERS (Session 35).
//   "Stuck" is then trivial: owner + waitingOn unchanged for too long. The sweep
//   below detects that automatically, even for code paths nobody instrumented.
//
// TOGGLE — never permanent, near-zero cost when off
//   Every emit method early-returns unless the bot's guid is in the per-guid
//   allowlist. The instrumentation calls can therefore live in the code forever;
//   only their OUTPUT is toggled (allowlist + global on/off persisted in
//   bot_settings). Run "group of 3 + group of 2", allowlist those 5 guids, run
//   1–2 hours, then read the merged timeline.
//
// OUTPUT — merged, not per-bot
//   One file per day: /opt/mangossuperui/diagnostics/trace/trace_{date}.jsonl
//   The Session 34/35 failure is a CROSS-bot interaction (pace-setter vs
//   followers); you can only see the dance in a single interleaved timeline.
//   Per-bot reading is a read-time filter on the `guid` field (grep / jq).
//
// WIRING
//   - Registered as a DI singleton (Program.cs), like BotFleetDiagnostics.
//   - BotBrainService calls BotTrace.Attach(this recorder) once at startup so the
//     static facade (BotTrace.*) can be called from any domain WITHOUT threading a
//     constructor ref through all 7 domains + the engine + both services.
//   - BotBrainService calls TickTrace(...) from its ~250ms main loop (flush + sweep).
//
// ════════════════════════════════════════════════════════════════════════════

/// <summary>Who holds the baton — the party responsible for the bot's next move.</summary>
public static class TraceOwner
{
    public const string CS = "CS";        // C# about to act
    public const string CPP = "CPP";      // C++ executing autonomously
    public const string WAIT = "WAIT";    // C# blocked awaiting a C++ ack/event
    public const string TIMER = "TIMER";  // gated by a C# timer
    public const string GROUP = "GROUP";  // blocked on a group sync gate (peers)
}

/// <summary>Record kinds — the shape of one line in the timeline.</summary>
public static class TraceKind
{
    public const string Transition = "TRANSITION"; // sub-phase / activity change
    public const string Decision = "DECISION";     // strategic eval result (weights + winner)
    public const string Command = "COMMAND";       // C# sent a bridge command
    public const string Event = "EVENT";           // a bridge event arrived
    public const string State = "STATE";           // 5s STATE reconcile / heartbeat
    public const string Stuck = "STUCK";           // owner+waitingOn frozen too long
    public const string Wait = "WAIT_ON";          // blocking on a dependency (gate/timer) without changing phase
    public const string Timeout = "TIMEOUT";       // an expected event never arrived
    public const string Mark = "MARK";             // arbitrary annotation
    public const string GroupLifecycle = "GROUP_LIFECYCLE"; // form / disband / remove / promote (fleet-level, not per-bot)
}

/// <summary>
/// One line in the timeline. Serialized as compact camelCase JSON (one per line).
/// Short field names keep the file readable when grepped/jq'd.
/// </summary>
public class TraceRecord
{
    public string Ts { get; set; } = "";
    public long Seq { get; set; }            // monotonic per-bot — detects gaps/reordering
    public int Guid { get; set; }
    public string Name { get; set; } = "";
    public int Lvl { get; set; }
    public int? Grp { get; set; }            // group id, null if solo
    public string Role { get; set; } = "";   // leader | follower | solo

    public string Owner { get; set; } = "";  // TraceOwner.*
    public string Act { get; set; } = "";    // current activity
    public string? Sub { get; set; }         // current sub-phase

    public string Kind { get; set; } = "";   // TraceKind.*
    public string? From { get; set; }
    public string? To { get; set; }
    public string? Cause { get; set; }       // what triggered this (tick/strategic/event:X/ack:Y/timeout/fallback)
    public string? Wait { get; set; }        // the blocking dependency (see WaitOn.* conventions)

    public int? Corr { get; set; }           // correlation id (command<->event stitch)
    public long? LatMs { get; set; }         // round-trip latency on a matched EVENT

    public string Detail { get; set; } = "";
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public int Map { get; set; }

    // Group sync gates (Session 35) — only meaningful when grouped. The follower
    // stall signals. Null when solo.
    public bool? GObj { get; set; }          // GroupAllObjectivesDone
    public bool? GTurn { get; set; }         // GroupAllMembersTurnedIn
    public bool? GQuest { get; set; }        // GroupAllMembersQuesting

    // C++ self-report block (populated on STATE once Batch 4 enriches the C++ side).
    // Left null until then; the reconcile logic no-ops gracefully when absent.
    public string? CppState { get; set; }    // IDLE/GRINDING/MOVING/IN_COMBAT/DEAD/GHOST/FLIGHT/EATING
    public bool? Mismatch { get; set; }      // true when C#-believed owner disagrees with C++ self-report
}

/// <summary>Conventional values for the `Wait` field, so Batches 2–4 stay consistent.</summary>
public static class WaitOn
{
    // WAIT (awaiting a C++ event) — prefix evt:
    public static string Event(string evtType) => $"evt:{evtType}";
    // TIMER — prefix timer:
    public const string StrategicEval = "timer:strategic_eval";
    public const string RezAt = "timer:rez_at";
    public const string VendorCooldown = "timer:vendor_cooldown";
    public const string GoThrottle = "timer:go_throttle";
    // GROUP gates (Session 35) — prefix group:
    public const string GroupObjectives = "group:all_objectives_done";
    public const string GroupTurnedIn = "group:all_turned_in";
    public const string GroupQuesting = "group:all_questing";
    public const string LeaderQuests = "group:leader_quests";
    // CPP autonomous — prefix cpp:
    public static string Cpp(string what) => $"cpp:{what}";
}

// ════════════════════════════════════════════════════════════════════════════

public class BotFlightRecorder
{
    private readonly ILogger<BotFlightRecorder> _logger;
    private readonly ConnectionFactory _db;

    // --- Toggle state (persisted in bot_settings) ---
    private volatile bool _enabled;
    private readonly HashSet<int> _targets = new();
    private readonly object _targetsLock = new();

    // --- Per-bot trace state (the recorder owns this; domains stay clean) ---
    private readonly ConcurrentDictionary<int, TraceState> _state = new();

    // --- Write buffer (FIFO so the file stays in emit order) ---
    private readonly ConcurrentQueue<TraceRecord> _pending = new();

    // --- Flush / sweep cadence ---
    private DateTime _lastFlush = DateTime.UtcNow;
    private const int FLUSH_INTERVAL_SECONDS = 30;
    private const int MAX_BUFFER_BEFORE_FORCE_FLUSH = 2000;

    // --- Auto-stuck thresholds (seconds with no owner/wait change) ---
    private const int STUCK_WAIT_SEC = 60;     // C# blocked on a C++ event this long = suspicious
    private const int STUCK_GROUP_SEC = 180;   // blocked on a peer gate
    private const int STUCK_TIMER_SEC = 90;    // a timer that should have fired
    private const int STUCK_CPP_SEC = 300;     // grinding/combat can be long; flag only if very long
    private const int STUCK_CS_SEC = 45;       // CS "about to act" but never acting

    // --- STATE record throttle (don't write 18 identical lines for a stuck bot) ---
    private const int STATE_HEARTBEAT_SEC = 30;

    private const string TRACE_DIR = "/opt/mangossuperui/diagnostics/trace";

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public BotFlightRecorder(ILogger<BotFlightRecorder> logger, ConnectionFactory db)
    {
        _logger = logger;
        _db = db;
        try { Directory.CreateDirectory(TRACE_DIR); }
        catch (Exception ex) { _logger.LogWarning(ex, "BotFlightRecorder: could not create {Dir}", TRACE_DIR); }
    }

    public bool Enabled => _enabled;

    public IReadOnlyCollection<int> Targets
    {
        get { lock (_targetsLock) return _targets.ToArray(); }
    }

    /// <summary>Cheap gate every emit method calls first.</summary>
    public bool IsTraced(int guid)
    {
        if (!_enabled) return false;
        lock (_targetsLock) return _targets.Contains(guid);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Control surface (BotBrainService / BotsController call these)
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Set the per-guid allowlist and global on/off. Persists to bot_settings and
    /// resets per-bot trace state so the next run starts clean.
    /// </summary>
    public async Task SetTargetsAsync(IEnumerable<int> guids, bool enabled)
    {
        lock (_targetsLock)
        {
            _targets.Clear();
            foreach (var g in guids) _targets.Add(g);
        }
        _enabled = enabled;

        ResetSession();
        await PersistSettingsAsync();
        _logger.LogInformation("BotFlightRecorder: enabled={Enabled}, targets=[{Guids}]",
            enabled, string.Join(",", Targets));
    }

    /// <summary>Wipe per-bot timeline state + buffered records (clean slate for a fresh run).</summary>
    public void ResetSession()
    {
        FlushToDisk();         // don't lose the previous run's tail
        _state.Clear();
        while (_pending.TryDequeue(out _)) { }
    }

    /// <summary>Load toggle + allowlist from bot_settings at startup.</summary>
    public async Task LoadSettingsAsync()
    {
        try
        {
            using var conn = _db.Admin();
            var rows = await conn.QueryAsync<dynamic>(
                "SELECT setting_key, setting_value FROM bot_settings WHERE setting_key IN ('trace:enabled','trace:guids')");

            foreach (var row in rows)
            {
                string key = (string)row.setting_key;
                string val = (string)row.setting_value;

                if (key == "trace:enabled")
                {
                    _enabled = val.Equals("true", StringComparison.OrdinalIgnoreCase);
                }
                else if (key == "trace:guids" && !string.IsNullOrWhiteSpace(val))
                {
                    lock (_targetsLock)
                    {
                        _targets.Clear();
                        foreach (var part in val.Split(',', StringSplitOptions.RemoveEmptyEntries))
                            if (int.TryParse(part.Trim(), out var g)) _targets.Add(g);
                    }
                }
            }

            _logger.LogInformation("BotFlightRecorder: loaded settings enabled={Enabled}, targets=[{Guids}]",
                _enabled, string.Join(",", Targets));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "BotFlightRecorder: failed to load settings (defaulting off)");
        }
    }

    private async Task PersistSettingsAsync()
    {
        try
        {
            using var conn = _db.Admin();
            await conn.ExecuteAsync(@"
                INSERT INTO bot_settings (setting_key, setting_value) VALUES
                    ('trace:enabled', @En), ('trace:guids', @Guids)
                ON DUPLICATE KEY UPDATE setting_value = VALUES(setting_value)",
                new { En = _enabled ? "true" : "false", Guids = string.Join(",", Targets) });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "BotFlightRecorder: failed to persist settings");
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // Emit API (Batches 2–4 call these, normally via the BotTrace facade)
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>A sub-phase or activity change. Default owner = CS (C# just decided).</summary>
    public void Transition(BotIdentity bot, string from, string to, string cause,
        string? waitingOn = null, string detail = "", BotStateSnapshot? state = null)
    {
        if (!IsTraced(bot.Guid)) return;
        var ts = _state.GetOrAdd(bot.Guid, _ => new TraceState());

        var owner = waitingOn switch
        {
            null => TraceOwner.CS,
            var w when w.StartsWith("timer:") => TraceOwner.TIMER,
            var w when w.StartsWith("group:") => TraceOwner.GROUP,
            var w when w.StartsWith("cpp:") => TraceOwner.CPP,
            _ => TraceOwner.WAIT
        };
        SetOwner(ts, owner, waitingOn);

        ts.LastActivity = bot.CurrentActivity.Type.ToString();
        ts.LastSubPhase = to;

        Emit(bot, ts, TraceKind.Transition, state, from: from, to: to, cause: cause, detail: detail);
    }

    /// <summary>A strategic-eval result: full weight vector + winner + why.</summary>
    public void Decision(BotIdentity bot, DecisionResult result, string cause, BotStateSnapshot? state = null)
    {
        if (!IsTraced(bot.Guid)) return;
        var ts = _state.GetOrAdd(bot.Guid, _ => new TraceState());
        SetOwner(ts, TraceOwner.CS, null);

        var weights = string.Join(" ", result.WeightBreakdown
            .OrderByDescending(kv => kv.Value)
            .Select(kv => $"{kv.Key}={kv.Value:0.00}"));
        var detail = $"win={result.NewActivity} roll={result.RollValue:0.000} reason={result.Reason} | {weights}";

        Emit(bot, ts, TraceKind.Decision, state,
            to: result.NewActivity.ToString(), cause: cause, detail: detail);
    }

    /// <summary>
    /// C# sent a bridge command. Registers an open command for heuristic event
    /// matching, flips owner → WAIT. Returns the correlation id.
    /// </summary>
    public int Command(BotIdentity bot, string cmdType, string detail = "", BotStateSnapshot? state = null)
    {
        if (!IsTraced(bot.Guid)) return 0;
        var ts = _state.GetOrAdd(bot.Guid, _ => new TraceState());

        int corr = (int)Interlocked.Increment(ref _corrCounter);
        var expects = ExpectedEvents.TryGetValue(cmdType, out var e) ? e : Array.Empty<string>();

        lock (ts.OpenCommands)
        {
            ts.OpenCommands.Add(new OpenCommand { Corr = corr, Type = cmdType, SentAt = DateTime.UtcNow, Expects = expects });
            // keep the open list bounded — a command with no matching event ages out
            if (ts.OpenCommands.Count > 16) ts.OpenCommands.RemoveAt(0);
        }

        // Fire-and-forget commands (no expected event) leave the bot in CS, not WAIT.
        if (expects.Length > 0) SetOwner(ts, TraceOwner.WAIT, WaitOn.Event(string.Join("|", expects)));

        Emit(bot, ts, TraceKind.Command, state, to: cmdType, corr: corr, detail: detail);
        return corr;
    }

    /// <summary>
    /// A bridge event arrived. Heuristically matches the most-recent open command
    /// whose expected set contains this event, computes round-trip latency, clears
    /// the wait, flips owner → CS.
    /// </summary>
    public void Event(BotIdentity bot, string evtType, string detail = "", BotStateSnapshot? state = null)
    {
        if (!IsTraced(bot.Guid)) return;
        var ts = _state.GetOrAdd(bot.Guid, _ => new TraceState());

        OpenCommand? matched = null;
        lock (ts.OpenCommands)
        {
            for (int i = ts.OpenCommands.Count - 1; i >= 0; i--)
            {
                if (ts.OpenCommands[i].Expects.Contains(evtType))
                {
                    matched = ts.OpenCommands[i];
                    ts.OpenCommands.RemoveAt(i);
                    break;
                }
            }
        }

        long? latMs = matched != null ? (long)(DateTime.UtcNow - matched.SentAt).TotalMilliseconds : null;
        if (matched != null) SetOwner(ts, TraceOwner.CS, null);  // notify arrived → C# owns again

        Emit(bot, ts, TraceKind.Event, state,
            from: evtType, cause: matched != null ? $"ack:{matched.Type}" : "unsolicited",
            corr: matched?.Corr, latMs: latMs, detail: detail);
    }

    /// <summary>
    /// 5s STATE reconcile. Records the C++ self-report and flags a mismatch when
    /// C#'s believed owner disagrees with what C++ says it's doing. Throttled:
    /// writes on change or every STATE_HEARTBEAT_SEC, so a frozen bot doesn't spam.
    /// </summary>
    public void State(BotIdentity bot, BotStateSnapshot snap, string? cppState = null)
    {
        if (!IsTraced(bot.Guid)) return;
        var ts = _state.GetOrAdd(bot.Guid, _ => new TraceState());

        ts.LastPos = (snap.X, snap.Y, snap.Z, snap.MapId);

        bool changed = cppState != ts.LastCppState;
        bool heartbeat = (DateTime.UtcNow - ts.LastStateWrite).TotalSeconds >= STATE_HEARTBEAT_SEC;
        ts.LastCppState = cppState;
        if (!changed && !heartbeat) return;
        ts.LastStateWrite = DateTime.UtcNow;

        // Reconcile: if C# believes it's waiting on a C++ event but C++ reports IDLE,
        // the grind/move silently ended or never started (a dropped notify).
        bool? mismatch = null;
        if (cppState != null)
            mismatch = ts.Owner == TraceOwner.WAIT && cppState == "IDLE";

        Emit(bot, ts, TraceKind.State, snap, detail: $"hp={snap.HealthPercent:0.00} bags={snap.FreeSlots}/{snap.TotalSlots}",
            cppState: cppState, mismatch: mismatch);
    }

    /// <summary>Arbitrary annotation (watchdog reset, RecordIssue mirror, manual note).</summary>
    public void Mark(BotIdentity bot, string detail, BotStateSnapshot? state = null)
    {
        if (!IsTraced(bot.Guid)) return;
        var ts = _state.GetOrAdd(bot.Guid, _ => new TraceState());
        Emit(bot, ts, TraceKind.Mark, state, detail: detail);
    }

    /// <summary>
    /// The bot is blocking on a dependency (a group gate, a timer) WITHOUT changing
    /// sub-phase. Owner is derived from the waitingOn prefix. Emits exactly ONE record
    /// on entry into a distinct wait — repeat calls with the same waitingOn are silent,
    /// so a follower parked on a gate produces a single line and the stuck-sweep is what
    /// flags "too long". This is the diagnostically critical Session 34/35 freeze signal.
    /// </summary>
    public void Wait(BotIdentity bot, string waitingOn, string detail = "", BotStateSnapshot? state = null)
    {
        if (!IsTraced(bot.Guid)) return;
        var ts = _state.GetOrAdd(bot.Guid, _ => new TraceState());

        var owner = waitingOn switch
        {
            var w when w.StartsWith("timer:") => TraceOwner.TIMER,
            var w when w.StartsWith("group:") => TraceOwner.GROUP,
            var w when w.StartsWith("cpp:") => TraceOwner.CPP,
            _ => TraceOwner.WAIT
        };

        bool isNew = ts.Owner != owner || ts.Wait != waitingOn;
        SetOwner(ts, owner, waitingOn);
        if (isNew)
            Emit(bot, ts, TraceKind.Wait, state, cause: "wait", detail: detail);
    }

    /// <summary>
    /// Silent liveness touch: re-arms the stuck sweep without emitting a record or
    /// changing owner. Called on a progress signal (KILL/LOOT/LEVEL_UP/QUEST_UPDATE) so an
    /// actively-working bot is never flagged — only one that has genuinely gone silent trips
    /// the sweep. Re-arms for:
    ///   • CPP phases (e.g. a KILL while grinding under cpp:grind), and
    ///   • non-movement WAITs (e.g. a multi-minute SET_TASK grind in DoingObjectives, which
    ///     waits on bare evt:TASK_COMPLETE while streaming KILL/LOOT).
    /// Deliberately a NO-OP for a movement WAIT (an evt: token that includes MOVE_FAILED or
    /// PATH_UNSAFE): a dropped MOVE_TO arrival produces NO further events, so it must still
    /// trip — that is the dropped-notify signature. GROUP/TIMER gates are likewise never
    /// re-armed by unrelated progress events.
    /// </summary>
    public void Ping(BotIdentity bot)
    {
        if (!IsTraced(bot.Guid)) return;
        if (!_state.TryGetValue(bot.Guid, out var ts)) return;

        bool isMovementWait = ts.Wait != null &&
            (ts.Wait.Contains("MOVE_FAILED") || ts.Wait.Contains("PATH_UNSAFE"));
        bool rearm = ts.Owner == TraceOwner.CPP ||
                     (ts.Owner == TraceOwner.WAIT && !isMovementWait);
        if (rearm)
        {
            ts.LastOwnerChange = DateTime.UtcNow;
            ts.StuckReported = false;
        }
    }

    /// <summary>
    /// Fleet-level group lifecycle record (form / disband / remove / promote). Unlike the
    /// per-bot emit methods, this isn't tied to one bot's owner/wait baton — it's a structural
    /// event about the group itself, keyed by leaderGuid + groupId so it interleaves into the
    /// merged timeline. Traced if ANY member is on the allowlist, so a group containing a
    /// watched bot always logs its membership changes (a botched leader promotion or a silent
    /// disband is a prime suspect for a permanently-false group gate).
    /// </summary>
    public void GroupEvent(string action, int groupId, int leaderGuid, IEnumerable<int> members, string detail = "")
    {
        var memberList = members?.ToList() ?? new List<int>();
        if (!_enabled) return;
        if (!memberList.Append(leaderGuid).Any(IsTraced)) return;

        var rec = new TraceRecord
        {
            Ts = DateTime.UtcNow.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture),
            Guid = leaderGuid,
            Name = $"group:{groupId}",
            Grp = groupId,
            Role = "leader",
            Owner = TraceOwner.GROUP,
            Act = "GroupManager",
            Kind = TraceKind.GroupLifecycle,
            Cause = action,
            Detail = string.IsNullOrEmpty(detail)
                ? $"leader={leaderGuid} members=[{string.Join(",", memberList)}]"
                : detail
        };
        _pending.Enqueue(rec);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Main-loop hook: flush + auto-stuck sweep (called from BotBrainService ~250ms)
    // ──────────────────────────────────────────────────────────────────────

    public void TickTrace(IReadOnlyDictionary<int, BotIdentity> bots, Func<int, BotStateSnapshot?> getState)
    {
        if (!_enabled) return;
        var now = DateTime.UtcNow;

        // Auto-stuck sweep: catch frozen ownership even where nobody instrumented.
        foreach (var kvp in _state)
        {
            if (!bots.TryGetValue(kvp.Key, out var bot)) continue;
            var ts = kvp.Value;

            int threshold = ts.Owner switch
            {
                TraceOwner.WAIT => STUCK_WAIT_SEC,
                TraceOwner.GROUP => STUCK_GROUP_SEC,
                TraceOwner.TIMER => STUCK_TIMER_SEC,
                TraceOwner.CPP => STUCK_CPP_SEC,
                _ => STUCK_CS_SEC
            };

            double frozenSec = (now - ts.LastOwnerChange).TotalSeconds;
            if (frozenSec >= threshold && !ts.StuckReported)
            {
                ts.StuckReported = true;
                Emit(bot, ts, TraceKind.Stuck, getState(kvp.Key),
                    cause: "frozen",
                    detail: $"owner={ts.Owner} wait={ts.Wait ?? "-"} for {frozenSec:0}s in {ts.LastActivity}:{ts.LastSubPhase}");
            }
        }

        if ((now - _lastFlush).TotalSeconds >= FLUSH_INTERVAL_SECONDS || _pending.Count >= MAX_BUFFER_BEFORE_FORCE_FLUSH)
        {
            FlushToDisk();
            _lastFlush = now;
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // Internals
    // ──────────────────────────────────────────────────────────────────────

    private long _corrCounter;

    private void SetOwner(TraceState ts, string owner, string? waitingOn)
    {
        if (ts.Owner != owner || ts.Wait != waitingOn)
        {
            ts.Owner = owner;
            ts.Wait = waitingOn;
            ts.LastOwnerChange = DateTime.UtcNow;
            ts.StuckReported = false;   // ownership moved → re-arm stuck detection
        }
    }

    private void Emit(BotIdentity bot, TraceState ts, string kind, BotStateSnapshot? state,
        string? from = null, string? to = null, string? cause = null,
        int? corr = null, long? latMs = null, string detail = "",
        string? cppState = null, bool? mismatch = null)
    {
        var pos = state != null ? (state.X, state.Y, state.Z, state.MapId) : ts.LastPos;

        var rec = new TraceRecord
        {
            Ts = DateTime.UtcNow.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture),
            Seq = ++ts.Seq,
            Guid = bot.Guid,
            Name = bot.Name,
            Lvl = bot.Level,
            Grp = bot.GroupId,
            Role = bot.IsGroupLeader ? "leader" : bot.IsGroupFollower ? "follower" : "solo",
            Owner = ts.Owner,
            Act = bot.CurrentActivity.Type.ToString(),
            Sub = bot.CurrentActivity.SubPhase,
            Kind = kind,
            From = from,
            To = to,
            Cause = cause,
            Wait = ts.Wait,
            Corr = corr,
            LatMs = latMs,
            Detail = detail,
            X = pos.Item1,
            Y = pos.Item2,
            Z = pos.Item3,
            Map = pos.Item4,
            GObj = bot.IsGrouped ? bot.GroupAllObjectivesDone : null,
            GTurn = bot.IsGrouped ? bot.GroupAllMembersTurnedIn : null,
            GQuest = bot.IsGrouped ? bot.GroupAllMembersQuesting : null,
            CppState = cppState,
            Mismatch = mismatch
        };

        _pending.Enqueue(rec);
    }

    private void FlushToDisk()
    {
        if (_pending.IsEmpty) return;
        var batch = new List<TraceRecord>();
        while (_pending.TryDequeue(out var r)) batch.Add(r);
        if (batch.Count == 0) return;

        try
        {
            var path = Path.Combine(TRACE_DIR,
                $"trace_{DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.jsonl");
            using var writer = new StreamWriter(path, append: true);
            foreach (var rec in batch)
                writer.WriteLine(JsonSerializer.Serialize(rec, _jsonOpts));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "BotFlightRecorder: failed to flush {Count} records", batch.Count);
        }
    }

    // --- Heuristic command → expected-event map (Batch 3 chose heuristic matching) ---
    // Best-effort: exact command/event strings get aligned when BotBridgeService
    // lands in Batch 2. An event with no matching open command is logged as
    // "unsolicited" rather than dropped.
    private static readonly Dictionary<string, string[]> ExpectedEvents = new()
    {
        ["MOVE_TO"] = new[] { "TASK_COMPLETE", "MOVE_FAILED", "PATH_UNSAFE" },
        ["SET_TASK"] = new[] { "TASK_COMPLETE" },
        ["QUERY_QUEST_STATUS"] = new[] { "QUEST_STATUS_ALL" },
        ["SELL_ITEMS"] = new[] { "SELL_ACK", "SELL_FAIL" },
        ["REPAIR_AT_NPC"] = new[] { "REPAIR_ACK", "REPAIR_FAIL" },
        ["TRAIN_AT_NPC"] = new[] { "TRAIN_ACK", "TRAIN_FAIL" },
        ["QUEST_INTERACT"] = new[] { "QUEST_ACCEPT_ACK", "QUEST_COMPLETE_ACK", "QUEST_INTERACT_FAIL" },
        ["ACCEPT_QUEST"] = new[] { "QUEST_ACCEPT_ACK", "QUEST_INTERACT_FAIL" },
        ["COMPLETE_QUEST"] = new[] { "QUEST_COMPLETE_ACK", "QUEST_INTERACT_FAIL" },
        ["USE_GAMEOBJECT"] = new[] { "USE_GO_ACK", "USE_GO_FAIL" },
        ["TAKE_FLIGHT"] = new[] { "FLIGHT_STARTED", "FLIGHT_FAILED" },
        ["RESURRECT"] = new[] { "RESPAWN" },
    };
}

// ════════════════════════════════════════════════════════════════════════════
// Per-bot trace state — owned by the recorder so domains stay clean.
// ════════════════════════════════════════════════════════════════════════════

internal class TraceState
{
    public long Seq;
    public string Owner = TraceOwner.CS;
    public string? Wait;
    public DateTime LastOwnerChange = DateTime.UtcNow;
    public bool StuckReported;

    public string LastActivity = "";
    public string? LastSubPhase;
    public (float, float, float, int) LastPos;

    public readonly List<OpenCommand> OpenCommands = new();

    public DateTime LastStateWrite = DateTime.MinValue;
    public string? LastCppState;
}

internal class OpenCommand
{
    public int Corr;
    public string Type = "";
    public DateTime SentAt;
    public string[] Expects = Array.Empty<string>();
}

// ════════════════════════════════════════════════════════════════════════════
// BotTrace — static facade so any domain can emit WITHOUT a constructor ref.
// BotBrainService calls BotTrace.Attach(recorder) once at startup. Every call is
// a cheap null-check + IsTraced gate when off. This is the "not permanent" lever:
// the calls can stay in the code, the output is toggled.
// ════════════════════════════════════════════════════════════════════════════

public static class BotTrace
{
    private static BotFlightRecorder? _r;
    public static void Attach(BotFlightRecorder recorder) => _r = recorder;

    public static void Transition(BotIdentity bot, string from, string to, string cause,
        string? waitingOn = null, string detail = "", BotStateSnapshot? state = null)
        => _r?.Transition(bot, from, to, cause, waitingOn, detail, state);

    public static void Decision(BotIdentity bot, DecisionResult result, string cause, BotStateSnapshot? state = null)
        => _r?.Decision(bot, result, cause, state);

    public static int Command(BotIdentity bot, string cmdType, string detail = "", BotStateSnapshot? state = null)
        => _r?.Command(bot, cmdType, detail, state) ?? 0;

    public static void Event(BotIdentity bot, string evtType, string detail = "", BotStateSnapshot? state = null)
        => _r?.Event(bot, evtType, detail, state);

    public static void State(BotIdentity bot, BotStateSnapshot snap, string? cppState = null)
        => _r?.State(bot, snap, cppState);

    public static void Mark(BotIdentity bot, string detail, BotStateSnapshot? state = null)
        => _r?.Mark(bot, detail, state);

    public static void Wait(BotIdentity bot, string waitingOn, string detail = "", BotStateSnapshot? state = null)
        => _r?.Wait(bot, waitingOn, detail, state);

    public static void Ping(BotIdentity bot) => _r?.Ping(bot);

    public static void GroupEvent(string action, int groupId, int leaderGuid, IEnumerable<int> members, string detail = "")
        => _r?.GroupEvent(action, groupId, leaderGuid, members, detail);
}