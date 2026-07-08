# GroupModel_Raw

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`GroupModel_Raw` is a lightweight data structure within the `VMAP` namespace, defined in `TileAssembler.h`. It represents a single **group** of geometry extracted from a World Model Object (WMO) file during the static world geometry processing pipeline. Specifically, it holds the raw vertex data, triangle indices, bounding box, and metadata (such as flags and liquid references) for one logical group within a larger WMO instance.

This structure serves as an intermediate container. It is populated by reading binary WMO file data and is subsequently consumed by the `TileAssembler` class to construct balanced Binary Space Partitioning (BSP) trees for collision detection and rendering optimization. It does not perform any complex logic itself; its primary responsibility is to aggregate related geometric primitives and their associated properties into a cohesive unit for further processing.

## Member-by-Member Behavior

The unit consists of a single member: the constructor.

### Initialization
The **`GroupModel_Raw`** constructor initializes all member variables to safe default states. This ensures that instances created via value initialization or default construction do not contain garbage data, which is critical for memory safety when these structs are stored in vectors or maps.

*   **`mogpflags`**: Initialized to `0`. These flags likely correspond to MOGP (Model Object Group Properties) bits from the WMO format, indicating properties like visibility or collision relevance.
*   **`GroupWMOID`**: Initialized to `0`. This identifier links the group back to its parent WMO definition.
*   **`liquidflags`**: Initialized to `0`. Flags related to liquid interaction or presence within this group's bounds.
*   **`liquid`**: Initialized to `nullptr`. A pointer to a `WmoLiquid` object, which is populated later if the group contains liquid data.
*   **`bounds`**, **`triangles`**, **`vertexArray`**: These are default-constructed by their respective types (`G3D::AABox`, `std::vector`). The vectors start empty, and the bounding box starts with invalid/default coordinates until populated by the `Read` method (which is declared in this header but implemented elsewhere, likely in `TileAssembler.cpp` or a related file).

## Cross-Unit Boundaries

*   **Called By**: The MAP indicates no external units explicitly call this constructor. However, in practice, `GroupModel_Raw` instances are typically constructed implicitly when `WorldModel_Raw::groupsArray` (a `std::vector<GroupModel_Raw>`) reserves space or pushes back elements, or when individual instances are default-initialized before being passed to the `Read` method.
*   **Calls Out**: The constructor performs no external calls. It relies solely on the default constructors of its member types (`uint32`, `G3D::AABox`, `std::vector`, `class WmoLiquid*`).

## Data Model

This unit does not interact with any database tables. It processes in-memory binary data structures derived from WMO files.

## Notable Implementation Details

1.  **Default Initialization Safety**: The explicit initialization of pointers (`liquid`) and integer flags (`mogpflags`, `GroupWMOID`, `liquidflags`) in the constructor initializer list is a defensive programming practice. Without this, default construction of the struct would leave these primitive members uninitialized (indeterminate values), leading to undefined behavior if accessed before the `Read` method populates them.
2.  **Dependency on External Types**: The struct depends on `G3D::AABox`, `MeshTriangle`, and `class WmoLiquid`. These types are defined outside this unit. `MeshTriangle` is likely a simple struct holding three vertex indices or coordinates, while `WmoLiquid` handles liquid-specific geometry or height data.
3.  **Memory Management**: The destructor `~GroupModel_Raw()` is declared but not defined in this header. Given that `liquid` is a raw pointer, the destructor in the corresponding `.cpp` file likely deletes the `liquid` object if it is non-null. Maintainers must ensure that if `liquid` is assigned, it is heap-allocated and properly cleaned up to avoid memory leaks.
4.  **Role in Pipeline**: As part of the `TileAssembler` system, `GroupModel_Raw` is a transient data holder. It exists only during the conversion phase where raw WMO data is transformed into optimized VMAP (Virtual Map) tiles. Once the BSP trees are built, these raw structures are discarded.

## Member Reference

**GroupModel_Raw**
Constructor for the `GroupModel_Raw` struct. Initializes `mogpflags`, `GroupWMOID`, and `liquidflags` to `0`, and `liquid` to `nullptr`. Default constructs `bounds`, `triangles`, and `vertexArray`. Ensures a clean state for subsequent data loading via the `Read` method.

---

<!-- machine-true, projected from graph.json -->

## Map — GroupModel_Raw

*Source:* TileAssembler.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GroupModel_Raw | ctor | — | — | — |
