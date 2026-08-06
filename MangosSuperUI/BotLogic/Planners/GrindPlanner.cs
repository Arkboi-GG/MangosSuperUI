using MangosSuperUI.BotLogic.Core;
using MangosSuperUI.BotLogic.Data;

namespace MangosSuperUI.BotLogic.Planners;

// ============================================================================
// GrindPlanner — Goal.Grinding. DEAD SIMPLE (2026-06-22 rewrite).
//
// Directive: find the nearest LEVEL-APPROPRIATE mob that gives XP, kill it, rescan,
// repeat. That scan is a C++ job (SelectGrindTarget) — only C++ sees live nearby
// creatures (entry / level / type / position) at grind time. The C# safety grid holds
// cell AGGREGATES only (avg/max level + spawn count), so it cannot name a reachable mob.
// This planner therefore does ONE thing: arm an indefinite SET_TASK GRIND at the bot's
// current spot and let C++ scan → kill → rescan.
//
// REMOVED (the stranding machinery): cell/density scoring, "spot weak → relocate to a
// denser cell", FindGrindCell, the dead-cell ring, HereIsGood, the relocate state machine.
// The relocate chased AVG creature density; density climbs toward higher-level zones, so it
// dragged bots (Ocagey) westward into red zones they then couldn't path out of. Gone.
//
// A bot with no valid mobs nearby simply lands no REAL kills → IsProgressing goes false →
// the brain's no-progress breaker escalates (park → hearth home), instead of this planner
// walking it off a cliff chasing density. (Real-kill gating lives in BotExecutor: a
// critter/grey KILL logs "trash kill — not progress" and does NOT advance LastKillUtc, so
// a chicken farmer reads as no-progress here by construction.)
//
// "Armed" = ctx.Grind != null; the brain nulls it on (re)entry to re-arm here.
// ============================================================================
public sealed class GrindPlanner : IBotPlanner
{
    private const float GrindRadius = 60f;       // solo-grind radius (C++ SelectGrindTarget leash)
    private const double ArmGraceSec = 45;       // grace after entry before KILL-recency applies
    private const double KillRecencySec = 120;   // a bot landing REAL kills is progressing

    private readonly ILogger<GrindPlanner> _log;
    private readonly ZoneSafetyMap _safety;

    public GrindPlanner(ILogger<GrindPlanner> log, ZoneSafetyMap safety)
    {
        _log = log;
        _safety = safety;
    }

    public Goal Handles => Goal.Grinding;

    public StepResult PlanNext(BotContext ctx, BotStateSnapshot snap)
    {
        // Arm / re-arm: indefinite grind at the bot's CURRENT position. C++ SelectGrindTarget
        // does the nearest-level-appropriate-XP-mob scan and keeps rescanning; we never steer it
        // beyond "here". creature_entry=0 = "nearest valid hostile" (C++ skips critters/grey);
        // kill_count=0 = indefinite (never TASK_COMPLETEs — the KILL stream is the only feedback,
        // so there is no WAIT that could false-stall a killing bot).
        if (ctx.Grind == null)
        {
            // GUARD-TOWN BAIL (FINDING_005). Never arm a filler grind while standing in an ENEMY city-guard
            // cell: city guards social-assist + respawn, so an L18 that strayed into Menethil grinds L47
            // guards forever (100-attacker chain-pull → 1%-HP grind-lock). The C++ SelectGrindTarget
            // IsGuard() exclusion stops the bot PICKING a guard; this stops it committing to grind here at
            // all, so no SET_TASK is sent and the stall path (OnStall→ReselectGoal, then the wedge breaker's
            // park→relocate/hearth) bails it out of the town instead of pinning it on the garrison. Record
            // the cell dead so a relocate doesn't drop back onto it. Fail-open: no grid/guard data → no-op.
            if (_safety.IsLoaded &&
                _safety.IsEnemyGuardCell(snap.MapId, snap.X, snap.Y, ZoneSafetyMap.TeamFromFaction(ctx.Identity?.Faction)))
            {
                ctx.RecordDeadGrindCell(snap.X, snap.Y);
                _log.LogWarning(
                    "[GRIND] {Name} refusing filler grind in enemy guard cell @ ({X:F0},{Y:F0}) map {M} — bailing (FINDING_005)",
                    ctx.Name, snap.X, snap.Y, snap.MapId);
                return StepResult.Block("grind:guard-town");
            }

            // Fallback grind is the re-evaluated FILLER, not a strategic commitment — so it carries no
            // held objective (the GoalSelector re-picks the moment a quest/group order appears, exactly
            // as today). Clear any stale held objective (e.g. a coordinator order left on a just-ungrouped
            // bot) so the reconcile (Held-Objective build §3) has nothing to defend here. (Held-Objective
            // anchor is inert until the Session-3 echo lands; this keeps it clean regardless.)
            ctx.ClearObjective();

            ctx.Grind = new GrindScratch
            {
                CreatureEntry = 0,
                AreaCenter = new Vec4(snap.X, snap.Y, snap.Z, snap.MapId),
                Radius = GrindRadius,
                KillGoal = 0,
                KillCount = 0
            };
            ctx.SetStep("grind");

            // Wire shape ported 1:1 from CombatDomain.BuildGrind (snake_case keys the C++
            // BridgeHandleSetTask parses; radius<10→40 clamp doesn't bite at 60).
            var cmd = new BridgeCommand("SET_TASK", new
            {
                task = "GRIND",
                x = snap.X,
                y = snap.Y,
                z = snap.Z,
                radius = GrindRadius,
                creature_entry = 0,
                kill_count = 0
            });
            return StepResult.Fire(cmd);   // no-WAIT — KILL stream is the only feedback
        }

        // Already armed; C++ owns the scan + kills. Nothing to issue.
        return StepResult.Wait();
    }

    public bool IsProgressing(BotContext ctx, BotStateSnapshot snap)
    {
        if (ctx.TimeInGoalSec < ArmGraceSec) return true;        // arm grace: let the patrol get going
        return (DateTime.UtcNow - ctx.LastKillUtc).TotalSeconds < KillRecencySec;  // REAL kills only (gated in BotExecutor)
    }

    public StallAction OnStall(BotContext ctx)
        => StallAction.Of(StallActionKind.ReselectGoal, "grind:no_kills");
}