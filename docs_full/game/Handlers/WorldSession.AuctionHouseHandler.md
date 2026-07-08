<!-- provenance: failed-members, boundary-bleed -->
# WorldSession.AuctionHouseHandler

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# WorldSession.AuctionHouseHandler

## Purpose & Responsibilities

This unit implements the server-side logic for the Auction House subsystem within the `WorldSession` class. It handles network packets originating from the player client related to auction interactions. Its primary responsibilities include:

1.  **Interaction Validation:** Verifying that a player is interacting with a valid Auctioneer NPC or utilizing Game Master (GM) privileges correctly.
2.  **Auction Creation (`HandleAuctionSellItem`):** Processing requests to list items for sale, including rigorous validation of item properties, pricing limits, deposit calculations, and database persistence.
3.  **Bidding & Buyouts (`HandleAuctionPlaceBid`):** Managing new bids and buyout purchases, handling financial transactions, updating auction states, notifying participants, and finalizing sales.
4.  **Auction Cancellation (`HandleAuctionRemoveItem`):** Allowing owners to cancel active auctions, calculating and applying cancellation fees, returning items via mail, and refunding bidders.
5.  **Listing Queries:** Offloading heavy database queries for auction lists (all, owned, or bid upon) to asynchronous tasks to prevent server lag, then sending the results to the client.
6.  **Notifications & Mail:** Constructing and sending real-time UI notifications to online players and generating in-game mail for offline participants regarding wins, losses, cancellations, and refunds.

## Member-by-Member Behavior

### Interaction & Initialization

**`HandleAuctionHelloOpcode`**
Triggered when a player clicks on an Auctioneer NPC.
1.  Retrieves the `Creature` object for the provided GUID using `Player.Main/GetNPCIfCanInteractWith`. If the NPC is invalid or inaccessible, it logs a debug message and returns.
2.  Checks if the player is in a "Feign Death" state (`Unit.Main/HasUnitState`). If so, it removes the associated aura (`Unit.Main/RemoveSpellsCausingAura`) to permit interaction.
3.  Calls `SendAuctionHello` to open the auction window.

**`SendAuctionHello`**
Sends the `MSG_AUCTION_HELLO` packet to the client to initiate the auction interface.
1.  Retrieves the `AuctionHouseEntry` for the given `Unit` via `AuctionHouseMgr/GetAuctionHouseEntry`.
2.  Constructs a packet containing the Unit's GUID and the Auction House ID.
3.  Sends the packet via `WorldSession.Main/SendPacket`.
*Called by:* Various chat commands (`ChatHandler.MiscCommands`) and `Player.Main/OnGossipSelect`.

**`GetCheckedAuctionHouseForAuctioneer`**
A helper method to validate the target of an auction interaction, distinguishing between normal players and GMs.
1.  **GM Case:** If the target GUID matches the player's own GUID, it verifies if the player has the `auction` command permission via `ChatHandler.Chat/FindCommand`. If not, it logs a cheating attempt and returns `nullptr`. Otherwise, it treats the player themselves as the auctioneer.
2.  **Normal Case:** Attempts to retrieve the NPC via `Player.Main/GetNPCIfCanInteractWith`. If invalid, it logs a cheating attempt.
3.  Returns the `AuctionHouseEntry` for the validated unit via `AuctionHouseMgr/GetAuctionHouseEntry`.

### Notifications & Mail

These methods construct and send specific packets or mail to inform players of auction events. They rely on `AuctionHouseMgr` for item details and `ObjectMgr` for player resolution.

**`SendAuctionCommandResult`**
Sends the result of an auction action (create, bid, remove) to the client.
1.  Constructs `SMSG_AUCTION_COMMAND_RESULT`.
2.  Includes the Auction ID, Action type, and Error Code.
3.  Depending on the error code, appends additional data:
    *   `AUCTION_OK` (Bid Placed): Appends the outbid amount.
    *   `AUCTION_ERR_INVENTORY`: Appends the inventory error.
    *   `AUCTION_ERR_HIGHER_BID`: Appends the new bidder's GUID, bid amount, and outbid amount.
4.  Sends the packet.

**`SendAuctionBidderNotification`**
Notifies an online bidder that they have won or been outbid.
1.  Retrieves the item's random property ID via `game_Objects_Item/GetItemRandomPropertyId` using the item GUID from the auction.
2.  Constructs `SMSG_AUCTION_BIDDER_NOTIFICATION` with House ID, Auction ID, Bidder GUID, current bid (0 if won), outbid amount, item template, and random property ID.
3.  Sends the packet.
*Called by:* `AuctionHouseMgr/SendAuctionWonMail`.

**`SendAuctionOwnerNotification`**
Notifies the owner of an auction about a new bid, expiration, or sale.
1.  Determines the bidder GUID to send. If the auction is not sold, it sends the current bidder's GUID; otherwise, it sends an empty GUID.
2.  Retrieves the item's random property ID.
3.  Constructs `SMSG_AUCTION_OWNER_NOTIFICATION` with Auction ID, current bid, outbid amount, bidder GUID, item template, and random property ID.
4.  Sends the packet.
*Called by:* `AuctionHouseMgr/SendAuctionExpiredMail`, `AuctionHouseMgr/SendAuctionSuccessfulMail`.

**`SendAuctionRemovedNotification`**
Notifies a player that an auction they were involved in has been removed/cancelled.
1.  Retrieves the item's random property ID.
2.  Constructs `SMSG_AUCTION_REMOVED_NOTIFICATION` with Auction ID, item template, and random property ID.
3.  Sends the packet.

**`SendAuctionOutbiddedMail`**
Sends mail to a bidder who has been outbid, refunding their previous bid.
1.  Resolves the old bidder's `Player` object or Account ID via `ObjectMgr/GetPlayer` and `ObjectMgr/GetPlayerAccountIdByGUID`.
2.  If the bidder is online, calls `SendAuctionBidderNotification` with `won=false`.
3.  Creates a `MailDraft` with the refund amount (`auction->bid`) and sends it to the bidder via `game_Mail_Mail/SendMailTo`.

**`SendAuctionCancelledToBidderMail`**
Sends mail to a bidder whose auction was cancelled by the owner, refunding their bid.
1.  Resolves the bidder's `Player` object or Account ID.
2.  If the bidder is online, calls `SendAuctionRemovedNotification`.
3.  Creates a `MailDraft` with the refund amount and sends it to the bidder.

### Core Auction Operations

**`HandleAuctionSellItem`**
Processes a request to create a new auction. This method performs extensive validation and state management.
1.  **Basic Validation:** Checks for zero bid/time. Validates bid/buyout amounts against a hard cap (2 billion) and ensures bid <= buyout. Logs anticheat actions for violations.
2.  **Permissions:** Checks GM trade restrictions and Trial account restrictions.
3.  **Auctioneer Validation:** Uses `GetCheckedAuctionHouseForAuctioneer` to verify the target.
4.  **Limits:** Checks the account's concurrent auction limit via `AuctionHouseMgr/GetAccountAuctionCount`.
5.  **Duration:** Validates the requested time against standard durations (1, 4, 12 hours).
6.  **Item Validation:**
    *   Ensures the item exists in the player's inventory.
    *   Prevents selling items already in an auction.
    *   Prevents selling items in bank slots.
    *   Checks if the item is tradable (`game_Objects_Item/CanBeTraded`).
    *   Prevents selling conjured or timed items.
7.  **Financials:** Calculates the deposit via `AuctionHouseMgr/GetAuctionDeposit`. Deducts the deposit from the player's money.
8.  **Logging:** Logs GM trades and general transaction data via `World/LogTransaction`.
9.  **Creation:**
    *   Generates a new `AuctionEntry`.
    *   Sets all fields (ID, item GUID, owner, prices, times, IP address).
    *   Adds the auction to the `AuctionHouseObject` map.
    *   Moves the item from the player's inventory to the auction house storage (`sAuctionMgr.AddAItem`).
10. **Persistence:** Begins a database transaction. Deletes the item from the inventory DB, saves the item to the auction DB, saves the auction entry to the `auction` table, and saves the player's inventory/gold. Commits the transaction.
11. **Response:** Sends `SendAuctionCommandResult` with success.

**`HandleAuctionPlaceBid`**
Processes a bid or buyout on an existing auction.
1.  **Permissions:** Checks GM trade and Trial restrictions.
2.  **Validation:** Ensures auction ID and price are non-zero. Validates the auctioneer.
3.  **Auction Retrieval:** Gets the `AuctionEntry` from the map. If not found, returns error.
4.  **Ownership Checks:** Prevents bidding on one's own auction or auctions owned by other characters on the same account.
5.  **Bid Logic:**
    *   Ensures the bid is >= start bid.
    *   Ensures the bid is > current highest bid.
    *   Ensures the bid meets the minimum increment (`auction->bid + auction->GetAuctionOutBid()`), unless buying out.
    *   Checks if the player has enough money.
6.  **Execution:**
    *   **Bid (not buyout):**
        *   Deducts the bid amount (or difference if re-bidding).
        *   If there was a previous bidder, calls `SendAuctionOutbiddedMail` to refund them.
        *   Updates the auction's bidder and bid fields.
        *   Notifies the owner via `SendAuctionOwnerNotification` if online.
        *   Updates the `auction` table in the database directly via `Database/PExecute`.
        *   Sends success result.
    *   **Buyout:**
        *   Deducts the buyout amount.
        *   Refunds previous bidder if applicable.
        *   Updates auction fields.
        *   Logs the transaction.
        *   Calls `AuctionHouseMgr/SendAuctionSuccessfulMail` and `AuctionHouseMgr/SendAuctionWonMail` to handle item transfer and payment to the owner.
        *   Removes the item from auction storage and the auction from the map.
        *   Deletes the auction from the DB.
        *   Deletes the `AuctionEntry` object.
        *   Sends success result.
7.  **Persistence:** Saves the bidder's inventory and gold to the DB within a transaction.

**`HandleAuctionRemoveItem`**
Allows an owner to cancel their auction.
1.  **Validation:** Validates the auctioneer.
2.  **Retrieval:** Gets the auction entry.
3.  **Ownership Check:** Ensures the player is the owner.
4.  **Item Retrieval:** Gets the item from auction storage.
5.  **Refund Logic:**
    *   If there is a bidder, calculates the auction cut (`AuctionHouseMgr/GetAuctionCut`).
    *   Checks if the owner has enough money to pay the cut. If not, it returns silently without cancelling the auction or sending an error message.
    *   Calls `SendAuctionCancelledToBidderMail` to refund the bidder.
    *   Deducts the cut from the owner's money.
6.  **Item Return:** Creates a mail draft to send the item back to the owner.
7.  **Cleanup:**
    *   Sends success result.
    *   Deletes the auction from the DB.
    *   Saves the owner's inventory/gold.
    *   Removes the item from auction storage.
    *   Removes the auction from the map.
    *   Deletes the `AuctionEntry` object.

### Listing Queries

These methods handle requests to view auction lists. To prevent server lag, they offload heavy database queries and packet construction to an asynchronous task.

**`AuctionHouseClientQueryTask`**
Constructor for the async task, storing the query type.

**`operator()`**
The execution body of the async task.
1.  Finds the player's session via `World/FindSession`. If the session is gone or the player is not in the world, it aborts.
2.  Clears the "received AH list request" flag.
3.  Initializes a `WorldPacket`.
4.  Executes the query based on `_queryType`:
    *   `AUCTION_QUERY_LIST`: Calls `AuctionHouseMgr/BuildListAuctionItems` to fill the packet with all matching auctions.
    *   `AUCTION_QUERY_LIST_BIDDER`: Iterates through outbid auctions and calls `AuctionHouseMgr/BuildListBidderItems`.
    *   `AUCTION_QUERY_LIST_OWNER`: Calls `AuctionHouseMgr/BuildListOwnerItems`.
5.  Updates the packet with the count of items returned.
6.  Sends the packet to the client.

**`HandleAuctionListBidderItems`**
Handles the request to list auctions the player has bid on.
1.  Checks if a request is already pending (`ReceivedAHListRequest`). If so, ignores.
2.  Validates the auctioneer.
3.  Creates an `AuctionHouseClientQueryTask` of type `AUCTION_QUERY_LIST_BIDDER`.
4.  Populates the task with the auction house map, account ID, paging index, and list of outbid auction IDs to refresh.
5.  Sets the pending flag and adds the task to the world's async queue via `World/AddAsyncTask`.

**`HandleAuctionListOwnerItems`**
Handles the request to list auctions the player owns.
1.  Checks for pending requests.
2.  Validates the auctioneer.
3.  Creates an `AuctionHouseClientQueryTask` of type `AUCTION_QUERY_LIST_OWNER`.
4.  Populates the task and adds it to the async queue.

**`HandleAuctionListItems`**
Handles the request to list all auctions in the house, filtered by search criteria.
1.  Checks for pending requests.
2.  Creates an `AuctionHouseClientQueryTask` of type `AUCTION_QUERY_LIST`.
3.  Populates the task with filtering criteria (level, category, quality, usable, search string).
4.  Converts the search string to wide characters and lowercase.
5.  Validates the auctioneer and populates the auction house map.
6.  Sets the pending flag and adds the task to the async queue.

## Cross-Unit Boundaries

*   **`AuctionHouseMgr`**: The central authority for auction data. This unit calls `AuctionHouseMgr` extensively to:
    *   Retrieve `AuctionHouseEntry` and `AuctionEntry` objects.
    *   Calculate deposits, cuts, and outbid amounts.
    *   Add/remove auctions and items from memory maps.
    *   Save/Load auction data to/from the database (`SaveToDB`, `DeleteFromDB`).
    *   Build packets for listing results (`BuildList...`).
    *   Send mail for successful/expired auctions (`SendAuctionSuccessfulMail`, `SendAuctionWonMail`, `SendAuctionExpiredMail`).
*   **`Player`**: Represents the acting user. This unit calls `Player` to:
    *   Get/Modify money.
    *   Access inventory items (`GetItemByGuid`, `MoveItemFromInventory`).
    *   Check permissions (`GetSecurity`, `HasTrialRestrictions`).
    *   Save inventory/gold to DB.
    *   Interact with NPCs (`GetNPCIfCanInteractWith`).
*   **`ObjectMgr`**: Used to resolve player GUIDs to `Player` objects or Account IDs for mail delivery.
*   **`World`**: Used to add asynchronous tasks (`AddAsyncTask`) and find sessions (`FindSession`). Also used for configuration (`getConfig`) and logging (`LogTransaction`).
*   **`Database`**: Direct SQL execution (`PExecute`) is used in `HandleAuctionPlaceBid` to update the `auction` table immediately upon a bid. Transactions (`BeginTransaction`, `CommitTransaction`) are used in `HandleAuctionSellItem`, `HandleAuctionPlaceBid`, and `HandleAuctionRemoveItem` to ensure consistency.
*   **`Mail`**: `MailDraft` and `game_Mail_Mail` functions are used to send refunds and items via in-game mail.
*   **`ChatHandler`**: Used in `GetCheckedAuctionHouseForAuctioneer` to verify GM command permissions.
*   **`Log`**: Used for debugging and error reporting.

## Data Model

This unit interacts with the `auction` table in the database.

**Table: `auction`**
*   **Usage:** Stores the state of active auctions.
*   **Columns Accessed:**
    *   `id`: Primary key, used to identify auctions in packets and lookups.
    *   `house_id`: Identifies the faction/auction house.
    *   `item_guid`: Links to the item being sold.
    *   `seller_guid`: The owner of the auction.
    *   `buyer_guid`: The current highest bidder. Updated via direct SQL in `HandleAuctionPlaceBid`.
    *   `last_bid`: The current highest bid amount. Updated via direct SQL in `HandleAuctionPlaceBid`.
    *   `start_bid`: The initial bid required.
    *   `buyout_price`: The instant purchase price.
    *   `deposit`: The amount held from the seller.
    *   `expire_time`: When the auction ends.
*   **Operations:**
    *   `INSERT`: Performed by `AuctionEntry::SaveToDB` (called in `HandleAuctionSellItem`).
    *   `UPDATE`: Performed by `Database/PExecute` in `HandleAuctionPlaceBid` to update `buyer_guid` and `last_bid`.
    *   `DELETE`: Performed by `AuctionEntry::DeleteFromDB` (called in `HandleAuctionPlaceBid` on buyout and `HandleAuctionRemoveItem`).

## Notable Implementation Details

1.  **Direct SQL Update in Bidding:** In `HandleAuctionPlaceBid`, the `auction` table is updated directly using `Database/PExecute` ("UPDATE `auction` SET `buyer_guid` = ...") *before* the transaction block that saves the player's gold. This means if the subsequent transaction fails (e.g., DB connection drop), the auction state in the DB might reflect a bid that didn't actually deduct money, leading to potential desync or loss. Most other operations wrap DB changes in transactions.
2.  **Anticheat Logging:** `HandleAuctionSellItem` explicitly checks for bid/buyout values exceeding 2 billion and logs "GoldDupe" anticheat actions if violated. It also checks if `bid > buyout`.
3.  **Feign Death Removal:** Multiple handlers (`HandleAuctionHelloOpcode`, `HandleAuctionSellItem`, `HandleAuctionPlaceBid`, `HandleAuctionRemoveItem`, `HandleAuctionList...`) check for and remove the `SPELL_AURA_FEIGN_DEATH` aura. This allows players to interact with auctioneers while pretending to be dead, which is generally intended behavior for convenience.
4.  **Async Listing:** The listing handlers (`HandleAuctionList...`) do not perform queries synchronously. They create `AuctionHouseClientQueryTask` objects and push them to `World/AddAsyncTask`. This prevents the main game loop from freezing during large database scans. The task checks if the session still exists before sending the response, handling disconnects gracefully.
5.  **Pending Request Flag:** `WorldSession` has a boolean `m_ah_list_recvd` (accessed via `ReceivedAHListRequest`/`SetReceivedAHListRequest`). All listing handlers check this flag and return early if a request is already pending. This prevents spamming the server with multiple concurrent listing queries from the same client.
6.  **GM Auctioneer:** `GetCheckedAuctionHouseForAuctioneer` allows GMs to use themselves as the auctioneer if they have the `auction` command permission. This enables GMs to open auction windows without needing an NPC nearby.
7.  **Cancellation Cut:** In `HandleAuctionRemoveItem`, if there is a bidder, the owner must pay an "auction cut" to cancel. The code checks if the owner has enough money for this cut. If not, it returns silently without cancelling the auction or sending any error message to the client, which could be confusing.
8.  **Item Random Property:** Notification packets (`SendAuctionBidderNotification`, `SendAuctionOwnerNotification`, `SendAuctionRemovedNotification`) include the item's `GetItemRandomPropertyId()`. This is crucial for displaying correct suffixes/stats for magical items in the client UI.

## Member Reference

**HandleAuctionHelloOpcode**: Handles the client packet when a player clicks an Auctioneer NPC. Validates the NPC, removes Feign Death aura if present, and calls `SendAuctionHello`.

**SendAuctionHello**: Constructs and sends the `MSG_AUCTION_HELLO` packet to open the auction window, providing the NPC GUID and Auction House ID. Called by chat commands and gossip selects.

**SendAuctionCommandResult**: Sends the result of an auction action (create, bid, remove) to the client, including error codes and additional context like new bidder info or inventory errors.

**SendAuctionBidderNotification**: Sends a real-time notification to an online bidder indicating they have won or been outbid, including item details and random property IDs. Called by `AuctionHouseMgr/SendAuctionWonMail`.

**SendAuctionOwnerNotification**: Sends a real-time notification to an auction owner about new bids, expirations, or sales, including bidder GUID and item details. Called by `AuctionHouseMgr/SendAuctionExpiredMail` and `AuctionHouseMgr/SendAuctionSuccessfulMail`.

**SendAuctionRemovedNotification**: Sends a notification to a player that an auction they were involved in has been removed or cancelled, including item details.

**SendAuctionOutbiddedMail**: Sends in-game mail to a bidder who has been outbid, refunding their previous bid amount. Also triggers a real-time notification if the bidder is online.

**SendAuctionCancelledToBidderMail**: Sends in-game mail to a bidder whose auction was cancelled by the owner, refunding their bid amount. Also triggers a removal notification if the bidder is online.

**GetCheckedAuctionHouseForAuctioneer**: Validates the target of an auction interaction. For GMs, it checks command permissions; for players, it validates the NPC. Returns the `AuctionHouseEntry` for the valid target.

**HandleAuctionSellItem**: Processes the creation of a new auction. Validates item, price, and permissions; calculates deposit; moves item from inventory to auction storage; persists changes to the database; and sends a success result.

**HandleAuctionPlaceBid**: Processes a bid or buyout. Validates ownership and bid increments; handles money transfers; refunds previous bidders via mail; updates the database; and finalizes the sale if bought out.

**HandleAuctionRemoveItem**: Allows an owner to cancel an auction. Calculates cancellation fees, refunds bidders via mail, returns the item to the owner via mail, and cleans up database records.

**AuctionHouseClientQueryTask**: Constructor for the asynchronous task used to handle auction list queries. Stores the query type.

**operator()**: Executes the asynchronous auction list query. Finds the player's session, builds the appropriate packet based on the query type (list, bidder, owner), and sends the result to the client.

**HandleAuctionListBidderItems**: Initiates an asynchronous task to list auctions the player has bid on. Checks for pending requests and validates the auctioneer before queuing the task.

**HandleAuctionListOwnerItems**: Initiates an asynchronous task to list auctions owned by the player. Checks for pending requests and validates the auctioneer before queuing the task.

**HandleAuctionListItems**: Initiates an asynchronous task to list all auctions in the house, filtered by search criteria. Converts search strings to lowercase and queues the task after validating the auctioneer.

---

<!-- machine-true, projected from graph.json -->

## Map — WorldSession.AuctionHouseHandler

*Source:* AuctionHouseHandler.cpp, WorldSession.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| HandleAuctionHelloOpcode | method | Log.Main/Out, ObjectGuid/GetString, Player.Main/GetNPCIfCanInteractWith, Unit.Main/HasUnitState, Unit.Main/RemoveSpellsCausingAura, WorldSession.Main/GetPlayer | — | — |
| SendAuctionHello | method | AuctionHouseMgr/GetAuctionHouseEntry, ByteBuffer/operator<<#10, Object/GetObjectGuid, ObjectGuid/operator<<, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | ChatHandler.MiscCommands/HandleAuctionAllianceCommand, ChatHandler.MiscCommands/HandleAuctionCommand, ChatHandler.MiscCommands/HandleAuctionGoblinCommand, ChatHandler.MiscCommands/HandleAuctionHordeCommand, Player.Main/OnGossipSelect | — |
| SendAuctionCommandResult | method | AuctionHouseMgr/GetAuctionOutBid, ByteBuffer/operator<<#10, ObjectGuid/ObjectGuid#2, ObjectGuid/operator<<, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | — | — |
| SendAuctionBidderNotification | method | AuctionEntry/GetHouseId, AuctionHouseMgr/GetAItem, AuctionHouseMgr/GetAuctionOutBid, ByteBuffer/operator<<#10, game_Objects_Item/GetItemRandomPropertyId, ObjectGuid/ObjectGuid#2, ObjectGuid/operator<<, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | AuctionHouseMgr/SendAuctionWonMail | — |
| SendAuctionOwnerNotification | method | AuctionHouseMgr/GetAItem, AuctionHouseMgr/GetAuctionOutBid, ByteBuffer/operator<<#10, game_Objects_Item/GetItemRandomPropertyId, ObjectGuid/ObjectGuid, ObjectGuid/ObjectGuid#2, ObjectGuid/operator<<, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | AuctionHouseMgr/SendAuctionExpiredMail, AuctionHouseMgr/SendAuctionSuccessfulMail | — |
| SendAuctionRemovedNotification | method | AuctionHouseMgr/GetAItem, ByteBuffer/operator<<#10, game_Objects_Item/GetItemRandomPropertyId, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | — | — |
| SendAuctionOutbiddedMail | method | game_Mail_Mail/MailReceiver#2, game_Mail_Mail/MailSender#3, game_Mail_Mail/SendMailTo, MailDraft/MailDraft#2, MailDraft/SetMoney, ObjectGuid/ObjectGuid#2, ObjectMgr/GetPlayer, ObjectMgr/GetPlayerAccountIdByGUID, Player.Main/GetSession | — | — |
| SendAuctionCancelledToBidderMail | method | game_Mail_Mail/MailReceiver#2, game_Mail_Mail/MailSender#3, game_Mail_Mail/SendMailTo, MailDraft/MailDraft#2, MailDraft/SetMoney, ObjectGuid/ObjectGuid#2, ObjectMgr/GetPlayer, ObjectMgr/GetPlayerAccountIdByGUID, Player.Main/GetSession | — | — |
| GetCheckedAuctionHouseForAuctioneer | method | AuctionHouseMgr/GetAuctionHouseEntry, ChatHandler.Chat/ChatHandler#2, ChatHandler.Chat/FindCommand#2, Log.Main/Out, Object/GetObjectGuid, ObjectGuid/GetString, ObjectGuid/operator==, Player.Main/GetAuctionAccessMode, Player.Main/GetNPCIfCanInteractWith, WorldSession.Main/GetPlayer | — | — |
| HandleAuctionSellItem | method | AuctionEntry/GetHouseId, AuctionHouseMgr/AddAItem, AuctionHouseMgr/AddAuction, AuctionHouseMgr/GetAccountAuctionCount, AuctionHouseMgr/GetAItem, AuctionHouseMgr/GetAuctionDeposit, AuctionHouseMgr/GetAuctionsMap, AuctionHouseMgr/SaveToDB, Database/BeginTransaction, Database/CommitTransaction, game_Objects_Item/CanBeTraded, game_Objects_Item/DeleteFromInventoryDB, game_Objects_Item/GetBagSlot, game_Objects_Item/GetCount, game_Objects_Item/GetPos, game_Objects_Item/GetProto, game_Objects_Item/GetSlot, game_Objects_Item/SaveToDB, Log.Main/Out, Object/GetEntry, Object/GetGUIDLow, Object/GetGuidStr, Object/GetObjectGuid, Object/GetUInt32Value, ObjectGuid/GetCounter, ObjectGuid/GetString, ObjectGuid/operator!, ObjectMgr/GenerateAuctionID, Player.Main/GetItemByGuid, Player.Main/GetMoney, Player.Main/GetSession, Player.Main/GetShortDescription, Player.Main/IsBankPos#2, Player.Main/ModifyMoney, Player.Main/MoveItemFromInventory, Player.Main/Player, Player.Main/Player#3, Player.Main/SaveInventoryAndGoldToDB, Player.Main/SendSysMessage, Unit.Main/HasUnitState, Unit.Main/RemoveSpellsCausingAura, World/getConfig, World/getConfig#2, World/getConfig#4, World/LogTransaction, WorldSession.Main/GetAccountId, WorldSession.Main/GetPlayer, WorldSession.Main/GetPlayerName, WorldSession.Main/GetRemoteAddress, WorldSession.Main/GetSecurity, WorldSession.Main/HasTrialRestrictions, WorldSession.Main/ProcessAnticheatAction | — | — |
| HandleAuctionPlaceBid | method | AuctionHouseMgr/DeleteFromDB, AuctionHouseMgr/GetAItem, AuctionHouseMgr/GetAuction, AuctionHouseMgr/GetAuctionOutBid, AuctionHouseMgr/GetAuctionsMap, AuctionHouseMgr/RemoveAItem, AuctionHouseMgr/RemoveAuction, AuctionHouseMgr/SendAuctionSuccessfulMail, AuctionHouseMgr/SendAuctionWonMail, Database/BeginTransaction, Database/CommitTransaction, Database/PExecute#2, game_Objects_Item/GetCount, Object/GetGUIDLow, ObjectGuid/ObjectGuid#2, ObjectMgr/GetPlayer, ObjectMgr/GetPlayerAccountIdByGUID, Player.Main/GetMoney, Player.Main/GetSession, Player.Main/LogModifyMoney, Player.Main/SaveInventoryAndGoldToDB, Unit.Main/HasUnitState, Unit.Main/RemoveSpellsCausingAura, World/getConfig, World/LogTransaction, WorldSession.Main/GetAccountId, WorldSession.Main/GetPlayer, WorldSession.Main/GetSecurity, WorldSession.Main/HasTrialRestrictions | — | auction |
| HandleAuctionRemoveItem | method | AuctionHouseMgr/DeleteFromDB, AuctionHouseMgr/GetAItem, AuctionHouseMgr/GetAuction, AuctionHouseMgr/GetAuctionCut, AuctionHouseMgr/GetAuctionsMap, AuctionHouseMgr/RemoveAItem, AuctionHouseMgr/RemoveAuction, Database/BeginTransaction, Database/CommitTransaction, game_Mail_Mail/AddItem, game_Mail_Mail/MailReceiver, game_Mail_Mail/MailSender#3, game_Mail_Mail/SendMailTo, Log.Main/Out, MailDraft/MailDraft#2, Object/GetGUIDLow, Player.Main/GetMoney, Player.Main/ModifyMoney, Player.Main/SaveInventoryAndGoldToDB, Unit.Main/HasUnitState, Unit.Main/RemoveSpellsCausingAura, WorldSession.Main/GetPlayer | — | — |
| AuctionHouseClientQueryTask | ctor | — | — | — |
| operator() | method | AuctionHouseMgr/BuildAuctionInfo, AuctionHouseMgr/BuildListAuctionItems, AuctionHouseMgr/BuildListBidderItems, AuctionHouseMgr/BuildListOwnerItems, AuctionHouseMgr/GetAuction, ByteBuffer/operator<<#10, ByteBuffer/wpos, Log.Main/Out, Object/IsInWorld, World/FindSession, WorldPacket/SetOpcode, WorldPacket/WorldPacket#4, WorldSession.Main/GetPlayer, WorldSession.Main/SendPacket, WorldSession.Main/SetReceivedAHListRequest | — | — |
| HandleAuctionListBidderItems | method | AuctionHouseMgr/GetAuctionsMap, Unit.Main/HasUnitState, Unit.Main/RemoveSpellsCausingAura, World/AddAsyncTask, WorldSession.Main/GetAccountId, WorldSession.Main/GetPlayer, WorldSession.Main/ReceivedAHListRequest, WorldSession.Main/SetReceivedAHListRequest | — | — |
| HandleAuctionListOwnerItems | method | AuctionHouseMgr/GetAuctionsMap, Unit.Main/HasUnitState, Unit.Main/RemoveSpellsCausingAura, World/AddAsyncTask, WorldSession.Main/GetAccountId, WorldSession.Main/GetPlayer, WorldSession.Main/ReceivedAHListRequest, WorldSession.Main/SetReceivedAHListRequest | — | — |
| HandleAuctionListItems | method | AuctionHouseMgr/GetAuctionsMap, shared_Util/Utf8toWStr, shared_Util/wstrToLower, Unit.Main/HasUnitState, Unit.Main/RemoveSpellsCausingAura, World/AddAsyncTask, WorldSession.Main/GetAccountId, WorldSession.Main/GetPlayer, WorldSession.Main/ReceivedAHListRequest, WorldSession.Main/SetReceivedAHListRequest | — | — |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `auction`: id int(11) unsigned PK, house_id int(11) unsigned, item_guid int(11) unsigned, item_id int(11) unsigned, seller_guid int(11) unsigned, buyout_price int(11), expire_time bigint(40), buyer_guid int(11) unsigned, last_bid int(11), start_bid int(11), deposit int(11)

*`?` = nullable, `PK` = primary key column.*


---

<!-- verify: failed-members | invented: operator -->

---

<!-- verify: boundary-bleed | foreign: update, WorldSession -->
