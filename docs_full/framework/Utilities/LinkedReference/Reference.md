<!-- provenance: verbose -->
# Reference

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`Reference<TO, FROM>` is a template base class that manages a directed relationship between a target object (`TO`) and a source object (`FROM`). It inherits from `LinkedListElement` to support doubly-linked list traversal of references. Its core responsibility is lifecycle synchronization: it notifies the involved objects via pure virtual hooks when a reference is established, voluntarily removed, or forcibly invalidated due to target destruction. This mechanism prevents dangling pointers by ensuring both parties are aware of the reference state.

## Member-by-Member Behavior

### Lifecycle Management

*   **`link(TO* toObj, FROM* fromObj)`**: Establishes a reference. It asserts `fromObj` is not `nullptr`. If the reference is already valid, it calls `unlink()` to sever the previous connection. If `toObj` is not `nullptr`, it stores the pointers and invokes `targetObjectBuildLink()`. If `toObj` is `nullptr`, no link is formed.
*   **`unlink()`**: Severs the reference, typically initiated by the source. It calls `targetObjectDestroyLink()`, removes the node from its linked list via `delink()`, and resets both `iRefTo` and `iRefFrom` to `nullptr`.
*   **`invalidate()`**: Severs the reference due to target destruction, typically initiated by the target. It calls `sourceObjectDestroyLink()`, removes the node from its linked list via `delink()`, and resets `iRefTo` to `nullptr`. Crucially, it **preserves** `iRefFrom`, as indicated by the code comment `// the iRefFrom MUST remain!!`, allowing the target's destructor to access the source for final cleanup.
*   **`isValid()`**: Returns `true` if `iRefTo` is not `nullptr`.

### Traversal & Access

*   **`next()` / `next() const`**: Casts the result of `LinkedListElement::next()` to `Reference<TO, FROM>*` (or const variant).
*   **`prev()` / `prev() const`**: Casts the result of `LinkedListElement::prev()` to `Reference<TO, FROM>*` (or const variant).
*   **`operator->()`**: Returns `iRefTo`, enabling direct member access on the target.
*   **`getTarget()`**: Returns the raw `iRefTo` pointer.
*   **`getSource()`**: Returns the raw `iRefFrom` pointer.

### Virtual Hooks

Derived classes must implement these pure virtual methods to handle specific logic for updating the `TO` and `FROM` objects:

*   **`targetObjectBuildLink()`**: Called by `link()` to notify the target that a new reference exists.
*   **`targetObjectDestroyLink()`**: Called by `unlink()` to notify the target that the reference is being removed.
*   **`sourceObjectDestroyLink()`**: Called by `invalidate()` to notify the source that the target has been destroyed.

## Cross-Unit Boundaries

*   **Calls Out**: None. The class interacts with the rest of the system solely through its pure virtual hooks, which are implemented in derived classes in other units.
*   **Called By**: None listed in the MAP. In practice, derived classes and the `TO`/`FROM` objects themselves invoke `link`, `unlink`, and `invalidate` during their lifecycle management.

## Data Model

This unit does not interact with any database tables. It operates entirely in memory.

## Notable Implementation Details

1.  **Asymmetric Cleanup**: `unlink()` clears both pointers, while `invalidate()` preserves `iRefFrom`. This asymmetry suggests that during target destruction, the target may still need to reference the source, whereas the source does not need to reference the target after `unlink()`.
2.  **Auto-Unlink on Relink**: `link()` automatically calls `unlink()` if the reference is already valid. This prevents double-linking but requires that the virtual hooks correctly handle the removal of the old target before adding the new one.
3.  **Null Target Handling**: `link()` allows `toObj` to be `nullptr`. In this case, no pointers are stored, and no hooks are called, leaving the reference in an invalid state.
4.  **Assertion on Source**: `link()` asserts that `fromObj` is not `nullptr`, enforcing that a source object is always required for a valid reference.

## Member Reference

**targetObjectBuildLink**: Pure virtual function. Declares the interface for notifying the target object (`TO`) that a new reference has been created. Must be implemented by derived classes.

**targetObjectDestroyLink**: Pure virtual function. Declares the interface for notifying the target object (`TO`) that the reference is being severed via `unlink()`. Must be implemented by derived classes.

**sourceObjectDestroyLink**: Pure virtual function. Declares the interface for notifying the source object (`FROM`) that the reference is being invalidated due to the target's destruction via `invalidate()`. Must be implemented by derived classes.

**~Reference<TO, FROM>**: Virtual destructor. Does not perform explicit cleanup; relies on prior calls to `unlink()` or `invalidate()`.

**link**: Establishes a reference between `toObj` (target) and `fromObj` (source). Automatically unlinks any existing reference if valid. Calls `targetObjectBuildLink()` if `toObj` is not null. Asserts `fromObj` is not null.

**unlink**: Severs the reference, typically called by the source. Calls `targetObjectDestroyLink()`, removes from linked list (`delink()`), and clears both `iRefTo` and `iRefFrom`.

**invalidate**: Severs the reference due to target destruction, typically called by the target. Calls `sourceObjectDestroyLink()`, removes from linked list (`delink()`), clears `iRefTo`, but preserves `iRefFrom`.

**isValid**: Returns `true` if `iRefTo` is not `nullptr`.

**next**: Returns pointer to the next `Reference<TO, FROM>` in the linked list. Non-const version.

**next#2**: Returns pointer to the next `Reference<TO, FROM>` in the linked list. Const version.

**prev**: Returns pointer to the previous `Reference<TO, FROM>` in the linked list. Non-const version.

**prev#2**: Returns pointer to the previous `Reference<TO, FROM>` in the linked list. Const version.

**operator->**: Returns `iRefTo`, allowing direct access to the target object's members.

**getTarget**: Returns the raw pointer `iRefTo`.

**getSource**: Returns the raw pointer `iRefFrom`.

---

<!-- machine-true, projected from graph.json -->

## Map — Reference

*Source:* Reference.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| targetObjectBuildLink | decl | — | — | — |
| targetObjectDestroyLink | decl | — | — | — |
| sourceObjectDestroyLink | decl | — | — | — |
| ~Reference<TO, FROM> | dtor | — | — | — |
| link | function | — | — | — |
| unlink | function | — | — | — |
| invalidate | function | — | — | — |
| isValid | function | — | — | — |
| next | function | — | — | — |
| next#2 | function | — | — | — |
| prev | function | — | — | — |
| prev#2 | function | — | — | — |
| operator-> | function | — | — | — |
| getTarget | function | — | — | — |
| getSource | function | — | — | — |
