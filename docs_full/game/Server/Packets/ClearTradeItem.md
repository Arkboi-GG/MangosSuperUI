# ClearTradeItem

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ClearTradeItem

`ClearTradeItem` is a client-side packet structure within the `WorldPackets::Trade` namespace, defined in `Trade.h`. It represents the `CMSG_CLEAR_TRADE_ITEM` message sent by a client to the server when a player removes an item from a specific slot in their active trade window.

## Purpose & Responsibilities

The primary responsibility of `ClearTradeItem` is to encapsulate the data required to identify which trade slot the player intends to clear. As a `ClientPacket`, it serves as the deserialization target for incoming network data corresponding to the `CMSG_CLEAR_TRADE_ITEM` opcode. It holds a single piece of state: the index of the trade slot (`tradeSlot`) from which the item should be removed.

## Member-by-Member Behavior

### **ClearTradeItem** (Constructor)
The constructor initializes the packet object. It sets the internal opcode to `CMSG_CLEAR_TRADE_ITEM` via the base class `ClientPacket` constructor. It also initializes the public member `tradeSlot` to `0` via in-class initialization. This default value ensures that if the packet is instantiated but not yet populated from network data, the slot index is in a known safe state.

## Cross-Unit Boundaries

This unit has no outgoing calls to other units and is not called by other units according to the provided MAP. However, in the broader context of the server architecture:
- **Called By**: The network layer (likely `WorldSession` or similar packet handling infrastructure) will instantiate this packet and invoke its `ReadFromWorldPacket` method when a `CMSG_CLEAR_TRADE_ITEM` message is received from a client.
- **Calls Out**: While not listed in the MAP as calling other *units*, the `ReadFromWorldPacket` method (declared in this header but implemented elsewhere) will parse the raw binary data from a `WorldPacket` object to populate the `tradeSlot` member.

## Data Model

This unit does not interact directly with any database tables. It operates entirely on transient network data representing a client action.

## Notable Implementation Details

- **In-Class Initialization**: The member `tradeSlot` is initialized to `0` directly in the class definition (`uint8 tradeSlot = 0;`). This is a modern C++ idiom that ensures the variable is zero-initialized regardless of which constructor is used, providing a safe default.
- **Final Class**: The class is marked `final`, indicating it cannot be inherited. This is appropriate for a simple data structure/packet handler that has no need for polymorphic behavior.
- **Namespace**: It resides in `WorldPackets::Trade`, clearly grouping it with other trade-related network messages.

## Member Reference

**ClearTradeItem**
Constructs a `ClearTradeItem` packet, setting the opcode to `CMSG_CLEAR_TRADE_ITEM` and initializing the `tradeSlot` member to `0`.

---

<!-- machine-true, projected from graph.json -->

## Map — ClearTradeItem

*Source:* Trade.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ClearTradeItem | ctor | — | — | — |
