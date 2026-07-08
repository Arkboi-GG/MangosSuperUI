# KeyFrame

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

The `KeyFrame` struct, defined in `TransportMgr.h`, represents a single waypoint in a transport’s movement path. Transports (ships, zeppelins, elevators) follow paths defined by `TaxiPathNodeEntry` records from the DBC files. `KeyFrame` wraps a `TaxiPathNodeEntry` pointer with runtime metadata—timing, distances, splines, and behavioral flags—calculated during path generation by `TransportMgr/GenerateWaypoints`. It enables the `Transport` class to interpolate position, handle teleports, and manage stops during its update cycle.

## Member-by-Member Behavior

### Construction

**`KeyFrame`**  
Initializes a `KeyFrame` from a `TaxiPathNodeEntry` reference. Sets `Index` to 0, `Node` to the provided entry, `InitialOrientation` to 0.0f, distance fields (`DistSinceStop`, `DistUntilStop`, `DistFromPrev`) to -1.0f (uncalculated), time fields to 0, flags (`Teleport`, `Update`) to false, `Spline` to nullptr, and look-ahead fields (`NextDistFromPrev`, `NextArriveTime`) to 0. Called by `TransportMgr/GenerateWaypoints`.

### State Queries

**`IsTeleportFrame`**  
Returns the `Teleport` flag. Indicates if the transition to this frame is instantaneous. Called by `Transport/Update#2`.

**`IsUpdateFrame`**  
Returns the `Update` flag. Indicates if the transport must perform specific update logic at this frame. Called by `Transport/Update#2`.

**`IsStopFrame`**  
Returns true if `Node->actionFlag == 2`, identifying the frame as a stop point in the path. Called by `TransportMgr/GenerateWaypoints` to segment paths and by `Transport/Update#2` to handle stop behavior.

## Cross-Unit Boundaries

### Called By: `TransportMgr/GenerateWaypoints`
`TransportMgr/GenerateWaypoints` constructs the transport’s path by creating `KeyFrame` instances for each DBC node. It calls the `KeyFrame` constructor to initialize frames and uses `IsStopFrame` to detect stop points, enabling correct calculation of distance segments (`DistSinceStop`, `DistUntilStop`).

### Called By: `Transport/Update#2`
During runtime, `Transport/Update#2` queries `IsTeleportFrame` and `IsUpdateFrame` to determine movement behavior (interpolation vs. teleport) and trigger necessary updates. It may also use `IsStopFrame` to handle pausing or state transitions at stop points.

## Data Model

`KeyFrame` does not access database tables directly. It relies on `TaxiPathNodeEntry` data from the `TaxiPathNodes.dbc` client data file, accessed via the `Node` pointer. No SQL queries are involved.

## Notable Implementation Details

1.  **Sentinel Values**: Distance fields are initialized to `-1.0f` to indicate they are uncalculated. `TransportMgr/GenerateWaypoints` populates these after construction.
2.  **Look-Ahead Data**: `NextDistFromPrev` and `NextArriveTime` store data for the subsequent frame, avoiding dynamic look-ups during the high-frequency update loop.
3.  **Hardcoded Action Flag**: `IsStopFrame` checks for `actionFlag == 2`. This value is specific to the game version’s DBC definition for stops; changes in DBC structure may require updating this constant.
4.  **Raw Pointer Spline**: `Spline` is a raw `TransportSpline*`. `KeyFrame` does not own the spline; its lifetime is managed externally, likely by `TransportTemplate` or `TransportMgr`.

## Member Reference

**`KeyFrame`**  
Constructor initializing a `KeyFrame` with a `TaxiPathNodeEntry`. Sets defaults for indices, distances (-1.0f), times (0), flags (false), and pointers (nullptr). Called by `TransportMgr/GenerateWaypoints`.

**`IsTeleportFrame`**  
Returns the `Teleport` flag. Indicates instantaneous position change. Called by `Transport/Update#2`.

**`IsUpdateFrame`**  
Returns the `Update` flag. Indicates need for specific update logic. Called by `Transport/Update#2`.

**`IsStopFrame`**  
Checks if `Node->actionFlag == 2`. Returns true if the frame is a stop. Called by `TransportMgr/GenerateWaypoints` and `Transport/Update#2`.

---

<!-- machine-true, projected from graph.json -->

## Map — KeyFrame

*Source:* TransportMgr.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| KeyFrame | ctor | — | TransportMgr/GenerateWaypoints | — |
| IsTeleportFrame | method | — | Transport/Update#2 | — |
| IsUpdateFrame | method | — | Transport/Update#2 | — |
| IsStopFrame | method | — | TransportMgr/GenerateWaypoints | — |
