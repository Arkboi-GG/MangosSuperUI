# game_Objects_Item

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Item

The `Item` class represents a single instance of an item within the game world, such as a sword in a player's inventory, a potion in a bag, or a quest item held by a character. It inherits from `Object`, providing the foundational identity (GUID, entry ID) and network synchronization capabilities, while adding specific logic for inventory management, durability, enchantments, loot generation, and database persistence.

This unit handles the lifecycle of an item: creation, modification (stacking, enchanting, durability loss), movement between inventory slots and bags, and deletion. It also manages the complex state machine required to synchronize item changes with the client via update queues and to persist those changes to the `character` database.

Key responsibilities include:
*   **Inventory State:** Tracking which bag and slot an item occupies, whether it is equipped, and its stack count.
*   **Attributes:** Managing dynamic properties like enchantments, random suffixes (e.g., "+5 Strength"), durability, and temporary durations.
*   **Loot Handling:** Acting as a container for loot tables when an item is opened (e.g., a chest or quest item), managing the state of generated loot before it is claimed.
*   **Persistence:** Saving and loading item instances from the `item_instance`, `item_loot`, and related tables.
*   **Set Bonuses:** Assisting the `Player` class in applying and removing item set bonuses when items are equipped or unequipped.

## Member-by-Member Behavior

### Item Lifecycle and Creation
*   **Item**: The constructor initializes the object as an `ITEM` type, sets the initial state to `ITEM_NEW`, and clears pointers to containers and loot.
*   **Create**: Initializes the item's core data fields using its prototype. It sets the owner GUID, stack count to 1, and initializes durability and spell charges based on the `ItemPrototype`. It marks the prototype as "discovered" in the global object manager.
*   **CreateItem**: A static factory method that creates a new `Item` instance, generates a unique low GUID, and calls `Create`. It ensures the stack count does not exceed the prototype's maximum.
*   **CloneItem**: Creates a duplicate of the current item, copying all dynamic properties (enchantments, random properties, flags, duration) but generating a new GUID. Used for splitting stacks or creating mail attachments.
*   **RemoveFromWorld**: Interrupts any spells cast by the item's owner that require this item as a reagent or focus, then removes the object from the world map.
*   **ChangeEntry**: Changes the item's entry ID to a new prototype, updating spell charges and random properties accordingly. Used primarily for race-specific item swaps.

### Inventory Management and Positioning
*   **GetOwnerGuid** / **SetOwnerGuid**: Accessors for the GUID of the player who owns this item.
*   **GetSlot** / **SetSlot**: Accessors for the specific slot index within the container (bag or equipment slot).
*   **GetContainer** / **SetContainer**: Accessors for the `Bag` object containing this item. If `nullptr`, the item is in the main equipment slots.
*   **GetBagSlot**: Returns the slot index of the bag containing this item. If the item is not in a bag, it returns the base inventory bag slot constant.
*   **GetPos**: Combines the bag slot and item slot into a single 16-bit position value used by the client and server for inventory operations.
*   **IsInBag**: Returns true if the item is inside a bag (i.e., `m_container` is not null).
*   **IsEquipped**: Returns true if the item is in an equipment slot (not in a bag) and the slot index is valid for equipment.
*   **SetInTrade** / **IsInTrade**: Flags indicating if the item is currently being offered in a trade window. This prevents other operations (like selling or using) while trading.
*   **CanBeTraded**: Determines if an item can be placed in a trade window. It checks if the item is soulbound, if it's a non-empty bag, if it's bound by enchantment, or if it has unclaimed loot. It also verifies the owner can unequip it.
*   **ItemCanGoIntoBag**: A static helper function that determines if a specific item prototype can fit into a specific bag prototype, respecting bag families (e.g., herbs only go in herb bags).

### Item Properties and Attributes
*   **GetProto**: Retrieves the static `ItemPrototype` definition for this item's entry ID.
*   **GetCount** / **SetCount**: Accessors for the stack count of the item.
*   **GetMaxStackCount**: Returns the maximum stack size defined in the item's prototype.
*   **isWeapon** / **isOneHandedWeapon**: Checks if the item is a weapon and specifically if it is a one-handed weapon type.
*   **IsBag**: Checks if the item's inventory type is a bag.
*   **IsBroken**: Returns true if the item has durability and its current durability is zero.
*   **GetItemRandomPropertyId** / **SetItemRandomProperties**: Manages the random suffix ID (e.g., "of Strength"). Setting this applies the corresponding enchantments from the `ItemRandomProperties` DBC.
*   **GenerateItemRandomPropertyId**: Static method that selects a random property ID based on the item's prototype definition.
*   **GetItemSuffixFactor**: Returns the seed value used for random stat generation.
*   **GetEnchantmentId** / **SetEnchantment**: Accessors and mutators for permanent and temporary enchantments. `SetEnchantment` updates the internal data fields and notifies the owner player to log the change.
*   **GetEnchantmentDuration** / **SetEnchantmentDuration**: Manages the remaining time on temporary enchantments.
*   **GetEnchantmentCharges** / **SetEnchantmentCharges**: Manages the number of uses remaining for charge-based enchantments.
*   **ClearEnchantment**: Removes an enchantment from a specific slot, notifying the client if requested.
*   **IsBoundByEnchant**: Checks if any active enchantment on the item has the "Soulbound" flag, effectively binding the item even if it wasn't originally soulbound.
*   **IsSoulBound**: Checks if the item has the dynamic bound flag set.
*   **SetBinding**: Applies or removes the soulbound flag.
*   **IsBindedNotWith**: Checks if the item is bound to a different player than the specified one.
*   **GetSpellCharges** / **SetSpellCharges**: Manages the charges for item-use spells (e.g., a health potion with 5 charges).
*   **UpdateDuration**: Decrements the item's duration timer. If the duration expires, it destroys the item. Used for consumables with timers (e.g., flares).
*   **SendTimeUpdate**: Sends a packet to the client to update the visual timer for duration-based items.

### Loot Handling
*   **loot**: A public member variable of type `Loot` that holds the generated loot table for this item.
*   **HasGeneratedLoot**: Returns true if the item has a non-empty loot table that is not marked as removed.
*   **HasTemporaryLoot**: Returns true if the loot is temporary (not saved to DB).
*   **HasSavedLoot**: Returns true if the loot has been persisted to the database.
*   **SetLootState**: Manages the state machine for loot persistence (`NEW`, `CHANGED`, `UNCHANGED`, `REMOVED`, `TEMPORARY`). It triggers item state changes to ensure loot is saved correctly.
*   **SetGeneratedLoot** / **HasGeneratedLootSecondary**: Flags indicating if the item has generated loot, used for secondary checks.
*   **LoadLootFromDB**: Loads loot entries from the `item_loot` table into the `loot` object.

### Spell and Requirement Checks
*   **IsFitToSpellRequirements**: Checks if the item meets the class, subclass, and inventory type requirements of a specific spell. Includes a hardcoded exception for a specific cloak enchantment spell.
*   **IsTargetValidForItemUse**: Checks if a target unit is valid for an item's use, based on required target maps (e.g., "must target a dead creature").
*   **IsLimitedToAnotherMapOrZone**: Checks if the item is restricted to a different map or zone than the current one.
*   **GetAllowedEquipSlots**: Static method on `ItemPrototype` that returns the valid equipment slots for an item based on its inventory type and class restrictions (e.g., relics for specific classes).
*   **GetProficiencySkill** / **GetProficiencySpell**: Static methods on `ItemPrototype` that return the skill ID or spell ID associated with using the item (e.g., Sword Skill).

### Persistence and Database Operations
*   **SaveToDB**: Persists the item's state to the `item_instance` table. If the item is wrapped, it updates the `character_gifts` table. It also handles saving or deleting loot from the `item_loot` table based on the loot state.
*   **LoadFromDB**: Reconstructs an item from database fields. It validates data integrity (e.g., fixing incorrect binding flags or durability values) and saves corrections if necessary.
*   **DeleteFromDB**: Deletes the item record from `item_instance`.
*   **DeleteFromInventoryDB**: Deletes the item's inventory slot record from `character_inventory`.
*   **DeleteAllFromDB**: Deletes the item from `item_instance`, `item_loot`, `character_gifts`, and `item_text` tables.
*   **DeleteAllFromDB (static)**: A static utility to clean up all references to an item GUID across multiple tables (`item_instance`, `character_inventory`, `auction`, `mail_items`, `character_gifts`) and guild petitions.
*   **LoadLootFromDB**: Loads loot data from the `item_loot` table.

### Network Synchronization
*   **SetState**: Updates the item's internal state (`NEW`, `CHANGED`, `REMOVED`, `UNCHANGED`) and adds it to the owner's update queue if necessary.
*   **AddToUpdateQueueOf** / **RemoveFromUpdateQueueOf**: Manages the item's presence in the player's update queue, ensuring changes are sent to the client.
*   **IsInUpdateQueue** / **GetQueuePos**: Checks if the item is pending an update and retrieves its position in the queue.
*   **GetState**: Returns the current update state.
*   **FSetState**: Forces a state change without triggering queue updates (used during loading/saving).
*   **AddToClientUpdateList** / **RemoveFromClientUpdateList**: Registers the item with the map's update system for periodic synchronization.
*   **BuildUpdateData**: Constructs the update packets for the item to be sent to the owner.

### Item Set Bonuses
*   **AddItemsSetItem**: A free function that increments the count of items in a set for the owner player. If the count meets the threshold for a set bonus spell, it applies the spell.
*   **RemoveItemsSetItem**: A free function that decrements the count of items in a set. If the count drops below the threshold for a bonus, it removes the associated spell.

## Cross-Unit Boundaries

*   **Player.Main**: The `Item` class interacts heavily with `Player` for inventory management (`EquipItem`, `MoveItemToInventory`), applying modifiers (`_ApplyAllItemMods`), and handling set bonuses (`AddItemsSetItem`, `RemoveItemsSetItem`). The `Player` class is the primary owner and manipulator of `Item` instances.
*   **Bag**: Items are contained within `Bag` objects. `Bag` calls `Item` methods to store, remove, and query items (`StoreItem`, `RemoveItem`). `Item` calls `Bag` to determine its container slot.
*   **Spell.Main/Effects**: Spells interact with items for reagents (`CheckItems`, `TakeCastItem`), enchantments (`EffectEnchantItemPerm`), and summoning (`EffectSummonChangeItem`). `Item` provides data on counts, charges, and requirements.
*   **WorldSession.ItemHandler/SpellHandler/TradeHandler**: These session handlers process client opcodes for item usage, trading, and selling. They call `Item` methods to validate actions (`CanBeTraded`, `IsSoulBound`) and update state (`SetCount`, `SetSlot`).
*   **AuctionHouseMgr/Mail**: Items are moved through auctions and mail. These managers call `Item` methods to clone items, update counts, and save/load state.
*   **ObjectMgr**: `Item` relies on `ObjectMgr` to retrieve static data (`GetItemPrototype`, `GetItemLocale`) and generate GUIDs.
*   **Database**: `Item` directly executes SQL statements to persist its state to `item_instance`, `item_loot`, `character_gifts`, and `item_text` tables.

## Data Model

The `Item` class persists data to the following tables:

*   **`item_instance`**: Stores the core instance data for each item.
    *   `guid`: Primary key, unique identifier for the item instance.
    *   `item_id`: Links to the static item definition.
    *   `owner_guid`: The player who owns the item.
    *   `creator_guid` / `gift_creator_guid`: Tracks who created or gifted the item.
    *   `count`: Stack size.
    *   `duration`: Remaining time for timed items.
    *   `charges`: Space-separated string of spell charges.
    *   `flags`: Dynamic flags (e.g., bound, wrapped).
    *   `enchantments`: Space-separated string of enchantment IDs, durations, and charges.
    *   `random_property_id`: ID for random suffixes.
    *   `durability`: Current durability.
    *   `text`: ID for attached text items.
    *   `generated_loot`: Flag indicating if loot has been generated.
*   **`item_loot`**: Stores loot tables for items that have been opened but not fully claimed.
    *   `guid`: Links to the parent item instance.
    *   `owner_guid`: The player who owns the loot.
    *   `item_id`: The ID of the looted item (0 for gold).
    *   `amount`: Quantity of the looted item.
    *   `property`: Random property ID for the looted item.
*   **`character_gifts`**: Tracks items that are wrapped as gifts.
    *   `item_guid`: The GUID of the wrapped item.
    *   `guid`: The GUID of the recipient (or wrapper).
*   **`item_text`**: Stores text content for items that carry messages.
    *   `id`: Primary key.
    *   `text`: The message content.
*   **`character_inventory`**: Tracks the slot location of items in a player's inventory. `Item` deletes records here when an item is removed from inventory.
*   **`auction`**, **`mail_items`**: Referenced during cleanup operations to ensure no dangling references remain when an item is deleted.

## Notable Implementation Details

*   **Hardcoded Spell Exception**: In `IsFitToSpellRequirements`, there is a hardcoded check for spell ID 13419 ("Enchant Cloak - Minor Agility") to bypass standard class/subclass checks. This suggests a data inconsistency in the spell database that was patched in code.
*   **Loot State Machine**: The loot handling uses a complex state machine (`ITEM_LOOT_NEW`, `ITEM_LOOT_CHANGED`, etc.) to determine when to save loot to the database. Temporary loot is never saved, while persistent loot is saved on state changes.
*   **Update Queue Optimization**: Items are added to a player's update queue only when their state changes. The `AddToUpdateQueueOf` method checks if the item is already in the queue to prevent duplicates.
*   **Durability Correction on Load**: `LoadFromDB` actively corrects durability values if they exceed the prototype's maximum, ensuring data integrity.
*   **Wrapped Item Validation**: When loading a wrapped item, the code verifies that the item is actually a wrapper type and not stackable. If not, it removes the wrapped flag and cleans up the `character_gifts` table.
*   **Thread Safety Note**: The code assumes single-threaded access to item instances per player, as there are no locks around state modifications. This is typical for game servers where each player's context is processed sequentially.

## Member Reference

**AddItemsSetItem**: Increments the item set count for the owner player and applies set bonus spells if thresholds are met. Logs errors if the set entry is missing.

**GetOwnerGuid**: Returns the GUID of the player who owns this item.

**SetOwnerGuid**: Sets the GUID of the player who owns this item.

**SetBinding**: Applies or removes the soulbound flag from the item's dynamic flags.

**IsSoulBound**: Returns true if the item has the soulbound flag set.

**isWeapon**: Returns true if the item's class is a weapon.

**isOneHandedWeapon**: Returns true if the item is a one-handed weapon (axe, sword, mace, fist, dagger, exotic).

**IsBag**: Returns true if the item's inventory type is a bag.

**IsBroken**: Returns true if the item has durability and its current durability is zero.

**SetInTrade**: Sets the flag indicating the item is in a trade window.

**IsInTrade**: Returns true if the item is currently in a trade window.

**GetCount**: Returns the stack count of the item.

**RemoveItemsSetItem**: Decrements the item set count for the owner player and removes set bonus spells if thresholds are no longer met. Logs errors if the set entry is missing.

**SetCount**: Sets the stack count of the item.

**GetMaxStackCount**: Returns the maximum stack size defined in the item's prototype.

**GetSlot**: Returns the slot index of the item within its container.

**GetContainer**: Returns the `Bag` object containing this item.

**SetSlot**: Sets the slot index of the item within its container.

**GetPos**: Returns a combined 16-bit value representing the bag slot and item slot.

**SetContainer**: Sets the `Bag` object containing this item.

**IsInBag**: Returns true if the item is inside a bag.

**GetItemRandomPropertyId**: Returns the random property ID (suffix) of the item.

**GetItemSuffixFactor**: Returns the seed value for random stat generation.

**GetEnchantmentId**: Returns the ID of the enchantment in the specified slot.

**GetEnchantmentDuration**: Returns the remaining duration of the enchantment in the specified slot.

**GetEnchantmentCharges**: Returns the remaining charges of the enchantment in the specified slot.

**GetSpellCharges**: Returns the charges for the item-use spell at the specified index.

**SetSpellCharges**: Sets the charges for the item-use spell at the specified index.

**HasGeneratedLoot**: Returns true if the item has a non-empty, non-removed loot table.

**HasTemporaryLoot**: Returns true if the item has temporary loot that is not saved to the database.

**HasSavedLoot**: Returns true if the item has loot that has been persisted to the database.

**GetState**: Returns the current update state of the item.

**ItemCanGoIntoBag**: Static function that checks if an item prototype can fit into a bag prototype based on bag family restrictions.

**IsInUpdateQueue**: Returns true if the item is currently in the owner's update queue.

**GetQueuePos**: Returns the position of the item in the owner's update queue.

**FSetState**: Forces the item's update state without triggering queue updates.

**HasQuest**: Returns true if the item starts the specified quest.

**HasInvolvedQuest**: Always returns false.

**IsConjuredConsumable**: Returns true if the item is a conjured consumable.

**SetGeneratedLoot**: Sets the flag indicating if the item has generated loot.

**HasGeneratedLootSecondary**: Returns the secondary flag for generated loot.

**IsCharter**: Returns true if the item is a guild charter (entry ID 5863).

**Item**: Constructor that initializes the item object.

**Create**: Initializes the item's data fields from its prototype.

**RemoveFromWorld**: Interrupts owner's spells using this item and removes it from the world.

**UpdateDuration**: Decrements the item's duration timer and destroys it if expired.

**SaveToDB**: Persists the item's state and loot to the database.

**LoadFromDB**: Reconstructs the item from database fields, validating and correcting data.

**DeleteAllFromDB**: Deletes the item from all related database tables.

**DeleteAllFromDB#2**: Static utility to clean up all references to an item GUID across multiple tables.

**LoadLootFromDB**: Loads loot entries from the `item_loot` table.

**DeleteFromDB**: Deletes the item record from `item_instance`.

**DeleteFromInventoryDB**: Deletes the item's inventory slot record from `character_inventory`.

**GetProto**: Retrieves the static `ItemPrototype` for this item.

**GetOwner**: Retrieves the `Player` object who owns this item.

**GetAllowedEquipSlots**: Static method that returns valid equipment slots for an item prototype.

**GetProficiencySkill**: Static method that returns the skill ID for an item prototype.

**GetProficiencySpell**: Static method that returns the proficiency spell ID for an item prototype.

**GenerateItemRandomPropertyId**: Static method that selects a random property ID for an item.

**SetItemRandomProperties**: Applies random suffix enchantments to the item.

**SetState**: Updates the item's update state and manages the update queue.

**AddToUpdateQueueOf**: Adds the item to the owner's update queue.

**RemoveFromUpdateQueueOf**: Removes the item from the owner's update queue.

**GetBagSlot**: Returns the slot index of the bag containing this item.

**IsEquipped**: Returns true if the item is in an equipment slot.

**CanBeTraded**: Checks if the item can be traded based on binding, loot, and bag status.

**IsBoundByEnchant**: Checks if any enchantment on the item makes it soulbound.

**IsFitToSpellRequirements#2**: Checks if the item meets spell requirements using prototype data.

**IsFitToSpellRequirements**: Checks if the item meets spell requirements using provided class/subclass/type.

**IsTargetValidForItemUse**: Checks if a target unit is valid for the item's use.

**SetEnchantment**: Sets an enchantment on the item, notifying the client.

**SetEnchantmentDuration**: Sets the duration of an enchantment.

**SetEnchantmentCharges**: Sets the charges of an enchantment.

**ClearEnchantment**: Removes an enchantment from the item.

**IsLimitedToAnotherMapOrZone**: Checks if the item is restricted to a different map or zone.

**SendTimeUpdate**: Sends a packet to the client to update the item's duration timer.

**CreateItem**: Static factory method to create a new item instance.

**CloneItem**: Creates a duplicate of the current item.

**IsBindedNotWith**: Checks if the item is bound to a different player.

**AddToClientUpdateList**: Registers the item with the map's update system.

**RemoveFromClientUpdateList**: Unregisters the item from the map's update system.

**BuildUpdateData**: Constructs update packets for the item.

**CanBeMergedPartlyWith**: Checks if the item can be partially merged with another item prototype.

**IsFitToRequirements**: Checks if a target unit fits the item's target requirements.

**SetLootState**: Manages the loot persistence state machine.

**ChangeEntry**: Changes the item's entry ID to a new prototype.

**GetLocalizedNameWithSuffix**: Generates the localized name of the item with its random suffix.

---

<!-- machine-true, projected from graph.json -->

## Map — game_Objects_Item

*Source:* Item.cpp, Item.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| AddItemsSetItem | function | Log.Main/Out, Player.Main/AddItemSetEffect, Player.Main/ApplyEquipSpell, Player.Main/GetItemSetEffect, Player.Main/GetSkillValue, SpellMgr/GetSpellEntry, SpellMgr/Instance | Player.Main/EquipItem, Player.Main/_ApplyAllItemMods | — |
| GetOwnerGuid | method | — | Bag/StoreItem, ChatHandler.DebugCommands/HandleDebugGetItemStateCommand, Player.Main/MoveItemToInventory, Player.Main/_SaveInventory, Spell.Effects/EffectSummonChangeItem, Spell.Main/CheckItems, Spell.Main/IgnoreItemRequirements, WorldObject.Object/GetUpdateFieldFlagsForTarget, WorldSession.ItemHandler/HandleSellItemOpcode, WorldSession.ItemHandler/HandleWrapItemOpcode | — |
| SetOwnerGuid | method | — | Player.Main/EquipItem, Player.Main/MoveItemToInventory, Player.Main/_StoreItem | — |
| SetBinding | method | — | ChatHandler.CharacterCommands/HandleAddItemCommand, ChatHandler.CharacterCommands/HandleAddItemSetCommand, Player.Main/VisualizeItem, Player.Main/_StoreItem, Spell.Effects/EffectDisEnchant, WorldSession.SpellHandler/HandleUseItemOpcode | — |
| IsSoulBound | method | — | WorldSession.ItemHandler/HandleWrapItemOpcode, WorldSession.SpellHandler/HandleUseItemOpcode | — |
| isWeapon | method | — | — | — |
| isOneHandedWeapon | method | — | SpellEntry/CalculateCustomCoefficient | — |
| IsBag | method | — | ChatHandler.DebugCommands/HandleDebugGetItemStateCommand, Player.Main/CanBankItem, Player.Main/CanStoreItems, Player.Main/CanUnequipItem, Player.Main/DestroyItem, Player.Main/SwapItem, Player.Main/_CanStoreItem, Player.Main/_CanStoreItem_InBag, Player.Main/_CanStoreItem_InInventorySlots, Player.Main/_CanStoreItem_InSpecificSlot, Player.Main/_LoadInventory, WorldSession.ItemHandler/HandleAutoEquipItemOpcode, WorldSession.ItemHandler/HandleSellItemOpcode, WorldSession.ItemHandler/HandleWrapItemOpcode, WorldSession.LootHandler/DoLootRelease | — |
| IsBroken | method | — | Player.Main/ApplyEnchantment, Player.Main/GetWeaponForAttack#2, Player.Main/HasItemFitToSpellReqirements, Player.Main/UpdateEquipSpellsAtFormChange, Player.Main/_ApplyAllItemMods, Player.Main/_ApplyItemMods, Player.Main/_ApplyWeaponDependentAuraCritMod, Player.Main/_RemoveAllItemMods, Unit.AuraProcHandler/IsTriggeredAtSpellProcEvent, Unit.Main/GetUnitBlockChance | — |
| SetInTrade | method | — | WorldSession.TradeHandler/clearAcceptTradeMode#2, WorldSession.TradeHandler/setAcceptTradeMode | — |
| IsInTrade | method | — | Player.Main/CanStoreItems, Player.Main/DestroyEquippedItem, Player.Main/DestroyItemCount#2, Player.Main/HasItemCount, Spell.Main/CheckItems, WorldSession.SpellHandler/HandleUseItemOpcode | — |
| GetCount | method | — | AiBotAI.Bridge/BridgeHandleSellItems, AuctionHouseMgr/BuildAuctionInfo, AuctionHouseMgr/GetAuctionDeposit, AuctionHouseMgr/SendAuctionWonMail, AuctionHouseMgr/Update#2, Bag/GetItemCount, game_Mail_Mail/CloneFrom, Player.Main/CanBankItem, Player.Main/CanEquipItem#2, Player.Main/CanStoreItems, Player.Main/CanUnequipItem, Player.Main/CanUnequipItems, Player.Main/DestroyItem, Player.Main/DestroyItemCount, Player.Main/DestroyItemCount#2, Player.Main/EquipItem, Player.Main/GetItemCount, Player.Main/HasItemCount, Player.Main/HasItemWithIdEquipped, Player.Main/MoveItemFromInventory, Player.Main/MoveItemToInventory, Player.Main/SendNewItem, Player.Main/SplitItem, Player.Main/SwapItem, Player.Main/_CanStoreItem_InBag, Player.Main/_CanStoreItem_InInventorySlots, Player.Main/_CanStoreItem_InSpecificSlot, Player.Main/_LoadInventory, Player.Main/_StoreItem, TradeData/FillTransactionLog, WorldSession.AuctionHouseHandler/HandleAuctionPlaceBid, WorldSession.AuctionHouseHandler/HandleAuctionSellItem, WorldSession.ItemHandler/HandleBuybackItem, WorldSession.ItemHandler/HandleSellItemOpcode, WorldSession.MailHandler/HandleGetMailList, WorldSession.MailHandler/HandleMailTakeItem, WorldSession.MailHandler/HandleSendMailCallback, WorldSession.TradeHandler/MoveItems, WorldSession.TradeHandler/SendUpdateTrade | — |
| RemoveItemsSetItem | function | Log.Main/Out, Player.Main/ApplyEquipSpell, Player.Main/GetItemSetEffect, Player.Main/RemoveItemSetEffect | Player.Main/DestroyItem, Player.Main/RemoveItem, Player.Main/_RemoveAllItemMods | — |
| SetCount | method | — | ChatHandler.AuctionHouseBotMgr/AddItem, CombatBotBaseAI/AddItemToInventory, Player.Main/DestroyItemCount, Player.Main/DestroyItemCount#2, Player.Main/EquipItem, Player.Main/SplitItem, Player.Main/StoreNewItemInBestSlots, Player.Main/SwapItem, Player.Main/_StoreItem, WorldSession.ItemHandler/HandleSellItemOpcode | — |
| GetMaxStackCount | method | — | Spell.Main/TakeAmmo, WorldSession.ItemHandler/HandleWrapItemOpcode | — |
| GetSlot | method | — | AiBotAI.Loot/TryAutoEquip, ChatHandler.DebugCommands/HandleDebugGetItemStateCommand, Player.Main/AddQuest, Player.Main/ApplyEnchantment, Player.Main/CanEquipItem#2, Player.Main/DestroyItemCount, Player.Main/DurabilityPointsLoss, Player.Main/SendNewItem, Player.Main/_LoadInventory, Player.Main/_SaveInventory, Spell.Effects/EffectSummonChangeItem, Spell.Main/CheckItems, Unit.SpellAuras/IsWeaponBuffCoexistableWith, WorldSession.AuctionHouseHandler/HandleAuctionSellItem, WorldSession.ItemHandler/HandleAutoEquipItemOpcode, WorldSession.ItemHandler/HandleSellItemOpcode, WorldSession.LootHandler/DoLootRelease, WorldSession.MailHandler/HandleSendMailCallback, WorldSession.PetitionsHandler/HandleTurnInPetitionOpcode, WorldSession.SpellHandler/HandleOpenItemOpcode, WorldSession.TradeHandler/HandleAcceptTradeOpcode, WorldSession.TradeHandler/setAcceptTradeMode | — |
| GetContainer | method | — | ChatHandler.DebugCommands/HandleDebugGetItemStateCommand, Player.Main/_LoadInventory, Player.Main/_SaveInventory | — |
| SetSlot | method | — | Bag/StoreItem, Player.Main/DestroyItem, Player.Main/RemoveItem, Player.Main/VisualizeItem, Player.Main/_LoadInventory, Player.Main/_StoreItem | — |
| GetPos | method | — | Player.Main/_LoadInventory, Spell.Effects/EffectSummonChangeItem, WorldSession.AuctionHouseHandler/HandleAuctionSellItem, WorldSession.ItemHandler/HandleAutoBankItemOpcode, WorldSession.ItemHandler/HandleAutoEquipItemOpcode, WorldSession.ItemHandler/HandleAutoEquipItemSlotOpcode, WorldSession.ItemHandler/HandleAutoStoreBagItemOpcode, WorldSession.ItemHandler/HandleSellItemOpcode, WorldSession.MailHandler/HandleSendMailCallback, WorldSession.NPCHandler/HandleRepairItemOpcode | — |
| SetContainer | method | — | Bag/RemoveItem, Bag/StoreItem, Player.Main/VisualizeItem, Player.Main/_LoadInventory, Player.Main/_StoreItem | — |
| IsInBag | method | — | — | — |
| GetItemRandomPropertyId | method | — | AuctionHouseMgr/BuildAuctionInfo, AuctionHouseMgr/BuildListAuctionItems, Player.Main/SendNewItem, Player.Main/SetVisibleItemSlot, WorldSession.AuctionHouseHandler/SendAuctionBidderNotification, WorldSession.AuctionHouseHandler/SendAuctionOwnerNotification, WorldSession.AuctionHouseHandler/SendAuctionRemovedNotification, WorldSession.MailHandler/HandleGetMailList, WorldSession.TradeHandler/SendUpdateTrade | — |
| GetItemSuffixFactor | method | — | AuctionHouseMgr/BuildAuctionInfo, Player.Main/SendNewItem, Player.Main/SetVisibleItemSlot, WorldSession.MailHandler/HandleGetMailList, WorldSession.TradeHandler/SendUpdateTrade | — |
| GetEnchantmentId | method | — | AuctionHouseMgr/BuildAuctionInfo, ChatHandler.CharacterCommands/HandleCharacterPremadeSaveGearCommand, CombatBotBaseAI/CastWeaponBuff, PartyBotAI/CloneFromPlayer, Player.Main/AddEnchantmentDurations, Player.Main/ApplyEnchantment, Player.Main/CastItemCombatSpell, Player.Main/DestroyItem, Player.Main/RemoveAllEnchantments, Player.Main/SetVisibleItemSlot, Player.Main/UpdateEnchantTime, Spell.Effects/EffectEnchantHeldItem, Spell.Effects/EffectSummonChangeItem, WorldSession.MailHandler/HandleGetMailList, WorldSession.PetitionsHandler/HandleOfferPetitionOpcode, WorldSession.PetitionsHandler/HandlePetitionRenameOpcode, WorldSession.PetitionsHandler/HandlePetitionShowSignOpcode, WorldSession.PetitionsHandler/HandleTurnInPetitionOpcode, WorldSession.TradeHandler/SendUpdateTrade | — |
| GetEnchantmentDuration | method | — | Player.Main/AddEnchantmentDurations, Player.Main/ApplyEnchantment, Spell.Effects/EffectSummonChangeItem | — |
| GetEnchantmentCharges | method | — | Player.Main/CastItemCombatSpell, Spell.Effects/EffectSummonChangeItem | — |
| GetSpellCharges | method | — | AuctionHouseMgr/BuildAuctionInfo, Spell.Main/CheckItems, Spell.Main/TakeCastItem, Spell.Main/TakeReagents, WorldSession.ItemHandler/HandleSellItemOpcode, WorldSession.MailHandler/HandleGetMailList, WorldSession.TradeHandler/SendUpdateTrade | — |
| SetSpellCharges | method | — | Spell.Main/TakeCastItem | — |
| HasGeneratedLoot | method | — | Player.Main/SendLoot, Player.Main/SplitItem, Player.Main/SwapItem, Spell.Main/CheckItems, WorldSession.LootHandler/HandleAutostoreLootItemOpcode, WorldSession.LootHandler/HandleLootMoneyOpcode | — |
| HasTemporaryLoot | method | — | Player.Main/CanBankItem, Player.Main/CanEquipItem#2, Player.Main/CanStoreItems, Player.Main/CanUnequipItem, Player.Main/_CanStoreItem | — |
| HasSavedLoot | method | — | — | — |
| GetState | method | — | ChatHandler.DebugCommands/HandleDebugGetItemStateCommand, Player.Main/_SaveInventory, WorldSession.ItemHandler/HandleWrapItemOpcode | — |
| ItemCanGoIntoBag | function | — | Player.Main/CanStoreItems, Player.Main/SwapItem, Player.Main/_CanStoreItem_InBag, Player.Main/_CanStoreItem_InSpecificSlot | — |
| IsInUpdateQueue | method | — | ChatHandler.DebugCommands/HandleDebugGetItemStateCommand | — |
| GetQueuePos | method | — | ChatHandler.DebugCommands/HandleDebugGetItemStateCommand | — |
| FSetState | method | — | MasterPlayer.Main/LoadMailedItems, Player.Main/DeleteFromDB, Player.Main/_LoadInventory, Player.Main/_SaveInventory | — |
| HasQuest | method | — | — | — |
| HasInvolvedQuest | method | — | — | — |
| IsConjuredConsumable | method | — | Player.Main/DestroyConjuredItems | — |
| SetGeneratedLoot | method | — | MasterPlayer.Main/LoadMailedItems, Player.Main/SendLoot, Player.Main/_LoadInventory | — |
| HasGeneratedLootSecondary | method | — | Player.Main/SendLoot | — |
| IsCharter | method | — | Player.Main/DestroyItem | — |
| Item | ctor | Loot/Loot | Bag/Bag, WorldSession.MailHandler/HandleMailCreateTextItem | — |
| Create | method | Object/SetEntry, Object/SetGuidValue, ObjectGuid/ObjectGuid, ObjectMgr/GetItemPrototype, WorldObject.Object/SetObjectScale, WorldObject.Object/SetUInt32Value, WorldObject.Object/_Create | WorldSession.MailHandler/HandleMailCreateTextItem | — |
| RemoveFromWorld | method | Object/RemoveFromWorld, Player.Main/InterruptSpellsWithCastItem | Bag/RemoveFromWorld, Player.Main/DestroyItem, Player.Main/EquipItem, Player.Main/MoveItemFromInventory, Player.Main/RemoveFromWorld, Player.Main/RemoveItemFromBuyBackSlot, Player.Main/_StoreItem | — |
| UpdateDuration | method | Object/GetUInt32Value, Player.Main/DestroyItem, WorldObject.Object/SetUInt32Value | Player.Main/UpdateItemDuration | — |
| SaveToDB | method | Database/CreateStatement, LootMgr/GetMaxSlotInLootFor, LootMgr/LootItemInSlot, Object/GetEntry, Object/GetGUIDLow, Object/GetGuidValue, Object/GetUInt32Value, Object/HasFlag, Object/IsInWorld, ObjectGuid/GetCounter, SqlPreparedStatement/Execute#2, SqlStatement/addInt32, SqlStatement/addString#2, SqlStatement/addUInt16, SqlStatement/addUInt32, SqlStatement/addUInt8, SqlStatementID/SqlStatementID | Bag/SaveToDB, ChatHandler.AuctionHouseBotMgr/AddItem, ChatHandler.MiscCommands/HandleSendItemsHelper, game_Battlegrounds_BattleGround/SendRewardMarkByMail, game_Mail_Mail/CloneFrom, game_Mail_Mail/prepareItems, game_Mail_Mail/prepareTemplateItems, game_Mail_Mail/SendReturnToSender, MasterPlayer.Main/LoadMailedItems, ObjectMgr/RestoreDeletedItems, Player.Main/AutoUnequipItemFromSlot, Player.Main/DeleteFromDB, Player.Main/_LoadInventory, Player.Main/_SaveInventory, WorldSession.AuctionHouseHandler/HandleAuctionSellItem, WorldSession.MailHandler/HandleSendMailCallback | character_gifts, item_instance, item_loot |
| LoadFromDB | method | Database/CreateStatement, Field/GetInt16, Field/GetString, Field/GetUInt16, Field/GetUInt32, Object/ApplyModFlag, Object/GetGUIDLow, Object/GetUInt32Value, Object/HasFlag, Object/SetEntry, Object/SetGuidValue, ObjectGuid/ObjectGuid#2, shared_Util/operator[], shared_Util/size, shared_Util/Tokenizer, SqlPreparedStatement/Execute#2, SqlStatement/addUInt32, SqlStatementID/SqlStatementID, WorldObject.Object/RemoveFlag, WorldObject.Object/SetInt32Value, WorldObject.Object/SetObjectScale, WorldObject.Object/SetUInt32Value, WorldObject.Object/_Create, WorldObject.Object/_LoadIntoDataField | AuctionHouseMgr/LoadAuctionItems, Bag/LoadFromDB, MasterPlayer.Main/LoadMailedItems, Player.Main/DeleteFromDB, Player.Main/_LoadInventory | character_gifts, item_instance |
| DeleteAllFromDB | method | Database/CreateStatement, Object/GetGUIDLow, Object/GetUInt32Value, Object/HasFlag, SqlPreparedStatement/operator=, SqlStatementID/SqlStatementID | WorldSession.TradeHandler/MoveItems | character_gifts, item_instance, item_loot, item_text |
| DeleteAllFromDB#2 | method | Database/PExecute#2, GuildMgr/DeletePetition, GuildMgr/GetPetitionByCharterGuid, ObjectGuid/ObjectGuid#4 | ChatHandler.CharacterCommands/HandleCleanCharactersItemsCommand | auction, character_gifts, character_inventory, item_instance, mail_items |
| LoadLootFromDB | method | Database/PExecute#2, Field/GetInt32, Field/GetUInt32, Log.Main/Out, LootMgr/LootItem#2, Object/GetGUIDLow, ObjectGuid/GetString, ObjectMgr/GetItemPrototype | Player.Main/_LoadItemLoot | item_loot |
| DeleteFromDB | method | Database/CreateStatement, Object/GetGUIDLow, SqlStatementID/SqlStatementID | Bag/DeleteFromDB | item_instance |
| DeleteFromInventoryDB | method | Database/CreateStatement, Object/GetGUIDLow, SqlStatementID/SqlStatementID | Player.Main/AutoUnequipItemFromSlot, WorldSession.AuctionHouseHandler/HandleAuctionSellItem, WorldSession.MailHandler/HandleSendMailCallback, WorldSession.TradeHandler/MoveItems | character_inventory |
| GetProto | method | Object/GetEntry, ObjectMgr/GetItemPrototype | AiBotAI.Bridge/BridgeHandleSellItems, AiBotAI.Bridge/BridgeSendState, AiBotAI.Loot/ChooseQuestReward, AiBotAI.Loot/TryAutoEquip, AiBotAI.Loot/TryAutoEquipBags, AiBotAI.Main/OnPacketReceived, AuctionHouseMgr/BuildListAuctionItems, AuctionHouseMgr/GetAuctionDeposit, AuctionHouseMgr/SendAuctionWonMail, Bag/LoadFromDB, boss_viscidus/SpellHit, CombatBotBaseAI/AddHunterAmmo, CombatBotBaseAI/EquipOrUseNewItem, CombatBotBaseAI/EquipRandomGearInEmptySlots, CombatBotBaseAI/GetHighestHonorRankFromEquippedItems, CombatBotBaseAI/IsWearingShield, CombatBotBaseAI/UseItemEffect, custom_creatures/Enchant, custom_creatures/GossipSelect_EnchantNPC, PartyBotAI/CloneFromPlayer, Player.Main/ApplyEnchantment, Player.Main/ApplyEquipCooldown, Player.Main/ApplyItemEquipSpell, Player.Main/CanBankItem, Player.Main/CanEquipItem, Player.Main/CanEquipItem#2, Player.Main/CanStoreItems, Player.Main/CanUnequipItem, Player.Main/CanUseItem, Player.Main/CastItemCombatSpell, Player.Main/CastItemUseSpell, Player.Main/ChangeItemsForRace, Player.Main/CheckAmmoCompatibility, Player.Main/CountFreeInventorySlots, Player.Main/CreateCorpse, Player.Main/DestroyItem, Player.Main/DurabilityRepair, Player.Main/EquipItem, Player.Main/GetBaseWeaponSkillValue, Player.Main/GetWeaponForAttack#2, Player.Main/OnReceivedItem, Player.Main/RemoveItem, Player.Main/RemoveItemDependentAurasAndCasts, Player.Main/SendEquipError, Player.Main/SendLoot, Player.Main/SetRegularAttackTime, Player.Main/StoreNewItemInBestSlots, Player.Main/SwapItem, Player.Main/UpdateCombatSkills, Player.Main/UpdateItemDuration, Player.Main/VisualizeItem, Player.Main/_ApplyAllItemMods, Player.Main/_ApplyItemMods, Player.Main/_CanStoreItem_InBag, Player.Main/_CanStoreItem_InSpecificSlot, Player.Main/_LoadInventory, Player.Main/_RemoveAllItemMods, Player.Main/_StoreItem, Spell.Effects/DoCreateItem, Spell.Effects/EffectDisEnchant, Spell.Effects/EffectDurabilityDamage, Spell.Effects/EffectEnchantItemPerm, Spell.Effects/EffectEnchantItemTmp, Spell.Effects/EffectFeedPet, Spell.Effects/EffectOpenLock, Spell.Effects/EffectSummonGuardian, Spell.Effects/EffectSummonWild, Spell.Main/CheckCast, Spell.Main/CheckItems, Spell.Main/SendSpellCooldown, Spell.Main/TakeAmmo, Spell.Main/TakeCastItem, Spell.Main/TakeReagents, Spell.Main/WriteAmmoToPacket, SpellCaster/GetAPMultiplier, SpellCaster/GetWeaponSkillValue, spell_item/OnCheckCast#2, spell_shaman/OnEffectExecute, Unit.AuraProcHandler/HandleDummyAuraProc, Unit.AuraProcHandler/IsTriggeredAtSpellProcEvent, Unit.Main/GetUnitBlockChance, Unit.SpellAuras/HandleRangedAmmoHaste, WorldSession.AuctionHouseHandler/HandleAuctionSellItem, WorldSession.ItemHandler/HandleDestroyItemOpcode, WorldSession.ItemHandler/HandleReadItemOpcode, WorldSession.ItemHandler/HandleSellItemOpcode, WorldSession.ItemHandler/HandleWrapItemOpcode, WorldSession.MailHandler/HandleMailTakeItem, WorldSession.MailHandler/HandleSendMailCallback, WorldSession.SpellHandler/HandleOpenItemOpcode, WorldSession.SpellHandler/HandleUseItemOpcode, WorldSession.TradeHandler/MoveItems, WorldSession.TradeHandler/SendUpdateTrade | — |
| GetOwner | method | ObjectMgr/GetPlayer | Spell.Effects/EffectEnchantItemPerm, Spell.Effects/EffectEnchantItemTmp, Spell.Main/CheckCast, Spell.Main/CheckItems | — |
| GetAllowedEquipSlots | method | — | CombatBotBaseAI/EquipRandomGearInEmptySlots, Player.Main/FindEquipSlot, Player.Main/SaveNewPlayer | — |
| GetProficiencySkill | method | — | CombatBotBaseAI/EquipRandomGearInEmptySlots, Player.Main/CanUseItem#2, Player.Main/GetBaseWeaponSkillValue, Player.Main/UpdateCombatSkills, SpellCaster/GetWeaponSkillValue | — |
| GetProficiencySpell | method | — | CombatBotBaseAI/EquipOrUseNewItem, Player.Main/SatisfyItemRequirements | — |
| GenerateItemRandomPropertyId | method | ItemEnchantmentMgr/GetItemEnchantMod, Log.Main/Out, ObjectMgr/GetItemPrototype | ChatHandler.AuctionHouseBotMgr/AddItem, ChatHandler.CharacterCommands/HandleAddItemCommand, CombatBotBaseAI/AddItemToInventory, LootMgr/LootItem, Player.Main/AddItem, Player.Main/BuyItemFromVendor, Player.Main/RewardQuest, Player.Main/StoreNewItemInBestSlots, Player.Main/StoreNewItemInInventorySlot, Spell.Effects/DoCreateItem, ThreatListCopier.battleground_alterac/GossipSelect_npc_AVBlood_collector | — |
| SetItemRandomProperties | method | Object/GetInt32Value, WorldObject.Object/SetInt32Value | ChatHandler.AuctionHouseBotMgr/AddItem, Player.Main/StoreNewItem, Player.Main/StoreNewItemInBestSlots | — |
| SetState | method | — | AiBotAI.Loot/TryAutoEquip, AiBotAI.Loot/TryAutoEquipBags, Player.Main/DestroyItem, Player.Main/DestroyItemCount, Player.Main/DestroyItemCount#2, Player.Main/DurabilityPointsLoss, Player.Main/DurabilityRepair, Player.Main/EquipItem, Player.Main/MoveItemToInventory, Player.Main/RemoveItemFromBuyBackSlot, Player.Main/SplitItem, Player.Main/SwapItem, Player.Main/VisualizeItem, Player.Main/_LoadInventory, Player.Main/_StoreItem, Spell.Effects/EffectOpenLock, Spell.Main/TakeCastItem, WorldSession.ItemHandler/HandleSellItemOpcode, WorldSession.ItemHandler/HandleWrapItemOpcode, WorldSession.MailHandler/HandleMailTakeItem, WorldSession.PetitionsHandler/HandlePetitionBuyOpcode, WorldSession.SpellHandler/HandleOpenItemOpcode, WorldSession.SpellHandler/HandleUseItemOpcode | — |
| AddToUpdateQueueOf | method | Log.Main/Out, Object/GetGuidStr, Object/GetObjectGuid, ObjectGuid/GetString, ObjectGuid/operator!= | — | — |
| RemoveFromUpdateQueueOf | method | Log.Main/Out, Object/GetGuidStr, Object/GetObjectGuid, ObjectGuid/GetString, ObjectGuid/operator!= | AiBotAI.Loot/TryAutoEquip, AiBotAI.Loot/TryAutoEquipBags, Player.Main/MoveItemFromInventory, WorldSession.ItemHandler/HandleSellItemOpcode | — |
| GetBagSlot | method | — | AiBotAI.Loot/TryAutoEquip, ChatHandler.DebugCommands/HandleDebugGetItemStateCommand, Player.Main/AddQuest, Player.Main/DestroyItemCount, Player.Main/SendNewItem, Player.Main/_SaveInventory, Spell.Effects/EffectSummonChangeItem, WorldSession.AuctionHouseHandler/HandleAuctionSellItem, WorldSession.ItemHandler/HandleAutoEquipItemOpcode, WorldSession.ItemHandler/HandleSellItemOpcode, WorldSession.LootHandler/DoLootRelease, WorldSession.MailHandler/HandleSendMailCallback, WorldSession.PetitionsHandler/HandleTurnInPetitionOpcode, WorldSession.SpellHandler/HandleOpenItemOpcode, WorldSession.TradeHandler/HandleAcceptTradeOpcode, WorldSession.TradeHandler/setAcceptTradeMode | — |
| IsEquipped | method | — | CombatBotBaseAI/EquipOrUseNewItem, Player.Main/ApplyEnchantment, Player.Main/DestroyItem, Player.Main/DurabilityPointsLoss, Spell.Effects/EffectEnchantHeldItem, Unit.SpellAuras/IsWeaponBuffCoexistableWith, WorldSession.ItemHandler/HandleWrapItemOpcode, WorldSession.SpellHandler/HandleUseItemOpcode | — |
| CanBeTraded | method | Bag/IsEmpty, Object/GetObjectGuid, ObjectGuid/operator==, Player.Main/CanUnequipItem, Player.Main/GetLootGuid, Player.Main/IsBagPos | WorldSession.AuctionHouseHandler/HandleAuctionSellItem, WorldSession.MailHandler/HandleSendMailCallback, WorldSession.TradeHandler/HandleAcceptTradeOpcode, WorldSession.TradeHandler/HandleSetTradeItemOpcode | — |
| IsBoundByEnchant | method | — | — | — |
| IsFitToSpellRequirements#2 | method | — | Player.StatSystem/GetWeaponBasedAuraModifier | — |
| IsFitToSpellRequirements | method | — | Player.Main/HasItemFitToSpellReqirements, Player.Main/_ApplyWeaponDependentAuraCritMod, Player.Main/_ApplyWeaponDependentAuraDamageMod, Player.StatSystem/GetWeaponBasedAuraModifier#2, Spell.Effects/EffectTriggerSpell, Spell.Main/CheckItems, SpellCaster/MeleeDamageBonusDone, SpellCaster/SpellDamageBonusDone | — |
| IsTargetValidForItemUse | method | ObjectMgr/GetItemRequiredTargetMapBounds | WorldSession.SpellHandler/HandleUseItemOpcode | — |
| SetEnchantment | method | Object/GetEntry, ObjectGuid/IsEmpty, ObjectGuid/ObjectGuid, Player.Main/SendEnchantmentLog, WorldObject.Object/SetUInt32Value | custom_creatures/Enchant, Player.Main/StoreNewItemInBestSlots, Spell.Effects/EffectEnchantHeldItem, Spell.Effects/EffectEnchantItemPerm, Spell.Effects/EffectEnchantItemTmp, Spell.Effects/EffectSummonChangeItem, Unit.SpellAuras/TriggerSpell | — |
| SetEnchantmentDuration | method | WorldObject.Object/SetUInt32Value | Player.Main/AddEnchantmentDuration, Player.Main/RemoveEnchantmentDurations, Player.Main/_SaveInventory | — |
| SetEnchantmentCharges | method | WorldObject.Object/SetUInt32Value | Player.Main/CastItemCombatSpell | — |
| ClearEnchantment | method | Object/GetEntry, ObjectGuid/ObjectGuid, Player.Main/SendEnchantmentLog, WorldObject.Object/SetUInt32Value | custom_creatures/Enchant, Player.Main/CastItemCombatSpell, Player.Main/RemoveAllEnchantments, Player.Main/RemoveItem, Player.Main/StoreNewItemInBestSlots, Player.Main/UpdateEnchantTime | — |
| IsLimitedToAnotherMapOrZone | method | — | Player.Main/DestroyZoneLimitedItem, Player.Main/_LoadInventory | — |
| SendTimeUpdate | method | ByteBuffer/operator<<#10, Object/GetObjectGuid, Object/GetUInt32Value, ObjectGuid/operator<<, Player.Main/GetSession, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | Player.Main/AddItemDurations, Player.Main/SendItemDurations | — |
| CreateItem | method | Bag/NewItemOrBag, Errors/PrintStacktraceAndThrow, ItemPrototype/GetMaxStackSize, ObjectGuid/ObjectGuid, ObjectMgr/GenerateItemLowGuid, ObjectMgr/GetItemPrototype | ChatHandler.AuctionHouseBotMgr/AddItem, ChatHandler.MiscCommands/HandleSendItemsHelper, game_Battlegrounds_BattleGround/SendRewardMarkByMail, game_Mail_Mail/prepareItems, game_Mail_Mail/prepareTemplateItems, ObjectMgr/RestoreDeletedItems, Player.Main/EquipNewItem, Player.Main/StoreNewItem, Spell.Effects/EffectSummonChangeItem | — |
| CloneItem | method | Object/GetEntry, Object/GetGuidValue, Object/GetObjectGuid, Object/GetUInt32Value, Object/SetGuidValue, ObjectGuid/ObjectGuid, WorldObject.Object/SetUInt32Value | game_Mail_Mail/CloneFrom, Player.Main/SplitItem, Player.Main/_StoreItem, WorldSession.ItemHandler/HandleSellItemOpcode | — |
| IsBindedNotWith | method | Object/GetObjectGuid, ObjectGuid/operator== | Player.Main/CanBankItem, Player.Main/CanEquipItem#2, Player.Main/CanStoreItems, Player.Main/CanUseItem, Player.Main/_CanStoreItem | — |
| AddToClientUpdateList | method | Map.Main/AddUpdateObject, Player.Main/GetSession, WorldObject.Object/GetMap, WorldSession.Main/IsConnected, WorldSession.Main/PlayerLogout | — | — |
| RemoveFromClientUpdateList | method | Map.Main/RemoveUpdateObject, WorldObject.Object/GetMap | — | — |
| BuildUpdateData | method | WorldObject.Object/BuildUpdateDataForPlayer, WorldObject.Object/ClearUpdateMask | — | — |
| CanBeMergedPartlyWith | method | ItemPrototype/GetMaxStackSize, Object/GetEntry | Player.Main/CanStoreItems, Player.Main/_CanStoreItem_InBag, Player.Main/_CanStoreItem_InInventorySlots, Player.Main/_CanStoreItem_InSpecificSlot | — |
| IsFitToRequirements | method | Object/GetEntry, Object/GetTypeId, Unit.Main/IsAlive | — | — |
| SetLootState | method | Errors/PrintStacktraceAndThrow | Player.Main/SendLoot, WorldSession.LootHandler/DoLootRelease, WorldSession.LootHandler/HandleAutostoreLootItemOpcode, WorldSession.LootHandler/HandleLootMoneyOpcode | — |
| ChangeEntry | method | Object/SetEntry | Player.Main/ChangeItemsForRace | — |
| GetLocalizedNameWithSuffix | method | ObjectMgr/GetItemLocale | AuctionHouseMgr/BuildListAuctionItems, ChatHandler.Chat/isValidChatMessage | — |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `auction`: id int(11) unsigned PK, house_id int(11) unsigned, item_guid int(11) unsigned, item_id int(11) unsigned, seller_guid int(11) unsigned, buyout_price int(11), expire_time bigint(40), buyer_guid int(11) unsigned, last_bid int(11), start_bid int(11), deposit int(11)
- `character_gifts`: guid int(20) unsigned, item_guid int(11) unsigned PK, item_id int(20) unsigned, flags int(20) unsigned
- `character_inventory`: guid int(11) unsigned, bag int(11) unsigned, slot tinyint(3) unsigned, item_guid int(11) unsigned PK, item_id int(11) unsigned
- `item_instance`: guid int(10) unsigned PK, item_id mediumint(8) unsigned, owner_guid int(10) unsigned, creator_guid int(10) unsigned, gift_creator_guid int(10) unsigned, count int(10) unsigned, duration int(10), charges tinytext?, flags mediumint(8) unsigned, enchantments text, random_property_id smallint(5), durability smallint(5) unsigned, text int(10) unsigned, generated_loot tinyint(4)?
- `item_loot`: guid int(11) unsigned PK, owner_guid int(11) unsigned, item_id int(11) unsigned PK, amount int(11) unsigned, property int(11)
- `item_text`: id int(11) unsigned PK, text longtext?
- `mail_items`: mail_id int(11) unsigned PK, item_guid int(11) unsigned PK, item_id int(11) unsigned, receiver_guid int(11) unsigned

*`?` = nullable, `PK` = primary key column.*

