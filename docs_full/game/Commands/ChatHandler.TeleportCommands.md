<!-- provenance: boundary-bleed -->
# ChatHandler.TeleportCommands

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ChatHandler.TeleportCommands

## Purpose & Responsibilities

This unit implements the server-side logic for Game Master (GM) and administrative teleportation commands within the `wowvmangos` emulator. It provides a suite of methods attached to the `ChatHandler` class that allow authorized users to move themselves, other players, or entire groups to specific locations, entities, or coordinates.

The responsibilities include:
1.  **Parsing Input:** Interpreting command arguments, which may be raw numbers, text names, or complex shift-click links (e.g., `|Htele:id|h...|`).
2.  **Validation:** Checking security permissions (`HasLowerSecurity`), verifying target existence, ensuring coordinates are valid within the world geometry, and handling edge cases like active taxi flights or combat states.
3.  **Execution:** Invoking the core teleportation mechanics via `Player::TeleportTo`, managing recall positions, clearing taxi paths, and updating battle ground or instance bindings where necessary.
4.  **Feedback:** Sending system messages to the issuer and targets regarding success, failure, or pending actions.

## Member-by-Member Behavior

### Core Teleportation Helpers

**HandleGoHelper**
This is the central engine for most self-teleportation commands. It accepts a player pointer, map ID, X/Y coordinates, and optional pointers for Z and Orientation.
- If Z and Orientation are provided, it validates the full coordinate set using `MapManager::IsValidMapCoord`.
- If Z is missing, it validates X/Y first, then queries the terrain manager (`sTerrainMgr`) for the ground or water level at that location.
- Before teleporting, it checks if the player is flying a taxi. If so, it expires the motion master and clears taxi destinations. Otherwise, it saves the current position as a "recall" point.
- Finally, it calls `Player::TeleportTo`.

**HandleTeleCommand**
Teleports the issuing GM to a predefined teleport location stored in the `game_tele` table. It extracts the `GameTele` structure from the argument (supporting IDs, names, or links) and delegates to `HandleGoHelper`.

**HandleTeleAddCommand**
Adds a new entry to the in-memory `game_tele` store. It captures the issuing player's current position, orientation, and map ID, associating them with the name provided in the arguments. It checks for duplicates before adding.

**HandleTeleDelCommand**
Removes an entry from the in-memory `game_tele` store by name.

### Group and Multi-Player Teleportation

**HandleTeleGroupCommand**
Teleports every member of a selected player's group to a specified `game_tele` location.
- It iterates through the group members.
- For each member, it checks security, ensures they aren't already teleporting, and stops any active taxi flight.
- It sends notifications to both the GM and the target players.
- It uses `HandleGoHelper` implicitly via `Player::TeleportTo(*tele)` (note: the code calls `pl->TeleportTo(*tele)` directly, bypassing `HandleGoHelper`'s terrain check, relying on the pre-stored valid coordinates in `GameTele`).

**HandleGroupgoCommand**
Summons all members of a target player's group to the GM's current location.
- Enforces strict rules if the GM is in an instance: the GM must be the group leader, and the group must be in the same instance or the world.
- Prevents summoning from one instance to another if they are different instances.
- Iterates group members, checks security, stops taxi flights, saves recall positions, and teleports them to the GM's coordinates.

**HandleNamegoCommand**
Summons a specific target player (online or offline) to the GM's location.
- **Online:** Checks security, stops taxi, saves recall, and teleports. If the GM is in a Battle Ground, it sets the target's Battle Ground ID to match the GM's, allowing them to enter the BG instance.
- **Offline:** Updates the player's saved position in the database (`Player::SavePositionInDB`) so they spawn at the GM's location upon login.

**HandleGonameCommand**
Teleports the GM to a specific target player.
- **Online:** Handles complex instance binding logic. If the target is in a dungeon, the GM must be in the same group or have GM mode. It manages `InstancePlayerBind` to ensure the GM enters the correct instance version. If the target is in a Battle Ground, it verifies the GM isn't already in a different BG. It teleports the GM slightly above the target (`z + 5.0f`) facing the target.
- **Offline:** Loads the player's last known position from the database and teleports the GM there.

**HandleRecallCommand**
Teleports a target player back to their last saved "recall" position (usually set before a teleport or taxi flight). It retrieves the coordinates from `Player::GetRecallPosition` and uses `HandleGoHelper`.

### Entity-Based Teleportation

**HandleGoCreatureCommand**
Teleports the GM to a creature. Supports three input modes:
1.  **GUID:** Direct lookup of creature data.
2.  **Entry ID:** Searches for a creature with that template entry near the GM (using `FindCreatureData`).
3.  **Name:** Performs a SQL query on the `creature` and `creature_template` tables to find a matching name, then resolves the closest instance to the GM.
- If the creature is currently spawned in the GM's map, it uses the live creature's position; otherwise, it uses the spawn point from the database.

**HandleGoObjectCommand**
Teleports the GM to a GameObject. Similar to creatures, it supports GUID, Entry ID, and Name lookups. It queries the `gameobject` and `gameobject_template` tables for name searches.

**HandleGoTargetCommand**
Teleports the GM to the unit currently selected in the client UI. It verifies the target is in the same map and uses `NearTeleportTo` (or `NearLandTo` depending on client build) to place the GM adjacent to the target.

**HandleGocorpseCommand**
Teleports the GM to the corpse of a specified player. It looks up the corpse object via `ObjectAccessor`. Note: The GM will arrive at the coordinates but may not see the corpse if not in the same instance/group context.

### Coordinate and Location-Based Teleportation

**HandleGoCommand**
Teleports to raw X, Y, Z coordinates. Supports both raw numeric arguments and shift-click location links. Defaults to the current map if no map ID is provided.

**HandleGoXYCommand**
Teleports to X, Y coordinates on the current map (or specified map). Z is calculated from terrain.

**HandleGoXYZCommand**
Teleports to X, Y, Z coordinates.

**HandleGoXYZOCommand**
Teleports to X, Y, Z, and Orientation.

**HandleGoGridCommand**
Teleports to the center of a specific grid cell. Converts grid indices (X, Y) to world coordinates using `SIZE_OF_GRIDS` and `CENTER_GRID_ID` constants.

**HandleGoZoneXYCommand**
Teleports to X, Y coordinates within a specific Zone/Area.
- Validates that X/Y are between 0 and 100.
- Resolves the Area Entry to its parent Zone if necessary.
- Converts zone-relative coordinates to world coordinates using `Zone2MapCoordinates`.
- Prevents teleportation to instanced maps.

**HandleGoTaxinodeCommand**
Teleports to a specific Taxi Node by ID. Validates that the node has non-zero coordinates.

**HandleGoGraveyardCommand**
Teleports to a specific Graveyard by ID. Looks up the `WorldSafeLocsEntry` and teleports there.

**HandleGoTriggerCommand**
Teleports to an Area Trigger.
- If the argument includes "target", it teleports to the trigger's destination (defined in `area_trigger_teleport`).
- Otherwise, it teleports to the trigger's own location.

### Relative Movement

**HandleGoForwardCommand**
Moves the GM forward by a specified distance (default 10.0f) relative to their current orientation. Uses `NearLandTo` to ensure they land on solid ground.

**HandleGoUpCommand**
Moves the GM upward by a specified distance (default 10.0f).

**HandleGoRelativeCommand**
Moves the GM by relative offsets (Forward/Back, Left/Right, Up/Down). Parses three floats from the arguments.

### Utility and State Management

**HandleStartCommand**
Casts spell 7355 ("Stuck") on the GM. This is likely a debug or convenience spell to simulate being stuck or trigger specific behaviors. It prevents execution if the GM is in combat or flying.

**HandleUnstuckCommand**
A player-facing command (not just GM) to escape stuck situations.
- **Alive:** Casts spell 20939 (likely a self-resurrection or unstuck spell).
- **Dead:** Adds a "Resurrection Sickness" aura, applies a 1-hour cooldown to the spell, and teleports the player to the nearest graveyard. If no graveyard is found (void), it defaults to Westfall (Alliance) or Barrens (Horde).
- Logs the usage for moderation purposes.

## Cross-Unit Boundaries

This unit relies heavily on several other subsystems:

1.  **ChatHandler.Chat (Same Class, Different Partial):**
    -   **Calls:** `ExtractGameTeleFromLink`, `ExtractPlayerTarget`, `ExtractKeyFromLink`, `ExtractUInt32`, `ExtractFloat`, `SendSysMessage`, `PSendSysMessage`, `SetSentErrorMessage`, `HasLowerSecurity`, `GetNameLink`, `GetSelectedPlayer`, `GetSelectedUnit`.
    -   **Why:** To parse command arguments, handle security checks, and provide user feedback. These are utility methods defined in the main `ChatHandler` implementation.

2.  **WorldSession.Main:**
    -   **Calls:** `GetPlayer`.
    -   **Why:** To retrieve the `Player` object associated with the session issuing the command.

3.  **Player.Main:**
    -   **Calls:** `TeleportTo`, `SaveRecallPosition`, `GetRecallPosition`, `IsBeingTeleported`, `GetGroup`, `GetSession`, `GetTaxi`, `IsTaxiFlying`, `GetMotionMaster`, `GetMap`, `GetMapId`, `GetPosition`, `GetOrientation`, `GetZoneId`, `SavePositionInDB`, `LoadPositionFromDB`, `GetBoundInstance`, `UnbindInstance`, `BindToInstance`, `SetBattleGroundId`, `SetBattleGroundEntryPoint`, `GetBattleGroundId`, `GetBattleGroundTypeId`, `GetSmartInstanceBindingMode`, `IsAlive`, `IsInCombat`, `InBattleGround`, `GetDeathState`, `GetLevel`, `IsSpellReady`, `CastSpell`, `AddAura`, `AddCooldown`, `GetName`, `GetTeam`, `GetTeamId`, `GetGUIDLow`.
    -   **Why:** To manipulate the player's state, position, group membership, instance bindings, and battle ground status.

4.  **ObjectMgr:**
    -   **Calls:** `GetGameTele`, `AddGameTele`, `DeleteGameTele`, `GetCreatureData`, `GetCreatureDataPair`, `GetCreatureTemplate`, `DoCreatureData`, `GetGOData`, `GetGODataPair`, `GetGameObjectTemplate`, `DoGOData`, `GetAreaTrigger`, `GetAreaTriggerTeleport`, `GetTaxiNodeEntry`, `GetClosestGraveYard`, `GetWorldSafeLocFacing`, `GetResult`.
    -   **Why:** To access static data stores for teleports, creatures, game objects, area triggers, and taxi nodes.

5.  **Database:**
    -   **Calls:** `WorldDatabase.PQuery`, `WorldDatabase.escape_string`.
    -   **Why:** Specifically in `HandleGoCreatureCommand` and `HandleGoObjectCommand` to search for entities by name when a direct ID/GUID is not provided.

6.  **MapManager / GridMap:**
    -   **Calls:** `IsValidMapCoord`, `LoadTerrain`, `GetWaterOrGroundLevel`.
    -   **Why:** To validate coordinates and determine the correct Z-height for teleportation.

7.  **Group / GroupReference:**
    -   **Calls:** `GetFirstMember`, `GetLeaderGuid`, `IsLeader`, `GetBoundInstance`, `BindToInstance`.
    -   **Why:** To iterate through group members and manage instance bindings for groups.

8.  **Unit.Main:**
    -   **Calls:** `GetMotionMaster`, `IsTaxiFlying`, `NearLandTo`, `GetRelativePositions`, `GetOrientation`.
    -   **Why:** To handle movement mechanics and relative positioning calculations.

9.  **SpellCaster / SpellMgr:**
    -   **Calls:** `CastSpell`, `IsSpellReady`, `GetSpellEntry`.
    -   **Why:** In `HandleUnstuckCommand` and `HandleStartCommand` to cast spells programmatically.

10. **ObjectAccessor:**
    -   **Calls:** `GetCorpseForPlayerGUID`.
    -   **Why:** In `HandleGocorpseCommand` to locate a player's corpse object.

11. **Log.Main:**
    -   **Calls:** `Out`.
    -   **Why:** In `HandleUnstuckCommand` to log the event for administrative review.

## Data Model

This unit interacts with two database tables directly via SQL queries in `HandleGoCreatureCommand` and `HandleGoObjectCommand`. Most other data (teleports, area triggers, etc.) is accessed via in-memory caches populated by `ObjectMgr`.

### `creature`
Used in `HandleGoCreatureCommand` when searching by name.
-   **Columns Accessed:** `guid`, `id` (joined with `creature_template.entry`), `name` (from `creature_template`).
-   **Usage:** The query `SELECT guid FROM creature, creature_template WHERE creature.id = creature_template.entry AND creature_template.name LIKE '%...%'` finds all spawns matching a name pattern. The results are then filtered by proximity to the GM.

### `gameobject`
Used in `HandleGoObjectCommand` when searching by name.
-   **Columns Accessed:** `guid`, `id` (joined with `gameobject_template.entry`), `name` (from `gameobject_template`).
-   **Usage:** Similar to creatures, it finds spawns matching a name pattern.

## Notable Implementation Details

1.  **Taxi Flight Handling:** Almost all teleport commands check `IsTaxiFlying()`. If true, they explicitly expire the motion master and clear taxi destinations before teleporting. This prevents desynchronization or crashes caused by teleporting while the client expects a taxi path animation.

2.  **Recall Position Logic:** Teleport commands generally save the player's current position as a "recall" point *unless* they are flying a taxi. This allows players to return to where they were before the GM intervention. `HandleRecallCommand` utilizes this saved data.

3.  **Instance Binding Complexity:** `HandleGonameCommand` contains significant logic to handle dungeon instances. It checks if the GM is bound to an instance, if the target is bound, and if they are in the same group. It dynamically binds the GM to the target's instance if necessary, ensuring the GM enters the correct version of the dungeon.

4.  **Battle Ground Integration:** `HandleNamegoCommand` and `HandleGonameCommand` handle Battle Grounds specially. Summoning a player to a GM in a BG requires setting the target's BG ID. Teleporting to a player in a BG requires checking that the GM isn't already in a conflicting BG.

5.  **Offline Player Support:** `HandleNamegoCommand` and `HandleGonameCommand` support offline targets. For summoning (`Namego`), it updates the database record so the player spawns at the GM's location. For teleporting to (`Goname`), it loads the last known position from the database.

6.  **Name Search Fallback:** When searching for creatures or objects by name, the code performs a SQL `LIKE` query. If multiple matches are found, it uses a helper functor (`FindCreatureData`/`FindGOData`) to find the one closest to the GM's current position. This ensures predictability in large worlds with duplicate names.

7.  **Coordinate Validation:** `HandleGoHelper` strictly validates coordinates. If Z is not provided, it calculates it from the terrain. If the resulting coordinates are invalid (e.g., outside map bounds), the teleport is rejected.

8.  **Unstuck Command Safety:** `HandleUnstuckCommand` has multiple safeguards: it cannot be used in combat, in a BG, while flying, or if dead/corpse. It also enforces a level requirement (>= 10) and a cooldown on the underlying spell.

## Member Reference

**HandleTeleCommand**: Teleports the GM to a named teleport location from the `game_tele` store. Parses ID/name/link, validates existence, and calls `HandleGoHelper`.

**HandleTeleAddCommand**: Adds the GM's current position as a new named teleport entry to the `game_tele` store. Checks for name collisions.

**HandleTeleDelCommand**: Removes a named teleport entry from the `game_tele` store.

**HandleTeleGroupCommand**: Teleports all members of a selected player's group to a specified `game_tele` location. Iterates group, checks security, stops taxi, and teleports each member.

**HandleGroupgoCommand**: Summons all members of a target player's group to the GM's location. Enforces instance leadership rules and prevents cross-instance summoning.

**HandleGoTriggerCommand**: Teleports the GM to an Area Trigger's location or its destination (if "target" is specified).

**HandleGoGraveyardCommand**: Teleports the GM to a specified Graveyard ID.

**HandleGoCreatureCommand**: Teleports the GM to a creature identified by GUID, Entry ID, or Name. Queries DB for name searches and resolves closest spawn.

**HandleGoObjectCommand**: Teleports the GM to a GameObject identified by GUID, Entry ID, or Name. Queries DB for name searches.

**HandleTeleNameCommand**: Teleports a specific target player (online or offline) to a named `game_tele` location. Updates DB for offline players.

**HandleGoHelper**: Central helper for teleporting a player to coordinates. Validates coords, calculates Z if missing, handles taxi flight cleanup, saves recall position, and executes teleport.

**HandleGoTargetCommand**: Teleports the GM to the currently selected unit in the UI.

**HandleGoTaxinodeCommand**: Teleports the GM to a specified Taxi Node ID.

**HandleGoCommand**: Teleports the GM to raw X, Y, Z coordinates or a location link.

**HandleGoXYCommand**: Teleports the GM to X, Y coordinates on the current/specified map. Z is derived from terrain.

**HandleGoXYZCommand**: Teleports the GM to X, Y, Z coordinates.

**HandleGoXYZOCommand**: Teleports the GM to X, Y, Z, and Orientation.

**HandleGoZoneXYCommand**: Teleports the GM to X, Y coordinates within a specific Zone. Converts zone-relative coords to world coords.

**HandleGoGridCommand**: Teleports the GM to the center of a specified grid cell.

**HandleGoForwardCommand**: Moves the GM forward by a specified distance relative to their orientation.

**HandleGoUpCommand**: Moves the GM upward by a specified distance.

**HandleGoRelativeCommand**: Moves the GM by relative offsets (Forward/Back, Left/Right, Up/Down).

**HandleStartCommand**: Casts spell 7355 on the GM. Blocks if in combat or flying.

**HandleUnstuckCommand**: Allows a player to escape stuck situations. Casts a spell if alive, or teleports to nearest graveyard if dead. Logs usage.

**HandleRecallCommand**: Teleports a target player to their last saved recall position.

**HandleNamegoCommand**: Summons a target player (online/offline) to the GM's location. Handles BG entry for online targets.

**HandleGonameCommand**: Teleports the GM to a target player. Handles complex instance binding and BG checks.

**HandleGocorpseCommand**: Teleports the GM to the corpse of a specified player.

---

<!-- machine-true, projected from graph.json -->

## Map — ChatHandler.TeleportCommands

*Source:* TeleportCommands.cpp, Chat.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| HandleTeleCommand | method | ChatHandler.Chat/ExtractGameTeleFromLink, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, WorldSession.Main/GetPlayer | — | — |
| HandleTeleAddCommand | method | ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, ObjectMgr/AddGameTele, ObjectMgr/GetGameTele, WorldObject.Object/GetMapId, WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldSession.Main/GetPlayer | — | — |
| HandleTeleDelCommand | method | ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, ObjectMgr/DeleteGameTele | — | — |
| HandleTeleGroupCommand | method | ChatHandler.Chat/ExtractGameTeleFromLink, ChatHandler.Chat/GetNameLink, ChatHandler.Chat/GetSelectedPlayer, ChatHandler.Chat/HasLowerSecurity, ChatHandler.Chat/needReportToTarget, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Group/GetFirstMember, GroupReference/next, MotionMaster/MovementExpired, Player.Main/GetGroup, Player.Main/GetSession, Player.Main/GetTaxi, Player.Main/IsBeingTeleported, Player.Main/PSendSysMessage#2, Player.Main/SaveRecallPosition, PlayerTaxi/ClearTaxiDestinations, Unit.Main/GetMotionMaster, Unit.Main/IsTaxiFlying | — | — |
| HandleGroupgoCommand | method | ChatHandler.Chat/ExtractPlayerTarget, ChatHandler.Chat/GetNameLink, ChatHandler.Chat/HasLowerSecurity, ChatHandler.Chat/needReportToTarget, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Group/GetFirstMember, Group/GetLeaderGuid, GroupReference/next, Map.Main/GetInstanceId, Map.Main/Instanceable, MotionMaster/MovementExpired, Object/GetObjectGuid, ObjectGuid/operator!=, Player.Main/GetGroup, Player.Main/GetSession, Player.Main/GetTaxi, Player.Main/IsBeingTeleported, Player.Main/PSendSysMessage#2, Player.Main/SaveRecallPosition, Player.Main/TeleportTo, PlayerTaxi/ClearTaxiDestinations, Unit.Main/GetMotionMaster, Unit.Main/IsTaxiFlying, WorldObject.Object/GetMap, WorldObject.Object/GetMapId, WorldObject.Object/GetOrientation, WorldObject.Object/GetPosition#2, WorldSession.Main/GetPlayer | — | — |
| HandleGoTriggerCommand | method | ChatHandler.Chat/ExtractKeyFromLink, ChatHandler.Chat/ExtractLiteralArg, ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, ObjectMgr/GetAreaTrigger, ObjectMgr/GetAreaTriggerTeleport, WorldSession.Main/GetPlayer | — | — |
| HandleGoGraveyardCommand | method | ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, WorldSession.Main/GetPlayer | — | — |
| HandleGoCreatureCommand | method | ChatHandler.Chat/ExtractKeyFromLink, ChatHandler.Chat/ExtractKeyFromLink#2, ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, CreatureData/GetObjectGuid, Database/escape_string, Database/PQuery, Field/GetUInt32, FindCreatureData/FindCreatureData, Map.Main/GetCreature, ObjectMgr/GetCreatureData, ObjectMgr/GetCreatureDataPair, ObjectMgr/GetCreatureTemplate, ObjectMgr/GetResult, ObjectMgr/operator(), QueryResult/Fetch, QueryResult/NextRow, WorldObject.Object/GetMap, WorldObject.Object/GetPosition#2, WorldSession.Main/GetPlayer | — | creature |
| HandleGoObjectCommand | method | ChatHandler.Chat/ExtractKeyFromLink, ChatHandler.Chat/ExtractKeyFromLink#2, ChatHandler.Chat/ExtractUInt32, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Database/escape_string, Database/PQuery, Field/GetUInt32, FindGOData/FindGOData, ObjectMgr/GetGameObjectTemplate, ObjectMgr/GetGOData, ObjectMgr/GetGODataPair, ObjectMgr/GetResult#2, ObjectMgr/operator()#2, QueryResult/Fetch, QueryResult/NextRow, WorldSession.Main/GetPlayer | — | gameobject |
| HandleTeleNameCommand | method | ChatHandler.Chat/ExtractGameTeleFromLink, ChatHandler.Chat/ExtractOptNotLastArg, ChatHandler.Chat/ExtractPlayerTarget, ChatHandler.Chat/GetMangosString, ChatHandler.Chat/GetNameLink#2, ChatHandler.Chat/HasLowerSecurity, ChatHandler.Chat/needReportToTarget, ChatHandler.Chat/playerLink, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, ObjectGuid/ObjectGuid, Player.Main/IsBeingTeleported, Player.Main/PSendSysMessage#2, Player.Main/SavePositionInDB, TerrainManager/GetZoneId | — | — |
| HandleGoHelper | method | ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, GridMap/GetWaterOrGroundLevel#2, GridMap/LoadTerrain, MapManager/IsValidMapCoord#2, MapManager/IsValidMapCoord#4, MotionMaster/MovementExpired, Player.Main/GetTaxi, Player.Main/SaveRecallPosition, Player.Main/TeleportTo, PlayerTaxi/ClearTaxiDestinations, Unit.Main/GetMotionMaster, Unit.Main/IsTaxiFlying, WorldObject.Object/GetOrientation | — | — |
| HandleGoTargetCommand | method | ChatHandler.Chat/GetSelectedUnit, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, ObjectGuid/operator!, Player.Main/GetSelectionGuid, WorldObject.Object/GetPosition#3, WorldObject.Object/IsInMap, WorldSession.Main/GetPlayer | — | — |
| HandleGoTaxinodeCommand | method | ChatHandler.Chat/ExtractUint32KeyFromLink, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, ObjectMgr/GetTaxiNodeEntry, WorldSession.Main/GetPlayer | — | — |
| HandleGoCommand | method | ChatHandler.Chat/ExtractFloat, ChatHandler.Chat/ExtractLocationFromLink, ChatHandler.Chat/ExtractOptUInt32, WorldObject.Object/GetMapId, WorldSession.Main/GetPlayer | — | — |
| HandleGoXYCommand | method | ChatHandler.Chat/ExtractFloat, ChatHandler.Chat/ExtractOptUInt32, WorldObject.Object/GetMapId, WorldSession.Main/GetPlayer | — | — |
| HandleGoXYZCommand | method | ChatHandler.Chat/ExtractFloat, ChatHandler.Chat/ExtractOptUInt32, WorldObject.Object/GetMapId, WorldSession.Main/GetPlayer | — | — |
| HandleGoXYZOCommand | method | ChatHandler.Chat/ExtractFloat, ChatHandler.Chat/ExtractOptUInt32, WorldObject.Object/GetMapId, WorldSession.Main/GetPlayer | — | — |
| HandleGoZoneXYCommand | method | AreaEntry/GetById, AreaEntry/IsZone, ChatHandler.Chat/ExtractFloat, ChatHandler.Chat/ExtractUint32KeyFromLink, ChatHandler.Chat/GetSessionDbLocaleIndex, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, DBCStores/Zone2MapCoordinates, MapEntry/Instanceable, ObjectMgr/GetAreaLocaleString, WorldObject.Object/GetZoneId, WorldSession.Main/GetPlayer | — | — |
| HandleGoGridCommand | method | ChatHandler.Chat/ExtractFloat, ChatHandler.Chat/ExtractOptUInt32, WorldObject.Object/GetMapId, WorldSession.Main/GetPlayer | — | — |
| HandleGoForwardCommand | method | Unit.Main/NearLandTo, WorldObject.Object/GetOrientation, WorldObject.Object/GetRelativePositions#2, WorldSession.Main/GetPlayer | — | — |
| HandleGoUpCommand | method | Unit.Main/NearLandTo, WorldObject.Object/GetOrientation, WorldObject.Object/GetRelativePositions#2, WorldSession.Main/GetPlayer | — | — |
| HandleGoRelativeCommand | method | ChatHandler.Chat/PSendSysMessage, Unit.Main/NearLandTo, WorldObject.Object/GetOrientation, WorldObject.Object/GetRelativePositions#2, WorldSession.Main/GetPlayer | — | — |
| HandleStartCommand | method | ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, SpellCaster/CastSpell#2, Unit.Main/IsInCombat, Unit.Main/IsTaxiFlying, WorldSession.Main/GetPlayer | — | — |
| HandleUnstuckCommand | method | ChatHandler.Chat/SendSysMessage#2, Log.Main/Out, Object/GetGUIDLow, ObjectMgr/GetClosestGraveYard, ObjectMgr/GetWorldSafeLocFacing, Player.Main/AddCooldown, Player.Main/GetName, Player.Main/GetTeam, Player.Main/GetTeamId, Player.Main/InBattleGround, Player.Main/TeleportTo, SpellCaster/CastSpell#2, SpellCaster/IsSpellReady#2, SpellMgr/GetSpellEntry, SpellMgr/Instance, Unit.Main/AddAura, Unit.Main/GetDeathState, Unit.Main/GetLevel, Unit.Main/IsAlive, Unit.Main/IsInCombat, Unit.Main/IsTaxiFlying, WorldObject.Object/GetMapId, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldSession.Main/GetPlayer | — | — |
| HandleRecallCommand | method | ChatHandler.Chat/ExtractPlayerTarget, ChatHandler.Chat/GetNameLink, ChatHandler.Chat/HasLowerSecurity, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Player.Main/GetRecallPosition, Player.Main/IsBeingTeleported | — | — |
| HandleNamegoCommand | method | ChatHandler.Chat/ExtractPlayerTarget, ChatHandler.Chat/GetMangosString, ChatHandler.Chat/HasLowerSecurity, ChatHandler.Chat/needReportToTarget, ChatHandler.Chat/playerLink, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, Map.Main/IsBattleGround, MotionMaster/MovementExpired, Object/GetObjectGuid, ObjectGuid/ObjectGuid, ObjectGuid/operator==, Player.Main/GetBattleGroundId, Player.Main/GetBattleGroundTypeId, Player.Main/GetName, Player.Main/GetTaxi, Player.Main/IsBeingTeleported, Player.Main/PSendSysMessage#2, Player.Main/SavePositionInDB, Player.Main/SaveRecallPosition, Player.Main/SetBattleGroundEntryPoint, Player.Main/SetBattleGroundId, Player.Main/TeleportTo, PlayerTaxi/ClearTaxiDestinations, Unit.Main/GetMotionMaster, Unit.Main/IsTaxiFlying, WorldObject.Object/GetMap, WorldObject.Object/GetMapId, WorldObject.Object/GetOrientation, WorldObject.Object/GetPosition#2, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/GetZoneId, WorldSession.Main/GetPlayer | — | — |
| HandleGonameCommand | method | ChatHandler.Chat/ExtractPlayerTarget, ChatHandler.Chat/GetNameLink#2, ChatHandler.Chat/needReportToTarget, ChatHandler.Chat/playerLink, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, DungeonPersistentState/CanReset, game_Group_Group/BindToInstance, game_Group_Group/GetBoundInstance, Group/IsLeader, Map.Main/GetPersistanceState#2, Map.Main/IsBattleGround, Map.Main/IsDungeon, MapPersistentStateMgr/GetInstanceId, MotionMaster/MovementExpired, Object/GetObjectGuid, ObjectGuid/ObjectGuid, ObjectGuid/operator==, Player.Main/BindToInstance, Player.Main/GetBattleGroundId, Player.Main/GetBattleGroundTypeId, Player.Main/GetBoundInstance, Player.Main/GetGroup, Player.Main/GetSmartInstanceBindingMode, Player.Main/GetTaxi, Player.Main/LoadPositionFromDB, Player.Main/PSendSysMessage#2, Player.Main/SaveRecallPosition, Player.Main/SetBattleGroundEntryPoint, Player.Main/SetBattleGroundId, Player.Main/TeleportTo, Player.Main/UnbindInstance#2, PlayerTaxi/ClearTaxiDestinations, Unit.Main/GetMotionMaster, Unit.Main/IsTaxiFlying, WorldObject.Object/GetAngle, WorldObject.Object/GetMap, WorldObject.Object/GetMapId, WorldObject.Object/GetPosition#2, WorldSession.Main/GetPlayer | PartyBotAI/UpdateAI | — |
| HandleGocorpseCommand | method | ChatHandler.Chat/ExtractPlayerTarget, ChatHandler.Chat/PSendSysMessage#2, ChatHandler.Chat/SetSentErrorMessage, ObjectAccessor/GetCorpseForPlayerGUID, ObjectGuid/ObjectGuid, WorldObject.Object/GetMapId, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldSession.Main/GetPlayer | — | — |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `creature`: guid int(10) unsigned PK, id mediumint(8) unsigned, id2 mediumint(8) unsigned, id3 mediumint(8) unsigned, id4 mediumint(8) unsigned, id5 mediumint(8) unsigned, map smallint(5) unsigned, position_x float, position_y float, position_z float, orientation float, spawntimesecsmin int(10) unsigned, spawntimesecsmax int(10) unsigned, wander_distance float, health_percent float, mana_percent float unsigned, movement_type tinyint(3) unsigned, spawn_flags int(10) unsigned, visibility_mod float?, patch_min tinyint(3) unsigned, patch_max tinyint(3) unsigned
- `gameobject`: guid int(10) unsigned PK, id mediumint(8) unsigned, map smallint(5) unsigned, position_x float, position_y float, position_z float, orientation float, rotation0 float, rotation1 float, rotation2 float, rotation3 float, spawntimesecsmin int(11), spawntimesecsmax int(11), animprogress tinyint(3) unsigned, state tinyint(3) unsigned, spawn_flags int(10) unsigned, visibility_mod float?, patch_min tinyint(3) unsigned, patch_max tinyint(3) unsigned

*`?` = nullable, `PK` = primary key column.*


---

<!-- verify: boundary-bleed | foreign: ChatHandler -->
