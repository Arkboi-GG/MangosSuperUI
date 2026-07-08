# LocationInfo

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# LocationInfo

**LocationInfo** is a lightweight data structure within the `VMAP` namespace, defined in `MapTree.h`. It serves as a container for the results of spatial queries performed by the Virtual Map (VMap) system, specifically regarding collision detection and terrain height calculations.

### Purpose & Responsibilities

The primary responsibility of `LocationInfo` is to aggregate and return specific geometric and object-related data resulting from a ray-cast or point-query against the virtual map's collision models. It is not a standalone actor; it has no methods or internal logic beyond its constructor. Instead, it acts as a value-return type for functions that need to communicate multiple pieces of information about a specific location in the game world back to the caller.

The structure captures four distinct pieces of information:
1.  **`hitInstance`**: A pointer to the specific `ModelInstance` that was intersected or identified at the queried location.
2.  **`hitModel`**: A pointer to the parent `GroupModel` containing the hit instance.
3.  **`ground_Z`**: The vertical coordinate (Z-axis) of the ground or surface at the queried location.
4.  **`rootId`**: An identifier for the root node or area associated with the location, likely used for area-of-effect checks or zone identification.

### Member-by-Member Behavior

#### **LocationInfo** (Constructor)
The default constructor initializes the structure with safe, neutral values indicating that no valid data has been retrieved yet. This ensures that if a query fails or returns no hits, the consumer of this structure can reliably detect the absence of valid data by checking these defaults.

*   **`hitInstance`**: Initialized to `nullptr`. Indicates no specific model instance was hit.
*   **`hitModel`**: Initialized to `nullptr`. Indicates no group model was associated with the hit.
*   **`ground_Z`**: Initialized to `-G3D::inf()` (negative infinity). This is a sentinel value indicating that no valid ground height was found. In many spatial algorithms, negative infinity is used to represent "no floor" or "void," allowing comparison logic to treat it as lower than any possible real-world coordinate.
*   **`rootId`**: Initialized to `-1`. A standard invalid ID value, indicating no specific root area was identified.

### Cross-Unit Boundaries

**LocationInfo** is tightly coupled with the `VMapManager2` unit, specifically its `GetLiquidLevel` method.

*   **Called by:** `VMapManager2/GetLiquidLevel`
    *   **Direction:** Outbound from `VMapManager2` to `LocationInfo` (instantiation).
    *   **Collaboration:** `VMapManager2` uses `LocationInfo` as a temporary or return buffer to store the results of liquid level calculations. When `GetLiquidLevel` performs its spatial queries, it populates an instance of `LocationInfo` with the relevant hit data and ground Z coordinates. This allows `VMapManager2` to pass complex result sets back to its callers without requiring them to parse raw pointers or separate variables. The use of `LocationInfo` here suggests that liquid level determination relies on the same underlying collision geometry queries used for general ground height and model intersection.

### Data Model

**LocationInfo** does not interact with any database tables. It is a purely in-memory C++ structure used for runtime spatial calculations. No SQL queries or table references are present in its definition or usage.

### Notable Implementation Details

1.  **Sentinel Values:** The choice of `-G3D::inf()` for `ground_Z` is critical. Consumers of `LocationInfo` must check if `ground_Z` is finite before using it. Using negative infinity allows the system to distinguish between "no ground found" and "ground found at Z=0".
2.  **Const Correctness:** The pointers `hitInstance` and `hitModel` are declared as `const*`, indicating that `LocationInfo` holds references to existing model objects but does not own them or modify them. This prevents accidental deletion or alteration of the virtual map's geometry through this structure.
3.  **Minimal Overhead:** As a simple struct with no virtual functions or dynamic allocations, `LocationInfo` is cheap to construct and copy. This makes it suitable for use in performance-sensitive loops, such as those iterating over multiple rays or positions during pathfinding or line-of-sight checks.
4.  **Namespace Context:** Defined within `VMAP`, it is part of the Virtual Map abstraction layer, which separates the engine's core logic from the specific implementation of virtual map data (likely M2/MDD files in World of Warcraft context).

## Member Reference

**LocationInfo**
Default constructor for the `LocationInfo` structure. Initializes `hitInstance` and `hitModel` to `nullptr`, `ground_Z` to negative infinity (`-G3D::inf()`), and `rootId` to `-1`. This provides a safe initial state indicating no valid spatial data has been retrieved. Called by `VMapManager2/GetLiquidLevel` to prepare a result buffer for liquid level calculations.

---

<!-- machine-true, projected from graph.json -->

## Map — LocationInfo

*Source:* MapTree.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| LocationInfo | ctor | — | VMapManager2/GetLiquidLevel | — |
