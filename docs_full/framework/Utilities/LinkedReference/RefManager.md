<!-- provenance: verbose -->
# RefManager

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`RefManager<TO, FROM>` is a template class managing a doubly-linked list of `Reference<TO, FROM>` objects. Inheriting from `LinkedListHead`, it provides the container side of a bidirectional reference system. Its responsibilities are limited to type-safe access to list endpoints, STL-style iteration support, and robust cleanup via `clearReferences`, which invalidates and removes all managed references.

## Member-by-Member Behavior

### Construction and Destruction
*   **`RefManager()`**: Default constructor; initializes the base `LinkedListHead`.
*   **`~RefManager()`**: Virtual destructor; invokes `clearReferences()` to ensure all managed `Reference` objects are invalidated and removed before destruction.

### Accessors
*   **`getFirst()` / `getFirst() const`**: Return pointers to the first `Reference<TO, FROM>` in the list. They cast the raw `LinkedListElement*` returned by `LinkedListHead::getFirst()` to the specific reference type. The const variant returns a const pointer.
*   **`getLast()` / `getLast() const`**: Return pointers to the last `Reference<TO, FROM>` in the list, similarly casting the result of `LinkedListHead::getLast()`.

### Iterators
*   **`begin()`**: Returns an iterator initialized with `getFirst()`.
*   **`end()`**: Returns an iterator initialized with `nullptr`, representing the past-the-end position.
*   **`rbegin()`**: Returns an iterator initialized with `getLast()`, intended for reverse traversal.
*   **`rend()`**: Returns an iterator initialized with `nullptr`, representing the past-the-beginning position.

### Cleanup
*   **`clearReferences()`**: Iterates through the list, calling `invalidate()` on each `Reference<TO, FROM>` to break the bidirectional link, followed by `delink()` to remove it from the list. The explicit `delink()` call ensures the list is empty even if `invalidate()` already performed the removal.

## Cross-Unit Boundaries

*   **Calls Out**:
    *   `LinkedListHead`: Inherits from and calls `getFirst()`, `getLast()`, and uses `LinkedListElement` types for list management.
    *   `Reference<TO, FROM>`: Calls `invalidate()` on instances during cleanup to break the bidirectional link.
*   **Called By**:
    *   Typically embedded as a member variable within game entity classes (e.g., `Creature`, `Player`) to manage outgoing references.

## Data Model

This unit does not interact with any database tables. It operates entirely in memory.

## Notable Implementation Details

1.  **Defensive Delinking**: `clearReferences()` calls `delink()` after `invalidate()`. This redundancy ensures the list is empty even if `Reference::invalidate()` fails to delink, protecting against inconsistent states.
2.  **Type Casting**: Accessors use C-style casts to convert `LinkedListElement*` to `Reference<TO, FROM>*`, relying on inheritance compatibility for performance.
3.  **Reverse Iteration**: `rbegin()` starts at `getLast()`. True reverse iteration depends on `LinkedListHead::Iterator` supporting backward traversal.

## Member Reference

**RefManager<TO, FROM>**
Default constructor. Initializes the base `LinkedListHead`.

**~RefManager<TO, FROM>**
Virtual destructor. Calls `clearReferences()` to clean up all managed references.

**getFirst**
Returns a mutable pointer to the first `Reference<TO, FROM>` in the list. Casts the result of `LinkedListHead::getFirst()`.

**getFirst#2**
Const overload of `getFirst`. Returns a const pointer to the first `Reference<TO, FROM>`.

**getLast**
Returns a mutable pointer to the last `Reference<TO, FROM>` in the list. Casts the result of `LinkedListHead::getLast()`.

**getLast#2**
Const overload of `getLast`. Returns a const pointer to the last `Reference<TO, FROM>`.

**begin**
Returns an iterator to the first element, constructed from `getFirst()`.

**end**
Returns an iterator representing the end of the list, constructed from `nullptr`.

**rbegin**
Returns an iterator to the last element, intended for reverse iteration, constructed from `getLast()`.

**rend**
Returns an iterator representing the beginning of the list (for reverse iteration), constructed from `nullptr`.

**clearReferences**
Iterates through the list, calling `invalidate()` and `delink()` on each `Reference<TO, FROM>` to remove it from the list and notify the other end of the reference. Ensures the list is empty.

---

<!-- machine-true, projected from graph.json -->

## Map — RefManager

*Source:* RefManager.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| RefManager<TO, FROM> | ctor | — | — | — |
| ~RefManager<TO, FROM> | dtor | — | — | — |
| getFirst | function | — | — | — |
| getFirst#2 | function | — | — | — |
| getLast | function | — | — | — |
| getLast#2 | function | — | — | — |
| begin | function | — | — | — |
| end | function | — | — | — |
| rbegin | function | — | — | — |
| rend | function | — | — | — |
| clearReferences | function | — | — | — |
