# instance_shadowfang_keep

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# instance_shadowfang_keep

**Purpose & Responsibilities**
`instance_shadowfang_keep` is the `ScriptedInstance` handler for the Shadowfang Keep dungeon. It manages the state of six distinct encounter types (NPC freeing, Baron Silverlaine, Fenrus the Devourer, Wolf Master Nandos, Archmage Arugal’s intro, and Voidwalker kills), controls the opening of specific doors based on progress, and handles dynamic creature visibility and faction changes for escort patrols. Additionally, this unit defines the logic for the "Haunting Spirits" spell aura, which periodically summons spirits around affected targets.

The unit operates primarily through the standard `ScriptedInstance` interface (`SetData`, `GetData`, `Load`, `Save`) and overrides lifecycle hooks (`OnCreatureCreate`, `OnCreatureDeath`, `OnObjectCreate`, `Update`) to react to world events. It maintains internal GUIDs for key NPCs and game objects to allow direct manipulation of their states (visibility, stand state, door activation) without relying on repeated database lookups.

## Member-by-Member Behavior

### Instance Lifecycle and Initialization
*   **`instance_shadowfang_keep`**: The constructor initializes the `ScriptedInstance` base class and immediately calls `Initialize()` to reset all internal state variables, GUIDs, and timers to their default values.
*   **`Initialize`**: Resets the encounter array `m_auiEncounter` to zero, clears all stored GUIDs for NPCs and doors, sets patrol spawn timers to 6000ms, and resets boolean flags for patrol visibility. It also resets the voidwalker kill counter.

### Creature and Object Management
*   **`OnCreatureCreate`**: Triggered when a creature spawns in the instance. It captures GUIDs for critical NPCs (Ash, Ada, Fenrus, Vincent, Baron Silverlaine, Nandos, Commander Springvale). It applies conditional logic based on current instance state:
    *   If the Intro event (`TYPE_INTRO`) is complete, Archmage Arugal is set to invisible (`VISIBILITY_OFF`).
    *   If the Intro event is complete, Vincent is set to a dead stand state (`UNIT_STAND_STATE_DEAD`).
    *   Wolf Guards (`NPC_WOLF_GUARD`) are initially hidden (`VISIBILITY_OFF`) and assigned a neutral/friendly faction template ID (35) to prevent them from attacking players prematurely.
*   **`OnCreatureDeath`**: Tracks the death of Baron Silverlaine and Commander Springvale by setting boolean flags (`showSilverlainePatrol`, `showSpringvalePatrol`) to true. These flags trigger the patrol spawning logic in the `Update` loop.
*   **`OnCreatureEnterCombat`**: Plays a specific sound effect (`SOUND_FENRUS_AGGRO`) when Fenrus the Devourer enters combat.
*   **`OnObjectCreate`**: Captures GUIDs for three key doors: Courtyard Door, Sorcerer Door, and Arugal Door. It checks the current encounter state; if the corresponding event is marked as `DONE`, it immediately activates the door (`GO_STATE_ACTIVE`). This ensures doors remain open after a server restart if the event was previously completed.

### Dynamic State Updates
*   **`Update`**: Handles time-based logic for escort patrols.
    *   If `showSilverlainePatrol` is true, it waits for a timer (`m_uiSpawnPatrolOnBaronDeath`) to expire. Once expired, it searches for Wolf Guards within 400 units of Baron Silverlaine. It identifies the correct guard by checking for a specific respawn delay (`7201`) and entry ID. It then makes the guard visible (`VISIBILITY_ON`) and changes its faction to hostile/neutral (ID 17), allowing it to patrol. The flag is reset to prevent re-triggering.
    *   Similar logic applies to Commander Springvale’s patrol, using a different respawn delay (`7202`) and timer (`m_uiSpawnPatrolOnCmdDeath`).

### Encounter Data Management
*   **`SetData`**: Receives updates from boss scripts or quest handlers.
    *   `TYPE_FREE_NPC`: Opens the Courtyard Door if data is `DONE`.
    *   `TYPE_FENRUS`: Records the encounter state.
    *   `TYPE_NANDOS`: Opens the Arugal Door if data is `DONE`.
    *   `TYPE_INTRO`: Records the intro completion state.
    *   `TYPE_VOIDWALKER`: Increments a kill counter. If the count exceeds 3, it opens the Sorcerer Door.
    *   After updating any encounter to `DONE`, it serializes the entire `m_auiEncounter` array into a string (`strInstData`) and calls `SaveToDB()` to persist the instance state.
*   **`GetData`**: Returns the current state of specific encounters (Free NPC, Rethilgore, Fenrus, Nandos, Intro) to querying scripts.
*   **`Save`**: Returns the serialized string representation of the instance data for database storage.
*   **`Load`**: Parses the saved string data back into the `m_auiEncounter` array. It converts any `IN_PROGRESS` states to `NOT_STARTED` to ensure clean instance resets upon reload.

### Spell Aura Logic
*   **`OnBeforeApply`**: Part of the `HauntingSpiritsScript`. Sets the periodic tick timer for the aura to 5 seconds.
*   **`OnPeriodicDummy`**: Executes every 5 seconds. There is a 5% chance (`roll_chance_i(5)`) to cast spell 7067 ("Summon Haunting Spirit") on the aura target.
*   **`GetScript_HauntingSpirits`**: Factory function returning the `HauntingSpiritsScript` instance.

### Registration
*   **`GetInstanceData_instance_shadowfang_keep`**: Factory function creating the instance script object.
*   **`AddSC_instance_shadowfang_keep`**: Registers both the instance script and the haunting spirits aura script with the core script manager.

## Cross-Unit Boundaries

*   **Calls `ScriptedInstance`**: The constructor delegates initialization to the base class. `SetData` calls `DoUseDoorOrButton` (inherited) to activate doors and `SaveToDB` to persist data.
*   **Calls `Object` methods**: `OnCreatureCreate`, `OnCreatureDeath`, `OnCreatureEnterCombat`, `OnObjectCreate`, and `Update` use `GetEntry` and `GetGUID` to identify entities and retrieve their unique identifiers.
*   **Calls `Unit.Main` methods**: `OnCreatureCreate` and `Update` manipulate creature states via `SetFactionTemplateId`, `SetStandState`, and `SetVisibility`.
*   **Calls `GameObject` methods**: `OnObjectCreate` uses `SetGoState` to open doors.
*   **Calls `WorldObject.Object` methods**: `OnCreatureEnterCombat` uses `PlayDirectSound` for audio feedback.
*   **Calls `Creature.Main` methods**: `Update` uses `GetRespawnDelay` to identify specific wolf guards.
*   **Calls `GridSearchers`**: `Update` uses `GetCreatureListWithEntryInGrid` to find nearby wolf guards for the patrol mechanic.
*   **Calls `Map.Main` methods**: `SetData` and `Load` use `GetId`, `GetInstanceId`, and `GetMapName` for logging purposes. `Update` uses `GetCreature` to retrieve creature pointers from GUIDs.
*   **Calls `Log.Main` methods**: `SetData` and `Load` use `Out` macros (`OUT_SAVE_INST_DATA`, etc.) for debug logging.
*   **Calls `Aura` methods**: `OnBeforeApply` uses `GetEffIndex` and `SetPeriodicTimer`. `OnPeriodicDummy` uses `GetTarget`.
*   **Calls `shared_Util`**: `OnPeriodicDummy` uses `roll_chance_i` for probability checks.
*   **Calls `SpellCaster`**: `OnPeriodicDummy` uses `CastSpell` to summon spirits.
*   **Calls `Script`/`ScriptMgr`**: `AddSC_instance_shadowfang_keep` registers the scripts with the global script manager.

## Data Model

This unit does not directly query or modify database tables via SQL. It relies on the `ScriptedInstance` framework to handle persistence. The `Save` method returns a formatted string containing the six encounter states, which is stored in the `instance` table (specifically the `data` column) by the core engine when `SaveToDB()` is called. The `Load` method reads this same string from the database upon instance creation. No other tables are touched.

## Notable Implementation Details

*   **Patrol Identification via Respawn Delay**: The `Update` method identifies specific wolf guards for the Baron and Springvale patrols not just by entry ID, but by checking their `GetRespawnDelay()` against hardcoded values (`7201` and `7202`). This is a fragile coupling to database configuration; if these respawn times change in the DB, the patrol logic will fail to identify the correct guards.
*   **Voidwalker Counter Persistence**: The voidwalker kill count (`m_auiEncounter[5]`) is persisted in the instance data string. However, the comment in `OnObjectCreate` notes that voidwalkers themselves are not persisted. If the server restarts, the voidwalkers despawn, but the counter remains. If the counter is already > 3, the Sorcerer Door opens immediately. If it is < 3, players must kill more voidwalkers, but since the original voidwalkers are gone, this might rely on respawns or new spawns, potentially breaking the intended flow if not handled elsewhere.
*   **Intro Event Visibility**: The visibility of Arugal and the stand state of Vincent are determined solely by whether `TYPE_INTRO` is `DONE`. This means if the intro is skipped or fails, these NPCs remain in their initial states (visible/alive), which might block progression or cause visual inconsistencies.
*   **Hardcoded Sound ID**: The Fenrus aggro sound is hardcoded as `6017`. This assumes the sound entry exists and is correctly configured in the `sound_entries` table.

## Member Reference

*   **instance_shadowfang_keep**: Constructor that initializes the `ScriptedInstance` base class and calls `Initialize`.
*   **Initialize**: Resets all internal state variables, GUIDs, timers, and encounter data to defaults.
*   **OnCreatureCreate**: Captures GUIDs for key NPCs and applies conditional visibility/stand states based on instance progress (e.g., hiding Arugal if intro is done).
*   **OnCreatureDeath**: Sets flags to trigger patrol spawning logic when Baron Silverlaine or Commander Springvale die.
*   **OnCreatureEnterCombat**: Plays a howl sound when Fenrus enters combat.
*   **OnObjectCreate**: Captures GUIDs for doors and opens them immediately if the corresponding encounter is already marked as done.
*   **Update**: Manages timers for spawning wolf guard escorts for Baron Silverlaine and Commander Springvale, identifying guards by entry ID and respawn delay.
*   **SetData**: Updates encounter states, triggers door openings, increments voidwalker counters, and persists data to the database if an encounter is completed.
*   **GetData**: Returns the current state of specific encounters to other scripts.
*   **Save**: Returns the serialized string of encounter data for database storage.
*   **Load**: Parses saved encounter data from the database and resets in-progress states to not-started.
*   **GetInstanceData_instance_shadowfang_keep**: Factory function to create the instance script object.
*   **OnBeforeApply**: Sets the periodic timer for the Haunting Spirits aura to 5 seconds.
*   **OnPeriodicDummy**: Checks a 5% chance to summon a Haunting Spirit on the aura target every tick.
*   **GetScript_HauntingSpirits**: Factory function returning the Haunting Spirits aura script.
*   **AddSC_instance_shadowfang_keep**: Registers the instance script and the Haunting Spirits aura script with the script manager.

---

<!-- machine-true, projected from graph.json -->

## Map — instance_shadowfang_keep

*Source:* instance_shadowfang_keep.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| instance_shadowfang_keep | ctor | ScriptedInstance/ScriptedInstance | — | — |
| Initialize | method | — | — | — |
| OnCreatureCreate | method | Object/GetEntry, Object/GetGUID, Unit.Main/SetFactionTemplateId, Unit.Main/SetStandState, Unit.Main/SetVisibility | — | — |
| OnCreatureDeath | method | Object/GetEntry | — | — |
| OnCreatureEnterCombat | method | Object/GetEntry, WorldObject.Object/PlayDirectSound | — | — |
| OnObjectCreate | method | GameObject/SetGoState, Object/GetEntry, Object/GetGUID | — | — |
| Update | method | Creature.Main/GetRespawnDelay, GridSearchers/GetCreatureListWithEntryInGrid#2, Map.Main/GetCreature, Object/GetEntry, ObjectGuid/ObjectGuid#5, Unit.Main/SetFactionTemplateId, Unit.Main/SetVisibility | — | — |
| SetData | method | InstanceData/SaveToDB, Log.Main/Out, Map.Main/GetId, Map.Main/GetInstanceId, Map.Main/GetMapName, ScriptedInstance/DoUseDoorOrButton | — | — |
| GetData | method | — | — | — |
| Save | method | — | — | — |
| Load | method | Log.Main/Out, Map.Main/GetId, Map.Main/GetInstanceId, Map.Main/GetMapName | — | — |
| GetInstanceData_instance_shadowfang_keep | function | — | — | — |
| OnBeforeApply | method | Aura/GetEffIndex, Aura/SetPeriodicTimer | — | — |
| OnPeriodicDummy | method | Aura/GetTarget, shared_Util/roll_chance_i, SpellCaster/CastSpell#2 | — | — |
| GetScript_HauntingSpirits | function | — | — | — |
| AddSC_instance_shadowfang_keep | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
