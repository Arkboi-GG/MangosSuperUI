<!-- provenance: boundary-bleed -->
# AiBotAI.Loot

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# AiBotAI.Loot

**Purpose & Responsibilities**

This translation unit (`AiBotAILoot.cpp`) implements the autonomous looting, item evaluation, and equipment management subsystem for the `AiBotAI` class. Its primary responsibility is to maximize the bot's combat effectiveness and inventory efficiency after defeating enemies or completing quests.

The unit operates in three distinct phases:
1.  **Looting:** Automatically generating loot tables from corpses, distributing gold among group members according to proximity and group loot rules, and storing items in the bot's inventory.
2.  **Item Scoring:** Evaluating the statistical value of items based on the bot's class, current equipment, and item quality. This involves parsing both explicit item stats and implicit "on-equip" spell effects (such as +Spell Damage or +Attack Power) which are common in Vanilla WoW but not stored in standard stat fields.
3.  **Auto-Equipping:** Iteratively upgrading the bot's gear and bags by comparing new items against currently equipped ones using the scoring system. It prioritizes bag expansion to increase inventory capacity before equipping combat gear.

This unit is decoupled from combat logic to allow independent refinement of economic and gearing behaviors. It relies heavily on the `AiBotAI.Bridge` unit to report events (like loot acquired or gear changed) to the external C# coordinator.

## Member-by-Member Behavior

### Looting and Quest Rewards

**`ChooseQuestReward`**
Determines the optimal reward index for a quest with multiple choice items.
*   **Logic:** It iterates through all available choice items defined in the `Quest` object.
*   **Evaluation Criteria:**
    1.  **Usability:** Filters out items the bot cannot use (wrong class, race, level, or proficiency).
    2.  **Gear Upgrade:** For weapons and armor, it calculates the `ScoreItem` difference between the new item and the currently equipped item in that slot. It tracks the item with the highest positive score gain.
    3.  **Vendor Value Fallback:** If no gear upgrade is found (or if the item is not gear), it tracks the item with the highest total vendor value (SellPrice × Stack Size).
*   **Decision:** Returns the index of the best gear upgrade. If no upgrade exists, it returns the index of the most valuable item for selling.
*   **Collaboration:** Calls `ScoreItem` (local) to evaluate gear. Uses `Player.Main` methods to check usability and retrieve currently worn items. Logs the decision via `Log.Main`.

**`DoAutoLoot`**
Executes the full looting sequence for a defeated creature.
*   **Preconditions:** Checks if the bot is alive and in the world. Verifies the creature exists and is dead.
*   **Loot Generation:** If loot hasn't been generated yet (`lootForBody` is false), it forces the bot as the recipient if none is set, then calls `Creature.Main/GenerateLootForBody`.
*   **Group Loot Handling:** If the bot is in a group, it delegates to `game_Group_Group/GroupLoot` or `game_Group_Group/NeedBeforeGreed` depending on the group's loot method.
*   **Gold Distribution:**
    *   Calculates the total gold on the corpse.
    *   If in a group, it identifies all group members within loot XP distance (`WorldObject.Object/IsWithinLootXPDist`).
    *   Divides gold equally among nearby members and applies it via `Player.Main/ModifyMoney` and `Player.Main/LootMoney`.
    *   If solo, the bot takes all gold.
*   **Item Storage:** Calls `Player.Main/AutoStoreLoot` to automatically move looted items into the inventory.
*   **Post-Loot Actions:**
    *   Sends a `LOOT` event to the bridge with details of gold and items taken.
    *   Calls `TryAutoEquipBags()` followed by `TryAutoEquip()` to immediately utilize any new gear or bags.
    *   Cleans up the corpse by clearing the loot table and removing the `UNIT_DYNFLAG_LOOTABLE` flag.
*   **Collaboration:** Extensive interaction with `Creature.Main`, `Player.Main`, `Group`, and `AiBotAI.Bridge`.

### Auto-Equipment Logic

**`TryAutoEquipBags`**
Attempts to equip larger bags to increase inventory space.
*   **Strategy:** Runs up to 8 passes to handle cascading replacements (e.g., moving an item from a small bag to a large bag to free up the small bag slot).
*   **Candidate Collection:** Scans the backpack and all currently equipped bags for container items.
*   **Sorting:** Prioritizes candidates by size (largest first).
*   **Priority 1 (Empty Slot):** If an empty bag slot exists, it moves the largest candidate bag into that slot.
*   **Priority 2 (Upgrade):** If no empty slots exist, it looks for an equipped bag that is:
    1.  Smaller than the candidate.
    2.  Currently empty.
    3.  The smallest such bag (to minimize loss of capacity during swap).
*   **Swap Execution:** Removes the candidate from its current location, removes the old bag from the equip slot, equips the new bag, and attempts to store the old bag in the inventory. If storage fails, the old bag is destroyed.
*   **Reporting:** Sends a `BAG_EQUIP` event to the bridge with details of the changes and updated slot counts.
*   **Collaboration:** Uses `Player.Main` for inventory manipulation and `AiBotAI.Bridge` for reporting.

**`TryAutoEquip`**
Attempts to equip better combat gear (weapons and armor).
*   **Preconditions:** Does not run if the bot is in combat (`Unit.Main/IsInCombat`).
*   **Strategy:** Runs up to 20 passes to handle complex swaps.
*   **Scanning:** Iterates through all inventory slots (backpack and bags).
*   **Evaluation:** For each item, checks if it is equippable (`Player.Main/CanEquipItem`). If so, compares its `ScoreItem` value against the currently equipped item in the target slot.
*   **Upgrade Execution:**
    *   If the new item scores higher, it performs the swap.
    *   Handles the case where the target slot is empty (simple move).
    *   Handles the case where the target slot is occupied (swap: unequip old, equip new, store old).
    *   If storing the old item fails, it tries the slot the new item came from. If that also fails, the old item is destroyed.
    *   Calls `Player.Main/AutoUnequipOffhandIfNeed` to ensure valid off-hand states (e.g., dual-wielding restrictions).
*   **Reporting:** Sends an `EQUIP` event to the bridge.
*   **Collaboration:** Uses `Player.Main` for inventory and `AiBotAI.Bridge` for reporting.

### Item Scoring Core

**`ScoreItem`**
Calculates a numerical score representing the value of an item for the bot's specific class.
*   **Inputs:** Item prototype and the target equipment slot (though slot is currently unused in calculation, it is passed for context).
*   **Weight Retrieval:** Calls `GetClassWeights` to obtain class-specific multipliers.
*   **Stat Calculation:**
    1.  **Primary Stats:** Sums Strength, Agility, Stamina, Intellect, and Spirit multiplied by their respective class weights.
    2.  **On-Equip Spells:** Parses the item's `Spells` array for triggers with `ITEM_SPELLTRIGGER_ON_EQUIP`. It extracts aura effects for Spell Damage, Healing, MP5, Attack Power, and Resistances, applying class-specific weights. This is critical for Vanilla WoW where many stats are implemented as spells.
    3.  **Armor:** Adds armor value multiplied by a class-specific weight (lower for cloth wearers).
    4.  **Block:** Adds shield block value.
    5.  **Weapon DPS:** Calculates average DPS for weapons and multiplies by a class weight.
    6.  **Item Level:** Adds a base score based on Item Level to account for hidden budget.
    7.  **Quality Multiplier:** Applies a final multiplier based on item rarity (Grey < White < Green < Blue < Purple).
*   **Collaboration:** Calls `GetClassWeights` (local static), `SpellMgr` for spell data, and `Unit.Main` for class info.

**`GetClassWeights`**
Returns a struct of floating-point weights for various item attributes, tailored to the bot's class.
*   **Implementation:** A static function that switches on the class ID.
*   **Design:** Reflects Vanilla WoW meta-knowledge. For example, Warriors value Strength and Armor highly, while Mages value Intellect and Spell Damage. Armor is heavily downweighted for casters.
*   **Collaboration:** None (pure function).

**`EquipSlotForInvType`**
Maps an `InventoryType` enum value to an `EquipmentSlot` enum value.
*   **Implementation:** A static lookup function.
*   **Usage:** Used by `ChooseQuestReward` and potentially other parts of the codebase to determine where an item fits.
*   **Collaboration:** None (pure function).

## Cross-Unit Boundaries

*   **AiBotAI.Bridge:**
    *   **Called By:** `DoAutoLoot`, `TryAutoEquipBags`, `TryAutoEquip`.
    *   **Direction:** Outbound.
    *   **Reason:** To notify the C# coordinator of significant state changes (loot acquired, bags equipped, gear upgraded). This allows the external brain to update its internal model of the bot's inventory and wealth.
*   **AiBotAI.Main:**
    *   **Called By:** `DoAutoLoot` (via `UpdateAI` calling `DoAutoLoot`), `ScoreItem` (via `OnPacketReceived` calling `ScoreItem`).
    *   **Direction:** Inbound (for `DoAutoLoot`) and Inbound (for `ScoreItem`).
    *   **Reason:** `UpdateAI` (implemented in `AiBotAI.Main`) triggers the looting process after a kill. `OnPacketReceived` (implemented in `AiBotAI.Main`) may trigger item scoring for loot rolls or other interactions.
*   **AiBotAI.Bridge (Handlers):**
    *   **Called By:** `ChooseQuestReward`, `TryAutoEquipBags`, `TryAutoEquip`.
    *   **Direction:** Inbound.
    *   **Reason:** These methods are invoked by bridge handlers (`BridgeHandleQuestInteract`, `BridgeHandleUseGameObject`) implemented in `AiBotAI.Bridge` when the C# coordinator instructs the bot to interact with a quest giver or game object.
*   **Player.Main:**
    *   **Called By:** All loot/equip methods.
    *   **Direction:** Outbound.
    *   **Reason:** To manipulate the bot's inventory, money, and equipment state.
*   **Creature.Main:**
    *   **Called By:** `DoAutoLoot`.
    *   **Direction:** Outbound.
    *   **Reason:** To generate loot tables and manage corpse state.
*   **Group:**
    *   **Called By:** `DoAutoLoot`.
    *   **Direction:** Outbound.
    *   **Reason:** To handle group loot distribution rules and identify nearby members for gold sharing.
*   **ObjectMgr / SpellMgr:**
    *   **Called By:** `ChooseQuestReward`, `ScoreItem`.
    *   **Direction:** Outbound.
    *   **Reason:** To retrieve static data about items and spells.

## Data Model

This unit does not directly interact with any database tables. It operates entirely on in-memory objects (`Player`, `Creature`, `Item`, `Quest`, `Group`) provided by the server engine.

## Notable Implementation Details

1.  **On-Equip Spell Parsing:** The `ScoreItem` method explicitly parses `ITEM_SPELLTRIGGER_ON_EQUIP` spells. This is crucial for Vanilla WoW accuracy, as many "stats" (like +5 Spell Damage) are implemented as temporary auras applied on equip, not as direct item stats. Without this, the bot would undervalue caster gear significantly.
2.  **Bag Upgrade Cascading:** `TryAutoEquipBags` runs multiple passes (up to 8) and sorts candidates by size. This ensures that if a large bag is found in a small bag, the system can move items out of the small bag, equip the large bag, and then store the small bag, maximizing capacity efficiently.
3.  **Combat Safety:** `TryAutoEquip` checks `Unit.Main/IsInCombat` and aborts if true. This prevents the bot from unequipping gear during a fight, which could cause death or desync.
4.  **Gold Sharing Logic:** `DoAutoLoot` manually calculates gold shares for group members within XP distance. It does not rely solely on the engine's group loot system for gold, ensuring fair distribution even if the engine's default behavior varies.
5.  **Fallback Destruction:** If an item cannot be stored during a swap (inventory full), the code destroys the old item (`SetState(ITEM_REMOVED)`). This prevents infinite loops or stuck states but results in item loss.
6.  **Class-Specific Weights:** The `GetClassWeights` function contains hardcoded weights that reflect the Vanilla WoW meta. For instance, Armor is weighted very low for Mages and Priests, while Spell Damage is weighted very high. This requires maintenance if the meta shifts or if the bot is used in different expansions.
7.  **Static Linking Constraint:** The comment in the source notes that `ChooseQuestReward` and `ScoreItem` are in this TU because they depend on file-local statics (`GetClassWeights`, `EquipSlotForInvType`). Static functions cannot be linked across translation units, necessitating this structure.

## Member Reference

**`ChooseQuestReward`**: Method that selects the best quest reward index by comparing gear upgrade scores (via `ScoreItem`) and vendor values. Calls `Player.Main`, `ObjectMgr`, `QuestDef`, and `Log.Main`. Called by `AiBotAI.Bridge/BridgeHandleQuestInteract`.

**`DoAutoLoot`**: Method that executes the full looting process for a creature corpse. Generates loot, handles group loot rules, distributes gold among nearby group members, stores items, triggers auto-equip, and cleans up the corpse. Calls `Creature.Main`, `Player.Main`, `Group`, `AiBotAI.Bridge`, `Log.Main`, `Map.Main`, `Object`, `ObjectGuid`, `Unit.Main`, `WorldObject.Object`. Called by `AiBotAI.Main/UpdateAI`.

**`TryAutoEquipBags`**: Method that scans inventory for larger bags and equips them to increase capacity. Handles empty slots and upgrades of smaller empty bags. Runs multiple passes to handle cascading moves. Calls `AiBotAI.Bridge`, `Bag`, `game_Objects_Item`, `Log.Main`, `Object`, `Player.Main`, `Unit.Main`. Called by `AiBotAI.Bridge/BridgeHandleQuestInteract` and `AiBotAI.Bridge/BridgeHandleUseGameObject`.

**`GetClassWeights`**: Static function that returns class-specific weighting factors for item stats (Strength, Agility, Armor, Spell Damage, etc.). No external calls. Not called by other units in the map.

**`EquipSlotForInvType`**: Static function that maps an `InventoryType` to an `EquipmentSlot`. No external calls. Not called by other units in the map.

**`ScoreItem`**: Method that calculates a numerical score for an item based on its stats, on-equip spells, armor, weapon DPS, item level, and quality. Uses class-specific weights. Calls `ItemPrototype`, `SpellMgr`, `Unit.Main`. Called by `AiBotAI.Main/OnPacketReceived`.

**`TryAutoEquip`**: Method that scans inventory for better gear (weapons/armor) and equips it if it scores higher than the currently equipped item. Handles swaps and storage failures. Aborts if in combat. Calls `AiBotAI.Bridge`, `Bag`, `game_Objects_Item`, `Log.Main`, `Object`, `Player.Main`, `Unit.Main`. Called by `AiBotAI.Bridge/BridgeHandleQuestInteract` and `AiBotAI.Bridge/BridgeHandleUseGameObject`.

---

<!-- machine-true, projected from graph.json -->

## Map — AiBotAI.Loot

*Source:* AiBotAILoot.cpp, AiBotAIMain.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ChooseQuestReward | method | game_Objects_Item/GetProto, Log.Main/Out, ObjectMgr/GetItemPrototype, Player.Main/CanUseItem#2, Player.Main/GetItemByPos, Player.Main/GetName, QuestDef/GetQuestId, QuestDef/GetRewChoiceItemsCount | AiBotAI.Bridge/BridgeHandleQuestInteract | — |
| DoAutoLoot | method | AiBotAI.Bridge/BridgeSendEvent, Creature.Main/AllLootRemovedFromCorpse, Creature.Main/GenerateLootForBody, Creature.Main/GetGroupLootRecipient, Creature.Main/GetName, Creature.Main/GetOriginalLootRecipient, Creature.Main/SetLootRecipient, game_Group_Group/GroupLoot, game_Group_Group/NeedBeforeGreed, Group/GetFirstMember, Group/GetLootMethod, GroupReference/next, Log.Main/Out, Loot/clear, Map.Main/GetCreature, Object/GetEntry, Object/GetGUIDLow, Object/IsInWorld, ObjectGuid/GetCounter, Player.Main/AutoStoreLoot, Player.Main/GetGroup, Player.Main/GetMoney, Player.Main/GetName, Player.Main/LootMoney, Player.Main/ModifyMoney, Unit.Main/IsAlive, WorldObject.Object/GetMap, WorldObject.Object/IsWithinLootXPDist, WorldObject.Object/RemoveFlag | AiBotAI.Main/UpdateAI | — |
| TryAutoEquipBags | method | AiBotAI.Bridge/BridgeSendEvent, Bag/GetBagSize, Bag/IsEmpty, game_Objects_Item/GetProto, game_Objects_Item/RemoveFromUpdateQueueOf, game_Objects_Item/SetState, Log.Main/Out, Object/GetEntry, Object/IsInWorld, Player.Main/CanStoreItem, Player.Main/EquipItem, Player.Main/GetItemByPos, Player.Main/GetName, Player.Main/RemoveItem, Player.Main/StoreItem, Unit.Main/IsAlive | AiBotAI.Bridge/BridgeHandleQuestInteract, AiBotAI.Bridge/BridgeHandleUseGameObject | — |
| GetClassWeights | function | — | — | — |
| EquipSlotForInvType | function | — | — | — |
| ScoreItem | method | ItemPrototype/IsWeapon, SpellMgr/GetSpellEntry, SpellMgr/Instance, Unit.Main/GetClass | AiBotAI.Main/OnPacketReceived | — |
| TryAutoEquip | method | AiBotAI.Bridge/BridgeSendEvent, Bag/GetBagSize, game_Objects_Item/GetBagSlot, game_Objects_Item/GetProto, game_Objects_Item/GetSlot, game_Objects_Item/RemoveFromUpdateQueueOf, game_Objects_Item/SetState, Log.Main/Out, Object/GetEntry, Object/IsInWorld, Player.Main/AutoUnequipOffhandIfNeed, Player.Main/CanEquipItem, Player.Main/CanStoreItem, Player.Main/EquipItem, Player.Main/GetItemByPos, Player.Main/GetName, Player.Main/RemoveItem, Player.Main/StoreItem, Unit.Main/IsAlive, Unit.Main/IsInCombat | AiBotAI.Bridge/BridgeHandleQuestInteract, AiBotAI.Bridge/BridgeHandleUseGameObject | — |

---

<!-- verify: boundary-bleed | foreign: AiBotAI -->
