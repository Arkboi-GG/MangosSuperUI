# AiBotPathSmoothing

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# AiBotPathSmoothing

**Purpose & Responsibilities**

`AiBotPathSmoothing` is a pure-geometry utility namespace that generates candidate path segments to replace sharp vertices in a bot’s navigation path. It produces smoother, more natural movement trajectories by rounding corners, operating independently of the navmesh or AI logic. The unit implements two techniques used in a fallback chain by `AiBotAI.Movement`:

1.  **Tight Fillet:** A conservative quadratic Bézier curve bounded within the triangle formed by the incoming segment, corner vertex, and outgoing segment. It is safe by construction, never extending beyond the original corner point.
2.  **Wide Bow:** An aggressive outward-curving arc that reaches back along the approach and forward past the corner, cutting diagonally across open space. Because it extends into unvalidated terrain, it requires strict validation by the caller.

`AiBotPathSmoothing` **only generates candidate coordinates**. It performs no collision detection, navmesh queries, or reachability checks. The caller (`AiBotAI.Movement`) validates candidates against the navmesh, rejecting invalid ones and falling back to the fillet or original path. All randomization is deterministic, seeded by `journeySeed` and the corner’s location, ensuring path stability during a single journey while varying between trips.

## Member-by-Member Behavior

### Geometry Primitives

Static helpers providing low-level vector math for the smoothing algorithms.

*   **`Lerp3`**: Linearly interpolates between `Vector3` points `a` and `b` by factor `t`.
*   **`QuadraticBezier`**: Computes a point on a quadratic Bézier curve defined by control points `p0`, `p1`, `p2` at parameter `t`. Core engine for the Tight Fillet.
*   **`DeterministicUnitFloat`**: Converts a `uint32` seed to a float in `[0, 1)` using a splitmix32-style hash. Provides pseudo-random values for deterministic variation.
*   **`CornerSeed`**: Generates a unique `uint32` seed for a corner by combining `journeySeed` with the quantized integer coordinates of vertex `B`. Ensures consistent smoothing for the same corner/journey pair.

### Tight Fillet Technique

Creates a smooth arc around corner `B` given neighbors `A` and `C`.

*   **`ComputeCornerFillet`**:
    *   Validates segment lengths (`AB`, `BC`); returns `false` if either is < 0.01f.
    *   Calculates turn angle at `B`; returns `false` if < `kFilletAngleThresholdDeg` (30°).
    *   Determines a randomized "pullback" distance from `B` along both segments, bounded by `kFilletMinPullbackYards` (0.5) and `kFilletMaxPullbackYards` (2.5), and clamped to 40% of the shorter adjacent segment.
    *   Defines entry/exit points at the pullback distance.
    *   Calculates a control point by interpolating between `B` and the entry/exit midpoint, scaled by a randomized "cut amount".
    *   Generates `kFilletInteriorPoints` (2) samples along the Bézier curve from Entry to Exit.
    *   Populates `outFillet` with Entry, interior samples, and Exit. Returns `true`.
    *   **Safety**: All points lie within the `A-B-C` triangle.

### Wide Bow Technique

Creates a broader arc cutting across the corner.

*   **`RollWideBowPlan`**:
    *   Generates a unique hash for the corner, XORing `journeySeed` with `0xB0A7C0DEu` to decorrelate from fillet rolls.
    *   Rolls four parameters via `DeterministicUnitFloat`:
        *   `preferredSide`: +1 or -1.
        *   `widthYards`: Lateral offset, biased small via squared roll (`rollWidth * rollWidth`).
        *   `anchorBackYards` / `anchorFwdYards`: Reach distances back/forward from the corner.
    *   Returns a `WideBowPlan` struct. Note: `widthYards` is **unclamped**; the caller must enforce chord/width proportionality.

*   **`ComputeWideBowSamples`**:
    *   Validates anchor distance (> 1.0f); returns empty if degenerate.
    *   Calculates the perpendicular vector to the chord.
    *   Iterates `kBowSamplePoints` (4) times, generating interior samples.
    *   Uses a raised-cosine profile (`0.5 * (1 - cos(2πt))`) for the offset, ensuring zero derivative at endpoints for tangent-continuous joining.
    *   Offsets the linear interpolation between anchors by the bump amount along the perpendicular.
    *   Sets Z to match the base linear interpolation (caller resolves real Z).
    *   Appends points to `outBow` (anchors excluded).

### Path Traversal Utilities

Assist the caller in determining valid anchor points.

*   **`WalkBackwardForDistance`**: Walks backward from `fromIdx` in `path`, accumulating segment distance until `targetDist` is met or `minIdx` is reached. Returns the resulting index.
*   **`WalkForwardForDistance`**: Walks forward from `fromIdx` in `path`, accumulating segment distance until `targetDist` is met or `maxIdx` is reached. Returns the resulting index.

## Cross-Unit Boundaries

*   **Called by `AiBotAI.Movement/SmoothPathCorners`**:
    *   `ComputeCornerFillet` generates the primary smoothing candidate.
    *   `RollWideBowPlan` determines wide bow parameters.
    *   `ComputeWideBowSamples` generates wide bow points using anchors determined by the caller (via `WalkBackwardForDistance`/`WalkForwardForDistance`).
    *   **Collaboration**: `AiBotAI.Movement` orchestrates the fallback chain, validates points against the navmesh, and enforces the chord/width proportionality guard (`kBowMinChordYards` / `kBowWidthToChordRatio`) on `widthYards` before calling `ComputeWideBowSamples`. This keeps `AiBotPathSmoothing` free of navmesh dependencies.

## Data Model

This unit interacts with **no database tables**. It operates entirely on in-memory geometric data structures (`Vector3`, `PointsArray`).

## Notable Implementation Details

1.  **Deterministic Randomization**: Seeds are derived from `journeySeed` and corner coordinates. Distinct XOR constants in `RollWideBowPlan` ensure fillet and bow parameters are uncorrelated.
2.  **Bias Towards Smaller Bows**: `RollWideBowPlan` squares the width roll to skew the distribution toward smaller offsets, making modest sweeps more common.
3.  **Tangent-Continuous Joining**: `ComputeWideBowSamples` uses a raised-cosine profile with zero derivatives at endpoints, preventing visual kinks at splices.
4.  **Safety Bounds**: The fillet is mathematically bounded within the `A-B-C` triangle. The bow has no inherent geometric safety; validation is delegated to the caller.
5.  **Chord/Width Proportionality Guard**: Constants `kBowMinChordYards` and `kBowWidthToChordRatio` prevent hairpin turns on short chords. These are enforced by the caller, as `AiBotPathSmoothing` lacks context on clamped anchor distances.
6.  **Z-Coordinate Handling**: `ComputeWideBowSamples` sets Z to the linear interpolation of anchors. The caller must resolve real Z via `ReGroundZ`.

## Member Reference

*   **`Lerp3`**: Static helper performing linear interpolation between two `Vector3` points.
*   **`QuadraticBezier`**: Static helper computing a point on a quadratic Bézier curve.
*   **`DeterministicUnitFloat`**: Static helper converting a `uint32` seed to a deterministic float in `[0, 1)` using a splitmix32-style hash.
*   **`CornerSeed`**: Static helper generating a unique `uint32` seed for a corner by hashing the `journeySeed` and the corner's quantized coordinates.
*   **`ComputeCornerFillet`**: Generates a tight, safe quadratic Bézier fillet for a corner `B` between `A` and `C`. Returns `false` if the turn is too gentle or segments too short. Output points are bounded within the `A-B-C` triangle.
*   **`RollWideBowPlan`**: Generates randomized parameters (`WideBowPlan`) for a wide bow, including side, width (biased small), and anchor distances. Uses a distinct hash seed to decorrelate from fillet rolls.
*   **`ComputeWideBowSamples`**: Generates interior sample points for a wide bow between two anchors, using a raised-cosine profile for tangent-continuous joining. Does not include anchor points in output.
*   **`WalkBackwardForDistance`**: Walks backward along a `PointsArray` from a starting index, accumulating distance until a target is met or a minimum index is reached.
*   **`WalkForwardForDistance`**: Walks forward along a `PointsArray` from a starting index, accumulating distance until a target is met or a maximum index is reached.

---

<!-- machine-true, projected from graph.json -->

## Map — AiBotPathSmoothing

*Source:* AiBotPathSmoothing.cpp, AiBotPathSmoothing.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| Lerp3 | function | — | — | — |
| QuadraticBezier | function | — | — | — |
| DeterministicUnitFloat | function | — | — | — |
| CornerSeed | function | — | — | — |
| ComputeCornerFillet | function | — | AiBotAI.Movement/SmoothPathCorners | — |
| RollWideBowPlan | function | — | — | — |
| ComputeWideBowSamples | function | — | — | — |
| WalkBackwardForDistance | function | — | — | — |
| WalkForwardForDistance | function | — | — | — |
