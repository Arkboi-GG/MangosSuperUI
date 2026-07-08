# AuctionHouseMgr

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# AuctionHouseMgr

**Purpose & Responsibilities**

`AuctionHouseMgr` is the singleton manager responsible for the entire lifecycle of the Auction House system in the server. It handles the creation, persistence, querying, and expiration of auctions. Its core responsibilities include:

1.  **State Management:** Maintaining in-memory maps of active auctions (`AuctionHouseObject`) and the items currently listed on them (`mAitems`).
2.  **Persistence:** Loading auctions and items from the `auction` and `item_instance` database tables on startup, and saving/removing records during auction creation, bidding, and expiration.
3.  **Auction Logic:** Calculating deposits, determining winning bids, handling auction expiration (sending items/money via mail), and enforcing auction house rules (e.g., faction separation, cross-faction interactions).
4.  **Network Communication:** Building packet data for clients to view auction listings, owner items, and bidder items.
5.  **Configuration Handling:** Adapting behavior based on world configuration flags such as `ALLOW_TWO_SIDE_INTERACTION_AUCTION` (cross-faction AH) and `UNLINKED_AUCTION_HOUSES`.

The class is split into two main components:
*   `AuctionHouseMgr`: The global singleton that manages the collection of auction houses and the global pool of auction items.
*   `AuctionHouseObject`: A helper class representing a single logical Auction House (e.g., "Alliance AH", "Horde AH", or a specific city's AH). It holds the actual list of active auctions for that house.

## Member-by-Member Behavior

### Initialization and Lifecycle

*   **`AuctionHouseMgr()` / `~AuctionHouseMgr()`**: The constructor is empty. The destructor iterates over `mAitems` (the map of items currently in auctions) and deletes them. Note that `AuctionHouseObject` instances are stored in `m_vRealAuctionHouses` as `std::unique_ptr`, so they are automatically cleaned up when the singleton is destroyed.
*   **`LoadAuctionHouses()`**: Called during world startup. It populates `m_mAuctionHouses` based on configuration:
    *   If `CONFIG_BOOL_ALLOW_TWO_SIDE_INTERACTION_AUCTION` is true, all DBC auction house entries point to a single shared `AuctionHouseObject`.
    *   If `CONFIG_BOOL_UNLINKED_AUCTION_HOUSES` is true (and patch < 1.9), each DBC entry gets its own unique `AuctionHouseObject`.
    *   Otherwise, it creates three objects (Alliance, Horde, Neutral) and maps DBC entries to them based on `GetAuctionHouseTeam()`.
*   **`LoadAuctionItems()`**: Loads item data from the `item_instance` table joined with `auction`. It reconstructs `Item` objects for every item currently in an auction and adds them to `mAitems` via `AddAItem()`. If an item prototype is missing, it logs an error and skips the item.
*   **`LoadAuctions()`**: Loads auction metadata from the `auction` table. For each row:
    *   It checks if the corresponding item exists in `mAitems` (loaded previously). If not, it deletes the auction record from the DB and logs an error.
    *   It checks if the auction house entry exists in the DBC. If not, it attempts to refund the item to the owner via mail and deletes the auction.
    *   It adds the valid auction to the appropriate `AuctionHouseObject` via `GetAuctionsMap()->AddAuction()`.

### Auction Creation and Removal

*   **`AddAuction()`** (in `AuctionHouseObject`): Adds an `AuctionEntry` to three internal maps:
    1.  `AuctionsMap`: Keyed by Auction ID for fast lookup.
    2.  `OrderedAuctionMap`: A multimap keyed by `buyout` price, allowing efficient retrieval of auctions sorted by buyout price.
    3.  `AccountAuctionMap`: A multimap keyed by `ownerAccount`, allowing efficient retrieval of all auctions owned by a specific account.
*   **`RemoveAuction()`** (in `AuctionHouseObject`): Removes an `AuctionEntry` from all three maps. It uses `equal_range` to find the correct entry in the multimaps (since multiple auctions can have the same buyout or belong to the same account) and erases only the matching one. Finally, it frees the auction ID via `sObjectMgr.FreeAuctionID()`.
*   **`AddAItem()` / `RemoveAItem()`** (in `AuctionHouseMgr`): Manages the global `mAitems` map. `AddAItem` inserts an item by its GUID low. `RemoveAItem` removes it. These are used to track items currently "in transit" or "on auction" so they don't appear in player inventories.

### Auction Expiration and Updates

*   **`Update()`** (in `AuctionHouseMgr`): Iterates over all `AuctionHouseObject` instances and calls their `Update()` method.
*   **`Update()`** (in `AuctionHouseObject`): Iterates through `AuctionsMap`. For each auction:
    *   It clears the `lockedIpAddress` if 5 minutes have passed since the deposit (preventing auction sniping).
    *   If the current time exceeds `expireTime`:
        *   If no bidder (`bidder == 0`), it calls `SendAuctionExpiredMail()` to return the item to the owner.
        *   If there is a bidder, it logs the transaction, then calls `SendAuctionSuccessfulMail()` (to pay the seller) and `SendAuctionWonMail()` (to give the item to the buyer).
        *   It deletes the auction from the DB, removes the item from `mAitems`, removes the auction from memory, and deletes the `AuctionEntry` object.

### Mail and Notifications

*   **`SendAuctionWonMail()`**: Sends the item to the winning bidder.
    *   It retrieves the item from `mAitems`.
    *   If GM logging is enabled, it logs the trade details.
    *   It updates the `owner_guid` in the `item_instance` table to the bidder's GUID.
    *   If the bidder is online, it sends a client notification.
    *   It sends a mail containing the item. If the bidder doesn't exist, it deletes the item from the DB and memory.
*   **`SendAuctionSuccessfulMail()`**: Sends the profit (bid + deposit - cut) to the seller.
    *   It calculates the profit and sends a mail with money.
    *   If the seller is online, it sends a client notification.
*   **`SendAuctionExpiredMail()`**: Returns the item to the owner if the auction expires with no bids.
    *   Similar to `SendAuctionWonMail`, but sends the item back to the owner. If the owner doesn't exist, it deletes the item.

### Querying and Listing

*   **`GetAuctionsMap()`**: Returns the `AuctionHouseObject` for a given DBC `AuctionHouseEntry`.
*   **`GetAItem()`**: Retrieves an `Item` from `mAitems` by its GUID low.
*   **`BuildListBidderItems()`**: Builds a packet listing all auctions the player has bid on. It iterates `AuctionsMap` and filters by `bidder == player->GetGUIDLow()`.
*   **`BuildListOwnerItems()`**: Builds a packet listing all auctions the player owns. It uses `AccountAuctionMap` for efficiency, filtering by `owner == player->GetGUIDLow()`.
*   **`BuildListAuctionItems()`**: Builds a packet listing general auction items based on a query (category, level, search term, etc.).
    *   If the query is empty (all categories), it iterates `OrderedAuctionMap` (sorted by buyout) and builds info for up to 50 items.
    *   Otherwise, it iterates `OrderedAuctionMap` and applies filters (class, subclass, slot, quality, level, usability, search name). It uses `Utf8FitTo` for name matching.
*   **`BuildAuctionInfo()`**: Serializes an `AuctionEntry` and its associated `Item` into a `WorldPacket`. It includes item details (enchantments, random properties, count, charges), owner/bidder GUIDs, prices, and time remaining.

### Helper Functions

*   **`GetAuctionDeposit()`**: Calculates the deposit required to list an item. Formula: `(ItemSellPrice * Count * (Time / MIN_AUCTION_TIME)) * DepositPercent * RateAuctionDeposit`. It enforces a minimum deposit configured in `CONFIG_UINT32_AUCTION_DEPOSIT_MIN`.
*   **`GetAuctionHouseTeam()`**: Determines the faction (Alliance/Horde/Neutral) of an auction house based on its DBC ID.
*   **`GetAuctionHouseId()`**: Maps a faction template ID to an auction house ID. It has hardcoded mappings for common races/factions and falls back to checking the faction mask.
*   **`GetAuctionHouseEntry()`**: Overloaded methods to get the DBC `AuctionHouseEntry` for a `Unit` or a faction ID. It respects the `ALLOW_TWO_SIDE_INTERACTION_AUCTION` config.
*   **`GetAuctionCut()`**: Calculates the auction house cut: `bid * cutPercent * RateAuctionCut / 100`.
*   **`GetAuctionOutBid()`**: Calculates the minimum increment needed to outbid: `(bid / 100) * 5`, with a minimum of 1 copper.
*   **`IsAvailableFor()`**: Checks if an auction is available to a player. If the auction is IP-locked (sniping protection), it returns true only if the player's IP matches the locked IP.

## Cross-Unit Boundaries

*   **`WorldSession.AuctionHouseHandler`**: The primary caller for most methods. It handles client packets for placing bids, selling items, removing items, and listing auctions. It calls `AddAuction`, `RemoveAuction`, `GetAuctionsMap`, `SendAuctionWonMail`, etc.
*   **`ObjectMgr`**: Called for freeing auction IDs (`FreeAuctionID`), getting player names/accounts (`GetPlayerNameByGUID`, `GetPlayerAccountIdByGUID`), and retrieving item prototypes (`GetItemPrototype`).
*   **`Database`**: Used for executing SQL queries (`PExecute`, `Query`) to save/delete auctions and update item ownership.
*   **`game_Mail_Mail`**: Used to send mails (`SendMailTo`, `MailDraft`, `AddItem`) for auction wins, successes, and expirations.
*   **`game_Objects_Item`**: Called to get item properties (`GetProto`, `GetCount`, `GetEnchantmentId`, etc.) and to load items from the DB (`LoadFromDB`).
*   **`Player.Main`**: Called to get player sessions, names, teams, and to check if a player can use an item (`CanUseItem`).
*   **`World`**: Called to get configuration values (`getConfig`) and game time (`GetGameTime`).
*   **`ChatHandler.AuctionHouseBotMgr`**: Used for bot-related operations like adding items to auctions.
*   **`Errors`**: `PrintStacktraceAndThrow` is called in `AddAuction` and `AddAItem` if assertions fail (though these are likely debug-only).
*   **`Log.Main`**: Used for logging errors, warnings, and GM trades.
*   **`AccountMgr`**: Used to get account security levels for GM logging.
*   **`SpellMgr`**: Used in `BuildListAuctionItems` to check if a player already knows a recipe spell.
*   **`shared_Util`**: `Utf8FitTo` is used for searching item names.

## Data Model

The unit interacts with two database tables:

1.  **`auction`**:
    *   Stores the core auction data: ID, house ID, item GUID, item ID, seller/buyer GUIDs, prices (start, bid, buyout, deposit), and expiration time.
    *   Used by `LoadAuctions`, `SaveToDB`, `DeleteFromDB`.
2.  **`item_instance`**:
    *   Stores the specific instance data of items (owner, count, enchantments, durability, etc.).
    *   Joined with `auction` in `LoadAuctionItems` to reconstruct items.
    *   Updated in `SendAuctionWonMail` to change the `owner_guid` to the bidder.
    *   Deleted in `SendAuctionWonMail` and `SendAuctionExpiredMail` if the recipient doesn't exist.

## Notable Implementation Details

*   **IP Locking for Sniping Prevention**: In `AuctionHouseObject::Update()`, if an auction has been bid on, the bidder's IP is locked for 5 minutes (`entry->lockedIpAddress`). During this time, only that IP can place further bids. This is enforced in `IsAvailableFor()`.
*   **Memory Management**: `AuctionHouseMgr` owns `mAitems` (raw pointers) and deletes them in the destructor. `AuctionHouseObject` owns `AuctionsMap` (raw pointers) and deletes them in its destructor. `AuctionHouseMgr` owns `m_vRealAuctionHouses` (unique_ptrs). Care is taken to remove items from `mAitems` before deleting them to avoid double-free or dangling pointers.
*   **Cross-Faction Auctions**: The `LoadAuctionHouses()` method supports three modes: linked (faction-based), unlinked (per-city), and cross-faction (single shared AH). This is controlled by world config flags.
*   **Efficient Listing**: `OrderedAuctionMap` allows `BuildListAuctionItems` to return items sorted by buyout price without sorting on every query. `AccountAuctionMap` allows efficient retrieval of owner items.
*   **Mail Handling**: If the recipient of an auction mail (winner or owner) doesn't exist in the database, the item is deleted from the `item_instance` table and memory. This prevents orphaned items.
*   **GM Logging**: If `CONFIG_BOOL_GM_LOG_TRADE` is enabled, `SendAuctionWonMail` logs detailed trade information for GMs, including account IDs and security levels.
*   **Fallback Auction House**: If an auction's DBC entry is invalid during loading, it defaults to the Goblin Auction House (ID 7) for sending refund mails.

## Member Reference

*   **`RemoveAuction`**: Removes an auction from all internal maps in `AuctionHouseObject` and frees its ID.
*   **`AddAuction`**: Adds an auction to `AuctionsMap`, `OrderedAuctionMap`, and `AccountAuctionMap` in `AuctionHouseObject`.
*   **`AuctionHouseMgr`**: Constructor, initializes nothing explicitly.
*   **`~AuctionHouseMgr`**: Destructor, deletes all items in `mAitems`.
*   **`GetAuctionsMap`**: Returns the `AuctionHouseObject` for a given DBC entry.
*   **`GetAuctionDeposit`**: Calculates the deposit required to list an item based on price, time, and config.
*   **`AuctionHouseObject`**: Constructor, initializes nothing explicitly.
*   **`~AuctionHouseObject`**: Destructor, deletes all `AuctionEntry` objects in `AuctionsMap`.
*   **`SendAuctionWonMail`**: Sends the item to the winning bidder via mail, updating DB ownership.
*   **`GetCount`**: Returns the number of auctions in an `AuctionHouseObject`.
*   **`GetAuctions`**: Returns a pointer to the `AuctionsMap` in an `AuctionHouseObject`.
*   **`GetAuction`**: Retrieves an `AuctionEntry` by ID from an `AuctionHouseObject`.
*   **`GetAccountAuctionCount`**: Returns the number of auctions owned by a specific account in an `AuctionHouseObject`.
*   **`GetAItem`**: Retrieves an `Item` from the global `mAitems` map by GUID low.
*   **`SendAuctionSuccessfulMail`**: Sends the profit to the seller via mail.
*   **`SendAuctionExpiredMail`**: Returns the item to the owner via mail if the auction expires with no bids.
*   **`MakeNewAuctionHouseObject`**: Creates a new `AuctionHouseObject` and stores it in `m_vRealAuctionHouses`.
*   **`LoadAuctionHouses`**: Initializes `m_mAuctionHouses` based on config flags.
*   **`LoadAuctionItems`**: Loads items from `item_instance` joined with `auction` into `mAitems`.
*   **`LoadAuctions`**: Loads auction data from `auction` table, validates items/houses, and adds to `AuctionHouseObject`s.
*   **`AddAItem`**: Adds an item to the global `mAitems` map.
*   **`RemoveAItem`**: Removes an item from the global `mAitems` map.
*   **`Update`**: Calls `Update()` on all `AuctionHouseObject` instances.
*   **`GetAuctionHouseTeam`**: Determines the faction of an auction house from its DBC ID.
*   **`GetAuctionHouseId`**: Maps a faction template ID to an auction house ID.
*   **`GetAuctionHouseEntry`**: Overloaded methods to get the DBC `AuctionHouseEntry` for a `Unit` or faction ID.
*   **`GetAuctionHouseEntry#2`**: Alias for `GetAuctionHouseEntry(uint32)`.
*   **`Update#2`**: Alias for `AuctionHouseObject::Update()`.
*   **`BuildListBidderItems`**: Builds a packet listing auctions the player has bid on.
*   **`BuildListOwnerItems`**: Builds a packet listing auctions the player owns.
*   **`BuildListAuctionItems`**: Builds a packet listing general auction items based on a query.
*   **`BuildAuctionInfo`**: Serializes an `AuctionEntry` and its item into a `WorldPacket`.
*   **`GetAuctionCut`**: Calculates the auction house cut from the bid.
*   **`GetAuctionOutBid`**: Calculates the minimum increment to outbid.
*   **`DeleteFromDB`**: Deletes the auction record from the `auction` table.
*   **`SaveToDB`**: Inserts a new auction record into the `auction` table.
*   **`IsAvailableFor`**: Checks if an auction is available to a player, respecting IP locks.

---

<!-- machine-true, projected from graph.json -->

## Map — AuctionHouseMgr

*Source:* AuctionHouseMgr.cpp, AuctionHouseMgr.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| RemoveAuction | method | ObjectMgr/FreeAuctionID | WorldSession.AuctionHouseHandler/HandleAuctionPlaceBid, WorldSession.AuctionHouseHandler/HandleAuctionRemoveItem | — |
| AddAuction | method | Errors/PrintStacktraceAndThrow | ChatHandler.AuctionHouseBotMgr/AddItem, WorldSession.AuctionHouseHandler/HandleAuctionSellItem | — |
| AuctionHouseMgr | ctor | — | — | — |
| ~AuctionHouseMgr | dtor | — | — | — |
| GetAuctionsMap | method | — | ChatHandler.AuctionHouseBotMgr/Update, WorldSession.AuctionHouseHandler/HandleAuctionListBidderItems, WorldSession.AuctionHouseHandler/HandleAuctionListItems, WorldSession.AuctionHouseHandler/HandleAuctionListOwnerItems, WorldSession.AuctionHouseHandler/HandleAuctionPlaceBid, WorldSession.AuctionHouseHandler/HandleAuctionRemoveItem, WorldSession.AuctionHouseHandler/HandleAuctionSellItem | — |
| GetAuctionDeposit | method | game_Objects_Item/GetCount, game_Objects_Item/GetProto, World/getConfig#2, World/getConfig#4 | ChatHandler.AuctionHouseBotMgr/AddItem, WorldSession.AuctionHouseHandler/HandleAuctionSellItem | — |
| AuctionHouseObject | ctor | — | — | — |
| ~AuctionHouseObject | dtor | — | — | — |
| SendAuctionWonMail | method | AccountMgr/GetSecurity, Database/CommitTransaction, Database/PExecute#2, game_Mail_Mail/AddItem, game_Mail_Mail/MailDraft, game_Mail_Mail/MailReceiver#2, game_Mail_Mail/MailSender#3, game_Mail_Mail/SendMailTo, game_Objects_Item/GetCount, game_Objects_Item/GetProto, Log.Main/Out, Object/GetEntry, Object/GetGUIDLow, ObjectGuid/ObjectGuid#2, ObjectMgr/GetMangosStringForDBCLocale, ObjectMgr/GetPlayer, ObjectMgr/GetPlayerAccountIdByGUID, ObjectMgr/GetPlayerNameByGUID, Player.Main/GetName, Player.Main/GetSession, Player.Main/Player#3, World/getConfig, WorldSession.AuctionHouseHandler/SendAuctionBidderNotification, WorldSession.Main/GetAccountId, WorldSession.Main/GetSecurity | WorldSession.AuctionHouseHandler/HandleAuctionPlaceBid | item_instance |
| GetCount | method | — | ChatHandler.AuctionHouseBotMgr/Update | — |
| GetAuctions | method | — | — | — |
| GetAuction | method | — | WorldSession.AuctionHouseHandler/HandleAuctionPlaceBid, WorldSession.AuctionHouseHandler/HandleAuctionRemoveItem, WorldSession.AuctionHouseHandler/operator() | — |
| GetAccountAuctionCount | method | — | WorldSession.AuctionHouseHandler/HandleAuctionSellItem | — |
| GetAItem | method | — | Player.Main/_LoadInventory, WorldSession.AuctionHouseHandler/HandleAuctionPlaceBid, WorldSession.AuctionHouseHandler/HandleAuctionRemoveItem, WorldSession.AuctionHouseHandler/HandleAuctionSellItem, WorldSession.AuctionHouseHandler/SendAuctionBidderNotification, WorldSession.AuctionHouseHandler/SendAuctionOwnerNotification, WorldSession.AuctionHouseHandler/SendAuctionRemovedNotification | — |
| SendAuctionSuccessfulMail | method | game_Mail_Mail/MailDraft, game_Mail_Mail/MailReceiver#2, game_Mail_Mail/MailSender#3, game_Mail_Mail/SendMailTo, Log.Main/Out, MailDraft/SetMoney, ObjectGuid/ObjectGuid#2, ObjectMgr/GetPlayer, ObjectMgr/GetPlayerAccountIdByGUID, Player.Main/GetSession, WorldSession.AuctionHouseHandler/SendAuctionOwnerNotification | WorldSession.AuctionHouseHandler/HandleAuctionPlaceBid | — |
| SendAuctionExpiredMail | method | Database/PExecute#2, game_Mail_Mail/AddItem, game_Mail_Mail/MailReceiver#2, game_Mail_Mail/MailSender#3, game_Mail_Mail/SendMailTo, Log.Main/Out, MailDraft/MailDraft#2, Object/GetGUIDLow, ObjectGuid/ObjectGuid#2, ObjectMgr/GetPlayer, ObjectMgr/GetPlayerAccountIdByGUID, Player.Main/GetSession, WorldSession.AuctionHouseHandler/SendAuctionOwnerNotification | — | item_instance |
| MakeNewAuctionHouseObject | method | — | — | — |
| LoadAuctionHouses | method | World/getConfig, World/GetWowPatch | World/SetInitialWorldSettings | — |
| LoadAuctionItems | method | Bag/NewItemOrBag, Database/Query, Field/GetUInt32, game_Objects_Item/LoadFromDB, Log.Main/Out, ObjectGuid/ObjectGuid, ObjectMgr/GetItemPrototype, ProgressBar/BarGoLink, ProgressBar/BarGoLink#3, ProgressBar/step, QueryResult/Fetch, QueryResult/GetRowCount, QueryResult/NextRow | World/SetInitialWorldSettings | auction, item_instance |
| LoadAuctions | method | Database/Query, Field/GetUInt32, game_Mail_Mail/AddItem, game_Mail_Mail/MailDraft, game_Mail_Mail/MailSender#3, game_Mail_Mail/SendMailTo, Log.Main/Out, MailReceiver/MailReceiver, ObjectGuid/ObjectGuid#2, ObjectGuid/ObjectGuid#5, ObjectMgr/GetPlayerAccountIdByGUID, ProgressBar/BarGoLink, ProgressBar/BarGoLink#3, ProgressBar/step, QueryResult/Fetch, QueryResult/GetRowCount, QueryResult/NextRow | World/SetInitialWorldSettings | auction |
| AddAItem | method | Errors/PrintStacktraceAndThrow, Object/GetGUIDLow | ChatHandler.AuctionHouseBotMgr/AddItem, WorldSession.AuctionHouseHandler/HandleAuctionSellItem | — |
| RemoveAItem | method | — | WorldSession.AuctionHouseHandler/HandleAuctionPlaceBid, WorldSession.AuctionHouseHandler/HandleAuctionRemoveItem | — |
| Update | method | — | World/Update | — |
| GetAuctionHouseTeam | method | — | — | — |
| GetAuctionHouseId | method | ObjectMgr/GetFactionTemplateEntry | — | — |
| GetAuctionHouseEntry | method | Object/GetTypeId, Player.Main/GetAuctionAccessMode, Player.Main/GetTeam, Unit.Main/GetFactionTemplateId, World/getConfig | WorldSession.AuctionHouseHandler/GetCheckedAuctionHouseForAuctioneer, WorldSession.AuctionHouseHandler/SendAuctionHello | — |
| GetAuctionHouseEntry#2 | method | — | ChatHandler.AuctionHouseBotMgr/Load | — |
| Update#2 | method | game_Objects_Item/GetCount, World/GetGameTime, World/LogTransaction | — | — |
| BuildListBidderItems | method | Object/GetGUIDLow | WorldSession.AuctionHouseHandler/operator() | — |
| BuildListOwnerItems | method | Object/GetGUIDLow, Player.Main/GetSession, WorldSession.Main/GetAccountId | WorldSession.AuctionHouseHandler/operator() | — |
| BuildListAuctionItems | method | game_Objects_Item/GetItemRandomPropertyId, game_Objects_Item/GetLocalizedNameWithSuffix, game_Objects_Item/GetProto, Player.Main/CanUseItem, Player.Main/GetSession, Player.Main/HasSpell, shared_Util/Utf8FitTo, SpellMgr/GetSpellEntry, SpellMgr/Instance, WorldSession.Main/GetSessionDbcLocale, WorldSession.Main/GetSessionDbLocaleIndex | WorldSession.AuctionHouseHandler/operator() | — |
| BuildAuctionInfo | method | ByteBuffer/operator<<#10, game_Objects_Item/GetCount, game_Objects_Item/GetEnchantmentId, game_Objects_Item/GetItemRandomPropertyId, game_Objects_Item/GetItemSuffixFactor, game_Objects_Item/GetSpellCharges, Log.Main/Out, Object/GetEntry, ObjectGuid/ObjectGuid#2, ObjectGuid/operator<< | WorldSession.AuctionHouseHandler/operator() | — |
| GetAuctionCut | method | World/getConfig#2 | WorldSession.AuctionHouseHandler/HandleAuctionRemoveItem | — |
| GetAuctionOutBid | method | — | WorldSession.AuctionHouseHandler/HandleAuctionPlaceBid, WorldSession.AuctionHouseHandler/SendAuctionBidderNotification, WorldSession.AuctionHouseHandler/SendAuctionCommandResult, WorldSession.AuctionHouseHandler/SendAuctionOwnerNotification | — |
| DeleteFromDB | method | Database/PExecute#2 | WorldSession.AuctionHouseHandler/HandleAuctionPlaceBid, WorldSession.AuctionHouseHandler/HandleAuctionRemoveItem | auction |
| SaveToDB | method | Database/PExecute#2 | ChatHandler.AuctionHouseBotMgr/AddItem, WorldSession.AuctionHouseHandler/HandleAuctionSellItem | auction |
| IsAvailableFor | method | Player.Main/GetSession, WorldSession.Main/GetRemoteAddress | — | — |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `auction`: id int(11) unsigned PK, house_id int(11) unsigned, item_guid int(11) unsigned, item_id int(11) unsigned, seller_guid int(11) unsigned, buyout_price int(11), expire_time bigint(40), buyer_guid int(11) unsigned, last_bid int(11), start_bid int(11), deposit int(11)
- `item_instance`: guid int(10) unsigned PK, item_id mediumint(8) unsigned, owner_guid int(10) unsigned, creator_guid int(10) unsigned, gift_creator_guid int(10) unsigned, count int(10) unsigned, duration int(10), charges tinytext?, flags mediumint(8) unsigned, enchantments text, random_property_id smallint(5), durability smallint(5) unsigned, text int(10) unsigned, generated_loot tinyint(4)?

*`?` = nullable, `PK` = primary key column.*

