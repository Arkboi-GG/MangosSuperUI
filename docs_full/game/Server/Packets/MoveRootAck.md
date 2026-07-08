# MoveRootAck

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# MoveRootAck

**MoveRootAck** is a client-to-server packet structure within the `WorldPackets::Movement` namespace, defined in `Movement.h`. It represents the acknowledgment sent by the game client in response to a server-initiated movement root or unroot command. Specifically, it handles the opcodes `CMSG_FORCE_MOVE_ROOT_ACK` and `CMSG_FORCE_MOVE_UNROOT_ACK`, confirming that the client has processed a change in its movement state regarding being rooted (immobilized) or unrooted.

## Purpose & Responsibilities

The primary responsibility of `MoveRootAck` is to deserialize incoming network data from the client into a structured format that the server can process. As a subclass of `ClientPacket`, it inherits the base functionality for handling client-bound traffic but specializes in movement-related acknowledgments.

Key responsibilities include:
1.  **State Synchronization**: Capturing the `ObjectGuid` of the entity being rooted/unrooted and the associated `MovementInfo` to ensure the server's view of the player's movement state aligns with the client's.
2.  **Opcode Handling**: Supporting two distinct opcodes (`CMSG_FORCE_MOVE_ROOT_ACK` and `CMSG_FORCE_MOVE_UNROOT_ACK`) through a single class definition, with the specific opcode determined at runtime during the reading phase (indicated by `OPCODE_WILL_BE_SET_IN_READ_FUNCTION`).
3.  **Client Version Compatibility**: Conditionally including a `movementCounter` field for client builds newer than 1.9.4, ensuring backward compatibility with older clients while maintaining synchronization integrity for newer ones.

## Member-by-Member Behavior

### Constructor: `MoveRootAck`
The default constructor initializes the packet with a placeholder opcode (`OPCODE_WILL_BE_SET_IN_READ_FUNCTION`). This indicates that the actual opcode is not known at construction time but will be resolved when the packet is read from the network stream. This design allows the same class instance to handle both root and unroot acknowledgments, depending on which opcode the server receives first.

### Method: `ReadFromWorldPacket`
Although the implementation of `ReadFromWorldPacket` is not shown in the provided source (it is likely defined in a corresponding `.cpp` file or inherited/templated elsewhere), its signature and context imply the following behavior:
-   It extracts the `guid` (ObjectGuid) from the raw `WorldPacket`.
-   It conditionally extracts the `movementCounter` if the client build supports it (`> CLIENT_BUILD_1_9_4`).
-   It deserializes the `movementInfo` object, which contains detailed movement state data (position, orientation, flags, etc.).
-   It sets the internal opcode of the packet to match the received message type, allowing the server to distinguish between a root acknowledgment and an unroot acknowledgment later in the processing pipeline.

## Cross-Unit Boundaries

-   **Inherits from `ClientPacket`**: `MoveRootAck` relies on the `ClientPacket` base class for core packet management, such as opcode storage and basic serialization utilities. The base class is responsible for the low-level mechanics of reading from the `WorldPacket` buffer.
-   **Uses `ObjectGuid`**: The `guid` member uses the `ObjectGuid` type, which is a fundamental identifier for entities in the game world. This type is defined elsewhere in the codebase and provides methods for parsing and validating GUIDs.
-   **Uses `MovementInfo`**: The `movementInfo` member is an instance of `MovementInfo`, a complex structure that encapsulates all relevant movement data. This class is defined in `MovementInfo.h` and handles the detailed breakdown of movement states, such as position, velocity, and movement flags.
-   **Called by Network Handler**: While not explicitly listed in the "Called by" column of the MAP, `MoveRootAck` instances are typically created and populated by the network handler layer when a `CMSG_FORCE_MOVE_ROOT_ACK` or `CMSG_FORCE_MOVE_UNROOT_ACK` packet is received from the client. The network handler then passes this populated object to the appropriate game logic handler (e.g., a player movement handler) for further processing.

## Data Model

This unit does not interact directly with any database tables. It operates entirely in memory, handling transient network packet data. The `guid` and `movementInfo` fields represent in-memory state synchronization between the client and server, not persistent storage.

## Notable Implementation Details

-   **Conditional Compilation for Client Builds**: The `movementCounter` field is wrapped in `#if SUPPORTED_CLIENT_BUILD > CLIENT_BUILD_1_9_4`. This means that for older clients, this field does not exist in the struct, and the `ReadFromWorldPacket` implementation must skip reading it. This is a critical detail for maintaining compatibility across different client versions.
-   **Dynamic Opcode Resolution**: The use of `OPCODE_WILL_BE_SET_IN_READ_FUNCTION` in the constructor is a notable pattern. It defers the determination of the specific opcode until the packet is actually read. This allows the server to reuse the same packet class for multiple related opcodes, reducing code duplication. The actual opcode is set during the `ReadFromWorldPacket` call, likely by checking the raw opcode from the `WorldPacket` before deserialization begins.
-   **No Default Values for Critical Fields**: Unlike some other packets in the same header (e.g., `MoveTimeSkipped` which initializes `lag = 0`), `MoveRootAck` does not initialize `guid` or `movementInfo` with default values in the constructor. This implies that these fields *must* be populated by `ReadFromWorldPacket` before the packet is considered valid. Accessing them before reading would result in undefined behavior.

## Member Reference

**MoveRootAck**  
Constructor for the `MoveRootAck` packet. Initializes the base `ClientPacket` with a placeholder opcode, indicating that the true opcode will be determined during the read phase. This allows the class to handle both `CMSG_FORCE_MOVE_ROOT_ACK` and `CMSG_FORCE_MOVE_UNROOT_ACK` messages.

---

<!-- machine-true, projected from graph.json -->

## Map — MoveRootAck

*Source:* Movement.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| MoveRootAck | ctor | — | — | — |
