# Location

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# MoveSpline.h: `Movement::Location`

## Purpose & Responsibilities

The `Movement::Location` struct, defined in `MoveSpline.h`, serves as the fundamental data structure for representing a specific position and orientation within the game world. It extends the base `Vector3` class to include an additional `orientation` component (typically representing yaw/heading in radians).

This unit is strictly a data carrier. It contains no logic, no virtual functions, and no complex state management. Its sole responsibility is to aggregate spatial coordinates (`x`, `y`, `z`) inherited from `Vector3` with an angular orientation (`o`) into a single, pass-by-value object. This allows movement systems to treat position and facing as a cohesive unit during spline calculations and state updates.

## Member-by-Member Behavior

The `Location` struct provides four constructors to facilitate creation from various input formats. All constructors initialize the `Vector3` base class and the `orientation` member.

1.  **Default Construction**: Initializes position to `(0,0,0)` (via `Vector3` default) and orientation to `0`.
2.  **Scalar Construction**: Accepts individual `float` arguments for `x`, `y`, `z`, and `o`.
3.  **Vector3-only Construction**: Accepts a `Vector3` reference for position, defaulting orientation to `0`.
4.  **Vector3 + Orientation Construction**: Accepts a `Vector3` reference for position and a separate `float` for orientation.

There are no member functions beyond these constructors. The `orientation` field is public, allowing direct read/write access.

## Cross-Unit Boundaries

`Location` is a leaf node in the dependency graph regarding outgoing calls; it calls no other units. However, it is heavily consumed by the `MoveSpline` class (defined in the same header but implemented elsewhere, likely `MoveSpline.cpp`).

*   **Called by `MoveSpline::ComputePosition`**: The `MoveSpline` class uses `Location` constructors to package calculated spline points into a format suitable for return to the caller. Specifically, the map indicates `MoveSpline::ComputePosition` invokes the `Location` constructor that takes `x, y, z, o` floats (labeled `Location` in the map, corresponding to the second constructor in the source).
*   **Called by `MoveSpline::ComputePositionAfterTime`**: Similarly, this method constructs a `Location` object using the constructor that accepts a `Vector3` and a `float` orientation (labeled `Location#2` in the map, corresponding to the fourth constructor in the source).

These interactions highlight that `Location` is the standard return type for positional queries within the movement system, ensuring that any entity requesting a position from a spline receives both the spatial coordinates and the correct facing angle.

## Data Model

This unit interacts with no database tables. It operates entirely in memory as part of the runtime movement calculation engine.

## Notable Implementation Details

*   **Inheritance from `Vector3`**: By inheriting from `Vector3`, `Location` implicitly gains all vector arithmetic operators (addition, subtraction, scaling) and coordinate accessors defined in `Vector3`. This allows `Location` objects to be used in vector math contexts where orientation is irrelevant, or to be easily converted back to a `Vector3` via implicit conversion or casting.
*   **Public Member Access**: The `orientation` field is public. This design choice prioritizes simplicity and performance over encapsulation, typical for high-frequency game loop structures where getter/setter overhead is undesirable.
*   **No Validation**: The constructors do not validate the range of the orientation value. It is assumed that callers provide normalized angles (e.g., $[0, 2\pi)$ or $[-\pi, \pi]$), though the struct itself imposes no constraints.
*   **Pass-by-Value Semantics**: As a small POD-like struct (4 floats), `Location` is designed to be passed by value. This avoids pointer indirection and ensures thread-safety for the data payload itself, although the underlying movement state it represents may change concurrently.

## Member Reference

**Location** (ctor): Default constructor. Initializes `Vector3` base to zero and `orientation` to `0`. Called by `MoveSpline::ComputePosition`.

**Location#4** (ctor): Constructor taking `float x, float y, float z, float o`. Initializes `Vector3` base with coordinates and sets `orientation`. No external callers listed in map.

**Location#2** (ctor): Constructor taking `Vector3 const& v, float o`. Initializes `Vector3` base with `v` and sets `orientation`. Called by `MoveSpline::ComputePositionAfterTime`.

**Location#3** (ctor): Constructor taking `Vector3 const& v`. Initializes `Vector3` base with `v` and sets `orientation` to `0`. No external callers listed in map.

---

<!-- machine-true, projected from graph.json -->

## Map — Location

*Source:* MoveSpline.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| Location | ctor | — | MoveSpline/ComputePosition#2 | — |
| Location#4 | ctor | — | — | — |
| Location#2 | ctor | — | MoveSpline/ComputePositionAfterTime | — |
| Location#3 | ctor | — | — | — |
