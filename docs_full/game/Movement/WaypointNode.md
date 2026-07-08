# WaypointNode

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# WaypointNode

**Purpose & Responsibilities**

`WaypointNode` is a plain data structure representing a single point in a creature's movement path. It stores spatial coordinates, orientation, timing, and behavioral metadata required for NPC movement logic. Defined in `WaypointManager.h`, it serves as the value type for `WaypointPath` maps managed by `WaypointManager`.

### Member-by-Member Behavior

The struct contains eight public data members and two constructors.

*   **`x`, `y`, `z`** (`float`): World coordinates of the waypoint.
*   **`orientation`** (`float`): Facing angle at the point.
*   **`delay`** (`uint32`): Pause duration before moving to the next point.
*   **`wander_distance`** (`float`): Radius for idle wandering around the point.
*   **`script_id`** (`uint32`): Identifier for a script triggered at this point.
*   **`path_id`** (`uint32`): Identifier associating the node with a specific path.

**Constructors**
*   **`WaypointNode()`**: Default constructor. Initializes all members to zero (`0.0f` or `0`). Ensures a known state for implicit constructions in containers.
*   **`WaypointNode(float _x, ...)`**: Parameterized constructor. Assigns provided arguments to the corresponding members. Used by `WaypointManager::AddNode` to instantiate nodes from external data.

### Cross-Unit Boundaries

*   **Called by `WaypointManager::AddNode`**: The parameterized constructor is invoked when `WaypointManager` (in `WaypointManager.cpp`) creates a new waypoint via the `.wp add` console command.
*   **No Outgoing Calls**: As a POD struct, it performs no operations or external calls.

### Data Model

`WaypointNode` does not access the database directly. Its fields correspond to columns in `creature_movement` and `creature_movement_template` tables, populated by `WaypointManager`.

### Notable Implementation Details

*   **POD Structure**: No encapsulation, virtual functions, or inheritance. Lightweight for storage in `std::map`.
*   **Explicit Zeroing**: The default constructor explicitly sets all fields to zero, preventing undefined behavior from uninitialized memory in standard containers.
*   **No Validation**: Constructors accept any numeric values; validity is enforced by callers or database constraints.

## Member Reference

**WaypointNode**  
Default constructor. Initializes all members (`x`, `y`, `z`, `orientation`, `delay`, `wander_distance`, `script_id`, `path_id`) to zero.

**WaypointNode#2**  
Parameterized constructor. Assigns eight arguments to corresponding members. Called by `WaypointManager::AddNode`.

---

<!-- machine-true, projected from graph.json -->

## Map — WaypointNode

*Source:* WaypointManager.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| WaypointNode | ctor | — | — | — |
| WaypointNode#2 | ctor | — | WaypointManager/AddNode | — |
