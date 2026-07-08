<!-- provenance: failed-members -->
# NearestUnitFitConditionInCombatRangeCheck

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# NearestUnitFitConditionInCombatRangeCheck

**Purpose & Responsibilities**

`NearestUnitFitConditionInCombatRangeCheck` is a predicate functor within the `MaNGOS` namespace, defined in `GridNotifiers.h`. It serves as a specialized filter for grid-based spatial searches, specifically designed to locate the **nearest** `Unit` that satisfies a complex set of criteria involving entry ID, life state, combat range, and script-defined conditions.

Its primary responsibility is to act as the `Check` template parameter for searcher structures like `UnitLastSearcher` or `UnitSearcher`. Unlike simple range checks, this class implements an iterative "nearest" logic: upon finding a valid unit, it updates its internal range threshold to the distance of that unit. Subsequent evaluations against other units will fail if those units are farther away than the current best match. This allows the caller to efficiently retrieve the closest qualifying target without sorting all candidates.

It is primarily used by the spell targeting system (`Spell.Main/CheckScriptTargeting`) to resolve targets for spells that require specific unit entries, combat-range proximity, and additional conditional logic defined in the database or scripts.

**Member-by-Member Behavior**

The class contains four members relevant to its operation:

1.  **Constructor (`NearestUnitFitConditionInCombatRangeCheck`)**: Initializes the search parameters. It stores a reference to the origin object (`i_obj`), the required unit entry ID (`i_entry`), a boolean flag for life state (`i_alive`), the initial maximum search radius (`i_range`), and an optional condition ID (`i_conditionId`). The copy constructor is explicitly deleted to prevent accidental cloning, ensuring the mutable `i_range` state remains tied to the original instance used during the search traversal.

2.  **`GetFocusObject`**: Returns a constant reference to `i_obj`. This is required by the `GridNotifiers` framework to determine the phase mask and spatial context for the search. It ensures the searcher operates relative to the correct world position and phase.

3.  **`operator()`**: The core evaluation logic. It accepts a `Unit*` candidate and returns `true` if the unit meets all criteria and is closer than any previously accepted unit. The logic proceeds as follows:
    *   **Entry Check**: Verifies the candidate's entry ID matches `i_entry`.
    *   **Life State Check**: If `i_alive` is true, the unit must be alive. If false, the unit must be a `Creature` and specifically a corpse (`IsCorpse()`).
    *   **Combat Range Check**: Uses `IsWithinCombatDistInMap` to verify the unit is within `i_range`. This method typically accounts for melee reach or spell range mechanics specific to combat interactions, differing from standard geometric distance checks.
    *   **Condition Check**: If `i_conditionId` is non-zero, it invokes `IsConditionSatisfied` with the candidate unit, its map, the origin object, and the source type `CONDITION_FROM_SPELL_AREA`. This allows dynamic filtering based on complex script conditions.
    *   **Range Update**: If all checks pass, it updates `i_range` to the direct distance between `i_obj` and the candidate (`i_obj.GetDistance(u)`). This tightens the search radius for subsequent candidates, ensuring only nearer units are accepted.
    *   Returns `true` to signal acceptance.

4.  **`GetLastRange`**: Returns the final value of `i_range` after the search completes. This allows the caller to know the exact distance to the found unit, which is useful for spell effects that depend on the target's distance (e.g., damage falloff or travel time calculations).

**Cross-Unit Boundaries**

*   **Called by `Spell.Main/CheckScriptTargeting`**: The spell targeting system instantiates this checker to resolve targets for spells that use specific targeting types (likely `TARGET_TYPE_UNIT_ENTRY` combined with condition checks). The spell engine passes the caster, target entry, range, and condition ID to the constructor. The spell engine then uses a grid searcher (like `UnitLastSearcher`) to traverse nearby units, invoking this checker's `operator()` for each candidate. The spell engine subsequently calls `GetLastRange` to retrieve the distance to the resolved target.

**Data Model**

This unit does not directly access database tables. However, the `i_conditionId` parameter implies reliance on the `conditions` table (or similar condition storage) via the `IsConditionSatisfied` function. The specific columns and structure of this table are not exposed in this unit's code, but the condition ID acts as a foreign key to define the logical constraints applied during the `operator()` evaluation.

**Notable Implementation Details**

*   **Iterative Range Reduction**: The critical design pattern here is the mutation of `i_range` inside `operator()`. This transforms the checker from a simple boolean filter into a stateful "best-so-far" tracker. This is efficient because it avoids collecting all candidates and sorting them; instead, it prunes the search space dynamically.
*   **Combat Distance vs. Geometric Distance**: The use of `IsWithinCombatDistInMap` for the initial filter, followed by `GetDistance` for the update, is significant. `IsWithinCombatDistInMap` likely includes checks for melee reach or line-of-sight relevant to combat, whereas `GetDistance` provides the raw Euclidean distance. This ensures the target is valid for combat interaction while accurately recording the physical distance.
*   **Corpse Handling**: The explicit check for `IsCreature()` and `IsCorpse()` when `i_alive` is false ensures that only creature corpses are considered, ignoring player corpses or other non-creature dead entities. This is crucial for spells that target dead mobs (e.g., resurrection or loot-related spells).
*   **Condition Source**: The condition is evaluated with `CONDITION_FROM_SPELL_AREA`, indicating that the condition logic is treated as part of a spell's area or targeting effect. This affects how certain condition types (like aura checks or faction checks) are interpreted by the condition system.
*   **Copy Prevention**: The deleted copy constructor prevents accidental duplication of the checker instance. Since `i_range` is mutable and essential for the "nearest" logic, copying the object would reset or duplicate the state, breaking the search algorithm.

## Member Reference

**NearestUnitFitConditionInCombatRangeCheck**  
Constructs the checker with the origin object, target entry ID, life state requirement, initial range, and optional condition ID. Initializes `i_range` to the provided maximum range. Deletes the copy constructor to enforce single-instance statefulness.

**GetFocusObject**  
Returns a constant reference to the origin object (`i_obj`). Used by the grid search framework to determine phase and spatial context.

**operator()**  
Evaluates a candidate `Unit`. Returns `true` if the unit matches the entry ID, satisfies the life state requirement (alive or corpse), is within the current combat range, and passes any associated condition checks. Upon success, updates `i_range` to the distance of this unit to ensure only nearer units are accepted in future calls.

**GetLastRange**  
Returns the final value of `i_range`, representing the distance to the nearest unit that satisfied all criteria.

**NearestUnitFitConditionInCombatRangeCheck#2**  
Declaration placeholder; no distinct behavior beyond the primary class definition.

---

<!-- machine-true, projected from graph.json -->

## Map — NearestUnitFitConditionInCombatRangeCheck

*Source:* GridNotifiers.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| NearestUnitFitConditionInCombatRangeCheck | ctor | — | Spell.Main/CheckScriptTargeting | — |
| GetFocusObject | method | — | — | — |
| operator() | method | — | — | — |
| GetLastRange | method | — | Spell.Main/CheckScriptTargeting | — |
| NearestUnitFitConditionInCombatRangeCheck#2 | decl | — | — | — |

---

<!-- verify: failed-members | invented: operator -->
