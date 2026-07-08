# game_Server_Packets_Guild

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Guild Packet Deserialization Unit

## Purpose & Responsibilities

This unit (`Guild.cpp` / `Guild.h`) defines the client-side packet structures for guild-related commands within the `WorldPackets::Guild` namespace. Its exclusive responsibility is **deserialization**: converting raw binary data from incoming `WorldPacket` instances into structured C++ objects. Each class corresponds to a specific `CMSG_*` or `MSG_*` opcode and inherits from `ClientPacket`.

The unit contains no business logic, validation, or database interaction. It serves as a data transport layer, exposing public member variables (e.g., player names, guild IDs, emblem colors) populated by `ReadFromWorldPacket` implementations.

## Member-by-Member Behavior

All members are either constructors initializing the packet opcode or `ReadFromWorldPacket` methods extracting data via stream operators.

### Guild Creation and Querying
*   **`GuildCreate`**: Initializes with `CMSG_GUILD_CREATE`. **`ReadFromWorldPacket#3`** extracts the `desiredGuildName` string.
*   **`GuildQuery`**: Initializes with `CMSG_GUILD_QUERY`. **`ReadFromWorldPacket#2`** extracts the `guildId` (uint32).

### Membership Management
*   **`GuildInvite`**: Initializes with `CMSG_GUILD_INVITE`. **`ReadFromWorldPacket#5`** extracts the `invitedName` string.
*   **`GuildRemove`**: Initializes with `CMSG_GUILD_REMOVE`. **`ReadFromWorldPacket#4`** extracts the `playerName` string.
*   **`GuildPromote`**: Initializes with `CMSG_GUILD_PROMOTE`. **`ReadFromWorldPacket#6`** extracts the `playerName` string.
*   **`GuildDemote`**: Initializes with `CMSG_GUILD_DEMOTE`. **`ReadFromWorldPacket#7`** extracts the `playerName` string.
*   **`GuildLeader`**: Initializes with `CMSG_GUILD_LEADER`. **`ReadFromWorldPacket#8`** extracts the `playerName` string.

### Guild Information and Notes
*   **`GuildMOTD`**: Initializes with `CMSG_GUILD_MOTD`. **`ReadFromWorldPacket#9`** checks `recv_data.empty()` before extracting the `motd` string, handling potential empty payloads.
*   **`GuildChangeInfoText`**: (Conditional: `> CLIENT_BUILD_1_8_4`) Initializes with `CMSG_GUILD_INFO_TEXT`. **`ReadFromWorldPacket#11`** extracts the `infoText` string.
*   **`GuildSetPublicNote`**: Initializes with `CMSG_GUILD_SET_PUBLIC_NOTE`. **`ReadFromWorldPacket#12`** extracts `playerName` and `note`.
*   **`GuildSetOfficerNote`**: Initializes with `CMSG_GUILD_SET_OFFICER_NOTE`. **`ReadFromWorldPacket#13`** extracts `playerName` and `note`.

### Ranks and Permissions
*   **`GuildAddRank`**: Initializes with `CMSG_GUILD_ADD_RANK`. **`ReadFromWorldPacket#14`** extracts the `rankName` string.
*   **`GuildRank`**: Initializes with `CMSG_GUILD_RANK`. **`ReadFromWorldPacket#10`** extracts `rankId`, `rights`, and `rankName`.

### Emblem Customization
*   **`SaveGuildEmblem`**: Initializes with `MSG_SAVE_GUILD_EMBLEM`. **`ReadFromWorldPacket`** extracts `vendorGuid` (via `ObjectGuid/operator>>`), followed by five `int32` values: `emblemStyle`, `emblemColor`, `borderStyle`, `borderColor`, and `backgroundColor`.

## Cross-Unit Boundaries

This unit is a leaf node in the call graph, relying on lower-level infrastructure for byte parsing.

*   **Calls Out:**
    *   **`ByteBuffer/operator>>`**: Used by most `ReadFromWorldPacket` variants to extract primitives (`uint32`, `int32`, `std::string`).
    *   **`ObjectGuid/operator>>`**: Used by **`ReadFromWorldPacket`** (in `SaveGuildEmblem`) to deserialize `vendorGuid`.
    *   **`ByteBuffer/empty`**: Used by **`ReadFromWorldPacket#9`** (in `GuildMOTD`) to validate packet content.

*   **Called By:**
    *   No external units are listed in the MAP. In practice, the central packet dispatcher invokes these methods after identifying the opcode.

## Data Model

This unit interacts with no database tables. It operates solely on in-memory network buffers.

## Notable Implementation Details

1.  **Conditional Compilation**: `GuildChangeInfoText` and **`ReadFromWorldPacket#11`** are guarded by `#if SUPPORTED_CLIENT_BUILD > CLIENT_BUILD_1_8_4`, excluding them for older clients.
2.  **Empty Packet Safety**: **`ReadFromWorldPacket#9`** is the only method checking `recv_data.empty()`, suggesting the MOTD packet may optionally be empty.
3.  **Default Initialization**: Integer members (e.g., `guildId`, `emblemStyle`) are initialized to 0 in the header, providing safe defaults if deserialization fails.

## Member Reference

*   **GuildCreate**: Constructor initializing opcode `CMSG_GUILD_CREATE`.
*   **ReadFromWorldPacket#3**: Extracts `desiredGuildName` for `GuildCreate`.
*   **ReadFromWorldPacket#2**: Extracts `guildId` for `GuildQuery`.
*   **ReadFromWorldPacket#5**: Extracts `invitedName` for `GuildInvite`.
*   **ReadFromWorldPacket#4**: Extracts `playerName` for `GuildRemove`.
*   **ReadFromWorldPacket#6**: Extracts `playerName` for `GuildPromote`.
*   **ReadFromWorldPacket#7**: Extracts `playerName` for `GuildDemote`.
*   **ReadFromWorldPacket#8**: Extracts `playerName` for `GuildLeader`.
*   **ReadFromWorldPacket#9**: Checks emptiness then extracts `motd` for `GuildMOTD`.
*   **ReadFromWorldPacket#11**: Extracts `infoText` for `GuildChangeInfoText` (client > 1.8.4).
*   **ReadFromWorldPacket#12**: Extracts `playerName` and `note` for `GuildSetPublicNote`.
*   **ReadFromWorldPacket#13**: Extracts `playerName` and `note` for `GuildSetOfficerNote`.
*   **ReadFromWorldPacket#14**: Extracts `rankName` for `GuildAddRank`.
*   **ReadFromWorldPacket#10**: Extracts `rankId`, `rights`, and `rankName` for `GuildRank`.
*   **ReadFromWorldPacket**: Extracts `vendorGuid` and five emblem style/color ints for `SaveGuildEmblem`.

---

<!-- machine-true, projected from graph.json -->

## Map — game_Server_Packets_Guild

*Source:* Guild.cpp, Guild.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ReadFromWorldPacket#3 | method | ByteBuffer/operator>> | — | — |
| ReadFromWorldPacket#9 | method | ByteBuffer/operator>>#9 | — | — |
| ReadFromWorldPacket#5 | method | ByteBuffer/operator>> | — | — |
| GuildCreate | ctor | — | — | — |
| ReadFromWorldPacket#11 | method | ByteBuffer/operator>> | — | — |
| ReadFromWorldPacket#8 | method | ByteBuffer/operator>> | — | — |
| ReadFromWorldPacket#4 | method | ByteBuffer/operator>> | — | — |
| ReadFromWorldPacket#6 | method | ByteBuffer/operator>> | — | — |
| ReadFromWorldPacket#7 | method | ByteBuffer/empty, ByteBuffer/operator>> | — | — |
| ReadFromWorldPacket#2 | method | ByteBuffer/operator>> | — | — |
| ReadFromWorldPacket#14 | method | ByteBuffer/operator>>#2, ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket#13 | method | ByteBuffer/operator>> | — | — |
| ReadFromWorldPacket#12 | method | ByteBuffer/operator>> | — | — |
| ReadFromWorldPacket | method | ByteBuffer/operator>> | — | — |
| ReadFromWorldPacket#10 | method | ByteBuffer/operator>>, ByteBuffer/operator>>#9 | — | — |
