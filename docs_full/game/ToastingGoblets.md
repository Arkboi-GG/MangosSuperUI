# ToastingGoblets

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ToastingGoblets

**ToastingGoblets** is a `WorldEvent` subclass defined in `HardcodedEvents.h` that manages the "Toasting Goblets" seasonal event. It inherits the standard lifecycle interface from `WorldEvent` and is responsible for determining when this specific event should be active based on internal logic encapsulated in its private helper method.

## Purpose & Responsibilities

The primary responsibility of `ToastingGoblets` is to control the activation state of the event identified by the constant `EVENT_TOASTING_GOBLETS` (value 39). As a `WorldEvent`, it is integrated into the server's global event management system, which periodically updates and checks the status of all registered events.

Unlike more complex events in the same header (such as `ElementalInvasion` or `ScourgeInvasionEvent`), `ToastingGoblets` is minimalist. It does not maintain persistent state variables, manage creature spawns directly in its declaration, or handle complex phase transitions. Instead, it relies on the `WorldEvent` base class for timing and scheduling, while providing specific logic via `ShouldEnable` to determine if the event conditions are met.

## Member-by-Member Behavior

### Construction
**`ToastingGoblets`**
The constructor initializes the base `WorldEvent` class with the identifier `EVENT_TOASTING_GOBLETS`. This registration allows the event manager to associate this instance with the configuration data for event ID 39.

### Lifecycle Methods
The class overrides the standard `WorldEvent` lifecycle methods:
*   **`Update`**: Called periodically by the event manager. While the implementation is not visible in the header, this method typically evaluates whether the event should transition between enabled and disabled states.
*   **`Enable`**: Called when the event becomes active. This method triggers the effects associated with "Toasting Goblets," such as spawning NPCs, playing sounds, or updating world states.
*   **`Disable`**: Called when the event ends. This method cleans up any resources or entities created during the event's active period.

### Internal Logic
*   **`ShouldEnable`**: A private const method that returns a boolean indicating whether the event should currently be active. This method encapsulates the specific conditions required for the event to run, separating the decision logic from the lifecycle execution.

## Cross-Unit Boundaries

*   **Called by `ChatHandler.HardcodedEvents/LoadHardcodedEvents`**:
    The `ToastingGoblets` constructor is invoked by the `LoadHardcodedEvents` function in the `ChatHandler` unit. This indicates that `ToastingGoblets` is instantiated during the server's initialization sequence for hardcoded events. The `ChatHandler` unit acts as the factory, creating instances of various `WorldEvent` subclasses and registering them with the event management system. No data is passed across this boundary other than the implicit creation of the object.

*   **Calls out**: None.
    The `ToastingGoblets` unit does not directly call into other units in its public or private interface as shown in the map. Any interactions with the game world are handled internally within the overridden `Enable`, `Disable`, and `Update` methods, which likely use standard server APIs abstracted by the `WorldEvent` base class.

## Data Model

This unit does not interact with any database tables. All configuration and state management are handled in-memory through the `WorldEvent` framework and hardcoded constants.

## Notable Implementation Details

*   **Minimalist Design**: Compared to other events in `HardcodedEvents.h`, `ToastingGoblets` is extremely lightweight. It contains no member variables, no complex state machines, and no nested structs. This suggests the event is simple, likely a timed broadcast or a static spawn set that turns on/off based on a straightforward condition.
*   **Encapsulation of Activation Logic**: The separation of `ShouldEnable` from the lifecycle methods allows for easy modification of the event's trigger conditions without altering the core enable/disable mechanics.
*   **Event ID**: The event is tied to ID 39 (`EVENT_TOASTING_GOBLETS`). This ID is used by the `WorldEvent` base class to look up configuration data (such as start/end times) from the `game_event` table or similar configuration sources, though the table interaction itself is abstracted away by the `WorldEvent` base class.

## Member Reference

**ToastingGoblets**
Constructor that initializes the `WorldEvent` base class with the ID `EVENT_TOASTING_GOBLETS` (39). Instantiated by `ChatHandler.HardcodedEvents/LoadHardcodedEvents` during server startup.

---

<!-- machine-true, projected from graph.json -->

## Map — ToastingGoblets

*Source:* HardcodedEvents.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ToastingGoblets | ctor | — | ChatHandler.HardcodedEvents/LoadHardcodedEvents | — |
