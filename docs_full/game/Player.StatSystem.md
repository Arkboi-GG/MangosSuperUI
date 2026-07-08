<!-- provenance: no-member-reference-section, failed-members, boundary-bleed -->
# Player.StatSystem

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Player.StatSystem

## Purpose & Responsibilities

The `Player.StatSystem` unit implements the core statistical calculation engine for player characters in the WoWVMaNGOS emulator. It is responsible for translating raw character attributes (Strength, Agility, Stamina, Intellect, Spirit) and external modifiers (from items, auras, talents, and skills) into derived combat statistics such as Health, Mana, Armor, Resistances, Attack Power, Damage ranges, and critical hit/dodge/parry/block percentages.

This system operates on the principle of layered modification: base values are modified by percentage and flat bonuses from various sources, then converted into final integer or floating-point values sent to the client. It handles specific class mechanics, such as Druid shapeshifting forms affecting damage and attack power, Hunter pet happiness influencing damage, and class-specific base dodge/crit values.

The unit is divided into three logical sections in the source code:
1.  **Players Stat System:** Logic specific to `Player` objects.
2.  **Mobs Stat System:** Logic specific to `Creature` objects (non-pet NPCs).
3.  **Pets Stat System:** Logic specific to `Pet` objects (summons, hunter pets).

While the MAP labels these as distinct units (`Player.StatSystem`, `Creature.Main`, `Pet.Main`), the source file `StatSystem.cpp` contains implementations for all three. This documentation focuses strictly on the members listed in the `Player.StatSystem` MAP, which correspond to the `Player` class methods. However, because `Player` inherits from `Unit` and interacts heavily with `Creature` and `Pet` logic via inheritance or direct calls, the context of those interactions is described where relevant.

## Member-by-Member Behavior

### Core Stat Updates

**UpdateStats**
Calculates the final value for a single primary stat (`STAT_STRENGTH` through `STAT_SPIRIT`). It retrieves the total modified value using `Unit.Main/GetTotalStatValue`, sets the internal stat value via `Unit.Main/SetStat`, and then triggers cascading updates for derived stats.
-   **Agility:** Triggers `UpdateArmor`, `UpdateAllCritPercentages`, and `UpdateDodgePercentage`.
-   **Stamina:** Triggers `UpdateMaxHealth`.
-   **Intellect:** Triggers `UpdateMaxPower` (for Mana), `UpdateAllSpellCritChances`, and `UpdateArmor` (due to potential intellect-based resistance auras).
-   **General:** Always triggers `UpdateAttackPowerAndDamage` (melee and ranged), `UpdateSpellDamageAndHealingBonus`, and `UpdateManaRegen`.

**UpdateAllStats**
Iterates through all primary stats (Strength to Spirit), calling `UpdateStats` for each. It then performs a comprehensive refresh of all derived combat capabilities:
-   Updates Melee and Ranged Attack Power and Damage.
-   Updates Max Health and all Power types (Mana, Rage, Focus, Energy, Happy).
-   Updates Crit Percentages, Spell Crit Chances, Defense Bonuses, Spell Damage/Healing Bonuses, and Mana Regen.
-   Updates Resistances for all spell schools.

**UpdateStats#2**
A variant of `UpdateStats` likely used in specific contexts or older code paths (as indicated by the `#2` suffix in the MAP, though the source shows only one `Player::UpdateStats`). Based on the MAP, it calls `Unit.Main/GetTotalStatValue`, `Unit.Main/SetStat`, and `Unit.Main/UpdateAllSpellCritChances`. *Note: In the provided source, `Player::UpdateStats` is the sole implementation. The MAP distinction may refer to overloaded versions or virtual overrides in derived classes not explicitly separated in this snippet, or potentially `Creature::UpdateStats` which has similar logic. Given the MAP specifies `Player.StatSystem`, we treat this as the primary `Player::UpdateStats` behavior.*

**UpdateAllStats#3**
Another variant listed in the MAP. The source code shows `Player::UpdateAllStats` being called by `Player.Main/GiveLevel`, `Player.Main/LoadFromDB`, and `PlayerBotAI/SpawnNewPlayer`. This confirms `UpdateAllStats` is the central recalculation hub invoked during leveling, loading, and spawning.

### Health and Power

**GetHealthBonusFromStamina**
A static helper function that calculates the health bonus provided by Stamina.
-   Formula: `baseStam + (moreStam * 10.0f)` where `baseStam` is capped at 20.
-   Effectively: First 20 Stamina give 1 HP each. Additional Stamina gives 10 HP each.

**GetManaBonusFromIntellect**
A static helper function that calculates the mana bonus provided by Intellect.
-   Formula: `baseInt + (moreInt * 15.0f)` where `baseInt` is capped at 20.
-   Effectively: First 20 Intellect give 1 Mana each. Additional Intellect gives 15 Mana each.

**UpdateMaxHealth**
Calculates the player's maximum health.
1.  Starts with base modifiers (`BASE_VALUE`) and creation health (`GetCreateHealth`).
2.  Applies base percentage modifiers.
3.  Adds total flat modifiers and the health bonus from Stamina (`GetHealthBonusFromStamina`).
4.  Applies total percentage modifiers.
5.  Sets the result via `Unit.Main/SetMaxHealth`, ensuring a minimum of 1.

**UpdateMaxPower**
Calculates the maximum power for a specific power type (e.g., Mana).
1.  Starts with base modifiers and creation power (`GetCreatePowers`).
2.  If the power type is Mana and the player has mana, adds the intellect bonus (`GetManaBonusFromIntellect`).
3.  Applies percentage and flat modifiers.
4.  Sets the result via `Unit.Main/SetMaxPower`.

**UpdateManaRegen**
Calculates mana regeneration rates.
1.  Gets base spirit-based regen (`GetRegenMPPerSpirit`).
2.  Applies percentage multipliers from `SPELL_AURA_MOD_POWER_REGEN_PERCENT`.
3.  Adds flat mana regen from `SPELL_AURA_MOD_POWER_REGEN` (converted from MP5 to per-tick).
4.  Calculates `m_modManaRegenInterrupt` (regen while casting), applying a penalty modifier (`SPELL_AURA_MOD_MANA_REGEN_INTERRUPT`) to the spirit-based portion only.
5.  Calculates `m_modManaRegen` (total regen out of combat).

### Armor and Resistances

**UpdateArmor**
Calculates total armor.
1.  Computes dynamic armor from Agility: `Agility * 2.0f`.
2.  Iterates through auras of type `SPELL_AURA_MOD_RESISTANCE_OF_STAT_PERCENT`. If an aura affects Normal school (armor), it adds `Intellect * (aura_amount * 0.01f)` to the dynamic value.
3.  Temporarily adds this dynamic value to the total armor modifier group.
4.  Retrieves the total resistance value for Normal school (which represents Armor).
5.  Sets the armor via `Unit.Main/SetArmor`.
6.  Removes the dynamic value from the modifier group to prevent double-counting in future calculations.

**UpdateResistances**
Updates resistance for a specific spell school.
-   If the school is Holy, resistance is forced to 0.
-   For other magical schools, it gets the total resistance value and sets it.
-   If the school is Normal (0), it delegates to `UpdateArmor`.

**UpdateResistances#3**
Same as above, referenced in the MAP as being called by no external units in this specific partial, but logically part of the resistance update chain.

### Attack Power and Damage

**GetAttackPowerFromStrengthAndAgility**
A static method (defined in `Unit` but implemented here for `Player` context in the MAP, though source shows it as `Unit::GetAttackPowerFromStrengthAndAgility` called by `Player` methods) that calculates raw Attack Power based on Strength and Agility.
-   **Ranged AP:**
    -   Hunters: `Level * 2 + Agility * 2 - 10`.
    -   Rogues/Warriors: `Level + Agility - 10`.
    -   Druids: 0 in Bear/Cat forms, `Agility - 10` otherwise.
    -   Others: `Agility - 10`.
-   **Melee AP:**
    -   Warriors/Paladins: `Level * 3 + Strength * 2 - 20`.
    -   Rogues/Hunters: `Level * 2 + Strength + Agility - 20`.
    -   Shamans: `Level * 2 + Strength * 2 - 20`.
    -   Druids: Complex logic involving `Predatory Strikes` talent (Spell Icon ID 1563). In Cat/Bear forms, AP scales with Level multiplied by the talent rank. Otherwise, `Strength * 2 - 20`.
    -   Mages/Priests/Warlocks: `Strength - 10`.

**UpdateAttackPowerAndDamage**
Updates the displayed Attack Power and recalculates weapon damage.
1.  Determines indices for Melee or Ranged AP fields.
2.  Calculates base AP using `GetAttackPowerFromStrengthAndAgility`.
3.  Retrieves positive and negative flat AP modifiers.
4.  Sets the AP fields in the object data.
5.  Sets AP multiplier fields (for clients > 1.8.4).
6.  Calls `UpdateDamagePhysical` for the relevant attack types (Base, Off-hand if dual-wielding, Ranged).

**CalculateMinMaxDamage**
Calculates the minimum and maximum damage range for a specific weapon attack type.
1.  Determines the unit modifier group for the attack type.
2.  Calculates attack speed multiplier.
3.  Computes base damage value incorporating Total Attack Power and attack speed.
4.  Retrieves weapon damage ranges from the equipped item.
5.  **Shapeshift Handling:** If the player is in a shapeshift form that doesn't use weapons (e.g., Bear/Cat):
    -   If damage index > 0, weapon damage is zeroed.
    -   Otherwise, calculates form-based damage based on Level and attack speed.
    -   Removes weapon enchant benefits (`total_value = 0`).
6.  **Broken Weapon Handling:** If the player cannot use the equipped weapon, sets damage to base minimum/maximum constants.
7.  **Ammo Handling:** Adds Ammo DPS to ranged damage.
8.  Applies all modifiers (base %, total flat, physical flat, total %) to derive final min/max damage.

**UpdateDamagePhysical**
Calls `CalculateMinMaxDamage` and sets the resulting min/max damage values into the appropriate object fields (`UNIT_FIELD_MINDAMAGE`, `UNIT_FIELD_MAXDAMAGE`, etc.).

**GetWeaponBasedAuraModifier**
Calculates the sum of aura modifiers that depend on the equipped weapon.
1.  Retrieves the weapon for the attack type.
2.  Iterates through auras of the specified type.
3.  Checks if the aura requires a specific item class/subclass/inventory type.
4.  If the weapon fits the requirements, adds the aura's modifier amount.

### Defense and Critical Hits

**UpdateDefenseBonusesMod**
A coordinator method that calls `UpdateBlockPercentage`, `UpdateParryPercentage`, and `UpdateDodgePercentage`.

**UpdateBlockPercentage**
Calculates block chance.
1.  Checks if the player can block (`CanBlock`).
2.  Base value: 5.0%.
3.  Adds bonus from Defense Skill exceeding max for level: `(DefenseSkill - MaxSkill) * 0.04`.
4.  Adds flat bonuses from `SPELL_AURA_MOD_BLOCK_PERCENT`.
5.  Clamps to minimum 0.0.
6.  Sets the value in `PLAYER_BLOCK_PERCENTAGE`.

**UpdateCritPercentage**
Calculates critical strike chance for Melee or Ranged attacks.
1.  Retrieves total percentage modifiers for the attack type.
2.  Adds class-specific base crit values (e.g., Mage +3.2%, Warlock +2.0%).
3.  Adds bonus from Weapon Skill exceeding max for level: `(WeaponSkill - MaxSkill) * 0.04`.
4.  Clamps to minimum 0.0.
5.  Sets the value in the appropriate player field.

**UpdateAllCritPercentages**
1.  Calculates melee crit from Agility (`GetMeleeCritFromAgility`).
2.  Sets this value as the base percentage modifier for both Melee and Ranged crit.
3.  Calls `UpdateCritPercentage` for both attack types.

**UpdateParryPercentage**
Calculates parry chance.
1.  Checks if the player can parry (`CanParry`).
2.  Base value: 5.0%.
3.  Adds bonus from Defense Skill exceeding max for level.
4.  Adds weapon-based aura modifiers (`GetWeaponBasedAuraModifier`).
5.  Clamps to minimum 0.0.
6.  Sets the value in `PLAYER_PARRY_PERCENTAGE`.

**UpdateDodgePercentage**
Calculates dodge chance.
1.  Adds class-specific base dodge values (same as crit).
2.  Adds dodge from Agility (`GetDodgeFromAgility`).
3.  Adds bonus from Defense Skill exceeding max for level.
4.  Adds flat bonuses from `SPELL_AURA_MOD_DODGE_PERCENT`.
5.  Clamps to minimum 0.0.
6.  Sets the value in `PLAYER_DODGE_PERCENTAGE`.

### Spell Damage and Healing

**UpdateSpellDamageAndHealingBonus**
Updates the client-side fields for spell damage and healing bonuses.
1.  Iterates through all spell schools (Holy to Nature).
2.  Calculates the base damage bonus done for each school using `SpellCaster/SpellBaseDamageBonusDone`.
3.  Sets the value in `PLAYER_FIELD_MOD_DAMAGE_DONE_POS` for the corresponding school.
*Note: The comment indicates this is primarily for client-side display, as actual magic damage modifiers are handled in `Unit::SpellDamageBonusDone`.*

### Internal State Management

**_ApplyAllStatBonuses**
Prepares the player for stat recalculation.
1.  Disables stat modification checks (`SetCanModifyStats(false)`).
2.  Applies all aura mods (`Unit.Main/_ApplyAllAuraMods`).
3.  Applies all item mods (`Player.Main/_ApplyAllItemMods`).
4.  Re-enables stat modification checks.
5.  Calls `UpdateAllStats`.

**_RemoveAllStatBonuses**
Removes all temporary stat bonuses.
1.  Disables stat modification checks.
2.  Removes all item mods (`Player.Main/_RemoveAllItemMods`).
3.  Removes all aura mods (`Unit.Main/_RemoveAllAuraMods`).
4.  Re-enables stat modification checks.
5.  Calls `UpdateAllStats`.

## Cross-Unit Boundaries

### Calls Out (Dependencies)

*   **Unit.Main:** Heavily relied upon for low-level stat retrieval (`GetTotalStatValue`, `GetStat`, `GetTotalResistanceValue`, `GetCreateHealth`, `GetCreatePowers`, `GetModifierValue`, `GetTotalAuraModifier`, `GetTotalAuraMultiplierByMiscValue`, `GetRegenMPPerSpirit`, `GetAttackPowerModifierValue`, `GetTotalAttackPowerValue`, `GetWeaponDamageRange`, `GetAurasByType`, `GetClass`, `GetLevel`, `GetShapeshiftForm`, `CanUseEquippedWeapon`, `IsAttackSpeedOverridenShapeShift`) and setting derived values (`SetStat`, `SetMaxHealth`, `SetMaxPower`, `SetArmor`, `SetResistance`, `UpdateAllSpellCritChances`).
*   **SpellCaster:** Used for calculating spell damage bonuses (`SpellBaseDamageBonusDone`) and retrieving skill/defense values (`GetSchoolMask`, `GetDefenseSkillValue`, `GetSkillMaxForLevel`, `GetWeaponSkillValue`, `GetAPMultiplier`).
*   **WorldObject.Object:** Used to set the final calculated values into the object's data fields for network transmission (`SetStatInt32Value`, `SetInt16Value`, `SetFloatValue`, `SetInt32Value`, `SetStatFloatValue`).
*   **Aura:** Used to inspect active auras for weapon-dependent modifiers (`GetModifier`, `GetSpellProto`).
*   **game_Objects_Item:** Used to verify if an equipped weapon meets spell requirements for aura applicability (`IsFitToSpellRequirements`).
*   **Player.Main:** Used for player-specific checks and data (`CanDualWield`, `HaveOffhandWeapon`, `GetWeaponForAttack`, `GetAmmoDPS`, `CanBlock`, `CanParry`, `GetMeleeCritFromAgility`, `GetDodgeFromAgility`, `SetBaseModValue`, `_ApplyAllItemMods`, `_RemoveAllItemMods`).

### Called By (Integration Points)

*   **Player.Main:** Invokes `UpdateAllStats` during leveling (`GiveLevel`), loading (`LoadFromDB`), and saving (`SaveNewPlayer` calls `GetHealthBonusFromStamina`/`GetManaBonusFromIntellect`). `UpdateMaxHealth` and `UpdateMaxPower` are called during `Create`. `UpdateDamagePhysical` is called when applying/removing ammo and item bonuses. `UpdateCritPercentage` is called when handling base mod values. `UpdateDefenseBonusesMod` is called when updating combat skills. `UpdateBlockPercentage`, `UpdateParryPercentage` are called when setting block/parry capabilities.
*   **Unit.SpellAuras:** Invokes `UpdateSpellDamageAndHealingBonus` when handling healing/damage percent mods from stats. `UpdateAttackPowerAndDamage` is called when handling dummy auras. `UpdateBlockPercentage`, `UpdateParryPercentage`, `UpdateDodgePercentage` are called when handling respective aura mods. `UpdateManaRegen` is called when handling mana regen interrupt auras.
*   **PlayerBotAI:** Invokes `UpdateAllStats` when spawning a new player bot.
*   **ChatHandler.UnitCommands:** Invokes `GetWeaponBasedAuraModifier` for the `.unit statinfo` command.

## Data Model

This unit does not directly interact with database tables. It operates entirely on in-memory object states (`Player`, `Unit`, `Aura`, `Item`). The `Tables` column in the MAP is empty for all members.

## Notable Implementation Details

1.  **Druid Shapeshift Complexity:** The `GetAttackPowerFromStrengthAndAgility` and `CalculateMinMaxDamage` methods contain significant logic for Druids. In `GetAttackPowerFromStrengthAndAgility`, the code checks for the "Predatory Strikes" talent by iterating through `SPELL_AURA_DUMMY` auras and checking for Spell Icon ID 1563. This allows AP to scale with level in Cat/Bear forms if the talent is learned. In `CalculateMinMaxDamage`, if a Druid is in a form that doesn't use weapons, weapon damage is replaced by level-based formulas, and weapon enchantments are ignored.
2.  **Armor Calculation Quirk:** `UpdateArmor` temporarily modifies the `m_auraModifiersGroup[UNIT_MOD_ARMOR][TOTAL_VALUE]` by adding the dynamic agility/intellect component, retrieves the total, sets the armor, and then subtracts the dynamic component. This suggests that the aura modifier system might otherwise double-count or incorrectly aggregate these dynamic stats if they were permanently added.
3.  **Client Version Checks:** The code uses `#if SUPPORTED_CLIENT_BUILD > CLIENT_BUILD_1_8_4` to conditionally set Attack Power Multiplier fields. This ensures compatibility with older clients that do not support these fields.
4.  **Stamina/Intellect Scaling:** The `GetHealthBonusFromStamina` and `GetManaBonusFromIntellect` functions implement a non-linear scaling curve. The first 20 points provide a 1:1 ratio, while subsequent points provide a 1:10 (HP) or 1:15 (Mana) ratio. This is a classic Vanilla WoW mechanic.
5.  **Pet Happiness:** While `Pet::UpdateDamagePhysical` is in the `Pet` section of the source, it is notable that Hunter pets' damage is scaled by their happiness state (Happy: 125%, Content: 100%, Unhappy: 75%). This logic is encapsulated within the `Pet` class methods, not the `Player` class, but is part of the overall stat system.
6.  **Disarm Effect on Creatures:** In `Creature::UpdateDamagePhysical`, if a creature has a weapon but cannot use it (disarmed), the total percentage modifier is multiplied by 0.4, effectively reducing damage by 60%. This is a specific mob mechanic not present in the player logic.
7.  **Holy Resistance:** Both `Player::UpdateResistances` and `Creature::UpdateResistances` force Holy resistance to 0. This reflects the game mechanic where Holy resistance is generally not applicable or calculable in the same way as other schools for most entities.

---

<!-- machine-true, projected from graph.json -->

## Map — Player.StatSystem

*Source:* StatSystem.cpp, Player.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| UpdateStats#2 | method | Unit.Main/GetTotalStatValue, Unit.Main/SetStat, Unit.Main/UpdateAllSpellCritChances | — | — |
| UpdateSpellDamageAndHealingBonus | method | SpellCaster/SpellBaseDamageBonusDone, SpellDefines/GetSchoolMask, WorldObject.Object/SetStatInt32Value | Unit.SpellAuras/HandleModHealingDone, Unit.SpellAuras/HandleModSpellDamagePercentFromStat, Unit.SpellAuras/HandleModSpellHealingPercentFromStat | — |
| UpdateAllStats#3 | method | Unit.Main/GetTotalStatValue, Unit.Main/SetStat, Unit.Main/UpdateAllSpellCritChances | Player.Main/GiveLevel, Player.Main/LoadFromDB, PlayerBotAI/SpawnNewPlayer | — |
| UpdateResistances#3 | method | Unit.Main/GetTotalResistanceValue, Unit.Main/SetResistance | — | — |
| UpdateArmor#3 | method | Aura/GetModifier, Unit.Main/GetAurasByType, Unit.Main/GetStat, Unit.Main/GetTotalResistanceValue, Unit.Main/SetArmor | — | — |
| GetHealthBonusFromStamina | method | — | Player.Main/GiveLevel, Player.Main/SaveNewPlayer | — |
| GetManaBonusFromIntellect | method | — | Player.Main/GiveLevel, Player.Main/SaveNewPlayer | — |
| UpdateMaxHealth#2 | method | Unit.Main/GetCreateHealth, Unit.Main/GetModifierValue, Unit.Main/GetStat, Unit.Main/SetMaxHealth | Player.Main/Create | — |
| UpdateMaxPower#2 | method | Unit.Main/GetCreatePowers, Unit.Main/GetModifierValue, Unit.Main/GetStat, Unit.Main/SetMaxPower | Player.Main/Create | — |
| GetAttackPowerFromStrengthAndAgility | method | Aura/GetModifier, Aura/GetSpellProto, Unit.Main/GetAurasByType, Unit.Main/GetClass, Unit.Main/GetLevel, Unit.Main/GetShapeshiftForm | — | — |
| UpdateAttackPowerAndDamage#3 | method | Object/SetInt16Value, Player.Main/CanDualWield, Unit.Main/GetAttackPowerModifierValue, Unit.Main/GetStat, Unit.Main/HaveOffhandWeapon, WorldObject.Object/SetFloatValue, WorldObject.Object/SetInt32Value | Player.Main/InitDataForForm, Unit.SpellAuras/HandleAuraDummy | — |
| CalculateMinMaxDamage | method | Player.Main/GetAmmoDPS, SpellCaster/GetAPMultiplier, Unit.Main/CanUseEquippedWeapon, Unit.Main/GetLevel, Unit.Main/GetModifierValue, Unit.Main/GetShapeshiftForm, Unit.Main/GetTotalAttackPowerValue, Unit.Main/GetTotalAuraModValue, Unit.Main/GetWeaponDamageRange, Unit.Main/IsAttackSpeedOverridenShapeShift | Unit.Main/CalculateDamage | — |
| UpdateDamagePhysical#3 | method | WorldObject.Object/SetStatFloatValue | Player.Main/RemoveAmmo, Player.Main/_ApplyAmmoBonuses, Player.Main/_ApplyItemBonuses | — |
| GetWeaponBasedAuraModifier#2 | method | Aura/GetModifier, Aura/GetSpellProto, game_Objects_Item/IsFitToSpellRequirements, Player.Main/GetWeaponForAttack, Unit.Main/GetAurasByType | ChatHandler.UnitCommands/HandleUnitStatInfoCommand | — |
| UpdateDefenseBonusesMod | method | — | Player.Main/UpdateCombatSkills, Player.Main/UpdateSkillsToMaxSkillsForLevel, Unit.SpellAuras/HandleAuraModSkill | — |
| UpdateBlockPercentage | method | Player.Main/CanBlock, SpellCaster/GetDefenseSkillValue, SpellCaster/GetSkillMaxForLevel, Unit.Main/GetTotalAuraModifier, WorldObject.Object/SetStatFloatValue | Player.Main/SetCanBlock, Unit.SpellAuras/HandleAuraModBlockPercent | — |
| UpdateCritPercentage | method | Player.Main/GetTotalPercentageModValue, SpellCaster/GetSkillMaxForLevel, SpellCaster/GetWeaponSkillValue, Unit.Main/GetClass, WorldObject.Object/SetStatFloatValue | Player.Main/HandleBaseModValue | — |
| UpdateAllCritPercentages | method | Player.Main/GetMeleeCritFromAgility, Player.Main/SetBaseModValue | Player.Main/UpdateCombatSkills | — |
| UpdateParryPercentage | method | Player.Main/CanParry, SpellCaster/GetDefenseSkillValue, SpellCaster/GetSkillMaxForLevel, WorldObject.Object/SetStatFloatValue | Player.Main/SetCanParry, Player.Main/_ApplyItemMods, Unit.SpellAuras/HandleAuraModParryPercent | — |
| UpdateDodgePercentage | method | Player.Main/GetDodgeFromAgility, SpellCaster/GetDefenseSkillValue, SpellCaster/GetSkillMaxForLevel, Unit.Main/GetClass, Unit.Main/GetTotalAuraModifier, WorldObject.Object/SetStatFloatValue | Unit.SpellAuras/HandleAuraModDodgePercent | — |
| UpdateManaRegen#2 | method | Unit.Main/GetRegenMPPerSpirit, Unit.Main/GetTotalAuraModifier, Unit.Main/GetTotalAuraModifierByMiscValue, Unit.Main/GetTotalAuraMultiplierByMiscValue | Unit.SpellAuras/HandleAuraModRegenInterrupt | — |
| _ApplyAllStatBonuses | method | Player.Main/_ApplyAllItemMods, Unit.Main/SetCanModifyStats, Unit.Main/_ApplyAllAuraMods | Player.Main/InitStatsForLevel | — |
| _RemoveAllStatBonuses | method | Player.Main/_RemoveAllItemMods, Unit.Main/SetCanModifyStats, Unit.Main/_RemoveAllAuraMods | Player.Main/InitStatsForLevel | — |
| UpdateStats | method | Unit.Main/GetMaxPower, Unit.Main/GetTotalStatValue, Unit.Main/SetStat | — | — |
| UpdateAllStats | method | Unit.Main/GetMaxPower, Unit.Main/GetTotalStatValue, Unit.Main/SetStat, Unit.Main/UpdateAllSpellCritChances | ChatHandler.CharacterCommands/HandleLevelUpCommand, Creature.Main/Update, Creature.Main/UpdateEntry | — |
| UpdateResistances | method | Unit.Main/GetTotalResistanceValue, Unit.Main/SetResistance | — | — |
| UpdateArmor | method | Unit.Main/GetStat, Unit.Main/GetTotalResistanceValue, Unit.Main/SetArmor | — | — |
| UpdateMaxHealth | method | Creature.Main/IsPet, Unit.Main/GetCreateHealth, Unit.Main/GetCreateStat, Unit.Main/GetModifierValue, Unit.Main/GetStat, Unit.Main/SetMaxHealth | — | — |
| UpdateMaxPower | method | Creature.Main/IsPet, Unit.Main/GetCreatePowers, Unit.Main/GetCreateStat, Unit.Main/GetModifierValue, Unit.Main/GetStat, Unit.Main/SetMaxPower | — | — |
| UpdateManaRegen | method | Unit.Main/GetRegenMPPerSpirit, Unit.Main/GetStat, Unit.Main/GetTotalAuraModifierByMiscValue, Unit.Main/GetTotalAuraMultiplierByMiscValue, World/getConfig#2 | — | — |
| UpdateAttackPowerAndDamage | method | Creature.Main/GetClassLevelStats, Object/SetInt16Value, Unit.Main/GetAttackPowerModifierValue, Unit.Main/GetCreateStat, Unit.Main/GetStat, WorldObject.Object/SetFloatValue, WorldObject.Object/SetInt32Value | — | — |
| UpdateDamagePhysical | method | Creature.Main/GetClassLevelStats, Creature.Main/HasWeapon, Unit.Main/CanUseEquippedWeapon, Unit.Main/GetModifierValue, Unit.Main/GetTotalAttackPowerValue, Unit.Main/GetTotalAuraModValue, Unit.Main/GetWeaponDamageRange, WorldObject.Object/SetStatFloatValue | boss_arlokk/UpdateAI, boss_marli/UpdateAI, Creature.Main/ResetStats | — |
| GetWeaponBasedAuraModifier | method | Aura/GetModifier, Aura/GetSpellProto, Creature.Main/GetVirtualItemClass, Creature.Main/GetVirtualItemDisplayId, Creature.Main/GetVirtualItemInventoryType, Creature.Main/GetVirtualItemSubclass, game_Objects_Item/IsFitToSpellRequirements#2, Unit.Main/GetAurasByType | — | — |
| UpdateAllStats#2 | method | Unit.Main/GetMaxPower, Unit.Main/UpdateAllSpellCritChances | Pet.Main/InitStatsForLevel, Spell.Effects/EffectSummon, Spell.Effects/EffectSummonPet#2 | — |
| UpdateResistances#2 | method | — | — | — |
| UpdateArmor#2 | method | Pet.Main/GetPetType, Unit.Main/GetStat, Unit.Main/GetTotalResistanceValue, Unit.Main/SetArmor | — | — |
| UpdateAttackPowerAndDamage#2 | method | Object/GetEntry, Object/SetInt16Value, Unit.Main/GetAttackPowerModifierValue, Unit.Main/GetStat, WorldObject.Object/SetFloatValue, WorldObject.Object/SetInt32Value | Unit.SpellAuras/HandleModDamageDone | — |
| UpdateDamagePhysical#2 | method | Object/GetInt32Value, Pet.Main/GetHappinessState, Pet.Main/GetPetType, Unit.Main/GetCreateStat, Unit.Main/GetModifierValue, Unit.Main/GetTotalAttackPowerValue, Unit.Main/GetTotalAuraModValue, Unit.Main/GetWeaponDamageRange, WorldObject.Object/SetStatFloatValue | Unit.Main/SetPower | — |

---

<!-- verify: failed-members | missing: GetWeaponBasedAuraModifier#2, UpdateAllStats#2, UpdateArmor#2, UpdateArmor#3, UpdateAttackPowerAndDamage#2, UpdateAttackPowerAndDamage#3, UpdateDamagePhysical#2, UpdateDamagePhysical#3, UpdateManaRegen#2, UpdateMaxHealth#2, UpdateMaxPower#2, UpdateResistances#2 -->

---

<!-- verify: boundary-bleed | foreign: Player -->
