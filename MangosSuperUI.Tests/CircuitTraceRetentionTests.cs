using MangosSuperUI.BotLogic.Tracking;
using MangosSuperUI.Services;
using Xunit;

namespace MangosSuperUI.Tests;

/// <summary>
/// GetRetentionSnapshot feeds the scale report's answer to "what is Shadow mode
/// costing us". Its first version counted only the segments still in the ring
/// and understated retention by roughly 8x, because DecisionRun.Representative
/// pins segments the ring has already evicted — up to DecisionHistoryCap more
/// per bot on top of SegmentRingCap.
///
/// Shares the flow tests' non-parallel collection: CircuitTrace state is static
/// and process-wide, so these counts are only meaningful in isolation.
/// </summary>
[Collection(CircuitTraceFlowCollection.Name)]
public sealed class CircuitTraceRetentionTests
{
    private const int SegmentRingCap = 1024;

    [Fact]
    public void HelloCircuitState_ShipsArmedBotWhileFleetShadowIsOff()
    {
        const int armedGuid = 14;
        const int unarmedGuid = 15;

        CircuitTrace.Mode = CircuitTrace.TraceMode.Off;
        CircuitTrace.Arm(armedGuid);
        CircuitTrace.Disarm(unarmedGuid);
        try
        {
            Assert.Equal((0, 1), BotBridgeService.CircuitStateForHello(armedGuid));
            Assert.Equal((0, 0), BotBridgeService.CircuitStateForHello(unarmedGuid));
        }
        finally
        {
            CircuitTrace.Disarm(armedGuid);
            CircuitTrace.Forget(armedGuid);
            CircuitTrace.Forget(unarmedGuid);
        }
    }

    /// <summary>
    /// Drives one tick whose probe path length varies with <paramref name="hits"/>.
    /// A differing path is what makes the next segment a NEW decision run rather
    /// than a compacted confirmation of the previous one.
    /// </summary>
    private static void DriveTick(int guid, int hits)
    {
        CircuitTrace.BeginTick(guid);
        for (int h = 0; h < hits; h++)
            CircuitTrace.Hit(guid, "retention-test: probe");
        CircuitTrace.EndTick(guid, 0, 0, 0f, 0f, 0f);
    }

    private static void AssertIsolated(CircuitTrace.RetentionSnapshot before)
    {
        // The decision-only ratio is extrapolated from a bounded ring sample, so
        // it is exact only when the sample covers every ring. A leaked ring from
        // another test would silently skew the numbers asserted below.
        Assert.Equal(0, before.RingCount);
    }

    [Fact]
    public void RetentionSnapshot_CountsSegmentsPinnedOnlyByDecisionHistory()
    {
        const int guid = 918_001;
        const int ticks = SegmentRingCap + 76;
        CircuitTrace.TraceMode priorMode = CircuitTrace.Mode;
        try
        {
            AssertIsolated(CircuitTrace.GetRetentionSnapshot());
            CircuitTrace.Mode = CircuitTrace.TraceMode.Shadow;

            for (int i = 0; i < ticks; i++)
                DriveTick(guid, 1 + (i % 3));

            CircuitTrace.RetentionSnapshot s = CircuitTrace.GetRetentionSnapshot();

            Assert.Equal(1, s.RingCount);
            Assert.Equal(SegmentRingCap, s.RecentSegments);   // ring is capped
            Assert.Equal(ticks, s.DecisionRuns);              // history is not

            // The 76 oldest decisions still pin segments the ring has evicted.
            // The old metric could not see these at all.
            Assert.Equal(ticks - SegmentRingCap, s.DecisionRetainedSegments);

            // Paths cycle 1,2,3 hits, so both averages sit inside that band.
            Assert.InRange(s.AverageHitsPerSampledSegment, 1.0, 3.0);
            Assert.InRange(s.AveragePathLengthPerSampledDecision, 1.0, 3.0);

            // Retention must exceed what the ring alone accounts for.
            long ringOnlyFloor = (long)(s.RecentSegments * s.AverageHitsPerSampledSegment * 48);
            Assert.True(
                s.EstimatedRetainedBytes > ringOnlyFloor,
                $"estimate {s.EstimatedRetainedBytes} should exceed ring-only floor {ringOnlyFloor}");
        }
        finally
        {
            CircuitTrace.Forget(guid);
            CircuitTrace.Mode = priorMode;
        }
    }

    [Fact]
    public void RetentionSnapshot_PinsNothingExtraWhileTheRingStillHoldsEverySegment()
    {
        const int guid = 918_002;
        const int ticks = 10;
        CircuitTrace.TraceMode priorMode = CircuitTrace.Mode;
        try
        {
            AssertIsolated(CircuitTrace.GetRetentionSnapshot());
            CircuitTrace.Mode = CircuitTrace.TraceMode.Shadow;

            for (int i = 0; i < ticks; i++)
                DriveTick(guid, 1 + (i % 3));

            CircuitTrace.RetentionSnapshot s = CircuitTrace.GetRetentionSnapshot();

            Assert.Equal(ticks, s.RecentSegments);
            Assert.Equal(ticks, s.DecisionRuns);
            // Every representative is still in the ring, so nothing is double counted.
            Assert.Equal(0, s.DecisionRetainedSegments);
            Assert.True(s.EstimatedRetainedBytes > 0);
            Assert.Equal("Shadow", s.Mode);
        }
        finally
        {
            CircuitTrace.Forget(guid);
            CircuitTrace.Mode = priorMode;
        }
    }

    [Fact]
    public void RetentionSnapshot_RecordsNothingWhileTracingIsOff()
    {
        const int guid = 918_003;
        CircuitTrace.TraceMode priorMode = CircuitTrace.Mode;
        try
        {
            AssertIsolated(CircuitTrace.GetRetentionSnapshot());
            CircuitTrace.Mode = CircuitTrace.TraceMode.Off;

            for (int i = 0; i < 50; i++)
                DriveTick(guid, 2);

            CircuitTrace.RetentionSnapshot s = CircuitTrace.GetRetentionSnapshot();

            Assert.Equal("Off", s.Mode);
            Assert.Equal(0, s.RingCount);
            Assert.Equal(0, s.RecentSegments);
            Assert.Equal(0, s.DecisionRuns);
            Assert.Equal(0, s.DecisionRetainedSegments);
            Assert.Equal(0, s.EstimatedRetainedBytes);
        }
        finally
        {
            CircuitTrace.Forget(guid);
            CircuitTrace.Mode = priorMode;
        }
    }

    [Fact]
    public void ArmedBot_RecordsWhileFleetShadowIsOff_AndUnarmedBotDoesNot()
    {
        const int armedGuid = 918_004;
        const int unarmedGuid = 918_005;
        CircuitTrace.TraceMode priorMode = CircuitTrace.Mode;
        try
        {
            AssertIsolated(CircuitTrace.GetRetentionSnapshot());
            CircuitTrace.Mode = CircuitTrace.TraceMode.Off;
            CircuitTrace.Arm(armedGuid);

            DriveTick(armedGuid, 2);
            DriveTick(unarmedGuid, 2);

            Assert.True(CircuitTrace.IsRecording(armedGuid));
            Assert.False(CircuitTrace.IsRecording(unarmedGuid));
            Assert.Single(CircuitTrace.PeekSegments(armedGuid));
            Assert.Empty(CircuitTrace.PeekSegments(unarmedGuid));
        }
        finally
        {
            CircuitTrace.Disarm(armedGuid);
            CircuitTrace.Forget(armedGuid);
            CircuitTrace.Forget(unarmedGuid);
            CircuitTrace.Mode = priorMode;
        }
    }

    [Fact]
    public void RetentionSnapshot_SamplesAtMostTheBoundedRingCount()
    {
        int[] guids = Enumerable.Range(918_010, CircuitTrace.RetentionSampleRings + 4).ToArray();
        CircuitTrace.TraceMode priorMode = CircuitTrace.Mode;
        try
        {
            AssertIsolated(CircuitTrace.GetRetentionSnapshot());
            CircuitTrace.Mode = CircuitTrace.TraceMode.Shadow;

            foreach (int guid in guids)
                DriveTick(guid, 2);

            CircuitTrace.RetentionSnapshot s = CircuitTrace.GetRetentionSnapshot();

            // Counts cover every ring; only the averages come from the sample.
            Assert.Equal(guids.Length, s.RingCount);
            Assert.Equal(guids.Length, s.RecentSegments);
            Assert.Equal(CircuitTrace.RetentionSampleRings, s.SampledRings);
        }
        finally
        {
            foreach (int guid in guids)
                CircuitTrace.Forget(guid);
            CircuitTrace.Mode = priorMode;
        }
    }
}
