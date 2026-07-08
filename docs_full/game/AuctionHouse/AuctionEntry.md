# AuctionEntry

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# AuctionEntry

**Purpose & Responsibilities**

`AuctionEntry` is a lightweight data structure (defined as a `struct` in `AuctionHouseMgr.h`) that represents a single active listing within the World of Warcraft auction house system. It holds all the state necessary to track an item's sale, including ownership, bidding history, pricing, timing, and the specific auction house instance it belongs to.

Unlike the `AuctionHouseObject` class, which manages collections of auctions, `AuctionEntry` is the atomic unit of auction data. It does not manage its own lifecycle (creation/destruction) or persistence directly in this partial; rather, it provides accessor methods and helper functions that allow other parts of the server (such as mail handlers, session handlers, and the auction manager itself) to query and manipulate the auction's state.

The struct relies heavily on a pointer to `AuctionHouseEntry` (loaded from `AuctionHouse.dbc`) to determine contextual properties like the auction house ID and faction alignment.

## Member-by-Member Behavior

The `AuctionEntry` struct contains two primary accessor methods documented in this unit: `GetHouseId` and `GetHouseFaction`. Both are inline getters that delegate to the associated `AuctionHouseEntry` DBC record.

### Accessors for Auction House Context

*   **`GetHouseId`**: Returns the unique identifier of the auction house instance (e.g., Alliance vs. Horde, or specific city instances) where this item is listed. It retrieves this value from the `houseId` field of the linked `AuctionHouseEntry`.
*   **`GetHouseFaction`**: Returns the faction ID associated with the auction house. This is used to enforce faction-based restrictions (e.g., preventing players from one faction from bidding on items in another faction's auction house). It retrieves this value from the `faction` field of the linked `AuctionHouseEntry`.

Both methods are `const`, indicating they do not modify the state of the `AuctionEntry`. They assume that the `auctionHouseEntry` pointer is valid and non-null.

## Cross-Unit Boundaries

`AuctionEntry` acts as a passive data holder, but its accessors are critical for decision-making logic in other units.

*   **Called by `game_Mail_Mail/MailSender#3`**: The mail system uses `GetHouseId` when constructing notifications related to auctions (e.g., winning bids, expired auctions). The mail sender needs to know which auction house the transaction originated from to potentially include context-specific information or routing in the mail message.
*   **Called by `WorldSession.AuctionHouseHandler/HandleAuctionSellItem`**: When a player attempts to list an item, the session handler uses `GetHouseId` to validate the request. It ensures the player is interacting with the correct auction house interface and that the item is being placed in a valid location relative to the player's current context.
*   **Called by `WorldSession.AuctionHouseHandler/SendAuctionBidderNotification`**: When sending notifications to bidders (e.g., outbid notices), the handler uses `GetHouseId` to ensure the notification is correctly associated with the specific auction house instance, maintaining consistency in the client-side UI.

`AuctionEntry` does not call out to any other units in these specific methods. It relies entirely on the pre-loaded `AuctionHouseEntry` DBC data.

## Data Model

`AuctionEntry` does not directly interact with database tables in the methods provided in this partial (`GetHouseId`, `GetHouseFaction`). However, the struct itself mirrors the schema of the `auctionhouse` table in the database. The fields in `AuctionEntry` (such as `Id`, `itemGuidLow`, `owner`, `startbid`, `bid`, `buyout`, `expireTime`, etc.) correspond to columns in the `auctionhouse` table.

While this specific partial does not execute SQL queries, the existence of methods like `SaveToDB()` and `DeleteFromDB()` (declared in the struct but not implemented in this partial) indicates that `AuctionEntry` is responsible for persisting its state to the `auctionhouse` table. The `auctionHouseEntry` pointer links to the `AuctionHouse.dbc` client data file, not a server-side database table, providing static configuration for the auction house instance.

## Notable Implementation Details

1.  **Dependency on DBC Pointer**: The correctness of `GetHouseId` and `GetHouseFaction` depends entirely on the `auctionHouseEntry` pointer being properly initialized. If this pointer is null, calling these methods will result in undefined behavior (likely a crash). The code assumes that any valid `AuctionEntry` in memory has a valid link to its DBC record.
2.  **Inline Performance**: These methods are defined inline within the struct definition. This suggests they are called frequently and that minimizing function call overhead is considered important for performance, likely due to the high volume of auction-related queries during peak server load.
3.  **Const Correctness**: Both methods are marked `const`, reinforcing that querying the auction house context does not alter the auction's state. This allows them to be called on `const AuctionEntry` objects, such as when iterating over a collection of auctions for display purposes.
4.  **No Validation**: There is no validation logic in these getters. They do not check if the returned ID or faction is valid within the game world; they simply return the raw values from the DBC record. Validation of whether a player can interact with a specific auction house is handled by the callers (e.g., `WorldSession.AuctionHouseHandler`).

## Member Reference

**GetHouseId**
Returns the `houseId` from the associated `AuctionHouseEntry` DBC record. Used by mail senders and auction handlers to identify the specific auction house instance.

**GetHouseFaction**
Returns the `faction` from the associated `AuctionHouseEntry` DBC record. Used to determine faction alignment for access control and UI display.

---

<!-- machine-true, projected from graph.json -->

## Map — AuctionEntry

*Source:* AuctionHouseMgr.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GetHouseId | method | — | game_Mail_Mail/MailSender#3, WorldSession.AuctionHouseHandler/HandleAuctionSellItem, WorldSession.AuctionHouseHandler/SendAuctionBidderNotification | — |
| GetHouseFaction | method | — | — | — |
