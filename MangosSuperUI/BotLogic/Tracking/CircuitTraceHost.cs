using System.Text.Json;
using Dapper;
using MangosSuperUI.Models;

namespace MangosSuperUI.BotLogic.Tracking;

// ════════════════════════════════════════════════════════════════════════════
// CircuitTraceHost — persistence + disk flush for the circuit board's C# probes.
//
// Owns everything the static CircuitTrace facade must not: the bot_settings
// toggle state (mode + armed guids — same pattern as BotFlightRecorder's
// trace:enabled/trace:guids), the daily JSONL trace file, and the per-loop pump
// that drains armed bots' sealed segments and any wedge auto-dump requests.
//
// Created by BotBrainService (like GroupManager — no DI registration churn);
// LoadSettingsAsync runs once at startup, Tick() runs from the ~250ms main loop.
//
// File format (one JSON object per line, self-decoding):
//   {"k":"site","id":1,"file":"BotLogic/...","line":105,"desc":"..."}      site manifest, emitted before first use
//   {"k":"tick","g":123,"t0":"...","t1":"...","map":0,"zone":12,"x":..,"y":..,"z":..,"h":[[id],[id,val],[id,null,"note"]]}
//   {"k":"inter", ...same shape, no pos...}                                 hits that arrived between ticks
//   {"k":"dump","g":123,"reason":"wedge","segs":N}                          header preceding an auto-dump's segments
// Every hit row is [siteId] | [siteId,value] | [siteId,null,"note"]; order within
// a segment is chronological, segments are ordered by their hits' global seq.
// ════════════════════════════════════════════════════════════════════════════
public sealed class CircuitTraceHost
{
    private const string TRACE_DIR = "/opt/mangossuperui/diagnostics/circuit";
    private const string KEY_MODE = "circuit:mode";     // "off" | "shadow"
    private const string KEY_GUIDS = "circuit:guids";   // csv of armed guids

    private readonly ConnectionFactory _db;
    private readonly ILogger _log;

    // [CIRCUIT Phase 3] toggles forward to the C++ side over the bridge (R6 — one
    // switch arms both probes). Late-set by BotBrainService to avoid ctor cycles.
    private Func<int, string, object, Task>? _sendToBot;
    private Func<string, object, Task>? _sendToAll;

    private StreamWriter? _writer;
    private string _writerDate = "";
    private int _siteWatermark;
    private DateTime _lastWriteError = DateTime.MinValue;

    public CircuitTraceHost(ConnectionFactory db, ILogger log)
    {
        _db = db;
        _log = log;
        try { Directory.CreateDirectory(TRACE_DIR); }
        catch (Exception ex) { _log.LogWarning(ex, "[CIRCUIT] cannot create trace dir {Dir} — flush disabled until it exists", TRACE_DIR); }
    }

    public void AttachBridge(Func<int, string, object, Task> sendToBot, Func<string, object, Task> sendToAll)
    {
        _sendToBot = sendToBot;
        _sendToAll = sendToAll;
    }

    // ── settings ────────────────────────────────────────────────────────────

    public async Task LoadSettingsAsync()
    {
        try
        {
            using var conn = _db.Admin();
            var rows = (await conn.QueryAsync<(string setting_key, string setting_value)>(
                "SELECT setting_key, setting_value FROM bot_settings WHERE setting_key IN (@M, @G)",
                new { M = KEY_MODE, G = KEY_GUIDS })).ToList();

            var mode = rows.FirstOrDefault(r => r.setting_key == KEY_MODE).setting_value;
            CircuitTrace.Mode = string.Equals(mode, "shadow", StringComparison.OrdinalIgnoreCase)
                ? CircuitTrace.TraceMode.Shadow
                : CircuitTrace.TraceMode.Off;

            var guids = rows.FirstOrDefault(r => r.setting_key == KEY_GUIDS).setting_value;
            if (!string.IsNullOrWhiteSpace(guids))
                foreach (var part in guids.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    if (int.TryParse(part, out var g))
                        CircuitTrace.Arm(g);

            _log.LogInformation("[CIRCUIT] settings loaded: mode={Mode} armed={Count}",
                CircuitTrace.Mode, CircuitTrace.ArmedGuids().Length);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "[CIRCUIT] failed to load settings — staying Off");
        }
    }

    public async Task SetModeAsync(CircuitTrace.TraceMode mode)
    {
        CircuitTrace.Mode = mode;
        await SaveSettingAsync(KEY_MODE, mode == CircuitTrace.TraceMode.Shadow ? "shadow" : "off");
        if (_sendToAll != null)
            await _sendToAll("CIRCUIT_TRACE", new { mode = mode == CircuitTrace.TraceMode.Shadow ? 1 : 0 });
        _log.LogInformation("[CIRCUIT] mode set to {Mode}", mode);
    }

    public async Task ArmAsync(int guid)
    {
        CircuitTrace.Arm(guid);
        await SaveSettingAsync(KEY_GUIDS, string.Join(",", CircuitTrace.ArmedGuids()));
        if (_sendToBot != null)
            await _sendToBot(guid, "CIRCUIT_TRACE", new { mode = CircuitTrace.Mode == CircuitTrace.TraceMode.Shadow ? 1 : 0, ship = 1 });
        _log.LogInformation("[CIRCUIT] armed bot {Guid}", guid);
    }

    public async Task DisarmAsync(int guid)
    {
        CircuitTrace.Disarm(guid);
        // Flush whatever the bot recorded up to the disarm so the tail isn't lost.
        FlushSegments(CircuitTrace.DrainSealed(guid));
        await SaveSettingAsync(KEY_GUIDS, string.Join(",", CircuitTrace.ArmedGuids()));
        if (_sendToBot != null)
            await _sendToBot(guid, "CIRCUIT_TRACE", new { ship = 0 });
        _log.LogInformation("[CIRCUIT] disarmed bot {Guid}", guid);
    }

    private async Task SaveSettingAsync(string key, string value)
    {
        try
        {
            using var conn = _db.Admin();
            await conn.ExecuteAsync(@"
                INSERT INTO bot_settings (setting_key, setting_value) VALUES (@K, @V)
                ON DUPLICATE KEY UPDATE setting_value = @V",
                new { K = key, V = value });
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "[CIRCUIT] failed to persist {Key} — toggle is live but won't survive restart", key);
        }
    }

    public object Status() => new
    {
        mode = CircuitTrace.Mode.ToString().ToLowerInvariant(),
        armed = CircuitTrace.ArmedGuids(),
        sites = CircuitTrace.Sites.Count,
        ringBots = CircuitTrace.RingCount,
        traceDir = TRACE_DIR,
        autoDumps = DumpStatsForStatus()
    };

    /// <summary>Wedge auto-dump accounting. Surfaced so the rate limit is visible:
    /// a throttle that silently eats evidence would undermine the instrument.</summary>
    private static object DumpStatsForStatus()
    {
        var d = CircuitTrace.DumpStats();
        return new { accepted = d.Accepted, suppressedBot = d.SuppressedBot, suppressedFleet = d.SuppressedFleet, thisHour = d.ThisHour };
    }

    // ── the per-loop pump ───────────────────────────────────────────────────

    /// <summary>Called from BotBrainService's main loop. Drains wedge auto-dump
    /// requests and every armed bot's sealed segments to the daily JSONL file.</summary>
    public void Tick()
    {
        if (CircuitTrace.Mode == CircuitTrace.TraceMode.Off) return;
        try
        {
            while (CircuitTrace.TryDequeueDump(out var dump))
            {
                var segs = CircuitTrace.DrainSealed(dump.Guid);
                if (segs.Count == 0) continue;
                WriteWedgeRecord(dump, segs);
            }

            foreach (var guid in CircuitTrace.ArmedGuids())
                FlushSegments(CircuitTrace.DrainSealed(guid));

            _writer?.Flush();
        }
        catch (Exception ex)
        {
            if ((DateTime.UtcNow - _lastWriteError).TotalSeconds > 60)
            {
                _lastWriteError = DateTime.UtcNow;
                _log.LogWarning(ex, "[CIRCUIT] flush failed (throttled log)");
            }
        }
    }

    // ── wedge auto-dump: a LEDGER, not a landfill ───────────────────────────
    // R8 was written assuming a wedge is a rare event worth preserving whole.
    // The live fleet says otherwise: ~30 wedge trips a minute across ~330 bots,
    // and the first implementation flushed each bot's ENTIRE ring every time —
    // 10,712 dumps, 5,016 of them the full 1,024 segments, 1.73 GB in one day,
    // 98.5% of it from bots nobody armed. It buried the traces you asked for.
    //
    // The fix keeps R8's promise (catch the wedge nobody was watching) and drops
    // its cost: EVERY wedge writes one compact ledger line, and the full ring is
    // written only when it can teach us something new — the bot is armed (you
    // asked for it), or this wedge SHAPE has not been seen before today. The
    // hundredth repeat of a known shape adds a counter, not 165 KB.
    private readonly Dictionary<string, int> _wedgeShapes = new();
    private string _wedgeShapeDay = "";
    private int _fullDumpsThisHour;
    private DateTime _fullDumpHour = DateTime.UtcNow;
    private const int FullDumpsPerHourCap = 20;

    private void WriteWedgeRecord((int Guid, string Reason) dump, List<CircuitTrace.TickSegment> segs)
    {
        var today = DateTime.UtcNow.ToString("yyyyMMdd");
        if (_wedgeShapeDay != today) { _wedgeShapes.Clear(); _wedgeShapeDay = today; }

        // The wedge's SHAPE: the ordered probe path of the last recorded slice.
        // Two wedges with the same shape are the same bug seen twice.
        var last = segs.LastOrDefault(s => s.Hits.Count > 0);
        var path = last?.Hits.Select(h => h.SiteId).ToArray() ?? Array.Empty<int>();
        var shape = string.Join(",", path);

        _wedgeShapes.TryGetValue(shape, out var seen);
        _wedgeShapes[shape] = seen + 1;

        if ((DateTime.UtcNow - _fullDumpHour).TotalHours >= 1) { _fullDumpHour = DateTime.UtcNow; _fullDumpsThisHour = 0; }
        bool armed = CircuitTrace.IsArmed(dump.Guid);
        bool novel = seen == 0 && _fullDumpsThisHour < FullDumpsPerHourCap;
        bool full = armed || novel;

        EnsureWriter();
        EmitNewSites();
        WriteLine(JsonSerializer.Serialize(new
        {
            k = "wedge",
            g = dump.Guid,
            t = DateTime.UtcNow.ToString("O"),
            reason = dump.Reason,
            shapeSeen = seen + 1,          // how many times this shape today
            segs = segs.Count,             // what was in the ring
            full,                          // whether the ring follows this line
            map = last?.MapId ?? -1,
            zone = last?.ZoneId ?? 0,
            x = last?.X ?? 0f,
            y = last?.Y ?? 0f,
            path                           // the decoded-elsewhere probe path
        }));

        if (!full) return;
        if (novel && !armed) _fullDumpsThisHour++;
        FlushSegments(segs);
        _log.LogWarning("[CIRCUIT] wedge dump for bot {Guid} ({Reason}): {Count} segments, shape #{Shape}",
            dump.Guid, dump.Reason, segs.Count, seen + 1);
    }

    private void FlushSegments(List<CircuitTrace.TickSegment> segs)
    {
        if (segs.Count == 0) return;
        EnsureWriter();
        EmitNewSites();
        foreach (var s in segs)
        {
            // A segment can contain hits from more than one thread (brain tick,
            // bridge socket, chat loop all write into whichever segment is open),
            // so each hit that did NOT come from the segment's own context carries
            // its context id as a 4th element. Readers treat "no 4th element" as
            // the segment's own context; two hits are control-flow adjacent only
            // when their contexts match. Absent that, the board draws edges that
            // never happened. Cost: nothing for the common case.
            int primaryCtx = s.Hits.Count > 0 ? s.Hits[0].Ctx : 0;
            var hits = new object?[s.Hits.Count][];
            for (int i = 0; i < s.Hits.Count; i++)
            {
                var h = s.Hits[i];
                hits[i] = h.Ctx != primaryCtx ? new object?[] { h.SiteId, h.Value, h.Note, h.Ctx }
                        : h.Note != null ? new object?[] { h.SiteId, h.Value, h.Note }
                        : h.Value != null ? new object?[] { h.SiteId, h.Value }
                        : new object?[] { h.SiteId };
            }
            WriteLine(JsonSerializer.Serialize(s.HasPos
                ? new
                {
                    k = s.Kind, g = s.Guid,
                    t0 = s.StartUtc.ToString("O"), t1 = s.EndUtc.ToString("O"),
                    map = s.MapId, zone = s.ZoneId, x = s.X, y = s.Y, z = s.Z,
                    h = hits
                }
                : (object)new
                {
                    k = s.Kind, g = s.Guid,
                    t0 = s.StartUtc.ToString("O"), t1 = s.EndUtc.ToString("O"),
                    h = hits
                }));
        }
    }

    /// <summary>Emit any sites registered since the watermark, directly to the current writer.
    /// Callers must EnsureWriter() first.</summary>
    private void EmitNewSites()
    {
        var news = new List<CircuitTrace.ProbeSite>();
        _siteWatermark = CircuitTrace.SitesSince(_siteWatermark, news);
        foreach (var site in news)
            _writer!.WriteLine(JsonSerializer.Serialize(new { k = "site", id = site.Id, file = site.File, line = site.Line, desc = site.Description }));
    }

    // A trace file is scoped to ONE PROCESS SESSION — never to a day.
    //
    // Site ids are session-local nicknames (R5: the durable identity is file:line;
    // the number is a coat-check ticket, valid for one visit). A daily file that
    // several processes append to therefore carries several CONFLICTING id spaces
    // in one stream, and any reader that builds a single id→site map silently
    // mislabels every record from the older sessions. It does not fail — it lies.
    //
    // This is not hypothetical. 2026-08-26: one day's file held FIVE sessions;
    // BotBrain.cs:255 was id 24, then 71, then 96, then 51, then 87. An analysis
    // decoded the whole file against the newest manifest and produced a confident,
    // completely wrong diagnosis of a stalled bot — retracted only because two
    // measurements happened to disagree and got chased down.
    //
    // One file per session makes the naive read the CORRECT read. The header
    // record below lets any reader assert it rather than infer it. See R11.
    private readonly string _sessionId =
        DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Environment.ProcessId;

    private void EnsureWriter()
    {
        var today = DateTime.UtcNow.ToString("yyyyMMdd");
        if (_writer != null && _writerDate == today) return;
        _writer?.Dispose();
        _writer = new StreamWriter(Path.Combine(TRACE_DIR, $"circuit_{today}_{_sessionId}.jsonl"), append: true);
        _writerDate = today;
        // Fresh file (new session, or midnight rollover within one session):
        // replay the FULL site manifest so this file decodes standalone — which is
        // now a true statement, because nothing else will ever append to it.
        _siteWatermark = 0;
        _writer.WriteLine(JsonSerializer.Serialize(new
        {
            k = "session",
            id = _sessionId,
            startedUtc = DateTime.UtcNow.ToString("O"),
            pid = Environment.ProcessId,
            note = "site ids in this file are scoped to THIS session only — never decode a trace against another session's manifest"
        }));
        EmitNewSites();
    }

    private void WriteLine(string line)
    {
        EnsureWriter();
        _writer!.WriteLine(line);
    }
}
