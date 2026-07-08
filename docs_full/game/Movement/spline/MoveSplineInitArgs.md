<!-- provenance: verbose -->
# MoveSplineInitArgs

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# MoveSplineInitArgs

`MoveSplineInitArgs` is a lightweight aggregate structure in the `Movement` namespace that holds the configuration parameters for initializing a movement spline. It defines the geometric path, kinematic properties (velocity, flags), and orientation constraints for a `Unit`'s movement action. It does not execute movement; it serves as the data payload passed to the spline generation system.

## Purpose & Responsibilities

The structure encapsulates the intent of a movement command, bridging high-level directives (e.g., "move to point X") and the low-level mathematical spline representation. Its responsibilities are:
1.  **Path Storage:** Holding a sequence of 3D waypoints (`PointsArray`).
2.  **Orientation Specification:** Defining how the unit faces during movement via the `FacingInfo` union.
3.  **Metadata Configuration:** Storing velocity, spline ID, transport association, and interruptibility settings.
4.  **Validation:** Providing a `Validate` method to check if the configuration is feasible for a specific `Unit` before spline generation.

## Member-by-Member Behavior

### Construction

*   **`MoveSplineInitArgs`**: The constructor accepts an optional `path_capacity` (default 16). It initializes `path_Idx_offset` to 0, `velocity` to 0.0f, `splineId` to 0, `transportGuid` to 0, and `uninterruptible` to false. It pre-allocates memory for the `path` vector using `reserve(path_capacity)` to minimize heap allocations during path building.

### Orientation: `FacingInfo`

`FacingInfo` is a union allowing three mutually exclusive orientation modes, saving memory since only one is active at a time.

*   **`FacingInfo(float o)`**: Initializes the `angle` member, forcing the unit to rotate to a specific absolute angle (radians).
*   **`FacingInfo(uint64 t)`**: Initializes the `target` member, instructing the unit to face a specific entity identified by its GUID.
*   **`FacingInfo()`**: The default constructor. It leaves the union uninitialized. In practice, this is used when no specific facing constraint is applied, relying on the consumer to ignore or default the value.

### Data Members

*   **`path`**: A `std::vector<G3D::Vector3>` storing the movement waypoints.
*   **`facing`**: A `FacingInfo` union specifying orientation.
*   **`flags`**: A `MoveSplineFlag` bitmask controlling spline behavior (e.g., smooth turns, flight).
*   **`path_Idx_offset`**: An integer offset into the `path` array, used for resuming movement or handling path segments.
*   **`velocity`**: The traversal speed.
*   **`splineId`**: A unique identifier for the spline instance.
*   **`transportGuid`**: The GUID of a transport vehicle. If non-zero, path coordinates are relative to this transport.
*   **`uninterruptible`**: Boolean flag indicating if the movement can be stopped by external events.

### Validation

*   **`Validate(Unit* unit)`**: Checks if the configuration is valid for the given `Unit`. It returns `true` if the arguments are consistent and executable. Implementation details are in the corresponding `.cpp` file, but it generally verifies path validity, velocity limits, and flag consistency.
*   **`_checkPathBounds()`**: A private helper likely called by `Validate` to ensure path coordinates are within world bounds.

## Cross-Unit Boundaries

*   **Called by**: Other units (AI modules, movement generators) instantiate this struct to configure movement.
*   **Calls out**: `Validate` interacts with the `Unit` class to check state feasibility. `_checkPathBounds` may interact with map boundary utilities.

## Data Model

This unit does not interact with any database tables. It operates entirely in memory.

## Notable Implementation Details

1.  **Union Optimization**: `FacingInfo` uses a union to reduce memory footprint, as only one orientation mode is active.
2.  **Pre-allocation**: The constructor reserves vector capacity to avoid reallocations during critical path-building phases.
3.  **Uninitialized Default**: The default `FacingInfo()` constructor leaves the union uninitialized. Consumers must ensure it is set or ignored appropriately to avoid undefined behavior.
4.  **Relative Coordinates**: `transportGuid` implies that `path` coordinates are relative to the transport if specified, requiring transformation by the spline engine.

## Member Reference

**FacingInfo#2**
Constructor for `FacingInfo` that initializes the `angle` member with a `float` value. Used to set a fixed rotation angle for the moving unit.

**FacingInfo#3**
Constructor for `FacingInfo` that initializes the `target` member with a `uint64` GUID. Used to make the unit face a specific game object or unit.

**FacingInfo**
Default constructor for `FacingInfo`. Leaves the union in an uninitialized state. Typically used when no specific facing constraint is intended.

**MoveSplineInitArgs**
Constructor for `MoveSplineInitArgs`. Initializes all members to default values and pre-allocates memory for the `path` vector based on the optional `path_capacity` argument.

---

<!-- machine-true, projected from graph.json -->

## Map — MoveSplineInitArgs

*Source:* MoveSplineInitArgs.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| FacingInfo#2 | ctor | — | — | — |
| FacingInfo#3 | ctor | — | — | — |
| FacingInfo | ctor | — | — | — |
| MoveSplineInitArgs | ctor | — | — | — |
