# AuctionListBidderItem

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# AuctionListBidderItem

## Purpose & Responsibilities

`AuctionListBidderItem` is a client-side packet structure within the `WorldPackets::AuctionHouse` namespace, defined in `AuctionHouse.h`. It represents the `CMSG_AUCTION_LIST_BIDDER_ITEMS` message sent by the game client to the server. Its sole responsibility is to encapsulate the data required for a player to request a list of auctions on which they have placed bids. This allows the client to refresh or retrieve the status of active bids associated with the player's account.

As a `ClientPacket`, it inherits the standard packet handling infrastructure but contains no custom logic beyond its constructor and the inherited `ReadFromWorldPacket` interface (which is implemented elsewhere, likely in a corresponding `.cpp` file not included in this unit's scope, though the declaration exists here). The unit itself is purely a data carrier for the network layer.

## Member-by-Member Behavior

The unit consists of a single member: the default constructor.

### Constructor Initialization

The **`AuctionListBidderItem`** constructor initializes the packet object for transmission. It performs two key actions:
1.  **Base Class Initialization**: It calls the base class `ClientPacket` constructor with the constant `CMSG_AUCTION_LIST_BIDDER_ITEMS`. This registers the packet type with the network engine, ensuring that when the server receives this message ID, it knows to deserialize it into an `AuctionListBidderItem` instance.
2.  **Member Initialization**: It explicitly initializes the `pagingElementStartIndex` member to `0`. This sets the default pagination offset, indicating that the client initially requests the first page of bidder items. Other members (`auctioneerGuid` and `bidAuctionIdsToRefresh`) rely on their default constructors (zero-initialization for `ObjectGuid` and empty vector for `std::vector`).

## Cross-Unit Boundaries

This unit acts as a data structure and does not contain executable logic that calls out to other units. However, it participates in the following cross-unit interactions:

*   **Inheritance**: It derives from `ClientPacket` (defined in `Packet.h`). This establishes the contract for network serialization and deserialization.
*   **Usage by Network Layer**: While not shown in the "Called by" column of the map (as it is a data structure), instances of `AuctionListBidderItem` are created and populated by the network handler when a `CMSG_AUCTION_LIST_BIDDER_ITEMS` packet arrives from the client. The network layer then passes this object to the auction house handler logic (likely in `AuctionHouseMgr` or similar) to process the bid list request.
*   **Data Dependencies**: It uses `ObjectGuid` (from `ObjectGuid.h`) to identify the auctioneer NPC and `std::vector` (from `<vector>`) to hold a list of auction IDs.

## Data Model

This unit does not directly interact with database tables. It operates entirely in memory as part of the network packet processing pipeline. The data it carries (`auctioneerGuid`, `pagingElementStartIndex`, `bidAuctionIdsToRefresh`) is used by downstream handlers to query the database (e.g., the `auctionhouse` table) for bid information, but `AuctionListBidderItem` itself performs no SQL operations.

## Notable Implementation Details

*   **Pagination Strategy**: The `pagingElementStartIndex` field suggests that the auction house system supports paginated results for bidder items. The comment indicates this index should be a multiple of 50, implying a fixed page size of 50 items. The constructor defaults this to 0, ensuring the first request always starts at the beginning of the list.
*   **Selective Refresh**: The `bidAuctionIdsToRefresh` vector allows the client to request updates for specific auctions only. This is an optimization to reduce bandwidth and server load when the client only needs to check the status of a few specific bids rather than refreshing the entire list. The comment notes these should be auctions where the player previously bid.
*   **No Custom Read Logic**: The `ReadFromWorldPacket` method is declared but not defined in this header. This implies the deserialization logic is either generated, templated, or implemented in a separate source file (e.g., `AuctionHouse.cpp` or a dedicated packet reader file). The header only defines the data layout.

## Member Reference

**AuctionListBidderItem**
Default constructor for the `AuctionListBidderItem` packet. Initializes the base `ClientPacket` with the message ID `CMSG_AUCTION_LIST_BIDDER_ITEMS` and sets `pagingElementStartIndex` to 0. Other members are default-initialized.

---

<!-- machine-true, projected from graph.json -->

## Map — AuctionListBidderItem

*Source:* AuctionHouse.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| AuctionListBidderItem | ctor | — | — | — |
