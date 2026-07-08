<!-- provenance: verbose -->
# GridDefines

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# GridDefines

**GridDefines.h** defines the geometric constants, type aliases, and coordinate utilities for the MaNGOS server’s spatial partitioning system. It establishes the world as a fixed hierarchy of grids and cells, providing functions to convert continuous world coordinates into discrete indices and to validate coordinate bounds.

## Purpose & Responsibilities

1.  **Spatial Constants**: Defines map dimensions (`MAP_SIZE` ~34,133 units), grid count (`MAX_NUMBER_OF_GRIDS` = 64), grid size (`SIZE_OF_GRIDS` ~533.33), cell count per grid (`MAX_NUMBER_OF_CELLS` = 16), and cell size (`SIZE_OF_GRID_CELL` ~33.33).
2.  **Coordinate Conversion**: Provides `ComputeGridPair` and `ComputeCellPair` to map `(x, y)` floats to `GridPair` or `CellPair` indices.
3.  **Validation**: Offers `IsValidMapCoord` overloads and `NormalizeMapCoord` to ensure coordinates are finite and within world bounds.
4.  **Type Aliases**: Defines `GridPair`, `CellPair`, and container types (`GridType`, `NGridType`) for storing world objects.

## Data Model

This unit interacts with no database tables.

## Member-by-Member Behavior

### `CoordPair<LIMIT>` Template Struct
Represents a 2D index bounded by `LIMIT`. Specialized as `GridPair` (limit 64) and `CellPair` (limit 1024).

*   **Constructors (`CoordPair<LIMIT>#2`, `CoordPair<LIMIT>`)**: Initialize `x_coord` and `y_coord`. Inputs are clamped to `[0, LIMIT-1]` to prevent out-of-bounds access.
*   **Arithmetic Operators (`operator<<`, `operator>>`, `operator-=`, `operator+=`)**: Modify coordinates with clamping. `<<` and `>>` adjust `x_coord`; `-=` and `+=` adjust `y_coord`. These are **not** stream operators.
*   **Comparison/Assignment (`operator==`, `operator!=`, `operator=`)**: Standard equality and assignment.
*   **`normalize`**: Clamps both coordinates to `LIMIT-1`.

### Coordinate Computation (`MaNGOS` Namespace)
*   **`Compute` (Internal)**: Converts floats to indices. Casts to `double` for precision matching MySQL. Returns `RET_TYPE(y_val, x_val)`, effectively swapping axes relative to the constructor arguments.
*   **`ComputeGridPair`**: Calculates grid indices from world `(x, y)`. Called by `Map.Main`, `ObjectMgr`, and `ChatHandler` units for object placement and grid loading.
*   **`ComputeCellPair`**: Calculates cell indices from world `(x, y)`. Called extensively by `Map.Main`, `ObjectMgr`, and `WorldObject.Object` for fine-grained spatial queries and updates.

### Validation & Normalization
*   **`NormalizeMapCoord`**: Clamps a coordinate to `[-(MAP_HALFSIZE - 0.5), +(MAP_HALFSIZE - 0.5)]`. Called by `WaypointManager` and `WorldObject.Object` to correct invalid positions.
*   **`IsValidZCoord`**: Checks if Z is within `[-400000, 400000]`.
*   **`IsValidMapCoord` (Overloads)**:
    *   Single float: Checks finiteness and X/Y bounds.
    *   `#2` (x, y): Validates both axes. Called by `ObjectMgr/LoadPointsOfInterest`.
    *   `#3` (x, y, z): Validates X, Y, and Z. Called by `Map.Main`, `Unit.Main`, `Player.Main`, and others for collision and movement checks.
    *   `#4` (x, y, z, o): Adds orientation validation (`[-4π, 4π]`). Called by `Player.Main`, `Unit.Main`, and `WorldSession` to sanitize input data.

## Notable Implementation Details

*   **Axis Swap**: `Compute` returns `RET_TYPE(y_val, x_val)`. Since `CoordPair`’s constructor takes `(x, y)`, the resulting `x_coord` holds the Y-derived index and `y_coord` holds the X-derived index. This convention is consistent across the codebase.
*   **Non-Standard Operators**: `operator<<` and `operator>>` in `CoordPair` perform arithmetic clamping, not I/O.
*   **Precision**: `Compute` uses `double` arithmetic to align with MySQL calculations, preventing drift between stored and computed coordinates.

## Member Reference

**CoordPair<LIMIT>#2**
Constructor initializing `x_coord` and `y_coord` with clamping to `[0, LIMIT-1]`.

**CoordPair<LIMIT>**
Copy constructor for `CoordPair`.

**operator==**
Equality comparison for `CoordPair`.

**operator!=**
Inequality comparison for `CoordPair`.

**operator=**
Assignment operator for `CoordPair`.

**operator<<**
Member function decreasing `x_coord` by `val`, clamped at `0`. Not a stream operator.

**operator>>**
Member function increasing `x_coord` by `val`, clamped at `LIMIT-1`. Not a stream operator.

**operator-=**
Member function decreasing `y_coord` by `val`, clamped at `0`.

**operator+=**
Member function increasing `y_coord` by `val`, clamped at `LIMIT-1`.

**normalize**
Clamps `x_coord` and `y_coord` to `LIMIT-1`.

**ComputeGridPair**
Converts world `(x, y)` to `GridPair` indices.

**ComputeCellPair**
Converts world `(x, y)` to `CellPair` indices.

**NormalizeMapCoord**
Clamps a float coordinate to valid world bounds.

**IsValidZCoord**
Checks if Z is within `[-400000, 400000]`.

**IsValidMapCoord**
Checks if a single float is finite and within X/Y world bounds.

**IsValidMapCoord#2**
Checks if two floats (x, y) are valid.

**IsValidMapCoord#3**
Checks if three floats (x, y, z) are valid.

**IsValidMapCoord#4**
Checks if four floats (x, y, z, o) are valid, including orientation bounds.

---

<!-- machine-true, projected from graph.json -->

## Map — GridDefines

*Source:* GridDefines.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| CoordPair<LIMIT>#2 | ctor | — | — | — |
| CoordPair<LIMIT> | ctor | — | — | — |
| operator== | function | — | — | — |
| operator!= | function | — | — | — |
| operator= | function | — | — | — |
| operator<< | function | — | — | — |
| operator>> | function | — | — | — |
| operator-= | function | — | — | — |
| operator+= | function | — | — | — |
| normalize | function | — | — | — |
| ComputeGridPair | function | — | ChatHandler.UnitCommands/HandleGPSCommand, Corpse/Create#2, Corpse/LoadFromDB, Map.Main/AddToActive, Map.Main/RemoveFromActive, MapManager/ExistMapAndVMap | — |
| ComputeCellPair | function | — | ChatHandler.DebugCommands/HandleDebugExp, ChatHandler.DebugCommands/HandleMmapTestArea, ChatHandler.ObjectCommands/HandleGameObjectSelectCommand, ChatHandler.UnitCommands/HandleGPSCommand, Creature.Main/SelectNearestHostileUnitInAggroRange, instance_scarlet_monastery/SetData, Map.Main/ActiveObjectsNearGrid, Map.Main/Add#3, Map.Main/Add#5, Map.Main/CheckGridIntegrity, Map.Main/CreatureRelocation, Map.Main/CreatureRespawnRelocation, Map.Main/DoPlayerGridRelocation, Map.Main/ExistingPlayerLogin, Map.Main/ForceLoadGridsAroundPosition, Map.Main/MessageBroadcast, Map.Main/MessageBroadcast#2, Map.Main/MessageDistBroadcast, Map.Main/MessageDistBroadcast#2, Map.Main/operator(), Map.Main/operator()#2, Map.Main/PlayerRelocation, Map.Main/Remove#3, MapPersistentStateMgr/AddCreatureToGrid, MapPersistentStateMgr/AddGameobjectToGrid, MapPersistentStateMgr/RemoveCreatureFromGrid, MapPersistentStateMgr/RemoveGameobjectFromGrid, ObjectAccessor/AddCorpse, ObjectAccessor/RemoveCorpse, ObjectGridLoader/Visit#5, ObjectMgr/AddCreatureToGrid, ObjectMgr/AddGameobjectToGrid, ObjectMgr/RemoveCreatureFromGrid, ObjectMgr/RemoveGameobjectFromGrid, Unit.Main/SelectNearestTarget, WorldObject.Object/FindNearestCreature, WorldObject.Object/FindNearestGameObject, WorldObject.Object/GetCreatureListWithEntryInGrid, WorldObject.Object/GetGameObjectListWithEntryInGrid, WorldObject.Object/UpdateObjectVisibility | — |
| NormalizeMapCoord | function | — | WaypointManager/Load, WorldObject.Object/GetFirstCollision, WorldObject.Object/GetNearPoint2DAroundPosition, WorldObject.Object/MovePositionToFirstCollision | — |
| IsValidZCoord | function | — | — | — |
| IsValidMapCoord | function | — | — | — |
| IsValidMapCoord#2 | function | — | ObjectMgr/LoadPointsOfInterest | — |
| IsValidMapCoord#3 | function | — | Conditions/IsValid, Creature.Main/GetRespawnCoord, FearMovementGenerator/_getPoint, FleeingMovementGenerator/_getPoint, GridMap/IsInWater, GridMap/IsUnderWater, Map.Main/FindCollisionModel, Map.Main/FindDynamicObjectCollisionModel, Map.Main/GetHeight, Map.Main/GetLosHitPosition, Map.Main/GetWalkHitPosition, Map.Main/GetWalkRandomPosition, Map.Main/isInLineOfSight, Player.Main/TeleportToHomebind, SpellCastTargetsInfo/read, Transport/UpdatePassengerPosition, Unit.Main/UpdateSplineMovement, WorldObject.Object/GetRandomPoint, WorldObject.Object/Relocate#2 | — |
| IsValidMapCoord#4 | function | — | Player.Main/LoadFromDB, Player.Main/SetPosition, Player.Main/_LoadBGData, ScriptMgr/LoadScripts, Unit.Main/ExtrapolateMovement, WaypointManager/Load, WorldObject.Object/IsPositionValid, WorldSession.MovementHandler/VerifyMovementInfo | — |
