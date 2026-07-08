# WrapItem

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# WrapItem

## Purpose & Responsibilities

`WrapItem` is a client-side packet structure within the `WorldPackets::Item` namespace, responsible for encapsulating the data required to request the wrapping of an item into a gift. It represents the `CMSG_WRAP_ITEM` message sent from the game client to the server.

The class holds four byte-sized fields identifying two distinct inventory locations:
1.  **Gift Container:** The bag and slot containing the empty gift wrapper (`giftBag`, `giftSlot`).
2.  **Item to Wrap:** The bag and slot containing the item intended to be placed inside the gift (`itemBag`, `itemSlot`).

This unit is purely a data carrier for network deserialization. It does not contain logic for validation, inventory manipulation, or database persistence. Its sole responsibility is to provide a structured interface for reading raw binary data from a `WorldPacket` into these specific fields.

## Member-by-Member Behavior

### Construction and Initialization
**WrapItem**
The constructor initializes the packet object. It explicitly calls the base class `ClientPacket` constructor with the opcode `CMSG_WRAP_ITEM`, ensuring the packet is correctly identified by the network handler. It also initializes all four member variables (`giftBag`, `giftSlot`, `itemBag`, `itemSlot`) to zero via their default member initializers.

### Data Deserialization
Although not listed as a separate member in the MAP because it is an override of a virtual function defined in the base class hierarchy, the behavior of `ReadFromWorldPacket` is intrinsic to this unit's usage. When invoked by the network layer, it extracts four bytes from the incoming `WorldPacket` stream in the order expected by the client protocol:
1.  Reads `giftBag`
2.  Reads `giftSlot`
3.  Reads `itemBag`
4.  Reads `itemSlot`

These values are then stored in the corresponding public members for subsequent processing by the calling unit.

## Cross-Unit Boundaries

*   **Calls Out:** None. The `WrapItem` class does not invoke methods in other units. It relies solely on the base class `ClientPacket` for its identity and the `WorldPacket` class (passed as a parameter to `ReadFromWorldPacket`) for data extraction.
*   **Called By:** The MAP indicates no external callers are explicitly tracked for this specific member in the provided context. However, in the broader system, this packet is instantiated and populated by the network handler (likely in a unit such as `WorldSession` or a dedicated packet handler dispatcher) when a `CMSG_WRAP_ITEM` opcode is received. The handler then passes this populated `WrapItem` object to the business logic unit responsible for executing the gift-wrapping action (e.g., `Player::WrapItem` or similar).

## Data Model

This unit does not interact directly with any database tables. It operates entirely on transient network data. Any persistence related to the resulting gift item would be handled by downstream units after the packet data has been processed and validated.

## Notable Implementation Details

*   **Field Order:** The order of fields in the struct (`giftBag`, `giftSlot`, `itemBag`, `itemSlot`) must strictly match the serialization order expected by the client for the `CMSG_WRAP_ITEM` opcode. A mismatch would result in corrupted inventory references.
*   **Type Constraints:** All fields are `uint8`, implying that bag and slot indices are limited to 0–255. This aligns with typical WoW inventory limits but assumes the client will not send invalid indices. Validation of these indices (e.g., checking if the bag exists, if the slot contains a valid item, if the gift is empty) is **not** performed here and must be done by the caller.
*   **No Validation:** The class provides no safeguards against invalid combinations (e.g., wrapping an item into itself, or using a non-gift item as the wrapper). It is a passive data holder.

## Member Reference

**WrapItem**
Constructor for the `WrapItem` packet. Initializes the base `ClientPacket` with the `CMSG_WRAP_ITEM` opcode and sets all inventory index fields (`giftBag`, `giftSlot`, `itemBag`, `itemSlot`) to zero.

---

<!-- machine-true, projected from graph.json -->

## Map — WrapItem

*Source:* Item.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| WrapItem | ctor | — | — | — |
