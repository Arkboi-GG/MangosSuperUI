<!-- provenance: verbose -->
# spline.impl

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# spline.impl

**Purpose & Responsibilities**

`spline.impl` provides the arc-length parametrization logic for the `Movement::Spline<length_type>` template class. It manages the `lengths` vector, which stores cumulative distances from the spline's start to each vertex, enabling conversion between normalized time (`t`, 0.0–1.0) and physical distance. This unit initializes these lengths, clears them, and computes the specific spline segment index and local parameter corresponding to a given time or distance. It does not define the spline's geometry or perform final interpolation; those responsibilities lie in other class partials.

**Member-by-Member Behavior**

### Length Management

*   **`initLengths`**: Precomputes cumulative segment lengths. It iterates from `index_lo` to `index_hi`, accumulating the length of each segment via `SegLength(i)` (defined in another unit) into the `lengths` vector. `lengths[i]` holds the total distance from the start to vertex `i`.
*   **`clear`**: Resets the spline's length data. It calls `SplineBase::clear()` (defined in `SplineBase`) to reset base state, then clears the local `lengths` vector.

### Index Computation

These functions map a distance or time value to a specific spline segment index.

*   **`computeIndexInBounds` (taking `length_type`)**: Given an absolute distance `length_`, finds the segment index `i` such that the point lies in segment `[i, i+1]`. It performs a linear scan starting from `index_lo`, incrementing `i` while `lengths[i + 1] < length_`. A binary search implementation is commented out due to an infinite loop bug at `t = 1.0`.
*   **`computeIndexInBounds` (taking `float t`)**: Converts normalized time `t` to absolute distance (`t * length()`) and delegates to the `length_type` overload. Asserts `t` is in [0, 1].
*   **`computeIndex`**: Resolves normalized time `t` into a segment index and local parameter `u`. It calculates target distance `length_ = t * length()`, finds the index via `computeIndexInBounds`, and computes `u` as the fractional offset within that segment: `(length_ - length(index)) / length(index, index + 1)`. Asserts `t` is in [0, 1] and the resulting index is valid.

### Evaluation Delegation

*   **`evaluate_percent`**: Computes the 3D position `c` at time `t`. It resolves `t` to an index and `u` via `computeIndex`, then calls the overloaded `evaluate_percent(Index, u, c)` (defined in another unit) to perform the interpolation.
*   **`evaluate_derivative`**: Computes the derivative vector `hermite` at time `t`. It resolves `t` to an index and `u` via `computeIndex`, then calls the overloaded `evaluate_derivative(Index, u, hermite)` (defined in another unit).

**Cross-Unit Boundaries**

This unit is an internal implementation file included by the `Spline` class header. It has no direct external callers or callees listed in the MAP, but it relies on members defined in other parts of the `Spline` hierarchy:

*   **Calls to Other Units**:
    *   `SplineBase::clear()`: Called by `clear()` to reset base state.
    *   `SegLength(i)`: Called by `initLengths()` to get individual segment lengths.
    *   `length()`, `length(index)`, `length(index, index + 1)`: Called by `computeIndex` and `computeIndexInBounds` to retrieve total and cumulative lengths.
    *   `evaluate_percent(Index, u, c)` and `evaluate_derivative(Index, u, hermite)`: Overloaded methods called by the public evaluation functions to perform actual interpolation.
    *   `index_lo`, `index_hi`, `lengths`: Member variables accessed throughout.

**Data Model**

This unit does not interact with any database tables. It operates entirely on in-memory data structures.

**Notable Implementation Details**

1.  **Disabled Binary Search**: `computeIndexInBounds(length_type)` uses a linear scan because a binary search implementation caused an infinite loop when `t = 1.0`. This is a known performance trade-off; linear scans are O(N) worst-case, whereas binary search would be O(log N).
2.  **Assertions**: `computeIndex` and `computeIndexInBounds(float)` assert that `t` is within [0, 1]. Callers must ensure `t` is clamped.
3.  **Template Precision**: The class is templated on `length_type`, allowing flexible precision for length calculations.

## Member Reference

**evaluate_percent**  
Computes the 3D position `c` at normalized time `t`. Resolves `t` to a segment index and local parameter `u` via `computeIndex`, then delegates to the overloaded `evaluate_percent(Index, u, c)` (defined in another unit) for interpolation.

**evaluate_derivative**  
Computes the derivative vector `hermite` at normalized time `t`. Resolves `t` to a segment index and local parameter `u` via `computeIndex`, then delegates to the overloaded `evaluate_derivative(Index, u, hermite)` (defined in another unit).

**computeIndexInBounds#2**  
Overload of `computeIndexInBounds` taking normalized time `t`. Converts `t` to absolute distance (`t * length()`) and calls the `length_type` overload. Asserts `t` is in [0, 1].

**computeIndex**  
Resolves normalized time `t` into a segment index and local parameter `u`. Calculates target distance `length_ = t * length()`, finds the index via `computeIndexInBounds`, and computes `u` as the fractional offset within the segment. Asserts `t` is in [0, 1] and the index is valid.

**computeIndexInBounds**  
Overload taking absolute distance `length_`. Finds the segment index `i` such that the point lies in segment `[i, i+1]` using a linear scan from `index_lo`. A binary search implementation is commented out due to an infinite loop bug at `t = 1.0`.

**initLengths**  
Precomputes cumulative segment lengths. Iterates from `index_lo` to `index_hi`, summing segment lengths via `SegLength(i)` (defined in another unit) into the `lengths` vector.

**clear**  
Resets the spline's length data. Calls `SplineBase::clear()` (defined in `SplineBase`) to reset base state, then clears the local `lengths` vector.

---

<!-- machine-true, projected from graph.json -->

## Map — spline.impl

*Source:* spline.impl.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| evaluate_percent | function | — | — | — |
| evaluate_derivative | function | — | — | — |
| computeIndexInBounds#2 | function | — | — | — |
| computeIndex | function | — | — | — |
| computeIndexInBounds | function | — | — | — |
| initLengths | function | — | — | — |
| clear | function | — | — | — |
