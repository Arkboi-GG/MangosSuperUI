<!-- provenance: verbose -->
# spell_hunter

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# spell_hunter

**Purpose & Responsibilities**  
`spell_hunter.cpp` implements custom logic for six Hunter-specific spells and one Hunter-specific aura. It provides targeted overrides for spell casting checks, effect execution, and aura lifecycle events to match expected game mechanics. The unit registers these scripts with the global script manager via `AddSC_hunter_spell_scripts`.

**Member-by-Member Behavior**  

1. **HunterWyvernStingScript** (`OnAfterApply`)  
   - Triggered after Wyvern Sting aura application/removal.  
   - Acts only on removal (`!apply`) of effect index 0.  
   - Verifies the caster is a player.  
   - Maps base Wyvern Sting spell IDs (19386, 24132, 24133) to finisher spell IDs (24131, 24134, 24135).  
   - Casts the finisher on the original target using the aura as the trigger source.  
   - Logs an error for unknown Wyvern Sting ranks.

2. **HunterReadinessScript** (`OnEffectExecute`)  
   - Triggered when Readiness’s first effect executes.  
   - If the caster is a player, removes cooldowns for all Hunter-family spells with `GetRecoveryTime() > 0`, excluding Readiness (ID 23989).

3. **HunterRefocusScript** (`OnEffectExecute#2`)  
   - Triggered when Refocus’s first effect executes.  
   - If the caster is a player, removes cooldowns for Arcane Shot, Multishot, Volley, and Aimed Shot, excluding Refocus itself.  
   - Filters by specific family flags (`CF_HUNTER_*`) and requires `GetRecoveryTime() > 0`.

4. **HunterMongooseBiteScript** (`OnCheckCast`)  
   - Validates cast targets for Mongoose Bite.  
   - Fails the cast if the target is not the caster’s current reactive defense target (`REACTIVE_DEFENSE`).

5. **HunterCounterAttackScript** (`OnCheckCast#2`)  
   - Validates cast targets for Counterattack.  
   - Fails the cast if the target is not the caster’s current reactive parry target (`REACTIVE_HUNTER_PARRY`).

6. **HunterFrostTrapAuraScript** (`OnPeriodicTrigger`)  
   - Triggered periodically while the Frost Trap aura is active on a target.  
   - Procs damage and spell effects for the trap caster, enabling talents like Entrapment.  
   - Uses `PROC_FLAG_ON_TRAP_ACTIVATION` to indicate the proc context.

**Cross-Unit Boundaries**  

- **`OnAfterApply` (HunterWyvernStingScript)**  
  - Calls `Aura::GetCaster`, `Aura::GetEffIndex`, `Aura::GetId`, `Aura::GetTarget` to inspect aura state.  
  - Calls `Object::GetTypeId` to verify the caster is a player.  
  - Calls `SpellCaster::CastSpell` to cast the finisher spell.  
  - Calls `Log::Main::Out` to log errors for unknown spell ranks.

- **`OnEffectExecute` (HunterReadinessScript) & `OnEffectExecute#2` (HunterRefocusScript)**  
  - Calls `Object::ToPlayer` to safely cast the caster to a `Player*`.  
  - Calls `SpellEntry::GetRecoveryTime` to filter spells with active cooldowns.

- **`OnCheckCast` (HunterMongooseBiteScript) & `OnCheckCast#2` (HunterCounterAttackScript)**  
  - Calls `ObjectGuid::operator!=` to compare target GUIDs.  
  - Calls `SpellCastTargetsInfo::getUnitTargetGuid` to retrieve the intended target.  
  - Calls `Unit::Main::GetReactiveTarget` to fetch the current defensive/reactive target.

- **`OnPeriodicTrigger` (HunterFrostTrapAuraScript)**  
  - Calls `Aura::GetCaster` and `Aura::GetSpellProto` to access caster and spell data.  
  - Calls `SpellCaster::ProcDamageAndSpell` and `SpellCaster::ProcSystemArguments` to trigger proc-based effects.

- **`AddSC_hunter_spell_scripts`**  
  - Calls `Script::Script` and `Script::RegisterSelf` to register each script.  
  - Is called by `ScriptLoader::AddScripts` during server initialization.

**Data Model**  
This unit does not interact with any database tables. All logic is driven by in-memory spell definitions, aura states, and player/cooldown data.

**Notable Implementation Details**  

- **Wyvern Sting Finisher Logic**: The finisher spell ID is determined by a `switch` on the base spell ID. If a new Wyvern Sting rank is added to the database without updating this switch, the spell will fail silently (after logging an error) and no finisher will be cast. This is a maintenance risk.

- **Readiness vs. Refocus Cooldown Removal**:  
  - `Readiness` uses a broad filter: all Hunter-family spells with `GetRecoveryTime() > 0`.  
  - `Refocus` uses a narrow filter: only four specific spells, identified by family flags (`CF_HUNTER_ARCANE_SHOT`, etc.).  
  - Both exclude themselves by ID.

- **Target Validation for Mongoose Bite and Counterattack**:  
  - Both spells require the target to match a specific reactive target (`REACTIVE_DEFENSE` or `REACTIVE_HUNTER_PARRY`).  
  - If the caster has no such reactive target, `GetReactiveTarget` likely returns an invalid GUID, causing the cast to fail. This enforces the requirement that these spells can only be used against attackers.

- **Frost Trap Proc Context**:  
  - The proc is triggered with `PROC_FLAG_ON_TRAP_ACTIVATION`, which may enable special talent effects (e.g., rooting). The actual proc chance and effects are determined by the spell proto and talent system, not hardcoded here.

- **Error Handling**:  
  - `HunterWyvernStingScript::OnAfterApply` logs an error for unknown spell IDs but does not crash. This is safe but relies on manual updates for new spell ranks.

## Member Reference

**OnAfterApply**  
Method of `HunterWyvernStingScript`. Triggered after Wyvern Sting aura application/removal. On removal of effect 0, verifies caster is a player, maps base spell ID to finisher spell ID, and casts the finisher on the target. Logs an error for unknown spell IDs. Calls `Aura::GetCaster`, `Aura::GetEffIndex`, `Aura::GetId`, `Aura::GetTarget`, `Object::GetTypeId`, `SpellCaster::CastSpell`, and `Log::Main::Out`.

**GetScript_HunterWyvernSting**  
Factory function returning a new `HunterWyvernStingScript` instance. No external calls.

**OnEffectExecute**  
Method of `HunterReadinessScript`. Triggered on execution of Readiness’s first effect. If caster is a player, removes cooldowns for all Hunter-family spells with recovery time > 0, excluding Readiness itself. Calls `Object::ToPlayer` and `SpellEntry::GetRecoveryTime`.

**GetScript_HunterReadiness**  
Factory function returning a new `HunterReadinessScript` instance. No external calls.

**OnEffectExecute#2**  
Method of `HunterRefocusScript`. Triggered on execution of Refocus’s first effect. If caster is a player, removes cooldowns for Arcane Shot, Multishot, Volley, and Aimed Shot, excluding Refocus itself. Calls `Object::ToPlayer` and `SpellEntry::GetRecoveryTime`.

**GetScript_HunterRefocus**  
Factory function returning a new `HunterRefocusScript` instance. No external calls.

**OnCheckCast#2**  
Method of `HunterCounterAttackScript`. Validates cast target for Counterattack. Fails if target is not the caster’s reactive parry target. Calls `ObjectGuid::operator!=`, `SpellCastTargetsInfo::getUnitTargetGuid`, and `Unit::Main::GetReactiveTarget`.

**GetScript_HunterMongooseBite**  
Factory function returning a new `HunterMongooseBiteScript` instance. No external calls.

**OnCheckCast**  
Method of `HunterMongooseBiteScript`. Validates cast target for Mongoose Bite. Fails if target is not the caster’s reactive defense target. Calls `ObjectGuid::operator!=`, `SpellCastTargetsInfo::getUnitTargetGuid`, and `Unit::Main::GetReactiveTarget`.

**GetScript_HunterCounterAttack**  
Factory function returning a new `HunterCounterAttackScript` instance. No external calls.

**OnPeriodicTrigger**  
Method of `HunterFrostTrapAuraScript`. Triggered periodically while Frost Trap aura is active. Procs damage and spell effects for the caster, enabling talents like Entrapment. Calls `Aura::GetCaster`, `Aura::GetSpellProto`, `SpellCaster::ProcDamageAndSpell`, and `SpellCaster::ProcSystemArguments`.

**GetScript_HunterFrostTrapAura**  
Factory function returning a new `HunterFrostTrapAuraScript` instance. No external calls.

**AddSC_hunter_spell_scripts**  
Registration function. Creates and registers six script objects (Wyvern Sting, Readiness, Refocus, Mongoose Bite, Counterattack, Frost Trap Aura) with the script manager. Called by `ScriptLoader::AddScripts`. Calls `Script::Script` and `Script::RegisterSelf`.

---

<!-- machine-true, projected from graph.json -->

## Map — spell_hunter

*Source:* spell_hunter.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| OnAfterApply | method | Aura/GetCaster, Aura/GetEffIndex, Aura/GetId, Aura/GetTarget, Log.Main/Out, Object/GetTypeId, SpellCaster/CastSpell#2 | — | — |
| GetScript_HunterWyvernSting | function | — | — | — |
| OnEffectExecute | method | Object/ToPlayer, SpellEntry/GetRecoveryTime | — | — |
| GetScript_HunterReadiness | function | — | — | — |
| OnEffectExecute#2 | method | Object/ToPlayer, SpellEntry/GetRecoveryTime | — | — |
| GetScript_HunterRefocus | function | — | — | — |
| OnCheckCast#2 | method | ObjectGuid/operator!=, SpellCastTargetsInfo/getUnitTargetGuid, Unit.Main/GetReactiveTarget | — | — |
| GetScript_HunterMongooseBite | function | — | — | — |
| OnCheckCast | method | ObjectGuid/operator!=, SpellCastTargetsInfo/getUnitTargetGuid, Unit.Main/GetReactiveTarget | — | — |
| GetScript_HunterCounterAttack | function | — | — | — |
| OnPeriodicTrigger | method | Aura/GetCaster, Aura/GetSpellProto, SpellCaster/ProcDamageAndSpell, SpellCaster/ProcSystemArguments | — | — |
| GetScript_HunterFrostTrapAura | function | — | — | — |
| AddSC_hunter_spell_scripts | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
