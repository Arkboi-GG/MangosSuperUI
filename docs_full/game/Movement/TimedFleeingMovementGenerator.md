# TimedFleeingMovementGenerator

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# TimedFleeingMovementGenerator

**TimedFleeingMovementGenerator** is a specialized movement generator for `Creature` entities that implements a time-bound fleeing behavior. It inherits from `FleeingMovementGenerator<Creature>`, providing the core logic for moving away from a specific threat (`i_frightGuid`), while adding a duration constraint. The creature will flee from the specified target until the allotted time expires, at which point the movement generator is expected to finalize and allow other behaviors to take over.

This unit acts as a wrapper around the generic `FleeingMovementGenerator`, specializing it for timed scenarios often used in combat AI or scripted events where a creature needs to retreat for a specific period before resuming normal activity or dying.

## Member-by-Member Behavior

### Construction and Initialization
*   **`TimedFleeingMovementGenerator`**: The constructor accepts two arguments: an `ObjectGuid` representing the entity to flee from (`fright`) and a `uint32` representing the total duration of the flee behavior (`time`). It initializes the base `FleeingMovementGenerator` with the threat GUID and sets its internal `i_totalFleeTime` tracker. This establishes the context for the fleeing behavior: who to run from and for how long.

### Core Movement Logic
*   **`GetMovementGeneratorType`**: Returns `TIMED_FLEEING_MOTION_TYPE`. This identifier allows the movement system to distinguish this specific generator from other fleeing variants (like indefinite fleeing) or other movement types (like following or wandering). This is crucial for debugging and state management within the broader movement framework.

## Cross-Unit Boundaries

*   **Called by `Creature.MotionMaster/MoveFleeing`**: The `TimedFleeingMovementGenerator` is instantiated by the `MotionMaster` component of a `Creature` object, specifically via the `MoveFleeing` interface. This indicates that the decision to initiate a timed flee originates from the creature's AI or script logic, which then delegates the actual pathfinding and movement execution to this generator. The `MotionMaster` passes the threat GUID and duration to the constructor.
*   **Inherits from `FleeingMovementGenerator<Creature>`**: While not a "call" in the traditional sense, this unit relies heavily on its parent class for the actual mechanics of calculating escape vectors, updating position, and handling interruptions. The `TimedFleeingMovementGenerator` overrides key methods like `Update`, `Initialize`, and `Finalize` to inject the time-limiting logic, but the spatial calculations remain in the parent.

## Data Model

This unit does not interact directly with any database tables. Its state is entirely transient, held in memory during the creature's lifetime, and derived from runtime parameters (threat GUID and duration) passed during construction.

## Notable Implementation Details

*   **Time Tracking**: The `i_totalFleeTime` member is a `TimeTracker`. This suggests that the `Update` method (inherited or overridden) checks this tracker to determine if the flee duration has elapsed. Once the time is up, the generator likely signals completion, allowing the `MotionMaster` to remove it from the creature's movement queue.
*   **Specialization for Creatures**: By inheriting from `FleeingMovementGenerator<Creature>`, this generator is strictly tied to `Creature` objects. It cannot be used for players or other unit types without significant refactoring of the base template.
*   **Override Strategy**: The class overrides `Initialize`, `Finalize`, and `Update`. This implies that the timing logic is integrated into these lifecycle hooks. For instance, `Initialize` might start the timer, `Update` checks it, and `Finalize` cleans up when the time is done or the generator is interrupted.

## Member Reference

**TimedFleeingMovementGenerator**
Constructor that initializes the timed fleeing behavior. Takes the GUID of the entity to flee from and the duration of the flee. Sets up the base `FleeingMovementGenerator` and the internal time tracker.

**GetMovementGeneratorType**
Returns the unique identifier `TIMED_FLEEING_MOTION_TYPE` for this movement generator, enabling the movement system to categorize and manage it correctly.

---

<!-- machine-true, projected from graph.json -->

## Map — TimedFleeingMovementGenerator

*Source:* FleeingMovementGenerator.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| TimedFleeingMovementGenerator | ctor | — | Creature.MotionMaster/MoveFleeing | — |
| GetMovementGeneratorType | method | — | — | — |
