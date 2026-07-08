# SocialMgr

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# SocialMgr

**Purpose & Responsibilities**

`SocialMgr` and its associated `PlayerSocial` class manage the persistent and runtime social relationships for players in the World of Warcraft emulation environment. Specifically, it handles **Friend Lists** and **Ignore Lists**.

The system operates on two levels:
1.  **Persistence (`SocialMgr`)**: The global singleton `sSocialMgr` maintains a registry of all loaded players' social data in memory (`m_socialMap`) and handles loading this data from the `character_social` database table upon player login. It also manages broadcasting status changes (online/offline) to relevant friends.
2.  **Per-Player State (`PlayerSocial`)**: Each `MasterPlayer` instance holds a `PlayerSocial` object that tracks the specific list of friends and ignores for that character. It provides methods to add/remove entries, check membership, and construct network packets to send the current lists to the client.

The social relationship is defined by a bitmask (`SocialFlag`) stored in the database and memory, allowing a single entry to potentially represent multiple states (though primarily used for Friend vs. Ignore). The system enforces hard limits on the number of friends (50) and ignores (25) per character.

## Data Model

The unit interacts with a single database table:

### `character_social`
*   **Columns**:
    *   `guid` (int(11) unsigned, PK): The low GUID of the player who owns this social entry.
    *   `friend` (int(11) unsigned, PK): The low GUID of the target player (friend or ignored).
    *   `flags` (tinyint(1) unsigned, PK): Bitmask indicating the relationship type.
        *   `0x01` (`SOCIAL_FLAG_FRIEND`): Target is a friend.
        *   `0x02` (`SOCIAL_FLAG_IGNORED`): Target is ignored.
        *   `0x04` (`SOCIAL_FLAG_MUTED`): Target is muted (defined in header, though usage in this specific unit is limited to storage/retrieval).

*   **Usage**:
    *   `AddToSocialList`: Inserts a new row or updates the `flags` column via bitwise OR if the row exists.
    *   `RemoveFromSocialList`: Updates `flags` via bitwise AND NOT, or deletes the row entirely if the resulting flags are zero.
    *   `LoadFromDB`: Reads all rows for a given `guid` and populates the in-memory `PlayerSocialMap`.

## Member-by-Member Behavior

### Initialization and Lifecycle

*   **`PlayerSocial` ctor**: Initializes the internal map and clears it upon destruction.
*   **`~PlayerSocial` dtor**: Clears `m_playerSocialMap`.
*   **`SocialMgr` ctor**: Empty constructors/destructors for the singleton manager.
*   **`~SocialMgr` dtor**: Empty destructor.
*   **`SetPlayerGuid` / `SetMasterPlayer` / `GetMasterPlayer`**: Simple accessors/setters for linking the `PlayerSocial` instance to its owning `MasterPlayer` and storing the owner's low GUID for database queries.

### List Management (Add/Remove/Check)

*   **`AddToSocialList`**:
    *   Validates that the list hasn't exceeded `SOCIALMGR_FRIEND_LIMIT` (50) or `SOCIALMGR_IGNORE_LIMIT` (25).
    *   Checks if the target GUID already exists in the local `m_playerSocialMap`.
    *   If it exists, it performs an `UPDATE` on `character_social` to set the appropriate bit in `flags`.
    *   If it does not exist, it performs an `INSERT` into `character_social` and adds the entry to the local map.
    *   Returns `true` on success, `false` if the limit was reached.

*   **`RemoveFromSocialList`**:
    *   Locates the target in the local map. If not found, returns immediately.
    *   Clears the specific bit (Friend or Ignore) from the `Flags` field.
    *   If the resulting `Flags` is 0, it deletes the row from `character_social` and removes the entry from the local map.
    *   Otherwise, it updates the `flags` column in `character_social` to reflect the remaining bits.

*   **`HasFriend` / `HasIgnore`**:
    *   Checks the local `m_playerSocialMap` for the target GUID and verifies if the corresponding bit (`SOCIAL_FLAG_FRIEND` or `SOCIAL_FLAG_IGNORED`) is set in the `Flags` field.

*   **`GetNumberOfSocialsWithFlag`**:
    *   Iterates through the entire `m_playerSocialMap` and counts entries where the specified flag bit is set. This is an O(N) operation relative to the size of the social list.

### Packet Construction and Sending

*   **`SendFriendList`**:
    *   Constructs an `SMSG_FRIEND_LIST` packet.
    *   Iterates over `m_playerSocialMap`, filtering for `SOCIAL_FLAG_FRIEND`.
    *   For each friend, it calls `sSocialMgr.GetFriendInfo` to populate `FriendInfo` with current online status, zone, level, and class.
    *   Serializes the GUID, status, and (if online) zone/level/class into the packet.
    *   Sends the packet to the owning player's session.

*   **`SendIgnoreList`**:
    *   Constructs an `SMSG_IGNORE_LIST` packet.
    *   Iterates over `m_playerSocialMap`, filtering for `SOCIAL_FLAG_IGNORED`.
    *   Serializes only the GUIDs of ignored players.
    *   Sends the packet to the owning player's session.

*   **`MakeFriendStatusPacket`**:
    *   Helper to initialize a `SMSG_FRIEND_STATUS` packet with a result code and target GUID. Used internally by `SendFriendStatus`.

*   **`SendFriendStatus`**:
    *   Constructs a status update packet using `MakeFriendStatusPacket`.
    *   Calls `GetFriendInfo` to determine the current state of the friend.
    *   Appends additional data (status, area, level, class) if the result indicates the friend is online (`FRIEND_ADDED_ONLINE` or `FRIEND_ONLINE`). Note: This extra data is only appended for client builds greater than 1.8.4.
    *   If `broadcast` is true, delegates to `BroadcastToFriendListers`; otherwise, sends directly to the player.

### Status Resolution and Broadcasting

*   **`GetFriendInfo`**:
    *   Populates a `FriendInfo` struct for a given friend GUID relative to a viewing player.
    *   **Self-Check**: If the friend GUID matches the viewer's GUID, it sets status based on AFK/DND/Online and fills in zone/level/class.
    *   **Visibility Logic**: If the friend is online (`ObjectAccessor::FindMasterPlayer` succeeds), it checks visibility rules:
        *   Players can only see friends on the same faction (team), unless `CONFIG_BOOL_ALLOW_TWO_SIDE_WHO_LIST` is enabled.
        *   Players cannot see GMs/Admins unless they themselves are higher security level.
        *   GMs/Admins can see everyone.
        *   The target must pass `IsVisibleGloballyFor`.
    *   If visible, it populates status, zone, level, and class. If not visible or offline, it sets status to `OFFLINE` and zeros out other fields.

*   **`BroadcastToFriendListers`**:
    *   Iterates through the global `m_socialMap` (protected by `_socialMapLock`).
    *   For each player in the global map, it checks if the broadcasting player is in their friend list (`SOCIAL_FLAG_FRIEND`).
    *   If so, it retrieves the friend's `MasterPlayer` object and applies the same visibility/security checks as `GetFriendInfo`.
    *   If visible, it sends the provided packet to that friend's session.

### Persistence

*   **`LoadFromDB`**:
    *   Acquires a unique lock on `_socialMapLock`.
    *   Retrieves or creates the `PlayerSocial` instance for the given GUID in `m_socialMap`.
    *   Iterates through the query results.
    *   Enforces friend/ignore limits during load (skipping entries if limits are exceeded).
    *   Skips entries where the target player's account ID cannot be resolved (deleted characters).
    *   Populates the local `m_playerSocialMap` with `FriendInfo` objects initialized with the stored flags.

*   **`RemovePlayerSocial`**:
    *   Acquires a unique lock on `_socialMapLock`.
    *   Erases the player's entry from the global `m_socialMap`. Called during logout/crash unload.

## Cross-Unit Boundaries

### Calls Out (Dependencies)

*   **`Database/PExecute`**: Used by `AddToSocialList` and `RemoveFromSocialList` to persist changes to `character_social`.
*   **`ObjectGuid/GetCounter`**: Used extensively to extract the low 32-bit GUID for map keys and database queries.
*   **`MasterPlayer.Main`**:
    *   `GetSession`: To send packets.
    *   `GetClass`, `GetLevel`, `GetName`, `GetTeam`, `GetZoneId`, `IsAFK`, `IsDND`, `IsVisibleGloballyFor`: Used by `GetFriendInfo` and `BroadcastToFriendListers` to resolve player state and visibility.
    *   `GetSocial`: Used by `GetFriendInfo` to access the friend's social map directly.
*   **`ObjectAccessor/FindMasterPlayer`**: Used by `GetFriendInfo` and `BroadcastToFriendListers` to find online players by GUID.
*   **`World/getConfig`**: Used to retrieve `CONFIG_BOOL_ALLOW_TWO_SIDE_WHO_LIST` and `CONFIG_UINT32_GM_LEVEL_IN_WHO_LIST` for visibility checks.
*   **`WorldSession.Main/SendPacket`**: Used by `SendFriendList`, `SendIgnoreList`, `SendFriendStatus`, and `BroadcastToFriendListers` to transmit data to clients.
*   **`ByteBuffer/operator<<` / `WorldPacket/WorldPacket`**: Used for serializing packet data.
*   **`Errors/PrintStacktraceAndThrow`**: Included in headers, likely for assertion failures (e.g., `ASSERT(plr)` in `SendFriendList`).
*   **`ObjectMgr/GetPlayerAccountIdByGUID`**: Used in `LoadFromDB` to verify if a target character still exists in the database.

### Called By (Consumers)

*   **`WorldSession.MiscHandler`**:
    *   `HandleAddFriendOpcode`, `HandleAddIgnoreOpcode`: Call `AddToSocialList` and `SendFriendStatus`.
    *   `HandleDelFriendOpcode`, `HandleDelIgnoreOpcode`: Call `RemoveFromSocialList` and `SendFriendStatus`.
    *   `HandleFriendListOpcode`: Calls `SendFriendList`.
*   **`WorldSession.CharacterHandler`**:
    *   `HandlePlayerLogin`: Calls `SendFriendList`, `SendIgnoreList`, and `SendFriendStatus` (for self-status).
*   **`WorldSession.Main`**:
    *   `LogoutPlayer`: Calls `SendFriendStatus` (offline broadcast) and `RemovePlayerSocial`.
*   **`MasterPlayer.Main`**:
    *   `LoadSocial`: Calls `LoadFromDB` and `SetMasterPlayer`.
*   **`Player.Main`**:
    *   `IsAllowedWhisperFrom`: Calls `HasFriend` (likely to allow whispers from friends).
*   **`game_Chat_Channel`**:
    *   `Invite`, `SendToAll`: Call `HasIgnore` to prevent ignored players from being invited or receiving messages.
*   **`game_Guild_Guild`**:
    *   `BroadcastToGuild`, `BroadcastToOfficers`: Call `HasIgnore` to filter ignored players from guild broadcasts.
*   **`Spell.Effects`**:
    *   `EffectDuel`: Calls `HasIgnore` to prevent dueling ignored players.
*   **`WorldSession.GroupHandler` / `GuildHandler`**:
    *   `HandleGroupInviteOpcode`, `HandleGuildInviteOpcode`: Call `HasIgnore` to block invites to ignored players.
*   **`PlayerBotAI`**:
    *   `SpawnNewPlayer`: Calls `LoadFromDB` (likely for bot initialization).
*   **`Map.Main`**:
    *   `CrashUnload`: Calls `RemovePlayerSocial`.

## Notable Implementation Details

1.  **Thread Safety**:
    *   The global `m_socialMap` in `SocialMgr` is protected by `std::shared_timed_mutex _socialMapLock`.
    *   `LoadFromDB` and `RemovePlayerSocial` acquire a `unique_lock` (write lock).
    *   `BroadcastToFriendListers` acquires a `shared_lock` (read lock), allowing concurrent reads during broadcasts.
    *   `PlayerSocial` instances themselves are **not** thread-safe. They are accessed via the player's session thread. However, `BroadcastToFriendListers` accesses `itr.second.m_playerSocialMap` (the `PlayerSocial` map) while holding the global read lock. This implies that modifications to individual `PlayerSocial` maps (via `AddToSocialList` etc.) must not occur concurrently with a broadcast, or there is a risk of data race if the broadcast iterates while another thread modifies the inner map. Given that social operations are typically triggered by player actions (single-threaded per player), this might be safe in practice, but strictly speaking, accessing `m_playerSocialMap` inside `BroadcastToFriendListers` without locking the individual `PlayerSocial` object is a potential hazard if `AddToSocialList` were called concurrently.

2.  **Visibility Logic Duplication**:
    *   The logic for determining if a player is visible to another (faction checks, GM security levels, `IsVisibleGloballyFor`) is duplicated between `GetFriendInfo` and `BroadcastToFriendListers`. Any change to visibility rules must be applied to both locations.

3.  **Client Build Compatibility**:
    *   `SendFriendStatus` uses `#if SUPPORTED_CLIENT_BUILD > CLIENT_BUILD_1_8_4` to conditionally append status/area/level/class data to the `SMSG_FRIEND_STATUS` packet. This ensures compatibility with older clients that may not expect these fields.

4.  **Limit Enforcement**:
    *   Limits are enforced at insertion time (`AddToSocialList`) and load time (`LoadFromDB`).
    *   `LoadFromDB` silently skips entries that exceed the limit, effectively truncating the list to the maximum allowed size. This prevents crashes or invalid states if the database contains more entries than the client supports.

5.  **Flag Bitmasking**:
    *   The system uses a bitmask for flags. While primarily used for Friend (0x01) and Ignore (0x02), the code supports combining them (e.g., a player could theoretically be both a friend and ignored, though the UI likely prevents this). `RemoveFromSocialList` correctly handles partial removal (clearing one bit while keeping the other) and only deletes the DB row if all bits are cleared.

6.  **Self-Status Handling**:
    *   `GetFriendInfo` has a special case for when the `friend_lowguid` equals the `player`'s GUID. This allows the system to report the player's own status to themselves (e.g., when logging in/out), ensuring the client displays the correct AFK/DND/Online state for the user's own icon.

## Member Reference

*   **PlayerSocial**: Initializes `m_playerLowGuid` to 0 and `m_masterPlayer` to nullptr.
*   **~PlayerSocial**: Clears `m_playerSocialMap`.
*   **GetNumberOfSocialsWithFlag**: Iterates `m_playerSocialMap` and counts entries with the specified flag bit set.
*   **AddToSocialList**: Adds a friend/ignore entry to memory and database, enforcing limits.
*   **RemoveFromSocialList**: Removes a friend/ignore entry from memory and database, handling partial flag removal.
*   **SendFriendList**: Constructs and sends `SMSG_FRIEND_LIST` packet to the player.
*   **SetFriendNote**: Declared in header, not implemented in this unit.
*   **SetPlayerGuid**: Sets `m_playerLowGuid` from the provided GUID.
*   **SetMasterPlayer**: Sets `m_masterPlayer` pointer.
*   **GetMasterPlayer**: Returns `m_masterPlayer` pointer.
*   **SendIgnoreList**: Constructs and sends `SMSG_IGNORE_LIST` packet to the player.
*   **HasFriend**: Checks if the target GUID is in the map with the `SOCIAL_FLAG_FRIEND` bit set.
*   **HasIgnore**: Checks if the target GUID is in the map with the `SOCIAL_FLAG_IGNORED` bit set.
*   **SocialMgr**: Empty constructor.
*   **~SocialMgr**: Empty destructor.
*   **GetFriendInfo**: Resolves online status, zone, level, and class for a friend, applying visibility/security filters.
*   **MakeFriendStatusPacket**: Initializes a `SMSG_FRIEND_STATUS` packet with result and GUID.
*   **SendFriendStatus**: Sends a friend status update, optionally broadcasting to other friends.
*   **BroadcastToFriendListers**: Iterates global social map to send a packet to all players who have the source player as a friend and can see them.
*   **LoadFromDB**: Loads social data from database query results into the global map, enforcing limits and validity checks.
*   **RemovePlayerSocial**: Removes a player's social data from the global map.

---

<!-- machine-true, projected from graph.json -->

## Map — SocialMgr

*Source:* SocialMgr.cpp, SocialMgr.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| PlayerSocial | ctor | — | — | — |
| ~PlayerSocial | dtor | — | — | — |
| GetNumberOfSocialsWithFlag | method | — | — | — |
| AddToSocialList | method | Database/PExecute#2, FriendInfo/FriendInfo, ObjectGuid/GetCounter | WorldSession.MiscHandler/HandleAddFriendOpcode, WorldSession.MiscHandler/HandleAddIgnoreOpcode | character_social |
| RemoveFromSocialList | method | Database/PExecute#2, ObjectGuid/GetCounter | WorldSession.MiscHandler/HandleDelFriendOpcode, WorldSession.MiscHandler/HandleDelIgnoreOpcode | character_social |
| SendFriendList | method | ByteBuffer/operator<<#10, ByteBuffer/operator<<#7, Errors/PrintStacktraceAndThrow, MasterPlayer.Main/GetSession, ObjectGuid/ObjectGuid#2, ObjectGuid/operator<<, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | WorldSession.CharacterHandler/HandlePlayerLogin, WorldSession.MiscHandler/HandleFriendListOpcode | — |
| SetFriendNote | decl | — | — | — |
| SetPlayerGuid | method | — | — | — |
| SetMasterPlayer | method | — | MasterPlayer.Main/LoadSocial | — |
| GetMasterPlayer | method | — | — | — |
| SendIgnoreList | method | ByteBuffer/operator<<#7, Errors/PrintStacktraceAndThrow, MasterPlayer.Main/GetSession, ObjectGuid/ObjectGuid#2, ObjectGuid/operator<<, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | WorldSession.CharacterHandler/HandlePlayerLogin | — |
| HasFriend | method | ObjectGuid/GetCounter | Player.Main/IsAllowedWhisperFrom, WorldSession.MiscHandler/HandleAddFriendOpcode | — |
| HasIgnore | method | ObjectGuid/GetCounter | game_Chat_Channel/Invite, game_Chat_Channel/SendToAll, game_Guild_Guild/BroadcastToGuild, game_Guild_Guild/BroadcastToOfficers, Spell.Effects/EffectDuel, WorldSession.GroupHandler/HandleGroupInviteOpcode, WorldSession.GuildHandler/HandleGuildInviteOpcode, WorldSession.MiscHandler/HandleAddIgnoreOpcode | — |
| SocialMgr | ctor | — | — | — |
| ~SocialMgr | dtor | — | — | — |
| GetFriendInfo | method | MasterPlayer.Main/GetClass, MasterPlayer.Main/GetLevel, MasterPlayer.Main/GetName, MasterPlayer.Main/GetObjectGuid, MasterPlayer.Main/GetSession, MasterPlayer.Main/GetSocial, MasterPlayer.Main/GetTeam, MasterPlayer.Main/GetZoneId, MasterPlayer.Main/IsAFK, MasterPlayer.Main/IsDND, MasterPlayer.Main/IsVisibleGloballyFor, ObjectAccessor/FindMasterPlayer, ObjectGuid/GetCounter, ObjectGuid/ObjectGuid#2, World/getConfig, World/getConfig#4, WorldSession.Main/GetSecurity | — | — |
| MakeFriendStatusPacket | method | ByteBuffer/operator<<#7, ObjectGuid/ObjectGuid#2, ObjectGuid/operator<<, WorldPacket/Initialize | — | — |
| SendFriendStatus | method | ByteBuffer/operator<<#10, ByteBuffer/operator<<#7, FriendInfo/FriendInfo, MasterPlayer.Main/GetSession, ObjectGuid/GetCounter, WorldPacket/WorldPacket, WorldSession.Main/SendPacket | WorldSession.CharacterHandler/HandlePlayerLogin, WorldSession.Main/LogoutPlayer, WorldSession.MiscHandler/HandleAddFriendOpcode, WorldSession.MiscHandler/HandleAddIgnoreOpcode, WorldSession.MiscHandler/HandleDelFriendOpcode, WorldSession.MiscHandler/HandleDelIgnoreOpcode | — |
| BroadcastToFriendListers | method | MasterPlayer.Main/GetGUIDLow, MasterPlayer.Main/GetSession, MasterPlayer.Main/GetTeam, MasterPlayer.Main/IsVisibleGloballyFor, ObjectAccessor/FindMasterPlayer, ObjectGuid/ObjectGuid#2, World/getConfig, World/getConfig#4, WorldSession.Main/GetSecurity, WorldSession.Main/SendPacket | — | — |
| LoadFromDB | method | Field/GetUInt32, FriendInfo/FriendInfo#2, ObjectGuid/GetCounter, ObjectGuid/ObjectGuid#2, ObjectMgr/GetPlayerAccountIdByGUID, QueryResult/Fetch, QueryResult/NextRow | MasterPlayer.Main/LoadSocial, PlayerBotAI/SpawnNewPlayer | — |
| RemovePlayerSocial | method | — | Map.Main/CrashUnload, WorldSession.Main/LogoutPlayer | — |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `character_social`: guid int(11) unsigned PK, friend int(11) unsigned PK, flags tinyint(1) unsigned PK

*`?` = nullable, `PK` = primary key column.*

