<!-- provenance: verbose, failed-members -->
# LinkedList

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`LinkedList.h` defines a custom, intrusive, doubly-linked list infrastructure consisting of two classes: `LinkedListElement` and `LinkedListHead`. It is designed for performance-critical contexts within the WoWVMaNGOS codebase where standard library containers might introduce unacceptable overhead or fragmentation.

*   **`LinkedListElement`**: A base class intended for inheritance. It embeds `iNext` and `iPrev` pointers directly into derived objects, allowing them to be linked into lists without allocating separate node structures. It handles its own unlinking upon destruction.
*   **`LinkedListHead`**: Manages a specific list instance using sentinel nodes (`iFirst`, `iLast`) to simplify boundary logic. It tracks list size manually and provides a nested `Iterator` template for STL-style bidirectional traversal.

## Member-by-Member Behavior

### List Element Management (`LinkedListElement`)

*   **Lifecycle**: The constructor initializes pointers to `nullptr`. The destructor calls `delink()` to ensure the element is removed from any list before memory is freed, preventing dangling pointers in neighboring nodes.
*   **State Queries**:
    *   `hasNext()`/`hasPrev()`: Return `true` if the immediate neighbor exists and is itself linked (i.e., not a sentinel or detached). This logic relies on the invariant that sentinels point to each other or valid elements.
    *   `isInList()`: Returns `true` if both `iNext` and `iPrev` are non-null, indicating the element is currently part of a list.
*   **Navigation**:
    *   `next()`/`prev()`: Return the adjacent element if valid; otherwise `nullptr`.
    *   `nocheck_next()`/`nocheck_prev()`: Return raw pointers without validation, used internally where validity is guaranteed.
*   **Modification**:
    *   `delink()`: Unlinks the element by updating neighbors’ pointers to bypass it and nullifying its own pointers.
    *   `insertBefore()`/`insertAfter()`: Splice a new element relative to the current one. These methods assume the target element is already linked and the new element is not.

### List Head Management (`LinkedListHead`)

*   **Initialization**: The constructor links `iFirst` and `iLast` sentinels together and sets `iSize` to 0.
*   **Accessors**:
    *   `isEmpty()`: Checks if `iFirst.iNext` points directly to `iLast`.
    *   `getFirst()`/`getLast()`: Return the first or last user element, or `nullptr` if the list is empty.
*   **Insertion**:
    *   `insertFirst()`: Inserts an element after `iFirst`.
    *   `insertLast()`: Inserts an element before `iLast`.
*   **Size Management**:
    *   `getSize()`: Returns the cached `iSize`. If `iSize` is 0, it performs a linear scan to recount elements, serving as a fallback for potential tracking errors.
    *   `incSize()`/`decSize()`: Manually adjust `iSize`. Callers must invoke these during insert/remove operations, as `LinkedListElement` methods do not update the head’s size automatically.

### Iterator Implementation (`LinkedListHead::Iterator<_Ty>`)

Provides STL-compatible bidirectional iteration over the list.

*   **Constructors**: Default creates a null iterator; the parameterized constructor takes a `LinkedListElement` pointer.
*   **Dereferencing**: `operator*()` and `operator->()` cast the internal `LinkedListElement*` to `_Ty*` for access.
*   **Traversal**: `operator++()` and `operator--()` move the internal pointer using `next()` and `prev()`. Pre/post variants handle return values accordingly.
*   **Comparison**: Equality/inequality operators compare internal pointers against other iterators, raw pointers, or references.
*   **Node Access**: `_Mynode()` exposes the underlying `LinkedListElement*` cast to `_Ty*`.

## Cross-Unit Boundaries

The `LinkedList` unit is self-contained. It does not call into other units nor is it called by other units according to the provided MAP. It serves as a foundational utility class used via inheritance and composition elsewhere in the codebase.

## Data Model

This unit does not interact with any database tables. It operates entirely in memory.

## Notable Implementation Details

1.  **Sentinel Nodes**: `iFirst` and `iLast` simplify insertion/removal logic by eliminating null-pointer checks for head/tail boundaries.
2.  **Manual Size Maintenance**: `iSize` is not updated automatically by element methods. Callers must use `incSize()`/`decSize()`. Discrepancies are partially mitigated by `getSize()`’s fallback linear scan when `iSize` is 0.
3.  **Intrusive Design**: Objects must inherit from `LinkedListElement`, coupling the data structure to the object hierarchy.
4.  **Iterator Safety**: Iteration stops at list ends because `next()`/`prev()` return `nullptr` for invalid neighbors. This prevents infinite loops but requires careful handling of end conditions.
5.  **Pointer Casting**: Iterators cast `LinkedListElement*` to `_Ty*`. Incorrect type usage leads to undefined behavior.
6.  **Delink in Destructor**: Ensures automatic removal from lists upon deletion, preventing dangling pointers.

## Member Reference

**Iterator<_Ty>**
Default constructor for the iterator, initializing the internal pointer to `nullptr`.

**Iterator<_Ty>#2**
Constructor taking a `LinkedListElement` pointer, initializing the internal pointer to the provided node.

**operator=#2**
Assignment operator assigning from a `const_pointer` (raw pointer), casting it to the internal pointer type.

**operator=**
Assignment operator assigning from another `Iterator`, copying the internal pointer.

**operator***
Dereference operator, casting the internal pointer to `_Ty*` and returning a reference to the pointed-to object.

**operator->**
Arrow operator, casting the internal pointer to `_Ty*` and returning it for member access.

**operator++**
Pre-increment operator, moving the internal pointer to the next element and returning the updated iterator.

**operator++#2**
Post-increment operator, saving the current state, incrementing, and returning the saved state.

**operator--**
Pre-decrement operator, moving the internal pointer to the previous element and returning the updated iterator.

**operator--#2**
Post-decrement operator, saving the current state, decrementing, and returning the saved state.

**operator==#2**
Equality operator comparing the iterator against a raw pointer (`pointer`), checking if the internal pointer matches the argument.

**operator!=#2**
Inequality operator comparing the iterator against a raw pointer, negating the equality result.

**operator==**
Equality operator comparing two iterators, checking if their internal pointers are identical.

**operator!=**
Inequality operator comparing two iterators, negating the equality result.

**operator==#3**
Equality operator comparing the iterator against a reference (`const_reference`), checking if the internal pointer points to the address of the reference.

**operator!=#3**
Inequality operator comparing the iterator against a reference, negating the equality result.

**_Mynode**
Accessor returning the internal pointer cast to `_Ty*`, exposing the underlying node.

---

<!-- machine-true, projected from graph.json -->

## Map — LinkedList

*Source:* LinkedList.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| Iterator<_Ty> | ctor | — | — | — |
| Iterator<_Ty>#2 | ctor | — | — | — |
| operator=#2 | function | — | — | — |
| operator= | function | — | — | — |
| operator* | function | — | — | — |
| operator-> | function | — | — | — |
| operator++ | function | — | — | — |
| operator++#2 | function | — | — | — |
| operator-- | function | — | — | — |
| operator--#2 | function | — | — | — |
| operator==#2 | function | — | — | — |
| operator!=#2 | function | — | — | — |
| operator== | function | — | — | — |
| operator!= | function | — | — | — |
| operator==#3 | function | — | — | — |
| operator!=#3 | function | — | — | — |
| _Mynode | function | — | — | — |

---

<!-- verify: failed-members | invented: operator -->
