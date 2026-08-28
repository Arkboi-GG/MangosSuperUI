using MangosSuperUI.BotLogic.Core;
using MangosSuperUI.BotLogic.Tracking;
using Xunit;

namespace MangosSuperUI.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class CircuitTraceFlowCollection
{
    public const string Name = "CircuitTrace flow";
}

[Collection(CircuitTraceFlowCollection.Name)]
public sealed class CircuitTraceFlowTests
{
    private static int _nextGuid = 1_700_000_000;

    [Fact]
    public async Task LogicalTickContext_SurvivesAwaitOntoAnotherPhysicalThread()
    {
        int guid = NextGuid();
        CircuitTrace.TraceMode priorMode = CircuitTrace.Mode;
        bool tickOpen = false;

        try
        {
            CircuitTrace.Mode = CircuitTrace.TraceMode.Shadow;
            CircuitTrace.BeginTick(guid);
            tickOpen = true;

            int firstPhysicalThread = Environment.CurrentManagedThreadId;
            CircuitTrace.Hit(guid, "flow-test: before await");

            int secondPhysicalThread = await Task.Factory.StartNew(
                () =>
                {
                    CircuitTrace.Hit(guid, "flow-test: after await on long-running worker");
                    return Environment.CurrentManagedThreadId;
                },
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);

            CircuitTrace.EndTick(guid, 0, 0, 1, 2, 3);
            tickOpen = false;

            CircuitTrace.TickSegment segment = Assert.Single(CircuitTrace.PeekSegments(guid));
            Assert.NotEqual(firstPhysicalThread, secondPhysicalThread);
            Assert.True(segment.PrimaryCtx > 0);
            Assert.Equal(2, segment.Hits.Count);
            Assert.All(segment.Hits, hit => Assert.Equal(segment.PrimaryCtx, hit.Ctx));
        }
        finally
        {
            if (tickOpen)
                CircuitTrace.EndTick(guid, 0, 0, 0, 0, 0);
            CircuitTrace.Forget(guid);
            CircuitTrace.Mode = priorMode;
        }
    }

    [Fact]
    public async Task ForeignHitBeforeFirstBrainHit_CannotStealPrimaryContextOrOwnPath()
    {
        int guid = NextGuid();
        CircuitTrace.TraceMode priorMode = CircuitTrace.Mode;
        bool tickOpen = false;

        try
        {
            CircuitTrace.Mode = CircuitTrace.TraceMode.Shadow;
            CircuitTrace.BeginTick(guid);
            tickOpen = true;

            Task foreignHit;
            using (ExecutionContext.SuppressFlow())
            {
                foreignHit = Task.Run(() =>
                    CircuitTrace.Hit(guid, "flow-test: foreign bridge hit arrives first"));
            }

            await foreignHit;
            CircuitTrace.Hit(guid, "flow-test: owning brain hit arrives second");
            CircuitTrace.EndTick(guid, 0, 0, 1, 2, 3);
            tickOpen = false;

            CircuitTrace.TickSegment segment = Assert.Single(CircuitTrace.PeekSegments(guid));
            Assert.Equal(2, segment.Hits.Count);
            Assert.True(segment.PrimaryCtx > 0);
            Assert.NotEqual(segment.PrimaryCtx, segment.Hits[0].Ctx);
            Assert.Equal(segment.PrimaryCtx, segment.Hits[1].Ctx);

            CircuitTrace.ProbeHit own = Assert.Single(
                segment.Hits.Where(hit => hit.Ctx == segment.PrimaryCtx));
            Assert.Equal(segment.Hits[1].SiteId, own.SiteId);
        }
        finally
        {
            if (tickOpen)
                CircuitTrace.EndTick(guid, 0, 0, 0, 0, 0);
            CircuitTrace.Forget(guid);
            CircuitTrace.Mode = priorMode;
        }
    }

    [Fact]
    public void SealedSegmentSequence_IsStableAndStrictlyIncreasing()
    {
        int guid = NextGuid();
        CircuitTrace.TraceMode priorMode = CircuitTrace.Mode;

        try
        {
            CircuitTrace.Mode = CircuitTrace.TraceMode.Shadow;
            RecordSteadyTick(guid);
            RecordSteadyTick(guid);

            List<CircuitTrace.TickSegment> segments = CircuitTrace.PeekSegments(guid, 10);
            Assert.Equal(2, segments.Count);
            Assert.True(segments[0].Seq > 0);
            Assert.True(segments[1].Seq > segments[0].Seq);

            (List<CircuitTrace.DecisionRunSnapshot> runs, bool truncated) =
                CircuitTrace.PeekDecisionRuns(guid);
            CircuitTrace.DecisionRunSnapshot run = Assert.Single(runs);
            Assert.False(truncated);
            Assert.Equal(segments[0].Seq, run.Id);
            Assert.Equal(segments[1].Seq, run.ThroughSeq);
            Assert.Equal(segments[1].Seq, run.Representative.Seq);
        }
        finally
        {
            CircuitTrace.Forget(guid);
            CircuitTrace.Mode = priorMode;
        }
    }

    [Fact]
    public void DecisionRuns_CompressTenMinutesAtFourHertzBeyondRawRingHorizon()
    {
        const int ticksPerSecond = 4;
        const int minutes = 10;
        const int tickCount = ticksPerSecond * 60 * minutes;

        int guid = NextGuid();
        CircuitTrace.TraceMode priorMode = CircuitTrace.Mode;
        var context = new BotContext { Guid = guid, Name = "flow-horizon" };

        try
        {
            // Configure one stable strategic/activity frame without recording the setup
            // mutations as inter-tick probe noise.
            CircuitTrace.Mode = CircuitTrace.TraceMode.Off;
            context.SetGoal(Goal.Grinding, "grind");
            context.HeldTask = new HeldTaskEcho(
                HeldTaskKind.Grind,
                TaskActivity.Searching,
                creatureEntry: 123,
                dest: new Vec4(10, 20, 30, 0),
                kills: 0);
            context.SetObjective(Objective.Grind(
                ObjectiveSource.SelfSolo,
                creatureEntry: 123,
                x: 10,
                y: 20,
                z: 30,
                map: 0,
                killCount: 0));

            CircuitTrace.Mode = CircuitTrace.TraceMode.Shadow;
            for (int i = 0; i < tickCount; i++)
            {
                CircuitTrace.BeginTick(guid);
                CircuitTrace.Hit(guid, "flow-test: steady productive grind decision");
                CircuitTrace.EndTick(guid, 0, 0, 10, 20, 30, context);
            }

            (List<CircuitTrace.DecisionRunSnapshot> runs, bool truncated) =
                CircuitTrace.PeekDecisionRuns(guid);
            CircuitTrace.DecisionRunSnapshot run = Assert.Single(runs);
            Assert.False(truncated);
            Assert.Equal(tickCount, run.SegmentCount);
            Assert.True(run.ThroughSeq > run.Id);
            Assert.Equal((int)Goal.Grinding, run.Representative.Goal);
            Assert.Equal("grind", run.Representative.Step);
            Assert.Equal((int)HeldTaskKind.Grind, run.Representative.TaskKind);
            Assert.Equal((int)TaskActivity.Searching, run.Representative.Activity);
            Assert.Equal(123, run.Representative.ObjectiveCreatureEntry);

            // The raw ring is intentionally shorter than ten minutes fleet-wide;
            // the compressed run must retain the complete logical horizon.
            Assert.True(CircuitTrace.PeekSegments(guid, tickCount).Count < tickCount);
        }
        finally
        {
            CircuitTrace.Forget(guid);
            CircuitTrace.Mode = priorMode;
        }
    }

    [Fact]
    public void SustainedConditionHistory_PreservesLeadupAndRecoveryPhasesBeyondRunCap()
    {
        const int deadPasses = 2_600; // 5,200 inter/tick frames: well beyond the 2,048-run cap without semantic compression.
        int guid = NextGuid();
        CircuitTrace.TraceMode priorMode = CircuitTrace.Mode;
        var context = new BotContext { Guid = guid, Name = "flow-condition-horizon" };

        try
        {
            CircuitTrace.Mode = CircuitTrace.TraceMode.Off;
            context.SetGoal(Goal.Training, "to_trainer");
            CircuitTrace.Mode = CircuitTrace.TraceMode.Shadow;

            CircuitTrace.BeginTick(guid);
            CircuitTrace.Hit(guid, "flow-test: useful activity before death");
            CircuitTrace.EndTick(guid, 0, 0, 10, 20, 30, context);

            context.Dead = true;
            for (int i = 0; i < deadPasses; i++)
            {
                // Mirrors the real host: tracker evidence opens an inter segment,
                // then BeginTick seals it before the authoritative C# tick.
                CircuitTrace.Hit(guid, "flow-test: tracker confirmation");
                CircuitTrace.BeginTick(guid);
                if (i == 0) context.SetGoal(Goal.Maintenance, "rez_wait");
                if (i == 800) context.SetStep("rez_guard_wait");
                if (i == 1_600) context.SetStep("rez_sent");
                CircuitTrace.Hit(guid, "flow-test: death recovery confirmation");
                CircuitTrace.EndTick(guid, 0, 0, 10, 20, 30, context);
            }

            context.Dead = false;
            CircuitTrace.Hit(guid, "flow-test: tracker before clear");
            CircuitTrace.BeginTick(guid);
            context.SetStep("heal");
            CircuitTrace.Hit(guid, "flow-test: recovery clear");
            CircuitTrace.EndTick(guid, 0, 0, 10, 20, 30, context);

            (List<CircuitTrace.DecisionRunSnapshot> runs, bool truncated) =
                CircuitTrace.PeekDecisionRuns(guid);

            Assert.False(truncated);
            Assert.InRange(runs.Count, 6, 12);
            Assert.Contains(runs, run => run.Representative.Step == "to_trainer");
            Assert.Contains(runs, run => run.ConditionTransition == CircuitTrace.ConditionTransition.Onset);
            Assert.Contains(runs, run => run.ConditionTransition == CircuitTrace.ConditionTransition.Clear);
            Assert.Contains(runs, run => run.Representative.Step == "rez_wait");
            Assert.Contains(runs, run => run.Representative.Step == "rez_guard_wait");
            Assert.Contains(runs, run => run.Representative.Step == "rez_sent");

            CircuitTraceTimelineResult timeline = CircuitTraceTimelineBuilder.Build(runs);
            CircuitTraceTimelineEpisode incident = Assert.Single(
                timeline.Episodes.Where(episode => episode.Condition == CircuitTraceConditionKind.Dead));
            Assert.Equal(CircuitTraceTimelineStatus.Resolved, incident.Status);
            Assert.True(incident.RawSegmentCount > 5_000);
            Assert.Equal(
                incident.RawSegmentCount - incident.TransitionCount,
                incident.ConfirmationCount);
            Assert.All(
                incident.Decisions.Where(decision =>
                    decision.Transition == CircuitTraceConditionTransition.Phase),
                decision => Assert.Equal(1, decision.RawSegmentCount));
            Assert.Contains(incident.Decisions, decision =>
                decision.Transition == CircuitTraceConditionTransition.Phase
                && decision.State.Step == "rez_guard_wait");
            Assert.Contains(incident.Decisions, decision =>
                decision.Transition == CircuitTraceConditionTransition.Phase
                && decision.State.Step == "rez_sent");
        }
        finally
        {
            CircuitTrace.Forget(guid);
            CircuitTrace.Mode = priorMode;
        }
    }

    [Fact]
    public void RemoteCppSegments_AppearInTheLongDecisionHistory()
    {
        int guid = NextGuid();
        CircuitTrace.TraceMode priorMode = CircuitTrace.Mode;
        string epoch = "flow-history-" + Guid.NewGuid().ToString("N");

        try
        {
            CircuitTrace.Mode = CircuitTrace.TraceMode.Shadow;
            Assert.Equal(
                CircuitTrace.RemoteSiteRegistration.Added,
                CircuitTrace.RegisterRemoteSite(epoch, 1, "SuiBots/Combat.cpp", 12, "combat: engaged"));

            CircuitTrace.IngestRemoteSegment(
                epoch, guid, 0, 0, 1, 2, 3,
                [(1, null, "target")],
                drops: 0);

            (List<CircuitTrace.DecisionRunSnapshot> runs, bool truncated) =
                CircuitTrace.PeekDecisionRuns(guid);
            CircuitTrace.DecisionRunSnapshot run = Assert.Single(runs);
            Assert.False(truncated);
            Assert.Equal("cpp", run.Representative.Kind);
            Assert.Equal(CircuitTrace.RemoteCtx, run.Representative.PrimaryCtx);
            Assert.Single(run.Representative.Hits);
        }
        finally
        {
            CircuitTrace.Forget(guid);
            CircuitTrace.Mode = priorMode;
        }
    }

    private static int NextGuid() => Interlocked.Increment(ref _nextGuid);

    private static void RecordSteadyTick(int guid)
    {
        CircuitTrace.BeginTick(guid);
        CircuitTrace.Hit(guid, "flow-test: stable sequence");
        CircuitTrace.EndTick(guid, 0, 0, 0, 0, 0);
    }
}
