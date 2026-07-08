<!-- provenance: verbose -->
# spell_warrior

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# spell_warrior

## Purpose & Responsibilities

`spell_warrior.cpp` implements custom spell and aura scripts for the **Warrior** class in the WoW server emulation. It overrides default spell engine behavior for specific abilities that require complex conditional logic, dynamic damage calculation, or state management not supported by static spell data.

The unit defines nine script structs (eight `SpellScript`s and one `AuraScript`) and a registration function. Key behaviors include:
*   **Intimidating Shout**: Excluding the primary target from AoE fear.
*   **Bloodthirst/Shield Slam**: Dynamic damage scaling based on Attack Power and Shield Block Value.
*   **Execute**: Coordinating rage consumption and damage casting across two linked spells.
*   **Bloodrage**: Forcing combat state on targets.
*   **Sweeping Strikes/Blood Fury**: Managing aura persistence and expiration penalties.

This unit does not interact with any database tables.

## Member-by-Member Behavior

### Intimidating Shout (Spell ID 5246)
*   **`OnCheckTarget`**: Validates targets for the fear effect. Returns `false` if the target matches the spell's main unit target (`SpellCastTargetsInfo::getUnitTarget`), preventing the primary target (who receives a stun) from also being feared.
*   **`GetScript_WarriorIntimidatingShout`**: Factory function returning a `WarriorIntimidatingShoutScript` instance.

### Bloodthirst (Spell IDs 23881–23894)
*   **`OnEffectExecute#2`**: Modifies damage for Effect Index 0. Calculates damage as `(Base Damage * Total Attack Power) / 100`. Total Attack Power includes base AP (`Unit::GetTotalAttackPowerValue`) and any type-specific melee AP modifiers (`Unit::GetTotalAuraModifierByMiscMask`) against the target's creature type (`Unit::GetCreatureTypeMask`).
*   **`GetScript_WarriorBloodthirst`**: Factory function returning a `WarriorBloodthirstScript` instance.

### Shield Slam (Spell IDs 23922–23925)
*   **`OnEffectExecute#4`**: Modifies damage for Effect Index 1. Adds the caster's Shield Block Value (`Unit::GetShieldBlockValue`) to the spell's damage.
*   **`GetScript_WarriorShieldSlam`**: Factory function returning a `WarriorShieldSlamScript` instance.

### Execute (Spell IDs 5308, 20647, 20658–20662)
Execute is split into a dummy spell that calculates damage and a damage spell that consumes rage.

*   **`OnCast`** (in `WarriorExecuteDummyScript`): Triggered when the dummy spell is cast. Calculates damage using `Base Points + dither(Rage * DmgMultiplier)` (`shared_Util::dither`, `Unit::GetPower`). Casts the actual damage spell (ID 20647) on the target (`SpellCaster::CastCustomSpell`). Rage consumption is deferred to the damage script to ensure correct calculation order.
*   **`GetScript_WarriorExecuteDummy`**: Factory function returning a `WarriorExecuteDummyScript` instance.
*   **`OnEffectExecute#3`** (in `WarriorExecuteDamageScript`): Triggered when the damage spell's Effect Index 0 executes. Sets the caster's Rage to 0 (`Unit::SetPower`), completing the rage consumption.
*   **`GetScript_WarriorExecuteDamage`**: Factory function returning a `WarriorExecuteDamageScript` instance.

### Warrior's Wrath (Spell ID 21977)
*   **`OnEffectExecute#5`**: Triggered when Effect Index 0 executes. Casts secondary spell ID 21887 on the target (`SpellCaster::CastSpell`) to apply the actual mechanical/visual effects.
*   **`GetScript_WarriorWrath`**: Factory function returning a `WarriorWrathScript` instance.

### Bloodrage (Spell ID 2687)
*   **`OnEffectExecute`**: Triggered when Effect Index 0 executes. Forces the target into combat state (`Unit::SetInCombatState`) to ensure proper aggro handling.
*   **`GetScript_WarriorBloodrage`**: Factory function returning a `WarriorBloodrageScript` instance.

### Sweeping Strikes (Aura ID 12292)
*   **`OnHolderInit`**: Configures the aura holder to persist through shapeshifts by calling `SpellAuraHolder::SetRemovedOnShapeLost(false)`.
*   **`GetScript_WarriorSweepingStrikes`**: Factory function returning a `WarriorSweepingStrikesAuraScript` instance.

### Blood Fury (Aura ID 23234)
*   **`OnBeforeApply`**: Active only for client builds 1.3.1–1.8.4. When the aura is removed (cancel/expire), it schedules a delayed lambda event (offset 1 tick) to apply a -25% Attack Power debuff (Spell ID 23230). The delay avoids engine errors during `ExclusiveAuraUnapply`.
*   **`GetScript_WarriorBloodFury`**: Factory function returning a `WarriorBloodFuryAuraScript` instance.

### Registration
*   **`AddSC_warrior_spell_scripts`**: Entry point called by `ScriptLoader`. Creates `Script` objects for each ability, assigns names, links factory functions, and registers them via `Script::RegisterSelf`.

## Cross-Unit Boundaries

| Member | Direction | Target Unit/Function | Purpose |
| :--- | :--- | :--- | :--- |
| `OnCheckTarget` | Calls Out | `SpellCastTargetsInfo::getUnitTarget` | Identify primary target to exclude from fear. |
| `OnEffectExecute#2` | Calls Out | `Spell::GetUnitTarget` | Check for valid target for AP calculation. |
| `OnEffectExecute#2` | Calls Out | `Unit::GetCreatureTypeMask` | Get target type for aura filtering. |
| `OnEffectExecute#2` | Calls Out | `Unit::GetTotalAttackPowerValue` | Retrieve base attack power. |
| `OnEffectExecute#2` | Calls Out | `Unit::GetTotalAuraModifierByMiscMask` | Retrieve type-specific AP bonuses. |
| `OnEffectExecute#4` | Calls Out | `Unit::GetShieldBlockValue` | Get shield block value for damage calc. |
| `OnEffectExecute#3` | Calls Out | `Unit::SetPower` | Set Rage to 0 after Execute. |
| `OnCast` | Calls Out | `shared_Util::dither` | Randomize/round rage-to-damage conversion. |
| `OnCast` | Calls Out | `Spell::GetUnitTarget` | Validate target for Execute. |
| `OnCast` | Calls Out | `Unit::GetPower` | Get current Rage amount. |
| `OnCast` | Calls Out | `SpellCaster::CastCustomSpell` | Cast the actual Execute damage spell. |
| `OnEffectExecute#5` | Calls Out | `Spell::GetUnitTarget` | Get target for Warrior's Wrath. |
| `OnEffectExecute#5` | Calls Out | `SpellCaster::CastSpell` | Cast secondary spell 21887. |
| `OnEffectExecute` | Calls Out | `Spell::GetUnitTarget` | Check target for Bloodrage. |
| `OnEffectExecute` | Calls Out | `Unit::SetInCombatState` | Force combat state on target. |
| `OnHolderInit` | Calls Out | `SpellAuraHolder::SetRemovedOnShapeLost` | Prevent Sweeping Strikes removal on shift. |
| `AddSC_...` | Calls Out | `Script::RegisterSelf` | Register scripts with the engine. |

## Data Model

This unit does not access any database tables. All data is sourced from runtime unit states and spell definitions.

## Notable Implementation Details

1.  **Execute Rage Split**: `WarriorExecuteDummyScript` calculates damage using current Rage but does not consume it. `WarriorExecuteDamageScript` consumes the Rage (sets to 0) when the damage spell executes. This ensures damage is calculated on the pre-consumption value.
2.  **Blood Fury Delay**: `WarriorBloodFuryAuraScript` uses a 1-tick delayed lambda to apply the AP debuff upon aura removal, working around an engine bug in `ExclusiveAuraUnapply`.
3.  **Client Build Guard**: Blood Fury's AP penalty logic is wrapped in `#if (SUPPORTED_CLIENT_BUILD > CLIENT_BUILD_1_3_1) && (SUPPORTED_CLIENT_BUILD <= CLIENT_BUILD_1_8_4)`, making it inactive for non-TBC clients.

## Member Reference

*   **`OnCheckTarget`**: Method in `WarriorIntimidatingShoutScript`. Excludes the main target from AoE fear by checking against `SpellCastTargetsInfo::getUnitTarget`.
*   **`GetScript_WarriorIntimidatingShout`**: Function. Factory for `WarriorIntimidatingShoutScript`.
*   **`OnEffectExecute#2`**: Method in `WarriorBloodthirstScript`. Calculates damage for Effect 0 using `Unit::GetTotalAttackPowerValue` and `Unit::GetTotalAuraModifierByMiscMask`.
*   **`GetScript_WarriorBloodthirst`**: Function. Factory for `WarriorBloodthirstScript`.
*   **`OnEffectExecute#4`**: Method in `WarriorShieldSlamScript`. Adds `Unit::GetShieldBlockValue` to damage for Effect 1.
*   **`GetScript_WarriorShieldSlam`**: Function. Factory for `WarriorShieldSlamScript`.
*   **`OnEffectExecute#3`**: Method in `WarriorExecuteDamageScript`. Sets caster Rage to 0 via `Unit::SetPower` for Effect 0.
*   **`GetScript_WarriorExecuteDamage`**: Function. Factory for `WarriorExecuteDamageScript`.
*   **`OnCast`**: Method in `WarriorExecuteDummyScript`. Calculates damage from Rage (`Unit::GetPower`, `shared_Util::dither`) and casts spell 20647 via `SpellCaster::CastCustomSpell`.
*   **`GetScript_WarriorExecuteDummy`**: Function. Factory for `WarriorExecuteDummyScript`.
*   **`OnEffectExecute#5`**: Method in `WarriorWrathScript`. Casts spell 21887 on target via `SpellCaster::CastSpell` for Effect 0.
*   **`GetScript_WarriorWrath`**: Function. Factory for `WarriorWrathScript`.
*   **`OnEffectExecute`**: Method in `WarriorBloodrageScript`. Forces target into combat via `Unit::SetInCombatState` for Effect 0.
*   **`GetScript_WarriorBloodrage`**: Function. Factory for `WarriorBloodrageScript`.
*   **`OnHolderInit`**: Method in `WarriorSweepingStrikesAuraScript`. Prevents aura removal on shapeshift via `SpellAuraHolder::SetRemovedOnShapeLost`.
*   **`GetScript_WarriorSweepingStrikes`**: Function. Factory for `WarriorSweepingStrikesAuraScript`.
*   **`OnBeforeApply`**: Method in `WarriorBloodFuryAuraScript`. Schedules delayed AP debuff on aura removal for TBC clients.
*   **`GetScript_WarriorBloodFury`**: Function. Factory for `WarriorBloodFuryAuraScript`.
*   **`AddSC_warrior_spell_scripts`**: Function. Registers all Warrior spell scripts via `Script::RegisterSelf`.

---

<!-- machine-true, projected from graph.json -->

## Map — spell_warrior

*Source:* spell_warrior.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| OnCheckTarget | method | SpellCastTargetsInfo/getUnitTarget | — | — |
| GetScript_WarriorIntimidatingShout | function | — | — | — |
| OnEffectExecute#2 | method | Spell.Main/GetUnitTarget, Unit.Main/GetCreatureTypeMask, Unit.Main/GetTotalAttackPowerValue, Unit.Main/GetTotalAuraModifierByMiscMask | — | — |
| GetScript_WarriorBloodthirst | function | — | — | — |
| OnEffectExecute#4 | method | Unit.Main/GetShieldBlockValue | — | — |
| GetScript_WarriorShieldSlam | function | — | — | — |
| OnEffectExecute#3 | method | Unit.Main/SetPower | — | — |
| GetScript_WarriorExecuteDamage | function | — | — | — |
| OnCast | method | shared_Util/dither, Spell.Main/GetUnitTarget, SpellCaster/CastCustomSpell#2, Unit.Main/GetPower | — | — |
| GetScript_WarriorExecuteDummy | function | — | — | — |
| OnEffectExecute#5 | method | Spell.Main/GetUnitTarget, SpellCaster/CastSpell#2 | — | — |
| GetScript_WarriorWrath | function | — | — | — |
| OnEffectExecute | method | Spell.Main/GetUnitTarget, Unit.Main/SetInCombatState | — | — |
| GetScript_WarriorBloodrage | function | — | — | — |
| OnHolderInit | method | SpellAuraHolder/SetRemovedOnShapeLost | — | — |
| GetScript_WarriorSweepingStrikes | function | — | — | — |
| OnBeforeApply | method | — | — | — |
| GetScript_WarriorBloodFury | function | — | — | — |
| AddSC_warrior_spell_scripts | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
