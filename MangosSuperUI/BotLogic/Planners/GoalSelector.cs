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

    // Crater this and the bot breaks for a vendor (mirrors MaintenancePlanner's gate).
    private const int DurabilityVendorThreshold = 30;

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

        // Post-rez heal hold: the bot is ALIVE again but MaintenancePlanner has not yet
        // healed it to full. Stay in Maintenance so it does NOT resume questing/grinding
        // at 50% HP — re-engaging low is the death spiral the heal phase exists to break.
        // Cleared the moment the planner marks HealDone and returns Complete() (→ Idle →
        // normal reselect next tick, now at ~full HP). Reads the carried scratch, which is
        // still present because the goal hasn't changed off Maintenance through the rez.
        if (ctx.Goal == Goal.Maintenance && ctx.Maintenance is { RezSent: true, HealDone: false })
        {
            ctx.GoalReason = "healing";
            return Goal.Maintenance;
        }

        // Vendor/repair errand hold — keep the bot in Maintenance while a vendor trip is
        // in flight (ctx.Service), exactly as the heal-hold pins it post-rez. Without this
        // the goal would flip on the next Select mid-trip and ResetScratch would wipe the
        // errand. Cleared when MaintenancePlanner nulls ctx.Service (done / give-up).
        if (ctx.Goal == Goal.Maintenance && ctx.Service is { Phase: not VendorPhase.None })
        {
            ctx.GoalReason = "vendor";
            return Goal.Maintenance;
        }

        // Self-maintenance trigger — cratered durability (gear about to break) or no free
        // bag slots (can't loot) routes the bot to a vendor via MaintenancePlanner's vendor
        // branch. Cooldown-gated (set on give-up / completion) so a borderline reading can't
        // thrash; a repair restores durability to full and selling frees slots, so it self-
        // clears. Sits ahead of "stay the course" so a low-durability emergency preempts a
        // quest (its leg is resumed from the log afterward).
        if (!(ctx.Identity?.VendorCooldownUntil is DateTime vc && DateTime.UtcNow < vc)
            && (ctx.Durability < DurabilityVendorThreshold || ctx.FreeSlots <= 0))
        {
            ctx.GoalReason = ctx.Durability < DurabilityVendorThreshold ? "repair" : "bags-full";
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

        // Range gate (OPEN #1) — same map + within the level/zone cap, with a WIDENING scan:
        // if nothing is pickable at the baseline radius, escalate the reach tier so a bot that
        // has drained the local hub heads for the next level-appropriate hub instead of grinding
        // in place. Computed off the SAME QuestPlanner.ReachTier PickFor seeds from, so the goal
        // and the pick never disagree (shared-filter invariant). Tier 0 keeps the old
        // `q av=N pick=M` reason byte-identical; a widened pick appends `reach=tN`. pick=0 with
        // no reach tag = nothing kill-only pickable even widened (the item/GO content ceiling).
        var pickable = avail.Where(q => QuestPlanner.IsPickable(q, id)).ToList();
        int tier = QuestPlanner.ReachTier(pickable, id, ctx.Pos.X, ctx.Pos.Y, ctx.MapId, ctx.ZoneId);
        float cap = ZoneSafetyMap.GetMaxTravelDistance(id.Level, ctx.ZoneId, tier < 0 ? 0 : tier);
        int pick = tier < 0 ? 0
                            : pickable.Count(q => QuestPlanner.InReach(q, ctx.Pos.X, ctx.Pos.Y, ctx.MapId, cap));

        ctx.GoalReason = tier > 0 ? $"q av={avail.Count} pick={pick} reach=t{tier}"
                                  : $"q av={avail.Count} pick={pick}";
        return pick > 0 ? Goal.Questing : Goal.Grinding;
    }
}