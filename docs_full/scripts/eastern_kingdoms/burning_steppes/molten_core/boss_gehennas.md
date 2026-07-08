# boss_gehennas

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`boss_gehennas.cpp` implements the combat AI for **Gehennas**, a boss in the **Molten Core** instance. The unit defines `boss_gehennasAI`, a `ScriptedAI` subclass that manages four timed spell rotations and melee attacks. It reports encounter progress (`NOT_STARTED`, `IN_PROGRESS`, `DONE`) to the `ScriptedInstance` and registers itself with the core’s `ScriptMgr`. No database tables are accessed.

## Member-by-Member Behavior

### Initialization & State
*   **`boss_gehennasAI` (ctor)**: Retrieves `ScriptedInstance` via `WorldObject::GetInstanceData` and stores it in `m_pInstance`. Calls `Reset()` to initialize timers.
*   **`Reset`**: If `m_pInstance` is valid and the creature is alive, sets instance data `TYPE_GEHENNAS` to `NOT_STARTED`. Initializes four timers with random ranges: `GehennasCurse` (5–10s), `RainOfFire` (6–12s), `ShadowBoltRandom` (3–6s), and `ShadowBoltTarget` (3–6s).
*   **`Aggro`**: Sets instance data `TYPE_GEHENNAS` to `IN_PROGRESS` and calls `Creature::SetInCombatWithZone`.
*   **`JustDied`**: Sets instance data `TYPE_GEHENNAS` to `DONE`.

### Combat Loop
*   **`UpdateAI`**: Returns early if no hostile target or victim exists. Processes four independent timers:
    *   **Rain of Fire**: Casts `SPELL_RAIN_OF_FIRE` (19717) on a random target (`ATTACKING_TARGET_RANDOM`). Resets timer to 6–12s on success.
    *   **Gehennas' Curse**: Casts `SPELL_GEHENNAS_CURSE` (19716) on the boss itself. Resets timer to 25–30s on success (note: significantly longer than the initial 5–10s reset in `Reset()`).
    *   **Shadow Bolt (Random)**: Casts `SPELL_SHADOW_BOLT_RANDOM` (19728) on a random target. Resets timer to 3–6s on success.
    *   **Shadow Bolt (Target)**: Casts `SPELL_SHADOW_BOLT_TARGET` (19729) on the current victim. Resets timer to 3–6s on success.
    *   Timers are decremented by `uiDiff`; if a cast fails (`DoCastSpellIfCan` != `CAST_OK`), the timer is not reset, allowing retry on the next tick. Finally, calls `DoMeleeAttackIfReady`.

### Registration
*   **`GetAI_boss_gehennas`**: Factory function returning a new `boss_gehennasAI` instance.
*   **`AddSC_boss_gehennas`**: Creates a `Script` object named `"boss_gehennas"`, links `GetAI_boss_gehennas`, and registers it via `Script::RegisterSelf`. Called by `ScriptLoader::AddScripts`.

## Cross-Unit Boundaries

*   **`ScriptedAI`**: Inherits helper methods `DoCastSpellIfCan` and `DoMeleeAttackIfReady`.
*   **`ScriptedInstance`**: Used via `m_pInstance` to update `TYPE_GEHENNAS` state. Pointer obtained from `WorldObject::GetInstanceData`.
*   **`Creature`/`Unit`**: Uses `SelectHostileTarget`, `GetVictim`, `SelectAttackingTarget`, `SetInCombatWithZone`, and `IsAlive` for combat state and targeting.
*   **`shared_Util`**: Uses `urand` for random timer initialization and resets.
*   **`ScriptMgr`**: `AddSC_boss_gehennas` registers the script with the core plugin system.

## Data Model

No database tables are accessed. All spell IDs and timer ranges are hardcoded.

## Notable Implementation Details

1.  **Asymmetric Curse Timer**: `Reset()` initializes `m_uiGehennasCurseTimer` to 5–10s, but `UpdateAI()` resets it to 25–30s after casting. This ensures an early initial cast followed by a long cooldown.
2.  **Independent Timers**: Spells fire asynchronously based on separate timers, creating unpredictable overlap.
3.  **Failure Retry**: If `DoCastSpellIfCan` fails, the timer is not reset, causing immediate retries on subsequent ticks until success.
4.  **Null Safety**: `m_pInstance` is checked before use in `Reset`, `Aggro`, and `JustDied` to prevent crashes if the creature lacks valid instance data.

## Member Reference

*   **`boss_gehennasAI`**: Constructor; fetches `ScriptedInstance` pointer and calls `Reset()`.
*   **`Reset`**: Sets instance state to `NOT_STARTED` (if alive/valid instance) and initializes four spell timers with random values.
*   **`Aggro`**: Sets instance state to `IN_PROGRESS` and marks creature in combat with zone.
*   **`JustDied`**: Sets instance state to `DONE`.
*   **`UpdateAI`**: Main loop; decrements timers, casts spells on expiration (resetting timers on success), and handles melee attacks. Exits early if no target/victim.
*   **`GetAI_boss_gehennas`**: Factory function creating `boss_gehennasAI` instances.
*   **`AddSC_boss_gehennas`**: Registers the script with `ScriptMgr` under name `"boss_gehennas"`.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_gehennas

*Source:* boss_gehennas.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_gehennasAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset | method | InstanceData/SetData, shared_Util/urand, Unit.Main/IsAlive | — | — |
| Aggro | method | Creature.Main/SetInCombatWithZone, InstanceData/SetData | — | — |
| JustDied | method | InstanceData/SetData | — | — |
| UpdateAI | method | Creature.Main/SelectAttackingTarget, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, shared_Util/urand, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_boss_gehennas | function | — | — | — |
| AddSC_boss_gehennas | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
