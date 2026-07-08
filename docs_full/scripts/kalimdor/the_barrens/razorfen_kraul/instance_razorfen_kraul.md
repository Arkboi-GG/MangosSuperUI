<!-- provenance: verbose -->
# instance_razorfen_kraul

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# instance_razorfen_kraul

**Purpose & Responsibilities**  
`instance_razorfen_kraul` is the `ScriptedInstance` handler for the **Razorfen Kraul** dungeon, managing the **Agathelos the Rager** encounter (`TYPE_AGATHELOS`). It tracks **Ward Keeper** (`NPC_WARD_KEEPER`, 4625) deaths to trigger the next phase: activating the **Agathelos Ward** (`GO_AGATHELOS_WARD`, 21099) and initiating **Agathelos**’s (`NPC_AGATHELOS`, 4422) waypoint movement. It persists the single encounter state to the database.

## Member-by-Member Behavior

**~instance_razorfen_kraul**  
Trivial destructor.

**instance_razorfen_kraul**  
Constructor. Initializes `ScriptedInstance`, sets `m_uiWardKeepersRemaining` to `0`, and calls `Initialize()`.

**Initialize**  
Zeros `m_auiEncounter` via `memset`.

**OnObjectCreate**  
For `GO_AGATHELOS_WARD`: stores GUID in `m_uiAgathelosWardGUID`; if `m_auiEncounter[0] == DONE`, sets state to `GO_STATE_ACTIVE`.

**OnCreatureCreate**  
For `NPC_WARD_KEEPER`: increments `m_uiWardKeepersRemaining`. For `NPC_AGATHELOS`: stores GUID in `m_uiAgathelosGUID`.

**SetData**  
When `uiType == TYPE_AGATHELOS`: decrements `m_uiWardKeepersRemaining`. If zero: sets `m_auiEncounter[0] = uiData`, activates ward via `DoUseDoorOrButton`, and if Agathelos exists, sets `SetWalk(false)`, `SetDefaultMovementType(WAYPOINT_MOTION_TYPE)`, and `MoveWaypoint()`. If `uiData == DONE`: serializes `m_auiEncounter[0]` to `m_strInstData`, calls `SaveToDB()`, and logs.

**Save**  
Returns `m_strInstData.c_str()`.

**Load**  
Deserializes `m_auiEncounter[0]` from `chrIn`. Resets any `IN_PROGRESS` states to `NOT_STARTED`. Logs status.

**GetData**  
Returns `m_auiEncounter[0]` for `TYPE_AGATHELOS`; else `0`.

**GetInstanceData_instance_razorfen_kraul**  
Factory function creating `instance_razorfen_kraul` for a `Map*`.

**AddSC_instance_razorfen_kraul**  
Registers the script with `ScriptMgr` via `Script` object and `RegisterSelf()`. Called by `ScriptLoader/AddScripts`.

## Cross-Unit Boundaries

| Member | Direction | Other Unit | Purpose |
|--------|-----------|------------|---------|
| `instance_razorfen_kraul` (ctor) | Calls | `ScriptedInstance` | Base initialization. |
| `OnObjectCreate` | Calls | `GameObject/SetGoState`, `Object/GetEntry`, `Object/GetObjectGuid` | Activate ward, get GUIDs. |
| `OnCreatureCreate` | Calls | `Object/GetEntry`, `Object/GetGUID` | Identify NPCs. |
| `SetData` | Calls | `Creature.Main/SetDefaultMovementType`, `Creature.MotionMaster/MoveWaypoint`, `Unit.Main/SetWalk`, `Unit.Main/GetMotionMaster` | Control Agathelos movement. |
| `SetData` | Calls | `Map.Main/GetCreature` | Retrieve Agathelos pointer. |
| `SetData` | Calls | `ScriptedInstance/DoUseDoorOrButton` | Activate ward. |
| `SetData` | Calls | `InstanceData/SaveToDB` | Persist state. |
| `SetData` | Calls | `Log.Main/Out`, `Map.Main/GetId`, etc. | Logging. |
| `Load` | Calls | `Log.Main/Out`, `Map.Main/GetId`, etc. | Logging. |
| `AddSC_instance_razorfen_kraul` | Calls | `Script/Script`, `ScriptMgr/RegisterSelf` | Register script. |
| `AddSC_instance_razorfen_kraul` | Called by | `ScriptLoader/AddScripts` | Startup registration. |

## Data Model

No direct table queries. Relies on `ScriptedInstance` base class for persistence via `SaveToDB()` and `Load()`, interacting with the `instance_data` table. Serialized format: string containing `m_auiEncounter[0]`.

## Notable Implementation Details

1. **Ward Keeper Counter:** `m_uiWardKeepersRemaining` increments on `NPC_WARD_KEEPER` spawn (`OnCreatureCreate`) and decrements on `SetData(TYPE_AGATHELOS)`. Assumes `SetData` is called exactly once per death; missed calls break activation.
2. **Agathelos Movement:** Waypoint movement starts only after ward activation. `SetWalk(false)` ensures smooth transition to `WAYPOINT_MOTION_TYPE`.
3. **State Reset on Load:** `Load()` resets `IN_PROGRESS` to `NOT_STARTED`, preventing mid-fight joins but resetting encounters on server restart.
4. **GUID Stability:** GUIDs stored in members are valid for instance lifecycle; stale if objects respawn outside standard flow.

## Member Reference

**~instance_razorfen_kraul**  
Trivial destructor.

**instance_razorfen_kraul**  
Constructor. Initializes base, `m_uiWardKeepersRemaining=0`, calls `Initialize()`.

**Initialize**  
Zeros `m_auiEncounter`.

**Save**  
Returns `m_strInstData.c_str()`.

**OnObjectCreate**  
Handles `GO_AGATHELOS_WARD`: stores GUID, activates if done.

**OnCreatureCreate**  
Increments `m_uiWardKeepersRemaining` for `NPC_WARD_KEEPER`; stores `NPC_AGATHELOS` GUID.

**SetData**  
Decrements ward keeper count; if zero, activates ward, moves Agathelos, persists if done.

**Load**  
Deserializes state, resets `IN_PROGRESS` to `NOT_STARTED`.

**GetData**  
Returns `m_auiEncounter[0]` for `TYPE_AGATHELOS`.

**GetInstanceData_instance_razorfen_kraul**  
Factory for instance data.

**AddSC_instance_razorfen_kraul**  
Registers script with engine.

---

<!-- machine-true, projected from graph.json -->

## Map — instance_razorfen_kraul

*Source:* instance_razorfen_kraul.cpp, razorfen_kraul.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ~instance_razorfen_kraul | dtor | — | — | — |
| instance_razorfen_kraul | ctor | ScriptedInstance/ScriptedInstance | — | — |
| Initialize | method | — | — | — |
| Save | method | — | — | — |
| OnObjectCreate | method | GameObject/SetGoState, Object/GetEntry, Object/GetObjectGuid | — | — |
| OnCreatureCreate | method | Object/GetEntry, Object/GetGUID | — | — |
| SetData | method | Creature.Main/SetDefaultMovementType, Creature.MotionMaster/MoveWaypoint, InstanceData/SaveToDB, Log.Main/Out, Map.Main/GetCreature, Map.Main/GetId, Map.Main/GetInstanceId, Map.Main/GetMapName, ObjectGuid/ObjectGuid#5, ScriptedInstance/DoUseDoorOrButton, Unit.Main/GetMotionMaster, Unit.Main/SetWalk, ZoneScript/GetMap#2 | — | — |
| Load | method | Log.Main/Out, Map.Main/GetId, Map.Main/GetInstanceId, Map.Main/GetMapName | — | — |
| GetData | method | — | — | — |
| GetInstanceData_instance_razorfen_kraul | function | — | — | — |
| AddSC_instance_razorfen_kraul | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
