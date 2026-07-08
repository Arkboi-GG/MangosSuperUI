# TaxiQueryAvailableNodes

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# TaxiQueryAvailableNodes

**Purpose & Responsibilities**

`TaxiQueryAvailableNodes` is a client-side packet structure within the `WorldPackets::Taxi` namespace, responsible for encapsulating the data associated with the `CMSG_TAXIQUERYAVAILABLENODES` message. Its primary role is to define the contract for receiving a request from a client to query available taxi nodes. It holds a single piece of data: the `ObjectGuid` of the entity (typically a flight master NPC) that initiated or is relevant to the query. As a `ClientPacket`, it serves as the input structure for server-side handlers that process this specific client command.

**Member-by-Member Behavior**

The unit consists of a single member: the constructor.

*   **Constructor (`TaxiQueryAvailableNodes`)**: This explicit constructor initializes the base `ClientPacket` class with the opcode `CMSG_TAXIQUERYAVAILABLENODES`. It ensures that any instance of this class is immediately identified by the network layer as a request for available taxi nodes. The member variable `guid` is default-initialized to an empty `ObjectGuid` by the compiler, awaiting population via the `ReadFromWorldPacket` method (which is declared in this header but implemented elsewhere, likely in a corresponding `.cpp` file not included in this unit's scope).

**Cross-Unit Boundaries**

*   **Calls Out**: None. The constructor only interacts with its base class `ClientPacket`.
*   **Called By**: None listed in the map. In practice, this object would be instantiated by the network handler layer when a raw packet with opcode `CMSG_TAXIQUERYAVAILABLENODES` is received from a client. The handler would then call `ReadFromWorldPacket` to populate the `guid` field before passing the object to the game logic handler.

**Data Model**

This unit does not interact directly with any database tables. It operates purely on in-memory packet data.

**Notable Implementation Details**

*   **Inheritance**: Inherits from `ClientPacket`, indicating it represents data sent *from* the client *to* the server.
*   **Opcode Association**: Hardcoded to `CMSG_TAXIQUERYAVAILABLENODES`. Any change in the client-server protocol regarding this opcode would require updating this constant.
*   **Guid Storage**: The `guid` member stores the identifier of the flight master or taxi node provider. This is critical for the server to determine context (e.g., which zone's nodes are available, or verifying the player is near the correct NPC).
*   **Final Class**: Marked as `final`, preventing further inheritance, which enforces a strict interface for this packet type.

## Member Reference

**TaxiQueryAvailableNodes**
Constructor that initializes the `ClientPacket` base class with the opcode `CMSG_TAXIQUERYAVAILABLENODES`. It prepares the object to receive the `ObjectGuid` of the relevant flight master/NPC from the incoming world packet.

---

<!-- machine-true, projected from graph.json -->

## Map — TaxiQueryAvailableNodes

*Source:* Taxi.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| TaxiQueryAvailableNodes | ctor | — | — | — |
