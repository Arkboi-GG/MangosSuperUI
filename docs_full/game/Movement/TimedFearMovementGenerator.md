# TimedFearMovementGenerator

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# TimedFearMovementGenerator

**Purpose & Responsibilities**

`TimedFearMovementGenerator` is a specialized movement generator for `Creature` objects that implements a "timed fear" behavior. Unlike standard fear effects which may persist until the effect wears off or the creature reaches safety, this generator forces the creature to flee for a specific, pre-determined duration (`i_totalFleeTime`). It inherits from `FearMovementGenerator<Creature>`, reusing the complex logic for calculating safe flee paths, avoiding obstacles, and maintaining distance from the threat source, while overriding the update loop to enforce the time limit.

This unit is part of the AI movement system in WoWVMaNGOS, specifically handling the `TIMED_FLEEING_MOTION_TYPE`. It ensures that creatures affected by timed fear spells move away from their attacker (`i_frightGuid`) until the timer expires, at which point the movement generator finalizes and stops.

## Member-by-Member Behavior

### Initialization and Lifecycle

*   **`TimedFearMovementGenerator(ObjectGuid fright, uint32 time)`**: The constructor initializes the base `FearMovementGenerator` with the GUID of the entity causing the fear (`fright`) and sets up the internal `TimeTracker` `i_totalFleeTime` with the specified duration. This duration dictates how long the creature will continue to flee regardless of distance or safety.

*   **`Initialize(Unit &)`**: Overrides the base class initialization. It likely calls the parent `Initialize` to set up initial flee points and timers, but may also reset or configure the total flee time tracker if not already done in the constructor. It prepares the creature for the fleeing motion.

*   **`Finalize(Unit &)`**: Overrides the base class finalization. This is called when the movement generator is removed or completes. It cleans up any state associated with the fear movement, such as stopping the flee animation or resetting movement flags. Crucially, for timed fear, this is also triggered when the `i_totalFleeTime` expires.

### Core Logic

*   **`Update(Unit &, uint32 const&)`**: This is the heart of the timed fear behavior. It overrides the base `Update` method. Its primary responsibility is to check if the `i_totalFleeTime` has elapsed.
    1.  If the time has expired, it calls `Finalize` on the owner unit to stop the fear movement and returns `false` to indicate the generator should be removed.
    2.  If the time has not expired, it delegates to the base `FearMovementGenerator::Update` to handle the actual pathfinding, movement execution, and avoidance logic. This ensures the creature still moves intelligently away from the threat while the timer runs down.

### Type Identification

*   **`GetMovementGeneratorType()`**: Returns `TIMED_FLEEING_MOTION_TYPE`. This constant identifies this specific generator type within the broader movement system, allowing the AI core to distinguish timed fear from regular fear (`FLEEING_MOTION_TYPE`) or other movement types.

## Cross-Unit Boundaries

*   **Calls Out**:
    *   **`FearMovementGenerator<Creature>` (Base Class)**: `TimedFearMovementGenerator` relies heavily on its base class for the complex mechanics of fear movement. Specifically, `Update` calls the base `Update` to perform pathfinding and movement updates. `Initialize` and `Finalize` also interact with the base class to ensure proper setup and teardown of the fear state. The base class handles interactions with the pathfinding system, collision detection, and target location calculation (`_setTargetLocation`, `_getPoint`).
    *   **`Unit`**: The `Update`, `Initialize`, and `Finalize` methods take a `Unit&` reference (specifically a `Creature` due to the template instantiation). They call methods on this `Unit` to get/set position, speed, orientation, and to trigger animations or state changes.

*   **Called By**:
    *   **AI System / Spell Effects**: While not explicitly shown in the MAP, `TimedFearMovementGenerator` instances are typically created and attached to a `Creature`'s movement manager by spell effects or AI routines that apply a "timed fear" debuff. The movement manager then calls `Initialize`, `Update`, and `Finalize` as part of its standard lifecycle.

## Data Model

This unit does not directly interact with any database tables. All state is held in memory within the object instance (`i_totalFleeTime`, `i_frightGuid`, etc.) and passed via parameters.

## Notable Implementation Details

*   **Inheritance Strategy**: The design cleanly separates the *duration* logic (in `TimedFearMovementGenerator`) from the *spatial/navigation* logic (in `FearMovementGenerator`). This avoids duplicating the complex pathfinding and avoidance code.
*   **Timer Management**: The use of `TimeTracker` for `i_totalFleeTime` allows for accurate tracking of the fear duration across multiple game ticks, accounting for variable frame rates or server load.
*   **Override Pattern**: The `Update` method's structure (check time -> finalize if done -> else call base) is a common pattern for time-limited behaviors in game engines. It ensures the base logic is only executed while the condition (time remaining) is true.
*   **Template Specialization**: `FearMovementGenerator` is templated on `T`, allowing it to work with different entity types (though here it's specialized for `Creature`). `TimedFearMovementGenerator` fixes this to `Creature`, simplifying its interface.

## Member Reference

**GetMovementGeneratorType**
Returns the constant `TIMED_FLEEING_MOTION_TYPE`, identifying this generator's type within the movement system. This allows the AI core to correctly categorize and manage the movement state.

---

<!-- machine-true, projected from graph.json -->

## Map — TimedFearMovementGenerator

*Source:* FearMovementGenerator.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GetMovementGeneratorType | method | — | — | — |
