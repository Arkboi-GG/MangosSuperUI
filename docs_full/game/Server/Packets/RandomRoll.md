# RandomRoll

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# RandomRoll

## Purpose & Responsibilities

The `RandomRoll` class is a client-side packet structure within the `WorldPackets::Group` namespace. It represents the `MSG_RANDOM_ROLL` message sent by the game client to the server when a player initiates a random number roll (typically via the `/roll` command in chat). Its sole responsibility is to define the data layout for this specific network message and provide the mechanism to deserialize binary packet data into accessible C++ fields (`minimum` and `maximum`).

As a `ClientPacket`, it serves as the input side of the communication channel for this feature. It does not contain logic for validating the roll, generating the result, or broadcasting it to other players; those responsibilities lie in the server-side handlers that process this packet after it has been read.

## Member-by-Member Behavior

### Constructor: `RandomRoll`

The constructor initializes the packet object. It performs two key actions:
1.  **Base Initialization**: It calls the base class constructor `ClientPacket(MSG_RANDOM_ROLL)`, registering this instance with the specific opcode `MSG_RANDOM_ROLL`. This opcode identifies the packet type during the server's packet dispatching phase.
2.  **Field Initialization**: The member variables `minimum` and `maximum` are initialized to `0` via in-class initializers. These fields represent the range of the random roll requested by the player (e.g., `/roll 1 100` would set `minimum` to 1 and `maximum` to 100).

### Deserialization: `ReadFromWorldPacket`

Although declared in the header, the implementation of `ReadFromWorldPacket` is not provided in the source snippet. However, based on the class structure and standard patterns in this codebase:
*   This virtual method overrides the base class implementation.
*   It is responsible for reading the raw binary data from a `WorldPacket` object.
*   It extracts the `minimum` and `maximum` values from the stream, populating the respective member variables.
*   The order and size of these reads correspond to the client's serialization format for `MSG_RANDOM_ROLL`.

## Cross-Unit Boundaries

*   **Calls Out**: None. The `RandomRoll` class itself does not invoke methods in other units. Its dependency is limited to the base class `ClientPacket` and the `WorldPacket` type used in the signature of `ReadFromWorldPacket`.
*   **Called By**: The MAP indicates no external callers listed explicitly, but in practice, this class is instantiated and populated by the server's packet parsing infrastructure (likely within a central packet handler or dispatcher unit such as `WorldSession` or a dedicated `GroupHandler` unit) when a `MSG_RANDOM_ROLL` opcode is received from a client.

## Data Model

This unit does not interact directly with any database tables. It operates entirely on transient network data. There are no SQL queries, table references, or schema dependencies associated with `RandomRoll`.

## Notable Implementation Details

*   **Minimal State**: The class contains only two `uint32` fields. This simplicity reflects the straightforward nature of the `/roll` command, which only requires a lower and upper bound.
*   **Default Values**: Both `minimum` and `maximum` default to `0`. If the client sends malformed data or if deserialization fails to populate these fields correctly, the server will receive zeros. Server-side validation (outside this unit) must handle cases where `minimum >= maximum` or where values are invalid.
*   **Inheritance**: As a `final` class inheriting from `ClientPacket`, it is part of a polymorphic hierarchy designed for efficient packet routing. The `final` specifier prevents further inheritance, ensuring the packet structure remains stable and predictable.
*   **Namespace Organization**: Located in `WorldPackets::Group`, it is logically grouped with other party/raid-related messages, even though rolling is a general chat feature. This suggests the original design may have categorized all social/group-interaction packets under this namespace.

## Member Reference

**RandomRoll**  
Constructor for the `RandomRoll` packet. Initializes the base `ClientPacket` with the opcode `MSG_RANDOM_ROLL` and sets the `minimum` and `maximum` member variables to `0`. This prepares the object to receive and store the bounds of a random roll request from the client.

---

<!-- machine-true, projected from graph.json -->

## Map — RandomRoll

*Source:* Group.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| RandomRoll | ctor | — | — | — |
