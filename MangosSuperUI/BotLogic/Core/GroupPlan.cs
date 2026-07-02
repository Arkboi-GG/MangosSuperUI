namespace MangosSuperUI.BotLogic.Core;

// ============================================================================
// GroupPlan — the locked group-coordinator contract (AIBOT_GROUPING_DESIGN §7.1).
//
// Step 1 of the central-driver build. Defines the three types the "god bot"
// coordinator and its thin phase executor share:
//
//   • GroupPhase  — the §3 phase machine's states. ONE loop the whole group runs
//                   in lockstep: Forming → TravelToGiver → Accept → Objective →
//                   TravelToTurnIn → TurnIn, with GroupTrain / GroupVendor /
//                   HoldAtAnchor as the errand / peel branches.
//   • GroupOrder  — the PER-MEMBER stamp (mirrors CombatDirective / ExecDirective:
//                   a readonly record struct, structural value-equality for the
//                   change-guard). It GENERALIZES the old kill-only ExecDirective to
//                   carry the group PHASE + the TARGET NPC (giver / ender / trainer /
//                   vendor for the travel + interact phases) + the embedded kill
//                   OBJECTIVE (the shared mob, active only in the Objective /
//                   HoldAtAnchor phases). ExecDirective is KEPT, unchanged, as that
//                   embedded payload — it still defines the enriched-grind MOVE_TO.
//   • GroupPlan   — the PER-GROUP held state the coordinator mutates each tick (the
//                   union pool / phase / cursor / latched objective / train baseline).
//                   Lives as a TRANSIENT field on BotGroup (added in step 3); dies on
//                   disband; NEVER persisted.
//
// No leader, no follower (§0 / §1). The coordinator STAMPS each member a GroupOrder;
// the spine (BotBrain) alone turns intent into a wire command. Exactly one decision
// layer — the "second live decider" that deadlocked every prior attempt never exists
// (§1 / §8).
//
// Lives in the Core namespace alongside BotContext, so it sees Vec4 and the embedded
// ExecDirective with no using (ExecDirective stays DEFINED in BotContext.cs; this
// file only references it).
// ============================================================================

// ---------------------------- GroupPhase (§3) ------------------------------
// The §3 phase table, in order. None = no group order this tick (solo / ungrouped /
// sub-2 present members) — the member falls back to its own planner, exactly as
// ExecMode.None did. Every gate that ADVANCES these phases is a LIVE POLL over member
// ground-truth with a timeout + a liveness escape (§3 / §6) — never a stored boolean
// (a miscounted stored flag is what froze the old leader; §8). The coordinator owns
// the transitions; the executor (QuestPlanner.DriveGroup) only READS the stamped phase.
public enum GroupPhase
{
    None = 0,

    // (re)build the union pool, elect the anchor, pick the first target. Transient:
    // resolves to a concrete phase the same tick.
    Forming = 1,

    // The whole group travels to the current giver together. Gate: every present, live
    // member is within reach of the giver.
    TravelToGiver = 2,

    // Each member, ON ITS OWN ARRIVAL in range, accepts its eligible subset (§1 breadth,
    // §2 accept-sync). Out-of-range members are still travelling and are WAITED FOR. Gate:
    // no present, eligible member still owes an in-range accept (the no-bot-left-behind
    // rule, §2).
    Accept = 3,

    // Every member runs the SAME enriched grind at the shared mob; the combat directive
    // focus-fires it. LATCHED (held on GroupPlan, not re-sampled per tick). Gate: no
    // present, LIVE holder still owes kills (eligibility-gated; the liveness escape drops
    // a frozen holder, §6).
    Objective = 4,

    // The whole group travels to the ender together. Gate: all present, live members at
    // the ender.
    TravelToTurnIn = 5,

    // Each member turns in the pool quests it holds at server-COMPLETE. Gate: no present
    // member still holds an unturned, complete pool quest.
    TurnIn = 6,

    // The group-gated training window (§4): every present member must reach
    // TrainBaselineLevel + 2 AND at least one still owes a trainer visit (HasUnlearnedSpells).
    // A member with something to learn peels via its OWN TrainingPlanner trip to its OWN class
    // trainer (GoalSelector authorizes the individual trigger only during this phase); a member
    // with nothing new grinds the embedded latched objective in place, if any, until every
    // trainee returns. No single shared NPC target -- classes need different trainers. This
    // also structurally ends the L1 bum-rush -- training is a group-gated event, not a per-bot
    // spawn reflex.
    GroupTrain = 7,

    // The whole group routes to one shared vendor / repair stop together; each needing
    // member sells / repairs. Trigger = any present member over the durability / bags
    // threshold (§3 / §4). Maintenance is NEVER a solo peel.
    GroupVendor = 8,

    // A member peeled to its own recovery (Maintenance — the survival hard-needs always
    // win, §4). The REST hold ON THE SAME TARGET at the anchor, still gaining XP / loot,
    // until the peeled member returns OR the liveness escape fires (then press on).
    HoldAtAnchor = 9
}

// ---------------------------- GroupOrder (§7.1) ----------------------------
// The per-member stamp the coordinator writes to BotContext.GroupOrder each tick (with
// BotContext.LastGroupOrder as the value-equality change-guard for the no-WAIT group
// grind, exactly as LastGroupExec guarded the old ExecDirective). A readonly record
// struct so equality is structural and free — assign a new value to re-stamp, compare to
// detect a change; never mutate in place (mirrors CombatDirective / ExecDirective).
//
// Carries, per the §7.1 generalization:
//   • Phase          — which §3 phase the whole group is in this tick.
//   • AnchorGuid     — the elected anchor (§5.3): stable across phases; whose live victim
//                      the team focus-fires and the "nearest" origin for objective
//                      selection. Set in Forming, held through the loop.
//   • TargetNpcEntry — the giver / ender / trainer / vendor the group converges on for the
//     / TargetPos      travel + interact + errand phases (TravelToGiver, Accept,
//                      TravelToTurnIn, TurnIn, GroupTrain, GroupVendor). 0 / default when
//                      the phase has no NPC target.
//   • Objective      — the embedded ExecDirective: the shared kill mob (creature + enriched-
//                      grind coords + the holder's quest+slot to read its OWN remaining
//                      count). Active ONLY in the Objective and HoldAtAnchor phases;
//                      ExecDirective.None otherwise.
//
// IsActive => Phase != None. NOTE the semantic shift from the old stamp: a GroupOrder is
// ACTIVE through the travel / accept / turn-in phases that carry NO kill objective — so
// GoalSelector routes a grouped member to Goal.Questing whenever Phase != None, not only
// when a kill mob is stamped (§7.5). HasObjective is the narrower "there is a mob to grind".
public readonly record struct GroupOrder
{
    public GroupPhase Phase { get; }
    public int AnchorGuid { get; }
    public int TargetNpcEntry { get; }
    public Vec4 TargetPos { get; }
    public ExecDirective Objective { get; }

    public GroupOrder(GroupPhase phase, int anchorGuid, int targetNpcEntry,
        Vec4 targetPos, ExecDirective objective)
    {
        Phase = phase;
        AnchorGuid = anchorGuid;
        TargetNpcEntry = targetNpcEntry;
        TargetPos = targetPos;
        Objective = objective;
    }

    /// <summary>A group order is stamped this tick (mirrors ExecDirective.IsActive, but
    /// keyed on the PHASE — active across the no-objective travel / accept / turn-in phases
    /// too, so a grouped member never falls back to its solo planner mid-loop).</summary>
    public bool IsActive => Phase != GroupPhase.None;

    /// <summary>True only while the group is grinding the shared mob (the Objective phase or
    /// the HoldAtAnchor hold) — i.e. there IS a live kill objective to drive the enriched
    /// grind / focus-fire. Lets the executor branch grind-vs-travel without re-deriving.</summary>
    public bool HasObjective => Objective.IsActive;

    /// <summary>The cleared stamp — solo / ungrouped / sub-2 (mirrors ExecDirective.None /
    /// CombatDirective.None). Emitted by the coordinator's default pass over every context.</summary>
    public static readonly GroupOrder None =
        new(GroupPhase.None, 0, 0, default, ExecDirective.None);

    // ---- per-phase factories (the coordinator stamps one of these on each member) ----

    /// <summary>Forming: anchor elected, pool (re)building. Resolves to a concrete phase the
    /// same tick; stamped so a mid-form tick still reads as an active order, not a solo gap.</summary>
    public static GroupOrder Forming(int anchorGuid)
        => new(GroupPhase.Forming, anchorGuid, 0, default, ExecDirective.None);

    /// <summary>A travel-or-interact phase keyed to an NPC the whole group converges on:
    /// TravelToGiver / Accept / TravelToTurnIn / TurnIn / GroupTrain / GroupVendor. The Phase
    /// tells the executor WHAT to do on arrival (move / accept / turn in / train / vendor);
    /// TargetNpcEntry + TargetPos tell it WHERE. (Train/Vendor are single §3 phases whose
    /// move→interact sub-steps the executor drives via ctx.Step, like TrainingPlanner.)</summary>
    public static GroupOrder ToNpc(GroupPhase phase, int anchorGuid, int npcEntry, Vec4 npcPos)
        => new(phase, anchorGuid, npcEntry, npcPos, ExecDirective.None);

    /// <summary>Objective: the whole group grinds the shared mob together (latched on
    /// GroupPlan). The embedded ExecDirective is the enriched-grind MOVE_TO target the
    /// member fires when in range; the combat directive focus-fires the anchor's victim.</summary>
    public static GroupOrder Engage(int anchorGuid, ExecDirective objective)
        => new(GroupPhase.Objective, anchorGuid, 0, default, objective);

    /// <summary>HoldAtAnchor: a member peeled to recover; the REST keep killing the SAME
    /// objective at the anchor (§4). Carries the latched objective AND the anchor hold-pos so
    /// the executor keeps the held members on the mob at the formation point.</summary>
    public static GroupOrder Hold(int anchorGuid, ExecDirective objective, Vec4 anchorPos)
        => new(GroupPhase.HoldAtAnchor, anchorGuid, 0, anchorPos, objective);

    /// <summary>GroupTrain: the group-gated training window is open (§4). No NPC target -- each
    /// trainee routes to its OWN class trainer via its own TrainingPlanner, never a single shared
    /// NPC (classes differ). Carries the latched objective (if any) so a member with nothing new
    /// to learn keeps grinding it in place instead of standing idle while trainees are away.</summary>
    public static GroupOrder Train(int anchorGuid, ExecDirective objective)
        => new(GroupPhase.GroupTrain, anchorGuid, 0, default, objective);
}

// ----------------------------- GroupPlan (§7) ------------------------------
// The per-group held state the coordinator mutates each tick. Lives as a TRANSIENT field
// on BotGroup (added when step 3 wires the coordinator); dies on disband; NEVER persisted
// (SaveGroupsToDbAsync writes its 5 fixed columns and ignores this). The coordinator stays
// a STATIC pre-pass that MUTATES group.Plan — so the group state is here, not in a second
// decider, and there is still exactly one decision layer (§7 / §8).
public sealed class GroupPlan
{
    /// <summary>The persistent "virtual member" (§Option A, 2026-07-01). A synthetic BotContext the
    /// coordinator drives through the REAL QuestPlanner.PlanNext -- Derive, BuildBatch, GatherLocals,
    /// PriorityLeg, TagOutliers, Recover, all of it -- instead of a hand-rolled parallel
    /// reimplementation (Forming/NextGiver/NextObjective/NextEnder are RETIRED; this replaces them).
    /// Never has a real bridge connection: its sensory state is refreshed each tick from the union of
    /// present real members (GroupCoordinator.RefreshVirtualSensory), and GroupOrder is NEVER set on
    /// it, which is what keeps QuestPlanner.PlanNext routing it through the solo decision path rather
    /// than recursing into the group executor. Lazily created; survives ResetForForming (the virtual
    /// bot's own accrued state -- deferrals, overflow-grind attempt counts, the in-flight leg -- is
    /// exactly as durable as a real bot's own BotIdentity/QuestScratch and must not be wiped just
    /// because the pool needs a fresh union pass).</summary>
    public BotContext? Virtual { get; set; }

    /// <summary>The raw command the virtual bot's last StepResult.Issue carried (§Option A). Outstanding
    /// (ctx.Pending) doesn't retain the payload, so this is where BuildGroupOrderFromVirtual reads the
    /// enriched-objective leg's x/y/z/creature_entry back out -- set by ArmVirtualPending, read once per
    /// translation, never needs to survive longer than that.</summary>
    public BridgeCommand? LastVirtualCommand { get; set; }

    /// <summary>The union pool: every pool quest id the group is working this cluster (the
    /// union across present members' eligible pickups, sized by §6 min-headroom — the
    /// tightest member's free quest-log slots, NOT BatchCap). Rebuilt in Forming; the
    /// cursor walks it one task at a time.</summary>
    public List<int> Pool { get; } = new();

    /// <summary>The phase the whole group is in (the §3 machine). The coordinator advances
    /// it on a live-poll gate; every member is stamped this same phase.</summary>
    public GroupPhase Phase { get; private set; } = GroupPhase.None;

    /// <summary>Cursor into the ordered worklist — which pool task (giver → accept →
    /// objective → turn-in) the group is on. "One task at a time, together" (§1). Advanced
    /// only when the current task's turn-in gate clears.</summary>
    public int Cursor { get; set; }

    /// <summary>The §3 LATCHED objective — the shared kill mob held ACROSS ticks (not
    /// re-sampled every tick, which would thrash the focus-fire target). Set when the
    /// Objective phase is entered; cleared when its completion gate clears. Stamped onto
    /// each member as GroupOrder.Objective.</summary>
    public ExecDirective LatchedObjective { get; set; } = ExecDirective.None;

    /// <summary>The GroupTrain cadence baseline (§4): the level all present members must
    /// reach +2 before the next group-train fires. Default 0 means "never seeded" (real levels
    /// start at 1) -- GroupCoordinator.DriveGroup lazy-seeds it to the current min present member
    /// level the FIRST time it's ever evaluated, so a fresh or freshly-squadded party's clock
    /// starts from wherever they actually are, not from zero (else everyone clearing "0 + 2" on
    /// their very first level-up would fire an immediate training round -- the same per-bot
    /// bum-rush this phase exists to prevent). After that, set to the MIN present member level
    /// the moment each round OPENS -- whether it actually sends anyone to a trainer (someone owed
    /// a visit) or just resets the clock (nobody did) -- so a mid-round level-up can't
    /// retroactively raise the bar for members already en route. The trigger is "every present
    /// member Level >= this + 2", so training is a whole-group, every-2-levels event — never a
    /// per-bot spawn reflex.</summary>
    public int TrainBaselineLevel { get; set; }

    /// <summary>When the current Phase was entered (UTC). Drives the §2 / §3 phase TIMEOUT
    /// half of every gate (the bounded hold): the live-poll answers "are we done yet"; this
    /// bounds "how long do we wait for a straggler before the liveness escape advances us
    /// anyway". Stamped by SetPhase on every phase change.</summary>
    public DateTime PhaseSinceUtc { get; private set; } = DateTime.UtcNow;

    /// <summary>Seconds the group has been in the current phase (the timeout clock).</summary>
    public double TimeInPhaseSec => (DateTime.UtcNow - PhaseSinceUtc).TotalSeconds;

    /// <summary>Move the whole group to a new phase, stamping the timeout clock on a change.
    /// The coordinator calls this; member GroupOrders are then re-stamped from the new phase.</summary>
    public void SetPhase(GroupPhase phase)
    {
        if (Phase != phase) PhaseSinceUtc = DateTime.UtcNow;
        Phase = phase;
    }

    /// <summary>Rebuild-from-scratch on (re)Forming: drop the pool, cursor, and latched
    /// objective so the union is recomputed clean (a disband→reform or a hub change starts
    /// fresh). Keeps TrainBaselineLevel — the train cadence survives a re-form.</summary>
    public void ResetForForming()
    {
        Pool.Clear();
        Cursor = 0;
        LatchedObjective = ExecDirective.None;
        SetPhase(GroupPhase.Forming);
    }
}