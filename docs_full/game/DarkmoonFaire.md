# DarkmoonFaire

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# DarkmoonFaire

**Purpose & Responsibilities**

The `DarkmoonFaire` class implements the server-side logic for the Darkmoon Faire world event in World of Warcraft. It inherits from `WorldEvent`, integrating into the server's global event management system. Its primary responsibility is to determine whether the Darkmoon Faire should be active based on the current real-world date, specifically identifying the first Monday of each month. It manages the state transitions between different phases of the Faire (Installation vs. Active) for both Alliance (`DARKMOON_A2`) and Horde (`DARKMOON_H2`) factions, ensuring the event spawns and despawns correctly according to the calendar.

**Member-by-Member Behavior**

The unit contains a single public member: the constructor.

*   **Constructor (`DarkmoonFaire`)**: Initializes the `DarkmoonFaire` object. It invokes the base class constructor `WorldEvent` with the argument `DARKMOON_A2`. This sets the initial internal event ID to the Alliance Installation phase constant (`23`). A comment in the source code notes `// TODO - should not be used that way`, indicating that initializing directly to the Installation phase might be a placeholder or suboptimal design choice, as the Faire typically starts in a "None" state until the specific date arrives.

**Cross-Unit Boundaries**

*   **Called by `ChatHandler.HardcodedEvents/LoadHardcodedEvents`**: The `DarkmoonFaire` instance is constructed during the server startup sequence, specifically within the `LoadHardcodedEvents` routine of the `ChatHandler` unit (likely part of the console/command handler subsystem responsible for initializing static game data). This establishes the `DarkmoonFaire` object as a singleton-like global event handler managed by the core event manager.
*   **Calls Out**: The constructor itself makes no outgoing calls to other units beyond the base class initialization. However, the inherited methods `Update`, `Enable`, and `Disable` (declared in `HardcodedEvents.h` but implemented elsewhere or implicitly via the framework) will interact with the `GameEventMgr` and `ObjectMgr` (included headers) to manipulate game states and spawn objects. Since these implementations are not in this partial, we note that the *interface* relies on these managers.

**Data Model**

This unit does not directly query or modify database tables. It operates entirely on hardcoded constants and real-time clock calculations. The event IDs (`DARKMOON_A2`, etc.) correspond to entries in the `game_event` table, but `DarkmoonFaire` itself does not perform SQL operations.

**Notable Implementation Details**

1.  **Initialization State**: The constructor initializes the event ID to `DARKMOON_A2_INSTALLATION` (value 23). This suggests the Faire is considered "installing" by default upon server boot, regardless of the current date. The actual logic to determine if the Faire *should* be active is likely contained in the `Update()` or `GetDarkmoonState()` methods, which are declared in this header but whose implementation details are not visible in this specific partial.
2.  **Faction Separation**: The enum `DarkmoonState` defines distinct states for Alliance (`DARKMOON_A2`, `DARKMOON_A2_INSTALLATION`) and Horde (`DARKMOON_H2`, `DARKMOON_H2_INSTALLATION`). This implies the Faire operates independently for each faction, likely spawning different NPCs and quests based on the player's affiliation.
3.  **Calendar Logic**: The private helper `FindMonthFirstMonday` indicates that the activation logic relies on calculating the first Monday of the month. This is a complex date calculation that must account for leap years and varying month lengths. The `tm` structure usage confirms standard C-style time manipulation.
4.  **TODO Comments**: The presence of `// TODO (spawns, game_event)` next to the installation states suggests that the full integration with spawn groups and game event triggers might be incomplete or handled in a fragmented manner across other files.

## Member Reference

**DarkmoonFaire**
Constructor for the `DarkmoonFaire` world event. Initializes the base `WorldEvent` class with the event ID `DARKMOON_A2_INSTALLATION` (value 23). This sets the initial state to the Alliance Installation phase. It is instantiated by `ChatHandler.HardcodedEvents/LoadHardcodedEvents` during server startup.

---

<!-- machine-true, projected from graph.json -->

## Map — DarkmoonFaire

*Source:* HardcodedEvents.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| DarkmoonFaire | ctor | — | ChatHandler.HardcodedEvents/LoadHardcodedEvents | — |
