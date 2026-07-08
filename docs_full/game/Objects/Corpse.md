# Corpse

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Corpse

## Purpose & Responsibilities

The `Corpse` class represents the physical remains of a player character after death within the game world. It inherits from `WorldObject`, meaning it exists spatially on a `Map`, has a position, orientation, and scale, and participates in visibility and interaction systems.

Its primary responsibilities are:
1.  **Persistence:** Saving and loading corpse data to and from the `corpse` database table, including position, type, owner GUID, and visual appearance (race, class, equipment display IDs).
2.  **Lifecycle Management:** Handling creation upon player death, removal when reclaimed or expired, and conversion to "bones" (a non-lootable, decaying state).
3.  **Interaction Logic:** Determining visibility to players, faction reactions (friendliness/hostility), and expiration times based on configuration settings.
4.  **Loot Integration:** Hosting a `Loot` object that manages items dropped by the player, though the specific loot mechanics are handled by the `Loot` class itself.

## Member-by-Member Behavior

### Construction and Destruction

**`Corpse` (Constructor)**
Initializes a new `Corpse` object. It sets the object type mask to `TYPEMASK_CORPSE` and initializes internal state such as `m_time` (current Unix timestamp) and `lootForBody` to `false`. It constructs the embedded `Loot` object, passing `this` as the owner. The constructor accepts a `CorpseType` argument (defaulting to `CORPSE_BONES`), which determines how the corpse behaves regarding expiration and saving.

**`~Corpse` (Destructor)**
When a `Corpse` object is destroyed, it checks if it is currently in the world and if its type is `CORPSE_BONES`. If so, it calls `Map.Main/RemoveBones` to unregister itself from the map's bone tracking system. This ensures that bones are properly cleaned up from the map's internal lists before the object memory is freed.

### World Presence and Grid Management

**`AddToWorld`**
Registers the corpse with the global `ObjectAccessor` for GUID-based lookups if it isn't already in the world. It then calls the base `Object::AddToWorld` to insert the object into the map's grid system. This is called by `ObjectGridLoader/LoadHelper` during map loading.

**`RemoveFromWorld`**
Removes the corpse from the `ObjectAccessor` if it is currently in the world, then calls the base `Object::RemoveFromWorld` to detach it from the map's grid. This is invoked by `ObjectAccessor/RemoveCorpse` and `ObjectAccessor::~ObjectAccessor` during cleanup.

**`GetGrid` / `SetGrid` / `GetGridRef`**
These methods manage the `GridPair` coordinate that identifies which map grid cell the corpse occupies. `GetGrid` and `SetGrid` are used by `Map.Main/RemoveCorpses` and `ObjectAccessor/AddCorpsesToGrid` to efficiently locate corpses within specific grid cells for removal or addition operations. `GetGridRef` returns a reference to the internal `GridReference` used for linked-list management within the grid, though it is not called by other units in the current map.

### Creation and Initialization

**`Create` (Overload 1)**
A simple initialization method that assigns a low GUID and high GUID type (`HIGHGUID_CORPSE`) to the object. It is called by `Map.Main/RemoveCorpses` likely for temporary or internal processing purposes.

**`Create` (Overload 2)**
The primary creation method, called by `Player.Main/CreateCorpse`. It performs the following steps:
1.  Initializes the object's GUID using `WorldObject.Object/_Create`.
2.  Copies the owner player's position and orientation.
3.  Sets the corpse's map to the owner's current map.
4.  Validates the position using `WorldObject.Object/IsPositionValid`. If invalid, it logs an error and returns `false`.
5.  Sets default scale and updates internal float values for position and facing.
6.  Sets the owner GUID field.
7.  Computes the grid pair for the position.

### Persistence (Database Interaction)

**`SaveToDB`**
Saves the corpse's current state to the `corpse` table in the character database. It asserts that the corpse is not of type `CORPSE_BONES`, as bones are transient and should not persist. It constructs a `REPLACE INTO` SQL statement containing the corpse's GUID, owner GUID, position, orientation, map ID, creation time, type, and instance ID. This is called by `Player.Main/CreateCorpse` immediately after a player dies.

**`LoadFromDB`**
Loads a corpse from a database query result set. Called by `ObjectMgr/LoadCorpses` during server startup. It extracts fields including position, orientation, map, time, type, instance, and appearance data (race, class, gender, skin, face, hair, etc.). It validates the corpse type and position. Crucially, it reconstructs the visual appearance by looking up the `PlayerInfo` for the race/class pair and setting the display ID. It also parses the `equipment_cache` string to determine which items were equipped, setting the corresponding `CORPSE_FIELD_ITEM` values with the appropriate display info and inventory type. Finally, it computes the grid pair and relocates the object.

**`DeleteFromDB`**
Deletes the corpse record from the `corpse` table. It asserts that the corpse is not `CORPSE_BONES`. It uses a prepared statement to delete records where `player_guid` matches the owner and `corpse_type` is not 0 (bones). This is called by `Map.Main/RemoveCorpses` and `ObjectAccessor/ConvertCorpseForPlayer`.

### State and Identity

**`GetOwnerGuid`**
Returns the GUID of the player who owns the corpse. This is heavily used by other systems: `LootMgr/GetLootTarget` to identify loot owners, `Map.Main/RemoveCorpses` for cleanup, `ObjectAccessor` methods for lookup, and spell effects like `Spell.Effects/EffectSkinPlayerCorpse`.

**`GetGhostTime` / `ResetGhostTime`**
`GetGhostTime` returns the timestamp when the corpse was created. `ResetGhostTime` updates this timestamp to the current time. These are used by `Player.Main/SendCorpseReclaimDelay` and `WorldSession.MiscHandler/HandleReclaimCorpseOpcode` to calculate how long until the corpse can be reclaimed, and by `Player.Main/BuildPlayerRepop` to reset timers during resurrection.

**`GetType`**
Returns the `CorpseType` enum value. Used by `Map.Main` for grid management, `ObjectAccessor` for filtering, and `Player.Main` for sending reclaim delays.

**`GetName`**
Returns the static string "Corpse". Not currently called by other units in the map.

**`GetFactionTemplate` / `SetFactionTemplate` / `GetFactionTemplateId`**
Manages the faction template associated with the corpse, which influences its reaction to other objects. `SetFactionTemplate` is called by `Map.Main/RemoveCorpses`, likely to set a neutral or specific faction before deletion or conversion. `GetFactionTemplateId` returns the ID of the current faction template.

### Visibility and Interaction

**`IsVisibleForInState`**
Determines if the corpse is visible to a detector object. It checks if both the corpse and detector are in the world, and if the corpse is within a calculated distance. The distance is the maximum of the map's visibility distance (plus a grey distance buffer if the object is already in the visible list) and the corpse's visibility modifier. This is used by the visibility system to optimize rendering and updates.

**`GetReactionTo`**
Calculates the reputation rank between the corpse and a target object. It follows this priority:
1.  If the owner player is online, it delegates to the owner's reaction.
2.  If the target is a player in a group that includes the corpse's owner, it returns `REP_FRIENDLY`.
3.  If a faction template is set, it calculates the reaction based on that faction.
4.  Otherwise, it returns `REP_NEUTRAL`.

**`IsHostileTo` / `IsFriendlyTo`**
Convenience wrappers around `GetReactionTo`. `IsHostileTo` returns true if the reaction is `REP_HOSTILE` or lower. `IsFriendlyTo` returns true if the reaction is `REP_FRIENDLY` or higher. `IsFriendlyTo` is called by `GridNotifiers/operator()` and `WorldObject.Object/BuildValuesUpdate` to determine update packets and notifications.

### Expiration and Cleanup

**`IsExpired`**
Checks if the corpse has exceeded its lifetime. For `CORPSE_BONES`, it compares the creation time against the configured `CONFIG_UINT32_BONES_EXPIRE_MINUTES`. For other types, it uses a hardcoded 3-day limit. This is called by `Map.Main/RemoveOldBones` and `ObjectAccessor/RemoveOldCorpses` to trigger cleanup routines.

**`DeleteBonesFromWorld`**
Specifically handles the removal of bone-type corpses from the world. It asserts the type is `CORPSE_BONES`, retrieves the corpse from the map, and adds it to the removal list via `WorldObject.Object/AddObjectToRemoveList`. If the corpse is not found, it logs an error.

## Cross-Unit Boundaries

*   **`Player.Main`**: Creates corpses (`CreateCorpse`), sends reclaim delay information (`SendCorpseReclaimDelay`), builds repopulation data (`BuildPlayerRepop`), and provides name data (`GetName`). The `Corpse` unit relies on `Player` for initial position, map context, and ownership identity.
*   **`Map.Main`**: Manages the lifecycle of corpses on the map. It adds/removes them from grids (`AddToGrid`, `RemoveFromGrid`), removes old bones (`RemoveOldBones`), removes corpses entirely (`RemoveCorpses`), and manages visibility distances (`GetVisibilityDistance`). It also sets faction templates before removal.
*   **`ObjectAccessor`**: Provides global lookup services. It adds/removes corpses from the accessor (`AddCorpse`, `RemoveCorpse`), converts corpses for players (`ConvertCorpseForPlayer`), and removes old corpses (`RemoveOldCorpses`). It also adds corpses to grids (`AddCorpsesToGrid`).
*   **`LootMgr`**: Uses `GetOwnerGuid` to determine the target for loot operations (`GetLootTarget`).
*   **`Spell`**: Spells like `EffectSkinPlayerCorpse` interact with corpses, likely modifying their appearance or state, and `SetTargetMap` may use corpse location data.
*   **`WorldObject.Object`**: Inherits core functionality for position, orientation, map association, and grid management. `Corpse` overrides or extends these behaviors for persistence and visibility.
*   **`Database`**: `SaveToDB` and `DeleteFromDB` interact directly with the character database to persist and remove corpse records.

## Data Model

The `Corpse` class interacts primarily with the `corpse` table in the character database.

**Table: `corpse`**
*   **`guid`**: Primary key, unique identifier for the corpse object.
*   **`player_guid`**: References the owner player's GUID.
*   **`position_x`, `position_y`, `position_z`**: Float coordinates of the corpse's location.
*   **`orientation`**: Float value representing the corpse's facing angle.
*   **`map`**: Integer ID of the map where the corpse resides.
*   **`time`**: Big integer timestamp of when the corpse was created.
*   **`corpse_type`**: Tiny integer indicating the type (0=Bones, 1=Resurrectable PVE, 2=Resurrectable PVP).
*   **`instance`**: Integer ID of the instance, if applicable.

Note: The `LoadFromDB` method also reads additional fields from the database query that are not explicitly listed in the provided schema dump (e.g., `gender`, `race`, `class`, `skin`, `face`, `hair_style`, `hair_color`, `facial_hair`, `equipment_cache`, `guild_id`, `player_flags`). These are used to reconstruct the visual appearance of the corpse. The schema provided only lists the core structural columns; the code implies a wider select statement including appearance data.

## Notable Implementation Details

*   **Bone vs. Corpse Distinction**: The code strictly distinguishes between `CORPSE_BONES` and other types. Bones are never saved to the database (`SaveToDB` asserts this) and are removed from the world differently (`DeleteBonesFromWorld`). They have a configurable expiration time, whereas standard corpses expire after 3 days.
*   **Visual Reconstruction**: `LoadFromDB` performs significant work to reconstruct the corpse's visual appearance. It looks up `PlayerInfo` based on race and class to set the display ID. It parses the `equipment_cache` string to set item display IDs for each equipment slot. This ensures the corpse looks like the player who died, including their gear.
*   **Position Validation**: Both `Create` and `LoadFromDB` validate the position using `IsPositionValid`. If the position is invalid (e.g., inside a wall or out of bounds), the corpse creation fails, and an error is logged. This prevents corrupted or impossible corpse states.
*   **Faction Reaction Logic**: `GetReactionTo` has a specific fallback chain. It prioritizes the online owner's reaction, then group membership, then a stored faction template, and finally defaults to neutral. This allows corpses to remain friendly to group members even if the owner is offline.
*   **Grid Management**: Corpses maintain a `GridPair` and `GridReference` for efficient spatial indexing. This is crucial for performance, allowing the server to quickly find corpses in a specific area for visibility checks or cleanup.
*   **Loot Integration**: The `Corpse` class contains a `Loot` object and pointers to `lootRecipient` and `lootForBody`. While the `Corpse` class itself doesn't implement loot logic, it provides the container and context for the `Loot` class to operate. The comment "remove insignia ONLY at BG" suggests special handling for battleground corpses, likely related to insignia drops.

## Member Reference

*   **`Corpse`**: Constructor initializing object type, time, and loot.
*   **`~Corpse`**: Destructor removing bones from map if applicable.
*   **`AddToWorld`**: Registers with ObjectAccessor and adds to world grid.
*   **`RemoveFromWorld`**: Removes from ObjectAccessor and world grid.
*   **`GetOwnerGuid`**: Returns the GUID of the player who owns the corpse.
*   **`GetGhostTime`**: Returns the timestamp of corpse creation.
*   **`Create`**: Simple GUID initialization overload.
*   **`ResetGhostTime`**: Updates the corpse creation timestamp to now.
*   **`GetType`**: Returns the corpse type enum.
*   **`GetName`**: Returns the string "Corpse".
*   **`Create#2`**: Full initialization from owner player data, including position validation and grid computation.
*   **`GetGrid`**: Returns the grid pair for the corpse's location.
*   **`SetGrid`**: Sets the grid pair for the corpse's location.
*   **`GetGridRef`**: Returns the grid reference for linked-list management.
*   **`SetFactionTemplate`**: Sets the faction template for reaction calculations.
*   **`GetFactionTemplate`**: Returns the current faction template.
*   **`SaveToDB`**: Persists corpse data to the database, excluding bones.
*   **`DeleteBonesFromWorld`**: Removes bone-type corpses from the world removal list.
*   **`DeleteFromDB`**: Deletes corpse record from the database, excluding bones.
*   **`LoadFromDB`**: Reconstructs corpse from database fields, including visual appearance.
*   **`IsVisibleForInState`**: Determines visibility based on distance and modifiers.
*   **`GetReactionTo`**: Calculates reputation rank towards a target.
*   **`IsHostileTo`**: Checks if reaction is hostile or lower.
*   **`IsFriendlyTo`**: Checks if reaction is friendly or higher.
*   **`IsExpired`**: Checks if corpse has exceeded its lifetime limit.
*   **`GetFactionTemplateId`**: Returns the ID of the current faction template.

---

<!-- machine-true, projected from graph.json -->

## Map — Corpse

*Source:* Corpse.cpp, Corpse.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| Corpse | ctor | Loot/Loot, WorldObject.Object/WorldObject | Map.Main/RemoveCorpses, ObjectMgr/LoadCorpses, Player.Main/CreateCorpse | — |
| ~Corpse | dtor | Map.Main/RemoveBones, WorldObject.Object/GetMap | — | — |
| AddToWorld | method | Object/AddToWorld, Object/IsInWorld, ObjectAccessor/AddObject | ObjectGridLoader/LoadHelper | — |
| RemoveFromWorld | method | Object/IsInWorld, Object/RemoveFromWorld, ObjectAccessor/RemoveObject | ObjectAccessor/RemoveCorpse, ObjectAccessor/~ObjectAccessor | — |
| GetOwnerGuid | method | — | LootMgr/GetLootTarget, Map.Main/RemoveCorpses, ObjectAccessor/AddCorpse, ObjectAccessor/RemoveCorpse, Spell.Effects/EffectSkinPlayerCorpse, Spell.Main/SetTargetMap | — |
| GetGhostTime | method | — | Player.Main/SendCorpseReclaimDelay, WorldSession.MiscHandler/HandleReclaimCorpseOpcode | — |
| Create | method | WorldObject.Object/_Create | Map.Main/RemoveCorpses | — |
| ResetGhostTime | method | — | Player.Main/BuildPlayerRepop | — |
| GetType | method | — | Map.Main/AddToGrid, Map.Main/RemoveFromGrid, ObjectAccessor/AddCorpse, ObjectAccessor/GetCorpseForPlayerGUID, ObjectAccessor/RemoveCorpse, Player.Main/SendCorpseReclaimDelay, WorldSession.MiscHandler/HandleReclaimCorpseOpcode | — |
| GetName | method | — | — | — |
| Create#2 | method | GridDefines/ComputeGridPair, Log.Main/Out, Object/GetObjectGuid, Object/SetGuidValue, Player.Main/GetName, WorldObject.Object/GetMap, WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/IsPositionValid, WorldObject.Object/Relocate#2, WorldObject.Object/SetFloatValue, WorldObject.Object/SetMap, WorldObject.Object/SetObjectScale, WorldObject.Object/_Create#2 | Player.Main/CreateCorpse | — |
| GetGrid | method | — | Map.Main/RemoveCorpses, ObjectAccessor/AddCorpsesToGrid | — |
| SetGrid | method | — | Map.Main/RemoveCorpses | — |
| GetGridRef | method | — | — | — |
| SetFactionTemplate | method | — | Map.Main/RemoveCorpses | — |
| GetFactionTemplate | method | — | — | — |
| SaveToDB | method | Database/Execute#2, Errors/PrintStacktraceAndThrow, Object/GetGUIDLow, ObjectGuid/GetCounter, WorldObject.Object/GetInstanceId, WorldObject.Object/GetMapId, WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ | Player.Main/CreateCorpse | — |
| DeleteBonesFromWorld | method | Errors/PrintStacktraceAndThrow, Log.Main/Out, Map.Main/GetCorpse, Object/GetGUIDLow, Object/GetObjectGuid, WorldObject.Object/AddObjectToRemoveList, WorldObject.Object/GetMap | — | — |
| DeleteFromDB | method | Database/CreateStatement, Errors/PrintStacktraceAndThrow, ObjectGuid/GetCounter, SqlStatementID/SqlStatementID | Map.Main/RemoveCorpses, ObjectAccessor/ConvertCorpseForPlayer | corpse |
| LoadFromDB | method | Field/GetCppString, Field/GetFloat, Field/GetUInt32, Field/GetUInt64, Field/GetUInt8, GridDefines/ComputeGridPair, Log.Main/Out, MapManager/GetContinentInstanceId, Object/GetGUIDLow, Object/GetGuidStr, Object/SetGuidValue, ObjectGuid/GetString, ObjectGuid/ObjectGuid#2, ObjectMgr/GetItemPrototype, ObjectMgr/GetPlayerInfo, shared_Util/GetUInt32ValueFromArray, shared_Util/StrSplit, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/IsPositionValid, WorldObject.Object/Relocate#2, WorldObject.Object/SetLocationInstanceId, WorldObject.Object/SetLocationMapId, WorldObject.Object/SetObjectScale, WorldObject.Object/SetUInt32Value, WorldObject.Object/_Create | ObjectMgr/LoadCorpses | — |
| IsVisibleForInState | method | Map.Main/GetVisibilityDistance, Object/IsInWorld, World/GetVisibleObjectGreyDistance, WorldObject.Object/GetMap, WorldObject.Object/GetVisibilityModifier, WorldObject.Object/IsWithinDist | — | — |
| GetReactionTo | method | Group/IsMember, Object/IsPlayer, Object/ToPlayer#2, ObjectMgr/GetPlayer, Player.Main/GetGroup#2, WorldObject.Object/GetFactionReactionTo, WorldObject.Object/GetReactionTo | — | — |
| IsHostileTo | method | — | — | — |
| IsFriendlyTo | method | — | GridNotifiers/operator(), WorldObject.Object/BuildValuesUpdate | — |
| IsExpired | method | World/getConfig#4 | Map.Main/RemoveOldBones, ObjectAccessor/RemoveOldCorpses | — |
| GetFactionTemplateId | method | — | — | — |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `corpse`: guid int(11) unsigned PK, player_guid int(11) unsigned, position_x float, position_y float, position_z float, orientation float, map int(11) unsigned, time bigint(20) unsigned, corpse_type tinyint(3) unsigned, instance int(11) unsigned

*`?` = nullable, `PK` = primary key column.*

