<!-- provenance: boundary-bleed -->
# Spell.Effects

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Spell Effects Implementation (`SpellEffects.cpp`)

## Purpose & Responsibilities

This translation unit implements the concrete behavior for every spell effect type defined in the World of Warcraft client protocol (specifically for the Classic/1.12 era, as indicated by the `CLIENT_BUILD` macros and copyright headers). It serves as the execution engine for the `Spell` class.

When a spell is cast and its effects are processed, the `Spell` dispatcher (located in `Spell.cpp`, not this unit) calls the appropriate method defined in this file based on the `SPELL_EFFECT_*` enum value. Each method handles the specific game logic associated with that effect, such as dealing damage, applying auras, summoning creatures, modifying player stats, or interacting with the environment.

Key responsibilities include:
1.  **Effect Execution:** Implementing the core logic for ~130 distinct spell effects.
2.  **Cross-Subsystem Integration:** Interacting with `Unit`, `Player`, `Creature`, `GameObject`, `Pet`, `Aura`, and `BattleGround` systems to produce side effects.
3.  **Data Validation:** Checking target validity, caster permissions, and resource availability before executing changes.
4.  **Client Communication:** Sending packets to clients to visualize effects, update combat logs, or request user input (e.g., resurrection requests).
5.  **Special Case Handling:** Managing hardcoded behaviors for specific spells (often via `switch` statements on Spell IDs within generic effects like `EffectDummy` or `EffectScriptEffect`).

## Member-by-Member Behavior

The members are grouped by their functional domain. Note that many effects share underlying logic or are stubs for unused/legacy effects.

### Core Damage and Healing
*   **EffectSchoolDMG**: Adds calculated damage to the spell's internal damage accumulator. It only processes if the target is alive and damage is non-negative.
*   **EffectEnvironmentalDMG**: Handles environmental damage (e.g., fire, fall). For players, it delegates to `Player::EnvironmentalDamage`. For non-players, it calculates absorption/resistance and sends a damage log packet.
*   **EffectInstaKill**: Instantly kills the target. It sends a specific `SMSG_SPELLINSTAKILLLOG` packet and deals damage equal to the target's current health. It prevents self-interrupt messages if the caster is the target.
*   **EffectWeaponDmg**: Calculates weapon-based damage. It aggregates bonuses from multiple weapon damage effects (normalized, percentage, flat) and applies spell damage modifiers. It handles complex interactions between weapon swings and spell bonuses.
*   **EffectHeal**: Applies healing to a target. It calculates healing bonuses (done/taken) and adds the result to the spell's healing accumulator.
*   **EffectHealMechanical**: Similar to `EffectHeal` but specifically for mechanical creatures. It uses `SpellCaster::DealHeal` to finalize the heal.
*   **EffectHealthLeech**: Damages the target and heals the caster. It caps damage to the target's current health to prevent overkill healing. It applies a multiplier to the healed amount.
*   **EffectPowerDrain**: Drains a specific power type (Mana, Rage, etc.) from the target. If draining Mana, it may restore a portion to the caster based on a multiplier. It respects spell damage bonuses.
*   **EffectPowerBurn**: Burns power from the target and converts it into direct damage dealt by the spell. It applies multipliers and spell modifiers.

### Auras and Status Effects
*   **EffectApplyAura**: Creates and applies an `Aura` to the target. It checks for dead targets (unless the spell allows it), verifies no more powerful spell is active, and determines the effective caster. It creates the `Aura` object and adds it to the target's aura holder.
*   **EffectApplyAreaAura**: Creates an `AreaAura` (party/pet/friend/enemy wide) and adds it to the target's aura holder.
*   **EffectPersistentAA**: Creates a `DynamicObject` representing a persistent area aura (like a totem or portal aura). It calculates radius, applies modifiers, and adds the object to the map and caster's dynamic object list.
*   **EffectDispel**: Removes auras from the target based on dispel type (Magic, Poison, etc.). It builds a list of eligible auras, prioritizes charm removal if necessary, rolls for dispel success/chance, and sends success/fail packets to the client. It removes the auras from the target's map.
*   **EffectDispelMechanic**: Removes all auras from the target that have a specific mechanic mask (e.g., Fear, Sleep).
*   **EffectInterruptCast**: Interrupts the target's current casting or channeling spell if it matches specific prevention types (Silence) and interrupt flags. It locks out spells of the same school for a duration.
*   **EffectSanctuary**: Implements stealth/vanish mechanics. It interrupts attacks/spells on the target, removes threat from hostile references (except guards in later patches), and sets a "cannot be detected" timer for players.

### Summoning and Spawning
*   **EffectSummon**: Summons a pet for the caster. It checks if the caster already has a pet, loads or creates the pet creature, initializes stats, sets ownership, and adds it to the map. It handles both new summons and reloading existing pets from DB.
*   **EffectSummonPet**: Delegates to `Unit::EffectSummonPet` (defined in `Unit.cpp`) to handle the complex logic of summoning a hunter/warlock pet, including unsummoning old pets and initializing the new one.
*   **EffectSummonPet#2**: This is the implementation of `Unit::EffectSummonPet`, declared in `Unit.h` and defined in `Unit.cpp`. It is included here because the `Spell` header includes `Unit.h`. It performs the actual creation, initialization, and database saving of the pet object, returning its GUID.
*   **EffectSummonGuardian**: Summons a guardian pet (e.g., Warlock demons, Druid feral guardians). It handles multiple guardians, calculates level scaling, and manages follow angles.
*   **EffectSummonWild**: Summons wild creatures (e.g., Engineering gadgets, nature summons). It can summon multiple units in a radius. It handles loot recipient assignment for creator-loot creatures.
*   **EffectSummonObject**: Summons a `GameObject` (e.g., portals, traps). It manages object slots to prevent duplicates, sets duration, and notifies AI.
*   **EffectSummonObjectWild**: Summons a wild `GameObject`. It handles special cases for battleground flags and linked traps.
*   **EffectSummonTotem**: Summons a Shaman totem. It unsummons existing totems in the same slot, creates the totem creature, sets health/duration, and adds it to the caster's totem list.
*   **EffectSummonCritter**: Summons a mini-pet (critter). It replaces any existing mini-pet, initializes the creature, and sets it as the player's mini-pet.
*   **EffectSummonDemon**: Summons a demon (typically via a ritual). It positions the demon at the ritual site or destination and sets its level to the caster's level.
*   **EffectSummonPossessed**: Summons a possessed minion (Warlock). It delegates to `Player::SummonPossessedMinion`.
*   **EffectCreateHouse**: Summons a player's house `GameObject` and removes any previous house instance.
*   **EffectSpawn**: Makes a unit visible and removes the "spawning" flag. Used for login animations.

### Movement and Positioning
*   **EffectTeleportUnits**: Teleports the target to a specific location (home bind, database location, or destination). It handles map changes and combat state preservation.
*   **EffectTeleUnitsFaceCaster**: Teleports the target to a point close to the caster, facing away from the caster.
*   **EffectLeapForward**: Teleports the target to a destination point, preserving orientation. Used for leap spells.
*   **EffectKnockBack**: Pushes the target away from the caster. It removes specific sleep auras to ensure the knockback registers.
*   **EffectPlayerPull**: Pulls the target toward the caster. It uses negative distance in `KnockBackFrom` to achieve the pull effect.
*   **EffectDistract**: Makes a creature move to a specific point (distract item). It sets the creature's motion master to `MoveDistract`.
*   **EffectSendTaxi**: Activates a taxi path for the player target.

### Player Progression and Stats
*   **EffectLearnSpell**: Teaches a spell to the player target. If the target is not a player, it delegates to `EffectLearnPetSpell`.
*   **EffectLearnSkill**: Increases a player's skill level. It calculates the new skill value based on steps and maximums.
*   **EffectLearnPetSpell**: Teaches a spell to the player's active pet. It deducts training points and saves the pet to the DB.
*   **EffectProficiency**: Grants weapon or armor proficiency to the player.
*   **EffectDualWield**: Enables dual-wielding for the player.
*   **EffectParry**: Enables parrying for the player.
*   **EffectBlock**: Enables blocking for the player.
*   **EffectAddHonor**: Awards honor points to the player.
*   **EffectReputation**: Modifies the player's reputation with a faction.
*   **EffectQuestComplete**: Marks a quest as completed/explored for the player.
*   **EffectAddComboPoints**: Adds combo points to the target for the caster (Rogue mechanic).

### Items and Economy
*   **EffectCreateItem**: Creates an item and adds it to the player's inventory. It handles stacking, inventory space checks, and battleground reward mailing if inventory is full.
*   **EffectSummonChangeItem**: Transforms the item used to cast the spell into a new item. It preserves enchantments and durability loss. It handles inventory, bank, and equipment slots.
*   **EffectEnchantItemPerm**: Applies a permanent enchantment to an item. It logs GM actions if applicable and updates the item's enchantment slot.
*   **EffectEnchantItemTmp**: Applies a temporary enchantment with charges and duration.
*   **EffectEnchantHeldItem**: Applies a temporary enchantment to the player's main-hand weapon.
*   **EffectDisEnchant**: Triggers the disenchanting loot window for an item. It binds the item and updates crafting skills.
*   **EffectDurabilityDamage**: Deals durability damage to a specific equipment slot or all items.
*   **EffectDurabilityDamagePCT**: Deals durability damage as a percentage to a specific slot or all items.
*   **EffectPickPocket**: Opens the pickpocketing loot window for the target creature.
*   **EffectSkinning**: Opens the skinning loot window for the target creature and updates the player's skinning skill.
*   **EffectFeedPet**: Consumes a food item to increase the pet's happiness/loyalty. It checks range and line-of-sight.

### Combat and Threat
*   **EffectThreat**: Adds a flat amount of threat to the target's threat list for the caster.
*   **EffectModifyThreatPercent**: Modifies the caster's threat percentage on the target.
*   **EffectTaunt**: Forces the target to attack the caster. It adjusts threat to match the current highest threat target and sets the caster as the current victim.
*   **EffectAddExtraAttacks**: Grants the target extra attacks on their next swing. It handles legacy timer resets for older client builds.

### Resurrection and Death
*   **EffectResurrect**: Sends a resurrection request to a dead player. It calculates restored health/mana percentages.
*   **EffectResurrectNew**: A newer resurrection effect that handles both players and pets. For pets, it instantly revives them. For players, it sends a request. It also cleans up specific warlock auras (Demonic Sacrifice).
*   **EffectSelfResurrect**: Revives the caster/player immediately. It restores health/mana (flat or percentage) and spawns corpse bones.
*   **EffectSpiritHeal**: Revives a player at a graveyard/spirit healer. It removes specific auras and re-summons pets.
*   **EffectSummonDeadPet**: Revives a dead pet at the player's location.

### Special and Scripted Effects
*   **EffectDummy**: A catch-all effect for spells with unique, hardcoded behaviors. It contains massive `switch` statements on Spell ID and Family Name to implement specific logic for hundreds of individual spells (e.g., Gnomish gadgets, holiday events, boss mechanics). It also handles pet auras and script manager callbacks.
*   **EffectScriptEffect**: Another catch-all for scripted effects. It handles specific spells via ID switches and then delegates to `Map::ScriptsStart` for database-defined scripts.
*   **EffectTriggerSpell**: Casts a secondary spell on the target. It checks for weapon requirements and determines the correct caster for the triggered spell.
*   **EffectTriggerMissileSpell**: Casts a missile spell at a specific coordinate.
*   **EffectSendEvent**: Triggers a server-side event script. It passes the caster and optional GameObject target to the script manager.
*   **EffectOpenLock**: Opens a locked GameObject or Item. It checks lockpicking skill, handles battleground flags, updates skill, and triggers loot/AI events.
*   **EffectActivateObject**: Performs an action on a GameObject (open, close, lock, unlock, animate, destroy). It delegates to the GameObject's AI if available.
*   **EffectDuel**: Initiates a duel between two players. It creates a duel flag GameObject, sends packets, and sets up duel state.
*   **EffectStuck**: Teleports a stuck player to their last safe position.
*   **EffectSummonPlayer**: Sends a summon request to a player.
*   **EffectBind**: Sets the player's homebind to their current location.
*   **EffectDespawnObject**: Adds a GameObject to the removal list.
*   **EffectNostalrius**: A debug/logging stub for custom Nostalrius-specific effects.
*   **EffectEmpty**, **EffectNULL**, **EffectUnused**: Stubs for effects that do nothing or are logged for debugging.

### Helper Methods
*   **DoCreateItem**: Shared logic for creating items, used by `EffectCreateItem` and potentially other effects.
*   **SendLoot**: Helper to send loot windows for GameObjects, handling different GO types (doors, chests, traps).

## Cross-Unit Boundaries

This unit acts as a central hub, calling into almost every major subsystem.

*   **Unit / Player / Creature / Pet**:
    *   *Direction*: Outbound.
    *   *Why*: To modify state (health, power, auras, position, threat), query state (alive, in combat, skills), and perform actions (cast spells, learn spells, summon pets).
    *   *Examples*: `Unit::SetHealth`, `Player::LearnSpell`, `Creature::AI`, `Pet::SavePetToDB`.

*   **Aura / SpellAuras**:
    *   *Direction*: Outbound.
    *   *Why*: To create, add, remove, and query auras.
    *   *Examples*: `Unit::AddAura`, `Unit::RemoveAurasDueToSpell`, `Aura::GetModifier`.

*   **GameObject**:
    *   *Direction*: Outbound.
    *   *Why*: To summon, despawn, activate, and interact with objects in the world.
    *   *Examples*: `GameObject::Create`, `GameObject::Use`, `GameObject::SetLootState`.

*   **BattleGround / BattleGroundMgr**:
    *   *Direction*: Outbound.
    *   *Why*: To handle battleground-specific logic like flag captures, reward marks, and team checks.
    *   *Examples*: `BattleGround::EventPlayerClickedOnFlag`, `BattleGroundMgr::GetBattleGroundTemplate`.

*   **ScriptMgr / InstanceData**:
    *   *Direction*: Outbound.
    *   *Why*: To trigger custom scripts defined in the database or code for specific spells/events.
    *   *Examples*: `ScriptMgr::OnEffectDummy`, `InstanceData::SetData`.

*   **Log / WorldPacket**:
    *   *Direction*: Outbound.
    *   *Why*: To record events and communicate with clients.
    *   *Examples*: `Log::Out`, `WorldPacket::operator<<`.

*   **ObjectMgr / SpellMgr**:
    *   *Direction*: Outbound.
    *   *Why*: To look up static data (spell entries, creature templates, item prototypes).
    *   *Examples*: `SpellMgr::GetSpellEntry`, `ObjectMgr::GetCreatureTemplate`.

*   **Called By**:
    *   Primarily called by the `Spell` class dispatcher (likely in `Spell.cpp` or similar, though not shown in the map as an inbound caller, it is implied by the structure). The map shows no inbound callers, suggesting this unit is purely reactive to the spell casting system.

## Data Model

This unit does not directly query or modify database tables via SQL. It interacts with the database indirectly through higher-level classes:
*   **Pet Data**: Modified via `Pet::SavePetToDB`, which likely writes to `character_pet` or similar tables.
*   **Character Data**: Modified via `Player` methods (e.g., `LearnSpell`, `SetSkill`), which persist to `characters` table.
*   **Item Data**: Modified via `Item` methods, persisting to `character_inventory`.
*   **Spell/Creature/Item Templates**: Read from DBC files or database tables via `ObjectMgr` and `SpellMgr`, but this unit does not perform the queries itself.

Therefore, there are no direct SQL queries or table manipulations in this source file.

## Notable Implementation Details

1.  **Hardcoded Spell Logic**: `EffectDummy` and `EffectScriptEffect` contain extensive `switch` statements on Spell IDs. This is a common pattern in WoW emulators to handle spells that don't fit standard effect models or require complex, unique logic. Maintainers must add new cases here for unsupported spells.
2.  **Client Build Compatibility**: The code uses `#if SUPPORTED_CLIENT_BUILD > CLIENT_BUILD_X` directives to handle differences between WoW versions (e.g., 1.11 vs 1.12). This ensures compatibility with specific client patches.
3.  **Threat Management**: Several effects (`EffectTaunt`, `EffectThreat`, `EffectModifyThreatPercent`) directly manipulate the `ThreatManager`. Care must be taken to ensure threat values are consistent with client expectations.
4.  **Pet Lifecycle**: Summoning pets involves complex initialization (`InitStatsForLevel`, `AIM_Initialize`, `SavePetToDB`). The code distinguishes between different pet types (Hunter, Warlock, Guardian, Critter) and handles their specific requirements.
5.  **Resource Safety**: Many effects check for `nullptr` targets and alive status before proceeding. However, some legacy code paths may assume valid pointers, so careful review is needed when modifying.
6.  **Logging**: Extensive use of `sLog.Out` for debugging and error reporting. This is crucial for diagnosing spell issues.
7.  **Execute Logs**: Many effects populate `ExecuteLogInfo` structs to track what happened for combat log purposes. This data is sent to clients to display damage/heal numbers and event logs.

## Member Reference

**EffectEmpty**: Stub method for empty effects. Does nothing.
**EffectNULL**: Logs a debug message. Stub for null effects.
**EffectUnused**: Stub for unused effects. Does nothing.
**EffectResurrectNew**: Revives dead pets instantly or sends resurrection requests to players. Cleans up Demonic Sacrifice auras.
**EffectInstaKill**: Kills target instantly, sends instakill log packet, and deals full health damage.
**EffectEnvironmentalDMG**: Applies environmental damage, calculating absorption/resistance for non-players.
**EffectSchoolDMG**: Adds calculated damage to the spell's damage accumulator.
**EffectDummy**: Massive switch-case handler for unique spell behaviors. Handles pet auras and script callbacks.
**EffectTriggerSpell**: Casts a secondary spell on the target, checking weapon requirements.
**EffectTriggerMissileSpell**: Casts a missile spell at a specific coordinate.
**EffectTeleportUnits**: Teleports target to home bind, database location, or destination.
**EffectApplyAura**: Creates and applies an aura to the target, checking validity and power levels.
**EffectPowerDrain**: Drains power from target, optionally restoring mana to caster.
**EffectSendEvent**: Triggers a server-side event script.
**EffectPowerBurn**: Burns power from target and converts it to damage.
**EffectHeal**: Applies healing to target, calculating bonuses.
**EffectHealMechanical**: Applies healing to mechanical creatures.
**EffectHealthLeech**: Damages target and heals caster, capping damage to target's health.
**DoCreateItem**: Helper to create and store items in player inventory, handling stacking and BG rewards.
**EffectCreateItem**: Calls `DoCreateItem` to create an item.
**EffectPersistentAA**: Creates a persistent area aura dynamic object.
**EffectEnergize**: Restores power to the target.
**SendLoot**: Helper to send loot windows for GameObjects.
**EffectOpenLock**: Opens locked objects, checks skill, handles BG flags, and updates skill.
**EffectSummonChangeItem**: Transforms the cast item into a new item, preserving enchantments/durability.
**EffectProficiency**: Grants weapon/armor proficiency to player.
**EffectApplyAreaAura**: Creates an area-wide aura.
**EffectSummon**: Summons a pet, initializing stats and ownership.
**EffectLearnSpell**: Teaches a spell to player or pet.
**EffectDispel**: Removes auras from target based on dispel type, rolling for success.
**EffectLanguage**: Teaches a language to the player.
**EffectDualWield**: Enables dual-wielding for player.
**EffectPull**: Stub for pull effect, logs debug message.
**EffectDistract**: Moves creature to a point using distract motion.
**EffectPickPocket**: Opens pickpocketing loot window.
**EffectAddFarsight**: Creates a farsight dynamic object and sets camera view.
**EffectSummonWild**: Summons wild creatures, handling multiple units and loot recipients.
**EffectSummonGuardian**: Summons guardian pets, managing multiple guardians and follow angles.
**EffectSummonPossessed**: Summons a possessed minion.
**EffectTeleUnitsFaceCaster**: Teleports target to face caster.
**EffectLearnSkill**: Increases player's skill level.
**EffectAddHonor**: Awards honor points to player.
**EffectSpawn**: Makes unit visible and removes spawning flag.
**EffectTradeSkill**: Stub for trade skill effect.
**EffectEnchantItemPerm**: Applies permanent enchantment to item.
**EffectEnchantItemTmp**: Applies temporary enchantment to item.
**EffectTameCreature**: Tames a creature, creating a hunter pet.
**EffectSummonPet**: Delegates to `Unit::EffectSummonPet` to summon a pet.
**EffectSummonPet#2**: Implementation of `Unit::EffectSummonPet` (owned by `Unit` unit), which creates, initializes, and saves the pet object.
**EffectLearnPetSpell**: Teaches a spell to the player's pet.
**EffectTaunt**: Forces target to attack caster, adjusting threat.
**EffectWeaponDmg**: Calculates and applies weapon-based damage.
**EffectThreat**: Adds flat threat to target.
**EffectHealMaxHealth**: Heals target for a percentage of max health.
**EffectInterruptCast**: Interrupts target's casting/channeling.
**EffectSummonObjectWild**: Summons a wild GameObject.
**EffectScriptEffect**: Handles scripted effects via ID switches and DB scripts.
**EffectSanctuary**: Implements stealth/vanish, removing threat and interrupting attacks.
**EffectAddComboPoints**: Adds combo points to target.
**EffectCreateHouse**: Summons player's house GameObject.
**EffectDuel**: Initiates a duel between two players.
**EffectStuck**: Teleports stuck player to last safe position.
**EffectSummonPlayer**: Sends summon request to player.
**EffectActivateObject**: Performs action on GameObject (open, close, etc.).
**EffectSummonTotem**: Summons a Shaman totem.
**EffectEnchantHeldItem**: Applies temporary enchantment to main-hand weapon.
**EffectDisEnchant**: Triggers disenchanting loot window.
**EffectInebriate**: Increases player's drunk value.
**EffectFeedPet**: Consumes food to increase pet happiness.
**EffectDismissPet**: Unsummons the player's pet.
**EffectSummonObject**: Summons a GameObject, managing slots.
**EffectResurrect**: Sends resurrection request to dead player.
**EffectAddExtraAttacks**: Grants extra attacks to target.
**EffectParry**: Enables parrying for player.
**EffectBlock**: Enables blocking for player.
**EffectLeapForward**: Teleports target to destination.
**EffectReputation**: Modifies player's reputation.
**EffectQuestComplete**: Marks quest as completed.
**EffectSelfResurrect**: Revives caster immediately.
**EffectSkinning**: Opens skinning loot window and updates skill.
**EffectCharge**: Stub for charge effect.
**EffectSummonCritter**: Summons a mini-pet.
**EffectKnockBack**: Pushes target away from caster.
**EffectSendTaxi**: Activates taxi path for player.
**EffectPlayerPull**: Pulls target toward caster.
**EffectDispelMechanic**: Removes auras with specific mechanic mask.
**EffectSummonDeadPet**: Revives dead pet at player's location.
**EffectDestroyAllTotems**: Unsummons all totems of caster.
**EffectDurabilityDamage**: Deals durability damage to items.
**EffectDurabilityDamagePCT**: Deals durability damage as percentage.
**EffectModifyThreatPercent**: Modifies caster's threat percentage.
**EffectTransmitted**: Summons a transmitted GameObject (e.g., fishing bobber, ritual).
**EffectSkill**: Stub for skill effect, logs debug message.
**EffectSummonDemon**: Summons a demon at ritual site.
**EffectSpiritHeal**: Revives player at graveyard.
**EffectSkinPlayerCorpse**: Removes insignia from player corpse.
**EffectBind**: Sets player's homebind.
**EffectDespawnObject**: Adds GameObject to removal list.
**EffectNostalrius**: Debug stub for custom effects.

---

<!-- machine-true, projected from graph.json -->

## Map — Spell.Effects

*Source:* SpellEffects.cpp, Spell.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| EffectEmpty | method | — | — | — |
| EffectNULL | method | Log.Main/Out | — | — |
| EffectUnused | method | — | — | — |
| EffectResurrectNew | method | Aura/GetId, Aura/GetModifier, Creature.Main/AIM_Initialize, ExecuteLogInfo/ExecuteLogInfo#2, Object/GetObjectGuid, Object/GetTypeId, Object/IsInWorld, Object/ToPet, Pet.Main/SavePetToDB, Pet.Main/SetDeathState, Player.Main/IsRessurectRequested, Player.Main/SetResurrectRequestData, Spell.Main/AddExecuteLogInfo, Spell.Main/SendResurrectRequest, Unit.Main/ClearUnitState, Unit.Main/GetAurasByType, Unit.Main/GetMaxHealth, Unit.Main/GetOwner, Unit.Main/IsAlive, Unit.Main/IsSpiritHealer, Unit.Main/RemoveAurasDueToSpell, Unit.Main/SetHealth, WorldObject.Object/GetInstanceId, WorldObject.Object/GetMapId, WorldObject.Object/GetOrientation, WorldObject.Object/GetPosition#3, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/RemoveFlag, WorldObject.Object/SetUInt32Value | — | — |
| EffectInstaKill | method | ByteBuffer/operator<<#10, Object/GetObjectGuid, ObjectGuid/operator<<, Spell.Main/finish, SpellCaster/DealDamage, Unit.Main/GetHealth, Unit.Main/IsAlive, WorldObject.Object/SendMessageToSet, WorldPacket/WorldPacket#4 | — | — |
| EffectEnvironmentalDMG | method | Object/GetTypeId, Player.Main/EnvironmentalDamage, shared_Util/dither, SpellCaster/SendSpellNonMeleeDamageLog#2, SpellEntry/GetSpellSchoolMask, Unit.Main/CalculateDamageAbsorbAndResist, Unit.Main/IsAlive | — | — |
| EffectSchoolDMG | method | Unit.Main/IsAlive | — | — |
| EffectDummy | method | BattleGround/EventPlayerClickedOnFlag, BattleGround/GetTypeID, Creature.Main/AI, Creature.Main/DespawnOrUnsummon, Creature.Main/ForcedDespawn, Creature.Main/GetRespawnTime, Creature.Main/IsPet, Creature.Main/SelectAttackingTarget, Creature.Main/SetDeathState, Creature.Main/ToCreature, Creature.Main/UpdateEntry, Creature.MotionMaster/MovePoint, CreatureAI/AttackStart, GameObject/Create, GameObject/Delete, GameObject/GameObject, GameObject/SetLootState, GameObject/SetOwnerGuid, GameObject/SetRespawnTime, GameObject/SetSpellId, InstanceData/CustomSpellCasted, InstanceData/SetData, Log.Main/Out, Map.Main/GenerateLocalLowGuid, Map.Main/GetCreature, Map.Main/GetGameObject, Map.Main/GetPlayer, Map.Main/ScriptCommandStart, Object/GetEntry, Object/GetFloatValue, Object/GetInt32Value, Object/GetObjectGuid, Object/GetTypeId, Object/GetUInt32Value, Object/IsCreature, Object/IsPlayer, Object/ToCreature, Object/ToPlayer, ObjectGuid/IsGameObject, ObjectGuid/ObjectGuid#5, ObjectGuid/operator<<, Player.Main/CanUseBattleGroundObject, Player.Main/GetBattleGround, Player.Main/GetSession, Player.Main/IsInSameRaidWith, Player.Main/KilledMonsterCredit, Player.Main/TeleportTo, Player.Main/ToPlayer, ScriptInfo/ScriptInfo, ScriptMgr/OnEffectDummy, ScriptMgr/OnEffectDummy#2, shared_Util/dither, shared_Util/irand, shared_Util/roll_chance_i, shared_Util/urand, Spell.Main/GetAffectiveCaster, SpellAuraHolder/GetSpellProto, SpellCaster/AddCooldown, SpellCaster/CastCustomSpell#2, SpellCaster/CastSpell#2, SpellCaster/DealDamage, SpellEntry/GetRecoveryTime, SpellMgr/GetPetAura, SpellMgr/GetSpellEntry, SpellMgr/Instance, ThreatManager/getThreat, ThreatManager/modifyThreatPercent#2, Unit.Main/AddAura, Unit.Main/AddPetAura, Unit.Main/AddUnitState, Unit.Main/CombatStop, Unit.Main/DeleteThreatList, Unit.Main/GetAttackTimer, Unit.Main/GetChannelObjectGuid, Unit.Main/GetCharmerOrOwner, Unit.Main/GetGender, Unit.Main/GetHealth, Unit.Main/GetHealthPercent, Unit.Main/GetMaxHealth, Unit.Main/GetMiniPet, Unit.Main/GetMotionMaster, Unit.Main/GetPowerType, Unit.Main/GetShapeshiftForm, Unit.Main/GetSpeedRate, Unit.Main/GetSpellAuraHolderMap, Unit.Main/GetThreatManager, Unit.Main/GetVictim, Unit.Main/HandleEmote, Unit.Main/HasAura#2, Unit.Main/HasAuraType, Unit.Main/HaveOffhandWeapon, Unit.Main/IsAlive, Unit.Main/IsDead, Unit.Main/IsInCombat, Unit.Main/IsStandingUp, Unit.Main/Kill, Unit.Main/ModifyAuraState, Unit.Main/RemoveAurasAtMechanicImmunity, Unit.Main/RemoveAurasDueToSpell, Unit.Main/RemoveAurasDueToSpellByCancel, Unit.Main/RemoveSpellsCausingAura, Unit.Main/SendSpellGo, Unit.Main/SetHealth, Unit.Main/SetPvPContested, Unit.Main/SetStandState, Unit.Main/SetVisibility, WorldObject.Object/FindNearestGameObject, WorldObject.Object/GetCombatDistance, WorldObject.Object/GetContactPoint, WorldObject.Object/GetInstanceData, WorldObject.Object/GetMap, WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/GetWorldMask, WorldObject.Object/IsWithinDistInMap, WorldObject.Object/PlayDirectSound, WorldObject.Object/SendMessageToSet, WorldObject.Object/SetFlag, WorldObject.Object/SetWorldMask, WorldObject.Object/SummonCreature#2, WorldObject.Object/SummonGameObject, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | — | — |
| EffectTriggerSpell | method | game_Objects_Item/IsFitToSpellRequirements, Log.Main/Out, Object/GetTypeId, Player.Main/GetWeaponForAttack#2, SpellCaster/CastSpell, SpellEntry/HasAttribute#5, SpellEntry/IsSpellWithCasterSourceTargetsOnly, SpellMgr/GetSpellEntry, SpellMgr/Instance | — | — |
| EffectTriggerMissileSpell | method | Log.Main/HasLogFilter, Log.Main/HasLogLevelOrHigher, Log.Main/Out, SpellCaster/CastSpell#3, SpellMgr/GetSpellEntry, SpellMgr/Instance | — | — |
| EffectTeleportUnits | method | Log.Main/Out, Object/GetTypeId, Player.Main/TeleportToHomebind, SpellMgr/GetSpellTargetPosition, SpellMgr/Instance, Unit.Main/IsTaxiFlying, Unit.Main/NearTeleportTo, WorldObject.Object/GetMapId, WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ | — | — |
| EffectApplyAura | method | Log.Main/HasLogFilter, Log.Main/HasLogLevelOrHigher, Log.Main/Out, Object/GetTypeId, ObjectGuid/IsGameObject, Player.Main/GetSession, Spell.Main/GetAffectiveCaster, SpellEntry/CanTargetDeadTarget, SpellEntry/IsDeathPersistentSpell, Unit.Main/HasMorePowerfulSpellActive, Unit.Main/IsAlive, Unit.SpellAuras/AddAura, Unit.SpellAuras/CreateAura, WorldSession.Main/PlayerLoading | — | — |
| EffectPowerDrain | method | ExecuteLogInfo/ExecuteLogInfo#2, Object/GetObjectGuid, Object/IsPet, shared_Util/dither, SpellCaster/SpellDamageBonusDone, Unit.Main/GetPower, Unit.Main/GetPowerType, Unit.Main/GetSpellModOwner, Unit.Main/IsAlive, Unit.Main/ModifyPower, Unit.Main/SpellDamageBonusTaken | — | — |
| EffectSendEvent | method | Log.Main/HasLogFilter, Log.Main/HasLogLevelOrHigher, Log.Main/Out, Map.Main/ScriptsStart, Object/GetObjectGuid, ObjectGuid/ObjectGuid, ScriptMgr/OnProcessEvent, SpellCastTargetsInfo/getGOTarget, WorldObject.Object/GetMap | — | — |
| EffectPowerBurn | method | Unit.Main/GetPower, Unit.Main/GetPowerType, Unit.Main/GetSpellModOwner, Unit.Main/IsAlive, Unit.Main/ModifyPower | — | — |
| EffectHeal | method | Spell.Main/GetAffectiveCasterObject, SpellCaster/SpellHealingBonusDone, Unit.Main/IsAlive, Unit.Main/SpellHealingBonusTaken | — | — |
| EffectHealMechanical | method | Spell.Main/GetAffectiveCasterObject, SpellCaster/DealHeal, SpellCaster/SpellHealingBonusDone, Unit.Main/IsAlive, Unit.Main/SpellHealingBonusTaken | — | — |
| EffectHealthLeech | method | Log.Main/HasLogFilter, Log.Main/HasLogLevelOrHigher, Log.Main/Out, shared_Util/ditheru, SpellCaster/DealHeal, SpellCaster/SpellDamageBonusDone, Unit.Main/GetHealth, Unit.Main/GetSpellModOwner, Unit.Main/IsAlive, Unit.Main/SpellDamageBonusTaken | — | — |
| DoCreateItem | method | BattleGroundMgr/GetBattleGroundTemplate, game_Battlegrounds_BattleGround/SendRewardMarkByMail, game_Objects_Item/GenerateItemRandomPropertyId, game_Objects_Item/GetProto, ItemPrototype/HasSignature, Object/GetObjectGuid, Object/SetGuidValue, ObjectMgr/GetItemPrototype, Player.Main/CanStoreNewItem, Player.Main/SendEquipError, Player.Main/SendNewItem, Player.Main/StoreNewItem, Player.Main/ToPlayer, Player.Main/UpdateCraftSkill | spell_warlock/OnEffectExecute#2 | — |
| EffectCreateItem | method | ExecuteLogInfo/ExecuteLogInfo, Spell.Main/AddExecuteLogInfo | — | — |
| EffectPersistentAA | method | DynamicObject/Create, DynamicObject/DynamicObject, GameObject/GetOwner, GameObject/ToGameObject, Map.Main/GenerateLocalLowGuid, Object/ToUnit, Spell.Main/GetAffectiveCasterObject, SpellCaster/AddDynObject, SpellEntry/GetSpellRadius, Unit.Main/GetSpellModOwner, WorldObject.Object/GetMap | — | — |
| EffectEnergize | method | SpellCaster/EnergizeBySpell, Unit.Main/GetMaxPower, Unit.Main/IsAlive | — | — |
| SendLoot | method | GameObject/GetGoType, GameObject/SetGoState, GameObject/SetLootState, GameObject/Use, InstanceData/SetData, Log.Main/Out, Object/GetEntry, Object/GetTypeId, Player.Main/SendLoot, WorldObject.Object/GetInstanceData | — | — |
| EffectOpenLock | method | BattleGround/EventPlayerClickedOnFlag, BattleGround/GetTypeID, ExecuteLogInfo/ExecuteLogInfo#2, GameObject/AddToSkillupList, GameObject/AI, GameObject/GetGOInfo, GameObject/GetOwner, GameObject/IsInSkillupList, GameObjectAI/OnUse, GameObjectInfo/CannotBeUsedUnderImmunity, GameObjectInfo/GetLockId, game_Objects_Item/GetProto, game_Objects_Item/SetState, Log.Main/Out, Object/GetObjectGuid, Object/HasFlag, Object/ToPlayer, ObjectGuid/ObjectGuid, Player.Main/GetBattleGround, Player.Main/GetSkillValuePure, Player.Main/UpdateGatherSkill, ScriptMgr/OnGameObjectOpen, Spell.Main/AddExecuteLogInfo, Spell.Main/CanOpenLock, Spell.Main/SendCastResult, Unit.Main/TogglePlayerPvPFlagOnAttackVictim, WorldObject.Object/SetFlag | — | — |
| EffectSummonChangeItem | method | game_Objects_Item/CreateItem, game_Objects_Item/GetBagSlot, game_Objects_Item/GetEnchantmentCharges, game_Objects_Item/GetEnchantmentDuration, game_Objects_Item/GetEnchantmentId, game_Objects_Item/GetOwnerGuid, game_Objects_Item/GetPos, game_Objects_Item/GetSlot, game_Objects_Item/SetEnchantment, Object/GetObjectGuid, Object/GetUInt32Value, Object/ToPlayer, ObjectGuid/operator!=, Player.Main/AutoUnequipOffhandIfNeed, Player.Main/BankItem, Player.Main/CanBankItem, Player.Main/CanEquipItem, Player.Main/CanStoreItem, Player.Main/DestroyItem, Player.Main/DurabilityLoss, Player.Main/EquipItem, Player.Main/IsBankPos#2, Player.Main/IsEquipmentPos#2, Player.Main/IsInventoryPos#2, Player.Main/StoreItem, Spell.Main/ClearCastItem | — | — |
| EffectProficiency | method | Player.Main/AddArmorProficiency, Player.Main/AddWeaponProficiency, Player.Main/GetArmorProficiency, Player.Main/GetWeaponProficiency, Player.Main/SendProficiency, Player.Main/ToPlayer | — | — |
| EffectApplyAreaAura | method | Unit.Main/IsAlive, Unit.SpellAuras/AddAura, Unit.SpellAuras/AreaAura | — | — |
| EffectSummon | method | Creature.Main/AI, Creature.Main/AIM_Initialize, Creature.Main/SetSummonPoint, CreatureAI/JustSummoned, CreatureCreatePos/CreatureCreatePos, CreatureCreatePos/CreatureCreatePos#2, ExecuteLogInfo/ExecuteLogInfo#2, Log.Main/Out, Map.Main/GenerateLocalLowGuid, Object/GetObjectGuid, Object/GetTypeId, Object/IsCreature, ObjectGuid/IsEmpty, ObjectMgr/GeneratePetNumber, ObjectMgr/GetCreatureTemplate, Pet.Main/Create, Pet.Main/InitializeDefaultName, Pet.Main/InitPetCreateSpells, Pet.Main/InitStatsForLevel, Pet.Main/LoadPetFromDB, Pet.Main/Pet, Pet.Main/SavePetToDB, Pet.Main/SetDuration, Player.Main/PetSpellInitialize, Player.StatSystem/UpdateAllStats#2, Spell.Main/AddExecuteLogInfo, SpellScript/OnSummon, Unit.Main/GetCharmInfo, Unit.Main/GetFactionTemplateId, Unit.Main/GetLevel, Unit.Main/GetMaxHealth, Unit.Main/GetMaxPower, Unit.Main/GetPetGuid, Unit.Main/IsPvP, Unit.Main/SetCreatorGuid, Unit.Main/SetFactionTemplateId, Unit.Main/SetHealth, Unit.Main/SetOwnerGuid, Unit.Main/SetPet, Unit.Main/SetPetNumber, Unit.Main/SetPower, Unit.Main/SetPvP, Unit.Main/SetReactState, WorldObject.Object/GetMap, WorldObject.Object/GetOrientation, WorldObject.Object/Relocate#2, WorldObject.Object/SetUInt32Value | — | — |
| EffectLearnSpell | method | Log.Main/Out, Object/GetGUIDLow, Object/GetTypeId, Player.Main/LearnSpell | — | — |
| EffectDispel | method | ByteBuffer/operator<<#10, CharmInfo/GetOriginalFactionTemplate, ExecuteLogInfo/ExecuteLogInfo#2, FactionTemplateEntry/IsFriendlyTo, Object/GetObjectGuid, Object/GetPackGUID, ObjectGuid/operator<<, ObjectGuid/operator<<#2, ObjectGuid/operator==, shared_Util/roll_chance_i, shared_Util/urand, Spell.Main/AddExecuteLogInfo, SpellAuraHolder/GetCasterGuid, SpellAuraHolder/GetSpellProto, SpellAuraHolder/GetStackAmount, SpellEntry/GetDispellMask, SpellEntry/IsCharmSpell, SpellScript/OnSuccessfulDispel, Unit.Main/GetCharmInfo, Unit.Main/GetSpellAuraHolderMap, Unit.Main/GetSpellModOwner, Unit.Main/IsFriendlyTo, Unit.Main/RemoveAuraHolderDueToSpellByDispel, Unit.SpellAuras/GetCaster, Unit.SpellAuras/GetId, Unit.SpellAuras/IsPositive, WorldObject.Object/GetFactionTemplateEntry, WorldObject.Object/SendMessageToSet, WorldPacket/WorldPacket#4 | — | — |
| EffectLanguage | method | Player.Main/LearnLanguage, Player.Main/ToPlayer | — | — |
| EffectDualWield | method | Object/GetTypeId, Player.Main/SetCanDualWield | — | — |
| EffectPull | method | Log.Main/Out | — | — |
| EffectDistract | method | Creature.MotionMaster/MoveDistract, ExecuteLogInfo/ExecuteLogInfo#2, Object/GetObjectGuid, Object/GetTypeId, Spell.Main/AddExecuteLogInfo, Unit.Main/ClearUnitState, Unit.Main/GetMotionMaster, Unit.Main/HasUnitState, Unit.Main/IsInCombat, Unit.Main/SetFacingTo, WorldObject.Object/GetAngle#2 | — | — |
| EffectPickPocket | method | Object/GetObjectGuid, Object/GetTypeId, Player.Main/SendLoot, Unit.Main/IsAlive, WorldObject.Object/IsFriendlyTo | — | — |
| EffectAddFarsight | method | Camera/SetView, DynamicObject/Create, DynamicObject/DynamicObject, Map.Main/GenerateLocalLowGuid, Object/GetTypeId, Player.Main/GetCamera, SpellCaster/AddDynObject, SpellEntry/GetDuration, WorldObject.Object/GetMap | — | — |
| EffectSummonWild | method | Creature.Main/HasStaticFlag, Creature.Main/SetLootRecipient, ExecuteLogInfo/ExecuteLogInfo#2, game_Objects_Item/GetProto, Object/GetObjectGuid, Object/GetTypeId, Player.Main/GetSkillValue, Spell.Main/AddExecuteLogInfo, SpellCaster/GetLevel, SpellEntry/GetDuration, SpellEntry/GetSpellRadius, SpellScript/OnSummon, Unit.Main/SetCreatorGuid, WorldObject.Object/GetClosePoint, WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/GetRandomPoint, WorldObject.Object/SetUInt32Value, WorldObject.Object/SummonCreature#2 | — | — |
| EffectSummonGuardian | method | Creature.Main/AI, Creature.Main/AIM_Initialize, Creature.Main/GetCreatureInfo, Creature.Main/LoadCreatureAddon, Creature.Main/SetSummonPoint, CreatureAI/JustSummoned, CreatureCreatePos/CreatureCreatePos, CreatureCreatePos/CreatureCreatePos#2, ExecuteLogInfo/ExecuteLogInfo#2, game_Objects_Item/GetProto, Log.Main/Out, Map.Main/GenerateLocalLowGuid, Object/GetObjectGuid, Object/GetTypeId, Object/IsCreature, ObjectGuid/IsEmpty, ObjectMgr/GeneratePetNumber, ObjectMgr/GetCreatureTemplate, Pet.Main/Create, Pet.Main/InitializeDefaultName, Pet.Main/InitStatsForLevel, Pet.Main/Pet, Pet.Main/SetDuration, Pet.Main/SetFollowAngle, Pet.Main/Unsummon, Player.Main/GetSkillValue, shared_Util/urand, Spell.Main/AddExecuteLogInfo, SpellEntry/GetSpellRadius, SpellScript/OnSummon, Unit.Main/AddGuardian, Unit.Main/FindGuardianWithEntry, Unit.Main/GetCharmInfo, Unit.Main/GetFactionTemplateId, Unit.Main/GetGuardianCountWithEntry, Unit.Main/GetGuardiansCount, Unit.Main/GetLevel, Unit.Main/GetPetGuid, Unit.Main/SetCreatorGuid, Unit.Main/SetFactionTemplateId, Unit.Main/SetOwnerGuid, Unit.Main/SetPetNumber, WorldObject.Object/GetMap, WorldObject.Object/GetOrientation, WorldObject.Object/GetRandomPoint, WorldObject.Object/SetUInt32Value | — | — |
| EffectSummonPossessed | method | CreatureAI/JustSummoned, Log.Main/Out, Object/ToPlayer, Player.Main/SummonPossessedMinion, SpellEntry/GetDuration, SpellScript/OnSummon, Unit.Main/AI, WorldObject.Object/GetOrientation | — | — |
| EffectTeleUnitsFaceCaster | method | SpellCastTargetsInfo/getDestination, SpellEntry/GetSpellRadius, Unit.Main/GetObjectBoundingRadius, Unit.Main/IsTaxiFlying, Unit.Main/NearTeleportTo, WorldObject.Object/GetClosePoint, WorldObject.Object/GetOrientation | — | — |
| EffectLearnSkill | method | Log.Main/Out, Object/GetGuidStr, Object/GetTypeId, Player.Main/GetSkillValuePure, Player.Main/SetSkill, Spell.Main/GetCastingObject | — | — |
| EffectAddHonor | method | HonorMgr/Add, Log.Main/HasLogFilter, Log.Main/HasLogLevelOrHigher, Log.Main/Out, Object/GetGUIDLow, Object/GetTypeId, Player.Main/GetHonorMgr | — | — |
| EffectSpawn | method | Object/GetTypeId, Unit.Main/GetVisibility, Unit.Main/SetVisibility, WorldObject.Object/RemoveFlag | — | — |
| EffectTradeSkill | method | Object/GetTypeId | — | — |
| EffectEnchantItemPerm | method | game_Objects_Item/GetOwner, game_Objects_Item/GetProto, game_Objects_Item/SetEnchantment, Object/GetEntry, Object/GetObjectGuid, Object/ToPlayer, Player.Main/ApplyEnchantment, Player.Main/GetName, Player.Main/GetSession, Player.Main/Player, Player.Main/UpdateCraftSkill, World/getConfig, WorldSession.Main/GetAccountId, WorldSession.Main/GetSecurity | — | — |
| EffectEnchantItemTmp | method | game_Objects_Item/GetOwner, game_Objects_Item/GetProto, game_Objects_Item/SetEnchantment, Log.Main/Out, Object/GetEntry, Object/GetObjectGuid, Object/ToPlayer, Player.Main/ApplyEnchantment, Player.Main/GetName, Player.Main/GetSession, Player.Main/Player, SpellMgr/GetSpellEnchantCharges, SpellMgr/Instance, World/getConfig, WorldSession.Main/GetAccountId, WorldSession.Main/GetSecurity | — | — |
| EffectTameCreature | method | CharmInfo/SetReactState, Creature.Main/AIM_Initialize, Creature.Main/ForcedDespawn, Log.Main/Out, Object/GetEntry, Object/GetObjectGuid, ObjectGuid/GetEntry, Pet.Main/CreateBaseAtCreature, Pet.Main/GetLoyaltyLevel, Pet.Main/GetStartLoyaltyPoints, Pet.Main/InitializeDefaultName, Pet.Main/InitPetCreateSpells, Pet.Main/InitStatsForLevel, Pet.Main/ModifyLoyalty, Pet.Main/Pet, Pet.Main/SavePetToDB, Player.Main/PetSpellInitialize, Spell.Main/finish, Spell.Main/GetAffectiveCaster, Unit.Main/GetCharmInfo, Unit.Main/GetFactionTemplateId, Unit.Main/GetLevel, Unit.Main/GetMaxHealth, Unit.Main/IsPvP, Unit.Main/SetCreatorGuid, Unit.Main/SetFactionTemplateId, Unit.Main/SetHealth, Unit.Main/SetOwnerGuid, Unit.Main/SetPet, Unit.Main/SetPetNumber, Unit.Main/SetPvP, World/getConfig#4, WorldObject.Object/GetMap, WorldObject.Object/SetUInt32Value | — | — |
| EffectSummonPet | method | ExecuteLogInfo/ExecuteLogInfo#2, Object/IsPlayer, Spell.Main/AddExecuteLogInfo, Unit.Main/GetLevel | — | — |
| EffectSummonPet#2 | method | Aura/GetId, Aura/GetModifier, CharmInfo/SetCommandState, Creature.Main/AIM_Initialize, Creature.Main/SetSummonPoint, CreatureCreatePos/CreatureCreatePos#2, Log.Main/Out, Map.Main/GenerateLocalLowGuid, Object/GetEntry, Object/GetGUIDLow, Object/GetObjectGuid, Object/GetTypeId, Object/GetUInt32Value, Object/ToPlayer, ObjectGuid/ObjectGuid, ObjectMgr/GeneratePetName, ObjectMgr/GeneratePetNumber, ObjectMgr/GetCreatureTemplate, Pet.Main/Create, Pet.Main/GetPetType, Pet.Main/InitializeDefaultName, Pet.Main/InitPetCreateSpells, Pet.Main/InitStatsForLevel, Pet.Main/LoadPetFromDB, Pet.Main/Pet, Pet.Main/SavePetToDB, Pet.Main/SetName, Pet.Main/SetPetType, Player.Main/PetSpellInitialize, Player.StatSystem/UpdateAllStats#2, Unit.Main/GetAurasByType, Unit.Main/GetCharmInfo, Unit.Main/GetFactionTemplateId, Unit.Main/GetMaxHealth, Unit.Main/GetMaxPower, Unit.Main/IsPvP, Unit.Main/RemoveAurasDueToSpell, Unit.Main/SetCreatorGuid, Unit.Main/SetFactionTemplateId, Unit.Main/SetHealth, Unit.Main/SetOwnerGuid, Unit.Main/SetPet, Unit.Main/SetPetNumber, Unit.Main/SetPower, Unit.Main/SetPvP, Unit.Main/SetReactState, Unit.Main/UnsummonOldPetBeforeNewSummon, WorldObject.Object/GetMap, WorldObject.Object/GetOrientation, WorldObject.Object/SetFlag, WorldObject.Object/SetUInt32Value | Player.Main/AutoReSummonPet | — |
| EffectLearnPetSpell | method | Object/ToPlayer, Pet.Main/CanLearnPetSpell, Pet.Main/GetTPForSpell, Pet.Main/LearnSpell, Pet.Main/SavePetToDB, Pet.Main/SetTP, Player.Main/PetSpellInitialize, SpellMgr/GetSpellEntry, SpellMgr/Instance, Unit.Main/GetPet, Unit.Main/IsAlive | — | — |
| EffectTaunt | method | ExecuteLogInfo/ExecuteLogInfo#2, HostileReference/getThreat, Object/GetObjectGuid, Object/GetTypeId, Spell.Main/AddExecuteLogInfo, Spell.Main/SendCastResult, ThreatManager/addThreat#3, ThreatManager/getCurrentVictim, ThreatManager/getThreat, ThreatManager/setCurrentVictimIfCan, Unit.Main/CanHaveThreatList, Unit.Main/GetThreatManager, Unit.Main/GetVictim | — | — |
| EffectWeaponDmg | method | Creature.Main/HasWeapon, Object/IsCreature, Spell.Main/CalculateDamage, SpellCaster/SpellDamageBonusDone, SpellDefines/GetSchoolMask, Unit.Main/CalculateDamage, Unit.Main/GetModifierValue, Unit.Main/GetWeaponDamageCount, Unit.Main/GetWeaponDamageSchool, Unit.Main/IsAlive, Unit.Main/IsImmuneToDamage, Unit.Main/SpellDamageBonusTaken | — | — |
| EffectThreat | method | ExecuteLogInfo/ExecuteLogInfo#2, Object/GetObjectGuid, Spell.Main/AddExecuteLogInfo, SpellEntry/GetSpellSchoolMask, Unit.Main/AddThreat, Unit.Main/CanHaveThreatList, Unit.Main/IsAlive | — | — |
| EffectHealMaxHealth | method | Aura/GetModifier, Unit.Main/GetAurasByType, Unit.Main/GetMaxHealth, Unit.Main/GetMaxNegativeAuraModifier, Unit.Main/GetMaxPositiveAuraModifier, Unit.Main/IsAlive | — | — |
| EffectInterruptCast | method | ExecuteLogInfo/ExecuteLogInfo#2, Object/GetObjectGuid, Spell.Main/AddExecuteLogInfo, Spell.Main/GetCastTime, Spell.Main/getState, SpellCaster/GetCurrentSpell, SpellCaster/InterruptSpell, SpellCaster/LockOutSpells, SpellEntry/GetDuration, SpellEntry/GetSpellSchoolMask, SpellEntry/HasChannelInterruptFlag, SpellEntry/HasSpellInterruptFlag, Unit.Main/IsAlive | — | — |
| EffectSummonObjectWild | method | BattleGround/GetStatus, BattleGround/GetTypeID, BattleGroundWS/SetDroppedFlagGuid, Creature.Main/AI, CreatureAI/JustSummoned#2, ExecuteLogInfo/ExecuteLogInfo#2, GameObject/AI, GameObject/Create, GameObject/GameObject, GameObject/GetGoType, GameObject/SetRespawnTime, GameObject/SetSpellId, GameObject/SummonLinkedTrapIfAny, GameObjectAI/JustSummoned#2, Map.Main/GenerateLocalLowGuid, Object/GetObjectGuid, Object/IsCreature, Object/IsGameObject, Object/IsPlayer, Player.Main/GetBattleGround, Player.Main/GetTeam, Spell.Main/AddExecuteLogInfo, SpellEntry/GetDuration, SpellScript/OnSummon#2, WorldObject.Object/GetMap, WorldObject.Object/GetMapId, WorldObject.Object/GetOrientation, WorldObject.Object/GetPosition#2, WorldObject.Object/GetWorldMask, WorldObject.Object/SetWorldMask | — | — |
| EffectScriptEffect | method | Aura/GetEffIndex, Aura/GetId, Aura/GetSpellProto, GameEventMgr.Main/IsActiveEvent, Group/GetFirstMember, GroupReference/next, InstanceData/SetData, Log.Main/HasLogFilter, Log.Main/HasLogLevelOrHigher, Log.Main/Out, Map.Main/GetInstanceData, Map.Main/ScriptsStart, MapEntry/IsMountAllowed, Object/GetEntry, Object/GetObjectGuid, Object/GetTypeId, Object/HasFlag, Object/IsPlayer, Object/ToPlayer, ObjectMgr/GetItemPrototype, Player.Main/GetGroup, Player.Main/GetMiniPet, Player.Main/HasItemCount, Player.Main/IsOutdoorPvPActive, Player.Main/RemoveMiniPet, Player.Main/ToPlayer, shared_Util/dither, shared_Util/irand, shared_Util/roll_chance_i, shared_Util/urand, SpellCaster/AddCooldown, SpellCaster/CastCustomSpell#2, SpellCaster/CastSpell#2, SpellEntry/CalculateSimpleValue, SpellEntry/GetAllSpellMechanicMask, SpellEntry/IsSealSpell, SpellMgr/GetSpellEntry, SpellMgr/Instance, ThreatManager/addThreat#3, ThreatManager/getThreat, ThreatManager/getThreatList, ThreatManager/modifyThreatPercent#2, Unit.Main/CanHaveThreatList, Unit.Main/GetAttackTime, Unit.Main/GetAurasByType, Unit.Main/GetGender, Unit.Main/GetRace, Unit.Main/GetThreatManager, Unit.Main/GetVictim, Unit.Main/HandleEmoteCommand, Unit.Main/HasAura#2, Unit.Main/IsAlive, Unit.Main/IsFriendlyTo, Unit.Main/IsInCombat, Unit.Main/Kill, Unit.Main/RemoveAuraHolderFromStack, Unit.Main/RemoveAurasAtMechanicImmunity, Unit.Main/RemoveAurasDueToSpell, Unit.Main/RemoveAurasDueToSpellByCancel, Unit.Main/RemoveSpellsCausingAura, Unit.Main/RemoveSpellsCausingAuraWithMechanic, Unit.Main/SetWalk, WorldObject.Object/FindNearestCreature, WorldObject.Object/GetAlivePlayerListInRange, WorldObject.Object/GetFactionTemplateId, WorldObject.Object/GetMap, WorldObject.Object/GetMapId, WorldObject.Object/IsWithinDist, WorldObject.Object/IsWithinDistInMap, WorldObject.Object/SetFlag | — | — |
| EffectSanctuary | method | ExecuteLogInfo/ExecuteLogInfo#2, HostileReference/next, HostileRefManager/getFirst, Object/GetObjectGuid, Object/IsPlayer, Player.Main/SetCannotBeDetectedTimer, shared_Util/getMSTime, Spell.Main/AddExecuteLogInfo, ThreatManager/getOwner, ThreatManager/removeReference, Unit.Main/CombatStop, Unit.Main/DoResetThreat, Unit.Main/GetHostileRefManager, Unit.Main/InterruptAttacksOnMe, Unit.Main/InterruptSpellsCastedOnMe, Unit.Main/IsContestedGuard, World/GetWowPatch | — | — |
| EffectAddComboPoints | method | Object/GetGUID, Object/GetTypeId, Player.Main/AddComboPoints, WorldObject.Object/SetUInt64Value | — | — |
| EffectCreateHouse | method | GameObject/SetSpellId, Object/ToPlayer, Unit.Main/RemoveGameObject#2, WorldObject.Object/SummonGameObject | — | — |
| EffectDuel | method | AreaEntry/GetById, GameObject/Create, GameObject/GameObject, GameObject/SetRespawnTime, GameObject/SetSpellId, Map.Main/GenerateLocalLowGuid, Object/GetGUIDLow, Object/GetObjectGuid, Object/IsPlayer, Object/SetGuidValue, ObjectGuid/operator<<, Player.Main/DuelComplete, Player.Main/GetSession, Player.Main/GetSocial, SocialMgr/HasIgnore, Spell.Main/SendCastResult, SpellEntry/GetDuration, Unit.Main/AddGameObject, Unit.Main/GetFactionTemplateId, Unit.Main/GetLevel, WorldObject.Object/FindMap, WorldObject.Object/GetAreaId, WorldObject.Object/GetMap, WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/GetTransport, WorldObject.Object/SetUInt32Value, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | — | — |
| EffectStuck | method | Log.Main/Out, Object/GetGUIDLow, Object/GetTypeId, Player.Main/GetName, Player.Main/TeleportTo, Unit.Main/IsTaxiFlying, World/getConfig, WorldObject.Object/GetMapId, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ | — | — |
| EffectSummonPlayer | method | GameObject/GetGoType, Object/GetObjectGuid, Player.Main/SendSummonRequest, Player.Main/ToPlayer, SpellCastTargetsInfo/getGOTarget, Unit.Main/GetObjectBoundingRadius, Unit.Main/HasAura#2, WorldObject.Object/GetClosePoint, WorldObject.Object/GetMapId, WorldObject.Object/GetZoneId | — | — |
| EffectActivateObject | method | GameObject/AI, GameObject/Despawn, GameObject/ResetDoorOrButton, GameObject/SendGameObjectCustomAnim, GameObject/SetLootState, GameObject/Use, GameObject/UseDoorOrButton, GameObjectAI/OnActivateBySpell, Log.Main/Out, WorldObject.Object/RemoveFlag, WorldObject.Object/SetFlag | — | — |
| EffectSummonTotem | method | Creature.Main/SetSummonPoint, CreatureCreatePos/CreatureCreatePos#2, ExecuteLogInfo/ExecuteLogInfo#2, Log.Main/Out, Map.Main/GenerateLocalLowGuid, Object/GetObjectGuid, Object/IsPlayer, ObjectMgr/GetCreatureTemplate, Spell.Main/AddExecuteLogInfo, SpellScript/OnSummon, Totem/Create, Totem/SetDuration, Totem/SetOwner, Totem/SetTypeBySummonSpell, Totem/Summon, Totem/Totem, Totem/UnSummon, Unit.Main/GetTotem, Unit.Main/IsPvP, Unit.Main/SetHealth, Unit.Main/SetMaxHealth, Unit.Main/SetPvP, Unit.Main/_AddTotem, WorldObject.Object/GetMap, WorldObject.Object/GetOrientation, WorldObject.Object/SetFlag, WorldObject.Object/SetUInt32Value | — | — |
| EffectEnchantHeldItem | method | game_Objects_Item/GetEnchantmentId, game_Objects_Item/IsEquipped, game_Objects_Item/SetEnchantment, Object/GetObjectGuid, Player.Main/ApplyEnchantment, Player.Main/GetItemByPos, Player.Main/ToPlayer, SpellEntry/GetDuration, SpellMgr/GetSpellEnchantCharges, SpellMgr/Instance | — | — |
| EffectDisEnchant | method | game_Objects_Item/GetProto, game_Objects_Item/SetBinding, Object/GetObjectGuid, Object/GetTypeId, Player.Main/SendLoot, Player.Main/UpdateCraftSkill | — | — |
| EffectInebriate | method | Object/GetEntry, Player.Main/GetDrunkValue, Player.Main/SetDrunkValue, Player.Main/ToPlayer | — | — |
| EffectFeedPet | method | ExecuteLogInfo/ExecuteLogInfo, game_Objects_Item/GetProto, Object/ToPlayer, Pet.Main/GetCurrentFoodBenefitLevel, Player.Main/DestroyItemCount, Spell.Main/AddExecuteLogInfo, Spell.Main/SendCastResult, SpellCaster/CastCustomSpell#2, SpellEntry/IsTargetInRange, Unit.Main/GetPet, Unit.Main/IsAlive, WorldObject.Object/IsWithinLOSInMap | — | — |
| EffectDismissPet | method | ExecuteLogInfo/ExecuteLogInfo#2, Object/GetObjectGuid, Object/ToPlayer, Pet.Main/Unsummon, Spell.Main/AddExecuteLogInfo, Unit.Main/GetPet, Unit.Main/IsAlive | — | — |
| EffectSummonObject | method | Creature.Main/AI, CreatureAI/JustSummoned#2, ExecuteLogInfo/ExecuteLogInfo#2, GameObject/Create, GameObject/GameObject, GameObject/SetLootState, GameObject/SetRespawnTime, GameObject/SetSpellId, GameObject/SummonLinkedTrapIfAny, Map.Main/GenerateLocalLowGuid, Map.Main/GetGameObject, Object/GetObjectGuid, Object/IsCreature, ObjectGuid/Clear, ObjectGuid/operator<<, Spell.Main/AddExecuteLogInfo, SpellEntry/GetDuration, SpellScript/OnSummon#2, Unit.Main/AddGameObject, Unit.Main/GetLevel, WorldObject.Object/GetClosePoint, WorldObject.Object/GetMap, WorldObject.Object/GetOrientation, WorldObject.Object/SendMessageToSet, WorldObject.Object/SetUInt32Value, WorldPacket/WorldPacket#4 | — | — |
| EffectResurrect | method | ExecuteLogInfo/ExecuteLogInfo#2, Object/GetObjectGuid, Object/GetTypeId, Object/IsInWorld, Player.Main/IsRessurectRequested, Player.Main/SetResurrectRequestData, shared_Util/ditheru, Spell.Main/AddExecuteLogInfo, Spell.Main/SendResurrectRequest, Unit.Main/GetMaxHealth, Unit.Main/GetMaxPower, Unit.Main/IsAlive, Unit.Main/IsSpiritHealer, WorldObject.Object/GetInstanceId, WorldObject.Object/GetMapId, WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ | — | — |
| EffectAddExtraAttacks | method | ExecuteLogInfo/ExecuteLogInfo#2, Object/GetObjectGuid, Spell.Main/AddExecuteLogInfo, Unit.Main/AddExtraAttackOnUpdate, Unit.Main/GetExtraAttacks, Unit.Main/IsAlive, Unit.Main/IsExtraAttacksLocked, Unit.Main/SetExtraAttaks | — | — |
| EffectParry | method | Object/GetTypeId, Player.Main/SetCanParry | — | — |
| EffectBlock | method | Object/GetTypeId, Player.Main/SetCanBlock | — | — |
| EffectLeapForward | method | SpellCastTargetsInfo/getDestination, Unit.Main/IsTaxiFlying, Unit.Main/NearTeleportTo, WorldObject.Object/GetOrientation | — | — |
| EffectReputation | method | ObjectMgr/GetFactionEntry, Player.Main/CalculateReputationGain, Player.Main/GetReputationMgr, Player.Main/ToPlayer, ReputationMgr/ModifyReputation | — | — |
| EffectQuestComplete | method | Object/GetTypeId, Player.Main/AreaExploredOrEventHappens | — | — |
| EffectSelfResurrect | method | Object/GetTypeId, Object/IsInWorld, Player.Main/ResurrectPlayer, Player.Main/SpawnCorpseBones, shared_Util/ditheru, Unit.Main/GetMaxHealth, Unit.Main/GetMaxPower, Unit.Main/IsAlive, Unit.Main/SetHealth, Unit.Main/SetPower | — | — |
| EffectSkinning | method | Creature.Main/IsElite, Object/GetObjectGuid, Object/GetTypeId, Object/IsInWorld, Player.Main/GetSkillValuePure, Player.Main/SendLoot, Player.Main/UpdateGatherSkill, Unit.Main/GetLevel, WorldObject.Object/RemoveFlag | — | — |
| EffectCharge | method | — | — | — |
| EffectSummonCritter | method | Creature.Main/AI, Creature.Main/AIM_Initialize, Creature.Main/GetCreatureInfo, Creature.Main/SelectLevel, Creature.Main/SetSummonPoint, CreatureAI/JustSummoned, CreatureCreatePos/CreatureCreatePos#2, ExecuteLogInfo/ExecuteLogInfo#2, Log.Main/Out, Map.Main/GenerateLocalLowGuid, Object/GetEntry, Object/GetObjectGuid, Object/IsCreature, Object/ToPlayer, ObjectMgr/GeneratePetNumber, ObjectMgr/GetCreatureTemplate, Pet.Main/Create, Pet.Main/InitializeDefaultName, Pet.Main/InitPetCreateSpells, Pet.Main/Pet, Pet.Main/SetDuration, Player.Main/GetMiniPet, Player.Main/RemoveMiniPet, Player.Main/_SetMiniPet, Spell.Main/AddExecuteLogInfo, SpellScript/OnSummon, Unit.Main/SetCreatorGuid, Unit.Main/SetFacingToObject, Unit.Main/SetFactionTemplateId, Unit.Main/SetOwnerGuid, WorldObject.Object/GetFactionTemplateId, WorldObject.Object/GetMap, WorldObject.Object/GetOrientation, WorldObject.Object/SetUInt32Value | — | — |
| EffectKnockBack | method | Unit.Main/IsTaxiFlying, Unit.Main/KnockBackFrom, Unit.Main/RemoveAurasDueToSpell | — | — |
| EffectSendTaxi | method | Object/GetTypeId, Player.Main/ActivateTaxiPathTo#2 | — | — |
| EffectPlayerPull | method | Unit.Main/KnockBackFrom, WorldObject.Object/GetDistance2d#3 | — | — |
| EffectDispelMechanic | method | ExecuteLogInfo/ExecuteLogInfo#2, Object/GetObjectGuid, Spell.Main/AddExecuteLogInfo, SpellAuraHolder/GetSpellProto, Unit.Main/GetSpellAuraHolderMap, Unit.Main/RemoveAurasDueToSpell, Unit.SpellAuras/HasMechanic | — | — |
| EffectSummonDeadPet | method | Creature.Main/AIM_Initialize, Object/ToPlayer, Pet.Main/SavePetToDB, Pet.Main/SetDeathState, Unit.Main/ClearUnitState, Unit.Main/GetMaxHealth, Unit.Main/GetPet, Unit.Main/IsAlive, Unit.Main/SetHealth, WorldObject.Object/GetPosition#3, WorldObject.Object/RemoveFlag, WorldObject.Object/SetUInt32Value | — | — |
| EffectDestroyAllTotems | method | Totem/UnSummon, Unit.Main/GetTotem | — | — |
| EffectDurabilityDamage | method | ExecuteLogInfo/ExecuteLogInfo#2, game_Objects_Item/GetProto, Object/GetObjectGuid, Object/GetTypeId, Player.Main/DurabilityPointsLoss, Player.Main/DurabilityPointsLossAll, Player.Main/GetItemByPos, Spell.Main/AddExecuteLogInfo | — | — |
| EffectDurabilityDamagePCT | method | Object/GetTypeId, Player.Main/DurabilityLoss, Player.Main/DurabilityLossAll, Player.Main/GetItemByPos | — | — |
| EffectModifyThreatPercent | method | ExecuteLogInfo/ExecuteLogInfo#2, Object/GetObjectGuid, Spell.Main/AddExecuteLogInfo, ThreatManager/modifyThreatPercent#2, Unit.Main/GetThreatManager | — | — |
| EffectTransmitted | method | Creature.Main/AI, CreatureAI/JustSummoned#2, ExecuteLogInfo/ExecuteLogInfo#2, GameObject/AddUniqueUse, GameObject/Create, GameObject/GameObject, GameObject/SetOwnerGroupId, GameObject/SetOwnerGuid, GameObject/SetRespawnTime, GameObject/SetSpellId, GameObject/SetSummonTarget, GameObject/SummonLinkedTrapIfAny, GridMap/IsSwimmable, Group/GetId, Log.Main/Out, Map.Main/GenerateLocalLowGuid, Map.Main/GetLosHitPosition, Map.Main/isInLineOfSight, Object/GetObjectGuid, Object/GetTypeId, Object/ToPlayer, ObjectMgr/GetGameObjectTemplate, Player.Main/GetGroup, Player.Main/GetSelectionGuid, shared_Util/rand_norm_f, Spell.Main/AddExecuteLogInfo, Spell.Main/finish, Spell.Main/SendCastResult, Spell.Main/SendChannelUpdate, Spell.Main/SetChannelingVisual, SpellCastTargetsInfo/setGOTarget, SpellEntry/GetDuration, SpellEntry/GetSpellMaxRange, SpellEntry/GetSpellMinRange, SpellEntry/GetSpellRadius, Unit.Main/AddGameObject, Unit.Main/GetLevel, Unit.Main/SetChannelObjectGuid, WorldObject.Object/GetMap, WorldObject.Object/GetObjectBoundingRadius, WorldObject.Object/GetOrientation, WorldObject.Object/GetPosition#2, WorldObject.Object/GetPositionZ, WorldObject.Object/GetTerrain, WorldObject.Object/SetUInt32Value | — | — |
| EffectSkill | method | Log.Main/Out | — | — |
| EffectSummonDemon | method | ExecuteLogInfo/ExecuteLogInfo#2, GameObject/GetGoType, Object/GetObjectGuid, Spell.Main/AddExecuteLogInfo, SpellCaster/GetLevel, SpellCastTargetsInfo/getGOTarget, SpellScript/OnSummon, Unit.Main/SetLevel, WorldObject.Object/GetOrientation, WorldObject.Object/GetPosition#2, WorldObject.Object/SummonCreature#2 | — | — |
| EffectSpiritHeal | method | BattleGround/GetStatus, Object/GetTypeId, Object/IsInWorld, Object/ToPlayer, Player.Main/AutoReSummonPet, Player.Main/GetBattleGround, Player.Main/IsGameMaster, Player.Main/RepopAtGraveyard, Player.Main/ResurrectPlayer, Player.Main/SpawnCorpseBones, Unit.Main/HasAura#2, Unit.Main/IsAlive, Unit.Main/RemoveAurasDueToSpell | — | — |
| EffectSkinPlayerCorpse | method | Corpse/GetOwnerGuid, Errors/PrintStacktraceAndThrow, ExecuteLogInfo/ExecuteLogInfo#2, Log.Main/Out, Object/GetObjectGuid, Object/GetTypeId, Object/ToPlayer, ObjectAccessor/ConvertCorpseForPlayer, ObjectAccessor/FindPlayer, Player.Main/RemovedInsignia, Player.Main/SendLoot, Spell.Main/AddExecuteLogInfo, Unit.Main/IsAlive | — | — |
| EffectBind | method | ByteBuffer/operator<<#10, ByteBuffer/operator<<#9, Log.Main/Out, Object/GetObjectGuid, ObjectGuid/operator<<, Player.Main/SendDirectMessage, Player.Main/SetHomebindToLocation, Player.Main/ToPlayer, WorldLocation/WorldLocation#2, WorldObject.Object/GetAreaId, WorldObject.Object/GetPosition, WorldPacket/Initialize, WorldPacket/WorldPacket#4 | — | — |
| EffectDespawnObject | method | Log.Main/Out, WorldObject.Object/AddObjectToRemoveList | — | — |
| EffectNostalrius | method | Log.Main/Out | — | — |

---

<!-- verify: boundary-bleed | foreign: cast, Execute, Spell, update -->
