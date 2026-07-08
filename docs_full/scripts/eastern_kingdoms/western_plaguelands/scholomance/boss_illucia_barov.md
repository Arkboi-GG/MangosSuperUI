<!-- provenance: verbose -->
# boss_illucia_barov

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`boss_illucia_barov.cpp` implements the combat AI for **Illucia Barov**, a boss in the Scholomance dungeon. The unit defines `boss_illuciabarovAI`, a subclass of `ScriptedAI`, which manages a timer-based rotation of four spells (*Curse of Agony*, *Shadow Shock*, *Silence*, *Fear*) alongside standard melee attacks. It also provides the factory function and registration logic to integrate this AI into the server’s script system.

## Member-by-Member Behavior

### Initialization and Lifecycle

**`boss_illuciabarovAI`**
Constructs the AI for a `Creature`. Calls `Reset()` immediately to initialize timer values.

**`Reset`**
Sets initial cooldowns for all spells:
*   `CurseOfAgony_Timer`: 18,000 ms
*   `ShadowShock_Timer`: 9,000 ms
*   `Silence_Timer`: 5,000 ms
*   `Fear_Timer`: 30,000 ms

### Combat Logic

**`UpdateAI`**
Executed periodically. Validates that a hostile target and victim exist. Processes four independent timers:
1.  **Curse of Agony:** Casts `SPELL_CURSEOFAGONY` on the victim when expired. Resets to 30,000 ms.
2.  **Shadow Shock:** Selects a random attacking target (`ATTACKING_TARGET_RANDOM`) and casts `SPELL_SHADOWSHOCK`. Resets to 12,000 ms.
3.  **Silence:** Casts `SPELL_SILENCE` on the victim when expired. Resets to 14,000 ms.
4.  **Fear:** Casts `SPELL_FEAR` on the victim when expired. Resets to 30,000 ms.

Finally, calls `DoMeleeAttackIfReady()` to handle physical attacks. Initial timers in `Reset()` differ from post-cast reset values, creating a staggered start to the ability rotation.

### Death Handling

**`JustDied`**
Retrieves the `ScriptedInstance` from the creature’s context. If valid, calls `SetData(TYPE_ILLUCIABAROV, DONE)` to mark the boss as defeated.

### Script Registration

**`GetAI_boss_illuciabarov`**
Factory function returning a new `boss_illuciabarovAI` instance.

**`AddSC_boss_illuciabarov`**
Registers the script with `ScriptMgr`. Creates a `Script` object named `"boss_illucia_barov"`, assigns `GetAI_boss_illuciabarov` as the AI getter, and calls `RegisterSelf()`. Called by `ScriptLoader::AddScripts`.

## Cross-Unit Boundaries

*   **Calls `ScriptedAI`**: Inherits base AI functionality and helpers like `DoCastSpellIfCan` and `DoMeleeAttackIfReady`.
*   **Calls `Creature`**: Uses `m_creature` to access `SelectHostileTarget`, `GetVictim`, `SelectAttackingTarget`, and `GetInstanceData`.
*   **Calls `ScriptedInstance`**: Invokes `SetData` in `JustDied` to update instance state.
*   **Calls `Script`/`ScriptMgr`**: Used in `AddSC_boss_illuciabarov` for registration.
*   **Called by `ScriptLoader::AddScripts`**: Loads the script during server startup.

## Data Model

This unit does not interact with any database tables. State is managed entirely in-memory via timers and `ScriptedInstance`.

## Notable Implementation Details

*   **Staggered Timers:** Initial timers in `Reset()` are shorter than the recurring cooldowns in `UpdateAI` (e.g., *Curse of Agony* starts at 18s but recurs every 30s). This ensures abilities come online quickly at the start of combat.
*   **Random Targeting:** *Shadow Shock* targets a random hostile unit, unlike other spells which target the current victim.
*   **C-Style Cast:** `JustDied` uses a C-style cast `(ScriptedInstance*)` on `GetInstanceData()`. This assumes the creature is always in a scripted instance; invalid contexts may lead to undefined behavior.

## Member Reference

**`boss_illuciabarovAI`**  
Constructor initializing `ScriptedAI` and calling `Reset()` to set initial timer values.

**`Reset`**  
Resets spell timers to initial values: *Curse of Agony* (18s), *Shadow Shock* (9s), *Silence* (5s), *Fear* (30s).

**`JustDied`**  
Marks the boss as defeated by calling `SetData(TYPE_ILLUCIABAROV, DONE)` on the `ScriptedInstance`.

**`UpdateAI`**  
Processes spell timers for *Curse of Agony*, *Shadow Shock*, *Silence*, and *Fear*, casting them on victims or random targets, and handles melee attacks.

**`GetAI_boss_illuciabarov`**  
Factory function creating a new `boss_illuciabarovAI` instance.

**`AddSC_boss_illuciabarov`**  
Registers the script with `ScriptMgr` under the name `"boss_illucia_barov"`.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_illucia_barov

*Source:* boss_illucia_barov.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_illuciabarovAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset | method | — | — | — |
| JustDied | method | InstanceData/SetData, WorldObject.Object/GetInstanceData | — | — |
| UpdateAI | method | Creature.Main/SelectAttackingTarget, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_boss_illuciabarov | function | — | — | — |
| AddSC_boss_illuciabarov | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
