# MovementPacketSender

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# MovementPacketSender

**Purpose & Responsibilities**

`MovementPacketSender` is a namespace containing static helper functions responsible for constructing and dispatching network packets related to character movement state changes. Its primary role is to abstract the complexity of World of Warcraft's movement protocol, which distinguishes between two distinct control models:

1.  **Player-Controlled Units:** Units directly controlled by a client (e.g., the player's own character, or a possessed pet). Changes to these units require a specific handshake: the server sends a "force" packet to the controlling client, the client acknowledges it, and then the server broadcasts the change to all other nearby clients ("observers"). `MovementPacketSender` manages this three-step process, including tracking pending changes via `Unit`'s internal state.
2.  **Server-Controlled Units:** Units controlled entirely by the server (e.g., NPCs, pets not possessed by players, or players under certain crowd-control effects like fear). Changes to these units are broadcast directly to all observers using spline-based opcodes, without requiring client acknowledgment.

The unit handles speed changes, teleportation, knockbacks, movement flag toggles (root, hover, water walking, feather fall), and run/walk mode switches. It contains extensive preprocessor logic to support multiple client versions (1.8.4 through 1.9.4+), adjusting opcodes and packet structures accordingly. It does not interact with any database tables.

## Member-by-Member Behavior

The members are grouped by the type of movement change they facilitate.

### Speed Changes

Speed changes involve modifying the rate at which a unit moves (walk, run, swim, turn).

*   **`AddSpeedChangeToController`**: Initiates a speed change for a player-controlled unit. It validates that the unit is indeed player-controlled (logging an error if not). It calculates the flat speed value from the provided rate multiplier, increments the unit's movement counter, creates a `PlayerMovementPendingChange` record, pushes it to the unit's pending queue, and immediately calls `SendSpeedChangeToController` to notify the client.
*   **`SendSpeedChangeToController`**: Constructs the "force speed change" packet (`SMSG_FORCE_*_SPEED_CHANGE`) for the controlling player. It serializes the unit's GUID, the movement counter (for newer clients), and the new flat speed value. It logs the packet for anticheat purposes and sends it to the player's session.
*   **`SendSpeedChangeToObservers`**: Broadcasts the speed change to other players. It checks if the unit's current movement spline is finalized. If so, it uses a `MSG_MOVE_SET_*_SPEED` opcode and includes the full `MovementInfo`. If not finalized, it uses a `SMSG_SPLINE_SET_*_SPEED` opcode (for newer clients) or a simpler structure for older ones, sending only the GUID and speed.
*   **`SendSpeedChangeToAll`**: Broadcasts a speed change to all observers for server-controlled units. It always uses the spline-based opcode (`SMSG_SPLINE_SET_*_SPEED` or legacy equivalent) and sends the calculated flat speed.

### Teleportation

Teleportation instantly moves a unit to new coordinates.

*   **`SendTeleportToController`**: Handles teleporting a player-controlled unit. It validates player control, increments the movement counter, records the pending change as a `TELEPORT` type, and constructs a `MSG_MOVE_TELEPORT_ACK` packet. This packet includes the new position within a modified `MovementInfo` object marked as server-side. It sends this to the controlling player.
*   **`SendTeleportToObservers`**: Broadcasts the teleport to other players. It constructs a `MSG_MOVE_TELEPORT` packet containing the unit's GUID and the updated `MovementInfo` with the new coordinates. It sends this directly to the observer set, bypassing the standard movement message queue to ensure immediate delivery.

### Knockback

Knockback applies a force vector to a unit.

*   **`SendKnockBackToController`**: Initiates a knockback for a player-controlled unit. It validates control, increments the counter, records the pending change with the knockback vector components (vcos, vsin, speedXY, speedZ), and sends an `SMSG_MOVE_KNOCK_BACK` packet to the controlling player.
*   **`SendKnockBackToObservers`**: Broadcasts the knockback to other players. It constructs a `MSG_MOVE_KNOCK_BACK` packet including the unit's current `MovementInfo` and the knockback vector components, then sends it to the observer set.

### Movement Flag Changes

Movement flags alter how a unit interacts with physics (rooted, hovering, etc.).

*   **`AddMovementFlagChangeToController`**: Initiates a flag change for a player-controlled unit. It maps the `MovementFlags` enum to an internal `MovementChangeType`. It validates player control, increments the counter, records the pending change (including whether the flag is being applied or removed), and calls `SendMovementFlagChangeToController`.
*   **`SendMovementFlagChangeToController`**: Constructs the appropriate "force" packet (e.g., `SMSG_FORCE_MOVE_ROOT`, `SMSG_MOVE_WATER_WALK`) based on the flag and apply status. It sends this to the controlling player.
*   **`SendMovementFlagChangeToObservers`**: Broadcasts the flag change to other players. It selects the corresponding `MSG_MOVE_*` opcode and sends a packet containing the unit's GUID and current `MovementInfo`.
*   **`SendMovementFlagChangeToAll`**: Broadcasts flag changes for server-controlled units. It uses spline-based opcodes (`SMSG_SPLINE_MOVE_*`) for newer clients. Note the special handling for `MOVEFLAG_ROOT` on clients between 1.8.4 and 1.9.4: applying root uses a legacy `MSG_MOVE_ROOT` packet sent immediately, while unrooting uses the spline opcode.

### Run/Walk Mode

*   **`SendToggleRunWalkToAll`**: Broadcasts a change between running and walking modes for server-controlled units. It uses `SMSG_SPLINE_MOVE_SET_RUN_MODE` or `SMSG_SPLINE_MOVE_SET_WALK_MODE` for newer clients, or legacy `MSG_MOVE_*` opcodes for older ones.

### Utility Functions

*   **`GetChangeTypeByMoveType`**: Converts a `UnitMoveType` (e.g., `MOVE_RUN`) to an internal `MovementChangeType` (e.g., `SPEED_CHANGE_RUN`). Used by `AddSpeedChangeToController`.
*   **`GetMoveTypeByChangeType`**: Converts an internal `MovementChangeType` back to a `UnitMoveType`. Used by `SendSpeedChangeToController`. Both functions assert on unsupported types.

## Cross-Unit Boundaries

`MovementPacketSender` acts as a bridge between high-level game logic (`Unit`, `Player`) and low-level networking (`WorldPacket`, `WorldSession`).

*   **Called By `Unit.Main`**:
    *   `Unit.Main/SetSpeedRate` calls `AddSpeedChangeToController` and `GetChangeTypeByMoveType` to initiate speed changes for player-controlled units.
    *   `Unit.Main/SetSpeedRate` also calls `SendSpeedChangeToAll` for server-controlled units.
    *   `Unit.Main/SetFeatherFall`, `SetHover`, `SetRooted`, `SetWaterWalking` call `AddMovementFlagChangeToController` (for player-controlled) and `SendMovementFlagChangeToAll` (for server-controlled).
    *   `Unit.Main/ResolvePendingMovementChange` calls `SendSpeedChangeToAll` and `SendMovementFlagChangeToAll` to finalize pending changes.
    *   `Unit.Main/TeleportTo` calls `SendTeleportToController`.
    *   `Unit.Main/NearTeleportTo` calls `SendTeleportToObservers`.
    *   `Unit.Main/KnockBack` calls `SendKnockBackToController`.
    *   `Unit.Main/SetWalk` calls `SendToggleRunWalkToAll`.
    *   `Unit.SpellAuras/ModPossess` and `ModPossessPet` call `AddMovementFlagChangeToController` to handle possession-related movement states.

*   **Called By `Player.Main`**:
    *   `Player.Main/TeleportTo` calls `SendTeleportToController`.

*   **Calls Out To `Unit.Main`**:
    *   Retrieves player controller via `GetPlayerMovingMe`.
    *   Manages movement state via `GetMovementCounterAndInc`, `PushPendingMovementChange`, and `PlayerMovementPendingChange`.
    *   Sends packets to observers via `SendMovementMessageToSet` and `SendObjectMessageToSet`.

*   **Calls Out To `Player.Main`**:
    *   Accesses session and cheat data via `GetSession` and `GetCheatData` to send packets and log them.

*   **Calls Out To `MovementAnticheat`**:
    *   Logs outgoing movement packets via `LogMovementPacket` for anticheat analysis.

*   **Calls Out To `WorldPacket` / `ByteBuffer`**:
    *   Constructs and serializes packet data.

*   **Calls Out To `Object`**:
    *   Retrieves GUIDs (`GetGuidStr`, `GetObjectGuid`, `GetPackGUID`) for logging and serialization.

*   **Calls Out To `Log.Main`**:
    *   Logs errors when functions are misused (e.g., calling controller functions on server-controlled units).

*   **Called By `WorldSession.MovementHandler`**:
    *   Various handlers (`HandleForceSpeedChangeAckOpcodes`, `HandleMoveKnockBackAck`, `HandleMovementFlagChangeToggleAck`, `HandleMoveRootAck`, `HandleSetActiveMoverOpcode`) call the corresponding `Send...ToObservers` or `Add...ToController` functions to complete the handshake after receiving client acknowledgments.

*   **Called By `MoveSplineInit`**:
    *   `MoveSplineInit/Launch` calls `SendMovementFlagChangeToAll` and `SendToggleRunWalkToAll` when launching splines for server-controlled units.

## Data Model

This unit does not access any database tables. All state is held in memory within `Unit` objects (specifically the `PlayerMovementPendingChange` queue) and transmitted over the network.

## Notable Implementation Details

1.  **Control Model Validation**: Every function ending in `ToController` or `ToObservers` (except `ToAll`) explicitly checks if the unit has a player mover (`unit->GetPlayerMovingMe()`). If not, it logs an error and returns. This enforces the separation between player-controlled and server-controlled movement logic. Misusing these functions on server-controlled units is a bug.
2.  **Pending Change Queue**: For player-controlled units, changes are not applied immediately on the server side upon sending the packet. Instead, a `PlayerMovementPendingChange` is pushed to the unit's queue. The actual state update likely occurs later when the client acknowledges the change (handled by `WorldSession.MovementHandler` calling back into `Unit` or `MovementPacketSender`). This prevents race conditions and ensures the client and server stay synchronized.
3.  **Client Version Compatibility**: The code is heavily guarded by `#if SUPPORTED_CLIENT_BUILD` directives.
    *   **GUID Packing**: Clients > 1.8.4 use `GetPackGUID()` (variable length), while older clients use `GetGUID()` (fixed 8 bytes).
    *   **OpCodes**: Newer clients use `SMSG_SPLINE_*` opcodes for many movements, while older clients use `MSG_MOVE_*`.
    *   **Movement Counter**: Clients > 1.9.4 include a movement counter in many packets to prevent replay attacks or desyncs.
    *   **Packet Structure**: Older clients often require the full `MovementInfo` struct in packets, while newer clients may only need the GUID and specific values.
4.  **Immediate vs. Queued Sending**: `SendTeleportToObservers` uses `SendObjectMessageToSet` instead of `SendMovementMessageToSet`. The comment explains this is to ensure the packet is sent immediately, bypassing any potential queuing mechanism that might delay the teleport broadcast.
5.  **Special Root Handling**: In `SendMovementFlagChangeToAll`, applying a root flag for clients between 1.8.4 and 1.9.4 uses a legacy `MSG_MOVE_ROOT` packet sent immediately, while unrooting uses the spline opcode. This asymmetry is likely due to client-specific quirks in how roots were processed during that era.
6.  **Anticheat Logging**: All packets sent to controllers are logged via `mover->GetCheatData()->LogMovementPacket(false, data)`. This allows the anticheat system to monitor server-initiated movement changes for anomalies.
7.  **Base Speed Calculation**: `AddSpeedChangeToController` and `SendSpeedChangeToAll` multiply the provided `newRate` by `baseMoveSpeed[mtype]`. This implies `newRate` is a multiplier relative to the base speed for that movement type, not an absolute speed value.

## Member Reference

**AddSpeedChangeToController**: Validates player control, calculates flat speed, increments movement counter, creates and pushes a `PlayerMovementPendingChange` record, and calls `SendSpeedChangeToController`. Called by `Unit.Main/SetSpeedRate`.

**SendSpeedChangeToController**: Constructs and sends the "force speed change" packet to the controlling player, including GUID, counter (if applicable), and flat speed. Logs for anticheat. Called by `AddSpeedChangeToController`.

**GetChangeTypeByMoveType**: Converts `UnitMoveType` to `MovementChangeType`. Asserts on unsupported types. Called by `AddSpeedChangeToController` and `Unit.Main/SetSpeedRate`.

**GetMoveTypeByChangeType**: Converts `MovementChangeType` to `UnitMoveType`. Asserts on unsupported types. Called by `SendSpeedChangeToController`.

**SendSpeedChangeToObservers**: Broadcasts speed change to observers. Uses different opcodes/packet structures depending on whether the unit's spline is finalized. Called by `WorldSession.MovementHandler/HandleForceSpeedChangeAckOpcodes`.

**SendSpeedChangeToAll**: Broadcasts speed change to all observers for server-controlled units using spline opcodes. Called by `Unit.Main/SetSpeedRate`, `Unit.Main/ResolvePendingMovementChange`, and `MovementAnticheat/HandlePositionTests`.

**SendTeleportToController**: Validates player control, records pending teleport change, constructs `MSG_MOVE_TELEPORT_ACK` with new position, and sends to controlling player. Called by `Player.Main/TeleportTo`.

**SendTeleportToObservers**: Broadcasts teleport to observers using `MSG_MOVE_TELEPORT` with updated `MovementInfo`. Sends immediately. Called by `Unit.Main/NearTeleportTo` and `WorldSession.MovementHandler/ExecuteTeleportNear`.

**SendKnockBackToController**: Validates player control, records pending knockback change, constructs `SMSG_MOVE_KNOCK_BACK` with vector components, and sends to controlling player. Called by `Unit.Main/KnockBack`.

**SendKnockBackToObservers**: Broadcasts knockback to observers using `MSG_MOVE_KNOCK_BACK` with `MovementInfo` and vector components. Called by `WorldSession.MovementHandler/HandleMoveKnockBackAck`.

**AddMovementFlagChangeToController**: Maps `MovementFlags` to `MovementChangeType`, validates player control, records pending flag change, and calls `SendMovementFlagChangeToController`. Called by `Unit.Main/SetFeatherFall`, `SetHover`, `SetRooted`, `SetWaterWalking`, `Unit.SpellAuras/ModPossess`, `ModPossessPet`, and `WorldSession.MovementHandler/HandleSetActiveMoverOpcode`.

**SendMovementFlagChangeToController**: Constructs and sends the appropriate "force" flag change packet to the controlling player. Called by `AddMovementFlagChangeToController`.

**SendMovementFlagChangeToObservers**: Broadcasts flag change to observers using `MSG_MOVE_*` opcodes and `MovementInfo`. Called by `WorldSession.MovementHandler/HandleMovementFlagChangeToggleAck` and `HandleMoveRootAck`.

**SendMovementFlagChangeToAll**: Broadcasts flag change to all observers for server-controlled units using spline opcodes. Special handling for root on intermediate client versions. Called by `Unit.Main/SetFeatherFall`, `SetHover`, `SetRooted`, `SetWaterWalking`, `Unit.Main/ResolvePendingMovementChange`, and `MoveSplineInit/Launch`.

**SendToggleRunWalkToAll**: Broadcasts run/walk mode change to all observers for server-controlled units using spline or legacy opcodes. Called by `Unit.Main/SetWalk` and `MoveSplineInit/Launch`.

---

<!-- machine-true, projected from graph.json -->

## Map — MovementPacketSender

*Source:* MovementPacketSender.cpp, MovementPacketSender.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| AddSpeedChangeToController | function | Log.Main/Out, Object/GetGuidStr, Object/GetObjectGuid, Unit.Main/GetMovementCounterAndInc, Unit.Main/GetPlayerMovingMe, Unit.Main/PlayerMovementPendingChange, Unit.Main/PushPendingMovementChange | Unit.Main/SetSpeedRate | — |
| SendSpeedChangeToController | function | ByteBuffer/operator<<#10, ByteBuffer/operator<<#9, MovementAnticheat/LogMovementPacket, Object/GetPackGUID, ObjectGuid/operator<<#2, Player.Main/GetCheatData, Player.Main/GetSession, WorldPacket/Initialize, WorldPacket/WorldPacket, WorldSession.Main/SendPacket | — | — |
| GetChangeTypeByMoveType | function | Errors/PrintStacktraceAndThrow | Unit.Main/SetSpeedRate | — |
| GetMoveTypeByChangeType | function | Errors/PrintStacktraceAndThrow | — | — |
| SendSpeedChangeToObservers | function | ByteBuffer/operator<<#9, Log.Main/Out, MovementInfo/operator<<, MoveSpline/Finalized, Object/GetGuidStr, Object/GetPackGUID, ObjectGuid/operator<<#2, Unit.Main/GetPlayerMovingMe, WorldObject.Object/SendMovementMessageToSet, WorldPacket/Initialize, WorldPacket/WorldPacket, WorldPacket/WorldPacket#2 | WorldSession.MovementHandler/HandleForceSpeedChangeAckOpcodes | — |
| SendSpeedChangeToAll | function | ByteBuffer/operator<<#9, Object/GetPackGUID, ObjectGuid/operator<<#2, WorldObject.Object/SendMovementMessageToSet, WorldPacket/Initialize, WorldPacket/WorldPacket, WorldPacket/WorldPacket#2 | MovementAnticheat/HandlePositionTests, Unit.Main/ResolvePendingMovementChange, Unit.Main/SetSpeedRate | — |
| SendTeleportToController | function | ByteBuffer/operator<<#10, Log.Main/Out, MovementAnticheat/LogMovementPacket, MovementInfo/ChangePosition, MovementInfo/operator<<, MovementInfo/SetAsServerSide, Object/GetGuidStr, Object/GetObjectGuid, Object/GetPackGUID, ObjectGuid/operator<<#2, Player.Main/GetCheatData, Player.Main/GetSession, Unit.Main/GetMovementCounterAndInc, Unit.Main/GetPlayerMovingMe, Unit.Main/PlayerMovementPendingChange, Unit.Main/PushPendingMovementChange, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | Player.Main/TeleportTo | — |
| SendTeleportToObservers | function | MovementInfo/ChangePosition, MovementInfo/operator<<, Object/GetPackGUID, ObjectGuid/operator<<#2, Unit.Main/GetPlayerMovingMe, WorldObject.Object/SendObjectMessageToSet, WorldPacket/WorldPacket#4 | Unit.Main/NearTeleportTo, WorldSession.MovementHandler/ExecuteTeleportNear | — |
| SendKnockBackToController | function | ByteBuffer/operator<<#10, ByteBuffer/operator<<#9, Log.Main/Out, MovementAnticheat/LogMovementPacket, Object/GetGuidStr, Object/GetObjectGuid, Object/GetPackGUID, ObjectGuid/operator<<#2, Player.Main/GetCheatData, Player.Main/GetSession, Unit.Main/GetMovementCounterAndInc, Unit.Main/GetPlayerMovingMe, Unit.Main/PlayerMovementPendingChange, Unit.Main/PushPendingMovementChange, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | Unit.Main/KnockBack | — |
| SendKnockBackToObservers | function | ByteBuffer/operator<<#9, Log.Main/Out, MovementInfo/operator<<, Object/GetGuidStr, Object/GetPackGUID, ObjectGuid/operator<<#2, Unit.Main/GetPlayerMovingMe, WorldObject.Object/SendMovementMessageToSet, WorldPacket/WorldPacket#2, WorldPacket/WorldPacket#4 | WorldSession.MovementHandler/HandleMoveKnockBackAck | — |
| AddMovementFlagChangeToController | function | Log.Main/Out, Object/GetGuidStr, Object/GetObjectGuid, Unit.Main/GetMovementCounterAndInc, Unit.Main/GetPlayerMovingMe, Unit.Main/PlayerMovementPendingChange, Unit.Main/PushPendingMovementChange | Unit.Main/SetFeatherFall, Unit.Main/SetHover, Unit.Main/SetRooted, Unit.Main/SetWaterWalking, Unit.SpellAuras/ModPossess, Unit.SpellAuras/ModPossessPet, WorldSession.MovementHandler/HandleSetActiveMoverOpcode | — |
| SendMovementFlagChangeToController | function | ByteBuffer/operator<<#10, Log.Main/Out, MovementAnticheat/LogMovementPacket, Object/GetGuidStr, Object/GetPackGUID, ObjectGuid/operator<<#2, PackedGuid/size, Player.Main/GetCheatData, Player.Main/GetSession, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | — | — |
| SendMovementFlagChangeToObservers | function | Log.Main/Out, MovementInfo/operator<<, Object/GetGuidStr, Object/GetPackGUID, ObjectGuid/operator<<#2, Unit.Main/GetPlayerMovingMe, WorldObject.Object/SendMovementMessageToSet, WorldPacket/WorldPacket#2, WorldPacket/WorldPacket#4 | WorldSession.MovementHandler/HandleMovementFlagChangeToggleAck, WorldSession.MovementHandler/HandleMoveRootAck | — |
| SendMovementFlagChangeToAll | function | Log.Main/Out, Object/GetGuidStr, Object/GetPackGUID, ObjectGuid/operator<<#2, PackedGuid/size, WorldObject.Object/SendMovementMessageToSet, WorldPacket/WorldPacket#2, WorldPacket/WorldPacket#4 | MoveSplineInit/Launch, Unit.Main/ResolvePendingMovementChange, Unit.Main/SetFeatherFall, Unit.Main/SetHover, Unit.Main/SetRooted, Unit.Main/SetWaterWalking | — |
| SendToggleRunWalkToAll | function | Object/GetPackGUID, ObjectGuid/operator<<#2, WorldObject.Object/SendMovementMessageToSet, WorldPacket/WorldPacket#2, WorldPacket/WorldPacket#4 | MoveSplineInit/Launch, Unit.Main/SetWalk | — |
