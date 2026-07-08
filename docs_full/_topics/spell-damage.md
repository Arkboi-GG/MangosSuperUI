# Spell Damage

<!-- aliases: spell damage, make spell hit harder, nuke damage, spell power, base points, increase spell damage, spell coefficients, damage formula -->
<!-- documentation: model-written from source via the local LLM; review before trusting -->

Spell damage in VMaNGOS is not a single formula but a pipeline that starts with the raw dice roll defined in the database, passes through caster-side bonuses, applies target-side reductions, and finishes with armor and resistance calculations. The path differs slightly depending on whether the spell is magical (`SPELL_DAMAGE_CLASS_MAGIC`), melee/ranged physical (`SPELL_DAMAGE_CLASS_MELEE`/`RANGED`), or a periodic effect (DOT).

The lifecycle begins when a spell effect resolves. For direct school damage, `Spell.Effects/EffectSchoolDMG` adds the pre-calculated `damage` value to the spell's accumulator. That `damage` value was prepared earlier by `SpellCaster/CalculateSpellDamage`, which acts as the central dispatcher. It inspects `spellInfo->DmgClass` to decide the route:
1.  **Magic Spells:** Calls `SpellCaster/SpellDamageBonusDone` to apply caster buffs (spell power, % damage done), then `Unit.Main/SpellDamageBonusTaken` to apply target debuffs (damage taken modifiers). Finally, it applies armor reduction if the school includes physical damage.
2.  **Melee/Ranged Spells:** Calls `MeleeDamageBonusDone` and `Unit.Main/MeleeDamageBonusTaken` instead, which factor in attack power and melee-specific auras.
3.  **Weapon Damage Spells:** Handled by `Spell.Effects/EffectWeaponDmg`, which sums base weapon damage, normalized modifiers, and percentage multipliers before adding any fixed bonus damage.

**The Caster Bonus Pipeline (`SpellDamageBonusDone`)**
Inside `SpellCaster/SpellDamageBonusDone`, the server aggregates several sources of increased damage:
*   **Elite Scaling:** Creatures multiply damage by a rate fetched via `Creature.Main/_GetSpellDamageMod`, which reads config keys like `RATE_CREATURE_NORMAL_SPELLDAMAGE`.
*   **Percentage Auras:** Iterates `SPELL_AURA_MOD_DAMAGE_PERCENT_DONE` auras that match the spell's school mask (derived via `SpellEntry/GetSpellSchoolMask` and `SpellDefines/GetSchoolMask`).
*   **Creature Type Bonuses:** Adds flat and percentage bonuses against specific creature types (e.g., "Spell Damage vs Undead").
*   **Pet Happiness:** Hunter pets receive a 25% boost if `Pet.Main/GetHappinessState` returns `HAPPY`, or a 25% penalty if `UNHAPPY`.
*   **Spell Power Coefficients:** The core conversion of "Spell Power" stat into damage happens in `SpellCaster/SpellBonusWithCoeffs`. It retrieves the coefficient from `spellProto->EffectBonusCoefficient`. If negative (default), it calculates a dynamic coefficient based on spell level and applies a level penalty. The final bonus is `benefit * coeff * lvlPenalty`.

**The Target Reduction Pipeline (`SpellDamageBonusTaken`)**
On the receiving end, `Unit.Main/SpellDamageBonusTaken` reduces the incoming damage:
*   It applies `SPELL_AURA_MOD_DAMAGE_PERCENT_TAKEN` multipliers.
*   It applies flat damage reduction auras (`SPELL_AURA_MOD_DAMAGE_TAKEN`), capped at reducing damage by no more than 50% of the base value.
*   Like the caster side, it uses `SpellBonusWithCoeffs` to scale these reductions by the spell's coefficient.

**Periodic Damage (DOTs)**
For damage-over-time effects, `Unit.SpellAuras/PeriodicTick` handles the logic. It calculates the tick damage, applies specific class adjustments (e.g., Curse of Agony ramping), and then calls `SpellDamageBonusTaken` (for magic) or `MeleeDamageBonusTaken` (for physical) to finalize the number before dealing damage. Immunities are checked early via `Unit.Main/IsImmuneToDamage`.

**Special Cases**
*   **Paladins:** `spell_paladin/OnEffectExecute` manually invokes `SpellDamageBonusDone/Taken` for Hammer of Wrath and Judgement of Command to ensure they benefit from spell damage gear despite being hybrid abilities.
*   **Warlocks:** `spell_warlock/OnEffectExecute#5` (Life Tap) calculates self-damage using the same bonus pipeline to determine how much health is lost and thus how much mana is gained.
*   **Procs:** `Unit.AuraProcHandler/HandleProcTriggerDamageAuraProc` calculates damage for triggered effects (like Seal of Righteousness) by calling `SpellDamageBonusDone` and `SpellDamageBonusTaken` directly.

## How to Modify

### Config
No dedicated configuration keys exist in the provided CONFIG block for general spell damage coefficients or player spell power scaling. However, creature spell damage is tunable via the `RATE_CREATURE_*_SPELLDAMAGE` keys accessed in `Creature.Main/_GetSpellDamageMod`. Operators can adjust these to make mobs hit harder or softer with spells without rebuilding the code.

### Database
The primary surface for changing spell damage is the `spell_template` table.
*   **Base Damage:** Adjust `effectBasePoints1`, `effectBaseDice1`, and `effectDieSides1` to change the raw dice roll.
*   **Spell Power Scaling:** Edit `effectBonusCoefficient1` (and 2/3). A positive value (e.g., `1.000000`) sets a fixed coefficient. A negative value (default) triggers the dynamic calculation based on spell level. Increasing this value makes the spell scale better with spell power.
*   **School Mask:** Change the `school` column to alter which damage type the spell deals, affecting which resistances and bonuses apply.
*   **Custom Flags:** The `customFlags` column can disable certain modifiers (e.g., `SPELL_CUSTOM_FIXED_DAMAGE` bypasses `SpellDamageBonusDone`).

### Code
To change the fundamental damage formula, coefficient calculation, or how specific auras interact with damage:
*   **Coefficient Logic:** Edit `SpellCaster/SpellBonusWithCoeffs` in `SpellCaster.cpp` to change how spell power is converted to damage or how level penalties are applied.
*   **Bonus Aggregation:** Edit `SpellCaster/SpellDamageBonusDone` in `SpellCaster.cpp` to add new sources of damage increase (e.g., a new aura type or stat).
*   **Reduction Logic:** Edit `Unit.Main/SpellDamageBonusTaken` in `Unit.cpp` to change how targets mitigate damage.
*   **Specific Spells:** Edit the scripts in `spell_paladin.cpp` or `spell_warlock.cpp` to override behavior for specific abilities.
*   **Elite Scaling Rates:** While the keys are in config, the mapping of elite ranks to keys is hardcoded in `Creature.Main/_GetSpellDamageMod` in `Creature.cpp`.

## Path Reference

**ScriptMgr/LoadSpellScripts**
Unit: ScriptMgr, File: ScriptMgr.cpp
Validates that spells registered in `spell_scripts` have the correct `SPELL_EFFECT_SCRIPT_EFFECT` in `spell_template`, ensuring custom scripts attach to valid spells.

**ScriptMgr/CollectPossibleEventIds**
Unit: ScriptMgr, File: ScriptMgr.cpp
Scans `spell_template` for `effectMiscValue` references to `SPELL_EFFECT_SEND_EVENT`, building a map of valid event IDs for spell-triggered scripts.

**Spell.Effects/EffectSchoolDMG**
Unit: Spell, File: SpellEffects.cpp
The terminal effect handler for school damage; it simply adds the pre-calculated `damage` value to the spell's total damage accumulator if the target is alive.

**Spell.Effects/EffectWeaponDmg**
Unit: Spell, File: SpellEffects.cpp
Calculates weapon-based spell damage by summing base weapon swings, normalized bonuses, and percentage multipliers, then adds fixed bonus damage from other effects.

**SpellMgr/LoadExistingSpellIds**
Unit: SpellMgr, File: SpellMgr.cpp
Loads all unique spell entries from `spell_template` into a set for quick validation checks elsewhere in the server.

**SpellMgr/LoadSpells**
Unit: SpellMgr, File: SpellMgr.cpp
Loads the full `spell_template` data into memory, including localized names from `locales_spell`, forming the static definition for all spells.

**Unit.Main/SpellDamageBonusTaken**
Unit: Unit, File: Unit.cpp
Applies target-side damage reductions, including percentage taken auras and flat damage reduction, scaled by the spell's coefficient via `SpellBonusWithCoeffs`.

**Unit.Main/MeleeDamageBonusTaken**
Unit: Unit, File: Unit.cpp
Applies target-side reductions for melee/ranged damage, handling attack power-based reductions and melee-specific percentage auras.

**Creature.Main/HasWeapon**
Unit: Creature, File: Creature.cpp
Checks if a creature has a weapon equipped, used to determine if weapon-based spell effects should apply or fall back to static damage.

**Spell.Effects/EffectPowerDrain**
Unit: Spell, File: SpellEffects.cpp
Handles power drain spells by calculating damage via `SpellDamageBonusDone/Taken` and transferring the drained power to the caster.

**Spell.Effects/EffectHealthLeech**
Unit: Spell, File: SpellEffects.cpp
Calculates damage via `SpellDamageBonusDone/Taken`, caps it at the target's health, and heals the caster for a portion of the damage dealt.

**SpellCaster/GetMeleeDamageSchoolMask**
Unit: SpellCaster, File: SpellCaster.cpp
Returns the `SPELL_SCHOOL_MASK_NORMAL` mask, used to identify physical damage schools for melee-based calculations.

**SpellCaster/CalculateSpellDamage**
Unit: SpellCaster, File: SpellCaster.cpp
The central dispatcher that routes damage calculation to either `SpellDamageBonusDone` (magic) or `MeleeDamageBonusDone` (physical) and applies armor reduction.

**SpellCaster/SpellDamageBonusDone**
Unit: SpellCaster, File: SpellCaster.cpp
Aggregates all caster-side damage increases, including elite scaling, percentage auras, creature type bonuses, pet happiness, and spell power coefficients.

**SpellCaster/SpellBonusWithCoeffs**
Unit: SpellCaster, File: SpellCaster.cpp
Converts raw spell power/bonus stats into final damage values by applying the spell's coefficient and level penalty.

**SpellDefines/GetSchoolMask**
Unit: SpellDefines, File: SpellDefines.h
Inline helper that converts a numeric school index into a bitmask, essential for matching auras to spell schools.

**SpellEntry/HasEffect**
Unit: SpellEntry, File: SpellEntry.h
Checks if a spell contains a specific effect type, used to determine damage class and behavior.

**SpellEntry/GetSpellSchoolMask**
Unit: SpellEntry, File: SpellEntry.h
Retrieves the school mask for a spell, used to filter applicable auras and immunities.

**spell_paladin/OnEffectExecute**
Unit: spell_paladin, File: spell_paladin.cpp
Custom script for Hammer of Wrath that manually applies `SpellDamageBonusDone/Taken` to ensure it benefits from spell damage gear.

**spell_paladin/OnEffectExecute#3**
Unit: spell_paladin, File: spell_paladin.cpp
Custom script for Judgement of Command that halves damage if the target isn't stunned, then applies standard damage bonuses.

**spell_warlock/OnCheckCast#3**
Unit: spell_warlock, File: spell_warlock.cpp
Pre-cast check for Life Tap that calculates potential self-damage using the bonus pipeline to prevent casting if health is insufficient.

**spell_warlock/OnEffectExecute#5**
Unit: spell_warlock, File: spell_warlock.cpp
Executes Life Tap by dealing calculated self-damage and granting mana, applying Improved Life Tap talent bonuses.

**Unit.AuraProcHandler/HandleDummyAuraProc**
Unit: Unit, File: UnitAuraProcHandler.cpp
Handles complex dummy aura procs like Sweeping Strikes and Eye for an Eye, calculating triggered damage amounts based on original hits.

**Unit.AuraProcHandler/HandleProcTriggerDamageAuraProc**
Unit: Unit, File: UnitAuraProcHandler.cpp
Processes triggered damage auras (e.g., Seal of Righteousness) by calculating damage via `SpellDamageBonusDone/Taken` and dealing it directly.

**Unit.Main/CalculateDamage**
Unit: Unit, File: Unit.cpp
Calculates raw weapon damage ranges for melee/ranged attacks, used as the base for weapon-based spell effects.

**Unit.Main/IsImmuneToDamage**
Unit: Unit, File: Unit.cpp
Checks if a target is immune to a specific school of damage, preventing damage calculation if immunity applies.

**Unit.Main/GetModifierValue**
Unit: Unit, File: Unit.cpp
Retrieves accumulated modifier values for unit stats, used to apply percentage-based damage modifiers.

**Unit.SpellAuras/PeriodicTick**
Unit: Unit, File: SpellAuras.cpp
Handles the tick logic for DOTs, calculating damage, applying class-specific ramps, and invoking `SpellDamageBonusTaken` for mitigation.

**Creature.Main/_GetSpellDamageMod**
Unit: Creature, File: Creature.cpp
Returns the spell damage multiplier for a creature based on its elite rank, reading from configuration rates.

**game_Objects_Item/IsFitToSpellRequirements**
Unit: Item, File: Item.cpp
Checks if an item meets the class/subclass/inventory type requirements specified in a spell's template.

**Object/IsPet**
Unit: Object, File: Pet.h
Determines if an object is a pet, used to apply pet-specific damage modifiers like happiness.

**Object/ToUnit**
Unit: Object, File: Unit.h
Casts an object to a Unit pointer, enabling access to unit-level damage methods.

**Pet.Main/GetHappinessState**
Unit: Pet, File: Pet.cpp
Returns the happiness state of a hunter pet, which directly modifies its spell damage output.

**Player.Main/GetWeaponForAttack#2**
Unit: Player, File: Player.cpp
Retrieves the weapon in a specific slot, used to check for weapon-based spell requirements and modifiers.

**Player.StatSystem/UpdateDamagePhysical**
Unit: Player, File: StatSystem.cpp
Updates physical damage ranges based on attack power and modifiers, underlying the base damage for melee spells.

**Spell.Main/CheckItems**
Unit: Spell, File: Spell.cpp
Validates item requirements for casting, including checking if the caster has the required weapon equipped for weapon-based spells.

**SpellEntry/GetAuraMaxTicks**
Unit: SpellEntry, File: SpellEntry.cpp
Calculates the maximum number of ticks for a DOT, used in damage calculations for effects like Moonfire.

**Unit.Main/GetCreatureTypeMask**
Unit: Unit, File: Unit.h
Generates a bitmask for the unit's creature type, used to apply type-specific damage bonuses.

**Unit.Main/GetOwner**
Unit: Unit, File: Unit.cpp
Retrieves the owner of a unit (e.g., pet owner), used to redirect damage bonuses to the player for pets and totems.

**Unit.Main/GetSpellModOwner**
Unit: Unit, File: Unit.cpp
Identifies the player responsible for spell modifications, allowing spellmod auras to apply to pets and totems.

---

<!-- machine-true, projected from graph.json -->

## Map — Spell Damage

*Source:* ScriptMgr.cpp, SpellEffects.cpp, SpellMgr.cpp, Unit.cpp, Creature.cpp, SpellCaster.cpp, SpellDefines.h, SpellEntry.h, spell_paladin.cpp, spell_warlock.cpp, UnitAuraProcHandler.cpp, SpellAuras.cpp, Item.cpp, Pet.h, Unit.h, Pet.cpp, Player.cpp, StatSystem.cpp, Spell.cpp, SpellEntry.cpp
*Config keys:* —
*Tables:* spell_template

| Member | Kind | Source | Role |
|---|---|---|---|
| ScriptMgr/LoadSpellScripts | method | ScriptMgr.cpp:1500-1549 | seed — queries spell_template |
| ScriptMgr/CollectPossibleEventIds | method | ScriptMgr.cpp:2465-2568 | seed — queries spell_template |
| Spell.Effects/EffectSchoolDMG | method | SpellEffects.cpp:300-307 | seed — Spell.Effects/*SchoolDmg* |
| Spell.Effects/EffectWeaponDmg | method | SpellEffects.cpp:3323-3427 | seed — Spell.Effects/*WeaponDmg* |
| SpellMgr/LoadExistingSpellIds | method | SpellMgr.cpp:3101-3117 | seed — queries spell_template |
| SpellMgr/LoadSpells | method | SpellMgr.cpp:3679-3771 | seed — queries spell_template |
| Unit.Main/SpellDamageBonusTaken | method | Unit.cpp:5424-5447 | seed — Unit.*/SpellDamageBonus* |
| Unit.Main/MeleeDamageBonusTaken | method | Unit.cpp:5918-5991 | seed — Unit.*/MeleeDamageBonus* |
| Creature.Main/HasWeapon | method | Creature.cpp:4192-4195 | related — 1 hop from a seed |
| Spell.Effects/EffectPowerDrain | method | SpellEffects.cpp:1647-1708 | related — 1 hop from a seed |
| Spell.Effects/EffectHealthLeech | method | SpellEffects.cpp:1800-1831 | related — 1 hop from a seed |
| SpellCaster/GetMeleeDamageSchoolMask | method | SpellCaster.cpp:853-856 | related — 1 hop from a seed |
| SpellCaster/CalculateSpellDamage | method | SpellCaster.cpp:966-1026 | related — 1 hop from a seed |
| SpellCaster/SpellDamageBonusDone | method | SpellCaster.cpp:1297-1438 | related — 1 hop from a seed |
| SpellCaster/SpellBonusWithCoeffs | method | SpellCaster.cpp:1474-1509 | related — 1 hop from a seed |
| SpellDefines/GetSchoolMask | function | SpellDefines.h:766-769 | related — 1 hop from a seed |
| SpellEntry/HasEffect | method | SpellEntry.h:751-757 | related — 1 hop from a seed |
| SpellEntry/GetSpellSchoolMask | method | SpellEntry.h:1178-1181 | related — 1 hop from a seed |
| spell_paladin/OnEffectExecute | method | spell_paladin.cpp:22-31 | related — 1 hop from a seed |
| spell_paladin/OnEffectExecute#3 | method | spell_paladin.cpp:42-54 | related — 1 hop from a seed |
| spell_warlock/OnCheckCast#3 | method | spell_warlock.cpp:115-130 | related — 1 hop from a seed |
| spell_warlock/OnEffectExecute#5 | method | spell_warlock.cpp:132-165 | related — 1 hop from a seed |
| Unit.AuraProcHandler/HandleDummyAuraProc | method | UnitAuraProcHandler.cpp:550-1145 | related — 1 hop from a seed |
| Unit.AuraProcHandler/HandleProcTriggerDamageAuraProc | method | UnitAuraProcHandler.cpp:1626-1676 | related — 1 hop from a seed |
| Unit.Main/CalculateDamage | method | Unit.cpp:2429-2472 | related — 1 hop from a seed |
| Unit.Main/IsImmuneToDamage | method | Unit.cpp:5646-5681 | related — 1 hop from a seed |
| Unit.Main/GetModifierValue | method | Unit.cpp:8083-8095 | related — 1 hop from a seed |
| Unit.SpellAuras/PeriodicTick | method | SpellAuras.cpp:5751-6348 | related — 1 hop from a seed |
| Creature.Main/_GetSpellDamageMod | method | Creature.cpp:1823-1840 | related — 2 hops from a seed |
| game_Objects_Item/IsFitToSpellRequirements | method | Item.cpp:998-1002 | related — 2 hops from a seed |
| Object/IsPet | method | Pet.h:289-292 | related — 2 hops from a seed |
| Object/ToUnit | method | Unit.h:1453-1456 | related — 2 hops from a seed |
| Pet.Main/GetHappinessState | method | Pet.cpp:869-876 | related — 2 hops from a seed |
| Player.Main/GetWeaponForAttack#2 | method | Player.cpp:8647-8676 | related — 2 hops from a seed |
| Player.StatSystem/UpdateDamagePhysical | method | StatSystem.cpp:873-948 | related — 2 hops from a seed |
| Spell.Main/CheckItems | method | Spell.cpp:7076-7456 | related — 2 hops from a seed |
| SpellEntry/GetAuraMaxTicks | method | SpellEntry.cpp:754-778 | related — 2 hops from a seed |
| Unit.Main/GetCreatureTypeMask | method | Unit.h:529-533 | related — 2 hops from a seed |
| Unit.Main/GetOwner | method | Unit.cpp:4999-5004 | related — 2 hops from a seed |
| Unit.Main/GetSpellModOwner | method | Unit.cpp:9210-9225 | related — 2 hops from a seed |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `gameobject_template`: entry mediumint(8) unsigned PK, patch tinyint(3) unsigned PK, type tinyint(3) unsigned, displayId mediumint(8) unsigned, name varchar(100), icon varchar(100), faction smallint(5) unsigned, flags int(10) unsigned, size float, data0 int(10), data1 int(11), data2 int(10), data3 int(10), data4 int(10), data5 int(10), data6 int(11), data7 int(10), data8 int(10), data9 int(10), data10 int(10), data11 int(10), data12 int(10), data13 int(10), data14 int(10), data15 int(10), data16 int(10), data17 int(10), data18 int(10), data19 int(10), data20 int(10), data21 int(10), data22 int(10), data23 int(10), mingold mediumint(8) unsigned, maxgold mediumint(8) unsigned, script_name varchar(64)
- `locales_spell`: entry smallint(5) unsigned PK, name_loc1 varchar(256), name_loc2 varchar(256), name_loc3 varchar(256), name_loc4 varchar(256), name_loc5 varchar(256), name_loc6 varchar(256), nameSubtext_loc1 varchar(256), nameSubtext_loc2 varchar(256), nameSubtext_loc3 varchar(256), nameSubtext_loc4 varchar(256), nameSubtext_loc5 varchar(256), nameSubtext_loc6 varchar(256), description_loc1 varchar(1024), description_loc2 varchar(1024), description_loc3 varchar(1024), description_loc4 varchar(1024), description_loc5 varchar(1024), description_loc6 varchar(1024), auraDescription_loc1 varchar(512), auraDescription_loc2 varchar(512), auraDescription_loc3 varchar(512), auraDescription_loc4 varchar(512), auraDescription_loc5 varchar(512), auraDescription_loc6 varchar(512)
- `spell_template`: entry mediumint(8) unsigned PK, build smallint(4) unsigned PK, school int(4) unsigned, category int(4) unsigned, castUI int(4) unsigned, dispel int(4) unsigned, mechanic int(4) unsigned, attributes int(4) unsigned, attributesEx int(4) unsigned, attributesEx2 int(4) unsigned, attributesEx3 int(4) unsigned, attributesEx4 int(4) unsigned, stances int(4) unsigned, stancesNot int(4) unsigned, targets int(4) unsigned, targetCreatureType int(4) unsigned, requiresSpellFocus int(4) unsigned, casterAuraState int(4) unsigned, targetAuraState int(4) unsigned, castingTimeIndex int(4) unsigned, recoveryTime int(4) unsigned, categoryRecoveryTime int(4) unsigned, interruptFlags int(4) unsigned, auraInterruptFlags int(4) unsigned, channelInterruptFlags int(4) unsigned, procFlags int(4) unsigned, procChance int(4) unsigned, procCharges int(4) unsigned, maxLevel int(4) unsigned, baseLevel int(4) unsigned, spellLevel int(4) unsigned, durationIndex int(4) unsigned, powerType int(4) unsigned, manaCost int(4) unsigned, manCostPerLevel int(4) unsigned, manaPerSecond int(4) unsigned, manaPerSecondPerLevel int(4) unsigned, rangeIndex int(4) unsigned, speed float, modelNextSpell int(4) unsigned, stackAmount int(4) unsigned, totem1 int(4) unsigned, totem2 int(4) unsigned, reagent1 int(4), reagent2 int(4), reagent3 int(4), reagent4 int(4), reagent5 int(4), reagent6 int(4), reagent7 int(4), reagent8 int(4), reagentCount1 int(4) unsigned, reagentCount2 int(4) unsigned, reagentCount3 int(4) unsigned, reagentCount4 int(4) unsigned, reagentCount5 int(4) unsigned, reagentCount6 int(4) unsigned, reagentCount7 int(4) unsigned, reagentCount8 int(4) unsigned, equippedItemClass int(4), equippedItemSubClassMask int(4), equippedItemInventoryTypeMask int(4), effect1 int(4) unsigned, effect2 int(4) unsigned, effect3 int(4) unsigned, effectDieSides1 int(4), effectDieSides2 int(4), effectDieSides3 int(4), effectBaseDice1 int(4) unsigned, effectBaseDice2 int(4) unsigned, effectBaseDice3 int(4) unsigned, effectDicePerLevel1 float, effectDicePerLevel2 float, effectDicePerLevel3 float, effectRealPointsPerLevel1 float, effectRealPointsPerLevel2 float, effectRealPointsPerLevel3 float, effectBasePoints1 int(4), effectBasePoints2 int(4), effectBasePoints3 int(4), effectBonusCoefficient1 float, effectBonusCoefficient2 float, effectBonusCoefficient3 float, effectMechanic1 int(4) unsigned, effectMechanic2 int(4) unsigned, effectMechanic3 int(4) unsigned, effectImplicitTargetA1 int(4) unsigned, effectImplicitTargetA2 int(4) unsigned, effectImplicitTargetA3 int(4) unsigned, effectImplicitTargetB1 int(4) unsigned, effectImplicitTargetB2 int(4) unsigned, effectImplicitTargetB3 int(4) unsigned, effectRadiusIndex1 int(4) unsigned, effectRadiusIndex2 int(4) unsigned, effectRadiusIndex3 int(4) unsigned, effectApplyAuraName1 int(4) unsigned, effectApplyAuraName2 int(4) unsigned, effectApplyAuraName3 int(4) unsigned, effectAmplitude1 int(4) unsigned, effectAmplitude2 int(4) unsigned, effectAmplitude3 int(4) unsigned, effectMultipleValue1 float, effectMultipleValue2 float, effectMultipleValue3 float, effectChainTarget1 int(4) unsigned, effectChainTarget2 int(4) unsigned, effectChainTarget3 int(4) unsigned, effectItemType1 bigint(20) unsigned, effectItemType2 bigint(20) unsigned, effectItemType3 bigint(20) unsigned, effectMiscValue1 int(4), effectMiscValue2 int(4), effectMiscValue3 int(4), effectTriggerSpell1 int(4) unsigned, effectTriggerSpell2 int(4) unsigned, effectTriggerSpell3 int(4) unsigned, effectPointsPerComboPoint1 float, effectPointsPerComboPoint2 float, effectPointsPerComboPoint3 float, spellVisual1 int(4) unsigned, spellVisual2 int(4) unsigned, spellIconId int(4) unsigned, activeIconId int(4) unsigned, spellPriority int(4) unsigned, name varchar(256), nameFlags int(4) unsigned, nameSubtext varchar(256), nameSubtextFlags int(4) unsigned, description varchar(1024), descriptionFlags int(4) unsigned, auraDescription varchar(512), auraDescriptionFlags int(4) unsigned, manaCostPercentage int(4) unsigned, startRecoveryCategory int(4) unsigned, startRecoveryTime int(4) unsigned, minTargetLevel int(4) unsigned, maxTargetLevel int(4) unsigned, spellFamilyName int(4) unsigned, spellFamilyFlags bigint(20) unsigned, maxAffectedTargets int(4) unsigned, dmgClass int(4) unsigned, preventionType int(4) unsigned, stanceBarOrder int(4), dmgMultiplier1 float, dmgMultiplier2 float, dmgMultiplier3 float, minFactionId int(4) unsigned, minReputation int(4) unsigned, requiredAuraVision int(4) unsigned, customFlags int(10) unsigned, script_name varchar(64)

*`?` = nullable, `PK` = primary key column.*

