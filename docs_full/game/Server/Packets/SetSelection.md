# SetSelection

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# SetSelection

**Purpose & Responsibilities**

`SetSelection` is a client-to-server packet structure within the `WorldPackets::Misc` namespace, defined in `Misc.h`. Its sole responsibility is to represent the `CMSG_SET_SELECTION` message sent by the game client. This message indicates that the player has changed their current selection target in the world—typically by clicking on an entity such as a player, creature, or game object. The structure holds the `ObjectGuid` of the newly selected entity, allowing the server to identify which object the player is now focusing on.

As a `ClientPacket`, `SetSelection` inherits the standard packet handling interface but contains no custom logic beyond its constructor and the declaration of the virtual `ReadFromWorldPacket` method. The actual parsing of the binary data into the `guid` member is implemented in the corresponding `.cpp` file (not provided here, but implied by the override declaration).

**Member-by-Member Behavior**

The unit consists of a single member: the constructor.

*   **`SetSelection`**: The default constructor initializes the base `ClientPacket` class with the opcode `CMSG_SET_SELECTION`. It prepares the object to receive and parse incoming network data associated with this specific command. It does not initialize the `guid` member explicitly, leaving it to be populated during the deserialization process handled by `ReadFromWorldPacket`.

**Cross-Unit Boundaries**

*   **Calls Out**: None. The constructor only invokes the base class constructor.
*   **Called By**: None listed in the map. In practice, instances of `SetSelection` are typically created by the packet dispatching system when a `CMSG_SET_SELECTION` opcode is detected on the socket, after which the `ReadFromWorldPacket` method is invoked to populate the `guid`.

**Data Model**

This unit does not interact directly with any database tables. It operates purely on in-memory network packet data.

**Notable Implementation Details**

*   **Final Class**: The class is marked `final`, preventing inheritance. This is consistent with the design pattern for leaf-node packet structures in this codebase.
*   **Public Member**: The `guid` member is declared `public`, allowing direct access by the handler logic after parsing. This avoids the need for getter/setter methods, keeping the packet structure lightweight.
*   **Opcode Association**: The constructor binds this structure to `CMSG_SET_SELECTION`, ensuring type-safe routing of the network message.

## Member Reference

**SetSelection**
Constructor that initializes the `ClientPacket` base class with the opcode `CMSG_SET_SELECTION`. It prepares the instance to handle the "set selection" command from the client.

---

<!-- machine-true, projected from graph.json -->

## Map — SetSelection

*Source:* Misc.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| SetSelection | ctor | — | — | — |
