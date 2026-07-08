<!-- provenance: failed-members -->
# AllGameObjectsWithEntryInRange

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# AllGameObjectsWithEntryInRange

**Purpose & Responsibilities**

`AllGameObjectsWithEntryInRange` is a predicate functor defined in `GridNotifiers.h` within the `MaNGOS` namespace. It serves as a filtering criterion for spatial queries within the server's grid-based world management system. Specifically, it identifies `GameObject` instances that match a specific database entry ID (`entry`) and fall within a defined 2D distance radius from a reference `WorldObject`.

This class is designed to be passed to grid traversal algorithms (such as `GridSearchers` or `WorldObject` query methods) that iterate over nearby objects. By encapsulating the matching logic in a callable object, the server can efficiently filter large sets of potential candidates during runtime searches without hardcoding specific entry IDs into the traversal infrastructure.

**Member-by-Member Behavior**

The unit consists of two members: a constructor and the function call operator.

*   **Constructor (`AllGameObjectsWithEntryInRange`)**: Initializes the predicate with three parameters:
    1.  `pObject`: A pointer to the `WorldObject` serving as the center point for the distance calculation.
    2.  `uiEntry`: The unsigned 32-bit integer representing the specific `GameObject` entry ID to match.
    3.  `fMaxRange`: The floating-point maximum distance threshold.
    These values are stored in private member variables `m_pObject`, `m_uiEntry`, and `m_fRange` respectively.

*   **Function Call Operator (`operator()`)**: This method implements the filtering logic. It accepts a single argument, `pGo` (a pointer to a `GameObject`). It returns `true` if and only if both of the following conditions are met:
    1.  The `GameObject`'s entry ID matches `m_uiEntry` (checked via `pGo->GetEntry() == m_uiEntry`).
    2.  The `GameObject` is within `m_fRange` distance of `m_pObject`. The distance check uses `m_pObject->IsWithinDist(pGo, m_fRange, false)`. The third argument `false` indicates that the distance calculation is **2D** (ignoring the Z-axis/elevation), which is standard for most ground-based interaction ranges in this engine.

**Cross-Unit Boundaries**

*   **Called By**:
    *   `GridSearchers/GetGameObjectListWithEntryInGrid`: This unit utilizes `AllGameObjectsWithEntryInRange` as the predicate when iterating through grid cells to collect all game objects of a specific type within a certain area.
    *   `WorldObject.Object/GetGameObjectListWithEntryInGrid`: This method on the `WorldObject` class uses this predicate to perform spatial queries relative to a specific object instance.

*   **Calls Out**:
    *   The unit does not explicitly call out to other documented units in the provided map. Internally, `operator()` invokes methods on `GameObject` (`GetEntry`) and `WorldObject` (`IsWithinDist`). These are core object methods, not separate architectural units listed in the cross-reference map.

**Data Model**

This unit does not interact directly with database tables. It operates entirely on in-memory object states (`GameObject` and `WorldObject` instances). The `entry` ID it matches against corresponds to records in the `gameobject_template` table (and related tables), but the unit itself performs no SQL queries or schema interactions.

**Notable Implementation Details**

1.  **2D Distance Calculation**: The use of `IsWithinDist(..., false)` is critical. It ensures that vertical displacement (Z-axis) does not affect the match. This is appropriate for many gameplay mechanics (e.g., finding a specific chest or door nearby) where elevation differences are negligible or irrelevant to the interaction range. If 3D distance were required, the third parameter would be `true`.
2.  **Const Correctness**: The predicate holds a `const` pointer to the reference object (`WorldObject const* m_pObject`), ensuring it does not modify the source object during evaluation.
3.  **Functor Pattern**: As a functor, this class allows the grid search algorithms to remain generic. The search algorithm doesn't need to know *what* it's looking for, only that it needs to apply a boolean test to each candidate. This decouples the spatial iteration logic from the specific business logic of "finding entry X."
4.  **No State Mutation**: Unlike some other checkers in `GridNotifiers.h` (e.g., `NearestGameObjectEntryInObjectRangeCheck`), `AllGameObjectsWithEntryInRange` does not mutate internal state (like updating a running minimum distance) during `operator()`. It is a pure filter: it simply accepts or rejects the candidate based on static criteria. This makes it safe for parallel or repeated evaluations without side effects.

## Member Reference

**AllGameObjectsWithEntryInRange**
Constructor that initializes the predicate with a reference `WorldObject`, a target `uint32` entry ID, and a maximum `float` range. Stores these in private members `m_pObject`, `m_uiEntry`, and `m_fRange`.

**operator()**
Method that evaluates whether a given `GameObject*` matches the criteria. Returns `true` if the object's entry ID equals `m_uiEntry` AND the object is within `m_fRange` 2D distance from `m_pObject`. Uses `IsWithinDist` with the 3D flag set to `false`.

---

<!-- machine-true, projected from graph.json -->

## Map — AllGameObjectsWithEntryInRange

*Source:* GridNotifiers.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| AllGameObjectsWithEntryInRange | ctor | — | GridSearchers/GetGameObjectListWithEntryInGrid#2, WorldObject.Object/GetGameObjectListWithEntryInGrid | — |
| operator() | method | — | — | — |

---

<!-- verify: failed-members | invented: operator -->
