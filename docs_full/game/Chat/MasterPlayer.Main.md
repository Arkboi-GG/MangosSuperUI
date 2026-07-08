# MasterPlayer.Main

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# MasterPlayer

**MasterPlayer** is a lightweight, session-bound data structure that aggregates player-specific state required by subsystems outside the core `Player` class hierarchy. It acts as a facade for chat, social, mail, action bar, and guild/GM status information, providing fast accessors and managing the persistence of these specific domains to the database.

Unlike the heavy `Player` object, which represents the entity in the world simulation, `MasterPlayer` is designed to be created during login or character creation and destroyed upon logout or deletion. It holds references to the `WorldSession` and manages the lifecycle of `Mail` objects, `ActionButton` configurations, and `PlayerSocial` relationships. Its primary responsibility is to decouple the transient network session and persistent character data from the complex simulation logic of the `Player` class, allowing modules like `AccountMgr`, `SocialMgr`, and `game_Mail_Mail` to interact with player state without requiring a full `Player` instance or risking circular dependencies.

## Purpose & Responsibilities

The unit serves three main architectural roles:

1.  **State Aggregation:** It caches frequently accessed attributes (name, GUID, guild ID, rank, team, AFK/DND status) derived from the `Player` object via `LoadPlayer`. This allows other systems to query player metadata efficiently without traversing the deep `Player` inheritance tree.
2.  **Subsystem Facade:** It provides the interface for the Mail, Action Bar, and Social systems. It owns the `PlayerMails` deque, the `ActionButtonList` map, and the `PlayerSocial` pointer. It handles the loading, saving, and memory management of these components.
3.  **Persistence Bridge:** It coordinates the saving of its managed subsystems to the database. `SaveToDB` wraps `SaveActions` and `SaveMails` in a transaction, ensuring consistency between the action bar configuration and the mail state.

## Member-by-Member Behavior

### Initialization and Lifecycle

*   **MasterPlayer**: Constructs the object, storing the provided `WorldSession` pointer.
*   **~MasterPlayer**: Cleans up resources. It calls `CleanupChannels` (from `MasterPlayer.Chat`) to leave any joined channels, removes the object from the global accessor via `ObjectAccessor/RemoveObject#2`, and manually deletes all `Mail` objects in `m_mail` and all `Item` pointers in `mMitems`. This manual deletion is critical because `MasterPlayer` owns these heap-allocated objects.
*   **Create**: Initializes a new player's action bar. It retrieves the default action configuration for the given race and class from `ObjectMgr/GetPlayerInfo` and populates the local `m_actionButtons` map using `addActionButton`.
*   **LoadPlayer**: Populates the cached metadata fields (`guid`, `name`, `zoneId`, `areaId`, `raceId`, `classId`, `level`, `guildId`, `m_team`, `m_chatTag`, `m_gmInvisibilityLevel`, `guildRank`, `m_ExtraFlags`) from an existing `Player` instance. This is typically called after a player logs in to sync the `MasterPlayer` with the live `Player` object.
*   **SetSession**: Updates the internal `m_session` pointer. Called by `WorldSession.CharacterHandler/HandlePlayerLogin` when the session is established.

### Accessors and Metadata

These methods provide read-only access to the cached player data. They are widely called by various managers (`AccountMgr`, `SocialMgr`, `game_Guild_Guild`, etc.) to identify the player or determine their status.

*   **GetSession**: Returns the associated `WorldSession`. Used by chat, social, and mail handlers to send packets or verify permissions.
*   **GetGUIDLow**: Returns the low part of the player's GUID. Used for database lookups and identification in mail and social systems.
*   **GetObjectGuid**: Returns the full `ObjectGuid`. Used for channel cleanup, whispers, friend lists, and mail handling.
*   **GetGuidStr**: Returns the GUID as a string. Used specifically in mail sending logic.
*   **GetSocial**: Returns the `PlayerSocial` pointer. Used by guild broadcasts, friend list operations, and social manager functions.
*   **SetSocial**: Sets the `PlayerSocial` pointer. Called during login (`WorldSession.Main/LogoutPlayer` clears it, `PlayerBotAI/SpawnNewPlayer` sets it) and crash recovery (`Map.Main/CrashUnload`).
*   **GetActionButtons**: Returns the map of action buttons. Used by `Player.Main/ConvertSpell`.
*   **GetGuildId**, **GetRank**: Return guild membership details. Used by guild broadcast systems.
*   **GetTeam**: Returns the faction team (Alliance/Horde). Used by chat, social, and mail systems to filter interactions.
*   **GetName**: Returns the player's name. Used extensively in social, mail, and chat contexts.
*   **GetZoneId**, **GetAreaId**: Return location IDs. Used by social info and whisper scoring.
*   **GetClass**, **GetRace**, **GetLevel**: Return character attributes. Used by social info and chat commands.
*   **GetChatTag**: Returns the chat tag (0=normal, 1=AFK, 2=DND). Used by whispers and guild broadcasts.
*   **IsAFK**, **IsDND**: Convenience wrappers around `GetChatTag`. Used by whispers and social info.
*   **IsGameMaster**: Checks if the player has the GM flag set in `m_ExtraFlags`. Used by chat handlers.
*   **GetGMInvisibilityRank**: Returns the GM invisibility level.
*   **IsVisibleGloballyFor**: Determines if this player is visible to another `MasterPlayer`. It checks if the viewer is a GM with sufficient security level to override invisibility. It relies on `WorldSession.Main/GetSecurity` for the viewer's privilege level.

### Mail System

The mail system is a major component of this unit. `MasterPlayer` owns the `Mail` objects and their associated items.

*   **Update**: Checks if any pending mail has reached its delivery time. If so, it triggers `WorldSession.MailHandler/SendNewMail` to notify the client, increments the unread count, and resets the next delivery timer.
*   **AddMail**: Adds a new `Mail` object to the front of the `m_mail` deque. Called by `game_Mail_Mail/SendMailTo`.
*   **GetMailSize**: Returns the number of mail messages. Used by mail list handlers.
*   **MarkMailsUpdated**: Sets a flag indicating that mail changes have occurred and need to be saved. Called by various mail handlers (delete, take item, mark read, etc.).
*   **HasUnreadMail**: Returns true if `unReadMails > 0`. Used by the "next mail time" query handler.
*   **DecreaseUnreadMailsCount**: Decrements the unread counter. Called when a mail is marked as read.
*   **GetMailBegin**, **GetMailEnd**: Return iterators for the mail list. Used by the mail list handler.
*   **AddMItem**: Adds an `Item` pointer to the `mMitems` map, keyed by the item's GUID low. Called when preparing template items or sending mail.
*   **GetMItem**: Retrieves an item from the `mMitems` map. Used by mail list and take-item handlers.
*   **SaveMails**: Persists mail changes to the database. It iterates through `m_mail`:
    *   If a mail is `MAIL_STATE_CHANGED`, it updates the `mail` table and deletes any removed items from `mail_items`.
    *   If a mail is `MAIL_STATE_DELETED`, it deletes the associated items from `item_instance` and `item_text` (if custom), then deletes the mail record from `mail` and `mail_items`.
    *   Finally, it deallocates the deleted `Mail` objects from memory.
    *   This method touches `item_instance`, `item_text`, `mail`, and `mail_items` tables.
*   **RemoveMItem**: Removes an item from the `mMitems` map. Called when returning mail or taking items.
*   **RemoveMail**: Removes a `Mail` object from the `m_mail` deque by ID. Note: It does *not* delete the `Mail` object itself, as `Player::removeMail()` (in another unit) handles the cleanup when returning mail to sender.
*   **UpdateNextMailTimeAndUnreads**: Recalculates the `m_nextMailDelivereTime` (the earliest time any pending mail arrives) and the `unReadMails` count. This is called on login and when opening the mailbox.
*   **AddNewMailDeliverTime**: Schedules a new mail delivery. If the mail is ready now, it immediately notifies the client. Otherwise, it updates the next delivery timer if this mail arrives sooner than previously scheduled.
*   **LoadMailedItems**: Loads items associated with mail from the database. It iterates through the query result, creates `Item` objects, loads their data via `game_Objects_Item/LoadFromDB`, and adds them to the `mMitems` map. If an item prototype is missing or the item fails to load, it logs an error and cleans up the database records, inserting the lost item into `character_deleted_items`.
*   **LoadMails**: Loads mail messages from the database. It creates `Mail` objects, populates their fields from the query result, and adds them to `m_mail`. If a mail has a template but no items, it calls `game_Mail_Mail/prepareTemplateItems` to generate them.
*   **GetMail**: Finds a `Mail` object by ID in the `m_mail` deque.

### Action Button System

Manages the player's action bar configuration.

*   **SendInitialActionButtons**: Sends the current action bar state to the client via `SMSG_ACTION_BUTTONS`. It iterates through all possible button slots, packing the action data or sending zero if empty/deleted.
*   **addActionButton**: Adds or updates an action button. It validates the data via `Player.Main/IsActionButtonDataValid`, then sets the action and type on the `ActionButton` object.
*   **removeActionButton**: Marks an action button for deletion. If the button is new (unsaved), it erases it immediately. If saved, it marks it as `ACTIONBUTTON_DELETED` so it can be removed from the database on the next save.
*   **LoadActions**: Loads action buttons from the database. It iterates through the result, adding each button and marking it as unchanged.
*   **SaveActions**: Persists action button changes. It iterates through `m_actionButtons`:
    *   `ACTIONBUTTON_NEW`: Inserts into `character_action`.
    *   `ACTIONBUTTON_CHANGED`: Updates `character_action`.
    *   `ACTIONBUTTON_DELETED`: Deletes from `character_action` and erases from the map.
    *   Unchanged buttons are skipped.

### Social and Chat Integration

*   **LoadSocial**: Loads the social list from the database via `SocialMgr/LoadFromDB` and sets the master player pointer on the resulting `PlayerSocial` object.
*   **IsAcceptTickets**, **SetAcceptTicket**: Manage the GM ticket acceptance flag in `m_ExtraFlags`.
*   **IsAcceptWhispers**, **AcceptsWhispersFrom**, **AddAllowedWhisperer**, **ClearAllowedWhisperers**, **SetAcceptWhispers**: Manage whisper permissions. `AcceptsWhispersFrom` checks if general whispers are accepted or if the specific whisperer is in the allowed list. These are used by chat handlers and the whisper function in `MasterPlayer.Chat`.

## Cross-Unit Boundaries

*   **Player.Main**: `MasterPlayer` depends heavily on `Player.Main` for initial data population (`LoadPlayer`) and validation (`IsActionButtonDataValid`). `Player.Main` calls `MasterPlayer` to save new players (`SaveNewPlayer`) and retrieve social/action data.
*   **WorldSession**: The `WorldSession` is the owner of the `MasterPlayer` instance. `WorldSession.CharacterHandler` constructs it, sets the session, and loads data. `WorldSession.Main` calls `Update` and `SaveToDB` during the game loop and logout. Various `WorldSession` handlers (Mail, Chat, Misc) call accessors to get player state for processing opcodes.
*   **ObjectMgr**: `Create` uses `ObjectMgr/GetPlayerInfo` to fetch default action bars. `LoadMailedItems` uses `ObjectMgr/GetItemPrototype` to validate items.
*   **SocialMgr**: `LoadSocial` delegates to `SocialMgr/LoadFromDB`. `SocialMgr` calls `MasterPlayer` accessors to broadcast friend status and get player info for friends.
*   **game_Mail_Mail**: `LoadMails` calls `game_Mail_Mail/prepareTemplateItems` to generate items for templated mail. `game_Mail_Mail` calls `MasterPlayer` to add items and send mail.
*   **game_Objects_Item**: `LoadMailedItems` creates and loads `Item` objects. `SaveMails` deletes items from the database.
*   **ObjectAccessor**: The destructor calls `ObjectAccessor/RemoveObject#2` to unregister the player from global lookups.
*   **Database**: `SaveToDB`, `SaveMails`, `SaveActions`, `LoadMailedItems`, and `LoadMails` all interact directly with the database layer to persist and retrieve state.

## Data Model

`MasterPlayer` interacts with the following database tables:

*   **`character_action`**: Stores the player's action bar configuration. Columns: `guid` (PK), `button` (PK), `action`, `type`.
*   **`mail`**: Stores mail messages. Columns: `id` (PK), `message_type`, `stationery`, `mail_template_id`, `sender_guid`, `receiver_guid`, `subject`, `item_text_id`, `has_items`, `expire_time`, `deliver_time`, `money`, `cod`, `checked`.
*   **`mail_items`**: Links items to mail messages. Columns: `mail_id` (PK), `item_guid` (PK), `item_id`, `receiver_guid`.
*   **`item_instance`**: Stores item data. Columns: `guid` (PK), `item_id`, `owner_guid`, `creator_guid`, `gift_creator_guid`, `count`, `duration`, `charges`, `flags`, `enchantments`, `random_property_id`, `durability`, `text`, `generated_loot`.
*   **`item_text`**: Stores custom text for items. Columns: `id` (PK), `text`.
*   **`character_deleted_items`**: Logs items that were deleted due to errors (e.g., missing prototypes). Columns: `id` (PK), `player_guid`, `item_id`, `stack_count`.

## Notable Implementation Details

*   **Manual Memory Management**: The destructor explicitly deletes `Mail` and `Item` objects. This is unusual in modern C++ but necessary here because `MasterPlayer` takes ownership of these objects from other systems. Failure to do so would cause memory leaks.
*   **Mail State Machine**: The `SaveMails` function implements a state machine for mail persistence. Mails are marked as `MAIL_STATE_CHANGED` or `MAIL_STATE_DELETED` by other parts of the system. `SaveMails` processes these states, performs the SQL operations, and then deallocates the deleted objects. This ensures that only changed data is written to the database.
*   **Error Handling in Mail Loading**: `LoadMailedItems` has robust error handling. If an item prototype is missing or the item fails to load, it logs an error, inserts the item into `character_deleted_items` for auditing, and cleans up the database records. This prevents corrupted data from crashing the server or causing infinite loops.
*   **Action Button Validation**: `addActionButton` validates input via `Player.Main/IsActionButtonDataValid` before modifying state. This prevents invalid actions from being added to the bar.
*   **GM Visibility Logic**: `IsVisibleGloballyFor` implements a simple visibility check based on GM security levels. It assumes that higher-level GMs can see lower-level GMs who are invisible, and that players are always visible to GMs. This logic is centralized here to avoid duplication in social and chat systems.
*   **Static SQL Statements**: `SaveMails` and `SaveActions` use static `SqlStatementID` objects to cache prepared statements. This improves performance by avoiding repeated statement preparation. However, care must be taken to ensure thread safety if these methods are called concurrently, though in this single-threaded context, it is likely safe.

## Member Reference

**MasterPlayer**: Constructor that initializes the `m_session` pointer.
**~MasterPlayer**: Destructor that cleans up channels, removes the object from the accessor, and deletes owned `Mail` and `Item` objects.
**Create**: Initializes the action bar with default entries for the given race and class.
**GetSession**: Returns the associated `WorldSession`.
**SetSession**: Sets the associated `WorldSession`.
**GetGUIDLow**: Returns the low part of the player's GUID.
**GetObjectGuid**: Returns the full `ObjectGuid`.
**GetGuidStr**: Returns the GUID as a string.
**LoadPlayer**: Copies metadata from a `Player` instance to the `MasterPlayer` cache.
**GetSocial**: Returns the `PlayerSocial` pointer.
**SetSocial**: Sets the `PlayerSocial` pointer.
**GetActionButtons**: Returns the map of action buttons.
**SaveToDB**: Saves actions and mails within a database transaction.
**Update**: Checks for pending mail deliveries and notifies the client if any arrive.
**AddMail**: Adds a new `Mail` object to the mail list.
**GetMailSize**: Returns the number of mail messages.
**MarkMailsUpdated**: Flags that mail changes need to be saved.
**HasUnreadMail**: Returns true if there are unread mails.
**DecreaseUnreadMailsCount**: Decrements the unread mail counter.
**GetMailBegin**: Returns an iterator to the beginning of the mail list.
**GetMailEnd**: Returns an iterator to the end of the mail list.
**AddMItem**: Adds an item to the mailed items map.
**GetMItem**: Retrieves an item from the mailed items map.
**SaveMails**: Persists mail changes to the database and deallocates deleted mails.
**RemoveMItem**: Removes an item from the mailed items map.
**GetGuildId**: Returns the player's guild ID.
**GetRank**: Returns the player's guild rank.
**IsAcceptTickets**: Checks if the player accepts GM tickets.
**SetAcceptTicket**: Sets the GM ticket acceptance flag.
**IsAcceptWhispers**: Checks if the player accepts whispers.
**AcceptsWhispersFrom**: Checks if the player accepts whispers from a specific sender.
**AddAllowedWhisperer**: Adds a sender to the allowed whisperers list.
**ClearAllowedWhisperers**: Clears the allowed whisperers list.
**SetAcceptWhispers**: Sets the general whisper acceptance flag.
**IsGameMaster**: Checks if the player is a GM.
**GetGMInvisibilityRank**: Returns the GM invisibility level.
**GetTeam**: Returns the player's faction team.
**GetName**: Returns the player's name.
**GetZoneId**: Returns the player's zone ID.
**GetAreaId**: Returns the player's area ID.
**GetClass**: Returns the player's class.
**GetRace**: Returns the player's race.
**GetLevel**: Returns the player's level.
**IsAFK**: Checks if the player is AFK.
**IsDND**: Checks if the player is DND.
**GetChatTag**: Returns the chat tag (AFK/DND status).
**RemoveMail**: Removes a mail from the list by ID.
**UpdateNextMailTimeAndUnreads**: Recalculates the next mail delivery time and unread count.
**AddNewMailDeliverTime**: Schedules a new mail delivery.
**LoadMailedItems**: Loads items associated with mail from the database.
**LoadMails**: Loads mail messages from the database.
**GetMail**: Finds a mail by ID.
**SendInitialActionButtons**: Sends the action bar state to the client.
**addActionButton**: Adds or updates an action button.
**removeActionButton**: Marks an action button for deletion.
**LoadActions**: Loads action buttons from the database.
**SaveActions**: Persists action button changes to the database.
**LoadSocial**: Loads the social list from the database.
**IsVisibleGloballyFor**: Determines if the player is visible to another player based on GM privileges.

---

<!-- machine-true, projected from graph.json -->

## Map — MasterPlayer.Main

*Source:* MasterPlayer.cpp, MasterPlayer.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| MasterPlayer | ctor | — | Player.Main/SaveNewPlayer, PlayerBotAI/SpawnNewPlayer, WorldSession.CharacterHandler/HandlePlayerLogin | — |
| ~MasterPlayer | dtor | MasterPlayer.Chat/CleanupChannels, ObjectAccessor/RemoveObject#2 | — | — |
| Create | method | Errors/PrintStacktraceAndThrow, ObjectMgr/GetPlayerInfo | Player.Main/SaveNewPlayer | — |
| GetSession | method | — | AccountMgr/CountWhispersTo, AccountMgr/GetWhisperScore, game_Chat_Channel/List, game_Guild_Guild/BroadcastToGuild, game_Guild_Guild/BroadcastToOfficers, MasterPlayer.Chat/UpdateSpeakTime, MasterPlayer.Chat/Whisper, SocialMgr/BroadcastToFriendListers, SocialMgr/GetFriendInfo, SocialMgr/SendFriendList, SocialMgr/SendFriendStatus, SocialMgr/SendIgnoreList, WorldSession.CharacterHandler/HandlePlayerLogin, WorldSession.ChatHandler/HandleChatMessageOpcode | — |
| SetSession | method | — | WorldSession.CharacterHandler/HandlePlayerLogin | — |
| GetGUIDLow | method | — | AccountMgr/CountWhispersTo, AccountMgr/WhisperedBy, game_Mail_Mail/prepareTemplateItems, SocialMgr/BroadcastToFriendListers, WorldSession.MailHandler/HandleMailReturnToSender, WorldSession.MailHandler/HandleSendMailCallback, WorldSession.Main/LogoutPlayer | — |
| GetObjectGuid | method | — | game_Guild_Guild/BroadcastToGuild, game_Guild_Guild/BroadcastToOfficers, MasterPlayer.Chat/CleanupChannels, MasterPlayer.Chat/Whisper, SocialMgr/GetFriendInfo, WorldSession.CharacterHandler/HandlePlayerLogin, WorldSession.ChatHandler/HandleChatMessageOpcode, WorldSession.MailHandler/HandleMailCreateTextItem, WorldSession.MailHandler/HandleSendMail, WorldSession.Main/LogoutPlayer, WorldSession.MiscHandler/HandleAddFriendOpcode, WorldSession.MiscHandler/HandleAddIgnoreOpcode | — |
| GetGuidStr | method | — | WorldSession.MailHandler/HandleSendMail | — |
| LoadPlayer | method | Object/GetObjectGuid, Player.Main/GetCachedAreaId, Player.Main/GetCachedZoneId, Player.Main/GetChatTag, Player.Main/GetExtraFlags, Player.Main/GetGMInvisibilityLevel, Player.Main/GetGuildId, Player.Main/GetName, Player.Main/GetRank, Player.Main/GetTeam, Unit.Main/GetClass, Unit.Main/GetLevel, Unit.Main/GetRace | PlayerBotAI/SpawnNewPlayer, WorldSession.CharacterHandler/HandlePlayerLogin, WorldSession.Main/Update | — |
| GetSocial | method | — | game_Guild_Guild/BroadcastToGuild, game_Guild_Guild/BroadcastToOfficers, Player.Main/FindSocial, Player.Main/GetSocial, SocialMgr/GetFriendInfo, WorldSession.Main/LogoutPlayer, WorldSession.MiscHandler/HandleAddFriendOpcode, WorldSession.MiscHandler/HandleAddIgnoreOpcode, WorldSession.MiscHandler/HandleDelFriendOpcode, WorldSession.MiscHandler/HandleDelIgnoreOpcode, WorldSession.MiscHandler/HandleFriendListOpcode | — |
| SetSocial | method | — | Map.Main/CrashUnload, PlayerBotAI/SpawnNewPlayer, WorldSession.Main/LogoutPlayer | — |
| GetActionButtons | method | — | Player.Main/ConvertSpell | — |
| SaveToDB | method | Database/BeginTransaction, Database/CommitTransaction | Player.Main/SaveNewPlayer, WorldSession.Main/LogoutPlayer | — |
| Update | method | WorldSession.MailHandler/SendNewMail | WorldSession.Main/Update | — |
| AddMail | method | — | game_Mail_Mail/SendMailTo | — |
| GetMailSize | method | — | WorldSession.MailHandler/HandleGetMailList, WorldSession.MailHandler/HandleSendMailRequest | — |
| MarkMailsUpdated | method | — | WorldSession.MailHandler/HandleMailCreateTextItem, WorldSession.MailHandler/HandleMailDelete, WorldSession.MailHandler/HandleMailMarkAsRead, WorldSession.MailHandler/HandleMailTakeItem, WorldSession.MailHandler/HandleMailTakeMoney | — |
| HasUnreadMail | method | — | WorldSession.MailHandler/HandleQueryNextMailTime | — |
| DecreaseUnreadMailsCount | method | — | WorldSession.MailHandler/HandleMailMarkAsRead | — |
| GetMailBegin | method | — | WorldSession.MailHandler/HandleGetMailList | — |
| GetMailEnd | method | — | WorldSession.MailHandler/HandleGetMailList | — |
| AddMItem | method | Object/GetGUIDLow | game_Mail_Mail/prepareTemplateItems, game_Mail_Mail/SendMailTo | — |
| GetMItem | method | — | WorldSession.MailHandler/HandleGetMailList, WorldSession.MailHandler/HandleMailReturnToSender, WorldSession.MailHandler/HandleMailTakeItem | — |
| SaveMails | method | Database/CreateStatement, Mail/HasItems, SqlPreparedStatement/Execute#2, SqlPreparedStatement/operator=, SqlStatement/addUInt32, SqlStatement/addUInt64, SqlStatementID/SqlStatementID | WorldSession.MailHandler/HandleMailTakeItem, WorldSession.MailHandler/HandleMailTakeMoney | item_instance, item_text, mail, mail_items |
| RemoveMItem | method | — | WorldSession.MailHandler/HandleMailReturnToSender, WorldSession.MailHandler/HandleMailTakeItem | — |
| GetGuildId | method | — | WorldSession.ChatHandler/HandleChatMessageOpcode | — |
| GetRank | method | — | game_Guild_Guild/BroadcastToGuild, game_Guild_Guild/BroadcastToOfficers | — |
| IsAcceptTickets | method | — | — | — |
| SetAcceptTicket | method | — | — | — |
| IsAcceptWhispers | method | — | ChatHandler.CharacterCommands/HandleWhispersCommand, MasterPlayer.Chat/Whisper | — |
| AcceptsWhispersFrom | method | — | WorldSession.ChatHandler/HandleChatMessageOpcode | — |
| AddAllowedWhisperer | method | — | MasterPlayer.Chat/Whisper | — |
| ClearAllowedWhisperers | method | — | ChatHandler.CharacterCommands/HandleWhispersCommand, Player.Main/SetGMVisible | — |
| SetAcceptWhispers | method | — | — | — |
| IsGameMaster | method | — | WorldSession.ChatHandler/HandleChatMessageOpcode | — |
| GetGMInvisibilityRank | method | — | — | — |
| GetTeam | method | — | MasterPlayer.Chat/CleanupChannels, SocialMgr/BroadcastToFriendListers, SocialMgr/GetFriendInfo, WorldSession.ChatHandler/HandleChatMessageOpcode, WorldSession.MailHandler/HandleSendMailCallback, WorldSession.MailHandler/HandleSendMailRequest, WorldSession.MiscHandler/HandleAddFriendOpcode | — |
| GetName | method | — | game_Guild_Guild/BroadcastToGuild, game_Guild_Guild/BroadcastToOfficers, MasterPlayer.Chat/Whisper, ObjectAccessor/AddObject#2, ObjectAccessor/RemoveObject#2, SocialMgr/GetFriendInfo, WorldSession.MailHandler/HandleSendMailCallback, WorldSession.MiscHandler/HandleAddFriendOpcode, WorldSession.MiscHandler/HandleAddIgnoreOpcode | — |
| GetZoneId | method | — | SocialMgr/GetFriendInfo | — |
| GetAreaId | method | — | AccountMgr/GetWhisperScore | — |
| GetClass | method | — | SocialMgr/GetFriendInfo | — |
| GetRace | method | — | — | — |
| GetLevel | method | — | SocialMgr/GetFriendInfo, WorldSession.ChatHandler/HandleChatMessageOpcode | — |
| IsAFK | method | — | MasterPlayer.Chat/Whisper, SocialMgr/GetFriendInfo | — |
| IsDND | method | — | MasterPlayer.Chat/Whisper, SocialMgr/GetFriendInfo | — |
| GetChatTag | method | — | game_Guild_Guild/BroadcastToGuild, game_Guild_Guild/BroadcastToOfficers, MasterPlayer.Chat/Whisper | — |
| RemoveMail | method | — | WorldSession.MailHandler/HandleMailReturnToSender | — |
| UpdateNextMailTimeAndUnreads | method | — | WorldSession.CharacterHandler/HandlePlayerLogin, WorldSession.MailHandler/HandleGetMailList | — |
| AddNewMailDeliverTime | method | WorldSession.MailHandler/SendNewMail | game_Mail_Mail/SendMailTo | — |
| LoadMailedItems | method | Bag/NewItemOrBag, Database/PExecute#2, Field/GetBool, Field/GetUInt32, game_Objects_Item/FSetState, game_Objects_Item/LoadFromDB, game_Objects_Item/SaveToDB, game_Objects_Item/SetGeneratedLoot, Log.Main/Out, Mail/AddItem, ObjectMgr/GetItemPrototype, QueryResult/Fetch, QueryResult/NextRow | WorldSession.CharacterHandler/HandlePlayerLogin | character_deleted_items, item_instance, mail_items |
| LoadMails | method | Errors/PrintStacktraceAndThrow, Field/GetBool, Field/GetCppString, Field/GetInt16, Field/GetUInt32, Field/GetUInt64, Field/GetUInt8, game_Mail_Mail/prepareTemplateItems, Log.Main/Out, ObjectGuid/ObjectGuid#2, QueryResult/Fetch, QueryResult/NextRow, WorldSession.Main/GetPlayer | WorldSession.CharacterHandler/HandlePlayerLogin | — |
| GetMail | method | — | WorldSession.MailHandler/HandleMailCreateTextItem, WorldSession.MailHandler/HandleMailDelete, WorldSession.MailHandler/HandleMailMarkAsRead, WorldSession.MailHandler/HandleMailReturnToSender, WorldSession.MailHandler/HandleMailTakeItem, WorldSession.MailHandler/HandleMailTakeMoney | — |
| SendInitialActionButtons | method | ByteBuffer/operator<<#10, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | Player.Main/SendInitialPacketsBeforeAddToMap, WorldSession.CharacterHandler/HandlePlayerLogin | — |
| addActionButton | method | ActionButton/SetActionAndType, Player.Main/IsActionButtonDataValid | WorldSession.MiscHandler/HandleSetActionButtonOpcode | — |
| removeActionButton | method | — | WorldSession.MiscHandler/HandleSetActionButtonOpcode | — |
| LoadActions | method | Field/GetUInt32, Field/GetUInt8, QueryResult/Fetch, QueryResult/NextRow | WorldSession.CharacterHandler/HandlePlayerLogin | — |
| SaveActions | method | ActionButton/GetAction, ActionButton/GetType, Database/CreateStatement, SqlPreparedStatement/Execute#2, SqlStatement/addUInt32, SqlStatementID/SqlStatementID | — | character_action |
| LoadSocial | method | SocialMgr/LoadFromDB, SocialMgr/SetMasterPlayer | WorldSession.CharacterHandler/HandlePlayerLogin | — |
| IsVisibleGloballyFor | method | WorldSession.Main/GetSecurity | game_Chat_Channel/List, SocialMgr/BroadcastToFriendListers, SocialMgr/GetFriendInfo, WorldSession.MiscHandler/HandleAddFriendOpcode | — |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `character_action`: guid int(11) unsigned PK, button tinyint(3) unsigned PK, action int(11) unsigned, type tinyint(3) unsigned
- `character_deleted_items`: id int(11) unsigned PK, player_guid int(11) unsigned, item_id mediumint(8) unsigned, stack_count mediumint(8) unsigned
- `item_instance`: guid int(10) unsigned PK, item_id mediumint(8) unsigned, owner_guid int(10) unsigned, creator_guid int(10) unsigned, gift_creator_guid int(10) unsigned, count int(10) unsigned, duration int(10), charges tinytext?, flags mediumint(8) unsigned, enchantments text, random_property_id smallint(5), durability smallint(5) unsigned, text int(10) unsigned, generated_loot tinyint(4)?
- `item_text`: id int(11) unsigned PK, text longtext?
- `mail`: id int(11) unsigned PK, message_type tinyint(3) unsigned, stationery tinyint(3), mail_template_id mediumint(8) unsigned, sender_guid int(11) unsigned, receiver_guid int(11) unsigned, subject longtext?, item_text_id int(11) unsigned, has_items tinyint(3) unsigned, expire_time bigint(40), deliver_time bigint(40), money int(11) unsigned, cod int(11) unsigned, checked tinyint(3) unsigned
- `mail_items`: mail_id int(11) unsigned PK, item_guid int(11) unsigned PK, item_id int(11) unsigned, receiver_guid int(11) unsigned

*`?` = nullable, `PK` = primary key column.*

