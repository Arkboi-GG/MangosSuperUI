<!-- provenance: verbose -->
# Taxi

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`Taxi.cpp` and `Taxi.h` define the client-side packet structures for the **Taxi (Flight Path)** subsystem within the `WorldPackets::Taxi` namespace. This unit deserializes network data received from the game client into strongly-typed C++ objects. It handles four distinct packet types related to interacting with flight masters:

1.  **`TaxiNodeStatusQuery`**: Requests the status of taxi nodes relative to a specific flight master.
2.  **`TaxiQueryAvailableNodes`**: Queries available taxi nodes associated with a specific entity.
3.  **`ActivateTaxi`**: Initiates a standard two-node flight path.
4.  **`ActivateTaxiExpress`**: Initiates a multi-node express flight path (supported only in client builds newer than 1.9.4).

This unit contains **no business logic**, **no database queries**, and **no server-side response generation**. Its sole responsibility is parsing binary data from `WorldPacket` buffers into member variables (`ObjectGuid`s, `uint32`s, and vectors) so that higher-level handlers can process the intent.

## Member-by-Member Behavior

### TaxiNodeStatusQuery
*   **`TaxiNodeStatusQuery` (ctor)**: Initializes the packet object with the opcode `CMSG_TAXINODE_STATUS_QUERY`.
*   **`ReadFromWorldPacket`**: Deserializes a single `ObjectGuid` from the incoming packet buffer into the member `creatureGuidNearTaxi`. The comment indicates this GUID represents the flight master NPC the player is interacting with.

### TaxiQueryAvailableNodes
*   **`ReadFromWorldPacket`**: Deserializes a single `ObjectGuid` into the member `guid`.

### ActivateTaxi
*   **`ReadFromWorldPacket`**: Deserializes three fields:
    1.  `flightmasterGuid`: The GUID of the flight master NPC.
    2.  `node1`: The ID of the starting taxi node.
    3.  `node2`: The ID of the destination taxi node.

### ActivateTaxiExpress
*   **`ReadFromWorldPacket`**: Deserializes a variable-length packet structure:
    1.  `flightmasterGuid`: The GUID of the flight master NPC.
    2.  `totalcost`: The total cost of the express route.
    3.  `node_count`: The number of nodes in the route.
    4.  A loop runs `node_count` times, reading a `uint32` node ID and appending it to the `nodes` vector.

## Cross-Unit Boundaries

All members in this unit call out to utility functions for data extraction. They are not called by other units directly in the provided map, but they are part of the packet handling pipeline.

*   **`ObjectGuid/operator>>`**: Called by all `ReadFromWorldPacket` methods. This operator extracts a 64-bit GUID from the `WorldPacket` stream and constructs an `ObjectGuid` object. This is a critical dependency for identifying NPCs and entities.
*   **`ByteBuffer/operator>>#9`**: Called by `TaxiNodeStatusQuery::ReadFromWorldPacket` and `TaxiQueryAvailableNodes::ReadFromWorldPacket` (as indicated in the MAP, though the source shows direct `>>` usage on `recv_data`, which likely delegates to `ByteBuffer` operators internally). This handles the low-level byte extraction.

## Data Model

This unit does not interact with any database tables. All data is transient, residing only in the network packet buffer during deserialization.

## Notable Implementation Details

1.  **Client Build Conditional Compilation**:
    The `ActivateTaxiExpress` class and its `ReadFromWorldPacket` method are wrapped in `#if SUPPORTED_CLIENT_BUILD > CLIENT_BUILD_1_9_4`. This ensures that the multi-node express taxi feature is only compiled and supported for client versions newer than 1.9.4. Older clients will not send this packet, and the server code for it will not exist, preventing compatibility issues.

2.  **Variable-Length Parsing**:
    `ActivateTaxiExpress::ReadFromWorldPacket` demonstrates dynamic parsing. It reads a count (`node_count`) and then iterates to populate a `std::vector<uint32>`. This contrasts with `ActivateTaxi`, which has a fixed structure (two nodes). Maintainers must ensure that `node_count` is validated elsewhere (e.g., in the handler) to prevent excessive memory allocation or denial-of-service via large counts, as this unit performs no validation.

3.  **Default Initialization**:
    In `ActivateTaxi`, `node1` and `node2` are initialized to `0` in the header. In `ActivateTaxiExpress`, `totalcost` is initialized to `0`. While `ReadFromWorldPacket` overwrites these values, default initialization provides safety if the packet reading fails or is skipped.

4.  **Namespace Organization**:
    All classes reside in `WorldPackets::Taxi`, clearly segregating taxi-related network protocols from other game systems.

## Member Reference

**ReadFromWorldPacket#3** (`ActivateTaxi::ReadFromWorldPacket`): Deserializes `flightmasterGuid`, `node1`, and `node2` from the packet buffer. Calls `ObjectGuid/operator>>` for the GUID and standard stream operators for the integers.

**ReadFromWorldPacket#4** (`ActivateTaxiExpress::ReadFromWorldPacket`): Deserializes `flightmasterGuid`, `totalcost`, and `node_count`. Then loops `node_count` times to read individual node IDs into the `nodes` vector. Calls `ObjectGuid/operator>>` for the GUID.

**ReadFromWorldPacket** (`TaxiNodeStatusQuery::ReadFromWorldPacket`): Deserializes `creatureGuidNearTaxi` from the packet buffer. Calls `ObjectGuid/operator>>` and `ByteBuffer/operator>>#9`.

**TaxiNodeStatusQuery** (ctor): Constructs the packet object with opcode `CMSG_TAXINODE_STATUS_QUERY`.

**ReadFromWorldPacket#2** (`TaxiQueryAvailableNodes::ReadFromWorldPacket`): Deserializes `guid` from the packet buffer. Calls `ObjectGuid/operator>>` and `ByteBuffer/operator>>#9`.

---

<!-- machine-true, projected from graph.json -->

## Map — Taxi

*Source:* Taxi.cpp, Taxi.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ReadFromWorldPacket#3 | method | ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket#4 | method | ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket | method | ByteBuffer/operator>>#9, ObjectGuid/operator>> | — | — |
| TaxiNodeStatusQuery | ctor | — | — | — |
| ReadFromWorldPacket#2 | method | ByteBuffer/operator>>#9, ObjectGuid/operator>> | — | — |
