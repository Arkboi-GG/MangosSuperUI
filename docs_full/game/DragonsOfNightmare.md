# DragonsOfNightmare

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# DragonsOfNightmare

**Purpose & Responsibilities**

The `DragonsOfNightmare` struct implements the logic for the "Dragons of Nightmare" world event (Event ID 66) within the `wowvmangos` server framework. It is responsible for managing the lifecycle of four specific dragon NPCs—Ysondre, Lethon, Emeriss, and Taerar—defined by the `NightmareDragons` array. The struct inherits from `WorldEvent`, integrating into the server's global event scheduling system to handle periodic updates, activation, and deactivation of the event based on the state of these creatures in the game world.

**Member-by-Member Behavior**

### Construction and Lifecycle

**DragonsOfNightmare**
This constructor initializes a `DragonsOfNightmare` instance. It invokes the base class `WorldEvent` constructor, passing the constant `EVENT_NIGHTMARE` (value 66) to register this specific event with the event manager. This registration ensures the event is recognized and scheduled for updates by the global `WorldEventMgr`. The constructor is called by `ChatHandler.HardcodedEvents/LoadHardcodedEvents` during the server's initialization sequence, ensuring the event object is created before the game world fully loads.

**Cross-Unit Boundaries**

*   **Called by `ChatHandler.HardcodedEvents/LoadHardcodedEvents`**: The `DragonsOfNightmare` constructor is invoked by the `ChatHandler` module (specifically the `LoadHardcodedEvents` function) during server startup. This establishes the event object in memory. No data is returned to the caller; the interaction is strictly for instantiation.
*   **Calls into `WorldEvent` (Base Class)**: As a derived class, `DragonsOfNightmare` relies on the base `WorldEvent` infrastructure for its fundamental lifecycle management. The constructor passes the event ID to the base class for registration. Although the `Update`, `Enable`, and `Disable` methods are declared in this header, their implementations (not shown in the provided source snippet for this specific unit) would interact with the base class's scheduling and state management mechanisms.

**Data Model**

This unit does not directly interact with any database tables. All configuration, including the event ID (`EVENT_NIGHTMARE`) and the NPC identifiers for the dragons (`NPC_YSONDRE`, `NPC_LETHON`, `NPC_EMERISS`, `NPC_TAERAR`), is hardcoded in the header file. State management is handled in-memory through the `WorldEvent` framework and direct interaction with creature entities in the game world.

**Notable Implementation Details**

*   **Hardcoded Configuration**: The event ID and the list of dragon NPC IDs are defined as static constants in `HardcodedEvents.h`. Changes to these values require recompilation of the server binary.
*   **State Tracking via Entities**: The design suggests that event state is derived from the actual presence and health of the creature entities in the game world, rather than relying solely on database variables. This is indicated by the presence of helper methods like `LoadDragons` and `GetAliveCountAndUpdateRespawnTime` in the class declaration, which operate on `ObjectGuid` vectors and creature states.
*   **Commented-Out Code**: A commented-out method declaration `GetExistingDragons` exists in the header, suggesting previous or alternative approaches to locating the dragons were considered but are not part of the current active implementation.

## Member Reference

**DragonsOfNightmare**
Constructor that initializes the `DragonsOfNightmare` event object. It calls the base `WorldEvent` constructor with the event ID `EVENT_NIGHTMARE` (66). It is instantiated by `ChatHandler.HardcodedEvents/LoadHardcodedEvents` during server startup.

---

<!-- machine-true, projected from graph.json -->

## Map — DragonsOfNightmare

*Source:* HardcodedEvents.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| DragonsOfNightmare | ctor | — | ChatHandler.HardcodedEvents/LoadHardcodedEvents | — |
