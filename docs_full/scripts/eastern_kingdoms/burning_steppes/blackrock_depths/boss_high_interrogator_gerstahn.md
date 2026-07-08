<!-- provenance: verbose -->
# boss_high_interrogator_gerstahn

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`boss_high_interrogator_gerstahn.cpp` implements the combat AI for **High Interrogator Gerstahn**, a boss in *Blackrock Depths*. The unit defines `boss_high_interrogator_gerstahnAI`, a `ScriptedAI` subclass that manages a fixed rotation of four spells—**Shadow Word: Pain**, **Mana Burn**, **Psychic Scream**, and **Shadow Shield**—alongside standard melee attacks. It provides the factory function and registration hook required by the server’s script manager to attach this behavior to the creature.

## Member-by-Member Behavior

### Initialization and Lifecycle

**`boss_high_interrogator_gerstahnAI` (Constructor)**
Initializes the AI by invoking the base `ScriptedAI` constructor and immediately calling `Reset()` to seed the spell timers.

**`Reset`**
Sets the initial delays for the four spell timers:
*   `m_uiShadowWordPain_Timer`: 4000 ms
*   `m_uiManaBurn_Timer`: 14000 ms
*   `m_uiPsychicScream_Timer`: 32000 ms
*   `m_uiShadowShield_Timer`: 8000 ms

### Combat Logic

**`UpdateAI`**
Executed every game tick with `uiDiff` (milliseconds since last tick). It performs these steps:
1.  **Guard Clause**: Returns immediately if the creature has no hostile target or current victim, preventing actions when out of combat.
2.  **Shadow Word: Pain**: If `m_uiShadowWordPain_Timer` expires, selects a random target via `SelectAttackingTarget(ATTACKING_TARGET_RANDOM, 0)` and casts `SPELL_SHADOWWORDPAIN` (14032). Resets timer to 7000 ms.
3.  **Mana Burn**: If `m_uiManaBurn_Timer` expires, selects a random target and casts `SPELL_MANABURN` (14033). Resets timer to 10000 ms.
4.  **Psychic Scream**: If `m_uiPsychicScream_Timer` expires, casts `SPELL_PSYCHICSCREAM` (13704) on the current victim (`GetVictim()`). Resets timer to 30000 ms.
5.  **Shadow Shield**: If `m_uiShadowShield_Timer` expires, casts `SPELL_SHADOWSHIELD` (12040) on itself. Resets timer to 25000 ms.
6.  **Melee**: Calls `DoMeleeAttackIfReady()` to handle physical attacks.

Timers are decremented by `uiDiff` each tick if not expired. Initial delays (set in `Reset`) differ from recurring intervals (set in `UpdateAI`).

### Integration Functions

**`GetAI_boss_high_interrogator_gerstahn`**
Factory function returning a new `boss_high_interrogator_gerstahnAI` instance for a given `Creature`.

**`AddSC_boss_high_interrogator_gerstahn`**
Registers the script with `ScriptMgr`. It creates a `Script` object named `"boss_high_interrogator_gerstahn"`, assigns the `GetAI` function pointer, and calls `RegisterSelf()`. Called by `ScriptLoader::AddScripts` at startup.

## Cross-Unit Boundaries

*   **`ScriptedAI`**: Base class providing `DoCastSpellIfCan` and `DoMeleeAttackIfReady`.
*   **`Creature` / `Unit`**: Used for target management (`SelectHostileTarget`, `GetVictim`, `SelectAttackingTarget`).
*   **`ScriptMgr`**: Receives the script registration via `RegisterSelf`.
*   **`ScriptLoader`**: Invokes `AddSC_boss_high_interrogator_gerstahn` during initialization.

## Data Model

This unit does not interact with any database tables. All spell IDs and timer values are hardcoded.

## Notable Implementation Details

*   **Timer Mechanics**: Uses simple subtraction-based timers. The check `timer < uiDiff` handles expiration. Initial delays are longer than some recurring intervals (e.g., Shadow Word: Pain starts at 4000ms but recurs every 7000ms).
*   **Targeting Strategy**: Offensive spells (`Shadow Word: Pain`, `Mana Burn`) target random players. `Psychic Scream` targets the main tank/victim. `Shadow Shield` is self-cast.
*   **No Phases**: The AI lacks health-based phases or complex event triggers; it runs a consistent loop until death or reset.
*   **Safety Check**: The early return in `UpdateAI` prevents casting errors when the boss is not engaged.

## Member Reference

**`boss_high_interrogator_gerstahnAI`**
Constructor initializing the base `ScriptedAI` and calling `Reset()` to set initial timer values.

**`Reset`**
Method resetting all internal spell timers to their initial startup values (4000, 14000, 32000, and 8000 ms).

**`UpdateAI`**
Core AI loop validating targets, processing four independent spell timers (Shadow Word: Pain, Mana Burn, Psychic Scream, Shadow Shield), casting spells upon expiration, and executing melee attacks.

**`GetAI_boss_high_interrogator_gerstahn`**
Factory function instantiating and returning a new `boss_high_interrogator_gerstahnAI` object for a given `Creature`.

**`AddSC_boss_high_interrogator_gerstahn`**
Registration function creating a `Script` object, assigning the AI getter, and registering the script with `ScriptMgr`.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_high_interrogator_gerstahn

*Source:* boss_high_interrogator_gerstahn.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_high_interrogator_gerstahnAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset | method | — | — | — |
| UpdateAI | method | Creature.Main/SelectAttackingTarget, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_boss_high_interrogator_gerstahn | function | — | — | — |
| AddSC_boss_high_interrogator_gerstahn | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
