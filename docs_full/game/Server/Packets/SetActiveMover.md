# SetActiveMover

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# SetActiveMover

**Purpose & Responsibilities**

`SetActiveMover` is a client-side packet structure within the `WorldPackets::Misc` namespace, defined in `Misc.h`. It represents the `CMSG_SET_ACTIVE_MOVER` message sent by the game client to the server. Its sole responsibility is to carry the `ObjectGuid` of the entity that the client intends to set as the "active mover." In the context of World of Warcraft emulation, the active mover is the object whose movement updates are currently being processed by the server for the player's character (typically the player themselves, but potentially a mount, pet, or vehicle under specific circumstances).

This unit is a data carrier; it contains no business logic, validation, or side effects. It serves as the interface for deserializing the raw binary data from the network stream into a structured C++ object that higher-level handlers can inspect.

**Member-by-Member Behavior**

The unit consists of a single constructor and one public data member.

*   **`guid`**: A public member of type `ObjectGuid`. This field stores the unique identifier of the entity designated as the active mover. The value is populated during the deserialization process handled by the `ReadFromWorldPacket` method (which is declared in this header but implemented elsewhere, likely in a corresponding `.cpp` file or via template specialization not shown in the provided source).
*   **`SetActiveMover()`**: The default constructor. It initializes the base class `ClientPacket` with the opcode `CMSG_SET_ACTIVE_MOVER`. This ensures that any instance of `SetActiveMover` is correctly identified by the packet routing system as a request to change the active mover.

**Cross-Unit Boundaries**

*   **Calls Out**: None. The constructor does not invoke any external functions.
*   **Called By**: None listed in the map. However, in the broader system, instances of this class are typically created by the packet reading infrastructure when a `CMSG_SET_ACTIVE_MOVER` opcode is detected on the socket. The `ReadFromWorldPacket` method (declared here) will be called by the packet parsing engine to populate the `guid` member.

**Data Model**

This unit does not interact with any database tables. It operates entirely in memory as part of the network communication layer.

**Notable Implementation Details**

*   **Inheritance**: Inherits from `ClientPacket`, indicating it is a message originating from the client.
*   **Opcode Association**: Hardcoded to `CMSG_SET_ACTIVE_MOVER`. This ties the class strictly to this specific network message type.
*   **Public Data Member**: The `guid` member is public, allowing direct access by handlers after deserialization. This is a common pattern in this codebase for packet structures to minimize boilerplate getter/setter methods.
*   **No Validation**: The class itself performs no validation on the `guid`. Validity checks (e.g., ensuring the GUID exists, is valid for the current player, etc.) are performed by the handler that processes this packet after it has been constructed and read.

## Member Reference

**SetActiveMover**
The default constructor for the `SetActiveMover` packet class. It initializes the base `ClientPacket` with the opcode `CMSG_SET_ACTIVE_MOVER`, identifying the packet type for the network router. It does not initialize the `guid` member, which remains default-constructed until `ReadFromWorldPacket` is called.

---

<!-- machine-true, projected from graph.json -->

## Map — SetActiveMover

*Source:* Misc.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| SetActiveMover | ctor | — | — | — |
