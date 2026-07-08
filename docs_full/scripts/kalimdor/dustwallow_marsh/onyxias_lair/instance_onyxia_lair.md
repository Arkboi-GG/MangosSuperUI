# instance_onyxia_lair

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`instance_onyxia_lair` is the instance data handler for Onyxia's Lair. It manages the single encounter state (`DATA_ONYXIA_EVENT`) and triggers the initial spawn of whelps when the specific spawner game object (`GO_WHELP_SPAWNER`) is loaded into the map.

## Member-by-Member Behavior

### State Management
*   **`instance_onyxia_lair`**: Constructs the instance data for a `Map`, initializing the base `ScriptedInstance` and calling `Initialize()`.
*   **`Initialize`**: An empty override. The `m_auiEncounter` array relies on default zero-initialization, representing `NOT_STARTED`.
*   **`IsEncounterInProgress`**: Returns `true` if any entry in `m_auiEncounter` is `IN_PROGRESS`. Since `MAX_ENCOUNTER` is 1, it checks only the boss state.
*   **`GetData`**: Returns the current state of `DATA_ONYXIA_EVENT` from `m_auiEncounter[0]`. Returns `0` for unknown identifiers.
*   **`SetData`**: Updates `m_auiEncounter[0]` with the provided `uiData` when `uiType` is `DATA_ONYXIA_EVENT`.

### Event Handling
*   **`OnObjectCreate`**: Triggered by the engine when a `GameObject` is created. If the object's entry is `GO_WHELP_SPAWNER` (176510), it casts `SPELL_SUMMON_WHELP` (17646) at the object's coordinates, bypassing line-of-sight checks (`true` flag).

### Registration
*   **`GetInstanceData_instance_onyxia_lair`**: Factory function returning a new `instance_onyxia_lair` instance for a given `Map`.
*   **`AddSC_instance_onyxia_lair`**: Registers the script with `ScriptMgr` by creating a `Script` object named `"instance_onyxia_lair"` and assigning the factory function. Called by `ScriptLoader::AddScripts`.

## Cross-Unit Boundaries

*   **`ScriptedInstance`**: Base class providing the interface for instance data. `instance_onyxia_lair` inherits from it and overrides `Initialize`, `IsEncounterInProgress`, `GetData`, `SetData`, and `OnObjectCreate`.
*   **`Object` / `WorldObject.Object`**: `OnObjectCreate` calls `GetEntry()` to identify the game object and `GetPositionX/Y/Z()` to determine the spell cast location.
*   **`SpellCaster`**: `OnObjectCreate` calls `CastSpell` on the `GameObject` to summon whelps.
*   **`Script` / `ScriptMgr`**: `AddSC_instance_onyxia_lair` creates a `Script` object and calls `RegisterSelf()` to register it with the global script manager.
*   **`ScriptLoader`**: Calls `AddSC_instance_onyxia_lair` during server startup to load the script.

## Data Model

This unit does not interact with any database tables. All state is held in memory for the lifetime of the instance map.

## Notable Implementation Details

*   **Implicit Initialization**: `Initialize()` is empty. The `m_auiEncounter` array is zero-initialized by default, which corresponds to `NOT_STARTED` (0). This relies on the convention that `IN_PROGRESS` is non-zero.
*   **Hardcoded IDs**: The script uses hardcoded IDs for `GO_WHELP_SPAWNER` (176510) and `SPELL_SUMMON_WHELP` (17646). Changes to these in the database require recompilation.
*   **Single Encounter**: `MAX_ENCOUNTER` is 1, limiting the instance to tracking only the main boss state.

## Member Reference

*   **`instance_onyxia_lair`**: Constructor initializing the instance data and calling `Initialize()`.
*   **`Initialize`**: Empty override; relies on default zero-initialization of `m_auiEncounter`.
*   **`IsEncounterInProgress`**: Returns `true` if `m_auiEncounter[0]` is `IN_PROGRESS`.
*   **`GetData`**: Returns the state of `DATA_ONYXIA_EVENT` from `m_auiEncounter[0]`.
*   **`SetData`**: Sets `m_auiEncounter[0]` for `DATA_ONYXIA_EVENT`.
*   **`OnObjectCreate`**: Casts `SPELL_SUMMON_WHELP` at the position of `GO_WHELP_SPAWNER` if created.
*   **`GetInstanceData_instance_onyxia_lair`**: Factory function creating a new `instance_onyxia_lair` instance.
*   **`AddSC_instance_onyxia_lair`**: Registers the script with `ScriptMgr`.

---

<!-- machine-true, projected from graph.json -->

## Map — instance_onyxia_lair

*Source:* instance_onyxia_lair.cpp, instance_onyxia_lair.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| instance_onyxia_lair | ctor | ScriptedInstance/ScriptedInstance | — | — |
| Initialize | method | — | — | — |
| IsEncounterInProgress | method | — | — | — |
| GetData | method | — | — | — |
| SetData | method | — | — | — |
| OnObjectCreate | method | Object/GetEntry, SpellCaster/CastSpell#4, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ | — | — |
| GetInstanceData_instance_onyxia_lair | function | — | — | — |
| AddSC_instance_onyxia_lair | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
