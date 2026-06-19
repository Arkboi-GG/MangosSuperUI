using MangosSuperUI.BotLogic.Core;
using MangosSuperUI.BotLogic.Data;   // ZoneSafetyMap — safe-rez-spot sampling (ported FindSafeRezSpot)

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
// The C++ contract (verified in AiBotAI::UpdateAI + BridgeHandleResurrect, DEPLOYED
// continuation binary with the at_graveyard rez change applied):
//   • on death: BuildPlayerRepop() → ghost AT THE CORPSE, emit DEATH x|y|z|map, return.
//     There is NO auto-revive — every subsequent dead tick is a bare return,
//     "wait for C# RESURRECT".
//   • RESURRECT → ResurrectPlayer(0.5f) + SpawnCorpseBones() IN PLACE at 50% HP.
//   • RESURRECT {at_graveyard:1} → RepopAtGraveyard() (relocate the ghost to the map's
//     nearest faction graveyard) THEN ResurrectPlayer(0.5f) there. This is now a REAL
//     teleport escape — the binary honors the flag (the prior "no-op on S36" is stale).
//   • the OOC eat gate (DrinkAndEat) only fires during a task below 40% HP, but an IDLE
//     bot below 100% eats EVERY tick and returns before it can wander → it sits and
//     heals to full. So C# heals a bot with SET_TASK IDLE + polling STATE.health.
//
// The recovery is a THREE-PHASE machine, all of it in PlanNext (the brain has no
// EscalateRez handler; the planner only returns Issue/Fire/Wait/Done):
//
//   ── REZ ──
//   arm       first dead tick — record the death (durable, BotIdentity), capture the
//             death spot, decide loop vs isolated, set a short "corpse-run" delay. Wait.
//   rez_wait  delay elapses → RESURRECT, WAIT on RESPAWN. On a SAME-SPOT loop (a quick
//             re-death within the window AND within DeathLoopRadius of the last death):
//             blacklist the kill pocket AND send {at_graveyard:1} so C++ ports the ghost
//             to a graveyard before rezzing — the geometric escape from a death trap.
//   rez_sent  WAIT on RESPAWN. A missed deadline comes back as ctx.Failure(deadline) →
//             re-issue, hard-escalated.
//
//   ── RELOCATE (ported from MaintenanceDomain.FindSafeRezSpot) ──
//   relocate  next STATE clears ctx.Dead. If the rez cell has hostile spawns (per
//             ZoneSafetyMap) and we did NOT graveyard-rez, MOVE_TO the safest of 8
//             sampled directions ~25yd out before healing — a 50%-HP bot eating next to
//             the mob that killed it just dies again. Best-effort and ONCE: on
//             TASK_COMPLETE / no_path / deadline we proceed to heal regardless. The old
//             domain ghost-walked THEN rezzed; that can't work (a dead bot bare-returns
//             every tick — it can't run a task), so we rez first and relocate ALIVE.
//             Skipped when the cell is clear or we already ported to a (safe) graveyard.
//
//   ── HEAL-TO-FULL (the survival fix) ──
//   heal      recovery does NOT release yet: rezzing at 50% and re-engaging is the death
//             spiral. Fire SET_TASK IDLE once, hold, and poll STATE.health; release to the
//             GoalSelector at RezHealTarget (and ~full mana for mana classes). A timeout
//             backstop covers a mob respawning on the corpse and gating DrinkAndEat.
//   GoalSelector keeps the bot in Maintenance through relocate+heal (RezSent && !HealDone).
//
// Intentionally DROPPED:
//   • the old GHOST-walk-then-rez — a ghost never runs a task on this binary; we relocate
//     ALIVE after rez instead (same FindSafeRezSpot sampling, applied post-rez).
//   • eating itself stays autonomous C++ (DrinkAndEat) — we only HOLD the bot IDLE so it
//     can finish eating before re-engaging.
//
// "Armed" = ctx.Maintenance != null. The brain nulls scratch on a goal CHANGE; a re-death
// while still in Maintenance (healing) is NOT a goal change, so the planner re-arms it
// itself (the Rezzed flag distinguishes a real re-death from the pre-alive RESPAWN wait).
// ============================================================================
public sealed class MaintenancePlanner : IBotPlanner
{
    // Injected: the creature-spawn safety grid. Used by FindSafeRezSpot to sample where
    // to relocate a just-rezzed bot off a hostile cell. Same singleton the QuestPlanner /
    // GoalSelector reach for; the old MaintenanceDomain took it the same way.
    private readonly ZoneSafetyMap _safetyMap;

    // Injected: zone NPC data. GetNearestVendor backs the vendor/repair errand (the old
    // EconomyDomain took the same singleton). Returns entry + coords + a CanRepair flag.
    private readonly ZoneDataLoader _zoneData;

    // Injected: the [VENDOR] narration channel. The vendor errand used to die silent —
    // GiveUp logged nothing and a 1-tick bags-full bounce never showed in the 30s
    // FleetReport snapshot. This logs every stage (trigger / route / arrive+dist / sell /
    // repair / finish / giveup) so one run says exactly where the loop drops. ILogger<T>
    // is DI-resolved automatically — no Program.cs registration change.
    private readonly ILogger<MaintenancePlanner> _log;

    public MaintenancePlanner(ZoneSafetyMap safetyMap, ZoneDataLoader zoneData, ILogger<MaintenancePlanner> log)
    {
        _safetyMap = safetyMap;
        _zoneData = zoneData;
        _log = log;
    }

    // Short "corpse-run" delay before rezzing: long enough for a leashing mob to
    // wander off before we pop up at 50% HP, with per-guid jitter so a wiped fleet
    // does not rez in lockstep. (Personality modulation can ride on top — see note.)
    private const float RezDelayBaseSec = 15f;
    private const int RezDelayJitterSec = 8;     // → 15-22s

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

    // ── Heal-to-full phase ──
    // Hold the just-rezzed bot IDLE until it has eaten/drunk back to ~full before
    // releasing it to the GoalSelector. RezHealTarget is short of 100% so a single
    // unhealable point (e.g. a debuff cap) can't strand the phase; ManaTarget is loose
    // for the same reason and is auto-satisfied for no-mana classes (ManaPct == 1f).
    private const float RezHealTarget = 0.95f;   // HP fraction to release at
    private const float RezHealManaTarget = 0.85f;   // mana fraction (1f for melee → always ok)
    private const double HealTimeoutSec = 60;      // backstop: a mob on the corpse gates DrinkAndEat (combat) → don't wedge

    // ── Relocate phase (ported FindSafeRezSpot) ──
    private const float RezOffsetYards = 25f;   // sample distance for the safe spot (within WoW's ~36yd rez radius)
    private const double RelocateDeadlineSec = 15;    // best-effort relocate MOVE_TO ceiling before we heal anyway

    // ── Vendor / repair errand (ported EconomyDomain) ──
    private const int DurabilityVendorThreshold = 30;    // min equipped durability % before we break for a vendor (mirror GoalSelector)
    private const float VendorMaxTravelYards = 3000f; // don't START a march past this — quest until one is closer
    private const double VendorRouteGiveupSec = 480;  // abandon a trip that never arrives (~3000yd + margin)
    private const double VendorLegDeadlineSec = 120;  // per-MOVE_TO leg ceiling (capped paths re-send; a truly stuck leg gives up)
    private const double VendorAckDeadlineSec = 30;   // SELL_ACK / REPAIR_ACK wait before proceeding best-effort
    private const double VendorGiveupCooldownSec = 300;  // after a give-up, don't retry vendoring this long
    private const double VendorDoneCooldownSec = 90;   // after a completed trip, let STATE durability/slots refresh before re-triggering
    private const float VendorArriveYards = 15f;  // C++ finds the NPC within 15yd — must be this close to sell/repair
    private const int SellKeepQuality = 2;    // sell grey+white, keep green+ (personality-tuned greed dropped for now)

    public Goal Handles => Goal.Maintenance;

    public StepResult PlanNext(BotContext ctx, BotStateSnapshot snap)
    {
        var id = ctx.Identity;

        // Consume any negative outcome. While in Maintenance the only WAIT is the
        // RESURRECT→RESPAWN one (the heal phase fires IDLE with NO wait), and the only
        // failure that can reach here is its deadline (the brain's expired-WAIT block) —
        // a cue to re-issue, escalated.
        var failure = ctx.Failure;
        ctx.Failure = null;

        // ── ALIVE ──
        if (!ctx.Dead)
        {
            var rm = ctx.Maintenance;

            // Death-recovery in flight takes priority — finish relocate → heal first.
            if (rm != null && rm.RezSent)
            {
                rm.Rezzed = true;                        // came back alive after a RESURRECT — a later dead tick is a re-death
                if (!rm.HealDone)
                {
                    if (!rm.RelocateDone)
                        return RelocateOrHeal(ctx, rm);  // step off a hostile rez cell (best-effort)
                    return HealToFull(ctx, rm);          // hold IDLE, eat back to ~full
                }
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

            var deathPos = new Vec4(ctx.Pos.X, ctx.Pos.Y, ctx.Pos.Z, ctx.MapId);
            id?.RecordDeath(deathPos.X, deathPos.Y, deathPos.Map);  // durable; also feeds QuestPlanner shelving

            ctx.Maintenance = new MaintenanceScratch
            {
                DeadSinceUtc = DateTime.UtcNow,
                RezAtUtc = DateTime.UtcNow.AddSeconds(RezDelayBaseSec + (ctx.Guid % RezDelayJitterSec)),
                DeathPos = deathPos,
                DeathLoop = deathLoop
            };
            ctx.Service = null;   // drop any in-flight vendor trip — recovery owns the bot; re-evaluate after heal
            ctx.SetStep("rez_wait");
            return StepResult.Wait();
        }

        var m = ctx.Maintenance;

        // RESURRECT WAIT blew its deadline (RESPAWN never arrived) → re-issue, hard-escalate.
        if (failure != null && m.RezSent)
        {
            m.DeathLoop = true;
            return SendResurrect(ctx, m, escalate: true);
        }

        // Already sent and waiting (Pending cleared but STATE not yet alive) — don't
        // spam a second RESURRECT; the WAIT / next STATE resolves it.
        if (m.RezSent)
            return StepResult.Wait();

        // Absolute dead-time backstop.
        if ((DateTime.UtcNow - m.DeadSinceUtc).TotalSeconds > MaxDeadSec)
            return SendResurrect(ctx, m, escalate: true);

        // Still waiting out the corpse-run delay.
        if (DateTime.UtcNow < m.RezAtUtc)
            return StepResult.Wait();

        // Delay elapsed → resurrect (graveyard-escalated if this is a same-spot loop).
        return SendResurrect(ctx, m, escalate: m.DeathLoop);
    }

    // ── Relocate: step a just-rezzed bot off a hostile cell before it heals ──
    // Ported from MaintenanceDomain.FindSafeRezSpot, but run ALIVE (post-rez) instead of
    // ghost-walked pre-rez — a dead bot can't move on this binary. Best-effort and ONCE:
    // the moment a relocate MOVE_TO has been issued, the next time we land here (its
    // outcome consumed at the top of PlanNext) we fall through to heal, win or lose.
    private StepResult RelocateOrHeal(BotContext ctx, MaintenanceScratch m)
    {
        // Already issued the relocate MOVE_TO — its ack / no_path / deadline came and went;
        // we don't retry. Proceed to heal.
        if (m.RelocateSent)
        {
            m.RelocateDone = true;
            return HealToFull(ctx, m);
        }

        // A graveyard rez already dropped the bot on safe ground — no relocate needed.
        if (m.Escalated)
        {
            m.RelocateDone = true;
            return HealToFull(ctx, m);
        }

        // Sample the safety grid around where we rezzed. Null = the cell has no hostile
        // spawns (or no safety data loaded) → safe to heal in place.
        var spot = FindSafeRezSpot(ctx.Pos.X, ctx.Pos.Y, ctx.Pos.Z, ctx.MapId);
        if (spot == null)
        {
            m.RelocateDone = true;
            return HealToFull(ctx, m);
        }

        var safe = spot.Value;
        m.RelocateSent = true;
        ctx.SetStep("relocate");
        var cmd = new BridgeCommand("MOVE_TO", new { mapId = ctx.MapId, x = safe.X, y = safe.Y, z = safe.Z });
        return StepResult.Send(cmd, "TASK_COMPLETE", TimeSpan.FromSeconds(RelocateDeadlineSec));
    }

    // Sample 8 directions at RezOffsetYards and return the safest (lowest max creature
    // level) — or null if the current cell already has no hostile spawns / no grid loaded.
    // Faithful to the old MaintenanceDomain sampler, minus the ghost-walk framing.
    private Vec4? FindSafeRezSpot(float x, float y, float z, int mapId)
    {
        if (!_safetyMap.IsLoaded)
            return null;

        int hereMax = _safetyMap.GetMaxCreatureLevel(mapId, x, y);
        if (hereMax == 0)
            return null;   // no hostile spawns in this cell — heal in place

        int bestLevel = hereMax;
        float bestX = x, bestY = y;
        bool foundBetter = false;

        for (int dir = 0; dir < 8; dir++)
        {
            float angle = dir * MathF.PI / 4f;                  // N, NE, E, ... NW
            float tx = x + MathF.Cos(angle) * RezOffsetYards;
            float ty = y + MathF.Sin(angle) * RezOffsetYards;
            int lvl = _safetyMap.GetMaxCreatureLevel(mapId, tx, ty);
            if (lvl < bestLevel) { bestLevel = lvl; bestX = tx; bestY = ty; foundBetter = true; }
        }

        if (!foundBetter)
        {
            // No safer direction — prefer any EMPTY cell; else just step 25yd east to put
            // distance between us and whatever killed us (forces a re-path, buys eat time).
            for (int dir = 0; dir < 8; dir++)
            {
                float angle = dir * MathF.PI / 4f;
                float tx = x + MathF.Cos(angle) * RezOffsetYards;
                float ty = y + MathF.Sin(angle) * RezOffsetYards;
                if (_safetyMap.GetMaxCreatureLevel(mapId, tx, ty) == 0)
                    return new Vec4(tx, ty, z, mapId);
            }
            bestX = x + RezOffsetYards;
            bestY = y;
        }

        return new Vec4(bestX, bestY, z, mapId);
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

            var vendor = _zoneData.GetNearestVendor(ctx.ZoneId, ctx.MapId, ctx.Pos.X, ctx.Pos.Y, ctx.Level);
            if (vendor == null)
                return GiveUp(ctx, "no vendor in zone");   // ZoneDataLoader logs the cap/closest that drove the null

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
            case VendorPhase.Sell: return SellStep(ctx, sv);
            case VendorPhase.Repair: return FinishVendor(ctx);   // REPAIR_ACK / deadline → done
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

    // SELL_ACK (or its deadline) landed. Repair if the vendor can — even when nothing sold,
    // gear may be wrecked from deaths — else finish.
    private StepResult SellStep(BotContext ctx, ServiceScratch sv)
    {
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

    private StepResult MoveToVendor(ServiceScratch sv)
    {
        var t = sv.TargetPos;
        var cmd = new BridgeCommand("MOVE_TO", new { mapId = t.Map, x = t.X, y = t.Y, z = t.Z });
        return StepResult.Send(cmd, "TASK_COMPLETE", TimeSpan.FromSeconds(VendorLegDeadlineSec));
    }

    // Trip done — short cooldown so STATE (durability/slots) can refresh before the trigger
    // re-evaluates, clear the errand, release to the GoalSelector.
    private StepResult FinishVendor(BotContext ctx)
    {
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

    private static StepResult SendResurrect(BotContext ctx, MaintenanceScratch m, bool escalate)
    {
        if (escalate && !m.Escalated)
        {
            m.Escalated = true;
            // Blacklist the kill pocket so the QuestPlanner stops ROUTING here until the
            // bot out-levels it (IsPathBlacklisted clears at danger-3). The graveyard port
            // below physically MOVES the bot out; the blacklist keeps it from walking back.
            ctx.Identity?.AddPathBlacklist(m.DeathPos.X, m.DeathPos.Y, ctx.Level + DeathSpotDangerGate);
        }

        m.RezSent = true;
        ctx.SetStep("rez_sent");

        // On a same-spot loop, escalate to a GRAVEYARD rez: C++ honors {at_graveyard:1}
        // by RepopAtGraveyard() (relocate the ghost to the map's nearest faction graveyard)
        // then rezzing there — the geometric escape from a death trap. Plain RESURRECT
        // rezzes in place at the corpse (50% HP) for an isolated death.
        var cmd = escalate
            ? new BridgeCommand("RESURRECT", new { at_graveyard = 1 })
            : new BridgeCommand("RESURRECT");

        // WAIT on RESPAWN: C++ revives (in place, or at the graveyard) at 50% HP and emits
        // it; the executor acks by event type, the next STATE clears isDead, and the
        // heal-to-full phase takes over before the GoalSelector reselects.
        return StepResult.Send(cmd, "RESPAWN", TimeSpan.FromSeconds(RespawnDeadlineSec));
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