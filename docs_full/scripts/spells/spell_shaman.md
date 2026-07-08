<!-- provenance: verbose -->
# spell_shaman

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# spell_shaman

## Purpose & Responsibilities

`spell_shaman` implements custom logic for two Shaman spells where default engine behavior is insufficient: **Flametongue Weapon/Totem Procs** and **Mana Tide Totem**. It overrides standard spell execution to calculate dynamic damage based on weapon speed and spell power, and to apply specific variance ("dithering") to healing amounts.

## Member-by-Member Behavior

### Flametongue Proc Handling

**`OnEffectExecute`**
Executes when a Flametongue dummy spell effect triggers.
*   **Validation**: Checks for `EFFECT_INDEX_0` and a valid unit target via `Spell.Main/GetUnitTarget`. Requires `spell->m_CastItem`; if missing, logs an error via `Log.Main/Out` and aborts.
*   **Calculation**: Retrieves caster spell power via `SpellCaster/SpellBaseDamageBonusDone` (using `SpellEntry/GetSpellSchoolMask`) and weapon speed from `game_Objects_Item/GetProto` (`Delay` / 1000). Computes damage as `(spell.damage + 3.85f * spellDamage) * 0.01f * weaponSpeed`.
*   **Execution**: Casts spell 10444 on the target using `SpellCaster/CastCustomSpell#2` with the result of `shared_Util/dither(totalDamage)`.

**`GetScript_ShamanFlametongueProcDummy`**
Factory function returning a new `ShamanFlametongueProcDummyScript` instance.

### Mana Tide Totem Handling

**`OnPeriodicTrigger`**
Intercepts the periodic tick of the Mana Tide Totem aura.
*   **Logic**: Retrieves the trigger spell ID from `Aura/GetSpellProto` using `Aura/GetEffIndex`. If valid, it gets the base amount from `Aura/GetModifier`, applies `shared_Util/dither`, and casts the spell on the target via `SpellCaster/CastCustomSpell#2`.
*   **Suppression**: Sets `spellInfo` to `nullptr` to prevent the engine’s default trigger behavior, avoiding double-healing.

**`GetScript_ShamanManaTide`**
Factory function returning a new `ShamanManaTideAuraScript` instance.

### Script Registration

**`AddSC_shaman_spell_scripts`**
Registers both scripts with the global manager. Creates `Script` objects for `"spell_shaman_flametongue_proc_dummy"` and `"spell_shaman_mana_tide"`, linking them to their respective factory functions, and calls `ScriptMgr/RegisterSelf`. Called by `ScriptLoader/AddScripts`.

## Cross-Unit Boundaries

*   **`OnEffectExecute`**: Calls `game_Objects_Item/GetProto` (item delay), `Log.Main/Out` (error logging), `shared_Util/dither` (variance), `Spell.Main/GetUnitTarget` (target validation), `SpellCaster/CastCustomSpell#2` (casting), `SpellCaster/SpellBaseDamageBonusDone` (spell power), and `SpellEntry/GetSpellSchoolMask` (school identification).
*   **`OnPeriodicTrigger`**: Calls `Aura/GetEffIndex` (effect index), `Aura/GetModifier` (base amount), `Aura/GetSpellProto` (trigger ID), `shared_Util/dither` (variance), and `SpellCaster/CastCustomSpell#2` (casting).
*   **`AddSC_shaman_spell_scripts`**: Calls `Script/Script` (construction) and `ScriptMgr/RegisterSelf` (registration). Called by `ScriptLoader/AddScripts`.

## Data Model

This unit does not interact with any database tables.

## Notable Implementation Details

*   **Hardcoded Coefficient**: Flametongue damage uses `3.85f` as a multiplier for spell power. Comments note this approximates reverse-engineered values (`0.381%` per `0.1` speed) to stay within one point of published values.
*   **Dithering**: Both spells use `shared_Util/dither` to introduce variance or specific rounding, likely to match client-side expectations.
*   **Item Dependency**: Flametongue strictly requires `m_CastItem`. Missing items cause silent failure after logging, preventing crashes from null pointers.
*   **Suppression Mechanism**: Mana Tide relies on setting `spellInfo = nullptr` to bypass default aura triggers; omitting this would result in double healing.

## Member Reference

**OnEffectExecute**: Method in `ShamanFlametongueProcDummyScript` that validates the cast item and target, calculates damage based on spell power and weapon speed using a hardcoded coefficient, and casts a custom spell with dithered damage.

**GetScript_ShamanFlametongueProcDummy**: Factory function that returns a new instance of `ShamanFlametongueProcDummyScript`.

**OnPeriodicTrigger**: Method in `ShamanManaTideAuraScript` that retrieves the trigger spell ID and base amount, casts the spell with a dithered value, and suppresses the default trigger by nullifying `spellInfo`.

**GetScript_ShamanManaTide**: Factory function that returns a new instance of `ShamanManaTideAuraScript`.

**AddSC_shaman_spell_scripts**: Function that registers the Flametongue and Mana Tide scripts with the global script manager via `ScriptMgr/RegisterSelf`.

---

<!-- machine-true, projected from graph.json -->

## Map — spell_shaman

*Source:* spell_shaman.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| OnEffectExecute | method | game_Objects_Item/GetProto, Log.Main/Out, shared_Util/dither, Spell.Main/GetUnitTarget, SpellCaster/CastCustomSpell#2, SpellCaster/SpellBaseDamageBonusDone, SpellEntry/GetSpellSchoolMask | — | — |
| GetScript_ShamanFlametongueProcDummy | function | — | — | — |
| OnPeriodicTrigger | method | Aura/GetEffIndex, Aura/GetModifier, Aura/GetSpellProto, shared_Util/dither, SpellCaster/CastCustomSpell#2 | — | — |
| GetScript_ShamanManaTide | function | — | — | — |
| AddSC_shaman_spell_scripts | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
