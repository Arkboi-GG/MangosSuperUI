<!-- provenance: boundary-bleed -->
# WorldSession.ItemHandler

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# WorldSession.ItemHandler

## Purpose & Responsibilities

The `WorldSession.ItemHandler` partial of the `WorldSession` class serves as the network-facing controller for all item-related interactions in the WoWVMaNGOS server. It is responsible for receiving opcode packets from the client that manipulate the player's inventory, equipment, bank, and vendor transactions.

Its primary responsibilities include:
1.  **Input Validation:** Verifying that client requests are logically consistent (e.g., source and destination slots are valid, items exist, player has permission to access banks or vendors).
2.  **Anti-Cheat Enforcement:** Detecting and reporting attempts to exploit inventory mechanics, such as swapping items into bank slots while out of range or duplicating items via rapid packet sequences.
3.  **Delegation:** Delegating the actual state changes (moving, equipping, destroying, buying, selling) to the `Player` class methods, which handle the complex rules of inventory management, quest updates, and database persistence.
4.  **Response Generation:** Constructing and sending appropriate server-to-client packets (`SMSG_*`) to confirm actions, report errors, or provide item data queries.

This unit does not store item state itself; it acts as a secure gateway between the untrusted client input and the trusted `Player` object's inventory logic.

## Member-by-Member Behavior

### Inventory Manipulation (Split, Swap, Equip, Destroy)

These methods handle direct manipulation of items within the player's personal inventory and equipment slots.

**HandleSplitItemOpcode**
Processes a request to split a stack of items. It validates that the source and destination positions are distinct and valid. It checks if the source position contains an item and if the destination is a valid storage location (including auto-store positions). If valid, it delegates to `Player.Main/SplitItem`.

**HandleSwapInvItemOpcode**
Handles swapping two items within the main inventory (bag slot 0). It includes specific anti-cheat logic: if either the source or destination is a bank slot, it verifies the player can currently use the bank (`Player.Main/CanUseBank`). If not, it reports a cheat action via `WorldSession.Main/ProcessAnticheatAction`. It prevents swapping an item to its current position to mitigate certain client-side cheating sequences.

**HandleAutoEquipItemSlotOpcode**
Processes a request to automatically equip an item into a specific slot. It first verifies that the destination slot is a valid equipment slot. It retrieves the item by GUID and ensures the item is not already in that slot. It then delegates the swap to `Player.Main/SwapItem`.

**HandleSwapItem**
A general-purpose item swap handler for any two inventory positions (bags, equipment, bank). Like `HandleSwapInvItemOpcode`, it enforces bank access restrictions and anti-cheat measures. It validates both source and destination positions before delegating to `Player.Main/SwapItem`.

**HandleAutoEquipItemOpcode**
Handles the complex logic of equipping an item when the target slot might already be occupied.
1.  It determines if the item can be equipped using `Player.Main/CanEquipItem`.
2.  If the target slot is empty, it simply removes the item from the source and equips it.
3.  If the target slot is occupied, it performs a multi-step validation:
    *   Checks if the currently equipped item can be unequipped.
    *   Determines where the displaced item should go (inventory, bank, or another equipment slot) by checking `Player.Main/CanStoreItem`, `Player.Main/CanBankItem`, or `Player.Main/CanEquipItem` recursively.
    *   If all checks pass, it executes the removals and re-equips/stores the items in the correct order.
4.  Finally, if the newly equipped item is a bag, it sends a command to open the container via `Player.Main/SendOpenContainer`.

**HandleDestroyItemOpcode**
Processes a request to destroy (drop/delete) an item.
1.  It prevents dropping unequippable items or non-empty bags unless they can be unequipped.
2.  It checks if the item is indestructible (`ITEM_FLAG_INDESTRUCTIBLE`) and rejects the action if so.
3.  It delegates the destruction to `Player.Main/DestroyItemCount` (for partial stacks) or `Player.Main/DestroyItem` (for full stacks).

### Vendor Interactions (Buy, Sell, List, Buyback)

These methods manage transactions with NPC vendors.

**HandleSellItemOpcode**
Processes selling an item to a vendor.
1.  Validates the vendor exists and is interactable via `Player.Main/GetNPCIfCanInteractWith`.
2.  Removes "fake death" state if present via `Unit.Main/RemoveSpellsCausingAura`.
3.  Validates ownership, ensures the item is not in the bank, not currently being looted, and not a non-empty bag.
4.  Calculates the sell price based on the item's prototype price.
5.  Adjusts the price for durability loss. If the item has lost durability, it calculates the repair cost and subtracts it from the sell price. If the repair cost exceeds the base sell price, the final price is set to 1 copper.
6.  Handles partial stack sales by cloning the item (`game_Objects_Item/CloneItem`) for the buyback slot and reducing the count of the original.
7.  Adds the sold item to the player's buyback slot (`Player.Main/AddItemToBuyBackSlot`) and logs the money modification via `Player.Main/LogModifyMoney`.

**HandleBuybackItem**
Allows a player to repurchase an item they recently sold.
1.  Validates the vendor and removes fake death state.
2.  Retrieves the item from the specified buyback slot via `Player.Main/GetItemFromBuyBackSlot`.
3.  Checks if the player has enough money via `Player.Main/GetMoney`.
4.  Validates that the item can be stored in the inventory via `Player.Main/CanStoreItem`.
5.  Deducts money via `Player.Main/ModifyMoney`, removes the item from the buyback slot, updates quest checks via `Player.Main/ItemAddedQuestCheck`, and stores the item via `Player.Main/StoreItem`.

**HandleBuyItemInSlotOpcode**
Buys an item from a vendor and places it in a specific bag/slot.
1.  Resolves the bag GUID to a bag index by iterating through the player's bags.
2.  Delegates the purchase to `Player.Main/BuyItemFromVendor`.

**HandleBuyItemOpcode**
Buys an item from a vendor with automatic storage. Delegates directly to `Player.Main/BuyItemFromVendor` with null bag/slot parameters.

**HandleListInventoryOpcode**
Requests the list of items a vendor sells. It checks if the player is alive via `Unit.Main/IsAlive` and then calls `SendListInventory`.

**SendListInventory**
Generates the `SMSG_LIST_INVENTORY` packet.
1.  Validates the vendor via `Player.Main/GetNPCIfCanInteractWith` and removes fake death state.
2.  Pauses the vendor's movement if applicable via `Creature.MotionMaster/PauseOutOfCombatMovement`.
3.  Interrupts channeling spells on the player via `SpellCaster/InterruptSpellsWithChannelFlags` and `Unit.Main/RemoveAurasWithInterruptFlags`.
4.  Retrieves vendor items from both custom data and template data via `Creature.Main/GetVendorItems` and `Creature.Main/GetVendorTemplateItems`.
5.  Filters items based on player class, race, reputation, and conditions (`Conditions/IsConditionSatisfied`).
6.  Calculates prices with reputation discounts via `Player.Main/GetReputationPriceDiscount`.
7.  Constructs the packet with item counts, display IDs, and prices, capped at `MAX_VENDOR_ITEMS`, and sends it via `WorldSession.Main/SendPacket`.

### Bank Interactions

**CheckBanker**
Validates whether a player can interact with a banker NPC or use the bank command.
1.  If the GUID matches the player's own GUID, it checks if the player has the `bank` chat command permission (GM feature) via `ChatHandler.Chat/FindCommand#2`.
2.  Otherwise, it checks if the GUID corresponds to an interactable NPC with the `UNIT_NPC_FLAG_BANKER` flag via `Player.Main/GetNPCIfCanInteractWith`.

**HandleBuyBankSlotOpcode**
Processes purchasing an additional bank bag slot.
1.  Validates the banker via `CheckBanker`.
2.  Looks up the price for the next bank slot from `sBankBagSlotPricesStore`.
3.  Checks if the player has sufficient funds via `Player.Main/GetMoney`.
4.  Updates the player's bank slot count via `Player.Main/SetBankBagSlotCount` and deducts money via `Player.Main/ModifyMoney`.

**HandleAutoBankItemOpcode**
Automatically moves an item from inventory to the bank.
1.  Validates bank access via `Player.Main/CanUseBank`.
2.  Determines a valid destination in the bank via `Player.Main/CanBankItem`.
3.  Removes the item from inventory and banks it via `Player.Main/BankItem`.

**HandleAutoStoreBankItemOpcode**
Moves an item between inventory and bank automatically.
1.  Validates bank access via `Player.Main/CanUseBank`.
2.  If moving from bank to inventory, it uses `Player.Main/CanStoreItem` and `Player.Main/StoreItem`.
3.  If moving from inventory to bank, it uses `Player.Main/CanBankItem` and `Player.Main/BankItem`.
4.  Updates quest checks via `Player.Main/ItemAddedQuestCheck` if an item is added to inventory.

### Item Queries & Miscellaneous

**HandleItemQuerySingleOpcode**
Responds to a client request for detailed information about a specific item prototype.
1.  Retrieves the `ItemPrototype` from `ObjectMgr/GetItemPrototype`.
2.  Handles localization by fetching localized names/descriptions via `ObjectMgr/GetItemLocale` if available.
3.  Constructs a large packet containing all static item data: class, subclass, name, display ID, quality, flags, prices, stats, damage, armor, resistances, spells (with cooldowns from `SpellMgr/GetSpellEntry`), bonding, page text, etc.
4.  Sends the packet to the client via `WorldSession.Main/SendPacket`.

**HandleReadItemOpcode**
Handles reading a page text from an item.
1.  Retrieves the item and checks if it has page text.
2.  Validates if the player can use the item via `Player.Main/CanUseItem`.
3.  Sends either `SMSG_READ_ITEM_OK` or `SMSG_READ_ITEM_FAILED` via `WorldSession.Main/SendPacket`.

**HandlePageQuerySkippedOpcode**
A stub handler that reads and discards packet data. It appears to be a placeholder or legacy handler for page text skipping.

**HandleSetAmmoOpcode**
Sets or clears the player's active ammo type.
1.  Checks if the player is alive via `Unit.Main/IsAlive`.
2.  If an item entry is provided, checks if the player possesses it via `Player.Main/GetItemCount`.
3.  Sets or removes the ammo via `Player.Main/SetAmmo` or `Player.Main/RemoveAmmo`.

**SendItemEnchantTimeUpdate**
Sends a packet to update the client on the remaining duration of an enchantment on an item. Called by `Player.Main/AddEnchantmentDuration` and `Player.Main/SendEnchantmentDurations`.

**HandleItemNameQueryOpcode**
Responds to a query for an item's name.
1.  Retrieves the prototype via `ObjectMgr/GetItemPrototype` and localized name via `ObjectMgr/GetItemLocale`.
2.  Sends the name in `SMSG_ITEM_NAME_QUERY_RESPONSE` via `WorldSession.Main/SendPacket`.

**HandleWrapItemOpcode**
Handles wrapping an item as a gift.
1.  Validates the wrapper item and the item to be wrapped.
2.  Ensures the item is not equipped, already wrapped, soulbound, stackable, or unique.
3.  Prevents wrapping while casting a spell via `SpellCaster/IsNonMeleeSpellCasted` to avoid exploits.
4.  Inserts a record into the `character_gifts` database table via `Database/PExecute#2`.
5.  Changes the item's entry to the wrapped gift ID, sets the gift creator GUID, and marks it as wrapped.
6.  Saves the inventory to the database via `Player.Main/SaveInventoryAndGoldToDB` if the item is new.
7.  Destroys the wrapper item via `Player.Main/DestroyItemCount`.

## Cross-Unit Boundaries

### Collaboration with Player.Main
The `WorldSession.ItemHandler` relies heavily on `Player.Main` for all stateful operations. The session layer validates inputs and permissions, while the player layer executes the changes.
*   **Validation:** Methods like `Player.Main/IsValidPos`, `Player.Main/CanUseBank`, `Player.Main/CanEquipItem`, and `Player.Main/CanStoreItem` are called to ensure the requested action is legal before execution.
*   **Execution:** Methods like `Player.Main/SwapItem`, `Player.Main/EquipItem`, `Player.Main/DestroyItem`, `Player.Main/BuyItemFromVendor`, and `Player.Main/SellItem` perform the actual inventory modifications.
*   **Feedback:** `Player.Main/SendEquipError` and `Player.Main/SendSellError` are used to notify the client of failures.

### Collaboration with game_Objects_Item
Direct access to `Item` objects is used for reading properties that are not exposed through high-level player methods or for low-level manipulations.
*   **Properties:** `game_Objects_Item/GetProto`, `game_Objects_Item/GetCount`, `game_Objects_Item/IsBag`, `game_Objects_Item/IsEquipped`, `game_Objects_Item/IsSoulBound` are used to inspect item states.
*   **Manipulation:** `game_Objects_Item/SetState`, `game_Objects_Item/SetCount`, `game_Objects_Item/CloneItem` are used in specific scenarios like selling partial stacks or wrapping gifts.

### Collaboration with ObjectMgr & SpellMgr
*   **ObjectMgr:** `ObjectMgr/GetItemPrototype` and `ObjectMgr/GetItemLocale` are used to fetch static item data for queries.
*   **SpellMgr:** `SpellMgr/GetSpellEntry` is used to retrieve spell cooldown data for items with spell effects.

### Collaboration with WorldSession.Main
*   **Anticheat:** `WorldSession.Main/ProcessAnticheatAction` is called when suspicious inventory movements are detected (e.g., bank access out of range).
*   **Networking:** `WorldSession.Main/SendPacket` is used to send responses to the client.
*   **Session State:** `WorldSession.Main/GetPlayer` and `WorldSession.Main/GetSecurity` are used to access the current player and account privileges.

### Collaboration with Database
*   **character_gifts:** `HandleWrapItemOpcode` inserts a row into the `character_gifts` table to track wrapped items. This allows the server to unwrap items later by referencing the original item data stored in this table.

## Data Model

The unit interacts with one database table:

*   **`character_gifts`**: Used exclusively by `HandleWrapItemOpcode` to store metadata about wrapped items.
    *   `guid`: The counter part of the owner's GUID.
    *   `item_guid`: The primary key, linking to the specific item instance.
    *   `item_id`: The original item entry ID before wrapping.
    *   `flags`: The original item flags.

No other database tables are accessed by this unit. All other inventory changes are handled by the `Player` class, which manages its own database persistence.

## Notable Implementation Details

1.  **Durability-Based Sell Price Adjustment:** In `HandleSellItemOpcode`, the sell price is dynamically adjusted based on the item's current durability. If the item has lost durability, the cost to repair it is subtracted from the sell price. This prevents players from selling heavily damaged items for full value. If the repair cost exceeds the base sell price, the item sells for 1 copper.

2.  **Anti-Cheat Bank Access:** Several methods (`HandleSwapInvItemOpcode`, `HandleSwapItem`, `HandleAutoStoreBagItemOpcode`) explicitly check if an item is being moved to or from a bank slot. If so, they verify `Player.Main/CanUseBank()`. If the player cannot use the bank (e.g., too far away), the action is rejected, and an anticheat report is generated via `WorldSession.Main/ProcessAnticheatAction`. This prevents exploits where clients might send packets to move items into the bank while out of range.

3.  **Partial Stack Selling:** `HandleSellItemOpcode` supports selling a portion of a stack. It achieves this by cloning the item (`game_Objects_Item/CloneItem`) for the buyback slot and reducing the count of the original item in the inventory. This ensures the buyback slot contains the exact items sold, while the player retains the remainder.

4.  **Gift Wrapping Exploit Prevention:** `HandleWrapItemOpcode` includes a check `if (_player->IsNonMeleeSpellCasted(true, false, false))` to prevent wrapping items while a spell is being cast. This mitigates an exploit where players could wrap an item during use, potentially allowing multiple uses or bypassing consumption.

5.  **Localization Handling:** Both `HandleItemQuerySingleOpcode` and `HandleItemNameQueryOpcode` handle localization by checking `WorldSession.Main/GetSessionDbLocaleIndex()` and fetching localized strings from `ObjectMgr/GetItemLocale`. This ensures clients receive item names and descriptions in their preferred language.

6.  **Vendor Item Filtering:** `SendListInventory` filters vendor items based on player class, race, reputation, and conditions. It also applies reputation discounts to prices. This logic ensures that players only see items they can actually buy and at the correct price.

7.  **Buyback Slot Management:** Items sold to vendors are added to the player's buyback slot via `Player.Main/AddItemToBuyBackSlot`. This allows players to repurchase items shortly after selling them. The buyback price is stored and retrieved via `Player.Main/GetBuyBackItemPrice`.

## Member Reference

**HandleSplitItemOpcode**: Validates source/destination positions and delegates item splitting to `Player.Main/SplitItem`.

**HandleSwapInvItemOpcode**: Swaps items in the main inventory, enforcing bank access rules and anti-cheat checks.

**HandleAutoEquipItemSlotOpcode**: Equips an item into a specific slot, validating the slot type and delegating to `Player.Main/SwapItem`.

**HandleSwapItem**: General item swap handler for any inventory positions, with bank access and anti-cheat enforcement.

**HandleAutoEquipItemOpcode**: Complex equip logic handling occupied slots, determining displacement destinations, and executing multi-step swaps.

**HandleDestroyItemOpcode**: Validates and destroys items, preventing destruction of indestructible or unequippable items.

**HandleItemQuerySingleOpcode**: Responds to item detail queries with full prototype data, including localization and spell cooldowns.

**HandleReadItemOpcode**: Handles reading page text from items, validating usage rights.

**HandlePageQuerySkippedOpcode**: Stub handler that discards packet data.

**HandleSellItemOpcode**: Sells items to vendors, calculating price adjustments for durability and handling partial stack sales.

**HandleBuybackItem**: Repurchases items from the vendor's buyback slot, validating funds and storage space.

**HandleBuyItemInSlotOpcode**: Buys an item from a vendor and places it in a specific bag/slot.

**HandleBuyItemOpcode**: Buys an item from a vendor with automatic storage.

**HandleListInventoryOpcode**: Requests the vendor's inventory list.

**SendListInventory**: Generates the vendor inventory packet, filtering items by class/race/reputation and applying discounts.

**HandleAutoStoreBagItemOpcode**: Automatically moves an item to a bag, enforcing bank access rules if involved.

**CheckBanker**: Validates if a player can interact with a banker NPC or use the bank command.

**HandleBuyBankSlotOpcode**: Purchases an additional bank bag slot, validating funds and banker presence.

**HandleAutoBankItemOpcode**: Automatically moves an item from inventory to the bank.

**HandleAutoStoreBankItemOpcode**: Moves an item between inventory and bank automatically.

**HandleSetAmmoOpcode**: Sets or clears the player's active ammo type.

**SendItemEnchantTimeUpdate**: Sends enchantment duration updates to the client.

**HandleItemNameQueryOpcode**: Responds to item name queries with localized data.

**HandleWrapItemOpcode**: Wraps an item as a gift, updating the database and item state, with exploit prevention checks.

---

<!-- machine-true, projected from graph.json -->

## Map — WorldSession.ItemHandler

*Source:* ItemHandler.cpp, WorldSession.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| HandleSplitItemOpcode | method | Player.Main/IsValidPos, Player.Main/SendEquipError, Player.Main/SplitItem | — | — |
| HandleSwapInvItemOpcode | method | Player.Main/CanUseBank, Player.Main/IsBankPos, Player.Main/IsValidPos, Player.Main/SendEquipError, Player.Main/SwapItem, WorldSession.Main/ProcessAnticheatAction | — | — |
| HandleAutoEquipItemSlotOpcode | method | game_Objects_Item/GetPos, Player.Main/GetItemByGuid, Player.Main/IsEquipmentPos, Player.Main/SwapItem | — | — |
| HandleSwapItem | method | Player.Main/CanUseBank, Player.Main/IsBankPos, Player.Main/IsValidPos, Player.Main/SendEquipError, Player.Main/SwapItem, WorldSession.Main/ProcessAnticheatAction | — | — |
| HandleAutoEquipItemOpcode | method | game_Objects_Item/GetBagSlot, game_Objects_Item/GetPos, game_Objects_Item/GetSlot, game_Objects_Item/IsBag, Object/GetObjectGuid, Player.Main/AutoUnequipOffhandIfNeed, Player.Main/BankItem, Player.Main/CanBankItem, Player.Main/CanEquipItem, Player.Main/CanStoreItem, Player.Main/CanUnequipItem, Player.Main/EquipItem, Player.Main/GetItemByPos, Player.Main/GetItemByPos#2, Player.Main/IsBagPos, Player.Main/IsBankPos#2, Player.Main/IsEquipmentPos#2, Player.Main/IsInventoryPos#2, Player.Main/RemoveItem, Player.Main/SendEquipError, Player.Main/SendOpenContainer, Player.Main/StoreItem | — | — |
| HandleDestroyItemOpcode | method | game_Objects_Item/GetProto, Player.Main/CanUnequipItem, Player.Main/DestroyItem, Player.Main/DestroyItemCount, Player.Main/GetItemByPos, Player.Main/GetItemByPos#2, Player.Main/IsBagPos, Player.Main/IsEquipmentPos#2, Player.Main/SendEquipError | — | — |
| HandleItemQuerySingleOpcode | method | ByteBuffer/operator<<#10, ByteBuffer/operator<<#3, ByteBuffer/operator<<#4, ByteBuffer/operator<<#9, ObjectMgr/GetItemLocale, ObjectMgr/GetItemPrototype, SpellMgr/GetSpellEntry, SpellMgr/Instance, WorldPacket/WorldPacket#4, WorldSession.Main/GetSecurity, WorldSession.Main/GetSessionDbLocaleIndex, WorldSession.Main/SendPacket | — | — |
| HandleReadItemOpcode | method | ByteBuffer/operator<<#7, game_Objects_Item/GetProto, Object/GetObjectGuid, ObjectGuid/operator<<, Player.Main/CanUseItem, Player.Main/GetItemByPos, Player.Main/SendEquipError, WorldPacket/Initialize, WorldPacket/WorldPacket, WorldSession.Main/SendPacket | — | — |
| HandlePageQuerySkippedOpcode | method | ByteBuffer/operator>>#9, ObjectGuid/ObjectGuid, ObjectGuid/operator>> | — | — |
| HandleSellItemOpcode | method | Bag/IsEmpty, game_Objects_Item/CloneItem, game_Objects_Item/GetBagSlot, game_Objects_Item/GetCount, game_Objects_Item/GetOwnerGuid, game_Objects_Item/GetPos, game_Objects_Item/GetProto, game_Objects_Item/GetSlot, game_Objects_Item/GetSpellCharges, game_Objects_Item/IsBag, game_Objects_Item/RemoveFromUpdateQueueOf, game_Objects_Item/SetCount, game_Objects_Item/SetState, ItemPrototype/ItemSubClassToDurabilityMultiplierId, Log.Main/Out, Object/GetEntry, Object/GetObjectGuid, Object/GetUInt32Value, Object/IsInWorld, ObjectGuid/GetString, ObjectGuid/operator!, ObjectGuid/operator!=, ObjectGuid/operator==, Player.Main/AddItemToBuyBackSlot, Player.Main/GetItemByGuid, Player.Main/GetLootGuid, Player.Main/GetNPCIfCanInteractWith, Player.Main/InterruptSpellsWithCastItem, Player.Main/IsBankPos#2, Player.Main/ItemRemovedQuestCheck, Player.Main/LogModifyMoney, Player.Main/RemoveItem, Player.Main/SendSellError, Unit.Main/HasUnitState, Unit.Main/RemoveSpellsCausingAura, WorldObject.Object/SendCreateUpdateToPlayer, WorldSession.Main/GetPlayer | — | — |
| HandleBuybackItem | method | game_Objects_Item/GetCount, Log.Main/Out, Object/GetEntry, ObjectGuid/GetString, ObjectGuid/ObjectGuid, Player.Main/CanStoreItem, Player.Main/GetBuyBackItemPrice, Player.Main/GetItemFromBuyBackSlot, Player.Main/GetMoney, Player.Main/GetNPCIfCanInteractWith, Player.Main/ItemAddedQuestCheck, Player.Main/ModifyMoney, Player.Main/RemoveItemFromBuyBackSlot, Player.Main/SendBuyError, Player.Main/SendEquipError, Player.Main/SendSellError, Player.Main/StoreItem, Unit.Main/HasUnitState, Unit.Main/RemoveSpellsCausingAura, WorldSession.Main/GetPlayer | — | — |
| HandleBuyItemInSlotOpcode | method | Object/GetObjectGuid, ObjectGuid/operator==, Player.Main/BuyItemFromVendor, Player.Main/GetItemByPos, WorldSession.Main/GetPlayer | — | — |
| HandleBuyItemOpcode | method | Player.Main/BuyItemFromVendor, WorldSession.Main/GetPlayer | — | — |
| HandleListInventoryOpcode | method | Unit.Main/IsAlive, WorldSession.Main/GetPlayer | — | — |
| SendListInventory | method | ByteBuffer/operator<<#10, ByteBuffer/operator<<#7, ByteBuffer/wpos, Conditions/IsConditionSatisfied, Creature.Main/GetVendorItemCurrentCount, Creature.Main/GetVendorItems, Creature.Main/GetVendorTemplateItems, Creature.Main/HasExtraFlag, Creature.MotionMaster/PauseOutOfCombatMovement, Log.Main/Out, ObjectGuid/GetString, ObjectGuid/ObjectGuid, ObjectGuid/operator<<, ObjectMgr/GetItemPrototype, Player.Main/GetNPCIfCanInteractWith, Player.Main/GetReputationPriceDiscount, Player.Main/GetReputationRank, Player.Main/IsGameMaster, Player.Main/SendSellError, SpellCaster/InterruptSpellsWithChannelFlags, Unit.Main/GetClassMask, Unit.Main/GetRaceMask, Unit.Main/HasUnitState, Unit.Main/RemoveAurasWithInterruptFlags, Unit.Main/RemoveSpellsCausingAura, VendorItemData/GetItem, VendorItemData/GetItemCount, WorldObject.Object/GetFactionId, WorldObject.Object/GetMap, WorldPacket/WorldPacket#4, WorldSession.Main/GetPlayer, WorldSession.Main/SendPacket | Player.Main/OnGossipSelect, ThreatListCopier.battleground_alterac/GossipSelect_npc_AVBlood_collector | — |
| HandleAutoStoreBagItemOpcode | method | game_Objects_Item/GetPos, Player.Main/CanStoreItem, Player.Main/CanUnequipItem, Player.Main/CanUseBank, Player.Main/GetItemByPos, Player.Main/IsBagPos, Player.Main/IsBankPos, Player.Main/IsEquipmentPos#2, Player.Main/IsValidPos, Player.Main/RemoveItem, Player.Main/SendEquipError, Player.Main/StoreItem | — | — |
| CheckBanker | method | ChatHandler.Chat/ChatHandler#2, ChatHandler.Chat/FindCommand#2, Log.Main/Out, Object/GetObjectGuid, ObjectGuid/GetString, ObjectGuid/operator==, Player.Main/GetNPCIfCanInteractWith, WorldSession.Main/GetPlayer | WorldSession.NPCHandler/HandleBankerActivateOpcode | — |
| HandleBuyBankSlotOpcode | method | ByteBuffer/operator<<#10, Player.Main/GetBankBagSlotCount, Player.Main/GetMoney, Player.Main/ModifyMoney, Player.Main/SetBankBagSlotCount, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | — | — |
| HandleAutoBankItemOpcode | method | game_Objects_Item/GetPos, Player.Main/BankItem, Player.Main/CanBankItem, Player.Main/CanUseBank, Player.Main/GetItemByPos, Player.Main/RemoveItem, Player.Main/SendEquipError | — | — |
| HandleAutoStoreBankItemOpcode | method | Object/GetEntry, Player.Main/BankItem, Player.Main/CanBankItem, Player.Main/CanStoreItem, Player.Main/CanUseBank, Player.Main/GetItemByPos, Player.Main/IsBankPos, Player.Main/ItemAddedQuestCheck, Player.Main/RemoveItem, Player.Main/SendEquipError, Player.Main/StoreItem | — | — |
| HandleSetAmmoOpcode | method | Player.Main/GetItemCount, Player.Main/RemoveAmmo, Player.Main/SendEquipError, Player.Main/SetAmmo, Unit.Main/IsAlive, WorldSession.Main/GetPlayer | — | — |
| SendItemEnchantTimeUpdate | method | ByteBuffer/operator<<#10, ObjectGuid/operator<<, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | Player.Main/AddEnchantmentDuration, Player.Main/SendEnchantmentDurations | — |
| HandleItemNameQueryOpcode | method | ByteBuffer/append#4, ByteBuffer/operator<<#10, ObjectMgr/GetItemLocale, ObjectMgr/GetItemPrototype, WorldPacket/WorldPacket#4, WorldSession.Main/GetSessionDbLocaleIndex, WorldSession.Main/SendPacket | — | — |
| HandleWrapItemOpcode | method | Database/BeginTransaction, Database/CommitTransaction, Database/PExecute#2, game_Objects_Item/GetMaxStackCount, game_Objects_Item/GetOwnerGuid, game_Objects_Item/GetProto, game_Objects_Item/GetState, game_Objects_Item/IsBag, game_Objects_Item/IsEquipped, game_Objects_Item/IsSoulBound, game_Objects_Item/SetState, Object/GetEntry, Object/GetGUIDLow, Object/GetGuidValue, Object/GetObjectGuid, Object/GetUInt32Value, Object/SetEntry, Object/SetGuidValue, ObjectGuid/GetCounter, Player.Main/DestroyItemCount, Player.Main/GetItemByPos, Player.Main/SaveInventoryAndGoldToDB, Player.Main/SendEquipError, SpellCaster/IsNonMeleeSpellCasted, WorldObject.Object/SetUInt32Value | — | character_gifts |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `character_gifts`: guid int(20) unsigned, item_guid int(11) unsigned PK, item_id int(20) unsigned, flags int(20) unsigned

*`?` = nullable, `PK` = primary key column.*


---

<!-- verify: boundary-bleed | foreign: update, WorldSession -->
