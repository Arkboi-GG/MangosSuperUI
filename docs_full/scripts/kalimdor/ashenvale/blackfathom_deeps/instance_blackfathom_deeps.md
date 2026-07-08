# instance_blackfathom_deeps

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`instance_blackfathom_deeps` implements the instance script logic for the **Blackfathom Deeps** dungeon in the WoW server emulator. It manages the state of three specific encounters: **Twilight Lord Kelris**, the **Shrine Event** (involving lighting four shrines and defeating waves of summoned mobs), and **Baron Aquanis**.

The unit performs two primary roles:
1.  **Instance Data Management (`instance_blackfathom_deeps` class):** Tracks the GUIDs of key NPCs and GameObjects, monitors encounter progress, handles persistence (saving/loading state to the database), and drives the timed spawning logic for the Shrine Event waves.
2.  **GameObject AI (`go_fire_of_akumaiAI` class):** Handles the interaction with the four "Fire of Aku'mai" shrines, validating preconditions (Kelris must be dead) before initiating the Shrine Event.

This unit does **not** implement the combat AI for the bosses themselves (e.g., Kelris or Aquanis); it only tracks their death status via `OnCreatureDeath` or external `SetData` calls.

## Member-by-Member Behavior

### Instance State and Initialization

*   **`instance_blackfathom_deeps`**: The constructor initializes the `ScriptedInstance` base class and immediately calls `Initialize()` to reset all internal state variables (GUIDs, timers, encounter flags) to their default "not started" values.
*   **`Initialize`**: Resets the `m_auiEncounter` array to zero, clears all stored GUIDs for bosses and objects, resets the shrine lit counter (`m_uiShrinesLit`) to 0, clears the list of wave mob GUIDs, and resets all spawn timers. This ensures a clean slate when the instance is first created or reloaded.
*   **`Load`**: Restores the instance state from a string saved in the database. It parses three integers representing the status of Kelris, the Shrine Event, and Aquanis. Crucially, it converts any `IN_PROGRESS` states back to `NOT_STARTED` to prevent stuck instances upon reload. It logs the load attempt and result using `Log.Main/Out`.
*   **`Save`**: Returns the current instance state as a C-string. The actual formatting of this string happens in `SetData`, where the three encounter statuses are concatenated with spaces.
*   **`GetData`**: Returns the current status (`DONE`, `NOT_STARTED`, etc.) of a specific encounter type (Kelris, Shrine, or Aquanis) requested by other scripts or the client.
*   **`GetData64`**: Returns the 64-bit GUID of a specific object (Bosses, Shrines, Door) identified by a data type constant. This allows other scripts (like boss AI or quest handlers) to locate these critical entities.

### Object Tracking

*   **`OnCreatureCreate`**: Triggered when a creature spawns in the instance. It checks if the creature is **Twilight Lord Kelris** (Entry 4832) and stores its GUID. This enables the instance script to reference Kelris later for summoning mobs relative to his position.
*   **`OnObjectCreate`**: Triggered when a GameObject spawns. It maps specific GameObject entries to internal GUID variables:
    *   Four Shrine Fires (`GO_SHRINE_1` through `GO_SHRINE_4`).
    *   The Shrine of Gelihast (Entry 103015).
    *   The Altar of the Deeps (Entry 103016).
    *   The Main Portal Door (`GO_PORTAL_DOOR`).
    *   **Notable Logic:** If the Main Door spawns and both the Shrine Event and Kelris are already marked `DONE`, it immediately activates the door (`SetGoState(GO_STATE_ACTIVE)`), allowing players to exit.

### Encounter Progression and Events

*   **`SetData`**: The central hub for updating instance state. It handles three types:
    1.  **`TYPE_KELRIS`**: Updates Kelris's status. If he is `DONE` and the Shrine Event is also `DONE`, it opens the main door.
    2.  **`TYPE_SHRINE`**:
        *   If `IN_PROGRESS`: Increments the `m_uiShrinesLit` counter. Asserts that fewer than 4 shrines are lit. Sets a 3-second delay timer (`m_uiSpawnMobsTimer`) for the next wave of mobs and resets the event check timer.
        *   If `DONE`: Checks if Kelris is also `DONE`; if so, opens the main door.
    3.  **`TYPE_AQUANIS`**: Simply updates Aquanis's status.
    *   **Persistence:** If any encounter reaches `DONE`, it formats the three encounter statuses into a string, stores it in `strInstData`, and calls `InstanceData/SaveToDB` to persist the change.
*   **`OnCreatureDeath`**: Checks if the dying creature is **Baron Aquanis**. If so, it triggers `SetData(TYPE_AQUANIS, DONE)`, marking the encounter complete.
*   **`Update`**: The periodic tick function for the instance.
    *   It only runs logic if the Shrine Event is `IN_PROGRESS`.
    *   **Event Completion Check:** Every second (controlled by `m_uiCheckEventEnd`), it calls `IsWaveEventFinished()`. If true, it marks the Shrine Event as `DONE`.
    *   **Wave Spawning:** Iterates through the four spawn timers. If a timer expires, it calls `DoSpawnMobs` for that specific wave index and resets the timer.

### Shrine Event Mechanics

*   **`DoSpawnMobs`**: Spawns a specific wave of mobs for the Shrine Event.
    *   It retrieves Twilight Lord Kelris's respawn coordinates to use as the home position for summoned mobs.
    *   It iterates through the static `aWaveSummonInformation` array to find entries matching the requested `uiWaveIndex`.
    *   For each match, it spawns the specified NPC entry at predefined locations (`aSpawnLocations`).
    *   **Position Adjustment:** If multiple mobs are spawned at the same location index, it offsets their Y-coordinate slightly to prevent stacking.
    *   **Mob Setup:** Each summoned mob is set to walk, cast a visual spell (ID 7741), set its home position to Kelris's location, and put into combat with the zone. Its GUID is added to `m_lWaveMobsGUIDList` for tracking.
*   **`IsWaveEventFinished`**: Determines if the current wave phase is complete.
    *   Returns `false` immediately if fewer than 4 shrines have been lit (event not fully triggered).
    *   Iterates through `m_lWaveMobsGUIDList`. If any tracked mob is still alive, it returns `false`.
    *   Returns `true` only if all tracked mobs are dead.

### Script Registration and AI

*   **`GetInstanceData_instance_blackfathom_deeps`**: Factory function that creates and returns a new `instance_blackfathom_deeps` object for the given map.
*   **`AddSC_instance_blackfathom_deeps`**: Registers the instance script and the `go_fire_of_akumai` GameObject AI with the server's `ScriptMgr`. It is called by `ScriptLoader/AddScripts` during server startup.
*   **`go_fire_of_akumaiAI`**: The AI for the four shrine fires.
    *   **Constructor**: Initializes the `GameObjectAI` base class.
*   **`OnUse`**: Triggered when a player interacts with a shrine fire.
    *   Validates that the instance data exists.
    *   **Precondition Check:** Ensures Twilight Lord Kelris is `DONE`. If not, interaction fails.
    *   **Activation:** Sets the GameObject state to `ACTIVE` and adds the `NO_INTERACT` flag to prevent further clicks.
    *   **Progression:** Calls `SetData(TYPE_SHRINE, IN_PROGRESS)` on the instance, triggering the wave spawn logic.
*   **`GetAIgo_fire_of_akumai`**: Factory function that creates and returns a new `go_fire_of_akumaiAI` object for the given GameObject.

## Cross-Unit Boundaries

*   **`ScriptedInstance`**: The base class for `instance_blackfathom_deeps`. Provides the framework for instance data management, including `SaveToDB`, `DoUseDoorOrButton`, and the virtual methods overridden here (`Initialize`, `SetData`, `GetData`, etc.).
*   **`Object` / `WorldObject`**: Used to retrieve basic properties like Entry ID and GUID from creatures and game objects during creation (`OnCreatureCreate`, `OnObjectCreate`).
*   **`Map.Main`**: Used in `Load` and `SetData` to log instance-specific information (Map ID, Instance ID, Map Name) for debugging and persistence logging. Also used in `DoSpawnMobs` and `IsWaveEventFinished` to retrieve `Creature` pointers from GUIDs.
*   **`Creature.Main`**: In `DoSpawnMobs`, used to get Kelris's respawn coordinates (`GetRespawnCoord`) and to configure summoned mobs (`SetHomePosition`, `SetInCombatWithZone`, `SetWalk`).
*   **`GameObject`**: In `OnObjectCreate` and `go_fire_of_akumaiAI::OnUse`, used to set the state of doors and shrines (`SetGoState`) and modify flags (`SetFlag`).
*   **`InstanceData`**: In `go_fire_of_akumaiAI::OnUse`, used to query the instance state (`GetData`) and update it (`SetData`).
*   **`WorldObject.Object`**: In `go_fire_of_akumaiAI::OnUse`, used to retrieve the instance data pointer (`GetInstanceData`) from the GameObject.
*   **`SpellCaster`**: In `DoSpawnMobs`, used to cast the visual summoning spell on newly spawned mobs.
*   **`Unit.Main`**: In `IsWaveEventFinished`, used to check if a mob is alive (`IsAlive`).
*   **`ObjectGuid`**: Used internally to handle GUID comparisons and storage.
*   **`Errors`**: `PrintStacktraceAndThrow` is listed in the map for `SetData`, likely due to the `ASSERT` macro used when checking `m_uiShrinesLit < 4`.
*   **`Log.Main`**: Used in `Load` and `SetData` to output debug messages regarding instance data loading and saving.
*   **`Script` / `ScriptMgr`**: In `AddSC_instance_blackfathom_deeps`, used to register the scripts with the engine.
*   **`ScriptLoader`**: Calls `AddSC_instance_blackfathom_deeps` during initialization.

## Data Model

This unit does **not** directly query or modify database tables via SQL statements. Instead, it relies on the `ScriptedInstance` base class to handle persistence. The `Save` method returns a formatted string containing the status of three encounters, which is stored in the instance data table managed by the core engine. No specific table schemas are referenced or manipulated directly in this source file.

## Notable Implementation Details

*   **Shrine Event Wave Logic:** The Shrine Event is complex. Lighting a shrine triggers `SetData(TYPE_SHRINE, IN_PROGRESS)`. This increments a counter and sets a 3-second timer for the next wave. The `Update` loop checks these timers and spawns mobs via `DoSpawnMobs`. The event only finishes when *all* 4 shrines are lit AND all spawned mobs are dead.
*   **Mob Persistence on Wipe:** The comments note that "On wipe the mobs don't despawn; they stay there until player returns." This is achieved by using `TEMPSUMMON_DEAD_DESPAWN` for summons, meaning they only despawn upon death, not when players leave the area. The instance tracks their GUIDs in `m_lWaveMobsGUIDList` to determine event completion.
*   **Position Offsetting:** In `DoSpawnMobs`, if multiple mobs are assigned to the same spawn location index, the code manually adjusts the Y-coordinate to spread them out, preventing visual clipping and potential aggro issues.
*   **Strict Precondition for Shrines:** Players cannot light the shrines unless Twilight Lord Kelris is dead. This is enforced in `go_fire_of_akumaiAI::OnUse`.
*   **Door Opening Logic:** The main portal door opens only when *both* Kelris and the Shrine Event are complete. This is checked in `SetData` for both encounter types and also in `OnObjectCreate` if the door spawns after the events are already done.
*   **State Reset on Load:** The `Load` method explicitly converts any `IN_PROGRESS` states to `NOT_STARTED`. This is a safety measure to ensure that if the server crashes during the Shrine Event, the instance resets cleanly rather than leaving players stuck in an incomplete event state.
*   **Static Spawn Data:** The spawn patterns and locations for the Shrine Event mobs are defined in static arrays (`aSpawnLocations`, `aWaveSummonInformation`) at the top of the file, making them easy to adjust but hard-coded.

## Member Reference

*   **instance_blackfathom_deeps**: Constructor that initializes the `ScriptedInstance` base class and calls `Initialize()` to reset all internal state variables.
*   **Initialize**: Resets encounter statuses, GUIDs, timers, and counters to their default "not started" values.
*   **OnCreatureCreate**: Captures the GUID of Twilight Lord Kelris (Entry 4832) when he spawns.
*   **OnObjectCreate**: Captures GUIDs for shrines, the altar, and the main door; activates the main door if both Kelris and the Shrine Event are already complete.
*   **SetData**: Updates encounter states, triggers door opening if conditions are met, schedules mob spawns for the Shrine Event, and persists completed encounters to the database.
*   **Save**: Returns the serialized instance state string for database persistence.
*   **GetData**: Returns the current status of a specific encounter (Kelris, Shrine, or Aquanis).
*   **GetData64**: Returns the GUID of a specific tracked object (bosses, shrines, or door).
*   **Load**: Parses the saved instance state string, restores encounter statuses, and resets any `IN_PROGRESS` states to `NOT_STARTED` to prevent stuck instances.
*   **OnCreatureDeath**: Marks Baron Aquanis as `DONE` when he dies.
*   **DoSpawnMobs**: Spawns a specific wave of mobs for the Shrine Event at predefined locations, adjusting positions to avoid stacking, and tracks their GUIDs.
*   **IsWaveEventFinished**: Checks if all shrines are lit and all summoned wave mobs are dead.
*   **Update**: Periodically checks for Shrine Event completion and triggers mob spawns based on timers.
*   **GetInstanceData_instance_blackfathom_deeps**: Factory function that creates and returns a new `instance_blackfathom_deeps` object.
*   **go_fire_of_akumaiAI**: Constructor for the AI of the Fire of Aku'mai GameObjects.
*   **OnUse**: Handles player interaction with a shrine fire, verifying Kelris is dead, activating the fire, and triggering the Shrine Event progression.
*   **GetAIgo_fire_of_akumai**: Factory function that creates and returns a new `go_fire_of_akumaiAI` object.
*   **AddSC_instance_blackfathom_deeps**: Registers the instance script and the `go_fire_of_akumai` GameObject AI with the server's script manager.

---

<!-- machine-true, projected from graph.json -->

## Map — instance_blackfathom_deeps

*Source:* instance_blackfathom_deeps.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| instance_blackfathom_deeps | ctor | ScriptedInstance/ScriptedInstance | — | — |
| Initialize | method | — | — | — |
| OnCreatureCreate | method | Object/GetEntry, Object/GetGUID | — | — |
| OnObjectCreate | method | GameObject/SetGoState, Object/GetEntry, Object/GetGUID | — | — |
| SetData | method | Errors/PrintStacktraceAndThrow, InstanceData/SaveToDB, Log.Main/Out, Map.Main/GetId, Map.Main/GetInstanceId, Map.Main/GetMapName, ScriptedInstance/DoUseDoorOrButton | — | — |
| Save | method | — | — | — |
| GetData | method | — | — | — |
| GetData64 | method | — | — | — |
| Load | method | Log.Main/Out, Map.Main/GetId, Map.Main/GetInstanceId, Map.Main/GetMapName | — | — |
| OnCreatureDeath | method | Object/GetEntry | — | — |
| DoSpawnMobs | method | Creature.Main/GetRespawnCoord, Creature.Main/SetHomePosition, Creature.Main/SetInCombatWithZone, Map.Main/GetCreature, Object/GetGUID, ObjectGuid/ObjectGuid#5, SpellCaster/CastSpell#2, Unit.Main/SetWalk, WorldObject.Object/SummonCreature#2 | — | — |
| IsWaveEventFinished | method | Map.Main/GetCreature, ObjectGuid/ObjectGuid#5, Unit.Main/IsAlive | — | — |
| Update | method | — | — | — |
| GetInstanceData_instance_blackfathom_deeps | function | — | — | — |
| go_fire_of_akumaiAI | ctor | GameObjectAI/GameObjectAI | — | — |
| OnUse | method | GameObject/SetGoState, InstanceData/GetData, InstanceData/SetData, WorldObject.Object/GetInstanceData, WorldObject.Object/SetFlag | — | — |
| GetAIgo_fire_of_akumai | function | — | — | — |
| AddSC_instance_blackfathom_deeps | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
