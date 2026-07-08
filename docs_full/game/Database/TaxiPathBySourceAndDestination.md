# TaxiPathBySourceAndDestination

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# TaxiPathBySourceAndDestination

**Purpose & Responsibilities**

`TaxiPathBySourceAndDestination` is a lightweight Plain Old Data (POD) struct defined in `DBCStructure.h`. It serves as a value object within the server’s internal representation of flight path (taxi) data. Specifically, it aggregates the **path identifier** (`ID`) and the **cost** (`price`) associated with a specific route between two taxi nodes.

This struct is not a standalone entity but a component of a larger hierarchical data structure used to organize flight paths by their starting node. It allows the server to quickly look up the cost and the specific path definition ID for a journey from a source node to a destination node, without needing to scan the entire `TaxiPath.dbc` table repeatedly.

**Member-by-Member Behavior**

The unit consists of two constructors and two public data members.

*   **Constructors**:
    *   The default constructor initializes the `ID` and `price` to zero. This is likely used for placeholder initialization or when declaring instances before assignment.
    *   The parameterized constructor accepts a `_id` (the path ID from `TaxiPath.dbc`) and a `_price` (the cost of the flight). It assigns these directly to the struct's members. This constructor is the primary way valid flight path records are instantiated during the loading process.

*   **Data Members**:
    *   `ID`: Stores the unique identifier of the flight path segment. This ID corresponds to the `ID` column in the `TaxiPath.dbc` file, which links to the detailed waypoint data in `TaxiPathNode.dbc`.
    *   `price`: Stores the gold cost for the player to take this specific flight path. This value comes directly from the `price` column in `TaxiPath.dbc`.

**Cross-Unit Boundaries**

*   **Called by `DBCStores/LoadDBCStores`**:
    The parameterized constructor `TaxiPathBySourceAndDestination#2` is invoked by the `DBCStores` unit (specifically within the `LoadDBCStores` routine). During server startup, the core reads the `TaxiPath.dbc` file. For each row in this DBC, it creates a `TaxiPathBySourceAndDestination` instance containing the path ID and price. These instances are then inserted into a nested map structure (`TaxiPathSetBySource`, defined in the same header) keyed by the source node ID and destination node ID. This pre-processing step transforms the flat DBC table into an efficient lookup structure for runtime queries.

*   **Calls Out**: None. This struct contains no logic that interacts with other units.

**Data Model**

This unit does not interact with the SQL database directly. It processes data from the **DBC (Data Block Chunk)** files, specifically:

*   **`TaxiPath.dbc`**: The struct mirrors the essential fields of this DBC table. Each row in `TaxiPath.dbc` represents a direct flight connection between two nodes. The `ID` field in the struct maps to the DBC's `ID` column, and the `price` field maps to the DBC's `price` column. The `from` and `to` columns of the DBC are used as keys in the surrounding map structures (`TaxiPathSetBySource`) rather than being stored within this struct itself.

**Notable Implementation Details**

*   **Part of a Hierarchical Lookup System**: The struct is designed to work in tandem with the typedefs `TaxiPathSetForSource` and `TaxiPathSetBySource` found in the same header. `TaxiPathSetBySource` is a `std::map<uint32, std::map<uint32, TaxiPathBySourceAndDestination>>`. This means the first key is the source node ID, the second key is the destination node ID, and the value is this struct. This design allows O(log N) lookup of flight costs and path IDs based on origin and destination.
*   **No Waypoint Data**: The struct intentionally excludes the actual coordinates or waypoints of the flight path. That data resides in `TaxiPathNodeEntry` and is accessed via the `ID` stored here. This separation keeps the lookup structure small and fast, deferring the heavier waypoint parsing until a path is actually requested.
*   **Default Initialization**: The presence of a default constructor initializing fields to zero suggests that instances might be default-constructed in containers before being populated, or used as sentinel values. However, given the map-based storage, the parameterized constructor is the standard usage pattern during DBC loading.

## Member Reference

**TaxiPathBySourceAndDestination**
Default constructor. Initializes `ID` and `price` to 0. Used for default initialization of instances.

**TaxiPathBySourceAndDestination#2**
Parameterized constructor. Takes `uint32 _id` and `uint32 _price`, assigning them to the `ID` and `price` members respectively. Called by `DBCStores/LoadDBCStores` during server startup to populate flight path data from `TaxiPath.dbc`.

---

<!-- machine-true, projected from graph.json -->

## Map — TaxiPathBySourceAndDestination

*Source:* DBCStructure.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| TaxiPathBySourceAndDestination | ctor | — | — | — |
| TaxiPathBySourceAndDestination#2 | ctor | — | DBCStores/LoadDBCStores | — |
