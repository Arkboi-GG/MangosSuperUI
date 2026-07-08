<!-- provenance: verbose -->
# ShortTimeTracker

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ShortTimeTracker

`ShortTimeTracker` is a lightweight, value-type struct in `Timer.h` implementing a countdown timer using signed 32-bit integers (`int32`). It tracks short-duration intervals by maintaining a remaining time value that decrements with each update. Unlike accumulating timers, it counts down; when the value drops to zero or below, the timer is considered "passed." It contains no database interactions and makes no cross-unit calls; it is embedded directly into other classes to manage local timing logic.

## Purpose & Responsibilities

1.  **Initialize** a timer with a specific duration.
2.  **Advance** the timer by subtracting elapsed time (`diff`).
3.  **Check** expiration via `Passed()`.
4.  **Reset** the timer to a new duration.

The use of `int32` permits negative values, simplifying expiration logic. A default-constructed tracker starts with `0` remaining time, meaning it is immediately "passed" until `Reset()` is called.

## Member-by-Member Behavior

### Construction

**`ShortTimeTracker`**
Initializes `i_expiryTime` to the provided `expiry` (default `0`). If omitted, the timer starts expired.

### State Management

**`Update`**
Subtracts `diff` from `i_expiryTime`. If `diff` exceeds the remaining time, the value becomes negative. No clamping occurs; negative values correctly indicate expiration via `Passed()`.

**`Reset`**
Overwrites `i_expiryTime` with the provided `interval`, restarting the countdown.

### Querying State

**`Passed`**
Returns `true` if `i_expiryTime <= 0`.

**`GetExpiry`**
Returns the current `i_expiryTime`, which may be negative if the timer has expired.

## Cross-Unit Boundaries

`ShortTimeTracker` is passive; it does not call out. It is instantiated and manipulated by numerous units to manage game-loop timing:

*   **AI Controllers** (`AiBotAI`, `BattleBotAI`, `PartyBotAI`): Manage decision cycles and cooldowns. They call `Update` in their main loops, `Passed` to trigger actions, and `Reset` to restart timers.
*   **Movement Generators** (`FearMovementGenerator`, `RandomMovementGenerator`, `TargetedMovementGenerator`, `WaypointMovementGenerator`): Control behavior durations (e.g., fear duration, random movement intervals). `Update` is called frequently; `Reset` handles state transitions.
*   **Spatial Indexing** (`BoundsTrait.DynamicTree`): Throttles expensive spatial recalculations.
*   **Transports** (`Transport`): Manages update intervals for moving vehicles.
*   **Chat Handlers** (`ChatHandler.PlayerBotMgr`): Enforces delays on commands like pausing bots or stopping attacks.

The pattern is consistent: the owner holds a `ShortTimeTracker`, passes delta time to `Update`, checks `Passed`, and calls `Reset` to schedule future events.

## Data Model

`ShortTimeTracker` does not interact with any database tables. It operates entirely in memory.

## Notable Implementation Details

1.  **Signed Arithmetic:** Using `int32` avoids the underflow wrapping issues of unsigned types. Negative values are valid and indicate expiration.
2.  **No Clamping:** `Update` does not clamp to zero. Large `diff` values result in negative `i_expiryTime`. Callers relying on `GetExpiry()` must handle negatives.
3.  **Default State:** A default-constructed tracker is immediately expired (`0`). Callers must call `Reset()` to start a countdown.
4.  **Inline Methods:** All methods are inline, ensuring zero overhead for high-frequency game-loop calls.

## Member Reference

**`ShortTimeTracker`**
Constructor initializing `i_expiryTime` to `expiry` (default `0`). Starts expired if no argument is provided.

**`Update`**
Decrements `i_expiryTime` by `diff`. Allows negative values if `diff` exceeds remaining time.

**`Passed`**
Returns `true` if `i_expiryTime <= 0`.

**`Reset`**
Sets `i_expiryTime` to `interval`, restarting the countdown.

**`GetExpiry`**
Returns current `i_expiryTime`, which may be negative if expired.

---

<!-- machine-true, projected from graph.json -->

## Map — ShortTimeTracker

*Source:* Timer.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ShortTimeTracker | ctor | — | BoundsTrait.DynamicTree/DynTreeImpl | — |
| Update | method | — | AiBotAI.Main/UpdateAI, BattleBotAI.Main/UpdateAI, BoundsTrait.DynamicTree/update, FearMovementGenerator/Update#2, PartyBotAI/UpdateAI, RandomMovementGenerator/UpdateAsync, TargetedMovementGenerator/Update, Transport/Update#2 | — |
| Passed | method | — | AiBotAI.Main/UpdateAI, BattleBotAI.Main/UpdateAI, BoundsTrait.DynamicTree/update, FearMovementGenerator/_getPoint, PartyBotAI/UpdateAI, RandomMovementGenerator/UpdateAsync, TargetedMovementGenerator/Update, Transport/Update#2 | — |
| Reset | method | — | AiBotAI.Main/UpdateAI, BattleBotAI.Main/UpdateAI, BoundsTrait.DynamicTree/update, ChatHandler.PlayerBotMgr/HandlePartyBotPauseApplyHelper, ChatHandler.PlayerBotMgr/StopPartyBotAttackHelper, FearMovementGenerator/TimedFearMovementGenerator, FearMovementGenerator/Update#2, PartyBotAI/UpdateAI, RandomMovementGenerator/Initialize, RandomMovementGenerator/UpdateAsync, RandomMovementGenerator/_setRandomLocation, TargetedMovementGenerator/Update, Transport/Update#2, WaypointMovementGenerator/InitializeWaypointPath, WaypointMovementGenerator/SetNextWaypoint, WaypointMovementGenerator/Update#3 | — |
| GetExpiry | method | — | ChatHandler.PlayerBotMgr/StopPartyBotAttackHelper, FearMovementGenerator/Update#2 | — |
