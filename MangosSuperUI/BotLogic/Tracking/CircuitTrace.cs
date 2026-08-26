using System.Runtime.CompilerServices;

namespace MangosSuperUI.BotLogic.Tracking;

// ════════════════════════════════════════════════════════════════════════════
// CircuitTrace — the C# probe facade of the circuit board (CIRCUIT_BOARD.md).
//
// A probe is one line dropped into a branch arm of the bot's decision logic:
//
//     CircuitTrace.Hit(ctx.Guid, "wedge: no progress past ceiling");
//     CircuitTrace.Hit(ctx.Guid, "grind: too few spawns", spawnCount);
//     CircuitTrace.HitNote(ctx.Guid, "negate: failure stamped", reason);
//     goal switch { Goal.Grinding => CircuitTrace.Pass(plan, guid, "chose grind"), ... }
//
// Identity is the CALL SITE (file:line, auto-stamped by the compiler — design
// rule R4). Probes carry no hand-assigned numbers; each site gets a compact
// session id on first hit, and the session manifest (Sites) maps ids back to
// file:line for decoding. The generated Map (tools/circuit-board) joins on the
// same file:line key.
//
// Phase 2 runtime — modes (resolves the R7/R8 tension, recorded in the doc):
//   Off    — probes are one volatile read; nothing is recorded anywhere.
//   Shadow — EVERY bot's ring records (bounded memory, zero disk); this is what
//            lets the wedge auto-dump (R8) produce a trace for a bot nobody had
//            armed. Flushing still requires arming.
//   Armed  — per-guid, on top of Shadow: the host drains that bot's sealed
//            segments to the JSONL trace file continuously.
//
// Segments (R10): hits accumulate into per-tick segments. The host brackets each
// brain tick with BeginTick/EndTick; EndTick stamps the bot's world position so
// every segment can be drawn on the game map. Hits that arrive outside a tick
// (bridge socket threads, chat loop) open an "inter" segment sealed at the next
// tick boundary — chronology holds via the global Seq on every hit.
//
// This class lives in BotLogic/Tracking, which is EXCLUDED from probing scope:
// the instrument never probes itself. Persistence/flush lives in
// CircuitTraceHost (bot_settings + daily JSONL), driven from BotBrainService's
// main loop — the same attach pattern as BotFlightRecorder.
// ════════════════════════════════════════════════════════════════════════════
public static class CircuitTrace
{
    public enum TraceMode { Off = 0, Shadow = 1 }

    /// <summary>One recorded probe firing. Seq is global and monotonic — the merge/order key.
    /// Ctx is the recording thread: the brain tick, the bridge socket and the chat loop all
    /// write into the SAME open segment, so two adjacent hits are only genuinely
    /// control-flow adjacent when their Ctx matches. Without it the board draws edges that
    /// never happened and the teleport checker cries wolf (found by the first Layer 3 scan,
    /// 2026-08-26: 51,663 "teleports" in one bot-hour, most of them thread interleaving).</summary>
    public readonly record struct ProbeHit(int SiteId, long Seq, string? Note, double? Value, int Ctx);

    /// <summary>Ctx value for hits that came from the C++ side: one batch is one
    /// core-side update, so the whole segment shares a context by construction.</summary>
    public const int RemoteCtx = -1;

    /// <summary>One registered call site. Session-scoped; the generated Map joins on File:Line.</summary>
    public sealed record ProbeSite(int Id, string File, int Line, string Description);

    /// <summary>A sealed slice of one bot's trace: the ordered hits of one tick (or the
    /// gap between ticks), stamped with when and — for real ticks — where in the world.</summary>
    public sealed class TickSegment
    {
        public int Guid;
        public string Kind = "tick";          // tick | inter | overflow
        public DateTime StartUtc;
        public DateTime EndUtc;
        public bool HasPos;
        public int MapId = -1;
        public int ZoneId;
        public float X, Y, Z;
        public List<ProbeHit> Hits = new(16);
    }

    // ── mode + armed set ────────────────────────────────────────────────────
    private static volatile int _mode = (int)TraceMode.Off;
    public static TraceMode Mode
    {
        get => (TraceMode)_mode;
        set => _mode = (int)value;
    }

    // Armed = flushed-to-disk set. Frozen snapshot swapped whole (lock-free readers).
    private static volatile HashSet<int> _armedGuids = new();
    private static readonly object _swapLock = new();

    public static void Arm(int guid)
    {
        lock (_swapLock) { _armedGuids = new HashSet<int>(_armedGuids) { guid }; }
    }

    public static void Disarm(int guid)
    {
        lock (_swapLock)
        {
            var next = new HashSet<int>(_armedGuids);
            next.Remove(guid);
            _armedGuids = next;
        }
    }

    public static bool IsArmed(int guid) => _armedGuids.Contains(guid);
    public static int[] ArmedGuids() => _armedGuids.ToArray();

    // ── call-site registry (session-scoped compact ids) ─────────────────────
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(string File, int Line), ProbeSite> _sites = new();
    private static readonly List<ProbeSite> _siteList = new(2048);   // append-only, watermark reads
    private static int _nextSiteId;

    /// <summary>All sites seen this session — the manifest the Map joins against.</summary>
    public static IReadOnlyList<ProbeSite> Sites
    {
        get { lock (_siteList) return _siteList.ToArray(); }
    }

    /// <summary>Sites registered since the given watermark (for incremental manifest emission).
    /// Returns the new watermark.</summary>
    public static int SitesSince(int watermark, List<ProbeSite> into)
    {
        lock (_siteList)
        {
            for (int i = watermark; i < _siteList.Count; i++) into.Add(_siteList[i]);
            return _siteList.Count;
        }
    }

    private static int SiteId(string file, int line, string desc)
    {
        if (_sites.TryGetValue((file, line), out var known)) return known.Id;
        var created = _sites.GetOrAdd((file, line),
            static (key, d) => new ProbeSite(Interlocked.Increment(ref _nextSiteId), key.File, key.Line, d), desc);
        // First sight: publish to the ordered manifest list exactly once (the GetOrAdd
        // winner and any racing losers all pass here; the list add is id-deduped).
        lock (_siteList)
        {
            if (!_siteListIds.Contains(created.Id)) { _siteList.Add(created); _siteListIds.Add(created.Id); }
        }
        return created.Id;
    }
    private static readonly HashSet<int> _siteListIds = new();

    // ── per-bot rings of sealed segments ────────────────────────────────────
    // Shadow memory budget: SegmentRingCap segments/bot ≈ 4 min of ticks at 4Hz.
    // ~200 bots × 1024 segs × ~20 hits × 32B ≈ 40MB worst case — tune here.
    private const int SegmentRingCap = 1024;
    private const int OpenSegmentHitCap = 10_000;   // runaway guard: force-seal a pathological open segment

    private sealed class BotRing
    {
        public readonly object Lock = new();
        public TickSegment? Open;
        public readonly Queue<TickSegment> Sealed = new();
        // View cache: sealed segments ALSO land here and survive DrainSealed, so
        // /Peek can show an armed bot whose flush queue is drained every 250ms.
        // Same objects, references only — the memory is the segments themselves.
        public readonly Queue<TickSegment> Recent = new();
        public long Dropped;   // segments evicted unflushed (shadow ring overwrite)
    }
    private const int RecentViewCap = 512;

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, BotRing> _rings = new();
    private static long _seq;

    /// <summary>The host brackets each brain tick. An unsealed previous segment
    /// (missed EndTick — exception path) is sealed as-is so nothing is lost.</summary>
    public static void BeginTick(int guid)
    {
        if (_mode == (int)TraceMode.Off) return;
        var ring = _rings.GetOrAdd(guid, static _ => new BotRing());
        lock (ring.Lock)
        {
            SealOpen_NoLock(ring);
            ring.Open = new TickSegment { Guid = guid, Kind = "tick", StartUtc = DateTime.UtcNow };
        }
    }

    /// <summary>Seal the current tick segment, stamping the bot's world position (R10).</summary>
    public static void EndTick(int guid, int mapId, int zoneId, float x, float y, float z)
    {
        if (_mode == (int)TraceMode.Off) return;
        if (!_rings.TryGetValue(guid, out var ring)) return;
        lock (ring.Lock)
        {
            if (ring.Open == null) return;
            ring.Open.HasPos = true;
            ring.Open.MapId = mapId;
            ring.Open.ZoneId = zoneId;
            ring.Open.X = x; ring.Open.Y = y; ring.Open.Z = z;
            SealOpen_NoLock(ring);
        }
    }

    private static void SealOpen_NoLock(BotRing ring)
    {
        var open = ring.Open;
        if (open == null) return;
        ring.Open = null;
        if (open.Hits.Count == 0 && open.Kind != "tick") return;   // empty inter-gap: drop silently
        open.EndUtc = DateTime.UtcNow;
        ring.Sealed.Enqueue(open);
        while (ring.Sealed.Count > SegmentRingCap) { ring.Sealed.Dequeue(); ring.Dropped++; }
        ring.Recent.Enqueue(open);
        while (ring.Recent.Count > RecentViewCap) ring.Recent.Dequeue();
    }

    /// <summary>Move a bot's sealed segments out (host flush). Oldest first.</summary>
    public static List<TickSegment> DrainSealed(int guid)
    {
        if (!_rings.TryGetValue(guid, out var ring)) return new List<TickSegment>();
        lock (ring.Lock)
        {
            var outList = new List<TickSegment>(ring.Sealed.Count);
            while (ring.Sealed.Count > 0) outList.Add(ring.Sealed.Dequeue());
            return outList;
        }
    }

    /// <summary>Copy (without clearing) a bot's recent segments — the API read path.
    /// Reads the view cache, which flushing does NOT drain, so it works for both
    /// shadow and armed bots.</summary>
    public static List<TickSegment> PeekSegments(int guid, int maxSegments = 256)
    {
        if (!_rings.TryGetValue(guid, out var ring)) return new List<TickSegment>();
        lock (ring.Lock)
        {
            return ring.Recent.Skip(Math.Max(0, ring.Recent.Count - maxSegments)).ToList();
        }
    }

    /// <summary>Drop a bot's ring entirely (bot evicted/deleted).</summary>
    public static void Forget(int guid) => _rings.TryRemove(guid, out _);

    /// <summary>Bots currently holding trace rings (for status display).</summary>
    public static int RingCount => _rings.Count;

    // ── chains (R2) + the C++ side's remote sites / segments (R1, R3) ──────
    // Chain ids stamp every outbound bridge envelope ("cbt"); both sides record
    // the id as a probe VALUE ("chain: command sent" here, "cpp-chain: command
    // adopted" over there), so the viewer stitches cause to effect by value.
    private static long _chain;
    public static long NextChain() => Interlocked.Increment(ref _chain);

    // Remote (C++) probe sites live in their own id space (rule R3): shifted by
    // RemoteSiteBase so they can never collide with local session ids.
    public const int RemoteSiteBase = 100000;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, ProbeSite> _remoteSites = new();

    public static void RegisterRemoteSite(int remoteId, string file, int line, string desc)
    {
        var site = new ProbeSite(RemoteSiteBase + remoteId, "cpp/" + file, line, desc);
        if (!_remoteSites.TryAdd(remoteId, site)) return;   // idempotent across re-ships
        lock (_siteList)
        {
            if (!_siteListIds.Contains(site.Id)) { _siteList.Add(site); _siteListIds.Add(site.Id); }
        }
    }

    /// <summary>Merge one C++ CIRCUIT_BATCH into the bot's timeline as a sealed
    /// "cpp" segment (position-stamped per R10). Hits get fresh global seqs at
    /// arrival — ordering across the two sides is by arrival, which the 1s ship
    /// cadence makes honest enough until chains take over.</summary>
    public static void IngestRemoteSegment(int guid, int mapId, int zoneId, float x, float y, float z,
        List<(int RemoteId, double? Value, string? Note)> hits, int drops)
    {
        if (_mode == (int)TraceMode.Off) return;
        var seg = new TickSegment
        {
            Guid = guid,
            Kind = drops > 0 ? "cpp-drops" : "cpp",
            StartUtc = DateTime.UtcNow,
            EndUtc = DateTime.UtcNow,
            HasPos = true,
            MapId = mapId,
            ZoneId = zoneId,
            X = x, Y = y, Z = z,
        };
        foreach (var h in hits)
            seg.Hits.Add(new ProbeHit(RemoteSiteBase + h.RemoteId, Interlocked.Increment(ref _seq), h.Note, h.Value, RemoteCtx));

        var ring = _rings.GetOrAdd(guid, static _ => new BotRing());
        lock (ring.Lock)
        {
            ring.Sealed.Enqueue(seg);
            while (ring.Sealed.Count > SegmentRingCap) { ring.Sealed.Dequeue(); ring.Dropped++; }
            ring.Recent.Enqueue(seg);
            while (ring.Recent.Count > RecentViewCap) ring.Recent.Dequeue();
        }
    }

    // ── wedge auto-dump requests (R8) ───────────────────────────────────────
    // Any code (the wedge breaker) may request that a bot's whole ring be flushed
    // to disk even though the bot was never armed. The host drains this queue.
    private static readonly System.Collections.Concurrent.ConcurrentQueue<(int Guid, string Reason)> _dumpRequests = new();

    // R8 promises the instrument catches wedges nobody was watching. At fleet
    // scale that promise bites: the wedge breaker trips for hundreds of bots and
    // every trip flushes that bot's ENTIRE ring. Measured on the live fleet
    // 2026-08-26: 9,872 auto-dumps in one afternoon → 8.1M lines / 1.6 GB in the
    // daily file. That is not a black box, it is a landfill — and it buries the
    // one bot you actually wanted to read.
    //
    // The real fix is in the host (CircuitTraceHost.WriteWedgeRecord): every
    // wedge writes one small LEDGER line, and only a novel wedge shape — or an
    // armed bot — costs a full ring. These two limits are just runaway backstops
    // on top of that: a wedge breaker that trips every tick must not be able to
    // spam even cheap lines, and nothing should ever queue without bound.
    // Suppressions are COUNTED and reported in Status, because a throttle that
    // hides what it dropped is worse than no throttle. They are set loose on
    // purpose: at the observed rate (~1 wedge per bot per 11 minutes) they should
    // suppress almost nothing — losing wedge COUNTS would cost us the fleet
    // picture, and the counts are now the cheap part.
    private const int DumpCooldownSec = 30;      // per bot: kills per-tick spam only
    private const int DumpsPerHourCap = 5000;    // fleet-wide runaway backstop
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, DateTime> _lastDumpAt = new();
    private static long _dumpsAccepted, _dumpsSuppressedBot, _dumpsSuppressedFleet;
    private static readonly object _dumpWindowLock = new();
    private static DateTime _dumpWindowStart = DateTime.UtcNow;
    private static int _dumpsThisWindow;

    public static void RequestDump(int guid, string reason)
    {
        if (_mode == (int)TraceMode.Off) return;

        var now = DateTime.UtcNow;
        if (_lastDumpAt.TryGetValue(guid, out var last) && (now - last).TotalSeconds < DumpCooldownSec)
        {
            Interlocked.Increment(ref _dumpsSuppressedBot);
            return;
        }

        lock (_dumpWindowLock)
        {
            if ((now - _dumpWindowStart).TotalHours >= 1) { _dumpWindowStart = now; _dumpsThisWindow = 0; }
            if (_dumpsThisWindow >= DumpsPerHourCap)
            {
                Interlocked.Increment(ref _dumpsSuppressedFleet);
                return;
            }
            _dumpsThisWindow++;
        }

        _lastDumpAt[guid] = now;
        Interlocked.Increment(ref _dumpsAccepted);
        _dumpRequests.Enqueue((guid, reason));
    }

    /// <summary>Auto-dump accounting for Status — accepted vs. suppressed, so the
    /// rate limit is always visible rather than silently eating evidence.</summary>
    public static (long Accepted, long SuppressedBot, long SuppressedFleet, int ThisHour) DumpStats()
        => (Interlocked.Read(ref _dumpsAccepted),
            Interlocked.Read(ref _dumpsSuppressedBot),
            Interlocked.Read(ref _dumpsSuppressedFleet),
            Volatile.Read(ref _dumpsThisWindow));

    /// <summary>Manual dumps from the UI bypass the rate limit — an operator
    /// asking for a specific bot is never noise.</summary>
    public static void RequestDumpForced(int guid, string reason)
    {
        if (_mode == (int)TraceMode.Off) return;
        _lastDumpAt[guid] = DateTime.UtcNow;
        Interlocked.Increment(ref _dumpsAccepted);
        _dumpRequests.Enqueue((guid, reason));
    }

    public static bool TryDequeueDump(out (int Guid, string Reason) request) => _dumpRequests.TryDequeue(out request);

    // ── probes ──────────────────────────────────────────────────────────────
    // Descriptions must be compile-time constant strings; values go through the
    // typed overloads so nothing allocates while the mode is Off.

    /// <summary>Bare routing probe: this branch arm ran.</summary>
    public static void Hit(int guid, string description,
        [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        if (_mode == (int)TraceMode.Off) return;
        Record(guid, file, line, description, null, null);
    }

    /// <summary>Probe carrying the value the branch looked at (count, distance, percent…).</summary>
    public static void Hit(int guid, string description, double value,
        [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        if (_mode == (int)TraceMode.Off) return;
        Record(guid, file, line, description, null, value);
    }

    /// <summary>Probe carrying a short state word (goal name, reason code). Distinct name, not an
    /// overload: a third string argument on Hit would be ambiguous against the caller-info
    /// parameters. Pass an ALREADY-BUILT string (never interpolate at the call site).</summary>
    public static void HitNote(int guid, string description, string note,
        [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        if (_mode == (int)TraceMode.Off) return;
        Record(guid, file, line, description, note, null);
    }

    /// <summary>Expression-position probe for switch arms and ternaries: returns its input unchanged.
    /// <c>Goal.Grinding => CircuitTrace.Pass(plan, guid, "chose grind")</c></summary>
    public static T Pass<T>(T value, int guid, string description,
        [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        if (_mode != (int)TraceMode.Off)
            Record(guid, file, line, description, null, null);
        return value;
    }

    private static void Record(int guid, string file, int line, string desc, string? note, double? value)
    {
        var hit = new ProbeHit(SiteId(file, line, desc), Interlocked.Increment(ref _seq), note, value,
                               Environment.CurrentManagedThreadId);
        var ring = _rings.GetOrAdd(guid, static _ => new BotRing());
        lock (ring.Lock)
        {
            // Hits outside a tick bracket (bridge/chat threads) open an inter-tick segment.
            ring.Open ??= new TickSegment { Guid = guid, Kind = "inter", StartUtc = DateTime.UtcNow };
            ring.Open.Hits.Add(hit);
            if (ring.Open.Hits.Count >= OpenSegmentHitCap)
            {
                ring.Open.Kind = "overflow";
                SealOpen_NoLock(ring);
            }
        }
    }
}
