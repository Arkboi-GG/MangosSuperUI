using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MangosSuperUI.Hubs;
using MangosSuperUI.BotLogic.Chat.Coordinator;
using MangosSuperUI.BotLogic.Core;
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

    /// <summary>
    /// Protocol correlation id. C# stamps it on every command and the core
    /// echoes it on terminal EVENT envelopes. It is intentionally top-level so
    /// payload schemas remain unchanged.
    /// </summary>
    [JsonPropertyName("cbt")]
    public long? Cbt { get; set; }
}

public enum CorrelatedSendStatus
{
    Sent,
    DefinitelyNotSent,
    SessionSuperseded,
    OutcomeUnknown
}

public sealed record ExactCreatureCommandDispatch(
    CorrelatedSendStatus Status,
    long CorrelationId,
    string Detail)
{
    public bool Sent => Status == CorrelatedSendStatus.Sent;

    public string StatusCode => Status switch
    {
        CorrelatedSendStatus.Sent => "sent",   // cb:fold pure API token projection
        CorrelatedSendStatus.DefinitelyNotSent => "not_sent",   // cb:fold pure API token projection
        CorrelatedSendStatus.SessionSuperseded => "session_superseded",   // cb:fold pure API token projection
        _ => "outcome_unknown"   // cb:fold pure API token projection
    };
}

// --- Inbound (C++ → C#) ---

public class BotHelloPayload
{
    [JsonPropertyName("bridgeProtocol")]
    public int BridgeProtocol { get; set; }

    /// <summary>
    /// Opaque process-wide C++ circuit identity. Optional for old cores; when
    /// absent the host scopes remote sites to this TCP session instead.
    /// </summary>
    [JsonPropertyName("circuitEpoch")]
    public string CircuitEpoch { get; set; } = "";

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

    // Direct human/free-view possession owns behavioral intent. Explicit
    // operator commands may still be allowed by the core, but the autonomous
    // brain stands down until a fresh STATE clears this latch.
    [JsonPropertyName("possessed")]
    public uint Possessed { get; set; } = 0;

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

// QUEST_INTERACT payload — same shape the planners send via BridgeCommand
// (BridgeHandleQuestInteract requires all three fields, npc_entry within 15yd).
public class QuestInteractPayload
{
    [JsonPropertyName("action")]
    public string Action { get; set; } = ""; // "accept" | "complete"

    [JsonPropertyName("quest_id")]
    public int QuestId { get; set; }

    [JsonPropertyName("npc_entry")]
    public int NpcEntry { get; set; }
}

public class LearnSpellPayload
{
    [JsonPropertyName("spell_id")]
    public int SpellId { get; set; }
}

public class TargetGuidPayload
{
    [JsonPropertyName("entry")]
    public int Entry { get; set; }

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
    public int BridgeProtocol { get; set; }
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
    // Broad runtime-update clock. STATE and control-plane acknowledgements may
    // advance this, so it must never be used to decide whether sensory data is
    // fresh. Use LastStateReceivedUtc for health/combat/position decisions.
    public DateTime LastUpdate { get; set; }
    /// <summary>
    /// Receive time of the last validated STATE envelope. Only HandleStateAsync
    /// may advance this clock; command traffic and EVENT acknowledgements do not.
    /// DateTime.MinValue means this connection has not delivered its first STATE.
    /// </summary>
    public DateTime LastStateReceivedUtc { get; set; }
    /// <summary>
    /// True once the dedicated STATE clock exceeds the brain safety budget. This
    /// remains distinct from TaskState so operators can tell a blind live socket
    /// from an ordinary task and from a fully disconnected bot.
    /// </summary>
    public bool SensoryFeedStale { get; set; }
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
    public bool Possessed { get; set; } = false;
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
    public bool HasReceivedState { get; set; } = false;
}

/// <summary>
/// Tracks a single TCP connection from an AiBotAI instance.
/// </summary>
public class BotConnection
{
    public long SessionId { get; init; }
    public DateTime AcceptedUtc { get; init; } = DateTime.UtcNow;
    public int HelloAccepted;
    public int Guid { get; set; }
    public int BridgeProtocol { get; set; }
    public string CircuitEpoch { get; set; } = "";
    public bool CircuitEpochAdvertised { get; set; }
    public TcpClient Client { get; set; } = null!;
    public NetworkStream Stream { get; set; } = null!;
    public CancellationTokenSource Cts { get; set; } = new();
    public BotState State { get; set; } = new();
    /// <summary>NetworkStream does not support concurrent writers.</summary>
    public SemaphoreSlim SendGate { get; } = new(1, 1);
    public object SensoryFeedGate { get; } = new();

    // The watchdog reads this concurrently with HandleStateAsync. Keeping the
    // receive stamp as ticks lets both sides use Volatile without racing on the
    // mutable BotState projection that is also serialized to the UI.
    public long LastStateReceivedUtcTicks;
    public int SensoryFeedStaleSignaled;
    public int SensoryFeedRecycleStarted;
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
    private static long _nextConnectionSessionId;
    public const int RequiredCorrelatedOutcomeProtocol = 4;
    public const int RequiredTransactionalGroupProtocol = 5;
    public const int RequiredExactCreatureIdentityProtocol = 5;
    private static readonly TimeSpan CombatLoadoutAckTimeout = TimeSpan.FromSeconds(15);

    // C++ emits STATE every five seconds. Three missed beats stop state-dependent
    // planning; six missed beats force-close the server side of the socket so the
    // C++ client cannot leave a half-open session alive indefinitely.
    public static readonly TimeSpan SensoryFeedStaleAfter = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan SensoryFeedRecycleAfter = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan SensoryFeedWatchInterval = TimeSpan.FromSeconds(1);

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
    private readonly object _connectionPublishGate = new();

    // Snapshot of all bot states (survives brief disconnects for UI display)
    public ConcurrentDictionary<int, BotState> BotStates { get; } = new();

    // Admin build changes use a request-id registry independent of BotBrain's
    // singular cbt-correlated planner WAIT.
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

    /// <summary>
    /// Confirms that an inbound event still belongs to the socket currently
    /// published for this bot. The brain repeats this check while holding the
    /// bot's mutation gate: a replacement HELLO can otherwise win while an old
    /// EVENT is awaiting SignalR and let that event resolve the new session's
    /// planner WAIT.
    /// </summary>
    internal bool IsActiveSession(int guid, long sessionId)
        => sessionId != 0
            && Connections.TryGetValue(guid, out BotConnection? active)
            && active.SessionId == sessionId;

    /// <summary>
    /// Apply a combat-loadout acknowledgement as one coherent projection only
    /// while its exact socket still owns the guid. This uses the same lock order
    /// as STATE publication, preventing a superseded ACK from overwriting HELLO
    /// or exposing a partially-updated loadout to snapshot readers.
    /// </summary>
    internal bool TryApplyCombatLoadoutAck(BotConnection conn, CombatLoadoutAck ack)
    {
        lock (conn.SensoryFeedGate)
        {
            lock (_connectionPublishGate)
            {
                if (!Connections.TryGetValue(conn.Guid, out BotConnection? active)
                    || !ReferenceEquals(active, conn))
                {
                    CircuitTrace.Hit(conn.Guid, "bridge: superseded loadout ACK projection rejected");
                    return false;
                }

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
                return true;
            }
        }
    }

    /// <summary>Publish task display state only after the behavioral outcome passed exact cbt admission.</summary>
    internal bool TryApplyAcceptedTaskState(BotConnection conn, string taskState)
    {
        lock (conn.SensoryFeedGate)
        {
            lock (_connectionPublishGate)
            {
                if (!Connections.TryGetValue(conn.Guid, out BotConnection? active)
                    || !ReferenceEquals(active, conn))
                {
                    CircuitTrace.Hit(conn.Guid, "bridge: accepted task state belongs to superseded session, ignored");
                    return false;
                }

                conn.State.TaskState = taskState;
                BotStates[conn.Guid] = conn.State;
                return true;
            }
        }
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
        CancellationTokenSource? sessionCts = null;
        Task? sensoryWatchdog = null;

        try
        {
            var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
            conn = new BotConnection
            {
                SessionId = Interlocked.Increment(ref _nextConnectionSessionId),
                Client = client,
                Stream = stream
            };

            sessionCts = CancellationTokenSource.CreateLinkedTokenSource(appToken, conn.Cts.Token);
            sensoryWatchdog = WatchSensoryFeedAsync(conn, sessionCts.Token);

            while (!sessionCts.Token.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(sessionCts.Token);
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
        catch (ObjectDisposedException) { /* cb:fold watchdog recycled the socket; teardown probe below fires */ }
        catch (Exception ex)
        {
            CircuitTrace.Hit(conn?.Guid ?? 0, "bridge: client loop error");
            _logger.LogWarning(ex, "BotBridge: client error from {Endpoint}", endpoint);
        }
        finally
        {
            // Stop and join the per-socket watchdog before publishing teardown.
            // This keeps it from racing a fast reconnect and marking the new
            // session stale through the old connection's finally path.
            try { sessionCts?.Cancel(); } catch { /* cb:fold disposal trivia */ }
            if (sensoryWatchdog != null)   // cb:fold teardown join; disconnect outcome probed below
            {   // cb:fold teardown join; disconnect outcome probed below
                try { await sensoryWatchdog; }
                catch (OperationCanceledException) { /* cb:fold normal session teardown */ }
            }
            sessionCts?.Dispose();

            if (conn != null && conn.Guid != 0)
            {
                CircuitTrace.Hit(conn.Guid, "bridge: bot disconnected, teardown");
                // A fast relog can replace the dictionary entry before this old
                // socket's finally runs. Only tear down state/requests if this is
                // still the active connection for the guid.
                bool removedActive;
                lock (_connectionPublishGate)
                {
                    removedActive = Connections.TryGetValue(conn.Guid, out var active)
                        && ReferenceEquals(active, conn)
                        && Connections.TryRemove(conn.Guid, out _);

                    if (removedActive)
                    {   // cb:fold exact-session teardown outcome probed immediately below
                        // Publish the disconnected projection while replacement
                        // HELLO publication is excluded by the same gate. A new
                        // session can then atomically overwrite both entries.
                        conn.State.TaskState = "DISCONNECTED";
                        BotStates[conn.Guid] = conn.State;
                    }
                }
                // Pending requests belong to a concrete socket session, not just
                // a guid. Fail this connection's request even if a fast relog has
                // already replaced it in Connections.
                FailPendingCombatLoadoutsForConnection(conn);
                // Keep the last state for UI, but never overwrite a replacement
                // session's projection from this old socket's finally block.
                if (removedActive)
                    CircuitTrace.Hit(conn.Guid, "bridge: state marked DISCONNECTED");

                _logger.LogInformation("BotBridge: bot {Guid} ({Name}) disconnected", conn.Guid, conn.State.Name);
                if (removedActive)
                    await _hub.Clients.All.SendAsync("BotDisconnected", conn.Guid);   // cb:fold notify only, teardown probe above fires
            }

            try { client.Dispose(); } catch { /* cb:fold disposal trivia */ }
        }
    }

    /// <summary>
    /// Per-connection STATE watchdog. Ordinary EVENT/control traffic deliberately
    /// does not feed this clock: a socket that can still read commands or return an
    /// acknowledgement is not necessarily delivering current world senses.
    /// </summary>
    private async Task WatchSensoryFeedAsync(BotConnection conn, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(SensoryFeedWatchInterval);
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            int guid = conn.Guid;
            if (guid == 0)
            {
                if (!HasHelloTimedOut(conn.AcceptedUtc, DateTime.UtcNow))   // cb:fold pre-HELLO watchdog wait; timeout outcome probed below
                    continue;   // cb:fold pre-HELLO watchdog wait; timeout outcome probed below

                CircuitTrace.Hit(0, "bridge: socket recycled before HELLO");
                _logger.LogWarning("BotBridge: recycling socket that sent no HELLO within {Seconds}s",
                    SensoryFeedRecycleAfter.TotalSeconds);
                try { conn.Cts.Cancel(); } catch { /* cb:fold already closing */ }
                try { conn.Client.Client.Shutdown(SocketShutdown.Both); } catch { /* cb:fold already closing */ }
                try { conn.Client.Dispose(); } catch { /* cb:fold already closing */ }
                return;
            }

            if (!Connections.TryGetValue(guid, out BotConnection? active))   // cb:fold publication race; HELLO adoption is probed
                continue;   // cb:fold publication race; HELLO adoption is probed
            if (!ReferenceEquals(active, conn))   // cb:fold supersession outcome probed by replacement/teardown
                return;     // cb:fold supersession outcome probed by replacement/teardown

            DateTime now = DateTime.UtcNow;
            long observedStateTicks = Volatile.Read(ref conn.LastStateReceivedUtcTicks);
            DateTime freshnessOrigin = observedStateTicks > 0
                ? new DateTime(observedStateTicks, DateTimeKind.Utc)
                : conn.State.ConnectedAt;
            TimeSpan age = now > freshnessOrigin ? now - freshnessOrigin : TimeSpan.Zero;

            bool signalStale = false;
            if (age >= SensoryFeedStaleAfter)   // cb:fold stale transition outcome probed when the latch wins below
            {   // cb:fold stale transition outcome probed when latch wins below
                signalStale = TryMarkSensoryFeedStale(conn, observedStateTicks);
            }

            if (signalStale)
            {
                CircuitTrace.Hit(guid, "bridge: sensory feed stale, brain held", age.TotalSeconds);
                _logger.LogWarning(
                    "BotBridge: sensory feed stale for {Name} (guid={Guid}); no STATE for {Age:F1}s, state-dependent planning held",
                    conn.State.Name, guid, age.TotalSeconds);

                // This is a distinct immutable notification rather than another
                // BotStateUpdate: consumers can show the fault without mistaking
                // the cached health/combat/position fields for a fresh snapshot.
                DateTime? lastStateReceivedUtc = conn.State.LastStateReceivedUtc == DateTime.MinValue
                    ? null
                    : conn.State.LastStateReceivedUtc;
                _ = PublishSensoryFeedStaleAsync(
                    guid, conn.State.Name, lastStateReceivedUtc, age);
            }

            if (age < SensoryFeedRecycleAfter)   // cb:fold ordinary watchdog cadence; recycle outcome probed below
                continue;   // cb:fold ordinary watchdog cadence; recycle outcome probed below

            // A STATE may have landed after the age calculation. Re-read the
            // atomic receive stamp before taking the destructive close action.
            lock (conn.SensoryFeedGate)
            {
                if (observedStateTicks != Volatile.Read(ref conn.LastStateReceivedUtcTicks))   // cb:fold STATE-race escape; receive edge probed in HandleStateAsync
                    continue;   // cb:fold STATE-race escape; receive edge probed in HandleStateAsync
                if (Interlocked.CompareExchange(ref conn.SensoryFeedRecycleStarted, 1, 0) != 0)   // cb:fold duplicate recycle suppression; first recycle probed below
                    return;   // cb:fold duplicate recycle suppression; first recycle probed below
                if (observedStateTicks != Volatile.Read(ref conn.LastStateReceivedUtcTicks))   // cb:fold final STATE-race escape; receive edge probed in HandleStateAsync
                {   // cb:fold final STATE-race escape; receive edge probed in HandleStateAsync
                    Volatile.Write(ref conn.SensoryFeedRecycleStarted, 0);
                    continue;
                }
            }

            CircuitTrace.Hit(guid, "bridge: stale sensory socket recycled", age.TotalSeconds);
            _logger.LogWarning(
                "BotBridge: recycling stale sensory socket for {Name} (guid={Guid}) after {Age:F1}s without STATE",
                conn.State.Name, guid, age.TotalSeconds);

            // C++ owns reconnect policy because it is the TCP client. Closing the
            // accepted socket is the bounded server-side action that forces that
            // policy to run instead of preserving a half-open session forever.
            try { conn.Cts.Cancel(); } catch { /* cb:fold disposal trivia */ }
            try { conn.Client.Client.Shutdown(SocketShutdown.Both); } catch { /* cb:fold already half-open; recycle probe precedes close */ }
            try { conn.Client.Dispose(); } catch { /* cb:fold disposal trivia */ }
            return;
        }
    }

    /// <summary>
    /// Atomically publish the stale projection only if the observed socket is
    /// still active and no newer STATE arrived. Kept as one helper so the
    /// replacement race is deterministic under test.
    /// </summary>
    internal bool TryMarkSensoryFeedStale(BotConnection conn, long observedStateTicks)
    {
        lock (conn.SensoryFeedGate)
        {
            // STATE takes these locks in this same order. Revalidate while HELLO
            // publication is excluded so an old watchdog cannot overwrite a
            // replacement session's fresh BotStates entry.
            lock (_connectionPublishGate)
            {
                if (!Connections.TryGetValue(conn.Guid, out BotConnection? stillActive)
                    || !ReferenceEquals(stillActive, conn))
                {
                    CircuitTrace.Hit(conn.Guid, "bridge: superseded watchdog stale publication rejected");
                    return false;
                }

                if (observedStateTicks != Volatile.Read(ref conn.LastStateReceivedUtcTicks)
                    || Interlocked.CompareExchange(ref conn.SensoryFeedStaleSignaled, 1, 0) != 0)
                {
                    CircuitTrace.Hit(conn.Guid, "bridge: watchdog stale latch lost to STATE/already signaled");
                    return false;
                }

                conn.State.SensoryFeedStale = true;
                BotStates[conn.Guid] = conn.State;
                return true;
            }
        }
    }

    private async Task PublishSensoryFeedStaleAsync(
        int guid,
        string name,
        DateTime? lastStateReceivedUtc,
        TimeSpan age)
    {
        try
        {
            await _hub.Clients.All.SendAsync("BotSensoryFeedStale", new
            {
                guid,
                name,
                lastStateReceivedUtc,
                stateAgeSeconds = age.TotalSeconds,
                recycleAfterSeconds = SensoryFeedRecycleAfter.TotalSeconds,
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)   // cb:fold SignalR notification failure; stale state remains authoritative and logged
        {   // cb:fold SignalR notification failure; stale state remains authoritative and logged
            _logger.LogDebug(ex, "BotBridge: failed to publish sensory-feed-stale notification for bot {Guid}", guid);
        }
    }

    // ==================== Inbound Processing ====================

    private const int MaxCircuitEpochLength = 128;

    /// <summary>
    /// A TCP connection owns exactly one HELLO identity. Re-adopting either the
    /// guid or circuit epoch without a new session would defeat every downstream
    /// stale-session check, so duplicate HELLOs fail closed before mutation.
    /// </summary>
    internal static bool TryClaimHelloIdentity(
        BotConnection conn,
        int guid,
        out string rejection)
    {
        if (guid <= 0)
        {   // cb:fold HELLO identity classifier; caller probes rejection
            rejection = "invalid_guid";
            return false;
        }
        if (conn.Guid != 0
            || Interlocked.CompareExchange(ref conn.HelloAccepted, 1, 0) != 0)
        {   // cb:fold HELLO identity classifier; caller probes rejection
            rejection = "hello_already_accepted";
            return false;
        }

        rejection = "";
        return true;
    }

    /// <summary>
    /// Adopt the C++ process epoch advertised by HELLO. Old cores deliberately
    /// receive a per-socket identity: reconnecting may duplicate the manifest,
    /// but can never reinterpret an earlier process's numeric site ids.
    /// </summary>
    internal static void AdoptCircuitEpoch(BotConnection conn, string? advertisedEpoch)
    {
        if (IsValidCircuitEpoch(advertisedEpoch))
        {
            CircuitTrace.HitNote(conn.Guid, "bridge: HELLO circuit epoch adopted", advertisedEpoch!);
            conn.CircuitEpoch = advertisedEpoch!;
            conn.CircuitEpochAdvertised = true;
            return;
        }

        conn.CircuitEpoch = $"legacy-session-{conn.SessionId}";
        conn.CircuitEpochAdvertised = false;
    }

    /// <summary>
    /// Bind each circuit payload to its HELLO identity. Epoch-aware clients must
    /// echo the exact value; missing or mismatched values are quarantined. A
    /// legacy connection may omit it and uses its synthetic session epoch.
    /// </summary>
    internal static bool TryResolveCircuitEpoch(
        JsonElement payload,
        BotConnection conn,
        out string epoch,
        out string rejection)
    {
        epoch = "";
        rejection = "";
        if (conn.Guid == 0 || string.IsNullOrEmpty(conn.CircuitEpoch))
        {
            CircuitTrace.Hit(conn.Guid, "bridge: circuit epoch rejected before HELLO identity");
            rejection = "hello_required";
            return false;
        }

        JsonElement suppliedElement = default;
        bool propertyPresent = payload.ValueKind == JsonValueKind.Object
            && payload.TryGetProperty("circuitEpoch", out suppliedElement);
        string? supplied = propertyPresent && suppliedElement.ValueKind == JsonValueKind.String
            ? suppliedElement.GetString()
            : null;

        if (conn.CircuitEpochAdvertised)
        {
            CircuitTrace.Hit(conn.Guid, "bridge: validating advertised circuit epoch");
            if (!propertyPresent || !IsValidCircuitEpoch(supplied))
            {
                CircuitTrace.Hit(conn.Guid, "bridge: advertised circuit epoch missing or invalid");
                rejection = "epoch_missing_or_invalid";
                return false;
            }
            if (!string.Equals(supplied, conn.CircuitEpoch, StringComparison.Ordinal))
            {
                CircuitTrace.Hit(conn.Guid, "bridge: advertised circuit epoch mismatch");
                rejection = "epoch_mismatch";
                return false;
            }
        }
        else if (propertyPresent
            && (suppliedElement.ValueKind != JsonValueKind.String
                || !string.IsNullOrEmpty(supplied)))
        {
            CircuitTrace.Hit(conn.Guid, "bridge: legacy socket tried to introduce a late circuit epoch");
            // Identity may only be established by HELLO. Accepting a later epoch
            // would splice two namespaces into one legacy socket.
            rejection = "epoch_not_declared_by_hello";
            return false;
        }

        epoch = conn.CircuitEpoch;
        return true;
    }

    private static bool IsValidCircuitEpoch(string? value)
        => !string.IsNullOrWhiteSpace(value)
            && value.Length <= MaxCircuitEpochLength
            && !value.Any(char.IsControl);

    private bool IsActiveCircuitConnection(BotConnection conn)
        => conn.Guid != 0
            && Connections.TryGetValue(conn.Guid, out BotConnection? active)
            && ReferenceEquals(active, conn);

    private bool TryAdmitCircuitPayload(
        JsonElement payload,
        BotConnection conn,
        out string epoch)
    {
        epoch = "";
        if (payload.ValueKind != JsonValueKind.Object)
        {
            CircuitTrace.Hit(conn.Guid, "bridge: non-object circuit payload rejected");
            return false;
        }
        if (!IsActiveCircuitConnection(conn))
        {
            CircuitTrace.Hit(conn.Guid, "bridge: circuit payload from superseded or pre-HELLO connection rejected");
            return false;
        }
        if (TryResolveCircuitEpoch(payload, conn, out epoch, out string rejection))
        {
            CircuitTrace.Hit(conn.Guid, "bridge: circuit payload epoch admitted");
            return true;
        }

        CircuitTrace.HitNote(conn.Guid, "bridge: circuit payload epoch rejected", rejection);
        _logger.LogWarning(
            "BotBridge: rejected circuit payload for bot {Guid}: {Reason} (HELLO epoch={Epoch})",
            conn.Guid,
            rejection,
            conn.CircuitEpoch);
        return false;
    }

    /// <summary>
    /// Commit a parsed remote manifest row only while this exact connection owns
    /// the guid. The HELLO publication gate closes the admission/commit race: a
    /// replacement either waits for this commit or wins first and rejects it.
    /// </summary>
    internal bool TryCommitCircuitSite(
        BotConnection conn,
        string epoch,
        int remoteId,
        string file,
        int line,
        string description,
        out CircuitTrace.RemoteSiteRegistration registration)
    {
        registration = default;
        lock (_connectionPublishGate)
        {
            if (!Connections.TryGetValue(conn.Guid, out BotConnection? active)
                || !ReferenceEquals(active, conn)
                || !string.Equals(epoch, conn.CircuitEpoch, StringComparison.Ordinal))
                return false;   // cb:fold atomic commit classifier; caller probes superseded SITE rejection

            registration = CircuitTrace.RegisterRemoteSite(
                epoch,
                remoteId,
                file,
                line,
                description);
            return true;
        }
    }

    /// <summary>Atomically admit and append one parsed remote segment.</summary>
    internal bool TryCommitCircuitBatch(
        BotConnection conn,
        string epoch,
        int batchGuid,
        int mapId,
        int zoneId,
        float x,
        float y,
        float z,
        List<(int RemoteId, double? Value, string? Note)> hits,
        int drops,
        out CircuitTrace.RemoteIngestResult result)
    {
        result = default;
        lock (_connectionPublishGate)
        {
            if (!Connections.TryGetValue(conn.Guid, out BotConnection? active)
                || !ReferenceEquals(active, conn)
                || batchGuid != conn.Guid
                || !string.Equals(epoch, conn.CircuitEpoch, StringComparison.Ordinal))
                return false;   // cb:fold atomic commit classifier; caller probes superseded BATCH rejection

            result = CircuitTrace.IngestRemoteSegment(
                epoch,
                batchGuid,
                mapId,
                zoneId,
                x,
                y,
                z,
                hits,
                drops);
            return true;
        }
    }

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
                await HandleEventAsync(msg.Payload, msg.Cbt, conn);
                break;

            case "CIRCUIT_SITE":   // cb:fold instrument plumbing, not bot routing
                {
                    var p = msg.Payload;
                    if (!TryAdmitCircuitPayload(p, conn, out string epoch))
                    {
                        CircuitTrace.Hit(conn.Guid, "bridge: CIRCUIT_SITE admission rejected");
                        break;
                    }
                    if (!p.TryGetProperty("id", out JsonElement idElement)
                        || !idElement.TryGetInt32(out int remoteId)
                        || remoteId <= 0
                        || !p.TryGetProperty("file", out JsonElement fileElement)
                        || fileElement.ValueKind != JsonValueKind.String
                        || !p.TryGetProperty("line", out JsonElement lineElement)
                        || !lineElement.TryGetInt32(out int line)
                        || line <= 0
                        || !p.TryGetProperty("desc", out JsonElement descElement)
                        || descElement.ValueKind != JsonValueKind.String)
                    {
                        CircuitTrace.Hit(conn.Guid, "bridge: malformed circuit site rejected");
                        _logger.LogWarning("BotBridge: malformed CIRCUIT_SITE from bot {Guid}", conn.Guid);
                        break;
                    }

                    if (!TryCommitCircuitSite(
                            conn,
                            epoch,
                            remoteId,
                            fileElement.GetString() ?? "?",
                            line,
                            descElement.GetString() ?? "?",
                            out CircuitTrace.RemoteSiteRegistration registration))
                    {
                        CircuitTrace.Hit(conn.Guid, "bridge: circuit site superseded before commit");
                        break;
                    }
                    if (registration == CircuitTrace.RemoteSiteRegistration.Conflict)
                    {
                        CircuitTrace.Hit(conn.Guid, "bridge: circuit site id conflict quarantined", remoteId);
                        _logger.LogError(
                            "BotBridge: quarantined conflicting C++ circuit site id {RemoteId} in epoch {Epoch}",
                            remoteId,
                            epoch);
                    }
                    break;
                }

            case "CIRCUIT_BATCH":   // cb:fold instrument plumbing, not bot routing
                {
                    var p = msg.Payload;
                    if (!TryAdmitCircuitPayload(p, conn, out string epoch))
                    {
                        CircuitTrace.Hit(conn.Guid, "bridge: CIRCUIT_BATCH admission rejected");
                        break;
                    }
                    if (!p.TryGetProperty("guid", out JsonElement guidElement)
                        || !guidElement.TryGetInt32(out int batchGuid)
                        || batchGuid != conn.Guid)
                    {
                        CircuitTrace.Hit(conn.Guid, "bridge: circuit batch guid mismatch rejected");
                        _logger.LogWarning(
                            "BotBridge: rejected CIRCUIT_BATCH guid mismatch on bot {Guid}",
                            conn.Guid);
                        break;
                    }
                    var hits = new List<(int, double?, string?)>();
                    if (!p.TryGetProperty("h", out JsonElement hitElement)
                        || hitElement.ValueKind != JsonValueKind.Array)
                    {
                        CircuitTrace.Hit(conn.Guid, "bridge: malformed circuit batch rejected");
                        break;
                    }
                    bool malformedHit = false;
                    foreach (var h in hitElement.EnumerateArray())
                    {
                        if (h.ValueKind != JsonValueKind.Array
                            || h.GetArrayLength() < 1
                            || !h[0].TryGetInt32(out int id)
                            || id <= 0)
                        {
                            CircuitTrace.Hit(conn.Guid, "bridge: malformed circuit hit rejected");
                            malformedHit = true;
                            break;
                        }
                        double? val = h.GetArrayLength() > 1 && h[1].ValueKind == System.Text.Json.JsonValueKind.Number ? h[1].GetDouble() : null;
                        string? note = h.GetArrayLength() > 2 && h[2].ValueKind == System.Text.Json.JsonValueKind.String ? h[2].GetString() : null;
                        hits.Add((id, val, note));
                    }
                    if (malformedHit
                        || !p.TryGetProperty("map", out JsonElement mapElement)
                        || !mapElement.TryGetInt32(out int mapId)
                        || !p.TryGetProperty("zone", out JsonElement zoneElement)
                        || !zoneElement.TryGetInt32(out int zoneId)
                        || !p.TryGetProperty("x", out JsonElement xElement)
                        || !xElement.TryGetSingle(out float x)
                        || !p.TryGetProperty("y", out JsonElement yElement)
                        || !yElement.TryGetSingle(out float y)
                        || !p.TryGetProperty("z", out JsonElement zElement)
                        || !zElement.TryGetSingle(out float z))
                    {
                        CircuitTrace.Hit(conn.Guid, "bridge: malformed circuit batch rejected");
                        _logger.LogWarning("BotBridge: malformed CIRCUIT_BATCH from bot {Guid}", conn.Guid);
                        break;
                    }
                    int drops = p.TryGetProperty("drops", out JsonElement dropsElement)
                        && dropsElement.TryGetInt32(out int parsedDrops)
                        ? Math.Max(0, parsedDrops)
                        : 0;
                    if (!TryCommitCircuitBatch(
                            conn,
                            epoch,
                            batchGuid,
                            mapId,
                            zoneId,
                            x,
                            y,
                            z,
                            hits,
                            drops,
                            out CircuitTrace.RemoteIngestResult result))
                    {
                        CircuitTrace.Hit(conn.Guid, "bridge: circuit batch superseded before commit");
                        break;
                    }
                    if (result.UnknownSites > 0 || result.ConflictedSites > 0)
                    {
                        CircuitTrace.Hit(
                            conn.Guid,
                            "bridge: circuit batch sites quarantined",
                            result.UnknownSites + result.ConflictedSites);
                        _logger.LogWarning(
                            "BotBridge: circuit batch for bot {Guid}, epoch {Epoch} used {Unknown} unregistered and {Conflicted} conflicting sites",
                            conn.Guid,
                            epoch,
                            result.UnknownSites,
                            result.ConflictedSites);
                    }
                    break;
                }

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
        if (!TryClaimHelloIdentity(conn, hello.Guid, out string helloRejection))
        {
            CircuitTrace.HitNote(conn.Guid, "bridge: HELLO identity rejected", helloRejection);
            _logger.LogWarning(
                "BotBridge: rejected HELLO on session {SessionId}: {Reason} (current guid={CurrentGuid}, supplied guid={SuppliedGuid})",
                conn.SessionId,
                helloRejection,
                conn.Guid,
                hello.Guid);
            return;
        }

        CircuitTrace.Hit(hello.Guid, "bridge: HELLO adopted, bot registered", hello.Level);
        DateTime connectedUtc = DateTime.UtcNow;
        conn.Guid = hello.Guid;
        conn.BridgeProtocol = hello.BridgeProtocol;
        AdoptCircuitEpoch(conn, hello.CircuitEpoch);
        if (conn.CircuitEpochAdvertised)
            CircuitTrace.HitNote(hello.Guid, "bridge: C++ circuit epoch adopted", conn.CircuitEpoch);
        else
            CircuitTrace.HitNote(hello.Guid, "bridge: legacy circuit epoch synthesized", conn.CircuitEpoch);
        Volatile.Write(ref conn.LastStateReceivedUtcTicks, 0);
        Volatile.Write(ref conn.SensoryFeedStaleSignaled, 0);
        Volatile.Write(ref conn.SensoryFeedRecycleStarted, 0);
        conn.State = new BotState
        {
            Guid = hello.Guid,
            BridgeProtocol = hello.BridgeProtocol,
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
            ConnectedAt = connectedUtc,
            LastUpdate = connectedUtc,
            LastStateReceivedUtc = DateTime.MinValue,
            SensoryFeedStale = false
        };

        // Register the exact-connection hydration barrier before making this
        // socket discoverable. Any loadout request that can see the connection
        // can therefore also see and await its persisted rotation replay.
        RotationService.HelloHydrationRegistration? rotationHydration =
            _rotations?.RegisterHelloHydration(conn, hello.Name);
        BotConnection? replacedConnection = null;
        try
        {
            lock (_connectionPublishGate)
            {
                Connections.TryGetValue(hello.Guid, out replacedConnection);
                Connections[hello.Guid] = conn;
                BotStates[hello.Guid] = conn.State;
            }
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

        if (replacedConnection != null && !ReferenceEquals(replacedConnection, conn))
        {
            CircuitTrace.Hit(conn.Guid, "bridge: prior socket superseded and closed");
            try { replacedConnection.Cts.Cancel(); } catch { /* cb:fold disposal trivia */ }
            try { replacedConnection.Client.Dispose(); } catch { /* cb:fold disposal trivia */ }
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

        // [CIRCUIT] Push the current recording state to the freshly-connected C++ side
        // (R6 — one switch arms both probes; a reconnect must re-learn mode + ship).
        if (CircuitTrace.Mode != CircuitTrace.TraceMode.Off)
        {
            CircuitTrace.Hit(conn.Guid, "bridge: circuit state pushed on hello");
            _ = SendToBotAsync(hello.Guid, "CIRCUIT_TRACE",
                new { mode = 1, ship = CircuitTrace.IsArmed(hello.Guid) ? 1 : 0 });
        }
    }

    private async Task HandleStateAsync(JsonElement payload, BotConnection conn)
    {
        var state = payload.Deserialize<BotStatePayload>(JsonOpts);
        if (state == null) { CircuitTrace.Hit(conn.Guid, "bridge: STATE payload malformed"); return; }
        if (conn.Guid == 0 || state.Guid != conn.Guid)
        {
            CircuitTrace.HitNote(conn.Guid, "bridge: STATE guid mismatch dropped", state.Guid.ToString());
            _logger.LogWarning(
                "BotBridge: dropped STATE guid={StateGuid} on connection owned by guid={ConnectionGuid}",
                state.Guid, conn.Guid);
            return;
        }
        if (!Connections.TryGetValue(conn.Guid, out BotConnection? active)
            || !ReferenceEquals(active, conn))
        {
            CircuitTrace.Hit(conn.Guid, "bridge: STATE from superseded connection ignored");
            return;
        }
        DateTime receivedUtc = DateTime.UtcNow;
        BotState bs;
        bool feedRecovered;
        lock (conn.SensoryFeedGate)
        {
            if (Volatile.Read(ref conn.SensoryFeedRecycleStarted) != 0)
            {
                // The watchdog already committed to closing this stale session.
                // The reconnect's first STATE will establish fresh truth.
                CircuitTrace.Hit(conn.Guid, "bridge: STATE raced committed recycle, ignored");
                return;
            }

        CircuitTrace.Hit(conn.Guid, "bridge: STATE heartbeat applied");
        bs = conn.State;
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
        bs.LastUpdate = receivedUtc;
        bs.QuestId = state.QuestId;
        bs.QuestStatus = state.QuestStatus;
        bs.Durability = state.Durability;
        bs.InPlayerParty = state.Pparty != 0;   // [PLAYERPARTY] pparty on STATE (2026-07-07)
        bs.Possessed = state.Possessed != 0;
        bs.Conscripted = state.Conscripted != 0;   // [CONSCRIPTED] conscripted on STATE (2026-08-24)
        bs.PartyBossDist = state.Ppdist;        // [HUB-ERRAND] ppdist on STATE (2026-07-08); HubErrandUntil deliberately NOT copied — it persists
        bs.Quests = state.Quests;   // full quest-log snapshot (retired pull → STATE is the single source of truth)
        bs.HasReceivedState = true;
        bs.LastStateReceivedUtc = receivedUtc;
        feedRecovered = Interlocked.Exchange(ref conn.SensoryFeedStaleSignaled, 0) != 0
            || bs.SensoryFeedStale;
        bs.SensoryFeedStale = false;

            lock (_connectionPublishGate)
            {
                if (!Connections.TryGetValue(conn.Guid, out BotConnection? stillActive)
                    || !ReferenceEquals(stillActive, conn))
                {
                    CircuitTrace.Hit(conn.Guid, "bridge: completed STATE from superseded connection discarded");
                    return;
                }
                BotStates[conn.Guid] = bs;
            }

            // Publish both freshness clocks only after the complete projection is
            // visible under SensoryFeedGate. Snapshot readers take this same gate,
            // so no brain tick can observe a half-old/half-new heartbeat.
            Volatile.Write(ref conn.LastStateReceivedUtcTicks, receivedUtc.Ticks);
        }

        if (feedRecovered)
        {
            CircuitTrace.Hit(conn.Guid, "bridge: sensory feed recovered");
            _logger.LogInformation(
                "BotBridge: sensory feed recovered for {Name} (guid={Guid})",
                bs.Name, conn.Guid);
        }

        await _hub.Clients.All.SendAsync("BotStateUpdate", bs);
    }

    private async Task HandleEventAsync(JsonElement payload, long? cbt, BotConnection conn)
    {
        // A replaced socket may still have already-buffered lines. Never allow
        // its late outcomes into the active session's brain/context.
        if (!Connections.TryGetValue(conn.Guid, out var active)
            || !ReferenceEquals(active, conn))
        {
            CircuitTrace.Hit(conn.Guid, "bridge: EVENT ignored from superseded connection");
            return;
        }

        var evt = payload.Deserialize<BotEventPayload>(JsonOpts);
        if (evt == null) { CircuitTrace.Hit(conn.Guid, "bridge: EVENT payload malformed"); return; }

        var eventType = evt.Event?.ToUpperInvariant() ?? "";
        CircuitTrace.HitNote(conn.Guid, "bridge: event received", eventType);

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

                    if (!TryApplyCombatLoadoutAck(conn, ack))
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

            case "MOVE_POINT_REFUSED":
                // Telemetry only: an autonomous wander/patrol/combat candidate
                // hop found no path. It must never negate a planner WAIT or feed
                // the durable MOVE_FAILED destination streak.
                CircuitTrace.HitNote(conn.Guid, "bridge: autonomous move point refused", evt.Data ?? "");
                goto default;

            case "ATTACK_TARGET_FAIL":
                // Operator-only, but explicitly named so the boundary inventory and UI both retain
                // the core's correlated validation/not-found verdict instead of treating it as silence.
                CircuitTrace.HitNote(conn.Guid, "bridge: attack target rejected", evt.Data ?? "");
                goto default;

            default:   // cb:fold UI forward only, event probed at method entry
                _logger.LogInformation("BotBridge: EVENT {Event} from {Name} (guid={Guid}): {Data}",
                    evt.Event, conn.State.Name, conn.Guid, evt.Data);
                await _hub.Clients.All.SendAsync("BotEvent", new
                {
                    guid = conn.Guid,
                    name = conn.State.Name,
                    eventType = evt.Event,
                    data = evt.Data,
                    cbt,
                    timestamp = DateTime.UtcNow
                });
                break;
        }

        // Route to behavioral engine (if wired)
        if (_brain != null)
        {
            // SignalR forwarding above can yield long enough for a replacement
            // HELLO to publish a new socket. Avoid queueing obviously stale work;
            // BotBrainService revalidates again inside MutationGate to close the
            // remaining check-to-dispatch race.
            if (!IsActiveSession(conn.Guid, conn.SessionId))
            {
                CircuitTrace.Hit(conn.Guid, "bridge: EVENT superseded before brain route");
                return;
            }

            CircuitTrace.Hit(conn.Guid, "bridge: event routed to brain");
            var botEvent = new MangosSuperUI.BotLogic.Core.BotEvent
            {
                BridgeSessionId = conn.SessionId,
                CorrelationId = cbt,
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
            // Preserve TCP order and await the context's single-writer gate so a
            // planner tick cannot replace Pending halfway through this outcome.
            bool handled = await _brain.HandleBridgeEventAsync(conn.Guid, botEvent);
            if (handled && eventType == "TASK_COMPLETE")
                TryApplyAcceptedTaskState(conn, "IDLE");   // cb:fold accepted projection; helper probes supersession and executor probes exact outcome
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

            long cbt = BridgeCorrelation.NextId();
            CircuitTrace.Hit(guid, "chain: command sent", cbt);
            var envelope = new { type, payload, cbt };
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
        CancellationToken cancellationToken,
        long? correlationId = null)
    {
        long cbt = correlationId ?? BridgeCorrelation.NextId();
        CircuitTrace.Hit(conn.Guid, "chain: command sent", cbt);
        var envelope = new { type, payload, cbt };
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

    // Manual accept/complete ride the same QUEST_INTERACT verb the planners use — the retired
    // v2 ACCEPT_QUEST/COMPLETE_QUEST verbs have no C++ dispatch anymore. C++ requires the named
    // npc_entry alive within 15yd of the bot; callers resolve it from the quest graph and a miss
    // comes back as a QUEST_INTERACT_FAIL event, not a silent drop.
    public Task SendAcceptQuestAsync(int guid, int questId, int npcEntry)
    {
        return SendToBotAsync(guid, "QUEST_INTERACT", new QuestInteractPayload { Action = "accept", QuestId = questId, NpcEntry = npcEntry });
    }

    public Task SendCompleteQuestAsync(int guid, int questId, int npcEntry)
    {
        return SendToBotAsync(guid, "QUEST_INTERACT", new QuestInteractPayload { Action = "complete", QuestId = questId, NpcEntry = npcEntry });
    }

    public Task SendAbandonQuestAsync(int guid, int questId)
    {
        return SendToBotAsync(guid, "ABANDON_QUEST", new QuestCommandPayload { QuestId = questId });
    }

    public Task SendLearnSpellAsync(int guid, int spellId)
    {
        return SendToBotAsync(guid, "LEARN_SPELL", new LearnSpellPayload { SpellId = spellId });
    }

    public Task<ExactCreatureCommandDispatch> SendAttackTargetAsync(int guid, int targetEntry, int targetGuid)
    {
        return SendExactCreatureCommandAsync(guid, "ATTACK_TARGET", new TargetGuidPayload
        {
            Entry = targetEntry,
            Guid = targetGuid
        });
    }

    public Task<ExactCreatureCommandDispatch> SendInteractNpcAsync(int guid, int npcEntry, int npcGuid)
    {
        return SendExactCreatureCommandAsync(guid, "INTERACT_NPC", new TargetGuidPayload
        {
            Entry = npcEntry,
            Guid = npcGuid
        });
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

    private BotConnection RequireExactCreatureIdentityProtocol(int guid, string command)
    {
        if (!Connections.TryGetValue(guid, out BotConnection? connection))
        {
            CircuitTrace.HitNote(guid, "bridge: exact creature command refused, bot disconnected", command);
            throw new BotNotConnectedException(guid);
        }
        if (connection.BridgeProtocol < RequiredExactCreatureIdentityProtocol)
        {
            CircuitTrace.Hit(guid, "bridge: exact creature command refused, protocol too old", connection.BridgeProtocol);
            throw new InvalidOperationException(
                $"Bot {guid} advertises bridge protocol {connection.BridgeProtocol}; {command} requires protocol {RequiredExactCreatureIdentityProtocol} exact creature identity.");
        }
        return connection;
    }

    /// <summary>
    /// Send an exact-creature operator command to the same protocol-v5 session that passed
    /// admission. The receipt confirms only a complete socket write, never core execution;
    /// the correlated EVENT remains authoritative for any explicit execution failure.
    /// </summary>
    private async Task<ExactCreatureCommandDispatch> SendExactCreatureCommandAsync(
        int guid,
        string command,
        TargetGuidPayload payload)
    {
        BotConnection connection;
        try
        {
            connection = RequireExactCreatureIdentityProtocol(guid, command);
        }
        catch (InvalidOperationException ex)
        {
            CircuitTrace.HitNote(guid, "bridge: exact creature command definitely not sent", command);
            return new ExactCreatureCommandDispatch(
                CorrelatedSendStatus.DefinitelyNotSent,
                0,
                ex.Message);
        }

        long cbt = BridgeCorrelation.NextId();
        var envelope = new { type = command, payload, cbt };
        byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(envelope, JsonOpts) + "\n");
        bool writeStarted = false;

        await connection.SendGate.WaitAsync(CancellationToken.None);
        try
        {
            // Admission and write must name one connection. A GUID lookup here is only a
            // revalidation; it must never redirect the command onto a replacement socket.
            if (!Connections.TryGetValue(guid, out BotConnection? active)
                || !ReferenceEquals(active, connection))
            {
                CircuitTrace.Hit(guid, "bridge: exact creature command refused, session superseded", cbt);
                return new ExactCreatureCommandDispatch(
                    CorrelatedSendStatus.SessionSuperseded,
                    cbt,
                    "session_superseded_before_send");
            }

            CircuitTrace.Hit(guid, "chain: exact creature command write started", cbt);
            writeStarted = true;
            await connection.Stream.WriteAsync(bytes, CancellationToken.None);
            await connection.Stream.FlushAsync(CancellationToken.None);
            return new ExactCreatureCommandDispatch(CorrelatedSendStatus.Sent, cbt, "sent");
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            CorrelatedSendStatus status = writeStarted
                ? CorrelatedSendStatus.OutcomeUnknown
                : CorrelatedSendStatus.DefinitelyNotSent;
            CircuitTrace.Hit(guid, "bridge: exact creature command write failed", cbt);
            _logger.LogWarning(
                ex,
                "BotBridge: {Command} write failed for bot {Guid} (cbt={Cbt}, status={Status})",
                command,
                guid,
                cbt,
                status);
            return new ExactCreatureCommandDispatch(
                status,
                cbt,
                status == CorrelatedSendStatus.OutcomeUnknown
                    ? "write_outcome_unknown"
                    : "definitely_not_sent");
        }
        finally
        {
            connection.SendGate.Release();
        }
    }

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

    /// <summary>
    /// Write a WAIT command with the correlation id already installed on the
    /// caller's Outstanding record. Distinguishes a definite pre-write failure
    /// (safe to release) from an indeterminate partial write (retain to deadline).
    /// </summary>
    public async Task<CorrelatedSendStatus> TrySendCorrelatedAsync(
        int guid,
        string type,
        object payload,
        long correlationId,
        long expectedSessionId,
        CancellationToken cancellationToken = default)
    {
        if (correlationId <= 0)   // cb:fold public argument validation; no bot guid is trustworthy yet
            throw new ArgumentOutOfRangeException(nameof(correlationId), "Bridge correlation ids must be positive.");   // cb:fold public argument validation

        if (!Connections.TryGetValue(guid, out var conn))
        {
            CircuitTrace.HitNote(guid, "bridge: correlated send dropped, bot not connected", type);
            _logger.LogWarning("BotBridge: cannot send correlated {Type} — bot {Guid} not connected", type, guid);
            return CorrelatedSendStatus.SessionSuperseded;
        }

        if (expectedSessionId <= 0 || conn.SessionId != expectedSessionId)
        {
            CircuitTrace.Hit(guid, "bridge: correlated send refused (snapshot session replaced)", correlationId);
            _logger.LogWarning(
                "BotBridge: refusing correlated {Type} for bot {Guid}; sensed session={Expected}, active session={Active}",
                type, guid, expectedSessionId, conn.SessionId);
            return CorrelatedSendStatus.SessionSuperseded;
        }

        if (conn.BridgeProtocol < RequiredCorrelatedOutcomeProtocol)
        {
            CircuitTrace.Hit(guid, "bridge: correlated send refused (protocol too old)", conn.BridgeProtocol);
            _logger.LogError(
                "BotBridge: refusing WAIT command {Type} for bot {Guid}; core bridgeProtocol={Actual}, required={Required}",
                type, guid, conn.BridgeProtocol, RequiredCorrelatedOutcomeProtocol);
            return CorrelatedSendStatus.DefinitelyNotSent;
        }

        bool writeStarted = false;
        try
        {
            var envelope = new { type, payload, cbt = correlationId };
            byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(envelope, JsonOpts) + "\n");

            await conn.SendGate.WaitAsync(cancellationToken);
            try
            {
                // A reconnect must not redirect a WAIT armed for one session
                // onto a replacement socket.
                if (!Connections.TryGetValue(guid, out var active)
                    || !ReferenceEquals(active, conn))
                {
                    CircuitTrace.Hit(guid, "bridge: correlated send refused (superseded)", correlationId);
                    return CorrelatedSendStatus.SessionSuperseded;
                }

                CircuitTrace.Hit(guid, "chain: correlated command sent", correlationId);
                writeStarted = true;
                await conn.Stream.WriteAsync(bytes, cancellationToken);
                await conn.Stream.FlushAsync(cancellationToken);
                return CorrelatedSendStatus.Sent;
            }
            finally
            {
                conn.SendGate.Release();
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException
                                   or OperationCanceledException)
        {
            CircuitTrace.Hit(guid, "bridge: correlated send failed", correlationId);
            var status = writeStarted
                ? CorrelatedSendStatus.OutcomeUnknown
                : CorrelatedSendStatus.DefinitelyNotSent;
            _logger.LogWarning(ex, "BotBridge: correlated send to bot {Guid} failed (cbt={Cbt}, status={Status})",
                guid, correlationId, status);
            return status;
        }
    }

    /// <summary>
    /// Age of the sensory feed. Before the first STATE, connection time is the
    /// hydration baseline so a socket that never hydrates is still bounded.
    /// </summary>
    public static TimeSpan GetSensoryFeedAge(BotState state, DateTime utcNow)
    {
        DateTime origin = state.LastStateReceivedUtc != DateTime.MinValue
            ? state.LastStateReceivedUtc
            : state.ConnectedAt;
        return origin == DateTime.MinValue || utcNow <= origin
            ? TimeSpan.Zero
            : utcNow - origin;
    }

    /// <summary>True only when at least one STATE landed within the safety budget.</summary>
    public static bool HasFreshSensoryState(BotState state, DateTime utcNow)
        => state.HasReceivedState
            && state.LastStateReceivedUtc != DateTime.MinValue
            && GetSensoryFeedAge(state, utcNow) < SensoryFeedStaleAfter;

    /// <summary>
    /// True after the safety budget even if the first STATE never arrived. This
    /// is intentionally independent of LastUpdate/control-plane traffic.
    /// </summary>
    public static bool IsSensoryFeedStale(BotState state, DateTime utcNow)
        => GetSensoryFeedAge(state, utcNow) >= SensoryFeedStaleAfter;

    /// <summary>Bounds accepted sockets that never identify with HELLO.</summary>
    public static bool HasHelloTimedOut(DateTime acceptedUtc, DateTime utcNow)
        => acceptedUtc != DateTime.MinValue
            && utcNow - acceptedUtc >= SensoryFeedRecycleAfter;

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

    /// <summary>
    /// Capture one internally consistent sensory heartbeat. HandleStateAsync
    /// writes under the same gate, so health/combat/position/quest fields cannot
    /// straddle two STATE envelopes in a planner snapshot.
    /// </summary>
    public BotStateSnapshot? GetBotStateSnapshot(int guid)
    {
        if (!Connections.TryGetValue(guid, out BotConnection? conn))   // cb:fold query boundary; callers own disconnected handling
            return null;   // cb:fold query boundary; callers own disconnected handling

        lock (conn.SensoryFeedGate)
        {
            if (!Connections.TryGetValue(guid, out BotConnection? active)
                || !ReferenceEquals(active, conn))   // cb:fold replacement boundary; bridge lifecycle probes the swap
                return null;   // cb:fold replacement boundary; bridge lifecycle probes the swap

            BotStateSnapshot snapshot = BotStateSnapshot.FromBridgeState(conn.State);
            snapshot.BridgeSessionId = conn.SessionId;
            return snapshot;
        }
    }

    /// <summary>
    /// Commit position carried by an accepted, correlated positive outcome. The
    /// caller holds BotContext.MutationGate, so the canonical projection changes
    /// before the next planner tick can snapshot it.
    /// </summary>
    internal void ApplyAcceptedOutcomePosition(int guid, BotEvent evt)
    {
        bool positionOutcome = evt.EventType.Equals("TASK_COMPLETE", StringComparison.OrdinalIgnoreCase)
            || evt.EventType.Equals("TELEPORT_ACK", StringComparison.OrdinalIgnoreCase);
        if (!positionOutcome || string.IsNullOrEmpty(evt.Data))   // cb:fold only positive arrival outcomes carry canonical position
            return;   // cb:fold only positive arrival outcomes carry canonical position

        var posKv = ParsePipeDelimited(evt.Data);
        if (!posKv.TryGetValue("x", out var sx)
            || !posKv.TryGetValue("y", out var sy)
            || !posKv.TryGetValue("z", out var sz)
            || !float.TryParse(sx, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var fx)
            || !float.TryParse(sy, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var fy)
            || !float.TryParse(sz, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var fz))   // cb:fold parse rejection is telemetry-only
            return;   // cb:fold parse rejection is telemetry-only

        if (!Connections.TryGetValue(guid, out BotConnection? conn)
            || conn.SessionId != evt.BridgeSessionId)
        {
            CircuitTrace.Hit(guid, "bridge: accepted position belongs to superseded connection, ignored");
            return;
        }

        lock (conn.SensoryFeedGate)
        {
            if (!Connections.TryGetValue(guid, out BotConnection? active)
                || !ReferenceEquals(active, conn))   // cb:fold replacement boundary; lifecycle probe carries the result
                return;   // cb:fold replacement boundary; lifecycle probe carries the result

            conn.State.X = fx;
            conn.State.Y = fy;
            conn.State.Z = fz;
            if (posKv.TryGetValue("map", out var smap) && int.TryParse(smap, out var mapId))
                conn.State.MapId = mapId;   // cb:fold optional teleport map field
        }
        CircuitTrace.Hit(guid, "bridge: correlated positive outcome position applied");
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
