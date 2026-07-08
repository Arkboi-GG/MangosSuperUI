# AuctionHouse

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# AuctionHouse Packet Definitions

**Purpose & Responsibilities**
The `AuctionHouse` unit defines client-to-server network packet structures within `WorldPackets::AuctionHouse`. It deserializes raw binary data from `WorldPacket` into typed C++ objects for auction house interactions. This unit handles only data extraction; it performs no validation, game logic, or database access.

## Member-by-Member Behavior

All members implement `ReadFromWorldPacket` or constructors, parsing sequential fields via stream extraction (`>>`).

### Initialization
*   **`AuctionHello`**: Handshake packet. Parses `auctioneerGuid` (NPC identifier).

### Browsing & Searching
*   **`AuctionListItems`**: General search. Parses `auctioneerGuid`, pagination (`listfrom`), text filter (`searchedname`), level range (`levelmin`/`levelmax`), slot/category/quality filters, and `usable` flag.
*   **`AuctionListOwnerItems`**: Lists player-owned auctions. Parses `auctioneerGuid` and pagination offset `listfrom`.
*   **`AuctionListBidderItem`**: Lists player-bid auctions. Parses `auctioneerGuid`, pagination `pagingElementStartIndex`, and a variable-length list of `auctionId`s in `bidAuctionIdsToRefresh` (prefixed by a count).

### Transactions
*   **`AuctionSellItem`**: Creates an auction. Parses `auctioneerGuid`, `itemGuid`, starting `bid`, `buyout` price, and duration `etime`.
*   **`AuctionPlaceBid`**: Places a bid. Parses `auctioneerGuid`, target `auctionId`, and `price`.
*   **`AuctionRemoveItem`**: Cancels an auction. Parses `auctioneerGuid` and `auctionId`.

## Cross-Unit Boundaries

*   **Calls Out**:
    *   **`ObjectGuid`**: All methods use `operator>>` to deserialize 64-bit entity IDs.
    *   **`ByteBuffer`**: Methods use various `operator>>` overloads (`#6`, `#9`) to extract primitives (`uint32`, `uint8`, `std::string`) from the `WorldPacket` buffer.
*   **Called By**:
    *   Network handlers (e.g., `WorldSession`) instantiate these classes upon receiving opcodes, call `ReadFromWorldPacket`, and pass the result to game logic handlers.

## Data Model

This unit performs **no database operations**. It processes transient network payloads only.

## Notable Implementation Details

1.  **Variable-Length Arrays**: `AuctionListBidderItem::ReadFromWorldPacket` reads a count (`idsToRefresh`) then loops to read individual IDs. Deserialization drifts if the count is incorrect.
2.  **No Validation**: Fields like `bid` or `etime` are not validated for range or positivity here; validation occurs in downstream handlers.
3.  **Defaults**: Header-initializers (e.g., `listfrom = 0`) serve as fallbacks but are overwritten by packet data.

## Member Reference

*   **`AuctionHello`**: Constructor sets opcode `MSG_AUCTION_HELLO`.
*   **`ReadFromWorldPacket`**: Extracts `auctioneerGuid`.
*   **`ReadFromWorldPacket#2`**: Extracts `auctioneerGuid`, `pagingElementStartIndex`, and a counted list of `auctionId`s into `bidAuctionIdsToRefresh`.
*   **`ReadFromWorldPacket#3`**: Extracts `auctioneerGuid`, `listfrom`, `searchedname`, `levelmin`, `levelmax`, `auctionSlotID`, `auctionMainCategory`, `auctionSubCategory`, `quality`, and `usable`.
*   **`ReadFromWorldPacket#4`**: Extracts `auctioneerGuid` and `listfrom`.
*   **`ReadFromWorldPacket#5`**: Extracts `auctioneerGuid`, `auctionId`, and `price`.
*   **`ReadFromWorldPacket#6`**: Extracts `auctioneerGuid` and `auctionId`.
*   **`ReadFromWorldPacket#7`**: Extracts `auctioneerGuid`, `itemGuid`, `bid`, `buyout`, and `etime`.

---

<!-- machine-true, projected from graph.json -->

## Map — AuctionHouse

*Source:* AuctionHouse.cpp, AuctionHouse.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ReadFromWorldPacket | method | ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket#2 | method | ByteBuffer/operator>>#9, ObjectGuid/operator>> | — | — |
| AuctionHello | ctor | — | — | — |
| ReadFromWorldPacket#4 | method | ByteBuffer/operator>>#9, ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket#5 | method | ByteBuffer/operator>>#9, ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket#6 | method | ByteBuffer/operator>>#9, ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket#7 | method | ByteBuffer/operator>>#9, ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket#3 | method | ByteBuffer/operator>>, ByteBuffer/operator>>#6, ByteBuffer/operator>>#9, ObjectGuid/operator>> | — | — |
