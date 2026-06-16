using MangosSuperUI.BotLogic.Core;

namespace MangosSuperUI.BotLogic.Planners;

// ============================================================================
// MaintenancePlanner — Goal.Maintenance (Phase 4 — death recovery).
//
// Ports the death-recovery half of the old MaintenanceDomain onto the spine.
// Bots were getting STUCK DEAD because nothing on the new brain reacted to the
// dead state. The C++ contract (verified in AiBotAI::UpdateAI + BridgeHandleResurrect,
// deployed S36 binary):
//   • on death: BuildPlayerRepop() → ghost AT THE CORPSE, emit DEATH x|y|z|map, return.
//     There is NO auto-revive — every subsequent dead tick is a bare return,
//     "wait for C# RESURRECT".
//   • RESURRECT → ResurrectPlayer(0.5f) + SpawnCorpseBones() IN PLACE at 50% HP,
//     emits RESPAWN + a fresh STATE. The handler IGNORES at_graveyard (no parse) —
//     plain corpse rez only on this binary.
// With no planner for Goal.Maintenance and no GoalSelector trigger, that RESURRECT
// was never sent → permanent ghost. This planner sends it.
//
// All rez logic lives in PlanNext (the brain has no EscalateRez handler; the planner
// only ever returns Issue/Fire/Wait/Done and lets the brain dispatch):
//   arm      first dead tick — record the death (durable, BotIdentity), capture the
//            death spot, set a short "corpse-run" delay. Wait.
//   rez_wait delay elapses → RESURRECT + WAIT on RESPAWN. On a death LOOP (a quick
//            re-death) blacklist the kill spot first and ride along at_graveyard.
//   rez_sent WAIT on RESPAWN. C++ revives in place and emits RESPAWN; the executor
//            acks by type, the next STATE clears ctx.Dead, GoalSelector drops
//            Maintenance and the bot resumes. A missed RESPAWN deadline comes back as
//            ctx.Failure(deadline) → re-issue, escalated.
//
// Intentionally DROPPED from the old domain (S36 reality, no C++ change):
//   • ghost-walk-to-safe-spot — a ghost never runs a task on this binary (the death
//     tick returns before task processing) and the safe-spot sampler is unreliable
//     near mesh holes. The lean, robust S36 escape is a death-spot blacklist instead.
//   • the at_graveyard TELEPORT — ignored by the deployed BridgeHandleResurrect.
//     We still send the flag as a forward-compatible ride-along; it only bites once
//     S41 ships. On S36 the real escape is the blacklist below.
//   • eating — stays autonomous C++ (DrinkAndEat). This planner is death-only.
//
// "Armed" = ctx.Maintenance != null (re-armed when the brain nulls scratch on goal
// (re)entry, so each fresh death starts clean — same idiom as GrindPlanner).
// ============================================================================
public sealed class MaintenancePlanner : IBotPlanner
{
    // Short "corpse-run" delay before rezzing: long enough for a leashing mob to
    // wander off before we pop up at 50% HP, with per-guid jitter so a wiped fleet
    // does not rez in lockstep. (Personality modulation can ride on top — see note.)
    private const float RezDelayBaseSec   = 15f;
    private const int   RezDelayJitterSec = 8;     // → 15-22s

    private const double RespawnDeadlineSec = 20;  // RESURRECT → RESPAWN ack window (old ResurrectTimeoutSeconds)
    private const double MaxDeadSec         = 300;  // absolute backstop (old MAX_DEAD_SECONDS)

    // Death-loop detection is time-windowed and DURABLE (BotIdentity.LastDeathTime),
    // so it survives the Maintenance scratch resetting on every death and does NOT
    // depend on the QuestPlanner's death-counter reset timing: a second death within
    // the window = a loop → blacklist the kill spot + escalate.
    private const double DeathLoopWindowSec = 300;

    // The DEATH event carries no killer level, so the death-spot blacklist is gated by
    // the bot's OWN level: IsPathBlacklisted clears at Level >= (danger - 3), so a gate
    // of +6 gives ~3 levels of breathing room before it retries whatever pocket killed it.
    private const int DeathSpotDangerGate = 6;

    public Goal Handles => Goal.Maintenance;

    public StepResult PlanNext(BotContext ctx, BotStateSnapshot snap)
    {
        var id = ctx.Identity;

        // Consume any negative outcome. While in Maintenance the only WAIT is the
        // RESURRECT→RESPAWN one, and the only failure that can reach here is its
        // deadline (the brain's expired-WAIT block) — a cue to re-issue, escalated.
        var failure = ctx.Failure;
        ctx.Failure = null;

        // Alive again — let the brain reselect (GoalSelector drops Maintenance the
        // moment STATE clears isDead; this is a guard if we are still resolved here).
        if (!ctx.Dead)
            return StepResult.Complete();

        // ── Arm on the first dead tick ──
        if (ctx.Maintenance == null)
        {
            // Loop check BEFORE RecordDeath (which overwrites LastDeathTime).
            bool deathLoop = id != null
                             && id.LastDeathTime != default
                             && (DateTime.UtcNow - id.LastDeathTime).TotalSeconds < DeathLoopWindowSec;

            // The ghost stands at the corpse (C++ never moves it), so ctx.Pos IS the
            // death spot — no need to consume the DEATH event payload.
            var deathPos = new Vec4(ctx.Pos.X, ctx.Pos.Y, ctx.Pos.Z, ctx.MapId);
            id?.RecordDeath(deathPos.X, deathPos.Y, deathPos.Map);  // durable; also feeds QuestPlanner shelving

            ctx.Maintenance = new MaintenanceScratch
            {
                DeadSinceUtc = DateTime.UtcNow,
                RezAtUtc     = DateTime.UtcNow.AddSeconds(RezDelayBaseSec + (ctx.Guid % RezDelayJitterSec)),
                DeathPos     = deathPos,
                DeathLoop    = deathLoop
            };
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

        // Delay elapsed → resurrect (escalated if this death is part of a loop).
        return SendResurrect(ctx, m, escalate: m.DeathLoop);
    }

    private static StepResult SendResurrect(BotContext ctx, MaintenanceScratch m, bool escalate)
    {
        if (escalate && !m.Escalated)
        {
            m.Escalated = true;
            // The S36 escape: blacklist the kill spot so the QuestPlanner stops routing
            // here until the bot out-levels it (IsPathBlacklisted clears at danger-3).
            ctx.Identity?.AddPathBlacklist(m.DeathPos.X, m.DeathPos.Y, ctx.Level + DeathSpotDangerGate);
        }

        m.RezSent = true;
        ctx.SetStep("rez_sent");

        var cmd = escalate
            ? new BridgeCommand("RESURRECT", new { at_graveyard = 1 })  // no-op on S36, real escape once S41 ships
            : new BridgeCommand("RESURRECT");

        // WAIT on RESPAWN: C++ revives in place at 50% HP and emits it; the executor
        // acks by event type, the next STATE clears isDead → GoalSelector exits Maintenance.
        return StepResult.Send(cmd, "RESPAWN", TimeSpan.FromSeconds(RespawnDeadlineSec));
    }

    // While dead, the rez timer + the RESPAWN WAIT own liveness, so we are always
    // "progressing" — the brain stays in PlanNext (where every rez decision lives)
    // and never routes to OnStall. Alive = not stalled.
    public bool IsProgressing(BotContext ctx, BotStateSnapshot snap) => true;

    // Semantic only: IsProgressing never returns false, so the current brain never
    // invokes this. Declares intent for when an EscalateRez handler lands in the brain.
    public StallAction OnStall(BotContext ctx)
        => StallAction.Of(StallActionKind.EscalateRez, "rez:stuck");
}
