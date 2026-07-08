<!-- provenance: failed-members -->
# Spell.Main

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Spell.Main

## Purpose & Responsibilities

The `Spell` class, defined in `Spell.h` and implemented in `Spell.cpp`, is the central runtime entity representing a single instance of a spell cast in the MaNGOS/Nostalrius emulator. It manages the entire lifecycle of a spell from initialization through casting, effect resolution, and cleanup.

Key responsibilities include:
1.  **Lifecycle Management:** Handling states (`PREPARING`, `CASTING`, `DELAYED`, `FINISHED`) via `prepare()`, `cast()`, `update()`, and `finish()`.
2.  **Target Resolution:** Determining valid targets for each spell effect using complex logic in `FillTargetMap()` and `SetTargetMap()`, supporting units, game objects, items, corpses, and locations.
3.  **Validation:** Checking cast requirements such as range, power costs, cooldowns, line-of-sight, and target eligibility via `CheckCast()`, `CheckRange()`, `CheckPower()`, and `CheckItems()`.
4.  **Effect Execution:** Dispatching specific spell effects (damage, healing, auras, summons, etc.) to their respective handlers via `HandleEffects()` and `DoAllEffectOnTarget()`.
5.  **Network Communication:** Sending spell-related packets to clients (start, go, miss, log, channel updates) to synchronize visual and gameplay state.
6.  **Trigger System Integration:** Preparing data for proc/triggers (`prepareDataForTriggerSystem()`) and handling triggered spells.

This unit does not directly interact with database tables; it operates entirely on in-memory objects and DBC data loaded by other managers.

## Member-by-Member Behavior

### Construction and Initialization
*   **`Spell#2` (ctor)** and **`Spell` (ctor)**: Initialize the spell instance with a caster (`Unit` or `GameObject`), spell definition (`SpellEntry`), and context (triggered status, original caster GUID). They set up internal state like attack type, school mask, auto-repeat/channeling flags, and reflection capability. They also load associated `SpellScript`s if defined.
*   **`~Spell` (dtor)**: Cleans up the `SpellScript` pointer and marks the spell as destroyed.

### Lifecycle Control
*   **`prepare`**: Validates the spell cast, calculates power cost and cast time, sets up timers, removes conflicting auras (stealth/invisibility), and transitions the spell to the `PREPARING` state. It schedules the spell for execution via `SpellEvent`.
*   **`cast`**: Executes the spell immediately if ready. It performs final checks, consumes resources (power, reagents, ammo), resolves targets (`FillTargetMap`), and dispatches effects. For delayed spells, it sets up the delay state.
*   **`update`**: Called periodically by the event system. It handles cast time countdowns, channeling updates, movement checks (interrupting if caster moves too far), and processes delayed spell effects as they arrive.
*   **`finish`**: Marks the spell as finished, cleans up state (combo points, pet commands), and triggers post-cast events like ritual completion or AI attacks.
*   **`cancel`**: Interrupts the spell, sends interruption packets, resets GCDs, and cleans up partial effects.

### Target Resolution
*   **`FillTargetMap`**: Iterates through all spell effects and populates the target lists (`m_UniqueTargetInfo`, `m_UniqueGOTargetInfo`, `m_UniqueItemInfo`) based on implicit target types defined in the spell data. It handles complex cases like chain healing, area-of-effect radii, and script-defined targets.
*   **`SetTargetMap`**: A helper called by `FillTargetMap` to resolve targets for a specific effect index and target mode. It uses grid searches (`Cell::Visit...`) to find units/game objects within range/cone/radius.
*   **`CheckScriptTargeting`**: Resolves targets defined by database scripts (`spell_script_target`), searching for specific creature/game object entries within range.
*   **`AddUnitTarget`**, **`AddGOTarget`**, **`AddItemTarget`**: Add specific targets to the respective lists, calculating hit results, delays, and reflection possibilities.
*   **`CheckTarget`**: Validates if a specific unit is a valid target for an effect, considering creature type masks, selection flags, and GM invisibility.

### Validation and Checks
*   **`CheckCast`**: Comprehensive validation of cast conditions: standing, cooldowns, death state, GCD, combat status, shapeshift forms, stealth, indoor/outdoor restrictions, caster auras, target validity, range, power, and items.
*   **`CheckPetCast`**: Specific validation for pet casts, ensuring the pet is alive, not casting another spell, and has a valid target.
*   **`CheckRange`**: Verifies the caster is within the spell's minimum and maximum range of the target or location.
*   **`CheckPower`**: Ensures the caster has sufficient power (mana, rage, energy, etc.) to cast the spell.
*   **`CheckItems`**: Validates equipped weapons, held items, reagents, and focus objects required by the spell.
*   **`CheckCasterAuras`**: Checks if the caster is silenced, stunned, feared, or pacified, and if the spell grants immunity to these states.
*   **`ValidateExplicitTargetMask`**: Anti-cheat measure verifying that the target mask sent by the client matches what the spell expects.

### Effect Execution
*   **`HandleEffects`**: Dispatches a specific effect index to the corresponding function pointer in the `SpellEffects` array.
*   **`DoAllEffectOnTarget`**: Applies all effects to a specific target (Unit, GameObject, or Item). It handles damage/healing calculations, critical hits, procs, threat, and aura application.
*   **`DoSpellHitOnUnit`**: Specifically handles spell hits on units, including diminishing returns, aura creation, and combat state updates.
*   **`HandleDelayedSpellLaunch`**: Prepares damage calculations for delayed spells before they hit.

### Resource Consumption
*   **`TakePower`**: Deducts the calculated power cost from the caster.
*   **`TakeReagents`**: Removes required reagent items from the caster's inventory.
*   **`TakeCastItem`**: Consumes the item used to cast the spell (charges or destruction).
*   **`TakeAmmo`**: Consumes ammo for ranged attacks.

### Network Communication
*   **`SendSpellStart`**: Sends `SMSG_SPELL_START` to indicate casting has begun.
*   **`SendSpellGo`**: Sends `SMSG_SPELL_GO` to indicate the spell has been released.
*   **`SendCastResult`**: Sends `SMSG_CAST_RESULT` to report success or failure reasons.
*   **`SendInterrupted`**: Sends `SMSG_SPELL_FAILED_OTHER` to indicate interruption.
*   **`SendAllTargetsMiss`**: Sends `SMSG_SPELLLOGMISS` if all targets missed.
*   **`SendLogExecute`**: Sends `SMSG_SPELLLOGEXECUTE` for combat log entries.
*   **`SendChannelStart`** / **`SendChannelUpdate`**: Manages channeling spell visuals and timers.
*   **`SendResurrectRequest`**: Sends `SMSG_RESURRECT_REQUEST` for resurrection spells.

### Utility and State Accessors
*   **`getState`** / **`setState`**: Accessors for the spell's current state.
*   **`GetCaster`** / **`GetAffectiveCaster`** / **`GetCastingObject`**: Retrieve different aspects of the caster (formal, effective for damage calculation, visual).
*   **`IsChanneled`** / **`IsAutoRepeat`**: Check spell properties.
*   **`CalculateDamage`**: Computes raw damage/healing values for an effect.
*   **`CalculatePowerCost`**: Static helper to compute power cost based on spell and caster stats.
*   **`UpdatePointers`**: Refreshes pointers to caster and targets after delays to prevent dangling references.
*   **`Delete`**: Safely deletes the spell instance if it's not currently referenced.

## Cross-Unit Boundaries

*   **Called by `SpellCaster/CastSpell`**: The primary entry point for initiating a spell cast. `SpellCaster` creates the `Spell` object and calls `prepare()`.
*   **Calls `SpellCaster/CalculateSpellDamage`**: During effect execution, `Spell` delegates damage calculation to the caster to incorporate caster-specific modifiers.
*   **Calls `Unit.Main/AttackerStateUpdate`**: Updates combat state and threat when a spell hits a target.
*   **Calls `SpellScript/OnInit`, `OnCheckCast`, `OnHit`, etc.`**: Integrates with the scripting system to allow custom behavior for specific spells.
*   **Calls `ObjectAccessor/GetUnit`**: Resolves target GUIDs to actual `Unit` objects during target resolution and effect execution.
*   **Calls `Map.Main/GetGameObject`**: Resolves target GUIDs to `GameObject` objects.
*   **Calls `Player.Main/DestroyItemCount`**: Consumes items and reagents.
*   **Calls `WorldSession.Main/SendPacket`**: Sends network packets to clients.
*   **Calls `SpellMgr/GetSpellEntry`**: Retrieves spell definition data.
*   **Calls `ScriptMgr/GetSpellScript`**: Loads custom scripts for spells.

## Data Model

This unit does not directly interact with database tables. It relies on in-memory data structures populated by other managers (e.g., `SpellMgr` loads `SpellEntry` from DBC files).

## Notable Implementation Details

*   **Delayed Spells**: Spells with flight time (`m_spellInfo->speed > 0`) are handled specially. `handle_delayed()` processes effects as they arrive at targets, recalculating hit results and immunities at the moment of impact.
*   **Channeled Spells**: Maintained via `m_channeledHolders` list. `update()` iterates through these holders to apply periodic effects and check for interruptions.
*   **Trigger System**: `prepareDataForTriggerSystem()` sets up flags (`m_procAttacker`, `m_procVictim`) that determine what procs can be triggered by the spell. This is crucial for correct equipment and talent interactions.
*   **Target Copying**: `FillTargetMap()` copies targets between effects if they share the same implicit target type, optimizing performance and ensuring consistency for multi-effect spells.
*   **Anti-Cheat**: `ValidateExplicitTargetMask()` checks client-provided target masks against expected values to prevent cheating.
*   **Memory Safety**: `UpdatePointers()` is critical for delayed spells to ensure pointers to units/game objects remain valid after time delays. `Delete()` checks `IsDeletable()` to prevent use-after-free errors.
*   **Stealth Removal**: `ShouldRemoveStealthAuras()` determines if casting a spell breaks stealth, with special handling for Sap and other abilities.
*   **Diminishing Returns**: Handled in `DoSpellHitOnUnit()` by tracking diminish groups and levels, affecting aura durations.

## Member Reference

**Spell#2** (ctor): Initializes spell with Unit caster.
**Spell** (ctor): Initializes spell with GameObject caster.
**~Spell** (dtor): Cleans up SpellScript.
**FillTargetMap**: Populates target lists for all effects.
**isSuccessCast**: Returns success flag.
**CalculateDamage**: Computes effect damage/healing.
**getState**: Returns current spell state.
**setState**: Sets current spell state.
**SetCastTime**: Sets cast time.
**GetCastTime**: Gets cast time.
**GetCastedTime**: Gets elapsed cast time.
**IsChanneled**: Checks if channeled.
**IsAutoRepeat**: Checks if auto-repeat.
**SetAutoRepeat**: Sets auto-repeat flag.
**ReSetTimer**: Resets cast timer.
**IsChannelActive**: Checks if channeling is active.
**IsMeleeAttackResetSpell**: Checks if spell resets melee timers.
**IsRangedAttackResetSpell**: Checks if spell resets ranged timers.
**IsDeletable**: Checks if spell can be deleted.
**SetReferencedFromCurrent**: Marks spell as referenced.
**SetExecutedCurrently**: Marks spell as executing.
**GetDelayStart**: Gets delay start time.
**SetDelayStart**: Sets delay start time.
**GetDelayMoment**: Gets next delay moment.
**GetCaster**: Gets formal caster.
**GetAffectiveCaster**: Gets effective caster for auras.
**GetOriginalCasterGuid**: Gets original caster GUID.
**GetPowerCost**: Gets calculated power cost.
**IsTriggered**: Checks if triggered.
**IsTriggeredByAura**: Checks if triggered by aura.
**IsCastByItem**: Checks if cast by item.
**SetCastItem**: Sets cast item.
**GetTargetNum**: Gets current target number.
**SetChannelingVisual**: Sets channeling visual flag.
**IsChannelingVisual**: Checks channeling visual flag.
**GetAbsorbedDamage**: Gets absorbed damage.
**GetNextDelayAtDamageMsTime**: Gets next delay time.
**GetUnitTarget**: Gets current unit target.
**GetItemTarget**: Gets current item target.
**GetCorpseTarget**: Gets current corpse target.
**GetGOTarget**: Gets current GO target.
**CheckScriptTargeting**: Resolves script-defined targets.
**AddExecuteLogInfo**: Adds info for combat log.
**prepareDataForTriggerSystem**: Sets up trigger flags.
**CleanupTargetList**: Clears target lists.
**GetSpellBatchingEffectDelay**: Gets batching delay.
**AddUnitTarget#2**: Adds unit target by GUID.
**AddUnitTarget**: Adds unit target.
**CheckAtDelay**: Checks immunity at delay.
**AddGOTarget#2**: Adds GO target by GUID.
**AddGOTarget**: Adds GO target.
**AddItemTarget**: Adds item target.
**DoAllEffectOnTarget#3**: Applies effects to Unit target.
**DoSpellHitOnUnit**: Handles spell hit on unit.
**DoAllEffectOnTarget**: Applies effects to GO target.
**DoAllEffectOnTarget#2**: Applies effects to Item target.
**HandleDelayedSpellLaunch**: Prepares delayed spell damage.
**InitializeDamageMultipliers**: Sets up damage multipliers.
**HasValidUnitPresentInTargetList**: Checks for valid unit targets.
**ChainHealingOrder** (ctor): Functor for chain healing sort.
**operator()#2**: Compares units for chain healing.
**ChainHealingHash**: Hashes unit for chain healing priority.
**ChainHealingFullHealth** (ctor): Functor for full health check.
**operator()**: Checks if unit is full health.
**TargetDistanceOrderNear** (ctor): Functor for distance sort.
**operator()#3**: Compares distances.
**SetTargetMap**: Resolves targets for an effect.
**IsAcceptableAutorepeatError**: Checks if error is acceptable for auto-repeat.
**UpdateCastStartPosition**: Updates caster position.
**prepare**: Prepares spell for casting.
**prepare#2**: Prepares spell with aura trigger.
**cancel**: Cancels spell.
**cast**: Executes spell.
**handle_immediate**: Handles immediate spell effects.
**handle_delayed**: Handles delayed spell effects.
**_handle_immediate_phase**: Processes immediate phase.
**_handle_finish_phase**: Processes finish phase.
**SendSpellCooldown**: Sends cooldown packet.
**update**: Updates spell state.
**HandleAddTargetTriggerAuras**: Handles target trigger auras.
**finish**: Finishes spell.
**SendCastResult**: Sends cast result packet.
**SendCastResult#2**: Sends cast result to player.
**WriteGuidHelper**: Writes GUID to packet.
**SendSpellStart**: Sends spell start packet.
**SendSpellGo**: Sends spell go packet.
**WriteAmmoToPacket**: Writes ammo info to packet.
**WriteSpellGoTargets**: Writes targets to packet.
**SendLogExecute**: Sends combat log packet.
**SendInterrupted**: Sends interruption packet.
**SendAllTargetsMiss**: Sends all miss packet.
**SendChannelUpdate**: Sends channel update packet.
**SendChannelStart**: Sends channel start packet.
**InitializeChanneledVisualTimer**: Initializes visual timer.
**SendResurrectRequest**: Sends resurrection request.
**TakeCastItem**: Consumes cast item.
**TakePower**: Consumes power.
**TakeReagents**: Consumes reagents.
**TakeAmmo**: Consumes ammo.
**HandleThreatSpells**: Handles threat generation.
**HandleEffects**: Dispatches effect handler.
**AddChanneledAuraHolder**: Adds holder to channeled list.
**RemoveChanneledAuraHolder**: Removes holder from channeled list.
**CheckCast**: Validates cast conditions.
**CheckPetCast**: Validates pet cast.
**CheckCasterAuras**: Checks caster auras.
**ValidateExplicitTargetMask**: Validates target mask.
**CanAutoCast**: Checks if spell can auto-cast.
**CheckTamingSpell**: Checks taming conditions.
**CheckRange**: Checks range.
**CalculatePowerCost**: Calculates power cost.
**CheckPower**: Checks power availability.
**IgnoreItemRequirements**: Checks if item reqs ignored.
**CheckItems**: Checks item requirements.
**Delayed**: Handles cast time pushback.
**DelayedChannel**: Handles channel pushback.
**UpdateOriginalCasterPointer**: Updates original caster pointer.
**UpdatePointers**: Updates all pointers.
**CheckTargetCreatureType**: Checks creature type.
**GetCurrentContainer**: Gets spell container type.
**CheckTarget**: Validates target.
**IsNeedSendToClient**: Checks if client notification needed.
**IsTriggeredSpellWithRedundentData**: Checks for redundant data.
**HaveTargetsForEffect**: Checks for targets for effect.
**SpellEvent** (ctor): Creates spell event.
**~SpellEvent** (dtor): Cleans up spell event.
**Execute#2**: Executes spell event.
**Abort#2**: Aborts spell event.
**IsDeletable#2**: Checks if event deletable.
**CanOpenLock**: Checks if lock can be opened.
**GetCenterX**: Gets center X coordinate.
**GetCenterY**: Gets center Y coordinate.
**SpellNotifierCreatureAndPlayer** (ctor): Creates notifier.
**Visit#2**: Visits grid for notifier.
**Visit#4**: Visits grid for notifier.
**Visit#3**: Visits grid for notifier.
**Visit**: Visits grid for notifier.
**FillAreaTargets**: Fills area targets.
**FillRaidOrPartyTargets**: Fills raid/party targets.
**GetAffectiveCasterObject**: Gets affective caster object.
**GetCastingObject**: Gets casting object.
**ClearCastItem**: Clears cast item.
**ResetEffectDamageAndHeal**: Resets damage/heal counters.
**SetClientStarted**: Sets client started flag.
**OnSpellLaunch**: Handles spell launch.
**HasModifierApplied**: Checks if modifier applied.
**IsTriggeredByProc**: Checks if triggered by proc.
**ShouldRemoveStealthAuras**: Checks if stealth removed.
**Delete**: Deletes spell.
**Execute**: Executes channel reset event.
**Abort**: Aborts channel reset event.

---

<!-- machine-true, projected from graph.json -->

## Map — Spell.Main

*Source:* Spell.cpp, Spell.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| Spell#2 | ctor | Errors/PrintStacktraceAndThrow, Object/GetObjectGuid, Object/IsPlayer, ObjectGuid/ObjectGuid, ScriptMgr/GetSpellScript, SpellDefines/GetSchoolMask, SpellEntry/CalculateSimpleValue, SpellEntry/GetSpellSchoolMask, SpellEntry/GetWeaponAttackType, SpellEntry/IsAutoRepeatRangedSpell, SpellEntry/IsChanneledSpell, SpellEntry/IsReflectableSpell, SpellEntry/IsReflectableSpell#2, SpellMgr/GetSpellEntry, SpellMgr/Instance, SpellScript/OnInit, Unit.Main/GetClassMask, Unit.Main/GetWeaponDamageSchool | CombatBotBaseAI/CastWeaponBuff, Creature.Main/TryToCast, GameObject/AddUniqueUse, GameObject/Use, PetAI/UpdateAI, Player.Main/CastItemUseSpell, Player.Main/DismountCheck, SpellCaster/CastCustomSpell, SpellCaster/CastSpell, SpellCaster/CastSpell#3, spell_priest/OnSuccessfulFinish, spell_special/OnSuccessfulStart, Unit.Main/_UpdateAutoRepeatSpell, WorldSession.NPCHandler/HandleTrainerBuySpellOpcode, WorldSession.PetHandler/HandlePetCastSpellOpcode, WorldSession.SpellHandler/HandleCastSpellOpcode, WorldSession.TradeHandler/HandleAcceptTradeOpcode | — |
| Spell | ctor | Errors/PrintStacktraceAndThrow, Object/GetObjectGuid, ObjectGuid/ObjectGuid, ScriptMgr/GetSpellScript, SpellEntry/CalculateSimpleValue, SpellEntry/GetSpellSchoolMask, SpellEntry/GetWeaponAttackType, SpellEntry/IsAutoRepeatRangedSpell, SpellEntry/IsChanneledSpell, SpellEntry/IsReflectableSpell, SpellEntry/IsReflectableSpell#2, SpellMgr/GetSpellEntry, SpellMgr/Instance, SpellScript/OnInit | GameObject/Use, SpellCaster/CastCustomSpell, SpellCaster/CastSpell, SpellCaster/CastSpell#3 | — |
| ~Spell | dtor | — | — | — |
| FillTargetMap | method | Map.Main/GetUnit, ObjectGuid/operator==, SpellCaster/SelectMagnetTarget, SpellCastTargetsInfo/getUnitTarget, SpellCastTargetsInfo/getUnitTargetGuid, SpellCastTargetsInfo/setDestination, SpellEntry/HasAttribute#3, SpellEntry/HasAttribute#4, SpellEntry/IsAreaAuraEffect, WorldObject.Object/GetMap, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/IsValidAttackTarget | — | — |
| isSuccessCast | method | — | Unit.Main/AttackerStateUpdate | — |
| CalculateDamage | method | — | Spell.Effects/EffectWeaponDmg | — |
| getState | method | — | GameObject/Update, PartyBotAI/UpdateAI, Player.Main/InterruptSpellsWithCastItem, Player.Main/RemoveItemDependentAurasAndCasts, Spell.Effects/EffectInterruptCast, SpellCaster/InterruptSpell, SpellCaster/InterruptSpellsWithChannelFlags, SpellCaster/InterruptSpellsWithInterruptFlags, SpellCaster/IsNoMovementSpellCasted, SpellCaster/IsNonMeleeSpellCasted, SpellCaster/MoveChannelledSpellWithCastTime, Unit.Main/DealDamage, Unit.Main/InterruptSpellsCastedOnMe, Unit.Main/SetInCombatState, Unit.Main/_UpdateSpells | — |
| setState | method | — | — | — |
| SetCastTime | method | — | spell_item/OnSuccessfulStart | — |
| GetCastTime | method | — | Spell.Effects/EffectInterruptCast | — |
| GetCastedTime | method | — | Creature.Main/SendAreaSpiritHealerQueryOpcode, SpellCaster/InterruptSpellsWithInterruptFlags, Unit.Main/InterruptSpellsCastedOnMe, Unit.Main/SetInCombatState | — |
| IsChanneled | method | — | SpellCaster/InterruptSpellsWithInterruptFlags | — |
| IsAutoRepeat | method | — | SpellCaster/InterruptSpellsWithInterruptFlags, SpellEntry/GetCastTime | — |
| SetAutoRepeat | method | — | — | — |
| ReSetTimer | method | — | spell_item/OnSuccessfulStart | — |
| IsChannelActive | method | — | — | — |
| IsMeleeAttackResetSpell | method | — | — | — |
| IsRangedAttackResetSpell | method | — | — | — |
| IsDeletable | method | — | — | — |
| SetReferencedFromCurrent | method | — | GameObject/Update, GameObject/~GameObject, SpellCaster/InterruptSpell, SpellCaster/SetCurrentCastedSpell, Unit.Main/_UpdateSpells, Unit.Main/~Unit | — |
| SetExecutedCurrently | method | — | — | — |
| GetDelayStart | method | — | — | — |
| SetDelayStart | method | — | — | — |
| GetDelayMoment | method | — | — | — |
| GetCaster | method | — | SpellEntry/GetCastTime, spell_item/OnEffectExecute#11, spell_item/OnEffectExecute#2, spell_item/OnEffectExecute#3, spell_item/OnEffectExecute#6, spell_special/OnInit | — |
| GetAffectiveCaster | method | — | Spell.Effects/EffectApplyAura, Spell.Effects/EffectDummy, Spell.Effects/EffectTameCreature, spell_item/OnCheckCast | — |
| GetOriginalCasterGuid | method | — | spell_priest/OnSuccessfulFinish | — |
| GetPowerCost | method | — | — | — |
| IsTriggered | method | — | SpellCaster/InterruptSpellsWithInterruptFlags, SpellCaster/ProcSystemArguments, SpellEntry/GetCastTime, spell_item/OnCheckCast#7, WorldSession.SpellHandler/HandleCancelChanneling | — |
| IsTriggeredByAura | method | — | SpellCaster/ProcSystemArguments | — |
| IsCastByItem | method | — | SpellCaster/ProcSystemArguments, SpellEntry/GetCastTime | — |
| SetCastItem | method | — | Creature.Main/TryToCast, Player.Main/CastItemUseSpell, SpellCaster/CastCustomSpell, SpellCaster/CastSpell, SpellCaster/CastSpell#3, WorldSession.TradeHandler/HandleAcceptTradeOpcode | — |
| GetTargetNum | method | — | SpellEntry/CalculateCustomCoefficient | — |
| SetChannelingVisual | method | — | GameObject/AddUniqueUse, Spell.Effects/EffectTransmitted | — |
| IsChannelingVisual | method | — | — | — |
| GetAbsorbedDamage | method | — | — | — |
| GetNextDelayAtDamageMsTime | method | — | — | — |
| GetUnitTarget | method | — | boss_gluth/OnEffectExecute, boss_grobbulus/OnEffectExecute, boss_loatheb/OnEffectExecute, boss_maexxna/OnEffectExecute, boss_nefarian/OnEffectExecute, boss_nefarian/OnEffectExecute#2, boss_nefarian/OnEffectExecute#4, boss_thaddius/OnEffectExecute, boss_thaddius/OnEffectExecute#2, boss_thaddius/OnEffectExecute#3, instance_naxxramas.boss_kelthuzad/OnEffectExecute, ruins_of_ahnqiraj/OnEffectExecute, spell_druid/OnEffectExecute, spell_druid/OnEffectExecute#2, spell_druid/OnEffectExecute#3, spell_item/OnAfterHit, spell_item/OnEffectExecute, spell_item/OnEffectExecute#10, spell_paladin/OnAfterHit, spell_paladin/OnEffectExecute, spell_paladin/OnEffectExecute#2, spell_paladin/OnEffectExecute#3, spell_paladin/OnEffectExecute#4, spell_paladin/OnEffectExecute#7, spell_priest/OnEffectExecute, spell_priest/OnHit, spell_rogue/OnEffectExecute, spell_rogue/OnEffectExecute#2, spell_shaman/OnEffectExecute, spell_special/OnEffectExecute#4, spell_special/OnSuccessfulFinish, spell_warlock/OnEffectExecute, spell_warlock/OnEffectExecute#2, spell_warlock/OnEffectExecute#4, spell_warrior/OnCast, spell_warrior/OnEffectExecute, spell_warrior/OnEffectExecute#2, spell_warrior/OnEffectExecute#5, ungoro_crater/OnEffectExecute | — |
| GetItemTarget | method | — | — | — |
| GetCorpseTarget | method | — | spell_special/OnSuccessfulFinish | — |
| GetGOTarget | method | — | instance_blackrock_spire/OnEffectExecute | — |
| CheckScriptTargeting | method | Creature.Main/IsCorpse, Log.Main/Out, NearestGameObjectEntryFitConditionInObjectRangeCheck/GetLastRange, NearestGameObjectEntryFitConditionInObjectRangeCheck/NearestGameObjectEntryFitConditionInObjectRangeCheck, NearestUnitFitConditionInCombatRangeCheck/GetLastRange, NearestUnitFitConditionInCombatRangeCheck/NearestUnitFitConditionInCombatRangeCheck, Object/GetEntry, Object/IsCreature, Object/IsPlayer, SpellCastTargetsInfo/getUnitTarget, SpellCastTargetsInfo/setDestination, SpellCastTargetsInfo/setUnitTarget, SpellMgr/GetSpellScriptTargetBounds, SpellMgr/Instance, SpellTargetEntry/CanNotHitWithSpellEffect, Unit.Main/IsAlive, WorldObject.Object/GetDistance#3, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/IsWithinDistInMap | — | — |
| AddExecuteLogInfo | method | — | Spell.Effects/EffectAddExtraAttacks, Spell.Effects/EffectCreateItem, Spell.Effects/EffectDismissPet, Spell.Effects/EffectDispel, Spell.Effects/EffectDispelMechanic, Spell.Effects/EffectDistract, Spell.Effects/EffectDurabilityDamage, Spell.Effects/EffectFeedPet, Spell.Effects/EffectInterruptCast, Spell.Effects/EffectModifyThreatPercent, Spell.Effects/EffectOpenLock, Spell.Effects/EffectResurrect, Spell.Effects/EffectResurrectNew, Spell.Effects/EffectSanctuary, Spell.Effects/EffectSkinPlayerCorpse, Spell.Effects/EffectSummon, Spell.Effects/EffectSummonCritter, Spell.Effects/EffectSummonDemon, Spell.Effects/EffectSummonGuardian, Spell.Effects/EffectSummonObject, Spell.Effects/EffectSummonObjectWild, Spell.Effects/EffectSummonPet, Spell.Effects/EffectSummonTotem, Spell.Effects/EffectSummonWild, Spell.Effects/EffectTaunt, Spell.Effects/EffectThreat, Spell.Effects/EffectTransmitted | — |
| prepareDataForTriggerSystem | method | ObjectGuid/IsGameObject, SpellEntry/HasAttribute#5, SpellEntry/IsAreaOfEffectSpell, SpellEntry/IsFitToFamilyMask, SpellEntry/IsHealSpell, SpellEntry/IsNextMeleeSwingSpell, SpellEntry/IsPositiveEffect, SpellEntry/IsPositiveSpell#4 | — | — |
| CleanupTargetList | method | — | — | — |
| GetSpellBatchingEffectDelay | method | Object/IsCreature, SpellEntry/HasAttribute#3, World/getConfig#4, World/GetDelayUntilNextSpellBatchingInterval | — | — |
| AddUnitTarget#2 | method | Object/GetObjectGuid, ObjectGuid/IsGameObject, ObjectGuid/operator==, SpellCaster/IsSpellCrit, SpellCaster/ProcDamageAndSpell, SpellCaster/ProcSystemArguments, SpellCaster/SpellHitResult, SpellEntry/CanCrit, SpellEntry/HasAttribute#3, SpellEntry/HasAttribute#5, SpellEntry/IsSummonEffect, Unit.Main/IsImmuneToSpellEffect, WorldObject.Object/GetDistance3dToCenter#3 | — | — |
| AddUnitTarget | method | Object/GetObjectGuid, ObjectAccessor/GetUnit, ObjectGuid/operator== | — | — |
| CheckAtDelay | method | Creature.Main/IsInEvadeMode, Object/GetObjectGuid, Object/IsCreature, ObjectAccessor/GetUnit, ObjectGuid/operator==, SpellEntry/GetSpellSchoolMask, SpellEntry/IsPositiveSpell#3, Unit.Main/IsImmuneToDamage, Unit.Main/IsImmuneToSpell, Unit.Main/IsImmuneToSpellEffect | — | — |
| AddGOTarget#2 | method | Object/GetObjectGuid, ObjectGuid/operator==, SpellScript/OnCheckTarget, WorldObject.Object/GetDistance3dToCenter#3 | — | — |
| AddGOTarget | method | Map.Main/GetGameObject, WorldObject.Object/GetMap | — | — |
| AddItemTarget | method | — | — | — |
| DoAllEffectOnTarget#3 | method | Creature.Main/AI, Creature.Main/IsPet, CreatureAI/SpellHit, CreatureAI/SpellHitTarget, Errors/PrintStacktraceAndThrow, HostileRefManager/threatAssist, Object/GetObjectGuid, Object/IsCreature, Object/IsPet, Object/IsPlayer, Object/ToCreature, ObjectAccessor/GetUnit, ObjectGuid/operator==, Player.Main/CastItemCombatSpell, Player.Main/RewardPlayerAndGroupAtCast, shared_Util/ditheru, shared_Util/getMSTime, SpellCaster/CalculateSpellDamage, SpellCaster/DealDamageMods, SpellCaster/DealHeal, SpellCaster/DealSpellDamage, SpellCaster/ProcDamageAndSpell, SpellCaster/ProcSystemArguments, SpellCaster/SendSpellNonMeleeDamageLog, SpellCaster/SpellCriticalHealingBonus, SpellDefines/GetFirstSchoolInMask, SpellEntry/CanTriggerWeaponProcs, SpellEntry/HasAttribute#3, SpellEntry/HasAttribute#5, SpellEntry/HasAura, SpellEntry/HasAuraInterruptFlag, SpellEntry/IsDirectDamageWithBonusEffect, SpellEntry/IsDispel, SpellEntry/IsEffectHandledOnDelayedSpellLaunch, SpellEntry/IsNextMeleeSwingSpell, SpellEntry/IsPositiveSpell#4, SpellEntry/IsSpellAppliesAura#2, SpellMgr/GetSpellEntry, SpellMgr/GetSpellThreatMultiplier, SpellMgr/Instance, SpellNonMeleeDamage/SpellNonMeleeDamage, SpellScript/OnAfterHit, SpellScript/OnHit, Unit.Main/AddThreat, Unit.Main/AttackedBy, Unit.Main/AttackStop, Unit.Main/CalculateAbsorbResistBlock, Unit.Main/CreateProcExtendMask, Unit.Main/GetAttackers, Unit.Main/GetCharmerOrOwnerPlayerOrPlayerItself, Unit.Main/GetClass, Unit.Main/GetHostileRefManager, Unit.Main/HasAuraPetShouldAvoidBreaking, Unit.Main/IsAlive, Unit.Main/ModifyPower, Unit.Main/RemoveSpellsCausingAura, Unit.Main/SetInCombatWithAggressor, Unit.Main/SetInCombatWithVictim, Unit.Main/SetOutOfCombatWithAggressor, Unit.Main/SetOutOfCombatWithVictim, Unit.Main/TriggerDamageShields, WorldObject.Object/GetZoneScript, WorldObject.Object/IsHostileTo, WorldObject.Object/IsVisibleForOrDetect, ZoneScript/OnCreatureSpellHit | — | — |
| DoSpellHitOnUnit | method | HostileRefManager/threatAssist, Object/GetObjectGuid, Object/IsCreature, Object/IsGameObject, Object/IsPlayer, ObjectGuid/operator!=, Player.Main/UpdatePvP, SpellAuraHolder/GetAuraMaxDuration, SpellAuraHolder/SetAddedBySpell, SpellAuraHolder/SetAuraDuration, SpellAuraHolder/setDiminishGroup, SpellAuraHolder/setDiminishLevel, SpellAuraHolder/SetReflected, SpellAuraHolder/SetTriggered, SpellCaster/GetCurrentSpell, SpellCaster/InterruptSpell, SpellCaster/SendSpellMiss, SpellCastTargetsInfo/getUnitTarget, SpellEntry/GetDiminishingReturnsGroup, SpellEntry/GetDiminishingReturnsGroupType, SpellEntry/GetFirstEffectIndexInMask, SpellEntry/GetSpellSchoolMask, SpellEntry/HasAttribute, SpellEntry/HasAttribute#3, SpellEntry/HasAttribute#4, SpellEntry/HasAttribute#5, SpellEntry/HasAura, SpellEntry/HasDirectThreatIncreaseEffect, SpellEntry/HasEffect, SpellEntry/IsFriendlyTarget, SpellEntry/IsPositiveSpell#3, SpellEntry/IsPositiveSpell#4, SpellEntry/IsSpellAppliesAura#2, Unit.Main/AddSpellAuraHolder, Unit.Main/AddThreat, Unit.Main/ApplyDiminishingToDuration, Unit.Main/AttackedBy, Unit.Main/GetCharmerOrOwnerOrOwnGuid, Unit.Main/GetCharmerOrOwnerPlayerOrPlayerItself, Unit.Main/GetDiminishing, Unit.Main/GetHostileRefManager, Unit.Main/GetSpellModOwner, Unit.Main/IncrDiminishing, Unit.Main/IsAlive, Unit.Main/IsCharmerOrOwnerPlayerOrPlayerItself, Unit.Main/IsDead, Unit.Main/IsEffectResist, Unit.Main/IsImmuneToDamage, Unit.Main/IsImmuneToSpell, Unit.Main/IsInCombat, Unit.Main/IsPvP, Unit.Main/IsVisibleForOrDetect, Unit.Main/RemoveAurasWithInterruptFlags, Unit.Main/RemoveNonPassiveSpellsCausingAura, Unit.Main/RemoveSpellsCausingAura, Unit.Main/SetInCombatWithAggressor, Unit.Main/SetInCombatWithAssisted, Unit.Main/SetInCombatWithVictim, Unit.Main/SetOutOfCombatWithAggressor, Unit.Main/SetOutOfCombatWithVictim, Unit.SpellAuras/CreateSpellAuraHolder, Unit.SpellAuras/IsEmptyHolder, Unit.SpellAuras/SetAuraMaxDuration, WorldObject.Object/IsControlledByPlayer, WorldObject.Object/IsFriendlyTo, WorldObject.Object/IsVisibleForOrDetect | — | — |
| DoAllEffectOnTarget | method | Map.Main/GetGameObject, Player.Main/RewardPlayerAndGroupAtCast, SpellEntry/IsNextMeleeSwingSpell, SpellScript/OnHit, Unit.Main/GetCharmerOrOwnerPlayerOrPlayerItself, WorldObject.Object/GetMap | — | — |
| DoAllEffectOnTarget#2 | method | SpellScript/OnHit | — | — |
| HandleDelayedSpellLaunch | method | Object/GetObjectGuid, ObjectAccessor/GetUnit, ObjectGuid/operator==, SpellCaster/CalculateSpellDamage, SpellDefines/GetFirstSchoolInMask, SpellEntry/IsDirectDamageWithBonusEffect, SpellEntry/IsEffectHandledOnDelayedSpellLaunch, SpellNonMeleeDamage/SpellNonMeleeDamage, Unit.Main/GetSpellModOwner, Unit.Main/RemoveAurasWithInterruptFlags, Unit.Main/ToUnit, WorldObject.Object/IsFriendlyTo | — | — |
| InitializeDamageMultipliers | method | Unit.Main/GetSpellModOwner | — | — |
| HasValidUnitPresentInTargetList | method | Object/GetObjectGuid, ObjectAccessor/GetUnit, ObjectGuid/operator==, SpellEntry/CanTargetAliveState, Unit.Main/IsAlive | — | — |
| ChainHealingOrder | ctor | — | — | — |
| operator()#2 | method | — | — | — |
| ChainHealingHash | method | Object/IsPlayer, Player.Main/IsInSameRaidWith, Unit.Main/GetHealth, Unit.Main/GetMaxHealth | — | — |
| ChainHealingFullHealth | ctor | — | — | — |
| operator() | method | Unit.Main/GetHealth, Unit.Main/GetMaxHealth | — | — |
| TargetDistanceOrderNear | ctor | — | — | — |
| operator()#3 | method | WorldObject.Object/GetDistanceOrder | — | — |
| SetTargetMap | method | AnyAoETargetUnitInObjectRangeCheck/AnyAoETargetUnitInObjectRangeCheck, AnyAoEVisibleTargetUnitInObjectRangeCheck/AnyAoEVisibleTargetUnitInObjectRangeCheck, AnyFriendlyUnitInObjectRangeCheck/AnyFriendlyUnitInObjectRangeCheck, Corpse/GetOwnerGuid, Creature.Main/IsCorpse, Creature.Main/IsPet, Creature.Main/ToCreature, GameObjectEntryInPosRangeCheck/GameObjectEntryInPosRangeCheck, GridMap/GetWaterLevel, Group/GetFirstMember, Group/IsMember, GroupReference/next, Log.Main/Out, Map.Main/GetCorpse, Map.Main/GetGameObject, Map.Main/GetHeight, Map.Main/GetLosHitPosition, Map.Main/GetWalkHitPosition, MapManager/IsValidMapCoord#3, Object/GetEntry, Object/GetObjectGuid, Object/GetTypeId, Object/IsCreature, Object/IsPlayer, ObjectAccessor/FindPlayer, ObjectGuid/IsEmpty, ObjectGuid/IsGameObject, ObjectGuid/operator==, Player.Main/GetGroup, Player.Main/GetSubGroup, Position/Position, shared_Util/urand, SpellCaster/RemoveSpellCooldown, SpellCaster/SelectMagnetTarget, SpellCastTargetsInfo/getCorpseTargetGuid, SpellCastTargetsInfo/getDestination, SpellCastTargetsInfo/getGOTarget, SpellCastTargetsInfo/getGOTargetGuid, SpellCastTargetsInfo/getItemTarget, SpellCastTargetsInfo/getSource, SpellCastTargetsInfo/getUnitTarget, SpellCastTargetsInfo/setCorpseTarget, SpellCastTargetsInfo/setDestination, SpellCastTargetsInfo/setSource, SpellCastTargetsInfo/setUnitTarget, SpellEntry/CanTargetAliveState, SpellEntry/GetSpellMaxRange, SpellEntry/GetSpellRadius, SpellEntry/HasAttribute#4, SpellEntry/HasAttribute#5, SpellEntry/IsPositiveSpell#3, SpellMgr/GetSpellScriptTargetBounds, SpellMgr/GetSpellTargetPosition, SpellMgr/Instance, SpellScript/OnSetTargetMap, SpellTargetEntry/CanNotHitWithSpellEffect, Unit.Main/GetCharm, Unit.Main/GetCharmerOrOwner, Unit.Main/GetClass, Unit.Main/GetHealth, Unit.Main/GetLevel, Unit.Main/GetMaxHealth, Unit.Main/GetOwner, Unit.Main/GetOwnerGuid, Unit.Main/GetPet, Unit.Main/GetSpellModOwner, Unit.Main/IsAlive, Unit.Main/IsTargetableBy, WorldObject.Object/FindNearbyClosedDoor, WorldObject.Object/GetFirstCollisionPosition, WorldObject.Object/GetLeewayBonusRadius, WorldObject.Object/GetMap, WorldObject.Object/GetMapId, WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/GetRandomPoint, WorldObject.Object/GetSafePosition, WorldObject.Object/GetTerrain, WorldObject.Object/GetTransport, WorldObject.Object/HasInArc, WorldObject.Object/HasInArc#2, WorldObject.Object/IsHostileTo, WorldObject.Object/IsWithinDist, WorldObject.Object/IsWithinDistInMap, WorldObject.Object/IsWithinLOS, WorldObject.Object/IsWithinLOSInMap | — | — |
| IsAcceptableAutorepeatError | function | — | — | — |
| UpdateCastStartPosition | method | WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/GetTransOffsetO, WorldObject.Object/GetTransOffsetX, WorldObject.Object/GetTransOffsetY, WorldObject.Object/GetTransOffsetZ, WorldObject.Object/GetTransport | Player.Main/UpdateChannelStartPosition | — |
| prepare | method | SpellCastTargetsInfo/operator= | CombatBotBaseAI/CastWeaponBuff, Creature.Main/TryToCast, GameObject/AddUniqueUse, GameObject/Use, PetAI/UpdateAI, Player.Main/CastItemUseSpell, SpellCaster/CastCustomSpell, SpellCaster/CastSpell, SpellCaster/CastSpell#3, spell_priest/OnSuccessfulFinish, Unit.Main/_UpdateAutoRepeatSpell, WorldSession.NPCHandler/HandleTrainerBuySpellOpcode, WorldSession.SpellHandler/HandleCastSpellOpcode, WorldSession.TradeHandler/HandleAcceptTradeOpcode | — |
| prepare#2 | method | Aura/GetBasePoints, Aura/GetSpellProto, Creature.Main/CanHaveTarget, Creature.Main/SetCastingTarget, EventProcessor/AddEvent, EventProcessor/CalculateTime, Log.Main/HasLogFilter, Log.Main/HasLogLevelOrHigher, Log.Main/Out, Object/GetGUIDLow, Object/IsCreature, Object/IsPet, Object/IsPlayer, Object/ToPlayer, ObjectGuid/IsGameObject, ObjectMgr/IsSpellDisabled, Player.Main/HasCheatOption, shared_Util/roll_chance_u, SpellCaster/AddGCD, SpellCaster/IsNonMeleeSpellCasted, SpellCaster/SetCurrentCastedSpell, SpellCastTargetsInfo/getGOTarget, SpellCastTargetsInfo/getUnitTarget, SpellEntry/CalculateDuration, SpellEntry/GetCastTime, SpellEntry/HasAttribute#4, SpellEntry/IsDirectDamageSpell, SpellEntry/IsExplicitlySelectedUnitTarget, SpellEntry/IsSpellWithDelayableEffects, SpellScript/OnSuccessfulStart, Unit.Main/CancelSpellChannelingAnimationInstantly, Unit.Main/ClearUnitState, Unit.Main/HasUnitState, Unit.Main/RemoveAurasWithInterruptFlags, Unit.Main/StopMoving, WorldObject.Object/GetName | spell_special/OnSuccessfulStart, WorldSession.PetHandler/HandlePetCastSpellOpcode | — |
| cancel | method | GameObject/GetGoType, GameObject/getLootState, GameObject/HasUniqueUser, GameObject/RemoveUniqueUse, Map.Main/GetGameObject, Object/GetObjectGuid, Object/IsPlayer, Object/ToPlayer, ObjectAccessor/GetUnit, ObjectGuid/operator==, Player.Main/RemoveSpellMods, Player.Main/RestoreSpellMods, SpellCaster/RemoveDynObject, SpellCaster/ResetGCD, SpellCastTargetsInfo/getGOTarget, SpellCastTargetsInfo/getGOTargetGuid, Unit.Main/IsAlive, Unit.Main/RemoveAurasByCasterSpell, Unit.Main/RemoveGameObject#2, WorldObject.Object/GetMap | Player.Main/InterruptSpellsWithCastItem, SpellCaster/InterruptSpell, Unit.Main/InterruptSpellsCastedOnMe, Unit.SpellAuras/PeriodicTick, WorldSession.MiscHandler/HandleSetSelectionOpcode | — |
| cast | method | Log.Main/Out, Object/GetGUIDLow, Object/GetObjectGuid, Object/IsPlayer, Object/ToPlayer, ObjectGuid/operator!=, ObjectGuid/operator==, Player.Main/RestoreSpellMods, SpellCaster/CheckAndIncreaseCastCounter, SpellCaster/DecreaseCastCounter, SpellCaster/ProcDamageAndSpell, SpellCaster/ProcSystemArguments, SpellCastTargetsInfo/getUnitTarget, SpellCastTargetsInfo/getUnitTargetGuid, SpellCastTargetsInfo/updateTradeSlotItem, SpellEntry/HasAttribute#3, SpellEntry/HasAttribute#4, SpellEntry/HasAttribute#5, SpellEntry/IsPositiveEffectMask, SpellEntry/IsPositiveTarget, SpellScript/OnCast, Unit.Main/CreateProcExtendMask, Unit.Main/GetCharmerGuid, Unit.Main/HasUnitState, Unit.Main/HaveOffhandWeapon, Unit.Main/IsCharmerOrOwnerPlayerOrPlayerItself, Unit.Main/RemoveAurasWithInterruptFlags, Unit.Main/ResetAttackTimer, Unit.Main/SetInCombatWithVictim, Unit.Main/SetInFront, WorldObject.Object/GetMapId, WorldObject.Object/GetName | Unit.Main/AttackerStateUpdate | — |
| handle_immediate | method | Map.Main/GetCorpse, Object/IsPlayer, Object/ToPlayer, Player.Main/RemoveSpellMods, SpellCaster/MoveChannelledSpellWithCastTime, SpellCastTargetsInfo/getCorpseTargetGuid, SpellEntry/HasEffect, SpellEntry/IsChanneledSpell, WorldObject.Object/GetMap | — | — |
| handle_delayed | method | — | — | — |
| _handle_immediate_phase | method | — | — | — |
| _handle_finish_phase | method | — | — | — |
| SendSpellCooldown | method | game_Objects_Item/GetProto, Object/ToPlayer, Player.Main/HasCheatOption, SpellCaster/AddCooldown, SpellEntry/HasAttribute | — | — |
| update | method | GameObject/Delete, GameObject/GetGOInfo, GameObject/GetGoType, GameObject/GetOwner, GameObject/GetUniqueUseCount, GameObject/SetGoState, Log.Main/Out, Map.Main/GetGameObject, MovementInfo/HasMovementFlag, Object/GetGuidStr, Object/GetObjectGuid, Object/IsPet, Object/IsPlayer, ObjectAccessor/GetUnit, ObjectGuid/IsCreature, ObjectGuid/operator==, Player.Main/RewardPlayerAndGroupAtCast, SpellAuraHolder/GetAuraDuration, SpellAuraHolder/GetRemoveMode, SpellAuraHolder/IsDeleted, SpellAuraHolder/SetInUse, SpellAuraHolder/UpdateHolder, SpellCastTargetsInfo/getGOTarget, SpellCastTargetsInfo/getUnitTarget, SpellCastTargetsInfo/getUnitTargetGuid, SpellEntry/HasAttribute#5, SpellEntry/HasAura, SpellEntry/HasAuraInterruptFlag, SpellEntry/HasChannelInterruptFlag, SpellEntry/HasSpellInterruptFlag, SpellEntry/IsIgnoringCasterAndTargetRestrictions, SpellEntry/IsNextMeleeSwingSpell, Unit.Main/GetCharmerOrOwnerPlayerOrPlayerItself, Unit.Main/HasPendingMovementChange, Unit.Main/HasUnitState, Unit.Main/IsRooted, Unit.Main/SendPlaySpellVisualKit, WorldObject.Object/GetMap, WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/GetTransOffsetO, WorldObject.Object/GetTransOffsetX, WorldObject.Object/GetTransOffsetY, WorldObject.Object/GetTransOffsetZ, WorldObject.Object/GetTransport | WorldSession.NPCHandler/HandleTrainerBuySpellOpcode | — |
| HandleAddTargetTriggerAuras | method | Aura/GetBasePoints, Aura/GetEffIndex, Aura/GetSpellProto, Object/GetObjectGuid, Object/IsPlayer, Object/ToPlayer, ObjectAccessor/GetUnit, ObjectGuid/operator!=, ObjectGuid/operator==, Player.Main/HasCheatOption, shared_Util/roll_chance_i, SpellCaster/CalculateSpellEffectValue, SpellCaster/CastSpell, SpellEntry/HasAttribute#6, SpellMgr/GetSpellEntry, SpellMgr/Instance, Unit.Main/GetAurasByType, Unit.Main/GetTargetGuid, Unit.Main/IsAlive, Unit.SpellAuras/IsAffectedOnSpell | — | — |
| finish | method | Creature.Main/AI, Creature.Main/ClearCastingTarget, CreatureAI/AttackStart, GameObject/FinishRitual, GameObject/GetGOInfo, GameObject/GetGoType, Object/GetObjectGuid, Object/IsPlayer, Object/ToPet, Object/ToPlayer, ObjectGuid/operator!=, Player.Main/ClearComboPoints, Player.Main/HasCheatOption, Player.Main/RemoveSpellMods, Player.Main/RestoreSpellMods, Player.Main/SendClearCooldown, SpellCaster/InterruptSpell, SpellCaster/IsSpellReady#2, SpellCastTargetsInfo/getGOTarget, SpellCastTargetsInfo/getUnitTarget, SpellEntry/HasAttribute, SpellEntry/HasAttribute#3, SpellEntry/HasAttribute#4, SpellEntry/IsPositiveSpell#4, SpellEntry/NeedsComboPoints, SpellScript/OnSuccessfulFinish, Unit.Main/AttackStop, Unit.Main/GetCharmInfo, Unit.Main/GetClass, Unit.Main/SetIsAtStay, Unit.Main/SetIsCommandAttack, Unit.Main/SetIsCommandFollow, Unit.Main/SetIsFollowing, Unit.Main/SetIsReturning | Spell.Effects/EffectInstaKill, Spell.Effects/EffectTameCreature, Spell.Effects/EffectTransmitted, SpellCaster/FinishSpell, WorldSession.PetHandler/HandlePetCastSpellOpcode | — |
| SendCastResult | method | Object/IsPlayer, Player.Main/GetSession, WorldSession.Main/PlayerLoading | Spell.Effects/EffectDuel, Spell.Effects/EffectFeedPet, Spell.Effects/EffectOpenLock, Spell.Effects/EffectTaunt, Spell.Effects/EffectTransmitted, spell_warlock/OnEffectExecute#5, WorldSession.TradeHandler/HandleAcceptTradeOpcode | — |
| SendCastResult#2 | method | ByteBuffer/operator<<#10, ByteBuffer/operator<<#7, Player.Main/GetSession, SpellCaster/IsSpellOnPermanentCooldown, SpellEntry/HasAttribute, SpellEntry/HasAttribute#4, SpellEntry/IsPassiveSpell#2, SpellMgr/GetRequiredAreaForSpell, SpellMgr/Instance, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | Unit.SpellAuras/Update#4, WorldSession.SpellHandler/HandleUseItemOpcode | — |
| WriteGuidHelper | function | Object/GetPackGUID, ObjectGuid/ObjectGuid, ObjectGuid/operator<<#2, ObjectGuid/WriteAsPacked | — | — |
| SendSpellStart | method | ByteBuffer/operator<<#10, ByteBuffer/operator<<#13, Log.Main/HasLogFilter, Log.Main/HasLogLevelOrHigher, Log.Main/Out, SpellCastTargetsInfo/operator<<, SpellEntry/IsRangedSpell, WorldObject.Object/SendObjectMessageToSet, WorldPacket/WorldPacket#4 | — | — |
| SendSpellGo | method | ByteBuffer/operator<<#10, ByteBuffer/operator<<#13, SpellCastTargetsInfo/operator<<, SpellEntry/IsRangedSpell, WorldObject.Object/SendObjectMessageToSet, WorldPacket/WorldPacket#4 | — | — |
| WriteAmmoToPacket | method | ByteBuffer/operator<<#10, game_Objects_Item/GetProto, Object/GetByteValue, Object/GetUInt32Value, Object/IsPlayer, ObjectMgr/GetItemPrototype, Player.Main/GetWeaponForAttack | — | — |
| WriteSpellGoTargets | method | ByteBuffer/operator<<#7, ByteBuffer/wpos, ObjectGuid/operator<< | — | — |
| SendLogExecute | method | ByteBuffer/operator<<#10, ByteBuffer/operator<<#4, ByteBuffer/operator<<#7, ByteBuffer/operator<<#9, Object/GetPackGUID, ObjectGuid/operator<<, ObjectGuid/operator<<#2, WorldObject.Object/SendMessageToSet, WorldPacket/WorldPacket#4 | — | — |
| SendInterrupted | method | ByteBuffer/operator<<#10, Object/GetObjectGuid, ObjectGuid/operator<<, WorldObject.Object/SendObjectMessageToSet, WorldPacket/WorldPacket#4 | — | — |
| SendAllTargetsMiss | method | ByteBuffer/operator<<#10, ByteBuffer/operator<<#7, Object/GetObjectGuid, Object/IsInWorld, ObjectGuid/operator<<, WorldObject.Object/SendObjectMessageToSet, WorldPacket/WorldPacket#4 | — | — |
| SendChannelUpdate | method | Camera/ResetView, ChannelResetEvent/ChannelResetEvent, Creature.Main/ForcedDespawn, EventProcessor/AddEventAtOffset, Object/GetObjectGuid, Object/GetUInt32Value, Object/IsCreature, Object/IsPlayer, Object/ToPlayer, ObjectAccessor/GetUnit, ObjectGuid/IsUnit, ObjectGuid/ObjectGuid, ObjectGuid/operator!=, Player.Main/GetCamera, Player.Main/RemovePetActionBar, Player.Main/SendChannelUpdate, Player.Main/SetClientControl, Player.Main/SetMover, SpellAuraHolder/GetTarget, SpellAuraHolder/IsDeleted, SpellAuraHolder/SetInUse, SpellCaster/GetCurrentSpell, SpellCaster/RemoveDynObject, SpellEntry/HasAttribute#3, SpellEntry/HasAura, Unit.Main/CancelSpellChannelingAnimationInstantly, Unit.Main/ClearUnitState, Unit.Main/GetChannelObjectGuid, Unit.Main/GetCharm, Unit.Main/GetCharmGuid, Unit.Main/RemoveAurasByCasterSpell, Unit.Main/RemoveGameObject#2, Unit.Main/RemoveSpellAuraHolder, Unit.Main/SetCharm, Unit.Main/SetCharmerGuid, WorldObject.Object/RemoveFlag | Spell.Effects/EffectTransmitted, SpellCaster/FinishSpell | — |
| SendChannelStart | method | ByteBuffer/operator<<#10, ByteBuffer/wpos, Map.Main/GetGameObject, Object/GetObjectGuid, Object/IsPlayer, ObjectAccessor/GetUnit, ObjectGuid/operator!=, ObjectGuid/operator<<, Player.Main/SendDirectMessage, SpellCaster/GetDynObject, SpellEntry/HasAttribute#3, Unit.Main/SetChannelObjectGuid, WorldObject.Object/GetMap, WorldObject.Object/SendMessageToSet, WorldObject.Object/SetUInt32Value, WorldPacket/WorldPacket#4 | — | — |
| InitializeChanneledVisualTimer | method | — | — | — |
| SendResurrectRequest | method | ByteBuffer/operator<<#10, ByteBuffer/operator<<#3, ByteBuffer/operator<<#7, Object/GetObjectGuid, Object/IsPlayer, ObjectGuid/operator<<, Player.Main/GetSession, SpellEntry/HasAttribute#5, WorldObject.Object/GetNameForLocaleIdx, WorldPacket/WorldPacket#4, WorldSession.Main/GetSessionDbLocaleIndex, WorldSession.Main/SendPacket | Spell.Effects/EffectResurrect, Spell.Effects/EffectResurrectNew | — |
| TakeCastItem | method | game_Objects_Item/GetProto, game_Objects_Item/GetSpellCharges, game_Objects_Item/SetSpellCharges, game_Objects_Item/SetState, Log.Main/Out, Object/GetGuidStr, Object/IsPlayer, Player.Main/DestroyItemCount | — | — |
| TakePower | method | Log.Main/Out, Object/ToPlayer, Player.Main/HasCheatOption, SpellEntry/HasAttribute#4, Unit.Main/ModifyHealth, Unit.Main/ModifyPower, Unit.Main/SetLastManaUse | — | — |
| TakeReagents | method | game_Objects_Item/GetProto, game_Objects_Item/GetSpellCharges, Object/IsPlayer, Player.Main/DestroyItemCount#2, SpellCastTargetsInfo/getItemTargetEntry, SpellCastTargetsInfo/setItemTarget | — | — |
| TakeAmmo | method | game_Objects_Item/GetMaxStackCount, game_Objects_Item/GetProto, Object/GetUInt32Value, Object/ToPlayer, Player.Main/DestroyItemCount, Player.Main/DestroyItemCount#2, Player.Main/DurabilityPointLossForEquipSlot, Player.Main/GetWeaponForAttack#2 | — | — |
| HandleThreatSpells | method | HostileRefManager/threatAssist, Log.Main/HasLogFilter, Log.Main/HasLogLevelOrHigher, Log.Main/Out, Object/GetObjectGuid, ObjectAccessor/GetUnit, ObjectGuid/operator==, SpellEntry/GetSpellSchoolMask, SpellEntry/GetWeaponAttackType, SpellMgr/GetSpellRank, SpellMgr/GetSpellThreatEntry, SpellMgr/Instance, Unit.Main/AddThreat, Unit.Main/CanHaveThreatList, Unit.Main/GetHostileRefManager, Unit.Main/GetTotalAttackPowerValue | — | — |
| HandleEffects | method | Log.Main/HasLogFilter, Log.Main/HasLogLevelOrHigher, Log.Main/Out, Object/GetGuidStr, SpellScript/OnEffectExecute | — | — |
| AddChanneledAuraHolder | method | SpellAuraHolder/IsChanneled, SpellAuraHolder/SetInUse | Unit.SpellAuras/Update | — |
| RemoveChanneledAuraHolder | method | SpellAuraHolder/SetInUse | Unit.Main/RemoveSpellAuraHolder | — |
| CheckCast | method | Aura/GetModifier, BattleGround/CheckSpellCast, BattleGround/GetStatus, CharmInfo/GetOriginalFactionTemplate, Creature.Main/GetCreatureInfo, Creature.Main/GetLootGroupRecipientId, Creature.Main/GetLootRecipientGuid, Creature.Main/IsSkinnableBy, Creature.Main/IsTappedBy, Creature.Main/ToCreature, FactionTemplateEntry/IsFriendlyTo, GameObject/GetGOInfo, GameObject/GetGoState, GameObject/GetGoType, GameObject/IsUseRequirementMet, GameObjectInfo/CannotBeUsedUnderImmunity, GameObjectInfo/GetLockId, game_Objects_Item/GetOwner, game_Objects_Item/GetProto, GridMap/IsOutdoors, IVMapManager/isLineOfSightCalcEnabled, Log.Main/HasLogFilter, Log.Main/HasLogLevelOrHigher, Log.Main/Out, Loot/HasPlayersLooting, Loot/isLooted, Map.Main/GetCorpse, Map.Main/GetHeight, Map.Main/GetUnit, MapEntry/IsDungeon, MapEntry/IsMountAllowed, MovementInfo/HasMovementFlag, Object/GetObjectGuid, Object/GetTypeId, Object/HasFlag, Object/IsCreature, Object/IsInWorld, Object/IsPlayer, Object/ToCreature, Object/ToPlayer, ObjectGuid/GetRawValue, ObjectGuid/IsPlayer, ObjectGuid/operator!=, PathInfo/getPathType, PathInfo/SetTransport, Pet.Main/CanTakeMoreActiveSpells, Pet.Main/GetCurrentFoodBenefitLevel, Pet.Main/HasTPForSpell, Pet.Main/HaveInDiet, Pet.Main/Unsummon, Player.Main/CanUseBattleGroundObject, Player.Main/GetBattleGround, Player.Main/GetSelectionGuid, Player.Main/GetSkillValue, Player.Main/GetTradeData, Player.Main/GetTrader, Player.Main/HasCheatOption, Player.Main/InBattleGround, Player.Main/IsBeingTeleported, Player.Main/IsGameMaster, Player.Main/IsInHighLiquid, Player.Main/IsInSameRaidWith, Player.Main/IsOutdoorOnTransport, Player.Main/SendPetTameFailure, Player.Main/ToPlayer, shared_Util/irand, SpellAuraHolder/GetSpellProto, SpellAuraHolder/IsDeleted, SpellCaster/HasGCD, SpellCaster/IsSpellReady, SpellCastTargetsInfo/getCorpseTargetGuid, SpellCastTargetsInfo/getGOTarget, SpellCastTargetsInfo/getItemTarget, SpellCastTargetsInfo/getItemTargetGuid, SpellCastTargetsInfo/getUnitTarget, SpellCastTargetsInfo/IsEmpty, SpellCastTargetsInfo/setUnitTarget, SpellEntry/CanTargetAliveState, SpellEntry/GetDispellMask, SpellEntry/GetEffectsCount, SpellEntry/GetErrorAtShapeshiftedCast, SpellEntry/GetSpellSchoolMask, SpellEntry/HasAttribute, SpellEntry/HasAttribute#3, SpellEntry/HasAttribute#4, SpellEntry/HasAttribute#5, SpellEntry/HasAuraInterruptFlag, SpellEntry/HasEffect, SpellEntry/IsAreaEffectTarget, SpellEntry/IsAreaOfEffectSpell, SpellEntry/IsAutoRepeatRangedSpell, SpellEntry/IsCharmSpell, SpellEntry/IsDeathOnlySpell, SpellEntry/IsDispel, SpellEntry/IsExplicitlySelectedUnitTarget, SpellEntry/IsExplicitNegativeTarget, SpellEntry/IsExplicitPositiveTarget, SpellEntry/IsFromBehindOnlySpell, SpellEntry/IsHealSpell, SpellEntry/IsNonCombatSpell, SpellEntry/IsNonPeriodicDispel, SpellEntry/IsPassiveSpell#2, SpellEntry/IsPositiveSpell#3, SpellEntry/IsPositiveSpell#4, SpellEntry/IsScriptTarget, SpellEntry/IsSpellAppliesAura, SpellEntry/IsSpellWithCasterSourceTargetsOnly, SpellMgr/GetRequiredAreaForSpell, SpellMgr/GetSpellAllowedInLocationError, SpellMgr/GetSpellEntry, SpellMgr/Instance, SpellMgr/SelectAuraRankForLevel, SpellScript/OnCheckCast, TradeData/IsInAcceptProcess, TradeData/SetSpell, Unit.Main/CanBeDisarmed, Unit.Main/GetAurasByType, Unit.Main/GetCharm, Unit.Main/GetCharmerGuid, Unit.Main/GetCharmerOrOwnerGuid, Unit.Main/GetCharmerOrOwnerPlayerOrPlayerItself, Unit.Main/GetCharmGuid, Unit.Main/GetCharmInfo, Unit.Main/GetCreatureType, Unit.Main/GetCreatureTypeMask, Unit.Main/GetLevel, Unit.Main/GetOwnerGuid, Unit.Main/GetPet, Unit.Main/GetPetGuid, Unit.Main/GetPowerType, Unit.Main/GetShapeshiftForm, Unit.Main/GetSpellAuraHolderMap, Unit.Main/HasAura#2, Unit.Main/HasAuraState, Unit.Main/HasMorePowerfulSpellActive, Unit.Main/HasStealthAura, Unit.Main/HasUnitState, Unit.Main/IsAlive, Unit.Main/IsBehindTarget, Unit.Main/IsFriendlyTo, Unit.Main/IsImmuneToSchoolMask, Unit.Main/IsImmuneToSpell, Unit.Main/IsInCombat, Unit.Main/IsInDisallowedMountForm, Unit.Main/IsInWater, Unit.Main/IsMounted, Unit.Main/IsShapeShifted, Unit.Main/IsStandingUp, Unit.Main/IsTaxiFlying, Unit.Main/RemoveSpellsCausingAura, Unit.Main/Uncharm, Unit.Main/Unmount, Unit.Main/UnsummonOldPetBeforeNewSummon, Unit.SpellAuras/IsPositive, VMapFactory/createOrGetVMapManager, World/getConfig, World/GetConfigMaxSkillValue, WorldObject.Object/GetAreaId, WorldObject.Object/GetFactionTemplateEntry, WorldObject.Object/GetMap, WorldObject.Object/GetMapId, WorldObject.Object/GetPosition#2, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/GetTerrain, WorldObject.Object/GetTransport, WorldObject.Object/HasInArc, WorldObject.Object/IsFriendlyTo, WorldObject.Object/IsHostileTo, WorldObject.Object/IsMoving, WorldObject.Object/IsValidAttackTarget, WorldObject.Object/IsValidHelpfulTarget, WorldObject.Object/IsWithinLOS, WorldObject.Object/IsWithinLOSInMap, WorldObject.PathFinder/calculate#2, WorldObject.PathFinder/Length, WorldObject.PathFinder/PathInfo | Player.Main/DismountCheck, Unit.Main/_UpdateAutoRepeatSpell, WorldSession.TradeHandler/HandleAcceptTradeOpcode | — |
| CheckPetCast | method | Creature.Main/IsPet, Object/IsCreature, SpellCaster/IsNonMeleeSpellCasted, SpellCaster/IsSpellReady, SpellCastTargetsInfo/getUnitTarget, SpellCastTargetsInfo/setUnitTarget, SpellEntry/IsExplicitlySelectedUnitTarget, SpellEntry/IsNonCombatSpell, SpellEntry/IsPositiveSpell#3, Unit.Main/GetCharmerOrOwner, Unit.Main/IsAlive, Unit.Main/IsCharmed, Unit.Main/IsHostileTo, Unit.Main/IsInCombat, Unit.Main/IsTargetableBy, WorldObject.Object/IsValidAttackTarget | WorldSession.PetHandler/HandlePetCastSpellOpcode | — |
| CheckCasterAuras | method | Aura/GetModifier, Object/GetUInt32Value, SpellAuraHolder/GetAuraByEffectIndex, SpellAuraHolder/GetSpellProto, SpellCaster/CheckLockout, SpellEntry/GetDispellMask, SpellEntry/GetSpellMechanicMask, SpellEntry/GetSpellSchoolMask, SpellEntry/HasSpellInterruptFlag, SpellEntry/IsIgnoringCasterAndTargetRestrictions, Unit.Main/GetSpellAuraHolderMap | — | — |
| ValidateExplicitTargetMask | method | Player.Main/GetSession, Player.Main/ToPlayer, SpellDefines/SpellCastTargetFlagToString, WorldSession.Main/ProcessAnticheatAction | — | — |
| CanAutoCast | method | Object/GetObjectGuid, Object/HasFlag, ObjectGuid/operator==, SpellEntry/HasAttribute, SpellEntry/HasEffect, SpellEntry/IsAreaAuraEffect, SpellEntry/IsHealSpell, Unit.Main/CanReachWithMeleeAutoAttack, Unit.Main/GetAttackerForHelper, Unit.Main/GetHealth, Unit.Main/GetMaxHealth, Unit.Main/GetVictim, Unit.Main/HasAura | PetAI/UpdateAI | — |
| CheckTamingSpell | method | CharacterDatabaseCache/GetCharacterPetByOwner, CharacterDatabaseCache/instance, Creature.Main/GetCreatureInfo, Creature.Main/IsPet, Creature.Main/ToCreature, CreatureInfo/IsTameable, Object/GetGUIDLow, Player.Main/IsSavingDisabled, SpellCastTargetsInfo/getUnitTarget, Unit.Main/GetCharmGuid, Unit.Main/GetClass, Unit.Main/GetLevel, Unit.Main/GetPetGuid, Unit.Main/IsCharmed | — | — |
| CheckRange | method | GameObject/IsAtInteractDistance#2, Object/IsPlayer, SpellCastTargetsInfo/getGOTarget, SpellCastTargetsInfo/getUnitTarget, SpellEntry/GetSpellMaxRange, SpellEntry/GetSpellMinRange, SpellEntry/IsNextMeleeSwingSpell, Unit.Main/GetPet, Unit.Main/GetSpellModOwner, WorldObject.Object/CanReachWithMeleeSpellAttack, WorldObject.Object/GetCombatDistance, WorldObject.Object/GetLeewayBonusRange, WorldObject.Object/IsFacingTarget, WorldObject.Object/IsWithinDist3d | — | — |
| CalculatePowerCost | method | Log.Main/Out, Object/GetFloatValue, Object/GetInt32Value, SpellDefines/GetFirstSchoolInMask, SpellEntry/GetSpellSchoolMask, Unit.Main/GetCreateHealth, Unit.Main/GetCreateMana, Unit.Main/GetHealth, Unit.Main/GetLevel, Unit.Main/GetMaxPower, Unit.Main/GetPower, Unit.Main/GetSpellModOwner, Unit.Main/GetSpellRank | CombatBotBaseAI/CanTryToCastSpell | — |
| CheckPower | method | Log.Main/Out, Object/GetObjectGuid, Object/IsCreature, Object/IsPet, Object/IsPlayer, ObjectGuid/operator!=, Player.Main/GetComboTargetGuid, SpellCastTargetsInfo/getUnitTarget, SpellEntry/IsExplicitlySelectedUnitTarget, SpellEntry/NeedsComboPoints, Unit.Main/GetClass, Unit.Main/GetCreateMana, Unit.Main/GetHealth, Unit.Main/GetPower | — | — |
| IgnoreItemRequirements | method | game_Objects_Item/GetOwnerGuid, Object/GetObjectGuid, ObjectGuid/operator!=, SpellCastTargetsInfo/getItemTarget | — | — |
| CheckItems | method | Creature.Main/HasWeapon, GameObjectFocusCheck/GameObjectFocusCheck, game_Objects_Item/GetOwner, game_Objects_Item/GetOwnerGuid, game_Objects_Item/GetProto, game_Objects_Item/GetSlot, game_Objects_Item/GetSpellCharges, game_Objects_Item/HasGeneratedLoot, game_Objects_Item/IsFitToSpellRequirements, game_Objects_Item/IsInTrade, ItemPrototype/HasItemFlag, Object/GetEntry, Object/GetObjectGuid, Object/GetUInt32Value, Object/IsPlayer, Object/ToCreature, Object/ToPlayer, ObjectGuid/operator!=, ObjectMgr/GetItemPrototype, Player.Main/CanStoreNewItem, Player.Main/GetWeaponForAttack, Player.Main/GetWeaponForAttack#2, Player.Main/HasItemCount, Player.Main/HasItemFitToSpellReqirements, Player.Main/SendEquipError, shared_Util/dither, SpellCastTargetsInfo/getItemTarget, SpellCastTargetsInfo/getItemTargetGuid, SpellCastTargetsInfo/getUnitTarget, SpellEntry/HasAttribute, SpellEntry/HasAttribute#4, SpellEntry/HasAttribute#5, Unit.Main/CanUseEquippedWeapon, Unit.Main/GetHealth, Unit.Main/GetMaxHealth, Unit.Main/GetMaxPower, Unit.Main/GetPower | — | — |
| Delayed | method | ByteBuffer/operator<<#10, Log.Main/HasLogFilter, Log.Main/HasLogLevelOrHigher, Log.Main/Out, Object/GetObjectGuid, Object/IsPlayer, ObjectGuid/operator<<, Player.Main/SendDirectMessage, shared_Util/roll_chance_i, SpellEntry/HasSpellInterruptFlag, Unit.Main/GetTotalAuraModifier, WorldPacket/WorldPacket#4 | Unit.Main/DealDamage | — |
| DelayedChannel | method | DynamicObject/Delay, Log.Main/HasLogFilter, Log.Main/HasLogLevelOrHigher, Log.Main/Out, Object/GetObjectGuid, Object/IsPlayer, ObjectAccessor/GetUnit, ObjectGuid/operator==, shared_Util/roll_chance_i, SpellCaster/GetDynObject, SpellCaster/InterruptSpell, Unit.Main/DelaySpellAuraHolder, Unit.Main/GetTotalAuraModifier | Unit.Main/DealDamage | — |
| UpdateOriginalCasterPointer | method | GameObject/GetOwner, Map.Main/GetGameObject, Object/GetObjectGuid, Object/IsInWorld, ObjectAccessor/GetUnit, ObjectGuid/IsGameObject, ObjectGuid/operator==, WorldObject.Object/GetMap | — | — |
| UpdatePointers | method | SpellCastTargetsInfo/Update | Unit.Main/_UpdateAutoRepeatSpell | — |
| CheckTargetCreatureType | method | Unit.Main/GetCharmerOrOwnerPlayerOrPlayerItself, Unit.Main/GetCreatureTypeMask, Unit.Main/HasAura#2 | — | — |
| GetCurrentContainer | method | SpellEntry/IsNextMeleeSwingSpell | SpellCaster/SetCurrentCastedSpell | — |
| CheckTarget | method | Creature.Main/IsImmuneToAoe, Object/GetObjectGuid, Object/GetTypeId, Object/HasFlag, Object/IsCreature, Object/IsPlayer, ObjectGuid/operator!=, Player.Main/IsGameMaster, SpellCastTargetsInfo/getUnitTarget, SpellEntry/HasAttribute#5, SpellEntry/IsIgnoringCasterAndTargetRestrictions, SpellEntry/IsPositiveSpell#4, SpellEntry/IsScriptTarget, SpellMgr/Instance, SpellMgr/SelectAuraRankForLevel, SpellScript/OnCheckTarget#2, Unit.Main/GetCharmerGuid, Unit.Main/GetCharmerOrOwnerGuid, Unit.Main/GetCharmerOrOwnerPlayerOrPlayerItself, Unit.Main/GetLevel, Unit.Main/GetVisibility | SpellCaster/SelectMagnetTarget | — |
| IsNeedSendToClient | method | Object/IsInWorld | — | — |
| IsTriggeredSpellWithRedundentData | method | — | SpellEntry/GetCastTime | — |
| HaveTargetsForEffect | method | — | — | — |
| SpellEvent | ctor | EventProcessor/BasicEvent | — | — |
| ~SpellEvent | dtor | — | — | — |
| Execute#2 | method | EventProcessor/AddEvent, Log.Main/Out, SpellCaster/IsNonMeleeSpellCasted, SpellEntry/IsChanneledSpell, WorldObject.Object/GetName | — | — |
| Abort#2 | method | — | — | — |
| IsDeletable#2 | method | — | — | — |
| CanOpenLock | method | Object/GetEntry, Object/IsPlayer, Player.Main/GetSkillValue, SharedDefines/SkillByLockType | Spell.Effects/EffectOpenLock | — |
| GetCenterX | method | — | — | — |
| GetCenterY | method | — | — | — |
| SpellNotifierCreatureAndPlayer | ctor | Log.Main/Out, SpellCastTargetsInfo/getUnitTarget, SpellMgr/GetSpellCone, SpellMgr/Instance, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY | — | — |
| Visit#2 | method | — | — | — |
| Visit#4 | method | — | — | — |
| Visit#3 | method | — | — | — |
| Visit | method | — | — | — |
| FillAreaTargets | method | WorldObject.Object/GetMap | — | — |
| FillRaidOrPartyTargets | method | Group/GetFirstMember, GroupReference/next, Player.Main/GetGroup, Player.Main/GetSubGroup, Unit.Main/GetCharmerOrOwnerOrSelf, Unit.Main/GetCharmerOrOwnerPlayerOrPlayerItself, Unit.Main/GetLevel, Unit.Main/GetPet, WorldObject.Object/IsHostileTo, WorldObject.Object/IsWithinDistInMap | — | — |
| GetAffectiveCasterObject | method | Map.Main/GetGameObject, Object/IsInWorld, ObjectGuid/IsGameObject, ObjectGuid/operator!, WorldObject.Object/GetMap | Spell.Effects/EffectHeal, Spell.Effects/EffectHealMechanical, Spell.Effects/EffectPersistentAA | — |
| GetCastingObject | method | Map.Main/GetGameObject, Object/IsInWorld, ObjectGuid/IsGameObject, WorldObject.Object/GetMap | Creature.Main/TryToCast, Spell.Effects/EffectLearnSkill, SpellCaster/CastSpell | — |
| ClearCastItem | method | SpellCastTargetsInfo/getItemTarget, SpellCastTargetsInfo/setItemTarget | Player.Main/InterruptSpellsWithCastItem, Spell.Effects/EffectSummonChangeItem | — |
| ResetEffectDamageAndHeal | method | — | — | — |
| SetClientStarted | method | — | Player.Main/CastItemUseSpell, WorldSession.SpellHandler/HandleCastSpellOpcode | — |
| OnSpellLaunch | method | Creature.MotionMaster/MoveCharge, GameObject/DoAggroWhenOpening, Object/IsInWorld, Object/IsPlayer, SpellCastTargetsInfo/getGOTarget, SpellCastTargetsInfo/getUnitTarget, SpellEntry/HasAttribute#4, SpellEntry/IsPositiveSpell#4, Unit.Main/GetAttackTimer, Unit.Main/GetMotionMaster, Unit.Main/IsInCombat, Unit.Main/SetAttackTimer, Unit.Main/SetInCombatWithVictim, WorldObject.Object/GetDistance#3 | — | — |
| HasModifierApplied | method | — | Player.Main/DropModCharge | — |
| IsTriggeredByProc | method | SpellEntry/HasAttribute#5 | Unit.Main/DealDamage | — |
| ShouldRemoveStealthAuras | method | Object/IsPlayer, shared_Util/roll_chance_u, Unit.Main/HasAura#2 | — | — |
| Delete | method | Log.Main/Out | PetAI/UpdateAI, WorldSession.PetHandler/HandlePetCastSpellOpcode, WorldSession.TradeHandler/HandleAcceptTradeOpcode | — |
| Execute | method | — | — | — |
| Abort | method | SpellCaster/GetCurrentSpell, Unit.Main/CancelSpellChannelingAnimationInstantly, Unit.Main/ClearUnitState, Unit.Main/HasUnitState | — | — |

---

<!-- verify: failed-members | invented: operator -->
