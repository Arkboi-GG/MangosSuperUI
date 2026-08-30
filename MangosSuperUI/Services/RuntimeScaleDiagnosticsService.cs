using System.Diagnostics;
using System.Globalization;
using System.Runtime;
using MangosSuperUI.BotLogic.Tracking;

namespace MangosSuperUI.Services;

public sealed record LinuxProcessMemorySnapshot(
    long RssBytes,
    long PssBytes,
    long AnonymousBytes,
    long PrivateCleanBytes,
    long PrivateDirtyBytes,
    long SwapBytes,
    int? OpenFileDescriptorCount);

/// <summary>
/// Cost per connected bot. These are the only figures in the report that
/// extrapolate: an absolute RSS at one fleet size says nothing about the next
/// step, whereas a stable per-bot slope across two bot counts is what turns a
/// 692-bot observation into a 5,000-bot projection. Null when no bots are
/// connected, so a divide-by-zero cannot masquerade as a zero cost.
/// </summary>
public sealed record PerBotCostSnapshot(
    int Connections,
    double? RssBytesPerConnection,
    double? GcCommittedBytesPerConnection,
    double? ManagedBytesPerConnection,
    double? AllocationBytesPerSecondPerConnection,
    double? OpenFileDescriptorsPerConnection);

public sealed record RuntimeScaleSnapshot(
    DateTime TimestampUtc,
    long ProcessWorkingSetBytes,
    long ProcessPrivateBytes,
    long ProcessVirtualBytes,
    int ProcessThreadCount,
    int? ProcessHandleCount,
    bool ServerGc,
    string GcLatencyMode,
    long ManagedAllocatedBytesEstimate,
    long GcHeapSizeBytes,
    long GcCommittedBytes,
    long GcFragmentedBytes,
    long GcMemoryLoadBytes,
    long GcHighMemoryLoadThresholdBytes,
    long GcTotalAvailableMemoryBytes,
    long LargeObjectHeapBytesAfterLastGc,
    long PinnedObjectHeapBytesAfterLastGc,
    long TotalAllocatedBytes,
    double AllocationRateBytesPerSecond,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections,
    long FinalizationPendingCount,
    long PinnedObjectCount,
    double LastGcPauseTimePercent,
    int ThreadPoolThreadCount,
    long ThreadPoolPendingWorkItems,
    long ThreadPoolCompletedWorkItems,
    int BridgeConnections,
    int TrackedBotStates,
    int StateBatchPublishIntervalMilliseconds,
    int StateBatchMaxSize,
    int UiPublishTimeoutMilliseconds,
    BotStateBatchMetrics StateBatching,
    BrainLoopStats BrainLoop,
    CircuitTrace.RetentionSnapshot CircuitTraceRetention,
    PerBotCostSnapshot PerBotCost,
    LinuxProcessMemorySnapshot? LinuxMemory);

public sealed record RuntimeScaleReport(
    RuntimeScaleSnapshot? Current,
    IReadOnlyList<RuntimeScaleSnapshot> Recent,
    int SampleIntervalSeconds,
    int MaximumHistorySamples,
    LiveHeapMeasurement? LastLiveHeapMeasurement);

/// <summary>
/// The result of one deliberate full compacting collection. This is the number
/// that decides whether a 5,000-bot target is reachable in one process at all:
/// the periodic snapshot reports in-use memory (uncollected garbage included)
/// and committed segments, neither of which is live data. DATAS and a lower
/// Server GC heap count both tune committed-versus-live; if live bytes per bot
/// is the wall, no GC setting moves it.
/// </summary>
public sealed record LiveHeapMeasurement(
    DateTime TimestampUtc,
    int BridgeConnections,
    long LiveBytes,
    double? LiveBytesPerConnection,
    long HeapSizeBytesAfterCompaction,
    long FragmentedBytesAfterCompaction,
    long CommittedBytesAfterCompaction,
    long WorkingSetBytesBefore,
    long WorkingSetBytesAfter,
    double CollectionSeconds,
    CircuitTrace.RetentionSnapshot CircuitTraceRetention);

public sealed record LiveHeapProbeResult(
    bool Measured,
    string? ThrottleReason,
    double? RetryAfterSeconds,
    LiveHeapMeasurement? Measurement);

/// <summary>
/// Cheap, bounded telemetry for answering the first scale question: is process
/// growth the managed heap, retained/fragmented GC segments, or other anonymous
/// memory?
///
/// The periodic sampler observes only — it never forces a collection or writes a
/// dump. <see cref="MeasureLiveHeap"/> is the one exception, and it is
/// operator-triggered, rate-limited, and never runs on the sampling path.
/// </summary>
public sealed class RuntimeScaleDiagnosticsService : BackgroundService
{
    internal const int SampleIntervalSeconds = 30;
    internal const int MaximumHistorySamples = 240;

    /// <summary>
    /// A full blocking compacting gen2 collection stalls every managed thread,
    /// which on a multi-GiB heap can mean seconds. Rate-limiting it keeps an
    /// enthusiastic poller from turning a diagnostic into an outage.
    /// </summary>
    internal static readonly TimeSpan MinimumLiveHeapProbeInterval = TimeSpan.FromMinutes(5);

    private readonly BotBridgeService _bridge;
    private readonly BrainLoopMetrics _brainLoop;
    private readonly ILogger<RuntimeScaleDiagnosticsService> _logger;
    private readonly object _historyGate = new();
    private readonly Queue<RuntimeScaleSnapshot> _history = new(MaximumHistorySamples);
    private readonly SemaphoreSlim _liveHeapProbeGate = new(1, 1);
    private long _previousAllocatedBytes;
    private long _previousSampleTimestamp;
    private bool _hasRateBaseline;
    private RuntimeScaleSnapshot? _current;
    private LiveHeapMeasurement? _lastLiveHeap;

    public RuntimeScaleDiagnosticsService(
        BotBridgeService bridge,
        BrainLoopMetrics brainLoop,
        ILogger<RuntimeScaleDiagnosticsService> logger)
    {
        _bridge = bridge;
        _brainLoop = brainLoop;
        _logger = logger;
    }

    public RuntimeScaleReport GetReport(int requestedHistorySamples = 60)
    {
        int take = Math.Clamp(requestedHistorySamples, 1, MaximumHistorySamples);
        lock (_historyGate)
        {
            RuntimeScaleSnapshot[] recent = _history
                .Skip(Math.Max(0, _history.Count - take))
                .ToArray();
            return new RuntimeScaleReport(
                _current,
                recent,
                SampleIntervalSeconds,
                MaximumHistorySamples,
                Volatile.Read(ref _lastLiveHeap));
        }
    }

    /// <summary>
    /// Force one full compacting collection and report the live heap. Throttled
    /// to <see cref="MinimumLiveHeapProbeInterval"/>; a throttled call returns
    /// the previous measurement rather than an error, so a polling caller still
    /// sees a usable number.
    /// </summary>
    public LiveHeapProbeResult MeasureLiveHeap()
    {
        if (!_liveHeapProbeGate.Wait(0))
        {
            return new LiveHeapProbeResult(
                Measured: false,
                ThrottleReason: "A live-heap probe is already running.",
                RetryAfterSeconds: null,
                Measurement: Volatile.Read(ref _lastLiveHeap));
        }

        try
        {
            LiveHeapMeasurement? previous = Volatile.Read(ref _lastLiveHeap);
            if (previous is not null)
            {
                TimeSpan since = DateTime.UtcNow - previous.TimestampUtc;
                if (since < MinimumLiveHeapProbeInterval)
                {
                    return new LiveHeapProbeResult(
                        Measured: false,
                        ThrottleReason:
                            $"A live-heap probe forces a blocking gen2 collection and is limited to one per {MinimumLiveHeapProbeInterval.TotalMinutes:F0} minutes.",
                        RetryAfterSeconds: (MinimumLiveHeapProbeInterval - since).TotalSeconds,
                        Measurement: previous);
                }
            }

            LiveHeapMeasurement measurement = CaptureLiveHeap();
            Volatile.Write(ref _lastLiveHeap, measurement);
            return new LiveHeapProbeResult(true, null, null, measurement);
        }
        finally
        {
            _liveHeapProbeGate.Release();
        }
    }

    private LiveHeapMeasurement CaptureLiveHeap()
    {
        using Process process = Process.GetCurrentProcess();
        process.Refresh();
        long workingSetBefore = process.WorkingSet64;
        int connections = _bridge.Connections.Count;

        long start = Stopwatch.GetTimestamp();

        // Two passes with finalizers drained in between: the first pass runs
        // finalizers, the second reclaims what they released. One pass alone
        // reports objects that are already unreachable as live.
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);

        double collectionSeconds = Stopwatch.GetElapsedTime(start).TotalSeconds;
        long liveBytes = GC.GetTotalMemory(forceFullCollection: false);

        GCMemoryInfo gc = GC.GetGCMemoryInfo();
        process.Refresh();

        var measurement = new LiveHeapMeasurement(
            TimestampUtc: DateTime.UtcNow,
            BridgeConnections: connections,
            LiveBytes: liveBytes,
            LiveBytesPerConnection: PerConnection(liveBytes, connections),
            HeapSizeBytesAfterCompaction: gc.HeapSizeBytes,
            FragmentedBytesAfterCompaction: gc.FragmentedBytes,
            CommittedBytesAfterCompaction: gc.TotalCommittedBytes,
            WorkingSetBytesBefore: workingSetBefore,
            WorkingSetBytesAfter: process.WorkingSet64,
            CollectionSeconds: collectionSeconds,
            CircuitTraceRetention: CircuitTrace.GetRetentionSnapshot());

        _logger.LogWarning(
            "ScaleDiag: forced live-heap probe took {Seconds:F2}s (all managed threads stalled) — live={LiveMiB:F0}MiB bots={Bots} perBot={PerBotKiB:F0}KiB rssBefore={BeforeMiB:F0}MiB rssAfter={AfterMiB:F0}MiB circuitRings={Rings} circuitMode={Mode}",
            measurement.CollectionSeconds,
            ToMiB(measurement.LiveBytes),
            measurement.BridgeConnections,
            (measurement.LiveBytesPerConnection ?? 0) / 1024d,
            ToMiB(measurement.WorkingSetBytesBefore),
            ToMiB(measurement.WorkingSetBytesAfter),
            measurement.CircuitTraceRetention.RingCount,
            measurement.CircuitTraceRetention.Mode);

        return measurement;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        int sampleNumber = 0;
        CaptureAndRecord(logSample: true);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(SampleIntervalSeconds));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                sampleNumber++;
                CaptureAndRecord(logSample: sampleNumber % 2 == 0);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal host shutdown.
        }
    }

    private void CaptureAndRecord(bool logSample)
    {
        try
        {
            RuntimeScaleSnapshot snapshot = CaptureSnapshot();
            lock (_historyGate)
            {
                _current = snapshot;
                _history.Enqueue(snapshot);
                while (_history.Count > MaximumHistorySamples)
                    _history.Dequeue();
            }

            if (logSample)
            {
                _logger.LogInformation(
                    "ScaleDiag: rss={RssMiB:F0}MiB private={PrivateMiB:F0}MiB managedAllocatedEstimate={ManagedMiB:F0}MiB gcCommitted={CommittedMiB:F0}MiB gcFragmented={FragmentedMiB:F0}MiB allocRate={AllocMiBPerSec:F1}MiB/s bots={Connections} rssPerBot={RssPerBotKiB:F0}KiB peakPendingUi={PeakPendingUi} peakUiAge={PeakUiAge:F1}s peakBatch={PeakBatch} brainTickP95={BrainP95:F0}ms brainTickPeak={BrainPeak:F0}ms circuitMode={CircuitMode} circuitRings={CircuitRings} circuitRetainedEst={CircuitRetainedMiB:F0}MiB fds={Fds}",
                    ToMiB(snapshot.ProcessWorkingSetBytes),
                    ToMiB(snapshot.ProcessPrivateBytes),
                    ToMiB(snapshot.ManagedAllocatedBytesEstimate),
                    ToMiB(snapshot.GcCommittedBytes),
                    ToMiB(snapshot.GcFragmentedBytes),
                    ToMiB(snapshot.AllocationRateBytesPerSecond),
                    snapshot.BridgeConnections,
                    (snapshot.PerBotCost.RssBytesPerConnection ?? 0) / 1024d,
                    snapshot.StateBatching.PeakPendingBotCount,
                    snapshot.StateBatching.PeakPendingAgeSeconds,
                    snapshot.StateBatching.PeakBatchSize,
                    snapshot.BrainLoop.BrainTicks.P95Milliseconds,
                    snapshot.BrainLoop.BrainTicks.PeakMillisecondsSinceLastRead,
                    snapshot.CircuitTraceRetention.Mode,
                    snapshot.CircuitTraceRetention.RingCount,
                    ToMiB(snapshot.CircuitTraceRetention.EstimatedRetainedBytes),
                    snapshot.LinuxMemory?.OpenFileDescriptorCount);
            }
        }
        catch (Exception ex)
        {
            // Diagnostics must never become a host-liveness dependency.
            _logger.LogWarning(ex, "ScaleDiag: sample failed");
        }
    }

    private RuntimeScaleSnapshot CaptureSnapshot()
    {
        DateTime timestampUtc = DateTime.UtcNow;
        long timestamp = Stopwatch.GetTimestamp();
        long totalAllocated = GC.GetTotalAllocatedBytes(precise: false);
        double allocationRate = 0;
        if (_hasRateBaseline)
        {
            double elapsedSeconds = (timestamp - _previousSampleTimestamp) / (double)Stopwatch.Frequency;
            if (elapsedSeconds > 0 && totalAllocated >= _previousAllocatedBytes)
                allocationRate = (totalAllocated - _previousAllocatedBytes) / elapsedSeconds;
        }
        _previousAllocatedBytes = totalAllocated;
        _previousSampleTimestamp = timestamp;
        _hasRateBaseline = true;

        GCMemoryInfo gc = GC.GetGCMemoryInfo();
        ReadOnlySpan<GCGenerationInfo> generations = gc.GenerationInfo;
        long lohBytes = generations.Length > 3 ? generations[3].SizeAfterBytes : 0;
        long pohBytes = generations.Length > 4 ? generations[4].SizeAfterBytes : 0;

        using Process process = Process.GetCurrentProcess();
        process.Refresh();
        int? handleCount = null;
        try { handleCount = process.HandleCount; }
        catch (PlatformNotSupportedException) { }
        catch (InvalidOperationException) { }

        LinuxProcessMemorySnapshot? linuxMemory = TryReadLinuxProcessMemory();
        int connections = _bridge.Connections.Count;
        long managedEstimate = GC.GetTotalMemory(forceFullCollection: false);

        // This sampler owns the "since last sample" window for every high-water
        // mark, so it is the only caller that resets them.
        BotStateBatchMetrics stateBatching = _bridge.GetStateBatchMetrics(resetPeaks: true);
        BrainLoopStats brainLoop = _brainLoop.GetStats(resetPeaks: true);

        return new RuntimeScaleSnapshot(
            TimestampUtc: timestampUtc,
            ProcessWorkingSetBytes: process.WorkingSet64,
            ProcessPrivateBytes: process.PrivateMemorySize64,
            ProcessVirtualBytes: process.VirtualMemorySize64,
            ProcessThreadCount: process.Threads.Count,
            ProcessHandleCount: handleCount,
            ServerGc: GCSettings.IsServerGC,
            GcLatencyMode: GCSettings.LatencyMode.ToString(),
            // This is an in-use estimate, not a post-collection live-object count:
            // uncollected garbage may still be included. MeasureLiveHeap() is the
            // only path that reports live data.
            ManagedAllocatedBytesEstimate: managedEstimate,
            GcHeapSizeBytes: gc.HeapSizeBytes,
            GcCommittedBytes: gc.TotalCommittedBytes,
            GcFragmentedBytes: gc.FragmentedBytes,
            GcMemoryLoadBytes: gc.MemoryLoadBytes,
            GcHighMemoryLoadThresholdBytes: gc.HighMemoryLoadThresholdBytes,
            GcTotalAvailableMemoryBytes: gc.TotalAvailableMemoryBytes,
            LargeObjectHeapBytesAfterLastGc: lohBytes,
            PinnedObjectHeapBytesAfterLastGc: pohBytes,
            TotalAllocatedBytes: totalAllocated,
            AllocationRateBytesPerSecond: allocationRate,
            Gen0Collections: GC.CollectionCount(0),
            Gen1Collections: GC.CollectionCount(1),
            Gen2Collections: GC.CollectionCount(2),
            FinalizationPendingCount: gc.FinalizationPendingCount,
            PinnedObjectCount: gc.PinnedObjectsCount,
            LastGcPauseTimePercent: gc.PauseTimePercentage,
            ThreadPoolThreadCount: ThreadPool.ThreadCount,
            ThreadPoolPendingWorkItems: ThreadPool.PendingWorkItemCount,
            ThreadPoolCompletedWorkItems: ThreadPool.CompletedWorkItemCount,
            BridgeConnections: connections,
            TrackedBotStates: _bridge.BotStates.Count,
            StateBatchPublishIntervalMilliseconds: _bridge.StateBatchPublishIntervalMilliseconds,
            StateBatchMaxSize: _bridge.StateBatchMaxSize,
            UiPublishTimeoutMilliseconds: _bridge.UiPublishTimeoutMilliseconds,
            StateBatching: stateBatching,
            BrainLoop: brainLoop,
            CircuitTraceRetention: CircuitTrace.GetRetentionSnapshot(),
            PerBotCost: BuildPerBotCost(
                connections,
                linuxMemory?.RssBytes ?? process.WorkingSet64,
                gc.TotalCommittedBytes,
                managedEstimate,
                allocationRate,
                linuxMemory?.OpenFileDescriptorCount),
            LinuxMemory: linuxMemory);
    }

    internal static PerBotCostSnapshot BuildPerBotCost(
        int connections,
        long rssBytes,
        long committedBytes,
        long managedBytes,
        double allocationRate,
        int? fileDescriptors)
        => new(
            Connections: connections,
            RssBytesPerConnection: PerConnection(rssBytes, connections),
            GcCommittedBytesPerConnection: PerConnection(committedBytes, connections),
            ManagedBytesPerConnection: PerConnection(managedBytes, connections),
            AllocationBytesPerSecondPerConnection: PerConnection(allocationRate, connections),
            OpenFileDescriptorsPerConnection: fileDescriptors.HasValue
                ? PerConnection(fileDescriptors.Value, connections)
                : null);

    private static double? PerConnection(double total, int connections)
        => connections > 0 ? total / connections : null;

    internal static LinuxProcessMemorySnapshot? ParseSmapsRollup(IEnumerable<string> lines, int? fdCount = null)
    {
        long rss = 0;
        long pss = 0;
        long anonymous = 0;
        long privateClean = 0;
        long privateDirty = 0;
        long swap = 0;

        foreach (string line in lines)
        {
            int colon = line.IndexOf(':');
            if (colon <= 0)
                continue;

            string key = line[..colon];
            string[] fields = line[(colon + 1)..]
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (fields.Length == 0
                || !long.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out long kib))
                continue;

            long bytes = checked(kib * 1024L);
            switch (key)
            {
                case "Rss": rss = bytes; break;
                case "Pss": pss = bytes; break;
                case "Anonymous": anonymous = bytes; break;
                case "Private_Clean": privateClean = bytes; break;
                case "Private_Dirty": privateDirty = bytes; break;
                case "Swap": swap = bytes; break;
            }
        }

        return rss == 0
            ? null
            : new LinuxProcessMemorySnapshot(
                rss,
                pss,
                anonymous,
                privateClean,
                privateDirty,
                swap,
                fdCount);
    }

    private static LinuxProcessMemorySnapshot? TryReadLinuxProcessMemory()
    {
        if (!OperatingSystem.IsLinux())
            return null;

        const string smapsPath = "/proc/self/smaps_rollup";
        try
        {
            int? fdCount = null;
            const string fdPath = "/proc/self/fd";
            try { fdCount = Directory.EnumerateFileSystemEntries(fdPath).Count(); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }

            return ParseSmapsRollup(File.ReadLines(smapsPath), fdCount);
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static double ToMiB(double bytes) => bytes / (1024d * 1024d);
}
