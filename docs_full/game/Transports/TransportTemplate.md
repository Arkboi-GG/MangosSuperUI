# TransportTemplate

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# TransportTemplate

**Purpose & Responsibilities**

`TransportTemplate` is a data structure defined in `TransportMgr.h` that holds the static configuration and pre-computed pathing data for a single transport entity (e.g., a ship or elevator). It serves as the blueprint for active transport instances, storing identity, timing, acceleration parameters, and a sequence of `KeyFrame` objects that define the movement trajectory. It is a passive data holder with no behavioral methods beyond construction and destruction.

## Member-by-Member Behavior

### Constructor

*   **`TransportTemplate()`**: The default constructor initializes all member variables to safe, neutral defaults:
    *   `inInstance`: `false`
    *   `pathTime`: `0`
    *   `accelTime`: `0.0f`
    *   `accelDist`: `0.0f`
    *   `entry`: `0`
    *   `spawned`: `false`
    *   `mapsUsed`: Empty `std::set`
    *   `keyFrames`: Empty `std::vector`
    This ensures a newly declared template is in a valid, empty state before population by `TransportMgr`.

## Cross-Unit Boundaries

The MAP indicates that `TransportTemplate` has **no outgoing calls** to other units and is **not called by** other units via method invocation. It is accessed indirectly through `TransportMgr`, which manages the lifecycle and storage of `TransportTemplate` objects in `m_transportTemplates`. Active transport instances (such as `ShipTransport`) read from this data to update their positions.

## Data Model

`TransportTemplate` does not directly interact with database tables. Its data is derived from DBC files (e.g., `TaxiPathNode.dbc` for `KeyFrame` nodes) and potentially SQL-defined paths processed by `TransportMgr`.

## Notable Implementation Details

1.  **Raw Pointer Ownership**: The `KeyFrame` struct contains a raw pointer `TransportSpline* Spline`. The `TransportTemplate` destructor (`~TransportTemplate()`) is declared but defined externally, implying it handles cleanup of these splines to prevent memory leaks. Care must be taken regarding copy semantics, as shallow copying of `TransportTemplate` could lead to double-free errors if not managed correctly.
2.  **Pre-computed Pathing**: The `keyFrames` vector is populated at load time, optimizing runtime performance by avoiding repeated path calculations.

## Member Reference

**TransportTemplate**
Default constructor. Initializes `inInstance` to `false`, `pathTime`, `accelTime`, `accelDist`, and `entry` to `0`, `spawned` to `false`, and leaves `mapsUsed` and `keyFrames` empty. Ensures a clean initial state.

---

<!-- machine-true, projected from graph.json -->

## Map — TransportTemplate

*Source:* TransportMgr.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| TransportTemplate | ctor | — | — | — |
