<!-- provenance: verbose -->
# ShortIntervalTimer

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`ShortIntervalTimer` is a lightweight, in-memory utility class in `Timer.h` for tracking elapsed time in milliseconds (`uint32`). It accumulates time deltas via `Update` and reports whether a configured threshold (`_interval`) has been reached via `Passed`. Unlike the sibling `IntervalTimer` (which uses `time_t` and clamps negatives), `ShortIntervalTimer` assumes non-negative inputs and provides a "rolling" reset mechanism to maintain periodic accuracy without drift. It is used by `GridMap`, `MirrorTimer`, and `Weather` subsystems for sub-second periodic tasks.

## Member-by-Member Behavior

### Initialization
**`ShortIntervalTimer`**
Constructs the timer with `_interval` and `_current` initialized to 0. The timer is inactive until `SetInterval` is called. Note that with default values, `Passed()` returns `true` (0 >= 0), so callers must configure the interval before use.

### Time Accumulation & State
**`Update`**
Adds the `diff` (milliseconds) to `_current`. No clamping or overflow protection is performed; callers must ensure `diff` is non-negative.

**`Passed`**
Returns `true` if `_current >= _interval`. This is the primary check for determining if a periodic action should execute.

**`Reset`**
If `Passed()` is true, subtracts `_interval` from `_current`. This "rolls over" the timer, preserving excess time to prevent drift in periodic loops. If the timer has not passed, it does nothing.

**`GetCurrent`**
Returns the raw accumulated `_current` value.

**`GetInterval`**
Returns the configured `_interval` value.

### Configuration
**`SetCurrent`**
Directly sets `_current`. Used to synchronize state or manually adjust elapsed time.

**`SetInterval`**
Sets the target `_interval` in milliseconds.

## Cross-Unit Boundaries

`ShortIntervalTimer` is passive; all interactions are inbound calls from other units:

*   **`GridMap`**: `CleanUpGrids` uses `Update`, `Passed`, and `Reset` for periodic grid cleanup. `TerrainInfo` uses `SetCurrent` and `SetInterval` for configuration.
*   **`MirrorTimer`**: Uses all methods (`Update`, `Passed`, `Reset`, `SetCurrent`, `SetInterval`, `GetInterval`, `GetCurrent`) for internal synchronization logic requiring millisecond precision.
*   **`Weather`**: Uses `SetInterval` and `GetInterval` during construction, and `Update`, `Passed`, and `Reset` in its update loop.

## Data Model

This unit interacts with no database tables. State is held entirely in memory.

## Notable Implementation Details

1.  **No Negative Clamping**: `Update` blindly adds `diff`. If a negative `diff` is passed, `_current` decreases, potentially causing incorrect `Passed` results. Callers must guarantee non-negative inputs.
2.  **Rolling Reset**: `Reset` subtracts the interval rather than zeroing `_current`. This preserves fractional periods (e.g., if 1005ms pass for a 1000ms interval, 5ms remains), ensuring long-term periodic accuracy.
3.  **Default State**: With `_interval=0` and `_current=0`, `Passed()` is initially `true`. Callers must call `SetInterval` with a positive value before relying on `Passed()` for control flow.
4.  **Overflow Limit**: Using `uint32` limits the max interval to ~49.7 days. For longer durations, `IntervalTimer` or `WorldTimer` should be used.

## Member Reference

**`ShortIntervalTimer`**
Constructor initializing `_interval` and `_current` to 0.

**`Update`**
Adds `diff` to `_current`. Called by `GridMap/CleanUpGrids`, `MirrorTimer/Update`, `Weather/Update`.

**`Passed`**
Returns `true` if `_current >= _interval`. Called by `GridMap/CleanUpGrids`, `MirrorTimer/Update`, `Weather/Update`.

**`Reset`**
Subtracts `_interval` from `_current` if passed. Called by `GridMap/CleanUpGrids`, `MirrorTimer/Update`, `Weather/Update`.

**`SetCurrent`**
Sets `_current`. Called by `GridMap/TerrainInfo`, `MirrorTimer/SetRemaining`, `MirrorTimer/Start`, `MirrorTimer/Start#2`, `MirrorTimer/Stop`, `MirrorTimer/Update`.

**`SetInterval`**
Sets `_interval`. Called by `GridMap/TerrainInfo`, `MirrorTimer/SetDuration`, `MirrorTimer/Start`, `MirrorTimer/Start#2`, `Weather/Weather`.

**`GetInterval`**
Returns `_interval`. Called by `MirrorTimer/Update`, `Weather/Weather`.

**`GetCurrent`**
Returns `_current`. Called by `MirrorTimer/Update`.

---

<!-- machine-true, projected from graph.json -->

## Map — ShortIntervalTimer

*Source:* Timer.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ShortIntervalTimer | ctor | — | — | — |
| Update | method | — | GridMap/CleanUpGrids, MirrorTimer/Update, Weather/Update | — |
| Passed | method | — | GridMap/CleanUpGrids, MirrorTimer/Update, Weather/Update | — |
| Reset | method | — | GridMap/CleanUpGrids, MirrorTimer/Update, Weather/Update | — |
| SetCurrent | method | — | GridMap/TerrainInfo, MirrorTimer/SetRemaining, MirrorTimer/Start, MirrorTimer/Start#2, MirrorTimer/Stop, MirrorTimer/Update | — |
| SetInterval | method | — | GridMap/TerrainInfo, MirrorTimer/SetDuration, MirrorTimer/Start, Weather/Weather | — |
| GetInterval | method | — | MirrorTimer/Update, Weather/Weather | — |
| GetCurrent | method | — | MirrorTimer/Update | — |
