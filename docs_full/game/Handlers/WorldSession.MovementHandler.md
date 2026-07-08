<!-- provenance: boundary-bleed -->
# WorldSession.MovementHandler

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# WorldSession.MovementHandler

## Purpose & Responsibilities

`WorldSession.MovementHandler` implements the server-side logic for processing movement-related network packets in the `wowvmangos` emulation framework. Defined in `MovementHandler.cpp` and declared in `WorldSession.h`, this partial of the `WorldSession` class serves as the authoritative validator and executor of player and unit movement.

Its core responsibilities include:
1.  **Packet Validation & Anticheat Integration:** Ingesting raw movement opcodes from the client, validating coordinates and timestamps against server state, and interfacing with `MovementAnticheat` to detect and penalize invalid movements.
2.  **Teleportation Orchestration:** Managing the complex state transitions for "far" teleports (world ports) and "near" teleports (short-range warps), including map resolution, instance validation, battleground integration, and pet resurrection.
3.  **Mover Authority Management:** Tracking which entity the client is currently controlling (`m_clientMoverGuid`), allowing for pet possession, vehicle control, and correct handoff of control back to the player character.
4.  **State Synchronization:** Processing acknowledgments for server-initiated movement changes (speed, root, knockback) to ensure the client and server remain synchronized.
5.  **Environmental Safety:** Implementing safeguards against "undermap" glitches and void falls, including automatic recall or death mechanics when players fall below valid terrain.

This unit operates entirely on in-memory objects (`Player`, `Unit`, `Map`) and network packets; it does not perform direct database queries.

## Member-by-Member Behavior

### Teleportation Handlers

**HandleMoveWorldportAckOpcode**
A thin wrapper that invokes `HandleMoveWorldportAck`. It receives a null packet, signaling that the client has finished loading the new map after a long-distance teleport.

**HandleMoveWorldportAck**
The core logic for completing a "far" teleport.
1.  **Validation:** Checks if the player is in a teleport state (`IsBeingTeleportedFar`). If not, it ignores the packet.
2.  **Coordinate Sanity:** Verifies the destination coordinates are valid using `MapManager::IsValidMapCoord`. If invalid (potential cheat), it teleports the player to their homebind and logs an error.
3.  **Map Resolution:**
    *   For Battlegrounds: Attempts to find the specific BG instance map. If the instance doesn't exist, it aborts the teleport and returns the player to their previous location.
    *   For Dungeons/Raids: Validates instance permissions and resets instance validity flags if leaving a dungeon.
    *   For Continents: Creates or finds the appropriate map object.
4.  **Relocation:** Sets the player's map and position. If on a transport, it updates the passenger position; otherwise, it relocates directly.
5.  **Post-Teleport Setup:**
    *   Sends initial packets to the new map.
    *   Adds the player to the map grid. If addition fails (e.g., map full), it rolls back to the previous location.
    *   Handles Battleground state: If entering a BG, it adds the player to the BG object if invited. If leaving a BG, it clears BG IDs.
    *   Handles Taxi/Flight paths: Resets flight generators if arriving at a destination.
    *   Handles Instance Resets: Sends warnings for raid resets or cleans up personal dungeon instances.
    *   Cleans up auras that should be interrupted by world entry (e.g., mounts in non-mount zones).
    *   Resummons temporary pets and processes delayed operations.
    *   **MacOS Specific Fix:** Sends a heartbeat to correct camera orientation issues on MacOS clients when on transports.

**HandleMoveTeleportAckOpcode**
Handles the acknowledgment for a "near" teleport (short-range warp).
1.  Extracts the movement counter from the packet (client-dependent).
2.  Identifies the mover (usually the player).
3.  Validates that the player is in a "near teleport" state and that the GUID matches.
4.  Checks for a pending teleport change using the movement counter. If missing, it logs an error (potential desync or cheat).
5.  Clears the "spline done pending" flag and delegates the finalization to `Player.ExecuteTeleportNear` (defined in `Player.Main`).

**ExecuteTeleportNear**
*(Note: Defined in `Player.Main`, but called exclusively by this unit to complete near teleport logic.)*
1.  Broadcasts the teleport to observers.
2.  Updates the movement info timestamp.
3.  Relocates the player to the destination.
4.  Handles special cases like confused movement (sheep) by resetting the generator's start position.
5.  Resummons pets if necessary, considering continent instantiation settings.
6.  Processes delayed operations.

### Standard Movement & Position Updates

**HandleMovementOpcodes**
The main entry point for continuous movement packets (walking, running, jumping, falling).
1.  **Rejection Check:** Ignores packets if the global reject timer (`m_moveRejectTime`) hasn't expired.
2.  **Mover Identification:** Gets the confirmed mover. If none, or if the mover has a pending spline completion or is currently being moved by the server (spline not finalized), it ignores the packet.
3.  **Teleport Filter:** Ignores movement if the player is currently teleporting (far or near).
4.  **Validation:**
    *   Updates the movement info timestamp.
    *   Calls `VerifyMovementInfo` to check coordinate bounds.
    *   Runs anticheat tests (`HandleFlagTests`, `HandlePositionTests`). If a cheat is detected, it sets a rejection timer and returns.
5.  **Physics Adjustments:**
    *   Sets jump initial speed for jumps and falls.
    *   Triggers fall damage calculation if landing (`MSG_MOVE_FALL_LAND`) and not flying.
    *   Resets "launched" state (knockback) when falling ends.
6.  **Root Handling (1.14+):** Special logic to ensure roots are applied correctly after landing, kicking the player if they violate root constraints.
7.  **Relocation:** Calls `HandleMoverRelocation` to update the server state.
8.  **Broadcasting:** Constructs a response packet and sends the movement update to other players in the vicinity.

**HandleMoverRelocation**
The central function for updating a unit's position on the server.
1.  **Data Correction:** Marks the session as the source and corrects movement data.
2.  **Root Preservation:** Prevents clients from removing the `MOVEFLAG_ROOT` flag if the server believes the unit is rooted.
3.  **Transport Handling:**
    *   If the unit is on a transport, it calculates the absolute world position from the relative transport position.
    *   If the unit was not on a transport but the packet says it is, it adds the unit as a passenger to the transport.
    *   If the unit was on a transport but the packet says it isn't, it removes the unit from the transport.
4.  **Player-Specific Logic:**
    *   Releases loot if the player starts moving.
    *   Updates the player's internal position.
    *   **Undermap Protection:**
        *   Checks if the player is falling far and is significantly below the terrain height. If so, it recalls them to a safe position.
        *   Saves "no-undermap" positions for valid movements.
        *   **Void Fall:** If the Z coordinate is below -500, it deals environmental damage (fall to void). If the player dies, it kills them and respawns them at the graveyard.
5.  **Creature-Specific Logic:** Calls `CreatureRelocation` on the map.

### Acknowledgment Handlers (Server-Initiated Changes)

These handlers process client confirmations for movement changes forced by the server (e.g., speed buffs/debuffs, roots, knockbacks). They follow a similar pattern: validate counter, check anticheat, apply change, broadcast.

**HandleForceSpeedChangeAckOpcodes**
Handles acknowledgments for speed changes (walk, run, swim, turn rate).
1.  Maps the opcode to a `UnitMoveType`.
2.  Validates the movement counter and pending change existence.
3.  Runs anticheat position/flag tests.
4.  Applies the new speed rate immediately (before relocation) to prevent desyncs with aura-based speeds.
5.  Relocates the mover if valid.
6.  Broadcasts the speed change to observers.

**HandleMovementFlagChangeToggleAck**
Handles acknowledgments for toggling flags like Water Walking, Hover, or Feather Fall.
1.  Maps opcode to flag type.
2.  Validates counter and pending change.
3.  Runs anticheat tests.
4.  Relocates if valid.
5.  Applies the flag change (`SetWaterWalkingReal`, etc.).
6.  Broadcasts the flag change.

**HandleMoveRootAck**
Handles acknowledgments for rooting/unrooting.
1.  Determines if the root is being applied or removed based on the opcode.
2.  Validates counter and pending change.
3.  Runs anticheat tests.
4.  **1.14 Client Workaround:** Checks for specific flag combinations to handle root-on-landing scenarios correctly. Kicks the player if they acknowledge a root but don't have the root flag set (and aren't falling/jumping).
5.  Applies the root state (`SetRootedReal`).
6.  Broadcasts the root change.

**HandleMoveKnockBackAck**
Handles acknowledgments for knockback effects.
1.  Validates counter and pending change.
2.  Runs anticheat tests.
3.  Resets fall information.
4.  Relocates the mover.
5.  Broadcasts the knockback vector to observers.

**HandleMoveSplineDoneOpcode**
Handles the completion of a spline movement (e.g., finishing a jump or a forced movement path).
1.  Validates the spline ID matches the current active spline.
2.  Clears the "pending spline done" flag.
3.  Runs anticheat tests.
4.  Relocates the mover.
5.  Broadcasts a heartbeat or stop message depending on whether the unit is still moving.

### Mover Control & Misc

**HandleSetActiveMoverOpcode**
Handles the client taking control of a different unit (e.g., a pet or vehicle).
1.  Validates the new mover GUID against the current server-known mover.
2.  If taking control of a creature:
    *   Ensures root flags are synchronized.
    *   Clears pending spline done flags for older clients.
3.  If swapping back from a pet:
    *   Clears possession flags on the pet.
    *   Dismisses the pet if it is out of range.
4.  Updates `m_clientMoverGuid`.

**HandleMoveNotActiveMoverOpcode**
Handles the client releasing control of a unit (returning to self-control).
1.  Validates the old mover GUID.
2.  Clears `m_clientMoverGuid`.
3.  Validates movement info and runs anticheat.
4.  Relocates the former mover.
5.  Updates channel start positions (fixes for channeled spells like Eye of the Beast).
6.  Broadcasts the final movement state of the former mover.

**HandleMountSpecialAnimOpcode**
Broadcasts a mount special animation packet to observers. Simple relay.

**HandleSummonResponseOpcode**
Processes a player's response to a summon request.
1.  Checks if the player is alive and not in combat.
2.  Delegates to `Player.SummonIfPossible` (defined in `Player.Main`).

**HandleMoveTimeSkippedOpcode**
Handles lag compensation packets from the client.
1.  Adjusts the movement info's start and current time by the reported lag.
2.  Fixes a 1.12 client bug with transports by re-sending create/out-of-range updates if the player just boarded.
3.  For newer clients, broadcasts the time skip to others.

### Utility & Validation

**GetMoverFromGuid**
Resolves a GUID to a `Unit` pointer, ensuring the session is authorized to control that unit. It checks against the player's current mover, the player themselves, or the client-controlled mover GUID.

**RejectMovementPacketsFor**
Sets a timer (`m_moveRejectTime`) during which all movement packets from this session will be ignored. Used by anticheat to punish invalid movements.

**VerifyMovementInfo**
Static validation of movement coordinates.
1.  Checks if the absolute coordinates are within valid map bounds.
2.  If on a transport, checks if the relative transport coordinates are within reasonable limits (±250 X/Y, ±100 Z) and if the resulting absolute coordinates are valid.

## Cross-Unit Boundaries

*   **Player.Main:** Heavily integrated. `WorldSession` acts as the network interface for the `Player` object. It calls `Player` methods to get/set teleport destinations, check teleport states, manage pets, handle battlegrounds, and update positions. `Player.ExecuteTeleportNear` (in `Player.Main`) is a critical dependency for near teleports.
*   **Unit.Main:** Used for generic movement logic applicable to both players and creatures (speed rates, root states, jump speeds, motion masters).
*   **MapManager / Map:** `WorldSession` relies on `MapManager` to create/find maps and validate coordinates. It interacts with `Map` objects to add/remove players and handle creature relocations.
*   **MovementAnticheat:** Called extensively in `HandleMovementOpcodes` and various Ack handlers. `WorldSession` passes raw movement data to `MovementAnticheat` for heuristic analysis. If anticheat detects anomalies, it triggers `RejectMovementPacketsFor`.
*   **MovementPacketSender:** Used to broadcast movement changes (teleports, speed changes, roots, knockbacks) to other players in the vicinity. `WorldSession` prepares the data, `MovementPacketSender` handles the distribution.
*   **Transport / GenericTransport:** Interacts with transport objects to manage passengers (boarding/disembarking) and calculate absolute positions from relative transport coordinates.
*   **BattleGround:** Integrates with the battleground system to add/remove players from BG instances upon teleportation.
*   **MapPersistentStateManager / DungeonResetScheduler:** Used to check raid reset times and send warnings to players entering raids.

## Data Model

This unit does not interact directly with any database tables. All operations are performed on in-memory objects (`Player`, `Unit`, `Map`, `WorldSession`).

## Notable Implementation Details

1.  **Undermap Protection:** `HandleMoverRelocation` contains robust logic to prevent players from getting stuck under the map. It checks for large vertical discrepancies between the player's Z and the terrain height. If detected, it teleports the player to a safe spot. If the player falls below Z=-500, it deals "fall to void" damage and eventually kills/respawns them.
2.  **Client-Specific Workarounds:** The code contains numerous `#if SUPPORTED_CLIENT_BUILD` blocks. Notably:
    *   **1.14 Root Handling:** Special logic in `HandleMoveRootAck` and `HandleMovementOpcodes` to handle differences in how 1.14 clients report roots during falls.
    *   **1.12 Transport Bug:** `HandleMoveTimeSkippedOpcode` re-sends transport updates to fix a known 1.12 client issue.
    *   **MacOS Camera:** `HandleMoveWorldportAck` sends a specific heartbeat to fix camera orientation on MacOS clients when on transports.
3.  **Movement Counter Validation:** For newer clients, the server tracks a movement counter. Ack packets must include the correct counter. Mismatches trigger anticheat penalties (`OnWrongAckData`). This prevents replay attacks or desync exploitation.
4.  **Teleport Rollback:** Both far and near teleports have rollback mechanisms. If the destination map cannot be created or the player cannot be added to the map (e.g., full), the player is returned to their pre-teleport location.
5.  **Mover Authority:** The `m_clientMoverGuid` variable is crucial. It decouples the player's character from the entity they are currently controlling. All movement validation checks if the packet's GUID matches this authority.

## Member Reference

**HandleMoveWorldportAckOpcode**: Wrapper that calls `HandleMoveWorldportAck`. Receives a null packet.

**HandleMoveWorldportAck**: Completes a far teleport. Validates coordinates, resolves the destination map (handling BGs/Dungeons), relocates the player, manages BG/taxi/pet states, and handles post-teleport cleanup (auras, delayed ops). Includes MacOS-specific camera fixes.

**HandleMoveTeleportAckOpcode**: Handles near teleport acknowledgment. Validates movement counter and GUID, then delegates to `Player.ExecuteTeleportNear` (defined in `Player.Main`).

**ExecuteTeleportNear**: *(Defined in `Player.Main`, called here)* Finalizes near teleport by broadcasting, relocating, handling confused movement, resummoning pets, and processing delayed ops.

**HandleMovementOpcodes**: Main handler for continuous movement. Validates packets against anticheat and server state, handles physics (jumps/falls), manages root states, calls `HandleMoverRelocation`, and broadcasts updates.

**HandleForceSpeedChangeAckOpcodes**: Handles speed change acknowledgments. Validates counter, applies speed rate, relocates, and broadcasts.

**HandleMovementFlagChangeToggleAck**: Handles flag toggle acknowledgments (water walk, hover, feather fall). Validates counter, applies flag, relocates, and broadcasts.

**HandleMoveRootAck**: Handles root/unroot acknowledgments. Validates counter, applies root state, handles 1.14 client root-on-landing quirks, and broadcasts.

**HandleMoveKnockBackAck**: Handles knockback acknowledgments. Validates counter, resets fall info, relocates, and broadcasts knockback vector.

**HandleMoveSplineDoneOpcode**: Handles spline completion. Validates spline ID, clears pending flag, relocates, and broadcasts heartbeat/stop.

**HandleSetActiveMoverOpcode**: Handles client taking control of a new unit (pet/vehicle). Validates GUID, syncs root flags, handles pet dismissal if out of range, and updates `m_clientMoverGuid`.

**HandleMoveNotActiveMoverOpcode**: Handles client releasing control of a unit. Validates old GUID, clears `m_clientMoverGuid`, relocates former mover, and broadcasts final state.

**HandleMountSpecialAnimOpcode**: Broadcasts mount special animation to observers.

**HandleSummonResponseOpcode**: Processes summon response. Checks alive/combat state, then calls `Player.SummonIfPossible` (defined in `Player.Main`).

**HandleMoveTimeSkippedOpcode**: Handles lag compensation. Adjusts movement timestamps, fixes 1.12 transport bugs, and broadcasts time skips for newer clients.

**GetMoverFromGuid**: Resolves a GUID to a `Unit` pointer, verifying the session is authorized to control that unit.

**RejectMovementPacketsFor**: Sets a timer to ignore subsequent movement packets, used for anticheat punishment.

**VerifyMovementInfo**: Validates movement coordinates against map bounds and transport limits.

**HandleMoverRelocation**: Central function for updating unit position. Handles transport boarding/disembarking, preserves root flags, implements undermap protection (safe recall/void kill), and updates player/creature positions.

---

<!-- machine-true, projected from graph.json -->

## Map — WorldSession.MovementHandler

*Source:* MovementHandler.cpp, WorldSession.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| HandleMoveWorldportAckOpcode | method | — | — | — |
| HandleMoveWorldportAck | method | Creature.MotionMaster/GetCurrentMovementGeneratorType, DungeonResetScheduler/GetResetTimeFor, game_Battlegrounds_BattleGround/AddPlayer, Map.Main/Add#3, MapEntry/IsBattleGround, MapEntry/IsDungeon, MapEntry/IsMountAllowed, MapEntry/IsRaid, MapManager/CreateMap, MapManager/FindMap, MapManager/GetContinentInstanceId, MapManager/IsValidMapCoord, MapPersistentStateManager/GetScheduler, MotionMaster/MovementExpired, MovementInfo/HasMovementFlag, Player.Main/GetBattleGround, Player.Main/GetBattleGroundId, Player.Main/GetTeleportDest, Player.Main/HandleReturnOnTeleportFail, Player.Main/InBattleGround, Player.Main/IsBeingTeleportedFar, Player.Main/IsInvitedForBattleGroundInstance, Player.Main/Player, Player.Main/ProcessDelayedOperations, Player.Main/RemoveDelayedOperation, Player.Main/ResetPersonalInstanceOnLeaveDungeon, Player.Main/ResummonPetTemporaryUnSummonedIfAny, Player.Main/SendInitialPacketsAfterAddToMap, Player.Main/SendInitialPacketsBeforeAddToMap, Player.Main/SendInstanceResetWarning, Player.Main/SetBattleGroundId, Player.Main/SetBGTeam, Player.Main/SetSemaphoreTeleportFar, Player.Main/TeleportToHomebind, Player.Main/UpdateTerainEnvironmentFlags, PlayerTaxi/ClearTaxiDestinations, PlayerTaxi/empty, Transport/UpdatePassengerPosition, Unit.Main/GetMotionMaster, Unit.Main/RemoveAurasWithInterruptFlags, Unit.Main/RemoveSpellsCausingAura, Unit.Main/SendHeartBeat, WaypointMovementGenerator/Reset, WorldLocation/WorldLocation#2, WorldObject.Object/GetMap, WorldObject.Object/GetPosition, WorldObject.Object/GetTransport, WorldObject.Object/Relocate#2, WorldObject.Object/SetLocationInstanceId, WorldObject.Object/SetMap, WorldSession.Main/GetPlayer | PlayerBotAI/UpdateAI#2, WorldSession.Main/LogoutPlayer | — |
| HandleMoveTeleportAckOpcode | method | Object/GetObjectGuid, Object/ToPlayer, ObjectGuid/operator!=, Player.Main/GetMover, Player.Main/IsBeingTeleportedNear, Player.Main/Player, Unit.Main/FindPendingMovementTeleportChange, Unit.Main/GetMovementCounter, Unit.Main/SetSplineDonePending | PlayerBotAI/UpdateAI#2 | — |
| ExecuteTeleportNear | method | Creature.MotionMaster/GetCurrentMovementGeneratorType, Map.Main/GetId, Map.Main/IsContinent, MapManager/GetContinentInstanceId, MovementInfo/UpdateTime, MovementPacketSender/SendTeleportToObservers, Player.Main/GetSession, Player.Main/GetTeleportDest, Player.Main/GetTemporaryUnsummonedPetNumber, Player.Main/IsBeingTeleportedNear, Player.Main/Player, Player.Main/ProcessDelayedOperations, Player.Main/ResummonPetTemporaryUnSummonedIfAny, Player.Main/SetSemaphoreTeleportNear, shared_Util/getMSTime, Unit.Main/GetMotionMaster, World/getConfig, WorldObject.Object/GetInstanceId, WorldObject.Object/GetMap | Unit.Main/ResolvePendingMovementChange | — |
| HandleMovementOpcodes | method | MovementAnticheat/HandleFlagTests, MovementAnticheat/HandlePositionTests, MovementInfo/HasMovementFlag, MovementInfo/UpdateTime, MoveSpline/Finalized, Object/ToPlayer, ObjectGuid/operator<<#2, ObjectGuid/WriteAsPacked, Opcodes/IsFallEndOpcode, Packet/GetOpcode, Player.Main/GetCheatData, Player.Main/GetConfirmedMover, Player.Main/HandleFall, Player.Main/IsBeingTeleported, Player.Main/IsLaunched, Player.Main/Player, Player.Main/SetLaunched, Player.Main/UpdateFallInformationIfNeed, Unit.Main/ClearUnitState, Unit.Main/HasPendingSplineDone, Unit.Main/HasUnitState, Unit.Main/IsTaxiFlying, Unit.Main/SetJumpInitialSpeed, Unit.Main/SetRootedReal, Unit.Main/ShouldBeRooted, World/GetCurrentMSTime, WorldObject.Object/SendMovementMessageToSet, WorldObject.Object/Write, WorldPacket/WorldPacket#2, WorldPacket/WorldPacket#4, WorldSession.Main/KickPlayer | — | — |
| HandleForceSpeedChangeAckOpcodes | method | Log.Main/Out, MovementAnticheat/HandleFlagTests, MovementAnticheat/HandlePositionTests, MovementAnticheat/OnWrongAckData, MovementInfo/UpdateTime, MovementPacketSender/SendSpeedChangeToObservers, Object/ToPlayer, Packet/GetOpcode, Player.Main/GetCheatData, Player.Main/GetMover, Player.Main/IsBeingTeleported, Player.Main/Player, Player.Main/UpdateFallInformationIfNeed, Unit.Main/FindPendingMovementSpeedChange, Unit.Main/GetMovementCounter, Unit.Main/HasPendingMovementChange#2, Unit.Main/HasPendingSplineDone, Unit.Main/SetSpeedRateReal, World/GetCurrentMSTime, WorldObject.Object/CorrectData | — | — |
| HandleMovementFlagChangeToggleAck | method | Log.Main/Out, MovementAnticheat/HandleFlagTests, MovementAnticheat/HandlePositionTests, MovementAnticheat/OnWrongAckData, MovementInfo/UpdateTime, MovementPacketSender/SendMovementFlagChangeToObservers, Object/ToPlayer, Packet/GetOpcode, Player.Main/GetCheatData, Player.Main/GetMover, Player.Main/IsBeingTeleported, Player.Main/Player, Player.Main/UpdateFallInformationIfNeed, Unit.Main/FindPendingMovementFlagChange, Unit.Main/GetJumpInitialSpeed, Unit.Main/GetMovementCounter, Unit.Main/HasPendingMovementChange#2, Unit.Main/HasPendingSplineDone, Unit.Main/SetFeatherFallReal, Unit.Main/SetHoverReal, Unit.Main/SetJumpInitialSpeed, Unit.Main/SetWaterWalkingReal, World/GetCurrentMSTime, WorldObject.Object/CorrectData | — | — |
| HandleMoveRootAck | method | MovementAnticheat/HandleFlagTests, MovementAnticheat/HandlePositionTests, MovementAnticheat/OnWrongAckData, MovementInfo/HasMovementFlag, MovementInfo/UpdateTime, MovementPacketSender/SendMovementFlagChangeToObservers, Object/ToPlayer, Packet/GetOpcode, Player.Main/GetCheatData, Player.Main/GetMover, Player.Main/IsBeingTeleported, Player.Main/Player, Player.Main/UpdateFallInformationIfNeed, Unit.Main/AddUnitState, Unit.Main/ClearUnitState, Unit.Main/FindPendingMovementRootChange, Unit.Main/GetMovementCounter, Unit.Main/HasPendingMovementChange#2, Unit.Main/HasPendingSplineDone, Unit.Main/SetRootedReal, World/GetCurrentMSTime, WorldObject.Object/CorrectData, WorldSession.Main/KickPlayer | — | — |
| HandleMoveKnockBackAck | method | MovementAnticheat/HandleFlagTests, MovementAnticheat/HandlePositionTests, MovementAnticheat/OnWrongAckData, MovementInfo/UpdateTime, MovementPacketSender/SendKnockBackToObservers, Object/IsPlayer, Object/ToPlayer, Packet/GetOpcode, Player.Main/GetCheatData, Player.Main/IsBeingTeleported, Player.Main/Player, Player.Main/SetFallInformation, Unit.Main/FindPendingMovementKnockbackChange, Unit.Main/GetMovementCounter, Unit.Main/HasPendingMovementChange#2, Unit.Main/HasPendingSplineDone, World/GetCurrentMSTime | — | — |
| HandleMoveSplineDoneOpcode | method | MovementAnticheat/HandleFlagTests, MovementAnticheat/HandleSplineDone, MovementInfo/HasMovementFlag, MovementInfo/UpdateTime, MoveSpline/GetId, Object/GetObjectGuid, Object/ToPlayer, ObjectGuid/operator!=, ObjectGuid/operator<<#2, ObjectGuid/WriteAsPacked, Player.Main/GetCheatData, Player.Main/GetMover, Player.Main/IsBeingTeleported, Unit.Main/HasPendingSplineDone, Unit.Main/SetSplineDonePending, World/GetCurrentMSTime, WorldObject.Object/SendMovementMessageToSet, WorldObject.Object/Write, WorldPacket/WorldPacket#2, WorldPacket/WorldPacket#4 | — | — |
| HandleSetActiveMoverOpcode | method | Map.Main/GetGridActivationDistance, MovementPacketSender/AddMovementFlagChangeToController, Object/GetGuidStr, Object/GetObjectGuid, Object/IsCreature, ObjectGuid/GetString, ObjectGuid/IsEmpty, ObjectGuid/operator!=, ObjectGuid/operator==, Player.Main/GetMover, Player.Main/Player, Player.Main/RemovePet, Unit.Main/ClearUnitState, Unit.Main/GetPet, Unit.Main/GetPetGuid, Unit.Main/IsRooted, WorldObject.Object/GetMap, WorldObject.Object/IsWithinDistInMap, WorldObject.Object/RemoveFlag | — | — |
| HandleMoveNotActiveMoverOpcode | method | Map.Main/GetUnit, MovementAnticheat/HandleFlagTests, MovementAnticheat/HandlePositionTests, MovementInfo/HasMovementFlag, MoveSpline/Finalized, Object/GetGuidStr, Object/GetObjectGuid, Object/ToPlayer, ObjectGuid/GetString, ObjectGuid/ObjectGuid, ObjectGuid/operator!=, ObjectGuid/operator<<#2, ObjectGuid/operator==, ObjectGuid/WriteAsPacked, Packet/GetOpcode, Player.Main/GetCheatData, Player.Main/GetMover, Player.Main/IsBeingTeleported, Player.Main/Player, Player.Main/UpdateChannelStartPosition, Unit.Main/HasPendingSplineDone, World/GetCurrentMSTime, WorldObject.Object/GetMap, WorldObject.Object/SendMovementMessageToSet, WorldObject.Object/Write, WorldPacket/WorldPacket#2, WorldPacket/WorldPacket#4 | — | — |
| HandleMountSpecialAnimOpcode | method | Object/GetObjectGuid, ObjectGuid/operator<<, WorldObject.Object/SendMovementMessageToSet, WorldPacket/WorldPacket#2, WorldPacket/WorldPacket#4, WorldSession.Main/GetPlayer | — | — |
| HandleSummonResponseOpcode | method | Player.Main/SummonIfPossible, Unit.Main/IsAlive, Unit.Main/IsInCombat | — | — |
| HandleMoveTimeSkippedOpcode | method | ByteBuffer/operator<<#10, Object/GetPackGUID, ObjectGuid/operator<<#2, Player.Main/HasJustBoarded, Player.Main/SetJustBoarded, WorldObject.Object/GetTransport, WorldObject.Object/SendCreateUpdateToPlayer, WorldObject.Object/SendMovementMessageToSet, WorldObject.Object/SendOutOfRangeUpdateToPlayer, WorldPacket/WorldPacket#2, WorldPacket/WorldPacket#4 | — | — |
| GetMoverFromGuid | method | Map.Main/GetUnit, Object/GetObjectGuid, ObjectGuid/operator==, Player.Main/GetMover, WorldObject.Object/GetMap | — | — |
| RejectMovementPacketsFor | method | shared_Util/getMSTime | Player.Main/SetCheatFixedZ, Player.Main/SetFly | — |
| VerifyMovementInfo | method | GridDefines/IsValidMapCoord#4, MovementInfo/GetPos, MovementInfo/GetTransportPos#2, MovementInfo/HasMovementFlag | — | — |
| HandleMoverRelocation | method | GenericTransport/CalculatePassengerPosition, Map.Main/CreatureRelocation, Map.Main/GetHeight, Map.Main/GetTransport, MovementInfo/AddMovementFlag, MovementInfo/ClearTransportData, MovementInfo/GetPos, MovementInfo/GetTransportGuid, MovementInfo/GetTransportPos, MovementInfo/HasMovementFlag, Object/GetGUIDLow, Object/ToPlayer, ObjectGuid/IsItem, Player.Main/BuildPlayerRepop, Player.Main/EnvironmentalDamage, Player.Main/GetLootGuid, Player.Main/GetName, Player.Main/GetSession, Player.Main/InBattleGround, Player.Main/IsGameMaster, Player.Main/KillPlayer, Player.Main/Player, Player.Main/RepopAtGraveyard, Player.Main/SaveNoUndermapPosition, Player.Main/SetJustBoarded, Player.Main/SetPosition, Player.Main/UndermapRecall, Transport/AddPassenger, Transport/RemovePassenger, Unit.Main/CanFreeMove, Unit.Main/GetHealth, Unit.Main/HandleInterruptsOnMovement, Unit.Main/IsAlive, WorldObject.Object/CorrectData, WorldObject.Object/GetMap, WorldObject.Object/GetMapId, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/GetTransport, WorldObject.Object/HasUnitMovementFlag, WorldSession.LootHandler/DoLootRelease, WorldSession.Main/GetGUID | — | — |

---

<!-- verify: boundary-bleed | foreign: process, update, WorldSession -->
