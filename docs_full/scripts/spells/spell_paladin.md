# spell_paladin

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`spell_paladin.cpp` implements custom logic for Paladin spells that cannot be fully defined by static database entries. It registers eight `SpellScript` subclasses to handle specific mechanics: applying melee damage bonuses to `Hammer of Wrath`, conditionally halving `Judgement of Command` damage based on stun status, chaining judgment spells, enforcing facing requirements and dual-nature (heal/harm) logic for `Holy Shock`, modifying `Judgement of Light` procs for Tier 3 gear, capping `Reckoning` extra attacks, and applying the `Forbearance` debuff after protective bubbles. The unit contains no database interactions.

## Member-by-Member Behavior

### Hammer of Wrath
**Members:** `PaladinHammerOfWrathScript::OnEffectExecute`, `GetScript_PaladinHammerOfWrath`

*   **`OnEffectExecute`**: Forces the spell to treat its damage as a `BASE_ATTACK` melee hit, enabling melee critical strikes. It then applies standard damage modifiers by calling `SpellDamageBonusDone` on the caster and `SpellDamageBonusTaken` on the target.
*   **`GetScript_PaladinHammerOfWrath`**: Factory function returning a new `PaladinHammerOfWrathScript` instance.

### Judgement of Command
**Members:** `PaladinJudgementOfCommandDamageScript::OnEffectExecute`, `GetScript_PaladinJudgementOfCommandDamage`, `PaladinJudgementOfCommandDummyScript::OnEffectExecute`, `GetScript_PaladinJudgementOfCommandDummy`

*   **`PaladinJudgementOfCommandDamageScript::OnEffectExecute`**: Checks if the target is stunned (`UNIT_STATE_STUNNED` or `UNIT_STATE_PENDING_STUNNED`). If not stunned, it halves the base damage (`* 0.5f`). It then applies standard damage bonus modifiers via `SpellDamageBonusDone` and `SpellDamageBonusTaken`.
*   **`PaladinJudgementOfCommandDummyScript::OnEffectExecute`**: Retrieves the spell ID from `m_currentBasePoints[effIdx]`, looks up the `SpellEntry` via `SpellMgr::GetSpellEntry`, and casts that spell on the target using `SpellCaster::CastSpell`. This decouples the judgment trigger from the specific judgment spell.

### Holy Shock
**Members:** `PaladinHolyShockScript::OnCheckCast`, `PaladinHolyShockScript::OnEffectExecute`, `GetScript_PaladinHolyShock`

*   **`OnCheckCast`**: Validates the cast. It fails with `SPELL_FAILED_UNIT_NOT_INFRONT` if the target is not friendly AND the caster is not facing the target. This enforces the mechanic that Holy Shock requires facing enemies but can be cast on friends from any angle.
*   **`OnEffectExecute`**: Determines whether to heal or damage based on the relationship between caster and target. It uses a `switch` statement on the spell ID (`20473`, `20929`, `20930`) to identify the corresponding "hurt" (damage) and "heal" spell IDs. If the target is friendly, it casts the heal spell; otherwise, it casts the hurt spell. If the spell ID is unrecognized, it logs an error via `Log::Out` and returns `false`.

### Judgement of Light
**Members:** `PaladinJudgementOfLightProcAuraScript::OnEffectExecute`, `GetScript_PaladinJudgementOfLightProcAura`, `PaladinJudgementOfLightHealScript::OnEffectExecute`, `GetScript_PaladinJudgementOfLightHeal`

*   **`PaladinJudgementOfLightProcAuraScript::OnEffectExecute`**: Checks if the caster has the "Paladin T3" set bonus aura (`28775`). If present, it sets the effect's base points to `20`, modifying the proc value.
*   **`PaladinJudgementOfLightHealScript::OnEffectExecute`**: Checks `m_triggeredByAuraBasePoints`; if greater than zero, it adds this value to the spell's damage (representing healing). This allows triggered heals from auras to augment the base heal amount.

### Reckoning
**Members:** `PaladinReckoningScript::OnEffectExecute`, `GetScript_PaladinReckoning`

*   **`OnEffectExecute`**: Implements the Reckoning talent logic, granting extra attacks.
    *   For clients `<= 1.2.4`, it resets the attack timer (`ResetAttackTimer`) to fix a bug where extra attacks caused subsequent swings to take too long.
    *   For clients `> 1.4.2`, it caps extra attacks at 4 (`GetExtraAttacks() < 4`) to prevent infinite stacking.
    *   It calls `AddExtraAttack()` on the target if the cap is not reached and returns `false` to suppress default behavior.

### Protective Bubbles
**Members:** `PaladinBubbleScript::OnAfterHit`, `GetScript_PaladinBubble`

*   **`OnAfterHit`**: Triggers after the spell hits. It casts `SPELL_FORBEARANCE` (`25771`) on the target, applying a debuff that prevents using other protective bubble spells for a duration.

### Registration
**Member:** `AddSC_paladin_spell_scripts`

*   Registers all scripts with `ScriptMgr` by creating `Script` objects, assigning names, linking factory functions, and calling `RegisterSelf`. Called by `ScriptLoader::AddScripts`.

## Cross-Unit Boundaries

*   **Spell/Main**: `OnEffectExecute` methods use `GetUnitTarget` to retrieve targets and access `m_spellInfo`/`m_currentBasePoints`.
*   **SpellCaster**: `SpellDamageBonusDone` applies caster-side damage modifiers; `CastSpell` triggers secondary spells.
*   **Unit/Main**: `HasUnitState`/`HasAura` check states; `SpellDamageBonusTaken` applies target-side modifiers; `AddExtraAttack`/`GetExtraAttacks`/`ResetAttackTimer` manage attack mechanics.
*   **WorldObject/Object**: `IsFacingTarget` and `IsFriendlyTo` validate cast conditions for `Holy Shock`.
*   **SpellMgr**: `GetSpellEntry` looks up spell data for `Judgement of Command`.
*   **Log/Main**: `Out` logs errors for unhandled `Holy Shock` spell IDs.
*   **Script/ScriptMgr**: `RegisterSelf` registers scripts during initialization.

## Data Model

This unit does not interact with any database tables. All spell IDs, aura IDs, and logic constants are hardcoded.

## Notable Implementation Details

1.  **Hardcoded Spell IDs**: Logic relies on hardcoded IDs (e.g., `25771` for Forbearance). Changes in game versions may require updates.
2.  **Client Version Conditionals**: `Reckoning` uses `#if SUPPORTED_CLIENT_BUILD` to adjust timer resets and attack caps based on client version.
3.  **Damage Halving**: `Judgement of Command` halves damage if the target is *not* stunned, reflecting specific game mechanics.
4.  **Holy Shock Dual Nature**: `Holy Shock` switches between healing and damaging spells based on target friendship, determined at execution time.
5.  **Forbearance Application**: `PaladinBubbleScript` applies Forbearance via `OnAfterHit`, ensuring the debuff is applied regardless of other spell effects.
6.  **Error Handling**: Unrecognized `Holy Shock` spell IDs log an error and return `false`, potentially causing the spell to fizzle.

## Member Reference

**OnEffectExecute** (PaladinHammerOfWrathScript): Sets attack type to BASE_ATTACK and applies damage bonuses for Hammer of Wrath.
**GetScript_PaladinHammerOfWrath**: Factory function for PaladinHammerOfWrathScript.
**OnEffectExecute#3** (PaladinJudgementOfCommandDamageScript): Halves damage if target is not stunned, then applies damage bonuses.
**GetScript_PaladinJudgementOfCommandDamage**: Factory function for PaladinJudgementOfCommandDamageScript.
**OnEffectExecute#4** (PaladinJudgementOfCommandDummyScript): Retrieves spell ID from base points, looks up SpellEntry, and casts the judgment spell.
**GetScript_PaladinJudgementOfCommandDummy**: Factory function for PaladinJudgementOfCommandDummyScript.
**OnCheckCast** (PaladinHolyShockScript): Validates cast, failing if target is hostile and not in front.
**OnEffectExecute#2** (PaladinHolyShockScript): Determines heal/hurt spell based on spell ID and target friendship, casting the appropriate spell.
**GetScript_PaladinHolyShock**: Factory function for PaladinHolyShockScript.
**OnEffectExecute#6** (PaladinJudgementOfLightProcAuraScript): Checks for T3 aura and sets base points to 20 if present.
**GetScript_PaladinJudgementOfLightProcAura**: Factory function for PaladinJudgementOfLightProcAuraScript.
**OnEffectExecute#5** (PaladinJudgementOfLightHealScript): Adds triggered aura base points to heal amount.
**GetScript_PaladinJudgementOfLightHeal**: Factory function for PaladinJudgementOfLightHealScript.
**OnEffectExecute#7** (PaladinReckoningScript): Manages extra attacks, resetting timers for old clients and capping stacks for newer ones.
**GetScript_PaladinReckoning**: Factory function for PaladinReckoningScript.
**OnAfterHit** (PaladinBubbleScript): Casts Forbearance on the target after bubble spells hit.
**GetScript_PaladinBubble**: Factory function for PaladinBubbleScript.
**AddSC_paladin_spell_scripts**: Registers all Paladin spell scripts with the ScriptMgr.

---

<!-- machine-true, projected from graph.json -->

## Map — spell_paladin

*Source:* spell_paladin.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| OnEffectExecute | method | Spell.Main/GetUnitTarget, SpellCaster/SpellDamageBonusDone, Unit.Main/SpellDamageBonusTaken | — | — |
| GetScript_PaladinHammerOfWrath | function | — | — | — |
| OnEffectExecute#3 | method | Spell.Main/GetUnitTarget, SpellCaster/SpellDamageBonusDone, Unit.Main/HasUnitState, Unit.Main/SpellDamageBonusTaken | — | — |
| GetScript_PaladinJudgementOfCommandDamage | function | — | — | — |
| OnEffectExecute#4 | method | Spell.Main/GetUnitTarget, SpellCaster/CastSpell, SpellMgr/GetSpellEntry, SpellMgr/Instance | — | — |
| GetScript_PaladinJudgementOfCommandDummy | function | — | — | — |
| OnCheckCast | method | SpellCastTargetsInfo/getUnitTarget, WorldObject.Object/IsFacingTarget, WorldObject.Object/IsFriendlyTo | — | — |
| OnEffectExecute#2 | method | Log.Main/Out, Spell.Main/GetUnitTarget, SpellCaster/CastSpell#2, WorldObject.Object/IsFriendlyTo | — | — |
| GetScript_PaladinHolyShock | function | — | — | — |
| OnEffectExecute#6 | method | Unit.Main/HasAura#2 | — | — |
| GetScript_PaladinJudgementOfLightProcAura | function | — | — | — |
| OnEffectExecute#5 | method | — | — | — |
| GetScript_PaladinJudgementOfLightHeal | function | — | — | — |
| OnEffectExecute#7 | method | Spell.Main/GetUnitTarget, Unit.Main/AddExtraAttack, Unit.Main/GetExtraAttacks | — | — |
| GetScript_PaladinReckoning | function | — | — | — |
| OnAfterHit | method | Spell.Main/GetUnitTarget, SpellCaster/CastSpell#2 | — | — |
| GetScript_PaladinBubble | function | — | — | — |
| AddSC_paladin_spell_scripts | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
