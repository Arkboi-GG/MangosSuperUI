# instance_sunken_temple

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# instance_sunken_temple

**Purpose & Responsibilities**

`instance_sunken_temple` is the `ScriptedInstance` implementation for the **Sunken Temple** dungeon in the WoWVMaNGOS emulator. It manages the persistent state, event sequencing, and object lifecycle for the dungeon's specific mechanics, distinct from individual creature AI scripts.

Its primary responsibilities include:
1.  **The Secret Circle Puzzle:** Tracking the sequential activation of six Atalai Statues to summon the boss **Atal'alarion**. It enforces a strict activation order, penalizing incorrect inputs by triggering traps.
2.  **Boss Progression Gates:** Managing the visibility and combat readiness of bosses (**Atal'alarion**, **Shade of Eranikus**, **Dreamscythe**, **Weaver**) based on the completion of preceding encounters (Secret Circle, Protectors, Jammal'an).
3.  **Minion Aggro Management:** Automatically pulling specific groups of ambient mobs (e.g., Mummified Atal'ai, Nightmare Scalebane) into combat with bosses like **Jammal'an** or **Shade of Eranikus** when those bosses enter combat, ensuring the encounter feels populated as intended.
4.  **State Persistence:** Saving and loading encounter progress (`DONE`, `IN_PROGRESS`, `NOT_STARTED`) to the database, allowing the dungeon state to survive server restarts.

This unit does not handle general map logic (handled by `Map` or `ScriptedInstance` base classes) or individual mob behavior (handled by separate `CreatureAI` scripts). It acts as the central coordinator for dungeon-wide events.

---

## Member-by-Member Behavior

### Initialization and Lifecycle

*   **`instance_sunken_temple` (Constructor)**: Initializes the instance data structure by calling `Initialize()`. It sets up the internal arrays for GUIDs and encounter states.
*   **`Initialize`**: Resets all internal state variables to their default "not started" values. It zeroes out encounter counters, GUID storage arrays, and timers. Crucially, it sets `m_restoreCircleState` to `true`, which triggers a consistency check in the first `Update` cycle to ensure the puzzle state matches the saved data after a server reload.
*   **`Load`**: Parses the saved instance data string from the database. It restores the `m_auiEncounter` array. A notable safety measure here is that any encounter marked as `IN_PROGRESS` is reset to `NOT_STARTED` upon load. This prevents stuck states where a boss might be invisible or unselectable indefinitely after a crash during a fight.
*   **`Save`**: Returns the current instance data as a C-string. The actual serialization happens in `SetData` when an encounter finishes, where the state is formatted into a space-separated string and stored in `strInstData`.
*   **`Update`**: Called periodically by the game loop. It performs two tasks:
    1.  **Cleanup Timer:** Decrements `RemoveTimer`. When it expires, it calls `Map::RemoveAllObjectsInRemoveList()` to clean up despawned objects, preventing memory leaks.
    2.  **State Restoration:** If `m_restoreCircleState` is true (first update after load), it checks if the Secret Circle is marked `DONE`. If so, it re-applies the visual effects (lights, idol state) and ensures Atal'alarion is properly visible/interactable if he has been killed. This fixes potential desyncs between the saved state and the actual objects in the world.

### The Secret Circle Puzzle (Atal'alarion Summoning)

*   **`ProcessStatueUsed`**: The core logic for the puzzle. It is called when a player interacts with one of the six statues.
    *   It checks if the puzzle is already `DONE`. If so, it ignores further interactions.
    *   It maps the statue entry ID to its index in the `m_atalaiStatueEntries` array.
    *   **Validation:** It compares the activated statue's index with the expected next step (`m_uiStatueCounter`).
    *   **Success:** If correct, it increments the counter, disables interaction on that statue (`GO_FLAG_NO_INTERACT`), and spawns a "green light" effect nearby. If all 6 are done, it marks the puzzle `DONE`.
    *   **Failure:** If the wrong statue is clicked, it triggers a trap. It searches for a random trap object near the statue and uses it on Atal'alarion (who is currently hidden/immune). This likely causes damage or aggro to the player, serving as a penalty.
*   **`HandleStatueEventDone`**: Called when the puzzle completes. It respawns the **Idol of Hakkar** (making it available for pickup), disables interaction on all statues permanently for this instance, and spawns the "big green lights" around the circle to signal success.
*   **`DoSpawnAtalarionIfCan`**: Makes the boss **Atal'alarion** visible and interactable. It removes flags like `UNIT_FLAG_NOT_SELECTABLE`, `UNIT_FLAG_SPAWNING`, and `UNIT_FLAG_IMMUNE_TO_NPC`, and plays a spawn sound. This is called after the puzzle is solved.

### Boss Encounter Coordination

*   **`OnCreatureCreate`**: Registers GUIDs for key NPCs and Game Objects as they spawn. It applies initial visibility and immunity flags based on the current dungeon state.
    *   **Atal'alarion:** Hidden and immune until the Secret Circle is solved.
    *   **Shade of Eranikus / Dreamscythe / Weaver:** Hidden/immune/sleeping until the **Jammal'an** encounter is complete. This enforces the linear progression of the dungeon.
    *   **Protectors:** Their GUIDs are stored in an array to track their deaths.
*   **`OnCreatureEnterCombat`**: Handles immediate reactions when key bosses engage.
    *   **Atal'alarion:** If he enters combat while still hidden (e.g., pulled prematurely), he immediately evades and resets his hidden/immune state. This prevents players from accidentally killing him before the puzzle is solved.
    *   **Dreamscythe:** Plays an aggro sound.
    *   **Shade of Eranikus:** Wakes up from sleep state.
*   **`OnCreatureDeath`**: Currently only handles **Atal'alarion**. Upon his death, it removes the `NO_INTERACT` flag from the **Idol of Hakkar**, allowing players to pick it up. This is a critical quest item mechanic.
*   **`SetData`**: The main interface for updating instance state from other scripts (e.g., boss AI scripts).
    *   **`TYPE_SECRET_CIRCLE`:** When set to `DONE`, it triggers `HandleStatueEventDone` and `DoSpawnAtalarionIfCan`.
    *   **`TYPE_PROTECTORS`:** When set to `DONE`, it verifies all 6 Protectors are dead. If so, it opens the **Jammal'an Barrier** door and triggers Jammal'an's intro speech.
    *   **`TYPE_JAMMALAN`:**
        *   When `DONE`: Unlocks **Shade of Eranikus**, **Dreamscythe**, and **Weaver**. It makes them visible, removes immunities, and starts their waypoint movements.
        *   When `IN_PROGRESS`: It actively pulls ambient mobs (Mummified Atal'ai, Deathwalkers, High Priests) within 150 yards of Jammal'an into combat. This simulates the boss summoning minions.
    *   **`TYPE_ERANIKUS`:** When `IN_PROGRESS`, it pulls ambient nightmare mobs (Scalebane, Wyrmkin, etc.) within 300 yards of Shade of Eranikus into combat.
    *   **`TYPE_ETERNAL_FLAME`:** Stores a flame counter, likely for the Avatar of Hakkar encounter.
    *   **Persistence:** If any encounter type is set to `DONE`, it serializes the state and saves it to the database via `SaveToDB`.

### Data Accessors

*   **`GetData`**: Returns the status of a specific encounter type or the eternal flame counter. Used by other scripts to check if a boss should be visible or if a door should be open.
*   **`GetData64`**: Returns the GUID of specific entities (Shade of Hakkar, Atal'alarion, Avatar of Hakkar). Used by other scripts to target these entities for spells or events.
*   **`SetData64`**: Stores GUIDs passed from other scripts. Notably, it also handles the `ProcessStatueUsed` logic when a statue GUID is passed, acting as a bridge between the GameObject script and the instance logic.

### Registration

*   **`GetInstance_instance_sunken_temple`**: Factory function to create the instance data object.
*   **`AddSC_instance_sunken_temple`**: Registers the script with the `ScriptMgr` so the engine knows to use this class for the Sunken Temple map.

---

## Cross-Unit Boundaries

*   **`ScriptedInstance` (Base Class):**
    *   **Calls:** `DoRespawnGameObject`, `DoUseDoorOrButton`, `SaveToDB`. These are high-level helpers for managing objects and persistence.
    *   **Called By:** The constructor inherits from it.
*   **`Map` (Main Map Logic):**
    *   **Calls:** `GetCreature`, `GetGameObject`, `RemoveAllObjectsInRemoveList`, `GetId`, `GetInstanceId`, `GetMapName`. Used to query the world state and manage cleanup.
    *   **Called By:** None explicitly listed in the map, but the instance is attached to the Map.
*   **`ScriptMgr` (Script Manager):**
    *   **Calls:** `DoScriptText`. Used to play sounds/dialogue for bosses (e.g., Atal'alarion spawn, Jammal'an intro).
    *   **Called By:** `AddSC_instance_sunken_temple` registers the script via `RegisterSelf`.
*   **`Unit` / `Creature` / `GameObject` (World Objects):**
    *   **Calls:** Extensive use of `SetVisibility`, `SetFlag`/`RemoveFlag` (for immunities/interactions), `SetStandState`, `GetMotionMaster` (for waypoints/idle), `Use` (for traps), `IsAlive`, `GetEntry`, `GetGUID`. These are the primary mechanisms for controlling the appearance and behavior of entities in the world.
    *   **Called By:** None directly, but these objects trigger callbacks like `OnCreatureCreate` and `OnCreatureEnterCombat`.
*   **`GridSearchers` (Spatial Queries):**
    *   **Calls:** `GetClosestGameObjectWithEntry`, `GetCreatureListWithEntryInGrid`. Used to find nearby traps for the puzzle penalty and to find ambient mobs to pull during boss fights.
*   **`Log` (Logging):**
    *   **Calls:** `Out` (via macros `OUT_SAVE_INST_DATA`, etc.). Used for debugging save/load operations.

---

## Data Model

This unit does not directly query or modify database tables via SQL statements. Instead, it relies on the `ScriptedInstance` base class to handle persistence. The `Save` method returns a string representation of the encounter states, which is stored in the `instance` table (specifically the `data` column) by the engine. The `Load` method reads this string back. No custom tables are created or accessed by this unit.

---

## Notable Implementation Details

1.  **Strict Puzzle Order:** The Secret Circle puzzle requires statues to be activated in a specific hardcoded order (`m_atalaiStatueEntries`). Clicking the wrong statue triggers a trap. The trap selection is random among three possible trap entries (`GO_ATALAI_TRAP_1`, `_2`, `_3`), and the trap is "used" on Atal'alarion. Since Atal'alarion is immune, this likely affects the player who triggered it, though the exact damage/mechanic is handled by the trap's own script.
2.  **Premature Pull Protection:** If Atal'alarion is pulled before the puzzle is solved, `OnCreatureEnterCombat` detects his hidden state and forces him to evade and reset. This prevents players from bypassing the puzzle by kiting or accidentally pulling him.
3.  **Ambient Mob Pulling:** The `SetData` method for `TYPE_JAMMALAN` and `TYPE_ERANIKUS` actively scans for nearby ambient mobs and forces them into combat with the boss. This is a performance-intensive operation (grid search) but ensures the encounter feels dynamic. It only targets mobs with a DB GUID (`GetDBTableGUIDLow`), ignoring temporary summons.
4.  **State Restoration on Reload:** The `Update` method includes a one-time check (`m_restoreCircleState`) to re-apply visual effects and entity states if the dungeon was saved in a `DONE` state. This is crucial because objects might respawn in their default state after a server restart, breaking the illusion of progress.
5.  **Idol of Hakkar Mechanic:** The Idol is initially non-interactable. It becomes interactable only after Atal'alarion dies. This links the puzzle solution (summoning the boss) to the quest reward (killing the boss to get the item).
6.  **Hardcoded Entries:** The unit relies heavily on hardcoded entry IDs for statues, traps, lights, and bosses. Any change in the database for these entries would require corresponding code changes.
7.  **No Custom Schema:** As noted, no custom tables are used. All state is serialized into a single string field in the standard instance table.

---

## Member Reference

*   **`instance_sunken_temple`**: Constructor that initializes the instance by calling `Initialize()`.
*   **`Initialize`**: Resets all internal state variables, GUID arrays, and timers to defaults. Sets `m_restoreCircleState` to true.
*   **`DoSpawnAtalarionIfCan`**: Makes Atal'alarion visible and interactable by removing immunity/spawning flags and playing a sound.
*   **`HandleStatueEventDone`**: Respawns the Idol of Hakkar, disables statue interactions, and spawns green lights upon puzzle completion.
*   **`ProcessStatueUsed`**: Validates statue activation order. Increments counter on success, triggers traps on failure, and marks puzzle done if all 6 are correct.
*   **`OnObjectCreate`**: Registers GUIDs for Game Objects (barriers, idols, statues, lights) and applies initial states (e.g., disabling idol interaction).
*   **`OnCreatureCreate`**: Registers GUIDs for creatures and applies initial visibility/immunity flags based on dungeon progress (e.g., hiding Atal'alarion until puzzle is solved).
*   **`OnCreatureEnterCombat`**: Handles boss-specific combat entry actions: Atal'alarion evades if pulled early, Dreamscythe plays aggro sound, Eranikus wakes up.
*   **`OnCreatureDeath`**: Removes `NO_INTERACT` flag from Idol of Hakkar when Atal'alarion dies.
*   **`SetData`**: Updates encounter state. Triggers specific events (door opening, minion pulling, boss unlocking) based on the type and data value. Saves state to DB if `DONE`.
*   **`SetData64`**: Stores GUIDs for specific entities. Also routes statue usage events to `ProcessStatueUsed`.
*   **`Save`**: Returns the serialized instance data string for database persistence.
*   **`GetData`**: Returns the status of a specific encounter type or the eternal flame counter.
*   **`GetData64`**: Returns the GUID of specific entities (Shade of Hakkar, Atal'alarion, Avatar of Hakkar).
*   **`Update`**: Manages cleanup timer and restores visual/state consistency after server reloads.
*   **`Load`**: Parses saved instance data from the database, resetting `IN_PROGRESS` states to `NOT_STARTED` for safety.
*   **`GetInstance_instance_sunken_temple`**: Factory function to create the instance data object.
*   **`AddSC_instance_sunken_temple`**: Registers the script with the ScriptMgr.

---

<!-- machine-true, projected from graph.json -->

## Map — instance_sunken_temple

*Source:* instance_sunken_temple.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| instance_sunken_temple | ctor | ScriptedInstance/ScriptedInstance | — | — |
| Initialize | method | — | — | — |
| DoSpawnAtalarionIfCan | method | Map.Main/GetCreature, ObjectGuid/ObjectGuid#5, ScriptMgr/DoScriptText, Unit.Main/SetVisibility, WorldObject.Object/RemoveFlag | — | — |
| HandleStatueEventDone | method | Map.Main/GetGameObject, ObjectGuid/ObjectGuid#5, ScriptedInstance/DoRespawnGameObject, WorldObject.Object/SetFlag | — | — |
| ProcessStatueUsed | method | GameObject/Use, GridSearchers/GetClosestGameObjectWithEntry, Map.Main/GetCreature, Map.Main/GetGameObject, Object/GetGUID, ObjectGuid/ObjectGuid#5, ScriptedInstance/DoRespawnGameObject, WorldObject.Object/SetFlag | — | — |
| OnObjectCreate | method | GameObject/SetGoState, Object/GetEntry, Object/GetGUID, WorldObject.Object/SetFlag | — | — |
| OnCreatureCreate | method | Creature.MotionMaster/MoveIdle, Object/GetEntry, Object/GetGUID, Unit.Main/GetMotionMaster, Unit.Main/SetStandState, Unit.Main/SetVisibility, WorldObject.Object/SetFlag | — | — |
| OnCreatureEnterCombat | method | Creature.Main/AI, CreatureAI/EnterEvadeMode, Object/GetEntry, ScriptMgr/DoScriptText, Unit.Main/GetVisibility, Unit.Main/SetStandState, WorldObject.Object/SetFlag | — | — |
| OnCreatureDeath | method | Map.Main/GetGameObject, Object/GetEntry, ObjectGuid/ObjectGuid#5, WorldObject.Object/RemoveFlag | — | — |
| SetData | method | Creature.Main/GetDBTableGUIDLow, Creature.Main/SetInCombatWithZone, Creature.MotionMaster/MoveWaypoint, GameObject/GetGoState, GridSearchers/GetCreatureListWithEntryInGrid#2, InstanceData/SaveToDB, Log.Main/Out, Map.Main/GetCreature, Map.Main/GetGameObject, Map.Main/GetId, Map.Main/GetInstanceId, Map.Main/GetMapName, ObjectGuid/ObjectGuid#5, ScriptedInstance/DoUseDoorOrButton, ScriptMgr/DoScriptText, Unit.Main/GetMotionMaster, Unit.Main/IsAlive, Unit.Main/SetVisibility, WorldObject.Object/RemoveFlag | — | — |
| SetData64 | method | — | — | — |
| Save | method | — | — | — |
| GetData | method | — | — | — |
| GetData64 | method | — | — | — |
| Update | method | Map.Main/GetCreature, Map.Main/GetGameObject, Map.Main/RemoveAllObjectsInRemoveList, ObjectGuid/ObjectGuid#5, Unit.Main/IsAlive, WorldObject.Object/RemoveFlag | — | — |
| Load | method | Log.Main/Out, Map.Main/GetId, Map.Main/GetInstanceId, Map.Main/GetMapName | — | — |
| GetInstance_instance_sunken_temple | function | — | — | — |
| AddSC_instance_sunken_temple | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
