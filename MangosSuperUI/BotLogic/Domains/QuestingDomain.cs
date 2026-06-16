using MangosSuperUI.BotLogic.Core;
using MangosSuperUI.BotLogic.Data;
using MangosSuperUI.BotLogic.Tracking;
using MangosSuperUI.Models;
using Microsoft.Extensions.Logging;

namespace MangosSuperUI.BotLogic.Domains;

/// <summary>
/// The most complex domain. Manages the quest lifecycle with multi-quest batching:
///   PickingQuests → TravelingToGiver → AcceptingQuests → TravelingToObjective →
///   DoingObjectives → TravelingToTurnIn → TurningIn → BatchComplete → (loop)
///
/// SESSION 14 CHANGES:
///
/// 1. ZONE SAFETY FILTER — Hard-rejects quests whose travel paths cross through
///    zones with creatures significantly above the bot's level. Uses ZoneSafetyMap
///    spatial grid (100yd cells of creature levels). This is the fix for the
///    Session 13 death loop — bots walking through Redridge at level 2.
///    - Hard distance cap by level (level 1-3: 400yd, 4-6: 800yd, etc.)
///    - Path safety check: bot→giver→objective→turnin sampled for max creature level
///    - Safety margin: botLevel + 3 (creatures ≤3 levels above are acceptable)
///    - Replaces the soft "starter zone stickiness" weight penalty with a hard gate
///
/// 2. MULTI-QUEST BATCHING — Bots now accept multiple quests from nearby NPCs and
///    work overlapping objectives. Like a real player at Marshal McBride: grab all
///    available quests, kill wolves AND kobolds on the way, turn in everything.
///    - ActiveQuests list on PhaseData instead of single ActiveQuestId
///    - PickingQuests selects a batch of 1-5 quests from nearby givers
///    - Objective targeting picks the nearest incomplete objective across ALL quests
///    - Turn-in visits each completed quest's NPC in nearest-first order
///    - Opportunistic objective: while traveling, if within 40yd of an active
///      quest objective, interrupt to complete it before continuing
///
/// 3. OPPORTUNISTIC OBJECTIVE COMPLETION — While traveling to any destination,
///    if the bot is within range of an active quest objective (grind area), it
///    interrupts travel to complete that objective before continuing.
///
/// KEY INVARIANTS:
/// - Zone safety is a HARD FILTER, not a soft weight.
/// - BotIdentity.ActiveQuestId still set (to primary quest for backward compat).
/// - Each quest gets its own QUEST_INTERACT accept/complete call.
/// - Quest log limit respected (20 slots vanilla).
/// </summary>
public class QuestingDomain : IBotDomain
{
    private readonly QuestGraphLoader _questGraph;
    private readonly ZoneSafetyMap _safetyMap;
    private readonly ILogger<QuestingDomain> _logger;
    private readonly ConnectionFactory _db;
    private readonly BotFleetDiagnostics _diagnostics;

    private const int MAX_BATCH_SIZE = 5;
    private const int MAX_QUEST_LOG = 20;
    private const float BATCH_GIVER_RADIUS = 150f;
    private const float OPPORTUNISTIC_OBJECTIVE_RADIUS = 40f;
    private const int SAFETY_MARGIN = 3;

    // Session 36: DoingObjectives no-progress guard. A bot grinding an objective
    // whose KILL/LOOT stream dries up (spawn depleted, grind task died, stale
    // current_grind_creature) used to sit in owner=CS limbo until the 20-min
    // watchdog. Relocate-and-re-grind after DO_NO_PROGRESS_SEC; after
    // DO_MAX_NOPROGRESS_REARMS unproductive relocations, defer the blocking
    // quest(s) and re-pick. Turns a ~13-min freeze into a ~90s self-heal.
    private const double DO_NO_PROGRESS_SEC = 90.0;
    private const int DO_MAX_NOPROGRESS_REARMS = 3;
    private const int DO_NOPROGRESS_DEFER_MIN = 15;
    private const double DO_QUEST_RESYNC_SEC = 6.0;   // S47: min gap between mid-grind QUEST_STATUS re-queries

    // Session 36 (F3): a turn-in NPC that C++ reports unreachable (incomplete_path/
    // no_path) is a navmesh dead-end, not a level-gated danger. Defer EVERY quest
    // sharing that turn-in in one shot, for longer, so siblings stop re-routing to
    // the same unreachable coordinate.
    private const int F3_TURNIN_DEFER_MIN = 30;

    // ── Session 38: force_* server-side credit for WMO-interior NPCs ─────
    // C++ BridgeHandleQuestInteract (deployed, verified via "bypassing
    // proximity" string) supports action "force_accept"/"force_complete":
    // 300yd NPC search, proximity skipped, ALL eligibility gates intact
    // (CanRewardQuest / CanTakeQuest / CanAddQuest). We arm it only when a
    // turn-in/giver path fails on the LAST LEG — bot within
    // FORCE_INTERACT_TRIGGER_RADIUS 2D of the failed dest — i.e. a
    // vertical/WMO problem (Brother Neals' bell tower), never a long-range
    // routing failure. Long-range failures keep the normal cluster-defer so
    // force can't mask real path bugs or an off-mesh bot.
    private const float FORCE_INTERACT_TRIGGER_RADIUS = 150f; // bot→failed-dest gate
    private const float FORCE_INTERACT_NPC_RADIUS = 180f;     // widened interact radius in force mode (C++ searches 300yd)
    private const int FORCE_MODE_TTL_SEC = 120;               // force mode auto-expires

    // ── Session 44: formation quest-sync leader hold ──
    // Max seconds the leader waits at a giver/ender for glued followers to match
    // its accepts/turn-ins (flags computed by the BotBrainService coordinator from
    // the group quest batch). Timeout-capped so a wedged/ineligible follower can
    // never freeze the leader (the June-6 lesson); after a timed-out hold the
    // leader won't re-hold in the same sub-phase for HOLD_GIVEUP_COOLDOWN_SEC.
    private const double GROUP_HOLD_TIMEOUT_SEC = 45.0;
    private const double GROUP_HOLD_GIVEUP_COOLDOWN_SEC = 120.0;

    // ── Stuck-aware quest reach (dynamic travel cap, June 13) ──
    // A bot that filters down to 0 pickable quests starts a "no-quest streak". The
    // longer it runs, the wider the travel cap (GetMaxTravelDistance escalationTier).
    // Self-heal for a bad/zero ZoneId collapsing the cap to 800yd and wedging the fleet
    // in NoQuestsAvailable: tight by default, widens only under proven stuckness, RESETS
    // the instant a pick succeeds. Distance only — creature path safety stays a hard,
    // independent gate. Rule A -> B -> C on a time gate.
    private const double ESC_TIER1_SEC = 90.0;    // A -> B
    private const double ESC_TIER2_SEC = 240.0;   // B -> C
    private const double ESC_TIER3_SEC = 480.0;   // C -> max
    private readonly Dictionary<long, DateTime> _noQuestStreakStartUtc = new();

    private int QuestReachTier(long guid)
    {
        if (!_noQuestStreakStartUtc.TryGetValue(guid, out var start)) return 0;
        double s = (DateTime.UtcNow - start).TotalSeconds;
        if (s >= ESC_TIER3_SEC) return 3;
        if (s >= ESC_TIER2_SEC) return 2;
        if (s >= ESC_TIER1_SEC) return 1;
        return 0;
    }

    public QuestingDomain(QuestGraphLoader questGraph, ZoneSafetyMap safetyMap,
        ILogger<QuestingDomain> logger, ConnectionFactory db, BotFleetDiagnostics diagnostics)
    {
        _questGraph = questGraph;
        _safetyMap = safetyMap;
        _logger = logger;
        _db = db;
        _diagnostics = diagnostics;
    }

    public ActivityType[] OwnedActivities => new[]
    {
        ActivityType.Questing,
        ActivityType.TravelingToQuest,
        ActivityType.Idle
    };

    public bool IsOperational => true;

    // ════════════════════════════════════════════════════════════════════
    // ActiveQuests batch tracking (stored in PhaseData)
    // ════════════════════════════════════════════════════════════════════

    public class ActiveQuestEntry
    {
        public int QuestId { get; set; }
        public string Title { get; set; } = "";
        public QuestNode Node { get; set; } = null!;
        public bool Accepted { get; set; }
        public bool ServerComplete { get; set; }
        public bool TurnedIn { get; set; }
        public float Progress { get; set; }
        public Dictionary<int, int> KillProgress { get; set; } = new();
        public Dictionary<int, int> ItemProgress { get; set; } = new();
    }

    public List<ActiveQuestEntry> GetActiveQuests(BotIdentity bot)
    {
        if (bot.CurrentActivity.PhaseData.TryGetValue("active_quests", out var obj)
            && obj is List<ActiveQuestEntry> list)
            return list;
        return new List<ActiveQuestEntry>();
    }

    public void SetActiveQuests(BotIdentity bot, List<ActiveQuestEntry> quests)
    {
        bot.CurrentActivity.PhaseData["active_quests"] = quests;
        bot.ActiveQuestId = quests.FirstOrDefault()?.QuestId;
    }

    // ════════════════════════════════════════════════════════════════════
    // EvaluateTransitions
    // ════════════════════════════════════════════════════════════════════

    public Dictionary<ActivityType, float> EvaluateTransitions(BotIdentity bot, BotStateSnapshot state)
    {
        var weights = new Dictionary<ActivityType, float>();
        var personality = bot.Personality;
        var minutesInActivity = bot.CurrentActivity.MinutesInState;
        var subPhase = bot.CurrentActivity.SubPhase ?? "PickingQuests";

        if (subPhase == "NoQuestsAvailable")
        {
            weights[ActivityType.Questing] = 0.1f;
            weights[ActivityType.Grinding] = 2.0f;
            weights[ActivityType.Exploring] = 0.5f;
            if (state.HealthPercent < 0.5f || state.ManaPercent < 0.3f)
                weights[ActivityType.Eating] = 0.6f;
            bot.Story?.Emit(StoryVerb.EVAL, StoryResult.BLOCK, reason: "no_quests_subphase", detail: "NoQuestsAvailable -> grind 2.0");
            return weights;
        }

        var activeQuests = GetActiveQuests(bot);
        bool activelyWorking = activeQuests.Count > 0
                               && subPhase != "PickingQuests" && subPhase != "BatchComplete";

        float stayWeight;

        if (activelyWorking)
        {
            stayWeight = 5.0f;
            if (subPhase == "DoingObjectives") stayWeight = 8.0f;

            float avgProgress = activeQuests.Count > 0
                ? activeQuests.Average(q => q.Progress) : 0f;
            if (avgProgress >= 0.7f) stayWeight *= 2.0f;

            // More quests in batch = more committed
            stayWeight *= 1.0f + (activeQuests.Count - 1) * 0.3f;

            float boredomRate = Lerp(0.008f, 0.003f, personality.Patience);
            float boredomPenalty = 1.0f - (float)(minutesInActivity * boredomRate);
            stayWeight *= Math.Max(0.5f, boredomPenalty);
        }
        else
        {
            stayWeight = 1.0f;
            if (bot.XPPercent > 0.85f) stayWeight *= 1.4f;
            stayWeight *= Lerp(0.8f, 1.3f, personality.Efficiency);

            float boredomRate = Lerp(0.03f, 0.01f, personality.Patience);
            float boredomPenalty = 1.0f - (float)(minutesInActivity * boredomRate);
            stayWeight *= Math.Max(0.3f, boredomPenalty);
        }

        if (!_questGraph.IsLoaded || (!activelyWorking && !HasAvailableQuests(bot, state.MapId)))
        {
            stayWeight *= 0.1f;
            weights[ActivityType.Grinding] = 0.8f;
            bot.Story?.Emit(StoryVerb.EVAL, StoryResult.BLOCK, reason: _questGraph.IsLoaded ? "no_available_quests" : "questgraph_unloaded", detail: $"map={state.MapId} stay*0.1 grind=0.8");
        }
        else
        {
            weights[ActivityType.Grinding] = activelyWorking ? 0.02f : 0.1f;
        }

        weights[ActivityType.Questing] = stayWeight;

        float suppressFactor = activelyWorking ? 0.1f : 1.0f;

        float vendorWeight;
        if (state.FreeSlots == 0)
            vendorWeight = 12.0f;
        else if (state.FreeSlots <= 3)
            vendorWeight = 7.0f;
        else if (state.FreeSlots <= 6)
            vendorWeight = 2.0f * suppressFactor;
        else
            vendorWeight = 0.05f * suppressFactor;
        weights[ActivityType.Vendoring] = vendorWeight;

        if (bot.HasUnlearnedSpells)
        {
            // Training weight is NOT suppressed by activelyWorking — bots should
            // train even mid-quest-chain. Base 0.4, scales up with ignored ticks.
            // At 10+ ticks, the DecisionEngine hard override takes over anyway.
            float trainBase = 0.4f + (bot.TicksSinceLastTrained * 0.15f);
            weights[ActivityType.TravelingToTrainer] = Math.Min(trainBase, 5.0f);
        }
        weights[ActivityType.AuctionHouse] = 0.02f * suppressFactor;
        weights[ActivityType.Exploring] = 0.05f * suppressFactor;
        if (state.IsNearTown)
            weights[ActivityType.Socializing] = 0.08f * suppressFactor;
        if (state.HealthPercent < 0.5f || state.ManaPercent < 0.3f)
            weights[ActivityType.Eating] = 0.6f;

        return weights;
    }

    // ════════════════════════════════════════════════════════════════════
    // OnEnter
    // ════════════════════════════════════════════════════════════════════

    public List<BridgeCommand> OnEnter(BotIdentity bot, BotStateSnapshot state)
    {
        var commands = new List<BridgeCommand>();
        bot.ResetDeathCounter();

        // ── Session 29: Always ask C++ for authoritative quest state ──
        // Every time we enter Questing (whether from connect, vendor detour, training,
        // eating, or anything else), ask C++ for the real quest log. The response
        // arrives as QUEST_STATUS_ALL event → OnEvent rebuilds active_quests from it.
        // This replaces the one-shot HydratedActiveQuests which was lost on activity switch.
        bot.CurrentActivity.SubPhase = "WaitingForQuestSync";
        bot.CurrentActivity.ContextTag = "quests:syncing";
        bot.CurrentActivity.IsInterruptible = false; // don't let strategic eval interrupt sync
        commands.Add(new BridgeCommand("QUERY_QUEST_STATUS", new { }));

        // FLIGHT RECORDER: entering Questing parks here until C++ answers QUEST_STATUS_ALL.
        BotTrace.Transition(bot, "", "WaitingForQuestSync", "enter_questing",
            waitingOn: WaitOn.Event("QUEST_STATUS_ALL"), state: state);

        _logger.LogInformation(
            "[BOT-QUEST] {Name}({Guid}) | OnEnter — requesting C++ quest status sync",
            bot.Name, bot.Guid);

        return commands;
    }

    /// <summary>
    /// Session 29: Rebuild active_quests from C++ QUEST_STATUS_ALL response.
    /// Called from OnEvent when the sync response arrives. Contains the same
    /// rehydration logic that was previously in OnEnter, but now uses live C++
    /// data instead of the one-shot HydratedActiveQuests cache.
    ///
    /// Payload format: "questId:status:mob1,mob2,mob3,mob4:item1,item2,item3,item4|..."
    /// </summary>
    private List<BridgeCommand> RebuildFromQuestSync(BotIdentity bot, BotStateSnapshot state, string payload)
    {
        var commands = new List<BridgeCommand>();
        var rehydrated = new List<ActiveQuestEntry>();

        if (!string.IsNullOrEmpty(payload))
        {
            var entries = payload.Split('|', StringSplitOptions.RemoveEmptyEntries);
            foreach (var entry in entries)
            {
                // Format: questId:status:mob1,mob2,mob3,mob4:item1,item2,item3,item4
                var parts = entry.Split(':');
                if (parts.Length < 4) continue;

                if (!int.TryParse(parts[0], out int questId)) continue;
                if (!int.TryParse(parts[1], out int status)) continue;

                var mobParts = parts[2].Split(',');
                var itemParts = parts[3].Split(',');

                int[] mobCounts = new int[4];
                int[] itemCounts = new int[4];
                for (int i = 0; i < 4 && i < mobParts.Length; i++)
                    int.TryParse(mobParts[i], out mobCounts[i]);
                for (int i = 0; i < 4 && i < itemParts.Length; i++)
                    int.TryParse(itemParts[i], out itemCounts[i]);

                var node = _questGraph.GetQuest(questId);
                if (node == null) continue;

                var aq = new ActiveQuestEntry
                {
                    QuestId = questId,
                    Title = node.Title,
                    Node = node,
                    Accepted = true,
                    TurnedIn = false,
                };

                // Populate KillProgress from C++ mob_count fields
                if (node.Objectives != null)
                {
                    int idx = 0;
                    foreach (var obj in node.Objectives)
                    {
                        if (idx >= 4) break;
                        if (obj.IsCreature && mobCounts[idx] > 0)
                            aq.KillProgress[obj.Slot] = mobCounts[idx];
                        idx++;
                    }
                }

                // Populate ItemProgress from C++ item_count fields
                if (node.ItemObjectives != null)
                {
                    int idx = 0;
                    foreach (var itemObj in node.ItemObjectives)
                    {
                        if (idx >= 4) break;
                        if (itemCounts[idx] > 0)
                            aq.ItemProgress[itemObj.ItemId] = itemCounts[idx];
                        idx++;
                    }
                }

                // VMaNGOS quest status enum: 1=COMPLETE, 3=INCOMPLETE
                // Do NOT use status==3 as "complete" — that's INCOMPLETE.
                bool objectivesDone = (status == 1) || AllObjectivesComplete(aq);
                aq.ServerComplete = objectivesDone;
                UpdateQuestProgress(aq);

                rehydrated.Add(aq);
            }
        }

        // Also consume any one-shot HydratedActiveQuests (first connect only)
        bot.HydratedActiveQuests = null;

        if (rehydrated.Count > 0)
        {
            // Distance sanity check: drop quests whose NPC is beyond max travel distance
            float maxDist = ZoneSafetyMap.GetMaxTravelDistance(bot.Level, state.ZoneId);
            int dropped = 0;
            for (int i = rehydrated.Count - 1; i >= 0; i--)
            {
                var aq = rehydrated[i];
                float questDist = float.MaxValue;

                if (aq.Node.Giver != null && aq.Node.Giver.Map == state.MapId)
                    questDist = Math.Min(questDist, Distance2D(state.X, state.Y, aq.Node.Giver.X, aq.Node.Giver.Y));
                if (aq.Node.TurnIn != null && aq.Node.TurnIn.Map == state.MapId)
                    questDist = Math.Min(questDist, Distance2D(state.X, state.Y, aq.Node.TurnIn.X, aq.Node.TurnIn.Y));

                if (questDist > maxDist)
                {
                    // ── Session 45: NEVER drop a READY (server-complete) quest for distance. ──
                    // Dropping banked turn-ins created the level-2 wedge: the bot cannot
                    // level without turning in, and cannot turn in because the ender sits
                    // past GetMaxTravelDistance for its (stuck) level. The cap exists to
                    // stop low-level bots STARTING far quests; a complete quest is banked
                    // XP and the walk itself is policed by C++ path safety (PATH_UNSAFE),
                    // the same authority principle as the §15.26 IsQuestPathSafe removal.
                    if (aq.ServerComplete)
                    {
                        _logger.LogInformation(
                            "[BOT-QUEST] {Name}({Guid}) | Keeping READY synced quest [{QuestId}] \"{Title}\" despite distance — " +
                            "turn-in NPC {Dist:F0}yd away (max {Max:F0}yd for level {Level})",
                            bot.Name, bot.Guid, aq.QuestId, aq.Title, questDist, maxDist, bot.Level);
                        continue;
                    }

                    _logger.LogWarning(
                        "[BOT-QUEST] {Name}({Guid}) | Dropping synced quest [{QuestId}] \"{Title}\" — " +
                        "nearest NPC is {Dist:F0}yd away (max {Max:F0}yd for level {Level})",
                        bot.Name, bot.Guid, aq.QuestId, aq.Title, questDist, maxDist, bot.Level);
                    rehydrated.RemoveAt(i);
                    dropped++;
                }
            }

            SetActiveQuests(bot, rehydrated);

            _logger.LogInformation(
                "[BOT-QUEST] {Name}({Guid}) | Synced {Count} quests from C++ (dropped {Dropped}): {Quests}",
                bot.Name, bot.Guid, rehydrated.Count, dropped,
                string.Join(", ", rehydrated.Select(q =>
                    $"[{q.QuestId}]\"{q.Title}\"({(q.ServerComplete ? "READY" : $"{q.Progress:P0}")})")));

            // Check for turn-in-ready quests FIRST
            var turnIn = GetNearestCompletedTurnIn(bot, state, rehydrated);
            if (turnIn != null)
            {
                AdvanceTo(bot, "TravelingToTurnIn");
                bot.CurrentActivity.ContextTag = $"quests:batch:{rehydrated.Count}";
                bot.CurrentActivity.IsInterruptible = true;
                commands.Add(MakeMoveTo(turnIn.Value.x, turnIn.Value.y, turnIn.Value.z, turnIn.Value.map,
                    fromX: state.X, fromY: state.Y));
                return commands;
            }

            // Then check for incomplete objectives
            var target = GetNearestObjectiveAcrossQuests(bot, state, rehydrated);
            if (target != null)
            {
                AdvanceTo(bot, "TravelingToObjective");
                bot.CurrentActivity.ContextTag = $"quests:batch:{rehydrated.Count}";
                bot.CurrentActivity.IsInterruptible = true;
                commands.Add(MakeMoveTo(target.Value.x, target.Value.y, target.Value.z, target.Value.map));
                return commands;
            }
        }

        // No active quests from C++ (or all dropped) — pick new ones
        bot.CurrentActivity.IsInterruptible = true;
        AdvanceTo(bot, "PickingQuests");
        commands.AddRange(ProcessPickingQuests(bot, state));
        return commands;
    }

    /// <summary>
    /// Session 47: lightweight IN-PLACE refresh of KillProgress / ItemProgress /
    /// ServerComplete on the EXISTING active-quest list from a QUEST_STATUS_ALL
    /// payload. Unlike RebuildFromQuestSync this does NOT rebuild the list,
    /// distance-drop, or change sub-phase — safe to call mid-grind. Server-truth
    /// mob counts (which include group-credited kills the leader never
    /// self-tapped) overwrite the local self-tap tally. Returns true if any quest
    /// NEWLY flipped to ServerComplete (caller stops the grind).
    /// </summary>
    private bool RefreshProgressFromQuestSync(BotIdentity bot, string payload)
    {
        if (string.IsNullOrEmpty(payload)) return false;

        var active = GetActiveQuests(bot);
        if (active.Count == 0) return false;

        bool newlyComplete = false;

        foreach (var entry in payload.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            // Format: questId:status:mob1,mob2,mob3,mob4:item1,item2,item3,item4
            var parts = entry.Split(':');
            if (parts.Length < 4) continue;
            if (!int.TryParse(parts[0], out int questId)) continue;
            if (!int.TryParse(parts[1], out int status)) continue;

            var aq = active.FirstOrDefault(q => q.QuestId == questId);
            if (aq?.Node == null) continue;

            var mobParts = parts[2].Split(',');
            int[] mobCounts = new int[4];
            for (int i = 0; i < 4 && i < mobParts.Length; i++)
                int.TryParse(mobParts[i], out mobCounts[i]);

            var itemParts = parts[3].Split(',');
            int[] itemCounts = new int[4];
            for (int i = 0; i < 4 && i < itemParts.Length; i++)
                int.TryParse(itemParts[i], out itemCounts[i]);

            // Server truth overwrites the local self-tap count (group credit).
            if (aq.Node.Objectives != null)
            {
                int idx = 0;
                foreach (var obj in aq.Node.Objectives)
                {
                    if (idx >= 4) break;
                    if (obj.IsCreature) aq.KillProgress[obj.Slot] = mobCounts[idx];
                    idx++;
                }
            }
            if (aq.Node.ItemObjectives != null)
            {
                int idx = 0;
                foreach (var itemObj in aq.Node.ItemObjectives)
                {
                    if (idx >= 4) break;
                    if (itemCounts[idx] > 0) aq.ItemProgress[itemObj.ItemId] = itemCounts[idx];
                    idx++;
                }
            }

            // VMaNGOS quest status: 1=COMPLETE, 3=INCOMPLETE.
            bool done = (status == 1) || AllObjectivesComplete(aq);
            if (done && !aq.ServerComplete)
            {
                newlyComplete = true;
                _logger.LogInformation(
                    "[BOT-QUEST] {Name}({Guid}) | Mid-grind resync: [{QuestId}] \"{Title}\" server-COMPLETE — stopping grind",
                    bot.Name, bot.Guid, aq.QuestId, aq.Title);
            }
            aq.ServerComplete = done;
            UpdateQuestProgress(aq);
        }

        return newlyComplete;
    }

    // ════════════════════════════════════════════════════════════════════
    // OnTick
    // ════════════════════════════════════════════════════════════════════

    public List<BridgeCommand> OnTick(BotIdentity bot, BotStateSnapshot state)
    {
        var commands = new List<BridgeCommand>();

        // ── Session 31: Group follower regroup — if >100yd from leader, drop everything and path back ──
        if (bot.IsGroupFollower && state.LeaderX.HasValue)
        {
            float leaderDist = Distance2D(state.X, state.Y, state.LeaderX.Value, state.LeaderY!.Value);
            if (leaderDist > 100f)
            {
                _logger.LogWarning("[BOT-GROUP] {Name}({Guid}) | Too far from leader ({Dist:F0}yd) — regrouping",
                    bot.Name, bot.Guid, leaderDist);
                commands.Add(new BridgeCommand("MOVE_TO", new
                {
                    mapId = state.MapId,
                    x = state.LeaderX.Value,
                    y = state.LeaderY!.Value,
                    z = state.LeaderZ!.Value
                }));
                return commands;
            }
        }

        if (bot.HasUnlearnedSpells)
            bot.TicksSinceLastTrained++;

        int maxChain = 8;
        for (int i = 0; i < maxChain; i++)
        {
            var subPhase = bot.CurrentActivity.SubPhase ?? "PickingQuests";
            string prevPhase = subPhase;

            switch (subPhase)
            {
                case "WaitingForQuestSync":
                    // Session 29: waiting for C++ QUEST_STATUS_ALL response.
                    // Timeout after 5 seconds — fall back to PickingQuests if C++ didn't respond.
                    if (bot.CurrentActivity.MinutesInState > 0.08) // ~5 seconds
                    {
                        _logger.LogWarning(
                            "[BOT-QUEST] {Name}({Guid}) | QUEST_STATUS_ALL timeout — falling back to PickingQuests",
                            bot.Name, bot.Guid);
                        bot.CurrentActivity.IsInterruptible = true;
                        AdvanceTo(bot, "PickingQuests");
                        commands.AddRange(ProcessPickingQuests(bot, state));
                    }
                    break;
                case "PickingQuests":
                    commands.AddRange(ProcessPickingQuests(bot, state));
                    break;
                case "TravelingToGiver":
                    commands.AddRange(ProcessTravelingToGiver(bot, state));
                    break;
                case "AcceptingQuests":
                    commands.AddRange(ProcessAcceptingQuests(bot, state));
                    break;
                case "TravelingToObjective":
                    commands.AddRange(ProcessTravelingToObjective(bot, state));
                    break;
                case "DoingObjectives":
                    commands.AddRange(ProcessDoingObjectives(bot, state));
                    break;
                case "TravelingToTurnIn":
                    commands.AddRange(ProcessTravelingToTurnIn(bot, state));
                    break;
                case "TurningIn":
                    commands.AddRange(ProcessTurningIn(bot, state));
                    break;
                case "BatchComplete":
                    commands.AddRange(ProcessBatchComplete(bot, state));
                    break;
                case "NoQuestsAvailable":
                    break;
            }

            string newPhase = bot.CurrentActivity.SubPhase ?? "PickingQuests";
            if (newPhase == prevPhase) break;

            // Break-points: stop chaining when we enter a phase that waits for
            // external events or long travel. AcceptingQuests and TurningIn are NOT
            // break-points — they fire one QUEST_INTERACT and return, then the ACK
            // event drives the next one (see QUEST_ACCEPT_ACK / QUEST_COMPLETE_ACK
            // handlers in OnEvent). This replaces the old one-per-tick safety with
            // ACK-driven pacing: each accept/complete waits for C++ confirmation.
            if (newPhase == "WaitingForQuestSync" ||
                newPhase == "TravelingToGiver" || newPhase == "TravelingToObjective" ||
                newPhase == "TravelingToTurnIn" || newPhase == "DoingObjectives" ||
                newPhase == "NoQuestsAvailable")
                break;
        }

        var finalPhase = bot.CurrentActivity.SubPhase ?? "";
        if (finalPhase.StartsWith("Traveling"))
            CheckStuckDetection(bot, state);

        return commands;
    }

    // ════════════════════════════════════════════════════════════════════
    // OnEvent
    // ════════════════════════════════════════════════════════════════════

    public List<BridgeCommand> OnEvent(BotIdentity bot, BotStateSnapshot state, BotEvent evt)
    {
        var commands = new List<BridgeCommand>();

        switch (evt.EventType)
        {
            case "KILL":
                commands.AddRange(HandleKillEvent(bot, evt));
                break;
            case "TASK_COMPLETE":
                commands.AddRange(HandleTaskComplete(bot, state));
                break;
            case "QUEST_STATUS_ALL":
                // Session 29: C++ responded with authoritative quest log.
                // Rebuild active_quests and route to the right sub-phase.
                if (bot.CurrentActivity.SubPhase == "WaitingForQuestSync")
                {
                    commands.AddRange(RebuildFromQuestSync(bot, state, evt.Data ?? ""));
                }
                // Session 47: mid-grind resync (see HandleKillEvent). Refresh
                // server-truth progress IN PLACE — no rebuild / drop / re-route —
                // and if a quest just flipped server-complete, run the
                // DoingObjectives stop check NOW instead of waiting a tactical tick.
                else if (bot.CurrentActivity.SubPhase == "DoingObjectives")
                {
                    if (RefreshProgressFromQuestSync(bot, evt.Data ?? ""))
                        commands.AddRange(ProcessDoingObjectives(bot, state));
                }
                break;
            case "QUEST_UPDATE":
                if (evt.QuestStatus == "COMPLETE" && evt.QuestId.HasValue)
                {
                    var aq = GetActiveQuests(bot);
                    var match = aq.FirstOrDefault(q => q.QuestId == evt.QuestId.Value);
                    if (match != null)
                    {
                        match.ServerComplete = true;
                        _logger.LogInformation(
                            "[BOT-QUEST] {Name}({Guid}) | Server confirmed [{QuestId}] \"{Title}\" complete",
                            bot.Name, bot.Guid, match.QuestId, match.Title);
                    }
                }
                break;
            case "QUEST_ACCEPT_ACK":
                {
                    int.TryParse(evt.Data ?? "", out int ackId);
                    _logger.LogInformation("[BOT-QUEST] {Name}({Guid}) | ACK: quest {AckId} accepted",
                        bot.Name, bot.Guid, ackId);

                    var subPhase = bot.CurrentActivity.SubPhase;
                    if (subPhase == "AcceptingQuests")
                    {
                        commands.AddRange(ProcessAcceptingQuests(bot, state));
                    }
                    break;
                }
            case "QUEST_COMPLETE_ACK":
                {
                    int.TryParse(evt.Data ?? "", out int ackId);
                    _logger.LogInformation("[BOT-QUEST] {Name}({Guid}) | ACK: quest {AckId} rewarded",
                        bot.Name, bot.Guid, ackId);

                    var subPhase = bot.CurrentActivity.SubPhase;
                    if (subPhase == "TurningIn")
                    {
                        commands.AddRange(ProcessTurningIn(bot, state));
                    }
                    break;
                }
            case "LOOT":
                commands.AddRange(HandleLootEvent(bot, evt));
                break;
            case "QUEST_INTERACT_FAIL":
                commands.AddRange(HandleQuestInteractFail(bot, state, evt));
                break;
            case "DEATH":
                commands.AddRange(HandleDeathWhileQuesting(bot, state));
                break;
            case "PATH_UNSAFE":
                commands.AddRange(HandlePathUnsafe(bot, state, evt));
                break;
            case "MOVE_FAILED":
                commands.AddRange(HandleMoveFailed(bot, state, evt));
                break;
            case "USE_GO_FAIL":
                _logger.LogWarning("[BOT-QUEST] {Name}({Guid}) | USE_GO_FAIL: {Data}",
                    bot.Name, bot.Guid, evt.Data ?? "");
                SetPhaseDateTime(bot, "go_last_interact", default);
                SetPhaseInt(bot, "go_spawn_pin", 0);
                break;
        }

        return commands;
    }

    // ════════════════════════════════════════════════════════════════════
    // Death-Reactive Quest Shelving (batch-aware)
    // ════════════════════════════════════════════════════════════════════

    private List<BridgeCommand> HandleDeathWhileQuesting(BotIdentity bot, BotStateSnapshot state)
    {
        bot.RecordDeath(state.X, state.Y, state.MapId);
        var subPhase = bot.CurrentActivity.SubPhase ?? "";

        bool diedWhileTraveling = subPhase.StartsWith("Traveling");
        int deathThreshold = diedWhileTraveling ? 2 : 3;

        if (bot.DeathsSinceQuestStart >= deathThreshold)
        {
            var activeQuests = GetActiveQuests(bot);
            float deferMinutes = Lerp(15f, 30f, bot.Personality.Patience);

            // Session 32: Surgical deferral — find the quest most likely responsible
            // for the deaths rather than nuking the entire batch.
            // If traveling: the quest whose destination NPC is farthest from bot (the travel target).
            // If doing objectives: the quest whose grind center is nearest to death location.
            ActiveQuestEntry? culprit = null;

            if (diedWhileTraveling && activeQuests.Count > 1)
            {
                // The bot was walking toward a specific NPC — defer the quest whose NPC
                // the bot was traveling toward (farthest incomplete quest NPC from bot)
                float bestDist = 0;
                foreach (var aq in activeQuests.Where(q => !q.TurnedIn))
                {
                    float dist = float.MaxValue;
                    if (subPhase == "TravelingToTurnIn" && aq.Node.TurnIn != null)
                        dist = Distance2D(state.X, state.Y, aq.Node.TurnIn.X, aq.Node.TurnIn.Y);
                    else if (subPhase == "TravelingToGiver" && aq.Node.Giver != null)
                        dist = Distance2D(state.X, state.Y, aq.Node.Giver!.X, aq.Node.Giver!.Y);
                    else if (subPhase == "TravelingToObjective")
                    {
                        var obj = aq.Node.Objectives?.FirstOrDefault(o => o.IsCreature && o.GrindRadius > 0);
                        if (obj != null) dist = Distance2D(state.X, state.Y, obj.GrindX, obj.GrindY);
                    }
                    // The travel destination is the one they're heading toward — likely farthest
                    if (dist > bestDist) { bestDist = dist; culprit = aq; }
                }
            }
            else if (subPhase == "DoingObjectives" && activeQuests.Count > 1)
            {
                // Died while grinding — defer quest whose grind area is nearest to death
                float bestDist = float.MaxValue;
                foreach (var aq in activeQuests.Where(q => q.Accepted && !q.TurnedIn))
                {
                    var obj = aq.Node.Objectives?.FirstOrDefault(o => o.IsCreature && o.GrindRadius > 0);
                    if (obj != null)
                    {
                        float dist = Distance2D(state.X, state.Y, obj.GrindX, obj.GrindY);
                        if (dist < bestDist) { bestDist = dist; culprit = aq; }
                    }
                }
            }

            if (culprit != null && activeQuests.Count > 1)
            {
                // Session 33: Check if we should abandon instead of deferring again
                var abandonCmd = TryFrustrationAbandon(bot, culprit);
                if (abandonCmd != null)
                {
                    activeQuests.Remove(culprit);
                    SetActiveQuests(bot, activeQuests);
                    var cmds = new List<BridgeCommand> { abandonCmd };
                    cmds.AddRange(FallbackToPickingQuests(bot));
                    return cmds;
                }

                // Defer only the culprit, keep the rest
                bot.DeferQuest(culprit.QuestId, TimeSpan.FromMinutes(deferMinutes));
                _logger.LogWarning(
                    "[BOT-QUEST] {Name} SHELVED [{QuestId}] \"{Title}\" after {Deaths} deaths " +
                    "(sub-phase={SubPhase}, deferred {Minutes:F0}min, surgical — {Remaining} quests remain)",
                    bot.Name, culprit.QuestId, culprit.Title, bot.DeathsSinceQuestStart,
                    subPhase, deferMinutes, activeQuests.Count - 1);

                _diagnostics.RecordIssue(DiagnosticIssueType.QuestDeferred, bot,
                    state.X, state.Y, state.MapId,
                    questId: culprit.QuestId, questTitle: culprit.Title,
                    detail: $"death_shelved (surgical) after {bot.DeathsSinceQuestStart} deaths, deferred {deferMinutes:F0}min");

                activeQuests.Remove(culprit);
                SetActiveQuests(bot, activeQuests);

                // Try to continue with remaining quests
                if (activeQuests.Count > 0)
                {
                    var turnIn = GetNearestCompletedTurnIn(bot, state, activeQuests);
                    if (turnIn != null)
                    {
                        AdvanceTo(bot, "TravelingToTurnIn");
                        bot.ResetDeathCounter();
                        return new List<BridgeCommand>
                        {
                            MakeMoveTo(turnIn.Value.x, turnIn.Value.y, turnIn.Value.z, turnIn.Value.map,
                                fromX: state.X, fromY: state.Y)
                        };
                    }

                    var objective = GetNearestObjectiveAcrossQuests(bot, state, activeQuests);
                    if (objective != null)
                    {
                        AdvanceTo(bot, "TravelingToObjective");
                        bot.ResetDeathCounter();
                        return new List<BridgeCommand>
                        {
                            MakeMoveTo(objective.Value.x, objective.Value.y, objective.Value.z, objective.Value.map)
                        };
                    }
                }
            }
            else
            {
                // Single quest in batch or couldn't identify culprit — defer all (original behavior)
                foreach (var aq in activeQuests)
                {
                    bot.DeferQuest(aq.QuestId, TimeSpan.FromMinutes(deferMinutes));
                    _logger.LogWarning(
                        "[BOT-QUEST] {Name} SHELVED [{QuestId}] \"{Title}\" after {Deaths} deaths " +
                        "(sub-phase={SubPhase}, deferred {Minutes:F0}min)",
                        bot.Name, aq.QuestId, aq.Title, bot.DeathsSinceQuestStart,
                        subPhase, deferMinutes);

                    _diagnostics.RecordIssue(DiagnosticIssueType.QuestDeferred, bot,
                        state.X, state.Y, state.MapId,
                        questId: aq.QuestId, questTitle: aq.Title,
                        detail: $"death_shelved after {bot.DeathsSinceQuestStart} deaths, deferred {deferMinutes:F0}min");
                }
            }

            return FallbackToPickingQuests(bot);
        }

        return new List<BridgeCommand>();
    }

    // ════════════════════════════════════════════════════════════════════
    // PATH_UNSAFE — C++ rejected a MOVE_TO (dangerous path)
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// C++ IsPathSafe rejected a MOVE_TO because the mmap path crossed through
    /// high-level creature spawns. The destination is already blacklisted on
    /// BotIdentity by BotBrainService. Here we shelve the current quest batch
    /// and force a re-pick so the bot tries different quests.
    ///
    /// CRITICAL: Without this handler, the bot enters a stuck loop:
    ///   PickingQuests → selects quest → MOVE_TO → C++ rejects → next tick picks
    ///   same quest → MOVE_TO → rejected → forever.
    /// The blacklist + re-pick breaks the loop.
    /// </summary>
    private List<BridgeCommand> HandlePathUnsafe(BotIdentity bot, BotStateSnapshot state, BotEvent evt)
    {
        var activeQuests = GetActiveQuests(bot);
        var subPhase = bot.CurrentActivity.SubPhase ?? "";

        // Parse destination from event data to identify which quest caused this
        float destX = 0, destY = 0;
        if (!string.IsNullOrEmpty(evt.Data))
        {
            var parts = evt.Data.Split('|')
                .Select(s => s.Split('=', 2))
                .Where(p => p.Length == 2)
                .ToDictionary(p => p[0].Trim(), p => p[1].Trim(), StringComparer.OrdinalIgnoreCase);

            if (parts.TryGetValue("dest_x", out var dxs))
                float.TryParse(dxs, System.Globalization.CultureInfo.InvariantCulture, out destX);
            if (parts.TryGetValue("dest_y", out var dys))
                float.TryParse(dys, System.Globalization.CultureInfo.InvariantCulture, out destY);
        }

        bot.Story?.Emit(StoryVerb.MOVE_FAIL, StoryResult.BLOCK, reason: "path_unsafe", target: (destX != 0 || destY != 0) ? new StoryTarget(destX, destY, 0, state.MapId) : null, detail: $"subPhase={subPhase}");

        // If we're in a travel phase, identify the specific quest that caused the
        // PATH_UNSAFE and defer only that one — not the entire batch.
        // Session 32 fix: was deferring ALL quests in batch, draining the quest pool.
        if (activeQuests.Count > 0 && subPhase.StartsWith("Traveling"))
        {
            // Parse danger_level from the event to create level-gated deferrals
            int dangerLevel = 0;
            if (!string.IsNullOrEmpty(evt.Data))
            {
                var parts2 = evt.Data.Split('|')
                    .Select(s => s.Split('=', 2))
                    .Where(p => p.Length == 2)
                    .ToDictionary(p => p[0].Trim(), p => p[1].Trim(), StringComparer.OrdinalIgnoreCase);

                if (parts2.TryGetValue("danger_level", out var dls))
                    int.TryParse(dls, out dangerLevel);
            }

            // Find the specific quest whose NPC is closest to the failed destination
            ActiveQuestEntry? culprit = null;
            float bestMatch = 50f;

            if (destX != 0 || destY != 0)
            {
                foreach (var aq in activeQuests.Where(q => !q.TurnedIn))
                {
                    if (aq.Node.Giver != null)
                    {
                        float d = Distance2D(destX, destY, aq.Node.Giver.X, aq.Node.Giver.Y);
                        if (d < bestMatch) { bestMatch = d; culprit = aq; }
                    }
                    if (aq.Node.TurnIn != null)
                    {
                        float d = Distance2D(destX, destY, aq.Node.TurnIn.X, aq.Node.TurnIn.Y);
                        if (d < bestMatch) { bestMatch = d; culprit = aq; }
                    }
                    if (aq.Node.Objectives != null)
                    {
                        foreach (var obj in aq.Node.Objectives.Where(o => o.IsCreature && o.GrindRadius > 0))
                        {
                            float d = Distance2D(destX, destY, obj.GrindX, obj.GrindY);
                            if (d < bestMatch) { bestMatch = d; culprit = aq; }
                        }
                    }
                }
            }

            // Defer only the culprit (or first quest if no match found)
            var toDeferList = culprit != null
                ? new List<ActiveQuestEntry> { culprit }
                : new List<ActiveQuestEntry> { activeQuests.First(q => !q.TurnedIn) };

            foreach (var aq in toDeferList)
            {
                if (dangerLevel > 0)
                {
                    bot.DeferQuestUntilLevel(aq.QuestId, dangerLevel, SAFETY_MARGIN);

                    _diagnostics.RecordIssue(DiagnosticIssueType.QuestDeferred, bot,
                        state.X, state.Y, state.MapId,
                        questId: aq.QuestId, questTitle: aq.Title,
                        detail: $"path_unsafe shelved (surgical), level-gated until lvl {Math.Max(1, dangerLevel - SAFETY_MARGIN)} " +
                                $"(danger={dangerLevel}, bot={bot.Level})");
                }
                else
                {
                    float deferMinutes = Lerp(10f, 25f, bot.Personality.Patience);
                    bot.DeferQuest(aq.QuestId, TimeSpan.FromMinutes(deferMinutes));

                    _diagnostics.RecordIssue(DiagnosticIssueType.QuestDeferred, bot,
                        state.X, state.Y, state.MapId,
                        questId: aq.QuestId, questTitle: aq.Title,
                        detail: $"path_unsafe shelved (surgical), time-gated {deferMinutes:F0}min (no danger_level)");
                }
                bot.Story?.Emit(StoryVerb.DEFER, StoryResult.SKIP, quest: new StoryQuest(aq.QuestId, aq.Title), reason: "path_unsafe", detail: dangerLevel > 0 ? $"until lvl {Math.Max(1, dangerLevel - SAFETY_MARGIN)}" : "10-25min");
            }

            int reqLevel = dangerLevel > 0 ? Math.Max(1, dangerLevel - SAFETY_MARGIN) : 0;
            _logger.LogWarning(
                "[BOT-PATH] {Name}({Guid}) | PATH_UNSAFE during {SubPhase} — " +
                "shelved [{QuestId}] \"{Title}\" {Gate} (surgical, {Remaining} quests remain in batch)",
                bot.Name, bot.Guid, subPhase,
                toDeferList[0].QuestId, toDeferList[0].Title,
                dangerLevel > 0 ? $"until lvl {reqLevel} (danger={dangerLevel})" : "for 10-25min",
                activeQuests.Count(q => !q.TurnedIn) - toDeferList.Count);

            // Remove culprit and try to continue with remaining quests
            foreach (var deferred in toDeferList)
                activeQuests.Remove(deferred);
            SetActiveQuests(bot, activeQuests);

            if (activeQuests.Count > 0)
            {
                var turnIn = GetNearestCompletedTurnIn(bot, state, activeQuests);
                if (turnIn != null)
                {
                    AdvanceTo(bot, "TravelingToTurnIn");
                    return new List<BridgeCommand>
                    {
                        MakeMoveTo(turnIn.Value.x, turnIn.Value.y, turnIn.Value.z, turnIn.Value.map,
                            fromX: state.X, fromY: state.Y)
                    };
                }

                var objective = GetNearestObjectiveAcrossQuests(bot, state, activeQuests);
                if (objective != null)
                {
                    AdvanceTo(bot, "TravelingToObjective");
                    return new List<BridgeCommand>
                    {
                        MakeMoveTo(objective.Value.x, objective.Value.y, objective.Value.z, objective.Value.map)
                    };
                }
            }

            return FallbackToPickingQuests(bot);
        }

        // If we're not traveling (maybe DoingObjectives or picking), just log it.
        // The blacklist on BotIdentity will prevent re-selecting the same destination.
        _logger.LogWarning(
            "[BOT-PATH] {Name}({Guid}) | PATH_UNSAFE during {SubPhase} — " +
            "destination ({DestX:F0},{DestY:F0}) blacklisted, will avoid on next pick",
            bot.Name, bot.Guid, subPhase, destX, destY);

        return new List<BridgeCommand>();
    }

    // ════════════════════════════════════════════════════════════════════
    // MOVE_FAILED — C++ could not pathfind to destination (Session 26)
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// C++ rejected a MOVE_TO because mmap returned PATHFIND_NOPATH or INCOMPLETE.
    /// The destination is already blacklisted by BotBrainService. Here we shelve
    /// ONLY the specific quest whose destination failed — not the entire batch.
    /// The bot then re-routes to the next actionable quest in the batch.
    ///
    /// Session 32 fix: Previously shelved the ENTIRE batch on any MOVE_FAILED,
    /// which cascaded into NoQuestsAvailable within hours as every quest got
    /// deferred simultaneously. Now surgical — only the culprit quest is deferred.
    /// </summary>
    private List<BridgeCommand> HandleMoveFailed(BotIdentity bot, BotStateSnapshot state, BotEvent evt)
    {
        var activeQuests = GetActiveQuests(bot);
        var subPhase = bot.CurrentActivity.SubPhase ?? "";

        // ── Session 28: If bot is already close enough to the NPC, skip the failed
        // MOVE_TO and advance to the interact phase instead of shelving.
        // C++ GetCreatureListWithEntryInGrid uses 15yd, so anything within 15yd can interact.
        if (subPhase == "TravelingToTurnIn" && activeQuests.Count > 0)
        {
            var turnIn = GetNearestCompletedTurnIn(bot, state, activeQuests);
            if (turnIn != null && IsNear(state, turnIn.Value.x, turnIn.Value.y, turnIn.Value.map, 10f))
            {
                _logger.LogInformation(
                    "[BOT-PATH] {Name}({Guid}) | MOVE_FAILED during TravelingToTurnIn but within 15yd of turn-in NPC — advancing to TurningIn",
                    bot.Name, bot.Guid);
                AdvanceTo(bot, "TurningIn");
                return new List<BridgeCommand>();
            }
        }
        else if (subPhase == "TravelingToGiver" && activeQuests.Count > 0)
        {
            var unaccepted = activeQuests
                .Where(e => !e.Accepted && e.Node.Giver != null)
                .OrderBy(e => Distance2D(state.X, state.Y, e.Node.Giver!.X, e.Node.Giver!.Y))
                .FirstOrDefault();
            if (unaccepted != null && IsNear(state, unaccepted.Node.Giver!.X, unaccepted.Node.Giver!.Y, unaccepted.Node.Giver!.Map, 10f))
            {
                _logger.LogInformation(
                    "[BOT-PATH] {Name}({Guid}) | MOVE_FAILED during TravelingToGiver but within 15yd of giver NPC — advancing to AcceptingQuests",
                    bot.Name, bot.Guid);
                AdvanceTo(bot, "AcceptingQuests");
                return new List<BridgeCommand>();
            }
        }

        // ── Surgical deferral: identify which quest caused the failure and only defer THAT one ──
        if (activeQuests.Count > 0 && (subPhase.StartsWith("Traveling") || subPhase == "DoingObjectives"))
        {
            float deferMinutes = Lerp(10f, 25f, bot.Personality.Patience);

            // Parse the failed destination from event data
            float destX = 0, destY = 0;
            if (!string.IsNullOrEmpty(evt.Data))
            {
                var parts = evt.Data.Split('|')
                    .Select(s => s.Split('=', 2))
                    .Where(p => p.Length == 2)
                    .ToDictionary(p => p[0].Trim(), p => p[1].Trim(), StringComparer.OrdinalIgnoreCase);

                if (parts.TryGetValue("dest_x", out var dxs))
                    float.TryParse(dxs, System.Globalization.CultureInfo.InvariantCulture, out destX);
                if (parts.TryGetValue("dest_y", out var dys))
                    float.TryParse(dys, System.Globalization.CultureInfo.InvariantCulture, out destY);
            }

            // ── Session 36 (F3): structural turn-in unreachability ───────────
            // incomplete_path/no_path to a turn-in NPC is a navmesh dead-end, not a
            // level-gated danger. Deferring one quest just routes the next sibling to
            // the SAME unreachable NPC (we re-cycled -9461,16 dozens of times/run).
            // Defer EVERY active quest turning in at this location in one shot.
            string mfReason = "";
            if (!string.IsNullOrEmpty(evt.Data))
            {
                var rp = evt.Data.Split('|').Select(s => s.Split('=', 2))
                    .Where(pp => pp.Length == 2)
                    .ToDictionary(pp => pp[0].Trim(), pp => pp[1].Trim(), StringComparer.OrdinalIgnoreCase);
                if (rp.TryGetValue("reason", out var r)) mfReason = r;
            }

            bot.Story?.Emit(StoryVerb.MOVE_FAIL, StoryResult.FAIL, reason: string.IsNullOrEmpty(mfReason) ? "move_failed" : mfReason, target: (destX != 0 || destY != 0) ? new StoryTarget(destX, destY, 0, state.MapId) : null, detail: $"subPhase={subPhase}");

            if ((mfReason == "incomplete_path" || mfReason == "no_path") && (destX != 0 || destY != 0))
            {
                var turnInCluster = activeQuests
                    .Where(q => !q.TurnedIn && q.Node.TurnIn != null
                                && Distance2D(destX, destY, q.Node.TurnIn!.X, q.Node.TurnIn!.Y) <= 25f)
                    .ToList();

                // ── Session 38: last-leg failure → force_* server-side credit ──
                // Path failed but the bot is essentially AT the NPC. Arm force
                // mode and route into the normal interact loop instead of
                // deferring the cluster for the Nth time.
                float botToDest = Distance2D(state.X, state.Y, destX, destY);
                if (botToDest <= FORCE_INTERACT_TRIGGER_RADIUS)
                {
                    bool anyForceComplete = turnInCluster.Any(q =>
                        q.Accepted && (q.ServerComplete || AllObjectivesComplete(q)));
                    bool anyForceAccept = activeQuests.Any(q =>
                        !q.Accepted && q.Node.Giver != null
                        && Distance2D(destX, destY, q.Node.Giver!.X, q.Node.Giver!.Y) <= 25f);

                    if (anyForceComplete || anyForceAccept)
                    {
                        SetPhaseDateTime(bot, "force_interact_until",
                            DateTime.UtcNow.AddSeconds(FORCE_MODE_TTL_SEC));

                        _logger.LogWarning(
                            "[BOT-QUEST] {Name}({Guid}) | MOVE_FAILED {Reason} to ({DestX:F0},{DestY:F0}) " +
                            "with bot only {Dist:F0}yd away — last-leg/WMO case, engaging force interact " +
                            "(complete={FC}, accept={FA})",
                            bot.Name, bot.Guid, mfReason, destX, destY, botToDest,
                            anyForceComplete, anyForceAccept);

                        _diagnostics.RecordIssue(DiagnosticIssueType.QuestInteractFail, bot,
                            state.X, state.Y, state.MapId,
                            detail: $"force mode engaged at ({destX:F0},{destY:F0}) dist={botToDest:F0} reason={mfReason}");

                        if (anyForceComplete)
                        {
                            AdvanceTo(bot, "TurningIn");
                            return ProcessTurningIn(bot, state);
                        }

                        AdvanceTo(bot, "AcceptingQuests");
                        return ProcessAcceptingQuests(bot, state);
                    }
                }

                if (turnInCluster.Count > 0)
                {
                    foreach (var aq in turnInCluster)
                    {
                        bot.DeferQuest(aq.QuestId, TimeSpan.FromMinutes(F3_TURNIN_DEFER_MIN));
                        _diagnostics.RecordIssue(DiagnosticIssueType.QuestDeferred, bot,
                            state.X, state.Y, state.MapId,
                            questId: aq.QuestId, questTitle: aq.Title,
                            detail: $"turn-in unreachable ({mfReason}) at ({destX:F0},{destY:F0}) — deferred {F3_TURNIN_DEFER_MIN}min (cluster)");
                        bot.Story?.Emit(StoryVerb.DEFER, StoryResult.SKIP, quest: new StoryQuest(aq.QuestId, aq.Title), reason: $"turnin_unreachable_{mfReason}", detail: $"cluster {F3_TURNIN_DEFER_MIN}min");
                        activeQuests.Remove(aq);
                    }
                    SetActiveQuests(bot, activeQuests);

                    _logger.LogWarning(
                        "[BOT-PATH] {Name}({Guid}) | MOVE_FAILED {Reason} to turn-in ({DestX:F0},{DestY:F0}) — " +
                        "deferred {N} quest(s) sharing that unreachable NPC for {Min}min ({Remaining} remain)",
                        bot.Name, bot.Guid, mfReason, destX, destY,
                        turnInCluster.Count, F3_TURNIN_DEFER_MIN, activeQuests.Count(q => !q.TurnedIn));

                    if (activeQuests.Count > 0)
                    {
                        var clTurnIn = GetNearestCompletedTurnIn(bot, state, activeQuests);
                        if (clTurnIn != null)
                        {
                            AdvanceTo(bot, "TravelingToTurnIn");
                            return new List<BridgeCommand>
                            {
                                MakeMoveTo(clTurnIn.Value.x, clTurnIn.Value.y, clTurnIn.Value.z, clTurnIn.Value.map,
                                    fromX: state.X, fromY: state.Y)
                            };
                        }

                        var clObjective = GetNearestObjectiveAcrossQuests(bot, state, activeQuests);
                        if (clObjective != null)
                        {
                            AdvanceTo(bot, "TravelingToObjective");
                            return new List<BridgeCommand>
                            {
                                MakeMoveTo(clObjective.Value.x, clObjective.Value.y, clObjective.Value.z, clObjective.Value.map)
                            };
                        }
                    }

                    return FallbackToPickingQuests(bot);
                }
            }

            // Find the specific quest whose NPC is closest to the failed destination
            ActiveQuestEntry? culprit = null;
            float bestMatch = 50f; // within 50yd of failed dest = match

            if (destX != 0 || destY != 0)
            {
                foreach (var aq in activeQuests.Where(q => !q.TurnedIn))
                {
                    // Check giver
                    if (aq.Node.Giver != null)
                    {
                        float d = Distance2D(destX, destY, aq.Node.Giver.X, aq.Node.Giver.Y);
                        if (d < bestMatch) { bestMatch = d; culprit = aq; }
                    }
                    // Check turn-in
                    if (aq.Node.TurnIn != null)
                    {
                        float d = Distance2D(destX, destY, aq.Node.TurnIn.X, aq.Node.TurnIn.Y);
                        if (d < bestMatch) { bestMatch = d; culprit = aq; }
                    }
                    // Check kill objective grind center
                    if (aq.Node.Objectives != null)
                    {
                        foreach (var obj in aq.Node.Objectives.Where(o => o.IsCreature && o.GrindRadius > 0))
                        {
                            float d = Distance2D(destX, destY, obj.GrindX, obj.GrindY);
                            if (d < bestMatch) { bestMatch = d; culprit = aq; }
                        }
                    }
                    // Check item objective grind center
                    if (aq.Node.ItemObjectives != null)
                    {
                        foreach (var itemObj in aq.Node.ItemObjectives)
                        {
                            if (itemObj.BestDropSource?.SpawnCount > 0)
                            {
                                float d = Distance2D(destX, destY, itemObj.BestDropSource.GrindX, itemObj.BestDropSource.GrindY);
                                if (d < bestMatch) { bestMatch = d; culprit = aq; }
                            }
                            if (itemObj.BestGoSource?.SpawnCount > 0)
                            {
                                float d = Distance2D(destX, destY, itemObj.BestGoSource.X, itemObj.BestGoSource.Y);
                                if (d < bestMatch) { bestMatch = d; culprit = aq; }
                            }
                        }
                    }
                }
            }

            if (culprit != null)
            {
                // Session 33: Check if we should abandon instead of deferring again
                var abandonCmd = TryFrustrationAbandon(bot, culprit);
                if (abandonCmd != null)
                {
                    activeQuests.Remove(culprit);
                    SetActiveQuests(bot, activeQuests);
                    var cmds = new List<BridgeCommand> { abandonCmd };
                    cmds.AddRange(FallbackToPickingQuests(bot));
                    return cmds;
                }

                // Defer ONLY the culprit quest
                bot.DeferQuest(culprit.QuestId, TimeSpan.FromMinutes(deferMinutes));
                bot.Story?.Emit(StoryVerb.DEFER, StoryResult.SKIP, quest: new StoryQuest(culprit.QuestId, culprit.Title), reason: "move_failed", detail: $"surgical {deferMinutes:F0}min");

                _logger.LogWarning(
                    "[BOT-PATH] {Name}({Guid}) | MOVE_FAILED during {SubPhase} — " +
                    "shelved [{QuestId}] \"{Title}\" for {Minutes:F0}min (surgical, {Remaining} quests remain in batch)",
                    bot.Name, bot.Guid, subPhase,
                    culprit.QuestId, culprit.Title, deferMinutes,
                    activeQuests.Count(q => !q.TurnedIn) - 1);

                _diagnostics.RecordIssue(DiagnosticIssueType.QuestDeferred, bot,
                    state.X, state.Y, state.MapId,
                    questId: culprit.QuestId, questTitle: culprit.Title,
                    detail: $"move_failed shelved (surgical), deferred {deferMinutes:F0}min");

                // Remove the culprit from the active list and try to continue with remaining quests
                activeQuests.Remove(culprit);
                SetActiveQuests(bot, activeQuests);

                if (activeQuests.Count > 0)
                {
                    // Try to find the next actionable quest in the remaining batch
                    var turnIn = GetNearestCompletedTurnIn(bot, state, activeQuests);
                    if (turnIn != null)
                    {
                        AdvanceTo(bot, "TravelingToTurnIn");
                        return new List<BridgeCommand>
                        {
                            MakeMoveTo(turnIn.Value.x, turnIn.Value.y, turnIn.Value.z, turnIn.Value.map,
                                fromX: state.X, fromY: state.Y)
                        };
                    }

                    var objective = GetNearestObjectiveAcrossQuests(bot, state, activeQuests);
                    if (objective != null)
                    {
                        AdvanceTo(bot, "TravelingToObjective");
                        return new List<BridgeCommand>
                        {
                            MakeMoveTo(objective.Value.x, objective.Value.y, objective.Value.z, objective.Value.map)
                        };
                    }

                    // No remaining actionable quests — fall through to re-pick
                }
            }
            else
            {
                // Couldn't identify culprit — defer only the first un-turned-in quest
                // (still better than nuking the whole batch)
                var first = activeQuests.FirstOrDefault(q => !q.TurnedIn);
                if (first != null)
                {
                    bot.DeferQuest(first.QuestId, TimeSpan.FromMinutes(deferMinutes));
                    bot.Story?.Emit(StoryVerb.DEFER, StoryResult.SKIP, quest: new StoryQuest(first.QuestId, first.Title), reason: "move_failed_no_match", detail: $"{deferMinutes:F0}min");

                    _logger.LogWarning(
                        "[BOT-PATH] {Name}({Guid}) | MOVE_FAILED during {SubPhase} — " +
                        "shelved [{QuestId}] \"{Title}\" for {Minutes:F0}min (no dest match, deferred first quest)",
                        bot.Name, bot.Guid, subPhase,
                        first.QuestId, first.Title, deferMinutes);

                    _diagnostics.RecordIssue(DiagnosticIssueType.QuestDeferred, bot,
                        state.X, state.Y, state.MapId,
                        questId: first.QuestId, questTitle: first.Title,
                        detail: $"move_failed shelved (no match), deferred {deferMinutes:F0}min");
                }
            }

            return FallbackToPickingQuests(bot);
        }

        // If not traveling, the blacklist on BotIdentity will prevent re-selecting
        _logger.LogWarning(
            "[BOT-PATH] {Name}({Guid}) | MOVE_FAILED during {SubPhase} — " +
            "destination blacklisted, will avoid on next pick",
            bot.Name, bot.Guid, subPhase);

        return new List<BridgeCommand>();
    }

    private List<BridgeCommand> ProcessPickingQuests(BotIdentity bot, BotStateSnapshot state)
    {
        var commands = new List<BridgeCommand>();

        // ── Session 35: Group non-pace-setter — adopt pace-setter's quest batch ──
        // Instead of waiting for leader_quests flag injection from BotBrainService,
        // grouped members directly read the pace-setter's active quests and build
        // their own batch from the same quest IDs. Every member accepts, grinds,
        // and turns in independently — they're autonomous questers sharing quest picks.
        if (bot.IsGroupFollower && bot.GroupLeaderGuid.HasValue)
        {
            // First: turn in any completed quests the member already has
            var followerPendingTurnIns = GetActiveQuests(bot);
            if (followerPendingTurnIns.Count > 0)
            {
                var turnIn = GetNearestCompletedTurnIn(bot, state, followerPendingTurnIns);
                if (turnIn != null)
                {
                    _logger.LogInformation(
                        "[BOT-GROUP] {Name}({Guid}) | Member has completed quest [{QuestId}] to turn in before adopting pace-setter batch",
                        bot.Name, bot.Guid, turnIn.Value.questId);
                    AdvanceTo(bot, "TravelingToTurnIn");
                    bot.CurrentActivity.ContextTag = $"quests:batch:{followerPendingTurnIns.Count}";
                    bot.CurrentActivity.IsInterruptible = true;
                    commands.Add(MakeMoveTo(turnIn.Value.x, turnIn.Value.y, turnIn.Value.z, turnIn.Value.map,
                        fromX: state.X, fromY: state.Y));
                    return commands;
                }

                // Re-drive existing incomplete objectives before looking at pace-setter
                var incompleteOwn = followerPendingTurnIns
                    .Where(q => !q.TurnedIn && !q.ServerComplete && !AllObjectivesComplete(q) && q.Node != null)
                    .ToList();
                if (incompleteOwn.Count > 0)
                {
                    _logger.LogInformation(
                        "[BOT-GROUP] {Name}({Guid}) | Member has {Count} incomplete quests — re-driving objectives",
                        bot.Name, bot.Guid, incompleteOwn.Count);
                    AdvanceTo(bot, "DoingObjectives");
                    bot.CurrentActivity.ContextTag = $"quests:batch:{followerPendingTurnIns.Count}";
                    bot.CurrentActivity.IsInterruptible = true;
                    return commands;
                }
            }

            // Wait for group turn-in gate — don't pick new quests until everyone turned in
            if (!bot.GroupAllMembersTurnedIn)
            {
                _logger.LogDebug(
                    "[BOT-GROUP] {Name}({Guid}) | Waiting for all group members to turn in before picking new batch",
                    bot.Name, bot.Guid);
                BotTrace.Wait(bot, WaitOn.GroupTurnedIn, "follower: picking blocked", state);
                return commands;
            }

            // Adopt pace-setter's quest batch (if they have one)
            // This is a read-only check — we build our own ActiveQuestEntry list
            // from the same quest IDs, filtered by what WE haven't completed yet.
            // Each member runs their own full accept→grind→turnin lifecycle.
            // Fall through to normal quest picking below — the pace-setter's quest graph
            // selection will naturally pick the same quests because all members share
            // the same CompletedQuestIds progression and are at similar levels.
        }

        // ── Session 35: Group sync gate — ALL members wait for turn-ins before new batch ──
        if (bot.IsGrouped && !bot.GroupAllMembersTurnedIn)
        {
            _logger.LogDebug(
                "[BOT-GROUP] {Name}({Guid}) | Waiting for all group members to turn in before picking new batch",
                bot.Name, bot.Guid);
            BotTrace.Wait(bot, WaitOn.GroupTurnedIn, "grouped: picking blocked", state);
            return commands;
        }

        // ── Session 35: Group pace-setter — wait for members to return from errands ──
        if (bot.IsGroupLeader && !bot.GroupAllMembersQuesting)
        {
            _logger.LogDebug(
                "[BOT-GROUP] {Name}({Guid}) | Pace-setter waiting for group members to finish vendoring/training/eating",
                bot.Name, bot.Guid);
            BotTrace.Wait(bot, WaitOn.GroupQuesting, "leader: peers on errands", state);
            return commands;
        }

        bot.PruneExpiredDeferrals();
        bot.PrunePathBlacklist();
        bot.PathUnsafeCountSinceLastPick = 0;

        // ── Session 33: Check completed-not-turned-in quests BEFORE picking new ones ──
        // Quests that were completed but never turned in (e.g. MOVE_FAILED during
        // travel to turn-in NPC, then deferral expired) still sit in the quest log.
        // These MUST be turned in first — they block prereq chains and waste log slots.
        var existingForTurnIn = GetActiveQuests(bot);
        if (existingForTurnIn.Count > 0)
        {
            var turnIn = GetNearestCompletedTurnIn(bot, state, existingForTurnIn);
            if (turnIn != null)
            {
                _logger.LogInformation(
                    "[BOT-QUEST] {Name}({Guid}) | PickingQuests found completed-not-turned-in quest [{QuestId}] — routing to turn-in first",
                    bot.Name, bot.Guid, turnIn.Value.questId);
                AdvanceTo(bot, "TravelingToTurnIn");
                bot.CurrentActivity.ContextTag = $"quests:batch:{existingForTurnIn.Count}";
                bot.CurrentActivity.IsInterruptible = true;
                commands.Add(MakeMoveTo(turnIn.Value.x, turnIn.Value.y, turnIn.Value.z, turnIn.Value.map,
                    fromX: state.X, fromY: state.Y));
                return commands;
            }
        }

        int raceBit = QuestGraphLoader.RaceToBitmask(bot.Race);
        int classBit = QuestGraphLoader.ClassToBitmask(bot.ClassId);

        int botMap = state.MapId;
        int reachTier = QuestReachTier(bot.Guid);
        float maxDist = ZoneSafetyMap.GetMaxTravelDistance(bot.Level, state.ZoneId, reachTier);
        if (reachTier > 0 && _noQuestStreakStartUtc.TryGetValue(bot.Guid, out var _stuckSince))
            _logger.LogInformation(
                "[BOT-REACH] {Name}({Guid}) | no pickable quest for {Sec:F0}s -> travel reach tier {Tier} ({Max:F0}yd)",
                bot.Name, bot.Guid, (DateTime.UtcNow - _stuckSince).TotalSeconds, reachTier, maxDist);
        var existingActive = GetActiveQuests(bot);
        var existingQuestIds = new HashSet<int>(existingActive.Select(q => q.QuestId));

        // ── Session 34: Re-drive existing INCOMPLETE quests before looking for new ones ──
        // If the bot has active quests with remaining objectives (e.g., Gold Dust 5/10,
        // Kobold Candles 4/8), route back to DoingObjectives instead of declaring
        // NoQuestsAvailable. This was the single biggest cause of fleet stalls — bots
        // had perfectly good incomplete quests but ProcessPickingQuests only looked for
        // NEW quests and ignored the ones already in the log.
        {
            var incompleteWithObjectives = existingActive
                .Where(q => !q.TurnedIn && !q.ServerComplete && !AllObjectivesComplete(q) && q.Node != null)
                .ToList();

            if (incompleteWithObjectives.Count > 0)
            {
                _logger.LogInformation(
                    "[BOT-QUEST] {Name}({Guid}) | PickingQuests found {Count} existing incomplete quests — re-driving objectives: [{Quests}]",
                    bot.Name, bot.Guid, incompleteWithObjectives.Count,
                    string.Join(", ", incompleteWithObjectives.Select(q => $"[{q.QuestId}]\"{q.Title}\"")));

                AdvanceTo(bot, "DoingObjectives");
                bot.CurrentActivity.ContextTag = $"quests:batch:{existingActive.Count}";
                bot.CurrentActivity.IsInterruptible = true;
                return commands;
            }
        }

        // Build full active quest set: C# active batch + DB-side active quests
        // This prevents re-offering quests the bot already has in its log
        var activeForFilter = new HashSet<int>(existingQuestIds);
        if (bot.ActiveQuestId.HasValue)
            activeForFilter.Add(bot.ActiveQuestId.Value);

        // Session 35: For grouped bots (any member, not just pace-setter), use the
        // minimum group level for quest selection. This ensures every member can accept
        // the same quests. The pace-setter picks first (processed first in tick order),
        // then other members naturally pick the same quests from the same pool.
        int questPickLevel = bot.Level;
        if (bot.IsGrouped && bot.GroupMinMemberLevel.HasValue)
        {
            questPickLevel = bot.GroupMinMemberLevel.Value;
            _logger.LogDebug(
                "[BOT-GROUP] {Name}({Guid}) | Using group min level {MinLevel} for quest selection (own level {OwnLevel})",
                bot.Name, bot.Guid, questPickLevel, bot.Level);
        }

        var available = _questGraph.GetAvailableQuests(raceBit, classBit, questPickLevel,
            bot.CompletedQuestIds, activeForFilter);

        // ── Session 42: follower quest adoption (ARCH §7a Questing directive) ──
        // Independent per-bot picking is what tore groups across zones (June 12:
        // grp1 median spread 3,668yd, 0% heal range). Followers restrict their
        // pick to the pace-setter's current active quest IDs. Pre-check: if NONE
        // of the leader's quests are even available to this bot (race-locked
        // starter quests, prereqs — e.g. grp1's Gnome), drop the restriction
        // instead of idling; the Regroup directive still caps the drift.
        HashSet<int>? leaderIds = null;
        if (bot.IsGroupFollower
            && bot.GroupLeaderQuestIds is { Count: > 0 }
            && (DateTime.UtcNow - bot.GroupDirectiveUtc).TotalSeconds < 120)
        {
            leaderIds = bot.GroupLeaderQuestIds;
            if (!available.Any(q => leaderIds.Contains(q.QuestId)))
            {
                _logger.LogInformation(
                    "[BOT-GROUP] {Name}({Guid}) | adoption: none of the leader's {N} active quests " +
                    "are available to this bot — falling back to own pick",
                    bot.Name, bot.Guid, leaderIds.Count);
                leaderIds = null;
            }
        }

        // ── HARD FILTERS ──
        var safe = new List<QuestNode>();
        int filteredUnsafe = 0, filteredTooFar = 0, filteredWrongMap = 0, filteredNotLeader = 0;

        foreach (var quest in available)
        {
            if (quest.Giver == null) continue;
            if (quest.Giver.Map != botMap) { filteredWrongMap++; continue; }
            if (bot.DeferredQuestIds.ContainsKey(quest.QuestId)) continue;
            if (existingQuestIds.Contains(quest.QuestId)) continue;

            // Session 42: adoption — followers only take the pace-setter's quests.
            if (leaderIds != null && !leaderIds.Contains(quest.QuestId)) { filteredNotLeader++; continue; }

            float giverDist = Distance2D(state.X, state.Y, quest.Giver.X, quest.Giver.Y);
            if (giverDist > maxDist) { filteredTooFar++; continue; }

            // P0b backstop: also check grind center distance (kill or item objective).
            // Even with P0a giver-proximity scoping, cross-map fallback or edge cases
            // could still produce a grind center far from the bot. Reject if any
            // objective's grind center exceeds maxDist from the bot.
            {
                bool grindTooFar = false;
                var killObj = quest.Objectives?.FirstOrDefault(o => o.IsCreature && o.GrindRadius > 0);
                if (killObj != null)
                {
                    float grindDist = Distance2D(state.X, state.Y, killObj.GrindX, killObj.GrindY);
                    if (grindDist > maxDist) grindTooFar = true;
                }
                if (!grindTooFar)
                {
                    var itemObj = quest.ItemObjectives?.FirstOrDefault(i => i.BestDropSource?.SpawnCount > 0);
                    if (itemObj?.BestDropSource != null)
                    {
                        float grindDist = Distance2D(state.X, state.Y, itemObj.BestDropSource.GrindX, itemObj.BestDropSource.GrindY);
                        if (grindDist > maxDist) grindTooFar = true;
                    }
                    else
                    {
                        var goItem = quest.ItemObjectives?.FirstOrDefault(i => i.BestGoSource?.SpawnCount > 0);
                        if (goItem?.BestGoSource != null)
                        {
                            float goDist = Distance2D(state.X, state.Y, goItem.BestGoSource.X, goItem.BestGoSource.Y);
                            if (goDist > maxDist) grindTooFar = true;
                        }
                    }
                }
                if (grindTooFar)
                {
                    filteredTooFar++;
                    _logger.LogDebug(
                        "[BOT-SAFETY] {Name}({Guid}) | REJECTED [{QuestId}] \"{Title}\" — " +
                        "grind center exceeds max travel distance ({MaxDist:F0}yd) for level {Level}",
                        bot.Name, bot.Guid, quest.QuestId, quest.Title, maxDist, bot.Level);
                    continue;
                }
            }

            // Zone safety check — REMOVED Session 32.
            // The straight-line IsQuestPathSafe sampling was too crude: it draws a line
            // from bot→giver→objective→turnin and checks creature levels along that line.
            // This false-positives on quests reachable via safe walking paths that go
            // AROUND dangerous areas. Meanwhile, it doesn't help with sub-leg safety
            // (e.g., walking between two NPCs within an accepted batch).
            //
            // The real safety is C++ IsPathSafe, which validates the actual mmap path
            // on every MOVE_TO command and emits PATH_UNSAFE if the path crosses
            // high-level creatures. Combined with the surgical deferral (Session 32),
            // a PATH_UNSAFE on one quest only shelves that quest, not the whole batch.
            //
            // The path blacklist check below (based on actual C++ rejections) remains
            // as the empirically-correct selection-time filter.

            // Path blacklist check — reject quests whose giver, objective, or
            // turn-in coordinates were previously rejected by C++ IsPathSafe.
            // This prevents the bot from re-selecting the same quest that just
            // got PATH_UNSAFE'd, breaking the pick→reject→pick loop.
            if (bot.PathBlacklist.Count > 0)
            {
                bool giverBlocked = bot.IsPathBlacklisted(quest.Giver.X, quest.Giver.Y);

                bool objBlocked = false;
                var chkObj = quest.Objectives?.FirstOrDefault(o => o.IsCreature && o.GrindRadius > 0);
                if (chkObj != null)
                    objBlocked = bot.IsPathBlacklisted(chkObj.GrindX, chkObj.GrindY);
                if (!objBlocked)
                {
                    var chkItem = quest.ItemObjectives?.FirstOrDefault(i => i.BestDropSource?.SpawnCount > 0);
                    if (chkItem?.BestDropSource != null)
                        objBlocked = bot.IsPathBlacklisted(chkItem.BestDropSource.GrindX, chkItem.BestDropSource.GrindY);
                    else
                    {
                        var chkGo = quest.ItemObjectives?.FirstOrDefault(i => i.BestGoSource?.SpawnCount > 0);
                        if (chkGo?.BestGoSource != null)
                            objBlocked = bot.IsPathBlacklisted(chkGo.BestGoSource.X, chkGo.BestGoSource.Y);
                    }
                }

                bool turnInBlocked = quest.TurnIn != null
                    && bot.IsPathBlacklisted(quest.TurnIn.X, quest.TurnIn.Y);

                if (giverBlocked || objBlocked || turnInBlocked)
                {
                    filteredUnsafe++;
                    string blockedLeg = giverBlocked ? "giver" : objBlocked ? "objective" : "turnin";
                    _logger.LogDebug(
                        "[BOT-SAFETY] {Name}({Guid}) | BLACKLISTED [{QuestId}] \"{Title}\" — " +
                        "{Leg} coords previously rejected by C++ IsPathSafe",
                        bot.Name, bot.Guid, quest.QuestId, quest.Title, blockedLeg);
                    continue;
                }
            }

            safe.Add(quest);
        }

        if (safe.Count > 0)
            _noQuestStreakStartUtc.Remove(bot.Guid);   // found something pickable — release reach escalation

        if (safe.Count == 0)
        {
            if (!_noQuestStreakStartUtc.ContainsKey(bot.Guid))
                _noQuestStreakStartUtc[bot.Guid] = DateTime.UtcNow;   // start no-quest streak (drives reach escalation)

            bot.CurrentActivity.ContextTag = "quests:none_available";
            bot.CurrentActivity.SubPhase = "NoQuestsAvailable";

            // FLIGHT RECORDER: the leader's Session 34 dead-end. Capture WHY nothing was safe.
            BotTrace.Transition(bot, "PickingQuests", "NoQuestsAvailable", "no_safe_quests",
                detail: $"wrongMap={filteredWrongMap} tooFar={filteredTooFar} unsafe={filteredUnsafe} notLeader={filteredNotLeader}",
                state: state);

            _logger.LogWarning(
                "[BOT-WARN] {Name}({Guid}) | PickingQuests: 0 safe quests! " +
                "Level={Level}, Completed={Completed} | " +
                "(wrongMap={WrongMap}, tooFar={TooFar}, unsafe={Unsafe}, notLeader={NotLeader})",
                bot.Name, bot.Guid, bot.Level, bot.CompletedQuestIds.Count,
                filteredWrongMap, filteredTooFar, filteredUnsafe, filteredNotLeader);

            _diagnostics.RecordIssue(DiagnosticIssueType.NoQuestsAvailable, bot,
                state.X, state.Y, state.MapId,
                detail: $"wrongMap={filteredWrongMap}, tooFar={filteredTooFar}, unsafe={filteredUnsafe}, " +
                        $"notLeader={filteredNotLeader}, completed={bot.CompletedQuestIds.Count}, blacklist={bot.PathBlacklist.Count}");

            bot.Story?.Emit(StoryVerb.NOQUESTS, StoryResult.BLOCK, reason: "no_safe_quests", detail: $"wrongMap={filteredWrongMap} tooFar={filteredTooFar} unsafe={filteredUnsafe} notLeader={filteredNotLeader} completed={bot.CompletedQuestIds.Count} blacklist={bot.PathBlacklist.Count}");
            return commands;
        }

        // ── SCORE & BATCH SELECT ──
        var scored = ScoreQuests(bot, safe, state);
        var anchor = scored.OrderByDescending(s => s.score).First().quest;
        float anchorX = anchor.Giver!.X;
        float anchorY = anchor.Giver!.Y;

        int batchSize = (int)Math.Ceiling(Lerp(1f, MAX_BATCH_SIZE, bot.Personality.Efficiency));
        batchSize = Math.Clamp(batchSize, 1, MAX_BATCH_SIZE);

        // Pick batch: anchor quest + nearby quests sorted by score
        var batch = new List<QuestNode> { anchor };

        var nearby = scored
            .Where(s => s.quest.QuestId != anchor.QuestId
                        && s.quest.Giver != null
                        && Distance2D(s.quest.Giver.X, s.quest.Giver.Y, anchorX, anchorY) <= BATCH_GIVER_RADIUS)
            .OrderByDescending(s => s.score)
            .Take(batchSize - 1)
            .Select(s => s.quest)
            .ToList();

        batch.AddRange(nearby);

        // Build ActiveQuestEntry list
        var newEntries = batch.Select(q => new ActiveQuestEntry
        {
            QuestId = q.QuestId,
            Title = q.Title,
            Node = q,
            Accepted = false,
            ServerComplete = false,
            TurnedIn = false,
            Progress = 0f
        }).ToList();

        var merged = new List<ActiveQuestEntry>(existingActive);
        merged.AddRange(newEntries);
        SetActiveQuests(bot, merged);

        var batchDesc = string.Join(", ", newEntries.Select(e => $"[{e.QuestId}]\"{e.Title}\""));
        _logger.LogInformation(
            "[BOT-BATCH] {Name}({Guid}) | Picked {Count} quests: {Batch} " +
            "(from {Pool} safe, {Unsafe} unsafe-filtered) | " +
            "Bot at ({BotX:F0},{BotY:F0}) → Anchor giver at ({AX:F0},{AY:F0})",
            bot.Name, bot.Guid, newEntries.Count, batchDesc,
            safe.Count, filteredUnsafe,
            state.X, state.Y, anchorX, anchorY);

        bot.Story?.Emit(StoryVerb.PICK, StoryResult.OK, quest: new StoryQuest(anchor.QuestId, anchor.Title), detail: $"batch={newEntries.Count} from {safe.Count} safe");

        // Navigate to the nearest unaccepted quest giver
        var nearestGiver = newEntries
            .Where(e => !e.Accepted && e.Node.Giver != null)
            .OrderBy(e => Distance2D(state.X, state.Y, e.Node.Giver!.X, e.Node.Giver!.Y))
            .FirstOrDefault();

        if (nearestGiver != null)
        {
            AdvanceTo(bot, "TravelingToGiver");
            bot.CurrentActivity.ContextTag = $"quests:batch:{merged.Count}";
            commands.Add(MakeMoveTo(nearestGiver.Node.Giver!.X, nearestGiver.Node.Giver!.Y,
                nearestGiver.Node.Giver!.Z, nearestGiver.Node.Giver!.Map,
                fromX: state.X, fromY: state.Y)
                .WithCorr(bot.Story?.Intent(StoryVerb.TRAVEL,
                    new StoryTarget(nearestGiver.Node.Giver!.X, nearestGiver.Node.Giver!.Y, nearestGiver.Node.Giver!.Z, nearestGiver.Node.Giver!.Map),
                    new StoryQuest(nearestGiver.QuestId, nearestGiver.Title))));
            ResetStuckDetection(bot, state);
        }

        return commands;
    }

    // ════════════════════════════════════════════════════════════════════
    // Sub-Phase: TravelingToGiver
    // ════════════════════════════════════════════════════════════════════

    private List<BridgeCommand> ProcessTravelingToGiver(BotIdentity bot, BotStateSnapshot state)
    {
        // Session 35: No follower override — all group members path independently.
        // They converge naturally because they're all going to the same quest givers.

        var activeQuests = GetActiveQuests(bot);
        var unaccepted = activeQuests
            .Where(e => !e.Accepted && e.Node.Giver != null)
            .OrderBy(e => Distance2D(state.X, state.Y, e.Node.Giver!.X, e.Node.Giver!.Y))
            .ToList();

        if (unaccepted.Count == 0)
        {
            AdvanceTo(bot, "TravelingToObjective");
            return SendToNearestObjective(bot, state);
        }

        var target = unaccepted[0];

        // Check for opportunistic objective completion while traveling
        var opportunistic = CheckOpportunisticObjective(bot, state, activeQuests);
        if (opportunistic.Count > 0) return opportunistic;

        // Arrive within 8yd before transitioning — C++ needs bot within 15yd of NPC
        if (IsNear(state, target.Node.Giver!.X, target.Node.Giver!.Y, target.Node.Giver!.Map, 8f))
        {
            AdvanceTo(bot, "AcceptingQuests");
            return new List<BridgeCommand>();
        }

        return CheckTravelNudge(bot, state,
            target.Node.Giver!.X, target.Node.Giver!.Y,
            target.Node.Giver!.Z, target.Node.Giver!.Map);
    }

    // ════════════════════════════════════════════════════════════════════
    // Sub-Phase: AcceptingQuests (batch accept at current NPC)
    // ════════════════════════════════════════════════════════════════════

    // ════════════════════════════════════════════════════════════════════
    // Session 44: formation quest-sync leader hold
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Leader-side hold at the NPC: returns true while the leader should stay
    /// put because glued followers are still catching up on batch accepts
    /// (needAccept=true, AcceptingQuests exit) or turn-ins (needAccept=false,
    /// TurningIn exit). The flags are computed by the BotBrainService
    /// coordinator from the group quest batch + member ack state; while the
    /// hold is active the coordinator also re-fans force_accept/force_complete
    /// to the lagging members every ~10s, and FollowDomain's own opportunistic
    /// pass keeps trying. Timeout-capped: a class-locked or wedged follower
    /// can never freeze the leader (the June-6 deadlock lesson). After a
    /// timed-out hold, no re-hold for GROUP_HOLD_GIVEUP_COOLDOWN_SEC.
    /// </summary>
    private bool HoldForFollowers(BotIdentity bot, BotStateSnapshot state, bool needAccept)
    {
        bool need = needAccept ? bot.GroupFollowersNeedAccept : bot.GroupFollowersNeedTurnIn;
        string key = needAccept ? "group_hold_accept_since" : "group_hold_turnin_since";
        string giveupKey = needAccept ? "group_hold_accept_giveup" : "group_hold_turnin_giveup";

        if (!bot.IsGroupLeader || !need)
        {
            // Caught up (or not a leader) — clear hold state so future holds start fresh.
            SetPhaseDateTime(bot, key, default);
            SetPhaseDateTime(bot, giveupKey, default);
            return false;
        }

        // Recently gave up on this hold class — don't immediately re-hold.
        var gaveUp = GetPhaseDateTime(bot, giveupKey);
        if (gaveUp != default && (DateTime.UtcNow - gaveUp).TotalSeconds < GROUP_HOLD_GIVEUP_COOLDOWN_SEC)
            return false;

        var since = GetPhaseDateTime(bot, key);
        if (since == default)
        {
            since = DateTime.UtcNow;
            SetPhaseDateTime(bot, key, since);
            _logger.LogInformation(
                "[BOT-HOLD] {Name}({Guid}) | holding at NPC for follower {What} (max {Max:F0}s)",
                bot.Name, bot.Guid, needAccept ? "ACCEPTS" : "TURN-INS", GROUP_HOLD_TIMEOUT_SEC);
        }

        if ((DateTime.UtcNow - since).TotalSeconds > GROUP_HOLD_TIMEOUT_SEC)
        {
            _logger.LogWarning(
                "[BOT-HOLD] {Name}({Guid}) | follower {What} hold timed out after {Max:F0}s — proceeding without them",
                bot.Name, bot.Guid, needAccept ? "ACCEPT" : "TURN-IN", GROUP_HOLD_TIMEOUT_SEC);
            SetPhaseDateTime(bot, key, default);
            SetPhaseDateTime(bot, giveupKey, DateTime.UtcNow);
            return false;
        }

        BotTrace.Wait(bot,
            needAccept ? WaitOn.GroupQuesting : WaitOn.GroupTurnedIn,
            needAccept ? "leader: holding for follower accepts" : "leader: holding for follower turn-ins",
            state);
        return true;
    }

    /// <summary>
    /// AcceptingQuests: accept ONE quest per tick to avoid rapid-fire QUEST_INTERACT
    /// commands crashing C++. The sub-phase loop in OnTick won't chain through this
    /// (it's not listed as an instant phase), so each accept gets its own tick with
    /// ~10-30s spacing. C++ processes each AddQuest fully before the next arrives.
    /// </summary>
    private List<BridgeCommand> ProcessAcceptingQuests(BotIdentity bot, BotStateSnapshot state)
    {
        var commands = new List<BridgeCommand>();
        var activeQuests = GetActiveQuests(bot);

        // ── Session 38: force mode (see ProcessTurningIn) — force_accept for
        // WMO-interior givers. All C++ gates (CanTakeQuest/CanAddQuest) intact.
        bool forceMode = GetPhaseDateTime(bot, "force_interact_until") > DateTime.UtcNow;
        float interactRadius = forceMode ? FORCE_INTERACT_NPC_RADIUS : 10f;

        // Find the FIRST unaccepted quest whose giver is within interact range.
        // C++ requires 15yd (GetCreatureListWithEntryInGrid), so we use 10yd to be safe.
        var toAccept = activeQuests
            .Where(q => !q.Accepted && q.Node.Giver != null
                        && IsNear(state, q.Node.Giver!.X, q.Node.Giver!.Y, q.Node.Giver!.Map, interactRadius))
            .FirstOrDefault();

        if (toAccept != null)
        {
            var objDesc = toAccept.Node.HasKillObjectives
                ? string.Join(", ", toAccept.Node.Objectives.Where(o => o.IsCreature)
                    .Select(o => $"Kill {o.Count}x {o.TargetName ?? $"creature#{o.CreatureEntry}"}"))
                : toAccept.Node.HasItemObjectives
                    ? string.Join(", ", toAccept.Node.ItemObjectives
                        .Select(i => $"Gather {i.Count}x {i.ItemName ?? $"item#{i.ItemId}"}"))
                    : "delivery/talk quest";

            _logger.LogInformation(
                "[BOT-QUEST] {Name}({Guid}) | ACCEPTING [{QuestId}] \"{Title}\" | Objectives: {Obj} | force={Force}",
                bot.Name, bot.Guid, toAccept.QuestId, toAccept.Title, objDesc, forceMode);

            commands.Add(new BridgeCommand("QUEST_INTERACT", new
            {
                action = forceMode ? "force_accept" : "accept",
                quest_id = toAccept.QuestId,
                npc_entry = toAccept.Node.Giver!.NpcEntry
            }).WithCorr(bot.Story?.Intent(StoryVerb.ACCEPT, quest: new StoryQuest(toAccept.QuestId, toAccept.Title))));

            toAccept.Accepted = true;

            // Stay in AcceptingQuests — next tick will accept the next one
            // (or advance if this was the last)
            return commands;
        }

        // Not close enough to any giver — walk closer to the nearest unaccepted quest giver
        var nearestUnaccepted = activeQuests
            .Where(q => !q.Accepted && q.Node.Giver != null
                        && q.Node.Giver!.Map == state.MapId)
            .OrderBy(q => Distance2D(state.X, state.Y, q.Node.Giver!.X, q.Node.Giver!.Y))
            .FirstOrDefault();

        if (nearestUnaccepted != null)
        {
            float dist = Distance2D(state.X, state.Y,
                nearestUnaccepted.Node.Giver!.X, nearestUnaccepted.Node.Giver!.Y);

            if (dist > interactRadius)
            {
                if (forceMode)
                {
                    // Session 38: force mode never walks (see ProcessTurningIn).
                    SetPhaseDateTime(bot, "force_interact_until", default);
                    forceMode = false;
                }
                // Too far — go back to TravelingToGiver to walk closer
                _logger.LogDebug(
                    "[BOT-QUEST] {Name}({Guid}) | AcceptingQuests but {Dist:F1}yd from giver — moving closer",
                    bot.Name, bot.Guid, dist);
                AdvanceTo(bot, "TravelingToGiver");
                commands.Add(MakeMoveTo(nearestUnaccepted.Node.Giver!.X, nearestUnaccepted.Node.Giver!.Y,
                    nearestUnaccepted.Node.Giver!.Z, nearestUnaccepted.Node.Giver!.Map,
                    fromX: state.X, fromY: state.Y));
                ResetStuckDetection(bot, state);
                return commands;
            }
        }

        // Session 38: leaving the accept loop — drop force mode.
        if (forceMode) SetPhaseDateTime(bot, "force_interact_until", default);

        // ── Session 44: formation hold — done accepting HERE, but don't walk off
        // while glued followers still need to accept batch quests offered at this
        // giver. The coordinator re-fans force_accepts to them while we wait.
        if (HoldForFollowers(bot, state, needAccept: true))
            return commands;

        // No more quests to accept at this NPC — check for more at other givers
        var moreUnaccepted = activeQuests
            .Where(q => !q.Accepted && q.Node.Giver != null)
            .OrderBy(q => Distance2D(state.X, state.Y, q.Node.Giver!.X, q.Node.Giver!.Y))
            .FirstOrDefault();

        if (moreUnaccepted != null)
        {
            AdvanceTo(bot, "TravelingToGiver");
            commands.Add(MakeMoveTo(moreUnaccepted.Node.Giver!.X, moreUnaccepted.Node.Giver!.Y,
                moreUnaccepted.Node.Giver!.Z, moreUnaccepted.Node.Giver!.Map,
                fromX: state.X, fromY: state.Y));
            ResetStuckDetection(bot, state);
        }
        else
        {
            var turnIn = GetNearestCompletedTurnIn(bot, state, activeQuests);
            if (turnIn != null)
            {
                AdvanceTo(bot, "TravelingToTurnIn");
                commands.Add(MakeMoveTo(turnIn.Value.x, turnIn.Value.y, turnIn.Value.z, turnIn.Value.map,
                    fromX: state.X, fromY: state.Y));
            }
            else
            {
                AdvanceTo(bot, "TravelingToObjective");
                commands.AddRange(SendToNearestObjective(bot, state));
            }
        }

        return commands;
    }

    // ════════════════════════════════════════════════════════════════════
    // Sub-Phase: TravelingToObjective
    // ════════════════════════════════════════════════════════════════════

    private List<BridgeCommand> ProcessTravelingToObjective(BotIdentity bot, BotStateSnapshot state)
    {
        // Session 35: No follower override — all group members path independently.

        var activeQuests = GetActiveQuests(bot);

        var opportunistic = CheckOpportunisticObjective(bot, state, activeQuests);
        if (opportunistic.Count > 0) return opportunistic;

        var target = GetNearestObjectiveAcrossQuests(bot, state, activeQuests);

        if (target == null)
        {
            AdvanceTo(bot, "TravelingToTurnIn");
            return SendToNearestTurnIn(bot, state);
        }

        if (IsNear(state, target.Value.x, target.Value.y, target.Value.map, 15f))
        {
            AdvanceTo(bot, "DoingObjectives");
            if (target.Value.goEntry > 0)
                return SendGoInteractTask(bot, state, target.Value);
            return SendGrindTask(bot, target.Value);
        }

        return CheckTravelNudge(bot, state, target.Value.x, target.Value.y,
            target.Value.z, target.Value.map);
    }

    // ════════════════════════════════════════════════════════════════════
    // Sub-Phase: DoingObjectives (multi-quest aware)
    // ════════════════════════════════════════════════════════════════════

    private List<BridgeCommand> ProcessDoingObjectives(BotIdentity bot, BotStateSnapshot state)
    {
        var activeQuests = GetActiveQuests(bot);

        // Check if ALL objectives for ALL quests are complete
        bool allDone = true;
        foreach (var aq in activeQuests.Where(q => q.Accepted && !q.TurnedIn))
        {
            bool questDone = aq.ServerComplete || AllObjectivesComplete(aq);
            if (!questDone) allDone = false;
            else aq.ServerComplete = true;
        }

        if (allDone)
        {
            var commands = new List<BridgeCommand>
            {
                new BridgeCommand("SET_TASK", new { task = "IDLE" })
            };
            AdvanceTo(bot, "TravelingToTurnIn");
            commands.AddRange(SendToNearestTurnIn(bot, state));
            return commands;
        }

        // ── GO interaction mode ──
        int currentGoEntry = GetPhaseInt(bot, "current_go_entry");
        if (currentGoEntry > 0)
        {
            return ProcessDoingObjectivesGO(bot, state, activeQuests, currentGoEntry);
        }

        // ── Creature grind mode (existing logic) ──
        // Check if current grind target's objectives are done
        // Session 19 fix: check BOTH kill objectives AND item-drop objectives.
        int currentCreature = GetPhaseInt(bot, "current_grind_creature");
        bool currentTargetDone = true;
        bool foundMatchingObjective = false;

        foreach (var aq in activeQuests.Where(q => q.Accepted && !q.ServerComplete))
        {
            // Check kill objectives
            foreach (var obj in aq.Node.Objectives)
            {
                if (obj.IsCreature && obj.CreatureEntry == currentCreature)
                {
                    foundMatchingObjective = true;
                    aq.KillProgress.TryGetValue(obj.Slot, out int prog);
                    if (prog < obj.Count) { currentTargetDone = false; break; }
                }
            }
            if (!currentTargetDone) break;

            // Check item-drop objectives (Session 19)
            foreach (var itemObj in aq.Node.ItemObjectives)
            {
                var source = itemObj.BestDropSource;
                if (source != null && source.CreatureEntry == currentCreature)
                {
                    foundMatchingObjective = true;
                    int held = aq.ItemProgress.GetValueOrDefault(itemObj.ItemId, 0);
                    if (held < itemObj.Count) { currentTargetDone = false; break; }
                }
            }
            if (!currentTargetDone) break;
        }

        // Session 19: if we didn't find ANY objective matching this creature,
        // treat it as "not done" to avoid the bounce loop.
        if (!foundMatchingObjective)
            currentTargetDone = false;

        if (currentTargetDone)
        {
            var next = GetNearestObjectiveAcrossQuests(bot, state, activeQuests);
            if (next == null)
            {
                // Session 35: Band of Brothers — ALL group members wait when their
                // objectives are done but other groupmates haven't finished.
                // This includes members who went to vendor/train and came back.
                // The group stays here grinding for XP until everyone's ready.
                if (bot.IsGrouped && !bot.GroupAllObjectivesDone)
                {
                    _logger.LogDebug(
                        "[BOT-GROUP] {Name}({Guid}) | All MY objectives done but groupmates still working — waiting",
                        bot.Name, bot.Guid);
                    BotTrace.Wait(bot, WaitOn.GroupObjectives, "objectives done, holding for peers", state);
                    // Stay in DoingObjectives — keep grinding for XP and shared kill credit
                    return new List<BridgeCommand>();
                }

                var commands = new List<BridgeCommand>
                {
                    new BridgeCommand("SET_TASK", new { task = "IDLE" })
                };
                AdvanceTo(bot, "TravelingToTurnIn");
                commands.AddRange(SendToNearestTurnIn(bot, state));
                return commands;
            }

            AdvanceTo(bot, "TravelingToObjective");
            return new List<BridgeCommand> { MakeMoveTo(next.Value.x, next.Value.y, next.Value.z, next.Value.map) };
        }

        foreach (var aq in activeQuests.Where(q => q.Accepted && !q.TurnedIn))
            UpdateQuestProgress(aq);

        // ── Session 36: no-progress guard ────────────────────────────────
        // We are "doing objectives" but the current target is not done. A C++
        // GRIND should be running and KILL/LOOT events refresh do_progress_ts.
        // If that stamp goes stale, the grind is wedged and this handler would
        // otherwise return empty forever (owner=CS limbo) until the 20-min
        // watchdog. Self-heal instead of waiting.
        var lastProgress = GetPhaseDateTime(bot, "do_progress_ts");
        if (lastProgress == default)
        {
            // First quiet tick — start the clock, do not act yet.
            SetPhaseDateTime(bot, "do_progress_ts", DateTime.UtcNow);
            return new List<BridgeCommand>();
        }

        if ((DateTime.UtcNow - lastProgress).TotalSeconds < DO_NO_PROGRESS_SEC)
            return new List<BridgeCommand>();

        int rearms = GetPhaseInt(bot, "do_noprogress_rearms");

        if (rearms < DO_MAX_NOPROGRESS_REARMS)
        {
            SetPhaseInt(bot, "do_noprogress_rearms", rearms + 1);
            SetPhaseDateTime(bot, "do_progress_ts", DateTime.UtcNow);

            var next = GetNearestObjectiveAcrossQuests(bot, state, activeQuests);
            if (next != null)
            {
                _logger.LogWarning(
                    "[BOT-QUEST] {Name}({Guid}) | DoingObjectives made no progress for {Sec:F0}s — " +
                    "relocating/re-grinding nearest objective ({N}/{Max})",
                    bot.Name, bot.Guid, DO_NO_PROGRESS_SEC, rearms + 1, DO_MAX_NOPROGRESS_REARMS);

                // Already on the objective → re-arm the grind in place; else walk back.
                if (IsNear(state, next.Value.x, next.Value.y, next.Value.map, 15f))
                    return SendGrindTask(bot, next.Value);

                AdvanceTo(bot, "TravelingToObjective");
                return new List<BridgeCommand> { MakeMoveTo(next.Value.x, next.Value.y, next.Value.z, next.Value.map) };
            }

            // No reachable objective left — try to close the batch out instead.
            AdvanceTo(bot, "TravelingToTurnIn");
            return SendToNearestTurnIn(bot, state);
        }

        // Relocating did not help: the objective(s) pinning us here are a dead end
        // for now. Defer the un-turned-in quests with incomplete objectives, reset
        // the counters, and go do something else.
        SetPhaseInt(bot, "do_noprogress_rearms", 0);
        SetPhaseDateTime(bot, "do_progress_ts", default);

        foreach (var aq in activeQuests.Where(q => q.Accepted && !q.TurnedIn
                                                   && !q.ServerComplete && !AllObjectivesComplete(q)))
        {
            bot.DeferQuest(aq.QuestId, TimeSpan.FromMinutes(DO_NOPROGRESS_DEFER_MIN));
            bot.Story?.Emit(StoryVerb.DEFER, StoryResult.SKIP, quest: new StoryQuest(aq.QuestId, aq.Title), reason: "noprogress", detail: $"{DO_NOPROGRESS_DEFER_MIN}min after relocations");
            _logger.LogWarning(
                "[BOT-QUEST] {Name}({Guid}) | DoingObjectives stalled through {Max} relocations — " +
                "deferring [{QuestId}] \"{Title}\" {Min}min and re-picking",
                bot.Name, bot.Guid, DO_MAX_NOPROGRESS_REARMS, aq.QuestId, aq.Title, DO_NOPROGRESS_DEFER_MIN);

            _diagnostics.RecordIssue(DiagnosticIssueType.QuestDeferred, bot,
                state.X, state.Y, state.MapId,
                questId: aq.QuestId, questTitle: aq.Title,
                detail: $"doingobjectives no-progress through {DO_MAX_NOPROGRESS_REARMS} relocations — deferred {DO_NOPROGRESS_DEFER_MIN}min");
        }

        return FallbackToPickingQuests(bot);
    }

    /// <summary>
    /// GO interaction sub-loop. Walks between GO spawn positions and sends
    /// USE_GAMEOBJECT commands. Each tick picks a random spawn within the cluster
    /// and walks to it, then interacts. C++ finds the nearest spawned GO within
    /// 15yd and loots it. The LOOT event updates ItemProgress automatically.
    /// </summary>
    private List<BridgeCommand> ProcessDoingObjectivesGO(BotIdentity bot, BotStateSnapshot state,
        List<ActiveQuestEntry> activeQuests, int goEntry)
    {
        var commands = new List<BridgeCommand>();

        // Find the GO source to get spawn positions
        GameObjectDropSource? goSource = null;
        foreach (var aq in activeQuests.Where(q => q.Accepted && !q.ServerComplete))
        {
            foreach (var itemObj in aq.Node.ItemObjectives)
            {
                var src = itemObj.BestGoSource;
                if (src != null && src.GoEntry == goEntry)
                {
                    goSource = src;
                    break;
                }
            }
            if (goSource != null) break;
        }

        if (goSource == null || goSource.SpawnPositions.Count == 0)
        {
            _logger.LogWarning("[BOT-QUEST] {Name}({Guid}) | GO source lost for goEntry={GoEntry}, falling back",
                bot.Name, bot.Guid, goEntry);
            SetPhaseInt(bot, "current_go_entry", 0);
            AdvanceTo(bot, "PickingQuests");
            return commands;
        }

        // ── Session 41: pin one spawn until we interact there ──
        // The old per-call random re-roll oscillated between spawns and (with
        // the unthrottled walk below) fed the event-rate MOVE_TO loop.
        int spawnPin = GetPhaseInt(bot, "go_spawn_pin");          // 1-based; 0 = none
        if (spawnPin < 1 || spawnPin > goSource.SpawnPositions.Count)
        {
            var rng = new Random();
            spawnPin = rng.Next(goSource.SpawnPositions.Count) + 1;
            SetPhaseInt(bot, "go_spawn_pin", spawnPin);
        }
        var spawn = goSource.SpawnPositions[spawnPin - 1];

        // If we're not close to the pinned spawn, walk to it first.
        // Session 41: the walk gets its own 3 s gate — the old code throttled
        // only USE_GAMEOBJECT, so arrivals could re-trigger walks at event rate.
        float distToSpawn = Distance2D(state.X, state.Y, spawn.X, spawn.Y);
        if (distToSpawn > 8f)
        {
            var lastWalk = GetPhaseDateTime(bot, "go_last_walk");
            if (lastWalk != default && (DateTime.UtcNow - lastWalk).TotalSeconds < 3.0)
                return commands;
            SetPhaseDateTime(bot, "go_last_walk", DateTime.UtcNow);
            commands.Add(MakeMoveTo(spawn.X, spawn.Y, spawn.Z, goSource.Map));
            return commands;
        }

        // We're close — send USE_GAMEOBJECT (3 s gate) and release the pin so
        // the next attempt may pick a different spawn.
        var lastInteract = GetPhaseDateTime(bot, "go_last_interact");
        if (lastInteract != default && (DateTime.UtcNow - lastInteract).TotalSeconds < 3.0)
            return commands;

        commands.Add(new BridgeCommand("USE_GAMEOBJECT", new { go_entry = goEntry }).WithCorr(bot.Story?.Intent(StoryVerb.OBJECTIVE, target: new StoryTarget(spawn.X, spawn.Y, spawn.Z, goSource.Map))));
        SetPhaseDateTime(bot, "go_last_interact", DateTime.UtcNow);
        SetPhaseInt(bot, "go_spawn_pin", 0);

        _logger.LogDebug("[BOT-QUEST] {Name}({Guid}) | USE_GAMEOBJECT goEntry={GoEntry} at ({X},{Y})",
            bot.Name, bot.Guid, goEntry, (int)spawn.X, (int)spawn.Y);

        return commands;
    }


    // ════════════════════════════════════════════════════════════════════
    // Sub-Phase: TravelingToTurnIn
    // ════════════════════════════════════════════════════════════════════

    private List<BridgeCommand> ProcessTravelingToTurnIn(BotIdentity bot, BotStateSnapshot state)
    {
        // Session 35: No follower override — all group members path independently.

        var activeQuests = GetActiveQuests(bot);

        var opportunistic = CheckOpportunisticObjective(bot, state, activeQuests);
        if (opportunistic.Count > 0) return opportunistic;

        var turnIn = GetNearestCompletedTurnIn(bot, state, activeQuests);

        if (turnIn == null)
        {
            var obj = GetNearestObjectiveAcrossQuests(bot, state, activeQuests);
            if (obj != null)
            {
                AdvanceTo(bot, "TravelingToObjective");
                return new List<BridgeCommand> { MakeMoveTo(obj.Value.x, obj.Value.y, obj.Value.z, obj.Value.map) };
            }

            AdvanceTo(bot, "BatchComplete");
            return new List<BridgeCommand>();
        }

        // Arrive within 8yd before transitioning — C++ needs bot within 15yd of NPC
        if (IsNear(state, turnIn.Value.x, turnIn.Value.y, turnIn.Value.map, 8f))
        {
            AdvanceTo(bot, "TurningIn");
            return new List<BridgeCommand>();
        }

        return CheckTravelNudge(bot, state, turnIn.Value.x, turnIn.Value.y,
            turnIn.Value.z, turnIn.Value.map);
    }

    // ════════════════════════════════════════════════════════════════════
    // Sub-Phase: TurningIn (batch turn-in at current NPC)
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// TurningIn: complete ONE quest per tick to avoid rapid-fire QUEST_INTERACT
    /// commands crashing C++. Same rationale as AcceptingQuests.
    /// </summary>
    private List<BridgeCommand> ProcessTurningIn(BotIdentity bot, BotStateSnapshot state)
    {
        var commands = new List<BridgeCommand>();
        var activeQuests = GetActiveQuests(bot);

        // ── Session 38: force mode — armed by HandleMoveFailed last-leg path.
        // Widens the interact radius (C++ force search = 300yd) and switches
        // the bridge action to force_complete. TTL'd; cleared on loop exit.
        bool forceMode = GetPhaseDateTime(bot, "force_interact_until") > DateTime.UtcNow;
        float interactRadius = forceMode ? FORCE_INTERACT_NPC_RADIUS : 10f;

        // Find the FIRST completed quest whose turn-in NPC is within interact range.
        // C++ requires 15yd (GetCreatureListWithEntryInGrid), so we use 10yd to be safe.
        var toTurnIn = activeQuests
            .Where(q => q.Accepted && !q.TurnedIn
                        && (q.ServerComplete || AllObjectivesComplete(q))
                        && q.Node.TurnIn != null
                        && IsNear(state, q.Node.TurnIn!.X, q.Node.TurnIn!.Y, q.Node.TurnIn!.Map, interactRadius))
            .FirstOrDefault();

        if (toTurnIn != null)
        {
            _logger.LogInformation(
                "[BOT-QUEST] {Name}({Guid}) | COMPLETING [{QuestId}] \"{Title}\" | " +
                "Rewards: {XP}xp {Money}copper | force={Force}",
                bot.Name, bot.Guid, toTurnIn.QuestId, toTurnIn.Title,
                toTurnIn.Node.RewXP, toTurnIn.Node.RewMoney, forceMode);

            commands.Add(new BridgeCommand("QUEST_INTERACT", new
            {
                action = forceMode ? "force_complete" : "complete",
                quest_id = toTurnIn.QuestId,
                npc_entry = toTurnIn.Node.TurnIn!.NpcEntry
            }).WithCorr(bot.Story?.Intent(StoryVerb.TURNIN, quest: new StoryQuest(toTurnIn.QuestId, toTurnIn.Title))));

            toTurnIn.TurnedIn = true;
            bot.CompletedQuestIds.Add(toTurnIn.QuestId);

            // Stay in TurningIn — next tick will turn in the next one
            // (or advance if this was the last at this NPC)
            return commands;
        }

        // Not close enough to any turn-in NPC — check if there's a completed quest
        // whose turn-in NPC is on the same map but beyond 10yd, and walk closer
        var nearestCompleted = activeQuests
            .Where(q => q.Accepted && !q.TurnedIn
                        && (q.ServerComplete || AllObjectivesComplete(q))
                        && q.Node.TurnIn != null
                        && q.Node.TurnIn!.Map == state.MapId)
            .OrderBy(q => Distance2D(state.X, state.Y, q.Node.TurnIn!.X, q.Node.TurnIn!.Y))
            .FirstOrDefault();

        if (nearestCompleted != null)
        {
            float dist = Distance2D(state.X, state.Y,
                nearestCompleted.Node.TurnIn!.X, nearestCompleted.Node.TurnIn!.Y);
            if (dist > interactRadius)
            {
                if (forceMode)
                {
                    // Session 38: force mode never walks — the walk is exactly
                    // what just MOVE_FAILED. Drop force and fall through to the
                    // normal walk-closer (it will fail and re-enter the defer path).
                    SetPhaseDateTime(bot, "force_interact_until", default);
                    forceMode = false;
                }
                _logger.LogDebug(
                    "[BOT-QUEST] {Name}({Guid}) | TurningIn but {Dist:F1}yd from turn-in NPC — moving closer",
                    bot.Name, bot.Guid, dist);
                AdvanceTo(bot, "TravelingToTurnIn");
                commands.Add(MakeMoveTo(nearestCompleted.Node.TurnIn!.X, nearestCompleted.Node.TurnIn!.Y,
                    nearestCompleted.Node.TurnIn!.Z, nearestCompleted.Node.TurnIn!.Map,
                    fromX: state.X, fromY: state.Y));
                ResetStuckDetection(bot, state);
                return commands;
            }
        }

        // Session 38: leaving the turn-in loop — drop force mode so it cannot
        // bleed into interactions elsewhere.
        if (forceMode) SetPhaseDateTime(bot, "force_interact_until", default);

        // ── Session 44: formation hold — done turning in HERE, but don't walk off
        // while glued followers still hold batch quests this ender rewards. The
        // coordinator re-fans force_completes to them while we wait.
        if (HoldForFollowers(bot, state, needAccept: false))
            return commands;

        // No more quests to turn in at this NPC
        var moreTurnIns = GetNearestCompletedTurnIn(bot, state, activeQuests);
        if (moreTurnIns != null)
        {
            AdvanceTo(bot, "TravelingToTurnIn");
            commands.Add(MakeMoveTo(moreTurnIns.Value.x, moreTurnIns.Value.y,
                moreTurnIns.Value.z, moreTurnIns.Value.map,
                fromX: state.X, fromY: state.Y));
            ResetStuckDetection(bot, state);
        }
        else
        {
            var remaining = GetNearestObjectiveAcrossQuests(bot, state, activeQuests);
            if (remaining != null)
            {
                AdvanceTo(bot, "TravelingToObjective");
                commands.Add(MakeMoveTo(remaining.Value.x, remaining.Value.y,
                    remaining.Value.z, remaining.Value.map));
            }
            else
            {
                AdvanceTo(bot, "BatchComplete");
            }
        }

        return commands;
    }

    // ════════════════════════════════════════════════════════════════════
    // Sub-Phase: BatchComplete
    // ════════════════════════════════════════════════════════════════════

    private List<BridgeCommand> ProcessBatchComplete(BotIdentity bot, BotStateSnapshot state)
    {
        var activeQuests = GetActiveQuests(bot);

        var completed = activeQuests.Where(q => q.TurnedIn).Select(q => $"[{q.QuestId}]\"{q.Title}\"");
        var failed = activeQuests.Where(q => !q.TurnedIn).Select(q => $"[{q.QuestId}]\"{q.Title}\"");

        _logger.LogInformation(
            "[BOT-QUEST] {Name}({Guid}) | BATCH COMPLETE | " +
            "Completed: {Completed} | Failed/remaining: {Failed} | " +
            "Total completed: {TotalCompleted}",
            bot.Name, bot.Guid,
            string.Join(", ", completed),
            string.Join(", ", failed),
            bot.CompletedQuestIds.Count);

        bot.ActiveQuestId = null;
        bot.CurrentQuestProgress = 0f;
        bot.QuestObjectiveProgress.Clear();
        bot.QuestItemProgress.Clear();

        // ── Session 45: if anything in the batch was NOT turned in, do not wipe
        // and re-pick — PhaseData.Clear() was destroying the Failed/remaining list
        // while the server still held those quests (status=COMPLETE, rewarded=0).
        // The bot then re-picked the same quests and C++ rejected the duplicate
        // accept with requirements_not_met (already in log) → 30 min defer each.
        // Resync from server truth instead: RebuildFromQuestSync routes straight
        // back to TravelingToTurnIn for survivors, or PickingQuests when empty.
        bool anyRemaining = activeQuests.Any(q => q.Accepted && !q.TurnedIn);

        bot.CurrentActivity.PhaseData.Clear();
        bot.ResetDeathCounter();

        if (anyRemaining)
        {
            AdvanceTo(bot, "WaitingForQuestSync");
            bot.CurrentActivity.ContextTag = "quests:resync";
            bot.CurrentActivity.IsInterruptible = false;
            BotTrace.Wait(bot, WaitOn.Event("QUEST_STATUS_ALL"), "batch remainder: resyncing", state);
            return new List<BridgeCommand> { new BridgeCommand("QUERY_QUEST_STATUS", new { }) };
        }

        AdvanceTo(bot, "PickingQuests");
        return ProcessPickingQuests(bot, state);
    }

    // ════════════════════════════════════════════════════════════════════
    // Event Handlers
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Handle KILL — update KILL progress across ALL active quests.
    /// A single kill might advance multiple quests simultaneously.
    /// NOTE: Item-drop quest progress is handled by HandleLootEvent, NOT here.
    /// Previously this read from ShadowInventory (deprecated/empty) and overwrote
    /// ItemProgress with 0 on every kill, breaking all item-drop quests.
    /// </summary>
    private List<BridgeCommand> HandleKillEvent(BotIdentity bot, BotEvent evt)
    {
        var activeQuests = GetActiveQuests(bot);

        foreach (var aq in activeQuests.Where(q => q.Accepted && !q.TurnedIn))
        {
            foreach (var obj in aq.Node.Objectives)
            {
                if (obj.IsCreature && obj.CreatureEntry == evt.CreatureEntry)
                {
                    aq.KillProgress.TryGetValue(obj.Slot, out int current);
                    if (current < obj.Count)
                    {
                        aq.KillProgress[obj.Slot] = current + 1;
                        UpdateQuestProgress(aq);
                    }
                }
            }
            // Item-drop progress is updated by HandleLootEvent, not here.
        }

        // Backward compat
        if (bot.ActiveQuestId.HasValue)
        {
            var primary = activeQuests.FirstOrDefault(q => q.QuestId == bot.ActiveQuestId.Value);
            if (primary != null)
            {
                bot.QuestObjectiveProgress = new Dictionary<int, int>(primary.KillProgress);
                bot.CurrentQuestProgress = primary.Progress;
            }
        }

        // Session 36: any kill while doing objectives proves the grind is alive —
        // refresh the no-progress guard and clear the relocation counter.
        if (bot.CurrentActivity.SubPhase == "DoingObjectives")
        {
            SetPhaseDateTime(bot, "do_progress_ts", DateTime.UtcNow);
            SetPhaseInt(bot, "do_noprogress_rearms", 0);

            // ── Session 47: mid-grind server-truth resync ──────────────────
            // The local KillProgress above only counts THIS bot's own taps. In a
            // group, kills credited by followers (and the leader to them) are
            // invisible here, so the count crawls and the grind runs to the C++
            // self-tap GRIND goal instead of stopping when the GROUP finishes
            // (measured: server-complete +0:00, turn-in +10:30). Throttle a
            // QUEST_STATUS re-query; the QUEST_STATUS_ALL handler refreshes
            // ServerComplete from server truth and ProcessDoingObjectives stops
            // the grind. Solo bots: harmless (their self-tap IS the server count).
            var lastResync = GetPhaseDateTime(bot, "do_last_resync");
            if (lastResync == default
                || (DateTime.UtcNow - lastResync).TotalSeconds >= DO_QUEST_RESYNC_SEC)
            {
                SetPhaseDateTime(bot, "do_last_resync", DateTime.UtcNow);
                return new List<BridgeCommand> { new BridgeCommand("QUERY_QUEST_STATUS", new { }) };
            }
        }

        return new List<BridgeCommand>();
    }

    /// <summary>
    /// Handle LOOT — update ITEM progress across ALL active quests.
    /// EconomyDomain.ProcessLootEvent already parses the LOOT event and populates
    /// bot.QuestItemProgress with real item counts from C++. We read from that
    /// authoritative source to update each ActiveQuestEntry.ItemProgress.
    ///
    /// This replaces the old ShadowInventory-based tracking in HandleKillEvent
    /// which was always reading 0 (ShadowInventory is deprecated/empty).
    /// </summary>
    private List<BridgeCommand> HandleLootEvent(BotIdentity bot, BotEvent evt)
    {
        var activeQuests = GetActiveQuests(bot);
        bool anyChanged = false;

        foreach (var aq in activeQuests.Where(q => q.Accepted && !q.TurnedIn))
        {
            foreach (var itemObj in aq.Node.ItemObjectives)
            {
                int held = bot.QuestItemProgress.GetValueOrDefault(itemObj.ItemId, 0);
                int prev = aq.ItemProgress.GetValueOrDefault(itemObj.ItemId, 0);

                if (held != prev)
                {
                    aq.ItemProgress[itemObj.ItemId] = held;
                    anyChanged = true;

                    if (held >= itemObj.Count && prev < itemObj.Count)
                    {
                        _logger.LogInformation(
                            "[BOT-QUEST] {Name}({Guid}) | Item objective COMPLETE for [{QuestId}] \"{Title}\" — " +
                            "{ItemName} {Held}/{Need}",
                            bot.Name, bot.Guid, aq.QuestId, aq.Title,
                            itemObj.ItemName ?? $"item#{itemObj.ItemId}", held, itemObj.Count);
                    }
                }
            }

            if (anyChanged)
                UpdateQuestProgress(aq);
        }

        // Session 36: item progress while doing objectives = grind alive.
        if (anyChanged && bot.CurrentActivity.SubPhase == "DoingObjectives")
        {
            SetPhaseDateTime(bot, "do_progress_ts", DateTime.UtcNow);
            SetPhaseInt(bot, "do_noprogress_rearms", 0);
        }

        return new List<BridgeCommand>();
    }

    private List<BridgeCommand> HandleTaskComplete(BotIdentity bot, BotStateSnapshot state)
    {
        var commands = new List<BridgeCommand>();
        var subPhase = bot.CurrentActivity.SubPhase;

        if (subPhase == "DoingObjectives")
        {
            // ── Session 41: TASK_COMPLETE conflation guard ──
            // TASK_COMPLETE fires for BOTH grind-goal-met AND MOVE_TO arrivals.
            // In GO mode there is no grind task outstanding, so any TASK_COMPLETE
            // here is a GO-walk arrival. Pre-41 this branch treated it as
            // goal-met and re-routed to the "next objective" (≈ the bot's own
            // position); with Session-39 instant acks that closed an event-rate
            // MOVE_TO loop (June-12 run: 8,491 GO-mode re-entries vs 104 actual
            // USE_GAMEOBJECTs) which starved the real GO interaction. Route the
            // arrival into the GO loop so USE_GAMEOBJECT fires immediately.
            if (GetPhaseInt(bot, "current_go_entry") > 0)
            {
                commands.AddRange(OnTick(bot, state));
                return commands;
            }

            // No grind armed either → this is a stray movement ack (e.g. a
            // relocation walk completing). Ignore it; OnTick owns recovery.
            if (GetPhaseInt(bot, "current_grind_creature") <= 0)
                return commands;

            var activeQuests = GetActiveQuests(bot);
            var next = GetNearestObjectiveAcrossQuests(bot, state, activeQuests);

            if (next == null)
            {
                commands.Add(new BridgeCommand("SET_TASK", new { task = "IDLE" }));
                AdvanceTo(bot, "TravelingToTurnIn");
                commands.AddRange(SendToNearestTurnIn(bot, state));
            }
            else
            {
                AdvanceTo(bot, "TravelingToObjective");
                commands.Add(MakeMoveTo(next.Value.x, next.Value.y, next.Value.z, next.Value.map));
            }
        }
        else if (subPhase != null && subPhase.StartsWith("Traveling"))
        {
            _logger.LogInformation(
                "[BOT-ARRIVE] {Name}({Guid}) | MOVE_TO arrived during {SubPhase} — processing immediately",
                bot.Name, bot.Guid, subPhase);
            commands.AddRange(OnTick(bot, state));
        }

        return commands;
    }

    private List<BridgeCommand> HandleQuestInteractFail(BotIdentity bot, BotStateSnapshot state, BotEvent evt)
    {
        var commands = new List<BridgeCommand>();
        int failedQuestId = 0;
        // C++ BridgeSendEvent puts fail reason in data field, not message
        string failReason = evt.Data ?? evt.Message ?? "unknown";

        if (failReason.Contains("|quest_id="))
        {
            var parts = failReason.Split('|');
            failReason = parts[0];
            var qidPart = parts.FirstOrDefault(p => p.StartsWith("quest_id="));
            if (qidPart != null) int.TryParse(qidPart.Replace("quest_id=", ""), out failedQuestId);
        }

        if (failedQuestId == 0 && bot.ActiveQuestId.HasValue)
            failedQuestId = bot.ActiveQuestId.Value;

        if (failedQuestId > 0)
        {
            var failedQuest = _questGraph.GetQuest(failedQuestId);

            _logger.LogError(
                "[BOT-QUEST] {Name}({Guid}) | *** QUEST INTERACT FAILED *** [{QuestId}] \"{Title}\" | " +
                "Reason: {Reason} | Bot level={Level}",
                bot.Name, bot.Guid, failedQuestId, failedQuest?.Title ?? "?",
                failReason, bot.Level);

            _diagnostics.RecordIssue(DiagnosticIssueType.QuestInteractFail, bot,
                state.X, state.Y, state.MapId,
                questId: failedQuestId, questTitle: failedQuest?.Title,
                detail: $"reason={failReason}, freeSlots={state.FreeSlots}");

            // ── Session 45: a failed interact means the server did NOT reward this
            // quest. ProcessTurningIn marks TurnedIn + CompletedQuestIds optimistically
            // on SEND; without this revert a single failed complete makes the quest
            // permanently invisible to every turn-in query (!q.TurnedIn filters all of
            // them) while the server still holds it at rewarded=0, and the polluted
            // CompletedQuestIds leaks into chain prereqs and the S44 fan-out skip.
            var aqRevert = GetActiveQuests(bot);
            var revertEntry = aqRevert.FirstOrDefault(q => q.QuestId == failedQuestId);
            if (revertEntry != null && revertEntry.TurnedIn)
            {
                revertEntry.TurnedIn = false;
                bot.CompletedQuestIds.Remove(failedQuestId);
                _logger.LogWarning(
                    "[BOT-QUEST] {Name}({Guid}) | Reverting optimistic turn-in mark on [{QuestId}] after {Reason}",
                    bot.Name, bot.Guid, failedQuestId, failReason);
            }

            bool isBagsFull = (failReason == "cannot_reward" || failReason == "bags_full")
                              && state.FreeSlots == 0;

            bool isNpcNotFound = failReason == "npc_not_found";

            if (isBagsFull)
            {
                bot.PendingAction = new PendingAction
                {
                    ReturnTo = ActivityType.Questing,
                    SubPhase = "TravelingToTurnIn",
                    QuestId = failedQuestId
                };
                _logger.LogWarning(
                    "[BOT-QUEST] {Name}({Guid}) | Bags full during turn-in of [{QuestId}] — saving PendingAction",
                    bot.Name, bot.Guid, failedQuestId);
            }
            else if (isNpcNotFound)
            {
                // ── Session 38: npc_not_found while in force mode ──
                // Even the C++ 300yd force search missed the NPC, so the
                // walk-closer retry below is pointless — that walk is exactly
                // what MOVE_FAILED. Clear force mode and defer instead.
                if (GetPhaseDateTime(bot, "force_interact_until") > DateTime.UtcNow)
                {
                    SetPhaseDateTime(bot, "force_interact_until", default);
                    _logger.LogWarning(
                        "[BOT-QUEST] {Name}({Guid}) | npc_not_found during force interact for [{QuestId}] — " +
                        "clearing force mode, deferring 30min",
                        bot.Name, bot.Guid, failedQuestId);
                    bot.DeferQuest(failedQuestId, TimeSpan.FromMinutes(30));

                    var aqForce = GetActiveQuests(bot);
                    aqForce.RemoveAll(q => q.QuestId == failedQuestId);
                    SetActiveQuests(bot, aqForce);
                    if (aqForce.Count == 0)
                        commands.AddRange(FallbackToPickingQuests(bot));
                    return commands;
                }

                // Bot was too far from the NPC — walk closer and retry instead of deferring.
                // Track retries to avoid infinite loops; after 3 attempts, defer normally.
                int npcRetries = GetPhaseInt(bot, $"npc_retry_{failedQuestId}");
                npcRetries++;
                SetPhaseInt(bot, $"npc_retry_{failedQuestId}", npcRetries);

                if (npcRetries >= 3)
                {
                    _logger.LogWarning(
                        "[BOT-QUEST] {Name}({Guid}) | npc_not_found 3x for [{QuestId}] — deferring 10min",
                        bot.Name, bot.Guid, failedQuestId);
                    bot.DeferQuest(failedQuestId, TimeSpan.FromMinutes(10));

                    var activeQuests2 = GetActiveQuests(bot);
                    activeQuests2.RemoveAll(q => q.QuestId == failedQuestId);
                    SetActiveQuests(bot, activeQuests2);
                    if (activeQuests2.Count == 0)
                        commands.AddRange(FallbackToPickingQuests(bot));
                }
                else
                {
                    // Find the NPC coords and walk closer
                    var activeQuests2 = GetActiveQuests(bot);
                    var entry = activeQuests2.FirstOrDefault(q => q.QuestId == failedQuestId);
                    var subPhase = bot.CurrentActivity.SubPhase ?? "";

                    float npcX = 0, npcY = 0, npcZ = 0;
                    int npcMap = state.MapId;
                    bool hasTarget = false;

                    if (entry != null)
                    {
                        if (!entry.Accepted && entry.Node.Giver != null)
                        {
                            npcX = entry.Node.Giver.X; npcY = entry.Node.Giver.Y;
                            npcZ = entry.Node.Giver.Z; npcMap = entry.Node.Giver.Map;
                            hasTarget = true;
                        }
                        else if (entry.Node.TurnIn != null)
                        {
                            npcX = entry.Node.TurnIn.X; npcY = entry.Node.TurnIn.Y;
                            npcZ = entry.Node.TurnIn.Z; npcMap = entry.Node.TurnIn.Map;
                            hasTarget = true;
                        }
                    }
                    else if (failedQuest != null)
                    {
                        // Quest not in active batch — try quest graph
                        if (failedQuest.Giver != null)
                        {
                            npcX = failedQuest.Giver.X; npcY = failedQuest.Giver.Y;
                            npcZ = failedQuest.Giver.Z; npcMap = failedQuest.Giver.Map;
                            hasTarget = true;
                        }
                    }

                    if (hasTarget)
                    {
                        _logger.LogWarning(
                            "[BOT-QUEST] {Name}({Guid}) | npc_not_found for [{QuestId}] (attempt {Retry}/3) — " +
                            "walking closer to NPC at ({X:F0},{Y:F0})",
                            bot.Name, bot.Guid, failedQuestId, npcRetries, npcX, npcY);

                        AdvanceTo(bot, subPhase.Contains("Turn") ? "TravelingToTurnIn" : "TravelingToGiver");
                        commands.Add(MakeMoveTo(npcX, npcY, npcZ, npcMap,
                            fromX: state.X, fromY: state.Y));
                        ResetStuckDetection(bot, state);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "[BOT-QUEST] {Name}({Guid}) | npc_not_found for [{QuestId}] but no NPC coords — deferring 10min",
                            bot.Name, bot.Guid, failedQuestId);
                        bot.DeferQuest(failedQuestId, TimeSpan.FromMinutes(10));

                        var aq = GetActiveQuests(bot);
                        aq.RemoveAll(q => q.QuestId == failedQuestId);
                        SetActiveQuests(bot, aq);
                        if (aq.Count == 0)
                            commands.AddRange(FallbackToPickingQuests(bot));
                    }
                }
            }
            else if (failReason == "cannot_reward" && state.FreeSlots > 0)
            {
                // C++ rejected the turn-in — quest isn't actually complete.
                // Most common case: C# kill count drifted ahead of C++ mob_count (off-by-one).
                // Instead of deferring 30min, reset completion status and send back to grind.
                var activeQuests = GetActiveQuests(bot);
                var entry = activeQuests.FirstOrDefault(q => q.QuestId == failedQuestId);
                if (entry != null)
                {
                    entry.ServerComplete = false;

                    _logger.LogWarning(
                        "[BOT-QUEST] {Name}({Guid}) | cannot_reward [{QuestId}] \"{Title}\" — " +
                        "resetting ServerComplete, returning to DoingObjectives",
                        bot.Name, bot.Guid, failedQuestId, entry.Title);

                    AdvanceTo(bot, "DoingObjectives");
                    var obj = GetNearestObjectiveAcrossQuests(bot, state, activeQuests);
                    if (obj != null)
                    {
                        commands.AddRange(SendGrindTask(bot, obj.Value));
                    }
                }
                else
                {
                    _logger.LogWarning(
                        "[BOT-QUEST] {Name}({Guid}) | cannot_reward [{QuestId}] but not in active batch — deferring 10min",
                        bot.Name, bot.Guid, failedQuestId);
                    bot.DeferQuest(failedQuestId, TimeSpan.FromMinutes(10));
                }
            }
            else
            {
                bot.DeferQuest(failedQuestId, TimeSpan.FromMinutes(30));

                var activeQuests = GetActiveQuests(bot);
                activeQuests.RemoveAll(q => q.QuestId == failedQuestId);
                SetActiveQuests(bot, activeQuests);

                if (activeQuests.Count == 0)
                    commands.AddRange(FallbackToPickingQuests(bot));
            }
        }

        return commands;
    }

    // ════════════════════════════════════════════════════════════════════
    // Opportunistic Objective Completion
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// While traveling, check if the bot is within range of an active quest
    /// objective. If so, interrupt travel to grind that objective.
    /// "Oh hey, there's a quest mob right here, let me kill it."
    /// </summary>
    private List<BridgeCommand> CheckOpportunisticObjective(
        BotIdentity bot, BotStateSnapshot state, List<ActiveQuestEntry> activeQuests)
    {
        foreach (var aq in activeQuests.Where(q => q.Accepted && !q.ServerComplete))
        {
            foreach (var obj in aq.Node.Objectives)
            {
                if (!obj.IsCreature || obj.GrindRadius <= 0) continue;
                aq.KillProgress.TryGetValue(obj.Slot, out int progress);
                if (progress >= obj.Count) continue;

                float dist = Distance2D(state.X, state.Y, obj.GrindX, obj.GrindY);
                if (dist <= OPPORTUNISTIC_OBJECTIVE_RADIUS)
                {
                    _logger.LogInformation(
                        "[BOT-OPPORTUNISTIC] {Name}({Guid}) | Spotted objective for [{QuestId}] \"{Title}\" " +
                        "({Dist:F0}yd away) — interrupting travel to grind!",
                        bot.Name, bot.Guid, aq.QuestId, aq.Title, dist);

                    AdvanceTo(bot, "DoingObjectives");
                    SetPhaseDateTime(bot, "do_progress_ts", DateTime.UtcNow);   // S36
                    SetPhaseInt(bot, "do_noprogress_rearms", 0);
                    int remaining = obj.Count - progress;
                    SetPhaseInt(bot, "current_grind_creature", obj.CreatureEntry);
                    // Session 31: Fan-out — pick per-bot spawn instead of centroid
                    float gX = obj.GrindX, gY = obj.GrindY, gZ = obj.GrindZ;
                    if (obj.SpawnPositions.Count > 1)
                    {
                        int idx = Math.Abs(bot.Guid.GetHashCode() ^ obj.CreatureEntry) % obj.SpawnPositions.Count;
                        var mySpawn = obj.SpawnPositions[idx];
                        gX = mySpawn.X; gY = mySpawn.Y; gZ = mySpawn.Z;
                    }

                    return new List<BridgeCommand>
                    {
                        // Clear any active MOVE_TO before switching to GRIND
                        new BridgeCommand("SET_TASK", new { task = "IDLE" }),
                        new BridgeCommand("SET_TASK", new
                        {
                            task = "GRIND",
                            x = gX, y = gY, z = gZ,
                            radius = 60.0f,
                            creature_entry = obj.CreatureEntry,
                            kill_count = remaining
                        })
                    };
                }
            }

            foreach (var itemObj in aq.Node.ItemObjectives)
            {
                int held = aq.ItemProgress.GetValueOrDefault(itemObj.ItemId, 0);
                if (held >= itemObj.Count) continue;

                var source = itemObj.BestDropSource;
                if (source == null || source.SpawnCount == 0) continue;

                float dist = Distance2D(state.X, state.Y, source.GrindX, source.GrindY);
                if (dist <= OPPORTUNISTIC_OBJECTIVE_RADIUS)
                {
                    _logger.LogInformation(
                        "[BOT-OPPORTUNISTIC] {Name}({Guid}) | Spotted drop source for [{QuestId}] " +
                        "\"{Title}\" ({Dist:F0}yd away) — interrupting travel!",
                        bot.Name, bot.Guid, aq.QuestId, aq.Title, dist);

                    AdvanceTo(bot, "DoingObjectives");
                    SetPhaseDateTime(bot, "do_progress_ts", DateTime.UtcNow);   // S36
                    SetPhaseInt(bot, "do_noprogress_rearms", 0);
                    SetPhaseInt(bot, "current_grind_creature", source.CreatureEntry);
                    // Session 31: Fan-out — pick per-bot spawn instead of centroid
                    float gX2 = source.GrindX, gY2 = source.GrindY, gZ2 = source.GrindZ;
                    if (source.SpawnPositions.Count > 1)
                    {
                        int idx = Math.Abs(bot.Guid.GetHashCode() ^ source.CreatureEntry) % source.SpawnPositions.Count;
                        var mySpawn = source.SpawnPositions[idx];
                        gX2 = mySpawn.X; gY2 = mySpawn.Y; gZ2 = mySpawn.Z;
                    }

                    return new List<BridgeCommand>
                    {
                        // Clear any active MOVE_TO before switching to GRIND
                        new BridgeCommand("SET_TASK", new { task = "IDLE" }),
                        new BridgeCommand("SET_TASK", new
                        {
                            task = "GRIND",
                            x = gX2, y = gY2, z = gZ2,
                            radius = 60.0f,
                            creature_entry = source.CreatureEntry,
                            kill_count = 0
                        })
                    };
                }
            }
        }

        return new List<BridgeCommand>();
    }

    // ════════════════════════════════════════════════════════════════════
    // Quest Selection (zone safety pre-filtered)
    // ════════════════════════════════════════════════════════════════════

    private List<(QuestNode quest, float score)> ScoreQuests(
        BotIdentity bot, List<QuestNode> available, BotStateSnapshot state)
    {
        var personality = bot.Personality;
        var scored = new List<(QuestNode quest, float score)>();

        int? lastCompletedChainNext = null;
        foreach (var completedId in bot.CompletedQuestIds)
        {
            var cq = _questGraph.GetQuest(completedId);
            if (cq?.NextQuestInChain > 0)
                lastCompletedChainNext = cq.NextQuestInChain;
        }

        foreach (var quest in available)
        {
            float score = 1.0f;
            bool isChain = false;

            if (lastCompletedChainNext.HasValue && quest.QuestId == lastCompletedChainNext.Value)
            { score *= 20.0f; isChain = true; }

            if (quest.PrevQuestId > 0 && bot.CompletedQuestIds.Contains(quest.PrevQuestId))
            { score *= 5.0f; isChain = true; }

            float roundTrip = EstimateRoundTripDistance(state.X, state.Y, quest);
            float distFactor = 1f / MathF.Pow(1f + roundTrip / 100f, 2f);
            distFactor = Lerp(distFactor, MathF.Sqrt(distFactor), personality.Patience * 0.3f);
            if (isChain) distFactor = MathF.Sqrt(distFactor);
            score *= distFactor;

            // Zone match bonus — strongly prefer quests whose ZoneOrSort matches
            // the bot's current zone. Session 29: cross-zone quests are effectively
            // blocked when local quests exist. Only Questing can decide to move zones
            // (when it genuinely runs out of local work).
            if (quest.ZoneId > 0 && state.ZoneId > 0 && quest.ZoneId == state.ZoneId)
                score *= 3.0f;
            else if (quest.ZoneId > 0 && state.ZoneId > 0 && quest.ZoneId != state.ZoneId)
            {
                // Check if there are ANY local zone quests still available.
                // If yes, cross-zone gets crushed. If no local quests remain,
                // allow cross-zone with mild penalty (the bot needs to move on).
                bool hasLocalQuests = available.Any(q => q.ZoneId == state.ZoneId && q.QuestId != quest.QuestId);
                score *= hasLocalQuests ? 0.01f : 0.5f;
            }

            // Low-level proximity clamp — at levels 1-5, quests beyond 150yd are
            // heavily penalized. A new player doesn't run 400yd to grab a quest
            // from a random NPC; they talk to whoever is nearby.
            if (bot.Level <= 5 && quest.Giver != null)
            {
                float giverDist = Distance2D(state.X, state.Y, quest.Giver.X, quest.Giver.Y);
                if (giverDist > 150f)
                    score *= 0.1f;
            }

            int levelDelta = quest.QuestLevel - bot.Level;
            float levelFit = levelDelta <= 2 ? 1.0f : levelDelta <= 4 ? 0.15f : 0.02f;
            if (bot.Level - quest.QuestLevel > 5) levelFit *= 0.5f;
            score *= levelFit;

            // Local preference (soft bonus — safety is already hard-filtered)
            if (roundTrip < 100f) score *= 1.5f;
            else if (roundTrip < 200f) score *= 1.2f;

            if (quest.RewXP > 0) score *= (1.0f + personality.Efficiency * 0.15f);
            if (quest.RewMoney > 0) score *= (1.0f + personality.Greed * 0.1f);
            if (personality.Curiosity > 0.7f && !isChain)
                score *= (1.0f + personality.Curiosity * 0.1f);

            if (personality.Quirks.Any(q => q.Id == "Completionist")) score *= 1.3f;
            if (personality.Quirks.Any(q => q.Id == "SpeedDemon") && !isChain && roundTrip > 300)
                score *= 0.1f;
            if (personality.Quirks.Any(q => q.Id == "BornGrinder")) score *= 0.5f;

            scored.Add((quest, Math.Max(0.001f, score)));
        }

        var top3 = scored.OrderByDescending(s => s.score).Take(3)
            .Select(s => $"[{s.quest.QuestId}]\"{s.quest.Title}\"({s.score:F2})");
        _logger.LogDebug("[BOT-SCORE] {Name}({Guid}) | Top3: {Top3} | Pool={Count}",
            bot.Name, bot.Guid, string.Join(", ", top3), scored.Count);

        return scored;
    }

    // ════════════════════════════════════════════════════════════════════
    // Cross-Quest Objective Targeting
    // ════════════════════════════════════════════════════════════════════

    private (float x, float y, float z, int map, int creatureEntry, int killCount, int goEntry)?
        GetNearestObjectiveAcrossQuests(
            BotIdentity bot, BotStateSnapshot state, List<ActiveQuestEntry> activeQuests)
    {
        float bestDist = float.MaxValue;
        (float x, float y, float z, int map, int creatureEntry, int killCount, int goEntry)? best = null;

        foreach (var aq in activeQuests.Where(q => q.Accepted && !q.ServerComplete && !q.TurnedIn))
        {
            foreach (var obj in aq.Node.Objectives)
            {
                if (!obj.IsCreature || obj.GrindRadius <= 0) continue;
                aq.KillProgress.TryGetValue(obj.Slot, out int progress);
                if (progress >= obj.Count) continue;

                float dist = Distance2D(state.X, state.Y, obj.GrindX, obj.GrindY);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = (obj.GrindX, obj.GrindY, obj.GrindZ, obj.GrindMap,
                            obj.CreatureEntry, obj.Count - progress, 0);
                }
            }

            foreach (var itemObj in aq.Node.ItemObjectives)
            {
                int held = aq.ItemProgress.GetValueOrDefault(itemObj.ItemId, 0);
                if (held >= itemObj.Count) continue;

                // Check creature drop source first
                var source = itemObj.BestDropSource;
                if (source != null && source.SpawnCount > 0)
                {
                    float dist = Distance2D(state.X, state.Y, source.GrindX, source.GrindY);
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        best = (source.GrindX, source.GrindY, source.GrindZ, source.GrindMap,
                                source.CreatureEntry, 0, 0);
                    }
                    continue;
                }

                // Check game object source (barrels, chests, etc.)
                var goSource = itemObj.BestGoSource;
                if (goSource != null && goSource.SpawnCount > 0)
                {
                    float dist = Distance2D(state.X, state.Y, goSource.X, goSource.Y);
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        best = (goSource.X, goSource.Y, goSource.Z, goSource.Map,
                                0, 0, goSource.GoEntry);
                    }
                }
            }
        }

        return best;
    }

    private (float x, float y, float z, int map, int questId)?
        GetNearestCompletedTurnIn(
            BotIdentity bot, BotStateSnapshot state, List<ActiveQuestEntry> activeQuests)
    {
        float bestDist = float.MaxValue;
        (float x, float y, float z, int map, int questId)? best = null;

        foreach (var aq in activeQuests.Where(q => q.Accepted && !q.TurnedIn
                                                   && (q.ServerComplete || AllObjectivesComplete(q))
                                                   && q.Node.TurnIn != null))
        {
            float dist = Distance2D(state.X, state.Y, aq.Node.TurnIn!.X, aq.Node.TurnIn!.Y);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = (aq.Node.TurnIn!.X, aq.Node.TurnIn!.Y, aq.Node.TurnIn!.Z,
                        aq.Node.TurnIn!.Map, aq.QuestId);
            }
        }

        return best;
    }

    // ════════════════════════════════════════════════════════════════════
    // Completion Checks
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Session 33: Public accessor for BotBrainService to check group objective completion.
    /// </summary>
    public bool AllObjectivesCompletePublic(ActiveQuestEntry aq) => AllObjectivesComplete(aq);

    private bool AllObjectivesComplete(ActiveQuestEntry aq)
    {
        foreach (var obj in aq.Node.Objectives)
        {
            if (!obj.IsCreature) continue;
            aq.KillProgress.TryGetValue(obj.Slot, out int progress);
            if (progress < obj.Count) return false;
        }

        foreach (var itemObj in aq.Node.ItemObjectives)
        {
            int held = aq.ItemProgress.GetValueOrDefault(itemObj.ItemId, 0);
            if (held < itemObj.Count) return false;
        }

        return true;
    }

    private void UpdateQuestProgress(ActiveQuestEntry aq)
    {
        int totalNeeded = 0, totalDone = 0;

        foreach (var obj in aq.Node.Objectives)
        {
            if (!obj.IsCreature) continue;
            totalNeeded += obj.Count;
            aq.KillProgress.TryGetValue(obj.Slot, out int progress);
            totalDone += Math.Min(progress, obj.Count);
        }

        foreach (var itemObj in aq.Node.ItemObjectives)
        {
            totalNeeded += itemObj.Count;
            int held = aq.ItemProgress.GetValueOrDefault(itemObj.ItemId, 0);
            totalDone += Math.Min(held, itemObj.Count);
        }

        aq.Progress = totalNeeded > 0 ? (float)totalDone / totalNeeded : 1.0f;
    }

    // ════════════════════════════════════════════════════════════════════
    // Round-Trip Distance
    // ════════════════════════════════════════════════════════════════════

    private float EstimateRoundTripDistance(float botX, float botY, QuestNode quest)
    {
        float total = 0f;
        float curX = botX, curY = botY;

        if (quest.Giver != null)
        {
            total += Distance2D(curX, curY, quest.Giver.X, quest.Giver.Y);
            curX = quest.Giver.X; curY = quest.Giver.Y;
        }

        var firstObj = quest.Objectives?.FirstOrDefault(o => o.IsCreature && o.GrindRadius > 0);
        if (firstObj != null)
        {
            total += Distance2D(curX, curY, firstObj.GrindX, firstObj.GrindY);
            curX = firstObj.GrindX; curY = firstObj.GrindY;
        }
        else
        {
            var firstItem = quest.ItemObjectives?.FirstOrDefault(i => i.BestDropSource?.SpawnCount > 0);
            if (firstItem?.BestDropSource != null)
            {
                total += Distance2D(curX, curY, firstItem.BestDropSource.GrindX, firstItem.BestDropSource.GrindY);
                curX = firstItem.BestDropSource.GrindX; curY = firstItem.BestDropSource.GrindY;
            }
            else
            {
                var goItem = quest.ItemObjectives?.FirstOrDefault(i => i.BestGoSource?.SpawnCount > 0);
                if (goItem?.BestGoSource != null)
                {
                    total += Distance2D(curX, curY, goItem.BestGoSource.X, goItem.BestGoSource.Y);
                    curX = goItem.BestGoSource.X; curY = goItem.BestGoSource.Y;
                }
            }
        }

        if (quest.TurnIn != null)
            total += Distance2D(curX, curY, quest.TurnIn.X, quest.TurnIn.Y);

        return total;
    }

    // ════════════════════════════════════════════════════════════════════
    // Navigation Helpers
    // ════════════════════════════════════════════════════════════════════

    private List<BridgeCommand> SendToNearestObjective(BotIdentity bot, BotStateSnapshot state)
    {
        var activeQuests = GetActiveQuests(bot);
        var target = GetNearestObjectiveAcrossQuests(bot, state, activeQuests);
        if (target == null) return SendToNearestTurnIn(bot, state);
        return new List<BridgeCommand> { MakeMoveTo(target.Value.x, target.Value.y, target.Value.z, target.Value.map)
            .WithCorr(bot.Story?.Intent(StoryVerb.TRAVEL, new StoryTarget(target.Value.x, target.Value.y, target.Value.z, target.Value.map))) };
    }

    private List<BridgeCommand> SendToNearestTurnIn(BotIdentity bot, BotStateSnapshot state)
    {
        var activeQuests = GetActiveQuests(bot);
        var turnIn = GetNearestCompletedTurnIn(bot, state, activeQuests);
        if (turnIn == null) return new List<BridgeCommand>();
        return new List<BridgeCommand> { MakeMoveTo(turnIn.Value.x, turnIn.Value.y, turnIn.Value.z, turnIn.Value.map,
            fromX: state.X, fromY: state.Y)
            .WithCorr(bot.Story?.Intent(StoryVerb.TRAVEL, new StoryTarget(turnIn.Value.x, turnIn.Value.y, turnIn.Value.z, turnIn.Value.map))) };
    }

    private List<BridgeCommand> SendGrindTask(BotIdentity bot,
        (float x, float y, float z, int map, int creatureEntry, int killCount, int goEntry) target)
    {
        SetPhaseInt(bot, "current_grind_creature", target.creatureEntry);
        SetPhaseInt(bot, "current_go_entry", 0);
        // Session 36: arming a grind starts the no-progress clock fresh.
        SetPhaseDateTime(bot, "do_progress_ts", DateTime.UtcNow);
        SetPhaseInt(bot, "do_noprogress_rearms", 0);

        // ── Session 31: Spawn Fan-Out ──
        // Instead of sending the centroid to ALL bots, pick a deterministic
        // individual spawn point for THIS bot using its GUID as seed.
        // The grind radius still covers the cluster, so the bot finds targets
        // across the whole area, but its anchor point is offset from other bots.
        // This prevents 28 bots converging on one coordinate and chain-pulling.
        float grindX = target.x, grindY = target.y, grindZ = target.z;

        var spawnPositions = GetSpawnPositionsForTarget(bot, target);
        if (spawnPositions != null && spawnPositions.Count > 1)
        {
            // Session 42: grouped bots seed fan-out with the LEADER's guid so the
            // whole group shares ONE spawn anchor (same camp, same mobs, heals and
            // kill credit in range). Different groups still land on different spawns;
            // solo bots keep the original per-guid scatter.
            int fanoutSeed = bot.IsGrouped && bot.GroupLeaderGuid.HasValue
                ? bot.GroupLeaderGuid.Value
                : bot.Guid;
            int idx = Math.Abs(fanoutSeed.GetHashCode() ^ target.creatureEntry) % spawnPositions.Count;
            var mySpawn = spawnPositions[idx];
            grindX = mySpawn.X;
            grindY = mySpawn.Y;
            grindZ = mySpawn.Z;

            _logger.LogDebug(
                "[BOT-FANOUT] {Name}({Guid}) | Grind center: spawn {Idx}/{Total} " +
                "at ({X:F0},{Y:F0}) instead of centroid ({CX:F0},{CY:F0})",
                bot.Name, bot.Guid, idx, spawnPositions.Count,
                grindX, grindY, target.x, target.y);
        }

        return new List<BridgeCommand>
        {
            new BridgeCommand("SET_TASK", new
            {
                task = "GRIND",
                x = grindX, y = grindY, z = grindZ,
                radius = 60.0f,
                creature_entry = target.creatureEntry,
                kill_count = target.killCount
            }).WithCorr(bot.Story?.Intent(StoryVerb.GRIND, new StoryTarget(grindX, grindY, grindZ, target.map)))
        };
    }

    /// <summary>
    /// Session 31: Look up individual spawn positions for a grind target.
    /// Checks QuestObjective.SpawnPositions (kill objectives) and
    /// ItemDropSource.SpawnPositions (item-drop objectives).
    /// Returns null if no per-spawn data is available (falls back to centroid).
    /// </summary>
    private List<(float X, float Y, float Z)>? GetSpawnPositionsForTarget(
        BotIdentity bot,
        (float x, float y, float z, int map, int creatureEntry, int killCount, int goEntry) target)
    {
        if (target.creatureEntry <= 0) return null;

        var activeQuests = GetActiveQuests(bot);

        foreach (var aq in activeQuests.Where(q => q.Accepted && !q.ServerComplete))
        {
            // Check kill objectives
            foreach (var obj in aq.Node.Objectives)
            {
                if (obj.IsCreature && obj.CreatureEntry == target.creatureEntry
                    && obj.SpawnPositions.Count > 1)
                {
                    return obj.SpawnPositions;
                }
            }

            // Check item-drop objectives
            foreach (var itemObj in aq.Node.ItemObjectives)
            {
                var source = itemObj.BestDropSource;
                if (source != null && source.CreatureEntry == target.creatureEntry
                    && source.SpawnPositions.Count > 1)
                {
                    return source.SpawnPositions;
                }
            }
        }

        return null;
    }

    private List<BridgeCommand> SendGoInteractTask(BotIdentity bot, BotStateSnapshot state,
        (float x, float y, float z, int map, int creatureEntry, int killCount, int goEntry) target)
    {
        SetPhaseInt(bot, "current_grind_creature", 0);
        SetPhaseInt(bot, "current_go_entry", target.goEntry);
        SetPhaseInt(bot, "go_spawn_pin", 0);
        _logger.LogInformation("[BOT-QUEST] {Name}({Guid}) | GO interaction mode — goEntry={GoEntry}",
            bot.Name, bot.Guid, target.goEntry);
        // Session 41: if we're already inside the cluster, do NOT MOVE_TO the
        // center — with Session-39 instant acks that round-trip immediately
        // re-entered this path (the GO MOVE_TO loop). The next DoingObjectives
        // tick (or the TASK_COMPLETE GO-mode route) sends USE_GAMEOBJECT.
        if (IsNear(state, target.x, target.y, target.map, 8f))
            return new List<BridgeCommand>();
        // First command: move to the GO cluster center, USE_GAMEOBJECT will be sent from DoingObjectives
        return new List<BridgeCommand>
        {
            MakeMoveTo(target.x, target.y, target.z, target.map)
                .WithCorr(bot.Story?.Intent(StoryVerb.TRAVEL, new StoryTarget(target.x, target.y, target.z, target.map)))
        };
    }

    // ════════════════════════════════════════════════════════════════════
    // Stuck Detection
    // ════════════════════════════════════════════════════════════════════

    private void CheckStuckDetection(BotIdentity bot, BotStateSnapshot state)
    {
        float lastX = GetPhaseFloat(bot, "stuck_x");
        float lastY = GetPhaseFloat(bot, "stuck_y");
        DateTime lastCheck = GetPhaseDateTime(bot, "stuck_check");

        if (lastCheck == default) { ResetStuckDetection(bot, state); return; }

        float elapsed = (float)(DateTime.UtcNow - lastCheck).TotalSeconds;
        if (elapsed < 30f) return;

        float moved = Distance2D(state.X, state.Y, lastX, lastY);

        if (moved < 2f)
        {
            int retries = GetPhaseInt(bot, "stuck_retries");
            retries++;
            SetPhaseInt(bot, "stuck_retries", retries);

            _logger.LogWarning("[BOT-STUCK] {Name}({Guid}) | Retry {Retries}/3 | Moved {Moved:F1}yd in 30s",
                bot.Name, bot.Guid, retries, moved);

            if (retries >= 3)
            {
                bot.Story?.Wedge("stuck_3x", $"no move 30s x3 @({state.X:F0},{state.Y:F0})");
                var activeQuests = GetActiveQuests(bot);
                foreach (var aq in activeQuests)
                {
                    bot.DeferQuest(aq.QuestId, TimeSpan.FromMinutes(20));
                    bot.Story?.Emit(StoryVerb.DEFER, StoryResult.SKIP, quest: new StoryQuest(aq.QuestId, aq.Title), reason: "stuck_3x", detail: "20min");
                    _logger.LogWarning("QuestingDomain: {Name} stuck 3 times on [{QuestId}], deferring",
                        bot.Name, aq.QuestId);

                    _diagnostics.RecordIssue(DiagnosticIssueType.StuckDetected, bot,
                        state.X, state.Y, state.MapId,
                        questId: aq.QuestId, questTitle: aq.Title,
                        detail: $"stuck 3x at ({state.X:F0},{state.Y:F0}), deferred 20min");
                }
                FallbackToPickingQuests(bot);
            }
            else
            {
                ResetStuckDetection(bot, state);
            }
        }
        else
        {
            ResetStuckDetection(bot, state);
        }
    }

    private void ResetStuckDetection(BotIdentity bot, BotStateSnapshot state)
    {
        SetPhaseFloat(bot, "stuck_x", state.X);
        SetPhaseFloat(bot, "stuck_y", state.Y);
        bot.CurrentActivity.PhaseData["stuck_check"] = DateTime.UtcNow;
        SetPhaseInt(bot, "stuck_retries", 0);
    }

    // ════════════════════════════════════════════════════════════════════
    // Helpers
    // ════════════════════════════════════════════════════════════════════

    private bool HasAvailableQuests(BotIdentity bot, int mapId)
    {
        int raceBit = QuestGraphLoader.RaceToBitmask(bot.Race);
        int classBit = QuestGraphLoader.ClassToBitmask(bot.ClassId);
        var activeIds = new HashSet<int>(GetActiveQuests(bot).Select(q => q.QuestId));
        if (bot.ActiveQuestId.HasValue)
            activeIds.Add(bot.ActiveQuestId.Value);
        var available = _questGraph.GetAvailableQuests(raceBit, classBit, bot.Level,
            bot.CompletedQuestIds, activeIds);
        return available.Any(q => q.Giver != null && q.Giver.Map == mapId
                                  && !bot.DeferredQuestIds.ContainsKey(q.QuestId));
    }

    private static BridgeCommand MakeMoveTo(float x, float y, float z, int mapId = 0,
        float? fromX = null, float? fromY = null)
    {
        var (jx, jy) = (fromX.HasValue && fromY.HasValue)
            ? WeightedRoller.JitterToward(x, y, fromX.Value, fromY.Value)
            : WeightedRoller.Jitter(x, y);
        return new BridgeCommand("MOVE_TO", new { mapId, x = jx, y = jy, z });
    }

    private List<BridgeCommand> CheckTravelNudge(BotIdentity bot, BotStateSnapshot state,
        float targetX, float targetY, float targetZ, int targetMap)
    {
        var commands = new List<BridgeCommand>();

        if (bot.CurrentActivity.TicksInState <= 1)
        {
            SetPhaseFloat(bot, "nudge_x", state.X);
            SetPhaseFloat(bot, "nudge_y", state.Y);
            return commands;
        }

        float lastX = GetPhaseFloat(bot, "nudge_x");
        float lastY = GetPhaseFloat(bot, "nudge_y");
        float moved = Distance2D(state.X, state.Y, lastX, lastY);

        SetPhaseFloat(bot, "nudge_x", state.X);
        SetPhaseFloat(bot, "nudge_y", state.Y);

        if (moved < 3f)
        {
            _logger.LogDebug("[BOT-NUDGE] {Name}({Guid}) | Re-sending MOVE_TO (moved {Moved:F1}yd)",
                bot.Name, bot.Guid, moved);
            commands.Add(MakeMoveTo(targetX, targetY, targetZ, targetMap));
        }

        return commands;
    }

    private static bool IsNear(BotStateSnapshot state, float x, float y, int map, float threshold = 15f)
    {
        if (state.MapId != map) return false;
        return Distance2D(state.X, state.Y, x, y) < threshold;
    }

    private static float Distance2D(float x1, float y1, float x2, float y2)
    {
        float dx = x1 - x2; float dy = y1 - y2;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    private void AdvanceTo(BotIdentity bot, string subPhase)
    {
        var prev = bot.CurrentActivity.SubPhase;
        bot.CurrentActivity.SubPhase = subPhase;
        _logger.LogInformation("[BOT-PHASE] {Name}({Guid}) | {Prev} → {Next}",
            bot.Name, bot.Guid, prev ?? "null", subPhase);

        // FLIGHT RECORDER: single chokepoint for every sub-phase transition (45 callers).
        // owner → CS (the bot just decided to move forward); group gates ride along via Emit.
        BotTrace.Transition(bot, prev ?? "", subPhase, "advance");
    }

    private List<BridgeCommand> FallbackToPickingQuests(BotIdentity bot)
    {
        bot.ActiveQuestId = null;
        bot.CurrentQuestProgress = 0f;
        bot.QuestObjectiveProgress.Clear();
        bot.QuestItemProgress.Clear();
        bot.CurrentActivity.PhaseData.Clear();
        bot.ResetDeathCounter();
        bot.Story?.Emit(StoryVerb.FALLBACK, StoryResult.OK, detail: "re-pick");
        AdvanceTo(bot, "PickingQuests");
        return new List<BridgeCommand>();
    }

    private static int GetPhaseInt(BotIdentity bot, string key, int defaultVal = 0)
    {
        if (bot.CurrentActivity.PhaseData.TryGetValue(key, out var obj) && obj is int v) return v;
        return defaultVal;
    }

    private static void SetPhaseInt(BotIdentity bot, string key, int value)
        => bot.CurrentActivity.PhaseData[key] = value;

    private static float GetPhaseFloat(BotIdentity bot, string key, float defaultVal = 0f)
    {
        if (bot.CurrentActivity.PhaseData.TryGetValue(key, out var obj) && obj is float v) return v;
        return defaultVal;
    }

    private static void SetPhaseFloat(BotIdentity bot, string key, float value)
        => bot.CurrentActivity.PhaseData[key] = value;

    private static DateTime GetPhaseDateTime(BotIdentity bot, string key)
    {
        if (bot.CurrentActivity.PhaseData.TryGetValue(key, out var obj) && obj is DateTime dt) return dt;
        return default;
    }

    private static void SetPhaseDateTime(BotIdentity bot, string key, DateTime value)
        => bot.CurrentActivity.PhaseData[key] = value;

    private static float Lerp(float min, float max, float t) => min + (max - min) * t;

    // ════════════════════════════════════════════════════════════════════
    // Session 33: Frustration-based quest abandonment
    // ════════════════════════════════════════════════════════════════════

    private const int FRUSTRATION_ABANDON_THRESHOLD = 3; // deferrals before considering abandon

    /// <summary>
    /// Check if a quest should be abandoned after repeated failures.
    /// Abandons ONLY if: deferred 3+ times AND not part of a chain AND no item rewards.
    /// Chain quests and quests with gear rewards are always kept — they're worth the pain.
    /// Returns a ABANDON_QUEST BridgeCommand if abandoned, null otherwise.
    /// </summary>
    private BridgeCommand? TryFrustrationAbandon(BotIdentity bot, ActiveQuestEntry aq)
    {
        bot.QuestDeferralCounts.TryGetValue(aq.QuestId, out int count);
        if (count < FRUSTRATION_ABANDON_THRESHOLD)
            return null;

        // Never abandon chain quests — they unlock future content
        if (aq.Node.IsPartOfChain)
        {
            _logger.LogDebug(
                "[BOT-QUEST] {Name}({Guid}) | [{QuestId}] \"{Title}\" deferred {Count}x but is part of a chain — keeping",
                bot.Name, bot.Guid, aq.QuestId, aq.Title, count);
            return null;
        }

        // Never abandon quests with item rewards — gear is worth the struggle
        if (aq.Node.HasItemReward)
        {
            _logger.LogDebug(
                "[BOT-QUEST] {Name}({Guid}) | [{QuestId}] \"{Title}\" deferred {Count}x but has item rewards — keeping",
                bot.Name, bot.Guid, aq.QuestId, aq.Title, count);
            return null;
        }

        // This quest is a dead-end time sink — abandon it
        _logger.LogWarning(
            "[BOT-QUEST] {Name}({Guid}) | ABANDONING [{QuestId}] \"{Title}\" — " +
            "deferred {Count}x, not part of chain, no item rewards. Freeing quest log slot.",
            bot.Name, bot.Guid, aq.QuestId, aq.Title, count);

        bot.QuestDeferralCounts.Remove(aq.QuestId);
        bot.DeferredQuestIds.Remove(aq.QuestId);

        _diagnostics.RecordIssue(DiagnosticIssueType.QuestDeferred, bot,
            0, 0, 0,
            questId: aq.QuestId, questTitle: aq.Title,
            detail: $"frustration_abandoned after {count} deferrals (no chain, no item reward)");

        bot.Story?.Emit(StoryVerb.ABANDON, StoryResult.OK, quest: new StoryQuest(aq.QuestId, aq.Title), reason: "frustration", detail: $"after {count} deferrals");
        return new BridgeCommand("ABANDON_QUEST", new { quest_id = aq.QuestId });
    }
}