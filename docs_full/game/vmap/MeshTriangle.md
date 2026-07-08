# MeshTriangle

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## MeshTriangle

### Purpose & Responsibilities

`MeshTriangle` is a lightweight, Plain Old Data (POD) structure within the `VMAP` namespace of the `wowvmangos` codebase. Its sole responsibility is to represent a single triangular face in a 3D mesh by storing the indices of its three constituent vertices. It acts as a fundamental building block for higher-level geometric structures, specifically `GroupModel` and `WorldModel`, which use collections of `MeshTriangle` objects to define the surface geometry of World Model Objects (WMOs) and other 3D assets.

The class contains no logic beyond construction; it does not perform calculations, validation, or memory management. It relies entirely on external systems (such as `BIH` for spatial indexing or rendering engines) to interpret these indices against a separate array of vertex coordinates.

### Member-by-Member Behavior

The unit defines two constructors that initialize the three index members (`idx0`, `idx1`, `idx2`).

1.  **Default Construction**: The parameterless constructor initializes all three indices to zero. This is typically used when declaring arrays or vectors of triangles before their specific vertex references are known, ensuring a deterministic initial state.
2.  **Parameterized Construction**: The second constructor accepts three `uint32` arguments (`na`, `nb`, `nc`) and assigns them directly to `idx0`, `idx1`, and `idx2` respectively. This is the primary mechanism for populating triangle data during mesh parsing or generation.

### Cross-Unit Boundaries

As indicated in the MAP, `MeshTriangle` has minimal cross-unit interaction because it is a data container rather than a behavioral component.

*   **Called by `BoundsTrait.TileAssembler/Read`**: The MAP indicates that the parameterized constructor (`MeshTriangle#2`) is invoked by `BoundsTrait.TileAssembler/Read`. This suggests that during the assembly or reading of tile-based bounds or mesh data, `BoundsTrait.TileAssembler/Read` constructs `MeshTriangle` instances to populate the geometric representation of a tile. The direction of data flow is from `BoundsTrait.TileAssembler/Read` into `MeshTriangle`, passing vertex indices to establish the triangle's topology.
*   **No Outgoing Calls**: `MeshTriangle` does not call into any other units. It is a passive data holder.

### Data Model

`MeshTriangle` does not interact with any database tables. It operates purely in memory, representing transient geometric data derived from asset files (such as WMO or M2 models) or generated procedurally. There are no SQL queries, table references, or persistence mechanisms associated with this class.

### Notable Implementation Details

*   **Public Member Variables**: The indices `idx0`, `idx1`, and `idx2` are declared as `public`. This design choice allows direct access and modification without getter/setter overhead, which is common in performance-critical graphics or physics simulation code where cache locality and instruction count matter. However, it also means there is no encapsulation; external code can assign arbitrary values to these indices without validation.
*   **Vertex Indexing**: The class stores *indices* (`uint32`), not the actual `Vector3` coordinates. This implies that `MeshTriangle` instances must always be used in conjunction with a separate array or vector of `Vector3` objects (as seen in `GroupModel::vertices`). The validity of a `MeshTriangle` depends entirely on the existence and size of that external vertex buffer.
*   **No Orientation Logic**: The class does not store or enforce winding order (clockwise vs. counter-clockwise). The interpretation of which side of the triangle is the "front" is determined by the consumer of the data (e.g., the `BIH` tree or rendering pipeline) based on the order of `idx0`, `idx1`, and `idx2` relative to the vertex positions.
*   **Namespace Context**: Defined within `namespace VMAP`, indicating its role in the Virtual Map system, likely used for collision detection, line-of-sight checks, or heightmap generation in the game world.

## Member Reference

**MeshTriangle** (default ctor): Initializes `idx0`, `idx1`, and `idx2` to 0. Used for default initialization of triangle arrays.

**MeshTriangle#2** (parameterized ctor): Initializes `idx0`, `idx1`, and `idx2` with the provided `uint32` arguments `na`, `nb`, and `nc`. Called by `BoundsTrait.TileAssembler/Read` to construct triangles from parsed or assembled tile data.

---

<!-- machine-true, projected from graph.json -->

## Map — MeshTriangle

*Source:* WorldModel.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| MeshTriangle | ctor | — | — | — |
| MeshTriangle#2 | ctor | — | BoundsTrait.TileAssembler/Read | — |
