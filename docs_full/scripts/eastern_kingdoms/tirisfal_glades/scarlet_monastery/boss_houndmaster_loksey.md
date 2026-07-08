# boss_houndmaster_loksey

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# boss_houndmaster_loksey

## Purpose & Responsibilities

`boss_houndmaster_loksey` implements the AI for **Houndmaster Loksey** in the **Scarlet Monastery** instance. The boss follows a straightforward combat pattern: upon aggro, it summons Scarlet Hounds and periodically casts **Bloodlust** on allies while performing standard melee attacks. All behavior is hardcoded; no database tables are accessed.

## Member-by-Member Behavior

### Initialization & State
*   **`boss_houndmaster_lokseyAI`**: Constructs the AI, calling the parent `ScriptedAI` constructor and immediately invoking `Reset()` to initialize timers.
*   **`Reset`**: Sets `BloodLust_Timer` to 20,000 ms. Called by the core on spawn or reset.

### Combat Logic
*   **`Aggro`**: Triggered on combat start. Calls `ScriptMgr::DoScriptText` for line ID `2655` ("Release the hounds!") and attempts to cast `SPELL_SUMMONSCARLETHOUND` (17164) via `CreatureAI::DoCastSpellIfCan`.
*   **`UpdateAI`**: The main tick loop.
    1.  Validates a hostile target exists using `Unit::SelectHostileTarget` and `Unit::GetVictim`; returns early if none.
    2.  Decrements `BloodLust_Timer` by `diff`. If expired, casts `SPELL_BLOODLUST` (6742) via `CreatureAI::DoCastSpellIfCan` and resets the timer to 20,000 ms.
    3.  Calls `CreatureAI::DoMeleeAttackIfReady` for physical attacks.

### Registration
*   **`GetAI_boss_houndmaster_loksey`**: Factory function allocating a new `boss_houndmaster_lokseyAI` instance for a given `Creature`.
*   **`AddSC_boss_houndmaster_loksey`**: Entry point called by `ScriptLoader::AddScripts`. Creates a `Script` object named `"boss_houndmaster_loksey"`, assigns `GetAI_boss_houndmaster_loksey` as the AI getter, and registers it via `Script::RegisterSelf`.

## Cross-Unit Boundaries

*   **Calls `ScriptedAI` / `CreatureAI`**: Inherits base AI structure and uses `DoCastSpellIfCan` and `DoMeleeAttackIfReady` for spell/melee abstraction.
*   **Calls `ScriptMgr`**: Uses `DoScriptText` for dialogue and `Script::RegisterSelf` (via `AddSC`) for registration.
*   **Calls `Unit`**: Uses `SelectHostileTarget` and `GetVictim` in `UpdateAI` to validate combat state.
*   **Called by `ScriptLoader`**: `AddSC_boss_houndmaster_loksey` is invoked during server startup to register the script.

## Data Model

No database tables are accessed. Spell IDs, text IDs, and timers are hardcoded in the source.

## Notable Implementation Details

*   **Fixed Timers**: `BloodLust` has a hardcoded 20-second cooldown.
*   **Unconditional Summon**: `Aggro` always attempts to summon hounds without checking for existing summons.
*   **Legacy Note**: The source header contains a `TODO` suggesting removal if the creature isn't part of a special event, implying potential redundancy with default "ACID" scripts, though this script remains active.

## Member Reference

*   **`boss_houndmaster_lokseyAI`**: Constructor initializing the AI and calling `Reset`.
*   **`Reset`**: Sets `BloodLust_Timer` to 20,000 ms.
*   **`Aggro`**: Broadcasts aggro text and summons Scarlet Hounds.
*   **`UpdateAI`**: Handles target validation, `BloodLust` timer/casting, and melee attacks.
*   **`GetAI_boss_houndmaster_loksey`**: Factory function returning a new `boss_houndmaster_lokseyAI` instance.
*   **`AddSC_boss_houndmaster_loksey`**: Registers the script with the `ScriptMgr`.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_houndmaster_loksey

*Source:* boss_houndmaster_loksey.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_houndmaster_lokseyAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset | method | — | — | — |
| Aggro | method | CreatureAI/DoCastSpellIfCan, ScriptMgr/DoScriptText | — | — |
| UpdateAI | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_boss_houndmaster_loksey | function | — | — | — |
| AddSC_boss_houndmaster_loksey | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
