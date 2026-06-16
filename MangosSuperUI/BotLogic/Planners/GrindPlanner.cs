using MangosSuperUI.BotLogic.Core;

namespace MangosSuperUI.BotLogic.Planners;

// ============================================================================
// GrindPlanner — Goal.Grinding (§6, Phase 2).
//
// Ported from CombatDomain's solo-grind half (the anchor/GroupDirective half is
// Phase 5 — stripped here). Thin: there is no C# combat loop. It fires ONE
// SET_TASK GRIND and C++ autonomously patrols + kills (DoGrindPatrol hops within
// the radius every 3–6s, so a target-less grind never stands still). The grind
// is indefinite + unacked (kill_count=0 ⇒ no TASK_COMPLETE), so it must carry NO
// WAIT — it is fired no-WAIT (StepResult.Fire). KILL events (stamped on the
// context by the executor) are the only liveness signal.
//
// "Armed" = ctx.Grind != null (GrindScratch has no Armed flag). Re-arm happens
// when the brain resets ctx.Grind = null on goal (re)entry.
// ============================================================================
public sealed class GrindPlanner : IBotPlanner
{
    private const float GrindRadius = 60f;       // CombatDomain solo-grind radius
    private const double ArmGraceSec = 45;       // grace after entry before KILL-recency applies
    private const double KillRecencySec = 120;   // a killing bot is progressing

    public Goal Handles => Goal.Grinding;

    public StepResult PlanNext(BotContext ctx, BotStateSnapshot snap)
    {
        if (ctx.Grind == null)
        {
            // Arm the grind centered on the bot's current position.
            ctx.Grind = new GrindScratch
            {
                CreatureEntry = 0,                                       // kill anything hostile
                AreaCenter = new Vec4(snap.X, snap.Y, snap.Z, snap.MapId),
                Radius = GrindRadius,
                KillGoal = 0,                                           // indefinite — never TASK_COMPLETEs
                KillCount = 0
            };
            ctx.SetStep("grind");

            // Wire shape ported 1:1 from CombatDomain.BuildGrind (snake_case keys
            // the C++ BridgeHandleSetTask parses; radius<10→40 clamp doesn't bite at 60).
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

        // Already armed; C++ owns the patrol + kills. Nothing to issue.
        return StepResult.Wait();
    }

    public bool IsProgressing(BotContext ctx, BotStateSnapshot snap)
    {
        if (ctx.TimeInGoalSec < ArmGraceSec) return true;   // arm grace: let the patrol get going
        return (DateTime.UtcNow - ctx.LastKillUtc).TotalSeconds < KillRecencySec;
    }

    public StallAction OnStall(BotContext ctx)
        => StallAction.Of(StallActionKind.ReselectGoal, "grind:no_kills");
}
