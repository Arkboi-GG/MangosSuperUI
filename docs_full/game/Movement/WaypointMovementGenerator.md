# WaypointMovementGenerator

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# WaypointMovementGenerator

**Purpose & Responsibilities**

The `WaypointMovementGenerator` unit implements three distinct movement strategies for non-player characters (`Creature`) and players (`Player`) within the WoWVMaNGOS engine. It serves as the bridge between high-level movement commands (e.g., "patrol this route," "take this taxi," "follow the leader") and the low-level spline interpolation system (`MoveSpline`).

The unit contains three primary classes:
1.  **`WaypointMovementGenerator<Creature>`**: Handles standard creature patrol routes defined by waypoints. It supports looping paths, delays, wandering behaviors, and script triggers at specific nodes.
2.  **`FlightPathMovementGenerator`**: Manages player taxi flights. It handles multi-map travel, cost deduction, PvP state transitions upon arrival, and map-boundary teleportation logic.
3.  **`PatrolMovementGenerator`**: Implements formation-based following for creature groups. Followers calculate their position relative to a leader’s current movement spline, ensuring they stay in formation while navigating terrain.

All three classes inherit from `MovementGeneratorMedium`, integrating them into the core motion master stack. They do not directly manipulate database tables; instead, they consume path data provided by `WaypointManager` and `CreatureGroups`.

---

## Member-by-Member Behavior

### WaypointMovementGenerator<Creature>

This class manages the lifecycle of a creature’s waypoint patrol. It loads a path, tracks the current node, handles arrival events (scripts, delays, wandering), and updates movement splines each tick.

#### Initialization and Path Loading

*   **`LoadPath`**: Loads the waypoint path for a specific creature GUID and entry ID. It queries `WaypointManager` for either a default path or a path from a specific origin. If no path is found, it logs an error via `Log.Main`. It initializes `i_currentNode` to the first node and resets `m_lastReachedWaypoint`.
*   **`InitializeWaypointPath`**: Prepares the generator for a specific patrol run. It sets wandering/repeating flags, calls `LoadPath`, and optionally starts at a specific `startPoint`. It resets the `ShortTimeTracker` (`i_nextMoveTime`) with an initial delay and triggers `StartMove`.
*   **`Initialize`**: Adds `UNIT_STATE_ROAMING` and `UNIT_STATE_ROAMING_MOVE` states to the creature via `Unit.Main/AddUnitState`. This marks the creature as actively patrolling.
*   **`GetMovementGeneratorType`**: Returns `WAYPOINT_MOTION_TYPE`, identifying this generator to the motion master.

#### Movement Control and Updates

*   **`StartMove`**: Calculates the next movement segment. If the path is exhausted and not repeating, it sets the home position and initializes the motion master to stop. Otherwise, it advances `i_currentNode`. It constructs a `MoveSplineInit`:
    *   If the node has a `path_id` (sub-path), it fetches the sub-path from `WaypointManager` and uses `MoveSplineInit/MovebyPath`.
    *   Otherwise, it moves to the node coordinates using `MoveSplineInit/MoveTo#2`, applying pathfinding or straight-line logic based on `m_PathOrigin`.
    *   It sets facing orientation if specified and launches the spline.
*   **`Update`**: The main tick handler. It checks if the creature is distracted or unable to move (`UNIT_STATE_CAN_NOT_MOVE`). It prevents movement during spell casts that disable movement (`SpellCaster/IsNoMovementSpellCasted`). If stopped, it waits until `CanMove` returns true (delay expired) then calls `StartMove`. If moving, it checks if the spline is `Finalized`; if so, it calls `OnArrived`.
*   **`OnArrived`**: Triggered when a creature reaches a waypoint node.
    *   Updates `m_lastReachedWaypoint`.
    *   Clears `UNIT_STATE_ROAMING_MOVE`.
    *   Executes scripts associated with the node via `Map.Main/ScriptsStart`.
    *   Informs the creature's AI via `CreatureAI/MovementInform`.
    *   If the creature is the leader of a `CreatureGroup`, it saves the last reached waypoint via `CreatureGroups/SetLastReachedWaypoint`.
    *   If the node has a delay, it enters a "wandering" state (calling `Creature.MotionMaster/MoveRandom`) or stops for the delay duration.
*   **`Reset`**: Restores roaming states. If in a wandering state, it calculates speed to reach the last node and forces movement via `Unit.Main/MonsterMoveWithSpeed`. Otherwise, it calls `StartMoveNow`.
*   **`Interrupt`** and **`Finalize`**: Clear roaming states and adjust walk/run status via `Unit.Main/SetWalk` and `Unit.Main/ClearUnitState`.

#### Utility and State Management

*   **`GetResetPosition`**: Returns the coordinates of the last reached waypoint, used for respawning or resetting position.
*   **`GetPathInformation`**: Outputs debug info about the last reached waypoint and path origin to an `std::ostringstream`. Called by `ChatHandler.CreatureCommands` for debugging.
*   **`SetNextWaypoint`**: Allows external commands (via `Creature.MotionMaster/SetNextWaypoint`) to jump to a specific waypoint ID. It resets the move timer to allow immediate movement.
*   **`getLastReachedWaypoint`**: Returns the index of the last successfully reached waypoint. Called by `Creature.MotionMaster/getLastReachedWaypoint`.
*   **`AddPauseTime`**: Extends the pause timer if the requested time is longer than the current expiry. Called by `Creature.MotionMaster/PauseOutOfCombatMovement`.
*   **`Stopped`**, **`CanMove`**, **`StartMoveNow`**: Internal helpers managing the `ShortTimeTracker` for delays.

### FlightPathMovementGenerator

This class manages player taxi flights, handling the complexities of multi-map travel, costs, and state transitions.

#### Initialization and Lifecycle

*   **`Initialize`**: Calls `Reset` to start the flight.
*   **`Reset`**: Sets up the flight spline. It iterates through the path nodes up to the end of the current map (`GetPathAtMapEnd`), adding them to the `MoveSplineInit`. It sets the flying flag, velocity, and launches the spline. It also sets `UNIT_STATE_TAXI_FLIGHT` and removes client control flags via `Unit.Main/SetFlag`.
*   **`Finalize`**: Handles post-flight cleanup.
    *   Resets fall information to prevent damage on landing.
    *   Clears taxi flight states and flags.
    *   Unmounts the player.
    *   Calls `Player.Main/TaxiStepFinished`.
    *   If the taxi path is empty (journey complete), it re-enables PvP combat (`HostileRefManager/setOnlineOfflineState#2`), casts the PvP aura if in an enforced area, and clears taxi destinations.
*   **`Interrupt`**: Immediately clears taxi flight states and flags.

#### Movement and Map Boundaries

*   **`Update`**: Tracks progress along the spline. As the player passes nodes, it increments `i_currentNode`. If the path changes (new segment), it deducts money via `Player.Main/ModifyMoney` and advances the taxi destination via `PlayerTaxi/NextTaxiDestination`.
*   **`GetPathAtMapEnd`**: Scans the path to find the index of the last node on the current map. This allows the generator to launch splines only for the current map segment, triggering a teleport when the map changes.
*   **`SetCurrentNodeAfterTeleport`**: Called after a map change. It scans the path to find the first node on the new map and sets `i_currentNode` to that index. Called by `Player.Main/TaxiStepFinished`.
*   **`GetResetPosition`**: Returns the coordinates of the current node for reset purposes.

#### Utilities

*   **`DoEventIfAny`**: Currently commented out/disabled in the source. Intended to trigger departure/arrival scripts for taxi nodes.
*   **`GetPathInformation`** (via `GetPathInformation#2` in MAP): Note: The MAP lists `GetPathInformation#2` calling `WaypointManager/GetOriginString`, but the source code for `FlightPathMovementGenerator` does not implement a `GetPathInformation` method taking an `ostringstream`. The MAP likely refers to the `WaypointMovementGenerator`'s method or a missing implementation. However, `FlightPathMovementGenerator` has `GetPath()` which returns the raw path.

### PatrolMovementGenerator

This class makes follower creatures move in formation behind a leader.

#### Initialization

*   **`InitPatrol`**: Validates that the creature is part of a formation group and is not the leader. It stores the leader's GUID and the follower's group member data. Called by the constructor and `Creature.MotionMaster/ReInitializePatrolMovement`.
*   **`Initialize`**: Adds roaming states and calls `StartMove`. Checks if the creature is alive.

#### Movement Logic

*   **`StartMove`**: The core logic for formation movement.
    *   Retrieves the leader creature. If the leader is not moving (spline finalized), it returns.
    *   Checks if the leader is using a valid movement type (Random, Waypoint, Home, Point).
    *   If the follower is too far from the leader (`DEFAULT_VISIBILITY_DISTANCE`), it teleports the follower near the leader.
    *   Calculates the leader's direction and remaining time to the next waypoint.
    *   Computes the follower's target position relative to the leader's orientation and final position using `CreatureGroups/ComputeRelativePosition`.
    *   Uses `WorldObject.PathFinder/calculate#2` to find a valid path to that target.
    *   Calculates required speed to arrive at the same time as the leader, capped at 130% of run speed.
    *   Launches the spline with `MoveSplineInit`.
*   **`Update`**: Checks for distraction or spell interruptions. If the spline is finalized, it calls `StartMove` to calculate the next segment.
*   **`GetResetPosition`**: Calculates the follower's position relative to the leader's current position and orientation, adjusting for ground height and transport.

#### Lifecycle

*   **`Reset`**: Calls `Initialize`.
*   **`Interrupt`** and **`Finalize`**: Clear roaming states and adjust walk/run status.

---

## Cross-Unit Boundaries

### WaypointMovementGenerator<Creature>

*   **Calls `WaypointManager`**:
    *   `GetDefaultPath`, `GetPathFromOrigin`: To retrieve the sequence of waypoints for a creature entry/GUID.
*   **Calls `Creature.MotionMaster`**:
    *   `Initialize`, `InitializeNewDefault`, `MoveWaypoint`, `MoveWaypointAsDefault`: These are the entry points that instantiate this generator.
    *   `MoveRandom`: Used when a waypoint node specifies a wandering behavior.
    *   `getLastReachedWaypoint`, `SetNextWaypoint`, `PauseOutOfCombatMovement`: Interfaces for external control and state querying.
*   **Calls `CreatureAI`**:
    *   `MovementInform`: Notifies the creature's AI script that it has arrived at a specific waypoint ID.
*   **Calls `CreatureGroups`**:
    *   `GetLeaderGuid`, `SetLastReachedWaypoint`: Syncs patrol progress with the group leader if the creature is leading.
*   **Calls `Unit.Main`**:
    *   `AddUnitState`, `ClearUnitState`, `SetWalk`, `MonsterMoveWithSpeed`: Manages creature state flags and forced movement.
*   **Calls `Map.Main`**:
    *   `ScriptsStart`: Executes scripts attached to waypoint nodes.
*   **Calls `MoveSplineInit`**:
    *   `Launch`, `MovebyPath`, `MoveTo#2`, `SetFacing#2`, `SetFly`: Constructs and launches the actual movement splines.

### FlightPathMovementGenerator

*   **Calls `Player.Main`**:
    *   `GetTaxi`, `SetFallInformation`, `Unmount`, `UpdatePvP`, `TaxiStepFinished`, `ModifyMoney`: Manages player state, currency, and PvP status.
*   **Calls `PlayerTaxi`**:
    *   `ClearTaxiDestinations`, `empty`, `GetCurrentTaxiCost`, `NextTaxiDestination`: Manages the taxi path queue and costs.
*   **Calls `HostileRefManager`**:
    *   `setOnlineOfflineState#2`: Re-enables combat threat tracking after flight ends.
*   **Calls `Unit.Main`**:
    *   `ClearUnitState`, `GetHostileRefManager`, `IsPvP`, `StopMoving`, `SetFlag`, `RemoveFlag`: Manages unit flags and states.
*   **Calls `MoveSplineInit`**:
    *   `Launch`, `MoveSplineInit`, `Path`, `SetFirstPointId`, `SetFly`, `SetVelocity`: Constructs the flight spline.
*   **Called by `WorldSession.MovementHandler`**:
    *   `HandleMoveWorldportAck`: Triggers `Reset` when a player acknowledges a world port during flight.
*   **Called by `ChatHandler.CharacterCommands`**:
    *   `HandleModifyFlyCommand`: Triggers `Reset` to modify flight speed.

### PatrolMovementGenerator

*   **Calls `CreatureGroups`**:
    *   `GetLeaderGuid`, `GetMembers`, `IsFormation`, `ComputeRelativePosition`: Retrieves group data and calculates relative positions.
*   **Calls `Map.Main`**:
    *   `GetCreature`, `GetWalkHitPosition`: Finds the leader creature and adjusts positions for terrain/walkability.
*   **Calls `WorldObject.Object`**:
    *   `GetMap`, `GetOrientation`, `GetPositionX/Y/Z`, `GetTransport`, `UpdateGroundPositionZ`: Retrieves spatial data for position calculation.
*   **Calls `MoveSplineInit`**:
    *   `Launch`, `Move`, `MoveSplineInit`, `SetFacing#2`, `SetVelocity`, `SetWalk`: Constructs the follower's movement spline.
*   **Calls `WorldObject.PathFinder`**:
    *   `calculate#2`, `Length`, `PathInfo`: Finds a valid path to the calculated target position.
*   **Called by `Creature.MotionMaster`**:
    *   `ReInitializePatrolMovement`: Triggers `InitPatrol` to set up the follower relationship.

---

## Data Model

This unit does not directly query or modify database tables. It relies on `WaypointManager` to provide path data (likely sourced from `waypoint_scripts` or similar tables) and `CreatureGroups` for formation data. No SQL statements are present in this source file.

---

## Notable Implementation Details

1.  **Multi-Map Taxi Flights**: `FlightPathMovementGenerator` handles map changes by splitting the path. `GetPathAtMapEnd` identifies the last node on the current map. The spline is launched only for that segment. When the player arrives, `SetCurrentNodeAfterTeleport` is called to skip ahead to the first node on the new map. This avoids launching a spline across map boundaries, which is invalid.
2.  **Wandering Behavior**: In `WaypointMovementGenerator::OnArrived`, if a node has `wander_distance`, the creature enters a random movement mode (`MoveRandom`) for the duration of the delay. The `m_isWandering` flag ensures that subsequent arrivals during this period do not trigger further scripts or advance the path prematurely.
3.  **Formation Speed Adjustment**: `PatrolMovementGenerator::StartMove` calculates the follower's speed dynamically based on the distance to the target and the leader's remaining time. This ensures followers arrive at the formation point simultaneously with the leader, preventing bunching or lagging. The speed is capped at 130% of run speed to prevent unrealistic sprinting.
4.  **Spell Interruption**: Both `WaypointMovementGenerator` and `PatrolMovementGenerator` check `IsNoMovementSpellCasted` in their `Update` methods. If a spell that disables movement is active, they stop the creature and clear the `ROAMING_MOVE` state, but do not finalize the movement generator. This allows the patrol to resume once the spell ends.
5.  **Path Sub-paths**: `WaypointMovementGenerator::StartMove` checks for `path_id` in the node. If present, it fetches a sub-path from `WaypointManager` and uses `MovebyPath`. This allows complex curved paths to be defined as separate entities and referenced by main waypoints.
6.  **Leader Change Handling**: If a creature is the leader of a group, `OnArrived` saves the last reached waypoint to the group. This allows the group to resume patrol from the correct point if the leader dies and is replaced.
7.  **Debug Logging**: Extensive use of `DETAIL_FILTER_LOG` and `sLog.Out` for debugging path loading, script execution, and errors. This aids in troubleshooting patrol issues.

---

## Member Reference

*   **LoadPath**: Loads waypoint path from `WaypointManager` for a given GUID/entry. Initializes current node.
*   **PathMovementBase<T, P>**: Base class constructor. Initializes `i_currentNode` to 0.
*   **~PathMovementBase<T, P>**: Base class destructor.
*   **MovementInProgress**: Returns true if there are more nodes in the path than the current node index.
*   **LoadPath#2**: Declaration of `LoadPath` in `PatrolMovementGenerator` (not implemented in this unit, likely empty or inherited).
*   **GetCurrentNode**: Returns the index of the current waypoint node.
*   **Initialize#3**: Adds roaming states to the creature.
*   **InitializeWaypointPath**: Prepares the waypoint path, sets start point, and begins movement.
*   **WaypointMovementGenerator**: Constructor for `WaypointMovementGenerator<Creature>`. Initializes timers and flags.
*   **~WaypointMovementGenerator**: Destructor. Sets `i_path` to nullptr.
*   **GetMovementGeneratorType**: Returns `WAYPOINT_MOTION_TYPE`.
*   **getLastReachedWaypoint**: Returns the index of the last reached waypoint.
*   **GetPathInformation**: Outputs debug info about the path to an `ostringstream`.
*   **AddPauseTime**: Extends the pause timer if necessary.
*   **Finalize#3**: Clears roaming states and adjusts walk/run status.
*   **Stop**: Resets the move timer to stop movement for a specified duration.
*   **Stopped**: Returns true if the move timer has not yet passed.
*   **CanMove**: Updates the move timer and returns true if it has passed.
*   **Interrupt#3**: Clears roaming states and adjusts walk/run status.
*   **Reset#3**: Restores roaming states. If wandering, forces movement to last node; otherwise, starts move immediately.
*   **StartMoveNow**: Resets move timer to 0 and calls `StartMove`.
*   **OnArrived**: Handles arrival at a waypoint: scripts, AI notification, group sync, delays, wandering.
*   **StartMove#2**: Calculates next movement segment, constructs spline, and launches it.
*   **Update#3**: Main tick handler. Checks for interruptions, delays, and spline completion.
*   **GetResetPosition#3**: Returns coordinates of the last reached waypoint.
*   **GetPathInformation#2**: (Note: Likely refers to `WaypointMovementGenerator::GetPathInformation` or a missing `FlightPath` method. Source shows `WaypointMovementGenerator` has this.) Outputs path origin string.
*   **SetNextWaypoint**: Jumps to a specific waypoint ID, resetting the move timer.
*   **GetPathAtMapEnd**: Finds the last node index on the current map for taxi flights.
*   **Initialize**: (For `FlightPathMovementGenerator`) Calls `Reset`.
*   **Finalize**: (For `FlightPathMovementGenerator`) Cleans up flight state, mounts, PvP, and costs.
*   **Interrupt**: (For `FlightPathMovementGenerator`) Clears flight states and flags.
*   **Reset**: (For `FlightPathMovementGenerator`) Sets up flight spline for the current map segment.
*   **Update**: (For `FlightPathMovementGenerator`) Tracks progress, deducts costs, and advances taxi destinations.
*   **SetCurrentNodeAfterTeleport**: Skips to the first node on the new map after a teleport.
*   **DoEventIfAny**: (Currently disabled) Intended to trigger taxi node scripts.
*   **GetResetPosition**: (For `FlightPathMovementGenerator`) Returns coordinates of the current taxi node.
*   **InitPatrol**: Validates and initializes the follower's relationship with the leader.
*   **Initialize#2**: (For `PatrolMovementGenerator`) Adds roaming states and starts movement.
*   **Reset#2**: (For `PatrolMovementGenerator`) Calls `Initialize`.
*   **Interrupt#2**: (For `PatrolMovementGenerator`) Clears roaming states.
*   **Finalize#2**: (For `PatrolMovementGenerator`) Clears roaming states.
*   **Update#2**: (For `PatrolMovementGenerator`) Checks for interruptions and spline completion.
*   **GetResetPosition#2**: (For `PatrolMovementGenerator`) Calculates follower's position relative to leader.
*   **StartMove**: (For `PatrolMovementGenerator`) Calculates follower's target position and speed, then launches spline.

---

<!-- machine-true, projected from graph.json -->

## Map — WaypointMovementGenerator

*Source:* WaypointMovementGenerator.cpp, WaypointMovementGenerator.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| LoadPath | method | Log.Main/HasLogFilter, Log.Main/HasLogLevelOrHigher, Log.Main/Out, WaypointManager/GetDefaultPath, WaypointManager/GetPathFromOrigin | — | — |
| PathMovementBase<T, P> | ctor | — | — | — |
| ~PathMovementBase<T, P> | dtor | — | — | — |
| MovementInProgress | function | — | — | — |
| LoadPath#2 | decl | — | — | — |
| GetCurrentNode | function | — | — | — |
| Initialize#3 | method | Unit.Main/AddUnitState | — | — |
| InitializeWaypointPath | method | Log.Main/Out, Object/GetEntry, Object/GetGUIDLow, Object/GetGuidStr, ShortTimeTracker/Reset | Creature.MotionMaster/Initialize, Creature.MotionMaster/InitializeNewDefault, Creature.MotionMaster/MoveWaypoint, Creature.MotionMaster/MoveWaypointAsDefault | — |
| WaypointMovementGenerator | ctor | — | Creature.MotionMaster/MoveWaypoint, Creature.MotionMaster/MoveWaypointAsDefault | — |
| ~WaypointMovementGenerator | dtor | — | — | — |
| GetMovementGeneratorType | method | — | — | — |
| getLastReachedWaypoint | method | — | Creature.MotionMaster/getLastReachedWaypoint | — |
| GetPathInformation | method | — | ChatHandler.CreatureCommands/HandleWpAddCommand, ChatHandler.CreatureCommands/HandleWpExportCommand, ChatHandler.CreatureCommands/HandleWpModifyCommand, ChatHandler.CreatureCommands/HandleWpShowCommand | — |
| AddPauseTime | method | — | Creature.MotionMaster/PauseOutOfCombatMovement | — |
| Finalize#3 | method | Unit.Main/ClearUnitState, Unit.Main/HasUnitState, Unit.Main/SetWalk | — | — |
| Stop | method | — | — | — |
| Stopped | method | — | — | — |
| CanMove | method | — | — | — |
| Interrupt#3 | method | Unit.Main/ClearUnitState, Unit.Main/HasUnitState, Unit.Main/SetWalk | — | — |
| Reset#3 | method | Unit.Main/AddUnitState, Unit.Main/MonsterMoveWithSpeed, WorldObject.Object/GetDistance#4 | — | — |
| StartMoveNow | method | — | — | — |
| OnArrived | method | Creature.Main/AI, Creature.Main/GetCreatureGroup, Creature.MotionMaster/MoveRandom, CreatureAI/MovementInform, CreatureGroups/GetLeaderGuid, CreatureGroups/SetLastReachedWaypoint, Errors/PrintStacktraceAndThrow, Log.Main/HasLogFilter, Log.Main/HasLogLevelOrHigher, Log.Main/Out, Map.Main/ScriptsStart, Object/GetGuidStr, Object/GetObjectGuid, ObjectGuid/operator==, Unit.Main/ClearUnitState, Unit.Main/GetMotionMaster, WorldObject.Object/GetMap | — | — |
| StartMove#2 | method | Creature.Main/CanFly, Creature.Main/SetHomePosition, Creature.MotionMaster/Initialize, Errors/PrintStacktraceAndThrow, MoveSplineInit/Launch, MoveSplineInit/MovebyPath, MoveSplineInit/MoveSplineInit, MoveSplineInit/MoveTo#2, MoveSplineInit/SetFacing#2, MoveSplineInit/SetFly, Unit.Main/AddUnitState, Unit.Main/GetMotionMaster, Unit.Main/HasUnitState, Unit.Main/SetWalk, WaypointManager/GetPathFromOrigin, WorldObject.Object/IsLevitating | — | — |
| Update#3 | method | MoveSpline/Finalized, ShortTimeTracker/Reset, SpellCaster/IsNoMovementSpellCasted, Unit.Main/ClearUnitState, Unit.Main/HasUnitState, Unit.Main/IsStopped, Unit.Main/StopMoving | — | — |
| GetResetPosition#3 | method | — | — | — |
| GetPathInformation#2 | method | WaypointManager/GetOriginString | Creature.MotionMaster/GetWaypointPathInformation | — |
| SetNextWaypoint | method | ShortTimeTracker/Reset | Creature.MotionMaster/SetNextWaypoint | — |
| GetPathAtMapEnd | method | — | — | — |
| Initialize | method | — | — | — |
| Finalize | method | HostileRefManager/setOnlineOfflineState#2, Player.Main/GetTaxi, Player.Main/SetFallInformation, Player.Main/TaxiStepFinished, Player.Main/Unmount, Player.Main/UpdatePvP, PlayerTaxi/ClearTaxiDestinations, PlayerTaxi/empty, SpellCaster/CastSpell#2, Unit.Main/ClearUnitState, Unit.Main/GetHostileRefManager, Unit.Main/IsPvP, Unit.Main/StopMoving, WorldObject.Object/RemoveFlag, WorldObject.Object/RemoveUnitMovementFlag | — | — |
| Interrupt | method | Unit.Main/ClearUnitState, WorldObject.Object/RemoveUnitMovementFlag | Player.Main/TaxiStepFinished | — |
| Reset | method | HostileRefManager/setOnlineOfflineState#2, MoveSplineInit/Launch, MoveSplineInit/MoveSplineInit, MoveSplineInit/Path, MoveSplineInit/SetFirstPointId, MoveSplineInit/SetFly, MoveSplineInit/SetVelocity, Unit.Main/AddUnitState, Unit.Main/GetHostileRefManager, WorldObject.Object/SetFlag | ChatHandler.CharacterCommands/HandleModifyFlyCommand, WorldSession.MovementHandler/HandleMoveWorldportAck | — |
| Update | method | MoveSpline/currentPathIdx, Player.Main/GetTaxi, Player.Main/ModifyMoney, PlayerTaxi/GetCurrentTaxiCost, PlayerTaxi/NextTaxiDestination | — | — |
| SetCurrentNodeAfterTeleport | method | — | Player.Main/TaxiStepFinished | — |
| DoEventIfAny | method | — | — | — |
| GetResetPosition | method | — | — | — |
| InitPatrol | method | Creature.Main/GetCreatureGroup, CreatureGroups/GetLeaderGuid, CreatureGroups/GetMembers, CreatureGroups/IsFormation, Log.Main/Out, Object/GetObjectGuid, ObjectGuid/GetString, ObjectGuid/operator== | Creature.MotionMaster/ReInitializePatrolMovement | — |
| Initialize#2 | method | Unit.Main/AddUnitState, Unit.Main/IsAlive | — | — |
| Reset#2 | method | — | — | — |
| Interrupt#2 | method | Unit.Main/ClearUnitState, Unit.Main/HasUnitState, Unit.Main/SetWalk | — | — |
| Finalize#2 | method | Unit.Main/ClearUnitState, Unit.Main/HasUnitState, Unit.Main/SetWalk | — | — |
| Update#2 | method | MoveSpline/Finalized, SpellCaster/IsNoMovementSpellCasted, Unit.Main/ClearUnitState, Unit.Main/HasUnitState, Unit.Main/IsStopped, Unit.Main/StopMoving | — | — |
| GetResetPosition#2 | method | CreatureGroups/ComputeRelativePosition, Map.Main/GetCreature, Map.Main/GetWalkHitPosition, WorldObject.Object/GetMap, WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/GetTransport, WorldObject.Object/UpdateGroundPositionZ | — | — |
| StartMove | method | Creature.MotionMaster/GetCurrentMovementGeneratorType, CreatureGroups/ComputeRelativePosition, Map.Main/GetCreature, Map.Main/GetWalkHitPosition, MoveSpline/CountSplinePoints, MoveSpline/Finalized, MoveSpline/GetPoint, MoveSpline/timeElapsed, MoveSplineInit/Launch, MoveSplineInit/Move, MoveSplineInit/MoveSplineInit, MoveSplineInit/SetFacing#2, MoveSplineInit/SetVelocity, MoveSplineInit/SetWalk, Unit.Main/AddUnitState, Unit.Main/GetMotionMaster, Unit.Main/GetSpeed, WorldObject.Object/GetDistance3dToCenter#3, WorldObject.Object/GetMap, WorldObject.Object/GetPosition#3, WorldObject.Object/GetTransport, WorldObject.Object/IsLevitating, WorldObject.Object/IsWalking, WorldObject.Object/UpdateGroundPositionZ, WorldObject.PathFinder/calculate#2, WorldObject.PathFinder/Length, WorldObject.PathFinder/PathInfo | — | — |
