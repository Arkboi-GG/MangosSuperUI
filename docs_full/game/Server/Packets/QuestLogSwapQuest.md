# QuestLogSwapQuest

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# QuestLogSwapQuest

**Purpose & Responsibilities**

`QuestLogSwapQuest` is a client-side packet structure within the `WorldPackets::Quest` namespace, responsible for representing the `CMSG_QUESTLOG_SWAP_QUEST` message sent from the game client to the server. Its sole responsibility is to deserialize two 8-bit unsigned integers (`slot1` and `slot2`) from the incoming network buffer. These slots correspond to indices in the player's active quest log, indicating which two quests the client wishes to swap positions. This packet contains no business logic, validation, or state management; it is a pure data carrier for the network layer.

**Member-by-Member Behavior**

The unit consists of a single member: the constructor.

*   **`QuestLogSwapQuest`**: This default constructor initializes the packet object. It sets the internal packet opcode to `CMSG_QUESTLOG_SWAP_QUEST` via the base class `ClientPacket` constructor. It also initializes the member variables `slot1` and `slot2` to `0`. The initialization of these fields to zero is handled by the in-class member initializers defined in the header, ensuring that if the deserialization process fails or is skipped, the fields hold a known safe value.

**Cross-Unit Boundaries**

*   **Calls Out**: None. The constructor performs no external calls.
*   **Called By**: None listed in the map. In practice, this packet is instantiated by the network handler (likely in a separate unit such as `WorldSession` or a dedicated quest handler) when the server receives raw bytes matching the `CMSG_QUESTLOG_SWAP_QUEST` opcode. The handler will then invoke the `ReadFromWorldPacket` method (declared in the base class `ClientPacket`, though not explicitly mapped here as it is inherited) to populate `slot1` and `slot2`.

**Data Model**

This unit does not interact directly with any database tables. It operates entirely on transient network data. The `slot1` and `slot2` values refer to logical indices in the client's local representation of the quest log, which corresponds to the server-side quest log state managed by other units (e.g., `Player` or `QuestManager`). No SQL queries are executed by this class.

**Notable Implementation Details**

*   **Inheritance**: `QuestLogSwapQuest` inherits from `ClientPacket`, which provides the framework for reading binary data from the network stream. The actual deserialization logic resides in the overridden `ReadFromWorldPacket` method, which is declared in the base class hierarchy but not detailed in this specific unit's map.
*   **Slot Indices**: The use of `uint8` for `slot1` and `slot2` implies that the quest log size is constrained to 256 entries or fewer, which aligns with typical World of Warcraft client limitations for active quests.
*   **No Validation**: The class itself performs no validation on the slot indices (e.g., checking if they are distinct, within bounds, or valid for the current player). Such checks are expected to occur in the server-side handler that processes this packet after instantiation.

## Member Reference

**QuestLogSwapQuest**
Constructor for the `QuestLogSwapQuest` packet. Initializes the packet opcode to `CMSG_QUESTLOG_SWAP_QUEST` and sets `slot1` and `slot2` to `0`. It prepares the object to receive data from the network stream via the inherited `ReadFromWorldPacket` method.

---

<!-- machine-true, projected from graph.json -->

## Map — QuestLogSwapQuest

*Source:* Quest.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| QuestLogSwapQuest | ctor | — | — | — |
