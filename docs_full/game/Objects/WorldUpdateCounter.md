# WorldUpdateCounter

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# WorldUpdateCounter

**Purpose & Responsibilities**

`WorldUpdateCounter` is a lightweight utility class designed to measure the elapsed time between periodic "world update" ticks in the game server loop. It is specifically intended to track how much time has passed since the last update cycle for a `WorldObject`, allowing the object to process time-dependent logic (such as spell ticks, movement interpolation, or cooldowns) accurately regardless of server load variations.

The class operates by storing a start timestamp (`m_tmStart`). When queried, it calculates the difference between this stored start time and the current server tick time. It supports two modes of operation:
1.  **Lazy Initialization:** If no start time has been set, the first call to `timeElapsed()` automatically initializes the start time to the previous server tick.
2.  **Explicit Control:** The caller can manually reset the counter to the current tick or to a specific historical timestamp.

This class is embedded within `WorldObject` as the member `m_updateTracker`. It is primarily manipulated by the `WorldObject::UpdateHelper` RAII wrapper, which ensures that the elapsed time is calculated and the counter is reset immediately after the `WorldObject::Update()` method completes.

**Member-by-Member Behavior**

The class consists of a constructor, two overloads for calculating elapsed time, and two methods for resetting the internal timer.

*   **Initialization:** The constructor initializes the internal start time to zero, indicating that no measurement period has begun.
*   **Measurement:** The `timeElapsed` methods compute the millisecond difference between the stored start time and a reference time (either the current server tick or a provided timestamp).
*   **Resetting:** The `Reset` and `ResetTo` methods allow the caller to restart the measurement period, either from the current moment or from a specific past timestamp.

**Cross-Unit Boundaries**

`WorldUpdateCounter` has no direct dependencies on other classes in the `wowvmangos` codebase other than the global `WorldTimer` utility. However, it is tightly coupled with `WorldObject` via composition.

*   **Called by `WorldObject::UpdateHelper` (in `Object.h`):**
    *   `WorldObject::UpdateHelper::Update()` calls `m_obj->m_updateTracker.timeElapsed()` to retrieve the time delta since the last update. It then passes this delta to `WorldObject::Update()` and subsequently calls `m_obj->m_updateTracker.Reset()` to prepare for the next cycle.
    *   `WorldObject::UpdateHelper::UpdateRealTime()` calls `m_obj->m_updateTracker.timeElapsed(now)` with a specific timestamp `now`, retrieves the delta, passes it to `WorldObject::Update()`, and then calls `m_obj->m_updateTracker.ResetTo(now)` to sync the tracker with the provided timestamp.

*   **Calls into `WorldTimer` (Global Utility):**
    *   `timeElapsed()` calls `WorldTimer::tickPrevTime()` to initialize `m_tmStart` if it is zero.
    *   `timeElapsed()` calls `WorldTimer::tickTime()` to get the current time for the difference calculation.
    *   `timeElapsed(uint32 now)` calls `WorldTimer::getMSTimeDiff()` to calculate the difference.
    *   `Reset()` calls `WorldTimer::tickTime()` to set the new start time.

**Data Model**

This unit does not interact with any database tables. It operates entirely in memory using volatile timestamps.

**Notable Implementation Details**

1.  **Lazy Initialization in `timeElapsed()`:**
    The non-const overload `timeElapsed()` checks if `m_tmStart` is zero. If it is, it sets `m_tmStart` to `WorldTimer::tickPrevTime()`. This means the first call to `timeElapsed()` effectively starts the timer at the *previous* server tick, not the current one. This design choice likely ensures that the first update cycle accounts for the time elapsed since the object was created or last processed, assuming the creation happened in the previous tick. Subsequent calls do not re-initialize; they simply calculate the difference from the original start time until `Reset()` or `ResetTo()` is called.

2.  **Const Correctness and Side Effects:**
    *   `timeElapsed()` is **non-const** because it modifies `m_tmStart` during lazy initialization.
    *   `timeElapsed(uint32 now)` is **const** because it does not modify any member variables. It returns `0` if `m_tmStart` is zero, rather than initializing it. This distinction is critical: callers must ensure the counter has been started (via the non-const overload or a reset) before using the const overload, or they will receive a zero delta.

3.  **Timestamp Source:**
    The class relies on `WorldTimer::tickPrevTime()` for initialization and `WorldTimer::tickTime()` for current time. This implies that `WorldUpdateCounter` is synchronized with the server's main game loop tick system. Using `tickPrevTime()` for initialization suggests that the "start" of a measurement period is considered to be the beginning of the previous tick, ensuring that the first measured interval includes the time spent in the current tick's processing up to the point of the first query.

4.  **Usage Pattern:**
    The class is designed for a "measure-reset-measure" pattern. After calling `timeElapsed()`, the caller is expected to call `Reset()` or `ResetTo()` to clear the start time for the next interval. Failure to reset will cause subsequent `timeElapsed()` calls to return increasingly larger values, representing the total time since the initial start, rather than the delta for the current tick.

## Member Reference

**WorldUpdateCounter**
Constructor. Initializes the private member `m_tmStart` to `0`, indicating that no time measurement has been started.

**timeElapsed**
Non-const method. Calculates the elapsed time in milliseconds since `m_tmStart`. If `m_tmStart` is `0`, it initializes `m_tmStart` to `WorldTimer::tickPrevTime()` before calculating the difference between `m_tmStart` and `WorldTimer::tickTime()`. Returns the result of `WorldTimer::getMSTimeDiff()`.

**timeElapsed#2**
Const method overload. Takes a `uint32 now` parameter representing a specific timestamp. If `m_tmStart` is `0`, it returns `0` immediately. Otherwise, it returns the difference between `m_tmStart` and `now` using `WorldTimer::getMSTimeDiff()`. Does not modify any state.

**Reset**
Resets the timer by setting `m_tmStart` to the current server tick time obtained via `WorldTimer::tickTime()`. This prepares the counter for measuring the next interval.

**ResetTo**
Resets the timer by setting `m_tmStart` to the provided `lastUpdate` timestamp. This allows synchronization with a specific historical time point, often used when processing updates with known timestamps.

---

<!-- machine-true, projected from graph.json -->

## Map — WorldUpdateCounter

*Source:* Object.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| WorldUpdateCounter | ctor | — | — | — |
| timeElapsed | method | — | — | — |
| timeElapsed#2 | method | — | — | — |
| Reset | method | — | — | — |
| ResetTo | method | — | — | — |
