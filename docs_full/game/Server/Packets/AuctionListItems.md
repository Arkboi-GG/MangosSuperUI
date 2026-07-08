# AuctionListItems

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# AuctionListItems

## Purpose & Responsibilities

`AuctionListItems` is a client-to-server packet structure within the `WorldPackets::AuctionHouse` namespace. Its sole responsibility is to encapsulate the data sent by a client when requesting a filtered list of items currently available on an auction house. It acts as a data carrier for search criteria, including the target auctioneer, pagination offset, item name fragments, level ranges, category filters, quality requirements, and usability constraints.

This unit does not perform any logic, validation, or database interaction itself. It strictly defines the memory layout and serialization interface (`ReadFromWorldPacket`) for the `CMSG_AUCTION_LIST_ITEMS` message type.

## Member-by-Member Behavior

### Construction
**`AuctionListItems`**
The default constructor initializes the packet with the opcode `CMSG_AUCTION_LIST_ITEMS`. It sets default values for several fields:
- `listfrom`: 0 (indicating the start of the list)
- `levelmin`: 0
- `levelmax`: 0
- `auctionSlotID`: 0
- `auctionMainCategory`: 0
- `auctionSubCategory`: 0
- `quality`: 0
- `usable`: 0

Fields `auctioneerGuid`, `searchedname`, and the remaining integers are left in their default-initialized states (empty GUID, empty string, zero).

### Serialization
**`ReadFromWorldPacket(WorldPacket& recv_data)`**
This virtual method overrides the base class implementation to deserialize the raw binary data from the network packet into the member variables. Based on standard Mangos/World of Warcraft packet structures for this message, it extracts:
1. The `auctioneerGuid` (ObjectGuid).
2. The `listfrom` index (uint32).
3. The `searchedname` string (std::string).
4. The `levelmin` and `levelmax` values (uint8).
5. The `auctionSlotID` (uint32).
6. The `auctionMainCategory` and `auctionSubCategory` (uint32).
7. The `quality` filter (uint32).
8. The `usable` flag (uint8).

*Note: The specific extraction order and bit-packing details are implemented in the corresponding `.cpp` file, which is not provided in the source snippet, but the member declarations define the expected payload.*

## Cross-Unit Boundaries

- **Called By:** This packet is instantiated and populated by the network layer when the server receives a `CMSG_AUCTION_LIST_ITEMS` message from a client. It is then passed to the auction house handler logic (likely in `AuctionHouseHandler.cpp` or similar, though not shown in the map) which interprets these filters to query the database.
- **Calls Out:** None. This unit is a pure data structure with no dependencies on other business logic units.

## Data Model

This unit does not directly interact with database tables. It carries filter criteria that *will be used* by downstream handlers to query auction-related tables (such as `auctionhouse` or `item_instance`). However, `AuctionListItems` itself performs no SQL operations.

## Notable Implementation Details

- **Filtering Granularity:** The packet supports a wide range of filters, allowing clients to request highly specific subsets of auctions. This includes text-based search (`searchedname`), numerical ranges (`levelmin`/`levelmax`), categorical filters (`auctionMainCategory`/`auctionSubCategory`), and boolean-like flags (`usable`).
- **Pagination:** The `listfrom` field indicates the starting index for the results, enabling paginated retrieval of large auction lists.
- **Default Values:** Many fields default to 0. In the context of auction filtering, a value of 0 often implies "no filter" or "any value," depending on how the downstream handler interprets these defaults. For instance, `levelmin=0` and `levelmax=0` might mean "all levels," while `quality=0` might mean "any quality."

## Member Reference

**AuctionListItems**
Default constructor that initializes the packet with opcode `CMSG_AUCTION_LIST_ITEMS` and sets default values for numeric and string fields.

---

<!-- machine-true, projected from graph.json -->

## Map — AuctionListItems

*Source:* AuctionHouse.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| AuctionListItems | ctor | — | — | — |
