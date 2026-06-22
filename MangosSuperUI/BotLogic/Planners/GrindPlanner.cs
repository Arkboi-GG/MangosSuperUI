using MangosSuperUI.BotLogic.Core;
using MangosSuperUI.BotLogic.Data;

namespace MangosSuperUI.BotLogic.Planners;

// ============================================================================
// GrindPlanner — Goal.Grinding (§6, Phase 2). Now AWARE.
//
// Old behavior: fire SET_TASK GRIND once at the bot's current position, kill-anything
// in 50yd, and on no-kills reselect re-arm at the SAME spot — which spins forever in a
// barren/town cell (the Ome "farm a chicken for an hour" case). The bounce wipes
// ctx.Grind, so the original couldn't even count its own failures.
//
// New behavior: before arming, read the ZoneSafetyMap cell the bot is standing in
// (cells already exclude critters / guards / service NPCs). If it's barren / grey / red /
// hyper-contested, ring-search for the nearest LEVEL-APPROPRIATE cell and relocate there
// (stop patrol → MOVE_TO → re-arm), then grind. Relocate phase state is durable on
// BotIdentity because the reselect bounce wipes GrindScratch, and IsProgressing returns
// true while relocating so the move isn't bounce-wiped mid-flight.
//
// "Armed" = ctx.Grind != null. Re-arm happens when the brain nulls ctx.Grind on (re)entry.
// ============================================================================
public sealed class GrindPlanner : IBotPlanner
{
    private const float GrindRadius = 60f;       // CombatDomain solo-grind radius
    private const double ArmGraceSec = 45;       // grace after entry before KILL-recency applies
    private const double KillRecencySec = 120;   // a killing bot is progressing

    // --- aware-grind tuning (BotTuning candidates) ---
    private const int GrindLevelLowOffset = 5;   // accept a cell whose AVG level ≥ L-5 (XP, not grey)
    private const int GrindLevelHighOffset = 2;   // …and ≤ L+2 (worth XP, not a wall)
    private const int GrindDangerCeil = 3;   // reject a cell whose MAX level > L+3 (a red mob lives there)
    private const int GrindMinSpawn = 1;   // a cell needs ≥1 aggressive spawn to be worth standing in
    private const int GrindMaxSpawn = 40;  // …but not a dogpile (death-by-density)
    private const int GrindContentionCap = 6;   // > this many bots underfoot → move off the contested spot
    private const float GrindRelocateMinYards = 40f;  // don't relocate for a cell we're basically on
    private const double GrindRelocateDeadline = 60;   // travel-to-cell WAIT ceiling (s)
    private const int GrindReachTier = 1;    // one hub-hop of search radius for a better cell

    private readonly ZoneSafetyMap _safety;
    private readonly ILogger<GrindPlanner> _log;

    public GrindPlanner(ZoneSafetyMap safety, ILogger<GrindPlanner> log)
    {
        _safety = safety;
        _log = log;
    }

    public Goal Handles => Goal.Grinding;

    public StepResult PlanNext(BotContext ctx, BotStateSnapshot snap)
    {
        var id = ctx.Identity;

        // ── (1) Relocation state machine (durable on BotIdentity; survives the reselect bounce) ──
        if (id != null && id.GrindRelocating)
        {
            if (!id.GrindRelocatePatrolStopped)
            {
                id.GrindRelocatePatrolStopped = true;
                ctx.SetStep("relocate-idle");
                return StepResult.Fire(IdleTask());   // stop any C++ patrol so the MOVE_TO can travel
            }
            if (!id.GrindRelocateMoveIssued)
            {
                id.GrindRelocateMoveIssued = true;
                ctx.SetStep("relocate-move");
                var move = new BridgeCommand("MOVE_TO", new
                {
                    x = id.GrindRelocateX,
                    y = id.GrindRelocateY,
                    z = id.GrindRelocateZ,
                    mapId = ctx.MapId
                });
                return StepResult.Send(move, "TASK_COMPLETE", TimeSpan.FromSeconds(GrindRelocateDeadline));
            }
            // Move issued and no longer pending → arrived (or negated). Done; the arm path below
            // re-fires GRIND at the new spot. Consume any relocate-move failure so it can't escape.
            id.ClearGrindRelocate();
            ctx.Failure = null;
            // fall through to arm
        }

        // ── (2) Arm / re-arm ──────────────────────────────────────────────────────────────────
        if (ctx.Grind == null)
        {
            if (id != null && !HereIsGood(ctx, snap, id))
            {
                float cap = ZoneSafetyMap.GetMaxTravelDistance(ctx.Level, ctx.ZoneId, GrindReachTier);
                var cell = _safety.FindGrindCell(
                    ctx.MapId, snap.X, snap.Y, ctx.Level, cap,
                    lowOffset: GrindLevelLowOffset, highOffset: GrindLevelHighOffset,
                    dangerCeil: GrindDangerCeil, minSpawn: GrindMinSpawn, maxSpawn: GrindMaxSpawn,
                    reject: (wx, wy) => id.IsPathBlacklisted(wx, wy));

                if (cell is { } c && c.DistYards >= GrindRelocateMinYards)
                {
                    id.GrindRelocating = true;
                    id.GrindRelocatePatrolStopped = true;     // this same step fires the IDLE below
                    id.GrindRelocateMoveIssued = false;
                    id.GrindRelocateX = c.X;
                    id.GrindRelocateY = c.Y;
                    id.GrindRelocateZ = snap.Z;               // 2D grid; C++ re-grounds Z on the navmesh path
                    ctx.SetStep("relocate-idle");
                    _log.LogInformation(
                        "[GRIND] {Name} spot weak (spawn={S} avg={A:F0} max={M} lvl={L}) → relocate {D:F0}yd to cell (avg={CA:F0} max={CM} spawn={CS})",
                        ctx.Name, _safety.GetSpawnCount(ctx.MapId, snap.X, snap.Y),
                        _safety.GetAvgCreatureLevel(ctx.MapId, snap.X, snap.Y),
                        _safety.GetMaxCreatureLevel(ctx.MapId, snap.X, snap.Y), ctx.Level,
                        c.DistYards, c.AvgLevel, c.MaxLevel, c.SpawnCount);
                    return StepResult.Fire(IdleTask());       // stop the patrol; (1) issues the MOVE_TO next tick
                }

                _log.LogDebug("[GRIND] {Name} spot weak but no level-appropriate cell within {Cap:F0}yd — grinding here",
                    ctx.Name, cap);
                // fall through: grind here best-effort (don't wedge with nowhere better to go)
            }

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

    /// <summary>Is the cell the bot is standing in worth grinding right now?</summary>
    private bool HereIsGood(BotContext ctx, BotStateSnapshot snap, BotIdentity id)
    {
        int spawn = _safety.GetSpawnCount(ctx.MapId, snap.X, snap.Y);
        if (spawn < GrindMinSpawn || spawn > GrindMaxSpawn) return false;     // barren (farmyard) or dogpile
        int maxLvl = _safety.GetMaxCreatureLevel(ctx.MapId, snap.X, snap.Y);
        if (maxLvl > ctx.Level + GrindDangerCeil) return false;              // a red mob lives here
        float avg = _safety.GetAvgCreatureLevel(ctx.MapId, snap.X, snap.Y);
        if (avg < Math.Max(1, ctx.Level - GrindLevelLowOffset)) return false; // grey — no XP
        if (avg > ctx.Level + GrindLevelHighOffset) return false;            // too hot on average
        if (id.IsPathBlacklisted(snap.X, snap.Y)) return false;             // death pocket
        if (snap.NearbyBotCount > GrindContentionCap) return false;          // hyper-contested
        return true;
    }

    public bool IsProgressing(BotContext ctx, BotStateSnapshot snap)
    {
        if (ctx.Identity?.GrindRelocating == true) return true;   // a relocate is active progress
        if (ctx.TimeInGoalSec < ArmGraceSec) return true;         // arm grace: let the patrol get going
        return (DateTime.UtcNow - ctx.LastKillUtc).TotalSeconds < KillRecencySec;
    }

    public StallAction OnStall(BotContext ctx)
        => StallAction.Of(StallActionKind.ReselectGoal, "grind:no_kills");

    /// <summary>SET_TASK IDLE — stops the C++ grind patrol so a relocate MOVE_TO can travel.</summary>
    private static BridgeCommand IdleTask() => new("SET_TASK", new { task = "IDLE" });
}