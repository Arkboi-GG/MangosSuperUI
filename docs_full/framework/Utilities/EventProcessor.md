# EventProcessor

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# EventProcessor

**EventProcessor** is a millisecond-precision, in-memory event scheduler used by `GameObject` and `Unit` entities to manage delayed actions. It maintains a priority queue (`std::multimap`) of `BasicEvent` objects keyed by absolute execution time. The processor advances an internal clock during game ticks and invokes `Execute` on events whose time has arrived.

It supports two event lifecycles:
1.  **One-shot:** The default. `Execute` returns `true`, and the event is deleted.
2.  **Persistent:** `Execute` returns `false` or `IsDeletable` returns `false`. The event is rescheduled for the next tick (`m_time + 1`), allowing continuous or conditional processing.

The system also supports **graceful abortion**. `ScheduleAbort` marks an event for cancellation; the actual `Abort` logic runs in the next `Update` cycle when the event is popped, preserving temporal consistency.

## Member-by-Member Behavior

### Event Lifecycle (`BasicEvent`)
*   **`BasicEvent` (ctor)**: Initializes state to `STATE_RUNNING` and timing fields to zero. Called by `Creature.Main/AssistDelayEvent`, `Spell.Main/SpellEvent`, and `Unit.Main/RelocationNotifyEvent`.
*   **`~BasicEvent` (dtor)**: Virtual destructor for derived class cleanup.
*   **`Execute`**: Virtual hook called by `Update` when the event fires. Returns `true` to signal deletion; `false` to persist.
*   **`IsDeletable`**: Returns `true` by default. Overridden by subclasses to prevent deletion even if `Execute` returns `true`.
*   **`Abort`**: Virtual method called when an event is cancelled before execution.
*   **`ScheduleAbort`**: Marks the event as `STATE_ABORT_SCHEDULED`. Asserts the event is currently running. Delegates actual abortion to the next `Update` cycle.
*   **`SetAborted`**: Transitions state to `STATE_ABORTED`. Called internally by `Update` after `Abort`. Asserts the event was not already aborted.
*   **`IsRunning`**, **`IsAbortScheduled`**, **`IsAborted`**: Private state queries used by `EventProcessor` to determine processing logic.

### Processor Control (`EventProcessor`)
*   **`Update`**: Driven by `GameObject/Update` and `Unit.Main/Update`. Advances `m_time` by `p_time`. Iterates `m_events`, processing all events with `key <= m_time`.
    *   If **Running**: Calls `Execute`. If `true`, deletes the event.
    *   If **Abort Scheduled**: Calls `Abort`, then `SetAborted`.
    *   If **Deletable**: Deletes the event.
    *   If **Non-Deletable**: Re-adds the event with `CalculateTime(1)`, pinning it to the next tick.
*   **`KillAllEvents`**: Called by `GameObject/CleanupsBeforeDelete` and `Unit.Main/CleanupsBeforeDelete`. If `force` is `true`, aborts and deletes all events, clearing the container. If `false`, aborts events but preserves non-deletable ones.
*   **`AddEvent`**: Inserts an event at absolute time `e_time`. Records `m_addTime` if requested. Called by `Spell.Main/Execute#2`, `Spell.Main/prepare#2`, `Unit.Main/ScheduleAINotify`, `Creature.Main/CallAssistance`, `Creature.Main/ForcedDespawn`, `Pet.Main/DelayedUnsummon`, `BattleGroundMgr/InviteGroupToBG`, `BattleGroundMgr/PlayerLoggedIn`, and `Map.ScriptCommands/ScriptCommand_Emote`.
*   **`AddEventAtOffset`**: Convenience wrapper calculating absolute time via `CalculateTime`. Called by `Spell.Main/SendChannelUpdate`.
*   **`CalculateTime`**: Returns `m_time + t_offset`. Used by callers to convert relative delays to absolute timestamps.
*   **`HasScheduledEvent`**: Returns `true` if `m_events` is not empty. Used by `Player.Main/InterruptSpellsWithCastItem` and `Unit.Main/InterruptSpellsCastedOnMe`.
*   **`GetEvents`**: Returns a const reference to `m_events`. Used by `Player.Main/InterruptSpellsWithCastItem` and `Unit.Main/InterruptSpellsCastedOnMe` for inspection.
*   **`EventProcessor` (ctor/dtor)**: Initializes `m_time` to 0. Destructor calls `KillAllEvents(true)`.

### Lambda Support
*   **`LambdaBasicEvent<T>` (ctor)**: Stores a callback functor.
*   **`Execute#2`**: Invokes the stored callback and returns `true`. Enables one-off delayed actions without subclassing `BasicEvent`.

## Cross-Unit Boundaries

*   **Called By**:
    *   `GameObject/Update`, `Unit.Main/Update`: Drive the scheduler via `Update`.
    *   `Spell.Main/*`, `Creature.Main/*`, `Unit.Main/*`, `Pet.Main/*`, `BattleGroundMgr/*`, `Map.ScriptCommands/*`: Schedule events via `AddEvent`, `AddEventAtOffset`, or `CalculateTime`.
    *   `Player.Main/*`, `Unit.Main/*`: Inspect events via `GetEvents` and `HasScheduledEvent`.
    *   `GameObject/CleanupsBeforeDelete`, `Unit.Main/CleanupsBeforeDelete`: Clean up via `KillAllEvents`.
*   **Calls Out**:
    *   `Errors/PrintStacktraceAndThrow`: Triggered by assertions in `ScheduleAbort` and `SetAborted` if state transitions are invalid.

## Data Model

This unit does not interact with any database tables. It operates entirely in memory.

## Notable Implementation Details

1.  **Rescheduling Logic**: Non-deletable events are rescheduled with `CalculateTime(1)`. This ensures they are processed in the immediate next tick, effectively creating a high-priority persistent loop until the event becomes deletable or is aborted.
2.  **Abort Consistency**: `ScheduleAbort` does not delete the event immediately. It waits for the next `Update` cycle to call `Abort`, ensuring cleanup logic runs at the correct simulation time relative to other events.
3.  **Assertion Safety**: `ScheduleAbort` and `SetAborted` assert valid state transitions. Violations indicate bugs in caller logic (e.g., double-aborting).
4.  **Lambda Integration**: `LambdaBasicEvent` allows inline callbacks, reducing boilerplate for simple delayed actions.

## Member Reference

*   **`ScheduleAbort`**: Marks the event for abortion in the next update tick. Asserts that the event is currently running.
*   **`SetAborted`**: Transitions the event state to aborted. Asserts that the event was not already aborted.
*   **`~EventProcessor`**: Destructor that calls `KillAllEvents(true)` to clean up all pending events.
*   **`BasicEvent`**: Constructor for the base event class, initializing state to running and times to zero.
*   **`Update`**: Advances the internal clock and processes all events due for execution, handling execution, abortion, and rescheduling.
*   **`~BasicEvent`**: Virtual destructor for cleanup in derived classes.
*   **`Execute`**: Virtual method called when an event fires. Returns `true` to delete the event, `false` to keep it.
*   **`IsDeletable`**: Returns `true` by default, indicating the event can be deleted after execution.
*   **`Abort`**: Virtual method called when an event is cancelled before execution.
*   **`IsRunning`**: Checks if the event state is `STATE_RUNNING`.
*   **`IsAbortScheduled`**: Checks if the event state is `STATE_ABORT_SCHEDULED`.
*   **`IsAborted`**: Checks if the event state is `STATE_ABORTED`.
*   **`LambdaBasicEvent<T>`**: Constructor for a lambda-based event, storing the callback.
*   **`Execute#2`**: Overrides `BasicEvent::Execute` to invoke the stored lambda callback.
*   **`KillAllEvents`**: Cancels and optionally deletes all pending events. Used during entity cleanup.
*   **`EventProcessor`**: Constructor initializing the internal time to zero.
*   **`AddEventAtOffset`**: Schedules an event at a relative time offset from the current simulation time.
*   **`HasScheduledEvent`**: Returns `true` if there are any events in the queue.
*   **`GetEvents`**: Returns a constant reference to the internal event list.
*   **`AddEvent`**: Inserts an event into the queue at a specific absolute time.
*   **`CalculateTime`**: Computes the absolute time by adding an offset to the current simulation time.

---

<!-- machine-true, projected from graph.json -->

## Map — EventProcessor

*Source:* EventProcessor.cpp, EventProcessor.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ScheduleAbort | method | Errors/PrintStacktraceAndThrow | — | — |
| SetAborted | method | Errors/PrintStacktraceAndThrow | — | — |
| ~EventProcessor | dtor | — | — | — |
| BasicEvent | ctor | — | Creature.Main/AssistDelayEvent, Spell.Main/SpellEvent, Unit.Main/RelocationNotifyEvent | — |
| Update | method | — | GameObject/Update, Unit.Main/Update | — |
| ~BasicEvent | dtor | — | — | — |
| Execute | method | — | — | — |
| IsDeletable | method | — | — | — |
| Abort | method | — | — | — |
| IsRunning | method | — | — | — |
| IsAbortScheduled | method | — | — | — |
| IsAborted | method | — | — | — |
| LambdaBasicEvent<T> | ctor | — | — | — |
| Execute#2 | function | — | — | — |
| KillAllEvents | method | — | GameObject/CleanupsBeforeDelete, Unit.Main/CleanupsBeforeDelete | — |
| EventProcessor | ctor | — | — | — |
| AddEventAtOffset | method | — | Spell.Main/SendChannelUpdate | — |
| HasScheduledEvent | method | — | — | — |
| GetEvents | method | — | Player.Main/InterruptSpellsWithCastItem, Unit.Main/InterruptSpellsCastedOnMe | — |
| AddEvent | method | — | BattleGroundMgr/InviteGroupToBG, BattleGroundMgr/PlayerLoggedIn, Creature.Main/CallAssistance, Creature.Main/ForcedDespawn, Map.ScriptCommands/ScriptCommand_Emote, Pet.Main/DelayedUnsummon, Spell.Main/Execute#2, Spell.Main/prepare#2, Unit.Main/ScheduleAINotify | — |
| CalculateTime | method | — | BattleGroundMgr/InviteGroupToBG, BattleGroundMgr/PlayerLoggedIn, Creature.Main/CallAssistance, Creature.Main/ForcedDespawn, Map.ScriptCommands/ScriptCommand_Emote, Pet.Main/DelayedUnsummon, Spell.Main/prepare#2, Unit.Main/ScheduleAINotify | — |
