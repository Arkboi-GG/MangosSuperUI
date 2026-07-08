<!-- provenance: boundary-bleed -->
# WorldSession.LootHandler

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# WorldSession.LootHandler

## Purpose & Responsibilities

`WorldSession.LootHandler` implements the server-side logic for processing loot-related network opcodes in the `wowvmangos` emulator. It acts as the bridge between client requests (via `WorldSession`) and the game world entities (`Creature`, `GameObject`, `Corpse`, `Item`) that hold lootable resources.

Its primary responsibilities are:
1.  **Validation:** Verifying that a player is eligible to loot a specific target based on distance, class restrictions (e.g., Rogue pickpocketing), group loot rules (Master Loot, Free-for-All), and combat states.
2.  **Loot Distribution:** Handling the transfer of items and gold from loot containers to player inventories, respecting group sharing mechanics for money and individual entitlements for items.
3.  **State Management:** Updating the visual and logical state of looted objects (e.g., closing chests, despawning corpses, removing lootable flags) and notifying other players of changes.
4.  **Anti-Cheat:** Detecting and preventing invalid loot attempts, such as looting non-existent targets, looting while stunned, or attempting to steal items in Master Loot mode.

This unit does not generate loot tables or probabilities; it consumes pre-generated `Loot` objects attached to world entities. It relies heavily on `LootMgr` for item eligibility checks and `Player.Main` methods for inventory management.

## Member-by-Member Behavior

### Opening and Initiating Loot

**`HandleLootOpcode`**
This method processes the initial request to open a loot window. It performs strict validation before sending the loot data to the client:
-   **Target Validation:** Ensures the GUID refers to a creature, player, or corpse. If not, it logs an anti-cheat action (`ItemsCheck`) via `WorldSession.Main/ProcessAnticheatAction` and rejects the request.
-   **Player State:** Checks if the player is alive, in the world, standing (not sitting/sleeping), and not stunned. It also checks for play-time restrictions (`PLAYER_FLAGS_NO_PLAY_TIME`) on supported builds.
-   **Spell Interruption:** If the player is casting a non-melee spell, it interrupts the cast via `SpellCaster/InterruptNonMeleeSpells`.
-   **Action:** Upon success, it calls `Player.Main/SendLoot` to transmit the loot contents to the client.

**`HandleLootReleaseOpcode`**
Processes the client's request to close the loot window. It delegates the actual cleanup logic to `DoLootRelease`, using the internally stored loot GUID retrieved via `Player.Main/GetLootGuid` to prevent cheating via modified client packets.

**`DoLootRelease`**
This is the core cleanup routine for releasing loot. It handles different entity types distinctly:
-   **General:** Clears the player's loot GUID via `Player.Main/SetLootGuid` and removes the `UNIT_FLAG_LOOTING` flag.
-   **GameObjects:**
    -   For chests, it sets the state to `GO_STATE_READY` (closed) via `GameObject/SetGoState`.
    -   For doors, it triggers `GameObject/UseDoorOrButton` and AI `GameObjectAI/OnUse`.
    -   For veins/minerals, it calculates remaining uses based on mining skill and configuration rates. If uses remain, it stays active; otherwise, it deactivates via `GameObject/SetLootState`.
    -   For fishing holes, it increments use count via `GameObject/AddUse` and deactivates if max uses are reached.
    -   Partially looted chests enter a cooldown state via `GameObject/SetCooldownTime` for 5 minutes.
-   **Corpses:** Clears loot and removes the `CORPSE_DYNFLAG_LOOTABLE` flag.
-   **Items:**
    -   For disenchanting loot, it auto-stores remaining items via `Player.Main/AutoStoreLoot` and destroys the source item via `Player.Main/DestroyItem`.
    -   For normal item loot (e.g., bags), it destroys the source item only if fully looted and not a bag itself.
-   **Creatures:**
    -   If fully looted, it calls `Creature.Main/AllLootRemovedFromCorpse` and removes the lootable flag.
    -   If partially looted in a group, it resets the `roundRobinPlayer` if the current player was the designated looter, allowing the next group member to loot via `game_Group_Group/SendLooter`.
-   **Final Step:** Removes the player from the loot's internal looter list via `Loot/RemoveLooter`.

### Acquiring Items

**`HandleAutostoreLootItemOpcode`**
Handles the automatic storage of a specific item from the loot table into the player's inventory.
-   **Entity Resolution:** Identifies the loot source (GameObject, Item, Corpse, or Creature) and retrieves the `Loot` pointer.
-   **Distance Checks:** Skips distance checks for owned GameObjects (like fishing bobbers) and fishing holes. For creatures, it applies specific ranges for skinning vs. standard looting via `WorldObject.Object/IsWithinDistInMap` or `WorldObject.Object/IsWithinCombatDistInMap`.
-   **Eligibility:** Uses `LootMgr/AllowedForPlayer` to verify if the player can take the item. It explicitly blocks players from taking items in Master Loot mode unless they are the master looter or the item is below the threshold/quest-related.
-   **Storage:** Attempts to store the item using `Player.Main/StoreNewItem`. If successful, it updates quest item statuses, notifies other players of removal via `LootMgr/NotifyItemRemoved`, decrements the unlooted count, and sends the new item packet to the client via `Player.Main/SendNewItem`.

### Acquiring Money

**`HandleLootMoneyOpcode`**
Processes the collection of gold from a loot source.
-   **Sharing Logic:** By default, money is shared among group members within loot XP distance. Exceptions include:
    -   Looting from an `Item` (e.g., disenchanting).
    -   Rogue pickpocketing (money is not shared).
-   **Distribution:** If sharing is enabled, it iterates through group members via `Group/GetFirstMember`, calculates an equal split, and calls `Player.Main/LootMoney` for each. Otherwise, the solo player takes all gold.
-   **Cleanup:** Sets the loot's gold amount to zero and notifies the loot manager via `LootMgr/NotifyMoneyRemoved`.

### Master Loot Distribution

**`HandleLootMasterGiveOpcode`**
Allows the Master Looter to give an item to another group member.
-   **Authorization:** Verifies the requester is the Master Looter via `Group/GetLooterGuid` and in Master Loot mode via `Group/GetLootMethod`.
-   **Target Validation:** Ensures the recipient is in the same raid/group, on the same map, and within reward distance via `Player.Main/IsAtGroupRewardDistance`. It also checks for play-time restrictions on the recipient.
-   **Inventory Check:** Verifies the recipient has space via `Player.Main/CanStoreNewItem`. If not, it sends specific error codes (bag full, unique item limit) to both the master and the recipient.
-   **Transfer:** Stores the item in the recipient's inventory via `Player.Main/StoreNewItem`, marks it as looted in the loot table, and notifies the loot manager via `LootMgr/NotifyItemRemoved`. It logs the transaction for audit purposes via `Log.Main/Out`.

## Cross-Unit Boundaries

### Collaboration with `Player.Main`
`WorldSession.LootHandler` relies extensively on `Player.Main` for inventory and state management.
-   **Direction:** `LootHandler` calls `Player.Main`.
-   **Why:** `Player` manages the complex logic of inventory slots, equipment rules, and client communication. `LootHandler` determines *what* to give, but `Player` determines *if* it can be held and *how* to inform the client.
-   **Key Calls:** `StoreNewItem`, `CanStoreNewItem`, `SendNewItem`, `LootMoney`, `SendLootError`, `SendLootRelease`, `GetLootGuid`, `SetLootGuid`, `GetGroup`, `GetItemByGuid`, `GetMaxLootDistance`, `SendEquipError`, `SendNotifyLootItemRemoved`, `OnReceivedItem`, `GetShortDescription`, `Player`, `IsAtGroupRewardDistance`, `GetName`, `AutoStoreLoot`, `DestroyItem`, `GetSkillValue`, `IsInRaidWith`.

### Collaboration with `LootMgr`
`LootMgr` provides the business logic for loot eligibility and notification.
-   **Direction:** `LootHandler` calls `LootMgr`.
-   **Why:** `LootHandler` deals with network packets and high-level flow, while `LootMgr` encapsulates the rules for who can take what (e.g., quest requirements, class restrictions) and manages the internal state of the `Loot` object (removing items, updating counts).
-   **Key Calls:** `AllowedForPlayer`, `LootItemInSlot`, `NotifyItemRemoved`, `NotifyMoneyRemoved`, `IsAllowedLooter`, `GetLootTarget`, `NotifyQuestItemRemoved`.

### Collaboration with `Group`
`Group` defines the social context for looting.
-   **Direction:** `LootHandler` calls `Group`.
-   **Why:** To enforce loot modes (Master, Free-for-All, Round Robin) and identify eligible recipients for shared rewards.
-   **Key Calls:** `GetLootMethod`, `GetLooterGuid`, `GetFirstMember`, `GetMembersCount`, `SendLooter`.

### Collaboration with `Map.Main`
`Map.Main` provides access to the world entities.
-   **Direction:** `LootHandler` calls `Map.Main`.
-   **Why:** To retrieve the actual `Creature`, `GameObject`, or `Corpse` objects associated with the GUIDs provided in the packets.
-   **Key Calls:** `GetCreature`, `GetGameObject`, `GetCorpse`.

### Collaboration with `GameObject` and `Creature`
These units represent the loot sources.
-   **Direction:** `LootHandler` calls `GameObject`/`Creature`.
-   **Why:** To update their visual state (open/close, despawn) and access their embedded `loot` structures.
-   **Key Calls:** `SetLootState`, `SetGoState`, `UseDoorOrButton`, `AllLootRemovedFromCorpse`, `IsWithinDistInMap`, `GetOwnerGuid`, `GetGoType`, `HasGeneratedLoot`, `SetLootState`, `GetBagSlot`, `GetSlot`, `IsBag`, `AddUse`, `AI`, `GetGOInfo`, `GetUseCount`, `isSpawnedByDefault`, `SetCooldownTime`.

### Collaboration with `Log.Main`
-   **Direction:** `LootHandler` calls `Log.Main`.
-   **Why:** To record loot transactions for debugging and anti-cheat auditing.
-   **Key Calls:** `Out` (with various log levels like `LOG_LOOTS`, `LOG_BASIC`).

### Collaboration with `WorldSession.Main`
-   **Direction:** `LootHandler` calls `WorldSession.Main`.
-   **Why:** To access the player object associated with the session and process anti-cheat actions.
-   **Key Calls:** `GetPlayer`, `ProcessAnticheatAction`.

### Collaboration with `ObjectGuid` and `Object`
-   **Direction:** `LootHandler` calls `ObjectGuid`/`Object`.
-   **Why:** To parse and validate the identifiers of loot sources and check their existence in the world.
-   **Key Calls:** `GetHigh`, `GetString`, `IsCreature`, `IsEmpty`, `operator==`, `GetGUIDLow`, `GetObjectGuid`, `IsInWorld`, `HasFlag`, `IsCorpse`, `IsPlayer`, `IsAnyTypeCreature`, `GetGUID`, `operator!`, `operator!=`, `IsGameObject`.

### Collaboration with `Unit.Main` and `WorldObject.Object`
-   **Direction:** `LootHandler` calls `Unit.Main`/`WorldObject.Object`.
-   **Why:** To check player and entity states (alive, class, distance, combat).
-   **Key Calls:** `GetClass`, `IsAlive`, `GetMap`, `IsWithinCombatDistInMap`, `IsWithinDistInMap`, `IsWithinLootXPDist`, `GetStandState`, `HasUnitState`, `IsNonMeleeSpellCasted`, `InterruptNonMeleeSpells`, `ExecuteDelayedActions`, `ForceValuesUpdateAtIndex`, `RemoveFlag`.

### Collaboration with `Loot`
-   **Direction:** `LootHandler` calls `Loot`.
-   **Why:** To manage the internal state of the loot table (clearing, checking if looted, removing looters).
-   **Key Calls:** `clear`, `HasFFAQuestItems`, `isLooted`, `RemoveLooter`.

### Collaboration with `ScriptMgr` and `World`
-   **Direction:** `LootHandler` calls `ScriptMgr`/`World`.
-   **Why:** To trigger scripted events for chests and retrieve configuration values for mining/fishing mechanics.
-   **Key Calls:** `OnProcessEvent`, `getConfig#2`.

### Collaboration with `shared_Util`
-   **Direction:** `LootHandler` calls `shared_Util`.
-   **Why:** To perform random number generation for vein depletion chances.
-   **Key Calls:** `roll_chance_f`, `urand`.

### Collaboration with `ObjectAccessor`
-   **Direction:** `LootHandler` calls `ObjectAccessor`.
-   **Why:** To find the target player object by GUID during Master Loot distribution.
-   **Key Calls:** `FindPlayer`.

## Data Model

This unit does not interact directly with database tables. It operates entirely on in-memory objects (`Loot`, `Player`, `Creature`, `GameObject`). The `Loot` objects themselves are typically populated by other subsystems (e.g., `LootMgr` reading from DBC files or database templates) before this handler is invoked. Therefore, no SQL queries or table interactions occur within `LootHandler.cpp`.

## Notable Implementation Details

1.  **Distance Check Exceptions:**
    In `HandleAutostoreLootItemOpcode`, distance checks are skipped for `GameObject`s if the player is the owner (e.g., fishing bobbers) or if the GO is a fishing hole. This prevents legitimate actions from failing due to minor positioning errors.

2.  **Rogue Pickpocketing Logic:**
    In `HandleAutostoreLootItemOpcode` and `HandleLootMoneyOpcode`, special handling exists for Rogues. A Rogue can loot a living creature if `lootForPickPocketed` is true. Crucially, money from pickpocketing is *not* shared with the group (`shareMoneyWithGroup = false`), reflecting game mechanics where pickpocketed gold is personal.

3.  **Vein/Mineral Depletion Algorithm:**
    In `DoLootRelease`, the depletion of mineral veins is not simple binary. It uses a probabilistic formula based on:
    -   Configuration rates (`CONFIG_FLOAT_RATE_MINING_AMOUNT`, `CONFIG_FLOAT_RATE_MINING_NEXT`).
    -   Player's Mining skill relative to the required skill.
    -   Number of uses already taken.
    The chance to continue is calculated as `pow(0.8 * chance_rate, 4 * (1/max_amount) * uses)`. This allows veins to last longer for skilled miners but eventually deplete.

4.  **Master Loot Anti-Theft:**
    `HandleAutostoreLootItemOpcode` explicitly checks if the loot source is a creature and if the group is in `MASTER_LOOT` mode. If so, it denies the autostore request unless the item is a quest item, free-for-all, or below the threshold. This prevents players from bypassing the Master Looter by quickly clicking "autostore" before the master can assign the item.

5.  **Round Robin Reset:**
    In `DoLootRelease`, if a player releases loot in a group setting (non-Master Loot), and that player was the `roundRobinPlayer`, the system resets the round-robin pointer to `0` and notifies the group. This ensures the loot doesn't get stuck on a player who has left or released.

6.  **Anti-Cheat Logging:**
    `HandleLootOpcode` calls `WorldSession.Main/ProcessAnticheatAction` if a player attempts to loot a GUID that is not a creature, player, or corpse. This helps identify hacked clients trying to exploit invalid loot windows.

7.  **Disenchanting Cleanup:**
    In `DoLootRelease`, for `LOOT_DISENCHANTING`, the source item is always destroyed after loot release, regardless of whether all loot was taken. This reflects the permanent nature of disenchanting.

## Member Reference

**HandleAutostoreLootItemOpcode**: Processes the client request to automatically store a specific item from the loot table into the player's inventory. Validates distance, eligibility, and group loot rules before storing the item and updating loot state.

**HandleLootMoneyOpcode**: Processes the client request to collect gold from a loot source. Calculates shares for group members within loot XP distance (excluding pickpocketing and item loot) and distributes the gold accordingly.

**HandleLootOpcode**: Initiates the loot window by validating the target and player state (alive, standing, not stunned). Interrupts non-melee spells and sends the loot contents to the client. Logs anti-cheat violations for invalid targets.

**HandleLootReleaseOpcode**: Entry point for releasing loot. Delegates to `DoLootRelease` using the server-stored loot GUID to ensure integrity.

**DoLootRelease**: Core logic for cleaning up loot after release. Handles entity-specific behaviors: closing chests, depleting veins/minerals based on skill and usage, despawning corpses, destroying disenchanting items, and resetting group round-robin pointers. Removes the player from the looter list.

**HandleLootMasterGiveOpcode**: Allows the Master Looter to assign an item to another group member. Validates authorization, recipient eligibility (distance, inventory space), and transfers the item while logging the transaction.

---

<!-- machine-true, projected from graph.json -->

## Map — WorldSession.LootHandler

*Source:* LootHandler.cpp, WorldSession.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| HandleAutostoreLootItemOpcode | method | GameObject/GetGoType, GameObject/GetOwnerGuid, game_Objects_Item/HasGeneratedLoot, game_Objects_Item/SetLootState, Group/GetLootMethod, Log.Main/Out, Loot/GetPlayerQuestItems, LootMgr/AllowedForPlayer, LootMgr/GetLootTarget, LootMgr/LootItemInSlot, LootMgr/NotifyItemRemoved, LootMgr/NotifyQuestItemRemoved, Map.Main/GetCorpse, Map.Main/GetCreature, Map.Main/GetGameObject, Object/GetGUIDLow, Object/GetObjectGuid, ObjectGuid/GetHigh, ObjectGuid/GetString, ObjectGuid/IsCreature, ObjectGuid/IsEmpty, ObjectGuid/operator==, Player.Main/CanStoreNewItem, Player.Main/GetGroup, Player.Main/GetItemByGuid, Player.Main/GetLootGuid, Player.Main/GetMaxLootDistance, Player.Main/GetShortDescription, Player.Main/OnReceivedItem, Player.Main/Player, Player.Main/SendEquipError, Player.Main/SendLootError, Player.Main/SendLootRelease, Player.Main/SendNewItem, Player.Main/SendNotifyLootItemRemoved, Player.Main/StoreNewItem, Unit.Main/GetClass, Unit.Main/IsAlive, WorldObject.Object/GetMap, WorldObject.Object/IsWithinCombatDistInMap, WorldObject.Object/IsWithinDistInMap, WorldSession.Main/GetPlayer | — | — |
| HandleLootMoneyOpcode | method | GameObject/GetOwnerGuid, game_Objects_Item/HasGeneratedLoot, game_Objects_Item/SetLootState, Group/GetFirstMember, Group/GetMembersCount, GroupReference/next, LootMgr/NotifyMoneyRemoved, Map.Main/GetCorpse, Map.Main/GetCreature, Map.Main/GetGameObject, Object/GetObjectGuid, Object/IsInWorld, ObjectGuid/GetHigh, ObjectGuid/operator!, ObjectGuid/operator==, Player.Main/GetGroup, Player.Main/GetItemByGuid, Player.Main/GetLootGuid, Player.Main/GetMaxLootDistance, Player.Main/LootMoney, Player.Main/SendLootMoneyNotify, Unit.Main/GetClass, Unit.Main/IsAlive, WorldObject.Object/GetMap, WorldObject.Object/IsWithinDistInMap, WorldObject.Object/IsWithinLootXPDist, WorldSession.Main/GetPlayer | — | — |
| HandleLootOpcode | method | Object/HasFlag, Object/IsInWorld, ObjectGuid/IsAnyTypeCreature, ObjectGuid/IsCorpse, ObjectGuid/IsPlayer, Player.Main/SendLoot, Player.Main/SendLootError, SpellCaster/InterruptNonMeleeSpells, SpellCaster/IsNonMeleeSpellCasted, Unit.Main/GetStandState, Unit.Main/HasUnitState, Unit.Main/IsAlive, WorldSession.Main/GetPlayer, WorldSession.Main/ProcessAnticheatAction | — | — |
| HandleLootReleaseOpcode | method | Player.Main/GetLootGuid, WorldSession.Main/GetPlayer | — | — |
| DoLootRelease | method | Creature.Main/AllLootRemovedFromCorpse, GameObject/AddUse, GameObject/AI, GameObject/GetGOInfo, GameObject/GetGoType, GameObject/GetUseCount, GameObject/isSpawnedByDefault, GameObject/SetCooldownTime, GameObject/SetGoState, GameObject/SetLootState, GameObject/UseDoorOrButton, GameObjectAI/OnUse, game_Group_Group/SendLooter, game_Objects_Item/GetBagSlot, game_Objects_Item/GetSlot, game_Objects_Item/IsBag, game_Objects_Item/SetLootState, Group/GetLootMethod, Log.Main/Out, Loot/clear, Loot/HasFFAQuestItems, Loot/isLooted, Loot/RemoveLooter, Map.Main/GetCorpse, Map.Main/GetCreature, Map.Main/GetGameObject, Map.Main/ScriptsStart, Object/GetGUID, Object/GetGUIDLow, Object/GetObjectGuid, Object/IsInWorld, ObjectGuid/GetHigh, ObjectGuid/GetString, ObjectGuid/ObjectGuid, Player.Main/AutoStoreLoot, Player.Main/DestroyItem, Player.Main/GetGroup, Player.Main/GetItemByGuid, Player.Main/GetSkillValue, Player.Main/SendLootRelease, Player.Main/SetLootGuid, ScriptMgr/OnProcessEvent, shared_Util/roll_chance_f, shared_Util/urand, Unit.Main/IsAlive, World/getConfig#2, WorldObject.Object/ExecuteDelayedActions, WorldObject.Object/ForceValuesUpdateAtIndex, WorldObject.Object/GetMap, WorldObject.Object/RemoveFlag, WorldSession.Main/GetPlayer | Player.Main/OnDisconnected, Player.Main/RemoveFromWorld, Player.Main/SendLoot, Player.Main/SetDeathState, Player.Main/SwapItem, Player.Main/TeleportTo, Unit.Main/ModConfuseSpell, Unit.SpellAuras/HandleAuraModStun, Unit.SpellAuras/HandleModCharm, Unit.SpellAuras/ModPossess, WorldSession.Main/LogoutPlayer, WorldSession.MiscHandler/HandleLogoutRequestOpcode, WorldSession.MovementHandler/HandleMoverRelocation, WorldSession.SpellHandler/HandleCastSpellOpcode | — |
| HandleLootMasterGiveOpcode | method | Group/GetLooterGuid, Group/GetLootMethod, Log.Main/Out, LootMgr/IsAllowedLooter, LootMgr/NotifyItemRemoved, Map.Main/GetCreature, Map.Main/GetGameObject, Object/GetObjectGuid, Object/HasFlag, Object/IsInWorld, ObjectAccessor/FindPlayer, ObjectGuid/GetString, ObjectGuid/IsCreature, ObjectGuid/IsGameObject, ObjectGuid/operator!=, Player.Main/CanStoreNewItem, Player.Main/GetGroup, Player.Main/GetLootGuid, Player.Main/GetName, Player.Main/GetShortDescription, Player.Main/IsAtGroupRewardDistance, Player.Main/OnReceivedItem, Player.Main/Player, Player.Main/SendEquipError, Player.Main/SendLootError, Player.Main/SendLootRelease, Player.Main/SendNewItem, Player.Main/StoreNewItem, Unit.Main/IsInRaidWith, WorldObject.Object/GetMap, WorldObject.Object/IsInMap, WorldSession.Main/GetPlayer | — | — |

---

<!-- verify: boundary-bleed | foreign: update, WorldSession -->
