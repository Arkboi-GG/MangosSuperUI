# AssistanceMovementGenerator

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# AssistanceMovementGenerator

## Purpose & Responsibilities

`AssistanceMovementGenerator` is a specialized movement generator for `Creature` objects, designed to move a unit to a specific static coordinate to assist an ally. It inherits from `PointMovementGenerator<Creature>`, reusing the base class’s pathfinding, interpolation, and update logic. This unit’s sole responsibility is to provide the correct semantic type (`ASSISTANCE_MOTION_TYPE`) and override the lifecycle hooks (`Initialize`, `Finalize`) to manage creature state transitions associated with seeking assistance. It does not implement dynamic tracking; the destination is fixed at construction time.

## Member-by-Member Behavior

### Construction and Type Identification

**`AssistanceMovementGenerator`**  
Constructs the generator with target coordinates (`_x`, `_y`, `_z`). It delegates to `PointMovementGenerator<Creature>`, passing ID `0`, the coordinates, and a `true` flag (which maps to the `options` parameter in the base class, typically enabling run mode/pathfinding). The base class stores these values for subsequent movement calculations.

**`GetMovementGeneratorType`**  
Returns `ASSISTANCE_MOTION_TYPE`. This allows the `MotionMaster` and debugging systems to distinguish assistance movement from other point-based movements (e.g., random walking or fleeing).

### Lifecycle Management

**`Initialize`**  
Overrides the base `Initialize` to prepare the `Creature` for movement. While the specific implementation details reside in the corresponding `.cpp` file (not provided here), this hook is responsible for setting up the creature’s state before the first movement tick, such as ensuring the creature is in combat or orienting it toward the destination.

**`Finalize`**  
Overrides the base `Finalize` to clean up state when the movement ends (destination reached, interrupted, or creature dies). This ensures the creature is properly returned to its previous state, allowing its AI to resume normal behavior.

## Cross-Unit Boundaries

### Called By: `Creature.MotionMaster/MoveSeekAssistance`

The `AssistanceMovementGenerator` is instantiated by `Creature::MoveSeekAssistance` (or the `MotionMaster` subsystem acting on behalf of the `Creature`).

*   **Direction:** `Creature` → `AssistanceMovementGenerator`
*   **Data Crossing Boundary:** The `Creature` calculates the destination coordinates (where it needs to be to assist) and passes them to the constructor.
*   **Why:** The `Creature` class makes the high-level decision to seek assistance, while the `MotionMaster` delegates the physical movement execution to this specialized generator.

### Calls Out: None

This unit does not directly call any external units. It relies entirely on `PointMovementGenerator<Creature>` for all movement mechanics, including pathfinding and position updates.

## Data Model

This unit does not interact with any database tables. It operates exclusively on runtime memory structures.

## Notable Implementation Details

1.  **Static Destination:** Unlike `FollowMovementGenerator`, `AssistanceMovementGenerator` does not override `Update`. The destination is fixed at construction. If the ally moves after the generator is created, the creature will continue to the original coordinates.
2.  **Inheritance:** By inheriting from `PointMovementGenerator<Creature>`, it avoids reimplementing complex movement logic. The `true` flag passed to the base constructor likely enables running mode, reflecting the urgency of assistance.
3.  **Template Specialization:** The base class is templated, but this unit hardcodes `T` to `Creature`, restricting its use to NPCs.

## Member Reference

**`AssistanceMovementGenerator`**  
Constructor accepting `_x`, `_y`, `_z` coordinates. Delegates to `PointMovementGenerator<Creature>` with ID `0`, the coordinates, and a `true` flag (enabling run mode/pathfinding options).

**`GetMovementGeneratorType`**  
Returns `ASSISTANCE_MOTION_TYPE`, identifying the movement intent to the engine.

---

<!-- machine-true, projected from graph.json -->

## Map — AssistanceMovementGenerator

*Source:* PointMovementGenerator.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| AssistanceMovementGenerator | ctor | — | Creature.MotionMaster/MoveSeekAssistance | — |
| GetMovementGeneratorType | method | — | — | — |
