<!-- provenance: failed-members -->
# AllGameObjectsMatchingOneEntryInRange

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# AllGameObjectsMatchingOneEntryInRange

**Purpose & Responsibilities**

`AllGameObjectsMatchingOneEntryInRange` is a predicate functor defined in `GridNotifiers.h` within the `MaNGOS` namespace. Its sole responsibility is to filter `GameObject` instances during spatial searches performed by the grid system. Specifically, it identifies Game Objects that satisfy two simultaneous conditions:
1. The object's entry ID matches **any** value in a provided list of valid entry IDs.
2. The object is within a specified maximum distance (`m_fRange`) from a reference `WorldObject` (`m_pObject`).

This class is designed to be used with the `GameObjectListSearcher` template (also in `GridNotifiers.h`) or similar grid traversal mechanisms to retrieve all matching objects in a specific area. It generalizes the simpler `AllGameObjectsWithEntryInRange` class by allowing multiple entry IDs to be matched against a single object, rather than just one.

**Member-by-Member Behavior**

The unit consists of a constructor and the function call operator.

*   **Constructor (`AllGameObjectsMatchingOneEntryInRange`)**: Initializes the predicate with the search context. It stores a pointer to the reference object (`m_pObject`), copies the vector of valid entry IDs (`entries`), and stores the maximum search radius (`m_fRange`). The entries vector is passed by `const&` but stored by value, ensuring the predicate remains valid even if the original vector goes out of scope after construction.
*   **Function Call Operator (`operator()`)**: This is the core filtering logic. It accepts a `GameObject*` pointer. It iterates through the stored `entries` vector. For each entry, it checks if the `GameObject`'s entry ID (`pGo->GetEntry()`) matches the current entry ID. If a match is found, it immediately performs a distance check using `m_pObject->IsWithinDist(pGo, m_fRange, false)`. The third argument `false` indicates that the distance calculation should likely ignore vertical height differences (2D distance) or follow specific engine conventions for "in range" checks depending on the `IsWithinDist` implementation details elsewhere. If both the entry match and distance check pass, it returns `true`. If the loop completes without finding a matching entry that is also in range, it returns `false`.

**Cross-Unit Boundaries**

*   **Called by**: `GridSearchers/GetGameObjectListWithEntryInGrid`.
    *   **Collaboration**: The `GridSearchers` unit (likely part of the spatial indexing/grid management system) invokes this predicate to filter results. The `GridSearchers` unit provides the candidate `GameObject` pointers to `operator()`. In return, `AllGameObjectsMatchingOneEntryInRange` returns a boolean indicating whether the object should be included in the final list. This allows the grid system to remain generic while delegating specific filtering criteria (entry ID + distance) to this specialized functor.
*   **Calls out**: None.
    *   The member functions do not call into other documented units. They rely on methods of `GameObject` (`GetEntry`) and `WorldObject` (`IsWithinDist`), which are part of the core object hierarchy, but these are not listed as cross-unit dependencies in the provided MAP.

**Data Model**

This unit does not interact with any database tables. It operates entirely on in-memory object states (`GameObject` instances) and runtime parameters.

**Notable Implementation Details**

*   **Vector Copying**: The constructor takes `std::vector<uint32> const& entries` but stores it as a member variable `std::vector<uint32> entries`. This implies a copy is made. For large lists of entry IDs, this could have performance implications due to memory allocation and copying overhead during predicate construction. However, it ensures safety against dangling references if the caller's vector is temporary.
*   **Early Exit on Match**: The `operator()` iterates through the `entries` vector. As soon as it finds an entry that matches the `GameObject`'s entry AND is within range, it returns `true`. It does not check subsequent entries in the vector. This is efficient for positive matches.
*   **Distance Check Logic**: The distance check `m_pObject->IsWithinDist(pGo, m_fRange, false)` is performed *inside* the loop, only for entries that match. This is slightly less efficient than checking distance first (since distance calculation might be more expensive than integer comparison), but logically correct because the distance is relative to the same `m_pObject` regardless of which entry ID matched. Since `IsWithinDist` is likely a simple squared-distance comparison, the performance difference is negligible.
*   **2D vs 3D Distance**: The `false` parameter in `IsWithinDist` suggests a 2D distance check (ignoring Z-axis) or a specific variant of distance calculation. Maintainers should verify if this aligns with the intended gameplay mechanic (e.g., some interactions are strictly horizontal, others are volumetric).
*   **Const Correctness**: The `operator()` is not marked `const`, although it logically does not modify the state of the predicate object (it only reads `entries`, `m_pObject`, and `m_fRange`). This prevents the functor from being used in contexts requiring a `const` callable, though this is rarely a strict requirement for such predicates.

## Member Reference

**AllGameObjectsMatchingOneEntryInRange** (ctor): Constructs the predicate, storing the reference `WorldObject`, the list of valid entry IDs (by value), and the maximum range.

**operator()** (method): Iterates through the stored entry IDs; returns `true` if the `GameObject`'s entry matches any ID in the list AND the object is within `m_fRange` of `m_pObject` (using 2D/specific distance metric), otherwise returns `false`.

---

<!-- machine-true, projected from graph.json -->

## Map — AllGameObjectsMatchingOneEntryInRange

*Source:* GridNotifiers.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| AllGameObjectsMatchingOneEntryInRange | ctor | — | GridSearchers/GetGameObjectListWithEntryInGrid | — |
| operator() | method | — | — | — |

---

<!-- verify: failed-members | invented: operator -->
