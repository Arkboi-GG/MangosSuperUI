<!-- provenance: failed-members -->
# AllCreaturesOfEntryInRange

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# AllCreaturesOfEntryInRange

**Purpose & Responsibilities**

`AllCreaturesOfEntryInRange` is a predicate functor defined in the `MaNGOS` namespace within `GridNotifiers.h`. Its sole responsibility is to filter `Unit` objects during spatial searches performed by the server's grid system. Specifically, it identifies units that match a specific database entry ID (`uiEntry`) and fall within a defined radius (`fMaxRange`) of a reference `WorldObject` (`m_pObject`).

This class is part of the broader "Searcher/Check" pattern used throughout MaNGOS to decouple spatial iteration logic (handled by grid visitors like `CreatureListSearcher`) from the specific filtering criteria required by high-level game logic (such as summoning, targeting, or area-of-effect calculations). It does not perform the search itself; it only returns `true` or `false` for a given candidate unit.

**Member-by-Member Behavior**

The unit consists of two members: a constructor and the call operator.

*   **`AllCreaturesOfEntryInRange` (Constructor)**
    Initializes the predicate with three parameters:
    1.  `pObject`: A pointer to the `WorldObject` serving as the center point for the distance calculation.
    2.  `uiEntry`: The numeric entry ID of the creature type to look for (e.g., a specific mob ID from the database).
    3.  `fMaxRange`: The maximum distance (in game units) from `pObject` within which creatures are considered valid.
    
    These values are stored in private member variables `m_pObject`, `m_uiEntry`, and `m_fRange` respectively.

*   **`operator()` (Method)**
    This method implements the filtering logic. It accepts a single argument, `pUnit` (a pointer to a `Unit`), and returns a boolean indicating whether the unit satisfies the search criteria. The logic proceeds as follows:
    1.  **Entry Check:** It verifies if `pUnit->GetEntry()` equals `m_uiEntry`. If the unit's entry ID does not match the target ID, it returns `false`.
    2.  **Distance Check:** It calls `m_pObject->IsWithinDist(pUnit, m_fRange, false)`. This checks if the unit is within `m_fRange` of `m_pObject`. The third argument `false` indicates that the distance calculation should likely ignore vertical height differences (2D distance) or follow specific visibility rules defined in `WorldObject::IsWithinDist` (typically 2D horizontal distance in many MaNGOS contexts unless specified otherwise, though `IsWithinDist` often defaults to 3D depending on overload; here the explicit `false` usually disables strict 3D checking or LOS depending on the specific `WorldObject` implementation version, but primarily it enforces the radius constraint).
    3.  **Result:** Returns `true` only if both conditions are met.

**Cross-Unit Boundaries**

`AllCreaturesOfEntryInRange` acts as a leaf node in the call chain; it does not call out to other complex subsystems beyond basic `Unit` and `WorldObject` interface methods. However, it is heavily utilized by other units to perform spatial queries.

*   **Called By:**
    *   **`ChatHandler.CreatureCommands/UnsummonVisualWaypoints`**: Uses this predicate to locate and remove specific visual waypoint creatures near a target object.
    *   **`GridSearchers/GetCreatureListWithEntryInGrid#2`**: A grid search utility that iterates over grid cells and uses this predicate to populate a list of matching creatures.
    *   **`WorldObject.Object/GetCreatureListWithEntryInGrid`**: A method on the `WorldObject` class that delegates to grid searchers, passing this predicate to find all creatures of a specific entry within a grid range.

*   **Collaboration Pattern:**
    The typical usage pattern involves a caller creating an instance of `AllCreaturesOfEntryInRange` with the desired parameters. This instance is then passed to a searcher class (like `CreatureListSearcher`, defined in the same header but outside this specific unit's scope). The searcher iterates through the relevant grid data structures, calling `operator()` on each candidate `Unit`. If `operator()` returns `true`, the searcher adds the unit to the result list. This design allows the grid iteration code to remain generic while allowing callers to define arbitrary filtering logic.

**Data Model**

This unit does not interact directly with any database tables. It operates entirely on in-memory `Unit` objects and their properties (`GetEntry()`, position data). The `uiEntry` parameter corresponds to the `entry` column in the `creature_template` table, but the unit itself performs no SQL queries.

**Notable Implementation Details**

1.  **Predicate Design:** As a functor, `AllCreaturesOfEntryInRange` is designed to be lightweight and stateless after construction. It holds no dynamic memory allocations, making it efficient to instantiate and pass by value or reference to search algorithms.
2.  **Distance Calculation Nuance:** The call to `m_pObject->IsWithinDist(pUnit, m_fRange, false)` is critical. The `false` parameter typically signifies that the check should not enforce Line-of-Sight (LOS) or may indicate a 2D distance check depending on the specific overload resolution in the `WorldObject` class. In many MaNGOS versions, `IsWithinDist(obj, range, false)` calculates the 2D horizontal distance, ignoring Z-axis differences. This is important for spells or mechanics that affect a circular area on the ground regardless of elevation.
3.  **Type Safety:** Although the `operator()` takes a `Unit*`, it is primarily intended to filter `Creature` objects because `GetEntry()` is most semantically meaningful for creatures in this context (players have different identification mechanisms). However, since `Player` inherits from `Unit`, if a player somehow had the same entry ID (unlikely/impossible in standard WoW data), they would technically pass the entry check. The caller is responsible for ensuring the search context (e.g., using `CreatureListSearcher`) restricts candidates to creatures if necessary.
4.  **Const Correctness:** The `operator()` is not marked `const`, although it logically does not modify the object's state. This is consistent with other predicates in the file.

## Member Reference

**AllCreaturesOfEntryInRange**
Constructor that initializes the predicate with a reference object (`m_pObject`), a target creature entry ID (`m_uiEntry`), and a maximum search radius (`m_fRange`). These values are stored for use by the `operator()`.

**operator()**
The filtering function called by grid searchers. It accepts a `Unit*` and returns `true` if the unit's entry ID matches `m_uiEntry` AND the unit is within `m_fRange` distance of `m_pObject` (using `IsWithinDist` with the `false` flag, typically implying 2D distance or no LOS check). Otherwise, it returns `false`.

---

<!-- machine-true, projected from graph.json -->

## Map — AllCreaturesOfEntryInRange

*Source:* GridNotifiers.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| AllCreaturesOfEntryInRange | ctor | — | ChatHandler.CreatureCommands/UnsummonVisualWaypoints, GridSearchers/GetCreatureListWithEntryInGrid#2, WorldObject.Object/GetCreatureListWithEntryInGrid | — |
| operator() | method | — | — | — |

---

<!-- verify: failed-members | invented: operator -->
