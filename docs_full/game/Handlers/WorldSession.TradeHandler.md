<!-- provenance: boundary-bleed -->
# WorldSession.TradeHandler

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# WorldSession.TradeHandler

## Purpose & Responsibilities

`WorldSession.TradeHandler` implements the server-side logic for player-to-player trading within the `wowvmangos` emulator. Residing in `TradeHandler.cpp`, it provides methods on the `WorldSession` class to manage the entire lifecycle of a trade interaction: initiation, negotiation (adding/removing items and gold), acceptance, execution, and cancellation.

The unit is responsible for:
1.  **Protocol Handling:** Parsing client opcodes related to trade actions (`CMSG_INITIATE_TRADE`, `CMSG_SET_TRADE_ITEM`, etc.) and sending corresponding server responses (`SMSG_TRADE_STATUS`, `SMSG_TRADE_STATUS_EXTENDED`).
2.  **Validation & Security:** Enforcing rules such as distance limits, faction restrictions, trial account limitations, and anti-cheat measures (e.g., preventing trades with dead/stunned players, verifying item tradability, checking for gold caps, and implementing a scam-prevention delay).
3.  **State Management:** Coordinating the `TradeData` objects associated with both participating players to ensure synchronized state (accepted, pending, modified).
4.  **Transaction Execution:** Safely moving items between inventories and transferring gold, including handling edge cases like inventory fullness and database consistency during trades involving Game Masters (GMs) or players with disabled saving.

It does not define the `TradeData` structure itself but interacts heavily with it via `Player.Main/GetTradeData` and direct access to `Player::m_trade`.

## Member-by-Member Behavior

### Trade Initiation and Status Reporting

**`HandleInitiateTradeOpcode`**
This method processes the initial request to start a trade with another player. It performs extensive validation before creating the trade session:
- Checks if the initiator or target is already in a trade, dead, stunned, taxi-flying, or logging out.
- Verifies the target exists on the same map and is within `TRADE_DISTANCE`.
- Enforces faction restrictions if `CONFIG_BOOL_ALLOW_TWO_SIDE_INTERACTION_TRADE` is disabled.
- Blocks trades if either account has trial restrictions.
- If valid, it creates new `TradeData` instances for both players, links them as traders, sets a 200ms scam prevention delay, and sends `TRADE_STATUS_BEGIN_TRADE` to both clients.

**`SendTradeStatus`**
Constructs and sends an `SMSG_TRADE_STATUS` packet to the client. The packet structure varies slightly depending on the `TradeStatus` enum value (e.g., `TRADE_STATUS_OPEN_WINDOW` vs `TRADE_STATUS_CLOSE_WINDOW`), encoding the status code and optional GUIDs or flags.

**`HandleBeginTradeOpcode`**
Sent by the client to formally open the trade window UI after initiation. It simply triggers `SendTradeStatus(TRADE_STATUS_OPEN_WINDOW)` for both the initiator and the target.

**`SendCancelTrade`**
A helper that sends a trade status packet, but first checks `m_playerRecentlyLogout` to suppress packets if the player has recently disconnected, avoiding errors on closed sockets.

**`HandleCancelTradeOpcode`**
Processes the client's request to cancel the trade. It delegates to `Player.Main/TradeCancel`, passing `true` to indicate the cancellation originated from the player.

**`HandleIgnoreTradeOpcode`**
Handles the "Ignore" button in the trade window. It calls `Player.Main/TradeCancel` with `TRADE_STATUS_IGNORE_YOU`, effectively ending the trade and notifying the other party.

**`HandleBusyTradeOpcode`**
Handles the "Busy" button. Similar to ignore, it calls `Player.Main/TradeCancel` with `TRADE_STATUS_BUSY`.

### Trade Negotiation (Items and Gold)

**`HandleSetTradeItemOpcode`**
Adds an item to the trade window.
- Validates the slot index and ensures the item exists in the player's inventory.
- Checks if the item is tradable (`Item::CanBeTraded`) unless it's the non-traded spell slot.
- Prevents trading items from bank slots.
- Prevents duplicate placement of the same item GUID into multiple slots.
- Resets the trade acceptance state for both parties (`SetAccepted(false)`) and updates the last modification time to reset the scam prevention timer.

**`HandleClearTradeItemOpcode`**
Removes an item from a specific trade slot. Like setting an item, it resets acceptance states and modification times for both traders.

**`HandleSetTradeGoldOpcode`**
Sets the amount of gold offered in the trade.
- Validates that the offered amount does not exceed the player's current gold.
- Resets acceptance states and modification times for both traders.

**`SendUpdateTrade`**
Sends the detailed contents of the trade window (`SMSG_TRADE_STATUS_EXTENDED`) to the client.
- Takes a boolean `trader_state` to determine whether to send the local player's trade data or the partner's.
- Iterates through all trade slots, packing item details (entry, display ID, count, enchantments, durability, etc.) into the packet.
- Includes the offered gold and any spell ID associated with the trade.

### Trade Acceptance and Execution

**`HandleAcceptTradeOpcode`**
The core logic for finalizing a trade. This is a complex, multi-step process:
1.  **Scam Prevention:** Checks if enough time (`GetScamPreventionDelay`) has passed since the last modification. If not, it rejects the trade with `TRADE_STATUS_BACK_TO_TRADE`.
2.  **State Setup:** Marks the local trade as accepted.
3.  **Validity Checks:**
    - Distance check (`TRADE_DISTANCE`).
    - Gold sufficiency checks for both parties.
    - Gold cap checks to prevent overflow/loss.
    - GM trade restrictions (`CONFIG_BOOL_GM_ALLOW_TRADES`).
    - Item tradability re-check for all items in both windows.
4.  **Partner Acceptance:** Proceeds only if the partner has also accepted (`his_trade->IsAccepted()`).
5.  **Logging:** Logs the transaction details via `TradeData/FillTransactionLog` and `World/LogTransaction`.
6.  **Preparation:** Calls `setAcceptTradeMode` to lock items in trade state.
7.  **Spell Handling:** If spells are involved, it validates and prepares `Spell` objects for both parties, checking cast validity. If validation fails, it cleans up and rejects.
8.  **Inventory Space Check:** Uses `Player.Main/CanStoreItems` to verify both players have space for the incoming items. If not, it notifies both players and cancels.
9.  **Execution:**
    - Removes items from inventories.
    - Calls `MoveItems` to transfer items to the opposite player.
    - Adjusts gold balances using `Player.Main/ModifyMoney` and logs the changes.
    - Casts any prepared spells.
10. **Cleanup:** Clears trade modes, deletes `TradeData` objects, saves inventory/gold to the database (`Player.Main/SaveInventoryAndGoldToDB`), and sends `TRADE_STATUS_TRADE_COMPLETE` to both clients.

**`HandleUnacceptTradeOpcode`**
Allows a player to withdraw their acceptance. It sets the local trade's accepted state to false and notifies the partner.

### Internal Helpers

**`MoveItems`**
Transfers items between the two players' inventories.
- Iterates through the traded slots.
- Checks if both players can store the respective items.
- If successful, it moves the items.
- **Critical Safety Logic:** If a player has `IsSavingDisabled()` (often true for GMs or during certain debug states), it explicitly deletes the item from the database (`DeleteFromInventoryDB`, `DeleteAllFromDB`) before moving it to prevent duplication exploits.
- Handles rollback: If storage fails for one item, it attempts to return already-moved items to their original owners and logs errors.

**`setAcceptTradeMode`**
Static helper that marks the trade as being in the acceptance process and sets the `InTrade` flag on all items involved, locking them from other interactions.

**`clearAcceptTradeMode`**
Two overloads:
1.  Clears the `InAcceptProcess` flag on `TradeData` objects.
2.  Clears the `InTrade` flag on the `Item` objects themselves, unlocking them.

## Cross-Unit Boundaries

### Collaboration with `Player` (`Player.Main`)
The `WorldSession` acts as the network interface, while `Player` holds the authoritative state.
- **Direction:** `WorldSession` -> `Player`
- **Why:** To validate actions (gold, inventory space, trade data existence) and execute state changes (moving items, modifying gold, saving to DB).
- **Key Calls:**
    - `Player.Main/GetTradeData`: Retrieves the `TradeData` object for validation and manipulation.
    - `Player.Main/TradeCancel`: Delegates the complex cleanup of a cancelled trade to the Player class.
    - `Player.Main/CanStoreItem` / `CanStoreItems`: Verifies inventory capacity before committing to a trade.
    - `Player.Main/MoveItemToInventory` / `MoveItemFromInventory`: Executes the physical transfer of items.
    - `Player.Main/ModifyMoney` / `LogModifyMoney`: Updates gold balance and records the transaction.
    - `Player.Main/SaveInventoryAndGoldToDB`: Persists the final state after a successful trade.

### Collaboration with `TradeData` (`TradeData`)
`TradeData` is the container for the trade's transient state (items, gold, acceptance flags).
- **Direction:** Bidirectional (mostly `WorldSession` reading/writing `TradeData`).
- **Why:** To maintain the synchronized view of the trade window for both clients.
- **Key Calls:**
    - `TradeData/SetItem`, `TradeData/SetMoney`: Update the trade contents.
    - `TradeData/SetAccepted`: Tracks whether both parties have agreed.
    - `TradeData/SetLastModificationTime`: Used for the scam prevention timer.
    - `TradeData/GetItem`, `TradeData/GetMoney`: Read current trade contents for validation or packet construction.

### Collaboration with `Item` (`game_Objects_Item`)
- **Direction:** `WorldSession` -> `Item`
- **Why:** To inspect item properties (tradability, enchantments, durability) for packet serialization and validation.
- **Key Calls:**
    - `game_Objects_Item/CanBeTraded`: Critical security check to prevent trading bound or untradeable items.
    - `game_Objects_Item/GetProto`, `GetCount`, `GetEnchantmentId`, etc.: Used in `SendUpdateTrade` to build the packet payload.
    - `game_Objects_Item/SetInTrade`: Locks the item during the acceptance phase.
    - `game_Objects_Item/DeleteFromInventoryDB`: Used in `MoveItems` for safety when dealing with unsaved/disabled players.

### Collaboration with `Spell` (`Spell.Main`)
- **Direction:** `WorldSession` -> `Spell`
- **Why:** Trades can include spells (e.g., enchanting or conjuring). These must be validated and cast upon trade completion.
- **Key Calls:**
    - `Spell.Main/CheckCast`: Validates if the spell can be cast in the current context.
    - `Spell.Main/prepare`: Executes the spell effect after the trade is finalized.
    - `Spell.Main/Delete`: Cleans up temporary spell objects if the trade fails or succeeds.

### Collaboration with `World` (`World`)
- **Direction:** `WorldSession` -> `World`
- **Why:** To access global configuration settings and log transactions.
- **Key Calls:**
    - `World/getConfig`: Checks flags like `CONFIG_BOOL_GM_ALLOW_TRADES`, `CONFIG_BOOL_ALLOW_TWO_SIDE_INTERACTION_TRADE`, and `CONFIG_BOOL_GM_LOG_TRADE`.
    - `World/LogTransaction`: Records the trade event in the server logs.

### Collaboration with `ByteBuffer` / `WorldPacket`
- **Direction:** `WorldSession` -> `ByteBuffer`/`WorldPacket`
- **Why:** To construct binary packets for network transmission.
- **Key Calls:** Standard serialization operators (`operator<<`) and constructors.

## Data Model

This unit does not directly query or modify database tables via SQL statements. It relies on higher-level abstractions (`Player`, `Item`) to handle persistence. Specifically:
- `Player.Main/SaveInventoryAndGoldToDB` handles the persistence of inventory and gold changes.
- `game_Objects_Item/DeleteFromInventoryDB` handles removal of item records.
- No direct table references (e.g., `character_inventory`, `characters`) are made in this source file.

## Notable Implementation Details

1.  **Scam Prevention Delay:**
    In `HandleAcceptTradeOpcode`, the code calculates the time elapsed since `GetLastModificationTime()`. If it is less than `GetScamPreventionDelay()` (set to 200ms in `HandleInitiateTradeOpcode`), the trade is rejected with `TRADE_STATUS_BACK_TO_TRADE`. This prevents race conditions where a player accepts a trade immediately after the other modifies it, potentially leading to inconsistent states or exploits.

2.  **GM Trade Safety & Duplication Prevention:**
    In `MoveItems`, there is a specific check: `if (trader->IsSavingDisabled())`. If the receiving player has saving disabled (common for GMs or bots), the code explicitly calls `DeleteFromInventoryDB()` and `DeleteAllFromDB()` on the item *before* moving it to the new inventory. The comment explains: *"If saving is disabled for player who receives the item, it must be deleted from db, or it enables duping."* This is a critical safeguard against item duplication exploits involving unsaved characters.

3.  **Gold Cap Overflow Protection:**
    `HandleAcceptTradeOpcode` includes a check to ensure that neither player exceeds their maximum gold capacity (`GetMaxMoney()`) after the trade. It calculates the net change for both parties and rejects the trade if the result would exceed the cap, preventing gold loss due to integer overflow or clamping.

4.  **Spell Casting in Trades:**
    The code supports casting spells as part of a trade (likely for enchanting or conjuring items directly into the trade window). It creates temporary `Spell` objects, validates them with `CheckCast`, and only executes them with `prepare` if the entire trade (items and gold) succeeds. If validation fails, the spell objects are deleted, and the trade is rolled back.

5.  **Inventory Rollback Logic:**
    In `MoveItems`, if one player cannot store an item (inventory full), the code attempts to return any items that were *already* moved in the loop to their original owners. This partial rollback mechanism helps mitigate data corruption, though it logs errors if the rollback itself fails.

6.  **Trial Account Restrictions:**
    `HandleInitiateTradeOpcode` checks `HasTrialRestrictions()` for both players. If either is on a trial account, the trade is blocked with `TRADE_STATUS_TRIAL_ACCOUNT`.

## Member Reference

**SendTradeStatus**
Constructs and sends an `SMSG_TRADE_STATUS` packet to the client based on the provided `TradeStatus` enum. It handles different packet sizes and structures for various statuses (e.g., including GUIDs for begin trade, or extra bytes for close window).

**HandleIgnoreTradeOpcode**
Processes the client's "Ignore" action in a trade. It calls `Player.Main/TradeCancel` with the status `TRADE_STATUS_IGNORE_YOU` to terminate the trade and notify the partner.

**HandleBusyTradeOpcode**
Processes the client's "Busy" action in a trade. It calls `Player.Main/TradeCancel` with the status `TRADE_STATUS_BUSY` to terminate the trade and notify the partner.

**SendUpdateTrade**
Serializes the current state of the trade window (items, gold, spells) into an `SMSG_TRADE_STATUS_EXTENDED` packet and sends it to the client. It uses the `trader_state` flag to determine whether to serialize the local player's trade data or the partner's.

**MoveItems**
Executes the physical transfer of items between two players' inventories. It validates storage capacity, handles GM/saving-disabled safety checks by deleting items from the DB if necessary, and implements a partial rollback mechanism if storage fails mid-transfer.

**setAcceptTradeMode**
Static helper function that marks the trade as being in the acceptance process (`SetInAcceptProcess`) and sets the `InTrade` flag on all items involved, locking them from other interactions.

**clearAcceptTradeMode**
Static helper function (overload 1) that clears the `InAcceptProcess` flag on the `TradeData` objects for both players.

**clearAcceptTradeMode#2**
Static helper function (overload 2) that clears the `InTrade` flag on the `Item` objects involved in the trade, unlocking them.

**HandleAcceptTradeOpcode**
The main logic for finalizing a trade. It validates scam prevention delays, distance, gold amounts, gold caps, GM restrictions, and item tradability. If both parties have accepted, it logs the transaction, prepares any spells, verifies inventory space, moves items via `MoveItems`, adjusts gold, casts spells, saves to DB, and sends completion status.

**HandleUnacceptTradeOpcode**
Allows a player to withdraw their acceptance of a trade. It sets the local trade's accepted state to false and notifies the partner client.

**HandleBeginTradeOpcode**
Processes the client's request to open the trade window UI. It sends `TRADE_STATUS_OPEN_WINDOW` to both the initiator and the target.

**SendCancelTrade**
Helper method that sends a trade status packet, suppressing output if the player has recently logged out to avoid socket errors.

**HandleCancelTradeOpcode**
Processes the client's request to cancel the trade. It delegates to `Player.Main/TradeCancel` to handle the cleanup and notification.

**HandleInitiateTradeOpcode**
Processes the initial request to start a trade. It validates player states (alive, not stunned, not logging out), distance, faction, and trial restrictions. If valid, it creates `TradeData` objects for both players, sets a scam prevention delay, and sends `TRADE_STATUS_BEGIN_TRADE`.

**HandleSetTradeGoldOpcode**
Sets the amount of gold offered in the trade. It validates the amount against the player's current gold and resets the acceptance state and modification timer for both traders.

**HandleSetTradeItemOpcode**
Adds an item to the trade window. It validates the item's existence, tradability, and source (not bank), prevents duplicates, and resets the acceptance state and modification timer for both traders.

**HandleClearTradeItemOpcode**
Removes an item from a specific trade slot. It resets the acceptance state and modification timer for both traders.

---

<!-- machine-true, projected from graph.json -->

## Map — WorldSession.TradeHandler

*Source:* TradeHandler.cpp, WorldSession.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| SendTradeStatus | method | ByteBuffer/operator<<#10, ByteBuffer/operator<<#11, ByteBuffer/operator<<#7, WorldPacket/Initialize, WorldPacket/WorldPacket, WorldSession.Main/SendPacket | TradeData/SetAccepted | — |
| HandleIgnoreTradeOpcode | method | Player.Main/TradeCancel | — | — |
| HandleBusyTradeOpcode | method | Player.Main/TradeCancel | — | — |
| SendUpdateTrade | method | ByteBuffer/operator<<#10, ByteBuffer/operator<<#7, game_Objects_Item/GetCount, game_Objects_Item/GetEnchantmentId, game_Objects_Item/GetItemRandomPropertyId, game_Objects_Item/GetItemSuffixFactor, game_Objects_Item/GetProto, game_Objects_Item/GetSpellCharges, Object/GetGuidValue, Object/GetUInt32Value, Object/HasFlag, ObjectGuid/operator<<, Player.Main/GetTradeData, TradeData/GetItem, TradeData/GetMoney, TradeData/GetSpell, TradeData/GetTraderData, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | TradeData/Update | — |
| MoveItems | method | game_Objects_Item/DeleteAllFromDB, game_Objects_Item/DeleteFromInventoryDB, game_Objects_Item/GetCount, game_Objects_Item/GetProto, Log.Main/Out, Object/GetEntry, Object/GetGUIDLow, Object/GetGuidStr, Player.Main/CanStoreItem, Player.Main/GetName, Player.Main/GetSession, Player.Main/GetTrader, Player.Main/IsSavingDisabled, Player.Main/MoveItemToInventory, Player.Main/Player, World/getConfig, WorldSession.Main/GetAccountId, WorldSession.Main/GetSecurity | — | — |
| setAcceptTradeMode | function | game_Objects_Item/GetBagSlot, game_Objects_Item/GetSlot, game_Objects_Item/SetInTrade, Log.Main/Out, Object/GetGuidStr, TradeData/GetItem, TradeData/SetInAcceptProcess | — | — |
| clearAcceptTradeMode | function | TradeData/SetInAcceptProcess | — | — |
| clearAcceptTradeMode#2 | function | game_Objects_Item/SetInTrade | — | — |
| HandleAcceptTradeOpcode | method | game_Objects_Item/CanBeTraded, game_Objects_Item/GetBagSlot, game_Objects_Item/GetSlot, Object/GetGuidValue, Object/GetObjectGuid, Object/SetGuidValue, ObjectGuid/IsEmpty, Player.Main/CanStoreItems, Player.Main/GetMaxMoney, Player.Main/GetMoney, Player.Main/GetName, Player.Main/GetSession, Player.Main/LogModifyMoney, Player.Main/ModifyMoney, Player.Main/MoveItemFromInventory, Player.Main/Player, Player.Main/SaveInventoryAndGoldToDB, Spell.Main/CheckCast, Spell.Main/Delete, Spell.Main/prepare, Spell.Main/SendCastResult, Spell.Main/SetCastItem, Spell.Main/Spell#2, SpellCastTargetsInfo/operator=, SpellCastTargetsInfo/setTradeItemTarget, SpellCastTargetsInfo/SpellCastTargets, SpellMgr/GetSpellEntry, SpellMgr/Instance, TradeData/FillTransactionLog, TradeData/GetItem, TradeData/GetLastModificationTime, TradeData/GetMoney, TradeData/GetScamPreventionDelay, TradeData/GetSpell, TradeData/GetSpellCastItem, TradeData/GetTrader, TradeData/HasSpellCastItem, TradeData/IsAccepted, TradeData/SetAccepted, TradeData/SetLastModificationTime, TradeData/SetSpell, World/getConfig, World/LogTransaction, WorldObject.Object/GetDistance3dToCenter#3, WorldSession.Main/GetAccountId, WorldSession.Main/GetSecurity, WorldSession.Main/SendNotification#2 | — | — |
| HandleUnacceptTradeOpcode | method | TradeData/SetAccepted | — | — |
| HandleBeginTradeOpcode | method | Player.Main/GetSession, TradeData/GetTrader | — | — |
| SendCancelTrade | method | — | Player.Main/TradeCancel | — |
| HandleCancelTradeOpcode | method | Player.Main/TradeCancel | — | — |
| HandleInitiateTradeOpcode | method | ByteBuffer/operator<<#10, Map.Main/GetPlayer, Object/GetObjectGuid, ObjectGuid/operator<<, Player.Main/GetSession, Player.Main/GetTeam, TradeData/SetScamPreventionDelay, TradeData/TradeData, Unit.Main/HasUnitState, Unit.Main/IsAlive, Unit.Main/IsTaxiFlying, World/getConfig, WorldObject.Object/FindMap, WorldObject.Object/GetDistance3dToCenter#3, WorldObject.Object/GetMap, WorldPacket/WorldPacket#4, WorldSession.Main/GetPlayer, WorldSession.Main/HasTrialRestrictions, WorldSession.Main/IsLogingOut, WorldSession.Main/SendPacket | — | — |
| HandleSetTradeGoldOpcode | method | Player.Main/GetMoney, Player.Main/GetTradeData, TradeData/GetTrader, TradeData/SetAccepted, TradeData/SetLastModificationTime, TradeData/SetMoney | — | — |
| HandleSetTradeItemOpcode | method | game_Objects_Item/CanBeTraded, Object/GetObjectGuid, Player.Main/GetItemByPos, Player.Main/GetTradeData, Player.Main/IsBankPos, TradeData/GetTrader, TradeData/HasItem, TradeData/SetAccepted, TradeData/SetItem, TradeData/SetLastModificationTime | — | — |
| HandleClearTradeItemOpcode | method | Player.Main/GetTradeData, TradeData/GetTrader, TradeData/SetAccepted, TradeData/SetItem, TradeData/SetLastModificationTime | — | — |

---

<!-- verify: boundary-bleed | foreign: process, WorldSession -->
