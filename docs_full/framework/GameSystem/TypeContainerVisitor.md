# TypeContainerVisitor

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# TypeContainerVisitor

`TypeContainerVisitor` is a template utility class implementing the Visitor pattern for traversing heterogeneous type containers (`TypeMapContainer` and `ContainerMapList`) in `wowvmangos`. It decouples iteration logic from element-specific operations by delegating to a user-provided `VISITOR` object.

## Purpose & Responsibilities

The class provides a uniform interface to visit elements stored in `TypeContainer`-based structures. Key responsibilities:
1.  **Traversal Encapsulation:** Hides the recursive structure of `ContainerMapList` and the wrapper nature of `TypeMapContainer`.
2.  **Delegation:** Calls the `Visit` method on the bound `VISITOR` for each element or sub-container.
3.  **Const-Correctness:** Offers mutable and const `Visit` overloads to support read-only and modifiable traversals.

## Member-by-Member Behavior

### Construction
**`TypeContainerVisitor<T, Y>`**
Stores a reference to the `VISITOR` object (`i_visitor`). No ownership transfer occurs; the visitor must remain valid during traversal.

### Visiting Containers
**`Visit` (mutable)**
Initiates traversal of a non-const `TYPE_CONTAINER`. Delegates to the free function `VisitorHelper`, which resolves the container type at compile time.

**`Visit` (const)**
Initiates traversal of a const `TYPE_CONTAINER`. Delegates to `VisitorHelper`, ensuring const-correctness is propagated to the visitor’s `Visit` calls.

## Cross-Unit Boundaries

*   **Calls Out:**
    *   **`VisitorHelper`**: A free function in this header that performs recursive descent. It specializes on container types to call `Visit` on the `VISITOR` or recurse into `ContainerMapList` members (`_element`, `_elements`, `_TailElements`) or `TypeMapContainer::GetElements()`.
*   **Called By:**
    *   The MAP lists no external callers. In practice, higher-level game logic instantiates this class with custom functors to apply operations (e.g., updates, saves) to entity collections.

## Data Model

This unit interacts with no database tables. It operates solely on in-memory C++ structures.

## Notable Implementation Details

1.  **Helper-Based Recursion:** Traversal logic resides in `VisitorHelper`, not the class. Specializations handle `ContainerMapList<TypeNull>` (termination), `ContainerMapList<T>` (single element), `ContainerMapList<TypeList<H, T>>` (recursive head/tail), and `TypeMapContainer` (delegation).
2.  **Zero Runtime Overhead:** All container structure navigation is resolved at compile time via templates. No virtual dispatch or type checking occurs in the traversal loop itself.
3.  **Stateless:** The class holds only the visitor reference, making it lightweight for stack allocation.

## Member Reference

**TypeContainerVisitor<T, Y>**
Constructor initializing `i_visitor` with the provided `VISITOR` reference `v`.

**Visit#2**
Const overload of `Visit` for `const TYPE_CONTAINER&`. Delegates to `VisitorHelper` for read-only traversal.

**Visit**
Mutable overload of `Visit` for `TYPE_CONTAINER&`. Delegates to `VisitorHelper` for modifiable traversal.

---

<!-- machine-true, projected from graph.json -->

## Map — TypeContainerVisitor

*Source:* TypeContainerVisitor.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| TypeContainerVisitor<T, Y> | ctor | — | — | — |
| Visit#2 | function | — | — | — |
| Visit | function | — | — | — |
