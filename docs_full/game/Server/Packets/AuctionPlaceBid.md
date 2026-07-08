# AuctionPlaceBid

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# AuctionPlaceBid

**Purpose & Responsibilities**

`AuctionPlaceBid` is a packet structure within the `WorldPackets::AuctionHouse` namespace, responsible for representing the client-to-server message `CMSG_AUCTION_PLACE_BID`. Its sole responsibility is to deserialize binary data received from the game client into structured fields that the server can process to place a bid on an existing auction. It acts as a data carrier, holding the GUID of the auctioneer NPC, the ID of the specific auction being bid on, and the bid amount proposed by the player.

**Member-by-Member Behavior**

The unit contains a single member: the constructor.

*   **`AuctionPlaceBid`**: This is the default constructor for the packet. It initializes the base class `ClientPacket` with the opcode `CMSG_AUCTION_PLACE_BID`, identifying the type of message to the network layer. It also initializes the member variables `auctionId` and `price` to `0` via in-class initializers, ensuring a known state before deserialization occurs. The constructor does not perform any I/O or logic; it merely prepares the object for use.

**Cross-Unit Boundaries**

*   **Calls Out**: None. The constructor performs no external calls.
*   **Called By**: None listed in the map. In practice, this packet is instantiated by the network handler when the server receives the `CMSG_AUCTION_PLACE_BID` opcode from a client. The handler will then call `ReadFromWorldPacket` (declared in the base class `ClientPacket` but implemented in derived classes like this one) to populate the fields. However, since `ReadFromWorldPacket` is not listed in the MAP for this unit, it is not described as part of this unit's behavior here.

**Data Model**

This unit does not interact directly with any database tables. It operates purely on in-memory data structures representing network packets.

**Notable Implementation Details**

*   **In-Class Initialization**: The members `auctionId` and `price` are initialized to `0` directly in the class definition (`uint32 auctionId = 0;`, `uint32 price = 0;`). This ensures that if `ReadFromWorldPacket` fails or is not called, these fields hold safe default values rather than garbage memory.
*   **Final Class**: The class is marked `final`, preventing further inheritance. This is consistent with its role as a leaf node in the packet hierarchy.
*   **Namespace**: It resides in `WorldPackets::AuctionHouse`, indicating it is part of the world server's packet handling subsystem, specifically for auction house interactions.

## Member Reference

**AuctionPlaceBid**
Constructor for the `AuctionPlaceBid` packet. Initializes the base `ClientPacket` with the opcode `CMSG_AUCTION_PLACE_BID`. Relies on in-class initializers to set `auctionId` and `price` to `0`. Does not perform any deserialization or external calls.

---

<!-- machine-true, projected from graph.json -->

## Map — AuctionPlaceBid

*Source:* AuctionHouse.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| AuctionPlaceBid | ctor | — | — | — |
