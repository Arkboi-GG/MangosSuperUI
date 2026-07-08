<!-- provenance: failed-members -->
# NearestGameObjectFishingHoleCheck

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# NearestGameObjectFishingHoleCheck

**Purpose & Responsibilities**

`NearestGameObjectFishingHoleCheck` is a predicate functor defined in `GridNotifiers.h` within the `MaNGOS` namespace. Its specific responsibility is to identify valid fishing holes (`GAMEOBJECT_TYPE_FISHINGHOLE`) within a certain radius of a `WorldObject` (typically a `Player` or `Creature` attempting to fish).

Unlike simple range checks, this class implements a "nearest-first" search strategy. It is designed to be used with grid-based searchers (such as `GameObjectLastSearcher` or similar iterative searchers in the MaNGOS grid system) that evaluate candidates sequentially. By updating its internal range threshold upon finding a valid candidate, it ensures that subsequent evaluations only accept objects closer than the previously found best match. This allows the caller to efficiently determine the closest valid fishing hole without sorting all candidates.

The class is strictly conditional on client build versions greater than `CLIENT_BUILD_1_6_1`. For older builds, the check always fails, reflecting that fishing hole mechanics were introduced or standardized in later versions of the World of Warcraft client supported by this server core.

**Member-by-Member Behavior**

*   **`NearestGameObjectFishingHoleCheck` (Constructor)**
    Initializes the checker with a reference to the source `WorldObject` (`i_obj`) and an initial maximum search radius (`i_range`). The `i_range` acts as the upper bound for the first evaluation; any fishing hole further away than this is immediately rejected.

*   **`operator()`**
    This is the core evaluation logic invoked by the grid searcher for each `GameObject` encountered.
    1.  **Build Check:** If the compiled client build is not greater than `CLIENT_BUILD_1_6_1`, it returns `false` immediately.
    2.  **Type Check:** Verifies the `GameObject` is of type `GAMEOBJECT_TYPE_FISHINGHOLE`.
    3.  **Spawn State:** Ensures the fishing hole is currently spawned (`go->isSpawned()`). Unspawned holes are invalid targets.
    4.  **Initial Range Check:** Confirms the fishing hole is within the current `i_range` of the source object (`i_obj`).
    5.  **Radius Check:** Confirms the source object is within the specific radius defined by the fishing hole's game object data (`go->GetGOInfo()->fishinghole.radius`). This ensures the player/caster is close enough to the hole itself to interact with it, distinct from just being within the general search radius.
    6.  **Update Best Match:** If all conditions pass, it updates `i_range` to the exact distance between `i_obj` and this specific fishing hole (`i_obj.GetDistance(go)`). This tightens the constraint for future evaluations, ensuring only closer holes are accepted.
    7.  Returns `true` to indicate this object is a valid candidate.

*   **`GetFocusObject`**
    Returns a constant reference to `i_obj`. This is required by the grid searcher interface to determine the phase mask and spatial context for the search.

*   **`GetLastRange`**
    Returns the current value of `i_range`. After a search completes, this value represents the distance to the nearest valid fishing hole found. If no hole was found, it retains the initial input range.

*   **`NearestGameObjectFishingHoleCheck#2` (Copy Constructor Declaration)**
    Declared in the private section to prevent copying. This enforces that the checker instance is unique and prevents accidental duplication which could lead to inconsistent state or memory issues if the object were copied while in use by a searcher.

**Cross-Unit Boundaries**

*   **Called by `GameObject/LookupFishingHoleAround`:**
    The primary consumer of this checker is the `LookupFishingHoleAround` method (likely located in `GameObject.cpp` or a related lookup utility). This method creates an instance of `NearestGameObjectFishingHoleCheck` and passes it to a grid searcher (e.g., `GameObjectLastSearcher`). The searcher iterates through nearby game objects, invoking `operator()` on this checker for each candidate. The collaboration allows `LookupFishingHoleAround` to delegate the complex filtering logic (type, spawn state, dual-radius validation) to this specialized functor, keeping the search algorithm generic.

*   **Calls Out:**
    This unit does not call out to other external units directly in its member functions. It relies on methods of `WorldObject` (e.g., `IsWithinDistInMap`, `GetDistance`) and `GameObject` (e.g., `GetGOInfo`, `isSpawned`) which are part of the core object hierarchy, not separate architectural units in the context of this map.

**Data Model**

This unit does not interact directly with database tables. It operates entirely on in-memory `GameObject` instances and their associated static data (`GameObjectInfo`). The `GameObjectInfo` structure contains the `fishinghole.radius` value, which is loaded from the `gameobject_template` table during server startup, but `NearestGameObjectFishingHoleCheck` does not perform any SQL queries or direct table access.

**Notable Implementation Details**

*   **Dual Radius Validation:** The check validates two distances:
    1.  Distance from the caster to the hole must be less than the *searcher's* current best range (`i_range`).
    2.  Distance from the caster to the hole must be less than the *hole's* intrinsic interaction radius (`go->GetGOInfo()->fishinghole.radius`).
    This ensures that a fishing hole is not only the "closest" among those found so far but also actually reachable/usable by the caster according to the hole's specific design parameters.

*   **State Mutation:** The `operator()` mutates the internal `i_range` state. This is a critical design pattern for "nearest" searchers in MaNGOS. The searcher typically iterates through objects in an arbitrary order (often by grid cell). By shrinking `i_range` after each success, the functor effectively filters out any subsequent objects that are farther away than the current best, optimizing the search by reducing the number of successful matches passed back to the caller.

*   **Client Build Dependency:** The entire logic is wrapped in `#if SUPPORTED_CLIENT_BUILD > CLIENT_BUILD_1_6_1`. This indicates that fishing hole mechanics as implemented here are irrelevant for older client versions. Maintainers must ensure that any code path using this checker also respects this build constraint or handles the case where no fishing holes are ever found due to this preprocessor guard.

*   **Non-Copyable:** The private copy constructor prevents accidental copying. Since the object holds a reference to `i_obj` and mutable state `i_range`, copying would create a shallow copy with potentially dangling references or split state, leading to undefined behavior if both copies were used in searches.

## Member Reference

**NearestGameObjectFishingHoleCheck**
Constructor that initializes the checker with a reference to the source `WorldObject` and an initial maximum search radius. Sets up the state for identifying the nearest valid fishing hole.

**GetFocusObject**
Returns a constant reference to the source `WorldObject` (`i_obj`). Used by grid searchers to determine phase masks and spatial context for the search operation.

**operator()**
The predicate function evaluated for each `GameObject`. Validates that the object is a spawned fishing hole, within the current best range, and within the hole's specific interaction radius. Updates the internal range to the distance of the found hole if valid, returning `true`. Always returns `false` for client builds <= 1.6.1.

**GetLastRange**
Returns the current value of `i_range`, which reflects the distance to the nearest valid fishing hole found during the search, or the initial range if none was found.

**NearestGameObjectFishingHoleCheck#2**
Private declaration of the copy constructor to prevent instantiation via copying, ensuring state integrity and preventing dangling references.

---

<!-- machine-true, projected from graph.json -->

## Map — NearestGameObjectFishingHoleCheck

*Source:* GridNotifiers.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| NearestGameObjectFishingHoleCheck | ctor | — | GameObject/LookupFishingHoleAround | — |
| GetFocusObject | method | — | — | — |
| operator() | method | — | — | — |
| GetLastRange | method | — | — | — |
| NearestGameObjectFishingHoleCheck#2 | decl | — | — | — |

---

<!-- verify: failed-members | invented: operator -->
