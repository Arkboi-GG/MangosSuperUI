# QueryCreature

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# QueryCreature

**Purpose & Responsibilities**

`QueryCreature` is a client-side packet structure within the `WorldPackets::Query` namespace, defined in `Query.h`. It represents the `CMSG_CREATURE_QUERY` message sent by a World of Warcraft client to the server. Its sole responsibility is to encapsulate the data required for the client to request detailed information about a specific creature entity currently visible or known to the client. This includes the creature's static definition identifier (`entry`) and its unique runtime instance identifier (`guid`).

As a `ClientPacket`, it serves as a data container that is populated during the deserialization process (via `ReadFromWorldPacket`) before being handed off to the server's query handling logic. It does not contain logic for processing the query, sending responses, or interacting with databases; it strictly models the binary layout of the incoming network message.

**Member-by-Member Behavior**

The unit contains a single member: the constructor.

*   **`QueryCreature`**: The default constructor initializes the packet object. It sets the base class `ClientPacket` to expect the opcode `CMSG_CREATURE_QUERY`. It also initializes the two public data members:
    *   `entry`: Set to `0` by default. This holds the creature's entry ID (from the `creature_template` table in the database context, though the packet itself only stores the integer).
    *   `guid`: Default-initialized `ObjectGuid`. This holds the unique identifier for the specific creature instance in the world.

**Cross-Unit Boundaries**

*   **Inheritance**: Inherits from `ClientPacket` (defined in `Packet.h`). This establishes the contract that this object represents a message originating from the client.
*   **Composition**: Uses `ObjectGuid` (defined in `ObjectGuid.h`) to store the creature's unique identifier.
*   **No Outgoing Calls**: The `QueryCreature` class itself does not call any other units. Its methods (specifically `ReadFromWorldPacket`, which is declared but not defined in this header) would typically interact with `WorldPacket` (from `Packet.h`) to deserialize data, but the implementation of that interaction is not part of this unit's visible behavior in the provided source.
*   **No Incoming Calls**: According to the MAP, no other units explicitly call into `QueryCreature` members. In practice, instances of this class are likely created by the packet parsing infrastructure (e.g., in `WorldSession` or similar handlers) when a `CMSG_CREATURE_QUERY` opcode is detected, but those interactions occur outside the scope of this unit's direct dependencies.

**Data Model**

This unit does not interact directly with any database tables. It is a pure network protocol abstraction. The `entry` field corresponds to the `entry` column in the `creature_template` table, and the `guid` corresponds to the `guid` column in the `creature` table, but these relationships are logical and handled by downstream server logic, not by this packet class.

**Notable Implementation Details**

*   **Default Initialization**: Both `entry` and `guid` are default-initialized. `entry` is explicitly set to `0`, while `guid` relies on `ObjectGuid`'s default constructor. This ensures that if deserialization fails or is incomplete, the fields hold safe, predictable values rather than garbage data.
*   **Final Class**: The class is marked `final`, indicating it cannot be subclassed. This is appropriate for a leaf-node packet type in the hierarchy.
*   **Namespace**: Located in `WorldPackets::Query`, grouping it logically with other query-type packets like `QueryPlayerName`, `QueryGameObject`, etc.

## Member Reference

**QueryCreature**
Constructor for the `QueryCreature` packet. Initializes the base `ClientPacket` with the opcode `CMSG_CREATURE_QUERY` and sets the `entry` member to `0` and the `guid` member to its default state.

---

<!-- machine-true, projected from graph.json -->

## Map — QueryCreature

*Source:* Query.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| QueryCreature | ctor | — | — | — |
