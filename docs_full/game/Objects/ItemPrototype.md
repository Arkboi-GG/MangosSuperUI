# ItemPrototype

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ItemPrototype

**Purpose & Responsibilities**

`ItemPrototype` is the static data structure representing the definition of a single item type in the game world. It acts as the blueprint for all dynamic `Item` instances created during gameplay. Defined in `ItemPrototype.h`, this unit contains no runtime state beyond the static configuration loaded from the database (specifically `item_template`) and DBC files.

Its primary responsibilities are:
1.  **Storage**: Holding all intrinsic properties of an item (stats, damage, spells, requirements, flags).
2.  **Classification**: Providing helper methods to determine item categories (e.g., `IsWeapon`, `IsRangedWeapon`) based on its `Class` and `SubClass`.
3.  **Constraint Checking**: Offering lightweight checks for gameplay rules, such as whether an item can be equipped during combat (`CanChangeEquipStateInCombat`) or how many copies can stack (`GetMaxStackSize`).
4.  **Data Translation**: Converting internal item class/subclass IDs into indices required by other systems, such as durability multipliers (`ItemSubClassToDurabilityMultiplierId`).

This unit is purely declarative and computational; it does not manage item instances, inventory slots, or player states. Those responsibilities belong to `Player`, `Item`, and related managers.

## Member-by-Member Behavior

The members of `ItemPrototype` are divided into global helper functions and instance methods of the `ItemPrototype` struct.

### Global Helper Functions

**`ItemSubClassToDurabilityMultiplierId`**
This standalone function translates an item's `Class` and `SubClass` into an index used for durability calculations.
*   **Logic**:
    *   If the item is a Weapon (`ITEM_CLASS_WEAPON`), it returns the `ItemSubClass` directly.
    *   If the item is Armor (`ITEM_CLASS_ARMOR`), it returns `ItemSubClass + 21`.
    *   For all other classes, it returns `0`.
*   **Usage**: This index likely maps to a row in a DBC file (e.g., `ItemSubClass.dbc` or similar) that defines the durability multiplier for that specific weapon or armor type. It is called by `Player.Main/DurabilityRepair` to calculate repair costs and by `WorldSession.ItemHandler/HandleSellItemOpcode` to determine sell value based on remaining durability.

### Instance Methods

**`CanChangeEquipStateInCombat`**
Determines if an item can be equipped or unequipped while the player is in combat.
*   **Logic**: Returns `true` if the item's `InventoryType` is `INVTYPE_RELIC`, `INVTYPE_SHIELD`, or `INVTYPE_HOLDABLE`. It also returns `true` if the item's `Class` is `ITEM_CLASS_WEAPON` or `ITEM_CLASS_PROJECTILE`. Otherwise, it returns `false`.
*   **Context**: This enforces the game rule that most gear changes are locked during combat, but weapons, ammo, shields, and off-hand items can be swapped. It is called by `Player.Main/CanEquipItem#2` and `Player.Main/CanUnequipItem`.

**`GetMaxStackSize`**
Returns the maximum number of items of this type that can be stacked in a single inventory slot.
*   **Logic**: Simply returns the `Stackable` member variable.
*   **Context**: Used extensively by inventory management logic in `Player.Main` (e.g., `CanStoreItems`, `StoreNewItemInBestSlots`, `SwapItem`) and by bot AI systems (`CombatBotBaseAI/AddHunterAmmo`) to determine if an item can be merged or needs a new slot.

**`IsConjuredConsumable`**
Checks if the item is a consumable that was conjured by a mage.
*   **Logic**: Returns `true` if `Class` is `ITEM_CLASS_CONSUMABLE` AND the `Flags` field contains `ITEM_FLAG_CONJURED`.
*   **Context**: Conjured items often have special handling (e.g., they cannot be sold to vendors, or they disappear on logout). While listed in the map as having no callers, this property is critical for distinguishing temporary magical items from standard consumables.

**`IsWeapon`**
Checks if the item is classified as a weapon.
*   **Logic**: Returns `true` if `Class` is `ITEM_CLASS_WEAPON`.
*   **Context**: Used by `AiBotAI.Loot/ScoreItem` to prioritize weapons for bots and by `Player.Main/_ApplyItemBonuses` to apply weapon-specific stat modifications.

**`IsRangedWeapon`**
Checks if the item is a ranged weapon.
*   **Logic**: Returns `true` if `IsWeapon()` is true AND `InventoryType` is one of `INVTYPE_RANGED`, `INVTYPE_THROWN`, or `INVTYPE_RANGEDRIGHT`.
*   **Context**: Used by `Creature.Main/LoadDefaultEquipment` to assign appropriate ranged gear to NPCs and by `Player.Main/_ApplyItemBonuses` for ranged attack power calculations.

**`HasSignature`**
Determines if the item should display a creator signature (e.g., "Created by [PlayerName]").
*   **Logic**: Returns `true` if:
    1.  `GetMaxStackSize()` is 1 (unique/stackable limit 1).
    2.  `Class` is NOT `ITEM_CLASS_CONSUMABLE` or `ITEM_CLASS_QUEST`.
    3.  `Flags` does NOT contain `ITEM_FLAG_NO_CREATOR`.
    4.  `ItemId` is NOT 6948 (Hardcoded exception for Hearthstone).
*   **Context**: Called by `Spell.Effects/DoCreateItem` to decide whether to attach the creator's name to the item upon creation via spell. The hardcoded exclusion of Hearthstone (ID 6948) is a notable edge case, ensuring that conjured hearthstones do not bear the mage's name, preserving their generic nature.

**`HasItemFlag`**
Checks if a specific bit is set in the item's `Flags` field.
*   **Logic**: Returns `Flags & flag`.
*   **Context**: Used by `Spell.Main/CheckItems` to verify item conditions for spell casting (e.g., checking if an item is indestructible or has a cooldown).

**`HasExtraFlag`**
Checks if a specific bit is set in the item's `ExtraFlags` field.
*   **Logic**: Returns `ExtraFlags & flag`.
*   **Context**: Used by `CombatBotBaseAI/EquipRandomGearInEmptySlots` to filter items, `LootMgr/AllowedForPlayer` to determine loot eligibility, and `ObjectMgr/LoadItemPrototypes` during data loading to validate or process extra metadata.

## Cross-Unit Boundaries

`ItemPrototype` is a passive data provider. It does not initiate actions but is queried by various subsystems to make decisions about item interaction.

*   **Player.Main**: Heavily relies on `ItemPrototype` for inventory logic.
    *   `CanEquipItem#2` and `CanUnequipItem` call `CanChangeEquipStateInCombat` to enforce combat restrictions.
    *   `_ApplyItemBonuses` calls `IsWeapon` and `IsRangedWeapon` to correctly apply stat modifiers.
    *   Storage functions (`CanStoreItems`, `StoreNewItemInBestSlots`, `SwapItem`, `_CanStoreItem_InBag`, `_CanStoreItem_InInventorySlots`) call `GetMaxStackSize` to manage stacking limits.
    *   `DurabilityRepair` calls `ItemSubClassToDurabilityMultiplierId` to compute repair costs.

*   **WorldSession.ItemHandler**:
    *   `HandleSellItemOpcode` calls `ItemSubClassToDurabilityMultiplierId` to calculate the vendor sell price based on durability loss.

*   **AiBotAI.Loot**:
    *   `ScoreItem` calls `IsWeapon` to evaluate the value of looted items for bot characters.

*   **CombatBotBaseAI**:
    *   `AddHunterAmmo` calls `GetMaxStackSize` to manage ammo stacks.
    *   `EquipRandomGearInEmptySlots` calls `HasExtraFlag` to filter suitable equipment.

*   **Creature.Main**:
    *   `LoadDefaultEquipment` calls `IsRangedWeapon` to properly equip NPCs with ranged gear.

*   **Spell System**:
    *   `Spell.Effects/DoCreateItem` calls `HasSignature` to handle creator attribution.
    *   `Spell.Main/CheckItems` calls `HasItemFlag` to validate item prerequisites for spells.

*   **LootMgr**:
    *   `AllowedForPlayer` calls `HasExtraFlag` to determine if a player can receive a specific item from loot.

*   **ObjectMgr**:
    *   `LoadItemPrototypes` calls `HasExtraFlag` during the initial loading of item data from the database.

*   **game_Objects_Item**:
    *   `CanBeMergedPartlyWith` and `CreateItem` call `GetMaxStackSize` to handle item merging and creation logic.

*   **ChatHandler.MiscCommands**:
    *   `HandleSendItemsHelper` calls `GetMaxStackSize` for debugging/admin commands related to item sending.

## Data Model

`ItemPrototype` corresponds to the `item_template` table in the database. However, the provided SCHEMA section is empty. Therefore, column types and constraints are not cited from a schema dump. Based on the struct definition and common World of Warcraft private server structures, the relevant fields map to columns such as:
*   `ItemId` -> `entry`
*   `Class` -> `class`
*   `SubClass` -> `subclass`
*   `Name1` -> `name`
*   `Description` -> `description`
*   `Flags` -> `flags`
*   `ExtraFlags` -> `extra_flags`
*   `Stackable` -> `stackable`
*   `InventoryType` -> `inventorytype`
*   `AllowableClass` -> `allowableclass`
*   `AllowableRace` -> `allowablerace`
*   `ItemLevel` -> `itemlevel`
*   `RequiredLevel` -> `requiredlevel`
*   `Damage` array -> `damage`, `damagetype`
*   `Armor` -> `armor`
*   `Spells` array -> `spellid_1` through `spellid_5`, `spelltrigger_1` through `spelltrigger_5`, etc.
*   `MaxDurability` -> `maxdurability`

No direct SQL queries are present in this unit; data loading is handled by `ObjectMgr/LoadItemPrototypes`.

## Notable Implementation Details

1.  **Hardcoded Hearthstone Exception**: In `HasSignature`, the item ID `6948` (Hearthstone) is explicitly excluded from having a creator signature, even though it meets all other criteria (stackable 1, not consumable, no `NO_CREATOR` flag). This ensures that mage-conjured hearthstones remain generic and do not display "Created by [MageName]", which would be undesirable for a utility item often traded or gifted.

2.  **Durability Multiplier Offset**: The `ItemSubClassToDurabilityMultiplierId` function uses an offset of `+21` for armor subclasses. This implies that the durability multiplier DBC file stores weapon subclasses first (indices 0-20) followed by armor subclasses (indices 21+). This is a fragile coupling to the DBC file structure; if the DBC layout changes, this offset must be updated.

3.  **Packed Struct**: The `ItemPrototype` struct is defined with `#pragma pack(1)`. This ensures that the memory layout matches the binary format expected by the DBC files or network packets, preventing padding bytes from misaligning data. This is critical for correct deserialization of item data.

4.  **Mutable Metadata**: The struct contains `mutable` members (`SourceQuestLevel`, `SourceQuestRaces`, `SourceQuestClasses`, `Discovered`). These are not part of the static item definition but are calculated at runtime based on quest rewards and player discovery. They allow the system to track dynamic properties (like whether an item has been discovered by the player base) without modifying the const-correctness of the prototype itself.

5.  **Limited Consumable Subclasses**: The `ItemSubclassConsumable` enum only defines `ITEM_SUBCLASS_CONSUMABLE` (0). Comments indicate that other subclasses (Potion, Elixir, etc.) were not used in pre-BC (Vanilla) versions. This reflects the simplified item classification in the Vanilla era compared to later expansions.

6.  **Flag Masks**: The `ItemPrototypeFlags` and `ItemExtraFlags` enums define bitmasks for various item behaviors. Some flags are marked as "not used" or "deprecated" (e.g., `ITEM_FLAG_EXOTIC`, `ITEM_FLAG_DEPRECATED`), indicating legacy data that may still exist in the database but has no effect on current gameplay logic.

## Member Reference

**`ItemSubClassToDurabilityMultiplierId`**: Global function that converts item class and subclass into a durability multiplier index. Returns subclass for weapons, subclass + 21 for armor, and 0 otherwise. Called by `Player.Main/DurabilityRepair` and `WorldSession.ItemHandler/HandleSellItemOpcode`.

**`CanChangeEquipStateInCombat`**: Method that returns `true` if the item can be equipped/unequipped during combat. Applies to Relics, Shields, Holdables, Weapons, and Projectiles. Called by `Player.Main/CanEquipItem#2` and `Player.Main/CanUnequipItem`.

**`GetMaxStackSize`**: Method that returns the `Stackable` member, indicating the maximum stack size. Called by `ChatHandler.MiscCommands/HandleSendItemsHelper`, `CombatBotBaseAI/AddHunterAmmo`, `game_Objects_Item/CanBeMergedPartlyWith`, `game_Objects_Item/CreateItem`, and multiple `Player.Main` storage functions.

**`IsConjuredConsumable`**: Method that returns `true` if the item is a consumable with the `ITEM_FLAG_CONJURED` flag. No external callers listed in the map.

**`IsWeapon`**: Method that returns `true` if the item class is `ITEM_CLASS_WEAPON`. Called by `AiBotAI.Loot/ScoreItem` and `Player.Main/_ApplyItemBonuses`.

**`IsRangedWeapon`**: Method that returns `true` if the item is a weapon with a ranged inventory type (Ranged, Thrown, RangedRight). Called by `Creature.Main/LoadDefaultEquipment` and `Player.Main/_ApplyItemBonuses`.

**`HasSignature`**: Method that determines if the item should have a creator signature. Excludes consumables, quests, items with `NO_CREATOR` flag, stackable > 1, and specifically Hearthstone (ID 6948). Called by `Spell.Effects/DoCreateItem`.

**`HasItemFlag`**: Method that checks if a specific bit is set in the `Flags` field. Called by `Spell.Main/CheckItems`.

**`HasExtraFlag`**: Method that checks if a specific bit is set in the `ExtraFlags` field. Called by `CombatBotBaseAI/EquipRandomGearInEmptySlots`, `LootMgr/AllowedForPlayer`, and `ObjectMgr/LoadItemPrototypes`.

---

<!-- machine-true, projected from graph.json -->

## Map — ItemPrototype

*Source:* ItemPrototype.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ItemSubClassToDurabilityMultiplierId | function | — | Player.Main/DurabilityRepair, WorldSession.ItemHandler/HandleSellItemOpcode | — |
| CanChangeEquipStateInCombat | method | — | Player.Main/CanEquipItem#2, Player.Main/CanUnequipItem | — |
| GetMaxStackSize | method | — | ChatHandler.MiscCommands/HandleSendItemsHelper, CombatBotBaseAI/AddHunterAmmo, game_Objects_Item/CanBeMergedPartlyWith, game_Objects_Item/CreateItem, Player.Main/CanStoreItems, Player.Main/StoreNewItemInBestSlots, Player.Main/SwapItem, Player.Main/_CanStoreItem_InBag, Player.Main/_CanStoreItem_InInventorySlots | — |
| IsConjuredConsumable | method | — | — | — |
| IsWeapon | method | — | AiBotAI.Loot/ScoreItem, Player.Main/_ApplyItemBonuses | — |
| IsRangedWeapon | method | — | Creature.Main/LoadDefaultEquipment, Player.Main/_ApplyItemBonuses | — |
| HasSignature | method | — | Spell.Effects/DoCreateItem | — |
| HasItemFlag | method | — | Spell.Main/CheckItems | — |
| HasExtraFlag | method | — | CombatBotBaseAI/EquipRandomGearInEmptySlots, LootMgr/AllowedForPlayer, ObjectMgr/LoadItemPrototypes | — |
