using System.Text.Json;
using Dapper;
using MangosSuperUI.Models;

namespace MangosSuperUI.BotLogic.Chat.Core;

// ======================== §14.4 registry (authoritative defaults) ========================

/// <summary>One tunable: key, UI group, control type, seeded default, meaning, slider range.</summary>
public sealed record ChatSettingDef(string Key, string Group, string Type, string Default,
                                    string Meaning, float Min = 0, float Max = 1, float Step = 0.01f);

/// <summary>
/// The CHAT_ARCHITECTURE §14.4 settings registry — the single source of truth for every
/// tunable's key, group, default, and UI shape. BotBrainDbInit seeds chat_settings from
/// this; the Feel page renders its controls from this; ChatSettingsService falls back to
/// this if a row ever goes missing. Locked defaults — change values in the DB, not here.
/// </summary>
public static class ChatSettingsRegistry
{
    public static readonly IReadOnlyList<ChatSettingDef> All = new List<ChatSettingDef>
    {
        // ── global (kill switches on Capacity; active_preset is display-only) ──
        new("global.chat_enabled",            "global", "bool",  "true",  "master kill switch"),
        new("global.ambient_enabled",         "global", "bool",  "true",  "ambient lane kill switch"),
        new("global.active_preset",           "global", "label", "2005 Authentic", "display only"),

        // ── density ──
        new("density.ambient_base_per_zone_hour",   "density", "int",   "12",  "exchanges/zone-hour pre-multipliers", 0, 60, 1),
        new("density.presence_mult",                "density", "float", "1.5", "zone has ≥1 real player", 0, 3, 0.05f),
        new("density.empty_zone_mult",              "density", "float", "0.15","D17 trickle", 0, 1, 0.01f),
        new("density.diurnal_curve",                "density", "curve", "0.2,0.15,0.3,0.7,1.0,0.8", "6 points at 02/06/10/14/18/22h, lerp", 0, 2, 0.05f),
        new("density.channel_msgs_per_hour",        "density", "int",   "30",  "ambient General/Trade lines per zone", 0, 120, 1),
        new("density.max_parallel_ambient_per_zone","density", "int",   "2",   "concurrent scripted exchanges", 0, 8, 1),

        // ── responsiveness ──
        new("responsiveness.urge_threshold",        "responsiveness", "float", "1.0", "§9.2 speak threshold", 0, 3, 0.05f),
        new("responsiveness.w_addr",                "responsiveness", "float", "2.0", "§9.2 weight: addressed", 0, 3, 0.05f),
        new("responsiveness.w_thread",              "responsiveness", "float", "1.2", "§9.2 weight: live thread", 0, 3, 0.05f),
        new("responsiveness.w_rel",                 "responsiveness", "float", "0.6", "§9.2 weight: relationship", 0, 3, 0.05f),
        new("responsiveness.w_pers",                "responsiveness", "float", "0.5", "§9.2 weight: personality", 0, 3, 0.05f),
        new("responsiveness.w_prox",                "responsiveness", "float", "0.4", "§9.2 weight: proximity", 0, 3, 0.05f),
        new("responsiveness.whisper_always_replies","responsiveness", "bool",  "true","whispers skip urge scoring"),
        new("responsiveness.bot_cooldown_s",        "responsiveness", "int",   "8",   "per-bot line cooldown (seconds)", 0, 60, 1),

        // ── noise ──
        new("noise.w_noise",                      "noise", "float", "0.35", "urge random term (D18)", 0, 1, 0.01f),
        new("noise.ignore_chance",                "noise", "float", "0.06", "post-threshold ignore roll", 0, 1, 0.01f),
        new("noise.max_parallel_convos_per_spot", "noise", "int",   "2",    "crosstalk allowance", 0, 8, 1),
        new("noise.max_bot_chain_depth",          "noise", "int",   "2",    "D16 hard cap", 0, 5, 1),
        new("noise.chain_penalty",                "noise", "float", "0.8",  "urge penalty per chain depth", 0, 2, 0.05f),

        // ── voice ──
        new("voice.wpm_mult",             "voice", "float", "1.0", "global typing speed scale", 0, 3, 0.05f),
        new("voice.typo_mult",            "voice", "float", "1.0", "global typo scale", 0, 3, 0.05f),
        new("voice.split_aggressiveness", "voice", "float", "1.0", "scales split_threshold inverse", 0, 3, 0.05f),
        new("voice.banter_intensity",     "voice", "float", "0.5", "0 wholesome → 1 edgy (above the floor)", 0, 1, 0.01f),
        new("voice.library_target",       "voice", "int",   "300", "§6.3 voice library size", 50, 1000, 10),
        new("voice.hold_min_ms",          "voice", "int",   "2000", "reply delay floor — lowest possible think+type hold", 500, 10000, 100),
        new("voice.hold_max_ms",          "voice", "int",   "45000", "reply delay ceiling before alt-tab tails", 5000, 120000, 1000),

        // ── topicality ──
        new("topicality.ingame_ratio",             "topicality", "float",  "0.65", "in-game vs out-of-game talk", 0, 1, 0.01f),
        new("topicality.weights",                  "topicality", "string", "loot:3,quests:3,class:2,reallife:2,popculture:1,server:2", "ambient topic categories"),
        new("topicality.lifesim_event_daily_chance","topicality","float",  "0.08", "§8 (alias lifesim.event_daily_chance)", 0, 1, 0.01f),

        // ── memory ──
        new("memory.overhear_log_chance",     "memory", "float", "0.15", "§7.2 overheard Tier-1 sampling", 0, 1, 0.01f),
        new("memory.t1_lines_per_bot_hour",   "memory", "int",   "120",  "§7.2 valve", 0, 600, 10),
        new("memory.t1_retention_days",       "memory", "int",   "14",   "§7.5 verbatim retention", 1, 90, 1),
        new("memory.compaction_cadence_hours","memory", "int",   "24",   "§7.5 batch cadence", 1, 168, 1),
        new("memory.compaction_min_rows",     "memory", "int",   "60",   "§7.5 skip below this", 0, 500, 5),
        new("memory.recency_halflife_days",   "memory", "int",   "21",   "§7.3 strength decay", 1, 90, 1),
        new("memory.forget_floor",            "memory", "float", "0.15", "§7.5 forget below strength", 0, 1, 0.01f),
        new("memory.forget_after_days",       "memory", "int",   "45",   "§7.5 forget after silence", 1, 365, 1),

        // ── era ──
        new("era.scrub_enabled", "era", "bool", "true", "§10.4 step 7 anachronism scrub"),

        // ── barks ──
        new("barks.ding_chance", "barks", "float", "0.35", "§9.6 level-up bark chance", 0, 1, 0.01f),

        // ── budget ──
        new("budget.bot_lines_per_min",          "budget", "int", "4",  "per-bot token bucket", 0, 60, 1),
        new("budget.zone_say_lines_per_min",     "budget", "int", "20", "per-zone say bucket", 0, 120, 1),
        new("budget.zone_channel_lines_per_min", "budget", "int", "10", "per-zone channel bucket", 0, 120, 1),
        new("budget.zone_party_lines_per_min",   "budget", "int", "10", "per-zone party bucket", 0, 120, 1),

        // ── lifesim ──
        new("lifesim.active_window_days", "lifesim", "int", "14", "§8 scope guard", 1, 60, 1),

        // ── pairing ──
        new("pairing.rel_bias",   "pairing", "float", "3.0", "§9.5 D5 relationship bias", 0, 10, 0.1f),
        new("pairing.level_band", "pairing", "int",   "4",   "± levels for ambient pairing", 0, 10, 1),

        // ── tier0 ──
        new("tier0.window_lines", "tier0", "int", "10", "§7.1 live window lines", 2, 30, 1),
        new("tier0.ttl_min",      "tier0", "int", "10", "§7.1 live window TTL (minutes)", 1, 60, 1),
    };

    public static readonly IReadOnlyDictionary<string, ChatSettingDef> ByKey =
        All.ToDictionary(d => d.Key, d => d);
}

// ======================== §14.2 built-in presets ========================

/// <summary>
/// The five built-in presets (seeded builtin=1). Each is a name→value map bulk-written
/// into GLOBAL scope on apply (zone overrides untouched). "2005 Authentic" carries the
/// FULL default set so applying it is a complete reset (§14.2: "the defaults").
/// Derived multiplier values (Quiet ×0.3, Bustling ×2.5, …) were computed from the doc's
/// factors against §14.4 defaults — implementer-computed, flagged for operator review.
/// </summary>
public static class ChatPresets
{
    public static IReadOnlyDictionary<string, Dictionary<string, string>> BuiltIn { get; } = Build();

    private static Dictionary<string, Dictionary<string, string>> Build()
    {
        // 2005 Authentic = every §14.4 default except the display-only active_preset row.
        var authentic = ChatSettingsRegistry.All
            .Where(d => d.Key != "global.active_preset")
            .ToDictionary(d => d.Key, d => d.Default);

        return new Dictionary<string, Dictionary<string, string>>
        {
            ["2005 Authentic"] = authentic,

            ["Quiet Realm"] = new()   // density ×0.3, ignore 0.12, threshold 1.3
            {
                ["density.ambient_base_per_zone_hour"] = "4",   // 12 × 0.3
                ["density.channel_msgs_per_hour"] = "9",        // 30 × 0.3
                ["noise.ignore_chance"] = "0.12",
                ["responsiveness.urge_threshold"] = "1.3",
            },

            ["Bustling City"] = new() // density ×2.5, crosstalk 3, channel budgets ×2
            {
                ["density.ambient_base_per_zone_hour"] = "30",  // 12 × 2.5
                ["density.channel_msgs_per_hour"] = "75",       // 30 × 2.5
                ["noise.max_parallel_convos_per_spot"] = "3",
                ["budget.zone_channel_lines_per_min"] = "20",   // 10 × 2
            },

            ["RP-Heavy"] = new()      // in-game 0.75, banter low, typo ×0.5
            {
                ["topicality.ingame_ratio"] = "0.75",
                ["voice.banter_intensity"] = "0.2",
                ["voice.typo_mult"] = "0.5",
            },

            ["Minimal"] = new()       // ambient off, whisper_always on, everything else quiet
            {
                ["global.ambient_enabled"] = "false",
                ["responsiveness.whisper_always_replies"] = "true",
                ["responsiveness.urge_threshold"] = "1.5",
                ["density.ambient_base_per_zone_hour"] = "0",
                ["density.channel_msgs_per_hour"] = "0",
                ["budget.bot_lines_per_min"] = "2",
            },
        };
    }
}

// ======================== ChatSettingsService (§14.1) ========================

/// <summary>
/// Reads chat_settings into an immutable snapshot with a 5 s TTL (hot-apply, D10).
/// Resolution: `zone:&lt;id&gt;` overrides `global` for that zone. Writes go through the
/// controller (which audit-logs at [CHAT-SET]); this service performs the upsert and
/// invalidates the snapshot so the next read is fresh. Missing rows fall back to the
/// §14.4 registry default (belt-and-braces — the seed makes this unreachable normally).
/// </summary>
public class ChatSettingsService
{
    private static readonly TimeSpan SnapshotTtl = TimeSpan.FromSeconds(5);

    private readonly ConnectionFactory _db;
    private readonly ILogger<ChatSettingsService> _logger;
    private readonly object _gate = new();

    private volatile Dictionary<(string Scope, string Name), string>? _snapshot;
    private DateTime _snapshotUtc = DateTime.MinValue;

    public ChatSettingsService(ConnectionFactory db, ILogger<ChatSettingsService> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ---------- reads ----------

    /// <summary>Zone→global resolution: zone:&lt;id&gt; row wins, else global, else registry default.</summary>
    public string? Get(int zoneId, string key)
    {
        var snap = Snapshot();
        if (zoneId > 0 && snap.TryGetValue(($"zone:{zoneId}", key), out var zv)) return zv;
        if (snap.TryGetValue(("global", key), out var gv)) return gv;
        return ChatSettingsRegistry.ByKey.TryGetValue(key, out var def) ? def.Default : null;
    }

    public string? Get(string key) => Get(0, key);

    public float GetFloat(int zoneId, string key, float fallback = 0f) =>
        float.TryParse(Get(zoneId, key), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;

    public int GetInt(int zoneId, string key, int fallback = 0) =>
        int.TryParse(Get(zoneId, key), out var v) ? v : fallback;

    public bool GetBool(int zoneId, string key, bool fallback = false)
    {
        var s = Get(zoneId, key);
        if (string.IsNullOrEmpty(s)) return fallback;
        return s.Trim().ToLowerInvariant() is "true" or "1" or "yes" or "on";
    }

    /// <summary>Comma-separated float list (e.g. the diurnal curve's 6 points).</summary>
    public float[] GetCurve(int zoneId, string key)
    {
        var s = Get(zoneId, key) ?? "";
        return s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(p => float.TryParse(p, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var f) ? f : 0f)
                .ToArray();
    }

    /// <summary>All rows of one scope (Feel page model, save-as-custom capture).</summary>
    public Dictionary<string, string> GetScope(string scope) =>
        Snapshot().Where(kv => kv.Key.Scope == scope)
                  .ToDictionary(kv => kv.Key.Name, kv => kv.Value);

    // ---------- writes (called by ChatSettingsController only) ----------

    /// <summary>Upsert one row. Returns the previous value (null = row did not exist).</summary>
    public async Task<string?> SetAsync(string scope, string key, string value)
    {
        using var conn = _db.Admin();
        var old = await conn.QuerySingleOrDefaultAsync<string?>(
            "SELECT value FROM chat_settings WHERE scope=@scope AND name=@key",
            new { scope, key });
        await conn.ExecuteAsync(@"
            INSERT INTO chat_settings (scope, name, value) VALUES (@scope, @key, @value)
            ON DUPLICATE KEY UPDATE value=@value",
            new { scope, key, value });
        Invalidate();
        return old;
    }

    /// <summary>
    /// §14.1 preset apply: bulk-write the preset's pairs into GLOBAL scope (zone overrides
    /// untouched), set global/active_preset. Returns (key, old, new) per pair for [CHAT-SET].
    /// </summary>
    public async Task<List<(string Key, string? Old, string New)>?> ApplyPresetAsync(string name)
    {
        Dictionary<string, string>? pairs;
        using (var conn = _db.Admin())
        {
            var json = await conn.QuerySingleOrDefaultAsync<string?>(
                "SELECT settings_json FROM chat_preset WHERE name=@name", new { name });
            if (json == null) return null;
            pairs = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        }
        if (pairs == null) return null;

        var changes = new List<(string, string?, string)>();
        foreach (var (key, value) in pairs)
            changes.Add((key, await SetAsync("global", key, value), value));
        changes.Add(("global.active_preset",
            await SetAsync("global", "global.active_preset", name), name));
        return changes;
    }

    /// <summary>Save the current GLOBAL scope (minus active_preset) as a custom preset.</summary>
    public async Task<bool> SavePresetAsync(string name)
    {
        var current = GetScope("global");
        current.Remove("global.active_preset");
        var json = JsonSerializer.Serialize(current);

        using var conn = _db.Admin();
        var builtin = await conn.QuerySingleOrDefaultAsync<int?>(
            "SELECT builtin FROM chat_preset WHERE name=@name", new { name });
        if (builtin == 1) return false;   // never overwrite a built-in

        await conn.ExecuteAsync(@"
            INSERT INTO chat_preset (name, settings_json, builtin) VALUES (@name, @json, 0)
            ON DUPLICATE KEY UPDATE settings_json=@json",
            new { name, json });
        return true;
    }

    // ---------- snapshot plumbing ----------

    public void Invalidate() { lock (_gate) { _snapshotUtc = DateTime.MinValue; } }

    private Dictionary<(string Scope, string Name), string> Snapshot()
    {
        var snap = _snapshot;
        if (snap != null && DateTime.UtcNow - _snapshotUtc < SnapshotTtl)
            return snap;

        lock (_gate)
        {
            if (_snapshot != null && DateTime.UtcNow - _snapshotUtc < SnapshotTtl)
                return _snapshot;
            try
            {
                using var conn = _db.Admin();
                var rows = conn.Query<(string Scope, string Name, string Value)>(
                    "SELECT scope, name, value FROM chat_settings");
                _snapshot = rows.ToDictionary(r => (r.Scope, r.Name), r => r.Value);
                _snapshotUtc = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[CHAT-SET] snapshot refresh failed — serving stale/registry defaults");
                _snapshot ??= new Dictionary<(string, string), string>();
                _snapshotUtc = DateTime.UtcNow;   // don't hammer a down DB; retry after TTL
            }
            return _snapshot;
        }
    }
}