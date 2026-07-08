# BattlemasterJoin

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# BattlemasterJoin

**Purpose & Responsibilities**

`BattlemasterJoin` is a client-to-server packet structure within the `WorldPackets::Battleground` namespace. It represents the network message sent by a client when a player attempts to join a battleground or arena queue. Specifically, it handles the `CMSG_BATTLEMASTER_JOIN` opcode.

This class is responsible for defining the data layout expected from the client for this specific action. It captures the target entity (a Battlemaster NPC or a portal), the specific map or instance being requested, and whether the player intends to join as part of a group. The class is conditionally compiled for client builds newer than 1.6.1 (`CLIENT_BUILD_1_6_1`), reflecting changes in the protocol for older versions of the game client.

**Member-by-Member Behavior**

The unit consists of a single constructor and several data members that define the packet's payload.

*   **Constructor (`BattlemasterJoin()`)**: Initializes the packet object. It sets the internal opcode to `CMSG_BATTLEMASTER_JOIN`, identifying this packet type to the server's packet dispatching system. It relies on the base class `ClientPacket` for further initialization.
*   **Data Members**:
    *   `guid`: An `ObjectGuid` representing the target of the join request. As noted in the source comments, this is either the GUID of a Battlemaster NPC the player interacted with, or the GUID of a player if the join is initiated via a Battleground portal.
    *   `mapId`: A `uint32` specifying the map ID associated with the battleground or arena. This allows the server to identify which specific battleground type is being requested.
    *   `instanceId`: A `uint32` specifying the instance ID. This is typically used for arenas or specific battleground instances to ensure players join the correct active session.
    *   `joinAsGroup`: A `uint8` flag indicating whether the player is attempting to join the queue as part of a group (party/raid) or individually.

**Cross-Unit Boundaries**

*   **Inheritance**: `BattlemasterJoin` inherits from `ClientPacket`. This establishes the contract that this class represents data arriving from the client. The base class provides the mechanism for reading raw bytes from the network stream into the structured fields defined here.
*   **Dependencies**:
    *   `ObjectGuid`: Used for the `guid` member. This type is defined elsewhere in the codebase and provides the unique identifier format for game objects.
    *   `WorldPacket`: Referenced in the declaration of `ReadFromWorldPacket`. This is the low-level packet container used by the network layer.
*   **Collaboration**: While this unit defines the structure, the actual logic for processing the join request (validating the GUID, checking queue eligibility, adding the player to the queue) resides in the server-side handler that receives this packet. That handler will instantiate `BattlemasterJoin`, call `ReadFromWorldPacket` to populate the fields, and then use the populated `guid`, `mapId`, `instanceId`, and `joinAsGroup` to execute the game logic. This unit itself contains no such logic; it is purely a data carrier.

**Data Model**

This unit does not directly interact with any database tables. It operates entirely on network packet data received from the client. Any database interactions related to queueing (e.g., saving queue status, checking player eligibility) would occur in the server-side handlers that consume this packet, not within this packet definition itself.

**Notable Implementation Details**

*   **Conditional Compilation**: The entire class is wrapped in `#if SUPPORTED_CLIENT_BUILD > CLIENT_BUILD_1_6_1`. This indicates that the `CMSG_BATTLEMASTER_JOIN` packet structure changed or was introduced after client build 1.6.1. For older clients, a different packet structure or handling path is likely used.
*   **Default Values**: The members `mapId`, `instanceId`, and `joinAsGroup` are initialized to `0` in their declarations. This ensures that if the client sends a malformed packet or omits these fields (though unlikely for a fixed-size packet), they default to a safe zero state.
*   **GUID Ambiguity**: The comment explicitly clarifies the dual nature of the `guid` field. It is critical for the server-side handler to distinguish between a Battlemaster NPC GUID and a Player GUID (from a portal) to correctly route the join request. The packet structure itself does not enforce this distinction; it merely carries the value.
*   **ReadFromWorldPacket Declaration**: The method `ReadFromWorldPacket` is declared but not defined in this header. Its implementation is presumably in a corresponding `.cpp` file (not provided in the source snippet, but implied by the standard pattern). This method is responsible for parsing the binary data from the `WorldPacket` into the member variables.

## Member Reference

**BattlemasterJoin**
The constructor for the `BattlemasterJoin` packet class. It initializes the packet with the opcode `CMSG_BATTLEMASTER_JOIN`. It is conditionally compiled for client builds greater than 1.6.1. This constructor prepares the object to receive data from the network via the `ReadFromWorldPacket` method.

---

<!-- machine-true, projected from graph.json -->

## Map — BattlemasterJoin

*Source:* Battleground.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| BattlemasterJoin | ctor | — | — | — |
