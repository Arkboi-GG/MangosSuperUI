using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using MangosSuperUI.BotLogic.Core;

namespace MangosSuperUI.BotLogic.Tracking;

// ════════════════════════════════════════════════════════════════════════════
// BotStoryRider — a per-bot causal "story" emitter that rides along one bot.
// ════════════════════════════════════════════════════════════════════════════
//
// WHAT THIS IS (and is deliberately NOT)
//   This is NOT the flight recorder. The recorder is a single DI singleton with a
//   static BotTrace facade that writes ONE MERGED, per-DAY file and answers "who
//   owns the baton / is this stuck." This rider is the opposite shape on purpose:
//
//     • One INSTANCE PER BOT, carried on BotIdentity (bot.Story) — it is passed
//       around with the bot, so any code path that has the bot can emit.
//     • One FILE PER BOT, keyed by guid+name (story_<guid>_<name>.jsonl). The file
//       IS that bot's narrative, already in order — no date bucket, no read-time
//       demux. Read Uqib's file, get Uqib's story.
//     • A PASSIVE listener: it only ever READS bot state and WRITES a record. It
//       must never change control flow, a weight, a command, a timer, or a quest
//       state. Every emit is additive and zero-behavior-change.
//     • TOGGLE-ABLE per bot (Enabled) for debugging. Off = a cheap bool check + an
//       early return, so the emit calls can live in the code forever.
//
// THE STORY vs THE TRACE
//   The recorder logs STATE/EVENT/COMMAND/TRANSITION. This logs INTENT and
//   CAUSALITY: why a pick happened, which quest a travel was for, what blocked it,
//   and the link (corr) between a C# decision and its C++ outcome across the bridge.
//
// CORRELATION (corr)
//   When the bot is about to send a bridge command, call Intent(...) — it returns
//   a corr string "<guid>:<seq>". Stamp that on the command payload
//   (BridgeCommand.WithCorr). The C++ body echoes the same corr on its outcome
//   event AND on its own story record; the offline merger stitches the two sides
//   by equal corr. EVAL/PICK/DIRECTIVE and other non-command records carry no corr.
//
// PAIRED C++ SIDE : BotStoryRider.h/.cpp → story_<guid>_<name>_cpp.jsonl (own file).
// MERGER          : aibot-story.py interleaves a bot's two files by ts, links corr.
//
// SCHEMA NOTE
//   Matches STORYRIDER_PLAN §2 with ONE addition: `lvl`. Because the sink is now
//   per-bot (not the recorder's merged trace), the story file must stand alone, and
//   the target read-out ("Uqib · level 4 · WEDGE …") needs the level locally. Flag
//   in the plan's §8 if you'd rather drop it and cross-ref STATE instead.
// ════════════════════════════════════════════════════════════════════════════

/// <summary>Which runtime emitted a story record.</summary>
public static class StorySide
{
    public const string CS = "CS";
    public const string CPP = "CPP";
}

/// <summary>Frozen verb taxonomy (STORYRIDER_PLAN §2). Do not invent verbs ad hoc;
/// record any addition in the plan's §8 first.</summary>
public static class StoryVerb
{
    // Quest decision
    public const string PICK = "PICK";
    public const string ACCEPT = "ACCEPT";
    public const string TURNIN = "TURNIN";
    public const string OBJECTIVE = "OBJECTIVE";
    public const string NOQUESTS = "NOQUESTS";
    public const string DEFER = "DEFER";
    public const string BLACKLIST = "BLACKLIST";
    public const string ABANDON = "ABANDON";
    public const string FALLBACK = "FALLBACK";
    // Movement
    public const string TRAVEL = "TRAVEL";
    public const string ARRIVE = "ARRIVE";
    public const string MOVE_FAIL = "MOVE_FAIL";
    public const string REPATH = "REPATH";
    // Activity
    public const string GRIND = "GRIND";
    public const string VENDOR = "VENDOR";
    public const string TRAIN = "TRAIN";
    public const string EAT = "EAT";
    public const string REZ = "REZ";
    public const string FOLLOW = "FOLLOW";
    public const string REGROUP = "REGROUP";
    public const string WANDER = "WANDER";
    // Decision engine
    public const string EVAL = "EVAL";
    public const string DIRECTIVE = "DIRECTIVE";
    public const string OVERRIDE = "OVERRIDE";
    public const string ROLL = "ROLL";
    // Meta
    public const string WEDGE = "WEDGE";
    public const string RESET = "RESET";
    public const string NOTE = "NOTE";
}

/// <summary>Frozen result set (STORYRIDER_PLAN §2).</summary>
public static class StoryResult
{
    public const string START = "START"; // beginning an attempt
    public const string OK     = "OK";    // succeeded
    public const string FAIL   = "FAIL";  // failed (see reason)
    public const string SKIP   = "SKIP";  // deliberately chose not to
    public const string BLOCK  = "BLOCK"; // gated / filtered out (blacklist, tooFar, class-mask…)
}

public sealed class StoryQuest
{
    [JsonPropertyName("id")]    public int Id { get; set; }
    [JsonPropertyName("title")] public string? Title { get; set; }

    public StoryQuest() { }
    public StoryQuest(int id, string? title = null) { Id = id; Title = title; }
}

public sealed class StoryTarget
{
    [JsonPropertyName("x")]   public float X { get; set; }
    [JsonPropertyName("y")]   public float Y { get; set; }
    [JsonPropertyName("z")]   public float Z { get; set; }
    [JsonPropertyName("map")] public int Map { get; set; }

    public StoryTarget() { }
    public StoryTarget(float x, float y, float z, int map) { X = x; Y = y; Z = z; Map = map; }
}

/// <summary>One line in a bot's story file. Frozen shape (STORYRIDER_PLAN §2) + `lvl`.</summary>
public sealed class StoryRecord
{
    [JsonPropertyName("kind")]   public string Kind { get; set; } = "STORY";
    [JsonPropertyName("ts")]     public long Ts { get; set; }            // unix ms UTC — cross-side ordering key
    [JsonPropertyName("guid")]   public int Guid { get; set; }
    [JsonPropertyName("name")]   public string Name { get; set; } = "";
    [JsonPropertyName("lvl")]    public int Lvl { get; set; }
    [JsonPropertyName("side")]   public string Side { get; set; } = StorySide.CS;
    [JsonPropertyName("seq")]    public long Seq { get; set; }           // per-side monotonic
    [JsonPropertyName("corr")]   public string? Corr { get; set; }       // "<guid>:<seq>" links CS↔CPP, else null
    [JsonPropertyName("verb")]   public string Verb { get; set; } = "";
    [JsonPropertyName("result")] public string? Result { get; set; }
    [JsonPropertyName("phase")]  public string? Phase { get; set; }      // "act:sub" at emit time
    [JsonPropertyName("quest")]  public StoryQuest? Quest { get; set; }
    [JsonPropertyName("target")] public StoryTarget? Target { get; set; }
    [JsonPropertyName("reason")] public string? Reason { get; set; }
    [JsonPropertyName("detail")] public string? Detail { get; set; }
}

// ════════════════════════════════════════════════════════════════════════════

public sealed class BotStoryRider
{
    public const string DefaultStoryDir = "/opt/mangossuperui/diagnostics/story";

    private readonly BotIdentity _bot;     // back-ref — read-only use: live phase + level
    private readonly int _guid;
    private readonly string _name;
    private readonly string _path;

    private long _seq;
    private readonly ConcurrentQueue<StoryRecord> _pending = new();
    private readonly object _flushLock = new();
    private DateTime _lastFlush = DateTime.UtcNow;

    private const int FlushIntervalSeconds = 30;
    private const int MaxBufferBeforeForceFlush = 512;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>Per-bot debug toggle. When false, every emit is a bool check + return.</summary>
    public bool Enabled { get; set; }

    /// <summary>Best-effort IO diagnostics (no ILogger dependency — stays self-contained).</summary>
    public long DroppedRecords { get; private set; }
    public string? LastError { get; private set; }

    public BotStoryRider(BotIdentity bot, bool enabled = false, string? storyDir = null)
    {
        _bot = bot;
        _guid = bot.Guid;
        _name = bot.Name;
        Enabled = enabled;

        var dir = storyDir ?? DefaultStoryDir;
        try { Directory.CreateDirectory(dir); }
        catch (Exception ex) { LastError = ex.Message; }
        _path = Path.Combine(dir, $"story_{_guid}_{Sanitize(_name)}.jsonl");
    }

    // ── Public emit surface ─────────────────────────────────────────────────

    /// <summary>
    /// A command-driven START. Generates a corr ("&lt;guid&gt;:&lt;seq&gt;") and returns it so
    /// the caller can stamp it on the bridge command (BridgeCommand.WithCorr).
    /// Returns null when the rider is disabled — WithCorr(null) is a safe no-op.
    /// </summary>
    public string? Intent(string verb, StoryTarget? target = null, StoryQuest? quest = null,
        string? reason = null, string detail = "", string result = StoryResult.START)
        => Write(verb, result, corr: null, genCorr: true, quest, target, reason, detail);

    /// <summary>An outcome that echoes the corr carried back on a bridge event/ack.</summary>
    public void Outcome(string verb, string result, string? corr = null, StoryTarget? target = null,
        StoryQuest? quest = null, string? reason = null, string detail = "")
        => Write(verb, result, corr, genCorr: false, quest, target, reason, detail);

    /// <summary>A record with no command lineage: EVAL/PICK/DIRECTIVE/ROLL/DEFER/NOQUESTS/etc.</summary>
    public void Emit(string verb, string? result = null, StoryQuest? quest = null,
        StoryTarget? target = null, string? reason = null, string detail = "")
        => Write(verb, result, corr: null, genCorr: false, quest, target, reason, detail);

    /// <summary>Self-diagnosis: a watchdog/guard classified a stall the moment it happened.</summary>
    public void Wedge(string reason, string detail = "")
        => Write(StoryVerb.WEDGE, result: null, corr: null, genCorr: false, null, null, reason, detail);

    /// <summary>Freeform annotation (rider attach, session boundary, etc.).</summary>
    public void Note(string detail)
        => Write(StoryVerb.NOTE, result: null, corr: null, genCorr: false, null, null, null, detail);

    // ── Core ────────────────────────────────────────────────────────────────

    private string? Write(string verb, string? result, string? corr, bool genCorr,
        StoryQuest? quest, StoryTarget? target, string? reason, string detail)
    {
        if (!Enabled) return corr;                 // passive + near-zero cost when off

        long seq = Interlocked.Increment(ref _seq);
        string? finalCorr = corr ?? (genCorr ? $"{_guid}:{seq}" : null);

        _pending.Enqueue(new StoryRecord
        {
            Ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Guid = _guid,
            Name = _name,
            Lvl = _bot.Level,
            Side = StorySide.CS,
            Seq = seq,
            Corr = finalCorr,
            Verb = verb,
            Result = result,
            Phase = CurrentPhase(),
            Quest = quest,
            Target = target,
            Reason = reason,
            Detail = string.IsNullOrEmpty(detail) ? null : detail
        });

        if (_pending.Count >= MaxBufferBeforeForceFlush) FlushToDisk();
        return finalCorr;
    }

    private string? CurrentPhase()
    {
        var act = _bot.CurrentActivity;
        if (act == null) return null;
        return string.IsNullOrEmpty(act.SubPhase) ? act.Type.ToString() : $"{act.Type}:{act.SubPhase}";
    }

    // ── Flush ─────────────────────────────────────────────────────────────────

    /// <summary>Called from the BotBrainService main loop; flushes if the interval elapsed.</summary>
    public void FlushIfDue()
    {
        if ((DateTime.UtcNow - _lastFlush).TotalSeconds < FlushIntervalSeconds) return;
        FlushToDisk();
    }

    /// <summary>Force a flush (shutdown / disconnect).</summary>
    public void Flush() => FlushToDisk();

    private void FlushToDisk()
    {
        if (_pending.IsEmpty) return;

        lock (_flushLock)
        {
            _lastFlush = DateTime.UtcNow;
            if (_pending.IsEmpty) return;

            var batch = new List<StoryRecord>();
            while (_pending.TryDequeue(out var rec)) batch.Add(rec);
            if (batch.Count == 0) return;

            try
            {
                using var writer = new StreamWriter(_path, append: true);
                foreach (var rec in batch)
                    writer.WriteLine(JsonSerializer.Serialize(rec, JsonOpts));
            }
            catch (Exception ex)
            {
                // Story is best-effort instrumentation — it never throws into bot logic.
                // Records are dropped (not re-queued) so a bad path can't grow memory
                // without bound; fix the path and the next batch writes fine.
                DroppedRecords += batch.Count;
                LastError = ex.Message;
            }
        }
    }

    private static string Sanitize(string name)
    {
        if (string.IsNullOrEmpty(name)) return "unknown";
        var chars = name.Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_').ToArray();
        return new string(chars);
    }
}
