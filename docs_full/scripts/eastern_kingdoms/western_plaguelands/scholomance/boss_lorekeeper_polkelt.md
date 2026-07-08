# boss_lorekeeper_polkelt

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`boss_lorekeeper_polkelt.cpp` implements the combat AI for **Lorekeeper Polkelt**, a boss in the **Scholomance** dungeon. The unit defines `boss_lorekeeperpolkeltAI`, a `ScriptedAI` subclass that manages the boss’s spell rotation, aggro behavior, and death event. It handles three timed spells (*Volatile Infection*, *Corrosive Acid*, *Noxious Catalyst*), applies an aura on aggro, and notifies the instance script upon death. The unit also provides the factory and registration functions required by the server’s script system.

## Member-by-Member Behavior

### Initialization and Lifecycle

*   **`boss_lorekeeperpolkeltAI` (Constructor):** Initializes the AI by calling the base `ScriptedAI` constructor and immediately invoking `Reset()` to set initial timer values.
*   **`Reset`:** Sets initial cooldowns for the three main spells: *Volatile Infection* (38s), *Corrosive Acid* (45s), and *Noxious Catalyst* (35s). The member variable `Darkplague_Timer` is declared but unused.
*   **`JustDied`:** Retrieves the instance data via `WorldObject::GetInstanceData`, casts it to `ScriptedInstance`, and calls `SetData(TYPE_POLKELT, DONE)` to mark the boss as defeated in the dungeon state.

### Combat Logic

*   **`Aggro`:** Casts `SPELL_DARKPLAGUE_AURA` on the creature itself using `CF_TRIGGERED | CF_AURA_NOT_PRESENT` to apply the aura silently if not already present.
*   **`UpdateAI`:** The main loop, executed every tick with a time delta (`diff`).
    1.  Returns early if no hostile target or victim exists.
    2.  Checks three independent timers. If a timer expires, it casts the corresponding spell on the current victim and resets the timer to a fixed duration (32s for *Volatile Infection*, 25s for *Corrosive Acid*, 38s for *Noxious Catalyst*).
    3.  Calls `DoMeleeAttackIfReady` to perform physical attacks.

### Registration

*   **`GetAI_boss_lorekeeperpolkelt`:** Factory function returning a new `boss_lorekeeperpolkeltAI` instance for a given `Creature`.
*   **`AddSC_boss_lorekeeperpolkelt`:** Registers the script with the `ScriptMgr`. It creates a `Script` object named `"boss_lorekeeper_polkelt"`, links the `GetAI` factory, and calls `RegisterSelf`.

## Cross-Unit Boundaries

*   **`ScriptedAI` / `CreatureAI`:** Inherits from `ScriptedAI` and uses `DoCastSpellIfCan` and `DoMeleeAttackIfReady` (from `CreatureAI`) to delegate spell casting and melee attacks to the core engine.
*   **`Unit.Main`:** `UpdateAI` calls `GetVictim` and `SelectHostileTarget` on `m_creature` to determine the current combat target.
*   **`InstanceData` / `WorldObject`:** `JustDied` calls `WorldObject::GetInstanceData` to get the dungeon context and `ScriptedInstance::SetData` to update progress.
*   **`ScriptMgr` / `Script`:** `AddSC_boss_lorekeeperpolkelt` constructs a `Script` object and registers it via `ScriptMgr::RegisterSelf`. `ScriptLoader::AddScripts` calls this function at startup.

## Data Model

This unit does not interact with any database tables. All state is managed in memory via creature properties and instance data objects.

## Notable Implementation Details

1.  **Unused Timer:** `Darkplague_Timer` is declared but never used; the aura is handled via a conditional cast in `Aggro`.
2.  **Fixed Timers:** Timers reset to fixed values after each cast, regardless of success or failure. This ensures a strict rotation schedule but may result in wasted casts if the target is invalid or immune.
3.  **Initial vs. Subsequent Cooldowns:** Initial timers in `Reset` (38s, 45s, 35s) differ from post-cast resets (32s, 25s, 38s), delaying the first cast of each ability relative to subsequent ones.
4.  **Unsafe Cast:** `JustDied` uses a C-style cast to `ScriptedInstance*`. This assumes the creature is always in a valid Scholomance instance; otherwise, it risks undefined behavior.

## Member Reference

*   **`boss_lorekeeperpolkeltAI`**: Constructor initializing the AI by calling the base `ScriptedAI` constructor and invoking `Reset()` to set initial timer values.
*   **`Reset`**: Method initializing internal timers for *Volatile Infection* (38000ms), *Corrosive Acid* (45000ms), and *Noxious Catalyst* (35000ms); `Darkplague_Timer` is declared but not initialized.
*   **`Aggro`**: Method triggered on combat start that casts `SPELL_DARKPLAGUE_AURA` on the creature itself, using flags to ensure it is treated as a triggered effect and only applied if not already present.
*   **`JustDied`**: Method triggered on death that retrieves the instance data from the creature and calls `SetData` on the `ScriptedInstance` to mark the boss as defeated (`TYPE_POLKELT`, `DONE`).
*   **`UpdateAI`**: Core loop method checking for a valid target, managing three independent spell timers (*Volatile Infection*, *Corrosive Acid*, *Noxious Catalyst*) by casting them on the victim when expired and resetting their durations, and finally attempting a melee attack if ready.
*   **`GetAI_boss_lorekeeperpolkelt`**: Factory function creating and returning a new `boss_lorekeeperpolkeltAI` instance for a given `Creature`.
*   **`AddSC_boss_lorekeeperpolkelt`**: Registration function creating a `Script` object, assigning the name and AI getter, and registering it with the `ScriptMgr` via `RegisterSelf`.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_lorekeeper_polkelt

*Source:* boss_lorekeeper_polkelt.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_lorekeeperpolkeltAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset | method | — | — | — |
| Aggro | method | CreatureAI/DoCastSpellIfCan | — | — |
| JustDied | method | InstanceData/SetData, WorldObject.Object/GetInstanceData | — | — |
| UpdateAI | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_boss_lorekeeperpolkelt | function | — | — | — |
| AddSC_boss_lorekeeperpolkelt | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
