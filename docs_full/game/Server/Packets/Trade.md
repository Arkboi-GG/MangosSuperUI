# Trade

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Trade Packet Definitions (`WorldPackets::Trade`)

**Purpose & Responsibilities**

This unit defines five client-to-server packet structures within the `WorldPackets::Trade` namespace. These classes represent the negotiation and setup phase of the trading subsystem: initiating a trade window, placing items and gold into trade slots, removing items, and accepting the trade. As `ClientPacket` subclasses, they are responsible solely for **deserialization**: reading binary data from a `WorldPacket` buffer into strongly-typed C++ members. They contain no game logic, validation, or server-side state management.

## Member-by-Member Behavior

### Trade Initiation
**`InitiateTrade`**
Represents the `CMSG_INITIATE_TRADE` packet. It captures the `ObjectGuid` of the target player.
*   **`ReadFromWorldPacket#3`**: Extracts the `tradeTargetGuid` from the packet stream. It calls `ObjectGuid::operator>>` (from the `ObjectGuid` unit) to deserialize the complex GUID structure.

### Trade Content Modification
**`SetTradeGold`**
Represents `CMSG_SET_TRADE_GOLD`. Used when a player adds gold to their side of the trade window.
*   **`ReadFromWorldPacket#4`**: Reads a single `uint32` value representing the amount of gold (in copper) and stores it in the `gold` member. It uses `ByteBuffer::operator>>#9` for extraction.

**`SetTradeItem`**
Represents `CMSG_SET_TRADE_ITEM`. Used when a player places an item from their inventory into the trade window.
*   **`ReadFromWorldPacket#5`**: Reads three `uint8` values: `tradeSlot` (destination in trade window), `bag` (source bag ID), and `slot` (source slot index). It uses `ByteBuffer::operator>>#6` for extraction.

**`ClearTradeItem`**
Represents `CMSG_CLEAR_TRADE_ITEM`. Used when a player removes an item from the trade window.
*   **`ReadFromWorldPacket#2`**: Reads a single `uint8` (`tradeSlot`) indicating which trade slot to empty. It uses `ByteBuffer::operator>>#6` for extraction.

### Trade Finalization
**`AcceptTrade`**
Represents `CMSG_ACCEPT_TRADE`. Sent when the player accepts the trade.
*   **`ReadFromWorldPacket`**: Explicitly skips one `uint32` field using `recv_data.read_skip<uint32>()`. The source comment notes this is an unused variable set to 1 if the player had a trade window open in the current session. This field is consumed only to maintain stream alignment.

## Cross-Unit Boundaries

*   **`ObjectGuid` Unit**: Called by `InitiateTrade::ReadFromWorldPacket#3` via `ObjectGuid::operator>>` to parse the target player's GUID.
*   **`ByteBuffer` Unit**: Called by all `ReadFromWorldPacket` methods via overloaded `operator>>` variants (`#6`, `#9`) to extract primitive types (`uint8`, `uint32`) from the packet buffer.

## Data Model

This unit interacts exclusively with in-memory network packets. It performs **no** direct database queries and touches **no** database tables.

## Notable Implementation Details

1.  **No Validation**: None of the `ReadFromWorldPacket` methods perform bounds checking (e.g., valid slot indices). Validation is deferred to higher-level handlers.
2.  **Legacy Field Skipping**: `AcceptTrade::ReadFromWorldPacket` discards a `uint32` field. Maintainers must ensure this skip remains synchronized with the client protocol; removing it without client changes will desynchronize subsequent packet reads.

## Member Reference

*   **InitiateTrade**: Constructor for the `InitiateTrade` packet class. Initializes the base `ClientPacket` with opcode `CMSG_INITIATE_TRADE`.
*   **ReadFromWorldPacket#3**: Deserializes the `tradeTargetGuid` from the incoming `WorldPacket` by calling `ObjectGuid::operator>>`.
*   **ReadFromWorldPacket#4**: Deserializes the `gold` amount (`uint32`) from the incoming `WorldPacket` using `ByteBuffer::operator>>#9`.
*   **ReadFromWorldPacket#5**: Deserializes three `uint8` values (`tradeSlot`, `bag`, `slot`) from the incoming `WorldPacket` using `ByteBuffer::operator>>#6`.
*   **ReadFromWorldPacket#2**: Deserializes the `tradeSlot` (`uint8`) from the incoming `WorldPacket` using `ByteBuffer::operator>>#6`.
*   **ReadFromWorldPacket**: Skips one `uint32` field from the incoming `WorldPacket` to advance the stream pointer correctly.

---

<!-- machine-true, projected from graph.json -->

## Map — Trade

*Source:* Trade.cpp, Trade.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ReadFromWorldPacket#3 | method | ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket#4 | method | ByteBuffer/operator>>#9 | — | — |
| ReadFromWorldPacket#5 | method | ByteBuffer/operator>>#6 | — | — |
| InitiateTrade | ctor | — | — | — |
| ReadFromWorldPacket#2 | method | ByteBuffer/operator>>#6 | — | — |
| ReadFromWorldPacket | method | — | — | — |
