# WorldEvent

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# WorldEvent

**Purpose & Responsibilities**

`WorldEvent` is an abstract base class (implemented as a struct with virtual methods) within the `GameEventMgr` system. It defines the interface for **hardcoded**, server-side world events that require custom logic beyond simple time-based activation. Unlike standard game events driven by database schedules (`gameevent` table), `WorldEvent` instances represent complex, scripted scenarios—such as the Scourge Invasion or War Effort—where the server must manage internal state, determine dynamic update intervals, and execute specific enable/disable behaviors.

The class serves as a polymorphic handle for `GameEventMgr`. The manager maintains a list of `WorldEvent*` pointers (`HardcodedEventList`) and iterates through them during its main update loop. Each derived class implements the pure virtual-like interface (though defaults are provided) to dictate how that specific event behaves over time.

**Member-by-Member Behavior**

*   **Construction/Destruction**: The constructor initializes the event ID. The destructor is virtual, ensuring proper cleanup of derived classes.
*   **Lifecycle Methods (`Enable`, `Disable`)**: These methods define the side effects of turning an event on or off. In the base class, they are empty stubs. Derived classes override these to spawn NPCs, change zone states, or send global messages.
*   **Update Loop (`Update`, `GetNextUpdateDelay`)**:
    *   `Update()` is called periodically by `GameEventMgr`. In the base class, it does nothing. Derived classes use this to check conditions (e.g., "Has the invasion wave ended?").
    *   `GetNextUpdateDelay()` returns the number of milliseconds (or ticks, depending on the caller's interpretation, though the constant `max_ge_check_delay` suggests seconds) until the next `Update()` call. The base class returns `max_ge_check_delay` (86400 seconds, i.e., 1 day), effectively disabling frequent updates unless overridden. This allows long-running events to sleep efficiently while short-duration events poll frequently.

**Cross-Unit Boundaries**

*   **Called by `ChatHandler.HardcodedEvents/ScourgeInvasionEvent` and `ChatHandler.HardcodedEvents/WarEffortEvent`**:
    *   **Direction**: Outbound from ChatHandler to WorldEvent.
    *   **Context**: When a Game Master uses commands like `.scourge` or `.wareffort`, the `ChatHandler` creates a new instance of the specific derived `WorldEvent` class (e.g., `ScourgeInvasionEvent`). This instantiates the object so it can be added to the manager's tracking list.
*   **Called by `GameEventMgr.Main/Update`**:
    *   **Direction**: Inbound to WorldEvent.
    *   **Context**: The central `GameEventMgr` singleton iterates through all registered hardcoded events. It calls `GetNextUpdateDelay()` to see if the event needs attention now. If the delay has elapsed, it calls `Update()` to let the event process its logic.
*   **Called by `GameEventMgr.Main/EnableEvent` and `GameEventMgr.Main/StartEvent`**:
    *   **Direction**: Inbound to WorldEvent.
    *   **Context**: When an event is manually started via command or automatically triggered, `GameEventMgr` calls `Enable()` on the `WorldEvent` instance to trigger its startup logic.
*   **Called by `GameEventMgr.Main/EnableEvent`**:
    *   **Direction**: Inbound to WorldEvent.
    *   **Context**: Specifically, when an event is being toggled off (or stopped), `Disable()` is called to clean up the event's state.

**Data Model**

This unit does not directly interact with any database tables. It operates entirely in memory using the `m_eventId` and state managed by derived classes. The `GameEventMgr` itself loads standard events from the `gameevent` table, but `WorldEvent` instances are hardcoded C++ objects.

**Notable Implementation Details**

*   **Default Sleep Interval**: The default `GetNextUpdateDelay` returns `max_ge_check_delay` (defined as 86400). This is a critical optimization. If a derived class fails to override this, the event will only update once a day. Maintainers must ensure derived classes return appropriate smaller values if the event requires frequent polling.
*   **Empty Base Methods**: `Update`, `Enable`, and `Disable` have empty bodies in the base class. This means if a derived class forgets to override one of these, the event will silently fail to perform that action. There is no runtime error or warning.
*   **Polymorphism via Struct**: Although defined as a `struct`, it functions as a class with virtual dispatch. The `virtual` keyword on the destructor and methods ensures that `GameEventMgr` can treat all hardcoded events uniformly regardless of their specific derived type.

## Member Reference

**WorldEvent**
Constructor that takes a `uint16 eventId` and stores it in `m_eventId`. Used by `ChatHandler` subclasses to instantiate specific event handlers.

**~WorldEvent**
Virtual destructor. Ensures correct cleanup when `GameEventMgr` deletes pointers to derived `WorldEvent` classes.

**Update**
Virtual method called by `GameEventMgr.Update`. In the base class, it performs no action. Derived classes override this to implement periodic logic (e.g., checking win conditions, spawning waves).

**Enable**
Virtual method called by `GameEventMgr.EnableEvent` and `GameEventMgr.StartEvent`. In the base class, it performs no action. Derived classes override this to initialize the event state (e.g., setting flags, spawning initial entities).

**Disable**
Virtual method called by `GameEventMgr.EnableEvent` (when disabling). In the base class, it performs no action. Derived classes override this to clean up the event state (e.g., despawning entities, resetting flags).

**GetNextUpdateDelay**
Virtual method called by `GameEventMgr.Update`. Returns the delay before the next `Update` call. The base class returns `max_ge_check_delay` (86400). Derived classes override this to provide dynamic or fixed shorter intervals for active events.

---

<!-- machine-true, projected from graph.json -->

## Map — WorldEvent

*Source:* GameEventMgr.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| WorldEvent | ctor | — | ChatHandler.HardcodedEvents/ScourgeInvasionEvent, ChatHandler.HardcodedEvents/WarEffortEvent | — |
| ~WorldEvent | dtor | — | — | — |
| Update | method | — | GameEventMgr.Main/Update | — |
| Enable | method | — | GameEventMgr.Main/EnableEvent, GameEventMgr.Main/StartEvent | — |
| Disable | method | — | GameEventMgr.Main/EnableEvent | — |
| GetNextUpdateDelay | method | — | GameEventMgr.Main/Update | — |
