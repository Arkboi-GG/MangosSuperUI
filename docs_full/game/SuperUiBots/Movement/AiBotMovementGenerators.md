<!-- provenance: verbose -->
# AiBotMovementGenerators

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# AiBotMovementGenerators

## Purpose & Responsibilities

`AiBotMovementGenerators` implements a specialized movement generator for AI-controlled bots (`Player` objects) that traverses pre-calculated paths smoothly. Unlike the standard `PointMovementGenerator`, which may trigger internal re-pathfinding for single destinations, `AiBotSmoothedPointMovementGenerator` consumes a complete `PointsArray` and feeds it directly to the spline engine via `MoveSplineInit::MovebyPath`. This ensures the bot follows the exact geometric curve defined by the path data without intermediate recalculations.

The unit also defines `AiBotMovementIssuer`, a minimal facade class. Its sole responsibility is to act as the sanctioned interface for attaching these custom generators to a bot's `MotionMaster`. Because `MotionMaster::Mutate()` is private, `AiBotMovementIssuer` is declared as a `friend` in the core `MotionMaster` header, allowing it to bypass access restrictions to install the movement generator while keeping the rest of the codebase decoupled from the core motion system's internals.

## Member-by-Member Behavior

### Path Initialization and Spline Configuration
**`AiBotSmoothedPointMovementGenerator::Initialize`** is the core execution entry point for this movement type. It prepares the bot's physical state and configures the movement spline before launching the motion.

1.  **State Preparation**: It checks if the unit is already moving via `Unit.Main/IsStopped`. If not stopped, it forces a stop using `Unit.Main/StopMoving` to prevent conflicting motion states. It then sets the unit's state flags to `UNIT_STATE_ROAMING | UNIT_STATE_ROAMING_MOVE` via `Unit.Main/AddUnitState`.
2.  **Spline Construction**: It creates a `Movement::MoveSplineInit` object, passing the unit and a debug identifier string.
3.  **Path Injection**: It calls `MoveSplineInit/MovebyPath`, injecting the stored `m_path` (`PointsArray`) directly into the spline engine. This bypasses internal pathfinding logic that might otherwise alter the trajectory.
4.  **Parameter Application**: It applies movement modifiers based on `m_options` and constructor arguments:
    *   **Velocity**: If `m_speed > 0.0f`, it sets velocity via `MoveSplineInit/SetVelocity`.
    *   **Locomotion Mode**: Toggles walking/running via `MoveSplineInit/SetWalk` based on `MOVE_WALK_MODE` and `MOVE_RUN_MODE` flags.
    *   **Flight/Fall**: Enables flying (`MoveSplineInit/SetFly`) or falling (`MoveSplineInit/SetFall`) if corresponding options are set.
    *   **Cyclic Movement**: Marks the path as cyclic via `MoveSplineInit/SetCyclic` if `MOVE_CYCLIC` is set.
    *   **Orientation**: If `m_o > -7.0f`, it sets the facing angle via `MoveSplineInit/SetFacing#2`.
5.  **Launch**: It calls `MoveSplineInit/Launch` to commit the configuration and begin movement.

### Movement Issuance
**`AiBotMovementIssuer::IssueSmoothedPath`** acts as the factory and installer for the movement generator.

1.  **Validation**: It returns immediately if the provided `path` is empty.
2.  **Installation**: It retrieves the bot's `MotionMaster` via `Unit.Main/GetMotionMaster` and calls `Creature.MotionMaster/Mutate`. This replaces the current top-level movement generator with a newly constructed `AiBotSmoothedPointMovementGenerator`, passing the ID, path, options, speed, and final orientation.

### Construction
**`AiBotSmoothedPointMovementGenerator`** (constructor) initializes the base class `PointMovementGenerator<Player>` and stores the path. It passes the coordinates of the last point in the path (or zeros if empty) to the base class, likely for legacy compatibility or initial position tracking. It stores the full `PointsArray` in `m_path` for use during `Initialize`.

## Cross-Unit Boundaries

*   **Caller: `AiBotAI.Movement`**
    *   **Members**: `MoveToDestination`, `StartNextPathChunk`
    *   **Interaction**: These methods in `AiBotAI.Movement` calculate the desired path and call `AiBotMovementIssuer::IssueSmoothedPath`. They provide the `PointsArray`, speed, and options, delegating the actual attachment of the movement generator to this unit.

*   **Callee: `MoveSplineInit`**
    *   **Members**: `Launch`, `MovebyPath`, `MoveSplineInit` (ctor), `SetCyclic`, `SetFacing#2`, `SetFall`, `SetFly`, `SetVelocity`, `SetWalk`
    *   **Interaction**: `AiBotSmoothedPointMovementGenerator::Initialize` uses these methods to configure the physical movement spline. `MovebyPath` is critical as it accepts the pre-calculated waypoint array.

*   **Callee: `Unit.Main`**
    *   **Members**: `AddUnitState`, `IsStopped`, `StopMoving`, `GetMotionMaster`
    *   **Interaction**: Used in `Initialize` to ensure the bot is in a valid state (stopping existing motion, setting roaming flags) and in `IssueSmoothedPath` to obtain the `MotionMaster` handle.

*   **Callee: `Creature.MotionMaster`**
    *   **Members**: `Mutate`
    *   **Interaction**: `AiBotMovementIssuer::IssueSmoothedPath` calls `Mutate` to replace the current movement generator. This is a privileged operation allowed only because `AiBotMovementIssuer` is a friend of `MotionMaster`.

## Data Model

This unit does not interact with any database tables. All path data and movement parameters are passed in-memory via C++ objects (`PointsArray`, floats, bitmasks).

## Notable Implementation Details

### Non-Virtual Base Class Methods
The header contains a critical warning ("LANDMINE") regarding inheritance from `PointMovementGenerator<Player>`. The base class methods `Interrupt`, `Reset`, and `Update` are **not virtual**. Only `Initialize`, `Finalize`, and `MovementInform` are virtual. Attempting to override `Interrupt`, `Reset`, or `Update` in `AiBotSmoothedPointMovementGenerator` would result in silent failures because the CRTP dispatch in `MovementGeneratorMedium` would call the base class versions instead. Maintainers must not add overrides for these non-virtual methods.

### Friend Class Privilege
`AiBotMovementIssuer` exists solely to exploit the `friend` relationship with `MotionMaster`. This design pattern isolates the dependency on the core engine's private API to a single, tiny class. All other parts of the AI system interact with `AiBotMovementIssuer`, not `MotionMaster` directly.

### Orientation Threshold
In `Initialize`, the final orientation is only applied if `m_o > -7.0f`. Negative values (specifically those <= -7.0f) are used as sentinel values to indicate "no specific orientation required." The default constructor argument for `finalOrientation` is `-10.0f`, reinforcing this convention.

## Member Reference

**Initialize**
Configures and launches the movement spline for the bot. Stops any existing movement, sets roaming state flags, and applies path, velocity, locomotion mode (walk/run/fly/fall), cyclic behavior, and final orientation to a `MoveSplineInit` object before launching it.

**AiBotSmoothedPointMovementGenerator**
Constructor for the movement generator. Initializes the base `PointMovementGenerator` with the final point of the path (or zeros if empty) and stores the full `PointsArray` in `m_path` for later use in `Initialize`.

**IssueSmoothedPath**
Static factory method that validates the input path and installs a new `AiBotSmoothedPointMovementGenerator` onto the bot's `MotionMaster` via the privileged `Mutate` call. Returns immediately if the path is empty.

---

<!-- machine-true, projected from graph.json -->

## Map — AiBotMovementGenerators

*Source:* AiBotMovementGenerators.cpp, AiBotMovementGenerators.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| Initialize | method | MoveSplineInit/Launch, MoveSplineInit/MovebyPath, MoveSplineInit/MoveSplineInit, MoveSplineInit/SetCyclic, MoveSplineInit/SetFacing#2, MoveSplineInit/SetFall, MoveSplineInit/SetFly, MoveSplineInit/SetVelocity, MoveSplineInit/SetWalk, Unit.Main/AddUnitState, Unit.Main/IsStopped, Unit.Main/StopMoving | — | — |
| AiBotSmoothedPointMovementGenerator | ctor | — | — | — |
| IssueSmoothedPath | method | Creature.MotionMaster/Mutate, Unit.Main/GetMotionMaster | AiBotAI.Movement/MoveToDestination, AiBotAI.Movement/StartNextPathChunk | — |
