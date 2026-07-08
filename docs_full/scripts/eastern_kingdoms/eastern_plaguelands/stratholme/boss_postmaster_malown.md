<!-- provenance: verbose -->
# boss_postmaster_malown

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`boss_postmaster_malown.cpp` implements the combat AI for **Postmaster Malown**, a boss in the Stratholme dungeon. The unit defines `boss_postmaster_malownAI`, a subclass of `ScriptedAI`, managing the boss's spell rotation, melee attacks, and audio cues. It handles probabilistic casting of five spells (*Wailing Dead*, *Backhand*, *Curse of Weakness*, *Curse of Tongues*, *Call of the Grave*) based on independent timers and random chance checks.

## Member-by-Member Behavior

### Initialization and State

**`boss_postmaster_malownAI`**
Constructs the AI object for a `Creature`, invoking the base `ScriptedAI` constructor and immediately calling `Reset()` to initialize timers.

**`Reset`**
Initializes five independent spell timers (`WailingDead_Timer`, `Backhand_Timer`, `CurseOfWeakness_Timer`, `CurseOfTongues_Timer`, `CallOfTheGrave_Timer`) to their maximum intervals (8,000ms–25,000ms). It also sets `HasYelled` to `false` (unused in current logic).

### Combat Events

**`Aggro`**
Triggers when the boss gains a hostile target. Calls `ScriptMgr::DoScriptText` to play the aggro line (ID 6504), then delegates to `ScriptedAI::Aggro`.

**`KilledUnit`**
Triggers when the boss kills a unit. Calls `ScriptMgr::DoScriptText` to play the kill line (ID 6530), then delegates to `CreatureAI::KilledUnit`.

### Core AI Loop

**`UpdateAI`**
Executes the main combat tick:
1.  **Validation:** Returns early if `Unit::SelectHostileTarget` or `Unit::GetVictim` indicates no active target.
2.  **Spell Rotation:** For each of the five spells, checks if its timer has expired. If so, it rolls a random percentage against a hardcoded threshold (65%, 45%, 3%, 3%, or 5%). On success, it casts the spell on the victim via `CreatureAI::DoCastSpellIfCan`. The timer resets regardless of the cast outcome; otherwise, it decrements by `diff`.
3.  **Melee:** Calls `CreatureAI::DoMeleeAttackIfReady` to perform physical attacks.

### Script Integration

**`GetAI_boss_postmaster_malown`**
Factory function allocating a new `boss_postmaster_malownAI` instance for a given `Creature`.

**`AddSC_boss_postmaster_malown`**
Registers the script with the core engine. Creates a `Script` object named `"boss_postmaster_malown"`, links `GetAI_boss_postmaster_malown`, and calls `Script::RegisterSelf`. Invoked by `ScriptLoader::AddScripts`.

## Cross-Unit Boundaries

*   **`ScriptedAI` / `CreatureAI`:** Inherits from `ScriptedAI` and calls `ScriptedAI::Aggro`, `CreatureAI::KilledUnit`, `CreatureAI::DoCastSpellIfCan`, and `CreatureAI::DoMeleeAttackIfReady` to leverage core threat, spell, and attack mechanics.
*   **`ScriptMgr`:** Calls `DoScriptText` to trigger audio events.
*   **`Unit.Main`:** Calls `GetVictim` and `SelectHostileTarget` to validate combat state and target selection.
*   **`Script` / `ScriptMgr`:** `AddSC_boss_postmaster_malown` creates a `Script` and calls `RegisterSelf` to integrate with the global registry. Called by `ScriptLoader::AddScripts`.

## Data Model

This unit does not interact with any database tables. All configuration is hardcoded.

## Notable Implementation Details

*   **Unused Flag:** `HasYelled` is declared and reset but never read or modified.
*   **Probabilistic Casting:** Spells do not cast deterministically on cooldown. Each spell has a fixed minimum interval, but casting requires a successful random roll (e.g., *Wailing Dead* has a 65% chance every 19s). This introduces variability and potential gaps in spell usage.
*   **Low-Probability Spells:** *Curse of Weakness* and *Curse of Tongues* have only a 3% cast chance per check, making them rare occurrences during typical fight durations.

## Member Reference

**`boss_postmaster_malownAI`**: Constructor initializing the base `ScriptedAI` and calling `Reset()`.

**`Reset`**: Resets spell timers to max intervals and sets `HasYelled` to false.

**`Aggro`**: Plays aggro audio via `ScriptMgr::DoScriptText` and calls `ScriptedAI::Aggro`.

**`KilledUnit`**: Plays kill audio via `ScriptMgr::DoScriptText` and calls `CreatureAI::KilledUnit`.

**`UpdateAI`**: Main combat loop. Validates victim. Checks five spell timers; if expired, rolls random chance to cast via `CreatureAI::DoCastSpellIfCan`. Resets timers or decrements by `diff`. Calls `CreatureAI::DoMeleeAttackIfReady`.

**`GetAI_boss_postmaster_malown`**: Factory function returning a new `boss_postmaster_malownAI` instance.

**`AddSC_boss_postmaster_malown`**: Registers the script with the core engine via `Script::RegisterSelf`. Called by `ScriptLoader::AddScripts`.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_postmaster_malown

*Source:* boss_postmaster_malown.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_postmaster_malownAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset | method | — | — | — |
| Aggro | method | ScriptedAI/Aggro, ScriptMgr/DoScriptText | — | — |
| KilledUnit | method | CreatureAI/KilledUnit, ScriptMgr/DoScriptText | — | — |
| UpdateAI | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_boss_postmaster_malown | function | — | — | — |
| AddSC_boss_postmaster_malown | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
