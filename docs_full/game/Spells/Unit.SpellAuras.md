<!-- provenance: failed-members, boundary-bleed -->
# Unit.SpellAuras

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Unit.SpellAuras

## Purpose & Responsibilities

This unit implements the core logic for **Auras** in the `wowvmangos` server engine. An Aura represents the active effect of a spell on a `Unit` (player or creature). This unit defines the lifecycle, state management, and behavioral handlers for these effects.

Key responsibilities include:
1.  **Aura Representation:** Defining the `Aura` class (individual spell effects), `SpellAuraHolder` (container for all effects of a single spell cast), and specialized subclasses (`AreaAura`, `PersistentAreaAura`, `SingleEnemyTargetAura`).
2.  **Effect Handling:** Implementing specific behaviors for hundreds of `AuraType`s (e.g., damage over time, stat increases, movement restrictions, immunity) via a large dispatch table (`AuraHandler`) and corresponding `Handle...` methods.
3.  **Periodic Processing:** Managing timers for periodic effects (ticks) such as healing, damage, or spell triggers.
4.  **Aura Interaction:** Handling stacking, refreshing, exclusive auras (where only the strongest applies), and debuff/buff slot limits.
5.  **Area of Effect (AoE):** Managing auras that affect multiple targets within a radius, including dynamic application/removal based on proximity and group membership.

This unit does not directly interact with database tables; it operates entirely on in-memory game state.

## Member-by-Member Behavior

### Aura Construction and Lifecycle

*   **`Aura` (ctor)**: Initializes an individual spell effect. It calculates the base value (damage/healing/modifier amount) using `SpellCaster::CalculateSpellEffectValue` and `SpellEntry::CalculateSimpleValue`. It sets up periodic timers if applicable, computes exclusivity rules, and determines if the aura consumes a visible buff/debuff slot.
*   **`~Aura`**: Destructor that cleans up the associated `SpellModifier` object.
*   **`AreaAura` (ctor)**: Inherits from `Aura`. Sets up radius and target type (Party, Raid, Friend, Enemy, Pet, Owner, Creature Group). It adjusts the aura type if the caster is a totem or creature.
*   **`~AreaAura`**: Destructor for `AreaAura`.
*   **`PersistentAreaAura` (ctor)**: Inherits from `Aura`. Used for auras tied to a `DynamicObject` (like a trap or spell projectile). Marks the aura as persistent.
*   **`~PersistentAreaAura`**: Destructor for `PersistentAreaAura`.
*   **`SingleEnemyTargetAura` (ctor)**: Inherits from `Aura`. Stores the caster's current target GUID to resolve the trigger target later.
*   **`~SingleEnemyTargetAura`**: Destructor for `SingleEnemyTargetAura`.

### Aura State and Modification

*   **`Refresh`**: Updates an existing aura's duration and recalculates its modifier values. It temporarily locks stat modification for certain stat-increasing auras to prevent health/mana jumps, then reapplies the modifier.
*   **`Refresh#2`** (`SpellAuraHolder::Refresh`): Refreshes the entire holder (all effects) by updating duration, apply time, and calling `Refresh` on each contained `Aura`.
*   **`CanBeRefreshedBy`**: Determines if one `SpellAuraHolder` can refresh another. Requires same caster, same spell ID, and no stacks or charges.
*   **`IsMoreImportantVisualAuraThan`**: Compares two holders to decide which one persists when debuff/buff slot limits are reached. Higher score wins; ties go to the most recently applied.
*   **`SetModifier`**: Sets the type, amount, period, and misc value of the aura's modifier.
*   **`GetMiscValue`**: Retrieves the miscellaneous value from the spell prototype.
*   **`UpdatePeriodicTimer`**: Adjusts the periodic timer if the aura's duration changes, ensuring ticks aren't lost.
*   **`UpdateForAffected#2`** (`Aura::UpdateForAffected`): Wraps `Update` with `SetInUse` flags.
*   **`Update#2`** (`Aura::Update`): Handles periodic ticks. If the timer expires, it calls `PeriodicTick`. It also checks for group-based removal (e.g., Greater Blessings removed when leaving raid).
*   **`UpdateForAffected`** (`AreaAura::UpdateForAffected`): Wrapper for `AreaAura` updates.
*   **`Update`** (`AreaAura::Update`): Complex logic for AoE auras.
    *   **Caster Side:** Identifies valid targets within radius based on `AreaAuraType` (Party, Raid, Friend, Enemy, etc.). It applies the aura to new targets or updates existing ones. It handles spell rank scaling for lower-level targets.
    *   **Target Side:** Checks if the target is still in range, friendly/hostile as required, and in the correct group/subgroup. Removes the aura if conditions fail.
*   **`Update#3`** (`PersistentAreaAura::Update`): Checks if the target is still within the `DynamicObject`'s radius. Removes the aura if out of range or if the dynamic object is gone.
*   **`ApplyModifier`**: Applies or removes the aura's effect. It checks exclusivity rules (`ExclusiveAuraCanApply`/`ExclusiveAuraUnapply`) and calls the specific handler from the `AuraHandler` table.
*   **`IsAffectedOnSpell`**: Checks if this aura is affected by another spell (used for spell mods).
*   **`CanProcFrom`**: Determines if the aura can proc from a specific spell event, checking family masks and proc flags.
*   **`ReapplyAffectedPassiveAuras#2`**: Re-applies passive auras on a target that are affected by this aura's spell mods (e.g., when a talent buff is removed).
*   **`ReapplyAffectedPassiveAurasHelper` (ctor)**: Helper functor for iterating controlled units.
*   **`operator()`**: Functor operator for `ReapplyAffectedPassiveAurasHelper`.
*   **`ReapplyAffectedPassiveAuras`**: Triggers re-application of affected passive auras on the target and its controlled pets/totems.
*   **`HandleAddModifier`**: Adds or removes a `SpellModifier` from the target player. Handles special cases for charged auras.
*   **`TriggerSpell`**: Handles `SPELL_AURA_PERIODIC_TRIGGER_SPELL`. Contains extensive hardcoded logic for specific spells (e.g., Firestone, Thaumaturgy, Brood Affliction) to cast triggered spells or apply effects.
*   **`HandleAuraDummy`**: Handles `SPELL_AURA_DUMMY`. Contains massive switch-case blocks for specific spell IDs to perform unique actions (e.g., dancing, camera views, phase changes, special summons).
*   **`HandleAuraMounted`**: Applies or removes a mount model.
*   **`HandleAuraWaterWalk`**, **`HandleAuraFeatherFall`**, **`HandleAuraHover`**: Toggle movement flags on the target.
*   **`HandleWaterBreathing`**, **`HandleModWaterBreathing`**: Adjusts water breathing interval multipliers for players.
*   **`GetShapeshiftDisplayInfo`**: Static function returning display ID and scale for a shapeshift form.
*   **`HandleAuraModShapeshift`**: Changes the target's form, display ID, power type, and applies/removes associated boost auras.
*   **`HandleAuraTransform`**: Transforms the target into a creature model, handling display IDs, scales, and equipment.
*   **`HandleForceReaction`**: Forces a reputation reaction with a faction.
*   **`HandleAuraModSkill`**: Modifies a player's skill bonus.
*   **`HandleChannelDeathItem`**: Awards items to the caster when the target dies (e.g., Soul Shards).
*   **`HandleBindSight`**, **`HandleFarSight`**: Manipulates camera views and sight ranges.
*   **`HandleAuraTrackCreatures`**, **`HandleAuraTrackResources`**, **`HandleAuraTrackStealthed`**: Toggles tracking flags on the player.
*   **`HandleAuraModScale`**: Changes the target's model scale.
*   **`HandleModPossess`**: Initiates possession of a unit. Calls `Unit::ModPossess` (defined in `Unit.Main`).
*   **`ModPossess`** (`Unit::ModPossess`): Implemented in `Unit.Main`. Implements the mechanics of possession: changing control, camera, faction, threat lists, and AI.
*   **`HandleModPossessPet`**: Initiates possession of a pet. Calls `Player::ModPossessPet` (defined in `Player.Main`).
*   **`ModPossessPet`** (`Player::ModPossessPet`): Implemented in `Player.Main`. Implements pet possession mechanics.
*   **`HandleModCharm`**: Charms a unit, changing its faction, threat, and AI to follow/attack for the caster.
*   **`HandleModConfuse`**, **`HandleModFear`**: Sets confused or feared states.
*   **`HandleFeignDeath`**: Attempts to feign death, checking for resistance from nearby enemies.
*   **`HandleAuraModDisarm`**: Disarms the target, resetting attack times and removing weapon mods.
*   **`HandleAuraModStun`**: Stuns the target, interrupting spells and stopping movement.
*   **`HandleModStealth`**, **`HandleInvisibility`**, **`HandleInvisibilityDetect`**: Manages stealth and invisibility states, visibility masks, and detection.
*   **`HandleDetectAmore`**: Toggles the "Detect Amore" flag for Love is in the Air event.
*   **`HandleAuraModRoot`**: Roots the target, stopping movement.
*   **`HandleAuraModSilence`**: Silences the target, interrupting spells.
*   **`HandleModThreat`**, **`HandleAuraModTotalThreat`**: Modifies threat generation.
*   **`HandleModTaunt`**: Applies taunt mechanics.
*   **`HandleAuraModIncreaseSpeed`**, **`HandleAuraModIncreaseMountedSpeed`**, **`HandleAuraModIncreaseSwimSpeed`**, **`HandleAuraModDecreaseSpeed`**, **`HandleAuraModUseNormalSpeed`**: Update movement speeds.
*   **`HandleModMechanicImmunity`**, **`HandleModMechanicImmunityMask`**, **`HandleAuraModEffectImmunity`**, **`HandleAuraModStateImmunity`**, **`HandleAuraModSchoolImmunity`**, **`HandleAuraModDmgImmunity`**, **`HandleAuraModDispelImmunity`**: Apply various forms of immunity.
*   **`HandleAuraProcTriggerSpell`**: Handles proc-triggered spells, setting charges if needed.
*   **`HandleAuraModStalked`**: Marks the target as stalked (Hunter's Mark).
*   **`HandlePeriodicTriggerSpell`**, **`HandlePeriodicTriggerSpellWithValue`**, **`HandlePeriodicEnergize`**, **`HandleAuraPowerBurn`**: Sets up periodic flags for these aura types.
*   **`HandlePeriodicHeal`**: Sets up periodic healing, calculating bonus healing at cast time (post-1.11).
*   **`CalculateDotDamage`**: Calculates damage for DoTs, including combo point contributions for Rogues/Druids.
*   **`HandlePeriodicDamage`**: Sets up periodic damage, calculating damage at cast time (post-1.10).
*   **`HandlePeriodicDamagePCT`**, **`HandlePeriodicLeech`**, **`HandlePeriodicManaLeech`**, **`HandlePeriodicHealthFunnel`**: Set up periodic resource manipulation.
*   **`HandleAuraModResistanceExclusive`**, **`HandleAuraModResistance`**, **`HandleModResistancePercent`**, **`HandleModBaseResistance`**, **`HandleAuraModBaseResistancePercent`**: Modify resistances.
*   **`HandleAurasVisible`**: Makes auras visible to others.
*   **`HandleAuraModStat`**, **`HandleModPercentStat`**, **`HandleModSpellDamagePercentFromStat`**, **`HandleModSpellHealingPercentFromStat`**, **`HandleModHealingDone`**, **`HandleModTotalPercentStat`**, **`HandleAuraModResistenceOfStatPercent`**: Modify stats and derived values.
*   **`HandleAuraModTotalHealthPercentRegen`**, **`HandleAuraModTotalManaPercentRegen`**, **`HandleModRegen`**, **`HandleModPowerRegen`**, **`HandleModPowerRegenPCT`**: Handle regeneration.
*   **`HandleAuraModIncreaseHealth`**, **`HandleAuraModIncreaseEnergy`**, **`HandleAuraModIncreaseEnergyPercent`**, **`HandleAuraModIncreaseHealthPercent`**: Modify health and power resources.
*   **`HandleAuraModParryPercent`**, **`HandleAuraModDodgePercent`**, **`HandleAuraModBlockPercent`**, **`HandleAuraModRegenInterrupt`**, **`HandleAuraModCritPercent`**: Modify combat chances and interrupts.
*   **`HandleModSpellHitChance`**, **`HandleModSpellCritChance`**, **`HandleModSpellCritChanceSchool`**: Modify spell hit/crit.
*   **`HandleModCastingSpeed`**, **`HandleModAttackSpeed`**, **`HandleModMeleeSpeedPct`**, **`HandleAuraModRangedHaste`**, **`HandleRangedAmmoHaste`**: Modify attack/cast speeds.
*   **`HandleAuraModAttackPower`**, **`HandleAuraModRangedAttackPower`**, **`HandleAuraModAttackPowerPercent`**, **`HandleAuraModRangedAttackPowerPercent`**: Modify attack power.
*   **`HandleModDamageDone`**, **`HandleModDamagePercentDone`**, **`HandleModOffhandDamagePercent`**: Modify damage output.
*   **`HandleModPowerCostPCT`**, **`HandleModPowerCost`**: Modify spell costs.
*   **`HandleReflectSpellsSchool`**: Reflects spells.
*   **`HandleShapeshiftBoosts`**: Applies/removes secondary auras associated with shapeshift forms.
*   **`HandleAuraEmpathy`**: Shows empathy icon.
*   **`HandleAuraUntrackable`**: Makes unit untrackable.
*   **`HandleAuraModPacify`**, **`HandleAuraModPacifyAndSilence`**: Pacifies the unit.
*   **`HandleAuraGhost`**: Sets ghost state.
*   **`HandleShieldBlockValue`**: Modifies shield block value.
*   **`HandleAuraRetainComboPoints`**: Retains combo points on aura expiry.
*   **`HandleModUnattackable`**: Makes unit unattackable.
*   **`HandleSpiritOfRedemption`**: Handles Spirit of Redemption state.
*   **`HandleAuraAoeCharm`**: Handles AoE charm (Kel'Thuzad chains).
*   **`HandleSchoolAbsorb`**: Sets up school absorb shields.
*   **`PeriodicTick`**: The core periodic processing loop. Handles damage, healing, leech, energize, mana burn, regen, and dummy ticks based on aura type.
*   **`PeriodicDummyTick`**: Handles periodic dummy effects (e.g., Forsaken skills, Party Time emotes).
*   **`HandlePreventFleeing`**: Prevents fleeing or cancels fear.
*   **`HandleManaShield`**: Sets up mana shield absorption.
*   **`IsLastAuraOnHolder`**: Checks if this is the last effect on the holder.
*   **`ComputeExclusive`**: Determines if the aura is exclusive.
*   **`CheckExclusiveWith`**: Compares this aura with another to determine which is stronger.
*   **`ExclusiveAuraCanApply`**: Checks if an exclusive aura can apply, potentially removing a weaker one.
*   **`ExclusiveAuraUnapply`**: Restores a stronger exclusive aura when this one is removed.

### SpellAuraHolder Management

*   **`SpellAuraHolder` (ctor)**: Initializes the holder, setting duration, charges, stacks, and passive/permanent flags.
*   **`AddAura`**: Adds an `Aura` to the holder's effect array.
*   **`RemoveAura`**: Removes an `Aura` from the holder.
*   **`ApplyAuraModifiers`**: Applies or removes modifiers for all auras in the holder.
*   **`_AddSpellAuraHolder`**: Finds a slot for the aura, updates unit fields, and applies diminishing returns.
*   **`_RemoveSpellAuraHolder`**: Cleans up triggered spells, updates unit fields, and removes diminishing returns.
*   **`CleanupTriggeredSpells`**: Removes spells triggered by this aura that have unlimited duration.
*   **`ModStackAmount`**: Modifies the stack count, returning true if the last stack is removed.
*   **`SetStackAmount`**: Sets the stack count, recalculating modifier amounts and refreshing duration.
*   **`GetId`**, **`GetCaster`**, **`GetRealCaster`**: Accessors for spell ID and caster units.
*   **`IsWeaponBuffCoexistableWith`**: Checks if a weapon buff can coexist with another.
*   **`IsNeedVisibleSlot`**: Determines if the aura needs a buff/debuff slot.
*   **`HandleSpellSpecificBoosts`**: Applies/removes specific boost spells associated with certain auras.
*   **`HandleCastOnAuraRemoval`**: Casts a spell when the aura is removed (e.g., Wyvern Sting).
*   **`HandleAuraSafeFall`**: A stub handler for `SPELL_AURA_SAFE_FALL`. The actual logic is implemented in `WorldSession::HandleMovementOpcodes` (not in this unit).
*   **`~SpellAuraHolder`**: Destructor that deletes all contained auras.
*   **`Update#4`** (`SpellAuraHolder::Update`): Decrements duration, handles power-per-second costs, updates contained auras, and checks channel range.
*   **`RefreshHolder`**: Refreshes the holder's duration.
*   **`RefreshAuraPeriodicTimers`**: Syncs periodic timers with the holder's duration.
*   **`SetAuraMaxDuration`**: Sets the maximum duration.
*   **`HasAuraType`**, **`HasMechanic`**, **`HasMechanicMask`**: Checks for specific aura types or mechanics.
*   **`IsPersistent`**, **`IsAreaAura`**, **`IsPositive`**, **`IsEmptyHolder`**: Status checks.
*   **`UnregisterSingleCastHolder`**: Unregisters single-target auras.
*   **`SetAura`**, **`SetAuraFlag`**, **`SetAuraLevel`**, **`UpdateAuraApplication`**, **`UpdateAuraDuration`**: Updates unit fields for aura display.
*   **`SetAffectedByVisibleSlotLimit`**: Flags the aura as affected by slot limits.
*   **`CalculateForBuffLimit`**, **`CalculateForDebuffLimit`**: Calculates priority scores for buff/debuff slot management.
*   **`CalculatePeriodic`**: Sets up periodic timers.
*   **`CalculateHeartBeat`**: Sets up heartbeat resist checks for crowd control.
*   **`HandleInterruptRegen`**: Interrupts regeneration.
*   **`HandleAuraAuraSpell`**: Applies/removes a secondary aura spell.
*   **`_IsExclusiveSpellAura`**: Static function determining if a spell aura is exclusive.

## Cross-Unit Boundaries

*   **Calls `Unit.Main` extensively**: Almost every aura handler interacts with `Unit` to modify stats, health, power, movement, threat, and state. `Unit::AddSpellAuraHolder` and `Unit::RemoveSpellAuraHolder` are key integration points. Note that `Unit::ModPossess` is defined in `Unit.Main`, not this unit.
*   **Calls `SpellCaster`**: For calculating spell values, casting triggered spells, and handling damage/healing bonuses.
*   **Calls `SpellEntry`/`SpellMgr`**: For retrieving spell data and checking attributes.
*   **Calls `Player.Main`**: For player-specific actions like inventory management, camera control, and reputation. Note that `Player::ModPossessPet` is defined in `Player.Main`.
*   **Calls `Creature.Main`**: For creature-specific actions like AI switching, display ID selection, and totem checks.
*   **Calls `ObjectAccessor`**: To retrieve units by GUID.
*   **Called by `Spell.Effects`**: `EffectApplyAura` and `EffectApplyAreaAura` create and apply auras.
*   **Called by `Unit.Main`**: `AddAura`, `RemoveAura`, and `HandleTriggers` interact with aura holders.
*   **Called by `ChatHandler`**: For debugging and admin commands.

## Data Model

This unit does not directly interact with database tables. It relies on in-memory spell data (`SpellEntry`) and unit state.

## Notable Implementation Details

*   **Exclusive Auras**: The code implements a custom system for exclusive auras (e.g., Blessing of Might vs. Battle Shout). `ComputeExclusive` marks an aura as exclusive, and `ExclusiveAuraCanApply`/`ExclusiveAuraUnapply` manage the swapping of the strongest active aura.
*   **Hardcoded Spell Logic**: Many `Handle...` methods contain large switch-case blocks for specific spell IDs. This is a legacy pattern for handling spells that don't fit standard aura behavior. Examples include `HandleAuraDummy` and `TriggerSpell`.
*   **Periodic Tick Drift**: `Aura::Update` adjusts the periodic timer to prevent drift, especially for permanent auras.
*   **Debuff Limit Priority**: `CalculateForDebuffLimit` assigns priority scores to debuffs to determine which ones persist when the debuff slot limit is reached. This is complex and involves checking spell families and aura types.
*   **Area Aura Sync**: `AreaAura::Update` ensures that auras on targets stay in sync with the caster's aura duration and periodic ticks.
*   **Real vs. Fake Application**: Handlers receive a `Real` boolean. `Real=true` means the aura is actually being added/removed. `Real=false` means it's a temporary reapplication for stat recalculation. Code that modifies object state (packets, AI) should only run when `Real=true`.

## Member Reference

**Aura** (ctor): Initializes an individual spell effect, calculating base values and setting up periodic timers.
**Refresh**: Updates an existing aura's duration and recalculates its modifier values.
**Refresh#2** (`SpellAuraHolder::Refresh`): Refreshes the entire holder (all effects) by updating duration, apply time, and calling `Refresh` on each contained `Aura`.
**CanBeRefreshedBy**: Determines if one `SpellAuraHolder` can refresh another.
**IsMoreImportantVisualAuraThan**: Compares two holders to decide which one persists when debuff/buff slot limits are reached.
**~Aura**: Destructor that cleans up the associated `SpellModifier` object.
**AreaAura** (ctor): Inherits from `Aura`. Sets up radius and target type (Party, Raid, Friend, Enemy, Pet, Owner, Creature Group).
**~AreaAura**: Destructor for `AreaAura`.
**PersistentAreaAura** (ctor): Inherits from `Aura`. Used for auras tied to a `DynamicObject`.
**~PersistentAreaAura**: Destructor for `PersistentAreaAura`.
**GetDynObject**: Retrieves the `DynamicObject` associated with a `PersistentAreaAura`.
**SingleEnemyTargetAura** (ctor): Inherits from `Aura`. Stores the caster's current target GUID.
**~SingleEnemyTargetAura**: Destructor for `SingleEnemyTargetAura`.
**GetTriggerTarget**: Resolves the trigger target for `SingleEnemyTargetAura`.
**CreateAura**: Factory function to create an `Aura` or `AreaAura`.
**CreateSpellAuraHolder**: Factory function to create a `SpellAuraHolder`.
**SetModifier**: Sets the type, amount, period, and misc value of the aura's modifier.
**GetMiscValue**: Retrieves the miscellaneous value from the spell prototype.
**UpdatePeriodicTimer**: Adjusts the periodic timer if the aura's duration changes.
**UpdateForAffected#2** (`Aura::UpdateForAffected`): Wraps `Update` with `SetInUse` flags.
**Update#2** (`Aura::Update`): Handles periodic ticks and group-based removal.
**UpdateForAffected** (`AreaAura::UpdateForAffected`): Wrapper for `AreaAura` updates.
**Update** (`AreaAura::Update`): Manages AoE aura application/removal based on radius and group membership.
**Update#3** (`PersistentAreaAura::Update`): Checks if the target is still within the `DynamicObject`'s radius.
**ApplyModifier**: Applies or removes the aura's effect, checking exclusivity.
**IsAffectedOnSpell**: Checks if this aura is affected by another spell.
**CanProcFrom**: Determines if the aura can proc from a specific spell event.
**ReapplyAffectedPassiveAuras#2**: Re-applies passive auras on a target affected by this aura's spell mods.
**ReapplyAffectedPassiveAurasHelper** (ctor): Helper functor for iterating controlled units.
**operator()**: Functor operator for `ReapplyAffectedPassiveAurasHelper`.
**ReapplyAffectedPassiveAuras**: Triggers re-application of affected passive auras.
**HandleAddModifier**: Adds or removes a `SpellModifier` from the target player.
**TriggerSpell**: Handles `SPELL_AURA_PERIODIC_TRIGGER_SPELL` with hardcoded logic for specific spells.
**HandleAuraDummy**: Handles `SPELL_AURA_DUMMY` with massive switch-case blocks for specific spell IDs.
**HandleAuraMounted**: Applies or removes a mount model.
**HandleAuraWaterWalk**: Toggles water walking flag.
**HandleAuraFeatherFall**: Toggles feather fall flag.
**HandleAuraHover**: Toggles hover flag.
**HandleWaterBreathing**: Adjusts water breathing interval multiplier.
**HandleModWaterBreathing**: Adjusts water breathing interval multiplier.
**GetShapeshiftDisplayInfo**: Static function returning display ID and scale for a shapeshift form.
**HandleAuraModShapeshift**: Changes the target's form, display ID, power type, and applies/removes associated boost auras.
**HandleAuraTransform**: Transforms the target into a creature model.
**HandleForceReaction**: Forces a reputation reaction with a faction.
**HandleAuraModSkill**: Modifies a player's skill bonus.
**HandleChannelDeathItem**: Awards items to the caster when the target dies.
**HandleBindSight**: Manipulates camera views.
**HandleFarSight**: Manipulates sight ranges.
**HandleAuraTrackCreatures**: Toggles creature tracking flag.
**HandleAuraTrackResources**: Toggles resource tracking flag.
**HandleAuraTrackStealthed**: Toggles stealthed tracking flag.
**HandleAuraModScale**: Changes the target's model scale.
**HandleModPossess**: Initiates possession of a unit.
**ModPossess** (`Unit::ModPossess`): Implemented in `Unit.Main`. Implements the mechanics of possession.
**HandleModPoss

---

<!-- machine-true, projected from graph.json -->

## Map — Unit.SpellAuras

*Source:* SpellAuras.cpp, SpellAuras.h, Unit.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| Aura | ctor | Aura/GetStackAmount, AuraScript/OnAuraValueCalculate, Errors/PrintStacktraceAndThrow, Log.Main/HasLogFilter, Log.Main/HasLogLevelOrHigher, Log.Main/Out, SpellAuraHolder/GetAuraScript, SpellCaster/CalculateSpellEffectValue, SpellEntry/CalculateSimpleValue, SpellEntry/IsPositiveEffect, SpellMgr/GetSpellEntry, SpellMgr/Instance, Unit.Main/GetSpellModOwner | — | — |
| Refresh | method | Aura/GetModifier, Aura/GetSpellProto, Aura/IsApplied, Aura/IsExclusive, SpellAuraHolder/GetAuraByEffectIndex, Unit.Main/GetSpellModOwner, Unit.Main/SetCanModifyStats, Unit.Main/UpdateAllStats | — | — |
| Refresh#2 | method | Object/GetObjectGuid, SpellAuraHolder/GetAuraByEffectIndex, SpellAuraHolder/GetAuraDuration, SpellAuraHolder/GetAuraMaxDuration | spell_mage/OnProc#2, Unit.Main/AddSpellAuraHolder | — |
| CanBeRefreshedBy | method | ObjectGuid/operator!=, SpellAuraHolder/GetCasterGuid | Unit.Main/AddSpellAuraHolder | — |
| IsMoreImportantVisualAuraThan | method | — | Unit.Main/RemoveAuraDueToVisibleSlotLimit | — |
| ~Aura | dtor | — | — | — |
| AreaAura | ctor | Creature.Main/IsTotem, Errors/PrintStacktraceAndThrow, Log.Main/Out, Object/GetTypeId, ObjectGuid/IsCreature, SpellEntry/GetSpellRadius, Unit.Main/GetCharmerOrOwnerOrOwnGuid, Unit.Main/GetSpellModOwner | Spell.Effects/EffectApplyAreaAura | — |
| ~AreaAura | dtor | — | — | — |
| PersistentAreaAura | ctor | — | — | — |
| ~PersistentAreaAura | dtor | — | — | — |
| GetDynObject | method | Aura/GetTarget, Map.Main/GetDynamicObject, Object/IsInWorld, WorldObject.Object/GetMap | — | — |
| SingleEnemyTargetAura | ctor | Object/GetTypeId, Player.Main/GetSelectionGuid, Unit.Main/GetTargetGuid | — | — |
| ~SingleEnemyTargetAura | dtor | — | — | — |
| GetTriggerTarget | method | ObjectAccessor/GetUnit, SpellAuraHolder/GetTarget | — | — |
| CreateAura | function | AuraScript/OnAuraInit, SpellAuraHolder/GetAuraScript, SpellEntry/IsAreaAuraEffect | ChatHandler.UnitCommands/HandleAuraHelper, Pet.Main/_LoadAuras, Player.Main/LoadAura, Spell.Effects/EffectApplyAura, Unit.Main/AddAura | — |
| CreateSpellAuraHolder | function | — | ChatHandler.UnitCommands/HandleAuraHelper, Pet.Main/_LoadAuras, Player.Main/LoadAura, Spell.Main/DoSpellHitOnUnit, Unit.Main/AddAura | — |
| SetModifier | method | — | — | — |
| GetMiscValue | method | SpellAuraHolder/GetSpellProto | spell_special/OnPeriodicTickEnd | — |
| UpdatePeriodicTimer | method | — | — | — |
| UpdateForAffected#2 | method | Aura/SetInUse | — | — |
| Update#2 | method | Aura/GetCasterGuid, Aura/GetId, Aura/GetSpellProto, Aura/GetTarget, Group/IsMember, Object/GetObjectGuid, ObjectGuid/IsPlayer, ObjectGuid/operator!=, Player.Main/GetGroup, Unit.Main/GetCharmerOrOwnerGuid, Unit.Main/GetCharmerOrOwnerPlayerOrPlayerItself, Unit.Main/RemoveAurasByCasterSpell | — | — |
| UpdateForAffected | method | — | — | — |
| Update | method | AnyAoETargetUnitInObjectRangeCheck/AnyAoETargetUnitInObjectRangeCheck, AnyCreatureGroupMembersInObjectRangeCheck/AnyCreatureGroupMembersInObjectRangeCheck, AnyFriendlyUnitInObjectRangeCheck/AnyFriendlyUnitInObjectRangeCheck, AnySameFactionUnitInObjectRangeCheck/AnySameFactionUnitInObjectRangeCheck, Aura/GetAuraDuration, Aura/GetAuraScript, Aura/GetCaster, Aura/GetCasterGuid, Aura/GetEffIndex, Aura/GetId, Aura/GetSpellProto, Aura/GetTarget, Aura/IsChanneled, AuraScript/OnAreaAuraCheckTarget, Creature.Main/GetCreatureGroup, game_Group_Group/SameSubGroup, Group/GetFirstMember, GroupReference/next, Log.Main/Out, Object/GetObjectGuid, Object/GetTypeId, Object/ToCreature, ObjectGuid/operator!=, ObjectGuid/operator==, Player.Main/GetGroup, Player.Main/GetSubGroup, Spell.Main/AddChanneledAuraHolder, SpellAuraHolder/GetAuraByEffectIndex, SpellAuraHolder/GetCasterGuid, SpellAuraHolder/GetTarget, SpellAuraHolder/IsDeleted, SpellAuraHolder/IsPassive, SpellAuraHolder/IsPermanent, SpellAuraHolder/SetAuraDuration, SpellAuraHolder/SetInUse, SpellCaster/GetCurrentSpell, SpellEntry/CalculateSimpleValue, SpellEntry/HasAttribute#5, SpellMgr/Instance, SpellMgr/SelectAuraRankForLevel, Unit.Main/AddAuraToModList, Unit.Main/AddSpellAuraHolder, Unit.Main/GetCharmerOrOwner, Unit.Main/GetCharmerOrOwnerGuid, Unit.Main/GetCharmerOrOwnerPlayerOrPlayerItself, Unit.Main/GetLevel, Unit.Main/GetPet, Unit.Main/GetSpellAuraHolder, Unit.Main/GetSpellAuraHolderBounds, Unit.Main/HasAura, Unit.Main/HasUnitState, Unit.Main/IsAlive, Unit.Main/IsFriendlyTo, Unit.Main/IsPvP, Unit.Main/RemoveSingleAuraFromSpellAuraHolder#2, WorldObject.Object/GetName, WorldObject.Object/IsWithinDistInMap | — | — |
| Update#3 | method | Aura/GetHolder, Aura/GetSpellProto, Aura/GetTarget, DynamicObject/GetRadius, DynamicObject/RemoveAffected, SpellEntry/HasAttribute#5, Unit.Main/RemoveSingleAuraFromSpellAuraHolder, WorldObject.Object/IsWithinDistInMap | — | — |
| ApplyModifier | method | Aura/GetAuraScript, Aura/GetHolder, Aura/IsApplied, Aura/IsExclusive, Aura/SetInUse, AuraScript/OnAfterApply, AuraScript/OnBeforeApply, SpellAuraHolder/SetInUse | Player.Main/SendInitialPacketsAfterAddToMap, Player.Main/SetSkill, spell_mage/OnProc#2, Unit.Main/RemoveAura | — |
| IsAffectedOnSpell | method | Aura/GetEffIndex, Aura/GetId, Aura/GetSpellProto, SpellEntry/IsFitToFamilyMask, SpellMgr/GetSpellAffectMask, SpellMgr/Instance, SpellModifier/IsAffectedOnSpell | Spell.Main/HandleAddTargetTriggerAuras, SpellCaster/SpellDamageBonusDone, SpellCaster/SpellHealingBonusDone | — |
| CanProcFrom | method | Aura/GetEffIndex, Aura/GetId, SpellMgr/GetSpellAffectMask, SpellMgr/GetSpellProcEvent, SpellMgr/Instance | Unit.Main/HandleTriggers | — |
| ReapplyAffectedPassiveAuras#2 | method | Aura/GetId, Object/GetObjectGuid, Object/GetTypeId, ObjectGuid/operator==, Player.Main/GetItemByGuid, SpellAuraHolder/GetCasterGuid, SpellAuraHolder/GetCastItemGuid, SpellAuraHolder/GetSpellProto, SpellAuraHolder/IsDeleted, SpellAuraHolder/IsPassive, SpellAuraHolder/IsPermanent, SpellCaster/CastSpell#2, Unit.Main/GetSpellAuraHolderMap, Unit.Main/RemoveAurasDueToSpell | — | — |
| ReapplyAffectedPassiveAurasHelper | ctor | — | — | — |
| operator() | method | — | — | — |
| ReapplyAffectedPassiveAuras | method | Aura/GetSpellProto, Aura/GetTarget | — | — |
| HandleAddModifier | method | Aura/GetHolder, Aura/GetSpellProto, Aura/GetTarget, Errors/PrintStacktraceAndThrow, Object/GetTypeId, Player.Main/AddSpellMod, SpellAuraHolder/GetAuraCharges, SpellAuraHolder/SetAuraCharges, SpellModifier/SpellModifier#2 | — | — |
| TriggerSpell | method | Aura/GetAuraScript, Aura/GetAuraTicks, Aura/GetCaster, Aura/GetCasterGuid, Aura/GetCastItemGuid, Aura/GetEffIndex, Aura/GetId, Aura/GetModifier, Aura/GetSpellProto, Aura/GetTarget, Aura/GetTriggerTarget, Aura/IsChanneled, AuraScript/OnPeriodicTrigger, Creature.Main/SetInCombatWithZone, game_Objects_Item/SetEnchantment, Geometry/NormalizeOrientation, Log.Main/Out, Map.Main/GetWorldObject, Object/GetObjectGuid, Object/GetTypeId, Object/IsType, ObjectGuid/IsEmpty, ObjectGuid/operator!, ObjectGuid/operator==, Player.Main/ApplyEnchantment, Player.Main/GetItemByGuid, Player.Main/GetWeaponForAttack, Player.Main/ToPlayer, ScriptMgr/OnEffectDummy, shared_Util/dither, shared_Util/urand, SpellAuraHolder/GetSpellProto, SpellCaster/CastCustomSpell#2, SpellCaster/CastSpell, SpellCaster/CastSpell#2, SpellCaster/CastSpell#3, SpellCaster/DealHeal, SpellCaster/EnergizeBySpell, SpellCaster/InterruptNonMeleeSpells, SpellCaster/SendEnergizeSpellLog, SpellEntry/GetSpellMaxRange, SpellEntry/HasAttribute, SpellEntry/HasAttribute#4, SpellMgr/GetSpellEntry, SpellMgr/Instance, Unit.Main/GetChannelObjectGuid, Unit.Main/GetMaxHealth, Unit.Main/GetMaxPower, Unit.Main/GetPower, Unit.Main/GetSpellAuraHolderMap, Unit.Main/GetVictim, Unit.Main/HasAura#2, Unit.Main/HasUnitState, Unit.Main/IsAlive, Unit.Main/ModifyPower, Unit.Main/RemoveAurasDueToSpell, Unit.Main/SetFacingTo, WorldObject.Object/GetMap, WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/IsWithinDist, WorldObject.Object/IsWithinDistInMap | — | — |
| HandleAuraDummy | method | Aura/GetCaster, Aura/GetCasterGuid, Aura/GetCastItemGuid, Aura/GetId, Aura/GetSpellProto, Aura/GetTarget, Camera/ResetView, Camera/SetView, Creature.Main/DespawnOrUnsummon, Creature.Main/HasCreatureState, Creature.Main/ToCreature, Map.Main/ScriptCommandStartDirect, Object/GetTypeId, Object/ToCreature, Object/ToPlayer, ObjectMgr/GetClosestGraveYard, ObjectMgr/GetWorldSafeLocFacing, Player.Main/AddSpellMod, Player.Main/GetCamera, Player.Main/GetItemByGuid, Player.Main/GetSpellMod, Player.Main/GetTeam, Player.Main/GetTeamId, Player.Main/InBattleGround, Player.Main/TeleportTo, Player.Main/ToPlayer, Player.Main/UnsummonPossessedMinion, Player.StatSystem/UpdateAttackPowerAndDamage#3, ScriptInfo/ScriptInfo, ScriptMgr/DoScriptText, ScriptMgr/OnAuraDummy, SpellCaster/CastSpell#2, SpellEntry/HasAttribute#4, SpellMgr/GetPetAura, SpellMgr/GetSpellAreaForAuraMapBounds, SpellMgr/Instance, SpellMgr/IsFitToRequirements, SpellModifier/SpellModifier#4, ThreatManager/modifyThreatPercent#2, Unit.Main/AddAura, Unit.Main/AddPetAura, Unit.Main/AddUnitState, Unit.Main/ClearUnitState, Unit.Main/DealDamage, Unit.Main/GetHealth, Unit.Main/GetThreatManager, Unit.Main/GetTotem, Unit.Main/HandleEmoteCommand, Unit.Main/HandleEmoteState, Unit.Main/HasAura, Unit.Main/IsAlive, Unit.Main/IsInCombat, Unit.Main/IsTaxiFlying, Unit.Main/RemoveAurasDueToSpell, Unit.Main/RemovePetAura, Unit.Main/SetFeignDeath, Unit.Main/SetHealth, Unit.Main/SetStandState, WorldObject.Object/GetMap, WorldObject.Object/GetMapId, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/GetZoneAndAreaId, WorldObject.Object/RemoveFlag, WorldObject.Object/SetFlag | — | — |
| HandleAuraMounted | method | Aura/GetId, Aura/GetTarget, Creature.Main/ChooseDisplayId, Log.Main/Out, ObjectMgr/GetCreatureDisplayInfoRandomGender, ObjectMgr/GetCreatureTemplate, Unit.Main/Mount, Unit.Main/Unmount | — | — |
| HandleAuraWaterWalk | method | Aura/GetTarget, Unit.Main/SetWaterWalking | — | — |
| HandleAuraFeatherFall | method | Aura/GetTarget, Unit.Main/SetFeatherFall | — | — |
| HandleAuraHover | method | Aura/GetTarget, Unit.Main/SetHover | — | — |
| HandleWaterBreathing | method | Aura/GetTarget, Object/ToPlayer, Player.Main/SetWaterBreathingIntervalMultiplier, Unit.Main/GetTotalAuraMultiplier, Unit.Main/HasAuraType | — | — |
| HandleModWaterBreathing | method | Aura/GetTarget, Object/ToPlayer, Player.Main/SetWaterBreathingIntervalMultiplier, Unit.Main/GetTotalAuraMultiplier | — | — |
| GetShapeshiftDisplayInfo | function | Object/IsPlayer, Player.Main/TeamForRace, Unit.Main/GetNativeDisplayId, Unit.Main/GetRace | — | — |
| HandleAuraModShapeshift | method | Aura/GetHolder, Aura/GetId, Aura/GetModifier, Aura/GetSpellProto, Aura/GetTarget, Log.Main/Out, Object/GetTypeId, Object/IsPlayer, Player.Main/InitDataForForm, shared_Util/irand, SpellCaster/CastSpell#2, Unit.Main/GetAurasByType, Unit.Main/GetClass, Unit.Main/GetNativeDisplayId, Unit.Main/GetPower, Unit.Main/GetPowerType, Unit.Main/GetTransForm, Unit.Main/RemoveAurasWithInterruptFlags, Unit.Main/RemoveSpellsCausingAura#2, Unit.Main/ResetTransformScale, Unit.Main/SetDisplayId, Unit.Main/SetPower, Unit.Main/SetPowerType, Unit.Main/SetShapeshiftForm, Unit.Main/SetTransformScale, Unit.Main/UpdateModelData, Unit.Main/UpdateSpeed | — | — |
| HandleAuraTransform | method | Aura/GetId, Aura/GetSpellProto, Aura/GetTarget, Creature.Main/ChooseDisplayId, Creature.Main/GetCreatureInfo, Creature.Main/LoadEquipment, Log.Main/Out, Object/GetTypeId, Object/IsCreature, ObjectMgr/GetCreatureDisplayInfoAddon, ObjectMgr/GetCreatureTemplate, SpellEntry/IsPositiveSpell, SpellEntry/IsPositiveSpell#4, Unit.Main/GetAurasByType, Unit.Main/GetDisplayId, Unit.Main/GetNativeDisplayId, Unit.Main/GetRace, Unit.Main/GetScaleForDisplayId, Unit.Main/GetShapeshiftForm, Unit.Main/GetTransForm, Unit.Main/ResetTransformScale, Unit.Main/SetDisplayId, Unit.Main/SetTransForm, Unit.Main/SetTransformScale, Unit.Main/UpdateModelData, Unit.Main/UpdateSpeed | — | — |
| HandleForceReaction | method | Aura/GetTarget, Object/GetTypeId, Player.Main/GetReputationMgr, Player.Main/GetReputationRank, ReputationMgr/ApplyForceReaction, ReputationMgr/SendForceReactions, Unit.Main/StopAttackFaction | — | — |
| HandleAuraModSkill | method | Aura/GetModifier, Aura/GetSpellProto, Aura/GetTarget, Object/GetTypeId, Object/ToPlayer, Player.Main/HasSkill, Player.Main/ModifySkillBonus, Player.StatSystem/UpdateDefenseBonusesMod | — | — |
| HandleChannelDeathItem | method | Aura/GetCaster, Aura/GetSpellProto, Aura/GetTarget, Creature.Main/IsTappedBy, Object/GetObjectGuid, Object/GetTypeId, Object/ToCreature, Object/ToPlayer, Player.Main/CanStoreNewItem, Player.Main/IsHonorOrXPTarget, Player.Main/SendEquipError, Player.Main/SendNewItem, Player.Main/StoreNewItem, Unit.Main/HasAuraTypeByCaster | — | — |
| HandleBindSight | method | Aura/GetCaster, Aura/GetTarget, Camera/ResetView, Camera/SetView, Player.Main/GetCamera, Player.Main/ToPlayer | — | — |
| HandleFarSight | method | Aura/GetCaster, Object/GetTypeId, Object/ToPlayer, Player.Main/SetLongSight | — | — |
| HandleAuraTrackCreatures | method | Aura/GetHolder, Aura/GetTarget, Object/GetTypeId, Unit.Main/RemoveNoStackAurasDueToAuraHolder, WorldObject.Object/RemoveFlag, WorldObject.Object/SetFlag | — | — |
| HandleAuraTrackResources | method | Aura/GetHolder, Aura/GetTarget, Object/GetTypeId, Unit.Main/RemoveNoStackAurasDueToAuraHolder, WorldObject.Object/RemoveFlag, WorldObject.Object/SetFlag | — | — |
| HandleAuraTrackStealthed | method | Aura/GetHolder, Aura/GetTarget, Object/ApplyModByteFlag, Object/GetTypeId, Unit.Main/RemoveNoStackAurasDueToAuraHolder | — | — |
| HandleAuraModScale | method | Aura/GetTarget, Object/ApplyPercentModFloatValue, Unit.Main/UpdateModelData | — | — |
| HandleModPossess | method | Aura/GetCaster, Aura/GetSpellProto, Aura/GetTarget, Object/ToPlayer, Player.Main/SendDirectMessage, UpdateData/BuildPacket#3, UpdateData/HasData, UpdateData/UpdateData, WorldObject.Object/BuildValuesUpdateBlockForPlayerWithFlags, WorldPacket/WorldPacket | — | — |
| ModPossess | method | Camera/ResetView, Camera/SetView, CharmInfo/SetCommandState, CharmInfo/SetOriginalFactionTemplate, CharmInfo/SetReactState, Creature.Main/AI, Creature.Main/AIM_Initialize, Creature.Main/HasStaticFlag, CreatureAI/SwitchAiAtControl, HostileRefManager/deleteReferences, MovementPacketSender/AddMovementFlagChangeToController, Object/GetEntry, Object/GetObjectGuid, Object/IsPet, Object/IsPlayer, Object/ToCreature, Object/ToPlayer, ObjectGuid/ObjectGuid, Player.Main/GetCamera, Player.Main/GetLootGuid, Player.Main/GetSession, Player.Main/PossessSpellInitialize, Player.Main/RelocateToLastClientPosition, Player.Main/RemovePetActionBar, Player.Main/RemoveTemporaryAI, Player.Main/SetClientControl, Player.Main/SetMover, SpellCaster/CastSpell#2, SpellEntry/GetSpellSchoolMask, Unit.Main/AddThreat, Unit.Main/AddUnitState, Unit.Main/AttackedBy, Unit.Main/ClearCharmInfo, Unit.Main/ClearUnitState, Unit.Main/CombatStop, Unit.Main/DeleteThreatList, Unit.Main/GetFactionTemplateId, Unit.Main/GetHostileRefManager, Unit.Main/GetMaxHealth, Unit.Main/HasUnitState, Unit.Main/InitCharmInfo, Unit.Main/InitPossessCreateSpells, Unit.Main/IsPvP, Unit.Main/IsRooted, Unit.Main/RemoveAttackersThreat, Unit.Main/RemoveAurasDueToSpell, Unit.Main/RestoreFaction, Unit.Main/ScheduleAINotify, Unit.Main/SetCharm, Unit.Main/SetCharmerGuid, Unit.Main/SetFactionTemplateId, Unit.Main/SetPossessorGuid, Unit.Main/SetPvP, Unit.Main/SetRooted, Unit.Main/SetWalk, Unit.Main/StopMoving, Unit.Main/UpdateControl, WorldObject.Object/GetFactionTemplateEntry, WorldObject.Object/IsValidAttackTarget, WorldObject.Object/IsWalking, WorldObject.Object/RemoveFlag, WorldObject.Object/SetFlag, WorldSession.LootHandler/DoLootRelease | — | — |
| HandleModPossessPet | method | Aura/GetCaster, Aura/GetTarget, Object/IsPet, Player.Main/ToPlayer | — | — |
| ModPossessPet | method | Camera/ResetView, Camera/SetView, Creature.MotionMaster/MoveIdle, MotionMaster/Clear, MovementPacketSender/AddMovementFlagChangeToController, Object/GetObjectGuid, ObjectGuid/ObjectGuid, Player.Main/GetCamera, Player.Main/SetMover, Unit.Main/AddUnitState, Unit.Main/GetCharmInfo, Unit.Main/GetMotionMaster, Unit.Main/HasUnitState, Unit.Main/IsRooted, Unit.Main/SetCharm, Unit.Main/SetCharmerGuid, Unit.Main/SetIsAtStay, Unit.Main/SetIsFollowing, Unit.Main/SetIsReturning, Unit.Main/SetPossessorGuid, Unit.Main/SetWalk, Unit.Main/StopMoving, Unit.Main/UpdateControl, WorldObject.Object/IsWalking, WorldObject.Object/SetFlag | Pet.Main/Unsummon | — |
| HandleModCharm | method | Aura/GetCaster, Aura/GetCasterGuid, Aura/GetHolder, Aura/GetSpellProto, Aura/GetTarget, CharmInfo/SetCommandState, CharmInfo/SetOriginalFactionTemplate, CharmInfo/SetReactState, Creature.Main/AI, Creature.Main/AIM_Initialize, Creature.Main/GetCreatureInfo, Creature.Main/HasStaticFlag, Creature.MotionMaster/MoveFollow, Creature.MotionMaster/MoveIdle, CreatureAI/SwitchAiAtControl, game_Group_Group/BroadcastGroupUpdate, HostileRefManager/deleteReferences, Log.Main/Out, MotionMaster/Clear, Object/GetByteValue, Object/GetGUIDLow, Object/GetObjectGuid, Object/GetTypeId, Object/IsPet, Object/IsPlayer, Object/ToCreature, Object/ToPlayer, ObjectGuid/ObjectGuid, ObjectGuid/operator==, ObjectMgr/GeneratePetNumber, Player.Main/CharmSpellInitialize, Player.Main/GetGroup, Player.Main/GetLootGuid, Player.Main/GetSession, Player.Main/RemovePetActionBar, Player.Main/RemoveTemporaryAI, Player.Main/SendAttackSwingCancelAttack, Player.Main/SendDirectMessage, Player.Main/SetControlledBy, Player.Main/SetFactionForRace, SpellCaster/InterruptNonMeleeSpells, SpellEntry/GetSpellSchoolMask, Unit.Main/AddThreat, Unit.Main/AttackedBy, Unit.Main/AttackStop, Unit.Main/ClearCharmInfo, Unit.Main/CombatStop, Unit.Main/DeleteThreatList, Unit.Main/GetCharmInfo, Unit.Main/GetClass, Unit.Main/GetFactionTemplateId, Unit.Main/GetHostileRefManager, Unit.Main/GetMaxHealth, Unit.Main/GetMotionMaster, Unit.Main/GetOwner, Unit.Main/GetRace, Unit.Main/InitCharmCreateSpells, Unit.Main/InitCharmInfo, Unit.Main/IsAlive, Unit.Main/IsInCombat, Unit.Main/IsPvP, Unit.Main/RemoveAllAttackers, Unit.Main/RemoveAttackersThreat, Unit.Main/RemoveAurasDueToSpell, Unit.Main/RemoveSpellsCausingAura#2, Unit.Main/SetCharm, Unit.Main/SetCharmerGuid, Unit.Main/SetFactionTemplateId, Unit.Main/SetInCombatState, Unit.Main/SetInCombatWith, Unit.Main/SetIsAtStay, Unit.Main/SetIsCommandAttack, Unit.Main/SetIsCommandFollow, Unit.Main/SetIsFollowing, Unit.Main/SetIsReturning, Unit.Main/SetPetNumber, Unit.Main/SetPvP, Unit.Main/StopMoving, Unit.Main/UpdateControl, UpdateData/BuildPacket#3, UpdateData/HasData, UpdateData/UpdateData, WorldObject.Object/BuildValuesUpdateBlockForPlayerWithFlags, WorldObject.Object/GetFactionTemplateEntry, WorldObject.Object/IsValidAttackTarget, WorldObject.Object/RemoveFlag, WorldObject.Object/SetByteValue, WorldObject.Object/SetFlag, WorldObject.Object/SetUInt32Value, WorldPacket/WorldPacket, WorldSession.LootHandler/DoLootRelease | — | — |
| HandleModConfuse | method | Aura/GetCasterGuid, Aura/GetId, Aura/GetTarget, Unit.Main/SetConfused | — | — |
| HandleModFear | method | Aura/GetCasterGuid, Aura/GetId, Aura/GetTarget, Unit.Main/SetFeared | — | — |
| HandleFeignDeath | method | Aura/GetCasterGuid, Aura/GetHolder, Aura/GetTarget, Creature.Main/GetAttackDistance, HostileReference/next, HostileRefManager/getFirst, Object/ToCreature, ObjectGuid/IsPlayer, SpellAuraHolder/GetSpellProto, SpellCaster/MagicSpellHitResult, ThreatManager/getSourceUnit, Unit.Main/GetCharmerOrOwnerGuid, Unit.Main/GetHostileRefManager, Unit.Main/SetFeignDeath, WorldObject.Object/IsWithinDistInMap | — | — |
| HandleAuraModDisarm | method | Aura/GetTarget, Object/ApplyModFlag, Object/GetTypeId, Object/ToPlayer, Player.Main/GetWeaponForAttack#2, Player.Main/SetRegularAttackTime, Player.Main/_ApplyWeaponDependentAuraMods, Unit.Main/HasAuraType, Unit.Main/IsNoWeaponShapeShift, Unit.Main/SetAttackTime, Unit.Main/UpdateDamagePhysical | — | — |
| HandleAuraModStun | method | Aura/GetCasterGuid, Aura/GetSpellProto, Aura/GetTarget, Creature.MotionMaster/GetCurrentMovementGeneratorType, Object/GetObjectGuid, Object/ToPlayer, ObjectGuid/ObjectGuid, ObjectGuid/operator!=, Player.Main/GetLootGuid, Player.Main/GetSession, SpellCaster/InterruptNonMeleeSpells, SpellEntry/GetSpellSchoolMask, Unit.Main/AddUnitState, Unit.Main/ClearUnitState, Unit.Main/GetAurasByType, Unit.Main/GetMotionMaster, Unit.Main/GetVictim, Unit.Main/HasAuraType, Unit.Main/HasUnitState, Unit.Main/IsAlive, Unit.Main/IsMounted, Unit.Main/IsTaxiFlying, Unit.Main/ModifyAuraState, Unit.Main/SetRooted, Unit.Main/SetStandState, Unit.Main/SetTargetGuid, WorldObject.Object/RemoveFlag, WorldObject.Object/SetFlag, WorldSession.LootHandler/DoLootRelease | — | — |
| HandleModStealth | method | Aura/GetSpellProto, Aura/GetTarget, Object/GetTypeId, Player.Main/GetZoneScript, Unit.Main/GetSpellAuraHolder#2, Unit.Main/GetVisibility, Unit.Main/HasAuraType, Unit.Main/InterruptSpellsCastedOnMe, Unit.Main/RemoveAurasDueToSpellByCancel, Unit.Main/RemoveAurasWithInterruptFlags, Unit.Main/SetVisibility, WorldObject.Object/GetZoneId, WorldObject.Object/RemoveByteFlag, WorldObject.Object/SetByteFlag, ZoneScript/HandleDropFlag#3 | — | — |
| HandleInvisibility | method | Aura/GetModifier, Aura/GetTarget, Object/GetTypeId, Unit.Main/GetAurasByType, Unit.Main/GetVisibility, Unit.Main/HasAuraType, Unit.Main/RemoveAurasWithInterruptFlags, Unit.Main/SetVisibility, WorldObject.Object/RemoveByteFlag, WorldObject.Object/SetByteFlag | — | — |
| HandleInvisibilityDetect | method | Aura/GetModifier, Aura/GetTarget, Camera/UpdateVisibilityForOwner, Object/GetTypeId, Player.Main/GetCamera, Unit.Main/GetAurasByType | — | — |
| HandleDetectAmore | method | Aura/GetTarget, Object/ApplyModByteFlag, Object/IsPlayer | — | — |
| HandleAuraModRoot | method | Aura/GetSpellProto, Aura/GetTarget, Creature.MotionMaster/GetCurrentMovementGeneratorType, MoveSpline/Finalized, SpellEntry/GetSpellSchoolMask, Unit.Main/AddUnitState, Unit.Main/ClearUnitState, Unit.Main/GetAurasByType, Unit.Main/GetMotionMaster, Unit.Main/GetVictim, Unit.Main/HasAuraType, Unit.Main/HasUnitState, Unit.Main/ModifyAuraState, Unit.Main/SetRooted, Unit.Main/StopMoving, WorldObject.Object/GetAngle, WorldObject.Object/SetOrientation | — | — |
| HandleAuraModSilence | method | Aura/GetTarget, SpellCaster/GetCurrentSpell, SpellCaster/InterruptSpell, Unit.Main/HasAuraType, WorldObject.Object/RemoveFlag, WorldObject.Object/SetFlag | — | — |
| HandleModThreat | method | Aura/GetId, Aura/GetTarget, Object/GetTypeId, shared_Util/ApplyPercentModFloatVar, Unit.Main/GetLevel | — | — |
| HandleAuraModTotalThreat | method | Aura/GetCaster, Aura/GetTarget, HostileRefManager/addTempThreat, Object/GetTypeId, Unit.Main/GetHostileRefManager, Unit.Main/IsAlive | — | — |
| HandleModTaunt | method | Aura/GetCaster, Aura/GetCasterGuid, Aura/GetTarget, Unit.Main/AddTauntCaster, Unit.Main/CanHaveThreatList, Unit.Main/IsAlive, Unit.Main/RemoveTauntCaster, Unit.Main/TauntApply, Unit.Main/TauntFadeOut | — | — |
| HandleAuraModIncreaseSpeed | method | Aura/GetCaster, Aura/GetHolder, Aura/GetSpellProto, Aura/GetTarget, SpellAuraHolder/IsAddedBySpell, Unit.Main/GetSpellModOwner, Unit.Main/UpdateSpeed | — | — |
| HandleAuraModIncreaseMountedSpeed | method | Aura/GetTarget, Unit.Main/UpdateSpeed | — | — |
| HandleAuraModIncreaseSwimSpeed | method | Aura/GetTarget, Unit.Main/UpdateSpeed | — | — |
| HandleAuraModDecreaseSpeed | method | Aura/GetCaster, Aura/GetHolder, Aura/GetSpellProto, Aura/GetTarget, SpellAuraHolder/IsAddedBySpell, Unit.Main/GetSpellModOwner, Unit.Main/UpdateSpeed | — | — |
| HandleAuraModUseNormalSpeed | method | Aura/GetTarget, Unit.Main/UpdateSpeed | — | — |
| HandleModMechanicImmunity | method | Aura/GetId, Aura/GetSpellProto, Aura/GetTarget, SpellEntry/HasAttribute#3, Unit.Main/ApplySpellImmune, Unit.Main/RemoveAurasAtMechanicImmunity | — | — |
| HandleModMechanicImmunityMask | method | Aura/GetId, Aura/GetSpellProto, Aura/GetTarget, SpellEntry/HasAttribute#3, Unit.Main/RemoveAurasAtMechanicImmunity | — | — |
| HandleAuraModEffectImmunity | method | Aura/GetId, Aura/GetSpellProto, Aura/GetTarget, BattleGround/EventPlayerDroppedFlag, Object/IsPlayer, Player.Main/GetBattleGround, SpellEntry/HasAuraInterruptFlag, Unit.Main/ApplySpellImmune, Unit.Main/HasAuraType | — | — |
| HandleAuraModStateImmunity | method | Aura/GetId, Aura/GetSpellProto, Aura/GetTarget, SpellEntry/HasAttribute#3, Unit.Main/ApplySpellImmune, Unit.Main/GetAurasByType, Unit.Main/RemoveAurasDueToSpell | — | — |
| HandleAuraModSchoolImmunity | method | Aura/GetId, Aura/GetSpellProto, Aura/GetTarget, Aura/IsPositive, Object/IsPlayer, SpellAuraHolder/GetSpellProto, SpellEntry/GetSpellSchoolMask, SpellEntry/HasAttribute, SpellEntry/HasAttribute#3, SpellEntry/IsPositiveSpell, Unit.Main/AddUnitState, Unit.Main/ApplySpellImmune, Unit.Main/ClearUnitState, Unit.Main/GetSpellAuraHolderMap, Unit.Main/HasAuraType, Unit.Main/IsCharmed, Unit.Main/RemoveAurasDueToSpell, Unit.Main/RemoveAurasWithInterruptFlags, WorldObject.Object/RemoveFlag, WorldObject.Object/SetFlag | — | — |
| HandleAuraModDmgImmunity | method | Aura/GetId, Aura/GetTarget, Unit.Main/ApplySpellImmune | — | — |
| HandleAuraModDispelImmunity | method | Aura/GetSpellProto, Aura/GetTarget, Unit.Main/ApplySpellDispelImmunity | — | — |
| HandleAuraProcTriggerSpell | method | Aura/GetHolder, Aura/GetId, SpellAuraHolder/SetAuraCharges | — | — |
| HandleAuraModStalked | method | Aura/GetTarget, WorldObject.Object/ForceValuesUpdateAtIndex, WorldObject.Object/RemoveFlag, WorldObject.Object/SetFlag | — | — |
| HandlePeriodicTriggerSpell | method | Aura/GetId, Aura/GetRealCaster, Aura/GetTarget, Creature.Main/GetCreatureInfo, Creature.Main/SetDefaultGossipMenuId, GameObject/SendGameObjectCustomAnim, GameObject/ToGameObject, LoveIsInTheAir/GetLoveIsInTheAirGossipForCreature, Object/GetEntry, Object/ToCreature, SpellCaster/CastSpell#2, Unit.Main/GetGender, WorldObject.Object/RemoveFlag, WorldObject.Object/SetFlag | — | — |
| HandlePeriodicTriggerSpellWithValue | method | — | — | — |
| HandlePeriodicEnergize | method | — | — | — |
| HandleAuraPowerBurn | method | — | — | — |
| HandlePeriodicHeal | method | Aura/GetCaster, Aura/GetEffIndex, Aura/GetSpellProto, Aura/GetStackAmount, Aura/GetTarget, Object/GetTypeId, Player.Main/GetSession, SpellCaster/SpellHealingBonusDone, WorldSession.Main/PlayerLoading | — | — |
| CalculateDotDamage | method | Aura/GetCaster, Aura/GetEffIndex, Aura/GetSpellProto, Aura/GetStackAmount, Aura/GetTarget, Object/GetTypeId, Object/IsPlayer, Player.Main/GetComboPoints, SpellCaster/MeleeDamageBonusDone, SpellCaster/SpellDamageBonusDone, SpellEntry/GetWeaponAttackType, Unit.Main/GetTotalAttackPowerValue | — | — |
| HandlePeriodicDamage | method | Aura/GetCaster, Aura/GetTarget, Object/GetTypeId, Player.Main/GetSession, WorldSession.Main/PlayerLoading | — | — |
| HandlePeriodicDamagePCT | method | — | — | — |
| HandlePeriodicLeech | method | Aura/GetCaster, Aura/GetEffIndex, Aura/GetSpellProto, Aura/GetStackAmount, Aura/GetTarget, Object/GetTypeId, Player.Main/GetSession, SpellCaster/SpellDamageBonusDone, WorldSession.Main/PlayerLoading | — | — |
| HandlePeriodicManaLeech | method | — | — | — |
| HandlePeriodicHealthFunnel | method | Aura/GetCaster, Aura/GetEffIndex, Aura/GetSpellProto, Aura/GetStackAmount, Aura/GetTarget, Object/GetTypeId, Player.Main/GetSession, SpellCaster/SpellDamageBonusDone, WorldSession.Main/PlayerLoading | — | — |
| HandleAuraModResistanceExclusive | method | Aura/GetModifier, Aura/GetTarget, Object/GetTypeId, Player.Main/ApplyResistanceBuffModsMod, Unit.Main/GetAurasByType, Unit.Main/GetTotalResistanceValue, Unit.Main/HandleStatModifier | — | — |
| HandleAuraModResistance | method | Aura/GetSpellProto, Aura/GetTarget, Object/GetTypeId, Player.Main/ApplyResistanceBuffModsMod, Unit.Main/ApplySpellDispelImmunity, Unit.Main/GetTotalResistanceValue, Unit.Main/HandleStatModifier | — | — |
| HandleAurasVisible | method | Aura/GetTarget, Object/ApplyModFlag | — | — |
| HandleModResistancePercent | method | Aura/GetTarget, Object/GetTypeId, Player.Main/ApplyResistanceBuffModsMod, Unit.Main/GetTotalResistanceValue, Unit.Main/HandleStatModifier | — | — |
| HandleModBaseResistance | method | Aura/GetTarget, Unit.Main/HandleStatModifier | — | — |
| HandleAuraModBaseResistancePercent | method | Aura/GetTarget, Unit.Main/HandleStatModifier | — | — |
| HandleAuraModStat | method | Aura/GetCaster, Aura/GetEffIndex, Aura/GetHolder, Aura/GetId, Aura/GetModifier, Aura/GetSpellProto, Aura/GetTarget, Log.Main/Out, Object/GetTypeId, Player.Main/ApplyStatBuffMod, SpellCaster/CastCustomSpell#2, Unit.Main/GetAurasByType, Unit.Main/HandleStatModifier, Unit.Main/RemoveAurasDueToSpell | — | — |
| HandleModPercentStat | method | Aura/GetTarget, Log.Main/Out, Object/GetTypeId, Unit.Main/HandleStatModifier | — | — |
| HandleModSpellDamagePercentFromStat | method | Aura/GetTarget, Object/GetTypeId, Player.StatSystem/UpdateSpellDamageAndHealingBonus | — | — |
| HandleModSpellHealingPercentFromStat | method | Aura/GetTarget, Object/GetTypeId, Player.StatSystem/UpdateSpellDamageAndHealingBonus | — | — |
| HandleModHealingDone | method | Aura/GetTarget, Object/GetTypeId, Player.StatSystem/UpdateSpellDamageAndHealingBonus | — | — |
| HandleModTotalPercentStat | method | Aura/GetSpellProto, Aura/GetTarget, Log.Main/Out, Object/GetTypeId, Player.Main/ApplyStatPercentBuffMod, Unit.Main/GetHealth, Unit.Main/GetMaxHealth, Unit.Main/HandleStatModifier, Unit.Main/IsAlive, Unit.Main/SetHealth | — | — |
| HandleAuraModResistenceOfStatPercent | method | Aura/GetTarget, Log.Main/Out, Object/GetTypeId, Unit.Main/UpdateArmor | — | — |
| HandleAuraModTotalHealthPercentRegen | method | — | — | — |
| HandleAuraModTotalManaPercentRegen | method | — | — | — |
| HandleModRegen | method | — | — | — |
| HandleModPowerRegen | method | Aura/GetTarget, Unit.Main/GetPowerType, Unit.Main/UpdateManaRegen | — | — |
| HandleModPowerRegenPCT | method | Aura/GetTarget, Unit.Main/UpdateManaRegen | — | — |
| HandleAuraModIncreaseHealth | method | Aura/GetId, Aura/GetTarget, shared_Util/dither, SpellCaster/CastCustomSpell#2, Unit.Main/GetHealth, Unit.Main/GetMaxHealth, Unit.Main/HandleStatModifier, Unit.Main/IsAlive, Unit.Main/ModifyHealth, Unit.Main/SetHealth | — | — |
| HandleAuraModIncreaseEnergy | method | Aura/GetTarget, Unit.Main/HandleStatModifier, Unit.Main/ModifyPower | — | — |
| HandleAuraModIncreaseEnergyPercent | method | Aura/GetTarget, Unit.Main/GetMaxPower, Unit.Main/GetPowerPercent, Unit.Main/HandleStatModifier, Unit.Main/IsAlive, Unit.Main/SetPower | — | — |
| HandleAuraModIncreaseHealthPercent | method | Aura/GetTarget, Unit.Main/DoKillUnit, Unit.Main/GetHealthPercent, Unit.Main/GetMaxHealth, Unit.Main/HandleStatModifier, Unit.Main/IsAlive, Unit.Main/SetHealthPercent | — | — |
| HandleAuraModParryPercent | method | Aura/GetTarget, Object/GetTypeId, Player.StatSystem/UpdateParryPercentage | — | — |
| HandleAuraModDodgePercent | method | Aura/GetTarget, Object/GetTypeId, Player.StatSystem/UpdateDodgePercentage | — | — |
| HandleAuraModBlockPercent | method | Aura/GetTarget, Object/GetTypeId, Player.StatSystem/UpdateBlockPercentage | — | — |
| HandleAuraModRegenInterrupt | method | Aura/GetTarget, Object/GetTypeId, Player.StatSystem/UpdateManaRegen#2 | — | — |
| HandleAuraModCritPercent | method | Aura/GetSpellProto, Aura/GetTarget, Object/GetTypeId, Player.Main/GetWeaponForAttack#2, Player.Main/HandleBaseModValue, Player.Main/_ApplyWeaponDependentAuraCritMod | — | — |
| HandleModSpellHitChance | method | Aura/GetTarget | — | — |
| HandleModSpellCritChance | method | Aura/GetTarget, Unit.Main/UpdateAllSpellCritChances | — | — |
| HandleModSpellCritChanceSchool | method | Aura/GetTarget, Unit.Main/UpdateSpellCritChance | — | — |
| HandleModCastingSpeed | method | Aura/GetCaster, Aura/GetSpellProto, Aura/GetTarget, Unit.Main/GetSpellModOwner, Unit.Main/UpdateCastSpeed | — | — |
| HandleModAttackSpeed | method | Aura/GetCaster, Aura/GetSpellProto, Aura/GetTarget, Unit.Main/ApplyAttackTimePercentMod, Unit.Main/GetSpellModOwner, Unit.Main/HandleStatModifier | — | — |
| HandleModMeleeSpeedPct | method | Aura/GetCaster, Aura/GetSpellProto, Aura/GetTarget, Unit.Main/ApplyAttackTimePercentMod, Unit.Main/GetSpellModOwner | — | — |
| HandleAuraModRangedHaste | method | Aura/GetCaster, Aura/GetSpellProto, Aura/GetTarget, Unit.Main/ApplyAttackTimePercentMod, Unit.Main/GetSpellModOwner | — | — |
| HandleRangedAmmoHaste | method | Aura/GetCaster, Aura/GetSpellProto, Aura/GetTarget, game_Objects_Item/GetProto, Object/GetTypeId, Object/ToPlayer, Player.Main/GetItemByPos, Unit.Main/ApplyAttackTimePercentMod, Unit.Main/GetSpellModOwner | — | — |
| HandleAuraModAttackPower | method | Aura/GetCaster, Aura/GetSpellProto, Aura/GetTarget, Aura/IsPositive, Unit.Main/GetSpellModOwner, Unit.Main/HandleAttackPowerModifier | — | — |
| HandleAuraModRangedAttackPower | method | Aura/GetCaster, Aura/GetSpellProto, Aura/GetTarget, Aura/IsPositive, Unit.Main/GetClassMask, Unit.Main/GetSpellModOwner, Unit.Main/HandleAttackPowerModifier | — | — |
| HandleAuraModAttackPowerPercent | method | Aura/GetCaster, Aura/GetSpellProto, Aura/GetTarget, Unit.Main/GetSpellModOwner, Unit.Main/HandleAttackPowerModifier | — | — |
| HandleAuraModRangedAttackPowerPercent | method | Aura/GetCaster, Aura/GetSpellProto, Aura/GetTarget, Unit.Main/GetClassMask, Unit.Main/GetSpellModOwner, Unit.Main/HandleAttackPowerModifier | — | — |
| HandleModDamageDone | method | Aura/GetSpellProto, Aura/GetTarget, Object/GetTypeId, Object/ToPlayer, Player.Main/GetWeaponForAttack#2, Player.Main/_ApplyWeaponDependentAuraDamageMod, Player.StatSystem/UpdateAttackPowerAndDamage#2, Unit.Main/GetPet, Unit.Main/HandleStatModifier, WorldObject.Object/ApplyModInt32Value, WorldObject.Object/ApplyModUInt32Value | — | — |
| HandleModDamagePercentDone | method | Aura/GetSpellProto, Aura/GetTarget, Log.Main/HasLogFilter, Log.Main/HasLogLevelOrHigher, Log.Main/Out, Object/IsPlayer, Object/ToPlayer, Player.Main/GetWeaponForAttack#2, Player.Main/UpdateDamageDonePercent, Player.Main/_ApplyWeaponDependentAuraDamageMod, Unit.Main/HandleStatModifier | — | — |
| HandleModOffhandDamagePercent | method | Aura/GetTarget, Log.Main/HasLogFilter, Log.Main/HasLogLevelOrHigher, Log.Main/Out, Unit.Main/HandleStatModifier | — | — |
| HandleModPowerCostPCT | method | Aura/GetTarget, Unit.Main/GetTotalAuraModifierByMiscMask, WorldObject.Object/SetFloatValue | — | — |
| HandleModPowerCost | method | Aura/GetTarget, WorldObject.Object/ApplyModInt32Value | — | — |
| HandleReflectSpellsSchool | method | Aura/GetCaster, Aura/GetId, Unit.Main/GetSpellModOwner | — | — |
| HandleShapeshiftBoosts | method | Aura/GetModifier, Aura/GetSpellProto, Aura/GetTarget, Object/GetTypeId, Object/IsPlayer, Player.Main/GetSpellMap, Player.Main/GetWeaponForAttack#2, Player.Main/HasSpell, Player.Main/_ApplyWeaponDependentAuraCritMod, SharedDefines/IsAttackSpeedOverridenForm, SpellAuraHolder/IsRemovedOnShapeLost, SpellCaster/CastCustomSpell#2, SpellCaster/CastSpell#2, SpellCaster/GetCurrentSpell, SpellCaster/InterruptSpell, SpellEntry/IsNeedCastSpellAtFormApply, SpellEntry/IsRemovedOnShapeLostSpell, SpellMgr/GetSpellEntry, SpellMgr/Instance, Unit.Main/AddAura, Unit.Main/GetAurasByType, Unit.Main/GetSpellAuraHolderMap, Unit.Main/RemoveAurasDueToSpell | — | — |
| HandleAuraEmpathy | method | Aura/GetCaster, Aura/GetTarget, Player.Main/SendDirectMessage, Player.Main/ToPlayer, UpdateData/BuildPacket#3, UpdateData/HasData, UpdateData/UpdateData, WorldObject.Object/ApplyModUInt32Value, WorldObject.Object/BuildValuesUpdateBlockForPlayerWithFlags, WorldPacket/WorldPacket | — | — |
| HandleAuraUntrackable | method | Aura/GetTarget, WorldObject.Object/RemoveByteFlag, WorldObject.Object/SetByteFlag | — | — |
| HandleAuraModPacify | method | Aura/GetTarget, WorldObject.Object/RemoveFlag, WorldObject.Object/SetFlag | — | — |
| HandleAuraModPacifyAndSilence | method | — | — | — |
| HandleAuraGhost | method | Aura/GetTarget, Object/ToPlayer, Player.Main/GetGroup, Player.Main/SetGroupUpdateFlag, WorldObject.Object/RemoveByteFlag, WorldObject.Object/RemoveFlag, WorldObject.Object/SetByteFlag, WorldObject.Object/SetFlag | — | — |
| HandleShieldBlockValue | method | Aura/GetTarget, Object/GetTypeId, Player.Main/HandleBaseModValue | — | — |
| HandleAuraRetainComboPoints | method | Aura/GetTarget, Object/GetTypeId, ObjectAccessor/GetUnit, Player.Main/AddComboPoints, Player.Main/GetComboTargetGuid | — | — |
| HandleModUnattackable | method | Aura/GetTarget, Object/ApplyModFlag, Unit.Main/CombatStop, Unit.Main/RemoveAurasWithInterruptFlags | — | — |
| HandleSpiritOfRedemption | method | Aura/GetTarget, Object/GetTypeId, SpellCaster/CastSpell#2, Unit.Main/GetMaxHealth, Unit.Main/GetMaxPower, Unit.Main/IsStandingUp, Unit.Main/SetInvincibilityHpThreshold, Unit.Main/SetPower, Unit.Main/SetStandState, WorldObject.Object/RemoveFlag, WorldObject.Object/SetFlag | — | — |
| HandleAuraAoeCharm | method | Aura/GetSpellProto | — | — |
| HandleSchoolAbsorb | method | Aura/GetCaster, Aura/GetSpellProto, Aura/GetTarget, Object/GetTypeId, Player.Main/GetSession, shared_Util/dither, SpellCaster/CalculateLevelPenalty, SpellCaster/SpellBaseDamageBonusDone, SpellCaster/SpellBaseHealingBonusDone, SpellEntry/GetSpellSchoolMask, WorldSession.Main/PlayerLoading | — | — |
| PeriodicTick | method | Aura/GetAuraScript, Aura/GetAuraTicks, Aura/GetCaster, Aura/GetCasterGuid, Aura/GetEffIndex, Aura/GetHolder, Aura/GetId, Aura/GetSpellProto, Aura/GetStackAmount, Aura/GetTarget, AuraScript/OnPeriodicCalculateAmount, AuraScript/OnPeriodicTickEnd, CleanDamage/CleanDamage, HostileRefManager/threatAssist, Log.Main/HasLogFilter, Log.Main/HasLogLevelOrHigher, Log.Main/Out, Object/GetGuidStr, Object/GetTypeId, Object/IsInWorld, ObjectGuid/GetString, shared_Util/dither, shared_Util/ditheru, shared_Util/urand, Spell.Main/cancel, SpellAuraHolder/GetSpellProto, SpellAuraHolder/IsReflected, SpellAuraHolder/SetAuraDuration, SpellCaster/CalculateSpellDamage, SpellCaster/CastSpell#2, SpellCaster/DealDamageMods, SpellCaster/DealHeal, SpellCaster/DealSpellDamage, SpellCaster/FinishSpell, SpellCaster/GetCurrentSpell, SpellCaster/IsNonMeleeSpellCasted, SpellCaster/ProcDamageAndSpell, SpellCaster/ProcSystemArguments, SpellCaster/SendSpellMiss, SpellCaster/SendSpellNonMeleeDamageLog, SpellCaster/SendSpellNonMeleeDamageLog#2, SpellCaster/SendSpellOrDamageImmune, SpellCaster/SpellDamageBonusDone, SpellCaster/SpellHitResult, SpellDefines/GetSchoolMask, SpellEntry/CalculateSimpleValue, SpellEntry/GetSpellSchoolMask, SpellEntry/GetWeaponAttackType, SpellEntry/HasAuraInterruptFlag, SpellMgr/GetSpellThreatMultiplier, SpellMgr/Instance, SpellNonMeleeDamage/SpellNonMeleeDamage, SpellPeriodicAuraLogInfo/SpellPeriodicAuraLogInfo, Unit.Main/AddThreat, Unit.Main/CalculateAbsorbResistBlock, Unit.Main/CalculateDamageAbsorbAndResist, Unit.Main/CreateProcExtendMask, Unit.Main/DealDamage, Unit.Main/GetAura#2, Unit.Main/GetHealth, Unit.Main/GetHostileRefManager, Unit.Main/GetMaxHealth, Unit.Main/GetMaxPower, Unit.Main/GetPower, Unit.Main/GetPowerType, Unit.Main/GetSpellModOwner, Unit.Main/HandleEmoteCommand, Unit.Main/IsAlive, Unit.Main/IsImmuneToDamage, Unit.Main/IsImmuneToSchool, Unit.Main/IsSpellCrit, Unit.Main/MeleeDamageBonusTaken, Unit.Main/ModifyHealth, Unit.Main/ModifyPower, Unit.Main/RemoveAurasDueToSpell, Unit.Main/RemoveAurasWithInterruptFlags, Unit.Main/SendPeriodicAuraLog, Unit.Main/SpellDamageBonusTaken, Unit.Main/SpellHealingBonusTaken | — | — |
| PeriodicDummyTick | method | Aura/GetAuraScript, Aura/GetCaster, Aura/GetHolder, Aura/GetSpellProto, Aura/GetTarget, AuraScript/OnPeriodicDummy, Creature.Main/DespawnOrUnsummon, Creature.Main/ToCreature, Creature.MotionMaster/GetCurrentMovementGeneratorType, Creature.MotionMaster/MoveFollow, Object/ToCreature, Object/ToPlayer, shared_Util/urand, SpellAuraHolder/SetAuraDuration, SpellCaster/CastSpell#2, SpellEntry/GetSpellRadius, Unit.Main/GetMotionMaster, Unit.Main/GetSpellAuraHolderMap, Unit.Main/HandleEmoteCommand, Unit.Main/IsAlive, Unit.Main/IsDead, Unit.Main/IsInCombat, WorldObject.Object/IsMoving, WorldObject.Object/IsWithinDistInMap | — | — |
| HandlePreventFleeing | method | Aura/GetCasterGuid, Aura/GetId, Aura/GetTarget, Creature.MotionMaster/GetCurrentMovementGeneratorType, MotionMaster/MovementExpired, Object/GetTypeId, Unit.Main/GetAurasByType, Unit.Main/GetMotionMaster, Unit.Main/SetFeared | — | — |
| HandleManaShield | method | Aura/GetCaster, Aura/GetTarget, Object/GetTypeId, Player.Main/GetSession, WorldSession.Main/PlayerLoading | — | — |
| IsLastAuraOnHolder | method | Aura/GetEffIndex, Aura/GetHolder | Unit.Main/RemoveSingleAuraFromSpellAuraHolder | — |
| SpellAuraHolder | ctor | AuraScript/OnHolderInit, Errors/PrintStacktraceAndThrow, Object/GetObjectGuid, Object/IsType, ObjectGuid/ObjectGuid, ObjectGuid/operator==, ScriptMgr/GetAuraScript, SpellAuraHolder/GetSpellProto, SpellEntry/CalculateDuration, SpellEntry/HasAura, SpellEntry/HasAuraInterruptFlag, SpellEntry/HasSingleTargetAura, SpellEntry/IsChanneledSpell, SpellEntry/IsDeathPersistentSpell, SpellEntry/IsPassiveSpell, SpellEntry/IsPositiveSpell#4, SpellEntry/IsRemovedOnShapeLostSpell, SpellMgr/GetSpellEntry, SpellMgr/Instance, Unit.Main/GetSpellModOwner | — | — |
| AddAura | method | Aura/GetEffIndex, Errors/PrintStacktraceAndThrow | ChatHandler.UnitCommands/HandleAuraHelper, Pet.Main/_LoadAuras, Player.Main/LoadAura, Spell.Effects/EffectApplyAreaAura, Spell.Effects/EffectApplyAura, Unit.Main/AddAura | — |
| RemoveAura | method | — | Unit.Main/RemoveAura | — |
| ApplyAuraModifiers | method | SpellAuraHolder/GetAuraByEffectIndex, SpellAuraHolder/IsDeleted | Unit.Main/AddSpellAuraHolder, Unit.Main/_ApplyAllAuraMods, Unit.Main/_RemoveAllAuraMods | — |
| _AddSpellAuraHolder | method | Object/GetUInt32Value, SpellAuraHolder/getDiminishGroup, SpellAuraHolder/GetSpellProto, SpellAuraHolder/SetAuraSlot, SpellEntry/HasAttribute#3, SpellEntry/HasAuraInterruptFlag, SpellEntry/IsSealSpell, Unit.Main/ApplyDiminishingAura, Unit.Main/GetLevel, Unit.Main/IsSittingDown, Unit.Main/ModifyAuraState, Unit.Main/RemoveSpellsCausingAura#2, Unit.Main/SetStandState, Unit.Main/UpdateAuraForGroup, World/getConfig#4, WorldObject.Object/SetFlag | Unit.Main/AddSpellAuraHolder | — |
| _RemoveSpellAuraHolder | method | Aura/IsPersistent, DynamicObject/RemoveAffected, Object/GetUInt32Value, SpellAuraHolder/GetAuraSlot, SpellAuraHolder/getDiminishGroup, SpellAuraHolder/GetSpellProto, SpellCaster/AddCooldown, SpellEntry/HasAttribute, SpellEntry/HasAttribute#3, SpellEntry/IsFitToFamily, SpellEntry/IsSealSpell, Unit.Main/ApplyDiminishingAura, Unit.Main/GetSpellAuraHolderMap, Unit.Main/ModifyAuraState, Unit.Main/UpdateAuraForGroup, WorldObject.Object/RemoveFlag | Unit.Main/RemoveSpellAuraHolder | — |
| CleanupTriggeredSpells | method | SpellEntry/GetDuration, SpellMgr/GetSpellEntry, SpellMgr/Instance, Unit.Main/RemoveAurasDueToSpell | — | — |
| ModStackAmount | method | — | spell_mage/OnProc#2, Unit.Main/AddSpellAuraHolder, Unit.Main/RemoveAuraHolderFromStack | — |
| SetStackAmount | method | Aura/GetBasePoints, Aura/GetModifier, AuraScript/OnAuraValueCalculate, SpellAuraHolder/GetSpellProto, SpellAuraHolder/GetTarget, SpellCaster/CalculateSpellEffectValue | boss_thaddius/OnPeriodicTrigger, boss_thaddius/OnPeriodicTrigger#2, instance_zulgurub/UpdateHakkarPowerStacks, spell_mage/OnProc#2 | — |
| GetId | method | — | ChatHandler.UnitCommands/HandleListAurasCommand, Pet.Main/_SaveAuras, Player.Main/DuelComplete, Player.Main/RemoveItemDependentAurasAndCasts, Player.Main/SaveAura, Player.Main/UpdateMirrorTimers, Spell.Effects/EffectDispel, SpellCaster/SelectMagnetTarget, Unit.Main/AddSpellAuraHolder, Unit.Main/CleanupDeletedAuras, Unit.Main/HandleTriggers, Unit.Main/ProcDamageAndSpellFor, Unit.Main/RemoveNoStackAurasDueToAuraHolder, Unit.Main/RemoveSpellAuraHolder | — |
| GetCaster | method | Object/GetObjectGuid, ObjectAccessor/GetUnit, ObjectGuid/operator==, SpellAuraHolder/GetCasterGuid | Spell.Effects/EffectDispel, Unit.Main/AddSpellAuraHolder | — |
| GetRealCaster | method | Map.Main/GetGameObject, ObjectAccessor/GetUnit, ObjectGuid/IsGameObject, ObjectGuid/IsUnit, ObjectGuid/operator==, SpellAuraHolder/GetCasterGuid, SpellAuraHolder/GetRealCasterGuid, WorldObject.Object/FindMap | Unit.Main/RemoveSpellAuraHolder | — |
| IsWeaponBuffCoexistableWith | method | game_Objects_Item/GetSlot, game_Objects_Item/IsEquipped, Object/GetObjectGuid, Object/GetTypeId, ObjectGuid/operator!, ObjectGuid/operator!=, Player.Main/GetItemByGuid, SpellAuraHolder/GetCasterGuid, SpellAuraHolder/GetCastItemGuid, SpellAuraHolder/GetSpellProto | Unit.Main/AddSpellAuraHolder | — |
| IsNeedVisibleSlot | method | Aura/GetModifier, Creature.Main/IsTotem, Object/GetTypeId, SpellEntry/IsCustomSpell | — | — |
| HandleSpellSpecificBoosts | method | SpellAuraHolder/GetCasterGuid, SpellAuraHolder/GetSpellProto, SpellAuraHolder/IsDeleted, SpellAuraHolder/SetInUse, SpellCaster/CastSpell#2, Unit.Main/GetAura#2, Unit.Main/RemoveAura, Unit.Main/RemoveAurasByCasterSpell | Unit.Main/AddSpellAuraHolder, Unit.Main/RemoveSpellAuraHolder | — |
| HandleCastOnAuraRemoval | method | SpellAuraHolder/GetRemoveMode, SpellAuraHolder/GetTarget, SpellCaster/CastSpell#2 | Unit.Main/RemoveSpellAuraHolder | — |
| HandleAuraSafeFall | method | — | — | — |
| ~SpellAuraHolder | dtor | — | — | — |
| Update#4 | method | Aura/UpdateAura, Object/GetObjectGuid, Object/ToPlayer, ObjectGuid/operator!=, ObjectGuid/operator==, shared_Util/urand, Spell.Main/SendCastResult#2, SpellAuraHolder/GetCasterGuid, SpellAuraHolder/GetSpellProto, SpellAuraHolder/GetTarget, SpellCaster/InterruptSpell, SpellEntry/GetDiminishingRate, SpellEntry/GetSpellMaxRange, SpellEntry/HasAttribute#3, SpellEntry/HasAttribute#4, Unit.Main/GetChannelObjectGuid, Unit.Main/GetHealth, Unit.Main/GetLevel, Unit.Main/GetPower, Unit.Main/GetSpellModOwner, Unit.Main/GetTargetGuid, Unit.Main/IsHostileTo, Unit.Main/IsVisibleForOrDetect, Unit.Main/ModifyHealth, Unit.Main/ModifyPower, Unit.Main/RemoveAurasByCasterSpell, Unit.Main/RemoveAurasDueToSpell, Unit.Main/RemoveSpellAuraHolder, Unit.Main/SetInCombatWith, WorldObject.Object/GetCombatDistance | — | — |
| RefreshHolder | method | SpellAuraHolder/GetAuraMaxDuration, SpellAuraHolder/SetAuraDuration | ChatHandler.AccountCommands/HandleMuteCommand, Unit.Main/DealMeleeDamage | — |
| RefreshAuraPeriodicTimers | method | Aura/IsPeriodic, SpellAuraHolder/GetAuraByEffectIndex | Unit.Main/DelaySpellAuraHolder | — |
| SetAuraMaxDuration | method | SpellAuraHolder/GetSpellProto, SpellAuraHolder/IsPassive, SpellAuraHolder/SetPermanent | boss_vaelastrasz/UpdateAI, ChatHandler.AccountCommands/HandleMuteCommand, Spell.Main/DoSpellHitOnUnit | — |
| HasAuraType | method | Aura/GetModifier | — | — |
| HasMechanic | method | — | Spell.Effects/EffectDispelMechanic | — |
| HasMechanicMask | method | — | Unit.Main/RemoveAurasAtMechanicImmunity | — |
| IsPersistent | method | Aura/IsPersistent | Unit.Main/AddSpellAuraHolder | — |
| IsAreaAura | method | Aura/IsAreaAura | Unit.Main/AddSpellAuraHolder | — |
| IsPositive | method | Aura/IsPositive | CombatBotBaseAI/IsValidDispelTarget, Creature.Main/RemoveAurasAtReset, Player.Main/DuelComplete, Spell.Effects/EffectDispel, Spell.Main/CheckCast, ThreatListCopier.battleground_alterac/EnterEvadeMode, Unit.Main/AddSpellAuraHolder, Unit.Main/GetVisibleAurasCount, Unit.Main/RemoveAllNegativeAuras, Unit.Main/RemoveAuraDueToVisibleSlotLimit, Unit.Main/RemoveAurasAtMechanicImmunity | — |
| IsEmptyHolder | method | — | Pet.Main/_LoadAuras, Player.Main/LoadAura, Spell.Main/DoSpellHitOnUnit | — |
| UnregisterSingleCastHolder | method | SpellAuraHolder/GetSpellProto, SpellAuraHolder/IsSingleTarget, Unit.Main/GetSingleCastSpellTargets | Unit.Main/RemoveSpellAuraHolder | — |
| SetAura | method | WorldObject.Object/SetUInt32Value | — | — |
| SetAuraFlag | method | Object/GetUInt32Value, SpellAuraHolder/GetAuraByEffectIndex, SpellEntry/HasAttribute, WorldObject.Object/SetUInt32Value | — | — |
| SetAuraLevel | method | Object/GetUInt32Value, WorldObject.Object/SetUInt32Value | — | — |
| UpdateAuraApplication | method | Object/GetUInt32Value, WorldObject.Object/SetUInt32Value | — | — |
| UpdateAuraDuration | method | ByteBuffer/operator<<#10, ByteBuffer/operator<<#7, Object/GetTypeId, Player.Main/SendDirectMessage, SpellAuraHolder/GetAuraDuration, SpellAuraHolder/GetAuraSlot, WorldPacket/WorldPacket#4 | Map.Main/ExistingPlayerLogin, Player.Main/ResurrectPlayer, Unit.Main/DelaySpellAuraHolder, Unit.Main/RefreshAura | — |
| SetAffectedByVisibleSlotLimit | method | — | — | — |
| CalculateForBuffLimit | method | Object/GetObjectGuid, ObjectGuid/operator!=, SpellAuraHolder/GetCasterGuid, SpellAuraHolder/GetCastItemGuid, SpellAuraHolder/GetTarget, SpellAuraHolder/IsPermanent | — | — |
| CalculateForDebuffLimit | method | SpellAuraHolder/IsTriggered, SpellMgr/GetFirstSpellInChain, SpellMgr/Instance | — | — |
| CalculatePeriodic | method | Aura/GetId, Aura/GetSpellProto | — | — |
| CalculateHeartBeat | method | Object/GetTypeId, shared_Util/rand_norm_f, SpellCaster/MagicSpellHitChance, SpellEntry/HasAttribute, SpellEntry/IsPvEHeartBeat, Unit.Main/IsCharmerOrOwnerPlayerOrPlayerItself | — | — |
| HandleInterruptRegen | method | Aura/GetTarget, Unit.Main/SetInCombatState | — | — |
| HandleAuraAuraSpell | method | Aura/GetSpellProto, Aura/GetTarget, Unit.Main/AddAura, Unit.Main/RemoveAurasDueToSpell | — | — |
| _IsExclusiveSpellAura | function | — | — | — |
| ComputeExclusive | method | Aura/GetEffIndex, Aura/GetHolder, Aura/GetModifier, Aura/GetSpellProto, SpellAuraHolder/IsPassive | — | — |
| CheckExclusiveWith | method | Aura/GetEffIndex, Aura/GetModifier#2, Aura/GetSpellProto, Aura/IsExclusive, Errors/PrintStacktraceAndThrow | Unit.Main/GetMostImportantAuraAfter | — |
| ExclusiveAuraCanApply | method | Aura/GetId, Aura/GetTarget, Aura/IsApplied, Aura/IsInUse, Errors/PrintStacktraceAndThrow, Log.Main/Out, Unit.Main/GetMostImportantAuraAfter, WorldObject.Object/GetMapId, WorldObject.Object/GetName | — | — |
| ExclusiveAuraUnapply | method | Aura/GetId, Aura/GetTarget, Aura/IsApplied, Aura/IsInUse, Errors/PrintStacktraceAndThrow, Log.Main/Out, Unit.Main/GetMostImportantAuraAfter, WorldObject.Object/GetMapId, WorldObject.Object/GetName | — | — |

---

<!-- verify: failed-members | invented: operator -->

---

<!-- verify: boundary-bleed | foreign: attack, Unit, Update -->
