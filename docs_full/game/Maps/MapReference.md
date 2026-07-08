<!-- provenance: verbose -->
# MapReference

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`MapReference` is a specialized node in the `wowvmangos` linked-reference system, managing the relationship between a `Map` (target) and a `Player` (source). Inheriting from `Reference<Map, Player>`, it overrides lifecycle hooks to maintain accurate reference counts on the target `Map`'s `m_mapRefManager`. This ensures the `Map` correctly tracks how many players reference it, supporting memory management and activity tracking.

## Member-by-Member Behavior

### Lifecycle and Link Management

**`MapReference` (Constructor)**
Delegates to `Reference<Map, Player>()`. No additional initialization.

**`~MapReference` (Destructor)**
Calls `unlink()` (inherited from `Reference`) to ensure the reference link is severed and counts updated before destruction.

**`targetObjectBuildLink`**
Invoked when a link is established. Inserts the reference at the head of the target `Map`'s list via `LinkedListHead::insertFirst` and increments the count via `LinkedListHead::incSize`.

**`targetObjectDestroyLink`**
Invoked when a link is removed from the target side. Checks `isValid()` (inherited) to prevent double-decrementing; if valid, decrements the target's count via `LinkedListHead::decSize`.

**`sourceObjectDestroyLink`**
Invoked when the source `Player` is destroyed. Unconditionally decrements the target `Map`'s count via `LinkedListHead::decSize`, ensuring cleanup even if explicit unlinking was missed.

### Navigation

**`next` / `next#2`**
Return pointers to the next `MapReference` in the list by casting the base `Reference::next()` result. `next#2` is the const overload.

## Cross-Unit Boundaries

### Calls Out

*   **`LinkedListHead`**: `targetObjectBuildLink` calls `insertFirst` and `incSize`; `targetObjectDestroyLink` and `sourceObjectDestroyLink` call `decSize` on the target `Map`'s `m_mapRefManager`. This maintains the linked list structure and reference counts.
*   **`Reference<Map, Player>` (Base Class)**: Inherits `unlink()`, `isValid()`, and `next()`. `MapReference` relies on the base class for low-level pointer manipulation and list maintenance.

### Called By

*   **`Player.Main`**: `Player.Main::GiveLevel` calls `next#2`. This indicates iteration over `MapReference` nodes during player level-up processing, likely to update map-specific states or clean up references.

## Data Model

This unit operates entirely in-memory. It does not access any database tables.

## Notable Implementation Details

*   **Asymmetric Cleanup**: `targetObjectDestroyLink` checks `isValid()` before decrementing, while `sourceObjectDestroyLink` does not. This suggests `targetObjectDestroyLink` may be called in contexts where the link might already be severed, whereas source destruction guarantees a pending reference.
*   **Unsafe Casts**: `next` methods use C-style casts to `MapReference*`. This assumes all nodes in `m_mapRefManager` are `MapReference` objects; mixing types would cause undefined behavior.
*   **Destructor Safety**: Explicit `unlink()` in the destructor prevents reference count leaks if a `MapReference` is destroyed while still linked.

## Member Reference

**`targetObjectBuildLink`**: Inserts reference at head of target list and increments count via `LinkedListHead::insertFirst` and `incSize`.

**`targetObjectDestroyLink`**: Checks `isValid()` and decrements target count via `LinkedListHead::decSize` if valid.

**`sourceObjectDestroyLink`**: Unconditionally decrements target count via `LinkedListHead::decSize`.

**`MapReference`**: Constructor delegating to `Reference<Map, Player>()`.

**`~MapReference`**: Destructor calling `unlink()` to sever links.

**`next`**: Returns non-const pointer to next `MapReference` via cast.

**`next#2`**: Returns const pointer to next `MapReference` via cast. Called by `Player.Main::GiveLevel`.

---

<!-- machine-true, projected from graph.json -->

## Map — MapReference

*Source:* MapReference.cpp, MapReference.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| targetObjectBuildLink | method | LinkedListHead/incSize, LinkedListHead/insertFirst | — | — |
| targetObjectDestroyLink | method | LinkedListHead/decSize | — | — |
| sourceObjectDestroyLink | method | LinkedListHead/decSize | — | — |
| MapReference | ctor | — | — | — |
| ~MapReference | dtor | — | — | — |
| next | method | — | — | — |
| next#2 | method | — | Player.Main/GiveLevel | — |
