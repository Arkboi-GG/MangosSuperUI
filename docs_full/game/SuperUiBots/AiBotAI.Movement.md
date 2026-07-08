<!-- provenance: boundary-bleed -->
# AiBotAI.Movement

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# AiBotAI.Movement

**Purpose & Responsibilities**

This translation unit (`AiBotAIMovement.cpp`, declared in `AiBotAIMain.h`) implements the **navigation, pathfinding, and physical movement logic** for the autonomous `AiBotAI`. It is responsible for translating high-level destination coordinates into safe, executable motion sequences within the game world.

Key responsibilities include:
1.  **Path Generation & Validation:** Calculating paths using the server's MMAP (navigation mesh) system, validating that paths do not traverse dangerous zones (hostile creatures), and handling edge cases where standard pathfinding fails (e.g., navmesh seams, off-mesh starts).
2.  **Chunked Execution:** Breaking long paths into manageable ~200-yard segments to avoid spline interpolation errors and maintain responsive AI behavior.
3.  **Terrain Correction:** Detecting and correcting "floating" bot states caused by discrepancies between the navigation mesh (which often sits slightly above collision geometry) and the actual terrain height, preventing bots from getting stuck in the air or underground.
4.  **Path Smoothing:** Applying geometric fillets to sharp corners in generated paths to produce natural-looking movement, ensuring all smoothed points remain valid on the navmesh.
5.  **Idle Behavior:** Managing random wandering when the bot has no active tasks.

This unit does not handle combat logic, loot processing, or network communication directly, but it collaborates closely with `AiBotAI.Bridge` (for reporting movement failures/completions) and `AiBotAI.Combat` (for stopping movement during fights).

---

## Member-by-Member Behavior

### Idle & Basic Movement Control

**`StopMoving`**
Halts all current motion. It calls `Unit.Main/StopMoving`, clears the `MotionMaster` queue, and sets the bot to an idle state. This is the primary way to abort a movement sequence immediately.

**`DoRandomWander`**
Implements idle wandering behavior. It checks if the bot is already moving, in combat, or not in an idle motion state. If conditions are met and the wander timer has expired, it selects a random point within 15 yards of the current position and initiates movement via `MovePointRun`. The timer is reset to a random interval between 10–20 seconds.

**`MovePointRun`**
A wrapper for issuing simple point-to-point movement commands. It retrieves the bot's current run speed, clamping it to a maximum of 7.0 (to prevent unrealistic speeds for low-level bots), and issues a `MovePoint` command with pathfinding enabled. This ensures consistent speed handling across all simple movement actions (wander, stalemate nudges, etc.).

### Path Safety & Validation

**`IsPathSafe`**
Validates whether a proposed path to a destination is safe from hostile encounters.
1.  It generates a raw path using `PathInfo`. If no path exists (`PATHFIND_NOPATH`), it considers the move safe (as it won't happen anyway).
2.  It iterates through the path waypoints (sampling every 3rd point for performance) and checks for hostile creatures within a 30-yard radius.
3.  It filters out non-threatening entities: critters, service NPCs (vendors, trainers), invisible triggers, and creatures not hostile to the bot's faction.
4.  It flags the path as unsafe if any remaining creature has a level higher than the bot's level + 3.
5.  If unsafe, it outputs the location and level of the threat and returns `false`. Otherwise, it returns `true`.

### Navmesh & Terrain Correction

**`FindNearestNavmeshPoint`**
A convenience wrapper that delegates to `FindNearestNavmeshPointNear`, using the bot's current position as the query origin.

**`FindNearestNavmeshPointNear`**
Locates the nearest valid navigation mesh polygon to an arbitrary coordinate.
1.  It accesses the MMAP manager and creates a navmesh query for the bot's map.
2.  It converts MaNGOS coordinates (X,Y,Z) to Detour coordinates (Y,Z,X) and searches for the nearest polygon within a specified radius.
3.  **Critical Correction:** It performs a "ground snap" check. Because navmesh polygons can float above the actual collision hull on slopes, it uses `WorldObject.Object/UpdateAllowedPositionZ` to find the true ground height. If the navmesh Z is significantly higher than the ground Z, it snaps the output Z down to the ground level to prevent the bot from being placed in mid-air.

**`ReGroundZ`**
Explicitly corrects the Z-coordinate of a destination to ensure it rests on solid terrain.
1.  It calls `WorldObject.Object/UpdateAllowedPositionZ` to determine the true ground height at the given X,Y.
2.  If the requested Z is significantly higher than the ground (more than 0.5 yards), it lowers Z to the ground level ("snap down").
3.  If the ground is significantly higher than the requested Z (e.g., inside a cave or under a bridge), it **keeps** the original Z to avoid pushing the bot into ceilings or upper floors. This prevents "float-maroon" states where bots hover and fail to pathfind.

### Path Generation & Execution

**`MoveToDestination`**
The core method for navigating to a target coordinate. It handles complex failure modes and recovery strategies:
1.  **Combat Check:** If the bot is in combat, it arms the task for later execution and returns immediately.
2.  **Journey Seeding:** Generates a unique seed for the current journey if the destination has changed significantly, used for deterministic path smoothing.
3.  **Off-Mesh Recovery:** Checks if the bot is currently standing off the navmesh. If so, it snaps the bot to the nearest valid navmesh point and re-calls itself recursively. If no navmesh is nearby, it teleports the bot to its spawn point (if on the same map) and retries.
4.  **Safety Check:** Calls `IsPathSafe`. If the path is unsafe, it sends a `PATH_UNSAFE` event to the bridge and aborts.
5.  **Path Calculation:** Attempts to calculate a path.
6.  **Seam Crossing:** If the destination is very close (<12 yards) but no path exists, it assumes a navmesh seam (e.g., cave entrance). It records the boundary, teleports the bot across the seam, and marks the task complete.
7.  **Retry Strategies:** If no path exists, it tries:
    *   **Nudge:** Moving the destination 3 yards closer to the bot.
    *   **Ring Scan:** Testing 8 points in a circle around the original destination.
8.  **Boundary Escape:** If still no path, it looks for a previously recorded navmesh boundary (seam) nearby and teleports to the "outer" anchor point to escape a trapped area.
9.  **Failure Reporting:** If all retries fail, it sends a `MOVE_FAILED` event to the bridge.
10. **Execution:** If a valid path is found:
    *   It applies corner smoothing via `SmoothPathCorners`.
    *   For short paths (<200 yards), it issues a single smoothed movement command.
    *   For long paths, it stores the waypoints and initiates chunked movement via `StartNextPathChunk`.

**`StartNextPathChunk`**
Executes the next segment of a stored long-distance path.
1.  It calculates the distance between consecutive waypoints in `m_pathWaypoints` starting from `m_pathIndex`.
2.  It accumulates waypoints until the total distance exceeds `AIBOT_PATH_CHUNK_DIST` (200 yards) or the path ends.
3.  It clamps the movement speed to 7.0 if necessary.
4.  It issues the chunk as a smoothed path via `AiBotMovementGenerators/IssueSmoothedPath`.
5.  It updates `m_pathIndex` to the end of the processed chunk.

**`ClearStoredPath`**
Resets the chunked pathing state by clearing the waypoint vector and resetting the index. Called when a new journey begins or when movement is aborted.

### Path Smoothing

**`SmoothPathCorners`**
Improves the visual quality of movement by rounding sharp corners in the raw path.
1.  It analyzes turn angles at each vertex.
2.  It merges consecutive turns in the same direction into "corner runs".
3.  For significant corners, it identifies the "peak" vertex (furthest from the straight line between entry and exit points).
4.  It calls `AiBotPathSmoothing/ComputeCornerFillet` to generate a smooth curve.
5.  **Validation:** Every point in the smoothed curve is validated against the navmesh using `FindNearestNavmeshPointNear`. If any point is invalid (off-mesh), the entire fillet is rejected, and the raw points are used instead. This prevents the bot from clipping through walls.

### Navmesh Boundary Learning

**`RecordNavBoundary`**
Stores information about a navmesh discontinuity (seam) that the bot successfully crossed. It records the "inner" point (destination) and "outer" point (current position) along with the map ID. This allows the bot to remember how to exit similar areas in the future.

**`FindNavBoundaryNear`**
Searches the list of recorded boundaries for one near the given coordinates. Returns a pointer to the closest boundary within the specified scope, used by `MoveToDestination` to escape trapped areas.

---

## Cross-Unit Boundaries

### Calls Out (Dependencies)

*   **`Creature.MotionMaster` / `Unit.Main`**: Used extensively to control the bot's physical movement (`MoveIdle`, `Clear`, `MovePoint`, `StopMoving`, `GetMotionMaster`). These are the core engine interfaces for character animation and positioning.
*   **`WorldObject.PathFinder` / `PathInfo`**: Used to calculate paths (`calculate`, `getPath`, `getPathType`). This is the primary interface to the MMAP navigation system.
*   **`MoveMap` / `MMAP::MMapManager`**: Used in `FindNearestNavmeshPointNear` to access the raw navmesh data for proximity queries and ground snapping.
*   **`ObjectMgr`**: Used in `IsPathSafe` to retrieve creature templates and faction data to determine if a path passes through hostile territory.
*   **`AiBotMovementGenerators/IssueSmoothedPath`**: Called by `MoveToDestination` and `StartNextPathChunk` to execute the final movement command with smoothed waypoints. This unit provides the specialized spline generation logic.
*   **`AiBotPathSmoothing/ComputeCornerFillet`**: Called by `SmoothPathCorners` to perform the geometric calculations for rounding corners.
*   **`AiBotAI.Bridge/BridgeSendEvent`**: Called by `MoveToDestination` to report success (`TASK_COMPLETE`) or failure (`MOVE_FAILED`, `PATH_UNSAFE`) to the external C# controller.
*   **`AiBotAI.Grind/ConvertMoveToGrindInPlace`**: Called by `MoveToDestination` when a seam is detected near an objective. It converts the movement task into a stationary grind task at the cave mouth.
*   **`shared_Util/urand`**: Used for randomizing wander timers and journey seeds.
*   **`Log.Main/Out`**: Used throughout for diagnostic logging of pathing decisions, failures, and corrections.

### Called By (Integration Points)

*   **`AiBotAI.Main/UpdateAI`**: The main game loop calls `DoRandomWander` for idle behavior and `StartNextPathChunk` to continue long journeys. It also calls `ClearStoredPath` when tasks change.
*   **`AiBotAI.Bridge`**: Various bridge handlers (`BridgeHandleMoveTo`, `BridgeHandleTeleport`, `BridgeHandleInteractNpc`, etc.) call `MoveToDestination`, `StopMoving`, `ReGroundZ`, and `MovePointRun` to execute commands received from the C# controller.
*   **`AiBotAI.Combat`**: Combat logic calls `StopMoving` to halt movement during fights, `ReGroundZ` to correct position after teleporting to a target, and `MovePointRun` for tactical maneuvers like stalemate nudges or overpull retreats. `CheckForUnreachableTarget` also calls `ReGroundZ`.
*   **`AiBotAI.Grind/DoGrindPatrol`**: Calls `MovePointRun` to execute patrol movements during grinding.

---

## Data Model

This unit does not directly interact with any database tables. It operates entirely on in-memory data structures:
*   **`m_pathWaypoints`**: A `std::vector<Vector3>` storing the current long-distance path.
*   **`m_navBoundaries`**: A `std::vector<NavBoundary>` storing learned navmesh seams for the current session.
*   **`m_currentTask`**: A struct holding the current objective coordinates and type.

All pathing data is derived from the server's MMAP files (binary navigation meshes) and the live object manager's creature data.

---

## Notable Implementation Details

### Float-Maroon Fix (`ReGroundZ`, `FindNearestNavmeshPointNear`)
A critical issue addressed in this unit is the discrepancy between the navmesh (used for pathfinding) and the collision hull (used for physics). Navmesh polygons often sit slightly above the actual ground on slopes. If a bot is teleported to a navmesh Z-coordinate, it may end up hovering in the air. From this elevated position, the pathfinder may fail to find a path back to the ground (because the start point is off-mesh), causing the bot to become permanently stuck ("marooned").
*   **Solution:** Every teleport destination and navmesh query result is passed through `ReGroundZ` or the ground-snap logic in `FindNearestNavmeshPointNear`. This uses `UpdateAllowedPositionZ` to find the true ground height and snaps the Z-coordinate **down** if necessary. Crucially, it never snaps **up**, preventing bots from being pushed into ceilings or upper floors in complex indoor environments.

### Deterministic Path Smoothing (`SmoothPathCorners`, `m_pathJourneySeed`)
Path smoothing uses a random seed (`m_pathJourneySeed`) to vary the exact shape of fillets slightly, making bot movement look less robotic. However, this seed is only regenerated when the destination changes significantly (`AIBOT_JOURNEY_DEST_EPSILON`). This ensures that if the bot retries a path due to a temporary obstruction or recalculates due to a minor position shift, it follows the same smoothed curve, preventing jittery or inconsistent movement during recovery attempts.

### Navmesh Seam Learning (`RecordNavBoundary`, `FindNavBoundaryNear`)
Some areas (like cave entrances) have disconnected navmesh islands. Standard pathfinding fails here. The bot learns these seams dynamically:
1.  When `MoveToDestination` detects a destination is close but unreachable (NOPATH), it assumes a seam.
2.  It teleports the bot across the gap.
3.  It records the "inner" (destination) and "outer" (start) points in `m_navBoundaries`.
4.  Later, if the bot is stuck inside such an area and cannot path out, `MoveToDestination` searches for a recorded boundary nearby and teleports the bot to the "outer" anchor, effectively reversing the seam crossing.

### Chunked Pathing (`StartNextPathChunk`)
Long paths are broken into ~200-yard chunks. This is necessary because:
1.  The `MotionMaster` spline interpolation can become inaccurate or unstable over very long distances.
2.  It allows the AI to react to interruptions (combat, new tasks) more frequently, as it only commits to a short segment at a time.
3.  It avoids the overhead of recalculating the entire path if the bot deviates slightly.

### Speed Clamping (`MovePointRun`, `StartNextPathChunk`)
The bot's run speed is clamped to a maximum of 7.0 yards/second. This prevents low-level bots (who might have speed buffs or anomalies) from moving unrealistically fast, which could cause them to clip through geometry or appear to glide.

### Safety Validation (`IsPathSafe`)
Before committing to a path, the bot scans for hostile creatures. This prevents the bot from walking directly into a group of higher-level enemies. It filters out non-threatening NPCs and critters, focusing only on potential combat threats. This is a proactive avoidance strategy, distinct from reactive combat logic.

---

## Member Reference

**`StopMoving`**
Halts all motion, clears the motion master, and sets the bot to idle. Called by combat logic, bridge handlers, and movement recovery routines to abort current movement.

**`DoRandomWander`**
Implements idle wandering. Picks a random point within 15 yards and moves there if the bot is idle, not in combat, and the wander timer has expired. Called by `AiBotAI.Main/UpdateAI`.

**`IsPathSafe`**
Validates a path by checking for hostile creatures within 30 yards of the waypoints. Filters out non-hostile, low-level, or service NPCs. Returns `false` if a threat is found, providing the unsafe coordinates and danger level. Called by `MoveToDestination`.

**`ClearStoredPath`**
Resets the chunked pathing state (`m_pathWaypoints`, `m_pathIndex`). Called by `MoveToDestination` when starting a new journey, and by `AiBotAI.Main/MovementInform` and `AiBotAI.Main/UpdateAI` when paths are completed or aborted.

**`StartNextPathChunk`**
Executes the next ~200-yard segment of a stored long-distance path. Calculates the chunk, clamps speed, and issues a smoothed movement command. Called by `MoveToDestination` for long paths and `AiBotAI.Main/UpdateAI` to continue progress.

**`MoveToDestination`**
Core navigation method. Handles combat deferral, off-mesh recovery, safety checks, path calculation, seam crossing, retry strategies (nudge, ring scan), boundary escape, and path execution (short vs. chunked). Called by `AiBotAI.Bridge/BridgeHandleMoveTo`, `AiBotAI.Main/MovementInform`, and `AiBotAI.Main/UpdateAI`.

**`FindNearestNavmeshPointNear`**
Finds the nearest navmesh polygon to an arbitrary point, correcting for Z-height discrepancies (float-maroon fix). Called by `SmoothPathCorners` for validation and internally by `FindNearestNavmeshPoint`.

**`FindNearestNavmeshPoint`**
Wrapper for `FindNearestNavmeshPointNear` using the bot's current position. Called by `MoveToDestination` for off-mesh recovery.

**`ReGroundZ`**
Snaps a Z-coordinate down to the true ground level to prevent floating bots. Called by `MoveToDestination`, `FindNearestNavmeshPointNear`, `SmoothPathCorners`, and various bridge/combat handlers before teleporting.

**`MovePointRun`**
Issues a simple point-to-point movement command with speed clamping. Called by `DoRandomWander`, `AiBotAI.Bridge/BridgeHandleInteractNpc`, `AiBotAI.Bridge/BridgeHandleSetTask`, `AiBotAI.Combat/HandleCombatStalemate`, `AiBotAI.Combat/HandleOverpullRetreat`, and `AiBotAI.Grind/DoGrindPatrol`.

**`RecordNavBoundary`**
Stores a learned navmesh seam (inner/outer points) for future escape use. Called by `MoveToDestination` when crossing a seam.

**`FindNavBoundaryNear`**
Finds a recorded navmesh boundary near a given point. Called by `MoveToDestination` for outbound seam escape and `AiBotAI.Combat/HandleCombatStalemate`.

**`SmoothPathCorners`**
Applies geometric fillets to sharp corners in a path, validating all smoothed points against the navmesh. Called by `MoveToDestination` before executing any path.

---

<!-- machine-true, projected from graph.json -->

## Map — AiBotAI.Movement

*Source:* AiBotAIMovement.cpp, AiBotAIMain.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| StopMoving | method | Creature.MotionMaster/MoveIdle, MotionMaster/Clear, Unit.Main/GetMotionMaster, Unit.Main/StopMoving | AiBotAI.Bridge/BridgeHandleInteractNpc, AiBotAI.Bridge/BridgeHandleSetTask, AiBotAI.Bridge/BridgeHandleTakeFlight, AiBotAI.Bridge/BridgeHandleTeleport, AiBotAI.Bridge/BridgeHandleTrain, AiBotAI.Combat/CheckForUnreachableTarget, AiBotAI.Combat/DrinkAndEat, AiBotAI.Combat/HandleCombatStalemate, AiBotAI.Combat/HandleOverpullRetreat, AiBotAI.Main/UpdateAI | — |
| DoRandomWander | method | Creature.MotionMaster/GetCurrentMovementGeneratorType, shared_Util/urand, Unit.Main/GetMotionMaster, Unit.Main/IsInCombat, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/GetRandomPoint, WorldObject.Object/IsMoving | AiBotAI.Main/UpdateAI | — |
| IsPathSafe | method | FactionTemplateEntry/IsHostileTo, Log.Main/Out, Object/IsInWorld, ObjectMgr/GetCreatureDataMap, ObjectMgr/GetCreatureTemplate, ObjectMgr/GetFactionTemplateEntry, PathInfo/getPath, PathInfo/getPathType, Player.Main/GetName, Unit.Main/GetLevel, WorldObject.Object/GetFactionTemplateEntry, WorldObject.Object/GetMap, WorldObject.Object/GetMapId, WorldObject.PathFinder/calculate#2, WorldObject.PathFinder/PathInfo | — | — |
| ClearStoredPath | method | — | AiBotAI.Bridge/BridgeHandleTeleport, AiBotAI.Grind/ConvertMoveToGrindInPlace, AiBotAI.Main/MovementInform, AiBotAI.Main/UpdateAI | — |
| StartNextPathChunk | method | AiBotMovementGenerators/IssueSmoothedPath, Log.Main/Out, Player.Main/GetName, Unit.Main/GetSpeed, Unit.Main/GetSpeedRate | AiBotAI.Main/MovementInform, AiBotAI.Main/UpdateAI | — |
| MoveToDestination | method | AiBotAI.Bridge/BridgeSendEvent, AiBotAI.Grind/ConvertMoveToGrindInPlace, AiBotMovementGenerators/IssueSmoothedPath, AiBotTaskData/Clear, Log.Main/Out, PathInfo/getPath, PathInfo/getPathType, Player.Main/GetName, shared_Util/urand, Unit.Main/GetLevel, Unit.Main/GetSpeed, Unit.Main/IsInCombat, Unit.Main/NearTeleportTo, WorldObject.Object/GetDistance2d#4, WorldObject.Object/GetMapId, WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.PathFinder/calculate#2, WorldObject.PathFinder/PathInfo | AiBotAI.Bridge/BridgeHandleMoveTo, AiBotAI.Main/MovementInform, AiBotAI.Main/UpdateAI | — |
| FindNearestNavmeshPointNear | method | Log.Main/Out, MoveMap/createOrGetMMapManager, MoveMap/GetNavMeshQuery, Player.Main/GetName, WorldObject.Object/GetMapId, WorldObject.Object/UpdateAllowedPositionZ | — | — |
| FindNearestNavmeshPoint | method | WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ | — | — |
| ReGroundZ | method | Log.Main/Out, Player.Main/GetName, WorldObject.Object/GetMap, WorldObject.Object/UpdateAllowedPositionZ | AiBotAI.Bridge/BridgeHandleMoveTo, AiBotAI.Bridge/BridgeHandleResurrect, AiBotAI.Bridge/BridgeHandleTeleport, AiBotAI.Combat/CheckForUnreachableTarget, AiBotAI.Combat/HandleCombatStalemate | — |
| MovePointRun | method | Creature.MotionMaster/MovePoint, Unit.Main/GetMotionMaster, Unit.Main/GetSpeed | AiBotAI.Bridge/BridgeHandleInteractNpc, AiBotAI.Bridge/BridgeHandleSetTask, AiBotAI.Combat/HandleCombatStalemate, AiBotAI.Combat/HandleOverpullRetreat, AiBotAI.Grind/DoGrindPatrol | — |
| RecordNavBoundary | method | Log.Main/Out, Player.Main/GetName, WorldObject.Object/GetMapId, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ | — | — |
| FindNavBoundaryNear | method | WorldObject.Object/GetMapId | AiBotAI.Combat/HandleCombatStalemate | — |
| SmoothPathCorners | method | AiBotPathSmoothing/ComputeCornerFillet, Log.Main/Out, Player.Main/GetName | — | — |

---

<!-- verify: boundary-bleed | foreign: AiBotAI -->
