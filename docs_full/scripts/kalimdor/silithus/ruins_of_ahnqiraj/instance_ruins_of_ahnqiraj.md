# instance_ruins_of_ahnqiraj

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# instance_ruins_of_ahnqiraj

## Purpose & Responsibilities

`instance_ruins_of_ahnqiraj` is the scripted instance data handler for the **Ruins of Ahn'Qiraj** dungeon in World of Warcraft. It manages the state, logic, and object lifecycle for the seven primary encounters within the instance: Kurinnaxx, General Andorov, General Rajaxx, Buru the Gorger, Ayamiss the Hunter, Moam, and Ossirian the Unscarred.

Key responsibilities include:
1.  **Encounter State Management:** Tracking the progress (`NOT_STARTED`, `IN_PROGRESS`, `DONE`, `FAIL`) of each boss and persisting this state to the database upon completion.
2.  **General Andorov & Rajaxx Logic:** Implementing complex interdependent mechanics between the Andorov and Rajaxx encounters, including summoning Andorov, managing his squad's immunity and respawn times, handling combat triggers based on WoW patch versions (specifically 1.10+ changes), and calculating reputation rewards based on surviving allies.
3.  **Ossirian Crystal Spawning:** Managing the dynamic spawning of crystals for the Ossirian encounter, ensuring they appear within valid proximity constraints relative to previously used crystals.
4.  **Creature & GameObject Lifecycle:** Registering GUIDs for key NPCs and objects upon creation, handling specific death behaviors (despawns, respawn timers), and managing special object states (e.g., Ossirian pylons).
5.  **Patch-Specific Behavior:** Adjusting mechanics based on the server's configured WoW patch level, particularly for the Rajaxx encounter (door mechanics) and reputation rewards.

## Member-by-Member Behavior

### Initialization and State Management

*   **`instance_ruins_of_ahnqiraj`**: The constructor initializes the instance by calling `Initialize`. It inherits from `ScriptedInstance`.
*   **`Initialize`**: Resets all internal state variables. This includes clearing GUID lists for bosses (Kurinnaxx, Buru, Ayamiss, Moam, Ossirian, Andorov, Rajaxx, and Rajaxx's lieutenants), clearing the Kaldorei Elite and Ossirian Pylon lists, resetting encounter statuses to `NOT_STARTED`, and initializing timers and flags for the Rajaxx event.
*   **`IsEncounterInProgress`**: Returns `true` if any encounter in `m_auiEncounter` is marked as `IN_PROGRESS` or `SPECIAL`. Used to determine if the instance is actively being played.
*   **`GetData`**: Retrieves specific instance data. It returns the count of dead Qiraji Gladiators (`m_uiGladiatorDeath`) or the status of a specific boss encounter from `m_auiEncounter`.
*   **`GetData64`**: Retrieves the Object GUID for a specific boss or object. For most bosses, it returns the stored GUID. For `DATA_YEGGETH_SHIELD`, it randomly selects one of the creatures in `m_lYeggethShieldList` (populated during combat) and returns its GUID.
*   **`SetData`**: Updates instance state.
    *   **`TYPE_QIRAJI_GLADIATOR`**: Increments the gladiator death counter or resets it.
    *   **`TYPE_GENERAL_ANDOROV`**: Updates Andorov's gossip menu and removes vendor flags if the encounter fails or is in progress. Sets squad immunity to false when starting.
    *   **`TYPE_RAJAXX`**: Handles post-combat logic. If done, it grants Andorov vendor status (patch 1.10+), sets his gossip menu, sets his respawn time to 4 days, and makes the Andorov squad immune. If not started, it sets Andorov squad respawn to 15 minutes if Kurinnaxx is done.
    *   **`TYPE_OSSIRIAN`**: Clears crystal tracking data (`crystalGuids`, `crystalIndexes`, etc.) if the encounter fails or is done.
    *   **Other Bosses**: Simply updates the encounter status.
    *   **Persistence**: After updating any boss status to `DONE`, it serializes the `m_auiEncounter` array into `strInstData` and calls `SaveToDB()` (from `InstanceData`).
*   **`Save`**: Returns the serialized string `strInstData` containing the encounter statuses.
*   **`Load`**: Parses the saved string to restore `m_auiEncounter` statuses. It resets any `IN_PROGRESS` or invalid states to `NOT_STARTED`. Crucially, it always resets `TYPE_GENERAL_ANDOROV` to `NOT_STARTED` upon load, preventing stale state issues.

### Creature Event Handlers

*   **`OnCreatureCreate`**: Registers the GUID of any created creature matching known NPC entries (bosses, lieutenants, elites) into the corresponding member variables or lists (`m_lKaldoreiElites`).
*   **`OnCreatureEnterCombat`**:
    *   **Rajaxx Lieutenants**: If a lieutenant enters combat, it checks if they are part of Yeggeth's group. If so, it adds their GUID to `m_lYeggethShieldList` for the shield mechanic.
    *   **Andorov Trigger (Patch 1.10+)**: If Rajaxx is not done, Andorov is not started/failed, and the patch is >= 1.10, it forces Andorov to respawn (if dead) and starts the `ANDOROV_START_SCRIPT` via `ScriptsStart`. It then marks Andorov's encounter as `IN_PROGRESS`.
    *   Resets `m_bRajaxxEventIsToReset` to `false`.
*   **`OnCreatureEvade`**:
    *   **Rajaxx Lieutenants**: If any lieutenant evades, it sets a timer (`m_uiRajaxxEventResetTimer`) and flag (`m_bRajaxxEventIsToReset`) to reset Rajaxx after 2 seconds.
    *   **Kaldorei Elites**: If Andorov is in combat, it adds the elite's threats to Andorov, pulling them into the fight.
*   **`OnCreatureDeath`**:
    *   **Rajaxx**: Calls `GiveRepAfterRajaxxDeath`.
    *   **Lieutenants/Mobs**: Swarmguard Needlers and Qiraji Warriors are forced to despawn after 3 seconds and set to respawn in 4 days.
    *   **Andorov**: Removes his gossip flag.
    *   **Kaldorei Elites**: Sets respawn time to 4 days if Kurinnaxx is not done or Rajaxx is in progress; otherwise, 15 minutes.

### Object Event Handlers

*   **`OnObjectCreate`**: Handles GameObject entry `180619` (Ossirian Pylons). It adds the GUID to `m_lOssirianPylons`. For a specific GUID (`399461`), it forces a respawn. For others, it hides them initially by setting spawn-by-default to false and updating visibility.

### Update Loop and Mechanics

*   **`Update`**: Called periodically with a time difference (`uiDiff`).
    *   **Rajaxx Door (Patch 1.10+)**: If any boss is in combat (`IsAnyBossInCombat`), it summons a door (GO 176149) if not already present. If no boss is in combat, it removes the door. This prevents players from re-entering the Rajaxx area during other fights.
    *   **Andorov Summoning**: If Kurinnaxx is done, Rajaxx is not done, and Andorov's GUID is missing, it loads Andorov from the database (`LoadCreatureSpawnWithGroup`), sets his gossip menu, and moves him to waypoint 0.
    *   **Rajaxx Reset Timer**: If `m_bRajaxxEventIsToReset` is true, it decrements the timer. When expired, it forces Rajaxx to evade (`EnterEvadeMode`) and clears the flag.
    *   **Crystal Cleanup**: Iterates through `crystalIndexes` and removes entries for crystals that no longer exist in the world.
*   **`IsAnyBossInCombat`**: Checks if Andorov is in progress or if any tracked boss (Kurinnaxx through Zerran) is alive and has a victim.
*   **`SetAndorovSquadRespawnTime`**: Sets the respawn time for Andorov and all Kaldorei Elites to a specified delay, but only if they are currently dead.
*   **`SetAndorovSquadImmunity`**: Toggles the `UNIT_FLAG_IMMUNE_TO_NPC` flag for Andorov and all Kaldorei Elites. Used to prevent players from attacking the squad during non-Rajaxx phases.
*   **`GiveRepAfterRajaxxDeath`**: Calculates and distributes reputation rewards for the Cenarion Circle faction.
    *   Determines if Andorov is alive.
    *   Counts alive Kaldorei Elites within 400 yards. If Andorov is dead, these elites are despawned and set to 4-day respawn.
    *   Calculates base rep (`repForKill`) and per-helper rep (`repPerHelper`) based on patch version (higher in 1.10+).
    *   Awards rep to the loot recipient and all group members, capped at reputation rank 7.
*   **`SpawnNewCrystals`**: Manages the Ossirian crystal spawn logic.
    *   Identifies the location of the recently used crystal.
    *   Maintains a history of recent spawn indices to avoid repetition.
    *   Selects two new crystal locations from `CrystalSpawn` that are within a specific distance range of the previous location (or an existing active crystal).
    *   Spawns the GameObjects (`GO_OSSIRIAN_CRYSTAL`) and tracks their GUIDs and indices.

### Script Registration

*   **`GetInstanceData_instance_ruins_of_ahnqiraj`**: Factory function that creates and returns a new `instance_ruins_of_ahnqiraj` instance.
*   **`AddSC_instance_ruins_of_ahnqiraj`**: Registers the script with the engine, linking the name `"instance_ruins_of_ahnqiraj"` to the factory function.

## Cross-Unit Boundaries

*   **`boss_ossirian`**:
    *   **Called By**: `GetData64` (for `DATA_OSSIRIAN` GUID), `SetData` (for `TYPE_OSSIRIAN` status), `SpawnNewCrystals` (triggered by aggro/use events in `boss_ossirian`).
    *   **Collaboration**: `instance_ruins_of_ahnqiraj` provides the GUID for Ossirian and handles the complex crystal spawning logic triggered by `boss_ossirian`'s actions. `boss_ossirian` relies on this instance data to manage its phase-specific mechanics.
*   **`ScriptedInstance`**:
    *   **Calls Out**: Constructor inherits from `ScriptedInstance`.
    *   **Collaboration**: Provides the base framework for instance scripts, including map access, basic data structures, and database save/load hooks.
*   **`Creature.Main` / `CreatureGroups` / `Map.Main` / `Object` / `ObjectGuid` / `Unit.Main` / `World` / `ZoneScript`**:
    *   **Calls Out**: Various methods interact with these core engine classes to manipulate creatures, objects, and game state (e.g., `GetCreature`, `Respawn`, `GetCreatureGroup`, `GetWowPatch`, `ScriptsStart`).
    *   **Collaboration**: Standard interaction with the WoW server engine to query and modify entity states.
*   **`boss_ossirian` (Specifically `OnUse` and `Aggro`)**:
    *   **Called By**: `GetData64` and `SpawnNewCrystals` are called from `boss_ossirian`'s `OnUse` and `Aggro` handlers.
    *   **Collaboration**: `boss_ossirian` triggers crystal spawning and queries instance data during its encounter phases.

## Data Model

This unit does not directly interact with custom database tables for its core logic. It relies on the standard `instance_data` table (managed by `InstanceData`/`ScriptedInstance`) to persist the `strInstData` string containing encounter statuses. The `Load` and `Save` methods handle serialization/deserialization of this string. No custom SQL queries or table schemas are defined or used within this file.

## Notable Implementation Details

1.  **Patch-Specific Rajaxx Mechanics**: The code explicitly checks `sWorld.GetWowPatch() >= WOW_PATCH_110` to alter behavior. In 1.10+, pulling Rajaxx's lieutenants automatically starts the Andorov encounter and summons a blocking door during any boss fight. This reflects historical WoW patch changes.
2.  **Andorov State Reset on Load**: The `Load` method forcibly resets `TYPE_GENERAL_ANDOROV` to `NOT_STARTED`. This is a critical safeguard to prevent the Andorov encounter from being stuck in a completed or failed state across server restarts, ensuring it can always be initiated again.
3.  **Yeggeth Shield List**: The `m_lYeggethShieldList` is populated dynamically in `OnCreatureEnterCombat` by checking if a creature is in Yeggeth's group. `GetData64` then randomly selects from this list, implying the shield mechanic targets a random member of Yeggeth's active group.
4.  **Crystal Spawn Algorithm**: `SpawnNewCrystals` uses a heuristic approach to place crystals. It avoids recently used locations (via `crystalIndexHistory`) and enforces distance constraints relative to the previous crystal or an existing active crystal. It expands the search radius if initial attempts fail, preventing deadlocks.
5.  **Reputation Calculation**: `GiveRepAfterRajaxxDeath` calculates reputation based on the number of alive helpers (Andorov + Elites). It despawns elites if Andorov is dead, reflecting the lore/mechanic that they retreat without their leader. The rep amounts differ significantly between pre-1.10 and 1.10+ patches.
6.  **Door Management**: The `Update` loop manages a temporary door (GO 176149) that appears when any boss is in combat (post-1.10). This door is summoned and removed dynamically, controlling player movement within the instance.

## Member Reference

**instance_ruins_of_ahnqiraj**: Constructor that initializes the instance by calling `Initialize`. Inherits from `ScriptedInstance`.

**Initialize**: Resets all internal state variables, including boss GUIDs, encounter statuses, lists, and timers.

**IsEncounterInProgress**: Returns `true` if any encounter is `IN_PROGRESS` or `SPECIAL`.

**GetData64**: Returns the GUID for a specific boss or object. For `DATA_YEGGETH_SHIELD`, it randomly selects a GUID from `m_lYeggethShieldList`.

**OnCreatureEnterCombat**: Handles combat entry for Rajaxx's lieutenants (populating shield list, triggering Andorov start in 1.10+) and resets the Rajaxx evade flag.

**OnCreatureEvade**: Handles evade for Rajaxx's lieutenants (setting Rajaxx reset timer) and Kaldorei Elites (pulling them to Andorov if he is in combat).

**OnCreatureCreate**: Registers GUIDs for known NPCs (bosses, lieutenants, elites) into member variables or lists.

**OnObjectCreate**: Handles creation of Ossirian Pylons (entry 180619), adding them to a list and adjusting their spawn/visibility state.

**OnCreatureDeath**: Handles death for Rajaxx (calling rep reward), lieutenants/mobs (despawn/respawn timers), Andorov (removing gossip flag), and Kaldorei Elites (setting respawn timers based on encounter state).

**GetData**: Returns the Qiraji Gladiator death count or the status of a specific boss encounter.

**SetData**: Updates encounter status and triggers associated logic (gossip menus, immunity, respawn times, crystal cleanup). Persists state to DB if a boss is `DONE`.

**Save**: Returns the serialized string of encounter statuses.

**Load**: Parses the saved string to restore encounter statuses, resetting invalid states and forcing Andorov to `NOT_STARTED`.

**Update**: Manages the Rajaxx door (summon/remove based on combat state), summons Andorov if needed, processes the Rajaxx reset timer, and cleans up invalid crystal indices.

**IsAnyBossInCombat**: Checks if Andorov is in progress or if any tracked boss is alive and has a victim.

**SetAndorovSquadRespawnTime**: Sets the respawn time for Andorov and Kaldorei Elites if they are dead.

**SetAndorovSquadImmunity**: Toggles NPC immunity for Andorov and Kaldorei Elites.

**GiveRepAfterRajaxxDeath**: Calculates and awards Cenarion Circle reputation based on alive helpers (Andorov/Elites) and patch version. Despawns elites if Andorov is dead.

**SpawnNewCrystals**: Spawns two new Ossirian crystals at valid locations relative to previously used crystals, maintaining a history to avoid repetition.

**GetInstanceData_instance_ruins_of_ahnqiraj**: Factory function to create a new `instance_ruins_of_ahnqiraj` instance.

**AddSC_instance_ruins_of_ahnqiraj**: Registers the script with the engine.

---

<!-- machine-true, projected from graph.json -->

## Map — instance_ruins_of_ahnqiraj

*Source:* instance_ruins_of_ahnqiraj.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| instance_ruins_of_ahnqiraj | ctor | ScriptedInstance/ScriptedInstance | — | — |
| Initialize | method | ObjectGuid/Clear | — | — |
| IsEncounterInProgress | method | — | — | — |
| GetData64 | method | Map.Main/GetCreature, Object/GetObjectGuid, ObjectGuid/ObjectGuid#5, shared_Util/urand | boss_ossirian/OnUse | — |
| OnCreatureEnterCombat | method | Creature.Main/GetCreatureGroup, Creature.Main/Respawn, CreatureGroups/GetOriginalLeaderGuid, Map.Main/GetCreature, Map.Main/ScriptsStart, Object/GetEntry, Object/GetGUID, Object/GetObjectGuid, ObjectGuid/GetEntry, ObjectGuid/ObjectGuid#5, Unit.Main/IsAlive, World/GetWowPatch, ZoneScript/GetMap#2 | — | — |
| OnCreatureEvade | method | Creature.Main/AddThreatsOf, Map.Main/GetCreature, Object/GetEntry, ObjectGuid/ObjectGuid#5, Unit.Main/IsInCombat | — | — |
| OnCreatureCreate | method | Object/GetEntry, Object/GetGUID | — | — |
| OnObjectCreate | method | GameObject/Refresh, GameObject/Respawn, GameObject/SetRespawnTime, GameObject/SetSpawnedByDefault, Object/GetEntry, Object/GetGUID, WorldObject.Object/UpdateObjectVisibility | — | — |
| OnCreatureDeath | method | Creature.Main/ForcedDespawn, Creature.Main/SetRespawnTime, Object/GetEntry, WorldObject.Object/RemoveFlag | — | — |
| GetData | method | — | — | — |
| SetData | method | Creature.Main/SetDefaultGossipMenuId, Creature.Main/SetRespawnTime, InstanceData/SaveToDB, Log.Main/Out, Map.Main/GetCreature, Map.Main/GetGameObject, Map.Main/GetId, Map.Main/GetInstanceId, Map.Main/GetMapName, ObjectGuid/ObjectGuid#5, World/GetWowPatch, WorldObject.Object/AddObjectToRemoveList, WorldObject.Object/RemoveFlag, WorldObject.Object/SetFlag | boss_ossirian/Aggro, boss_ossirian/JustDied, boss_ossirian/Reset | — |
| Save | method | — | — | — |
| Load | method | Log.Main/Out, Map.Main/GetId, Map.Main/GetInstanceId, Map.Main/GetMapName | — | — |
| Update | method | Creature.Main/AI, Creature.Main/SetDefaultGossipMenuId, Creature.MotionMaster/MoveWaypoint, CreatureAI/EnterEvadeMode, Map.Main/GetCreature, Map.Main/GetGameObject, Map.Main/LoadCreatureSpawnWithGroup, Map.Main/SummonGameObject, Object/GetObjectGuid, ObjectGuid/Clear, ObjectGuid/IsEmpty, ObjectGuid/ObjectGuid#5, Unit.Main/GetMotionMaster, World/GetWowPatch, WorldObject.Object/AddObjectToRemoveList, ZoneScript/GetMap#2 | — | — |
| IsAnyBossInCombat | method | Map.Main/GetCreature, ObjectGuid/ObjectGuid#5, Unit.Main/GetVictim, Unit.Main/IsAlive, ZoneScript/GetMap#2 | — | — |
| SetAndorovSquadRespawnTime | method | Creature.Main/SetRespawnTime, Map.Main/GetCreature, ObjectGuid/ObjectGuid#5, Unit.Main/IsAlive | — | — |
| SetAndorovSquadImmunity | method | Map.Main/GetCreature, ObjectGuid/ObjectGuid#5, WorldObject.Object/RemoveFlag, WorldObject.Object/SetFlag | — | — |
| GiveRepAfterRajaxxDeath | method | Creature.Main/DespawnOrUnsummon, Creature.Main/GetLootRecipient, Creature.Main/SetRespawnTime, GridSearchers/GetCreatureListWithEntryInGrid#2, Group/GetFirstMember, GroupReference/next, Log.Main/Out, Map.Main/GetCreature, Object/IsInWorld, ObjectGuid/ObjectGuid#5, ObjectMgr/GetFactionEntry, Player.Main/GetGroup, Player.Main/GetReputationMgr, ReputationMgr/GetRank, ReputationMgr/ModifyReputation, shared_Util/irand, Unit.Main/IsAlive, World/GetWowPatch | — | — |
| SpawnNewCrystals | method | Log.Main/Out, Map.Main/SummonGameObject, Object/GetObjectGuid | boss_ossirian/Aggro, boss_ossirian/OnUse | — |
| GetInstanceData_instance_ruins_of_ahnqiraj | function | — | — | — |
| AddSC_instance_ruins_of_ahnqiraj | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
