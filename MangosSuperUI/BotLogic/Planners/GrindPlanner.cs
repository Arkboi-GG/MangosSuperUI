using MangosSuperUI.BotLogic.Core;
using MangosSuperUI.BotLogic.Data;
using MangosSuperUI.BotLogic.Tracking;

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
    private const int GuardTownParkSec = 120;    // guard-town bail: hold Idle this long instead of re-bailing at tick speed
    private const double ArmGraceSec = 45;       // grace after entry before KILL-recency applies
    private const double KillRecencySec = 120;   // a bot landing REAL kills is progressing

    // [GRIND-RELOCATE] (FINDING_003 residual) — see the relocate block in PlanNext.
    private const float RelocateSearchRadius = 350f; // a modest walk, never a cross-zone trek
    private const float RelocateMinDist = 80f;       // beyond the 60yd grind leash — nearer is the same spot
    private const int RelocatePathDangerCeil = 5;    // corridor mobs over botLevel+this → reject the route
    private const int RelocateCooldownMin = 15;      // one relocate ATTEMPT per ~lock window (009 lesson: never at tick speed)
    private static readonly TimeSpan RelocateDeadline = TimeSpan.FromMinutes(3); // 350yd at ~7yd/s + recovery slack
    private const int GrindHubJumpCooldownMin = 10;  // bound a bad camp/data landing; never continent-ping-pong
    private const int GrindHubQuestRescanParkSec = 8; // wait for cross-map STATE, then select quests at the new camp

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
        // [GRIND-HUB] C++ already tells us the decisive fact after three wildcard scans:
        // GRIND_BLOCKED reason=no_target. QuestPlanner consumes that signal for objective grinds,
        // but filler Grinding historically ignored it (ctx.Failure stayed set forever) and waited
        // for the 150s/6-wedge town escape. Consume it here and jump to a PROVEN hostile-spawn camp,
        // preferring the other continent so a bad local town/zone cannot recycle the bot.
        //
        // The jump clears grind-lock and parks briefly. Once the cross-map STATE arrives,
        // GoalSelector runs normally from the new position: nearby viable quests win; only if none
        // exist does it re-arm wildcard grinding at the already-validated level-safe camp.
        // The core signal is authoritative when its scan is literally empty. It is not sufficient by
        // itself, though: a town can contain a nominally valid mob that never becomes a REAL kill
        // (unreachable/contested/bad combat pocket), so C++ never says no_target. The old 45s planner
        // stall then re-issued SET_TASK GRIND, resetting the core scan and preserving the bad pocket.
        // Treat the planner's own bounded no-real-kill verdict as the same relocation request.
        WaitFailure? empty = ctx.Failure is { CommandType: "GRIND", Reason: "no_target" } reported
            ? reported
            : ctx.Failure == null
              && ctx.Grind != null
              && ctx.TimeInGoalSec >= ArmGraceSec
              && (DateTime.UtcNow - ctx.LastKillUtc).TotalSeconds >= KillRecencySec
                ? new WaitFailure
                {
                    CommandType = "GRIND",
                    Reason = "no_kills",
                    Dest = ctx.Grind.AreaCenter,
                    Utc = DateTime.UtcNow
                }
                : null;

        if (empty != null && ctx.Identity is { } hid)
        {
            CircuitTrace.HitNote(ctx.Guid, "grind: barren verdict", empty.Reason);
            ctx.Failure = null;
            ctx.RecordDeadGrindCell(empty.Dest?.X ?? snap.X, empty.Dest?.Y ?? snap.Y);

            if (ctx.InCombat || ctx.InPlayerParty)
            {
                CircuitTrace.Hit(ctx.Guid, "grind-hub: port blocked by combat/party, parking");
                hid.WedgeBackoffUntil = DateTime.UtcNow.AddSeconds(GuardTownParkSec);
                ctx.Grind = null;
                _log.LogInformation("[GRIND-HUB] {Name} empty grind but combat/player-party blocks port — parking {P}s",
                    ctx.Name, GuardTownParkSec);
                return StepResult.Block("grind:hub-port-blocked");
            }

            if (hid.GrindHubJumpCooldownUntil is DateTime hcd && DateTime.UtcNow < hcd)
            {
                CircuitTrace.Hit(ctx.Guid, "grind-hub: in jump cooldown, parking");
                hid.WedgeBackoffUntil = DateTime.UtcNow.AddSeconds(GuardTownParkSec);
                ctx.Grind = null;
                _log.LogInformation("[GRIND-HUB] {Name} empty grind during hub-jump cooldown — parking {P}s",
                    ctx.Name, GuardTownParkSec);
                return StepResult.Block("grind:hub-cooldown");
            }

            int otherContinent = snap.MapId == 0 ? 1 : 0;
            var hteam = ZoneSafetyMap.TeamFromFaction(hid.Faction);
            int seed = unchecked((int)ctx.Guid + hid.GrindHubJumpRotation * 7919);
            var hub = _safety.FindGlobalGrindCell(
                ctx.Level, hteam, seed, otherContinent,
                reject: (map, x, y) =>
                    map == snap.MapId && (ctx.IsDeadGrindCell(x, y) || hid.IsPathBlacklisted(x, y)));

            if (hub is { } h)
            {
                CircuitTrace.Hit(ctx.Guid, "grind-hub: cross-continent camp jump issued");
                hid.GrindHubJumpCooldownUntil = DateTime.UtcNow.AddMinutes(GrindHubJumpCooldownMin);
                hid.GrindHubJumpRotation++;
                hid.GrindLockUntil = null;
                hid.GrindLockReleaseCooldownUntil = null;
                hid.ClearGrindRelocate();
                hid.WedgeBackoffUntil = DateTime.UtcNow.AddSeconds(GrindHubQuestRescanParkSec);
                ctx.LastProgressUtc = hid.WedgeBackoffUntil.Value;
                ctx.Grind = null;
                ctx.Quest = null;
                ctx.ClearObjective();

                _log.LogWarning(
                    "[GRIND-HUB] {Name} barren grind ({Reason}) @ map{From} ({PX:F0},{PY:F0}) — cross-continent jump to map{Map} camp ({X:F0},{Y:F0},{Z:F0}) avg L{Avg:F1} max L{Max} spawns {N}; quest rescan after {Park}s",
                    ctx.Name, empty.Reason, snap.MapId, snap.X, snap.Y, h.MapId, h.X, h.Y, h.Z,
                    h.AvgLevel, h.MaxLevel, h.SpawnCount, GrindHubQuestRescanParkSec);
                return StepResult.Fire(new BridgeCommand("SET_TASK", new
                {
                    task = "PORT_HOME",
                    home_x = h.X,
                    home_y = h.Y,
                    home_z = h.Z,
                    home_map = h.MapId
                }));
            }

            // No camp data is safer than a blind teleport. Park and let the existing wedge ladder
            // remain the fail-safe; this path is visible and bounded rather than lying as Grinding.
            CircuitTrace.Hit(ctx.Guid, "grind-hub: no camp on either continent, parking");
            hid.WedgeBackoffUntil = DateTime.UtcNow.AddSeconds(GuardTownParkSec);
            ctx.Grind = null;
            _log.LogWarning("[GRIND-HUB] {Name} no level-safe camp found on either continent — parking {P}s",
                ctx.Name, GuardTownParkSec);
            return StepResult.Block("grind:no-global-hub");
        }

        // Arm / re-arm: indefinite grind at the bot's CURRENT position. C++ SelectGrindTarget
        // does the nearest-level-appropriate-XP-mob scan and keeps rescanning; we never steer it
        // beyond "here". creature_entry=0 = "nearest valid hostile" (C++ skips critters/grey);
        // kill_count=0 = indefinite (never TASK_COMPLETEs — the KILL stream is the only feedback,
        // so there is no WAIT that could false-stall a killing bot).
        if (ctx.Grind == null)
        {
            CircuitTrace.Hit(ctx.Guid, "grind: not armed yet");
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
                CircuitTrace.Hit(ctx.Guid, "grind: refusing enemy guard cell, parking");
                ctx.RecordDeadGrindCell(snap.X, snap.Y);
                // PARK, don't just Block: Block → OnStall → ReselectGoal re-enters Grinding next tick
                // (grind-lock / pick=0 hasn't changed), which re-bails here at tick speed — observed
                // 2026-08-08, 8 bots at ~1.5Hz for hours. Stamping the wedge backoff makes GoalSelector
                // hold Idle (its backoff check sits BEFORE the grind-lock), and we deliberately do NOT
                // future-stamp LastProgressUtc, so the real wedge breaker still accrues no-progress and
                // runs its park→dead-cell→escalation ladder on schedule.
                if (ctx.Identity is { } gid)
                {
                    CircuitTrace.Hit(ctx.Guid, "grind: guard-cell park stamped");
                    gid.WedgeBackoffUntil = DateTime.UtcNow.AddSeconds(GuardTownParkSec);
                }
                _log.LogWarning(
                    "[GRIND] {Name} refusing filler grind in enemy guard cell @ ({X:F0},{Y:F0}) map {M} — parking {P}s (FINDING_005)",
                    ctx.Name, snap.X, snap.Y, snap.MapId, GuardTownParkSec);
                return StepResult.Block("grind:guard-town");
            }

            // [GRIND-RELOCATE] (FINDING_003 residual) A grind-LOCKED bot parked where nothing is
            // killable used to idle out the whole window (417 bots observed 2026-08-16). Walk it to
            // the nearest LEVEL-SAFE cell instead. This is NOT the removed density-chaser: the old
            // relocate scored raw density and dragged bots into red zones; FindGrindCell's level
            // ceiling/band plus the rejects below (dead cells, per-bot blacklist, enemy-guard cells,
            // fleet-known no-path dests, corridor danger) are the rails it lacked. One ATTEMPT per
            // cooldown window, and every failure path falls through to today's arm-here behavior —
            // worst case is exactly the status quo.
            if (ctx.Identity is { } rid)
            {   // cb:fold identity gate, every inner arm probed
                if (rid.GrindRelocating && ctx.Failure is { CommandType: "MOVE_TO" } rf)
                {
                    CircuitTrace.HitNote(ctx.Guid, "grind-relocate: leg failed, arming in place", rf.Reason);
                    // relocate leg failed — grind here as before; this window's attempt is spent
                    if (rf.Reason is "no_path" or "empty_path")
                    {
                        CircuitTrace.Hit(ctx.Guid, "grind-relocate: failed dest recorded dead");
                        // [FINDING_020] an isolated START says nothing about the dest — keep it per-bot only
                        if (!rf.StartIsolated)
                        {
                            CircuitTrace.Hit(ctx.Guid, "grind-relocate: fleet-wide no-path dest recorded");
                            _safety.RecordNoPathDest(snap.MapId, rid.GrindRelocateX, rid.GrindRelocateY);
                        }
                        ctx.RecordDeadGrindCell(rid.GrindRelocateX, rid.GrindRelocateY);
                    }
                    rid.ClearGrindRelocate();
                    ctx.Failure = null;
                    _log.LogInformation("[GRIND] {Name} relocate leg failed ({Reason}) — arming grind in place",
                        ctx.Name, rf.Reason);
                }
                else if (rid.GrindRelocating)
                {
                    CircuitTrace.Hit(ctx.Guid, "grind-relocate: leg in flight, checking arrival");
                    float rdx = snap.X - rid.GrindRelocateX, rdy = snap.Y - rid.GrindRelocateY;
                    if (rdx * rdx + rdy * rdy <= 20f * 20f)
                    {
                        CircuitTrace.Hit(ctx.Guid, "grind-relocate: arrived, arming at new spot");
                        rid.ClearGrindRelocate();   // arrived — fall through, arm at the NEW spot
                        _log.LogInformation("[GRIND] {Name} relocate arrived @ ({X:F0},{Y:F0}) — arming grind",
                            ctx.Name, snap.X, snap.Y);
                    }
                    else
                    {
                        CircuitTrace.Hit(ctx.Guid, "grind-relocate: still walking the leg");
                        return StepResult.Wait();   // C++ is still walking the relocate leg
                    }
                }
                else if (rid.GrindLockUntil is DateTime rgl && DateTime.UtcNow < rgl
                    && (DateTime.UtcNow - ctx.LastKillUtc).TotalSeconds > KillRecencySec
                    && !(rid.GrindRelocateCooldownUntil is DateTime rcd && DateTime.UtcNow < rcd)
                    && _safety.IsLoaded)
                {
                    CircuitTrace.Hit(ctx.Guid, "grind-relocate: attempt window opened");
                    rid.GrindRelocateCooldownUntil = DateTime.UtcNow.AddMinutes(RelocateCooldownMin);
                    var rteam = ZoneSafetyMap.TeamFromFaction(rid.Faction);
                    var cell = _safety.FindGrindCell(
                        snap.MapId, snap.X, snap.Y, ctx.Level, RelocateSearchRadius, rteam,
                        reject: (wx, wy) =>
                            ctx.IsDeadGrindCell(wx, wy)
                            || rid.IsPathBlacklisted(wx, wy)
                            || _safety.IsEnemyGuardCell(snap.MapId, wx, wy, rteam)
                            || _safety.IsNoPathDest(snap.MapId, wx, wy)
                            || _safety.GetMaxCreatureLevelOnPath(snap.MapId, snap.X, snap.Y, wx, wy, rteam)
                                > ctx.Level + RelocatePathDangerCeil);
                    if (cell is { } c && c.DistYards >= RelocateMinDist)
                    {
                        CircuitTrace.Hit(ctx.Guid, "grind-relocate: safe cell found, MOVE_TO issued", c.DistYards);
                        rid.GrindRelocating = true;
                        rid.GrindRelocateMoveIssued = true;
                        rid.GrindRelocateX = c.X;
                        rid.GrindRelocateY = c.Y;
                        rid.GrindRelocateZ = snap.Z;
                        ctx.SetStep("grind-relocate");
                        _log.LogInformation(
                            "[GRIND] {Name} barren grind-lock — relocating {D:F0}yd to cell ({X:F0},{Y:F0}) avg L{Avg:F0} max L{Max} spawns {N}",
                            ctx.Name, c.DistYards, c.X, c.Y, c.AvgLevel, c.MaxLevel, c.SpawnCount);
                        return StepResult.Send(
                            new BridgeCommand("MOVE_TO", new { mapId = snap.MapId, x = c.X, y = c.Y, z = snap.Z }),
                            "TASK_COMPLETE", RelocateDeadline);
                    }
                    // no viable safe cell in range — grind here exactly as before (attempt spent)
                }
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
            CircuitTrace.Hit(ctx.Guid, "grind: armed SET_TASK GRIND here");

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
        // A relocate walk IS progress (per the BotIdentity design note): the Send deadline
        // bounds it, and the goal-change / wedge clears abort it — no unbounded pass.
        if (ctx.Identity is { GrindRelocating: true }) { CircuitTrace.Hit(ctx.Guid, "grind: progressing (relocate walk)"); return true; }

        // Once armed, PlanNext owns the no-real-kill verdict and converts it into the same hub jump as
        // GRIND_BLOCKED. Returning false here used to route through OnStall -> EnterGoal(Idle), which
        // cancelled and re-issued the C++ patrol every ~45s before its negative feedback could help.
        // Keep the patrol intact; PlanNext still runs every tick because grind carries no Pending WAIT.
        if (ctx.Grind != null) { CircuitTrace.Hit(ctx.Guid, "grind: progressing (armed, C++ owns scan)"); return true; }

        return ctx.TimeInGoalSec < ArmGraceSec
            || (DateTime.UtcNow - ctx.LastKillUtc).TotalSeconds < KillRecencySec;
    }

    public StallAction OnStall(BotContext ctx)
        => StallAction.Of(StallActionKind.ReselectGoal, "grind:no_kills");
}
