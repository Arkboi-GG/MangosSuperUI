<!-- provenance: boundary-bleed -->
# ChatHandler.AuctionHouseBotMgr

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# AuctionHouseBotMgr

## Purpose & Responsibilities

`AuctionHouseBotMgr` is a singleton service responsible for automatically populating the server's auction houses with items. It acts as a background agent that maintains a minimum number of active listings (`itemcount`) by creating new auctions from a predefined list of items stored in the `auctionhousebot` database table.

The manager handles:
1.  **Configuration Loading:** Reading bot settings (enable/disable, target faction, desired item count) from the server configuration files.
2.  **Item Definition Loading:** Fetching the catalog of items to sell, including their stack sizes, starting bids, and buyout prices, from the `auctionhousebot` table.
3.  **Periodic Updates:** Checking the current auction house inventory during the world update loop and adding new random items from its catalog if the total count falls below the configured threshold.
4.  **Auction Creation:** Constructing valid `AuctionEntry` objects, generating associated `Item` instances with appropriate random properties, calculating deposits, and persisting both to the database.

It provides two chat commands (`ahbot update` and `ahbot reload`) for administrators to manually trigger updates or reload configurations without restarting the server. Note that the command handlers themselves are implemented in the `ChatHandler` unit, while the core logic resides here.

## Member-by-Member Behavior

### Initialization and Lifecycle

**`~AuctionHouseBotMgr`**
The destructor cleans up resources. It clears the internal vector `m_items` holding the loaded item definitions. If the configuration object `m_config` exists, it resets the unique pointer, freeing the memory.

**`Load`**
This method initializes the bot's state. It performs the following steps:
1.  **Reset:** Clears `m_items` and sets `m_loaded` to `false`. Resets `m_config` if it exists.
2.  **Database Query:** Executes `SELECT item, stack, bid, buyout FROM auctionhousebot` against the `WorldDatabase`.
3.  **Parsing:** If results exist, it iterates through each row, extracting the four columns into an `AuctionHouseBotEntry` struct and pushing it onto `m_items`. It uses a `BarGoLink` progress bar for logging feedback.
4.  **Logging:** Outputs the number of loaded items.
5.  **Configuration:** Creates a new `AuctionHouseBotConfig` object. It reads three values from the server config:
    *   `AHBot.Enable` (bool, default `false`)
    *   `AHBot.ah.fid` (int, default `120`)
    *   `AHBot.itemcount` (int, default `2`)
6.  **Validation:** Retrieves the `AuctionHouseEntry` corresponding to the configured faction ID (`ahfid`) via `AuctionHouseMgr`. If no such auction house exists, it logs an error and returns early, leaving `m_loaded` as `false`.
7.  **Finalization:** Sets `m_loaded` to `true` if successful.

### Core Logic

**`Update`**
Called periodically by the `World` update loop. It ensures the auction house meets the minimum item count requirement.
1.  **Guard Clauses:** Returns immediately if `m_loaded` is `false`, if the bot is disabled (`!m_config->enable`) and not forced, or if `m_items` is empty.
2.  **Auction House Retrieval:** Gets the `AuctionHouseObject` for the configured faction using `AuctionHouseMgr`. Logs an error and returns if the object is missing.
3.  **Count Comparison:** Compares the current number of auctions (`auctionHouse->GetCount()`) against the configured `itemcount`.
4.  **Population Loop:** While the current count is less than the target:
    *   Selects a random item definition from `m_items` using `urand`.
    *   Calls `AddItem` to create and register the auction.
    *   Increments the local `auctions` counter.

**`AddItem`**
Creates a single auction listing for the given `AuctionHouseBotEntry` and adds it to the specified `AuctionHouseObject`.
1.  **Prototype Validation:** Looks up the `ItemPrototype` for the item entry. If invalid, logs an error and returns.
2.  **Item Creation:** Creates a new `Item` instance with count 1. If creation fails, logs and returns.
3.  **Random Properties:** Generates a random property ID (for sockets, enchants, etc.) using `Item::GenerateItemRandomPropertyId`. If a valid ID is returned, applies it to the item.
4.  **Duration Calculation:** Randomly selects an expiration time (`etime`) from three options: 12 hours (43200s), 24 hours (86400s), or 48 hours (172800s).
5.  **Stack Size:** Sets the item's count to the `stack` value from the entry.
6.  **Deposit Calculation:** Calculates the auction deposit using `AuctionHouseMgr::GetAuctionDeposit`.
7.  **Auction Entry Construction:** Allocates a new `AuctionEntry` and populates its fields:
    *   `Id`: Generated via `ObjectMgr::GenerateAuctionID`.
    *   `auctionHouseEntry`: The configured auction house entry.
    *   `itemGuidLow`: From the created item.
    *   `itemTemplate`: From the created item.
    *   `owner`: Set to `0` (indicating a bot/system owner, not a player).
    *   `startbid`: From the entry's `bid`.
    *   `buyout`: From the entry's `buyout`.
    *   `bidder`/`bid`: Set to `0`.
    *   `deposit`: Calculated deposit.
    *   `depositTime`: Current time.
    *   `expireTime`: Current time + `etime`.
8.  **Persistence:** Saves the item to the database via `item->SaveToDB()`.
9.  **Registration:** Adds the item to the auction house manager via `AuctionHouseMgr::AddAItem()` and adds the auction entry to the specific `AuctionHouseObject` via `auctionHouse->AddAuction()`. Finally, saves the auction entry to the database via `auctionEntry->SaveToDB()`.

### Administrative Commands

**`HandleAHBotUpdateCommand`**
A chat command handler implemented in `ChatHandler` that forces an immediate update cycle by calling `AuctionHouseBotMgr::Update(true)`. It sends a system message confirming completion.

**`HandleAHBotReloadCommand`**
A chat command handler implemented in `ChatHandler` that reloads the bot's configuration and item list by calling `AuctionHouseBotMgr::Load()`. It sends a system message confirming completion.

## Cross-Unit Boundaries

### Calls Out

*   **`AuctionHouseMgr`**:
    *   `GetAuctionHouseEntry`: Used in `Load` to validate the configured faction ID maps to a valid auction house.
    *   `GetAuctionsMap`: Used in `Update` to retrieve the live auction house object for counting and adding auctions.
    *   `GetCount`: Used in `Update` to determine how many auctions currently exist.
    *   `AddAItem`: Used in `AddItem` to register the newly created item with the global auction manager.
    *   `AddAuction`: Used in `AddItem` to add the specific auction entry to the faction's auction house object.
    *   `GetAuctionDeposit`: Used in `AddItem` to calculate the required deposit for the new auction.
    *   `SaveToDB`: Used in `AddItem` to persist the new auction entry.
*   **`Config`**:
    *   `GetBoolDefault` / `GetIntDefault`: Used in `Load` to read `AHBot.Enable`, `AHBot.ah.fid`, and `AHBot.itemcount`.
*   **`Database`**:
    *   `Query`: Used in `Load` to fetch item definitions from `auctionhousebot`.
*   **`Field` / `QueryResult`**:
    *   `GetUInt32`, `Fetch`, `GetRowCount`, `NextRow`: Used in `Load` to parse the database results.
*   **`Log.Main`**:
    *   `Out`: Used extensively for status logging, errors, and debug information.
*   **`ProgressBar`**:
    *   `BarGoLink` / `step`: Used in `Load` to display progress while loading items.
*   **`game_Objects_Item`**:
    *   `CreateItem`: Used in `AddItem` to instantiate the item object.
    *   `GenerateItemRandomPropertyId`: Used in `AddItem` to assign random stats/enchants.
    *   `SaveToDB`: Used in `AddItem` to persist the item.
    *   `SetCount`: Used in `AddItem` to set the stack size.
    *   `SetItemRandomProperties`: Used in `AddItem` to apply random properties.
*   **`Object`**:
    *   `GetEntry` / `GetGUIDLow`: Used in `AddItem` to retrieve item metadata for the auction entry.
*   **`ObjectMgr`**:
    *   `GenerateAuctionID`: Used in `AddItem` to create a unique ID for the auction.
    *   `GetItemPrototype`: Used in `AddItem` to validate the item exists.
*   **`shared_Util`**:
    *   `urand`: Used in `Update` to pick random items and in `AddItem` to pick random durations.
*   **`ChatHandler.Chat`**:
    *   `SendSysMessage`: Used in `HandleAHBotUpdateCommand` and `HandleAHBotReloadCommand` (implemented in `ChatHandler`) to notify the admin.

### Called By

*   **`World`**:
    *   `SetInitialWorldSettings`: Calls `Load` during server startup to initialize the bot.
    *   `Update`: Calls `Update` periodically to maintain auction house population.

## Data Model

The unit interacts with one database table:

**`auctionhousebot`**
*   **Purpose:** Defines the catalog of items the bot is allowed to list.
*   **Columns:**
    *   `item` (int unsigned): The item entry ID.
    *   `stack` (tinyint unsigned): The number of items in the stack.
    *   `bid` (int unsigned): The starting bid price.
    *   `buyout` (int unsigned): The buyout price.

The code assumes this table contains valid item IDs and reasonable prices. It does not enforce constraints beyond basic existence checks in C++.

## Notable Implementation Details

1.  **Owner GUID 0:** In `AddItem`, the `owner` field of the `AuctionEntry` is hardcoded to `0`. This distinguishes bot auctions from player auctions. Care must be taken elsewhere in the codebase to handle auctions with owner `0` correctly (e.g., preventing players from bidding on them if intended, or handling their expiration differently).
2.  **Random Duration Switch:** The duration selection in `AddItem` uses a `switch` on a random integer 1-3. Case 1 sets 12 hours, Case 2 sets 24 hours, Case 3 sets 48 hours. The `default` case also sets 24 hours, which is redundant since `urand(1, 3)` guarantees a value between 1 and 3.
3.  **No Bidder/Bid Initialization:** New auctions are created with `bidder = 0` and `bid = 0`. This is standard for new listings but implies no initial bid has been placed.
4.  **Force Flag:** The `Update` method accepts a `force` boolean. If `true`, it bypasses the `m_config->enable` check. This allows admins to trigger updates via chat command even if the bot is globally disabled in the config.
5.  **Memory Management:** `AuctionEntry` is allocated with `new` in `AddItem`. The responsibility for deleting this object lies with the `AuctionHouseObject` or `AuctionHouseMgr` when the auction expires or is removed. The bot itself does not track these pointers after insertion.
6.  **Thread Safety:** As a singleton accessed from the main world update loop and potentially from chat commands (which run on the main thread in typical MaNGOS architectures), it relies on the single-threaded nature of the core loop for safety. No explicit locks are used.

## Member Reference

**`~AuctionHouseBotMgr`**
Destructor. Clears the `m_items` vector and resets the `m_config` unique pointer to free memory.

**`Load`**
Initializes the bot. Clears existing state. Queries the `auctionhousebot` table to populate `m_items` with item definitions. Reads configuration values (`AHBot.Enable`, `AHBot.ah.fid`, `AHBot.itemcount`) from the server config. Validates that the configured faction ID corresponds to a valid `AuctionHouseEntry`. Sets `m_loaded` to `true` on success.

**`Update`**
Periodically checks if the auction house for the configured faction has fewer items than `m_config->itemcount`. If so, and if the bot is enabled (or `force` is true), it repeatedly selects random items from `m_items` and calls `AddItem` until the count matches the target.

**`AddItem`**
Creates a new auction for the given `AuctionHouseBotEntry`. Validates the item prototype. Creates an `Item` object, assigns random properties, and sets the stack size. Calculates a random duration (12, 24, or 48 hours). Computes the deposit. Constructs an `AuctionEntry` with owner `0`, starting bid, and buyout from the entry. Persists the item and auction to the database and registers them with `AuctionHouseMgr` and the specific `AuctionHouseObject`.

**`HandleAHBotUpdateCommand`**
Chat command handler (implemented in `ChatHandler`). Forces an immediate execution of `AuctionHouseBotMgr::Update(true)` and sends a confirmation message to the chat handler.

**`HandleAHBotReloadCommand`**
Chat command handler (implemented in `ChatHandler`). Triggers `AuctionHouseBotMgr::Load()` to refresh configuration and item lists from the database and config files, then sends a confirmation message.

---

<!-- machine-true, projected from graph.json -->

## Map — ChatHandler.AuctionHouseBotMgr

*Source:* AuctionHouseBotMgr.cpp, AuctionHouseBotMgr.h, Chat.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ~AuctionHouseBotMgr | dtor | — | — | — |
| Load | method | AuctionHouseMgr/GetAuctionHouseEntry#2, Config/GetBoolDefault, Config/GetIntDefault, Database/Query, Field/GetUInt32, Log.Main/Out, ProgressBar/BarGoLink, ProgressBar/BarGoLink#3, ProgressBar/step, QueryResult/Fetch, QueryResult/GetRowCount, QueryResult/NextRow | World/SetInitialWorldSettings | auctionhousebot |
| Update | method | AuctionHouseMgr/GetAuctionsMap, AuctionHouseMgr/GetCount, Errors/PrintStacktraceAndThrow, Log.Main/Out, shared_Util/urand | World/Update | — |
| AddItem | method | AuctionHouseMgr/AddAItem, AuctionHouseMgr/AddAuction, AuctionHouseMgr/GetAuctionDeposit, AuctionHouseMgr/SaveToDB, Errors/PrintStacktraceAndThrow, game_Objects_Item/CreateItem, game_Objects_Item/GenerateItemRandomPropertyId, game_Objects_Item/SaveToDB, game_Objects_Item/SetCount, game_Objects_Item/SetItemRandomProperties, Log.Main/Out, Object/GetEntry, Object/GetGUIDLow, ObjectMgr/GenerateAuctionID, ObjectMgr/GetItemPrototype, shared_Util/urand | — | — |
| HandleAHBotUpdateCommand | method | ChatHandler.Chat/SendSysMessage | — | — |
| HandleAHBotReloadCommand | method | ChatHandler.Chat/SendSysMessage | — | — |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `auctionhousebot`: item int(11) unsigned, stack tinyint(3) unsigned, bid int(11) unsigned, buyout int(11) unsigned

*`?` = nullable, `PK` = primary key column.*


---

<!-- verify: boundary-bleed | foreign: ChatHandler, disable, enable, Load -->
