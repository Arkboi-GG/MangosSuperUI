# AuctionRemoveItem

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# AuctionRemoveItem

**Purpose & Responsibilities**

`AuctionRemoveItem` is a client-side packet structure within the `WorldPackets::AuctionHouse` namespace. Its sole responsibility is to represent the `CMSG_AUCTION_REMOVE_ITEM` message sent from the game client to the server. This message indicates that a player intends to remove an item from an active auction house listing (typically canceling their own auction before it expires or sells). The class acts as a data container, holding the necessary identifiers—the GUID of the auctioneer NPC and the ID of the specific auction—to allow the server to locate and process the removal request.

**Member-by-Member Behavior**

The unit consists of a single constructor and two public data members, along with an inherited virtual method declaration.

*   **Data Members**:
    *   `auctioneerGuid`: An `ObjectGuid` representing the unique identifier of the Auction House NPC (Non-Player Character) with whom the transaction is taking place. This allows the server to validate that the request is directed at a valid auction house entity.
    *   `auctionId`: A `uint32` representing the unique identifier of the specific auction listing to be removed. This ID corresponds to the record in the server's auction database or memory structures.

*   **Constructor (`AuctionRemoveItem`)**:
    *   Initializes the base class `ClientPacket` with the opcode `CMSG_AUCTION_REMOVE_ITEM`. This opcode tells the network layer how to route and identify this packet type.
    *   Default-initializes `auctionId` to `0`. Note that `auctioneerGuid` relies on default initialization of `ObjectGuid`.

*   **ReadFromWorldPacket**:
    *   Declared as an override of the pure virtual function from `ClientPacket`. While the definition is not present in this header, its signature indicates that this method will parse the raw binary data from a `WorldPacket` into the structured members (`auctioneerGuid` and `auctionId`).

**Cross-Unit Boundaries**

*   **Calls Out**: None. This unit is a passive data structure and does not invoke methods in other units.
*   **Called By**: None listed in the map. In practice, instances of this class are typically constructed by the network layer upon receiving the corresponding opcode from the client, then passed to the auction house handler logic (likely in a separate handler unit like `AuctionHouseHandler.cpp`) for validation and execution.

**Data Model**

This unit does not directly interact with database tables. It operates entirely in memory as part of the network packet parsing layer. The `auctionId` it carries likely corresponds to a primary key in an `auctionhouse` table (common in WoW-like databases), but this unit itself performs no SQL queries or schema interactions.

**Notable Implementation Details**

*   **Final Class**: The class is marked `final`, preventing inheritance. This is appropriate for a leaf-node packet structure.
*   **Default Initialization**: `auctionId` is explicitly initialized to `0` in the member declaration. `auctioneerGuid` is not explicitly initialized in the constructor initializer list, relying on `ObjectGuid`'s default constructor.
*   **Namespace**: It resides in `WorldPackets::AuctionHouse`, indicating it is part of a modular packet system separating different game subsystems (Auction House vs. Chat, Combat, etc.).

## Member Reference

**AuctionRemoveItem**
Constructor for the `AuctionRemoveItem` packet. It initializes the base `ClientPacket` with the opcode `CMSG_AUCTION_REMOVE_ITEM` and sets the `auctionId` member to `0`. It prepares the object to receive deserialized data from the network layer.

---

<!-- machine-true, projected from graph.json -->

## Map — AuctionRemoveItem

*Source:* AuctionHouse.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| AuctionRemoveItem | ctor | — | — | — |
