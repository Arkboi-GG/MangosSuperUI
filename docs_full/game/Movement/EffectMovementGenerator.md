# EffectMovementGenerator

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`EffectMovementGenerator` is a minimal, passive movement generator within the MaNGOS/WoWVMaNGOS engine. Its primary responsibility is **not** to drive active locomotion, but to occupy the movement generator slot of a `Unit` to prevent other, potentially conflicting movement generators (such as AI-driven patrol or chase routines) from interrupting a spell or effect that is currently applying forced movement or positional constraints.

As noted in the source comments, this class "does almost nothing" regarding pathfinding or velocity calculation. It acts as a placeholder or lock. It is designed to be lightweight and reusable for effects like charges or other spell-induced movements where the actual motion logic might be handled elsewhere (e.g., by the spell system itself or a different subsystem), but the unit's movement state must remain stable and uninterruptible by standard AI behaviors.

It inherits from `MovementGenerator`, the base class for all movement logic, but unlike its siblings (`PointMovementGenerator`, `ChargeMovementGenerator`), it does not inherit from `MovementGeneratorMedium`. This indicates it lacks the intermediate state management (like path recalculation buffers or speed tracking) required for complex autonomous navigation.

## Member-by-Member Behavior

The `EffectMovementGenerator` implements the standard `MovementGenerator` interface with minimal overhead.

### Initialization and Lifecycle
*   **`EffectMovementGenerator` (Constructor)**: Accepts a `uint32 Id`. This ID likely corresponds to the spell ID or effect identifier that triggered this movement state. It stores this ID in `m_id`. No initialization of movement paths or velocities occurs here.
*   **`Initialize`**: Takes a `Unit&` reference. The body is empty (`{}`). It performs no setup, does not calculate a destination, and does not set initial velocities. It simply acknowledges the unit it is attached to.
*   **`Finalize`**: Takes a `Unit&` reference. While declared in the header, the implementation is not shown in the provided snippet (likely defined in the corresponding `.cpp` file). Based on the pattern of other trivial generators, it likely cleans up any references held by the unit, though `EffectMovementGenerator` holds no internal state other than `m_id`.
*   **`Interrupt`**: Takes a `Unit&` reference. The body is empty (`{}`). This is critical: it explicitly does nothing when interrupted. This reinforces its role as a non-disruptive placeholder. It does not stop the unit's physical movement (which is likely controlled by the spell effect itself), nor does it reset internal state.
*   **`Reset`**: Takes a `Unit&` reference. The body is empty (`{}`). Similar to `Interrupt`, it performs no reset logic.

### State Updates and Identification
*   **`Update`**: Takes a `Unit&` and a time difference (`uint32 const&`). Returns a `bool`. The implementation is not shown in the header (defined in `.cpp`). Given the class's passive nature, this method likely returns `true` (indicating the movement generator is still valid/active) or `false` (if the effect has expired), but it does not perform any position updates, pathfinding, or velocity calculations itself. It relies on the caller (the spell system or unit update loop) to determine if the effect is still active.
*   **`GetMovementGeneratorType`**: Returns `EFFECT_MOTION_TYPE`. This constant identifies this generator to the engine as a spell/effect-driven movement type, distinguishing it from AI-driven (`POINT_MOTION_TYPE`, `CHARGE_MOTION_TYPE`) or follower-driven movements.

## Cross-Unit Boundaries

The MAP indicates that `EffectMovementGenerator` has **no outgoing calls** to other units and is **not called by** other units in the context of cross-file dependencies listed. However, its integration is implicit through the `MovementGenerator` hierarchy:

1.  **Inheritance from `MovementGenerator`**: It implements the abstract interface defined in `MovementGenerator.h`. This allows the core `Unit` class (specifically `Unit::MoveTo` or similar movement management functions) to treat it polymorphically. The `Unit` class calls `Initialize`, `Update`, `Interrupt`, etc., on whatever `MovementGenerator` instance is currently active.
2.  **Usage Context**: While not shown in the MAP's "Called by" column, `EffectMovementGenerator` instances are typically instantiated by spell handlers or effect processors when a spell applies a movement constraint. The `Unit` class then delegates movement updates to this generator. Because `Update` and `Interrupt` are empty, the `Unit`'s movement state remains unchanged by this generator, effectively "locking" the movement slot against replacement by AI generators that check for existing movement types.

## Data Model

This unit does not interact with any database tables. It operates entirely in memory, managing transient runtime state associated with a specific `Unit` object during a spell effect.

## Notable Implementation Details

1.  **Passive Design**: The most significant detail is the emptiness of `Initialize`, `Interrupt`, and `Reset`. This confirms that `EffectMovementGenerator` is not a driver of motion but a blocker of other drivers. If a unit has an `EffectMovementGenerator` active, the AI system (which typically uses `PointMovementGenerator` or `WaypointMovementGenerator`) will see that the unit is already moving under an "effect" and will not attempt to take over control.
2.  **ID Storage**: The constructor stores a `uint32 Id`. This is likely used for debugging or for the `Update` method (in the `.cpp` file) to verify if the specific spell effect is still valid. If the spell ends, the `Update` method likely returns `false`, causing the `Unit` to remove this generator and revert to its previous movement state or idle.
3.  **No Pathfinding**: Unlike `PointMovementGenerator` or `ChargeMovementGenerator`, this class does not include `PathFinder` or any coordinate storage (`m_x`, `m_y`, `m_z`). It assumes the destination and trajectory are managed externally (by the spell effect logic).
4.  **Template vs. Concrete Class**: `EffectMovementGenerator` is a concrete class inheriting from `MovementGenerator`, whereas `PointMovementGenerator` is a template class inheriting from `MovementGeneratorMedium`. This reflects the simpler, less flexible nature of effect-based movement compared to general-purpose point-to-point navigation.

## Member Reference

**EffectMovementGenerator**  
Constructor that initializes the generator with a `uint32 Id`. Stores this ID in the private member `m_id`. No other initialization occurs.

**Initialize**  
Method that takes a `Unit&` reference. The body is empty. It performs no setup or state initialization.

**Interrupt**  
Method that takes a `Unit&` reference. The body is empty. It performs no cleanup or state change upon interruption.

**Reset**  
Method that takes a `Unit&` reference. The body is empty. It performs no reset logic.

**GetMovementGeneratorType**  
Method that returns the constant `EFFECT_MOTION_TYPE`. This identifies the generator's type to the movement system.

---

<!-- machine-true, projected from graph.json -->

## Map — EffectMovementGenerator

*Source:* PointMovementGenerator.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| EffectMovementGenerator | ctor | — | — | — |
| Initialize | method | — | — | — |
| Interrupt | method | — | — | — |
| Reset | method | — | — | — |
| GetMovementGeneratorType | method | — | — | — |
