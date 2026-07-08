# ElementalInvasion

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ElementalInvasion

## Purpose & Responsibilities

`ElementalInvasion` is a `WorldEvent` subclass responsible for managing the **Elemental Invasion** world event in the World of Warcraft server emulation. This event involves periodic invasions by elemental forces (Fire, Air, Earth, Water) across specific zones, culminating in boss encounters.

The class acts as a state machine controller for four distinct elemental factions. It tracks the progression of each faction through stages (rift opening, mob spawning, kill requirements) and triggers the corresponding boss spawns when conditions are met. The logic is driven by hardcoded configuration data (`InvasionData`) and interacts with the global game event system to manage spawns, despawns, and variable updates.

As a `WorldEvent`, it integrates into the server's global tick loop via `Update()`, allowing it to monitor time-based progressions and react to changes in world state variables (likely representing kill counts or stage progress). It does not store persistent state in the database itself but relies on the `game_event` and `creature_template` infrastructure managed by `GameEventMgr` and `ObjectMgr`.

## Member-by-Member Behavior

### Construction and Lifecycle

**`ElementalInvasion()`**
This constructor initializes the `ElementalInvasion` instance as a `WorldEvent` with the ID `EVENT_INVASION` (value 13). It sets up the base class context required for the event to be recognized and scheduled by the `GameEventMgr`. The constructor is called by `ChatHandler.HardcodedEvents/LoadHardcodedEvents` during the server startup sequence when hardcoded events are registered.

### Core Event Logic (Implied by Interface)

While the detailed implementation of `Update()`, `Enable()`, and `Disable()` is not provided in the source snippet, their signatures indicate standard `WorldEvent` lifecycle management:
*   **`Update()`**: Likely iterates through the four elemental factions, checking their current stage against world variables. It would trigger transitions (e.g., from rift phase to boss phase) based on kill counts or timers.
*   **`Enable()`**: Activates the event, potentially starting initial timers or setting up the first wave of invasions.
*   **`Disable()`**: Cleans up the event state, despawning any active invaders or bosses associated with this event.

### Internal Helper Methods (Declared in Header)

These private methods define the internal mechanics of the invasion phases:

*   **`StartLocalInvasion(uint8 index, uint32 stage)`**: Initiates the invasion phase for a specific element (identified by `index` 0-3) at a given `stage`. This likely involves spawning rifts or initial mobs.
*   **`StartLocalBoss(uint8 index, uint32 stage, uint8 delay)`**: Triggers the boss encounter for the specified element. The `delay` parameter suggests a brief pause before the boss becomes active or visible.
*   **`StopLocalInvasion(uint8 index, uint32 stage, uint8 delay)`**: Ends the invasion phase for an element, likely despawning non-boss entities and preparing for the next cycle or boss phase.
*   **`ResetThings()`**: Resets the internal state of the event, possibly clearing temporary flags or resetting counters for a new cycle.

## Cross-Unit Boundaries

*   **Called by `ChatHandler.HardcodedEvents/LoadHardcodedEvents`**:
    *   **Direction**: Inbound.
    *   **Context**: During server initialization, the chat handler module registers hardcoded world events. It instantiates `ElementalInvasion` and adds it to the `GameEventMgr`'s list of active events. This establishes the event's presence in the server's global event loop.

*   **Calls out to `GameEventMgr` (Implicit)**:
    *   **Direction**: Outbound.
    *   **Context**: As a `WorldEvent`, `ElementalInvasion` relies on `GameEventMgr` for scheduling updates and managing the global event state. Methods like `EnableAndStartEvent` or `DisableAndStopEvent` (common in similar classes like `ScourgeInvasionEvent` in the same file) would interact with `GameEventMgr` to toggle sub-events or spawn groups.

*   **Calls out to `ObjectMgr` (Implicit)**:
    *   **Direction**: Outbound.
    *   **Context**: To spawn creatures (rifts, mobs, bosses), the event logic uses `ObjectMgr` to retrieve creature templates and spawn instances. The `InvasionData` array contains GUIDs and event IDs that map to entries in the `creature_template` and `game_event_creature` tables.

## Data Model

The `ElementalInvasion` class does not directly query or modify database tables in its own scope. Instead, it operates on **hardcoded configuration** and **runtime world state**.

*   **`InvasionData` Array**: A static array of `InvasionDataStruct` defines the mapping between elements and their game event IDs, boss GUIDs, and variable indices.
    *   `eventRift`: Game event ID for the rift/spawn phase.
    *   `eventBoss`: Game event ID for the boss phase.
    *   `varDelay`, `varKills`, `varStage`: Indices into the world state variables (likely stored in memory or synced via `worldstates` table) that track the progress of each element.
    *   `bossGuid`: The unique identifier for the boss creature.

*   **Database Tables (Indirect)**:
    *   `game_event`: Defines the timing and duration of the overall Elemental Invasion event (ID 13).
    *   `game_event_creature`: Links the sub-event IDs (68-75) to specific creature spawns.
    *   `creature_template`: Contains the definitions for the rifts, mobs, and bosses referenced by the GUIDs in `InvasionData`.

## Notable Implementation Details

1.  **Hardcoded Configuration**: The entire structure of the Elemental Invasion (which element corresponds to which event ID, boss GUID, and variable index) is hardcoded in the `InvasionData` array. This means changes to the event's structure require recompilation of the server binary.
2.  **Elemental Mapping**:
    *   **Fire**: Event 68, Boss GUID 58300.
    *   **Air**: Event 69, Boss GUID 58000.
    *   **Earth**: Event 70, Boss GUID 58100.
    *   **Water**: Event 71, Boss GUID 1184054.
3.  **Variable-Driven Progression**: The use of `varDelay`, `varKills`, and `varStage` suggests that the event's progression is tracked via world state variables. These variables are likely updated by scripts attached to the spawned creatures (e.g., when a mob dies, it increments a kill counter variable). The `ElementalInvasion` class reads these variables to determine when to advance the stage or spawn the boss.
4.  **No Persistence**: The class does not save its state to the database. Upon server restart, the event resets to its initial state defined by the `game_event` table's schedule.

## Member Reference

**`ElementalInvasion`**
Constructor for the `ElementalInvasion` class. Initializes the object as a `WorldEvent` with ID `EVENT_INVASION` (13). Called by `ChatHandler.HardcodedEvents/LoadHardcodedEvents` during server startup to register the event with the `GameEventMgr`.

---

<!-- machine-true, projected from graph.json -->

## Map — ElementalInvasion

*Source:* HardcodedEvents.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ElementalInvasion | ctor | — | ChatHandler.HardcodedEvents/LoadHardcodedEvents | — |
