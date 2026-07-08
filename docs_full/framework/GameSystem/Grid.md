# Grid

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Grid

## Purpose & Responsibilities

`Grid` is a templated container representing a logical segment of the game world. It manages two distinct object categories: **World Objects** (persistent entities like players/NPCs) and **Grid Objects** (transient entities like corpses/items). Parameterized by `ACTIVE_OBJECT`, `WORLD_OBJECT_TYPES`, and `GRID_OBJECT_TYPES`, it maintains separate `TypeMapContainer` instances for each category and tracks a subset of "active" Grid Objects in `m_activeGridObjects` for efficient counting. It delegates loading/unloading to `GridLoader` (a friend class) and provides visitor interfaces for iteration.

## Member-by-Member Behavior

### Object Lifecycle
- **`AddWorldObject` / `RemoveWorldObject`**: Insert or remove a `SPECIFIC_OBJECT` pointer from the `i_objects` container. They return the boolean result of the underlying container operation.
- **`AddGridObject` / `RemoveGridObject`**: Manage Grid Objects in `i_container`. Additionally, if `obj->IsActiveObject()` returns true, the pointer is inserted into or erased from `m_activeGridObjects` (a `std::set<void*>`). This tracking is snapshot-based: it reflects the object's active state *only* at the moment of addition or removal. If an object's active state changes while resident in the grid, `m_activeGridObjects` becomes stale until the object is re-added.

### Iteration & Inspection
- **`Visit`**: Two overloads accept `TypeContainerVisitor` instances specialized for `GRID_OBJECT_TYPES` or `WORLD_OBJECT_TYPES`, visiting `i_container` or `i_objects` respectively. This enables type-safe traversal of the heterogeneous containers.
- **`ActiveObjectsInGrid`**: Returns the total count of active entities. It sums `m_activeGridObjects.size()` (active Grid Objects) and `i_objects.template Count<ACTIVE_OBJECT>()` (all World Objects of the `ACTIVE_OBJECT` type, assumed active by virtue of their type presence).

## Cross-Unit Boundaries

- **Calls Out**: None. `Grid` is a passive data structure.
- **Called By**: External units (e.g., spawn/despawn handlers, movement systems) invoke `Add*`/`Remove*` methods to register entities entering or leaving the grid. `GridLoader` (friend) accesses private members directly for initialization or bulk operations.

## Data Model

No database tables are accessed. All state is held in-memory via `TypeMapContainer` and `std::set`.

## Notable Implementation Details

1. **Stale Active Tracking**: `m_activeGridObjects` is only updated during `AddGridObject`/`RemoveGridObject`. There is no mechanism to update it if an object’s `IsActiveObject()` status changes mid-residency.
2. **Void Pointer Set**: `m_activeGridObjects` stores `void*` pointers, losing type information. It is used solely for counting/existence checks, not iteration.
3. **Type Assumption**: `ActiveObjectsInGrid` assumes all `ACTIVE_OBJECT` instances in `i_objects` are active, unlike Grid Objects which require an explicit `IsActiveObject()` check.

## Member Reference

**~Grid<ACTIVE_OBJECT, WORLD_OBJECT_TYPES, GRID_OBJECT_TYPES>**
Empty destructor. Cleanup is handled by member container destructors or the owning `GridLoader`.

**ActiveObjectsInGrid**
Returns `m_activeGridObjects.size() + i_objects.template Count<ACTIVE_OBJECT>()`, providing a quick count of active entities in the grid.

---

<!-- machine-true, projected from graph.json -->

## Map — Grid

*Source:* Grid.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ~Grid<ACTIVE_OBJECT, WORLD_OBJECT_TYPES, GRID_OBJECT_TYPES> | dtor | — | — | — |
| ActiveObjectsInGrid | function | — | — | — |
