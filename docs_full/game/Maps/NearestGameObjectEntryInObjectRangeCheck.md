<!-- provenance: failed-members -->
# NearestGameObjectEntryInObjectRangeCheck

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# NearestGameObjectEntryInObjectRangeCheck

**Purpose & Responsibilities**

`NearestGameObjectEntryInObjectRangeCheck` is a predicate functor (a "Check" class) defined in `GridNotifiers.h` within the `MaNGOS` namespace. Its specific responsibility is to identify the **nearest** `GameObject` of a specific entry ID (`uint32`) that lies within a certain radius of a source `WorldObject`.

Unlike simple range checks that return a boolean for any object in range, this class implements an optimization strategy for iterative searches. It maintains an internal `i_range` state. When it finds a valid `GameObject`, it updates `i_range` to the distance of that specific object. Subsequent evaluations of other objects will only succeed if they are closer than the previously found best candidate. This allows grid search algorithms (such as `GameObjectLastSearcher`) to efficiently converge on the single closest object without requiring a post-search sort or a full scan of all candidates.

It is part of the broader spatial query system used by the server to manage visibility, targeting, and interaction logic for entities in the game world.

**Member-by-Member Behavior**

The class contains four primary members:

1.  **Constructor (`NearestGameObjectEntryInObjectRangeCheck`)**: Initializes the check with a reference to the source object (`i_obj`), the target `GameObject` entry ID (`i_entry`), and the initial maximum search radius (`i_range`).
2.  **`GetFocusObject`**: Returns a constant reference to the source `WorldObject` (`i_obj`). This is required by the grid notifier infrastructure to determine phase masks or other contextual properties relative to the searcher.
3.  **`operator()`**: The core evaluation logic. It accepts a `GameObject*`. It returns `true` if the object matches the entry ID AND is within the current `i_range` of the source object. Crucially, if it returns `true`, it shrinks `i_range` to the exact distance of this object, ensuring only closer objects will pass future checks in the same search iteration.
4.  **`GetLastRange`**: Returns the final value of `i_range` after a search completes. This tells the caller how far away the nearest found object was.

**Cross-Unit Boundaries**

This unit acts as a leaf node in the dependency graph; it does not call out to other complex subsystems but relies on base class methods of `WorldObject` and `GameObject`.

*   **Called By:**
    *   **`GameObject/RespawnLinkedGameObject`** & **`GameObject/TriggerLinkedGameObject`**: These units likely instantiate this check to find linked game objects (e.g., doors linked to buttons) that need to be respawned or triggered when the primary object changes state. They need the *nearest* link if multiple exist, or simply to verify proximity constraints.
    *   **`GridSearchers/GetClosestGameObjectWithEntry`**: This is the primary consumer. This grid searcher iterates through the spatial grid, applying this check to filter and narrow down the candidate set to the single closest match.
    *   **`WorldObject.Object/FindNearestGameObject`**: A convenience method on `WorldObject` that wraps the grid search machinery. It uses this check to provide a high-level API for finding nearby game objects by entry.

*   **Calls Out:**
    *   None explicitly listed in the MAP, but internally it calls `WorldObject::IsWithinDistInMap` and `WorldObject::GetDistance` on the `i_obj` reference. These are standard spatial calculation methods inherent to the `WorldObject` hierarchy.

**Data Model**

This unit performs purely in-memory spatial calculations. It does not interact with any database tables. The `entry` parameter corresponds to the `entry` column in the `gameobject_template` table, but this unit does not query the database; it compares against runtime-loaded data structures.

**Notable Implementation Details**

1.  **State Mutation for Optimization**: The most critical detail is that `operator()` is **not pure**. It modifies the member variable `i_range`. This design pattern is essential for the "nearest" search algorithm. If `i_range` were constant, the check would return `true` for *all* objects within the initial radius, forcing the caller to sort them manually. By shrinking the radius dynamically, the search can often terminate early or reduce the number of valid candidates significantly.
2.  **Copy Prevention**: The class defines a private copy constructor `NearestGameObjectEntryInObjectRangeCheck(NearestGameObjectEntryInObjectRangeCheck const&)` to prevent cloning. This is likely because the internal state (`i_range`) must remain unique to the specific search instance. Cloning could lead to unexpected behavior if the cloned object's range updates didn't reflect back to the original search context, or simply to enforce strict ownership semantics in the grid traversal code.
3.  **Reference Semantics**: The source object `i_obj` is stored as a `const&`. This avoids copying the large `WorldObject` structure but requires the caller to ensure the source object remains alive during the search duration. Given that these searches are typically synchronous and short-lived within the game loop, this is a safe assumption in the MaNGOS architecture.
4.  **Entry Matching**: The check strictly compares `go->GetEntry() == i_entry`. It does not account for inheritance or family relationships between game object entries. It is an exact match filter.

## Member Reference

**NearestGameObjectEntryInObjectRangeCheck**
Constructor that initializes the check with the source `WorldObject`, the target `GameObject` entry ID, and the initial maximum search range. It sets up the internal state for the nearest-object search algorithm.

**GetFocusObject**
Returns a constant reference to the source `WorldObject` (`i_obj`). Used by the grid notifier framework to access properties like phase mask for filtering purposes during the spatial search.

**operator()**
The predicate function called by the grid searcher for each candidate `GameObject`. It returns `true` if the object's entry matches `i_entry` and it is within the current `i_range` of the source object. If true, it updates `i_range` to the distance of this object, effectively narrowing the search criteria for subsequent candidates to find the absolute nearest.

**GetLastRange**
Returns the final value of `i_range` after the search process. This indicates the distance to the nearest `GameObject` found that satisfied the conditions.

**NearestGameObjectEntryInObjectRangeCheck#2**
Private copy constructor declared to prevent copying of this object. This ensures that the mutable `i_range` state is not inadvertently duplicated, which could break the nearest-search logic if multiple instances of the check were used interchangeably.

---

<!-- machine-true, projected from graph.json -->

## Map — NearestGameObjectEntryInObjectRangeCheck

*Source:* GridNotifiers.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| NearestGameObjectEntryInObjectRangeCheck | ctor | — | GameObject/RespawnLinkedGameObject, GameObject/TriggerLinkedGameObject, GridSearchers/GetClosestGameObjectWithEntry, WorldObject.Object/FindNearestGameObject | — |
| GetFocusObject | method | — | — | — |
| operator() | method | — | — | — |
| GetLastRange | method | — | — | — |
| NearestGameObjectEntryInObjectRangeCheck#2 | decl | — | — | — |

---

<!-- verify: failed-members | invented: operator -->
