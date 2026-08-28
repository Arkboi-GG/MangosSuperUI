using Microsoft.AspNetCore.SignalR;
using MangosSuperUI.Services;
using MangosSuperUI.BotLogic.Data;
using MangosSuperUI.BotLogic.Tracking;

namespace MangosSuperUI.Hubs;

/// <summary>
/// SignalR hub at /hubs/botbridge.
///
/// Server → Client events (pushed from BotBridgeService):
///   BotConnected(BotState)          — new bot joined
///   BotDisconnected(int guid)       — bot TCP dropped
///   BotStateUpdate(BotState)        — periodic state refresh
///   BotSensoryFeedStale(object)      — STATE heartbeat age crossed the fail-closed threshold
///   BotEvent(object)                — discrete event (combat, death, quest, etc.)
///   BotChatReceived(object)         — player whispered a bot
///
/// Client → Server methods:
///   GetAllBots()                    — returns current snapshot of all bot states
///   SendMoveTo(guid, mapId, x,y,z) — send MOVE_TO to a specific bot
///   SendSayText(guid, text, type)  — make a bot say/yell/whisper
///   SendMoveToAll(mapId, x,y,z)    — rally all bots to a point
/// </summary>
public class BotBridgeHub : Hub
{
    private readonly BotBridgeService _bridge;
    private readonly QuestGraphLoader _questGraph;
    private readonly ILogger<BotBridgeHub> _logger;

    public BotBridgeHub(BotBridgeService bridge, QuestGraphLoader questGraph, ILogger<BotBridgeHub> logger)
    {
        _bridge = bridge;
        _questGraph = questGraph;
        _logger = logger;
    }

    /// <summary>
    /// Client requests the full bot roster on page load.
    /// </summary>
    public async Task GetAllBots()
    {
        var bots = _bridge.GetAllBotStates();
        await Clients.Caller.SendAsync("AllBots", bots);
    }

    /// <summary>
    /// UI sends a MOVE_TO command to a specific bot.
    /// </summary>
    public async Task SendMoveTo(int guid, int mapId, float x, float y, float z)
    {
        _logger.LogInformation("BotBridgeHub: MOVE_TO bot {Guid} → map={Map} ({X},{Y},{Z})",
            guid, mapId, x, y, z);
        await _bridge.SendMoveToAsync(guid, mapId, x, y, z);
        await Clients.Caller.SendAsync("CommandAck", new { guid, command = "MOVE_TO", success = true });
    }

    /// <summary>
    /// UI makes a bot say/yell/whisper text.
    /// </summary>
    public async Task SendSayText(int guid, string text, int chatType = 0)
    {
        _logger.LogInformation("BotBridgeHub: SAY_TEXT bot {Guid} type={Type}: {Text}",
            guid, chatType, text);
        await _bridge.SendSayTextAsync(guid, text, chatType);
        await Clients.Caller.SendAsync("CommandAck", new { guid, command = "SAY_TEXT", success = true });
    }

    /// <summary>
    /// Rally all connected bots to a single point.
    /// </summary>
    public async Task SendMoveToAll(int mapId, float x, float y, float z)
    {
        _logger.LogInformation("BotBridgeHub: MOVE_TO ALL → map={Map} ({X},{Y},{Z})", mapId, x, y, z);
        await _bridge.SendToAllBotsAsync("MOVE_TO", new { mapId, x, y, z });
        await Clients.Caller.SendAsync("CommandAck", new { command = "MOVE_TO_ALL", success = true });
    }

    // --- Phase 2.5 commands ---

    // Manual accept/complete send QUEST_INTERACT (the retired ACCEPT_QUEST/COMPLETE_QUEST verbs
    // have no C++ dispatch); the giver/turn-in npc_entry is resolved from the quest graph and an
    // unresolvable quest is refused in the ack instead of sent into the void.
    public async Task SendAcceptQuest(int guid, int questId)
    {
        _logger.LogInformation("BotBridgeHub: QUEST_INTERACT accept bot {Guid} quest={QuestId}", guid, questId);
        var npcEntry = _questGraph.ResolveInteractNpc(questId, "accept", out var error);
        if (npcEntry == null)
        {
            CircuitTrace.HitNote(guid, "hub: manual quest accept unresolvable", error ?? "");
            await Clients.Caller.SendAsync("CommandAck", new { guid, command = "QUEST_INTERACT", questId, success = false, error });
            return;
        }
        await _bridge.SendAcceptQuestAsync(guid, questId, npcEntry.Value);
        await Clients.Caller.SendAsync("CommandAck", new { guid, command = "QUEST_INTERACT", questId, success = true });
    }

    public async Task SendCompleteQuest(int guid, int questId)
    {
        _logger.LogInformation("BotBridgeHub: QUEST_INTERACT complete bot {Guid} quest={QuestId}", guid, questId);
        var npcEntry = _questGraph.ResolveInteractNpc(questId, "complete", out var error);
        if (npcEntry == null)
        {
            CircuitTrace.HitNote(guid, "hub: manual quest complete unresolvable", error ?? "");
            await Clients.Caller.SendAsync("CommandAck", new { guid, command = "QUEST_INTERACT", questId, success = false, error });
            return;
        }
        await _bridge.SendCompleteQuestAsync(guid, questId, npcEntry.Value);
        await Clients.Caller.SendAsync("CommandAck", new { guid, command = "QUEST_INTERACT", questId, success = true });
    }

    public async Task SendAbandonQuest(int guid, int questId)
    {
        _logger.LogInformation("BotBridgeHub: ABANDON_QUEST bot {Guid} quest={QuestId}", guid, questId);
        await _bridge.SendAbandonQuestAsync(guid, questId);
        await Clients.Caller.SendAsync("CommandAck", new { guid, command = "ABANDON_QUEST", questId, success = true });
    }

    public async Task SendLearnSpell(int guid, int spellId)
    {
        _logger.LogInformation("BotBridgeHub: LEARN_SPELL bot {Guid} spell={SpellId}", guid, spellId);
        await _bridge.SendLearnSpellAsync(guid, spellId);
        await Clients.Caller.SendAsync("CommandAck", new { guid, command = "LEARN_SPELL", spellId, success = true });
    }

    public async Task SendAttackTarget(int guid, int targetEntry, int targetGuid)
    {
        _logger.LogInformation("BotBridgeHub: ATTACK_TARGET bot {Guid} target={Entry}:{Target}", guid, targetEntry, targetGuid);
        ExactCreatureCommandDispatch dispatch = await _bridge.SendAttackTargetAsync(guid, targetEntry, targetGuid);
        await Clients.Caller.SendAsync("CommandAck", new
        {
            guid,
            command = "ATTACK_TARGET",
            targetEntry,
            targetGuid,
            accepted = dispatch.Sent,
            sent = dispatch.Sent,
            executionPending = dispatch.Sent,
            status = dispatch.StatusCode,
            cbt = dispatch.CorrelationId,
            error = dispatch.Sent ? null : dispatch.Detail
        });
    }

    public async Task SendInteractNpc(int guid, int npcEntry, int npcGuid)
    {
        _logger.LogInformation("BotBridgeHub: INTERACT_NPC bot {Guid} npc={Entry}:{Npc}", guid, npcEntry, npcGuid);
        ExactCreatureCommandDispatch dispatch = await _bridge.SendInteractNpcAsync(guid, npcEntry, npcGuid);
        await Clients.Caller.SendAsync("CommandAck", new
        {
            guid,
            command = "INTERACT_NPC",
            npcEntry,
            npcGuid,
            accepted = dispatch.Sent,
            sent = dispatch.Sent,
            executionPending = dispatch.Sent,
            status = dispatch.StatusCode,
            cbt = dispatch.CorrelationId,
            error = dispatch.Sent ? null : dispatch.Detail
        });
    }

    public async Task SendTakeFlight(int guid, int sourceNode, int destNode)
    {
        _logger.LogInformation("BotBridgeHub: TAKE_FLIGHT bot {Guid} {Src} → {Dst}", guid, sourceNode, destNode);
        await _bridge.SendTakeFlightAsync(guid, sourceNode, destNode);
        await Clients.Caller.SendAsync("CommandAck", new { guid, command = "TAKE_FLIGHT", sourceNode, destNode, success = true });
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("BotBridge UI client connected: {ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("BotBridge UI client disconnected: {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}
