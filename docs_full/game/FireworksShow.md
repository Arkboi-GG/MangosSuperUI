# FireworksShow

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

The `FireworksShow` class, defined in `HardcodedEvents.h`, is a specialized implementation of the `WorldEvent` base class responsible for managing the **Lunar Festival Fireworks** event within the server. It handles the lifecycle (enable/disable) and periodic updates of the fireworks display associated with event ID `EVENT_FIREWORKS` (value 6).

Unlike more complex events like `ElementalInvasion` or `ScourgeInvasionEvent` which manage creature spawns, stages, and combat logic, `FireworksShow` is a lightweight controller. Its primary responsibility is to determine when the fireworks should trigger based on the current time, specifically checking if the minute mark aligns with the beginning of an hour (or a specific interval defined by `FIREWORKS_DURATION`). It inherits standard event management capabilities from `WorldEvent` but provides custom logic for timing checks via its private helper `IsHourBeginning`.

This unit does not interact with any database tables; all configuration (event IDs, durations) is hardcoded in the header file.

## Member-by-Member Behavior

### Construction and Initialization

*   **`FireworksShow()`**: The constructor initializes the `WorldEvent` base class with the constant `EVENT_FIREWORKS` (value 6). This registers the instance with the global event manager as the handler for the fireworks event. It is instantiated during the server startup phase when hardcoded events are loaded.

### Event Lifecycle Management

These methods are virtual overrides of the `WorldEvent` interface, allowing the global event manager to control the state of the fireworks event. Their implementations are not visible in this header file but are declared here to fulfill the contract required by the `WorldEvent` base class.

*   **`Update()`**: Called periodically by the event manager while the event is active. Based on the presence of the private helper `IsHourBeginning`, this method is expected to check the current time and trigger visual effects or sound cues if the condition is met.
*   **`Enable()`**: Activates the fireworks event. This likely sets internal state flags to indicate the event is running and may schedule the first update check.
*   **`Disable()`**: Deactivates the fireworks event. This cleans up any active states and stops further updates until the event is re-enabled.

### Timing Logic

*   **`IsHourBeginning(uint8 minutes)`**: A private helper method that determines if the current time corresponds to the start of an hour (or a specific interval). It takes an optional parameter `minutes`, defaulting to `FIREWORKS_DURATION` (10). This suggests the fireworks might not trigger exactly on the hour, but potentially every 10 minutes, or that the "beginning" is defined relative to a 10-minute window. The method returns a boolean indicating whether the current time satisfies this condition.

## Cross-Unit Boundaries

*   **Called by `ChatHandler.HardcodedEvents/LoadHardcodedEvents`**:
    *   **Direction**: Incoming.
    *   **Context**: During server initialization or when reloading hardcoded events via chat commands, the `ChatHandler` unit instantiates `FireworksShow`. This places the object into the global event registry managed by `GameEventMgr` (included via `GameEventMgr.h`).
    *   **Purpose**: To ensure the fireworks event is recognized and can be triggered by the global calendar/event system.

*   **Calls into `WorldEvent` (Base Class)**:
    *   **Direction**: Outgoing (Inheritance).
    *   **Context**: `FireworksShow` inherits from `WorldEvent`. It relies on the base class for fundamental event management structures, such as registration with the event loop, basic enable/disable state tracking, and potentially logging.
    *   **Purpose**: To integrate seamlessly with the server's core event scheduling system.

*   **Includes `GameEventMgr.h` and `ObjectMgr.h`**:
    *   **Context**: These headers provide access to global event management utilities and object management facilities. While `FireworksShow` itself doesn't explicitly call functions from these managers in the visible header, the base class `WorldEvent` and the surrounding infrastructure rely on them for event persistence and object lookup.

## Data Model

This unit does not interact with any database tables. All event identifiers (`EVENT_FIREWORKS`, `EVENT_NEW_YEAR`, etc.) and configuration constants (`FIREWORKS_DURATION`) are hardcoded in the `HardcodedEvents.h` file. There are no SQL queries or table references in the provided source.

## Notable Implementation Details

1.  **Hardcoded Event IDs**: The event ID `EVENT_FIREWORKS` is set to `6`. Other related festival events like `EVENT_NEW_YEAR` (34), `EVENT_LUNAR_NEW_YEAR` (38), `EVENT_TOASTING_GOBLETS` (39), `EVENT_JULY_4TH` (41), and `EVENT_SEPTEMBER_30TH` (42) are also defined in the same enum block. This suggests that `FireworksShow` is part of a broader set of seasonal/holiday events managed similarly.
2.  **Duration Constant**: `FIREWORKS_DURATION` is defined as `10`. This value is passed as the default argument to `IsHourBeginning`. This implies that the fireworks logic might check for triggers every 10 minutes, or that the "hour beginning" check is offset or scaled by this duration. Without the `.cpp` implementation, the exact semantic meaning of this 10-minute interval is inferred but critical for understanding the timing.
3.  **Minimalist Design**: Compared to other events in the same file (e.g., `ScourgeInvasionEvent` with its complex zone management, or `WarEffortEvent` with its multi-stage progression), `FireworksShow` is extremely simple. It lacks state variables for stages, kills, or creature GUIDs. This indicates it is likely a purely visual/audio effect event, triggered by time, without player interaction or combat components.
4.  **Template for Other Events**: The structure of `FireworksShow` mirrors `ToastingGoblets`, another simple event in the same file. Both inherit from `WorldEvent` and override `Update`, `Enable`, and `Disable`. This pattern suggests a standardized approach for simple, time-based world events in this codebase.

## Member Reference

*   **FireworksShow**: Constructor that initializes the `WorldEvent` base class with `EVENT_FIREWORKS` (ID 6). Instantiated by `ChatHandler.HardcodedEvents/LoadHardcodedEvents` during server startup or event reload.

---

<!-- machine-true, projected from graph.json -->

## Map — FireworksShow

*Source:* HardcodedEvents.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| FireworksShow | ctor | — | ChatHandler.HardcodedEvents/LoadHardcodedEvents | — |
