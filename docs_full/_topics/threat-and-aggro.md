# Threat & Aggro Range

<!-- aliases: threat, aggro range, aggro radius, reduce aggro, how does threat work, pull distance, mob leash -->
<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Threat & Aggro Range

The threat system in VMaNGOS determines which unit a creature attacks next and whether it will engage a target at all. It operates through two distinct mechanisms: **Threat Calculation** (how much "hatred" a unit generates) and **Victim Selection** (who the creature chooses to attack based on that hatred and spatial constraints).

## End-to-End Flow

### 1. Generating Threat
When a unit damages or heals another, threat is generated. The entry point is typically `ThreatManager::addThreat` (overload #4 in `ThreatManager.cpp`). This method performs several checks before proceeding:
*   **Validity:** It ignores self-targeting, dead units, and Game Masters (`IsGameMaster()`).
*   **Assist Logic:** If the threat is from an assist (e.g., a pet attacking), it checks if the attacker is under hard crowd control (stun, fear, etc.). If so, threat is zeroed out.
*   **Calculation:** It calls `ThreatCalcHelper::CalcThreat`. This static helper applies spell modifiers (`SPELLMOD_THREAT`) and critical strike multipliers (`SPELL_AURA_MOD_CRITICAL_THREAT`). Finally, it applies the target's total threat modifier aura.
*   **Storage:** The calculated threat is passed to `ThreatManager::addThreatDirectly`. This method looks for an existing `HostileReference` in the online or offline containers. If none exists, it creates a new `HostileReference` object. The `HostileReference` constructor initializes the threat value and links the source (attacker) and target (victim) units.

### 2. Managing Threat State
Each `HostileReference` tracks the relationship between a specific attacker and victim.
*   **Online Status:** `HostileReference::updateOnlineStatus` determines if a reference is "online" (active in combat consideration). A reference goes offline if the target is invalid, a GM, or taxi-flying. Offline references are moved to `iThreatOfflineContainer` and ignored during victim selection.
*   **Temporary Threat:** Spells like Taunt use temporary threat. `ThreatManager::tauntApply` sets a `tempThreatModifier` on the taunter's reference, effectively boosting their threat value temporarily. `tauntFadeOut` resets this.
*   **Events:** Changes in threat or status fire `ThreatRefStatusChangeEvent`s via `HostileReference::fireStatusChanged`. These events are processed by `ThreatManager::processThreatEvent`, which updates the internal lists and marks the threat list as "dirty" if the order might have changed.

### 3. Selecting the Next Victim
When a creature needs to choose a target (e.g., after its current target dies or it enters combat), it calls `ThreatManager::getHostileTarget`. This triggers `ThreatContainer::selectNextVictim`.

**The Selection Algorithm:**
1.  **Sorting:** If the list is marked dirty, `ThreatContainer::update` sorts the online threat list in descending order using `HostileReferenceSortPredicate`.
2.  **Iteration:** `selectNextVictim` iterates through the sorted list. It performs up to two passes:
    *   **Pass 1:** Considers only high-priority targets.
    *   **Pass 2:** Allows low-priority targets (immune units, secondary targets like feared units, or unreachable units if the attacker is immobilized).
3.  **Validation:** For each candidate, it checks:
    *   `IsOutOfThreatArea`: If the target is outside the creature's threat area (leash range), the function returns `nullptr`, causing the creature to lose aggro and potentially despawn or return to patrol.
    *   `IsValidAttackTarget`: General validity checks (faction, line of sight, etc.).
4.  **Threshholding:** If there is a current victim, the algorithm compares the new candidate's threat to the current victim's threat:
    *   **Melee Rule:** If the attacker can reach the candidate with a melee auto-attack, the candidate must have **>110%** of the current victim's threat to switch.
    *   **Ranged Rule:** Otherwise, the candidate must have **>130%** of the current victim's threat to switch.
    *   If no current victim exists, the first valid target is selected.

### 4. Aggro Range (Leash)
The "aggro range" or "pull distance" is primarily governed by `Creature::IsOutOfThreatArea`. While the specific implementation of `IsOutOfThreatArea` is not in the provided slices, `selectNextVictim` relies on it heavily. If a target is determined to be out of the threat area, the creature will not select them, and if they are the current victim, the creature may disengage. This mechanism prevents mobs from chasing players across the entire map.

## How to Modify

### Config
No dedicated configuration keys exist in the provided `CONFIG` block for tuning threat multipliers, aggro ranges, or victim selection thresholds. The behavior is hardcoded in the source.

### Database
No database tables or columns are listed in the `SCHEMA` or `TOPIC MAP` for this topic. Threat calculations rely on spell attributes and unit states, which are defined in static DBC data or runtime unit properties, not directly tunable via standard world database rows in this context.

### Code
To modify threat behavior, you must edit the C++ source code and rebuild the server.

*   **Change Threat Multipliers:**
    *   Edit `ThreatManager::selectNextVictim` in `ThreatManager.cpp`.
    *   Locate the lines comparing `currentRef->getThreat()` to `pCurrentVictim->getThreat()`.
    *   Change `1.1f` to adjust the melee switch threshold (currently 110%).
    *   Change `1.3f` to adjust the ranged switch threshold (currently 130%).

*   **Change Critical Threat Bonus:**
    *   Edit `ThreatCalcHelper::CalcThreat` in `ThreatManager.cpp`.
    *   The multiplier is retrieved via `pHatedUnit->GetTotalAuraMultiplierByMiscMask(SPELL_AURA_MOD_CRITICAL_THREAT, schoolMask)`. To change the base value, you would need to modify how `SPELL_AURA_MOD_CRITICAL_THREAT` is applied in the spell system or unit aura handling, which is outside this specific file.

*   **Change Aggro/Leash Range:**
    *   The function `Creature::IsOutOfThreatArea` is called in `selectNextVictim`. You must locate this method in the `Creature` class (likely in `Creature.cpp` or `Unit.cpp`) to modify the distance calculation. It likely uses `GetMaxChaseDistance` or similar methods.

*   **Change Assist Threat Rules:**
    *   Edit `ThreatManager::addThreat` (overload #4) in `ThreatManager.cpp`.
    *   Modify the conditions under which `threat = 0.0f` is set for assist threats (e.g., remove the stun/fear checks if you want assists to generate threat even when CC'd).

*   **Change Taunt Behavior:**
    *   Edit `ThreatManager::tauntApply` in `ThreatManager.cpp`.
    *   Currently, it sets the taunter's threat to match the current victim's threat. You can change `ref->setTempThreat(getCurrentVictim()->getThreat())` to add a bonus or multiply the value.

## Path Reference

**HostileRefManager/addTempThreat**
Method in `HostileRefManager.cpp`. Iterates through all hostile references and applies or resets a temporary threat modifier. Used for effects that temporarily boost or reduce threat for all targets.

**HostileRefManager/addThreatPercent**
Method in `HostileRefManager.cpp`. Iterates through all hostile references and adds a percentage-based threat modifier to each. Used for global threat adjustments.

**ThreatManager/CalcThreat**
Static method in `ThreatManager.cpp`. Calculates the final threat value by applying spell modifiers, critical strike multipliers, and aura-based threat modifiers. Centralizes the math for threat generation.

**ThreatManager/HostileReference**
Constructor in `ThreatManager.cpp`. Creates a new `HostileReference` object linking a target unit to a threat manager, initializing threat values and online status.

**ThreatManager/targetObjectBuildLink**
Method in `ThreatManager.cpp`. Notifies the target unit that a hostile reference has been created, allowing the target to track who hates it.

**ThreatManager/targetObjectDestroyLink**
Method in `ThreatManager.cpp`. Notifies the target unit that a hostile reference is being destroyed, cleaning up the target's hate list.

**ThreatManager/sourceObjectDestroyLink**
Method in `ThreatManager.cpp`. Handles cleanup when the source unit (attacker) is destroyed, marking the reference as offline.

**ThreatManager/fireStatusChanged**
Method in `ThreatManager.cpp`. Sends a status change event to the source unit's threat manager, triggering updates in the threat list and victim selection logic.

**ThreatManager/addThreat**
Method in `ThreatManager.cpp`. Adds a raw threat value to a reference, clamping it to zero if negative. Triggers status updates and pet assist logic.

**ThreatManager/updateOnlineStatus**
Method in `ThreatManager.cpp`. Determines if a reference should be online or offline based on target validity, GM status, and taxi flying. Moves references between online and offline containers.

**ThreatManager/setOnlineOfflineState**
Method in `ThreatManager.cpp`. Sets the online flag of a reference and fires an event. If going offline, it also sets the accessible state to false.

**ThreatManager/setAccessibleState**
Method in `ThreatManager.cpp`. Sets the accessible flag of a reference and fires an event. Used to mark references as inaccessible for targeting.

**ThreatManager/removeReference**
Method in `ThreatManager.cpp`. Invalidates a reference and fires a removal event, removing it from the threat list.

**ThreatManager/~ThreatManager**
Destructor in `ThreatManager.h`. Clears all references when the threat manager is destroyed.

**ThreatManager/addThreat#3**
Overload in `ThreatManager.h`. Simplified version of `addThreat` that delegates to the main overload with default parameters.

**ThreatManager/getSourceUnit**
Method in `ThreatManager.cpp`. Returns the owner unit of the threat manager (the attacker).

**ThreatManager/isThreatListEmpty**
Method in `ThreatManager.h`. Checks if the online threat container is empty.

**ThreatManager/clearReferences**
Method in `ThreatManager.cpp`. Clears all references in both online and offline containers, deleting the objects.

**ThreatManager/getCurrentVictim**
Method in `ThreatManager.h`. Returns the current victim reference, representing the unit the attacker is currently targeting.

**ThreatManager/getOwner**
Method in `ThreatManager.h`. Returns the owner unit of the threat manager.

**ThreatManager/getReferenceByTarget**
Method in `ThreatManager.cpp`. Finds a `HostileReference` by the target unit's GUID.

**ThreatManager/setDirty**
Method in `ThreatManager.h`. Marks the threat list as needing a sort, ensuring the next victim selection uses the updated order.

**ThreatManager/getThreatList**
Method in `ThreatManager.h`. Returns the online threat list, allowing external systems to inspect the threat values.

**ThreatManager/addThreat#2**
Overload in `ThreatManager.cpp`. Main entry point for adding threat, taking detailed parameters like critical hit status and spell school.

**ThreatManager/modifyThreatPercent**
Method in `ThreatManager.cpp`. Modifies threat by a percentage for a specific victim.

**ThreatManager/HostileReferenceSortPredicate**
Function in `ThreatManager.cpp`. Predicate function for sorting the threat list in descending order of threat value.

**ThreatManager/update**
Method in `ThreatManager.cpp`. Sorts the threat list if it is marked as dirty.

**ThreatManager/selectNextVictim**
Method in `ThreatManager.cpp`. Core logic for selecting the next target based on threat thresholds, validity, and spatial constraints. Implements the 110%/130% switch rules.

**ThreatManager/ThreatManager**
Constructor in `ThreatManager.cpp`. Initializes the threat manager with an owner unit.

**ThreatManager/clearReferences#2**
Alias in `ThreatManager.cpp`. Calls `clearReferences` to clean up all threat data.

**ThreatManager/addThreat#4**
Overload in `ThreatManager.cpp`. Detailed overload for adding threat, handling validity checks, assist logic, and delegating to `CalcThreat` and `addThreatDirectly`.

**ThreatManager/addThreatDirectly**
Method in `ThreatManager.cpp`. Adds threat directly to the containers, creating a new reference if necessary. Handles GM and offline status.

**ThreatManager/modifyThreatPercent#2**
Overload in `ThreatManager.cpp`. Delegates to the container's `modifyThreatPercent` method.

**ThreatManager/getHostileTarget**
Method in `ThreatManager.cpp`. Updates the threat list and selects the next victim, returning the target unit.

**ThreatManager/getThreat**
Method in `ThreatManager.cpp`. Retrieves the current threat value for a specific victim, searching both online and offline lists if requested.

**ThreatManager/tauntApply**
Method in `ThreatManager.cpp`. Applies a taunt by setting a temporary threat modifier on the taunter's reference, forcing the attacker to target them.

**ThreatManager/tauntFadeOut**
Method in `ThreatManager.cpp`. Resets the temporary threat modifier when the taunt effect ends.

**ThreatManager/setCurrentVictim**
Method in `ThreatManager.cpp`. Sets the current victim reference, updating the attacker's target.

**ThreatManager/processThreatEvent**
Method in `ThreatManager.cpp`. Processes events from hostile references, updating the threat list structure and marking it dirty if necessary.

**ThreatManager/setCurrentVictimIfCan**
Method in `ThreatManager.cpp`. Sets the current victim if a valid reference exists for the target unit.

---

<!-- machine-true, projected from graph.json -->

## Map — Threat & Aggro Range

*Source:* HostileRefManager.cpp, ThreatManager.cpp, ThreatManager.h
*Config keys:* —
*Tables:* —

| Member | Kind | Source | Role |
|---|---|---|---|
| HostileRefManager/addTempThreat | method | HostileRefManager.cpp:39-55 | seed — HostileRefManager/* |
| HostileRefManager/addThreatPercent | method | HostileRefManager.cpp:80-90 | seed — HostileRefManager/* |
| ThreatManager/CalcThreat | method | ThreatManager.cpp:35-52 | seed — ThreatManager/* |
| ThreatManager/HostileReference | ctor | ThreatManager.cpp:58-66 | seed — ThreatManager/* |
| ThreatManager/targetObjectBuildLink | method | ThreatManager.cpp:70-73 | seed — ThreatManager/* |
| ThreatManager/targetObjectDestroyLink | method | ThreatManager.cpp:77-80 | seed — ThreatManager/* |
| ThreatManager/sourceObjectDestroyLink | method | ThreatManager.cpp:85-88 | seed — ThreatManager/* |
| ThreatManager/fireStatusChanged | method | ThreatManager.cpp:93-97 | seed — ThreatManager/* |
| ThreatManager/addThreat | method | ThreatManager.cpp:101-123 | seed — ThreatManager/* |
| ThreatManager/updateOnlineStatus | method | ThreatManager.cpp:128-149 | seed — ThreatManager/* |
| ThreatManager/setOnlineOfflineState | method | ThreatManager.cpp:154-165 | seed — ThreatManager/* |
| ThreatManager/setAccessibleState | method | ThreatManager.cpp:169-178 | seed — ThreatManager/* |
| ThreatManager/removeReference | method | ThreatManager.cpp:184-190 | seed — ThreatManager/* |
| ThreatManager/~ThreatManager | dtor | ThreatManager.h:187-187 | seed — ThreatManager/* |
| ThreatManager/addThreat#3 | method | ThreatManager.h:192-192 | seed — ThreatManager/* |
| ThreatManager/getSourceUnit | method | ThreatManager.cpp:194-197 | seed — ThreatManager/* |
| ThreatManager/isThreatListEmpty | method | ThreatManager.h:201-201 | seed — ThreatManager/* |
| ThreatManager/clearReferences | method | ThreatManager.cpp:203-211 | seed — ThreatManager/* |
| ThreatManager/getCurrentVictim | method | ThreatManager.h:205-205 | seed — ThreatManager/* |
| ThreatManager/getOwner | method | ThreatManager.h:208-208 | seed — ThreatManager/* |
| ThreatManager/getReferenceByTarget | method | ThreatManager.cpp:215-232 | seed — ThreatManager/* |
| ThreatManager/setDirty | method | ThreatManager.h:217-217 | seed — ThreatManager/* |
| ThreatManager/getThreatList | method | ThreatManager.h:220-220 | seed — ThreatManager/* |
| ThreatManager/addThreat#2 | method | ThreatManager.cpp:237-246 | seed — ThreatManager/* |
| ThreatManager/modifyThreatPercent | method | ThreatManager.cpp:250-262 | seed — ThreatManager/* |
| ThreatManager/HostileReferenceSortPredicate | function | ThreatManager.cpp:266-270 | seed — ThreatManager/* |
| ThreatManager/update | method | ThreatManager.cpp:275-280 | seed — ThreatManager/* |
| ThreatManager/selectNextVictim | method | ThreatManager.cpp:286-370 | seed — ThreatManager/* |
| ThreatManager/ThreatManager | ctor | ThreatManager.cpp:376-378 | seed — ThreatManager/* |
| ThreatManager/clearReferences#2 | method | ThreatManager.cpp:382-387 | seed — ThreatManager/* |
| ThreatManager/addThreat#4 | method | ThreatManager.cpp:391-425 | seed — ThreatManager/* |
| ThreatManager/addThreatDirectly | method | ThreatManager.cpp:427-447 | seed — ThreatManager/* |
| ThreatManager/modifyThreatPercent#2 | method | ThreatManager.cpp:451-454 | seed — ThreatManager/* |
| ThreatManager/getHostileTarget | method | ThreatManager.cpp:458-464 | seed — ThreatManager/* |
| ThreatManager/getThreat | method | ThreatManager.cpp:468-477 | seed — ThreatManager/* |
| ThreatManager/tauntApply | method | ThreatManager.cpp:481-492 | seed — ThreatManager/* |
| ThreatManager/tauntFadeOut | method | ThreatManager.cpp:496-500 | seed — ThreatManager/* |
| ThreatManager/setCurrentVictim | method | ThreatManager.cpp:504-507 | seed — ThreatManager/* |
| ThreatManager/processThreatEvent | method | ThreatManager.cpp:513-557 | seed — ThreatManager/* |
| ThreatManager/setCurrentVictimIfCan | method | ThreatManager.cpp:559-565 | seed — ThreatManager/* |
