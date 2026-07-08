# game_Mail_Mail

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Unit: `game_Mail_Mail`

**Files:** `Mail.cpp`, `Mail.h`

## Purpose & Responsibilities

The `game_Mail_Mail` unit implements the core data structures and logic for the in-game mail system within the WoWVMaNGOS server. It handles the creation, formatting, persistence, and delivery of emails between players, NPCs, game objects, and system entities (such as the Auction House).

Key responsibilities include:
1.  **Abstraction of Sender/Receiver:** Encapsulating the identity of mail participants via `MailSender` and `MailReceiver` classes, which normalize different entity types (Players, Creatures, Items, Auctions) into a consistent format for database storage and client presentation.
2.  **Mail Drafting:** Providing the `MailDraft` class to compose messages, attach items, set monetary values (including Cash-on-Delivery), and define expiration/delivery delays.
3.  **Persistence & Delivery:** Managing the insertion of mail records into the `mail` and `mail_items` database tables, handling transactional integrity, and updating the in-memory state of online players (`MasterPlayer`) to reflect new mail immediately.
4.  **Template Item Generation:** Supporting "lazy" generation of items defined by mail templates. If a mail is sent to an offline player, items are not generated until the player logs in and loads their mail, ensuring consistency and preventing item loss if the mail fails to send.
5.  **Return Logic:** Implementing the mechanism to return undeliverable mail to the original sender, including ownership transfer of attached items and application of delivery delays if accounts differ.

## Member-by-Member Behavior

### Sender and Receiver Identification

*   **`MailSender` constructors**: These initialize the sender metadata.
    *   The constructor taking an `Object*` determines the `MailMessageType` based on the object's type ID (Creature, GameObject, Item, or Player). For Players, it uses the low GUID; for others, it uses the entry ID. It logs an error if an unexpected type is encountered.
    *   The constructor taking an `AuctionEntry*` sets the message type to `MAIL_AUCTION`, uses the Auction House ID as the sender ID, and applies specific auction stationery.
    *   The default constructor initializes a neutral sender (Normal type, ID 0).
*   **`MailReceiver` constructors**: These initialize the recipient metadata.
    *   One constructor takes a `Player*` pointer and extracts its GUID.
    *   Another allows overriding the GUID used for identification while retaining the `Player*` pointer (useful for mass mail or specific routing scenarios), asserting that if both are provided, they match.

### Mail Composition (`MailDraft`)

*   **`MailDraft` constructors**:
    *   One variant accepts a `mailTemplateId`. It marks the draft as needing item generation (`m_mailTemplateItemsNeed = true`). For older client builds, it pre-fetches the body text from the template.
    *   Another variant accepts a subject string and body text string. It creates a new item text record in the database for the body if the text is not empty.
    *   A third variant accepts a subject and an existing `itemTextId`.
*   **`SetSubjectAndBodyId` / `SetSubjectAndBody`**: These methods allow modifying the subject and body of a draft. `SetSubjectAndBody` dynamically creates a new item text record for the provided string. Both assert that the body ID has not already been set, enforcing a single-source-of-truth for the body content.
*   **`AddItem`**: Adds an `Item*` to the internal `m_items` map, keyed by the item's low GUID. This prevents duplicate items from being added if the same item pointer is passed multiple times.
*   **`prepareItems`**: This private method generates items for a mail template. It is called during `SendMailTo` if the receiver is online. It uses the `Loot` system to roll items based on the `mailTemplateId`. If items are successfully rolled, they are created, saved to the database, and added to the draft. This ensures that template-based items are materialized before the mail is persisted.
*   **`deleteIncludedItems`**: Cleans up items attached to the draft. If `inDB` is true, it deletes the item instances from the `item_instance` table. It then deletes the C++ `Item` objects and clears the map. This is crucial for cleaning up failed sends or returned mail.
*   **`CloneFrom`**: Creates a deep copy of another `MailDraft`. It clones the subject, body text (by fetching the text and creating a new record), money, COD, and all attached items. Cloning items involves creating new `Item` instances and saving them to the database, ensuring the new draft has independent item ownership. This is primarily used by `MassMailMgr` to send identical mail to multiple recipients.

### Delivery and Persistence

*   **`SendMailTo`**: The central method for persisting and delivering a mail.
    1.  **Preparation**: If the receiver is online, it calls `prepareItems` to generate template items.
    2.  **Timing**: Calculates `deliver_time` (current time + delay) and `expire_time`. Expiration defaults to 30 days, 3 days for COD, or 1 hour for empty auction notifications.
    3.  **Database Insertion**: Begins a transaction. Inserts a row into the `mail` table with all metadata (sender, receiver, subject, money, COD, timestamps, etc.). Then, for each item in the draft, inserts a row into `mail_items`. Commits the transaction.
    4.  **In-Memory Update**: If the receiver is online (`MasterPlayer` exists), it constructs a `Mail` struct, populates it with the draft's data, and adds it to the player's in-memory mail list via `MasterPlayer::AddMail` and `MasterPlayer::AddMItem`. This ensures the player sees the mail immediately without reloading.
    5.  **Cleanup**: If the receiver is offline and items were attached, it deletes the items from memory (they remain in the DB linked to the mail). If the send fails or items are not needed, `deleteIncludedItems` is called.
*   **`SendReturnToSender`**: Handles returning mail to the original sender.
    1.  **Validation**: Checks if the original sender still exists. If not, it deletes the attached items from the database and returns.
    2.  **Ownership Transfer**: If items are present, it updates the `owner_guid` in the `item_instance` table to the receiver's GUID (who is now the sender of the returned mail) within a transaction. This prevents items from being deleted if the original sender's character is deleted.
    3.  **Delay Application**: If the sender and receiver are on different accounts, it applies a configurable delivery delay (`CONFIG_UINT32_MAIL_DELIVERY_DELAY`) to prevent instant trading exploits.
    4.  **Resending**: Calls `SendMailTo` with the original sender as the receiver and the current receiver as the sender, marking the mail as `MAIL_CHECK_MASK_RETURNED`.

### Offline Item Generation

*   **`Mail::prepareTemplateItems`**: This method is called when an offline player logs in and loads their mail. If a mail has a `mailTemplateId` but no items yet (`has_items` is false), it generates the items now.
    1.  It rolls the loot template.
    2.  Updates the `mail` table to set `has_items = 1`.
    3.  Creates the `Item` objects, saves them to the DB, and inserts rows into `mail_items`.
    4.  Adds the items to the player's in-memory `MasterPlayer` state.

## Cross-Unit Boundaries

*   **`AuctionHouseMgr`**: Calls `MailSender` (auction variant) and `MailDraft::SendMailTo` to send auction notifications (won, expired, outbid, etc.). It relies on `MailSender` to correctly identify the auction house as the sender.
*   **`Player`**: Calls `MailSender` (object variant) and `MailDraft::SendMailTo` for quest rewards and inventory management (auto-unequipping items sent via mail). It also calls `Mail::prepareTemplateItems` during `_LoadInventory` to ensure offline-generated mail items are ready.
*   **`WorldSession.MailHandler`**: Calls `MailSender` and `MailDraft::SendMailTo` for player-initiated mail sending (`HandleSendMailCallback`) and taking items from mail (`HandleMailTakeItem`). It also calls `MailDraft::SendReturnToSender` when a player returns a mail.
*   **`ChatHandler`**: Uses `MailSender` and `MailDraft` to implement GM commands for sending mail, items, and money to players.
*   **`MassMailMgr`**: Calls `MailDraft::CloneFrom` to create copies of a draft for mass distribution, then calls `SendMailTo` for each recipient.
*   **`ObjectMgr`**: Called by `MailDraft` to create item text records (`CreateItemText`) and retrieve existing ones (`GetItemText`). Also used to generate unique mail IDs (`GenerateMailID`).
*   **`Database`**: `MailDraft::SendMailTo`, `SendReturnToSender`, `deleteIncludedItems`, and `Mail::prepareTemplateItems` all interact directly with the `CharacterDatabase` to insert/update/delete records in `mail`, `mail_items`, and `item_instance`.

## Data Model

This unit interacts with three primary database tables:

1.  **`mail`**: Stores the core metadata of each email.
    *   Used by `SendMailTo` to insert new mail records.
    *   Columns used: `id`, `message_type`, `stationery`, `mail_template_id`, `sender_guid`, `receiver_guid`, `subject`, `item_text_id`, `has_items`, `expire_time`, `deliver_time`, `money`, `cod`, `checked`.
    *   Updated by `Mail::prepareTemplateItems` to set `has_items = 1` when template items are generated for an offline player.

2.  **`mail_items`**: Links items to specific mail records.
    *   Used by `SendMailTo` to insert rows for each item attached to a mail.
    *   Used by `Mail::prepareTemplateItems` to insert rows for items generated from templates upon login.
    *   Columns used: `mail_id`, `item_guid`, `item_id`, `receiver_guid`.

3.  **`item_instance`**: Stores the persistent state of items.
    *   Used by `MailDraft::prepareItems` and `Mail::prepareTemplateItems` to save newly created items to the database.
    *   Used by `MailDraft::deleteIncludedItems` to remove items from the database if the mail is cancelled or failed.
    *   Used by `MailDraft::SendReturnToSender` to update the `owner_guid` of items when mail is returned, transferring ownership to the new sender.

## Notable Implementation Details

*   **Lazy Item Generation**: Items defined by mail templates are not generated at send time if the receiver is offline. This is handled by `Mail::prepareTemplateItems` during login. This design choice prevents item duplication or loss if the mail system fails after item creation but before persistence, and ensures that the item generation context (player level, etc.) is valid at the time of receipt.
*   **Transactional Integrity**: All database writes involving mail and items are wrapped in `BeginTransaction`/`CommitTransaction` blocks. This ensures that either the mail record and all its item links are created together, or none are, preventing orphaned items or mail records.
*   **Item Ownership Transfer on Return**: When mail is returned, `SendReturnToSender` explicitly updates the `owner_guid` in `item_instance` to the receiver's GUID (who becomes the sender of the returned mail). This is critical because if the original sender's character is deleted, the items would otherwise be destroyed. By transferring ownership first, the items survive the deletion of the original sender's character.
*   **Delivery Delay for Cross-Account Mail**: `SendReturnToSender` applies a configurable delay if the sender and receiver are on different accounts. This is a security measure to prevent instant trading exploits via mail return mechanisms.
*   **In-Memory vs. Database State**: For online players, `SendMailTo` updates both the database and the in-memory `MasterPlayer` structure. This ensures immediate visibility of new mail without requiring a reload. For offline players, only the database is updated, and the in-memory state is populated later during login.
*   **Assertion on Body ID**: `SetSubjectAndBodyId` and `SetSubjectAndBody` assert that `m_bodyId` is zero before setting it. This enforces that the body text is set exactly once, preventing accidental overwrites or inconsistencies in the mail draft.
*   **Error Handling in Sender Construction**: The `MailSender(Object*)` constructor logs an error if the object type is unexpected, defaulting to `MAIL_NORMAL` with ID 0. This prevents crashes but may result in confusing mail origins in the client.

## Member Reference

**MailSender#4** (ctor): Initializes a `MailSender` from an `Object*`. Determines message type and sender ID based on the object's type (Creature, GameObject, Item, Player). Logs errors for unexpected types. Called by `Player.Main/AutoUnequipItemFromSlot`, `Player.Main/RewardQuest`, `Player.Main/_LoadInventory`, `WorldSession.MailHandler/HandleMailTakeItem`, `WorldSession.MailHandler/HandleSendMailCallback`.

**MailSender#3** (ctor): Initializes a `MailSender` from an `AuctionEntry*`. Sets message type to `MAIL_AUCTION`, sender ID to the auction house ID, and stationery to `MAIL_STATIONERY_AUCTION`. Called by `AuctionHouseMgr/LoadAuctions`, `AuctionHouseMgr/SendAuctionExpiredMail`, `AuctionHouseMgr/SendAuctionSuccessfulMail`, `AuctionHouseMgr/SendAuctionWonMail`, `WorldSession.AuctionHouseHandler/HandleAuctionRemoveItem`, `WorldSession.AuctionHouseHandler/SendAuctionCancelledToBidderMail`, `WorldSession.AuctionHouseHandler/SendAuctionOutbiddedMail`.

**MailReceiver** (ctor): Initializes a `MailReceiver` from a `Player*`. Extracts the player's GUID. Called by `game_Battlegrounds_BattleGround/SendRewardMarkByMail`, `Player.Main/AutoUnequipItemFromSlot`, `Player.Main/RewardQuest`, `Player.Main/_LoadInventory`, `WorldSession.AuctionHouseHandler/HandleAuctionRemoveItem`.

**MailReceiver#2** (ctor): Initializes a `MailReceiver` with a `Player*` and an optional override `ObjectGuid`. Asserts consistency if both are provided. Called by `AuctionHouseMgr/SendAuctionExpiredMail`, `AuctionHouseMgr/SendAuctionSuccessfulMail`, `AuctionHouseMgr/SendAuctionWonMail`, `ChatHandler.MiscCommands/HandleSendItemsCommand`, `ChatHandler.MiscCommands/HandleSendMailCommand`, `ChatHandler.MiscCommands/HandleSendMoneyCommand`, `MassMailMgr/Update`, `WorldSession.AuctionHouseHandler/SendAuctionCancelledToBidderMail`, `WorldSession.AuctionHouseHandler/SendAuctionOutbiddedMail`, `WorldSession.MailHandler/HandleMailTakeItem`, `WorldSession.MailHandler/HandleSendMailCallback`.

**MailDraft#2** (ctor): Initializes a `MailDraft` with a subject string and an existing `itemTextId`. Called by `GameEventMgr.Main/SendEventMails`, `Player.Main/RewardQuest`.

**MailSender** (ctor): Default constructor. Initializes a neutral sender (Normal type, ID 0, default stationery). Not called by other units in the map.

**MailDraft** (ctor): Initializes a `MailDraft` with a subject string and body text string. Creates a new item text record for the body if not empty. Called by `AuctionHouseMgr/LoadAuctions`, `AuctionHouseMgr/SendAuctionSuccessfulMail`, `AuctionHouseMgr/SendAuctionWonMail`, `game_Battlegrounds_BattleGround/SendRewardMarkByMail`, `ObjectMgr/RestoreDeletedItems`, `WorldSession.MailHandler/HandleSendMailCallback`.

**MailSender#2** (ctor): Initializes a `MailSender` with explicit message type, sender ID, and stationery. Called by `ChatHandler.MiscCommands/HandleSendItemsCommand`, `ChatHandler.MiscCommands/HandleSendMailCommand`, `ChatHandler.MiscCommands/HandleSendMassItemsCommand`, `ChatHandler.MiscCommands/HandleSendMassMailCommand`, `ChatHandler.MiscCommands/HandleSendMassMoneyCommand`, `ChatHandler.MiscCommands/HandleSendMoneyCommand`, `GameEventMgr.Main/SendEventMails`, `game_Battlegrounds_BattleGround/SendRewardMarkByMail`, `ObjectMgr/RestoreDeletedItems`, `Player.Main/RewardQuest`.

**SetSubjectAndBodyId** (method): Sets the subject and body ID of a `MailDraft`. Asserts that the body ID was not previously set. Called by `Player.Main/DeleteFromDB`, `WorldSession.MailHandler/HandleMailReturnToSender`.

**GetMailMessageType** (method): Returns the message type of the sender. Not called by other units in the map.

**GetSenderId** (method): Returns the sender ID (low GUID or entry). Not called by other units in the map.

**GetStationery** (method): Returns the stationery type of the sender. Not called by other units in the map.

**SetSubjectAndBody** (method): Sets the subject and body text of a `MailDraft`. Creates a new item text record for the body. Asserts that the body ID was not previously set. Called by `ChatHandler.MiscCommands/HandleSendItemsHelper`, `ChatHandler.MiscCommands/HandleSendMailHelper`, `ChatHandler.MiscCommands/HandleSendMoneyHelper`.

**MailSender#5** (decl): Private trap constructor to prevent misuse of 64-bit GUIDs. Not implemented.

**AddItem** (method): Adds an `Item*` to the `MailDraft`'s internal map, keyed by low GUID. Called by `AuctionHouseMgr/LoadAuctions`, `AuctionHouseMgr/SendAuctionExpiredMail`, `AuctionHouseMgr/SendAuctionWonMail`, `ChatHandler.MiscCommands/HandleSendItemsHelper`, `game_Battlegrounds_BattleGround/SendRewardMarkByMail`, `ObjectMgr/RestoreDeletedItems`, `Player.Main/AutoUnequipItemFromSlot`, `Player.Main/DeleteFromDB`, `Player.Main/_LoadInventory`, `WorldSession.AuctionHouseHandler/HandleAuctionRemoveItem`, `WorldSession.MailHandler/HandleMailReturnToSender`, `WorldSession.MailHandler/HandleSendMailCallback`.

**prepareItems** (method): Generates items for a mail template if the receiver is online. Uses the `Loot` system to roll items, creates them, saves to DB, and adds to the draft. Called internally by `SendMailTo`.

**deleteIncludedItems** (method): Deletes items attached to the draft from memory and optionally from the `item_instance` table. Called internally by `SendMailTo` and `SendReturnToSender`.

**CloneFrom** (method): Deep copies another `MailDraft`, including cloning items and creating new item text records. Called by `MassMailMgr/Update`.

**SendReturnToSender** (method): Returns mail to the original sender. Transfers item ownership, applies delivery delays for cross-account returns, and resends the mail. Called by `Player.Main/DeleteFromDB`, `WorldSession.MailHandler/HandleMailReturnToSender`.

**SendMailTo** (method): Persists the mail to the `mail` and `mail_items` tables, updates in-memory state for online players, and handles cleanup. Called by `AuctionHouseMgr/LoadAuctions`, `AuctionHouseMgr/SendAuctionExpiredMail`, `AuctionHouseMgr/SendAuctionSuccessfulMail`, `AuctionHouseMgr/SendAuctionWonMail`, `ChatHandler.MiscCommands/HandleSendItemsCommand`, `ChatHandler.MiscCommands/HandleSendMailCommand`, `ChatHandler.MiscCommands/HandleSendMoneyCommand`, `game_Battlegrounds_BattleGround/SendRewardMarkByMail`, `MassMailMgr/Update`, `ObjectMgr/RestoreDeletedItems`, `Player.Main/AutoUnequipItemFromSlot`, `Player.Main/RewardQuest`, `Player.Main/_LoadInventory`, `WorldSession.AuctionHouseHandler/HandleAuctionRemoveItem`, `WorldSession.AuctionHouseHandler/SendAuctionCancelledToBidderMail`, `WorldSession.AuctionHouseHandler/SendAuctionOutbiddedMail`, `WorldSession.MailHandler/HandleMailTakeItem`, `WorldSession.MailHandler/HandleSendMailCallback`.

**prepareTemplateItems** (method): Generates items for a mail template when an offline player logs in. Updates the `mail` table and inserts rows into `mail_items`. Called by `MasterPlayer.Main/LoadMails`.

---

<!-- machine-true, projected from graph.json -->

## Map — game_Mail_Mail

*Source:* Mail.cpp, Mail.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| MailSender#4 | ctor | Log.Main/Out, Object/GetEntry, Object/GetGUIDLow, Object/GetTypeId | Player.Main/AutoUnequipItemFromSlot, Player.Main/RewardQuest, Player.Main/_LoadInventory, WorldSession.MailHandler/HandleMailTakeItem, WorldSession.MailHandler/HandleSendMailCallback | — |
| MailSender#3 | ctor | AuctionEntry/GetHouseId | AuctionHouseMgr/LoadAuctions, AuctionHouseMgr/SendAuctionExpiredMail, AuctionHouseMgr/SendAuctionSuccessfulMail, AuctionHouseMgr/SendAuctionWonMail, WorldSession.AuctionHouseHandler/HandleAuctionRemoveItem, WorldSession.AuctionHouseHandler/SendAuctionCancelledToBidderMail, WorldSession.AuctionHouseHandler/SendAuctionOutbiddedMail | — |
| MailReceiver | ctor | Object/GetObjectGuid | game_Battlegrounds_BattleGround/SendRewardMarkByMail, Player.Main/AutoUnequipItemFromSlot, Player.Main/RewardQuest, Player.Main/_LoadInventory, WorldSession.AuctionHouseHandler/HandleAuctionRemoveItem | — |
| MailReceiver#2 | ctor | Errors/PrintStacktraceAndThrow, Object/GetObjectGuid, ObjectGuid/operator== | AuctionHouseMgr/SendAuctionExpiredMail, AuctionHouseMgr/SendAuctionSuccessfulMail, AuctionHouseMgr/SendAuctionWonMail, ChatHandler.MiscCommands/HandleSendItemsCommand, ChatHandler.MiscCommands/HandleSendMailCommand, ChatHandler.MiscCommands/HandleSendMoneyCommand, MassMailMgr/Update, WorldSession.AuctionHouseHandler/SendAuctionCancelledToBidderMail, WorldSession.AuctionHouseHandler/SendAuctionOutbiddedMail, WorldSession.MailHandler/HandleMailTakeItem, WorldSession.MailHandler/HandleSendMailCallback | — |
| MailDraft#2 | ctor | — | GameEventMgr.Main/SendEventMails, Player.Main/RewardQuest | — |
| MailSender | ctor | — | — | — |
| MailDraft | ctor | ObjectMgr/CreateItemText | AuctionHouseMgr/LoadAuctions, AuctionHouseMgr/SendAuctionSuccessfulMail, AuctionHouseMgr/SendAuctionWonMail, game_Battlegrounds_BattleGround/SendRewardMarkByMail, ObjectMgr/RestoreDeletedItems, WorldSession.MailHandler/HandleSendMailCallback | — |
| MailSender#2 | ctor | — | ChatHandler.MiscCommands/HandleSendItemsCommand, ChatHandler.MiscCommands/HandleSendMailCommand, ChatHandler.MiscCommands/HandleSendMassItemsCommand, ChatHandler.MiscCommands/HandleSendMassMailCommand, ChatHandler.MiscCommands/HandleSendMassMoneyCommand, ChatHandler.MiscCommands/HandleSendMoneyCommand, GameEventMgr.Main/SendEventMails, game_Battlegrounds_BattleGround/SendRewardMarkByMail, ObjectMgr/RestoreDeletedItems, Player.Main/RewardQuest | — |
| SetSubjectAndBodyId | method | Errors/PrintStacktraceAndThrow | Player.Main/DeleteFromDB, WorldSession.MailHandler/HandleMailReturnToSender | — |
| GetMailMessageType | method | — | — | — |
| GetSenderId | method | — | — | — |
| GetStationery | method | — | — | — |
| SetSubjectAndBody | method | Errors/PrintStacktraceAndThrow, ObjectMgr/CreateItemText | ChatHandler.MiscCommands/HandleSendItemsHelper, ChatHandler.MiscCommands/HandleSendMailHelper, ChatHandler.MiscCommands/HandleSendMoneyHelper | — |
| MailSender#5 | decl | — | — | — |
| AddItem | method | Object/GetGUIDLow | AuctionHouseMgr/LoadAuctions, AuctionHouseMgr/SendAuctionExpiredMail, AuctionHouseMgr/SendAuctionWonMail, ChatHandler.MiscCommands/HandleSendItemsHelper, game_Battlegrounds_BattleGround/SendRewardMarkByMail, ObjectMgr/RestoreDeletedItems, Player.Main/AutoUnequipItemFromSlot, Player.Main/DeleteFromDB, Player.Main/_LoadInventory, WorldSession.AuctionHouseHandler/HandleAuctionRemoveItem, WorldSession.MailHandler/HandleMailReturnToSender, WorldSession.MailHandler/HandleSendMailCallback | — |
| prepareItems | method | game_Objects_Item/CreateItem, game_Objects_Item/SaveToDB, Loot/Loot, LootMgr/FillLoot, LootMgr/GetMaxSlotInLootFor, LootMgr/LootItemInSlot, Object/GetGUIDLow, Object/GetObjectGuid | — | — |
| deleteIncludedItems | method | Database/PExecute#2, Object/GetGUIDLow | — | item_instance |
| CloneFrom | method | Errors/PrintStacktraceAndThrow, game_Objects_Item/CloneItem, game_Objects_Item/GetCount, game_Objects_Item/SaveToDB, MailDraft/GetBodyId, MailDraft/GetCOD, MailDraft/GetMailTemplateId, MailDraft/GetMoney, MailDraft/GetSubject, ObjectMgr/CreateItemText, ObjectMgr/GetItemText | MassMailMgr/Update | — |
| SendReturnToSender | method | Database/BeginTransaction, Database/CommitTransaction, Database/PExecute#2, game_Objects_Item/SaveToDB, Object/GetGUIDLow, ObjectGuid/GetCounter, ObjectMgr/GetPlayer, ObjectMgr/GetPlayerAccountIdByGUID, World/getConfig#4 | Player.Main/DeleteFromDB, WorldSession.MailHandler/HandleMailReturnToSender | item_instance |
| SendMailTo | method | Database/BeginTransaction, Database/CommitTransaction, Database/escape_string, Database/PExecute#2, Mail/AddItem, MailDraft/GetBodyId, MailDraft/GetCOD, MailDraft/GetMailTemplateId, MailDraft/GetMoney, MailDraft/GetSubject, MailReceiver/GetPlayer, MailReceiver/GetPlayerGuid, MasterPlayer.Main/AddMail, MasterPlayer.Main/AddMItem, MasterPlayer.Main/AddNewMailDeliverTime, Object/GetEntry, Object/GetGUIDLow, ObjectAccessor/FindMasterPlayer, ObjectGuid/GetCounter, ObjectMgr/GenerateMailID | AuctionHouseMgr/LoadAuctions, AuctionHouseMgr/SendAuctionExpiredMail, AuctionHouseMgr/SendAuctionSuccessfulMail, AuctionHouseMgr/SendAuctionWonMail, ChatHandler.MiscCommands/HandleSendItemsCommand, ChatHandler.MiscCommands/HandleSendMailCommand, ChatHandler.MiscCommands/HandleSendMoneyCommand, game_Battlegrounds_BattleGround/SendRewardMarkByMail, MassMailMgr/Update, ObjectMgr/RestoreDeletedItems, Player.Main/AutoUnequipItemFromSlot, Player.Main/RewardQuest, Player.Main/_LoadInventory, WorldSession.AuctionHouseHandler/HandleAuctionRemoveItem, WorldSession.AuctionHouseHandler/SendAuctionCancelledToBidderMail, WorldSession.AuctionHouseHandler/SendAuctionOutbiddedMail, WorldSession.MailHandler/HandleMailTakeItem, WorldSession.MailHandler/HandleSendMailCallback | mail, mail_items |
| prepareTemplateItems | method | Database/BeginTransaction, Database/CommitTransaction, Database/PExecute#2, game_Objects_Item/CreateItem, game_Objects_Item/SaveToDB, Loot/Loot, LootMgr/FillLoot, LootMgr/GetMaxSlotInLootFor, LootMgr/LootItemInSlot, Mail/AddItem, MasterPlayer.Main/AddMItem, MasterPlayer.Main/GetGUIDLow, Object/GetEntry, Object/GetGUIDLow, Object/GetObjectGuid, Player.Main/GetSession, WorldSession.Main/GetMasterPlayer | MasterPlayer.Main/LoadMails | mail, mail_items |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `item_instance`: guid int(10) unsigned PK, item_id mediumint(8) unsigned, owner_guid int(10) unsigned, creator_guid int(10) unsigned, gift_creator_guid int(10) unsigned, count int(10) unsigned, duration int(10), charges tinytext?, flags mediumint(8) unsigned, enchantments text, random_property_id smallint(5), durability smallint(5) unsigned, text int(10) unsigned, generated_loot tinyint(4)?
- `mail`: id int(11) unsigned PK, message_type tinyint(3) unsigned, stationery tinyint(3), mail_template_id mediumint(8) unsigned, sender_guid int(11) unsigned, receiver_guid int(11) unsigned, subject longtext?, item_text_id int(11) unsigned, has_items tinyint(3) unsigned, expire_time bigint(40), deliver_time bigint(40), money int(11) unsigned, cod int(11) unsigned, checked tinyint(3) unsigned
- `mail_items`: mail_id int(11) unsigned PK, item_guid int(11) unsigned PK, item_id int(11) unsigned, receiver_guid int(11) unsigned

*`?` = nullable, `PK` = primary key column.*

