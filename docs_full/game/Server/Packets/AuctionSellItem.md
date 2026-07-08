# AuctionSellItem

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# AuctionSellItem

## Purpose & Responsibilities

`AuctionSellItem` is a data structure within the `WorldPackets::AuctionHouse` namespace that represents a client-to-server network message (`CMSG_AUCTION_SELL_ITEM`). Its sole responsibility is to encapsulate the raw data sent by a player when attempting to list an item for sale on the in-game auction house. It acts as a passive container for deserialization; it does not contain business logic, validation, or database interaction code itself. Instead, it provides a structured interface for higher-level handlers (not shown in this unit) to extract the necessary parameters—such as the item being sold, the desired bid/buyout prices, and the duration of the listing—to process the transaction.

## Member-by-Member Behavior

The unit consists of a single constructor and several public data members.

### Construction and Initialization

**`AuctionSellItem()`**
This is the explicit default constructor. It initializes the base class `ClientPacket` with the opcode `CMSG_AUCTION_SELL_ITEM`, identifying this packet type to the network layer. It also initializes the numeric fields (`bid`, `buyout`, `etime`) to zero via in-class member initializers. The `ObjectGuid` members (`auctioneerGuid`, `itemGuid`) are default-constructed (empty/null GUIDs). This constructor ensures that any instance of `AuctionSellItem` starts in a known, safe state before data is read from the network stream.

### Data Members

The following members store the payload of the auction sell request:

*   **`auctioneerGuid`**: An `ObjectGuid` representing the unique identifier of the Auctioneer NPC with whom the player is interacting. This allows the server to validate that the player is near a valid auction house vendor.
*   **`itemGuid`**: An `ObjectGuid` representing the unique identifier of the specific item instance the player wishes to sell. This distinguishes between identical items owned by the player.
*   **`bid`**: A `uint32` representing the minimum starting bid price set by the seller.
*   **`buyout`**: A `uint32` representing the optional immediate purchase price. If set, buyers can skip bidding and buy the item instantly for this amount.
*   **`etime`**: A `uint32` representing the expiration time (duration) of the auction listing, typically encoded in hours or days depending on the game's internal time representation.

## Cross-Unit Boundaries

As a pure data structure defined in `AuctionHouse.h`, `AuctionSellItem` has no outgoing calls to other units. It is designed to be instantiated and populated by the network layer.

*   **Called By**: While the MAP indicates no external callers are explicitly listed, in the broader context of the Mangos architecture, instances of `AuctionSellItem` are typically created by the network handler loop when a `CMSG_AUCTION_SELL_ITEM` packet is received from a client. The handler then passes this object to an auction house manager or session handler (units not included in this specific MAP) to execute the sell logic.
*   **Dependencies**: It depends on `Packet.h` (for the `ClientPacket` base class and `WorldPacket` type) and `ObjectGuid.h` (for the GUID type). These are standard library components within the engine and are not detailed as "other units" in the cross-boundary MAP because they are foundational types rather than functional collaborators.

## Data Model

This unit does not interact directly with any database tables. It operates entirely in memory as a transient network packet buffer. Any persistence of the auction data occurs in downstream units that consume this packet, likely writing to tables such as `auctionhouse` or `item_instance` (names inferred from common WoW emulator schemas, but **not** present in this unit's code or MAP). Therefore, no database schema is relevant to this specific translation unit.

## Notable Implementation Details

1.  **Passive Structure**: `AuctionSellItem` contains no methods other than the constructor and the inherited `ReadFromWorldPacket`. All logic regarding how the data is parsed from the binary stream is implemented in the `ReadFromWorldPacket` method, which is declared here but defined elsewhere (likely in a corresponding `.cpp` file not included in this source snippet, or potentially inline in a different partial). However, since `ReadFromWorldPacket` is not listed in the MAP as a member *of this unit* (only the constructor is), we treat the class as a data holder for the purposes of this documentation. The MAP strictly lists only `AuctionSellItem` (the constructor) as the member.
2.  **Zero-Initialization**: The numeric fields are initialized to `0` in the class definition. This is a safety measure to prevent undefined behavior if the packet reading fails or if fields are missing from older client versions.
3.  **Explicit Constructor**: The use of `explicit` prevents implicit conversions from `WorldPacket` or other types, ensuring that `AuctionSellItem` objects are only created intentionally.

## Member Reference

**AuctionSellItem**
The default constructor for the `AuctionSellItem` packet. It initializes the base `ClientPacket` with the opcode `CMSG_AUCTION_SELL_ITEM` and sets all numeric data members (`bid`, `buyout`, `etime`) to zero. It prepares the object to receive data via the `ReadFromWorldPacket` method (defined externally).

---

<!-- machine-true, projected from graph.json -->

## Map — AuctionSellItem

*Source:* AuctionHouse.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| AuctionSellItem | ctor | — | — | — |
