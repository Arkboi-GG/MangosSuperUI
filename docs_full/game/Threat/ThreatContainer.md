# ThreatContainer

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ThreatContainer

**ThreatContainer** is a protected helper class within the `ThreatManager` subsystem of the WoWVMaNGOS server engine. It serves as the underlying storage and sorting mechanism for the active threat list of a `Creature` or `Unit`. Its primary responsibility is to maintain a `std::list` of `HostileReference` pointers, ensuring that the list remains sorted by threat value (highest threat at the front) so that the most hated target can be retrieved in constant time.

The class operates on a "dirty flag" pattern: modifications to the list (adding or removing references) mark the container as dirty (`iDirty = true`). The actual sorting operation is deferred until explicitly requested via `update()` (called by `ThreatManager`) or implicitly required by accessors like `getMostHated()`, depending on the specific implementation path in `ThreatManager`. This design minimizes expensive sort operations during high-frequency combat events.

Because `ThreatContainer` is declared with `protected` visibility for its core mutation methods (`remove`, `addReference`) and grants `friend` access to `ThreatManager`, it is not intended for direct external use. It is strictly an internal component of the threat management lifecycle, handling the low-level list maintenance while `ThreatManager` handles the high-level game logic (taunts, offline lists, victim selection).

## Member-by-Member Behavior

### Construction and Destruction
*   **ThreatContainer**: Initializes the internal `iThreatList` (empty) and sets the `iDirty` flag to `false`.
*   **~ThreatContainer**: Calls `clearReferences()` to ensure all `HostileReference` objects stored in the list are properly cleaned up. This prevents memory leaks by invoking the destruction logic of the linked-reference system.

### State Management
*   **setDirty**: Sets the internal `iDirty` boolean to the provided value. This is used by `ThreatManager` to signal that the list order may be invalid after a modification.
*   **isDirty**: Returns the current state of the `iDirty` flag.
*   **empty**: Returns `true` if the internal `iThreatList` contains no elements.

### List Accessors
*   **getMostHated**: Returns the `HostileReference` pointer at the front of the list (`iThreatList.front()`). If the list is empty, it returns `nullptr`. This assumes the list is sorted such that the highest threat is first.
*   **getThreatList**: Returns a constant reference to the internal `std::list<HostileReference*>`. This allows external iterators (primarily from `ThreatManager`) to traverse the entire threat list.

### Internal Mutations (Protected/Friend Access)
These methods are marked `protected` and are primarily called by `ThreatManager` (via friendship) or potentially by derived classes, though the current codebase shows `ThreatManager` as the main consumer.

*   **addReference**: Appends a `HostileReference` pointer to the back of `iThreatList`. This operation marks the container as dirty implicitly through the caller's logic (usually `ThreatManager` sets the dirty flag after calling this).
*   **remove**: Removes the specified `HostileReference` pointer from `iThreatList`. Like `addReference`, this invalidates the sorted order, requiring a subsequent sort/update.

## Cross-Unit Boundaries

**ThreatContainer** exists solely to support **ThreatManager**. It does not call out to any other units itself. Its interactions are entirely inbound from `ThreatManager`:

1.  **ThreatManager::processThreatEvent**: Calls `ThreatContainer::remove` when a hostile reference needs to be detached from the active list (e.g., when a unit goes offline or dies).
2.  **ThreatManager::addThreatDirectly** and **ThreatManager::processThreatEvent**: Call `ThreatContainer::addReference` when a new hostile entity enters the threat list or an existing one is re-added.
3.  **ThreatManager**: Calls `ThreatContainer::setDirty` and `ThreatContainer::isDirty` to manage the sorting state.
4.  **ThreatManager**: Calls `ThreatContainer::getMostHated` and `ThreatContainer::getThreatList` to retrieve targets for AI decision-making.

**Note on HostileReference**: While `ThreatContainer` stores `HostileReference` pointers, it does not instantiate them. `HostileReference` is a separate class defined in `ThreatManager.h` that inherits from `Reference<Unit, ThreatManager>`. `ThreatContainer` treats these objects as opaque pointers for storage and sorting purposes, relying on `HostileReference`'s own logic for threat calculation and link management.

## Data Model

**ThreatContainer** does not interact with any database tables. All data is held in memory within the `std::list<HostileReference*>` structure. The threat values themselves are stored within the `HostileReference` objects, not in the container.

## Notable Implementation Details

1.  **Sorting Strategy**: The class relies on the assumption that `iThreatList` is sorted by threat value in descending order. The `getMostHated()` method simply returns `front()`. The actual sorting logic is not visible in `ThreatContainer`'s public interface but is likely handled by the `update()` method (which is declared but not defined in this header snippet, implying it is implemented in the corresponding `.cpp` file or inherited). The `iDirty` flag is the critical control mechanism for this lazy sorting.
2.  **Memory Management**: The destructor calls `clearReferences()`. This is crucial because `HostileReference` objects are dynamically allocated. Failure to clean them up would result in significant memory leaks during zone changes or creature despawns.
3.  **Friendship**: `ThreatManager` is declared as a `friend` class. This allows `ThreatManager` to bypass the `protected` visibility of `addReference` and `remove`, enabling tight coupling between the manager and the container. This is a deliberate design choice to keep the container's API minimal and prevent accidental misuse by other parts of the codebase.
4.  **No Copy Semantics**: The class does not define copy constructors or assignment operators. Given that it manages raw pointers to complex linked-reference objects, copying a `ThreatContainer` would lead to double-free errors or dangling pointers. It is designed to be a unique, non-copyable component of a `ThreatManager`.

## Member Reference

**remove**
Removes the specified `HostileReference` pointer from the internal `iThreatList`. This is a protected method, primarily called by `ThreatManager::processThreatEvent` when a threat link is broken. It does not delete the `HostileReference` object itself; that is handled by the reference counting system in `HostileReference`.

**addReference**
Appends the specified `HostileReference` pointer to the end of `iThreatList`. This is a protected method, called by `ThreatManager::addThreatDirectly` and `ThreatManager::processThreatEvent` when adding a new target to the threat list. It marks the list as unsorted (dirty).

**ThreatContainer**
Constructor. Initializes `iDirty` to `false`. The `iThreatList` is default-initialized as empty.

**~ThreatContainer**
Destructor. Calls `clearReferences()` to iterate through the list and properly destroy/remove all `HostileReference` objects, preventing memory leaks.

**setDirty**
Sets the `iDirty` flag to the provided boolean value. Used by `ThreatManager` to indicate that the list order is invalid and needs re-sorting.

**isDirty**
Returns the current value of the `iDirty` flag. Allows `ThreatManager` to check if a sort operation is pending.

**empty**
Returns `true` if `iThreatList` contains no elements. Provides a quick check for an empty threat list.

**getMostHated**
Returns the `HostileReference` pointer at the front of `iThreatList`. Assumes the list is sorted by threat value (descending). Returns `nullptr` if the list is empty. This is the primary method for determining the current target.

**getThreatList**
Returns a constant reference to the internal `iThreatList`. Allows iteration over all threats, typically used by `ThreatManager` for debugging, logging, or complex victim selection algorithms.

---

<!-- machine-true, projected from graph.json -->

## Map — ThreatContainer

*Source:* ThreatManager.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| remove | method | — | ThreatManager/processThreatEvent | — |
| addReference | method | — | ThreatManager/addThreatDirectly, ThreatManager/processThreatEvent | — |
| ThreatContainer | ctor | — | — | — |
| ~ThreatContainer | dtor | — | — | — |
| setDirty | method | — | — | — |
| isDirty | method | — | — | — |
| empty | method | — | — | — |
| getMostHated | method | — | — | — |
| getThreatList | method | — | — | — |
