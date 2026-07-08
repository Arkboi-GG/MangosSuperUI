# PushQuestToParty

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# PushQuestToParty

**Purpose & Responsibilities**

`PushQuestToParty` is a client-to-server packet structure within the `WorldPackets::Quest` namespace. It represents the network message sent by a player client when they attempt to share a specific quest with their party members. This corresponds to the `CMSG_PUSHQUESTTOPARTY` opcode. The class is responsible for holding the `questId` associated with the push request and providing the mechanism (`ReadFromWorldPacket`) to deserialize this data from the raw network stream upon receipt by the server.

**Member-by-Member Behavior**

The unit consists of a single constructor and inherits standard packet behavior.

*   **Construction**: The default constructor initializes the packet with the specific opcode `CMSG_PUSHQUESTTOPARTY` via the base class `ClientPacket`. It also initializes the `questId` member to `0`.
*   **Data Storage**: The class exposes a public member `questId` (uint32), which stores the database entry ID of the quest being pushed. This field is populated during deserialization.
*   **Deserialization**: Although not explicitly listed in the MAP as a "member" of this specific partial (as it is likely implemented in a corresponding `.cpp` file or inherited/templated logic not shown in the provided header snippet's body), the declaration `void ReadFromWorldPacket(WorldPacket& recv_data) override;` indicates that this class implements the interface required to extract the `questId` from an incoming `WorldPacket`.

**Cross-Unit Boundaries**

*   **Inheritance**: Inherits from `WorldPackets::ClientPacket` (defined in `Packet.h`). This establishes the contract for client-originated messages, including the opcode assignment and the virtual `ReadFromWorldPacket` interface.
*   **Dependencies**: Uses `uint32` from standard types and relies on the `WorldPacket` class (from `Packet.h`) for the deserialization process.
*   **No Outbound Calls**: The MAP confirms this unit makes no calls to other units. It is a passive data structure until instantiated and processed by higher-level game logic (not part of this unit).
*   **No Inbound Calls from Other Units**: The MAP shows no other units calling into this specific member. Typically, instances of this class are created by the network layer when the opcode `CMSG_PUSHQUESTTOPARTY` is detected, after which the game logic consumes the object.

**Data Model**

This unit does not directly interact with database tables. It operates purely on network data structures. The `questId` it carries corresponds to the `entry` column in the `quest_template` table (implied by standard WoW server architecture), but this unit itself performs no SQL queries or direct table access.

**Notable Implementation Details**

*   **Final Class**: The class is marked `final`, preventing further inheritance. This is appropriate for a leaf-node packet structure.
*   **Default Initialization**: The `questId` is initialized to `0` in the class definition. This ensures that even if deserialization fails or is skipped, the member holds a valid, non-garbage value.
*   **Namespace Organization**: Located in `WorldPackets::Quest`, grouping all quest-related network protocols together for maintainability.

## Member Reference

**PushQuestToParty**
Constructor for the `PushQuestToParty` packet. Initializes the base `ClientPacket` with the opcode `CMSG_PUSHQUESTTOPARTY` and sets the `questId` member to `0`. It prepares the object to receive and store the quest identifier from an incoming network packet.

---

<!-- machine-true, projected from graph.json -->

## Map — PushQuestToParty

*Source:* Quest.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| PushQuestToParty | ctor | — | — | — |
