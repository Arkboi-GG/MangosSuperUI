<!-- provenance: failed-members -->
# MostHPMissingInRangeCheck

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# MostHPMissingInRangeCheck

**MostHPMissingInRangeCheck** is a predicate functor defined in `GridNotifiers.h` within the `MaNGOS` namespace. It implements the logic for identifying friendly units that are currently in combat, alive, and missing a significant amount of health relative to a specified threshold. This check is designed to be used with grid-based searchers (such as `UnitSearcher` or `UnitListSearcher`) to locate targets for healing spells or abilities.

The class supports two modes of operation determined at construction: absolute health deficit (comparing raw hit points) and relative health deficit (comparing percentage of maximum health). It strictly filters out non-combatants, dead units, and unselectable entities to ensure that healing resources are directed only toward active participants in combat who are eligible to be targeted.

## Member-by-Member Behavior

### **MostHPMissingInRangeCheck** (Constructor)
The constructor initializes the predicate with the necessary context for evaluation:
*   `obj`: The `Unit` acting as the origin point for distance calculations and friendship checks.
*   `range`: The maximum distance within which candidate units must reside.
*   `hp`: The threshold value. Its interpretation depends on the `percent` flag.
*   `percent`: A boolean flag. If `true`, `hp` represents a percentage (e.g., 50 means "missing more than 50% HP"). If `false`, `hp` represents an absolute number of hit points.

### **operator()** (Method)
This method evaluates whether a given `Unit* u` satisfies the criteria for being a valid healing target. It returns `true` only if all of the following conditions are met:
1.  **Alive**: The unit `u` must be alive (`u->IsAlive()`).
2.  **In Combat**: The unit `u` must be actively engaged in combat (`u->IsInCombat()`). This prevents healing idle allies.
3.  **Friendly**: The origin unit `i_obj` must consider `u` friendly (`i_obj->IsFriendlyTo(u)`).
4.  **In Range**: The unit `u` must be within the specified `i_range` of `i_obj` (`i_obj->IsWithinDistInMap(u, i_range)`).
5.  **Selectable**: The unit `u` must not have the `UNIT_FLAG_NOT_SELECTABLE` flag set. This ensures the target can be selected by the healer.
6.  **Health Deficit**:
    *   If `i_percent` is `true`: The unit's missing health percentage (`100 - u->GetHealthPercent()`) must be greater than `i_hp`.
    *   If `i_percent` is `false`: The unit's missing absolute health (`u->GetMaxHealth() - u->GetHealth()`) must be greater than `i_hp`.

If any condition fails, the method returns `false`.

## Cross-Unit Boundaries

*   **Called by `Unit.Main/FindLowestHpFriendlyUnit`**:
    The MAP indicates that this check is instantiated and used by `Unit.Main/FindLowestHpFriendlyUnit`. In this collaboration, `Unit.Main` provides the context (the healer unit, the range, and the threshold) to construct a `MostHPMissingInRangeCheck` instance. This instance is then passed to a grid searcher (likely `UnitSearcher` or similar) to iterate over nearby units. The searcher invokes `operator()` on each candidate unit. The result determines if the unit is a viable candidate for the "lowest HP" selection logic implemented in `Unit.Main`. The data crossing the boundary is the configuration parameters (origin unit, range, threshold, mode) during construction, and the boolean result of the evaluation during iteration.

## Data Model

This unit does not interact with any database tables. It operates entirely on in-memory `Unit` objects and their current states (health, flags, combat status, position).

## Notable Implementation Details

*   **Combat Requirement**: Unlike many other "friendly in range" checks (e.g., `AnyFriendlyUnitInObjectRangeCheck`), this check explicitly requires `u->IsInCombat()`. This is a critical design choice for healing AI, ensuring that healers prioritize targets actively fighting rather than those standing safely behind.
*   **Selectability Check**: The check explicitly verifies `!u->HasFlag(UNIT_FIELD_FLAGS, UNIT_FLAG_NOT_SELECTABLE)`. This prevents the AI from attempting to heal units that are visually present but mechanically untargetable (e.g., certain NPCs, pets, or units with specific buffs/debuffs).
*   **Percentage vs. Absolute**: The dual-mode support allows flexible usage. Percentage mode is useful for global healing thresholds (e.g., "heal anyone below 50%"), while absolute mode is useful for specific high-value targets or low-level content where raw HP numbers matter more.
*   **Const Correctness**: The `operator()` is marked `const`, indicating it does not modify the internal state of the `MostHPMissingInRangeCheck` instance. This makes it safe for concurrent use or repeated invocation by searchers.
*   **Friendship Direction**: The friendship check is `i_obj->IsFriendlyTo(u)`. This relies on the perspective of the healer (`i_obj`). It does not check if `u` considers `i_obj` friendly, which is consistent with how targeting usually works in WoW-like mechanics (the caster must perceive the target as friendly).

## Member Reference

**MostHPMissingInRangeCheck**
Constructor that initializes the check with the origin unit, range, health threshold, and a flag indicating whether the threshold is a percentage or absolute value.

**operator()**
Evaluates a candidate unit to determine if it is alive, in combat, friendly to the origin, within range, selectable, and missing more health than the specified threshold (either in percentage or absolute terms). Returns `true` if all conditions are met, `false` otherwise.

---

<!-- machine-true, projected from graph.json -->

## Map — MostHPMissingInRangeCheck

*Source:* GridNotifiers.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| MostHPMissingInRangeCheck | ctor | — | Unit.Main/FindLowestHpFriendlyUnit | — |
| operator() | method | — | — | — |

---

<!-- verify: failed-members | invented: operator -->
