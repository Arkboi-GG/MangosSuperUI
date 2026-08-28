using MangosSuperUI.BotLogic.Core;
using MangosSuperUI.BotLogic.Tracking;
using Xunit;

namespace MangosSuperUI.Tests;

public sealed class CircuitTraceTimelineBuilderTests
{
    private static readonly DateTime Epoch = new(2026, 8, 27, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void TenMinuteRoutine_WithVariedPaths_BecomesOneLosslessGrindingEpisode()
    {
        var segments = new List<CircuitTrace.TickSegment>();
        TaskActivity[] pattern =
        [
            TaskActivity.Searching,
            TaskActivity.Engaged,
            TaskActivity.Recovering
        ];
        int kills = 0;
        for (int i = 0; i < 31; i++)
        {
            TaskActivity activity = pattern[i % pattern.Length];
            if (activity == TaskActivity.Engaged) kills++;
            // The detailed route varies from decision to decision. Semantic
            // activity/objective identity, not an exact probe path, owns the macro.
            segments.Add(Segment(
                seq: 1_000 + i,
                at: Epoch.AddSeconds(i * 20),
                activity: activity,
                path: [10 + i % 7, 100 + i % 5],
                taskKills: kills));
        }

        CircuitTraceTimelineResult timeline = CircuitTraceTimelineBuilder.Build(segments);

        Assert.Equal(31, timeline.DecisionCount);
        CircuitTraceTimelineEpisode episode = Assert.Single(timeline.Episodes);
        Assert.Equal(1_000, episode.Id);
        Assert.Equal(CircuitTraceTimelineSeverity.Normal, episode.Severity);
        Assert.Contains("Grinding", episode.Label);
        Assert.Equal(31, episode.DecisionCount);
        Assert.Equal(31, episode.RawSegmentCount);
        Assert.Equal(10, episode.KillDelta);
        Assert.Equal(11, episode.ActivityCounts[TaskActivity.Searching]);
        Assert.Equal(10, episode.ActivityCounts[TaskActivity.Engaged]);
        Assert.Equal(10, episode.ActivityCounts[TaskActivity.Recovering]);
        Assert.True(episode.Duration >= TimeSpan.FromMinutes(10));

        CircuitTraceCycleEstimate cycle = Assert.IsType<CircuitTraceCycleEstimate>(episode.CycleEstimate);
        Assert.Equal(pattern, cycle.Pattern);
        Assert.Equal(10, cycle.CompleteCycles);
        Assert.Equal(1, cycle.TrailingSpanCount);

        // Drill-down is lossless even though the macro is one line.
        Assert.Equal(31, episode.Decisions.Count);
        Assert.Equal(
            segments.SelectMany(OwnSiteIds),
            episode.Decisions.SelectMany(decision => decision.OrderedOwnHits.Select(hit => hit.SiteId)));
    }

    [Fact]
    public void SustainedBlockedCondition_AndStrongPointAlarm_AreGroupedButNeverHidden()
    {
        IReadOnlyDictionary<int, CircuitTrace.ProbeSite> sites = Sites(
            new CircuitTrace.ProbeSite(900, "BotBrain.cs", 90, "wedge: TRIPPED no progress"));
        var segments = new[]
        {
            Segment(1, Epoch, TaskActivity.Searching, [1]),
            Segment(2, Epoch.AddSeconds(10), TaskActivity.Blocked, [2]),
            Segment(3, Epoch.AddSeconds(11), TaskActivity.Unknown, [3],
                kind: "inter", primaryCtx: 31, structured: false),
            Segment(4, Epoch.AddSeconds(12), TaskActivity.Blocked, [4]),
            Segment(5, Epoch.AddSeconds(20), TaskActivity.Engaged, [5]),
            Segment(6, Epoch.AddSeconds(30), TaskActivity.Searching, [900]),
            Segment(7, Epoch.AddSeconds(40), TaskActivity.Recovering, [7])
        };

        CircuitTraceTimelineResult timeline = CircuitTraceTimelineBuilder.Build(segments, sites);

        Assert.Equal(4, timeline.Episodes.Count);
        Assert.Equal(CircuitTraceTimelineSeverity.Normal, timeline.Episodes[0].Severity);
        Assert.Equal(CircuitTraceTimelineSeverity.Alarm, timeline.Episodes[1].Severity);
        Assert.Contains("blocked", timeline.Episodes[1].Label, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(CircuitTraceTimelineEpisodeKind.Condition, timeline.Episodes[1].Kind);
        Assert.Equal(CircuitTraceConditionKind.Blocked, timeline.Episodes[1].Condition);
        Assert.Equal(CircuitTraceTimelineStatus.Resolved, timeline.Episodes[1].Status);
        Assert.False(timeline.Episodes[1].StartedBeforeWindow);
        Assert.Equal(2, timeline.Episodes[1].TransitionCount);
        Assert.Equal(2, timeline.Episodes[1].ConfirmationCount);
        Assert.Equal(4, timeline.Episodes[1].DecisionCount);
        Assert.Equal(4, timeline.Episodes[1].RawSegmentCount);
        Assert.Equal(CircuitTraceTimelineSeverity.Alarm, timeline.Episodes[2].Severity);
        Assert.Equal(CircuitTraceTimelineEpisodeKind.EventBurst, timeline.Episodes[2].Kind);
        Assert.Null(timeline.Episodes[2].Condition);
        Assert.Equal(1, timeline.Episodes[2].OccurrenceCount);
        Assert.Single(timeline.Episodes[2].Events);
        Assert.Contains("TRIPPED", timeline.Episodes[2].Label);
        Assert.Equal(900, timeline.Episodes[2].FocusSiteId);
        Assert.Equal(CircuitTraceTimelineSeverity.Normal, timeline.Episodes[3].Severity);
    }

    [Fact]
    public void ContinuousDeath_AcrossTickInterCppAndMacroChanges_IsOneIncident()
    {
        var segments = new[]
        {
            Segment(300, Epoch, TaskActivity.Traveling, [10], Goal.Training, step: "to_trainer"),
            Segment(301, Epoch.AddMilliseconds(250), TaskActivity.Traveling, [11], Goal.Training,
                dead: true, step: "death_detected"),
            Segment(302, Epoch.AddMilliseconds(500), TaskActivity.Unknown, [12],
                kind: "inter", primaryCtx: 41, structured: false),
            Segment(303, Epoch.AddMilliseconds(750), TaskActivity.Unknown, [13],
                kind: "cpp", primaryCtx: CircuitTrace.RemoteCtx, structured: false),
            Segment(304, Epoch.AddSeconds(1), TaskActivity.Recovering, [14], Goal.Maintenance,
                dead: true, step: "rez_wait"),
            Segment(305, Epoch.AddMilliseconds(1250), TaskActivity.Unknown, [15],
                kind: "inter", primaryCtx: 42, structured: false),
            Segment(306, Epoch.AddMilliseconds(1500), TaskActivity.Recovering, [16], Goal.Maintenance,
                dead: true, step: "rez_sent"),
            Segment(307, Epoch.AddSeconds(2), TaskActivity.Recovering, [17], Goal.Maintenance,
                step: "heal"),
            Segment(308, Epoch.AddSeconds(3), TaskActivity.Recovering, [18], Goal.Maintenance,
                step: "heal")
        };

        CircuitTraceTimelineResult timeline = CircuitTraceTimelineBuilder.Build(segments);

        Assert.Equal(3, timeline.Episodes.Count);
        Assert.Equal(CircuitTraceTimelineSeverity.Normal, timeline.Episodes[0].Severity);
        CircuitTraceTimelineEpisode death = Assert.Single(
            timeline.Episodes.Where(episode => episode.AlarmReasons.Contains("dead", StringComparer.Ordinal)));
        Assert.Equal(CircuitTraceTimelineSeverity.Alarm, death.Severity);
        Assert.Equal(CircuitTraceTimelineEpisodeKind.Condition, death.Kind);
        Assert.Equal(CircuitTraceConditionKind.Dead, death.Condition);
        Assert.Equal(CircuitTraceTimelineStatus.Resolved, death.Status);
        Assert.False(death.StartedBeforeWindow);
        Assert.Equal(4, death.TransitionCount);
        Assert.Equal(3, death.ConfirmationCount);
        Assert.Equal(0, death.OccurrenceCount);
        Assert.Equal(301, death.Id);
        Assert.Equal(7, death.DecisionCount);
        Assert.Equal(7, death.RawSegmentCount);
        Assert.Equal(Epoch.AddMilliseconds(250), death.StartUtc);
        Assert.Equal(Epoch.AddMilliseconds(2100), death.EndUtc);
        Assert.Equal(CircuitTraceConditionTransition.Onset, death.Decisions[0].Transition);
        Assert.Equal(CircuitTraceTimelineDecisionPresentation.Transition, death.Decisions[0].Presentation);
        Assert.Equal(
            [
                CircuitTraceConditionTransition.Onset,
                null,
                null,
                CircuitTraceConditionTransition.Phase,
                null,
                CircuitTraceConditionTransition.Phase,
                CircuitTraceConditionTransition.Clear
            ],
            death.Decisions.Select(decision => decision.Transition));
        Assert.Equal(CircuitTraceConditionTransition.Clear, death.Decisions[^1].Transition);
        Assert.Equal(CircuitTraceTimelineSeverity.Normal, timeline.Episodes[^1].Severity);
    }

    [Fact]
    public void DeadAliveDead_ProducesTwoDistinctDeathIncidents()
    {
        var segments = new[]
        {
            Segment(400, Epoch, TaskActivity.Engaged, [20], Goal.Questing),
            Segment(401, Epoch.AddSeconds(1), TaskActivity.Recovering, [21], Goal.Maintenance,
                dead: true, step: "rez_wait"),
            Segment(402, Epoch.AddSeconds(2), TaskActivity.Unknown, [22],
                kind: "cpp", primaryCtx: CircuitTrace.RemoteCtx, structured: false),
            Segment(403, Epoch.AddSeconds(3), TaskActivity.Recovering, [23], Goal.Maintenance,
                step: "heal"),
            Segment(404, Epoch.AddSeconds(4), TaskActivity.Recovering, [24], Goal.Maintenance,
                dead: true, step: "rez_wait"),
            Segment(405, Epoch.AddSeconds(5), TaskActivity.Unknown, [25],
                kind: "inter", primaryCtx: 52, structured: false)
        };

        CircuitTraceTimelineResult timeline = CircuitTraceTimelineBuilder.Build(segments);

        CircuitTraceTimelineEpisode[] deaths = timeline.Episodes
            .Where(episode => episode.AlarmReasons.Contains("dead", StringComparer.Ordinal))
            .ToArray();
        Assert.Equal(2, deaths.Length);
        Assert.Equal([401L, 404L], deaths.Select(episode => episode.Id));
        Assert.Equal([3, 2], deaths.Select(episode => episode.DecisionCount));
        Assert.Equal([CircuitTraceTimelineStatus.Resolved, CircuitTraceTimelineStatus.Ongoing],
            deaths.Select(episode => episode.Status));
        Assert.Equal([2, 1], deaths.Select(episode => episode.TransitionCount));
        Assert.Equal([1, 1], deaths.Select(episode => episode.ConfirmationCount));
        Assert.Equal(CircuitTraceConditionTransition.Clear, deaths[0].Decisions[^1].Transition);
        Assert.Equal(CircuitTraceConditionTransition.Onset, deaths[1].Decisions[0].Transition);
        Assert.DoesNotContain(
            timeline.Episodes.Where(episode => episode.Severity == CircuitTraceTimelineSeverity.Normal),
            episode => episode.Decisions.Any(decision => decision.State.Dead));
    }

    [Fact]
    public void HistoryStartingDead_ShowsOneOngoingIncidentInsteadOfFabricatingAnOnsetPerFrame()
    {
        var segments = new[]
        {
            Segment(500, Epoch, TaskActivity.Recovering, [30], Goal.Maintenance,
                dead: true, step: "rez_wait"),
            Segment(501, Epoch.AddMilliseconds(250), TaskActivity.Unknown, [31],
                kind: "inter", primaryCtx: 61, structured: false),
            Segment(502, Epoch.AddMilliseconds(500), TaskActivity.Unknown, [32],
                kind: "cpp", primaryCtx: CircuitTrace.RemoteCtx, structured: false),
            Segment(503, Epoch.AddMilliseconds(750), TaskActivity.Recovering, [33], Goal.Maintenance,
                dead: true, step: "rez_wait")
        };
        foreach (CircuitTrace.TickSegment segment in segments)
        {
            segment.ConditionIncidentId = 499;
            segment.Condition = CircuitTrace.ConditionKind.Dead;
            segment.ConditionTransition = segment.Kind == "tick"
                ? CircuitTrace.ConditionTransition.Confirmation
                : CircuitTrace.ConditionTransition.None;
            segment.HasStructuredState = segment.Kind == "tick";
        }

        CircuitTraceTimelineResult timeline = CircuitTraceTimelineBuilder.Build(segments);

        CircuitTraceTimelineEpisode death = Assert.Single(timeline.Episodes);
        Assert.Equal(CircuitTraceTimelineSeverity.Alarm, death.Severity);
        Assert.Equal(CircuitTraceTimelineEpisodeKind.Condition, death.Kind);
        Assert.Equal(CircuitTraceConditionKind.Dead, death.Condition);
        Assert.Equal(499, death.Id);
        Assert.Equal(CircuitTraceTimelineStatus.Ongoing, death.Status);
        Assert.True(death.StartedBeforeWindow);
        Assert.Equal(0, death.TransitionCount);
        Assert.Equal(4, death.ConfirmationCount);
        Assert.Contains("dead", death.AlarmReasons);
        Assert.Equal(4, death.DecisionCount);
        Assert.Equal(4, death.RawSegmentCount);
        Assert.All(
            death.Decisions,
            decision => Assert.Equal(CircuitTraceTimelineDecisionPresentation.Confirmation, decision.Presentation));
    }

    [Fact]
    public void StrongPointAlarmDuringDeath_RemainsVisibleInsideTheSameIncident()
    {
        const string strongReason = "wedge: TRIPPED no progress";
        IReadOnlyDictionary<int, CircuitTrace.ProbeSite> sites = Sites(
            new CircuitTrace.ProbeSite(900, "BotBrain.cs", 90, strongReason));
        var segments = new[]
        {
            Segment(600, Epoch, TaskActivity.Recovering, [40], Goal.Maintenance,
                dead: true, step: "rez_wait"),
            Segment(601, Epoch.AddSeconds(1), TaskActivity.Recovering, [900], Goal.Maintenance,
                dead: true, step: "rez_wait"),
            Segment(602, Epoch.AddSeconds(2), TaskActivity.Unknown, [41],
                kind: "cpp", primaryCtx: CircuitTrace.RemoteCtx, structured: false),
            Segment(603, Epoch.AddSeconds(3), TaskActivity.Recovering, [42], Goal.Maintenance,
                step: "heal")
        };

        CircuitTraceTimelineResult timeline = CircuitTraceTimelineBuilder.Build(segments, sites);

        CircuitTraceTimelineEpisode death = Assert.Single(
            timeline.Episodes.Where(episode => episode.Severity == CircuitTraceTimelineSeverity.Alarm));
        Assert.Equal(CircuitTraceTimelineEpisodeKind.Condition, death.Kind);
        Assert.Equal(CircuitTraceConditionKind.Dead, death.Condition);
        Assert.Equal(CircuitTraceTimelineStatus.Resolved, death.Status);
        Assert.True(death.StartedBeforeWindow);
        Assert.Equal(4, death.DecisionCount);
        Assert.Contains("dead", death.AlarmReasons);
        Assert.Contains(strongReason, death.AlarmReasons);
        Assert.Equal(1, death.OccurrenceCount);
        Assert.Single(death.Events);
        Assert.Contains(death.Decisions, decision => decision.AlarmReasons.Contains(strongReason));
        Assert.Contains(death.Decisions, decision => decision.PointEvents.Count > 0);
    }

    [Fact]
    public void RepeatedPointEvents_WithRoutineFramesBetweenThem_BecomeOneVisibleBurst()
    {
        const string strongReason = "movement: MOVE_FAILED at destination";
        IReadOnlyDictionary<int, CircuitTrace.ProbeSite> sites = Sites(
            new CircuitTrace.ProbeSite(910, "Mover.cs", 91, strongReason));
        var segments = new[]
        {
            Segment(700, Epoch, TaskActivity.Traveling, [70], Goal.Questing),
            Segment(701, Epoch.AddSeconds(1), TaskActivity.Traveling, [910], Goal.Questing),
            Segment(702, Epoch.AddSeconds(2), TaskActivity.Unknown, [71],
                kind: "inter", primaryCtx: 81, structured: false),
            Segment(703, Epoch.AddSeconds(3), TaskActivity.Traveling, [72], Goal.Questing),
            Segment(704, Epoch.AddSeconds(4), TaskActivity.Traveling, [910], Goal.Questing),
            Segment(705, Epoch.AddSeconds(5), TaskActivity.Unknown, [73],
                kind: "cpp", primaryCtx: CircuitTrace.RemoteCtx, structured: false),
            Segment(706, Epoch.AddSeconds(6), TaskActivity.Traveling, [910], Goal.Questing),
            Segment(707, Epoch.AddSeconds(7), TaskActivity.Traveling, [74], Goal.Questing)
        };

        CircuitTraceTimelineResult timeline = CircuitTraceTimelineBuilder.Build(segments, sites);

        Assert.Equal(3, timeline.Episodes.Count);
        CircuitTraceTimelineEpisode burst = timeline.Episodes[1];
        Assert.Equal(CircuitTraceTimelineEpisodeKind.EventBurst, burst.Kind);
        Assert.Equal(3, burst.OccurrenceCount);
        Assert.Equal(6, burst.DecisionCount);
        Assert.Single(burst.Events);
        Assert.Equal(3, burst.Events[0].OccurrenceCount);
        Assert.Equal(
            3,
            burst.Decisions.Count(decision =>
                decision.Presentation == CircuitTraceTimelineDecisionPresentation.Event));
        Assert.Equal(
            3,
            burst.Decisions.Count(decision =>
                decision.Presentation == CircuitTraceTimelineDecisionPresentation.Confirmation));
        Assert.Equal(CircuitTraceTimelineEpisodeKind.Routine, timeline.Episodes[0].Kind);
        Assert.Equal(CircuitTraceTimelineEpisodeKind.Routine, timeline.Episodes[2].Kind);
    }

    [Fact]
    public void TenMinutesOfEightHertzDeadFrames_PreservesBoundaryContextInOneTopLevelIncident()
    {
        const int framesPerSecond = 8;
        const int durationSeconds = 10 * 60;
        const int deadFrameCount = framesPerSecond * durationSeconds;
        var segments = new List<CircuitTrace.TickSegment>(deadFrameCount + 2)
        {
            Segment(10_000, Epoch.AddSeconds(-1), TaskActivity.Traveling, [50], Goal.Training,
                step: "to_trainer")
        };

        for (int i = 0; i < deadFrameCount; i++)
        {
            DateTime at = Epoch.AddMilliseconds(i * 125);
            long seq = 10_001 + i;
            int phase = i % 3;
            if (phase == 0)
            {
                segments.Add(Segment(seq, at, TaskActivity.Recovering, [100 + i % 17], Goal.Maintenance,
                    dead: true, step: i < framesPerSecond * 20 ? "rez_wait" : "rez_sent"));
            }
            else
            {
                segments.Add(Segment(seq, at, TaskActivity.Unknown, [200 + i % 19],
                    kind: phase == 1 ? "inter" : "cpp",
                    primaryCtx: phase == 1 ? 71 : CircuitTrace.RemoteCtx,
                    structured: false));
            }
        }

        segments.Add(Segment(10_001 + deadFrameCount, Epoch.AddMinutes(10),
            TaskActivity.Recovering, [51], Goal.Maintenance, step: "heal"));
        segments.Add(Segment(10_002 + deadFrameCount, Epoch.AddMinutes(10).AddSeconds(1),
            TaskActivity.Recovering, [52], Goal.Maintenance, step: "heal"));

        CircuitTraceTimelineResult timeline = CircuitTraceTimelineBuilder.Build(segments);

        Assert.Equal(3, timeline.Episodes.Count);
        CircuitTraceTimelineEpisode death = timeline.Episodes[1];
        Assert.Equal(CircuitTraceTimelineSeverity.Alarm, death.Severity);
        Assert.Equal(CircuitTraceTimelineEpisodeKind.Condition, death.Kind);
        Assert.Equal(CircuitTraceConditionKind.Dead, death.Condition);
        Assert.Equal(CircuitTraceTimelineStatus.Resolved, death.Status);
        Assert.False(death.StartedBeforeWindow);
        Assert.Equal(3, death.TransitionCount);
        Assert.Equal(deadFrameCount - 2, death.ConfirmationCount);
        Assert.Contains("dead", death.AlarmReasons);
        Assert.Equal(deadFrameCount + 1, death.DecisionCount);
        Assert.Equal(deadFrameCount + 1, death.RawSegmentCount);
        Assert.True(death.Duration >= TimeSpan.FromMinutes(9.99));
        Assert.Equal((int)Goal.Training, timeline.Episodes[0].Key.Goal);
        Assert.False(timeline.Episodes[^1].Decisions[0].State.Dead);
    }

    [Fact]
    public void GoalObjectiveAndThirtySecondGap_AreHardMacroBoundaries()
    {
        var segments = new[]
        {
            Segment(10, Epoch, TaskActivity.Searching, [1], creature: 100),
            Segment(11, Epoch.AddSeconds(10), TaskActivity.Engaged, [2], creature: 100),
            Segment(12, Epoch.AddSeconds(20), TaskActivity.Searching, [3], creature: 200),
            Segment(13, Epoch.AddSeconds(30), TaskActivity.Traveling, [4], Goal.Questing, creature: 200),
            Segment(14, Epoch.AddSeconds(70), TaskActivity.Engaged, [5], Goal.Questing, creature: 200)
        };

        CircuitTraceTimelineResult timeline = CircuitTraceTimelineBuilder.Build(segments);

        Assert.Equal(4, timeline.Episodes.Count);
        Assert.Equal(2, timeline.Episodes[0].DecisionCount);
        Assert.Equal(100, timeline.Episodes[0].Key.ObjectiveCreatureEntry);
        Assert.Equal(200, timeline.Episodes[1].Key.ObjectiveCreatureEntry);
        Assert.Equal((int)Goal.Questing, timeline.Episodes[2].Key.Goal);
        Assert.Equal((int)Goal.Questing, timeline.Episodes[3].Key.Goal);
        Assert.True(timeline.Episodes[3].StartUtc - timeline.Episodes[2].EndUtc > TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void PrimaryContext_PreventsForeignFirstHitFromStealingDecisionPath_AndZeroFallsBack()
    {
        CircuitTrace.TickSegment explicitOwner = Segment(
            20, Epoch, TaskActivity.Searching, [], primaryCtx: 42);
        explicitOwner.Hits.Add(new CircuitTrace.ProbeHit(90, 1, null, null, 9));
        explicitOwner.Hits.Add(new CircuitTrace.ProbeHit(1, 2, null, null, 42));
        explicitOwner.Hits.Add(new CircuitTrace.ProbeHit(2, 3, null, null, 42));
        explicitOwner.Hits.Add(new CircuitTrace.ProbeHit(91, 4, null, null, 9));

        CircuitTrace.TickSegment fallback = Segment(
            21, Epoch.AddSeconds(1), TaskActivity.Engaged, [], primaryCtx: 0);
        fallback.Hits.Add(new CircuitTrace.ProbeHit(3, 5, null, null, 77));
        fallback.Hits.Add(new CircuitTrace.ProbeHit(4, 6, null, null, 88));

        IReadOnlyList<CircuitTraceTimelineDecision> decisions =
            CircuitTraceTimelineBuilder.BuildDecisions([explicitOwner, fallback]);

        Assert.Equal([1, 2], decisions[0].Path);
        Assert.Equal([1, 2], decisions[0].OrderedOwnHits.Select(hit => hit.SiteId));
        Assert.Equal([3], decisions[1].Path);
    }

    [Fact]
    public void OrderOnlyPathChange_IsAnExplicitVisibleDiff()
    {
        var segments = new[]
        {
            Segment(30, Epoch, TaskActivity.Searching, [1, 2]),
            Segment(31, Epoch.AddSeconds(1), TaskActivity.Searching, [2, 1])
        };

        IReadOnlyList<CircuitTraceTimelineDecision> decisions =
            CircuitTraceTimelineBuilder.BuildDecisions(segments);

        Assert.Equal(2, decisions.Count);
        Assert.Equal([1, 2], decisions[0].Enter);
        Assert.Empty(decisions[1].Enter);
        Assert.Empty(decisions[1].Exit);
        Assert.True(decisions[1].OrderChanged);
    }

    [Fact]
    public void ExactRun_UsesFirstSegmentSeqForStableIds_AndNewestRepresentativePayload()
    {
        CircuitTrace.TickSegment first = Segment(100, Epoch, TaskActivity.Searching, [1, 2]);
        first.Hits[^1] = first.Hits[^1] with { Value = 11 };
        CircuitTrace.TickSegment second = Segment(101, Epoch.AddSeconds(1), TaskActivity.Searching, [1, 2]);
        second.Hits[^1] = second.Hits[^1] with { Value = 22 };
        CircuitTrace.TickSegment changed = Segment(102, Epoch.AddSeconds(2), TaskActivity.Engaged, [3]);

        CircuitTraceTimelineResult firstBuild = CircuitTraceTimelineBuilder.Build([first, second, changed]);
        CircuitTraceTimelineResult secondBuild = CircuitTraceTimelineBuilder.Build([first, second, changed]);

        Assert.Equal(2, firstBuild.Decisions.Count);
        Assert.Equal(100, firstBuild.Decisions[0].Id);
        Assert.Equal(2, firstBuild.Decisions[0].RawSegmentCount);
        Assert.Equal(22d, firstBuild.Decisions[0].RepresentativeHits[^1].Value);
        Assert.Equal(102, firstBuild.Decisions[1].Id);
        Assert.Equal(100, Assert.Single(firstBuild.Episodes).Id);
        Assert.Equal(
            firstBuild.Decisions.Select(decision => decision.Id),
            secondBuild.Decisions.Select(decision => decision.Id));
    }

    [Fact]
    public void SameProbePath_WithNewActivity_IsStillANewDecision()
    {
        CircuitTrace.TickSegment searching = Segment(
            150, Epoch, TaskActivity.Searching, [1, 2]);
        CircuitTrace.TickSegment engaged = Segment(
            151, Epoch.AddSeconds(1), TaskActivity.Engaged, [1, 2]);

        CircuitTraceTimelineResult timeline = CircuitTraceTimelineBuilder.Build([searching, engaged]);

        Assert.Equal(2, timeline.Decisions.Count);
        Assert.Equal((int)TaskActivity.Searching, timeline.Decisions[0].State.Activity);
        Assert.Equal((int)TaskActivity.Engaged, timeline.Decisions[1].State.Activity);
    }

    [Fact]
    public void CppDecision_CarriesLatestStructuredCSharpState()
    {
        CircuitTrace.TickSegment searching = Segment(
            200, Epoch, TaskActivity.Searching, [1], taskKills: 4, creature: 321);
        CircuitTrace.TickSegment cpp = Segment(
            201, Epoch.AddSeconds(5), TaskActivity.Unknown, [2], kind: "cpp", primaryCtx: CircuitTrace.RemoteCtx,
            structured: false);
        CircuitTrace.TickSegment engaged = Segment(
            202, Epoch.AddSeconds(10), TaskActivity.Engaged, [3], taskKills: 5, creature: 321);

        CircuitTraceTimelineResult timeline = CircuitTraceTimelineBuilder.Build([searching, cpp, engaged]);

        Assert.Equal((int)Goal.Grinding, timeline.Decisions[1].State.Goal);
        Assert.Equal((int)TaskActivity.Searching, timeline.Decisions[1].State.Activity);
        Assert.Equal(321, timeline.Decisions[1].State.ObjectiveCreatureEntry);
        Assert.Equal(4, timeline.Decisions[1].State.TaskKills);
        CircuitTraceTimelineEpisode episode = Assert.Single(timeline.Episodes);
        Assert.Equal(3, episode.DecisionCount);
        Assert.Equal(2, episode.ActivityCounts[TaskActivity.Searching]);
        Assert.Equal(1, episode.KillDelta);
    }

    [Fact]
    public void CompactDecisionRunAdapter_PreservesSuppliedStableIdAndRawCount()
    {
        CircuitTrace.TickSegment representative = Segment(
            999, Epoch.AddSeconds(30), TaskActivity.Engaged, [7], taskKills: 8);
        var run = new CircuitTraceDecisionRunInput(
            id: 700,
            rawSegmentCount: 12,
            startUtc: Epoch,
            endUtc: Epoch.AddSeconds(30),
            representative);

        CircuitTraceTimelineResult timeline = CircuitTraceTimelineBuilder.BuildFromDecisionRuns([run]);

        CircuitTraceTimelineDecision decision = Assert.Single(timeline.Decisions);
        Assert.Equal(700, decision.Id);
        Assert.Equal(12, decision.RawSegmentCount);
        CircuitTraceTimelineEpisode episode = Assert.Single(timeline.Episodes);
        Assert.Equal(700, episode.Id);
        Assert.Equal(12, episode.RawSegmentCount);
    }

    [Fact]
    public void CircuitDecisionRunSnapshot_UsesLongHistoryAdapterDirectly()
    {
        CircuitTrace.TickSegment representative = Segment(
            1_200, Epoch.AddSeconds(45), TaskActivity.Recovering, [8], taskKills: 9);
        var snapshot = new CircuitTrace.DecisionRunSnapshot(
            Id: 800,
            ThroughSeq: 1_200,
            StartUtc: Epoch,
            EndUtc: Epoch.AddSeconds(45),
            SegmentCount: 18,
            Representative: representative);

        CircuitTraceTimelineResult timeline = CircuitTraceTimelineBuilder.Build([snapshot]);

        CircuitTraceTimelineDecision decision = Assert.Single(timeline.Decisions);
        Assert.Equal(800, decision.Id);
        Assert.Equal(1_200, decision.ThroughSeq);
        Assert.Equal(18, decision.RawSegmentCount);
    }

    private static CircuitTrace.TickSegment Segment(
        long seq,
        DateTime at,
        TaskActivity activity,
        IReadOnlyList<int> path,
        Goal goal = Goal.Grinding,
        int taskKills = 0,
        int creature = 123,
        string kind = "tick",
        int primaryCtx = 7,
        bool structured = true,
        bool dead = false,
        string? step = null)
    {
        var segment = new CircuitTrace.TickSegment
        {
            Seq = seq,
            Guid = 14,
            Kind = kind,
            StartUtc = at,
            EndUtc = at.AddMilliseconds(100),
            PrimaryCtx = primaryCtx,
            Goal = structured ? (int)goal : -1,
            Step = structured ? step ?? "grind" : null,
            TaskKind = structured ? (int)HeldTaskKind.Grind : -1,
            Activity = structured ? (int)activity : -1,
            TaskKills = structured ? taskKills : 0,
            ObjectiveKind = structured ? (int)ObjectiveKind.Grind : -1,
            ObjectiveSource = structured ? (int)ObjectiveSource.SelfSolo : -1,
            ObjectiveCreatureEntry = structured ? creature : 0,
            Dead = structured && dead
        };
        long hitSeq = seq * 100;
        foreach (int siteId in path)
            segment.Hits.Add(new CircuitTrace.ProbeHit(siteId, hitSeq++, null, null, primaryCtx));
        return segment;
    }

    private static IEnumerable<int> OwnSiteIds(CircuitTrace.TickSegment segment)
    {
        int owner = segment.PrimaryCtx != 0
            ? segment.PrimaryCtx
            : segment.Hits.Count == 0 ? 0 : segment.Hits[0].Ctx;
        return segment.Hits.Where(hit => hit.Ctx == owner).Select(hit => hit.SiteId);
    }

    private static IReadOnlyDictionary<int, CircuitTrace.ProbeSite> Sites(
        params CircuitTrace.ProbeSite[] sites)
        => sites.ToDictionary(site => site.Id);
}
