# ActivateTaxi

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ActivateTaxi

## Purpose & Responsibilities

`ActivateTaxi` is a lightweight data structure within the `WorldPackets::Taxi` namespace, designed to represent a specific client-to-server network message: `CMSG_ACTIVATETAXI`. Its sole responsibility is to hold the raw data extracted from this incoming packet so that higher-level game logic can process a player's request to initiate a standard taxi flight.

This class is part of the packet parsing layer. It does not contain business logic, validation, or side effects. Instead, it serves as a typed container for three pieces of information sent by the client:
1.  The GUID of the flight master NPC the player interacted with.
2.  The ID of the starting taxi node (`node1`).
3.  The ID of the destination taxi node (`node2`).

It is distinct from `ActivateTaxiExpress`, which handles complex multi-node flights introduced in later client versions. `ActivateTaxi` is strictly for simple, point-to-point taxi requests.

## Member-by-Member Behavior

### **ActivateTaxi** (Constructor)
The constructor initializes the packet object. It performs two actions:
1.  Calls the base class constructor `ClientPacket(CMSG_ACTIVATETAXI)` to register the packet type identifier.
2.  Initializes the member variables `node1` and `node2` to `0`. The `flightmasterGuid` is default-initialized by the `ObjectGuid` class (typically to an invalid/empty state).

This initialization ensures that if the packet reading fails or fields are missing, the data remains in a known safe state rather than containing garbage memory.

## Cross-Unit Boundaries

*   **Calls out:** None. This unit is a leaf in the call graph regarding outgoing dependencies. It relies only on the base class `ClientPacket` and the utility class `ObjectGuid`, which are part of the core framework infrastructure.
*   **Called by:** The MAP indicates no external callers. In practice, this class is instantiated by the network input handler when a `CMSG_ACTIVATETAXI` opcode is detected. The handler will call `ReadFromWorldPacket` (declared in the header but not listed in the MAP as a primary member for this specific documentation scope, though it is part of the class interface) to populate the fields. Subsequently, game logic handlers (likely in a separate unit such as `WorldSession` or a dedicated `TaxiHandler`) will access the public members `flightmasterGuid`, `node1`, and `node2` to execute the flight.

## Data Model

This unit does not directly interact with any database tables. It operates entirely in memory, processing transient network data. The `node1` and `node2` integers correspond to IDs found in the `taxi_nodes` and `taxi_path` tables in the database, but `ActivateTaxi` itself performs no SQL queries or schema interactions.

## Notable Implementation Details

1.  **Simple Point-to-Point Logic:** Unlike `ActivateTaxiExpress`, which uses a `std::vector<uint32>` to store a sequence of nodes for complex routes, `ActivateTaxi` uses fixed `uint32` fields for `node1` and `node2`. This reflects the older client protocol where players could only select a direct next hop in the taxi chain.
2.  **Default Initialization:** The explicit initialization of `node1` and `node2` to `0` in the constructor is a defensive coding practice. Since `0` is typically not a valid taxi node ID (IDs usually start at 1), this allows downstream logic to easily detect if the packet was malformed or if the fields were not properly populated by `ReadFromWorldPacket`.
3.  **Public Data Members:** All data fields (`flightmasterGuid`, `node1`, `node2`) are public. This design choice prioritizes simplicity and performance in the packet parsing layer, avoiding the overhead of getter/setter methods for transient data structures that are destroyed after processing.

## Member Reference

**ActivateTaxi**
The constructor for the `ActivateTaxi` packet class. It initializes the base `ClientPacket` with the opcode `CMSG_ACTIVATETAXI` and sets the `node1` and `node2` member variables to `0`. It prepares the object to receive data from an incoming network packet representing a simple taxi flight request.

---

<!-- machine-true, projected from graph.json -->

## Map — ActivateTaxi

*Source:* Taxi.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ActivateTaxi | ctor | — | — | — |
