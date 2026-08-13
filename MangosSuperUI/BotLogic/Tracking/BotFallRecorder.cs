using System.Collections.Concurrent;
using System.Text.Json;
using MangosSuperUI.BotLogic.Core;
using MangosSuperUI.BotLogic.Data;
using MangosSuperUI.Services;
using Microsoft.Extensions.Logging;

namespace MangosSuperUI.BotLogic.Tracking;

// ════════════════════════════════════════════════════════════════════════════
// BotFallRecorder — the "black box" for bot MIS-PLACEMENT (wrong-Z and wrong-area)
// ════════════════════════════════════════════════════════════════════════════
//
// PURPOSE
//   Catch a bot going wrong IN THE ACT, so we stop reasoning about aftermath. For
//   every bot, always, keep a short in-memory ring of the last ~60s of {goal, step,
//   where it came from, where it was going, pos, ground-Z, corridor threat}. On a
//   trigger, persist the ring (the ~50% BEFORE) plus a ~60s tail (the ~50% AFTER)
//   to ONE JSONL, then stop. Two independent triggers, two output folders:
//     falls/  — WRONG Z: a sudden one-tick Z drop, or sinking below authoritative
//               ground (HeightMapService). "It fell through the floor."
//     strays/ — WRONG AREA: while travelling, the route/destination crosses into
//               content above the bot's level band (ZoneSafetyMap). "It's heading
//               somewhere it shouldn't." The capture logs the safety-gate verdict
//               (destination level, corridor max, the travel cap that allowed it)
//               so a stray shows WHY the guard was bypassed, not just that it was.
//   Nothing is written until a trigger fires, so this costs disk only when there
//   is a bug to look at.
//
// GROUND TRUTH, NOT SELF-REPORT
//   The floor comes from HeightMapService.GetHeight — the server's own GridMap
//   heightfield (the same data mangosd's Map::GetHeight reads, holes included).
//   A NULL return means the bot is over a terrain data-hole; that is recorded, not
//   guessed. This is deliberately independent of anything the bot brain believes.
//
// FALSE-POSITIVE GUARD (legit underground: Undercity, mines, caves)
//   A bot that has ALWAYS been below ground (Undercity sits under Tirisfal terrain)
//   must not fire. So the sustained-below trigger requires the bot to have been ON
//   ground recently (LastOnGroundUtc) — i.e. it fires on the TRANSITION surface→void,
//   never on steady-state underground. The sudden-drop trigger is Z-only and self-
//   guards (a stationary underground bot never suddenly drops).
//
// WIRING
//   DI singleton (Program.cs). BotBrainService.RunBrainTicksAsync calls Observe(ctx)
//   once per bot per ~250ms tick, right after Sense refreshes ctx.Pos.
// ════════════════════════════════════════════════════════════════════════════
public sealed class BotFallRecorder
{
    private readonly HeightMapService _height;
    private readonly ZoneSafetyMap _safety;
    private readonly ILogger<BotFallRecorder> _logger;

    // Kill switch (2026-08-13): the recorder writes ~190KB per capture and runs at its
    // GLOBAL_MAX_PER_MIN cap on a live fleet (~GBs/day). That is a diagnostics tool, not a
    // default — OFF unless appsettings opts in with "BotDiagnostics:FallRecorder": true.
    private readonly bool _enabled;

    // ~60s before + ~60s after at the 250ms tick → a symmetric ~2-minute window.
    private const int RING_FRAMES = 240;      // BEFORE the trigger (the ring)
    private const int TAIL_FRAMES = 240;      // AFTER the trigger (recorded post-fire)

    // ── Wrong-Z (fall) trigger ──
    private const float SUDDEN_DROP_YD = 12f;    // > this much down in ONE ~250ms tick = falling, not walking
    private const float BELOW_GROUND_YD = 15f;   // Z this far under authoritative ground = under the floor
    private const int SUSTAIN_TICKS = 3;         // consecutive below-ground ticks before firing (ignore a blip)
    private const double RECENTLY_ON_GROUND_SEC = 45; // sustained-below only counts as a FALL if on-ground this recently
    private const double FALL_COOLDOWN_SEC = 300;     // at most one fall capture per bot per 5 min
    // FALSE-POSITIVE guards (2026-08-06, FINDING_001): a capture must show the bot ACTUALLY
    // off-surface, not just a one-tick Z delta or a noisy GetHeight sample over a still bot.
    private const float SUDDEN_BELOW_YD = 5f;    // a sudden drop only counts if it ALSO leaves the bot under ground (a step-down lands ON ground)
    private const float SINK_OWN_Z_YD = 8f;      // sustained-below only counts if the bot's OWN Z fell this far during the streak (else GetHeight flicker over a still bot)

    // ── Wrong-area (stray) trigger ──
    private const int STRAY_MARGIN = 5;          // route/destination mob > botLevel + this = straying into danger
    private const float STRAY_MIN_TRAVEL_YD = 150f;   // must actually be EN ROUTE (a real journey ahead)
    private const double STRAY_COOLDOWN_SEC = 600;    // travel is long — one stray capture per bot per 10 min
    // Movement gate (FINDING_009): "has a far target" is NOT "is traveling" — stationary bots carry
    // stale ctx.Targets for minutes (all 5 audited captures were artifacts). Require the same target
    // held STRAY_SUSTAIN_TICKS consecutive ticks AND the bot to have CLOSED ≥ STRAY_MIN_APPROACH_YD
    // on it since first seen (ticks are ~0.25–0.5 s; 20 ticks ≈ 5–10 s ≈ 35–70 yd at run speed).
    private const int STRAY_SUSTAIN_TICKS = 20;
    private const float STRAY_MIN_APPROACH_YD = 40f;

    // Fleet-wide backstop (2026-08-06, FINDING_001): even if a trigger regresses, cap total
    // captures across ALL bots so the recorder can never run away and bury real signal again.
    private const int GLOBAL_MAX_PER_MIN = 20;

    private const string BASE_DIR = "/opt/mangossuperui/diagnostics";
    private const string FALL_DIR = BASE_DIR + "/falls";
    private const string STRAY_DIR = BASE_DIR + "/strays";

    private static readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public BotFallRecorder(HeightMapService height, ZoneSafetyMap safety, ILogger<BotFallRecorder> logger,
        IConfiguration config)
    {
        _height = height;
        _safety = safety;
        _logger = logger;
        _enabled = config.GetValue("BotDiagnostics:FallRecorder", false);
        if (!_enabled)
        {
            _logger.LogInformation("BotFallRecorder: disabled (default) — set BotDiagnostics:FallRecorder=true to capture fall/stray black boxes");
            return;
        }
        try { Directory.CreateDirectory(FALL_DIR); Directory.CreateDirectory(STRAY_DIR); }
        catch (Exception ex) { _logger.LogWarning(ex, "BotFallRecorder: could not create diagnostics dirs under {Dir}", BASE_DIR); }
    }

    /// <summary>One recorded frame — compact camelCase JSON, one per line.</summary>
    private sealed class Frame
    {
        public string Ts { get; init; } = "";
        public int Guid { get; init; }
        public string Name { get; init; } = "";
        public int Lvl { get; init; }
        public string Goal { get; init; } = "";
        public string Step { get; init; } = "";
        public string? Reason { get; init; }        // ctx.GoalReason — the brain's own "why"
        public float X { get; init; }
        public float Y { get; init; }
        public float Z { get; init; }
        public int Map { get; init; }
        public int Zone { get; init; }
        public float? GroundZ { get; init; }         // authoritative GridMap height; null = terrain data-hole
        public float? DeltaGround { get; init; }      // Z - GroundZ (negative = under the floor)
        public bool? Hole { get; init; }              // true when GroundZ is null at this (x,y)
        public float? DropTick { get; init; }         // Z change since the previous tick (negative = descending)
        public float? Tx { get; init; }               // target the brain is driving to (where it's GOING)
        public float? Ty { get; init; }
        public float? Tz { get; init; }
        public float Dist2D { get; init; }
        public float? GrindZ { get; init; }           // grind AreaCenter Z (the surface the brain believes in)
        public int? CellLvl { get; init; }             // ZoneSafetyMap max creature level at the bot's own cell
        public int? CorridorMax { get; init; }         // max creature level on the straight route to Target (same-map)
        public int? TargetLvl { get; init; }           // max creature level at the destination cell
        public bool Dead { get; init; }
        public bool InCombat { get; init; }
        public float Hp { get; init; }
        public string? Pending { get; init; }         // outstanding bridge command type, if any
    }

    private sealed class Track
    {
        public readonly Queue<Frame> Ring = new();
        public float LastZ;
        public bool HaveLast;
        public int BelowStreak;
        public float BelowStreakStartZ;   // the bot's Z when the below-ground streak began — real sinks descend from it
        public DateTime LastOnGroundUtc = DateTime.MinValue;
        public DateTime LastFallCaptureUtc = DateTime.MinValue;
        public DateTime LastStrayCaptureUtc = DateTime.MinValue;
        // ── Stray movement gate (FINDING_009 method note): all 5 audited stray captures were
        // artifacts — 3 STATIONARY bots flagged via a stale ctx.Target, 2 transient-target flips.
        // A stray must be the SAME destination held across ticks AND the bot actually CLOSING
        // distance on it. Reset whenever the target changes or clears. ──
        public float StrayTgtX, StrayTgtY;
        public int StrayTgtTicks;         // consecutive ticks with this same target
        public float StrayTgtFirstDist;   // DistToTarget when this target was first seen
        public List<Frame>? Capture;   // non-null while recording the AFTER window
        public int TailLeft;
        public string? Kind;           // "fall" | "stray" — picks the output folder + header
        public string? Trigger;
    }

    private readonly ConcurrentDictionary<int, Track> _tracks = new();

    // ── Fleet-wide capture ceiling (backstop; see FINDING_001). Shared across all bots. ──
    private static readonly object _rateLock = new();
    private static readonly Queue<DateTime> _recentCaptures = new();
    private static bool GlobalRateOk()
    {
        var now = DateTime.UtcNow;
        lock (_rateLock)
        {
            while (_recentCaptures.Count > 0 && (now - _recentCaptures.Peek()).TotalSeconds > 60)
                _recentCaptures.Dequeue();
            if (_recentCaptures.Count >= GLOBAL_MAX_PER_MIN) return false;
            _recentCaptures.Enqueue(now);   // only consumed when we actually proceed to capture
            return true;
        }
    }

    /// <summary>
    /// Called once per bot per tick from BotBrainService after Sense. Records to the
    /// bot's ring and, on a detected fall, snapshots the ring + records the tail.
    /// Single-threaded (the brain tick loop is the only caller) — no locking needed.
    /// </summary>
    public void Observe(BotContext ctx)
    {
        if (!_enabled) return;

        var t = _tracks.GetOrAdd(ctx.Guid, _ => new Track());

        float z = ctx.Pos.Z;
        float? ground = _height.GetHeight(ctx.MapId, ctx.Pos.X, ctx.Pos.Y);
        float? deltaGround = ground is float g ? z - g : null;
        float? dropTick = t.HaveLast ? z - t.LastZ : null;

        // ── ZoneSafetyMap threat (2D). Cheap O(1) grid reads; null when the grid isn't loaded. ──
        int? cellLvl = null, corridorMax = null, targetLvl = null;
        if (_safety.IsLoaded)
        {
            // Danger is faction-relative (FINDING_002): query the grid for THIS bot's team, so a
            // Horde bot near Orgrimmar isn't flagged by its own (Alliance-hostile) city guards.
            var team = ZoneSafetyMap.TeamFromFaction(ctx.Identity?.Faction);
            cellLvl = _safety.GetMaxCreatureLevel(ctx.MapId, ctx.Pos.X, ctx.Pos.Y, team);
            if (ctx.Target is { } tg)
            {
                targetLvl = _safety.GetMaxCreatureLevel(tg.Map, tg.X, tg.Y, team);
                if (tg.Map == ctx.MapId)   // corridor sampling is single-map
                    corridorMax = _safety.GetMaxCreatureLevelOnPath(ctx.MapId, ctx.Pos.X, ctx.Pos.Y, tg.X, tg.Y, team);
            }
        }

        var frame = new Frame
        {
            Ts = DateTime.UtcNow.ToString("O"),
            Guid = ctx.Guid, Name = ctx.Name, Lvl = ctx.Level,
            Goal = ctx.Goal.ToString(), Step = ctx.Step, Reason = ctx.GoalReason,
            X = ctx.Pos.X, Y = ctx.Pos.Y, Z = z, Map = ctx.MapId, Zone = ctx.ZoneId,
            GroundZ = ground, DeltaGround = deltaGround, Hole = ground is null ? true : null,
            DropTick = dropTick,
            Tx = ctx.Target?.X, Ty = ctx.Target?.Y, Tz = ctx.Target?.Z, Dist2D = ctx.DistToTarget,
            GrindZ = ctx.Grind?.AreaCenter.Z,
            CellLvl = cellLvl, CorridorMax = corridorMax, TargetLvl = targetLvl,
            Dead = ctx.Dead, InCombat = ctx.InCombat, Hp = ctx.HpPct,
            Pending = ctx.Pending?.CommandType,
        };

        t.Ring.Enqueue(frame);
        while (t.Ring.Count > RING_FRAMES) t.Ring.Dequeue();

        // Track "was recently standing on the ground" — the surface→void transition guard.
        if (deltaGround is float dg && dg > -5f) t.LastOnGroundUtc = DateTime.UtcNow;

        // Already capturing an AFTER window: append and count down to the flush.
        if (t.Capture is not null)
        {
            t.Capture.Add(frame);
            if (--t.TailLeft <= 0) { Flush(ctx, t); t.Capture = null; t.Trigger = null; t.Kind = null; }
            t.LastZ = z; t.HaveLast = true;
            return;
        }

        // ── Triggers (never on a corpse — death has its own Z games). Fall has priority;
        //    one capture at a time (the guard above skips detection while capturing).
        //    FALSE-POSITIVE guards added 2026-08-06 (FINDING_001) — the raw triggers fired
        //    fleet-wide on non-bugs, so each now demands proof the bot is really off-surface. ──
        if (!ctx.Dead)
        {
            bool below = deltaGround is float dgb && dgb <= -BELOW_GROUND_YD;

            // WRONG-Z (sudden): a big one-tick drop that ALSO lands the bot under ground. A ledge
            // step-down or a server position-snap lands ON ground (deltaGround≈0) and is NOT a fall.
            bool suddenBelow = deltaGround is float dgs && dgs <= -SUDDEN_BELOW_YD;
            bool sudden = dropTick is float d && d <= -SUDDEN_DROP_YD && suddenBelow;

            // WRONG-Z (sustained): under ground for N ticks AND the bot's OWN Z fell to get there.
            // GetHeight flickers ±40-50yd at cliff edges over a stationary bot; those never descend.
            if (below)
            {
                if (t.BelowStreak == 0) t.BelowStreakStartZ = t.HaveLast ? t.LastZ : z;
                t.BelowStreak++;
            }
            else t.BelowStreak = 0;
            bool ownZSank = below && (t.BelowStreakStartZ - z) >= SINK_OWN_Z_YD;
            bool wasRecentlyOnGround = (DateTime.UtcNow - t.LastOnGroundUtc).TotalSeconds <= RECENTLY_ON_GROUND_SEC;
            bool sank = t.BelowStreak >= SUSTAIN_TICKS && wasRecentlyOnGround && ownZSank;

            bool fell = sudden || sank;

            // WRONG-AREA: EN ROUTE toward a DESTINATION cell above the band. The straight-line
            // corridor sampler crosses whole high-level zones on the shared continent (map 0) and
            // is too noisy to trigger on by itself — corridorMax is logged for context only.
            // Movement gate (FINDING_009): track how long the CURRENT target has been held and how
            // much distance the bot has closed on it; a stale target on a parked bot never closes.
            if (ctx.Target is { } stg && stg.Map == ctx.MapId)
            {
                float tdx = stg.X - t.StrayTgtX, tdy = stg.Y - t.StrayTgtY;
                if (t.StrayTgtTicks > 0 && tdx * tdx + tdy * tdy < 4f)   // same dest (±2 yd)
                    t.StrayTgtTicks++;
                else
                {
                    t.StrayTgtX = stg.X; t.StrayTgtY = stg.Y;
                    t.StrayTgtTicks = 1;
                    t.StrayTgtFirstDist = ctx.DistToTarget;
                }
            }
            else t.StrayTgtTicks = 0;

            int destLvl = targetLvl ?? 0;
            bool traveling = ctx.Target is not null && ctx.DistToTarget > STRAY_MIN_TRAVEL_YD
                             && t.StrayTgtTicks >= STRAY_SUSTAIN_TICKS
                             && (t.StrayTgtFirstDist - ctx.DistToTarget) >= STRAY_MIN_APPROACH_YD;
            bool strayed = traveling && destLvl > ctx.Level + STRAY_MARGIN;

            if (fell && (DateTime.UtcNow - t.LastFallCaptureUtc).TotalSeconds >= FALL_COOLDOWN_SEC && GlobalRateOk())
            {
                t.LastFallCaptureUtc = DateTime.UtcNow;
                t.Kind = "fall";
                t.Trigger = sudden ? $"sudden_drop {dropTick:F0}yd in one tick to {deltaGround:F0}yd under ground"
                                   : $"sank_through_floor {deltaGround:F0}yd under ground (own-Z fell {(t.BelowStreakStartZ - z):F0}yd)";
                StartCapture(t);
                _logger.LogWarning("BotFallRecorder: {Name} (guid {Guid}) FELL — {Trigger} at z={Z:F0} ground={Ground} zone={Zone}",
                    ctx.Name, ctx.Guid, t.Trigger, z, ground?.ToString("F0") ?? "hole", ctx.ZoneId);
            }
            else if (strayed && (DateTime.UtcNow - t.LastStrayCaptureUtc).TotalSeconds >= STRAY_COOLDOWN_SEC && GlobalRateOk())
            {
                t.LastStrayCaptureUtc = DateTime.UtcNow;
                t.Kind = "stray";
                t.Trigger = $"stray L{ctx.Level} toward dest mob L{destLvl} (corridor {corridorMax?.ToString() ?? "?"}, dest {targetLvl?.ToString() ?? "?"})";
                StartCapture(t);
                _logger.LogWarning("BotFallRecorder: {Name} (guid {Guid}, L{Lvl}) STRAYING — {Trigger}, {Dist:F0}yd out, zone={Zone}",
                    ctx.Name, ctx.Guid, ctx.Level, t.Trigger, ctx.DistToTarget, ctx.ZoneId);
            }
        }

        t.LastZ = z;
        t.HaveLast = true;
    }

    private void StartCapture(Track t)
    {
        t.Capture = new List<Frame>(t.Ring);   // the BEFORE (ring already includes the current frame)
        t.TailLeft = TAIL_FRAMES;               // then record the AFTER
    }

    private void Flush(BotContext ctx, Track t)
    {
        var frames = t.Capture;
        if (frames is null || frames.Count == 0) return;
        bool stray = t.Kind == "stray";
        string dir = stray ? STRAY_DIR : FALL_DIR;
        string prefix = stray ? "stray" : "fall";
        try
        {
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, $"{prefix}_{ctx.Guid}_{Sanitize(ctx.Name)}_{DateTime.UtcNow:yyyyMMdd-HHmmss}.jsonl");

            object header;
            if (stray)
            {
                int firstOver = frames.FindIndex(f => (f.TargetLvl ?? 0) > ctx.Level + STRAY_MARGIN);
                header = new
                {
                    kind = "stray_header", trigger = t.Trigger,
                    guid = ctx.Guid, name = ctx.Name, level = ctx.Level, zone = ctx.ZoneId, map = ctx.MapId,
                    dest = ctx.Target is { } tg ? new { x = tg.X, y = tg.Y, z = tg.Z, map = tg.Map } : null,
                    distToDest = ctx.DistToTarget,
                    // The gate that was SUPPOSED to stop this — dest within the cap = the 2D straight-line leak.
                    travelCapForLevel = ZoneSafetyMap.GetMaxTravelDistance(ctx.Level, ctx.ZoneId),
                    frames = frames.Count, firstOverBandIndex = firstOver,
                    window = "ring(before) + tail(after)",
                };
            }
            else
            {
                int firstBelow = frames.FindIndex(f => f.DeltaGround is float d && d <= -BELOW_GROUND_YD);
                header = new
                {
                    kind = "fall_header", trigger = t.Trigger,
                    guid = ctx.Guid, name = ctx.Name, level = ctx.Level, zone = ctx.ZoneId, map = ctx.MapId,
                    frames = frames.Count, firstBelowIndex = firstBelow,
                    window = "ring(before) + tail(after)",
                };
            }

            using var sw = new StreamWriter(path);
            sw.WriteLine(JsonSerializer.Serialize(header, _json));
            foreach (var f in frames) sw.WriteLine(JsonSerializer.Serialize(f, _json));

            _logger.LogWarning("BotFallRecorder: wrote {Path} ({N} frames)", path, frames.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BotFallRecorder: failed to write {Kind} capture for {Name} (guid {Guid})", prefix, ctx.Name, ctx.Guid);
        }
    }

    /// <summary>Drop a bot's ring when it disconnects, so evicted guids don't leak memory.</summary>
    public void Forget(int guid) => _tracks.TryRemove(guid, out _);

    private static string Sanitize(string s)
    {
        var chars = s.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray();
        return chars.Length == 0 ? "bot" : new string(chars);
    }
}
