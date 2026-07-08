# GossipDef

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# GossipDef

**Purpose & Responsibilities**

`GossipDef` defines the core data structures and network serialization logic for non-player character (NPC) interaction menus in the WoW server emulator. It manages two distinct types of UI interactions: **Gossip Menus** (custom scripted options like vendors, trainers, or quest-specific dialogue) and **Quest Menus** (standard quest giver interfaces).

The unit provides:
1.  **Data Aggregation:** Classes (`GossipMenu`, `QuestMenu`) that collect menu items, icons, text, and associated actions during the preparation phase of an NPC interaction.
2.  **Network Serialization:** Methods within `PlayerMenu` that construct specific World of Warcraft protocol packets (`SMSG_GOSSIP_MESSAGE`, `SMSG_QUESTGIVER_*`, etc.) to send these aggregated menus to the client.
3.  **Localization Handling:** Logic to resolve localized text for gossip messages, quest titles, and NPC dialogue based on the player's session locale.
4.  **State Management:** Tracking whether a menu is empty, discovered, or contains specific items, facilitating control flow in higher-level script handlers.

This unit does not handle the business logic of *which* items appear in a menu (that is done by `ScriptMgr` and individual NPC scripts); it only handles the *storage* and *transmission* of those items.

## Member-by-Member Behavior

### Gossip Menu Construction (`GossipMenu`)

The `GossipMenu` class acts as a container for custom gossip options. It stores a list of `GossipMenuItem` structs, each containing an icon, message text, sender/action IDs, and optional box text (for password prompts).

*   **`GossipMenu` (ctor):** Initializes the menu with a `WorldSession` pointer. It pre-allocates memory for 16 items (`m_gItems.reserve(16)`) to optimize performance, assuming most menus are small. It resets the menu ID and discovery flag.
*   **`~GossipMenu` (dtor):** Calls `ClearMenu()` to ensure resources are freed.
*   **`AddMenuItem` (overloads):** There are five overloads. The primary implementation (`AddMenuItem#2`) validates that the item count does not exceed `GOSSIP_MAX_MENU_ITEMS` (32) via `MANGOS_ASSERT`. It constructs a `GossipMenuItem` and pushes it to the internal vector.
    *   `AddMenuItem#3` and `AddMenuItem#4` are convenience wrappers that convert `char const*` arguments to `std::string` before calling the primary overload.
    *   `AddMenuItem#5` handles localization. It takes integer IDs for text (`itemText`, `boxText`). If positive, it retrieves `BroadcastText` from `ObjectMgr`; if negative, it retrieves `MangosString`. It resolves the text based on the session's locale index and the player's gender (via `Unit.Main/GetGender`), then calls the primary `AddMenuItem`.
*   **`AddGossipMenuItemData`:** Stores auxiliary action data (`action_menu`, `action_poi`, `action_script`) in a parallel vector `m_gItemsData`. This allows scripts to associate complex actions with a menu item index without embedding them in the `GossipMenuItem` struct itself.
*   **`MenuItemSender`, `MenuItemAction`, `MenuItemCoded`:** Accessors that retrieve specific fields from a `GossipMenuItem` by index. They perform bounds checking; if the index is out of range, they return default values (0 or false).
*   **`ClearMenu`:** Clears both the item list and the data list, resetting the menu ID and discovery flag.
*   **`SetMenuId`, `GetMenuId`:** Simple getters/setters for the gossip menu ID, used to identify the menu type in scripts.
*   **`SetDiscoveredNode`, `IsJustDiscoveredNode`:** Flags used to suppress automatic gossip menu opening when a player discovers a map node, preventing UI clutter.
*   **`MenuItemCount`, `Empty`, `GetItem`, `GetItemData`:** Standard container accessors. `GetItem` returns a const reference to the item at the given index.
*   **`GetMenuSession`:** Returns the stored `WorldSession` pointer, allowing methods to access player context.

### Quest Menu Construction (`QuestMenu`)

The `QuestMenu` class is simpler, storing only `QuestMenuItem` structs (Quest ID and Icon).

*   **`QuestMenu` (ctor):** Pre-allocates memory for 16 items.
*   **`~QuestMenu` (dtor):** Calls `ClearMenu()`.
*   **`AddMenuItem` (`AddMenuItem#6`):** Validates that the quest template exists via `ObjectMgr/GetQuestTemplate`. If valid, it asserts the item count limit and adds the item. This ensures only valid quests are added to the menu.
*   **`HasItem`:** Iterates through the item list to check if a specific Quest ID is present.
*   **`ClearMenu`:** Clears the item list.
*   **`MenuItemCount`, `Empty`, `GetItem`:** Standard container accessors.

### Player Menu & Network Transmission (`PlayerMenu`)

`PlayerMenu` aggregates `GossipMenu` and `QuestMenu` instances and handles the actual packet construction and sending. It is instantiated per-player session.

*   **`PlayerMenu` (ctor):** Initializes the nested `GossipMenu` and `QuestMenu` objects with the session pointer.
*   **`~PlayerMenu` (dtor):** Calls `ClearMenus()`.
*   **`ClearMenus`:** Clears both the gossip and quest menus. This is called frequently by `ScriptMgr` and `Player.Main` to reset state between interactions.
*   **`GossipOptionSender`, `GossipOptionAction`, `GossipOptionCoded`:** Delegates to the corresponding `GossipMenu` methods. These are used by `WorldSession.NPCHandler` to process player selections.
*   **`SendGossipMenu`:** Constructs the `SMSG_GOSSIP_MESSAGE` packet.
    *   It calculates packet size dynamically based on the number of gossip and quest items.
    *   It serializes the NPC GUID, text ID, and counts.
    *   For each gossip item, it writes the index, icon, coded flag, and message text. Note: The icon size depends on the client build (`CLIENT_BUILD_1_5_1`), using `uint8` for newer clients and `uint32` for older ones.
    *   For each quest item, it retrieves the quest template, resolves the localized title, and writes the quest ID, icon, level, and title.
    *   Finally, it sends the packet via `WorldSession.Main/SendPacket`.
*   **`CloseGossip`:** Sends an empty `SMSG_GOSSIP_COMPLETE` packet to close the gossip window on the client.
*   **`SendPointOfInterest` (overloads):**
    *   `SendPointOfInterest#1` (Outdated): Sends a POI with raw coordinates and icon data.
    *   `SendPointOfInterest#2`: Takes a POI ID, retrieves the `PointOfInterest` structure from `ObjectMgr`, resolves the localized icon name, and sends the `SMSG_GOSSIP_POI` packet. It logs an error if the POI ID is invalid.
*   **`SendTalking` (overloads):**
    *   `SendTalking#2`: Takes a text ID, retrieves `NpcText` from `ObjectMgr`, and constructs an `SMSG_NPC_TEXT_UPDATE` packet. It iterates through 8 possible text options, resolving localized broadcast text for male/female genders. If text is missing, it falls back to default placeholders ("Greetings $N").
    *   `SendTalking#1`: Takes raw title and text strings and constructs a similar `SMSG_NPC_TEXT_UPDATE` packet, repeating the text 8 times to fill the expected structure.
*   **`SendQuestGiverStatus`:** Sends `SMSG_QUESTGIVER_STATUS` with the NPC GUID and status byte.
*   **`SendQuestGiverQuestList`:** Sends `SMSG_QUESTGIVER_QUEST_LIST`. It includes a greeting message (localized if available) and iterates through the `QuestMenu` items, serializing each quest's ID, icon, level, and localized title.
*   **`SendQuestGiverQuestDetails`:** Sends `SMSG_QUESTGIVER_QUEST_DETAILS`. It serializes the quest title, details, and objectives (all localized). It handles reward visibility flags (`QUEST_FLAGS_HIDDEN_REWARDS`). If rewards are visible, it iterates through choice items and fixed rewards, retrieving `ItemPrototype` from `ObjectMgr` to get display info IDs. It also includes emote data.
*   **`SendQuestGiverOfferReward`:** Sends `SMSG_QUESTGIVER_OFFER_REWARD`. Similar to details, but focuses on reward text and emotes. It calculates the number of valid emotes to send.
*   **`SendQuestGiverRequestItems`:** Sends `SMSG_QUESTGIVER_REQUEST_ITEMS`. It checks if the quest is completable and if items are required. If no items are required and the quest is completable, it redirects to `SendQuestGiverOfferReward`. Otherwise, it serializes the required items, including their display info. It sets specific flags (`0x02`, `0x03`, `0x04`, `0x08`) based on completion status.

### Utility Functions

*   **`IsValidGossipOptionIconForBuild`:** A static inline function that checks if a given gossip icon ID is valid for the current supported client build. It disables icons introduced in later patches (e.g., `GOSSIP_ICON_BATTLE` in 1.7, `GOSSIP_ICON_DOT` in 1.10) for older clients. Called by `ObjectMgr/LoadGossipMenuItems`.

## Cross-Unit Boundaries

### Calls Out

*   **`Errors/PrintStacktraceAndThrow`:** Called by `AddMenuItem#2` and `AddMenuItem#6` if assertions fail (though in release builds, assertions may be disabled, the potential for invalid state remains).
*   **`ObjectMgr`:** Extensively used to retrieve game data:
    *   `GetBroadcastText`, `GetMangosString`: For localized gossip text.
    *   `GetQuestTemplate`, `GetQuestLocale`, `GetQuestGreetingLocale`: For quest data and localization.
    *   `GetItemPrototype`: For item display info in quest rewards/requirements.
    *   `GetPointOfInterest`, `GetPointOfInterestLocale`: For POI data.
    *   `GetNpcText`, `GetBroadcastTextLocale`: For NPC dialogue.
*   **`Unit.Main/GetGender`:** Used in `AddMenuItem#5` to resolve gender-specific broadcast text.
*   **`WorldSession.Main`:**
    *   `GetPlayer`, `GetSessionDbLocaleIndex`: To access player context and locale settings.
    *   `SendPacket`: To transmit constructed packets to the client.
*   **`ByteBuffer` / `WorldPacket`:** Used for constructing binary network packets.
*   **`Log.Main/Out`:** Used in `SendPointOfInterest#2` to log errors for invalid POI IDs.

### Called By

*   **`Player.Main`:** Prepares gossip and quest menus (`PrepareGossipMenu`, `PrepareQuestMenu`), sends prepared gossip (`SendPreparedGossip`), and handles gossip selection (`OnGossipSelect`).
*   **`ScriptMgr`:** Triggers gossip hello/select events (`OnGossipHello`, `OnGossipSelect`) and dialog status checks.
*   **`WorldSession.NPCHandler`:** Handles the opcode for selecting a gossip option (`HandleGossipSelectOptionOpcode`), using `GossipOptionSender/Action/Coded` to determine the outcome.
*   **`WorldSession.QuestHandler`:** Handles various quest-related opcodes (accept, cancel, query, reward), calling `CloseGossip` and `SendQuestGiver*` methods.
*   **`Creature.Main`:** Checks interaction capabilities (`CanInteractWithBattleMaster`, `IsTrainerOf`), clearing menus as part of the interaction flow.
*   **`ChatHandler.DebugCommands`:** Uses `SendPointOfInterest#1` for debugging purposes.
*   **Numerous NPC Scripts:** Scripts in zones like `burning_steppes`, `instance_naxxramas`, `custom_creatures`, etc., call `AddMenuItem` and `SendGossipMenu` to define custom NPC behaviors.

## Data Model

This unit does not directly interact with database tables. It relies on `ObjectMgr` to provide data that was loaded from the database during server startup. The relevant tables (inferred from `ObjectMgr` usage) include:
*   `broadcast_text`: For gossip and NPC dialogue.
*   `mangos_string`: For custom server strings.
*   `quest_template`: For quest definitions.
*   `quest_locale`: For localized quest text.
*   `creature_template`: For NPC greetings (via `GetQuestGreetingLocale`).
*   `points_of_interest`: For POI data.
*   `item_template`: For item display info.

No direct SQL queries are executed in this unit.

## Notable Implementation Details

1.  **Client Build Compatibility:** `SendGossipMenu` uses preprocessor directives (`#if SUPPORTED_CLIENT_BUILD > CLIENT_BUILD_1_5_1`) to change the size of the gossip icon field in the packet. Older clients expect a `uint32` icon, while newer ones expect a `uint8`. This is critical for maintaining compatibility with different WoW versions.
2.  **Localization Fallback:** In `SendTalking#2`, if localized text is missing, the code falls back to a default string ("Greetings $N") and zeroed emote data. This prevents packet corruption but may result in generic-looking dialogue.
3.  **Quest Reward Visibility:** `SendQuestGiverQuestDetails` checks `QUEST_FLAGS_HIDDEN_REWARDS`. If set, it sends zeros for reward counts and money, hiding the rewards from the client until the quest is accepted/completed.
4.  **POI Validation:** `SendPointOfInterest#2` explicitly checks if the retrieved `PointOfInterest` pointer is null and logs an error if so, preventing crashes from invalid POI IDs.
5.  **Memory Pre-allocation:** Both `GossipMenu` and `QuestMenu` constructors reserve space for 16 items. This is an optimization to reduce memory allocations for typical menus, though the maximum allowed is 32 (`GOSSIP_MAX_MENU_ITEMS`).
6.  **Outdated POI Method:** `SendPointOfInterest#1` is marked as "Outdated" in comments. It sends raw coordinates, whereas `SendPointOfInterest#2` uses predefined POI entries from the database. New code should prefer the latter.
7.  **Assertion Limits:** `AddMenuItem` asserts that the item count does not exceed 32. Exceeding this limit will cause a crash in debug builds or undefined behavior in release builds. Scripts must respect this limit.

## Member Reference

**GossipMenu** (ctor): Initializes the gossip menu with a session pointer, reserves memory for 16 items, and resets state flags.

**~GossipMenu** (dtor): Calls `ClearMenu()` to clean up resources.

**AddMenuItem#2**: The primary method for adding a gossip item. Validates item count limit, constructs `GossipMenuItem`, and appends it to the list.

**AddGossipMenuItemData**: Appends auxiliary action data (menu, POI, script IDs) to a parallel data list, indexed by menu item position.

**AddMenuItem**: Convenience overload converting `char const*` message to `std::string` before calling the primary `AddMenuItem`.

**AddMenuItem#3**: Convenience overload converting `char const*` message and box message to `std::string` before calling the primary `AddMenuItem`.

**AddMenuItem#4**: Convenience overload converting `char const*` message and box message to `std::string` before calling the primary `AddMenuItem`.

**AddMenuItem#5**: Adds a gossip item using localized text IDs. Resolves `BroadcastText` or `MangosString` based on sign of ID, locale, and player gender.

**IsValidGossipOptionIconForBuild**: Static inline function checking if a gossip icon ID is valid for the current client build, disabling newer icons for older clients.

**MenuItemSender**: Returns the sender ID for a gossip item at the given index, or 0 if out of bounds.

**MenuItemAction**: Returns the action ID for a gossip item at the given index, or 0 if out of bounds.

**MenuItemCoded**: Returns the coded flag for a gossip item at the given index, or false if out of bounds.

**ClearMenu**: Clears all gossip items and data, resetting menu ID and discovery flag.

**PlayerMenu** (ctor): Initializes nested `GossipMenu` and `QuestMenu` objects with the session pointer.

**~PlayerMenu** (dtor): Calls `ClearMenus()` to clean up resources.

**ClearMenus**: Clears both the gossip and quest menus.

**GossipOptionSender**: Delegates to `GossipMenu::MenuItemSender` to get the sender ID for a selected gossip option.

**GossipOptionAction**: Delegates to `GossipMenu::MenuItemAction` to get the action ID for a selected gossip option.

**GossipOptionCoded**: Delegates to `GossipMenu::MenuItemCoded` to get the coded flag for a selected gossip option.

**SendGossipMenu**: Constructs and sends `SMSG_GOSSIP_MESSAGE` packet, serializing gossip items and quest items with localized text and client-build-specific formatting.

**SetMenuId**: Sets the gossip menu ID.

**GetMenuId**: Gets the gossip menu ID.

**SetDiscoveredNode**: Sets the flag indicating the menu was triggered by node discovery.

**IsJustDiscoveredNode**: Checks if the menu was triggered by node discovery.

**MenuItemCount**: Returns the number of gossip items.

**Empty**: Checks if the gossip menu has no items.

**GetItem**: Returns a const reference to the gossip item at the given index.

**GetItemData**: Returns a const reference to the gossip item data at the given index.

**CloseGossip**: Sends `SMSG_GOSSIP_COMPLETE` packet to close the gossip window.

**GetMenuSession**: Returns the stored `WorldSession` pointer.

**SendPointOfInterest**: Sends `SMSG_GOSSIP_POI` packet with raw coordinates and icon data (outdated).

**SendPointOfInterest#2**: Sends `SMSG_GOSSIP_POI` packet using data from a predefined POI entry, with localized icon name.

**SendTalking#2**: Sends `SMSG_NPC_TEXT_UPDATE` packet using `NpcText` data, resolving localized broadcast text for multiple options.

**SendTalking**: Sends `SMSG_NPC_TEXT_UPDATE` packet with raw title and text strings.

**QuestMenu** (ctor): Reserves memory for 16 quest items.

**~QuestMenu** (dtor): Calls `ClearMenu()` to clean up resources.

**AddMenuItem#6**: Adds a quest item after validating the quest template exists.

**HasItem**: Checks if a specific quest ID is present in the menu.

**ClearMenu#2**: Clears all quest items.

**SendQuestGiverQuestList**: Sends `SMSG_QUESTGIVER_QUEST_LIST` packet with greeting and quest list.

**SendQuestGiverStatus**: Sends `SMSG_QUESTGIVER_STATUS` packet with NPC GUID and status.

**SendQuestGiverQuestDetails**: Sends `SMSG_QUESTGIVER_QUEST_DETAILS` packet with quest details, objectives, and rewards.

**SendQuestGiverOfferReward**: Sends `SMSG_QUESTGIVER_OFFER_REWARD` packet with reward text and items.

**SendQuestGiverRequestItems**: Sends `SMSG_QUESTGIVER_REQUEST_ITEMS` packet with required items, or redirects to offer reward if no items needed.

---

<!-- machine-true, projected from graph.json -->

## Map — GossipDef

*Source:* GossipDef.cpp, GossipDef.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GossipMenu | ctor | — | — | — |
| ~GossipMenu | dtor | — | — | — |
| AddMenuItem#2 | method | Errors/PrintStacktraceAndThrow | Player.Main/PrepareGossipMenu | — |
| AddGossipMenuItemData | method | — | Player.Main/PrepareGossipMenu | — |
| AddMenuItem | method | — | — | — |
| AddMenuItem#3 | method | — | — | — |
| AddMenuItem#4 | method | — | burning_steppes/GossipHello_npc_klinfran, custom_creatures/GossipHello_EnchantNPC, custom_creatures/GossipHello_PremadeGearNPC, custom_creatures/GossipHello_PremadeSpecNPC, custom_creatures/GossipHello_ProfessionNPC, custom_creatures/GossipHello_TeleportNPC, custom_creatures/GossipSelect_EnchantNPC, custom_creatures/SendDefaultMenu_TeleportNPC, darkshore/GossipHello_npc_threshwackonator, dustwallow_marsh/GossipHello_npc_cassa_crimsonwing, dustwallow_marsh/GossipHello_npc_lady_jaina_proudmoore, instance_dire_maul/GossipHello_boss_kromcrush, instance_dire_maul/GossipHello_npc_knot_thimblejack, instance_dire_maul/GossipSelect_boss_kromcrush, instance_naxxramas.Main/GossipSelect_npc_MasterCraftsmanOmarion, quest_stormwind_rendezvous/GossipHello_npc_squire_rowe, silithus/GossipHello_npc_Krug_SkullSplit, silithus/GossipSelect_npc_Krug_SkullSplit, thousand_needles/GossipHello_npc_plucky_johnson, ungoro_crater/GossipHello_npc_simone_the_inconspicuous, zulfarrak/OnGossipHello_npc_sergeant_bly, zulfarrak/OnGossipHello_npc_weegli_blastfuse | — |
| AddMenuItem#5 | method | ObjectMgr/GetBroadcastText, ObjectMgr/GetMangosString, Unit.Main/GetGender, WorldSession.Main/GetPlayer, WorldSession.Main/GetSessionDbLocaleIndex | blackrock_depths/GossipHello_npc_mistress_nagmara, boss_vaelastrasz/GossipHello_boss_vael, boss_vaelastrasz/GossipSelect_boss_vael, gnomeregan/GossipHello_npc_blastmaster_emi_shortfuse, instance_naxxramas.Main/GossipHello_npc_MasterCraftsmanOmarion, instance_naxxramas.Main/GossipSelect_npc_MasterCraftsmanOmarion, npcs_special/GossipHello_npc_res_fixer, quest_stormwind_rendezvous/GossipHello_npc_reginald_windsor, scourge_invasion/GossipHello_npc_argent_emissary, scourge_invasion/GossipSelect_npc_argent_emissary, ThreatListCopier.battleground_alterac/GossipHello_npc_AVBlood_collector | — |
| IsValidGossipOptionIconForBuild | function | — | ObjectMgr/LoadGossipMenuItems | — |
| MenuItemSender | method | — | — | — |
| MenuItemAction | method | — | — | — |
| MenuItemCoded | method | — | — | — |
| ClearMenu | method | — | — | — |
| PlayerMenu | ctor | — | Player.Main/Player#5, Player.Main/SetSession | — |
| ~PlayerMenu | dtor | — | — | — |
| ClearMenus | method | — | Creature.Main/CanInteractWithBattleMaster, Creature.Main/IsTrainerOf, instance_dire_maul/GossipSelect_boss_kromcrush, instance_dire_maul/GossipSelect_npc_knot_thimblejack, Player.Main/PrepareGossipMenu, ScriptMgr/GetDialogStatus, ScriptMgr/GetDialogStatus#2, ScriptMgr/OnGameObjectUse, ScriptMgr/OnGossipHello, ScriptMgr/OnGossipHello#2, ScriptMgr/OnGossipSelect, ScriptMgr/OnGossipSelect#2, ScriptMgr/OnQuestAccept, ScriptMgr/OnQuestAccept#2, ScriptMgr/OnQuestRewarded, ScriptMgr/OnQuestRewarded#2, world_event_wareffort/GossipHello_npc_AQwar_collector | — |
| GossipOptionSender | method | — | WorldSession.NPCHandler/HandleGossipSelectOptionOpcode | — |
| GossipOptionAction | method | — | WorldSession.NPCHandler/HandleGossipSelectOptionOpcode | — |
| GossipOptionCoded | method | — | WorldSession.NPCHandler/HandleGossipSelectOptionOpcode | — |
| SendGossipMenu | method | ByteBuffer/append#4, ByteBuffer/operator<<, ByteBuffer/operator<<#10, ByteBuffer/operator<<#7, ObjectGuid/operator<<, ObjectMgr/GetQuestLocale, ObjectMgr/GetQuestTemplate, PlayerMenu/GetMenuSession, QuestDef/GetQuestLevel, QuestDef/GetTitle, QuestMenu/GetItem, QuestMenu/MenuItemCount, WorldPacket/WorldPacket#4, WorldSession.Main/GetSessionDbLocaleIndex, WorldSession.Main/SendPacket | blackrock_depths/GossipHello_npc_mistress_nagmara, boss_vaelastrasz/GossipHello_boss_vael, boss_vaelastrasz/GossipSelect_boss_vael, burning_steppes/GossipHello_npc_klinfran, Creature.Main/CanInteractWithBattleMaster, Creature.Main/IsTrainerOf, custom_creatures/GossipHello_EnchantNPC, custom_creatures/GossipHello_PremadeGearNPC, custom_creatures/GossipHello_PremadeSpecNPC, custom_creatures/GossipHello_ProfessionNPC, custom_creatures/GossipHello_TeleportNPC, custom_creatures/GossipSelect_EnchantNPC, custom_creatures/SendDefaultMenu_TeleportNPC, darkshore/GossipHello_npc_threshwackonator, dustwallow_marsh/GossipHello_npc_cassa_crimsonwing, dustwallow_marsh/GossipHello_npc_lady_jaina_proudmoore, dustwallow_marsh/GossipSelect_npc_lady_jaina_proudmoore, eastern_plaguelands/GossipHello_npc_joseph_redpath, feralas/GossipHello_npc_screecher_spirit, gnomeregan/GossipHello_npc_blastmaster_emi_shortfuse, instance_dire_maul/GossipHello_boss_kromcrush, instance_dire_maul/GossipHello_npc_knot_thimblejack, instance_dire_maul/GossipSelect_boss_kromcrush, instance_dire_maul/GossipSelect_npc_knot_thimblejack, instance_naxxramas.Main/GossipHello_npc_MasterCraftsmanOmarion, instance_naxxramas.Main/GossipSelect_npc_MasterCraftsmanOmarion, instance_zulgurub/OnGossipHello_go_table_madness, npcs_special/GossipHello_npc_kwee_peddlefeet, npcs_special/GossipHello_npc_res_fixer, Player.Main/SendPreparedGossip, quest_stormwind_rendezvous/GossipHello_npc_reginald_windsor, quest_stormwind_rendezvous/GossipHello_npc_squire_rowe, scourge_invasion/GossipHello_npc_argent_emissary, scourge_invasion/GossipSelect_npc_argent_emissary, searing_gorge/GossipHello_npc_dying_archaeologist, silithus/GossipHello_npc_Krug_SkullSplit, silithus/GossipSelect_npc_Krug_SkullSplit, thousand_needles/GossipHello_npc_plucky_johnson, thousand_needles/GossipSelect_npc_plucky_johnson, ThreatListCopier.battleground_alterac/GossipHello_npc_AVBlood_collector, ThreatListCopier.battleground_alterac/GossipSelect_npc_AVBlood_collector, ungoro_crater/GossipHello_npc_simone_the_inconspicuous, wetlands/GossipHello_npc_mikhail, world_event_wareffort/GossipHello_npc_AQwar_collector, zulfarrak/OnGossipHello_go_table_theka, zulfarrak/OnGossipHello_npc_sergeant_bly, zulfarrak/OnGossipHello_npc_weegli_blastfuse | — |
| SetMenuId | method | — | Player.Main/PrepareGossipMenu | — |
| GetMenuId | method | — | Player.Main/SendPreparedGossip | — |
| SetDiscoveredNode | method | — | Player.Main/PrepareGossipMenu | — |
| IsJustDiscoveredNode | method | — | Player.Main/SendPreparedGossip | — |
| MenuItemCount | method | — | Player.Main/OnGossipSelect | — |
| Empty | method | — | Player.Main/SendPreparedGossip | — |
| GetItem | method | — | Player.Main/OnGossipSelect | — |
| GetItemData | method | — | Player.Main/OnGossipSelect | — |
| CloseGossip | method | PlayerMenu/GetMenuSession, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | blackrock_depths/GossipSelect_npc_mistress_nagmara, boss_vaelastrasz/GossipSelect_boss_vael, burning_steppes/GossipSelect_npc_klinfran, custom_creatures/GossipSelect_EnchantNPC, custom_creatures/GossipSelect_PremadeGearNPC, custom_creatures/GossipSelect_PremadeSpecNPC, custom_creatures/GossipSelect_ProfessionNPC, custom_creatures/SendDefaultMenu_TeleportNPC, darkshore/GossipSelect_npc_threshwackonator, dustwallow_marsh/GossipSelect_npc_cassa_crimsonwing, gnomeregan/GossipSelect_npc_blastmaster_emi_shortfuse, instance_dire_maul/GossipSelect_boss_kromcrush, instance_naxxramas.Main/GossipSelect_npc_MasterCraftsmanOmarion, npcs_special/GossipSelect_npc_res_fixer, Player.Main/OnGossipSelect, quest_stormwind_rendezvous/GossipSelect_npc_reginald_windsor, quest_stormwind_rendezvous/GossipSelect_npc_squire_rowe, silithus/GossipSelect_npc_Krug_SkullSplit, ThreatListCopier.battleground_alterac/GossipSelect_npc_AVBlood_collector, ungoro_crater/GossipSelect_npc_simone_the_inconspicuous, WorldSession.NPCHandler/SendBindPoint, WorldSession.QuestHandler/HandleQuestgiverAcceptQuestOpcode, WorldSession.QuestHandler/HandleQuestgiverCancel, WorldSession.QuestHandler/HandleQuestgiverQueryQuestOpcode, zulfarrak/OnGossipSelect_npc_sergeant_bly, zulfarrak/OnGossipSelect_npc_weegli_blastfuse | — |
| GetMenuSession | method | — | — | — |
| SendPointOfInterest | method | ByteBuffer/operator<<#10, ByteBuffer/operator<<#3, ByteBuffer/operator<<#9, PlayerMenu/GetMenuSession, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | ChatHandler.DebugCommands/HandleDebugSendPoiCommand | — |
| SendPointOfInterest#2 | method | ByteBuffer/append#4, ByteBuffer/operator<<#10, ByteBuffer/operator<<#9, Log.Main/Out, ObjectMgr/GetPointOfInterest, ObjectMgr/GetPointOfInterestLocale, PlayerMenu/GetMenuSession, WorldPacket/WorldPacket#4, WorldSession.Main/GetSessionDbLocaleIndex, WorldSession.Main/SendPacket | Player.Main/OnGossipSelect | — |
| SendTalking#2 | method | BroadcastText/GetText, ByteBuffer/operator<<, ByteBuffer/operator<<#10, ByteBuffer/operator<<#3, ByteBuffer/operator<<#9, ObjectMgr/GetBroadcastTextLocale, ObjectMgr/GetNpcText, PlayerMenu/GetMenuSession, WorldPacket/WorldPacket#4, WorldSession.Main/GetSessionDbLocaleIndex, WorldSession.Main/SendPacket | — | — |
| SendTalking | method | ByteBuffer/append#4, ByteBuffer/operator<<#10, ByteBuffer/operator<<#9, PlayerMenu/GetMenuSession, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | — | — |
| QuestMenu | ctor | — | — | — |
| ~QuestMenu | dtor | — | — | — |
| AddMenuItem#6 | method | Errors/PrintStacktraceAndThrow, ObjectMgr/GetQuestTemplate | Player.Main/PrepareQuestMenu | — |
| HasItem | method | — | — | — |
| ClearMenu#2 | method | — | Player.Main/PrepareQuestMenu | — |
| SendQuestGiverQuestList | method | ByteBuffer/append#4, ByteBuffer/operator<<, ByteBuffer/operator<<#10, ByteBuffer/operator<<#7, ByteBuffer/wpos, ObjectGuid/GetEntry, ObjectGuid/IsAnyTypeCreature, ObjectGuid/operator<<, ObjectMgr/GetQuestGreetingLocale, ObjectMgr/GetQuestLocale, ObjectMgr/GetQuestTemplate, PlayerMenu/GetMenuSession, QuestDef/GetQuestLevel, QuestDef/GetTitle, QuestMenu/GetItem, QuestMenu/MenuItemCount, WorldPacket/WorldPacket#4, WorldSession.Main/GetSessionDbLocaleIndex, WorldSession.Main/SendPacket | Player.Main/SendPreparedQuest | — |
| SendQuestGiverStatus | method | ByteBuffer/operator<<#10, ObjectGuid/operator<<, PlayerMenu/GetMenuSession, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | WorldSession.QuestHandler/HandleQuestgiverStatusQueryOpcode | — |
| SendQuestGiverQuestDetails | method | ByteBuffer/append#4, ByteBuffer/operator<<#10, ObjectGuid/operator<<, ObjectMgr/GetItemPrototype, ObjectMgr/GetQuestLocale, PlayerMenu/GetMenuSession, QuestDef/GetDetails, QuestDef/GetObjectives, QuestDef/GetQuestId, QuestDef/GetRewChoiceItemsCount, QuestDef/GetRewItemsCount, QuestDef/GetRewOrReqMoney, QuestDef/GetRewSpell, QuestDef/GetTitle, QuestDef/HasQuestFlag, WorldPacket/WorldPacket#4, WorldSession.Main/GetSessionDbLocaleIndex, WorldSession.Main/SendPacket | Player.Main/SendPreparedQuest, WorldSession.QuestHandler/HandlePushQuestToParty, WorldSession.QuestHandler/HandleQuestgiverChooseRewardOpcode, WorldSession.QuestHandler/HandleQuestgiverQueryQuestOpcode | — |
| SendQuestGiverOfferReward | method | ByteBuffer/append#4, ByteBuffer/operator<<#10, ObjectGuid/operator<<, ObjectMgr/GetItemPrototype, ObjectMgr/GetQuestLocale, PlayerMenu/GetMenuSession, QuestDef/GetOfferRewardText, QuestDef/GetQuestId, QuestDef/GetRewChoiceItemsCount, QuestDef/GetRewItemsCount, QuestDef/GetRewOrReqMoney, QuestDef/GetRewSpell, QuestDef/GetTitle, WorldPacket/WorldPacket#4, WorldSession.Main/GetSessionDbLocaleIndex, WorldSession.Main/SendPacket | WorldSession.QuestHandler/HandleQuestgiverChooseRewardOpcode, WorldSession.QuestHandler/HandleQuestgiverRequestRewardOpcode | — |
| SendQuestGiverRequestItems | method | ByteBuffer/append#4, ByteBuffer/operator<<#10, ObjectGuid/operator<<, ObjectMgr/GetItemPrototype, ObjectMgr/GetQuestLocale, PlayerMenu/GetMenuSession, QuestDef/GetCompleteEmote, QuestDef/GetIncompleteEmote, QuestDef/GetQuestId, QuestDef/GetReqItemsCount, QuestDef/GetRequestItemsText, QuestDef/GetRewOrReqMoney, QuestDef/GetTitle, WorldPacket/WorldPacket#4, WorldSession.Main/GetSessionDbLocaleIndex, WorldSession.Main/SendPacket | Player.Main/SendPreparedQuest, WorldSession.QuestHandler/HandleQuestgiverCompleteQuest | — |
