using MangosSuperUI.BotLogic.Core;
using MangosSuperUI.BotLogic.Data;

namespace MangosSuperUI.BotLogic.Planners;

// ============================================================================
// GoalSelector — picks the high-level goal each tick (§6 / § P3).
//
// Arbitration: "quest if there's a pickable quest, else grind." Records WHY on
// ctx.GoalReason every tick so FleetReport can explain the decision — the
// arbitration is observable state, not a throwaway log. The reason carries the
// decisive counts: `q av=N pick=M` (av = quests available to this bot at its
// level; pick = those that pass the pick filter). av=0 ⇒ a gate (race/class/
// level/prereqs/done); av>0,pick=0 ⇒ the pick filter (kill-only — most content
// is item/GO, the later layer); pick>0 ⇒ questing.
//
// One graph scan: GetAvailableQuests once, pick derived via QuestPlanner.IsPickable.
// Fine for a small fleet; throttle/cache per bot if the roster grows.
// ============================================================================
public sealed class GoalSelector
{
    private readonly QuestGraphLoader _quests;

    public GoalSelector(QuestGraphLoader quests)
    {
        _quests = quests;
    }

    public Goal Select(BotContext ctx, BotStateSnapshot snap)
    {
        // Dead preempts everything → Maintenance (death recovery) until alive again.
        // MUST be first: a bot can die mid-quest with ctx.Quest still live, which the
        // "stay the course" line below would otherwise hold (→ stuck-dead, the bug
        // this fixes). MaintenancePlanner drives RESURRECT; the bot leaves Maintenance
        // automatically when STATE next reports isDead=false.
        if (ctx.Dead)
        {
            ctx.GoalReason = "dead";
            return Goal.Maintenance;
        }

        // Stay the course on a live quest.
        if (ctx.Goal == Goal.Questing && ctx.Quest?.Node != null)
        {
            ctx.GoalReason = "in-quest";
            return Goal.Questing;
        }

        var id = ctx.Identity;
        if (id == null) { ctx.GoalReason = "no-identity"; return Goal.Grinding; }
        if (!_quests.IsLoaded) { ctx.GoalReason = "graph-loading"; return Goal.Grinding; }

        id.PruneExpiredDeferrals();

        int raceBit = QuestGraphLoader.RaceToBitmask(id.Race);
        int classBit = QuestGraphLoader.ClassToBitmask(id.ClassId);
        var avail = _quests.GetAvailableQuests(raceBit, classBit, id.Level, id.CompletedQuestIds);

        // Range gate (OPEN #1) — same map + within the level/zone cap. Counted with the
        // SAME InReach + cap PickFor uses, so the arbitration matches the pick (no bounce).
        float cap = ZoneSafetyMap.GetMaxTravelDistance(id.Level, ctx.ZoneId, 0);
        int pick = avail.Count(q => QuestPlanner.IsPickable(q, id)
                                    && QuestPlanner.InReach(q, ctx.Pos.X, ctx.Pos.Y, ctx.MapId, cap));

        ctx.GoalReason = $"q av={avail.Count} pick={pick}";
        return pick > 0 ? Goal.Questing : Goal.Grinding;
    }
}