# game_Movement_spline_util

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# game_Movement_spline_util

## Purpose & Responsibilities

`game_Movement_spline_util` (`util.cpp`) provides free-fall physics calculations and flag serialization for the `Movement` namespace. It implements kinematic equations for falling objects, accounting for gravity and terminal velocity limits, including a reduced-velocity "safe fall" mode. It also converts `MoveSplineFlag` bitmasks into human-readable strings for debugging.

## Member-by-Member Behavior

### Physics Calculations

The unit defines global constants for gravity (`19.29110527038574`), normal terminal velocity (`60.148003f`), and safe-fall terminal velocity (`7.f`). Derived constants pre-calculate the distance and time required to reach these limits.

**computeFallTime** calculates the duration of a fall given `path_length` and `isSafeFall`. If the distance exceeds the threshold to reach terminal velocity, it sums the acceleration time and the constant-velocity time. Otherwise, it uses $t = \sqrt{2d/g}$. Negative lengths return `0.f`.

**computeFallElevation#2** calculates vertical distance fallen over `t_passed`, given `start_velocity` and `isSafeFall`. It clamps `start_velocity` to the applicable terminal limit. If `t_passed` exceeds the time remaining to reach terminal velocity, it adds the distance covered during acceleration to the distance covered at constant terminal velocity. Otherwise, it uses $d = v_0t + 0.5gt^2$.

**computeFallElevation** is a simplified overload assuming zero initial velocity. It uses global `terminalFallTime` and `terminal_length` to switch between acceleration ($d = 0.5gt^2$) and constant-velocity phases.

### Flag Serialization

**ToString** converts a `MoveSplineFlag` object into a space-separated string of active flag names. It iterates through bits using the `print_flags` helper and the `g_SplineFlag_names` array. The `g_MovementFlag_names` array is defined in this file but unused.

## Cross-Unit Boundaries

*   **Calls Out:**
    *   `MoveSplineFlag/raw#2`: Called by `ToString` to retrieve the underlying integer bitmask.
*   **Called By:**
    *   `MoveSpline/operator()#2`: Calls `computeFallTime` to determine spline duration.
    *   `Unit.Main/ExtrapolateMovement`: Calls `computeFallElevation#2` to predict position during falls.
    *   `MoveSpline/computeFallElevation`: Calls `computeFallElevation` for internal elevation calculations.
    *   `MoveSpline/ToString`: Calls `ToString` for debug output.

## Data Model

This unit does not interact with any database tables.

## Notable Implementation Details

*   **Hardcoded Physics:** Gravity and terminal velocities are fixed globals; changes require recompilation.
*   **Unused Array:** `g_MovementFlag_names` is defined but never referenced; only `g_SplineFlag_names` is used by `ToString`.
*   **Bitwise Assumption:** `print_flags` assumes flag enums map directly to bit positions (`1 << i`).

## Member Reference

**computeFallTime**
Calculates fall duration for a given path length and safe-fall status, handling acceleration and terminal velocity phases. Called by `MoveSpline/operator()#2`.

**computeFallElevation#2**
Calculates vertical distance fallen over time given an initial velocity and safe-fall status, clamping start velocity to terminal limits. Called by `Unit.Main/ExtrapolateMovement`.

**computeFallElevation**
Calculates vertical distance fallen over time assuming zero initial velocity, using global terminal velocity constants. Called by `MoveSpline/computeFallElevation`.

**ToString**
Converts `MoveSplineFlag` bitmask to a string of active flag names using `g_SplineFlag_names`. Calls `MoveSplineFlag/raw#2`. Called by `MoveSpline/ToString`.

---

<!-- machine-true, projected from graph.json -->

## Map — game_Movement_spline_util

*Source:* util.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| computeFallTime | function | — | MoveSpline/operator()#2 | — |
| computeFallElevation#2 | function | — | Unit.Main/ExtrapolateMovement | — |
| computeFallElevation | function | — | MoveSpline/computeFallElevation | — |
| ToString | method | MoveSplineFlag/raw#2 | MoveSpline/ToString | — |
