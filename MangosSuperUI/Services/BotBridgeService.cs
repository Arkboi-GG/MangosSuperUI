using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MangosSuperUI.Hubs;
using MangosSuperUI.BotLogic.Chat.Coordinator;
using MangosSuperUI.BotLogic.Tracking;
using Microsoft.AspNetCore.SignalR;

namespace MangosSuperUI.Services;

// ======================== Wire Protocol DTOs ========================

/// <summary>
/// Envelope for all messages on the wire (both directions).
/// Each line on the TCP socket is one JSON object with "type" + "payload".
/// </summary>
public class BridgeMessage
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("payload")]
    public JsonElement Payload { get; set; }
}

// --- Inbound (C++ → C#) ---

public class BotHelloPayload
{
    [JsonPropertyName("guid")]
    public int Guid { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("race")]
    public int Race { get; set; }

    [JsonPropertyName("classId")]
    public int ClassId { get; set; }

    [JsonPropertyName("level")]
    public int Level { get; set; }

    [JsonPropertyName("mapId")]
    public int MapId { get; set; }

    [JsonPropertyName("zoneId")]
    public int ZoneId { get; set; }

    [JsonPropertyName("x")]
    public float X { get; set; }

    [JsonPropertyName("y")]
    public float Y { get; set; }

    [JsonPropertyName("z")]
    public float Z { get; set; }

    // Persistent bot identity from characters.playerbot. Old core binaries omit
    // these and deserialize to the migration-safe sentinels below.
    [JsonPropertyName("specTab")]
    public int SpecTab { get; set; } = 255;

    [JsonPropertyName("specProfile")]
    public string SpecProfile { get; set; } = "";

    [JsonPropertyName("activeRole")]
    public int ActiveRole { get; set; } = 0;

    [JsonPropertyName("talentProfileState")]
    public string TalentProfileState { get; set; } = "unchecked";

    [JsonPropertyName("rotationSource")]
    public string RotationSource { get; set; } = "legacy";

    [JsonPropertyName("rotationProfile")]
    public string RotationProfile { get; set; } = "";

    [JsonPropertyName("rotationInstructionCount")]
    public int RotationInstructionCount { get; set; }

    [JsonPropertyName("rotationCastableCount")]
    public int RotationCastableCount { get; set; }

    [JsonPropertyName("combatConfigRevision")]
    public uint CombatConfigRevision { get; set; }

}

public class BotStatePayload
{
    [JsonPropertyName("guid")]
    public int Guid { get; set; }

    [JsonPropertyName("health")]
    public int Health { get; set; }

    [JsonPropertyName("maxHealth")]
    public int MaxHealth { get; set; }

    [JsonPropertyName("mana")]
    public int Mana { get; set; }

    [JsonPropertyName("maxMana")]
    public int MaxMana { get; set; }

    [JsonPropertyName("level")]
    public int Level { get; set; }

    [JsonPropertyName("specTab")]
    public int SpecTab { get; set; } = 255;

    [JsonPropertyName("specProfile")]
    public string SpecProfile { get; set; } = "";

    [JsonPropertyName("activeRole")]
    public int ActiveRole { get; set; } = 0;

    [JsonPropertyName("talentProfileState")]
    public string TalentProfileState { get; set; } = "unchecked";

    [JsonPropertyName("rotationSource")]
    public string RotationSource { get; set; } = "legacy";

    [JsonPropertyName("rotationProfile")]
    public string RotationProfile { get; set; } = "";

    [JsonPropertyName("rotationInstructionCount")]
    public int RotationInstructionCount { get; set; }

    [JsonPropertyName("rotationCastableCount")]
    public int RotationCastableCount { get; set; }

    [JsonPropertyName("combatConfigRevision")]
    public uint CombatConfigRevision { get; set; }

    [JsonPropertyName("mapId")]
    public int MapId { get; set; }

    [JsonPropertyName("zoneId")]
    public int ZoneId { get; set; }

    [JsonPropertyName("x")]
    public float X { get; set; }

    [JsonPropertyName("y")]
    public float Y { get; set; }

    [JsonPropertyName("z")]
    public float Z { get; set; }

    [JsonPropertyName("inCombat")]
    public bool InCombat { get; set; }

    [JsonPropertyName("isDead")]
    public bool IsDead { get; set; }

    [JsonPropertyName("targetGuid")]
    public int TargetGuid { get; set; }

    [JsonPropertyName("taskState")]
    public string TaskState { get; set; } = "IDLE";

    // --- Held-task echo (Held-Objective build §4) — what C++ reports it is ACTUALLY running. ---
    // taskState (above) is a DISPLAY status that conflates DEAD/COMBAT with the task, so it is NOT
    // a clean kind. taskKind is the committed task kind from m_currentTask.type (stays GRIND/MOVE_TO
    // through combat) — this is what the reconcile matches against. These add the within-objective
    // ACTIVITY (the headway signal triage reads instead of a clock) + the target + pushed kill
    // progress. ABSENT on a pre-Session-3 binary → defaults below → the snapshot maps to
    // HeldTaskEcho.Unknown (gated on taskActivity being non-empty), so the reconcile stays a no-op
    // until the C++ writer ships. snake/camel keys match the existing style.
    [JsonPropertyName("taskKind")]
    public string TaskKind { get; set; } = "";              // IDLE/MOVE_TO/GRIND — the committed task kind (NOT the display taskState)

    [JsonPropertyName("taskActivity")]
    public string TaskActivity { get; set; } = "";          // idle/traveling/searching/engaged/recovering/blocked — "" = no echo (Unknown)

    [JsonPropertyName("taskCreature")]
    public uint TaskCreature { get; set; } = 0;             // the grind mob C++ is on (0 = nearest valid)

    [JsonPropertyName("taskDestX")]
    public float TaskDestX { get; set; } = 0;

    [JsonPropertyName("taskDestY")]
    public float TaskDestY { get; set; } = 0;

    [JsonPropertyName("taskDestZ")]
    public float TaskDestZ { get; set; } = 0;

    [JsonPropertyName("taskKills")]
    public int TaskKills { get; set; } = 0;                 // kills credited on the current task so far

    [JsonPropertyName("freeSlots")]
    public uint FreeSlots { get; set; } = 16;

    [JsonPropertyName("totalSlots")]
    public uint TotalSlots { get; set; } = 16;

    [JsonPropertyName("copper")]
    public uint Copper { get; set; } = 0;

    [JsonPropertyName("questId")]
    public uint QuestId { get; set; } = 0;

    [JsonPropertyName("questStatus")]
    public uint QuestStatus { get; set; } = 0;

    [JsonPropertyName("durability")]
    public uint Durability { get; set; } = 100;   // min equipped-slot durability % (100 = full / no damageable gear)

    // [PLAYERPARTY] 1 = this bot's group contains a REAL player (C++ FindPartyBoss on the
    // 5s STATE, 2026-07-07). Sent as 0/1 (C++ %u), converted to bool on the BotState copy.
    [JsonPropertyName("pparty")]
    public uint Pparty { get; set; } = 0;

    // [HUB-ERRAND] Distance to the party boss (2026-07-08): -1 = no boss resolved,
    // 99999 = boss on ANOTHER map (instance/boat), else 3D yards. Sent beside pparty
    // by the same FindPartyBoss walk; feeds the C# errand abort guard.
    [JsonPropertyName("ppdist")]
    public int Ppdist { get; set; } = -1;

    // [CONSCRIPTED] 1 = enlisted in a player's RTS army (client control group →
    // CMSG_SUI_ORDER conscript, 2026-08-24). The planner stands down like the
    // player-party hold, but the held objective is PRESERVED so a dismissal
    // resumes questing in place. Sent 0/1 (C++ %u).
    [JsonPropertyName("conscripted")]
    public uint Conscripted { get; set; } = 0;

    // Full quest-log snapshot, pushed on every STATE (replaces the retired QUERY_QUEST_STATUS pull).
    // Pipe-delimited, identical format to the old QUEST_STATUS_ALL payload:
    //   questId:status:mob0,mob1,mob2,mob3:item0,item1,item2,item3 | questId:...
    // status: COMPLETE=1, INCOMPLETE=3 (VMaNGOS enum). Empty string = the bot genuinely holds no quests.
    // This is C++ ground truth (me->GetQuestStatusMap()) on the 5s heartbeat, so ctx.QuestLog is now a
    // continuously-maintained mirror of the core log — never a request/reply cache that can go stale/partial.
    [JsonPropertyName("quests")]
    public string Quests { get; set; } = "";
}

public class BotEventPayload
{
    [JsonPropertyName("guid")]
    public int Guid { get; set; }

    [JsonPropertyName("event")]
    public string Event { get; set; } = "";

    [JsonPropertyName("data")]
    public string Data { get; set; } = "";

    // --- Phase 2.5 extended fields (present depending on event type) ---

    [JsonPropertyName("creature_entry")]
    public int? CreatureEntry { get; set; }

    [JsonPropertyName("creature_guid")]
    public int? CreatureGuid { get; set; }

    [JsonPropertyName("quest_id")]
    public int? QuestId { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("new_level")]
    public int? NewLevel { get; set; }

    [JsonPropertyName("sender")]
    public string? Sender { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("chat_type")]
    public string? ChatType { get; set; }

    [JsonPropertyName("channel_name")]
    public string? ChannelName { get; set; }

    // C0 (§5.1): GUID low of the chat sender when resolvable, else 0. Roster/is-bot lookup key;
    // the NAME remains the memory key (D3).
    [JsonPropertyName("sender_guid")]
    public uint? SenderGuid { get; set; }


    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    [JsonPropertyName("have")]
    public uint? Have { get; set; }

    [JsonPropertyName("need")]
    public uint? Need { get; set; }

    [JsonPropertyName("cost")]
    public uint? Cost { get; set; }
}

// --- Outbound (C# → C++) ---

public class MoveToPayload
{
    [JsonPropertyName("guid")]
    public int Guid { get; set; }

    [JsonPropertyName("mapId")]
    public int MapId { get; set; }

    [JsonPropertyName("x")]
    public float X { get; set; }

    [JsonPropertyName("y")]
    public float Y { get; set; }

    [JsonPropertyName("z")]
    public float Z { get; set; }
}

public class SayTextPayload
{
    [JsonPropertyName("guid")]
    public int Guid { get; set; }

    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    [JsonPropertyName("chatType")]
    public int ChatType { get; set; } // 0=SAY, 1=PARTY (==CHAT_MSG_PARTY 0x01, VERIFIED SharedDefines.h), 6=YELL, 7=WHISPER, 14=CHANNEL

    [JsonPropertyName("target")]
    public string? Target { get; set; } // Player name for whisper replies

    [JsonPropertyName("channel")]
    public string? Channel { get; set; } // Channel name for channel replies
}

// --- Phase 2.5 command payloads ---

public class QuestCommandPayload
{
    [JsonPropertyName("quest_id")]
    public int QuestId { get; set; }
}

public class LearnSpellPayload
{
    [JsonPropertyName("spell_id")]
    public int SpellId { get; set; }
}

public class TargetGuidPayload
{
    [JsonPropertyName("guid")]
    public int Guid { get; set; }
}

public class TakeFlightPayload
{
    [JsonPropertyName("sourceNode")]
    public int SourceNode { get; set; }

    [JsonPropertyName("destNode")]
    public int DestNode { get; set; }
}

// ======================== Live Bot State ========================

public class BotState
{
    public int Guid { get; set; }
    public string Name { get; set; } = "";
    public int Race { get; set; }
    public int ClassId { get; set; }
    public int Level { get; set; }
    public int SpecTab { get; set; } = 255;
    public string SpecProfile { get; set; } = "";
    public int ActiveRole { get; set; } = 0;
    public string TalentProfileState { get; set; } = "unchecked";
    public string RotationSource { get; set; } = "legacy";
    public string RotationProfile { get; set; } = "";
    public int RotationInstructionCount { get; set; }
    public int RotationCastableCount { get; set; }
    public uint CombatConfigRevision { get; set; }
    public int Health { get; set; }
    public int MaxHealth { get; set; }
    public int Mana { get; set; }
    public int MaxMana { get; set; }
    public int MapId { get; set; }
    public int ZoneId { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public bool InCombat { get; set; }
    public bool IsDead { get; set; }
    public int TargetGuid { get; set; }
    public string TaskState { get; set; } = "IDLE";
    // Held-task echo (Held-Objective build §4) — kind (committed task) + activity + target + kill progress.
    public string TaskKind { get; set; } = "";
    public string TaskActivity { get; set; } = "";
    public uint TaskCreature { get; set; } = 0;
    public float TaskDestX { get; set; } = 0;
    public float TaskDestY { get; set; } = 0;
    public float TaskDestZ { get; set; } = 0;
    public int TaskKills { get; set; } = 0;
    public uint FreeSlots { get; set; } = 16;
    public uint TotalSlots { get; set; } = 16;
    public uint Copper { get; set; } = 0;
    public DateTime ConnectedAt { get; set; }
    public DateTime LastUpdate { get; set; }
    public uint QuestId { get; set; } = 0;
    public uint QuestStatus { get; set; } = 0;
    public uint Durability { get; set; } = 100;   // min equipped-slot durability % from STATE (100 = full)
    // [PLAYERPARTY] a REAL player leads this bot's group (server truth off STATE, 2026-07-07).
    // Flows snapshot -> ctx.Sense -> the GoalSelector "player-party" Idle hold; C++ owns the
    // whole escort behaviour (PlayerParty doctrine).
    public bool InPlayerParty { get; set; } = false;
    // [HUB-ERRAND] Boss distance off STATE (ppdist, 2026-07-08): -1 no boss, 99999 boss on
    // another map, else 3D yards. Rides snapshot -> the errand planner's abort guard.
    public int PartyBossDist { get; set; } = -1;
    // [CONSCRIPTED] Enlisted in a player's RTS army (server truth off STATE, 2026-08-24).
    // Planner stands down; the held objective survives so dismissal resumes in place.
    public bool Conscripted { get; set; } = false;
    // [HUB-ERRAND] The "do your rounds" run token (2026-07-08 §3). Stamped HERE by the
    // CHAT_RECV recognizer in HandleEventAsync — deliberately NOT in the STATE field-by-field
    // copy, so it persists across STATEs exactly like the conn.State control-plane pattern
    // promises. Null = nothing armed / "lets move" cleared it. Expiry is enforced by the
    // GoalSelector's liveness check, not here.
    public DateTime? HubErrandUntil { get; set; }
    // Full quest-log snapshot pushed on STATE (retired pull). Pipe-delimited, QUEST_STATUS_ALL format.
    public string Quests { get; set; } = "";
    // BotState class — add:
    public bool HasReceivedState { get; set; } = false;
}

/// <summary>
/// Tracks a single TCP connection from an AiBotAI instance.
/// </summary>
public class BotConnection
{
    public int Guid { get; set; }
    public TcpClient Client { get; set; } = null!;
    public NetworkStream Stream { get; set; } = null!;
    public CancellationTokenSource Cts { get; set; } = new();
    public BotState State { get; set; } = new();
    /// <summary>NetworkStream does not support concurrent writers.</summary>
    public SemaphoreSlim SendGate { get; } = new(1, 1);
}

public sealed class CombatLoadoutBridgeCommand
{
    public string RequestId { get; init; } = "";
    public uint ExpectedRevision { get; init; }
    public int SpecTab { get; init; }
    public int ActiveRole { get; init; }
    public bool ResetTalents { get; init; }
    /// <summary>Wire values are SPEC or CUSTOM.</summary>
    public string RotationMode { get; init; } = "SPEC";
    public string RotationProfile { get; init; } = "";
    public string RotationData { get; init; } = "";
}

public sealed class CombatLoadoutAck
{
    public int Guid { get; init; }
    public string RequestId { get; init; } = "";
    public string Status { get; init; } = "error";
    public string Code { get; init; } = "unknown";
    public uint Revision { get; init; }
    public int SpecTab { get; init; } = 255;
    public string TalentProfile { get; init; } = "";
    public int ActiveRole { get; init; }
    public string TalentProfileState { get; init; } = "unchecked";
    public int LearnedPoints { get; init; }
    public string RotationSource { get; init; } = "legacy";
    public string RotationProfile { get; init; } = "";
    public int LoadedInstructions { get; init; }
    public int SkippedInstructions { get; init; }
    public bool Reset { get; init; }

    public bool Success => Status.Equals("ok", StringComparison.OrdinalIgnoreCase)
        || Status.Equals("success", StringComparison.OrdinalIgnoreCase)
        || Status.Equals("applied", StringComparison.OrdinalIgnoreCase);
}

public sealed class BotNotConnectedException : InvalidOperationException
{
    public BotNotConnectedException(int guid)
        : base($"Bot {guid} is not connected to the bridge.")
    {
        Guid = guid;
    }

    public int Guid { get; }
}

public sealed class CombatLoadoutAckTimeoutException : TimeoutException
{
    public CombatLoadoutAckTimeoutException(int guid, string requestId, TimeSpan timeout)
        : base($"Bot {guid} did not acknowledge combat loadout request {requestId} within {timeout.TotalSeconds:0} seconds.")
    {
        Guid = guid;
        RequestId = requestId;
    }

    public int Guid { get; }
    public string RequestId { get; }
}

/// <summary>
/// The bridge started writing a destructive combat-loadout request, but lost the
/// socket or waiter before a correlated ACK arrived. Callers must refresh live
/// state and must never retry automatically.
/// </summary>
public sealed class CombatLoadoutOutcomeUnknownException : IOException
{
    public CombatLoadoutOutcomeUnknownException(int guid, string requestId, Exception? inner = null)
        : base(
            $"Bot {guid} lost its bridge connection after combat loadout request {requestId} began sending; the outcome is unknown.",
            inner)
    {
        Guid = guid;
        RequestId = requestId;
    }

    public int Guid { get; }
    public string RequestId { get; }
}

// ======================== BotBridgeService ========================

/// <summary>
/// TCP listener on port 3444. Each AiBotAI (C++ inside mangosd) connects as a client.
/// Protocol: newline-delimited JSON ("JSON lines"). Each line is a BridgeMessage.
///
/// Inbound message types:
///   HELLO       — bot announces itself on connect (guid, name, race, class, level, position)
///   STATE       — periodic state update (health, mana, position, combat, task)
///   EVENT       — discrete events (COMBAT_START, DEATH, RESPAWN, QUEST_COMPLETE, CHAT_RECV, etc.)
///
/// Outbound message types:
///   MOVE_TO     — walk to coordinates
///   SAY_TEXT    — say/yell/whisper text
///   SET_TASK    — assign a persistent task (GRIND, IDLE)
///   PING        — keepalive
/// </summary>
public class BotBridgeService : BackgroundService
{
    private static readonly TimeSpan CombatLoadoutAckTimeout = TimeSpan.FromSeconds(15);

    private sealed class PendingCombatLoadout
    {
        public PendingCombatLoadout(
            int guid,
            BotConnection connection,
            TaskCompletionSource<CombatLoadoutAck> completion)
        {
            Guid = guid;
            Connection = connection;
            Completion = completion;
        }

        public int Guid { get; }
        public BotConnection Connection { get; }
        public TaskCompletionSource<CombatLoadoutAck> Completion { get; }
        public int SendAttempted;
    }

    private readonly ILogger<BotBridgeService> _logger;
    private readonly IHubContext<BotBridgeHub> _hub;
    private TcpListener? _listener;

    // BotBrain integration — set after startup to avoid circular DI
    private BotBrainService? _brain;
    private RotationService? _rotations;   // [ROTATION] late-wired by RotationService's ctor (see SetRotationService)

    // ChatCoordinator integration (C0, §5.5) — late-wired like the brain: the coordinator
    // constructor-injects this bridge (for SendSayTextAsync in C2+), so the bridge cannot
    // constructor-inject it back. ChatCoordinator.StartAsync calls SetChatCoordinator(this).
    private IChatCoordinator? _chat;

    // All connected bots, keyed by character GUID
    public ConcurrentDictionary<int, BotConnection> Connections { get; } = new();

    // Snapshot of all bot states (survives brief disconnects for UI display)
    public ConcurrentDictionary<int, BotState> BotStates { get; } = new();

    // Admin build changes use a correlated request registry independent of
    // BotBrain's singular, event-type-only planner WAIT.
    private readonly ConcurrentDictionary<string, PendingCombatLoadout> _pendingCombatLoadouts
        = new(StringComparer.Ordinal);

    // [HUB-ERRAND] How long a "do your rounds" run token stays live. The GoalSelector
    // auto-reverts to the follow hold at expiry regardless of errand progress, so this is
    // the errand's hard timebox as well as its arming window.
    private static readonly TimeSpan HubErrandWindow = TimeSpan.FromMinutes(4);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public BotBridgeService(ILogger<BotBridgeService> logger, IHubContext<BotBridgeHub> hub)
    {
        _logger = logger;
        _hub = hub;
    }

    /// <summary>
    /// Called by BotBrainService after DI resolution to wire itself in.
    /// Can't inject directly due to circular dependency (Bridge ← Brain → Bridge).
    /// </summary>
    public void SetBrainService(BotBrainService brain)
    {
        _brain = brain;
        _logger.LogInformation("BotBridge: BotBrainService wired for event routing");
    }

    /// <summary>
    /// [ROTATION] Called by RotationService from its constructor — the same late-wire
    /// pattern as SetBrainService (Bridge ← Rotation → Bridge would otherwise be a DI
    /// cycle). Enables the HELLO re-push so assignments survive restarts and relogs.
    /// </summary>
    public void SetRotationService(RotationService rotations)
    {
        _rotations = rotations;
        _logger.LogInformation("BotBridge: RotationService wired for HELLO re-push");
    }

    private RaidPlanService? _raidPlans;   // [RAID-PLAN] late-wired by RaidPlanService's ctor (same pattern)

    /// <summary>
    /// [RAID-PLAN] Called by RaidPlanService from its constructor — the SetRotationService
    /// late-wire pattern. Enables the HELLO re-push so raid-plan assignments survive
    /// restarts and relogs (PLAN_19 M-B).
    /// </summary>
    public void SetRaidPlanService(RaidPlanService raidPlans)
    {
        _raidPlans = raidPlans;
        _logger.LogInformation("BotBridge: RaidPlanService wired for HELLO re-push");
    }

    /// <summary>
    /// Called by ChatCoordinator.StartAsync to wire itself in (C0, §5.5).
    /// Same late-wire pattern as SetBrainService — see _chat field comment.
    /// </summary>
    public void SetChatCoordinator(IChatCoordinator chat)
    {
        _chat = chat;
        _logger.LogInformation("BotBridge: ChatCoordinator wired for chat stimulus routing");
    }

    // ==================== Lifecycle ====================

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _listener = new TcpListener(IPAddress.Loopback, 3444);
        try
        {
            _listener.Start();
        }
        catch (SocketException ex)
        {
            CircuitTrace.Hit(0, "bridge: port 3444 busy, bridge disabled for this instance");
            // Another instance (e.g. the WSL-hosted server stack) already owns 3444. Losing the
            // bridge only means bots can't connect to THIS instance — that must not take down the
            // whole host (BackgroundServiceExceptionBehavior is StopHost).
            _logger.LogError(
                "BotBridge: cannot listen on 127.0.0.1:3444 ({Message}) — another instance is likely running; bridge disabled for this instance",
                ex.Message);
            return;
        }
        _logger.LogInformation("BotBridge TCP listener started on 127.0.0.1:3444");

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(stoppingToken);
                _ = HandleClientAsync(client, stoppingToken);
            }
        }
        catch (OperationCanceledException) { /* shutdown */ }   // cb:fold shutdown trivia
        catch (Exception ex)
        {
            CircuitTrace.Hit(0, "bridge: listener crashed");
            _logger.LogError(ex, "BotBridge listener error");
        }
        finally
        {
            _listener.Stop();
            _logger.LogInformation("BotBridge TCP listener stopped");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("BotBridge stopping — closing listener and all connections");

        _listener?.Stop();

        foreach (var kvp in Connections)
        {
            try { kvp.Value.Cts.Cancel(); } catch { /* cb:fold disposal trivia */ }
            try { kvp.Value.Client.Dispose(); } catch { /* cb:fold disposal trivia */ }
        }
        Connections.Clear();
        foreach (var pending in _pendingCombatLoadouts.ToArray())
        {
            if (_pendingCombatLoadouts.TryRemove(pending.Key, out var removed))
            {
                CircuitTrace.Hit(removed.Guid, "bridge: pending loadout failed on shutdown");
                Exception error = Volatile.Read(ref removed.SendAttempted) == 0
                    ? new BotNotConnectedException(removed.Guid)
                    : new CombatLoadoutOutcomeUnknownException(removed.Guid, pending.Key);
                removed.Completion.TrySetException(error);
            }
        }

        await base.StopAsync(cancellationToken);
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken appToken)
    {
        var endpoint = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
        _logger.LogInformation("BotBridge: new connection from {Endpoint}", endpoint);

        BotConnection? conn = null;

        try
        {
            var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
            conn = new BotConnection { Client = client, Stream = stream };

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(appToken, conn.Cts.Token);

            while (!linked.Token.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(linked.Token);
                if (line == null) { CircuitTrace.Hit(conn.Guid, "bridge: socket closed by peer"); break; }

                if (string.IsNullOrWhiteSpace(line)) continue;   // cb:fold blank keepalive line

                try
                {
                    var msg = JsonSerializer.Deserialize<BridgeMessage>(line, JsonOpts);
                    if (msg != null)
                        await ProcessInboundAsync(msg, conn);   // cb:fold parse detail, dispatch cases probe every message
                }
                catch (JsonException ex)
                {
                    CircuitTrace.Hit(conn.Guid, "bridge: malformed JSON line dropped");
                    _logger.LogWarning("BotBridge: malformed JSON from {Endpoint}: {Error}", endpoint, ex.Message);
                }
            }
        }
        catch (OperationCanceledException) { /* normal */ }   // cb:fold shutdown trivia
        catch (IOException) { /* disconnected */ }            // cb:fold disconnect trivia, teardown probe below fires
        catch (Exception ex)
        {
            CircuitTrace.Hit(conn?.Guid ?? 0, "bridge: client loop error");
            _logger.LogWarning(ex, "BotBridge: client error from {Endpoint}", endpoint);
        }
        finally
        {
            if (conn != null && conn.Guid != 0)
            {
                CircuitTrace.Hit(conn.Guid, "bridge: bot disconnected, teardown");
                // A fast relog can replace the dictionary entry before this old
                // socket's finally runs. Only tear down state/requests if this is
                // still the active connection for the guid.
                bool removedActive = Connections.TryGetValue(conn.Guid, out var active)
                    && ReferenceEquals(active, conn)
                    && Connections.TryRemove(conn.Guid, out _);
                // Pending requests belong to a concrete socket session, not just
                // a guid. Fail this connection's request even if a fast relog has
                // already replaced it in Connections.
                FailPendingCombatLoadoutsForConnection(conn);
                // Mark state as disconnected but keep it for UI
                if (removedActive && BotStates.TryGetValue(conn.Guid, out var state))
                {
                    CircuitTrace.Hit(conn.Guid, "bridge: state marked DISCONNECTED");
                    state.TaskState = "DISCONNECTED";
                }

                _logger.LogInformation("BotBridge: bot {Guid} ({Name}) disconnected", conn.Guid, conn.State.Name);
                if (removedActive)
                    await _hub.Clients.All.SendAsync("BotDisconnected", conn.Guid);   // cb:fold notify only, teardown probe above fires
            }

            try { client.Dispose(); } catch { /* cb:fold disposal trivia */ }
        }
    }

    // ==================== Inbound Processing ====================

    private async Task ProcessInboundAsync(BridgeMessage msg, BotConnection conn)
    {
        switch (msg.Type.ToUpperInvariant())
        {
            case "HELLO":
                CircuitTrace.Hit(conn.Guid, "bridge: HELLO dispatched");
                await HandleHelloAsync(msg.Payload, conn);
                break;

            case "STATE":
                CircuitTrace.Hit(conn.Guid, "bridge: STATE dispatched");
                await HandleStateAsync(msg.Payload, conn);
                break;

            case "EVENT":
                CircuitTrace.Hit(conn.Guid, "bridge: EVENT dispatched");
                await HandleEventAsync(msg.Payload, conn);
                break;

            default:
                CircuitTrace.HitNote(conn.Guid, "bridge: unknown message type", msg.Type);
                _logger.LogWarning("BotBridge: unknown message type '{Type}' from bot {Guid}", msg.Type, conn.Guid);
                break;
        }
    }

    private async Task HandleHelloAsync(JsonElement payload, BotConnection conn)
    {
        var hello = payload.Deserialize<BotHelloPayload>(JsonOpts);
        if (hello == null) { CircuitTrace.Hit(conn.Guid, "bridge: HELLO payload malformed"); return; }

        CircuitTrace.Hit(hello.Guid, "bridge: HELLO adopted, bot registered", hello.Level);
        conn.Guid = hello.Guid;
        conn.State = new BotState
        {
            Guid = hello.Guid,
            Name = hello.Name,
            Race = hello.Race,
            ClassId = hello.ClassId,
            Level = hello.Level,
            SpecTab = hello.SpecTab,
            SpecProfile = hello.SpecProfile?.Trim() ?? "",
            ActiveRole = hello.ActiveRole,
            TalentProfileState = NormalizeRuntimeToken(hello.TalentProfileState, "unchecked"),
            RotationSource = NormalizeRuntimeToken(hello.RotationSource, "legacy"),
            RotationProfile = hello.RotationProfile?.Trim() ?? "",
            RotationInstructionCount = Math.Max(0, hello.RotationInstructionCount),
            RotationCastableCount = Math.Max(0, hello.RotationCastableCount),
            CombatConfigRevision = hello.CombatConfigRevision,
            MapId = hello.MapId,
            ZoneId = hello.ZoneId,
            X = hello.X,
            Y = hello.Y,
            Z = hello.Z,
            Health = 100,
            MaxHealth = 100,
            TaskState = "IDLE",
            ConnectedAt = DateTime.UtcNow,
            LastUpdate = DateTime.UtcNow
        };

        // Register the exact-connection hydration barrier before making this
        // socket discoverable. Any loadout request that can see the connection
        // can therefore also see and await its persisted rotation replay.
        RotationService.HelloHydrationRegistration? rotationHydration =
            _rotations?.RegisterHelloHydration(conn, hello.Name);
        try
        {
            Connections[hello.Guid] = conn;
            BotStates[hello.Guid] = conn.State;
        }
        finally
        {
            // Always settle a registration. If publication failed, the exact
            // writer will reject this inactive connection and completion still
            // releases any waiter instead of stranding it forever.
            if (rotationHydration != null)
            {
                CircuitTrace.Hit(conn.Guid, "bridge: rotation hydration started");
                _rotations!.StartHelloHydration(rotationHydration);
            }
        }

        _logger.LogInformation("BotBridge: HELLO from {Name} (guid={Guid}, class={Class}, level={Level})",
            hello.Name, hello.Guid, hello.ClassId, hello.Level);

        // Publish to web clients only after the hydration replay has been
        // registered and started for this exact socket.
        await _hub.Clients.All.SendAsync("BotConnected", conn.State);

        // [RAID-PLAN] Same law for the raid plan: the persisted assignment re-pushes
        // on every HELLO, fire-and-forget, failures log inside (PLAN_19 M-B).
        if (_raidPlans != null)
        {
            CircuitTrace.Hit(conn.Guid, "bridge: raid-plan re-push queued");
            _ = _raidPlans.OnBotHelloAsync(hello.Guid, hello.Name);
        }
    }

    private async Task HandleStateAsync(JsonElement payload, BotConnection conn)
    {
        var state = payload.Deserialize<BotStatePayload>(JsonOpts);
        if (state == null) { CircuitTrace.Hit(conn.Guid, "bridge: STATE payload malformed"); return; }

        CircuitTrace.Hit(conn.Guid, "bridge: STATE heartbeat applied");
        var bs = conn.State;
        bs.Health = state.Health;
        bs.MaxHealth = state.MaxHealth;
        bs.Mana = state.Mana;
        bs.MaxMana = state.MaxMana;
        bs.Level = state.Level;
        if (state.SpecTab is >= 0 and <= 2) bs.SpecTab = state.SpecTab;   // cb:fold field normalization
        bs.SpecProfile = state.SpecProfile?.Trim() ?? "";
        if (state.ActiveRole is >= 1 and <= 4) bs.ActiveRole = state.ActiveRole;   // cb:fold field normalization
        bs.TalentProfileState = NormalizeRuntimeToken(state.TalentProfileState, "unchecked");
        bs.RotationSource = NormalizeRuntimeToken(state.RotationSource, "legacy");
        bs.RotationProfile = state.RotationProfile?.Trim() ?? "";
        bs.RotationInstructionCount = Math.Max(0, state.RotationInstructionCount);
        bs.RotationCastableCount = Math.Max(0, state.RotationCastableCount);
        bs.CombatConfigRevision = state.CombatConfigRevision;
        bs.MapId = state.MapId;
        bs.ZoneId = state.ZoneId;
        bs.X = state.X;
        bs.Y = state.Y;
        bs.Z = state.Z;
        bs.InCombat = state.InCombat;
        bs.IsDead = state.IsDead;
        bs.TargetGuid = state.TargetGuid;
        bs.TaskState = state.TaskState;
        bs.TaskKind = state.TaskKind;           // Held-task echo (§4) — committed kind, NOT the display taskState
        bs.TaskActivity = state.TaskActivity;   // Held-task echo (§4) — Unknown until C++ emits it
        bs.TaskCreature = state.TaskCreature;
        bs.TaskDestX = state.TaskDestX;
        bs.TaskDestY = state.TaskDestY;
        bs.TaskDestZ = state.TaskDestZ;
        bs.TaskKills = state.TaskKills;
        bs.FreeSlots = state.FreeSlots;
        bs.TotalSlots = state.TotalSlots;
        bs.Copper = state.Copper;
        bs.LastUpdate = DateTime.UtcNow;
        bs.QuestId = state.QuestId;
        bs.QuestStatus = state.QuestStatus;
        bs.Durability = state.Durability;
        bs.InPlayerParty = state.Pparty != 0;   // [PLAYERPARTY] pparty on STATE (2026-07-07)
        bs.Conscripted = state.Conscripted != 0;   // [CONSCRIPTED] conscripted on STATE (2026-08-24)
        bs.PartyBossDist = state.Ppdist;        // [HUB-ERRAND] ppdist on STATE (2026-07-08); HubErrandUntil deliberately NOT copied — it persists
        bs.Quests = state.Quests;   // full quest-log snapshot (retired pull → STATE is the single source of truth)
        bs.HasReceivedState = true;

        BotStates[conn.Guid] = bs;

        await _hub.Clients.All.SendAsync("BotStateUpdate", bs);
    }

    private async Task HandleEventAsync(JsonElement payload, BotConnection conn)
    {
        var evt = payload.Deserialize<BotEventPayload>(JsonOpts);
        if (evt == null) { CircuitTrace.Hit(conn.Guid, "bridge: EVENT payload malformed"); return; }

        var eventType = evt.Event?.ToUpperInvariant() ?? "";
        CircuitTrace.HitNote(conn.Guid, "bridge: event received", eventType);

        // Fresh-position refresh ("give C# fresh data whenever C++ has it"). Any event whose data
        // carries x|y|z — a TASK_COMPLETE arrival, a TELEPORT_ACK — updates the bot's canonical
        // position NOW, the same field the 5s STATE heartbeat writes (conn.State.X/Y/Z, which the
        // brain snapshots into ctx.Pos). Without it, ctx.Pos is up to one 5s cycle stale: a bot that
        // just walked to a giver still reads tens of yards out, so the planner re-issues MOVE_TO
        // (or the arrival gate false-rejects) instead of interacting — the 150s between-task park.
        // C++ already knows the exact arrival coord; this stops discarding it until the next STATE.
        if (!string.IsNullOrEmpty(evt.Data) && evt.Data.Contains("x="))
        {
            CircuitTrace.Hit(conn.Guid, "bridge: event carries fresh position, canonical pos updated");
            var posKv = ParsePipeDelimited(evt.Data);
            if (posKv.TryGetValue("x", out var sx) &&
                posKv.TryGetValue("y", out var sy) &&
                posKv.TryGetValue("z", out var sz) &&
                float.TryParse(sx, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var fx) &&
                float.TryParse(sy, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var fy) &&
                float.TryParse(sz, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var fz))
            {   // cb:fold parse detail, outcome carried by the fresh-position probe
                conn.State.X = fx;
                conn.State.Y = fy;
                conn.State.Z = fz;
            }
        }

        switch (eventType)
        {
            case "COMBAT_LOADOUT_ACK":
                {
                    CircuitTrace.Hit(conn.Guid, "bridge: combat-loadout ack received");
                    var values = ParsePipeDelimited(evt.Data ?? "");
                    string requestId = values.GetValueOrDefault("requestId", "").Trim();
                    var ack = new CombatLoadoutAck
                    {
                        Guid = conn.Guid,
                        RequestId = requestId,
                        Status = values.GetValueOrDefault("status", "error"),
                        Code = values.GetValueOrDefault("code", "unknown"),
                        Revision = ParseUInt(values.GetValueOrDefault("revision")),
                        SpecTab = ParseInt(values.GetValueOrDefault("specTab"), 255),
                        TalentProfile = values.GetValueOrDefault("profile", ""),
                        ActiveRole = ParseInt(values.GetValueOrDefault("role")),
                        TalentProfileState = NormalizeRuntimeToken(
                            values.GetValueOrDefault("talentState"), "unchecked"),
                        LearnedPoints = Math.Max(0, ParseInt(values.GetValueOrDefault("learned"))),
                        RotationSource = NormalizeRuntimeToken(
                            values.GetValueOrDefault("rotationSource"), "legacy"),
                        RotationProfile = values.GetValueOrDefault("rotationProfile", ""),
                        LoadedInstructions = Math.Max(0, ParseInt(values.GetValueOrDefault("loaded"))),
                        SkippedInstructions = Math.Max(0, ParseInt(values.GetValueOrDefault("skipped"))),
                        Reset = ParseBool(values.GetValueOrDefault("reset"))
                    };

                    if (!Connections.TryGetValue(conn.Guid, out BotConnection? activeConnection)
                        || !ReferenceEquals(activeConnection, conn))
                    {
                        CircuitTrace.Hit(conn.Guid, "bridge: ack from superseded connection ignored");
                        if (requestId.Length > 0
                            && _pendingCombatLoadouts.TryGetValue(requestId, out PendingCombatLoadout? inactivePending)
                            && ReferenceEquals(inactivePending.Connection, conn))
                        {
                            CircuitTrace.Hit(conn.Guid, "bridge: superseded ack fails its own waiter");
                            inactivePending.Completion.TrySetException(
                                new CombatLoadoutOutcomeUnknownException(conn.Guid, requestId));
                        }
                        _logger.LogWarning(
                            "[COMBAT-LOADOUT] ignored ACK from superseded connection for {Name}: requestId={RequestId}; live outcome requires refresh",
                            conn.State.Name, requestId.Length == 0 ? "(missing)" : requestId);
                        return;
                    }

                    // The ACK describes final runtime truth on both success and a
                    // verified rollback. Copy every well-formed field before waking
                    // the HTTP request so its follow-up GET sees the same revision.
                    if (ack.SpecTab is >= 0 and <= 2) conn.State.SpecTab = ack.SpecTab;   // cb:fold field normalization
                    conn.State.SpecProfile = ack.TalentProfile.Trim();
                    if (ack.ActiveRole is >= 1 and <= 4) conn.State.ActiveRole = ack.ActiveRole;   // cb:fold field normalization
                    conn.State.TalentProfileState = ack.TalentProfileState;
                    conn.State.RotationSource = ack.RotationSource;
                    conn.State.RotationProfile = ack.RotationProfile.Trim();
                    conn.State.RotationInstructionCount = ack.LoadedInstructions + ack.SkippedInstructions;
                    conn.State.RotationCastableCount = ack.LoadedInstructions;
                    conn.State.CombatConfigRevision = ack.Revision;
                    conn.State.LastUpdate = DateTime.UtcNow;
                    BotStates[conn.Guid] = conn.State;

                    bool matched = false;
                    if (requestId.Length > 0
                        && _pendingCombatLoadouts.TryGetValue(requestId, out var pending)
                        && pending.Guid == conn.Guid
                        && ReferenceEquals(pending.Connection, conn))
                    {
                        CircuitTrace.Hit(conn.Guid, "bridge: ack correlated to its waiter");
                        matched = pending.Completion.TrySetResult(ack);
                    }

                    if (ack.Success)
                        _logger.LogInformation(   // cb:fold logging only, ack probes carry the outcome
                            "[COMBAT-LOADOUT] {Name} ACK {RequestId}: revision={Revision} spec={Spec} role={Role} rotation={Source}/{Profile} loaded={Loaded} skipped={Skipped}",
                            conn.State.Name, requestId, ack.Revision, ack.SpecTab, ack.ActiveRole,
                            ack.RotationSource, ack.RotationProfile, ack.LoadedInstructions, ack.SkippedInstructions);
                    else
                        _logger.LogWarning(   // cb:fold logging only, rejection rides the ack status note below
                            "[COMBAT-LOADOUT] {Name} rejected {RequestId}: status={Status} code={Code} revision={Revision}",
                            conn.State.Name, requestId, ack.Status, ack.Code, ack.Revision);
                    CircuitTrace.HitNote(conn.Guid, "bridge: loadout ack outcome", ack.Status);

                    if (!matched)
                    {
                        CircuitTrace.Hit(conn.Guid, "bridge: late or uncorrelated loadout ack");
                        _logger.LogWarning(
                            "[COMBAT-LOADOUT] late or uncorrelated ACK from {Name}: requestId={RequestId}",
                            conn.State.Name, requestId.Length == 0 ? "(missing)" : requestId);
                    }

                    await _hub.Clients.All.SendAsync("BotCombatLoadoutChanged", new
                    {
                        guid = conn.Guid,
                        state = conn.State,
                        ack,
                        timestamp = DateTime.UtcNow
                    });
                    return;
                }

            case "ROTATION_ACK":   // cb:fold forwarding shell, event probed at method entry; the skipped-spells arm is the decision
                {

                    // [ROTATION] C++ resolved the pushed slate. skipped>0 = the profile names
                    // spells this bot doesn't know (wrong rank / not yet trained) — warn loudly
                    // so an under-performing rotation is a log line, not a mystery at the keyboard.
                    var ackKv = ParsePipeDelimited(evt.Data ?? "");
                    ackKv.TryGetValue("profile", out var ackProfile);
                    ackKv.TryGetValue("loaded", out var ackLoaded);
                    ackKv.TryGetValue("skipped", out var ackSkipped);
                    if (int.TryParse(ackSkipped, out var nSkipped) && nSkipped > 0)
                    {
                        CircuitTrace.Hit(conn.Guid, "bridge: rotation ack reports skipped spells", nSkipped);
                        _logger.LogWarning("[ROTATION] {Name} ACK '{Profile}': loaded={Loaded} SKIPPED={Skipped} — profile names unknown/unlearned spells",
                            conn.State.Name, ackProfile ?? "?", ackLoaded ?? "?", nSkipped);
                    }
                    else
                        _logger.LogInformation("[ROTATION] {Name} ACK '{Profile}': loaded={Loaded} skipped={Skipped}",   // cb:fold logging only
                            conn.State.Name, ackProfile ?? "?", ackLoaded ?? "0", ackSkipped ?? "0");
                    break;
                }

            case "KILL":   // cb:fold UI forward only, event probed at method entry
                _logger.LogInformation("BotBridge: KILL by {Name} — creature entry={Entry} guid={CrGuid}",
                    conn.State.Name, evt.CreatureEntry, evt.CreatureGuid);
                await _hub.Clients.All.SendAsync("BotEvent", new
                {
                    guid = conn.Guid,
                    name = conn.State.Name,
                    eventType = "KILL",
                    creatureEntry = evt.CreatureEntry,
                    creatureGuid = evt.CreatureGuid,
                    timestamp = DateTime.UtcNow
                });
                break;

            case "QUEST_UPDATE":   // cb:fold UI forward only, event probed at method entry
                _logger.LogInformation("BotBridge: QUEST_UPDATE {Name} — quest={QuestId} status={Status}",
                    conn.State.Name, evt.QuestId, evt.Status);
                await _hub.Clients.All.SendAsync("BotEvent", new
                {
                    guid = conn.Guid,
                    name = conn.State.Name,
                    eventType = "QUEST_UPDATE",
                    questId = evt.QuestId,
                    status = evt.Status,
                    timestamp = DateTime.UtcNow
                });
                break;

            case "LEVEL_UP":   // cb:fold UI forward only, event probed at method entry; executor probes the progress stamp
                _logger.LogInformation("BotBridge: LEVEL_UP {Name} → level {Level}",
                    conn.State.Name, evt.NewLevel);
                if (evt.NewLevel.HasValue)
                    conn.State.Level = evt.NewLevel.Value;   // cb:fold field normalization
                await _hub.Clients.All.SendAsync("BotEvent", new
                {
                    guid = conn.Guid,
                    name = conn.State.Name,
                    eventType = "LEVEL_UP",
                    newLevel = evt.NewLevel,
                    timestamp = DateTime.UtcNow
                });
                break;

            case "CHAT_RECV":
                CircuitTrace.Hit(conn.Guid, "bridge: chat received, recognizer + coordinator hand-off");
                _logger.LogInformation("BotBridge: CHAT_RECV bot={Name} from={Sender} [{ChatType}]: {Message}",
                    conn.State.Name, evt.Sender, evt.ChatType ?? "say", evt.Message);
                await _hub.Clients.All.SendAsync("BotChatReceived", new
                {
                    guid = conn.Guid,
                    botName = conn.State.Name,
                    senderName = evt.Sender ?? "Unknown",
                    message = evt.Message ?? "",
                    chatType = evt.ChatType ?? "say",
                    channelName = evt.ChannelName ?? "",
                    timestamp = DateTime.UtcNow
                });
                // [HUB-ERRAND] Deterministic party-chat command recognizer (2026-07-08 §3).
                // Placed BEFORE the ChatCoordinator hand-off (a command must never depend on an
                // LLM turn) but still FORWARDING after it (personas may ad-lib on top of the ack).
                // Boss-only: the sender must be a resolvable REAL player — guid known (non-zero)
                // and NOT in the bot roster. Every party bot hears the party line and stamps
                // ITSELF — no party-membership mapping needed. "do your rounds" arms a bounded
                // run token on conn.State (persists across STATEs — the field-by-field STATE copy
                // never touches it); "lets move"/"let's move" clears it, the GoalSelector reverts
                // to the follow hold next tick, and the goal change SET_TASK IDLEs C++ back into
                // formation. The interrupt is free.
                if (string.Equals(evt.ChatType, "party", StringComparison.OrdinalIgnoreCase)
                    && evt.SenderGuid is uint hubSender && hubSender != 0
                    && !BotStates.ContainsKey((int)hubSender)
                    && !string.IsNullOrWhiteSpace(evt.Message))
                {
                    CircuitTrace.Hit(conn.Guid, "bridge: party line from real player, command recognizer running");
                    string hubMsg = evt.Message.Replace("'", "").ToLowerInvariant();
                    if (hubMsg.Contains("do your rounds"))
                    {
                        CircuitTrace.Hit(conn.Guid, "bridge: hub-errand token armed");
                        conn.State.HubErrandUntil = DateTime.UtcNow.Add(HubErrandWindow);
                        _logger.LogInformation("[HUB-ERRAND] {Name} armed by {Sender}: 'do your rounds' (until {Until:HH:mm:ss}Z)",
                            conn.State.Name, evt.Sender ?? "?", conn.State.HubErrandUntil);
                    }
                    else if (hubMsg.Contains("lets move"))
                    {
                        CircuitTrace.Hit(conn.Guid, "bridge: hub-errand token cleared");
                        conn.State.HubErrandUntil = null;
                        _logger.LogInformation("[HUB-ERRAND] {Name} cleared by {Sender}: 'lets move'",
                            conn.State.Name, evt.Sender ?? "?");
                    }
                    else
                    {   // cb:fold recognizer fall-through, follow-cmd arm below is the decision
                        // [FOLLOW-CMD] "{bot} follow {player|me|auto}" (2026-07-16) — addressed
                        // escort override. ONLY the named bot obeys (each connection checks the
                        // first token against ITS OWN name — a party line reaches every bot's
                        // CHAT_RECV, so no roster lookup is needed). "me" resolves to the speaker;
                        // "auto" or a bare "follow" reverts to the GUIDLow-modulo split. C++ stores
                        // the name and FindEscortBoss prefers it while that human is in the group,
                        // falling back to the auto split otherwise — a typo'd name is therefore
                        // harmless (and visible: the bot answers with the name it was given).
                        // Documented in CHAT_COMMANDS.md — keep that file current when adding here.
                        var tok = hubMsg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (tok.Length >= 2
                            && string.Equals(tok[0], conn.State.Name, StringComparison.OrdinalIgnoreCase)
                            && tok[1] == "follow")
                        {
                            CircuitTrace.Hit(conn.Guid, "bridge: follow override command for this bot");
                            string target = tok.Length >= 3 ? tok[2] : "auto";
                            if (target == "me")
                                target = (evt.Sender ?? "").ToLowerInvariant();   // cb:fold target resolution detail
                            bool clearOverride = target is "auto" or "";

                            await SendToBotAsync(conn.Guid, "SET_ESCORT",
                                new { player_name = clearOverride ? "" : target });
                            string followAck = clearOverride
                                ? "Back to the usual spread."
                                : $"Following {char.ToUpperInvariant(target[0]) + target[1..]}!";
                            await SendSayTextAsync(conn.Guid, followAck, 1);
                            _logger.LogInformation("[FOLLOW-CMD] {Name} escort override by {Sender}: '{Target}'",
                                conn.State.Name, evt.Sender ?? "?", clearOverride ? "(auto)" : target);
                        }
                    }
                }

                // C0 (§5.5): chat is the ChatCoordinator's business, not the goal spine's.
                // Hand off and RETURN — CHAT_RECV must not reach _brain.HandleBridgeEventAsync
                // (it used to fall through to _driver.OnEvent and evaporate).
                _chat?.EnqueueStimulus(new ChatStimulusRaw(
                    conn.Guid,
                    evt.Sender ?? "Unknown",
                    evt.SenderGuid ?? 0,
                    evt.Message ?? "",
                    evt.ChatType ?? "say",
                    evt.ChannelName ?? "",
                    DateTime.UtcNow));
                return;

            case "TASK_COMPLETE":   // cb:fold UI forward only, event probed at method entry
                _logger.LogInformation("BotBridge: TASK_COMPLETE {Name} — {Data}",
                    conn.State.Name, evt.Data);
                conn.State.TaskState = "IDLE";
                await _hub.Clients.All.SendAsync("BotEvent", new
                {
                    guid = conn.Guid,
                    name = conn.State.Name,
                    eventType = "TASK_COMPLETE",
                    data = evt.Data,
                    timestamp = DateTime.UtcNow
                });
                break;

            case "FLIGHT_STARTED":   // cb:fold UI forward only, event probed at method entry
                _logger.LogInformation("BotBridge: FLIGHT_STARTED {Name}", conn.State.Name);
                conn.State.TaskState = "FLYING";
                await _hub.Clients.All.SendAsync("BotEvent", new
                {
                    guid = conn.Guid,
                    name = conn.State.Name,
                    eventType = "FLIGHT_STARTED",
                    timestamp = DateTime.UtcNow
                });
                break;

            case "FLIGHT_COMPLETE":   // cb:fold UI forward only, event probed at method entry
                _logger.LogInformation("BotBridge: FLIGHT_COMPLETE {Name}", conn.State.Name);
                conn.State.TaskState = "IDLE";
                await _hub.Clients.All.SendAsync("BotEvent", new
                {
                    guid = conn.Guid,
                    name = conn.State.Name,
                    eventType = "FLIGHT_COMPLETE",
                    timestamp = DateTime.UtcNow
                });
                break;

            case "FLIGHT_FAILED":   // cb:fold UI forward only, event probed at method entry
                _logger.LogInformation("BotBridge: FLIGHT_FAILED {Name} — reason={Reason} have={Have} need={Need}",
                    conn.State.Name, evt.Reason, evt.Have, evt.Need);
                await _hub.Clients.All.SendAsync("BotEvent", new
                {
                    guid = conn.Guid,
                    name = conn.State.Name,
                    eventType = "FLIGHT_FAILED",
                    reason = evt.Reason ?? "unknown",
                    have = evt.Have,
                    need = evt.Need,
                    cost = evt.Cost,
                    timestamp = DateTime.UtcNow
                });
                break;

            case "LOOT":   // cb:fold UI forward only, event probed at method entry
                _logger.LogInformation("BotBridge: LOOT {Name} — {Data}",
                    conn.State.Name, evt.Data);
                await _hub.Clients.All.SendAsync("BotEvent", new
                {
                    guid = conn.Guid,
                    name = conn.State.Name,
                    eventType = "LOOT",
                    data = evt.Data,
                    timestamp = DateTime.UtcNow
                });
                break;

            case "SELL_ACK":   // cb:fold UI forward only, event probed at method entry
                {
                    var sellParts = ParsePipeDelimited(evt.Data);
                    _logger.LogInformation(
                        "BotBridge: SELL_ACK {Name} — sold={Sold} earned={Earned}c free={Free} total={Total}c",
                        conn.State.Name,
                        sellParts.GetValueOrDefault("sold", "0"),
                        sellParts.GetValueOrDefault("copper_earned", "0"),
                        sellParts.GetValueOrDefault("free_slots", "0"),
                        sellParts.GetValueOrDefault("copper_total", "0"));
                    await _hub.Clients.All.SendAsync("BotEvent", new
                    {
                        guid = conn.Guid,
                        name = conn.State.Name,
                        eventType = "SELL_ACK",
                        data = evt.Data,
                        timestamp = DateTime.UtcNow
                    });
                    break;
                }

            case "SELL_FAIL":   // cb:fold UI forward only, event probed at method entry
                {
                    _logger.LogWarning("BotBridge: SELL_FAIL {Name} — {Data}",
                        conn.State.Name, evt.Data);
                    await _hub.Clients.All.SendAsync("BotEvent", new
                    {
                        guid = conn.Guid,
                        name = conn.State.Name,
                        eventType = "SELL_FAIL",
                        data = evt.Data,
                        timestamp = DateTime.UtcNow
                    });
                    break;
                }

            case "EQUIP":   // cb:fold UI forward only, event probed at method entry
                _logger.LogInformation("BotBridge: EQUIP {Name} — {Data}",
                    conn.State.Name, evt.Data);
                await _hub.Clients.All.SendAsync("BotEvent", new
                {
                    guid = conn.Guid,
                    name = conn.State.Name,
                    eventType = "EQUIP",
                    data = evt.Data,
                    timestamp = DateTime.UtcNow
                });
                break;

            case "BAG_EQUIP":   // cb:fold UI forward only, event probed at method entry
                _logger.LogInformation("BotBridge: BAG_EQUIP {Name} — {Data}",
                    conn.State.Name, evt.Data);
                await _hub.Clients.All.SendAsync("BotEvent", new
                {
                    guid = conn.Guid,
                    name = conn.State.Name,
                    eventType = "BAG_EQUIP",
                    data = evt.Data,
                    timestamp = DateTime.UtcNow
                });
                break;

            default:   // cb:fold UI forward only, event probed at method entry
                _logger.LogInformation("BotBridge: EVENT {Event} from {Name} (guid={Guid}): {Data}",
                    evt.Event, conn.State.Name, conn.Guid, evt.Data);
                await _hub.Clients.All.SendAsync("BotEvent", new
                {
                    guid = conn.Guid,
                    name = conn.State.Name,
                    eventType = evt.Event,
                    data = evt.Data,
                    timestamp = DateTime.UtcNow
                });
                break;
        }

        // Route to behavioral engine (if wired)
        if (_brain != null)
        {
            CircuitTrace.Hit(conn.Guid, "bridge: event routed to brain");
            var botEvent = new MangosSuperUI.BotLogic.Core.BotEvent
            {
                EventType = eventType,
                CreatureEntry = evt.CreatureEntry ?? 0,
                CreatureGuid = evt.CreatureGuid ?? 0,
                QuestId = evt.QuestId ?? 0,
                QuestStatus = evt.Status ?? "",
                NewLevel = evt.NewLevel ?? 0,
                Sender = evt.Sender ?? "",
                SenderGuid = evt.SenderGuid ?? 0,
                Message = evt.Message ?? "",
                ChatType = evt.ChatType ?? "",
                ChannelName = evt.ChannelName ?? "",
                Data = evt.Data ?? "",
                Reason = evt.Reason ?? "",
                Have = evt.Have ?? 0,
                Need = evt.Need ?? 0,
                Cost = evt.Cost ?? 0
            };
            _ = Task.Run(() => _brain.HandleBridgeEventAsync(conn.Guid, botEvent));
        }
    }

    // ==================== Outbound Commands ====================

    public async Task SendToBotAsync(int guid, string type, object payload)
    {
        if (!Connections.TryGetValue(guid, out var conn))
        {
            CircuitTrace.HitNote(guid, "bridge: send dropped, bot not connected", type);
            _logger.LogWarning("BotBridge: cannot send {Type} — bot {Guid} not connected", type, guid);
            return;
        }

        try
        {
            CircuitTrace.HitNote(guid, "bridge: command sent", type);
            await WriteEnvelopeAsync(conn, type, payload, CancellationToken.None);
        }
        catch (Exception ex)
        {
            CircuitTrace.HitNote(guid, "bridge: send failed", type);
            _logger.LogWarning(ex, "BotBridge: send to bot {Guid} failed", guid);
        }
    }

    /// <summary>
    /// Send only to the supplied live connection. HELLO hydration uses this so a
    /// delayed replay from an older socket can never resolve the guid again and
    /// overwrite a newer session's rotation.
    /// </summary>
    public async Task SendToBotConnectionAsync(
        BotConnection expectedConnection,
        string type,
        object payload,
        CancellationToken cancellationToken = default)
    {
        int guid = expectedConnection.Guid;
        await expectedConnection.SendGate.WaitAsync(cancellationToken);
        try
        {
            // Recheck after acquiring the writer gate; replacement may have
            // happened while an earlier message held the stream.
            if (!Connections.TryGetValue(guid, out BotConnection? active)
                || !ReferenceEquals(active, expectedConnection))
            {
                CircuitTrace.Hit(guid, "bridge: exact-connection send refused (superseded)");
                throw new BotNotConnectedException(guid);
            }

            var envelope = new { type, payload };
            byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(envelope, JsonOpts) + "\n");
            await expectedConnection.Stream.WriteAsync(bytes, cancellationToken);
            await expectedConnection.Stream.FlushAsync(cancellationToken);
        }
        finally
        {
            expectedConnection.SendGate.Release();
        }
    }

    /// <summary>
    /// Send one destructive combat-build operation and await only its correlated
    /// core acknowledgement. A timeout/disconnect is returned to the caller and
    /// is never converted into a blind retry (the late ACK can still update live
    /// state, but repeating a talent reset would be unsafe).
    /// </summary>
    public async Task<CombatLoadoutAck> ApplyCombatLoadoutAsync(
        int guid,
        CombatLoadoutBridgeCommand command,
        BotConnection expectedConnection,
        CancellationToken cancellationToken = default)
    {
        if (!Connections.TryGetValue(guid, out var conn)
            || !ReferenceEquals(conn, expectedConnection))
            { CircuitTrace.Hit(guid, "loadout: bot not connected"); throw new BotNotConnectedException(guid); }
        if (!Guid.TryParseExact(command.RequestId, "N", out _))
            throw new ArgumentException("Combat loadout request ids must be GUIDs in N format.", nameof(command));   // cb:fold argument validation
        if (command.SpecTab is < 0 or > 2)
            throw new ArgumentOutOfRangeException(nameof(command), "Specialization slot must be 0-2.");   // cb:fold argument validation
        if (command.ActiveRole is < 1 or > 4)
            throw new ArgumentOutOfRangeException(nameof(command), "Active role must be 1-4.");   // cb:fold argument validation

        string rotationMode = (command.RotationMode ?? "").Trim().ToUpperInvariant();
        if (rotationMode is not ("SPEC" or "CUSTOM"))
            throw new ArgumentException("Rotation mode must be SPEC or CUSTOM.", nameof(command));   // cb:fold argument validation
        if (rotationMode == "CUSTOM"
            && (string.IsNullOrWhiteSpace(command.RotationProfile) || string.IsNullOrWhiteSpace(command.RotationData)))
            throw new ArgumentException("Custom rotations require profile and data.", nameof(command));   // cb:fold argument validation

        var completion = new TaskCompletionSource<CombatLoadoutAck>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new PendingCombatLoadout(guid, conn, completion);
        if (!_pendingCombatLoadouts.TryAdd(
                command.RequestId,
                pending))
            { CircuitTrace.Hit(guid, "loadout: request id already pending"); throw new InvalidOperationException($"Combat loadout request {command.RequestId} is already pending."); }

        try
        {
            // Honor cancellation and then mark the pending request before the
            // final active-socket check. The local check can still prove a
            // no-write bot_offline result, while any concurrent disconnect after
            // this point fails the waiter as outcome_unknown.
            cancellationToken.ThrowIfCancellationRequested();
            Volatile.Write(ref pending.SendAttempted, 1);

            // Keep the exact captured connection: a disconnect/relog between the
            // registration and write must fail this request, not deliver it to a
            // different session and ambiguously await the old one.
            if (!Connections.TryGetValue(guid, out var active) || !ReferenceEquals(active, conn))
                { CircuitTrace.Hit(guid, "loadout: connection lost before write"); throw new BotNotConnectedException(guid); }
            CircuitTrace.Hit(guid, "loadout: destructive write beginning");

            // Once a destructive write begins we keep the correlated
            // waiter alive for its bounded ACK window even if the browser or
            // host request goes away.
            try
            {
                await WriteEnvelopeAsync(conn, "APPLY_COMBAT_LOADOUT", new
                {
                    requestId = command.RequestId,
                    expectedRevision = command.ExpectedRevision,
                    specTab = command.SpecTab,
                    activeRole = command.ActiveRole,
                    resetTalents = command.ResetTalents,
                    rotationMode,
                    rotationProfile = rotationMode == "CUSTOM" ? command.RotationProfile : "",
                    rotationData = rotationMode == "CUSTOM" ? command.RotationData : ""
                }, CancellationToken.None);
            }
            catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
            {
                CircuitTrace.Hit(guid, "loadout: write failed, outcome unknown");
                throw new CombatLoadoutOutcomeUnknownException(guid, command.RequestId, ex);
            }

            try
            {
                return await completion.Task.WaitAsync(CombatLoadoutAckTimeout, CancellationToken.None);
            }
            catch (TimeoutException)
            {
                CircuitTrace.Hit(guid, "loadout: ack timeout");
                throw new CombatLoadoutAckTimeoutException(guid, command.RequestId, CombatLoadoutAckTimeout);
            }
        }
        finally
        {
            _pendingCombatLoadouts.TryRemove(command.RequestId, out _);
        }
    }

    private static async Task WriteEnvelopeAsync(
        BotConnection conn,
        string type,
        object payload,
        CancellationToken cancellationToken)
    {
        var envelope = new { type, payload };
        byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(envelope, JsonOpts) + "\n");

        await conn.SendGate.WaitAsync(cancellationToken);
        try
        {
            await conn.Stream.WriteAsync(bytes, cancellationToken);
            await conn.Stream.FlushAsync(cancellationToken);
        }
        finally
        {
            conn.SendGate.Release();
        }
    }

    public Task SendMoveToAsync(int guid, int mapId, float x, float y, float z)
    {
        return SendToBotAsync(guid, "MOVE_TO", new MoveToPayload
        {
            Guid = guid,
            MapId = mapId,
            X = x,
            Y = y,
            Z = z
        });
    }

    public Task SendSayTextAsync(int guid, string text, int chatType = 0, string? target = null, string? channel = null)
    {
        return SendToBotAsync(guid, "SAY_TEXT", new SayTextPayload
        {
            Guid = guid,
            Text = text,
            ChatType = chatType,
            Target = target,
            Channel = channel
        });
    }

    public async Task SendToAllBotsAsync(string type, object payload)
    {
        foreach (var kvp in Connections)
        {
            await SendToBotAsync(kvp.Key, type, payload);
        }
    }

    // --- Phase 2.5 commands ---

    public Task SendAcceptQuestAsync(int guid, int questId)
    {
        return SendToBotAsync(guid, "ACCEPT_QUEST", new QuestCommandPayload { QuestId = questId });
    }

    public Task SendCompleteQuestAsync(int guid, int questId)
    {
        return SendToBotAsync(guid, "COMPLETE_QUEST", new QuestCommandPayload { QuestId = questId });
    }

    public Task SendAbandonQuestAsync(int guid, int questId)
    {
        return SendToBotAsync(guid, "ABANDON_QUEST", new QuestCommandPayload { QuestId = questId });
    }

    public Task SendLearnSpellAsync(int guid, int spellId)
    {
        return SendToBotAsync(guid, "LEARN_SPELL", new LearnSpellPayload { SpellId = spellId });
    }

    public Task SendAttackTargetAsync(int guid, int targetGuid)
    {
        return SendToBotAsync(guid, "ATTACK_TARGET", new TargetGuidPayload { Guid = targetGuid });
    }

    public Task SendInteractNpcAsync(int guid, int npcGuid)
    {
        return SendToBotAsync(guid, "INTERACT_NPC", new TargetGuidPayload { Guid = npcGuid });
    }

    public Task SendSetTaskGrindAsync(int guid, float x, float y, float z,
        float radius = 40f, int creatureEntry = 0, int killCount = 0)
    {
        return SendToBotAsync(guid, "SET_TASK", new
        {
            task = "GRIND",
            x,
            y,
            z,
            radius,
            creature_entry = creatureEntry,
            kill_count = killCount
        });
    }

    public Task SendSetTaskIdleAsync(int guid)
    {
        return SendToBotAsync(guid, "SET_TASK", new { task = "IDLE" });
    }

    public Task SendTakeFlightAsync(int guid, int sourceNode, int destNode)
    {
        return SendToBotAsync(guid, "TAKE_FLIGHT", new TakeFlightPayload
        {
            SourceNode = sourceNode,
            DestNode = destNode
        });
    }

    public Task SendSellItemsAsync(int guid, int npcEntry, int keepQuality = 2)
    {
        return SendToBotAsync(guid, "SELL_ITEMS", new
        {
            npc_entry = npcEntry,
            keep_quality = keepQuality
        });
    }

    // ==================== Helpers ====================

    /// <summary>
    /// Parse pipe-delimited key=value event data. E.g., "sold=7|copper_earned=432|free_slots=12"
    /// </summary>
    private static Dictionary<string, string> ParsePipeDelimited(string? data)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(data)) return result;   // cb:fold pure helper
        foreach (var segment in data.Split('|'))
        {
            var eq = segment.IndexOf('=');
            if (eq > 0)
                result[segment[..eq].Trim()] = segment[(eq + 1)..].Trim();   // cb:fold pure helper
        }
        return result;
    }

    private static string NormalizeRuntimeToken(string? value, string fallback)
    {
        string token = (value ?? "").Trim().ToLowerInvariant();
        return token.Length == 0 ? fallback : token;
    }

    private static int ParseInt(string? value, int fallback = 0)
        => int.TryParse(value, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : fallback;

    private static uint ParseUInt(string? value, uint fallback = 0)
        => uint.TryParse(value, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out uint parsed)
            ? parsed
            : fallback;

    private static bool ParseBool(string? value)
        => value != null && (value.Equals("1", StringComparison.OrdinalIgnoreCase)
            || value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("yes", StringComparison.OrdinalIgnoreCase));

    private void FailPendingCombatLoadoutsForConnection(BotConnection connection)
    {
        foreach (var entry in _pendingCombatLoadouts.ToArray())
        {
            if (!ReferenceEquals(entry.Value.Connection, connection))
                continue;   // cb:fold iteration filter
            if (_pendingCombatLoadouts.TryRemove(entry.Key, out var removed))
            {
                CircuitTrace.Hit(connection.Guid, "bridge: pending loadout failed for dropped connection");
                Exception error = Volatile.Read(ref removed.SendAttempted) == 0
                    ? new BotNotConnectedException(connection.Guid)
                    : new CombatLoadoutOutcomeUnknownException(connection.Guid, entry.Key);
                removed.Completion.TrySetException(error);
            }
        }
    }

    // ==================== Query ====================

    public List<BotState> GetAllBotStates()
    {
        return BotStates.Values
            .OrderBy(b => b.Name)
            .ToList();
    }

    public BotState? GetBotState(int guid)
    {
        BotStates.TryGetValue(guid, out var state);
        return state;
    }

    public int ConnectedCount => Connections.Count;
    public int TotalTracked => BotStates.Count;

    /// <summary>
    /// Drop a bot's last-known state, e.g. after a DB-level delete. BotStates is a
    /// last-seen-per-guid cache that nothing else ever purges from (disconnect just
    /// removes the live Connections entry, not BotStates) — without this, a deleted
    /// bot lingers forever in /Bots/States and the IBot Monitor page.
    /// </summary>
    public void RemoveBotState(int guid)
    {
        BotStates.TryRemove(guid, out _);
        Connections.TryRemove(guid, out _);
    }
}
