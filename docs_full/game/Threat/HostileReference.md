# HostileReference

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# HostileReference

**Purpose & Responsibilities**

`HostileReference` represents a single entry in a `Unit`'s threat list within the `wowvmangos` engine. It serves as a node in an intrusive doubly-linked list (via inheritance from `Reference<Unit, ThreatManager>`), tracking the hostility level (`iThreat`) between an attacker (managed by `ThreatManager`) and a specific target `Unit`.

Its core responsibilities are:
1.  **Threat Accounting:** Storing and modifying raw threat values, including percentage-based adjustments and temporary spikes used for mechanics like Taunt.
2.  **State Tracking:** Maintaining the target's connectivity status (`iOnline`) and physical reachability (`iAccessible`) relative to the owner.
3.  **Identity Management:** Providing stable identification via `ObjectGuid` for safe iteration and comparison, independent of the `Unit` object's lifetime.

## Member-by-Member Behavior

### Threat Value Management

*   **`setThreat`**: An inline setter that calculates the delta between the desired absolute threat and the current `iThreat`, then delegates to `addThreat`. This ensures consistent side effects for threat modifications.
*   **`addThreatPercent`**: Modifies `iThreat` by a percentage. It explicitly handles the `-100` case by subtracting the exact current `iThreat` value to avoid floating-point rounding errors that might leave residual threat. Other percentages use standard multiplication.
*   **`getThreat`**: Returns the current raw `iThreat` value.
*   **`setTempThreat`**: Sets a temporary threat value by calculating the difference from the current threat, storing this delta in `iTempThreatModifyer`, and adding it to `iThreat`. Used for Taunt mechanics.
*   **`setTempThreatModifier`**: Directly assigns a value to `iTempThreatModifyer` and adds it to `iThreat`. Used when the modifier amount is pre-calculated.
*   **`resetTempThreat`**: Reverts temporary threat by subtracting `iTempThreatModifyer` from `iThreat` and resetting the modifier to zero. Critical for Taunt expiration.
*   **`getTempThreatModifyer`**: Returns the current `iTempThreatModifyer` value.

### State and Identity

*   **`isOnline`**: Returns `iOnline`, indicating if the target is within aggro range and visible.
*   **`isAccessable`**: Returns `iAccessible`, indicating if the owner can physically reach the target (e.g., not separated by walls or water).
*   **`operator==`**: Compares two `HostileReference` objects for equality based solely on their `iUnitGuid`, ensuring safe comparison even if `Unit` pointers are stale.
*   **`getUnitGuid`**: Returns the `ObjectGuid` of the target unit, serving as the stable identifier for the entry.

### List Navigation

*   **`next`**: Casts the result of the base class `Reference::next()` to `HostileReference*`, enabling traversal of the threat list.

## Cross-Unit Boundaries

`HostileReference` is a passive data structure with no outgoing calls to other units. It is extensively consumed by:

*   **`ThreatManager`**: Uses `isOnline` to validate threat additions, `getThreat` for sorting and victim selection, and `setTempThreat`/`resetTempThreat` for Taunt mechanics.
*   **`HostileRefManager`**: Iterates the list via `next()` to apply bulk modifications (`addThreatPercent`, `addTempThreat`) or delete references.
*   **AI Modules & Scripts**: Units like `boss_baroness_anastari` and `boss_buru` use `getUnitGuid()` to identify specific targets. AI systems like `AiBotAI` and `BattleBotAI` use `next()` to select attack targets. Debug commands like `HandleListThreatCommand` use `getThreat` and `getUnitGuid` for reporting.

## Data Model

This unit does not interact with any database tables. All threat data is maintained in memory.

## Notable Implementation Details

1.  **Precision Handling**: `addThreatPercent` explicitly checks for `-100` to prevent floating-point inaccuracies from leaving non-zero threat values.
2.  **Temporary Threat Isolation**: `iTempThreatModifyer` isolates temporary boosts (like Taunt) from permanent threat accumulation, allowing precise reversion via `resetTempThreat`.
3.  **Intrusive List Design**: Inheriting from `Reference` embeds list pointers directly in the object, enabling O(1) insertion/removal without wrapper allocations.
4.  **Guid-Based Safety**: `operator==` and `getUnitGuid` rely on `ObjectGuid` rather than `Unit*` pointers, preventing undefined behavior from dangling pointers during list iteration.

## Member Reference

**setThreat** Inline method calculating the delta to the desired threat value and delegating to `addThreat` to ensure consistent side effects.

**addThreatPercent** Modifies `iThreat` by a percentage, with a special case for `-100` to subtract the exact current value and avoid floating-point rounding errors.

**getThreat** Returns the current raw `iThreat` value.

**isOnline** Returns the `iOnline` boolean, indicating if the target is within aggro range and visible.

**isAccessable** Returns the `iAccessible` boolean, indicating if the owner can physically reach the target.

**setTempThreat** Calculates the delta to reach a specific threat value, stores it in `iTempThreatModifyer`, and adds it to `iThreat`.

**setTempThreatModifier** Sets `iTempThreatModifyer` to a specific value and adds it to `iThreat`.

**resetTempThreat** Subtracts `iTempThreatModifyer` from `iThreat` and resets the modifier to zero.

**getTempThreatModifyer** Returns the current `iTempThreatModifyer` value.

**operator==** Compares two `HostileReference` objects for equality based on their `iUnitGuid`.

**getUnitGuid** Returns the `ObjectGuid` of the target unit.

**next** Returns the next `HostileReference` in the linked list by casting the base class result.

---

<!-- machine-true, projected from graph.json -->

## Map — HostileReference

*Source:* ThreatManager.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| setThreat | method | — | — | — |
| addThreatPercent | method | — | HostileRefManager/addThreatPercent, ThreatManager/modifyThreatPercent | — |
| getThreat | method | — | boss_baroness_anastari/UpdateAI, ChatHandler.UnitCommands/HandleListThreatCommand, instance_blackwing_lair/AddTechnician, instance_blackwing_lair/RecalculateThreat, ScriptedAI/EnterVanish, Spell.Effects/EffectTaunt, ThreatManager/getThreat, ThreatManager/HostileReferenceSortPredicate, ThreatManager/processThreatEvent, ThreatManager/selectNextVictim, ThreatManager/tauntApply, Unit.Main/DoResetThreat | — |
| isOnline | method | — | ThreatManager/addThreat, ThreatManager/processThreatEvent | — |
| isAccessable | method | — | — | — |
| setTempThreat | method | — | ThreatManager/tauntApply | — |
| setTempThreatModifier | method | — | HostileRefManager/addTempThreat | — |
| resetTempThreat | method | — | HostileRefManager/addTempThreat, ThreatManager/tauntFadeOut | — |
| getTempThreatModifyer | method | — | HostileRefManager/addTempThreat, ThreatManager/tauntApply | — |
| operator== | method | — | — | — |
| getUnitGuid | method | — | boss_baroness_anastari/UpdateAI, boss_buru/UpdateAI, boss_patchwerk/DoHatefulStrike, ChatHandler.UnitCommands/HandleListThreatCommand, Creature.Main/FillGuidsListFromThreatList, duskwood/FillPlayerList, ThreatManager/getReferenceByTarget, ThreatManager/updateOnlineStatus | — |
| next | method | — | AiBotAI.Combat/SelectAttackTarget, AiBotAI.Grind/SelectGrindTarget, BattleBotAI.Main/SelectAttackTarget, ChatHandler.UnitCommands/HandleListHostileRefsCommand, HostileRefManager/addTempThreat, HostileRefManager/addThreatPercent, HostileRefManager/deleteReference, HostileRefManager/deleteReferences, HostileRefManager/deleteReferencesForFaction, HostileRefManager/setOnlineOfflineState, HostileRefManager/setOnlineOfflineState#2, HostileRefManager/threatAssist, HostileRefManager/updateThreatTables, Player.Main/LeaveCombatWithFarAwayCreatures, Spell.Effects/EffectSanctuary, Unit.Main/FindLowestHpFriendlyUnit, Unit.SpellAuras/HandleFeignDeath | — |
