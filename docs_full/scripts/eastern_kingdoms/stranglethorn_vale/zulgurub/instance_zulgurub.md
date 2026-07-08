# instance_zulgurub

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# instance_zulgurub

**Purpose & Responsibilities**

`instance_zulgurub` is the `ScriptedInstance` implementation for the Zul'Gurub raid instance. It manages the persistent state of the instance, tracking boss encounter progress, coordinating complex multi-boss mechanics (specifically the "High Priests" feeding power to Hakkar and the resurrection mechanics of the Thekal encounter), and handling the spawning logic for the weekly random boss.

Key responsibilities include:
1.  **Encounter Tracking:** Maintaining the state (`NOT_STARTED`, `IN_PROGRESS`, `DONE`) for 12 distinct encounter types, including the five High Priests (Arlokk, Jeklik, Venoxis, Marli, Thekal), Ohgan, Hakkar, Jindo, Gahzranka, and the Random Boss.
2.  **Hakkar Power Mechanic:** Dynamically adjusting the stack count of the `SPELL_HAKKAR_POWER` aura on the final boss, Hakkar, based on how many High Priest encounters remain incomplete. Each remaining priest adds a stack; completing a priest removes a stack.
3.  **Thekal Resurrection Logic:** Providing helper methods to determine which NPC (Lorkhan, Zath, or Thekal) can resurrect, which needs resurrection, and who is currently casting, enabling conditional quest criteria checks.
4.  **Random Boss Management:** Determining the weekly random boss (Grilek, Hazza'rah, Renataki, or Wushoolay) based on game time or active game events, and managing its spawn state.
5.  **Marli Trash Coordination:** Triggering specific trash mobs to attack the player when the Marli encounter begins.

**Member-by-Member Behavior**

### Initialization and Lifecycle

*   **`instance_zulgurub`**: The constructor initializes the base `ScriptedInstance` and sets `m_randomBossSpawned` to false. It immediately calls `Initialize()` to zero out the encounter array and GUIDs.
*   **`Initialize`**: Resets the `m_auiEncounter` array to zero and clears all stored creature GUIDs (`m_uiLorKhanGUID`, `m_uiHakkarGUID`, etc.). This ensures a clean slate for a new instance load.
*   **`Create`**: Called when a new instance is created. It generates the random boss ID using `GenerateRandomBoss()`, saves the initial state to the database via `SaveToDB()` (from `InstanceData`), and attempts to spawn the random boss if it hasn't been spawned yet.
*   **`Save`**: Returns the serialized string representation of the instance data (`strInstData`).
*   **`Load`**: Parses the saved data string using `LoadSaveData` (from `ScriptedInstance`). It resets any encounters marked as `IN_PROGRESS` to `NOT_STARTED` (since in-progress states are transient). If the random boss hasn't been spawned, it calls `SpawnRandomBoss()`.

### Encounter State Management

*   **`SetData`**: The central hub for updating instance state. It switches on `uiType`:
    *   **`TYPE_HAKKAR_POWER`**: Triggers `UpdateHakkarPowerStacks()`.
    *   **High Priests & Other Bosses**: Updates the corresponding index in `m_auiEncounter`.
    *   **`TYPE_MARLI`**: If the state changes to `IN_PROGRESS`, it iterates through `m_lMarliTrashGUIDList`. For each trash mob alive, not in combat, and in the correct zone/area, it commands the mob's AI to attack Marli's current victim.
    *   **`TYPE_THEKAL_DEATH_TIME` / `TYPE_THEKAL_REZ_TIME`**: Records the current game time (via `sWorld.GetGameTime()`) if the data is `SPECIAL`, otherwise stores the provided value. These are used for quest timers.
    *   **`TYPE_RANDOM_BOSS`**: Determines the boss ID. If `uiData` is 0, it checks active game events (IDs 29-32) to force a specific boss. Otherwise, it uses the provided `uiData`.
    *   **Persistence**: If `uiData` is `DONE`, it serializes the encounter array using `GenSaveData` (from `ScriptedInstance`) and persists it to the database via `SaveToDB` (from `InstanceData`).

*   **`GetData`**: Retrieves the state of a specific encounter type from `m_auiEncounter`. For `TYPE_RANDOM_BOSS`, it validates that the stored ID is within the valid range (15080-15085); otherwise, it returns 0.
*   **`GetData64`**: Retrieves stored GUIDs for specific NPCs (Lorkhan, Zath, Thekal, Jindo, Hakkar, Gahzranka). For `DATA_THEKAL_NEED_REZ`, it delegates to `Thekal_GetUnitThatNeedsRez()` to find the appropriate target dynamically.
*   **`IsEncounterInProgress`**: Returns `true` if any entry in `m_auiEncounter` is `IN_PROGRESS` or `SPECIAL`. This prevents instance resets while fights are active.

### Creature Management

*   **`OnCreatureCreate`**: Called when a creature spawns in the instance. It matches the creature's entry ID to known NPCs:
    *   **Thekal Group (Lorkhan, Zath, Thekal)**: Calls `HandleLoadCreature` to check if the encounter is already done. If done, the creature is removed. If not, its GUID is stored. For Thekal, it also updates Hakkar's power stacks.
    *   **Jindo, Hakkar, Gahzranka**: Stores their GUIDs. Hakkar and Venoxis/Arlokk/Marli triggers also update Hakkar's power stacks.
    *   **Marli Trash**: Adds specific trash mob entries (Skitterer, Venombrood, Shadowcaster, Broodwidow) to `m_lMarliTrashGUIDList`.
    *   **Venoxis, Arlokk, Marli**: Calls `HandleLoadCreature` with a null GUID store (as their GUIDs aren't strictly needed for later logic beyond the encounter state) but still updates Hakkar's power stacks.

*   **`HandleLoadCreature`**: A helper to manage creatures that should despawn if their associated encounter is already complete. If `GetData(dataType)` returns `DONE`, it adds the creature to the removal list (`AddObjectToRemoveList` from `WorldObject.Object`). Otherwise, it stores the creature's GUID in the provided reference.

*   **`OnCreatureDeath`**: Logs the death. If the dead creature is one of the four random bosses (entries 15082-15085), it marks `TYPE_RANDOM_BOSS` as `DONE`. If the creature is `NPC_NIGHTMARE_ILLUSION`, it forces a despawn after 3 seconds and sets a very long respawn time (4 days), effectively removing it from the instance.

### Hakkar Power Mechanic

*   **`UpdateHakkarPowerStacks`**: Calculates the number of High Priest encounters that are *not* `DONE` (indices 0-4 correspond to Arlokk, Jeklik, Venoxis, Marli, Thekal). This count becomes the required stack amount for `SPELL_HAKKAR_POWER`.
    *   If the required stacks are 0, it removes the aura from Hakkar.
    *   If the current stacks differ from the required stacks, it adjusts them. If no aura exists, it casts the spell repeatedly to build stacks. If an aura exists, it sets the stack amount directly.

### Thekal Resurrection Helpers

These methods support the `CheckConditionCriteriaMeet` function for quests related to the Thekal encounter.

*   **`Thekal_GetUnitThatCanRez`**: Iterates through Lorkhan, Zath, and Thekal. Returns the first one that is alive and not in a dead stand state.
*   **`Thekal_GetUnitThatNeedsRez`**: Iterates through Lorkhan, Zath, and Thekal. Returns the first one that is alive but in a dead stand state.
*   **`Thekal_GetUnitCastingRez`**: Iterates through Lorkhan, Zath, and Thekal. Checks if they are currently casting `SPELL_THEKAL_RESURRECTION` (ID 24173). Returns the caster if found.

### Random Boss Logic

*   **`GenerateRandomBoss`**: Calculates a boss ID based on the current game day. It uses a modulo operation on the week count to cycle through IDs 15082, 15083, and 15084. Note: The logic only cycles through 3 bosses, but the valid range includes 15085. It logs the result.
*   **`SpawnRandomBoss`**: Currently **deactivated**. It contains a `return;` statement at the beginning, preventing any execution of the subsequent summoning logic. The commented-out code would have summoned the creature determined by `m_auiEncounter[9]`, set its orientation, moved it to idle, and cast a visual spell.

### Condition Checking

*   **`CheckConditionCriteriaMeet`**: Evaluates quest conditions for the Zul'Gurub map (ID 309).
    *   **Condition 1**: Checks if a unit that can rez exists (`Thekal_GetUnitThatCanRez` != nullptr).
    *   **Condition 2**: Checks if a unit needs rez, no one is currently casting rez, and sufficient time has passed since the last death/rez attempt (10 seconds). This prevents spamming the resurrection quest step.

### Global Functions

*   **`GetInstanceData_instance_zulgurub`**: Factory function that creates and returns a new `instance_zulgurub` object for the given map.
*   **`OnGossipHello_go_table_madness`**: Handles gossip interaction with the "Tablet Madness" game objects. It determines the current random boss (checking game events again) and displays the appropriate gossip menu text based on whether the tablet corresponds to the active random boss.
*   **`ProcessEventId_event_summon_gahzranka`**: Event handler for summoning Gahzranka. It verifies the source is a player, checks if Gahzranka is not already in progress, casts a spell on the player, and respawns the Gahzranka creature if found.
*   **`AddSC_instance_zulgurub`**: Registers the instance script and the two global functions (`go_table_madness` and `event_summon_gahzranka`) with the script manager.

**Cross-Unit Boundaries**

*   **`Map`**: Used extensively to retrieve creatures and units by GUID (`GetCreature`, `GetUnit`). This is the primary way the instance script interacts with entities in the world.
*   **`Object` / `WorldObject`**: Used to get GUIDs, check entries, and manipulate objects (e.g., `AddObjectToRemoveList`, `SetOrientation`, `SummonCreature`).
*   **`Creature` / `Unit`**: Used to check life states (`IsAlive`), combat status (`IsInCombat`), stand states (`GetStandState`), victims (`GetVictim`), and motion masters (`GetMotionMaster`).
*   **`SpellCaster` / `SpellAuraHolder`**: Used to manage the Hakkar power aura (`CastSpell`, `GetSpellAuraHolder`, `SetStackAmount`, `RemoveAurasDueToSpell`) and check for active spells (`GetCurrentSpell`).
*   **`ScriptedInstance` / `InstanceData`**: Inherits from these classes. Uses `GenSaveData` and `LoadSaveData` for serialization, and `SaveToDB` for persistence.
*   **`World`**: Accesses `sWorld` to get game time (`GetGameTime`) and game day (`GetGameDay`) for timer-based logic and random boss generation.
*   **`GameEventMgr`**: Checks `IsActiveEvent` to determine if specific game events are forcing a particular random boss.
*   **`Log`**: Uses `sLog.Out` for debugging output in `OnCreatureDeath`, `GenerateRandomBoss`, and `Load`.
*   **`GossipDef`**: Used in `OnGossipHello_go_table_madness` to send gossip menus to players.

**Data Model**

This unit does not directly query database tables. It relies on the `InstanceData` base class to handle persistence. The `Save` method returns a string generated by `GenSaveData`, which is stored in the `instance` table (typically `data` column) by the core engine. The `Load` method parses this string. No direct SQL queries are present in this unit.

**Notable Implementation Details**

1.  **Deactivated Random Boss Spawn**: The `SpawnRandomBoss` method has an early `return;` statement, effectively disabling the automatic spawning of the weekly random boss. The logic to summon the creature is present but unreachable. This suggests the feature might be handled elsewhere or is intentionally disabled in this version.
2.  **Hardcoded Game Events**: The `SetData` and `OnGossipHello_go_table_madness` functions hardcode game event IDs (29-32) to determine the random boss. This couples the instance logic to specific game event configurations.
3.  **Marli Trash Aggro**: The `SetData` method for `TYPE_MARLI` manually triggers aggro on specific trash mobs. It checks multiple conditions (alive, not in combat, correct map/zone/area) before calling `AI()->AttackStart`. This is a fragile approach as it relies on specific area IDs and assumes the trash mobs are still present and valid.
4.  **Hakkar Power Stack Calculation**: The `UpdateHakkarPowerStacks` method calculates stacks based on indices 0-4 of `m_auiEncounter`. This assumes these indices always correspond to the five High Priests. If the encounter order changes, this logic breaks.
5.  **Thekal Resurrection Timers**: The `CheckConditionCriteriaMeet` function uses a 10-second cooldown for resurrection checks. This is hardcoded and prevents rapid re-evaluation of the quest condition.
6.  **Nightmare Illusion Despawn**: `OnCreatureDeath` handles `NPC_NIGHTMARE_ILLUSION` by forcing a despawn and setting a 4-day respawn time. This is likely to prevent the illusion from respawning during the instance run.
7.  **GUID Storage**: The instance stores GUIDs for key NPCs (Lorkhan, Zath, Thekal, Jindo, Hakkar, Gahzranka, Marli). This allows direct access to these units without searching the map, improving performance for frequent checks (like Hakkar's power stacks).
8.  **Random Boss Generation Logic**: `GenerateRandomBoss` uses a formula based on `GetGameDay()` to cycle through bosses. However, it only generates IDs 15082-15084, ignoring 15085. The `GetData` function for `TYPE_RANDOM_BOSS` accepts 15085, suggesting a discrepancy between generation and validation.

## Member Reference

**Initialize**
Resets the internal encounter state array `m_auiEncounter` to zero and clears all stored creature GUIDs (`m_uiLorKhanGUID`, `m_uiHakkarGUID`, etc.) to ensure a clean state for a new instance.

**UpdateHakkarPowerStacks**
Calculates the number of High Priest encounters (indices 0–4 in `m_auiEncounter`) that are not `DONE`. It then adjusts the stack count of `SPELL_HAKKAR_POWER` on the Hakkar creature (`m_uiHakkarGUID`) to match this count. If the count is zero, it removes the aura. If the aura exists, it updates the stack amount directly; otherwise, it casts the spell repeatedly to build stacks.

**instance_zulgurub**
Constructor that initializes the `ScriptedInstance` base class, sets `m_randomBossSpawned` to `false`, and calls `Initialize()` to reset state variables.

**IsEncounterInProgress**
Iterates through `m_auiEncounter` and returns `true` if any encounter is in `IN_PROGRESS` or `SPECIAL` state, indicating that a boss fight is active.

**OnCreatureCreate**
Handles the creation of creatures in the instance. It identifies specific NPCs by entry ID, stores their GUIDs in member variables (e.g., `m_uiHakkarGUID`), adds trash mobs to `m_lMarliTrashGUIDList`, and calls `HandleLoadCreature` for bosses that should despawn if their encounter is already complete. It also triggers `UpdateHakkarPowerStacks` when relevant High Priest or Hakkar-related creatures spawn.

**HandleLoadCreature**
Helper method that checks if the encounter associated with `dataType` is `DONE`. If so, it adds the creature to the removal list via `AddObjectToRemoveList` (from `WorldObject.Object`). Otherwise, it stores the creature's GUID in the provided reference `storeGuid`.

**SetData**
Updates the instance state based on `uiType`. It modifies `m_auiEncounter` for various bosses, handles special logic for `TYPE_MARLI` (triggering trash aggro) and `TYPE_RANDOM_BOSS` (checking game events), and records timestamps for Thekal's resurrection mechanics. If the new state is `DONE`, it serializes the encounter data using `GenSaveData` (from `ScriptedInstance`) and saves it to the database via `SaveToDB` (from `InstanceData`).

**Save**
Returns the C-string pointer to `strInstData`, which holds the serialized encounter state.

**Load**
Parses the saved data string using `LoadSaveData` (from `ScriptedInstance`). It resets any `IN_PROGRESS` encounters to `NOT_STARTED` and calls `SpawnRandomBoss` if the random boss has not yet been spawned.

**GetData**
Returns the state of a specific encounter type from `m_auiEncounter`. For `TYPE_RANDOM_BOSS`, it validates the stored ID against the range 15080–15085.

**GetData64**
Returns stored GUIDs for specific NPCs (Lorkhan, Zath, Thekal, Jindo, Hakkar, Gahzranka). For `DATA_THEKAL_NEED_REZ`, it calls `Thekal_GetUnitThatNeedsRez` to dynamically find a target.

**CheckConditionCriteriaMeet**
Evaluates quest conditions for map ID 309. Condition 1 checks if a unit capable of resurrection exists. Condition 2 checks if a unit needs resurrection, no one is currently casting resurrection, and sufficient time (10 seconds) has passed since the last death or resurrection attempt.

**Create**
Called when a new instance is created. It generates the random boss ID via `GenerateRandomBoss`, saves the initial state to the database via `SaveToDB` (from `InstanceData`), and calls `SpawnRandomBoss` if necessary.

**OnCreatureDeath**
Logs the creature's death. If the creature is one of the random bosses (entries 15082–15085), it sets `TYPE_RANDOM_BOSS` to `DONE`. If the creature is `NPC_NIGHTMARE_ILLUSION`, it forces a despawn and sets a long respawn time.

**GenerateRandomBoss**
Calculates a random boss ID based on the current game day (`sWorld.GetGameDay`). It uses a modulo operation to cycle through IDs 15082, 15083, and 15084, storing the result in `randomBossEntry` and logging the outcome.

**SpawnRandomBoss**
Currently deactivated due to an early `return` statement. The intended logic was to summon the creature specified by `m_auiEncounter[9]`, set its orientation, move it to idle, and cast a visual spell.

**Thekal_GetUnitThatCanRez**
Iterates through Lorkhan, Zath, and Thekal (using their stored GUIDs) and returns the first unit that is alive and not in a dead stand state.

**Thekal_GetUnitThatNeedsRez**
Iterates through Lorkhan, Zath, and Thekal and returns the first unit that is alive but in a dead stand state.

**Thekal_GetUnitCastingRez**
Iterates through Lorkhan, Zath, and Thekal and returns the first unit that is currently casting `SPELL_THEKAL_RESURRECTION`.

**GetInstanceData_instance_zulgurub**
Factory function that creates and returns a new `instance_zulgurub` object for the provided `Map`.

**OnGossipHello_go_table_madness**
Handles gossip interactions with "Tablet Madness" game objects. It determines the current random boss by checking active game events (IDs 29–32) and displays the appropriate gossip menu text based on whether the tablet matches the active random boss.

**ProcessEventId_event_summon_gahzranka**
Event handler for summoning Gahzranka. It verifies the source is a player, checks if Gahzranka is not already in progress, casts a spell on the player, and respawns the Gahzranka creature if found.

**AddSC_instance_zulgurub**
Registers the `instance_zulgurub` script and the global functions `OnGossipHello_go_table_madness` and `ProcessEventId_event_summon_gahzranka` with the script manager via `ScriptMgr.RegisterSelf`.

---

<!-- machine-true, projected from graph.json -->

## Map — instance_zulgurub

*Source:* instance_zulgurub.cpp, zulgurub.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| Initialize | method | — | — | — |
| UpdateHakkarPowerStacks | method | Map.Main/GetCreature, ObjectGuid/ObjectGuid#5, SpellAuraHolder/GetStackAmount, SpellCaster/CastSpell#2, Unit.Main/GetSpellAuraHolder#2, Unit.Main/IsAlive, Unit.Main/RemoveAurasDueToSpell, Unit.SpellAuras/SetStackAmount | — | — |
| instance_zulgurub | ctor | — | — | — |
| IsEncounterInProgress | method | — | — | — |
| OnCreatureCreate | method | Object/GetEntry, Object/GetGUID | — | — |
| HandleLoadCreature | method | Object/GetGUID, WorldObject.Object/AddObjectToRemoveList | — | — |
| SetData | method | Creature.Main/AI, CreatureAI/AttackStart, GameEventMgr.Main/IsActiveEvent, InstanceData/SaveToDB, Log.Main/Out, Map.Main/GetCreature, Map.Main/GetId, Map.Main/GetInstanceId, Map.Main/GetMapName, ObjectGuid/ObjectGuid#5, ScriptedInstance/GenSaveData, Unit.Main/GetVictim, Unit.Main/IsAlive, Unit.Main/IsInCombat, World/GetGameTime, WorldObject.Object/GetAreaId, WorldObject.Object/GetMapId, WorldObject.Object/GetZoneId | — | — |
| Save | method | — | — | — |
| Load | method | Log.Main/Out, Map.Main/GetId, Map.Main/GetInstanceId, Map.Main/GetMapName, ScriptedInstance/LoadSaveData | — | — |
| GetData | method | — | — | — |
| GetData64 | method | Object/GetGUID | — | — |
| CheckConditionCriteriaMeet | method | World/GetGameTime | — | — |
| Create | method | InstanceData/SaveToDB, ScriptedInstance/GenSaveData | — | — |
| OnCreatureDeath | method | Creature.Main/ForcedDespawn, Creature.Main/SetRespawnTime, Log.Main/Out, Object/GetEntry | — | — |
| GenerateRandomBoss | method | Log.Main/Out, World/GetGameDay | — | — |
| SpawnRandomBoss | method | Creature.MotionMaster/MoveIdle, SpellCaster/CastSpell#2, Unit.Main/GetMotionMaster, WorldObject.Object/SetOrientation, WorldObject.Object/SummonCreature | — | — |
| Thekal_GetUnitThatCanRez | method | Map.Main/GetUnit, ObjectGuid/ObjectGuid#5, Unit.Main/GetStandState, Unit.Main/IsAlive | — | — |
| Thekal_GetUnitThatNeedsRez | method | Map.Main/GetUnit, ObjectGuid/ObjectGuid#5, Unit.Main/GetStandState, Unit.Main/IsAlive | — | — |
| Thekal_GetUnitCastingRez | method | Map.Main/GetUnit, ObjectGuid/ObjectGuid#5, SpellCaster/GetCurrentSpell | — | — |
| GetInstanceData_instance_zulgurub | function | — | — | — |
| OnGossipHello_go_table_madness | function | GameEventMgr.Main/IsActiveEvent, GossipDef/SendGossipMenu, InstanceData/GetData, Object/GetEntry, Object/GetGUID, ObjectGuid/ObjectGuid#5, WorldObject.Object/GetInstanceData | — | — |
| ProcessEventId_event_summon_gahzranka | function | Creature.Main/Respawn, InstanceData/GetData, InstanceData/GetData64, InstanceData/SetData, Map.Main/GetCreature, Object/ToPlayer, ObjectGuid/ObjectGuid#5, SpellCaster/CastSpell#2, WorldObject.Object/GetInstanceData | — | — |
| AddSC_instance_zulgurub | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
