# TimeTracker

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

The `TimeTracker` struct, defined in `Timer.h`, is a lightweight, countdown-based timer utility designed to track whether a specific duration has elapsed since its initialization or last reset. Unlike the `IntervalTimer` and `ShortIntervalTimer` classes in the same header—which accumulate time to check if an interval has passed—`TimeTracker` starts with a positive expiry value and decrements it on each update. It signals completion when the remaining time drops to zero or below.

This unit is primarily used within the movement generation subsystems of the game engine, specifically by `FearMovementGenerator` and `FleeingMovementGenerator` (as indicated by the MAP), to manage the duration of fear or flee effects. It ensures that these temporary states expire correctly after a set period, allowing entities to return to normal behavior.

## Member-by-Member Behavior

### Construction and Initialization
**`TimeTracker`**
The constructor initializes the internal expiry counter (`i_expiryTime`) with the provided `expiry` value. This value represents the total duration (in seconds, given the `time_t` type) that the timer should run before expiring. The constructor is marked `explicit` to prevent implicit conversions from integer types.

### Time Progression and Expiry Checks
**`Update`**
This method advances the timer by subtracting the provided time difference (`diff`) from the remaining expiry time. It is called periodically by the movement generators (`FearMovementGenerator/Update`, `FleeingMovementGenerator/Update`) to reflect the passage of game time. If `diff` exceeds the remaining time, the expiry value becomes negative, indicating the timer has expired.

**`Passed`**
A const method that returns `true` if the timer has expired (i.e., `i_expiryTime <= 0`). This is the primary query used by callers like `FearMovementGenerator` and `GridStates` to determine if the associated effect (fear, flee, or grid state change) should end or transition.

### State Management
**`Reset`**
Resets the timer to a new duration specified by `interval`. This effectively restarts the countdown. It is called by `_setTargetLocation` methods in `FearMovementGenerator` and `FleeingMovementGenerator`, likely when the target of the fear/flee changes, requiring the duration to be recalculated or restarted.

**`GetExpiry`**
Returns the current remaining time until expiry. While listed in the MAP, the "Called by" column is empty, suggesting this method might be reserved for debugging, logging, or future use, as the primary logic relies on `Passed()` for boolean checks.

## Cross-Unit Boundaries

`TimeTracker` itself has no outgoing calls; it is a pure data-and-beholder struct. Its interactions are entirely inbound, driven by movement generators and grid management systems:

*   **FearMovementGenerator / FleeingMovementGenerator**: These units construct `TimeTracker` instances to limit the duration of fear/flee behaviors. They call `Update` during their update loops to decrement the timer, `Passed` to check if the effect should end, and `Reset` when the target location changes, potentially restarting the timer.
*   **GridStates**: This unit uses `TimeTracker` (via `Passed`) to manage time-based state transitions in the game world's grid system, ensuring that temporary grid states do not persist indefinitely.

## Data Model

`TimeTracker` does not interact with any database tables. It operates entirely in memory using primitive types (`time_t`).

## Notable Implementation Details

*   **Countdown vs. Accumulation**: `TimeTracker` uses a countdown approach (`expiry -= diff`), whereas `IntervalTimer` uses accumulation (`current += diff`). This makes `TimeTracker` suitable for one-off durations (like a spell effect) while `IntervalTimer` is better for repeating actions.
*   **Negative Expiry**: The `Passed` method returns `true` if `i_expiryTime <= 0`. This means the timer can go significantly negative if updates are missed or if a large `diff` is passed. Callers should be aware that `Passed` does not indicate *how long* ago it expired, only *that* it has expired.
*   **Type Consistency**: It uses `time_t` for both the expiry value and the update difference, ensuring consistency with standard C++ time functions. However, `time_t` resolution is typically seconds, which may be coarse for high-frequency game logic. The presence of `ShortTimeTracker` (using `int32`) suggests that higher-resolution timers are needed elsewhere, but `TimeTracker` is strictly second-based.
*   **No Thread Safety**: Like all units in `Timer.h`, `TimeTracker` is not thread-safe. It assumes single-threaded access or external synchronization by the caller.

## Member Reference

**TimeTracker**  
Constructor that initializes the internal expiry counter with the provided duration. Used by `FearMovementGenerator` and `TimedFearMovementGenerator` to start tracking the duration of fear effects.

**Update**  
Decrements the remaining expiry time by the provided time difference. Called by `FearMovementGenerator/Update`, `FearMovementGenerator/Update#2`, `FleeingMovementGenerator/Update`, and `FleeingMovementGenerator/Update#2` to advance the timer during game ticks.

**Passed**  
Returns `true` if the remaining expiry time is less than or equal to zero, indicating the timer has expired. Called by `FearMovementGenerator/Update`, `FearMovementGenerator/Update#2`, `FleeingMovementGenerator/Update`, `FleeingMovementGenerator/Update#2`, `GridStates/Update`, and `GridStates/Update#4` to determine if an effect or state should end.

**Reset**  
Sets the remaining expiry time to a new interval, effectively restarting the countdown. Called by `FearMovementGenerator/_setTargetLocation` and `FleeingMovementGenerator/_setTargetLocation` when the target of the movement changes.

**GetExpiry**  
Returns the current remaining time until expiry. Currently not called by any other unit in the provided MAP, possibly reserved for debugging or future use.

---

<!-- machine-true, projected from graph.json -->

## Map — TimeTracker

*Source:* Timer.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| TimeTracker | ctor | — | FearMovementGenerator/TimedFearMovementGenerator | — |
| Update | method | — | FearMovementGenerator/Update, FearMovementGenerator/Update#2, FleeingMovementGenerator/Update, FleeingMovementGenerator/Update#2 | — |
| Passed | method | — | FearMovementGenerator/Update, FearMovementGenerator/Update#2, FleeingMovementGenerator/Update, FleeingMovementGenerator/Update#2, GridStates/Update, GridStates/Update#4 | — |
| Reset | method | — | FearMovementGenerator/_setTargetLocation, FleeingMovementGenerator/_setTargetLocation | — |
| GetExpiry | method | — | — | — |
