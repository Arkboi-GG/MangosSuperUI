<!-- provenance: verbose -->
# Movement

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Movement Packet Handlers (`WorldPackets::Movement`)

## Purpose & Responsibilities

The `WorldPackets::Movement` namespace defines client-to-server packet structures for movement acknowledgments and state updates. These classes deserialize binary data from `WorldPacket` buffers into structured C++ objects (`MovementInfo`, `ObjectGuid`, scalars) for consumption by higher-level game logic. This unit handles confirmations for server-initiated changes (teleports, speed adjustments, roots, knockbacks) and client-reported states (lag, spline completion, mover switches). It performs no movement logic itself.

## Member-by-Member Behavior

Each class provides a `ReadFromWorldPacket` method that extracts fields according to the protocol for its specific opcode(s).

*   **Generic Movement**: `MovementPacket` (MAP: `ReadFromWorldPacket`) extracts the opcode and `movementInfo`.
*   **Lag**: `MoveTimeSkipped` (MAP: `ReadFromWorldPacket#8`) extracts `guid` and `lag`.
*   **Teleport**: `MoveTeleportAck` (MAP: `ReadFromWorldPacket#7`) extracts `guid`, optional `movementCounter` (build > 1.9.4), and `time`.
*   **Speed**: `MoveSpeedAck` (MAP: `ReadFromWorldPacket#5`) extracts opcode, `guid`, optional `movementCounter`, `movementInfo`, and `speed`. Handles multiple speed-change opcodes.
*   **Flags**: `MoveFlagChangeAck` (MAP: `ReadFromWorldPacket#4`) extracts opcode, `guid`, optional `movementCounter`, `movementInfo`, and converts a raw `uint32` to boolean `apply`. Handles hover/feather-fall/water-walk acks.
*   **Root**: `MoveRootAck` (MAP: `ReadFromWorldPacket#2`) extracts opcode, `guid`, optional `movementCounter`, and `movementInfo`. Handles root/unroot acks.
*   **Knockback**: `MoveKnockBackAck` (MAP: `ReadFromWorldPacket#3`) extracts `guid`, optional `movementCounter`, and `movementInfo`.
*   **Spline**: `MoveSplineDone` (MAP: `ReadFromWorldPacket#6`) extracts `movementInfo`, `splineId`, and skips a reserved float.
*   **Mover Switch**: `MoveNotActiveMover` (MAP: `ReadFromWorldPacket#9`) extracts optional `oldMoverGuid` (build > 1.9.4) and `movementInfo`.

## Cross-Unit Boundaries

*   **Calls Out**:
    *   `MovementInfo/operator>>`: Deserializes complex movement state in all movement-related packets.
    *   `ObjectGuid/operator>>`: Extracts entity identifiers in packets involving specific movers.
    *   `WorldPacket/GetOpcode`: Used by `MovementPacket`, `MoveSpeedAck`, `MoveFlagChangeAck`, and `MoveRootAck` to determine the specific sub-type at runtime, as multiple opcodes share these handlers.
    *   `ByteBuffer/operator>>#9` / `operator>>#8`: Reads scalar types (`uint32`, `float`) and skips reserved bytes.

*   **Called By**:
    *   Invoked by the central packet dispatch system (e.g., `WorldSession`) when matching opcodes arrive from the client.

## Data Model

No database tables are touched.

## Notable Implementation Details

1.  **Client Build Compatibility**: `movementCounter` is conditionally parsed (`#if SUPPORTED_CLIENT_BUILD > CLIENT_BUILD_1_9_4`) in `MoveTeleportAck`, `MoveSpeedAck`, `MoveFlagChangeAck`, `MoveRootAck`, `MoveKnockBackAck`, and `MoveNotActiveMover`. Omitting it for older clients prevents parse errors.
2.  **Boolean Parsing**: `MoveFlagChangeAck::ReadFromWorldPacket` reads `apply` as `uint32` then checks `!= 0u`.
3.  **Reserved Fields**: `MoveSplineDone::ReadFromWorldPacket` calls `read_skip<float>()` to consume an unused field.
4.  **Opcode Flexibility**: `MoveSpeedAck`, `MoveFlagChangeAck`, and `MoveRootAck` initialize with `OPCODE_WILL_BE_SET_IN_READ_FUNCTION` and read the opcode during parsing to support multiple related opcodes.

## Member Reference

**ReadFromWorldPacket#9**
`MoveNotActiveMover::ReadFromWorldPacket`: Extracts optional `oldMoverGuid` (build > 1.9.4) and `movementInfo`.

**ReadFromWorldPacket#8**
`MoveTimeSkipped::ReadFromWorldPacket`: Extracts `guid` and `lag`.

**MovementPacket**
`MovementPacket::MovementPacket`: Constructor initializing parent with placeholder opcode.

**ReadFromWorldPacket#7**
`MoveTeleportAck::ReadFromWorldPacket`: Extracts `guid`, optional `movementCounter`, and `time`.

**ReadFromWorldPacket#5**
`MoveSpeedAck::ReadFromWorldPacket`: Extracts opcode, `guid`, optional `movementCounter`, `movementInfo`, and `speed`.

**ReadFromWorldPacket**
`MovementPacket::ReadFromWorldPacket`: Extracts opcode and `movementInfo`.

**ReadFromWorldPacket#4**
`MoveFlagChangeAck::ReadFromWorldPacket`: Extracts opcode, `guid`, optional `movementCounter`, `movementInfo`, and boolean `apply`.

**ReadFromWorldPacket#2**
`MoveRootAck::ReadFromWorldPacket`: Extracts opcode, `guid`, optional `movementCounter`, and `movementInfo`.

**ReadFromWorldPacket#6**
`MoveSplineDone::ReadFromWorldPacket`: Extracts `movementInfo`, `splineId`, and skips a float.

**ReadFromWorldPacket#3**
`MoveKnockBackAck::ReadFromWorldPacket`: Extracts `guid`, optional `movementCounter`, and `movementInfo`.

---

<!-- machine-true, projected from graph.json -->

## Map — Movement

*Source:* Movement.cpp, Movement.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ReadFromWorldPacket#9 | method | MovementInfo/operator>>, WorldPacket/GetOpcode | — | — |
| ReadFromWorldPacket#8 | method | ByteBuffer/operator>>#9, ObjectGuid/operator>> | — | — |
| MovementPacket | ctor | — | — | — |
| ReadFromWorldPacket#7 | method | ByteBuffer/operator>>#9, ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket#5 | method | ByteBuffer/operator>>#8, ByteBuffer/operator>>#9, MovementInfo/operator>>, ObjectGuid/operator>>, WorldPacket/GetOpcode | — | — |
| ReadFromWorldPacket | method | ByteBuffer/operator>>#9, MovementInfo/operator>>, ObjectGuid/operator>>, WorldPacket/GetOpcode | — | — |
| ReadFromWorldPacket#4 | method | ByteBuffer/operator>>#9, MovementInfo/operator>>, ObjectGuid/operator>>, WorldPacket/GetOpcode | — | — |
| ReadFromWorldPacket#2 | method | ByteBuffer/operator>>#9, MovementInfo/operator>>, ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket#6 | method | ByteBuffer/operator>>#9, MovementInfo/operator>> | — | — |
| ReadFromWorldPacket#3 | method | MovementInfo/operator>>, ObjectGuid/operator>> | — | — |
