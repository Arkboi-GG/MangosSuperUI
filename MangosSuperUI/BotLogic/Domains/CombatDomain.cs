using MangosSuperUI.BotLogic.Core;
using MangosSuperUI.BotLogic.Tracking;

namespace MangosSuperUI.BotLogic.Domains;

/// <summary>
/// Thin on the C# side since combat execution is in C++.
/// C# controls engagement decisions and post-combat behavior.
/// On enter: sends SET_TASK GRIND to C++ so the bot autonomously patrols + kills.
/// On exit: the orchestrator (BotBrainService) sends SET_TASK IDLE to clear the grind.
///
/// Session 8: Vendoring weight uses state.FreeSlots thresholds instead of flat 0.1.
/// Same fix as QuestingDomain Session 7 — ShadowInventory.Count was deprecated.
/// </summary>
public class CombatDomain : IBotDomain
{
    public ActivityType[] OwnedActivities => new[] { ActivityType.Grinding };

    public bool IsOperational => true;

    public Dictionary<ActivityType, float> EvaluateTransitions(BotIdentity bot, BotStateSnapshot state)
    {
        var weights = new Dictionary<ActivityType, float>();

        if (state.InCombat)
        {
            // Can't transition during combat
            weights[ActivityType.Grinding] = 1.0f;
            return weights;
        }

        // Post-combat: eat if low
        if (state.HealthPercent < GetEatThreshold(bot))
            weights[ActivityType.Eating] = 1.5f;

        // Continue grinding
        float minutesGrinding = (float)bot.CurrentActivity.MinutesInState;
        float stayWeight = 0.8f;

        // Aggression → loves grinding, stays longer
        stayWeight *= Lerp(0.6f, 1.5f, bot.Personality.Aggression);

        // Boredom still applies
        float boredomPenalty = 1.0f - (minutesGrinding * Lerp(0.04f, 0.015f, bot.Personality.Patience));
        stayWeight *= Math.Max(0.2f, boredomPenalty);

        weights[ActivityType.Grinding] = stayWeight;
        weights[ActivityType.Questing] = 0.3f;
        weights[ActivityType.Exploring] = 0.05f;

        // --- Vendoring weight: FreeSlots thresholds (Session 8 fix) ---
        // Previously flat 0.1 — bots would never proactively vendor from combat.
        // These weights are NOT suppressed by combat lock since we already checked
        // InCombat above and returned early.
        //
        // Session 9 fix: Don't push vendoring if bags are nearly empty (nothing to sell).
        uint usedSlots = state.TotalSlots - state.FreeSlots;
        if (usedSlots <= 2)
        {
            weights[ActivityType.Vendoring] = 0f;
        }
        else if (state.FreeSlots == 0)
            weights[ActivityType.Vendoring] = 12.0f; // override everything — can't loot
        else if (state.FreeSlots <= 3)
            weights[ActivityType.Vendoring] = 7.0f;
        else if (state.FreeSlots <= 6)
            weights[ActivityType.Vendoring] = 2.0f;
        else
            weights[ActivityType.Vendoring] = 0.1f;

        return weights;
    }

    public List<BridgeCommand> OnEnter(BotIdentity bot, BotStateSnapshot state)
    {
        bot.CurrentActivity.IsInterruptible = !state.InCombat;
        var commands = new List<BridgeCommand>();

        // ── Session 42: grind at the GROUP ANCHOR when a directive is active ──
        // (ARCH §7a). HoldAndGrind/Regroup both converge here: far from the anchor
        // → walk there first (continuation-travel journey; the grind is armed by
        // TASK_COMPLETE in OnEvent); near it → grind centered on the anchor so the
        // group bunches in one spot. Solo bots (no directive) grind in place,
        // unchanged. This retires gotcha-96's solo-grind-anywhere for grouped bots.
        if (HasActiveAnchorDirective(bot, state))
        {
            float dx = state.X - bot.GroupAnchorX, dy = state.Y - bot.GroupAnchorY;
            float dist = MathF.Sqrt(dx * dx + dy * dy);
            if (dist > 80f)
            {
                bot.CurrentActivity.PhaseData["anchor_travel"] = true;
                bot.CurrentActivity.ContextTag = $"zone:{state.ZoneId}:regroup:{(int)dist}yd";
                commands.Add(new BridgeCommand("MOVE_TO", new
                {
                    mapId = state.MapId,
                    x = bot.GroupAnchorX,
                    y = bot.GroupAnchorY,
                    z = bot.GroupAnchorZ
                }));
                return commands;
            }

            bot.CurrentActivity.ContextTag = $"zone:{state.ZoneId}:grind:anchor";
            commands.Add(BuildGrind(bot, bot.GroupAnchorX, bot.GroupAnchorY, bot.GroupAnchorZ));
            return commands;
        }

        // Solo / no directive: grind in the bot's current area (original behavior)
        bot.CurrentActivity.ContextTag = $"zone:{state.ZoneId}:grind";
        commands.Add(BuildGrind(bot, state.X, state.Y, state.Z));
        return commands;
    }

    public List<BridgeCommand> OnTick(BotIdentity bot, BotStateSnapshot state)
    {
        // Update interruptibility based on combat state
        bot.CurrentActivity.IsInterruptible = !state.InCombat;
        var commands = new List<BridgeCommand>();

        // ── Session 42: walking to the anchor — the MOVE_TO's WAIT TASK_COMPLETE
        // owns this phase. Do NOT assert cpp:grind (no grind is armed yet); doing
        // so every tick would also bury the travel WAIT the recorder is watching.
        if (bot.CurrentActivity.PhaseData.ContainsKey("anchor_travel"))
            return commands;

        // ── Session 42: re-anchor a grind centered in the wrong place ────────
        // The bot may have ENTERED Grinding before the directive existed, or the
        // anchor moved. If the stored grind center is >100yd off the live anchor,
        // re-run the anchor logic. Throttled to once per 60s (we just spent a
        // session killing an event-rate loop — never re-send unthrottled). Anchors
        // are stable while directives hold: the leader is itself forced to grind
        // AT the anchor (its own position), so it stays put.
        if (HasActiveAnchorDirective(bot, state) && !state.InCombat)
        {
            var pd = bot.CurrentActivity.PhaseData;
            bool checkDue = !pd.TryGetValue("anchor_check", out var lastObj)
                || lastObj is not DateTime last
                || (DateTime.UtcNow - last).TotalSeconds >= 60;
            if (checkDue)
            {
                pd["anchor_check"] = DateTime.UtcNow;
                float cx = PhaseFloat(bot, "grind_cx", state.X);
                float cy = PhaseFloat(bot, "grind_cy", state.Y);
                float odx = cx - bot.GroupAnchorX, ody = cy - bot.GroupAnchorY;
                float offBy = MathF.Sqrt(odx * odx + ody * ody);
                if (offBy > 100f)
                {
                    BotTrace.Mark(bot, $"re-anchor: grind center {(int)offBy}yd off group anchor", state);
                    float dx = state.X - bot.GroupAnchorX, dy = state.Y - bot.GroupAnchorY;
                    if (MathF.Sqrt(dx * dx + dy * dy) > 80f)
                    {
                        pd["anchor_travel"] = true;
                        bot.CurrentActivity.ContextTag = $"zone:{state.ZoneId}:regroup";
                        commands.Add(new BridgeCommand("MOVE_TO", new
                        {
                            mapId = state.MapId,
                            x = bot.GroupAnchorX,
                            y = bot.GroupAnchorY,
                            z = bot.GroupAnchorZ
                        }));
                        return commands;
                    }
                    bot.CurrentActivity.ContextTag = $"zone:{state.ZoneId}:grind:anchor";
                    commands.Add(BuildGrind(bot, bot.GroupAnchorX, bot.GroupAnchorY, bot.GroupAnchorZ));
                    return commands;
                }
            }
        }

        // FLIGHT RECORDER: the grind is an indefinite C++ hand-off (SET_TASK kill_count=0),
        // so there's no TASK_COMPLETE to wait on — owner is CPP, not WAIT. This overrides
        // the WAIT the SET_TASK command set on entry; emits once, then KILL pings keep it
        // alive (see OnEvent). Without this the indefinite grind would trip the WAIT sweep.
        BotTrace.Wait(bot, WaitOn.Cpp("grind"), "grinding", state);

        return commands;
    }

    public List<BridgeCommand> OnEvent(BotIdentity bot, BotStateSnapshot state, BotEvent evt)
    {
        var commands = new List<BridgeCommand>();

        if (evt.EventType == "KILL")
        {
            // Note the kill for activity context
            bot.CurrentActivity.ContextTag = $"zone:{state.ZoneId}:kill:{evt.CreatureEntry}";
            // FLIGHT RECORDER: a kill is the grind's progress signal — re-arm the CPP sweep
            // so an actively-killing bot is never flagged; only one that stopped killing trips it.
            BotTrace.Ping(bot);
        }
        else if (evt.EventType == "TASK_COMPLETE")
        {
            // ── Session 42: arrived at the group anchor — arm the grind there.
            if (bot.CurrentActivity.PhaseData.Remove("anchor_travel"))
            {
                bot.CurrentActivity.ContextTag = $"zone:{state.ZoneId}:grind:anchor";
                commands.Add(BuildGrind(bot, bot.GroupAnchorX, bot.GroupAnchorY, bot.GroupAnchorZ));
            }
            else
            {
                // Grind task finished (kill_count reached) — mark for transition
                bot.CurrentActivity.ContextTag = $"zone:{state.ZoneId}:grind:complete";
            }
        }
        else if ((evt.EventType == "MOVE_FAILED" || evt.EventType == "PATH_UNSAFE")
                 && bot.CurrentActivity.PhaseData.Remove("anchor_travel"))
        {
            // ── Session 42: can't reach the anchor — grind in place rather than
            // wedge. The next directive recompute / 60s re-anchor check retries.
            bot.CurrentActivity.ContextTag = $"zone:{state.ZoneId}:grind:anchor_unreachable";
            commands.Add(BuildGrind(bot, state.X, state.Y, state.Z));
        }

        return commands;
    }

    /// <summary>
    /// HP threshold at which bot decides to eat. Modified by Cautiousness.
    /// </summary>
    public static float GetEatThreshold(BotIdentity bot)
    {
        return Lerp(0.3f, 0.7f, bot.Personality.Cautiousness);
    }

    /// <summary>
    /// Whether this bot should engage a creature at the given level delta.
    /// </summary>
    public static bool ShouldEngage(BotIdentity bot, int creatureLevel, int botLevel)
    {
        int delta = creatureLevel - botLevel;
        int maxDelta = 2;

        maxDelta += (int)(bot.Personality.Aggression * 2);
        maxDelta -= (int)(bot.Personality.Cautiousness * 1.5f);

        // Quirk overrides
        foreach (var quirk in bot.Personality.Quirks)
        {
            float quirkDelta = quirk.GetFloat("Combat.MaxLevelDelta", -1f);
            if (quirkDelta >= 0) { maxDelta = (int)quirkDelta; break; }
        }

        return delta <= maxDelta;
    }

    // ── Session 42: GroupCoordinator executors (ARCH §7a) ───────────────

    /// <summary>
    /// Build the SET_TASK GRIND and remember its center in PhaseData so OnTick
    /// can detect a grind anchored in the wrong place (re-anchor check).
    /// Session 44b: GroupErrand anchors use the tightest radius the C++
    /// allows — BridgeHandleSetTask clamps radius &lt; 10.0f up to 40, and
    /// DoGrindPatrol hops to a random point INSIDE the radius every 3-6s
    /// (confirmed in source: a grind never stands still). 10yd keeps the
    /// leader's meander within SELL_ITEMS' strict 15yd NPC search.
    /// Session 45: GrindToFund is also a GroupErrand but must actually FIND mobs, so it
    /// carries a wide radius via bot.GroupAnchorRadius (50 = the C++ SelectGrindTarget
    /// search cap) instead of the 10yd service hold. Train/Vendor stops still use 10.
    /// </summary>
    private static BridgeCommand BuildGrind(BotIdentity bot, float x, float y, float z)
    {
        float radius = bot.GroupDirective == GroupDirective.GroupErrand
            ? bot.GroupAnchorRadius   // Session 45: service stops hold tight (10); GrindToFund grinds wide (50)
            : 60.0f;
        bot.CurrentActivity.PhaseData["grind_cx"] = x;
        bot.CurrentActivity.PhaseData["grind_cy"] = y;
        return new BridgeCommand("SET_TASK", new
        {
            task = "GRIND",
            x,
            y,
            z,
            radius,
            creature_entry = 0,   // kill anything hostile
            kill_count = 0        // indefinite — C# transitions away via decision engine
        });
    }

    /// <summary>
    /// Directive says converge/hold at the group anchor: HoldAndGrind, Regroup, or
    /// GroupErrand (Session 44b — anchor = the service NPC the whole team is
    /// visiting), fresh (<2 min — staleness guard mirrors DecisionEngine), and the
    /// anchor is on this map (cross-map convergence can't be walked — grind in
    /// place instead).
    /// </summary>
    private static bool HasActiveAnchorDirective(BotIdentity bot, BotStateSnapshot state)
    {
        return bot.IsGrouped
            && (bot.GroupDirective == GroupDirective.HoldAndGrind
                || bot.GroupDirective == GroupDirective.Regroup
                || bot.GroupDirective == GroupDirective.GroupErrand)
            && (DateTime.UtcNow - bot.GroupDirectiveUtc).TotalSeconds < 120
            && bot.GroupAnchorMap == state.MapId;
    }

    private static float PhaseFloat(BotIdentity bot, string key, float fallback)
        => bot.CurrentActivity.PhaseData.TryGetValue(key, out var v) && v is float f ? f : fallback;

    private static float Lerp(float min, float max, float t) => min + (max - min) * t;
}