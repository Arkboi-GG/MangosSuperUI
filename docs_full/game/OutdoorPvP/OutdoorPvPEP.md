# OutdoorPvPEP

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# OutdoorPvPEP: Eastern Plaguelands Outdoor PvP

`OutdoorPvPEP` implements the server-side logic for the **Eastern Plaguelands** outdoor world PvP zone in World of Warcraft (Classic/TBC/WotLK eras, guarded by `SUPPORTED_CLIENT_BUILD > CLIENT_BUILD_1_11_2`). This system manages the capture and control of four distinct towers: **Eastwall Tower**, **Northpass Tower**, **Plaguewood Tower**, and **Crown Guard Tower**.

The core gameplay loop involves players capturing these towers to gain strategic advantages. Controlling towers grants teams escalating buffs ("Echoes of Lordaeron"), spawns defensive NPCs or utility creatures (flight masters, curing shrines), and alters the visual state of the zone (banners, flares, graveyards). The system tracks the number of towers held by each faction to determine global buffs and broadcast victory messages when all four towers are controlled by one side.

This unit consists of:
1.  **`OutdoorPvPEP`**: The main zone script manager that handles global state, player entry/exit buffs, and periodic updates.
2.  **Four Capture Point Classes** (`OPvPCapturePointEP_EWT`, `_NPT`, `_CGT`, `_PWT`): Specialized subclasses of `OPvPCapturePoint` that manage the specific visual, audio, and entity-spawning behaviors for each individual tower.
3.  **`OutdoorPvP_eastern_plaguelands`**: A registration script that links the map ID to the `OutdoorPvPEP` instance.

There are no direct database table interactions in this unit; all configuration (coordinates, NPC entries, spell IDs) is hardcoded in static arrays within the header file.

## Member-by-Member Behavior

### Global Zone Management (`OutdoorPvPEP`)

The `OutdoorPvPEP` class acts as the central controller for the entire Eastern Plaguelands PvP zone. It maintains the count of towers controlled by the Alliance and Horde and applies corresponding buffs to players in the zone.

*   **`OutdoorPvPEP` (ctor)**: Initializes the zone type identifier (`OUTDOOR_PVP_EP`) and resets the internal control counters (`EP_Controls`, `m_AllianceTowersControlled`, `m_HordeTowersControlled`) to zero.
*   **`SetupZoneScript`**: Registers the relevant area IDs (`EP_BuffZones`) with the zone script manager via `ZoneScript/RegisterZone`. It then instantiates the four specific tower capture point objects (`OPvPCapturePointEP_EWT`, `_PWT`, `_CGT`, `_NPT`) and adds them to the zone's capture point list via `OutdoorPvP/AddCapturePoint`.
*   **`Update`**: Called periodically. If objectives have changed (`m_objective_changed`), it recalculates the total number of towers controlled by each faction by iterating through `EP_Controls`. It updates the UI world states for tower counts (`WS_UI_TOWER_COUNT_ALLIANCE`, `WS_UI_TOWER_COUNT_HORDE`) and triggers `BuffTeams()` to apply or refresh buffs for all players in the zone. If one faction controls all four towers, it broadcasts a victory message via `Map.Main/SendDefenseMessage`. Finally, it delegates to the parent `ZoneScript/Update#2`.
*   **`OnPlayerEnter`**: When a player enters the zone, it checks their team. If the team controls at least one tower (and less than 5, a safety bound), it casts the appropriate tiered buff spell from `EP_AllianceBuffs` or `EP_HordeBuffs` onto the player using `SpellCaster/CastSpell#2`. It then calls the parent `ZoneScript/OnPlayerEnter`.
*   **`OnPlayerLeave`**: Removes all tiered buffs associated with the zone from the departing player using `Unit.Main/RemoveAurasDueToSpell` before calling the parent `ZoneScript/OnPlayerLeave`.
*   **`BuffTeams`**: Iterates through all players currently tracked in the zone (`m_players`). For each player, it removes existing zone buffs and reapplies the correct tier based on the current number of controlled towers (`m_AllianceTowersControlled` or `m_HordeTowersControlled`). This ensures buffs update dynamically as tower control shifts.
*   **`FillInitialWorldStates#5`**: Prepares the initial world state packet for a joining player. It writes the current tower counts and slider UI states. It then iterates through all capture points, calling their respective `FillInitialWorldStates` methods to include tower-specific state data.
*   **`SendRemoveWorldStates`**: Sends a series of `Player.Main/SendUpdateWorldState` packets to reset all UI elements related to the EP PvP zone (tower counts, sliders, and individual tower states) to zero. This is typically used when a player leaves the zone or the zone script is deactivated.

### Tower-Specific Capture Points

Each of the four tower classes (`EWT`, `NPT`, `CGT`, `PWT`) follows a similar pattern but implements unique visual and entity-spawning behaviors. They inherit from `OPvPCapturePoint` (not shown in this unit, but referenced in calls).

#### Common Methods Across All Towers

*   **Constructors (`OPvPCapturePointEP_*`)**: Initialize the capture point with specific coordinates and object entries from the static arrays in the header. They add banner game objects and call `ChangeState()` to set the initial neutral appearance.
*   **`ChangeState`**: The core logic triggered when the capture progress changes. It handles:
    *   Resetting control flags in the parent `OutdoorPvPEP` if control is lost.
    *   Removing old entities (creatures, objects).
    *   Switching on the new state (`OBJECTIVESTATE_*`) to spawn new entities, play sounds, update banner art, and notify the parent class of control changes.
    *   Calling `UpdateTowerState()` to sync UI.
*   **`SendChangePhase`**: Updates the UI slider position (`WS_UI_TOWER_SLIDER_POSITION`) via `ZoneScript/SendUpdateWorldState`.
*   **`FillInitialWorldStates`**: Writes the current tower state bits (Neutral, Alliance/Horde Contested/Progressing/Controlled) to the world state packet using `WorldStates/WriteInitialWorldStatePair`.
*   **`UpdateTowerState`**: Broadcasts the current tower state bits to all players in the zone via `ZoneScript/SendUpdateWorldState#2`.
*   **`UpdateBannerArt`**: Finds the banner game objects associated with the tower and updates their visual kit (`SetGoArtKit`) and plays an animation (`SendGameObjectCustomAnim`) if the art kit has changed.
*   **`PlaySound`**: Plays a specific sound effect on the main banner game object via `WorldObject.Object/PlayDirectSound`.
*   **`HandlePlayerEnter` / `HandlePlayerLeave`**: Delegates to the parent `OPvPCapturePoint` methods.

#### Unique Tower Behaviors

**1. Eastwall Tower (`OPvPCapturePointEP_EWT`)**
*   **`SummonSquadAtEastWallTower`**: Spawns a squad of NPCs (Lordaeron Commander/Soldiers for Alliance, Veterans/Fighters for Horde) at predefined positions. It uses `ZoneScript/AddCreature` and organizes them into a group via `Creature.Main/JoinCreatureGroup` so they move and aggro together.
*   **`RemoveSquad`**: Deletes all summoned squad members using `ZoneScript/DelCreature`.
*   **Behavior**: When captured, it spawns a defensive buffer NPC and a squad of elite guards. It does not spawn utility objects like shrines or flight masters.

**2. Northpass Tower (`OPvPCapturePointEP_NPT`)**
*   **`SummonCuringShrine`**: Spawns a team-specific curing shrine game object and a banner aura. It sets the shrine to be spawned by default (`GameObject/SetSpawnedByDefault`).
*   **Behavior**: When captured, it provides a healing utility (curing shrine) for the controlling team. It does not spawn combat NPCs.

**3. Crown Guard Tower (`OPvPCapturePointEP_CGT`)**
*   **`LinkGraveYard` / `UnLinkGraveYard`**: Modifies the graveyard linking for the Eastern Plaguelands and The Fungal Vale areas. It uses `ObjectMgr/AddGraveYardLink` and `ObjectMgr/RemoveGraveYardLink` to ensure dead players respawn at the controlling team's base near this tower.
*   **`SummonBannerAura`**: Spawns a large visual aura game object indicating control.
*   **`SummonSpiritOfVictory`**: Spawns a "Spirit of Victory" creature that moves along a waypoint path (`Creature.MotionMaster/MoveWaypoint`) and emits particle effects (`Unit.Main/AddAura`). This is a visual celebration effect.
*   **Behavior**: This is the most strategically significant tower due to graveyard control. It also features the most complex visual feedback (spirit, large aura).

**4. Plaguewood Tower (`OPvPCapturePointEP_PWT`)**
*   **`SummonFlightMaster`**: Spawns a flight master NPC (`NPC_WILLIAM_KIELAR`) with team-specific faction and particle effects (`Unit.Main/SetFactionTemplateId`, `Unit.Main/AddAura`).
*   **Behavior**: Provides fast travel utility for the controlling team. Like Northpass, it does not spawn combat NPCs.

### Registration Script

*   **`OutdoorPvP_eastern_plaguelands`**: A simple wrapper class inheriting from `ZoneScript_Script`.
    *   **`GetMapId`**: Returns `0` (the map ID for Eastern Plaguelands).
    *   **`GetZoneScript`**: Instantiates and returns a new `OutdoorPvPEP` object.
*   **`AddSC_outdoorpvp_ep`**: The entry point function that registers the `OutdoorPvP_eastern_plaguelands` script with the `ZoneScriptMgr` via `ZoneScriptMgr/AddScript`.

## Cross-Unit Boundaries

*   **`ZoneScript`**: The primary interface for zone management. `OutdoorPvPEP` inherits from it (implicitly via `OutdoorPvP`) and uses it to register zones, send world state updates, and manage player entry/exit. The capture points use it to add/delete creatures and objects, and get references to the map.
*   **`OutdoorPvP`**: The base class for outdoor PvP zones. `OutdoorPvPEP` extends it to add specific logic for buffing and tower counting. The capture points interact with the `OutdoorPvPEP` instance via the `m_PvP` pointer (cast to `OutdoorPvPEP*`) to update global control flags (`EP_Controls`).
*   **`ObjectMgr`**: Used exclusively by `OPvPCapturePointEP_CGT` to link/unlink graveyards. This is a critical interaction for gameplay balance.
*   **`Map`**: Used to send defense messages (broadcasts) when towers are captured or all towers are controlled. Also used to retrieve player objects in `BuffTeams`.
*   **`Player`**: Interacted with in `OutdoorPvPEP` to cast/remove buffs and send world state updates.
*   **`Creature` / `GameObject`**: The capture points manipulate these objects extensively to spawn buffers, squads, shrines, flight masters, and spirits. They also query them to apply spells or animations.
*   **`WorldStates`**: Helper functions used to write initial world state pairs into packets.
*   **`SpellCaster`**: Used to cast buffs on players and spells on buffer NPCs (e.g., `SPELL_TOWER_CAPTURE_TEST_DND`).
*   **`Unit`**: Used to manage auras and motion masters for summoned creatures (Spirit of Victory, Flight Master).
*   **`CreatureGroups`**: Used in `OPvPCapturePointEP_EWT::SummonSquadAtEastWallTower` to group the summoned soldiers under a commander.

## Data Model

This unit does not interact with any database tables directly. All configuration data (coordinates, NPC entries, spell IDs, sound IDs, art kits) is defined in static constant arrays and enums within `OutdoorPvPEP.h`. The graveyard linking interacts with the in-memory graveyard manager maintained by `ObjectMgr`, but no SQL queries are executed here.

## Notable Implementation Details

1.  **Hardcoded Configuration**: All spatial and entity data is hardcoded in `OutdoorPvPEP.h`. This makes the zone rigid but performant. Changes to tower locations or NPC types require recompilation.
2.  **State Bitmasking**: Tower states are represented by bitmasks (`TOWERSTATE_*`). The `UpdateTowerState` and `FillInitialWorldStates` methods check these bits individually to update the UI. This allows for composite states (e.g., contested and progressing) though the logic primarily treats them as mutually exclusive phases.
3.  **Buffer NPCs**: Each tower spawns a "Buffer" NPC upon capture. These NPCs cast `SPELL_TOWER_CAPTURE_TEST_DND` (30882), which is described in comments as a script effect that likely prevents players from leaving the capture point or interfering with the capture process once started/completed.
4.  **Graveyard Logic**: Only Crown Guard Tower affects graveyards. The `LinkGraveYard` method explicitly links the graveyard for both `EP_Zone` (Eastern Plaguelands) and `TFV_area` (The Fungal Vale). This implies that controlling Crown Guard Tower influences respawn points for a larger area than just the immediate tower vicinity.
5.  **Buff Tiering**: Buffs are applied based on the *number* of towers controlled. The arrays `EP_AllianceBuffs` and `EP_HordeBuffs` contain 4 spells, corresponding to controlling 1, 2, 3, or 4 towers. The code accesses these arrays using `m_AllianceTowersControlled - 1` as the index, ensuring the buff strength scales with territorial dominance.
6.  **Visual Feedback**: Significant effort is put into visual feedback:
    *   Banners change art kits and animate.
    *   Flares (game objects) are spawned to indicate control color.
    *   Specific sounds play for capture, warning, and victory.
    *   Crown Guard Tower spawns a moving "Spirit of Victory" creature.
7.  **Safety Checks**: In `OnPlayerEnter`, the code checks `m_AllianceTowersControlled < 5` before indexing the buff array. Since there are only 4 towers, this is a safeguard against out-of-bounds access if the counter ever becomes corrupted or if a future expansion adds more towers without updating the array size.
8.  **Client Build Guard**: The entire file is wrapped in `#if SUPPORTED_CLIENT_BUILD > CLIENT_BUILD_1_11_2`, indicating this PvP system was introduced in Wrath of the Lich King (3.0+) and is not applicable to earlier versions (Vanilla/TBC).

## Member Reference

**OPvPCapturePointEP_EWT** (ctor): Initializes the Eastwall Tower capture point with specific coordinates and banner objects, then sets the initial state.
**ChangeState#2**: Handles state transitions for Eastwall Tower, spawning/removing buffer NPCs and squads, playing sounds, and updating visuals.
**SendChangePhase#2**: Updates the UI slider position for Eastwall Tower.
**FillInitialWorldStates#2**: Writes Eastwall Tower's state bits to the initial world state packet.
**UpdateTowerState#2**: Broadcasts Eastwall Tower's state bits to all players.
**UpdateBannerArt#2**: Updates the visual art kit and animation of Eastwall Tower's banners.
**PlaySound#2**: Plays a sound effect on Eastwall Tower's main banner.
**HandlePlayerEnter#2**: Delegates player entry handling to the parent capture point class.
**HandlePlayerLeave#2**: Delegates player leave handling to the parent capture point class.
**RemoveSquad**: Deletes all summoned squad members from Eastwall Tower.
**SummonSquadAtEastWallTower**: Spawns a team-specific squad of NPCs at Eastwall Tower and groups them under a commander.
**OPvPCapturePointEP_NPT** (ctor): Initializes the Northpass Tower capture point with specific coordinates and banner objects, then sets the initial state.
**ChangeState#3**: Handles state transitions for Northpass Tower, spawning/removing buffer NPCs and curing shrines, playing sounds, and updating visuals.
**SendChangePhase#3**: Updates the UI slider position for Northpass Tower.
**FillInitialWorldStates#3**: Writes Northpass Tower's state bits to the initial world state packet.
**UpdateTowerState#3**: Broadcasts Northpass Tower's state bits to all players.
**UpdateBannerArt#3**: Updates the visual art kit and animation of Northpass Tower's banners.
**PlaySound#3**: Plays a sound effect on Northpass Tower's main banner.
**HandlePlayerEnter#3**: Delegates player entry handling to the parent capture point class.
**HandlePlayerLeave#3**: Delegates player leave handling to the parent capture point class.
**SummonCuringShrine**: Spawns a team-specific curing shrine and banner aura at Northpass Tower.
**OPvPCapturePointEP_CGT** (ctor): Initializes the Crown Guard Tower capture point, unlinks graveyards initially, sets coordinates/banners, and sets the initial state.
**ChangeState**: Handles state transitions for Crown Guard Tower, spawning/removing buffer NPCs, linking graveyards, summoning spirits/auras, playing sounds, and updating visuals.
**SendChangePhase**: Updates the UI slider position for Crown Guard Tower.
**FillInitialWorldStates**: Writes Crown Guard Tower's state bits to the initial world state packet.
**UpdateTowerState**: Broadcasts Crown Guard Tower's state bits to all players.
**UpdateBannerArt**: Updates the visual art kit and animation of Crown Guard Tower's banners.
**PlaySound**: Plays a sound effect on Crown Guard Tower's main banner.
**HandlePlayerEnter**: Delegates player entry handling to the parent capture point class.
**HandlePlayerLeave**: Delegates player leave handling to the parent capture point class.
**LinkGraveYard**: Links the specified team's graveyard to the Eastern Plaguelands and The Fungal Vale areas.
**UnLinkGraveYard**: Removes graveyard links for both teams in the Eastern Plaguelands and The Fungal Vale areas.
**SummonBannerAura**: Spawns a large visual aura game object at Crown Guard Tower.
**SummonSpiritOfVictory**: Spawns a "Spirit of Victory" creature at Crown Guard Tower, applies particle auras, and starts its waypoint movement.
**OPvPCapturePointEP_PWT** (ctor): Initializes the Plaguewood Tower capture point with specific coordinates and banner objects, then sets the initial state.
**ChangeState#4**: Handles state transitions for Plaguewood Tower, spawning/removing buffer NPCs and flight masters, playing sounds, and updating visuals.
**SendChangePhase#4**: Updates the UI slider position for Plaguewood Tower.
**FillInitialWorldStates#4**: Writes Plaguewood Tower's state bits to the initial world state packet.
**UpdateTowerState#4**: Broadcasts Plaguewood Tower's state bits to all players.
**UpdateBannerArt#4**: Updates the visual art kit and animation of Plaguewood Tower's banners.
**PlaySound#4**: Plays a sound effect on Plaguewood Tower's main banner.
**HandlePlayerEnter#4**: Delegates player entry handling to the parent capture point class.
**HandlePlayerLeave#4**: Delegates player leave handling to the parent capture point class.
**SummonFlightMaster**: Spawns a team-specific flight master NPC at Plaguewood Tower with appropriate faction and particle effects.
**OutdoorPvPEP** (ctor): Initializes the Eastern Plaguelands PvP zone manager, resetting control counters.
**SetupZoneScript**: Registers the zone areas and instantiates the four tower capture point objects.
**Update**: Recalculates tower control counts, updates UI, applies buffs to all players, and broadcasts victory messages if applicable.
**OnPlayerEnter**: Applies tiered buffs to entering players based on current tower control.
**OnPlayerLeave**: Removes tiered buffs from departing players.
**BuffTeams**: Iterates through all players in the zone, removing old buffs and applying new ones based on current tower control.
**FillInitialWorldStates#5**: Prepares the initial world state packet for the zone, including global UI states and delegating to each tower's fill method.
**SendRemoveWorldStates**: Resets all UI-related world states for a specific player.
**OutdoorPvP_eastern_plaguelands** (ctor): Initializes the registration script wrapper.
**GetMapId**: Returns the map ID for Eastern Plaguelands (0).
**GetZoneScript**: Creates and returns a new `OutdoorPvPEP` instance.
**AddSC_outdoorpvp_ep**: Registers the `OutdoorPvP_eastern_plaguelands` script with the zone script manager.

---

<!-- machine-true, projected from graph.json -->

## Map — OutdoorPvPEP

*Source:* OutdoorPvPEP.cpp, OutdoorPvPEP.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| OPvPCapturePointEP_EWT | ctor | ZoneScript/AddObject, ZoneScript/OPvPCapturePoint, ZoneScript/SetCapturePointData | — | — |
| ChangeState#2 | method | Map.Main/SendDefenseMessage, ObjectGuid/ObjectGuid#5, SpellCaster/CastSpell#2, ZoneScript/AddCreature, ZoneScript/AddObject, ZoneScript/DelCreature, ZoneScript/DelObject, ZoneScript/GetCreature, ZoneScript/GetMap#2 | — | — |
| SendChangePhase#2 | method | ZoneScript/SendUpdateWorldState | — | — |
| FillInitialWorldStates#2 | method | WorldStates/WriteInitialWorldStatePair | — | — |
| UpdateTowerState#2 | method | ZoneScript/SendUpdateWorldState#2 | — | — |
| UpdateBannerArt#2 | method | GameObject/GetGoArtKit, GameObject/SendGameObjectCustomAnim, GameObject/SetGoArtKit, Object/GetObjectGuid, ObjectGuid/ObjectGuid#5, ZoneScript/GetGameObject | — | — |
| PlaySound#2 | method | Object/GetObjectGuid, WorldObject.Object/PlayDirectSound, ZoneScript/GetGameObject | — | — |
| HandlePlayerEnter#2 | method | ZoneScript/HandlePlayerEnter | — | — |
| HandlePlayerLeave#2 | method | ZoneScript/HandlePlayerLeave | — | — |
| RemoveSquad | method | ZoneScript/DelCreature | — | — |
| SummonSquadAtEastWallTower | method | Creature.Main/JoinCreatureGroup, ObjectGuid/ObjectGuid#5, WorldObject.Object/GetAngle, WorldObject.Object/GetOrientation, ZoneScript/AddCreature, ZoneScript/DelCreature, ZoneScript/GetCreature | — | — |
| OPvPCapturePointEP_NPT | ctor | ZoneScript/AddObject, ZoneScript/OPvPCapturePoint, ZoneScript/SetCapturePointData | — | — |
| ChangeState#3 | method | Map.Main/SendDefenseMessage, ObjectGuid/ObjectGuid#5, SpellCaster/CastSpell#2, ZoneScript/AddCreature, ZoneScript/AddObject, ZoneScript/DelCreature, ZoneScript/DelObject, ZoneScript/GetCreature, ZoneScript/GetMap#2 | — | — |
| SendChangePhase#3 | method | ZoneScript/SendUpdateWorldState | — | — |
| FillInitialWorldStates#3 | method | WorldStates/WriteInitialWorldStatePair | — | — |
| UpdateTowerState#3 | method | ZoneScript/SendUpdateWorldState#2 | — | — |
| UpdateBannerArt#3 | method | GameObject/GetGoArtKit, GameObject/SendGameObjectCustomAnim, GameObject/SetGoArtKit, Object/GetObjectGuid, ObjectGuid/ObjectGuid#5, ZoneScript/GetGameObject | — | — |
| PlaySound#3 | method | Object/GetObjectGuid, WorldObject.Object/PlayDirectSound, ZoneScript/GetGameObject | — | — |
| HandlePlayerEnter#3 | method | ZoneScript/HandlePlayerEnter | — | — |
| HandlePlayerLeave#3 | method | ZoneScript/HandlePlayerLeave | — | — |
| SummonCuringShrine | method | GameObject/SetSpawnedByDefault, ObjectGuid/ObjectGuid#5, ZoneScript/AddObject, ZoneScript/GetGameObject | — | — |
| OPvPCapturePointEP_CGT | ctor | ZoneScript/AddObject, ZoneScript/OPvPCapturePoint, ZoneScript/SetCapturePointData | — | — |
| ChangeState | method | Map.Main/SendDefenseMessage, ObjectGuid/ObjectGuid#5, SpellCaster/CastSpell#2, ZoneScript/AddCreature, ZoneScript/AddObject, ZoneScript/DelCreature, ZoneScript/DelObject, ZoneScript/GetCreature, ZoneScript/GetMap#2 | — | — |
| SendChangePhase | method | ZoneScript/SendUpdateWorldState | — | — |
| FillInitialWorldStates | method | WorldStates/WriteInitialWorldStatePair | — | — |
| UpdateTowerState | method | ZoneScript/SendUpdateWorldState#2 | — | — |
| UpdateBannerArt | method | GameObject/GetGoArtKit, GameObject/SendGameObjectCustomAnim, GameObject/SetGoArtKit, Object/GetObjectGuid, ObjectGuid/ObjectGuid#5, ZoneScript/GetGameObject | — | — |
| PlaySound | method | Object/GetObjectGuid, WorldObject.Object/PlayDirectSound, ZoneScript/GetGameObject | — | — |
| HandlePlayerEnter | method | ZoneScript/HandlePlayerEnter | — | — |
| HandlePlayerLeave | method | ZoneScript/HandlePlayerLeave | — | — |
| LinkGraveYard | method | ObjectMgr/AddGraveYardLink, ObjectMgr/RemoveGraveYardLink | — | — |
| UnLinkGraveYard | method | ObjectMgr/RemoveGraveYardLink | — | — |
| SummonBannerAura | method | ZoneScript/AddObject | — | — |
| SummonSpiritOfVictory | method | Creature.MotionMaster/MoveWaypoint, MotionMaster/Clear, ObjectGuid/ObjectGuid#5, Unit.Main/AddAura, Unit.Main/GetMotionMaster, Unit.Main/RemoveAllAuras, ZoneScript/AddCreature, ZoneScript/DelCreature, ZoneScript/GetCreature | — | — |
| OPvPCapturePointEP_PWT | ctor | ZoneScript/AddObject, ZoneScript/OPvPCapturePoint, ZoneScript/SetCapturePointData | — | — |
| ChangeState#4 | method | Map.Main/SendDefenseMessage, ObjectGuid/ObjectGuid#5, SpellCaster/CastSpell#2, ZoneScript/AddCreature, ZoneScript/AddObject, ZoneScript/DelCreature, ZoneScript/DelObject, ZoneScript/GetCreature, ZoneScript/GetMap#2 | — | — |
| SendChangePhase#4 | method | ZoneScript/SendUpdateWorldState | — | — |
| FillInitialWorldStates#4 | method | WorldStates/WriteInitialWorldStatePair | — | — |
| UpdateTowerState#4 | method | ZoneScript/SendUpdateWorldState#2 | — | — |
| UpdateBannerArt#4 | method | GameObject/GetGoArtKit, GameObject/SendGameObjectCustomAnim, GameObject/SetGoArtKit, Object/GetObjectGuid, ObjectGuid/ObjectGuid#5, ZoneScript/GetGameObject | — | — |
| PlaySound#4 | method | Object/GetObjectGuid, WorldObject.Object/PlayDirectSound, ZoneScript/GetGameObject | — | — |
| HandlePlayerEnter#4 | method | ZoneScript/HandlePlayerEnter | — | — |
| HandlePlayerLeave#4 | method | ZoneScript/HandlePlayerLeave | — | — |
| SummonFlightMaster | method | ObjectGuid/ObjectGuid#5, Unit.Main/AddAura, Unit.Main/RemoveAllAuras, Unit.Main/SetFactionTemplateId, ZoneScript/AddCreature, ZoneScript/GetCreature | — | — |
| OutdoorPvPEP | ctor | — | — | — |
| SetupZoneScript | method | OutdoorPvP/AddCapturePoint, ZoneScript/RegisterZone | — | — |
| Update | method | Map.Main/SendDefenseMessage, ZoneScript/GetMap#2, ZoneScript/SendUpdateWorldState#2, ZoneScript/Update#2 | — | — |
| OnPlayerEnter | method | Player.Main/GetTeam, SpellCaster/CastSpell#2, ZoneScript/OnPlayerEnter | — | — |
| OnPlayerLeave | method | Player.Main/GetTeam, Unit.Main/RemoveAurasDueToSpell, ZoneScript/OnPlayerLeave | — | — |
| BuffTeams | method | Map.Main/GetPlayer, SpellCaster/CastSpell#2, Unit.Main/RemoveAurasDueToSpell, ZoneScript/GetMap#2 | — | — |
| FillInitialWorldStates#5 | method | WorldStates/WriteInitialWorldStatePair, ZoneScript/FillInitialWorldStates | — | — |
| SendRemoveWorldStates | method | Player.Main/SendUpdateWorldState | — | — |
| OutdoorPvP_eastern_plaguelands | ctor | ZoneScript_Script/ZoneScript_Script | — | — |
| GetMapId | method | — | — | — |
| GetZoneScript | method | — | — | — |
| AddSC_outdoorpvp_ep | function | ZoneScriptMgr/AddScript | Register/RegisterZoneScripts | — |
