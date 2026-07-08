# boss_shadow_hunter_voshgajin

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`boss_shadow_hunter_voshgajin.cpp` implements the combat AI for **Shadow Hunter Voshgajin**, a boss in the Blackrock Spire dungeon. The unit defines `boss_shadowvoshAI`, which inherits from `ScriptedAI` to manage three timed abilities—**Curse of Blood**, **Hex**, and **Cleave**—alongside standard melee attacks. It contains no database interactions; all state is managed via in-memory timers.

## Member-by-Member Behavior

### AI Lifecycle
*   **`boss_shadowvoshAI` (Constructor):** Initializes the AI by calling the parent `ScriptedAI` constructor and immediately invoking `Reset()` to set initial timer values.
*   **`Reset`:** Sets initial countdowns for the three abilities: `m_uiCurseOfBloodTimer` (2,000 ms), `m_uiHexTimer` (8,000 ms), and `m_uiCleaveTimer` (14,000 ms). A commented-out line indicates a previously disabled self-cast of Ice Armor.

### Combat Logic (`UpdateAI`)
Executed periodically by the engine, `UpdateAI` performs these steps:
1.  **Validation:** Returns early if the creature lacks a hostile target or current victim.
2.  **Curse of Blood:** If `m_uiCurseOfBloodTimer` expires, casts `SPELL_CURSEOFBLOOD` on self and resets the timer to 45,000 ms. Otherwise, decrements the timer.
3.  **Hex:** If `m_uiHexTimer` expires, selects a random hostile target. If valid, casts `SPELL_HEX` and adds 15,000 ms to the timer (relative reset). Otherwise, decrements the timer.
4.  **Cleave:** If `m_uiCleaveTimer` expires, casts `SPELL_CLEAVE` on the current victim and resets the timer to 7,000 ms. Otherwise, decrements the timer.
5.  **Melee:** Calls `DoMeleeAttackIfReady()` to handle physical attacks.

### Script Registration
*   **`GetAI_boss_shadowvosh`:** Factory function returning a new `boss_shadowvoshAI` instance for a given `Creature`.
*   **`AddSC_boss_shadowvosh`:** Registers the script with `ScriptMgr`. It creates a `Script` object named `"boss_shadow_hunter_voshgajin"`, links `GetAI_boss_shadowvosh`, and calls `RegisterSelf()`. This function is invoked by `ScriptLoader::AddScripts` during server startup.

## Cross-Unit Boundaries

*   **`ScriptedAI` (Inheritance):** Provides the base AI structure, including `UpdateAI`, `DoCastSpellIfCan`, and `DoMeleeAttackIfReady`.
*   **`Creature` / `Unit` (Outbound Calls):** `UpdateAI` calls `SelectHostileTarget`, `GetVictim`, and `SelectAttackingTarget` to determine valid targets for spells and attacks.
*   **`Script` / `ScriptMgr` (Outbound Calls):** `AddSC_boss_shadowvosh` constructs a `Script` object and calls `RegisterSelf()` to register the AI with the global script manager.
*   **`ScriptLoader` (Inbound Call):** `ScriptLoader::AddScripts` calls `AddSC_boss_shadowvosh` to load this AI during initialization.

## Data Model

This unit does not interact with any database tables.

## Notable Implementation Details

*   **Timer Reset Strategies:** `CurseOfBlood` and `Cleave` use absolute resets (`=`), ensuring strict periodicity. `Hex` uses relative addition (`+= 15000`), which maintains the interval relative to the last cast attempt but may drift slightly if updates are delayed.
*   **Targeting Safety:** `Hex` explicitly checks if `SelectAttackingTarget` returns a valid pointer before casting, preventing errors if no valid targets exist.
*   **Hardcoded Spell IDs:** Spell IDs (24673, 16708, 20691) are defined as enums. Changes to these spells in the database require updating these constants.

## Member Reference

*   **`boss_shadowvoshAI`**: Constructor initializing the AI via `ScriptedAI` and invoking `Reset()` to set initial timers.
*   **`Reset`**: Method setting initial timer values for Curse of Blood (2s), Hex (8s), and Cleave (14s); contains a commented-out Ice Armor cast.
*   **`UpdateAI`**: Core combat loop managing timers for Curse of Blood, Hex, and Cleave, casting spells upon expiration, and triggering melee attacks.
*   **`GetAI_boss_shadowvosh`**: Factory function creating and returning a new `boss_shadowvoshAI` instance for a `Creature`.
*   **`AddSC_boss_shadowvosh`**: Registration function creating a `Script` object, linking `GetAI_boss_shadowvosh`, and registering it with `ScriptMgr`.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_shadow_hunter_voshgajin

*Source:* boss_shadow_hunter_voshgajin.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_shadowvoshAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset | method | — | — | — |
| UpdateAI | method | Creature.Main/SelectAttackingTarget, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_boss_shadowvosh | function | — | — | — |
| AddSC_boss_shadowvosh | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
