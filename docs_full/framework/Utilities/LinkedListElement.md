# LinkedListElement

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`LinkedListElement` is a minimal, intrusive doubly-linked list node implementation within the `wowvmangos` codebase. It provides the core linkage mechanics (`iNext`, `iPrev`) required for custom linked list structures used throughout the server. Unlike standard library containers (`std::list`), this implementation is **intrusive**: the node data is embedded directly within user-defined objects that inherit from or contain a `LinkedListElement`. This allows for zero-overhead iteration and manipulation of complex game entities (such as creatures, players, or world objects) without requiring separate heap allocations for list nodes.

The class exposes methods for safe traversal (`next`, `prev`), unsafe direct access (`nocheck_next`, `nocheck_prev`), membership testing (`isInList`), and structural modification (`delink`, `insertBefore`, `insertAfter`). It is designed to work in tandem with `LinkedListHead` (defined in the same header), which manages sentinel nodes (`iFirst`, `iLast`) to simplify boundary conditions during insertion and removal.

Crucially, `LinkedListElement` assumes the caller maintains list integrity. It does not automatically update size counters or notify other systems upon modification; these responsibilities fall to the owning container or the caller. The destructor automatically calls `delink()` to prevent dangling pointers if an element is destroyed while still attached to a list, though this relies on the list head remaining valid.

## Member-by-Member Behavior

### Construction and Destruction

*   **`LinkedListElement` (ctor)**: Initializes `iNext` and `iPrev` to `nullptr`. This represents an unlinked state.
*   **`~LinkedListElement` (dtor)**: Automatically invokes `delink()`. This is a safety measure to ensure that if an object containing a `LinkedListElement` is destroyed, it removes itself from any list it might still be part of. Note that this assumes the list structure (specifically the neighbors' pointers) remains valid during destruction. If the list head is destroyed before the elements, this can lead to undefined behavior.

### State Inspection

*   **`hasNext`**: Returns `true` if the current element has a valid next neighbor (`iNext`) AND that neighbor also has a valid next neighbor (`iNext->iNext`). This effectively checks if there is at least one more element after the immediate next one, or if the next element is not the tail sentinel (depending on how sentinels are structured). In the context of `LinkedListHead`, `iLast` is the tail sentinel. If `iNext` points to `iLast`, `iNext->iNext` is `nullptr` (since `iLast` is initialized with `iNext=nullptr` in `LinkedListHead`? No, wait. Let's look at `LinkedListHead` constructor: `iFirst.iNext = &iLast; iLast.iPrev = &iFirst;`. `iLast` is a `LinkedListElement`. Its `iNext` is `nullptr` by default. So `hasNext` returns true only if `iNext` is not `nullptr` AND `iNext->iNext` is not `nullptr`. This means `hasNext` returns `false` if the next element is the last element in the list (because the last element's `iNext` is `nullptr`). This is a specific semantic choice: "Is there a next element that is *not* the final element?" or "Are we not at the second-to-last position?". Actually, looking at `LinkedListHead::getFirst`, it returns `iFirst.iNext`. If the list has one element `E`, `iFirst.iNext = E`, `E.iNext = iLast`. `iLast.iNext` is `nullptr`. So `E.hasNext()` would check `E.iNext` (which is `iLast`, non-null) and `iLast.iNext` (which is `nullptr`). So `E.hasNext()` is `false`. This suggests `hasNext` is used to determine if there are *more* elements beyond the immediate next one, or perhaps it's a flawed implementation intended to mean "is there a next element?". Given `next()` uses `hasNext()`, `next()` will return `nullptr` if `hasNext()` is false. This implies that if you are at the second-to-last element, `next()` returns `nullptr`, skipping the last element? Or if you are at the last element, `next()` returns `nullptr`. Let's trace: Element `E` is last. `E.iNext = iLast`. `iLast.iNext = nullptr`. `E.hasNext()` -> `iNext` (non-null) && `iNext->iNext` (null) -> `false`. So `E.next()` returns `nullptr`. This is correct for iterating to the end. What about the second-to-last element `S`? `S.iNext = E`. `E.iNext = iLast`. `S.hasNext()` -> `E` (non-null) && `E.iNext` (non-null, it's `iLast`) -> `true`. So `S.next()` returns `E`. This works. The sentinel `iLast` acts as the terminator.
*   **`hasPrev`**: Symmetric to `hasNext`. Checks if `iPrev` exists and `iPrev->iPrev` exists. Used by `prev()` to determine if a previous element is accessible.
*   **`isInList`**: Returns `true` if both `iNext` and `iPrev` are non-null. This is the primary check for whether an element is currently linked into a list. An unlinked element has both as `nullptr`.

### Traversal

*   **`next` / `next#2`**: Returns the next element in the list if `hasNext()` is true; otherwise returns `nullptr`. The const and non-const overloads allow usage in both contexts.
*   **`prev` / `prev#2`**: Returns the previous element in the list if `hasPrev()` is true; otherwise returns `nullptr`.
*   **`nocheck_next` / `nocheck_next#2`**: Returns `iNext` directly without any validation. This is an optimization for internal use or performance-critical paths where the caller guarantees validity.
*   **`nocheck_prev` / `nocheck_prev#2`**: Returns `iPrev` directly without validation. Called by `Map.Main/Remove#3` (from another unit), indicating that some external logic relies on direct pointer access for removal operations, likely to avoid the overhead of `hasPrev` checks when the context guarantees linkage.

### Structural Modification

*   **`delink`**: Removes the current element from its list. It updates the `iPrev` of the next element and the `iNext` of the previous element to bypass the current node. Then it sets its own `iNext` and `iPrev` to `nullptr`. It only performs this operation if `isInList()` is true, preventing double-unlinking errors.
*   **`insertBefore`**: Inserts a given element `pElem` immediately before the current element. It updates `pElem`'s neighbors to point to the current element and the current element's previous neighbor. It then updates the previous neighbor's `iNext` and the current element's `iPrev` to include `pElem`. **Note**: This method assumes the current element is already in a list (it accesses `iPrev->iNext`). Calling this on an unlinked element results in undefined behavior (dereferencing `nullptr`).
*   **`insertAfter`**: Inserts a given element `pElem` immediately after the current element. Similar to `insertBefore`, it assumes the current element is linked (accesses `iNext->iPrev`).

## Cross-Unit Boundaries

*   **Called by `Map.Main/Remove#3`**: The `nocheck_prev` method is invoked by `Map.Main/Remove#3`. This indicates that the `Map` module (likely managing spatial partitioning or entity maps) uses direct pointer access to traverse or remove elements from linked lists. The use of `nocheck_` variants suggests that `Map.Main` operates in a context where list integrity is guaranteed or performance is prioritized over safety checks.

## Data Model

This unit does not interact with any database tables. It is a pure in-memory data structure component.

## Notable Implementation Details

1.  **Sentinel-Based List Management**: The `LinkedListHead` class (defined in the same header) uses two sentinel nodes, `iFirst` and `iLast`, to bound the list. `LinkedListElement` methods like `hasNext` and `hasPrev` rely on the properties of these sentinels (specifically that `iLast.iNext` is `nullptr` and `iFirst.iPrev` is `nullptr`) to determine list boundaries. This design avoids special-case checks for head/tail elements during traversal.
2.  **Unsafe Insertion Assumptions**: `insertBefore` and `insertAfter` do not check if the current element is linked. They directly dereference `iPrev` and `iNext`. This is a significant risk factor: calling these methods on an unlinked `LinkedListElement` will cause a segmentation fault. Callers must ensure the element is part of a list (typically via `LinkedListHead`'s `insertFirst`/`insertLast` which handle the sentinel linkage correctly).
3.  **Destructor Side Effects**: The destructor calls `delink()`. While this prevents dangling pointers in the list, it can be dangerous if the list head is destroyed before the elements. If `iFirst` or `iLast` are invalid, `delink()` will crash. Proper destruction order (elements first, then head) is critical.
4.  **Size Counter Decoupling**: `LinkedListElement` does not manage the list size. `LinkedListHead` has `incSize()` and `decSize()` methods, but `LinkedListElement`'s `delink`, `insertBefore`, and `insertAfter` do not call them. The responsibility for updating the size counter lies entirely with the caller or the `LinkedListHead` wrapper methods. This can lead to inconsistent size counts if raw `LinkedListElement` methods are used directly without corresponding size updates.
5.  **Iterator Implementation**: The `LinkedListHead::Iterator` class provides STL-compatible bidirectional iteration. It wraps `LinkedListElement` pointers and uses `next()` and `prev()` for increment/decrement. This ensures safe traversal through the iterator interface, unlike direct pointer manipulation.

## Member Reference

**LinkedListElement** (ctor): Initializes `iNext` and `iPrev` to `nullptr`, placing the element in an unlinked state.

**~LinkedListElement** (dtor): Automatically calls `delink()` to remove the element from any list it is part of, preventing dangling pointers. Assumes list neighbors are still valid.

**hasNext** (method): Returns `true` if the element has a next neighbor (`iNext`) and that neighbor also has a next neighbor (`iNext->iNext`). Used to determine if there are more elements beyond the immediate next one, leveraging sentinel properties.

**hasPrev** (method): Returns `true` if the element has a previous neighbor (`iPrev`) and that neighbor also has a previous neighbor (`iPrev->iPrev`). Symmetric to `hasNext`.

**isInList** (method): Returns `true` if both `iNext` and `iPrev` are non-null, indicating the element is currently linked into a list.

**next** (method): Returns the next element if `hasNext()` is true; otherwise returns `nullptr`. Provides safe traversal.

**next#2** (method): Const overload of `next()`.

**prev** (method): Returns the previous element if `hasPrev()` is true; otherwise returns `nullptr`. Provides safe traversal.

**prev#2** (method): Const overload of `prev()`.

**nocheck_next** (method): Returns `iNext` directly without validation. Used for performance-critical paths where validity is guaranteed.

**nocheck_next#2** (method): Const overload of `nocheck_next()`.

**nocheck_prev** (method): Returns `iPrev` directly without validation. Called by `Map.Main/Remove#3` for direct pointer access during removal operations.

**nocheck_prev#2** (method): Const overload of `nocheck_prev()`.

**delink** (method): Removes the element from its list by updating neighbors' pointers and setting its own `iNext`/`iPrev` to `nullptr`. Only executes if `isInList()` is true. Does not update list size counters.

**insertBefore** (method): Inserts a given element before the current element. Assumes the current element is already linked; dereferences `iPrev` directly. Undefined behavior if called on an unlinked element.

**insertAfter** (method): Inserts a given element after the current element. Assumes the current element is already linked; dereferences `iNext` directly. Undefined behavior if called on an unlinked element.

---

<!-- machine-true, projected from graph.json -->

## Map — LinkedListElement

*Source:* LinkedList.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| LinkedListElement | ctor | — | — | — |
| ~LinkedListElement | dtor | — | — | — |
| hasNext | method | — | — | — |
| hasPrev | method | — | — | — |
| isInList | method | — | — | — |
| next | method | — | — | — |
| next#2 | method | — | — | — |
| prev | method | — | — | — |
| prev#2 | method | — | — | — |
| nocheck_next | method | — | — | — |
| nocheck_next#2 | method | — | — | — |
| nocheck_prev | method | — | Map.Main/Remove#3 | — |
| nocheck_prev#2 | method | — | — | — |
| delink | method | — | — | — |
| insertBefore | method | — | — | — |
| insertAfter | method | — | — | — |
