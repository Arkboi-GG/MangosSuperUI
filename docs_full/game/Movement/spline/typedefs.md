<!-- provenance: verbose -->
# typedefs

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# typedefs.h

## Purpose & Responsibilities

`typedefs.h` provides low-level utilities within the `Movement` namespace. It defines inline time conversion functions (`SecToMS`, `MSToSec`) and a templated `counter` class for generating sequential, bounded IDs. It also aliases `G3D` vector types and defines a `UInt32Counter` typedef. This unit contains no database interactions.

## Member-by-Member Behavior

### Time Conversion

*   **`SecToMS`**: Converts seconds (`float`) to milliseconds (`uint32`) by multiplying by `1000.0f` and truncating via `static_cast`.
*   **`MSToSec`**: Converts milliseconds (`uint32`) to seconds (`float`) by dividing by `1000.0f`.

### Counter ID Generator

The `counter<T, limit>` template manages a sequential counter that wraps to zero upon reaching `limit`.

*   **`counter()`**: Constructor; initializes the counter to `0` via `init()`.
*   **`Increase`**: Increments `m_counter`. If `m_counter` equals `limit`, it resets to `0`; otherwise, it increments by `1`.
*   **`NewId`**: Calls `Increase()` and returns the resulting `m_counter`. The first call returns `1`.
*   **`getCurrent`**: Returns the current `m_counter` value without modification.
*   **`init`**: Private helper that sets `m_counter` to `0`.

### Type Aliases

*   **`UInt32Counter`**: Typedef for `counter<uint32, 0xFFFFFFFF>`, generating 32-bit IDs that wrap after reaching the maximum `uint32` value.

## Cross-Unit Boundaries

*   **Called by `MoveSpline/computeDuration`**: Uses `SecToMS` to convert calculated movement durations from seconds to milliseconds.
*   **Called by `MoveSpline/computeFallElevation`**: Uses `MSToSec` to convert millisecond-based time values back to seconds for physics calculations.

No other units call into `typedefs.h`, and it does not call out to other units.

## Data Model

This unit does not interact with any database tables.

## Notable Implementation Details

1.  **Precision Loss**: `SecToMS` truncates fractional milliseconds due to the `static_cast<uint32>`.
2.  **Wrap-Around Sequence**: `NewId` returns `1` on the first call. The sequence is `1, 2, ..., limit, 0, 1...`. Zero is only produced immediately after hitting `limit`.
3.  **Thread Safety**: `counter` is not thread-safe; concurrent access requires external synchronization.
4.  **Macros**: Defines `CONCAT` and `CONCAT1` macros if not already present, though they are unused within this file.

## Member Reference

**SecToMS**
Inline function converting seconds (`float`) to milliseconds (`uint32`). Called by `MoveSpline/computeDuration`.

**MSToSec**
Inline function converting milliseconds (`uint32`) to seconds (`float`). Called by `MoveSpline/computeFallElevation`.

**counter<T, limit>**
Constructor for the templated counter class; initializes internal state to zero.

**Increase**
Increments the counter, resetting to zero if it reaches the template-defined `limit`.

**NewId**
Calls `Increase` and returns the new counter value.

**getCurrent**
Returns the current counter value without modification.

**init**
Private helper that resets the internal counter to zero.

---

<!-- machine-true, projected from graph.json -->

## Map — typedefs

*Source:* typedefs.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| SecToMS | function | — | MoveSpline/computeDuration | — |
| MSToSec | function | — | MoveSpline/computeFallElevation | — |
| counter<T, limit> | ctor | — | — | — |
| Increase | function | — | — | — |
| NewId | function | — | — | — |
| getCurrent | function | — | — | — |
| init | function | — | — | — |
