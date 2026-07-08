<!-- provenance: verbose -->
# PathInfo

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# PathInfo

`PathInfo` (aliased as `PathFinder`) is a data container for navigation results within the MaNGOS server. It bridges the Recast/Detour navigation mesh library and the high-level movement systems. It stores the geometric definition of a path—start position, intended end position, actual reachable end position, and the sequence of waypoints (`PointsArray`)—along with metadata describing the path's nature (e.g., normal, shortcut, incomplete, underwater).

It does not perform path calculation itself; that is handled by `WorldObject.PathFinder`. Instead, `PathInfo` holds the state required to execute movement. Once populated, instances are consumed by movement splines (`MoveSplineInit`) and various movement generators (`AiBotAI`, `FearMovementGenerator`, etc.) to determine how a unit traverses the world.

## Member-by-Member Behavior

### Path Configuration and State Management

**`setUseStrightPath`**
Sets the internal flag `m_useStraightPath`. When enabled, the pathfinder bypasses complex obstacle avoidance logic, generating a direct line to the destination.

**`ExcludeSteepSlopes`**
Configures the underlying Detour navigation filter (`m_filter`) to exclude polygons marked as steep slopes (`NAV_STEEP_SLOPES`). This ensures grounded units do not attempt to walk up cliffs. Called by `ConfusedMovementGenerator`, `FearMovementGenerator`, and `FleeingMovementGenerator`.

**`SetTransport` / `GetTransport`**
Associates a `GenericTransport` object with the path. Transports (boats, elevators) move independently of the static world grid. Linking a transport allows the system to adjust path coordinates relative to the transport's movement. `SetTransport` is used by debug commands, movement generators, and spell logic. `GetTransport` retrieves this association for use in spline initialization (`MoveSplineInit/Move`).

**`clear`**
Resets the path state by zeroing the polygon count (`m_polyLength`) and clearing the waypoint array (`m_pathPoints`). Called by `WorldObject.PathFinder` methods (`BuildPathWithoutMMaps`, `BuildPolyPath`, `BuildShortcut`, `BuildUnderwaterPath`, `UpdateForCaster`, `UpdateForMelee`) to prepare for new calculations or discard invalid paths.

### Position Accessors

The class distinguishes between three positions:
1.  **Start Position**: Where the unit begins.
2.  **End Position**: The requested destination.
3.  **Actual End Position**: The closest reachable point on the navigation mesh to the requested destination.

**`getStartPosition` / `setStartPosition`**
Retrieves or sets the starting vector. The setter is used internally by `WorldObject.PathFinder/calculate`. The getter has two overloads: one returning by reference and one returning a `Vector3` copy. The copy version (`getStartPosition#2`) is used by `WorldObject.PathFinder` helpers (`BuildPointPath`, `BuildShortcut`, etc.).

**`getEndPosition` / `setEndPosition`**
Retrieves or sets the target destination. The setter updates both `m_endPosition` and `m_actualEndPosition` initially. The getter (`getEndPosition#2`) is used by `PointMovementGenerator/Update` and `WorldObject.PathFinder`.

**`getActualEndPosition` / `setActualEndPosition`**
Retrieves or sets the final reachable point. Critical for "incomplete" paths. `WorldObject.PathFinder` uses this to report the best-effort destination.

### Path Data and Type Accessors

**`getPath` / `getFullPath`**
Returns the `PointsArray` containing the sequence of 3D coordinates. `getPath` returns a const reference, used by `AiBotAI`, `MoveSplineInit`, and movement generators. `getFullPath` returns a non-const reference, primarily used by `ChatHandler.DebugCommands/HandleMmapPathCommand`.

**`getPathType`**
Returns the `PathType` enum value indicating the path's status. Consumed by almost all movement systems:
*   `AiBotAI.Movement` checks safety/completeness.
*   `Spell.Main/CheckCast` verifies reachability.
*   Movement generators (`Fear`, `Fleeing`, `Home`, `Targeted`) use this to decide behavior.

## Cross-Unit Boundaries

*   **WorldObject.PathFinder**: Primary producer. Populates `PathInfo` with waypoints and sets its type via methods like `BuildPointPath`, `BuildShortcut`, `calculate`, etc.
*   **Movement Generators (`AiBotAI.Movement`, `FearMovementGenerator`, `FleeingMovementGenerator`, `HomeMovementGenerator`, `PointMovementGenerator`, `TargetedMovementGenerator`)**: Consumers. Read `getPathType`, `getPath`, and configure options like `ExcludeSteepSlopes` or `SetTransport`.
*   **MoveSplineInit**: Initializes the movement spline. Consumes `getPath`, `GetTransport`, and `getPathType` to construct the movement curve.
*   **ChatHandler.DebugCommands**: Administrative debugging. Accesses `getFullPath`, `getPathType`, and `SetTransport`.
*   **Spell.Main**: Checks path validity (`getPathType`) during spell casting.

## Data Model

`PathInfo` does not interact with any database tables. All data is transient, existing only in memory for the duration of a path calculation and subsequent movement.

## Notable Implementation Details

*   **Alias `PathFinder`**: The typedef `typedef PathInfo PathFinder;` means `PathFinder` refers to this class.
*   **Detour Integration**: Relies on `dtPolyRef` and `dtNavMeshQuery`. `m_pathPolyRefs` stores raw polygon indices, converted to `PointsArray` for the game engine.
*   **Path Types**: The `PathType` enum is bitmask-based. Key types include `PATHFIND_INCOMPLETE` (partial path) and `PATHFIND_NOPATH` (failure).
*   **Transport Handling**: `m_transport` pointer allows paths relative to moving objects. Incorrect setting causes units to jump off transports.
*   **Steep Slope Exclusion**: `ExcludeSteepSlopes` modifies a shared `dtQueryFilter`. Care is needed to ensure exclusions don't affect concurrent calculations if the filter isn't properly scoped.

## Member Reference

**`setUseStrightPath`**
Sets the `m_useStraightPath` boolean flag, instructing the pathfinder to generate a direct line to the destination, ignoring obstacles.

**`getStartPosition`**
Returns the starting position of the path as a `Vector3` or via reference parameters. Used by `WorldObject.PathFinder` helpers to retrieve the origin for path building.

**`getEndPosition`**
Returns the requested destination position as a `Vector3` or via reference parameters. Used by `PointMovementGenerator` and `WorldObject.PathFinder` to identify the target.

**`getActualEndPosition`**
Returns the closest reachable position to the destination as a `Vector3` or via reference parameters. Used by `WorldObject.PathFinder` to report the effective endpoint of the path.

**`getStartPosition#2`**
Overload of `getStartPosition` returning a `Vector3` copy. Called by `WorldObject.PathFinder` methods (`BuildPointPath`, `BuildShortcut`, `BuildUnderwaterPath`, `UpdateForCaster`, `UpdateForMelee`) to pass start coordinates to Detour functions.

**`getEndPosition#2`**
Overload of `getEndPosition` returning a `Vector3` copy. Called by `PointMovementGenerator/Update` and `WorldObject.PathFinder` (`BuildPointPath`, `calculate`) to pass end coordinates to Detour functions.

**`getActualEndPosition#2`**
Overload of `getActualEndPosition` returning a `Vector3` copy. Called by `WorldObject.PathFinder` methods (`BuildPointPath`, `BuildShortcut`, `BuildUnderwaterPath`) to retrieve the effective endpoint for path construction.

**`getFullPath`**
Returns a non-const reference to the `PointsArray` of waypoints. Primarily used by `ChatHandler.DebugCommands/HandleMmapPathCommand` for debugging output.

**`getPath`**
Returns a const reference to the `PointsArray` of waypoints. Consumed by `AiBotAI.Movement` (`IsPathSafe`, `MoveToDestination`), `MoveSplineInit/Move`, `TargetedMovementGenerator/_setTargetLocation`, and `WorldObject.Object/MovePositionToFirstCollision` to execute movement along the path.

**`getPathType`**
Returns the `PathType` enum indicating the path's status (e.g., normal, incomplete, no path). Checked by `AiBotAI.Bridge/BridgeHandleMoveTo`, `AiBotAI.Movement` (`IsPathSafe`, `MoveToDestination`), `ChatHandler.DebugCommands` (`HandleMmapPathCommand`, `HandleMmapTestArea`), `FearMovementGenerator/_setTargetLocation`, `FleeingMovementGenerator/_setTargetLocation`, `HomeMovementGenerator/_setTargetLocation`, `MoveSplineInit/Move`, `PointMovementGenerator/Initialize`, `Spell.Main/CheckCast`, `TargetedMovementGenerator/_setTargetLocation` (and #2), and `WorldObject.Object/MovePositionToFirstCollision` to validate path usability.

**`ExcludeSteepSlopes`**
Configures the navigation filter to avoid steep slopes. Called by `ConfusedMovementGenerator/Update`, `FearMovementGenerator/_setTargetLocation`, and `FleeingMovementGenerator/_setTargetLocation` to ensure safe retreat paths.

**`SetTransport`**
Associates a `GenericTransport` with the path. Used by `ChatHandler.DebugCommands/HandleMmapPathCommand`, `ConfusedMovementGenerator/Update`, `FearMovementGenerator/_setTargetLocation`, `FleeingMovementGenerator/_setTargetLocation`, `PointMovementGenerator/ComputePath`, `Spell.Main/CheckCast`, `TargetedMovementGenerator/_setTargetLocation` (and #2) to handle movement on moving vehicles.

**`GetTransport`**
Retrieves the associated `GenericTransport`. Used by `MoveSplineInit/Move` to adjust path coordinates relative to the transport.

**`setStartPosition`**
Sets the starting position vector. Called by `WorldObject.PathFinder/calculate` to initialize the path origin.

**`setEndPosition`**
Sets the destination position vector, updating both `m_endPosition` and `m_actualEndPosition`. Called by `WorldObject.PathFinder/calculate` to define the target.

**`setActualEndPosition`**
Sets the actual reachable end position vector. Called by `WorldObject.PathFinder` methods (`BuildPointPath`, `BuildPolyPath`) to record the effective endpoint.

**`clear`**
Resets the path data (polygon count and waypoints). Called by `WorldObject.PathFinder` methods (`BuildPathWithoutMMaps`, `BuildPolyPath`, `BuildShortcut`, `BuildUnderwaterPath`, `UpdateForCaster`, `UpdateForMelee`) to prepare for new calculations or discard invalid paths.

---

<!-- machine-true, projected from graph.json -->

## Map — PathInfo

*Source:* PathFinder.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| setUseStrightPath | method | — | — | — |
| getStartPosition | method | — | — | — |
| getEndPosition | method | — | — | — |
| getActualEndPosition | method | — | — | — |
| getStartPosition#2 | method | — | WorldObject.PathFinder/BuildPointPath, WorldObject.PathFinder/BuildShortcut, WorldObject.PathFinder/BuildUnderwaterPath, WorldObject.PathFinder/UpdateForCaster, WorldObject.PathFinder/UpdateForMelee | — |
| getEndPosition#2 | method | — | PointMovementGenerator/Update#2, WorldObject.PathFinder/BuildPointPath, WorldObject.PathFinder/calculate | — |
| getActualEndPosition#2 | method | — | WorldObject.PathFinder/BuildPointPath, WorldObject.PathFinder/BuildShortcut, WorldObject.PathFinder/BuildUnderwaterPath | — |
| getFullPath | method | — | ChatHandler.DebugCommands/HandleMmapPathCommand | — |
| getPath | method | — | AiBotAI.Movement/IsPathSafe, AiBotAI.Movement/MoveToDestination, MoveSplineInit/Move, TargetedMovementGenerator/_setTargetLocation, WorldObject.Object/MovePositionToFirstCollision | — |
| getPathType | method | — | AiBotAI.Bridge/BridgeHandleMoveTo, AiBotAI.Movement/IsPathSafe, AiBotAI.Movement/MoveToDestination, ChatHandler.DebugCommands/HandleMmapPathCommand, ChatHandler.DebugCommands/HandleMmapTestArea, FearMovementGenerator/_setTargetLocation, FleeingMovementGenerator/_setTargetLocation, HomeMovementGenerator/_setTargetLocation, MoveSplineInit/Move, PointMovementGenerator/Initialize#2, Spell.Main/CheckCast, TargetedMovementGenerator/_setTargetLocation, TargetedMovementGenerator/_setTargetLocation#2, WorldObject.Object/MovePositionToFirstCollision | — |
| ExcludeSteepSlopes | method | — | ConfusedMovementGenerator/Update, FearMovementGenerator/_setTargetLocation, FleeingMovementGenerator/_setTargetLocation | — |
| SetTransport | method | — | ChatHandler.DebugCommands/HandleMmapPathCommand, ConfusedMovementGenerator/Update, FearMovementGenerator/_setTargetLocation, FleeingMovementGenerator/_setTargetLocation, PointMovementGenerator/ComputePath, Spell.Main/CheckCast, TargetedMovementGenerator/_setTargetLocation, TargetedMovementGenerator/_setTargetLocation#2 | — |
| GetTransport | method | — | MoveSplineInit/Move | — |
| setStartPosition | method | — | WorldObject.PathFinder/calculate | — |
| setEndPosition | method | — | WorldObject.PathFinder/calculate | — |
| setActualEndPosition | method | — | WorldObject.PathFinder/BuildPointPath, WorldObject.PathFinder/BuildPolyPath | — |
| clear | method | — | WorldObject.PathFinder/BuildPathWithoutMMaps, WorldObject.PathFinder/BuildPolyPath, WorldObject.PathFinder/BuildShortcut, WorldObject.PathFinder/BuildUnderwaterPath, WorldObject.PathFinder/UpdateForCaster, WorldObject.PathFinder/UpdateForMelee | — |
