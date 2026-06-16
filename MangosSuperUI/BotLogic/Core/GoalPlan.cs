namespace MangosSuperUI.BotLogic.Core;

// ============================================================================
// GoalPlan — the FROZEN planner contracts (§3.1 / §3.2 / §3.3).
// Build TO these. Changing any signature here bumps the rebuild-doc version.
//
// The brain never inspects a planner's internals. A planner is a stateless
// ADVISOR: given the live context + the latest snapshot it answers "what next"
// (PlanNext → StepResult) and "am I progressing" (IsProgressing / OnStall).
// It never owns the bot, never sequences, never runs a timer, never WAITs.
// ============================================================================

// ---------------------------- Goal (§3.1) ----------------------------------
// Replaces the ActivityType grab-bag. One goal at a time per bot; the bot's
// whole high-level intent is ctx.Goal.
public enum Goal
{
    Idle,
    Questing,
    Grinding,
    Vendoring,
    Training,
    Maintenance,
    Following,
    Exploring,
    Socializing
}

// ------------------------- StepResult (§3.2) -------------------------------
// The only thing a planner returns. The brain acts on exactly one of these.
public abstract class StepResult
{
    /// <summary>Working; nothing to issue this tick.</summary>
    public sealed class Continue : StepResult
    {
        public static readonly Continue Instance = new();
        private Continue() { }
    }

    /// <summary>
    /// Send this command and arm a WAIT. The brain issues it (via BotExecutor)
    /// and records the outstanding command on the context; ExpectedEvent is the
    /// wire event-name the WAIT keys on (e.g. "TASK_COMPLETE"); Deadline bounds
    /// it (see §5b.1 for per-command values). The Supervisor's deadline rule then
    /// watches it until the matching event clears it.
    /// </summary>
    public sealed class Issue : StepResult
    {
        public BridgeCommand Command { get; }
        public string ExpectedEvent { get; }
        public TimeSpan Deadline { get; }
        public Issue(BridgeCommand command, string expectedEvent, TimeSpan deadline)
        {
            Command = command;
            ExpectedEvent = expectedEvent;
            Deadline = deadline;
        }
    }

    /// <summary>
    /// Send this command WITHOUT arming a WAIT (Phase-2 addition). For indefinite,
    /// unacked tasks — SET_TASK GRIND with kill_count=0 emits no TASK_COMPLETE; its
    /// only liveness signal is the KILL stream — so there is no Pending to expire
    /// (which would false-stall a killing bot) and the planner's IsProgressing owns
    /// liveness instead (§6.2 / §6.3). Fire-and-forget.
    /// </summary>
    public sealed class Dispatch : StepResult
    {
        public BridgeCommand Command { get; }
        public Dispatch(BridgeCommand command) { Command = command; }
    }

    /// <summary>Goal achieved; the brain picks the next goal.</summary>
    public sealed class Done : StepResult
    {
        public static readonly Done Instance = new();
        private Done() { }
    }

    /// <summary>
    /// Cannot proceed (no_quests, no_path, gold_blocked, …). The brain / Supervisor
    /// decides what to do (defer / abandon / reselect).
    /// </summary>
    public sealed class Blocked : StepResult
    {
        public string Reason { get; }
        public Blocked(string reason) { Reason = reason; }
    }

    // ---- ergonomic factories (so planner code reads cleanly) ----
    public static StepResult Wait() => Continue.Instance;
    public static StepResult Send(BridgeCommand cmd, string expectedEvent, TimeSpan deadline)
        => new Issue(cmd, expectedEvent, deadline);
    public static StepResult Fire(BridgeCommand cmd) => new Dispatch(cmd);
    public static StepResult Complete() => Done.Instance;
    public static StepResult Block(string reason) => new Blocked(reason);
}

// ------------------------- StallAction (§3.3) ------------------------------
// What a planner's OnStall returns; the Supervisor enforces the result.
public enum StallActionKind
{
    Reroute,
    ReselectGoal,
    Defer,
    Abandon,
    ForceInteract,
    EscalateRez,
    GiveUpStop
}

public readonly struct StallAction
{
    public StallActionKind Kind { get; }
    public string Detail { get; }
    public StallAction(StallActionKind kind, string detail = "")
    {
        Kind = kind;
        Detail = detail;
    }
    public static StallAction Of(StallActionKind kind, string detail = "") => new(kind, detail);
}

// ------------------------- IBotPlanner (§3.3) ------------------------------
// Replaces IBotDomain. The planner defines what "progress" MEANS for its goal;
// the Supervisor runs that predicate centrally and enforces the result.
public interface IBotPlanner
{
    /// <summary>Which goal this planner serves.</summary>
    Goal Handles { get; }

    /// <summary>The next concrete step for the bot, given the live context + snapshot.</summary>
    StepResult PlanNext(BotContext ctx, BotStateSnapshot snap);

    /// <summary>Supervisor calls every tick: is this goal making forward progress?</summary>
    bool IsProgressing(BotContext ctx, BotStateSnapshot snap);

    /// <summary>Supervisor calls when progress stops: what corrective action to force.</summary>
    StallAction OnStall(BotContext ctx);
}