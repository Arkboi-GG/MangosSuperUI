# OutdoorPvPSI

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# OutdoorPvPSI

**OutdoorPvPSI** implements the server-side logic for the Silithus Outdoor PvP event in World of Warcraft (client builds newer than 1.11.2). It manages the collection of "Silithyst" resources by players from both the Alliance and Horde factions. The system tracks how many resources each faction has gathered toward a configurable maximum (`SI_MAX_RESOURCES_DEFAULT`, typically 200). When a faction reaches the maximum, they gain control of the zone, receive a buff (`SI_CENARION_FAVOR`), and the resource count resets.

The unit handles:
1.  **Resource Tracking:** Incrementing counters when players deposit Silithyst at specific area triggers.
2.  **Visual Feedback:** Spawning "Dust Bag" game objects to represent collected resources and updating world state UI elements.
3.  **Announcements:** Triggering NPC yells at 25%, 50%, 75%, and 100% completion milestones.
4.  **Rewards:** Granting honor points, faction reputation, and quest credit upon successful deposition.
5.  **Persistence:** Saving and restoring resource counts across server restarts using `ObjectMgr` saved variables.

It is registered as a `ZoneScript` for Map ID 1 (Eastern Kingdoms) via the `OutdoorPvP_silithus` helper class.

## Member-by-Member Behavior

### Initialization and Setup

*   **OutdoorPvPSI**: The constructor initializes the internal state. It sets `m_TypeId` to `OUTDOOR_PVP_SI`, resets gathered resources for both factions (`m_Gathered_A`, `m_Gathered_H`) to zero, sets the maximum resource threshold to `SI_MAX_RESOURCES_DEFAULT` (200), and clears the last controller record.
*   **SetupZoneScript**: Called during zone script registration. It registers the relevant zones (`OutdoorPvPSIBuffZones`: areas 1377, 3428, 3429) so that players entering these areas trigger the script. Crucially, it restores the `m_MaxRessources` value from the database via `sObjectMgr.GetSavedVariable`. Note that while it restores the max, it explicitly resets `m_Gathered_A` and `m_Gathered_H` to 0, meaning progress is lost on reboot, but the configuration (max cap) persists.
*   **Update**: An empty override. No periodic updates are required for this event logic.

### World State Management

*   **FillInitialWorldStates**: Prepares a `WorldPacket` with the initial values for the Silithus PvP UI. It writes three pairs: Alliance gathered, Horde gathered, and the maximum resource cap. It returns `3` indicating three world states were written.
*   **SendRemoveWorldStates**: Sends update packets to a specific player setting all three Silithus world states to 0. This is likely used when a player leaves the zone or the event ends, clearing their UI.
*   **UpdateWorldState**: Broadcasts the current resource counts to all players in the zone via `SendUpdateWorldState` (inherited from `ZoneScript`). It also persists these values to the database using `sObjectMgr.SetSavedVariable`, ensuring the current progress is saved for potential future use or debugging, although `SetupZoneScript` resets the counters on load.

### Player Interaction

*   **OnPlayerEnter**: Checks if the entering player belongs to the faction that last controlled the zone (`m_LastController`). If so, it casts `SI_CENARION_FAVOR` on them. It then delegates to the base `OutdoorPvP::OnPlayerEnter`.
*   **OnPlayerLeave**: Delegates entirely to the base `OutdoorPvP::OnPlayerLeave`.

### Resource Collection and Deposition

*   **HandleAreaTrigger**: The core logic for depositing Silithyst.
    1.  Validates that the player has the `SI_SILITHYST_FLAG` aura.
    2.  Determines if the trigger is for Alliance (`SI_AREATRIGGER_A`) or Horde (`SI_AREATRIGGER_H`).
    3.  Verifies the player's team matches the trigger's faction.
    4.  Increments the respective faction's counter (`m_Gathered_A` or `m_Gathered_H`).
    5.  **Milestones:** Checks if the new total hits 25%, 50%, 75%, or 100% of `m_MaxRessources`. If so, it calls `DoSilithystYell` to announce the progress via an NPC.
    6.  **Victory Condition:** If the counter meets or exceeds `m_MaxRessources`:
        *   Applies `SI_CENARION_FAVOR` to the entire faction via `TeamApplyBuff`.
        *   Sends a zone-wide text message announcing the capture.
        *   Updates `m_LastController`.
        *   Calls `ResetResourceCount` to clear the board.
        *   Logs the victory.
    7.  **Partial Progress:** If not a victory, it calls `SpawnDustBags` to visually represent the collected resources.
    8.  **Rewards:** Grants quest credit (`KilledMonsterCredit`), removes the flag aura, and applies reward spells: `SI_TRACES_OF_SILITHYST` (visual), `HONOR_POINTS_199` (honor), and `SILITHYST_CAP_REWARD` (faction rep).
    9.  Updates the world state and logs the action.
    10. Returns `true` to indicate the trigger was handled.

*   **HandleDropFlag**: Handles the logic when a player drops the Silithyst flag (spell `SI_SILITHYST_FLAG`).
    1.  Checks if the player is within 5.0 units of their faction's drop trigger location. If they are, it returns `false` (preventing the drop, likely forcing them to deposit).
    2.  If outside the trigger range, it casts `SILLITHUS_FLAG_DROP` on the player (likely removing the flag aura and spawning a ground object).
    3.  Logs the drop event.

### Visuals and Cleanup

*   **SpawnDustBags**: Calculates how many "Dust Bag" game objects should be visible based on the current resource count (`resource / 15`). It iterates through a static list of bag GUIDs (`allBags`), checking if they have already been spawned (`spawnedBags`). If not, it loads the spawn via `GetMap()->LoadGameObjectSpawn` and marks it as spawned. This creates a visual pile of bags growing as resources are collected.
*   **ResetResourceCount**: Resets the gathered counters to 0. It iterates through the tracked Alliance and Horde dust bag GUIDs, finds the corresponding `GameObject` in the map, and adds them to the removal list (effectively despawning them). It then clears the tracking sets.

### Helper Functions

*   **DoSilithystYell**: A static helper function. It finds the nearest NPC of a specified type (Alliance or Horde announcer) to the player. If found, it makes the NPC say a specific text ID. If not found, it logs an error.

### Registration

*   **OutdoorPvP_silithus**: A local class inheriting from `ZoneScript_Script`. It defines the map ID (1) and provides a factory method `GetZoneScript` that returns a new `OutdoorPvPSI` instance.
*   **GetMapId**: Returns `1` (Eastern Kingdoms).
*   **GetZoneScript**: Instantiates and returns the `OutdoorPvPSI` object.
*   **AddSC_outdoorpvp_si**: The entry point function called by the script manager to register the `OutdoorPvP_silithus` script with `sZoneScriptMgr`.

## Cross-Unit Boundaries

*   **WorldStates/WriteInitialWorldStatePair**: Called by `FillInitialWorldStates` to serialize resource counts into network packets for client UI.
*   **Player.Main/SendUpdateWorldState**: Called by `SendRemoveWorldStates` to clear UI elements for a specific player.
*   **ObjectMgr/SetSavedVariable**: Called by `UpdateWorldState` to persist resource counts to the database.
*   **ZoneScript/SendUpdateWorldState**: Called by `UpdateWorldState` to broadcast current counts to all players in the zone.
*   **ObjectMgr/GetSavedVariable**: Called by `SetupZoneScript` to restore the maximum resource cap from the database.
*   **ZoneScript/RegisterZone**: Called by `SetupZoneScript` to activate the script for specific area IDs.
*   **Player.Main/GetTeam**: Called by `OnPlayerEnter`, `HandleAreaTrigger`, and `HandleDropFlag` to determine faction alignment.
*   **SpellCaster/CastSpell**: Called by `OnPlayerEnter`, `HandleAreaTrigger`, and `HandleDropFlag` to apply buffs, rewards, or remove flags.
*   **ZoneScript/OnPlayerEnter/OnPlayerLeave**: Called by `OnPlayerEnter`/`OnPlayerLeave` to maintain base class behavior.
*   **Map.Main/LoadGameObjectSpawn**: Called by `SpawnDustBags` to instantiate visual game objects.
*   **ZoneScript/GetMap**: Called by `SpawnDustBags` and `ResetResourceCount` to access the map instance for object management.
*   **Map.Main/GetGameObject**: Called by `ResetResourceCount` to find existing dust bags for removal.
*   **WorldObject.Object/AddObjectToRemoveList**: Called by `ResetResourceCount` to schedule dust bags for deletion.
*   **Log.Main/Out**: Called by `DoSilithystYell`, `HandleAreaTrigger`, and `HandleDropFlag` for logging events and errors.
*   **ScriptMgr/DoScriptText**: Called by `DoSilithystYell` to trigger NPC dialogue.
*   **WorldObject.Object/FindNearestCreature**: Called by `DoSilithystYell` to locate the announcer NPC.
*   **ObjectMgr/GetMangosStringForDBCLocale**: Called by `HandleAreaTrigger` to retrieve localized capture messages.
*   **Player.Main/GetName/GetSession/GetAccountId/GetRemoteAddress**: Called by `HandleAreaTrigger` and `HandleDropFlag` for detailed logging of player actions.
*   **Player.Main/KilledMonsterCredit**: Called by `HandleAreaTrigger` to grant quest progress.
*   **Unit.Main/HasAura/RemoveAurasDueToSpell**: Called by `HandleAreaTrigger` to manage the Silithyst flag aura.
*   **World/SendZoneText**: Called by `HandleAreaTrigger` to broadcast capture announcements.
*   **ZoneScript/TeamApplyBuff**: Called by `HandleAreaTrigger` to buff the winning faction.
*   **ObjectMgr/GetAreaTrigger**: Called by `HandleDropFlag` to get coordinates of the drop zone.
*   **WorldObject.Object/IsWithinDist3d**: Called by `HandleDropFlag` to check proximity to the drop trigger.
*   **ZoneScript_Script/ZoneScript_Script**: Base class for `OutdoorPvP_silithus`.
*   **ZoneScriptMgr/AddScript**: Called by `AddSC_outdoorpvp_si` to register the script.

## Data Model

This unit does not interact directly with SQL tables via queries. It uses `ObjectMgr`'s `SetSavedVariable` and `GetSavedVariable` methods to store and retrieve key-value pairs (likely stored in a generic `variables` table or similar mechanism managed by the core). The keys used are derived from world state constants:
*   `WS_OPVP_SI_GATHERED_A`
*   `WS_OPVP_SI_GATHERED_H`
*   `WS_OPVP_SI_SILITHYST_MAX`

No specific database schema is required for this unit's operation beyond the standard `ObjectMgr` variable storage.

## Notable Implementation Details

1.  **Progress Loss on Reboot:** In `SetupZoneScript`, `m_Gathered_A` and `m_Gathered_H` are hardcoded to `0`. While `m_MaxRessources` is restored from the database, the actual progress is not. This means if the server restarts mid-event, the resource count resets, but the max cap remains configured.
2.  **Hardcoded Milestones:** The announcement logic in `HandleAreaTrigger` uses fixed percentages (25%, 50%, 75%, 100%) of `m_MaxRessources`. If `m_MaxRessources` is changed dynamically, the milestones adjust accordingly, but the logic assumes integer division works cleanly for these thresholds.
3.  **Dust Bag Limit:** `SpawnDustBags` calculates needed bags as `resource / 15`. With a default max of 200, this results in roughly 13 bags. The static vectors `sAllianceDustBags` and `sHordeDustBags` contain 11 and 12 GUIDs respectively. If `m_MaxRessources` is increased significantly, the code might run out of predefined bag GUIDs to spawn, leading to incomplete visual feedback.
4.  **Flag Drop Prevention:** `HandleDropFlag` prevents dropping the flag if the player is within 5.0 units of the correct trigger. This forces players to enter the trigger zone to deposit, preventing accidental drops near the objective.
5.  **Static Announcer NPCs:** `DoSilithystYell` relies on finding specific NPC IDs (17079 for Horde, 17080 for Alliance) nearby. If these NPCs are missing or despawned, the announcements fail silently (with a log error).
6.  **Client Build Guard:** The entire implementation is wrapped in `#if SUPPORTED_CLIENT_BUILD > CLIENT_BUILD_1_11_2`, indicating this feature is only available for TBC/WotLK+ clients.

## Member Reference

**OutdoorPvPSI**: Constructor initializing member variables for the Silithus PvP event state.
**FillInitialWorldStates**: Serializes current resource counts and max cap into a `WorldPacket` for client initialization.
**SendRemoveWorldStates**: Sends packets to a player to reset Silithus UI elements to zero.
**UpdateWorldState**: Broadcasts current resource counts to all players and persists them to the database.
**SetupZoneScript**: Registers relevant zones and restores the max resource cap from the database, resetting progress counters.
**Update**: Empty override; no periodic logic required.
**OnPlayerEnter**: Applies a buff to players from the last controlling faction and delegates to base class.
**OnPlayerLeave**: Delegates to base class.
**SpawnDustBags**: Spawns visual game objects representing collected resources based on count.
**ResetResourceCount**: Resets counters and despawns all active dust bag game objects.
**DoSilithystYell**: Static helper to find an announcer NPC and play a specific text line.
**HandleAreaTrigger**: Core logic for depositing Silithyst: validates flag, increments counter, handles milestones/victory, spawns visuals, grants rewards, and updates state.
**HandleDropFlag**: Handles flag dropping, preventing it near the trigger zone and applying drop effects elsewhere.
**OutdoorPvP_silithus**: Local class registering the `OutdoorPvPSI` script for Map ID 1.
**GetMapId**: Returns map ID 1.
**GetZoneScript**: Factory method returning a new `OutdoorPvPSI` instance.
**AddSC_outdoorpvp_si**: Entry point to register the script with the zone script manager.

---

<!-- machine-true, projected from graph.json -->

## Map — OutdoorPvPSI

*Source:* OutdoorPvPSI.cpp, OutdoorPvPSI.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| OutdoorPvPSI | ctor | — | — | — |
| FillInitialWorldStates | method | WorldStates/WriteInitialWorldStatePair | — | — |
| SendRemoveWorldStates | method | Player.Main/SendUpdateWorldState | — | — |
| UpdateWorldState | method | ObjectMgr/SetSavedVariable, ZoneScript/SendUpdateWorldState#2 | — | — |
| SetupZoneScript | method | ObjectMgr/GetSavedVariable, ZoneScript/RegisterZone | — | — |
| Update | method | — | — | — |
| OnPlayerEnter | method | Player.Main/GetTeam, SpellCaster/CastSpell#2, ZoneScript/OnPlayerEnter | — | — |
| OnPlayerLeave | method | ZoneScript/OnPlayerLeave | — | — |
| SpawnDustBags | method | Map.Main/LoadGameObjectSpawn, ZoneScript/GetMap#2 | — | — |
| ResetResourceCount | method | Map.Main/GetGameObject, ObjectGuid/ObjectGuid#3, WorldObject.Object/AddObjectToRemoveList, ZoneScript/GetMap#2 | — | — |
| DoSilithystYell | function | Log.Main/Out, ScriptMgr/DoScriptText, WorldObject.Object/FindNearestCreature | — | — |
| HandleAreaTrigger | method | Log.Main/Out, Object/GetGUIDLow, ObjectGuid/ObjectGuid, ObjectMgr/GetMangosStringForDBCLocale, Player.Main/GetName, Player.Main/GetSession, Player.Main/GetTeam, Player.Main/KilledMonsterCredit, SpellCaster/CastSpell#2, Unit.Main/HasAura#2, Unit.Main/RemoveAurasDueToSpell, World/SendZoneText, WorldSession.Main/GetAccountId, WorldSession.Main/GetRemoteAddress, ZoneScript/TeamApplyBuff | — | — |
| HandleDropFlag | method | Log.Main/Out, Object/GetGUIDLow, ObjectMgr/GetAreaTrigger, Player.Main/GetName, Player.Main/GetSession, Player.Main/GetTeam, SpellCaster/CastSpell#2, WorldObject.Object/IsWithinDist3d, WorldSession.Main/GetAccountId, WorldSession.Main/GetRemoteAddress | — | — |
| OutdoorPvP_silithus | ctor | ZoneScript_Script/ZoneScript_Script | — | — |
| GetMapId | method | — | — | — |
| GetZoneScript | method | — | — | — |
| AddSC_outdoorpvp_si | function | ZoneScriptMgr/AddScript | Register/RegisterZoneScripts | — |
