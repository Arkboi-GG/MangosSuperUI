# IdleState

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# IdleState

## Purpose & Responsibilities

`IdleState` is a concrete implementation of the `GridState` abstract base class, defined in `GridStates.h`. It represents one of the possible lifecycle states for a grid cell within the game world map management system (specifically, the MaNGOS/WoWVMaNGOS server architecture).

In this state machine pattern, a grid can exist in various states such as `Active`, `Idle`, `Invalid`, or `Removal`. The `IdleState` class encapsulates the behavior required when a grid is considered "idle"—typically meaning it contains no active entities or processes requiring frequent updates, allowing the server to optimize resource usage by updating these grids less frequently or differently than active ones.

As a leaf class in this hierarchy, `IdleState` provides a specific implementation of the pure virtual `Update` method declared in `GridState`. However, the provided source code for `IdleState` itself contains only the destructor declaration and the interface inheritance; the actual logic for the `Update` method is implemented in a corresponding `.cpp` file (not provided in the source snippet, but implied by the class structure). The MAP confirms that `IdleState` has no outgoing calls to other units and is not called by other units in the context of this specific translation unit's boundary analysis, suggesting its primary interaction is through the polymorphic `Update` interface called by the `Map` manager.

## Member-by-Member Behavior

### Destructor

**~IdleState**
The destructor for `IdleState`. It is marked `override` to explicitly satisfy the virtual destructor requirement from the base class `GridState`. Since `IdleState` does not manage any dynamic memory or resources in its definition, this destructor is trivial. Its presence ensures proper cleanup if an `IdleState` object is deleted via a `GridState*` pointer, adhering to standard C++ polymorphism best practices.

## Cross-Unit Boundaries

According to the provided MAP, `IdleState` has **no** explicit calls out to other units and is **not** called by other units within the scope of this specific unit's dependency graph. 

However, conceptually, `IdleState` participates in a larger system:
1.  **Called By:** The `Map` class (and potentially `GridManager` or similar controllers) will hold pointers to `GridState` objects. When the map update loop runs, it will invoke the `Update` method on the current state of a grid. If the grid is in an idle state, the `Map` will call `IdleState::Update`. This interaction is polymorphic and occurs outside the direct call graph tracked in this specific MAP entry, likely because the `Map` class interacts with the abstract `GridState` interface rather than concrete subclasses directly in its header declarations.
2.  **Calls Out:** The implementation of `IdleState::Update` (in the associated `.cpp` file, not shown here) likely interacts with `Map`, `NGridType`, and `GridInfo` objects passed as arguments. These interactions are internal to the state update process and do not constitute cross-unit dependencies in the sense of calling distinct, separate architectural components like database handlers or network modules, unless the `Update` implementation itself triggers such calls (which is not visible in the provided header-only source).

## Data Model

This unit does not interact directly with any database tables. The `IdleState` class operates entirely in memory as part of the runtime state management for game world grids. No SQL queries or table references are present in the provided source code.

## Notable Implementation Details

1.  **Polymorphic Design:** `IdleState` is part of a classic State Pattern implementation. The base class `GridState` defines the interface (`Update`), and `IdleState` provides one concrete behavior. Other classes like `ActiveState`, `InvalidState`, and `RemovalState` provide alternative behaviors. This allows the `Map` manager to change a grid's behavior dynamically by swapping the `GridState` pointer without changing the manager's code.
2.  **Trivial Destructor:** The destructor `~IdleState()` is empty. This indicates that `IdleState` objects are lightweight and do not require custom cleanup logic. They are likely managed by smart pointers or owned by the `Map`/`Grid` structures that control their lifetime.
3.  **Const Correctness:** The `Update` method is declared `const`, implying that invoking the update logic does not modify the internal state of the `IdleState` object itself. Any side effects (e.g., modifying the grid data, moving entities) occur through the non-const references passed to the method (`Map&`, `NGridType&`, etc.).
4.  **Header-Only Definition:** The provided source shows only the header file `GridStates.h`. The actual implementation of `IdleState::Update` is not visible here. Therefore, the specific logic for how an idle grid is updated (e.g., whether it checks for new entities, cleans up expired data, or simply does nothing) cannot be detailed from this snippet alone. The documentation must rely on the assumption that the `.cpp` implementation follows the contract defined by the `GridState` interface.

## Member Reference

**~IdleState**
The destructor for the `IdleState` class. It is a trivial, empty destructor that overrides the virtual destructor from the base class `GridState`. It ensures proper polymorphic deletion if an `IdleState` instance is destroyed through a base class pointer. No resource cleanup is performed as the class holds no dynamic resources.

---

<!-- machine-true, projected from graph.json -->

## Map — IdleState

*Source:* GridStates.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ~IdleState | dtor | — | — | — |
