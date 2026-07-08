# MasterPlayer — Class Overview

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# MasterPlayer

`MasterPlayer` is a lightweight, session-bound data structure that aggregates player-specific state required by subsystems outside the core `Player` class hierarchy. It acts as a facade for chat, social, mail, action bar, and guild/GM status information, providing fast accessors and managing the persistence of these specific domains to the database. Unlike the heavy `Player` object, which represents the entity in the world simulation, `MasterPlayer` is designed to be created during login or character creation and destroyed upon logout or deletion. It holds references to the `WorldSession` and manages the lifecycle of `Mail` objects, `ActionButton` configurations, and `PlayerSocial` relationships, decoupling transient network session and persistent character data from the complex simulation logic of the `Player` class.

## Class Structure

The class is implemented across two partials, each handling distinct aspects of player state:

*   **`MasterPlayer.Main`**: The core of the class. It manages the object's lifecycle (construction, destruction, initialization), caches frequently accessed metadata (name, GUID, guild, team, AFK/DND status) from the `Player` object, and serves as the primary interface for the Mail, Action Bar, and Social systems. It handles the loading and saving of these subsystems to the database and provides the accessor methods used by managers like `AccountMgr`, `SocialMgr`, and `game_Guild_Guild`.
*   **`MasterPlayer.Chat`**: Handles server-side logic for private messaging (whispers), status indicators (AFK/DND toggling), and channel membership management. It constructs chat packets, enforces anti-spam rate limiting for non-GM players, manages the allow-list for incoming whispers, and ensures clean disconnection from all joined channels upon player destruction. This unit operates entirely in memory.

## Collaboration and Flow

`MasterPlayer` sits between the high-level `WorldSession` (which owns the instance) and the various game managers (`SocialMgr`, `AccountMgr`, `ChannelMgr`).

1.  **Initialization**: During login (`WorldSession.CharacterHandler/HandlePlayerLogin`), a `MasterPlayer` is constructed. `MasterPlayer.Main` calls `LoadPlayer` to populate its cache from the live `Player` object, then loads persistent state from the database via `LoadActions`, `LoadMails`, `LoadMailedItems`, and `LoadSocial`.
2.  **Runtime Interaction**:
    *   **Chat**: When a player sends a message, `WorldSession.ChatHandler` calls `MasterPlayer.Chat/UpdateSpeakTime` for spam checking and `MasterPlayer.Chat/Whisper` for private messages. These methods rely on `MasterPlayer.Main` for identity data (name, GUID, session) and status flags (AFK/DND).
    *   **Mail**: The `MasterPlayer.Main` unit owns the `Mail` objects. `WorldSession.MailHandler` methods call `MasterPlayer.Main` accessors (`AddMail`, `GetMail`, `MarkMailsUpdated`) to manipulate the mail queue. `MasterPlayer.Main/Update` is called periodically by `WorldSession.Main/Update` to check for pending mail deliveries.
    *   **Social/Guild**: Managers like `SocialMgr` and `game_Guild_Guild` call `MasterPlayer.Main` accessors (`GetSocial`, `GetGuildId`, `GetTeam`) to broadcast information or verify visibility.
3.  **Persistence**: On logout or explicit save, `WorldSession.Main` calls `MasterPlayer.Main/SaveToDB`. This method wraps `SaveActions` and `SaveMails` in a database transaction, ensuring that action bar changes and mail state updates are committed atomically.
4.  **Cleanup**: Upon destruction, `MasterPlayer.Main/~MasterPlayer` triggers `MasterPlayer.Chat/CleanupChannels` to remove the player from all channels and deletes owned `Mail` and `Item` objects to prevent memory leaks.

## Data Model

`MasterPlayer` interacts with several database tables to persist player-specific configuration and communication state. The `MasterPlayer.Chat` unit does not touch the database.

*   **`character_action`**: Stores the player's action bar configuration.
    *   *Columns*: `guid` (PK), `button` (PK), `action`, `type`.
    *   *Usage*: Loaded by `LoadActions`, saved by `SaveActions`.
*   **`mail`**: Stores mail messages.
    *   *Columns*: `id` (PK), `message_type`, `stationery`, `mail_template_id`, `sender_guid`, `receiver_guid`, `subject`, `item_text_id`, `has_items`, `expire_time`, `deliver_time`, `money`, `cod`, `checked`.
    *   *Usage*: Loaded by `LoadMails`, updated/deleted by `SaveMails`.
*   **`mail_items`**: Links items to mail messages.
    *   *Columns*: `mail_id` (PK), `item_guid` (PK), `item_id`, `receiver_guid`.
    *   *Usage*: Updated/deleted by `SaveMails`.
*   **`item_instance`**: Stores item data for items attached to mail.
    *   *Columns*: `guid` (PK), `item_id`, `owner_guid`, `creator_guid`, `gift_creator_guid`, `count`, `duration`, `charges`, `flags`, `enchantments`, `random_property_id`, `durability`, `text`, `generated_loot`.
    *   *Usage*: Loaded by `LoadMailedItems`, deleted by `SaveMails` when mail is removed.
*   **`item_text`**: Stores custom text for items.
    *   *Columns*: `id` (PK), `text`.
    *   *Usage*: Deleted by `SaveMails` if associated with deleted mail items.
*   **`character_deleted_items`**: Logs items that were deleted due to errors (e.g., missing prototypes) during mail loading.
    *   *Columns*: `id` (PK), `player_guid`, `item_id`, `stack_count`.
    *   *Usage*: Inserted into by `LoadMailedItems` on error.

## Where to Go Deeper

*   **For Chat, Whispers, and Channel Management**: Open **`MasterPlayer.Chat`**. This doc details the anti-spam logic, whisper packet construction, AFK/DND toggling, and channel cleanup procedures.
*   **For Mail, Action Bars, Social Lists, and Lifecycle**: Open **`MasterPlayer.Main`**. This doc covers the initialization process, database persistence strategies for mail and actions, memory management of owned objects, and the accessor methods used by other systems to query player state.

---

<!-- machine-true, projected from graph.json -->

## Map — MasterPlayer

*Source:* MasterPlayerChat.cpp, MasterPlayer.h, MasterPlayer.cpp

| Member | Partial | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|---|
| UpdateSpeakTime | MasterPlayer.Chat | method | MasterPlayer.Main/GetSession, World/getConfig#4, WorldSession.Main/GetSecurity | WorldSession.ChatHandler/HandleChatMessageOpcode | — |
| Whisper | MasterPlayer.Chat | method | ByteBuffer/clear, ChatHandler.Chat/BuildChatPacket, MasterPlayer.Main/AddAllowedWhisperer, MasterPlayer.Main/GetChatTag, MasterPlayer.Main/GetName, MasterPlayer.Main/GetObjectGuid, MasterPlayer.Main/GetSession, MasterPlayer.Main/IsAcceptWhispers, MasterPlayer.Main/IsAFK, MasterPlayer.Main/IsDND, WorldPacket/WorldPacket, WorldSession.Main/SendPacket | ChatHandler.DebugCommands/HandleDebugChatFreezeCommand, WorldSession.ChatHandler/HandleChatMessageOpcode | — |
| ToggleDND | MasterPlayer.Chat | method | — | — | — |
| ToggleAFK | MasterPlayer.Chat | method | — | — | — |
| JoinedChannel | MasterPlayer.Chat | method | — | — | — |
| LeftChannel | MasterPlayer.Chat | method | — | — | — |
| CleanupChannels | MasterPlayer.Chat | method | ChannelMgr/channelMgr, ChannelMgr/LeftChannel, game_Chat_Channel/GetName, game_Chat_Channel/Leave, MasterPlayer.Main/GetObjectGuid, MasterPlayer.Main/GetTeam | MasterPlayer.Main/~MasterPlayer | — |
| MasterPlayer | MasterPlayer.Main | ctor | — | Player.Main/SaveNewPlayer, PlayerBotAI/SpawnNewPlayer, WorldSession.CharacterHandler/HandlePlayerLogin | — |
| ~MasterPlayer | MasterPlayer.Main | dtor | MasterPlayer.Chat/CleanupChannels, ObjectAccessor/RemoveObject#2 | — | — |
| Create | MasterPlayer.Main | method | Errors/PrintStacktraceAndThrow, ObjectMgr/GetPlayerInfo | Player.Main/SaveNewPlayer | — |
| GetSession | MasterPlayer.Main | method | — | AccountMgr/CountWhispersTo, AccountMgr/GetWhisperScore, game_Chat_Channel/List, game_Guild_Guild/BroadcastToGuild, game_Guild_Guild/BroadcastToOfficers, MasterPlayer.Chat/UpdateSpeakTime, MasterPlayer.Chat/Whisper, SocialMgr/BroadcastToFriendListers, SocialMgr/GetFriendInfo, SocialMgr/SendFriendList, SocialMgr/SendFriendStatus, SocialMgr/SendIgnoreList, WorldSession.CharacterHandler/HandlePlayerLogin, WorldSession.ChatHandler/HandleChatMessageOpcode | — |
| SetSession | MasterPlayer.Main | method | — | WorldSession.CharacterHandler/HandlePlayerLogin | — |
| GetGUIDLow | MasterPlayer.Main | method | — | AccountMgr/CountWhispersTo, AccountMgr/WhisperedBy, game_Mail_Mail/prepareTemplateItems, SocialMgr/BroadcastToFriendListers, WorldSession.MailHandler/HandleMailReturnToSender, WorldSession.MailHandler/HandleSendMailCallback, WorldSession.Main/LogoutPlayer | — |
| GetObjectGuid | MasterPlayer.Main | method | — | game_Guild_Guild/BroadcastToGuild, game_Guild_Guild/BroadcastToOfficers, MasterPlayer.Chat/CleanupChannels, MasterPlayer.Chat/Whisper, SocialMgr/GetFriendInfo, WorldSession.CharacterHandler/HandlePlayerLogin, WorldSession.ChatHandler/HandleChatMessageOpcode, WorldSession.MailHandler/HandleMailCreateTextItem, WorldSession.MailHandler/HandleSendMail, WorldSession.Main/LogoutPlayer, WorldSession.MiscHandler/HandleAddFriendOpcode, WorldSession.MiscHandler/HandleAddIgnoreOpcode | — |
| GetGuidStr | MasterPlayer.Main | method | — | WorldSession.MailHandler/HandleSendMail | — |
| LoadPlayer | MasterPlayer.Main | method | Object/GetObjectGuid, Player.Main/GetCachedAreaId, Player.Main/GetCachedZoneId, Player.Main/GetChatTag, Player.Main/GetExtraFlags, Player.Main/GetGMInvisibilityLevel, Player.Main/GetGuildId, Player.Main/GetName, Player.Main/GetRank, Player.Main/GetTeam, Unit.Main/GetClass, Unit.Main/GetLevel, Unit.Main/GetRace | PlayerBotAI/SpawnNewPlayer, WorldSession.CharacterHandler/HandlePlayerLogin, WorldSession.Main/Update | — |
| GetSocial | MasterPlayer.Main | method | — | game_Guild_Guild/BroadcastToGuild, game_Guild_Guild/BroadcastToOfficers, Player.Main/FindSocial, Player.Main/GetSocial, SocialMgr/GetFriendInfo, WorldSession.Main/LogoutPlayer, WorldSession.MiscHandler/HandleAddFriendOpcode, WorldSession.MiscHandler/HandleAddIgnoreOpcode, WorldSession.MiscHandler/HandleDelFriendOpcode, WorldSession.MiscHandler/HandleDelIgnoreOpcode, WorldSession.MiscHandler/HandleFriendListOpcode | — |
| SetSocial | MasterPlayer.Main | method | — | Map.Main/CrashUnload, PlayerBotAI/SpawnNewPlayer, WorldSession.Main/LogoutPlayer | — |
| GetActionButtons | MasterPlayer.Main | method | — | Player.Main/ConvertSpell | — |
| SaveToDB | MasterPlayer.Main | method | Database/BeginTransaction, Database/CommitTransaction | Player.Main/SaveNewPlayer, WorldSession.Main/LogoutPlayer | — |
| Update | MasterPlayer.Main | method | WorldSession.MailHandler/SendNewMail | WorldSession.Main/Update | — |
| AddMail | MasterPlayer.Main | method | — | game_Mail_Mail/SendMailTo | — |
| GetMailSize | MasterPlayer.Main | method | — | WorldSession.MailHandler/HandleGetMailList, WorldSession.MailHandler/HandleSendMailRequest | — |
| MarkMailsUpdated | MasterPlayer.Main | method | — | WorldSession.MailHandler/HandleMailCreateTextItem, WorldSession.MailHandler/HandleMailDelete, WorldSession.MailHandler/HandleMailMarkAsRead, WorldSession.MailHandler/HandleMailTakeItem, WorldSession.MailHandler/HandleMailTakeMoney | — |
| HasUnreadMail | MasterPlayer.Main | method | — | WorldSession.MailHandler/HandleQueryNextMailTime | — |
| DecreaseUnreadMailsCount | MasterPlayer.Main | method | — | WorldSession.MailHandler/HandleMailMarkAsRead | — |
| GetMailBegin | MasterPlayer.Main | method | — | WorldSession.MailHandler/HandleGetMailList | — |
| GetMailEnd | MasterPlayer.Main | method | — | WorldSession.MailHandler/HandleGetMailList | — |
| AddMItem | MasterPlayer.Main | method | Object/GetGUIDLow | game_Mail_Mail/prepareTemplateItems, game_Mail_Mail/SendMailTo | — |
| GetMItem | MasterPlayer.Main | method | — | WorldSession.MailHandler/HandleGetMailList, WorldSession.MailHandler/HandleMailReturnToSender, WorldSession.MailHandler/HandleMailTakeItem | — |
| SaveMails | MasterPlayer.Main | method | Database/CreateStatement, Mail/HasItems, SqlPreparedStatement/Execute#2, SqlPreparedStatement/operator=, SqlStatement/addUInt32, SqlStatement/addUInt64, SqlStatementID/SqlStatementID | WorldSession.MailHandler/HandleMailTakeItem, WorldSession.MailHandler/HandleMailTakeMoney | item_instance, item_text, mail, mail_items |
| RemoveMItem | MasterPlayer.Main | method | — | WorldSession.MailHandler/HandleMailReturnToSender, WorldSession.MailHandler/HandleMailTakeItem | — |
| GetGuildId | MasterPlayer.Main | method | — | WorldSession.ChatHandler/HandleChatMessageOpcode | — |
| GetRank | MasterPlayer.Main | method | — | game_Guild_Guild/BroadcastToGuild, game_Guild_Guild/BroadcastToOfficers | — |
| IsAcceptTickets | MasterPlayer.Main | method | — | — | — |
| SetAcceptTicket | MasterPlayer.Main | method | — | — | — |
| IsAcceptWhispers | MasterPlayer.Main | method | — | ChatHandler.CharacterCommands/HandleWhispersCommand, MasterPlayer.Chat/Whisper | — |
| AcceptsWhispersFrom | MasterPlayer.Main | method | — | WorldSession.ChatHandler/HandleChatMessageOpcode | — |
| AddAllowedWhisperer | MasterPlayer.Main | method | — | MasterPlayer.Chat/Whisper | — |
| ClearAllowedWhisperers | MasterPlayer.Main | method | — | ChatHandler.CharacterCommands/HandleWhispersCommand, Player.Main/SetGMVisible | — |
| SetAcceptWhispers | MasterPlayer.Main | method | — | — | — |
| IsGameMaster | MasterPlayer.Main | method | — | WorldSession.ChatHandler/HandleChatMessageOpcode | — |
| GetGMInvisibilityRank | MasterPlayer.Main | method | — | — | — |
| GetTeam | MasterPlayer.Main | method | — | MasterPlayer.Chat/CleanupChannels, SocialMgr/BroadcastToFriendListers, SocialMgr/GetFriendInfo, WorldSession.ChatHandler/HandleChatMessageOpcode, WorldSession.MailHandler/HandleSendMailCallback, WorldSession.MailHandler/HandleSendMailRequest, WorldSession.MiscHandler/HandleAddFriendOpcode | — |
| GetName | MasterPlayer.Main | method | — | game_Guild_Guild/BroadcastToGuild, game_Guild_Guild/BroadcastToOfficers, MasterPlayer.Chat/Whisper, ObjectAccessor/AddObject#2, ObjectAccessor/RemoveObject#2, SocialMgr/GetFriendInfo, WorldSession.MailHandler/HandleSendMailCallback, WorldSession.MiscHandler/HandleAddFriendOpcode, WorldSession.MiscHandler/HandleAddIgnoreOpcode | — |
| GetZoneId | MasterPlayer.Main | method | — | SocialMgr/GetFriendInfo | — |
| GetAreaId | MasterPlayer.Main | method | — | AccountMgr/GetWhisperScore | — |
| GetClass | MasterPlayer.Main | method | — | SocialMgr/GetFriendInfo | — |
| GetRace | MasterPlayer.Main | method | — | — | — |
| GetLevel | MasterPlayer.Main | method | — | SocialMgr/GetFriendInfo, WorldSession.ChatHandler/HandleChatMessageOpcode | — |
| IsAFK | MasterPlayer.Main | method | — | MasterPlayer.Chat/Whisper, SocialMgr/GetFriendInfo | — |
| IsDND | MasterPlayer.Main | method | — | MasterPlayer.Chat/Whisper, SocialMgr/GetFriendInfo | — |
| GetChatTag | MasterPlayer.Main | method | — | game_Guild_Guild/BroadcastToGuild, game_Guild_Guild/BroadcastToOfficers, MasterPlayer.Chat/Whisper | — |
| RemoveMail | MasterPlayer.Main | method | — | WorldSession.MailHandler/HandleMailReturnToSender | — |
| UpdateNextMailTimeAndUnreads | MasterPlayer.Main | method | — | WorldSession.CharacterHandler/HandlePlayerLogin, WorldSession.MailHandler/HandleGetMailList | — |
| AddNewMailDeliverTime | MasterPlayer.Main | method | WorldSession.MailHandler/SendNewMail | game_Mail_Mail/SendMailTo | — |
| LoadMailedItems | MasterPlayer.Main | method | Bag/NewItemOrBag, Database/PExecute#2, Field/GetBool, Field/GetUInt32, game_Objects_Item/FSetState, game_Objects_Item/LoadFromDB, game_Objects_Item/SaveToDB, game_Objects_Item/SetGeneratedLoot, Log.Main/Out, Mail/AddItem, ObjectMgr/GetItemPrototype, QueryResult/Fetch, QueryResult/NextRow | WorldSession.CharacterHandler/HandlePlayerLogin | character_deleted_items, item_instance, mail_items |
| LoadMails | MasterPlayer.Main | method | Errors/PrintStacktraceAndThrow, Field/GetBool, Field/GetCppString, Field/GetInt16, Field/GetUInt32, Field/GetUInt64, Field/GetUInt8, game_Mail_Mail/prepareTemplateItems, Log.Main/Out, ObjectGuid/ObjectGuid#2, QueryResult/Fetch, QueryResult/NextRow, WorldSession.Main/GetPlayer | WorldSession.CharacterHandler/HandlePlayerLogin | — |
| GetMail | MasterPlayer.Main | method | — | WorldSession.MailHandler/HandleMailCreateTextItem, WorldSession.MailHandler/HandleMailDelete, WorldSession.MailHandler/HandleMailMarkAsRead, WorldSession.MailHandler/HandleMailReturnToSender, WorldSession.MailHandler/HandleMailTakeItem, WorldSession.MailHandler/HandleMailTakeMoney | — |
| SendInitialActionButtons | MasterPlayer.Main | method | ByteBuffer/operator<<#10, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | Player.Main/SendInitialPacketsBeforeAddToMap, WorldSession.CharacterHandler/HandlePlayerLogin | — |
| addActionButton | MasterPlayer.Main | method | ActionButton/SetActionAndType, Player.Main/IsActionButtonDataValid | WorldSession.MiscHandler/HandleSetActionButtonOpcode | — |
| removeActionButton | MasterPlayer.Main | method | — | WorldSession.MiscHandler/HandleSetActionButtonOpcode | — |
| LoadActions | MasterPlayer.Main | method | Field/GetUInt32, Field/GetUInt8, QueryResult/Fetch, QueryResult/NextRow | WorldSession.CharacterHandler/HandlePlayerLogin | — |
| SaveActions | MasterPlayer.Main | method | ActionButton/GetAction, ActionButton/GetType, Database/CreateStatement, SqlPreparedStatement/Execute#2, SqlStatement/addUInt32, SqlStatementID/SqlStatementID | — | character_action |
| LoadSocial | MasterPlayer.Main | method | SocialMgr/LoadFromDB, SocialMgr/SetMasterPlayer | WorldSession.CharacterHandler/HandlePlayerLogin | — |
| IsVisibleGloballyFor | MasterPlayer.Main | method | WorldSession.Main/GetSecurity | game_Chat_Channel/List, SocialMgr/BroadcastToFriendListers, SocialMgr/GetFriendInfo, WorldSession.MiscHandler/HandleAddFriendOpcode | — |

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

