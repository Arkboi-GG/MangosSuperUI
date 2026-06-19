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
    public DateTime DeadlineUtc { get; set; }   // settable so the executor can PUSH it on KILL — progress-extending objective deadline (§6B.2)

    // True when this WAIT is an ENRICHED objective MOVE_TO (§4): a MOVE_TO that carries
    // creature_entry/kill_count, so C++ travels then GRINDS IN PLACE under one WAIT. The
    // initial deadline (TravelDeadline) covers the travel leg; once the bot arrives and
    // starts killing, the executor's KILL-push rolls the deadline forward on each kill —
    // exactly as it does for a SET_TASK grind — so a long grind never false-fails on the
    // travel ceiling. Set in BotExecutor.IssueAsync from the command payload.
    public bool IsObjectiveGrind { get; init; }

    // When set, this WAIT is an INTERRUPTIBLE leg (a quest trek): while it's still
    // pending, BotBrain calls the planner's Rescan on this cadence WITHOUT clearing the
    // WAIT, so newly-discovered work en route can preempt a long journey. Null = not
    // interruptible (the default — most legs ride to their ack/deadline untouched).
    // Settable so the brain can push it forward when the planner chooses to keep waiting.
    public DateTime? RescanAtUtc { get; set; }

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

// One quest inside the batch. CARRIED — accepted in the C++ quest log with its
// partial kill progress — until it is rewarded, goes grey (out-leveled), or, for
// the span of a single sweep, gets shelved (far outlier or a failed leg). The
// durable truth is the C++ log + QUEST_STATUS_ALL; this list is rebuilt from it
// on every (re)entry to Questing, so shelving survives goal bounces for free.
public sealed class BatchQuest
{
    public int QuestId { get; init; }
    public QuestNode Node { get; init; } = null!;
    public bool Accepted { get; set; }      // in the log (resumed) or QUEST_ACCEPT_ACK'd this run
    public bool TurnedIn { get; set; }       // QUEST_COMPLETE_ACK seen — dropped from the batch next derive
    public bool Deferred { get; set; }       // far outlier THIS sweep: skip; cleared on reprocess
    public bool Failed { get; set; }         // a leg failed THIS sweep: skip, keep accepted; cleared on fresh BuildBatch
    public bool ForceMode { get; set; }      // WMO-interior NPC: interact via force_*
}

// Batch quest scratch (§ P3 batching). Drives ALL quests the bot accepted in the
// current sweep, not one at a time:
//   gather → accept-all → objective-sweep (nearest-first, outlier-shelved) →
//   turn-in-all → reprocess (follow-ups + new locals re-evaluated) → ↺
public sealed class QuestScratch
{
    // The carried batch. Rebuilt from the C++ quest log on (re)entry.
    public List<BatchQuest> Batch { get; } = new();

    // The quest whose leg armed the current WAIT (to_giver/accept/to_objective/
    // to_turnin/turnin). Cleared when that leg's outcome is applied.
    public BatchQuest? Active { get; set; }
    public int ActiveSlot { get; set; }                  // objective index within Active.Node.Objectives

    // En-route re-gather throttle: re-scan for new local givers once the bot has
    // moved this far from where we last gathered (so a hub passed mid-sweep is caught).
    public Vec3 LastGatherPos { get; set; }

    // FleetReport display — the live batch ids.
    public List<int> ActiveQuestIds { get; } = new();

    // ---- back-compat read shims (anything that peeked the old single-quest scratch) ----
    public int QuestId => Active?.QuestId ?? (Batch.Count > 0 ? Batch[0].QuestId : 0);
    public QuestNode? Node => Active?.Node ?? (Batch.Count > 0 ? Batch[0].Node : null);
}

public sealed class GrindScratch
{
    public int CreatureEntry { get; set; }
    public Vec4 AreaCenter { get; set; }
    public float Radius { get; set; }
    public int KillGoal { get; set; }                     // 0 = indefinite grind (never TASK_COMPLETEs)
    public int KillCount { get; set; }
}

// Stage of a vendor/repair errand (driven by MaintenancePlanner under Goal.Maintenance,
// on ctx.Service). None = no errand in flight (the GoalSelector hold keys off this).
public enum VendorPhase { None, Route, Sell, Repair }

public sealed class ServiceScratch
{
    public int TargetNpcEntry { get; set; }               // vendor or trainer
    public Vec4 TargetPos { get; set; }
    public List<int> ToLearn { get; } = new();            // spell ids queued to train
    public long GoldNeeded { get; set; }                  // > 0 when gold-blocked
    public Dictionary<string, DateTime> Cooldowns { get; } = new();

    // ── Vendor/repair errand (MaintenancePlanner) ──
    public VendorPhase Phase { get; set; }                // route → sell → repair → done
    public bool CanRepair { get; set; }                   // selected vendor has UNIT_NPC_FLAG_REPAIR
    public DateTime StartedUtc { get; set; }              // trip start — drives the never-arrived give-up
}

// Death-recovery scratch (Goal.Maintenance). Transient per death: armed by
// MaintenancePlanner on the first dead tick, nulled by the brain on goal (re)entry.
// Death-LOOP detection is NOT here (it must survive this reset) — it rides durable
// BotIdentity fields (LastDeathTime / LastDeathLocation / RecordDeath / PathBlacklist).
//
// Recovery is a three-phase machine: REZ (corpse-run delay → RESURRECT; at_graveyard on
// a same-spot loop), then RELOCATE (a best-effort 25yd MOVE_TO to safer ground when the
// rez cell has hostile spawns — ported from the old MaintenanceDomain.FindSafeRezSpot,
// but run while ALIVE since a ghost can't move on this binary), then HEAL-TO-FULL
// (SET_TASK IDLE + poll STATE.health). The heal phase is the survival fix: C++ rezzes the
// bot at 50% HP, and a TASKED bot only tops off below 40% while the grind patrol breaks
// the eat channel — so re-engaging at 50% is the death spiral. An IDLE bot below 100%
// eats every tick and never wanders, so we hold it IDLE until ~full, THEN release.
public sealed class MaintenanceScratch
{
    public DateTime DeadSinceUtc { get; set; }            // entered recovery — drives the dead-time backstop
    public DateTime RezAtUtc { get; set; }                // when the corpse-run delay elapses → send RESURRECT
    public Vec4 DeathPos { get; set; }                    // where we died (death-spot blacklist target on a loop)
    public bool DeathLoop { get; set; }                   // quick SAME-SPOT re-death → escalate (blacklist + at_graveyard port)
    public bool RezSent { get; set; }                     // RESURRECT issued — guards against duplicate sends
    public bool Escalated { get; set; }                   // death-spot blacklisted + at_graveyard sent (once per recovery)

    // ── Post-rez phases (alive) ──
    public bool Rezzed { get; set; }                      // bot was seen ALIVE after RESURRECT — a later dead tick = a re-death → re-arm
    public bool RelocateSent { get; set; }                // safe-spot MOVE_TO issued once (best-effort, ported FindSafeRezSpot)
    public bool RelocateDone { get; set; }                // relocate finished or skipped → heal next
    public bool IdleFired { get; set; }                   // SET_TASK IDLE sent once for the heal phase
    public DateTime HealSinceUtc { get; set; }            // heal phase entered — drives the heal timeout backstop
    public bool HealDone { get; set; }                    // healed (or timed out) → recovery releases to the GoalSelector
}

// One quest's authoritative server-side state, parsed from QUEST_STATUS_ALL (the
// C++ reply to QUERY_QUEST_STATUS). Lets the QuestPlanner RESUME an in-log quest
// after a death/restart instead of re-accepting it (which C++ rejects → zombie).
public sealed class QuestLogEntry
{
    public int Status { get; set; }                       // VMaNGOS QUEST_STATUS — COMPLETE=1, INCOMPLETE=3 (counterintuitive; NONE=0, UNAVAILABLE=2)
    public int[] MobCounts { get; set; } = new int[4];    // per-slot kill counts, indexed by (QuestObjective.Slot - 1)
    public int[] ItemCounts { get; set; } = new int[4];   // per-slot item counts, indexed by (QuestItemReq.Slot - 1)
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

    // Why the GoalSelector chose the current goal — set every tick by the arbitration so
    // FleetReport can explain it (e.g. "q av=12 pick=0", "no-identity"). The decision is
    // first-class observable state, not a throwaway log.
    public string GoalReason { get; set; } = "";

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
    public int Durability { get; set; } = 100;            // min equipped-slot durability % (100 = full); drives the repair errand
    public bool InCombat { get; set; }
    public bool Dead { get; set; }

    // ---- goal scratch (typed; only the active goal's is populated) ----
    public QuestScratch? Quest { get; set; }
    public GrindScratch? Grind { get; set; }
    public ServiceScratch? Service { get; set; }
    public MaintenanceScratch? Maintenance { get; set; }

    // ---- quest-log cache (refreshed by QUEST_STATUS_ALL; read by QuestPlanner to resume) ----
    // Reference-swapped by the executor (not mutated in place) so the planner can read a
    // stable snapshot without locking. Stamp = when it was last refreshed.
    public Dictionary<int, QuestLogEntry> QuestLog { get; set; } = new();
    public DateTime QuestLogStampUtc { get; set; }

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
        Durability = (int)snap.Durability;
        InCombat = snap.InCombat;
        Dead = snap.IsDead;
    }
}