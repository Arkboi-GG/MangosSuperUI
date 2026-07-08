# GameEventData

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# GameEventData

**Purpose & Responsibilities**

`GameEventData` is a plain data structure defined in `GameEventMgr.h` that stores the static configuration for a single game event. It holds temporal parameters (start/end times, duration, recurrence interval), administrative flags (`disabled`, `hardcoded`), and metadata (`holiday_id`, `description`). The struct is designed to be stored in a `std::vector` indexed by event ID within `GameEventMgr`, providing fast lookup for event properties. Its primary behavioral responsibility is defining validity: an event is considered valid only if it has a positive duration (`length > 0`).

## Member-by-Member Behavior

### Initialization
**`GameEventData`** (Constructor)
The default constructor initializes all members to neutral defaults. Crucially, it sets `length` to `0`, which marks the instance as invalid until populated with actual event data. Other fields like `start` are set to `1` (epoch + 1s) and `end` to `0`, ensuring that uninitialized objects do not accidentally trigger time-based logic.

### Validation
**`isValid`**
A const method that returns `true` if `length > 0`. This is the sole criterion for event validity. It is used by command handlers to ensure that administrative actions (enable/disable/start/stop) are only performed on events that have been properly configured with a duration.

## Cross-Unit Boundaries

`GameEventData` does not call out to other units. It is consumed by `ChatHandler.ServerCommands` for administrative validation:

- **Called by `ChatHandler.ServerCommands/HandleEventDisableCommand`**: Checks `isValid()` to ensure the target event exists and is configured before disabling it.
- **Called by `ChatHandler.ServerCommands/HandleEventEnableCommand`**: Checks `isValid()` before enabling an event.
- **Called by `ChatHandler.ServerCommands/HandleEventStartCommand`**: Checks `isValid()` before manually starting an event.
- **Called by `ChatHandler.ServerCommands/HandleEventStopCommand`**: Checks `isValid()` before manually stopping an event.

In all cases, `ChatHandler` uses `isValid()` as a guard to prevent operations on non-existent or malformed event IDs.

## Data Model

`GameEventData` does not directly access database tables. It is populated by `GameEventMgr::LoadFromDB()` (in `GameEventMgr.cpp`), which reads from the `game_event` table. The struct fields map to columns in that table, but no SQL or schema is present in this unit.

## Notable Implementation Details

1.  **Validity Logic**: Validity is strictly tied to `length > 0`. An event with zero duration is treated as non-existent, allowing `GameEventMgr` to skip empty slots in its event vector.
2.  **Time Units**: `start` and `end` are `time_t` (seconds), while `occurence` and `length` are `uint32` (minutes). Callers must handle unit conversions.
3.  **Leap Days**: The `leapDays` field accounts for calendar anomalies in long-term recurring events, though the logic for using it resides in `GameEventMgr`.

## Member Reference

**GameEventData**
Default constructor initializing all members to safe defaults (`start=1`, `end=0`, `occurence=0`, `length=0`, `holiday_id=HOLIDAY_NONE`, `hardcoded=0`, `disabled=0`, `leapDays=0`). Sets `length=0` to mark the instance as invalid until configured.

**isValid**
Const method returning `true` if `length > 0`. Called by `ChatHandler.ServerCommands/HandleEventDisableCommand`, `ChatHandler.ServerCommands/HandleEventEnableCommand`, `ChatHandler.ServerCommands/HandleEventStartCommand`, and `ChatHandler.ServerCommands/HandleEventStopCommand` to verify event configuration before administrative actions.

---

<!-- machine-true, projected from graph.json -->

## Map — GameEventData

*Source:* GameEventMgr.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GameEventData | ctor | — | — | — |
| isValid | method | — | ChatHandler.ServerCommands/HandleEventDisableCommand, ChatHandler.ServerCommands/HandleEventEnableCommand, ChatHandler.ServerCommands/HandleEventStartCommand, ChatHandler.ServerCommands/HandleEventStopCommand | — |
