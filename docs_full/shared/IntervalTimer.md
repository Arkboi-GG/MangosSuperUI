<!-- provenance: verbose -->
# IntervalTimer

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`IntervalTimer` is a lightweight, stateful utility that tracks elapsed time against a fixed periodic interval. It accumulates time deltas passed to `Update` and allows callers to determine if the configured `_interval` has been exceeded via `Passed`. Unlike absolute-time trackers (e.g., `TimeTracker`), it uses a relative accumulation model, making it suitable for periodic server tasks like world updates or map manager cycles. It contains no external dependencies, database interactions, or thread-safety mechanisms.

## Member-by-Member Behavior

### Initialization and State Management

*   **`IntervalTimer`**: Initializes `_interval` and `_current` to `0`.

### Time Accumulation and Checking

*   **`Update`**: Adds the provided `time_t` delta to `_current`. If `_current` becomes negative (due to overflow or negative deltas), it clamps `_current` to `0`.
*   **`Passed`**: Returns `true` if `_current >= _interval`.

### Resetting and Configuration

*   **`Reset`**: If `_current >= _interval`, subtracts `_interval` from `_current` to preserve excess time for the next cycle. Otherwise, does nothing.
*   **`SetInterval`**: Sets the target duration `_interval`.
*   **`SetCurrent`**: Manually sets the accumulated time `_current`.

### Querying State

*   **`GetInterval`**: Returns `_interval`.
*   **`GetCurrent`**: Returns `_current`.

## Cross-Unit Boundaries

`IntervalTimer` is passive; it does not call other units. It is driven by `World` and `MapManager`:

*   **`World/Update` and `MapManager/Update`**: Call `Update` with time deltas, check `Passed` to trigger periodic actions, and call `Reset` if passed.
*   **`World/LoadConfigSettings`, `World/SetInitialWorldSettings`, `MapManager/MapManager`**: Call `SetInterval` to configure the timer based on settings.
*   **`World/SetWorldUpdateTimer`, `World/Update`, `MapManager/Update`**: Call `SetCurrent` or `GetCurrent` to synchronize or inspect state.
*   **`World/GetWorldUpdateTimerInterval`**: Calls `GetInterval` to retrieve the configured frequency.

## Data Model

`IntervalTimer` does not interact with any database tables.

## Notable Implementation Details

1.  **Negative Clamping in `Update`**: `Update` clamps `_current` to `0` if it goes negative, preventing invalid states from negative deltas or overflow.
2.  **Excess Preservation in `Reset`**: `Reset` subtracts `_interval` rather than zeroing `_current`, preserving excess time to maintain accurate average periods despite irregular loop timings.
3.  **No Thread Safety**: The class lacks synchronization primitives; it assumes single-threaded access (typically the main server loop).
4.  **`time_t` Usage**: Uses `time_t` for storage, which may vary in size/platform representation but is sufficient for typical server uptimes.

## Member Reference

**IntervalTimer**  
Constructor initializing `_interval` and `_current` to `0`.

**Update**  
Adds delta to `_current`; clamps to `0` if negative.

**Passed**  
Returns `true` if `_current >= _interval`.

**Reset**  
Subtracts `_interval` from `_current` if `_current >= _interval`; otherwise does nothing.

**SetCurrent**  
Sets `_current` to the specified value.

**SetInterval**  
Sets `_interval` to the specified value.

**GetInterval**  
Returns `_interval`.

**GetCurrent**  
Returns `_current`.

---

<!-- machine-true, projected from graph.json -->

## Map — IntervalTimer

*Source:* Timer.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| IntervalTimer | ctor | — | — | — |
| Update | method | — | MapManager/Update, World/Update | — |
| Passed | method | — | MapManager/Update, World/Update | — |
| Reset | method | — | World/LoadConfigSettings, World/Update | — |
| SetCurrent | method | — | MapManager/Update, World/SetWorldUpdateTimer, World/Update | — |
| SetInterval | method | — | MapManager/MapManager, World/LoadConfigSettings, World/SetInitialWorldSettings, World/Update | — |
| GetInterval | method | — | World/GetWorldUpdateTimerInterval | — |
| GetCurrent | method | — | MapManager/Update, World/GetWorldUpdateTimer, World/Update | — |
