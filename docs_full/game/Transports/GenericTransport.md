<!-- provenance: verbose -->
# GenericTransport

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# GenericTransport

**Purpose & Responsibilities**

`GenericTransport` is the abstract base class for moving vehicles (ships, zeppelins, elevators, trams) in `wowvmangos`. Inheriting from `GameObject`, it manages the spatial relationship between the vehicle’s global movement and its passengers (`Unit`s). Its core responsibility is coordinate transformation: converting between **global world coordinates** and **local transport offsets** so passengers remain fixed relative to the vehicle while it moves. It maintains a thread-safe set of active passengers and exposes methods to calculate their current global positions or local offsets based on the transport’s current location and orientation. Specific movement logic (keyframes, animations) is handled by derived classes (`ShipTransport`, `ElevatorTransport`), while `GenericTransport` handles passenger synchronization.

## Member-by-Member Behavior

### Construction

**`GenericTransport`**
Initializes internal state: sets `m_passengerTeleportItr` to `m_passengers.end()`, and resets `m_pathProgress` and `m_creationTime` to 0. Called by derived classes `Transport/ShipTransport` (and implicitly `ElevatorTransport`) to instantiate specific vehicle types.

### Passenger Management

**`GetPassengers`**
Returns a reference to the `std::set<Unit*>` `m_passengers`. Allows external systems to iterate over passengers.
*   **Called by:** `GridNotifiers/Notify` (grid notifications) and `Map.Main/SendInitSelf` (map initialization).

### Coordinate Transformation

**`CalculatePassengerPosition`**
Transforms **local offsets** to **global world coordinates**. The instance overload retrieves the transport’s current global position/orientation via `GameObject` methods and delegates to the static implementation. The static version applies rotation and translation matrices.
*   **Called by:** `ChatHandler.DebugCommands/HandleMmapLocCommand`, `ChatHandler.DebugCommands/HandleMmapPathCommand`, `Map.Main/GetWalkHitPosition`, `Map.Main/GetWalkRandomPosition`, `MoveSplineInit/Launch`, `Player.Main/LoadFromDB`, `TargetedMovementGenerator/Update`, `TargetedMovementGenerator/Update#2`, `TargetedMovementGenerator/_setTargetLocation`, `TargetedMovementGenerator/_setTargetLocation#2`, `Transport/UpdatePassengerPosition`, `Unit.Main/UpdateSplineMovement`, `WorldObject.Object/MovePositionToFirstCollision`, `WorldSession.MovementHandler/HandleMoverRelocation`.

**`CalculatePassengerOffset`**
Transforms **global world coordinates** to **local transport offsets**. The instance overload retrieves the transport’s current global state and delegates to the static implementation. The static version applies inverse rotation and translation. Crucial for boarding logic and saving positions.
*   **Called by:** `ChatHandler.DebugCommands/HandleMmapLocCommand`, `Map.Main/GetWalkHitPosition`, `Map.Main/GetWalkRandomPosition`, `MoveSplineInit/Launch`, `Transport/AddPassenger`, `WorldObject.Object/GetPosition#2`, `WorldObject.Object/MovePositionToFirstCollision`, `WorldObject.PathFinder/calculate`.

### State Reporting

**`GetPathProgress`**
Returns `m_pathProgress`, tracking the transport’s progress along its route or animation cycle.
*   **Called by:** `WorldObject.Object/BuildMovementUpdate` (network synchronization).

## Cross-Unit Boundaries

*   **Map & WorldObject:** `Map.Main` calls `GetPassengers` for initialization and `CalculatePassengerPosition/Offset` for pathfinding. `WorldObject.Object` calls these methods for collision detection and position retrieval, ensuring physics treats passengers correctly. `WorldObject.Object/BuildMovementUpdate` calls `GetPathProgress` for network packets.
*   **Movement Systems:** `MoveSplineInit` and `TargetedMovementGenerator` call `CalculatePassengerPosition/Offset` to convert local targets/origins to global space, preventing passengers from slipping off.
*   **Networking:** `WorldSession.MovementHandler/HandleMoverRelocation` calls `CalculatePassengerPosition` to validate client-reported positions against expected global coordinates.
*   **Persistence:** `Player.Main/LoadFromDB` calls `CalculatePassengerPosition` to spawn players at the correct global location based on their saved local offset and the transport’s current state.
*   **Debugging:** `ChatHandler.DebugCommands` uses these methods to inspect coordinates in both frames.

## Data Model

`GenericTransport` does not interact with any database tables. It operates entirely on in-memory state.

## Notable Implementation Details

1.  **Static Math:** Coordinate transformations are implemented in `static` methods. Instance methods merely fetch current global state and delegate. This allows reuse without a transport instance.
2.  **Thread Safety:** `m_passengers` is protected by `std::mutex m_passengerMutex`, indicating concurrent access from gameplay and network threads.
3.  **No Stored Positions:** The class stores `Unit*` pointers, not positions. Passengers’ local offsets are stored within the `Unit` objects themselves.
4.  **Inheritance:** Derived classes (`ShipTransport`, `ElevatorTransport`) handle movement logic; `GenericTransport` handles passenger sync.

## Member Reference

**`GenericTransport`**
Constructor initializing passenger iterator, path progress, and creation time. Called by `Transport/ShipTransport`.

**`GetPassengers`**
Returns reference to `m_passengers` set. Called by `GridNotifiers/Notify` and `Map.Main/SendInitSelf`.

**`CalculatePassengerPosition`**
Transforms local offsets to global coordinates. Instance overload delegates to static implementation. Called by `ChatHandler.DebugCommands/HandleMmapLocCommand`, `ChatHandler.DebugCommands/HandleMmapPathCommand`, `Map.Main/GetWalkHitPosition`, `Map.Main/GetWalkRandomPosition`, `MoveSplineInit/Launch`, `Player.Main/LoadFromDB`, `TargetedMovementGenerator/Update`, `TargetedMovementGenerator/Update#2`, `TargetedMovementGenerator/_setTargetLocation`, `TargetedMovementGenerator/_setTargetLocation#2`, `Transport/UpdatePassengerPosition`, `Unit.Main/UpdateSplineMovement`, `WorldObject.Object/MovePositionToFirstCollision`, `WorldSession.MovementHandler/HandleMoverRelocation`.

**`CalculatePassengerOffset`**
Transforms global coordinates to local offsets. Instance overload delegates to static implementation. Called by `ChatHandler.DebugCommands/HandleMmapLocCommand`, `Map.Main/GetWalkHitPosition`, `Map.Main/GetWalkRandomPosition`, `MoveSplineInit/Launch`, `Transport/AddPassenger`, `WorldObject.Object/GetPosition#2`, `WorldObject.Object/MovePositionToFirstCollision`, `WorldObject.PathFinder/calculate`.

**`GetPathProgress`**
Returns `m_pathProgress`. Called by `WorldObject.Object/BuildMovementUpdate`.

---

<!-- machine-true, projected from graph.json -->

## Map — GenericTransport

*Source:* Transport.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GenericTransport | ctor | — | Transport/ShipTransport | — |
| GetPassengers | method | — | GridNotifiers/Notify, Map.Main/SendInitSelf | — |
| CalculatePassengerPosition | method | — | ChatHandler.DebugCommands/HandleMmapLocCommand, ChatHandler.DebugCommands/HandleMmapPathCommand, Map.Main/GetWalkHitPosition, Map.Main/GetWalkRandomPosition, MoveSplineInit/Launch, Player.Main/LoadFromDB, TargetedMovementGenerator/Update, TargetedMovementGenerator/Update#2, TargetedMovementGenerator/_setTargetLocation, TargetedMovementGenerator/_setTargetLocation#2, Transport/UpdatePassengerPosition, Unit.Main/UpdateSplineMovement, WorldObject.Object/MovePositionToFirstCollision, WorldSession.MovementHandler/HandleMoverRelocation | — |
| CalculatePassengerOffset | method | — | ChatHandler.DebugCommands/HandleMmapLocCommand, Map.Main/GetWalkHitPosition, Map.Main/GetWalkRandomPosition, MoveSplineInit/Launch, Transport/AddPassenger, WorldObject.Object/GetPosition#2, WorldObject.Object/MovePositionToFirstCollision, WorldObject.PathFinder/calculate | — |
| GetPathProgress | method | — | WorldObject.Object/BuildMovementUpdate | — |
