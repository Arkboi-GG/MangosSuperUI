<!-- provenance: verbose -->
# Geometry

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Geometry

The `Geometry` namespace provides inline utility functions for 2D and 3D spatial calculations, serving as the foundational mathematical layer for position, distance, angle, and orientation logic. It operates purely on primitive floats or objects exposing `.x`, `.y`, and `.z` members, with no runtime dependencies on other compiled units or database tables.

## Member-by-Member Behavior

### Angle and Distance Calculations

**`GetAngle`** computes the counter-clockwise angle in radians from an "own" position to a "target" position in 2D space using `atan2(dy, dx)`. The result is normalized to `[0, 2π]` by adding `2π` if the raw result is negative. It exists in two overloads: one taking four explicit floats, and a templated version accepting any two types with `.x` and `.y` members.

**`GetDistance2D`** calculates the Euclidean distance between two 2D points via `sqrt(dx^2 + dy^2)`. It clamps the result to `0` if the computed distance is `<= 0`, handling identical points or floating-point precision artifacts. It provides both explicit float and templated overloads.

**`GetDistance3D`** extends distance calculation to 3D space, incorporating the Z-axis (`dz`). It follows the same pattern as `GetDistance2D`, computing `sqrt(dx^2 + dy^2 + dz^2)` and clamping negative results to `0`.

### Range Checking

**`IsInRange2D`** determines whether a target point lies within an annular region defined by `minRange` and `maxRadius` around an origin. It operates on squared distances to avoid square roots:
1. Calculates `distsq = dx^2 + dy^2`.
2. If `minRange > 0.0f`, returns `false` if `distsq < minRange^2` (too close).
3. Returns `true` if `distsq < maxRange^2` (within range), otherwise `false`.

### Position Manipulation

**`GetNearPoint2DAroundPosition`** calculates a new 2D coordinate (`x`, `y`) at a specified `distance2d` and `absAngle` from an origin (`ownX`, `ownY`) using trigonometric projection. Crucially, it calls `MaNGOS::NormalizeMapCoord` on both `x` and `y` to ensure the resulting coordinates remain within valid game world map bounds.

**`Move2dPointTowards`** adjusts a second point (`x2`, `y2`) to move it closer to or further from a first point (`x1`, `y1`) by a specified `dist`.
1. Calculates the delta vector (`dx`, `dy`) from `x2` to `x1`.
2. If the vector length is `0.0f`, it returns immediately to avoid division by zero.
3. Normalizes the delta vector and adds `nx * dist` and `ny * dist` to `x2` and `y2`.
Positive `dist` moves `x2` toward `x1`; negative `dist` moves it away.

### Orientation Normalization

**`NormalizeOrientation`** constrains a rotation value (in radians) to `[0, 2π]`. It handles negative inputs specially because `fmod` behavior with negatives can be inconsistent:
1. If `o < 0`: Takes the absolute value, applies `fmod` with `2π`, negates the result, and adds `2π`.
2. If `o >= 0`: Applies `fmod(o, 2π)`.

## Cross-Unit Boundaries

`Geometry` does not call out to other units. It is extensively called by core subsystems for spatial reasoning:

*   **WorldObject.Object/add**: Calls `GetAngle`, `GetDistance2D`, `IsInRange2D`, and `NormalizeOrientation` for basic object positioning, facing, and proximity checks.
*   **GameObject/GetClosestChairSlotPosition**: Calls `GetDistance2D` to find the nearest seating slot.
*   **Player.Main/IsOutdoorOnTransport**: Calls `GetDistance3D` to determine if a player is sufficiently far from a transport vessel.
*   **Map.Main/GetWalkRandomPosition**: Calls `GetNearPoint2DAroundPosition` to generate random walk targets within valid map coordinates.
*   **WorldObject.PathFinder/BuildPathStep**: Calls `GetNearPoint2DAroundPosition` and `NormalizeOrientation` for pathfinding steps and orientation adjustments.
*   **Transport/**: `CalculatePassengerOffset`, `CalculatePassengerOrientation`, and `CalculatePassengerPosition` call `NormalizeOrientation` to correctly orient passengers on moving vehicles.
*   **TransportMgr/GenerateWaypoints**: Calls `NormalizeOrientation` to ensure valid waypoint orientations.
*   **Unit.SpellAuras/TriggerSpell**: Calls `NormalizeOrientation` for spell casting directions.
*   **WorldObject.Object/HasInArc** (and `HasInArc#2`): Call `NormalizeOrientation` for arc checks (e.g., front cone targeting).
*   **Player.Main/SaveToDB**: Calls `NormalizeOrientation` when persisting player data.

## Data Model

This unit does not access any database tables. All operations are performed in memory on transient geometric data.

## Notable Implementation Details

1.  **Square Root Avoidance**: `IsInRange2D` compares squared distances against squared radii to avoid the computational cost of `sqrt`.
2.  **Negative Angle Handling**: `NormalizeOrientation` manually handles negative inputs to ensure consistent `[0, 2π]` results across platforms, as `fmod` may return negative values for negative inputs.
3.  **Map Coordinate Safety**: `GetNearPoint2DAroundPosition` calls `MaNGOS::NormalizeMapCoord` to prevent out-of-bounds coordinates that could cause crashes.
4.  **Division by Zero Protection**: `Move2dPointTowards` checks for zero-length vectors before normalization to prevent NaN propagation.
5.  **Distance Clamping**: `GetDistance2D` and `GetDistance3D` clamp results to `0` if `<= 0`, ensuring explicit "no distance" representation.

## Member Reference

**GetAngle**
Computes the 2D angle in radians from an origin to a target, normalized to `[0, 2π]`. Uses `atan2` and adjusts negative results. Called by `WorldObject.Object/add`.

**GetDistance2D**
Calculates the Euclidean distance between two 2D points. Clamps result to `0` if `<= 0`. Called by `GameObject/GetClosestChairSlotPosition` and `WorldObject.Object/add`.

**GetDistance3D**
Calculates the Euclidean distance between two 3D points. Clamps result to `0` if `<= 0`. Called by `Player.Main/IsOutdoorOnTransport`.

**IsInRange2D**
Checks if a target is within a 2D annular range (between `minRange` and `maxRange`) using squared distances for performance. Called by `WorldObject.Object/add`.

**GetNearPoint2DAroundPosition**
Calculates a new 2D position at a given distance and angle from an origin, then normalizes the resulting coordinates to valid map bounds. Called by `Map.Main/GetWalkRandomPosition` and `WorldObject.PathFinder/BuildPathStep`.

**NormalizeOrientation**
Normalizes a rotation angle to the range `[0, 2π]`, with special handling for negative inputs to ensure correctness across platforms. Called by `Player.Main/SaveToDB`, `Transport/CalculatePassengerOffset`, `Transport/CalculatePassengerOrientation`, `Transport/CalculatePassengerPosition`, `TransportMgr/GenerateWaypoints`, `Unit.SpellAuras/TriggerSpell`, `WorldObject.Object/add`, `WorldObject.Object/HasInArc`, `WorldObject.Object/HasInArc#2`, and `WorldObject.PathFinder/BuildPathStep`.

**Move2dPointTowards**
Moves a 2D point closer to or further from another point by a specified distance. Handles division by zero if points are identical. Not called by any other unit in the provided map.

---

<!-- machine-true, projected from graph.json -->

## Map — Geometry

*Source:* Geometry.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GetAngle | function | — | WorldObject.Object/add | — |
| GetDistance2D | function | — | GameObject/GetClosestChairSlotPosition, WorldObject.Object/add | — |
| GetDistance3D | function | — | Player.Main/IsOutdoorOnTransport | — |
| IsInRange2D | function | — | WorldObject.Object/add | — |
| GetNearPoint2DAroundPosition | function | — | Map.Main/GetWalkRandomPosition, WorldObject.PathFinder/BuildPathStep | — |
| NormalizeOrientation | function | — | Player.Main/SaveToDB, Transport/CalculatePassengerOffset, Transport/CalculatePassengerOrientation, Transport/CalculatePassengerPosition, TransportMgr/GenerateWaypoints, Unit.SpellAuras/TriggerSpell, WorldObject.Object/add, WorldObject.Object/HasInArc, WorldObject.Object/HasInArc#2, WorldObject.PathFinder/BuildPathStep | — |
| Move2dPointTowards | function | — | — | — |
