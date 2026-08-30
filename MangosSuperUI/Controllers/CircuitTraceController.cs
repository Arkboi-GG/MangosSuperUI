using Microsoft.AspNetCore.Mvc;
using MangosSuperUI.BotLogic.Tracking;
using MangosSuperUI.Services;

namespace MangosSuperUI.Controllers;

// ════════════════════════════════════════════════════════════════════════════
// CircuitTraceController — the ONE trace query surface (docs/CIRCUIT_BOARD.md R10).
//
// Both consumers — the SuperUI visual layers (logic view + world-map layer) and
// the LLM context-pack — read THESE endpoints and nothing else, so the two
// views can never drift. Decoding is client-side: /Sites is the id→(file,line,
// description) registry, /Peek returns raw segments carrying site ids, world
// position, and value/note payloads.
//
//   GET  /CircuitTrace/Status          mode, armed guids, site count, ring count
//   GET  /CircuitTrace/Sites           the session site manifest (the decoder ring)
//   GET  /CircuitTrace/Peek/{guid}     recent sealed segments for one bot (ring copy, non-destructive)
//   GET  /CircuitTrace/Changes/{guid}  the same window folded to decision CHANGES (steppable history)
//   GET  /CircuitTrace/Timeline/{guid} long-horizon decisions grouped into human-scale episodes
//   POST /CircuitTrace/Arm/{guid}      arm one bot (persists; flushes continuously)
//   POST /CircuitTrace/Disarm/{guid}   disarm (flushes the tail first; persists)
//   POST /CircuitTrace/Mode?mode=off|shadow   global recording mode (persists)
//   POST /CircuitTrace/Dump/{guid}     manual ring dump to the daily JSONL (like the wedge auto-dump)
// ════════════════════════════════════════════════════════════════════════════
public class CircuitTraceController : Controller
{
    private readonly BotBrainService _brain;
    private readonly SourceIndexerService _sourceIndexer;
    private readonly CircuitTraceSourceService _circuitSources;

    public CircuitTraceController(
        BotBrainService brain,
        SourceIndexerService sourceIndexer,
        CircuitTraceSourceService circuitSources)
    {
        _brain = brain;
        _sourceIndexer = sourceIndexer;
        _circuitSources = circuitSources;
    }

    /// <summary>The Circuit Board viewer page (Bot Development → Circuit Board).</summary>
    public IActionResult Index() => View(_circuitSources.GetStatus());

    [HttpGet]
    public IActionResult Status() => Json(_brain.Circuit.Status());

    [HttpGet]
    public IActionResult Sites() =>
        Json(CircuitTrace.Sites.Select(s => new
        {
            id = s.Id,
            file = s.File,
            line = s.Line,
            desc = s.Description,
            circuitEpoch = s.RemoteEpoch,
            remoteId = s.RemoteId
        }));

    /// <summary>Small, safe source window for one server-registered probe. A caller
    /// supplies an id, never a path; the resolver confines C# and C++ to their own roots.</summary>
    [HttpGet]
    public IActionResult Source(int siteId, int before = 5, int after = 1)
    {
        CircuitTrace.ProbeSite? site = CircuitTrace.Sites.FirstOrDefault(s => s.Id == siteId);
        if (site == null) return NotFound(new { error = "Unknown circuit site." });

        CircuitTraceSourceSetupStatus sourceStatus = _circuitSources.GetStatus();
        string? csharpRoot = sourceStatus.CSharp.Ready ? sourceStatus.CSharp.Root : null;
        string? cppRoot = sourceStatus.Cpp.Ready ? sourceStatus.Cpp.Root : null;
        var index = _sourceIndexer.GetIndex();
        IEnumerable<string>? indexedPaths = index != null
            && SameRoot(index.SourcePath, cppRoot)
                ? index.Files.Keys
                : null;
        CircuitTraceSourceSnippet snippet = CircuitTraceSourceReader.Read(
            site,
            csharpRoot,
            cppRoot,
            indexedPaths,
            before,
            after);

        return Json(new
        {
            siteId,
            snippet.Available,
            snippet.Error,
            file = snippet.DisplayFile,
            line = snippet.TargetLine,
            startLine = snippet.StartLine,
            endLine = snippet.EndLine,
            snippet.Language,
            snippet.Lines,
            sourceVersion = sourceStatus.SourceVersion,
            // circuitEpoch is process identity, not a source hash. Be explicit rather
            // than claiming an unverified checkout is byte-identical to the running build.
            sourceExact = false,
            sourceNote = snippet.Available
                ? "Configured source checkout; build revision has not been hash-verified."
                : snippet.Error
        });
    }

    private static bool SameRoot(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right)) return false;
        try
        {
            string a = Path.TrimEndingDirectorySeparator(Path.GetFullPath(left));
            string b = Path.TrimEndingDirectorySeparator(Path.GetFullPath(right));
            return string.Equals(
                a,
                b,
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>Recent sealed segments for one bot, oldest first. Non-destructive ring copy —
    /// safe to poll. maxSegments caps the window (default 256 ≈ ~1 min of ticks).</summary>
    [HttpGet]
    public IActionResult Peek(int guid, int maxSegments = 256)
    {
        var segs = CircuitTrace.PeekSegments(guid, maxSegments);
        return Json(new
        {
            guid,
            mode = CircuitTrace.Mode.ToString().ToLowerInvariant(),
            armed = CircuitTrace.IsArmed(guid),
            segments = segs.Select(s => new
            {
                seq = s.Seq,
                k = s.Kind,
                t0 = s.StartUtc,
                t1 = s.EndUtc,
                goal = EnumName<BotLogic.Core.Goal>(s.Goal),
                step = s.Step,
                activity = EnumName<BotLogic.Core.TaskActivity>(s.Activity),
                taskKind = EnumName<BotLogic.Core.HeldTaskKind>(s.TaskKind),
                taskKills = s.TaskKills,
                pos = s.HasPos ? new { map = s.MapId, zone = s.ZoneId, x = s.X, y = s.Y, z = s.Z } : null,
                // 4th element = a foreign context id (see CircuitTraceHost.FlushSegments):
                // hits from another thread that landed in this segment. The viewer
                // refuses to draw an edge across a context change.
                h = s.Hits.Select(h => HitRow(s, h))
            })
        });
    }

    /// <summary>Change-collapsed decision timeline for one bot: consecutive segments whose
    /// decision path is identical are folded into ONE entry, so the reader steps through the
    /// moments the decision actually CHANGED — not every unchanged tick (the "constantly
    /// shifting board" is exactly what this replaces). Non-destructive ring copy, safe to call.
    ///
    /// A change's "path" is its ordered OWN-context site ids — foreign-thread hits are excluded
    /// (same rule the board uses for edge adjacency), so an unrelated bridge/chat hit landing in
    /// the segment never reads as a decision change. <c>enter</c>/<c>exit</c> are the site-id set
    /// differences from the previous DISTINCT path: what this decision newly touched, and what it
    /// stopped touching — the "what actually changed" signal. <c>count</c> is how many ticks the
    /// decision held before it changed.</summary>
    [HttpGet]
    public IActionResult Changes(int guid, int maxSegments = 512)
    {
        var segs = CircuitTrace.PeekSegments(guid, maxSegments);

        var changes = new List<object>();
        List<int>? prevPath = null;
        int ix = 0, i = 0;
        while (i < segs.Count)
        {
            var seg = segs[i];
            var path = OwnPath(seg);
            var kind = seg.Kind;

            // Extend the run while the next segment is the same kind AND same decision path.
            int run = 1;
            while (i + run < segs.Count
                   && segs[i + run].Kind == kind
                   && path.SequenceEqual(OwnPath(segs[i + run])))
                run++;

            var last = segs[i + run - 1];   // representative = newest in the run: current values/notes
            var enter = prevPath == null ? path : path.Where(p => !prevPath.Contains(p)).Distinct().ToList();
            var exit = prevPath == null ? new List<int>() : prevPath.Where(p => !path.Contains(p)).Distinct().ToList();

            changes.Add(new
            {
                i = ix++,
                k = kind,
                t0 = seg.StartUtc,
                t1 = last.EndUtc,
                count = run,                                        // ticks this decision held
                durMs = (last.EndUtc - seg.StartUtc).TotalMilliseconds,
                pos = last.HasPos ? new { map = last.MapId, zone = last.ZoneId, x = last.X, y = last.Y, z = last.Z } : null,
                path,
                enter,
                exit,
                h = last.Hits.Select(h => HitRow(last, h))          // decoded payload of the representative
            });

            prevPath = path;
            i += run;
        }

        return Json(new
        {
            guid,
            mode = CircuitTrace.Mode.ToString().ToLowerInvariant(),
            armed = CircuitTrace.IsArmed(guid),
            scanned = segs.Count,       // raw segments folded
            distinct = changes.Count,   // decision changes found
            changes
        });
    }

    /// <summary>
    /// Human-scale activity reader payload. The tracer's longer-lived run-length encoded
    /// decision horizon is grouped into macro episodes, while every decision retains its
    /// exact ordered own-context probe path for source-code drill-down. The response is
    /// shaped explicitly so internal timeline types and enum serialization cannot become
    /// accidental UI contracts.
    /// </summary>
    [HttpGet("/CircuitTrace/Timeline/{guid:int}")]
    public IActionResult Timeline(int guid, int maxRuns = 2048)
    {
        const int MaxTimelineRuns = 2048;
        int take = Math.Clamp(maxRuns, 1, MaxTimelineRuns);
        (List<CircuitTrace.DecisionRunSnapshot> runs, bool truncated) =
            CircuitTrace.PeekDecisionRuns(guid, take);

        // Site ids are session-local but unique inside this snapshot. Grouping makes the
        // read path defensive if a malformed manifest ever publishes the same id twice.
        IReadOnlyDictionary<int, CircuitTrace.ProbeSite> sitesById = CircuitTrace.Sites
            .GroupBy(site => site.Id)
            .ToDictionary(group => group.Key, group => group.Last());
        CircuitTraceTimelineResult timeline = CircuitTraceTimelineBuilder.Build(runs, sitesById);
        CircuitTraceTimelineDecision? newest = timeline.Decisions.LastOrDefault();

        return Json(new
        {
            guid,
            mode = CircuitTrace.Mode.ToString().ToLowerInvariant(),
            armed = CircuitTrace.IsArmed(guid),
            windowTruncated = truncated,
            start = timeline.StartUtc,
            end = timeline.EndUtc,
            rawSegmentCount = timeline.RawSegmentCount,
            decisionCount = timeline.DecisionCount,
            newestDecisionId = newest?.Id,
            newestSeq = newest?.ThroughSeq,
            episodes = timeline.Episodes.Select(episode => new
            {
                id = episode.Id,
                label = episode.Label,
                severity = episode.Severity.ToString().ToLowerInvariant(),
                kind = TimelineEpisodeKind(episode.Kind),
                status = episode.Status.ToString().ToLowerInvariant(),
                condition = episode.Condition?.ToString().ToLowerInvariant(),
                startedBeforeWindow = episode.StartedBeforeWindow,
                continuationUnknown = episode.ContinuationUnknown,
                transitionCount = episode.TransitionCount,
                confirmationCount = episode.ConfirmationCount,
                occurrenceCount = episode.OccurrenceCount,
                start = episode.StartUtc,
                end = episode.EndUtc,
                durationMs = episode.Duration.TotalMilliseconds,
                rawSegmentCount = episode.RawSegmentCount,
                decisionCount = episode.DecisionCount,
                killDelta = episode.KillDelta,
                state = TimelineMacroState(episode.Key),
                activitySpans = episode.ActivitySpans.Select(span => new
                {
                    activity = span.Activity.ToString(),
                    start = span.StartUtc,
                    end = span.EndUtc,
                    durationMs = span.Duration.TotalMilliseconds,
                    decisionCount = span.DecisionCount,
                    rawSegmentCount = span.RawSegmentCount
                }),
                activityTotals = episode.ActivityCounts
                    .OrderBy(pair => (int)pair.Key)
                    .Select(pair => new
                    {
                        activity = pair.Key.ToString(),
                        decisionCount = pair.Value,
                        rawSegmentCount = episode.ActivityRawSegmentCounts.GetValueOrDefault(pair.Key)
                    }),
                cycle = episode.CycleEstimate == null ? null : new
                {
                    pattern = episode.CycleEstimate.Pattern.Select(activity => activity.ToString()),
                    completeCycles = episode.CycleEstimate.CompleteCycles,
                    trailingSpanCount = episode.CycleEstimate.TrailingSpanCount,
                    label = episode.CycleEstimate.Label
                },
                focusSiteId = episode.FocusSiteId,
                focusSite = TimelineSite(episode.FocusSite),
                alarmReasons = episode.AlarmReasons,
                events = episode.Events.Select(point => new
                {
                    reason = point.Reason,
                    label = point.Reason,
                    siteId = point.SiteId,
                    site = point.SiteId is { } siteId
                           && sitesById.TryGetValue(siteId, out CircuitTrace.ProbeSite? site)
                        ? TimelineSite(site)
                        : null,
                    start = point.StartUtc,
                    end = point.EndUtc,
                    durationMs = (point.EndUtc - point.StartUtc).TotalMilliseconds,
                    occurrenceCount = point.OccurrenceCount,
                    decisionIds = point.DecisionIds
                }),
                decisions = episode.Decisions.Select(TimelineDecision)
            })
        });
    }

    private static object TimelineDecision(CircuitTraceTimelineDecision decision) => new
    {
        id = decision.Id,
        throughSeq = decision.ThroughSeq,
        kind = decision.Kind,
        label = decision.Label,
        severity = decision.Severity.ToString().ToLowerInvariant(),
        start = decision.StartUtc,
        end = decision.EndUtc,
        durationMs = decision.Duration.TotalMilliseconds,
        rawSegmentCount = decision.RawSegmentCount,
        state = TimelineState(decision.State),
        taskKillsStart = decision.TaskKillsStart,
        taskKillsEnd = decision.TaskKillsEnd,
        path = decision.Path,
        enter = decision.Enter,
        exit = decision.Exit,
        orderChanged = decision.OrderChanged,
        focusSiteId = decision.FocusSiteId,
        focusSite = TimelineSite(decision.FocusSite),
        alarmReasons = decision.AlarmReasons,
        conditionIncidentId = decision.ConditionIncidentId,
        condition = decision.Condition?.ToString().ToLowerInvariant(),
        presentation = decision.Presentation.ToString().ToLowerInvariant(),
        transition = decision.Transition?.ToString().ToLowerInvariant(),
        authoritativeState = decision.HasAuthoritativeState,
        startedBeforeWindow = decision.StartedBeforeWindow,
        continuationUnknown = decision.ContinuationUnknown,
        events = decision.PointEvents.Select(point => new
        {
            reason = point.Reason,
            label = point.Reason,
            siteId = point.SiteId,
            start = point.StartUtc,
            end = point.EndUtc,
            occurrenceCount = point.OccurrenceCount,
            decisionIds = point.DecisionIds
        }),
        hits = decision.OrderedOwnHits.Select(hit => new
        {
            order = hit.Order,
            siteId = hit.SiteId,
            seq = hit.Hit.Seq,
            value = hit.Hit.Value,
            note = hit.Hit.Note,
            site = TimelineSite(hit.Site)
        })
    };

    private static string TimelineEpisodeKind(CircuitTraceTimelineEpisodeKind kind)
        => kind switch
        {
            CircuitTraceTimelineEpisodeKind.Routine => "routine",
            CircuitTraceTimelineEpisodeKind.Condition => "condition",
            CircuitTraceTimelineEpisodeKind.EventBurst => "eventBurst",
            _ => "routine"
        };

    private static object TimelineMacroState(CircuitTraceMacroKey state) => new
    {
        goal = EnumName<BotLogic.Core.Goal>(state.Goal),
        objectiveKind = EnumName<BotLogic.Core.ObjectiveKind>(state.ObjectiveKind),
        objectiveSource = EnumName<BotLogic.Core.ObjectiveSource>(state.ObjectiveSource),
        objectiveQuestId = state.ObjectiveQuestId,
        objectiveSlot = state.ObjectiveSlot,
        objectiveCreatureEntry = state.ObjectiveCreatureEntry,
        objectiveNpcEntry = state.ObjectiveNpcEntry
    };

    private static object TimelineState(CircuitTraceTimelineState state) => new
    {
        goal = EnumName<BotLogic.Core.Goal>(state.Goal),
        step = state.Step,
        taskKind = EnumName<BotLogic.Core.HeldTaskKind>(state.TaskKind),
        activity = EnumName<BotLogic.Core.TaskActivity>(state.Activity),
        taskKills = state.TaskKills,
        objectiveKind = EnumName<BotLogic.Core.ObjectiveKind>(state.ObjectiveKind),
        objectiveSource = EnumName<BotLogic.Core.ObjectiveSource>(state.ObjectiveSource),
        objectiveQuestId = state.ObjectiveQuestId,
        objectiveSlot = state.ObjectiveSlot,
        objectiveCreatureEntry = state.ObjectiveCreatureEntry,
        objectiveNpcEntry = state.ObjectiveNpcEntry,
        inCombat = state.InCombat,
        dead = state.Dead
    };

    private static object? TimelineSite(CircuitTrace.ProbeSite? site) => site == null ? null : new
    {
        id = site.Id,
        file = site.File,
        line = site.Line,
        desc = site.Description,
        circuitEpoch = site.RemoteEpoch,
        remoteId = site.RemoteId
    };

    /// <summary>One hit projected to the compact wire row shared by /Peek and /Changes:
    /// [id] | [id,value] | [id,value,note] | [id,value,note,ctx] where the 4th element marks a
    /// foreign-context hit (another thread wrote it into this segment).</summary>
    private static object?[] HitRow(CircuitTrace.TickSegment seg, CircuitTrace.ProbeHit h)
    {
        int primary = seg.PrimaryCtx != 0
            ? seg.PrimaryCtx
            : seg.Hits.Count > 0 ? seg.Hits[0].Ctx : 0;
        return h.Ctx != primary ? new object?[] { h.SiteId, h.Value, h.Note, h.Ctx }
            : h.Note != null ? new object?[] { h.SiteId, h.Value, h.Note }
            : h.Value != null ? new object?[] { h.SiteId, h.Value }
            : new object?[] { h.SiteId };
    }

    /// <summary>The ordered own-context site ids of a segment — the decision path, with
    /// foreign-thread hits removed so path identity reflects the bot's own control flow.</summary>
    private static List<int> OwnPath(CircuitTrace.TickSegment seg)
    {
        var outp = new List<int>(seg.Hits.Count);
        if (seg.Hits.Count == 0) return outp;
        int primary = seg.PrimaryCtx != 0 ? seg.PrimaryCtx : seg.Hits[0].Ctx;
        foreach (var h in seg.Hits)
            if (h.Ctx == primary) outp.Add(h.SiteId);
        return outp;
    }

    private static string? EnumName<TEnum>(int value) where TEnum : struct, Enum =>
        value >= 0 && Enum.IsDefined(typeof(TEnum), value)
            ? ((TEnum)Enum.ToObject(typeof(TEnum), value)).ToString()
            : null;

    [HttpPost]
    public async Task<IActionResult> Arm(int guid)
    {
        await _brain.Circuit.ArmAsync(guid);
        return Json(new { ok = true, armed = CircuitTrace.ArmedGuids() });
    }

    [HttpPost]
    public async Task<IActionResult> Disarm(int guid)
    {
        await _brain.Circuit.DisarmAsync(guid);
        return Json(new { ok = true, armed = CircuitTrace.ArmedGuids() });
    }

    [HttpPost]
    public async Task<IActionResult> Mode(string mode)
    {
        var parsed = string.Equals(mode, "shadow", StringComparison.OrdinalIgnoreCase)
            ? CircuitTrace.TraceMode.Shadow
            : CircuitTrace.TraceMode.Off;
        await _brain.Circuit.SetModeAsync(parsed);
        return Json(new { ok = true, mode = parsed.ToString().ToLowerInvariant() });
    }

    /// <summary>Manual dump: flush this bot's whole ring to the daily JSONL now (the same
    /// path the wedge auto-dump uses). Works for any bot while mode is shadow.</summary>
    [HttpPost]
    public IActionResult Dump(int guid)
    {
        CircuitTrace.RequestDumpForced(guid, "manual");   // operator asked for THIS bot — never rate-limited
        return Json(new { ok = true, note = "queued; the brain loop flushes it within ~250ms" });
    }
}
