<!-- provenance: failed-members -->
# NearestFriendlyUnitCheck

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# NearestFriendlyUnitCheck

**Purpose & Responsibilities**

`NearestFriendlyUnitCheck` is a predicate functor defined in `GridNotifiers.h` within the `MaNGOS` namespace. Its sole responsibility is to determine whether a specific `Unit` is a valid candidate for being the "nearest friendly unit" relative to a source `WorldObject`. It is designed to be used in conjunction with grid-based search utilities (such as `UnitLastSearcher`) to efficiently locate the closest friendly entity within a specified radius.

The class implements a "nearest-first" search optimization strategy. By updating its internal range threshold upon finding a valid candidate, it allows the calling search algorithm to prune subsequent checks against units that are farther away than the current best match.

**Member-by-Member Behavior**

### **NearestFriendlyUnitCheck** (Constructor)
Initializes the checker with a source object (`me`) and an optional maximum distance (`dist`).
*   If `dist` is provided and non-zero, `m_range` is set to `dist`.
*   If `dist` is zero or omitted, `m_range` defaults to `9999`, effectively acting as an infinite range for the initial search.
*   The constructor is marked `explicit` to prevent implicit conversions.

### **operator()**
This method evaluates a candidate `Unit* u` against the criteria for being the nearest friendly unit. It returns `true` if the unit is a valid candidate and updates the search radius; otherwise, it returns `false`.

The evaluation logic proceeds as follows:
1.  **Self-Exclusion:** If the candidate `u` is the same object as the source `me`, it returns `false`.
2.  **Distance Check:** It verifies if `u` is within the current `m_range` of `me` using `IsWithinDistInMap`. If not, it returns `false`.
3.  **Friendship Check:** It verifies if `me` considers `u` friendly via `IsFriendlyTo`. If not, it returns `false`.
4.  **Range Update:** If all checks pass, it updates `m_range` to the exact distance between `me` and `u` (`me->GetDistance(u)`). This tightens the search constraint for future candidates.
5.  **Result:** Returns `true`.

**Cross-Unit Boundaries**

*   **Called by `WorldObject.Object/FindNearestFriendlyPlayer`:**
    The MAP indicates that `NearestFriendlyUnitCheck` is called by `WorldObject.Object/FindNearestFriendlyPlayer`. In the context of MaNGOS/WoWVMaNGOS, `WorldObject` methods like `FindNearestFriendlyPlayer` typically instantiate a `NearestFriendlyUnitCheck` (or a similar specialized check) and pass it to a grid searcher (like `UnitLastSearcher`). The searcher iterates through units in the grid, invoking `operator()` on this check. The check's role is to filter units and maintain the "best so far" distance, allowing the searcher to stop early or skip distant units.

*   **Calls Out:** None. The `operator()` method relies entirely on member functions of `WorldObject` and `Unit` (e.g., `IsWithinDistInMap`, `IsFriendlyTo`, `GetDistance`) which are part of the core entity hierarchy, not distinct external units in the MAP sense. It does not call other grid notifier structures or database interfaces.

**Data Model**

This unit does not interact with any database tables. It operates purely on in-memory object states and spatial relationships.

**Notable Implementation Details**

1.  **Mutable State for Optimization:** Unlike many pure predicates, `NearestFriendlyUnitCheck` modifies its internal state (`m_range`) during execution. This is intentional and critical for performance. It transforms the search from a simple filter into a "find minimum" operation. The caller (the grid searcher) must be aware that the check object is modified and that the final `m_range` value represents the distance to the found unit.
2.  **Default Infinite Range:** The constructor's handling of `dist == 0` by setting `m_range` to `9999` is a common pattern in this codebase to represent "no limit." Maintainers should note that `9999` is an arbitrary large number, not a true infinity. If a map area exceeds this size (unlikely in WoW, but theoretically possible in custom maps), the search might incorrectly exclude valid distant units.
3.  **Copy Prevention:** The class declares a private copy constructor `NearestFriendlyUnitCheck(NearestFriendlyUnitCheck const&)` but does not define it. This prevents accidental copying of the checker, which would break the state-sharing mechanism required for the nearest-search optimization. If copied, the copy would have its own `m_range`, leading to incorrect pruning behavior in the searcher.
4.  **Friendship Logic:** The check relies on `IsFriendlyTo`. This method encapsulates complex faction, team, and PvP logic from the `Unit` class. `NearestFriendlyUnitCheck` does not implement friendship rules itself; it delegates them. Changes to how "friendliness" is determined in `Unit` will automatically affect this check.
5.  **Map Awareness:** The distance check uses `IsWithinDistInMap`, which accounts for map boundaries and potentially different map instances (though typically used within the same map). This ensures that units on different maps (even if coordinates are close) are not considered.

## Member Reference

**NearestFriendlyUnitCheck**
Constructor that initializes the source object (`me`) and the initial search range (`m_range`). If the provided distance is zero, it defaults to 9999. Prevents implicit conversion.

**operator()**
Predicate method that checks if a given `Unit` is friendly to the source, within the current `m_range`, and not the source itself. If valid, it updates `m_range` to the distance to this unit and returns `true`; otherwise, returns `false`. Used to iteratively find the nearest friendly unit.

**NearestFriendlyUnitCheck#2**
Declaration of the private copy constructor, preventing copying of the object to preserve state integrity during search operations.

---

<!-- machine-true, projected from graph.json -->

## Map — NearestFriendlyUnitCheck

*Source:* GridNotifiers.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| NearestFriendlyUnitCheck | ctor | — | WorldObject.Object/FindNearestFriendlyPlayer | — |
| operator() | method | — | — | — |
| NearestFriendlyUnitCheck#2 | decl | — | — | — |

---

<!-- verify: failed-members | invented: operator -->
