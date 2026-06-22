using MangosSuperUI.BotLogic.Core;

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

    public GrindPlanner(ILogger<GrindPlanner> log)
    {
        _log = log;
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