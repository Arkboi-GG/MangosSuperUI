# ScopedTimerPeriod

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ScopedTimerPeriod

`ScopedTimerPeriod` is a lightweight RAII utility that guarantees the restoration of a timer period upon scope exit. It does not manage timer state directly; instead, it holds a callback (`std::function<void()>`) provided by its creator to perform the restoration. This ensures that even if exceptions occur or control flow exits unexpectedly, the original timer configuration is restored.

## Purpose & Responsibilities

The class serves as a transactional guard for temporary timer period changes:
1.  **Capture Status**: Records whether the initial timer change succeeded.
2.  **Guarantee Restoration**: Invokes a user-provided callback in its destructor to revert the timer state.

## Member-by-Member Behavior

### Construction and Destruction

**`ScopedTimerPeriod` (Constructor)**
Initializes the object with a `success` boolean and a `Callback`. The callback is moved into the internal member `cb_` to avoid copying. No timer manipulation occurs here; the object merely prepares for scope exit.

**`~ScopedTimerPeriod` (Destructor)**
Unconditionally invokes `cb_()`. This ensures the timer period is restored regardless of how the scope is exited (normal return, exception, etc.). The caller must ensure the callback is valid.

### State Inspection

**`success`**
Returns the `success_` boolean passed during construction, allowing the caller to verify if the temporary timer change was applied.

## Cross-Unit Boundaries

*   **Called by `TimePeriod/set_time_period`**: The function `set_time_period` (declared in `TimePeriod.h`) constructs and returns a `ScopedTimerPeriod`. It is responsible for capturing the current timer period, applying the new one, and creating the restoration callback passed to this class.
*   **No Outgoing Calls**: `ScopedTimerPeriod` does not call into other units. Its interaction with external state is limited to executing the `cb_` callback.

## Data Model

This unit does not interact with any database tables. It operates entirely in memory.

## Notable Implementation Details

1.  **Unconditional Restoration**: The destructor always calls `cb_()`. There is no check for empty callbacks or failure states. The caller (`set_time_period`) must provide a valid callback even if the initial set failed.
2.  **Final Class**: Marked `final` to prevent inheritance, enforcing its role as a simple utility.
3.  **Thread Safety**: The class is not thread-safe. It is intended for use as a local stack variable within a single thread.

## Member Reference

**ScopedTimerPeriod**
Constructor that initializes the internal success flag and moves the provided restoration callback into the object.

**success**
Returns the boolean flag indicating whether the temporary timer period change was successfully applied.

**~ScopedTimerPeriod**
Destructor that invokes the stored restoration callback, ensuring the timer period is reverted when the object goes out of scope.

---

<!-- machine-true, projected from graph.json -->

## Map — ScopedTimerPeriod

*Source:* TimePeriod.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ScopedTimerPeriod | ctor | — | TimePeriod/set_time_period | — |
| success | method | — | — | — |
| ~ScopedTimerPeriod | dtor | — | — | — |
