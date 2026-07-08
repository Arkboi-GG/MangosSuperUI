# GroupModel

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# GroupModel

**GroupModel** is a data structure within the `VMAP` namespace representing a single logical group (sub-mesh) from a World Model Object (WMO) file. It holds the axis-aligned bounding box (`AABox`), semantic flags (`mogpFlags`), parent WMO ID, and optional liquid height data for that group. It serves as a leaf node in the spatial hierarchy, providing metadata and geometry for collision and visibility queries performed by `WorldModel`.

### Member-by-Member Behavior

Members handle lifecycle, data attachment, and metadata retrieval.

*   **Lifecycle**: The default constructor (**`GroupModel`**) zeroes flags and IDs. The parameterized constructor (**`GroupModel#2`**) initializes the bounding box, flags, and WMO ID from raw file data. The destructor (**`~GroupModel`**) deletes the owned `WmoLiquid` pointer.
*   **Data Attachment**: **`setLiquidData`** transfers ownership of a `WmoLiquid` object by assigning it to the internal pointer and nullifying the caller’s reference.
*   **Metadata Access**: **`GetBound`**, **`GetMogpFlags`**, and **`GetWmoID`** return constant references or values for the bounding box, group flags (e.g., indoor/outdoor bits), and parent WMO ID, respectively.

### Cross-Unit Boundaries

*   **Called by `BoundsTrait.TileAssembler/convertRawFile`**: Uses **`GroupModel#2`** to instantiate groups from parsed WMO chunks and **`setLiquidData`** to attach liquid grids.
*   **Called by `BoundsTrait.WorldModel`**: Uses **`GetBound`** (via `getBounds`) for coarse spatial culling, and **`GetMogpFlags`** / **`GetWmoID`** (via `IntersectPoint`) to resolve semantic context during point-intersection tests.

### Data Model

This unit interacts with no database tables. It processes in-memory geometry derived from game asset files.

### Notable Implementation Details

*   **Ownership Transfer**: `setLiquidData` uses move-like semantics (`liquid = nullptr`) to prevent double-free errors; the destructor assumes sole ownership of `iLiquid`.
*   **Flag Bits**: `iMogpFlags` encodes spatial semantics; comments indicate bit `0x8` for outdoor and `0x2000` for indoor, influencing collision logic.

## Member Reference

**GroupModel**  
Default constructor. Initializes `iMogpFlags` to 0, `iGroupWMOID` to 0, and `iLiquid` to `nullptr`.

**GroupModel#2**  
Parameterized constructor. Initializes `iBound`, `iMogpFlags`, and `iGroupWMOID`. Called by `BoundsTrait.TileAssembler/convertRawFile`.

**~GroupModel**  
Destructor. Deletes `iLiquid` to release owned liquid data.

**setLiquidData**  
Transfers ownership of `WmoLiquid*` to `iLiquid` and nullifies the input reference. Called by `BoundsTrait.TileAssembler/convertRawFile`.

**GetBound**  
Returns `const G3D::AABox&` to `iBound`. Called by `BoundsTrait.WorldModel/getBounds`.

**GetMogpFlags**  
Returns `iMogpFlags`. Called by `BoundsTrait.WorldModel/IntersectPoint`.

**GetWmoID**  
Returns `iGroupWMOID`. Called by `BoundsTrait.WorldModel/IntersectPoint`.

---

<!-- machine-true, projected from graph.json -->

## Map — GroupModel

*Source:* WorldModel.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GroupModel | ctor | — | — | — |
| GroupModel#2 | ctor | — | BoundsTrait.TileAssembler/convertRawFile | — |
| ~GroupModel | dtor | — | — | — |
| setLiquidData | method | — | BoundsTrait.TileAssembler/convertRawFile | — |
| GetBound | method | — | BoundsTrait.WorldModel/getBounds | — |
| GetMogpFlags | method | — | BoundsTrait.WorldModel/IntersectPoint | — |
| GetWmoID | method | — | BoundsTrait.WorldModel/IntersectPoint | — |
