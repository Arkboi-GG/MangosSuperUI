# Spell Cooldowns & Costs

<!-- aliases: spell cooldown, reduce cooldowns, no cooldown, mana cost, spell cost, cooldown reset -->
<!-- documentation: model-written from source via the local LLM; review before trusting -->

Spell cooldowns and resource costs in VMaNGOS are governed by the `SpellEntry` template data loaded from `spell_template`, modified by item overrides, and enforced by the `CooldownContainer` within the `Player` (or `SpellCaster`) object. The system distinguishes between **Spell Cooldowns** (specific to one spell ID) and **Category Cooldowns** (shared among a group of spells, such as the Global Cooldown or potion categories). Resource costs (mana, rage, energy, health) are calculated dynamically at cast time based on base values, level scaling, and active modifiers.

When a player casts a spell, `Spell::SendSpellCooldown` checks if the player has the `PLAYER_CHEAT_NO_COOLDOWN` flag enabled. If not, it invokes `Player::AddCooldown`. This method determines the effective cooldown duration by checking `spell_template` (`RecoveryTime` and `CategoryRecoveryTime`) and applying any item-specific overrides if the spell was cast from an item. It then applies spell modifications (e.g., from talents or gear) via `ApplySpellMod`. The resulting duration is stored in the player's `m_cooldownMap` (a `CooldownContainer`). If the spell belongs to a category marked as global in `SpellCategoryEntry`, it also updates the internal Global Cooldown (GCD) timer.

Resource costs are determined by `Spell::CalculatePowerCost`. This function starts with the base `manaCost` and `manaCostPerlevel` from `spell_template`, adjusted by the caster's spell rank. It adds any percentage-based costs defined in `manaCostPercentage`. Finally, it applies flat and percentage modifiers from the caster's auras and equipment, ensuring the cost never drops below zero. For bots, `CombatBotBaseAI::CanTryToCastSpell` pre-validates these costs before attempting a cast to avoid failed attempts.

## How to Modify

### Config
No dedicated configuration keys exist in the provided `CONFIG` block for globally scaling spell cooldowns or mana costs. While general rate keys (like `Rate.XP.Kill`) exist in the broader VMaNGOS configuration, they do not apply to spell cooldown durations or power costs. To change these values globally, you must modify the database or the code.

### Database
The primary surface for modifying cooldowns and costs is the `spell_template` table in the `mangos` database.

**Changing Cooldowns:**
*   **`RecoveryTime`**: Sets the specific cooldown for this spell in milliseconds. Setting this to `0` removes the specific spell cooldown (though it may still be subject to category cooldowns).
*   **`CategoryRecoveryTime`**: Sets the cooldown for the spell's category. This affects all spells sharing the same `category` ID.
*   **`category`**: Defines which category the spell belongs to. Changing this moves the spell to a different shared cooldown group.

**Changing Mana/Power Costs:**
*   **`manaCost`**: The base cost of the spell.
*   **`manaCostPerlevel`**: The additional cost added per level above `baseLevel`.
*   **`manaCostPercentage`**: A percentage of the caster's total power pool (or health) added to the cost.
*   **`powerType`**: Determines which resource is spent (0=Mana, 1=Rage, 2=Focus, 3=Energy, 4=Happiness, 5=Health, etc.).

**Persistence:**
Active cooldowns are saved to the `character_spell_cooldown` table in the `characters` database when a player logs out. This table is managed automatically by `Player::_SaveSpellCooldowns` and `_LoadSpellCooldowns`. Operators should not manually edit this table unless clearing stuck cooldowns for a specific character.

### Code
For behaviors not supported by database columns, such as removing cooldowns entirely for all players or altering the calculation formula:

*   **Disable Cooldowns Globally:** Edit `Spell::SendSpellCooldown` in `Spell.cpp`. Currently, it checks `if (pPlayer->HasCheatOption(PLAYER_CHEAT_NO_COOLDOWN))`. You can remove this check or invert the logic to always skip adding the cooldown, effectively making all spells instant-cast (ignoring GCD if not handled elsewhere).
*   **Modify Cost Calculation:** Edit `Spell::CalculatePowerCost` in `Spell.cpp`. This function contains the formula for base cost, level scaling, and percentage costs. You can hardcode multipliers here (e.g., `powerCost = powerCost / 2;` to halve all costs).
*   **Change Equip Cooldowns:** Edit `Player::ApplyEquipCooldown` in `Player.cpp`. The hardcoded `30 * IN_MILLISECONDS` value sets the standard 30-second cooldown for use-items. Change this constant to adjust the equip cooldown duration.
*   **Bot Casting Logic:** Edit `CombatBotBaseAI::CanTryToCastSpell` in `CombatBotBaseAI.cpp` if you need bots to ignore certain cooldowns or cost checks differently than players.

## Path Reference

**SetCheatNoCooldown** (Player.cpp): Toggles the `PLAYER_CHEAT_NO_COOLDOWN` flag, which bypasses cooldown application in `Spell::SendSpellCooldown`.

**_LoadSpellCooldowns** (Player.cpp): Restores active cooldowns from the `character_spell_cooldown` table upon login, calculating remaining time based on the current server clock.

**_SaveSpellCooldowns** (Player.cpp): Persists non-permanent active cooldowns to the `character_spell_cooldown` table during logout or save intervals.

**ApplyEquipCooldown** (Player.cpp): Applies a hardcoded 30-second cooldown to items with the `ITEM_FLAG_NO_EQUIP_COOLDOWN` flag unset, triggered when a use-item is activated.

**SendClearCooldown** (Player.cpp): Sends the `SMSG_CLEAR_COOLDOWN` packet to the client to visually remove a specific spell's cooldown icon.

**SendClearAllCooldowns** (Player.cpp): Sends the `SMSG_COOLDOWN_CHEAT` packet to clear all cooldown icons on the client side for a target.

**SendSpellCooldown** (Player.cpp): Sends the `SMSG_SPELL_COOLDOWN` packet to start or update a specific spell's cooldown timer on the client.

**AddCooldown** (Player.cpp): The central method for applying cooldowns. It resolves item overrides, applies spell mods, handles category ownership, and updates the internal `CooldownContainer`.

**RemoveSpellCooldown** (Player.cpp): Removes a specific spell's cooldown from the container and optionally notifies the client.

**RemoveSpellCategoryCooldown** (Player.cpp): Finds the owner of a category cooldown, removes it, and notifies the client.

**RemoveAllCooldowns** (Player.cpp): Iterates through the cooldown map, clears non-permanent cooldowns, resets lockouts, and sends clear packets to the client.

**LoadSpellScripts** (ScriptMgr.cpp): Validates spell scripts against `spell_template` to ensure they have the correct `SPELL_EFFECT_SCRIPT_EFFECT`.

**CollectPossibleEventIds** (ScriptMgr.cpp): Scans `spell_template` and `gameobject_template` to identify all potential event IDs referenced by spells.

**GetPowerCost** (Spell.h): Inline accessor returning the calculated power cost stored in the `Spell` instance.

**SendSpellCooldown** (Spell.cpp): Invoked after a spell cast; checks for cheat flags and delegates to `Player::AddCooldown` or `SpellCaster::AddCooldown`.

**CalculatePowerCost** (Spell.cpp): Computes the final resource cost by combining base values, level scaling, percentage costs, and aura/equipment modifiers.

**LoadExistingSpellIds** (SpellMgr.cpp): Loads all unique spell IDs from `spell_template` into a set for quick existence checks.

**LoadSpells** (SpellMgr.cpp): Loads all spell data from `spell_template` and `locales_spell` into memory at server startup.

**HandleUnstuckCommand** (TeleportCommands.cpp): Example of manual cooldown manipulation; adds a 1-hour cooldown to spell 20939 if the player is dead.

**HandleCooldownClearClientSideCommand** (UnitCommands.cpp): GM command that calls `Player::RemoveAllCooldowns` to clear a target's cooldowns.

**CanTryToCastSpell** (CombatBotBaseAI.cpp): Pre-cast validation for bots, checking if the bot has sufficient power and if the spell is off cooldown.

**AddCooldown** (SpellCaster.h): Template method for adding cooldowns to the caster's container, handling category ownership logic.

**RemoveBySpellId** (SpellCaster.h): Removes a specific spell's cooldown entry and its associated category entry if it was the owner.

**erase** (SpellCaster.h): Erases a cooldown entry by iterator, cleaning up category maps if necessary.

**FindByCategory** (SpellCaster.h): Locates the spell entry that currently owns a specific category cooldown.

**GetSpellCDExpireTime** (SpellCaster.h): Retrieves the absolute expiration time for a spell's cooldown.

**GetCatCDExpireTime** (SpellCaster.h): Retrieves the absolute expiration time for a category's cooldown.

**IsSpellCDExpired** (SpellCaster.h): Checks if the current time has passed the spell's cooldown expiration.

**IsCatCDExpired** (SpellCaster.h): Checks if the current time has passed the category's cooldown expiration.

**CreateStatement** (Database.cpp): Prepares SQL statements for saving/loading cooldowns, ensuring efficient execution.

**GetUInt64** (Field.h): Parses 64-bit integers from database results, used for reading cooldown timestamps.

**FinishRitual** (GameObject.cpp): Triggers a cooldown on the player who completed a ritual spell, using `Player::AddCooldown`.

**GetInt32Value** (Object.h): Accessor for integer object fields, used in cost modifier calculations.

**GetFloatValue** (Object.h): Accessor for float object fields, used in ranged attack time adjustments for cooldowns.

**AddCooldown** (SpellCaster.cpp): Implementation of the base caster cooldown addition, delegating to the container.

**GetFirstSchoolInMask** (SpellDefines.h): Helper to determine the primary spell school for applying school-specific cost modifiers.

**GetSpellSchoolMask** (SpellEntry.h): Returns the spell school mask from the template, used for modifier targeting.

**Execute#2** (SqlPreparedStatement.cpp): Executes the prepared SQL statements for saving cooldown data to the database.

**GetSpellRank** (Unit.cpp): Calculates the caster's spell rank, used in `CalculatePowerCost` for level-based cost scaling.

**GetSpellModOwner** (Unit.cpp): Identifies the player responsible for applying spell modifications, crucial for pets and totems.

---

<!-- machine-true, projected from graph.json -->

## Map — Spell Cooldowns & Costs

*Source:* Player.cpp, ScriptMgr.cpp, Spell.h, Spell.cpp, SpellMgr.cpp, TeleportCommands.cpp, UnitCommands.cpp, CombatBotBaseAI.cpp, SpellCaster.h, Database.cpp, Field.h, GameObject.cpp, Object.h, SpellCaster.cpp, SpellDefines.h, SpellEntry.h, SqlPreparedStatement.cpp, Unit.cpp
*Config keys:* —
*Tables:* spell_template

| Member | Kind | Source | Role |
|---|---|---|---|
| Player.Main/SetCheatNoCooldown | method | Player.cpp:2834-2842 | seed — Player.*/*Cooldown* |
| Player.Main/_LoadSpellCooldowns | method | Player.cpp:4005-4068 | seed — Player.*/*Cooldown* |
| Player.Main/_SaveSpellCooldowns | method | Player.cpp:4070-4102 | seed — Player.*/*Cooldown* |
| Player.Main/ApplyEquipCooldown | method | Player.cpp:19307-19333 | seed — Player.*/*Cooldown* |
| Player.Main/SendClearCooldown | method | Player.cpp:21030-21036 | seed — Player.*/*Cooldown* |
| Player.Main/SendClearAllCooldowns | method | Player.cpp:21038-21043 | seed — Player.*/*Cooldown* |
| Player.Main/SendSpellCooldown | method | Player.cpp:21045-21052 | seed — Player.*/*Cooldown* |
| Player.Main/AddCooldown | method | Player.cpp:22184-22298 | seed — Player.*/*Cooldown* |
| Player.Main/RemoveSpellCooldown | method | Player.cpp:22300-22306 | seed — Player.*/*Cooldown* |
| Player.Main/RemoveSpellCategoryCooldown | method | Player.cpp:22308-22319 | seed — Player.*/*Cooldown* |
| Player.Main/RemoveAllCooldowns | method | Player.cpp:22321-22355 | seed — Player.*/*Cooldown* |
| ScriptMgr/LoadSpellScripts | method | ScriptMgr.cpp:1500-1549 | seed — queries spell_template |
| ScriptMgr/CollectPossibleEventIds | method | ScriptMgr.cpp:2465-2568 | seed — queries spell_template |
| Spell.Main/GetPowerCost | method | Spell.h:328-328 | seed — Spell.Main/*PowerCost* |
| Spell.Main/SendSpellCooldown | method | Spell.cpp:3935-3946 | seed — Spell.Main/*Cooldown* |
| Spell.Main/CalculatePowerCost | method | Spell.cpp:6945-7017 | seed — Spell.Main/*PowerCost* |
| SpellMgr/LoadExistingSpellIds | method | SpellMgr.cpp:3101-3117 | seed — queries spell_template |
| SpellMgr/LoadSpells | method | SpellMgr.cpp:3679-3771 | seed — queries spell_template |
| ChatHandler.TeleportCommands/HandleUnstuckCommand | method | TeleportCommands.cpp:1062-1096 | related — 1 hop from a seed |
| ChatHandler.UnitCommands/HandleCooldownClearClientSideCommand | method | UnitCommands.cpp:2652-2667 | related — 1 hop from a seed |
| CombatBotBaseAI/CanTryToCastSpell | method | CombatBotBaseAI.cpp:2803-2856 | related — 1 hop from a seed |
| CooldownContainer/AddCooldown | method | SpellCaster.h:203-228 | related — 1 hop from a seed |
| CooldownContainer/RemoveBySpellId | method | SpellCaster.h:230-244 | related — 1 hop from a seed |
| CooldownContainer/erase | method | SpellCaster.h:256-266 | related — 1 hop from a seed |
| CooldownContainer/FindByCategory | method | SpellCaster.h:270-274 | related — 1 hop from a seed |
| CooldownData/GetSpellCDExpireTime | method | SpellCaster.h:111-118 | related — 1 hop from a seed |
| CooldownData/GetCatCDExpireTime | method | SpellCaster.h:126-133 | related — 1 hop from a seed |
| CooldownData/IsSpellCDExpired | method | SpellCaster.h:135-141 | related — 1 hop from a seed |
| CooldownData/IsCatCDExpired | method | SpellCaster.h:143-155 | related — 1 hop from a seed |
| Database/CreateStatement | method | Database.cpp:676-702 | related — 1 hop from a seed |
| Field/GetUInt64 | method | Field.h:68-75 | related — 1 hop from a seed |
| GameObject/FinishRitual | method | GameObject.cpp:797-825 | related — 1 hop from a seed |
| Object/GetInt32Value | method | Object.h:177-181 | related — 1 hop from a seed |
| Object/GetFloatValue | method | Object.h:195-199 | related — 1 hop from a seed |
| SpellCaster/AddCooldown | method | SpellCaster.cpp:2153-2158 | related — 1 hop from a seed |
| SpellDefines/GetFirstSchoolInMask | function | SpellDefines.h:771-778 | related — 1 hop from a seed |
| SpellEntry/GetSpellSchoolMask | method | SpellEntry.h:1178-1181 | related — 1 hop from a seed |
| SqlPreparedStatement/Execute#2 | method | SqlPreparedStatement.cpp:72-86 | related — 1 hop from a seed |
| Unit.Main/GetSpellRank | method | Unit.cpp:5412-5418 | related — 1 hop from a seed |
| Unit.Main/GetSpellModOwner | method | Unit.cpp:9210-9225 | related — 1 hop from a seed |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `character_spell_cooldown`: guid int(11) unsigned PK, spell int(11) unsigned PK, spell_expire_time bigint(20) unsigned, category int(11) unsigned, category_expire_time bigint(20) unsigned, item_id int(11) unsigned
- `gameobject_template`: entry mediumint(8) unsigned PK, patch tinyint(3) unsigned PK, type tinyint(3) unsigned, displayId mediumint(8) unsigned, name varchar(100), icon varchar(100), faction smallint(5) unsigned, flags int(10) unsigned, size float, data0 int(10), data1 int(11), data2 int(10), data3 int(10), data4 int(10), data5 int(10), data6 int(11), data7 int(10), data8 int(10), data9 int(10), data10 int(10), data11 int(10), data12 int(10), data13 int(10), data14 int(10), data15 int(10), data16 int(10), data17 int(10), data18 int(10), data19 int(10), data20 int(10), data21 int(10), data22 int(10), data23 int(10), mingold mediumint(8) unsigned, maxgold mediumint(8) unsigned, script_name varchar(64)
- `locales_spell`: entry smallint(5) unsigned PK, name_loc1 varchar(256), name_loc2 varchar(256), name_loc3 varchar(256), name_loc4 varchar(256), name_loc5 varchar(256), name_loc6 varchar(256), nameSubtext_loc1 varchar(256), nameSubtext_loc2 varchar(256), nameSubtext_loc3 varchar(256), nameSubtext_loc4 varchar(256), nameSubtext_loc5 varchar(256), nameSubtext_loc6 varchar(256), description_loc1 varchar(1024), description_loc2 varchar(1024), description_loc3 varchar(1024), description_loc4 varchar(1024), description_loc5 varchar(1024), description_loc6 varchar(1024), auraDescription_loc1 varchar(512), auraDescription_loc2 varchar(512), auraDescription_loc3 varchar(512), auraDescription_loc4 varchar(512), auraDescription_loc5 varchar(512), auraDescription_loc6 varchar(512)
- `spell_template`: entry mediumint(8) unsigned PK, build smallint(4) unsigned PK, school int(4) unsigned, category int(4) unsigned, castUI int(4) unsigned, dispel int(4) unsigned, mechanic int(4) unsigned, attributes int(4) unsigned, attributesEx int(4) unsigned, attributesEx2 int(4) unsigned, attributesEx3 int(4) unsigned, attributesEx4 int(4) unsigned, stances int(4) unsigned, stancesNot int(4) unsigned, targets int(4) unsigned, targetCreatureType int(4) unsigned, requiresSpellFocus int(4) unsigned, casterAuraState int(4) unsigned, targetAuraState int(4) unsigned, castingTimeIndex int(4) unsigned, recoveryTime int(4) unsigned, categoryRecoveryTime int(4) unsigned, interruptFlags int(4) unsigned, auraInterruptFlags int(4) unsigned, channelInterruptFlags int(4) unsigned, procFlags int(4) unsigned, procChance int(4) unsigned, procCharges int(4) unsigned, maxLevel int(4) unsigned, baseLevel int(4) unsigned, spellLevel int(4) unsigned, durationIndex int(4) unsigned, powerType int(4) unsigned, manaCost int(4) unsigned, manCostPerLevel int(4) unsigned, manaPerSecond int(4) unsigned, manaPerSecondPerLevel int(4) unsigned, rangeIndex int(4) unsigned, speed float, modelNextSpell int(4) unsigned, stackAmount int(4) unsigned, totem1 int(4) unsigned, totem2 int(4) unsigned, reagent1 int(4), reagent2 int(4), reagent3 int(4), reagent4 int(4), reagent5 int(4), reagent6 int(4), reagent7 int(4), reagent8 int(4), reagentCount1 int(4) unsigned, reagentCount2 int(4) unsigned, reagentCount3 int(4) unsigned, reagentCount4 int(4) unsigned, reagentCount5 int(4) unsigned, reagentCount6 int(4) unsigned, reagentCount7 int(4) unsigned, reagentCount8 int(4) unsigned, equippedItemClass int(4), equippedItemSubClassMask int(4), equippedItemInventoryTypeMask int(4), effect1 int(4) unsigned, effect2 int(4) unsigned, effect3 int(4) unsigned, effectDieSides1 int(4), effectDieSides2 int(4), effectDieSides3 int(4), effectBaseDice1 int(4) unsigned, effectBaseDice2 int(4) unsigned, effectBaseDice3 int(4) unsigned, effectDicePerLevel1 float, effectDicePerLevel2 float, effectDicePerLevel3 float, effectRealPointsPerLevel1 float, effectRealPointsPerLevel2 float, effectRealPointsPerLevel3 float, effectBasePoints1 int(4), effectBasePoints2 int(4), effectBasePoints3 int(4), effectBonusCoefficient1 float, effectBonusCoefficient2 float, effectBonusCoefficient3 float, effectMechanic1 int(4) unsigned, effectMechanic2 int(4) unsigned, effectMechanic3 int(4) unsigned, effectImplicitTargetA1 int(4) unsigned, effectImplicitTargetA2 int(4) unsigned, effectImplicitTargetA3 int(4) unsigned, effectImplicitTargetB1 int(4) unsigned, effectImplicitTargetB2 int(4) unsigned, effectImplicitTargetB3 int(4) unsigned, effectRadiusIndex1 int(4) unsigned, effectRadiusIndex2 int(4) unsigned, effectRadiusIndex3 int(4) unsigned, effectApplyAuraName1 int(4) unsigned, effectApplyAuraName2 int(4) unsigned, effectApplyAuraName3 int(4) unsigned, effectAmplitude1 int(4) unsigned, effectAmplitude2 int(4) unsigned, effectAmplitude3 int(4) unsigned, effectMultipleValue1 float, effectMultipleValue2 float, effectMultipleValue3 float, effectChainTarget1 int(4) unsigned, effectChainTarget2 int(4) unsigned, effectChainTarget3 int(4) unsigned, effectItemType1 bigint(20) unsigned, effectItemType2 bigint(20) unsigned, effectItemType3 bigint(20) unsigned, effectMiscValue1 int(4), effectMiscValue2 int(4), effectMiscValue3 int(4), effectTriggerSpell1 int(4) unsigned, effectTriggerSpell2 int(4) unsigned, effectTriggerSpell3 int(4) unsigned, effectPointsPerComboPoint1 float, effectPointsPerComboPoint2 float, effectPointsPerComboPoint3 float, spellVisual1 int(4) unsigned, spellVisual2 int(4) unsigned, spellIconId int(4) unsigned, activeIconId int(4) unsigned, spellPriority int(4) unsigned, name varchar(256), nameFlags int(4) unsigned, nameSubtext varchar(256), nameSubtextFlags int(4) unsigned, description varchar(1024), descriptionFlags int(4) unsigned, auraDescription varchar(512), auraDescriptionFlags int(4) unsigned, manaCostPercentage int(4) unsigned, startRecoveryCategory int(4) unsigned, startRecoveryTime int(4) unsigned, minTargetLevel int(4) unsigned, maxTargetLevel int(4) unsigned, spellFamilyName int(4) unsigned, spellFamilyFlags bigint(20) unsigned, maxAffectedTargets int(4) unsigned, dmgClass int(4) unsigned, preventionType int(4) unsigned, stanceBarOrder int(4), dmgMultiplier1 float, dmgMultiplier2 float, dmgMultiplier3 float, minFactionId int(4) unsigned, minReputation int(4) unsigned, requiredAuraVision int(4) unsigned, customFlags int(10) unsigned, script_name varchar(64)

*`?` = nullable, `PK` = primary key column.*

