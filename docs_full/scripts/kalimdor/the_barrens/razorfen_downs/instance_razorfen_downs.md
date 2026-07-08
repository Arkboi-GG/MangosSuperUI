<!-- provenance: verbose -->
# instance_razorfen_downs

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`instance_razorfen_downs` implements the instance script for the *Razorfen Downs* dungeon. It tracks key GameObjects (the Gong and two Fire Cups), manages the wave-based summoning mechanic triggered by the Gong, persists the final boss encounter state, and updates visual elements for puzzle completion. It does not implement combat AI or player movement logic.

## Member-by-Member Behavior

### Initialization and Persistence

**`instance_razorfen_downs`**
Constructs the instance script, passing the `Map*` to the `ScriptedInstance` base class, then calls `Initialize()` to reset all internal state.

**`Initialize`**
Resets `uiGongGUID`, `uiCupFire1GUID`, `uiCupFire2GUID`, `uiGongWaves`, and the `m_auiEncounter` array to zero/default values.

**`Load`**
Parses the saved instance data string from the database. It reads `m_auiEncounter[0]` (boss state). If the loaded state is `IN_PROGRESS`, it forces it to `NOT_STARTED` to prevent stuck encounters after crashes. Logs success/failure via `Log.Main`.

### Object Tracking

**`OnObjectCreate`**
Called when GameObjects spawn.
-   **Gong (`GO_GONG`):** Stores its GUID in `uiGongGUID`. If the boss is already `DONE`, it sets `GO_FLAG_NO_INTERACT` to disable interaction.
-   **Fire Cups (`GO_IDOL_CUP_FIRE`):** Stores the first two encountered GUIDs in `uiCupFire1GUID` and `uiCupFire2GUID`.

### Event Handling

**`SetData`**
Processes events from other scripts:
-   **`DATA_GONG_WAVES`:** Updates `uiGongWaves`.
    -   Waves 9 and 14: Removes `GO_FLAG_NO_INTERACT` from the Gong, re-enabling it.
    -   Waves 1, 10, and 15: Sets `GO_FLAG_NO_INTERACT` on the Gong. Summons creatures based on the wave:
        -   Wave 1: 7 `CREATURE_TOMB_FIEND`.
        -   Wave 10: 3 `CREATURE_TOMB_REAVER`.
        -   Wave 15: 1 `CREATURE_TUTEN_KASH` (Boss).
    -   Summoned creatures are placed at hardcoded coordinates with ±5 unit random offsets and commanded to move to a target point using `MOVE_PATHFINDING`. They are set to run (`SetWalk(false)`).
-   **`BOSS_TUTEN_KASH`:** Updates `m_auiEncounter[0]`. If `DONE`, calls `SaveToDB()`.
-   **`EXTINGUISH_FIRES`:** Sets the loot state of both Fire Cups to `GO_JUST_DEACTIVATED`.
-   **Generic Save:** If `uiData == DONE` (for any type), it serializes `m_auiEncounter[0]` and calls `SaveToDB()`.

### Data Retrieval

**`GetData`**
Returns `uiGongWaves` for `DATA_GONG_WAVES`; otherwise returns 0.

**`GetData64`**
Returns `uiGongGUID` for `DATA_GONG`; otherwise returns 0.

### Registration

**`GetInstanceData_instance_razorfen_downs`**
Factory function creating a new `instance_razorfen_downs` instance.

**`AddSC_instance_razorfen_downs`**
Registers the script with `ScriptMgr` under the name `"instance_razorfen_downs"`. Called by `ScriptLoader/AddScripts`.

## Cross-Unit Boundaries

-   **Calls `ScriptedInstance`:** Inherits base functionality; calls `SaveToDB()` for persistence.
-   **Calls `Log.Main`:** Logs load/save events via macros.
-   **Calls `Map.Main`:** Retrieves GameObjects via `GetGameObject` and map info for logging.
-   **Calls `Object` / `WorldObject.Object`:** Gets entries/GUIDs, sets/removes interaction flags, and summons creatures.
-   **Calls `Creature.MotionMaster` / `Unit.Main`:** Configures summoned creature movement (`MovePoint`, `SetWalk`).
-   **Calls `GameObject`:** Updates fire cup states via `SetLootState`.
-   **Calls `shared_Util`:** Uses `irand` for spawn position jitter.
-   **Calls `Script` / `ScriptMgr`:** Registers the script.
-   **Called by `ScriptLoader`:** Invokes `AddSC_instance_razorfen_downs` at startup.
-   **Called by External Scripts:** Boss AI, Gong GameObject, and Puzzle scripts call `SetData`, `GetData`, and `GetData64` to drive instance logic.

## Data Model

This unit does not execute raw SQL. It relies on `ScriptedInstance` to persist data to the `instance` table.
-   **Column `data`:** Stores the serialized string of `m_auiEncounter[0]`. Parsed in `Load`, written in `SetData`.

## Notable Implementation Details

-   **Hardcoded Coordinates:** Summon positions and movement targets are fixed floats, tying the script to the specific map geometry.
-   **Stuck Instance Recovery:** `Load` resets `IN_PROGRESS` to `NOT_STARTED` to ensure playable instances after crashes.
-   **Gong Locking:** The Gong is disabled during active waves and permanently after the boss is defeated to prevent abuse.
-   **Randomized Spawns:** Creatures spawn with ±5 unit offsets to avoid stacking and use pathfinding to reach their target points.

## Member Reference

**`instance_razorfen_downs`**
Constructor initializing the instance script and calling `Initialize()`.

**`Initialize`**
Resets all internal state variables (GUIDs, encounter flags, wave counters) to default values.

**`Load`**
Loads instance state from the database string. Parses `m_auiEncounter[0]`. Resets `IN_PROGRESS` states to `NOT_STARTED` to prevent stuck instances. Logs load status.

**`OnObjectCreate`**
Tracks GUIDs of key GameObjects. Stores `uiGongGUID` for the Gong. Stores `uiCupFire1GUID` and `uiCupFire2GUID` for the two Fire Cups. Disables Gong interaction if the boss is already defeated.

**`SetData`**
Handles instance events.
-   `DATA_GONG_WAVES`: Updates wave count. Enables/Disables Gong interaction. Summons specific creatures (Tomb Fiend, Tomb Reaver, Tuten Kash) at hardcoded coordinates with random offsets.
-   `BOSS_TUTEN_KASH`: Updates boss encounter state. Saves to DB if `DONE`.
-   `EXTINGUISH_FIRES`: Deactivates the two Fire Cups.
-   Generic `DONE` check: Serializes and saves encounter data to DB.

**`GetData`**
Returns integer data. Supports `DATA_GONG_WAVES`. Returns `0` otherwise.

**`GetData64`**
Returns 64-bit GUIDs. Supports `DATA_GONG`. Returns `0` otherwise.

**`GetInstanceData_instance_razorfen_downs`**
Factory function that creates a new `instance_razorfen_downs` object.

**`AddSC_instance_razorfen_downs`**
Registers the script with the `ScriptMgr` under the name `"instance_razorfen_downs"`. Called by `ScriptLoader`.

---

<!-- machine-true, projected from graph.json -->

## Map — instance_razorfen_downs

*Source:* instance_razorfen_downs.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| instance_razorfen_downs | ctor | ScriptedInstance/ScriptedInstance | — | — |
| Initialize | method | — | — | — |
| Load | method | Log.Main/Out, Map.Main/GetId, Map.Main/GetInstanceId, Map.Main/GetMapName | — | — |
| OnObjectCreate | method | Object/GetEntry, Object/GetGUID, WorldObject.Object/SetFlag | — | — |
| SetData | method | Creature.MotionMaster/MovePoint, GameObject/SetLootState, InstanceData/SaveToDB, Log.Main/Out, Map.Main/GetGameObject, Map.Main/GetId, Map.Main/GetInstanceId, Map.Main/GetMapName, ObjectGuid/ObjectGuid#5, shared_Util/irand, Unit.Main/GetMotionMaster, Unit.Main/SetWalk, WorldObject.Object/RemoveFlag, WorldObject.Object/SetFlag, WorldObject.Object/SummonCreature#2 | — | — |
| GetData | method | — | — | — |
| GetData64 | method | — | — | — |
| GetInstanceData_instance_razorfen_downs | function | — | — | — |
| AddSC_instance_razorfen_downs | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
