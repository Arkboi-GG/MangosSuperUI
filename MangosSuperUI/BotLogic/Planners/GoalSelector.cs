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

    // Minimum copper before a training trip. The REAL affordability gate is in C++ (TRAIN_AT_NPC
    // only buys ranks the bot can pay for), and what it can't afford waits for the next LEVEL_UP's
    // gold — so this only needs to avoid a pointless trek for a stone-broke bot far from a trainer.
    // Kept at 0 for now: low-level bots run single-digit copper, the starting trainer is steps away,
    // and untrained is why they can't kill — so always attempt when flagged. Raise it later only if
    // far-from-trainer high-level bots start making wasted trips.
    private const long TrainGoldFloor = 0;

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

        // Teleport-assist round-trip in flight — hold the CURRENT goal so nothing preempts the short
        // hop-in / do-business / hop-back. Set by a planner (Training / Maintenance) when a final
        // approach to a service NPC no_paths in the vicinity. Sits AFTER dead/heal (which null
        // ctx.Teleport on their preempting goal change) and AHEAD of every trigger below, so a
        // durability emergency / grind-lock can't yank the bot mid-warp — it fires next tick once the
        // round-trip releases. ctx.Teleport is only ever set while ctx.Goal is the planner's goal.
        if (ctx.Teleport != null)
        {
            ctx.GoalReason = $"teleport:{ctx.Teleport.Phase}";
            return ctx.Goal;
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

        // Training errand hold — keep the bot in Training while a trainer trip is in flight
        // (ctx.Train), exactly as the vendor hold pins it. Without this the goal would flip on the
        // next Select mid-trip and ResetScratch would wipe the errand. Cleared when TrainingPlanner
        // nulls ctx.Train (done / give-up).
        if (ctx.Goal == Goal.Training && ctx.Train != null)
        {
            ctx.GoalReason = "training";
            return Goal.Training;
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

        // Group execution directive (grouping §3) — the god bot stamped this bot's group a phase this
        // tick (travel / accept / shared objective / turn-in / hold / group-train). Work it as a TEAM
        // (Questing → QuestPlanner.DriveGroup executes the stamped phase for this bot; the combat
        // directive focus-fires the shared mob). Sits AFTER the survival hard-needs (dead / heal /
        // teleport / vendor) so a dying or broke-gear member still peels, and BEFORE solo training /
        // grind-lock / questing so a GROUPED bot never wanders off to train or grind ALONE while the
        // team has work.
        //
        // ONE exception: GroupPhase.GroupTrain (§4). That phase means the coordinator has ALREADY
        // authorized a group-gated training round (every present member cleared TrainBaselineLevel+2)
        // — so a member that itself still owes a trainer visit (HasUnlearnedSpells) is let PAST this
        // gate to reach the training trigger below, instead of being held on Questing. A member with
        // nothing new to learn is NOT exempted — it falls into the group branch same as any other
        // phase and stays with the team (grinding the latched objective if one's embedded) until every
        // trainee returns. This is also why a grouped bot no longer solo-trains at spawn or mid-fight:
        // outside GroupTrain, the group phase preempts the training trigger unconditionally.
        bool groupTrainWindow = ctx.GroupOrder.Phase == GroupPhase.GroupTrain
                                 && ctx.Identity is { HasUnlearnedSpells: true };
        if (ctx.GroupOrder.IsActive && !groupTrainWindow)
        {
            ctx.GoalReason = $"group:{ctx.GroupOrder.Phase}";
            return Goal.Questing;
        }

        // Training trigger — the bot has unlearned class spells AND enough gold to buy something.
        // A broke bot does NOT trek (it'd learn nothing): it keeps questing/grinding to earn, and
        // trains once it can afford it / after the next LEVEL_UP re-flags new spells. Cooldown-gated
        // so an unreachable-trainer / TRAIN_FAIL trip doesn't immediately re-fire. Sits AFTER survival
        // (dead/heal/vendor) and BEFORE grind-lock + questing, so spells are learned before the bot
        // commits to more fighting (the whole point — spell-starved bots can't kill). For a GROUPED
        // bot this only fires when groupTrainWindow just let it through — a solo bot is unrestricted.
        if (ctx.Identity is { HasUnlearnedSpells: true } tid
            && !(tid.TrainCooldownUntil is DateTime tcd && DateTime.UtcNow < tcd)
            && ctx.Copper >= TrainGoldFloor)
        {
            ctx.GoalReason = "train";
            return Goal.Training;
        }

        // Wedge backoff: the no-progress breaker parked this bot (no real progress / fast fail-loop /
        // off-mesh). Sit Idle until it lapses, then resume — it relocates to a fresh cell. Sits AFTER the
        // dead/heal/vendor recovery holds above (recovery still preempts a parked bot) and ahead of the
        // rest. Expires by clock.
        if (ctx.Identity?.WedgeBackoffUntil is DateTime wb && DateTime.UtcNow < wb)
        {
            ctx.GoalReason = $"wedge-backoff {(int)Math.Ceiling((wb - DateTime.UtcNow).TotalSeconds)}s";
            return Goal.Idle;
        }

        // Grind-lock: questing has shelved its way out of all in-reach content (everything
        // currently deferred), so the bot COMMITS to grinding for a window to gain levels rather
        // than oscillating quest⇄grind at tick speed. Sits AFTER the dead/heal/vendor holds above
        // (recovery still preempts a locked grind) and AHEAD of "stay the course" so it overrides
        // a stale live quest. Set by QuestPlanner on a deferral-driven batch exhaust; expires by
        // clock (level-ups do NOT cut it short — the bot earns its hour of XP).
        if (ctx.Identity?.GrindLockUntil is DateTime gl && DateTime.UtcNow < gl)
        {
            ctx.GoalReason = $"grind-lock {(int)Math.Ceiling((gl - DateTime.UtcNow).TotalMinutes)}m";
            return Goal.Grinding;
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