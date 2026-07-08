# Timer

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Timer

## Purpose & Responsibilities

The `Timer` unit provides fundamental time-tracking utilities for the wowvmangos server. It defines a single free function, `GetApplicationStartTime`, which captures the moment the server process began execution using a monotonic clock. This timestamp serves as the absolute reference point for calculating uptime or elapsed time since launch.

The unit also declares several classes (`WorldTimer`, `IntervalTimer`, `ShortIntervalTimer`, `TimeTracker`, `ShortTimeTracker`) that manage game-time ticks and interval-based timers. However, **only** `GetApplicationStartTime` is implemented within this translation unit. The methods for `WorldTimer` and the member functions for the interval/tracker classes are declared here but implemented elsewhere (not included in this unit's source). Consequently, this documentation focuses exclusively on the behavior of `GetApplicationStartTime`.

## Member-by-Member Behavior

### GetApplicationStartTime

**Purpose:** Returns the steady clock time point representing the instant the application started.

**Behavior:**
1.  Uses `std::chrono::steady_clock`, which is a monotonic clock unaffected by system clock updates (e.g., NTP adjustments). This ensures that elapsed time calculations remain consistent even if the server's wall-clock time changes.
2.  Employs a `static const` local variable `ApplicationStartTime` to capture `steady_clock::now()` exactly once, during the first invocation of this function.
3.  Subsequent calls return the cached `ApplicationStartTime` value, ensuring that all callers receive the identical reference point regardless of when they query it.

This design guarantees that "application start time" is a fixed constant derived from the first access, rather than a moving target or a value set at global initialization (which might occur before the main loop is ready, though `steady_clock` is generally safe for early use).

## Cross-Unit Boundaries

*   **Called by:** `shared_Util/getMSTime`
    *   **Direction:** Outbound call from `shared_Util` into this unit.
    *   **Collaboration:** The utility module `shared_Util` requires the application's start time to calculate the current server time in milliseconds. By calling `GetApplicationStartTime`, it obtains the baseline timestamp needed to compute the delta between the start of the process and the current moment.

## Data Model

This unit does not interact with any database tables. It operates entirely in memory using C++ standard library chrono facilities.

## Notable Implementation Details

*   **Monotonic Clock Usage:** The choice of `std::chrono::steady_clock` is critical for server stability. Using `system_clock` would risk incorrect time deltas if the OS clock jumps backward or forward. `steady_clock` guarantees that time always moves forward, making it suitable for measuring intervals and uptime.
*   **Lazy Initialization:** The start time is captured on first use, not at static initialization time. While `steady_clock::now()` is generally safe to call early, this pattern ensures the value is tied to the first request for timing data, which typically occurs during the server's active lifecycle.
*   **Const Correctness:** The cached time point is `const`, preventing accidental modification after initialization.

## Member Reference

**GetApplicationStartTime**: Returns a `std::chrono::steady_clock::time_point` representing the time the application started. It caches this value in a static local variable upon the first call, ensuring all subsequent calls return the identical timestamp. This function is called by `shared_Util/getMSTime` to establish the baseline for server time calculations.

---

<!-- machine-true, projected from graph.json -->

## Map — Timer

*Source:* Timer.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GetApplicationStartTime | function | — | shared_Util/getMSTime | — |
