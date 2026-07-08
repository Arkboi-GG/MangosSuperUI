<!-- provenance: failed-members, boundary-bleed -->
# ChatHandler.ObjectCommands

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ChatHandler.ObjectCommands

## Purpose & Responsibilities

This unit implements the server-side logic for Game Object (GO) management commands within the `ChatHandler` class of the WoWVMaNGOS emulator. It provides Game Masters (GMs) and administrators with tools to inspect, manipulate, spawn, despawn, move, rotate, and debug static world objects (doors, buttons, chests, environmental props, etc.).

The unit handles two distinct workflows:
1.  **Direct Manipulation:** Commands that require a specific Game Object GUID (often extracted from chat links or manually entered) to perform actions like moving, rotating, deleting, or changing state.
2.  **Selection-Based Manipulation:** Commands that operate on the "currently selected" Game Object, allowing for rapid iteration over nearby objects without needing to know their GUIDs beforehand.

It interacts heavily with the `GameObject` class, `ObjectMgr`, `Map` systems, and the `WorldDatabase` to persist changes and query spatial data.

## Member-by-Member Behavior

### Inspection and Information Retrieval

**HandleGameObjectTargetCommand**
Locates a Game Object based on user input (ID, name, or proximity) and displays detailed information.
- If arguments are provided, it attempts to parse a Game Object entry ID or a name from a chat link (`Hgameobject_entry`) using helpers from `ChatHandler.Chat`. It queries the `gameobject` table for the nearest match on the current map, optionally filtering by active game events via `GameEventMgr.Main/GetActiveEventList`.
- If no arguments are provided, it queries the `gameobject` table for the 10 nearest objects on the current map, excluding those tied to inactive events (using a `LEFT OUTER JOIN` with `game_event_gameobject`).
- It checks if the found object is part of a pool (`PoolManager/IsPartOfAPool`) and if it is currently spawned.
- If the object exists in memory (`Map.Main/GetGameObject`), it calculates and displays respawn delays using `shared_Util/secsToTimeString`.

**HandleGameObjectInfoCommand**
Displays comprehensive runtime statistics for a specific Game Object identified by GUID.
- Retrieves the object via `getSelectedGameObject` or by parsing a GUID from a chat link using `ChatHandler.Chat/ExtractUint32KeyFromLink`.
- Outputs Entry ID, GUID, Name, Type, Display ID, GO State, and Loot State.
- Converts flag bitmasks to human-readable strings using `shared_Util/FlagsToString`.
- Reports visibility modifiers, active status, and spawn status. If not spawned, it calculates the remaining respawn time.

**HandleGameObjectUpdateFieldsInfoCommand**
Debug utility that dumps the raw update fields of a Game Object.
- Identifies the object by GUID using `ChatHandler.Chat/ExtractUint32KeyFromLink`.
- Delegates to `ChatHandler.DebugCommands/ShowAllUpdateFieldsHelper` to print the internal object field values, useful for debugging synchronization issues between client and server.

**HandleGameObjectNearCommand**
Lists all Game Objects within a specified radius of the player.
- Queries the `gameobject` table directly using a spatial calculation (`POW(position_x - ..., 2) + ...`) to find objects within the squared distance limit.
- Iterates through results, fetching template info via `ObjectMgr/GetGameObjectTemplate` and printing details for each valid object.

### Selection Management

**HandleGameObjectSelectCommand**
Selects the nearest Game Object to the player within a 10-unit radius.
- Uses `NearestGameObjectInObjectRangeCheck` and grid visitors (`MaNGOS::GameObjectLastSearcher`) to efficiently search the map grid.
- Sets the player's selected Game Object via `Player.Main/SetSelectedGobj`.
- Displays the selected object's details.

**getSelectedGameObject**
Retrieves the `GameObject` pointer associated with the player's currently selected GUID.
- Called by many other commands (e.g., `HandleGameObjectDespawnCommand`, `HandleGameObjectUseCommand`) and by `ChatHandler.PlayerBotMgr/HandlePartyBotUseGObjectCommand`.
- Fetches the object from the map using `Map.Main/GetGameObject` and the GUID stored in `Player.Main/GetSelectedGobj`.

### Movement and Rotation

**HandleGameObjectMoveCommand**
Relocates a Game Object to new coordinates.
- Accepts optional X, Y, Z arguments. If omitted, moves the object to the player's current position.
- Validates coordinates using `MapManager/IsValidMapCoord`.
- Removes the object from the map, updates its position via `WorldObject.Object/Relocate` and `WorldObject.Object/SetFloatValue`, re-adds it to the map, saves to DB, and refreshes its state.

**HandleGameObjectTurnCommand**
Rotates a Game Object to a new orientation.
- Accepts an optional angle argument; defaults to the player's orientation.
- Similar to move, it removes the object from the map, updates rotation via `WorldObject.Object/Relocate` and `GameObject/UpdateRotationFields`, re-adds it, saves to DB, and refreshes.

### Lifecycle Management (Spawn/Despawn/Delete)

**HandleGameObjectAddCommand**
Permanently spawns a new Game Object at the player's location.
- Validates the entry ID and display ID.
- Generates a new static low GUID via `ObjectMgr/GenerateStaticGameObjectLowGuid`.
- Creates the `GameObject` instance, sets its initial state, and saves it to the database via `GameObject/SaveToDB`.
- Loads it back from DB to ensure consistency (`GameObject/LoadFromDB`) and adds it to the grid.

**HandleGameObjectTempAddCommand**
Temporarily summons a Game Object that despawns after a set time.
- Does not save to the database.
- Calculates rotation quaternions based on the player's orientation.
- Uses `WorldObject.Object/SummonGameObject` to create the transient object.

**HandleGameObjectDeleteCommand**
Permanently removes a Game Object from the world and database.
- Checks if the GUID is referenced in scripts (`ScriptMgr/IsGameObjectGuidReferencedInScripts`) to prevent accidental deletion of scripted objects.
- Handles ownership cleanup if the object was summoned by a player/unit.
- Sets respawn time to 0, deletes the object from memory (`GameObject/Delete`), and removes its record from the database (`GameObject/DeleteFromDB`).

**HandleGameObjectDespawnCommand**
Temporarily hides a Game Object.
- Calls `GameObject/Despawn` on the selected object. The object remains in memory and DB but is invisible and non-interactable until respawned.

**HandleGameObjectRespawnCommand**
Restores a despawned Game Object.
- Calls `GameObject/Respawn` on the selected object.

### State and Interaction Control

**HandleGameObjectToggleCommand**
Toggles the state of doors or buttons.
- If the object is ready or just deactivated, it activates it (`GameObject/UseDoorOrButton`).
- Otherwise, it resets it (`GameObject/ResetDoorOrButton`).

**HandleGameObjectResetCommand**
Resets a door or button to its default state.
- Calls `GameObject/ResetDoorOrButton` on the selected object.

**HandleGameObjectUseCommand**
Simulates a player using the selected Game Object.
- Calls `GameObject/Use` with the player as the initiator.

**HandleGameObjectSetGoStateCommand**
Forces a Game Object into a specific visual/state mode (e.g., closed, open, broken).
- Validates the state value against `GO_STATE_ACTIVE_ALTERNATIVE`.
- Applies the state via `GameObject/SetGoState`.

**HandleGameObjectSetLootStateCommand**
Forces a Game Object into a specific loot state (e.g., ready, opened, just deactivated).
- Validates the state value against `GO_JUST_DEACTIVATED`.
- Applies the state via `GameObject/SetLootState`.

### Animation Triggers

**HandleGameObjectSendCustomAnimCommand**
Plays a custom animation on a Game Object.
- Takes an animation ID argument.
- Calls `GameObject/SendGameObjectCustomAnim`.

**HandleGameObjectSendSpawnAnimCommand**
Triggers the standard spawn animation.
- Calls `WorldObject.Object/SendObjectSpawnAnim`.

**HandleGameObjectSendDespawnAnimCommand**
Triggers the standard despawn animation.
- Calls `WorldObject.Object/SendObjectDeSpawnAnim`.

### Helper Class: NearestGameObjectInObjectRangeCheck

This local functor class supports `HandleGameObjectSelectCommand`.

**NearestGameObjectInObjectRangeCheck**
Constructor for the functor used to find the nearest Game Object. Initializes focus object and range.

**GetFocusObject**
Returns the reference to the focus object.

**operator()**
Checks if a candidate `GameObject` is within the current range of the focus object.
- If within range, it updates the internal `i_range` to the actual distance (to find the *nearest* one) and returns `true`.
- This allows the grid searcher to progressively tighten the search radius.

**GetLastRange**
Returns the final calculated range after the search completes.

## Cross-Unit Boundaries

- **ChatHandler.Chat**: All commands rely on helper methods in the main `ChatHandler` unit for argument parsing (`ExtractUInt32`, `ExtractKeyFromLink`), messaging (`PSendSysMessage`, `SendSysMessage`), and error handling (`SetSentErrorMessage`).
- **GameObject**: The core entity being manipulated. Methods like `GetRespawnTimeEx`, `SaveToDB`, `Delete`, `UseDoorOrButton`, and `SetGoState` are called extensively.
- **ObjectMgr**: Used to fetch static data templates (`GetGameObjectTemplate`, `GetGOData`) and manage GUID generation (`GenerateStaticGameObjectLowGuid`).
- **Map.Main / WorldObject.Object**: Used for spatial operations (`GetMap`, `GetPositionX/Y/Z`, `Relocate`, `GetDistance`) and retrieving objects from the active world state (`GetGameObject`).
- **Database**: Direct SQL queries are executed via `WorldDatabase.PQuery` for spatial searches (`HandleGameObjectTargetCommand`, `HandleGameObjectNearCommand`) and string escaping.
- **GameEventMgr.Main**: Used in `HandleGameObjectTargetCommand` to filter out objects tied to inactive events.
- **PoolManager**: Used in `HandleGameObjectTargetCommand` to verify if a found object is part of a random spawn pool and if it is currently active.
- **ScriptMgr**: Used in `HandleGameObjectDeleteCommand` to protect scripted objects from deletion.
- **ChatHandler.DebugCommands**: `HandleGameObjectUpdateFieldsInfoCommand` delegates to `ShowAllUpdateFieldsHelper` in this unit.
- **ChatHandler.PlayerBotMgr**: `getSelectedGameObject` is called by `HandlePartyBotUseGObjectCommand` in the bot management unit, indicating bots can interact with GM-selected objects.

## Data Model

This unit interacts with two database tables:

1.  **`gameobject`**
    -   **Usage**: Primary source of truth for persistent Game Object instances.
    -   **Columns Accessed**:
        -   `guid`: Unique identifier for the instance.
        -   `id`: Links to the template entry.
        -   `position_x`, `position_y`, `position_z`: Spatial coordinates.
        -   `orientation`: Rotation angle.
        -   `map`: Map ID.
        -   `state`, `animprogress`, `visibility_mod`: Runtime state persisted to DB.
    -   **Queries**:
        -   `HandleGameObjectTargetCommand`: Selects nearest objects by ID or name, calculating Euclidean distance squared in SQL.
        -   `HandleGameObjectNearCommand`: Selects objects within a radius using squared distance comparison.

2.  **`game_event_gameobject`**
    -   **Usage**: Links Game Objects to specific game events.
    -   **Columns Accessed**:
        -   `guid`: Foreign key to `gameobject.guid`.
        -   `event`: Event ID.
    -   **Queries**:
        -   `HandleGameObjectTargetCommand`: Left joins this table to exclude objects tied to inactive events from the "nearest" search when no specific ID/name is provided.

## Notable Implementation Details

-   **Spatial Search in SQL**: Both `HandleGameObjectTargetCommand` and `HandleGameObjectNearCommand` perform spatial filtering directly in MySQL using `POW(x1-x2, 2) + ...`. This avoids loading all objects into memory for distance calculations but relies on the database engine's optimization. Note that `HandleGameObjectNearCommand` uses `<= '%f'` for the distance check, passing `distance * distance`.
-   **Pool Awareness**: `HandleGameObjectTargetCommand` explicitly checks `sPoolMgr.IsPartOfAPool` and `pl->GetMap()->GetPersistentState()->IsSpawnedPoolObject`. This ensures that GMs don't try to interact with or report on pool objects that aren't currently spawned in the world, preventing confusion between potential spawns and active entities.
-   **Script Protection**: `HandleGameObjectDeleteCommand` includes a safeguard: `sScriptMgr.IsGameObjectGuidReferencedInScripts`. This prevents accidental deletion of objects that are hard-coded into C++ scripts, which would likely cause server crashes or broken quest logic.
-   **Temporary vs. Permanent Spawns**: `HandleGameObjectAddCommand` generates a permanent static GUID and saves to DB. `HandleGameObjectTempAddCommand` uses `SummonGameObject`, which typically creates a temporary object with a dynamic GUID that is not persisted.
-   **Rotation Handling**: `HandleGameObjectTurnCommand` and `HandleGameObjectMoveCommand` both remove the object from the map before relocating and re-add it. This is necessary because the map's spatial indexing needs to be updated when an object's position changes significantly.
-   **Argument Parsing Flexibility**: Most commands accept either a direct GUID/ID or a "Shift-click" link (`Hgameobject:...`). The `ExtractUint32KeyFromLink` and `ExtractKeyFromLink` helpers facilitate this dual-input style.
-   **Event Filtering Logic**: In `HandleGameObjectTargetCommand`, the SQL query construction for event filtering is dynamic. It builds a string `AND (event IS NULL OR event IN (...))` based on the list of active events returned by `GameEventMgr`. This ensures that objects tied to inactive events are hidden from general "find nearest" searches.

## Member Reference

**HandleGameObjectTargetCommand**
Locates and displays info for a Game Object by ID, name, or proximity. Queries `gameobject` and `game_event_gameobject` tables. Filters by active events. Checks pool status.

**HandleGameObjectInfoCommand**
Displays detailed runtime stats (entry, GUID, state, flags, respawn time) for a specific Game Object identified by GUID.

**HandleGameObjectUpdateFieldsInfoCommand**
Debug tool that prints the raw update fields of a Game Object. Delegates to `ChatHandler.DebugCommands/ShowAllUpdateFieldsHelper`.

**HandleGameObjectTurnCommand**
Rotates a Game Object to a specified orientation (defaulting to player's). Removes from map, updates rotation, saves to DB, and re-adds.

**HandleGameObjectMoveCommand**
Moves a Game Object to specified coordinates (or player's location). Validates coords, removes from map, updates position, saves to DB, and re-adds.

**HandleGameObjectDeleteCommand**
Permanently deletes a Game Object from memory and DB. Prevents deletion if GUID is referenced in scripts. Cleans up ownership.

**HandleGameObjectAddCommand**
Permanently spawns a new Game Object at player's location. Generates static GUID, validates template, saves to DB, and loads into world.

**HandleGameObjectTempAddCommand**
Temporarily summons a Game Object at player's location. Does not save to DB. Calculates rotation quaternions from player orientation.

**HandleGameObjectNearCommand**
Lists Game Objects within a radius. Queries `gameobject` table with spatial SQL filter. Prints details for each match.

**NearestGameObjectInObjectRangeCheck**
Constructor for the functor used to find the nearest Game Object. Initializes focus object and range.

**GetFocusObject**
Returns the focus object reference held by the `NearestGameObjectInObjectRangeCheck` functor.

**operator()**
Functor method that checks if a Game Object is within the current search range. Updates the range to the found object's distance if closer.

**GetLastRange**
Returns the final range value from the `NearestGameObjectInObjectRangeCheck` functor after search completion.

**HandleGameObjectSelectCommand**
Selects the nearest Game Object within 10 units. Uses grid visitors and `NearestGameObjectInObjectRangeCheck`. Sets player's selected GO.

**getSelectedGameObject**
Retrieves the `GameObject` pointer for the player's currently selected GUID. Used by many toggle/use/despawn commands.

**HandleGameObjectDespawnCommand**
Temporarily hides the selected Game Object by calling `GameObject/Despawn`.

**HandleGameObjectRespawnCommand**
Restores the selected despawned Game Object by calling `GameObject/Respawn`.

**HandleGameObjectToggleCommand**
Toggles the state of doors/buttons. Activates if ready/deactivated, otherwise resets.

**HandleGameObjectResetCommand**
Resets the selected door/button to its default state.

**HandleGameObjectUseCommand**
Simulates the player using the selected Game Object.

**HandleGameObjectSetGoStateCommand**
Forces the selected Game Object into a specific visual/state mode (e.g., open/closed).

**HandleGameObjectSetLootStateCommand**
Forces the selected Game Object into a specific loot state (e.g., ready/opened).

**HandleGameObjectSendCustomAnimCommand**
Plays a custom animation on the selected Game Object using a provided animation ID.

**HandleGameObjectSendSpawnAnimCommand**
Triggers the standard spawn animation on the selected Game Object.

**HandleGameObjectSendDespawnAnimCommand**
Triggers the standard despawn animation on the selected Game Object.

---

<!-- machine-true, projected from graph.json -->

## Map — ChatHandler.ObjectCommands

*Source:* ObjectCommands.cpp, Chat.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| HandleGameObjectTargetCommand | method | ChatHandler.Chat/ExtractKeyFromLink#2, ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, Database/escape_string, Database/PQuery, Field/GetFloat, Field/GetUInt16, Field/GetUInt32, GameEventMgr.Main/GetActiveEventList, GameObject/GetRespawnDelay, GameObject/GetRespawnTimeEx, Map.Main/GetGameObject, Map.Main/GetPersistentState, Object/GetGUIDLow, ObjectGuid/ObjectGuid#3, ObjectMgr/GetGameObjectTemplate, PoolManager/IsPartOfAPool#2, QueryResult/Fetch, QueryResult/NextRow, shared_Util/secsToTimeString, WorldObject.Object/GetMap, WorldObject.Object/GetMapId, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldSession.Main/GetPlayer | — | gameobject, game_event_gameobject |
| HandleGameObjectInfoCommand | method | ChatHandler.Chat/ExtractUint32KeyFromLink, ChatHandler.Chat/GetGameObjectWithGuid, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SetSentErrorMessage, GameObject/GetDisplayId, GameObject/GetGOInfo, GameObject/GetGoState, GameObject/GetGoType, GameObject/getLootState, GameObject/GetRespawnDelay, GameObject/GetRespawnTime, GameObject/isSpawned, Object/GetEntry, Object/GetGUIDLow, Object/GetUInt32Value, ObjectMgr/GetGOData, shared_Util/FlagsToString, WorldObject.Object/GetVisibilityModifier, WorldObject.Object/IsActiveObject | — | — |
| HandleGameObjectUpdateFieldsInfoCommand | method | ChatHandler.Chat/ExtractUint32KeyFromLink, ChatHandler.Chat/GetGameObjectWithGuid, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, ChatHandler.DebugCommands/ShowAllUpdateFieldsHelper, ObjectMgr/GetGOData | — | — |
| HandleGameObjectTurnCommand | method | ChatHandler.Chat/ExtractOptFloat, ChatHandler.Chat/ExtractUint32KeyFromLink, ChatHandler.Chat/GetGameObjectWithGuid, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, GameObject/GetGOInfo, GameObject/Refresh, GameObject/SaveToDB, GameObject/UpdateRotationFields, Object/GetGUIDLow, ObjectMgr/GetGOData, WorldObject.Object/GetMap, WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/Relocate#2, WorldSession.Main/GetPlayer | — | — |
| HandleGameObjectMoveCommand | method | ChatHandler.Chat/ExtractFloat, ChatHandler.Chat/ExtractUint32KeyFromLink, ChatHandler.Chat/GetGameObjectWithGuid, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, GameObject/GetGOInfo, GameObject/Refresh, GameObject/SaveToDB, MapManager/IsValidMapCoord#3, Object/GetGUIDLow, ObjectMgr/GetGOData, WorldObject.Object/GetMap, WorldObject.Object/GetMapId, WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/Relocate#2, WorldObject.Object/SetFloatValue, WorldSession.Main/GetPlayer | — | — |
| HandleGameObjectDeleteCommand | method | ChatHandler.Chat/ExtractUint32KeyFromLink, ChatHandler.Chat/GetGameObjectWithGuid, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage, ChatHandler.Chat/SetSentErrorMessage, GameObject/Delete, GameObject/DeleteFromDB, GameObject/GetDBTableGUIDLow, GameObject/GetOwnerGuid, GameObject/SetRespawnTime, Object/GetGUIDLow, ObjectAccessor/GetUnit, ObjectGuid/GetString, ObjectGuid/IsPlayer, ObjectMgr/GetGOData, ScriptMgr/IsGameObjectGuidReferencedInScripts, Unit.Main/RemoveGameObject, WorldSession.Main/GetPlayer | — | — |
| HandleGameObjectAddCommand | method | ChatHandler.Chat/ExtractOptInt32, ChatHandler.Chat/ExtractUint32KeyFromLink, ChatHandler.Chat/GetMangosString, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, GameObject/Create, GameObject/CreateGameObject, GameObject/LoadFromDB, GameObject/SaveToDB#2, GameObject/SetRespawnTime, Log.Main/Out, Map.Main/GetId, ObjectMgr/AddGameobjectToGrid, ObjectMgr/GenerateStaticGameObjectLowGuid, ObjectMgr/GetGameObjectTemplate, ObjectMgr/GetGOData, WorldObject.Object/GetMap, WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldSession.Main/GetPlayer | — | — |
| HandleGameObjectTempAddCommand | method | ChatHandler.Chat/ExtractUint32KeyFromLink, WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/SummonGameObject, WorldSession.Main/GetPlayer | — | — |
| HandleGameObjectNearCommand | method | ChatHandler.Chat/ExtractOptFloat, ChatHandler.Chat/PSendSysMessage#2, Database/PQuery, Field/GetFloat, Field/GetUInt16, Field/GetUInt32, ObjectMgr/GetGameObjectTemplate, QueryResult/Fetch, QueryResult/NextRow, WorldObject.Object/GetMapId, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldSession.Main/GetPlayer | — | gameobject |
| NearestGameObjectInObjectRangeCheck | ctor | — | — | — |
| GetFocusObject | method | — | — | — |
| operator() | method | WorldObject.Object/GetDistance#3, WorldObject.Object/IsWithinDistInMap | — | — |
| GetLastRange | method | — | — | — |
| HandleGameObjectSelectCommand | method | Cell/Cell#2, Cell/SetNoCreate, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, GameObject/GetDBTableGUIDLow, GameObject/GetGOInfo, GameObject/GetName, GridDefines/ComputeCellPair, Object/GetEntry, Object/GetObjectGuid, Player.Main/SetSelectedGobj, WorldObject.Object/GetMap, WorldObject.Object/GetMapId, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldSession.Main/GetPlayer | — | — |
| getSelectedGameObject | method | Map.Main/GetGameObject, Player.Main/GetSelectedGobj, WorldObject.Object/GetMap, WorldSession.Main/GetPlayer | ChatHandler.PlayerBotMgr/HandlePartyBotUseGObjectCommand | — |
| HandleGameObjectDespawnCommand | method | ChatHandler.Chat/SendSysMessage#2, GameObject/Despawn | — | — |
| HandleGameObjectRespawnCommand | method | ChatHandler.Chat/SendSysMessage#2, GameObject/Respawn | — | — |
| HandleGameObjectToggleCommand | method | ChatHandler.Chat/ExtractUint32KeyFromLink, ChatHandler.Chat/GetGameObjectWithGuid, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, GameObject/getLootState, GameObject/ResetDoorOrButton, GameObject/UseDoorOrButton, ObjectMgr/GetGOData | — | — |
| HandleGameObjectResetCommand | method | ChatHandler.Chat/SendSysMessage#2, GameObject/ResetDoorOrButton | — | — |
| HandleGameObjectUseCommand | method | ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage#2, GameObject/Use, Object/GetGuidStr, WorldSession.Main/GetPlayer | — | — |
| HandleGameObjectSetGoStateCommand | method | ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/ExtractUint32KeyFromLink, ChatHandler.Chat/GetGameObjectWithGuid, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, GameObject/GetName, GameObject/SetGoState, Object/GetGUIDLow, ObjectMgr/GetGOData | — | — |
| HandleGameObjectSetLootStateCommand | method | ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/ExtractUint32KeyFromLink, ChatHandler.Chat/GetGameObjectWithGuid, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, GameObject/GetName, GameObject/SetLootState, Object/GetGUIDLow, ObjectMgr/GetGOData | — | — |
| HandleGameObjectSendCustomAnimCommand | method | ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/ExtractUint32KeyFromLink, ChatHandler.Chat/GetGameObjectWithGuid, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, GameObject/GetName, GameObject/SendGameObjectCustomAnim, Object/GetGUIDLow, ObjectMgr/GetGOData | — | — |
| HandleGameObjectSendSpawnAnimCommand | method | ChatHandler.Chat/ExtractUint32KeyFromLink, ChatHandler.Chat/GetGameObjectWithGuid, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, GameObject/GetName, Object/GetGUIDLow, ObjectMgr/GetGOData, WorldObject.Object/SendObjectSpawnAnim | — | — |
| HandleGameObjectSendDespawnAnimCommand | method | ChatHandler.Chat/ExtractUint32KeyFromLink, ChatHandler.Chat/GetGameObjectWithGuid, ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, GameObject/GetName, Object/GetGUIDLow, ObjectMgr/GetGOData, WorldObject.Object/SendObjectDeSpawnAnim | — | — |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `game_event_gameobject`: guid int(10) unsigned PK, event smallint(6) PK
- `gameobject`: guid int(10) unsigned PK, id mediumint(8) unsigned, map smallint(5) unsigned, position_x float, position_y float, position_z float, orientation float, rotation0 float, rotation1 float, rotation2 float, rotation3 float, spawntimesecsmin int(11), spawntimesecsmax int(11), animprogress tinyint(3) unsigned, state tinyint(3) unsigned, spawn_flags int(10) unsigned, visibility_mod float?, patch_min tinyint(3) unsigned, patch_max tinyint(3) unsigned

*`?` = nullable, `PK` = primary key column.*


---

<!-- verify: failed-members | invented: operator -->

---

<!-- verify: boundary-bleed | foreign: ChatHandler, ExtractKeyFromLink, update -->
