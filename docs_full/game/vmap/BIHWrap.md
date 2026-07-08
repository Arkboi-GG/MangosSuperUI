<!-- provenance: verbose, failed-members -->
# BIHWrap

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# BIHWrap

**Purpose & Responsibilities**

`BIHWrap` is a template wrapper around the `BIH` spatial acceleration structure. It manages a collection of generic objects (`T`) by maintaining an array of pointers and deferring expensive tree rebuilds until a spatial query occurs. Key responsibilities include lazy rebalancing of the underlying tree, tracking pending insertions and removals, and adapting user-provided callbacks to the index-based interface required by `BIH`.

This unit contains no database interactions.

## Member-by-Member Behavior

### Construction

*   **`BIHWrap<T, BoundsFunc>`**: Initializes the `unbalanced_times` counter to zero. Internal containers (`m_tree`, `m_objects`, etc.) use their default constructors.

### Object Management

*   **`insert`**: Marks the tree as unbalanced by incrementing `unbalanced_times` and adds the object pointer to `m_objects_to_push`. It does not immediately modify the tree or the main object array.
*   **`remove`**: Marks the tree as unbalanced. It attempts to locate the object in `m_obj2Idx` (a map from pointers to indices). If found, it nullifies the corresponding entry in `m_objects`. If not found in the map, it removes the pointer from `m_objects_to_push`. Note: `m_obj2Idx` is never populated in this source, so `remove` effectively only deletes objects that are pending insertion.

### Tree Maintenance

*   **`balance`**: Rebuilds the `BIH` tree if `unbalanced_times` is non-zero. It resets the counter, clears `m_objects`, repopulates it with keys from `m_obj2Idx` and members from `m_objects_to_push`, and invokes `m_tree.build()` using `BoundsFunc::getBounds2`.

### Spatial Queries

*   **`intersectRay`**: Calls `balance()` to ensure the tree is current. Creates an `MDLCallback` adapter wrapping the user's `intersectCallback` and the current `m_objects` array, then delegates to `m_tree.intersectRay()`.
*   **`intersectPoint`**: Calls `balance()`. Creates an `MDLCallback` adapter and delegates to `m_tree.intersectPoint()`.

### Internal Helper: MDLCallback

The nested `MDLCallback` struct adapts user callbacks to the `BIH` interface, which expects integer indices rather than object pointers.

*   **`MDLCallback<RayCallback>`**: Constructor stores references to the user callback, the object array, and the array size.
*   **`operator()`**: Invoked by `BIH` during ray traversal. Validates that the provided index is within bounds and that the pointer at that index is non-null, then forwards the call to the user callback.
*   **`operator()#2`**: Invoked by `BIH` during point traversal. Validates the index and pointer, then forwards the call to the user callback.

## Cross-Unit Boundaries

*   **Calls `BIH` (from `BIH.h`)**:
    *   `balance()` calls `m_tree.build()`.
    *   `intersectRay()` calls `m_tree.intersectRay()`.
    *   `intersectPoint()` calls `m_tree.intersectPoint()`.
    *   *Direction*: Outbound. `BIHWrap` delegates all low-level tree construction and traversal logic to `BIH`.

## Data Model

This unit does not interact with any database tables. All data is held in memory using G3D containers.

## Notable Implementation Details

1.  **Unpopulated Index Map**: `m_obj2Idx` is declared but never populated in this source. `insert` adds to `m_objects_to_push`, and `balance` moves items to `m_objects` but does not update `m_obj2Idx`. Consequently, `remove`'s attempt to find objects in `m_obj2Idx` will always fail, limiting `remove` to only deleting objects that are still pending insertion.
2.  **Null Pointer Handling**: `MDLCallback` checks if `objects[Idx]` is non-null before invoking the user callback. This protects against stale pointers if `remove` nullifies slots before the next `balance`.

## Member Reference

*   **MDLCallback<RayCallback>**: Nested struct constructor initializing the callback adapter with the user callback reference, object array pointer, and size.
*   **operator()**: Method of `MDLCallback` invoked during ray traversal; validates index and pointer, then forwards to the user callback.
*   **operator()#2**: Method of `MDLCallback` invoked during point traversal; validates index and pointer, then forwards to the user callback.
*   **BIHWrap<T, BoundsFunc>**: Default constructor initializing the `unbalanced_times` counter to zero.
*   **insert**: Increments `unbalanced_times` and adds the object pointer to `m_objects_to_push`.
*   **remove**: Increments `unbalanced_times`; attempts to remove from `m_obj2Idx` and nullify `m_objects`, or removes from `m_objects_to_push` if not in the map.
*   **balance**: Rebuilds the tree if imbalanced; merges pending inserts with existing objects and calls `m_tree.build()`.

---

<!-- machine-true, projected from graph.json -->

## Map — BIHWrap

*Source:* BIHWrap.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| MDLCallback<RayCallback> | ctor | — | — | — |
| operator() | function | — | — | — |
| operator()#2 | function | — | — | — |
| BIHWrap<T, BoundsFunc> | ctor | — | — | — |
| insert | function | — | — | — |
| remove | function | — | — | — |
| balance | function | — | — | — |

---

<!-- verify: failed-members | invented: operator -->
