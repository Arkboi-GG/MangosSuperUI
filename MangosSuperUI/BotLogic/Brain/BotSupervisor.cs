using MangosSuperUI.BotLogic.Core;
using MangosSuperUI.BotLogic.Tracking;

namespace MangosSuperUI.BotLogic.Brain;

// ============================================================================
// BotSupervisor — the monitor with teeth (§4).
//
// Reads BotContext, runs progress predicates, and (from Phase 2) forces
// corrective action when a goal stops progressing. It writes the verdict fields
// on the context; it never drives the bot directly.
//
// Phase 1 implements ONLY the universal, domain-independent stall rule (§3.5):
// an outstanding WAIT whose DeadlineUtc has passed. That single rule needs no
// planner and catches the most common failure — a command issued and never
// acked. Per-goal progress predicates (IBotPlanner.IsProgressing) and forced
// correction (OnStall → StallAction) are layered on in Phases 2+, right here.
// ============================================================================
public sealed class BotSupervisor
{
    private readonly ILogger<BotSupervisor> _logger;

    public BotSupervisor(ILogger<BotSupervisor> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Run the stall check for one bot this tick. Writes the verdict onto the
    /// context (Stalled / StallReason / StalledSinceUtc). Returns true if stalled.
    /// </summary>
    public bool Check(BotContext ctx, BotStateSnapshot snap)
    {
        // Universal rule: an outstanding command past its deadline is a stall.
        var pending = ctx.Pending;
        if (pending != null && pending.Expired)
        {
            CircuitTrace.Hit(ctx.Guid, "supervisor: WAIT past deadline -> stall tripped");
            Trip(ctx, $"deadline:{pending.ExpectedEvent}");
            return true;
        }

        // No active stall condition — clear any prior verdict.
        if (ctx.Stalled)
        {
            CircuitTrace.Hit(ctx.Guid, "supervisor: stall cleared");
            _logger.LogDebug("[SUPV] {Name} stall cleared (was {Reason})", ctx.Name, ctx.StallReason);
            ctx.Stalled = false;
            ctx.StallReason = "";
            ctx.StalledSinceUtc = default;
        }
        return false;
    }

    private void Trip(BotContext ctx, string reason)
    {
        if (ctx.Stalled && ctx.StallReason == reason) { CircuitTrace.Hit(ctx.Guid, "supervisor: already tripped on this reason"); return; }

        CircuitTrace.HitNote(ctx.Guid, "supervisor: stall verdict written", reason);
        ctx.Stalled = true;
        ctx.StallReason = reason;
        ctx.StalledSinceUtc = DateTime.UtcNow;
        _logger.LogWarning("[SUPV] {Name} STALL {Reason} (goal={Goal} step={Step})",
            ctx.Name, reason, ctx.Goal, ctx.Step);
    }
}
