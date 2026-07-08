<!-- provenance: verbose -->
# boss_halycon

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`boss_halycon.cpp` implements the AI for **Halycon**, a boss in Blackrock Spire. It manages two timed spells (*Crowd Pummel*, *Mighty Blow*) and triggers the summoning of the next boss, **Gizrul**, upon Halycon's death.

## Member-by-Member Behavior

### Initialization & Combat
**`boss_halyconAI` (Constructor)**
Initializes `Summoned` to `false` and calls `Reset()` to set initial timer values. Inherits from `ScriptedAI`.

**`Reset`**
Sets `CrowdPummel_Timer` to 8000 ms and `MightyBlow_Timer` to 14000 ms.

**`UpdateAI`**
Executes the main combat loop:
1.  Returns early if no hostile target or victim exists (`SelectHostileTarget`, `GetVictim`).
2.  **Crowd Pummel:** If timer expires, casts `SPELL_CROWDPUMMEL` (10887) on the victim via `DoCastSpellIfCan` and resets timer to 14000 ms.
3.  **Mighty Blow:** If timer expires, casts `SPELL_MIGHTYBLOW` (14099) on the victim via `DoCastSpellIfCan` and resets timer to 10000 ms.
4.  Calls `DoMeleeAttackIfReady` for physical attacks.

### Death & Transition
**`JustDied`**
Handles the phase transition to Gizrul:
1.  Guards against duplicate summons using the `Summoned` flag.
2.  Emotes a death message via `MonsterTextEmote`.
3.  Summons Gizrul (Entry 10268) at `(-167.58, -382.41, 64.401)` with `TEMPSUMMON_DEAD_DESPAWN`.
4.  On successful summon:
    *   Sets Gizrul's home position to `(-172.633, -324.253, 64.401)` via `SetHomePosition`.
    *   Forces immediate combat state via `SetInCombatWithZone`.
5.  Sets `Summoned = true`.

### Registration
**`GetAI_boss_halycon`**
Factory function returning a new `boss_halyconAI` instance for a `Creature`.

**`AddSC_boss_halycon`**
Registers the script: creates a `Script` named "boss_halycon", assigns `GetAI_boss_halycon`, and calls `RegisterSelf()`. Called by `ScriptLoader/AddScripts`.

## Cross-Unit Boundaries

*   **`ScriptedAI` / `CreatureAI`:** Inherits from `ScriptedAI`; uses `DoCastSpellIfCan` and `DoMeleeAttackIfReady` from `CreatureAI` for spell/melee logic.
*   **`Unit.Main`:** `UpdateAI` uses `GetVictim` and `SelectHostileTarget` to validate targets.
*   **`Creature.Main`:** `JustDied` uses `SetHomePosition` and `SetInCombatWithZone` on the summoned Gizrul.
*   **`WorldObject.Object`:** `JustDied` uses `MonsterTextEmote` for text and `SummonCreature` to spawn Gizrul.
*   **`Script` / `ScriptMgr`:** `AddSC_boss_halycon` constructs a `Script` and registers it via `RegisterSelf`.

## Data Model

This unit does not interact with any database tables. All configuration (spell IDs, coordinates, timers) is hardcoded.

## Notable Implementation Details

*   **Idempotent Summon:** The `Summoned` boolean prevents multiple Gizruls if `JustDied` fires repeatedly.
*   **Fixed Timers:** Timers reset to fixed constants (14s/10s) regardless of elapsed time, ensuring consistent intervals after the initial delay.
*   **Immediate Aggro:** `SetInCombatWithZone()` forces Gizrul to aggro nearby players instantly upon spawn.

## Member Reference

**`boss_halyconAI`**: Constructor initializing `Summoned` to false and calling `Reset()`. Inherits from `ScriptedAI`.

**`Reset`**: Resets `CrowdPummel_Timer` to 8000 ms and `MightyBlow_Timer` to 14000 ms.

**`UpdateAI`**: Main loop. Validates target. Casts `SPELL_CROWDPUMMEL` (reset 14s) and `SPELL_MIGHTYBLOW` (reset 10s) on victim when timers expire. Calls `DoMeleeAttackIfReady`.

**`JustDied`**: If `!Summoned`, emotes death, summons Gizrul (10268), sets its home position and combat state, then sets `Summoned = true`.

**`GetAI_boss_halycon`**: Factory function returning a new `boss_halyconAI` instance.

**`AddSC_boss_halycon`**: Registers the "boss_halycon" script with `GetAI_boss_halycon` via `Script::RegisterSelf()`. Called by `ScriptLoader/AddScripts`.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_halycon

*Source:* boss_halycon.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_halyconAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset | method | — | — | — |
| UpdateAI | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| JustDied | method | Creature.Main/SetHomePosition, Creature.Main/SetInCombatWithZone, WorldObject.Object/MonsterTextEmote, WorldObject.Object/SummonCreature#2 | — | — |
| GetAI_boss_halycon | function | — | — | — |
| AddSC_boss_halycon | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
