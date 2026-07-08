# instance_wailing_caverns

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# instance_wailing_caverns

**Purpose & Responsibilities**

`instance_wailing_caverns` is the scripted instance data manager for the **Wailing Caverns** dungeon in World of Warcraft. It implements the `ScriptedInstance` interface to track boss encounter states, manage specific creature GUIDs for key NPCs, handle dynamic spawning/despawning logic tied to boss deaths, and persist instance progress to the database.

Additionally, this unit defines an area trigger script (`AreaTrigger_at_dmf_chest_wc`) that controls the visibility of a specific game object (Darkmoon Chest) based on player quest completion status.

The unit handles six primary encounter types: Anacondra, Cobranh, Pythas, Mutanus, Serpentis, and The Disciple of Naralex. It also manages a global list of "nightmare monsters" that are despawned upon the defeat of Mutanus.

## Member-by-Member Behavior

### Initialization and State Management

*   **`instance_wailing_caverns` (ctor)**: Constructs the instance data object, passing the `Map` pointer to the base `ScriptedInstance`. It immediately calls `Initialize()` to reset all internal state variables.
*   **`Initialize`**: Resets the encounter array `m_auiEncounter` to zero (indicating `NOT_STARTED`), clears all stored creature GUIDs (`m_uiDiscipleGUID`, etc.), sets the `Assaulted` flag to `false`, and clears the `vNightmareMonsters` vector. This ensures a clean slate for a new instance load.
*   **`Load`**: Restores instance state from a string saved in the database. It parses space-separated integers into the `m_auiEncounter` array. Crucially, it iterates through the loaded encounters and resets any state marked as `IN_PROGRESS` back to `NOT_STARTED`. This prevents stuck instances where a boss was killed mid-fight during a server crash. It logs the loading process using macros like `OUT_LOAD_INST_DATA`.
*   **`Save`**: Returns the current instance state as a C-string. It relies on `strInstData`, which is populated in `SetData` whenever a boss is defeated (`DONE`). The format is six space-separated integers representing the state of each encounter type.

### Creature and Object Tracking

*   **`OnCreatureCreate`**: Triggered when a creature spawns in the instance. It performs two main tasks:
    1.  **GUID Registration**: It checks the creature's entry ID. If it matches one of the key bosses (Anacondra, Naralex, Serpentis, Disciple), it stores the creature's GUID in the corresponding member variable (`m_uiAnacondraGUID`, etc.). Note that Verdan the Everliving is commented out in this switch block.
    2.  **Nightmare Monster List**: It adds the creature's GUID to `vNightmareMonsters` unless the creature is a critter, has faction template ID 35 (specific druids), or is Kresh (entry 3653). This list is used later to despawn these mobs when Mutanus dies.
*   **`OnObjectCreate`**: Triggered when a game object spawns. It checks if the object is the Darkmoon Chest (`GO_DMF_CHEST`). If so, it sets the object to invisible (`SetVisible(false)`). This ensures the chest is hidden by default until a player with the correct quest status triggers its visibility via the area trigger.

### Encounter Logic and Progression

*   **`SetData`**: The core logic hub for instance events. It accepts a type (boss ID) and data (state, usually `DONE`, `SPECIAL`, or `IN_PROGRESS`).
    *   **Anacondra, Cobranh, Pythas**: When any of these three bosses reach `DONE` state, the script checks if the `Assaulted` flag is `false`. If so, it triggers a yell from Serpentis (`SERPENTIS_YELL`) and sets `Assaulted` to `true`. This ensures Serpentis reacts only once to the first of these three bosses being defeated.
    *   **Anacondra (Special Case)**: If `uiData` is `SPECIAL`, it finds the closest Druid (entry 3840) to Anacondra within interaction distance. If found and alive, it forces the Druid to disappear and die. This likely represents a specific mechanic or bug fix related to Anacondra's fight.
    *   **Mutanus**: When Mutanus reaches `DONE`, it iterates through `vNightmareMonsters`. For each creature in the list, if it is still alive or has empty loot, it forces a despawn. This cleans up the "nightmare" adds associated with Mutanus's phase.
    *   **Serpentis/Disciple**: Simply records the state.
    *   **Disciple Spawn Condition**: After processing the specific type, it checks if Anacondra, Cobranh, Pythas, and Mutanus are all `DONE` and Serpentis is `NOT_STARTED`. If this condition is met, it sets the Disciple's data to `SPECIAL`, assigns a gossip menu (`GOSSIP_DISCIPLE_SPECIAL`) to the Disciple creature, and triggers a yell (`YELL_AFTER_GOSSIP`). This gates the final boss encounter behind the completion of the previous four.
    *   **Persistence**: If `uiData` is `DONE`, it serializes the current `m_auiEncounter` array into `strInstData` and calls `SaveToDB()` to persist the progress.

*   **`GetData`**: Returns the current state (`uint32`) of a specific encounter type. Used by other scripts (e.g., boss AI) to check if a boss is already dead or in progress.
*   **`GetData64`**: Returns the GUID of Naralex (`DATA_NARALEX`). This allows other scripts to interact with Naralex directly.

### External Scripts and Registration

*   **`GetInstanceData_instance_wailing_caverns`**: Factory function that creates and returns a new `instance_wailing_caverns` object. Registered with the script system.
*   **`AreaTrigger_at_dmf_chest_wc`**: A standalone area trigger function. When a player enters the trigger zone, it checks if the player has completed `QUEST_FORTUNE_AWAITS`. If yes, it finds the nearest Darkmoon Chest (`GO_DMF_CHEST`) within 100 units and makes it visible. This enables quest-specific loot availability.
*   **`AddSC_instance_wailing_caverns`**: Registers both the instance script (`instance_wailing_caverns`) and the area trigger script (`at_dmf_chest_wc`) with the `ScriptMgr`.

## Cross-Unit Boundaries

*   **Calls Out**:
    *   **`ScriptedInstance`**: Inherits from this base class, utilizing its framework for instance data management, saving/loading, and map context.
    *   **`Object` / `Unit` / `Creature` / `GameObject`**: Uses methods like `GetEntry`, `GetGUID`, `SetVisible`, `IsAlive`, `DisappearAndDie`, `ForcedDespawn`, `SetDefaultGossipMenuId`, and `GetCreatureType` to manipulate entities in the world.
    *   **`GridSearchers`**: Uses `GetClosestCreatureWithEntry` to find nearby creatures (e.g., the Druid near Anacondra).
    *   **`InstanceData`**: Calls `SaveToDB` to persist data.
    *   **`Log.Main`**: Uses `sLog.Out` for debugging and error reporting.
    *   **`ScriptMgr`**: Uses `DoScriptText` to play sound/text emotes and `RegisterSelf` to register scripts.
    *   **`Loot`**: Checks `loot.empty()` to determine if a corpse has been looted before despawning.
    *   **`Map.Main`**: Uses `GetCreature` to retrieve creature pointers from GUIDs, and `GetId`/`GetInstanceId`/`GetMapName` for logging.
    *   **`Player.Main`**: In the area trigger, uses `GetQuestStatus` to check player progress.
    *   **`WorldObject.Object`**: Uses `FindNearestGameObject` in the area trigger.

*   **Called By**:
    *   **`ScriptLoader`**: Calls `AddSC_instance_wailing_caverns` during server startup to register the scripts.

## Data Model

This unit does not directly query or modify database tables via SQL statements in its source code. Instead, it relies on the `ScriptedInstance` base class to handle persistence. The `Save()` method returns a string representation of the encounter states, which is stored in the `instance` table (typically in the `data` column) by the engine. The `Load()` method reads this string back. No custom tables are created or accessed by this unit.

## Notable Implementation Details

1.  **Assaulted Flag**: The `Assaulted` boolean ensures that Serpentis's reaction to the first boss death (Anacondra, Cobranh, or Pythas) happens only once. Without this, Serpentis would yell multiple times if multiple bosses were killed in quick succession or if the state was updated redundantly.
2.  **Nightmare Monster Cleanup**: The `vNightmareMonsters` vector is populated dynamically in `OnCreatureCreate`. This approach avoids hardcoding entries for every possible mob in the dungeon, making it robust against changes in spawn lists. However, it relies on `OnCreatureCreate` being called for all relevant mobs before Mutanus dies. If a mob spawns after Mutanus is dead, it won't be added to the list and thus won't be despawned by this logic (though it might not need to be).
3.  **Stuck Instance Prevention**: In `Load()`, any encounter state loaded as `IN_PROGRESS` is reset to `NOT_STARTED`. This is a critical safety measure to prevent players from being unable to respawn a boss because the server crashed while the boss was technically "in combat" but no longer existed.
4.  **Disciple Gating**: The Disciple of Naralex does not become interactive (gossip menu) until Anacondra, Cobranh, Pythas, and Mutanus are all defeated. This enforces a linear progression through the dungeon.
5.  **Darkmoon Chest Visibility**: The chest is hidden by default (`OnObjectCreate`) and only shown to players who have completed `QUEST_FORTUNE_AWAITS` via the area trigger. This prevents players without the quest from seeing or interacting with the chest, maintaining quest integrity.
6.  **Hardcoded Entry IDs**: The script uses hardcoded entry IDs for bosses (e.g., 3678 for Disciple, 3679 for Naralex). While common in older scripts, this reduces flexibility if entry IDs change in future database updates.
7.  **Commented Out Code**: Verdan the Everliving (entry 5775) is commented out in `OnCreatureCreate`. This suggests incomplete implementation or a disabled feature. If Verdan is ever re-enabled, the corresponding GUID storage and logic must be restored.
8.  **Error Logging**: `SetData` logs an error if an unknown `uiType` is passed. This helps developers catch bugs where scripts call `SetData` with incorrect constants.

## Member Reference

*   **`instance_wailing_caverns`**: Constructor that initializes the instance data object and calls `Initialize()`.
*   **`Initialize`**: Resets all internal state variables, including encounter states, GUIDs, and flags, to their default values.
*   **`OnCreatureCreate`**: Registers GUIDs for key bosses and adds non-critter, non-special faction creatures to the `vNightmareMonsters` list for later cleanup.
*   **`OnObjectCreate`**: Hides the Darkmoon Chest (`GO_DMF_CHEST`) by default upon spawn.
*   **`SetData`**: Handles boss death events, triggers Serpentis yells, despawns nightmare monsters on Mutanus death, gates the Disciple encounter, and saves instance progress to the database.
*   **`Save`**: Returns the serialized encounter state string for database persistence.
*   **`GetData`**: Returns the current state of a specified encounter type.
*   **`GetData64`**: Returns the GUID of Naralex.
*   **`Load`**: Parses the saved instance data string, restores encounter states, and resets any `IN_PROGRESS` states to `NOT_STARTED` to prevent stuck instances.
*   **`GetInstanceData_instance_wailing_caverns`**: Factory function to create a new `instance_wailing_caverns` instance.
*   **`AreaTrigger_at_dmf_chest_wc`**: Makes the Darkmoon Chest visible to players who have completed `QUEST_FORTUNE_AWAITS`.
*   **`AddSC_instance_wailing_caverns`**: Registers the instance script and the area trigger script with the script manager.

---

<!-- machine-true, projected from graph.json -->

## Map — instance_wailing_caverns

*Source:* instance_wailing_caverns.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| instance_wailing_caverns | ctor | ScriptedInstance/ScriptedInstance | — | — |
| Initialize | method | — | — | — |
| OnCreatureCreate | method | Object/GetEntry, Object/GetGUID, Unit.Main/GetCreatureType, Unit.Main/GetFactionTemplateId | — | — |
| OnObjectCreate | method | GameObject/SetVisible, Object/GetEntry | — | — |
| SetData | method | Creature.Main/DisappearAndDie, Creature.Main/ForcedDespawn, Creature.Main/SetDefaultGossipMenuId, GridSearchers/GetClosestCreatureWithEntry, InstanceData/SaveToDB, Log.Main/Out, Loot/empty, Map.Main/GetCreature, Map.Main/GetId, Map.Main/GetInstanceId, Map.Main/GetMapName, ObjectGuid/ObjectGuid#5, ScriptMgr/DoScriptText, Unit.Main/IsAlive | — | — |
| Save | method | — | — | — |
| GetData | method | — | — | — |
| GetData64 | method | — | — | — |
| Load | method | Log.Main/Out, Map.Main/GetId, Map.Main/GetInstanceId, Map.Main/GetMapName | — | — |
| GetInstanceData_instance_wailing_caverns | function | — | — | — |
| AreaTrigger_at_dmf_chest_wc | function | GameObject/SetVisible, Player.Main/GetQuestStatus, WorldObject.Object/FindNearestGameObject | — | — |
| AddSC_instance_wailing_caverns | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
