# ItemTextQuery

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ItemTextQuery

**ItemTextQuery** is a minimal client-side packet structure within the `WorldPackets::Misc` namespace, defined in `Misc.h`. It represents the `CMSG_ITEM_TEXT_QUERY` message sent by the game client to the server. Its sole responsibility is to carry the raw binary data associated with a request for item text information—typically lore, descriptions, or flavor text associated with specific items or mail messages.

As a `ClientPacket`, it inherits the standard interface for incoming network messages but contains no complex logic itself. It acts as a simple data container (POD-like structure with a virtual destructor via inheritance) that holds three `uint32` fields extracted from the network stream. The actual parsing of these fields occurs in the `ReadFromWorldPacket` method, which is declared in this header but implemented elsewhere (likely in a corresponding `.cpp` file not included in this unit's scope, or potentially inline in a different partial if this were a multi-file class, though the MAP indicates only the constructor is part of this specific unit's behavioral surface).

The unit has no dependencies on other code units, does not call out to any other classes or functions, and is not called by any other units outside of the packet dispatching system (which is handled by the base `ClientPacket` infrastructure). It interacts with no database tables directly; any database lookups triggered by this packet would occur in the handler that processes this packet after deserialization.

## Member Reference

**ItemTextQuery**
The default constructor for the `ItemTextQuery` packet. It initializes the object as a `ClientPacket` with the opcode `CMSG_ITEM_TEXT_QUERY`. This registration ensures that when the server receives a network packet with this specific opcode, it instantiates an `ItemTextQuery` object to deserialize the payload. The constructor takes no arguments and performs no additional initialization beyond the base class setup.

---

<!-- machine-true, projected from graph.json -->

## Map — ItemTextQuery

*Source:* Misc.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ItemTextQuery | ctor | — | — | — |
