# ItemNameQuery

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ItemNameQuery

**Purpose & Responsibilities**

`ItemNameQuery` is a lightweight data structure within the `WorldPackets::Query` namespace that represents a client-to-server network message requesting the name of a specific item. It serves as the C++ counterpart to the `CMSG_ITEM_NAME_QUERY` packet opcode, encapsulating the raw data received from the client before it is processed by the server's query handling logic. Its sole responsibility is to hold the `itemId` associated with the request and provide the mechanism (`ReadFromWorldPacket`) to deserialize this value from the incoming binary stream.

**Member-by-Member Behavior**

The unit contains a single constructor and relies on inherited functionality for packet identification.

*   **Construction**: The explicit constructor initializes the base `ClientPacket` with the opcode `CMSG_ITEM_NAME_QUERY`. This registration ensures that when the network layer receives a packet with this opcode, it instantiates this specific class to handle the payload.
*   **Data Storage**: The class exposes a public member `itemId` (initialized to 0) which stores the numeric identifier of the item whose name is being requested.
*   **Deserialization**: Although the implementation of `ReadFromWorldPacket` is not present in this header (it is likely defined in a corresponding `.cpp` file or implemented inline elsewhere), the declaration indicates that this method overrides the base class virtual function to extract the `itemId` from the `WorldPacket` buffer.

**Cross-Unit Boundaries**

*   **Inheritance**: Inherits from `WorldPackets::ClientPacket` (defined in `Packet.h`). This provides the base infrastructure for packet opcodes, serialization hooks, and network protocol compliance.
*   **No Outbound Calls**: As a pure data carrier for an inbound message, `ItemNameQuery` does not call into other units during its construction or data storage phase.
*   **No Inbound Calls from Other Units**: The MAP indicates no other units explicitly call into `ItemNameQuery` members. In practice, the network dispatcher creates instances of this class, and the query handler system reads from it, but these interactions are mediated through the base `ClientPacket` interface or direct object instantiation rather than explicit cross-unit member calls listed in the MAP.

**Data Model**

This unit does not interact directly with database tables. It operates entirely in memory as part of the network I/O layer. The `itemId` it carries will eventually be used by downstream components (not part of this unit) to query the `item_template` table or similar structures, but `ItemNameQuery` itself performs no SQL operations.

**Notable Implementation Details**

*   **Final Class**: The class is marked `final`, preventing further inheritance. This is appropriate for a leaf-node packet structure that has no need for polymorphic extension.
*   **Public Data Member**: The `itemId` is declared as a public member variable rather than a private field with getters/setters. This design choice prioritizes simplicity and performance for a transient packet object, allowing the receiving handler to access the ID directly without function call overhead.
*   **Default Initialization**: `itemId` is initialized to `0` in the class definition. This ensures that if deserialization fails or is skipped, the object remains in a known safe state, though valid game logic should always populate this field via `ReadFromWorldPacket`.

## Member Reference

**ItemNameQuery**
Constructor for the `ItemNameQuery` packet. It explicitly initializes the base `ClientPacket` with the opcode `CMSG_ITEM_NAME_QUERY`. It does not take arguments and does not call other units. It prepares the object to receive and store an `itemId` from the network stream.

---

<!-- machine-true, projected from graph.json -->

## Map — ItemNameQuery

*Source:* Query.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ItemNameQuery | ctor | — | — | — |
