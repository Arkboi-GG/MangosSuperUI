# ObjectUpdater

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ObjectUpdater

**Purpose & Responsibilities**

`ObjectUpdater` is a visitor struct within the `MaNGOS` namespace, defined in `GridNotifiers.h`. Its sole responsibility is to drive the periodic state updates for `Creature` objects residing in a specific grid cell. It is part of the server's spatial partitioning system, where the world is divided into grids, and grids into cells. When the server needs to update the logical state of entities in a cell (such as processing AI ticks, movement, or spell effects), it invokes `ObjectUpdater` via the grid's notification mechanism.

Unlike other notifiers in this file (e.g., `VisibleNotifier`, which handles client visibility, or `MessageDeliverer`, which sends packets), `ObjectUpdater` performs server-side logic updates. It specifically targets `Creature` objects, ignoring Players, Corpses, Cameras, and other object types, as these are updated through different mechanisms or do not require this specific tick-based update loop.

**Member-by-Member Behavior**

The `ObjectUpdater` struct contains a constructor and five `Visit` methods. The `Visit` methods are overloaded to accept different map types representing collections of objects within a grid cell. The MAP distinguishes between the generic template `Visit` and three specific empty overloads labeled `Visit#2`, `Visit#3`, and `Visit#4`.

*   **Constructor (`ObjectUpdater`)**: Initializes the updater with two time-related values: `i_timeDiff` (the elapsed time since the last update, in milliseconds) and `i_now` (the current server time). These values are passed down to the individual creatures to ensure their internal timers and AI logic advance correctly relative to the server's clock.

*   **`Visit(CreatureMapType&)`**: This is the primary functional method. It iterates over all `Creature` objects in the provided `CreatureMapType` (the collection of creatures in the current grid cell). For each creature, it creates a `WorldObject::UpdateHelper` and calls `UpdateRealTime` on it, passing the stored `i_now` and `i_timeDiff`. This triggers the creature's internal update cycle, which typically includes AI execution, movement processing, and spell effect ticks. The iterator is incremented before calling `UpdateRealTime` because the update process may cause the creature to be removed from the map (e.g., if it dies or despawns), which would invalidate the iterator.

*   **`Visit#2` (`Visit(PlayerMapType&)`)**: This method is declared as an empty stub (defined as `{}` in the header). It exists to satisfy the visitor interface required by the grid notification system but performs no action. This ensures that `ObjectUpdater` can be passed to the grid's generic visitation routine without causing compilation errors or unintended side effects on player objects.

*   **`Visit#3` (`Visit(CorpseMapType&)`)**: This method is declared as an empty stub (defined as `{}` in the header). It exists to satisfy the visitor interface required by the grid notification system but performs no action. This ensures that `ObjectUpdater` can be passed to the grid's generic visitation routine without causing compilation errors or unintended side effects on corpse objects.

*   **`Visit#4` (`Visit(CameraMapType&)`)**: This method is declared as an empty stub (defined as `{}` in the header). It exists to satisfy the visitor interface required by the grid notification system but performs no action. This ensures that `ObjectUpdater` can be passed to the grid's generic visitation routine without causing compilation errors or unintended side effects on camera objects.

**Cross-Unit Boundaries**

*   **Called By**:
    *   `Map.Main/UpdateActiveCellsCallback`: This function in the `Map` unit is responsible for iterating over active grid cells and invoking updates. It constructs an `ObjectUpdater` instance and passes it to the grid's visitation mechanism to trigger the update cycle for all creatures in active cells.
    *   `Map.Main/UpdateCellsAroundObject`: Similar to the above, this function updates cells surrounding a specific object (likely during relocation or significant state changes). It also uses `ObjectUpdater` to ensure creatures in those cells are synchronized with the latest time delta.

*   **Calls Out**:
    *   `ObjectUpdater` does not directly call out to other units in the sense of invoking functions on other classes. However, its `Visit(CreatureMapType&)` method indirectly relies on `WorldObject::UpdateHelper` and the `UpdateRealTime` method of `WorldObject` (and its derived class `Creature`). These are part of the core object hierarchy, not separate "units" in the context of this map, but they are the critical dependencies for the update logic.

**Data Model**

`ObjectUpdater` does not interact with any database tables. It operates entirely on in-memory object states.

**Notable Implementation Details**

*   **Iterator Safety**: In `Visit(CreatureMapType&)`, the iterator is incremented (`++iter`) *before* calling `helper.UpdateRealTime(...)`. This is a crucial defensive programming technique. If `UpdateRealTime` causes the creature to be removed from the map (e.g., due to death, despawn, or relocation to another cell), the iterator would become invalid. By incrementing first, the loop remains safe even if the current element is erased.
*   **Selective Updating**: The empty implementations for `PlayerMapType`, `CorpseMapType`, and `CameraMapType` indicate that `ObjectUpdater` is specialized for creatures. Players are likely updated through their session loops or direct input handling, while corpses and cameras have different update requirements or lifecycles.
*   **Time Management**: The separation of `i_timeDiff` and `i_now` allows for precise simulation of time passage. `i_timeDiff` ensures that AI and timers progress proportionally to the actual time elapsed, preventing issues like "rubber-banding" or desynchronized timers if the server frame rate fluctuates. `i_now` provides an absolute timestamp for any logic that requires knowing the current server time.

## Member Reference

**ObjectUpdater**
Constructs the updater with the time difference since the last update (`i_timeDiff`) and the current server time (`i_now`). These values are used to drive the real-time updates of creatures.

**Visit**
Iterates through all creatures in the given `CreatureMapType`. For each creature, it creates a `WorldObject::UpdateHelper` and calls `UpdateRealTime` with the stored time values. The iterator is incremented before the update call to prevent invalidation if the creature is removed from the map during the update.

**Visit#2**
Empty stub corresponding to `Visit(PlayerMapType&)`. Does nothing. Ensures the visitor interface is complete for player maps.

**Visit#3**
Empty stub corresponding to `Visit(CorpseMapType&)`. Does nothing. Ensures the visitor interface is complete for corpse maps.

**Visit#4**
Empty stub corresponding to `Visit(CameraMapType&)`. Does nothing. Ensures the visitor interface is complete for camera maps.

---

<!-- machine-true, projected from graph.json -->

## Map — ObjectUpdater

*Source:* GridNotifiers.h, GridNotifiersImpl.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| Visit#3 | method | — | — | — |
| ObjectUpdater | ctor | — | Map.Main/UpdateActiveCellsCallback, Map.Main/UpdateCellsAroundObject | — |
| Visit#4 | method | — | — | — |
| Visit#2 | method | — | — | — |
| Visit | method | — | — | — |
