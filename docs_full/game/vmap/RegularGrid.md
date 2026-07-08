# RegularGrid

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`RegularGrid2D` is a template-based spatial partitioning structure that divides a fixed 2D plane into a $64 \times 64$ uniform grid. It accelerates geometric queries—such as containment checks, point/ray intersections, and object retrieval—by mapping objects of type `T` into specific grid cells based on their axis-aligned bounding boxes (AABBs).

Each cell contains a `Node` object (specified by the template parameter `Node`) that manages the actual storage and local intersection logic for objects within that cell. `RegularGrid2D` handles the coarse-grained indexing: determining which cells an object overlaps, lazily allocating `Node` instances for occupied cells, and traversing the grid during ray-casting operations. It relies on the G3D library for vector math and trait-based access to object bounds and positions.

## Member-by-Member Behavior

### Initialization and Lifecycle
*   **`RegularGrid2D<T, Node, NodeCreatorFunc, BoundsFunc, PositionFunc>`**: Initializes the `nodes` array (a $64 \times 64$ matrix of `Node*`) to `nullptr` using `memset`. No `Node` objects are allocated at construction; allocation is lazy.
*   **`~RegularGrid2D<T, Node, NodeCreatorFunc, BoundsFunc, PositionFunc>`**: Iterates through all $64 \times 64$ cells and deletes any non-null `Node` pointer, reclaiming memory for occupied cells.

### Object Management
*   **`insert`**: Adds an object `value` to the grid.
    1.  Computes the object’s AABB using `BoundsFunc`.
    2.  Determines the range of grid cells `[low, high]` intersected by the AABB.
    3.  For each cell in the range, ensures a `Node` exists via `getGrid`, inserts `value` into that `Node`, and records the association `(&value, &node)` in `memberTable` (an `std::unordered_multimap`). This multimap allows efficient reverse lookup for removal.
*   **`remove`**: Removes `value` from the grid.
    1.  Uses `memberTable` to find all `Node` pointers associated with `&value`.
    2.  Calls `remove` on each of those `Node` objects.
    3.  Erases all entries for `&value` from `memberTable`.
*   **`contains`**: Returns `true` if `memberTable` contains an entry for `&value`, indicating the object is currently tracked in the grid.
*   **`size`**: Returns the number of unique objects tracked in `memberTable`. Note that because large objects span multiple cells, the total count of items stored across all `Node` objects will exceed this value.

### Grid Access and Maintenance
*   **`getGrid`**: Returns a reference to the `Node` at grid coordinates `(x, y)`. If the cell is empty, it allocates a new `Node` using `NodeCreatorFunc::makeNode(x, y)`. Asserts that `x` and `y` are within `[0, CELL_NUMBER)`.
*   **`balance`**: Iterates through all cells and calls `balance()` on any existing `Node`. This propagates a rebalancing command to the underlying node structures, likely to maintain performance of internal trees or lists.

### Geometric Queries
*   **`intersectRay`**: Casts a ray through the grid to find intersections.
    1.  Computes the start and end cells based on the ray’s origin and endpoint (`origin + direction * max_dist`).
    2.  If the ray lies entirely within one cell, it delegates to that cell’s `Node::intersectRay`.
    3.  Otherwise, it performs a 2D Digital Differential Analyzer (DDA) traversal across the X/Y grid. It calculates step directions and distances to cell boundaries (`tMaxX`, `tMaxY`) and iteratively steps into adjacent cells, calling `Node::intersectRay` for each occupied cell until the ray exits the grid or reaches the end cell.
*   **`intersectPoint`**: Tests for intersections at a specific 3D `point`. It computes the corresponding grid cell and delegates to `Node::intersectPoint` if the cell is valid and occupied.
*   **`intersectZAllignedRay`**: Optimized ray cast for vertical rays (parallel to Z-axis). Since the grid is 2D, a vertical ray intersects only one column of cells. It locates the single cell containing the ray’s X/Y origin and delegates the intersection test to that `Node`.

### Iteration Support
*   **`IteratorPair<iterator>`**, **`IteratorPair<iterator>#2`**, **`IteratorPair<iterator>#3`**: Constructors for the nested `IteratorPair` class. They wrap a `std::pair<iterator, iterator>` (typically from `equal_range`) into a consistent interface with `begin()` and `end()` methods.
*   **`begin`** and **`end`**: Listed in the MAP but not implemented as member functions in the provided source. The class provides `IteratorPair` and `MapEqualRange` for range-based iteration over `memberTable` entries, but lacks direct `begin`/`end` members on the grid itself.

## Cross-Unit Boundaries

*   **Calls Out**:
    *   **`Node` (Template Parameter)**: Delegates fine-grained operations: `insert`, `remove`, `balance`, `intersectRay`, and `intersectPoint`.
    *   **`NodeCreatorFunc` (Template Parameter)**: Calls `makeNode` to instantiate new `Node` objects.
    *   **`BoundsFunc` / `PositionFunc` (Template Parameters)**: Calls `getBounds` to retrieve object AABBs.
    *   **`G3D` Library**: Uses `Vector2`, `Vector3`, `AABox`, and `Ray` for geometric calculations.
    *   **`Errors.h`**: Uses `MANGOS_ASSERT` for bounds checking in `getGrid`.

*   **Called By**:
    *   The MAP indicates no external callers. This unit is a utility class instantiated by higher-level spatial managers within the MaNGOS codebase.

## Data Model

This unit operates entirely in memory and does not interact with any database tables.

## Notable Implementation Details

1.  **Fixed Grid Dimensions**: The grid is hardcoded to $64 \times 64$ cells (`CELL_NUMBER`). The world size is derived from `HGRID_MAP_SIZE` ($533.33 \times 64 \approx 34,133$ units), implying a coverage area of roughly $34km \times 34km$. Coordinates outside this range are invalid (`Cell::isValid()` returns false).
2.  **Lazy Allocation**: `Node` objects are allocated only when a cell is first accessed via `getGrid`. This conserves memory for sparse worlds.
3.  **Reverse Lookup Table**: `memberTable` maps object pointers to `Node` pointers. Since an object can span multiple cells, one key maps to multiple values. This enables $O(1)$ average-case removal without recalculating bounds or scanning the grid.
4.  **Ray Traversal Optimization**: `intersectRay` uses a DDA algorithm to traverse only the cells intersected by the ray, skipping empty space. `intersectZAllignedRay` further optimizes vertical rays by avoiding traversal entirely.
5.  **Thread Safety**: The class contains no synchronization primitives. Concurrent access requires external locking.
6.  **Pointer Validity**: `memberTable` stores raw pointers to objects (`T const*`). Callers must ensure objects remain valid for the grid’s lifetime or are removed before destruction to avoid dangling pointers.

## Member Reference

*   **`makeNode`**: Static function in `NodeCreator` struct (default `NodeCreatorFunc`). Allocates a new `Node` instance, ignoring `x`/`y` parameters in the default implementation.
*   **`RegularGrid2D<T, Node, NodeCreatorFunc, BoundsFunc, PositionFunc>`**: Constructor. Zero-initializes the `nodes` array.
*   **`~RegularGrid2D<T, Node, NodeCreatorFunc, BoundsFunc, PositionFunc>`**: Destructor. Deletes all allocated `Node` objects in the grid.
*   **`insert`**: Inserts an object into all grid cells its bounding box overlaps and updates `memberTable`.
*   **`IteratorPair<iterator>`**: Constructor for `IteratorPair`. Initializes from default values.
*   **`IteratorPair<iterator>#3`**: Constructor for `IteratorPair`. Initializes from two separate iterators.
*   **`IteratorPair<iterator>#2`**: Constructor for `IteratorPair`. Initializes from a `std::pair`.
*   **`begin`**: Listed in MAP but not present in source code.
*   **`end`**: Listed in MAP but not present in source code.
*   **`remove`**: Removes an object from all grid cells it occupies using `memberTable` for lookup.
*   **`balance`**: Calls `balance()` on every occupied `Node` in the grid.
*   **`contains`**: Returns `true` if the object’s address is in `memberTable`.
*   **`size`**: Returns the number of unique objects in `memberTable`.
*   **`getGrid`**: Returns a reference to the `Node` at `(x, y)`, creating it if necessary. Asserts bounds.

---

<!-- machine-true, projected from graph.json -->

## Map — RegularGrid

*Source:* RegularGrid.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| makeNode | function | — | — | — |
| RegularGrid2D<T, Node, NodeCreatorFunc, BoundsFunc, PositionFunc> | ctor | — | — | — |
| ~RegularGrid2D<T, Node, NodeCreatorFunc, BoundsFunc, PositionFunc> | dtor | — | — | — |
| insert | function | — | — | — |
| IteratorPair<iterator> | ctor | — | — | — |
| IteratorPair<iterator>#3 | ctor | — | — | — |
| IteratorPair<iterator>#2 | ctor | — | — | — |
| begin | function | — | — | — |
| end | function | — | — | — |
| remove | function | — | — | — |
| balance | function | — | — | — |
| contains | function | — | — | — |
| size | function | — | — | — |
| getGrid | function | — | — | — |
