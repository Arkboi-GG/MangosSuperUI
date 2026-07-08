# SpellAuraHolder

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# SpellAuraHolder

**Purpose & Responsibilities**

`SpellAuraHolder` is the central container class responsible for managing the lifecycle, state, and metadata of a single spell cast applied to a `Unit`. In the WoWVMaNGOS architecture, a "spell" can produce multiple effects (e.g., damage over time, stat buffs, visual effects). `SpellAuraHolder` does not handle the individual effect logic itself; instead, it holds an array of `Aura` objects (`m_auras`), one for each effect index of the spell.

Its primary responsibilities are:
1.  **State Management:** Tracking the spell's duration, charges, stacks, caster information, and application flags (permanent, passive, channeled, etc.).
2.  **Lifecycle Coordination:** Orchestrating the addition and removal of its contained `Aura` objects from the target `Unit`'s modifier lists and visual slots.
3.  **Game Rule Enforcement:** Handling diminishing returns, debuff limits, buff exclusivity checks, and refresh logic (whether a new cast replaces an existing one).
4.  **Periodic Updates:** Providing the interface for the game loop to tick down durations and process periodic effects via its contained `Aura` instances.

It acts as the bridge between the high-level `Spell` casting system and the low-level `Unit` stat modification system.

## Member-by-Member Behavior

### Construction and Initialization
*   **Constructor**: Initializes the holder with the spell prototype, target, caster, item, and real caster. It sets up the internal `m_auras` array and initializes state flags.

### Diminishing Returns (DR)
*   **setDiminishGroup / getDiminishGroup**: Sets and retrieves the diminishing returns group (e.g., Fear, Stun) associated with the spell. This is used to track how many times a unit has been affected by spells in the same group recently.
*   **setDiminishLevel**: Sets the specific DR level (e.g., Level 1 fear vs. Level 2 fear) for the spell.

### Stack and Charge Management
*   **GetStackAmount / SetStackAmount / ModStackAmount**: Manages the number of stacks of the aura. `ModStackAmount` adjusts the stack count and triggers `UpdateAuraApplication` if the count changes, ensuring visual and mechanical updates.
*   **GetAuraCharges / SetAuraCharges / DropAuraCharge**: Manages the number of uses (charges) remaining for the aura. `DropAuraCharge` decrements the charge count and returns `true` if the last charge was consumed, signaling potential removal.

### Aura Effect Access
*   **GetAuraByEffectIndex**: Retrieves the specific `Aura` object corresponding to a spell effect index (0, 1, or 2). This is the primary way external systems access the detailed effect logic.
*   **GetSpellProto**: Returns the `SpellEntry` (DBC data) for the spell, allowing access to spell name, icon, and raw properties.
*   **GetAuraScript**: Returns the custom C++ script attached to the spell, if any.

### Caster and Target Identification
*   **GetCasterGuid / SetCasterGuid**: Identifies the direct caster of the spell.
*   **GetRealCasterGuid / SetRealCasterGuid**: Identifies the ultimate source of the spell (e.g., a pet's owner if the pet cast the spell).
*   **GetCastItemGuid**: Identifies the item used to cast the spell, if applicable.
*   **GetTarget / SetTarget**: Identifies the unit currently affected by the aura. Note that for some auras (like stolen buffs), the target might change, hence the setter.

### State Flags and Persistence
*   **IsPermanent / SetPermanent**: Marks the aura as permanent (no duration countdown). Permanent auras are not removed by normal expiration.
*   **IsPassive / SetPassive**: Marks the aura as passive. Passive auras are typically not shown in the standard aura bar and may have different removal rules.
*   **IsDeathPersistent**: If true, the aura survives the death of the target.
*   **IsRemovedOnShapeLost**: If true, the aura is removed when the target leaves a shapeshift form (e.g., Bear Form).
*   **IsSingleTarget / SetIsSingleTarget**: Indicates if the aura is tied to a single target. This affects how it is saved/loaded and removed.
*   **IsChanneled**: Indicates if the aura is part of a channeled spell. Channeled auras are tightly coupled with the spell's active state.
*   **IsTriggered / SetTriggered**: Marks if the aura was applied by a triggered spell (e.g., a proc). This affects debuff priority.
*   **IsReflected / SetReflected**: Marks if the aura was applied by a reflected spell. This prevents certain interactions, such as killing a dueling opponent with a reflected spell.
*   **IsAddedBySpell / SetAddedBySpell**: Distinguishes between auras applied by a direct spell cast versus those added programmatically (e.g., by scripts or item effects).

### Duration and Timing
*   **GetAuraMaxDuration / SetAuraMaxDuration**: The total possible duration of the aura.
*   **GetAuraDuration / SetAuraDuration**: The remaining time before the aura expires.
*   **GetAuraApplyTime**: The timestamp when the aura was applied, used for calculating elapsed time.
*   **GetAuraSlot / SetAuraSlot**: The visual slot index on the client's UI where the aura icon appears.

### Removal and Cleanup
*   **SetRemoveMode / GetRemoveMode**: Stores the reason for removal (e.g., expired, dispelled, killed). This is used for logging and specific cleanup logic.
*   **SetDeleted / IsDeleted**: Marks the holder for deletion. The actual destruction happens asynchronously to avoid iterator invalidation during iteration.
*   **SetInUse / IsInUse**: A reference counter used to protect the holder from being deleted while it is actively being processed (e.g., during a tick or modifier application).

### Visual and Limit Management
*   **IsAffectedByVisibleSlotLimit**: Determines if the aura counts towards the client's limit on visible aura icons.
*   **CalculateForBuffLimit / CalculateForDebuffLimit**: Logic to determine if the aura should be removed due to buff/debuff limits (e.g., only X positive buffs allowed).
*   **Refresh**: Attempts to refresh an existing aura with a new cast. This involves checking if the new cast can replace the old one (based on caster, spell ID, and rank) and updating duration/stacks accordingly.
*   **CanBeRefreshedBy**: Helper to determine if another holder can refresh this one.

### Special Mechanics
*   **CalculateHeartBeat**: Specific logic for PvP heartbeats (likely related to threat or aggro mechanics in PvP zones).
*   **SetTargetSecondaryThreatFocus / IsTargetSecondaryThreatFocus**: Marks if the aura makes the target a secondary threat focus (relevant for tanking mechanics).

## Cross-Unit Boundaries

`SpellAuraHolder` is heavily integrated with `Unit`, `Spell`, and `Aura` classes.

*   **Called by `Spell.Main/DoSpellHitOnUnit`**: When a spell hits a target, `Spell` creates or updates a `SpellAuraHolder`. It calls `setDiminishGroup`, `setDiminishLevel`, `SetTriggered`, `SetReflected`, and `SetAddedBySpell` to initialize the holder's state based on the spell's properties and context.
*   **Called by `Unit.SpellAuras/_AddSpellAuraHolder` and `_RemoveSpellAuraHolder`**: These methods in `Unit` manage the list of holders on a unit. They call `getDiminishGroup` to update DR trackers and `GetAuraByEffectIndex` to add/remove the individual `Aura` effects from the unit's modifier lists.
*   **Called by `Unit.Main/AddSpellAuraHolder`**: Adds the holder to the unit's internal list. It calls `GetStackAmount`, `GetAuraByEffectIndex`, `GetSpellProto`, `GetCasterGuid`, `GetCastItemGuid`, `IsAffectedByVisibleSlotLimit`, `IsPermanent`, `IsPassive`, `IsSingleTarget`, and `GetTarget` to validate and register the aura.
*   **Called by `Unit.SpellAuras/Update`**: The main game loop update function for auras. It calls `UpdateHolder` on each holder, which in turn ticks down durations and processes periodic effects.
*   **Called by `Player.Main/SaveAura` and `Pet.Main/_SaveAuras`**: Persists the aura state to the database. It calls `GetStackAmount`, `GetAuraByEffectIndex`, `GetSpellProto`, `GetCasterGuid`, `GetCastItemGuid`, `IsPassive`, `IsSingleTarget`, `GetAuraMaxDuration`, `GetAuraDuration`, and `GetAuraCharges` to serialize the necessary fields.
*   **Called by `Spell.Effects/EffectDispel`**: When a dispel spell is cast, it iterates through the target's holders. It calls `GetSpellProto` and `GetAuraByEffectIndex` to check if the aura is dispellable and to remove it.

## Data Model

This unit does not directly interact with database tables. It relies on `Player.Main/SaveAura` and `Pet.Main/_SaveAuras` to persist its state. The relevant tables are `character_aura` (for players) and `pet_aura` (for pets), but `SpellAuraHolder` itself contains no SQL logic.

## Notable Implementation Details

1.  **Reference Counting for Safety**: The `m_in_use` member is a critical safety mechanism. Since aura updates happen during iteration over the unit's aura list, deleting a holder immediately could crash the server. `SetInUse(true)` is called before processing, and `SetInUse(false)` after. The holder is only destroyed when `m_in_use` reaches zero and `IsDeleted()` is true.
2.  **Indirect Item References**: `m_castItemGuid` stores the GUID of the casting item, not a pointer. This is explicitly noted in the comments because items can be deleted (e.g., consumed or unequipped) while the aura persists. Using a pointer would lead to dangling references.
3.  **Refresh Logic Complexity**: The `Refresh` method is complex because it must handle various edge cases: different casters, different ranks, stacking vs. non-stacking, and diminishing returns. It coordinates with `CanBeRefreshedBy` to decide whether to replace the existing holder or create a new one.
4.  **Visual Slot Limits**: The `m_visibleSlotLimitAffected` flag and `m_visibleSlotLimitScore` are used to prioritize which auras are displayed on the client when the limit is reached. Higher scores mean the aura is more likely to be shown.
5.  **Diminishing Returns Integration**: The holder stores DR group and level, but the actual DR tracking (timers, counts) is managed by the `Unit` class. The holder simply provides the metadata needed for the `Unit` to update its DR state.

## Member Reference

**setDiminishGroup**: Sets the diminishing returns group for the aura.
**setDiminishLevel**: Sets the diminishing returns level for the aura.
**getDiminishGroup**: Returns the diminishing returns group.
**GetStackAmount**: Returns the current number of stacks.
**GetAuraByEffectIndex**: Returns the `Aura` object for a specific effect index.
**GetSpellProto**: Returns the `SpellEntry` for the spell.
**GetAuraScript**: Returns the custom script attached to the spell.
**GetCasterGuid**: Returns the GUID of the direct caster.
**SetCasterGuid**: Sets the GUID of the direct caster.
**GetRealCasterGuid**: Returns the GUID of the ultimate caster.
**SetRealCasterGuid**: Sets the GUID of the ultimate caster.
**GetCastItemGuid**: Returns the GUID of the casting item.
**GetTarget**: Returns the target unit.
**SetTarget**: Sets the target unit.
**IsAffectedByVisibleSlotLimit**: Checks if the aura counts towards the visual slot limit.
**IsPermanent**: Checks if the aura is permanent.
**SetPermanent**: Sets the permanent flag.
**IsPassive**: Checks if the aura is passive.
**SetPassive**: Sets the passive flag.
**IsDeathPersistent**: Checks if the aura persists on death.
**IsRemovedOnShapeLost**: Checks if the aura is removed on shapeshift loss.
**SetRemovedOnShapeLost**: Sets the removed-on-shape-lost flag.
**IsInUse**: Checks if the holder is currently being processed.
**IsDeleted**: Checks if the holder is marked for deletion.
**SetDeleted**: Marks the holder for deletion.
**SetInUse**: Increments or decrements the usage counter.
**UpdateHolder**: Updates the holder's state, including ticking down duration.
**IsSingleTarget**: Checks if the aura is single-target.
**SetIsSingleTarget**: Sets the single-target flag.
**IsChanneled**: Checks if the aura is channeled.
**GetAuraMaxDuration**: Returns the maximum duration.
**GetAuraDuration**: Returns the remaining duration.
**SetAuraDuration**: Sets the remaining duration.
**GetAuraSlot**: Returns the visual slot index.
**SetAuraSlot**: Sets the visual slot index.
**GetAuraLevel**: Returns the aura level.
**SetAuraLevel**: Sets the aura level.
**GetAuraCharges**: Returns the remaining charges.
**SetAuraCharges**: Sets the remaining charges.
**DropAuraCharge**: Decrements the charge count.
**GetAuraApplyTime**: Returns the time the aura was applied.
**SetRemoveMode**: Sets the removal reason.
**GetRemoveMode**: Returns the removal reason.
**SetLoadedState**: Initializes the holder's state from loaded data.
**SetTargetSecondaryThreatFocus**: Sets the secondary threat focus flag.
**IsTargetSecondaryThreatFocus**: Checks the secondary threat focus flag.
**SetTriggered**: Sets the triggered flag.
**IsTriggered**: Checks the triggered flag.
**SetReflected**: Sets the reflected flag.
**IsReflected**: Checks the reflected flag.
**SetAddedBySpell**: Sets the added-by-spell flag.
**IsAddedBySpell**: Checks the added-by-spell flag.

---

<!-- machine-true, projected from graph.json -->

## Map — SpellAuraHolder

*Source:* SpellAuras.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| setDiminishGroup | method | — | Spell.Main/DoSpellHitOnUnit | — |
| setDiminishLevel | method | — | Spell.Main/DoSpellHitOnUnit | — |
| getDiminishGroup | method | — | Unit.SpellAuras/_AddSpellAuraHolder, Unit.SpellAuras/_RemoveSpellAuraHolder | — |
| GetStackAmount | method | — | boss_four_horsemen/SpellHitTarget, CreatureEventAI/ProcessEvent, darkshore/DoAttack, instance_zulgurub/UpdateHakkarPowerStacks, Pet.Main/_SaveAuras, Player.Main/SaveAura, Spell.Effects/EffectDispel, Unit.Main/AddSpellAuraHolder | — |
| GetAuraByEffectIndex | method | — | boss_four_horsemen/SpellHitTarget, ChatHandler.UnitCommands/HandleListAurasCommand, Pet.Main/_SaveAuras, Player.Main/SaveAura, Spell.Main/CheckCasterAuras, Unit.Main/AddSpellAuraHolder, Unit.Main/GetAura#2, Unit.Main/HandleTriggers, Unit.Main/HasAura, Unit.Main/ProcDamageAndSpellFor, Unit.Main/RemoveNoStackAurasDueToAuraHolder, Unit.Main/RemoveSingleAuraFromSpellAuraHolder, Unit.SpellAuras/ApplyAuraModifiers, Unit.SpellAuras/Refresh, Unit.SpellAuras/Refresh#2, Unit.SpellAuras/RefreshAuraPeriodicTimers, Unit.SpellAuras/SetAuraFlag, Unit.SpellAuras/Update | — |
| GetSpellProto | method | — | ChatHandler.UnitCommands/HandleListAurasCommand, CombatBotBaseAI/IsValidDispelTarget, Conditions/Evaluate, Creature.Main/RemoveAurasAtReset, DynamicObject/Delay, Pet.Main/_SaveAuras, Player.Main/SaveAura, Player.Main/UpdateAreaDependentAuras, Spell.Effects/EffectDispel, Spell.Effects/EffectDispelMechanic, Spell.Effects/EffectDummy, Spell.Main/CheckCast, Spell.Main/CheckCasterAuras, Unit.AuraProcHandler/IsTriggeredAtSpellProcEvent, Unit.Main/AddSpellAuraHolder, Unit.Main/DealMeleeDamage, Unit.Main/HandleTriggers, Unit.Main/IsImmuneToSpell, Unit.Main/ModifyAuraState, Unit.Main/ProcDamageAndSpellFor, Unit.Main/RemoveAurasAtMechanicImmunity, Unit.Main/RemoveAurasWithAttribute, Unit.Main/RemoveAurasWithDispelType, Unit.Main/RemoveAurasWithInterruptFlags, Unit.Main/RemoveNoStackAurasDueToAuraHolder, Unit.SpellAuras/GetMiscValue, Unit.SpellAuras/HandleAuraModSchoolImmunity, Unit.SpellAuras/HandleFeignDeath, Unit.SpellAuras/HandleSpellSpecificBoosts, Unit.SpellAuras/IsWeaponBuffCoexistableWith, Unit.SpellAuras/PeriodicTick, Unit.SpellAuras/ReapplyAffectedPassiveAuras#2, Unit.SpellAuras/SetAuraMaxDuration, Unit.SpellAuras/SetStackAmount, Unit.SpellAuras/SpellAuraHolder, Unit.SpellAuras/TriggerSpell, Unit.SpellAuras/UnregisterSingleCastHolder, Unit.SpellAuras/Update#4, Unit.SpellAuras/_AddSpellAuraHolder, Unit.SpellAuras/_RemoveSpellAuraHolder, WorldSession.SpellHandler/HandleCancelAuraOpcode | — |
| GetAuraScript | method | — | Unit.AuraProcHandler/IsTriggeredAtSpellProcEvent, Unit.Main/HandleTriggers, Unit.SpellAuras/Aura, Unit.SpellAuras/CreateAura | — |
| GetCasterGuid | method | — | ChatHandler.UnitCommands/HandleListAurasCommand, Creature.Main/RemoveAurasAtReset, Pet.Main/_SaveAuras, Player.Main/DuelComplete, Player.Main/RemoveItemDependentAurasAndCasts, Player.Main/SaveAura, Spell.Effects/EffectDispel, Unit.AuraProcHandler/IsTriggeredAtSpellProcEvent, Unit.Main/AddSpellAuraHolder, Unit.Main/DealMeleeDamage, Unit.Main/DelaySpellAuraHolder, Unit.Main/ProcDamageAndSpellFor, Unit.Main/RemoveAuraHolderFromStack, Unit.Main/RemoveAurasByCasterSpell, Unit.Main/RemoveAurasWithDispelType, Unit.Main/RemoveNoStackAurasDueToAuraHolder, Unit.Main/RemoveNotOwnSingleTargetAuras, Unit.SpellAuras/CalculateForBuffLimit, Unit.SpellAuras/CanBeRefreshedBy, Unit.SpellAuras/GetCaster, Unit.SpellAuras/GetRealCaster, Unit.SpellAuras/HandleSpellSpecificBoosts, Unit.SpellAuras/IsWeaponBuffCoexistableWith, Unit.SpellAuras/ReapplyAffectedPassiveAuras#2, Unit.SpellAuras/Update, Unit.SpellAuras/Update#4, WorldSession.SpellHandler/HandleCancelAuraOpcode | — |
| SetCasterGuid | method | — | — | — |
| GetRealCasterGuid | method | — | Unit.Main/GetSpellAuraHolder, Unit.SpellAuras/GetRealCaster | — |
| SetRealCasterGuid | method | — | — | — |
| GetCastItemGuid | method | — | Pet.Main/_SaveAuras, Player.Main/ApplyEquipSpell, Player.Main/SaveAura, Unit.Main/AddSpellAuraHolder, Unit.Main/RemoveAurasDueToItemSpell, Unit.Main/RemoveSingleAuraDueToItemSet, Unit.SpellAuras/CalculateForBuffLimit, Unit.SpellAuras/IsWeaponBuffCoexistableWith, Unit.SpellAuras/ReapplyAffectedPassiveAuras#2 | — |
| GetTarget | method | — | Spell.Main/SendChannelUpdate, Unit.AuraProcHandler/IsTriggeredAtSpellProcEvent, Unit.Main/AddSpellAuraHolder, Unit.SpellAuras/CalculateForBuffLimit, Unit.SpellAuras/GetTriggerTarget, Unit.SpellAuras/HandleCastOnAuraRemoval, Unit.SpellAuras/SetStackAmount, Unit.SpellAuras/Update, Unit.SpellAuras/Update#4 | — |
| SetTarget | method | — | — | — |
| IsAffectedByVisibleSlotLimit | method | — | Unit.Main/AddSpellAuraHolder, Unit.Main/GetVisibleAurasCount, Unit.Main/RemoveAuraDueToVisibleSlotLimit | — |
| IsPermanent | method | — | Creature.Main/RemoveAurasAtReset, Unit.Main/_UpdateSpells, Unit.SpellAuras/CalculateForBuffLimit, Unit.SpellAuras/ReapplyAffectedPassiveAuras#2, Unit.SpellAuras/Update | — |
| SetPermanent | method | — | Unit.Main/AddAura, Unit.SpellAuras/SetAuraMaxDuration | — |
| IsPassive | method | — | ChatHandler.UnitCommands/HandleListAurasCommand, Pet.Main/_SaveAuras, Player.Main/RemoveItemDependentAurasAndCasts, Player.Main/SaveAura, Unit.Main/AddSpellAuraHolder, Unit.Main/RemoveAllAurasOnDeath, Unit.Main/RemoveAuraTypeOnDeath, Unit.SpellAuras/ComputeExclusive, Unit.SpellAuras/ReapplyAffectedPassiveAuras#2, Unit.SpellAuras/SetAuraMaxDuration, Unit.SpellAuras/Update | — |
| SetPassive | method | — | Unit.Main/AddAura | — |
| IsDeathPersistent | method | — | Unit.Main/RemoveAllAurasOnDeath, Unit.Main/RemoveAuraTypeOnDeath | — |
| IsRemovedOnShapeLost | method | — | Unit.SpellAuras/HandleShapeshiftBoosts | — |
| SetRemovedOnShapeLost | method | — | spell_warrior/OnHolderInit | — |
| IsInUse | method | — | Unit.Main/CleanupDeletedAuras, Unit.Main/DeleteAuraHolder, Unit.Main/RemoveAuraDueToVisibleSlotLimit, Unit.Main/RemoveNoStackAurasDueToAuraHolder | — |
| IsDeleted | method | — | Spell.Main/CheckCast, Spell.Main/SendChannelUpdate, Spell.Main/update, Unit.Main/AddSpellAuraHolder, Unit.Main/HandleTriggers, Unit.Main/ProcDamageAndSpellFor, Unit.SpellAuras/ApplyAuraModifiers, Unit.SpellAuras/HandleSpellSpecificBoosts, Unit.SpellAuras/ReapplyAffectedPassiveAuras#2, Unit.SpellAuras/Update | — |
| SetDeleted | method | — | Unit.Main/DeleteAuraHolder | — |
| SetInUse | method | — | Spell.Main/AddChanneledAuraHolder, Spell.Main/RemoveChanneledAuraHolder, Spell.Main/SendChannelUpdate, Spell.Main/update, Unit.Main/HandleTriggers, Unit.Main/ProcDamageAndSpellFor, Unit.SpellAuras/ApplyModifier, Unit.SpellAuras/HandleSpellSpecificBoosts, Unit.SpellAuras/Update | — |
| UpdateHolder | method | — | Spell.Main/update, Unit.Main/_UpdateSpells | — |
| IsSingleTarget | method | — | Pet.Main/_SaveAuras, Player.Main/LoadAura, Player.Main/SaveAura, Unit.Main/AddSpellAuraHolder, Unit.Main/RemoveNotOwnSingleTargetAuras, Unit.SpellAuras/UnregisterSingleCastHolder | — |
| SetIsSingleTarget | method | — | Player.Main/LoadAura | — |
| IsChanneled | method | — | Spell.Main/AddChanneledAuraHolder, Unit.Main/RemoveSpellAuraHolder, Unit.Main/_UpdateSpells | — |
| GetAuraMaxDuration | method | — | boss_grobbulus/OnSetTargetMap, Pet.Main/_SaveAuras, Player.Main/GetMirrorTimerBuff, Player.Main/SaveAura, Player.Main/UpdateMirrorTimers, Spell.Main/DoSpellHitOnUnit, Unit.SpellAuras/Refresh#2, Unit.SpellAuras/RefreshHolder | — |
| GetAuraDuration | method | — | boss_grobbulus/OnSetTargetMap, Pet.Main/_SaveAuras, Player.Main/SaveAura, Player.Main/UpdateMirrorTimers, Spell.Main/update, Unit.Main/DelaySpellAuraHolder, Unit.Main/_UpdateSpells, Unit.SpellAuras/Refresh#2, Unit.SpellAuras/UpdateAuraDuration | — |
| SetAuraDuration | method | — | boss_vaelastrasz/UpdateAI, ChatHandler.AccountCommands/HandleMuteCommand, ChatHandler.UnitCommands/HandleAuraHelper, Player.Main/ResurrectPlayer, Spell.Main/DoSpellHitOnUnit, Unit.Main/DelaySpellAuraHolder, Unit.Main/RefreshAura, Unit.SpellAuras/PeriodicDummyTick, Unit.SpellAuras/PeriodicTick, Unit.SpellAuras/RefreshHolder, Unit.SpellAuras/Update | — |
| GetAuraSlot | method | — | Unit.SpellAuras/UpdateAuraDuration, Unit.SpellAuras/_RemoveSpellAuraHolder | — |
| SetAuraSlot | method | — | Unit.SpellAuras/_AddSpellAuraHolder | — |
| GetAuraLevel | method | — | — | — |
| SetAuraLevel | method | — | — | — |
| GetAuraCharges | method | — | Pet.Main/_SaveAuras, Player.Main/SaveAura, spell_mage/OnProc, Unit.AuraProcHandler/HandleHasteAuraProc, Unit.Main/HandleTriggers, Unit.SpellAuras/HandleAddModifier | — |
| SetAuraCharges | method | — | Unit.SpellAuras/HandleAddModifier, Unit.SpellAuras/HandleAuraProcTriggerSpell | — |
| DropAuraCharge | method | — | SpellCaster/SelectMagnetTarget, Unit.Main/CalculateDamageAbsorbAndResist, Unit.Main/HandleTriggers | — |
| GetAuraApplyTime | method | — | Player.Main/DuelComplete, Unit.Main/ProcDamageAndSpellFor | — |
| SetRemoveMode | method | — | Unit.Main/RemoveSpellAuraHolder | — |
| GetRemoveMode | method | — | Spell.Main/update, spell_special/OnAfterApply#4, Unit.SpellAuras/HandleCastOnAuraRemoval | — |
| SetLoadedState | method | — | Pet.Main/_LoadAuras, Player.Main/LoadAura | — |
| SetTargetSecondaryThreatFocus | method | — | — | — |
| IsTargetSecondaryThreatFocus | method | — | Unit.Main/IsSecondaryThreatTarget | — |
| SetTriggered | method | — | Spell.Main/DoSpellHitOnUnit | — |
| IsTriggered | method | — | Unit.SpellAuras/CalculateForDebuffLimit | — |
| SetReflected | method | — | Spell.Main/DoSpellHitOnUnit | — |
| IsReflected | method | — | Player.Main/DuelComplete, Unit.SpellAuras/PeriodicTick | — |
| SetAddedBySpell | method | — | Spell.Main/DoSpellHitOnUnit | — |
| IsAddedBySpell | method | — | Unit.SpellAuras/HandleAuraModDecreaseSpeed, Unit.SpellAuras/HandleAuraModIncreaseSpeed | — |
