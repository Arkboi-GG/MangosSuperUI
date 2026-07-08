# AutoStoreBagItem

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# AutoStoreBagItem

**AutoStoreBagItem** is a client-to-server network packet structure within the `WorldPackets::Item` namespace, defined in `Item.h`. It represents the `CMSG_AUTOSTORE_BAG_ITEM` message sent by the game client to request the automatic storage of an item from a source bag/slot into a destination bag.

## Purpose & Responsibilities

The primary responsibility of `AutoStoreBagItem` is to deserialize the raw binary data of the `CMSG_AUTOSTORE_BAG_ITEM` packet received from the client. It extracts three specific fields required to process the item movement request:
1.  **Source Bag (`srcbag`)**: The bag index containing the item to be moved.
2.  **Source Slot (`srcslot`)**: The specific slot within the source bag holding the item.
3.  **Destination Bag (`dstbag`)**: The target bag index where the item should be automatically stored.

This class acts solely as a data carrier and deserializer. It does not contain logic for validating the move, checking inventory space, or updating the database; those responsibilities lie in the server-side handlers that consume this packet object.

## Member-by-Member Behavior

### Constructor: `AutoStoreBagItem`
The constructor initializes the packet with the opcode `CMSG_AUTOSTORE_BAG_ITEM`. It sets the default values for `srcbag`, `srcslot`, and `dstbag` to `0` via in-class member initializers. This ensures that if `ReadFromWorldPacket` is not called or fails to populate these fields, they hold a known safe state.

### Method: `ReadFromWorldPacket`
Although the implementation is not provided in the source snippet (it is likely defined in a corresponding `.cpp` file or inline elsewhere), the declaration indicates that this virtual method overrides the base `ClientPacket::ReadFromWorldPacket`. Its role is to parse the incoming `WorldPacket` buffer and populate the `srcbag`, `srcslot`, and `dstbag` members according to the protocol specification for `CMSG_AUTOSTORE_BAG_ITEM`.

## Cross-Unit Boundaries

*   **Calls Out**: None. The `AutoStoreBagItem` class itself does not call into other units. Its `ReadFromWorldPacket` method interacts with the `WorldPacket` class (from the `Packet.h` include) to extract data, but this is standard deserialization behavior inherent to all `ClientPacket` subclasses.
*   **Called By**: The MAP indicates no external callers. In practice, this packet is instantiated and populated by the network layer when a `CMSG_AUTOSTORE_BAG_ITEM` message is received. It is then passed to the appropriate handler (likely in a session or player handler unit) which processes the item movement logic.

## Data Model

This unit does not interact directly with any database tables. It operates entirely on in-memory packet data. Any persistence of item states resulting from this packet's processing would be handled by downstream units (e.g., `Player`, `Item`, or `InventoryManager` classes) after the packet has been parsed and validated.

## Notable Implementation Details

*   **Namespace**: The class resides in `WorldPackets::Item`, indicating it is part of the world server's packet handling subsystem, specifically for item-related interactions.
*   **Final Class**: The class is marked `final`, preventing further inheritance. This is consistent with its role as a leaf node in the packet hierarchy.
*   **Default Initialization**: All data members (`srcbag`, `srcslot`, `dstbag`) are initialized to `0` in the class definition. This is a safety measure to avoid reading uninitialized memory if the packet parsing logic encounters an error or an empty packet.
*   **Opcode Association**: The constructor explicitly binds this class to `CMSG_AUTOSTORE_BAG_ITEM`, ensuring type safety and correct routing within the network dispatcher.

## Member Reference

**AutoStoreBagItem**
Constructor for the `AutoStoreBagItem` packet. Initializes the packet opcode to `CMSG_AUTOSTORE_BAG_ITEM` and sets `srcbag`, `srcslot`, and `dstbag` to `0`.

---

<!-- machine-true, projected from graph.json -->

## Map — AutoStoreBagItem

*Source:* Item.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| AutoStoreBagItem | ctor | — | — | — |
