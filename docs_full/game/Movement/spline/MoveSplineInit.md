<!-- provenance: verbose -->
# MoveSplineInit

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# MoveSplineInit

**MoveSplineInit** is a builder-pattern class in the `Movement` namespace that configures and launches spline-based movement for a `Unit`. It aggregates movement parameters—destination, path, velocity, and animation flags—into a `MoveSplineInitArgs` structure. Calling `Launch()` validates the configuration, updates the `Unit`'s internal state and transport associations, and broadcasts the resulting `SMSG_MONSTER_MOVE` packet to clients. It bridges high-level `MovementGenerator` strategies with the low-level `MoveSpline` execution engine and network layer.

## Purpose & Responsibilities

1.  **Configuration Aggregation**: Collects movement parameters into `MoveSplineInitArgs`.
2.  **State Transition**: Updates `Unit`'s `MovementInfo` flags (e.g., `MOVEFLAG_SPLINE_ENABLED`, walk/run modes).
3.  **Transport Management**: Handles passenger addition/removal from `GenericTransport` objects and coordinate adjustments.
4.  **Network Broadcasting**: Constructs and sends movement packets to observers.
5.  **Validation**: Validates arguments via `MoveSpline::Validate` and resets anti-cheat counters for players.

## Member-by-Member Behavior

### Initialization and Construction

*   **`MoveSplineInit` (ctor)**: Initializes the builder with a `Unit` reference and movement type string. It inspects the unit's current `MovementInfo` via `MovementInfo::HasMovementFlag` to preserve existing "run mode" (absence of `MOVEFLAG_WALK_MODE`) and "flying/levitating" status, ensuring the new movement inherits the current locomotion style unless overridden.

### Movement Configuration Methods

*   **`MoveTo` / `MoveTo#2`**: Sets direct point-to-point movement.
    *   The `Vector3` overload checks for `MOVE_PATHFINDING`. If set, it instantiates a `PathFinder`, calculates a path (optionally excluding steep slopes or forcing straight paths), and delegates to `Move(PathFinder*)`.
    *   If pathfinding is disabled, it creates a simple two-point path (current position to destination).
    *   Marks movement as uninterruptible if `MOVE_FORCE_DESTINATION` is set.
*   **`MovebyPath`**: Accepts a pre-calculated `PointsArray`. It copies points into `args.path` and sets `path_Idx_offset`, critical for waypoint systems splitting long paths into segments.
*   **`Move`**: Convenience wrapper for pathfinding results. Extracts path, transport GUID, and flight status from a `PathFinder` and calls `MovebyPath`, `SetTransport`, and `SetFly`.
*   **`SetFirstPointId`**: Sets `path_Idx_offset`, telling the system which logical waypoint index corresponds to the first point in the current spline segment.
*   **`Path`**: Returns a reference to `args.path` for inspection or modification.

### Flag and Mode Setters

These inline methods toggle bits in `args.flags`:

*   **`SetWalk`**: Enables walking mode. Internally sets `runmode = !enable`.
*   **`SetFly`**: Enables flying animation (`flying = true`).
*   **`SetFall`**: Enables falling physics (`falling = true`).
*   **`SetCyclic`**: Marks the spline as cyclic (looping).
*   **`SetStop`**: Marks movement as "done" (`done = true`), stopping the unit.
*   **`SetVelocity`**: Overrides default speed calculation. If unset, `Launch()` calculates speed based on unit stats and mode.
*   **`SetTransport`**: Specifies the transport GUID. Path coordinates are treated as offsets relative to this transport.

### Facing Configuration

*   **`SetFacing` (angle)**: Sets a fixed rotation angle upon movement completion. Wraps angle to `[0, 2π]`.
*   **`SetFacing` (point)**: Sets a target point in space to face.
*   **`SetFacingGUID`**: Sets a target object GUID to face. Calls `MoveSplineFlag::EnableFacingTarget`.

### Launch and Execution

*   **`Launch`**: Core execution method. Steps:
    1.  **Transport Resolution**: Resolves new transport from low GUID via `ObjectMgr::GetFullTransportGuidFromLowGuid` and `Map::GetTransport`.
    2.  **Position Correction**: If current spline isn't finalized, computes real-world position via `MoveSpline::ComputePosition`. Adjusts for old/new transport offsets using `GenericTransport::CalculatePassengerPosition`/`CalculatePassengerOffset`.
    3.  **Default Path**: If no path is set, defaults to stationary move at current position.
    4.  **Flag Synthesis**: Combines `args.flags` with `MovementInfo` flags. Sets `MOVEFLAG_SPLINE_ENABLED`/`MOVEFLAG_FORWARD` for active movement, clears for stops. Syncs walk/run modes.
    5.  **Velocity Calculation**: If unset, calls `SelectSpeedType` to determine speed multiplier, then retrieves value via `Unit::GetSpeed`.
    6.  **Validation**: Calls `MoveSpline::Validate`. Aborts and returns 0 if invalid.
    7.  **Anti-Cheat & State**: Resets player jump counters via `Player::GetCheatData()->ResetJumpCounters()`. Sets `SplineDonePending` for players/pets.
    8.  **Spline Init**: Assigns unique ID from thread-local counter, initializes `MoveSpline` with args, sets movement origin.
    9.  **Packet Construction**: Builds `WorldPacket` (`SMSG_MONSTER_MOVE` or `SMSG_MONSTER_MOVE_TRANSPORT`). Includes unit GUID, transport GUID (if applicable), and position/spline data.
    10. **Transport Passenger Update**: Removes unit from old transport, adds to new one if necessary.
    11. **Broadcast**: Sends packet via `WorldObject::SendMovementMessageToSet`.
    12. **Flag Sync**: Sends auxiliary sync packets via `MovementPacketSender` if root/walk/run flags changed.
    13. **Return**: Returns estimated movement duration.

*   **`SelectSpeedType`**: Static helper determining `UnitMoveType` (SWIM, WALK, RUN, etc.) based on `moveFlags`. Prioritizes swimming, then walking, then running. Used by `Launch` for speed selection.

## Cross-Unit Boundaries

### Collaboration with Movement Generators
*   **Called By**: `ConfusedMovementGenerator`, `CyclicMovementGenerator`, `FearMovementGenerator`, `FleeingMovementGenerator`, `HomeMovementGenerator`, `PointMovementGenerator`, `RandomMovementGenerator`, `TargetedMovementGenerator`, `WaypointMovementGenerator`.
*   **Direction**: Generators call `MoveSplineInit` methods to configure and launch movements.
*   **Why**: Generators decide *where* to go; `MoveSplineInit` handles *how* to tell the engine and clients.

### Collaboration with Unit and Transport
*   **Calls Out**:
    *   `Unit`: Position, speed, movement info, player cheat data.
    *   `GenericTransport`: Passenger offsets, passenger list management.
    *   `ObjectMgr`: Transport GUID resolution.
    *   `Map`: Transport instance retrieval.
*   **Why**: Reconciles local spline coordinates with global world/transport coordinates.

### Collaboration with Network Layer
*   **Calls Out**:
    *   `WorldPacket`: Movement message construction.
    *   `MovementPacketSender`: Auxiliary flag-change packets.
    *   `packet_builder`: Spline data serialization.
*   **Why**: Informs clients of new movement state.

### Collaboration with Pathfinding
*   **Calls Out**: `PathFinder` (via `Move`).
*   **Why**: Delegates geometric calculation when `MoveTo` uses pathfinding options.

## Data Model

This unit does not interact directly with database tables. All movement data is transient, held in memory within the `Unit`'s `MoveSpline` object and `MoveSplineInitArgs` during launch.

## Notable Implementation Details

1.  **Thread-Local Spline Counter**: `Launch()` uses `thread_local uint32 splineCounter` for unique spline IDs, avoiding race conditions in multi-threaded maps.
2.  **Position Recalculation**: If previous spline isn't finalized, `Launch` recalculates real position. Ensures new spline starts from actual current location, not stale database position.
3.  **Transport Offset Logic**: Distinguishes "real position" (world coords) from "passenger offset" (relative to transport). Converts real position to new transport's offset space when switching.
4.  **Walk/Run Mode Inversion**: `SetWalk` sets `args.flags.runmode = !enable`. "Run mode" is default; "walk mode" is exception.
5.  **Anti-Cheat Integration**: Resets jump counters for players to prevent exploitation of movement glitches.
6.  **Client Build Compatibility**: Packet construction uses `#if SUPPORTED_CLIENT_BUILD > CLIENT_BUILD_1_8_4` for opcode/GUID format differences.
7.  **Facing Override**: Only one facing instruction allowed. Subsequent `SetFacing` calls override previous ones.

## Member Reference

**SelectSpeedType**
Static function determining `UnitMoveType` (SWIM, WALK, RUN, etc.) based on movement flags. Used by `Launch` for speed selection.

**Move**
Configures movement from `PathFinder` result. Extracts path, transport, flight status, delegating to `MovebyPath`, `SetTransport`, `SetFly`.

**Launch**
Validates arguments, updates unit state, manages transport passengers, constructs movement packet, broadcasts to clients. Returns movement duration.

**SetFirstPointId**
Sets path point index offset. Used by waypoint systems to track progress across segmented paths.

**Path**
Returns reference to internal path array (`args.path`). Allows inspection/modification before launch.

**SetStop**
Inline method setting `done` flag, indicating movement should stop unit.

**SetFly**
Inline method enabling flying animation by setting `flying` flag.

**SetWalk**
Inline method enabling/disabling walking mode. Internally inverts `runmode` flag.

**SetCyclic**
Inline method enabling cyclic (looping) movement.

**SetFall**
Inline method enabling falling physics.

**SetVelocity**
Inline method setting custom velocity, overriding default speed calculation.

**SetTransport**
Inline method setting transport GUID for relative coordinate calculations.

**MovebyPath**
Inline method assigning pre-calculated control points array to movement args and setting path index offset.

**MoveTo#2**
Inline `MoveTo` overload taking float coordinates, converting to `Vector3`, calling main `MoveTo`.

**MoveTo**
Inline method setting direct movement to destination. Supports optional pathfinding, delegating to `PathFinder` and `Move`.

**SetFacing**
Method (overloaded) setting final facing angle or point for unit after movement completes.

**MoveSplineInit**
Constructor initializing builder with `Unit` reference and movement type string. Preserves existing walk/fly states from unit's current movement info.

**SetFacingGUID**
Method setting target object GUID for unit to face. Calls `MoveSplineFlag::EnableFacingTarget`.

**SetFacing#2**
Method (overloaded) setting fixed facing angle. Wraps angle to `[0, 2π]` and enables facing angle flag.

---

<!-- machine-true, projected from graph.json -->

## Map — MoveSplineInit

*Source:* MoveSplineInit.cpp, MoveSplineInit.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| SelectSpeedType | function | — | — | — |
| Move | method | Object/GetGUIDLow, PathInfo/getPath, PathInfo/getPathType, PathInfo/GetTransport | ConfusedMovementGenerator/Update, CyclicMovementGenerator/_setTargetLocation, FearMovementGenerator/_setTargetLocation, FleeingMovementGenerator/_setTargetLocation, HomeMovementGenerator/_setTargetLocation, PointMovementGenerator/Initialize#2, TargetedMovementGenerator/_setTargetLocation, TargetedMovementGenerator/_setTargetLocation#2, WaypointMovementGenerator/StartMove | — |
| Launch | method | ByteBuffer/operator<<#10, ByteBuffer/operator<<#7, ByteBuffer/operator<<#9, GenericTransport/CalculatePassengerOffset, GenericTransport/CalculatePassengerPosition, Map.Main/GetTransport, MovementAnticheat/ResetJumpCounters, MovementInfo/GetMovementFlags, MovementInfo/SetMovementFlags, MovementPacketSender/SendMovementFlagChangeToAll, MovementPacketSender/SendToggleRunWalkToAll, MoveSpline/ComputePosition, MoveSpline/Duration, MoveSpline/Finalized, MoveSpline/GetId, MoveSpline/GetTransportGuid, MoveSpline/Initialize, MoveSpline/setLastPointSent, MoveSpline/SetMovementOrigin, MoveSpline/Validate, MoveSplineFlag/MoveSplineFlag#3, Object/GetPackGUID, Object/GetTypeId, Object/IsPlayer, Object/ToPlayer, ObjectGuid/IsPlayer, ObjectGuid/operator<<#2, ObjectMgr/GetFullTransportGuidFromLowGuid, packet_builder/WriteMonsterMove, Player.Main/GetCheatData, Transport/AddPassenger, Transport/RemovePassenger, Unit.Main/GetPossessorGuid, Unit.Main/GetSpeed, Unit.Main/SetSplineDonePending, WorldObject.Object/GetMap, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/GetTransport, WorldObject.Object/SendMovementMessageToSet, WorldPacket/SetOpcode, WorldPacket/WorldPacket#2, WorldPacket/WorldPacket#4 | AiBotMovementGenerators/Initialize, ChatHandler.DebugCommands/HandleDebugExp, ChatHandler.DebugCommands/HandleMmapPathCommand, ChatHandler.DebugCommands/HandleVideoTurn, ConfusedMovementGenerator/Update, Creature.Main/FallGround, CyclicMovementGenerator/_setTargetLocation, FearMovementGenerator/_setTargetLocation, FleeingMovementGenerator/_setTargetLocation, HomeMovementGenerator/_setTargetLocation, PointMovementGenerator/Initialize, PointMovementGenerator/Initialize#2, PointMovementGenerator/Initialize#3, RandomMovementGenerator/_setRandomLocation, TargetedMovementGenerator/DoBackMovement, TargetedMovementGenerator/DoSpreadIfNeeded, TargetedMovementGenerator/_setTargetLocation, TargetedMovementGenerator/_setTargetLocation#2, Unit.Main/MonsterMoveWithSpeed, Unit.Main/SetFacingTo, Unit.Main/StopMoving, WaypointMovementGenerator/Reset, WaypointMovementGenerator/StartMove, WaypointMovementGenerator/StartMove#2 | — |
| SetFirstPointId | method | — | CyclicMovementGenerator/_setTargetLocation, RandomMovementGenerator/_setRandomLocation, WaypointMovementGenerator/Reset | — |
| Path | method | — | WaypointMovementGenerator/Reset | — |
| SetStop | method | — | Unit.Main/StopMoving | — |
| SetFly | method | — | AiBotMovementGenerators/Initialize, ChatHandler.DebugCommands/HandleVideoTurn, CyclicMovementGenerator/_setTargetLocation, PointMovementGenerator/Initialize#3, RandomMovementGenerator/_setRandomLocation, Unit.Main/MonsterMoveWithSpeed, WaypointMovementGenerator/Reset, WaypointMovementGenerator/StartMove#2 | — |
| SetWalk | method | — | AiBotMovementGenerators/Initialize, ChatHandler.DebugCommands/HandleDebugExp, ConfusedMovementGenerator/Update, CyclicMovementGenerator/_setTargetLocation, FearMovementGenerator/_setTargetLocation, FleeingMovementGenerator/_setTargetLocation, HomeMovementGenerator/_setTargetLocation, PointMovementGenerator/Initialize, PointMovementGenerator/Initialize#2, PointMovementGenerator/Initialize#3, RandomMovementGenerator/_setRandomLocation, TargetedMovementGenerator/DoBackMovement, TargetedMovementGenerator/DoSpreadIfNeeded, TargetedMovementGenerator/_setTargetLocation, TargetedMovementGenerator/_setTargetLocation#2, Unit.Main/MonsterMoveWithSpeed, WaypointMovementGenerator/StartMove | — |
| SetCyclic | method | — | AiBotMovementGenerators/Initialize, PointMovementGenerator/Initialize#3, Unit.Main/MonsterMoveWithSpeed | — |
| SetFall | method | — | AiBotMovementGenerators/Initialize, Creature.Main/FallGround, PointMovementGenerator/Initialize#3, Unit.Main/MonsterMoveWithSpeed | — |
| SetVelocity | method | — | AiBotMovementGenerators/Initialize, ChatHandler.DebugCommands/HandleDebugExp, ChatHandler.DebugCommands/HandleVideoTurn, FearMovementGenerator/_setTargetLocation, FleeingMovementGenerator/_setTargetLocation, PointMovementGenerator/Initialize, PointMovementGenerator/Initialize#2, PointMovementGenerator/Initialize#3, TargetedMovementGenerator/_setTargetLocation#2, Unit.Main/MonsterMoveWithSpeed, WaypointMovementGenerator/Reset, WaypointMovementGenerator/StartMove | — |
| SetTransport | method | — | ChatHandler.DebugCommands/HandleMmapPathCommand, Unit.Main/SetFacingTo, Unit.Main/StopMoving | — |
| MovebyPath | method | — | AiBotMovementGenerators/Initialize, ChatHandler.DebugCommands/HandleDebugExp, ChatHandler.DebugCommands/HandleVideoTurn, CyclicMovementGenerator/_setTargetLocation, RandomMovementGenerator/_setRandomLocation, WaypointMovementGenerator/StartMove#2 | — |
| MoveTo#2 | method | — | Creature.Main/FallGround, PointMovementGenerator/Initialize, PointMovementGenerator/Initialize#3, RandomMovementGenerator/_setRandomLocation, TargetedMovementGenerator/DoBackMovement, TargetedMovementGenerator/DoSpreadIfNeeded, Unit.Main/MonsterMoveWithSpeed, WaypointMovementGenerator/StartMove#2 | — |
| MoveTo | method | — | — | — |
| SetFacing | method | — | — | — |
| MoveSplineInit | ctor | MovementInfo/HasMovementFlag | AiBotMovementGenerators/Initialize, ChatHandler.DebugCommands/HandleDebugExp, ChatHandler.DebugCommands/HandleMmapPathCommand, ChatHandler.DebugCommands/HandleVideoTurn, Creature.Main/FallGround, CyclicMovementGenerator/_setTargetLocation, HomeMovementGenerator/_setTargetLocation, PointMovementGenerator/Initialize, RandomMovementGenerator/_setRandomLocation, Unit.Main/MonsterMoveWithSpeed, Unit.Main/SetFacingTo, Unit.Main/StopMoving, WaypointMovementGenerator/Reset, WaypointMovementGenerator/StartMove, WaypointMovementGenerator/StartMove#2 | — |
| SetFacingGUID | method | MoveSplineFlag/EnableFacingTarget | PointMovementGenerator/Initialize#2, TargetedMovementGenerator/_setTargetLocation | — |
| SetFacing#2 | method | MoveSplineFlag/EnableFacingAngle | AiBotMovementGenerators/Initialize, ChatHandler.DebugCommands/HandleMmapPathCommand, HomeMovementGenerator/_setTargetLocation, PointMovementGenerator/Initialize#3, Unit.Main/MonsterMoveWithSpeed, Unit.Main/SetFacingTo, WaypointMovementGenerator/StartMove, WaypointMovementGenerator/StartMove#2 | — |
