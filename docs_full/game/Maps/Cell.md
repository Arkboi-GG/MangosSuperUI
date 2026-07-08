# Cell

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Cell

The `Cell` unit provides the fundamental spatial indexing abstraction for the game world’s grid system. It defines how continuous 2D coordinates are mapped to discrete grid and cell indices, manages the state of those indices (including flags to suppress dynamic loading), and implements algorithms to visit objects within specific geometric areas (rectangular ranges or circular radii) across the map’s object containers.

This unit is composed of two distinct classes that share the name `Cell` but serve different subsystems:
1.  **`Cell` (in `Cell.h`/`CellImpl.h`)**: The primary spatial index for the main game world (`Map`). It divides the world into large "Grids" and smaller "Cells" within those grids. It handles visibility calculations, object iteration via visitors, and coordinate conversion.
2.  **`RegularGrid2D::Cell` (in `RegularGrid.h`)**: A nested struct within the `RegularGrid2D` template class, used for a separate, fixed-size 64x64 grid system likely associated with pathfinding or collision detection (using G3D library primitives).

## Purpose & Responsibilities

### Main Game World Spatial Index (`Cell`)
The `Cell` struct in `Cell.h` acts as a lightweight, 64-bit integer wrapper that encodes four 8-bit values: `grid_x`, `grid_y`, `cell_x`, and `cell_y`. This allows efficient storage and comparison of spatial locations. Its responsibilities include:
*   **Coordinate Conversion**: Translating floating-point world coordinates into discrete grid/cell indices.
*   **Spatial Querying**: Determining which cells fall within a given radius or rectangular area around a point or object.
*   **Object Visitation**: Iterating over objects stored in `Map` containers (`GridTypeMapContainer`, `WorldTypeMapContainer`) that reside in relevant cells, using a Visitor pattern.
*   **Loading Control**: Managing a `nocreate` flag to indicate whether accessing a cell should trigger the dynamic loading of that grid from disk/memory or if it should remain unloaded (used for queries that must not cause side effects like loading).

### Regular Grid Spatial Index (`RegularGrid2D::Cell`)
The nested `Cell` struct in `RegularGrid.h` serves a similar purpose for the `RegularGrid2D` template. It maps floating-point coordinates to a fixed 64x64 integer grid. It is responsible for:
*   Validating if a coordinate falls within the grid bounds.
*   Providing the integer indices needed to access the underlying `Node` array in `RegularGrid2D`.

## Member-by-Member Behavior

### Coordinate Encoding and Accessors
The core of the `Cell` class is its ability to pack spatial indices into a single `uint64` value.

*   **Constructors (`Cell#2`, `Cell`)**:
    *   `Cell(CellPair const& p)`: Converts a linear `CellPair` (which holds raw X/Y indices) into the hierarchical `grid_x`, `grid_y`, `cell_x`, `cell_y` structure. It uses division and modulo by `MAX_NUMBER_OF_CELLS` to separate the grid index from the cell-within-grid index.
    *   `Cell()`: Default constructor, zero-initializing the data.

*   **Index Retrieval (`CellX`, `CellY`, `GridX`, `GridY`)**:
    *   These methods return the individual 8-bit components of the packed `data` union. They are heavily used by `Map.Main` and `ObjectGridLoader` to determine which specific grid or cell an object resides in for relocation, loading, or removal operations.

*   **Pair Generation (`gridPair`, `cellPair`)**:
    *   `gridPair()`: Returns a `GridPair` struct containing the `grid_x` and `grid_y` indices. Used by `Map.Main/CreatureCellRelocation` to identify the grid level location.
    *   `cellPair()`: Reconstructs the linear `CellPair` from the hierarchical indices. This is the inverse of the constructor.

*   **Comparison Operators (`operator==`, `operator!=`, `operator==#2`)**:
    *   `operator==(Cell const& cell)`: Compares the entire 64-bit `data.All` value. This is highly efficient for checking if two cells are identical. Used by `Map.Main` during player relocation and integrity checks.
    *   `operator!=(Cell const& cell)`: Negation of equality. Used by `Map.Main/CheckGridIntegrity` and `Map.Main/CreatureCellRelocation`.
    *   `operator==#2`: Likely an overload or alternative comparison (possibly against a `CellPair` or similar, though the signature in the map is ambiguous, the source shows only `Cell const&`). *Note: The source code only explicitly defines `operator==(Cell const&)`. The map lists `operator==#2`, which may refer to an implicit conversion or a different overload not fully detailed in the snippet, but functionally serves identity checks.*

### State Management (`NoCreate`, `SetNoCreate`)
*   **`NoCreate()`**: Returns the boolean state of the `nocreate` bit.
*   **`SetNoCreate()`**: Sets the `nocreate` bit to 1.
    *   **Usage Context**: This is critical for "read-only" spatial queries. When `Map.Main` or `ChatHandler` commands need to find objects (e.g., `HandleGameObjectSelectCommand`, `FindNearestCreature`) without triggering the expensive and potentially disruptive process of loading a grid into memory, they create a `Cell` object and call `SetNoCreate()`. This flag is passed down to the `Map`'s visitation logic, which respects it to avoid side effects.

### Difference Checks (`DiffCell`, `DiffGrid`)
*   **`DiffCell(Cell const& cell)`**: Returns `true` if the `cell_x` or `cell_y` differs between the current cell and the argument. This checks if two positions are in different sub-cells within the same grid. Used by `Map.Main/DoPlayerGridRelocation` and `Map.Main/PlayerRelocation` to detect fine-grained movement.
*   **`DiffGrid(Cell const& cell)`**: Returns `true` if the `grid_x` or `grid_y` differs. This checks if two positions are in entirely different grids. Used by `Map.Main/CreatureCellRelocation` and `ObjectGridLoader/Visit#5` to handle coarse-grained movement or grid transitions.

### Area Calculation (`CalculateCellArea`)
*   **`CalculateCellArea(float x, float y, float radius)`**: A static method that computes the bounding box of cells covered by a circle of `radius` centered at `(x, y)`.
    *   It returns a `CellArea` struct containing `low_bound` and `high_bound` `CellPair`s.
    *   If the radius is 0, it returns a single cell area.
    *   This is used by `Map.Main/MarkCellsAroundObject` and `Map.Main/UpdateCellsAroundObject` to determine which cells need to be updated when an object moves or changes visibility.

### Visitation Algorithms (`Visit`, `VisitCircle`, `VisitGridObjects`, etc.)
These methods implement the core logic for iterating over objects in the game world. They use the Visitor pattern (`TypeContainerVisitor`) to allow flexible processing of objects without modifying the container structures.

*   **`Visit` (Overloads)**:
    *   Takes a `standing_cell` (where the observer/object is), a `visitor`, a `Map` reference, and either a `WorldObject` or raw coordinates with a `radius`.
    *   **Logic**:
        1.  Validates that the standing cell is within map bounds.
        2.  Handles edge cases: if `radius <= 0`, it visits only the standing cell. If `radius > MAX_VISIBILITY_DISTANCE`, it caps the radius.
        3.  Calculates the `CellArea` covered by the radius.
        4.  If the area fits entirely within the standing cell, it visits only that cell.
        5.  If the area spans multiple cells, it decides between two strategies:
            *   **Optimized Circle (`VisitCircle`)**: If the span is larger than 4x4 cells, it uses an octagon-filling algorithm to approximate the circle, reducing unnecessary visits to corner cells.
            *   **Brute Force Rectangle**: For smaller spans, it iterates through all cells in the bounding box, skipping the standing cell (which is visited first for priority).
    *   **Collaboration**: Calls `Map.Main/Visit` (via `m.Visit`) to actually retrieve objects from the map's internal containers.

*   **`VisitCircle`**:
    *   A private helper method implementing the octagon approximation. It fills a central strip and then adds trapezoidal layers on the sides to approximate a circle. This optimization reduces the number of cells visited for large radii compared to a full square bounding box.

*   **Static Visitor Wrappers (`VisitGridObjects`, `VisitWorldObjects`, `VisitAllObjects`)**:
    *   These static methods provide convenient entry points for callers who don't want to manually construct `Cell` objects or `TypeContainerVisitor`s.
    *   They accept a center object or coordinates, a visitor, a radius, and a `dont_load` flag.
    *   If `dont_load` is true, they set the `nocreate` flag on the temporary `Cell` object.
    *   They instantiate the appropriate `TypeContainerVisitor` (for Grid, World, or Both) and delegate to the instance `Visit` method.
    *   **Usage**: Heavily used by `WorldObject.Object` methods like `FindNearestCreature`, `GetCreatureListWithEntryInGrid`, and `UpdateObjectVisibility`.

### RegularGrid2D::Cell Methods
*   **`ComputeCell`**: Static method converting float coordinates to integer grid indices based on `CELL_SIZE`.
*   **`isValid`**: Checks if the integer indices are within the `[0, CELL_NUMBER)` range.
*   **`operator==`**: Compares two `RegularGrid2D::Cell` instances by their `x` and `y` integers.

## Cross-Unit Boundaries

### Collaboration with `Map.Main`
The `Cell` unit is tightly coupled with `Map.Main`.
*   **Direction**: `Cell` calls `Map.Main/Visit`.
*   **Why**: `Cell` calculates *which* cells to look at, but `Map` holds the actual object containers (`GridTypeMapContainer`, `WorldTypeMapContainer`). `Map.Main/Visit` is responsible for retrieving the container for a given cell and applying the visitor to its contents.
*   **Data Crossing**: `Cell` passes itself (containing grid/cell indices and the `nocreate` flag) and the `TypeContainerVisitor` to `Map`.

### Collaboration with `WorldObject.Object`
*   **Direction**: `WorldObject.Object` calls `Cell` static visitors (`VisitGridObjects`, etc.).
*   **Why**: `WorldObject` needs to query nearby objects (e.g., for aggro, visibility, or interaction). It delegates the spatial calculation to `Cell`.
*   **Data Crossing**: `WorldObject` provides its position, bounding radius, and the visitor logic. `Cell` returns populated containers via the visitor.

### Collaboration with `ChatHandler` and `Creature.Main`
*   **Direction**: These units call `Cell` constructors and `SetNoCreate`.
*   **Why**: Debug commands and AI logic often need to inspect the world state without causing side effects (like loading grids). They use `Cell` to define safe query regions.

### Collaboration with `ObjectGridLoader`
*   **Direction**: `ObjectGridLoader` calls `Cell` methods like `CellX`, `CellY`, `GridX`, `GridY`, and `DiffGrid`.
*   **Why**: During map loading or saving, `ObjectGridLoader` needs to categorize objects by their grid/cell location to write them to the correct database records or memory structures.

## Data Model

The `Cell` unit itself does not directly interact with database tables. It operates on in-memory spatial indices. However, the grid/cell coordinates it generates are used by `Map.Main` and `ObjectGridLoader` to interact with tables such as `creature`, `gameobject`, and `gameobject_respawn` (implied by the context of `ObjectGridLoader` and `Map` persistence). The `Cell` unit provides the `grid_x`, `grid_y`, `cell_x`, `cell_y` values that correspond to the spatial partitioning used in these tables' loading/saving logic.

## Notable Implementation Details

1.  **Packed 64-bit Integer Storage**: The `Cell` struct uses a `union` with a `uint64 All` and a `struct Part` of five `uint8` fields. This allows efficient copying and comparison of cell identities using a single integer operation, which is crucial for performance in hot paths like player movement updates.

2.  **Octagon Approximation for Circles**: The `VisitCircle` method does not perform true distance checks for every cell. Instead, it uses a geometric approximation (central strip + trapezoids) to fill an octagon. This is a significant optimization for large radii, avoiding the overhead of calculating distances for cells clearly outside the circle but inside the bounding box. The threshold for switching to this mode is a span greater than 4x4 cells.

3.  **`nocreate` Flag Semantics**: The `nocreate` flag is a critical safety mechanism. Many spatial queries (e.g., "find nearest creature") must not trigger grid loading, as this can cause lag spikes or unexpected state changes. Callers must explicitly set this flag via `SetNoCreate()` or the `dont_load` parameter in static visitors. Failure to do so may result in unintended grid loads.

4.  **Standing Cell Priority**: In the `Visit` method, the "standing cell" (where the observer is located) is always visited first, before iterating through neighboring cells. This ensures that objects in the same cell as the observer are processed with highest priority, which is important for immediate interactions and visibility checks.

5.  **Radius Clamping**: The `Visit` method clamps the search radius to `MAX_VISIBILITY_DISTANCE`. This prevents excessive computational load from extremely large radii, enforcing a hard limit on visibility/search range regardless of input.

6.  **Dynamic Object Radius Issue**: A comment in `CellImpl.h` notes that `DynamicObjects` sometimes pass a radius of `0.0f`, which was previously problematic. The code now handles `radius <= 0.0f` by visiting only the standing cell, effectively treating it as a point query.

## Member Reference

**Cell#2**: Constructor that initializes the `Cell` from a `CellPair`, decomposing linear coordinates into hierarchical `grid_x`, `grid_y`, `cell_x`, `cell_y` indices.

**CalculateCellArea**: Static method that computes the bounding `CellArea` (min/max `CellPair`) for a circle of given `radius` centered at `(x, y)`.

**Cell**: Default constructor that zero-initializes the cell data.

**Compute**: Method that reconstructs linear `x` and `y` indices from the hierarchical grid/cell components.

**DiffCell**: Method that returns `true` if the `cell_x` or `cell_y` indices differ from another `Cell`, indicating a change in sub-cell position.

**DiffGrid**: Method that returns `true` if the `grid_x` or `grid_y` indices differ from another `Cell`, indicating a change in grid position.

**CellX**: Getter for the `cell_x` index (sub-cell X coordinate).

**CellY**: Getter for the `cell_y` index (sub-cell Y coordinate).

**GridX**: Getter for the `grid_x` index (grid X coordinate).

**GridY**: Getter for the `grid_y` index (grid Y coordinate).

**NoCreate**: Getter for the `nocreate` flag, indicating if grid loading should be suppressed.

**SetNoCreate**: Setter that enables the `nocreate` flag to prevent dynamic grid loading during visits.

**gridPair**: Method that returns a `GridPair` struct containing the `grid_x` and `grid_y` indices.

**cellPair**: Method that reconstructs and returns a `CellPair` struct from the hierarchical indices.

**operator==**: Equality operator comparing the entire 64-bit `data.All` value of two `Cell` instances.

**operator!=**: Inequality operator, negating the result of `operator==`.

**operator==#2**: Alternative equality operator (likely for implicit conversions or specific overloads, functionally similar to `operator==`).

**ComputeCell**: Static method in `RegularGrid2D::Cell` that converts float coordinates to integer grid indices.

**isValid**: Method in `RegularGrid2D::Cell` that checks if the integer indices are within valid grid bounds.

---

<!-- machine-true, projected from graph.json -->

## Map — Cell

*Source:* CellImpl.h, Cell.h, RegularGrid.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| Cell#2 | ctor | — | ChatHandler.DebugCommands/HandleDebugExp, ChatHandler.DebugCommands/HandleMmapTestArea, ChatHandler.ObjectCommands/HandleGameObjectSelectCommand, ChatHandler.UnitCommands/HandleGPSCommand, Creature.Main/SelectNearestHostileUnitInAggroRange, instance_scarlet_monastery/SetData, Map.Main/Add#3, Map.Main/CheckGridIntegrity, Map.Main/CreatureRelocation, Map.Main/CreatureRespawnRelocation, Map.Main/DoPlayerGridRelocation, Map.Main/ExistingPlayerLogin, Map.Main/ForceLoadGridsAroundPosition, Map.Main/MessageBroadcast, Map.Main/MessageBroadcast#2, Map.Main/MessageDistBroadcast, Map.Main/MessageDistBroadcast#2, Map.Main/operator(), Map.Main/operator()#2, Map.Main/PlayerRelocation, Map.Main/Remove#3, Map.Main/UpdateActiveCellsCallback, Map.Main/UpdateCellsAroundObject, ObjectGridLoader/AddUnitState, ObjectGridLoader/AddUnitState#2, ObjectGridLoader/Visit#5, Unit.Main/SelectNearestTarget, WorldObject.Object/FindNearestCreature, WorldObject.Object/FindNearestGameObject, WorldObject.Object/GetCreatureListWithEntryInGrid, WorldObject.Object/GetGameObjectListWithEntryInGrid, WorldObject.Object/UpdateObjectVisibility | — |
| CalculateCellArea | method | — | Map.Main/MarkCellsAroundObject, Map.Main/UpdateCellsAroundObject | — |
| Cell | ctor | — | — | — |
| Compute | method | — | — | — |
| DiffCell | method | — | Map.Main/DoPlayerGridRelocation, Map.Main/PlayerRelocation | — |
| DiffGrid | method | — | Map.Main/CreatureCellRelocation, Map.Main/DoPlayerGridRelocation, Map.Main/PlayerRelocation, ObjectGridLoader/Visit#5 | — |
| CellX | method | — | ChatHandler.UnitCommands/HandleGPSCommand, Map.Main/Add#3, Map.Main/AddToGrid, Map.Main/AddToGrid#2, Map.Main/AddToGrid#3, Map.Main/AddToGrid#4, Map.Main/CheckGridIntegrity, Map.Main/CreatureCellRelocation, Map.Main/CreatureRespawnRelocation, Map.Main/DoPlayerGridRelocation, Map.Main/EnsureGridLoaded, Map.Main/EnsureGridLoadedAtEnter, Map.Main/ExistingPlayerLogin, Map.Main/PlayerRelocation, Map.Main/RemoveFromGrid, Map.Main/RemoveFromGrid#2, Map.Main/RemoveFromGrid#3, ObjectGridLoader/Visit#2, ObjectGridLoader/Visit#4, ObjectGridLoader/Visit#8 | — |
| CellY | method | — | ChatHandler.UnitCommands/HandleGPSCommand, Map.Main/Add#3, Map.Main/AddToGrid, Map.Main/AddToGrid#2, Map.Main/AddToGrid#3, Map.Main/AddToGrid#4, Map.Main/CheckGridIntegrity, Map.Main/CreatureCellRelocation, Map.Main/CreatureRespawnRelocation, Map.Main/DoPlayerGridRelocation, Map.Main/EnsureGridLoaded, Map.Main/EnsureGridLoadedAtEnter, Map.Main/ExistingPlayerLogin, Map.Main/PlayerRelocation, Map.Main/RemoveFromGrid, Map.Main/RemoveFromGrid#2, Map.Main/RemoveFromGrid#3, ObjectGridLoader/Visit#2, ObjectGridLoader/Visit#4, ObjectGridLoader/Visit#8 | — |
| GridX | method | — | Map.Main/Add#3, Map.Main/CheckGridIntegrity, Map.Main/CreatureCellRelocation, Map.Main/CreatureRespawnRelocation, Map.Main/DoPlayerGridRelocation, Map.Main/EnsureGridLoaded, Map.Main/EnsureGridLoadedAtEnter, Map.Main/ExistingPlayerLogin, Map.Main/LoadGrid, Map.Main/PlayerRelocation, Map.Main/Remove#3, ObjectGridLoader/Visit#2, ObjectGridLoader/Visit#4, ObjectGridLoader/Visit#8 | — |
| GridY | method | — | Map.Main/Add#3, Map.Main/CheckGridIntegrity, Map.Main/CreatureCellRelocation, Map.Main/CreatureRespawnRelocation, Map.Main/DoPlayerGridRelocation, Map.Main/EnsureGridLoaded, Map.Main/EnsureGridLoadedAtEnter, Map.Main/ExistingPlayerLogin, Map.Main/LoadGrid, Map.Main/PlayerRelocation, Map.Main/Remove#3, ObjectGridLoader/Visit#2, ObjectGridLoader/Visit#4, ObjectGridLoader/Visit#8 | — |
| NoCreate | method | — | — | — |
| SetNoCreate | method | — | ChatHandler.DebugCommands/HandleDebugExp, ChatHandler.DebugCommands/HandleMmapTestArea, ChatHandler.ObjectCommands/HandleGameObjectSelectCommand, Creature.Main/SelectNearestHostileUnitInAggroRange, Map.Main/MessageBroadcast, Map.Main/MessageBroadcast#2, Map.Main/MessageDistBroadcast, Map.Main/MessageDistBroadcast#2, Map.Main/UpdateActiveCellsCallback, Map.Main/UpdateCellsAroundObject, Map.Main/UpdateObjectVisibility, Unit.Main/SelectNearestTarget, WorldObject.Object/FindNearestCreature, WorldObject.Object/FindNearestGameObject, WorldObject.Object/GetCreatureListWithEntryInGrid, WorldObject.Object/GetGameObjectListWithEntryInGrid | — |
| gridPair | method | — | Map.Main/CreatureCellRelocation | — |
| cellPair | method | — | — | — |
| operator== | method | — | Map.Main/DoPlayerGridRelocation, Map.Main/PlayerRelocation | — |
| operator!= | method | — | Map.Main/CheckGridIntegrity, Map.Main/CreatureCellRelocation | — |
| operator==#2 | method | — | — | — |
| ComputeCell | method | — | — | — |
| isValid | method | — | — | — |
