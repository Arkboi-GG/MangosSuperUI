# RequestAccountData

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# RequestAccountData

**Purpose & Responsibilities**

`RequestAccountData` is a client-side packet structure within the `WorldPackets::Misc` namespace, defined in `Misc.h`. It represents the `CMSG_REQUEST_ACCOUNT_DATA` message sent by the game client to the server. Its sole responsibility is to encapsulate the raw binary data of this specific network request, specifically identifying the **type** of account data the client is requesting.

This class is part of a larger system for handling miscellaneous client-to-server communications. Like all classes in this namespace inheriting from `ClientPacket`, it serves as a data carrier for deserialization. The actual logic for parsing the incoming byte stream is implemented in the `ReadFromWorldPacket` method (which is declared here but defined elsewhere, likely in a corresponding `.cpp` file or via template instantiation patterns common in this codebase). The constructor initializes the packet with the correct opcode (`CMSG_REQUEST_ACCOUNT_DATA`) and defaults the `type` field to `0`.

**Member-by-Member Behavior**

*   **`RequestAccountData` (Constructor)**: Initializes the packet object. It calls the base `ClientPacket` constructor with the opcode `CMSG_REQUEST_ACCOUNT_DATA`, ensuring the server knows how to route this message upon receipt. It also initializes the member variable `type` to `0`. This default value is typically overwritten during the deserialization process when the packet is read from the network buffer.

**Cross-Unit Boundaries**

*   **Calls Out**: None. The constructor only invokes the base class constructor.
*   **Called By**: None listed in the map. In practice, instances of this class are typically created by the network layer when a packet with opcode `CMSG_REQUEST_ACCOUNT_DATA` is received, and then passed to a handler function (not shown in this unit) that processes the request.

**Data Model**

This unit does not interact directly with any database tables. It operates purely on network packet data.

**Notable Implementation Details**

*   **Inheritance**: Inherits from `ClientPacket`, indicating it is a message originating from the client.
*   **Final Class**: The class is marked `final`, preventing further inheritance.
*   **Default Initialization**: The `type` member is initialized to `0` in the class definition. This is a safety measure, though the actual value will be populated by `ReadFromWorldPacket`.
*   **Namespace**: Located in `WorldPackets::Misc`, grouping it with other miscellaneous client commands like teleportation requests, friend list updates, and emotes.

## Member Reference

**RequestAccountData**
The constructor for the `RequestAccountData` packet. It initializes the base `ClientPacket` with the opcode `CMSG_REQUEST_ACCOUNT_DATA` and sets the `type` member to `0`. It prepares the object to receive and store the account data type requested by the client.

---

<!-- machine-true, projected from graph.json -->

## Map — RequestAccountData

*Source:* Misc.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| RequestAccountData | ctor | — | — | — |
