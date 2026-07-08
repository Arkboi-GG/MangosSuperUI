<!-- provenance: boundary-bleed -->
# WorldSession.QueryHandler

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# WorldSession.QueryHandler

## Purpose & Responsibilities

`WorldSession.QueryHandler` implements the methods within the `WorldSession` class responsible for processing specific client-to-server "query" opcodes and generating the corresponding server-to-client responses. These queries allow the game client to retrieve static or semi-static metadata about entities in the world, including player names, creature templates, game object details, NPC dialogue text, and page text items.

This unit serves as the interface between the client's request for information and the server's data stores. It primarily relies on in-memory caches and DBC (Data Block Chunk) files managed by `ObjectMgr`, with limited fallback to live `Player` objects or, in one deprecated case, the database. A key responsibility is ensuring that response packets conform to the binary layout expected by the specific World of Warcraft client version connected to the session, handling structural differences across builds (e.g., 1.6.1 through 1.12.1).

The primary responsibilities are:
1.  **Handling Query Opcodes:** Receiving and parsing opcodes such as `CMSG_QUERY_PLAYER_NAME`, `CMSG_CREATURE_QUERY`, and `CMSG_CORPSE_QUERY`.
2.  **Data Retrieval:** Fetching data from `ObjectMgr` (for templates and locales), live `Player` objects (for online players), or memory caches (for offline players).
3.  **Packet Construction:** Building `WorldPacket` objects with precise binary layouts, managing locale-specific string substitutions, and applying client-version-dependent fields.
4.  **Response Transmission:** Sending the constructed packets back to the client via `WorldSession.Main/SendPacket`.

## Member-by-Member Behavior

### Player Name Queries

**`HandleQueryPlayerNameOpcode`**
Handles the `CMSG_QUERY_PLAYER_NAME` opcode. It receives a `QueryPlayerName` packet containing a `playerGuid`.
1.  It attempts to retrieve the live `Player` object associated with that GUID from the global `ObjectMgr` using `ObjectMgr/GetPlayer`.
2.  If the player is online (`pChar` is valid), it delegates to `SendNameQueryOpcode` to generate the response from the live object.
3.  If the player is offline, it delegates to `SendNameQueryOpcodeFromDB` to attempt retrieval from cached sources.

**`SendNameQueryOpcode`**
Generates the `SMSG_NAME_QUERY_RESPONSE` packet for an online `Player` object.
1.  Validates that the `Player` pointer is not null.
2.  Constructs a `WorldPacket` with opcode `SMSG_NAME_QUERY_RESPONSE`. The initial buffer size is estimated based on the client build.
3.  Serializes the following data:
    *   The player's `ObjectGuid` (via `Object/GetObjectGuid` and `ObjectGuid/operator<<`).
    *   The player's name (via `Player.Main/GetName`).
    *   An empty string for the realm name (reserved for cross-realm battleground usage in newer clients).
    *   Race, Gender, and Class IDs (via `Unit.Main/GetRace`, `Unit.Main/GetGender`, `Unit.Main/GetClass`).
4.  Sends the packet via `WorldSession.Main/SendPacket`.
5.  **Client Versioning:** For clients `>= 1.12.1`, the packet includes the extra realm name field. Older clients omit it.

**`SendNameQueryOpcodeFromDB`**
Attempts to generate a name query response for an offline player using cached data.
1.  It checks the `ObjectMgr`'s player cache using `ObjectMgr/GetPlayerDataByGUID` with the GUID's counter.
2.  If cache data (`PlayerCacheData`) exists:
    *   It constructs the `SMSG_NAME_QUERY_RESPONSE` packet using the cached name, race, gender, and class.
    *   It reconstructs the `ObjectGuid` using `HIGHGUID_PLAYER` and the cached counter.
    *   It sends the packet via `WorldSession.Main/SendPacket`.
3.  If cache data does not exist, it performs no action. The code contains commented-out logic that previously performed an asynchronous database query (`CharacterDatabase.AsyncPQuery`) calling `SendNameQueryOpcodeFromDBCallBack`. The comment indicates this was removed because querying the database for players who have not logged in during the current server uptime is considered unnecessary.

**`SendNameQueryOpcodeFromDBCallBack`**
This is a static callback method intended to handle the result of the now-commented-out asynchronous database query.
1.  It checks if the `QueryResult` is valid.
2.  It retrieves the `WorldSession` associated with the `accountId` passed from the async call using `World/FindSession`.
3.  If the session is invalid, it deletes the result and returns.
4.  It extracts the GUID, name, race, gender, and class from the `Field` objects in the result.
5.  It constructs and sends the `SMSG_NAME_QUERY_RESPONSE` packet similar to `SendNameQueryOpcode`, but using the database-fetched values.
6.  It deletes the `QueryResult` object.
*Note: Since the caller (`SendNameQueryOpcodeFromDB`) no longer invokes the async query, this method is effectively dead code in the current state of the source.*

### Time Queries

**`HandleQueryTimeOpcode`**
Handles the `CMSG_QUERY_TIME` opcode. It simply calls `SendQueryTimeResponse()`.

**`SendQueryTimeResponse`**
Constructs and sends the `SMSG_QUERY_TIME_RESPONSE` packet.
1.  Creates a packet with opcode `SMSG_QUERY_TIME_RESPONSE`.
2.  Serializes the current Unix timestamp (`time(nullptr)`) as a `uint32`.
3.  Sends the packet via `WorldSession.Main/SendPacket`.
*Called by:* `WorldSession.GMTicketHandler/HandleGMTicketGetTicketOpcode` (as per MAP), indicating this response utility is reused in other contexts within the `WorldSession` class hierarchy.

### Creature Queries

**`HandleCreatureQueryOpcode`**
Handles `CMSG_CREATURE_QUERY`. It retrieves static template data for a creature entry.
1.  Retrieves `CreatureInfo` from `ObjectMgr/GetCreatureTemplate(packet.entry)`.
2.  If found:
    *   Determines the appropriate name and subname. It first uses the default English names from `CreatureInfo`.
    *   It checks the session's locale index via `WorldSession.Main/GetSessionDbLocaleIndex`. If a valid locale index exists, it fetches localized names from `ObjectMgr/GetCreatureLocale`. If localized strings are available and non-empty, they override the defaults.
    *   Calculates the fixed size of the packet based on the client build, accounting for fields like `type_flags`, `pet_family`, `rank`, `display_id`, `civilian`, and `racial_leader`.
    *   Constructs the `SMSG_CREATURE_QUERY_RESPONSE` packet.
    *   Serializes: Entry ID, Name (appended as raw bytes), empty placeholders for name2/3/4, Subname, Type Flags (using `CreatureInfo/GetTypeFlags` for newer clients, `static_flags1` for older), Type, Pet Family, Rank, Unknown (0), Pet Spell List ID (newer clients), Display ID, Civilian flag, and Racial Leader flag (newer clients).
    *   Sends the packet via `WorldSession.Main/SendPacket`.
3.  If not found:
    *   Logs a debug message via `Log.Main/Out`.
    *   Sends a failure packet: `SMSG_CREATURE_QUERY_RESPONSE` with the entry ID OR'd with `0x80000000` (indicating failure/not found).

### Game Object Queries

**`HandleGameObjectQueryOpcode`**
Handles `CMSG_GAMEOBJECT_QUERY`. Similar to creature queries, it retrieves static template data.
1.  Retrieves `GameObjectInfo` from `ObjectMgr/GetGameObjectTemplate(packet.entryID)`.
2.  If found:
    *   Determines the name, checking for localized versions via `ObjectMgr/GetGameObjectLocale` if the session has a valid locale index.
    *   Calculates fixed packet size, varying significantly between clients `< 1.12.1` (16 uint32s of data) and `>= 1.12.1` (24 uint32s of data plus an icon field).
    *   Constructs `SMSG_GAMEOBJECT_QUERY_RESPONSE`.
    *   Serializes: Entry ID, Type, Display ID, Name (appended), empty placeholders for name2/3/4.
    *   For clients `>= 1.12.1`: Serializes Icon and 24 uint32s of raw data from `info->raw.data`.
    *   For older clients: Serializes 16 uint32s of raw data.
    *   Sends the packet via `WorldSession.Main/SendPacket`.
3.  If not found:
    *   Logs a debug message via `Log.Main/Out`.
    *   Sends a failure packet with entry ID OR'd with `0x80000000`.

### Corpse Queries

**`HandleCorpseQueryOpcode`**
Handles `MSG_CORPSE_QUERY`.
1.  Retrieves the `Corpse` object associated with the session's player via `WorldSession.Main/GetPlayer` and `Player.Main/GetCorpse`.
2.  If no corpse exists:
    *   Sends `MSG_CORPSE_QUERY` with `uint8(0)` (not found).
3.  If a corpse exists:
    *   Gets the corpse's map ID and coordinates (X, Y, Z) via `WorldObject.Object/GetMapId`, `WorldObject.Object/GetPositionX`, etc.
    *   Checks if the corpse is on a different map than the player.
    *   If on a different map, it checks if the corpse's map is a dungeon with a ghost entrance (`MapEntry/IsDungeon` and `ghostEntranceMap`).
    *   If so, it loads the terrain for the entrance map via `GridMap/LoadTerrain` and calculates the height at the entrance coordinates using `GridMap/GetHeightStatic`. It updates the map ID and coordinates to point to the entrance portal instead of the actual corpse location, ensuring the client displays the correct resurrection portal.
    *   Constructs `MSG_CORPSE_QUERY` with `uint8(1)` (found), followed by the (possibly adjusted) map ID, X, Y, Z, and the original corpse map ID.
    *   Sends the packet via `WorldSession.Main/SendPacket`.

### NPC Text Queries

**`HandleNpcTextQueryOpcode`**
Handles `CMSG_NPC_TEXT_QUERY`. Retrieves dialogue options for an NPC.
1.  Retrieves `NpcText` from `ObjectMgr/GetNpcText(packet.textID)`.
2.  Constructs `SMSG_NPC_TEXT_UPDATE` with a guessed size of 512 bytes.
3.  Serializes the `textID`.
4.  Iterates 8 times (for 8 possible dialogue options):
    *   If `NpcText` is null or the specific option's `BroadcastTextID` yields no `BroadcastText` via `ObjectMgr/GetBroadcastTextLocale`:
        *   Serializes default/fallback values: Probability 0, "Greetings $N" for both male and female text, language 0, and all emote delays/IDs as 0.
    *   If valid `BroadcastText` is found:
        *   Retrieves localized male and female text using `BroadcastText/GetText` with the session's locale index.
        *   Serializes Probability.
        *   Serializes Male Text (falls back to Female if Male is empty).
        *   Serializes Female Text (falls back to Male if Female is empty).
        *   Serializes Language ID.
        *   Serializes Emote Delay 1, Emote ID 1, Emote Delay 2, Emote ID 2, Emote Delay 3, Emote ID 3.
5.  Sends the packet via `WorldSession.Main/SendPacket`.

### Page Text Queries

**`HandlePageTextQueryOpcode`**
Handles `CMSG_PAGE_TEXT_QUERY`. Retrieves text for items or quests.
1.  Enters a `while` loop iterating through `pageID` chains.
2.  Looks up `PageText` in `sPageTextStore`.
3.  Constructs `SMSG_PAGE_TEXT_QUERY_RESPONSE`.
4.  Serializes `pageID`.
5.  If `PageText` is not found:
    *   Serializes "Item page missing." and next_page 0.
    *   Breaks the loop by setting `pageID = 0`.
6.  If found:
    *   Determines text, checking for localized versions via `ObjectMgr/GetPageTextLocale`.
    *   Serializes the text.
    *   Serializes `next_page`.
    *   Updates `pageID` to `next_page` to continue the chain if necessary.
7.  Sends the packet via `WorldSession.Main/SendPacket` for each page in the chain.

## Cross-Unit Boundaries

*   **`ObjectMgr`**: Heavily relied upon for retrieving static data.
    *   `GetPlayer`: Used by `HandleQueryPlayerNameOpcode` to check if a player is online.
    *   `GetPlayerDataByGUID`: Used by `SendNameQueryOpcodeFromDB` to access cached offline player data.
    *   `GetCreatureTemplate`, `GetCreatureLocale`, `GetGameObjectTemplate`, `GetGameObjectLocale`, `GetNpcText`, `GetBroadcastTextLocale`, `GetPageTextLocale`: Used by respective query handlers to fetch DBC/locale data.
*   **`Player` / `Unit`**:
    *   `GetName`, `GetClass`, `GetGender`, `GetRace`: Called by `SendNameQueryOpcode` to extract live player attributes.
    *   `GetCorpse`: Called by `HandleCorpseQueryOpcode` to locate the player's corpse.
    *   `GetObjectGuid`: Called by `SendNameQueryOpcode` to include the player's GUID in the response.
*   **`World`**:
    *   `FindSession`: Called by `SendNameQueryOpcodeFromDBCallBack` to locate the session associated with an account ID for sending the async response.
*   **`MapEntry` / `GridMap`**:
    *   `IsDungeon`, `LoadTerrain`, `GetHeightStatic`: Used by `HandleCorpseQueryOpcode` to calculate the correct resurrection portal coordinates if the corpse is in a dungeon.
*   **`Log`**:
    *   `Out`: Used by `HandleCreatureQueryOpcode` and `HandleGameObjectQueryOpcode` to log missing template entries.
*   **`ByteBuffer` / `WorldPacket`**:
    *   Standard serialization utilities used by all response methods to construct outgoing packets.
*   **`ObjectGuid`**:
    *   Used for constructing and serializing GUIDs in name queries.

## Data Model

This unit does not directly execute SQL queries against database tables in its active code paths.
*   `SendNameQueryOpcodeFromDB` contains commented-out code that previously queried the `characters` table (`SELECT guid, name, race, gender, class FROM characters WHERE guid = '%u'`). However, this path is disabled.
*   Active data retrieval relies on in-memory structures managed by `ObjectMgr` (DBC files, locale tables, and player caches).
*   Therefore, this unit has **no active database table dependencies**.

## Notable Implementation Details

1.  **Dead Code in Name Query**: The asynchronous database lookup for offline player names (`SendNameQueryOpcodeFromDBCallBack` and the `AsyncPQuery` call in `SendNameQueryOpcodeFromDB`) is commented out. The current implementation relies solely on `ObjectMgr`'s cache. If a player is offline and not in the cache, the name query will fail silently (no packet sent). This is a significant behavioral constraint: offline player name lookups only work if the player has logged in recently enough to remain in the `ObjectMgr` cache.
2.  **Client Version Branching**: Extensive use of `#if SUPPORTED_CLIENT_BUILD` preprocessor directives ensures compatibility across multiple WoW client versions. Key differences include:
    *   Presence of the "Realm Name" field in name queries.
    *   Structure of `SMSG_CREATURE_QUERY_RESPONSE` (e.g., `pet_spell_list_id`, `racial_leader`).
    *   Structure of `SMSG_GAMEOBJECT_QUERY_RESPONSE` (size of `raw.data` array, presence of `icon`).
3.  **Locale Handling**: Most query handlers (Creature, GameObject, NPC Text, Page Text) implement a fallback mechanism for localization. They first check if the session has a valid locale index. If so, they attempt to fetch localized strings. If the localized string is empty or unavailable, they fall back to the default (English) string. This ensures clients always receive some text, even if localization is incomplete.
4.  **Corpse Resurrection Portal Logic**: `HandleCorpseQueryOpcode` contains specific logic to handle corpses in dungeons. If the player is outside the dungeon, the server calculates the position of the entrance portal (ghost entrance) and sends that position instead of the actual corpse location. This prevents the client from trying to render a resurrection portal inside a dungeon the player cannot currently enter. It uses `GridMap::GetHeightStatic` to ensure the Z-coordinate is valid for the entrance map.
5.  **NPC Text Fallback**: In `HandleNpcTextQueryOpcode`, if a `BroadcastText` entry is missing or empty, it defaults to "Greetings $N". This prevents UI crashes or blank dialogue boxes in the client.
6.  **Page Text Chaining**: `HandlePageTextQueryOpcode` handles chained pages (where one page links to another) by looping until `next_page` is 0. It sends a separate packet for each page in the chain.

## Member Reference

**SendNameQueryOpcode**
Generates and sends `SMSG_NAME_QUERY_RESPONSE` for an online `Player`. Serializes GUID, name, realm name (client-dependent), race, gender, and class.

**SendNameQueryOpcodeFromDB**
Attempts to send `SMSG_NAME_QUERY_RESPONSE` for an offline player using `ObjectMgr`'s cache. If cache miss, no action is taken (async DB query is disabled).

**SendNameQueryOpcodeFromDBCallBack**
Static callback for the disabled async DB query. Extracts data from `QueryResult` and sends the response packet. Currently unreachable.

**HandleQueryPlayerNameOpcode**
Dispatches name query requests. Checks if player is online via `ObjectMgr`; if so, calls `SendNameQueryOpcode`, otherwise calls `SendNameQueryOpcodeFromDB`.

**HandleQueryTimeOpcode**
Handles time query requests by calling `SendQueryTimeResponse`.

**HandleCreatureQueryOpcode**
Retrieves creature template and locale data, constructs `SMSG_CREATURE_QUERY_RESPONSE` with entry, names, flags, and display ID. Handles missing entries by sending a failure packet.

**HandleGameObjectQueryOpcode**
Retrieves game object template and locale data, constructs `SMSG_GAMEOBJECT_QUERY_RESPONSE` with entry, type, display ID, name, and raw data. Handles missing entries by sending a failure packet.

**HandleCorpseQueryOpcode**
Retrieves player's corpse location. Adjusts coordinates to the dungeon entrance portal if the corpse is in a dungeon and the player is outside. Sends `MSG_CORPSE_QUERY` with location or not-found status.

**HandleNpcTextQueryOpcode**
Retrieves NPC dialogue text from `NpcText` and `BroadcastText` locales. Constructs `SMSG_NPC_TEXT_UPDATE` with up to 8 options, handling missing texts with defaults.

**HandlePageTextQueryOpcode**
Retrieves page text from `PageText` store, supporting chained pages. Sends `SMSG_PAGE_TEXT_QUERY_RESPONSE` for each page in the chain, handling missing pages with a default message.

**SendQueryTimeResponse**
Constructs and sends `SMSG_QUERY_TIME_RESPONSE` containing the current Unix timestamp.

---

<!-- machine-true, projected from graph.json -->

## Map — WorldSession.QueryHandler

*Source:* QueryHandler.cpp, WorldSession.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| SendNameQueryOpcode | method | ByteBuffer/operator<<#10, ByteBuffer/operator<<#3, Object/GetObjectGuid, ObjectGuid/operator<<, Player.Main/GetName, Unit.Main/GetClass, Unit.Main/GetGender, Unit.Main/GetRace, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | — | — |
| SendNameQueryOpcodeFromDB | method | ByteBuffer/operator<<, ByteBuffer/operator<<#10, ByteBuffer/operator<<#3, ObjectGuid/GetCounter, ObjectGuid/ObjectGuid#2, ObjectGuid/operator<<, ObjectMgr/GetPlayerDataByGUID, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | — | — |
| SendNameQueryOpcodeFromDBCallBack | method | ByteBuffer/operator<<, ByteBuffer/operator<<#10, ByteBuffer/operator<<#3, Field/GetCppString, Field/GetUInt32, Field/GetUInt8, ObjectGuid/ObjectGuid#2, ObjectGuid/operator<<, QueryResult/Fetch, World/FindSession, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | — | — |
| HandleQueryPlayerNameOpcode | method | ObjectMgr/GetPlayer | — | — |
| HandleQueryTimeOpcode | method | — | — | — |
| HandleCreatureQueryOpcode | method | ByteBuffer/append#4, ByteBuffer/operator<<#10, ByteBuffer/operator<<#7, CreatureInfo/GetTypeFlags, Log.Main/Out, ObjectGuid/GetString, ObjectMgr/GetCreatureLocale, ObjectMgr/GetCreatureTemplate, WorldPacket/WorldPacket#4, WorldSession.Main/GetSessionDbLocaleIndex, WorldSession.Main/SendPacket | — | — |
| HandleGameObjectQueryOpcode | method | ByteBuffer/append#4, ByteBuffer/operator<<, ByteBuffer/operator<<#10, ByteBuffer/operator<<#7, Log.Main/Out, ObjectGuid/GetString, ObjectMgr/GetGameObjectLocale, ObjectMgr/GetGameObjectTemplate, WorldPacket/WorldPacket#4, WorldSession.Main/GetSessionDbLocaleIndex, WorldSession.Main/SendPacket | — | — |
| HandleCorpseQueryOpcode | method | ByteBuffer/operator<<#10, ByteBuffer/operator<<#4, ByteBuffer/operator<<#7, ByteBuffer/operator<<#9, GridMap/GetHeightStatic, GridMap/LoadTerrain, MapEntry/IsDungeon, Player.Main/GetCorpse, WorldObject.Object/GetMapId, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldPacket/WorldPacket#4, WorldSession.Main/GetPlayer, WorldSession.Main/SendPacket | — | — |
| HandleNpcTextQueryOpcode | method | BroadcastText/GetText, ByteBuffer/operator<<, ByteBuffer/operator<<#10, ByteBuffer/operator<<#3, ByteBuffer/operator<<#9, ObjectMgr/GetBroadcastTextLocale, ObjectMgr/GetNpcText, WorldPacket/WorldPacket#4, WorldSession.Main/GetSessionDbLocaleIndex, WorldSession.Main/SendPacket | — | — |
| HandlePageTextQueryOpcode | method | ByteBuffer/operator<<#10, ByteBuffer/operator<<#3, ObjectMgr/GetPageTextLocale, WorldPacket/WorldPacket#4, WorldSession.Main/GetSessionDbLocaleIndex, WorldSession.Main/SendPacket | — | — |
| SendQueryTimeResponse | method | ByteBuffer/operator<<#10, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | WorldSession.GMTicketHandler/HandleGMTicketGetTicketOpcode | — |

---

<!-- verify: boundary-bleed | foreign: callback, WorldSession -->
