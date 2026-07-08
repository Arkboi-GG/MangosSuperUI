<!-- provenance: verbose -->
# ZoneScriptMgr

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ZoneScriptMgr

**ZoneScriptMgr** is the singleton manager for zone-specific gameplay logic, primarily coordinating outdoor PvP events and custom zone behaviors. It acts as a registry and dispatcher, bridging core engine events (player movement, spells, game object interactions) with specialized `ZoneScript` implementations.

The manager maintains two collections:
1.  **`m_ZoneScriptsMap`**: Maps Zone IDs to `ZoneScript` instances for fast lookup during player zone transitions.
2.  **`m_ZoneScriptsSet`**: A set of all active `ZoneScript` instances for global updates and broadcast-style event dispatching.

It initializes scripts when maps load, cleans them up on map crashes, and routes player actions to the appropriate script handlers.

## Member-by-Member Behavior

### Initialization and Lifecycle

**ZoneScriptMgr** and **~ZoneScriptMgr** construct and destroy the singleton. The destructor deletes all `ZoneScript` objects in `m_ZoneScriptsSet` and all `ZoneScript_Script` objects in `m_ZoneScripts_Scripts`.

**InitZoneScripts** calls `RegisterZoneScripts()` to populate the script registry. It is called by `World/SetInitialWorldSettings` during server startup.

**InitMapZoneScripts** initializes scripts for a specific map. It iterates `m_ZoneScripts_Scripts`, filtering by `mapId`. For matching scripts, it retrieves the `ZoneScript` via `GetZoneScript()`, sets the map via `SetMap()`, and calls `SetupZoneScript()`. If setup succeeds, the script is added to `m_ZoneScriptsSet`. Failures or null pointers are logged via `Log.Main/Out`.

**MapLoaded** is an inline wrapper calling `InitMapZoneScripts`. It is triggered by `MapManager/CreateMap` and `MapManager/CreateTestMap`.

**AddScript** registers a `ZoneScript_Script` into `m_ZoneScripts_Scripts`. It is called by `OutdoorPvPEP/AddSC_outdoorpvp_ep` and `OutdoorPvPSI/AddSC_outdoorpvp_si`.

**OnMapCrashed** removes scripts associated with a crashed map from both `m_ZoneScriptsSet` and `m_ZoneScriptsMap`. It iterates and erases elements, resetting iterators to `begin()` after each erase to handle invalidation safely. It is called by `MapManager/Update`.

### Player Zone Events

**HandlePlayerEnterZone** looks up the `ZoneScript` for `zoneid` in `m_ZoneScriptsMap`. If found and the player is not already tracked (`HasPlayer()` returns false), it calls `OnPlayerEnter()` on the script. It is called by `Player.Main/UpdateZone`.

**HandlePlayerLeaveZone** looks up the script for `zoneid`. If found and the player is tracked (`HasPlayer()` returns true), it calls `OnPlayerLeave()`. This guard prevents duplicate calls from teleports or removals. It is called by `Player.Main/RemoveFromWorld` and `Player.Main/UpdateZone`.

**AddZone** registers a `ZoneScript` handle for a `zoneid` in `m_ZoneScriptsMap`. It is called by `ZoneScript/RegisterZone`.

**GetZoneScriptToZoneId** returns the `ZoneScript` for `zoneid` from `m_ZoneScriptsMap`, or `nullptr`. It is called by `Player.Main/GetZoneScript`.

**GetZoneScript** returns the `ZoneScript` for `zoneId` from `m_ZoneScriptsMap`, or `nullptr`. It is called by `WorldObject.Object/SetZoneScript`.

### Global Event Dispatching

**Update** accumulates time in `m_UpdateTimer`. When exceeding `OUTDOORPVP_OBJECTIVE_UPDATE_INTERVAL` (1000 ms), it calls `Update()` on all scripts in `m_ZoneScriptsSet` and resets the timer. It is called by `World/Update`.

**HandleCustomSpell**, **HandleOpenGo**, **HandleGossipOption**, and **HandleDropFlag** iterate through `m_ZoneScriptsSet` to dispatch events. They stop at the first script that handles the event (returns `true` or executes logic). These methods allow scripts to intercept spells, game object openings, gossip options, and flag drops.

## Cross-Unit Boundaries

*   **ZoneScript**: The primary dependency. `ZoneScriptMgr` stores `ZoneScript` pointers and delegates logic (enter/leave, updates, spells) to them. It uses `ZoneScript_Script` as a factory interface.
*   **MapManager**: Triggers `MapLoaded` on map creation and `OnMapCrashed` on map removal/crash.
*   **Player**: Calls `HandlePlayerEnterZone` and `HandlePlayerLeaveZone` during zone updates and world removal. Queries scripts via `GetZoneScriptToZoneId`.
*   **World**: Calls `InitZoneScripts` at startup and `Update` periodically.
*   **WorldObject/Object**: Calls `GetZoneScript` to associate scripts with objects.
*   **OutdoorPvPEP/OutdoorPvPSI**: Call `AddScript` to register their specific zone scripts.
*   **Log**: Used for error and status messages during initialization.

## Data Model

ZoneScriptMgr does not interact with any database tables. All configuration is in-memory.

## Notable Implementation Details

*   **Iterator Invalidation in OnMapCrashed**: `OnMapCrashed` resets iterators to `begin()` after erasing elements from `std::set` and `std::map`. This is safe but inefficient (O(N^2)); however, map crashes are rare events.
*   **Duplicate Event Guards**: `HandlePlayerEnterZone` and `HandlePlayerLeaveZone` check `HasPlayer()` to prevent duplicate processing caused by teleportation or multiple zone update triggers.
*   **Short-Circuiting Dispatch**: Event handlers (`HandleCustomSpell`, etc.) stop iterating after the first script handles the event. Priority is determined by `std::set` iteration order.
*   **Memory Ownership**: `ZoneScriptMgr` owns `ZoneScript` objects created by `ZoneScript_Script` factories and deletes them. It does not own `ZoneScript_Script` objects, which are managed by registering modules.

## Member Reference

**ZoneScriptMgr**  
Constructor. Initializes `m_UpdateTimer` to 0.

**~ZoneScriptMgr**  
Destructor. Deletes all `ZoneScript` objects in `m_ZoneScriptsSet` and all `ZoneScript_Script` objects in `m_ZoneScripts_Scripts`.

**InitZoneScripts**  
Calls `RegisterZoneScripts()` to populate the script registry. Called by `World/SetInitialWorldSettings`.

**InitMapZoneScripts**  
Iterates `m_ZoneScripts_Scripts`, filters by `mapId`, instantiates `ZoneScript` objects, sets their map, calls `SetupZoneScript()`, and adds successful ones to `m_ZoneScriptsSet`. Logs errors for null pointers or setup failures. Called by `MapLoaded`.

**MapLoaded**  
Inline wrapper that calls `InitMapZoneScripts(mapId, pMap)`. Called by `MapManager/CreateMap` and `MapManager/CreateTestMap`.

**AddZone**  
Adds a `ZoneScript` handle to `m_ZoneScriptsMap` for a given `zoneid`. Called by `ZoneScript/RegisterZone`.

**HandlePlayerEnterZone**  
Looks up the script for `zoneid`. If found and the player is not already tracked, calls `OnPlayerEnter()` on the script. Called by `Player.Main/UpdateZone`.

**AddScript**  
Adds a `ZoneScript_Script` pointer to `m_ZoneScripts_Scripts`. Called by `OutdoorPvPEP/AddSC_outdoorpvp_ep` and `OutdoorPvPSI/AddSC_outdoorpvp_si`.

**HandlePlayerLeaveZone**  
Looks up the script for `zoneid`. If found and the player is tracked, calls `OnPlayerLeave()` on the script. Called by `Player.Main/RemoveFromWorld` and `Player.Main/UpdateZone`.

**GetZoneScriptToZoneId**  
Returns the `ZoneScript` pointer for `zoneid` from `m_ZoneScriptsMap`, or `nullptr` if not found. Called by `Player.Main/GetZoneScript`.

**Update**  
Accumulates `diff` in `m_UpdateTimer`. If timer exceeds 1000 ms, calls `Update()` on all scripts in `m_ZoneScriptsSet` and resets the timer. Called by `World/Update`.

**HandleCustomSpell**  
Iterates through `m_ZoneScriptsSet`. Returns `true` if any script’s `HandleCustomSpell()` returns `true`, otherwise `false`.

**GetZoneScript**  
Returns the `ZoneScript` pointer for `zoneId` from `m_ZoneScriptsMap`, or `nullptr` if not found. Called by `WorldObject.Object/SetZoneScript`.

**HandleOpenGo**  
Iterates through `m_ZoneScriptsSet`. Returns `true` if any script’s `HandleOpenGo()` returns `true`, otherwise `false`.

**HandleGossipOption**  
Iterates through `m_ZoneScriptsSet`. Stops if any script’s `HandleGossipOption()` returns `true`.

**HandleDropFlag**  
Iterates through `m_ZoneScriptsSet`. Stops if any script’s `HandleDropFlag()` returns `true`.

**OnMapCrashed**  
Removes all `ZoneScript` objects associated with the given `map` from both `m_ZoneScriptsSet` and `m_ZoneScriptsMap`. Called by `MapManager/Update`.

---

<!-- machine-true, projected from graph.json -->

## Map — ZoneScriptMgr

*Source:* ZoneScriptMgr.cpp, ZoneScriptMgr.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ZoneScriptMgr | ctor | — | — | — |
| ~ZoneScriptMgr | dtor | — | — | — |
| InitZoneScripts | method | Register/RegisterZoneScripts | World/SetInitialWorldSettings | — |
| InitMapZoneScripts | method | Log.Main/Out, ZoneScript/SetMap, ZoneScript/SetupZoneScript, ZoneScript_Script/GetMapId, ZoneScript_Script/GetZoneScript | — | — |
| MapLoaded | method | — | MapManager/CreateMap, MapManager/CreateTestMap | — |
| AddZone | method | — | ZoneScript/RegisterZone | — |
| HandlePlayerEnterZone | method | ZoneScript/HasPlayer, ZoneScript/OnPlayerEnter#2 | Player.Main/UpdateZone | — |
| AddScript | method | — | OutdoorPvPEP/AddSC_outdoorpvp_ep, OutdoorPvPSI/AddSC_outdoorpvp_si | — |
| HandlePlayerLeaveZone | method | ZoneScript/HasPlayer, ZoneScript/OnPlayerLeave#2 | Player.Main/RemoveFromWorld, Player.Main/UpdateZone | — |
| GetZoneScriptToZoneId | method | — | Player.Main/GetZoneScript | — |
| Update | method | ZoneScript/Update#3 | World/Update | — |
| HandleCustomSpell | method | ZoneScript/HandleCustomSpell#3 | — | — |
| GetZoneScript | method | — | WorldObject.Object/SetZoneScript | — |
| HandleOpenGo | method | ZoneScript/HandleOpenGo#3 | — | — |
| HandleGossipOption | method | ZoneScript/HandleGossipOption#3 | — | — |
| HandleDropFlag | method | ZoneScript/HandleDropFlag#3 | — | — |
| OnMapCrashed | method | ZoneScript/GetMap#2 | MapManager/Update | — |
