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
        traceDir = TRACE_DIR
    };

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
                WriteLine(JsonSerializer.Serialize(new { k = "dump", g = dump.Guid, reason = dump.Reason, segs = segs.Count }));
                FlushSegments(segs);
                _log.LogWarning("[CIRCUIT] auto-dump for bot {Guid} ({Reason}): {Count} segments flushed", dump.Guid, dump.Reason, segs.Count);
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

    private void FlushSegments(List<CircuitTrace.TickSegment> segs)
    {
        if (segs.Count == 0) return;
        EnsureWriter();
        EmitNewSites();
        foreach (var s in segs)
        {
            var hits = new object?[s.Hits.Count][];
            for (int i = 0; i < s.Hits.Count; i++)
            {
                var h = s.Hits[i];
                hits[i] = h.Note != null ? new object?[] { h.SiteId, h.Value, h.Note }
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

    private void EnsureWriter()
    {
        var today = DateTime.UtcNow.ToString("yyyyMMdd");
        if (_writer != null && _writerDate == today) return;
        _writer?.Dispose();
        _writer = new StreamWriter(Path.Combine(TRACE_DIR, $"circuit_{today}.jsonl"), append: true);
        _writerDate = today;
        // New file (or rollover): replay the FULL site manifest so every daily file
        // decodes standalone.
        _siteWatermark = 0;
        EmitNewSites();
    }

    private void WriteLine(string line)
    {
        EnsureWriter();
        _writer!.WriteLine(line);
    }
}
