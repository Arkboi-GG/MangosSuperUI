namespace MangosSuperUI.BotLogic.Core;

using MangosSuperUI.BotLogic.Data;   // QuestNode (quest scratch)

// ============================================================================
// BotContext — THE live state. The keystone of the rebuild (§3.4).
//
// One per bot: the authoritative record of what the bot is doing and how it's
// going. The architecture fix AND the observability fix in one object — dump it
// (FleetReport) and you have the complete picture, no grep, no six log streams.
//
// Owned and mutated by the brain (BotBrain/BotExecutor). The Supervisor writes
// the verdict fields. Planners READ it and never mutate control state.
// ============================================================================

// ------------------------------ Geometry -----------------------------------
public readonly struct Vec3
{
    public float X { get; }
    public float Y { get; }
    public float Z { get; }
    public Vec3(float x, float y, float z) { X = x; Y = y; Z = z; }

    public float Dist2D(Vec3 o) { float dx = X - o.X, dy = Y - o.Y; return MathF.Sqrt(dx * dx + dy * dy); }
    public float Dist3D(Vec3 o) { float dx = X - o.X, dy = Y - o.Y, dz = Z - o.Z; return MathF.Sqrt(dx * dx + dy * dy + dz * dz); }

    public override string ToString() => $"({X:F0},{Y:F0},{Z:F0})";
}

public readonly struct Vec4
{
    public float X { get; }
    public float Y { get; }
    public float Z { get; }
    public int Map { get; }
    public Vec4(float x, float y, float z, int map) { X = x; Y = y; Z = z; Map = map; }

    public Vec3 Pos => new(X, Y, Z);
    public override string ToString() => $"({X:F0},{Y:F0},{Z:F0})@{Map}";
}

// ---------------------------- The WAIT (§3.5) ------------------------------
// Every command BotBrain issues sets ctx.Pending. The matching outcome clears
// it and stamps a progress timer. The Supervisor's first, universal stall rule
// is DeadlineUtc exceeded — independent of any domain.
public sealed class Outstanding
{
    public string CommandType { get; init; } = "";
    public string ExpectedEvent { get; init; } = "";
    public DateTime SentUtc { get; init; }
    public DateTime DeadlineUtc { get; init; }

    public double AgeSec => (DateTime.UtcNow - SentUtc).TotalSeconds;
    public bool Expired => DateTime.UtcNow > DeadlineUtc;
}

// ---------------------- Negative-ack outcome (§3.5b) -----------------------
// A pending WAIT can be resolved two ways: by its positive ack (ExpectedEvent),
// or NEGATED by a failure event that means "this won't complete" — MOVE_FAILED /
// PATH_UNSAFE (a MOVE_TO WAIT) or QUEST_INTERACT_FAIL (a QUEST_INTERACT WAIT).
// The executor clears the WAIT and stamps this so the planner escapes PROMPTLY
// (PlanNext → Blocked(reason) → OnStall) instead of waiting out the deadline
// (the no_path-plateau fix). The brain clears it once consumed.
public sealed class WaitFailure
{
    public string CommandType { get; init; } = "";   // the command that failed (MOVE_TO / QUEST_INTERACT)
    public string Reason { get; init; } = "";          // no_path / no_progress / empty_path / cross_map / path_unsafe / interact reason
    public Vec4? Dest { get; init; }                    // failed destination (for blacklist / last-leg force detection)
    public int DangerLevel { get; init; }               // PATH_UNSAFE danger_level (0 if n/a)
    public int? QuestId { get; init; }                  // QUEST_INTERACT_FAIL quest_id (if carried)
    public DateTime Utc { get; init; }

    public double AgeSec => (DateTime.UtcNow - Utc).TotalSeconds;
}

// --------------------- Typed goal scratch (§3.4) ---------------------------
// TYPED per goal — never an untyped bag. This is what replaces PhaseData.
// Only the active goal's scratch is populated; the rest stay null.
public sealed class QuestScratch
{
    public int QuestId { get; set; }
    public QuestNode? Node { get; set; }                  // the quest being worked (from QuestGraphLoader)
    public bool Accepted { get; set; }                    // QUEST_ACCEPT_ACK received
    public bool ForceMode { get; set; }                   // WMO-interior NPC: interact via force_* (proximity bypassed)
    public int ObjectiveSlot { get; set; }                // cursor into Node.Objectives for the kill objective in progress
    public List<int> ActiveQuestIds { get; } = new();     // FleetReport q=[...] display
}

public sealed class GrindScratch
{
    public int CreatureEntry { get; set; }
    public Vec4 AreaCenter { get; set; }
    public float Radius { get; set; }
    public int KillGoal { get; set; }                     // 0 = indefinite grind (never TASK_COMPLETEs)
    public int KillCount { get; set; }
}

public sealed class ServiceScratch
{
    public int TargetNpcEntry { get; set; }               // vendor or trainer
    public Vec4 TargetPos { get; set; }
    public List<int> ToLearn { get; } = new();            // spell ids queued to train
    public long GoldNeeded { get; set; }                  // > 0 when gold-blocked
    public Dictionary<string, DateTime> Cooldowns { get; } = new();
}

// ----------------------------- Group (Phase 5) -----------------------------
// GroupRole is new; GroupDirective already exists in BotIdentity.cs (same Core
// namespace) with None/Questing/HoldAndGrind/Regroup/GroupErrand — reuse it so
// BotContext.Directive and BotIdentity.GroupDirective share one source of truth.
public enum GroupRole { None, Leader, Member }

// =============================== BotContext =================================
public sealed class BotContext
{
    // ---- identity snapshot ----
    public int Guid { get; init; }
    public string Name { get; set; } = "";
    public int Level { get; set; }

    // ---- durable roster back-ref (set once by the host at seed) ----
    // Quest completed-set / deferrals / path blacklist live on BotIdentity (durable,
    // survive reconnect). The quest planner READS them to pick+filter and routes
    // defer/abandon/blacklist through BotIdentity's own methods. Null until seeded.
    public BotIdentity? Identity { get; set; }

    // ---- intent ----
    public Goal Goal { get; private set; } = Goal.Idle;   // current high-level intent
    public string Step { get; private set; } = "idle";    // explicit step within the goal
    public Vec4? Target { get; set; }                     // x,y,z,map the brain is driving toward

    // ---- THE WAIT — the observability spine ----
    public Outstanding? Pending { get; set; }

    // ---- last negative outcome (set by the executor when a WAIT is negated; consumed by the brain) ----
    public WaitFailure? Failure { get; set; }

    // ---- progress ----
    public DateTime LastProgressUtc { get; set; } = DateTime.UtcNow;  // last forward motion of ANY kind
    public DateTime LastKillUtc { get; set; }
    public DateTime LastQuestAdvanceUtc { get; set; }
    public DateTime LastLevelUtc { get; set; }
    public float LastPosDelta { get; set; }
    public Vec3 LastPosRef { get; set; }                  // ping-pong / no-progress detection

    // ---- step / goal timing ----
    public DateTime GoalSinceUtc { get; private set; } = DateTime.UtcNow;
    public DateTime StepSinceUtc { get; private set; } = DateTime.UtcNow;

    // ---- verdict (written by the Supervisor) ----
    public bool Stalled { get; set; }
    public string StallReason { get; set; } = "";
    public DateTime StalledSinceUtc { get; set; }

    // ---- sensory (refreshed each tick from BotStateSnapshot) ----
    public Vec3 Pos { get; set; }
    public int MapId { get; set; }
    public int ZoneId { get; set; }
    public float HpPct { get; set; } = 1f;
    public float ManaPct { get; set; } = 1f;
    public int FreeSlots { get; set; }
    public long Copper { get; set; }
    public bool InCombat { get; set; }
    public bool Dead { get; set; }

    // ---- goal scratch (typed; only the active goal's is populated) ----
    public QuestScratch? Quest { get; set; }
    public GrindScratch? Grind { get; set; }
    public ServiceScratch? Service { get; set; }

    // ---- group (Phase 5 — empty until then) ----
    public int? GroupId { get; set; }
    public GroupRole Role { get; set; } = GroupRole.None;
    public int? LeaderGuid { get; set; }
    public GroupDirective Directive { get; set; } = GroupDirective.None;
    public Vec4? Anchor { get; set; }

    // ----------------------------- helpers ---------------------------------
    public double TimeInGoalSec => (DateTime.UtcNow - GoalSinceUtc).TotalSeconds;
    public double TimeInStepSec => (DateTime.UtcNow - StepSinceUtc).TotalSeconds;
    public double TimeSinceProgressSec => (DateTime.UtcNow - LastProgressUtc).TotalSeconds;
    public float DistToTarget => Target.HasValue ? Pos.Dist2D(Target.Value.Pos) : -1f;

    /// <summary>Switch the high-level goal, resetting step + timers. Scratch is the brain's to swap.</summary>
    public void SetGoal(Goal goal, string step)
    {
        if (Goal != goal) GoalSinceUtc = DateTime.UtcNow;
        Goal = goal;
        SetStep(step);
    }

    /// <summary>Move to a new step within the current goal; stamps the step timer on change.</summary>
    public void SetStep(string step)
    {
        if (Step != step) StepSinceUtc = DateTime.UtcNow;
        Step = step;
    }

    /// <summary>Stamp generic forward progress (resets the no-progress clock).</summary>
    public void MarkProgress() => LastProgressUtc = DateTime.UtcNow;

    /// <summary>Refresh the sensory fields from the latest bridge snapshot. No control logic here.</summary>
    public void Sense(BotStateSnapshot snap)
    {
        Level = snap.Level;
        Pos = new Vec3(snap.X, snap.Y, snap.Z);
        MapId = snap.MapId;
        ZoneId = snap.ZoneId;
        HpPct = snap.HealthPercent;
        ManaPct = snap.ManaPercent;
        FreeSlots = (int)snap.FreeSlots;
        Copper = snap.Copper;
        InCombat = snap.InCombat;
        Dead = snap.IsDead;
    }
}