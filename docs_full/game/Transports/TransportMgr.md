<!-- provenance: failed-members -->
# TransportMgr

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# TransportMgr

**TransportMgr** is the singleton manager responsible for loading, configuring, and spawning **transports**—large moving objects such as ships, zeppelins, and elevators—in the world. It bridges static configuration data (DBC files and the `transports` database table) with dynamic runtime entities (`ShipTransport` objects on `Map` instances).

Its primary responsibilities are:
1.  **Path Generation:** Converting raw taxi path nodes into smooth, timed splines with acceleration/deceleration profiles and orientation data.
2.  **Animation Loading:** Loading discrete animation states from DBC stores for visual effects.
3.  **Spawning Logic:** Determining which maps require specific transports and instantiating them at the correct starting waypoints, handling both continent-wide and instance-specific transports.
4.  **Elevator Tracking:** Maintaining a registry of elevator-type game objects per map for efficient lookup.

## Member-by-Member Behavior

### Initialization and Loading

**LoadTransportAnimationAndRotation**  
Iterates through the global `sTransportAnimationStore` (a DBC store) and populates the internal `m_transportAnimations` map. For each animation entry, it calls `AddPathNodeToTransport` to associate the animation node with a specific time segment of the transport's cycle. This method is called during world startup by `World/SetInitialWorldSettings`.

**LoadTransportTemplates**  
This is the core initialization routine for transport paths. It performs two distinct phases:
1.  **Path Construction:** It iterates over all `GameObjectInfo` entries in `ObjectMgr/GetGameObjectInfoMap`. For any object of type `GAMEOBJECT_TYPE_MO_TRANSPORT`, it invokes `GenerateWaypoints` to build the movement spline and timing data. If generation fails, the template is discarded.
2.  **Period Override:** It queries the `transports` table in the World Database to retrieve manually configured periods. The query selects the `period` for the highest `build` number less than or equal to the current `SUPPORTED_CLIENT_BUILD`. If a valid period is found, it overrides the automatically calculated `pathTime` and sets the final keyframe's departure time. This allows DB administrators to fine-tune loop durations that the algorithmic calculation might get slightly wrong. If an entry in the DB does not correspond to a loaded template, it logs an error via `Log.Main/Out`.

**GenerateWaypoints**  
A complex private method that constructs the `TransportTemplate`'s `keyFrames` vector from a `TaxiPathNodeList`.
*   **Spline Initialization:** It gathers all 3D coordinates from the taxi path and adds extrapolated points at the beginning and end to allow for Catmull-Rom spline derivative calculations (needed for orientation).
*   **KeyFrame Creation:** It iterates through the path nodes. If a node indicates a map change or a teleport action flag, it marks the previous keyframe as a `Teleport`. Otherwise, it calculates the initial orientation using the spline's derivative at that point, normalized via `Geometry/NormalizeOrientation`.
*   **Map Validation:** It tracks which maps are used. If multiple maps are involved, it asserts that none are instanceable (since cross-map transports generally don't work across instances). It sets `inInstance` based on whether the single map used is instanceable.
*   **Distance and Timing Calculation:**
    *   It creates `TransportSpline` objects for segments between teleports.
    *   It calculates distances between keyframes (`DistFromPrev`).
    *   It identifies "stop frames" (where `actionFlag == 2`) and calculates `DistSinceStop` and `DistUntilStop` for each frame.
    *   It computes travel times (`TimeTo`) for each segment, accounting for acceleration (`accelRate`) and maximum speed (`moveSpeed`). It handles edge cases where the distance is too short to reach full speed, requiring pure acceleration/deceleration math.
    *   It calculates absolute timestamps (`ArriveTime`, `DepartureTime`) in milliseconds.
*   **Special Cases:** It explicitly flags `Update = true` for specific path IDs (303 and 293, corresponding to Feathermoon and Teldrassil ferries) at index 12, likely to force a client-side refresh or despawn/re-spawn logic mid-route.

**AddPathNodeToTransport**  
A helper used by `LoadTransportAnimationAndRotation`. It inserts a `TransportAnimationEntry` into the `Path` map of a `TransportAnimation` struct, keyed by `timeSeg`. It also updates the `TotalTime` if the new segment exceeds the current maximum.

### Runtime Access and Spawning

**GetTransportTemplate**  
Returns a pointer to a `TransportTemplate` from the `m_transportTemplates` unordered map by entry ID. Returns `nullptr` if not found.

**GetTransportAnimInfo**  
Returns a pointer to the `TransportAnimation` struct for a given entry ID from `m_transportAnimations`. Used by `Transport/Create` to access visual animation data.

**CreateTransport**  
Instantiates a `ShipTransport` object on a specific `Map`.
1.  **Validation:** It retrieves the template and finds the first keyframe matching the target map's ID.
2.  **Instance Check:** It verifies that the transport is being created on the correct continent instance ID (using `MapManager/GetContinentInstanceId`). If the map is a continent and the instance IDs mismatch, it returns `nullptr`. It also checks if the map's instanceability matches the template's `inInstance` flag.
3.  **Creation:** It constructs a `ShipTransport` using `Transport/Create#2`. If successful, it sets the location instance ID, assigns the map via `WorldObject.Object/SetMap`, and adds the transport to the map's object list via `Map.Main/Add#6`.
4.  **Logging:** Logs the successful creation.

**SpawnTransportsOnMap**  
Called by `Map.Main/Map` when a map is loaded. It iterates through all loaded templates.
*   It skips continent transports that have already been spawned (`spawned == true` and `!inInstance`) to prevent duplicates.
*   If the map ID is in the template's `mapsUsed` set, it calls `CreateTransport`.
*   On success, it marks the template as `spawned`.

### Elevator Management

**AddElevatorTransportForMap**  
Registers an elevator transport (identified by `guidLow`) for a specific `mapId` in the `m_elevatorTransportsByMap` multimap. It first checks for duplicates to avoid redundant entries. Called by `ObjectMgr/LoadGameobjects`.

**GetElevatorTransportsForMap**  
Returns an iterator range (`equal_range`) of all elevator transports registered for a given `mapId`. Called by `Map.Main/LoadElevatorTransports`.

### Animation Node Lookup

**GetPrevAnimNode** & **GetNextAnimNode**  
Member functions of the `TransportAnimation` struct. They use `std::map::lower_bound` to find the animation node immediately preceding or succeeding a given `time` value. These are called by `Transport/Update` to interpolate visual animations based on the current time in the transport's cycle.

## Cross-Unit Boundaries

*   **World/SetInitialWorldSettings:** Calls `LoadTransportAnimationAndRotation` and `LoadTransportTemplates` during server startup to initialize all transport data before players can log in.
*   **ObjectMgr/GetGameObjectInfoMap:** `LoadTransportTemplates` reads this to identify which Game Objects are transports.
*   **Database/PQuery:** `LoadTransportTemplates` executes a SQL query against the `transports` table to fetch period overrides.
*   **Log.Main/Out:** Used by `LoadTransportTemplates` to report invalid DB entries and by `CreateTransport` to log errors (wrong instance) and details (successful creation).
*   **Geometry/NormalizeOrientation:** Called by `GenerateWaypoints` to ensure calculated orientations are within standard bounds.
*   **Map.Main/GetId, Map.Main/GetInstanceId, Map.Main/IsContinent:** Used by `CreateTransport` to validate the target map context.
*   **MapManager/GetContinentInstanceId:** Used by `CreateTransport` to ensure continent transports spawn on the correct shard/instance.
*   **Transport/Create#2, Transport/ShipTransport:** `CreateTransport` constructs the actual runtime entity.
*   **WorldObject.Object/SetLocationInstanceId, WorldObject.Object/SetMap, WorldObject.Object/GetMap:** Used by `CreateTransport` to integrate the new transport into the world simulation.
*   **Map.Main/Add#6:** Adds the transport to the map's active object list.
*   **Map.Main/Map:** Calls `SpawnTransportsOnMap` when a map is initialized.
*   **ObjectMgr/LoadGameobjects:** Calls `AddElevatorTransportForMap` to register elevators.
*   **Map.Main/LoadElevatorTransports:** Calls `GetElevatorTransportsForMap` to retrieve elevator lists.
*   **Transport/Update:** Calls `GetPrevAnimNode` and `GetNextAnimNode` to update visual animations.
*   **Transport/Create:** Calls `GetTransportAnimInfo` to retrieve animation data.

## Data Model

The unit interacts with one database table:

**`transports`**
*   **Usage:** Stores manual overrides for transport loop periods.
*   **Columns Accessed:**
    *   `entry` (mediumint, PK): Matches the Game Object entry ID.
    *   `build` (smallint, PK): Allows version-specific configurations. The code selects the row with the maximum `build` number less than or equal to the current client build.
    *   `period` (mediumint): The duration of the transport loop in milliseconds. If non-zero, this overrides the algorithmically calculated path time.

## Notable Implementation Details

1.  **Algorithmic vs. Manual Periods:** The system first calculates the total path time based on physics (speed, acceleration, distance). However, because this calculation can be imperfect, `LoadTransportTemplates` allows DB admins to override the final `pathTime` via the `transports` table. This hybrid approach ensures accuracy while allowing manual tuning.
2.  **Spline Orientation:** Orientation is not stored directly in the taxi path nodes. Instead, `GenerateWaypoints` constructs a Catmull-Rom spline from the position points and calculates the tangent (derivative) at each node to determine the facing direction. This ensures smooth rotation along curves.
3.  **Teleport Handling:** Transports can "teleport" between distant points (e.g., from one side of the world to another). `GenerateWaypoints` detects these via `actionFlag` or map changes and breaks the spline into separate segments. Distance calculations reset at teleports.
4.  **Acceleration Profiles:** The timing calculation in `GenerateWaypoints` is sophisticated. It doesn't just divide distance by speed. It accounts for acceleration ramps. If a segment is too short to reach top speed, it calculates the time purely based on acceleration/deceleration curves. This prevents transports from snapping instantly to max speed.
5.  **Hardcoded Refresh Flags:** The code contains a hardcoded check for `pathId == 303 || pathId == 293` to set `Update = true` on a specific keyframe. This suggests a known client-side bug or requirement for these specific ferries (Feathermoon and Teldrassil) where the client needs a forced update mid-route to maintain synchronization or visibility.
6.  **Instance Safety:** `CreateTransport` rigorously checks instance IDs. Continent transports must match the continent's instance ID, and instance transports must only spawn on instance maps. This prevents transports from appearing in the wrong dungeon or raid instance.
7.  **Singleton Pattern:** `TransportMgr` is instantiated as a singleton (`INSTANTIATE_SINGLETON_1`), ensuring a single global source of truth for transport templates and animations.

## Member Reference

**LoadTransportAnimationAndRotation**  
Iterates the DBC `TransportAnimationStore` and populates `m_transportAnimations` by calling `AddPathNodeToTransport`. Called by `World/SetInitialWorldSettings`.

**GetTransportTemplate**  
Returns a pointer to a `TransportTemplate` from `m_transportTemplates` by entry ID, or `nullptr` if not found.

**LoadTransportTemplates**  
Loads transport paths from `GameObjectInfo` via `GenerateWaypoints`. Then queries the `transports` table to override calculated periods with DB values. Logs errors for invalid DB entries. Called by `World/SetInitialWorldSettings`.

**SplineRawInitializer**  
Constructor for the local helper class used in `GenerateWaypoints` to initialize spline parameters.

**operator()**  
Method of `SplineRawInitializer` that sets spline mode to Catmull-Rom, copies points, and sets loop bounds.

**GenerateWaypoints**  
Private method that converts a `TaxiPathNodeList` into a `TransportTemplate`. Calculates splines, orientations, distances, and timings including acceleration/deceleration. Handles teleports and map changes. Sets special `Update` flags for specific ferry paths.

**GetTransportAnimInfo**  
Returns a pointer to the `TransportAnimation` struct for a given entry from `m_transportAnimations`. Called by `Transport/Create`.

**GetElevatorTransportsForMap**  
Returns an iterator range of elevator transports for a given map ID from `m_elevatorTransportsByMap`. Called by `Map.Main/LoadElevatorTransports`.

**AddElevatorTransportForMap**  
Adds an elevator transport GUID to `m_elevatorTransportsByMap` for a given map ID, avoiding duplicates. Called by `ObjectMgr/LoadGameobjects`.

**AddPathNodeToTransport**  
Private helper that inserts an animation node into `m_transportAnimations` at a specific time segment.

**GetPrevAnimNode**  
Member of `TransportAnimation`. Finds the animation node immediately preceding a given time using `lower_bound`. Called by `Transport/Update`.

**GetNextAnimNode**  
Member of `TransportAnimation`. Finds the animation node immediately following a given time using `lower_bound`. Called by `Transport/Update`.

**CreateTransport**  
Validates map context (instance ID, instanceability), constructs a `ShipTransport` from a template, sets its location/map, and adds it to the map. Returns `nullptr` on validation failure.

**SpawnTransportsOnMap**  
Iterates loaded templates and spawns those relevant to the given map via `CreateTransport`, marking them as spawned to prevent duplicates. Called by `Map.Main/Map`.

---

<!-- machine-true, projected from graph.json -->

## Map — TransportMgr

*Source:* TransportMgr.cpp, TransportMgr.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| LoadTransportAnimationAndRotation | method | — | World/SetInitialWorldSettings | — |
| GetTransportTemplate | method | — | — | — |
| LoadTransportTemplates | method | Database/PQuery, Field/GetUInt32, Log.Main/Out, ObjectMgr/GetGameObjectInfoMap, QueryResult/Fetch, QueryResult/NextRow | World/SetInitialWorldSettings | transports |
| SplineRawInitializer | ctor | — | — | — |
| operator() | method | — | — | — |
| GenerateWaypoints | method | Errors/PrintStacktraceAndThrow, Geometry/NormalizeOrientation, KeyFrame/IsStopFrame, KeyFrame/KeyFrame, MapEntry/Instanceable | — | — |
| GetTransportAnimInfo | method | — | Transport/Create | — |
| GetElevatorTransportsForMap | method | — | Map.Main/LoadElevatorTransports | — |
| AddElevatorTransportForMap | method | — | ObjectMgr/LoadGameobjects | — |
| AddPathNodeToTransport | method | — | — | — |
| GetPrevAnimNode | method | — | Transport/Update | — |
| GetNextAnimNode | method | — | Transport/Update | — |
| CreateTransport | method | Errors/PrintStacktraceAndThrow, Log.Main/Out, Map.Main/Add#6, Map.Main/GetId, Map.Main/GetInstanceId, Map.Main/Instanceable, Map.Main/IsContinent, MapManager/GetContinentInstanceId, Transport/Create#2, Transport/ShipTransport, WorldObject.Object/GetMap, WorldObject.Object/SetLocationInstanceId, WorldObject.Object/SetMap | — | — |
| SpawnTransportsOnMap | method | Map.Main/GetId | Map.Main/Map | — |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `transports`: entry mediumint(8) unsigned PK, build smallint(5) unsigned PK, name text?, period mediumint(8) unsigned

*`?` = nullable, `PK` = primary key column.*


---

<!-- verify: failed-members | invented: operator -->
