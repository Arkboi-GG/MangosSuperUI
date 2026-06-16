using MangosSuperUI.BotLogic.Core;
using MangosSuperUI.BotLogic.Tracking;
using Microsoft.Extensions.Logging;

namespace MangosSuperUI.BotLogic.Domains;

/// <summary>
/// Eating/drinking and death recovery.
///
/// Death flow (Session 12 — simplified, no ghost walk):
///   C++ death tick: captures death position, emits DEATH event.
///     NO BuildPlayerRepop, NO RepopAtGraveyard. Bot stays dead at death spot.
///   BotBrainService.HandleBridgeEventAsync: parses DEATH data, stores on
///     BotIdentity.CorpseX/Y/Z/CorpseMapId.
///   DecisionEngine critical trigger (IsDead && !CorpseRunning) → switches here.
///   OnEnter: calculates a fake "corpse run" delay (15-45s), stores rez timer.
///   OnTick: waits for timer, sends RESURRECT. Bot rezzes at death position.
///   C++ RESURRECT handler: ResurrectPlayer(0.5f) + SpawnCorpseBones(), emits RESPAWN.
///   OnEvent(RESPAWN): marks interruptible, forces immediate strategic re-eval.
///   Bot eats (50% HP from revive), then resumes previous activity.
///
/// Why no ghost walk: RepopAtGraveyard picks graveyards that can be thousands of
///   yards from the corpse (cross-zone). MovePoint can't path that far. Bots get
///   stuck as ghosts at graveyards forever. Timer-based rez is simple and reliable.
/// </summary>
public class MaintenanceDomain : IBotDomain
{
    private readonly ILogger<MaintenanceDomain> _logger;
    private readonly Data.ZoneSafetyMap _safetyMap;

    // Safety timeout if RESPAWN never arrives after sending RESURRECT
    private const float ResurrectTimeoutSeconds = 20f;

    // How far to offset from corpse when seeking a safe rez spot.
    // WoW allows resurrection within ~36yd of corpse.
    private const float REZ_OFFSET_DISTANCE = 25f;

    // ── Session 41: death-loop / unpathable-corpse failsafe ──
    // June-12 run: 3/5 bots lost 4–7.5 h each to die→rez-into-danger→die loops
    // around no_path ghost-walk dests. Per-guid here because PhaseData does not
    // survive the CorpseRunning→Eating→CorpseRunning churn and BotIdentity is
    // out of scope for this fix (move there when that file is next open).
    private sealed class DeathLoopState
    {
        public DateTime LastRespawnUtc;
        public DateTime FirstDeadUtc;
        public int QuickDeaths;      // deaths within DEATH_LOOP_WINDOW_SEC of last respawn
        public int GhostWalkFails;   // MOVE_FAILED during GhostWalkingToSafeSpot
    }
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, DeathLoopState> _deathLoop = new();

    private const float DEATH_LOOP_WINDOW_SEC = 300f;  // re-death within 5 min of respawn = loop candidate
    private const int DEATH_LOOP_THRESHOLD = 3;     // quick deaths before escalation
    private const float MAX_DEAD_SECONDS = 300f;  // absolute cap on time spent dead per death

    public MaintenanceDomain(Data.ZoneSafetyMap safetyMap, ILogger<MaintenanceDomain> logger)
    {
        _safetyMap = safetyMap;
        _logger = logger;
    }

    public ActivityType[] OwnedActivities => new[]
    {
        ActivityType.Eating,
        ActivityType.CorpseRunning
    };

    public bool IsOperational => true;

    // ════════════════════════════════════════════════════════════════════
    // EvaluateTransitions
    // ════════════════════════════════════════════════════════════════════

    public Dictionary<ActivityType, float> EvaluateTransitions(BotIdentity bot, BotStateSnapshot state)
    {
        var weights = new Dictionary<ActivityType, float>();

        if (bot.CurrentActivity.Type == ActivityType.CorpseRunning)
        {
            if (bot.CurrentActivity.IsInterruptible)
            {
                // Alive again. Bot is at 50% HP from ResurrectPlayer(0.5f).
                weights[ActivityType.CorpseRunning] = 0.05f;
                weights[ActivityType.Eating] = 5.0f;
                weights[ActivityType.Questing] = 1.0f;
                weights[ActivityType.Grinding] = 0.8f;
            }
            else
            {
                // Still dead / waiting to rez — stay locked
                weights[ActivityType.CorpseRunning] = 10.0f;
            }
            return weights;
        }

        // Eating: stay until HP > 80% and Mana > 60%
        if (state.HealthPercent < 0.8f || state.ManaPercent < 0.6f)
        {
            weights[ActivityType.Eating] = 2.0f;
        }
        else
        {
            // Done eating — go back to what we were doing
            weights[ActivityType.Eating] = 0.1f;
            weights[bot.PreviousActivity?.Type ?? ActivityType.Questing] = 1.5f;
        }

        return weights;
    }

    // ════════════════════════════════════════════════════════════════════
    // OnEnter
    // ════════════════════════════════════════════════════════════════════

    public List<BridgeCommand> OnEnter(BotIdentity bot, BotStateSnapshot state)
    {
        var commands = new List<BridgeCommand>();

        if (bot.CurrentActivity.Type == ActivityType.CorpseRunning)
        {
            bot.CurrentActivity.IsInterruptible = false;

            // ── Session 41: death-loop accounting ──
            // FirstDeadUtc is cleared only by RESPAWN — re-entering CorpseRunning
            // while it's still set means the rez never happened (e.g. RESURRECT
            // timeout → forced Alive → eval → straight back here). That is the
            // SAME death continuing: don't restart the dead-time clock and don't
            // reset the sampler distrust, or a rez-fail churn defeats both.
            var dls = _deathLoop.GetOrAdd(bot.Guid.ToString(), _ => new DeathLoopState());
            bool sameDeath = dls.FirstDeadUtc != default;
            if (!sameDeath)
            {
                dls.FirstDeadUtc = DateTime.UtcNow;
                if (dls.LastRespawnUtc != default
                    && (DateTime.UtcNow - dls.LastRespawnUtc).TotalSeconds < DEATH_LOOP_WINDOW_SEC)
                {
                    dls.QuickDeaths++;
                    if (dls.QuickDeaths >= DEATH_LOOP_THRESHOLD)
                        _logger.LogWarning(
                            "[BOT-MAINT] {Name} DEATH LOOP — {Count} deaths each within {Window:F0}s of respawn. " +
                            "Escalating: no ghost walk, graveyard rez requested.",
                            bot.Name, dls.QuickDeaths, DEATH_LOOP_WINDOW_SEC);
                }
                else
                {
                    dls.QuickDeaths = 1;
                    dls.GhostWalkFails = 0;   // fresh context — re-trust the sampler
                }
            }

            // Calculate fake "corpse run" delay.
            // Personality-modulated: impatient bots rez faster (they'd sprint).
            // Range: 15-45 seconds.
            float baseDelay = 25.0f;
            float patienceMod = 0.7f + (bot.Personality.Patience * 0.6f); // 0.7–1.3
            float delay = baseDelay * patienceMod;
            delay = Math.Clamp(delay, 15.0f, 45.0f);

            var rezAt = DateTime.UtcNow.AddSeconds(delay);
            bot.CurrentActivity.PhaseData["rez_at_utc"] = rezAt;
            AdvanceTo(bot, "WaitingToRez", "death", WaitOn.RezAt);

            float corpseX = bot.CorpseX ?? state.X;
            float corpseY = bot.CorpseY ?? state.Y;
            float corpseZ = bot.CorpseZ ?? state.Z;
            int corpseMap = bot.CorpseMapId ?? state.MapId;

            _logger.LogInformation(
                "[BOT-MAINT] {Name} died at ({X:F0},{Y:F0},{Z:F0}) map={Map}. " +
                "Will resurrect in {Delay:F0}s (timer-based, no ghost walk).",
                bot.Name, corpseX, corpseY, corpseZ, corpseMap, delay);

            // No MOVE_TO — bot stays at death position, C++ doesn't ghost them.
            return commands;
        }

        // Eating
        bot.CurrentActivity.ContextTag = $"eat:hp{(int)(state.HealthPercent * 100)}";
        AdvanceTo(bot, "Sitting", "eat", WaitOn.Cpp("eat"));

        return commands;
    }

    // ════════════════════════════════════════════════════════════════════
    // OnTick
    // ════════════════════════════════════════════════════════════════════

    public List<BridgeCommand> OnTick(BotIdentity bot, BotStateSnapshot state)
    {
        var commands = new List<BridgeCommand>();

        if (bot.CurrentActivity.Type == ActivityType.CorpseRunning)
        {
            var subPhase = bot.CurrentActivity.SubPhase ?? "";

            // ── Session 41: absolute dead-time cap (belt-and-suspenders) ──
            // Normal cycle is ≤80 s (45 s timer + 15 s walk + 20 s rez timeout);
            // anything past MAX_DEAD_SECONDS is a wedge we haven't named yet.
            var dlsCap = _deathLoop.GetOrAdd(bot.Guid.ToString(), _ => new DeathLoopState());
            if (subPhase != "Alive" && subPhase != "WaitingForResurrect"
                && dlsCap.FirstDeadUtc != default
                && (DateTime.UtcNow - dlsCap.FirstDeadUtc).TotalSeconds > MAX_DEAD_SECONDS)
            {
                _logger.LogWarning(
                    "[BOT-MAINT] {Name} dead for >{Cap:F0}s in '{Sub}' — forcing graveyard rez.",
                    bot.Name, MAX_DEAD_SECONDS, subPhase);
                AdvanceTo(bot, "WaitingForResurrect");
                bot.CurrentActivity.PhaseData["resurrect_sent_at"] = DateTime.UtcNow;
                commands.Add(new BridgeCommand("RESURRECT", new { at_graveyard = 1 }));
                return commands;
            }

            switch (subPhase)
            {
                case "WaitingToRez":
                    commands.AddRange(ProcessWaitingToRez(bot, state));
                    break;

                case "GhostWalkingToSafeSpot":
                    commands.AddRange(ProcessGhostWalkToSafeSpot(bot, state));
                    break;

                case "WaitingForResurrect":
                    ProcessWaitingForResurrect(bot);
                    break;

                case "Alive":
                    // RESPAWN received — waiting for strategic eval to switch out
                    break;

                default:
                    // Unknown sub-phase — reset to WaitingToRez with immediate rez
                    _logger.LogWarning(
                        "[BOT-MAINT] {Name} unknown corpse sub-phase '{Sub}', resurrecting now.",
                        bot.Name, subPhase);
                    bot.CurrentActivity.PhaseData["rez_at_utc"] = DateTime.UtcNow;
                    AdvanceTo(bot, "WaitingToRez");
                    break;
            }

            return commands;
        }

        // Eating: update context tag
        if (bot.CurrentActivity.Type == ActivityType.Eating)
        {
            bot.CurrentActivity.ContextTag =
                $"eat:hp{(int)(state.HealthPercent * 100)}:mp{(int)(state.ManaPercent * 100)}";
        }

        return commands;
    }

    // ════════════════════════════════════════════════════════════════════
    // OnEvent
    // ════════════════════════════════════════════════════════════════════

    public List<BridgeCommand> OnEvent(BotIdentity bot, BotStateSnapshot state, BotEvent evt)
    {
        var commands = new List<BridgeCommand>();

        // Eating: allow transition out if attacked
        if (evt.EventType == "COMBAT_START" || state.InCombat)
        {
            if (bot.CurrentActivity.Type == ActivityType.Eating)
                bot.CurrentActivity.IsInterruptible = true;
        }

        // ── Session 41: ghost walk failed (no_path safe spot) ──
        // The sampler picks by ZoneSafetyMap only and never path-checks; near a
        // mesh hole every pick fails. Don't burn the 15 s timeout — rez now,
        // and mark the sampler untrusted here so the next death skips the walk.
        if (evt.EventType == "MOVE_FAILED"
            && bot.CurrentActivity.Type == ActivityType.CorpseRunning
            && (bot.CurrentActivity.SubPhase ?? "") == "GhostWalkingToSafeSpot")
        {
            var dlsGw = _deathLoop.GetOrAdd(bot.Guid.ToString(), _ => new DeathLoopState());
            dlsGw.GhostWalkFails++;
            _logger.LogWarning(
                "[BOT-MAINT] {Name} ghost walk MOVE_FAILED ({Data}) — rezzing immediately (fail #{N}).",
                bot.Name, evt.Data ?? "", dlsGw.GhostWalkFails);
            AdvanceTo(bot, "WaitingForResurrect");
            bot.CurrentActivity.PhaseData["resurrect_sent_at"] = DateTime.UtcNow;
            if (dlsGw.GhostWalkFails >= 2 || dlsGw.QuickDeaths >= DEATH_LOOP_THRESHOLD)
                commands.Add(new BridgeCommand("RESURRECT", new { at_graveyard = 1 }));
            else
                commands.Add(new BridgeCommand("RESURRECT"));
            return commands;
        }

        // RESPAWN: bot is alive again
        if (evt.EventType == "RESPAWN" && bot.CurrentActivity.Type == ActivityType.CorpseRunning)
        {
            _logger.LogInformation(
                "[BOT-MAINT] {Name} RESPAWN received — alive! Forcing re-eval.",
                bot.Name);

            var dlsRs = _deathLoop.GetOrAdd(bot.Guid.ToString(), _ => new DeathLoopState());
            dlsRs.LastRespawnUtc = DateTime.UtcNow;
            dlsRs.FirstDeadUtc = default;

            bot.CurrentActivity.IsInterruptible = true;
            AdvanceTo(bot, "Alive");
            bot.CurrentActivity.ContextTag = "corpse:alive";
            bot.NextStrategicEval = DateTime.UtcNow;

            // Clear corpse position
            bot.CorpseX = null;
            bot.CorpseY = null;
            bot.CorpseZ = null;
            bot.CorpseMapId = null;
        }

        return commands;
    }

    // ════════════════════════════════════════════════════════════════════
    // Sub-phase processors
    // ════════════════════════════════════════════════════════════════════

    private List<BridgeCommand> ProcessWaitingToRez(BotIdentity bot, BotStateSnapshot state)
    {
        var commands = new List<BridgeCommand>();

        if (bot.CurrentActivity.PhaseData.TryGetValue("rez_at_utc", out var obj)
            && obj is DateTime rezAt)
        {
            if (DateTime.UtcNow >= rezAt)
            {
                float corpseX = bot.CorpseX ?? state.X;
                float corpseY = bot.CorpseY ?? state.Y;
                float corpseZ = bot.CorpseZ ?? state.Z;
                int corpseMap = bot.CorpseMapId ?? state.MapId;

                // ── Session 41: escalated rez — death loop or untrusted sampler ──
                // The at_graveyard flag is a forward-compatible ride-along: the
                // current C++ BridgeHandleResurrect ignores unknown fields (plain
                // rez-at-corpse, no worse than today); once the RepopAtGraveyard
                // variant ships, this becomes a true escape from the kill pocket.
                var dlsEsc = _deathLoop.GetOrAdd(bot.Guid.ToString(), _ => new DeathLoopState());
                if (dlsEsc.QuickDeaths >= DEATH_LOOP_THRESHOLD || dlsEsc.GhostWalkFails >= 2)
                {
                    _logger.LogWarning(
                        "[BOT-MAINT] {Name} ESCALATED REZ (quickDeaths={QD}, ghostWalkFails={GW}) — " +
                        "skipping ghost walk, requesting graveyard rez.",
                        bot.Name, dlsEsc.QuickDeaths, dlsEsc.GhostWalkFails);
                    AdvanceTo(bot, "WaitingForResurrect");
                    bot.CurrentActivity.PhaseData["resurrect_sent_at"] = DateTime.UtcNow;
                    commands.Add(new BridgeCommand("RESURRECT", new { at_graveyard = 1 }));
                    return commands;
                }

                // Session 32: Before rezzing, find a safe spot near the corpse.
                // Check the corpse location itself — if it's safe, rez immediately.
                // If not, ghost-walk to a safer offset within rez range (~36yd).
                var safeSpot = FindSafeRezSpot(bot, corpseX, corpseY, corpseZ, corpseMap);

                if (safeSpot == null)
                {
                    // Corpse location is already safe (or no safety data) — rez in place
                    _logger.LogInformation(
                        "[BOT-MAINT] {Name} rez timer expired. Corpse area is safe — resurrecting in place.",
                        bot.Name);

                    AdvanceTo(bot, "WaitingForResurrect");
                    bot.CurrentActivity.PhaseData["resurrect_sent_at"] = DateTime.UtcNow;
                    commands.Add(new BridgeCommand("RESURRECT"));
                }
                else
                {
                    // Corpse area has hostiles — ghost-walk to safer spot first
                    _logger.LogInformation(
                        "[BOT-MAINT] {Name} rez timer expired. Hostiles near corpse " +
                        "(maxLvl={CorpseMax} at corpse vs {SafeMax} at safe spot). " +
                        "Ghost-walking {Dist:F0}yd to ({SX:F0},{SY:F0}) before rezzing.",
                        bot.Name, safeSpot.Value.corpseMaxLevel, safeSpot.Value.safeMaxLevel,
                        Distance2D(corpseX, corpseY, safeSpot.Value.x, safeSpot.Value.y),
                        safeSpot.Value.x, safeSpot.Value.y);

                    AdvanceTo(bot, "GhostWalkingToSafeSpot");
                    bot.CurrentActivity.PhaseData["safe_x"] = safeSpot.Value.x;
                    bot.CurrentActivity.PhaseData["safe_y"] = safeSpot.Value.y;
                    bot.CurrentActivity.PhaseData["safe_z"] = safeSpot.Value.z;
                    bot.CurrentActivity.PhaseData["ghost_walk_started"] = DateTime.UtcNow;

                    commands.Add(new BridgeCommand("MOVE_TO", new
                    {
                        mapId = corpseMap,
                        x = safeSpot.Value.x,
                        y = safeSpot.Value.y,
                        z = safeSpot.Value.z
                    }));
                }
            }
            // else: still waiting, do nothing
        }
        else
        {
            // PhaseData missing/corrupt — resurrect immediately
            _logger.LogWarning(
                "[BOT-MAINT] {Name} rez_at_utc missing from PhaseData, resurrecting now.",
                bot.Name);
            AdvanceTo(bot, "WaitingForResurrect");
            bot.CurrentActivity.PhaseData["resurrect_sent_at"] = DateTime.UtcNow;
            commands.Add(new BridgeCommand("RESURRECT"));
        }

        return commands;
    }

    /// <summary>
    /// Ghost is walking to a safe rez spot. Check if arrived (within 5yd)
    /// or if timeout exceeded (ghost might be stuck — rez anyway).
    /// </summary>
    private List<BridgeCommand> ProcessGhostWalkToSafeSpot(BotIdentity bot, BotStateSnapshot state)
    {
        var commands = new List<BridgeCommand>();

        float targetX = bot.CurrentActivity.PhaseData.TryGetValue("safe_x", out var sx) && sx is float fx ? fx : state.X;
        float targetY = bot.CurrentActivity.PhaseData.TryGetValue("safe_y", out var sy) && sy is float fy ? fy : state.Y;

        float dist = Distance2D(state.X, state.Y, targetX, targetY);

        // Check timeout — don't let ghost walk take more than 15 seconds
        bool timedOut = false;
        if (bot.CurrentActivity.PhaseData.TryGetValue("ghost_walk_started", out var gwObj)
            && gwObj is DateTime started)
        {
            timedOut = (DateTime.UtcNow - started).TotalSeconds > 15.0;
        }

        if (dist <= 5f || timedOut)
        {
            if (timedOut && dist > 5f)
            {
                _logger.LogWarning(
                    "[BOT-MAINT] {Name} ghost walk timed out ({Dist:F0}yd from safe spot). Rezzing at current position.",
                    bot.Name, dist);
            }
            else
            {
                _logger.LogInformation(
                    "[BOT-MAINT] {Name} arrived at safe rez spot ({Dist:F1}yd from corpse). Resurrecting.",
                    bot.Name, Distance2D(state.X, state.Y, bot.CorpseX ?? state.X, bot.CorpseY ?? state.Y));
            }

            AdvanceTo(bot, "WaitingForResurrect");
            bot.CurrentActivity.PhaseData["resurrect_sent_at"] = DateTime.UtcNow;
            commands.Add(new BridgeCommand("RESURRECT"));
        }

        return commands;
    }

    /// <summary>
    /// Find a safe resurrection spot near the corpse. Samples 8 directions at
    /// REZ_OFFSET_DISTANCE yards (within WoW's ~36yd rez-at-corpse range).
    /// Returns the spot with the lowest max creature level, or null if the
    /// corpse location has no hostile creature spawns at all.
    ///
    /// This mimics a real player backing away from mobs before accepting the
    /// rez — you'd ghost-walk to the edge of the rez radius and rez there
    /// instead of on top of the mob that killed you.
    ///
    /// We ghost-walk whenever there are ANY hostile creature spawns in the
    /// corpse cell — even same-level mobs will aggro a 50% HP bot and
    /// potentially chain into a death loop.
    /// </summary>
    private (float x, float y, float z, int corpseMaxLevel, int safeMaxLevel)?
        FindSafeRezSpot(BotIdentity bot, float corpseX, float corpseY, float corpseZ, int mapId)
    {
        if (!_safetyMap.IsLoaded)
            return null;

        int corpseMaxLevel = _safetyMap.GetMaxCreatureLevel(mapId, corpseX, corpseY);

        // No creature spawns at all in this cell — safe to rez in place
        if (corpseMaxLevel == 0)
            return null;

        // Any hostiles near corpse → find the safest direction to ghost-walk
        // Sample 8 directions (N, NE, E, SE, S, SW, W, NW)
        int bestLevel = corpseMaxLevel;
        float bestX = corpseX, bestY = corpseY;
        bool foundBetter = false;

        for (int dir = 0; dir < 8; dir++)
        {
            float angle = dir * MathF.PI / 4f; // 0, 45, 90, ... degrees
            float testX = corpseX + MathF.Cos(angle) * REZ_OFFSET_DISTANCE;
            float testY = corpseY + MathF.Sin(angle) * REZ_OFFSET_DISTANCE;

            int levelAtSpot = _safetyMap.GetMaxCreatureLevel(mapId, testX, testY);

            if (levelAtSpot < bestLevel)
            {
                bestLevel = levelAtSpot;
                bestX = testX;
                bestY = testY;
                foundBetter = true;
            }
        }

        if (!foundBetter)
        {
            // Every direction is equally dangerous or worse — still pick the
            // direction with the fewest spawns. Since GetMaxCreatureLevel only
            // gives us max level, pick any direction with level 0 (empty cell).
            // If none are empty, just pick the first direction with equal level
            // to at least get 25yd of distance from the exact death spot.
            for (int dir = 0; dir < 8; dir++)
            {
                float angle = dir * MathF.PI / 4f;
                float testX = corpseX + MathF.Cos(angle) * REZ_OFFSET_DISTANCE;
                float testY = corpseY + MathF.Sin(angle) * REZ_OFFSET_DISTANCE;

                int levelAtSpot = _safetyMap.GetMaxCreatureLevel(mapId, testX, testY);
                if (levelAtSpot == 0)
                {
                    // Found an empty cell — great, rez there
                    return (testX, testY, corpseZ, corpseMaxLevel, 0);
                }
            }

            // All directions have spawns — just move away from corpse anyway.
            // Even if we can't find a perfectly safe spot, 25yd of distance
            // means the mob that killed us has to re-path to us, buying time.
            bestX = corpseX + REZ_OFFSET_DISTANCE; // default: move east
            bestY = corpseY;
            bestLevel = _safetyMap.GetMaxCreatureLevel(mapId, bestX, bestY);
        }

        return (bestX, bestY, corpseZ, corpseMaxLevel, bestLevel);
    }

    private void ProcessWaitingForResurrect(BotIdentity bot)
    {
        if (bot.CurrentActivity.PhaseData.TryGetValue("resurrect_sent_at", out var obj)
            && obj is DateTime sentAt)
        {
            float waitTime = (float)(DateTime.UtcNow - sentAt).TotalSeconds;
            if (waitTime > ResurrectTimeoutSeconds)
            {
                _logger.LogWarning(
                    "[BOT-MAINT] {Name} RESURRECT timeout ({Wait:F0}s). " +
                    "Forcing interruptible for strategic eval recovery.",
                    bot.Name, waitTime);

                bot.CurrentActivity.IsInterruptible = true;
                AdvanceTo(bot, "Alive");
                bot.CorpseX = null;
                bot.CorpseY = null;
                bot.CorpseZ = null;
                bot.CorpseMapId = null;
                bot.NextStrategicEval = DateTime.UtcNow;
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════
    // Helpers
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Single sub-phase transition point so the flight recorder sees every move.
    /// waitingOn lets a phase declare what it's blocked on: the corpse-rez delay is a
    /// TIMER wait, sitting-to-eat is a CPP recovery wait (longer sweep threshold so a
    /// normal sit isn't flagged). The RESURRECT / MOVE_TO commands set their own WAIT
    /// via the command→event map, so those phases stay plain.
    /// </summary>
    private void AdvanceTo(BotIdentity bot, string subPhase, string cause = "advance", string? waitingOn = null)
    {
        var prev = bot.CurrentActivity.SubPhase;
        bot.CurrentActivity.SubPhase = subPhase;
        _logger.LogInformation("[BOT-PHASE] {Name}({Guid}) | {Prev} → {Next}",
            bot.Name, bot.Guid, prev ?? "null", subPhase);
        BotTrace.Transition(bot, prev ?? "", subPhase, cause, waitingOn: waitingOn);
    }

    private static float Distance2D(float x1, float y1, float x2, float y2)
    {
        float dx = x1 - x2, dy = y1 - y2;
        return MathF.Sqrt(dx * dx + dy * dy);
    }
}