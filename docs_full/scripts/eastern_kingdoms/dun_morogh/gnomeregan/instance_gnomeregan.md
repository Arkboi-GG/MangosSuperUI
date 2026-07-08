# instance_gnomeregan

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# instance_gnomeregan

**Purpose & Responsibilities**

`instance_gnomeregan` is the `ScriptedInstance` data holder and logic coordinator for the Gnomeregan dungeon instance. It manages the state of two primary encounters: **Grubbis** (a mini-boss involving explosive charges) and **Thermaplugg** (the final boss involving a bomb-face puzzle).

Its core responsibilities are:
1.  **Tracking Entities:** Storing GUIDs for critical NPCs (Blastmaster Shortfuse, Alarm-a-Bomb) and GameObjects (explosive charges, cave-in markers, doorways, and bomb faces) upon their creation.
2.  **Grubbis Encounter Logic:** Sorting explosive charges spatially (East-to-West, then North/South relative to cave-ins) to determine which charges spawn during the fight. It handles the spawning of these charges and triggers their detonation via Blastmaster Shortfuse when required.
3.  **Thermaplugg Encounter Logic:** Managing the state of six "Bomb Faces." It locks/unlocks the final chamber door and activates/deactivates specific bomb faces based on encounter phases.
4.  **Persistence:** Saving and loading encounter progress (`NOT_STARTED`, `IN_PROGRESS`, `DONE`, `FAIL`) to the database.

This unit does not contain AI logic itself; rather, it provides the infrastructure and state queries that the AI scripts for `boss_grubbis`, `boss_thermaplugg`, and various NPCs rely on.

## Member-by-Member Behavior

### Initialization and Lifecycle

*   **`instance_gnomeregan` (Constructor):** Initializes member variables for GUIDs to zero/null. Calls `Initialize()` to reset encounter states and bomb face arrays. Inherits from `ScriptedInstance`.
*   **`Initialize`:** Resets the `m_auiEncounter` array (tracking Grubbis and Thermaplugg status) and the `m_asBombFaces` array (tracking activation state and timers for the 6 bomb faces) to zero/false. Also clears the sorted explosive GUIDs array.
*   **`~instance_gnomeregan` (Destructor):** Empty override. Cleanup is handled by the base class or memory management of the instance system.
*   **`GetInstanceData_instance_gnomeregan`:** Factory function called by the engine to create an instance of this class for a given `Map`.
*   **`AddSC_instance_gnomeregan`:** Registration function. Creates a `Script` object, assigns the factory function, and registers it with the `ScriptMgr`. Called by `ScriptLoader::AddScripts`.

### Entity Tracking

*   **`OnCreatureCreate`:** Triggered by the engine when a creature spawns in the instance.
    *   If the creature is `NPC_BLASTMASTER_SHORTFUSE` (7998), stores its GUID in `m_uiBlastmasterShortfuseGUID`.
    *   If the creature is `NPC_ALARM_A_BOMB_2600` (7897), stores its GUID in `m_uiAlarmABomb2600GUID`.
*   **`OnObjectCreate`:** Triggered by the engine when a GameObject spawns.
    *   Stores GUIDs for `GO_RED_ROCKET` (103820) in a list (`m_lRedRocketGUIDs`).
    *   Stores GUIDs for `GO_CAVE_IN_NORTH` (146085) and `GO_CAVE_IN_SOUTH` (146086) for spatial calculations.
    *   Adds `GO_EXPLOSIVE_CHARGE` (144065) objects to a list (`m_lExplosiveCharges`) for later sorting.
    *   Stores the GUID for `GO_THE_FINAL_CHAMBER` (142207) door.
    *   Maps `GO_GNOME_FACE_1` through `GO_GNOME_FACE_6` to indices 0–5 in the `m_asBombFaces` array, storing their GUIDs.

### Grubbis Encounter Logic

The Grubbis encounter involves managing explosive charges. The logic relies on spatial sorting to determine which charges are "active" or relevant for specific phases.

*   **`sortFromEastToWest`:** Static helper function. Compares two `GameObject` pointers based on their Y position (`GetPositionY`). Returns true if the first object has a lower Y value than the second. Note: In many coordinate systems, Y corresponds to North/South, but the comment says "East to West". Regardless of cardinal direction naming, it sorts by Y-coordinate ascending.
*   **`SetData` (Case `TYPE_GRUBBIS`):**
    *   **`IN_PROGRESS`:** If the list of explosive charges (`m_lExplosiveCharges`) is not empty:
        1.  Sorts the list using `sortFromEastToWest`.
        2.  Retrieves the North and South Cave-In GameObjects.
        3.  Iterates through the sorted charges. Uses `GetDistanceOrder` to determine if a charge is closer to the South Cave-In or the North Cave-In.
        4.  Assigns up to `MAX_EXPLOSIVES_PER_SIDE` (2) charges to the South side (`m_auiExplosiveSortedGUIDs[0]`) and up to 2 to the North side (`m_auiExplosiveSortedGUIDs[1]`).
        5.  Clears the original list `m_lExplosiveCharges` after sorting.
    *   **`FAIL`:** Triggers `SetData(TYPE_EXPLOSIVE_CHARGE, DATA_EXPLOSIVE_CHARGE_USE)`. This causes any currently spawned explosive charges to be used/detonated by Blastmaster Shortfuse, effectively despawning them or triggering their death animation.
    *   **`DONE`:** Respawns all `GO_RED_ROCKET` GameObjects with a 1-hour respawn timer.

*   **`SetData` (Case `TYPE_EXPLOSIVE_CHARGE`):** Handles sub-events within the Grubbis fight.
    *   **`DATA_EXPLOSIVE_CHARGE_1` to `_4`:** Respawns a specific explosive charge from the pre-sorted `m_auiExplosiveSortedGUIDs` array.
        *   Charge 1 & 2 come from the South side (index 0).
        *   Charge 3 & 4 come from the North side (index 1).
        *   Each spawned GUID is added to `m_luiSpawnedExplosiveChargeGUIDs` to track active charges.
    *   **`DATA_EXPLOSIVE_CHARGE_USE`:** Retrieves `Blastmaster Shortfuse`. Iterates through all currently spawned explosive charges (`m_luiSpawnedExplosiveChargeGUIDs`). Calls `Use(pBlastmaster)` on each GameObject. This likely triggers the explosion effect. Finally, clears the spawned list.

### Thermaplugg Encounter Logic

The Thermaplugg encounter involves a puzzle with 6 bomb faces and locking the final door.

*   **`SetData` (Case `TYPE_THERMAPLUGG`):**
    *   **`IN_PROGRESS`:**
        1.  Retrieves the Final Chamber Door (`m_uiDoorFinalChamberGUID`).
        2.  If the door is `GO_ACTIVATED`, resets it.
        3.  Sets the `GO_FLAG_LOCKED` flag on the door. *Note: The code comments that this might not take effect immediately due to update ticks, but the flag is set.*
        4.  Calls `DoActivateBombFace(2)` (index 2, which is the 3rd face). This is hardcoded to always activate this specific face when the fight starts.
    *   **`DONE` or `FAIL`:**
        1.  Retrieves the Final Chamber Door.
        2.  If the door is `GO_READY`, uses it (opens it).
        3.  Removes the `GO_FLAG_LOCKED` flag.
        4.  Iterates through all 6 bomb faces and calls `DoDeactivateBombFace(i)` to reset them.

*   **`GetBombFaces`:** Returns a pointer to the `m_asBombFaces` array. Used by `boss_thermaplugg::Aggro` to access the state of the faces.
*   **`DoActivateBombFace`:**
    *   Checks bounds (`MAX_GNOME_FACES`).
    *   If the face at `uiIndex` is not already activated:
        *   Calls `DoUseDoorOrButton` on the face's GUID (triggers visual/state change).
        *   Sets `m_bActivated` to true.
        *   Sets `m_uiBombTimer` to 3000 (likely milliseconds).
*   **`DoDeactivateBombFace`:**
    *   Checks bounds.
    *   If the face at `uiIndex` is activated:
        *   Calls `DoUseDoorOrButton` on the face's GUID.
        *   Sets `m_bActivated` to false.
        *   Resets `m_uiBombTimer` to 0.

### Persistence and Data Access

*   **`Save`:** Returns the string `strInstData`. This string is populated in `SetData` when an encounter reaches `DONE`.
*   **`Load`:** Parses the saved string from the database.
    *   Reads two integers into `m_auiEncounter[0]` (Grubbis) and `m_auiEncounter[1]` (Thermaplugg).
    *   **Critical Logic:** Iterates through the encounter array. If any encounter is marked `IN_PROGRESS`, it resets it to `NOT_STARTED`. This prevents stuck states if the server crashed mid-fight.
*   **`GetData`:** Returns the status of an encounter.
    *   `TYPE_GRUBBIS` returns `m_auiEncounter[0]`.
    *   `TYPE_THERMAPLUGG` returns `m_auiEncounter[1]`.
*   **`GetData64`:** Returns GUIDs for specific objects.
    *   `GO_CAVE_IN_NORTH` / `GO_CAVE_IN_SOUTH`: Returns the respective cave-in GUIDs.
    *   `NPC_ALARM_A_BOMB_2600`: Returns the Alarm-a-Bomb GUID.

## Cross-Unit Boundaries

### Called By (External Units calling into `instance_gnomeregan`)

1.  **`gnomeregan` (NPC Scripts):**
    *   `GossipHello_npc_blastmaster_emi_shortfuse`, `GossipSelect_npc_blastmaster_emi_shortfuse`, `npc_blastmaster_emi_shortfuseAI`: Call `GetData` to check encounter status (likely to determine gossip options or behavior).
    *   `JustDied`, `JustSummoned`, `StartQuest`, `UpdateEscortAI`, `WaypointReached`, `WaypointStart`: Call `GetData64` to retrieve GUIDs for cave-ins or Alarm-a-Bomb, likely for movement targets or quest triggers.
2.  **`boss_thermaplugg` (Boss Script):**
    *   `Aggro`: Calls `GetBombFaces` to get the array of bomb faces and `DoActivateBombFace` (via `SetData` or direct call? Map says `SetData` is called by `boss_thermaplugg/Aggro`? No, Map says `SetData` is called by `boss_thermaplugg/Aggro`? Let's re-read Map.
        *   Map: `SetData` is called by `boss_thermaplugg/Aggro`, `boss_thermaplugg/JustDied`, `boss_thermaplugg/JustReachedHome`.
        *   Map: `GetBombFaces` is called by `boss_thermaplugg/Aggro`.
        *   Map: `DoActivateBombFace` is called by `boss_thermaplugg/EffectDummyCreature_spell_boss_thermaplugg`.
        *   Map: `DoDeactivateBombFace` is called by `boss_thermaplugg/GOHello_go_gnomeface_button`.
    *   *Correction:* The Map indicates `boss_thermaplugg` AI calls `SetData` to update the instance state (e.g., starting the fight, finishing it). It also calls `GetBombFaces` to inspect the current state of the puzzle. Specific spell effects or button interactions in the Thermaplugg script trigger `DoActivateBombFace` and `DoDeactivateBombFace`.

### Calls Out (This unit calling into External Units)

1.  **`ScriptedInstance` (Base Class):**
    *   `DoRespawnGameObject`: Used to respawn rockets and explosive charges.
    *   `DoUseDoorOrButton`: Used to trigger visual/state changes on bomb faces.
2.  **`Map` / `WorldObject` / `Object` (Engine Classes):**
    *   `GetCreature` / `GetGameObject`: To retrieve pointers to entities by GUID.
    *   `GetDistanceOrder`: To sort explosives relative to cave-ins.
    *   `SetFlag` / `RemoveFlag`: To lock/unlock the final door.
    *   `getLootState`: To check door state.
    *   `ResetDoorOrButton` / `UseDoorOrButton`: To manipulate door states.
    *   `Use`: To trigger explosive charges.
3.  **`Log.Main`:**
    *   `Out`: Used for logging save/load operations (`OUT_SAVE_INST_DATA`, etc.).

## Data Model

This unit does not directly query or modify database tables via SQL statements in its source code. It relies on the `ScriptedInstance` base class methods (`SaveToDB`, `Load`) to persist the `strInstData` string to the `instance` table (typically `instance.data` column). The schema for this table is managed by the core engine, not this script.

## Notable Implementation Details

1.  **Spatial Sorting of Explosives:** The Grubbis logic depends heavily on `sortFromEastToWest` and `GetDistanceOrder`. The sorting is done *once* when the encounter enters `IN_PROGRESS`. If the map geometry or object positions change dynamically, this static sort may become invalid. The code assumes the initial spawn positions are fixed.
2.  **Hardcoded Bomb Face Activation:** In `SetData` for `TYPE_THERMAPLUGG` with `IN_PROGRESS`, `DoActivateBombFace(2)` is called unconditionally. This means the 3rd bomb face (index 2) is always active at the start of the fight. This might be intentional design or a bug if the puzzle is supposed to start clean.
3.  **Door Locking Race Condition:** The comment in `SetData` (`// Doesn't work here, because the flags are to be reseted on next tick in GO::Update`) suggests a known limitation. Setting `GO_FLAG_LOCKED` might not prevent immediate interaction until the next game tick. This could allow players to slip through the door briefly if they act fast enough.
4.  **Stuck State Prevention:** The `Load` method explicitly resets `IN_PROGRESS` states to `NOT_STARTED`. This is a crucial safeguard to ensure that if the server restarts during a fight, the encounter can be restarted cleanly rather than being stuck in a half-finished state.
5.  **Explosive Charge Tracking:** The code maintains two lists for explosives: `m_lExplosiveCharges` (all charges found on load) and `m_luiSpawnedExplosiveChargeGUIDs` (charges currently active in the fight). The former is cleared after sorting. The latter is cleared after use. This separation ensures that only the intended charges are detonated.

## Member Reference

*   **`instance_gnomeregan`**: Constructor. Initializes GUIDs to 0 and calls `Initialize`.
*   **`Initialize`**: Resets encounter states and bomb face arrays to default values.
*   **`OnCreatureCreate`**: Tracks GUIDs for Blastmaster Shortfuse and Alarm-a-Bomb.
*   **`OnObjectCreate`**: Tracks GUIDs for rockets, cave-ins, explosives, final door, and bomb faces.
*   **`~instance_gnomeregan`**: Destructor. Empty.
*   **`Save`**: Returns the serialized encounter data string.
*   **`sortFromEastToWest`**: Static helper to sort GameObjects by Y position.
*   **`SetData`**: Core logic handler. Manages Grubbis (sorting/spawning explosives) and Thermaplugg (door locking/bomb face activation) states. Persists data on `DONE`.
*   **`Load`**: Deserializes encounter data. Resets `IN_PROGRESS` states to `NOT_STARTED`.
*   **`GetData`**: Returns encounter status for Grubbis or Thermaplugg.
*   **`GetData64`**: Returns GUIDs for cave-ins or Alarm-a-Bomb.
*   **`GetBombFaces`**: Returns pointer to the bomb face state array.
*   **`DoActivateBombFace`**: Activates a specific bomb face, setting its timer and state.
*   **`DoDeactivateBombFace`**: Deactivates a specific bomb face, resetting its state.
*   **`GetInstanceData_instance_gnomeregan`**: Factory function to create the instance.
*   **`AddSC_instance_gnomeregan`**: Registers the script with the engine.

---

<!-- machine-true, projected from graph.json -->

## Map — instance_gnomeregan

*Source:* instance_gnomeregan.cpp, gnomeregan.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| instance_gnomeregan | ctor | ScriptedInstance/ScriptedInstance | — | — |
| Initialize | method | — | — | — |
| OnCreatureCreate | method | Object/GetEntry, Object/GetGUID | — | — |
| OnObjectCreate | method | Object/GetEntry, Object/GetGUID | — | — |
| ~instance_gnomeregan | dtor | — | — | — |
| Save | method | — | — | — |
| sortFromEastToWest | function | WorldObject.Object/GetPositionY | — | — |
| SetData | method | GameObject/getLootState, GameObject/ResetDoorOrButton, GameObject/Use, GameObject/UseDoorOrButton, InstanceData/SaveToDB, Log.Main/Out, Map.Main/GetCreature, Map.Main/GetGameObject, Map.Main/GetId, Map.Main/GetInstanceId, Map.Main/GetMapName, Object/GetGUID, ObjectGuid/ObjectGuid#5, ScriptedInstance/DoRespawnGameObject, WorldObject.Object/GetDistanceOrder, WorldObject.Object/RemoveFlag, WorldObject.Object/SetFlag | boss_thermaplugg/Aggro, boss_thermaplugg/JustDied, boss_thermaplugg/JustReachedHome, gnomeregan/JustDied, gnomeregan/StartEvent, gnomeregan/SummonedCreatureJustDied, gnomeregan/UpdateEscortAI | — |
| Load | method | Log.Main/Out, Map.Main/GetId, Map.Main/GetInstanceId, Map.Main/GetMapName | — | — |
| GetData | method | — | gnomeregan/GossipHello_npc_blastmaster_emi_shortfuse, gnomeregan/GossipSelect_npc_blastmaster_emi_shortfuse, gnomeregan/npc_blastmaster_emi_shortfuseAI | — |
| GetData64 | method | — | gnomeregan/JustDied, gnomeregan/JustSummoned, gnomeregan/StartQuest, gnomeregan/UpdateEscortAI, gnomeregan/WaypointReached, gnomeregan/WaypointStart | — |
| GetBombFaces | method | — | boss_thermaplugg/Aggro | — |
| DoActivateBombFace | method | ScriptedInstance/DoUseDoorOrButton | boss_thermaplugg/EffectDummyCreature_spell_boss_thermaplugg | — |
| DoDeactivateBombFace | method | ScriptedInstance/DoUseDoorOrButton | boss_thermaplugg/GOHello_go_gnomeface_button | — |
| GetInstanceData_instance_gnomeregan | function | — | — | — |
| AddSC_instance_gnomeregan | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
