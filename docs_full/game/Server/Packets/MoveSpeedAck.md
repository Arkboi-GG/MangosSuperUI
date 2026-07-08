# MoveSpeedAck

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# MoveSpeedAck

`MoveSpeedAck` is a client-to-server packet structure within the `WorldPackets::Movement` namespace, defined in `Movement.h`. It serves as the acknowledgment message sent by the game client when it confirms a forced change to its movement speed. This packet is utilized for multiple specific speed-change opcodes, including run, walk, swim, and turn rate modifications.

## Purpose & Responsibilities

The primary responsibility of `MoveSpeedAck` is to deserialize and hold the data contained in the acknowledgment packets for forced speed changes. Specifically, it handles the following client opcodes:
*   `CMSG_FORCE_RUN_SPEED_CHANGE_ACK`
*   `CMSG_FORCE_RUN_BACK_SPEED_CHANGE_ACK`
*   `CMSG_FORCE_SWIM_SPEED_CHANGE_ACK`
*   `CMSG_FORCE_WALK_SPEED_CHANGE_ACK`
*   `CMSG_FORCE_SWIM_BACK_SPEED_CHANGE_ACK`
*   `CMSG_FORCE_TURN_RATE_CHANGE_ACK`

When the server sends a command to alter a player's speed, the client processes this change locally and sends back a `MoveSpeedAck` packet to confirm synchronization. This unit defines the memory layout and constructor for this packet but relies on the base class `ClientPacket` and the `ReadFromWorldPacket` method (defined elsewhere, likely in a corresponding `.cpp` file or inline implementation not shown in the provided header snippet, though declared here) for the actual deserialization logic.

## Member-by-Member Behavior

### **MoveSpeedAck** (Constructor)
The constructor initializes the `MoveSpeedAck` object. It sets the opcode field inherited from `ClientPacket` to `OPCODE_WILL_BE_SET_IN_READ_FUNCTION`. This indicates that the specific opcode is not known at construction time but will be determined dynamically during the reading process, allowing the same struct type to handle multiple different speed-change acknowledgment opcodes.

## Cross-Unit Boundaries

*   **Inherits from `ClientPacket`**: `MoveSpeedAck` derives from `ClientPacket`, inheriting the base functionality for client-side packet handling, such as opcode management and basic serialization interfaces.
*   **Uses `ObjectGuid`**: The `guid` member uses the `ObjectGuid` type, which is a core identifier system in the engine, linking the packet to a specific entity (player or creature).
*   **Uses `MovementInfo`**: The `movementInfo` member contains detailed movement state data, relying on the `MovementInfo` structure defined in `MovementInfo.h`.
*   **Called by Network Layer**: While not explicitly shown in the map's "Called by" column for this specific member, instances of `MoveSpeedAck` are typically instantiated and populated by the network layer when a matching opcode is received from the client. The `ReadFromWorldPacket` method (declared in this header) is the entry point for this deserialization.

## Data Model

This unit does not interact directly with any database tables. It operates purely on in-memory network packet data.

## Notable Implementation Details

*   **Opcode Flexibility**: The use of `OPCODE_WILL_BE_SET_IN_READ_FUNCTION` in the constructor is a key design choice. It allows `MoveSpeedAck` to be a generic handler for six different speed-related opcodes. The actual opcode is likely resolved inside the `ReadFromWorldPacket` implementation (not shown in the header) based on the incoming packet's opcode field.
*   **Client Build Compatibility**: The `movementCounter` field is conditionally compiled (`#if SUPPORTED_CLIENT_BUILD > CLIENT_BUILD_1_9_4`). This ensures binary compatibility with older client versions that do not include this counter in their packet structure, preventing deserialization errors or memory misalignment.
*   **Speed Value**: The `speed` member stores the confirmed speed value as a `float`. This is the critical piece of data the server needs to verify that the client has applied the correct speed modifier.

## Member Reference

**MoveSpeedAck**
Constructor for the `MoveSpeedAck` packet. Initializes the base `ClientPacket` with a placeholder opcode (`OPCODE_WILL_BE_SET_IN_READ_FUNCTION`), indicating that the specific opcode will be identified during the deserialization phase. This allows the same packet structure to handle multiple speed-change acknowledgment opcodes.

---

<!-- machine-true, projected from graph.json -->

## Map — MoveSpeedAck

*Source:* Movement.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| MoveSpeedAck | ctor | — | — | — |
