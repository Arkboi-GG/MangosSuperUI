# BattlemasterHello

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# BattlemasterHello

**Purpose & Responsibilities**

`BattlemasterHello` is a client-side packet structure within the `WorldPackets::Battleground` namespace, responsible for representing the `CMSG_BATTLEMASTER_HELLO` message sent by the game client to the server. Its sole responsibility is to deserialize the raw network data received from the client into a structured object containing the `ObjectGuid` of the battlemaster NPC the player is interacting with. This packet serves as the initial handshake in the battleground queue interaction flow, signaling the player's intent to inquire about or join a battleground via a specific NPC.

**Member-by-Member Behavior**

The unit consists of a single constructor and relies on inherited functionality for packet deserialization.

*   **Constructor (`BattlemasterHello`)**: The explicit default constructor initializes the base `ClientPacket` class with the opcode `CMSG_BATTLEMASTER_HELLO`. It does not perform any additional initialization of the `guid` member, leaving it in a default-constructed state until `ReadFromWorldPacket` is invoked.

**Cross-Unit Boundaries**

*   **Inheritance**: Inherits from `WorldPackets::ClientPacket` (defined in `Packet.h`). This provides the base infrastructure for handling client-to-server network messages, including the opcode identification and the interface for reading data from the `WorldPacket` buffer.
*   **Dependency**: Uses `ObjectGuid` (defined in `ObjectGuid.h`) to store the unique identifier of the target battlemaster NPC.
*   **Deserialization**: The `ReadFromWorldPacket` method (declared in this header but implemented elsewhere, likely in a corresponding `.cpp` file or inline in a different partial) is responsible for extracting the `guid` from the incoming `WorldPacket` buffer. This method is called by the network handler layer when a `CMSG_BATTLEMASTER_HELLO` packet is received.

**Data Model**

This unit does not interact directly with any database tables. It operates purely on runtime network data.

**Notable Implementation Details**

*   **Final Class**: The class is marked `final`, preventing further inheritance. This enforces a strict hierarchy for packet types and ensures that no derived classes can alter its behavior.
*   **Minimal State**: The class contains only one data member, `guid`, reflecting the simplicity of the `CMSG_BATTLEMASTER_HELLO` protocol message, which typically only requires the identifier of the NPC being addressed.
*   **Namespace Organization**: Located within `WorldPackets::Battleground`, indicating its specific role in the battleground subsystem of the world server's packet handling logic.

## Member Reference

**BattlemasterHello**
The explicit default constructor for the `BattlemasterHello` packet class. It initializes the base `ClientPacket` with the opcode `CMSG_BATTLEMASTER_HELLO`. No additional initialization is performed on the `guid` member.

---

<!-- machine-true, projected from graph.json -->

## Map — BattlemasterHello

*Source:* Battleground.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| BattlemasterHello | ctor | — | — | — |
