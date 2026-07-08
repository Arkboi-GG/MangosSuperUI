# ThreatManager

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ThreatManager

## Purpose & Responsibilities

`ThreatManager` is the core subsystem responsible for managing combat aggression and target selection for `Creature` entities (NPCs, mobs, bosses) in the WoWVMaNGOS server. It implements the "Threat" mechanic, where creatures track how much "hate" they hold towards various units (primarily players and pets) based on damage dealt, healing received, and specific spell effects.

The system maintains two distinct lists of hostile references:
1.  **Online List (`iThreatContainer`)**: Contains targets that are currently valid candidates for attack (alive, reachable, not immune, not GM, etc.). This list is kept sorted by threat level to facilitate efficient target selection.
2.  **Offline List (`iThreatOfflineContainer`)**: Contains targets that are technically hostile but currently invalid for attack (e.g., flying taxis, game masters, dead units, or units out of range). These units retain their threat values but are excluded from immediate target selection until their status changes.

Key responsibilities include:
*   Calculating raw threat values modified by spells, auras, and critical hits.
*   Maintaining bidirectional links between the attacker (`ThreatManager` owner) and the target (`HostileReference`).
*   Dynamically updating the "current victim" based on threat thresholds (110% for melee, 130% for ranged) and validity checks.
*   Handling special mechanics like Taunts (temporary threat boosts) and Assist threats.
*   Providing interfaces for AI scripts and spells to query, modify, or reset threat states.

## Member-by-Member Behavior

### Threat Calculation & Modification

**`ThreatCalcHelper::CalcThreat`**
A static helper function that computes the final threat value to be added. It applies:
1.  Spell-specific threat modifiers via `Unit.Main/ApplyTotalThreatModifier`.
2.  Critical hit threat multipliers if the attack was a crit, using `Unit.Main/GetTotalAuraMultiplierByMiscMask`.
3.  Returns 0 if the base threat is 0.

**`ThreatManager::addThreat`**
The primary entry point for adding threat. It performs several validation checks before delegating to `addThreatDirectly`:
*   Ignores self-threat.
*   Ignores Game Masters (`Player.Main/IsGameMaster`).
*   Ignores dead units or if the owner is dead.
*   Asserts the owner is a `Creature` (`TYPEID_UNIT`).
*   For assist threats (`isAssistThreat`), it zeroes out threat if the owner is under hard crowd control (confused, fleeing, isolated, or breakable stun).
*   Calculates the final threat using `ThreatCalcHelper::CalcThreat` and calls `addThreatDirectly`.

**`ThreatManager::addThreatDirectly`**
Adds threat to the internal containers without recalculating modifiers.
*   Validates the victim is alive and in the same map as the owner.
*   Attempts to find an existing `HostileReference` in the online container.
*   If not found, searches the offline container.
*   If no reference exists and `noNew` is false, creates a new `HostileReference`.
*   Special handling for Game Masters: if the victim is a GM, the reference is created but immediately marked as offline (`setOnlineOfflineState(false)`).

**`ThreatManager::modifyThreatPercent`**
Delegates to `ThreatContainer::modifyThreatPercent` to adjust threat by a percentage. If the percentage is less than -100%, it removes the reference entirely.

**`HostileReference::addThreat`**
Increments the internal threat counter.
*   Clamps threat to a minimum of 0.
*   If the reference was previously offline, it triggers `updateOnlineStatus` to potentially bring it back online.
*   Fires a `ThreatRefStatusChangeEvent` to notify the `ThreatManager` of the change.
*   **Pet Assist Logic**: If the victim has an owner (e.g., a pet) and that owner is targetable by the attacker, it adds 0 threat to the owner. This ensures the owner appears in the threat list, facilitating proper aggro transfer if the pet dies or despawns.

### Target Selection & State Management

**`ThreatContainer::selectNextVictim`**
The complex logic for choosing the next target. It iterates through the sorted threat list twice if necessary:
1.  **First Pass**: Selects high-priority targets. Skips units that are:
    *   Out of threat area.
    *   Invalid attack targets.
    *   Immune to the attacker's damage school.
    *   Secondary threat targets (feared, gouged, etc.).
    *   Unreachable if the attacker is immobilized (rooted casters/meleers).
2.  **Second Pass**: If no suitable target was found, it repeats the loop allowing "low priority" targets (those skipped in the first pass).
3.  **Threshold Logic**:
    *   If there is a `currentVictim`, it only switches if the new target has >130% threat (ranged/general) OR >110% threat AND is in melee range.
    *   If no `currentVictim` exists, it selects the first valid target found.

**`ThreatManager::getHostileTarget`**
Triggers an update of the threat list (sorting if dirty) and calls `selectNextVictim`. It updates the `iCurrentVictim` pointer and returns the `Unit` associated with the selected reference.

**`ThreatManager::tauntApply`**
Implements the taunt mechanic. If the taunter is on the threat list and has lower threat than the current victim, it sets a temporary threat modifier (`iTempThreatModifyer`) equal to the current victim's threat. This effectively boosts the taunter's threat to match the current victim, forcing a target switch if the taunt is valid.

**`ThreatManager::tauntFadeOut`**
Resets the temporary threat modifier for the specified unit, removing the taunt effect.

**`HostileReference::updateOnlineStatus`**
Determines if a reference should be online or offline.
*   Re-links the target if the reference was invalid.
*   Sets `online = true` if the target is valid, not a GM, and not taxi-flying.
*   Note: The code sets `accessible = false` initially in this function, though `setAccessibleState` is called separately. The logic implies accessibility is a stricter subset of online status.

### Event Processing & Lifecycle

**`ThreatManager::processThreatEvent`**
Handles events fired by `HostileReference` objects:
*   **`UEV_THREAT_REF_THREAT_CHANGE`**: Marks the list as dirty if the current victim's threat decreases or a non-victim's threat increases, indicating a potential reorder.
*   **`UEV_THREAT_REF_ONLINE_STATUS`**: Moves references between `iThreatContainer` and `iThreatOfflineContainer`. If the current victim goes offline, it clears the victim and marks the list dirty. If a reference comes online with higher threat than the current victim (by 10%), it marks the list dirty.
*   **`UEV_THREAT_REF_REMOVE_FROM_LIST`**: Removes the reference from the appropriate container. Clears the current victim if it was removed.

**`HostileReference::removeReference`**
Invalidates the reference and fires a removal event. This is typically called when the target unit dies or despawns.

**`ThreatManager::clearReferences`**
Destroys all references in both online and offline containers and resets the current victim. Used when the creature dies or resets.

### Accessors & Utilities

**`ThreatManager::getThreat`**
Returns the threat value for a specific unit. Optionally searches the offline list if the unit is not found online.

**`ThreatManager::getThreatList`**
Returns a constant reference to the online threat list. Used extensively by AI scripts and spells to iterate over targets.

**`ThreatManager::isThreatListEmpty`**
Checks if the online container is empty.

**`ThreatManager::getCurrentVictim`**
Returns the `HostileReference` of the current target.

**`ThreatManager::setCurrentVictimIfCan`**
Sets the current victim to a specific unit if a reference exists in the online container. Used by Taunt spells.

**`HostileReference::getSourceUnit`**
Returns the owner of the `ThreatManager` (the attacker).

**`ThreatContainer::getReferenceByTarget`**
Linear search through the threat list to find a reference by `Unit` pointer.

**`HostileReferenceSortPredicate`**
A global function used to sort the threat list in descending order of threat.

## Cross-Unit Boundaries

### Collaboration with `Unit` and `Player`
*   **`Unit.Main`**: `ThreatManager` relies heavily on `Unit` methods for validity checks (`IsAlive`, `IsTargetableBy`, `GetOwner`, `HasUnitState`, `IsImmuneToDamage`, `CanReachWithMeleeAutoAttack`, `GetMaxChaseDistance`). It also uses `Unit` for threat calculation modifiers (`ApplyTotalThreatModifier`, `GetSpellModOwner`).
*   **`Player.Main`**: Checks `IsGameMaster` to exclude GMs from threat lists and `IsTaxiFlying` to determine online status.

### Collaboration with `Creature`
*   **`Creature.Main`**: `selectNextVictim` casts the owner to `Creature` to access `IsOutOfThreatArea` and `GetMeleeDamageSchoolMask`. Many AI scripts (`boss_*`, `ScriptedAI`) call `ThreatManager` methods to manipulate threat or select targets.

### Collaboration with `HostileRefManager`
*   **`HostileRefManager`**: This unit manages the reverse side of the relationship (who hates me). `ThreatManager` calls `HostileRefManager` methods indirectly via `HostileReference` link management (`AddHatedBy`, `RemoveHatedBy`). `HostileRefManager` calls `ThreatManager::updateOnlineStatus` and `setOnlineOfflineState` to synchronize states.

### Collaboration with Spells & Effects
*   **`Spell.Effects`**: Various spell effects (`EffectTaunt`, `EffectSanctuary`, `EffectModifyThreatPercent`, `EffectScriptEffect`) interact directly with `ThreatManager` to apply taunts, remove threat, or modify percentages.

### Collaboration with AI Scripts
*   **Boss AIs**: Numerous boss scripts (`boss_ayamiss`, `boss_buru`, etc.) call `addThreat`, `modifyThreatPercent`, `getThreat`, and `getThreatList` to implement complex encounter mechanics.
*   **Generic AIs**: `PetAI`, `ScriptedAI`, and `Unit.Main` use `getHostileTarget`, `isThreatListEmpty`, and `getThreatList` for standard combat behavior.

## Data Model

This unit does not interact directly with database tables. All threat data is held in memory within `std::list<HostileReference*>` structures inside `ThreatContainer` instances.

## Notable Implementation Details

1.  **Dual Container System**: The separation into `iThreatContainer` (online) and `iThreatOfflineContainer` (offline) is crucial. It allows the system to maintain threat history for units that temporarily become invalid (e.g., a player flying away) without losing their accumulated threat. When they return, they re-enter the online list with their previous threat value.
2.  **Lazy Sorting**: The threat list is not sorted after every threat addition. Instead, a `iDirty` flag is set. Sorting occurs only when `getHostileTarget` is called or explicitly requested. This optimizes performance during high-threat-generation phases.
3.  **Taunt Implementation**: Taunts are implemented via a temporary threat modifier (`iTempThreatModifyer`) stored in `HostileReference`. This allows the taunt to expire naturally when `tauntFadeOut` is called, reverting the threat to its pre-taunt value.
4.  **Pet Assist Aggro**: The logic in `HostileReference::addThreat` that adds 0 threat to the pet's owner is a subtle but important detail. It ensures that if a pet is attacking, the master is aware of the threat context, even if the master hasn't dealt damage themselves.
5.  **GM Exclusion**: Game Masters are explicitly excluded from threat calculations and are always placed in the offline container if a reference is created. This prevents NPCs from targeting GMs in combat.
6.  **Threshold Switching**: The 110%/130% rule for target switching is hardcoded in `selectNextVictim`. Melee attackers can switch targets with a smaller threat differential (110%) if the target is in melee range, while ranged/general targets require a larger differential (130%). This encourages melee DPS to maintain aggro more tightly.
7.  **Hard CC Assist Suppression**: `addThreat` suppresses assist threat if the owner is under hard crowd control. This prevents NPCs from gaining aggro on players who are assisting against a controlled enemy, which could lead to unintended pulls or aggro spikes.

## Member Reference

**`CalcThreat`**: Static helper in `ThreatCalcHelper` that calculates final threat value applying spell mods and crit multipliers. Calls `Unit.Main/ApplyTotalThreatModifier`, `Unit.Main/GetSpellModOwner`, `Unit.Main/GetTotalAuraMultiplierByMiscMask`.

**`HostileReference`**: Constructor for `HostileReference`. Initializes threat, temp modifier, and links to target/source. Calls `Object/GetObjectGuid`.

**`targetObjectBuildLink`**: Notifies the target unit that a hostile reference has been created. Calls `Unit.Main/AddHatedBy`.

**`targetObjectDestroyLink`**: Notifies the target unit that the hostile reference is being destroyed. Calls `Unit.Main/RemoveHatedBy`.

**`sourceObjectDestroyLink`**: Handles cleanup when the source unit (attacker) is destroyed. Sets online state to false.

**`fireStatusChanged`**: Fires a `ThreatRefStatusChangeEvent` to the source unit (`ThreatManager`) to notify of status changes.

**`addThreat`**: Adds threat to the reference, clamps to 0, updates online status if needed, fires event, and handles pet assist logic. Calls `HostileReference/isOnline`, `ThreatRefStatusChangeEvent/ThreatRefStatusChangeEvent#4`, `Unit.Main/GetOwner`, `Unit.Main/IsTargetableBy`.

**`updateOnlineStatus`**: Determines if the reference should be online/offline based on target validity, GM status, and taxi flying. Calls `HostileReference/getUnitGuid`, `Object/GetTypeId`, `ObjectAccessor/GetUnit`, `Player.Main/IsGameMaster`, `Unit.Main/IsTaxiFlying`. Called by `HostileRefManager/updateThreatTables`.

**`setOnlineOfflineState`**: Sets the online flag and fires an event. If going offline, also sets accessible to false. Calls `ThreatRefStatusChangeEvent/ThreatRefStatusChangeEvent#2`. Called by `HostileRefManager/setOnlineOfflineState`, `HostileRefManager/setOnlineOfflineState#2`.

**`setAccessibleState`**: Sets the accessible flag and fires an event. Calls `ThreatRefStatusChangeEvent/ThreatRefStatusChangeEvent#2`.

**`removeReference`**: Invalidates the reference and fires a removal event. Calls `ThreatRefStatusChangeEvent/ThreatRefStatusChangeEvent#2`. Called by `HostileRefManager/deleteReference`, `HostileRefManager/deleteReferences`, `HostileRefManager/deleteReferencesForFaction`, `Spell.Effects/EffectSanctuary`.

**`~ThreatManager`**: Destructor. Clears all references.

**`addThreat#3`**: Overload of `addThreat` taking only `Unit*` and `float`. Delegates to the main `addThreat`. Called by `boss_ayamiss/UpdateAI#2`, `boss_buru/UpdateAI`, `boss_fankriss/UpdateAI#3`, `boss_mandokir/UpdateAI`, `boss_ossirian/UpdateAI`, `boss_thaddius/HandleMagneticPull`, `boss_twinemperors/UpdateAI`, `Spell.Effects/EffectScriptEffect`, `Spell.Effects/EffectTaunt`.

**`getSourceUnit`**: Returns the owner of the `ThreatManager` (the attacker). Called by `AiBotAI.Combat/SelectAttackTarget`, `AiBotAI.Grind/SelectGrindTarget`, `BattleBotAI.Main/SelectAttackTarget`, `ChatHandler.UnitCommands/HandleListHostileRefsCommand`, `Player.Main/LeaveCombatWithFarAwayCreatures`, `Unit.Main/FindLowestHpFriendlyUnit`, `Unit.SpellAuras/HandleFeignDeath`.

**`isThreatListEmpty`**: Checks if the online threat container is empty. Called by `boss_gothik/UpdateAI`, `boss_heigan/UpdateAI`, `boss_noth/UpdateAI`, `boss_patchwerk/CustomGetTarget`, `boss_razorgore/UpdateAI#2`, `boss_sapphiron/UpdateAI`, `boss_thaddius/UpdateAI`, `custom_creatures/UpdateAI#2`, `PetAI/SelectNextTarget`, `PetAI/UpdateAI`, `PetEventAI/FindTargetForAttack`, `ScriptedAI/EnterVanish`, `Unit.Main/DoResetThreat`, `Unit.Main/SelectHostileTarget`, `Unit.Main/TauntFadeOut`.

**`clearReferences`**: Clears all references in both online and offline containers. Called by `Unit.Main/DeleteThreatList`.

**`getCurrentVictim`**: Returns the current victim `HostileReference`. Called by `Spell.Effects/EffectTaunt`.

**`getOwner`**: Returns the owner `Unit` of the `ThreatManager`. Called by `HostileRefManager/deleteReference`, `HostileRefManager/deleteReferencesForFaction`, `HostileRefManager/setOnlineOfflineState`, `Spell.Effects/EffectSanctuary`.

**`getReferenceByTarget`**: Finds a `HostileReference` by `Unit` pointer. Calls `HostileReference/getUnitGuid`, `Object/GetObjectGuid`, `ObjectGuid/operator==`.

**`setDirty`**: Marks the threat list as needing a sort. Called by `HostileRefManager/setOnlineOfflineState`, `HostileRefManager/setOnlineOfflineState#2`.

**`getThreatList`**: Returns the online threat list. Called by numerous boss AIs, `Creature.Main` methods, `ChatHandler`, `Map.ScriptCommands`, and `Spell.Effects`.

**`addThreat#2`**: Overload of `addThreat` taking `Unit*`, `float`, `bool`, `SpellSchoolMask`, `SpellEntry*`, `bool`. Main entry point for threat addition.

**`modifyThreatPercent`**: Modifies threat by a percentage. Calls `HostileReference/addThreatPercent`.

**`HostileReferenceSortPredicate`**: Global function for sorting threat list in descending order. Calls `HostileReference/getThreat`.

**`update`**: Sorts the threat list if dirty. Called by `ThreatManager::getHostileTarget`.

**`selectNextVictim`**: Selects the next target based on threat thresholds and validity. Calls `Creature.Main/GetMeleeDamageSchoolMask`, `Creature.Main/IsOutOfThreatArea`, `Errors/PrintStacktraceAndThrow`, `HostileReference/getThreat`, `Unit.Main/CanReachWithMeleeAutoAttack`, `Unit.Main/GetMaxChaseDistance`, `Unit.Main/HasDistanceCasterMovement`, `Unit.Main/HasUnitState`, `Unit.Main/IsImmuneToDamage`, `Unit.Main/IsSecondaryThreatTarget`, `WorldObject.Object/IsValidAttackTarget`, `WorldObject.Object/IsWithinDist`.

**`ThreatManager`**: Constructor. Initializes owner and current victim. Called by `Unit.Main/Unit`.

**`clearReferences#2`**: Alias for `clearReferences`. Called by `Unit.Main/DeleteThreatList`.

**`addThreat#4`**: Overload of `addThreat` taking `Unit*` and `float`. Delegates to main `addThreat`. Called by `HostileRefManager/threatAssist`, `Unit.Main/AddThreat`.

**`addThreatDirectly`**: Adds threat directly to containers, creating reference if needed. Calls `Errors/PrintStacktraceAndThrow`, `Object/GetTypeId`, `Player.Main/IsGameMaster`, `SpellEntry/HasAttribute#3`, `Unit.Main/HasBreakableByDamageAuraType`, `Unit.Main/HasUnitState`, `Unit.Main/IsAlive`. Called by `boss_ayamiss/UpdateAI`, `boss_baroness_anastari/UpdateAI`, `boss_dathrohan_balnazzar/UpdateAI`, `boss_hakkar/UpdateAI`, `boss_jindo/UpdateAI`, `boss_maleki_the_pallid/UpdateAI`, `boss_nerubenkan/UpdateAI`, `boss_sartura/AssignRandomThreat`, `boss_sartura/AssignRandomThreat#2`, `boss_victor_nefarius/UpdateAI`, `duskwood/UpdateAI#3`, `quest_stormwind_rendezvous/UpdateAI`.

**`modifyThreatPercent#2`**: Overload of `modifyThreatPercent`. Calls `ThreatContainer::modifyThreatPercent`. Called by numerous boss AIs, `Creature.Main/Update`, `ScriptedAI`, `Spell.Effects`, `Unit.Main`.

**`getHostileTarget`**: Updates list and selects next victim. Called by `boss_cthun/SelectHostileTargetMelee`, `boss_patchwerk/CustomGetTarget`, `PetAI/SelectNextTarget`, `PetAI/UpdateAI`, `PetEventAI/FindTargetForAttack`, `Unit.Main/SelectHostileTarget`, `Unit.Main/TauntFadeOut`.

**`getThreat`**: Gets threat value for a unit. Calls `HostileReference/getThreat`. Called by numerous boss AIs, `PartyBotAI`, `ScriptedAI`, `Spell.Effects`, `Unit.Main`.

**`tauntApply`**: Applies taunt by setting temp threat modifier. Calls `HostileReference/getTempThreatModifyer`, `HostileReference/getThreat`, `HostileReference/setTempThreat`. Called by `Unit.Main/TauntApply`.

**`tauntFadeOut`**: Resets temp threat modifier. Calls `HostileReference/resetTempThreat`. Called by `Unit.Main/TauntFadeOut`.

**`setCurrentVictim`**: Sets the current victim reference. Called by `ThreatManager::getHostileTarget`, `ThreatManager::processThreatEvent`, `ThreatManager::setCurrentVictimIfCan`.

**`processThreatEvent`**: Processes events from `HostileReference` to update list state and victim. Calls `HostileReference/getThreat`, `HostileReference/isOnline`, `ThreatContainer/addReference`, `ThreatContainer/remove`, `ThreatRefStatusChangeEvent/getFValue`, `ThreatRefStatusChangeEvent/getReference`, `ThreatRefStatusChangeEvent/setThreatManager`, `UnitBaseEvent/getType`.

**`setCurrentVictimIfCan`**: Sets current victim if reference exists. Called by `Spell.Effects/EffectTaunt`.

---

<!-- machine-true, projected from graph.json -->

## Map — ThreatManager

*Source:* ThreatManager.cpp, ThreatManager.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| CalcThreat | method | Unit.Main/ApplyTotalThreatModifier, Unit.Main/GetSpellModOwner, Unit.Main/GetTotalAuraMultiplierByMiscMask | — | — |
| HostileReference | ctor | Object/GetObjectGuid | — | — |
| targetObjectBuildLink | method | Unit.Main/AddHatedBy | — | — |
| targetObjectDestroyLink | method | Unit.Main/RemoveHatedBy | — | — |
| sourceObjectDestroyLink | method | — | — | — |
| fireStatusChanged | method | — | — | — |
| addThreat | method | HostileReference/isOnline, ThreatRefStatusChangeEvent/ThreatRefStatusChangeEvent#4, Unit.Main/GetOwner, Unit.Main/IsTargetableBy | — | — |
| updateOnlineStatus | method | HostileReference/getUnitGuid, Object/GetTypeId, ObjectAccessor/GetUnit, Player.Main/IsGameMaster, Unit.Main/IsTaxiFlying | HostileRefManager/updateThreatTables | — |
| setOnlineOfflineState | method | ThreatRefStatusChangeEvent/ThreatRefStatusChangeEvent#2 | HostileRefManager/setOnlineOfflineState, HostileRefManager/setOnlineOfflineState#2 | — |
| setAccessibleState | method | ThreatRefStatusChangeEvent/ThreatRefStatusChangeEvent#2 | — | — |
| removeReference | method | ThreatRefStatusChangeEvent/ThreatRefStatusChangeEvent#2 | HostileRefManager/deleteReference, HostileRefManager/deleteReferences, HostileRefManager/deleteReferencesForFaction, Spell.Effects/EffectSanctuary | — |
| ~ThreatManager | dtor | — | — | — |
| addThreat#3 | method | — | boss_ayamiss/UpdateAI#2, boss_buru/UpdateAI, boss_fankriss/UpdateAI#3, boss_mandokir/UpdateAI, boss_ossirian/UpdateAI, boss_thaddius/HandleMagneticPull, boss_twinemperors/UpdateAI, Spell.Effects/EffectScriptEffect, Spell.Effects/EffectTaunt | — |
| getSourceUnit | method | — | AiBotAI.Combat/SelectAttackTarget, AiBotAI.Grind/SelectGrindTarget, BattleBotAI.Main/SelectAttackTarget, ChatHandler.UnitCommands/HandleListHostileRefsCommand, Player.Main/LeaveCombatWithFarAwayCreatures, Unit.Main/FindLowestHpFriendlyUnit, Unit.SpellAuras/HandleFeignDeath | — |
| isThreatListEmpty | method | — | boss_gothik/UpdateAI, boss_heigan/UpdateAI, boss_noth/UpdateAI, boss_patchwerk/CustomGetTarget, boss_razorgore/UpdateAI#2, boss_sapphiron/UpdateAI, boss_thaddius/UpdateAI, custom_creatures/UpdateAI#2, PetAI/SelectNextTarget, PetAI/UpdateAI, PetEventAI/FindTargetForAttack, ScriptedAI/EnterVanish, Unit.Main/DoResetThreat, Unit.Main/SelectHostileTarget, Unit.Main/TauntFadeOut | — |
| clearReferences | method | — | — | — |
| getCurrentVictim | method | — | Spell.Effects/EffectTaunt | — |
| getOwner | method | — | HostileRefManager/deleteReference, HostileRefManager/deleteReferencesForFaction, HostileRefManager/setOnlineOfflineState, Spell.Effects/EffectSanctuary | — |
| getReferenceByTarget | method | HostileReference/getUnitGuid, Object/GetObjectGuid, ObjectGuid/operator== | — | — |
| setDirty | method | — | — | — |
| getThreatList | method | — | boss_ayamiss/UpdateAI, boss_baroness_anastari/UpdateAI, boss_buru/UpdateAI, boss_four_horsemen/UpdateAI, boss_grobbulus/DoCastMutagenInjection, boss_heigan/CheckManausersAndRepeat, boss_heigan/EventPortPlayer, boss_maexxna/DoCastWebWrap, boss_ouro/UpdateAI, boss_patchwerk/DoHatefulStrike, boss_sapphiron/DoIceBolt, boss_sapphiron/PickNewTarget, boss_twinemperors/GetPlayerInMeleeRange, boss_twinemperors/GetPlayerInP2PRange, boss_vaelastrasz/UpdateAI, boss_ysondre/DoSpecialAbility, burning_steppes/DemonDespawn, burning_steppes/UpdateAI, ChatHandler.UnitCommands/HandleListThreatCommand, Creature.Main/AddThreatsOf, Creature.Main/FillGuidsListFromThreatList, Creature.Main/GetFarthestVictimInRange, Creature.Main/GetHostileCaster, Creature.Main/GetHostileCasterInRange, Creature.Main/GetNearestVictimInRange, Creature.Main/GetVictimInRange, Creature.Main/ProcessThreatList, Creature.Main/SelectAttackingTarget, Creature.Main/TryToCast, duskwood/FillPlayerList, instance_blackwing_lair/AddTechnician, instance_blackwing_lair/RecalculateThreat, Map.ScriptCommands/ScriptCommand_ModifyThreat, moonglade/UpdateAI, npcs_special/UpdateAI#16, ScriptedAI/EnterVanish, silithus/DemonDespawn, silithus/GetVictimInRangePlayerOnly, silithus/UpdateAI#9, Spell.Effects/EffectScriptEffect, ThreatListCopier.battleground_alterac/UpdateAI#12, ThreatListCopier.battleground_alterac/UpdateAI#14, ThreatListCopier.boss_ragnaros/UpdateAI, ungoro_crater/DemonDespawn, ungoro_crater/UpdateAI#3, Unit.Main/DoResetThreat, winterspring/DemonDespawn, winterspring/UpdateAI | — |
| addThreat#2 | method | — | — | — |
| modifyThreatPercent | method | HostileReference/addThreatPercent | — | — |
| HostileReferenceSortPredicate | function | HostileReference/getThreat | — | — |
| update | method | — | — | — |
| selectNextVictim | method | Creature.Main/GetMeleeDamageSchoolMask, Creature.Main/IsOutOfThreatArea, Errors/PrintStacktraceAndThrow, HostileReference/getThreat, Unit.Main/CanReachWithMeleeAutoAttack, Unit.Main/GetMaxChaseDistance, Unit.Main/HasDistanceCasterMovement, Unit.Main/HasUnitState, Unit.Main/IsImmuneToDamage, Unit.Main/IsSecondaryThreatTarget, WorldObject.Object/IsValidAttackTarget, WorldObject.Object/IsWithinDist | — | — |
| ThreatManager | ctor | — | Unit.Main/Unit | — |
| clearReferences#2 | method | — | Unit.Main/DeleteThreatList | — |
| addThreat#4 | method | Errors/PrintStacktraceAndThrow, Object/GetTypeId, Player.Main/IsGameMaster, SpellEntry/HasAttribute#3, Unit.Main/HasBreakableByDamageAuraType, Unit.Main/HasUnitState, Unit.Main/IsAlive | HostileRefManager/threatAssist, Unit.Main/AddThreat | — |
| addThreatDirectly | method | Object/GetTypeId, Player.Main/IsGameMaster, ThreatContainer/addReference, Unit.Main/IsAlive, WorldObject.Object/IsInMap | boss_ayamiss/UpdateAI, boss_baroness_anastari/UpdateAI, boss_dathrohan_balnazzar/UpdateAI, boss_hakkar/UpdateAI, boss_jindo/UpdateAI, boss_maleki_the_pallid/UpdateAI, boss_nerubenkan/UpdateAI, boss_sartura/AssignRandomThreat, boss_sartura/AssignRandomThreat#2, boss_victor_nefarius/UpdateAI, duskwood/UpdateAI#3, quest_stormwind_rendezvous/UpdateAI | — |
| modifyThreatPercent#2 | method | — | boss_arlokk/UpdateAI, boss_ayamiss/SpellHitTarget, boss_ayamiss/UpdateAI, boss_broodlord_lashlayer/SpellHitTarget, boss_bug_trio/SpellHitTarget, boss_cthun/SelectHostileTargetMelee, boss_dathrohan_balnazzar/UpdateAI, boss_dragon_of_nightmare/ChangeTarget, boss_ebonroc/SpellHitTarget, boss_firemaw/SpellHitTarget, boss_flamegor/SpellHitTarget, boss_four_horsemen/UpdateAI, boss_gothik/UpdateAI, boss_hakkar/UpdateAI, boss_immol_thar/UpdateAI, boss_ironaya/UpdateAI, boss_jandice_barov/UpdateAI, boss_jindo/SpellHitTarget, boss_jindo/UpdateAI, boss_jindo/UpdateAI#3, boss_maleki_the_pallid/UpdateAI, boss_mandokir/UpdateAI, boss_marli/SpellHitTarget, boss_nerubenkan/UpdateAI, boss_onyxia/PhaseOne, boss_onyxia/PhaseTwo, boss_ossirian/SpellHitTarget, boss_ouro/SpellHitTarget, boss_ramstein_the_gorger/UpdateAI, boss_razorgore/SpellHitTarget, boss_razorgore/UpdateAI#2, boss_renataki/UpdateAI, boss_victor_nefarius/UpdateAI, Creature.Main/Update, custom_creatures/UpdateAI#2, duskwood/UpdateAI#3, dustwallow_marsh/UpdateAI#5, instance_blackwing_lair/AddTechnician, instance_blackwing_lair/RecalculateThreat, instance_dire_maul/SpellHitTarget, Map.ScriptCommands/ScriptCommand_ModifyThreat, mob_anubisath_sentinel/SpellHitTarget, moonglade/UpdateAI, Player.Main/LeaveCombatWithFarAwayCreatures, ruins_of_ahnqiraj/UpdateAI#12, ScriptedAI/DoModifyThreatPercent, ScriptedAI/EnterVanish, Spell.Effects/EffectDummy, Spell.Effects/EffectModifyThreatPercent, Spell.Effects/EffectScriptEffect, ThreatListCopier.boss_ragnaros/CheckForMelee, ThreatListCopier.boss_ragnaros/SummonSonsOfFlame, uldaman/UpdateAI#2, Unit.Main/DoResetThreat, Unit.Main/operator()#6, Unit.Main/RemoveAttackersThreat, Unit.SpellAuras/HandleAuraDummy | — |
| getHostileTarget | method | — | boss_cthun/SelectHostileTargetMelee, boss_patchwerk/CustomGetTarget, PetAI/SelectNextTarget, PetAI/UpdateAI, PetEventAI/FindTargetForAttack, Unit.Main/SelectHostileTarget, Unit.Main/TauntFadeOut | — |
| getThreat | method | HostileReference/getThreat | boss_arlokk/UpdateAI, boss_ayamiss/SpellHitTarget, boss_bug_trio/SpellHitTarget, boss_buru/UpdateAI, boss_dathrohan_balnazzar/UpdateAI, boss_dragon_of_nightmare/ChangeTarget, boss_ebonroc/SpellHitTarget, boss_firemaw/SpellHitTarget, boss_flamegor/SpellHitTarget, boss_four_horsemen/UpdateAI, boss_hakkar/UpdateAI, boss_jindo/SpellHitTarget, boss_jindo/UpdateAI#2, boss_maleki_the_pallid/UpdateAI, boss_mandokir/CheckWatchedPlayer, boss_mandokir/SpellHitTarget, boss_mandokir/UpdateAI, boss_nerubenkan/UpdateAI, boss_onyxia/PhaseOne, boss_onyxia/PhaseTwo, boss_ossirian/SpellHitTarget, boss_ouro/SpellHitTarget, boss_razorgore/UpdateAI#2, boss_renataki/UpdateAI, boss_thaddius/HandleMagneticPull, boss_victor_nefarius/UpdateAI, duskwood/UpdateAI#3, mob_anubisath_sentinel/SpellHitTarget, PartyBotAI/CanTryToCastSpell, ScriptedAI/DoGetThreat, Spell.Effects/EffectDummy, Spell.Effects/EffectScriptEffect, Spell.Effects/EffectTaunt, Unit.Main/operator()#6 | — |
| tauntApply | method | HostileReference/getTempThreatModifyer, HostileReference/getThreat, HostileReference/setTempThreat | Unit.Main/TauntApply | — |
| tauntFadeOut | method | HostileReference/resetTempThreat | Unit.Main/TauntFadeOut | — |
| setCurrentVictim | method | — | — | — |
| processThreatEvent | method | HostileReference/getThreat, HostileReference/isOnline, ThreatContainer/addReference, ThreatContainer/remove, ThreatRefStatusChangeEvent/getFValue, ThreatRefStatusChangeEvent/getReference, ThreatRefStatusChangeEvent/setThreatManager, UnitBaseEvent/getType | — | — |
| setCurrentVictimIfCan | method | — | Spell.Effects/EffectTaunt | — |
