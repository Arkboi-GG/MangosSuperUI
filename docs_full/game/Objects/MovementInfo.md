<!-- provenance: verbose -->
# MovementInfo

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# MovementInfo

`MovementInfo` is a passive data structure representing the complete movement state of a unit (player or NPC) at a specific instant. It serves as the canonical payload for movement-related network packets and the internal state holder for position, orientation, movement flags, and transport association. The class contains no business logic; it provides serialization (`Read`/`Write`), accessors, and mutators for its fields.

Key responsibilities:
1.  **Network Serialization:** Converts binary `ByteBuffer` data to/from structured fields via `Read`, `Write`, and overloaded stream operators.
2.  **State Container:** Holds `Position` (world coords), `t_pos` (transport-relative coords), `moveFlags` (bitmask of movement modes), timestamps (`stime`, `ctime`), and jump physics data.
3.  **Transport Context:** Tracks whether a unit is on a transport via `t_guid` and `t_pos`.
4.  **Anti-Cheat Metadata:** Stores `sourceSessionGuid` and client/server times to enable `MovementAnticheat` to validate packet origins and timing.

## Member-by-Member Behavior

### Construction and Initialization
*   **`MovementInfo`**: Default constructor initializes `moveFlags` to `MOVEFLAG_NONE`, timestamps to 0, and all positional/angular fields to 0.0f. The nested `JumpInfo` struct is also zero-initialized.

### Movement Flag Manipulation
The `moveFlags` bitmask controls client-side interpretation of movement (walking, flying, jumping, etc.).
*   **`AddMovementFlag`**: Sets a bit in `moveFlags` using bitwise OR. Called by `Unit.Main` (e.g., `SetWalk`, `SetFly`), `Transport` (`AddPassenger`), and `WorldSession.MovementHandler` to activate states.
*   **`RemoveMovementFlag`**: Clears a bit in `moveFlags` using bitwise AND with inverse mask. Called by `MovementAnticheat`, `Player.Main` (teleports, instance switches), and `Unit.Main` (spell effects like `SetFeignDeath`) to deactivate states.
*   **`HasMovementFlag`**: Returns `true` if a flag is set. Heavily queried by `MovementAnticheat` (e.g., `CheckMoveStart`, `CheckFallReset`) and `WorldSession.MovementHandler` to validate consistency between reported state and physics.
*   **`GetMovementFlags`**: Returns `moveFlags` cast to `MovementFlags` enum. Used by `MoveSplineInit/Launch` to configure spline animations.
*   **`SetMovementFlags`**: Overwrites `moveFlags`. Used by `MoveSplineInit/Launch` to enforce a specific state for splines.

### Position and Orientation
*   **`GetPos`**: Returns const reference to `pos` (world coordinates). Used by `Player.Main/HandleFall` and `WorldSession.MovementHandler/VerifyMovementInfo`.
*   **`ChangeOrientation`**: Updates `pos.o`. Called by `Unit.Main/SetFacingTo` and `WorldObject.Object/SetOrientation`.
*   **`ChangePosition`**: Updates `pos.x/y/z/o`. Called by `MovementPacketSender`, `Unit.Main/NearLandTo`, and `WorldObject.Object/BuildMovementUpdate` to relocate units in memory.

### Transport Data
Units on transports store position relative to the transport vehicle.
*   **`SetTransportData`**: Sets `t_guid` and `t_pos`. Called by `Player.Main/LoadFromDB` and `Transport/AddFollowerToTransport`.
*   **`ClearTransportData`**: Resets `t_guid` to empty and `t_pos` to zeros. Called by `Player.Main/TeleportTo`, `SwitchInstance`, and `Transport/RemovePassenger`.
*   **`GetTransportGuid`**: Returns `t_guid`. Used by `MovementAnticheat` and `WorldSession.MovementHandler` to identify the associated transport.
*   **`GetTransportPos`**: Const overload returning `t_pos`. Used by `Player.Main/SaveToDB` and `WorldSession.CharacterHandler`.
*   **`GetTransportPos#2`**: Non-const overload returning `t_pos`. Used by `WorldSession.MovementHandler/HandleMoverRelocation` to modify relative position.

### Timing and Anti-Cheat Metadata
*   **`UpdateTime`**: Sets `stime` (server time). Called by `WorldSession.MovementHandler` handlers to stamp received packets.
*   **`SetAsServerSide`**: Marks data as server-originated. Sets `stime` to current world time (ensuring monotonicity: `if (oldTime >= stime) stime = oldTime + 1`), sets `ctime` to 0 (disabling client extrapolation), and clears `sourceSessionGuid`. Called by `MovementPacketSender` and `Unit.Main` for authoritative updates.
*   **`WasSentBySession`**: Checks if `ctime != 0` and `sourceSessionGuid` matches a given ID. Used exclusively by `MovementAnticheat` to verify packet origin.

### Jump Information
*   **`GetJumpInfo`**: Returns const reference to `jump` struct containing `zspeed`, `sinAngle`, `cosAngle`, `xyspeed`, and start position/time. No external callers listed in the map.

### Serialization Helpers
*   **`MoveFlagToString`**: Static utility converting `uint32` flag to string. No external callers.
*   **`MoveFlagToString#2`**: Second overload (likely for enum type). No external callers.
*   **`operator<<`**: Serializes `MovementInfo` to `ByteBuffer` by calling `Write`. Called by `MovementPacketSender` and `Unit.Main`.
*   **`operator>>`**: Deserializes `ByteBuffer` to `MovementInfo` by calling `Read`. Called by `Movement/ReadFromWorldPacket` variants.

## Cross-Unit Boundaries

`MovementInfo` is a data carrier, rarely initiating calls. It is consumed by:

1.  **WorldSession.MovementHandler**: Primary input path. Handlers like `HandleMovementOpcodes` deserialize packets via `operator>>`, validate via `HasMovementFlag`/`GetPos`, stamp time via `UpdateTime`, and pass the struct to `Unit`/`Player` for state application.
2.  **MovementPacketSender**: Primary output path. Serializes `MovementInfo` via `operator<<` to send updates to observers or the controller. Calls `SetAsServerSide` before sending to ensure server authority.
3.  **MovementAnticheat**: Validates integrity. Uses `HasMovementFlag` to check for impossible states (e.g., moving while rooted), `WasSentBySession` to detect spoofed sources, and `GetTransportGuid`/`GetTransportPos` to verify transport bounds.
4.  **Unit.Main / Player.Main**: State management. Call `AddMovementFlag`/`RemoveMovementFlag` for spell/ability effects, and `ChangePosition`/`SetTransportData` for programmatic relocations.

## Data Model

`MovementInfo` does not interact with database tables directly. It is an in-memory structure. Its contents are persisted indirectly via `Player.Main/SaveToDB` (writing position and transport data to the `characters` table) and loaded via `Player.Main/LoadFromDB`. No SQL queries exist in this unit.

## Notable Implementation Details

1.  **Monotonic Server Time**: `SetAsServerSide` enforces `stime` monotonicity. If the new server time is less than or equal to the stored `stime`, it increments `stime` by 1. This prevents client interpolation errors caused by non-monotonic timestamps in rapid server updates.
2.  **Extrapolation Control**: Setting `ctime = 0` in `SetAsServerSide` signals the client to stop extrapolating and snap to the new position, ensuring authoritative server moves override client predictions.
3.  **Flag Masks**: The `MovementFlags` enum defines masks (`MOVEFLAG_MASK_MOVING`, `MOVEFLAG_MASK_XZ`) used by external units to check groups of flags efficiently.
4.  **Transport Coordinates**: `t_pos` is relative to the transport. `ClearTransportData` zeroes `t_pos`, which is a safe default for "not on transport."

## Member Reference

**MoveFlagToString**
Static utility converting `uint32` flag to human-readable string. No external callers.

**MoveFlagToString#2**
Second overload of flag-to-string converter. No external callers.

**MovementInfo**
Default constructor initializing all fields to zero/empty states.

**AddMovementFlag**
Sets a bit in `moveFlags` via bitwise OR. Called by `Player.Main`, `Transport`, `Unit.Main`, `WorldSession.MovementHandler`.

**RemoveMovementFlag**
Clears a bit in `moveFlags` via bitwise AND with inverse mask. Called by `MovementAnticheat`, `Player.Main`, `Unit.Main`, `WorldObject.Object`.

**HasMovementFlag**
Returns `true` if flag is set. Called by `ChatHandler.UnitCommands`, `Map.Main`, `MovementAnticheat`, `MoveSplineInit`, `Player.Main`, `Spell.Main`, `Unit.Main`, `WorldObject.Object`, `WorldSession.MiscHandler`, `WorldSession.MovementHandler`.

**GetMovementFlags**
Returns `moveFlags` as `MovementFlags` enum. Called by `MoveSplineInit/Launch`.

**SetMovementFlags**
Overwrites `moveFlags`. Called by `MoveSplineInit/Launch`.

**GetPos**
Returns const ref to `pos`. Called by `MovementAnticheat`, `Player.Main`, `WorldSession.MovementHandler`.

**SetTransportData**
Sets `t_guid` and `t_pos`. Called by `Player.Main/LoadFromDB`, `Transport/AddFollowerToTransport`.

**ClearTransportData**
Resets `t_guid` and `t_pos` to zero/empty. Called by `Player.Main`, `Transport`, `WorldSession.MovementHandler`.

**GetTransportGuid**
Returns `t_guid`. Called by `MovementAnticheat`, `WorldSession.MovementHandler`.

**GetTransportPos#2**
Non-const ref to `t_pos`. Called by `MovementAnticheat`, `Player.Main`, `WorldSession.MovementHandler`.

**GetTransportPos**
Const ref to `t_pos`. Called by `Player.Main`, `Unit.Main`, `WorldSession.CharacterHandler`, `WorldSession.MovementHandler`.

**GetFallTime**
Returns `fallTime`. Called by `Player.Main/HandleFall`.

**ChangeOrientation**
Updates `pos.o`. Called by `Unit.Main/SetFacingTo`, `WorldObject.Object/SetOrientation`.

**ChangePosition**
Updates `pos.x/y/z/o`. Called by `MovementPacketSender`, `Unit.Main`, `WorldObject.Object`.

**UpdateTime**
Sets `stime`. Called by `WorldObject.Object`, `WorldSession.MovementHandler`.

**SetAsServerSide**
Marks as server-originated: updates `stime` monotonically, zeros `ctime`, clears `sourceSessionGuid`. Called by `MovementPacketSender`, `Transport`, `Unit.Main`.

**WasSentBySession**
Checks `ctime` and `sourceSessionGuid`. Called by `MovementAnticheat`.

**GetJumpInfo**
Returns const ref to `jump` struct. No external callers.

**operator<<**
Serializes to `ByteBuffer` via `Write`. Called by `MovementPacketSender`, `Unit.Main`, `WorldObject.Object`.

**operator>>**
Deserializes from `ByteBuffer` via `Read`. Called by `Movement/ReadFromWorldPacket` variants.

---

<!-- machine-true, projected from graph.json -->

## Map — MovementInfo

*Source:* MovementInfo.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| MoveFlagToString | function | — | — | — |
| MoveFlagToString#2 | function | — | — | — |
| MovementInfo | ctor | — | — | — |
| AddMovementFlag | method | — | Player.Main/SendInitialPacketsBeforeAddToMap, Player.Main/SetCheatFixedZ, Transport/AddPassenger, Unit.Main/SetFly, Unit.Main/SetLevitate, Unit.Main/SetWalk, WorldSession.MovementHandler/HandleMoverRelocation | — |
| RemoveMovementFlag | method | — | MovementAnticheat/HandleFlagTests, Player.Main/SetCheatFixedZ, Player.Main/SwitchInstance, Player.Main/TeleportTo, Unit.Main/DisableSpline, Unit.Main/ModConfuseSpell, Unit.Main/NearLandTo, Unit.Main/SetFeignDeath, Unit.Main/SetFly, Unit.Main/SetLevitate, Unit.Main/SetWalk, WorldObject.Object/CorrectData | — |
| HasMovementFlag | method | — | ChatHandler.UnitCommands/HandleUnitMoveInfoCommand, Map.Main/PlayerRelocation, MovementAnticheat/CheckBotting, MovementAnticheat/CheckFakeTransport, MovementAnticheat/CheckFallReset, MovementAnticheat/CheckFallStop, MovementAnticheat/CheckMoveStart, MovementAnticheat/CheckNoFallTime, MovementAnticheat/CheckTeleport, MovementAnticheat/CheckTeleportToTransport, MovementAnticheat/GetMoveTypeForMovementInfo, MovementAnticheat/HandlePositionTests, MovementAnticheat/HandleSplineDone, MoveSplineInit/MoveSplineInit, Player.Main/HandleFall, Player.Main/HasMovementFlag, Player.Main/SetPosition, Player.Main/UpdateFallInformationIfNeed, Spell.Main/CheckCast, Spell.Main/update, Unit.Main/GetSpeedForMovementInfo, Unit.Main/HandleInterruptsOnMovement, Unit.Main/SetWalk, WorldObject.Object/CorrectData, WorldObject.Object/FillFrom, WorldObject.Object/GetLeewayBonusRadius, WorldObject.Object/Read, WorldObject.Object/Write, WorldSession.MiscHandler/HandleLogoutRequestOpcode, WorldSession.MiscHandler/HandleZoneUpdateOpcode, WorldSession.MovementHandler/HandleMovementOpcodes, WorldSession.MovementHandler/HandleMoveNotActiveMoverOpcode, WorldSession.MovementHandler/HandleMoveRootAck, WorldSession.MovementHandler/HandleMoverRelocation, WorldSession.MovementHandler/HandleMoveSplineDoneOpcode, WorldSession.MovementHandler/HandleMoveWorldportAck, WorldSession.MovementHandler/VerifyMovementInfo | — |
| GetMovementFlags | method | — | MoveSplineInit/Launch | — |
| SetMovementFlags | method | — | MoveSplineInit/Launch | — |
| GetPos | method | — | MovementAnticheat/HandleSplineDone, Player.Main/HandleFall, WorldSession.MovementHandler/HandleMoverRelocation, WorldSession.MovementHandler/VerifyMovementInfo | — |
| SetTransportData | method | — | Player.Main/LoadFromDB, Transport/AddFollowerToTransport | — |
| ClearTransportData | method | — | Player.Main/LoadFromDB, Player.Main/SwitchInstance, Player.Main/TeleportTo, Transport/RemovePassenger, WorldSession.MovementHandler/HandleMoverRelocation | — |
| GetTransportGuid | method | — | MovementAnticheat/HandleSplineDone, WorldSession.MovementHandler/HandleMoverRelocation | — |
| GetTransportPos#2 | method | — | MovementAnticheat/HandleSplineDone, Player.Main/HandleFall, WorldSession.MovementHandler/VerifyMovementInfo | — |
| GetTransportPos | method | — | Player.Main/LoadFromDB, Player.Main/SaveToDB, Player.Main/SendNewWorld, Unit.Main/UpdateSplineMovement, WorldSession.CharacterHandler/HandlePlayerLogin, WorldSession.MovementHandler/HandleMoverRelocation | — |
| GetFallTime | method | — | Player.Main/HandleFall | — |
| ChangeOrientation | method | — | Unit.Main/SetFacingTo, WorldObject.Object/SetOrientation | — |
| ChangePosition | method | — | MovementPacketSender/SendTeleportToController, MovementPacketSender/SendTeleportToObservers, Unit.Main/NearLandTo, Unit.Main/TeleportPositionRelocation, WorldObject.Object/BuildMovementUpdate, WorldObject.Object/Relocate#2 | — |
| UpdateTime | method | — | WorldObject.Object/Relocate#2, WorldSession.MovementHandler/ExecuteTeleportNear, WorldSession.MovementHandler/HandleForceSpeedChangeAckOpcodes, WorldSession.MovementHandler/HandleMoveKnockBackAck, WorldSession.MovementHandler/HandleMovementFlagChangeToggleAck, WorldSession.MovementHandler/HandleMovementOpcodes, WorldSession.MovementHandler/HandleMoveRootAck, WorldSession.MovementHandler/HandleMoveSplineDoneOpcode | — |
| SetAsServerSide | method | — | MovementPacketSender/SendTeleportToController, Transport/TeleportTransport, Unit.Main/NearLandTo, Unit.Main/SendMovementPacket | — |
| WasSentBySession | method | — | MovementAnticheat/CheckFallReset, MovementAnticheat/CheckFallStop, MovementAnticheat/CheckMoveStart, MovementAnticheat/CheckNoFallTime, MovementAnticheat/CheckTimeDesync | — |
| GetJumpInfo | method | — | — | — |
| operator<< | function | — | MovementPacketSender/SendKnockBackToObservers, MovementPacketSender/SendMovementFlagChangeToObservers, MovementPacketSender/SendSpeedChangeToObservers, MovementPacketSender/SendTeleportToController, MovementPacketSender/SendTeleportToObservers, Unit.Main/NearLandTo, Unit.Main/SendMovementPacket, WorldObject.Object/BuildMovementUpdate | — |
| operator>> | function | — | Movement/ReadFromWorldPacket, Movement/ReadFromWorldPacket#2, Movement/ReadFromWorldPacket#3, Movement/ReadFromWorldPacket#4, Movement/ReadFromWorldPacket#5, Movement/ReadFromWorldPacket#6, Movement/ReadFromWorldPacket#9 | — |
