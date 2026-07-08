<!-- provenance: boundary-bleed -->
# WorldObject.PathFinder

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# WorldObject.PathFinder

## Purpose & Responsibilities

The `WorldObject.PathFinder` unit implements the core navigation logic for entities (`Unit`) moving within the game world. It is responsible for calculating valid paths from a starting position to a destination, accounting for terrain geometry, obstacles, and entity capabilities (flying, swimming, walking).

The system operates in two primary modes:
1.  **MMAP-based Navigation:** Uses pre-computed navigation meshes (Recast/Detour library) for precise obstacle avoidance and pathfinding on maps with available `.mmtile` data.
2.  **Fallback Navigation:** Uses basic terrain height queries and recursive step-checking for maps without MMAP data or when MMAP queries fail.

Key responsibilities include:
- Determining if a direct path is possible (shortcut) or if complex navigation is required.
- Handling special movement states: underwater, flying, and swimming.
- Optimizing path updates for moving targets by reusing previous path segments.
- Providing utility methods for Line of Sight (LoS) checks and dynamic path truncation for combat scenarios (melee/ranged).

This unit does not handle the actual movement execution; it produces a sequence of `Vector3` points (`m_pathPoints`) that movement generators consume.

## Member-by-Member Behavior

### Path Calculation Core

**`PathInfo` (ctor)**
Initializes the path finder for a specific `Unit`. It sets up internal state variables, clears previous path data, and calls `createFilter()` to configure the Detour query filter based on the unit's mobility flags (walk/swim).

**`~PathInfo` (dtor)**
Destructor. Currently performs no cleanup beyond standard member destruction, as the Detour resources are managed externally or are lightweight copies.

**`calculate` (method, overload #2)**
The primary entry point for path calculation. It accepts raw coordinates (`destX`, `destY`, `destZ`). It retrieves the unit's current safe position via `WorldObject.Object/GetSafePosition` and delegates to the `Vector3` overload of `calculate`.

**`calculate` (method, overload #1)**
Performs the high-level orchestration of path finding:
1.  Clears existing path data.
2.  Acquires a thread-safe `dtNavMeshQuery` from the `MoveMap` manager. If the unit is on a transport, it uses the transport's model nav mesh; otherwise, it uses the map's nav mesh.
3.  If no nav mesh query is available, it falls back to `BuildPathWithoutMMaps`.
4.  Checks if tiles are loaded for start/end positions (`HaveTiles`). If not, or if the unit ignores pathfinding, it builds a shortcut.
5.  **Optimization:** If the destination hasn't moved significantly since the last calculation, it reuses the existing path by popping the first point (assuming the unit has moved forward along it).
6.  If the target moved or no previous path exists, it calls `BuildPolyPath` to compute a new polygon-based path.

**`setPathLengthLimit`**
Sets the maximum number of points allowed in the final smoothed path. This limits memory usage and processing time for long paths.

### Polygon Path Building (MMAP)

**`BuildPolyPath`**
Constructs the high-level polygon path using the Detour library.
1.  Determines if the unit can fly or swim directly to the destination. If yes, and no collision models block the way, it delegates to `BuildShortcut` or `BuildUnderwaterPath`.
2.  Finds the nearest navigation polygons (`dtPolyRef`) for the start and end positions using `getPolyByLocation`.
3.  If start or end polygons are invalid (e.g., off-mesh), it attempts to build a shortcut or marks the path as incomplete/nopath depending on mobility.
4.  **Path Reuse:** It checks if the start or end polygons exist in the previously calculated path (`m_pathPolyRefs`).
    -   If both are found, it extracts the sub-path between them.
    -   If only the start is found, it keeps the prefix and calculates a new suffix to the new end point.
    -   If neither is found, it calculates a completely new path from scratch.
5.  Finally, it calls `BuildPointPath` to convert the polygon sequence into concrete coordinate points.

**`getPolyByLocation`**
Finds the nearest navigation polygon to a given coordinate. It uses `FindWalkPoly` internally, adjusting the search filter to include target-specific flags (like steep slopes) if provided.

**`FindWalkPoly`**
A static helper that wraps the Detour `findNearestPoly` function. It ensures the found polygon is not significantly higher than the input point (preventing selection of floating platforms if the unit is on the ground).

**`HaveTiles`**
Checks if the navigation mesh tiles for the start and end coordinates are currently loaded in memory. This prevents querying unloaded areas, which would cause errors.

### Point Path Generation & Smoothing

**`BuildPointPath`**
Converts the polygon path (`m_pathPolyRefs`) into a list of `Vector3` coordinates (`m_pathPoints`).
1.  Chooses between `findSmoothPath` (for natural-looking curves) or Detour's `findStraightPath` (for direct corners) based on `m_useStraightPath`.
2.  Iterates through the resulting points, calling `WorldObject.Object/UpdateAllowedPositionZ` to ensure each point has a valid Z-height relative to the terrain.
3.  Sets the `m_actualEndPosition` to the last point in the path.
4.  If `m_forceDestination` is true, it adjusts the final point to match the requested destination exactly, potentially marking the path as forced.

**`findSmoothPath`**
Generates a smoothed path by stepping along the polygon path.
1.  Uses `getSteerTarget` to find the next steering point.
2.  Moves along the surface using `moveAlongSurface`.
3.  Handles off-mesh connections (bridges, doors) by inserting specific start/end points for those links.
4.  Simplifies the path by removing redundant points that lie nearly on the same line (`Distance2DPointToLineYZX`).

**`getSteerTarget`**
Determines the next immediate target point to steer towards along the straight path approximation. It stops at off-mesh connections or when the point is sufficiently far from the current position.

**`fixupCorridor`**
A Detour utility wrapper that updates the path corridor when the agent moves. It merges the visited polygons with the remaining path to maintain a valid search corridor.

**`fixupShortcuts`**
A static function that optimizes the path by cutting corners. If a neighbor polygon of the current position is reachable further down the path, it shortcuts directly to it.

### Fallback & Special Paths

**`BuildPathWithoutMMaps`**
Used when MMAP data is unavailable.
1.  Checks if the unit can fly or swim directly.
2.  If walking, it uses a recursive algorithm (`BuildPathStep`) to find a path by checking terrain heights at regular intervals around the target direction.
3.  This method is less precise than MMAP but allows movement on maps without navigation meshes.

**`BuildPathStep`**
A static recursive function used by `BuildPathWithoutMMaps`. It tries 12 angles around the current position to find a valid next step that doesn't involve steep cliffs or invalid terrain.

**`BuildShortcut`**
Creates a direct two-point path from start to end. Used when flying, swimming directly, or when pathfinding fails/is disabled. Marks the path type as `PATHFIND_SHORTCUT`.

**`BuildUnderwaterPath`**
Handles underwater movement. It checks liquid status at the destination. If the destination is underwater and the unit can swim, it creates a path that respects the water level, ensuring the unit doesn't clip through the surface if it cannot fly.

### Combat & Utility Adjustments

**`UpdateForCaster`**
Truncates the path for ranged attackers. It scans the existing path points to find the first point from which the caster has Line of Sight (LoS) and is within range of the target. The path is resized to end at that point.

**`UpdateForMelee`**
Truncates the path for melee attackers. It finds the first path point where the attacker can reach the target with a melee auto-attack. The path ends there.

**`CutPathWithDynamicLoS`**
Truncates the path if a dynamic object (like a moving player or creature) blocks the line of sight between path segments. It uses `Map.Main/GetDynamicObjectHitPos` to detect intersections.

**`Length`**
Calculates the total Euclidean distance of the path by summing the distances between consecutive points.

**`FillTargetAllowedFlags`**
Configures the navigation filter to allow the path to traverse terrain types suitable for the *target* unit (e.g., allowing steep slopes if the target is a non-player creature). This is used when pathfinding *to* a target.

**`createFilter`**
Initializes the Detour query filter based on the *source* unit's capabilities.
-   Walkers: Allow `NAV_GROUND`.
-   Swimmers: Allow `NAV_WATER`. Creatures also allow `NAV_MAGMA` and `NAV_SLIME` (as they don't take environmental damage).

**`updateFilter`**
Currently empty placeholder. Intended for dynamic filter updates during pathfinding.

**`HasMMapsForCurrentMap`**
A method implemented in this unit but declared in `Object.h` as part of the `WorldObject` class. It checks if the current map has loaded MMAP data. It queries the `MoveMap` manager for a valid nav mesh query. Note: While declared in `Object.h`, its implementation resides in `PathFinder.cpp`, making it part of this translation unit's behavior despite belonging to the `WorldObject` class interface.

### Geometry Helpers

**`CrossProduct`**, **`Distance`**, **`Distance2DPointToLineYZX`**
Static helper functions for 2D vector math, used primarily in path smoothing and simplification logic.

**`inRange`**, **`inRangeYZX`**, **`dist3DSqr`**
Static helper functions for distance comparisons. `inRangeYZX` handles the coordinate swap required by the Detour library (which uses YZX instead of XYZ).

## Cross-Unit Boundaries

### Collaboration with Movement Generators
The `PathFinder` is heavily integrated with various movement generators. These generators call `calculate` to get a path and then consume `m_pathPoints`.
-   **Called By:** `AiBotAI.Bridge/BridgeHandleMoveTo`, `AiBotAI.Movement/IsPathSafe`, `AiBotAI.Movement/MoveToDestination`, `ConfusedMovementGenerator/Update`, `FearMovementGenerator/_setTargetLocation`, `FleeingMovementGenerator/_setTargetLocation`, `HomeMovementGenerator/_setTargetLocation`, `PointMovementGenerator/ComputePath`, `PointMovementGenerator/Update#2`, `TargetedMovementGenerator/_setTargetLocation`, `WaypointMovementGenerator/StartMove`, `CyclicMovementGenerator/_setTargetLocation`.
-   **Direction:** Inbound calls to `calculate` and `setPathLengthLimit`. Outbound access to `m_pathPoints` (via getters not explicitly listed in the map but implied by usage).
-   **Why:** Movement generators need a sequence of waypoints to animate the unit's movement. The pathfinder provides these waypoints, respecting obstacles.

### Collaboration with Map & Terrain Systems
-   **Calls Out:** `MoveMap/createOrGetMMapManager`, `MoveMap/GetModelNavMeshQuery`, `MoveMap/GetNavMeshQuery`, `GridMap/IsSwimmable`, `GridMap/getLiquidStatus#2`, `Map.Main/FindCollisionModel`, `Map.Main/GetDynamicObjectHitPos`, `Map.Main/GetHeight`, `WorldObject.Object/GetTerrain`.
-   **Why:** The pathfinder relies on the `MoveMap` system for navigation mesh data and the `GridMap`/`Map` systems for terrain height, liquid status, and collision detection. This separation allows the pathfinder to remain agnostic of the underlying data storage format.

### Collaboration with Unit/Object State
-   **Calls Out:** `Unit.Main/CanFly`, `Unit.Main/CanSwim`, `Unit.Main/CanWalk`, `Unit.Main/HasUnitState`, `Unit.Main/GetObjectBoundingRadius`, `Unit.Main/CanReachWithMeleeAutoAttack`, `WorldObject.Object/GetSafePosition`, `WorldObject.Object/UpdateAllowedPositionZ`, `WorldObject.Object/UpdateGroundPositionZ`, `WorldObject.Object/GetMapId`, `WorldObject.Object/GetTransport`, `WorldObject.Object/IsWithinDist3d`, `WorldObject.Object/IsWithinLOS`, `WorldObject.Object/GetDistance#4`, `WorldObject.Object/GetPositionX/Y/Z`, `WorldObject.Object/GetName`, `Object/GetGUIDLow`, `Object/GetTypeId`, `Object/HasFlag`, `Object/IsCreature`, `Object/IsPlayer`, `GameObject/GetDisplayId`, `GenericTransport/CalculatePassengerOffset`, `Creature.Main/CanFly`.
-   **Why:** Path validity depends entirely on the unit's physical constraints (can it fly? swim?) and current state (is it stunned/ignoring pathfinding?). It also needs accurate positioning data to start the path.

### Collaboration with Debugging & Logging
-   **Calls Out:** `Log.Main/Out`, `Errors/PrintStacktraceAndThrow`.
-   **Called By:** `ChatHandler.DebugCommands/HandleMmapPathCommand`, `ChatHandler.DebugCommands/HandleMmapTestArea`, `ChatHandler.DebugCommands/HandleMmapLocCommand`.
-   **Why:** Developers use chat commands to test pathfinding visually. The pathfinder logs errors when path building fails (e.g., invalid polygons) to aid debugging.

## Data Model

This unit does not interact directly with any database tables. All navigation data is loaded from binary `.mmtile` files into memory via the `MoveMap` system. The `PathInfo` object holds transient state in memory only.

## Notable Implementation Details

1.  **Thread Safety of NavMeshQuery:**
    The `calculate` method explicitly notes that `dtNavMeshQuery` is not thread-safe. However, the code acquires a new query object from the `MMapManager` for each calculation. This suggests the `MMapManager` provides thread-local or per-request query objects to avoid race conditions.

2.  **Coordinate System Swap:**
    The Detour library uses a YZX coordinate system (Y=forward, Z=up, X=right), whereas the game engine uses XYZ (X=forward, Y=right, Z=up). The code frequently swaps coordinates when passing data to/from Detour functions (e.g., `startPoint[VERTEX_SIZE] = {startPos.y, startPos.z, startPos.x}`). This is a critical detail for anyone modifying the pathfinding logic.

3.  **Path Reuse Optimization:**
    `BuildPolyPath` implements a sophisticated path reuse strategy. Instead of recalculating the entire path when the target moves slightly, it checks if the start or end polygons are already in the previous path. If so, it reuses the overlapping segment and only calculates the missing suffix or prefix. This significantly reduces CPU load for chasing moving targets.

4.  **Fallback Mechanism:**
    If MMAP data is missing or invalid, the system gracefully degrades to `BuildPathWithoutMMaps`, which uses a simpler, less efficient recursive step-checking algorithm. This ensures units can still move on maps without navigation meshes, albeit with potential clipping issues.

5.  **Forced Destination:**
    The `m_forceDestination` flag allows callers to insist on reaching the exact requested coordinates, even if the pathfinder determines a shorter or safer alternative. This is used in `BuildPointPath` to adjust the final point, potentially marking the path as `PATHFIND_DEST_FORCED`.

6.  **Smoothing vs. Straight Path:**
    The `m_useStraightPath` flag toggles between `findSmoothPath` (custom implementation for natural curves) and Detour's `findStraightPath` (sharp corners). This allows different behaviors for different types of movement (e.g., NPCs might use smooth paths, while projectiles might use straight paths).

## Member Reference

**PathInfo** (ctor): Initializes the path finder for a `Unit`, setting up filters and clearing state.
**~PathInfo** (dtor): Destructor, no special cleanup.
**setPathLengthLimit**: Sets the maximum number of points in the smoothed path.
**calculate#2** (method): Entry point for path calculation using raw coordinates; delegates to `calculate(Vector3...)`.
**calculate** (method): Orchestrates path finding, acquiring nav mesh queries, checking tile availability, optimizing for stationary targets, and delegating to `BuildPolyPath`.
**FindWalkPoly**: Static helper to find the nearest navigation polygon to a point, ensuring it's not too high.
**getPolyByLocation**: Finds the nearest nav polygon to a coordinate, adjusting filters for target flags.
**BuildPolyPath**: Constructs the polygon path using Detour, handling path reuse, flying/swimming shortcuts, and invalid polygons.
**BuildPointPath**: Converts the polygon path into `Vector3` points, smoothing if necessary, and validating Z-heights.
**BuildShortcut**: Creates a direct two-point path for flying, swimming, or fallback cases.
**BuildUnderwaterPath**: Handles underwater movement, respecting water levels and liquid status.
**BuildPathStep**: Static recursive function for fallback pathfinding without MMAPs.
**BuildPathWithoutMMaps**: Fallback pathfinding using terrain height checks when MMAPs are unavailable.
**createFilter**: Configures the Detour query filter based on the unit's walk/swim capabilities.
**updateFilter**: Placeholder for dynamic filter updates.
**FillTargetAllowedFlags**: Configures the filter to allow terrain types suitable for the target unit.
**HaveTiles**: Checks if nav mesh tiles are loaded for the given coordinates.
**fixupCorridor**: Updates the path corridor when the agent moves, merging visited polygons.
**fixupShortcuts**: Static function to optimize the path by cutting corners.
**getSteerTarget**: Determines the next steering point along the path.
**CrossProduct**: Static helper for 2D vector cross product.
**Distance**: Static helper for 2D distance calculation.
**Distance2DPointToLineYZX**: Static helper for distance from a point to a line in 2D.
**findSmoothPath**: Generates a smoothed path by stepping along the polygon path, handling off-mesh connections.
**UpdateForCaster**: Truncates the path for ranged attackers to the first point with LoS and range.
**UpdateForMelee**: Truncates the path for melee attackers to the first point within melee reach.
**CutPathWithDynamicLoS**: Truncates the path if a dynamic object blocks LoS.
**Length**: Calculates the total Euclidean distance of the path.
**inRangeYZX**: Static helper for distance comparison in Detour's YZX coordinate system.
**inRange**: Static helper for distance comparison in XYZ coordinate system.
**dist3DSqr**: Static helper for squared 3D distance calculation.
**HasMMapsForCurrentMap**: Method implemented in this unit (declared in `Object.h` for `WorldObject`) to check if the current map has loaded MMAP data.

---

<!-- machine-true, projected from graph.json -->

## Map — WorldObject.PathFinder

*Source:* PathFinder.cpp, PathFinder.h, Object.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| PathInfo | ctor | — | AiBotAI.Bridge/BridgeHandleMoveTo, AiBotAI.Movement/IsPathSafe, AiBotAI.Movement/MoveToDestination, ChatHandler.DebugCommands/HandleMmapPathCommand, ChatHandler.DebugCommands/HandleMmapTestArea, CyclicMovementGenerator/_setTargetLocation, HomeMovementGenerator/_setTargetLocation, Spell.Main/CheckCast, WaypointMovementGenerator/StartMove, WorldObject.Object/MovePositionToFirstCollision | — |
| ~PathInfo | dtor | — | — | — |
| setPathLengthLimit | method | — | ConfusedMovementGenerator/Update, FearMovementGenerator/_setTargetLocation, FleeingMovementGenerator/_setTargetLocation | — |
| calculate#2 | method | WorldObject.Object/GetSafePosition | AiBotAI.Bridge/BridgeHandleMoveTo, AiBotAI.Movement/IsPathSafe, AiBotAI.Movement/MoveToDestination, ChatHandler.DebugCommands/HandleMmapPathCommand, ChatHandler.DebugCommands/HandleMmapTestArea, ConfusedMovementGenerator/Update, CyclicMovementGenerator/_setTargetLocation, FearMovementGenerator/_setTargetLocation, FleeingMovementGenerator/_setTargetLocation, HomeMovementGenerator/_setTargetLocation, PointMovementGenerator/ComputePath, PointMovementGenerator/Update#2, Spell.Main/CheckCast, TargetedMovementGenerator/_setTargetLocation, TargetedMovementGenerator/_setTargetLocation#2, WaypointMovementGenerator/StartMove | — |
| calculate | method | GameObject/GetDisplayId, GenericTransport/CalculatePassengerOffset, MoveMap/createOrGetMMapManager, MoveMap/GetModelNavMeshQuery, MoveMap/GetNavMeshQuery, PathInfo/getEndPosition#2, PathInfo/setEndPosition, PathInfo/setStartPosition, Unit.Main/GetObjectBoundingRadius, Unit.Main/HasUnitState, WorldObject.Object/GetMapId | WorldObject.Object/MovePositionToFirstCollision | — |
| FindWalkPoly | method | Errors/PrintStacktraceAndThrow | ChatHandler.DebugCommands/HandleMmapLocCommand, Map.Main/GetWalkHitPosition, Map.Main/GetWalkRandomPosition | — |
| getPolyByLocation | method | — | — | — |
| BuildPolyPath | method | Creature.Main/CanFly, Errors/PrintStacktraceAndThrow, GridMap/IsSwimmable, Log.Main/Out, Map.Main/FindCollisionModel, Object/GetGUIDLow, Object/GetTypeId, Object/HasFlag, Object/IsCreature, PathInfo/clear, PathInfo/setActualEndPosition, Unit.Main/CanFly, Unit.Main/CanSwim, WorldObject.Object/GetMap, WorldObject.Object/GetTerrain | — | — |
| BuildPointPath | method | PathInfo/getActualEndPosition#2, PathInfo/getEndPosition#2, PathInfo/getStartPosition#2, PathInfo/setActualEndPosition, Unit.Main/CanFly, WorldObject.Object/UpdateAllowedPositionZ | — | — |
| BuildShortcut | method | PathInfo/clear, PathInfo/getActualEndPosition#2, PathInfo/getStartPosition#2, Unit.Main/CanFly | — | — |
| BuildUnderwaterPath | method | GridMap/getLiquidStatus#2, PathInfo/clear, PathInfo/getActualEndPosition#2, PathInfo/getStartPosition#2, Unit.Main/CanFly, Unit.Main/CanWalk, WorldObject.Object/GetTerrain, WorldObject.Object/UpdateGroundPositionZ | — | — |
| BuildPathStep | function | Geometry/GetNearPoint2DAroundPosition, Geometry/NormalizeOrientation, Map.Main/GetHeight | — | — |
| BuildPathWithoutMMaps | method | PathInfo/clear, Unit.Main/CanFly, Unit.Main/CanSwim, Unit.Main/CanWalk, WorldObject.Object/FindMap | — | — |
| createFilter | method | Object/GetTypeId, Unit.Main/CanSwim, Unit.Main/CanWalk | — | — |
| updateFilter | method | — | — | — |
| FillTargetAllowedFlags | method | Object/IsPlayer, Unit.Main/CanSwim, Unit.Main/CanWalk | — | — |
| HaveTiles | method | — | — | — |
| fixupCorridor | method | — | — | — |
| fixupShortcuts | function | — | — | — |
| getSteerTarget | method | — | — | — |
| CrossProduct | function | — | — | — |
| Distance | function | — | — | — |
| Distance2DPointToLineYZX | function | — | — | — |
| findSmoothPath | method | Log.Main/Out, WorldObject.Object/GetName | — | — |
| UpdateForCaster | method | PathInfo/clear, PathInfo/getStartPosition#2, WorldObject.Object/GetDistance#4, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/IsWithinDist3d, WorldObject.Object/IsWithinLOS | TargetedMovementGenerator/_setTargetLocation | — |
| UpdateForMelee | method | PathInfo/clear, PathInfo/getStartPosition#2, Unit.Main/CanReachWithMeleeAutoAttack, WorldObject.Object/GetDistance#4, WorldObject.Object/GetPosition#2, WorldObject.Object/IsWithinDist3d | PointMovementGenerator/ComputePath | — |
| CutPathWithDynamicLoS | method | Map.Main/GetDynamicObjectHitPos, WorldObject.Object/GetMap | ConfusedMovementGenerator/Update, FearMovementGenerator/_setTargetLocation, FleeingMovementGenerator/_setTargetLocation, TargetedMovementGenerator/_setTargetLocation | — |
| Length | method | Errors/PrintStacktraceAndThrow | ChatHandler.DebugCommands/HandleMmapPathCommand, Spell.Main/CheckCast, TargetedMovementGenerator/_setTargetLocation, TargetedMovementGenerator/_setTargetLocation#2, WaypointMovementGenerator/StartMove | — |
| inRangeYZX | method | — | — | — |
| inRange | method | — | — | — |
| dist3DSqr | method | — | — | — |
| HasMMapsForCurrentMap | method | GameObject/GetDisplayId, MoveMap/createOrGetMMapManager, MoveMap/GetModelNavMeshQuery, MoveMap/GetNavMeshQuery, WorldObject.Object/GetMapId, WorldObject.Object/GetTransport | Unit.Main/GetRandomAttackPoint | — |

---

<!-- verify: boundary-bleed | foreign: object, WorldObject -->
