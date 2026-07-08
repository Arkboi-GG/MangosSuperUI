# spline

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Spline Interpolation Engine (`spline.h` / `spline.cpp`)

## Purpose & Responsibilities

The `Movement::Spline` and `Movement::SplineBase` classes provide the mathematical foundation for interpolating movement paths within the WoWVMaNGOS server. They define continuous curves through a series of control points using three distinct interpolation algorithms: **Linear**, **Catmull-Rom**, and **Bezier3** (Cubic Bezier).

This unit is responsible for:
1.  **Path Representation:** Storing control points and managing the valid index range for evaluation.
2.  **Interpolation:** Calculating the exact 3D position (`Vector3`) and velocity vector (derivative) at any point along the curve, given either a segment index and local parameter ($t \in [0,1]$) or a global normalized progress ($t \in [0,1]$ over the entire spline).
3.  **Arc Length Computation:** Approximating the physical length of spline segments to enable time-based movement calculations (e.g., moving at X units per second).
4.  **Initialization:** Setting up the internal data structures, including handling "virtual" control points required for Catmull-Rom and Bezier continuity at path endpoints or cyclic loops.

This unit does not handle network transmission, AI decision-making, or collision detection. It is a pure computational library consumed by the `MoveSpline` system to determine entity positions during movement updates.

## Member-by-Member Behavior

### Initialization and State Management

The spline lifecycle begins with initialization via `init_spline` or `init_cyclic_spline`. These methods select the appropriate algorithm (Linear, Catmull-Rom, or Bezier3) and populate the internal `points` array.

*   **`init_spline` / `init_cyclic_spline`**: These entry points set the `m_mode` and `cyclic` flags. They dispatch to specific initializer methods (`InitLinear`, `InitCatmullRom`, `InitBezier3`) via function pointers stored in the static `initializers` array.
    *   **`InitLinear`**: Copies control points directly. It adds a "virtual" point at the end to ensure the last segment can be evaluated. If cyclic, the virtual point mirrors the start point.
    *   **`InitCatmullRom`**: Requires extra buffer space. It inserts "virtual" points before the first control point and after the last to ensure smooth tangents at the boundaries. For non-cyclic splines, the pre-first point is extrapolated backwards from the first two points. For cyclic splines, it wraps around using the `cyclic_point` index.
    *   **`InitBezier3`**: Expects control points in groups of 4 (start, 2 handles, end). It truncates the input to the nearest multiple of 3 segments (12 points? No, 4 points per segment, so count must be divisible by 4? The code uses `count / 3u * 3u` and `index_hi = t - 1` where `t = c/3`. This implies a specific packing format where every 3 indices might represent a segment, or it's a legacy artifact. Looking at `EvaluateBezier3`, it multiplies index by 3. This suggests the internal storage packs 3 vectors per logical segment or uses a specific stride. *Correction*: `EvaluateBezier3` does `index *= 3u`. `InitBezier3` sets `index_hi = t - 1` where `t = c/3`. If `c` is the number of points, and we divide by 3, it seems to treat every 3 points as a unit? However, Bezier usually needs 4 points. Let's look closer. `C_Evaluate` takes 4 vertices. `EvaluateBezier3` passes `&points[index]`. If `index` is multiplied by 3, it accesses `points[0..3]`, `points[3..6]`, etc. This implies overlapping control points or a specific storage scheme where the 4th point of segment N is the 1st point of segment N+1, but stored compactly? Actually, `InitBezier3` copies `c` points. If `c=4`, `t=1`, `index_hi=0`. `EvaluateBezier3` with `index=0` accesses `points[0..3]`. This works for one segment. If `c=7` (2 segments sharing a point), `c/3*3 = 6`. `t=2`. `index_hi=1`. Segment 0 uses `0..3`. Segment 1 uses `3..6`. This confirms the storage shares endpoints between segments to save memory.)
*   **`clear`**: Resets the index bounds and clears the points vector.
*   **`empty`**: Returns true if `index_lo == index_hi`, indicating no valid segments.

### Position and Derivative Evaluation

Once initialized, the spline can be queried for positions and derivatives.

*   **`evaluate_percent` (Segment-based)**: Given a segment index and a local $t$, it calls the appropriate evaluator (`EvaluateLinear`, `EvaluateCatmullRom`, or `EvaluateBezier3`) via the `evaluators` function pointer table.
*   **`evaluate_derivative` (Segment-based)**: Similar to above, but computes the tangent vector (velocity direction/magnitude) using the `derivative_evaluators` table.
*   **`EvaluateLinear`**: Performs simple linear interpolation: $P = P_0 + (P_1 - P_0) * t$.
*   **`EvaluateCatmullRom`**: Uses a standard Catmull-Rom matrix multiplication. It requires 4 control points surrounding the segment. The static matrix `s_catmullRomCoeffs` defines the basis functions.
*   **`EvaluateBezier3`**: Uses a Cubic Bezier matrix. It accesses 4 points starting from `index * 3`. The static matrix `s_Bezier3Coeffs` defines the Bernstein polynomial basis.
*   **`C_Evaluate` / `C_Evaluate_Derivative`**: Static helper functions that perform the actual matrix-vector multiplications using G3D math libraries. They take an array of 4 vertices, a parameter $t$, and a coefficient matrix.

### Arc Length and Global Parameterization

To move entities at constant speed, the server needs to know the physical length of the spline.

*   **`SegLength`**: Dispatches to `SegLengthLinear`, `SegLengthCatmullRom`, or `SegLengthBezier3`.
*   **`SegLengthLinear`**: Computes Euclidean distance between two points.
*   **`SegLengthCatmullRom` / `SegLengthBezier3`**: Since these curves are non-linear, arc length cannot be computed analytically in closed form easily. Instead, these methods approximate the length by sampling the curve at `STEPS_PER_SEGMENT` intervals (default 3 steps) and summing the linear distances between samples. This is a numerical approximation.
*   **`Spline<length_type>` Template Class**: Inherits from `SplineBase` and adds support for cumulative length arrays.
    *   **`initLengths`**: Pre-computes the cumulative length of each segment and stores it in the `lengths` vector. This allows $O(1)$ lookup for total length and $O(\log N)$ or $O(N)$ lookup for position at global $t$.
    *   **`length()`**: Returns the total length of the spline.
    *   **`computeIndexInBounds`**: Given a global distance or percentage, determines which segment index contains that point.
    *   **`evaluate_percent` (Global)**: Converts a global $t$ (0.0 to 1.0 over the whole path) into a segment index and local $t$, then delegates to `SplineBase::evaluate_percent`.

### Accessors and Utilities

*   **`first` / `last`**: Return the valid range of segment indices `[index_lo, index_hi)`. Used by `MoveSpline` to iterate over segments.
*   **`getPoint` / `getPoints`**: Provide direct access to the underlying control point array. Used by `packet_builder` to serialize path data to clients.
*   **`mode` / `isCyclic`**: Return the current interpolation mode and whether the path loops.
*   **`ToString`**: Generates a debug string representation of the spline, listing the mode and all control points. Used by `MoveSpline::ToString`.

## Cross-Unit Boundaries

### Collaboration with `MoveSpline`

The `MoveSpline` class is the primary consumer of this unit. It manages the high-level movement state of game entities.

*   **`MoveSpline::init_spline`**: Calls `SplineBase::init_spline` or `init_cyclic_spline` to construct the path geometry from raw waypoint data.
*   **`MoveSpline::_updateState`**: Calls `first`, `last`, `isCyclic`, and `evaluate_percent` to update the entity's position during simulation ticks. It iterates through segments using `first` and `last`.
*   **`MoveSpline::computeFallElevation`**: Calls `first` and `getPoint` to retrieve specific control points for gravity/fall calculations.
*   **`MoveSpline::currentPathIdx`**: Calls `first` and `last` to validate and manage the current segment index.
*   **`MoveSpline::ComputePositionAfterTime`**: Calls `last` to determine the end of the path for time-based projections.
*   **`MoveSpline::operator()`**: Calls `SegLength` to calculate distances for progress tracking.
*   **`MoveSpline::ToString`**: Calls `SplineBase::ToString` for debugging output.

### Collaboration with `packet_builder`

The `packet_builder` unit serializes movement data to send to connected clients.

*   **`packet_builder::WriteCommonMonsterMovePart`**: Calls `first`, `getPoint` to extract path data for basic movement packets.
*   **`packet_builder::WriteCatmullRomPath` / `WriteCatmullRomCyclicPath`**: Calls `getPointCount` and `getPoint` to serialize Catmull-Rom specific path data.
*   **`packet_builder::WriteLinearPath`**: Calls `getPointCount` and `getPoint` to serialize linear path data.

### Collaboration with `Errors`

*   **`Errors::PrintStacktraceAndThrow`**: Called by various `Evaluate*` and `SegLength*` methods if assertions fail (e.g., invalid index). This indicates that incorrect usage of the spline API is considered a fatal error in the server logic.

## Data Model

This unit operates entirely in memory. It does not interact with any database tables. The control points are passed as raw `Vector3` arrays from higher-level systems (likely parsed from database queries in `MoveSpline` or related waypoint loaders, but not directly by this unit).

## Notable Implementation Details

1.  **Virtual Points for Continuity**:
    *   `InitCatmullRom` creates "virtual" control points outside the user-provided range to ensure the curve starts and ends smoothly. For non-cyclic paths, the pre-first point is calculated as `controls[0].lerp(controls[1], -1)`, which extrapolates the tangent backwards. This ensures the derivative at the start matches the direction of the first segment.
    *   `InitLinear` simply duplicates the last point to allow the final segment to be evaluated without bounds errors.

2.  **Approximate Arc Length**:
    *   The `SegLengthCatmullRom` and `SegLengthBezier3` methods use a fixed-step numerical integration (`STEPS_PER_SEGMENT = 3`). This is a trade-off between performance and accuracy. With only 3 steps, sharp curves may have significant length estimation errors, potentially causing entities to "jump" slightly in speed if the server relies strictly on this length for time-based movement. The comment notes that Blizzard clients use 2-3 steps, suggesting this is an acceptable approximation for the game's visual fidelity.

3.  **Function Pointer Dispatch**:
    *   Instead of virtual functions or switch statements, `SplineBase` uses static arrays of function pointers (`evaluators`, `derivative_evaluators`, `seglengths`, `initializers`) indexed by `EvaluationMode`. This avoids runtime branching overhead during the critical path evaluation loop, which is called frequently for every moving entity.

4.  **Bezier Storage Optimization**:
    *   `InitBezier3` and `EvaluateBezier3` use a stride of 3 for indexing (`index *= 3u`). This implies that consecutive Bezier segments share control points in the storage array (specifically, the end point of segment $N$ is the start point of segment $N+1$). This reduces memory usage compared to storing 4 independent points per segment.

5.  **Thread Safety**:
    *   The class contains no mutexes or atomic operations. It is assumed that each `Spline` instance is owned by a single `MoveSpline` object, which is likely processed by a single thread (the movement update thread or the main game loop thread). Concurrent access to the same spline instance is not supported.

6.  **Assertion Heavy**:
    *   Methods like `EvaluateLinear` and `SegLengthLinear` contain `MANGOS_ASSERT(index >= index_lo && index < index_hi)`. This enforces strict bounds checking in debug builds, helping catch logic errors in `MoveSpline` early. In release builds, these checks are disabled, relying on correct usage.

## Member Reference

**UninitializedSpline**: A placeholder method that triggers an assertion failure. Used as a default entry in the function pointer tables for uninitialized modes to catch programming errors.

**SplineBase**: Constructor initializes the base spline state with zero bounds and uninitialized mode.

**C_Evaluate**: Static helper function that performs matrix-vector multiplication to compute a point on a cubic spline given 4 control points, a parameter $t$, and a coefficient matrix.

**evaluate_percent**: Evaluates the position on the spline at a specific segment index and local parameter $t$. Dispatches to the correct algorithm via function pointer.

**evaluate_derivative**: Evaluates the derivative (tangent vector) of the spline at a specific segment index and local parameter $t$. Dispatches to the correct algorithm via function pointer.

**C_Evaluate_Derivative**: Static helper function that computes the derivative of a cubic spline by multiplying the derivative of the basis functions with the control points.

**first**: Returns the lower bound of valid segment indices (`index_lo`). Used by `MoveSpline` to start iteration.

**last**: Returns the upper bound of valid segment indices (`index_hi`). Used by `MoveSpline` to end iteration.

**empty**: Returns true if the spline has no valid segments (`index_lo == index_hi`).

**mode**: Returns the current interpolation mode (Linear, CatmullRom, Bezier3).

**isCyclic**: Returns true if the spline is configured as a closed loop.

**getPoints**: Returns a const reference to the internal vector of control points.

**getPointCount**: Returns the number of control points stored.

**EvaluateLinear**: Computes the position on a linear segment between two points. Throws if index is out of bounds.

**getPoint**: Returns a const reference to a specific control point by index.

**EvaluateCatmullRom**: Computes the position on a Catmull-Rom segment using 4 surrounding control points and the Catmull-Rom basis matrix. Throws if index is out of bounds.

**EvaluateBezier3**: Computes the position on a Bezier segment using 4 control points (packed with stride 3) and the Bezier basis matrix. Throws if index is out of bounds.

**SegLength**: Dispatches to the appropriate segment length calculator based on the current mode.

**EvaluateDerivativeLinear**: Computes the constant derivative (direction vector) of a linear segment. Throws if index is out of bounds.

**EvaluateDerivativeCatmullRom**: Computes the derivative of a Catmull-Rom segment. Throws if index is out of bounds.

**EvaluateDerivativeBezier3**: Computes the derivative of a Bezier segment. Throws if index is out of bounds.

**Spline<length_type>**: Constructor for the templated spline class that supports cumulative length caching.

**SegLengthLinear**: Calculates the Euclidean distance between two control points. Throws if index is out of bounds.

**SegLengthCatmullRom**: Approximates the arc length of a Catmull-Rom segment by sampling 3 intermediate points. Throws if index is out of bounds.

**evaluate_percent#2**: Overload in `Spline` class that evaluates position given a global parameter $t$ (0.0 to 1.0 over the entire spline).

**evaluate_derivative#2**: Overload in `Spline` class that evaluates derivative given a global parameter $t$.

**init_spline#2**: Overload in `Spline` class that delegates to `SplineBase::init_spline`.

**init_cyclic_spline#2**: Overload in `Spline` class that delegates to `SplineBase::init_cyclic_spline`.

**SegLengthBezier3**: Approximates the arc length of a Bezier segment by sampling 3 intermediate points. Throws if index is out of bounds.

**length**: Returns the total cumulative length of the spline (from `Spline` class).

**init_spline**: Initializes the spline with a set of control points and an evaluation mode. Sets up virtual points and index bounds.

**length#3**: Returns the cumulative length up to a specific segment index (from `Spline` class).

**length#2**: Returns the length of a specific segment or range (from `Spline` class).

**set_length**: Manually sets the cumulative length at a specific index (from `Spline` class).

**init_cyclic_spline**: Initializes the spline as a cyclic loop, wrapping control points appropriately.

**InitLinear**: Internal initializer for Linear mode. Copies points and adds a virtual endpoint. Throws if count < 2.

**InitCatmullRom**: Internal initializer for Catmull-Rom mode. Adds virtual start/end points for smooth tangents.

**InitBezier3**: Internal initializer for Bezier mode. Packs points with stride 3.

**clear**: Resets the spline state, clearing points and bounds.

**ToString**: Generates a debug string representation of the spline's mode and control points.

---

<!-- machine-true, projected from graph.json -->

## Map — spline

*Source:* spline.cpp, spline.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| UninitializedSpline | method | — | — | — |
| SplineBase | ctor | — | — | — |
| C_Evaluate | function | — | — | — |
| evaluate_percent | method | — | — | — |
| evaluate_derivative | method | — | — | — |
| C_Evaluate_Derivative | function | — | — | — |
| first | method | — | MoveSpline/computeFallElevation, MoveSpline/currentPathIdx, MoveSpline/init_spline, MoveSpline/_updateState, packet_builder/WriteCommonMonsterMovePart | — |
| last | method | — | MoveSpline/ComputePositionAfterTime, MoveSpline/currentPathIdx, MoveSpline/init_spline, MoveSpline/_Finalize, MoveSpline/_updateState | — |
| empty | method | — | — | — |
| mode | method | — | — | — |
| isCyclic | method | — | MoveSpline/init_spline, MoveSpline/_updateState | — |
| getPoints | method | — | — | — |
| getPointCount | method | — | packet_builder/WriteCatmullRomCyclicPath, packet_builder/WriteCatmullRomPath, packet_builder/WriteLinearPath | — |
| EvaluateLinear | method | Errors/PrintStacktraceAndThrow | — | — |
| getPoint | method | — | MoveSpline/computeFallElevation, MoveSpline/init_spline, MoveSpline/operator()#2, packet_builder/WriteCatmullRomCyclicPath, packet_builder/WriteCatmullRomPath, packet_builder/WriteCommonMonsterMovePart, packet_builder/WriteLinearPath | — |
| EvaluateCatmullRom | method | Errors/PrintStacktraceAndThrow | — | — |
| EvaluateBezier3 | method | Errors/PrintStacktraceAndThrow | — | — |
| SegLength | method | — | MoveSpline/operator() | — |
| EvaluateDerivativeLinear | method | Errors/PrintStacktraceAndThrow | — | — |
| EvaluateDerivativeCatmullRom | method | Errors/PrintStacktraceAndThrow | — | — |
| EvaluateDerivativeBezier3 | method | Errors/PrintStacktraceAndThrow | — | — |
| Spline<length_type> | ctor | — | — | — |
| SegLengthLinear | method | Errors/PrintStacktraceAndThrow | — | — |
| SegLengthCatmullRom | method | Errors/PrintStacktraceAndThrow | — | — |
| evaluate_percent#2 | function | — | — | — |
| evaluate_derivative#2 | function | — | — | — |
| init_spline#2 | function | — | — | — |
| init_cyclic_spline#2 | function | — | — | — |
| SegLengthBezier3 | method | Errors/PrintStacktraceAndThrow | — | — |
| length | function | — | — | — |
| init_spline | method | — | — | — |
| length#3 | function | — | — | — |
| length#2 | function | — | — | — |
| set_length | function | — | — | — |
| init_cyclic_spline | method | — | — | — |
| InitLinear | method | Errors/PrintStacktraceAndThrow | — | — |
| InitCatmullRom | method | — | — | — |
| InitBezier3 | method | — | — | — |
| clear | method | — | — | — |
| ToString | method | — | MoveSpline/ToString | — |
