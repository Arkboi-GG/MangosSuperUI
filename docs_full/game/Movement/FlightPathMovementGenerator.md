# FlightPathMovementGenerator

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`FlightPathMovementGenerator` drives the movement of `Player` objects along predefined taxi routes. It inherits from `MovementGeneratorMedium` and `PathMovementBase`, integrating into the core motion system to handle temporal progression, spatial updates, and event triggers associated with flying between nodes. It holds a pointer to a `TaxiPathNodeList const*` provided at construction, ensuring the route definition remains immutable during flight.

## Member-by-Member Behavior

### Initialization and Lifecycle

**`FlightPathMovementGenerator`**
The constructor accepts a constant reference to a `TaxiPathNodeList` and an optional starting node index (defaulting to 0). It initializes the base class member `i_path` to point to the provided list and sets `i_currentNode` to the specified start index. It is instantiated by `Creature.MotionMaster/MoveTaxiFlight` and `Creature.MotionMaster/MoveTaxiFlight#2`.

### Movement Logic and State Queries

**`HasArrived`**
A simple predicate checking if `i_currentNode` has reached or exceeded the size of `i_path`. This signals to the caller that the flight route is complete.

**`GetPath`**
Returns a constant reference to the underlying `TaxiPathNodeList`. This allows external units, such as `Player.Main/TaxiStepFinished`, to inspect the route details.

**`SkipCurrentNode`**
Increments `i_currentNode` by one. This is used by `Player.Main/TaxiStepFinished` to bypass the current node, likely in scenarios where the player has already arrived at a node via teleportation or manual intervention.

### Type Identification

**`GetMovementGeneratorType`**
Returns `FLIGHT_MOTION_TYPE`, identifying this generator to the broader motion system.

## Cross-Unit Boundaries

### Called By: `Creature.MotionMaster/MoveTaxiFlight` and `Creature.MotionMaster/MoveTaxiFlight#2`
Although the generator operates on a `Player`, the instantiation is driven by the `Creature.MotionMaster` unit. This suggests a unified motion management system where both creatures and players share similar movement infrastructure. The `MotionMaster` likely creates the `FlightPathMovementGenerator` with the appropriate `TaxiPathNodeList` and attaches it to the player's motion stack.

### Called By: `Player.Main/TaxiStepFinished`
This unit interacts with the generator after a flight step completes. It calls `GetPath` to inspect the route and `SkipCurrentNode` to advance the state. This collaboration ensures that the player's logical state is synchronized with the movement generator's internal node tracking.

## Data Model

This unit does not directly interact with database tables. It operates entirely on in-memory data structures (`TaxiPathNodeList`, `TaxiPathNodeEntry`) that are presumably loaded from the database by other components before being passed to the generator.

## Notable Implementation Details

1.  **Immutable Path Reference:** The generator stores a pointer to a `const` `TaxiPathNodeList`. This design choice prevents accidental modification of the route during flight.
2.  **Template Base Class Integration:** `FlightPathMovementGenerator` inherits from `PathMovementBase<Player, TaxiPathNodeList const*>`. This base class provides common functionality for path-based movement.
3.  **Node Skipping Mechanism:** The presence of `SkipCurrentNode` and its usage by `Player.Main/TaxiStepFinished` highlights a need for manual state adjustment, likely due to the asynchronous nature of networked games.

## Member Reference

**FlightPathMovementGenerator**: Constructor that initializes the generator with a constant reference to a `TaxiPathNodeList` and an optional starting node index. Sets `i_path` and `i_currentNode` accordingly. Instantiated by `Creature.MotionMaster/MoveTaxiFlight` and `Creature.MotionMaster/MoveTaxiFlight#2`.

**GetMovementGeneratorType**: Returns `FLIGHT_MOTION_TYPE`, identifying the generator type to the motion system.

**GetPath**: Returns a constant reference to the underlying `TaxiPathNodeList`. Called by `Player.Main/TaxiStepFinished` to inspect the route.

**HasArrived**: Checks if `i_currentNode` has reached or exceeded the size of `i_path`, indicating the flight is complete.

**SkipCurrentNode**: Increments `i_currentNode` by one. Called by `Player.Main/TaxiStepFinished` to bypass the current node, likely for synchronization purposes.

---

<!-- machine-true, projected from graph.json -->

## Map — FlightPathMovementGenerator

*Source:* WaypointMovementGenerator.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| FlightPathMovementGenerator | ctor | — | Creature.MotionMaster/MoveTaxiFlight, Creature.MotionMaster/MoveTaxiFlight#2 | — |
| GetMovementGeneratorType | method | — | — | — |
| GetPath | method | — | Player.Main/TaxiStepFinished | — |
| HasArrived | method | — | — | — |
| SkipCurrentNode | method | — | Player.Main/TaxiStepFinished | — |
