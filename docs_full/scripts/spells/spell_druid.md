<!-- provenance: verbose -->
# spell_druid

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# spell_druid

**Purpose & Responsibilities**

`spell_druid.cpp` implements custom server-side logic for three Druid spells: **Ferocious Bite**, **Enrage**, and **Swiftmend**. It overrides default spell behavior to handle mechanics dependent on player state (combo points, energy, existing healing auras) or form-specific modifiers (armor reduction). The unit contains no database interactions.

## Member-by-Member Behavior

### Ferocious Bite Mechanics
Ferocious Bite scales damage with the caster’s Energy and Combo Points. Logic varies by client build.

*   **`OnEffectExecute#2`** (`DruidFerociousBiteScript::OnEffectExecute`):
    Executes on effect index 0. Requires a valid `Unit` target and `Player` caster.
    *   **Post-1.12.0**: Adds damage equal to `Total Attack Power * Combo Points * 0.03` and `Energy * DmgMultiplier` directly to `spell->damage`.
    *   **Pre-1.12.0**: Calculates damage as `Energy * DmgMultiplier`, then casts a secondary spell (ID determined by rank) on the target via `CastCustomSpell`.
    *   **Final Step**: Sets caster Energy to 0.

### Enrage Mechanics
Enrage reduces target armor based on bear form type.

*   **`OnEffectExecute`** (`DruidEnrageScript::OnEffectExecute`):
    Executes on effect index 1 (post-1.8.4). Checks if the target has Aura 9634 (Dire Bear Form). Applies a -16% armor reduction if Dire Bear, otherwise -27%, by casting spell 25503 with the calculated modifier.

### Swiftmend Mechanics
Swiftmend consumes a Regrowth or Rejuvenation aura to deal immediate healing.

*   **`OnCheckCast`** (`DruidSwiftmendScript::OnCheckCast`):
    Validates cast target (post-1.10.2). Returns `SPELL_FAILED_TARGET_AURASTATE` if the target lacks a `SPELL_AURA_PERIODIC_HEAL` aura with Druid family mask `0x50`.

*   **`OnEffectExecute#3`** (`DruidSwiftmendScript::OnEffectExecute`):
    Executes on effect index 0 (post-1.10.2).
    1.  Iterates target’s `SPELL_AURA_PERIODIC_HEAL` auras to find Regrowth or Rejuvenation.
    2.  Selects the aura with the **shortest** remaining duration.
    3.  If none found, logs an error (including target GUID/TypeId) and returns `false`.
    4.  Calculates healing: `tick_heal * tick_count`. Tick counts are hardcoded: 6 for Regrowth, 4 for Rejuvenation.
    5.  Removes the consumed aura and adds the result to `spell->damage`.

### Registration and Factories

*   **`GetScript_DruidFerociousBite`**, **`GetScript_DruidEnrage`**, **`GetScript_DruidSwiftmend`**: Factory functions returning new instances of their respective `SpellScript` subclasses.
*   **`AddSC_druid_spell_scripts`**: Creates `Script` objects for each spell, assigns names, links factory functions, and registers them via `ScriptMgr`. Called by `ScriptLoader/AddScripts`.

## Cross-Unit Boundaries

*   **`Object/ToPlayer`**, **`Player.Main/GetComboPoints`**, **`Unit.Main/GetPower`**, **`Unit.Main/GetTotalAttackPowerValue`**, **`Unit.Main/SetPower`**: Used by `OnEffectExecute#2` to validate caster, calculate AP/Energy-based damage, and drain energy.
*   **`Spell.Main/GetUnitTarget`**: Used by all three scripts to identify the target unit.
*   **`SpellCaster/CastCustomSpell#2`**: Used by `OnEffectExecute#2` (pre-1.12) and `OnEffectExecute` (Enrage) to apply secondary effects.
*   **`Unit.Main/HasAura#2`**: Used by `OnEffectExecute` (Enrage) to detect Dire Bear Form (Aura 9634).
*   **`SpellCastTargetsInfo/getUnitTarget`**, **`Unit.Main/GetAura`**: Used by `OnCheckCast` (Swiftmend) to validate target aura state.
*   **`Unit.Main/GetAurasByType`**, **`Aura/GetAuraDuration`**, **`Aura/GetId`**, **`Aura/GetModifier`**, **`Aura/GetSpellProto`**, **`Unit.Main/RemoveAurasDueToSpell`**: Used by `OnEffectExecute#3` (Swiftmend) to select, analyze, and remove the consumed healing aura.
*   **`Log.Main/Out`**, **`Object/GetGUIDLow`**, **`Object/GetTypeId`**: Used by `OnEffectExecute#3` (Swiftmend) for error logging.
*   **`Script/Script`**, **`ScriptMgr/RegisterSelf`**: Used by `AddSC_druid_spell_scripts` for registration.
*   **`ScriptLoader/AddScripts`**: Calls `AddSC_druid_spell_scripts`.

## Data Model

No database tables are accessed.

## Notable Implementation Details

1.  **Hardcoded Tick Counts**: `OnEffectExecute#3` assumes Regrowth always has 6 ticks and Rejuvenation 4. This ignores potential variations from talents or spell ranks affecting duration/tick rate.
2.  **Energy Drain Order**: `OnEffectExecute#2` calculates damage using current energy, then sets energy to 0. This order is critical; reversing it would yield zero damage.
3.  **Dire Bear Form ID**: `OnEffectExecute` (Enrage) hardcodes Aura ID 9634 for Dire Bear Form detection. Changes to this ID in spell data will break the logic.
4.  **Race Condition Logging**: `OnEffectExecute#3` logs an error if no valid aura is found during execution, despite `OnCheckCast` passing. This indicates the aura expired or was removed between check and execution.

## Member Reference

*   **`OnEffectExecute#2`**: Calculates Ferocious Bite damage based on Combo Points (post-1.12) or secondary spell cast (pre-1.12), scaled by Energy. Drains caster Energy to 0.
*   **`GetScript_DruidFerociousBite`**: Factory function creating `DruidFerociousBiteScript`.
*   **`OnEffectExecute`**: Applies Enrage armor reduction (-16% for Dire Bear, -27% otherwise) via spell 25503.
*   **`GetScript_DruidEnrage`**: Factory function creating `DruidEnrageScript`.
*   **`OnCheckCast`**: Validates Swiftmend target has a consumable Regrowth/Rejuvenation aura.
*   **`OnEffectExecute#3`**: Consumes shortest-duration Regrowth/Rejuvenation aura, calculates immediate healing (hardcoded ticks), removes aura, and adds healing to spell damage. Logs error if aura missing.
*   **`GetScript_DruidSwiftmend`**: Factory function creating `DruidSwiftmendScript`.
*   **`AddSC_druid_spell_scripts`**: Registers all three Druid spell scripts with the `ScriptMgr`.

---

<!-- machine-true, projected from graph.json -->

## Map — spell_druid

*Source:* spell_druid.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| OnEffectExecute#2 | method | Object/ToPlayer, Player.Main/GetComboPoints, Spell.Main/GetUnitTarget, Unit.Main/GetPower, Unit.Main/GetTotalAttackPowerValue, Unit.Main/SetPower | — | — |
| GetScript_DruidFerociousBite | function | — | — | — |
| OnEffectExecute | method | Spell.Main/GetUnitTarget, SpellCaster/CastCustomSpell#2, Unit.Main/HasAura#2 | — | — |
| GetScript_DruidEnrage | function | — | — | — |
| OnCheckCast | method | SpellCastTargetsInfo/getUnitTarget, Unit.Main/GetAura | — | — |
| OnEffectExecute#3 | method | Aura/GetAuraDuration, Aura/GetId, Aura/GetModifier, Aura/GetSpellProto, Log.Main/Out, Object/GetGUIDLow, Object/GetTypeId, Spell.Main/GetUnitTarget, Unit.Main/GetAurasByType, Unit.Main/RemoveAurasDueToSpell | — | — |
| GetScript_DruidSwiftmend | function | — | — | — |
| AddSC_druid_spell_scripts | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
