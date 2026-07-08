<!-- provenance: boundary-bleed -->
# WorldSession.MailHandler

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# WorldSession.MailHandler

## Purpose & Responsibilities

The `WorldSession.MailHandler` partial implements the server-side logic for the in-game mail system within the `wowvmangos` emulator. It handles all client-server communication related to sending, receiving, reading, deleting, and managing items or currency within mail messages.

Key responsibilities include:
1.  **Packet Handling:** Parsing incoming mail-related opcodes (e.g., `CMSG_SEND_MAIL`, `CMSG_TAKE_MAIL_ITEM`) and generating outgoing responses (e.g., `SMSG_SEND_MAIL_RESULT`, `SMSG_MAIL_LIST_RESULT`).
2.  **Validation & Security:** Enforcing game rules such as mailbox interaction ranges, trial account restrictions, faction limits, anti-spam measures, and anti-cheat checks (e.g., preventing sending conjured items, bags with contents, or exceeding gold caps).
3.  **State Management:** Updating the `MasterPlayer`'s internal mail list, marking messages as read/deleted, and handling delivery delays.
4.  **Persistence:** Coordinating with the database (`mail`, `mail_items`, `item_instance`) to persist mail records, transfer item ownership, and update player gold/inventory states via transactions.
5.  **Asynchronous Processing:** Using an asynchronous query pattern (`AsyncMailSendRequest`) to check recipient mailbox capacity without blocking the main thread during the initial send validation phase.

## Member-by-Member Behavior

### Sending Mail

**`HandleSendMail`**
Entry point for the `CMSG_SEND_MAIL` opcode. It validates the mailbox interaction via `CheckMailBox`. It performs initial sanity checks:
-   Blocks trial accounts.
-   Validates subject/body length limits.
-   Enforces COD (Cash on Delivery) limits and anti-cheat logging for excessive COD amounts.
-   Applies GM trade restrictions if configured.
-   Resolves the recipient's GUID from the name using `ObjectMgr`.
-   Prevents self-mailing.
-   Constructs an `AsyncMailSendRequest` object and passes it to `HandleSendMailRequest` for further processing.

**`HandleSendMailRequest`**
Continues validation after recipient resolution.
-   If the recipient is online, it immediately checks their trial status and mailbox size, then invokes the callback.
-   If the recipient is offline, it issues an asynchronous database query (`CharacterDatabase.AsyncPQueryUnsafe`) to count existing mails for the recipient. The result triggers `AsyncMailSendRequest::Callback`.

**`AsyncMailSendRequest` (ctor)**
Constructor for the helper class used to hold state during the asynchronous mail send process. Initializes fields to default values.

**`AsyncMailSendRequest::Callback`**
Executed upon completion of the asynchronous mailbox size query (or immediately if the recipient was online).
-   Verifies the sender's session and player are still valid and in-world.
-   Retrieves the mailbox count from the query result (if applicable).
-   Invokes `HandleSendMailCallback` to perform the final transaction and send the mail.
-   Deletes itself.

**`HandleSendMailCallback`**
The core logic for finalizing a mail send.
-   **Cost Calculation:** Adds a 30 copper fee to the money amount. Checks for integer overflow and sufficient funds.
-   **Recipient Limits:** Ensures the recipient has fewer than 100 mails.
-   **Faction Checks:** Blocks cross-faction mail if configured.
-   **Trade Window Conflict:** Prevents sending if the player is in a trade window (anti-spoofing).
-   **Item Validation:** If an item is attached, verifies it exists, is in the world, is not in the bank, is tradable, and is not conjured or temporary.
-   **Anti-Spam:** Checks level/money/item thresholds and account-level mail limits via `AccountMgr`. Logs violations.
-   **Execution:**
    -   Deducts money from the sender.
    -   Logs the transaction.
    -   If an item is sent: Removes it from the sender's inventory, updates the `item_instance` table to change `owner_guid` to the recipient, and saves the item.
    -   Constructs a `MailDraft` with money, COD, and items.
    -   Calculates delivery delay (instant for text-only, configurable delay otherwise).
    -   Sends the mail via `MailDraft::SendMailTo`.
    -   Saves the sender's inventory and gold to the database.

### Receiving & Managing Mail

**`HandleGetMailList`**
Handles `CMSG_GET_MAIL_LIST`. Iterates through the player's `MasterPlayer` mail list.
-   Filters out deleted, undelivered, or expired mails.
-   Constructs the `SMSG_MAIL_LIST_RESULT` packet, including detailed item information (entry, enchantments, durability, etc.) if an item is attached.
-   Updates the client's mail count and triggers `MasterPlayer::UpdateNextMailTimeAndUnreads`.

**`HandleMailMarkAsRead`**
Handles `CMSG_MAIL_MARK_AS_READ`.
-   Validates the mailbox.
-   Retrieves the mail object. If valid and not deleted, marks it as read (`MAIL_CHECK_MASK_READ`), decreases the unread count, and updates the expiration time if it exceeds 3 days.
-   Marks the mail state as changed.

**`HandleMailDelete`**
Handles `CMSG_MAIL_DELETE`.
-   Validates the mailbox.
-   Prevents deletion of COD mails (must be returned or taken).
-   Sets the mail state to `MAIL_STATE_DELETED` and notifies the client.

**`HandleMailReturnToSender`**
Handles `CMSG_MAIL_RETURN_TO_SENDER`.
-   Validates the mailbox and mail state.
-   Deletes the mail record and associated items from the `mail` and `mail_items` database tables.
-   Removes the mail from the player's memory structure.
-   If the mail is normal and has a sender, creates a new `MailDraft` containing the original items and money, and sends it back to the sender using `MailDraft::SendReturnToSender`.

**`HandleMailTakeItem`**
Handles `CMSG_TAKE_MAIL_ITEM`.
-   Validates the mailbox and mail state.
-   Checks if the player has enough gold to pay the COD.
-   Checks if the player has inventory space for the item.
-   If COD is present:
    -   Logs the transaction.
    -   Sends a new mail to the original sender with the COD amount.
    -   Deducts COD from the player.
-   Moves the item from the mail storage to the player's inventory.
-   Updates the database: Saves inventory, gold, and mail state.
-   Removes the item from the mail object in memory.

**`HandleMailTakeMoney`**
Handles `CMSG_TAKE_MAIL_MONEY`.
-   Validates the mailbox and mail state.
-   Checks for gold cap overflow.
-   Adds the money to the player's gold.
-   Logs the modification.
-   Updates the database: Saves gold and mail state.
-   Sets the mail's money field to 0.

**`HandleMailCreateTextItem`**
Handles `CMSG_MAIL_CREATE_TEXT_ITEM`. Allows players to "print" the body of a mail as a physical item.
-   Validates the mailbox and mail state.
-   Creates a new item using the `MAIL_BODY_ITEM_TEMPLATE`.
-   Sets the item's text ID and creator GUID.
-   Stores the item in the player's inventory if space permits.
-   Marks the mail as copied (`MAIL_CHECK_MASK_COPIED`).

**`HandleItemTextQuery`**
Handles `CMSG_ITEM_TEXT_QUERY`. Returns the text content associated with a specific `itemTextId` from the `ObjectMgr`.

**`HandleQueryNextMailTime`**
Handles `MSG_QUERY_NEXT_MAIL_TIME`. Returns `0.0` if the player has unread mail, otherwise `-86400.0` (indicating no new mail for 24 hours).

### Helper Methods

**`SendMailResult`**
Constructs and sends the `SMSG_SEND_MAIL_RESULT` packet to the client, indicating success or failure of a mail action. Includes specific error codes for equip errors or item details if an item was taken.

**`SendNewMail`**
Sends the `SMSG_RECEIVED_MAIL` packet to notify the client that new mail has arrived.

**`CheckMailBox`**
Verifies that the player can interact with the specified mailbox GameObject. Logs a debug message if the check fails.

## Cross-Unit Boundaries

### Calls Out

*   **`MasterPlayer.Main`**: Extensively used to access the player's mail list (`GetMail`, `GetMailBegin`, `GetMailEnd`), modify mail state (`MarkMailsUpdated`, `DecreaseUnreadMailsCount`), manage mail items (`GetMItem`, `RemoveMItem`), and save mail data (`SaveMails`). Also used to get player GUIDs and team info.
*   **`game_Mail_Mail`**: Uses `MailDraft` to construct mail messages, `MailReceiver`/`MailSender` for addressing, and `SendMailTo`/`SendReturnToSender` to dispatch mails.
*   **`game_Objects_Item`**: Interacts with `Item` objects to validate tradability, get properties (count, proto, enchantments), move items between inventory and mail, and save/delete items from the DB.
*   **`Player.Main`**: Accesses player inventory, gold, and session data. Calls `MoveItemFromInventory`, `MoveItemToInventory`, `ModifyMoney`, `SaveInventoryAndGoldToDB`, and `CanStoreItem`.
*   **`ObjectMgr`**: Resolves player names to GUIDs (`GetPlayerGuidByName`), retrieves player accounts (`GetPlayerAccountIdByGUID`), and fetches item text (`GetItemText`).
*   **`Database`**: Executes direct SQL queries for mail persistence (`PExecute`), transaction management (`BeginTransaction`, `CommitTransaction`), and asynchronous queries for mailbox size.
*   **`AccountMgr`**: Checks trial restrictions (`HasTrialRestrictions`) and manages anti-spam limits (`CanMail`, `JustMailed`, `GetAccountPersistentData`).
*   **`World`**: Retrieves configuration settings (`getConfig`) for mail delays, GM trade permissions, and spam levels. Logs transactions (`LogTransaction`) and inserts logs (`InsertLog`).
*   **`ChatHandler.Chat`**: Formats player links for logging purposes.
*   **`Errors`**: Throws exceptions via `PrintStacktraceAndThrow` in various handlers if critical assertions fail (though many handlers just return early).
*   **`Log.Main`**: Outputs debug and detail logs for mail operations.
*   **`ByteBuffer` / `WorldPacket`**: Used to construct outgoing network packets.
*   **`WorldSession.Main`**: Calls `SendPacket` to transmit data to the client. Note: `SendPacket` is implemented in the `WorldSession.Main` partial, not this unit.

### Called By

*   **`ChatHandler.DebugCommands`**: `HandleDebugSendMailErrorCommand` calls `SendMailResult` to simulate mail errors for debugging.
*   **`MasterPlayer.Main`**: `AddNewMailDeliverTime` and `Update` call `SendNewMail` to notify the client of new arrivals.

## Data Model

The unit interacts with the following database tables:

*   **`mail`**:
    *   Used in `HandleSendMailCallback` (implicitly via `MailDraft::SendMailTo`) to insert new mail records.
    *   Used in `HandleSendMailRequest` to count existing mails for a recipient (`SELECT COUNT(*) FROM mail WHERE receiver_guid = ...`).
    *   Used in `HandleMailReturnToSender` to delete mail records (`DELETE FROM mail WHERE id = ...`).
    *   Columns involved: `id`, `receiver_guid`, `sender_guid`, `money`, `cod`, `subject`, `item_text_id`, `has_items`, `expire_time`, `deliver_time`, `checked`, `message_type`, `stationery`, `mail_template_id`.

*   **`mail_items`**:
    *   Used in `HandleSendMailCallback` (implicitly via `MailDraft::SendMailTo`) to insert item associations.
    *   Used in `HandleMailReturnToSender` to delete item associations (`DELETE FROM mail_items WHERE mail_id = ...`).
    *   Columns involved: `mail_id`, `item_guid`, `item_id`, `receiver_guid`.

*   **`item_instance`**:
    *   Used in `HandleSendMailCallback` to update the `owner_guid` of an item being mailed (`UPDATE item_instance SET owner_guid = ... WHERE guid = ...`).
    *   Columns involved: `owner_guid`, `guid`.

## Notable Implementation Details

1.  **Asynchronous Mail Sending**: The mail send process is split into synchronous validation (`HandleSendMail`), asynchronous capacity checking (`HandleSendMailRequest` + `AsyncMailSendRequest::Callback`), and final execution (`HandleSendMailCallback`). This prevents blocking the main thread while querying the database for the recipient's mailbox size. However, note that `AsyncPQueryUnsafe` is used, which implies the callback might execute in a context where thread safety must be carefully managed (though the code checks session validity).

2.  **COD Logic**:
    *   COD can only be sent with items. If a mail has COD but no item, the COD is zeroed out and logged as an anticheat violation in `HandleSendMailCallback`.
    *   When taking an item with COD, the player pays the COD, and a new mail is automatically generated to the original sender with the COD amount (`HandleMailTakeItem`).

3.  **Anti-Cheat & Spam Protection**:
    *   **Overflow Check**: `HandleSendMailCallback` checks for integer overflow when calculating the mail fee (`reqmoney < req->money`).
    *   **Trial Accounts**: Both sender and receiver are checked for trial restrictions.
    *   **Item Validation**: Conjured items, temporary items, and items in bank slots cannot be mailed. Bags with items inside are blocked.
    *   **Spam Limits**: `AccountMgr` tracks mail frequency per account. Exceeding limits triggers a log and blocks the mail.

4.  **Delivery Delay**: Text-only mails are delivered instantly. Mails with items or money have a configurable delay (`CONFIG_UINT32_MAIL_DELIVERY_DELAY`), defaulting to 1 hour in Classic WoW. This is enforced by checking `deliver_time` against the current time in `HandleGetMailList` and other take/read handlers.

5.  **Mail State Management**: Mails have states like `MAIL_STATE_DELETED` and `MAIL_STATE_CHANGED`. Deleted mails are filtered out of the list but remain in memory until cleanup. COD mails cannot be deleted, only returned or taken.

6.  **Direct SQL Usage**: Unlike some other parts of the engine, this handler uses direct `PExecute` calls for deleting mail records and updating item ownership, rather than relying solely on higher-level ORM-like abstractions. This requires careful transaction management (`BeginTransaction`/`CommitTransaction`) to ensure consistency.

7.  **GM Restrictions**: Game Masters may be restricted from sending money or items if `CONFIG_BOOL_GM_ALLOW_TRADES` is disabled. They are also logged separately if `CONFIG_BOOL_GM_LOG_TRADE` is enabled.

## Member Reference

**SendMailResult**: Constructs and sends the `SMSG_SEND_MAIL_RESULT` packet to the client, reporting the outcome of a mail action (send, take, delete, etc.) with specific error codes and optional item details.

**SendNewMail**: Sends the `SMSG_RECEIVED_MAIL` packet to notify the client that new mail has arrived in their mailbox.

**CheckMailBox**: Validates that the player can interact with the specified mailbox GameObject, returning false and logging a debug message if the interaction is invalid.

**AsyncMailSendRequest**: Constructor for the helper class that holds state (sender, receiver, items, money, etc.) during the asynchronous mail sending process.

**Callback**: Method of `AsyncMailSendRequest` executed after the asynchronous mailbox size query completes. It verifies the sender's session, retrieves the mailbox count, and invokes `HandleSendMailCallback` to finalize the mail send.

**HandleSendMail**: Entry point for the `CMSG_SEND_MAIL` opcode. Performs initial validation (mailbox, trial accounts, length limits, COD limits, GM restrictions, recipient resolution) and initiates the asynchronous mail send process by creating an `AsyncMailSendRequest` and calling `HandleSendMailRequest`.

**HandleSendMailRequest**: Continues mail send validation. If the recipient is online, it checks their trial status and mailbox size synchronously. If offline, it issues an asynchronous database query to count their existing mails, triggering `AsyncMailSendRequest::Callback` upon completion.

**HandleSendMailCallback**: Finalizes the mail send. Validates funds, recipient mailbox capacity, faction rules, and item eligibility. Deducts fees, updates item ownership in the database, constructs the `MailDraft`, applies delivery delays, and persists the sender's inventory and gold changes.

**HandleMailMarkAsRead**: Handles `CMSG_MAIL_MARK_AS_READ`. Marks the specified mail as read, updates the unread count, adjusts expiration time if necessary, and notifies the client.

**HandleMailDelete**: Handles `CMSG_MAIL_DELETE`. Marks the specified mail as deleted in memory and notifies the client. Prevents deletion of COD mails.

**HandleMailReturnToSender**: Handles `CMSG_MAIL_RETURN_TO_SENDER`. Deletes the mail and its items from the database, removes them from memory, and sends a new mail containing the original contents back to the sender.

**HandleMailTakeItem**: Handles `CMSG_TAKE_MAIL_ITEM`. Validates inventory space and COD payment. Moves the item from mail to inventory, processes COD payments by sending a new mail to the sender, and updates the database.

**HandleMailTakeMoney**: Handles `CMSG_TAKE_MAIL_MONEY`. Validates gold cap, adds the money to the player's gold, logs the transaction, and updates the database.

**HandleGetMailList**: Handles `CMSG_GET_MAIL_LIST`. Iterates through the player's mail list, filters out invalid mails, constructs the `SMSG_MAIL_LIST_RESULT` packet with detailed item information, and sends it to the client.

**HandleItemTextQuery**: Handles `CMSG_ITEM_TEXT_QUERY`. Retrieves and sends the text content associated with a given `itemTextId` to the client.

**HandleMailCreateTextItem**: Handles `CMSG_MAIL_CREATE_TEXT_ITEM`. Creates a physical item representing the mail's body text, stores it in the player's inventory, and marks the mail as copied.

**HandleQueryNextMailTime**: Handles `MSG_QUERY_NEXT_MAIL_TIME`. Sends a response indicating whether the player has unread mail (0.0) or not (-86400.0).

---

<!-- machine-true, projected from graph.json -->

## Map — WorldSession.MailHandler

*Source:* MailHandler.cpp, WorldSession.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| SendMailResult | method | ByteBuffer/operator<<#10, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | ChatHandler.DebugCommands/HandleDebugSendMailErrorCommand | — |
| SendNewMail | method | ByteBuffer/operator<<#10, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | MasterPlayer.Main/AddNewMailDeliverTime, MasterPlayer.Main/Update | — |
| CheckMailBox | method | Log.Main/Out, ObjectGuid/GetString, Player.Main/GetGameObjectIfCanInteractWith, WorldSession.Main/GetPlayer | — | — |
| AsyncMailSendRequest | ctor | — | — | — |
| Callback | method | Field/GetUInt32, Object/GetObjectGuid, Object/IsInWorld, ObjectGuid/operator!=, QueryResult/Fetch, World/FindSession, WorldSession.Main/GetPlayer | — | — |
| HandleSendMail | method | Log.Main/Out, MasterPlayer.Main/GetGuidStr, MasterPlayer.Main/GetObjectGuid, ObjectGuid/IsEmpty, ObjectGuid/operator!, ObjectGuid/operator==, ObjectMgr/GetPlayer, ObjectMgr/GetPlayerGuidByName, ObjectMgr/normalizePlayerName, World/getConfig, World/getConfig#4, WorldSession.Main/GetAccountId, WorldSession.Main/GetAccountMaxLevel, WorldSession.Main/GetMasterPlayer, WorldSession.Main/GetSecurity, WorldSession.Main/HasTrialRestrictions, WorldSession.Main/ProcessAnticheatAction | — | — |
| HandleSendMailRequest | method | Errors/PrintStacktraceAndThrow, MasterPlayer.Main/GetMailSize, MasterPlayer.Main/GetTeam, ObjectGuid/GetCounter, ObjectMgr/GetPlayerTeamByGUID, Player.Main/GetSession, WorldSession.Main/GetMasterPlayer, WorldSession.Main/GetSecurity, WorldSession.Main/HasTrialRestrictions | — | mail |
| HandleSendMailCallback | method | AccountMgr/CanMail, AccountMgr/GetAccountPersistentData, AccountMgr/HasTrialRestrictions, AccountMgr/JustMailed, ChatHandler.Chat/ChatHandler#3, ChatHandler.Chat/playerLink, Database/BeginTransaction, Database/CommitTransaction, Database/PExecute#2, Errors/PrintStacktraceAndThrow, game_Mail_Mail/AddItem, game_Mail_Mail/MailDraft, game_Mail_Mail/MailReceiver#2, game_Mail_Mail/MailSender#4, game_Mail_Mail/SendMailTo, game_Objects_Item/CanBeTraded, game_Objects_Item/DeleteFromInventoryDB, game_Objects_Item/GetBagSlot, game_Objects_Item/GetCount, game_Objects_Item/GetPos, game_Objects_Item/GetProto, game_Objects_Item/GetSlot, game_Objects_Item/SaveToDB, MailDraft/SetCOD, MailDraft/SetMoney, MasterPlayer.Main/GetGUIDLow, MasterPlayer.Main/GetName, MasterPlayer.Main/GetTeam, Object/GetEntry, Object/GetGUIDLow, Object/GetUInt32Value, Object/IsInWorld, ObjectGuid/GetCounter, ObjectGuid/GetString, ObjectGuid/operator!, ObjectMgr/GetPlayerAccountIdByGUID, Player.Main/GetItemByGuid, Player.Main/GetMoney, Player.Main/IsBankPos#2, Player.Main/ModifyMoney, Player.Main/MoveItemFromInventory, Player.Main/Player#3, Player.Main/SaveInventoryAndGoldToDB, Unit.Main/GetLevel, World/getConfig, World/getConfig#4, World/InsertLog, World/LogTransaction, WorldSession.Main/GetAccountId, WorldSession.Main/GetMasterPlayer, WorldSession.Main/GetPlayer, WorldSession.Main/GetPlayerName, WorldSession.Main/GetSecurity, WorldSession.Main/ProcessAnticheatAction | — | item_instance |
| HandleMailMarkAsRead | method | Errors/PrintStacktraceAndThrow, MasterPlayer.Main/DecreaseUnreadMailsCount, MasterPlayer.Main/GetMail, MasterPlayer.Main/MarkMailsUpdated, WorldSession.Main/GetMasterPlayer, WorldSession.Main/ProcessAnticheatAction | — | — |
| HandleMailDelete | method | Errors/PrintStacktraceAndThrow, MasterPlayer.Main/GetMail, MasterPlayer.Main/MarkMailsUpdated, WorldSession.Main/GetMasterPlayer | — | — |
| HandleMailReturnToSender | method | Database/BeginTransaction, Database/CommitTransaction, Database/PExecute#2, Errors/PrintStacktraceAndThrow, game_Mail_Mail/AddItem, game_Mail_Mail/SendReturnToSender, game_Mail_Mail/SetSubjectAndBodyId, Mail/HasItems, MailDraft/MailDraft, MailDraft/SetMailTemplate, MailDraft/SetMoney, MasterPlayer.Main/GetGUIDLow, MasterPlayer.Main/GetMail, MasterPlayer.Main/GetMItem, MasterPlayer.Main/RemoveMail, MasterPlayer.Main/RemoveMItem, ObjectGuid/ObjectGuid#2, WorldSession.Main/GetAccountId, WorldSession.Main/GetMasterPlayer | — | mail, mail_items |
| HandleMailTakeItem | method | Database/BeginTransaction, Database/CommitTransaction, Errors/PrintStacktraceAndThrow, game_Mail_Mail/MailReceiver#2, game_Mail_Mail/MailSender#4, game_Mail_Mail/SendMailTo, game_Objects_Item/GetCount, game_Objects_Item/GetProto, game_Objects_Item/SetState, Mail/HasItems, Mail/RemoveItem, MailDraft/MailDraft#2, MailDraft/SetMoney, MasterPlayer.Main/GetMail, MasterPlayer.Main/GetMItem, MasterPlayer.Main/MarkMailsUpdated, MasterPlayer.Main/RemoveMItem, MasterPlayer.Main/SaveMails, Object/GetEntry, Object/GetGUIDLow, ObjectGuid/GetCounter, ObjectGuid/ObjectGuid#2, ObjectMgr/GetMangosStringForDBCLocale, ObjectMgr/GetPlayer, ObjectMgr/GetPlayerAccountIdByGUID, ObjectMgr/GetPlayerNameByGUID, Player.Main/CanStoreItem, Player.Main/GetMoney, Player.Main/GetName, Player.Main/GetSession, Player.Main/ModifyMoney, Player.Main/MoveItemToInventory, Player.Main/Player#3, Player.Main/SaveInventoryAndGoldToDB, World/getConfig, World/LogTransaction, WorldSession.Main/GetAccountId, WorldSession.Main/GetMasterPlayer, WorldSession.Main/GetPlayer, WorldSession.Main/GetPlayerName, WorldSession.Main/GetSecurity | — | — |
| HandleMailTakeMoney | method | Database/BeginTransaction, Database/CommitTransaction, Errors/PrintStacktraceAndThrow, MasterPlayer.Main/GetMail, MasterPlayer.Main/MarkMailsUpdated, MasterPlayer.Main/SaveMails, Object/GetGUIDLow, ObjectGuid/ObjectGuid#2, Player.Main/GetMaxMoney, Player.Main/GetMoney, Player.Main/LogModifyMoney, Player.Main/SaveGoldToDB, WorldSession.Main/GetMasterPlayer, WorldSession.Main/GetPlayer | — | — |
| HandleGetMailList | method | ByteBuffer/operator<<, ByteBuffer/operator<<#10, ByteBuffer/operator<<#7, ByteBuffer/operator<<#9, Errors/PrintStacktraceAndThrow, game_Objects_Item/GetCount, game_Objects_Item/GetEnchantmentId, game_Objects_Item/GetItemRandomPropertyId, game_Objects_Item/GetItemSuffixFactor, game_Objects_Item/GetSpellCharges, MasterPlayer.Main/GetMailBegin, MasterPlayer.Main/GetMailEnd, MasterPlayer.Main/GetMailSize, MasterPlayer.Main/GetMItem, MasterPlayer.Main/UpdateNextMailTimeAndUnreads, Object/GetEntry, Object/GetUInt32Value, ObjectGuid/ObjectGuid#2, ObjectGuid/operator<<, WorldPacket/WorldPacket#4, WorldSession.Main/GetMasterPlayer, WorldSession.Main/SendPacket | — | — |
| HandleItemTextQuery | method | ByteBuffer/operator<<, ByteBuffer/operator<<#10, ObjectMgr/GetItemText, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | — | — |
| HandleMailCreateTextItem | method | Errors/PrintStacktraceAndThrow, game_Objects_Item/Create, game_Objects_Item/Item, MasterPlayer.Main/GetMail, MasterPlayer.Main/GetObjectGuid, MasterPlayer.Main/MarkMailsUpdated, Object/SetGuidValue, ObjectGuid/ObjectGuid#2, ObjectMgr/GenerateItemLowGuid, Player.Main/CanStoreItem, Player.Main/StoreItem, WorldObject.Object/SetUInt32Value, WorldSession.Main/GetMasterPlayer | — | — |
| HandleQueryNextMailTime | method | ByteBuffer/operator<<#9, Errors/PrintStacktraceAndThrow, MasterPlayer.Main/HasUnreadMail, WorldPacket/WorldPacket#4, WorldSession.Main/GetMasterPlayer, WorldSession.Main/SendPacket | — | — |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `item_instance`: guid int(10) unsigned PK, item_id mediumint(8) unsigned, owner_guid int(10) unsigned, creator_guid int(10) unsigned, gift_creator_guid int(10) unsigned, count int(10) unsigned, duration int(10), charges tinytext?, flags mediumint(8) unsigned, enchantments text, random_property_id smallint(5), durability smallint(5) unsigned, text int(10) unsigned, generated_loot tinyint(4)?
- `mail`: id int(11) unsigned PK, message_type tinyint(3) unsigned, stationery tinyint(3), mail_template_id mediumint(8) unsigned, sender_guid int(11) unsigned, receiver_guid int(11) unsigned, subject longtext?, item_text_id int(11) unsigned, has_items tinyint(3) unsigned, expire_time bigint(40), deliver_time bigint(40), money int(11) unsigned, cod int(11) unsigned, checked tinyint(3) unsigned
- `mail_items`: mail_id int(11) unsigned PK, item_guid int(11) unsigned PK, item_id int(11) unsigned, receiver_guid int(11) unsigned

*`?` = nullable, `PK` = primary key column.*


---

<!-- verify: boundary-bleed | foreign: process, WorldSession -->
