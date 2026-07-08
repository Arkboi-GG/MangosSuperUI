# LootValidatorRef

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`LootValidatorRef` is a lightweight adapter class within `LootMgr.h` that enables the `Loot` struct to notify external listeners when the loot object is destroyed or invalidated. It inherits from `Reference<Loot, LootValidatorRef>`, a template-based linked-list node provided by the engine’s utility library (`Utilities/LinkedReference/RefManager.h`).

Its sole responsibility is to participate in the `LootValidatorRefManager` system, which tracks objects (such as loot rolls or pending transactions) that depend on the validity of a specific `Loot` instance. When a `Loot` object is cleared or destroyed, it iterates through its `LootValidatorRefManager` and invokes callbacks on each attached `LootValidatorRef`. `LootValidatorRef` provides the interface for these callbacks but implements them as no-ops, serving primarily as a marker or placeholder in the reference chain.

## Member-by-Member Behavior

### **LootValidatorRef** (Constructor)
The default constructor initializes the base `Reference` class. It does not perform any custom initialization logic. This allows instances to be created and inserted into the `LootValidatorRefManager` without side effects.

### **targetObjectDestroyLink**
This method overrides the virtual destructor-link callback from the `Reference` base class. In the context of the `RefManager` pattern, this is called when the *target* of the reference (the `Loot` object) is being destroyed or cleared. The implementation is empty (`{}`), indicating that `LootValidatorRef` itself does not need to perform cleanup actions when the loot it references is removed. The actual notification logic is handled by the manager or the owner of the reference, not the reference node itself.

### **sourceObjectDestroyLink**
This method overrides the virtual source-link callback from the `Reference` base class. It is called when the *source* object holding the reference is destroyed. Like `targetObjectDestroyLink`, the implementation is empty. This ensures that if a `LootValidatorRef` instance goes out of scope or is deleted, it cleanly detaches from the `Loot` object’s reference list without requiring explicit manual removal code in destructors.

## Cross-Unit Boundaries

`LootValidatorRef` interacts exclusively with the internal reference management infrastructure defined in `Utilities/LinkedReference/RefManager.h`.

*   **Calls Out:** None. The methods are empty stubs.
*   **Called By:**
    *   **`Loot` (in `LootMgr.h`):** The `Loot` struct contains a `LootValidatorRefManager`. When `Loot::clear()` is called, it invokes `m_LootValidatorRefManager.clearReferences()`. This triggers the reference manager to iterate over all attached `LootValidatorRef` nodes and call their `targetObjectDestroyLink` methods.
    *   **`RefManager` Base Class:** The base class machinery calls `sourceObjectDestroyLink` when a `LootValidatorRef` instance is removed from the list or destroyed.

There are no cross-file dependencies beyond the inclusion of `RefManager.h`. The class does not interact with database tables, network packets, or other game systems directly.

## Data Model

`LootValidatorRef` does not access any database tables. It operates entirely in memory as part of the runtime loot handling logic.

## Notable Implementation Details

1.  **Empty Callbacks:** Both `targetObjectDestroyLink` and `sourceObjectDestroyLink` are implemented as empty functions. This suggests that `LootValidatorRef` is used as a *passive* reference holder. The act of being attached to the `Loot` object is sufficient for the system to track the dependency; the reference node itself does not need to react to lifecycle events. The reaction to the loot becoming invalid is likely handled by the object that *owns* the `LootValidatorRef` (e.g., a `Roll` object), which checks its own validity or state upon being notified via the manager, rather than the reference node performing the action.
2.  **Inheritance Pattern:** By inheriting from `Reference<Loot, LootValidatorRef>`, the class embeds pointers to the previous and next nodes in the linked list managed by `LootValidatorRefManager`. This allows O(1) insertion and removal from the loot’s validator list.
3.  **Memory Management:** Because the callbacks are empty, there is no risk of dangling pointer dereferences within the reference node itself during destruction. The safety relies on the `RefManager` correctly unlinking nodes before they are freed.

## Member Reference

**LootValidatorRef**
Default constructor for the `LootValidatorRef` class. Initializes the base `Reference` class. No custom logic is performed.

**targetObjectDestroyLink**
Overrides the base class virtual method. Called when the referenced `Loot` object is destroyed or cleared. The implementation is empty, serving as a no-op placeholder to satisfy the interface requirements of the `Reference` template.

**sourceObjectDestroyLink**
Overrides the base class virtual method. Called when the `LootValidatorRef` instance itself is destroyed or removed from the reference list. The implementation is empty, ensuring clean detachment from the `Loot` object’s reference chain without manual intervention.

---

<!-- machine-true, projected from graph.json -->

## Map — LootValidatorRef

*Source:* LootMgr.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| LootValidatorRef | ctor | — | — | — |
| targetObjectDestroyLink | method | — | — | — |
| sourceObjectDestroyLink | method | — | — | — |
