using System.Runtime.CompilerServices;
using MangosSuperUI.BotLogic.Core;

namespace MangosSuperUI.BotLogic.Tracking;

// ════════════════════════════════════════════════════════════════════════════
// CircuitTrace — the C# probe facade of the circuit board (docs/CIRCUIT_BOARD.md).
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
//   Off    — unarmed bots are one volatile read + armed-set lookup; explicitly
//            armed bots still record and flush. Off means fleet shadow is off.
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

    /// <summary>
    /// Sustained conditions are stamped at ingestion time so the activity reader can
    /// distinguish a lifecycle from a repeated point alarm. Only a structured C# tick
    /// may change this state; inter-tick and C++ segments may only attach to the latest
    /// still-fresh incident.
    /// </summary>
    public enum ConditionKind { None = 0, Dead = 1, Blocked = 2 }
    public enum ConditionTransition { None = 0, Onset = 1, Confirmation = 2, Clear = 3 }

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

    /// <summary>One registered call site. Session-scoped; the generated Map joins on File:Line.
    /// RemoteEpoch/RemoteId are populated only for C++ sites so a trace file carries the
    /// complete identity needed to decode sites reused after a core restart.</summary>
    public sealed record ProbeSite(
        int Id,
        string File,
        int Line,
        string Description,
        string? RemoteEpoch = null,
        int? RemoteId = null);

    /// <summary>A sealed slice of one bot's trace: the ordered hits of one tick (or the
    /// gap between ticks), stamped with when and — for real ticks — where in the world.</summary>
    public sealed class TickSegment
    {
        /// <summary>Stable arrival cursor for a frozen, steppable trace view.</summary>
        public long Seq;
        public int Guid;
        public string Kind = "tick";          // tick | inter | overflow
        public DateTime StartUtc;
        public DateTime EndUtc;
        /// <summary>The context that owns this path. Brain ticks stamp it before a
        /// bridge/chat thread can race in with the segment's first hit.</summary>
        public int PrimaryCtx;
        public bool HasPos;
        public int MapId = -1;
        public int ZoneId;
        public float X, Y, Z;
        public string? RemoteEpoch;
        // Structured state sampled at EndTick. Primitive fields keep the long-lived
        // activity reader independent from descriptions and from mutable BotContext.
        public int Goal = -1;
        public string? Step;
        public int TaskKind = -1;
        public int Activity = -1;
        public int TaskKills;
        public int ObjectiveKind = -1;
        public int ObjectiveSource = -1;
        public int ObjectiveQuestId;
        public int ObjectiveSlot;
        public int ObjectiveCreatureEntry;
        public int ObjectiveNpcEntry;
        public bool InCombat;
        public bool Dead;
        public bool HasStructuredState;
        public long ConditionIncidentId;
        public ConditionKind Condition;
        public ConditionTransition ConditionTransition;
        public bool HasPointAlarm;
        public List<ProbeHit> Hits = new(16);
    }

    /// <summary>A compact, stable decision run retained longer than raw tick history.
    /// Identical consecutive decision/state frames keep one newest representative while
    /// preserving their first/last cursor and raw segment count for drill-down context.</summary>
    public sealed record DecisionRunSnapshot(
        long Id,
        long ThroughSeq,
        DateTime StartUtc,
        DateTime EndUtc,
        int SegmentCount,
        TickSegment Representative)
    {
        public long ConditionIncidentId { get; init; }
        public ConditionKind Condition { get; init; }
        public ConditionTransition ConditionTransition { get; init; }
        public bool HasStructuredState { get; init; }
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

    /// <summary>
    /// Whether this bot is recording. Fleet-wide Shadow and explicit arming are
    /// independent controls: Off disables the fleet shadow, never an explicit arm.
    /// </summary>
    public static bool IsRecording(int guid)
        => _mode != (int)TraceMode.Off || _armedGuids.Contains(guid);

    /// <summary>Whether the host has any rings that must be serviced.</summary>
    public static bool HasActiveRecording
        => _mode != (int)TraceMode.Off || _armedGuids.Count != 0;

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
        public readonly LinkedList<DecisionRun> Decisions = new();
        public long DecisionsDropped;
        public ConditionKind OpenCondition;
        public long OpenConditionId;
        public DateTime LastStructuredConditionUtc;
    }
    // Keep the complete bounded shadow horizon readable. Recent contains references
    // to the same segment objects as Sealed, so matching the cap does not duplicate hits.
    private const int RecentViewCap = SegmentRingCap;
    private const int DecisionHistoryCap = 2048;
    private static readonly TimeSpan DecisionHistoryHorizon = TimeSpan.FromMinutes(20);
    private static readonly TimeSpan ConditionCarryHorizon = TimeSpan.FromSeconds(30);

    private sealed class DecisionRun
    {
        public required long Id;
        public required long ThroughSeq;
        public required DateTime StartUtc;
        public required DateTime EndUtc;
        public required int SegmentCount;
        public required TickSegment Representative;
        public required int[] Path;
        // The first structured frame after a recovery-phase change is retained as
        // its own teaching moment. Later identical samples may compact together,
        // but never into the transition frame itself.
        public bool StartsConditionPhase;
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, BotRing> _rings = new();
    private static long _seq;
    private static long _segmentSeq;
    // A managed thread id is not a control-flow id: TickAsync awaits and may resume
    // elsewhere. AsyncLocal follows that logical tick across awaits while unrelated
    // bridge/chat callbacks keep their own (negative) physical fallback context.
    private static readonly AsyncLocal<int> _logicalContext = new();
    private static int _nextLogicalContext;

    /// <summary>The host brackets each brain tick. An unsealed previous segment
    /// (missed EndTick — exception path) is sealed as-is so nothing is lost.</summary>
    public static void BeginTick(int guid)
    {
        if (!IsRecording(guid)) return;
        var ring = _rings.GetOrAdd(guid, static _ => new BotRing());
        lock (ring.Lock)
        {
            SealOpen_NoLock(ring);
            int flow = Interlocked.Increment(ref _nextLogicalContext);
            if (flow <= 0)
            {
                Interlocked.Exchange(ref _nextLogicalContext, 1);
                flow = 1;
            }
            _logicalContext.Value = flow;
            ring.Open = new TickSegment
            {
                Guid = guid,
                Kind = "tick",
                StartUtc = DateTime.UtcNow,
                PrimaryCtx = flow
            };
        }
    }

    /// <summary>Seal the current tick segment, stamping the bot's world position (R10).</summary>
    public static void EndTick(
        int guid,
        int mapId,
        int zoneId,
        float x,
        float y,
        float z,
        BotContext? context = null)
    {
        int closingContext = _logicalContext.Value;
        try
        {
            if (!_rings.TryGetValue(guid, out var ring)) return;
            lock (ring.Lock)
            {
                if (ring.Open == null) return;
                ring.Open.HasPos = true;
                ring.Open.MapId = mapId;
                ring.Open.ZoneId = zoneId;
                ring.Open.X = x; ring.Open.Y = y; ring.Open.Z = z;
                if (context != null)
                {
                    ring.Open.HasStructuredState = true;
                    ring.Open.Goal = (int)context.Goal;
                    ring.Open.Step = context.Step;
                    ring.Open.TaskKind = (int)context.HeldTask.Kind;
                    ring.Open.Activity = (int)context.HeldTask.Activity;
                    ring.Open.TaskKills = context.HeldTask.Kills;
                    ring.Open.InCombat = context.InCombat;
                    ring.Open.Dead = context.Dead;
                    if (context.Held is { } held)
                    {
                        ring.Open.ObjectiveKind = (int)held.Kind;
                        ring.Open.ObjectiveSource = (int)held.Source;
                        ring.Open.ObjectiveQuestId = held.QuestId;
                        ring.Open.ObjectiveSlot = held.Slot;
                        ring.Open.ObjectiveCreatureEntry = held.CreatureEntry;
                        ring.Open.ObjectiveNpcEntry = held.NpcEntry;
                    }
                }
                SealOpen_NoLock(ring);
            }
        }
        finally
        {
            if (_logicalContext.Value == closingContext)
                _logicalContext.Value = 0;
        }
    }

    private static void SealOpen_NoLock(BotRing ring)
    {
        var open = ring.Open;
        if (open == null) return;
        ring.Open = null;
        if (open.Hits.Count == 0 && open.Kind != "tick") return;   // empty inter-gap: drop silently
        open.Seq = Interlocked.Increment(ref _segmentSeq);
        open.EndUtc = DateTime.UtcNow;
        StampCondition_NoLock(ring, open);
        ring.Sealed.Enqueue(open);
        while (ring.Sealed.Count > SegmentRingCap) { ring.Sealed.Dequeue(); ring.Dropped++; }
        ring.Recent.Enqueue(open);
        while (ring.Recent.Count > RecentViewCap) ring.Recent.Dequeue();
        AddDecisionHistory_NoLock(ring, open);
    }

    private static void StampCondition_NoLock(BotRing ring, TickSegment segment)
    {
        if (segment.HasStructuredState)
        {
            ConditionKind observed = segment.Dead
                ? ConditionKind.Dead
                : segment.Activity == (int)TaskActivity.Blocked
                    ? ConditionKind.Blocked
                    : ConditionKind.None;
            ConditionKind prior = ring.OpenCondition;
            long priorId = ring.OpenConditionId;

            if (observed == prior)
            {
                if (observed != ConditionKind.None)
                {
                    segment.Condition = observed;
                    segment.ConditionIncidentId = priorId;
                    segment.ConditionTransition = ConditionTransition.Confirmation;
                }
            }
            else if (observed == ConditionKind.None)
            {
                // The first authoritative clear belongs to the incident it closes so
                // drill-down shows the literal transition back to routine behavior.
                if (prior != ConditionKind.None)
                {
                    segment.Condition = prior;
                    segment.ConditionIncidentId = priorId;
                    segment.ConditionTransition = ConditionTransition.Clear;
                }
                ring.OpenCondition = ConditionKind.None;
                ring.OpenConditionId = 0;
            }
            else
            {
                // A direct condition change (for example blocked -> dead) closes the
                // old incident at its previous observation and starts the new one here.
                ring.OpenCondition = observed;
                ring.OpenConditionId = segment.Seq;
                segment.Condition = observed;
                segment.ConditionIncidentId = segment.Seq;
                segment.ConditionTransition = ConditionTransition.Onset;
            }

            ring.LastStructuredConditionUtc = segment.EndUtc;
            return;
        }

        if (ring.OpenCondition == ConditionKind.None
            || ring.OpenConditionId == 0
            || ring.LastStructuredConditionUtc == default
            || segment.StartUtc - ring.LastStructuredConditionUtc > ConditionCarryHorizon)
            return;

        segment.Condition = ring.OpenCondition;
        segment.ConditionIncidentId = ring.OpenConditionId;
        segment.ConditionTransition = ConditionTransition.None;
    }

    private static void AddDecisionHistory_NoLock(BotRing ring, TickSegment segment)
    {
        int primary = segment.PrimaryCtx != 0
            ? segment.PrimaryCtx
            : segment.Hits.Count > 0 ? segment.Hits[0].Ctx : 0;
        int[] path = segment.Hits.Where(h => h.Ctx == primary).Select(h => h.SiteId).ToArray();
        DecisionRun? current = ring.Decisions.Last?.Value;

        if (current != null && !current.StartsConditionPhase && SameDecision(current, segment, path))
        {
            current.ThroughSeq = segment.Seq;
            current.EndUtc = segment.EndUtc;
            current.SegmentCount++;
            // Inter/C++ confirmations contribute duration/count evidence, but must
            // not replace the structured C# frame that explains the recovery phase.
            // A later structured confirmation is the freshest representative.
            if (segment.HasStructuredState || !current.Representative.HasStructuredState)
                current.Representative = segment;
        }
        else
        {
            bool startsConditionPhase = current != null
                && current.Representative.ConditionIncidentId != 0
                && current.Representative.ConditionIncidentId == segment.ConditionIncidentId
                && current.Representative.Condition == segment.Condition
                && current.Representative.HasStructuredState
                && segment.HasStructuredState
                && segment.ConditionTransition is ConditionTransition.None or ConditionTransition.Confirmation
                && !segment.HasPointAlarm
                && !SameConditionPhase(current.Representative, segment);
            ring.Decisions.AddLast(new DecisionRun
            {
                Id = segment.Seq,
                ThroughSeq = segment.Seq,
                StartUtc = segment.StartUtc,
                EndUtc = segment.EndUtc,
                SegmentCount = 1,
                Representative = segment,
                Path = path,
                StartsConditionPhase = startsConditionPhase
            });
        }

        DateTime cutoff = segment.EndUtc - DecisionHistoryHorizon;
        while (ring.Decisions.Count > DecisionHistoryCap
               || (ring.Decisions.First is { Value.EndUtc: var oldest } && oldest < cutoff))
        {
            ring.Decisions.RemoveFirst();
            ring.DecisionsDropped++;
        }
    }

    private static bool SameDecision(DecisionRun run, TickSegment segment, int[] path)
    {
        TickSegment prior = run.Representative;
        // A sustained condition is one human incident, even though its literal
        // control-flow path alternates between C#, inter-tick callbacks, and C++.
        // Compact only quiet confirmations. A point alarm deliberately breaks the
        // run so its exact source-bearing decision remains available as a child.
        if (prior.ConditionIncidentId != 0
            && prior.ConditionIncidentId == segment.ConditionIncidentId
            && prior.Condition == segment.Condition
            && prior.Condition != ConditionKind.None
            && prior.ConditionTransition is ConditionTransition.None or ConditionTransition.Confirmation
            && segment.ConditionTransition is ConditionTransition.None or ConditionTransition.Confirmation
            && !prior.HasPointAlarm
            && !segment.HasPointAlarm)
        {
            // Repeated samples inside one phase are cheap confirmations. A structured
            // phase change (rez_wait -> rez_sent, goal handoff, guard wait, and so on)
            // remains its own long-history run so the teaching/source view can show it.
            return !prior.HasStructuredState
                || !segment.HasStructuredState
                || SameConditionPhase(prior, segment);
        }

        return prior.Kind == segment.Kind
            && run.Path.AsSpan().SequenceEqual(path)
            && prior.ConditionIncidentId == segment.ConditionIncidentId
            && prior.Condition == segment.Condition
            && prior.ConditionTransition == segment.ConditionTransition
            && prior.Goal == segment.Goal
            && prior.Step == segment.Step
            && prior.TaskKind == segment.TaskKind
            && prior.Activity == segment.Activity
            && prior.TaskKills == segment.TaskKills
            && prior.ObjectiveKind == segment.ObjectiveKind
            && prior.ObjectiveSource == segment.ObjectiveSource
            && prior.ObjectiveQuestId == segment.ObjectiveQuestId
            && prior.ObjectiveSlot == segment.ObjectiveSlot
            && prior.ObjectiveCreatureEntry == segment.ObjectiveCreatureEntry
            && prior.ObjectiveNpcEntry == segment.ObjectiveNpcEntry
            && prior.InCombat == segment.InCombat
            && prior.Dead == segment.Dead;
    }

    private static bool SameConditionPhase(TickSegment prior, TickSegment current)
        => prior.Goal == current.Goal
            && prior.Step == current.Step
            && prior.TaskKind == current.TaskKind
            && prior.Activity == current.Activity
            && prior.ObjectiveKind == current.ObjectiveKind
            && prior.ObjectiveSource == current.ObjectiveSource
            && prior.ObjectiveQuestId == current.ObjectiveQuestId
            && prior.ObjectiveSlot == current.ObjectiveSlot
            && prior.ObjectiveCreatureEntry == current.ObjectiveCreatureEntry
            && prior.ObjectiveNpcEntry == current.ObjectiveNpcEntry;

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

    /// <summary>
    /// Report the retained view horizon and all eviction accounting for one bot.
    /// The Recent queue survives DrainSealed, so this is the honest pre-wedge
    /// depth even when the armed flush pump has emptied the disk queue.
    /// </summary>
    public static (int Depth, DateTime FromUtc, DateTime ToUtc, long Dropped, long DecisionsDropped)
        RingDepth(int guid)
    {
        if (!_rings.TryGetValue(guid, out var ring))
            return (0, default, default, 0, 0);
        lock (ring.Lock)
        {
            int depth = ring.Recent.Count;
            DateTime from = depth > 0 ? ring.Recent.Peek().StartUtc : default;
            DateTime to = depth > 0 ? ring.Recent.Last().EndUtc : default;
            return (depth, from, to, ring.Dropped, ring.DecisionsDropped);
        }
    }

    /// <summary>Drop a bot's ring entirely (bot evicted/deleted).</summary>
    public static void Forget(int guid) => _rings.TryRemove(guid, out _);

    /// <summary>
    /// Seal any open segment, return every still-unflushed segment, and remove
    /// the ring. Unlike Forget, this preserves a retiring bot's final evidence.
    /// </summary>
    public static List<TickSegment> SealAndForget(int guid)
    {
        if (!_rings.TryRemove(guid, out var ring)) return new List<TickSegment>();
        lock (ring.Lock)
        {
            SealOpen_NoLock(ring);
            var outList = new List<TickSegment>(ring.Sealed.Count);
            while (ring.Sealed.Count > 0) outList.Add(ring.Sealed.Dequeue());
            return outList;
        }
    }

    /// <summary>Bots currently holding trace rings (for status display).</summary>
    public static int RingCount => _rings.Count;

    /// <summary>
    /// What the shadow rings are actually retaining right now — the scale
    /// report needs this because Shadow mode is per-bot retention that grows
    /// with the fleet and was previously invisible to every memory metric.
    /// Each ring can hold SegmentRingCap sealed segments plus DecisionHistoryCap
    /// decision runs, so at a few hundred bots this is a first-order term in the
    /// managed heap, not a rounding error.
    /// </summary>
    /// <param name="DecisionRetainedSegments">
    /// Segments held ONLY by <c>DecisionRun.Representative</c>, i.e. already
    /// evicted from the segment ring but still reachable through the decision
    /// history. Missing these is what made the first version of this metric
    /// understate circuit-trace retention by roughly 8x: the ring cap bounds
    /// SegmentRingCap segments per bot, but the decision history can pin up to
    /// DecisionHistoryCap more on top of it.
    /// </param>
    /// <param name="EstimatedRetainedBytes">
    /// Estimated, not measured. Per-object sizes are order-of-magnitude
    /// constants for 64-bit; the two things that actually vary — hits per
    /// segment and decision path length — are counted exactly on a bounded
    /// sample of rings and extrapolated. Walking every ring in full would hold
    /// each ring lock against the live probe path for no diagnostic gain.
    /// Read it as "hundreds of MiB or tens", never as an exact figure.
    /// </param>
    public readonly record struct RetentionSnapshot(
        string Mode,
        int RingCount,
        long SealedSegments,
        long RecentSegments,
        long DecisionRuns,
        long DecisionRetainedSegments,
        long DroppedSegments,
        int SampledRings,
        double AverageHitsPerSampledSegment,
        double AveragePathLengthPerSampledDecision,
        long EstimatedRetainedBytes);

    // Order-of-magnitude 64-bit object sizes. See EstimatedRetainedBytes.
    /// <summary>One ProbeHit in a List slot.</summary>
    private const int ApproximateHitBytes = 48;
    /// <summary>TickSegment object plus its List header and array header, excluding hits.</summary>
    private const int ApproximateSegmentBytes = 224;
    /// <summary>DecisionRun object plus its LinkedList node.</summary>
    private const int ApproximateDecisionBytes = 112;
    /// <summary>Header of the DecisionRun Path array, excluding its elements.</summary>
    private const int ApproximateArrayHeaderBytes = 24;

    /// <summary>Rings sampled for the per-segment and per-decision averages.</summary>
    internal const int RetentionSampleRings = 8;

    public static RetentionSnapshot GetRetentionSnapshot()
    {
        string mode = Mode.ToString();
        long sealedSegments = 0, recentSegments = 0, decisionRuns = 0, dropped = 0;
        int sampledRings = 0;
        long sampledSegments = 0, sampledHits = 0;
        long sampledDecisions = 0, sampledDecisionOnlySegments = 0, sampledPathLength = 0;

        foreach (var entry in _rings)
        {
            var ring = entry.Value;
            bool sampleThisRing = sampledRings < RetentionSampleRings;
            lock (ring.Lock)
            {
                sealedSegments += ring.Sealed.Count;
                recentSegments += ring.Recent.Count;
                decisionRuns += ring.Decisions.Count;
                dropped += ring.Dropped;

                if (!sampleThisRing)
                    continue;

                sampledRings++;

                // Reference identity: TickSegment is a class, so the default
                // comparer is what we want — two distinct segments must never
                // collapse together just because their fields match.
                var live = new HashSet<TickSegment>(ring.Recent.Count);
                foreach (var segment in ring.Recent)
                {
                    live.Add(segment);
                    sampledSegments++;
                    sampledHits += segment.Hits.Count;
                }

                foreach (var decision in ring.Decisions)
                {
                    sampledDecisions++;
                    sampledPathLength += decision.Path.Length;

                    // Only representatives the ring has already evicted add new
                    // bytes; the rest are the same objects counted above.
                    if (live.Add(decision.Representative))
                    {
                        sampledDecisionOnlySegments++;
                        sampledSegments++;
                        sampledHits += decision.Representative.Hits.Count;
                    }
                }
            }
        }

        double averageHits = sampledSegments > 0 ? sampledHits / (double)sampledSegments : 0;
        double averagePath = sampledDecisions > 0 ? sampledPathLength / (double)sampledDecisions : 0;

        // Extrapolate the sampled "how many decisions pin an evicted segment"
        // ratio across the fleet. Rings fill at the same cadence, so a bounded
        // sample tracks the whole population closely.
        double decisionOnlyRatio = sampledDecisions > 0
            ? sampledDecisionOnlySegments / (double)sampledDecisions
            : 0;
        long decisionRetainedSegments = (long)(decisionRuns * decisionOnlyRatio);

        // Recent holds the same objects as Sealed, so segments are counted once
        // from Recent, plus the evicted ones the decision history still pins.
        double segmentBytes = ApproximateSegmentBytes + (averageHits * ApproximateHitBytes);
        double decisionBytes = ApproximateDecisionBytes + ApproximateArrayHeaderBytes + (averagePath * sizeof(int));
        long estimatedRetainedBytes =
            (long)(((recentSegments + decisionRetainedSegments) * segmentBytes)
                   + (decisionRuns * decisionBytes));

        return new RetentionSnapshot(
            Mode: mode,
            RingCount: _rings.Count,
            SealedSegments: sealedSegments,
            RecentSegments: recentSegments,
            DecisionRuns: decisionRuns,
            DecisionRetainedSegments: decisionRetainedSegments,
            DroppedSegments: dropped,
            SampledRings: sampledRings,
            AverageHitsPerSampledSegment: averageHits,
            AveragePathLengthPerSampledDecision: averagePath,
            EstimatedRetainedBytes: estimatedRetainedBytes);
    }

    // ── chains (R2) + the C++ side's remote sites / segments (R1, R3) ──────
    // BridgeCorrelation owns behavioral cbt allocation. CircuitTrace records
    // those ids as probe VALUES but must never maintain a second/colliding id
    // generator of its own.

    // Remote (C++) probe sites live in their own id space (rule R3): shifted by
    // RemoteSiteBase so they can never collide with local session ids. The wire's
    // compact remote id is process-epoch-local, however, so it is NEVER itself a
    // valid host site id. Each (epoch, remote id) receives a unique host id.
    public const int RemoteSiteBase = 100000;
    private static readonly object _remoteSiteLock = new();
    private static readonly Dictionary<(string Epoch, int RemoteId), RemoteSiteBinding> _remoteSites = new();
    private static readonly Dictionary<(string Epoch, int RemoteId), ProbeSite> _unknownRemoteSites = new();
    private static int _nextRemoteSiteId;

    private sealed class RemoteSiteBinding
    {
        public required ProbeSite Site { get; init; }
        public ProbeSite? ConflictSite { get; set; }
        public bool Conflicted => ConflictSite != null;
    }

    /// <summary>Copy the longer compressed decision horizon, oldest first.</summary>
    public static (List<DecisionRunSnapshot> Runs, bool Truncated) PeekDecisionRuns(
        int guid,
        int maxRuns = DecisionHistoryCap)
    {
        if (!_rings.TryGetValue(guid, out var ring))
            return (new List<DecisionRunSnapshot>(), false);

        lock (ring.Lock)
        {
            int take = Math.Clamp(maxRuns, 1, DecisionHistoryCap);
            int skip = Math.Max(0, ring.Decisions.Count - take);
            var runs = ring.Decisions.Skip(skip).Select(d => new DecisionRunSnapshot(
                d.Id,
                d.ThroughSeq,
                d.StartUtc,
                d.EndUtc,
                d.SegmentCount,
                d.Representative)
            {
                ConditionIncidentId = d.Representative.ConditionIncidentId,
                Condition = d.Representative.Condition,
                ConditionTransition = d.Representative.ConditionTransition,
                HasStructuredState = d.Representative.HasStructuredState
            }).ToList();
            return (runs, ring.DecisionsDropped > 0 || skip > 0);
        }
    }

    public enum RemoteSiteRegistration
    {
        Added,
        AlreadyRegistered,
        Conflict
    }

    public readonly record struct RemoteIngestResult(int UnknownSites, int ConflictedSites);

    /// <summary>
    /// Register one process-epoch-local C++ site. Re-shipping the same manifest is
    /// idempotent. Reusing an id for different metadata inside one claimed epoch is
    /// quarantined onto an explicit conflict site; it can never inherit the old label.
    /// </summary>
    public static RemoteSiteRegistration RegisterRemoteSite(
        string remoteEpoch,
        int remoteId,
        string file,
        int line,
        string desc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(remoteEpoch);
        var key = (remoteEpoch, remoteId);
        lock (_remoteSiteLock)
        {
            if (!_remoteSites.TryGetValue(key, out var binding))
            {
                var site = CreateRemoteSite_NoLock(remoteEpoch, remoteId, "cpp/" + file, line, desc);
                _remoteSites.Add(key, new RemoteSiteBinding { Site = site });
                return RemoteSiteRegistration.Added;
            }

            if (!binding.Conflicted
                && binding.Site.File == "cpp/" + file
                && binding.Site.Line == line
                && binding.Site.Description == desc)
                return RemoteSiteRegistration.AlreadyRegistered;

            if (!binding.Conflicted)
            {
                string conflict =
                    $"C++ site id {remoteId} reused inside epoch {remoteEpoch}: " +
                    $"{binding.Site.File}:{binding.Site.Line} '{binding.Site.Description}' != " +
                    $"cpp/{file}:{line} '{desc}'";
                binding.ConflictSite = CreateRemoteSite_NoLock(
                    remoteEpoch,
                    remoteId,
                    "cpp/<circuit-site-conflict>",
                    remoteId,
                    conflict);
            }
            return RemoteSiteRegistration.Conflict;
        }
    }

    private static ProbeSite CreateRemoteSite_NoLock(
        string remoteEpoch,
        int remoteId,
        string file,
        int line,
        string desc)
    {
        var site = new ProbeSite(
            RemoteSiteBase + Interlocked.Increment(ref _nextRemoteSiteId),
            file,
            line,
            desc,
            remoteEpoch,
            remoteId);
        lock (_siteList)
        {
            _siteList.Add(site);
            _siteListIds.Add(site.Id);
        }
        return site;
    }

    private static ProbeSite ResolveRemoteSite_NoLock(
        string remoteEpoch,
        int remoteId,
        out bool unknown,
        out bool conflicted)
    {
        var key = (remoteEpoch, remoteId);
        if (_remoteSites.TryGetValue(key, out var binding))
        {
            unknown = false;
            conflicted = binding.Conflicted;
            return binding.ConflictSite ?? binding.Site;
        }

        unknown = true;
        conflicted = false;
        if (_unknownRemoteSites.TryGetValue(key, out var placeholder)) return placeholder;

        placeholder = CreateRemoteSite_NoLock(
            remoteEpoch,
            remoteId,
            "cpp/<unregistered-circuit-site>",
            remoteId,
            $"C++ site id {remoteId} used before its manifest in epoch {remoteEpoch}");
        _unknownRemoteSites.Add(key, placeholder);
        return placeholder;
    }

    /// <summary>Merge one C++ CIRCUIT_BATCH into the bot's timeline as a sealed
    /// "cpp" segment (position-stamped per R10). Hits get fresh global seqs at
    /// arrival — ordering across the two sides is by arrival, which the 1s ship
    /// cadence makes honest enough until chains take over.</summary>
    public static RemoteIngestResult IngestRemoteSegment(
        string remoteEpoch,
        int guid,
        int mapId,
        int zoneId,
        float x,
        float y,
        float z,
        List<(int RemoteId, double? Value, string? Note)> hits, int drops)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(remoteEpoch);
        if (!IsRecording(guid)) return default;
        var seg = new TickSegment
        {
            Seq = Interlocked.Increment(ref _segmentSeq),
            Guid = guid,
            Kind = drops > 0 ? "cpp-drops" : "cpp",
            StartUtc = DateTime.UtcNow,
            EndUtc = DateTime.UtcNow,
            PrimaryCtx = RemoteCtx,
            HasPos = true,
            MapId = mapId,
            ZoneId = zoneId,
            X = x, Y = y, Z = z,
            RemoteEpoch = remoteEpoch,
            HasPointAlarm = drops > 0,
        };
        int unknownSites = 0;
        int conflictedSites = 0;
        lock (_remoteSiteLock)
        {
            foreach (var h in hits)
            {
                ProbeSite site = ResolveRemoteSite_NoLock(
                    remoteEpoch,
                    h.RemoteId,
                    out bool unknown,
                    out bool conflicted);
                if (unknown) unknownSites++;
                if (conflicted) conflictedSites++;
                if (IsPointAlarmText(site.Description, site.File, h.Note))
                    seg.HasPointAlarm = true;
                seg.Hits.Add(new ProbeHit(site.Id, Interlocked.Increment(ref _seq), h.Note, h.Value, RemoteCtx));
            }
        }

        var ring = _rings.GetOrAdd(guid, static _ => new BotRing());
        lock (ring.Lock)
        {
            StampCondition_NoLock(ring, seg);
            ring.Sealed.Enqueue(seg);
            while (ring.Sealed.Count > SegmentRingCap) { ring.Sealed.Dequeue(); ring.Dropped++; }
            ring.Recent.Enqueue(seg);
            while (ring.Recent.Count > RecentViewCap) ring.Recent.Dequeue();
            // The activity reader has a longer, run-compressed horizon than the
            // raw ring. C++ activity is part of the same human decision flow, so
            // it must enter that history too (the timeline builder carries the
            // latest structured C# objective across these remote segments).
            AddDecisionHistory_NoLock(ring, seg);
        }
        return new RemoteIngestResult(unknownSites, conflictedSites);
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
        if (!IsRecording(guid)) return;

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
        if (!IsRecording(guid)) return;
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
        if (!IsRecording(guid)) return;
        Record(guid, file, line, description, null, null);
    }

    /// <summary>Probe carrying the value the branch looked at (count, distance, percent…).</summary>
    public static void Hit(int guid, string description, double value,
        [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        if (!IsRecording(guid)) return;
        Record(guid, file, line, description, null, value);
    }

    /// <summary>Probe carrying a short state word (goal name, reason code). Distinct name, not an
    /// overload: a third string argument on Hit would be ambiguous against the caller-info
    /// parameters. Pass an ALREADY-BUILT string (never interpolate at the call site).</summary>
    public static void HitNote(int guid, string description, string note,
        [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        if (!IsRecording(guid)) return;
        Record(guid, file, line, description, note, null);
    }

    /// <summary>Expression-position probe for switch arms and ternaries: returns its input unchanged.
    /// <c>Goal.Grinding => CircuitTrace.Pass(plan, guid, "chose grind")</c></summary>
    public static T Pass<T>(T value, int guid, string description,
        [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
    {
        if (IsRecording(guid))
            Record(guid, file, line, description, null, null);
        return value;
    }

    private static void Record(int guid, string file, int line, string desc, string? note, double? value)
    {
        int context = _logicalContext.Value;
        if (context == 0)
            context = -1 - Environment.CurrentManagedThreadId; // -1 is reserved for C++
        var hit = new ProbeHit(SiteId(file, line, desc), Interlocked.Increment(ref _seq), note, value,
                               context);
        var ring = _rings.GetOrAdd(guid, static _ => new BotRing());
        lock (ring.Lock)
        {
            // Hits outside a tick bracket (bridge/chat threads) open an inter-tick segment.
            ring.Open ??= new TickSegment
            {
                Guid = guid,
                Kind = "inter",
                StartUtc = DateTime.UtcNow,
                PrimaryCtx = context
            };
            ring.Open.Hits.Add(hit);
            if (IsPointAlarmText(desc, file, note)) ring.Open.HasPointAlarm = true;
            if (ring.Open.Hits.Count >= OpenSegmentHitCap)
            {
                ring.Open.Kind = "overflow";
                SealOpen_NoLock(ring);
            }
        }
    }

    internal static bool IsPointAlarmText(string description, string file, string? note)
    {
        string text = string.Join(' ', description, file, note ?? "").ToUpperInvariant();
        return text.Contains("TRIPPED", StringComparison.Ordinal)
            || text.Contains("GIVEUP", StringComparison.Ordinal)
            || text.Contains("GIVE UP", StringComparison.Ordinal)
            || (text.Contains("ENGAGED", StringComparison.Ordinal)
                && text.Contains("QUARANTINE", StringComparison.Ordinal))
            || (text.Contains("FROZEN", StringComparison.Ordinal)
                && text.Contains("WHOLE WINDOW", StringComparison.Ordinal))
            || text.Contains("MOVE_FAILED", StringComparison.Ordinal)
            || text.Contains("MOVE FAILED", StringComparison.Ordinal)
            || text.Contains("PATH_UNSAFE", StringComparison.Ordinal)
            || text.Contains("PATH UNSAFE", StringComparison.Ordinal)
            || text.Contains("GRIND_BLOCKED", StringComparison.Ordinal)
            || text.Contains("GRIND BLOCKED", StringComparison.Ordinal)
            || (text.Contains("SITE", StringComparison.Ordinal)
                && text.Contains("CONFLICT", StringComparison.Ordinal))
            || text.Contains("UNREGISTERED", StringComparison.Ordinal);
    }
}
