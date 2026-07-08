# ScriptedInstance_PTR

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ScriptedInstance_PTR

## Purpose & Responsibilities

`ScriptedInstance_PTR` is a minimal subclass of `ScriptedInstance` intended to provide specific lifecycle hooks and state tracking for instance scripts, particularly those requiring boss expiration tracking or combat entry handling. It inherits the full suite of utility methods from `ScriptedInstance` (door control, object storage, save/load data generation) but adds two overridden virtual methods (`Update`, `OnCreatureEnterCombat`) and a protected member (`boss_expirations`) to support time-sensitive boss mechanics.

## Member-by-Member Behavior

### Construction
**`ScriptedInstance_PTR`**
The constructor accepts a `Map*` and forwards it to the `ScriptedInstance` base class constructor. It performs no additional initialization.

## Cross-Unit Boundaries

*   **Calls Out:** None. The constructor only invokes the parent `ScriptedInstance` constructor.
*   **Called By:** The server core (via `Map` or `InstanceManager`) instantiates this class (or derived classes) when loading a map that requires this specific instance script type. The core subsequently calls the overridden `Update` and `OnCreatureEnterCombat` methods.

## Data Model

`ScriptedInstance_PTR` does not directly interact with database tables. It relies on the string-based save/load mechanisms inherited from `ScriptedInstance` to persist instance state (such as encounter progress) to the `instance` table's `data` column.

## Notable Implementation Details

1.  **Thin Abstraction:** This class adds only two method overrides and one member variable to `ScriptedInstance`. Its primary value is providing a dedicated place for derived scripts to implement `Update` and `OnCreatureEnterCombat` logic while accessing the `boss_expirations` map.
2.  **Boss Expirations:** The `boss_expirations` map (`std::map<ObjectGuid, time_t>`) is protected, meaning derived classes must manage it directly. The comment "For PTR testes" suggests it may originate from testing scenarios or specific Public Test Realm mechanics, but it remains part of the class interface.
3.  **Empty Overrides:** In this partial, `Update` and `OnCreatureEnterCombat` are declared but not defined. Derived classes must implement them to utilize these hooks.

## Member Reference

**`ScriptedInstance_PTR`**
Constructor that initializes the `ScriptedInstance` base class with the provided `Map*`.

---

<!-- machine-true, projected from graph.json -->

## Map — ScriptedInstance_PTR

*Source:* ScriptedInstance.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ScriptedInstance_PTR | ctor | — | — | — |
