# WaypointManager

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# WaypointManager

**Purpose & Responsibilities**

`WaypointManager` is a singleton service responsible for loading, validating, caching, and providing access to creature movement paths (waypoints) in the game world. It acts as the bridge between the database tables storing waypoint definitions and the runtime movement generators that execute them.

Its core responsibilities include:
1.  **Loading:** Reading waypoint data from three distinct database tables (`creature_movement`, `creature_movement_template`, `creature_movement_special`) during server startup.
2.  **Validation & Correction:** Verifying that waypoints reference valid creatures, exist within valid map coordinates, and link to existing scripts. It automatically corrects invalid coordinates by snapping them to the terrain and updates the database to reflect these corrections. It also renumbers out-of-order waypoint points in the database.
3.  **Caching:** Storing parsed waypoint paths in memory (`std::unordered_map`) for fast retrieval by movement generators.
4.  **Runtime Modification:** Providing an API for Game Master (GM) chat commands to add, delete, or modify waypoints dynamically, updating both the in-memory cache and the database.

## Data Model

`WaypointManager` interacts with four primary database tables. The schema for the first three is verified below; `temp` is a temporary table created and dropped during cleanup operations.

### Verified Tables

| Table | Primary Key | Description |
| :--- | :--- | :--- |
| `creature_movement` | `id`, `point` | Stores waypoints for specific creature instances identified by their GUID (`id`). Used when a specific spawned creature has a unique path. |
| `creature_movement_template` | `entry`, `point` | Stores waypoints for creature templates identified by their Entry ID (`entry`). Used as a fallback for all creatures of that type if no specific GUID-based path exists. |
| `creature_movement_special` | `id`, `point` | Stores "special" paths, often used for complex scripted movements or events. Referenced by other paths via the `path_id` column. |

### Column Usage
The manager reads the following columns from these tables to populate `WaypointNode` structures:
*   `position_x`, `position_y`, `position_z`: Spatial coordinates.
*   `orientation`: The facing angle of the creature at this point.
*   `waittime`: Delay in milliseconds before moving to the next point.
*   `wander_distance`: Radius for random wandering around the point.
*   `script_id`: ID of a script to execute upon reaching this point.
*   `path_id`: Reference to another path in `creature_movement_special`.

### Temporary Table
*   `temp`: Created temporarily in `Cleanup()` to assist in renumbering waypoint points. It is dropped immediately after use.

## Member-by-Member Behavior

### Initialization and Loading

#### **Load**
This is the primary entry point for populating the waypoint system. It performs the following steps:
1.  Calls `Cleanup()` to ensure database integrity (renumbering points).
2.  Iterates through `sCreatureMovementScripts` to build a set of known valid script IDs.
3.  **Loads `creature_movement`:**
    *   Queries the table to count paths and nodes for progress reporting.
    *   Fetches all rows. For each row:
        *   Validates `script_id` against known scripts. Logs an error and skips the node if invalid.
        *   Validates the creature GUID exists via `ObjectMgr/GetCreatureData`. Logs an error and skips if the creature doesn't exist.
        *   Populates a `WaypointNode` in `m_pathMap`.
        *   Validates coordinates using `GridDefines/IsValidMapCoord`. If invalid, it logs an error, normalizes X/Y, calculates Z from terrain height (`GridMap/GetHeightStatic`), and **updates the database** with the corrected coordinates.
        *   Tracks `path_id` references to validate later.
4.  **Loads `creature_movement_template`:**
    *   Similar process to above, but keyed by `entry` (shifted left by 8 bits for internal storage).
    *   Validates `entry` exists via `ObjectMgr/GetCreatureTemplate`.
    *   Corrects invalid coordinates and updates the database.
5.  **Loads `creature_movement_special`:**
    *   Similar process, keyed by `id`.
    *   Corrects invalid coordinates and updates the database.
6.  **Post-Load Validation:**
    *   Checks if any `path_id` referenced in the loaded paths points to a non-existent path in `m_pathSpecialMap`. Logs errors for dangling references.
    *   Logs any scripts in `sCreatureMovementScripts` that were never referenced by any waypoint.

#### **Cleanup**
Ensures that waypoint points are numbered sequentially starting from 1 for each path.
1.  Checks `creature_movement` for gaps or out-of-order points using a correlated subquery.
2.  If issues are found:
    *   Creates a temporary table `temp` copying the current data.
    *   Drops the primary key.
    *   Updates `point` values to be sequential counts based on the original order.
    *   Restores the primary key.
    *   Drops `temp`.
3.  Repeats this process for `creature_movement_template` and `creature_movement_special`.

#### **Unload**
Clears all in-memory waypoint maps (`m_pathMap`, `m_pathTemplateMap`, `m_pathSpecialMap`) by calling `_clearPath` on each entry. This is typically called during server shutdown or reload.

#### **_clearPath**
A helper method that clears the contents of a `WaypointPath` (std::map).

### Path Retrieval

#### **GetDefaultPath**
Retrieves the appropriate path for a creature based on priority:
1.  First attempts to find a path specific to the creature's GUID (`lowGuid`) using `GetPath`. If found, sets `wpOrigin` to `PATH_FROM_GUID`.
2.  If no GUID-specific path exists, attempts to find a template path based on the creature's `entry` using `GetPathTemplate`. If found, sets `wpOrigin` to `PATH_FROM_ENTRY`.
3.  Returns `nullptr` if neither exists.

#### **GetPathFromOrigin**
Retrieves a path explicitly specified by its origin type and key:
*   `PATH_FROM_GUID`: Looks up `lowGuid` in `m_pathMap`.
*   `PATH_FROM_ENTRY`: Constructs a key from `(entry << 8) + pathId` and looks it up in `m_pathTemplateMap`. Note: `pathId` must be within a valid range (0-255) due to the bit-shifting logic.
*   `PATH_FROM_SPECIAL`: Looks up `entry` (used as ID) in `m_pathSpecialMap`.

#### **GetPath**, **GetPathTemplate**, **GetPathSpecial**
Private helper methods that perform simple lookups in their respective maps (`m_pathMap`, `m_pathTemplateMap`, `m_pathSpecialMap`). They are not exposed publicly but are used by `GetDefaultPath` and `GetPathFromOrigin`.

### Runtime Modification (GM Commands)

These methods are primarily called by `ChatHandler.CreatureCommands` to allow Game Masters to edit waypoints live. They update both the in-memory cache and the database.

#### **AddNode**
Adds a new waypoint node to a path.
*   Supports `PATH_FROM_GUID` and `PATH_FROM_ENTRY`.
*   If `pointId` is 0, it appends to the end of the path.
*   If `pointId` is specified and already exists, it shifts subsequent nodes down (incrementing their point numbers) to make room.
*   Updates the database: Inserts the new node and updates the `point` column for shifted nodes.
*   Returns a pointer to the newly added `WaypointNode`.

#### **DeleteNode**
Removes a waypoint node from a path.
*   Supports `PATH_FROM_GUID` and `PATH_FROM_ENTRY`.
*   Shifts subsequent nodes up (decrementing their point numbers) to fill the gap.
*   Updates the database: Deletes the node and updates the `point` column for shifted nodes.

#### **DeletePath**
Deletes all waypoints for a specific creature GUID.
*   Executes a `DELETE` query on `creature_movement`.
*   Clears the in-memory path in `m_pathMap`.
*   **Note:** The map entry itself is not erased to avoid dangling pointers held by active movement generators, though the comment notes this is a minor memory leak acceptable for GM commands.

#### **SetNodePosition**
Updates the X, Y, Z coordinates of a specific waypoint.
*   Supports `PATH_FROM_GUID` and `PATH_FROM_ENTRY`.
*   Updates the database and the in-memory `WaypointNode`.

#### **SetNodeWaittime**
Updates the `waittime` (delay) of a specific waypoint.
*   Supports `PATH_FROM_GUID` and `PATH_FROM_ENTRY`.
*   Updates the database and the in-memory `WaypointNode`.

#### **SetNodeOrientation**
Updates the `orientation` of a specific waypoint.
*   Supports `PATH_FROM_GUID` and `PATH_FROM_ENTRY`.
*   Updates the database and the in-memory `WaypointNode`.

#### **SetNodeScriptId**
Updates the `script_id` of a specific waypoint.
*   Supports `PATH_FROM_GUID` and `PATH_FROM_ENTRY`.
*   Updates the database and the in-memory `WaypointNode`.
*   Returns `true` if the provided `scriptId` exists in `sCreatureMovementScripts`, `false` otherwise.

#### **GetOriginString**
A static utility function that converts a `WaypointPathOrigin` enum value into a human-readable string (e.g., "guid", "entry", "special"). Used by chat handlers for display purposes.

## Cross-Unit Boundaries

### Calls Out
*   **Database/PExecute, PQuery, DirectExecute, Query:** Used extensively in `Load`, `Cleanup`, and modification methods to read and write waypoint data.
*   **Errors/PrintStacktraceAndThrow:** Called if database queries fail critically (though most failures are handled with logging and skipping).
*   **Field/GetFloat, GetUInt32:** Used to extract data from query results.
*   **GridDefines/IsValidMapCoord, NormalizeMapCoord:** Used to validate and correct waypoint coordinates.
*   **GridMap/GetHeightStatic, LoadTerrain:** Used to calculate the correct Z coordinate for invalid waypoints.
*   **Log.Main/Out:** Used for logging load progress, errors, and warnings.
*   **ObjectMgr/GetCreatureData, GetCreatureTemplate, IsExistingCreatureGuid, IsExistingCreatureId:** Used to validate that waypoints reference existing creatures or templates.
*   **ProgressBar/BarGoLink, step:** Used to display progress bars during the loading phase.
*   **QueryResult/Fetch, GetRowCount, NextRow:** Standard iteration over database results.
*   **WaypointNode/WaypointNode#2:** Constructor called in `AddNode`.

### Called By
*   **World/SetInitialWorldSettings:** Triggers `Load()` during server startup.
*   **ChatHandler.CreatureCommands/HandleWpAddCommand, HandleWpExportCommand, HandleWpModifyCommand, HandleWpShowCommand, HandleNpcSpawnSetMoveTypeCommand:** Use various getter and setter methods to manage waypoints via GM commands.
*   **CyclicMovementGenerator/LoadPath, WaypointMovementGenerator/LoadPath, StartMove#2:** Retrieve paths for active creature movement.
*   **WaypointMovementGenerator/GetPathInformation#2:** Uses `GetOriginString` for debugging/display.

## Notable Implementation Details

1.  **Coordinate Auto-Correction:** `Load` actively fixes bad data. If a waypoint has invalid X/Y coordinates, it normalizes them and fetches the terrain height for Z, then **writes these corrections back to the database**. This ensures that subsequent loads don't repeat the error, but it also means the database is modified at runtime.
2.  **Template Key Encoding:** In `m_pathTemplateMap`, the key is constructed as `(entry << 8) + pathId`. This allows multiple paths per template entry (up to 256, since `pathId` is treated as a byte). This is a compact storage trick but limits the number of distinct paths per template.
3.  **Memory Leak in DeletePath:** `DeletePath` clears the vector/map content but does not erase the key from `m_pathMap`. The comment explains this is to prevent crashes from dangling pointers in active movement generators. This is a deliberate trade-off for stability over memory efficiency in a rare GM operation.
4.  **Script Validation:** During load, if a `script_id` is present but not found in `sCreatureMovementScripts`, the entire waypoint node is skipped. This prevents runtime crashes from executing undefined scripts.
5.  **Renumbering Logic:** `Cleanup` uses a complex SQL subquery to detect and fix non-sequential point numbers. This is crucial because the movement generators expect points to be indexed 1, 2, 3... sequentially. Gaps or duplicates would cause movement errors.
6.  **Special Path References:** `Load` tracks `path_id` references in `specialPathSet` and validates them at the end. If a path references a special path that doesn't exist, it logs an error but continues loading. This allows for graceful degradation rather than a hard failure.

## Member Reference

**Load**: Loads waypoint data from `creature_movement`, `creature_movement_template`, and `creature_movement_special` tables into memory, validating creatures, scripts, and coordinates. Automatically corrects invalid coordinates in the database.

**WaypointManager**: Default constructor. Initializes the singleton instance.

**~WaypointManager**: Destructor. Calls `Unload()` to clear memory.

**GetDefaultPath**: Retrieves the best matching path for a creature, preferring GUID-specific paths over template paths.

**GetPathFromOrigin**: Retrieves a path explicitly specified by origin type (GUID, Entry, or Special) and key.

**GetOriginString**: Static utility converting `WaypointPathOrigin` enum to string.

**GetPath**: Private helper to lookup path by GUID in `m_pathMap`.

**GetPathTemplate**: Private helper to lookup path by Entry in `m_pathTemplateMap`.

**GetPathSpecial**: Private helper to lookup path by ID in `m_pathSpecialMap`.

**Cleanup**: Renumberes waypoint points in database tables to ensure sequential ordering.

**Unload**: Clears all in-memory waypoint maps.

**_clearPath**: Helper to clear a single `WaypointPath` container.

**AddNode**: Adds a new waypoint node to a path, shifting existing nodes if necessary, and updates the database.

**DeleteNode**: Removes a waypoint node from a path, shifting subsequent nodes, and updates the database.

**DeletePath**: Deletes all waypoints for a specific GUID from the database and clears the in-memory cache (without removing the map key).

**SetNodePosition**: Updates the X, Y, Z coordinates of a waypoint in memory and database.

**SetNodeWaittime**: Updates the wait time of a waypoint in memory and database.

**SetNodeOrientation**: Updates the orientation of a waypoint in memory and database.

**SetNodeScriptId**: Updates the script ID of a waypoint in memory and database, returning validity status.

---

<!-- machine-true, projected from graph.json -->

## Map — WaypointManager

*Source:* WaypointManager.cpp, WaypointManager.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| Load | method | Database/PExecute#2, Database/PQuery, Database/Query, Errors/PrintStacktraceAndThrow, Field/GetFloat, Field/GetUInt32, GridDefines/IsValidMapCoord#4, GridDefines/NormalizeMapCoord, GridMap/GetHeightStatic, GridMap/LoadTerrain, Log.Main/Out, ObjectMgr/GetCreatureData, ObjectMgr/GetCreatureTemplate, ObjectMgr/IsExistingCreatureGuid, ObjectMgr/IsExistingCreatureId, ProgressBar/BarGoLink, ProgressBar/BarGoLink#2, ProgressBar/BarGoLink#3, ProgressBar/step, QueryResult/Fetch, QueryResult/GetRowCount, QueryResult/NextRow | World/SetInitialWorldSettings | creature, creature_movement, creature_movement_special, creature_movement_template |
| WaypointManager | ctor | — | — | — |
| ~WaypointManager | dtor | — | — | — |
| GetDefaultPath | method | — | ChatHandler.CreatureCommands/HandleWpAddCommand, ChatHandler.CreatureCommands/HandleWpExportCommand, ChatHandler.CreatureCommands/HandleWpModifyCommand, ChatHandler.CreatureCommands/HandleWpShowCommand, CyclicMovementGenerator/LoadPath, WaypointMovementGenerator/LoadPath | — |
| GetPathFromOrigin | method | — | ChatHandler.CreatureCommands/HandleWpAddCommand, ChatHandler.CreatureCommands/HandleWpExportCommand, ChatHandler.CreatureCommands/HandleWpModifyCommand, ChatHandler.CreatureCommands/HandleWpShowCommand, CyclicMovementGenerator/LoadPath, WaypointMovementGenerator/LoadPath, WaypointMovementGenerator/StartMove#2 | — |
| GetOriginString | method | — | ChatHandler.CreatureCommands/HandleWpAddCommand, ChatHandler.CreatureCommands/HandleWpModifyCommand, ChatHandler.CreatureCommands/HandleWpShowCommand, WaypointMovementGenerator/GetPathInformation#2 | — |
| GetPath | method | — | — | — |
| GetPathTemplate | method | — | — | — |
| GetPathSpecial | method | — | — | — |
| Cleanup | method | Database/DirectExecute, Database/Query, Errors/PrintStacktraceAndThrow, Log.Main/Out | — | creature_movement, creature_movement_special, creature_movement_template, temp |
| Unload | method | — | — | — |
| _clearPath | method | — | — | — |
| AddNode | method | Database/PExecuteLog, WaypointNode/WaypointNode#2 | ChatHandler.CreatureCommands/HandleWpAddCommand | — |
| DeleteNode | method | Database/PExecuteLog | ChatHandler.CreatureCommands/HandleWpModifyCommand | — |
| DeletePath | method | Database/PExecuteLog | ChatHandler.CreatureCommands/HandleNpcSpawnSetMoveTypeCommand | creature_movement |
| SetNodePosition | method | Database/PExecuteLog | ChatHandler.CreatureCommands/HandleWpModifyCommand | — |
| SetNodeWaittime | method | Database/PExecuteLog | ChatHandler.CreatureCommands/HandleWpModifyCommand | — |
| SetNodeOrientation | method | Database/PExecuteLog | ChatHandler.CreatureCommands/HandleWpModifyCommand | — |
| SetNodeScriptId | method | Database/PExecuteLog | ChatHandler.CreatureCommands/HandleWpModifyCommand | — |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `creature`: guid int(10) unsigned PK, id mediumint(8) unsigned, id2 mediumint(8) unsigned, id3 mediumint(8) unsigned, id4 mediumint(8) unsigned, id5 mediumint(8) unsigned, map smallint(5) unsigned, position_x float, position_y float, position_z float, orientation float, spawntimesecsmin int(10) unsigned, spawntimesecsmax int(10) unsigned, wander_distance float, health_percent float, mana_percent float unsigned, movement_type tinyint(3) unsigned, spawn_flags int(10) unsigned, visibility_mod float?, patch_min tinyint(3) unsigned, patch_max tinyint(3) unsigned
- `creature_movement`: id int(10) unsigned PK, point mediumint(8) unsigned PK, position_x float, position_y float, position_z float, orientation float, waittime int(10) unsigned, wander_distance float unsigned, script_id mediumint(8) unsigned, path_id int(10) unsigned
- `creature_movement_special`: id int(10) unsigned PK, point mediumint(8) unsigned PK, position_x float, position_y float, position_z float, orientation float, waittime int(10) unsigned, wander_distance float unsigned, script_id mediumint(8) unsigned, path_id int(10) unsigned
- `creature_movement_template`: entry mediumint(8) unsigned PK, point mediumint(8) unsigned PK, position_x float, position_y float, position_z float, orientation float, waittime int(10) unsigned, wander_distance float unsigned, script_id mediumint(8) unsigned, path_id int(10) unsigned

*`?` = nullable, `PK` = primary key column.*

## Tables with NO verified schema — column names/types unknown, do not guess

- `temp`

