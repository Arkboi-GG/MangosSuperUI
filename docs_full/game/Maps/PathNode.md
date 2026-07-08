# PathNode

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# PathNode

**PathNode** is a lightweight, aggregate data structure representing a single point in three-dimensional space. It serves as the fundamental building block for pathfinding and movement calculations within the `wowvmangos` engine.

### Purpose & Responsibilities

The primary responsibility of `PathNode` is to encapsulate Cartesian coordinates (`x`, `y`, `z`) as floating-point values. It does not contain any behavioral logic, validation, or transformation methods. Instead, it acts as a passive data carrier, designed to be stored in contiguous or sequential containers (such as `std::deque` or `std::vector`) to represent sequences of waypoints, known as paths.

In the context of the broader system, `PathNode` instances are aggregated by the `Path` template class (defined in the same header) to form `PointPath` objects. These paths are likely used by AI agents, NPCs, or players to navigate the game world, avoiding obstacles, or following predefined routes. The simplicity of `PathNode` ensures minimal memory overhead and fast access patterns, which is critical for performance-sensitive pathfinding algorithms that may process thousands of nodes per second.

### Member-by-Member Behavior

`PathNode` exposes two constructors and three public member variables. There are no methods beyond construction.

#### Constructors

1.  **Default Constructor (`PathNode()`)**
    *   Initializes all coordinate fields (`x`, `y`, `z`) to `0.0f`.
    *   This provides a safe, zero-initialized state for nodes that are default-constructed, such as when resizing a container of `PathNode` objects.

2.  **Parameterized Constructor (`PathNode(float _x, float _y, float _z)`)**
    *   Initializes `x`, `y`, and `z` with the provided arguments.
    *   This allows for direct initialization of a node with specific spatial coordinates, typically used when adding a new waypoint to a path.

#### Member Variables

*   **`float x`**: The horizontal coordinate along the X-axis.
*   **`float y`**: The horizontal coordinate along the Y-axis.
*   **`float z`**: The vertical coordinate along the Z-axis.

These members are public, allowing direct read/write access from any code that holds a `PathNode` instance. This design choice prioritizes ease of use and performance over encapsulation, as `PathNode` is a simple data holder without invariants that need protection.

### Cross-Unit Boundaries

According to the provided MAP, `PathNode` has **no outgoing calls** to other units and is **not called by** any other units in the sense of function invocation. However, it is heavily depended upon by the `Path` class (specifically the `PointPath` typedef) defined in the same header.

*   **Dependency Direction**: Other units (e.g., AI modules, movement generators) interact with `PathNode` indirectly through the `Path` class. They populate `Path` objects with `PathNode` instances, and the `Path` class provides utility functions (like `GetTotalLength`) that operate on these nodes.
*   **Data Flow**: Coordinates flow *into* `PathNode` during construction or assignment, and *out* of `PathNode` when accessed by algorithms calculating distances, interpolating positions, or rendering paths.

### Data Model

`PathNode` does not interact with any database tables. It is a purely in-memory structure. No SQL queries, table references, or schema dependencies exist in this unit.

### Notable Implementation Details

1.  **Aggregate Structure**: `PathNode` is a Plain Old Data (POD) type. It has no virtual functions, no private members, and no complex initialization logic. This makes it trivially copyable and movable, which is essential for efficient storage in standard containers like `std::deque` (used by `Path`).

2.  **Floating-Point Precision**: All coordinates are `float`. In large-scale game worlds, single-precision floats can suffer from precision loss at extreme distances from the origin. However, for typical zone-based games like World of Warcraft (which `wowvmangos` emulates), zones are often centered around `(0,0,0)` or use relative coordinates, mitigating this issue. Maintainers should be aware that if paths span very large areas without re-centering, jitter or inaccuracies might occur.

3.  **No Validation**: The constructor accepts any `float` value, including `NaN` (Not a Number) or `Infinity`. If invalid coordinates are passed, the `PathNode` will store them silently. Downstream code (e.g., `Path::GetTotalLength`) must handle or validate these inputs, as `sqrtf` of negative numbers (from squared differences) is always non-negative, but operations involving `NaN` will propagate `NaN` results.

4.  **Template Flexibility**: Although `PathNode` is the default node type for `PointPath`, the `Path` template allows for other node types (`PathElem`). This suggests that `PathNode` is the standard case, but the architecture supports extensions (e.g., nodes with additional metadata like terrain type or cost) without changing the core path management logic.

## Member Reference

**PathNode**  
Default constructor. Initializes `x`, `y`, and `z` to `0.0f`. Provides a zero-state for uninitialized nodes.

**PathNode#2**  
Parameterized constructor. Initializes `x`, `y`, and `z` with the provided `float` arguments `_x`, `_y`, and `_z`. Used for creating nodes with specific coordinates.

---

<!-- machine-true, projected from graph.json -->

## Map — PathNode

*Source:* Path.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| PathNode | ctor | — | — | — |
| PathNode#2 | ctor | — | — | — |
