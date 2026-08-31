using MangosSuperUI.BotLogic.Brain;
using MangosSuperUI.BotLogic.Core;
using Xunit;

namespace MangosSuperUI.Tests;

public sealed class CombatStillRecoveryTests
{
    private static readonly DateTime T0 = new(2026, 8, 27, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void FreshCombatStates_AdvanceFixedPositionTimer()
    {
        var (ctx, id) = ContextAt(100, 200, inCombat: true);

        ctx.LastStateReceivedUtc = T0;
        Assert.Equal(StillObservationKind.Seeded, BotBrain.ObserveFreshStillPosition(ctx, id).Kind);

        StillObservation observation = default;
        for (int seconds = 5; seconds <= 120; seconds += 5)
        {
            ctx.LastStateReceivedUtc = T0.AddSeconds(seconds);
            observation = BotBrain.ObserveFreshStillPosition(ctx, id);
        }

        Assert.True(ctx.InCombat);
        Assert.Equal(StillObservationKind.Still, observation.Kind);
        Assert.Equal(120, observation.ElapsedSeconds);
        Assert.Equal(T0, id.StillSinceUtc);
    }

    [Fact]
    public void RepeatedBrainTick_CannotAgeTimerWithoutNewState()
    {
        var (ctx, id) = ContextAt(100, 200, inCombat: true);
        ctx.LastStateReceivedUtc = T0;
        BotBrain.ObserveFreshStillPosition(ctx, id);

        ctx.LastStateReceivedUtc = T0.AddSeconds(5);
        StillObservation firstFresh = BotBrain.ObserveFreshStillPosition(ctx, id);
        StillObservation repeated = BotBrain.ObserveFreshStillPosition(ctx, id);

        Assert.Equal(5, firstFresh.ElapsedSeconds);
        Assert.Equal(StillObservationKind.StateNotAdvanced, repeated.Kind);
        Assert.Equal(T0, id.StillSinceUtc);
    }

    [Fact]
    public void TelemetryGap_RestartsStationaryProof()
    {
        var (ctx, id) = ContextAt(100, 200, inCombat: true);
        ctx.LastStateReceivedUtc = T0;
        BotBrain.ObserveFreshStillPosition(ctx, id);

        ctx.LastStateReceivedUtc = T0.AddSeconds(5);
        BotBrain.ObserveFreshStillPosition(ctx, id);
        ctx.LastStateReceivedUtc = T0.AddSeconds(21); // 16s since the prior STATE; freshness wall is 15s

        StillObservation observation = BotBrain.ObserveFreshStillPosition(ctx, id);

        Assert.Equal(StillObservationKind.ContinuityReset, observation.Kind);
        Assert.Equal(T0.AddSeconds(21), id.StillSinceUtc);
        Assert.Equal(0, observation.ElapsedSeconds);
    }

    [Fact]
    public void BridgeSessionReplacement_RestartsStationaryProofEvenWhenGapIsFresh()
    {
        var (ctx, id) = ContextAt(100, 200, inCombat: true);
        ctx.Sense(State(sessionId: 77, stateUtc: T0, inCombat: true));
        Assert.Equal(StillObservationKind.Seeded, BotBrain.ObserveFreshStillPosition(ctx, id).Kind);

        ctx.Sense(State(sessionId: 78, stateUtc: T0.AddSeconds(5), inCombat: true));
        StillObservation observation = BotBrain.ObserveFreshStillPosition(ctx, id);

        Assert.Equal(StillObservationKind.BridgeSessionChanged, observation.Kind);
        Assert.Equal(T0.AddSeconds(5), id.StillSinceUtc);
        Assert.Equal(78, ctx.LastStillObservationBridgeSessionId);
        Assert.Equal(0, observation.ElapsedSeconds);
    }

    [Fact]
    public void RealMovement_RestartsStationaryProof()
    {
        var (ctx, id) = ContextAt(100, 200, inCombat: true);
        ctx.LastStateReceivedUtc = T0;
        BotBrain.ObserveFreshStillPosition(ctx, id);
        id.WedgeStreak = 2;

        ctx.Pos = new Vec3(104, 200, 30);
        ctx.LastStateReceivedUtc = T0.AddSeconds(5);

        StillObservation observation = BotBrain.ObserveFreshStillPosition(ctx, id);

        Assert.Equal(StillObservationKind.Moved, observation.Kind);
        Assert.Equal(104, id.StillAnchorX);
        Assert.Equal(T0.AddSeconds(5), id.StillSinceUtc);
        Assert.Equal(T0.AddSeconds(5), ctx.LastPhysicalAdvanceUtc);
        Assert.Equal(ctx.BridgeSessionId, ctx.LastPhysicalAdvanceBridgeSessionId);
        Assert.Equal(0, id.WedgeStreak);
    }

    [Fact]
    public void StaleOutcome_WithFreshSameSessionMovement_DoesNotTripWedge()
    {
        var (ctx, _) = ContextAt(100, 200, inCombat: false);
        ctx.Sense(State(sessionId: 77, stateUtc: T0, inCombat: false));
        ctx.LastProgressUtc = T0.AddSeconds(-151);
        ctx.LastPhysicalAdvanceUtc = T0.AddSeconds(-10);
        ctx.LastPhysicalAdvanceBridgeSessionId = 77;

        Assert.Equal(WedgeGate.PhysicalAdvanceFresh, BotBrain.ClassifyWedgeGate(ctx, T0));
    }

    [Fact]
    public void FreshMovement_CannotMaskFastFailureLoop()
    {
        var (ctx, _) = ContextAt(100, 200, inCombat: false);
        ctx.Sense(State(sessionId: 77, stateUtc: T0, inCombat: false));
        ctx.LastProgressUtc = T0.AddSeconds(-151);
        ctx.LastPhysicalAdvanceUtc = T0.AddSeconds(-1);
        ctx.LastPhysicalAdvanceBridgeSessionId = 77;
        ctx.ConsecutiveFailures = 8;

        Assert.Equal(WedgeGate.FailureLoop, BotBrain.ClassifyWedgeGate(ctx, T0));
    }

    [Fact]
    public void OldOrSupersededMovement_CannotMaskStaleOutcome()
    {
        var (ctx, _) = ContextAt(100, 200, inCombat: false);
        ctx.Sense(State(sessionId: 77, stateUtc: T0, inCombat: false));
        ctx.LastProgressUtc = T0.AddSeconds(-151);
        ctx.LastPhysicalAdvanceUtc = T0.AddSeconds(-16);
        ctx.LastPhysicalAdvanceBridgeSessionId = 77;

        Assert.Equal(WedgeGate.OutcomeStale, BotBrain.ClassifyWedgeGate(ctx, T0));

        ctx.LastPhysicalAdvanceUtc = T0.AddSeconds(-1);
        ctx.LastPhysicalAdvanceBridgeSessionId = 76;
        Assert.Equal(WedgeGate.OutcomeStale, BotBrain.ClassifyWedgeGate(ctx, T0));
    }

    [Fact]
    public void DeadBot_IsOwnedByDeathRecoveryInsteadOfSlowWedge()
    {
        var (ctx, _) = ContextAt(100, 200, inCombat: false);
        ctx.LastProgressUtc = T0.AddSeconds(-151);
        ctx.Dead = true;

        Assert.Equal(WedgeGate.DeathRecovery, BotBrain.ClassifyWedgeGate(ctx, T0));
    }

    [Fact]
    public void ActiveCombat_IsProtectedBeforePhysicalStillProofMatures()
    {
        var (ctx, id) = ContextAt(100, 200, inCombat: true);
        ctx.Sense(State(sessionId: 77, stateUtc: T0, inCombat: true));
        ctx.LastProgressUtc = T0.AddSeconds(-151);
        id.StillSinceUtc = T0.AddSeconds(-30);
        ctx.LastStillObservationStateUtc = T0;
        ctx.LastStillObservationBridgeSessionId = 77;

        Assert.Equal(WedgeGate.ActiveCombat, BotBrain.ClassifyWedgeGate(ctx, T0));
    }

    [Fact]
    public void HeldEngagement_ProtectsSlowClockWhenCombatBitLags()
    {
        var (ctx, _) = ContextAt(100, 200, inCombat: false);
        ctx.LastProgressUtc = T0.AddSeconds(-151);
        ctx.HeldTask = new HeldTaskEcho(
            HeldTaskKind.Grind, TaskActivity.Engaged, 123, default, 0);

        Assert.Equal(WedgeGate.ActiveCombat, BotBrain.ClassifyWedgeGate(ctx, T0));
    }

    [Fact]
    public void HeldRecovery_IsOwnedByTaskInsteadOfSlowWedge()
    {
        var (ctx, _) = ContextAt(100, 200, inCombat: false);
        ctx.LastProgressUtc = T0.AddSeconds(-151);
        ctx.HeldTask = new HeldTaskEcho(
            HeldTaskKind.Grind, TaskActivity.Recovering, 123, default, 0);

        Assert.Equal(WedgeGate.TaskRecovery, BotBrain.ClassifyWedgeGate(ctx, T0));
    }

    [Fact]
    public void ProvenCombatStill_RemainsEligibleToAccrueRecoveryStreak()
    {
        var (ctx, id) = ContextAt(100, 200, inCombat: true);
        ctx.Sense(State(sessionId: 77, stateUtc: T0, inCombat: true));
        ctx.LastProgressUtc = T0.AddSeconds(-151);
        id.StillSinceUtc = T0.AddSeconds(-120);
        ctx.LastStillObservationStateUtc = T0;
        ctx.LastStillObservationBridgeSessionId = 77;

        Assert.Equal(WedgeGate.OutcomeStale, BotBrain.ClassifyWedgeGate(ctx, T0));
    }

    [Fact]
    public void ProtectedState_CannotMaskFastFailureLoop()
    {
        var (ctx, _) = ContextAt(100, 200, inCombat: true);
        ctx.LastProgressUtc = T0.AddSeconds(-151);
        ctx.Dead = true;
        ctx.ConsecutiveFailures = 8;

        Assert.Equal(WedgeGate.FailureLoop, BotBrain.ClassifyWedgeGate(ctx, T0));
    }

    [Fact]
    public void CombatResetGate_RequiresCapProtocolAndExpiredCooldown()
    {
        var (ctx, id) = ContextAt(100, 200, inCombat: true);
        ctx.BridgeProtocol = 6;

        id.WedgeStreak = 5;
        Assert.Equal(
            CombatStillResetGate.WedgeStreakBelowCap,
            BotBrain.ClassifyCombatStillResetGate(ctx, id, T0));

        id.WedgeStreak = 6;
        ctx.BridgeProtocol = 5;
        Assert.Equal(
            CombatStillResetGate.ProtocolTooOld,
            BotBrain.ClassifyCombatStillResetGate(ctx, id, T0));

        ctx.BridgeProtocol = 6;
        ctx.CombatStillResetCooldownUntilUtc = T0.AddMinutes(10);
        Assert.Equal(
            CombatStillResetGate.CooldownActive,
            BotBrain.ClassifyCombatStillResetGate(ctx, id, T0));

        Assert.Equal(
            CombatStillResetGate.Eligible,
            BotBrain.ClassifyCombatStillResetGate(ctx, id, T0.AddMinutes(10)));
    }

    [Fact]
    public void CombatResetCommand_CarriesMeasuredProofForCoreRevalidation()
    {
        var (ctx, id) = ContextAt(100, 200, inCombat: true);
        ctx.Pos = new Vec3(101, 201, 33);
        id.StillAnchorX = 100;
        id.StillAnchorY = 200;
        id.WedgeStreak = 7;

        BridgeCommand command = BotBrain.CreateCombatStillResetCommand(ctx, id, 157.9);

        Assert.Equal(BotBrain.CombatStillResetCommandType, command.Type);
        Assert.Equal(100f, command.Payload["anchor_x"]);
        Assert.Equal(200f, command.Payload["anchor_y"]);
        Assert.Equal(33f, command.Payload["anchor_z"]);
        Assert.Equal(1, command.Payload["anchor_map"]);
        Assert.Equal(3f, command.Payload["radius"]);
        Assert.Equal(157, command.Payload["still_seconds"]);
        Assert.Equal(7, command.Payload["wedge_streak"]);
    }

    [Fact]
    public void CombatResetAck_StillRequiresExactCorrelation()
    {
        var pending = new Outstanding
        {
            CorrelationId = 42,
            CommandType = BotBrain.CombatStillResetCommandType,
            ExpectedEvent = BotBrain.CombatStillResetAckEvent
        };

        Assert.Equal(
            WaitOutcomeMatcher.Disposition.CorrelationMismatch,
            WaitOutcomeMatcher.Classify(pending, new BotEvent
            {
                EventType = BotBrain.CombatStillResetAckEvent,
                CorrelationId = 41
            }));
        Assert.Equal(
            WaitOutcomeMatcher.Disposition.Positive,
            WaitOutcomeMatcher.Classify(pending, new BotEvent
            {
                EventType = BotBrain.CombatStillResetAckEvent,
                CorrelationId = 42
            }));
    }

    [Fact]
    public void CombatResetFailure_NegatesOnlyItsExactCorrelatedReset()
    {
        var pending = new Outstanding
        {
            CorrelationId = 42,
            CommandType = BotBrain.CombatStillResetCommandType,
            ExpectedEvent = BotBrain.CombatStillResetAckEvent
        };

        Assert.Equal(
            WaitOutcomeMatcher.Disposition.CorrelationMismatch,
            WaitOutcomeMatcher.Classify(pending, new BotEvent
            {
                EventType = "COMBAT_RESET_FAIL",
                CorrelationId = 41,
                Data = "reason=anchor_mismatch"
            }));
        Assert.Equal(
            WaitOutcomeMatcher.Disposition.Negative,
            WaitOutcomeMatcher.Classify(pending, new BotEvent
            {
                EventType = "COMBAT_RESET_FAIL",
                CorrelationId = 42,
                Data = "reason=anchor_mismatch"
            }));
        Assert.Equal(
            WaitOutcomeMatcher.Disposition.NotForPending,
            WaitOutcomeMatcher.Classify(new Outstanding
            {
                CorrelationId = 42,
                CommandType = "SET_TASK",
                ExpectedEvent = "TASK_COMPLETE"
            }, new BotEvent
            {
                EventType = "COMBAT_RESET_FAIL",
                CorrelationId = 42
            }));
    }

    [Fact]
    public void ResetAckAlone_CannotAuthorizePortWithoutNewerOutOfCombatState()
    {
        var (ctx, _) = ContextAt(100, 200, inCombat: true);
        ctx.Sense(new BotStateSnapshot
        {
            BridgeSessionId = 77,
            BridgeProtocol = 6,
            StateUtc = T0,
            MapId = 1,
            X = 100,
            Y = 200,
            Z = 30,
            InCombat = true
        });
        ctx.CombatStillResetBridgeSessionId = 77;
        ctx.CombatStillResetIssuedStateUtc = T0;
        ctx.CombatStillResetAckReceivedUtc = T0.AddSeconds(1);

        // This is the state immediately after the correlated ACK: generic ACK
        // progress cannot make the unchanged sensory sample safe to port.
        Assert.Equal(
            CombatStillPostResetGate.AwaitNewerState,
            BotBrain.ClassifyCombatStillPostResetGate(ctx));

        ctx.LastStateReceivedUtc = T0.AddSeconds(5);
        Assert.Equal(
            CombatStillPostResetGate.StillInCombat,
            BotBrain.ClassifyCombatStillPostResetGate(ctx));

        ctx.InCombat = false;
        Assert.Equal(
            CombatStillPostResetGate.SafeToEscape,
            BotBrain.ClassifyCombatStillPostResetGate(ctx));
    }

    [Fact]
    public void StateNewerThanIssueButOlderThanAck_CannotAuthorizePostResetPort()
    {
        var (ctx, _) = ContextAt(100, 200, inCombat: false);
        ctx.Sense(State(sessionId: 77, stateUtc: T0.AddSeconds(4), inCombat: false));
        ctx.CombatStillResetBridgeSessionId = 77;
        ctx.CombatStillResetIssuedStateUtc = T0;
        ctx.CombatStillResetAckReceivedUtc = T0.AddSeconds(5);

        Assert.Equal(
            CombatStillPostResetGate.AwaitNewerState,
            BotBrain.ClassifyCombatStillPostResetGate(ctx));

        ctx.LastStateReceivedUtc = T0.AddSeconds(6);
        Assert.Equal(
            CombatStillPostResetGate.SafeToEscape,
            BotBrain.ClassifyCombatStillPostResetGate(ctx));
    }

    [Fact]
    public void ActiveBotGroup_SuppressesPostResetEscape()
    {
        var (ctx, _) = ContextAt(100, 200, inCombat: false);
        ctx.Sense(State(sessionId: 77, stateUtc: T0.AddSeconds(6), inCombat: false));
        ctx.GroupId = 9;
        ctx.CombatStillResetBridgeSessionId = 77;
        ctx.CombatStillResetIssuedStateUtc = T0;
        ctx.CombatStillResetAckReceivedUtc = T0.AddSeconds(5);

        Assert.True(BotBrain.IsBotGroupOwned(ctx));
        Assert.Equal(
            CombatStillPostResetGate.ExternalOwner,
            BotBrain.ClassifyCombatStillPostResetGate(ctx));
    }

    [Fact]
    public void CombatResetAck_StampsAckBoundaryWithoutGenericProgress()
    {
        var executor = new BotExecutor(
            bridge: null!,
            safety: null!,
            logger: Microsoft.Extensions.Logging.Abstractions.NullLogger<BotExecutor>.Instance);
        var (ctx, _) = ContextAt(100, 200, inCombat: true);
        ctx.LastProgressUtc = T0;
        ctx.ConsecutiveReselects = 4;
        ctx.ConsecutiveFailures = 3;
        ctx.Pending = new Outstanding
        {
            CorrelationId = 42,
            CommandType = BotBrain.CombatStillResetCommandType,
            ExpectedEvent = BotBrain.CombatStillResetAckEvent
        };

        bool handled = executor.OnEvent(ctx, new BotEvent
        {
            EventType = BotBrain.CombatStillResetAckEvent,
            CorrelationId = 42
        });

        Assert.True(handled);
        Assert.Null(ctx.Pending);
        Assert.NotEqual(default, ctx.CombatStillResetAckReceivedUtc);
        Assert.Equal(T0, ctx.LastProgressUtc);
        Assert.Equal(4, ctx.ConsecutiveReselects);
        Assert.Equal(3, ctx.ConsecutiveFailures);
    }

    private static BotStateSnapshot State(long sessionId, DateTime stateUtc, bool inCombat)
        => new()
        {
            BridgeSessionId = sessionId,
            BridgeProtocol = 6,
            StateUtc = stateUtc,
            MapId = 1,
            X = 100,
            Y = 200,
            Z = 30,
            InCombat = inCombat
        };

    private static (BotContext Context, BotIdentity Identity) ContextAt(
        float x,
        float y,
        bool inCombat)
    {
        var identity = new BotIdentity
        {
            Guid = 14,
            Name = "Malwolf",
            Level = 28,
            Race = 2,
            Faction = "Horde"
        };
        var context = new BotContext
        {
            Guid = 14,
            Name = "Malwolf",
            Level = 28,
            Identity = identity,
            Pos = new Vec3(x, y, 30),
            MapId = 1,
            InCombat = inCombat
        };
        return (context, identity);
    }
}
