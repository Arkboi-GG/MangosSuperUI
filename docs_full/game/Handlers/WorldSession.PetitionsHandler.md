<!-- provenance: boundary-bleed -->
# WorldSession.PetitionsHandler

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# WorldSession.PetitionsHandler

## Purpose & Responsibilities

`WorldSession.PetitionsHandler` implements the server-side logic for the **Guild Charter** system in World of Warcraft (specifically the Classic/1.x era supported by this codebase). It handles the entire lifecycle of creating a guild via the "Petition" item mechanic:

1.  **Acquisition:** Buying a blank Guild Charter from a Tabard Designer NPC.
2.  **Management:** Renaming the charter, viewing signatures, and offering the charter to other players.
3.  **Signing:** Allowing eligible players to sign the charter, enforcing faction restrictions, trial account limits, and duplicate-signature checks.
4.  **Completion:** Turning in a fully signed charter to create the actual `Guild` object, deleting the charter item, and registering the guild with the `GuildMgr`.

This unit acts as the bridge between the client's petition-related opcodes and the core `GuildMgr` and `ObjectMgr` systems. It performs validation (names, funds, inventory space, faction alignment) before delegating heavy lifting (petitions storage, guild creation) to other managers.

## Member-by-Member Behavior

### Charter Acquisition and Initialization

**`HandlePetitionBuyOpcode`**
Handles the client request to purchase a Guild Charter.
1.  **Validation:**
    *   Verifies the target NPC exists, is a valid Petitioner (`UNIT_NPC_FLAG_PETITIONER`), and is a Tabard Designer.
    *   Checks if the player is a Trial Account (`HasTrialRestrictions`), blocking them if so.
    *   Removes `FEIGN_DEATH` state if active.
    *   Ensures the player is not already in a guild (`GetGuildId`).
    *   Ensures the player does not already own an active petition (`GetPetitionByOwnerGuid`).
    *   Validates the proposed guild name: it must not already exist (`GetGuildByName`), must not be reserved (`IsReservedName`), must pass format checks (`IsValidCharterName`), and must pass antispam filters (`AntispamInterface/filterMessage`).
2.  **Transaction:**
    *   Retrieves the item prototype for `GUILD_CHARTER` (ID 5863).
    *   Checks if the player has sufficient gold (`GUILD_CHARTER_COST`, 10000 copper).
    *   Checks inventory space (`CanStoreNewItem`).
    *   Deducts gold (`ModifyMoney`) and creates the item (`StoreNewItem`).
3.  **Petition Creation:**
    *   Generates a unique Petition ID (`GeneratePetitionID`).
    *   Sets this ID as the enchantment on the charter item (`SetUInt32Value` on `ITEM_FIELD_ENCHANTMENT`).
    *   Marks the item as changed and sends it to the client.
    *   Creates the petition record in memory via `GuildMgr/CreatePetition`.
    *   Saves the player's inventory and gold to the database.

**`SendPetitionShowList`**
Called when a player interacts with a Petitioner NPC to view available charters.
1.  Validates the NPC interaction and removes `FEIGN_DEATH`.
2.  Constructs an `SMSG_PETITION_SHOWLIST` packet containing hardcoded data for a single Guild Charter type:
    *   Entry: `GUILD_CHARTER` (5863)
    *   Display ID: `CHARTER_DISPLAY_ID` (16161)
    *   Cost: `GUILD_CHARTER_COST` (10000)
3.  Sends the packet to the client.

### Charter Management and Viewing

**`HandlePetitionShowSignOpcode`**
Displays the current signatures on a charter held by the player.
1.  Blocks players who are already in a guild.
2.  Retrieves the charter item from the player's inventory.
3.  Extracts the Petition ID from the item's enchantment.
4.  Fetches the `Petition` object from `GuildMgr`. If missing, logs an error and aborts.
5.  Builds an `SMSG_PETITION_SHOW_SIGNATURES` packet containing:
    *   Item GUID, Owner GUID, Petition ID, Signature Count.
    *   Detailed signature data appended via `Petition/BuildSignatureData`.
6.  Sends the packet.

**`HandlePetitionQueryOpcode`**
Responds to a client query about a specific petition's metadata.
1.  Fetches the `Petition` by ID.
2.  Constructs an `SMSG_PETITION_QUERY_RESPONSE` packet.
3.  Populates fields with:
    *   Petition ID, Owner GUID, Name.
    *   Hardcoded values for body text (empty), flags, min/max signatures (9), deadlines, and allowed classes/races/genders (all zero/unrestricted in this implementation).
4.  Sends the packet.

**`HandlePetitionRenameOpcode`**
Allows the charter owner to change the proposed guild name.
1.  Retrieves the charter item.
2.  Validates the new name against existing guilds, reserved names, format rules, and antispam filters (same logic as buying).
3.  Fetches the `Petition` object.
4.  Calls `Petition/Rename`. If successful, sends a `MSG_PETITION_RENAME` packet to the client confirming the change.

### Signing and Offering

**`HandlePetitionSignOpcode`**
Processes a player's attempt to sign a charter.
1.  Retrieves the `Petition` via the charter item GUID.
2.  **Pre-flight Checks:**
    *   Aborts if the petition is already complete (`IsComplete`).
    *   Aborts if the signer is the owner (`PETITION_SIGN_CANT_SIGN_OWN`).
    *   Aborts if the signer is a Trial Account.
    *   Aborts if factions differ and cross-faction guilds are disabled (`CONFIG_BOOL_ALLOW_TWO_SIDE_INTERACTION_GUILD`).
    *   Aborts if the signer is already in a guild or has a pending guild invitation.
    *   Aborts if the signature count is already 9 (client hard limit).
    *   Aborts if the player has already signed this specific petition (`GetSignatureForPlayer`).
3.  **Execution:**
    *   Calls `Petition/AddNewSignature`.
    *   On success, sends `SMSG_PETITION_SIGN_RESULTS` with `PETITION_SIGN_OK` to both the signer and the petition owner (if online).
    *   Logs the signing event.

**`HandleOfferPetitionOpcode`**
Allows the charter owner to offer the charter to another player for signing.
1.  Finds the target player by GUID.
2.  Validates faction alignment (cross-faction check).
3.  Validates the target player is not in a guild, not invited, and not a Trial Account.
4.  Retrieves the charter and petition objects.
5.  Constructs an `SMSG_PETITION_SHOW_SIGNATURES` packet (similar to `HandlePetitionShowSignOpcode`) and sends it directly to the **target player's session**, allowing them to see the current signatures and sign if eligible.

**`HandlePetitionDeclineOpcode`**
Handles a player declining a petition offer.
1.  Logs the decline.
2.  Retrieves the petition.
3.  If the petition owner is online, sends a `MSG_PETITION_DECLINE` packet to the owner indicating which player declined.

### Finalization

**`HandleTurnInPetitionOpcode`**
Converts a fully signed charter into a permanent Guild.
1.  Retrieves the charter and petition objects.
2.  **Validation:**
    *   Blocks Trial Accounts.
    *   Blocks if the player is already in a guild.
    *   Blocks if the player is not the petition owner.
    *   Blocks if the petition is not complete (`IsComplete`).
    *   Blocks if a guild with that name already exists.
3.  **Creation:**
    *   Instantiates a new `Guild` object.
    *   Calls `Guild/Create` with the petition data and the owner player.
    *   Registers the guild with `GuildMgr/AddGuild`.
    *   Deletes the petition record from `GuildMgr/DeletePetition`.
    *   Destroys the charter item from the player's inventory (`DestroyItem`).
4.  Sends `SMSG_TURN_IN_PETITION_RESULTS` with `PETITION_SIGN_OK` to confirm success.

**`HandlePetitionShowListOpcode`**
A thin wrapper that calls `SendPetitionShowList` with the NPC GUID provided in the packet.

## Cross-Unit Boundaries

### Collaboration with `GuildMgr`
The `GuildMgr` is the central authority for guild and petition data.
*   **`HandlePetitionBuyOpcode`**: Calls `GetGuildByName` to prevent duplicate names, `GetPetitionByOwnerGuid` to prevent multiple charters per player, and `CreatePetition` to initialize the new petition record.
*   **`HandlePetitionShowSignOpcode`**, **`HandlePetitionQueryOpcode`**, **`HandlePetitionRenameOpcode`**, **`HandleTurnInPetitionOpcode`**: Call `GetPetitionById` or `GetPetitionByCharterGuid` to retrieve the in-memory `Petition` object associated with the item.
*   **`HandlePetitionSignOpcode`**: Calls `GetSignatureForPlayer` to check for duplicates and `AddNewSignature` to record the sign.
*   **`HandleTurnInPetitionOpcode`**: Calls `AddGuild` to register the new guild and `DeletePetition` to clean up the temporary petition data.

### Collaboration with `ObjectMgr`
`ObjectMgr` provides static data and ID generation.
*   **`HandlePetitionBuyOpcode`**: Calls `GeneratePetitionID` for a unique petition identifier, `GetItemPrototype` to verify the charter item exists, `IsReservedName` and `IsValidCharterName` for name validation.
*   **`HandlePetitionSignOpcode`**: Calls `GetPlayer` to locate the petition owner online to notify them of a new signature.
*   **`HandlePetitionDeclineOpcode`**: Calls `GetPlayer` to locate the owner to notify them of a decline.

### Collaboration with `Player.Main` and `Unit.Main`
These units provide access to the player's state and inventory.
*   **Inventory/Gold**: `HandlePetitionBuyOpcode` uses `GetMoney`, `ModifyMoney`, `CanStoreNewItem`, `StoreNewItem`, and `SaveInventoryAndGoldToDB` to handle the transaction.
*   **Guild Status**: Multiple handlers use `GetGuildId` and `GetGuildIdInvited` to enforce exclusivity rules.
*   **Item Access**: Handlers use `GetItemByGuid` to locate the charter in the player's bags.
*   **State Management**: `HandlePetitionBuyOpcode` and `SendPetitionShowList` use `HasUnitState` and `RemoveSpellsCausingAura` to handle `FEIGN_DEATH`.
*   **Notifications**: `SendGuildCommandResult` (from `WorldSession.GuildHandler`) and `SendNotification` (from `WorldSession.Main`) are used extensively to report errors (invalid name, insufficient funds, etc.) to the client.

### Collaboration with `Anticheat` and `Log`
*   **Anticheat**: `HandlePetitionBuyOpcode` uses `AntispamInterface/filterMessage` to block spammy guild names.
*   **Logging**: `Log.Main/Out` is used for debug/error logging in most handlers, particularly for missing petitions or failed interactions. `World/LogChat` is used specifically for logging spam attempts.

## Data Model

This unit does not directly execute SQL queries against database tables. It relies entirely on the `GuildMgr` and `ObjectMgr` units to manage persistence. The `GuildMgr` is responsible for loading/saving `Petition` and `Guild` data to their respective database tables (typically `guild_petition` and `guild` in the Mangos/WowVMaNGOS schema), but those interactions are encapsulated within those other units. Therefore, **no direct database table interactions occur in this unit.**

## Notable Implementation Details

1.  **Hardcoded Limits**: The minimum and maximum signature counts are hardcoded to 9 in `HandlePetitionQueryOpcode` and `HandlePetitionSignOpcode`. The client also enforces a hard limit of 9 signatures.
2.  **Enchantment as ID**: The Petition ID is stored in the `ITEM_FIELD_ENCHANTMENT` field of the charter item. This is a legacy WoW mechanic where the "enchantment" slot is repurposed to link the physical item to the logical petition record.
3.  **Cross-Faction Logic**: The ability for opposing factions to sign the same charter is controlled by `CONFIG_BOOL_ALLOW_TWO_SIDE_INTERACTION_GUILD`. If disabled, `HandlePetitionSignOpcode` and `HandleOfferPetitionOpcode` reject cross-faction interactions.
4.  **Trial Account Restrictions**: Trial accounts are explicitly blocked from buying, signing, or turning in charters via `HasTrialRestrictions()` checks.
5.  **Owner Notification Race Condition**: In `HandlePetitionSignOpcode`, if the owner is online, they receive the `SMSG_PETITION_SIGN_RESULTS` packet. However, the code comments note uncertainty about whether this is the correct message for the owner, suggesting potential UI quirks in the client.
6.  **Memory Management**: In `HandleTurnInPetitionOpcode`, the `Guild` object is manually allocated with `new`. If `Guild::Create` fails, it is manually `delete`d. If successful, ownership is transferred to `GuildMgr`, and the local pointer is set to `nullptr` after `DeletePetition` is called (which likely deletes the `Petition` object, not the `Guild`). The charter item is destroyed last to ensure the petition data remains valid until the guild is fully registered.
7.  **Fake Death Handling**: Both `HandlePetitionBuyOpcode` and `SendPetitionShowList` actively remove the `FEIGN_DEATH` aura if the player is feigning death, allowing them to interact with the NPC despite being "dead".

## Member Reference

**HandlePetitionBuyOpcode**: Processes the purchase of a Guild Charter from a Tabard Designer NPC. Validates NPC interaction, player eligibility (not in guild, not trial, no existing petition), guild name availability/format/spam, and inventory/gold sufficiency. Creates the charter item, sets its enchantment to a new Petition ID, and registers the petition with `GuildMgr`.

**HandlePetitionShowSignOpcode**: Displays the current signatures on a charter held by the player. Validates the player is not in a guild, retrieves the petition via the item's enchantment ID, and sends an `SMSG_PETITION_SHOW_SIGNATURES` packet with signature details built by `Petition/BuildSignatureData`.

**HandlePetitionQueryOpcode**: Responds to a client query about a petition's metadata. Retrieves the petition by ID and sends an `SMSG_PETITION_QUERY_RESPONSE` packet with the petition name, owner, and hardcoded limits (min/max 9 signatures).

**HandlePetitionRenameOpcode**: Allows the charter owner to change the proposed guild name. Validates the new name against existing guilds, reserved words, and spam filters. Updates the petition name via `Petition/Rename` and confirms with the client.

**HandlePetitionSignOpcode**: Processes a player's signature on a charter. Validates the petition is incomplete, the signer is not the owner, not a trial account, not cross-faction (if restricted), not already in a guild/invited, and hasn't already signed. Adds the signature via `Petition/AddNewSignature` and notifies both the signer and the owner.

**HandlePetitionDeclineOpcode**: Handles a player declining a petition offer. Logs the event and notifies the petition owner (if online) via `MSG_PETITION_DECLINE`.

**HandleOfferPetitionOpcode**: Allows the charter owner to offer the charter to another player. Validates the target player's eligibility (faction, guild status, trial status) and sends the target player an `SMSG_PETITION_SHOW_SIGNATURES` packet so they can view and potentially sign the charter.

**HandleTurnInPetitionOpcode**: Converts a fully signed charter into a permanent Guild. Validates the player is the owner, the petition is complete, and the name is still available. Creates a new `Guild` object, registers it with `GuildMgr`, deletes the petition record, destroys the charter item, and confirms success to the client.

**HandlePetitionShowListOpcode**: Wrapper that calls `SendPetitionShowList` with the NPC GUID from the packet.

**SendPetitionShowList**: Sends the list of available charters to the client. Validates NPC interaction, removes fake death, and constructs an `SMSG_PETITION_SHOWLIST` packet with hardcoded Guild Charter data (entry, display ID, cost).

---

<!-- machine-true, projected from graph.json -->

## Map — WorldSession.PetitionsHandler

*Source:* PetitionsHandler.cpp, WorldSession.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| HandlePetitionBuyOpcode | method | Anticheat/GetAnticheatLib, Anticheat/GetAntispam, AntispamInterface/filterMessage, game_Objects_Item/SetState, GuildMgr/CreatePetition, GuildMgr/GetGuildByName, GuildMgr/GetPetitionByOwnerGuid, Log.Main/Out, Object/GetObjectGuid, ObjectGuid/GetString, ObjectMgr/GeneratePetitionID, ObjectMgr/GetItemPrototype, ObjectMgr/IsReservedName, ObjectMgr/IsValidCharterName, Player.Main/CanStoreNewItem, Player.Main/GetGuildId, Player.Main/GetMoney, Player.Main/GetNPCIfCanInteractWith, Player.Main/ModifyMoney, Player.Main/SaveInventoryAndGoldToDB, Player.Main/SendBuyError, Player.Main/SendEquipError, Player.Main/SendNewItem, Player.Main/StoreNewItem, Unit.Main/HasUnitState, Unit.Main/IsTabardDesigner, Unit.Main/RemoveSpellsCausingAura, World/LogChat, WorldObject.Object/SetUInt32Value, WorldSession.GuildHandler/SendGuildCommandResult, WorldSession.Main/GetPlayer, WorldSession.Main/HasTrialRestrictions, WorldSession.Main/SendNotification#2 | — | — |
| HandlePetitionShowSignOpcode | method | ByteBuffer/operator<<#10, ByteBuffer/operator<<#7, game_Objects_Item/GetEnchantmentId, GuildMgr/BuildSignatureData, GuildMgr/GetPetitionById, Log.Main/Out, Object/GetGuidStr, Object/GetObjectGuid, ObjectGuid/GetCounter, ObjectGuid/operator<<, Petition/GetSignatureCount, Player.Main/GetGuildId, Player.Main/GetItemByGuid, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | — | — |
| HandlePetitionQueryOpcode | method | ByteBuffer/operator<<, ByteBuffer/operator<<#10, ByteBuffer/operator<<#13, ByteBuffer/operator<<#7, GuildMgr/GetPetitionById, ObjectGuid/operator<<, Petition/GetName, Petition/GetOwnerGuid, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | — | — |
| HandlePetitionRenameOpcode | method | ByteBuffer/operator<<, game_Objects_Item/GetEnchantmentId, GuildMgr/GetGuildByName, GuildMgr/GetPetitionById, GuildMgr/Rename, ObjectGuid/operator<<, ObjectMgr/IsReservedName, ObjectMgr/IsValidCharterName, Player.Main/GetItemByGuid, WorldPacket/WorldPacket#4, WorldSession.GuildHandler/SendGuildCommandResult, WorldSession.Main/SendPacket | — | — |
| HandlePetitionSignOpcode | method | ByteBuffer/operator<<#10, GuildMgr/AddNewSignature, GuildMgr/GetPetitionByCharterGuid, GuildMgr/GetSignatureForPlayer, Log.Main/Out, Object/GetGuidStr, Object/GetObjectGuid, ObjectGuid/GetCounter, ObjectGuid/operator<<, ObjectGuid/operator==, ObjectMgr/GetPlayer, Petition/GetId, Petition/GetOwnerGuid, Petition/GetSignatureCount, Petition/GetTeam, Petition/IsComplete, Player.Main/GetGuildId, Player.Main/GetGuildIdInvited, Player.Main/GetName, Player.Main/GetSession, Player.Main/GetTeam, World/getConfig, WorldPacket/WorldPacket#4, WorldSession.GuildHandler/SendGuildCommandResult, WorldSession.Main/GetPlayer, WorldSession.Main/HasTrialRestrictions, WorldSession.Main/SendNotification#2, WorldSession.Main/SendPacket | — | — |
| HandlePetitionDeclineOpcode | method | GuildMgr/GetPetitionByCharterGuid, Log.Main/Out, Object/GetGuidStr, Object/GetObjectGuid, ObjectGuid/GetString, ObjectGuid/operator<<, ObjectMgr/GetPlayer, Petition/GetOwnerGuid, Player.Main/GetSession, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | — | — |
| HandleOfferPetitionOpcode | method | ByteBuffer/operator<<#10, ByteBuffer/operator<<#7, game_Objects_Item/GetEnchantmentId, GuildMgr/BuildSignatureData, GuildMgr/GetPetitionById, Log.Main/Out, Object/GetGuidStr, Object/GetObjectGuid, ObjectAccessor/FindPlayer, ObjectGuid/GetCounter, ObjectGuid/operator<<, Petition/GetSignatureCount, Player.Main/GetGuildId, Player.Main/GetGuildIdInvited, Player.Main/GetItemByGuid, Player.Main/GetName, Player.Main/GetSession, Player.Main/GetTeam, World/getConfig, WorldPacket/WorldPacket#4, WorldSession.GuildHandler/SendGuildCommandResult, WorldSession.Main/GetPlayer, WorldSession.Main/HasTrialRestrictions, WorldSession.Main/SendNotification#2, WorldSession.Main/SendPacket | — | — |
| HandleTurnInPetitionOpcode | method | ByteBuffer/operator<<#10, game_Guild_Guild/Create, game_Guild_Guild/Guild, game_Objects_Item/GetBagSlot, game_Objects_Item/GetEnchantmentId, game_Objects_Item/GetSlot, GuildMgr/AddGuild, GuildMgr/DeletePetition, GuildMgr/GetGuildByName, GuildMgr/GetPetitionById, Log.Main/Out, Object/GetGuidStr, Object/GetObjectGuid, ObjectGuid/GetCounter, ObjectGuid/operator!=, Petition/GetName, Petition/GetOwnerGuid, Petition/IsComplete, Player.Main/DestroyItem, Player.Main/GetGuildId, Player.Main/GetItemByGuid, Player.Main/GetSession, WorldPacket/WorldPacket#4, WorldSession.GuildHandler/SendGuildCommandResult, WorldSession.Main/HasTrialRestrictions, WorldSession.Main/SendNotification#2, WorldSession.Main/SendPacket | — | — |
| HandlePetitionShowListOpcode | method | — | — | — |
| SendPetitionShowList | method | ByteBuffer/operator<<#10, ByteBuffer/operator<<#7, Log.Main/Out, ObjectGuid/GetString, ObjectGuid/operator<<, Player.Main/GetNPCIfCanInteractWith, Unit.Main/HasUnitState, Unit.Main/RemoveSpellsCausingAura, WorldPacket/WorldPacket#4, WorldSession.Main/GetPlayer, WorldSession.Main/SendPacket | Player.Main/OnGossipSelect | — |

---

<!-- verify: boundary-bleed | foreign: initialize, WorldSession -->
