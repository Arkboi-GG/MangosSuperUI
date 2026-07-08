# Packet

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`Packet` is the abstract base class for network protocol messages in `wowvmangos`. It stores a `uint16` opcode to identify the message type. This unit also defines `ClientPacket`, which adds a pure virtual `ReadFromWorldPacket` method to enforce deserialization from raw binary data (`WorldPacket`), and `NullClientPacket`, a concrete implementation for packets with no relevant payload.

## Member-by-Member Behavior

### Core Packet Identification
*   **`Packet` (Constructor)**: Initializes the protected `opcode` member.
*   **`~Packet` (Destructor)**: Virtual destructor for polymorphic deletion.
*   **`GetOpcode`**: Returns the stored `opcode`.

### Client-Side Packet Handling
*   **`ClientPacket` (Constructor)**: Inherits from `Packet`. Declares the contract that derived classes must implement `ReadFromWorldPacket`.
*   **`ReadFromWorldPacket`**: Pure virtual method in `ClientPacket`. Derived classes implement this to parse `WorldPacket` data.

### Empty/Opaque Packet Handling
*   **`NullClientPacket`**: A `final` class for packets where the payload is ignored.
    *   **Constructors**: Accepts an explicit opcode or the sentinel `OPCODE_WILL_BE_SET_IN_READ_FUNCTION` (`0xFFFF`).
    *   **`ReadFromWorldPacket`**: Overrides the base method to update the internal `opcode` from the `WorldPacket` header, ignoring any payload.

## Cross-Unit Boundaries

### Collaboration with `WorldSession`
`WorldSession` (subsystems `Main` and `MovementHandler`) calls `Packet::GetOpcode` extensively.
*   **Routing**: `WorldSession.Main/Process` and `WorldSession.Main/ProcessPackets` use it to dispatch packets.
*   **Validation & Logging**: `WorldSession.Main/VerifyPacketWasCorrectlyRead` and `WorldSession.Main/LogUnexpectedOpcode` use it for integrity checks and error logging.
*   **Queuing**: `WorldSession.Main/QueuePacket` uses it for packet management.
*   **Movement**: `WorldSession.MovementHandler` methods (e.g., `HandleForceSpeedChangeAckOpcodes`, `HandleMoveKnockBackAck`) use it to identify specific movement acknowledgments.

### Dependency on `WorldPacket`
`ClientPacket` and `NullClientPacket` receive a `WorldPacket&` in `ReadFromWorldPacket`. `NullClientPacket` calls `recv_data.GetOpcode()` to retrieve the opcode from the raw packet.

## Data Model

This unit does not interact with any database tables.

## Notable Implementation Details

*   **Sentinel Opcode**: `OPCODE_WILL_BE_SET_IN_READ_FUNCTION` (`0xFFFF`) indicates the opcode is unknown at construction. `NullClientPacket` uses this when constructed without an explicit opcode, setting the real opcode during `ReadFromWorldPacket`.
*   **Polymorphism**: `Packet` has a virtual destructor. `ClientPacket` has a pure virtual method, making it abstract.
*   **Final Class**: `NullClientPacket` is `final`, preventing further derivation.

## Member Reference

**Packet** (ctor): Initializes the `opcode` member with the provided `uint16` value.

**~Packet** (dtor): Virtual destructor for safe polymorphic deletion of derived packet objects.

**GetOpcode** (method): Returns the stored `opcode`. Used by `WorldSession` for routing, validation, logging, and queuing decisions.

---

<!-- machine-true, projected from graph.json -->

## Map — Packet

*Source:* Packet.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| Packet | ctor | — | — | — |
| ~Packet | dtor | — | — | — |
| GetOpcode | method | — | WorldSession.Main/LogUnexpectedOpcode, WorldSession.Main/Process, WorldSession.Main/ProcessPackets, WorldSession.Main/QueuePacket, WorldSession.Main/VerifyPacketWasCorrectlyRead, WorldSession.MovementHandler/HandleForceSpeedChangeAckOpcodes, WorldSession.MovementHandler/HandleMoveKnockBackAck, WorldSession.MovementHandler/HandleMovementFlagChangeToggleAck, WorldSession.MovementHandler/HandleMovementOpcodes, WorldSession.MovementHandler/HandleMoveNotActiveMoverOpcode, WorldSession.MovementHandler/HandleMoveRootAck | — |
