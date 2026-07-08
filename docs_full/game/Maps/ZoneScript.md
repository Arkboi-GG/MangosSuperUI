<!-- provenance: failed-members -->
# ZoneScript

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ZoneScript

**Purpose & Responsibilities**

`ZoneScript` and its derived classes (`OutdoorPvP`, `OPvPCapturePoint`) provide the framework for implementing zone-specific gameplay mechanics, primarily focusing on large-scale Outdoor Player vs. Player (PvP) battlegrounds and war effort systems (such as Warsong Gulch, Arathi Basin, or Eastern Plaguelands).

The system operates on three levels:
1.  **`ZoneScript`**: The base class for any zone-specific logic. It tracks players present in the zone, manages world state updates (UI elements like timers or flags), and provides hooks for events like creature spawns, deaths, and player entries.
2.  **`OutdoorPvP`**: A specialization of `ZoneScript` designed for PvP zones. It manages multiple capture points (`OPvPCapturePoint`), handles kill rewards for players within objectives, and coordinates the global state of the PvP zone.
3.  **`OPvPCapturePoint`**: Represents a single capture point (e.g., a tower or shrine) within an `OutdoorPvP` zone. It calculates capture progress based on the number of Alliance vs. Horde players within its radius, updates the client-side slider UI, and manages the spawning/despawning of associated Game Objects (banners, shrines) and Creatures (guards, flight masters).

This unit contains **no direct database table interactions**. All configuration data (spawn locations, capture point radii, max capture times) is loaded from memory via `ObjectMgr` templates (`GameObjectTemplate`, `CreatureTemplate`).

---

## Member-by-Member Behavior

### ZoneScript Base Functionality

The `ZoneScript` class serves as the foundation for zone scripts. It maintains two sets of player GUIDs, indexed by team ID (`m_players[0]` for Alliance, `m_players[1]` for Horde).

*   **`ZoneScript` / `~ZoneScript`**: Constructors and destructors. The destructor is empty; cleanup is expected to be handled by the manager or derived classes.
*   **`OnPlayerEnter`**: Adds the entering player's GUID to the appropriate team set in `m_players`. This ensures the player receives subsequent world state updates for this zone.
*   **`OnPlayerLeave`**: Removes the player from `m_players`. Crucially, it calls `SendRemoveWorldStates` (virtual, default empty) to clean up UI elements specific to this zone before the player fully logs out or leaves. It logs the departure.
*   **`Update`**: A virtual tick method called periodically by `ZoneScriptMgr`. The base implementation does nothing; derived classes override this to process game logic.
*   **`SendUpdateWorldState`**: Iterates through all tracked players in `m_players` and sends a `WorldStateUpdate` packet to each. This is used to update UI elements like timers, scores, or flag positions.
*   **`BroadcastPacket`**: Sends a raw `WorldPacket` to all tracked players. The comment notes this is faster than `sWorld.SendZoneMessage` because it targets only players known to be in this specific script's scope.
*   **`RegisterZone`**: Registers this script instance with `ZoneScriptMgr` for a specific zone ID. This links the script to the zone so that `ZoneScriptMgr` can invoke it when players enter/leave that zone.
*   **`HasPlayer`**: Checks if a specific player is currently tracked in this zone script.
*   **`TeamCastSpell`**: Casts a spell on all players of a specific team. If `spellId` is positive, it casts the spell. If negative, it removes auras caused by that spell ID (used for debuffs or removing buffs).
*   **`TeamApplyBuff`**: Applies a buff to one team and optionally a different buff (or removal) to the opposing team. It uses `OTHER_TEAM` macro to determine the enemy faction.
*   **`GetCreature` / `GetGameObject`**: Helper methods to retrieve pointers to creatures or game objects by GUID from the current map (`m_pMap`). They assert that `m_pMap` is valid.
*   **`SetMap`**: Sets the internal map pointer. Called by `ZoneScriptMgr` during initialization.
*   **`GetMap`**: Returns the current map pointer.
*   **`FillInitialWorldStates`**: Virtual method intended to populate initial world state packets for a player joining the zone. Default returns 0.
*   **Event Hooks**: `HandleAreaTrigger`, `HandleCustomSpell`, `HandleOpenGo`, `HandleGossipOption`, `HandleDropFlag`, `HandleKill`, `AwardKillBonus`, `OnGameObjectCreate`, `OnGameObjectRemove`, `OnCreatureRemove`, `OnPlayerDeath`, `OnObjectCreate`, `OnCreatureCreate`, `OnCreatureEnterCombat`, `OnCreatureEvade`, `OnCreatureRespawn`, `OnCreatureDeath`, `OnCreatureSpellHit`, `OnUnitDeath`, `SetupZoneScript`. These are virtual hooks with default empty implementations. Derived classes override these to implement specific zone logic.

### OutdoorPvP Specifics

`OutdoorPvP` extends `ZoneScript` to manage multiple capture points.

*   **`OutdoorPvP` / `~OutdoorPvP`**: Constructor initializes `m_objective_changed` to false. Destructor deletes all `OPvPCapturePoint` instances stored in `m_capturePoints`. Note: `DeleteSpawns()` is commented out in the destructor because maps should already be unloaded by then.
*   **`DeleteSpawns`**: Iterates through all capture points, calling their `DeleteSpawns()` method to remove spawned entities, then deletes the capture point objects themselves and clears the map.
*   **`OnPlayerEnter`**: Calls the base `ZoneScript::OnPlayerEnter`. It does *not* automatically add the player to capture points; that happens when they enter the specific radius of a point.
*   **`OnPlayerLeave`**: Iterates through all capture points and calls `HandlePlayerLeave` on each, ensuring the player is removed from all active objective lists. Then calls base `ZoneScript::OnPlayerLeave`.
*   **`Update`**: Resets `m_objective_changed` to false. Iterates through all capture points and calls their `Update(diff)` method. If any capture point reports a state change, `m_objective_changed` is set to true. This flag is likely used by the caller to decide whether to broadcast global UI updates.
*   **`HandleKill`**: Handles kill credits for PvP rewards.
    *   If the killer is in a group, it iterates through group members.
    *   It checks if the group member is within reward distance (`IsAtGroupRewardDistance`).
    *   It grants credit if:
        1.  The player is inside an objective (`IsInsideObjective`) AND is active in Outdoor PvP (`IsOutdoorPvPActive`).
        2.  OR, if the killed unit is a Creature (`TYPEID_UNIT`). Creature kills often grant honor/reputation regardless of location in some contexts, or this logic ensures creature kills in the zone still count towards certain metrics.
    *   If the killer is solo, similar logic applies: credit is granted if inside objective/active OR if killing a creature.
    *   Calls `HandleKillImpl` (virtual, empty base) for specific reward logic.
*   **`IsInsideObjective`**: Checks if a player is currently considered "inside" any of the zone's capture points by querying each `OPvPCapturePoint`.
*   **`HandleCustomSpell`**, **`HandleOpenGo`**, **`HandleGossipOption`**, **`HandleDropFlag`**, **`HandleAreaTrigger`**: These iterate through all capture points and delegate to the respective handler in each `OPvPCapturePoint`. If any capture point handles the event (returns true or non-negative), the method returns true. This allows specific capture points to intercept interactions (e.g., using a flag stand).
*   **`OnGameObjectRemove`**: Specifically handles the removal of capture point Game Objects. If a GO of type `GAMEOBJECT_TYPE_CAPTURE_POINT` is removed, it finds the corresponding `OPvPCapturePoint` and sets its `m_capturePoint` pointer to `nullptr` to prevent dangling pointers.

### OPvPCapturePoint Specifics

`OPvPCapturePoint` manages the logic for a single capture point.

*   **`OPvPCapturePoint`**: Initializes member variables. `m_PvP` holds the parent `OutdoorPvP` instance.
*   **`GetMap`**: Retrieves the map from the parent `OutdoorPvP` instance.
*   **`HandlePlayerEnter`**:
    *   Sends world state updates to the player:
        1.  `worldState1`: Set to 1 (likely indicates the player is in range).
        2.  `worldstate3`: Set to `m_neutralValuePct` (neutral slider position).
        3.  `worldstate2`: Set to `m_valuePct` (current slider position). **Critical Comment**: The code emphasizes that `worldstate2` (the slider) must be sent *last*. Sending other world states after it can cause the client to delete the slider UI element.
    *   Adds the player's GUID to `m_activePlayers` for their team. Returns true if added successfully.
*   **`HandlePlayerLeave`**:
    *   Sends `worldState1` set to 0 to the player (indicating they are no longer in range).
    *   Removes the player's GUID from `m_activePlayers`.
*   **`SendChangePhase`**: Sends the current slider value (`m_valuePct`) to all active players. Used when the capture progress changes but the overall state (Neutral/Alliance/Horde) hasn't flipped yet.
*   **`AddObject`**: Spawns a Game Object.
    *   Validates the template exists.
    *   Finds the correct map (handling continent instance IDs for outdoor areas).
    *   Summons the GO.
    *   Stores the GUID in `m_Objects` and maps the GUID back to the type in `m_ObjectTypes`.
*   **`AddCreature`**: Spawns a Creature.
    *   Similar to `AddObject`, validates template, finds map, summons creature with `TEMPSUMMON_MANUAL_DESPAWN`.
    *   Stores GUID in `m_Creatures` and maps GUID to type in `m_CreatureTypes`.
*   **`SetCapturePointData`**: Initializes the capture point itself.
    *   Validates the GO template is of type `GAMEOBJECT_TYPE_CAPTURE_POINT`.
    *   Summons the capture point GO.
    *   Calculates `m_maxValue`, `m_maxSpeed`, `m_minValue`, and `m_neutralValuePct` from the GO template's `capturePoint` data.
    *   Stores the GO pointer and GUID.
*   **`DelCreature`**: Desummons a creature by type.
    *   Retrieves the creature from the parent `OutdoorPvP` (which inherits `ZoneScript`'s `GetCreature`).
    *   Asserts it is a temporary summon and calls `UnSummon`.
    *   Clears the tracking maps.
*   **`DelObject`**: Deletes a Game Object by type.
    *   Retrieves the GO.
    *   Sets respawn time to 0 (prevents saving respawn data).
    *   Calls `Delete`.
    *   Clears tracking maps.
*   **`DelCapturePoint`**: Deletes the main capture point GO. Sets respawn time to 0 and deletes it. Clears the GUID.
*   **`DeleteSpawns`**: Iterates through `m_Objects` and `m_Creatures`, deleting them via `DelObject` and `DelCreature`. Finally calls `DelCapturePoint`.
*   **`Update`**: The core capture logic.
    1.  **Cleanup**: Iterates `m_activePlayers`. If a player is no longer in range, not in the world, or not active in PvP, they are removed from `m_activePlayers` and sent a `worldState1 = 0` update.
    2.  **Scan**: Uses `Cell::VisitWorldObjects` to find all players within the capture point's radius.
    3.  **Add New Players**: For each player found, if they are in the world, active in PvP, and *not* already in `m_activePlayers`, they are added via `HandlePlayerEnter`.
    4.  **Calculate Progress**:
        *   Calculates `fact_diff` based on the difference in player counts between Alliance (index 0) and Horde (index 1), scaled by time delta.
        *   Determines the `Challenger` team (Alliance if diff > 0, Horde if diff < 0, None if equal).
        *   Clamps `fact_diff` to `m_maxSpeed`.
        *   Updates `m_value` by adding `fact_diff`.
    5.  **Determine State**:
        *   **Red (Horde)**: If `m_value <= -m_minValue`. If `<= -m_maxValue`, state is `OBJECTIVESTATE_HORDE`. Else `OBJECTIVESTATE_HORDE_PROGRESSING`. Team is Horde.
        *   **Blue (Alliance)**: If `m_value >= m_minValue`. If `>= m_maxValue`, state is `OBJECTIVESTATE_ALLIANCE`. Else `OBJECTIVESTATE_ALLIANCE_PROGRESSING`. Team is Alliance.
        *   **Grey (Neutral)**: Between `-m_minValue` and `m_minValue`. State depends on challenger: `ALLIANCE_CONTESTED`, `HORDE_CONTESTED`, or `NEUTRAL`. Team is Neutral.
    6.  **UI Updates**:
        *   Calculates `m_valuePct` (0-100 scale for the slider).
        *   If the state changed (`m_oldState != m_state`), it calls `ChangeTeam` (if team changed) and `ChangeState` (virtual, implemented by subclasses to handle visual/audio effects). Returns `true` to signal a major change.
        *   If only the slider value or faction difference changed, it calls `SendChangePhase` to update the slider UI. Returns `false`.
*   **`SendUpdateWorldState`**: Sends a world state update to all players in `m_activePlayers`.
*   **`IsInsideObjective`**: Checks if the player is in `m_activePlayers`.
*   **`HandleCustomSpell`**: Currently returns false unless overridden. Checks if player is active in PvP.
*   **`HandleOpenGo`**: Checks if the opened GO is one of the managed objects (`m_ObjectTypes`). Returns the type ID if found, -1 otherwise.
*   **`HandleGossipOption`**, **`HandleDropFlag`**: Default implementations return false.

---

## Cross-Unit Boundaries

### ZoneScript
*   **Calls `ZoneScriptMgr`**:
    *   `RegisterZone` calls `ZoneScriptMgr::AddZone` to register the script.
*   **Calls `Map`**:
    *   `GetCreature`/`GetGameObject` call `Map::GetCreature`/`Map::GetGameObject`.
    *   `SendUpdateWorldState`/`BroadcastPacket` call `Map::GetPlayer` to resolve GUIDs to Player pointers.
*   **Calls `Player`**:
    *   `OnPlayerEnter`/`OnPlayerLeave` access `Player::GetTeamId` and `Player::GetObjectGuid`.
    *   `OnPlayerLeave` calls `Player::GetSession` -> `WorldSession::PlayerLogout`.
    *   `SendUpdateWorldState` calls `Player::SendUpdateWorldState`.
    *   `BroadcastPacket` calls `Player::GetSession` -> `WorldSession::SendPacket`.
    *   `TeamCastSpell` calls `Player::CastSpell` and `Player::RemoveAurasDueToSpell`.
*   **Called by `ZoneScriptMgr`**:
    *   `Update` is called by `ZoneScriptMgr::Update`.
    *   `OnPlayerEnter`/`OnPlayerLeave` are called by `ZoneScriptMgr::HandlePlayerEnterZone`/`HandlePlayerLeaveZone`.
    *   `SetupZoneScript` is called by `ZoneScriptMgr::InitMapZoneScripts`.
    *   `SetMap` is called by `ZoneScriptMgr::InitMapZoneScripts`.
*   **Called by `Map`**:
    *   `OnPlayerEnter` is called by `Map::Add` (when a player enters the map/zone).
    *   `OnPlayerLeave` is called by `Map::Remove` (when a player leaves).
*   **Called by `GameObject`/`Creature`**:
    *   Various `On...` hooks (`OnCreatureCreate`, `OnCreatureDeath`, etc.) are called by the respective entity managers when those events occur.

### OutdoorPvP
*   **Calls `OPvPCapturePoint`**:
    *   `Update` calls `OPvPCapturePoint::Update`.
    *   `OnPlayerLeave` calls `OPvPCapturePoint::HandlePlayerLeave`.
    *   `IsInsideObjective` calls `OPvPCapturePoint::IsInsideObjective`.
    *   `HandleCustomSpell`/`HandleOpenGo`/etc. delegate to `OPvPCapturePoint` methods.
    *   `DeleteSpawns` calls `OPvPCapturePoint::DeleteSpawns`.
*   **Calls `ZoneScript`**:
    *   Inherits and calls `ZoneScript::OnPlayerEnter` and `ZoneScript::OnPlayerLeave`.
*   **Calls `Player`**:
    *   `HandleKill` calls `Player::GetGroup`, `Player::IsAtGroupRewardDistance`, `Player::IsOutdoorPvPActive`.
*   **Calls `Group`**:
    *   `HandleKill` iterates `Group::GetFirstMember` and `GroupReference::next`.
*   **Calls `Unit`**:
    *   `HandleKill` checks `Unit::GetTypeId`.
*   **Called by `OutdoorPvPEP` (Eastern Plaguelands)**:
    *   Many `OutdoorPvPEP` methods call `OutdoorPvP` methods like `HandlePlayerEnter`, `HandlePlayerLeave`, `ChangeState`, `SummonBannerAura`, etc. (Note: `OutdoorPvPEP` is a subclass of `OutdoorPvP`, so these are often overrides or calls to base functionality).
*   **Called by `OutdoorPvPSI` (Strand of the Ancients?)**:
    *   Similar to EP, `OutdoorPvPSI` interacts with `OutdoorPvP` base methods.

### OPvPCapturePoint
*   **Calls `OutdoorPvP`**:
    *   `GetMap` calls `OutdoorPvP::GetMap`.
    *   `DelCreature`/`DelObject` call `OutdoorPvP::GetCreature`/`GetGameObject` (inherited from `ZoneScript`).
*   **Calls `Map`**:
    *   `AddObject`/`AddCreature`/`SetCapturePointData` call `Map::GetId`, `Map::SummonGameObject`, `Map::SummonCreature`.
    *   `Update` calls `Map::GetPlayer`.
*   **Calls `MapManager`**:
    *   `AddObject`/`AddCreature`/`SetCapturePointData` call `MapManager::FindMap` and `MapManager::GetContinentInstanceId` to locate the correct map instance for outdoor spawns.
*   **Calls `ObjectMgr`**:
    *   `AddObject`/`SetCapturePointData` call `ObjectMgr::GetGameObjectTemplate`.
    *   `AddCreature` calls `ObjectMgr::GetCreatureTemplate`.
*   **Calls `GameObject`**:
    *   `HandlePlayerEnter`/`HandlePlayerLeave`/`SendChangePhase` call `GameObject::GetGOInfo` to access capture point UI data.
    *   `DelObject`/`DelCapturePoint` call `GameObject::SetRespawnTime` and `GameObject::Delete`.
*   **Calls `Creature`**:
    *   `DelCreature` calls `Creature::IsTemporarySummon` and casts to `TemporarySummon` to call `UnSummon`.
*   **Calls `Player`**:
    *   `HandlePlayerEnter`/`HandlePlayerLeave`/`Update` call `Player::GetTeamId`, `Player::GetObjectGuid`, `Player::SendUpdateWorldState`, `Player::IsOutdoorPvPActive`, `Player::IsInWorld`.
*   **Calls `Log`**:
    *   Various methods log errors/debug info via `Log::Out`.
*   **Called by `OutdoorPvPEP`**:
    *   `OutdoorPvPEP` creates `OPvPCapturePoint` instances (CGT, EWT, NPT, PWT) and calls their constructors.
    *   `OutdoorPvPEP` calls `OPvPCapturePoint::ChangeState`, `SummonBannerAura`, `SummonCuringShrine`, `SummonFlightMaster`, etc. (These are likely overrides or specific methods in the EP subclass, but the base `OPvPCapturePoint` provides the infrastructure).

---

## Data Model

This unit does not interact with any database tables directly. All data is sourced from in-memory templates (`GameObjectTemplate`, `CreatureTemplate`) managed by `ObjectMgr`.

---

## Notable Implementation Details

1.  **Slider UI Sensitivity**: In `OPvPCapturePoint::HandlePlayerEnter`, the order of `SendUpdateWorldState` calls is critical. The comment explicitly states that `worldstate2` (the slider position) must be sent *last*. Sending any other world state update after it causes the client to delete the slider UI element. This is a client-side quirk that the server must accommodate.
2.  **Capture Logic**: The capture progress (`m_value`) is a floating-point value that ranges from `-m_maxValue` (Horde captured) to `+m_maxValue` (Alliance captured). The neutral zone is between `-m_minValue` and `+m_minValue`. The speed of capture is limited by `m_maxSpeed`, which is derived from the GO template's `minTime` and `maxTime`.
3.  **Kill Credit Logic**: In `OutdoorPvP::HandleKill`, creature kills grant credit even if the player is not inside an objective or not active in PvP. This is distinct from player kills, which require the player to be inside an objective and active. This likely supports honor gain from mob kills in PvP zones.
4.  **Map Instance Handling**: When spawning objects or creatures, `OPvPCapturePoint` methods check if the target map ID matches the current map. If not, it uses `MapManager::FindMap` with `GetContinentInstanceId` to locate the correct outdoor map instance. This is crucial for zones that span multiple map IDs or have separate instance IDs for different factions/areas.
5.  **Memory Management**: `OutdoorPvP` owns `OPvPCapturePoint` instances and is responsible for deleting them in its destructor. `OPvPCapturePoint` manages the lifecycle of its spawned creatures and game objects via `AddCreature`/`DelCreature` and `AddObject`/`DelObject`.
6.  **Thread Safety**: The code assumes that `ZoneScript` methods are called from the main game thread (where map and player operations are safe). No explicit locking is visible.
7.  **Client Build Check**: The `OutdoorPvP` and `OPvPCapturePoint` classes are wrapped in `#if SUPPORTED_CLIENT_BUILD > CLIENT_BUILD_1_11_2`. This indicates that the outdoor PvP system described here is only active for client builds newer than 1.11.2 (likely TBC or later). For older clients, these classes are not compiled.

---

## Member Reference

**OPvPCapturePoint** (ctor): Initializes the capture point object with default values and a pointer to the parent `OutdoorPvP` instance.

**GetMap**: Returns the map pointer from the parent `OutdoorPvP` instance.

**HandlePlayerEnter**: Sends world state updates to the player indicating they are in range and adds them to the active players list for their team. Critical: Slider update must be last.

**HandlePlayerLeave**: Sends world state update indicating player is out of range and removes them from the active players list.

**SendChangePhase**: Sends the current slider value to all active players in the capture point.

**AddObject**: Spawns a Game Object, validates template, finds correct map, summons it, and tracks its GUID and type.

**~OPvPCapturePoint** (dtor): Empty destructor. Cleanup is handled by `DeleteSpawns` called by parent.

**FillInitialWorldStates**: Virtual method, default returns 0. Intended to populate initial UI states.

**AddCreature**: Spawns a Creature, validates template, finds correct map, summons it as a temporary summon, and tracks its GUID and type.

**ChangeState**: Pure virtual method. Must be implemented by subclasses to handle visual/audio effects when the capture state changes.

**ChangeTeam**: Virtual method, default empty. Called when the controlling team of the capture point changes.

**SetCapturePointData**: Initializes the main capture point Game Object, validates it is a capture point type, summons it, and calculates capture parameters from the template.

**DelCreature**: Desummons a tracked creature by type, asserts it is temporary, and clears tracking maps.

**FillInitialWorldStates#2**: Alias for `FillInitialWorldStates` in `OutdoorPvP`.

**HandleAreaTrigger#2**: Alias for `HandleAreaTrigger` in `OutdoorPvP`.

**HandleCustomSpell#3**: Alias for `HandleCustomSpell` in `OutdoorPvP`.

**HandleOpenGo#3**: Alias for `HandleOpenGo` in `OutdoorPvP`.

**DelObject**: Deletes a tracked Game Object by type, sets respawn time to 0, and clears tracking maps.

**SetupZoneScript**: Virtual method, default returns true. Called during initialization.

**OnGameObjectCreate**: Virtual hook, default empty. Called when a GO is created in the zone.

**OnGameObjectRemove#2**: Alias for `OnGameObjectRemove` in `OutdoorPvP`.

**OnCreatureRemove**: Virtual hook, default empty. Called when a creature is removed from the zone.

**OnPlayerDeath**: Virtual hook, default empty. Called when a player dies in the zone.

**OnObjectCreate**: Virtual hook, default empty. Called when a GO is created.

**OnCreatureCreate**: Virtual hook, default empty. Called when a creature is created in the zone.

**OnCreatureEnterCombat**: Virtual hook, default empty. Called when a creature enters combat.

**OnCreatureEvade**: Virtual hook, default empty. Called when a creature evades combat.

**OnCreatureRespawn**: Virtual hook, default empty. Called when a creature respawns.

**OnCreatureDeath**: Virtual hook, default empty. Called when a creature dies.

**OnCreatureSpellHit**: Virtual hook, default empty. Called when a creature is hit by a spell.

**DelCapturePoint**: Deletes the main capture point Game Object, sets respawn time to 0, and clears the GUID.

**HandleKill#2**: Alias

---

<!-- machine-true, projected from graph.json -->

## Map — ZoneScript

*Source:* ZoneScript.cpp, ZoneScript.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| OPvPCapturePoint | ctor | — | OutdoorPvPEP/OPvPCapturePointEP_CGT, OutdoorPvPEP/OPvPCapturePointEP_EWT, OutdoorPvPEP/OPvPCapturePointEP_NPT, OutdoorPvPEP/OPvPCapturePointEP_PWT | — |
| GetMap | method | — | — | — |
| HandlePlayerEnter | method | GameObject/GetGOInfo, Object/GetObjectGuid, Player.Main/GetTeamId, Player.Main/SendUpdateWorldState | OutdoorPvPEP/HandlePlayerEnter, OutdoorPvPEP/HandlePlayerEnter#2, OutdoorPvPEP/HandlePlayerEnter#3, OutdoorPvPEP/HandlePlayerEnter#4 | — |
| HandlePlayerLeave | method | GameObject/GetGOInfo, Object/GetObjectGuid, Player.Main/GetTeamId, Player.Main/SendUpdateWorldState | OutdoorPvPEP/HandlePlayerLeave, OutdoorPvPEP/HandlePlayerLeave#2, OutdoorPvPEP/HandlePlayerLeave#3, OutdoorPvPEP/HandlePlayerLeave#4 | — |
| SendChangePhase | method | GameObject/GetGOInfo | — | — |
| AddObject | method | Log.Main/Out, Map.Main/GetId, Map.Main/SummonGameObject, MapManager/FindMap, MapManager/GetContinentInstanceId, Object/GetObjectGuid, ObjectMgr/GetGameObjectTemplate | OutdoorPvPEP/ChangeState, OutdoorPvPEP/ChangeState#2, OutdoorPvPEP/ChangeState#3, OutdoorPvPEP/ChangeState#4, OutdoorPvPEP/OPvPCapturePointEP_CGT, OutdoorPvPEP/OPvPCapturePointEP_EWT, OutdoorPvPEP/OPvPCapturePointEP_NPT, OutdoorPvPEP/OPvPCapturePointEP_PWT, OutdoorPvPEP/SummonBannerAura, OutdoorPvPEP/SummonCuringShrine | — |
| ~OPvPCapturePoint | dtor | — | — | — |
| FillInitialWorldStates | method | — | OutdoorPvPEP/FillInitialWorldStates#5 | — |
| AddCreature | method | Log.Main/Out, Map.Main/GetId, MapManager/FindMap, MapManager/GetContinentInstanceId, Object/GetObjectGuid, ObjectMgr/GetCreatureTemplate, WorldObject.Object/SummonCreature | OutdoorPvPEP/ChangeState, OutdoorPvPEP/ChangeState#2, OutdoorPvPEP/ChangeState#3, OutdoorPvPEP/ChangeState#4, OutdoorPvPEP/SummonFlightMaster, OutdoorPvPEP/SummonSpiritOfVictory, OutdoorPvPEP/SummonSquadAtEastWallTower | — |
| ChangeState | decl | — | — | — |
| ChangeTeam | method | — | — | — |
| SetCapturePointData | method | Log.Main/Out, Map.Main/GetId, Map.Main/SummonGameObject, MapManager/FindMap, MapManager/GetContinentInstanceId, Object/GetGUIDLow, ObjectMgr/GetGameObjectTemplate | OutdoorPvPEP/OPvPCapturePointEP_CGT, OutdoorPvPEP/OPvPCapturePointEP_EWT, OutdoorPvPEP/OPvPCapturePointEP_NPT, OutdoorPvPEP/OPvPCapturePointEP_PWT | — |
| DelCreature | method | Creature.Main/IsTemporarySummon, Errors/PrintStacktraceAndThrow, Log.Main/Out, ObjectGuid/ObjectGuid#5, TemporarySummon/UnSummon | OutdoorPvPEP/ChangeState, OutdoorPvPEP/ChangeState#2, OutdoorPvPEP/ChangeState#3, OutdoorPvPEP/ChangeState#4, OutdoorPvPEP/RemoveSquad, OutdoorPvPEP/SummonSpiritOfVictory, OutdoorPvPEP/SummonSquadAtEastWallTower | — |
| FillInitialWorldStates#2 | method | — | Player.Main/SendInitWorldStates | — |
| HandleAreaTrigger#2 | method | — | WorldSession.MiscHandler/HandleAreaTriggerOpcode | — |
| HandleCustomSpell#3 | method | — | ZoneScriptMgr/HandleCustomSpell | — |
| HandleOpenGo#3 | method | — | ZoneScriptMgr/HandleOpenGo | — |
| DelObject | method | GameObject/Delete, GameObject/SetRespawnTime, ObjectGuid/ObjectGuid#5 | OutdoorPvPEP/ChangeState, OutdoorPvPEP/ChangeState#2, OutdoorPvPEP/ChangeState#3, OutdoorPvPEP/ChangeState#4 | — |
| SetupZoneScript | method | — | ZoneScriptMgr/InitMapZoneScripts | — |
| OnGameObjectCreate | method | — | GameObject/AddToWorld | — |
| OnGameObjectRemove#2 | method | — | GameObject/RemoveFromWorld | — |
| OnCreatureRemove | method | — | Creature.Main/RemoveFromWorld | — |
| OnPlayerDeath | method | — | Player.Main/SetDeathState | — |
| OnObjectCreate | method | — | GameObject/Create | — |
| OnCreatureCreate | method | — | Creature.Main/AddToWorld, Totem/Create | — |
| OnCreatureEnterCombat | method | — | Creature.Main/OnEnterCombat, ScriptedInstance/OnCreatureEnterCombat | — |
| OnCreatureEvade | method | — | Creature.Main/OnLeaveCombat | — |
| OnCreatureRespawn | method | — | Creature.Main/Update | — |
| OnCreatureDeath | method | — | Unit.Main/Kill | — |
| OnCreatureSpellHit | method | — | Spell.Main/DoAllEffectOnTarget#3 | — |
| DelCapturePoint | method | GameObject/Delete, GameObject/SetRespawnTime | — | — |
| HandleKill#2 | method | — | Unit.Main/Kill | — |
| AwardKillBonus | method | — | — | — |
| HandleDropFlag#3 | method | — | spell_special/OnAfterApply#4, Unit.Main/Mount, Unit.SpellAuras/HandleModStealth, ZoneScriptMgr/HandleDropFlag | — |
| HandleGossipOption#3 | method | — | ZoneScriptMgr/HandleGossipOption | — |
| GetMap#2 | method | — | boss_archaedas/UpdateAI, boss_cthun/SelectRandomAliveNotStomach, boss_four_horsemen/UpdateAI#3, boss_gluth/SpellHit, boss_gothik/HasLessPlayersPerSide, boss_gothik/SummonAdd, boss_maexxna/UpdateWraps, boss_skeram/Aggro, boss_thaddius/DoPolarityShift, boss_twinemperors/OnEndTeleport, dreadsteed_ritual/UpdateAI#4, instance_blackrock_depths/SetData, instance_blackrock_depths/Update, instance_blackrock_spire/Update, instance_dire_maul/OnCreatureDeath, instance_naxxramas.boss_kelthuzad/Reset, instance_naxxramas.Main/onNaxxramasAreaTrigger, instance_naxxramas.Main/SetData, instance_naxxramas.Main/Update, instance_razorfen_kraul/SetData, instance_ruins_of_ahnqiraj/IsAnyBossInCombat, instance_ruins_of_ahnqiraj/OnCreatureEnterCombat, instance_ruins_of_ahnqiraj/Update, instance_temple_of_ahnqiraj/HandleStomachTriggers, instance_temple_of_ahnqiraj/KillPlayersInStomach, instance_temple_of_ahnqiraj/PerformCthunKnockback, instance_temple_of_ahnqiraj/UpdateCThunWhisper, instance_temple_of_ahnqiraj/UpdateStomachOfCthun, instance_uldaman/SetData64, OutdoorPvPEP/BuffTeams, OutdoorPvPEP/ChangeState, OutdoorPvPEP/ChangeState#2, OutdoorPvPEP/ChangeState#3, OutdoorPvPEP/ChangeState#4, OutdoorPvPEP/Update, OutdoorPvPSI/ResetResourceCount, OutdoorPvPSI/SpawnDustBags, ScriptedInstance/Update, ZoneScriptMgr/OnMapCrashed | — |
| SetMap | method | — | ZoneScriptMgr/InitMapZoneScripts | — |
| DeleteSpawns | method | — | — | — |
| OnUnitDeath | method | — | — | — |
| DeleteSpawns#2 | method | — | — | — |
| SendRemoveWorldStates | method | — | — | — |
| OutdoorPvP | ctor | — | — | — |
| ~OutdoorPvP | dtor | — | — | — |
| OnPlayerLeave | method | — | OutdoorPvPEP/OnPlayerLeave, OutdoorPvPSI/OnPlayerLeave | — |
| OnPlayerEnter | method | — | OutdoorPvPEP/OnPlayerEnter, OutdoorPvPSI/OnPlayerEnter | — |
| Update#2 | method | — | OutdoorPvPEP/Update | — |
| Update | method | AnyPlayerInObjectRangeCheck/AnyPlayerInObjectRangeCheck, GameObject/GetGOInfo, Map.Main/GetPlayer, Object/GetObjectGuid, Object/IsInWorld, Player.Main/GetTeamId, Player.Main/IsOutdoorPvPActive, Player.Main/SendUpdateWorldState, WorldObject.Object/IsWithinDistInMap | — | — |
| SendUpdateWorldState | method | Map.Main/GetPlayer, Player.Main/SendUpdateWorldState | OutdoorPvPEP/SendChangePhase, OutdoorPvPEP/SendChangePhase#2, OutdoorPvPEP/SendChangePhase#3, OutdoorPvPEP/SendChangePhase#4 | — |
| HandleKill | method | Group/GetFirstMember, GroupReference/next, Object/GetTypeId, OutdoorPvP/HandleKillImpl, Player.Main/GetGroup, Player.Main/IsAtGroupRewardDistance, Player.Main/IsOutdoorPvPActive | — | — |
| IsInsideObjective#2 | method | — | — | — |
| IsInsideObjective | method | Object/GetObjectGuid, Player.Main/GetTeamId | — | — |
| HandleCustomSpell#2 | method | — | — | — |
| HandleCustomSpell | method | Player.Main/IsOutdoorPvPActive | — | — |
| HandleOpenGo#2 | method | — | — | — |
| HandleGossipOption#2 | method | — | — | — |
| HandleDropFlag#2 | method | — | — | — |
| HandleGossipOption | method | — | — | — |
| HandleDropFlag | method | — | — | — |
| HandleOpenGo | method | — | — | — |
| HandleAreaTrigger | method | — | — | — |
| OnGameObjectRemove | method | GameObject/GetGoType, Object/GetGUIDLow, OutdoorPvP/GetCapturePoint | — | — |
| ZoneScript | ctor | — | — | — |
| ~ZoneScript | dtor | — | — | — |
| Update#3 | method | — | ZoneScriptMgr/Update | — |
| OnPlayerEnter#2 | method | Object/GetObjectGuid, Player.Main/GetTeamId | Map.Main/Add#3, ZoneScriptMgr/HandlePlayerEnterZone | — |
| OnPlayerLeave#2 | method | Log.Main/Out, Object/GetObjectGuid, Player.Main/GetName, Player.Main/GetSession, Player.Main/GetTeamId, WorldSession.Main/PlayerLogout | Map.Main/Remove#3, ZoneScriptMgr/HandlePlayerLeaveZone | — |
| SendUpdateWorldState#2 | method | Map.Main/GetPlayer, Player.Main/SendUpdateWorldState | OutdoorPvPEP/Update, OutdoorPvPEP/UpdateTowerState, OutdoorPvPEP/UpdateTowerState#2, OutdoorPvPEP/UpdateTowerState#3, OutdoorPvPEP/UpdateTowerState#4, OutdoorPvPSI/UpdateWorldState | — |
| BroadcastPacket | method | Map.Main/GetPlayer, Player.Main/GetSession, WorldSession.Main/SendPacket | — | — |
| RegisterZone | method | ZoneScriptMgr/AddZone | OutdoorPvPEP/SetupZoneScript, OutdoorPvPSI/SetupZoneScript | — |
| HasPlayer | method | Object/GetObjectGuid, Player.Main/GetTeamId | ZoneScriptMgr/HandlePlayerEnterZone, ZoneScriptMgr/HandlePlayerLeaveZone | — |
| TeamCastSpell | method | Map.Main/GetPlayer, SpellCaster/CastSpell#2, Unit.Main/RemoveAurasDueToSpell | — | — |
| TeamApplyBuff | method | — | OutdoorPvPSI/HandleAreaTrigger | — |
| GetCreature | method | Errors/PrintStacktraceAndThrow, Map.Main/GetCreature | blackrock_depths/AreaTrigger_at_ring_of_law, blackrock_depths/GOUse_go_bar_ale_mug, blackrock_depths/PlayerEnteredArena, boss_anubrekhan/Aggro, boss_anubrekhan/ExplodeOneDeadCryptGuard, boss_anubrekhan/JustReachedHome, boss_buru/DamageTaken, boss_buru/Reset, boss_buru/UpdateAI, boss_cthun/AttackStart, boss_cthun/CheckRespawnEye, boss_cthun/FixPortalPosition, boss_cthun/UpdateAI#2, boss_loatheb/WhackAStalk, boss_razuvious/getRPBuddy, boss_razuvious/RespawnAdds, boss_sapphiron/UnSummonWingBuffet, boss_thaddius/CheckSpawnAdds, boss_thaddius/GetOtherAdd, boss_thaddius/HandleCheckSpawnAdd, boss_thaddius/HandleUnsummonAdd, boss_thaddius/HandleUnsummonCoil, boss_thaddius/UpdateAI#3, boss_thaddius/UpdateTransitionPhase, instance_blackrock_depths/Update, instance_blackrock_spire/AreaTrigger_at_blackrock_spire, instance_blackrock_spire/DoSendNextStadiumWave, instance_blackrock_spire/GetSpeakerByEntry, instance_blackrock_spire/JustDidDialogueStep, instance_blackrock_spire/OnCreatureDeath, instance_blackrock_spire/SetData, instance_blackwing_lair/SetData, instance_naxxramas.boss_kelthuzad/EvadeAllGuardians, instance_scarlet_monastery/AreaTrigger_at_cathedral_entrance, instance_scarlet_monastery/IsMograineOrWhitemaneDead, instance_scarlet_monastery/OnCreatureDeath, instance_scarlet_monastery/SetData, instance_scarlet_monastery/Update, OutdoorPvPEP/ChangeState, OutdoorPvPEP/ChangeState#2, OutdoorPvPEP/ChangeState#3, OutdoorPvPEP/ChangeState#4, OutdoorPvPEP/SummonFlightMaster, OutdoorPvPEP/SummonSpiritOfVictory, OutdoorPvPEP/SummonSquadAtEastWallTower, ScriptMgr/GetTargetByType, wailing_caverns/JustDied, wailing_caverns/MovementInform, wailing_caverns/UpdateEscortAI, wailing_caverns/WaypointReached | — |
| GetGameObject | method | Errors/PrintStacktraceAndThrow, Map.Main/GetGameObject | blackrock_depths/UpdateEscortAI#4, blackrock_depths/WaypointReached#5, boss_heigan/UpdateEruption, dreadsteed_ritual/UpdateAI#4, instance_blackrock_depths/SetData, instance_maraudon/Update, instance_naxxramas.boss_kelthuzad/UpdateP1, instance_scarlet_monastery/SetData, instance_temple_of_ahnqiraj/SetData, instance_uldaman/SetData, OutdoorPvPEP/PlaySound, OutdoorPvPEP/PlaySound#2, OutdoorPvPEP/PlaySound#3, OutdoorPvPEP/PlaySound#4, OutdoorPvPEP/SummonCuringShrine, OutdoorPvPEP/UpdateBannerArt, OutdoorPvPEP/UpdateBannerArt#2, OutdoorPvPEP/UpdateBannerArt#3, OutdoorPvPEP/UpdateBannerArt#4, ScriptMgr/GetTargetByType | — |

---

<!-- verify: failed-members | missing: DeleteSpawns#2, GetMap#2, HandleCustomSpell#2, HandleDropFlag#2, HandleDropFlag#3, HandleGossipOption#2, HandleGossipOption#3, HandleOpenGo#2, IsInsideObjective#2, OnPlayerEnter#2, OnPlayerLeave#2, SendUpdateWorldState#2, Update#2, Update#3 -->
