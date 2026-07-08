# QuestgiverCompleteQuest

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# QuestgiverCompleteQuest

**Purpose & Responsibilities**

`QuestgiverCompleteQuest` is a client-to-server network packet structure within the `WorldPackets::Quest` namespace. It represents the `CMSG_QUESTGIVER_COMPLETE_QUEST` message sent by the game client when a player attempts to turn in a completed quest to an NPC quest giver.

The class serves two primary responsibilities:
1.  **Data Container:** It holds the raw data extracted from the incoming network stream: the `ObjectGuid` of the target NPC (`guid`) and the numeric ID of the quest being turned in (`quest`).
2.  **Protocol Binding:** It binds this data to the specific opcode `CMSG_QUESTGIVER_COMPLETE_QUEST`, ensuring the server's packet dispatch system routes this message to the correct handler.

As a `ClientPacket`, it inherits the responsibility of parsing binary data from the `WorldPacket` buffer via its `ReadFromWorldPacket` method. This unit does not contain any game logic, validation, or database interaction; it strictly defines the interface for receiving this specific command from the client.

## Member-by-Member Behavior

### Construction and Initialization

**`QuestgiverCompleteQuest()`**
This is the default constructor for the packet. Its behavior is minimal and focused on initialization:
1.  It invokes the base class constructor `ClientPacket(CMSG_QUESTGIVER_COMPLETE_QUEST)`. This registers the packet with the server's networking layer under the specific opcode associated with quest completion.
2.  It initializes the member variables:
    *   `guid`: Default-initialized to an empty/null `ObjectGuid` (typically all zeros).
    *   `quest`: Explicitly initialized to `0`.

This constructor is typically called by the server's packet factory when a new instance of this packet type is needed to process an incoming stream. The actual population of `guid` and `quest` occurs later during the deserialization phase handled by `ReadFromWorldPacket`.

### Deserialization

**`ReadFromWorldPacket(WorldPacket& recv_data)`**
Although declared in the header, the implementation is not provided in the source snippet. However, based on the inheritance from `ClientPacket` and the presence of `guid` and `quest` members, this method is responsible for extracting the binary data from the `recv_data` buffer. It will sequentially read:
1.  The `ObjectGuid` of the quest giver NPC.
2.  The `uint32` quest entry ID.

These values are stored in the respective member variables for subsequent processing by the server's quest handling logic.

## Cross-Unit Boundaries

*   **Calls Out:** None. This unit is a pure data structure with no outgoing dependencies to other classes or modules.
*   **Called By:** While the MAP indicates no external callers, in practice, this class is instantiated and populated by the server's core networking loop (likely within `WorldSession` or a similar packet dispatcher) when a `CMSG_QUESTGIVER_COMPLETE_QUEST` opcode is detected. The populated object is then passed to the quest management subsystem (e.g., `Player::HandleQuestgiverCompleteQuest` or equivalent) for actual game logic execution.

## Data Model

This unit does not interact directly with any database tables. It operates solely on runtime memory structures derived from the network packet. The `quest` field corresponds to a `quest_template` entry in the database, and `guid` corresponds to a creature or game object in the world, but these relationships are resolved by higher-level game logic, not by this packet class.

## Notable Implementation Details

*   **Default Initialization:** The `quest` member is explicitly initialized to `0` in the constructor. This is a defensive measure, though `ReadFromWorldPacket` should overwrite this value. If the packet is malformed or truncated, the default value ensures the variable is not left in an indeterminate state.
*   **Final Class:** The class is marked `final`, preventing inheritance. This enforces a strict, immutable interface for this specific packet type, which is appropriate for network protocol definitions where polymorphism is unnecessary and potentially dangerous.
*   **Namespace Organization:** Located in `WorldPackets::Quest`, this grouping keeps all quest-related network packets together, improving maintainability and discoverability.

## Member Reference

**`QuestgiverCompleteQuest()`**
The default constructor for the `QuestgiverCompleteQuest` packet. It initializes the base `ClientPacket` with the opcode `CMSG_QUESTGIVER_COMPLETE_QUEST` and sets the `quest` member to `0`. The `guid` member is default-initialized. This constructor prepares the object to receive data from the network stream.

---

<!-- machine-true, projected from graph.json -->

## Map — QuestgiverCompleteQuest

*Source:* Quest.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| QuestgiverCompleteQuest | ctor | — | — | — |
