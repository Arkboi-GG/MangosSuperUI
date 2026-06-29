namespace MangosSuperUI.BotLogic.Core;

// ============================================================================
// Objective — the HELD strategic assignment (Held-Objective build §2).
//
// One per bot, on BotContext.Held. It sits ABOVE Goal and OUTLIVES the leg-level
// WAIT (ctx.Pending) and goal bounces: a planner re-arms its scratch and the brain
// fires SET_TASK IDLE on every Grinding/Questing exit, but the bot's COMMITTED
// objective survives that churn until it is genuinely done, reassigned, or capped.
//
// Two jobs:
//   • Selection: GoalSelector consults it so a committed bot STAYS on its objective
//     instead of re-deriving from the pick filter every tick (the per-tick blink the
//     2026-06-26 diagnosis pinned). "Have an objective → don't re-pick."
//   • Reconcile: BotBrain compares it against the C++ task echo (HeldTaskEcho, the
//     mirror of m_currentTask shipped on STATE). If C++ has dropped or never adopted
//     the objective (echo says IDLE / a different task) the brain knocks out the
//     in-flight guards so the planner RE-ISSUES the realizing leg — the self-heal for
//     the SET_TASK IDLE strand (a goal bounce idles C++ while C# still holds the
//     objective; the old change-guard then suppressed re-issue → 31,043 dead ticks).
//
// A readonly record struct so equality is structural and free (the change-guard
// re-stamp check), mirroring CombatDirective / ExecDirective / GroupOrder. Assign a
// new value to (re)commit; compare to detect a change; never mutate in place.
//
// NOTE (build order): nothing STAMPS Held in the foundation movement — the producers
// (GrindPlanner / QuestPlanner / TrainingPlanner / GroupCoordinator) wire it in the
// next movement. Until then Held is always null and every consult/reconcile branch is
// inert (provably byte-for-byte today's behavior).
// ============================================================================

// What KIND of strategic task the bot holds. Only Grind and Travel are re-issuable by
// the reconcile (NeedsActuation) — Hold/Idle are passive and a mismatch on them is
// harmless (the bot is meant to be sitting). None = no objective held.
public enum ObjectiveKind
{
    None = 0,
    Grind,      // grind CreatureEntry at Target until KillCount / until reassigned (enriched MOVE_TO)
    Travel,     // travel to Target (optionally to interact with NpcEntry on arrival)
    Hold,       // hold at Target (group HoldAtAnchor — sit / grind the latched mob)
    Idle        // explicit idle
}

// Where the objective came from — drives WAIT-vs-Fire on a reconcile re-issue and keeps
// the provenance observable. SelfSolo = the bot's own planner committed it; Coordinator =
// the GroupCoordinator assigned the shared objective.
public enum ObjectiveSource { None = 0, SelfSolo, Coordinator }

public readonly record struct Objective
{
    public ObjectiveKind Kind { get; }
    public ObjectiveSource Source { get; }

    // Target — grind coords / travel dest (Vec4 carries the map). Default for Idle.
    public Vec4 Target { get; }

    // Grind payload: the mob and the count owed (>=1 for a counted solo grind; 0 = an
    // indefinite/sentinel grind, e.g. the group shared objective whose completion is the
    // coordinator's server-count gate, never a local count).
    public int CreatureEntry { get; }
    public int KillCount { get; }

    // Provenance / readback keys: the quest+slot this objective realizes (0 = solo grind /
    // none). NpcEntry is the giver / ender / trainer for a Travel-to-interact (0 otherwise).
    public int QuestId { get; }
    public int Slot { get; }
    public int NpcEntry { get; }

    public Objective(ObjectiveKind kind, ObjectiveSource source, Vec4 target,
        int creatureEntry, int killCount, int questId, int slot, int npcEntry)
    {
        Kind = kind;
        Source = source;
        Target = target;
        CreatureEntry = creatureEntry;
        KillCount = killCount;
        QuestId = questId;
        Slot = slot;
        NpcEntry = npcEntry;
    }

    public bool IsActive => Kind != ObjectiveKind.None;

    /// <summary>True for the long-running task kinds the reconcile can RE-ISSUE (Grind / Travel).
    /// Hold / Idle are passive — a C++ mismatch on them is benign, so the reconcile leaves them be.</summary>
    public bool NeedsActuation => Kind == ObjectiveKind.Grind || Kind == ObjectiveKind.Travel;

    public static readonly Objective None =
        new(ObjectiveKind.None, ObjectiveSource.None, default, 0, 0, 0, 0, 0);

    // ---- factories (the producers stamp one of these on ctx; Movement 2) ----

    /// <summary>Grind CreatureEntry at (x,y,z,map) until killCount (0 = indefinite/sentinel).
    /// questId/slot = the quest objective it realizes (0 for a pure solo grind).</summary>
    public static Objective Grind(ObjectiveSource source, int creatureEntry,
        float x, float y, float z, int map, int killCount, int questId = 0, int slot = 0)
        => new(ObjectiveKind.Grind, source, new Vec4(x, y, z, map),
               creatureEntry, killCount, questId, slot, 0);

    /// <summary>Travel to (x,y,z,map); npcEntry > 0 = an interact-on-arrival (giver/ender/trainer).</summary>
    public static Objective Travel(ObjectiveSource source, float x, float y, float z, int map,
        int npcEntry = 0, int questId = 0)
        => new(ObjectiveKind.Travel, source, new Vec4(x, y, z, map), 0, 0, questId, 0, npcEntry);

    /// <summary>Hold at the anchor (group HoldAtAnchor). Passive — not reconciled.</summary>
    public static Objective Hold(Vec4 anchor)
        => new(ObjectiveKind.Hold, ObjectiveSource.Coordinator, anchor, 0, 0, 0, 0, 0);

    /// <summary>Does C++'s reported task correspond to THIS held objective? Used by the reconcile to
    /// decide "C++ is on the right TASK" (true → leave it; the Activity stream + §5 progress checks own
    /// the rest) vs "C++ has dropped / never adopted it" (false → re-issue). This is the TASK-IDENTITY
    /// check ONLY — whether C++ is making HEADWAY is the separate Activity stream, never elapsed time.
    /// Only meaningful for NeedsActuation kinds.</summary>
    public bool MatchedBy(HeldTaskEcho echo, float destTolYards = 25f)
    {
        switch (Kind)
        {
            case ObjectiveKind.Grind:
                // C++ is GRINDING the mob in place (arrived + handed off to TASK_GRIND) — steady state.
                if (echo.Kind == HeldTaskKind.Grind
                    && (CreatureEntry == 0 || echo.CreatureEntry == 0 || echo.CreatureEntry == CreatureEntry))
                    return true;
                // OR C++ is still TRAVELING the ENRICHED MOVE_TO toward the grind coords. A solo grind
                // objective is realized by ONE enriched MOVE_TO (creature_entry+kill_count) that travels to
                // the dest, THEN ConvertMoveToGrindInPlace hands off to TASK_GRIND on arrival / scan-hit. So
                // for the WHOLE approach C++ correctly reports taskKind=MOVE_TO toward Target — that is the
                // objective IN PROGRESS, not a dropped task. Without this clause the reconcile re-issues
                // (re-paths) the bot every STATE tick for the entire travel leg — the observed crawl+WEDGE.
                if (echo.Kind == HeldTaskKind.MoveTo
                    && echo.Dest.Map == Target.Map
                    && echo.Dest.Pos.Dist2D(Target.Pos) <= destTolYards)
                    return true;
                return false;
            case ObjectiveKind.Travel:
                // C++ is moving toward our destination (same map, within tolerance).
                return echo.Kind == HeldTaskKind.MoveTo
                       && echo.Dest.Map == Target.Map
                       && echo.Dest.Pos.Dist2D(Target.Pos) <= destTolYards;
            default:
                return true;   // Hold / Idle / None are never reconciled as mismatched
        }
    }
}

// ----------------------- C++ held-task echo (§4 — the seam) ----------------
// The mirror of C++ m_currentTask, shipped on the 5s STATE message (Session 3) and parsed
// into BotStateSnapshot.HeldTask, then copied onto BotContext.HeldTask by Sense each tick.
// Unknown until the C++ writer lands — the reconcile treats Unknown as "no readback → today's
// behavior", so the whole loop is a safe no-op until the wire half ships. This is the ONE
// contract both build sessions must agree on byte-for-byte (the task_* STATE field names).
//
// PHILOSOPHY (Nico, 2026-06-27): we DON'T infer failure from elapsed time — we STREAM what is
// happening within the objective and react to STATE. C++ already knows whether the bot is
// fighting, eating, hunting a target, or genuinely stuck; this carries that as Activity. A bot
// that pulls something tough, peels off to eat, and re-engages over 3 minutes is PRODUCTIVE
// (Engaged → Recovering → Engaged), NOT stalled — the old clock-based wedge would have yanked it;
// the Activity stream forgives it. Only Blocked (C++ tried and can't proceed) is a react-now
// signal; the wall-clock cap is demoted to a forgiving last-resort seatbelt that only catches a
// C++ hang that never even reports Blocked.
public enum HeldTaskKind
{
    Unknown = 0,   // C++ sent no echo (old binary / field absent) → reconcile is a no-op
    Idle,          // SET_TASK IDLE (no task) — the strand signal when we hold a Grind/Travel
    MoveTo,        // a plain or enriched MOVE_TO toward Dest
    Grind,         // SET_TASK GRIND / enriched-grind-in-place on CreatureEntry
    Interact       // parked at an NPC for an interaction
}

// What the bot is DOING within its current task right now — the headway signal C++ derives for
// free from its own UpdateAI state (victim / eat-drink / SelectGrindTarget / MOVE_FAILED).
// This, not a timeout, is what triage keys on (§5).
public enum TaskActivity
{
    Unknown = 0,
    Idle,          // no task / wandering
    Traveling,     // moving toward the destination (productive)
    Searching,     // grind task, hunting for a valid target (productive short-term; a long dwell = no mobs → relocate)
    Engaged,       // in combat, has a victim (productive — a hard fight is NOT a stall)
    Recovering,    // eating / drinking / regen after a fight (productive recovery)
    Blocked        // tried and can't proceed (no_path / no target found / stuck) — the REAL stall, react NOW
}

public readonly record struct HeldTaskEcho
{
    public HeldTaskKind Kind { get; }
    public TaskActivity Activity { get; }   // the within-objective state — what triage reacts to
    public int CreatureEntry { get; }   // the grind mob C++ is actually on (0 = "nearest valid")
    public Vec4 Dest { get; }           // the MOVE_TO destination C++ is actually heading to
    public int Kills { get; }           // kills credited on the current grind so far (pushed progress)

    public HeldTaskEcho(HeldTaskKind kind, TaskActivity activity, int creatureEntry, Vec4 dest, int kills)
    {
        Kind = kind;
        Activity = activity;
        CreatureEntry = creatureEntry;
        Dest = dest;
        Kills = kills;
    }

    /// <summary>A real readback is present (C++ shipped the task_* fields). False = pre-Session-3
    /// binary / absent echo → the reconcile degrades to today's ctx.Pending-only behavior.</summary>
    public bool IsKnown => Kind != HeldTaskKind.Unknown;

    /// <summary>C++ is making headway on the task — a hard fight, recovery, travel, or active hunt.
    /// Triage FORGIVES this regardless of how long it takes (the anti-wedge: a 3-minute fight-eat-
    /// re-engage cycle reads as productive, not failed). Searching is productive short-term; a long
    /// Searching dwell is the "no mobs here → relocate" case the §5 triage handles separately.</summary>
    public bool IsProductive =>
        Activity is TaskActivity.Engaged or TaskActivity.Recovering
                 or TaskActivity.Traveling or TaskActivity.Searching;

    /// <summary>C++ has tried and genuinely can't proceed — the only REACT-NOW stall signal (vs a
    /// clock guess). Triage acts on this immediately (no timeout): reposition / drop / re-issue.</summary>
    public bool IsBlocked => Activity == TaskActivity.Blocked;

    public static readonly HeldTaskEcho Unknown =
        new(HeldTaskKind.Unknown, TaskActivity.Unknown, 0, default, 0);
}