# QuestgiverHello

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# QuestgiverHello

**Purpose & Responsibilities**

`QuestgiverHello` is a lightweight data structure representing a specific client-to-server network message within the `wowvmangos` game server. It resides in the `WorldPackets::Quest` namespace and inherits from `ClientPacket`, indicating it is part of the incoming packet parsing infrastructure.

Its sole responsibility is to encapsulate the data associated with the `CMSG_QUESTGIVER_HELLO` opcode. This message is sent by the game client when a player interacts with an NPC (Non-Player Character) designated as a quest giver, initiating the quest interaction sequence. The class stores the `ObjectGuid` of the target NPC, which identifies the specific entity the player is interacting with.

As a "partial" of the broader `Quest.h` header, this unit defines only the `QuestgiverHello` class. Other classes in the same header (e.g., `QueryQuest`, `QuestgiverAcceptQuest`) belong to different logical units or partials and are not described here.

**Member-by-Member Behavior**

The unit contains a single member: the constructor.

*   **`QuestgiverHello`**: This is the default constructor for the class. It performs two initialization tasks:
    1.  It invokes the base class constructor `ClientPacket`, passing the constant `CMSG_QUESTGIVER_HELLO`. This registers the packet type with the network layer, ensuring that incoming data streams with this opcode are routed to instances of this class for parsing.
    2.  It leaves the `guid` member uninitialized (default-initialized to zero/null depending on `ObjectGuid`'s default behavior, though typically set during parsing).

The class also declares a pure virtual method `ReadFromWorldPacket` inherited from `ClientPacket`. While declared in the header, its implementation is not present in this source snippet. Based on standard patterns in this codebase, this method would parse the binary data from the `WorldPacket` object to populate the `guid` member. However, since the implementation is not provided in the source, we cannot detail its specific parsing logic.

**Cross-Unit Boundaries**

*   **Calls Out**: None. The constructor does not call any other units.
*   **Called By**: None listed in the MAP. In practice, this class is instantiated by the network packet dispatcher (likely in a unit like `PacketHandler` or `WorldSession`) when a `CMSG_QUESTGIVER_HELLO` packet is received. The dispatcher would then call `ReadFromWorldPacket` and subsequently pass the populated `QuestgiverHello` object to a handler function (likely in a unit like `QuestHandler.cpp`) to process the quest interaction.

**Data Model**

This unit does not directly interact with any database tables. It operates purely on in-memory network data structures (`ObjectGuid`). Any database queries related to quest givers (e.g., checking if the NPC has quests, retrieving quest templates) would occur in downstream handler units after this packet has been parsed and validated.

**Notable Implementation Details**

*   **Final Class**: The class is marked `final`, preventing further inheritance. This is consistent with leaf-node packet structures that should not be subclassed.
*   **Minimal State**: The class holds only one piece of state: `ObjectGuid guid`. This reflects the simplicity of the "hello" handshake; the client only needs to specify *who* it is talking to. All other quest-related data (available quests, rewards, etc.) is retrieved by the server in response to this hello, likely via subsequent packets or immediate server-side lookups triggered by the handler.
*   **Namespace Organization**: It is nested within `WorldPackets::Quest`, clearly segregating quest-related network traffic from other game systems (combat, chat, movement, etc.).

## Member Reference

**QuestgiverHello**
Constructor for the `QuestgiverHello` packet class. Initializes the base `ClientPacket` with the opcode `CMSG_QUESTGIVER_HELLO`. Does not perform any additional logic or call other units.

---

<!-- machine-true, projected from graph.json -->

## Map — QuestgiverHello

*Source:* Quest.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| QuestgiverHello | ctor | — | — | — |
