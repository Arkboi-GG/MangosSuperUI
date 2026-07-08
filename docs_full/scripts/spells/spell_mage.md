<!-- provenance: verbose -->
# spell_mage

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# spell_mage

**Purpose & Responsibilities**
`spell_mage.cpp` implements custom scripting logic for specific Mage class spells in the WoWVMaNGOS server emulator. It overrides default engine behavior for **Cold Snap** (cooldown reset), **Ignite** (damage-over-time stacking), and **Combustion** (critical strike chaining). The unit contains no database interactions; all logic is driven by in-memory spell definitions, aura states, and unit properties.

## Member-by-Member Behavior

### Cold Snap
**`MageColdSnapScript::OnEffectExecute`**
Executes when *Cold Snap* (12472) triggers.
1.  **Validation**: Ensures `effIdx` is `EFFECT_INDEX_0` and the caster is a `Player`.
2.  **Cooldown Reset**: Defines a lambda `cdCheck` filtering `SpellEntry` objects for spells that are:
    *   In `SPELLFAMILY_MAGE`.
    *   Have `SPELL_SCHOOL_MASK_FROST`.
    *   Have `GetRecoveryTime() > 0`.
3.  **Execution**: Calls `Player::RemoveSomeCooldown` with the filter to clear internal timers for matching spells.

**`GetScript_MageColdSnap`**
Factory function returning a new `MageColdSnapScript`.

### Ignite
**`MageIgniteScript::OnProc`**
Handles procs for *Ignite* talents (11119–12848).
1.  **Damage Calculation**: Determines base damage addition based on the triggering talent rank (4% to 20% of `originalAmount`). Logs an error if the spell ID is unrecognized.
2.  **Aura Management**: Checks for an existing *Ignite* DoT (`SPELL_DOT`) on the victim.
    *   **If present and not expired**: Adds damage to the tick amount if stacks < 5, increments stacks via `ModStackAmount(1)`, updates the modifier, and refreshes duration. If already at 5 stacks, it forces the count to 5 and refreshes duration.
    *   **If expired or absent**: Removes the old aura (if any) and triggers a new *Ignite* DoT via `TriggerProccedSpell`.

**`GetScript_MageIgnite`**
Factory function returning a new `MageIgniteScript`.

### Combustion (Build > 1.10.2)
*Compiled only for client builds newer than 1.10.2.*

**`MageCombustionProcScript::OnProc`**
Manages the invisible proc aura (`SPELL_COMBUSTION_PROC_AURA`, 11129).
1.  **Validation**: Fails if `pVictim` is null (e.g., AoE) or if the owner lacks the visible buff (`SPELL_COMBUSTION_CRIT_BUFF`). If the buff is missing, it cleans up the proc aura.
2.  **Charge Consumption**: If the proc aura has ≤1 charge remaining and the hit is a critical strike (`PROC_EX_CRITICAL_HIT`), it removes the visible buff and returns `OK` to consume the final charge.
3.  **Buff Application**: Otherwise, it casts the visible buff on the owner.
4.  **Return**: Returns `SPELL_AURA_PROC_OK` only on critical hits; non-crits return `FAILED` to avoid consuming charges.

**`GetScript_MageCombustionProc`**
Factory function returning a new `MageCombustionProcScript`.

**`MageCombustionBuffScript::OnAfterApply`**
Handles the visible *Combustion* buff (`SPELL_COMBUSTION_CRIT_BUFF`, 28682).
1.  **Removal Handling**: If the aura is removed (`!apply`) via cancellation (`AURA_REMOVE_BY_CANCEL`) on effect index 0, it removes the underlying proc aura from the target, initiating the talent's cooldown.

**`GetScript_MageCombustionBuff`**
Factory function returning a new `MageCombustionBuffScript`.

### Registration
**`AddSC_mage_spell_scripts`**
Registers all Mage spell scripts with `ScriptMgr`. Combustion scripts are conditionally registered based on `SUPPORTED_CLIENT_BUILD`.

## Cross-Unit Boundaries

*   **`OnEffectExecute` (MageColdSnapScript)**: Calls `Object::GetTypeId`, `SpellEntry::GetSpellSchoolMask`, `SpellEntry::GetRecoveryTime`, and `Player::RemoveSomeCooldown`.
*   **`OnProc` (MageIgniteScript)**: Calls `Aura::GetSpellProto`, `Aura::GetId`, `Aura::GetStackAmount`, `Aura::GetAuraTicks`, `Aura::GetAuraMaxTicks`, `Aura::GetModifier`, `Aura::GetHolder`, `Aura::GetCaster`, `Unit::GetAura`, `SpellAuraHolder::ModStackAmount`, `SpellAuraHolder::SetStackAmount`, `SpellAuraHolder::Refresh`, `Unit::ApplyModifier`, `Unit::RemoveAurasDueToSpell`, `Unit::TriggerProccedSpell`, and `Log::Main::Out`.
*   **`OnProc` (MageCombustionProcScript)**: Calls `Unit::HasAura`, `Aura::GetHolder`, `SpellAuraHolder::GetAuraCharges`, `Unit::RemoveAurasDueToSpell`, and `SpellCaster::CastSpell`.
*   **`OnAfterApply` (MageCombustionBuffScript)**: Calls `Aura::GetEffIndex`, `Aura::GetRemoveMode`, `Aura::GetTarget`, and `Unit::RemoveAurasDueToSpell`.
*   **`AddSC_mage_spell_scripts`**: Calls `Script::RegisterSelf`. Called by `ScriptLoader::AddScripts`.

## Data Model
This unit does not interact with any database tables.

## Notable Implementation Details

1.  **Ignite Stacking Cap**: `MageIgniteScript::OnProc` explicitly caps Ignite stacks at 5. If the aura is at max stacks, it calls `SetStackAmount(5)` to prevent overflow before refreshing duration.
2.  **Combustion Charge Logic**: `MageCombustionProcScript::OnProc` consumes the final charge only on a critical hit (`triggeredByAura->GetHolder()->GetAuraCharges() <= 1 && (procEx & PROC_EX_CRITICAL_HIT)`). Non-crits do not consume charges, preserving the buff until a crit occurs or the aura expires.
3.  **Client Build Dependency**: Combustion scripts are guarded by `#if SUPPORTED_CLIENT_BUILD > CLIENT_BUILD_1_10_2`, reflecting patch-specific mechanics.
4.  **Error Logging**: `MageIgniteScript::OnProc` logs an error via `Log::Main::Out` if an unrecognized spell ID triggers the proc, aiding debugging for missing talent ranks.

## Member Reference

**`OnEffectExecute`**
Method in `MageColdSnapScript`. Executes when Cold Snap is cast. Validates caster is a Player, then removes cooldowns for all Mage Frost spells with positive recovery times using a lambda filter passed to `Player::RemoveSomeCooldown`.

**`GetScript_MageColdSnap`**
Factory function. Returns a new `MageColdSnapScript` instance. Registered for spell 12472.

**`OnProc#2`**
Method in `MageIgniteScript`. Handles Ignite procs. Calculates damage addition based on talent rank (4%-20%). Checks for existing Ignite aura on victim. If present and not expired, stacks damage (up to 5) and refreshes duration. If expired or absent, removes old aura and triggers new Ignite DoT via `TriggerProccedSpell`. Logs error for unknown spell IDs.

**`GetScript_MageIgnite`**
Factory function. Returns a new `MageIgniteScript` instance. Registered for spells 11119, 11120, 12846, 12847, 12848.

**`OnProc`**
Method in `MageCombustionProcScript`. Handles Combustion proc aura. Fails if no victim or if visible buff is missing. If last charge and crit hit, removes visible buff. Otherwise, casts visible buff on owner. Returns OK only on crit hits to manage charge consumption.

**`GetScript_MageCombustionProc`**
Factory function. Returns a new `MageCombustionProcScript` instance. Registered for spell 11129. Compiled only for client builds > 1.10.2.

**`OnAfterApply`**
Method in `MageCombustionBuffScript`. Triggers when Combustion buff is applied/removed. If removed by cancellation, removes the underlying proc aura to start cooldown.

**`GetScript_MageCombustionBuff`**
Factory function. Returns a new `MageCombustionBuffScript` instance. Registered for spell 28682. Compiled only for client builds > 1.10.2.

**`AddSC_mage_spell_scripts`**
Function. Registers all Mage spell scripts (Cold Snap, Ignite, Combustion Proc, Combustion Buff) with the `ScriptMgr`. Combustion scripts are conditionally registered based on client build version.

---

<!-- machine-true, projected from graph.json -->

## Map — spell_mage

*Source:* spell_mage.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| OnEffectExecute | method | Object/GetTypeId, SpellEntry/GetRecoveryTime, SpellEntry/GetSpellSchoolMask | — | — |
| GetScript_MageColdSnap | function | — | — | — |
| OnProc#2 | method | Aura/GetAuraMaxTicks, Aura/GetAuraTicks, Aura/GetCaster, Aura/GetHolder, Aura/GetId, Aura/GetModifier, Aura/GetSpellProto, Aura/GetStackAmount, Log.Main/Out, Unit.AuraProcHandler/TriggerProccedSpell#2, Unit.Main/GetAura#2, Unit.Main/RemoveAurasDueToSpell, Unit.SpellAuras/ApplyModifier, Unit.SpellAuras/ModStackAmount, Unit.SpellAuras/Refresh#2, Unit.SpellAuras/SetStackAmount | — | — |
| GetScript_MageIgnite | function | — | — | — |
| OnProc | method | Aura/GetHolder, Aura/GetId, SpellAuraHolder/GetAuraCharges, SpellCaster/CastSpell#2, Unit.Main/HasAura#2, Unit.Main/RemoveAurasDueToSpell | — | — |
| GetScript_MageCombustionProc | function | — | — | — |
| OnAfterApply | method | Aura/GetEffIndex, Aura/GetRemoveMode, Aura/GetTarget, Unit.Main/RemoveAurasDueToSpell | — | — |
| GetScript_MageCombustionBuff | function | — | — | — |
| AddSC_mage_spell_scripts | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
