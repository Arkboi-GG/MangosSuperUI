<!-- provenance: failed-members -->
# MoveSpline

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# MoveSpline

**MoveSpline** is the core runtime representation of a movement trajectory for a `Unit` in the WoWVMaNGOS server. It encapsulates a mathematical spline (either linear or Catmull-Rom) that defines a path through 3D space, along with the temporal state required to interpolate the unit's position and orientation over time.

Its primary responsibilities are:
1.  **Trajectory Storage:** Holding the control points, flags (e.g., falling, cyclic, facing), and timing data for a specific movement action.
2.  **State Advancement:** Updating its internal clock (`time_passed`) and segment index (`point_Idx`) as time elapses, allowing the owning `Unit` to determine its current location via interpolation.
3.  **Position Interpolation:** Calculating the exact `(x, y, z)` coordinates and orientation of the unit at any given moment within the spline's duration, including special handling for gravity-based falls.
4.  **Lifecycle Management:** Tracking whether the movement is active, finalized, or interrupted, and providing hooks for network synchronization (e.g., tracking which points have been sent to clients).

It does not perform the actual movement of the `Unit`; rather, it provides the data and logic for `Unit.Main/UpdateSplineMovement` and various `MovementGenerator` classes to query and advance.

## Member-by-Member Behavior

### Initialization and Configuration

The lifecycle of a `MoveSpline` begins with initialization via `MoveSplineInit/Launch`. The `Initialize` method sets up the spline's metadata (flags, ID, transport GUID) and delegates the heavy lifting of constructing the geometric curve to `init_spline`.

*   **Initialize**: Resets the spline state (time, indices) and applies the configuration from `MoveSplineInitArgs`. It calls `init_spline` to build the underlying `Spline` object.
*   **init_spline**: Constructs the geometric path. It determines whether to use linear or Catmull-Rom interpolation based on `MoveSplineFlag/isSmooth`. If the movement is cyclic, it initializes a cyclic spline; otherwise, a standard spline. Crucially, it calculates the timestamp for each segment. If the `falling` flag is set, it uses `FallInitializer` to calculate segment durations based on physics (gravity); otherwise, it uses `CommonInitializer` to calculate durations based on constant velocity. It includes a safety check: if the total spline length is less than 1ms (indicating all points are identical or too close), it logs an error and forces a minimal duration to prevent division-by-zero or infinite loops in update logic.
*   **CommonInitializer** and **FallInitializer**: These are functor structs used during spline initialization. `CommonInitializer` calculates segment time as `distance / velocity`. `FallInitializer` calculates segment time using `game_Movement_spline_util/computeFallTime`, simulating gravitational acceleration.
*   **MoveSpline** (ctor): Default constructor. Initializes member variables to safe defaults (ID 0, uninterruptible false, etc.) and marks the spline as "done" initially until `Initialize` is called.

### State Updates and Time Management

The spline advances its state in discrete steps, typically driven by the game loop via `Unit.Main/UpdateSplineMovement`.

*   **updateState**: A template method that repeatedly calls `_updateState` until the provided time difference (`difftime`) is consumed. It invokes a user-provided `handler` for each state change (e.g., arriving at a new segment). This allows callers to react immediately to segment transitions.
*   **_updateState**: The core logic for advancing time. It adds the elapsed time to `time_passed`. If `time_passed` exceeds the timestamp of the next control point (`next_timestamp`), it increments `point_Idx`.
    *   If the spline is **cyclic**, it wraps `point_Idx` back to the start and adjusts `time_passed` modulo the total duration.
    *   If the spline is **linear** and reaches the end, it calls `_Finalize` and returns `Result_Arrived`.
    *   It ensures that `time_passed` never exceeds the current segment's end time within a single call, preventing overshoot.
*   **_Finalize**: Marks the spline as done (`splineflags.done = true`), sets the index to the last segment, and sets `time_passed` to the total duration.
*   **_Interrupt**: Immediately marks the spline as done, effectively stopping the movement. Called by `Unit.Main/DisableSpline`.

### Position and Orientation Calculation

These methods answer the question: "Where is the unit right now?" or "Where will it be in X milliseconds?"

*   **ComputePosition**: Returns the current `Location` (position + orientation) by calling the overloaded `ComputePosition` with the current `point_Idx` and `time_passed`. It asserts that the spline is initialized.
*   **ComputePosition#2** (Overload): The actual interpolation logic. It calculates the normalized parameter `u` (0.0 to 1.0) within the current segment based on `desiredTime`. It evaluates the spline at this `u` to get the `(x, y, z)` coordinates.
    *   **Orientation Logic**:
        *   If the spline is marked as `done` and has a facing flag (`isFacing`), it uses the pre-calculated facing info (`facing.angle`, `facing.point`, or `facing.target`).
        *   Otherwise, it computes the derivative of the spline at `u` to determine the tangent vector, setting orientation to `atan2(dy, dx)`.
    *   **Fall Handling**: If the `falling` flag is set, it adjusts the Z-coordinate using `computeFallElevation` to apply gravitational drop relative to the start of the fall.
*   **ComputePositionAfterTime**: Extrapolates the position forward by `duration` milliseconds. It iterates through future segments to find which segment the unit would occupy after the specified time, then calls `ComputePosition#2` for that specific segment and time. Used by `Unit.Main/ExtrapolateMovement`.
*   **computeFallElevation**: Adjusts the Z-coordinate for falling movements. It calculates the expected height based on time passed since the fall started (`spline.getPoint(spline.first()).z - Movement::computeFallElevation(...)`) and clamps it to the final destination Z if the calculated height is lower (preventing the unit from falling below the ground target).

### Querying State and Metadata

These methods provide read-only access to the spline's properties for networking, AI, and debugging.

*   **Finalized**: Returns true if the movement is complete. Heavily used by `Unit.Main` and various `MovementGenerator`s to decide whether to start a new movement or stop.
*   **isCyclic**: Returns true if the spline loops. Used by `packet_builder` and `Unit.Main` to determine how to handle the end of the movement.
*   **IsUninterruptible**: Returns true if the movement cannot be stopped by external forces (e.g., spells). Used by `Unit.Main/StopMoving`.
*   **Duration** / **Duration#2**: Returns the total time of the spline or the time between two specific points. Used by `packet_builder` to send movement data to clients.
*   **GetId**: Returns the unique identifier for this spline instance. Used for matching movement updates with completion opcodes (`WorldSession.MovementHandler/HandleMoveSplineDoneOpcode`).
*   **FinalDestination** / **CurrentDestination** / **PreviousDestination**: Return the coordinates of the end, next, or previous control points. Used by `Creature.MotionMaster` and `MovementAnticheat` to validate movement paths.
*   **currentPathIdx**: Calculates the index of the current path point relative to the original input array, accounting for offsets and cyclic wrapping. Used by `WaypointMovementGenerator` to track progress.
*   **getLastPointSent** / **setLastPointSent**: Tracks the last control point index that was synchronized with clients. Used by `Unit.Main/UpdateSplineMovement` to minimize network traffic by only sending updates when the unit passes a new waypoint.
*   **getPath**: Returns the raw control points. Used by `ChatHandler.DebugCommands` and `packet_builder` for debugging and initial creation packets.
*   **ToString**: Generates a human-readable string of the spline's state (ID, flags, time, position). Used for logging and debugging.

### Cross-Unit Boundaries

*   **Called by `Unit.Main/UpdateSplineMovement`**: This is the primary driver. `Unit.Main` calls `updateState` to advance time, `ComputePosition` to get the new location, and `Finalized`/`isCyclic` to manage the movement lifecycle.
*   **Called by `MoveSplineInit/Launch`**: `Launch` creates and configures the `MoveSpline` before attaching it to a `Unit`. It calls `Initialize`, `SetMovementOrigin`, `GetTransportGuid`, and `GetId`.
*   **Called by `packet_builder`**: Various `Write*` functions (e.g., `WriteCreate`, `WriteMonsterMove`) query `MoveSpline` for `Duration`, `GetId`, `isCyclic`, `FinalDestination`, and `Initialized` to construct network packets that tell clients how to animate the movement.
*   **Called by `MovementGenerator`s**: Classes like `WaypointMovementGenerator`, `RandomMovementGenerator`, and `PointMovementGenerator` interact with `MoveSpline` to start movements (`StartMove` calls `timeElapsed`, `GetPoint`, `CountSplinePoints`) and update state (`Update` calls `Finalized`, `currentPathIdx`).
*   **Calls `Errors/PrintStacktraceAndThrow`**: Several computation methods (`ComputePosition`, `ComputePositionAfterTime`, `_updateState`) assert preconditions (like being initialized). If these fail in debug builds, they trigger stack traces.
*   **Calls `game_Movement_spline_util`**: `computeFallElevation` and `computeFallTime` are used for physics-based falling calculations.
*   **Calls `Log.Main/Out`**: `init_spline` and `_checkPathBounds` log errors if invalid data (zero-length splines, out-of-bounds paths) is detected.

## Data Model

This unit does not directly interact with database tables. It operates entirely on in-memory data structures provided by `MoveSplineInitArgs` and the `Spline` class. Any persistence of movement data (e.g., waypoint paths) occurs at higher levels (e.g., `WaypointMovementGenerator` loading from DB) before being passed to `MoveSpline`.

## Notable Implementation Details

1.  **Minimal Duration Safety Check**: In `init_spline`, if the calculated spline length is less than 1ms, the code forces the length to 1ms (or 1000ms for cyclic). This prevents division-by-zero errors in `ComputePosition#2` where `segTime` is used as a divisor. The comment notes this is likely due to bad input data (all points at the same coordinate).
2.  **Cyclic Wrapping Logic**: In `_updateState`, cyclic splines reset `point_Idx` to `spline.first()` and wrap `time_passed` using modulo arithmetic. This ensures continuous looping without accumulating floating-point drift in the index.
3.  **Fall Physics Approximation**: The `computeFallElevation` method uses a simplified physics model. It calculates the expected Z position based on time passed since the *start* of the fall (`spline.first()`), not the current segment. This assumes the fall starts at the first point of the spline. It clamps the result to the final destination Z to prevent underground clipping.
4.  **Orientation Derivation**: When no specific facing flag is set, orientation is derived from the spline's derivative (tangent). This ensures the unit faces the direction of travel. However, if the spline is "done" and a facing flag exists, it overrides this with static facing data.
5.  **Network Optimization**: The `last_point_sent_Idx` mechanism allows the server to batch movement updates. Clients only need to be informed when the unit crosses a new control point, reducing bandwidth for long, smooth splines.
6.  **Thread Safety**: `MoveSpline` is not thread-safe. It is assumed to be accessed only from the main game loop thread associated with the owning `Unit`. Concurrent access from multiple threads (e.g., during network packet construction vs. state update) could lead to race conditions, though the architecture typically serializes these operations.

## Member Reference

**ComputePosition**: Returns the current interpolated `Location` (position and orientation) of the unit on the spline. Asserts that the spline is initialized. Delegates to the overloaded `ComputePosition` method.

**ComputePositionAfterTime**: Extrapolates the unit's position forward by `duration` milliseconds. Iterates through future spline segments to find the correct segment and time offset, then calls `ComputePosition#2`. Used for prediction/extrapolation.

**ComputePosition#2**: Core interpolation logic. Calculates the normalized parameter `u` within the specified segment. Evaluates the spline for `(x, y, z)`. Determines orientation either from pre-set facing flags (if `done` and `isFacing`) or from the spline's derivative (tangent). Adjusts Z for falling movements.

**getPath**: Returns a const reference to the spline's control point array. Used for debugging and packet construction.

**GetTransportGuid**: Returns the GUID of the transport vehicle the unit is riding, if any. Used to adjust coordinates relative to the transport.

**next_timestamp**: Returns the absolute time (in ms) at which the next control point is reached. Calculated as `spline.length(point_Idx + 1)`.

**segment_time_elapsed**: Returns the remaining time in the current segment. Calculated as `next_timestamp() - time_passed`.

**timeElapsed**: Returns the remaining time until the spline completes. Calculated as `Duration() - time_passed`.

**timePassed**: Returns the amount of time (in ms) that has elapsed since the spline started.

**_Spline**: Returns a const reference to the underlying `MySpline` object. Exposed for advanced debugging or inspection.

**_currentSplineIdx**: Returns the current internal spline point index (`point_Idx`).

**_Interrupt**: Immediately marks the spline as done (`splineflags.done = true`), stopping the movement. Called when movement is disabled externally.

**Initialized**: Returns true if the spline has been initialized (i.e., is not empty). Used to guard against accessing uninitialized data.

**computeFallElevation**: Adjusts the Z-coordinate for falling movements. Calculates the expected height based on time passed since the fall started and clamps it to the final destination Z.

**updateState**: Template method that advances the spline state by `difftime` milliseconds. Repeatedly calls `_updateState` and invokes a handler for each state change (e.g., segment transition). Allows callers to react to intermediate states.

**computeDuration**: Static helper function (defined in cpp) that calculates duration in milliseconds from length and velocity. Uses `SecToMS`.

**FallInitializer**: Functor struct used during spline initialization for falling movements. Calculates segment durations based on gravitational physics (`computeFallTime`).

**GetId**: Returns the unique identifier for this spline instance. Used to match movement updates with completion opcodes.

**Finalized**: Returns true if the spline has completed (`splineflags.done`). Used extensively by movement generators and unit logic to determine if a new movement should be started.

**operator()#2**: Functor operator for `FallInitializer`. Calculates the time duration for a spline segment based on the vertical distance and gravitational physics, calling `game_Movement_spline_util/computeFallTime`.

**isCyclic**: Returns true if the spline is configured to loop continuously.

**IsUninterruptible**: Returns true if the movement cannot be stopped by external interrupts (e.g., spells).

**FinalDestination**: Returns the coordinates of the last control point in the spline.

**CurrentDestination**: Returns the coordinates of the next control point (`point_Idx + 1`).

**PreviousDestination**: Returns the coordinates of the current control point (`point_Idx`).

**GetPoint**: Returns the coordinates of a specific control point by index.

**CountSplinePoints**: Returns the index of the last point in the spline (effectively the count of segments + 1).

**getLastPointSent**: Returns the index of the last control point that was synchronized with clients.

**setLastPointSent**: Sets the index of the last control point synchronized with clients. Used to optimize network traffic.

**Duration**: Returns the total duration of the spline in milliseconds.

**CommonInitializer**: Functor struct used during spline initialization for non-falling movements. Calculates segment durations based on constant velocity.

**Duration#2**: Returns the duration between two specific points in the spline.

**GetMovementOrigin**: Returns a string describing the origin of the movement (for debugging).

**operator()**: Functor operator for `CommonInitializer`. Calculates the time duration for a spline segment based on the segment length (`spline/SegLength`) and the inverse velocity.

**SetMovementOrigin**: Sets the debug string describing the origin of the movement.

**init_spline**: Internal method that constructs the geometric spline. Determines interpolation mode (linear/Catmull-Rom), initializes cyclic or linear spline, and calculates segment timestamps using either `FallInitializer` or `CommonInitializer`. Includes safety checks for zero-length splines.

**Initialize**: Public method to configure the spline. Sets flags, ID, transport GUID, and resets time/indices. Calls `init_spline`.

**MoveSpline**: Default constructor. Initializes member variables to safe defaults.

**Validate**: Method of `MoveSplineInitArgs` (referenced in MAP). Validates the input arguments: checks that path size > 1 and velocity > 0. Logs errors if validation fails.

**_checkPathBounds**: Method of `MoveSplineInitArgs` (referenced in MAP). Checks if path vertices fit within network packet limits for non-CatmullRom splines. Logs errors if bounds are exceeded.

**_updateState**: Internal method that advances the spline state by a small time step. Updates `time_passed` and `point_Idx`. Handles cyclic wrapping and finalization. Returns an `UpdateResult` indicating the state change.

**ToString**: Generates a human-readable string representation of the spline's current state, including ID, flags, time, and position.

**_Finalize**: Internal method that marks the spline as done, sets the index to the last segment, and sets `time_passed` to the total duration.

**currentPathIdx**: Calculates the index of the current path point relative to the original input array, accounting for offsets and cyclic wrapping. Used by movement generators to track progress.

---

<!-- machine-true, projected from graph.json -->

## Map — MoveSpline

*Source:* MoveSpline.cpp, MoveSpline.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ComputePosition | method | Errors/PrintStacktraceAndThrow | MoveSplineInit/Launch, Unit.Main/UpdateSplineMovement | — |
| ComputePositionAfterTime | method | Errors/PrintStacktraceAndThrow, Location/Location#2, spline/last | Unit.Main/ExtrapolateMovement | — |
| ComputePosition#2 | method | Errors/PrintStacktraceAndThrow, Location/Location, MoveSplineFlag/isFacing | — | — |
| getPath | method | — | ChatHandler.DebugCommands/HandleDebugMoveSplineCommand, packet_builder/WriteCreate | — |
| GetTransportGuid | method | — | MoveSplineInit/Launch | — |
| next_timestamp | method | — | — | — |
| segment_time_elapsed | method | — | — | — |
| timeElapsed | method | — | WaypointMovementGenerator/StartMove | — |
| timePassed | method | — | packet_builder/WriteCreate | — |
| _Spline | method | — | — | — |
| _currentSplineIdx | method | — | — | — |
| _Interrupt | method | — | Unit.Main/DisableSpline | — |
| Initialized | method | — | packet_builder/WriteCreate | — |
| computeFallElevation | method | game_Movement_spline_util/computeFallElevation, spline/first, spline/getPoint, typedefs/MSToSec | — | — |
| updateState | method | — | Unit.Main/UpdateSplineMovement | — |
| computeDuration | function | typedefs/SecToMS | — | — |
| FallInitializer | ctor | — | — | — |
| GetId | method | — | MoveSplineInit/Launch, packet_builder/WriteCommonMonsterMovePart, packet_builder/WriteCreate, WorldSession.MovementHandler/HandleMoveSplineDoneOpcode | — |
| Finalized | method | — | ChatHandler.CreatureCommands/HandleNpcAIInfoCommand, ChatHandler.DebugCommands/HandleDebugMoveSplineCommand, Creature.MotionMaster/GetDestination, CyclicMovementGenerator/Update, HomeMovementGenerator/Update, MovementAnticheat/HandleFlagTests, MovementAnticheat/HandlePositionTests, MovementPacketSender/SendSpeedChangeToObservers, MoveSplineInit/Launch, PlayerBotAI/UpdateAI, PointMovementGenerator/Finalize#2, PointMovementGenerator/Update, RandomMovementGenerator/UpdateAsync, Unit.Main/ExtrapolateMovement, Unit.Main/IsMovedByPlayer, Unit.Main/KnockBack, Unit.Main/StopMoving, Unit.Main/UpdateSplineMovement, Unit.SpellAuras/HandleAuraModRoot, WaypointMovementGenerator/StartMove, WaypointMovementGenerator/Update#2, WaypointMovementGenerator/Update#3, WorldSession.MovementHandler/HandleMovementOpcodes, WorldSession.MovementHandler/HandleMoveNotActiveMoverOpcode | — |
| operator()#2 | method | game_Movement_spline_util/computeFallTime, spline/getPoint | — | — |
| isCyclic | method | — | packet_builder/WriteCommonMonsterMovePart, packet_builder/WriteCreate, Unit.Main/UpdateSplineMovement | — |
| IsUninterruptible | method | — | Unit.Main/StopMoving | — |
| FinalDestination | method | — | Creature.MotionMaster/GetDestination, MovementAnticheat/HandleSplineDone, packet_builder/WriteCreate, Unit.Main/SaveStayPosition | — |
| CurrentDestination | method | — | — | — |
| PreviousDestination | method | — | — | — |
| GetPoint | method | — | WaypointMovementGenerator/StartMove | — |
| CountSplinePoints | method | — | packet_builder/WriteMonsterMove, WaypointMovementGenerator/StartMove | — |
| getLastPointSent | method | — | Unit.Main/UpdateSplineMovement | — |
| setLastPointSent | method | — | MoveSplineInit/Launch, Unit.Main/UpdateSplineMovement | — |
| Duration | method | — | MoveSplineInit/Launch, packet_builder/WriteCommonMonsterMovePart, packet_builder/WriteCreate | — |
| CommonInitializer | ctor | — | — | — |
| Duration#2 | method | — | packet_builder/WriteMonsterMove | — |
| GetMovementOrigin | method | — | ChatHandler.CreatureCommands/HandleNpcAIInfoCommand, ChatHandler.DebugCommands/HandleDebugMoveSplineCommand | — |
| operator() | method | spline/SegLength | — | — |
| SetMovementOrigin | method | — | MoveSplineInit/Launch | — |
| init_spline | method | Log.Main/Out, MoveSplineFlag/isSmooth, spline/first, spline/getPoint, spline/isCyclic, spline/last | — | — |
| Initialize | method | — | MoveSplineInit/Launch | — |
| MoveSpline | ctor | — | Unit.Main/Unit | — |
| Validate | method | Log.Main/Out, Object/GetGuidStr | MoveSplineInit/Launch | — |
| _checkPathBounds | method | Log.Main/Out, MoveSplineFlag/operator& | — | — |
| _updateState | method | Errors/PrintStacktraceAndThrow, spline/first, spline/isCyclic, spline/last | — | — |
| ToString | method | game_Movement_spline_util/ToString, spline/ToString | — | — |
| _Finalize | method | spline/last | — | — |
| currentPathIdx | method | spline/first, spline/last | Unit.Main/UpdateSplineMovement, WaypointMovementGenerator/Update | — |

---

<!-- verify: failed-members | invented: operator -->
