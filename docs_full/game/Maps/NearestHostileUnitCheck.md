<!-- provenance: failed-members -->
# NearestHostileUnitCheck

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# NearestHostileUnitCheck

**Purpose & Responsibilities**

`NearestHostileUnitCheck` is a predicate functor defined in `GridNotifiers.h` within the `MaNGOS` namespace. Its sole responsibility is to evaluate whether a specific `Unit` qualifies as a valid, hostile target for a searching `WorldObject`, while simultaneously maintaining state to identify the *nearest* such unit encountered during a grid traversal.

It implements the "Check" pattern used by the MaNGOS grid notification system (specifically `UnitSearcher` or `UnitLastSearcher`). When a searcher iterates over units in a grid cell, it invokes `operator()` on this check object. If the unit passes all hostility, validity, and distance criteria, the check returns `true` and updates its internal range threshold to the distance of the found unit. This ensures that subsequent units must be closer than the current best candidate to be considered, effectively performing a nearest-neighbor search without requiring a post-sort operation.

This unit is primarily used by AI logic (`Unit.Main/SelectNearestTarget`) to determine immediate combat targets and by player interaction logic (`WorldObject.Object/FindNearestHostilePlayer`) to locate nearby enemies.

## Member-by-Member Behavior

### `NearestHostileUnitCheck` (Constructor)
The constructor initializes the check context. It accepts a pointer to the `source` `WorldObject` (the entity performing the search) and an optional `dist` parameter representing the maximum search radius.
*   **Initialization**: It stores the `source` in the private member `me`.
*   **Range Logic**: If `dist` is provided and non-zero, `m_range` is set to `dist`. If `dist` is zero (or omitted), `m_range` defaults to `9999`. This large default value acts as an "infinity" placeholder, ensuring that the first valid hostile unit found will always pass the initial distance check, thereby establishing the baseline for the "nearest" calculation.

### `operator()`
This method defines the evaluation logic for a candidate `Unit* u`. It performs a series of sequential filters. If any filter fails, it returns `false` immediately. If all pass, it updates the internal state and returns `true`.

1.  **Self-Exclusion**: Checks if `me == u`. A unit cannot target itself. Returns `false` if true.
2.  **Distance Check**: Calls `me->IsWithinDistInMap(u, m_range)`. This verifies that the candidate unit is within the current best-known range (`m_range`) and exists in the same map instance. This is the critical optimization step: as the search progresses and `m_range` shrinks, distant units are rejected early.
3.  **Hostility Check**: Calls `me->IsHostileTo(u)`. Ensures the relationship between the searcher and the target is hostile.
4.  **Attack Validity Check**: Calls `me->IsValidAttackTarget(u)`. This is a higher-level validation that likely incorporates factors like stealth immunity, death status, or specific attack restrictions beyond simple faction hostility.
5.  **State Update**: If the unit passes all checks, `m_range` is updated to `me->GetDistance(u)`. This tightens the constraint for all subsequent evaluations in the search loop.
6.  **Result**: Returns `true`.

### `NearestHostileUnitCheck#2` (Declaration)
The MAP lists a second declaration `NearestHostileUnitCheck#2`. In the provided source code, there is only one class definition named `NearestHostileUnitCheck`. The `#2` designation in the MAP typically indicates a duplicate symbol or a template instantiation artifact in the analysis tool. Since the source contains only one concrete class definition with this name, this entry refers to the same logical entity described above. No distinct behavior exists for a separate second class.

## Cross-Unit Boundaries

`NearestHostileUnitCheck` does not initiate calls to other units; it is a passive functor invoked by searchers. However, it relies heavily on methods provided by the `WorldObject` and `Unit` classes (which belong to other units/files, such as `Unit.cpp` or `WorldObject.cpp`).

*   **Called By: `Unit.Main/SelectNearestTarget`**
    *   **Direction**: `Unit.Main` creates an instance of `NearestHostileUnitCheck` and passes it to a grid searcher (likely `UnitSearcher` or `UnitLastSearcher` from `GridNotifiers.h`).
    *   **Collaboration**: The AI system needs to find the closest enemy to attack. It uses this check to filter the grid contents. The check delegates the complex logic of "is this unit hostile?" and "is this unit attackable?" to the `Unit` class methods, keeping the check itself lightweight and focused on iteration efficiency.

*   **Called By: `WorldObject.Object/FindNearestHostilePlayer`**
    *   **Direction**: Similar to above, `WorldObject.Object` instantiates this check to find the nearest hostile *player*.
    *   **Collaboration**: While the check evaluates any `Unit`, the caller likely restricts the search scope to players via the searcher type (e.g., `PlayerSearcher`) or by filtering results after the search. The check ensures that among the candidates presented, only valid hostile targets are selected, prioritizing proximity.

## Data Model

This unit operates entirely in memory using runtime object states. It does not query or modify any database tables.

## Notable Implementation Details

1.  **Mutable State in Functor**: Unlike pure predicates, `NearestHostileUnitCheck` modifies its internal state (`m_range`) upon success. This side effect is intentional and necessary for the "nearest" algorithm. It assumes that the searcher iterates through candidates in an order where updating the range allows for pruning. If the searcher were to iterate randomly, this logic would still work but might be less efficient; however, grid searches typically proceed in a structured manner.
2.  **Default Range of 9999**: The use of `9999` as a default "infinite" range is a hardcoded magic number. It implies that the engine assumes no relevant combat interaction occurs beyond 9999 units (yards/meters). If a map or scenario requires larger scales, this constant would need adjustment.
3.  **Copy Prevention**: The class declares a private copy constructor `NearestHostileUnitCheck(NearestHostileUnitCheck const&);` but does not define it. This prevents accidental copying of the check object. Copying would be dangerous because it would duplicate the `m_range` state, breaking the nearest-neighbor logic if multiple copies were used in parallel or if the object was passed by value unexpectedly.
4.  **Order of Checks**: The sequence of checks is optimized for performance:
    *   Self-check (cheapest, pointer comparison).
    *   Distance check (relatively cheap spatial query).
    *   Hostility check (state lookup).
    *   Valid Attack Target (potentially more expensive logic).
    This ordering ensures that expensive validations are only performed on units that are already close and potentially hostile.
5.  **Map Consistency**: The use of `IsWithinDistInMap` rather than `IsWithinDist` ensures that units on different maps (e.g., different instances or zones) are correctly excluded, even if their coordinates overlap in world space.

## Member Reference

**NearestHostileUnitCheck**
Constructor that initializes the checker with a source `WorldObject` and an optional maximum distance. Sets `m_range` to the provided distance or 9999 if zero. Prevents self-targeting and establishes the initial search radius.

**operator()**
Evaluation method that determines if a candidate `Unit` is a valid hostile target. Returns `true` if the unit is not self, is within the current `m_range`, is hostile to the source, and is a valid attack target. Updates `m_range` to the distance of the found unit to enforce nearest-neighbor selection for subsequent candidates.

**NearestHostileUnitCheck#2**
Referenced in the MAP but corresponds to the same class definition as `NearestHostileUnitCheck` in the source. No distinct implementation exists; treated as the same entity.

---

<!-- machine-true, projected from graph.json -->

## Map — NearestHostileUnitCheck

*Source:* GridNotifiers.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| NearestHostileUnitCheck | ctor | — | Unit.Main/SelectNearestTarget, WorldObject.Object/FindNearestHostilePlayer | — |
| operator() | method | — | — | — |
| NearestHostileUnitCheck#2 | decl | — | — | — |

---

<!-- verify: failed-members | invented: operator -->
