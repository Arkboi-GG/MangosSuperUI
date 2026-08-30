using MangosSuperUI.BotLogic.Brain;
using MangosSuperUI.BotLogic.Core;
using MangosSuperUI.BotLogic.Data;
using MangosSuperUI.BotLogic.Tracking;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MangosSuperUI.Tests;

[Collection(CircuitTraceFlowCollection.Name)]
public sealed class KillCreditTests
{
    [Theory]
    [InlineData(true, true, (int)KillCreditKind.Progress)]
    [InlineData(false, true, (int)KillCreditKind.Unconfirmed)]
    [InlineData(true, false, (int)KillCreditKind.TrashOrGrey)]
    [InlineData(false, false, (int)KillCreditKind.TrashOrGrey)]
    public void ClassifyKillCredit_SplitsCorpseProofFromLevelRelevance(
        bool confirmed, bool realKill, int expected)
        => Assert.Equal((KillCreditKind)expected, BotExecutor.ClassifyKillCredit(confirmed, realKill));

    [Fact]
    public void MissingConfirmedField_IsTreatedAsAConfirmedKill()
    {
        var evt = new BotEvent { EventType = "KILL", CreatureEntry = 1234 };
        Assert.True(evt.KillConfirmed);
        Assert.Equal(KillCreditKind.Progress,
            BotExecutor.ClassifyKillCredit(evt.KillConfirmed, isRealKill: true));
    }

    [Fact]
    public void UnconfirmedKill_StillStampsProgressInStageOne()
    {
        const int guid = 1_710_000_001;
        var executor = new BotExecutor(
            bridge: null!,
            safety: new ZoneSafetyMap(null!, NullLogger<ZoneSafetyMap>.Instance),
            logger: NullLogger<BotExecutor>.Instance);
        var ctx = new BotContext { Guid = guid, Level = 20, LastKillUtc = default };
        try
        {
            executor.OnEvent(ctx, new BotEvent
            {
                EventType = "KILL",
                CreatureEntry = 1234,
                KillConfirmed = false
            });

            Assert.NotEqual(default, ctx.LastKillUtc);
        }
        finally
        {
            CircuitTrace.Forget(guid);
        }
    }
}
