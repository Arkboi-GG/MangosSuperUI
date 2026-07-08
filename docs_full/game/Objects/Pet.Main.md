<!-- provenance: no-member-reference-section -->
# Pet.Main

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Pet

The `Pet` class represents a creature entity that is owned and controlled by a player or another creature, distinct from standard wild or NPC creatures. It extends `Creature` to provide specialized behavior for four specific pet types: **Summon Pets** (e.g., Warlock demons, Mage elementals), **Hunter Pets** (tamed beasts), **Guardian Pets** (temporary summons like Warlock guardians), and **Mini Pets** (cosmetic companions).

`Pet` manages the complete lifecycle of these entities, including creation, persistence to the database, stat initialization based on level and type, and complex subsystems for loyalty, happiness, focus regeneration, and spell learning. It acts as the central authority for pet-specific data, coordinating with `CharmInfo` (owned by `Unit`) for command states and action bars, and with `Player` for ownership links and group updates.

## Purpose & Responsibilities

1.  **Lifecycle Management**: Handles creation (`Create`, `CreateBaseAtCreature`), loading from the database (`LoadPetFromDB`), saving (`SavePetToDB`), and removal (`Unsummon`, `DeleteFromDB`). It ensures pets are correctly registered in the world map and removed when they die, expire, or are dismissed.
2.  **Stat Initialization & Scaling**: Calculates health, mana, damage, armor, and resistances based on the pet's type, level, and the owner's level. Hunter pets scale with the owner's level and use specific formulas, while Summon pets often match the owner's level exactly.
3.  **Persistence**: Serializes and deserializes pet state (spells, auras, cooldowns, stats, loyalty) to and from the `character_pet`, `pet_spell`, `pet_aura`, and `pet_spell_cooldown` database tables.
4.  **Hunter Pet Subsystems**: Implements mechanics unique to Hunter pets, including:
    *   **Loyalty**: Tracks loyalty points and levels (Rebellious to Best Friend), affecting spell retention and happiness decay.
    *   **Happiness**: Manages a happiness meter that decays over time or during combat, affecting damage output.
    *   **Focus**: Regenerates Focus power, modified by auras and configuration rates.
    *   **Training Points**: Manages the currency used to learn new spells, tracking spent and available points.
5.  **Spell Management**: Maintains the pet's spellbook (`m_petSpells`), handling learning, unlearning, autocasting toggles, and spell rank upgrades. It also handles "teach spells" where the pet can teach abilities back to the hunter.
6.  **Behavioral State**: Manages follow angles, enabled/disabled states (e.g., when the owner mounts), and death/corpse handling (despawn timers).

## Member-by-Member Behavior

### Lifecycle and World Interaction

*   **Pet**: Constructor initializes the pet as a `CREATURE_SUBTYPE_PET`. It sets up `CharmInfo` via `Unit.Main/InitCharmInfo`. Depending on the `PetType`, it sets the default reaction state (Passive for Mini Pets, Aggressive for Guardians) and follow angle.
*   **~Pet**: Destructor. Currently empty, relying on base class cleanup.
*   **AddToWorld**: Registers the pet in the map's object storage via `WorldObject.Object/GetMap`. It resets command flags (Attack, Follow, Stay, Returning) to ensure the pet starts in a clean "follow" state, preventing stuck movement bugs when zoning.
*   **RemoveFromWorld**: Removes the pet from the map's object storage. It calls `Unit.Main/RemoveFromWorld` but explicitly avoids calling `Creature::RemoveFromWorld` to prevent conflicts with standard creature storage mechanisms.
*   **Create**: Low-level creation method. Initializes the GUID, entry, and position. Sets basic flags and byte values. For Mini Pets, it applies immunity flags and loads default auras.
*   **CreateBaseAtCreature**: Creates a new pet based on an existing creature (used for taming). It copies the creature's display ID, level, and orientation. It initializes happiness, loyalty, and XP fields. For beasts, it sets specific byte values for class/gender/power type and sets the initial loyalty to Rebellious.
*   **LoadPetFromDB**: Loads a persistent pet from the database. It queries `CharacterDatabaseCache` for pet data. It validates the creature template and checks if the pet is temporary. It creates the pet object, sets its position relative to the owner, and initializes stats, spells, auras, and cooldowns. It handles slot management (current vs. stable) and updates the database if the pet is being moved to the current slot.
*   **SavePetToDB**: Persists the pet's state to the database. It determines the save mode (current, stable, deleted, etc.). It removes auras if saving to stable slots (non-Hunter pets). It deletes the old record and inserts a new one with updated stats, spells, auras, and cooldowns. It updates the `CharacterDatabaseCache`.
*   **DeleteFromDB#2**: Static method to permanently remove a pet and its associated data (spells, auras, cooldowns) from the database. It executes DELETE statements on `character_pet`, `pet_aura`, `pet_spell`, and `pet_spell_cooldown` and updates the cache.
*   **Unsummon**: Removes the pet from the world. It stops combat, returns reagents if applicable (for Warlock pets), clears owner references, removes charm/possession auras, and saves the pet to the database based on the provided mode. It adds the pet to the removal list.
*   **DelayedUnsummon**: Schedules an `UnsummonPetDelayEvent` to unsummon the pet after a specified delay.

### Stat Initialization and Updates

*   **InitStatsForLevel**: Core method for calculating pet stats. It branches by `PetType`:
    *   **SUMMON_PET**: Uses `PetLevelInfo` from the database or falls back to class stats multiplied by creature template multipliers.
    *   **HUNTER_PET**: Uses specific formulas for damage and stats from `PetLevelInfo`. It scales the pet's model size based on level and family constraints.
    *   **GUARDIAN_PET**: Uses class stats and template multipliers.
    *   It sets attack times, resistances, and flags. It calls `Player.StatSystem/UpdateAllStats` to finalize calculations.
*   **SynchronizeLevelWithOwner**: Ensures Summon Pets match the owner's level exactly, and Hunter Pets do not exceed the owner's level. Calls `GivePetLevel` if adjustments are needed.
*   **GivePetXP**: Awards experience to the pet. It applies the owner's personal XP rate. If the pet levels up, it calls `GivePetLevel` and awards loyalty bonuses.
*   **GivePetLevel**: Handles the level-up process. Resets XP, sets next level XP, re-initializes stats, and adjusts training points based on loyalty.

### Hunter Pet Subsystems (Loyalty, Happiness, Focus)

*   **RegenerateAll**: Overrides the base regeneration loop. In addition to health/mana, it triggers `RegenerateFocus`, `LooseHappiness`, and `TickLoyaltyChange` on their respective timers for Hunter Pets.
*   **RegenerateFocus**: Increases Focus power based on a configurable rate and modifiers from auras.
*   **LooseHappiness**: Decreases happiness over time. The decay rate is faster if the pet is in combat and depends on the loyalty level.
*   **TickLoyaltyChange**: Adjusts loyalty points based on the current happiness state (Happy: +20, Content: +10, Unhappy: -20).
*   **ModifyLoyalty**: Applies changes to loyalty points. If points exceed the maximum for the current level, the pet levels up in loyalty. If points drop below zero, the pet levels down, potentially losing training points. If loyalty drops to Rebellious and points go negative, the pet runs away (`Unsummon`).
*   **GetHappinessState**: Returns the current happiness state (Happy, Content, Unhappy) based on the happiness power value.
*   **GetLoyaltyLevel**: Retrieves the current loyalty level from unit bytes.
*   **SetLoyaltyLevel**: Sets the loyalty level in unit bytes.
*   **GetMaxLoyaltyPoints** / **GetStartLoyaltyPoints**: Helper methods to retrieve loyalty thresholds from static arrays.
*   **KillLoyaltyBonus**: Awards extra loyalty points upon leveling up, based on the pet's level and current loyalty.

### Spell and Ability Management

*   **AddSpell**: Adds a spell to the pet's spellbook. It handles rank replacements (unlearning lower ranks if a higher rank is added). It sets the spell's active state (passive, enabled, disabled) and adds it to the action bar if applicable.
*   **LearnSpell**: Wrapper for `AddSpell` that also triggers `Player.Main/PetSpellInitialize` if not loading.
*   **UnlearnSpell** / **RemoveSpell**: Removes a spell from the spellbook. It can optionally learn the previous rank and clear the action bar. It marks the spell as `PETSPELL_REMOVED` for database synchronization.
*   **HasSpell**: Checks if a spell is in the spellbook and not marked as removed.
*   **ToggleAutocast**: Enables or disables autocasting for a spell. Updates the internal `m_autospells` list and the spell's active state.
*   **CanTakeMoreActiveSpells**: Checks if the pet can learn more active spells (limit of 4 distinct spell chains).
*   **HasTPForSpell** / **GetTPForSpell**: Determines if the pet has enough Training Points to learn a spell and calculates the cost.
*   **SetTP** / **GetDispTP**: Manages training points. `GetDispTP` returns a negative value for display purposes, indicating remaining points.
*   **CanLearnPetSpell**: Checks if a spell is valid for the pet's skill line.
*   **GetSkillIdForPetTraining**: Retrieves the skill line ID associated with the pet's creature family, used to validate which spells the pet can learn.
*   **CheckLearning**: Randomly allows the pet to teach a spell to the owner if the spell is in the `m_teachspells` map.
*   **InitPetCreateSpells**: Initializes the spellbook for a newly created/tamed pet. It loads default spells from `PetCreateSpellEntry`, learns passives, and sets initial training points.
*   **LearnPetPassives**: Loads passive spells specific to the pet's family from `sPetFamilySpellsStore`.
*   **CastPetAuras** / **CastPetAura**: Applies auras from the owner's `m_petAuras` set to the pet. Used for glyphs and talents that affect pets.
*   **CleanupActionBar**: Removes invalid spells from the action bar after loading.

### Persistence Helpers

*   **SaveToDB**: Private override that asserts failure. `Pet` instances should never be saved via the base `Unit::SaveToDB` mechanism; they must use `SavePetToDB`.
*   **DeleteFromDB**: Private override that asserts failure. `Pet` instances should never be deleted via the base `Unit::DeleteFromDB` mechanism; they must use the static `DeleteFromDB#2`.
*   **_LoadSpells** / **_SaveSpells**: Loads/saves the pet's spellbook to/from `pet_spell`.
*   **_LoadAuras** / **_SaveAuras**: Loads/saves active auras to/from `pet_aura`. Handles duration decay for negative spells.
*   **_LoadSpellCooldowns** / **_SaveSpellCooldowns**: Loads/saves spell cooldowns to/from `pet_spell_cooldown`. Sends cooldown packets to the client.

### Utility and State

*   **GetPetType** / **SetPetType**: Accessors for the pet type.
*   **IsControlled**: Returns true for Summon and Hunter pets.
*   **IsTemporarySummoned**: Returns true if the pet has a duration timer.
*   **IsPermanentPetFor**: Determines if the pet is considered permanent (Hunter pets, Warlock demons).
*   **GetName** / **SetName**: Accessors for the pet's name.
*   **InitializeDefaultName**: Sets the pet's name based on its type and family.
*   **GetNameForLocaleIdx**: Returns the localized name.
*   **SetDeathState**: Overrides death handling. For Hunter pets, corpses decay after 1 hour; others after 15 seconds. It removes happiness on death (outside battlegrounds) and casts pet auras on resurrection.
*   **Update**: Overrides the update loop. It checks for owner validity, distance, and duration expiration. It handles corpse decay timers.
*   **SetEnabled**: Toggles the pet's enabled state (e.g., when owner mounts). Sends `SMSG_PET_MODE` to the client.
*   **IsEnabled**: Returns the enabled state.
*   **GetFollowAngle** / **SetFollowAngle**: Accessors for the follow angle.
*   **SetDuration**: Sets the duration timer for temporary pets, typically used by spell effects to define how long a summoned pet persists.
*   **GetBonusDamage** / **SetBonusDamage**: Accessors for bonus damage (used in happiness calculations).
*   **HaveInDiet**: Checks if an item is suitable food for the pet based on its family's diet mask.
*   **GetCurrentFoodBenefitLevel**: Calculates the happiness benefit from feeding the pet, based on level difference.
*   **GetResetTalentsCost**: Calculates the cost to reset pet talents, increasing with frequency.
*   **RemoveAllCooldowns**: Clears all spell cooldowns and notifies the client.
*   **GetAuraUpdateMask** / **SetAuraUpdateSlot** / **SetAuraUpdateMask** / **ResetAuraUpdateMask**: Manages a bitmask for aura updates, used for group updates.
*   **GetPetAutoSpellSize** / **GetPetAutoSpellOnPos**: Accessors for the autocast spell list.
*   **AddTeachSpell**: Adds a spell to the `m_teachspells` map, allowing the pet to teach it to the owner.
*   **Execute** (UnsummonPetDelayEvent): Event handler that calls `Unsummon` on the pet.

## Cross-Unit Boundaries

*   **Player.Main**: `Pet` is heavily integrated with `Player`. `Player.Main/LoadPet` and `Player.Main/ResummonPetTemporaryUnSummonedIfAny` trigger `LoadPetFromDB`. `Player.Main/SetPet` links the pet to the owner. `Player.Main/PetSpellInitialize` updates the owner's UI when spells change. `Player.Main/GetGroup` and `Player.Main/SetGroupUpdateFlag` manage group visibility. `Player.Main/IsPetNeedBeTemporaryUnSummonedIfAny` handles temporary unsummoning logic.
*   **CharmInfo**: `Pet` uses `CharmInfo` (via `Unit.Main/GetCharmInfo`) to manage command states (`COMMAND_FOLLOW`, `COMMAND_ATTACK`), action bars, and pet number. `CharmInfo/HasCommandState` is used in `AddToWorld` to reset flags.
*   **Creature.Main**: `Pet` inherits from `Creature`. It calls `Creature.Main/AIM_Initialize` to set up AI. `Creature.Main/GetCreatureInfo` provides template data. `Creature.Main/SetDeathState` and `Creature.Main/Update` are overridden.
*   **Unit.Main**: `Pet` inherits from `Unit` (via `Creature`). It uses `Unit.Main/InitCharmInfo`, `Unit.Main/SetReactState`, `Unit.Main/AddToWorld`, `Unit.Main/RemoveFromWorld`, `Unit.Main/SetOwnerGuid`, `Unit.Main/SetPet`, `Unit.Main/GetOwner`, `Unit.Main/GetOwnerPlayer`, `Unit.Main/GetLevel`, `Unit.Main/SetLevel`, `Unit.Main/SetHealth`, `Unit.Main/SetPower`, `Unit.Main/GetPower`, `Unit.Main/GetMaxHealth`, `Unit.Main/GetMaxPower`, `Unit.Main/SetMaxPower`, `Unit.Main/SetDisplayId`, `Unit.Main/SetNativeDisplayId`, `Unit.Main/SetFactionTemplateId`, `Unit.Main/SetCreatorGuid`, `Unit.Main/SetCanModifyStats`, `Unit.Main/SetAttackTime`, `Unit.Main/SetBaseWeaponDamage`, `Unit.Main/SetCreateHealth`, `Unit.Main/SetCreateMana`, `Unit.Main/SetCreateResistance`, `Unit.Main/SetCreateStat`, `Unit.Main/SetSheath`, `Unit.Main/SetPvP`, `Unit.Main/IsPvP`, `Unit.Main/IsInCombat`, `Unit.Main/IsPolymorphed`, `Unit.Main/IsDead`, `Unit.Main/IsAlive`, `Unit.Main/GetAttackerForHelper`, `Unit.Main/GetCharmGuid`, `Unit.Main/GetPetGuid`, `Unit.Main/GetCharmerGuid`, `Unit.Main/GetPossessorGuid`, `Unit.Main/GetUnit`, `Unit.Main/RemoveCharmAuras`, `Unit.Main/RemoveAurasDueToSpell`, `Unit.Main/RemoveGuardian`, `Unit.Main/SetPet`, `Unit.Main/CombatStop`, `Unit.Main/GetCreatePowers`, `Unit.Main/GetFactionTemplateId`, `Unit.Main/GetLevel`, `Unit.Main/GetMaxHealth`, `Unit.Main/GetMaxPower`, `Unit.Main/IsMounted`, `Unit.Main/LoadPetActionBar`, `Unit.Main/SetCanModifyStats`, `Unit.Main/SetCreatorGuid`, `Unit.Main/SetDisplayId`, `Unit.Main/SetFactionTemplateId`, `Unit.Main/SetHealth`, `Unit.Main/SetMaxPower`, `Unit.Main/SetNativeDisplayId`, `Unit.Main/SetOwnerGuid`, `Unit.Main/SetPet`, `Unit.Main/SetPetNumber`, `Unit.Main/SetPower`, `Unit.Main/SetPowerType`, `Unit.Main/SetReactState`, `Unit.Main/UpdateAuraForGroup`, `Unit.Main/GetCharmerOrOwnerPlayerOrPlayerItself`, `Unit.Main/RemoveAllAuras`, `Unit.Main/AddSpellAuraHolder`, `Unit.Main/RemoveAllAuras`, `Unit.Main/GetSpellAuraHolderMap`, `Unit.Main/UpdateModelData`, `Unit.Main/GetAttackTime`, `Unit.Main/GetMaxHealth`, `Unit.Main/GetMaxPower`, `Unit.Main/GetOwner`, `Unit.Main/IsPvP`, `Unit.Main/SetAttackTime`, `Unit.Main/SetBaseWeaponDamage`, `Unit.Main/SetCreateHealth`, `Unit.Main/SetCreateMana`, `Unit.Main/SetCreateResistance`, `Unit.Main/SetCreateStat`, `Unit.Main/SetHealth`, `Unit.Main/SetLevel`, `Unit.Main/SetPower`, `Unit.Main/SetPvP`, `Unit.Main/SetSheath`, `Unit.Main/UpdateModelData`, `Unit.Main/GetAurasByType`, `Unit.Main/ModifyPower`, `Unit.Main/GetPower`, `Unit.Main/IsInCombat`, `Unit.Main/GetLevel`, `Unit.Main/GetOwnerPlayer`, `Unit.Main/GetSession`, `Unit.Main/GetOwner`, `Unit.Main/IsAlive`, `Unit.Main/GetUInt32Value`, `Unit.Main/SetUInt32Value`, `Unit.Main/RemoveFlag`, `Unit.Main/SetFlag`, `Unit.Main/SetByteValue`, `Unit.Main/SetFloatValue`, `Unit.Main/SetObjectScale`, `Unit.Main/GetFloatValue`, `Unit.Main/GetInt32Value`, `Unit.Main/GetCreatureInfo`, `Unit.Main/GetClass`, `Unit.Main/GetNameForLocaleIdx`, `Unit.Main/GetObjectGuid`, `Unit.Main/GetGuidStr`, `Unit.Main/GetTypeId`, `Unit.Main/GetEntry`, `Unit.Main/GetGUIDLow`, `Unit.Main/GetMap`, `Unit.Main/GetTransport`, `Unit.Main/GetPositionX`, `Unit.Main/GetPositionY`, `Unit.Main/GetOrientation`, `Unit.Main/IsInWorld`.
*   **Spell.Effects**: Various spell effects trigger pet creation (`EffectSummon`, `EffectSummonCritter`, `EffectSummonGuardian`, `EffectSummonPet#2`, `EffectTameCreature`). `EffectLearnPetSpell` triggers `LearnSpell`. `EffectFeedPet` uses `GetCurrentFoodBenefitLevel`. `EffectDismissPet` triggers `Unsummon`. `EffectResurrectNew` and `EffectSummonDeadPet` interact with `SetDeathState`.
*   **WorldSession.NPCHandler**: `HandleStablePet`, `HandleStableSwapPet`, `HandleUnstablePet` interact with pet saving/loading. `SendStablePet` uses `GetName` and `GetLoyaltyLevel`.
*   **WorldSession.PetHandler**: `HandlePetAbandon`, `HandlePetCastSpellOpcode`, `HandlePetRename`, `HandlePetUnlearnOpcode`, `HandlePetAction`, `HandlePetSetAction` interact with pet management.
*   **ChatHandler.CharacterCommands**: `HandlePetInfoCommand`, `HandlePetLoyaltyCommand`, `HandlePetLearnSpellCommand`, `HandlePetUnlearnSpellCommand` use pet data for commands.
*   **CharacterDatabaseCache**: Used for querying and caching pet data (`GetCharacterPetCacheByOwnerAndId`, `GetCharacterCurrentPet`, `GetCharacterPetByOwner`, `GetCharacterPetByOwnerAndEntry`, `CharacterPetSetOthersNotInSlot`, `InsertCharacterPet`, `DeleteCharacterPetById`).
*   **Database**: Direct database interactions for saving/loading (`BeginTransaction`, `CommitTransaction`, `CreateStatement`, `PExecute`).
*   **ObjectMgr**: Provides template data (`GetCreatureTemplate`, `GetPetLevelInfo`, `GetXPForPetLevel`, `GeneratePetName`, `GeneratePetNumber`, `GetPetCreateSpellEntry`, `GetDBCLocaleIndex`).
*   **SpellMgr**: Provides spell data (`GetSpellEntry`, `GetSpellRank`, `IsHighRankOfSpell`, `IsRankSpellDueToSpell`, `GetFirstSpellInChain`, `GetPrevSpellInChain`, `GetSkillLineAbilityMapBoundsBySpellId`, `Instance`).
*   **World**: Provides configuration (`getConfig`) and time (`GetGameTime`, `GetCurrentClockTime`).
*   **Log**: Logging (`Out`).
*   **Map.Main**: Map interaction (`GenerateLocalLowGuid`, `GetMapEntry`).
*   **Object**: Base object functionality (`GetObjectGuid`, `GetEntry`, `GetGUIDLow`, `GetGuidStr`, `GetTypeId`, `GetUInt32Value`, `GetFloatValue`, `GetInt32Value`, `GetMap`, `GetTransport`, `GetPositionX`, `GetPositionY`, `GetOrientation`, `IsInWorld`, `RemoveFlag`, `SetFlag`, `SetByteValue`, `SetUInt32Value`, `SetFloatValue`, `SetObjectScale`).
*   **CreatureCreatePos**: Position calculation (`CreatureCreatePos#2`, `GetMap`, `SelectFinalPoint`, `Relocate`).
*   **Transport**: Transport interaction (`AddPassenger`, `UpdatePassengerPosition`).
*   **Aura**: Aura manipulation (`GetModifier`, `SetLoadedState`).
*   **SpellAuraHolder**: Aura holder manipulation (`SetLoadedState`, `AddAura`, `CreateAura`, `CreateSpellAuraHolder`, `IsEmptyHolder`, `GetAuraByEffectIndex`, `GetAuraCharges`, `GetAuraDuration`, `GetAuraMaxDuration`, `GetCasterGuid`, `GetCastItemGuid`, `GetSpellProto`, `GetStackAmount`, `IsPassive`, `IsSingleTarget`, `GetId`).
*   **SpellEntry**: Spell data (`GetDuration`, `IsPassiveSpell`, `HasSingleTargetAura`, `IsPositiveSpell`, `IsChanneledSpell`, `StackAmount`, `procCharges`, `EffectApplyAuraName`, `Effect`).
*   **CooldownContainer**: Cooldown management (`AddCooldown`).
*   **CooldownData**: Cooldown data (`GetSpellCDExpireTime`, `IsPermanent`).
*   **WorldPacket**: Packet construction (`WorldPacket#4`, `operator<<#7`, `append#3`, `size`).
*   **WorldSession.Main**: Packet sending (`SendPacket`).
*   **shared_Util**: String splitting (`StrSplit`).
*   **SqlPreparedStatement** / **SqlStatement** / **SqlStatementID**: Database statement handling.
*   **ByteBuffer**: Buffer manipulation (`ByteBuffer`, `operator<<#10`, `append#3`, `size`).
*   **ObjectGuid**: GUID manipulation (`GetCounter`, `GetRawValue`, `IsPlayer`, `operator!=`, `operator==`, `operator<<`).
*   **UnitActionBarEntry**: Action bar entry data (`GetAction`, `GetType`, `IsActionBarForSpell`).
*   **CharmInfo**: Charm info data (`GetPetNumber`, `GetActionBarEntry`, `SetActionBar`, `HasCommandState`, `SetIsCommandAttack`, `SetIsCommandFollow`, `SetIsAtStay`, `SetIsFollowing`, `SetIsReturning`, `LoadPetActionBar`, `InitPetActionBar`, `AddSpellToActionBar`, `RemoveSpellFromActionBar`, `GetCommandState`, `GetReactState`).
*   **CreatureAI**: AI interaction (`SummonedCreatureDespawn`).
*   **CreatureAISelector**: AI selection (`selectAI`).
*   **PetAI**: AI interaction (`UpdateAI`, `HandleReturnMovement`, `AttackedBy`, `CanAttack`, `OwnerAttacked`, `OwnerAttackedBy`, `SelectNextTarget`, `_needToStop`).
*   **PetEventAI**: AI interaction (`UpdateAI`, `AttackStart`).
*   **SpellCaster**: Spell casting (`MeleeDamageBonusDone`, `SpellDamageBonusDone`, `CastSpell#2`, `RemoveAllCooldowns`).
*   **Spell.Main**: Spell checking (`CheckCast`).
*   **game_Group_Group**: Group management (`AddMember`, `RewardGroupAtKill_helper`).
*   **npcs_special**: Special NPC logic (`DespawnShahram`, `JustDied`, `JustDied#2`, `npc_shahramAI`).
*   **wetlands**: Zone-specific logic (`DespawnFriendIfExists`).
*   **Errors**: Error handling (`PrintStacktraceAndThrow`).

## Data Model

The `Pet` class interacts with the following database tables:

*   **`character_pet`**: Stores the primary pet record, including `id`, `entry`, `owner_guid`, `display_id`, `level`, `xp`, `react_state`, `loyalty_points`, `loyalty`, `training_points`, `name`, `renamed`, `slot`, `current_health`, `current_mana`, `current_happiness`, `save_time`, `reset_talents_cost`, `reset_talents_time`, `created_by_spell`, `pet_type`, `action_bar_data`, and `teach_spell_data`.
*   **`pet_spell`**: Stores the spells known by the pet (`guid`, `spell`, `active`).
*   **`pet_aura`**: Stores active auras on the pet (`guid`, `caster_guid`, `item_guid`, `spell`, `stacks`, `charges`, `base_points0-2`, `periodic_time0-2`, `max_duration`, `duration`, `effect_index_mask`).
*   **`pet_spell_cooldown`**: Stores spell cooldowns (`guid`, `spell`, `time`).

## Notable Implementation Details

*   **Loyalty Decay**: In `LooseHappiness`, happiness decay is calculated as `(140 >> GetLoyaltyLevel()) * 125`. This results in decay rates of 70, 35, 17, 8, and 4 per

---

<!-- machine-true, projected from graph.json -->

## Map — Pet.Main

*Source:* Pet.cpp, Pet.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| Execute | method | — | — | — |
| Pet | ctor | Creature.Main/Creature, Unit.Main/InitCharmInfo, Unit.Main/SetReactState | Player.Main/LoadPet, Player.Main/ResummonPetTemporaryUnSummonedIfAny, Spell.Effects/EffectSummon, Spell.Effects/EffectSummonCritter, Spell.Effects/EffectSummonGuardian, Spell.Effects/EffectSummonPet#2, Spell.Effects/EffectTameCreature, WorldSession.NPCHandler/HandleStableSwapPet, WorldSession.NPCHandler/HandleUnstablePet | — |
| ~Pet | dtor | — | — | — |
| AddToWorld | method | CharmInfo/HasCommandState, Object/GetObjectGuid, Object/IsInWorld, Unit.Main/AddToWorld, Unit.Main/GetCharmInfo, Unit.Main/SetIsAtStay, Unit.Main/SetIsCommandAttack, Unit.Main/SetIsCommandFollow, Unit.Main/SetIsFollowing, Unit.Main/SetIsReturning, WorldObject.Object/GetMap | — | — |
| RemoveFromWorld | method | Object/GetObjectGuid, Object/IsInWorld, Unit.Main/RemoveFromWorld, WorldObject.Object/GetMap | — | — |
| LoadPetFromDB | method | CharacterDatabaseCache/CharacterPetSetOthersNotInSlot, CharacterDatabaseCache/GetCharacterCurrentPet, CharacterDatabaseCache/GetCharacterPetByOwner, CharacterDatabaseCache/GetCharacterPetByOwnerAndEntry, CharacterDatabaseCache/GetCharacterPetCacheByOwnerAndId, CharacterDatabaseCache/instance, CharmInfo/GetPetNumber, Creature.Main/AIM_Initialize, Creature.Main/GetCreatureInfo, CreatureCreatePos/CreatureCreatePos#2, CreatureInfo/IsTameable, Database/BeginTransaction, Database/CommitTransaction, Database/CreateStatement, Log.Main/Out, Map.Main/GenerateLocalLowGuid, Object/GetEntry, Object/GetGUIDLow, Object/GetGuidStr, Object/GetObjectGuid, Object/GetTypeId, Object/GetUInt32Value, ObjectMgr/GeneratePetName, ObjectMgr/GetCreatureTemplate, Player.Main/GetGroup, Player.Main/IsPetNeedBeTemporaryUnsummoned, Player.Main/PetSpellInitialize, Player.Main/SetGroupUpdateFlag, Player.Main/SetTemporaryUnsummonedPetNumber, shared_Util/StrSplit, SpellEntry/GetDuration, SpellMgr/GetSpellEntry, SpellMgr/Instance, SqlPreparedStatement/operator=, SqlStatementID/SqlStatementID, Transport/AddPassenger, Transport/UpdatePassengerPosition, Unit.Main/GetCreatePowers, Unit.Main/GetFactionTemplateId, Unit.Main/GetLevel, Unit.Main/GetMaxHealth, Unit.Main/GetMaxPower, Unit.Main/IsMounted, Unit.Main/LoadPetActionBar, Unit.Main/SetCanModifyStats, Unit.Main/SetCreatorGuid, Unit.Main/SetDisplayId, Unit.Main/SetFactionTemplateId, Unit.Main/SetHealth, Unit.Main/SetMaxPower, Unit.Main/SetNativeDisplayId, Unit.Main/SetOwnerGuid, Unit.Main/SetPet, Unit.Main/SetPetNumber, Unit.Main/SetPower, Unit.Main/SetPowerType, Unit.Main/SetReactState, WorldObject.Object/GetMap, WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetTransport, WorldObject.Object/RemoveFlag, WorldObject.Object/SetByteValue, WorldObject.Object/SetFlag, WorldObject.Object/SetUInt32Value | Player.Main/LoadPet, Player.Main/ResummonPetTemporaryUnSummonedIfAny, Spell.Effects/EffectSummon, Spell.Effects/EffectSummonPet#2, WorldSession.NPCHandler/HandleStableSwapPet, WorldSession.NPCHandler/HandleUnstablePet | character_pet |
| GetPetType | method | — | ChatHandler.CharacterCommands/HandlePetInfoCommand, ChatHandler.CharacterCommands/HandlePetLoyaltyCommand, Creature.Main/SetInitCreaturePowerType, PetAI/UpdateAI, Player.Main/PrepareGossipMenu, Player.StatSystem/UpdateArmor#2, Player.StatSystem/UpdateDamagePhysical#2, Spell.Effects/EffectSummonPet#2, SpellCaster/MeleeDamageBonusDone, SpellCaster/SpellDamageBonusDone, Unit.Main/GetCreatePowers, Unit.Main/HandlePetCommand, Unit.Main/SetPower, Unit.Main/UnsummonOldPetBeforeNewSummon, WorldSession.NPCHandler/HandleStablePet, WorldSession.NPCHandler/HandleStableSwapPet, WorldSession.NPCHandler/SendStablePet, WorldSession.PetHandler/HandlePetAbandon, WorldSession.PetHandler/HandlePetCastSpellOpcode, WorldSession.PetHandler/HandlePetRename, WorldSession.PetHandler/HandlePetUnlearnOpcode | — |
| SetPetType | method | — | Spell.Effects/EffectSummonPet#2 | — |
| IsControlled | method | — | CreatureAISelector/selectAI, Player.Main/UnsummonPetTemporaryIfAny, Unit.Main/ApplyMaxPowerMod, Unit.Main/ApplyPowerMod, Unit.Main/SetDisplayId, Unit.Main/SetHealth, Unit.Main/SetMaxHealth, Unit.Main/SetMaxPower, Unit.Main/SetPower, Unit.Main/SetPowerType, Unit.Main/UpdateAuraForGroup, WorldObject.Object/IsLikePlayer | — |
| IsTemporarySummoned | method | — | Player.Main/UnsummonPetTemporaryIfAny | — |
| GetName | method | — | ChatHandler.CharacterCommands/HandlePetLearnSpellCommand, ChatHandler.CharacterCommands/HandlePetUnlearnSpellCommand, WorldSession.GroupHandler/BuildPartyMemberStatsPacket, WorldSession.NPCHandler/SendStablePet | — |
| SetName | method | — | Spell.Effects/EffectSummonPet#2, WorldSession.PetHandler/HandlePetRename | — |
| GetPetAutoSpellSize | method | — | — | — |
| GetPetAutoSpellOnPos | method | — | — | — |
| GetLoyaltyLevel | method | — | Spell.Effects/EffectTameCreature, WorldSession.NPCHandler/SendStablePet, WorldSession.PetHandler/HandlePetUnlearnOpcode | — |
| SetDuration | method | — | Spell.Effects/EffectSummon, Spell.Effects/EffectSummonCritter, Spell.Effects/EffectSummonGuardian | — |
| GetFollowAngle | method | — | PetAI/HandleReturnMovement, PetEventAI/UpdateAI | — |
| SetFollowAngle | method | — | Spell.Effects/EffectSummonGuardian | — |
| GetBonusDamage | method | — | SpellCaster/MeleeDamageBonusDone, SpellCaster/SpellDamageBonusDone | — |
| SetBonusDamage | method | — | — | — |
| IsEnabled | method | — | Creature.Main/GetAttackDistance, PetAI/AttackedBy, PetAI/CanAttack, PetAI/OwnerAttacked, PetAI/OwnerAttackedBy, PetAI/SelectNextTarget, PetAI/_needToStop, PetEventAI/AttackStart, Player.Main/PetSpellInitialize, WorldSession.PetHandler/HandlePetAction, WorldSession.PetHandler/HandlePetSetAction | — |
| AddTeachSpell | method | — | — | — |
| GetAuraUpdateMask | method | — | WorldSession.GroupHandler/BuildPartyMemberStatsPacket | — |
| SetAuraUpdateSlot | method | — | Unit.Main/UpdateAuraForGroup | — |
| SetAuraUpdateMask | method | — | game_Group_Group/AddMember | — |
| ResetAuraUpdateMask | method | — | Player.Main/SendUpdateToOutOfRangeGroupMembers | — |
| SaveToDB | method | — | — | — |
| DeleteFromDB | method | — | — | — |
| SavePetToDB | method | CharacterDatabaseCache/GetCharacterPetCacheByOwnerAndId, CharacterDatabaseCache/InsertCharacterPet, CharacterDatabaseCache/instance, CharmInfo/GetActionBarEntry, CharmInfo/GetPetNumber, Database/BeginTransaction, Database/CommitTransaction, Database/CreateStatement, Object/GetEntry, Object/GetUInt32Value, Object/HasFlag, ObjectGuid/GetCounter, Player.Main/GetTemporaryUnsummonedPetNumber, Player.Main/IsSavingDisabled, SqlPreparedStatement/Execute#2, SqlPreparedStatement/operator=, SqlStatement/addInt32, SqlStatement/addString, SqlStatement/addString#2, SqlStatement/addUInt32, SqlStatement/addUInt64, SqlStatementID/SqlStatementID, Unit.Main/GetHealth, Unit.Main/GetLevel, Unit.Main/GetNativeDisplayId, Unit.Main/GetOwnerGuid, Unit.Main/GetOwnerPlayer, Unit.Main/GetPower, Unit.Main/GetReactState, Unit.Main/IsAlive, Unit.Main/RemoveAllAuras, UnitActionBarEntry/GetAction, UnitActionBarEntry/GetType | Player.Main/SaveToDB, Spell.Effects/EffectLearnPetSpell, Spell.Effects/EffectResurrectNew, Spell.Effects/EffectSummon, Spell.Effects/EffectSummonDeadPet, Spell.Effects/EffectSummonPet#2, Spell.Effects/EffectTameCreature | character_pet |
| DeleteFromDB#2 | method | CharacterDatabaseCache/DeleteCharacterPetById, CharacterDatabaseCache/instance, Database/BeginTransaction, Database/CommitTransaction, Database/CreateStatement, SqlPreparedStatement/operator=, SqlStatementID/SqlStatementID | Player.Main/DeleteFromDB | character_pet, pet_aura, pet_spell, pet_spell_cooldown |
| SetDeathState | method | Creature.Main/SetDeathState, Map.Main/GetMapEntry, Unit.Main/GetDeathState, Unit.Main/ModifyPower, WorldObject.Object/GetMap, WorldObject.Object/RemoveFlag, WorldObject.Object/SetFlag, WorldObject.Object/SetUInt32Value | Player.Main/AutoReSummonPet, Spell.Effects/EffectResurrectNew, Spell.Effects/EffectSummonDeadPet | — |
| Update | method | Creature.Main/Update, Object/GetObjectGuid, ObjectGuid/operator!, ObjectGuid/operator!=, ObjectGuid/operator==, Unit.Main/GetAttackerForHelper, Unit.Main/GetCharmGuid, Unit.Main/GetOwner, Unit.Main/GetPetGuid, Unit.Main/IsDead, WorldObject.Object/GetTransport, WorldObject.Object/IsWithinDistInMap | — | — |
| RegenerateAll | method | Creature.Main/RegenerateHealth, Creature.Main/RegenerateMana, Unit.Main/IsInCombat, Unit.Main/IsPolymorphed | — | — |
| RegenerateFocus | method | Aura/GetModifier, Unit.Main/GetAurasByType, Unit.Main/GetMaxPower, Unit.Main/GetPower, Unit.Main/ModifyPower, World/getConfig#2 | — | — |
| LooseHappiness | method | Unit.Main/GetPower, Unit.Main/IsInCombat, Unit.Main/ModifyPower | — | — |
| ModifyLoyalty | method | Player.Main/GetSession, Unit.Main/GetLevel, Unit.Main/GetOwnerPlayer, World/getConfig#2, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | ChatHandler.CharacterCommands/HandlePetLoyaltyCommand, Spell.Effects/EffectTameCreature | — |
| TickLoyaltyChange | method | — | — | — |
| KillLoyaltyBonus | method | — | — | — |
| GetHappinessState | method | Unit.Main/GetPower | Player.StatSystem/UpdateDamagePhysical#2, SpellCaster/MeleeDamageBonusDone, SpellCaster/SpellDamageBonusDone | — |
| SetLoyaltyLevel | method | WorldObject.Object/SetByteValue | — | — |
| CanTakeMoreActiveSpells | method | SpellEntry/IsPassiveSpell, SpellMgr/GetFirstSpellInChain, SpellMgr/Instance | Spell.Main/CheckCast | — |
| HasTPForSpell | method | — | Spell.Main/CheckCast | — |
| GetTPForSpell | method | SpellMgr/GetFirstSpellInChain, SpellMgr/GetSkillLineAbilityMapBoundsBySpellId, SpellMgr/Instance | Spell.Effects/EffectLearnPetSpell | — |
| GetMaxLoyaltyPoints | method | — | — | — |
| GetStartLoyaltyPoints | method | — | Spell.Effects/EffectTameCreature | — |
| SetTP | method | WorldObject.Object/SetUInt32Value | Spell.Effects/EffectLearnPetSpell, WorldSession.PetHandler/HandlePetUnlearnOpcode | — |
| GetDispTP | method | — | — | — |
| GetSkillIdForPetTraining | method | Creature.Main/GetCreatureInfo | — | — |
| CanLearnPetSpell | method | SpellMgr/GetSkillLineAbilityMapBoundsBySpellId, SpellMgr/Instance | Spell.Effects/EffectLearnPetSpell | — |
| Unsummon | method | CharmInfo/GetPetNumber, Creature.Main/AI, CreatureAI/SummonedCreatureDespawn, Object/GetObjectGuid, Object/GetUInt32Value, Object/IsInWorld, Object/ToCreature, Object/ToPlayer, ObjectGuid/operator!=, ObjectGuid/operator==, Player.Main/CanStoreNewItem, Player.Main/GetGroup, Player.Main/GetTemporaryUnsummonedPetNumber, Player.Main/RemovePetActionBar, Player.Main/SendNewItem, Player.Main/SetGroupUpdateFlag, Player.Main/StoreNewItem, Player.Main/_SetMiniPet, SpellMgr/GetSpellEntry, SpellMgr/Instance, Unit.Main/CombatStop, Unit.Main/GetCharmerGuid, Unit.Main/GetCharmInfo, Unit.Main/GetOwner, Unit.Main/GetOwnerGuid, Unit.Main/GetPetGuid, Unit.Main/GetPossessorGuid, Unit.Main/GetUnit, Unit.Main/RemoveAurasDueToSpell, Unit.Main/RemoveCharmAuras, Unit.Main/RemoveGuardian, Unit.Main/SetPet, Unit.SpellAuras/ModPossessPet, WorldObject.Object/AddObjectToRemoveList | ChatHandler.CreatureCommands/HandleNpcDeleteCommand, Creature.Main/DisappearAndDie, npcs_special/DespawnShahram, Player.Main/RemoveMiniPet, Player.Main/RemovePet, Player.Main/UnsummonPetTemporaryIfAny, Spell.Effects/EffectDismissPet, Spell.Effects/EffectSummonGuardian, Spell.Main/CheckCast, Unit.Main/HandlePetCommand, Unit.Main/RemoveFromWorld, Unit.Main/RemoveGuardians, Unit.Main/RemoveGuardiansWithEntry, Unit.Main/UnsummonOldPetBeforeNewSummon, wetlands/DespawnFriendIfExists, WorldSession.NPCHandler/HandleStablePet, WorldSession.NPCHandler/HandleStableSwapPet, WorldSession.PetHandler/HandlePetAbandon | — |
| DelayedUnsummon | method | EventProcessor/AddEvent, EventProcessor/CalculateTime, UnsummonPetDelayEvent/UnsummonPetDelayEvent | Creature.Main/DespawnOrUnsummon, npcs_special/JustDied, npcs_special/JustDied#2 | — |
| GivePetXP | method | Object/GetUInt32Value, Object/IsPlayer, Player.Main/GetPersonalXpRate, Unit.Main/GetLevel, Unit.Main/GetOwner, Unit.Main/IsAlive, World/getConfig#4, WorldObject.Object/SetUInt32Value | game_Group_Group/RewardGroupAtKill_helper, Player.Main/RewardSinglePlayerAtKill | — |
| GivePetLevel | method | ObjectMgr/GetXPForPetLevel, Unit.Main/GetLevel, WorldObject.Object/SetUInt32Value | ChatHandler.CharacterCommands/HandleLevelUpCommand, ChatHandler.CreatureCommands/HandleNpcSetLevelCommand | — |
| CreateBaseAtCreature | method | Creature.Main/GetCreatureInfo, CreatureCreatePos/CreatureCreatePos#2, Log.Main/Out, Map.Main/GenerateLocalLowGuid, Object/GetFloatValue, ObjectMgr/GeneratePetNumber, ObjectMgr/GetXPForPetLevel, Unit.Main/GetCreatePowers, Unit.Main/GetDisplayId, Unit.Main/GetLevel, Unit.Main/GetNativeDisplayId, Unit.Main/SetDisplayId, Unit.Main/SetMaxPower, Unit.Main/SetNativeDisplayId, Unit.Main/SetPower, Unit.Main/SetPowerType, Unit.Main/SetSheath, WorldObject.Object/GetMap, WorldObject.Object/GetOrientation, WorldObject.Object/SetByteValue, WorldObject.Object/SetFloatValue, WorldObject.Object/SetUInt32Value | Spell.Effects/EffectTameCreature | — |
| InitStatsForLevel | method | Creature.Main/GetClassLevelStats, Creature.Main/GetCreatureInfo, Creature.Main/SetInitCreaturePowerType, Creature.Main/SetMeleeDamageSchool, Creature.Main/ToggleUnitFlagsFromStaticFlags, Creature.Main/_GetDamageMod, Creature.Main/_GetHealthMod, Errors/PrintStacktraceAndThrow, Log.Main/Out, Object/HasFlag, Object/IsPlayer, ObjectMgr/GetPetLevelInfo, ObjectMgr/GetXPForPetLevel, Player.StatSystem/UpdateAllStats#2, Unit.Main/GetAttackTime, Unit.Main/GetLevel, Unit.Main/GetMaxHealth, Unit.Main/GetMaxPower, Unit.Main/GetOwner, Unit.Main/IsPvP, Unit.Main/SetAttackTime, Unit.Main/SetBaseWeaponDamage, Unit.Main/SetCreateHealth, Unit.Main/SetCreateMana, Unit.Main/SetCreateResistance, Unit.Main/SetCreateStat, Unit.Main/SetHealth, Unit.Main/SetLevel, Unit.Main/SetPower, Unit.Main/SetPvP, Unit.Main/SetSheath, Unit.Main/UpdateModelData, World/getConfig, World/GetWowPatch, WorldObject.Object/RemoveFlag, WorldObject.Object/SetByteValue, WorldObject.Object/SetFlag, WorldObject.Object/SetFloatValue, WorldObject.Object/SetObjectScale, WorldObject.Object/SetUInt32Value | npcs_special/npc_shahramAI, Spell.Effects/EffectSummon, Spell.Effects/EffectSummonGuardian, Spell.Effects/EffectSummonPet#2, Spell.Effects/EffectTameCreature | — |
| HaveInDiet | method | Creature.Main/GetCreatureInfo | Spell.Main/CheckCast | — |
| GetCurrentFoodBenefitLevel | method | Unit.Main/GetLevel | Spell.Effects/EffectFeedPet, Spell.Main/CheckCast | — |
| _LoadSpellCooldowns | method | ByteBuffer/append#3, ByteBuffer/ByteBuffer, ByteBuffer/operator<<#10, ByteBuffer/size, CooldownContainer/AddCooldown, Log.Main/Out, Object/GetGuidStr, Object/GetObjectGuid, ObjectGuid/operator<<, Player.Main/GetSession, SpellMgr/GetSpellEntry, SpellMgr/Instance, Unit.Main/GetOwnerPlayer, World/GetCurrentClockTime, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | — | — |
| _SaveSpellCooldowns | method | CharmInfo/GetPetNumber, CooldownData/GetSpellCDExpireTime, CooldownData/IsPermanent, Database/CreateStatement, SqlPreparedStatement/operator=, SqlStatementID/SqlStatementID, World/GetCurrentClockTime | — | pet_spell_cooldown |
| _LoadSpells | method | — | — | — |
| _SaveSpells | method | CharmInfo/GetPetNumber, Database/CreateStatement, SqlPreparedStatement/operator=, SqlStatementID/SqlStatementID | — | pet_spell |
| _LoadAuras | method | Aura/GetModifier, Aura/SetLoadedState, Log.Main/Out, Object/GetObjectGuid, ObjectGuid/ObjectGuid#2, ObjectGuid/operator!=, SpellAuraHolder/SetLoadedState, SpellEntry/HasSingleTargetAura, SpellEntry/IsPositiveSpell#4, SpellMgr/GetSpellEntry, SpellMgr/Instance, Unit.Main/AddSpellAuraHolder, Unit.Main/RemoveAllAuras, Unit.SpellAuras/AddAura, Unit.SpellAuras/CreateAura, Unit.SpellAuras/CreateSpellAuraHolder, Unit.SpellAuras/IsEmptyHolder, WorldObject.Object/SetUInt32Value | — | — |
| _SaveAuras | method | Aura/GetModifier, Aura/IsAreaAura, CharmInfo/GetPetNumber, Database/CreateStatement, Object/GetObjectGuid, ObjectGuid/GetCounter, ObjectGuid/GetRawValue, ObjectGuid/operator!=, ObjectGuid/operator==, SpellAuraHolder/GetAuraByEffectIndex, SpellAuraHolder/GetAuraCharges, SpellAuraHolder/GetAuraDuration, SpellAuraHolder/GetAuraMaxDuration, SpellAuraHolder/GetCasterGuid, SpellAuraHolder/GetCastItemGuid, SpellAuraHolder/GetSpellProto, SpellAuraHolder/GetStackAmount, SpellAuraHolder/IsPassive, SpellAuraHolder/IsSingleTarget, SpellEntry/IsChanneledSpell, SqlPreparedStatement/Execute#2, SqlPreparedStatement/operator=, SqlStatement/addFloat, SqlStatement/addInt32, SqlStatement/addUInt32, SqlStatement/addUInt64, SqlStatement/addUInt8, SqlStatementID/SqlStatementID, Unit.Main/GetSpellAuraHolderMap, Unit.SpellAuras/GetId | — | pet_aura |
| AddSpell | method | Database/PExecute#2, Log.Main/Out, SpellCaster/CastSpell#2, SpellEntry/IsPassiveSpell#2, SpellMgr/GetSpellEntry, SpellMgr/GetSpellRank, SpellMgr/Instance, SpellMgr/IsHighRankOfSpell, SpellMgr/IsRankSpellDueToSpell, Unit.Main/AddSpellToActionBar | — | pet_spell |
| LearnSpell | method | Player.Main/PetSpellInitialize, Unit.Main/GetOwnerPlayer | ChatHandler.CharacterCommands/HandlePetLearnSpellCommand, Spell.Effects/EffectLearnPetSpell | — |
| UnlearnSpell | method | — | ChatHandler.CharacterCommands/HandlePetUnlearnSpellCommand, WorldSession.PetHandler/HandlePetUnlearnOpcode | — |
| RemoveSpell | method | Player.Main/PetSpellInitialize, SpellMgr/GetPrevSpellInChain, SpellMgr/Instance, Unit.Main/GetOwnerPlayer, Unit.Main/RemoveAurasDueToSpell, Unit.Main/RemoveSpellFromActionBar | — | — |
| CleanupActionBar | method | CharmInfo/GetActionBarEntry, CharmInfo/SetActionBar, UnitActionBarEntry/GetAction, UnitActionBarEntry/IsActionBarForSpell | — | — |
| InitPetCreateSpells | method | Object/GetEntry, ObjectMgr/GetPetCreateSpellEntry, Player.Main/HasSpell, Player.Main/LearnSpell, SpellEntry/IsPassiveSpell, SpellMgr/GetSkillLineAbilityMapBoundsBySpellId, SpellMgr/GetSpellEntry, SpellMgr/Instance, Unit.Main/GetOwnerPlayer, Unit.Main/InitPetActionBar | Spell.Effects/EffectSummon, Spell.Effects/EffectSummonCritter, Spell.Effects/EffectSummonPet#2, Spell.Effects/EffectTameCreature | — |
| CheckLearning | method | Object/GetTypeId, Player.Main/LearnSpell, shared_Util/urand, Unit.Main/GetOwnerPlayer | PetAI/UpdateAI, WorldSession.PetHandler/HandlePetAction, WorldSession.PetHandler/HandlePetCastSpellOpcode | — |
| GetResetTalentsCost | method | World/GetGameTime | Player.Main/SendPetSkillWipeConfirm, WorldSession.PetHandler/HandlePetUnlearnOpcode | — |
| ToggleAutocast | method | SpellEntry/IsPassiveSpell | WorldSession.PetHandler/HandlePetSetAction, WorldSession.PetHandler/HandlePetSpellAutocastOpcode | — |
| IsPermanentPetFor | method | Creature.Main/GetCreatureInfo, Unit.Main/GetClass | Player.Main/Mount, Player.Main/PetSpellInitialize | — |
| InitializeDefaultName | method | Creature.Main/GetCreatureInfo, Creature.Main/GetNameForLocaleIdx, ObjectGuid/IsPlayer, ObjectMgr/GetDBCLocaleIndex, Unit.Main/GetOwnerGuid, World/GetDefaultDbcLocale | Spell.Effects/EffectSummon, Spell.Effects/EffectSummonCritter, Spell.Effects/EffectSummonGuardian, Spell.Effects/EffectSummonPet#2, Spell.Effects/EffectTameCreature | — |
| GetNameForLocaleIdx | method | Creature.Main/GetNameForLocaleIdx, ObjectGuid/IsPlayer, Unit.Main/GetOwnerGuid | — | — |
| Create | method | Creature.Main/InitEntry, Creature.Main/LoadDefaultAuras, Creature.Main/Relocate, Creature.Main/SelectFinalPoint, Creature.Main/SetDefaultGossipMenuId, CreatureCreatePos/GetMap, Unit.Main/SetSheath, WorldObject.Object/SetByteValue, WorldObject.Object/SetFlag, WorldObject.Object/SetMap, WorldObject.Object/_Create | Spell.Effects/EffectSummon, Spell.Effects/EffectSummonCritter, Spell.Effects/EffectSummonGuardian, Spell.Effects/EffectSummonPet#2 | — |
| HasSpell | method | — | — | — |
| LearnPetPassives | method | Creature.Main/GetCreatureInfo | WorldSession.PetHandler/HandlePetUnlearnOpcode | — |
| CastPetAuras | method | PetAura/IsRemovedOnChangePet, Unit.Main/GetOwnerPlayer, Unit.Main/RemovePetAura | — | — |
| CastPetAura | method | Object/GetEntry, PetAura/GetAura, SpellCaster/CastSpell#2 | Unit.Main/AddPetAura | — |
| RemoveAllCooldowns | method | Player.Main/SendClearAllCooldowns, SpellCaster/RemoveAllCooldowns, Unit.Main/GetCharmerOrOwnerPlayerOrPlayerItself | — | — |
| SynchronizeLevelWithOwner | method | Unit.Main/GetLevel, Unit.Main/GetOwnerPlayer | ChatHandler.CharacterCommands/HandleResetLevelCommand, Player.Main/GiveLevel, Player.Main/InitStatsForLevel | — |
| SetEnabled | method | ByteBuffer/operator<<#7, CharmInfo/GetCommandState, CharmInfo/GetReactState, Object/GetObjectGuid, ObjectGuid/operator<<, Player.Main/GetSession, Unit.Main/GetCharmInfo, Unit.Main/GetOwnerPlayer, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | Player.Main/Mount, Player.Main/Unmount | — |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `character_pet`: id int(11) unsigned PK, entry int(11) unsigned, owner_guid int(11) unsigned, display_id int(11) unsigned?, created_by_spell int(11) unsigned, pet_type tinyint(3) unsigned, level int(11) unsigned, xp int(11) unsigned, react_state tinyint(1) unsigned, loyalty_points int(11), loyalty int(11) unsigned, training_points int(11), name varchar(100)?, renamed tinyint(1) unsigned, slot int(11) unsigned, current_health int(11) unsigned, current_mana int(11) unsigned, current_happiness int(11) unsigned, save_time bigint(20) unsigned, reset_talents_cost int(11) unsigned, reset_talents_time bigint(20) unsigned, action_bar_data longtext?, teach_spell_data longtext?
- `pet_aura`: guid int(11) unsigned PK, caster_guid bigint(20) unsigned PK, item_guid int(11) unsigned PK, spell int(11) unsigned PK, stacks int(11) unsigned, charges int(11) unsigned, base_points0 float, base_points1 float, base_points2 float, periodic_time0 int(11) unsigned, periodic_time1 int(11) unsigned, periodic_time2 int(11) unsigned, max_duration int(11), duration int(11), effect_index_mask tinyint(3) unsigned
- `pet_spell`: guid int(11) unsigned PK, spell int(11) unsigned PK, active int(11) unsigned
- `pet_spell_cooldown`: guid int(11) unsigned PK, spell int(11) unsigned PK, time bigint(20) unsigned

*`?` = nullable, `PK` = primary key column.*

