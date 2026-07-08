# MoveSetRawPosition

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# MoveSetRawPosition

**Purpose & Responsibilities**

`MoveSetRawPosition` is a client-side packet structure within the `WorldPackets::Misc` namespace, responsible for representing a raw position update request sent from the game client to the server. It encapsulates a single `Position` object, which contains the spatial coordinates (and potentially orientation) intended for the player character or mover. This packet is part of the movement system's low-level communication layer, allowing the client to transmit precise positional data that bypasses higher-level movement command abstractions.

As a `ClientPacket`, its primary responsibility is to deserialize incoming binary data from the network stream into the structured `location` field. It does not contain logic for validation, movement execution, or server-side processing; those concerns belong to the handlers that consume this packet after deserialization.

**Member-by-Member Behavior**

The unit consists of a single constructor and one public data member, both serving the deserialization workflow:

*   **Constructor (`MoveSetRawPosition`)**: Initializes the base `ClientPacket` class with a special opcode value `OPCODE_WILL_BE_SET_IN_READ_FUNCTION`. This indicates that the specific network opcode for this packet is not known at construction time but will be determined dynamically during the reading process. This pattern is typically used for packets whose opcode varies by client version or context, or where the opcode is embedded within the packet stream itself. The constructor does not initialize the `location` member, leaving it in a default-constructed state until `ReadFromWorldPacket` is invoked.
*   **`location`**: A public member of type `Position`. This holds the deserialized spatial data. Being public allows direct access by the calling handler code after the packet has been read, avoiding the need for getter methods.

**Cross-Unit Boundaries**

*   **Calls Out**: None. The `MoveSetRawPosition` class does not invoke functions in other units. Its logic is limited to initialization and data storage.
*   **Called By**: The MAP indicates no external callers are explicitly listed. However, by definition of its inheritance from `ClientPacket`, it is instantiated and populated by the packet parsing infrastructure (likely within `Packet.cpp` or a similar central dispatcher) when the corresponding network message is received. The handler responsible for processing raw position updates will instantiate this class, call `ReadFromWorldPacket`, and then access the `location` member.

**Data Model**

This unit does not interact with any database tables. It operates entirely in memory, handling transient network data.

**Notable Implementation Details**

*   **Dynamic Opcode Handling**: The use of `OPCODE_WILL_BE_SET_IN_READ_FUNCTION` in the constructor is significant. Unlike most other `ClientPacket` subclasses in `Misc.h` (e.g., `AddFriend`, `Emote`) which specify a static opcode like `CMSG_ADD_FRIEND` at construction, `MoveSetRawPosition` defers opcode assignment. This suggests that the opcode for raw position updates might be variable or determined by the packet reader itself, possibly to support multiple movement-related opcodes under a single handler or to accommodate differences between client builds.
*   **Public Data Member**: The `location` field is public. While this simplifies access for the consuming handler, it exposes the internal state directly. Maintainers should ensure that the `Position` object is fully initialized by `ReadFromWorldPacket` before being accessed to avoid undefined behavior.
*   **Minimal Logic**: The class contains no validation logic. It assumes the incoming packet data is well-formed according to the `Position` serialization format. Any validation of the position's validity (e.g., checking for out-of-bounds coordinates) must occur in the handler that processes this packet.

## Member Reference

**MoveSetRawPosition**  
Constructor for the `MoveSetRawPosition` packet. Initializes the base `ClientPacket` with `OPCODE_WILL_BE_SET_IN_READ_FUNCTION`, indicating that the network opcode is resolved during the read phase rather than at construction. Does not initialize the `location` member.

---

<!-- machine-true, projected from graph.json -->

## Map — MoveSetRawPosition

*Source:* Misc.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| MoveSetRawPosition | ctor | — | — | — |
