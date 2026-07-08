# AuctionListOwnerItems

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# AuctionListOwnerItems

**Purpose & Responsibilities**

`AuctionListOwnerItems` is a client-side packet structure within the `WorldPackets::AuctionHouse` namespace, responsible for encapsulating the data sent by the client when a player requests to view the items they currently have listed for sale on an auction house. It corresponds to the network message opcode `CMSG_AUCTION_LIST_OWNER_ITEMS`.

As a `ClientPacket`, its primary responsibility is to define the binary layout and deserialization logic for this specific request. It holds two pieces of state:
1.  `auctioneerGuid`: The unique identifier of the Auctioneer NPC with whom the interaction is taking place.
2.  `listfrom`: An index indicating the starting position for pagination, allowing the server to return a subset of the owner's auctions if the list is long.

This unit does not perform business logic, database queries, or validation. It strictly serves as a data container and parser for the incoming network stream.

## Member-by-Member Behavior

### **AuctionListOwnerItems** (Constructor)
The default constructor initializes the packet object. It sets the internal packet opcode to `CMSG_AUCTION_LIST_OWNER_ITEMS` via the base class `ClientPacket` constructor. It also initializes the `listfrom` member variable to `0` using an in-class initializer. This ensures that if no specific page offset is provided or parsed, the default behavior is to start from the beginning of the list.

## Cross-Unit Boundaries

*   **Calls Out:** None. This unit is a leaf node in the call graph regarding outgoing dependencies. It relies on the base class `ClientPacket` (defined elsewhere) for packet identification and potentially helper methods for reading, but it does not actively call other business logic units.
*   **Called By:** None listed in the MAP. In practice, this packet would be instantiated and populated by the network layer (likely in a handler such as `AuctionHouseHandler.cpp`) upon receiving the `CMSG_AUCTION_LIST_OWNER_ITEMS` opcode from the client. The handler would then pass this populated object to the auction house service logic.

## Data Model

This unit does not interact directly with any database tables. It operates solely on in-memory data structures derived from network packets. Any persistence or retrieval of auction data occurs in downstream units that consume this packet.

## Notable Implementation Details

*   **Pagination Support:** The presence of the `listfrom` field indicates that the auction house system supports paginated results for owner items. The default value of `0` suggests that clients typically request the first page unless they have scrolled further.
*   **Inheritance:** Inherits from `ClientPacket`, which likely provides common functionality for all client-to-server packets, such as opcode management and basic validation hooks.
*   **Namespace Organization:** Located in `WorldPackets::AuctionHouse`, clearly segregating auction-related network protocols from other game systems.

## Member Reference

**AuctionListOwnerItems**
Default constructor that initializes the packet with the opcode `CMSG_AUCTION_LIST_OWNER_ITEMS` and sets `listfrom` to `0`.

---

<!-- machine-true, projected from graph.json -->

## Map — AuctionListOwnerItems

*Source:* AuctionHouse.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| AuctionListOwnerItems | ctor | — | — | — |
