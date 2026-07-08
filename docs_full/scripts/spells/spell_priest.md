<!-- provenance: verbose -->
# spell_priest

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# spell_priest

## Purpose & Responsibilities

`spell_priest.cpp` implements custom server-side logic for three specific Priest spells in the World of Warcraft emulation environment. While the core engine handles standard spell effects via database templates, these spells require complex conditional behaviors, secondary spell triggers, or rank-specific side effects that cannot be expressed through static data alone.

This unit provides `SpellScript` subclasses that hook into the spell lifecycle:
1.  **Touch of Weakness**: Triggers a secondary debuff on the target when the primary aura ticks, mapping the triggering aura's rank to the correct secondary spell ID.
2.  **Power Word: Shield**: Applies the "Weakened Soul" debuff to the caster upon successfully casting the shield.
3.  **Holy Nova**: Triggers a secondary area-of-effect spell on the caster after the main spell finishes, mapping the main spell's rank to the corresponding secondary spell ID.

The unit also contains `AddSC_priest_spell_scripts`, which registers these scripts with the global `ScriptMgr`.

## Member-by-Member Behavior

### Touch of Weakness
**`PriestTouchOfWeaknessScript::OnEffectExecute`**
Executes when the "Touch of Weakness" aura (Spell ID 28598) ticks. It validates that the effect index is 0, a unit target exists, and the spell was triggered by an aura. It then maps the ID of the triggering aura (ranks 1–6) to a specific secondary spell ID using a `switch` statement. If matched, it casts the secondary spell on the target via `SpellCaster::CastSpell`. If the aura ID is unrecognized, it logs an error via `Log.Main/Out` and returns `false`.

**`GetScript_PriestTouchOfWeakness`**
Factory function that instantiates and returns a new `PriestTouchOfWeaknessScript` object.

### Power Word: Shield
**`PriestPowerWordShieldScript::OnHit`**
Executes when "Power Word: Shield" hits its target. If the hit was successful (`SPELL_MISS_NONE`) and a target exists, it casts the internal "Weakened Soul" spell (ID 6788) on the **caster** using `SpellCaster::CastSpell`. The cast is marked as triggered, bypassing normal cooldowns.

**`GetScript_PriestPowerWordShield`**
Factory function that instantiates and returns a new `PriestPowerWordShieldScript` object.

### Holy Nova
**`PriestHolyNovaScript::OnSuccessfulFinish`**
Executes after "Holy Nova" completes. It validates the caster unit and maps the main spell ID to a secondary spell ID. If the ID is unknown, it logs an error via `Log.Main/Out` and returns. Otherwise, it retrieves the `SpellEntry` via `SpellMgr/GetSpellEntry`, constructs a new `Spell` object using `Spell.Main/Spell#2` (preserving the original caster GUID via `Spell.Main/GetOriginalCasterGuid`), and prepares it with the original targets via `Spell.Main/prepare`.

**`GetScript_PriestHolyNova`**
Factory function that instantiates and returns a new `PriestHolyNovaScript` object.

### Registration
**`AddSC_priest_spell_scripts`**
Registers the three script classes with the global script manager. It creates `Script` objects, assigns their names and factory functions, and calls `ScriptMgr/RegisterSelf`. Called by `ScriptLoader/AddScripts` during initialization.

## Cross-Unit Boundaries

*   **`Log.Main/Out`**: Called by `OnEffectExecute` and `OnSuccessfulFinish` to log errors for unrecognized spell IDs.
*   **`Spell.Main/GetUnitTarget`**: Called by `OnEffectExecute` and `OnHit` to retrieve the target unit.
*   **`SpellCaster/CastSpell#2`**: Called by `OnEffectExecute` and `OnHit` to cast secondary spells.
*   **`Spell.Main/GetOriginalCasterGuid`**: Called by `OnSuccessfulFinish` to preserve caster identity for the secondary Holy Nova spell.
*   **`Spell.Main/prepare`**: Called by `OnSuccessfulFinish` to initialize the secondary spell with targets.
*   **`Spell.Main/Spell#2`**: Called by `OnSuccessfulFinish` to construct the secondary `Spell` object.
*   **`SpellMgr/GetSpellEntry`**: Called by `OnSuccessfulFinish` to fetch the secondary spell's static definition.
*   **`SpellMgr/Instance`**: Listed in the map for `OnSuccessfulFinish`; implicitly involved in spell creation.
*   **`Script/Script`**: Used in `AddSC_priest_spell_scripts` to create registration wrappers.
*   **`ScriptMgr/RegisterSelf`**: Called by `AddSC_priest_spell_scripts` to activate scripts.
*   **`ScriptLoader/AddScripts`**: Caller of `AddSC_priest_spell_scripts`.

## Data Model

This unit does not interact with any database tables. All spell IDs and mappings are hardcoded.

## Notable Implementation Details

*   **Hardcoded Rank Mappings**: `Touch of Weakness` and `Holy Nova` rely on explicit `switch` statements. New ranks require manual code updates; otherwise, errors are logged and effects fail.
*   **Triggered Spells**: Secondary casts in `Touch of Weakness` and `Power Word: Shield` use the `triggered` flag, indicating they are part of the original spell's resolution.
*   **Holy Nova Target Reuse**: `OnSuccessfulFinish` prepares the secondary spell using `spell->m_targets`, applying the secondary effect to the same targets as the primary spell.
*   **Memory Management**: `GetScript_*` functions use `new`; ownership is managed by `ScriptMgr`.

## Member Reference

**`OnEffectExecute`**: Method in `PriestTouchOfWeaknessScript`. Executes on aura tick. Validates trigger aura, maps ID to secondary spell ID, and casts it on the target. Logs error if ID is unrecognized. Calls `Log.Main/Out`, `Spell.Main/GetUnitTarget`, `SpellCaster/CastSpell#2`.

**`GetScript_PriestTouchOfWeakness`**: Factory function. Instantiates and returns a new `PriestTouchOfWeaknessScript` object.

**`OnHit`**: Method in `PriestPowerWordShieldScript`. Executes on spell hit. If successful, casts "Weakened Soul" (ID 6788) on the caster. Calls `Spell.Main/GetUnitTarget`, `SpellCaster/CastSpell#2`.

**`GetScript_PriestPowerWordShield`**: Factory function. Instantiates and returns a new `PriestPowerWordShieldScript` object.

**`OnSuccessfulFinish`**: Method in `PriestHolyNovaScript`. Executes after spell completion. Maps main spell ID to secondary ID, retrieves entry, creates new `Spell` object, and prepares it with original targets. Logs error if ID is unrecognized. Calls `Log.Main/Out`, `Spell.Main/GetOriginalCasterGuid`, `Spell.Main/prepare`, `Spell.Main/Spell#2`, `SpellMgr/GetSpellEntry`, `SpellMgr/Instance`.

**`GetScript_PriestHolyNova`**: Factory function. Instantiates and returns a new `PriestHolyNovaScript` object.

**`AddSC_priest_spell_scripts`**: Function. Registers the three Priest spell scripts with `ScriptMgr` by creating `Script` objects, setting names/factories, and calling `RegisterSelf`. Called by `ScriptLoader/AddScripts`. Uses `Script/Script`, `ScriptMgr/RegisterSelf`.

---

<!-- machine-true, projected from graph.json -->

## Map — spell_priest

*Source:* spell_priest.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| OnEffectExecute | method | Log.Main/Out, Spell.Main/GetUnitTarget, SpellCaster/CastSpell#2 | — | — |
| GetScript_PriestTouchOfWeakness | function | — | — | — |
| OnHit | method | Spell.Main/GetUnitTarget, SpellCaster/CastSpell#2 | — | — |
| GetScript_PriestPowerWordShield | function | — | — | — |
| OnSuccessfulFinish | method | Log.Main/Out, Spell.Main/GetOriginalCasterGuid, Spell.Main/prepare, Spell.Main/Spell#2, SpellMgr/GetSpellEntry, SpellMgr/Instance | — | — |
| GetScript_PriestHolyNova | function | — | — | — |
| AddSC_priest_spell_scripts | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
