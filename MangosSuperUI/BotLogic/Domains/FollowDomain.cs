using MangosSuperUI.BotLogic.Core;
using MangosSuperUI.BotLogic.Data;
using MangosSuperUI.BotLogic.Tracking;

namespace MangosSuperUI.BotLogic.Domains;

/// <summary>
/// Session 43: the FORMATION executor (ARCH §7a, formation model).
///
/// Grouped followers stop being independent quest-pathing agents entirely.
/// While the Following activity holds, C++ keeps the bot glued to the leader
/// via MoveFollow (the pet motion generator) — combat interrupts it, the C++
/// UpdateAI re-arm restores it. This domain's job on top of that glue is
/// deliberately dumb:
///
///   1. Keep the FOLLOW armed (re-send periodically; re-send on FOLLOW_FAIL).
///   2. Opportunistic quest interact: the follower is always standing where
///      the leader is, so whenever a leader-batch quest's giver or an active
///      quest's turn-in NPC is within reach, just TRY the QUEST_INTERACT.
///      The SERVER is the authority (CanTakeQuest / CanRewardQuest intact) —
///      shared group kill credit means the server often knows a quest is
///      complete before our own counters do, and trying-when-near is exactly
///      how we discover it. No C# bookkeeping mirror needed.
///   3. Server-truth quest view: periodic QUERY_QUEST_STATUS keeps a local
///      {questId → status} map (COMPLETE=1 / INCOMPLETE=3, gotcha 59) so the
///      turn-in pass knows what's ready. PhaseData-resident — rebuilt on every
///      re-entry, same pattern as QuestingDomain (gotcha 64).
///
/// Individual hard needs (bags full, low HP, overdue training) still pull the
/// bot out via the DecisionEngine enforcement skip + the escape weights below;
/// the errand finishes, the directive snaps it back to Following, MoveFollow
/// chases the leader down wherever it went.
/// </summary>
public class FollowDomain : IBotDomain
{
    private readonly QuestGraphLoader _questGraph;
    private readonly ILogger _logger;

    private const float INTERACT_RANGE_YD = 12f;     // C++ questgiver search is 15 yd
    private const double INTERACT_GATE_SEC = 10.0;   // max one interact attempt per gate
    private const double SYNC_GATE_SEC = 60.0;       // QUERY_QUEST_STATUS cadence
    private const double FOLLOW_RESEND_SEC = 120.0;  // idempotent FOLLOW refresh

    public FollowDomain(QuestGraphLoader questGraph, ILogger logger)
    {
        _questGraph = questGraph;
        _logger = logger;
    }

    public ActivityType[] OwnedActivities => new[] { ActivityType.Following };

    public bool IsOperational => true;

    public Dictionary<ActivityType, float> EvaluateTransitions(BotIdentity bot, BotStateSnapshot state)
    {
        var weights = new Dictionary<ActivityType, float>();

        if (state.InCombat)
        {
            weights[ActivityType.Following] = 1.0f;
            return weights;
        }

        // Disbanded / leaderless / stale directive → escape to normal life.
        // (The DecisionEngine enforcement won't fire for an ungrouped bot, so
        // these weights are what actually gets rolled.)
        if (!bot.IsGrouped || !bot.GroupLeaderGuid.HasValue || bot.IsGroupLeader)
        {
            weights[ActivityType.Following] = 0f;
            weights[ActivityType.Questing] = 5.0f;
            weights[ActivityType.Grinding] = 1.0f;
            return weights;
        }

        // Healthy follower: stay. Individual-need escapes mirror CombatDomain's
        // ladder so the roll can route a real errand when enforcement steps aside.
        weights[ActivityType.Following] = 5.0f;

        if (state.HealthPercent < CombatDomain.GetEatThreshold(bot))
            weights[ActivityType.Eating] = 1.5f;

        uint usedSlots = state.TotalSlots - state.FreeSlots;
        if (usedSlots <= 2)
            weights[ActivityType.Vendoring] = 0f;
        else if (state.FreeSlots == 0)
            weights[ActivityType.Vendoring] = 12.0f;
        else if (state.FreeSlots <= 3)
            weights[ActivityType.Vendoring] = 7.0f;
        else
            weights[ActivityType.Vendoring] = 0.1f;

        return weights;
    }

    public List<BridgeCommand> OnEnter(BotIdentity bot, BotStateSnapshot state)
    {
        bot.CurrentActivity.IsInterruptible = !state.InCombat;
        var commands = new List<BridgeCommand>();

        if (!bot.IsGrouped || !bot.GroupLeaderGuid.HasValue || bot.IsGroupLeader)
        {
            // Shouldn't be here — next eval escapes via EvaluateTransitions.
            AdvanceTo(bot, "NotFollowing", "no_leader", state);
            return commands;
        }

        AdvanceTo(bot, "Following", "enter", state);
        bot.CurrentActivity.ContextTag = $"follow:leader:{bot.GroupLeaderGuid.Value}";

        commands.Add(BuildFollow(bot));
        bot.CurrentActivity.PhaseData["fq_follow_sent"] = DateTime.UtcNow;

        // Server-truth quest view: ask for the quest log now; OnEvent parses it.
        commands.Add(new BridgeCommand("QUERY_QUEST_STATUS", new { }));
        bot.CurrentActivity.PhaseData["fq_last_sync"] = DateTime.UtcNow;

        return commands;
    }

    public List<BridgeCommand> OnTick(BotIdentity bot, BotStateSnapshot state)
    {
        bot.CurrentActivity.IsInterruptible = !state.InCombat;
        var commands = new List<BridgeCommand>();
        var pd = bot.CurrentActivity.PhaseData;

        if (!bot.IsGrouped || !bot.GroupLeaderGuid.HasValue || bot.IsGroupLeader)
            return commands;   // eval escapes next tick

        // ── Liveness for the flight recorder ──
        // Following has no completion event to WAIT on — it's an indefinite C++
        // hand-off like the grind (owner CPP). Ping when the bot is doing its
        // job: moving, or standing in formation near the anchor (= leader's
        // live position, stamped fresh by the coordinator every pass). A bot
        // far from the leader AND not moving is the genuinely-stuck case the
        // sweep should still catch.
        BotTrace.Wait(bot, WaitOn.Cpp("follow"), "following", state);
        float lx = PhaseFloat(pd, "fq_last_x", float.MinValue);
        float ly = PhaseFloat(pd, "fq_last_y", float.MinValue);
        float movedDx = state.X - lx, movedDy = state.Y - ly;
        bool moved = lx != float.MinValue
            && MathF.Sqrt(movedDx * movedDx + movedDy * movedDy) > 2f;
        pd["fq_last_x"] = state.X;
        pd["fq_last_y"] = state.Y;

        float adx = state.X - bot.GroupAnchorX, ady = state.Y - bot.GroupAnchorY;
        float anchorDist = MathF.Sqrt(adx * adx + ady * ady);
        bool directiveFresh = (DateTime.UtcNow - bot.GroupDirectiveUtc).TotalSeconds < 120;
        if (moved || (directiveFresh && bot.GroupAnchorMap == state.MapId && anchorDist <= 25f))
            BotTrace.Ping(bot);

        // ── Keep the FOLLOW armed ──
        var followSent = PhaseDate(pd, "fq_follow_sent");
        if (followSent == default || (DateTime.UtcNow - followSent).TotalSeconds >= FOLLOW_RESEND_SEC)
        {
            commands.Add(BuildFollow(bot));
            pd["fq_follow_sent"] = DateTime.UtcNow;
        }

        // ── Server-truth re-sync (also how shared-credit completions surface) ──
        var lastSync = PhaseDate(pd, "fq_last_sync");
        if (lastSync == default || (DateTime.UtcNow - lastSync).TotalSeconds >= SYNC_GATE_SEC)
        {
            commands.Add(new BridgeCommand("QUERY_QUEST_STATUS", new { }));
            pd["fq_last_sync"] = DateTime.UtcNow;
        }

        // ── Opportunistic quest interact (one attempt per gate, server validates) ──
        if (state.InCombat)
            return commands;
        var lastInteract = PhaseDate(pd, "fq_last_interact");
        if (lastInteract != default && (DateTime.UtcNow - lastInteract).TotalSeconds < INTERACT_GATE_SEC)
            return commands;

        var fq = GetFq(pd);
        var failed = GetFailed(pd);

        // Turn-in pass first (frees log slots, banks XP): any active quest the
        // SERVER says is complete whose turn-in NPC is in reach.
        foreach (var kv in fq)
        {
            if (kv.Value != 1) continue;   // QuestStatus COMPLETE = 1 (gotcha 59)
            var node = _questGraph.GetQuest(kv.Key);
            if (node?.TurnIn == null || node.TurnIn.Map != state.MapId) continue;
            if (Distance2D(state.X, state.Y, node.TurnIn.X, node.TurnIn.Y) > INTERACT_RANGE_YD) continue;

            commands.Add(new BridgeCommand("QUEST_INTERACT", new
            {
                action = "complete",
                quest_id = kv.Key,
                npc_entry = node.TurnIn.NpcEntry
            }));
            pd["fq_last_interact"] = DateTime.UtcNow;
            _logger.LogInformation(
                "[BOT-FOLLOW] {Name}({Guid}) | opportunistic TURN-IN [{QuestId}] \"{Title}\" at npc {Npc}",
                bot.Name, bot.Guid, kv.Key, node.Title, node.TurnIn.NpcEntry);
            return commands;
        }

        // Accept pass: any leader-batch quest we don't hold, whose giver is in
        // reach. Race/class/prereq gates are the server's job — a FAIL just
        // marks the quest skipped for this Following stint.
        if (bot.GroupLeaderQuestIds is { Count: > 0 } && directiveFresh)
        {
            foreach (var qid in bot.GroupLeaderQuestIds)
            {
                if (fq.ContainsKey(qid) || failed.Contains(qid)) continue;
                if (bot.CompletedQuestIds.Contains(qid)) continue;
                var node = _questGraph.GetQuest(qid);
                if (node?.Giver == null || node.Giver.Map != state.MapId) continue;
                if (Distance2D(state.X, state.Y, node.Giver.X, node.Giver.Y) > INTERACT_RANGE_YD) continue;

                commands.Add(new BridgeCommand("QUEST_INTERACT", new
                {
                    action = "accept",
                    quest_id = qid,
                    npc_entry = node.Giver.NpcEntry
                }));
                pd["fq_last_interact"] = DateTime.UtcNow;
                _logger.LogInformation(
                    "[BOT-FOLLOW] {Name}({Guid}) | opportunistic ACCEPT [{QuestId}] \"{Title}\" at npc {Npc}",
                    bot.Name, bot.Guid, qid, node.Title, node.Giver.NpcEntry);
                return commands;
            }
        }

        return commands;
    }

    public List<BridgeCommand> OnEvent(BotIdentity bot, BotStateSnapshot state, BotEvent evt)
    {
        var commands = new List<BridgeCommand>();
        var pd = bot.CurrentActivity.PhaseData;

        switch (evt.EventType)
        {
            case "KILL":
                // Server handles group kill credit; the periodic sync pulls the
                // resulting quest status. Just prove liveness.
                BotTrace.Ping(bot);
                break;

            case "QUEST_STATUS_ALL":
                {
                    // Format per BridgeHandleQueryQuestStatus:
                    //   questId:status:mob1,mob2,mob3,mob4:item1,item2,item3,item4 | ...
                    var fq = new Dictionary<int, int>();
                    if (!string.IsNullOrEmpty(evt.Data))
                    {
                        foreach (var entry in evt.Data.Split('|', StringSplitOptions.RemoveEmptyEntries))
                        {
                            var parts = entry.Split(':');
                            if (parts.Length < 2) continue;
                            if (!int.TryParse(parts[0], out int qid)) continue;
                            if (!int.TryParse(parts[1], out int status)) continue;
                            fq[qid] = status;
                        }
                    }
                    pd["fq_status"] = fq;
                    BotTrace.Ping(bot);
                    break;
                }

            case "QUEST_ACCEPT_ACK":
                {
                    if (int.TryParse(evt.Data ?? "", out int ackId))
                    {
                        GetFq(pd)[ackId] = 3;   // INCOMPLETE until the server says otherwise
                        _logger.LogInformation(
                            "[BOT-FOLLOW] {Name}({Guid}) | ACK: quest {AckId} accepted (formation)",
                            bot.Name, bot.Guid, ackId);
                    }
                    BotTrace.Ping(bot);
                    break;
                }

            case "QUEST_COMPLETE_ACK":
                {
                    if (int.TryParse(evt.Data ?? "", out int ackId))
                    {
                        GetFq(pd).Remove(ackId);
                        bot.CompletedQuestIds.Add(ackId);

                        // Session 44: rewarding ANY quest is chain progress — prereq-locked
                        // skips (quest 7 failing PrevQ before 783 was rewarded) are now
                        // stale. Clear the stint skip set so the next opportunistic pass
                        // retries them; truly-locked quests (class/race) just re-fail once.
                        GetFailed(pd).Clear();

                        _logger.LogInformation(
                            "[BOT-FOLLOW] {Name}({Guid}) | ACK: quest {AckId} rewarded (formation)",
                            bot.Name, bot.Guid, ackId);
                    }
                    BotTrace.Ping(bot);
                    break;
                }

            case "QUEST_INTERACT_FAIL":
                {
                    // Data: "reason|quest_id=N|npc_entry=M".
                    // Session 44: npc_not_found is TRANSIENT — the bot was simply out of
                    // the C++ 15 yd search (glued 6 yd behind a leader who stands ~10 yd
                    // from the NPC ≈ 16 yd; or mid-combat lag). Stint-banning it was a
                    // root cause of the June-12 bricked followers: one out-of-range try
                    // permanently skipped the chain head. Only hard server rejections
                    // (CanTakeQuest: class/race/prereq, log full, …) enter the skip set,
                    // and chain progress clears it (see QUEST_COMPLETE_ACK).
                    int qid = ParseKeyedInt(evt.Data, "quest_id");
                    string failReason = (evt.Data ?? "").Split('|')[0].Trim();
                    if (qid > 0 && failReason != "npc_not_found")
                    {
                        GetFailed(pd).Add(qid);
                        _logger.LogDebug(
                            "[BOT-FOLLOW] {Name}({Guid}) | interact FAIL for quest {QuestId} ({Data}) — skipping this stint",
                            bot.Name, bot.Guid, qid, evt.Data ?? "");
                    }
                    else if (qid > 0)
                    {
                        _logger.LogDebug(
                            "[BOT-FOLLOW] {Name}({Guid}) | transient interact fail for quest {QuestId} ({Data}) — will retry",
                            bot.Name, bot.Guid, qid, evt.Data ?? "");
                    }
                    break;
                }

            case "FOLLOW_FAIL":
                // Lost the leader (cross-map, despawn race). Clear the resend
                // stamp so OnTick re-arms immediately once it's resolvable.
                pd.Remove("fq_follow_sent");
                _logger.LogWarning(
                    "[BOT-FOLLOW] {Name}({Guid}) | FOLLOW_FAIL ({Data}) — will re-arm",
                    bot.Name, bot.Guid, evt.Data ?? "");
                break;

            case "MOVE_FAILED":
            case "PATH_UNSAFE":
                // A stray movement failure while following — let the C++ re-arm
                // and the next FOLLOW resend recover; nothing to wedge on here.
                pd.Remove("fq_follow_sent");
                break;
        }

        return commands;
    }

    // ──────────────────────── helpers ────────────────────────

    private static BridgeCommand BuildFollow(BotIdentity bot)
    {
        // 6 yd at a per-bot angle — the C++ side spreads members so they don't
        // stack on one coordinate (same intent as MoveFollow pet offsets).
        return new BridgeCommand("FOLLOW", new
        {
            target_guid = bot.GroupLeaderGuid!.Value,
            dist = 6
        });
    }

    private void AdvanceTo(BotIdentity bot, string phase, string reason, BotStateSnapshot state)
    {
        var from = bot.CurrentActivity.SubPhase ?? "";
        bot.CurrentActivity.SubPhase = phase;
        BotTrace.Transition(bot, from, phase, reason, detail: "", state: state);
        _logger.LogDebug("[BOT-PHASE] {Name}({Guid}) | Following: {From} → {To} ({Reason})",
            bot.Name, bot.Guid, from, phase, reason);
    }

    private static Dictionary<int, int> GetFq(Dictionary<string, object> pd)
    {
        if (pd.TryGetValue("fq_status", out var v) && v is Dictionary<int, int> d)
            return d;
        var fresh = new Dictionary<int, int>();
        pd["fq_status"] = fresh;
        return fresh;
    }

    private static HashSet<int> GetFailed(Dictionary<string, object> pd)
    {
        if (pd.TryGetValue("fq_failed", out var v) && v is HashSet<int> s)
            return s;
        var fresh = new HashSet<int>();
        pd["fq_failed"] = fresh;
        return fresh;
    }

    private static DateTime PhaseDate(Dictionary<string, object> pd, string key)
        => pd.TryGetValue(key, out var v) && v is DateTime t ? t : default;

    private static float PhaseFloat(Dictionary<string, object> pd, string key, float fallback)
        => pd.TryGetValue(key, out var v) && v is float f ? f : fallback;

    private static int ParseKeyedInt(string? data, string key)
    {
        if (string.IsNullOrEmpty(data)) return 0;
        foreach (var part in data.Split('|'))
        {
            var kv = part.Split('=', 2);
            if (kv.Length == 2 && kv[0].Trim() == key && int.TryParse(kv[1].Trim(), out int val))
                return val;
        }
        return 0;
    }

    private static float Distance2D(float x1, float y1, float x2, float y2)
    {
        float dx = x1 - x2, dy = y1 - y2;
        return MathF.Sqrt(dx * dx + dy * dy);
    }
}