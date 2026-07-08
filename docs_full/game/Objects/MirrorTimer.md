<!-- provenance: verbose -->
# MirrorTimer

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# MirrorTimer

`MirrorTimer` manages client-visible environmental timers (fatigue, breath, feign death) by tracking duration, scaling, and state changes. It distinguishes between **countdown** (negative `m_scale`, e.g., breath holding) and **regeneration** (positive `m_scale`, e.g., fatigue recovery). A scale of zero pauses the timer. The class uses two `ShortIntervalTimer` instances: `m_tracker` for the primary duration and `m_pulse` for post-expiration animation ticks in countdown mode. It reports status changes (`FULL_UPDATE`, `STATUS_UPDATE`, `UNCHANGED`) to `Player.Main` to optimize network traffic.

## Purpose & Responsibilities

1.  **State Management**: Tracks active/frozen states and timer direction via `m_scale`.
2.  **Time Scaling**: Applies `m_scale` to time deltas in `Update`. Negative scales count down; positive scales regenerate.
3.  **Pulse Logic**: For countdown timers, triggers `m_pulse` (2-second intervals) after expiration to allow smooth client animation of the final moments.
4.  **Change Notification**: Reports status changes to `Player` to minimize unnecessary network packets.

## Member-by-Member Behavior

### Lifecycle and Control

*   **`MirrorTimer` (ctor)**: Initializes with a `Type`, sets `m_scale` to -1 (countdown default), and marks inactive/unfrozen.
*   **`Start(uint32 interval, uint32 spellId)`**: Activates countdown mode. Sets `m_pulse` to 2s intervals and `m_tracker` to `interval`. Requires `m_scale < 0`; otherwise calls `Stop()`. Marks `FULL_UPDATE`.
*   **`Start#2` (`Start(uint32 current, uint32 max, uint32 spellId)`)**: Activates with specific initial remaining time. Calls the first `Start` overload, then sets `m_tracker` current to `max - current` and unfreezes. Marks `FULL_UPDATE`.
*   **`Stop`**: Deactivates timer, resets trackers to 0, marks `STATUS_UPDATE`.
*   **`SetDuration`**: Updates total duration via `m_tracker.SetInterval`. Stops if duration is 0. Marks `FULL_UPDATE` if active and changed.
*   **`SetRemaining`**: Sets remaining time by adjusting `m_tracker` current. Stops if duration is 0. Marks `FULL_UPDATE` if active and changed.
*   **`SetScale`**: Sets direction/speed. Zero scale calls `SetFrozen(true)`. Marks `FULL_UPDATE` if active and changed.
*   **`SetFrozen`**: Pauses timer without stopping. Marks `STATUS_UPDATE` if active and changed.

### State Querying

*   **`IsActive`**, **`IsRegenerating`**, **`IsFrozen`**: Return boolean state flags. `IsFrozen` is false if regenerating.
*   **`GetType`**, **`GetRemaining`**, **`GetDuration`**, **`GetScale`**, **`GetSpellId`**: Expose internal state. `GetRemaining` calculates `interval - current`.
*   **`FetchStatus`**: Returns current status and resets it to `UNCHANGED`.

### Core Update Logic

*   **`Update(uint32 diff)`**: Advances time by `diff * abs(m_scale)`.
    *   **Countdown (`m_scale < 0`)**: Updates `m_tracker`. If passed, calculates overflow. If overflow equals `diff` (just expired), starts `m_pulse`. If `m_pulse` passes, returns `false` (fully expired). Otherwise returns `true`.
    *   **Regeneration (`m_scale > 0`)**: Subtracts scaled `diff` from `m_tracker` current. If current <= diff, calls `Stop()`. Returns `true`.

## Cross-Unit Boundaries

*   **`Player.Main`**:
    *   **`SendMirrorTimers`**: Reads state via `FetchStatus`, `IsActive`, `IsFrozen`, `GetType`, `GetRemaining`, `GetDuration`, `GetScale`, `GetSpellId`.
    *   **`UpdateMirrorTimers`**: Drives `Update`, `Stop`, `Start`, `SetRemaining`, `SetDuration`, `IsActive`, `GetType`, `GetSpellId`.
    *   **`FreezeMirrorTimers`**: Calls `SetFrozen`, `GetSpellId`.
    *   **`SetEnvironmentFlags`** / **`SetWaterBreathingIntervalMultiplier`**: Call `SetScale`, `SetDuration`.
    *   **`GetMirrorTimerMaxDuration`**: Calls `GetDuration`.
*   **`ShortIntervalTimer`**: `MirrorTimer` delegates low-level time tracking to `m_tracker` and `m_pulse` via `SetCurrent`, `SetInterval`, `GetCurrent`, `GetInterval`, `Passed`, `Reset`, `Update`.

## Data Model

No database tables are accessed.

## Notable Implementation Details

1.  **Pulse Trigger Condition**: In `Update`, the pulse only starts if `overflow == diff`. This ensures the pulse begins exactly when the timer crosses the threshold, preventing premature ticks.
2.  **Regeneration Freeze**: `IsFrozen` returns false if regenerating, even if `m_frozen` is true. Regeneration timers are not "frozen" in the same way; they simply stop progressing if scale is 0.
3.  **Status Reset**: `FetchStatus` resets `m_status` to `UNCHANGED`. Missed network updates are acceptable for this UI element.

## Member Reference

**FetchStatus**: Returns current status and resets to `UNCHANGED`. Called by `Player.Main/SendMirrorTimers`.

**Stop**: Deactivates timer, resets trackers, marks `STATUS_UPDATE`. Called by `Player.Main/UpdateMirrorTimers`.

**Start**: Activates countdown mode. Sets `m_pulse` to 2s, `m_tracker` to interval. Requires `m_scale < 0`. Marks `FULL_UPDATE`. Called by `Player.Main/UpdateMirrorTimers`.

**MirrorTimer**: Constructor initializes type, scale (-1), and inactive state.

**IsActive**: Returns `m_active`. Called by `Player.Main/SendMirrorTimers`, `Player.Main/UpdateMirrorTimers`.

**IsRegenerating**: Returns `m_scale > 0`.

**IsFrozen**: Returns `m_frozen && !IsRegenerating()`. Called by `Player.Main/SendMirrorTimers`.

**GetType**: Returns `m_type`. Called by `Player.Main/SendMirrorTimers`, `Player.Main/UpdateMirrorTimers`.

**GetRemaining**: Returns `m_tracker.GetInterval() - m_tracker.GetCurrent()`. Called by `Player.Main/SendMirrorTimers`.

**Start#2**: Activates with initial remaining time. Calls `Start(interval, spellId)`, sets `m_tracker` current to `max - current`, unfreezes. Called by `Player.Main/UpdateMirrorTimers`.

**GetDuration**: Returns `m_tracker.GetInterval()`. Called by `Player.Main/GetMirrorTimerMaxDuration`, `Player.Main/SendMirrorTimers`.

**GetScale**: Returns `m_scale`. Called by `Player.Main/SendMirrorTimers`.

**GetSpellId**: Returns `m_spellId`. Called by `Player.Main/FreezeMirrorTimers`, `Player.Main/SendMirrorTimers`, `Player.Main/UpdateMirrorTimers`.

**SetRemaining**: Sets remaining time via `m_tracker.SetCurrent`. Stops if 0. Marks `FULL_UPDATE` if active/changed. Called by `Player.Main/UpdateMirrorTimers`.

**SetDuration**: Sets total duration via `m_tracker.SetInterval`. Stops if 0. Marks `FULL_UPDATE` if active/changed. Called by `Player.Main/SetWaterBreathingIntervalMultiplier`.

**SetFrozen**: Sets `m_frozen`. Marks `STATUS_UPDATE` if active/changed. Called by `Player.Main/FreezeMirrorTimers`.

**SetScale**: Sets `m_scale`. Zero calls `SetFrozen(true)`. Marks `FULL_UPDATE` if active/changed. Called by `Player.Main/SetEnvironmentFlags`, `Player.Main/SetWaterBreathingIntervalMultiplier`.

**Update**: Advances time. Handles countdown expiration with pulse logic and regeneration completion. Returns `false` if countdown fully expired/pulsed. Called by `Player.Main/UpdateMirrorTimers`.

---

<!-- machine-true, projected from graph.json -->

## Map — MirrorTimer

*Source:* MirrorTimer.cpp, MirrorTimer.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| FetchStatus | method | — | Player.Main/SendMirrorTimers | — |
| Stop | method | ShortIntervalTimer/SetCurrent | Player.Main/UpdateMirrorTimers | — |
| Start | method | ShortIntervalTimer/SetCurrent, ShortIntervalTimer/SetInterval | Player.Main/UpdateMirrorTimers | — |
| MirrorTimer | ctor | — | — | — |
| IsActive | method | — | Player.Main/SendMirrorTimers, Player.Main/UpdateMirrorTimers | — |
| IsRegenerating | method | — | — | — |
| IsFrozen | method | — | Player.Main/SendMirrorTimers | — |
| GetType | method | — | Player.Main/SendMirrorTimers, Player.Main/UpdateMirrorTimers | — |
| GetRemaining | method | — | Player.Main/SendMirrorTimers | — |
| Start#2 | method | ShortIntervalTimer/SetCurrent | Player.Main/UpdateMirrorTimers | — |
| GetDuration | method | — | Player.Main/GetMirrorTimerMaxDuration, Player.Main/SendMirrorTimers | — |
| GetScale | method | — | Player.Main/SendMirrorTimers | — |
| GetSpellId | method | — | Player.Main/FreezeMirrorTimers, Player.Main/SendMirrorTimers, Player.Main/UpdateMirrorTimers | — |
| SetRemaining | method | ShortIntervalTimer/SetCurrent | Player.Main/UpdateMirrorTimers | — |
| SetDuration | method | ShortIntervalTimer/SetInterval | Player.Main/SetWaterBreathingIntervalMultiplier | — |
| SetFrozen | method | — | Player.Main/FreezeMirrorTimers | — |
| SetScale | method | — | Player.Main/SetEnvironmentFlags, Player.Main/SetWaterBreathingIntervalMultiplier | — |
| Update | method | ShortIntervalTimer/GetCurrent, ShortIntervalTimer/GetInterval, ShortIntervalTimer/Passed, ShortIntervalTimer/Reset, ShortIntervalTimer/SetCurrent, ShortIntervalTimer/Update | Player.Main/UpdateMirrorTimers | — |
