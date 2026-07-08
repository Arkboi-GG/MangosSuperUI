# PlayerMenu

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# PlayerMenu

`PlayerMenu` is a lightweight composite class that aggregates two distinct interaction menus—`GossipMenu` and `QuestMenu`—into a single object owned by a player’s session. It serves as the central container for all non-combat NPC interactions initiated by a player, including custom gossip options, quest giver interfaces, vendor/trainer flags, and point-of-interest markers. The class does not contain business logic for populating these menus; instead, it provides accessor methods to retrieve the underlying menu objects so that script handlers (e.g., `GossipHello_*` functions in various zone-specific scripts) can populate them. It also provides helper methods to send the resulting menu data to the client via the `WorldSession`.

The class is defined in `GossipDef.h` and implemented in the corresponding `.cpp` file (not provided in the source snippet, but the header defines the interface). It is instantiated once per player session and lives for the duration of that session.

## Purpose & Responsibilities

1.  **Aggregation:** Holds both a `GossipMenu` (for custom NPC dialogue, teleporters, vendors, etc.) and a `QuestMenu` (for standard quest giver interactions).
2.  **Access Control:** Provides `GetGossipMenu()` and `GetQuestMenu()` to allow external scripts to populate the respective menus.
3.  **State Management:** Tracks whether the combined menu state is empty (`Empty()`), allowing the server to decide whether to send a menu packet or simply close the interaction.
4.  **Packet Construction & Sending:** Contains methods to serialize the populated menu data into network packets and send them to the client (`SendGossipMenu`, `SendQuestGiver...`, `SendPointOfInterest`, etc.).
5.  **Session Binding:** Maintains a reference to the `WorldSession` to facilitate packet sending and context retrieval.

## Member-by-Member Behavior

### Menu Accessors

*   **`GetGossipMenu`**: Returns a non-const reference to the internal `GossipMenu` object. This is the primary entry point for scripts to add custom gossip options. It is called by numerous zone-specific and custom NPC scripts (e.g., `GossipHello_npc_mistress_nagmara`, `GossipHello_EnchantNPC`) to populate the gossip menu before it is sent to the player.
*   **`GetQuestMenu`**: Returns a non-const reference to the internal `QuestMenu` object. Used by quest-related logic to add available quests to the quest menu. Called by `Player.Main/PrepareQuestMenu` and related methods.

### State & Session Queries

*   **`GetMenuSession`**: Returns the `WorldSession*` associated with this menu. This is used internally by `PlayerMenu`'s own sending methods (e.g., `SendGossipMenu`, `CloseGossip`) to access the network socket and player context. It delegates to `GossipMenu::GetMenuSession()`.
*   **`Empty`**: Returns `true` if both the `GossipMenu` and `QuestMenu` are empty. This is a critical check performed before sending any menu packet. If `Empty()` returns `true`, the server typically closes the gossip window or does nothing, preventing the client from receiving an invalid empty menu.

### Menu Population Helpers (Delegated)

While `PlayerMenu` itself does not add items, it exposes methods that delegate to the underlying `GossipMenu` or `QuestMenu` for convenience or specific action handling:

*   **`ClearMenus`**: Clears both the gossip and quest menus. This is likely called at the start of a new interaction cycle to ensure stale data is not sent.
*   **`GossipOptionSender`**, **`GossipOptionAction`**, **`GossipOptionCoded`**: These methods retrieve metadata about a selected gossip option. They delegate to the `GossipMenu` to fetch the sender GUID, action ID, and whether the message is coded (encrypted) for a given selection index. These are used when processing a player's response to a gossip menu.

### Packet Sending Methods

These methods construct and send the appropriate network packets to the client. They use `GetMenuSession()` to obtain the `WorldSession` for sending.

*   **`SendGossipMenu`**: Sends the populated gossip menu to the client. It takes a title text ID and the NPC's GUID. It checks if the menu is empty; if so, it may close the gossip window instead.
*   **`CloseGossip`**: Sends a packet to the client to close the current gossip window.
*   **`SendPointOfInterest`**: Sends a Point of Interest (POI) marker to the client. There are two overloads: one that takes raw coordinates and icon data, and another that takes a POI ID. This is used for dynamic map markers, such as battleground objectives or custom teleport destinations.
*   **`SendTalking`**: Sends a simple text message to the player, often used for NPC dialogue that doesn't require a full gossip menu. There are two overloads: one for a text ID and one for raw title/text strings.

### Quest System Integration

These methods handle the specific packets required for the quest giver interface. They take a `Quest` object and the NPC's GUID, then format the data into the correct packet structure.

*   **`SendQuestGiverStatus`**: Sends the status of a quest (e.g., available, completed, failed) to the client.
*   **`SendQuestGiverQuestList`**: Sends the list of available quests from an NPC. It includes an emote and a title.
*   **`SendQuestGiverQuestDetails`**: Sends detailed information about a specific quest, including description, objectives, and rewards. The `ActivateAccept` parameter determines if the "Accept" button is enabled.
*   **`SendQuestGiverOfferReward`**: Sends the reward offer screen for a completed quest. The `EnbleNext` parameter likely controls whether the "Next" button is shown for multi-step quests.
*   **`SendQuestGiverRequestItems`**: Sends the item turn-in screen for a quest. The `Completable` and `CloseOnCancel` parameters control the behavior of the quest completion flow.

## Cross-Unit Boundaries

`PlayerMenu` acts as a bridge between high-level script logic and low-level network packet construction.

*   **Called By (Scripts):** Numerous NPC scripts (e.g., `blackrock_depths/GossipHello_npc_mistress_nagmara`, `custom_creatures/GossipHello_EnchantNPC`) call `GetGossipMenu()` to populate the gossip menu. These scripts are responsible for adding items, setting actions, and preparing the menu content.
*   **Called By (Player Logic):** `Player.Main` methods like `PrepareGossipMenu`, `SendPreparedGossip`, and `OnGossipSelect` interact with `PlayerMenu` to manage the lifecycle of gossip interactions. `PrepareQuestMenu` and `SendPreparedQuest` handle quest menu preparation and sending.
*   **Internal Delegation:** `PlayerMenu` delegates most of its work to `GossipMenu` and `QuestMenu`. For example, `GetMenuSession()` calls `GossipMenu::GetMenuSession()`. The actual packet construction logic is likely implemented in the `.cpp` file, using the data stored in `mGossipMenu` and `mQuestMenu`.

## Data Model

`PlayerMenu` does not directly interact with any database tables. It operates entirely on in-memory data structures (`GossipMenuItemList`, `QuestMenuItemList`) populated by scripts and quest logic. Any database queries related to gossip or quests are performed by other units (e.g., `GossipDef`'s underlying logic, `QuestDef`, or the NPC scripts themselves) before the data is passed to `PlayerMenu` for display.

## Notable Implementation Details

*   **Composite Pattern:** `PlayerMenu` is a classic composite, aggregating `GossipMenu` and `QuestMenu`. This allows the server to handle both gossip and quest interactions through a single interface, simplifying the player-side code.
*   **Empty Check:** The `Empty()` method is crucial for preventing invalid states. If a script fails to add any items to either menu, `Empty()` returns `true`, and the server can gracefully close the interaction instead of sending an empty menu, which would confuse the client.
*   **Session Binding:** The `WorldSession*` is stored in the `GossipMenu` and accessed via `GetMenuSession()`. This ensures that all packet sending is tied to the correct player session.
*   **Coded Messages:** The `GossipMenuItem` struct includes a `m_gCoded` flag, indicating whether the message is encrypted. This is handled by the `AddMenuItem` methods and retrieved via `MenuItemCoded()`. The server must ensure that coded messages are properly encrypted before sending.
*   **POI Icons:** The `Poi_Icon` enum lists many possible POI icons, some of which are color-coded (Red/Blue) for Alterac Valley. The `SendPointOfInterest` methods allow scripts to place these markers dynamically.
*   **Gossip Option Icons:** The `GossipOptionIcon` enum defines the icons displayed next to gossip options. The `IsValidGossipOptionIconForBuild` function ensures that only valid icons for the client build are used, preventing visual glitches or crashes.

## Member Reference

**GetGossipMenu**  
Returns a non-const reference to the internal `GossipMenu` object. Called by numerous NPC scripts to populate gossip options.

**GetQuestMenu**  
Returns a non-const reference to the internal `QuestMenu` object. Called by `Player.Main/PrepareQuestMenu` and related methods to populate quest options.

**GetMenuSession**  
Returns the `WorldSession*` associated with this menu. Delegates to `GossipMenu::GetMenuSession()`. Used internally by sending methods.

**Empty**  
Returns `true` if both `GossipMenu` and `QuestMenu` are empty. Used to determine if a menu packet should be sent or if the interaction should be closed.

---

<!-- machine-true, projected from graph.json -->

## Map — PlayerMenu

*Source:* GossipDef.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GetGossipMenu | method | — | blackrock_depths/GossipHello_npc_mistress_nagmara, boss_vaelastrasz/GossipHello_boss_vael, boss_vaelastrasz/GossipSelect_boss_vael, burning_steppes/GossipHello_npc_klinfran, custom_creatures/GossipHello_EnchantNPC, custom_creatures/GossipHello_PremadeGearNPC, custom_creatures/GossipHello_PremadeSpecNPC, custom_creatures/GossipHello_ProfessionNPC, custom_creatures/GossipHello_TeleportNPC, custom_creatures/GossipSelect_EnchantNPC, custom_creatures/SendDefaultMenu_TeleportNPC, darkshore/GossipHello_npc_threshwackonator, dustwallow_marsh/GossipHello_npc_cassa_crimsonwing, dustwallow_marsh/GossipHello_npc_lady_jaina_proudmoore, gnomeregan/GossipHello_npc_blastmaster_emi_shortfuse, instance_dire_maul/GossipHello_boss_kromcrush, instance_dire_maul/GossipHello_npc_knot_thimblejack, instance_dire_maul/GossipSelect_boss_kromcrush, instance_naxxramas.Main/GossipHello_npc_MasterCraftsmanOmarion, instance_naxxramas.Main/GossipSelect_npc_MasterCraftsmanOmarion, npcs_special/GossipHello_npc_res_fixer, Player.Main/OnGossipSelect, Player.Main/PrepareGossipMenu, Player.Main/SendPreparedGossip, quest_stormwind_rendezvous/GossipHello_npc_reginald_windsor, quest_stormwind_rendezvous/GossipHello_npc_squire_rowe, scourge_invasion/GossipHello_npc_argent_emissary, scourge_invasion/GossipSelect_npc_argent_emissary, silithus/GossipHello_npc_Krug_SkullSplit, silithus/GossipSelect_npc_Krug_SkullSplit, thousand_needles/GossipHello_npc_plucky_johnson, ThreatListCopier.battleground_alterac/GossipHello_npc_AVBlood_collector, ungoro_crater/GossipHello_npc_simone_the_inconspicuous, zulfarrak/OnGossipHello_npc_sergeant_bly, zulfarrak/OnGossipHello_npc_weegli_blastfuse | — |
| GetQuestMenu | method | — | Player.Main/PrepareQuestMenu, Player.Main/SendPreparedGossip, Player.Main/SendPreparedQuest | — |
| GetMenuSession | method | — | GossipDef/CloseGossip, GossipDef/SendGossipMenu, GossipDef/SendPointOfInterest, GossipDef/SendPointOfInterest#2, GossipDef/SendQuestGiverOfferReward, GossipDef/SendQuestGiverQuestDetails, GossipDef/SendQuestGiverQuestList, GossipDef/SendQuestGiverRequestItems, GossipDef/SendQuestGiverStatus, GossipDef/SendTalking, GossipDef/SendTalking#2 | — |
| Empty | method | — | — | — |
