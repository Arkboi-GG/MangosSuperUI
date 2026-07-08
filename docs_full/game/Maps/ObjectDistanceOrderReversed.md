<!-- provenance: failed-members -->
# ObjectDistanceOrderReversed

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ObjectDistanceOrderReversed

**Purpose & Responsibilities**

`ObjectDistanceOrderReversed` is a comparator functor defined in `GridSearchers.h` used to order `WorldObject` instances by their distance from a specific source `Unit` in descending order. It is designed to be used with Standard Library algorithms (such as `std::sort` or `std::priority_queue`) that require a strict weak ordering predicate. By inverting the result of the standard distance comparison, this functor allows game logic to identify or prioritize objects that are farthest from the source unit. It serves as the inverse counterpart to `ObjectDistanceOrder`, which sorts in ascending distance order.

**Member-by-Member Behavior**

The unit comprises a constructor for initialization and a call operator for comparison logic.

1.  **Initialization**: The constructor accepts a constant pointer to a `Unit` (`pSource`) and stores it in the member variable `m_pSource`. This `Unit` acts as the fixed reference point for all distance calculations performed by this functor instance.
2.  **Comparison**: The `operator()` method takes two constant pointers to `WorldObject` (`pLeft` and `pRight`). It delegates the actual geometric comparison to `m_pSource->GetDistanceOrder(pLeft, pRight)` and returns the logical negation of that result.
    *   If `GetDistanceOrder` indicates `pLeft` is closer than `pRight` (returns `true`), `operator()` returns `false`.
    *   If `GetDistanceOrder` indicates `pLeft` is not closer than `pRight` (returns `false`), `operator()` returns `true`.
    *   In the context of sorting, this means objects farther from the source are considered "less than" (and thus precede) objects closer to the source, resulting in a descending distance sort.

**Cross-Unit Boundaries**

*   **Calls Out**:
    *   `Unit.GetDistanceOrder`: The functor relies entirely on this method from the `Unit` class (defined in `Unit.h`) to determine the relative distance of two objects. `ObjectDistanceOrderReversed` does not perform geometric calculations itself; it strictly inverts the semantic output of `Unit::GetDistanceOrder`.
*   **Called By**:
    *   The MAP indicates no explicit external callers for this specific functor. In practice, it is instantiated locally by algorithms or utility functions that need to process a collection of world objects sorted by distance from a unit, typically within grid search operations facilitated by headers like `GridNotifiers.h` or `CellImpl.h`.

**Data Model**

This unit operates entirely in memory using object pointers and geometric data associated with `Unit` and `WorldObject` instances. It does not interact with any database tables.

**Notable Implementation Details**

*   **Source Type Restriction**: The functor requires a `Unit const*` as its source, rather than a generic `WorldObject const*`. This restricts its use to scenarios where the observer is a living entity (player, creature, pet, etc.) capable of providing the `GetDistanceOrder` interface, excluding static objects or coordinates as sources.
*   **Strict Weak Ordering**: The correctness of sorting algorithms depends on `Unit::GetDistanceOrder` providing a valid strict weak ordering. The negation logic preserves the transitivity and symmetry properties required by the STL, assuming the underlying `GetDistanceOrder` is well-formed. If `GetDistanceOrder` uses secondary tie-breakers (e.g., GUID comparison), those are preserved but reversed in priority relative to distance.
*   **Const Correctness**: The functor and its `operator()` are `const`, ensuring that the comparison process does not modify the source `Unit` or the compared `WorldObjects`, allowing safe use in concurrent or const-correct contexts.

## Member Reference

**ObjectDistanceOrderReversed**
Constructor that initializes the internal `m_pSource` member with the provided `Unit const*`. This source unit defines the origin for all distance comparisons executed by this functor instance.

**operator()**
Call operator that compares two `WorldObject const*` arguments (`pLeft` and `pRight`). It invokes `m_pSource->GetDistanceOrder(pLeft, pRight)` and returns the logical negation of the result, effectively reversing the sort order to prioritize objects farther from the source.

---

<!-- machine-true, projected from graph.json -->

## Map — ObjectDistanceOrderReversed

*Source:* GridSearchers.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ObjectDistanceOrderReversed | ctor | — | — | — |
| operator() | method | — | — | — |

---

<!-- verify: failed-members | invented: operator -->
