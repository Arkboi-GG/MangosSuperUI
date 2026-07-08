# Transport

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Transport

## Purpose & Responsibilities

The `Transport` unit implements the server-side logic for moving vehicles—specifically ships, zeppelins, and elevators—that carry players and creatures across the game world. It inherits from `GameObject`, treating the vehicle itself as a movable object with a visual representation, but adds complex spatial management for "passengers" (Units riding the vehicle).

Its primary responsibilities are:
1.  **Pathfinding & Animation:** Calculating the vehicle's position and orientation over time based on predefined keyframes (for ships/zeppelins) or animation nodes (for elevators).
2.  **Passenger Management:** Tracking all Units (players and creatures) currently on board. When the vehicle moves, rotates, or changes maps, the unit calculates the new absolute coordinates for every passenger relative to the vehicle's local coordinate system.
3.  **Map Transitions:** Handling the complex logic required when a vehicle moves from one map to another (e.g., a ship leaving a continent to enter the open sea), including teleporting passengers and updating their instance IDs.
4.  **Network Updates:** Sending specific update packets to players on the map to ensure clients correctly render the vehicle and its passengers, especially during map transitions or when entering/exiting range.

The unit is split into two main subclasses:
*   `ShipTransport`: Handles long-distance travel between maps using spline-based keyframes.
*   `ElevatorTransport`: Handles short-range, repetitive animations within a single map using linear interpolation between animation nodes.

Both inherit from `GenericTransport`, which contains the shared logic for passenger tracking, coordinate transformation, and cleanup.

## Member-by-Member Behavior

### Initialization and Lifecycle

**ShipTransport** (Constructor)
Initializes a ship or zeppelin transport. It sets the internal update flags to indicate this is a transport object, ensuring the server sends appropriate movement updates. It retrieves the total path duration (`pathTime`) from the provided `TransportTemplate` and sets it as the period for the loop.

**Create#2** (Method)
Creates the `ShipTransport` instance in the world. It validates the starting coordinates from the first keyframe. If valid, it initializes the object's GUID, loads the `GameObjectInfo` template, and configures visual properties like scale, faction, display ID, and state. It registers the transport with the `ObjectAccessor` so it can be looked up by GUID. If the template is missing or coordinates are invalid, it logs an error and fails silently.

**Create** (Method)
Creates an `ElevatorTransport`. It delegates to the parent `GenericTransport::Create` (not shown in this unit's map, but implied by inheritance) to handle basic object setup. Then, it fetches specific animation info (`TransportAnimation`) from the `TransportMgr` and initializes the progress timer.

**CleanupsBeforeDelete** (Method)
Ensures all passengers are removed from the transport before the object is destroyed. It iterates through the passenger set, calling `RemovePassenger` for each, then calls the parent `GameObject::CleanupsBeforeDelete`. This prevents dangling references to deleted transports in player/creature movement data.

**~TransportTemplate** (Destructor)
Cleans up memory allocated for `TransportSpline` objects referenced by the keyframes. It uses a `std::set` to ensure unique splines are deleted only once, preventing double-free errors if multiple keyframes share the same spline data.

### Movement Logic

**Update#2** (Method)
The core update loop for `ShipTransport`. It calculates the current time elapsed since creation and determines the transport's progress along its path.
1.  **State Check:** If the transport is stopped at a keyframe (waiting period), it does nothing until the wait expires.
2.  **Waypoint Transition:** If the progress exceeds the current keyframe's departure time, it advances to the next waypoint.
3.  **Map Teleportation:** If the next waypoint is on a different map or marked as a teleport frame, it calls `TeleportTransport` to move the entire vehicle and its passengers to the new map.
4.  **Range Updates:** If the current frame is an "update frame," it sends out-of-range and create updates to players on the map to handle visibility changes.
5.  **Position Interpolation:** If moving, it calculates the precise position along the spline segment using `CalculateSegmentPos` and updates the transport's location via `UpdatePosition`.

**Update** (Method)
The core update loop for `ElevatorTransport`. It calculates progress based on the total animation time. It identifies the previous and next animation nodes and interpolates the position linearly between them. It applies the vehicle's base rotation to the interpolated position, performs a specific coordinate adjustment (flipping the Y axis, noted as a "magical sign flip" for Vanilla/TBC compatibility), and updates the model position and passenger positions.

**MoveToNextWayPoint** (Method)
Advances the internal iterators (`m_currentFrame` and `m_nextFrame`) to the next keyframe in the path. If the end of the path is reached, it loops back to the beginning.

**CalculateSegmentPos** (Method)
Calculates the normalized position (0.0 to 1.0) along the current spline segment. It accounts for acceleration and deceleration phases defined in the transport template. It determines whether the vehicle is accelerating from a stop or decelerating towards one, calculating the distance traveled accordingly to produce smooth motion.

**GetPeriod**, **SetPeriod**, **GetKeyFrames**, **IsMoving**, **SetMoving**
Accessors and mutators for the ship transport's timing and state. `GetKeyFrames` returns the vector of keyframes defining the path. `IsMoving` and `SetMoving` track whether the vehicle is currently in transit or paused at a station.

**GetTimeSinceCreation** (Method)
Returns the difference in milliseconds between the current server time and the transport's creation time. Used by both ship and elevator updates to drive progress.

**UpdatePosition** (Method)
Updates the transport's absolute position and orientation. It relocates the object, updates its model position for visual rendering, and triggers `UpdatePassengerPositions` to move all riders.

**UpdatePassengerPositions** (Method)
Iterates through all passengers and calls `UpdatePassengerPosition` for each. It locks the passenger mutex to prevent concurrent modification during iteration.

**UpdatePassengerPosition** (Method)
Calculates the new absolute position for a single passenger based on their offset from the transport center and the transport's current transform. It validates the resulting coordinates. If valid, it relocates the passenger on the map. For players, it ensures the movement info is updated correctly; for creatures, it handles relocation differently depending on whether they are in the world.

### Passenger Management

**AddPassenger** (Method)
Adds a Unit to the transport's passenger list. It acquires a lock on the passenger mutex. If the passenger is new, it sets the transport pointer on the passenger, adds the `MOVEFLAG_ONTRANSPORT` flag to their movement info, and calculates their initial offset relative to the transport. It unlocks immediately after insertion to prevent deadlocks if `SetTransport` triggers recursive calls (e.g., summoning a pet).

**RemovePassenger** (Method)
Removes a Unit from the passenger list. It handles iterator invalidation carefully, especially if the passenger is currently being processed by `TeleportTransport`. It clears the transport pointer and transport-specific movement data from the passenger.

**AddFollowerToTransport** (Method)
Adds a follower (e.g., a pet) to the transport alongside its owner. It adds the follower as a passenger and sets its transport data to match the owner's offset. It then teleports or relocates the follower to the correct position relative to the owner.

**RemoveFollowerFromTransport** (Method)
Removes a follower from the transport. It removes the passenger entry and relocates the follower to the owner's current position, effectively detaching it from the transport's movement.

**CalculatePassengerPosition** (Static Method)
Transforms local coordinates (offset from transport center) into global world coordinates. It applies the transport's rotation and translation to the offset.

**CalculatePassengerOffset** (Static Method)
Transforms global world coordinates into local coordinates relative to the transport. This is the inverse of `CalculatePassengerPosition`, used when a passenger boards the transport to determine their fixed offset.

**CalculatePassengerOrientation** (Method)
Adjusts a passenger's orientation by adding the transport's current orientation, ensuring the passenger faces the correct direction relative to the world.

### Map Transitions & Networking

**TeleportTransport** (Method)
Handles the complex process of moving the transport and all its passengers to a new map.
1.  It determines the new instance ID and creates/joins the new map.
2.  It removes the transport from the old map.
3.  It iterates through all passengers:
    *   **Creatures:** Generally removed from the transport and teleported to a safe location (owner's position or respawn point) if they are not owned by a player on the transport. Combat states are cleared.
    *   **Players:** Resurrected if dead, fear/confuse auras removed, and combat stopped. They are teleported to the new map with their relative offset preserved. If the map ID doesn't change (rare), it just repositions them.
4.  It relocates the transport itself to the new coordinates and adds it to the new map.

**SendOutOfRangeUpdateToMap** (Method)
Sends an "out of range" update packet to all players on the current map who are *not* on this transport. This ensures clients remove the transport from their view if it has moved out of range or is transitioning.

**SendCreateUpdateToMap** (Method)
Sends a "create" update packet to all players on the current map who are *not* on this transport. This ensures clients spawn the transport visually when it enters their range or appears on the map.

## Cross-Unit Boundaries

*   **TransportMgr:** `Create#2` and `Create` are called by `TransportMgr::CreateTransport` to instantiate transports when the server starts or reloads. `Create` also calls `TransportMgr::GetTransportAnimInfo` to fetch animation data for elevators.
*   **GameObject:** Inherits from `GameObject`. Uses methods like `SetDisplayId`, `SetGoState`, `GetGOInfo`, and `UpdateModelPosition` to manage its visual representation. `CleanupsBeforeDelete` calls `GameObject::CleanupsBeforeDelete`.
*   **ObjectAccessor:** `Create#2` calls `ObjectAccessor::AddObject` to register the transport in the global object lookup table.
*   **ObjectMgr:** `Create#2` calls `ObjectMgr::GetGameObjectTemplate` to load the static definition of the transport from the database cache.
*   **World:** `Create#2` and `Create` call `World::GetCurrentMSTime` to initialize timers.
*   **Map/MapManager:** `TeleportTransport` interacts heavily with `MapManager` to create new maps, get instance IDs, and schedule instance switches. It calls `Map::Add` and `Map::Remove` to manage the transport's presence on the map grid. `UpdatePassengerPosition` calls `Map::CreatureRelocation` and `Map::PlayerRelocation` to update passenger positions on the map.
*   **Player/Unit/Creature:** `TeleportTransport` and `UpdatePassengerPosition` manipulate `Player` and `Unit` objects extensively. They call `TeleportTo`, `ResurrectPlayer`, `CombatStopWithPets`, `NearTeleportTo`, and `Relocate`. `AddPassenger` and `RemovePassenger` modify `Unit` movement info.
*   **Log:** Various methods log errors (invalid coords, missing templates) and debug info (boarding/alighting, movement steps) using `Log::Out` and `DETAIL_FILTER_LOG`.
*   **ChatHandler/MoveSplineInit/Pet/WorldSession:** These units call `AddPassenger` and `RemovePassenger` when entities board or leave transports via commands, pathing, pet loading, or player login/teleportation.

## Data Model

This unit does not directly query or modify database tables. It relies on cached data loaded by `ObjectMgr` and `TransportMgr` from tables such as `gameobject_template` and `transport_template` (implied by `TransportTemplate` and `GameObjectInfo`). The `TransportTemplate` destructor manages heap-allocated `TransportSpline` objects, but these are in-memory structures, not database rows.

## Notable Implementation Details

*   **Thread Safety:** `AddPassenger` and `RemovePassenger` use a `std::mutex` (`m_passengerMutex`) to protect the `m_passengers` set. However, `AddPassenger` unlocks the mutex *before* calling `passenger->SetTransport` to prevent deadlocks, as `SetTransport` might recursively call `AddPassenger` (e.g., when a pet is summoned). This requires careful handling of the `boarded` boolean to ensure idempotency.
*   **Iterator Invalidation:** `TeleportTransport` uses a special iterator `m_passengerTeleportItr` to traverse the passenger list while potentially modifying it (via `RemovePassenger`). `RemovePassenger` checks if the iterator being erased is the active teleport iterator and increments it if so, preventing undefined behavior.
*   **Coordinate Systems:** The unit distinguishes between global world coordinates and local transport offsets. `CalculatePassengerPosition` and `CalculatePassengerOffset` perform the trigonometric transformations between these systems. The `ElevatorTransport::Update` method includes a hardcoded Y-axis flip (`currentPos.y = -currentPos.y`) described as "magical" but necessary for compatibility with older client versions (Vanilla/TBC).
*   **Passenger Types:** The code treats `TYPEID_UNIT` (creatures) and `TYPEID_PLAYER` differently during teleportation. Creatures are often removed from the transport and placed near their owner or respawn point, while players are kept on the transport and teleported with it. This reflects game mechanics where pets/followers might behave differently than players during map transitions.
*   **Acceleration/Deceleration:** `ShipTransport::CalculateSegmentPos` implements physics-based movement with acceleration and deceleration phases, rather than constant speed, for smoother visual transitions at stops.

## Member Reference

**ShipTransport** (ctor): Initializes the ship transport with its template, setting update flags and period.
**Create#2**: Creates the ship transport object, validating coordinates and loading template data.
**CleanupsBeforeDelete**: Removes all passengers before destroying the transport object.
**GetPeriod**: Returns the total duration of the transport's path loop.
**SetPeriod**: Sets the total duration of the transport's path loop.
**GetKeyFrames**: Returns the vector of keyframes defining the transport's path.
**MoveToNextWayPoint**: Advances the internal frame iterators to the next waypoint in the path.
**IsMoving**: Returns whether the transport is currently moving or paused.
**SetMoving**: Sets the moving state of the transport.
**TeleportTransport**: Moves the transport and all its passengers to a new map, handling instance switches and passenger relocation.
**AddPassenger**: Adds a Unit to the transport's passenger list, calculating its initial offset.
**RemovePassenger**: Removes a Unit from the transport's passenger list, clearing its transport data.
**AddFollowerToTransport**: Adds a follower to the transport, matching its owner's offset.
**RemoveFollowerFromTransport**: Removes a follower from the transport, relocating it to the owner's position.
**Update#2**: Updates the ship transport's position and state based on elapsed time and keyframes.
**CalculateSegmentPos**: Calculates the normalized position along the current spline segment, accounting for acceleration.
**Create**: Creates the elevator transport object, loading animation data.
**Update**: Updates the elevator transport's position by interpolating between animation nodes.
**GetTimeSinceCreation**: Returns the time elapsed since the transport was created.
**UpdatePosition**: Updates the transport's position and triggers passenger position updates.
**UpdatePassengerPositions**: Iterates through all passengers and updates their positions.
**UpdatePassengerPosition**: Calculates and applies the new absolute position for a single passenger.
**CalculatePassengerOrientation**: Adjusts a passenger's orientation by the transport's rotation.
**CalculatePassengerPosition**: Transforms local transport offsets into global world coordinates.
**CalculatePassengerOffset**: Transforms global world coordinates into local transport offsets.
**SendOutOfRangeUpdateToMap**: Sends out-of-range update packets to players on the map.
**SendCreateUpdateToMap**: Sends create update packets to players on the map.
**~TransportTemplate**: Destroys the transport template, cleaning up shared spline memory.

---

<!-- machine-true, projected from graph.json -->

## Map — Transport

*Source:* Transport.cpp, Transport.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ShipTransport | ctor | GenericTransport/GenericTransport | TransportMgr/CreateTransport | — |
| Create#2 | method | GameObject/SetDisplayId, GameObject/SetGoAnimProgress, GameObject/SetGoState, GameObject/SetGoType, Log.Main/Out, Object/SetEntry, ObjectAccessor/AddObject#4, ObjectMgr/GetGameObjectTemplate, World/GetCurrentMSTime, WorldObject.Object/IsPositionValid, WorldObject.Object/Relocate#2, WorldObject.Object/SetObjectScale, WorldObject.Object/SetUInt32Value, WorldObject.Object/_Create | TransportMgr/CreateTransport | — |
| CleanupsBeforeDelete | method | GameObject/CleanupsBeforeDelete | Map.Main/Remove#5 | — |
| GetPeriod | method | — | — | — |
| SetPeriod | method | — | — | — |
| GetKeyFrames | method | — | — | — |
| MoveToNextWayPoint | method | — | — | — |
| IsMoving | method | — | — | — |
| SetMoving | method | — | — | — |
| TeleportTransport | method | Creature.Main/GetRespawnCoord, Creature.Main/OnLeaveCombat, Map.Main/Add#6, Map.Main/Remove#6, MapManager/CreateMap, MapManager/GetContinentInstanceId, MapManager/ScheduleInstanceSwitch, MovementInfo/SetAsServerSide, Object/GetTypeId, Object/IsInWorld, Player.Main/ResurrectPlayer, Player.Main/TeleportTo, Unit.Main/CombatStopWithPets, Unit.Main/GetOwnerPlayer, Unit.Main/IsAlive, Unit.Main/IsInCombat, Unit.Main/NearTeleportTo, Unit.Main/RemoveSpellsCausingAura, Unit.Main/TeleportPositionRelocation, WorldObject.Object/GetInstanceId, WorldObject.Object/GetMap, WorldObject.Object/GetMapId, WorldObject.Object/GetPosition#2, WorldObject.Object/GetTransOffsetO, WorldObject.Object/GetTransOffsetX, WorldObject.Object/GetTransOffsetY, WorldObject.Object/GetTransOffsetZ, WorldObject.Object/GetTransport, WorldObject.Object/Relocate#2, WorldObject.Object/SetLocationInstanceId, WorldObject.Object/SetMap | — | — |
| AddPassenger | method | GameObject/GetName, GenericTransport/CalculatePassengerOffset, Log.Main/Out, MovementInfo/AddMovementFlag, Object/GetObjectGuid, ObjectGuid/operator!=, WorldObject.Object/GetName, WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/SetTransport | ChatHandler.DebugCommands/HandleMmapLocCommand, ChatHandler.DebugCommands/HandleMmapPathCommand, MoveSplineInit/Launch, Pet.Main/LoadPetFromDB, Player.Main/LoadFromDB, Player.Main/SummonPossessedMinion, WorldObject.Object/SummonCreature#2, WorldSession.MovementHandler/HandleMoverRelocation | — |
| RemovePassenger | method | GameObject/GetName, Log.Main/Out, MovementInfo/ClearTransportData, WorldObject.Object/GetName, WorldObject.Object/SetTransport | Creature.MotionMaster/MoveTargetedHome, MoveSplineInit/Launch, Player.Main/RepopAtGraveyard, Player.Main/SetFly, Player.Main/SwitchInstance, Player.Main/TeleportTo, WorldObject.Object/CleanupsBeforeDelete, WorldSession.MovementHandler/HandleMoverRelocation | — |
| AddFollowerToTransport | method | MovementInfo/SetTransportData, Object/GetObjectGuid, Object/IsCreature, Unit.Main/NearTeleportTo, Unit.Main/SendHeartBeat, WorldObject.Object/Relocate#2 | TargetedMovementGenerator/_setTargetLocation#2 | — |
| RemoveFollowerFromTransport | method | Object/IsCreature, Unit.Main/NearTeleportTo, Unit.Main/SendHeartBeat, WorldObject.Object/Relocate#2 | — | — |
| Update#2 | method | GameObject/GetName, KeyFrame/IsTeleportFrame, KeyFrame/IsUpdateFrame, Log.Main/HasLogFilter, Log.Main/HasLogLevelOrHigher, Log.Main/Out, ShortTimeTracker/Passed, ShortTimeTracker/Reset, ShortTimeTracker/Update, WorldObject.Object/GetMapId | — | — |
| CalculateSegmentPos | method | — | — | — |
| Create | method | GameObject/Create, GameObject/GetGOInfo, TransportMgr/GetTransportAnimInfo, World/GetCurrentMSTime | — | — |
| Update | method | GameObject/GetLocalRotation, GameObject/UpdateModelPosition, TransportMgr/GetNextAnimNode, TransportMgr/GetPrevAnimNode, WorldObject.Object/GetOrientation, WorldObject.Object/Relocate#2 | — | — |
| GetTimeSinceCreation | method | WorldTimer/getMSTimeDiffToNow | — | — |
| UpdatePosition | method | GameObject/UpdateModelPosition, WorldObject.Object/Relocate#2 | — | — |
| UpdatePassengerPositions | method | — | — | — |
| UpdatePassengerPosition | method | GenericTransport/CalculatePassengerPosition, GridDefines/IsValidMapCoord#3, Log.Main/Out, Map.Main/CreatureRelocation, Map.Main/PlayerRelocation, Object/GetGUIDLow, Object/GetObjectGuid, Object/GetTypeId, Object/IsInWorld, WorldObject.Object/FindMap, WorldObject.Object/GetMap, WorldObject.Object/GetName, WorldObject.Object/GetTransOffsetO, WorldObject.Object/GetTransOffsetX, WorldObject.Object/GetTransOffsetY, WorldObject.Object/GetTransOffsetZ, WorldObject.Object/Relocate#2 | Pet.Main/LoadPetFromDB, WorldSession.MovementHandler/HandleMoveWorldportAck | — |
| CalculatePassengerOrientation | method | Geometry/NormalizeOrientation, WorldObject.Object/GetOrientation | — | — |
| CalculatePassengerPosition | method | Geometry/NormalizeOrientation | — | — |
| CalculatePassengerOffset | method | Geometry/NormalizeOrientation | — | — |
| SendOutOfRangeUpdateToMap | method | LinkedListHead/isEmpty, Map.Main/GetPlayers, Player.Main/SendDirectMessage, UpdateData/BuildPacket#3, UpdateData/UpdateData, WorldObject.Object/BuildOutOfRangeUpdateBlock, WorldObject.Object/GetMap, WorldObject.Object/GetTransport, WorldPacket/WorldPacket | Map.Main/Remove#5 | — |
| SendCreateUpdateToMap | method | LinkedListHead/isEmpty, Map.Main/GetPlayers, Player.Main/GetSession, UpdateData/Send, UpdateData/UpdateData, WorldObject.Object/BuildCreateUpdateBlockForPlayer, WorldObject.Object/GetMap, WorldObject.Object/GetTransport | Map.Main/Add#5 | — |
| ~TransportTemplate | dtor | — | — | — |
