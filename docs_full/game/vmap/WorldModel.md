# WorldModel

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# WorldModel

## Purpose & Responsibilities

`WorldModel` is a lightweight container within the `VMAP` namespace that holds a single static 3D model instance (derived from `.wmo` or `.m2` files) in its original coordinate space. It serves as the leaf node in the virtual map hierarchy, aggregating `GroupModel` sub-parts and managing a Bounding Interval Hierarchy (`BIH`) for accelerated spatial queries.

This unit’s primary role is to store metadata—specifically the root WMO identifier and operational flags—that governs how the model interacts with collision and visibility systems. Heavy geometric computation (ray casting, containment checks) is delegated to internal `BIH` structures and `GroupModel` instances; `WorldModel` itself provides only the configuration interface for these properties.

## Member-by-Member Behavior

The documented members are simple state setters and getters. They configure the identity and behavioral flags of the model instance after construction.

*   **Construction**: Initializes the model with a null root ID and zeroed flags.
*   **Identity**: `setRootWmoID` assigns the unique WMO identifier, linking the in-memory geometry to its source asset.
*   **Configuration**: `setModelFlags` and `getModelFlags` manage a `uint32` bitmask that dictates model properties (e.g., indoor/outdoor status, collision solidity).

## Cross-Unit Boundaries

`WorldModel` is instantiated and configured by map loading and game object initialization systems. It does not call out to other units in the mapped members.

### Incoming Dependencies (Called By)

1.  **`BoundsTrait.TileAssembler/convertRawFile`**:
    *   **Action**: Constructs `WorldModel` and calls `setRootWmoID`.
    *   **Context**: During the conversion of raw map files into VMAP binary format, this assembler creates model instances and tags them with their source WMO ID for traceability.

2.  **`VMapManager2/acquireModelInstance`**:
    *   **Action**: Constructs `WorldModel`.
    *   **Context**: The central VMAP manager retrieves or creates model instances for runtime use, initializing them before populating geometry.

3.  **`GameObjectModel/initialize`**, **`MapTree/InitMap`**, **`MapTree/LoadMapTile`**:
    *   **Action**: Call `setModelFlags`.
    *   **Context**: As map tiles or game objects are loaded, these units apply specific flags to the `WorldModel` to define its collision and visibility behavior within the game world.

## Data Model

`WorldModel` does not interact with any database tables. All data is derived from in-memory parsing of model files and stored in binary VMAP structures.

## Notable Implementation Details

*   **Coordinate Space**: The class comment specifies it holds models in their "original coordinate space." Transformations are applied externally by callers (e.g., `VMapManager2`) before queries are executed.
*   **Flag Semantics**: While `modelFlags` is opaque in this header, related `GroupModel` comments suggest bits like `0x8` (outdoor) and `0x2000` (indoor) influence collision logic.
*   **Delegation**: Methods like `IntersectRay` (not in MAP) delegate to the internal `BIH groupTree`. The mapped members only manage the scalar metadata required to configure that tree.

## Member Reference

**WorldModel**
Default constructor. Initializes `RootWMOID` to 0 and `modelFlags` to 0. Called by `BoundsTrait.TileAssembler/convertRawFile` and `VMapManager2/acquireModelInstance`.

**setRootWmoID**
Setter for the root WMO identifier (`uint32`). Links the instance to its source asset file. Called by `BoundsTrait.TileAssembler/convertRawFile`.

**setModelFlags**
Setter for the model property bitmask (`uint32`). Defines collision and visibility behaviors. Called by `GameObjectModel/initialize`, `MapTree/InitMap`, and `MapTree/LoadMapTile`.

**getModelFlags**
Getter for the model property bitmask. Returns the current `modelFlags` value. Not called by any other unit in the MAP.

---

<!-- machine-true, projected from graph.json -->

## Map — WorldModel

*Source:* WorldModel.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| WorldModel | ctor | — | BoundsTrait.TileAssembler/convertRawFile, VMapManager2/acquireModelInstance | — |
| setRootWmoID | method | — | BoundsTrait.TileAssembler/convertRawFile | — |
| setModelFlags | method | — | GameObjectModel/initialize, MapTree/InitMap, MapTree/LoadMapTile | — |
| getModelFlags | method | — | — | — |
