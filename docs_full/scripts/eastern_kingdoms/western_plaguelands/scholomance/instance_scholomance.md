<!-- provenance: verbose -->
# instance_scholomance

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# instance_scholomance

**Purpose & Responsibilities**  
`instance_scholomance` implements the instance script for the Scholomance dungeon. It tracks boss encounter states, manages door states (opening/closing gates), handles special summoning logic (Gandling), and persists instance data. It also defines two game object scripts: `go_brazier_herald` (summons Kirtonos the Herald) and `go_viewing_room_door` (marks the viewing room door as opened).

## Member-by-Member Behavior

### Core Instance Logic

#### **instance_scholomance** (ctor)
Initializes the instance script by calling `Initialize()`. Inherits from `ScriptedInstance`.

#### **Initialize**
Resets all encounter states (`m_auiEncounter`) to zero and clears all GUIDs for bosses and doors.

#### **OnCreatureCreate**
Records GUIDs for `NPC_VECTUS` and `NPC_MARDUKE`.

#### **OnGameObjectCreate**
Records GUIDs for gates (`GO_GATE_*`), the brazier (`GO_BRAZIER_KIRTONOS`), and the viewing room door (`GO_VIEWING_ROOM_DOOR`). If the viewing room door’s encounter is already `DONE`, it activates the door immediately.

#### **GetData**
Returns encounter state. For `TYPE_GANDLING`, if state is `NOT_STARTED` and all six professors are `DONE`, it sets Gandling’s state to `SPECIAL` before returning.

#### **GetData64**
Returns GUIDs for `DATA_VECTUS` or `DATA_MARDUKE`.

#### **OnCreatureDeath**
Opens Kirtonos’ gate if closed upon his death.

#### **SetData**
Updates encounter states and triggers side effects:
- **Gandling**: If `FAIL` or `DONE`, opens all six professor gates if closed.
- **Kirtonos**: If `IN_PROGRESS`, opens his gate. If `FAIL`, closes his gate and resets the brazier.
- **Others**: Updates states for professors, viewing room door, and Darkreaver.
- **Persistence**: If any encounter reaches `DONE`, serializes states to `strInstData`, saves to DB, and logs completion.
- Calls `SummonGandlingIfPossible()`.

#### **Save**
Returns serialized encounter data string.

#### **Load**
Deserializes encounter data, resetting `IN_PROGRESS` states to `NOT_STARTED` to prevent stuck encounters. Calls `SummonGandlingIfPossible()` and logs completion.

#### **SummonGandlingIfPossible**
If Gandling’s state is `SPECIAL`, summons him at fixed coordinates and sets state to `IN_PROGRESS`.

### Game Object Scripts

#### **GOOpen_brazier_herald**
Prevents Kirtonos summoning if he is `IN_PROGRESS` or `DONE`. Otherwise, sets state to `IN_PROGRESS`, plays a sound, and summons Kirtonos.

#### **go_viewing_room_door** (ctor)
Initializes the AI for the viewing room door.

#### **OnUse** (in `go_viewing_room_door`)
Marks the viewing room door encounter as `DONE` in instance data. Returns `false`.

#### **GOGetAI_go_viewing_room_door**
Factory function creating `go_viewing_room_door` AI.

### Registration

#### **GetInstanceData_instance_scholomance**
Factory function creating `instance_scholomance` instances.

#### **AddSC_instance_scholomance**
Registers `instance_scholomance`, `go_brazier_herald`, and `go_viewing_room_door` scripts.

## Cross-Unit Boundaries

### Calls Out
- **`ScriptedInstance`**: Provides `DoUseDoorOrButton()`, `SaveToDB()`.
- **`Object`**: Retrieves entries/GUIDs.
- **`GameObject`**: Checks/activates/resets doors.
- **`Map.Main`**: Retrieves game objects/map info.
- **`WorldObject.Object`**: Summons creatures/plays sounds.
- **`Log.Main`**: Logs save/load.
- **`InstanceData`**: Accesses instance data.
- **`Script`/`ScriptMgr`**: Registers scripts.

### Called By
- **`ScriptLoader.AddScripts`**: Calls `AddSC_instance_scholomance()`.

## Data Model

No direct database table interaction. Persistence is handled by `ScriptedInstance` via `SaveToDB()` using serialized `strInstData`.

## Notable Implementation Details

1. **Gandling’s Special State**: Dynamically computed in `GetData()` if all professors are defeated. `SummonGandlingIfPossible()` is called after every `SetData()`.
2. **Door State Management**: Doors open/close based on encounter states. Viewing room door pre-opens if already `DONE`.
3. **Persistence Edge Case**: `Load()` resets `IN_PROGRESS` states to `NOT_STARTED`.
4. **Brazier Interaction Guard**: `GOOpen_brazier_herald()` prevents duplicate Kirtonos summons.
5. **Hardcoded Coordinates**: Summoning positions are hardcoded floats.

## Member Reference

- **instance_scholomance**: Constructor; initializes the instance script.
- **Initialize**: Resets encounter states and GUIDs.
- **OnCreatureCreate**: Records GUIDs for Vectus and Marduke.
- **OnGameObjectCreate**: Records GUIDs for doors/braziers; activates viewing room door if already done.
- **GetData**: Returns encounter state; computes Gandling’s special state.
- **GetData64**: Returns GUIDs for Vectus/Marduke.
- **OnCreatureDeath**: Opens Kirtonos’ gate on death.
- **SetData**: Updates encounter states; triggers door actions; persists data; summons Gandling if possible.
- **Save**: Serializes encounter data.
- **Load**: Deserializes encounter data; resets IN_PROGRESS states; summons Gandling if possible.
- **SummonGandlingIfPossible**: Summons Gandling if all professors are defeated.
- **GetInstanceData_instance_scholomance**: Factory for instance script.
- **GOOpen_brazier_herald**: Handles brazier interaction; summons Kirtonos.
- **go_viewing_room_door**: Constructor for viewing room door AI.
- **OnUse** (in `go_viewing_room_door`): Marks viewing room door as done.
- **GOGetAI_go_viewing_room_door**: Factory for viewing room door AI.
- **AddSC_instance_scholomance**: Registers all scripts with the server.

---

<!-- machine-true, projected from graph.json -->

## Map — instance_scholomance

*Source:* instance_scholomance.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| instance_scholomance | ctor | ScriptedInstance/ScriptedInstance | — | — |
| Initialize | method | — | — | — |
| OnCreatureCreate | method | Object/GetEntry, Object/GetGUID | — | — |
| OnGameObjectCreate | method | GameObject/UseDoorOrButton, Object/GetEntry, Object/GetGUID | — | — |
| GetData | method | — | — | — |
| GetData64 | method | — | — | — |
| OnCreatureDeath | method | GameObject/GetGoState, Map.Main/GetGameObject, Object/GetEntry, ObjectGuid/ObjectGuid#5, ScriptedInstance/DoUseDoorOrButton | — | — |
| SetData | method | GameObject/GetGoState, GameObject/ResetDoorOrButton, InstanceData/SaveToDB, Log.Main/Out, Map.Main/GetGameObject, Map.Main/GetId, Map.Main/GetInstanceId, Map.Main/GetMapName, ObjectGuid/ObjectGuid#5, ScriptedInstance/DoUseDoorOrButton | — | — |
| Save | method | — | — | — |
| Load | method | Log.Main/Out, Map.Main/GetId, Map.Main/GetInstanceId, Map.Main/GetMapName | — | — |
| SummonGandlingIfPossible | method | WorldObject.Object/SummonCreature | — | — |
| GetInstanceData_instance_scholomance | function | — | — | — |
| GOOpen_brazier_herald | function | InstanceData/GetData, InstanceData/SetData, WorldObject.Object/GetInstanceData, WorldObject.Object/PlayDirectSound, WorldObject.Object/SummonCreature#2 | — | — |
| go_viewing_room_door | ctor | GameObjectAI/GameObjectAI | — | — |
| OnUse | method | InstanceData/SetData, WorldObject.Object/GetInstanceData | — | — |
| GOGetAI_go_viewing_room_door | function | — | — | — |
| AddSC_instance_scholomance | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
