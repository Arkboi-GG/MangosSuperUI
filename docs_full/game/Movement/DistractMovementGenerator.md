# DistractMovementGenerator

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# DistractMovementGenerator

**Purpose & Responsibilities**

`DistractMovementGenerator` is a specialized movement generator within the `wowvmangos` engine responsible for controlling the behavior of a `Creature` when it enters a "distracted" state. In the context of the game mechanics represented by this codebase, distraction typically occurs when a creature is attacked or targeted by a player or another entity while it is engaged in other activities (such as idle wandering or patrolling). The primary responsibility of this unit is to manage the temporal aspect of this state: it ensures the creature remains in the distracted mode for a specific duration (`m_timer`) before potentially reverting to its previous behavior or transitioning to a new state. It acts as a state machine component within the broader `MovementGenerator` hierarchy, providing the interface required by the `MotionMaster` system to initialize, update, interrupt, and finalize the distraction logic.

This unit is defined in `IdleMovementGenerator.h` alongside `IdleMovementGenerator` and `AssistanceDistractMovementGenerator`. While they share a header file, `DistractMovementGenerator` is a distinct class with its own state (`m_timer`) and lifecycle methods. It does not handle pathfinding or direct position updates itself; rather, it signals its active status and manages its internal timer, relying on the `MotionMaster` and potentially other generators or AI components to execute the actual physical movement or combat actions associated with being distracted.

## Member-by-Member Behavior

The behavior of `DistractMovementGenerator` is encapsulated in two members listed in the MAP: its constructor and its type identifier. The remaining methods declared in the header (`Initialize`, `Finalize`, `Interrupt`, `Reset`, `Update`) are part of the class interface but are not detailed in the provided MAP as having external callers or callees within this specific documentation scope, nor is their implementation provided in the source snippet (only declarations are visible for most, except the constructor and `GetMovementGeneratorType`). However, based on the class structure and standard patterns in such engines:

1.  **State Initialization**: The constructor sets the duration of the distraction.
2.  **Type Identification**: The `GetMovementGeneratorType` method allows the `MotionMaster` to identify this generator's role.
3.  **Lifecycle Management**: Methods like `Initialize`, `Update`, `Interrupt`, and `Finalize` manage the transition into, during, and out of the distracted state. Although their implementations are not fully visible in the provided source snippet (only declarations in the header), their signatures indicate they interact with the `Unit` object (the creature) and time deltas.

### Constructor: `DistractMovementGenerator`

*   **Kind**: Constructor
*   **Behavior**: Initializes the `DistractMovementGenerator` instance with a specific timer value. This `timer` (stored in `m_timer`) dictates how long the creature should remain in the distracted state. The constructor is `explicit`, preventing implicit conversions.
*   **Cross-Unit Boundary**: Called by `Creature.MotionMaster/MoveDistract`. This indicates that when a `Creature` needs to enter a distracted state, its `MotionMaster` component creates an instance of `DistractMovementGenerator` with a calculated or predefined duration. The `MotionMaster` is the central manager for all movement-related states of a `Unit`.

### Method: `GetMovementGeneratorType`

*   **Kind**: Method
*   **Behavior**: Returns the constant `DISTRACT_MOTION_TYPE`. This enum value identifies the generator to the `MotionMaster` and other parts of the system. It is a pure virtual requirement of the `MovementGenerator` base class.
*   **Cross-Unit Boundary**: No external callers or callees are listed in the MAP. This method is likely called internally by the `MotionMaster` or other movement-related systems to determine the current state of the creature's movement logic.

*(Note: The methods `Initialize`, `Finalize`, `Interrupt`, `Reset`, and `Update` are declared in the header but are not included in the MAP's "Member" column. Therefore, per the instructions, they are not described in detail here as "this unit's own behavior" in the context of the MAP, although they are part of the class definition. The MAP focuses on the constructor and the type getter as the key entry points and identifiers.)*

## Cross-Unit Boundaries

The interaction of `DistractMovementGenerator` with the rest of the system is primarily mediated through the `MotionMaster` component of a `Creature`.

*   **Called By: `Creature.MotionMaster/MoveDistract`**
    *   **Direction**: Inbound (to `DistractMovementGenerator`)
    *   **Collaboration**: When a `Creature` is triggered to become distracted (e.g., by taking damage from a non-combatant source, or by a specific spell effect), the `MotionMaster` associated with that `Creature` invokes the `MoveDistract` routine. This routine constructs a new `DistractMovementGenerator` instance, passing a `uint32` timer value. This generator is then pushed onto the `MotionMaster`'s stack of movement generators, becoming the active controller for the creature's movement state. The `MotionMaster` relies on this generator to report its type and manage its own lifecycle via the `Update`, `Interrupt`, etc., methods.

*   **Calls Out: None**
    *   The MAP indicates that `DistractMovementGenerator` does not directly call into other units. Its logic is self-contained regarding state management (timer decrementing, state flags). Any actual movement execution or AI decision-making triggered by the distracted state is handled by the `MotionMaster` or the `Creature`'s AI, which react to the presence of this generator on the stack.

## Data Model

`DistractMovementGenerator` does not interact directly with any database tables. It operates entirely in memory, managing transient state for a `Creature` instance during runtime. The `Tables` column in the MAP is empty, confirming no SQL queries or table accesses are performed by this unit.

## Notable Implementation Details

1.  **Timer-Based State**: The core logic revolves around `m_timer`, a `uint32` member variable. This suggests that the distraction state is time-bound. The `Update` method (declared but not implemented in the snippet) would typically decrement this timer using the `time_diff` parameter. When the timer reaches zero, the generator would likely signal completion, allowing the `MotionMaster` to pop it from the stack and revert to the previous movement state.
2.  **Inheritance Hierarchy**: `DistractMovementGenerator` inherits from `MovementGenerator`. This base class defines the contract for all movement behaviors (idle, follow, point, etc.). `DistractMovementGenerator` fulfills this contract by implementing the required virtual methods.
3.  **Sibling Classes**: The header also defines `IdleMovementGenerator` and `AssistanceDistractMovementGenerator`. `AssistanceDistractMovementGenerator` inherits from `DistractMovementGenerator`, suggesting a specialized form of distraction (perhaps related to assisting allies) that reuses the timer logic but overrides `GetMovementGeneratorType` to return `ASSISTANCE_DISTRACT_MOTION_TYPE` and provides its own `Finalize` logic. This highlights a design pattern where common distraction logic is factored out into the base `DistractMovementGenerator`.
4.  **No Direct Movement Calculation**: Unlike some movement generators that might calculate paths or velocities, `DistractMovementGenerator` appears to be a state holder. It doesn't compute *where* to move, but rather *that* the creature is in a distracted state for a certain duration. The actual movement response to distraction (e.g., stopping, fleeing, attacking) is likely determined by the `Creature`'s AI or the `MotionMaster`'s handling of the `DISTRACT_MOTION_TYPE`.
5.  **Explicit Constructor**: The use of `explicit` prevents accidental construction from integer literals, ensuring that the timer value is intentionally passed.

## Member Reference

**DistractMovementGenerator**  
Constructor that initializes the `m_timer` member with the provided `uint32` value. It is called by `Creature.MotionMaster/MoveDistract` when a creature needs to enter a distracted state for a specified duration.

**GetMovementGeneratorType**  
Method that returns the constant `DISTRACT_MOTION_TYPE`. This identifies the generator's role to the `MotionMaster` and other systems. It has no external callers or callees listed in the MAP.

---

<!-- machine-true, projected from graph.json -->

## Map — DistractMovementGenerator

*Source:* IdleMovementGenerator.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| DistractMovementGenerator | ctor | — | Creature.MotionMaster/MoveDistract | — |
| GetMovementGeneratorType | method | — | — | — |
