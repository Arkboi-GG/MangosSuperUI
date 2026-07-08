# MoveNotActiveMover

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# MoveNotActiveMover

`MoveNotActiveMover` is a client-to-server packet structure within the `WorldPackets::Movement` namespace, defined in `Movement.h`. It represents the `CMSG_MOVE_NOT_ACTIVE_MOVER` message sent by the World of Warcraft client to the server. This packet is part of the movement synchronization protocol, specifically handling scenarios where the client indicates that a particular mover object is no longer the active entity controlling movement updates, or that the current movement context has shifted away from the expected mover.

## Purpose & Responsibilities

The primary responsibility of `MoveNotActiveMover` is to deserialize incoming network data for the `CMSG_MOVE_NOT_ACTIVE_MOVER` opcode. It acts as a data carrier, extracting relevant movement state information from the raw byte stream provided by the network layer.

Key responsibilities include:
1.  **Opcode Association**: Binding itself to the specific network opcode `CMSG_MOVE_NOT_ACTIVE_MOVER` during construction.
2.  **Data Extraction**: Implementing the `ReadFromWorldPacket` method to parse the binary payload into structured fields (`oldMoverGuid` and `movementInfo`).
3.  **Client Version Compatibility**: Conditionally including the `oldMoverGuid` field based on the supported client build version, ensuring compatibility with clients newer than build 1.9.4.

## Member-by-Member Behavior

### Constructor: `MoveNotActiveMover()`
The default constructor initializes the packet base class with the opcode `CMSG_MOVE_NOT_ACTIVE_MOVER`. This ensures that when the packet is processed by the server's message dispatcher, it is correctly routed to the handler responsible for "not active mover" events. No additional initialization is performed on the member variables, relying on their default values or subsequent parsing.

### Method: `ReadFromWorldPacket(WorldPacket& recv_data)`
Although the implementation body is not provided in the source snippet, the declaration indicates that this virtual method overrides the base class behavior to parse the incoming `WorldPacket`. Based on the member variables declared in the class, this method is expected to:
1.  Check the client build version. If `SUPPORTED_CLIENT_BUILD > CLIENT_BUILD_1_9_4`, it reads an `ObjectGuid` into `oldMoverGuid`.
2.  Reads the `MovementInfo` structure into `movementInfo`.

This method transforms the raw network bytes into usable C++ objects for further processing by the game logic.

## Cross-Unit Boundaries

*   **Calls Out**: The MAP indicates no outgoing calls to other units from `MoveNotActiveMover`. This is consistent with its role as a simple data structure/packet parser; it does not contain business logic that requires interacting with other subsystems like AI, combat, or database layers.
*   **Called By**: The MAP indicates no incoming calls from other units. In practice, this packet is instantiated and populated by the network layer (likely `WorldSession` or a similar packet dispatcher) when a client sends the `CMSG_MOVE_NOT_ACTIVE_MOVER` message. The parsed data is then passed to the appropriate movement handler (e.g., `Unit::HandleMoveNotActiveMover` or similar, though these are outside this unit's scope).

## Data Model

This unit does not interact with any database tables. It operates entirely on in-memory network packet data.

## Notable Implementation Details

1.  **Conditional Compilation for Client Builds**:
    The class uses preprocessor directives (`#if SUPPORTED_CLIENT_BUILD > CLIENT_BUILD_1_9_4`) to conditionally include the `oldMoverGuid` member. This reflects changes in the World of Warcraft client protocol over time. Older clients (build 1.9.4 and below) do not send this GUID, so the parser must account for this difference to avoid deserialization errors. Maintainers must ensure that the corresponding `ReadFromWorldPacket` implementation respects this same conditional logic.

2.  **Inheritance from `ClientPacket`**:
    `MoveNotActiveMover` inherits from `ClientPacket`, indicating it is strictly a client-to-server message. It does not handle server-to-client responses.

3.  **Final Class**:
    The class is marked as `final`, preventing further inheritance. This enforces a flat hierarchy for this specific packet type, simplifying maintenance and ensuring no derived classes alter its behavior.

4.  **Dependency on `MovementInfo`**:
    The class relies on the `MovementInfo` structure (defined in `MovementInfo.h`, included via `Movement.h`) to represent complex movement state. Any changes to `MovementInfo`'s serialization format will impact how `MoveNotActiveMover` parses its data.

## Member Reference

**MoveNotActiveMover**
Constructor for the `MoveNotActiveMover` packet. Initializes the base `ClientPacket` with the opcode `CMSG_MOVE_NOT_ACTIVE_MOVER`. Does not perform any data parsing or external calls.

---

<!-- machine-true, projected from graph.json -->

## Map — MoveNotActiveMover

*Source:* Movement.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| MoveNotActiveMover | ctor | — | — | — |
