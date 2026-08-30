using System.Collections.Concurrent;
using Dapper;
using MangosSuperUI.BotLogic.Core;
using MangosSuperUI.BotLogic.Brain;
using MangosSuperUI.BotLogic.Data;
using MangosSuperUI.BotLogic.Tracking;
using MangosSuperUI.Models;

namespace MangosSuperUI.Services;

/// <summary>
/// The behavioral engine HOST (rebuild — Strategy B, Phase 1).
///
/// Ownership is inverted from the old design: this service no longer scatters
/// control across DecisionEngine + per-domain phase state. It is a thin host that
///   1. keeps the bridge roster mirrored into BotIdentity (for the dashboard +
///      grouping, which is rebuilt last) and into BotContext (the new live-state
///      keystone the spine drives),
///   2. drives the spine — BotBrain (driver) → BotExecutor (issue + WAIT) →
///      BotSupervisor (stall) — one BotContext per bot per tick, and
///   3. prints the FleetReport: one bounded, context-window-sized fleet picture
///      that replaces grepping six log streams.
///
/// Phase 1 is spine, no behavior: every bot is held in Goal.Idle and the only
/// live correction is the Supervisor's universal deadline rule. Goal selection
/// and per-goal planners land in Phases 2+ inside BotBrain, untouched here.
///
/// Retained for the dashboard / grouping (carved out in their own phases):
///   GroupManager + group endpoints, BotIdentity roster (AllBots), personality
///   load/roll/persist, GetBotBrainSummary, BrainEnabled. (The old flight
///   recorder + story rider were removed 2026-08-26 — the circuit board
///   [docs/CIRCUIT_BOARD.md, CircuitTrace/CircuitTraceHost] replaces both.)
/// Shed: DecisionEngine + all domains as the driver, the grouping batch/errand
///   fan-out, known-good destinations, in-flight MOVE_TO recovery, fleet
///   diagnostics, and the flight recorder.
///
/// The bridge contract is unchanged: it calls SetBrainService(this) and routes
/// every EVENT/CHAT_RECV through HandleBridgeEventAsync(guid, BotEvent).
/// </summary>
public class BotBrainService : BackgroundService
{
    private readonly BotBridgeService _bridge;
    private readonly ConnectionFactory _db;
    private readonly BotStateTracker _tracker;
    private readonly QuirkLoader _quirkLoader;
    private readonly BotBrainDbInit _dbInit;
    private readonly ILogger<BotBrainService> _logger;
    private readonly BotBrain _driver;           // the spine (BotBrain → BotExecutor/BotSupervisor)
    private readonly GroupManager _groupManager;
    private readonly QuestGraphLoader _quests;    // the shared quest graph -> handed to the god-bot pre-pass for union objective selection
    private readonly ZoneSafetyMap _safety;       // §5.1 weakest-member travel gate (path creature-level)
    private readonly CreatureSpawnLoader _spawns; // Scatter Build 2: real-spawn anchor sampler -> god-bot shared-objective dispersal
    private readonly ZoneDataLoader _zoneData;    // GAP G (2026-07-02): nearest vendor/repair NPC for the whole-group vendor errand (GroupCoordinator.Update)
    private readonly BotFallRecorder _fallRecorder; // always-on void/fall black box — Observe(ctx) each tick, flush-only-on-fall

    // Roster mirrors. _bots feeds the dashboard + grouping; _contexts is the
    // live-state the spine drives. One entry per connected bot in both.
    private readonly ConcurrentDictionary<int, BotIdentity> _bots = new();
    private readonly ConcurrentDictionary<int, BotContext> _contexts = new();
    private readonly ConcurrentDictionary<long, PendingGroupMutation> _pendingGroupMutations = new();
    private readonly SemaphoreSlim _groupMutationGate = new(1, 1);

    private readonly HashSet<int> _initializedGuids = new();
    private readonly object _initializedGuidsLock = new();
    private readonly ConcurrentDictionary<int, DateTime> _disconnectedAt = new();

    private volatile bool _brainEnabled = false;
    private DateTime _lastFleetLog = DateTime.MinValue;

    private const double EVICT_DISCONNECT_SEC = 60.0;
    private const double FLEET_LOG_INTERVAL_SEC = 30.0;
    internal static readonly TimeSpan GroupMutationAckDeadline = TimeSpan.FromSeconds(15);

    public BotBrainService(
        BotBridgeService bridge,
        ConnectionFactory db,
        BotStateTracker tracker,
        QuirkLoader quirkLoader,
        BotBrainDbInit dbInit,
        ILogger<BotBrainService> logger,
        ILoggerFactory loggerFactory,
        BotBrain driver,
        QuestGraphLoader quests,
        ZoneSafetyMap safety,
        CreatureSpawnLoader spawns,
        ZoneDataLoader zoneData,
        BotFallRecorder fallRecorder,
        BrainLoopMetrics loopMetrics)
    {
        _loopMetrics = loopMetrics;
        _bridge = bridge;
        _db = db;
        _tracker = tracker;
        _quirkLoader = quirkLoader;
        _dbInit = dbInit;
        _logger = logger;
        _driver = driver;
        _quests = quests;
        _safety = safety;
        _spawns = spawns;
        _zoneData = zoneData;
        _fallRecorder = fallRecorder;
        _groupManager = new GroupManager(_db, loggerFactory.CreateLogger<GroupManager>());
        _circuit = new CircuitTraceHost(_db, loggerFactory.CreateLogger<CircuitTraceHost>());
    }

    // Shared with RuntimeScaleDiagnosticsService. Unlike GroupManager/CircuitTraceHost
    // this cannot be constructed inline: the point is that another service reads it.
    private readonly BrainLoopMetrics _loopMetrics;

    // Circuit-board trace host (docs/CIRCUIT_BOARD.md Phase 2): settings + JSONL flush for
    // the CircuitTrace probes. Created here like GroupManager (no DI churn); the
    // controller reaches it through this property.
    private readonly CircuitTraceHost _circuit;
    public CircuitTraceHost Circuit => _circuit;

    // ==================== Public API (controller / hub) ====================

    /// <summary>Group manager — exposed for the dashboard controller.</summary>
    public GroupManager GroupManager => _groupManager;

    /// <summary>
    /// Whether the spine DRIVES (selects goals, issues commands, supervises).
    /// When false, bots are still sensed each tick so FleetReport stays live, but
    /// no command is issued and no stall is raised. Toggling off resets session
    /// roster state (it re-syncs from the bridge on the next loop). In Phase 1 the
    /// spine only holds Idle, so this gates nothing visible yet — from Phase 2 it
    /// gates planner execution.
    /// </summary>
    public bool BrainEnabled
    {
        get => _brainEnabled;
        set
        {
            _brainEnabled = value;
            _logger.LogInformation("BotBrain: driving {State}", value ? "ENABLED" : "DISABLED");

            if (!value)
            {
                CircuitTrace.Hit(0, "host: driving disabled, roster cleared", _contexts.Count);
                var count = _contexts.Count;
                _bots.Clear();
                _contexts.Clear();
                lock (_initializedGuidsLock) { _initializedGuids.Clear(); }
                _disconnectedAt.Clear();
                _logger.LogInformation("BotBrain: cleared {Count} bot entries on disable — next sync starts clean", count);
            }
        }
    }

    /// <summary>Live BotIdentity roster — consumed by the dashboard (BrainStatus).</summary>
    public IReadOnlyDictionary<int, BotIdentity> AllBots => _bots;

    public BotIdentity? GetBotIdentity(int guid) =>
        _bots.TryGetValue(guid, out var bot) ? bot : null;

    public int ActiveBotCount => _contexts.Count;

    /// <summary>
    /// Drop a bot from every in-memory roster mirror immediately, mirroring the stale-bot
    /// eviction path in SyncBotRosterAsync. Called after a DB-level bot delete so the
    /// dashboard forgets it right away instead of needing a mangossuperui restart.
    /// </summary>
    public void EvictBot(int guid)
    {
        _groupManager.RemoveFromGroup(guid);
        _bots.TryRemove(guid, out _);
        _contexts.TryRemove(guid, out _);
        lock (_initializedGuidsLock) { _initializedGuids.Remove(guid); }
        _disconnectedAt.TryRemove(guid, out _);
        _tracker.Remove(guid);
        _fallRecorder.Forget(guid);
        CircuitTrace.Forget(guid);
    }

    /// <summary>
    /// Per-bot brain summary for the dashboard. Shape preserved from the old design
    /// so the existing UI keeps rendering; the values now reflect the Idle spine.
    /// </summary>
    public object? GetBotBrainSummary(int guid)
    {
        if (!_bots.TryGetValue(guid, out var bot)) return null;   // cb:fold read-only dashboard projection
        var bs = _bridge.GetBotState(guid);
        return new
        {
            guid = bot.Guid,
            name = bot.Name,
            activity = bot.CurrentActivity.Type.ToString(),
            activityDuration = bot.CurrentActivity.MinutesInState,
            subPhase = bot.CurrentActivity.SubPhase,
            contextTag = bot.CurrentActivity.ContextTag,
            personality = bot.Personality.ToSummary(),
            tickBase = bot.Personality.DecisionTickBase,
            nextTick = bot.NextDecisionTick,
            copper = bs?.Copper ?? 0,
            freeSlots = bs?.FreeSlots ?? 16,
            totalSlots = bs?.TotalSlots ?? 16,
            inventoryCount = bot.ShadowInventory.Count,
            hasUnlearnedSpells = bot.HasUnlearnedSpells,
            questProgress = bot.CurrentQuestProgress,
            activeQuestId = bot.ActiveQuestId,
            pendingAction = bot.PendingAction != null ? new
            {
                returnTo = bot.PendingAction.ReturnTo.ToString(),
                subPhase = bot.PendingAction.SubPhase,
                questId = bot.PendingAction.QuestId
            } : null
        };
    }

    /// <summary>
    /// The one-shot fleet picture (§3.6): rollups + one bounded line per bot.
    /// Exposed for a future endpoint; also logged on an interval (see the loop).
    /// </summary>
    /// <summary>Rollups plus the bounded row table. On-demand only — the periodic
    /// log emits <see cref="GetFleetSummary"/> instead.</summary>
    public string GetFleetReport(int maxRows = FleetReport.MaxRows)
        => FleetReport.RenderDetailed(_contexts.Values.ToList(), maxRows);

    /// <summary>Rollups only — cheap enough to log on an interval at any fleet size.</summary>
    public string GetFleetSummary() => FleetReport.RenderSummary(_contexts.Values.ToList());

    // ==================== Live spine state (UI: the "Live" tab) ====================
    // The structured, per-bot projection of BotContext — the same picture FleetReport
    // renders as text, but as JSON the dashboard can render and tick client-side. This
    // is the spine's live state (Goal/Step/why/WAIT/Failure/timers/typed scratch), NOT
    // the old DecisionEngine summary that GetBotBrainSummary serves.

    /// <summary>Structured live state for one bot, or null if it has no live context.</summary>
    public object? GetLiveState(int guid) =>
        _contexts.TryGetValue(guid, out var ctx) ? ProjectLive(ctx) : null;

    /// <summary>Structured live state for the whole fleet (stalled first, then by name).</summary>
    public IReadOnlyList<object> GetLiveFleet() =>
        _contexts.Values
            .OrderByDescending(c => c.Stalled)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .Select(ProjectLive)
            .ToList();

    private static object ProjectLive(BotContext c)
    {
        var now = DateTime.UtcNow;
        return new
        {
            guid = c.Guid,
            name = c.Name,
            level = c.Level,

            // intent
            goal = c.Goal.ToString(),
            step = c.Step,
            why = c.GoalReason,

            // timers
            timeInGoalSec = (int)c.TimeInGoalSec,
            timeInStepSec = (int)c.TimeInStepSec,
            noProgressSec = (int)c.TimeSinceProgressSec,
            lastKillSec = AgoOrNull(c.LastKillUtc, now),
            lastQuestSec = AgoOrNull(c.LastQuestAdvanceUtc, now),
            lastLevelSec = AgoOrNull(c.LastLevelUtc, now),

            // sensory
            hpPct = (int)Math.Round(c.HpPct * 100),
            manaPct = (int)Math.Round(c.ManaPct * 100),
            pos = new { x = c.Pos.X, y = c.Pos.Y, z = c.Pos.Z },
            mapId = c.MapId,
            zoneId = c.ZoneId,
            durability = c.Durability,
            freeSlots = c.FreeSlots,
            copper = c.Copper,
            inCombat = c.InCombat,
            dead = c.Dead,
            sensoryFeed = new
            {
                status = c.SensoryFeedStale
                    ? "stale"
                    : c.LastStateReceivedUtc == DateTime.MinValue ? "hydrating" : "fresh",
                stale = c.SensoryFeedStale,
                lastStateReceivedUtc = c.LastStateReceivedUtc == DateTime.MinValue
                    ? (DateTime?)null
                    : c.LastStateReceivedUtc,
                stateAgeSec = AgoOrNull(c.LastStateReceivedUtc, now)
            },
            bridgeProtocol = new
            {
                actual = c.BridgeProtocol,
                required = BotBridgeService.RequiredCorrelatedOutcomeProtocol,
                compatible = !c.BridgeProtocolIncompatible
            },
            externalControl = c.Possessed ? "possessed" : c.Conscripted ? "conscripted" : null,

            // combat directive (grouping §3.6) -- the coordinator's per-tick stamp. Assist = this bot
            // is focus-firing the anchor member's victim (anchorGuid == self => this bot IS the anchor;
            // the team assists it). anchorGuid joins to a name client-side from the same fleet list,
            // like classId. null = solo / unstamped.
            combat = c.CombatDirective.IsActive
                ? new { mode = c.CombatDirective.Mode.ToString(), anchorGuid = c.CombatDirective.AnchorGuid }
                : null,

            // where it's driving
            target = c.Target.HasValue
                ? new { x = c.Target.Value.X, y = c.Target.Value.Y, z = c.Target.Value.Z, map = c.Target.Value.Map }
                : null,
            distToTarget = c.Target.HasValue ? (int?)(int)c.DistToTarget : null,

            // THE WAIT — the spine
            pending = c.Pending == null ? null : new
            {
                cmd = c.Pending.CommandType,
                expect = c.Pending.ExpectedEvent,
                ageSec = (int)c.Pending.AgeSec,
                secsToDeadline = (int)Math.Round((c.Pending.DeadlineUtc - now).TotalSeconds),
                isObjectiveGrind = c.Pending.IsObjectiveGrind,
                interruptible = c.Pending.RescanAtUtc.HasValue
            },

            // last negative outcome
            failure = c.Failure == null ? null : new
            {
                cmd = c.Failure.CommandType,
                reason = c.Failure.Reason,
                ageSec = (int)c.Failure.AgeSec,
                dest = c.Failure.Dest.HasValue
                    ? new { x = c.Failure.Dest.Value.X, y = c.Failure.Dest.Value.Y, z = c.Failure.Dest.Value.Z, map = c.Failure.Dest.Value.Map }
                    : null,
                danger = c.Failure.DangerLevel,
                questId = c.Failure.QuestId
            },

            // supervisor verdict
            stall = c.Stalled
                ? new { reason = c.StallReason, sinceSec = (int)(now - c.StalledSinceUtc).TotalSeconds }
                : null,

            // the active goal's typed scratch only
            scratch = ProjectScratch(c, now)
        };
    }

    // Recovery > vendor errand > quest batch > grind. Only one goal's scratch is live.
    private static object ProjectScratch(BotContext c, DateTime now)
    {
        if (c.Maintenance is { } m)
        {   // cb:fold read-only dashboard projection
            string phase =
                !m.RezSent ? "rez-wait"
                : c.Dead ? "resurrecting"
                : (m.RelocateSent && !m.RelocateDone) ? "relocate"
                : (m.IdleFired && !m.HealDone) ? "heal"
                : m.HealDone ? "done"
                : "post-rez";

            return new
            {
                kind = "maintenance",
                phase,
                deathLoop = m.DeathLoop,
                escalated = m.Escalated,
                rezSent = m.RezSent,
                relocateSent = m.RelocateSent,
                relocateDone = m.RelocateDone,
                healDone = m.HealDone,
                deadForSec = m.DeadSinceUtc == default ? (int?)null : (int)(now - m.DeadSinceUtc).TotalSeconds,
                rezInSec = m.RezAtUtc == default ? (int?)null : (int)Math.Round((m.RezAtUtc - now).TotalSeconds)
            };
        }

        if (c.Service is { } sv && sv.Phase != VendorPhase.None)
        {   // cb:fold read-only dashboard projection
            return new
            {
                kind = "vendor",
                phase = sv.Phase.ToString(),
                npcEntry = sv.TargetNpcEntry,
                canRepair = sv.CanRepair,
                startedSec = sv.StartedUtc == default ? (int?)null : (int)(now - sv.StartedUtc).TotalSeconds,
                target = new { x = sv.TargetPos.X, y = sv.TargetPos.Y, z = sv.TargetPos.Z, map = sv.TargetPos.Map }
            };
        }

        if (c.Quest is { } q && q.Batch.Count > 0)
        {   // cb:fold read-only dashboard projection
            return new
            {
                kind = "quest",
                count = q.Batch.Count,
                activeId = q.Active?.QuestId ?? 0,
                activeSlot = q.ActiveSlot,
                batch = q.Batch.Select(bq => new
                {
                    id = bq.QuestId,
                    title = bq.Node?.Title ?? "",
                    accepted = bq.Accepted,
                    turnedIn = bq.TurnedIn,
                    deferred = bq.Deferred,
                    failed = bq.Failed,
                    force = bq.ForceMode
                }).ToList(),
                active = ProjectActiveQuest(c)
            };
        }

        if (c.Grind is { } g)
        {   // cb:fold read-only dashboard projection
            return new
            {
                kind = "grind",
                creatureEntry = g.CreatureEntry,
                killGoal = g.KillGoal,
                killCount = g.KillCount,
                radius = g.Radius,
                center = new { x = g.AreaCenter.X, y = g.AreaCenter.Y, z = g.AreaCenter.Z, map = g.AreaCenter.Map }
            };
        }

        return new { kind = "none" };
    }

    // The active quest's human-readable detail: title, where to accept / hand in, and
    // each objective with its resolved name, required/current counts, and world coords.
    // Names come straight from the quest graph (TargetName/ItemName/CreatureName/GoName) —
    // no DB hit. Kill "have" counts come from the STATE quest-log snapshot (slot-1 indexed).
    private static object? ProjectActiveQuest(BotContext c)
    {
        var aq = c.Quest?.Active;
        var node = aq?.Node;
        if (aq == null || node == null) return null;   // cb:fold read-only dashboard projection

        c.QuestLog.TryGetValue(aq.QuestId, out var log);
        var objectives = new List<object>();

        // kill / interact objectives
        for (int i = 0; i < node.Objectives.Length; i++)
        {
            var o = node.Objectives[i];
            int have = (log != null && o.Slot >= 1 && o.Slot <= 4) ? log.MobCounts[o.Slot - 1] : 0;
            objectives.Add(new
            {
                slot = o.Slot,
                kind = o.IsGameObject ? "interact" : "kill",
                name = !string.IsNullOrEmpty(o.TargetName) ? o.TargetName
                       : o.IsGameObject ? ("Object #" + o.GameObjectEntry) : ("Creature #" + o.CreatureEntry),
                entry = o.IsGameObject ? o.GameObjectEntry : o.CreatureEntry,
                need = o.Count,
                have = (int?)have,
                from = (string?)null,
                x = o.GrindX,
                y = o.GrindY,
                map = o.GrindMap,
                active = i == c.Quest!.ActiveSlot
            });
        }

        // Item counts exist in the STATE quest-log snapshot but this dashboard projection does not
        // currently expose them; keep the omission explicit instead of blaming the retired pull.
        foreach (var it in node.ItemObjectives)
        {
            var src = it.BestDropSource;
            var go = it.BestGoSource;
            objectives.Add(new
            {
                slot = it.Slot,
                kind = "gather",
                name = !string.IsNullOrEmpty(it.ItemName) ? it.ItemName : ("Item #" + it.ItemId),
                entry = it.ItemId,
                need = it.Count,
                have = (int?)null,
                from = src?.CreatureName ?? go?.GoName,
                x = src?.GrindX ?? go?.X ?? 0f,
                y = src?.GrindY ?? go?.Y ?? 0f,
                map = src?.GrindMap ?? go?.Map ?? c.MapId,
                active = false
            });
        }

        return new
        {
            id = aq.QuestId,
            title = node.Title,
            level = node.QuestLevel,
            giver = node.Giver == null ? null
                : new { name = node.Giver.Name, x = node.Giver.X, y = node.Giver.Y, map = node.Giver.Map },
            turnIn = node.TurnIn == null ? null
                : new { name = node.TurnIn.Name, x = node.TurnIn.X, y = node.TurnIn.Y, map = node.TurnIn.Map },
            objectives
        };
    }

    private static int? AgoOrNull(DateTime utc, DateTime now)
        => utc == default ? (int?)null : (int)(now - utc).TotalSeconds;

    // -------------------- Grouping (delegates to GroupManager) --------------------

    /// <summary>
    /// Set grouping mode from the dashboard. Switching Off first disbands every
    /// group through the same correlated transaction as the explicit endpoint;
    /// a rejection or unknown outcome leaves the mode enabled for reconciliation.
    /// </summary>
    public async Task<GroupMutationBatchResult> SetGroupingModeAsync(GroupingMode mode)
    {
        await _groupMutationGate.WaitAsync();
        try
        {
            GroupMutationBatchResult disbands = mode == GroupingMode.Off
                ? await DisbandAllGroupsCoreAsync()
                : new GroupMutationBatchResult { Results = Array.Empty<GroupMutationResult>() };
            if (!disbands.Succeeded)
            {
                CircuitTrace.Hit(0, "host: grouping mode change withheld after unresolved disband");
                return disbands;
            }

            _groupManager.Mode = mode;
            await PersistGroupingModeAsync(mode);
            _groupManager.EnrichAllBots(_bots.Values);
            return disbands;
        }
        finally
        {
            _groupMutationGate.Release();
        }
    }

    /// <summary>
    /// Form a group only after the core acknowledges the exact requested member
    /// set on the same session and cbt. Rejection/no-send/timeout leaves C# and
    /// the DB untouched.
    /// </summary>
    public async Task<GroupMutationResult> FormGroupAsync(int leaderGuid, params int[] followerGuids)
    {
        await _groupMutationGate.WaitAsync();
        try
        {
            return await FormGroupCoreAsync(leaderGuid, followerGuids);
        }
        finally
        {
            _groupMutationGate.Release();
        }
    }

    private async Task<GroupMutationResult> FormGroupCoreAsync(int leaderGuid, int[] followerGuids)
    {
        int[] followers = followerGuids.ToArray();
        int[] expectedMembers = followers.Append(leaderGuid).OrderBy(guid => guid).ToArray();
        if (!_groupManager.CanFormGroup(leaderGuid, followers))
        {
            CircuitTrace.Hit(leaderGuid, "host: form group rejected by local preflight");
            return GroupResult(
                GroupMutationStatus.Rejected,
                "local_preflight_rejected",
                GroupMutationKind.Form,
                leaderGuid,
                expectedMembers);
        }

        var pending = CreatePendingGroupMutation(
            GroupMutationKind.Form,
            leaderGuid,
            expectedMembers,
            out string refusal);
        if (pending == null)
            return GroupResult(   // cb:fold refusal is probed by pending factory
                GroupMutationStatus.NotSent,
                refusal,
                GroupMutationKind.Form,
                leaderGuid,
                expectedMembers);

        GroupMutationResult outcome = await SendGroupMutationAsync(
            pending,
            new BridgeCommand("FORM_GROUP", new
            {
                leader_guid = leaderGuid,
                member_guids = followers
            }));
        if (!outcome.Succeeded)
            return outcome;   // cb:fold terminal outcome probed by send helper

        // The dashboard mutation gate keeps other form/disband requests out while
        // the ACK is in flight. Revalidate anyway: roster eviction/mode recovery
        // is allowed to run independently and must not turn an ACK into a blind
        // overwrite of newer local topology.
        BotGroup? group = _groupManager.FormGroup(leaderGuid, followers);
        if (group == null)
        {
            CircuitTrace.Hit(leaderGuid, "host: FORM_GROUP ACK could not commit local topology", pending.CorrelationId);
            _logger.LogError(
                "BotBrain: core ACKed FORM_GROUP cbt={Cbt}, but local topology changed before commit; reconciliation required",
                pending.CorrelationId);
            return GroupResult(
                GroupMutationStatus.OutcomeUnknown,
                "local_topology_changed_after_ack",
                pending.Kind,
                pending.LeaderGuid,
                pending.MemberGuids,
                pending.CorrelationId);
        }

        EnrichGroupMembers(group.MemberGuids);
        await _groupManager.SaveGroupsToDbAsync();
        CircuitTrace.Hit(leaderGuid, "host: FORM_GROUP exact ACK committed", pending.CorrelationId);
        return outcome with { Group = group };
    }

    /// <summary>Disband a group only after an exact correlated core acknowledgement.</summary>
    public async Task<GroupMutationResult> DisbandGroupAsync(int groupId)
    {
        await _groupMutationGate.WaitAsync();
        try
        {
            return await DisbandGroupCoreAsync(groupId);
        }
        finally
        {
            _groupMutationGate.Release();
        }
    }

    private async Task<GroupMutationResult> DisbandGroupCoreAsync(int groupId)
    {
        BotGroup? group = _groupManager.GetAllGroups().FirstOrDefault(candidate => candidate.GroupId == groupId);
        if (group == null)
        {
            CircuitTrace.Hit(0, "host: disband rejected, group not found");
            return GroupResult(
                GroupMutationStatus.Rejected,
                "group_not_found",
                GroupMutationKind.Disband,
                0,
                Array.Empty<int>());
        }

        int leaderGuid = group.LeaderGuid;
        int[] expectedMembers = group.MemberGuids.OrderBy(guid => guid).ToArray();
        var pending = CreatePendingGroupMutation(
            GroupMutationKind.Disband,
            leaderGuid,
            expectedMembers,
            out string refusal);
        if (pending == null)
            return GroupResult(   // cb:fold refusal is probed by pending factory
                GroupMutationStatus.NotSent,
                refusal,
                GroupMutationKind.Disband,
                leaderGuid,
                expectedMembers);

        GroupMutationResult outcome = await SendGroupMutationAsync(
            pending,
            new BridgeCommand("DISBAND_GROUP", new
            {
                leader_guid = leaderGuid,
                member_guids = expectedMembers
            }));
        if (!outcome.Succeeded)
            return outcome with { Group = group };   // cb:fold terminal outcome probed by send helper

        BotGroup? current = _groupManager.GetAllGroups().FirstOrDefault(candidate => candidate.GroupId == groupId);
        bool topologyStillOwned = current != null
            && current.LeaderGuid == leaderGuid
            && current.MemberGuids.OrderBy(guid => guid).SequenceEqual(expectedMembers);
        if (!topologyStillOwned || !_groupManager.DisbandGroup(groupId))
        {
            CircuitTrace.Hit(leaderGuid, "host: GROUP_DISBANDED ACK could not commit local topology", pending.CorrelationId);
            _logger.LogError(
                "BotBrain: core ACKed DISBAND_GROUP cbt={Cbt}, but local topology changed before commit; reconciliation required",
                pending.CorrelationId);
            return GroupResult(
                GroupMutationStatus.OutcomeUnknown,
                "local_topology_changed_after_ack",
                pending.Kind,
                pending.LeaderGuid,
                pending.MemberGuids,
                pending.CorrelationId,
                group);
        }

        EnrichGroupMembers(expectedMembers);
        await _groupManager.SaveGroupsToDbAsync();
        CircuitTrace.Hit(leaderGuid, "host: GROUP_DISBANDED exact ACK committed", pending.CorrelationId);
        return outcome with { Group = group };
    }

    /// <summary>Dissolve every group, reporting each exact transactional result.</summary>
    public async Task<GroupMutationBatchResult> DisbandAllGroupsAsync()
    {
        await _groupMutationGate.WaitAsync();
        try
        {
            return await DisbandAllGroupsCoreAsync();
        }
        finally
        {
            _groupMutationGate.Release();
        }
    }

    private async Task<GroupMutationBatchResult> DisbandAllGroupsCoreAsync()
    {
        var results = new List<GroupMutationResult>();
        foreach (BotGroup group in _groupManager.GetAllGroups().OrderBy(group => group.GroupId).ToList())
            results.Add(await DisbandGroupCoreAsync(group.GroupId));
        return new GroupMutationBatchResult { Results = results };
    }

    /// <summary>
    /// Plan auto-groups without mutating the manager, then transactionally form
    /// each candidate. Failed/unknown candidates remain uncommitted and visible
    /// in the per-candidate result list.
    /// </summary>
    public async Task<GroupMutationBatchResult> AutoFormGroupsAsync()
    {
        await _groupMutationGate.WaitAsync();
        try
        {
            var candidates = _groupManager.PlanAutoGroups(AllBots,
                guid => _tracker.GetAllPositions().FirstOrDefault(position => position.Guid == guid));
            var results = new List<GroupMutationResult>(candidates.Count);
            foreach (BotGroup candidate in candidates)
            {
                int[] followers = candidate.MemberGuids
                    .Where(guid => guid != candidate.LeaderGuid)
                    .ToArray();
                results.Add(await FormGroupCoreAsync(candidate.LeaderGuid, followers));
            }
            return new GroupMutationBatchResult { Results = results };
        }
        finally
        {
            _groupMutationGate.Release();
        }
    }

    private PendingGroupMutation? CreatePendingGroupMutation(
        GroupMutationKind kind,
        int leaderGuid,
        int[] expectedMembers,
        out string refusal)
    {
        refusal = "leader_not_connected";
        if (!_bridge.Connections.TryGetValue(leaderGuid, out BotConnection? connection)
            || connection.SessionId <= 0)
        {
            CircuitTrace.Hit(leaderGuid, "host: group mutation not sent, leader disconnected");
            return null;
        }
        if (connection.BridgeProtocol < BotBridgeService.RequiredTransactionalGroupProtocol)
        {
            refusal = "transactional_group_protocol_required";
            CircuitTrace.Hit(
                leaderGuid,
                "host: group mutation not sent, protocol lacks exact topology ACK",
                connection.BridgeProtocol);
            _logger.LogWarning(
                "BotBrain: refusing group mutation for leader {Guid}; core bridgeProtocol={Actual}, required={Required}",
                leaderGuid,
                connection.BridgeProtocol,
                BotBridgeService.RequiredTransactionalGroupProtocol);
            return null;
        }

        return new PendingGroupMutation
        {
            Kind = kind,
            LeaderGuid = leaderGuid,
            SessionId = connection.SessionId,
            CorrelationId = BridgeCorrelation.NextId(),
            MemberGuids = expectedMembers.OrderBy(guid => guid).ToArray()
        };
    }

    private async Task<GroupMutationResult> SendGroupMutationAsync(
        PendingGroupMutation pending,
        BridgeCommand command)
    {
        if (!_pendingGroupMutations.TryAdd(pending.CorrelationId, pending))
        {
            CircuitTrace.Hit(pending.LeaderGuid, "host: duplicate group correlation id", pending.CorrelationId);
            throw new InvalidOperationException($"Duplicate bridge correlation id {pending.CorrelationId}.");
        }

        CorrelatedSendStatus sendStatus = CorrelatedSendStatus.DefinitelyNotSent;
        try
        {
            sendStatus = await _bridge.TrySendCorrelatedAsync(
                pending.LeaderGuid,
                command.Type,
                command.Payload,
                pending.CorrelationId,
                pending.SessionId);
            if (sendStatus is CorrelatedSendStatus.DefinitelyNotSent or CorrelatedSendStatus.SessionSuperseded)
            {
                CircuitTrace.Hit(pending.LeaderGuid, "host: group mutation definitely not sent", pending.CorrelationId);
                return GroupResult(
                    GroupMutationStatus.NotSent,
                    sendStatus == CorrelatedSendStatus.SessionSuperseded
                        ? "session_superseded_before_send"
                        : "definitely_not_sent",
                    pending.Kind,
                    pending.LeaderGuid,
                    pending.MemberGuids,
                    pending.CorrelationId);
            }

            GroupBridgeOutcome bridgeOutcome;
            try
            {
                bridgeOutcome = await pending.Completion.Task.WaitAsync(GroupMutationAckDeadline);
            }
            catch (TimeoutException)
            {
                CircuitTrace.Hit(pending.LeaderGuid, "host: group mutation outcome unknown at deadline", pending.CorrelationId);
                _logger.LogWarning(
                    "BotBrain: {Command} cbt={Cbt} has no exact terminal outcome; not retrying and not changing C# topology",
                    command.Type, pending.CorrelationId);
                return GroupResult(
                    GroupMutationStatus.OutcomeUnknown,
                    sendStatus == CorrelatedSendStatus.OutcomeUnknown
                        ? "send_and_outcome_unknown"
                        : "ack_timeout",
                    pending.Kind,
                    pending.LeaderGuid,
                    pending.MemberGuids,
                    pending.CorrelationId);
            }

            return bridgeOutcome.Disposition switch
            {
                GroupBridgeOutcomeDisposition.Accepted => GroupResult(   // cb:fold pure result projection; accepted terminal was probed by resolver
                    GroupMutationStatus.Success, bridgeOutcome.Detail,
                    pending.Kind, pending.LeaderGuid, pending.MemberGuids, pending.CorrelationId),
                GroupBridgeOutcomeDisposition.Rejected => GroupResult(   // cb:fold pure result projection; rejected terminal was probed by resolver
                    GroupMutationStatus.Rejected, bridgeOutcome.Detail,
                    pending.Kind, pending.LeaderGuid, pending.MemberGuids, pending.CorrelationId),
                _ => GroupResult(   // cb:fold pure result projection; protocol mismatch was probed by resolver
                    GroupMutationStatus.OutcomeUnknown, bridgeOutcome.Detail,
                    pending.Kind, pending.LeaderGuid, pending.MemberGuids, pending.CorrelationId)
            };
        }
        finally
        {
            _pendingGroupMutations.TryRemove(pending.CorrelationId, out _);
        }
    }

    private async Task PersistGroupingModeAsync(GroupingMode mode)
    {
        try
        {
            using var conn = _db.Admin();
            await conn.ExecuteAsync(@"
                INSERT INTO bot_settings (setting_key, setting_value)
                VALUES ('grouping_mode', @Value)
                ON DUPLICATE KEY UPDATE setting_value = @Value",
                new { Value = ((int)mode).ToString() });
            await _groupManager.SaveGroupsToDbAsync();
        }
        catch (Exception ex)
        {
            CircuitTrace.Hit(0, "host: grouping mode persist failed");
            _logger.LogError(ex, "BotBrain: failed to persist grouping mode");
        }
    }

    private void EnrichGroupMembers(IEnumerable<int> memberGuids)
    {
        foreach (int guid in memberGuids)
            if (_bots.TryGetValue(guid, out BotIdentity? bot))
                _groupManager.EnrichBotIdentity(bot);   // cb:fold identity projection after authoritative commit
    }

    private static GroupMutationResult GroupResult(
        GroupMutationStatus status,
        string detail,
        GroupMutationKind operation,
        int leaderGuid,
        IEnumerable<int> memberGuids,
        long correlationId = 0,
        BotGroup? group = null)
        => new()
        {
            Status = status,
            Detail = detail,
            Operation = operation,
            LeaderGuid = leaderGuid,
            MemberGuids = memberGuids.OrderBy(guid => guid).ToArray(),
            CorrelationId = correlationId,
            Group = group
        };

    // -------------------- Bridge event entry (unchanged contract) --------------------

    /// <summary>
    /// Routes a bridge EVENT/CHAT_RECV into the spine. Thin in the rebuild: keep the
    /// dashboard's BotIdentity level fresh, then hand the event to BotBrain → BotExecutor
    /// for WAIT/ack matching. The old grouping fan-out + economy loot routing are shed
    /// (grouping → Phase 5, economy → Phase 4).
    /// </summary>
    public async Task<bool> HandleBridgeEventAsync(int guid, BotEvent evt)
    {
        // Group topology has its own dashboard transaction owner rather than a
        // planner Outstanding. Resolve it before requiring a BotContext: an ACK
        // must still settle its waiter while driving is disabled or the roster is
        // between HELLO hydration passes.
        if (GroupBridgeOutcomeMatcher.IsGroupTerminal(evt.EventType))
            return TryResolveGroupMutationOutcome(guid, evt);   // cb:fold resolver probes admitted/rejected ownership outcome

        if (!_contexts.TryGetValue(guid, out var ctx))
        {
            CircuitTrace.Hit(guid, "host: event has no spine context");
            return false;
        }

        await ctx.MutationGate.WaitAsync();
        try
        {
            // Session admission belongs inside the single-writer gate. An event
            // can pass BotBridgeService's entry check, await a SignalR broadcast,
            // and then queue here while a replacement HELLO takes ownership.
            // Reject it before it can clear/negate Pending or mutate progress.
            if (!_bridge.IsActiveSession(guid, evt.BridgeSessionId))
            {
                CircuitTrace.Hit(guid, "host: event from superseded bridge session rejected");
                return false;
            }

            if (evt.EventType == "LEVEL_UP" && evt.NewLevel > 0 && _bots.TryGetValue(guid, out var bot))
            {
                CircuitTrace.Hit(guid, "host: level-up reflags training", evt.NewLevel);
                bot.Level = evt.NewLevel;
                // A level-up unlocks new class spells → flag the bot to visit a trainer (gold-gated in
                // GoalSelector). Clear any training cooldown so a fresh level justifies a retry even if a
                // recent trainer trip gave up. TrainingPlanner buys what the bot can afford and re-clears
                // the flag; what it can't afford waits for the next level-up's gold.
                bot.HasUnlearnedSpells = true;
                bot.TrainCooldownUntil = null;
            }

            CircuitTrace.Hit(guid, "host: event handed to spine driver");
            bool handled = _driver.OnEvent(ctx, evt);
            if (handled)   // cb:fold accepted-position helper internally whitelists positive arrival events
                _bridge.ApplyAcceptedOutcomePosition(guid, evt);   // cb:fold exact outcome handling is probed in executor/bridge
            return handled;
        }
        finally
        {
            ctx.MutationGate.Release();
        }
    }

    private bool TryResolveGroupMutationOutcome(int guid, BotEvent evt)
    {
        // Close the replacement race after BotBridgeService's pre-route check:
        // a HELLO can publish a new socket in the few instructions before this
        // resolver runs. Matching the pending session is necessary but the old
        // session must still actively own the guid at admission time as well.
        if (!_bridge.IsActiveSession(guid, evt.BridgeSessionId))
        {
            CircuitTrace.Hit(guid, "host: group terminal from superseded session ignored", evt.CorrelationId ?? 0);
            return false;
        }

        if (evt.CorrelationId is not long cbt
            || !_pendingGroupMutations.TryGetValue(cbt, out PendingGroupMutation? pending))
        {
            CircuitTrace.Hit(guid, "host: late or unowned group terminal ignored", evt.CorrelationId ?? 0);
            return false;
        }

        GroupBridgeOutcome outcome = GroupBridgeOutcomeMatcher.Classify(pending, guid, evt);
        if (outcome.Disposition == GroupBridgeOutcomeDisposition.Ignore)
        {
            CircuitTrace.Hit(guid, "host: group terminal owner mismatch ignored", cbt);
            return false;
        }

        if (outcome.Disposition == GroupBridgeOutcomeDisposition.ProtocolMismatch)
        {
            CircuitTrace.Hit(guid, "host: correlated group ACK topology mismatch", cbt);
            _logger.LogError(
                "BotBrain: rejecting correlated group ACK cbt={Cbt}: {Detail}; C# topology remains unchanged",
                cbt, outcome.Detail);
        }
        else
        {
            CircuitTrace.HitNote(guid, "host: correlated group terminal admitted", outcome.Disposition.ToString());
        }

        return pending.Completion.TrySetResult(outcome);
    }

    // ==================== Lifecycle ====================

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("BotBrain: host started (Strategy B spine; driving disabled by default — enable from dashboard)");

        // Tables the retained surface needs (bot_personality, bot_settings, …).
        await _dbInit.InitializeAsync();

        // Self-heal the core characters.playerbot schema. Stock VMaNGOS ships the
        // base table (char_guid, chance, ai) but NOT the identity columns the fork's
        // PlayerBotMgr::Load() and our auto-register below both read/write. On a fresh
        // clone of the fork these are absent and every bot load fails with a 1054
        // "unknown column" — so add them here, guarded, once. No-op after the first run.
        await EnsurePlayerbotColumnsAsync();

        // Personality quirk tables (load/roll on connect).
        _quirkLoader.Load();

        // Circuit-board trace toggle state (mode + armed guids) from bot_settings.
        // Bridge attach lets Arm/Disarm/Mode forward CIRCUIT_TRACE to the C++ side (R6).
        _circuit.AttachBridge(_bridge.SendToBotAsync, _bridge.SendToAllBotsAsync);
        await _circuit.LoadSettingsAsync();

        // Groups + grouping mode from DB.
        await _groupManager.LoadGroupsFromDbAsync();
        await LoadGroupingModeAsync();

        // Wire event routing from bridge → brain (breaks the circular DI).
        _bridge.SetBrainService(this);

        while (!stoppingToken.IsCancellationRequested)
        {
            long iterationStart = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                // 1. Mirror the bridge roster into BotIdentity + BotContext.
                await BrainLoopMetrics.TimeAsync(_loopMetrics.RecordRosterSync, SyncBotRosterAsync);

                // 2. Drive the spine (sense always; drive+supervise when enabled).
                await BrainLoopMetrics.TimeAsync(_loopMetrics.RecordBrainTicks, RunBrainTicksAsync);

                // 2b. Circuit-board pump: flush armed bots' sealed trace segments +
                //     any wedge auto-dump requests to the daily JSONL.
                _circuit.Tick();

                // 3. Print the fleet picture on an interval.
                if (_contexts.Count > 0 &&
                    (DateTime.UtcNow - _lastFleetLog).TotalSeconds >= FLEET_LOG_INTERVAL_SEC)
                {   // cb:fold fleet-report logging cadence

                    _lastFleetLog = DateTime.UtcNow;

                    // IsEnabled first: the report argument is evaluated before the
                    // call, so without this guard the whole string was built every
                    // 30 s even when Info was filtered out. Summary only — the row
                    // table is on demand via GetFleetReport().
                    if (_logger.IsEnabled(LogLevel.Information))
                    {
                        long reportStart = System.Diagnostics.Stopwatch.GetTimestamp();
                        string summary = GetFleetSummary();
                        _loopMetrics.RecordFleetReport(
                            System.Diagnostics.Stopwatch.GetElapsedTime(reportStart));
                        _logger.LogInformation("BotBrain fleet:\n{Report}", summary);
                    }
                }

                _loopMetrics.RecordTrackedContexts(_contexts.Count);
            }
            catch (Exception ex)
            {
                CircuitTrace.Hit(0, "host: main loop error");
                _logger.LogError(ex, "BotBrain: main loop error");
            }
            finally
            {
                _loopMetrics.RecordLoopIteration(
                    System.Diagnostics.Stopwatch.GetElapsedTime(iterationStart));
            }

            await Task.Delay(250, stoppingToken);
        }
    }

    // ==================== Roster sync ====================

    /// <summary>
    /// Detect new bot connections from the bridge and initialize BotIdentity +
    /// BotContext; detect disconnects and evict after a grace period.
    /// </summary>
    private async Task SyncBotRosterAsync()
    {
        var bridgeStates = _bridge.BotStates;

        foreach (var kvp in bridgeStates)
        {
            int guid = kvp.Key;
            var bs = kvp.Value;

            if (bs.TaskState == "DISCONNECTED") continue;   // cb:fold iteration filter

            bool alreadyInit;
            lock (_initializedGuidsLock) { alreadyInit = _initializedGuids.Contains(guid); }

            if (!alreadyInit)
            {
                CircuitTrace.Hit(guid, "host: new bot on bridge, initializing");
                await InitializeBotAsync(guid, bs);
                lock (_initializedGuidsLock) { _initializedGuids.Add(guid); }
            }
            else if (_bots.TryGetValue(guid, out var bot))
            {   // cb:fold mirror refresh, no decision
                // Position/level are STATE fields copied under SensoryFeedGate.
                // Read one coherent snapshot instead of racing the mutable UI
                // projection while HandleStateAsync is copying the next beat.
                BotStateSnapshot? snapshot = _bridge.GetBotStateSnapshot(guid);
                DateTime trackerNow = DateTime.UtcNow;
                if (snapshot != null
                    && snapshot.StateUtc != DateTime.MinValue
                    && trackerNow - snapshot.StateUtc < BotBridgeService.SensoryFeedStaleAfter)
                {
                    CircuitTrace.Hit(guid, "host: tracker refreshed from coherent STATE snapshot");
                    bot.Level = snapshot.Level;
                    _tracker.UpdatePosition(guid, snapshot.ZoneId, snapshot.MapId, snapshot.X, snapshot.Y, snapshot.Z);   // cb:fold tracker mirror only; planning gate is separate
                }
            }
        }

        // Evict bots that have been gone for the grace window.
        List<int> disconnected;
        lock (_initializedGuidsLock)
        {
            disconnected = _initializedGuids
                .Where(g => !bridgeStates.ContainsKey(g) || bridgeStates[g].TaskState == "DISCONNECTED")
                .ToList();
        }

        foreach (var guid in disconnected)
        {
            _disconnectedAt.TryAdd(guid, DateTime.UtcNow);

            if (_disconnectedAt.TryGetValue(guid, out var dcTime) &&
                (DateTime.UtcNow - dcTime).TotalSeconds >= EVICT_DISCONNECT_SEC)
            {
                CircuitTrace.Hit(guid, "host: stale bot evicted");
                _groupManager.RemoveFromGroup(guid);
                _bots.TryRemove(guid, out _);
                _contexts.TryRemove(guid, out _);
                lock (_initializedGuidsLock) { _initializedGuids.Remove(guid); }
                _disconnectedAt.TryRemove(guid, out _);
                _tracker.Remove(guid);
                _fallRecorder.Forget(guid);   // drop the bot's fall-ring so evicted guids don't leak memory
                CircuitTrace.Forget(guid);    // drop the bot's circuit ring likewise
                _logger.LogInformation("BotBrain: evicted stale bot {Guid} (disconnected {Sec}s+)", guid, (int)EVICT_DISCONNECT_SEC);
            }
        }

        // Clear the disconnect timer for any bot that reconnected.
        var reconnected = _disconnectedAt.Keys
            .Where(g => bridgeStates.ContainsKey(g) && bridgeStates[g].TaskState != "DISCONNECTED")
            .ToList();
        foreach (var guid in reconnected)
            _disconnectedAt.TryRemove(guid, out _);
    }

    // ==================== Spine driver ====================

    /// <summary>
    /// One pass over the fleet. Every bot with a real STATE is sensed (so FleetReport
    /// stays live even when driving is off); when driving is enabled, BotBrain runs the
    /// full tick — Sense → hold Idle (Phase 1) → Supervisor deadline check.
    /// </summary>
    private async Task RunBrainTicksAsync()
    {
        DateTime now = DateTime.UtcNow;

        // Build the exact sensory-safe roster once for both decision layers. A
        // stale member is absent from the god-bot pre-pass as well as its own
        // BotBrain tick, so group decisions cannot consume the same frozen
        // health/combat/position that the per-bot guard rejects.
        var freshContexts = new Dictionary<int, BotContext>(_contexts.Count);
        var freshSnapshots = new Dictionary<int, BotStateSnapshot>(_contexts.Count);
        foreach (var kvp in _contexts)
        {
            await kvp.Value.MutationGate.WaitAsync();
            try
            {
                BotState? bs = _bridge.GetBotState(kvp.Key);
                if (bs == null || bs.TaskState == "DISCONNECTED")
                    continue;   // cb:fold iteration filter

                if (!BotBridgeService.HasFreshSensoryState(bs, now))   // cb:fold stale/hydrating outcomes probed in the called hold or explicit trace below
                {   // cb:fold stale/hydrating outcomes probed in called hold or explicit trace
                    if (BotBridgeService.IsSensoryFeedStale(bs, now))   // cb:fold stale outcome probed inside HoldForStaleSensoryFeed
                        HoldForStaleSensoryFeed(kvp.Value, bs, now);   // cb:fold stale outcome probed inside helper
                    else
                        CircuitTrace.Hit(kvp.Key, "host: awaiting first STATE, not ticked");
                    continue;
                }

                BotStateSnapshot? snapshot = _bridge.GetBotStateSnapshot(kvp.Key);
                if (snapshot == null)
                    continue;   // cb:fold connection replacement boundary; bridge probes the session change
                if (snapshot.StateUtc == DateTime.MinValue
                    || (now > snapshot.StateUtc
                        && now - snapshot.StateUtc >= BotBridgeService.SensoryFeedStaleAfter))
                {
                    CircuitTrace.Hit(kvp.Key, "host: captured snapshot is not fresh, excluded from group roster");
                    continue;
                }

                bool feedRecovered = kvp.Value.SensoryFeedStale;

                // GroupCoordinator runs before BotBrain.TickAsync, so refresh the
                // context here as well. Otherwise the first pre-pass after recovery
                // would consume the very frozen snapshot this hold is meant to fence.
                kvp.Value.Sense(snapshot);
                if (feedRecovered)
                {   // cb:fold incompatible outcome probed inside hold helper
                    CircuitTrace.Hit(kvp.Key, "host: fresh STATE releases sensory hold");
                    _logger.LogInformation(
                        "BotBrain: sensory feed fresh for {Name} (guid={Guid}); planning may resume",
                        kvp.Value.Name, kvp.Key);
                }

                if (snapshot.BridgeProtocol < BotBridgeService.RequiredCorrelatedOutcomeProtocol)   // cb:fold incompatible outcome probed inside hold helper
                {   // cb:fold boundary recheck outcome probed inside hold helper
                    HoldForIncompatibleBridgeProtocol(kvp.Value, snapshot.BridgeProtocol);
                    continue;
                }
                if (kvp.Value.BridgeProtocolIncompatible)
                {
                    kvp.Value.BridgeProtocolIncompatible = false;
                    CircuitTrace.Hit(kvp.Key, "host: correlated bridge protocol restored", snapshot.BridgeProtocol);
                    _logger.LogInformation(
                        "BotBrain: correlated bridge protocol restored for {Name} (guid={Guid}, protocol={Protocol})",
                        kvp.Value.Name, kvp.Key, snapshot.BridgeProtocol);
                }
                freshContexts[kvp.Key] = kvp.Value;
                freshSnapshots[kvp.Key] = snapshot;
            }
            finally
            {
                kvp.Value.MutationGate.Release();
            }
        }

        // Grouping pre-pass (§3.2): the "god bot" stamps each grouped member's BotContext BEFORE the
        // per-bot ticks, so each tick consults a fresh stamp. Two seams: the combat directive
        // (Assist(anchor)) and the EXECUTION directive -- the union-chosen shared objective the team
        // grinds together, gated on all eligible holders finishing (needs the quest graph). Pure
        // decision+stamp -- it issues NO commands; the spine emits COMBAT_DIRECTIVE on change (BotBrain
        // step 1a) and the QuestPlanner consults the exec stamp. Only when driving; sensing-only skips it.
        if (_brainEnabled)
        {   // cb:fold mutation-serialization shell; coordinator probes all behavioral outcomes
            var lockedContexts = new List<BotContext>(freshContexts.Count);
            var revalidatedContexts = new Dictionary<int, BotContext>(freshContexts.Count);
            var revalidatedSnapshots = new Dictionary<int, BotStateSnapshot>(freshContexts.Count);
            try
            {
                // Stable lock order makes the multi-bot pre-pass one serialized
                // mutation relative to each bot's socket EVENT stream.
                foreach (BotContext ctx in freshContexts.Values.OrderBy(c => c.Guid))
                {   // cb:fold mutation serialization; coordinator probes behavior
                    await ctx.MutationGate.WaitAsync();
                    lockedContexts.Add(ctx);
                }

                // Lock acquisition can wait behind socket EVENT work. Recapture
                // every member only after all context gates are held; a cached
                // pre-lock snapshot must never drive group orders after HELLO
                // replaced that member or its STATE crossed the stale boundary.
                DateTime groupNow = DateTime.UtcNow;
                foreach (var kvp in freshContexts)
                {
                    BotStateSnapshot? current = _bridge.GetBotStateSnapshot(kvp.Key);
                    if (current == null)
                    {
                        CircuitTrace.Hit(kvp.Key, "host: group member session changed while locks acquired, excluded");
                        continue;
                    }
                    if (current.StateUtc == DateTime.MinValue
                        || groupNow - current.StateUtc >= BotBridgeService.SensoryFeedStaleAfter)
                    {
                        CircuitTrace.Hit(kvp.Key, "host: group member stale at decision boundary, excluded");
                        if (_bridge.GetBotState(kvp.Key) is { } staleState)
                            HoldForStaleSensoryFeed(kvp.Value, staleState, groupNow);   // cb:fold state projection availability; exclusion probe carries decision
                        continue;
                    }
                    if (current.BridgeProtocol < BotBridgeService.RequiredCorrelatedOutcomeProtocol)
                    {   // cb:fold incompatible outcome probed inside hold helper
                        HoldForIncompatibleBridgeProtocol(kvp.Value, current.BridgeProtocol);
                        continue;
                    }

                    kvp.Value.Sense(current);
                    revalidatedContexts[kvp.Key] = kvp.Value;
                    revalidatedSnapshots[kvp.Key] = current;
                }

                // Final generation/age check gives the coordinator a roster that
                // was simultaneously admissible at its decision boundary. A
                // later replacement still cannot receive an old decision because
                // executor sends are bound to the sensed session id.
                DateTime decisionNow = DateTime.UtcNow;
                foreach (int guid in revalidatedSnapshots
                    .Where(kvp => !_bridge.IsActiveSession(kvp.Key, kvp.Value.BridgeSessionId)
                        || decisionNow - kvp.Value.StateUtc >= BotBridgeService.SensoryFeedStaleAfter)
                    .Select(kvp => kvp.Key)
                    .ToList())
                {
                    CircuitTrace.Hit(guid, "host: group member invalidated before coordinator, excluded");
                    revalidatedSnapshots.Remove(guid);
                    revalidatedContexts.Remove(guid);
                }

                var groupContexts = revalidatedContexts
                    .Where(kvp => !kvp.Value.HasUnreleasedControlFence)
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                GroupCoordinator.Update(groupContexts, _groupManager, _quests, _safety, _spawns, _driver.QuestPlanner, _zoneData);   // cb:fold god-bot pre-pass, probed inside GroupCoordinator
            }
            finally
            {
                for (int i = lockedContexts.Count - 1; i >= 0; --i)
                    lockedContexts[i].MutationGate.Release();   // cb:fold mutation serialization only
            }

            freshContexts = revalidatedContexts;
            freshSnapshots = revalidatedSnapshots;
        }

        foreach (var kvp in freshContexts)
        {
            await kvp.Value.MutationGate.WaitAsync();
            try
            {
                var bs = _bridge.GetBotState(kvp.Key);
                if (bs == null || bs.TaskState == "DISCONNECTED") continue;   // cb:fold iteration filter

                BotStateSnapshot? currentSnapshot = _bridge.GetBotStateSnapshot(kvp.Key);
                if (currentSnapshot == null)
                {
                    CircuitTrace.Hit(kvp.Key, "host: tick snapshot unavailable after session replacement, skipped");
                    continue;
                }
                BotStateSnapshot snap = currentSnapshot;

                // A group decision is tied to the exact session generation it
                // sensed. If HELLO replaced this member after the pre-pass, skip
                // its tick and recompute the fleet next pass from hydrated STATE.
                if (_brainEnabled
                    && freshSnapshots.TryGetValue(kvp.Key, out BotStateSnapshot? admitted)
                    && snap.BridgeSessionId != admitted.BridgeSessionId)
                {
                    CircuitTrace.Hit(kvp.Key, "host: tick session differs from group decision, skipped");
                    continue;
                }

                // Close the small boundary between the fleet classification and this
                // bot's turn. If the snapshot crossed the age limit during the group
                // pre-pass, fail closed here instead of granting one stale tick.
                DateTime tickNow = DateTime.UtcNow;
                if (snap.StateUtc == DateTime.MinValue || tickNow - snap.StateUtc >= BotBridgeService.SensoryFeedStaleAfter)   // cb:fold boundary recheck outcome probed inside hold helper
                {   // cb:fold stale boundary body is covered by HoldForStaleSensoryFeed probes
                    HoldForStaleSensoryFeed(kvp.Value, bs, tickNow);
                    continue;
                }
                if (snap.BridgeProtocol < BotBridgeService.RequiredCorrelatedOutcomeProtocol)
                {   // cb:fold incompatible outcome probed inside hold helper
                    HoldForIncompatibleBridgeProtocol(kvp.Value, snap.BridgeProtocol);
                    continue;
                }

                // Circuit-board tick bracket (R10): every probe between Begin and End lands in
                // one per-tick segment; EndTick stamps the bot's world position for the map view.
                CircuitTrace.BeginTick(kvp.Key);
                try
                {
                    if (_brainEnabled)
                        await _driver.TickAsync(kvp.Value, snap);   // cb:fold spine tick, probed throughout BotBrain
                    else
                    {
                        CircuitTrace.Hit(kvp.Key, "host: sense only (driving disabled)");
                        kvp.Value.Sense(snap);                        // disabled: still sense so FleetReport stays live
                    }

                    // Always-on void/fall black box: ctx.Pos is fresh (post-Sense) either way. Cheap; flushes only on a fall.
                    _fallRecorder.Observe(kvp.Value);
                }
                finally
                {
                    CircuitTrace.EndTick(
                        kvp.Key,
                        snap.MapId,
                        snap.ZoneId,
                        snap.X,
                        snap.Y,
                        snap.Z,
                        kvp.Value);
                }
            }
            finally
            {
                kvp.Value.MutationGate.Release();
            }
        }
    }

    private void HoldForStaleSensoryFeed(BotContext ctx, BotState bs, DateTime now)
    {
        TimeSpan age = BotBridgeService.GetSensoryFeedAge(bs, now);

        // These are transient intent stamps. Clear them while blind so neither a
        // stale group order nor a stale combat directive can leak through if a
        // caller inspects the held context outside the normal driver loop.
        ctx.CombatDirective = CombatDirective.None;
        ctx.GroupOrder = GroupOrder.None;

        if (ctx.SensoryFeedStale)   // cb:fold duplicate-log suppression; initial hold is probed below
            return;   // cb:fold duplicate stale-log suppression; initial hold is probed below

        ctx.SensoryFeedStale = true;
        _tracker.Remove(ctx.Guid);
        CircuitTrace.Hit(ctx.Guid, "host: stale sensory feed, planning held", age.TotalSeconds);
        _logger.LogWarning(
            "BotBrain: holding state-dependent planning for {Name} (guid={Guid}); last STATE is {Age:F1}s old",
            ctx.Name, ctx.Guid, age.TotalSeconds);
    }

    private void HoldForIncompatibleBridgeProtocol(BotContext ctx, int bridgeProtocol)
    {
        ctx.CombatDirective = CombatDirective.None;
        ctx.GroupOrder = GroupOrder.None;
        ctx.Pending = null;
        ctx.Failure = null;

        if (ctx.BridgeProtocolIncompatible)   // cb:fold duplicate-log suppression; initial hold is probed below
            return;   // cb:fold duplicate protocol-log suppression; initial hold is probed below

        ctx.BridgeProtocolIncompatible = true;
        CircuitTrace.Hit(ctx.Guid, "host: bridge protocol lacks correlated outcomes", bridgeProtocol);
        _logger.LogError(
            "BotBrain: holding planner for {Name} (guid={Guid}); core bridgeProtocol={Actual}, required={Required}",
            ctx.Name, ctx.Guid, bridgeProtocol, BotBridgeService.RequiredCorrelatedOutcomeProtocol);
    }

    // ==================== Bot init ====================

    /// <summary>
    /// Initialize a new bot: load or roll personality, build the BotIdentity (dashboard +
    /// grouping), seed the BotContext (the spine's live state), enrich group membership,
    /// and auto-register in characters.playerbot for restart persistence.
    /// </summary>
    private async Task InitializeBotAsync(int guid, BotState bs)
    {
        // Try to load persisted personality from the admin DB.
        BotPersonality? personality = null;
        try
        {
            using var conn = _db.Admin();
            var row = await conn.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT * FROM bot_personality WHERE bot_guid = @Guid", new { Guid = guid });

            if (row != null)
            {
                CircuitTrace.Hit(guid, "host: persisted personality loaded");
                personality = new BotPersonality
                {
                    Patience = (float)row.patience,
                    Greed = (float)row.greed,
                    Curiosity = (float)row.curiosity,
                    Sociability = (float)row.sociability,
                    Aggression = (float)row.aggression,
                    Efficiency = (float)row.efficiency,
                    Cautiousness = (float)row.cautiousness,
                    Indecisiveness = (float)row.indecisiveness,
                    Spontaneity = (float)row.spontaneity,
                    ChatStyle = (string)row.chat_style,
                    Temperament = (string)row.temperament,
                    Quirks = _quirkLoader.ResolveQuirkIds((string?)row.quirk_ids)
                };
                _logger.LogInformation("BotBrain: loaded persisted personality for {Name} (guid={Guid})", bs.Name, guid);
            }
        }
        catch (Exception ex)
        {
            CircuitTrace.Hit(guid, "host: personality load failed, will roll new");
            _logger.LogWarning(ex, "BotBrain: failed to load personality for bot {Guid} — will roll new", guid);
        }

        // Roll a new personality if none persisted.
        if (personality == null)
        {
            CircuitTrace.Hit(guid, "host: new personality rolled");
            personality = PersonalityRoller.Roll(_quirkLoader.AllQuirks.ToList());
            await PersistPersonalityAsync(guid, personality);
            _logger.LogInformation("BotBrain: rolled new personality for {Name} (guid={Guid})", bs.Name, guid);
        }

        var bot = new BotIdentity
        {
            Guid = guid,
            Name = bs.Name,
            Race = bs.Race,
            ClassId = bs.ClassId,
            Faction = BotIdentity.FactionForRace(bs.Race),
            Level = bs.Level,
            Personality = personality,
            CurrentActivity = new ActivityState { Type = ActivityType.Idle, StartedAt = DateTime.UtcNow },
            NextDecisionTick = DateTime.UtcNow.AddSeconds(2),
            // Train-up-on-connect catch-up for the post-rebuild fleet (mid-level bots that never ran the
            // trainer pass are spell-starved) — but NOT for a fresh L1. An L1's trainables are trivial (most
            // starting spells are auto-granted), and flagging it makes every fresh spawn bee-line the trainer
            // before it ever quests: the GoalSelector training trigger outranks questing, so the whole fleet
            // routes to the (crowded / interior-pocket) trainer, wedges, and never leaves L1. An L1 quests
            // first and is re-flagged at its first LEVEL_UP (which catches the real L2+ ranks). Higher-level
            // connects still catch up immediately; the trainer-wedge give-up in BotBrain.TryBreakWedgeAsync
            // is the backstop if any trainer trip can't complete. Raise the floor if you ever want L1s to
            // train pre-quest.
            HasUnlearnedSpells = bs.Level > 1
        };

        // Hydrate the durable completed-quest set from the character DB so a restart does NOT
        // re-offer already-rewarded quests (which inflates `av`, re-seeds finished content, and
        // makes chain prereqs read as un-done so follow-ups never unlock). rewarded=1 is the
        // TURN-IN flag -- deliberately NOT status==1 (COMPLETE-but-not-handed-in), so a quest
        // still waiting at its ender stays resumable. character_queststatus is in the CHARACTERS
        // db; guid is the character lowguid == the bot guid.
        try
        {
            using var qsConn = _db.Characters();
            var rewarded = await qsConn.QueryAsync<int>(
                "SELECT quest FROM character_queststatus WHERE guid = @Guid AND rewarded = 1",
                new { Guid = guid });
            foreach (var qid in rewarded)
                bot.CompletedQuestIds.Add(qid);
            CircuitTrace.Hit(guid, "host: completed quests hydrated", bot.CompletedQuestIds.Count);
            _logger.LogInformation("BotBrain: hydrated {Count} completed quests for {Name} (guid={Guid})",
                bot.CompletedQuestIds.Count, bs.Name, guid);
        }
        catch (Exception ex)
        {
            CircuitTrace.Hit(guid, "host: completed-quest hydration failed");
            _logger.LogWarning(ex, "BotBrain: failed to hydrate completed quests for bot {Guid}", guid);
        }

        _bots[guid] = bot;

        // Seed the spine's live state. Sensory fills in on the first STATE tick.
        _contexts[guid] = new BotContext
        {
            Guid = guid,
            Name = bs.Name,
            Level = bs.Level,
            Identity = bot          // P3: durable roster back-ref for the quest planner
        };

        // HELLO position is only a hydration placeholder. Do not publish it into
        // the live position tracker unless a real, still-fresh STATE already won
        // the race with roster initialization.
        if (BotBridgeService.HasFreshSensoryState(bs, DateTime.UtcNow))   // cb:fold initialization mirror only; tick gate owns planning decision
            _tracker.UpdatePosition(guid, bs.ZoneId, bs.MapId, bs.X, bs.Y, bs.Z);   // cb:fold initialization mirror only; tick gate owns planning

        // Stamp group membership (from DB-loaded groups).
        _groupManager.EnrichBotIdentity(bot);

        // Auto-register in characters.playerbot for restart persistence.
        try
        {
            using var charConn = _db.Characters();

            // [SUI] HARD WALL (2026-08-10, Tesfff): an unattended REAL character
            // enrolls in the fleet like any bot, but must NEVER be persisted to the
            // playerbot registry — the next mangosd restart would respawn it as a
            // fabricated bot on a synthetic account, SaveToDB would stamp that
            // account over the owner's, and the character vanishes from their
            // account list. A character whose account exists in realmd is real.
            var realOwner = await charConn.QueryFirstOrDefaultAsync<int?>(
                "SELECT c.account FROM characters c WHERE c.guid = @Guid AND c.account IN (SELECT id FROM realmd.account)",
                new { Guid = guid });
            if (realOwner != null)
            {
                CircuitTrace.Hit(guid, "host: REAL character detected, NOT registered as bot");
                _logger.LogWarning(
                    "BotBrain: NOT registering {Name} (guid={Guid}) — real character on account {Account}",
                    bs.Name, guid, realOwner);
                return;
            }

            var existing = await charConn.QueryFirstOrDefaultAsync<PlayerbotIdentityRow>(
                @"SELECT char_guid AS CharGuid, spec_tab AS SpecTab, active_role AS ActiveRole
                  FROM playerbot WHERE char_guid = @Guid", new { Guid = guid });

            int specTab = existing?.SpecTab is >= 0 and <= 2
                ? existing.SpecTab
                : bs.SpecTab is >= 0 and <= 2 ? bs.SpecTab : 255;
            int activeRole = existing?.ActiveRole is >= 1 and <= 4
                ? existing.ActiveRole
                : bs.ActiveRole is >= 1 and <= 4
                    ? bs.ActiveRole
                    : specTab is >= 0 and <= 2 ? ResolveDefaultBotRole(guid, bs.ClassId, specTab) : 0;
            bs.SpecTab = specTab;
            bs.ActiveRole = activeRole;

            if (existing == null)
            {
                CircuitTrace.Hit(guid, "host: auto-registered in playerbot table");
                await charConn.ExecuteAsync(@"
                    INSERT INTO playerbot (char_guid, chance, ai, name, race, `class`, level, map, position_x, position_y, position_z, spec_tab, active_role)
                    VALUES (@Guid, 100, 'AiBotAI', @Name, @Race, @Class, @Level, @Map, @X, @Y, @Z, @SpecTab, @ActiveRole)",
                    new
                    {
                        Guid = guid,
                        Name = bs.Name,
                        Race = bs.Race,
                        Class = bs.ClassId,
                        Level = bs.Level,
                        Map = bs.MapId,
                        X = bs.X,
                        Y = bs.Y,
                        Z = bs.Z,
                        SpecTab = specTab,
                        ActiveRole = activeRole
                    });
                _logger.LogInformation("BotBrain: auto-registered {Name} (guid={Guid}) in playerbot table", bs.Name, guid);
            }
            else
            {
                CircuitTrace.Hit(guid, "host: playerbot row refreshed");
                await charConn.ExecuteAsync(@"
                    UPDATE playerbot SET name=@Name, level=@Level, map=@Map,
                           position_x=@X, position_y=@Y, position_z=@Z,
                           spec_tab=@SpecTab, active_role=@ActiveRole
                    WHERE char_guid = @Guid",
                    new
                    {
                        Guid = guid,
                        Name = bs.Name,
                        Level = bs.Level,
                        Map = bs.MapId,
                        X = bs.X,
                        Y = bs.Y,
                        Z = bs.Z,
                        SpecTab = specTab,
                        ActiveRole = activeRole
                    });
            }
        }
        catch (Exception ex)
        {
            CircuitTrace.Hit(guid, "host: playerbot auto-register failed");
            _logger.LogWarning(ex, "BotBrain: failed to auto-register bot {Guid} in playerbot table", guid);
        }
    }

    // ==================== Grouping mode load ====================

    private async Task LoadGroupingModeAsync()
    {
        try
        {
            using var conn = _db.Admin();
            var value = await conn.QueryFirstOrDefaultAsync<string>(
                "SELECT setting_value FROM bot_settings WHERE setting_key = 'grouping_mode'");

            if (value != null && int.TryParse(value, out int mode) && Enum.IsDefined(typeof(GroupingMode), mode))
            {
                CircuitTrace.Hit(0, "host: grouping mode loaded from DB", mode);
                _groupManager.Mode = (GroupingMode)mode;
                _logger.LogInformation("BotBrain: loaded grouping mode from DB: {Mode}", _groupManager.Mode);
            }
            else
            {
                CircuitTrace.Hit(0, "host: grouping mode defaulted to Off");
                _groupManager.Mode = GroupingMode.Off;
            }
        }
        catch (Exception ex)
        {
            CircuitTrace.Hit(0, "host: grouping mode load failed, defaulting to Off");
            _logger.LogWarning(ex, "BotBrain: failed to load grouping mode, defaulting to Off");
            _groupManager.Mode = GroupingMode.Off;
        }
    }

    // ==================== Personality persistence ====================

    private async Task PersistPersonalityAsync(int guid, BotPersonality p)
    {
        try
        {
            using var conn = _db.Admin();
            await conn.ExecuteAsync(@"
                INSERT INTO bot_personality
                    (bot_guid, patience, greed, curiosity, sociability, aggression, efficiency,
                     cautiousness, indecisiveness, spontaneity, chat_style, temperament, quirk_ids)
                VALUES
                    (@Guid, @Patience, @Greed, @Curiosity, @Sociability, @Aggression, @Efficiency,
                     @Cautiousness, @Indecisiveness, @Spontaneity, @ChatStyle, @Temperament, @QuirkIds)
                ON DUPLICATE KEY UPDATE
                    patience=@Patience, greed=@Greed, curiosity=@Curiosity,
                    sociability=@Sociability, aggression=@Aggression, efficiency=@Efficiency,
                    cautiousness=@Cautiousness, indecisiveness=@Indecisiveness,
                    spontaneity=@Spontaneity, chat_style=@ChatStyle, temperament=@Temperament,
                    quirk_ids=@QuirkIds",
                new
                {
                    Guid = guid,
                    p.Patience,
                    p.Greed,
                    p.Curiosity,
                    p.Sociability,
                    p.Aggression,
                    p.Efficiency,
                    p.Cautiousness,
                    p.Indecisiveness,
                    p.Spontaneity,
                    p.ChatStyle,
                    p.Temperament,
                    QuirkIds = string.Join(",", p.Quirks.Select(q => q.Id))
                });
        }
        catch (Exception ex)
        {
            CircuitTrace.Hit(guid, "host: personality persist failed");
            _logger.LogWarning(ex, "BotBrain: failed to persist personality for bot {Guid}", guid);
        }
    }

    // ==================== Core schema self-heal ====================

    /// <summary>
    /// Add the fork's identity, specialization, and active-role columns to
    /// characters.playerbot if they're missing.
    /// The base VMaNGOS table has only (char_guid, chance, ai); the fork reads/writes
    /// name/race/class/level/map/position_x/y/z. Each column is added independently and
    /// guarded against information_schema, so this is idempotent and survives a partial
    /// (some-columns-already-present) state. Portable across MySQL 5.6/5.7/8.0 and
    /// MariaDB — deliberately NOT using "ADD COLUMN IF NOT EXISTS" (MariaDB-only).
    /// AFTER clauses are omitted on purpose: column position is cosmetic and pinning it
    /// to a base column ("AFTER ai") would fail on any schema variant that renamed it.
    /// </summary>
    private async Task EnsurePlayerbotColumnsAsync()
    {
        // (column name, DDL type spec). `class` is a reserved word -> backticked in DDL,
        // but the information_schema lookup uses the bare name.
        var columns = new (string Name, string Ddl)[]
        {
            ("name",       "`name` VARCHAR(12) NOT NULL DEFAULT ''"),
            ("race",       "`race` TINYINT UNSIGNED NOT NULL DEFAULT 0"),
            ("class",      "`class` TINYINT UNSIGNED NOT NULL DEFAULT 0"),
            ("level",      "`level` TINYINT UNSIGNED NOT NULL DEFAULT 1"),
            ("map",        "`map` SMALLINT UNSIGNED NOT NULL DEFAULT 0"),
            ("position_x", "`position_x` FLOAT NOT NULL DEFAULT 0"),
            ("position_y", "`position_y` FLOAT NOT NULL DEFAULT 0"),
            ("position_z", "`position_z` FLOAT NOT NULL DEFAULT 0"),
            ("spec_tab",   "`spec_tab` TINYINT UNSIGNED NOT NULL DEFAULT 255"),
            ("active_role","`active_role` TINYINT UNSIGNED NOT NULL DEFAULT 0"),
        };

        try
        {
            using var conn = _db.Characters();
            int added = 0;

            foreach (var (name, ddl) in columns)
            {
                if (await ColumnExistsAsync(conn, "playerbot", name))
                    continue;   // cb:fold schema check detail

                await conn.ExecuteAsync($"ALTER TABLE playerbot ADD COLUMN {ddl}");
                added++;
                _logger.LogInformation("BotBrain: added missing playerbot.{Column} column", name);
            }

            if (added > 0)
                _logger.LogInformation("BotBrain: playerbot schema self-heal added {Count} column(s)", added);   // cb:fold logging only

            // Identity migration is deliberately left to the core talent planner. It
            // can distinguish a fresh zero-point bot (safe round-robin assignment)
            // from an existing compatible or conflicting build; SuperUI cannot safely
            // infer that from the registry row alone.
        }
        catch (Exception ex)
        {
            CircuitTrace.Hit(0, "host: playerbot schema self-heal failed");
            // Non-fatal: log loudly. If this fails (e.g. permissions), bot load will fail
            // later with the same 1054 and the operator gets a second, pointed signal.
            _logger.LogError(ex, "BotBrain: failed to self-heal characters.playerbot schema — bot load may fail with unknown-column errors until the identity columns exist");
        }
    }

    /// <summary>
    /// Portable column-existence check against information_schema. DATABASE() resolves
    /// to the connection's current schema (characters), so no DB name needs threading in.
    /// </summary>
    private static async Task<bool> ColumnExistsAsync(System.Data.IDbConnection conn, string table, string column)
    {
        var count = await conn.ExecuteScalarAsync<int>(
            @"SELECT COUNT(*) FROM information_schema.COLUMNS
              WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @table AND COLUMN_NAME = @column",
            new { table, column });
        return count > 0;
    }

    private static int ResolveDefaultBotRole(int guid, int classId, int specTab) => (classId, specTab) switch
    {
        (1, 2) => 3,                    // Protection Warrior   // cb:fold pure role lookup
        (1, _) => 1,   // cb:fold pure role lookup
        (2, 0) => 4,                    // Holy Paladin   // cb:fold pure role lookup
        (2, 1) => 3,                    // Protection Paladin   // cb:fold pure role lookup
        (2, _) => 1,   // cb:fold pure role lookup
        (3, _) => 2,   // cb:fold pure role lookup
        (4, _) => 1,   // cb:fold pure role lookup
        (5, 0 or 1) => 4,   // cb:fold pure role lookup
        (5, _) => 2,   // cb:fold pure role lookup
        (7, 0) => 2,   // cb:fold pure role lookup
        (7, 1) => 1,   // cb:fold pure role lookup
        (7, _) => 4,   // cb:fold pure role lookup
        (8, _) => 2,   // cb:fold pure role lookup
        (9, _) => 2,   // cb:fold pure role lookup
        (11, 0) => 2,   // cb:fold pure role lookup
        (11, 1) => ((guid / 3) % 2 == 0 ? 1 : 3), // Feral Cat/Bear alternation   // cb:fold pure role lookup
        (11, _) => 4,   // cb:fold pure role lookup
        _ => 0   // cb:fold pure role lookup
    };

    private sealed class PlayerbotIdentityRow
    {
        public int CharGuid { get; set; }
        public int SpecTab { get; set; } = 255;
        public int ActiveRole { get; set; }
    }
}
