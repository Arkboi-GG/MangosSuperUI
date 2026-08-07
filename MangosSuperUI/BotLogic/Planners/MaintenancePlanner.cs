using MangosSuperUI.BotLogic.Core;
using MangosSuperUI.BotLogic.Data;   // ZoneDataLoader — vendor/repair NPC lookup (GetNearestVendor)

namespace MangosSuperUI.BotLogic.Planners;

// ============================================================================
// MaintenancePlanner — Goal.Maintenance (Phase 4 — death recovery + heal + vendor/repair).
//
// Owns the bot's self-maintenance. Two ways in via the GoalSelector:
//   • DEAD → death recovery (rez → relocate → heal-to-full), held through the post-rez heal.
//   • ALIVE with cratered durability / full bags → a vendor errand (route → sell → repair),
//     held while ctx.Service is in flight. Recovery always wins; after a heal the planner
//     falls through to the vendor check, so a death that wrecks gear chains heal → repair.
//
// The C++ contract (verified against AiBotAI::UpdateAI + MoveToDestination +
// BridgeHandleResurrect, DEPLOYED continuation binary):
//   • on death: BuildPlayerRepop() → ghost AT THE CORPSE, emit DEATH x|y|z|map, return.
//     The bot's OWN task pipeline is dead (the death block returns before the TASK_MOVE_TO
//     resume logic). We TRIED to drive the ghost with a bridge MOVE_TO (the old ghost-walk) —
//     in theory BridgeRecv() runs before the death block and MoveToDestination has no IsDead
//     guard, so MovePoint(...) is reached — but EMPIRICALLY it never translated the ghost on
//     this build: every ghost-walk came back MOVE_FAILED no_path / moved=0yd. So a dead bot
//     can't be repositioned by a MOVE_TO; the only reposition-while-dead that works is the
//     NearTeleportTo graveyard port below.
//   • RESURRECT → ResurrectPlayer(0.5f) + SpawnCorpseBones() IN PLACE at the unit's current
//     pos, 50% HP. No teleport, no rez-radius limit → it revives exactly where it fell (or
//     at the graveyard, if a port moved it first).
//   • RESURRECT {at_graveyard:1} → (UPDATED C++ contract) NearTeleportTo the INVULNERABLE
//     ghost to the nearest faction graveyard, emit GRAVEYARD_PORT, stay DEAD. The planner
//     then sends a plain RESURRECT to rez at the graveyard. NearTeleportTo is the proven
//     seam-cross primitive (queued + applied by PlayerBotAI::UpdateAI's pending-teleport
//     handler) — NOT the old RepopAtGraveyard()+same-tick-rez race that revived on the corpse.
//     Used as the death-loop / death-cluster escape (a teleport ignores the mesh — unlike a
//     MOVE_TO, which a dead bot ignores).
//   • the OOC eat gate (DrinkAndEat) only fires during a task below 40% HP, but an IDLE
//     bot below 100% eats EVERY tick and returns before it can wander → it sits and
//     heals to full. So C# heals a bot with SET_TASK IDLE + polling STATE.health.
//
// The recovery is a THREE-PHASE machine, all of it in PlanNext (the brain has no
// EscalateRez handler; the planner only returns Issue/Fire/Wait/Done):
//
//   ── ARM ──
//   arm       first dead tick — record the death (durable, BotIdentity), capture the
//             death spot, decide loop vs isolated (bump DeathLoopStreak), blacklist the
//             pocket on a loop (steers QuestPlanner; does NOT move the bot), set a short
//             "corpse-run" delay. Wait.
//
//   ── ESCAPE (only when trapped) ──
//   graveyard_port  STILL DEAD. If DeathLoopStreak >= GraveyardAfterStreak (died more than
//             twice in the same pocket inside the 5-min window) OR a death-cluster fired
//             (≥N deaths / window, any spot/goal) OR this death is a RAPID RE-DEATH
//             (within RapidRedeathPortSec of the previous death, ANY spot — the measured
//             70-83% re-death-<90s bucket, 2026-07-06 run), the bot is trapped in a lethal area —
//             send RESURRECT{at_graveyard:1}; C++ NearTeleportTo's the invulnerable ghost
//             to the nearest faction graveyard, emits GRAVEYARD_PORT, stays dead; we then
//             plain-RESURRECT at the graveyard. A teleport, not a corpse-walk — it's the
//             ONLY reposition-while-dead that works on this build.
//
//   ── REZ ──
//   rez_sent  An isolated/first death just RESURRECTs IN PLACE where it fell (after the
//             corpse-run delay); a trapped death rezzes at the graveyard it was ported to.
//             WAIT on RESPAWN. A missed deadline comes back as ctx.Failure(deadline) → re-issue.
//
//   ── HEAL-TO-FULL (the survival fix) ──
//   heal      recovery does NOT release yet: rezzing at 50% and re-engaging is the death
//             spiral. Fire SET_TASK IDLE once, hold, and poll STATE.health; release to the
//             GoalSelector at RezHealTarget (and ~full mana for mana classes). A timeout
//             backstop covers a mob respawning on the corpse and gating DrinkAndEat.
//   GoalSelector keeps the bot in Maintenance through the rez+heal (RezSent && !HealDone,
//   and Dead while porting).
//
// Intentionally DROPPED:
//   • the GHOST-WALK (MOVE_TO the invulnerable corpse to a "safer" cell, then rez there) —
//     it never moved a ghost on this build: every walk returned MOVE_FAILED no_path / moved=0yd
//     and only burned the GhostWalkDeadlineSec before rezzing in place anyway, while spraying
//     bogus no_path events into the log. A lethal pocket is the graveyard port's job (a teleport
//     ignores the mesh); an isolated death just rezzes in place. FindSafeRezSpot + the
//     ZoneSafetyMap injection went with it.
//   • the alive-relocate (rez-first-then-move-at-50%-HP) — it died mid-hop in the pack.
//   • the OLD {at_graveyard:1} impl (RepopAtGraveyard + same-tick rez) — it raced and landed
//     on the corpse. The C++ branch is now NearTeleportTo-the-ghost-then-rez (see contract
//     above), so {at_graveyard:1} is IN USE as the death-loop / cluster escape.
//   • eating itself stays autonomous C++ (DrinkAndEat) — we only HOLD the bot IDLE so it
//     can finish eating before re-engaging.
//
// "Armed" = ctx.Maintenance != null. The brain nulls scratch on a goal CHANGE; a re-death
// while still in Maintenance (healing) is NOT a goal change, so the planner re-arms it
// itself (the Rezzed flag distinguishes a real re-death from the pre-alive RESPAWN wait).
// ============================================================================
public sealed class MaintenancePlanner : IBotPlanner
{
    // Injected: zone NPC data. GetNearestVendor backs the vendor/repair errand (the old
    // EconomyDomain took the same singleton). Returns entry + coords + a CanRepair flag.
    private readonly ZoneDataLoader _zoneData;

    // Injected: the [VENDOR] narration channel. The vendor errand used to die silent —
    // GiveUp logged nothing and a 1-tick bags-full bounce never showed in the 30s
    // FleetReport snapshot. This logs every stage (trigger / route / arrive+dist / sell /
    // repair / finish / giveup) so one run says exactly where the loop drops. ILogger<T>
    // is DI-resolved automatically — no Program.cs registration change.
    private readonly ILogger<MaintenancePlanner> _log;

    public MaintenancePlanner(ZoneDataLoader zoneData, ILogger<MaintenancePlanner> log)
    {
        _zoneData = zoneData;
        _log = log;
    }

    // Short "corpse-run" delay before rezzing: long enough for a leashing mob to
    // wander off before we pop up at 50% HP, with per-guid jitter so a wiped fleet
    // does not rez in lockstep. (Personality modulation can ride on top — see note.)
    private const float RezDelayBaseSec = 15f;
    private const int RezDelayJitterSec = 8;     // → 15-22s

    // ── Group rez guard gate (2026-07-04 round 5) ──
    // A GROUPED bot holds its in-place 50% rez until the coordinator reports a living groupmate
    // within guard range of the corpse (BotContext.GroupGuardNearUtc, stamped by TrackDeaths while
    // the GroupDefend protocol converges the team on this body). 33-45% of the round-5 re-deaths
    // landed inside 90s of a rez — a bot standing up at half HP alone inside the camp that just
    // killed it. Capped: a marooned / mid-converge party can never deadlock the rez, and the
    // MaxDeadSec backstop above it remains the absolute ceiling. Graveyard rezzes (safe ground)
    // and solo bots are untouched.
    private const double GuardFreshSec = 5;        // a stamp this recent = a guard is standing here NOW
    private const double GuardWaitCapSec = 45;     // max extra wait past the corpse-run delay before rezzing anyway

    private const double RespawnDeadlineSec = 20;  // RESURRECT → RESPAWN ack window (old ResurrectTimeoutSeconds)
    private const double MaxDeadSec = 300;  // absolute backstop (old MAX_DEAD_SECONDS)

    // Death-loop detection is time-windowed and DURABLE (BotIdentity.LastDeathTime),
    // so it survives the Maintenance scratch resetting on every death and does NOT
    // depend on the QuestPlanner's death-counter reset timing: a second death within
    // the window = a loop → blacklist the kill spot + escalate.
    private const double DeathLoopWindowSec = 300;

    // Only treat a quick re-death as a LOOP (→ blacklist + graveyard port) if it is in
    // the SAME pocket — within this many yards of the last death. A bot that dies here,
    // escapes, then dies to something else 200yd away is NOT looping; porting it to a
    // graveyard there would be disruptive for no reason.
    private const float DeathLoopRadiusYards = 30f;

    // The DEATH event carries no killer level, so the death-spot blacklist is gated by
    // the bot's OWN level: IsPathBlacklisted clears at Level >= (danger - 3), so a gate
    // of +6 gives ~3 levels of breathing room before it retries whatever pocket killed it.
    private const int DeathSpotDangerGate = 6;

    // ── Graveyard escalation (the no_path-pocket / death-march escape) ──
    // Ghost-walk can't climb out of a navmesh pocket — Hide proved it: streak 0→6, with
    // moved=0 on every single walk. So once we KNOW we're trapped, stop walking and teleport
    // out. DeathLoopStreak counts consecutive SAME-SPOT deaths inside DeathLoopWindowSec
    // (5 min); streak==2 is the 3rd such death — i.e. "died more than twice in < 5 min".
    // (streak>=1 would port on the 2nd death; streak>=2 on the 3rd. Tune here.) The graveyard
    // is a safe town hub that also has an armorer, so this fixes the dur-cratered death-march
    // in the same move.
    private const int GraveyardAfterStreak = 2;
    private const double GraveyardPortDeadlineSec = 10;  // wait for GRAVEYARD_PORT ack, else fall back to rez-in-place

    // ── Rapid re-death port (2026-07-06 — targets the measured re-death mass) ──
    // The 2026-07-06 17.5h solo run: 70-83% of deaths on the worst bots landed within 90s of a
    // RESPAWN — rez in place at the corpse, the killer/a wanderer still inside GetAttackDistance,
    // DrinkAndEat gated by combat, dead again. The same-spot streak (30yd) missed drifting
    // re-deaths and charged 2-3 deaths of tuition per pocket before porting; the cluster cap (3)
    // charged the same. This trigger matches the bucket exactly: a death within this many seconds
    // of the PREVIOUS death (death-to-death; corpse delay 15-22s + re-death <90s ≈ 105-112s),
    // ANY spot — the bot is trapped, port NOW. Tuition per pocket: exactly one re-death.
    // NB: the old MOVE_TO ghost-walk is NOT an option here — it never translated a ghost on this
    // build (see the contract block at the top); the graveyard NearTeleportTo is the only working
    // reposition-while-dead. Known no-op case: dying NEXT to the nearest graveyard (the Westfall
    // Sentinel Hill economic seal) — the port lands where the bot already stands; that class is
    // the broke+broken recovery's job, not this trigger's.
    private const double RapidRedeathPortSec = 120;

    // ── Per-quest death attribution (the macro-loop exit) ──
    // A death WHILE QUESTING is blamed on the quest the bot was working (the brain stamps
    // ctx.DeathBlameQuestId at the death transition). At QuestFailCap attributed failures the quest
    // is durably deferred so the bot stops walking back into the kill. Cap = 1: shelve on the FIRST
    // death — if the content is killing you, back off now and grind; the 60-min defer brings it back
    // when the bot is a level or two stronger. The unified streak (BotIdentity.QuestFailStreak) is
    // also bumped by a hard MOVE failure in QuestPlanner, so death + no_path share one cap. (The
    // SAME-SPOT physical loop that triggers the graveyard port is a SEPARATE axis — DeathLoopStreak.)
    private const int QuestFailCap = 3;   // was 1 -- keep in sync with QuestPlanner; one death over-shelved into grind-lock
    private const int QuestDeathDeferMinutes = 60;

    // ── Death-cluster escape (goal-agnostic) ──
    // A bot chain-dying at a lethal AREA (e.g. murlocs in a lake) during a vendor errand or grind
    // threads between BOTH existing nets: attribution is Questing-gated (no active quest to blame),
    // and the graveyard escalation is same-spot (30yd) + short-window (300s) gated while these deaths
    // are spread ~100yd and spaced by the vendor route timeout, so DeathLoopStreak resets to 0 each
    // time. This catches it on raw frequency: ≥ RecentDeathClusterCap deaths in RecentDeathWindowSec
    // — ANY spot, ANY goal — forces the graveyard port (teleport out of the area; a town graveyard has
    // an armorer so the repair errand can finally complete, then it heals + grinds somewhere safe).
    // The window is LONGER than a full vendor cycle (VendorRouteGiveupSec=480) so timeout-spaced deaths
    // still accumulate. Separate axis from DeathLoopStreak. RecentDeaths clears on any port.
    private const int RecentDeathClusterCap = 3;
    private const double RecentDeathWindowSec = 600;   // 10 min — must exceed VendorRouteGiveupSec (480)

    // ── Hearth escape (FINDING_008) ── the graveyard port fails to break a loop when the graveyard is
    // adjacent to the killer (SneakyShock: 307 deaths at the Wetlands Dragonmaw camp, its graveyard 30yd
    // away). HearthDeaths counts deaths in a window that does NOT clear on port; at the cap the ghost is
    // ported to the RACIAL START (guaranteed-safe, faction-appropriate) instead of the useless graveyard,
    // and the bot hard-resets so it re-quests from a clean spot. Same-map only (NearTeleportTo) — the
    // common case (a stray in an adjacent too-high zone on the bot's own continent).
    private const int HearthDeathCap = 5;              // deaths in the window before we give up on the graveyard and hearth home
    private const double HearthWindowSec = 360;        // 6 min — a genuinely persistent loop, not one bad camp

    // ── Heal-to-full phase ──
    // Hold the just-rezzed bot IDLE until it has eaten/drunk back to ~full before
    // releasing it to the GoalSelector. RezHealTarget is short of 100% so a single
    // unhealable point (e.g. a debuff cap) can't strand the phase; ManaTarget is loose
    // for the same reason and is auto-satisfied for no-mana classes (ManaPct == 1f).
    private const float RezHealTarget = 0.95f;   // HP fraction to release at
    private const float RezHealManaTarget = 0.85f;   // mana fraction (1f for melee → always ok)
    private const double HealTimeoutSec = 60;      // backstop: a mob on the corpse gates DrinkAndEat (combat) → don't wedge

    // ── Vendor / repair errand (ported EconomyDomain) ──
    private const int DurabilityVendorThreshold = 30;    // min equipped durability % before we break for a vendor (mirror GoalSelector)
    private const int RepairRequiredBelowDurability = 70;   // below this durability %, the vendor lookup is HARD-FILTERED to repair-capable NPCs (no sell-only detour) — force the armorer
    private const float VendorMaxTravelYards = 3000f; // don't START a march past this — quest until one is closer
    private const double VendorRouteGiveupSec = 480;  // abandon a trip that never arrives (~3000yd + margin)
    private const double VendorLegDeadlineSec = 120;  // per-MOVE_TO leg ceiling (capped paths re-send; a truly stuck leg gives up)
    private const double VendorAckDeadlineSec = 30;   // SELL_ACK / REPAIR_ACK wait before proceeding best-effort
    private const double VendorGiveupCooldownSec = 300;  // after a give-up, don't retry vendoring this long
    private const double VendorPhantomCooldownSec = 45;   // vendor/repair NPC wasn't in the world at arrival (runtime despawn / pool rotation past the load-time event-gate filter) — re-resolves fast, so a SHORT retry, not the 300s policy giveup
    private const double VendorDoneCooldownSec = 90;   // after a completed trip, let STATE durability/slots refresh before re-triggering
    private const float VendorArriveYards = 15f;  // C++ finds the NPC within 15yd — must be this close to sell/repair
    private const int SellKeepQuality = 2;    // sell grey+white, keep green+ (personality-tuned greed dropped for now)

    public Goal Handles => Goal.Maintenance;

    public StepResult PlanNext(BotContext ctx, BotStateSnapshot snap)
    {
        var id = ctx.Identity;

        // Consume any negative outcome. While in Maintenance the WAITs are the RESURRECT→
        // RESPAWN one and the graveyard-port→GRAVEYARD_PORT one (the heal phase fires IDLE
        // with NO wait), and the only failure that reaches here is a deadline (the brain's
        // expired-WAIT block) — a cue to re-issue / fall through.
        var failure = ctx.Failure;
        ctx.Failure = null;

        // ── ALIVE ──
        if (!ctx.Dead)
        {
            var rm = ctx.Maintenance;

            // Death-recovery in flight takes priority — heal first. Repositioning happens
            // PRE-rez (ghost-walk or graveyard port), so by the time we're alive the bot is
            // already on safe ground — nothing to relocate, just heal.
            if (rm != null && rm.RezSent)
            {
                rm.Rezzed = true;                        // came back alive after a RESURRECT — a later dead tick is a re-death
                if (!rm.HealDone)
                    return HealToFull(ctx, rm);          // hold IDLE, eat back to ~full
                // Healed. Gear may be wrecked from the death — fall through to the vendor
                // errand (durability/bags are re-checked there; it no-ops if we're fine).
            }

            // Vendor / repair errand. The GoalSelector routed us here for low durability /
            // full bags, or we're resuming an in-flight trip on ctx.Service.
            return VendorStep(ctx, failure);
        }

        // ── DEAD ──
        // Re-death during/after recovery: we already rezzed (Rezzed) and died again. The
        // goal stayed Maintenance, so the brain never nulled the scratch — drop it so we
        // re-arm cleanly for the NEW death (and the loop check below sees it fresh).
        // NB: gated on Rezzed, NOT RezSent — during the pre-alive RESPAWN wait RezSent is
        // already true but Rezzed is not, so this won't spuriously re-arm mid-rez.
        if (ctx.Maintenance is { Rezzed: true })
            ctx.Maintenance = null;

        // ── Arm on the first dead tick (of this death) ──
        if (ctx.Maintenance == null)
        {
            // The ghost stands at the corpse (C++ never moves it), so ctx.Pos IS the death
            // spot. Loop = a quick re-death in the SAME pocket — check BEFORE RecordDeath
            // (which overwrites LastDeathTime / LastDeathLocation).
            bool deathLoop = id != null
                             && id.LastDeathTime != default
                             && (DateTime.UtcNow - id.LastDeathTime).TotalSeconds < DeathLoopWindowSec
                             && SameSpotAsLastDeath(id.LastDeathLocation, ctx);

            // Rapid re-death (2026-07-06): this death landed within RapidRedeathPortSec of the
            // PREVIOUS death — ANY spot (no 30yd gate; a rez→stagger-40yd→die chain is the same
            // trap). Computed BEFORE RecordDeath overwrites LastDeathTime, like the loop check.
            bool rapidRedeath = id != null
                                && id.LastDeathTime != default
                                && (DateTime.UtcNow - id.LastDeathTime).TotalSeconds < RapidRedeathPortSec;

            // Escalation counter: a loop death pushes the relocate further next rez; a death
            // somewhere new means the last relocate worked → reset. Bump BEFORE RecordDeath
            // (which overwrites LastDeathLocation) using the loop verdict computed above.
            if (id != null) id.DeathLoopStreak = deathLoop ? id.DeathLoopStreak + 1 : 0;

            var deathPos = new Vec4(ctx.Pos.X, ctx.Pos.Y, ctx.Pos.Z, ctx.MapId);
            id?.RecordDeath(deathPos.X, deathPos.Y, deathPos.Map);  // durable; also feeds QuestPlanner shelving

            // Goal-agnostic death-cluster: count deaths in the rolling window (any spot, any goal).
            // At the cap this forces the graveyard port below even when DeathLoop is false — the
            // murloc-lake / vendor-errand chain that DeathLoopStreak and Questing attribution both miss.
            int recentDeaths = id?.RecordRecentDeathAndCount(RecentDeathWindowSec) ?? 0;
            bool deathCluster = recentDeaths >= RecentDeathClusterCap;

            // [HEARTH] (FINDING_008) Death window that SURVIVES graveyard ports: at the cap the ports
            // have demonstrably failed to break the loop → hearth to the racial start instead. Same-map
            // only (the ghost port is a NearTeleportTo); a cross-continent home would fall back to the
            // graveyard port (racialHome.Map != ctx.MapId → hearthEscape false, no regression).
            int hearthDeaths = id?.RecordHearthDeathAndCount(HearthWindowSec) ?? 0;
            var racialHome = id != null ? BotIdentity.RacialStart(id.Race) : (X: 0f, Y: 0f, Z: 0f, Map: -1);
            bool hearthEscape = hearthDeaths >= HearthDeathCap && racialHome.Map == ctx.MapId && !ctx.InPlayerParty;

            // On a same-spot loop, blacklist the pocket so the QuestPlanner stops ROUTING
            // back here until the bot out-levels it (clears at danger-3). This only steers
            // future quest selection — it does NOT move the bot; the ghost-walk below does.
            if (deathLoop)
                id?.AddPathBlacklist(deathPos.X, deathPos.Y, ctx.Level + DeathSpotDangerGate);

            // ── Attribute this death to the quest the bot was working (the macro-loop exit) ──
            // The brain stamped ctx.DeathBlameQuestId at the death transition (it had ctx.Quest
            // before the scratch reset; we don't). Bump the unified per-quest fail streak; at the
            // cap, durably DEFER that quest so the bot won't be routed back to the kill — and clear
            // the streak. Cap is 3 (at 1, one unlucky death over-shelved into grind-lock); the
            // 60-min defer brings the quest back when the bot is a level or two stronger.
            // No blame id (died grinding, or between legs) → nothing to do.
            if (id != null && ctx.DeathBlameQuestId is int blamed)
            {
                int fails = id.QuestFailStreak.GetValueOrDefault(blamed, 0) + 1;
                id.QuestFailStreak[blamed] = fails;
                if (fails >= QuestFailCap)
                {
                    id.DeferQuest(blamed, TimeSpan.FromMinutes(QuestDeathDeferMinutes));
                    id.QuestFailStreak.Remove(blamed);
                    _log.LogInformation("[REZ] {Name} shelving quest [{Q}] {Min}min — {N} death(s) attributed (won't route back)",
                        ctx.Name, blamed, QuestDeathDeferMinutes, fails);
                }
                else
                {
                    _log.LogInformation("[REZ] {Name} death blamed on quest [{Q}] (streak {N}/{Cap})",
                        ctx.Name, blamed, fails, QuestFailCap);
                }
            }
            ctx.DeathBlameQuestId = null;

            // A death while GRIND-LOCKED proves the locked spot is lethal. Release the lock so the
            // bot reselects after recovery (quests if anything is pickable where it lands) instead of
            // being FORCED back onto the grind that is killing it. A safe, productive grind never dies
            // here, so a working lock survives untouched -- only a lethal one breaks. The graveyard
            // port (below / earlier) is what physically relocates it; this just stops re-pinning it.
            if (id?.GrindLockUntil is DateTime glk && DateTime.UtcNow < glk)
            {
                id.GrindLockUntil = null;
                _log.LogInformation("[REZ] {Name} grind-lock released (died while locked)", ctx.Name);
            }

            ctx.Maintenance = new MaintenanceScratch
            {
                DeadSinceUtc = DateTime.UtcNow,
                RezAtUtc = DateTime.UtcNow.AddSeconds(RezDelayBaseSec + (ctx.Guid % RezDelayJitterSec)),
                DeathPos = deathPos,
                DeathLoop = deathLoop,
                // Rapid re-death rides the cluster flag — identical escape (graveyard port), zero
                // new scratch fields. The ARM log line below distinguishes them (RAPID vs CLUSTER).
                DeathCluster = deathCluster || rapidRedeath,
                HearthEscape = hearthEscape   // FINDING_008: persistent loop → port to racial start, not the graveyard
            };
            ctx.Service = null;   // drop any in-flight vendor trip — recovery owns the bot; re-evaluate after heal
            ctx.SetStep("rez_wait");
            _log.LogInformation("[REZ] {Name} DEATH @ ({X:F0},{Y:F0})@{Map} loop={Loop} streak={Streak} recent={Recent}{Cluster}{Rapid}{Hearth} (deaths={Deaths} hearthWin={HearthN})",
                ctx.Name, deathPos.X, deathPos.Y, deathPos.Map, deathLoop, id?.DeathLoopStreak ?? 0,
                recentDeaths, deathCluster ? " CLUSTER" : "", rapidRedeath ? " RAPID" : "", hearthEscape ? " HEARTH" : "",
                id?.DeathsSinceQuestStart ?? 0, hearthDeaths);
            return StepResult.Wait();
        }

        var m = ctx.Maintenance;

        // RESURRECT WAIT blew its deadline (RESPAWN never arrived) → re-issue.
        if (failure != null && m.RezSent)
            return SendResurrect(ctx, m);

        // Already sent and waiting (Pending cleared but STATE not yet alive) — don't
        // spam a second RESURRECT; the WAIT / next STATE resolves it.
        if (m.RezSent)
            return StepResult.Wait();

        // Absolute dead-time backstop → rez now, wherever we are.
        if ((DateTime.UtcNow - m.DeadSinceUtc).TotalSeconds > MaxDeadSec)
            return SendResurrect(ctx, m);

        // ── GRAVEYARD ESCALATION ──
        // Three triggers, same escape: (1) DeathLoopStreak — died >2× in the SAME pocket inside the
        // 5-min window (a no_path pocket / lethal spot we keep dying in); (2) DeathCluster —
        // ≥N deaths in the rolling window regardless of spot/goal (the murloc-lake / vendor-errand
        // chain that streak + Questing-attribution both miss); or (3) a RAPID re-death — this death
        // within RapidRedeathPortSec of the previous one, any spot (folded into the DeathCluster
        // stamp at ARM — the measured 70-83% re-death bucket, 2026-07-06). Either way C++ ports the INVULNERABLE
        // ghost to the nearest faction graveyard (RESURRECT{at_graveyard:1} → NearTeleportTo, the
        // proven seam-cross primitive — NOT the old RepopAtGraveyard race), emits GRAVEYARD_PORT, and
        // stays dead. We then send a plain RESURRECT to rez there. RelocateSent/RelocateDone are reused
        // as the port phase flags. Runs BEFORE the corpse-run delay below: we're leaving the area now.
        bool useGraveyard = (id?.DeathLoopStreak ?? 0) >= GraveyardAfterStreak || m.DeathCluster || m.HearthEscape;
        // [PLAYERPARTY] Never graveyard-port a companion (2026-07-07): the party is standing at
        // the corpse — an in-place rez rejoins the fight, while a port would teleport the escort
        // across the map away from the human mid-quest (the rapid-redeath trigger would fire on
        // exactly the "died twice defending you" case). All three triggers suppressed; the plain
        // in-place RESURRECT below still runs on the normal delay. The ARM bookkeeping above
        // (death-spot blacklist, quest blame, streaks) still recorded — it informs post-party
        // behaviour without moving anyone now.
        if (useGraveyard && ctx.InPlayerParty)
        {
            useGraveyard = false;
            _log.LogInformation("[GRAVE] {Name} port suppressed — in a REAL player's party (in-place rez beside the group)",
                ctx.Name);
        }
        if (useGraveyard)
        {
            if (!m.RelocateSent)
                return SendGraveyardPort(ctx, m);

            if (!m.RelocateDone)
            {
                // GRAVEYARD_PORT acked, OR the port WAIT failed/deadlined. Either way rez now:
                // on success at the graveyard, on failure in place (never worse than today). If
                // the teleport fired it has already landed — PlayerBotAI::UpdateAI applies
                // pending teleports at the TOP of the tick, before BridgeRecv processes this
                // RESURRECT, so the rez can't beat the port.
                m.RelocateDone = true;
                _log.LogInformation("[GRAVE] {Name} port {Why} — rez @ ({X:F0},{Y:F0})@{Map} streak={Streak}",
                    ctx.Name, failure != null ? "FAILED/deadline (rez in place)" : "done",
                    ctx.Pos.X, ctx.Pos.Y, ctx.MapId, id?.DeathLoopStreak ?? 0);

                // [HEARTH] (FINDING_008) The ghost was ported to the racial start (C++ home override) and
                // is about to rez there — HARD-RESET so the bot re-quests from the clean spot instead of
                // walking back into the killer: drop the stale grind (armed at the death zone), broadly
                // blacklist the death zone from quest routing, and clear the death counters/streaks.
                if (m.HearthEscape && id != null)
                {
                    int wasDeaths = id.DeathsSinceQuestStart;
                    id.AddPathBlacklist(m.DeathPos.X, m.DeathPos.Y, ctx.Level + DeathSpotDangerGate);
                    id.ResetDeathCounter();          // also clears HearthDeaths
                    ctx.Grind = null;                // re-arm grind fresh at the racial start, not the death cell
                    ctx.ClearObjective();
                    _log.LogWarning("[HEARTH] {Name} hearth-escape — rez at racial start (race {Race}); death zone ({X:F0},{Y:F0})@{Map} blacklisted, counters reset (was {Deaths} deaths)",
                        ctx.Name, id.Race, m.DeathPos.X, m.DeathPos.Y, m.DeathPos.Map, wasDeaths);
                }
            }

            return SendResurrect(ctx, m);
        }

        // Still waiting out the corpse-run delay — lets a leashing mob wander off before we
        // pop up at 50% HP in place. (Heal-to-full then tops us off before re-engaging.)
        if (DateTime.UtcNow < m.RezAtUtc)
            return StepResult.Wait();

        // ── GROUP GUARD GATE (round 5) ── a grouped bot waits for the defend converge before
        // standing up at 50% in the camp that killed it. Fresh guard stamp OR the cap elapsed ->
        // fall through to the rez.
        if (ctx.GroupOrder.IsActive
            && (DateTime.UtcNow - ctx.GroupGuardNearUtc).TotalSeconds > GuardFreshSec
            && (DateTime.UtcNow - m.RezAtUtc).TotalSeconds < GuardWaitCapSec)
        {
            if (ctx.Step != "rez_guard_wait")
            {
                ctx.SetStep("rez_guard_wait");
                _log.LogInformation("[REZ] {Name} holding rez — waiting for a guard (group converging on corpse)", ctx.Name);
            }
            return StepResult.Wait();
        }

        // Delay elapsed, not a loop/cluster → rez IN PLACE, where we died.
        //
        // The old ghost-walk (MOVE_TO the invulnerable corpse to a "safer" cell, then rez
        // there) is GONE. On this build it never actually moved a ghost: every walk came back
        // MOVE_FAILED no_path with moved=0yd, so it only burned its ~45s walk deadline
        // before rezzing right here anyway — pure noise (and a pile of bogus no_path events in
        // the log). A genuinely lethal pocket is already handled by the same-spot DeathLoopStreak
        // and the death-cluster graveyard port ABOVE — both teleports, not corpse-walks. So an
        // isolated/first death just rezzes in place; heal-to-full then sits it IDLE to ~full.
        return SendResurrect(ctx, m);
    }

    // ── Vendor / repair errand (ported from EconomyDomain, on the WAIT spine) ──
    // route → sell → repair → done, all driven from PlanNext while ALIVE under
    // Goal.Maintenance. State rides ctx.Service (nulled on a death tick and on finish, so
    // each trip is fresh). Everything past the route is best-effort: a missing ack rides
    // its deadline (→ ctx.Failure) and we proceed rather than wedge — exactly the old
    // domain's timeout behaviour. The GoalSelector pins us in Maintenance while
    // ctx.Service.Phase != None.
    private StepResult VendorStep(BotContext ctx, WaitFailure? failure)
    {
        var sv = ctx.Service;

        // ── Teleport-assist: the TELEPORT_TO hop ITSELF failed/deadlined ──
        // CommandType distinguishes a teleport fail from a SELL_FAIL/REPAIR_FAIL (those flow to
        // SellStep/RepairStep, which return-to-anchor via GiveUp/GiveUpPhantom). Outbound fail →
        // couldn't reach the vendor → give up. Inbound fail → business is done and the bot is at a
        // (safe) town vendor, just couldn't get home → exit per the trip outcome.
        if (failure != null && failure.CommandType == "TELEPORT_TO" && ctx.Teleport is { } tpf)
        {
            var phase = tpf.Phase;
            bool wasFailed = tpf.Failed;
            ctx.Teleport = null;
            if (phase == TpPhase.Inbound)
                return wasFailed ? GiveUp(ctx, $"tp-return:{failure.Reason}") : FinishVendor(ctx);
            return GiveUp(ctx, $"teleport:{failure.Reason}");
        }

        // ── Teleport-assist round-trip advance (TELEPORT_ACK arrivals) ──
        // Outbound → AtTarget: the executor set ctx.Pos from the ack, so we're AT the vendor — begin
        // selling. Inbound: returned to the pre-teleport anchor → finish (or give up if the trip failed).
        // AtTarget falls through to the phase switch, which drives sell/repair at real proximity.
        if (sv != null && ctx.Teleport is { } tp)
        {
            if (tp.Phase == TpPhase.Outbound)
            {
                tp.Phase = TpPhase.AtTarget;
                sv.Phase = VendorPhase.Sell;
                ctx.SetStep("vendor_sell");
                _log.LogInformation("[VENDOR] {Name} teleported in → SELL_ITEMS entry={Entry} keepQ={Q} bag={Bag} dur={Dur}",
                    ctx.Name, sv.TargetNpcEntry, SellKeepQuality, ctx.FreeSlots, ctx.Durability);
                var cmd = new BridgeCommand("SELL_ITEMS", new { npc_entry = sv.TargetNpcEntry, keep_quality = SellKeepQuality });
                return StepResult.Send(cmd, "SELL_ACK", TimeSpan.FromSeconds(VendorAckDeadlineSec));
            }
            if (tp.Phase == TpPhase.Inbound)
            {
                bool failed = tp.Failed;
                ctx.Teleport = null;
                return failed ? GiveUp(ctx, "vendor failed at npc (returned)") : FinishVendor(ctx);
            }
            // AtTarget → fall through to the sell/repair phase switch below.
        }

        // Not started — confirm we still need it and pick the nearest (repair-biased) vendor.
        if (sv == null || sv.Phase == VendorPhase.None)
        {
            if (!NeedsVendor(ctx))
            {
                _log.LogInformation("[VENDOR] {Name} release: NeedsVendor=false (bag={Bag} dur={Dur} cooldownUntil={CD})",
                    ctx.Name, ctx.FreeSlots, ctx.Durability, ctx.Identity?.VendorCooldownUntil);
                return StepResult.Complete();   // durability/slots already fine / on cooldown → release
            }

            // [VENDOR] point 1 — the trigger fired and we ENTERED the errand. This is the
            // thing the 30s snapshot could never confirm: proof the vendor branch runs.
            _log.LogInformation("[VENDOR] {Name} guid={Guid} trigger bag={Bag} dur={Dur} z={Zone} pos=({X:F0},{Y:F0})@{Map} lvl={Lvl}",
                ctx.Name, ctx.Guid, ctx.FreeSlots, ctx.Durability, ctx.ZoneId, ctx.Pos.X, ctx.Pos.Y, ctx.MapId, ctx.Level);

            // Below the repair floor a sell-only vendor is useless — hard-require an armorer
            // so the bot can't keep walking to the nearest food vendor while durability latches.
            bool requireRepair = ctx.Durability < RepairRequiredBelowDurability;
            var vendor = _zoneData.GetNearestVendor(ctx.ZoneId, ctx.MapId, ctx.Pos.X, ctx.Pos.Y, ctx.Level, requireRepair);
            if (vendor == null)
                return GiveUp(ctx, requireRepair ? "no repair vendor in range (dur low)" : "no vendor in zone");   // ZoneDataLoader logs the cap/closest that drove the null

            var target = new Vec4(vendor.X, vendor.Y, vendor.Z, vendor.MapId);
            float startDist = ctx.Pos.Dist2D(target.Pos);
            if (startDist > VendorMaxTravelYards)
            {
                _log.LogInformation("[VENDOR] {Name} nearest {Vendor} @ {Dist:F0}yd past policy cap {Cap:F0} → giveup",
                    ctx.Name, vendor.NpcName, startDist, VendorMaxTravelYards);
                return GiveUp(ctx, "nearest vendor past travel cap");
            }

            sv = ctx.Service = new ServiceScratch
            {
                TargetNpcEntry = vendor.NpcEntry,
                TargetPos = target,
                CanRepair = vendor.CanRepair,
                Phase = VendorPhase.Route,
                StartedUtc = DateTime.UtcNow
            };
            ctx.SetStep("vendor_route");
            // [VENDOR] point 2 — route armed. NpcEntry/dist/repair lets the run show whether
            // the chosen target is the convenient one or a far/interior pick.
            _log.LogInformation("[VENDOR] {Name} route → {Vendor} (entry={Entry}) @ {Dist:F0}yd repair={Rep}",
                ctx.Name, vendor.NpcName, vendor.NpcEntry, startDist, vendor.CanRepair);
            return MoveToVendor(sv);
        }

        // In flight — advance the active phase (its WAIT just resolved; failure consumed).
        switch (sv.Phase)
        {
            case VendorPhase.Route: return RouteStep(ctx, sv, failure);
            case VendorPhase.Sell: return SellStep(ctx, sv, failure);
            case VendorPhase.Repair: return RepairStep(ctx, sv, failure);   // REPAIR_ACK / REPAIR_FAIL / deadline
            default: return FinishVendor(ctx);
        }
    }

    // A MOVE_TO leg resolved. Arrived (≤15yd) → sell. A long path can complete short of
    // the vendor (capped MovePoint) with no failure → send another leg. A no_path/deadline
    // failure, or blowing the overall trip budget, → give up (cooldown).
    private StepResult RouteStep(BotContext ctx, ServiceScratch sv, WaitFailure? failure)
    {
        if (failure != null)
        {
            // Teleport-assist: a no_path on the final approach to the vendor, in the vicinity → hop
            // the last few yards instead of giving up (the vendor sits in a nav-dead pocket MOVE_TO
            // can't reach the last yards into). First no_path retries; the second, within reach, warps.
            if (TeleportAssist.IsApproachNoPath(failure))
            {
                sv.RouteFails++;
                switch (TeleportAssist.Decide(sv.RouteFails, ctx.Pos, sv.TargetPos, ctx.MapId))
                {
                    case TeleportAssist.TpDecision.Teleport:
                        _log.LogInformation("[VENDOR] {Name} vendor unreachable ({N}× no_path, {D:F0}yd) — TELEPORT_TO entry={Entry}",
                            ctx.Name, sv.RouteFails, ctx.Pos.Dist2D(sv.TargetPos.Pos), sv.TargetNpcEntry);
                        return StepResult.Send(TeleportAssist.BeginOutbound(ctx, sv.TargetPos), "TELEPORT_ACK", TeleportAssist.AckDeadline);
                    case TeleportAssist.TpDecision.Retry:
                        return MoveToVendor(sv);   // one more chance to path closer
                                                   // TpDecision.GiveUp → fall through (vendor genuinely far — not a final-approach pocket)
                }
            }

            _log.LogInformation("[VENDOR] {Name} route FAILED reason={Reason} (target entry={Entry} @ ({TX:F0},{TY:F0})) → giveup",
                ctx.Name, failure.Reason, sv.TargetNpcEntry, sv.TargetPos.X, sv.TargetPos.Y);
            return GiveUp(ctx, $"route {failure.Reason}");
        }

        float dist = ctx.Pos.Dist2D(sv.TargetPos.Pos);

        if (dist <= VendorArriveYards)
        {
            sv.Phase = VendorPhase.Sell;
            ctx.SetStep("vendor_sell");
            // [VENDOR] point 3 — arrived. The arrival distance vs the 15yd gate is the whole
            // ballgame for "reached the vendor but never sold": if we only ever log this with
            // dist hugging the gate, the bot is squeaking in; if we NEVER log it, see the
            // 'never arrived' line below for the closest approach it managed.
            _log.LogInformation("[VENDOR] {Name} ARRIVED dist={Dist:F1}yd (gate {Gate}) → SELL_ITEMS entry={Entry} keepQ={Q} bag={Bag} dur={Dur}",
                ctx.Name, dist, VendorArriveYards, sv.TargetNpcEntry, SellKeepQuality, ctx.FreeSlots, ctx.Durability);
            var cmd = new BridgeCommand("SELL_ITEMS", new { npc_entry = sv.TargetNpcEntry, keep_quality = SellKeepQuality });
            return StepResult.Send(cmd, "SELL_ACK", TimeSpan.FromSeconds(VendorAckDeadlineSec));
        }

        if ((DateTime.UtcNow - sv.StartedUtc).TotalSeconds > VendorRouteGiveupSec)
        {
            _log.LogInformation("[VENDOR] {Name} NEVER ARRIVED — closest approach {Dist:F1}yd (gate {Gate}) after {Sec:F0}s (target entry={Entry}) → giveup",
                ctx.Name, dist, VendorArriveYards, (DateTime.UtcNow - sv.StartedUtc).TotalSeconds, sv.TargetNpcEntry);
            return GiveUp(ctx, "never arrived");
        }

        // Still en route — capped MovePoint completes short, send another leg. Logging the
        // per-leg distance shows whether the bot is closing on the vendor or stuck off it.
        _log.LogInformation("[VENDOR] {Name} route leg done dist={Dist:F1}yd > gate {Gate} → another MOVE_TO ({Sec:F0}s/{Budget:F0}s)",
            ctx.Name, dist, VendorArriveYards, (DateTime.UtcNow - sv.StartedUtc).TotalSeconds, VendorRouteGiveupSec);
        return MoveToVendor(sv);   // another leg toward the vendor
    }

    // SELL_ACK landed (or its deadline), OR SELL_FAIL negated the WAIT (BotExecutor.TryNegate).
    // On SELL_FAIL the chosen NPC isn't in the world right now (vendor_not_found — a runtime
    // despawn / pool rotation that slipped past ZoneDataLoader's load-time event-gate filter) or
    // the command was malformed (missing_npc_entry): do NOT fall through to repair/finish as if we
    // sold — abandon on a SHORT phantom cooldown so a transient despawn re-resolves quickly, and
    // without the 30s deadline burn (the WAIT is now negated immediately). On success: repair if the
    // vendor can — even when nothing sold, gear may be wrecked from deaths — else finish.
    private StepResult SellStep(BotContext ctx, ServiceScratch sv, WaitFailure? failure)
    {
        if (failure != null)
        {
            if (failure.Reason is "vendor_not_found" or "missing_npc_entry")
                return GiveUpPhantom(ctx, $"sell {failure.Reason} entry={sv.TargetNpcEntry}");
            return GiveUp(ctx, $"sell {failure.Reason}");
        }

        // [VENDOR] point 4 — SELL_ACK (or its 30s deadline) landed. bag AFTER the sell is the
        // tell for "vendored but freed nothing" (all-protected / all-kept-quality bags): if
        // bag is still 0 here, the sell ran but didn't help → the bot re-triggers after cooldown.
        _log.LogInformation("[VENDOR] {Name} SELL done bag={Bag} dur={Dur} canRepair={Rep} → {Next}",
            ctx.Name, ctx.FreeSlots, ctx.Durability, sv.CanRepair, sv.CanRepair ? "REPAIR_AT_NPC" : "finish");
        if (sv.CanRepair)
        {
            sv.Phase = VendorPhase.Repair;
            ctx.SetStep("vendor_repair");
            var cmd = new BridgeCommand("REPAIR_AT_NPC", new { npc_entry = sv.TargetNpcEntry });
            return StepResult.Send(cmd, "REPAIR_ACK", TimeSpan.FromSeconds(VendorAckDeadlineSec));
        }
        return FinishVendor(ctx);
    }

    // REPAIR_ACK landed (or its deadline), OR REPAIR_FAIL negated the WAIT. Distinguish the
    // reasons: not_enough_gold is the ECONOMIC wall, NOT a phantom — the sell leg already ran and
    // the bot is simply broke, so finish normally (it'll grind for gold and re-trigger; the old
    // behaviour, now without the 30s deadline burn). npc_not_found = the repair NPC isn't in the
    // world → short phantom cooldown, same as the sell case. A clean REPAIR_ACK or any other
    // outcome → finish.
    private StepResult RepairStep(BotContext ctx, ServiceScratch sv, WaitFailure? failure)
    {
        if (failure != null)
        {
            if (failure.Reason == "npc_not_found")
                return GiveUpPhantom(ctx, $"repair npc_not_found entry={sv.TargetNpcEntry}");
            _log.LogInformation("[VENDOR] {Name} repair failed reason={Reason} → finish (sell leg already done)",
                ctx.Name, failure.Reason);
        }
        return FinishVendor(ctx);
    }

    private StepResult MoveToVendor(ServiceScratch sv)
    {
        var t = sv.TargetPos;
        var cmd = new BridgeCommand("MOVE_TO", new { mapId = t.Map, x = t.X, y = t.Y, z = t.Z });
        return StepResult.Send(cmd, "TASK_COMPLETE", TimeSpan.FromSeconds(VendorLegDeadlineSec));
    }

    // Teleport-assist: if we teleported INTO this vendor (final-approach pocket), hop back to the
    // anchor BEFORE actually exiting — never strand the bot in the pocket (its next goal's MOVE_TO out
    // would no_path). Returns the return-teleport step when a return is owed (the Inbound completion
    // in VendorStep then re-enters the real exit with ctx.Teleport cleared), else null. tripFailed
    // rides ctx.Teleport.Failed so the Inbound completion finishes vs gives up correctly.
    private static StepResult? ReturnIfTeleported(BotContext ctx, bool tripFailed)
    {
        if (ctx.Teleport is { Phase: TpPhase.AtTarget } tp)
        {
            tp.Failed = tripFailed;
            return StepResult.Send(TeleportAssist.BeginReturn(ctx), "TELEPORT_ACK", TeleportAssist.AckDeadline);
        }
        return null;
    }

    // Trip done — short cooldown so STATE (durability/slots) can refresh before the trigger
    // re-evaluates, clear the errand, release to the GoalSelector.
    private StepResult FinishVendor(BotContext ctx)
    {
        if (ReturnIfTeleported(ctx, tripFailed: false) is { } ret) return ret;   // hop home first if we teleported in
        if (ctx.Identity is { } id) id.VendorCooldownUntil = DateTime.UtcNow.AddSeconds(VendorDoneCooldownSec);
        ctx.Service = null;
        ctx.SetStep("vendor_done");
        // [VENDOR] point 5 — reached the END of the errand cleanly. If bag is STILL 0 / dur
        // still low here, the trip completed but accomplished nothing (→ it'll re-trigger
        // after the done-cooldown). This is the success path's narration.
        _log.LogInformation("[VENDOR] {Name} FINISH (done cooldown {Sec}s) bag={Bag} dur={Dur}",
            ctx.Name, VendorDoneCooldownSec, ctx.FreeSlots, ctx.Durability);
        return StepResult.Complete();
    }

    // Abandon the trip (no vendor / too far / unreachable) — longer cooldown so we quest a
    // while before retrying instead of re-rolling the same far vendor every tick.
    private StepResult GiveUp(BotContext ctx, string why)
    {
        if (ReturnIfTeleported(ctx, tripFailed: true) is { } ret) return ret;   // hop home first if we teleported in
        if (ctx.Identity is { } id) id.VendorCooldownUntil = DateTime.UtcNow.AddSeconds(VendorGiveupCooldownSec);
        ctx.Service = null;
        ctx.SetStep($"vendor_giveup:{why}");
        // [VENDOR] point 6 — THE silent killer, now loud. This is the line that was never
        // emitted before: every reason the errand bails (no vendor / past cap / route
        // failure / never arrived) lands here with the cooldown it just set, so the next run
        // says exactly WHY the loop never reaches the end. Warning level so it can't be missed.
        _log.LogWarning("[VENDOR] {Name} GIVEUP why='{Why}' (cooldown {Sec}s) bag={Bag} dur={Dur} z={Zone} pos=({X:F0},{Y:F0})@{Map} lvl={Lvl}",
            ctx.Name, why, VendorGiveupCooldownSec, ctx.FreeSlots, ctx.Durability, ctx.ZoneId, ctx.Pos.X, ctx.Pos.Y, ctx.MapId, ctx.Level);
        return StepResult.Complete();
    }

    // The vendor/repair NPC the bot routed to wasn't in the world at arrival (SELL_FAIL/REPAIR_FAIL
    // vendor_not_found / npc_not_found). ZoneDataLoader's load-time event-gate filter removes the
    // KNOWN phantom class (Darkmoon faire vendors while the faire is down); what reaches here is a
    // RUNTIME despawn — a killed/respawning spawn or a pool rotation — which re-resolves on its own
    // shortly. So abandon on a SHORT cooldown (re-trigger soon) rather than the 300s policy giveup,
    // and — via the executor's new SELL_FAIL/REPAIR_FAIL negation — without the 30s ack-deadline burn.
    // Separate, louder [VENDOR] PHANTOM line so a hot phantom (an entry that keeps despawning) is
    // visible in one grep instead of hiding among the policy give-ups.
    private StepResult GiveUpPhantom(BotContext ctx, string why)
    {
        if (ReturnIfTeleported(ctx, tripFailed: true) is { } ret) return ret;   // hop home first if we teleported in
        if (ctx.Identity is { } id) id.VendorCooldownUntil = DateTime.UtcNow.AddSeconds(VendorPhantomCooldownSec);
        ctx.Service = null;
        ctx.SetStep($"vendor_phantom:{why}");
        _log.LogWarning("[VENDOR] {Name} PHANTOM why='{Why}' (short cooldown {Sec}s) bag={Bag} dur={Dur} z={Zone} pos=({X:F0},{Y:F0})@{Map} lvl={Lvl}",
            ctx.Name, why, VendorPhantomCooldownSec, ctx.FreeSlots, ctx.Durability, ctx.ZoneId, ctx.Pos.X, ctx.Pos.Y, ctx.MapId, ctx.Level);
        return StepResult.Complete();
    }

    // Durability cratered (gear about to break) or bags full (can't loot), and not on the
    // post-trip / give-up cooldown. Mirrors the GoalSelector trigger so a stale entry that
    // arrives here after the condition cleared just releases.
    private static bool NeedsVendor(BotContext ctx)
    {
        if (ctx.Identity?.VendorCooldownUntil is DateTime cd && DateTime.UtcNow < cd)
            return false;
        return ctx.Durability < DurabilityVendorThreshold || ctx.FreeSlots <= 0;
    }

    // ── Heal-to-full: hold the just-rezzed bot IDLE until it has eaten back to ~full ──
    // C++ rezzes at 50% HP; a TASKED bot only tops off below 40% and the grind patrol
    // breaks the eat channel, so re-engaging at 50% is the spiral. An IDLE bot below 100%
    // eats every tick and never wanders, so we fire IDLE once, then poll STATE.health and
    // release at the target. The GoalSelector keeps us in Maintenance until HealDone.
    private static StepResult HealToFull(BotContext ctx, MaintenanceScratch m)
    {
        if (!m.IdleFired)
        {
            m.IdleFired = true;
            m.HealSinceUtc = DateTime.UtcNow;
            ctx.SetStep("heal");
            // Fire-and-forget: SET_TASK IDLE arms no WAIT (its liveness is the heal poll,
            // not a one-shot ack). Lets autonomous C++ DrinkAndEat sit the bot to full.
            return StepResult.Fire(new BridgeCommand("SET_TASK", new { task = "IDLE" }));
        }

        bool hpOk = ctx.HpPct >= RezHealTarget;
        bool manaOk = ctx.ManaPct >= RezHealManaTarget;   // 1f for no-mana classes → always ok
        if (hpOk && manaOk)
        {
            m.HealDone = true;
            return StepResult.Complete();
        }

        // Backstop: a mob respawned on the corpse / persistent combat gates DrinkAndEat
        // (it bails when the bot has a victim) → HP can't climb. Don't wedge in heal —
        // release after the timeout and let the GoalSelector reselect. If it dies again in
        // the same pocket, next cycle escalates to a graveyard rez.
        if ((DateTime.UtcNow - m.HealSinceUtc).TotalSeconds > HealTimeoutSec)
        {
            m.HealDone = true;
            return StepResult.Complete();
        }

        return StepResult.Wait();   // keep IDLEing and polling
    }

    // A re-death is a LOOP only if it is in the same pocket as the last death (same map,
    // within DeathLoopRadiusYards). Compares the PREVIOUS death location (still live in
    // BotIdentity — we check before RecordDeath) against the current ghost position.
    private static bool SameSpotAsLastDeath(in (float X, float Y, int Map) last, BotContext ctx)
    {
        if (last.Map != ctx.MapId) return false;
        float dx = last.X - ctx.Pos.X, dy = last.Y - ctx.Pos.Y;
        return (dx * dx + dy * dy) <= DeathLoopRadiusYards * DeathLoopRadiusYards;
    }

    // Plain, in-place rez. The ghost-walk (or the graveyard port) already moved us to safe
    // ground, so ResurrectPlayer revives at the unit's current pos — we come back exactly
    // where the ghost stopped (the safe cell, or the graveyard after a port).
    private StepResult SendResurrect(BotContext ctx, MaintenanceScratch m)
    {
        m.RezSent = true;
        ctx.SetStep("rez_sent");

        var cmd = new BridgeCommand("RESURRECT");

        _log.LogInformation("[REZ] {Name} RESURRECT (in place) streak={Streak} @ ({X:F0},{Y:F0})@{Map}",
            ctx.Name, ctx.Identity?.DeathLoopStreak ?? 0, ctx.Pos.X, ctx.Pos.Y, ctx.MapId);

        // WAIT on RESPAWN: C++ revives in place at 50% HP and emits it; the executor acks by
        // event type, the next STATE clears isDead, and heal-to-full takes over before the
        // GoalSelector reselects.
        return StepResult.Send(cmd, "RESPAWN", TimeSpan.FromSeconds(RespawnDeadlineSec));
    }

    // Graveyard escalation: tell C++ to port the invulnerable ghost to the nearest faction
    // graveyard. C++ uses NearTeleportTo (the proven seam-cross primitive — NOT the old
    // RepopAtGraveyard race), stays dead, and emits GRAVEYARD_PORT. On that ack the planner
    // sends a plain RESURRECT (SendResurrect) to rez at the graveyard. A missed ack falls
    // through to the !RelocateDone branch → rez in place, so the worst case is exactly today.
    private StepResult SendGraveyardPort(BotContext ctx, MaintenanceScratch m)
    {
        m.RelocateSent = true;
        ctx.SetStep("graveyard_port");

        // Fresh rolling window at the new location: we're leaving the lethal area, so a death soon
        // after the port is a new problem, not a continuation of this cluster.
        ctx.Identity?.RecentDeaths.Clear();

        _log.LogInformation("[GRAVE] {Name} graveyard port ({Why}) from ({X:F0},{Y:F0})@{Map} streak={Streak}",
            ctx.Name,
            m.HearthEscape ? "HEARTH — racial start"
                           : m.DeathCluster ? $"death-cluster ≥{RecentDeathClusterCap}/{RecentDeathWindowSec / 60:F0}min OR rapid re-death <{RapidRedeathPortSec:F0}s"
                           : $"streak={ctx.Identity?.DeathLoopStreak ?? 0} >= {GraveyardAfterStreak}",
            ctx.Pos.X, ctx.Pos.Y, ctx.MapId, ctx.Identity?.DeathLoopStreak ?? 0);

        // [HEARTH] (FINDING_008) On a hearth-escape, hand C++ the racial-start coord so it ports the ghost
        // THERE (safe) instead of the nearest — useless, killer-adjacent — graveyard. Same-map guaranteed
        // by the arm-time check (racialHome.Map == ctx.MapId). Otherwise the plain graveyard port.
        BridgeCommand cmd;
        if (m.HearthEscape && ctx.Identity != null)
        {
            var h = BotIdentity.RacialStart(ctx.Identity.Race);
            cmd = new BridgeCommand("RESURRECT", new { at_graveyard = 1, home_x = h.X, home_y = h.Y, home_z = h.Z, home_map = h.Map });
        }
        else
        {
            cmd = new BridgeCommand("RESURRECT", new { at_graveyard = 1 });
        }
        return StepResult.Send(cmd, "GRAVEYARD_PORT", TimeSpan.FromSeconds(GraveyardPortDeadlineSec));
    }

    // The rez timer, the RESPAWN WAIT, and the heal poll all own liveness, so this goal is
    // always "progressing" — the brain stays in PlanNext (where every recovery decision
    // lives) and never routes to OnStall. The heal phase can't false-stall either: it arms
    // no WAIT (IDLE is fire-and-forget) and is bounded by HealTimeoutSec.
    public bool IsProgressing(BotContext ctx, BotStateSnapshot snap) => true;

    // Semantic only: IsProgressing never returns false, so the current brain never
    // invokes this. Declares intent for when an EscalateRez handler lands in the brain.
    public StallAction OnStall(BotContext ctx)
        => StallAction.Of(StallActionKind.EscalateRez, "rez:stuck");
}