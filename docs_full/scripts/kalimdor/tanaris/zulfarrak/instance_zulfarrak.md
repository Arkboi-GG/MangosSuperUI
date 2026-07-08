# instance_zulfarrak

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# instance_zulfarrak

**Purpose & Responsibilities**  
`instance_zulfarrak` is the `ScriptedInstance` handler for the Zul'Farrak raid instance. It manages instance-wide state, including boss encounter progress, object GUID tracking, and a complex multi-phase event sequence centered on the "Pyramid" area. Specifically, it orchestrates the spawning, movement, and death-checking of troll waves during the Pyramid event, while also handling the persistence of the final door's state to the database.

**Member-by-Member Behavior**  

### Initialization & State Management
*   **`instance_zulfarrak`**: The constructor initializes the `ScriptedInstance` base class with the provided `Map*` and immediately calls `Initialize()` to reset all internal state variables.
*   **`Initialize`**: Resets all encounter states (`GahzRillaEncounter`, `EndDoorEncounter`, etc.) to `NOT_STARTED`, clears all stored creature/gameobject GUIDs, resets the `PyramidPhase` to 0, and zeroes out timers and counters. This ensures a clean slate for a new instance load.
*   **`GetData` / `GetData64`**: Provide read-only access to specific instance states. `GetData` returns the current phase of the Pyramid event or the status of specific bosses (Zumrah, Antusul). `GetData64` returns the `uint64` GUIDs for key NPCs (Ukorz, Zumrah, Bly, Weegli, Oro, Raven, Murta) and the End Door game object, allowing other scripts to interact with these entities.
*   **`SetData`**: Updates instance state. It handles updates for the Pyramid phase, End Door, Zumrah, and Antusul encounters. Crucially, if the `EVENT_END_DOOR` is set to `DONE`, it serializes this state into `strInstData` and calls `SaveToDB()` (from `InstanceData`) to persist the completion status, logging the action via `OUT_SAVE_INST_DATA`.
*   **`Update`**: The core logic loop, driven by the `PyramidPhase` state machine. It manages the timing and progression of the Pyramid event:
    *   `PYRAMID_ARRIVED_AT_STAIR`: Triggers Wave 1, sets a long timer for the next major wave, and initializes minor wave timers.
    *   `PYRAMID_WAVE_1` / `PYRAMID_WAVE_2`: Checks if all spawned adds are dead via `IsWaveAllDead()`. If so, it transitions to the pre-next-wave phase. Otherwise, it uses `minor_wave_Timer` to periodically call `SendAddsUpStairs()` to push existing adds forward.
    *   `PYRAMID_PRE_WAVE_2` / `PYRAMID_PRE_WAVE_3`: Waits for a `major_wave_Timer` to expire before triggering the next wave or moving NPCs.
    *   `PYRAMID_WAVE_3`: After all adds are dead, it moves the five caged NPCs (Bly, Murta, Oro, Raven, Weegli) to their final positions using `MoveNPCIfAlive()` and sets the phase to `PYRAMID_KILLED_ALL_TROLLS`.
*   **`Save` / `Load`**: Handle persistence. `Save` returns the serialized string `strInstData`. `Load` parses this string to restore `EndDoorEncounter`. If the loaded state is invalid or missing, it defaults to `NOT_STARTED`. Logging is performed via `OUT_LOAD_INST_DATA` macros.

### Object Tracking
*   **`OnCreatureCreate`**: Intercepts the creation of creatures within the instance. It stores GUIDs for key NPCs (Zumrah, Bly, Raven, Oro, Weegli, Murta, Ukorz). For `NPC_GAHZRILLA`, it checks if the encounter is already in progress; if so, it forces the creature to die immediately via `DisappearAndDie()` (from `Creature.Main`) to prevent duplicates or stale spawns. Otherwise, it marks the encounter as `IN_PROGRESS`.
*   **`OnObjectCreate`**: Intercepts game object creation. It stores the GUID for `GO_END_DOOR`. If the `EndDoorEncounter` is already `DONE`, it opens the door immediately via `UseDoorOrButton()` (from `GameObject`).

### Pyramid Event Logic
*   **`SpawnPyramidWave`**: Iterates through the static `pyramidSpawns` array. For entries matching the specified `wave` number, it summons the creature at the predefined coordinates using `SummonCreature()` (from `WorldObject.Object`) and adds the new creature's GUID to the `addsAtBase` list for tracking.
*   **`IsWaveAllDead`**: Iterates through both `addsAtBase` and `movedadds` lists. For each GUID, it retrieves the creature via `GetCreature()` (from `Map.Main`) and checks `IsAlive()` (from `Unit.Main`). Returns `true` only if all tracked adds are dead or no longer exist.
*   **`SendAddsUpStairs`**: Moves a specified `count` of alive adds from `addsAtBase` up the stairs. It selects creatures from the front of `addsAtBase`, commands them to move to a randomized position near the top of the stairs using `MovePoint()` (from `Creature.MotionMaster`) and `SetWalk(false)`, then transfers their GUIDs to the `movedadds` list. This simulates adds advancing toward players.
*   **`MoveNPCIfAlive`**: A helper to reposition the five caged NPCs. It retrieves the creature by GUID, checks if it is alive, and if so, commands it to move to specific coordinates using `MovePoint()`, updates its combat start position, and sets its home position. This ensures NPCs follow the event flow even if they were damaged.

### Registration
*   **`GetInstanceData_instance_zulfarak`**: Factory function that creates and returns a new `instance_zulfarrak` instance.
*   **`AddSC_instance_zulfarrak`**: Registers the script with the `ScriptMgr` via `RegisterSelf()`, making it available to the server. It is called by `ScriptLoader/AddScripts`.

**Cross-Unit Boundaries**  
*   **`ScriptedInstance`**: The base class provides the framework for instance data management, including `SaveToDB()` called in `SetData`.
*   **`Creature.Main` / `Unit.Main` / `WorldObject.Object`**: Used extensively for retrieving entities (`GetCreature`, `GetGUID`), checking state (`IsAlive`), and commanding actions (`DisappearAndDie`, `SummonCreature`, `MovePoint`, `SetWalk`, `GetOrientation`).
*   **`Map.Main`**: Provides access to the instance map context (`GetId`, `GetInstanceId`, `GetMapName`) for logging and entity retrieval (`GetCreature`).
*   **`Log.Main`**: Used for outputting save/load status messages (`Out`).
*   **`shared_Util`**: `urand` is used in `SendAddsUpStairs` to randomize the destination X coordinate for moving adds.
*   **`ScriptMgr` / `ScriptLoader`**: Handles the registration lifecycle of the script.

**Data Model**  
This unit does not directly query or manipulate database tables via SQL. It relies on the `InstanceData` base class infrastructure to persist a single string (`strInstData`) containing the `EndDoorEncounter` state. No custom tables are involved.

**Notable Implementation Details**  
*   **Static Spawn Data**: The `pyramidSpawns` array is hardcoded with coordinates and creature entries for three distinct waves. This tight coupling means changes to spawn locations require recompilation.
*   **Two-List Tracking**: The system uses two `std::list<uint64>` containers (`addsAtBase` and `movedadds`) to track adds. `IsWaveAllDead` checks both, ensuring that adds who have moved up the stairs are still considered part of the wave until they die.
*   **Timer Logic**: The `Update` method uses manual timer decrementing (`timer -= diff`). This is a common pattern in this codebase but requires careful handling to avoid negative values or missed ticks if `diff` is large.
*   **Gahz'rilla Duplicate Prevention**: The check in `OnCreatureCreate` for `NPC_GAHZRILLA` prevents multiple instances of the boss from existing if the encounter is already active, forcing immediate deletion of late spawns.
*   **Hardcoded Coordinates**: `SendAddsUpStairs` uses a hardcoded Y/Z coordinate (1274, 42) and a randomized X offset (1880 + urand(0, 10)). This assumes a specific map layout and may break if map geometry changes.
*   **Passive React State Comments**: Several comments in `OnCreatureCreate` indicate that caged NPCs (Bly, Raven, etc.) should start passive, but the code is commented out. This suggests reliance on default creature template settings or external scripts for initial react state.

## Member Reference

*   **`instance_zulfarrak`**: Constructor that initializes the `ScriptedInstance` base and calls `Initialize()`.
*   **`Initialize`**: Resets all encounter states, GUIDs, timers, and phase variables to their default values.
*   **`OnCreatureCreate`**: Stores GUIDs for key NPCs; handles `NPC_GAHZRILLA` duplicate prevention and encounter state setting.
*   **`OnObjectCreate`**: Stores GUID for `GO_END_DOOR`; opens it if the encounter is already done.
*   **`GetData`**: Returns integer state for Pyramid phase, Zumrah, or Antusul encounters.
*   **`GetData64`**: Returns GUIDs for key NPCs and the End Door game object.
*   **`SetData`**: Updates encounter states; persists `EndDoorEncounter` to DB if set to `DONE`.
*   **`Update`**: Drives the Pyramid event state machine, managing wave spawns, add movement, and NPC repositioning based on timers and death checks.
*   **`MoveNPCIfAlive`**: Helper to move caged NPCs to specific coordinates if they are alive.
*   **`SpawnPyramidWave`**: Spawns creatures from the static `pyramidSpawns` array for a given wave number and tracks their GUIDs.
*   **`IsWaveAllDead`**: Checks if all tracked adds (both at base and moved) are dead.
*   **`SendAddsUpStairs`**: Moves a specified number of alive adds from `addsAtBase` to a higher position, transferring them to `movedadds`.
*   **`Save`**: Returns the serialized instance data string.
*   **`Load`**: Parses the saved string to restore `EndDoorEncounter` state.
*   **`GetInstanceData_instance_zulfarak`**: Factory function to create the instance script object.
*   **`AddSC_instance_zulfarrak`**: Registers the script with the `ScriptMgr`.

---

<!-- machine-true, projected from graph.json -->

## Map — instance_zulfarrak

*Source:* instance_zulfarrak.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| instance_zulfarrak | ctor | ScriptedInstance/ScriptedInstance | — | — |
| Initialize | method | — | — | — |
| OnCreatureCreate | method | Creature.Main/DisappearAndDie, Object/GetEntry, Object/GetGUID | — | — |
| OnObjectCreate | method | GameObject/UseDoorOrButton, Object/GetEntry, Object/GetGUID | — | — |
| GetData | method | — | — | — |
| GetData64 | method | — | — | — |
| SetData | method | InstanceData/SaveToDB, Log.Main/Out, Map.Main/GetId, Map.Main/GetInstanceId, Map.Main/GetMapName | — | — |
| Update | method | — | — | — |
| MoveNPCIfAlive | method | Creature.Main/SetCombatStartPosition, Creature.Main/SetHomePosition, Creature.MotionMaster/MovePoint, Map.Main/GetCreature, ObjectGuid/ObjectGuid#5, Unit.Main/GetMotionMaster, Unit.Main/IsAlive, WorldObject.Object/GetOrientation | — | — |
| SpawnPyramidWave | method | Object/GetGUID, WorldObject.Object/SummonCreature | — | — |
| IsWaveAllDead | method | Map.Main/GetCreature, ObjectGuid/ObjectGuid#5, Unit.Main/IsAlive | — | — |
| SendAddsUpStairs | method | Creature.MotionMaster/MovePoint, Map.Main/GetCreature, Object/GetGUID, ObjectGuid/ObjectGuid#5, shared_Util/urand, Unit.Main/GetMotionMaster, Unit.Main/IsAlive, Unit.Main/SetWalk | — | — |
| Save | method | — | — | — |
| Load | method | Log.Main/Out, Map.Main/GetId, Map.Main/GetInstanceId, Map.Main/GetMapName | — | — |
| GetInstanceData_instance_zulfarak | function | — | — | — |
| AddSC_instance_zulfarrak | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
