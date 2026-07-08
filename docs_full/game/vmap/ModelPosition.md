# ModelPosition

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`ModelPosition` is a lightweight data structure within the `VMAP` namespace, defined in `TileAssembler.h`. Its primary responsibility is to encapsulate the spatial transformation parameters required to position and orient a 3D model instance within the world geometry processing pipeline. Specifically, it stores a position (`iPos`), a direction/orientation vector (`iDir`), and a scale factor (`iScale`). It provides methods to initialize a rotation matrix from Euler angles derived from `iDir` and to adjust the stored position relative to a base coordinate.

This class is a component of the VMAP (Virtual Map) system, which converts raw vector data into balanced BSP-Trees for collision detection and rendering purposes in the WoW server environment. `ModelPosition` does not handle I/O, database access, or complex geometric calculations itself; it serves as a container for transformation state that is consumed by other parts of the assembly process.

## Member-by-Member Behavior

The `ModelPosition` class contains two public methods and three public data members, along with one private data member.

### Transformation Initialization
**`init`**: This method prepares the internal rotation state. It takes the current values of the public member `iDir` (which holds orientation angles in degrees) and converts them into a `G3D::Matrix3` rotation matrix stored in the private member `iRotation`. The conversion assumes the input angles are in degrees and applies the standard degree-to-radian conversion (`* pi() / 180.f`). The order of application for the Euler angles is ZYX (Yaw, Pitch, Roll), corresponding to `iDir.y`, `iDir.x`, and `iDir.z` respectively. This method must be called after `iDir` is set but before any transformations requiring the rotation matrix are performed.

### Position Adjustment
**`moveToBasePos`**: This method adjusts the model's position relative to a specified base position. It subtracts the provided `pBasePos` vector from the current `iPos` vector. This operation effectively shifts the model's local origin so that it is relative to the new base point. This is likely used during the tiling or chunking process where models need to be repositioned relative to the corner of a specific map tile or sector.

### Data Members
*   **`iPos`**: A `G3D::Vector3` representing the model's position in 3D space.
*   **`iDir`**: A `G3D::Vector3` representing the model's orientation. Although named "Dir", the usage in `init` confirms it stores Euler angles (degrees) for X, Y, and Z axes, not a directional vector.
*   **`iScale`**: A `float` representing the uniform scale factor of the model.
*   **`iRotation`**: A private `G3D::Matrix3` storing the computed rotation matrix. This is populated by `init` and is presumably used by external consumers (like `transform`, though `transform` is declared but not defined in this header) to apply rotational transformations to vertices.

## Cross-Unit Boundaries

`ModelPosition` interacts with the rest of the codebase primarily through its initialization and position adjustment methods.

*   **Called by `BoundsTrait.TileAssembler/calculateTransformedBound`**: The `init` method is invoked by `calculateTransformedBound` in the `BoundsTrait` partial of `TileAssembler` (likely defined in `TileAssembler.cpp` or a related trait file). This indicates that before calculating the transformed bounding box of a model, the system ensures the rotation matrix is up-to-date based on the model's current orientation. This is a critical step for accurate collision geometry calculation.
*   **No Outgoing Calls**: `ModelPosition` does not call any other units. It relies entirely on the `G3D` library for vector and matrix operations, which are considered external dependencies rather than internal codebase units.

## Data Model

`ModelPosition` does not interact with any database tables. It operates purely on in-memory data structures provided during the model loading and assembly phase.

## Notable Implementation Details

1.  **Euler Angle Convention**: The `init` method explicitly uses `G3D::Matrix3::fromEulerAnglesZYX`. This means the rotation is applied in the order Z, then Y, then X. Engineers must ensure that the values stored in `iDir` correspond to this convention. If the source data uses a different rotation order (e.g., XYZ), the resulting orientation will be incorrect.
2.  **Degree Input**: The input to `init` is assumed to be in degrees. The code manually converts to radians using `G3D::pi() * value / 180.f`. This is a common source of bugs if the input data is already in radians or if the conversion factor is misinterpreted.
3.  **Mutable State**: The `init` method modifies the private `iRotation` member. This implies that `iRotation` is not automatically kept in sync with `iDir`. Any change to `iDir` requires a subsequent call to `init` to update the rotation matrix. Failure to do so will result in transformations using stale rotation data.
4.  **Position Subtraction**: `moveToBasePos` performs a simple subtraction (`iPos -= pBasePos`). This changes the absolute position of the model to be relative to `pBasePos`. This is a destructive operation on `iPos`; the original absolute position is lost unless saved elsewhere. This suggests that `ModelPosition` instances might be reused or that the absolute position is not needed after this step.
5.  **Undefined `transform` Method**: The header declares `G3D::Vector3 transform(G3D::Vector3 const& pIn) const;` but does not provide an implementation. This method is likely defined in a corresponding `.cpp` file (not provided in the source snippet) or is intended to be implemented by a derived class or template specialization. Its presence suggests that `ModelPosition` is expected to apply its stored position, rotation, and scale to arbitrary points, but the logic for this is not visible in this unit.

## Member Reference

**`init`**: Initializes the private `iRotation` matrix by converting the Euler angles stored in `iDir` (in degrees) to a ZYX rotation matrix using `G3D::Matrix3::fromEulerAnglesZYX`. Must be called after `iDir` is set.

**`moveToBasePos`**: Adjusts the model's position (`iPos`) by subtracting the provided `pBasePos` vector, effectively making the position relative to the new base point.

---

<!-- machine-true, projected from graph.json -->

## Map — ModelPosition

*Source:* TileAssembler.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| init | method | — | BoundsTrait.TileAssembler/calculateTransformedBound | — |
| moveToBasePos | method | — | — | — |
