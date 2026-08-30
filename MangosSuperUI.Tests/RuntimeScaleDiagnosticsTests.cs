using MangosSuperUI.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MangosSuperUI.Tests;

public sealed class RuntimeScaleDiagnosticsTests
{
    [Fact]
    public void SmapsRollupParser_SeparatesAnonymousAndFileBackedMemory()
    {
        string[] lines =
        {
            "Rss:               12000 kB",
            "Pss:               11500 kB",
            "Private_Clean:       500 kB",
            "Private_Dirty:     11000 kB",
            "Anonymous:        10500 kB",
            "Swap:               250 kB"
        };

        LinuxProcessMemorySnapshot memory = Assert.IsType<LinuxProcessMemorySnapshot>(
            RuntimeScaleDiagnosticsService.ParseSmapsRollup(lines, fdCount: 735));

        Assert.Equal(12_000L * 1024, memory.RssBytes);
        Assert.Equal(10_500L * 1024, memory.AnonymousBytes);
        Assert.Equal(11_000L * 1024, memory.PrivateDirtyBytes);
        Assert.Equal(735, memory.OpenFileDescriptorCount);
    }

    [Fact]
    public void SmapsRollupParser_ReturnsNullWhenNoRssRecordExists()
    {
        Assert.Null(RuntimeScaleDiagnosticsService.ParseSmapsRollup(
            new[] { "Anonymous: 100 kB" }));
    }

    // ---- per-bot cost: the only figures in the report that extrapolate -------

    [Fact]
    public void PerBotCost_DividesEveryCostByTheConnectedFleet()
    {
        PerBotCostSnapshot cost = RuntimeScaleDiagnosticsService.BuildPerBotCost(
            connections: 692,
            rssBytes: 692L * 1024 * 1024,
            committedBytes: 692L * 512 * 1024,
            managedBytes: 692L * 256 * 1024,
            allocationRate: 692 * 100d,
            fileDescriptors: 1384);

        Assert.Equal(1024d * 1024, cost.RssBytesPerConnection);
        Assert.Equal(512d * 1024, cost.GcCommittedBytesPerConnection);
        Assert.Equal(256d * 1024, cost.ManagedBytesPerConnection);
        Assert.Equal(100d, cost.AllocationBytesPerSecondPerConnection);
        Assert.Equal(2d, cost.OpenFileDescriptorsPerConnection);
    }

    [Fact]
    public void PerBotCost_IsNullWithNoBotsSoZeroCostIsNeverImplied()
    {
        PerBotCostSnapshot cost = RuntimeScaleDiagnosticsService.BuildPerBotCost(
            connections: 0,
            rssBytes: 6L * 1024 * 1024 * 1024,
            committedBytes: 6L * 1024 * 1024 * 1024,
            managedBytes: 4L * 1024 * 1024 * 1024,
            allocationRate: 100d * 1024 * 1024,
            fileDescriptors: 40);

        Assert.Null(cost.RssBytesPerConnection);
        Assert.Null(cost.GcCommittedBytesPerConnection);
        Assert.Null(cost.ManagedBytesPerConnection);
        Assert.Null(cost.AllocationBytesPerSecondPerConnection);
        Assert.Null(cost.OpenFileDescriptorsPerConnection);
    }

    [Fact]
    public void PerBotCost_OmitsDescriptorsWhenTheHostDoesNotReportThem()
    {
        PerBotCostSnapshot cost = RuntimeScaleDiagnosticsService.BuildPerBotCost(
            connections: 10,
            rssBytes: 1000,
            committedBytes: 1000,
            managedBytes: 1000,
            allocationRate: 1000,
            fileDescriptors: null);

        Assert.Null(cost.OpenFileDescriptorsPerConnection);
        Assert.Equal(100d, cost.RssBytesPerConnection);
    }

    // ---- UI backlog high-water marks ----------------------------------------
    //
    // The instantaneous pending count is sampled every 30 s and the publisher
    // drains every 200 ms, so a backlog that forms and clears between samples was
    // invisible. The stop gate depends on these peaks, so they get their own
    // tests rather than riding along with the buffer's coalescing tests.

    [Fact]
    public void UiStateBuffer_PeakAgeSurvivesADrainThatEmptiedTheQueue()
    {
        var buffer = new BotStateUpdateBuffer();
        buffer.Enqueue(new BotState { Guid = 1, LastUpdate = DateTime.UtcNow.AddSeconds(-12) });

        Assert.Single(buffer.Drain(10));
        BotStateBatchMetrics metrics = buffer.GetMetrics();

        // Instantaneously clean, but the interval contained a 12-second backlog.
        Assert.Equal(0, metrics.PendingBotCount);
        Assert.Null(metrics.OldestPendingAgeSeconds);
        Assert.True(metrics.PeakPendingAgeSeconds >= 12, $"peak was {metrics.PeakPendingAgeSeconds}");
        Assert.Equal(1, metrics.PeakBatchSize);
        Assert.Equal(1, metrics.PeakPendingBotCount);
    }

    [Fact]
    public void UiStateBuffer_PeaksClearOnlyForTheResettingReader()
    {
        var buffer = new BotStateUpdateBuffer();
        for (int guid = 1; guid <= 4; guid++)
            buffer.Enqueue(new BotState { Guid = guid, LastUpdate = DateTime.UtcNow.AddSeconds(-3) });
        buffer.Drain(10);

        Assert.Equal(4, buffer.GetMetrics().PeakPendingBotCount);
        // A non-resetting read must not steal the spike from the sampler.
        Assert.Equal(4, buffer.GetMetrics().PeakPendingBotCount);

        BotStateBatchMetrics sampled = buffer.GetMetrics(resetPeaks: true);
        Assert.Equal(4, sampled.PeakPendingBotCount);
        Assert.True(sampled.PeaksWereReset);
        Assert.Equal(0, buffer.GetMetrics().PeakPendingBotCount);
    }

    [Fact]
    public void UiStateBuffer_WedgedPublisherStillReportsAGrowingPeakAge()
    {
        // Nothing is ever drained here: without folding the live queue into the
        // peak, a permanently stuck publisher would report a flat zero forever.
        var buffer = new BotStateUpdateBuffer();
        buffer.Enqueue(new BotState { Guid = 1, LastUpdate = DateTime.UtcNow.AddSeconds(-30) });

        BotStateBatchMetrics metrics = buffer.GetMetrics();

        Assert.Equal(1, metrics.PendingBotCount);
        Assert.True(metrics.PeakPendingAgeSeconds >= 30, $"peak was {metrics.PeakPendingAgeSeconds}");
    }

    [Fact]
    public void UiStateBuffer_DrainCyclesTrackPublisherLiveness()
    {
        var buffer = new BotStateUpdateBuffer();

        buffer.Drain(10);
        buffer.Drain(10);

        // An empty drain still counts: a stalled publisher is the failure this
        // counter exists to expose, and it produces no batches at all.
        Assert.Equal(2, buffer.GetMetrics().DrainCycles);
        Assert.Equal(0, buffer.GetMetrics().BatchesPublished);
    }

    // ---- brain loop timing ---------------------------------------------------

    [Fact]
    public void BrainLoopMetrics_ReportsMedianP95AndMax()
    {
        var metrics = new BrainLoopMetrics();
        for (int ms = 1; ms <= 100; ms++)
            metrics.RecordBrainTicks(TimeSpan.FromMilliseconds(ms));

        BrainLoopPhaseStats stats = metrics.GetStats().BrainTicks;

        Assert.Equal(100, stats.SampleCount);
        Assert.Equal(100, stats.Iterations);
        Assert.Equal(50, stats.MedianMilliseconds);
        Assert.Equal(95, stats.P95Milliseconds);
        Assert.Equal(100, stats.MaxMilliseconds);
        Assert.Equal(100, stats.LastMilliseconds);
    }

    [Fact]
    public void BrainLoopMetrics_PeakClearsOnlyForTheResettingReader()
    {
        var metrics = new BrainLoopMetrics();
        metrics.RecordRosterSync(TimeSpan.FromMilliseconds(250));
        metrics.RecordRosterSync(TimeSpan.FromMilliseconds(5));

        Assert.Equal(250, metrics.GetStats().RosterSync.PeakMillisecondsSinceLastRead);
        Assert.Equal(250, metrics.GetStats(resetPeaks: true).RosterSync.PeakMillisecondsSinceLastRead);
        Assert.Equal(0, metrics.GetStats().RosterSync.PeakMillisecondsSinceLastRead);

        // The rolling ring is not a peak window: it keeps its history.
        Assert.Equal(250, metrics.GetStats().RosterSync.MaxMilliseconds);
    }

    [Fact]
    public void BrainLoopMetrics_RingKeepsOnlyTheMostRecentSamples()
    {
        var metrics = new BrainLoopMetrics();
        for (int i = 0; i < BrainLoopMetrics.SampleCapacity + 50; i++)
            metrics.RecordLoopIteration(TimeSpan.FromMilliseconds(1));

        BrainLoopPhaseStats stats = metrics.GetStats().LoopIteration;

        Assert.Equal(BrainLoopMetrics.SampleCapacity, stats.SampleCount);
        Assert.Equal(BrainLoopMetrics.SampleCapacity + 50, stats.Iterations);
    }

    [Fact]
    public async Task BrainLoopMetrics_TimesAPhaseThatThrows()
    {
        var metrics = new BrainLoopMetrics();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BrainLoopMetrics.TimeAsync(
                metrics.RecordBrainTicks,
                () => throw new InvalidOperationException("planner blew up")));

        // The slow pass that precedes a failure is exactly the sample worth having.
        Assert.Equal(1, metrics.GetStats().BrainTicks.Iterations);
        Assert.Equal(1, metrics.GetStats().BrainTicks.SampleCount);
    }

    [Fact]
    public void BrainLoopMetrics_ReportsZeroesBeforeTheFirstLoopPass()
    {
        BrainLoopStats stats = new BrainLoopMetrics().GetStats();

        Assert.Equal(0, stats.TrackedContexts);
        Assert.Equal(0, stats.BrainTicks.SampleCount);
        Assert.Equal(0, stats.BrainTicks.P95Milliseconds);
    }

    // ---- live-heap probe -----------------------------------------------------

    [Fact]
    public void LiveHeapProbe_MeasuresOnceThenThrottlesWithThePreviousResult()
    {
        var bridge = new BotBridgeService(NullLogger<BotBridgeService>.Instance, hub: null!);
        var diagnostics = new RuntimeScaleDiagnosticsService(
            bridge,
            new BrainLoopMetrics(),
            NullLogger<RuntimeScaleDiagnosticsService>.Instance);

        LiveHeapProbeResult first = diagnostics.MeasureLiveHeap();

        Assert.True(first.Measured);
        LiveHeapMeasurement measurement = Assert.IsType<LiveHeapMeasurement>(first.Measurement);
        Assert.True(measurement.LiveBytes > 0);
        Assert.True(measurement.CollectionSeconds >= 0);
        // No bots connected in a unit test, so the per-bot slope must stay absent
        // rather than reporting a fabricated zero.
        Assert.Null(measurement.LiveBytesPerConnection);

        // A forced blocking gen2 collection is expensive enough that the rate
        // limit is a safety property, not a nicety.
        LiveHeapProbeResult second = diagnostics.MeasureLiveHeap();

        Assert.False(second.Measured);
        Assert.NotNull(second.ThrottleReason);
        Assert.NotNull(second.RetryAfterSeconds);
        Assert.True(second.RetryAfterSeconds <= RuntimeScaleDiagnosticsService.MinimumLiveHeapProbeInterval.TotalSeconds);
        Assert.Same(measurement, second.Measurement);
    }

    [Fact]
    public void LiveHeapProbe_IsCarriedOnTheReport()
    {
        var bridge = new BotBridgeService(NullLogger<BotBridgeService>.Instance, hub: null!);
        var diagnostics = new RuntimeScaleDiagnosticsService(
            bridge,
            new BrainLoopMetrics(),
            NullLogger<RuntimeScaleDiagnosticsService>.Instance);

        Assert.Null(diagnostics.GetReport().LastLiveHeapMeasurement);

        LiveHeapProbeResult probe = diagnostics.MeasureLiveHeap();

        Assert.Same(probe.Measurement, diagnostics.GetReport().LastLiveHeapMeasurement);
    }
}
