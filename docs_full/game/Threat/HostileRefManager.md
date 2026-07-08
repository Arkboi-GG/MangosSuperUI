<!-- provenance: verbose -->
# HostileRefManager

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# HostileRefManager

## Purpose & Responsibilities

`HostileRefManager` manages the linked list of `HostileReference` objects representing entities hostile toward a specific `Unit`. Inheriting from `RefManager<Unit, ThreatManager>`, it provides iteration, bulk threat modification, lifecycle management (removal/deletion), and state synchronization for these references. It does not store threat values itself but coordinates updates between the owner’s perspective and the sources’ `ThreatManager`s.

## Member-by-Member Behavior

### Initialization and Cleanup
*   **`HostileRefManager`**: Stores the owning `Unit*` in `iOwner`.
*   **`~HostileRefManager`**: Calls `deleteReferences()` to unlink and destroy all `HostileReference` objects, preventing memory leaks.

### Threat Modification
*   **`addTempThreat`**: Iterates all references. If `apply` is `true`, sets the temporary threat modifier to `threat` only if the current modifier is `0.0f` (preventing overwrite of active modifiers). If `false`, resets the modifier.
*   **`addThreatPercent`**: Iterates all references, calling `addThreatPercent` on each to apply global percentage-based threat adjustments.
*   **`threatAssist`**: Generates threat for `pVictim` on all hostile references, used for healing/buffing.
    *   Returns immediately if `pThreatSpell` has `SPELL_ATTR_EX4_NO_HELPFUL_THREAT`.
    *   Calculates distributed threat: `pThreat / size` (where `size` is `1` if `pSingleTarget`, else `getSize()`).
    *   Iterates references, calling `addThreat` on each source unit's `ThreatManager` for `pVictim`.

### List Management and Iteration
*   **`getOwner`**: Returns the `Unit*` owning this manager.
*   **`getFirst`**: Returns the head `HostileReference*` of the linked list, casting from the base class.

### State Updates
*   **`setOnlineOfflineState` (bool)**: Sets the online/offline state for *all* references to `pIsOnline`.
*   **`updateThreatTables`**: Iterates references, calling `updateOnlineStatus` on each to refresh state based on current conditions.
*   **`setOnlineOfflineState` (Unit*)**: Finds the reference where `ref->getSource()->getOwner() == pCreature` and updates its online/offline state.

### Reference Removal
*   **`deleteReferences`**: Iterates all references, calling `removeReference()` to unlink from the source `ThreatManager`, then `delete`s the object. Uses safe iteration (`nextRef`) to handle deletion during traversal.
*   **`deleteReferencesForFaction`**: Removes references where the source unit's owner has the specified `faction`.
*   **`deleteReference`**: Removes the single reference where `ref->getSource()->getOwner() == pCreature`.

## Cross-Unit Boundaries

*   **Calls Out:**
    *   **`HostileReference`**: Used for iteration (`next`), state access/modification (`getTempThreatModifyer`, `setTempThreatModifier`, `resetTempThreat`, `addThreatPercent`, `setOnlineOfflineState`, `updateOnlineStatus`, `getSource`), and cleanup (`removeReference`).
    *   **`ThreatManager`**: Accessed via `HostileReference` or directly on source units (`addThreat`, `removeReference`, `setOnlineOfflineState`).
    *   **`SpellEntry`**: `HasAttribute` and `GetSpellSchoolMask` used in `threatAssist`.
    *   **`WorldObject`**: `GetFactionId` used in `deleteReferencesForFaction`.
    *   **`LinkedListHead`**: `getSize` used in `threatAssist`.

*   **Called By:**
    *   **`Unit.Main/Unit`**: Constructor called during `Unit` creation.
    *   **`Unit.SpellAuras/HandleAuraModTotalThreat`**: Calls `addTempThreat`.
    *   **`AiBotAI`, `BattleBotAI`, `ChatHandler`, `Player`, `Spell`, `Unit`**: Various modules call `getFirst` to inspect threat lists for target selection, debugging, or combat logic.
    *   **`Spell.Main`, `Unit.SpellAuras`**: Call `threatAssist` for spell-based threat generation.
    *   **`Player`, `Unit`, `WorldSession`, `WaypointMovementGenerator`**: Call `setOnlineOfflineState` variants during login/logout/GM mode changes.
    *   **`Player.Main/SetEnvironmentFlags`**: Calls `updateThreatTables`.
    *   **`ChatHandler`, `Battlegrounds`, `PetAI`, `Player`, `Unit`, `WorldSession`**: Call `deleteReferences` variants during death, logout, teleport, or combat stop.

## Data Model

This unit does not interact with any database tables. All data is held in memory within the `HostileReference` linked list.

## Notable Implementation Details

*   **Division by Zero Risk in `threatAssist`**: If `pSingleTarget` is `false` and the list is empty, `getSize()` returns `0`, causing `pThreat / 0`. Although the subsequent loop won't execute, the division occurs beforehand, potentially resulting in `inf` or `NaN` for the `threat` variable.
*   **Safe Iteration During Deletion**: `deleteReferences`, `deleteReferencesForFaction`, and `deleteReference` all capture `ref->next()` before processing the current node. This is critical because `removeReference()` may alter the list structure or the node itself, and `delete` invalidates the pointer.
*   **Temporary Threat Guard**: `addTempThreat` only applies a new modifier if the current one is `0.0f`. This prevents stacking or overwriting active temporary threat effects (e.g., taunts) with new ones unless the previous effect has expired/resets.

## Member Reference

**HostileRefManager**
Constructor initializing `iOwner` with the provided `Unit*`.

**~HostileRefManager**
Destructor calling `deleteReferences()` to clean up all hostile references.

**addTempThreat**
Iterates references; if `apply` is true, sets temp modifier to `threat` only if current is `0.0f`; otherwise resets temp threat. Calls `HostileReference::getTempThreatModifyer`, `setTempThreatModifier`, `resetTempThreat`, `next`.

**getOwner**
Returns `iOwner`.

**getFirst**
Returns the head `HostileReference*` from the base class.

**threatAssist**
Generates threat for `pVictim` on all haters. Checks `SpellEntry::HasAttribute` for `NO_HELPFUL_THREAT`. Divides `pThreat` by `LinkedListHead::getSize` (or 1). Calls `ThreatManager::addThreat` on each source. Uses `HostileReference::next`, `SpellEntry::GetSpellSchoolMask`.

**addThreatPercent**
Iterates references, calling `HostileReference::addThreatPercent` on each.

**setOnlineOfflineState#2**
Sets online/offline state for all references to `pIsOnline`. Calls `HostileReference::setOnlineOfflineState`, `next`.

**updateThreatTables**
Updates online status for all references. Calls `HostileReference::updateOnlineStatus`, `next`.

**deleteReferences**
Removes all references. Calls `HostileReference::removeReference`, then `delete`s the object. Uses safe iteration with `next`.

**deleteReferencesForFaction**
Removes references where `ref->getSource()->getOwner()->GetFactionId()` matches `faction`. Calls `HostileReference::removeReference`, `delete`, `next`.

**deleteReference**
Removes reference where `ref->getSource()->getOwner() == pCreature`. Calls `HostileReference::removeReference`, `delete`, `next`.

**setOnlineOfflineState**
Sets online/offline state for reference where `ref->getSource()->getOwner() == pCreature`. Calls `HostileReference::setOnlineOfflineState`, `next`.

---

<!-- machine-true, projected from graph.json -->

## Map — HostileRefManager

*Source:* HostileRefManager.cpp, HostileRefManager.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| HostileRefManager | ctor | — | Unit.Main/Unit | — |
| ~HostileRefManager | dtor | — | — | — |
| addTempThreat | method | HostileReference/getTempThreatModifyer, HostileReference/next, HostileReference/resetTempThreat, HostileReference/setTempThreatModifier | Unit.SpellAuras/HandleAuraModTotalThreat | — |
| getOwner | method | — | Unit.Main/CleanupsBeforeDelete | — |
| getFirst | method | — | AiBotAI.Combat/SelectAttackTarget, AiBotAI.Grind/SelectGrindTarget, BattleBotAI.Main/SelectAttackTarget, ChatHandler.UnitCommands/HandleListHostileRefsCommand, Player.Main/LeaveCombatWithFarAwayCreatures, Spell.Effects/EffectSanctuary, Unit.Main/FindLowestHpFriendlyUnit, Unit.SpellAuras/HandleFeignDeath | — |
| threatAssist | method | HostileReference/next, LinkedListHead/getSize, SpellEntry/GetSpellSchoolMask, SpellEntry/HasAttribute#6, ThreatManager/addThreat#4 | Spell.Main/DoAllEffectOnTarget#3, Spell.Main/DoSpellHitOnUnit, Spell.Main/HandleThreatSpells, Unit.SpellAuras/PeriodicTick | — |
| addThreatPercent | method | HostileReference/addThreatPercent, HostileReference/next | — | — |
| setOnlineOfflineState#2 | method | HostileReference/next, ThreatManager/setOnlineOfflineState | Player.Main/operator()#2, Player.Main/operator()#3, Player.Main/SetGameMaster, Unit.Main/CleanupsBeforeDelete, WaypointMovementGenerator/Finalize, WaypointMovementGenerator/Reset, WorldSession.Main/LogoutPlayer | — |
| updateThreatTables | method | HostileReference/next, ThreatManager/updateOnlineStatus | Player.Main/SetEnvironmentFlags | — |
| deleteReferences | method | HostileReference/next, ThreatManager/removeReference | ChatHandler.CharacterCommands/HandleCombatStopCommand, game_Battlegrounds_BattleGround/EndBattleGround, PetAI/_stopAttack, Player.Main/ExecuteTeleportFar, Player.Main/SwitchInstance, Unit.Main/CleanupsBeforeDelete, Unit.Main/Kill, Unit.Main/SetFeignDeath, Unit.SpellAuras/HandleModCharm, Unit.SpellAuras/ModPossess, WorldSession.Main/LogoutPlayer | — |
| deleteReferencesForFaction | method | HostileReference/next, ThreatManager/getOwner, ThreatManager/removeReference, WorldObject.Object/GetFactionId | Unit.Main/StopAttackFaction | — |
| deleteReference | method | HostileReference/next, ThreatManager/getOwner, ThreatManager/removeReference | Player.Main/LeaveCombatWithFarAwayCreatures | — |
| setOnlineOfflineState | method | HostileReference/next, ThreatManager/getOwner, ThreatManager/setOnlineOfflineState | — | — |
