# BuybackItem

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# BuybackItem

**Purpose & Responsibilities**

`BuybackItem` is a client-to-server packet structure within the `WorldPackets::Item` namespace, responsible for conveying the player's intent to repurchase an item from a vendor's buyback window. It encapsulates the network data received from the client for the `CMSG_BUYBACK_ITEM` message type.

As a `ClientPacket`, its primary responsibility is to define the binary layout of the incoming network message and provide the interface (`ReadFromWorldPacket`) for deserializing raw byte streams into structured C++ fields. It does not contain business logic for processing the buyback request; that logic resides in the handler that consumes this packet.

**Member-by-Member Behavior**

The unit consists of a single constructor and associated data members defined in the header.

*   **Constructor (`BuybackItem`)**: Initializes the packet object. It sets the packet opcode to `CMSG_BUYBACK_ITEM` via the base class `ClientPacket` constructor. It initializes the `vendorGuid` member (implicitly default-constructed as an empty `ObjectGuid`) and conditionally initializes the `slot` member to `0` if the supported client build is greater than `CLIENT_BUILD_1_7_1`.

**Cross-Unit Boundaries**

*   **Calls Out**: None. The constructor performs only local initialization and calls the base class constructor.
*   **Called By**: None listed in the map. In practice, this packet is instantiated by the network layer when a `CMSG_BUYBACK_ITEM` message is received, and then passed to a handler (likely in a different unit, such as a world session or item handler) for validation and execution.

**Data Model**

This unit does not interact directly with any database tables. It represents transient network data. Any persistence related to buyback items (e.g., storing the last few sold items for a vendor) would occur in the handler that processes this packet, likely involving tables such as `character_bought_items` or similar, but `BuybackItem` itself has no SQL queries or table associations.

**Notable Implementation Details**

*   **Client Build Conditional Compilation**: The presence of the `slot` member is guarded by `#if SUPPORTED_CLIENT_BUILD > CLIENT_BUILD_1_7_1`. This indicates that older client versions (1.7.1 and below) do not send a slot index in the buyback packet, while newer clients do. The handler consuming this packet must account for this difference, potentially relying on the `vendorGuid` alone for older clients or using the `slot` for more direct access in newer ones.
*   **Vendor Identification**: The packet uses an `ObjectGuid` for `vendorGuid`, identifying the specific NPC vendor instance from which the item is being bought back. This allows the server to look up the correct buyback history associated with that specific vendor interaction.
*   **No Count Field**: Unlike `SellItem` or `BuyItem`, `BuybackItem` does not include a `count` field. This implies that buying back an item typically restores the entire stack or the specific item instance as it was sold, rather than allowing the player to specify a quantity to buy back.

## Member Reference

**BuybackItem**
Constructor for the `BuybackItem` packet. Initializes the base `ClientPacket` with opcode `CMSG_BUYBACK_ITEM`. Sets `vendorGuid` to its default state. Conditionally initializes `slot` to `0` if `SUPPORTED_CLIENT_BUILD > CLIENT_BUILD_1_7_1`. No external calls are made.

---

<!-- machine-true, projected from graph.json -->

## Map — BuybackItem

*Source:* Item.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| BuybackItem | ctor | — | — | — |
