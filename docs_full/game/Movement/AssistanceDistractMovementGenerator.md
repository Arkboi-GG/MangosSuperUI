# AssistanceDistractMovementGenerator

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# AssistanceDistractMovementGenerator

**Purpose & Responsibilities**

`AssistanceDistractMovementGenerator` is a specialized movement generator within the `wowvmangos` engine that dictates how a creature moves when it is seeking assistance from allies while simultaneously attempting to distract or evade threats. It inherits from `DistractMovementGenerator`, reusing the core logic for timed distraction behaviors, but overrides specific lifecycle methods to handle the nuances of "assistance-seeking" states.

This unit is part of the broader movement generation system, where different generators (Idle, Distract, Assistance, etc.) are swapped in and out of a creature's `MotionMaster` to control its AI-driven locomotion. Specifically, this generator is instantiated when a creature needs to move towards an ally for help (`MoveSeekAssistanceDistract`) while maintaining a distraction profile.

**Member-by-Member Behavior**

The unit consists of two primary members defined in `IdleMovementGenerator.h`:

1.  **Constructor (`AssistanceDistractMovementGenerator`)**: Initializes the movement generator with a specific timer duration. It delegates initialization to its parent class, `DistractMovementGenerator`, passing the timer value. This timer likely controls the duration of the distraction phase before the creature might revert to another state or complete its assistance request.
2.  **`GetMovementGeneratorType`**: Returns the enum value `ASSISTANCE_DISTRACT_MOTION_TYPE`. This identifier allows the rest of the engine (particularly the `MotionMaster` and AI systems) to recognize the current movement state of the creature. This is crucial for debugging, logging, and conditional logic elsewhere in the codebase that needs to know *why* a creature is moving.

**Cross-Unit Boundaries**

*   **Called by `Creature.MotionMaster/MoveSeekAssistanceDistract`**: The `Creature` class (via its `MotionMaster` component) instantiates this generator when the AI decides the creature should seek assistance while distracting enemies. The `MoveSeekAssistanceDistract` method in `Creature` creates an instance of `AssistanceDistractMovementGenerator` and pushes it onto the motion stack. This establishes the direction of control: the AI/Creature logic triggers the movement, and this generator executes the low-level movement updates.
*   **Inherits from `DistractMovementGenerator`**: While not a "call out" in the traditional sense, `AssistanceDistractMovementGenerator` relies heavily on `DistractMovementGenerator` for its core functionality (`Initialize`, `Finalize`, `Interrupt`, `Reset`, `Update`). The only method it overrides besides the type getter is `Finalize`. This means the actual movement calculation, pathfinding, and timer management are handled by the parent class. The specialization here is minimal, focusing on identity (`GetMovementGeneratorType`) and cleanup (`Finalize`).

**Data Model**

This unit does not interact directly with any database tables. All state is held in memory within the object instance (specifically, the `m_timer` inherited from `DistractMovementGenerator`).

**Notable Implementation Details**

*   **Minimal Override Strategy**: `AssistanceDistractMovementGenerator` is a thin wrapper around `DistractMovementGenerator`. It does not override `Initialize`, `Update`, `Interrupt`, or `Reset`. This implies that the mechanical behavior of "distracting while seeking assistance" is identical to standard "distracting" behavior, except for the semantic label (`ASSISTANCE_DISTRACT_MOTION_TYPE`) and potentially custom cleanup logic in `Finalize`.
*   **`Finalize` Override**: The class declares `void Finalize(Unit& unit);` but does not define it in the header. The implementation is presumably in a corresponding `.cpp` file (not provided in the source snippet, but implied by the declaration). This suggests that when the assistance-distract movement ends, there is specific cleanup required that differs from the base `DistractMovementGenerator::Finalize`. A maintainer must check the `.cpp` file to understand what state is cleared or what notifications are sent upon completion.
*   **Timer Dependency**: The constructor requires a `uint32 timer`. This value is passed to the parent `DistractMovementGenerator`. The behavior of the movement is entirely dependent on this timer, which likely dictates how long the creature will continue to distract before stopping or changing state. Incorrect timer values could lead to creatures getting stuck in distraction loops or ending them prematurely.

## Member Reference

**AssistanceDistractMovementGenerator**
Constructor that initializes the movement generator with a specified timer duration. It delegates to the `DistractMovementGenerator` constructor, inheriting all core distraction logic. This member is called by `Creature.MotionMaster/MoveSeekAssistanceDistract` when the creature's AI determines it needs to seek help while distracting enemies.

**GetMovementGeneratorType**
Returns the constant `ASSISTANCE_DISTRACT_MOTION_TYPE`. This method identifies the current movement state to the engine, allowing other systems to distinguish this specific behavior from other distraction or movement types. It is a pure virtual requirement of the `MovementGenerator` interface.

---

<!-- machine-true, projected from graph.json -->

## Map — AssistanceDistractMovementGenerator

*Source:* IdleMovementGenerator.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| AssistanceDistractMovementGenerator | ctor | — | Creature.MotionMaster/MoveSeekAssistanceDistract | — |
| GetMovementGeneratorType | method | — | — | — |
