# TaxiPathNodePtr

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# TaxiPathNodePtr

**TaxiPathNodePtr** is a lightweight wrapper struct defined in `DBCStructure.h` that encapsulates a constant pointer to a `TaxiPathNodeEntry`. It serves as an adapter between the raw data structures loaded from Blizzard’s DBC files and the generic `Path` template class. By wrapping the pointer, `TaxiPathNodePtr` allows the `Path` template to manage sequences of taxi waypoints while providing an implicit conversion operator that lets consumer code treat the wrapper as a direct reference to the underlying entry. This design simplifies iteration and access to coordinate and flag data during path traversal or rendering, avoiding the need for explicit dereferencing throughout the codebase.

This unit contains no business logic, state management, or database interactions. Its sole responsibility is type adaptation and interface compatibility for the taxi path system.

## Member-by-Member Behavior

The unit defines two constructors and one implicit conversion operator.

### Construction
*   **Default Constructor (`TaxiPathNodePtr()`)**: Initializes the internal pointer `i_ptr` to `nullptr`. This is used when creating empty path nodes or default-initialized instances of the wrapper.
*   **Pointer Constructor (`TaxiPathNodePtr(TaxiPathNodeEntry const* ptr)`)**: Initializes `i_ptr` with the provided constant pointer to a `TaxiPathNodeEntry`. This is the primary mechanism for adding nodes to a path, typically invoked during the loading of DBC data.

### Access
*   **Implicit Conversion Operator (`operator TaxiPathNodeEntry const& () const`)**: Allows a `TaxiPathNodePtr` instance to be treated directly as a reference to the underlying `TaxiPathNodeEntry`. This eliminates the need for explicit dereferencing (e.g., accessing `node.x` instead of `node.i_ptr->x`), simplifying code that iterates over `TaxiPathNodeList`.

## Cross-Unit Boundaries

*   **Called by `DBCStores/LoadDBCStores`**: The `DBCStores` unit (specifically the logic responsible for loading DBC files) instantiates `TaxiPathNodePtr` objects. During the initialization of the game server, DBC data for taxi paths is parsed. Each node in a path is wrapped in a `TaxiPathNodePtr` and inserted into a `TaxiPathNodeList`. This boundary represents the transition from raw binary DBC parsing to structured path data usable by the game world simulation.
*   **Calls out**: None. This unit is a pure data wrapper and does not invoke functions in other units.

## Data Model

This unit does not interact with database tables directly. However, it wraps data derived from the **TaxiPathNode.dbc** file. The underlying `TaxiPathNodeEntry` struct reflects the columns of this DBC file:
*   `path`: The ID of the taxi path this node belongs to.
*   `index`: The sequential order of the node within the path.
*   `mapid`, `x`, `y`, `z`: The spatial coordinates of the waypoint.
*   `actionFlag`: Flags determining client-side behavior at this node (e.g., dismount, play animation).
*   `delay`: Time delay before proceeding to the next node.

## Notable Implementation Details

*   **Const Correctness**: The internal pointer `i_ptr` is a pointer to a `const` entry (`TaxiPathNodeEntry const*`). This ensures that once a taxi path node is loaded from the DBC, its data cannot be modified through this wrapper, preserving the integrity of static game data.
*   **Implicit Conversion Risk**: While convenient, the implicit conversion operator can lead to ambiguity if multiple conversion paths exist. However, in the context of `Path<TaxiPathNodePtr, TaxiPathNodeEntry const>`, this design is intentional to allow the `Path` template algorithms to work seamlessly with the underlying data structures without verbose dereferencing.
*   **Null Pointer Handling**: The default constructor sets `i_ptr` to `nullptr`. Code consuming `TaxiPathNodePtr` must ensure the pointer is valid before invoking the conversion operator, otherwise, dereferencing a null pointer will cause a crash. The `DBCStores` unit is responsible for ensuring only valid pointers are passed to the constructor.

## Member Reference

**TaxiPathNodePtr** (default ctor): Initializes the internal pointer `i_ptr` to `nullptr`. Used for default initialization of path nodes.

**TaxiPathNodePtr#2** (ctor): Initializes `i_ptr` with the provided `TaxiPathNodeEntry const*`. Called by `DBCStores/LoadDBCStores` during DBC loading to wrap raw DBC entries into path-compatible nodes.

---

<!-- machine-true, projected from graph.json -->

## Map — TaxiPathNodePtr

*Source:* DBCStructure.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| TaxiPathNodePtr | ctor | — | — | — |
| TaxiPathNodePtr#2 | ctor | — | DBCStores/LoadDBCStores | — |
