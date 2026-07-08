# Misc

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Misc Packet Deserialization

## Purpose & Responsibilities

The `Misc` unit (`Misc.cpp` / `Misc.h`) defines a collection of C++ classes within the `WorldPackets::Misc` namespace. Each class represents a specific client-to-server network message (packet) related to miscellaneous gameplay actions, social interactions, UI updates, and system queries in the World of Warcraft emulation context.

The primary responsibility of this unit is **deserialization**: converting raw binary data received from the game client (encapsulated in a `WorldPacket`) into structured C++ objects. Every class inherits from `ClientPacket` and implements the virtual `ReadFromWorldPacket` method. This method extracts fields such as object GUIDs, coordinates, strings, and flags from the incoming packet buffer, populating the class's public member variables. These populated objects are then passed to higher-level game logic handlers (not defined in this unit) for processing.

This unit does not contain game logic, validation, or database access. It is purely a data transfer layer (DTO) definition and parsing implementation.

## Member-by-Member Behavior

The members of this unit are organized by the specific packet type they represent. Each class corresponds to a specific opcode (e.g., `CMSG_ADD_FRIEND`, `CMSG_WORLD_TELEPORT`). The behavior of each `ReadFromWorldPacket` method is strictly sequential extraction from the `WorldPacket` stream using the `>>` operator or direct `read` calls.

### Social & Interaction Packets

*   **AddFriend**: Extracts a `friendName` string. Used when a player adds another player to their friends list.
*   **DelFriend**: Extracts a `friendGuid` (ObjectGuid). Used when removing a friend.
*   **AddIgnore**: Extracts an `ignoreName` string. Used when adding a player to the ignore list.
*   **DelIgnore**: Extracts an `ignoreGuid`. Used when removing a player from the ignore list.
*   **ChatIgnored**: Extracts a `guid`. Likely used to report or handle chat filtering states for a specific user.
*   **Who**: A complex query packet for searching players. It extracts:
    *   `levelMin` and `levelMax`: Level range filters.
    *   `playerName` and `guildName`: String filters.
    *   `raceMask` and `classMask`: Bitmask filters for character attributes.
    *   `zoneIds`: A variable-length list of zone IDs the client is interested in.
    *   `searchTerms`: A variable-length list of additional string search terms.
    *   The implementation loops through the count-prefixed lists to populate `std::vector` members.

### Movement & Positioning Packets

*   **WorldTeleport**: Extracts `timeMs` (timestamp) and a `WorldLocation` structure containing `mapId`, `x`, `y`, `z`, and `o` (orientation). This packet likely requests a teleportation to specific coordinates.
*   **MoveSetRawPosition**: Extracts a `Position` structure (`x`, `y`, `z`, `o`). Notably, this class also reads the `opcode` from the packet itself (`recv_data.GetOpcode()`) because the opcode varies depending on the specific movement update type, but the payload structure is identical.
*   **SetSelection**: Extracts a `guid`. Indicates the client has selected a specific object (NPC, player, game object) in the world.
*   **SetActiveMover**: Extracts a `guid`. Indicates the client is now controlling the movement of the specified object (often used for pets or vehicles).

### Combat & Status Packets

*   **StandStateChange**: Extracts `animState`. Changes the character's standing/sitting animation state.
*   **Emote**: Extracts `emote`. Triggers a simple emote action.
*   **TextEmote**: Extracts `textEmote` (type), `emoteNum` (target ID or specific variant), and `guid` (target object). Handles complex emotes like "wave at [Player]".
*   **TogglePvP**: Extracts a boolean `state` if the packet size is 1 byte. This optional field indicates whether the player wants to enable or disable PvP mode. The use of `nonstd::optional<bool>` allows the server to distinguish between "no data sent" and "explicit false".
*   **ResurrectResponse**: Extracts `resurrectorGuid` and `accept` (boolean). The player's response to a resurrection offer.
*   **SummonResponse**: Extracts `summonerGuid`. The player's response to a summon request.
*   **ReclaimCorpse**: Extracts `guid`. Requests the retrieval of the player's corpse.

### UI & Configuration Packets

*   **SetActionButton**: Extracts `button` (slot index) and `packetData` (action ID/spell ID). Updates the action bar.
*   **SetActionBarToggles**: Extracts `actionBar`. Toggles visibility or state of action bars.
*   **FarSight**: Extracts `op`. Toggles the "far sight" camera mode.
*   **TutorialFlag**: Extracts `iFlag`. Marks a tutorial step as completed.
*   **ZoneUpdate**: Extracts `newZone`. Notifies the server of a zone change (likely for area triggers or reputation updates).
*   **RequestAccountData**: Extracts `type`. Requests specific account-wide data (e.g., macros, keybindings) from the server.
*   **UpdateAccountData**: Extracts `type`, `decompressedSize`, and the raw `compressedData` bytes. Sends updated account data back to the server. The implementation manually calculates the remaining bytes in the packet and reads them into a vector.

### Faction & Reputation Packets

*   **SetFactionAtWar**: Extracts `repListId` and `flag`. Declares war or peace with a faction.
*   **SetFactionInactive**: (Conditional compilation: `> CLIENT_BUILD_1_9_4`) Extracts `replistid` and `inactive`. Marks a faction as inactive in the UI.
*   **SetWatchedFaction**: (Conditional compilation: `> CLIENT_BUILD_1_9_4`) Extracts `repId`. Sets the faction currently watched in the reputation window.

### Inspection & Queries

*   **Inspect**: Extracts `guid`. Requests detailed stats of another player.
*   **InspectHonorStats**: Extracts `guid`. Requests honor-specific statistics.
*   **ItemTextQuery**: Extracts `itemTextId`, `mailId`, and `unk`. Requests text associated with an item (e.g., lore text).
*   **Bug**: Extracts `suggestion`, `content`, and `type`. Handles bug reports submitted by the client. Note: It skips two `uint32` length fields (`contentLen` and `typeLen`) before reading the strings, indicating the client sends length-prefixed strings but the parser relies on fixed-size reads or assumes the string terminator handles bounds.

### System & Anti-Cheat

*   **AreaTrigger**: Extracts `triggerId`. Reports entering/exiting a scripted area trigger.
*   **GameObjectUse**: Extracts `guid`. Interacts with a game object (chest, door, etc.).
*   **MeetingStoneJoin**: Extracts `guid`. Joins a raid group via a meeting stone.
*   **TeleportToUnit**: Extracts `playerName`. Requests a teleport to another player (GM command or specific feature).
*   **WardenData**: (Conditional compilation: `> CLIENT_BUILD_1_5_1`) Extracts raw binary `data`. Contains anti-cheat integrity checks sent by the Warden module. Like `UpdateAccountData`, it manually reads the remaining bytes of the packet.

## Cross-Unit Boundaries

This unit acts as a leaf node in the call graph for packet processing. It does not call into other high-level game logic units. Its dependencies are limited to low-level utility classes:

1.  **ByteBuffer**: All `ReadFromWorldPacket` methods rely heavily on `ByteBuffer` operators (`operator>>`, `read`, `rpos`, `size`).
    *   *Direction*: Misc calls ByteBuffer.
    *   *Purpose*: To extract typed data (integers, floats, strings, GUIDs) from the raw byte stream.
    *   *Specifics*:
        *   `operator>>` is used for standard types (`uint32`, `float`, `std::string`, `ObjectGuid`).
        *   `read` is used for bulk binary data (`UpdateAccountData`, `WardenData`).
        *   `rpos` and `size` are used to calculate remaining buffer lengths for bulk reads.
        *   `GetOpcode` is called on the `WorldPacket` (which inherits from or wraps `ByteBuffer`) in `MoveSetRawPosition` to determine the dynamic opcode.

2.  **ObjectGuid**: Several packets extract `ObjectGuid` structures.
    *   *Direction*: Misc calls `ObjectGuid::operator>>`.
    *   *Purpose*: To deserialize the 64-bit unique identifier for game entities.

3.  **WorldPacket**: The input parameter for all `ReadFromWorldPacket` methods.
    *   *Direction*: Misc is called by the network handler (not shown in map, but implied by `WorldPacket` argument).
    *   *Purpose*: Provides the raw data stream.

**Note on "Called By"**: The MAP shows no external units calling these members directly. In practice, these `ReadFromWorldPacket` methods are invoked by the central packet dispatcher (likely in `WorldSession` or a similar network handler unit) after a packet is received and routed based on its opcode. The dispatcher creates an instance of the appropriate `Misc` class and calls `ReadFromWorldPacket`.

## Data Model

This unit does not interact with any database tables. It operates entirely on in-memory network buffers. No SQL queries or table references exist in the source code.

## Notable Implementation Details

1.  **Conditional Compilation for Client Versions**:
    *   `SetFactionInactive`, `SetWatchedFaction`, and `WardenData` are guarded by `#if SUPPORTED_CLIENT_BUILD > ...`. This indicates the codebase supports multiple WoW client versions, and certain packets were added or changed in later builds. Maintainers must ensure these guards align with the supported client versions.

2.  **Manual Buffer Management for Binary Blobs**:
    *   `UpdateAccountData` and `WardenData` do not use the `>>` operator for their main data payload. Instead, they calculate `remaining = recv_data.size() - recv_data.rpos()` and use `recv_data.read()` to copy the rest of the packet into a `std::vector<uint8>`. This is necessary because the data is opaque binary blobs whose size is not explicitly prefixed in a way that the `>>` operator handles automatically, or simply because the entire remainder of the packet is the payload.

3.  **Dynamic Opcode Handling**:
    *   `MoveSetRawPosition` sets its own `opcode` member by calling `recv_data.GetOpcode()`. This is unusual because most packet classes have a fixed opcode defined in their constructor. This suggests that multiple different movement opcodes share the same payload structure, and the server needs to know which specific opcode was used to process the movement correctly.

4.  **Optional PvP State**:
    *   `TogglePvP` uses `nonstd::optional<bool>` for `targetState`. It only reads the boolean if `recv_data.size() == 1`. This handles cases where the client might send an empty packet (perhaps just to acknowledge) vs. a packet with explicit state. This prevents reading garbage data if the packet is empty.

5.  **String Length Skipping in Bug Report**:
    *   `Bug::ReadFromWorldPacket` uses `recv_data.read_skip<uint32>()` to skip `contentLen` and `typeLen` before reading the strings. This implies the client sends length-prefixed strings, but the `>>` operator for `std::string` might not handle the specific prefix format, or the developer chose to skip the length explicitly to avoid potential mismatch issues. This is a fragile pattern if the string encoding changes.

6.  **Variable-Length Lists in Who Query**:
    *   `Who::ReadFromWorldPacket` correctly handles variable-length arrays by first reading a count (`zonesCount`, `strCount`) and then looping to read each element. This is the standard pattern for handling lists in this codebase.

## Member Reference

**ReadFromWorldPacket#35** (WorldTeleport): Reads `timeMs` and `location` (mapId, x, y, z, o) from the packet. Uses `ByteBuffer/operator>>#8` and `#9`.

**ReadFromWorldPacket** (AddFriend): Reads `friendName` string. Uses `ByteBuffer/operator>>`.

**ReadFromWorldPacket#6** (DelFriend): Reads `friendGuid`. Uses `ObjectGuid/operator>>`.

**WorldTeleport** (Constructor): Initializes `timeMs` to 0 and sets opcode to `CMSG_WORLD_TELEPORT`.

**ReadFromWorldPacket#2** (AddIgnore): Reads `ignoreName` string. Uses `ByteBuffer/operator>>`.

**ReadFromWorldPacket#7** (DelIgnore): Reads `ignoreGuid`. Uses `ObjectGuid/operator>>`.

**ReadFromWorldPacket#26** (StandStateChange): Reads `animState`. Uses `ByteBuffer/operator>>#9`.

**ReadFromWorldPacket#3** (AreaTrigger): Reads `triggerId`. Uses `ByteBuffer/operator>>#9`.

**ReadFromWorldPacket#11** (Inspect): Reads `guid`. Uses `ObjectGuid/operator>>`.

**ReadFromWorldPacket#10** (GameObjectUse): Reads `guid`. Uses `ObjectGuid/operator>>`.

**ReadFromWorldPacket#8** (Emote): Reads `emote`. Uses `ByteBuffer/operator>>#9`.

**ReadFromWorldPacket#29** (TextEmote): Reads `textEmote`, `emoteNum`, and `guid`. Uses `ByteBuffer/operator>>#9` and `ObjectGuid/operator>>`.

**ReadFromWorldPacket#24** (SetSelection): Reads `guid`. Uses `ObjectGuid/operator>>`.

**ReadFromWorldPacket#9** (FarSight): Reads `op`. Uses `ByteBuffer/operator>>#6`.

**ReadFromWorldPacket#31** (TutorialFlag): Reads `iFlag`. Uses `ByteBuffer/operator>>#9`.

**ReadFromWorldPacket#20** (SetActionButton): Reads `button` and `packetData`. Uses `ByteBuffer/operator>>#6` and `#9`.

**ReadFromWorldPacket#12** (InspectHonorStats): Reads `guid`. Uses `ObjectGuid/operator>>`.

**ReadFromWorldPacket#21** (SetActiveMover): Reads `guid`. Uses `ObjectGuid/operator>>`.

**ReadFromWorldPacket#22** (SetFactionAtWar): Reads `repListId` and `flag`. Uses `ByteBuffer/operator>>#6` and `#9`.

**ReadFromWorldPacket#23** (SetFactionInactive): Reads `replistid` and `inactive`. Uses `ByteBuffer/operator>>#6` and `#9`. Conditional on client build > 1.9.4.

**ReadFromWorldPacket#36** (ZoneUpdate): Reads `newZone`. Uses `ByteBuffer/operator>>#9`.

**ReadFromWorldPacket#16** (ReclaimCorpse): Reads `guid`. Uses `ObjectGuid/operator>>`.

**ReadFromWorldPacket#5** (ChatIgnored): Reads `guid`. Uses `ObjectGuid/operator>>`.

**ReadFromWorldPacket#18** (ResurrectResponse): Reads `resurrectorGuid` and `accept`. Uses `ByteBuffer/operator>>#5` and `ObjectGuid/operator>>`.

**ReadFromWorldPacket#13** (ItemTextQuery): Reads `itemTextId`, `mailId`, and `unk`. Uses `ByteBuffer/operator>>#9`.

**ReadFromWorldPacket#27** (SummonResponse): Reads `summonerGuid`. Uses `ObjectGuid/operator>>`.

**ReadFromWorldPacket#19** (SetActionBarToggles): Reads `actionBar`. Uses `ByteBuffer/operator>>#6`.

**ReadFromWorldPacket#14** (MeetingStoneJoin): Reads `guid`. Uses `ObjectGuid/operator>>`.

**ReadFromWorldPacket#28** (TeleportToUnit): Reads `playerName`. Uses `ByteBuffer/operator>>`.

**ReadFromWorldPacket#17** (RequestAccountData): Reads `type`. Uses `ByteBuffer/operator>>#9`.

**ReadFromWorldPacket#32** (UpdateAccountData): Reads `type` and `decompressedSize`. Calculates remaining bytes using `size()` and `rpos()`, then reads raw bytes into `compressedData` using `read()`. Uses `ByteBuffer/operator>>#9`, `ByteBuffer/read`, `ByteBuffer/rpos`, `ByteBuffer/size`.

**ReadFromWorldPacket#25** (SetWatchedFaction): Reads `repId`. Uses `ByteBuffer/operator>>#2`. Conditional on client build > 1.9.4.

**ReadFromWorldPacket#15** (MoveSetRawPosition): Reads `opcode` from packet, then `location` (x, y, z, o). Uses `ByteBuffer/operator>>#8` and `WorldPacket/GetOpcode`.

**ReadFromWorldPacket#30** (TogglePvP): Checks packet size. If 1, reads `state` into `targetState`. Uses `ByteBuffer/operator>>#5` and `ByteBuffer/size`.

**ReadFromWorldPacket#34** (Who): Reads level ranges, names, masks. Loops to read `zoneIds` and `searchTerms` vectors. Uses `ByteBuffer/operator>>` and `#9`.

**ReadFromWorldPacket#4** (Bug): Reads `suggestion`. Skips `contentLen` and `typeLen` using `read_skip`. Reads `content` and `type`. Uses `ByteBuffer/operator>>` and `#9`.

**ReadFromWorldPacket#33** (WardenData): Calculates remaining bytes using `size()` and `rpos()`, then reads raw bytes into `data` using `read()`. Uses `ByteBuffer/read`, `ByteBuffer/rpos`, `ByteBuffer/size`. Conditional on client build > 1.5.1.

---

<!-- machine-true, projected from graph.json -->

## Map — Misc

*Source:* Misc.cpp, Misc.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ReadFromWorldPacket#35 | method | ByteBuffer/operator>>#8, ByteBuffer/operator>>#9 | — | — |
| ReadFromWorldPacket | method | ByteBuffer/operator>> | — | — |
| ReadFromWorldPacket#6 | method | ObjectGuid/operator>> | — | — |
| WorldTeleport | ctor | — | — | — |
| ReadFromWorldPacket#2 | method | ByteBuffer/operator>> | — | — |
| ReadFromWorldPacket#7 | method | ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket#26 | method | ByteBuffer/operator>>#9 | — | — |
| ReadFromWorldPacket#3 | method | ByteBuffer/operator>>#9 | — | — |
| ReadFromWorldPacket#11 | method | ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket#10 | method | ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket#8 | method | ByteBuffer/operator>>#9 | — | — |
| ReadFromWorldPacket#29 | method | ByteBuffer/operator>>#9, ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket#24 | method | ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket#9 | method | ByteBuffer/operator>>#6 | — | — |
| ReadFromWorldPacket#31 | method | ByteBuffer/operator>>#9 | — | — |
| ReadFromWorldPacket#20 | method | ByteBuffer/operator>>#6, ByteBuffer/operator>>#9 | — | — |
| ReadFromWorldPacket#12 | method | ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket#21 | method | ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket#22 | method | ByteBuffer/operator>>#6, ByteBuffer/operator>>#9 | — | — |
| ReadFromWorldPacket#23 | method | ByteBuffer/operator>>#6, ByteBuffer/operator>>#9 | — | — |
| ReadFromWorldPacket#36 | method | ByteBuffer/operator>>#9 | — | — |
| ReadFromWorldPacket#16 | method | ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket#5 | method | ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket#18 | method | ByteBuffer/operator>>#5, ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket#13 | method | ByteBuffer/operator>>#9 | — | — |
| ReadFromWorldPacket#27 | method | ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket#19 | method | ByteBuffer/operator>>#6 | — | — |
| ReadFromWorldPacket#14 | method | ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket#28 | method | ByteBuffer/operator>> | — | — |
| ReadFromWorldPacket#17 | method | ByteBuffer/operator>>#9 | — | — |
| ReadFromWorldPacket#32 | method | ByteBuffer/operator>>#9, ByteBuffer/read, ByteBuffer/rpos, ByteBuffer/size | — | — |
| ReadFromWorldPacket#25 | method | ByteBuffer/operator>>#2 | — | — |
| ReadFromWorldPacket#15 | method | ByteBuffer/operator>>#8, WorldPacket/GetOpcode | — | — |
| ReadFromWorldPacket#30 | method | ByteBuffer/operator>>#5, ByteBuffer/size | — | — |
| ReadFromWorldPacket#34 | method | ByteBuffer/operator>>, ByteBuffer/operator>>#9 | — | — |
| ReadFromWorldPacket#4 | method | ByteBuffer/operator>>, ByteBuffer/operator>>#9 | — | — |
| ReadFromWorldPacket#33 | method | ByteBuffer/read, ByteBuffer/rpos, ByteBuffer/size | — | — |
