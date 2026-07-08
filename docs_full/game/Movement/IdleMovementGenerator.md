<!-- provenance: verbose -->
# IdleMovementGenerator

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# IdleMovementGenerator

**Purpose & Responsibilities**

This unit defines three `MovementGenerator` subclasses for handling transient creature states: idling, distraction, and assistance-based distraction. These generators temporarily override standard movement logic. The unit contains no database interactions.

1.  **`IdleMovementGenerator`**: A passive, no-op generator representing a static state. It remains active indefinitely (`Update` always returns `true`) and performs no actions. A global static instance `si_idleMovement` is provided for shared reuse.
2.  **`DistractMovementGenerator`**: Manages a timed "distracted" state. Initialization forces the unit to stand and sets the `UNIT_STATE_DISTRACTED` flag. The generator runs for a fixed duration (`m_timer`). Finalization restores the creature’s facing to its home position and clears the distracted state.
3.  **`AssistanceDistractMovementGenerator`**: Inherits from `DistractMovementGenerator` but overrides `Finalize` to re-engage combat. Instead of restoring facing, it checks if the unit has a victim and is alive; if so, it stops the current attack and commands the AI to attack the victim again.

## Member-by-Member Behavior

### IdleMovementGenerator

All methods are trivial stubs or simple return values, reflecting its role as a passive placeholder.

*   **`Initialize`**, **`Finalize`**, **`Interrupt`**: Empty inline methods defined in the header.
*   **`Reset`**: Empty method defined in the `.cpp` file.
*   **`Update`**: Inline method that always returns `true`, indicating the generator stays active.
*   **`GetMovementGeneratorType`**: Inline method returning `IDLE_MOTION_TYPE`.

### DistractMovementGenerator

Handles the lifecycle of a timed distraction.

*   **`Initialize`**: Checks if the owner is standing via `Unit.Main/IsStandingUp`; if not, forces standing via `Unit.Main/SetStandState`. Adds `UNIT_STATE_DISTRACTED` via `Unit.Main/AddUnitState`.
*   **`Finalize`**: If the owner is a `Creature` (checked via `Object/GetTypeId` and `Object/ToCreature`), it retrieves the home orientation via `Creature.Main/GetHomePositionO` and sets facing via `Unit.Main/SetFacingTo`. Finally, it clears `UNIT_STATE_DISTRACTED` via `Unit.Main/ClearUnitState`.
*   **`Reset`**: Calls `Initialize` to restart the state.
*   **`Interrupt`**: Empty method. State cleanup occurs in `Finalize`.
*   **`Update`**: Decrements `m_timer` by `time_diff`. Returns `false` if `time_diff` exceeds the remaining timer (expired), otherwise `true`.
*   **`GetMovementGeneratorType`**: Inline method returning `DISTRACT_MOTION_TYPE`.

### AssistanceDistractMovementGenerator

Overrides finalization to resume combat.

*   **`Finalize`**: Clears `UNIT_STATE_DISTRACTED` via `Unit.Main/ClearUnitState`. If `Unit.Main/GetVictim` returns a target and `Unit.Main/IsAlive` is true, it calls `Unit.Main/AttackStop(true)` and then `CreatureAI/AttackStart(victim)` via `Creature.Main/AI` to re-engage. This override bypasses the parent's facing restoration logic.
*   **`GetMovementGeneratorType`**: Inline method returning `ASSISTANCE_DISTRACT_MOTION_TYPE`.

## Cross-Unit Boundaries

*   **`DistractMovementGenerator::Initialize`** calls `Unit.Main/IsStandingUp`, `Unit.Main/SetStandState`, and `Unit.Main/AddUnitState` to set posture and state flags.
*   **`DistractMovementGenerator::Finalize`** calls `Object/GetTypeId`, `Object/ToCreature`, `Creature.Main/GetHomePositionO`, `Unit.Main/SetFacingTo`, and `Unit.Main/ClearUnitState` to restore orientation and clear flags.
*   **`AssistanceDistractMovementGenerator::Finalize`** calls `Unit.Main/ClearUnitState`, `Unit.Main/GetVictim`, `Unit.Main/IsAlive`, `Unit.Main/AttackStop`, `Creature.Main/AI`, and `CreatureAI/AttackStart` to resume combat.

## Data Model

This unit does not interact with any database tables.

## Notable Implementation Details

*   **Static Instance**: `IdleMovementGenerator` exposes a global `si_idleMovement` instance, suggesting it is intended for shared use rather than per-creature instantiation.
*   **Timer Expiration**: `DistractMovementGenerator::Update` returns `false` immediately if `time_diff > m_timer`, signaling expiration before decrementing.
*   **Unsafe Cast in Assistance**: `AssistanceDistractMovementGenerator::Finalize` casts `unit` to `Creature*` directly (`((Creature*)&unit)`) without type checking, assuming the caller guarantees a creature context. It also skips the parent's facing restoration.
*   **Empty Methods**: `IdleMovementGenerator` methods are intentionally empty to serve as a no-op base.

## Member Reference

**Reset#2**: Empty method in `IdleMovementGenerator` (cpp).
**Initialize#2**: Empty inline method in `IdleMovementGenerator` (h).
**Finalize#3**: Empty inline method in `IdleMovementGenerator` (h).
**Initialize**: Forces stand and sets distracted state in `DistractMovementGenerator`. Calls `Unit.Main/IsStandingUp`, `Unit.Main/SetStandState`, `Unit.Main/AddUnitState`.
**Interrupt#2**: Empty inline method in `IdleMovementGenerator` (h).
**Update#2**: Inline method in `IdleMovementGenerator` returning `true`.
**GetMovementGeneratorType**: Inline method in `IdleMovementGenerator` returning `IDLE_MOTION_TYPE`.
**Finalize#2**: Restores facing and clears distracted state in `DistractMovementGenerator`. Calls `Creature.Main/GetHomePositionO`, `Object/GetTypeId`, `Object/ToCreature`, `Unit.Main/ClearUnitState`, `Unit.Main/SetFacingTo`.
**Reset**: Calls `Initialize` in `DistractMovementGenerator`.
**Interrupt**: Empty method in `DistractMovementGenerator`.
**Update**: Decrements timer and checks expiration in `DistractMovementGenerator`.
**Finalize**: Clears distracted state and re-engages combat in `AssistanceDistractMovementGenerator`. Calls `Creature.Main/AI`, `CreatureAI/AttackStart`, `Unit.Main/AttackStop`, `Unit.Main/ClearUnitState`, `Unit.Main/GetVictim`, `Unit.Main/IsAlive`.

---

<!-- machine-true, projected from graph.json -->

## Map — IdleMovementGenerator

*Source:* IdleMovementGenerator.cpp, IdleMovementGenerator.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| Reset#2 | method | — | — | — |
| Initialize#2 | method | — | — | — |
| Finalize#3 | method | — | — | — |
| Initialize | method | Unit.Main/AddUnitState, Unit.Main/IsStandingUp, Unit.Main/SetStandState | — | — |
| Interrupt#2 | method | — | — | — |
| Update#2 | method | — | — | — |
| GetMovementGeneratorType | method | — | — | — |
| Finalize#2 | method | Creature.Main/GetHomePositionO, Object/GetTypeId, Object/ToCreature, Unit.Main/ClearUnitState, Unit.Main/SetFacingTo | — | — |
| Reset | method | — | — | — |
| Interrupt | method | — | — | — |
| Update | method | — | — | — |
| Finalize | method | Creature.Main/AI, CreatureAI/AttackStart, Unit.Main/AttackStop, Unit.Main/ClearUnitState, Unit.Main/GetVictim, Unit.Main/IsAlive | — | — |
