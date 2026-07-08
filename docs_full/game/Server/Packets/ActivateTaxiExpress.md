# ActivateTaxiExpress

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ActivateTaxiExpress

**Purpose & Responsibilities**

`ActivateTaxiExpress` is a client-side packet structure within the `WorldPackets::Taxi` namespace, designed to handle the `CMSG_ACTIVATETAXIEXPRESS` message sent by the game client to the server. Its primary responsibility is to deserialize the raw binary data of this specific network packet into structured fields that the server can process. This packet represents a request to initiate an express taxi flight, allowing a player to travel through multiple nodes in a single transaction, as opposed to standard point-to-point taxi requests.

The class is conditionally compiled only for client builds newer than `CLIENT_BUILD_1_9_4`, indicating that the "express taxi" feature was introduced or standardized in later versions of the client protocol. It inherits from `ClientPacket`, establishing it as an incoming message from the client.

**Member-by-Member Behavior**

The unit contains a single member: the constructor.

*   **`ActivateTaxiExpress()`**: This is the default constructor for the packet structure. It initializes the base class `ClientPacket` with the opcode `CMSG_ACTIVATETAXIEXPRESS`. This registration ensures that when the server receives a packet with this specific opcode, it can correctly instantiate this type for processing. The constructor does not perform any additional initialization of the member variables (`flightmasterGuid`, `totalcost`, `nodes`), relying instead on their default initializers defined in the class declaration (zero for integers, empty for vectors).

**Cross-Unit Boundaries**

*   **Calls Out**: None. The constructor only interacts with its base class `ClientPacket`.
*   **Called By**: None listed in the map. In practice, this class is instantiated by the packet reading infrastructure (likely within `WorldSession` or a packet handler dispatcher) when a `CMSG_ACTIVATETAXIEXPRESS` packet is received from the network socket. The `ReadFromWorldPacket` method (declared but not implemented in this unit) would be called subsequently by that infrastructure to populate the fields.

**Data Model**

This unit does not interact directly with any database tables. It operates purely on network packet data. The `tables` column in the map is empty, and no SQL queries are present in the source code.

**Notable Implementation Details**

1.  **Conditional Compilation**: The entire class definition is wrapped in `#if SUPPORTED_CLIENT_BUILD > CLIENT_BUILD_1_9_4`. This means the class does not exist in the compiled binary for older client versions. Code attempting to use this class must also respect this preprocessor guard to avoid compilation errors.
2.  **Default Initializers**: The member variables `totalcost` and `node1`/`node2` (in related classes) use in-class default initializers (`= 0`). The `nodes` vector is default-initialized to an empty state. This ensures that even before `ReadFromWorldPacket` is called, the object is in a valid, zeroed-out state.
3.  **Vector Usage**: Unlike the simpler `ActivateTaxi` class which uses two fixed `uint32` fields (`node1`, `node2`), `ActivateTaxiExpress` uses a `std::vector<uint32>` for `nodes`. This reflects the semantic difference: an express taxi route can consist of an arbitrary number of intermediate stops, requiring dynamic storage.
4.  **Missing Implementation**: The header declares `void ReadFromWorldPacket(WorldPacket& recv_data) override;` but the provided source snippet (which appears to be only the header `Taxi.h`) does not contain the implementation. The actual deserialization logic resides in the corresponding `.cpp` file (not provided in the source block, though implied by the unit structure). Documentation here focuses strictly on the interface defined in the header.

## Member Reference

**ActivateTaxiExpress**
Constructor for the `ActivateTaxiExpress` packet class. Initializes the base `ClientPacket` with the opcode `CMSG_ACTIVATETAXIEXPRESS`. Only compiled for client builds greater than `CLIENT_BUILD_1_9_4`.

---

<!-- machine-true, projected from graph.json -->

## Map — ActivateTaxiExpress

*Source:* Taxi.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ActivateTaxiExpress | ctor | — | — | — |
