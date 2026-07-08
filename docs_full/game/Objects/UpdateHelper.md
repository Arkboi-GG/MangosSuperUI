# UpdateHelper

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# UpdateHelper

**Purpose & Responsibilities**

`UpdateHelper` is a nested class within `WorldObject` (defined in `Object.h`) that serves as a RAII-style wrapper for invoking the `WorldObject::Update` lifecycle method. Its primary responsibility is to accurately calculate the elapsed time since the previous update tick and pass this delta, along with the global server tick difference, to the underlying `WorldObject`. It ensures that the internal `WorldUpdateCounter` (`m_updateTracker`) associated with the `WorldObject` is correctly reset after each update cycle, maintaining precise timing for spell ticks, movement interpolation, and other time-dependent logic.

The class is designed to be instantiated on the stack during the map update loop. It decouples the timing measurement logic from the core update logic, allowing `WorldObject` to remain unaware of the specific mechanism used to measure the interval between its own updates.

## Member-by-Member Behavior

### Construction and Destruction

*   **`UpdateHelper(WorldObject* obj)`**: The constructor accepts a pointer to the `WorldObject` being updated. It stores this pointer in the private member `m_obj`. No timing initialization occurs here; the `WorldUpdateCounter` inside the `WorldObject` retains its state from the previous tick until `Update` or `UpdateRealTime` is called.
*   **`~UpdateHelper()`**: The destructor is defaulted. Since `UpdateHelper` is typically used as a local variable in a scope that explicitly calls `Update` or `UpdateRealTime`, the destructor performs no cleanup. The critical state management (resetting the timer) happens explicitly within the update methods, not in the destructor.

### Update Methods

*   **`Update(uint32 time_diff)`**:
    1.  Retrieves the elapsed time since the last update by calling `m_obj->m_updateTracker.timeElapsed()`. This calculates the difference between the stored start time and the current world tick time.
    2.  Calls `m_obj->Update(elapsed_time, time_diff)`, passing the calculated elapsed time and the provided `time_diff` (likely the global server tick interval).
    3.  Resets the tracker by calling `m_obj->m_updateTracker.Reset()`, which sets the start time to the current world tick time. This prepares the tracker for the next update cycle.

*   **`UpdateRealTime(uint32 now, uint32 time_diff)`**:
    1.  Calculates the elapsed time using `m_obj->m_updateTracker.timeElapsed(now)`. This variant allows passing an explicit "now" timestamp, likely used in scenarios where the current tick time needs to be specified explicitly (e.g., during certain synchronization steps or when the standard tick time isn't appropriate).
    2.  Calls `m_obj->Update(elapsed_time, time_diff)`.
    3.  Resets the tracker using `m_obj->m_updateTracker.ResetTo(now)`, setting the start time to the explicitly provided `now` value rather than the current world tick.

### Copy Control

*   **`UpdateHelper(UpdateHelper const&)`**: Deleted copy constructor.
*   **`UpdateHelper& operator=(UpdateHelper const&)`**: Deleted assignment operator.
    These deletions enforce that `UpdateHelper` instances cannot be copied or assigned, ensuring that each instance is uniquely tied to a single `WorldObject` pointer and preventing accidental duplication of state or double-updating issues.

## Cross-Unit Boundaries

*   **Calls into `WorldObject`**:
    *   `UpdateHelper` calls `WorldObject::Update` (defined in `WorldObject` partial, likely in `WorldObject.cpp`). This is the core method that processes the object's state changes over the elapsed time.
    *   It accesses `WorldObject::m_updateTracker` (a `WorldUpdateCounter` member) to read and reset the timing state.
*   **Called by `Map.Main/UpdatePlayers`**:
    *   The MAP indicates that `UpdateHelper` is constructed and used by the `Map` unit's main update loop (specifically `UpdatePlayers` or similar grid/cell update routines). The `Map` unit iterates through objects in active cells, creates an `UpdateHelper` for each relevant `WorldObject`, and invokes either `Update` or `UpdateRealTime` to advance the object's state.

## Data Model

This unit does not interact directly with any database tables. It operates entirely on in-memory state (`WorldObject` and `WorldUpdateCounter`).

## Notable Implementation Details

1.  **RAII-like Timing Management**: Although the destructor is empty, the class acts as a scope-bound manager for the update timing. The critical invariant is that `m_updateTracker` is reset *after* the `Update` call. This ensures that the next time `timeElapsed()` is called, it measures the interval from the *end* of the previous update to the *start* of the next.
2.  **Two Update Modes**: The existence of both `Update` and `UpdateRealTime` suggests two distinct timing contexts. `Update` relies on the global `WorldTimer::tickTime()`, while `UpdateRealTime` allows injecting a specific timestamp. This flexibility might be necessary for handling objects that are updated outside the standard game loop or for precise synchronization in distributed or lag-compensated scenarios.
3.  **No Internal State Beyond Pointer**: `UpdateHelper` holds no timing state itself. It delegates all timing logic to `WorldUpdateCounter` within the `WorldObject`. This keeps `UpdateHelper` lightweight and focused solely on orchestration.
4.  **Deleted Copy/Swap**: Preventing copying ensures thread-safety concerns related to shared mutable state are minimized, as each `UpdateHelper` is strictly bound to one `WorldObject` instance for the duration of its scope.

## Member Reference

*   **`UpdateHelper`**: Constructor that initializes the helper with a pointer to the `WorldObject` to be updated.
*   **`~UpdateHelper`**: Defaulted destructor; performs no action.
*   **`Update`**: Calculates elapsed time using the object's internal tracker, calls `WorldObject::Update` with the elapsed time and provided `time_diff`, then resets the tracker to the current world tick.
*   **`UpdateRealTime`**: Similar to `Update`, but uses an explicitly provided `now` timestamp for calculating elapsed time and resetting the tracker.
*   **`UpdateHelper#2`**: Deleted copy constructor.
*   **`operator=`**: Deleted assignment operator.

---

<!-- machine-true, projected from graph.json -->

## Map — UpdateHelper

*Source:* Object.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| UpdateHelper | ctor | — | Map.Main/UpdatePlayers | — |
| ~UpdateHelper | decl | — | — | — |
| Update | method | — | — | — |
| UpdateRealTime | method | — | Map.Main/UpdatePlayers | — |
| UpdateHelper#2 | decl | — | — | — |
| operator= | decl | — | — | — |
