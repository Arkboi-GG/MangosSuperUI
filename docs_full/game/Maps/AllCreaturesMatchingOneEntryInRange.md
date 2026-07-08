<!-- provenance: failed-members -->
# AllCreaturesMatchingOneEntryInRange

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# AllCreaturesMatchingOneEntryInRange

**Purpose & Responsibilities**

`AllCreaturesMatchingOneEntryInRange` is a predicate functor defined in `GridNotifiers.h` within the `MaNGOS` namespace. It serves as a filtering criterion for spatial queries within the server's grid system. Its specific role is to identify `Unit` objects (typically creatures) that satisfy two conditions simultaneously:
1. Their entry ID matches **any** value in a provided list of entry IDs.
2. They are located within a specified maximum 2D distance from a reference `WorldObject`.

This functor enables efficient batch searching for multiple creature types in a single pass, avoiding the need for chained or repeated grid traversals when looking for groups of related entities.

**Member-by-Member Behavior**

*   **Constructor (`AllCreaturesMatchingOneEntryInRange`)**
    Initializes the functor with the context required for evaluation. It accepts three arguments:
    *   `pObject`: A pointer to a `WorldObject` that acts as the origin for distance calculations.
    *   `entries`: A constant reference to a `std::vector<uint32>` containing the target creature entry IDs.
    *   `fMaxRange`: A `float` defining the maximum allowable distance from `pObject`.
    
    The constructor stores these values in private member variables (`m_pObject`, `entries`, `m_fRange`). Notably, the `entries` vector is copied by value into the member variable `entries`. This ensures the functor remains valid and self-contained even if the original vector passed by the caller is modified or destroyed after construction.

*   **Functional Operator (`operator()`)**
    This method performs the actual filtering logic. It takes a single argument, `pUnit` (of type `Unit*`), and returns a `bool`.
    1.  It iterates through the stored `entries` vector.
    2.  For each entry ID in the vector, it checks:
        *   Whether `pUnit->GetEntry()` equals the current entry ID.
        *   Whether `pUnit` is within `m_fRange` of `m_pObject`. This is evaluated via `m_pObject->IsWithinDist(pUnit, m_fRange, false)`. The final argument `false` specifies a 2D distance check, ignoring vertical (Z-axis) differences.
    3.  If both conditions are met for any entry, the method immediately returns `true`.
    4.  If the loop completes without finding a matching entry, it returns `false`.

**Cross-Unit Boundaries**

*   **Called By:** `GridSearchers/GetCreatureListWithEntryInGrid`
    The functor is instantiated and utilized by search routines in `GridSearchers.cpp`, specifically `GetCreatureListWithEntryInGrid`. The grid system iterates over objects in relevant spatial cells and applies this functor to each candidate. If the functor returns `true`, the object is included in the resulting list. This design decouples the generic grid traversal logic from the specific matching criteria.

*   **Calls Out:** None
    The functor does not call into other architectural units. It relies on methods inherent to the `Unit` and `WorldObject` classes (`GetEntry`, `IsWithinDist`) which are part of the core object model.

**Data Model**

This unit does not interact with any database tables. It operates exclusively on in-memory object states and runtime parameters.

**Notable Implementation Details**

*   **Vector Copy Overhead:** The constructor copies the `std::vector<uint32>` of entries. While this guarantees safety against dangling references, it incurs a memory allocation and copy cost. This is generally acceptable because entry lists for such checks are typically small, but it is a consideration if the functor is instantiated extremely frequently in tight loops.
*   **2D Distance Calculation:** The use of `IsWithinDist(..., false)` enforces horizontal distance checking. This aligns with most gameplay mechanics (e.g., aggro, interaction) where vertical elevation differences do not negate proximity.
*   **Early Exit Optimization:** The `operator()` returns `true` immediately upon finding the first matching entry ID, avoiding unnecessary iterations through the remainder of the list.
*   **Unit Type Generality:** Although named for "Creatures," the functor accepts `Unit*`. This allows it to be used in broader contexts where `Unit` is the base iteration type, though entry-based filtering is primarily meaningful for `Creature` objects.

## Member Reference

**AllCreaturesMatchingOneEntryInRange**
Constructor that initializes the functor with a reference `WorldObject`, a vector of creature entry IDs to match, and a maximum range. It copies the entry vector to ensure independence from the caller's data.

**operator()**
Method that evaluates whether a given `Unit` matches any of the stored entry IDs and is within the specified 2D distance from the reference object. Returns `true` if a match is found, `false` otherwise. Iterates through the entry list and exits early on the first match.

---

<!-- machine-true, projected from graph.json -->

## Map — AllCreaturesMatchingOneEntryInRange

*Source:* GridNotifiers.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| AllCreaturesMatchingOneEntryInRange | ctor | — | GridSearchers/GetCreatureListWithEntryInGrid | — |
| operator() | method | — | — | — |

---

<!-- verify: failed-members | invented: operator -->
