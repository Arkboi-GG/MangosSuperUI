<!-- provenance: failed-members -->
# NearestFriendlyGuardInRangeCheck

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# NearestFriendlyGuardInRangeCheck

**Purpose & Responsibilities**

`NearestFriendlyGuardInRangeCheck` is a predicate functor defined in `GridNotifiers.h` within the `MaNGOS` namespace. It implements the "Check" pattern used by the server's grid-based spatial query system to locate specific entities within the game world. Specifically, this class identifies the **nearest friendly guard** creature relative to a source `Creature`, subject to strict constraints: the guard must be alive, not currently engaged in combat, explicitly flagged as a guard type, friendly to the source, within a specified maximum range, and visible via Line-of-Sight (LOS).

This class is designed to be used with searchers like `CreatureLastSearcher` (also in `GridNotifiers.h`). Because `NearestFriendlyGuardInRangeCheck` updates its internal range threshold every time it finds a valid candidate, iterating through a grid with this check ensures that the final result is the geometrically closest valid guard, rather than just the first one encountered in memory order.

**Member-by-Member Behavior**

The class contains four primary members exposed in the MAP, plus a deleted copy constructor for safety.

*   **`NearestFriendlyGuardInRangeCheck` (Constructor)**
    Initializes the checker with a pointer to the source `Creature` (`i_obj`) and a maximum search radius (`i_range`). The source creature serves as the anchor point for all distance and visibility calculations.

*   **`GetFocusObject`**
    Returns a constant reference to the source `Creature` (`i_obj`). This method is required by the `Check` interface contract defined in the comments of `GridNotifiers.h`. It allows the associated `Searcher` (e.g., `CreatureSearcher`) to determine the phase mask of the focus object, ensuring that only objects in the same phase are considered during the grid traversal.

*   **`operator()`**
    This is the core evaluation logic invoked for every `Creature` in the relevant grid cells. It performs a series of short-circuit checks to filter candidates:
    1.  **Identity Check:** Rejects the source creature itself (`u == i_obj`).
    2.  **Life State:** Rejects dead creatures (`!u->IsAlive()`).
    3.  **Combat State:** Rejects creatures already in combat (`u->IsInCombat()`). This is a critical constraint, ensuring that only idle guards are selected, likely to prevent pulling aggro from active fights or selecting guards who are already busy.
    4.  **Type Check:** Rejects creatures that are not guards (`!u->IsGuard()`).
    5.  **Faction Check:** Rejects creatures not friendly to the source (`!u->IsFriendlyTo(i_obj)`).
    6.  **Distance Check:** Rejects creatures outside the current `i_range` (`!i_obj->IsWithinDistInMap(u, i_range)`). Note that `i_range` shrinks as closer guards are found.
    7.  **Line-of-Sight Check:** Rejects creatures not visible via LOS (`!i_obj->IsWithinLOSInMap(u)`).
    
    If all checks pass, the method updates `i_range` to the actual distance between the source and the candidate (`i_obj->GetDistance(u)`). This shrinking range ensures that subsequent candidates must be *closer* than the current best to be accepted. It then returns `true` to signal a match.

*   **`GetLastRange`**
    Returns the final value of `i_range` after the search completes. This allows the caller to know the distance to the nearest found guard, or the original max range if no guard was found.

*   **`NearestFriendlyGuardInRangeCheck#2` (Deleted Copy Constructor)**
    The declaration `NearestFriendlyGuardInRangeCheck(NearestFriendlyGuardInRangeCheck const&);` in the private section prevents copying. This is a standard idiom in this codebase to prevent accidental duplication of the checker object, which could lead to inconsistent state if multiple copies were used in parallel searches.

**Cross-Unit Boundaries**

*   **Called by `Creature.Main/FindNearestFriendlyGuard`:**
    As indicated in the MAP, this checker is instantiated and used by logic in the `Creature` class (specifically the `Main` partial, likely in `Creature.cpp`). The `Creature.Main` unit creates an instance of `NearestFriendlyGuardInRangeCheck` and passes it to a grid searcher (likely `CreatureLastSearcher` from `GridNotifiers.h`). The collaboration flows as follows:
    1.  `Creature.Main` defines the source creature and desired range.
    2.  `Creature.Main` instantiates `NearestFriendlyGuardInRangeCheck`.
    3.  `Creature.Main` invokes the grid search mechanism.
    4.  The grid search iterates over nearby creatures, calling `NearestFriendlyGuardInRangeCheck::operator()` for each.
    5.  `NearestFriendlyGuardInRangeCheck` filters and ranks candidates.
    6.  The search mechanism returns the best candidate to `Creature.Main`.

    There are no outgoing calls from `NearestFriendlyGuardInRangeCheck` to other units; it relies entirely on methods provided by the `Creature` and `Unit` classes (which are part of the core entity hierarchy, not separate "units" in the context of this modular map, but are external dependencies).

**Data Model**

This unit does not interact with any database tables. It operates purely on in-memory game state objects (`Creature` instances) located within the server's spatial grid.

**Notable Implementation Details**

1.  **Combination of "Last" Searcher and Shrinking Range:**
    The comment in `GridNotifiers.h` explains the pattern: *"Success at unit in range, range update for next check (this can be use with CreatureLastSearcher to find nearest creature)."*
    Standard searchers often stop at the *first* match. However, `CreatureLastSearcher` continues iterating through the entire grid cell set, updating the result whenever `operator()` returns `true`. By shrinking `i_range` inside `operator()`, `NearestFriendlyGuardInRangeCheck` ensures that only progressively closer guards are accepted. This combination effectively implements a "find nearest" query without requiring a pre-sorted list or expensive distance calculations for every single object in the grid (since `IsWithinDistInMap` is typically a fast bounding-box or squared-distance check before the precise `GetDistance` calculation).

2.  **Strict Combat Exclusion:**
    The check `!u->IsInCombat()` is significant. Many similar checkers (e.g., `AnyFriendlyUnitInObjectRangeCheck`) do not exclude units in combat. This suggests that the use case for `NearestFriendlyGuardInRangeCheck` is specifically for finding *available* assistance or idle guards, rather than just any friendly guard. Pulling a guard already in combat might be undesirable or impossible depending on the AI logic.

3.  **Line-of-Sight Requirement:**
    Unlike some simpler range checks, this class enforces `IsWithinLOSInMap`. This adds computational cost but ensures realism; a guard behind a wall cannot assist or be targeted by the source creature.

4.  **Const-Correctness and Immutability:**
    The `i_obj` member is a `const*`, ensuring the source creature cannot be modified by the checker. The `i_range` member is mutable (implicitly, as it is updated in `operator()`), allowing the stateful "shrinking range" behavior. The class is not thread-safe, as is typical for these short-lived functor objects used in single-threaded grid iterations.

5.  **Prevention of Cloning:**
    The private copy constructor `NearestFriendlyGuardInRangeCheck(NearestFriendlyGuardInRangeCheck const&);` is a deliberate design choice to prevent accidental copies. If a copy were made, it would have its own `i_range`, potentially leading to incorrect results if the copy were used in a separate search context. This forces the user to pass the checker by reference, which is how the `Searcher` templates expect it.

## Member Reference

**NearestFriendlyGuardInRangeCheck**
Constructor that initializes the checker with a source `Creature` pointer and a maximum search range. Sets up the initial state for the nearest-guard search algorithm.

**GetFocusObject**
Returns a constant reference to the source `Creature` (`i_obj`). Used by the grid searcher to determine phase masking and other contextual properties of the search origin.

**operator()**
The predicate function executed for each candidate `Creature`. Filters out non-guards, dead units, units in combat, hostile units, units out of range, and units blocked by LOS. Updates the internal range to the distance of the current best candidate if valid, enabling the "nearest" selection logic.

**GetLastRange**
Returns the final range value after the search. Useful for determining the distance to the found guard or verifying if a guard was found within the original bounds.

**NearestFriendlyGuardInRangeCheck#2**
Private deleted copy constructor. Prevents accidental copying of the checker object, ensuring state integrity during grid searches.

---

<!-- machine-true, projected from graph.json -->

## Map — NearestFriendlyGuardInRangeCheck

*Source:* GridNotifiers.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| NearestFriendlyGuardInRangeCheck | ctor | — | Creature.Main/FindNearestFriendlyGuard | — |
| GetFocusObject | method | — | — | — |
| operator() | method | — | — | — |
| GetLastRange | method | — | — | — |
| NearestFriendlyGuardInRangeCheck#2 | decl | — | — | — |

---

<!-- verify: failed-members | invented: operator -->
