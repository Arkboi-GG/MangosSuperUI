# boss_lord_alexei_barov

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`boss_lord_alexei_barov.cpp` implements the combat AI for **Lord Alexei Barov**, a boss in the **Scholomance** dungeon. The unit defines `boss_lordalexeibarovAI`, a `ScriptedAI` subclass that manages two timed spells—**Immolate** (random target) and **Veil of Shadow** (victim)—alongside standard melee attacks. It also provides the factory and registration functions required to load this AI into the server’s script manager.

## Member-by-Member Behavior

### Initialization & Lifecycle

*   **`boss_lordalexeibarovAI`**: Constructs the AI, invoking the base `ScriptedAI` constructor and immediately calling `Reset()` to initialize timers.
*   **`Reset`**: Sets `Immolate_Timer` to 7000 ms and `VeilofShadow_Timer` to 15000 ms. Calls `m_creature->LoadCreatureAddon(true)` to restore any database-defined visual or mechanical addons.
*   **`JustDied`**: Retrieves the instance data via `m_creature->GetInstanceData()`, casts it to `ScriptedInstance`, and calls `SetData(TYPE_ALEXEIBAROV, DONE)` to mark the encounter as complete.

### Combat Loop

*   **`UpdateAI`**: Executed periodically with a time delta `diff`.
    1.  Returns early if no hostile target exists.
    2.  **Immolate**: If `Immolate_Timer` expires, selects a random target via `SelectAttackingTarget` and casts `SPELL_IMMOLATE` (15570) if possible. Resets timer to 12000 ms.
    3.  **Veil of Shadow**: If `VeilofShadow_Timer` expires, casts `SPELL_VEILOFSHADOW` (17820) on the current victim. Resets timer to 20000 ms.
    4.  Calls `DoMeleeAttackIfReady()` for physical attacks.

### Registration

*   **`GetAI_boss_lordalexeibarov`**: Factory function returning a new `boss_lordalexeibarovAI` instance for a given `Creature`.
*   **`AddSC_boss_lordalexeibarov`**: Creates a `Script` object named `"boss_lord_alexei_barov"`, assigns the `GetAI` factory, and registers it via `Script::RegisterSelf()`. Called by `ScriptLoader::AddScripts`.

## Cross-Unit Boundaries

*   **`ScriptedAI`**: Base class providing `DoCastSpellIfCan`, `DoMeleeAttackIfReady`, and timer infrastructure.
*   **`Creature`**: Accessed via `m_creature` to load addons, retrieve instance data, and select targets (`SelectHostileTarget`, `GetVictim`, `SelectAttackingTarget`).
*   **`ScriptedInstance`**: Used in `JustDied` to update dungeon progress via `SetData`.
*   **`ScriptMgr` / `Script`**: Infrastructure for registering the AI. `AddSC_boss_lordalexeibarov` populates a `Script` struct and calls `RegisterSelf()`.

## Data Model

This unit does not execute SQL queries. It relies on:
*   **Creature Addons**: Loaded via `LoadCreatureAddon`, implying database-backed visual/mechanical states.
*   **Instance Data**: In-memory state updated via `SetData`, persisted by the instance manager.
*   **Spell Templates**: Spell behaviors for IDs 15570 and 17820 are defined in the database, not the code.

## Notable Implementation Details

*   **Timer Logic**: Timers are decremented by `diff` only if not expired. Initial delays (7s/15s) differ from repeat intervals (12s/20s).
*   **Targeting Strategy**: `Immolate` targets a random enemy, while `Veil of Shadow` targets the primary victim.
*   **Casting Safety**: `DoCastSpellIfCan` handles range, line-of-sight, and cooldown checks automatically.
*   **Instance Cast**: `GetInstanceData()` is cast to `ScriptedInstance*` without null-checking the result before dereferencing, assuming valid instance context.

## Member Reference

*   **`boss_lordalexeibarovAI`**: Constructor initializing base `ScriptedAI` and calling `Reset()`.
*   **`Reset`**: Sets initial timers (7000/15000 ms) and reloads creature addons.
*   **`JustDied`**: Marks `TYPE_ALEXEIBAROV` as `DONE` in the `ScriptedInstance`.
*   **`UpdateAI`**: Manages `Immolate` (random target, 12s) and `Veil of Shadow` (victim, 20s) timers, plus melee attacks.
*   **`GetAI_boss_lordalexeibarov`**: Factory function creating `boss_lordalexeibarovAI` instances.
*   **`AddSC_boss_lordalexeibarov`**: Registers the script with `ScriptMgr` via `Script::RegisterSelf()`.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_lord_alexei_barov

*Source:* boss_lord_alexei_barov.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_lordalexeibarovAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset | method | Creature.Main/LoadCreatureAddon | — | — |
| JustDied | method | InstanceData/SetData, WorldObject.Object/GetInstanceData | — | — |
| UpdateAI | method | Creature.Main/SelectAttackingTarget, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_boss_lordalexeibarov | function | — | — | — |
| AddSC_boss_lordalexeibarov | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
