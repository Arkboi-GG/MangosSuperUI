# GridReference

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`GridReference` is a template class specializing the generic `Reference` base class for use within the `wowvmangos` spatial partitioning system. It acts as an intrusive linked-list node that manages the relationship between a `GridRefManager<OBJECT>` (source) and an `OBJECT` (target). Its primary responsibility is to maintain the target object’s internal list of referencing grid managers and its associated reference count (`size`). Unlike standard containers, `GridReference` is embedded within the manager, while the target object stores the list head, requiring direct manipulation of the target’s state (`insertFirst`, `incSize`, `decSize`) upon link creation or destruction.

## Member-by-Member Behavior

### Link Lifecycle Hooks

These protected methods override virtual hooks from `Reference` to synchronize the target object’s state with the reference lifecycle.

*   **`targetObjectBuildLink`**: Invoked by the base class `link()` when a target is assigned. It inserts the current `GridReference` at the head of the target’s internal list (`insertFirst`) and increments the target’s reference count (`incSize`).
*   **`targetObjectDestroyLink`**: Invoked by the base class `unlink()` when the link is severed. It decrements the target’s reference count (`decSize`) only if the reference remains valid (`isValid()`), preventing double-decrement on redundant unlink calls.
*   **`sourceObjectDestroyLink`**: Invoked by the base class `invalidate()` when the source side is invalidated. It unconditionally decrements the target’s reference count (`decSize`), assuming the source was previously valid and linked.

### Construction and Destruction

*   **`GridReference<OBJECT>`**: Default constructor initializing the base `Reference` class. No immediate list manipulation occurs; insertion happens dynamically via `link()`.
*   **`~GridReference<OBJECT>`**: Destructor calling `unlink()` to ensure the target’s reference count is decremented and the link is properly severed before the object is destroyed.

### Navigation

*   **`next`**: Returns a pointer to the subsequent `GridReference` in the target’s intrusive list. It casts the raw pointer returned by the base class `Reference::next()` to `GridReference*`, enabling iteration over all grid references pointing to a specific object.

## Cross-Unit Boundaries

*   **Calls Out**: None. All operations (`insertFirst`, `incSize`, `decSize`, `isValid`, `getTarget`) are performed on the templated `OBJECT` target or the base `Reference` class. The specific list-manipulation methods are expected to be implemented by the `OBJECT` type (e.g., `Creature`, `GameObject`) or a mixin it utilizes.
*   **Called By**: None listed in the map. Instances are typically embedded within `GridRefManager` objects, which drive the linking/unlinking lifecycle.

## Data Model

This unit does not interact with any database tables. It operates entirely in memory as part of the runtime spatial indexing system.

## Notable Implementation Details

1.  **Intrusive List Dependency**: The class assumes the `OBJECT` type provides `insertFirst`, `incSize`, and `decSize` methods. This design avoids heap allocations for list nodes by embedding the `GridReference` within the manager while storing the list head in the target.
2.  **Validity Check Asymmetry**: `targetObjectDestroyLink` checks `isValid()` before decrementing the size, whereas `sourceObjectDestroyLink` does not. This implies `invalidate()` is only called when the source is known to be valid and linked, while `unlink()` may be called redundantly or on invalid states.
3.  **Explicit Cast in `next()`**: The cast `(GridReference*)` relies on the base class storing the next pointer in a way that allows safe casting back to the derived type. This is safe because `GridReference` is the concrete type used in the list.

## Member Reference

**targetObjectBuildLink**
Overrides the base class hook to insert the current reference into the target's list and increment the target's reference count. Called when a link is established.

**targetObjectDestroyLink**
Overrides the base class hook to decrement the target's reference count if the reference is still valid. Called when a link is severed.

**sourceObjectDestroyLink**
Overrides the base class hook to decrement the target's reference count. Called when the source side of the reference is invalidated.

**GridReference<OBJECT>**
Default constructor that initializes the base `Reference` class.

**~GridReference<OBJECT>**
Destructor that calls `unlink()` to ensure proper cleanup of the target's reference count and list state.

**next**
Returns a pointer to the next `GridReference` in the target's intrusive list, casting the result from the base class.

---

<!-- machine-true, projected from graph.json -->

## Map — GridReference

*Source:* GridReference.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| targetObjectBuildLink | function | — | — | — |
| targetObjectDestroyLink | function | — | — | — |
| sourceObjectDestroyLink | function | — | — | — |
| GridReference<OBJECT> | ctor | — | — | — |
| ~GridReference<OBJECT> | dtor | — | — | — |
| next | function | — | — | — |
