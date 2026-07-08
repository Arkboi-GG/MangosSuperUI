<!-- provenance: verbose -->
# spell_rogue

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`spell_rogue.cpp` implements custom server-side logic for two Rogue spells: **Eviscerate** and **Vanish**. It enforces Patch 1.12.0 mechanics that diverge from default spell behavior: Attack Power scaling for Eviscerate damage, and aura cleansing combined with forced stealth casting for Vanish. The unit defines two `SpellScript` subclasses, provides factory functions for their instantiation, and registers them with the global script manager.

## Member-by-Member Behavior

### Eviscerate Logic
**Eviscerate** is a finishing move whose damage scales with combo points. The custom logic handles the Patch 1.12.0 change where damage also scales with the caster's Attack Power.

*   **`OnEffectExecute` (in `RogueEviscerateScript`)**:
    *   Triggered on `EFFECT_INDEX_0` if a valid unit target exists.
    *   **Patch 1.12+ Specifics**: If `SUPPORTED_CLIENT_BUILD > CLIENT_BUILD_1_11_2` and the spell matches the Rogue Eviscerate family mask:
        1.  Casts the caster to `Player`.
        2.  Retrieves current **Combo Points**.
        3.  Calculates bonus damage: `Total Base Attack Power * Combo Points * 0.03`.
        4.  Adds this value to `spell->damage`.
    *   Returns `true` to allow normal spell processing.

*   **`GetScript_RogueEviscerate`**:
    *   Factory function returning a new `RogueEviscerateScript` instance.

### Vanish Logic
**Vanish** allows the Rogue to stealth and break movement impairments. The custom logic removes specific auras from the target (the Rogue) and forces the highest rank of Stealth.

*   **`OnEffectExecute#2` (in `RogueVanishScript`)**:
    *   Triggered on `EFFECT_INDEX_1` if a valid unit target exists.
    *   **Aura Removal**: Removes spells causing `SPELL_AURA_MOD_ROOT` and `SPELL_AURA_MOD_DECREASE_SPEED` from the target.
    *   **Patch 1.12+ Specifics**: If `SUPPORTED_CLIENT_BUILD > CLIENT_BUILD_1_11_2`, also removes `SPELL_AURA_MOD_STALKED` (e.g., Hunter's Mark).
    *   **Stealth Casting**: If the target is a `Player`, calls `CastHighestStealthRank()`.
    *   Returns `false` to suppress the default spell effect for this index, preventing duplicate stealth application.

*   **`GetScript_RogueVanish`**:
    *   Factory function returning a new `RogueVanishScript` instance.

### Registration
*   **`AddSC_rogue_spell_scripts`**:
    *   Creates and registers two scripts with the `ScriptMgr`:
        1.  `"spell_rogue_eviscerate"` linked to `GetScript_RogueEviscerate`.
        2.  `"spell_rogue_vanish"` linked to `GetScript_RogueVanish`.
    *   Called by `ScriptLoader::AddScripts` during server initialization.

## Cross-Unit Boundaries

### Eviscerate (`RogueEviscerateScript::OnEffectExecute`)
*   **Calls `Object::ToPlayer`**: Casts the spell caster to a `Player` object.
*   **Calls `Player::GetComboPoints`**: Retrieves the Rogue's current combo point count.
*   **Calls `Spell::GetUnitTarget`**: Verifies the spell has a valid target unit.
*   **Calls `Unit::GetTotalAttackPowerValue`**: Retrieves the base attack power for damage calculation.

### Vanish (`RogueVanishScript::OnEffectExecute#2`)
*   **Calls `Spell::GetUnitTarget`**: Identifies the target of Vanish (typically the Rogue).
*   **Calls `Unit::RemoveSpellsCausingAura`**: Removes roots, slows, and (Patch 1.12+) stalked auras from the target.
*   **Calls `Object::ToPlayer`**: Casts the target to a `Player` object.
*   **Calls `Player::CastHighestStealthRank`**: Forces the player to cast their highest learned Stealth rank.

### Registration (`AddSC_rogue_spell_scripts`)
*   **Calls `Script::RegisterSelf`**: Registers the script objects with the global manager.
*   **Called by `ScriptLoader::AddScripts`**: Invoked during server startup.

## Data Model

This unit does not interact with any database tables. All logic is performed in-memory using runtime spell and player data.

## Notable Implementation Details

1.  **Patch Version Gating**: Both scripts use `#if SUPPORTED_CLIENT_BUILD > CLIENT_BUILD_1_11_2` to conditionally compile Patch 1.12.0 mechanics (AP scaling for Eviscerate, Stalked aura removal for Vanish).
2.  **Vanish Return Value**: `RogueVanishScript::OnEffectExecute#2` returns `false` after execution. This suppresses the default `EFFECT_INDEX_1` effect, which is necessary because stealth is manually applied via `CastHighestStealthRank()`.
3.  **Eviscerate Formula**: Bonus damage is strictly `Base Attack Power * Combo Points * 0.03`, ignoring off-hand or other modifiers.

## Member Reference

**OnEffectExecute** (in `RogueEviscerateScript`): Executes on Eviscerate hit. If Patch 1.12+, adds `Base Attack Power * Combo Points * 0.03` to damage. Calls `Object::ToPlayer`, `Player::GetComboPoints`, `Spell::GetUnitTarget`, `Unit::GetTotalAttackPowerValue`.

**GetScript_RogueEviscerate**: Factory function returning a new `RogueEviscerateScript` instance.

**OnEffectExecute#2** (in `RogueVanishScript`): Executes on Vanish hit. Removes Root, Slow, and (Patch 1.12+) Stalked auras from target. If target is Player, casts highest Stealth rank. Returns `false` to suppress default effect. Calls `Object::ToPlayer`, `Player::CastHighestStealthRank`, `Spell::GetUnitTarget`, `Unit::RemoveSpellsCausingAura`.

**GetScript_RogueVanish**: Factory function returning a new `RogueVanishScript` instance.

**AddSC_rogue_spell_scripts**: Registers `spell_rogue_eviscerate` and `spell_rogue_vanish` with `ScriptMgr`. Calls `Script::RegisterSelf`. Called by `ScriptLoader::AddScripts`.

---

<!-- machine-true, projected from graph.json -->

## Map — spell_rogue

*Source:* spell_rogue.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| OnEffectExecute | method | Object/ToPlayer, Player.Main/GetComboPoints, Spell.Main/GetUnitTarget, Unit.Main/GetTotalAttackPowerValue | — | — |
| GetScript_RogueEviscerate | function | — | — | — |
| OnEffectExecute#2 | method | Object/ToPlayer, Player.Main/CastHighestStealthRank, Spell.Main/GetUnitTarget, Unit.Main/RemoveSpellsCausingAura | — | — |
| GetScript_RogueVanish | function | — | — | — |
| AddSC_rogue_spell_scripts | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
