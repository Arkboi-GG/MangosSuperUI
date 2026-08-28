using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using MangosSuperUI.BotLogic.Brain;
using MangosSuperUI.BotLogic.Core;
using MangosSuperUI.BotLogic.Data;
using MangosSuperUI.BotLogic.Planners;
using MangosSuperUI.Controllers;
using MangosSuperUI.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MangosSuperUI.Tests;

public sealed class BridgeIntegrityTests
{
    [Fact]
    public void BridgeMessage_DeserializesTopLevelCorrelation()
    {
        const string json = """
            {"type":"EVENT","payload":{"guid":14,"event":"TASK_COMPLETE","data":"ok"},"cbt":7123456789012}
            """;

        var message = JsonSerializer.Deserialize<BridgeMessage>(json);

        Assert.NotNull(message);
        Assert.Equal(7_123_456_789_012L, message.Cbt);
    }

    [Fact]
    public void TargetGuidPayload_SerializesExactCreatureIdentity()
    {
        string json = JsonSerializer.Serialize(new TargetGuidPayload
        {
            Entry = 257,
            Guid = 54_321
        });

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        Assert.Equal(257, root.GetProperty("entry").GetInt32());
        Assert.Equal(54_321, root.GetProperty("guid").GetInt32());
    }

    [Theory]
    [InlineData(CorrelatedSendStatus.Sent, "sent", true)]
    [InlineData(CorrelatedSendStatus.DefinitelyNotSent, "not_sent", false)]
    [InlineData(CorrelatedSendStatus.SessionSuperseded, "session_superseded", false)]
    [InlineData(CorrelatedSendStatus.OutcomeUnknown, "outcome_unknown", false)]
    public void ExactCreatureDispatch_ReportsTransportTruth(
        CorrelatedSendStatus status,
        string statusCode,
        bool sent)
    {
        var dispatch = new ExactCreatureCommandDispatch(status, 42, statusCode);

        Assert.Equal(statusCode, dispatch.StatusCode);
        Assert.Equal(sent, dispatch.Sent);
    }

    [Fact]
    public void PositiveOutcome_RequiresExactCorrelation()
    {
        var pending = Pending("MOVE_TO", "TASK_COMPLETE", 42);

        Assert.Equal(
            WaitOutcomeMatcher.Disposition.CorrelationMismatch,
            WaitOutcomeMatcher.Classify(pending, new BotEvent { EventType = "TASK_COMPLETE" }));
        Assert.Equal(
            WaitOutcomeMatcher.Disposition.CorrelationMismatch,
            WaitOutcomeMatcher.Classify(pending, new BotEvent { EventType = "TASK_COMPLETE", CorrelationId = 41 }));
        Assert.Equal(
            WaitOutcomeMatcher.Disposition.Positive,
            WaitOutcomeMatcher.Classify(pending, new BotEvent { EventType = "TASK_COMPLETE", CorrelationId = 42 }));
    }

    [Fact]
    public void QuestCastFailure_NegatesOnlyItsCorrelatedCast()
    {
        var pending = Pending("QUEST_CAST", "QUEST_CAST_ACK", 77);

        Assert.Equal(
            WaitOutcomeMatcher.Disposition.Negative,
            WaitOutcomeMatcher.Classify(pending, new BotEvent
            {
                EventType = "QUEST_CAST_FAIL",
                CorrelationId = 77,
                Data = "reason=target_not_found|entry=124|spell=2052"
            }));
        Assert.Equal(
            WaitOutcomeMatcher.Disposition.NotForPending,
            WaitOutcomeMatcher.Classify(Pending("MOVE_TO", "TASK_COMPLETE", 77), new BotEvent
            {
                EventType = "QUEST_CAST_FAIL",
                CorrelationId = 77
            }));
    }

    [Theory]
    [InlineData("POSSESSED_DROP")]
    [InlineData("CONSCRIPTED_DROP")]
    public void ControlFence_RequiresCorrelationAndDroppedCommandToken(string eventType)
    {
        var pending = Pending("MOVE_TO", "TASK_COMPLETE", 91);

        Assert.Equal(
            WaitOutcomeMatcher.Disposition.NotForPending,
            WaitOutcomeMatcher.Classify(pending, new BotEvent
            {
                EventType = eventType,
                CorrelationId = 91,
                Data = "QUEST_CAST"
            }));
        Assert.Equal(
            WaitOutcomeMatcher.Disposition.CorrelationMismatch,
            WaitOutcomeMatcher.Classify(pending, new BotEvent
            {
                EventType = eventType,
                CorrelationId = 90,
                Data = "MOVE_TO"
            }));
        Assert.Equal(
            WaitOutcomeMatcher.Disposition.Negative,
            WaitOutcomeMatcher.Classify(pending, new BotEvent
            {
                EventType = eventType,
                CorrelationId = 91,
                Data = "MOVE_TO"
            }));
    }

    [Fact]
    public void SetTaskApproachNoPath_IsTerminalOnlyForTaggedSource()
    {
        var pending = Pending("SET_TASK", "TASK_COMPLETE", 105);

        Assert.Equal(
            WaitOutcomeMatcher.Disposition.NotForPending,
            WaitOutcomeMatcher.Classify(pending, new BotEvent
            {
                EventType = "MOVE_FAILED",
                CorrelationId = 105,
                Data = "reason=no_path|source=move_point"
            }));
        Assert.Equal(
            WaitOutcomeMatcher.Disposition.Negative,
            WaitOutcomeMatcher.Classify(pending, new BotEvent
            {
                EventType = "MOVE_FAILED",
                CorrelationId = 105,
                Data = "reason=no_path|source=set_task_approach"
            }));
    }

    [Theory]
    [InlineData("MOVE_TO")]
    [InlineData("SET_TASK")]
    public void GrindBlocked_NegatesOnlyItsOwningTask(string command)
    {
        var pending = Pending(command, "TASK_COMPLETE", 106);

        Assert.Equal(
            WaitOutcomeMatcher.Disposition.Negative,
            WaitOutcomeMatcher.Classify(pending, new BotEvent
            {
                EventType = "GRIND_BLOCKED",
                CorrelationId = 106,
                Data = "x=1|y=2|z=3|reason=no_target"
            }));
        Assert.Equal(
            WaitOutcomeMatcher.Disposition.CorrelationMismatch,
            WaitOutcomeMatcher.Classify(pending, new BotEvent
            {
                EventType = "GRIND_BLOCKED",
                CorrelationId = 105
            }));
    }

    [Fact]
    public void GrindBlocked_PreservesSelfHealingPlannerContract()
    {
        var executor = new BotExecutor(
            bridge: null!,
            safety: null!,
            logger: NullLogger<BotExecutor>.Instance);
        var context = new BotContext
        {
            MapId = 1,
            Pos = new Vec3(4, 5, 6),
            ConsecutiveFailures = 3,
            Pending = Pending("MOVE_TO", "TASK_COMPLETE", 107)
        };

        bool handled = executor.OnEvent(context, new BotEvent
        {
            EventType = "GRIND_BLOCKED",
            CorrelationId = 107,
            Data = "x=10|y=20|z=30|reason=no_target"
        });

        Assert.True(handled);
        Assert.Null(context.Pending);
        Assert.NotNull(context.Failure);
        Assert.Equal("GRIND", context.Failure.CommandType);
        Assert.Equal("no_target", context.Failure.Reason);
        Assert.Equal(10, context.Failure.Dest?.X);
        Assert.Equal(3, context.ConsecutiveFailures);
    }

    [Fact]
    public void NoWaitGrindBlocked_RequiresLatestTaskOwnerCorrelation()
    {
        var owner = new NoWaitCommandOwner
        {
            CorrelationId = 401,
            CommandType = "SET_TASK",
            OwnsTaskMotion = true,
            CanGrindBlock = true
        };

        Assert.Equal(
            WaitOutcomeMatcher.Disposition.CorrelationMismatch,
            WaitOutcomeMatcher.Classify(owner, new BotEvent
            {
                EventType = "GRIND_BLOCKED",
                CorrelationId = 400
            }));
        Assert.Equal(
            WaitOutcomeMatcher.Disposition.Negative,
            WaitOutcomeMatcher.Classify(owner, new BotEvent
            {
                EventType = "GRIND_BLOCKED",
                CorrelationId = 401
            }));
    }

    [Fact]
    public void StaleNoWaitGrindBlocked_CannotMutateReplacementTask()
    {
        var executor = new BotExecutor(
            bridge: null!,
            safety: null!,
            logger: NullLogger<BotExecutor>.Instance);
        var context = new BotContext { Pos = new Vec3(4, 5, 6) };
        context.NoWaitTaskOwner = new NoWaitCommandOwner
        {
            CorrelationId = 502,
            CommandType = "SET_TASK",
            OwnsTaskMotion = true,
            CanGrindBlock = true
        };

        bool staleHandled = executor.OnEvent(context, new BotEvent
        {
            EventType = "GRIND_BLOCKED",
            CorrelationId = 501,
            Data = "x=10|y=20|z=30|reason=no_target"
        });

        Assert.False(staleHandled);
        Assert.Null(context.Failure);
        Assert.Equal(502, context.NoWaitTaskOwner?.CorrelationId);

        bool currentHandled = executor.OnEvent(context, new BotEvent
        {
            EventType = "GRIND_BLOCKED",
            CorrelationId = 502,
            Data = "x=10|y=20|z=30|reason=no_target"
        });

        Assert.True(currentHandled);
        Assert.Equal("GRIND", context.Failure?.CommandType);
        Assert.Null(context.NoWaitTaskOwner);
    }

    [Fact]
    public void NoWaitPathUnsafe_RequiresCurrentMoveOwnerAndStampsRecoveryFailure()
    {
        var executor = new BotExecutor(
            bridge: null!,
            safety: null!,
            logger: NullLogger<BotExecutor>.Instance);
        var owner = new NoWaitCommandOwner
        {
            CorrelationId = 552,
            CommandType = "MOVE_TO",
            OwnsTaskMotion = true
        };
        var context = new BotContext
        {
            MapId = 1,
            Pos = new Vec3(4, 5, 6),
            ConsecutiveFailures = 2
        };
        context.NoWaitTaskOwner = owner;
        context.LatestNoWaitCommand = owner;

        Assert.False(executor.OnEvent(context, new BotEvent
        {
            EventType = "PATH_UNSAFE",
            CorrelationId = 551,
            Data = "dest_x=10|dest_y=20|dest_z=30|danger_level=27"
        }));
        Assert.Same(owner, context.NoWaitTaskOwner);
        Assert.Null(context.Failure);

        Assert.True(executor.OnEvent(context, new BotEvent
        {
            EventType = "PATH_UNSAFE",
            CorrelationId = 552,
            Data = "dest_x=10|dest_y=20|dest_z=30|danger_level=27"
        }));
        Assert.Null(context.NoWaitTaskOwner);
        Assert.Null(context.LatestNoWaitCommand);
        Assert.Equal("MOVE_TO", context.Failure?.CommandType);
        Assert.Equal("path_unsafe", context.Failure?.Reason);
        Assert.Equal(10, context.Failure?.Dest?.X);
        Assert.Equal(27, context.Failure?.DangerLevel);
        Assert.Equal(3, context.ConsecutiveFailures);
    }

    [Fact]
    public void NoWaitTaskComplete_RetiresExactOwnerAndMarksProgress()
    {
        var executor = new BotExecutor(
            bridge: null!,
            safety: null!,
            logger: NullLogger<BotExecutor>.Instance);
        var owner = new NoWaitCommandOwner
        {
            CorrelationId = 652,
            CommandType = "SET_TASK",
            OwnsTaskMotion = true
        };
        DateTime priorProgress = DateTime.UtcNow - TimeSpan.FromMinutes(1);
        var context = new BotContext
        {
            LastProgressUtc = priorProgress,
            ConsecutiveFailures = 4,
            Failure = new WaitFailure { CommandType = "MOVE_TO", Reason = "no_path" }
        };
        context.NoWaitTaskOwner = owner;
        context.LatestNoWaitCommand = owner;

        Assert.True(executor.OnEvent(context, new BotEvent
        {
            EventType = "TASK_COMPLETE",
            CorrelationId = 652
        }));
        Assert.Null(context.NoWaitTaskOwner);
        Assert.Null(context.LatestNoWaitCommand);
        Assert.Null(context.Failure);
        Assert.Equal(0, context.ConsecutiveFailures);
        Assert.True(context.LastProgressUtc > priorProgress);
    }

    [Fact]
    public void NoWaitMoveFailed_IsOneShotEvenWithoutDurableIdentity()
    {
        var executor = new BotExecutor(
            bridge: null!,
            safety: null!,
            logger: NullLogger<BotExecutor>.Instance);
        var owner = new NoWaitCommandOwner
        {
            CorrelationId = 752,
            CommandType = "MOVE_TO",
            OwnsTaskMotion = true
        };
        var context = new BotContext();
        context.NoWaitTaskOwner = owner;
        context.LatestNoWaitCommand = owner;
        var outcome = new BotEvent
        {
            EventType = "MOVE_FAILED",
            CorrelationId = 752,
            Data = "reason=no_path|dest_x=10|dest_y=20"
        };

        Assert.True(executor.OnEvent(context, outcome));
        Assert.Null(context.NoWaitTaskOwner);
        Assert.Null(context.LatestNoWaitCommand);
        Assert.False(executor.OnEvent(context, outcome));
    }

    [Fact]
    public void NoWaitControlDrop_RequiresLatestCommandOwner()
    {
        var executor = new BotExecutor(
            bridge: null!,
            safety: null!,
            logger: NullLogger<BotExecutor>.Instance);
        var context = new BotContext();
        context.LatestNoWaitCommand = new NoWaitCommandOwner
        {
            CorrelationId = 602,
            CommandType = "COMBAT_DIRECTIVE"
        };

        Assert.False(executor.OnEvent(context, new BotEvent
        {
            EventType = "POSSESSED_DROP",
            CorrelationId = 601,
            Data = "COMBAT_DIRECTIVE"
        }));
        Assert.False(context.Possessed);

        Assert.True(executor.OnEvent(context, new BotEvent
        {
            EventType = "POSSESSED_DROP",
            CorrelationId = 602,
            Data = "COMBAT_DIRECTIVE"
        }));
        Assert.True(context.Possessed);
        Assert.Null(context.LatestNoWaitCommand);
    }

    [Fact]
    public void CorrelationIds_AreMonotonicAndExactlyRepresentableByDouble()
    {
        long first = BridgeCorrelation.NextId();
        long second = BridgeCorrelation.NextId();

        Assert.True(first > 0);
        Assert.Equal(first + 1, second);
        Assert.True(second < 1L << 53);
        Assert.Equal(second, (long)(double)second);
    }

    [Fact]
    public void ExactWaitClear_NeverErasesAReplacementWaiter()
    {
        var context = new BotContext();
        var first = Pending("MOVE_TO", "TASK_COMPLETE", 201);
        var replacement = Pending("QUEST_CAST", "QUEST_CAST_ACK", 202);
        context.Pending = first;
        context.Pending = replacement;

        Assert.False(context.TryClearPending(first));
        Assert.Same(replacement, context.Pending);
        Assert.True(context.TryClearPending(replacement));
        Assert.Null(context.Pending);
    }

    [Fact]
    public void ControlFence_ReleasesOnlyAfterANewerState()
    {
        DateTime fence = new(2026, 8, 26, 22, 0, 0, DateTimeKind.Utc);
        var context = new BotContext
        {
            ControlFenceObservedUtc = fence,
            LastStateReceivedUtc = fence
        };

        Assert.True(context.HasUnreleasedControlFence);
        context.LastStateReceivedUtc = fence.AddTicks(1);
        Assert.False(context.HasUnreleasedControlFence);
    }

    [Fact]
    public void SensoryFreshness_IgnoresRecentNonStateTraffic()
    {
        DateTime now = new(2026, 8, 26, 22, 0, 0, DateTimeKind.Utc);
        var state = new BotState
        {
            ConnectedAt = now - TimeSpan.FromMinutes(1),
            LastUpdate = now,
            LastStateReceivedUtc = now - BotBridgeService.SensoryFeedStaleAfter - TimeSpan.FromMilliseconds(1),
            HasReceivedState = true
        };

        Assert.True(BotBridgeService.IsSensoryFeedStale(state, now));
        Assert.False(BotBridgeService.HasFreshSensoryState(state, now));
    }

    [Fact]
    public void SensoryFreshness_BoundsConnectionThatNeverHydrates()
    {
        DateTime now = new(2026, 8, 26, 22, 0, 0, DateTimeKind.Utc);
        var state = new BotState
        {
            ConnectedAt = now - BotBridgeService.SensoryFeedStaleAfter,
            LastUpdate = now,
            LastStateReceivedUtc = DateTime.MinValue,
            HasReceivedState = false
        };

        Assert.True(BotBridgeService.IsSensoryFeedStale(state, now));
        Assert.False(BotBridgeService.HasFreshSensoryState(state, now));
    }

    [Fact]
    public void PreHelloSocket_IsBoundedByRecycleDeadline()
    {
        DateTime now = new(2026, 8, 26, 22, 0, 0, DateTimeKind.Utc);

        Assert.False(BotBridgeService.HasHelloTimedOut(
            now - BotBridgeService.SensoryFeedRecycleAfter + TimeSpan.FromTicks(1),
            now));
        Assert.True(BotBridgeService.HasHelloTimedOut(
            now - BotBridgeService.SensoryFeedRecycleAfter,
            now));
    }

    [Fact]
    public void Snapshot_CarriesExactActiveSessionGeneration()
    {
        const int guid = 14;
        var bridge = new BotBridgeService(NullLogger<BotBridgeService>.Instance, hub: null!);
        bridge.Connections[guid] = new BotConnection
        {
            Guid = guid,
            SessionId = 77,
            State = new BotState { Guid = guid }
        };

        BotStateSnapshot snapshot = Assert.IsType<BotStateSnapshot>(bridge.GetBotStateSnapshot(guid));

        Assert.Equal(77, snapshot.BridgeSessionId);
    }

    [Fact]
    public void CombatDirectiveEmission_IsScopedToSensedBridgeSession()
    {
        var context = new BotContext
        {
            CombatDirective = CombatDirective.Assist(anchorGuid: 14)
        };

        context.Sense(new BotStateSnapshot { BridgeSessionId = 81 });
        Assert.True(context.NeedsCombatDirectiveEmission);

        context.MarkCombatDirectiveEmitted();
        Assert.False(context.NeedsCombatDirectiveEmission);

        context.Sense(new BotStateSnapshot { BridgeSessionId = 81 });
        Assert.False(context.NeedsCombatDirectiveEmission);

        context.Sense(new BotStateSnapshot { BridgeSessionId = 82 });
        Assert.Equal(CombatDirective.Assist(anchorGuid: 14), context.CombatDirective);
        Assert.True(context.NeedsCombatDirectiveEmission);

        context.MarkCombatDirectiveEmitted();
        Assert.False(context.NeedsCombatDirectiveEmission);
    }

    [Fact]
    public async Task CorrelatedSend_RefusesReplacementSessionBeforeWriting()
    {
        const int guid = 14;
        var bridge = new BotBridgeService(NullLogger<BotBridgeService>.Instance, hub: null!);
        bridge.Connections[guid] = new BotConnection
        {
            Guid = guid,
            SessionId = 82,
            BridgeProtocol = BotBridgeService.RequiredCorrelatedOutcomeProtocol
        };

        CorrelatedSendStatus status = await bridge.TrySendCorrelatedAsync(
            guid,
            "MOVE_TO",
            new { x = 1, y = 2, z = 3 },
            correlationId: 700,
            expectedSessionId: 81);

        Assert.Equal(CorrelatedSendStatus.SessionSuperseded, status);
    }

    [Fact]
    public void SupersededWatchdog_CannotOverwriteReplacementProjection()
    {
        const int guid = 14;
        var bridge = new BotBridgeService(NullLogger<BotBridgeService>.Instance, hub: null!);
        var oldConnection = new BotConnection
        {
            Guid = guid,
            SessionId = 91,
            State = new BotState { Guid = guid, SensoryFeedStale = false }
        };
        var replacement = new BotConnection
        {
            Guid = guid,
            SessionId = 92,
            State = new BotState { Guid = guid, SensoryFeedStale = false }
        };
        bridge.Connections[guid] = replacement;
        bridge.BotStates[guid] = replacement.State;

        bool published = bridge.TryMarkSensoryFeedStale(oldConnection, observedStateTicks: 0);

        Assert.False(published);
        Assert.Same(replacement.State, bridge.BotStates[guid]);
        Assert.False(replacement.State.SensoryFeedStale);
    }

    [Fact]
    public void SupersededLoadoutAck_CannotOverwriteReplacementProjection()
    {
        const int guid = 14;
        var bridge = new BotBridgeService(NullLogger<BotBridgeService>.Instance, hub: null!);
        var oldConnection = new BotConnection
        {
            Guid = guid,
            SessionId = 101,
            State = new BotState { Guid = guid, CombatConfigRevision = 1 }
        };
        var replacement = new BotConnection
        {
            Guid = guid,
            SessionId = 102,
            State = new BotState { Guid = guid, CombatConfigRevision = 9 }
        };
        bridge.Connections[guid] = replacement;
        bridge.BotStates[guid] = replacement.State;

        bool applied = bridge.TryApplyCombatLoadoutAck(oldConnection, new CombatLoadoutAck
        {
            Guid = guid,
            Revision = 2,
            SpecTab = 1,
            ActiveRole = 2,
            TalentProfile = "stale",
            TalentProfileState = "valid",
            RotationSource = "profile",
            RotationProfile = "stale",
            LoadedInstructions = 4
        });

        Assert.False(applied);
        Assert.Same(replacement.State, bridge.BotStates[guid]);
        Assert.Equal((uint)9, replacement.State.CombatConfigRevision);
    }

    [Fact]
    public void SupersededAcceptedTaskOutcome_CannotOverwriteReplacementProjection()
    {
        const int guid = 14;
        var bridge = new BotBridgeService(NullLogger<BotBridgeService>.Instance, hub: null!);
        var oldConnection = new BotConnection
        {
            Guid = guid,
            SessionId = 111,
            State = new BotState { Guid = guid, TaskState = "GRIND" }
        };
        var replacement = new BotConnection
        {
            Guid = guid,
            SessionId = 112,
            State = new BotState { Guid = guid, TaskState = "FOLLOW" }
        };
        bridge.Connections[guid] = replacement;
        bridge.BotStates[guid] = replacement.State;

        bool applied = bridge.TryApplyAcceptedTaskState(oldConnection, "IDLE");

        Assert.False(applied);
        Assert.Same(replacement.State, bridge.BotStates[guid]);
        Assert.Equal("FOLLOW", replacement.State.TaskState);
    }

    [Fact]
    public async Task ReplacedSession_IsRejectedInsideMutationGateBeforeWaitResolution()
    {
        const int guid = 14;
        var bridge = new BotBridgeService(
            NullLogger<BotBridgeService>.Instance,
            hub: null!);
        var brain = new BotBrainService(
            bridge,
            db: null!,
            tracker: null!,
            quirkLoader: null!,
            dbInit: null!,
            NullLogger<BotBrainService>.Instance,
            NullLoggerFactory.Instance,
            driver: null!,
            quests: null!,
            safety: null!,
            spawns: null!,
            zoneData: null!,
            fallRecorder: null!);
        var context = new BotContext
        {
            Guid = guid,
            Pending = Pending("MOVE_TO", "TASK_COMPLETE", 301)
        };

        var contextsField = typeof(BotBrainService).GetField(
            "_contexts",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var contexts = Assert.IsType<ConcurrentDictionary<int, BotContext>>(
            contextsField?.GetValue(brain));
        contexts[guid] = context;

        bridge.Connections[guid] = new BotConnection { SessionId = 1, Guid = guid };
        await context.MutationGate.WaitAsync();
        Task<bool> routing;
        try
        {
            // Model an EVENT that passed the socket entry check and then waited
            // behind SignalR/planner work while a replacement HELLO took over.
            routing = brain.HandleBridgeEventAsync(guid, new BotEvent
            {
                BridgeSessionId = 1,
                CorrelationId = 301,
                EventType = "TASK_COMPLETE"
            });
            await Task.Yield();
            Assert.False(routing.IsCompleted);
            bridge.Connections[guid] = new BotConnection { SessionId = 2, Guid = guid };
        }
        finally
        {
            context.MutationGate.Release();
        }

        Assert.False(await routing);
        Assert.NotNull(context.Pending);
        Assert.Equal(301, context.Pending.CorrelationId);
    }

    [Fact]
    public void GroupFormationAck_RequiresExactSessionCorrelationAndTopology()
    {
        PendingGroupMutation pending = PendingGroup(
            GroupMutationKind.Form,
            leaderGuid: 14,
            sessionId: 801,
            cbt: 901,
            14, 15, 16);

        GroupBridgeOutcome accepted = GroupBridgeOutcomeMatcher.Classify(
            pending,
            eventGuid: 14,
            new BotEvent
            {
                BridgeSessionId = 801,
                CorrelationId = 901,
                EventType = "FORM_GROUP_ACK",
                Data = "leader_guid=14|member_guids=16,14,15"
            });
        Assert.Equal(GroupBridgeOutcomeDisposition.Accepted, accepted.Disposition);

        GroupBridgeOutcome wrongSession = GroupBridgeOutcomeMatcher.Classify(
            pending,
            eventGuid: 14,
            new BotEvent
            {
                BridgeSessionId = 800,
                CorrelationId = 901,
                EventType = "FORM_GROUP_ACK",
                Data = "leader_guid=14|member_guids=14,15,16"
            });
        Assert.Equal(GroupBridgeOutcomeDisposition.Ignore, wrongSession.Disposition);

        GroupBridgeOutcome missingMember = GroupBridgeOutcomeMatcher.Classify(
            pending,
            eventGuid: 14,
            new BotEvent
            {
                BridgeSessionId = 801,
                CorrelationId = 901,
                EventType = "FORM_GROUP_ACK",
                Data = "leader_guid=14|member_guids=14,15"
            });
        Assert.Equal(GroupBridgeOutcomeDisposition.ProtocolMismatch, missingMember.Disposition);
    }

    [Fact]
    public void GroupDisbandFailure_IsARejectedExactTerminal()
    {
        PendingGroupMutation pending = PendingGroup(
            GroupMutationKind.Disband,
            leaderGuid: 14,
            sessionId: 802,
            cbt: 902,
            14, 15);

        GroupBridgeOutcome rejected = GroupBridgeOutcomeMatcher.Classify(
            pending,
            eventGuid: 14,
            new BotEvent
            {
                BridgeSessionId = 802,
                CorrelationId = 902,
                EventType = "GROUP_DISBAND_FAIL",
                Data = "topology_mismatch"
            });

        Assert.Equal(GroupBridgeOutcomeDisposition.Rejected, rejected.Disposition);
        Assert.Equal("topology_mismatch", rejected.Detail);
    }

    [Fact]
    public void UnknownGroupFormation_ProjectsExactTopologyForManualReconciliation()
    {
        var batch = new GroupMutationBatchResult
        {
            Results = new[]
            {
                new GroupMutationResult
                {
                    Status = GroupMutationStatus.OutcomeUnknown,
                    Detail = "ack_timeout",
                    Operation = GroupMutationKind.Form,
                    LeaderGuid = 14,
                    MemberGuids = new[] { 14, 15, 16 },
                    CorrelationId = 903
                }
            }
        };
        MethodInfo projector = typeof(BotsController).GetMethod(
            "GroupMutationOutcomes",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        object projection = projector.Invoke(null, new object[] { batch })!;
        using JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(projection));
        JsonElement outcome = document.RootElement[0];

        Assert.Equal("outcome_unknown", outcome.GetProperty("status").GetString());
        Assert.Equal("form", outcome.GetProperty("operation").GetString());
        Assert.Equal(14, outcome.GetProperty("leaderGuid").GetInt32());
        Assert.Equal(new[] { 14, 15, 16 },
            outcome.GetProperty("memberGuids").EnumerateArray().Select(value => value.GetInt32()));
        Assert.Equal(903, outcome.GetProperty("cbt").GetInt64());
    }

    [Fact]
    public void AutoGroupPlanning_DoesNotPrecommitManagerTopology()
    {
        var manager = new GroupManager(db: null!, NullLogger.Instance)
        {
            Mode = GroupingMode.Sticky
        };
        var bots = new Dictionary<int, BotIdentity>
        {
            [14] = new() { Guid = 14, Name = "Tank", ClassId = 1, Level = 10 },
            [15] = new() { Guid = 15, Name = "Healer", ClassId = 5, Level = 10 }
        };

        List<BotGroup> candidates = manager.PlanAutoGroups(bots);

        Assert.Single(candidates);
        Assert.Empty(manager.GetAllGroups());
        Assert.False(manager.IsGrouped(14));
        Assert.False(manager.IsGrouped(15));
    }

    [Fact]
    public void GroupPlanner_UsesStateQuestLogWithoutRetiredQueryDispatch()
    {
        var planner = new QuestPlanner(
            quests: null!,
            spawns: null!,
            NullLogger<QuestPlanner>.Instance,
            safety: null!);
        var context = new BotContext
        {
            Guid = 14,
            GroupOrder = GroupOrder.Forming(anchorGuid: 14),
            QuestLogStampUtc = DateTime.MinValue
        };

        StepResult result = planner.PlanNext(context, new BotStateSnapshot());

        Assert.IsType<StepResult.Continue>(result);
        Assert.Equal(5, BotBridgeService.RequiredTransactionalGroupProtocol);
    }

    [Fact]
    public void UnsupportedVirtualWait_BecomesExplicitPlannerFailure()
    {
        var context = new BotContext
        {
            Guid = -1,
            Pending = Pending("FUTURE_COMMAND", "FUTURE_ACK", 0)
        };
        MethodInfo resolver = typeof(GroupCoordinator).GetMethod(
            "TryResolveVirtualWait",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        var previousLog = GroupCoordinator.Log;
        GroupCoordinator.Log = NullLogger.Instance;

        try
        {
            bool resolved = (bool)resolver.Invoke(null, new object?[]
            {
                new GroupPlan(), context, new List<BotContext>(), null,
                14, GroupPhase.Objective
            })!;

            Assert.True(resolved);
            Assert.Null(context.Pending);
            Assert.NotNull(context.Failure);
            Assert.Equal("FUTURE_COMMAND", context.Failure.CommandType);
            Assert.Equal("unsupported_virtual_wait", context.Failure.Reason);
        }
        finally
        {
            GroupCoordinator.Log = previousLog;
        }
    }

    [Fact]
    public void QuestOverflow_SkipsKnownNoPathSpawnAndDispatchesPathableAlternative()
    {
        var (planner, context, identity, safety) = OverflowScenario();
        safety.RecordNoPathDest(mapId: 0, x: 10f, y: 0f);

        StepResult result = planner.PlanNext(context, new BotStateSnapshot());

        var issue = Assert.IsType<StepResult.Issue>(result);
        Assert.Equal("MOVE_TO", issue.Command.Type);
        Assert.Equal(100f, Assert.IsType<float>(issue.Command.Payload["x"]));
        Assert.Equal(0f, Assert.IsType<float>(issue.Command.Payload["y"]));
        Assert.Equal(2f, Assert.IsType<float>(issue.Command.Payload["z"]));
        Assert.Equal(1, identity.QuestOverflowGrinds[1234]);
    }

    [Fact]
    public void QuestOverflow_AllSpawnsKnownNoPath_DoesNotDispatchOrConsumeAttempt()
    {
        var (planner, context, identity, safety) = OverflowScenario();
        safety.RecordNoPathDest(mapId: 0, x: 10f, y: 0f);
        safety.RecordNoPathDest(mapId: 0, x: 100f, y: 0f);

        StepResult result = planner.PlanNext(context, new BotStateSnapshot());

        Assert.IsType<StepResult.Blocked>(result);
        Assert.DoesNotContain(1234, identity.QuestOverflowGrinds.Keys);
        Assert.Null(context.Held);
    }

    private static (QuestPlanner Planner, BotContext Context, BotIdentity Identity, ZoneSafetyMap Safety)
        OverflowScenario()
    {
        var safety = new ZoneSafetyMap(null!, NullLogger<ZoneSafetyMap>.Instance);
        var planner = new QuestPlanner(
            new QuestGraphLoader(null!, NullLogger<QuestGraphLoader>.Instance),
            new CreatureSpawnLoader(null!, NullLogger<CreatureSpawnLoader>.Instance),
            NullLogger<QuestPlanner>.Instance,
            safety);
        var objective = new QuestObjective
        {
            Slot = 1,
            CreatureOrGOId = 456,
            Count = 1,
            GrindMap = 0,
            GrindX = 10f,
            GrindY = 0f,
            GrindZ = 1f,
            SpawnPositions = new List<(float X, float Y, float Z)>
            {
                (10f, 0f, 1f),
                (100f, 0f, 2f)
            }
        };
        var quest = new BatchQuest
        {
            QuestId = 1234,
            Accepted = true,
            Node = new QuestNode
            {
                QuestId = 1234,
                Title = "Overflow regression",
                QuestLevel = 10,
                MinLevel = 1,
                Objectives = new[] { objective }
            }
        };
        var scratch = new QuestScratch();
        scratch.Batch.Add(quest);
        scratch.LastGatherPos = new Vec3(0f, 0f, 0f);
        var identity = new BotIdentity { Guid = 14, Name = "Probe", Level = 10 };
        var context = new BotContext
        {
            Guid = 14,
            Name = "Probe",
            Level = 10,
            Identity = identity,
            MapId = 0,
            Pos = new Vec3(0f, 0f, 0f),
            Quest = scratch,
            QuestLog = new Dictionary<int, QuestLogEntry>
            {
                [1234] = new()
                {
                    Status = 3,
                    MobCounts = new[] { 1, 0, 0, 0 }
                }
            }
        };
        context.SetStep("plan");
        return (planner, context, identity, safety);
    }

    private static Outstanding Pending(string command, string expected, long cbt)
        => new()
        {
            CommandType = command,
            ExpectedEvent = expected,
            CorrelationId = cbt,
            SentUtc = DateTime.UtcNow,
            DeadlineUtc = DateTime.UtcNow + TimeSpan.FromMinutes(1)
        };

    private static PendingGroupMutation PendingGroup(
        GroupMutationKind kind,
        int leaderGuid,
        long sessionId,
        long cbt,
        params int[] memberGuids)
        => new()
        {
            Kind = kind,
            LeaderGuid = leaderGuid,
            SessionId = sessionId,
            CorrelationId = cbt,
            MemberGuids = memberGuids.OrderBy(guid => guid).ToArray()
        };
}
