using MangosSuperUI.BotLogic.Core;
using MangosSuperUI.BotLogic.Data;

namespace MangosSuperUI.BotLogic.Planners;

// ============================================================================
// GoalSelector — picks the high-level goal each tick (§6 / § P3).
//
// Arbitration: "quest if there's a pickable quest, else grind." Uses the SAME
// QuestPlanner.Pickable filter PlanNext picks from, so the selector and the
// planner can never disagree (no Questing↔Idle bounce). A bot stays in Questing
// while it has a live quest; it drops to Grinding only when nothing is reachable
// (all done / deferred / blacklisted), and returns to Questing automatically when
// a deferral clears or it levels into new quests.
//
// Cost: GetAvailableQuests scans the graph per call. Fine for a small fleet;
// throttle/cache per bot if the roster grows.
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
        // Stay the course on a live quest.
        if (ctx.Goal == Goal.Questing && ctx.Quest?.Node != null)
            return Goal.Questing;

        var id = ctx.Identity;
        if (id == null || !_quests.IsLoaded)
            return Goal.Grinding;

        id.PruneExpiredDeferrals();
        return QuestPlanner.Pickable(_quests, id).Any()
            ? Goal.Questing
            : Goal.Grinding;
    }
}