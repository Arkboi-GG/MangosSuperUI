using System.Collections.Concurrent;

namespace MangosSuperUI.Services;

/// <summary>
/// Counters for the latest-wins UI state buffer.
///
/// Two different kinds of number live here, and mixing them up is what made the
/// first 692-bot soak unfalsifiable:
///
/// * The lifetime totals (Queued/Coalesced/Published/Failures/Requeued/Drains)
///   never reset, so two samples can be diffed.
/// * The Peak* values are high-water marks recorded on every drain cycle
///   (~5 Hz) and cleared when the sampler reads them. The instantaneous
///   <see cref="PendingBotCount"/> and <see cref="OldestPendingAgeSeconds"/> are
///   sampled every 30 s, which cannot see a two-second backlog that clears in
///   between — "0 pending states" was sampling luck, not proof. The stop gate
///   (UI pending states older than ten seconds) must be judged on the peaks.
/// </summary>
public sealed record BotStateBatchMetrics(
    int PendingBotCount,
    double? OldestPendingAgeSeconds,
    long StateUpdatesQueued,
    long StateUpdatesCoalesced,
    long BatchesPublished,
    long StatesPublished,
    long PublishFailures,
    long StatesRequeued,
    long DrainCycles,
    int PeakPendingBotCount,
    double PeakPendingAgeSeconds,
    int PeakBatchSize,
    bool PeaksWereReset);

/// <summary>
/// Bounds UI backpressure to one immutable state per bot. A slow or absent web
/// client can no longer make the TCP reader await one SignalR send per STATE.
/// </summary>
internal sealed class BotStateUpdateBuffer
{
    private readonly ConcurrentDictionary<int, BotState> _pending = new();
    private readonly ConcurrentDictionary<int, byte> _scheduled = new();
    private readonly ConcurrentQueue<int> _readyGuids = new();
    private long _stateUpdatesQueued;
    private long _stateUpdatesCoalesced;
    private long _batchesPublished;
    private long _statesPublished;
    private long _publishFailures;
    private long _statesRequeued;
    private long _drainCycles;

    // High-water marks since the last resetting read. Recorded at drain cadence
    // rather than at sample cadence so a short spike cannot hide between samples.
    private long _peakPendingBotCount;
    private long _peakPendingAgeTicks;
    private long _peakBatchSize;

    public int PendingCount => _pending.Count;

    public void Enqueue(BotState snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Guid <= 0)
            throw new ArgumentOutOfRangeException(nameof(snapshot), "A queued bot state must have a positive guid.");

        Interlocked.Increment(ref _stateUpdatesQueued);
        if (!_pending.TryAdd(snapshot.Guid, snapshot))
        {
            Interlocked.Increment(ref _stateUpdatesCoalesced);
            _pending.AddOrUpdate(
                snapshot.Guid,
                snapshot,
                (_, current) => Newest(current, snapshot));
        }

        Schedule(snapshot.Guid);
    }

    public IReadOnlyList<BotState> Drain(int maxBatchSize)
    {
        if (maxBatchSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxBatchSize));

        Interlocked.Increment(ref _drainCycles);

        // Depth before draining: this is the backlog the publisher actually found.
        RecordMax(ref _peakPendingBotCount, _pending.Count);

        DateTime utcNow = DateTime.UtcNow;
        var batch = new List<BotState>(Math.Min(maxBatchSize, _pending.Count));
        while (batch.Count < maxBatchSize && _readyGuids.TryDequeue(out int guid))
        {
            if (!_scheduled.TryRemove(guid, out _))
                continue;

            // Removing by key intentionally takes the newest value. A concurrent
            // update either becomes this value or schedules the guid again below.
            if (_pending.TryRemove(guid, out BotState? latest))
            {
                batch.Add(latest);
                RecordMax(ref _peakPendingAgeTicks, AgeTicks(latest, utcNow));
            }

            // Close the window where Enqueue saw the old scheduled marker and
            // therefore did not add another queue token.
            if (_pending.ContainsKey(guid))
                Schedule(guid);
        }

        RecordMax(ref _peakBatchSize, batch.Count);
        return batch;
    }

    public bool Remove(int guid)
    {
        bool removed = _pending.TryRemove(guid, out _);
        _scheduled.TryRemove(guid, out _);

        // Close the inverse of Drain's enqueue window: Enqueue may have added a
        // value while the old scheduled marker still existed, then Remove can
        // clear that marker without clearing the new value.
        if (_pending.ContainsKey(guid))
            Schedule(guid);

        return removed;
    }

    public void Requeue(IReadOnlyList<BotState> failedBatch)
    {
        foreach (BotState snapshot in failedBatch)
        {
            _pending.AddOrUpdate(
                snapshot.Guid,
                snapshot,
                (_, current) => Newest(current, snapshot));
            Schedule(snapshot.Guid);
            Interlocked.Increment(ref _statesRequeued);
        }
    }

    public void RecordPublished(int stateCount)
    {
        if (stateCount <= 0)
            return;

        Interlocked.Increment(ref _batchesPublished);
        Interlocked.Add(ref _statesPublished, stateCount);
    }

    public void RecordPublishFailure() => Interlocked.Increment(ref _publishFailures);

    /// <param name="resetPeaks">
    /// True only for the periodic scale sampler, which owns the "since last
    /// sample" window. Ad-hoc readers pass false so they cannot silently steal a
    /// spike out of the interval the sampler is about to report.
    /// </param>
    public BotStateBatchMetrics GetMetrics(bool resetPeaks = false)
    {
        DateTime utcNow = DateTime.UtcNow;
        double? oldestAgeSeconds = null;
        foreach (BotState snapshot in _pending.Values)
        {
            if (snapshot.LastUpdate == DateTime.MinValue)
                continue;
            double age = Math.Max(0, (utcNow - snapshot.LastUpdate).TotalSeconds);
            oldestAgeSeconds = !oldestAgeSeconds.HasValue || age > oldestAgeSeconds.Value
                ? age
                : oldestAgeSeconds;
        }

        // A backlog sitting in the buffer right now has not been drained yet, so
        // it has never reached the drain-time peak. Fold it in before reporting,
        // otherwise a permanently wedged publisher would read as a flat zero —
        // the exact failure this metric exists to catch.
        if (oldestAgeSeconds.HasValue)
            RecordMax(ref _peakPendingAgeTicks, (long)(oldestAgeSeconds.Value * TimeSpan.TicksPerSecond));
        RecordMax(ref _peakPendingBotCount, _pending.Count);

        long peakPendingBots = resetPeaks
            ? Interlocked.Exchange(ref _peakPendingBotCount, 0)
            : Interlocked.Read(ref _peakPendingBotCount);
        long peakAgeTicks = resetPeaks
            ? Interlocked.Exchange(ref _peakPendingAgeTicks, 0)
            : Interlocked.Read(ref _peakPendingAgeTicks);
        long peakBatch = resetPeaks
            ? Interlocked.Exchange(ref _peakBatchSize, 0)
            : Interlocked.Read(ref _peakBatchSize);

        return new BotStateBatchMetrics(
            PendingBotCount: _pending.Count,
            OldestPendingAgeSeconds: oldestAgeSeconds,
            StateUpdatesQueued: Interlocked.Read(ref _stateUpdatesQueued),
            StateUpdatesCoalesced: Interlocked.Read(ref _stateUpdatesCoalesced),
            BatchesPublished: Interlocked.Read(ref _batchesPublished),
            StatesPublished: Interlocked.Read(ref _statesPublished),
            PublishFailures: Interlocked.Read(ref _publishFailures),
            StatesRequeued: Interlocked.Read(ref _statesRequeued),
            DrainCycles: Interlocked.Read(ref _drainCycles),
            PeakPendingBotCount: (int)Math.Min(peakPendingBots, int.MaxValue),
            PeakPendingAgeSeconds: peakAgeTicks / (double)TimeSpan.TicksPerSecond,
            PeakBatchSize: (int)Math.Min(peakBatch, int.MaxValue),
            PeaksWereReset: resetPeaks);
    }

    private static long AgeTicks(BotState snapshot, DateTime utcNow)
    {
        if (snapshot.LastUpdate == DateTime.MinValue)
            return 0;
        long ticks = (utcNow - snapshot.LastUpdate).Ticks;
        return ticks > 0 ? ticks : 0;
    }

    /// <summary>Lock-free "keep the larger value" for a high-water mark.</summary>
    private static void RecordMax(ref long target, long candidate)
    {
        long current = Interlocked.Read(ref target);
        while (candidate > current)
        {
            long observed = Interlocked.CompareExchange(ref target, candidate, current);
            if (observed == current)
                return;
            current = observed;
        }
    }

    private void Schedule(int guid)
    {
        if (_scheduled.TryAdd(guid, 0))
            _readyGuids.Enqueue(guid);
    }

    private static BotState Newest(BotState current, BotState candidate)
        => candidate.LastUpdate >= current.LastUpdate ? candidate : current;
}
