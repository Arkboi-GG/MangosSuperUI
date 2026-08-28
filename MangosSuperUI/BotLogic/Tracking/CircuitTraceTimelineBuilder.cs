using MangosSuperUI.BotLogic.Core;

namespace MangosSuperUI.BotLogic.Tracking;

/// <summary>
/// Pure projection from trace segments (or pre-collapsed decision runs) to a
/// human-scale activity timeline. It deliberately keys macro episodes from the
/// structured bot state stamped on each segment; probe prose is consulted only
/// for the small, explicit set of alarm sites that must never be hidden.
/// </summary>
internal static class CircuitTraceTimelineBuilder
{
    internal static readonly TimeSpan EpisodeGap = TimeSpan.FromSeconds(30);
    internal static readonly TimeSpan PointEventBurstGap = TimeSpan.FromSeconds(5);

    public static CircuitTraceTimelineResult Build(
        IReadOnlyList<CircuitTrace.TickSegment> segments,
        IReadOnlyDictionary<int, CircuitTrace.ProbeSite>? sites = null)
        => BuildFromDecisions(BuildDecisions(segments, sites));

    public static CircuitTraceTimelineResult Build(
        IReadOnlyList<CircuitTrace.DecisionRunSnapshot> runs,
        IReadOnlyDictionary<int, CircuitTrace.ProbeSite>? sites = null)
        => BuildFromDecisions(BuildDecisions(runs, sites));

    /// <summary>Direct adapter for CircuitTrace's bounded 20-minute run history.</summary>
    public static IReadOnlyList<CircuitTraceTimelineDecision> BuildDecisions(
        IReadOnlyList<CircuitTrace.DecisionRunSnapshot> runs,
        IReadOnlyDictionary<int, CircuitTrace.ProbeSite>? sites = null)
    {
        var inputs = runs.Select(run => new CircuitTraceDecisionRunInput(
            run.Id,
            run.SegmentCount,
            run.StartUtc,
            run.EndUtc,
            run.Representative)
        {
            ThroughSeq = run.ThroughSeq,
            ConditionIncidentId = run.ConditionIncidentId,
            Condition = run.Condition,
            ConditionTransition = run.ConditionTransition,
            HasStructuredState = run.HasStructuredState
        }).ToArray();
        return BuildDecisions(inputs, sites);
    }

    /// <summary>
    /// Exact decision framing is intentionally separable from macro grouping so
    /// the live tracer can retain compact decision runs for much longer than its
    /// raw segment ring.
    /// </summary>
    public static IReadOnlyList<CircuitTraceTimelineDecision> BuildDecisions(
        IReadOnlyList<CircuitTrace.TickSegment> segments,
        IReadOnlyDictionary<int, CircuitTrace.ProbeSite>? sites = null)
    {
        if (segments.Count == 0) return Array.Empty<CircuitTraceTimelineDecision>();

        var normalized = Normalize(segments, sites);
        var runs = new List<CircuitTraceDecisionRunInput>();
        int i = 0;
        while (i < normalized.Count)
        {
            NormalizedSegment first = normalized[i];
            int[] path = OwnPath(first.Segment);
            int run = 1;
            while (i + run < normalized.Count
                   && string.Equals(normalized[i + run].Segment.Kind, first.Segment.Kind, StringComparison.Ordinal)
                   && normalized[i + run].State == first.State
                   && path.SequenceEqual(OwnPath(normalized[i + run].Segment)))
            {
                run++;
            }

            NormalizedSegment last = normalized[i + run - 1];
            var alarmReasons = new List<string>();
            int? alarmSiteId = null;
            for (int j = i; j < i + run; j++)
            {
                foreach (AlarmFinding finding in normalized[j].Alarms)
                {
                    if (!alarmReasons.Contains(finding.Reason, StringComparer.Ordinal))
                        alarmReasons.Add(finding.Reason);
                    alarmSiteId ??= finding.SiteId;
                }
            }

            runs.Add(new CircuitTraceDecisionRunInput(
                first.Segment.Seq,
                run,
                SafeStart(first.Segment),
                SafeEnd(last.Segment),
                last.Segment)
            {
                ThroughSeq = last.Segment.Seq,
                FirstState = first.State,
                LastState = last.State,
                AlarmReasons = alarmReasons,
                AlarmSiteId = alarmSiteId,
                ConditionIncidentId = last.Segment.ConditionIncidentId,
                Condition = last.Segment.Condition,
                ConditionTransition = last.Segment.ConditionTransition,
                HasStructuredState = last.Segment.HasStructuredState
            });
            i += run;
        }

        return BuildDecisions(runs, sites);
    }

    /// <summary>
    /// Materializes typed decisions from compact exact-path runs. This is the
    /// adapter surface for CircuitTrace's longer-lived decision history.
    /// </summary>
    public static IReadOnlyList<CircuitTraceTimelineDecision> BuildDecisions(
        IReadOnlyList<CircuitTraceDecisionRunInput> runs,
        IReadOnlyDictionary<int, CircuitTrace.ProbeSite>? sites = null)
    {
        if (runs.Count == 0) return Array.Empty<CircuitTraceTimelineDecision>();

        var decisions = new List<CircuitTraceTimelineDecision>(runs.Count);
        CircuitTraceTimelineState? carriedCSharpState = null;
        CircuitTraceConditionKind? activeCondition = null;
        long? activeIncidentId = null;
        DateTime lastConditionEnd = default;
        bool sawAuthoritativeState = false;
        CircuitTraceTimelineState? lastConditionPhaseState = null;
        long? lastConditionPhaseIncidentId = null;

        foreach (CircuitTraceDecisionRunInput run in runs)
        {
            CircuitTrace.TickSegment representative = run.Representative;
            CircuitTraceTimelineState own = CircuitTraceTimelineState.From(representative);
            CircuitTraceTimelineState lastState;
            CircuitTraceTimelineState firstState;

            if (run.LastState is { } suppliedLast)
            {
                lastState = suppliedLast;
                firstState = run.FirstState ?? suppliedLast;
                if (!IsCpp(representative.Kind) && suppliedLast.HasStructuredState)
                    carriedCSharpState = suppliedLast;
            }
            else if (IsCpp(representative.Kind) || !own.HasStructuredState)
            {
                lastState = carriedCSharpState ?? own;
                firstState = run.FirstState ?? lastState;
            }
            else
            {
                lastState = own;
                firstState = run.FirstState ?? own;
                carriedCSharpState = own;
            }

            bool authoritativeState = run.HasStructuredState
                || representative.HasStructuredState
                || (!IsCpp(representative.Kind) && own.HasStructuredState);

            int owner = OwnerContext(representative);
            CircuitTrace.ProbeHit[] representativeHits = representative.Hits.ToArray();
            CircuitTraceTimelineHit[] ownHits = representative.Hits
                .Where(hit => hit.Ctx == owner)
                .Select((hit, order) => new CircuitTraceTimelineHit(
                    order,
                    hit,
                    TryGetSite(sites, hit.SiteId)))
                .ToArray();
            int[] path = ownHits.Select(hit => hit.SiteId).ToArray();

            var pointFindings = new List<AlarmFinding>();
            foreach (string reason in run.AlarmReasons)
            {
                if (IsConditionReason(reason)) continue;
                if (!pointFindings.Any(finding => string.Equals(
                        finding.Reason,
                        reason,
                        StringComparison.Ordinal)))
                    pointFindings.Add(new AlarmFinding(reason, run.AlarmSiteId));
            }
            foreach (AlarmFinding finding in DetectPointAlarms(representative, sites))
            {
                if (!pointFindings.Contains(finding)) pointFindings.Add(finding);
            }

            CircuitTraceConditionKind? condition = null;
            long? conditionIncidentId = null;
            CircuitTraceConditionTransition? transition = null;
            bool startedBeforeWindow = false;
            bool clearAfterDecision = false;

            if (run.ConditionIncidentId != 0 && run.Condition != CircuitTrace.ConditionKind.None)
            {
                condition = ToTimelineCondition(run.Condition);
                conditionIncidentId = run.ConditionIncidentId;
                transition = ToTimelineTransition(run.ConditionTransition);
                startedBeforeWindow = !sawAuthoritativeState
                    && transition != CircuitTraceConditionTransition.Onset;
                clearAfterDecision = transition == CircuitTraceConditionTransition.Clear;
                if (!clearAfterDecision)
                {
                    activeCondition = condition;
                    activeIncidentId = conditionIncidentId;
                    lastConditionEnd = run.EndUtc;
                }
            }
            else if (authoritativeState)
            {
                CircuitTraceConditionKind? observed = ConditionFrom(lastState);
                if (observed == null)
                {
                    if (activeCondition != null && activeIncidentId != null)
                    {
                        condition = activeCondition;
                        conditionIncidentId = activeIncidentId;
                        transition = CircuitTraceConditionTransition.Clear;
                        clearAfterDecision = true;
                    }
                }
                else if (observed == activeCondition && activeIncidentId != null)
                {
                    condition = observed;
                    conditionIncidentId = activeIncidentId;
                }
                else
                {
                    condition = observed;
                    conditionIncidentId = run.Id;
                    if (sawAuthoritativeState)
                        transition = CircuitTraceConditionTransition.Onset;
                    else
                        startedBeforeWindow = true;
                    activeCondition = condition;
                    activeIncidentId = conditionIncidentId;
                }

                if (!clearAfterDecision && condition != null)
                    lastConditionEnd = run.EndUtc;
            }
            else if (activeCondition != null
                     && activeIncidentId != null
                     && (lastConditionEnd == default
                         || run.StartUtc - lastConditionEnd <= EpisodeGap))
            {
                condition = activeCondition;
                conditionIncidentId = activeIncidentId;
                lastConditionEnd = run.EndUtc;
            }

            if (condition != null
                && !clearAfterDecision
                && transition == null
                && authoritativeState
                && lastConditionPhaseIncidentId == conditionIncidentId
                && lastConditionPhaseState is { } priorPhase
                && !SameConditionPhase(priorPhase, lastState))
            {
                transition = CircuitTraceConditionTransition.Phase;
            }

            if (condition != null && !clearAfterDecision && authoritativeState)
            {
                lastConditionPhaseState = lastState;
                lastConditionPhaseIncidentId = conditionIncidentId;
            }

            var alarmReasons = new List<string>();
            if (condition != null) alarmReasons.Add(ConditionReason(condition.Value));
            foreach (AlarmFinding finding in pointFindings)
            {
                if (!alarmReasons.Contains(finding.Reason, StringComparer.Ordinal))
                    alarmReasons.Add(finding.Reason);
            }

            int? alarmSiteId = pointFindings.Select(finding => finding.SiteId)
                .FirstOrDefault(siteId => siteId != null);

            CircuitTraceTimelineSeverity severity = alarmReasons.Count == 0
                ? CircuitTraceTimelineSeverity.Normal
                : CircuitTraceTimelineSeverity.Alarm;
            int? focusSiteId = alarmSiteId ?? (ownHits.Length == 0 ? null : ownHits[^1].SiteId);
            CircuitTrace.ProbeSite? focusSite = focusSiteId is { } focus
                ? TryGetSite(sites, focus)
                : null;

            CircuitTraceTimelineDecisionPresentation presentation = condition != null
                ? transition != null
                    ? CircuitTraceTimelineDecisionPresentation.Transition
                    : pointFindings.Count > 0
                        ? CircuitTraceTimelineDecisionPresentation.Event
                        : CircuitTraceTimelineDecisionPresentation.Confirmation
                : pointFindings.Count > 0
                    ? CircuitTraceTimelineDecisionPresentation.Event
                    : CircuitTraceTimelineDecisionPresentation.Decision;
            CircuitTraceTimelineAlarmEvent[] pointEvents = pointFindings
                .Select(finding => new CircuitTraceTimelineAlarmEvent
                {
                    Reason = finding.Reason,
                    SiteId = finding.SiteId,
                    StartUtc = run.StartUtc,
                    EndUtc = run.EndUtc,
                    OccurrenceCount = Math.Max(1, run.RawSegmentCount),
                    DecisionIds = [run.Id]
                })
                .ToArray();

            decisions.Add(new CircuitTraceTimelineDecision
            {
                Id = run.Id,
                ThroughSeq = run.ThroughSeq,
                Kind = representative.Kind,
                StartUtc = run.StartUtc,
                EndUtc = run.EndUtc,
                RawSegmentCount = Math.Max(1, run.RawSegmentCount),
                Path = path,
                RepresentativeHits = representativeHits,
                OrderedOwnHits = ownHits,
                State = lastState,
                TaskKillsStart = firstState.TaskKills,
                TaskKillsEnd = lastState.TaskKills,
                Severity = severity,
                AlarmReasons = alarmReasons,
                FocusSiteId = focusSiteId,
                FocusSite = focusSite,
                Label = severity == CircuitTraceTimelineSeverity.Alarm
                    ? "Alarm · " + alarmReasons[0]
                    : DecisionLabel(lastState),
                ConditionIncidentId = conditionIncidentId,
                Condition = condition,
                Transition = transition,
                Presentation = presentation,
                HasAuthoritativeState = authoritativeState,
                StartedBeforeWindow = startedBeforeWindow,
                PointEvents = pointEvents
            });

            if (clearAfterDecision)
            {
                activeCondition = null;
                activeIncidentId = null;
                lastConditionEnd = default;
                lastConditionPhaseState = null;
                lastConditionPhaseIncidentId = null;
            }
            if (authoritativeState) sawAuthoritativeState = true;
        }

        ApplyPathDiffs(decisions);
        return decisions;
    }

    public static CircuitTraceTimelineResult BuildFromDecisionRuns(
        IReadOnlyList<CircuitTraceDecisionRunInput> runs,
        IReadOnlyDictionary<int, CircuitTrace.ProbeSite>? sites = null)
        => BuildFromDecisions(BuildDecisions(runs, sites));

    public static CircuitTraceTimelineResult BuildFromDecisions(
        IReadOnlyList<CircuitTraceTimelineDecision> decisions)
    {
        if (decisions.Count == 0)
            return new CircuitTraceTimelineResult(
                Array.Empty<CircuitTraceTimelineDecision>(),
                Array.Empty<CircuitTraceTimelineEpisode>());

        var materialized = decisions.ToList();
        ApplyPathDiffs(materialized);
        AssignPointEventBursts(materialized);

        var episodes = new List<CircuitTraceTimelineEpisode>();
        var pending = new List<CircuitTraceTimelineDecision>();
        CircuitTraceTimelineDecision? previous = null;

        foreach (CircuitTraceTimelineDecision decision in materialized)
        {
            bool boundary = pending.Count == 0;
            if (!boundary && previous != null)
            {
                bool gap = decision.StartUtc - previous.EndUtc > EpisodeGap;
                if (gap
                    && decision.ConditionIncidentId != null
                    && decision.ConditionIncidentId == previous.ConditionIncidentId)
                    decision.ContinuationUnknown = true;
                boundary = gap || !SameEpisode(pending[0], previous, decision);
            }

            if (boundary && pending.Count > 0)
            {
                episodes.Add(BuildEpisode(pending));
                pending.Clear();
            }

            pending.Add(decision);
            previous = decision;
        }

        if (pending.Count > 0) episodes.Add(BuildEpisode(pending));

        // A condition without a literal clear is ongoing only when it owns the
        // newest retained decision. Earlier condition chapters ended at an observed
        // replacement, gap, or session/window discontinuity.
        for (int i = 0; i < episodes.Count; i++)
        {
            CircuitTraceTimelineEpisode episode = episodes[i];
            if (episode.Kind != CircuitTraceTimelineEpisodeKind.Condition) continue;
            bool hasClear = episode.Decisions.Any(decision =>
                decision.Transition == CircuitTraceConditionTransition.Clear);
            episode.Status = !hasClear && i == episodes.Count - 1
                ? CircuitTraceTimelineStatus.Ongoing
                : CircuitTraceTimelineStatus.Resolved;
        }
        return new CircuitTraceTimelineResult(materialized, episodes);
    }

    private static bool SameEpisode(
        CircuitTraceTimelineDecision first,
        CircuitTraceTimelineDecision previous,
        CircuitTraceTimelineDecision current)
    {
        if (first.ConditionIncidentId != null || current.ConditionIncidentId != null)
            return first.ConditionIncidentId != null
                && current.ConditionIncidentId == first.ConditionIncidentId;

        if (first.EventBurstId != null || current.EventBurstId != null)
            return first.EventBurstId != null
                && current.EventBurstId == first.EventBurstId;

        return current.MacroKey == previous.MacroKey;
    }

    private static void AssignPointEventBursts(
        IReadOnlyList<CircuitTraceTimelineDecision> decisions)
    {
        int i = 0;
        while (i < decisions.Count)
        {
            CircuitTraceTimelineDecision first = decisions[i];
            if (first.ConditionIncidentId != null || first.PointEvents.Count == 0)
            {
                i++;
                continue;
            }

            string signature = PointEventSignature(first);
            int lastEvent = i;
            int scan = i + 1;
            while (scan < decisions.Count)
            {
                CircuitTraceTimelineDecision candidate = decisions[scan];
                if (candidate.ConditionIncidentId != null
                    || candidate.MacroKey != first.MacroKey
                    || candidate.StartUtc - decisions[lastEvent].EndUtc > PointEventBurstGap)
                    break;

                if (candidate.PointEvents.Count > 0)
                {
                    if (PointEventSignature(candidate) != signature) break;
                    lastEvent = scan;
                }
                scan++;
            }

            long burstId = first.Id;
            first.EventBurstId = burstId;
            if (lastEvent > i)
            {
                for (int j = i + 1; j <= lastEvent; j++)
                {
                    CircuitTraceTimelineDecision member = decisions[j];
                    member.EventBurstId = burstId;
                    if (member.PointEvents.Count == 0)
                        member.Presentation = CircuitTraceTimelineDecisionPresentation.Confirmation;
                }
            }
            i = lastEvent + 1;
        }
    }

    private static string PointEventSignature(CircuitTraceTimelineDecision decision)
        => string.Join('|', decision.PointEvents
            .Select(point => $"{point.SiteId?.ToString() ?? "-"}:{point.Reason}")
            .OrderBy(value => value, StringComparer.Ordinal));

    private static List<NormalizedSegment> Normalize(
        IReadOnlyList<CircuitTrace.TickSegment> segments,
        IReadOnlyDictionary<int, CircuitTrace.ProbeSite>? sites)
    {
        var result = new List<NormalizedSegment>(segments.Count);
        CircuitTraceTimelineState? carriedCSharpState = null;
        foreach (CircuitTrace.TickSegment segment in segments)
        {
            CircuitTraceTimelineState own = CircuitTraceTimelineState.From(segment);
            CircuitTraceTimelineState effective;
            if (IsCpp(segment.Kind) || !own.HasStructuredState)
            {
                effective = carriedCSharpState ?? own;
            }
            else
            {
                effective = own;
                carriedCSharpState = own;
            }

            result.Add(new NormalizedSegment(
                segment,
                effective,
                DetectPointAlarms(segment, sites)));
        }
        return result;
    }

    private static CircuitTraceTimelineEpisode BuildEpisode(
        IReadOnlyList<CircuitTraceTimelineDecision> decisions)
    {
        CircuitTraceTimelineDecision first = decisions[0];
        CircuitTraceTimelineDecision last = decisions[^1];
        CircuitTraceTimelineEpisodeKind kind = first.ConditionIncidentId != null
            ? CircuitTraceTimelineEpisodeKind.Condition
            : first.EventBurstId != null
                ? CircuitTraceTimelineEpisodeKind.EventBurst
                : CircuitTraceTimelineEpisodeKind.Routine;
        CircuitTraceTimelineSeverity severity = kind == CircuitTraceTimelineEpisodeKind.Routine
            ? CircuitTraceTimelineSeverity.Normal
            : CircuitTraceTimelineSeverity.Alarm;

        IReadOnlyList<CircuitTraceActivitySpan> spans = BuildActivitySpans(decisions);
        var counts = new Dictionary<TaskActivity, int>();
        var rawCounts = new Dictionary<TaskActivity, int>();
        foreach (CircuitTraceTimelineDecision decision in decisions)
        {
            TaskActivity activity = NormalizeActivity(decision.State.Activity);
            counts[activity] = counts.GetValueOrDefault(activity) + 1;
            rawCounts[activity] = rawCounts.GetValueOrDefault(activity) + decision.RawSegmentCount;
        }

        IReadOnlyList<CircuitTraceTimelineAlarmEvent> events = BuildAlarmEvents(decisions);
        var reasons = new List<string>();
        if (first.Condition is { } condition)
            reasons.Add(ConditionReason(condition));
        foreach (CircuitTraceTimelineAlarmEvent point in events)
        {
            if (!reasons.Contains(point.Reason, StringComparer.Ordinal))
                reasons.Add(point.Reason);
        }
        CircuitTraceTimelineDecision focusDecision = kind != CircuitTraceTimelineEpisodeKind.Routine
            ? decisions.FirstOrDefault(decision => decision.PointEvents.Count > 0) ?? first
            : last;
        int occurrenceCount = events.Sum(point => point.OccurrenceCount);
        string label = kind switch
        {
            CircuitTraceTimelineEpisodeKind.Condition => "Alarm · " + ConditionReason(first.Condition!.Value),
            CircuitTraceTimelineEpisodeKind.EventBurst => "Alarm · " + reasons[0],
            _ => EpisodeLabel(first.MacroKey)
        };

        return new CircuitTraceTimelineEpisode
        {
            // A stamped incident id survives the reader-window onset rolling out.
            // Keeping it as the episode identity prevents a paused viewer from
            // reporting the same sustained condition as a brand-new incident.
            Id = kind == CircuitTraceTimelineEpisodeKind.Condition
                ? first.ConditionIncidentId!.Value
                : first.Id,
            Key = first.MacroKey,
            Label = label,
            Severity = severity,
            Kind = kind,
            Status = CircuitTraceTimelineStatus.Resolved,
            Condition = first.Condition,
            StartedBeforeWindow = first.StartedBeforeWindow,
            ContinuationUnknown = first.ContinuationUnknown,
            TransitionCount = decisions.Count(decision => decision.Transition != null),
            ConfirmationCount = kind == CircuitTraceTimelineEpisodeKind.Condition
                ? decisions.Sum(decision => Math.Max(
                    0,
                    decision.RawSegmentCount - (decision.Transition != null ? 1 : 0)))
                : 0,
            OccurrenceCount = occurrenceCount,
            StartUtc = first.StartUtc,
            EndUtc = last.EndUtc,
            RawSegmentCount = decisions.Sum(decision => decision.RawSegmentCount),
            DecisionCount = decisions.Count,
            KillDelta = KillDelta(decisions),
            ActivitySpans = spans,
            ActivityCounts = counts,
            ActivityRawSegmentCounts = rawCounts,
            CycleEstimate = kind == CircuitTraceTimelineEpisodeKind.Routine
                ? EstimateCycle(spans)
                : null,
            FocusSiteId = focusDecision.FocusSiteId,
            FocusSite = focusDecision.FocusSite,
            AlarmReasons = reasons,
            Events = events,
            Decisions = decisions.ToArray()
        };
    }

    private static IReadOnlyList<CircuitTraceTimelineAlarmEvent> BuildAlarmEvents(
        IReadOnlyList<CircuitTraceTimelineDecision> decisions)
    {
        var events = new List<CircuitTraceTimelineAlarmEvent>();
        foreach (CircuitTraceTimelineAlarmEvent point in decisions.SelectMany(
                     decision => decision.PointEvents))
        {
            CircuitTraceTimelineAlarmEvent? previous = events.LastOrDefault();
            if (previous != null
                && previous.SiteId == point.SiteId
                && string.Equals(previous.Reason, point.Reason, StringComparison.Ordinal)
                && point.StartUtc - previous.EndUtc <= PointEventBurstGap)
            {
                previous.EndUtc = point.EndUtc;
                previous.OccurrenceCount += point.OccurrenceCount;
                previous.DecisionIds = previous.DecisionIds.Concat(point.DecisionIds)
                    .Distinct()
                    .ToArray();
                continue;
            }

            events.Add(new CircuitTraceTimelineAlarmEvent
            {
                Reason = point.Reason,
                SiteId = point.SiteId,
                StartUtc = point.StartUtc,
                EndUtc = point.EndUtc,
                OccurrenceCount = point.OccurrenceCount,
                DecisionIds = point.DecisionIds.ToArray()
            });
        }
        return events;
    }

    private static IReadOnlyList<CircuitTraceActivitySpan> BuildActivitySpans(
        IReadOnlyList<CircuitTraceTimelineDecision> decisions)
    {
        var spans = new List<CircuitTraceActivitySpan>();
        int i = 0;
        while (i < decisions.Count)
        {
            TaskActivity activity = NormalizeActivity(decisions[i].State.Activity);
            int run = 1;
            while (i + run < decisions.Count
                   && NormalizeActivity(decisions[i + run].State.Activity) == activity)
            {
                run++;
            }

            spans.Add(new CircuitTraceActivitySpan(
                activity,
                decisions[i].StartUtc,
                decisions[i + run - 1].EndUtc,
                run,
                decisions.Skip(i).Take(run).Sum(decision => decision.RawSegmentCount)));
            i += run;
        }
        return spans;
    }

    private static CircuitTraceCycleEstimate? EstimateCycle(
        IReadOnlyList<CircuitTraceActivitySpan> spans)
    {
        if (spans.Count < 4) return null;
        TaskActivity[] values = spans.Select(span => span.Activity).ToArray();
        for (int period = 2; period <= values.Length / 2; period++)
        {
            bool repeats = true;
            for (int i = period; i < values.Length; i++)
            {
                if (values[i] == values[i % period]) continue;
                repeats = false;
                break;
            }

            int complete = values.Length / period;
            if (!repeats || complete < 2) continue;
            return new CircuitTraceCycleEstimate(
                values.Take(period).ToArray(),
                complete,
                values.Length % period);
        }
        return null;
    }

    private static int KillDelta(IReadOnlyList<CircuitTraceTimelineDecision> decisions)
    {
        int delta = 0;
        int? prior = null;
        foreach (CircuitTraceTimelineDecision decision in decisions)
        {
            int start = decision.TaskKillsStart;
            int end = decision.TaskKillsEnd;
            if (prior is { } previous && start >= previous) delta += start - previous;
            if (end >= start) delta += end - start;
            prior = end;
        }
        return delta;
    }

    private static void ApplyPathDiffs(IReadOnlyList<CircuitTraceTimelineDecision> decisions)
    {
        IReadOnlyList<int>? previous = null;
        foreach (CircuitTraceTimelineDecision decision in decisions)
        {
            if (previous == null)
            {
                decision.Enter = StableDistinct(decision.Path);
                decision.Exit = Array.Empty<int>();
                decision.OrderChanged = false;
            }
            else
            {
                var priorSet = previous.ToHashSet();
                var currentSet = decision.Path.ToHashSet();
                decision.Enter = StableDistinct(decision.Path.Where(site => !priorSet.Contains(site)));
                decision.Exit = StableDistinct(previous.Where(site => !currentSet.Contains(site)));
                decision.OrderChanged = !previous.SequenceEqual(decision.Path)
                    && SameMultiset(previous, decision.Path);
            }
            previous = decision.Path;
        }
    }

    private static int[] StableDistinct(IEnumerable<int> values)
    {
        var seen = new HashSet<int>();
        return values.Where(seen.Add).ToArray();
    }

    private static bool SameMultiset(IReadOnlyList<int> left, IReadOnlyList<int> right)
    {
        if (left.Count != right.Count) return false;
        var counts = new Dictionary<int, int>();
        foreach (int value in left) counts[value] = counts.GetValueOrDefault(value) + 1;
        foreach (int value in right)
        {
            if (!counts.TryGetValue(value, out int count) || count == 0) return false;
            if (count == 1) counts.Remove(value);
            else counts[value] = count - 1;
        }
        return counts.Count == 0;
    }

    private static IReadOnlyList<AlarmFinding> DetectPointAlarms(
        CircuitTrace.TickSegment segment,
        IReadOnlyDictionary<int, CircuitTrace.ProbeSite>? sites)
    {
        var findings = new List<AlarmFinding>();
        if (string.Equals(segment.Kind, "cpp-drops", StringComparison.OrdinalIgnoreCase))
            findings.Add(new AlarmFinding("C++ trace dropped hits", null));

        foreach (CircuitTrace.ProbeHit hit in segment.Hits)
        {
            CircuitTrace.ProbeSite? site = TryGetSite(sites, hit.SiteId);
            if (site == null) continue;
            if (CircuitTrace.IsPointAlarmText(site.Description, site.File, hit.Note))
                findings.Add(new AlarmFinding(site.Description, hit.SiteId));
        }

        return findings
            .DistinctBy(finding => (finding.Reason, finding.SiteId))
            .ToArray();
    }

    private static CircuitTraceConditionKind? ConditionFrom(CircuitTraceTimelineState state)
    {
        if (state.Dead) return CircuitTraceConditionKind.Dead;
        return NormalizeActivity(state.Activity) == TaskActivity.Blocked
            ? CircuitTraceConditionKind.Blocked
            : null;
    }

    private static CircuitTraceConditionKind ToTimelineCondition(CircuitTrace.ConditionKind condition)
        => condition switch
        {
            CircuitTrace.ConditionKind.Dead => CircuitTraceConditionKind.Dead,
            CircuitTrace.ConditionKind.Blocked => CircuitTraceConditionKind.Blocked,
            _ => throw new ArgumentOutOfRangeException(nameof(condition), condition, null)
        };

    private static CircuitTraceConditionTransition? ToTimelineTransition(
        CircuitTrace.ConditionTransition transition)
        => transition switch
        {
            CircuitTrace.ConditionTransition.Onset => CircuitTraceConditionTransition.Onset,
            CircuitTrace.ConditionTransition.Clear => CircuitTraceConditionTransition.Clear,
            _ => null
        };

    private static string ConditionReason(CircuitTraceConditionKind condition)
        => condition == CircuitTraceConditionKind.Dead ? "dead" : "activity blocked";

    private static bool IsConditionReason(string reason)
        => string.Equals(reason, "dead", StringComparison.Ordinal)
            || string.Equals(reason, "activity blocked", StringComparison.Ordinal);

    private static bool SameConditionPhase(
        CircuitTraceTimelineState prior,
        CircuitTraceTimelineState current)
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

    private static int[] OwnPath(CircuitTrace.TickSegment segment)
    {
        int owner = OwnerContext(segment);
        return segment.Hits
            .Where(hit => hit.Ctx == owner)
            .Select(hit => hit.SiteId)
            .ToArray();
    }

    private static int OwnerContext(CircuitTrace.TickSegment segment)
        => segment.PrimaryCtx != 0
            ? segment.PrimaryCtx
            : segment.Hits.Count == 0 ? 0 : segment.Hits[0].Ctx;

    private static CircuitTrace.ProbeSite? TryGetSite(
        IReadOnlyDictionary<int, CircuitTrace.ProbeSite>? sites,
        int siteId)
        => sites != null && sites.TryGetValue(siteId, out CircuitTrace.ProbeSite? site)
            ? site
            : null;

    private static bool IsCpp(string? kind)
        => kind?.StartsWith("cpp", StringComparison.OrdinalIgnoreCase) == true;

    private static DateTime SafeStart(CircuitTrace.TickSegment segment)
        => segment.StartUtc == default ? segment.EndUtc : segment.StartUtc;

    private static DateTime SafeEnd(CircuitTrace.TickSegment segment)
        => segment.EndUtc == default ? segment.StartUtc : segment.EndUtc;

    private static TaskActivity NormalizeActivity(int value)
        => Enum.IsDefined(typeof(TaskActivity), value)
            ? (TaskActivity)value
            : TaskActivity.Unknown;

    private static string DecisionLabel(CircuitTraceTimelineState state)
    {
        TaskActivity activity = NormalizeActivity(state.Activity);
        if (activity != TaskActivity.Unknown) return activity.ToString();
        if (!string.IsNullOrWhiteSpace(state.Step)) return state.Step!;
        return GoalName(state.Goal);
    }

    private static string EpisodeLabel(CircuitTraceMacroKey key)
    {
        string goal = GoalName(key.Goal);
        if (key.ObjectiveQuestId > 0)
            return key.ObjectiveSlot > 0
                ? $"{goal} · quest {key.ObjectiveQuestId} slot {key.ObjectiveSlot}"
                : $"{goal} · quest {key.ObjectiveQuestId}";
        if (key.ObjectiveCreatureEntry > 0)
            return $"{goal} · creature {key.ObjectiveCreatureEntry}";
        if (key.ObjectiveNpcEntry > 0)
            return $"{goal} · NPC {key.ObjectiveNpcEntry}";
        return goal;
    }

    private static string GoalName(int value)
        => Enum.IsDefined(typeof(Goal), value) ? ((Goal)value).ToString() : "Unknown";

    private sealed record NormalizedSegment(
        CircuitTrace.TickSegment Segment,
        CircuitTraceTimelineState State,
        IReadOnlyList<AlarmFinding> Alarms);

    private readonly record struct AlarmFinding(string Reason, int? SiteId);
}

internal enum CircuitTraceTimelineSeverity
{
    Normal,
    Alarm
}

internal enum CircuitTraceTimelineEpisodeKind { Routine, Condition, EventBurst }
internal enum CircuitTraceTimelineStatus { Resolved, Ongoing }
internal enum CircuitTraceConditionKind { Dead, Blocked }
internal enum CircuitTraceConditionTransition { Onset, Phase, Clear }
internal enum CircuitTraceTimelineDecisionPresentation { Decision, Transition, Confirmation, Event }

internal readonly record struct CircuitTraceMacroKey(
    int Goal,
    int ObjectiveKind,
    int ObjectiveSource,
    int ObjectiveQuestId,
    int ObjectiveSlot,
    int ObjectiveCreatureEntry,
    int ObjectiveNpcEntry);

internal readonly record struct CircuitTraceTimelineState(
    int Goal,
    string? Step,
    int TaskKind,
    int Activity,
    int TaskKills,
    int ObjectiveKind,
    int ObjectiveSource,
    int ObjectiveQuestId,
    int ObjectiveSlot,
    int ObjectiveCreatureEntry,
    int ObjectiveNpcEntry,
    bool InCombat,
    bool Dead)
{
    public bool HasStructuredState => Goal >= 0
        || TaskKind >= 0
        || Activity >= 0
        || ObjectiveKind >= 0;

    public CircuitTraceMacroKey MacroKey => new(
        Goal,
        ObjectiveKind,
        ObjectiveSource,
        ObjectiveQuestId,
        ObjectiveSlot,
        ObjectiveCreatureEntry,
        ObjectiveNpcEntry);

    public static CircuitTraceTimelineState From(CircuitTrace.TickSegment segment) => new(
        segment.Goal,
        segment.Step,
        segment.TaskKind,
        segment.Activity,
        segment.TaskKills,
        segment.ObjectiveKind,
        segment.ObjectiveSource,
        segment.ObjectiveQuestId,
        segment.ObjectiveSlot,
        segment.ObjectiveCreatureEntry,
        segment.ObjectiveNpcEntry,
        segment.InCombat,
        segment.Dead);
}

/// <summary>Compact adapter shape for a pre-RLE decision history.</summary>
internal sealed class CircuitTraceDecisionRunInput
{
    public CircuitTraceDecisionRunInput(
        long id,
        int rawSegmentCount,
        DateTime startUtc,
        DateTime endUtc,
        CircuitTrace.TickSegment representative)
    {
        Id = id;
        RawSegmentCount = rawSegmentCount;
        StartUtc = startUtc;
        EndUtc = endUtc;
        Representative = representative;
        ThroughSeq = representative.Seq;
    }

    public long Id { get; }
    public long ThroughSeq { get; init; }
    public int RawSegmentCount { get; }
    public DateTime StartUtc { get; }
    public DateTime EndUtc { get; }
    public CircuitTrace.TickSegment Representative { get; }
    public CircuitTraceTimelineState? FirstState { get; init; }
    public CircuitTraceTimelineState? LastState { get; init; }
    public IReadOnlyList<string> AlarmReasons { get; init; } = Array.Empty<string>();
    public int? AlarmSiteId { get; init; }
    public long ConditionIncidentId { get; init; }
    public CircuitTrace.ConditionKind Condition { get; init; }
    public CircuitTrace.ConditionTransition ConditionTransition { get; init; }
    public bool HasStructuredState { get; init; }
}

internal sealed record CircuitTraceTimelineHit(
    int Order,
    CircuitTrace.ProbeHit Hit,
    CircuitTrace.ProbeSite? Site)
{
    public int SiteId => Hit.SiteId;
    public string? File => Site?.File;
    public int? Line => Site?.Line;
    public string? Description => Site?.Description;
}

internal sealed class CircuitTraceTimelineDecision
{
    public long Id { get; internal init; }
    public long ThroughSeq { get; internal init; }
    public string Kind { get; internal init; } = "tick";
    public string Label { get; internal init; } = "Unknown";
    public CircuitTraceTimelineSeverity Severity { get; internal init; }
    public DateTime StartUtc { get; internal init; }
    public DateTime EndUtc { get; internal init; }
    public TimeSpan Duration => EndUtc - StartUtc;
    public int RawSegmentCount { get; internal init; }
    public CircuitTraceTimelineState State { get; internal init; }
    public CircuitTraceMacroKey MacroKey => State.MacroKey;
    public int TaskKillsStart { get; internal init; }
    public int TaskKillsEnd { get; internal init; }
    public IReadOnlyList<int> Path { get; internal init; } = Array.Empty<int>();
    public IReadOnlyList<int> Enter { get; internal set; } = Array.Empty<int>();
    public IReadOnlyList<int> Exit { get; internal set; } = Array.Empty<int>();
    public bool OrderChanged { get; internal set; }
    public IReadOnlyList<CircuitTrace.ProbeHit> RepresentativeHits { get; internal init; }
        = Array.Empty<CircuitTrace.ProbeHit>();
    public IReadOnlyList<CircuitTraceTimelineHit> OrderedOwnHits { get; internal init; }
        = Array.Empty<CircuitTraceTimelineHit>();
    public int? FocusSiteId { get; internal init; }
    public CircuitTrace.ProbeSite? FocusSite { get; internal init; }
    public IReadOnlyList<string> AlarmReasons { get; internal init; } = Array.Empty<string>();
    public long? ConditionIncidentId { get; internal init; }
    public CircuitTraceConditionKind? Condition { get; internal init; }
    public CircuitTraceConditionTransition? Transition { get; internal init; }
    public CircuitTraceTimelineDecisionPresentation Presentation { get; internal set; }
    public bool HasAuthoritativeState { get; internal init; }
    public bool StartedBeforeWindow { get; internal init; }
    public bool ContinuationUnknown { get; internal set; }
    public IReadOnlyList<CircuitTraceTimelineAlarmEvent> PointEvents { get; internal init; }
        = Array.Empty<CircuitTraceTimelineAlarmEvent>();
    public long? EventBurstId { get; internal set; }
}

internal sealed class CircuitTraceTimelineAlarmEvent
{
    public string Reason { get; internal init; } = "Unknown alarm";
    public int? SiteId { get; internal init; }
    public DateTime StartUtc { get; internal init; }
    public DateTime EndUtc { get; internal set; }
    public int OccurrenceCount { get; internal set; }
    public IReadOnlyList<long> DecisionIds { get; internal set; } = Array.Empty<long>();
}

internal sealed record CircuitTraceActivitySpan(
    TaskActivity Activity,
    DateTime StartUtc,
    DateTime EndUtc,
    int DecisionCount,
    int RawSegmentCount)
{
    public TimeSpan Duration => EndUtc - StartUtc;
}

internal sealed record CircuitTraceCycleEstimate(
    IReadOnlyList<TaskActivity> Pattern,
    int CompleteCycles,
    int TrailingSpanCount)
{
    public string Label => string.Join(" → ", Pattern) + $" ×{CompleteCycles}";
}

internal sealed class CircuitTraceTimelineEpisode
{
    public long Id { get; internal init; }
    public CircuitTraceMacroKey Key { get; internal init; }
    public string Label { get; internal init; } = "Unknown";
    public CircuitTraceTimelineSeverity Severity { get; internal init; }
    public CircuitTraceTimelineEpisodeKind Kind { get; internal init; }
    public CircuitTraceTimelineStatus Status { get; internal set; }
    public CircuitTraceConditionKind? Condition { get; internal init; }
    public bool StartedBeforeWindow { get; internal init; }
    public bool ContinuationUnknown { get; internal init; }
    public int TransitionCount { get; internal init; }
    public int ConfirmationCount { get; internal init; }
    public int OccurrenceCount { get; internal init; }
    public DateTime StartUtc { get; internal init; }
    public DateTime EndUtc { get; internal init; }
    public TimeSpan Duration => EndUtc - StartUtc;
    public int RawSegmentCount { get; internal init; }
    public int DecisionCount { get; internal init; }
    public int KillDelta { get; internal init; }
    public IReadOnlyList<CircuitTraceActivitySpan> ActivitySpans { get; internal init; }
        = Array.Empty<CircuitTraceActivitySpan>();
    public IReadOnlyDictionary<TaskActivity, int> ActivityCounts { get; internal init; }
        = new Dictionary<TaskActivity, int>();
    public IReadOnlyDictionary<TaskActivity, int> ActivityRawSegmentCounts { get; internal init; }
        = new Dictionary<TaskActivity, int>();
    public CircuitTraceCycleEstimate? CycleEstimate { get; internal init; }
    public int? FocusSiteId { get; internal init; }
    public CircuitTrace.ProbeSite? FocusSite { get; internal init; }
    public IReadOnlyList<string> AlarmReasons { get; internal init; } = Array.Empty<string>();
    public IReadOnlyList<CircuitTraceTimelineAlarmEvent> Events { get; internal init; }
        = Array.Empty<CircuitTraceTimelineAlarmEvent>();
    public IReadOnlyList<CircuitTraceTimelineDecision> Decisions { get; internal init; }
        = Array.Empty<CircuitTraceTimelineDecision>();
}

internal sealed class CircuitTraceTimelineResult
{
    public CircuitTraceTimelineResult(
        IReadOnlyList<CircuitTraceTimelineDecision> decisions,
        IReadOnlyList<CircuitTraceTimelineEpisode> episodes)
    {
        Decisions = decisions;
        Episodes = episodes;
    }

    public IReadOnlyList<CircuitTraceTimelineDecision> Decisions { get; }
    public IReadOnlyList<CircuitTraceTimelineEpisode> Episodes { get; }
    public DateTime? StartUtc => Decisions.Count == 0 ? null : Decisions[0].StartUtc;
    public DateTime? EndUtc => Decisions.Count == 0 ? null : Decisions[^1].EndUtc;
    public int RawSegmentCount => Decisions.Sum(decision => decision.RawSegmentCount);
    public int DecisionCount => Decisions.Count;
}
