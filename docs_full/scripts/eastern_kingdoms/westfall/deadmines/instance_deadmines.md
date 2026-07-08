# instance_deadmines

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`instance_deadmines` implements the instance script for the **Deadmines** dungeon. It coordinates dungeon-specific mechanics by tracking boss states (Rhahk'zor, Sneed, Gildnid, Mr. Smite), managing door openings upon boss deaths, and executing timed patrol spawn sequences. It also handles the "Iron Door" event sequence involving Mr. Smite and specific patrol creatures. Additionally, the unit defines a global area trigger (`AreaTrigger_at_dmf_chest_dm`) that reveals a Darkmoon Chest to players who have completed the quest "Fortune Awaits."

## Member-by-Member Behavior

### Initialization and State Management

*   **`instance_deadmines` (ctor)**: Constructs the instance data, passing the `Map` pointer to `ScriptedInstance` and calling `Initialize()` to reset state.
*   **`Initialize`**: Resets the `m_auiEncounter` array, clears all stored GUIDs, and initializes timers and flags. Specifically, it sets `m_uiSpawnPatrolOnRhahkDeath` and `m_uiSpawnPatrolOnGilnidDeath` to 30,000 ms, `m_isRhahkDead` and `m_isGilnidDead` to `false`, `m_isGunPowderEventDone` to 0, and `m_uiIronDoorTimer`/`m_uiIronDoorStep` to 0.

### Entity Tracking

*   **`OnCreatureCreate`**: Captures GUIDs for Rhahk'zor (`NPC_RHAHKZOR`), Gildnid (`NPC_GILDNID`), and Mr. Smite (`NPC_MR_SMITE`). It identifies patrol creatures by their respawn delay (43199 for Rhahk's patrols, 43201 for Gildnid's patrols) and sets them to invisible (`VISIBILITY_OFF`) and neutral faction (35).
*   **`OnObjectCreate`**: Captures GUIDs for the Iron Door (`GO_IRON_CLAD`), Defias Cannon (`GO_DEFIAS_CANNON`), and three doors (`GO_DOOR1`, `GO_DOOR2`, `GO_DOOR3`). `GO_DOOR2` and `GO_DOOR3` are distinguished by their X coordinates (`-291.0f` to `-290.0f` and `-169.0f` to `-168.0f`, respectively). It also hides the Darkmoon Chest (`GO_DMF_CHEST`).

### Encounter Progression

*   **`OnCreatureDeath`**:
    *   **Rhahk'zor**: Opens Door 1 if inactive. Sets `m_isRhahkDead` to `true` and resets `m_uiSpawnPatrolOnRhahkDeath` to 60,000 ms.
    *   **Sneed**: Opens Door 2 if inactive.
    *   **Gildnid**: Opens Door 3 if inactive. Sets `m_isGilnidDead` to `true` and resets `m_uiSpawnPatrolOnGilnidDeath` to 30,000 ms.

### Data Access

*   **`SetData`**:
    *   `TYPE_DEFIAS_ENDDOOR`: If `IN_PROGRESS`, opens the Iron Door (`m_uiIronCladGUID`) if closed and starts `m_uiIronDoorTimer` (3,000 ms). Updates `m_auiEncounter[0]`.
    *   `GUN_POWDER_EVENT`: Updates `m_isGunPowderEventDone`.
*   **`GetData`**: Returns `m_auiEncounter[0]` for `TYPE_DEFIAS_ENDDOOR` or `m_isGunPowderEventDone` for `GUN_POWDER_EVENT`.
*   **`GetData64`**: Returns `m_uiIronCladGUID` for `DATA_DEFIAS_DOOR`.

### Time-Based Logic

*   **`Update`**:
    *   **Rhahk Patrol Spawn**: If `m_isRhahkDead` and timer expires, finds Rhahk'zor. Searches for creatures with entries 634 and 1729 within 400 units. Those with respawn delay 43199 are made visible and hostile (faction 17). Timer resets to 0.
    *   **Gilnid Patrol Spawn**: Similar to Rhahk, but uses entries 4417/4418 and respawn delay 43201.
    *   **Iron Door Event**: If `m_uiIronDoorTimer` expires:
        *   **Step 0**: Mr. Smite speaks (`INST_SAY_ALARM1`). Creatures with entry 657 and respawn delay 43202 within 400 units move to `(-99.6611, -671.071655, 7.42241)`. Timer sets to 15,000 ms, step increments to 1.
        *   **Step 1**: Mr. Smite speaks (`INST_SAY_ALARM2`). Timer and step reset to 0. Debug log emitted.
        *   If Mr. Smite is missing, timer resets to 0.

### Global Scripts

*   **`GetInstanceData_instance_deadmines`**: Factory function creating `instance_deadmines` instances.
*   **`AreaTrigger_at_dmf_chest_dm`**: Checks if a player has completed `QUEST_FORTUNE_AWAITS`. If so, makes the nearest `GO_DMF_CHEST` within 100 units visible.
*   **`AddSC_instance_deadmines`**: Registers the instance script and area trigger with the script manager.

## Cross-Unit Boundaries

*   **`instance_deadmines` (ctor)**: Inherits from `ScriptedInstance`.
*   **`OnCreatureCreate`**: Calls `Creature.Main/GetRespawnDelay`, `Object/GetEntry`, `Object/GetGUID` for identification; `Unit.Main/SetFactionTemplateId`, `Unit.Main/SetVisibility` for modification.
*   **`OnCreatureDeath`**: Calls `Object/GetEntry`, `Map.Main/GetGameObject`, `GameObject/GetGoState`, `ScriptedInstance/DoUseDoorOrButton`. Uses `ObjectGuid/ObjectGuid#5`.
*   **`OnObjectCreate`**: Calls `Object/GetEntry`, `Object/GetGUID`, `WorldObject.Object/GetPositionX`, `GameObject/SetVisible`.
*   **`SetData`**: Calls `Map.Main/GetGameObject`, `GameObject/GetGoState`, `GameObject/UseDoorOrButton`. Uses `ObjectGuid/ObjectGuid#5`.
*   **`Update`**: Calls `Map.Main/GetCreature`, `GridSearchers/GetCreatureListWithEntryInGrid#2`, `Creature.Main/GetRespawnDelay`, `Unit.Main/SetVisibility`, `Unit.Main/SetFactionTemplateId`, `Unit.Main/GetMotionMaster`, `Creature.MotionMaster/MovePoint`, `ScriptMgr/DoScriptText`, `Log.Main/Out`. Uses `ObjectGuid/ObjectGuid#5`.
*   **`AreaTrigger_at_dmf_chest_dm`**: Calls `Player.Main/GetQuestStatus`, `WorldObject.Object/FindNearestGameObject`, `GameObject/SetVisible`.
*   **`AddSC_instance_deadmines`**: Calls `Script/Script`, `ScriptMgr/RegisterSelf`. Called by `ScriptLoader/AddScripts`.

## Data Model

This unit does not interact directly with any database tables. All state is managed in-memory.

## Notable Implementation Details

*   **Door Identification**: `OnObjectCreate` uses X-coordinate ranges to distinguish `GO_DOOR2` and `GO_DOOR3`, implying multiple objects share the same entry ID.
*   **Patrol Markers**: Patrol creatures are identified by specific `GetRespawnDelay()` values (43199, 43201, 43202) rather than standard respawn times. They start invisible/neutral and become visible/hostile after boss deaths.
*   **Iron Door Sequence**: The `Update` loop manages a two-step sequence for the Iron Door event, moving specific creatures to a fixed coordinate and triggering dialogue.
*   **Timer Safety**: Timers in `Update` reset to 0 if the target creature (boss or Mr. Smite) is not found, preventing dangling references.

## Member Reference

*   **instance_deadmines**: Constructor initializing the instance data structure and calling `Initialize()`.
*   **Initialize**: Resets all internal state variables, GUIDs, timers, and flags to defaults.
*   **OnCreatureCreate**: Captures boss GUIDs and initializes patrol creatures as invisible/neutral based on respawn delay.
*   **OnCreatureDeath**: Opens doors on boss death and triggers patrol spawn timers for Rhahk'zor and Gildnid.
*   **OnObjectCreate**: Captures game object GUIDs, distinguishes doors by position, and hides the Darkmoon Chest.
*   **SetData**: Updates instance state, initiating the Iron Door event or updating the Gun Powder event flag.
*   **GetData**: Returns the state of the Iron Door event or Gun Powder event flag.
*   **GetData64**: Returns the GUID of the Iron Door.
*   **Update**: Executes time-based logic for patrol spawns and the Iron Door event sequence.
*   **GetInstanceData_instance_deadmines**: Factory function creating `instance_deadmines` objects.
*   **AreaTrigger_at_dmf_chest_dm**: Global area trigger revealing the Darkmoon Chest to players with completed quest "Fortune Awaits".
*   **AddSC_instance_deadmines**: Registers the instance script and area trigger with the server.

---

<!-- machine-true, projected from graph.json -->

## Map — instance_deadmines

*Source:* instance_deadmines.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| instance_deadmines | ctor | ScriptedInstance/ScriptedInstance | — | — |
| Initialize | method | — | — | — |
| OnCreatureCreate | method | Creature.Main/GetRespawnDelay, Object/GetEntry, Object/GetGUID, Unit.Main/SetFactionTemplateId, Unit.Main/SetVisibility | — | — |
| OnCreatureDeath | method | GameObject/GetGoState, Map.Main/GetGameObject, Object/GetEntry, ObjectGuid/ObjectGuid#5, ScriptedInstance/DoUseDoorOrButton | — | — |
| OnObjectCreate | method | GameObject/SetVisible, Object/GetEntry, Object/GetGUID, WorldObject.Object/GetPositionX | — | — |
| SetData | method | GameObject/GetGoState, GameObject/UseDoorOrButton, Map.Main/GetGameObject, ObjectGuid/ObjectGuid#5 | — | — |
| GetData | method | — | — | — |
| GetData64 | method | — | — | — |
| Update | method | Creature.Main/GetRespawnDelay, Creature.MotionMaster/MovePoint, GridSearchers/GetCreatureListWithEntryInGrid#2, Log.Main/Out, Map.Main/GetCreature, ObjectGuid/ObjectGuid#5, ScriptMgr/DoScriptText, Unit.Main/GetMotionMaster, Unit.Main/SetFactionTemplateId, Unit.Main/SetVisibility | — | — |
| GetInstanceData_instance_deadmines | function | — | — | — |
| AreaTrigger_at_dmf_chest_dm | function | GameObject/SetVisible, Player.Main/GetQuestStatus, WorldObject.Object/FindNearestGameObject | — | — |
| AddSC_instance_deadmines | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
