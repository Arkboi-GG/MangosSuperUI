<!-- provenance: verbose -->
# AuctionHouseBotMgr

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# AuctionHouseBotMgr

## Purpose & Responsibilities

`AuctionHouseBotMgr` is a singleton manager responsible for automating the placement of items into the game's Auction House. It maintains a configured list of items (`m_items`) and periodically attempts to list them for sale, subject to configuration settings stored in `m_config`. The class encapsulates the state required to track loaded data, the specific auction house entry it targets, and the collection of item entries to be listed.

As indicated by the `MaNGOS::Singleton` macro, there is exactly one instance of this manager per server process, accessible globally via `sAuctionHouseBotMgr`. Its primary lifecycle involves loading configuration and item lists (presumably from a database, though the implementation details are not in this unit), and then updating the auction house listings through the `Update` method.

## Member-by-Member Behavior

The provided source contains only the declaration header (`AuctionHouseBotMgr.h`). The implementation (`.cpp`) is not provided, so behavior descriptions are derived strictly from the interface signatures, member variables, and comments in the header.

### Initialization and Lifecycle

**`AuctionHouseBotMgr` (Constructor)**
The constructor is defaulted. It initializes the object with empty containers and default flags. Since this is a singleton managed by `MaNGOS::Singleton`, the constructor is invoked automatically when the singleton instance is first accessed.

**`~AuctionHouseBotMgr` (Destructor)**
Declared but not defined in the header. It likely handles cleanup of resources, though `std::unique_ptr` and `std::vector` handle their own memory management.

### Core Operations

**`Load`**
This method is responsible for initializing the manager's state. Based on the presence of `m_loaded` (a boolean flag) and `m_config`/`m_items` members, this function presumably reads configuration data and item lists from persistent storage (likely a database table, though no SQL is visible in this unit). It sets `m_loaded` to true upon successful completion.

**`Update(bool force = false)`**
This is the main operational method, likely called periodically by the server's world update loop.
- **Normal Operation**: If the bot is enabled (checked via `m_config->enable`), it iterates through `m_items` and places auctions using `AddItem`.
- **Force Mode**: If `force` is `true`, the method bypasses the `enable` check, allowing administrators or other systems to trigger auction placements even if the bot is generally disabled. This is useful for debugging or emergency restocking.

**`AddItem(AuctionHouseBotEntry e, AuctionHouseObject *auctionHouse)`**
This helper method takes a single `AuctionHouseBotEntry` and a pointer to an `AuctionHouseObject` and performs the actual action of creating an auction. It translates the bot's internal representation (item ID, stack size, bid price, buyout price) into a live auction in the game world.

## Cross-Unit Boundaries

The MAP indicates that `AuctionHouseBotMgr` has **no outgoing calls** to other documented units and **no incoming calls** from other documented units. However, the source code reveals implicit dependencies:

1.  **`AuctionHouseObject`**: The `AddItem` method accepts an `AuctionHouseObject *`. This implies `AuctionHouseBotMgr` collaborates with the core Auction House system to create listings. The direction of data flow is from `AuctionHouseBotMgr` (providing item details) to `AuctionHouseObject` (executing the listing).
2.  **`Player`**: The header includes a forward declaration for `class Player`. While not explicitly used in the public interface shown, bots often require a "bot player" character to own the auctions. This suggests `AuctionHouseBotMgr` may interact with a `Player` object internally (in the unprovided `.cpp`) to act as the seller.
3.  **`MaNGOS::Singleton`**: The manager relies on the `MaNGOS::Singleton` template for instantiation and global access.

## Data Model

The MAP states that `AuctionHouseBotMgr` touches **no tables**. However, the presence of `Load()` and structured data types (`AuctionHouseBotEntry`, `AuctionHouseBotConfig`) strongly implies that the underlying implementation (in the missing `.cpp`) queries database tables to populate `m_items` and `m_config`. Without the source code or schema, we cannot name these tables or their columns. We can only observe the in-memory structures:

-   **`AuctionHouseBotEntry`**: Represents a single item to be auctioned.
    -   `item`: Item ID.
    -   `stack`: Stack size.
    -   `bid`: Starting bid price.
    -   `buyout`: Immediate buyout price.
-   **`AuctionHouseBotConfig`**: Global configuration for the bot.
    -   `itemcount`: Likely the maximum number of items to list or a limit on concurrent auctions.
    -   `ahfid`: Auction House Faction ID, determining which faction's auction house the bot uses.
    -   `enable`: Boolean flag to turn the bot on/off.

## Notable Implementation Details

1.  **Singleton Pattern**: The use of `MaNGOS::Singleton` ensures global accessibility via `sAuctionHouseBotMgr`. This is a common pattern in MaNGOS/WowServer architectures for managers that need to be accessed from various parts of the codebase (e.g., world updates, console commands).
2.  **Force Flag**: The `Update` method's `force` parameter is a critical design choice. It allows the system to decouple the *mechanism* of placing auctions from the *policy* of whether the bot should be active. This enables administrative overrides without changing code.
3.  **Memory Management**: The use of `std::unique_ptr<AuctionHouseBotConfig>` suggests that the configuration might be allocated dynamically, possibly because it is loaded from a variable-length source or to allow for easy reset/reload. The `m_items` vector is owned directly, implying it is rebuilt or cleared during `Load()`.
4.  **Missing Implementation**: The provided source is only the header. All logic for database interaction, iteration over items, and actual auction creation resides in the corresponding `.cpp` file, which is not part of this documentation scope.

## Member Reference

**AuctionHouseBotMgr**
Default constructor for the singleton manager. Initializes internal state to defaults.

---

<!-- machine-true, projected from graph.json -->

## Map — AuctionHouseBotMgr

*Source:* AuctionHouseBotMgr.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| AuctionHouseBotMgr | ctor | — | — | — |
