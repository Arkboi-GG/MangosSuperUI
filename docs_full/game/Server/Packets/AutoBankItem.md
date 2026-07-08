# AutoBankItem

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# AutoBankItem

**Purpose & Responsibilities**

`AutoBankItem` is a client-side network packet structure within the `WorldPackets::Item` namespace, defined in `Item.h`. Its sole responsibility is to represent the `CMSG_AUTOBANK_ITEM` message sent by the game client to the server. This message requests that a specific item, identified by its current bag and slot location, be automatically moved to the player's bank inventory. The class acts as a data carrier, holding the source coordinates (`srcbag`, `srcslot`) required to locate the item before the server processes the move request. It contains no business logic, validation, or database interaction; it is purely a serialization target for incoming network data.

**Member-by-Member Behavior**

The unit consists of a single member: the constructor.

*   **AutoBankItem**: The default constructor initializes the packet instance. It sets the packet type to `CMSG_AUTOBANK_ITEM` via the base class `ClientPacket` and initializes the member variables `srcbag` and `srcslot` to `0`. This initialization ensures that if the packet is instantiated but not yet populated from network data, it holds a known safe state.

**Cross-Unit Boundaries**

*   **Calls Out**: None. The constructor does not invoke any functions in other units.
*   **Called By**: None listed in the map. In practice, instances of `AutoBankItem` are typically created by the network layer (likely in `WorldSession` or a packet handler) when a `CMSG_AUTOBANK_ITEM` opcode is received. The `ReadFromWorldPacket` method (declared in the header but not part of this unit's behavior map) would then be called to populate `srcbag` and `srcslot` from the raw `WorldPacket` buffer.

**Data Model**

This unit does not interact with any database tables. It operates entirely on transient network data.

**Notable Implementation Details**

*   **Final Class**: The class is marked `final`, preventing inheritance. This is consistent with its role as a leaf data structure in the packet hierarchy.
*   **Default Initialization**: Both `srcbag` and `srcslot` are initialized to `0` in the class definition. This is a defensive measure, though the `ReadFromWorldPacket` method (implemented elsewhere) will overwrite these values with data from the client.
*   **Namespace**: It resides in `WorldPackets::Item`, indicating it is part of the world server's packet handling subsystem, specifically dealing with item-related operations.

## Member Reference

**AutoBankItem**
Constructor for the `AutoBankItem` packet. Initializes the base `ClientPacket` with the opcode `CMSG_AUTOBANK_ITEM` and sets `srcbag` and `srcslot` to `0`.

---

<!-- machine-true, projected from graph.json -->

## Map — AutoBankItem

*Source:* Item.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| AutoBankItem | ctor | — | — | — |
