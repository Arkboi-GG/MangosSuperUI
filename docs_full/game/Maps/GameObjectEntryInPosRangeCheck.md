<!-- provenance: failed-members -->
# GameObjectEntryInPosRangeCheck

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# GameObjectEntryInPosRangeCheck

## Purpose & Responsibilities

`GameObjectEntryInPosRangeCheck` is a predicate functor defined in `GridNotifiers.h` within the `MaNGOS` namespace. It serves as a specialized filter for grid-based searches, identifying `GameObject` instances that satisfy two specific criteria:
1.  The object’s database entry ID matches a specified `uint32` value.
2.  The object lies within a specified spherical radius of a fixed set of 3D coordinates (`x`, `y`, `z`).

Unlike other check classes in this file that calculate distance relative to a dynamic `WorldObject`’s current position (e.g., `NearestGameObjectEntryInObjectRangeCheck`), this class decouples the spatial query from any moving entity. It allows the caller to define an arbitrary point in space as the center of the search sphere. This is particularly useful for spell effects or mechanics that target locations independent of the caster’s or target’s immediate position.

The class implements the standard "Check" interface expected by the grid search infrastructure (such as `GameObjectSearcher` or `GameObjectListSearcher`). It provides:
*   **`operator()`**: The boolean evaluation logic applied to each candidate `GameObject`.
*   **`GetFocusObject()`**: Returns a reference to a `WorldObject` used by the searcher infrastructure to determine phase masks and visibility contexts.
*   **`GetLastRange()`**: Returns the configured search range, allowing callers to inspect the parameter after execution.

## Member-by-Member Behavior

### Construction and Initialization
**`GameObjectEntryInPosRangeCheck`**
The constructor initializes the functor with the following parameters:
*   `obj`: A reference to a `WorldObject`. This object acts as the "focus" for the search context. It is **not** used for distance calculations but is critical for phase mask validation. The searcher infrastructure uses this object to ensure that only `GameObject`s visible to `obj` (based on phase) are considered.
*   `entry`: The `uint32` entry ID of the `GameObject` being sought.
*   `x`, `y`, `z`: Floating-point coordinates defining the center of the search sphere.
*   `range`: The maximum radius of the search sphere.

These values are stored in private member variables (`i_obj`, `i_entry`, `i_x`, `i_y`, `i_z`, `i_range`). The class also declares a private copy constructor to prevent accidental duplication, enforcing that the functor is passed by reference or moved.

### Evaluation Logic
**`operator()`**
This method is invoked by grid searchers for each `GameObject` candidate in the relevant grid cells. It returns `true` if and only if both conditions are met:
1.  **Entry Match**: The candidate `GameObject`’s entry ID (`go->GetEntry()`) equals `i_entry`.
2.  **Spatial Proximity**: The candidate `GameObject` is within `i_range` units of the point `(i_x, i_y, i_z)`. This is determined by calling `go->IsWithinDist3d(i_x, i_y, i_z, i_range)`.

The check uses `IsWithinDist3d`, which calculates the Euclidean distance in 3D space, including vertical distance (Z-axis). It does not perform line-of-sight checks or phase mask validations internally; phase filtering is handled by the searcher infrastructure using the object returned by `GetFocusObject()`.

### Accessors
**`GetFocusObject`**
Returns a constant reference to `i_obj`. This satisfies the `Check` interface contract. The returned object is used by searchers (like `GameObjectSearcher`) to retrieve the phase mask (`GetPhaseMask()`) to ensure that only objects visible to the focus object are considered during the iteration.

**`GetLastRange`**
Returns the value of `i_range`. This allows the caller to verify the range used for the search, which can be useful for debugging or for logic that needs to know the exact boundary of the successful search.

## Cross-Unit Boundaries

### Called By
*   **`Spell.Main/SetTargetMap`**: According to the MAP, this functor is instantiated and utilized by the spell targeting system. Spells that need to locate a specific type of game object (identified by entry) near a specific location (which might be the caster’s position, a target’s position, or a calculated point) create an instance of this check. The spell system passes this functor to a grid searcher to populate a target list or validate a single target.

### Calls Out
*   **None**: The functor itself does not call into other architectural units. It relies on methods of the `GameObject` class (`GetEntry`, `IsWithinDist3d`) and the `WorldObject` class (via the reference returned by `GetFocusObject`). These are internal library calls within the core engine, not cross-unit dependencies in the architectural sense defined by the MAP.

## Data Model

This unit does not interact directly with database tables. It operates entirely on in-memory `GameObject` instances. The `entry` parameter corresponds to the `entry` column in the `gameobject` table (and related definition tables like `gameobject_template`), but the lookup is performed via the object’s already-loaded memory representation, not via SQL queries.

## Notable Implementation Details

1.  **Decoupled Spatial Reference**: The key distinction of this class compared to `NearestGameObjectEntryInObjectRangeCheck` is that the spatial center is fixed at construction time (`i_x`, `i_y`, `i_z`). This makes the position immutable. If the intended target location moves, a new functor instance must be created. This is efficient for static location checks but requires careful management if the location is dynamic.

2.  **Copy Prevention**: The class declares a private copy constructor `GameObjectEntryInPosRangeCheck(const GameObjectEntryInPosRangeCheck&)`. This prevents accidental copying of the functor. Since the functor holds a reference (`i_obj`), copying would technically work for the values but could lead to dangling references if the original object is destroyed or if the design intent is to treat it as a lightweight, non-copyable policy object passed by reference to searchers.

3.  **Phase Mask Dependency**: While the functor checks entry and distance, it does **not** check phase masks. The `GetFocusObject()` method returns `i_obj`, and the searcher infrastructure uses this to filter candidates by phase. This means the correctness of the search depends on `i_obj` having the correct phase mask for the desired results. If `i_obj` is in a different phase than the target `GameObject`, the searcher will exclude it before `operator()` is even called.

4.  **3D Distance Calculation**: The use of `IsWithinDist3d` implies that vertical distance (Z-axis) is included in the range check. This is appropriate for most spell effects but differs from some other checks that might use 2D distance (ignoring Z) for ground-based interactions.

## Member Reference

**GameObjectEntryInPosRangeCheck**
Constructor that initializes the functor with a focus `WorldObject`, a target `GameObject` entry ID, 3D coordinates (`x`, `y`, `z`), and a search `range`. It sets up the private member variables and disables copying via a private copy constructor declaration.

**GetFocusObject**
Returns a constant reference to the stored `WorldObject` (`i_obj`). Used by searchers to determine phase masks and initial search bounds.

**operator()**
The predicate function. Takes a `GameObject*` pointer. Returns `true` if the object's entry matches `i_entry` AND the object is within `i_range` distance of the point `(i_x, i_y, i_z)` using 3D distance calculation.

**GetLastRange**
Returns the stored `i_range` value. Allows callers to inspect the range parameter used for the search.

**GameObjectEntryInPosRangeCheck#2**
Declaration of the private copy constructor. Prevents copying of the functor instance.

---

<!-- machine-true, projected from graph.json -->

## Map — GameObjectEntryInPosRangeCheck

*Source:* GridNotifiers.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GameObjectEntryInPosRangeCheck | ctor | — | Spell.Main/SetTargetMap | — |
| GetFocusObject | method | — | — | — |
| operator() | method | — | — | — |
| GetLastRange | method | — | — | — |
| GameObjectEntryInPosRangeCheck#2 | decl | — | — | — |

---

<!-- verify: failed-members | invented: operator -->
