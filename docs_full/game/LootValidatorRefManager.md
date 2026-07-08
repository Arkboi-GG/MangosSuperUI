# LootValidatorRefManager

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# LootValidatorRefManager

**Purpose & Responsibilities**

`LootValidatorRefManager` is a specialized container class responsible for managing the lifecycle of references to `Loot` objects. It implements an intrusive doubly-linked list of `LootValidatorRef` objects, allowing the system to track which game entities (such as players or rolls) are currently observing or dependent on a specific `Loot` instance.

Its primary role is to provide safe, standard-compliant iteration over these references. By exposing STL-style iterators (`begin`, `end`, `rbegin`, `rend`) and direct accessors (`getFirst`, `getLast`), it enables other parts of the codebase to traverse the list of validators—typically to notify them when the underlying `Loot` object is being destroyed or modified. It inherits from `RefManager<Loot, LootValidatorRef>`, leveraging the core framework's reference counting and link management infrastructure.

**Member-by-Member Behavior**

The members of `LootValidatorRefManager` are thin wrappers around the functionality provided by its base class, `RefManager`. They do not contain independent logic but serve to cast the generic base class pointers to the specific `LootValidatorRef` type and wrap them in iterator objects.

*   **Accessors (`getFirst`, `getLast`)**: These methods retrieve the head and tail nodes of the intrusive linked list maintained by the base class. They perform a static cast from the base `Reference` pointer to `LootValidatorRef*`.
*   **Iterators (`begin`, `end`, `rbegin`, `rend`)**: These methods construct `iterator` objects (defined as `LinkedListHead::Iterator<LootValidatorRef>`). `begin()` and `rbegin()` initialize the iterator with the first and last elements respectively, while `end()` and `rend()` initialize it with `nullptr`, marking the termination of the forward and reverse traversal ranges.

**Cross-Unit Boundaries**

*   **Calls Out**: None. The methods in this unit do not call any functions in other units. They rely entirely on the implementation of `RefManager` (inherited) and the definition of `LootValidatorRef` (defined in the same header).
*   **Called By**: According to the provided MAP, no other units explicitly call these members. However, in the broader context of the `Loot` struct (also in `LootMgr.h`), the `Loot` object contains an instance of `LootValidatorRefManager` (`m_LootValidatorRefManager`). While the MAP does not list external callers, the design implies that code handling loot destruction or validation would iterate through this manager to clean up references. The absence of listed callers in the MAP suggests that the iteration might be handled internally within the `Loot` class's destructor or cleanup methods (like `Loot::clear`), or that the specific iteration points were not captured in the cross-file analysis.

**Data Model**

This unit does not interact with any database tables. It operates purely in memory, managing pointers to `LootValidatorRef` objects associated with a `Loot` instance.

**Notable Implementation Details**

*   **Static Casting**: The `getFirst` and `getLast` methods use `static_cast` (via C-style cast syntax `(LootValidatorRef*)`) to convert the return value of the base class methods. This assumes that the `RefManager` correctly stores `LootValidatorRef` instances. Since `LootValidatorRef` inherits from `Reference<Loot, LootValidatorRef>`, and `RefManager` is templated on these types, this cast is safe provided the base class implementation is correct.
*   **Iterator Type**: The `iterator` typedef is `LinkedListHead::Iterator<LootValidatorRef>`. This indicates that the underlying data structure is a linked list, likely implemented via the `LinkedListHead` utility class. The iterators are constructed by passing the raw pointer returned by `getFirst`/`getLast` or `nullptr`.
*   **No Ownership**: `LootValidatorRefManager` does not own the `LootValidatorRef` objects; it manages links to them. The actual lifetime management of the `LootValidatorRef` objects is handled by the `RefManager` base class and the `LootValidatorRef`'s own `targetObjectDestroyLink` and `sourceObjectDestroyLink` hooks (which are empty in this implementation, suggesting the validation logic is handled elsewhere or simply relies on the link removal).
*   **Triviality**: This class is a very small adapter. Its entire purpose is to expose the base class's linked list interface with the correct type and iterator semantics. There is no complex logic, error handling, or state management beyond what is inherited.

## Member Reference

**getFirst**
Returns a pointer to the first `LootValidatorRef` in the linked list by casting the result of the base class's `getFirst()` method.

**getLast**
Returns a pointer to the last `LootValidatorRef` in the linked list by casting the result of the base class's `getLast()` method.

**begin**
Constructs and returns an `iterator` initialized with the first element of the list (obtained via `getFirst()`), enabling forward iteration.

**end**
Constructs and returns an `iterator` initialized with `nullptr`, serving as the sentinel for forward iteration.

**rbegin**
Constructs and returns an `iterator` initialized with the last element of the list (obtained via `getLast()`), enabling reverse iteration.

**rend**
Constructs and returns an `iterator` initialized with `nullptr`, serving as the sentinel for reverse iteration.

---

<!-- machine-true, projected from graph.json -->

## Map — LootValidatorRefManager

*Source:* LootMgr.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| getFirst | method | — | — | — |
| getLast | method | — | — | — |
| begin | method | — | — | — |
| end | method | — | — | — |
| rbegin | method | — | — | — |
| rend | method | — | — | — |
