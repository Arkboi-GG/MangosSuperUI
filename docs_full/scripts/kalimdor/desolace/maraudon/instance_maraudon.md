<!-- provenance: verbose -->
# instance_maraudon

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# instance_maraudon

**Purpose & Responsibilities**  
`instance_maraudon` is the `ScriptedInstance` handler for the Maraudon dungeon, managing state for the **Celebras** (`TYPE_CELEBRAS`) and **Larva Spewer** (`TYPE_LARVA_SPEWER`) encounters. It tracks creature/game object GUIDs, enforces visibility rules (hiding `NPC_CELEBRAS_REDEEMED` until completion), coordinates the Larva Spewer’s animation with a delayed larva respawn, cleans up corrupted vines, and persists progress via `ScriptedInstance` serialization.

---

## Member-by-Member Behavior

### Initialization & State Management

**`instance_maraudon` (ctor)**  
Delegates to `ScriptedInstance`; initializes `cGuid` to 0.

**`Initialize`**  
Resets `m_auiEncounter` to zero, clears GUIDs (`cGuid`, `spewedLarvaGuid`, `vineGuid`, `larvaSpewerGuid`), sets `bRespawnSpewedLarva` to false, and sets `uiSpewedLarvaTimer` to 4000 ms.

**`Save`**  
Returns `strInstData.c_str()` (space-separated encounter states) for persistence.

**`Load`**  
Parses saved string into `m_auiEncounter`. Forces `IN_PROGRESS` states to `NOT_STARTED` to prevent inconsistency on reload.

**`GetData`**  
Returns state for `TYPE_LARVA_SPEWER` or `TYPE_CELEBRAS`; 0 otherwise.

**`GetData64`**  
Returns GUID of `NPC_CELEBRAS_REDEEMED` if queried; 0 otherwise.

### Creature & Game Object Lifecycle Hooks

**`OnCreatureCreate`**  
- `NPC_CELEBRAS_REDEEMED`: Stores GUID in `cGuid`. Hides (`VISIBILITY_OFF`) if `TYPE_CELEBRAS` is not `DONE`.  
- `NPC_SPEWED_LARVA`: Stores GUID in `spewedLarvaGuid`. Kills via `DisappearAndDie()` if `TYPE_LARVA_SPEWER` is `DONE`.

**`OnGameObjectCreate`**  
- `GO_HEALED_CELEBRIAN_VINE`: Stores GUID in `vineGuid`.  
- `GO_LARVA_SPEWER`: Stores GUID in `larvaSpewerGuid`. Sets state to `GO_STATE_ACTIVE_ALTERNATIVE` if `TYPE_LARVA_SPEWER` is `DONE`.

**`OnCreatureRespawn`**  
- `NPC_SPEWED_LARVA`: Kills via `DisappearAndDie()` if `TYPE_LARVA_SPEWER` is `DONE`, preventing post-encounter respawns.

### Encounter Progression & Side Effects

**`SetData`**  
- `TYPE_LARVA_SPEWER`: If `IN_PROGRESS`, kills existing larva via `DisappearAndDie()` and calls `SpewLarva()`. Else updates state.  
- `TYPE_CELEBRAS`: If `DONE`, reveals `NPC_CELEBRAS_REDEEMED` via `SetVisibility(VISIBILITY_ON)`. Updates state.  
If new state is `DONE`, serializes `m_auiEncounter` to `strInstData` and calls `SaveToDB()`.

**`SpewLarva`**  
Retrieves `GO_LARVA_SPEWER`. If `GO_STATE_READY`, triggers `SendGameObjectCustomAnim()`, sets `bRespawnSpewedLarva = true`, and resets `uiSpewedLarvaTimer` to 4000 ms.

**`Update`**  
1. **Vine Cleanup**: If `vineGuid` is set, finds closest `GO_VYLESTEM_VINE` within `INTERACTION_DISTANCE` and marks it for removal via `AddObjectToRemoveList()`. Clears `vineGuid`.  
2. **Larva Respawn**: If `bRespawnSpewedLarva` is true, decrements `uiSpewedLarvaTimer`. On expiry, respawns `NPC_SPEWED_LARVA` via `Respawn()` and resets flag.

### Framework Integration

**`GetInstanceData_instance_maraudon`**  
Factory function creating a new `instance_maraudon` instance for a given `Map*`.

**`AddSC_instance_maraudon`**  
Registers the script with the engine. Called by `ScriptLoader/AddScripts` during startup.

---

## Cross-Unit Boundaries

| Member | Direction | Collaborating Unit | Interaction Details |
|--------|-----------|--------------------|---------------------|
| `instance_maraudon` (ctor) | Calls out | `ScriptedInstance` | Inherits base instance functionality. |
| `OnCreatureCreate` | Calls out | `Creature.Main/DisappearAndDie`, `Object/GetEntry`, `Object/GetObjectGuid`, `Unit.Main/SetVisibility` | Hides/kills creatures based on encounter state. |
| `OnGameObjectCreate` | Calls out | `GameObject/SetGoState`, `Object/GetEntry`, `Object/GetObjectGuid` | Sets game object states and stores GUIDs. |
| `OnCreatureRespawn` | Calls out | `Creature.Main/DisappearAndDie`, `Object/GetEntry` | Prevents larva from respawning post-encounter. |
| `SetData` | Calls out | `Creature.Main/DisappearAndDie`, `InstanceData/SaveToDB`, `Log.Main/Out`, `Map.Main/GetCreature`, `Map.Main/GetId`, `Map.Main/GetInstanceId`, `Map.Main/GetMapName`, `ObjectGuid/ObjectGuid#5`, `Unit.Main/SetVisibility` | Kills larva, reveals Celebras, saves state, logs operations. |
| `SpewLarva` | Calls out | `GameObject/GetGoState`, `GameObject/SendGameObjectCustomAnim`, `Map.Main/GetGameObject`, `ObjectGuid/ObjectGuid#5` | Triggers animation and schedules respawn. |
| `Update` | Calls out | `Creature.Main/Respawn`, `GridSearchers/GetClosestGameObjectWithEntry`, `Map.Main/GetCreature`, `ObjectGuid/ObjectGuid#5`, `WorldObject.Object/AddObjectToRemoveList`, `ZoneScript/GetGameObject` | Removes corrupted vines, respawns larva after delay. |
| `AddSC_instance_maraudon` | Called by | `ScriptLoader/AddScripts` | Registers the script during server initialization. |

---

## Data Model

This unit does not directly interact with database tables. It relies on `ScriptedInstance`’s built-in persistence, storing encounter states as a space-separated string in the `instance` table’s `data` column. No SQL queries are executed within this unit.

---

## Notable Implementation Details

1. **Delayed Larva Respawn**: `SpewLarva()` triggers an animation and sets a 4-second timer. `Update()` handles the actual respawn, ensuring visual synchronization.
2. **Vine Cleanup**: `Update()` manually removes `GO_VYLESTEM_VINE` near `GO_HEALED_CELEBRIAN_VINE` because the engine does not auto-despawn it.
3. **State Reset on Load**: `Load()` forces `IN_PROGRESS` states to `NOT_STARTED` to handle server crashes gracefully.
4. **GUID Volatility**: GUIDs are not persisted; they are re-established via `OnCreatureCreate`/`OnGameObjectCreate` on load.

---

## Member Reference

**`instance_maraudon`**  
Constructor initializing `cGuid` to 0 and inheriting from `ScriptedInstance`.

**`Initialize`**  
Resets encounter states, GUIDs, and timers to initial values.

**`OnCreatureCreate`**  
Stores GUIDs for key creatures and enforces visibility/death rules based on encounter state.

**`OnGameObjectCreate`**  
Stores GUIDs for key game objects and sets their state if the associated encounter is complete.

**`OnCreatureRespawn`**  
Prevents the spewed larva from respawning if the Larva Spewer encounter is already done.

**`Save`**  
Returns the serialized encounter state string for database persistence.

**`Load`**  
Deserializes the encounter state string, resetting any `IN_PROGRESS` states to `NOT_STARTED`.

**`GetData`**  
Returns the current state of the Larva Spewer or Celebras encounter.

**`SetData`**  
Updates encounter state, triggers side effects (killing larva, revealing Celebras), and persists changes if the state is `DONE`.

**`GetData64`**  
Returns the GUID of `NPC_CELEBRAS_REDEEMED` when queried.

**`SpewLarva`**  
Triggers the Larva Spewer’s animation and schedules a delayed larva respawn.

**`Update`**  
Handles periodic tasks: removing corrupted vines near healed vines and respawning the larva after a 4-second delay.

**`GetInstanceData_instance_maraudon`**  
Factory function that creates a new `instance_maraudon` instance for a given map.

**`AddSC_instance_maraudon`**  
Registers the script with the engine during server startup.

---

<!-- machine-true, projected from graph.json -->

## Map — instance_maraudon

*Source:* instance_maraudon.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| instance_maraudon | ctor | ScriptedInstance/ScriptedInstance | — | — |
| Initialize | method | — | — | — |
| OnCreatureCreate | method | Creature.Main/DisappearAndDie, Object/GetEntry, Object/GetObjectGuid, Unit.Main/SetVisibility | — | — |
| OnGameObjectCreate | method | GameObject/SetGoState, Object/GetEntry, Object/GetObjectGuid | — | — |
| OnCreatureRespawn | method | Creature.Main/DisappearAndDie, Object/GetEntry | — | — |
| Save | method | — | — | — |
| Load | method | — | — | — |
| GetData | method | — | — | — |
| SetData | method | Creature.Main/DisappearAndDie, InstanceData/SaveToDB, Log.Main/Out, Map.Main/GetCreature, Map.Main/GetId, Map.Main/GetInstanceId, Map.Main/GetMapName, ObjectGuid/ObjectGuid#5, Unit.Main/SetVisibility | — | — |
| GetData64 | method | — | — | — |
| SpewLarva | method | GameObject/GetGoState, GameObject/SendGameObjectCustomAnim, Map.Main/GetGameObject, ObjectGuid/ObjectGuid#5 | — | — |
| Update | method | Creature.Main/Respawn, GridSearchers/GetClosestGameObjectWithEntry, Map.Main/GetCreature, ObjectGuid/ObjectGuid#5, WorldObject.Object/AddObjectToRemoveList, ZoneScript/GetGameObject | — | — |
| GetInstanceData_instance_maraudon | function | — | — | — |
| AddSC_instance_maraudon | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
