using MangosSuperUI.BotLogic.Brain;
using MangosSuperUI.BotLogic.Core;
using MangosSuperUI.BotLogic.Data;
using MangosSuperUI.BotLogic.Planners;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MangosSuperUI.Tests;

public sealed class AutonomousMoveRefusalTests
{
    [Fact]
    public void ValidRefusal_IsTransientAndCannotTouchPendingOrDurableFailures()
    {
        var executor = Executor();
        var identity = new BotIdentity { Guid = 14, Name = "Malwolf", IslandStreak = 4 };
        identity.RecordNoPath(1, 15f, 25f);
        identity.RecordNoPath(1, 15f, 25f);
        var pending = Pending(77);
        var noWaitOwner = new NoWaitCommandOwner
        {
            CorrelationId = 76,
            CommandType = "SET_TASK",
            OwnsTaskMotion = true,
            CanGrindBlock = true
        };
        DateTime priorProgress = DateTime.UtcNow - TimeSpan.FromMinutes(1);
        var failure = new WaitFailure
        {
            CommandType = "MOVE_TO",
            Reason = "existing_failure",
            Utc = DateTime.UtcNow
        };
        var context = new BotContext
        {
            Guid = 14,
            Identity = identity,
            MapId = 1,
            Pos = new Vec3(10f, 20f, 5f),
            Pending = pending,
            Failure = failure,
            ConsecutiveFailures = 7,
            LastProgressUtc = priorProgress
        };
        context.LatestNoWaitCommand = noWaitOwner;
        context.NoWaitTaskOwner = noWaitOwner;

        bool handled = executor.OnEvent(context, Refusal(cbt: null, pointId: 102));

        Assert.True(handled);
        Assert.Same(pending, context.Pending);
        Assert.Same(failure, context.Failure);
        Assert.Same(noWaitOwner, context.LatestNoWaitCommand);
        Assert.Same(noWaitOwner, context.NoWaitTaskOwner);
        Assert.Equal(7, context.ConsecutiveFailures);
        Assert.Equal(priorProgress, context.LastProgressUtc);
        Assert.Equal(2, identity.GetNoPathStreak(1, 15f, 25f));
        Assert.Equal(4, identity.IslandStreak);
        Assert.Equal(1, context.AutonomousMoveRefusalStreak);
        Assert.Equal(102, context.AutonomousMoveRefusalPointId);
    }

    [Fact]
    public void Refusal_RejectsNonzeroCorrelationAndMalformedContracts()
    {
        var executor = Executor();
        var pending = Pending(88);
        var failure = new WaitFailure { CommandType = "MOVE_TO", Reason = "keep_me" };
        var context = new BotContext
        {
            Guid = 14,
            MapId = 1,
            Pos = new Vec3(10f, 20f, 5f),
            Pending = pending,
            Failure = failure
        };
        BotEvent[] rejected =
        {
            Refusal(cbt: 9, pointId: 102),
            Refusal(cbt: null, pointId: 102, data: "reason=blocked|source=move_point|point_id=102|dest_x=15|dest_y=25|dest_z=5"),
            Refusal(cbt: null, pointId: 102, data: "reason=no_path|source=set_task_approach|point_id=102|dest_x=15|dest_y=25|dest_z=5"),
            Refusal(cbt: null, pointId: 101),
            Refusal(cbt: null, pointId: 102, data: "reason=no_path|source=move_point|point_id=102|dest_x=NaN|dest_y=25|dest_z=5"),
            Refusal(cbt: null, pointId: 102, data: "reason=no_path|source=move_point|point_id=102|dest_x=15|dest_y=25"),
            Refusal(cbt: null, pointId: 102, data: "reason=no_path|Reason=no_path|source=move_point|point_id=102|dest_x=15|dest_y=25|dest_z=5")
        };

        foreach (BotEvent evt in rejected)
            Assert.False(executor.OnEvent(context, evt));

        Assert.Same(pending, context.Pending);
        Assert.Same(failure, context.Failure);
        Assert.Equal(0, context.AutonomousMoveRefusalStreak);
    }

    [Fact]
    public void RefusalChain_IsBoundedAndClearsOnProgressMovementOrSessionReplacement()
    {
        var executor = Executor();
        var context = new BotContext { Guid = 14 };
        context.Sense(State(sessionId: 10, x: 10f, y: 20f));

        for (int i = 0; i < 12; i++)
            Assert.True(executor.OnEvent(context, Refusal(cbt: 0, pointId: 102)));
        Assert.Equal(8, context.AutonomousMoveRefusalStreak);

        context.MarkProgress();
        Assert.Equal(0, context.AutonomousMoveRefusalStreak);

        Assert.True(executor.OnEvent(context, Refusal(cbt: 0, pointId: 102)));
        context.Sense(State(sessionId: 10, x: 19f, y: 20f));
        Assert.Equal(0, context.AutonomousMoveRefusalStreak);

        Assert.True(executor.OnEvent(context, Refusal(cbt: 0, pointId: 102)));
        context.Sense(State(sessionId: 10, x: 19f, y: 20f, z: 14f));
        Assert.Equal(0, context.AutonomousMoveRefusalStreak);

        Assert.True(executor.OnEvent(context, Refusal(cbt: 0, pointId: 102)));
        context.Sense(State(sessionId: 11, x: 19f, y: 20f, z: 14f));
        Assert.Equal(0, context.AutonomousMoveRefusalStreak);
    }

    [Fact]
    public void RefusalChain_RestartsForAnotherPointTypeOrAfterRecencyWindow()
    {
        var context = new BotContext
        {
            Guid = 14,
            MapId = 1,
            Pos = new Vec3(10f, 20f, 5f)
        };
        DateTime first = DateTime.UtcNow - TimeSpan.FromMinutes(2);

        context.RecordAutonomousMoveRefusal(102, new Vec3(15f, 25f, 5f), first);
        context.RecordAutonomousMoveRefusal(102, new Vec3(16f, 26f, 5f), first + TimeSpan.FromSeconds(10));
        Assert.Equal(2, context.AutonomousMoveRefusalStreak);

        context.RecordAutonomousMoveRefusal(104, new Vec3(11f, 21f, 5f), first + TimeSpan.FromSeconds(20));
        Assert.Equal(1, context.AutonomousMoveRefusalStreak);
        Assert.Equal(104, context.AutonomousMoveRefusalPointId);

        context.RecordAutonomousMoveRefusal(104, new Vec3(12f, 22f, 5f), first + TimeSpan.FromSeconds(96));
        Assert.Equal(1, context.AutonomousMoveRefusalStreak);
    }

    [Fact]
    public void ThreeRecentGrindPatrolRefusals_EnterExistingBarrenRecoveryBeforeGrace()
    {
        var safety = new ZoneSafetyMap(null!, NullLogger<ZoneSafetyMap>.Instance);
        var planner = new GrindPlanner(NullLogger<GrindPlanner>.Instance, safety);
        var context = ArmedGrindContext();
        var executor = Executor();
        Assert.True(executor.OnEvent(context, Refusal(cbt: 0, pointId: 102)));
        Assert.True(executor.OnEvent(context, Refusal(cbt: 0, pointId: 102)));
        Assert.True(executor.OnEvent(context, Refusal(cbt: 0, pointId: 102)));

        Assert.True(context.TimeInGoalSec < 45);
        var blocked = Assert.IsType<StepResult.Blocked>(planner.PlanNext(context, State(0, 10f, 20f)));

        Assert.Equal("grind:no-global-hub", blocked.Reason);
        Assert.Null(context.Grind);
        Assert.True(context.IsDeadGrindCell(10f, 20f));
    }

    [Fact]
    public void StaleGrindPatrolRefusalCluster_DoesNotTriggerBarrenRecovery()
    {
        var safety = new ZoneSafetyMap(null!, NullLogger<ZoneSafetyMap>.Instance);
        var planner = new GrindPlanner(NullLogger<GrindPlanner>.Instance, safety);
        var context = ArmedGrindContext();
        DateTime now = DateTime.UtcNow;
        context.RecordAutonomousMoveRefusal(102, new Vec3(15f, 25f, 5f), now - TimeSpan.FromSeconds(100));
        context.RecordAutonomousMoveRefusal(102, new Vec3(16f, 26f, 5f), now - TimeSpan.FromSeconds(90));
        context.RecordAutonomousMoveRefusal(102, new Vec3(17f, 27f, 5f), now - TimeSpan.FromSeconds(80));

        StepResult result = planner.PlanNext(context, State(0, 10f, 20f));

        Assert.IsType<StepResult.Continue>(result);
        Assert.NotNull(context.Grind);
        Assert.False(context.HasRecentAutonomousMoveRefusals(102, 3, DateTime.UtcNow));
    }

    [Theory]
    [InlineData(104, false)] // combat-stalemate nudge: evidence belongs to the reset handshake
    [InlineData(102, true)]  // even patrol evidence cannot trigger a port while combat is live
    public void CombatRefusalEvidence_DoesNotBecomeAGrindBarrenVerdict(int pointId, bool inCombat)
    {
        var safety = new ZoneSafetyMap(null!, NullLogger<ZoneSafetyMap>.Instance);
        var planner = new GrindPlanner(NullLogger<GrindPlanner>.Instance, safety);
        var context = ArmedGrindContext();
        context.InCombat = inCombat;
        DateTime now = DateTime.UtcNow;
        context.RecordAutonomousMoveRefusal(pointId, new Vec3(15f, 25f, 5f), now - TimeSpan.FromSeconds(20));
        context.RecordAutonomousMoveRefusal(pointId, new Vec3(16f, 26f, 5f), now - TimeSpan.FromSeconds(10));
        context.RecordAutonomousMoveRefusal(pointId, new Vec3(17f, 27f, 5f), now);

        StepResult result = planner.PlanNext(context, State(0, 10f, 20f));

        Assert.IsType<StepResult.Continue>(result);
        Assert.NotNull(context.Grind);
        Assert.Equal(3, context.AutonomousMoveRefusalStreak);
    }

    private static BotContext ArmedGrindContext()
    {
        var context = new BotContext
        {
            Guid = 14,
            Name = "Malwolf",
            Level = 14,
            MapId = 1,
            Pos = new Vec3(10f, 20f, 5f),
            LastKillUtc = DateTime.UtcNow,
            Identity = new BotIdentity
            {
                Guid = 14,
                Name = "Malwolf",
                Level = 14,
                Faction = "Horde"
            },
            Grind = new GrindScratch
            {
                CreatureEntry = 0,
                AreaCenter = new Vec4(10f, 20f, 5f, 1),
                Radius = 60f,
                KillGoal = 0
            }
        };
        context.SetGoal(Goal.Grinding, "grind");
        return context;
    }

    private static BotExecutor Executor()
        => new(null!, null!, NullLogger<BotExecutor>.Instance);

    private static Outstanding Pending(long cbt)
        => new()
        {
            CorrelationId = cbt,
            CommandType = "MOVE_TO",
            ExpectedEvent = "TASK_COMPLETE",
            SentUtc = DateTime.UtcNow,
            DeadlineUtc = DateTime.UtcNow + TimeSpan.FromMinutes(1)
        };

    private static BotEvent Refusal(long? cbt, int pointId, string? data = null)
        => new()
        {
            EventType = "MOVE_POINT_REFUSED",
            CorrelationId = cbt,
            Data = data
                ?? $"reason=no_path|source=move_point|point_id={pointId}|dest_x=15|dest_y=25|dest_z=5"
        };

    private static BotStateSnapshot State(long sessionId, float x, float y, float z = 5f)
        => new()
        {
            BridgeSessionId = sessionId,
            BridgeProtocol = 5,
            MapId = 1,
            X = x,
            Y = y,
            Z = z,
            Level = 14,
            Health = 100,
            MaxHealth = 100,
            StateUtc = DateTime.UtcNow
        };
}
