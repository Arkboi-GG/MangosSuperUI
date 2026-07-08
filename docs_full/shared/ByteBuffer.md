# ByteBuffer

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ByteBuffer

**Purpose & Responsibilities**

`ByteBuffer` is the fundamental binary serialization and deserialization container for the WoWVMaNGOS server. It acts as a growable byte array with independent read (`_rpos`) and write (`_wpos`) cursors, enabling the construction of network packets for transmission to clients and the parsing of incoming packets from clients.

Key responsibilities include:
1.  **Packet Construction:** Providing stream-like operators (`operator<<`) to serialize primitive types (integers, floats, strings, GUIDs) into a contiguous byte buffer, handling endianness conversion automatically via `EndianConvert`.
2.  **Packet Parsing:** Providing stream-like operators (`operator>>`) and direct read methods to deserialize bytes back into C++ types, advancing the read cursor appropriately.
3.  **Memory Management:** Managing underlying storage (`std::vector<uint8>`) with automatic resizing during writes and bounds checking during reads/writes to prevent buffer overflows.
4.  **Specialized Encoding:** Implementing World of Warcraft-specific encoding schemes, such as packed GUIDs (`appendPackGUID`/`readPackGUID`) and packed XYZ coordinates (`appendPackXYZ`).
5.  **Debugging Support:** Offering utilities like `hexlike()` to dump buffer contents for debugging and `ByteBufferException` to report position errors during serialization/deserialization.

The class is heavily used across the entire codebase, from low-level socket handling (`WorldSocket`, `AuthSocket`) to high-level game logic (`Player`, `Spell`, `Unit`, `WorldSession`). It does not interact with any database tables.

## Member-by-Member Behavior

### Initialization and Lifecycle

*   **Constructors (`ByteBuffer`, `ByteBuffer#2`, `ByteBuffer#3`, `ByteBuffer#4`):** Initialize the buffer with default or specified capacity. The default constructor reserves `DEFAULT_SIZE` (0x1000 bytes). Copy and move constructors/assignment operators handle standard C++ object lifecycle semantics.
*   **`from` / `from#2`:** Static factory methods that create a `ByteBuffer` from an existing `std::vector<uint8>`. They set `_rpos` to 0 and `_wpos` to the vector's size, effectively treating the vector as a pre-filled packet ready for reading.
*   **`clear`:** Resets the buffer by clearing the underlying storage and setting both `_rpos` and `_wpos` to 0. This allows reuse of the same `ByteBuffer` instance for multiple packets.
*   **`resize`:** Resizes the underlying storage to `newsize`, resetting `_rpos` to 0 and `_wpos` to the new size. This is typically used when receiving raw data from sockets.
*   **`reserve`:** Ensures the underlying storage has enough capacity for `ressize` bytes without necessarily changing the current size or positions.

### Writing Data (Serialization)

*   **`append` variants:** Core methods for adding raw bytes or typed data to the buffer.
    *   `append(uint8 const*, size_t)`: Copies raw bytes from `src` to the current write position, resizing storage if necessary, and advances `_wpos`.
    *   `append(T value)` (private template): Converts `value` to endianness and appends its raw bytes. Used by `operator<<`.
    *   `append(std::string)`, `append(std::vector<uint8>)`, `append(std::array)`, `append(char const*, size_t)`: Convenience wrappers that delegate to the raw byte append.
    *   `append(ByteBuffer const&)`: Appends the contents of another buffer up to its write position.
*   **`operator<<` variants:** Stream-style insertion operators for various types. Each converts the value to network endianness (via `EndianConvert` inside the private `append` template) and appends it.
    *   Primitives: `uint8`, `uint16`, `uint32`, `uint64`, `int8`, `int16`, `int32`, `int64`, `float`, `double`, `bool`.
    *   Strings: `std::string` and `char const*` append the characters followed by a null terminator.
    *   Containers: Free-function templates (defined in the header) exist for `std::vector`, `std::list`, and `std::map`, which serialize the size followed by each element.
*   **`appendPackGUID`:** Encodes a 64-bit GUID using the WoW protocol's variable-length packing scheme. It creates a bitmask indicating which bytes are non-zero, then appends the bitmask byte followed by the non-zero bytes.
*   **`appendPackXYZ`:** Packs three float coordinates (x, y, z) into a single 32-bit integer using fixed-point arithmetic (multiplying by 4.0f and rounding) and bit-shifting. This is a lossy compression specific to movement packets.
*   **`put`:** Writes raw bytes or a converted value at a *specific* absolute position `pos` in the buffer, without advancing `_wpos`. This is used for patching headers or metadata after the main content is written. Bounds checking throws `ByteBufferException` if `pos + cnt` exceeds the current buffer size.

### Reading Data (Deserialization)

*   **`read` variants:** Core methods for extracting data.
    *   `read<T>()`: Reads a value of type `T` at the current `_rpos`, advances `_rpos` by `sizeof(T)`, performs endianness conversion, and returns the value. Throws `ByteBufferException` if insufficient data remains.
    *   `read<T>(size_t pos)`: Reads a value of type `T` at an absolute position `pos` without advancing `_rpos`. Used by `operator[]`.
    *   `read(uint8*, size_t)`: Copies raw bytes from the current `_rpos` to `dest`, advancing `_rpos`.
*   **`operator>>` variants:** Stream-style extraction operators. They delegate to `read<T>()` for primitives and strings.
    *   Strings: `operator>>(std::string&)` reads bytes until a null terminator is found, assigning them to the string. It includes bounds checking to prevent crashes on malformed packets.
    *   Containers: Free-function templates (defined in the header) read the size first, then loop to read each element.
*   **`ReadCString`:** Reads a null-terminated string from the current `_rpos`, advancing `_rpos` past the null terminator. Returns a pointer to the string data *within* the buffer's storage. This avoids copying but requires caution as the pointer becomes invalid if the buffer is resized or cleared.
*   **`ReadCString(char*&, size_t&)`:** Variant that also calculates and returns the length of the string (excluding the null terminator).
*   **`readPackGUID`:** Decodes a packed GUID. It reads a bitmask byte, then iterates through the 8 possible bytes, reading only those indicated by the mask bits, and reconstructs the 64-bit GUID.
*   **`read_skip` / `read_skip#2` / `read_skip#3` / `read_skip#4`:** Advances `_rpos` by a specified number of bytes or `sizeof(T)`. Used to ignore unknown or unused fields in packets. Includes bounds checking.
*   **`rfinish`:** Sets `_rpos` to `_wpos`, effectively marking the buffer as fully read.

### Position and State Management

*   **`rpos` / `rpos#2`:** Get or set the read position `_rpos`. Setting it allows random access for parsing.
*   **`wpos` / `wpos#2`:** Get or set the write position `_wpos`. Setting it allows overwriting or truncating the logical content without clearing storage.
*   **`size`:** Returns the total allocated size of the underlying storage (`_storage.size()`). Note: This is not necessarily the amount of valid data written; `_wpos` indicates that.
*   **`empty`:** Checks if the underlying storage is empty.
*   **`contents`:** Returns a pointer to the beginning of the underlying storage. Used for efficient bulk operations or passing raw data to sockets.
*   **`operator[]`:** Provides read-only access to a byte at a specific position via `read<uint8>(pos)`.

### Error Handling and Debugging

*   **`ByteBufferException`:** A helper class that stores error context (`add` flag, position, expected size, actual size). Its constructor calls `PrintPosError`.
*   **`PrintPosError`:** Logs an error message detailing whether the operation was a read ("get") or write ("put"), the position, the expected size, and the actual buffer size. Called by `ByteBufferException` constructor and potentially other error paths.
*   **`hexlike`:** Dumps the entire buffer contents in hexadecimal format, formatted in rows of 16 bytes with separators every 8 bytes. Output is conditional on the log level being `DEBUG` or higher. Useful for debugging packet structures.

## Cross-Unit Boundaries

`ByteBuffer` is a foundational utility, so it has extensive cross-unit interactions.

*   **Called By (Consumers):** Virtually every system that sends or receives network data uses `ByteBuffer`.
    *   **Network Layer:** `WorldSocket` and `AuthSocket` use it to build outgoing packets and parse incoming ones.
    *   **Game Logic:** `Player`, `Unit`, `Spell`, `Creature`, `GameObject`, `WorldSession` handlers use it extensively to construct complex game state updates, chat messages, quest data, spell effects, etc.
    *   **Anti-Cheat:** `WardenWin`, `WardenMac`, `Log.Warden` use it for challenge-response protocols and scan data.
    *   **Social/Group:** `game_Group_Group`, `game_Guild_Guild`, `SocialMgr` use it for party/guild updates.
    *   **Debugging:** `ChatHandler.DebugCommands` uses it to send test opcodes and data.
*   **Calls Out (Dependencies):**
    *   **Logging:** `PrintPosError` and `hexlike` call `Log.Main/Out` and related log level checks.
    *   **Error Handling:** `append` calls `Errors/PrintStacktraceAndThrow` (though the provided source shows `MANGOS_ASSERT` and implicit exceptions via `ByteBufferException`; the map indicates a dependency on the Errors unit for stack traces in some contexts).
    *   **Utilities:** Uses `ByteConverter/EndianConvert` (included via `Utilities/ByteConverter.h`) for endianness swaps.

## Data Model

`ByteBuffer` does not interact with any database tables. It operates entirely in memory on binary data streams.

## Notable Implementation Details

1.  **Endianness Conversion:** All typed reads and writes go through `EndianConvert`. This ensures that the server (likely little-endian on x86/x64) correctly serializes data to the network byte order (big-endian for WoW protocol) and vice versa. The ARM-specific code in `read<T>` uses `memcpy` to avoid strict aliasing or alignment issues, whereas x86/x64 directly casts pointers.
2.  **Packed GUIDs:** The `appendPackGUID` and `readPackGUID` methods implement a variable-length encoding for GUIDs. This saves bandwidth by omitting zero bytes. The bitmask byte determines which of the 8 subsequent bytes are present. This is a critical optimization for the WoW protocol.
3.  **Packed XYZ:** `appendPackXYZ` uses fixed-point math to pack three floats into one `uint32`. This is lossy and reduces precision, suitable for frequent movement updates where high precision is less critical than bandwidth.
4.  **String Handling:** `operator<<` for strings appends a null terminator. `operator>>` for strings reads until a null terminator. `ReadCString` returns a pointer into the buffer, which is efficient but dangerous if the buffer is modified. Users must ensure the buffer remains valid while using the returned pointer.
5.  **Bounds Checking:** `read` and `put` throw `ByteBufferException` if the operation would exceed the buffer bounds. This prevents buffer overflows and helps catch protocol mismatches. `append` grows the buffer dynamically, so it rarely throws unless memory is exhausted.
6.  **Read/Write Separation:** The separate `_rpos` and `_wpos` allow the buffer to be used as a queue or for partial reads/writes. For example, a packet might be built (writing), then partially read (parsing headers), then the rest read later.
7.  **Template Specializations:** The header provides template specializations for `read_skip<char*>`, `read_skip<char const*>`, and `read_skip<std::string>` to correctly skip null-terminated strings by reading them into a temporary string. This is necessary because `sizeof(char*)` is not the length of the string.
8.  **Assertion in Append:** `append(uint8 const*, size_t)` contains `MANGOS_ASSERT(size() < 10000000)`. This is a sanity check to prevent accidentally creating extremely large buffers, which could indicate a bug or malicious input.

## Member Reference

**PrintPosError**: Logs an error message detailing a read/write position error in the buffer, including operation type, position, expected size, and actual size. Called by `ByteBufferException` constructor.

**append#5**: Appends raw bytes from a source pointer to the buffer, resizing storage if necessary, and advances the write position. Throws `ByteBufferException` if bounds are exceeded (though dynamic resizing usually prevents this for writes). Called by many packet-building functions.

**ByteBufferException**: Constructor initializes error context and calls `PrintPosError`. Used to signal read/write failures due to insufficient buffer space.

**hexlike**: Dumps the buffer contents in hexadecimal format for debugging, conditional on log level. Formats output in rows of 16 bytes.

**ByteBuffer**: Default constructor initializes read/write positions to 0 and reserves default storage size.

**ByteBuffer#4**: Constructor initializing buffer with a specified reserved size.

**ByteBuffer#3**: Copy constructor, duplicating positions and storage from another buffer.

**ByteBuffer#2**: Move constructor, transferring ownership of storage and positions from another buffer.

**operator=**: Move assignment operator, transferring ownership of storage and positions from another buffer.

**from#2**: Static factory method creating a buffer from an rvalue `std::vector<uint8>`, moving the vector's data into the buffer.

**from**: Static factory method creating a buffer from an lvalue `std::vector<uint8>`, copying the vector's data into the buffer.

**clear**: Clears the buffer storage and resets read/write positions to 0.

**operator<<#7**: Inserts a `uint8` value into the buffer, converting endianness and appending bytes.

**operator<<#13**: Inserts a `uint16` value into the buffer, converting endianness and appending bytes.

**operator<<#10**: Inserts a `uint32` value into the buffer, converting endianness and appending bytes.

**operator<<#11**: Inserts a `uint64` value into the buffer, converting endianness and appending bytes.

**operator<<#12**: Inserts a `time_t` value (on MinGW) into the buffer, converting endianness and appending bytes.

**operator<<#6**: Inserts an `int8` value into the buffer, converting endianness and appending bytes.

**operator<<#4**: Inserts an `int16` value into the buffer, converting endianness and appending bytes.

**operator<<#5**: Inserts an `int32` value into the buffer, converting endianness and appending bytes.

**operator<<#9**: Inserts an `int64` value into the buffer, converting endianness and appending bytes.

**operator<<#8**: Inserts a `float` value into the buffer, converting endianness and appending bytes.

**operator<<**: Inserts a `double` value into the buffer, converting endianness and appending bytes.

**operator<<#3**: Inserts a `std::string` into the buffer, appending characters followed by a null terminator.

**operator<<#2**: Inserts a `char const*` into the buffer, appending characters followed by a null terminator.

**operator>>#5**: Extracts a `bool` value from the buffer, reading a byte and checking if it is non-zero.

**operator>>#6**: Extracts a `uint8` value from the buffer, reading bytes and converting endianness.

**operator>>#12**: Extracts a `uint16` value from the buffer, reading bytes and converting endianness.

**operator>>#9**: Extracts a `uint32` value from the buffer, reading bytes and converting endianness.

**operator>>#10**: Extracts a `uint64` value from the buffer, reading bytes and converting endianness.

**operator>>#11**: Extracts a `time_t` value (on MinGW) from the buffer, reading bytes and converting endianness.

**operator>>#4**: Extracts an `int8` value from the buffer, reading bytes and converting endianness.

**operator>>#2**: Extracts an `int16` value from the buffer, reading bytes and converting endianness.

**operator>>#3**: Extracts an `int32` value from the buffer, reading bytes and converting endianness.

**operator>>#8**: Extracts an `int64` value from the buffer, reading bytes and converting endianness.

**operator>>#7**: Extracts a `float` value from the buffer, reading bytes and converting endianness.

**operator>>**: Extracts a `double` value from the buffer, reading bytes and converting endianness.

**operator[]**: Returns a `uint8` at a specific absolute position without advancing the read position.

**rpos**: Gets the current read position.

**rpos#2**: Sets the current read position to a specified value.

**wpos**: Gets the current write position.

**wpos#2**: Sets the current write position to a specified value.

**read_skip**: Advances the read position by `sizeof(T)` bytes, with bounds checking.

**rfinish**: Sets the read position to the write position, marking the buffer as fully read.

**read**: Reads a value of type `T` at the current read position, advances the read position, converts endianness, and returns the value. Throws `ByteBufferException` if bounds are exceeded.

**ReadCString**: Reads a null-terminated string from the current read position, advances the read position past the null terminator, and returns a pointer to the string data within the buffer.

**ReadCString#2**: Reads a null-terminated string, returning a pointer and its length (excluding null terminator).

**readPackGUID**: Reads a packed GUID from the buffer, decoding the bitmask and variable-length bytes to reconstruct the 64-bit GUID.

**contents**: Returns a pointer to the beginning of the underlying storage.

**size**: Returns the total allocated size of the underlying storage.

**empty**: Checks if the underlying storage is empty.

**resize**: Resizes the underlying storage, resetting read position to 0 and write position to the new size.

**reserve**: Ensures the underlying storage has enough capacity for the specified size.

**append**: Appends a `std::string` to the buffer, including the null terminator.

**append#2**: Appends a `std::vector<uint8>` to the buffer.

**append#4**: Appends a `std::array<uint8, Size>` to the buffer.

**append#3**: Appends raw bytes from a `char const*` source to the buffer.

**appendPackGUID**: Encodes a 64-bit GUID into a variable-length packed format and appends it to the buffer.

**appendPackXYZ**: Packs three float coordinates into a single 32-bit integer using fixed-point arithmetic and appends it to the buffer.

**put**: Writes raw bytes or a converted value at a specific absolute position in the buffer, without advancing the write position. Throws `ByteBufferException` if bounds are exceeded.

**read_skip#4**: Template specialization to skip a `char*` by reading it as a string.

**read_skip#3**: Template specialization to skip a `char const*` by reading it as a string.

**read_skip#2**: Template specialization to skip a `std::string` by reading it as a string.

---

<!-- machine-true, projected from graph.json -->

## Map — ByteBuffer

*Source:* ByteBuffer.cpp, ByteBuffer.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| PrintPosError | method | Log.Main/Out | — | — |
| append#5 | method | Errors/PrintStacktraceAndThrow | AddonHandler/BuildAddonPacket, AuthSocket/_HandleLogonChallenge, AuthSocket/_HandleReconnectChallenge, Log.Warden/RequestChallenge, Log.Warden/SendModuleToClient, Log.Warden/SendModuleUse, UpdateData/AddPacket, WardenScan/WindowsCodeScan, WardenScan/WindowsDriverScan, WardenScan/WindowsHookScan, WardenScan/WindowsModuleScan, WardenScan/WindowsModuleScan#2, WardenWin/LoadScriptedScans, WorldObject.Object/BuildValuesUpdate, WorldObject.Object/DirectSendPublicValueUpdate#2, WorldSession.Main/SendAccountDataTimes, WorldSocket/HandleResultOfAsyncWrite | — |
| ByteBufferException | ctor | — | SpellCastTargetsInfo/read | — |
| hexlike | method | Log.Main/HasLogLevelOrHigher, Log.Main/IsIncludeTime, Log.Main/Out | ChatHandler.DebugCommands/HandleDebugSendOpcodeCommand, WorldSocket/_HandleCompleteReceivedPacket | — |
| ByteBuffer | ctor | — | AddonHandler/BuildAddonPacket, AuthSocket/GenerateLogonProofResponse, AuthSocket/_HandleLogonProof__PostRecv, AuthSocket/_HandleLogonProof__PostRecv_HandleInvalidVersion, AuthSocket/_HandleRealmList, Log.Warden/RequestScans, Pet.Main/_LoadSpellCooldowns, Player.Main/LockOutSpells, WardenWin/InitializeClient | — |
| ByteBuffer#4 | ctor | — | Log.Warden/RequestChallenge, Log.Warden/SendModuleToClient, Log.Warden/SendModuleUse, UpdateData/BuildPacket#2, WardenWin/BuildFileHashInit, WardenWin/BuildLuaInit, WardenWin/BuildTimingInit, WardenWin/InitializeClient | — |
| ByteBuffer#3 | ctor | — | — | — |
| ByteBuffer#2 | ctor | — | — | — |
| operator= | method | — | WardenWin/BuildFileHashInit, WardenWin/BuildLuaInit, WardenWin/BuildTimingInit | — |
| from#2 | method | — | — | — |
| from | method | — | Log.Warden/Update | — |
| clear | method | — | game_Chat_Channel/Invite, game_Chat_Channel/Join, game_Chat_Channel/Leave, MasterPlayer.Chat/Whisper, UpdateData/Send, WardenMac/Update, WardenWin/Update, WorldSocket/HandleResultOfAsyncWrite | — |
| operator<<#7 | method | — | AddonHandler/BuildAddonPacket, AuthSocket/LoadRealmlistAndWriteIntoBuffer, AuthSocket/_HandleLogonChallenge, AuthSocket/_HandleLogonProof__PostRecv, AuthSocket/_HandleLogonProof__PostRecv_HandleInvalidVersion, AuthSocket/_HandleRealmList, AuthSocket/_HandleReconnectChallenge, AuthSocket/_HandleReconnectProof, BattleGroundMgr/BuildBattleGroundListPacket, BattleGroundMgr/BuildBattleGroundStatusPacket, BattleGroundMgr/BuildPvpLogDataPacket, ChatHandler.Chat/BuildChatPacket, ChatHandler.DebugCommands/HandleDebugMonsterChatCommand, ChatHandler.DebugCommands/HandleDebugSendChannelNotifyCommand, ChatHandler.DebugCommands/HandleDebugSendOpcodeCommand, ChatHandler.DebugCommands/HandleDebugSendSpellFailCommand, ChatHandler.DebugCommands/HandleDebugSpellModsCommand, game_Chat_Channel/List, game_Chat_Channel/MakeModeChange, game_Chat_Channel/MakeNotifyPacket, game_Chat_Channel/MakeNotOnPacket, game_Group_Group/MasterLoot, game_Group_Group/RemoveMember, game_Group_Group/SendLootRoll, game_Group_Group/SendLootRollWon, game_Group_Group/SendTargetIconList, game_Group_Group/SendUpdate, game_Group_Group/SetTargetIcon, game_Guild_Guild/BroadcastEvent, game_Guild_Guild/Query, game_Guild_Guild/Roster, GMTicketMgr/WritePacket, GossipDef/SendGossipMenu, GossipDef/SendQuestGiverQuestList, LFGMgr/BuildSetQueuePacket, Log.Warden/RequestChallenge, Log.Warden/RequestScans, Log.Warden/SendModuleToClient, Log.Warden/SendModuleUse, LootMgr/operator<<#2, Map.Main/RemoveCorpses, MoveSplineInit/Launch, packet_builder/WriteCommonMonsterMovePart, Pet.Main/SetEnabled, Player.Main/BuildEnchantmentLog, Player.Main/BuildEnumData, Player.Main/CharmSpellInitialize, Player.Main/DuelComplete, Player.Main/PetSpellInitialize, Player.Main/PossessSpellInitialize, Player.Main/RefreshBitsForVisibleUnits, Player.Main/RemovedInsignia, Player.Main/SendBuyError, Player.Main/SendEquipError, Player.Main/SendFactionAtWar, Player.Main/SendInitialSpells, Player.Main/SendLogXPGain, Player.Main/SendLoot, Player.Main/SendLootError, Player.Main/SendLootRelease, Player.Main/SendMirrorTimerPause, Player.Main/SendMirrorTimerStart, Player.Main/SendNewItem, Player.Main/SendNotifyLootItemRemoved, Player.Main/SendPetTameFailure, Player.Main/SendProficiency, Player.Main/SendPushToPartyResponse, Player.Main/SendSellError, Player.Main/SendSpellMod, Player.Main/SendTransferAborted, Player.Main/SetClientControl, ReputationMgr/SendInitialReputations, SocialMgr/MakeFriendStatusPacket, SocialMgr/SendFriendList, SocialMgr/SendFriendStatus, SocialMgr/SendIgnoreList, Spell.Main/SendAllTargetsMiss, Spell.Main/SendCastResult#2, Spell.Main/SendLogExecute, Spell.Main/SendResurrectRequest, Spell.Main/WriteSpellGoTargets, SpellCaster/SendHealSpellLog, SpellCaster/SendSpellDamageResist, SpellCaster/SendSpellMiss, SpellCaster/SendSpellNonMeleeDamageLog, SpellCaster/SendSpellOrDamageImmune, SpellCastTargetsInfo/write, Unit.Main/SendAttackStateUpdate, Unit.Main/SendEnvironmentalDamageLog, Unit.Main/SendPetActionFeedback, Unit.Main/SendPetCastFail, Unit.Main/SendSpellGo, Unit.Main/SetStandState, Unit.SpellAuras/UpdateAuraDuration, UpdateData/AddPacket, UpdateData/BuildPacket#2, WardenScan/GetBuilder, WardenScan/WindowsCodeScan, WardenScan/WindowsDriverScan, WardenScan/WindowsFileHashScan, WardenScan/WindowsHookScan, WardenScan/WindowsLuaScan, WardenScan/WindowsLuaScan#2, WardenScan/WindowsLuaScan#3, WardenScan/WindowsMemoryScan, WardenScan/WindowsMemoryScan#2, WardenScan/WindowsMemoryScan#3, WardenScan/WindowsMemoryScan#4, WardenScan/WindowsModuleScan, WardenScan/WindowsModuleScan#2, WardenScan/WindowsTimeScan, WardenWin/BuildFileHashInit, WardenWin/BuildLuaInit, WardenWin/BuildTimingInit, WardenWin/LoadScriptedScans, Weather/SendWeatherForPlayersInZone, Weather/SendWeatherUpdateToPlayer, World/AddQueuedSession, World/AddSession_, WorldObject.Object/BuildCreateUpdateBlockForPlayer, WorldObject.Object/BuildMovementUpdate, WorldObject.Object/BuildMovementUpdateBlock, WorldObject.Object/BuildValuesUpdate, WorldObject.Object/BuildValuesUpdateBlockForPlayer, WorldObject.Object/DirectSendPublicValueUpdate#2, WorldSession.BattleGroundHandler/HandleBattleGroundPlayerPositionsOpcode, WorldSession.ChannelHandler/HandleJoinChannelOpcode, WorldSession.CharacterHandler/HandleChangePlayerNameOpcodeCallBack, WorldSession.CharacterHandler/HandleCharCreateOpcode, WorldSession.CharacterHandler/HandleCharDeleteOpcode, WorldSession.CharacterHandler/HandleCharEnum, WorldSession.CharacterHandler/HandleCharRenameOpcode, WorldSession.CharacterHandler/HandlePlayerLogin, WorldSession.CharacterHandler/HandlePlayerLoginOpcode, WorldSession.ChatHandler/operator(), WorldSession.GroupHandler/BuildPartyMemberStatsPacket, WorldSession.GroupHandler/HandleRaidReadyCheckOpcode, WorldSession.GroupHandler/HandleRequestPartyMemberStatsOpcode, WorldSession.ItemHandler/HandleReadItemOpcode, WorldSession.ItemHandler/SendListInventory, WorldSession.LFGHandler/SendMeetingstoneFailed, WorldSession.LFGHandler/SendMeetingstoneSetqueue, WorldSession.MailHandler/HandleGetMailList, WorldSession.Main/SendAuthWaitQue, WorldSession.MiscHandler/HandleInspectHonorStatsOpcode, WorldSession.MiscHandler/HandleLogoutRequestOpcode, WorldSession.MiscHandler/HandleZoneUpdateOpcode, WorldSession.NPCHandler/SendStablePet, WorldSession.NPCHandler/SendStableResult, WorldSession.NPCHandler/SendTrainerSpellHelper, WorldSession.PetitionsHandler/HandleOfferPetitionOpcode, WorldSession.PetitionsHandler/HandlePetitionQueryOpcode, WorldSession.PetitionsHandler/HandlePetitionShowSignOpcode, WorldSession.PetitionsHandler/SendPetitionShowList, WorldSession.QueryHandler/HandleCorpseQueryOpcode, WorldSession.QueryHandler/HandleCreatureQueryOpcode, WorldSession.QueryHandler/HandleGameObjectQueryOpcode, WorldSession.QuestHandler/HandleQuestPushResult, WorldSession.SpellHandler/HandleCastSpellOpcode, WorldSession.TaxiHandler/SendLearnNewTaxiNode, WorldSession.TaxiHandler/SendTaxiStatus, WorldSession.TradeHandler/SendTradeStatus, WorldSession.TradeHandler/SendUpdateTrade, WorldSocket/_HandleAuthSession | — |
| operator<<#13 | method | — | AuthSocket/LoadRealmlistAndWriteIntoBuffer, AuthSocket/_HandleRealmList, ChatHandler.DebugCommands/HandleDebugSendOpcodeCommand, Log.Warden/SendModuleToClient, Player.Main/AddSpell, Player.Main/RemoveSpell, Player.Main/SendInitialSpells, Player.Main/SendInitWorldStates, Player.Main/SendSpellRemoved, Spell.Main/SendSpellGo, Spell.Main/SendSpellStart, SpellCastTargetsInfo/write, Unit.Main/SendSpellGo, Unit.Main/WritePetSpellsCooldown, UpdateData/AddPacket, WardenWin/BuildFileHashInit, WardenWin/BuildLuaInit, WardenWin/BuildTimingInit, WorldObject.Object/BuildValuesUpdate, WorldSession.GroupHandler/BuildPartyMemberStatsPacket, WorldSession.MiscHandler/HandleInspectHonorStatsOpcode, WorldSession.PetitionsHandler/HandlePetitionQueryOpcode | — |
| operator<<#10 | method | — | AddonHandler/BuildAddonPacket, AuctionHouseMgr/BuildAuctionInfo, AuthSocket/LoadRealmlistAndWriteIntoBuffer, AuthSocket/_HandleLogonChallenge, BattleGroundMgr/BuildBattleGroundListPacket, BattleGroundMgr/BuildBattleGroundStatusPacket, BattleGroundMgr/BuildPlaySoundPacket, BattleGroundMgr/BuildPvpLogDataPacket, ChatHandler.Chat/BuildChatPacket, ChatHandler.DebugCommands/HandleDebugMonsterChatCommand, ChatHandler.DebugCommands/HandleDebugPvPCreditCommand, ChatHandler.DebugCommands/HandleDebugSendChannelNotifyCommand, ChatHandler.DebugCommands/HandleDebugSendNextChannelSpellVisualCommand, ChatHandler.DebugCommands/HandleDebugSendOpcodeCommand, ChatHandler.DebugCommands/HandleDebugSendSpellFailCommand, ChatHandler.DebugCommands/HandleSendSpellChannelVisualCommand, ChatHandler.DebugCommands/HandleSendSpellImpactCommand, ChatHandler.DebugCommands/HandleSendSpellVisualCommand, ChatHandler.TicketCommands/HandleGMTicketCloseByIdCommand, ChatHandler.TicketCommands/HandleGMTicketDeleteByIdCommand, Creature.Main/SendAIReaction, Creature.Main/SendAreaSpiritHealerQueryOpcode, Creature.Main/SendZoneUnderAttackMessage, GameObject/SendGameObjectCustomAnim, game_Chat_Channel/MakeYouJoined, game_Group_Group/RemoveMember, game_Group_Group/SendLootAllPassed, game_Group_Group/SendLootRoll, game_Group_Group/SendLootRollWon, game_Group_Group/SendLootStartRoll, game_Group_Group/SendLootStartRollsForPlayer, game_Group_Group/SendUpdate, game_Guild_Guild/Query, game_Guild_Guild/Roster, game_Objects_Item/SendTimeUpdate, GMTicketMgr/SendTicket, GMTicketMgr/WritePacket, GossipDef/SendGossipMenu, GossipDef/SendPointOfInterest, GossipDef/SendPointOfInterest#2, GossipDef/SendQuestGiverOfferReward, GossipDef/SendQuestGiverQuestDetails, GossipDef/SendQuestGiverQuestList, GossipDef/SendQuestGiverRequestItems, GossipDef/SendQuestGiverStatus, GossipDef/SendTalking, GossipDef/SendTalking#2, HonorMgr/SendPVPCredit, LFGMgr/BuildSetQueuePacket, Log.Warden/SendModuleUse, LootMgr/operator<<, LootMgr/operator<<#2, Map.Main/BindPlayerOrGroupOnEnter, Map.Main/PermBindAllPlayers, Map.Main/PlayDirectSoundToMap, Map.Main/SendDefenseMessage, MasterPlayer.Main/SendInitialActionButtons, MovementPacketSender/SendKnockBackToController, MovementPacketSender/SendMovementFlagChangeToController, MovementPacketSender/SendSpeedChangeToController, MovementPacketSender/SendTeleportToController, MoveSplineInit/Launch, packet_builder/WriteCatmullRomCyclicPath, packet_builder/WriteCatmullRomPath, packet_builder/WriteCommonMonsterMovePart, packet_builder/WriteCreate, packet_builder/WriteLinearPath, Pet.Main/_LoadSpellCooldowns, Player.Main/ActivateTaxiPathTo, Player.Main/AddCooldown, Player.Main/ApplyEquipCooldown, Player.Main/BuildEnchantmentLog, Player.Main/BuildEnumData, Player.Main/BuyItemFromVendor, Player.Main/CharmSpellInitialize, Player.Main/ExecuteTeleportFar, Player.Main/GiveLevel, Player.Main/LearnSpell, Player.Main/LockOutSpells, Player.Main/PetSpellInitialize, Player.Main/PossessSpellInitialize, Player.Main/SendBuyError, Player.Main/SendCanTakeQuestResponse, Player.Main/SendChannelUpdate, Player.Main/SendCinematicStart, Player.Main/SendClearCooldown, Player.Main/SendCorpseReclaimDelay, Player.Main/SendDismountResult, Player.Main/SendDuelCountdown, Player.Main/SendEquipError, Player.Main/SendExplorationExperience, Player.Main/SendFactionAtWar, Player.Main/SendInitialPacketsBeforeAddToMap, Player.Main/SendInitialSpells, Player.Main/SendInitWorldStates, Player.Main/SendInstanceResetWarning, Player.Main/SendLogXPGain, Player.Main/SendLootMoneyNotify, Player.Main/SendMirrorTimerPause, Player.Main/SendMirrorTimerStart, Player.Main/SendMirrorTimerStop, Player.Main/SendMountResult, Player.Main/SendNewItem, Player.Main/SendNewWorld, Player.Main/SendPetSkillWipeConfirm, Player.Main/SendProficiency, Player.Main/SendQuestCompleteEvent, Player.Main/SendQuestConfirmAccept, Player.Main/SendQuestFailed, Player.Main/SendQuestFailedAtTaker, Player.Main/SendQuestReward, Player.Main/SendQuestTimerFailed, Player.Main/SendQuestUpdateAddCreatureOrGo, Player.Main/SendQuestUpdateAddItem, Player.Main/SendRaidGroupOnlyError, Player.Main/SendRaidInfo, Player.Main/SendResetInstanceFailed, Player.Main/SendResetInstanceSuccess, Player.Main/SendSavedInstances, Player.Main/SendSpellCooldown, Player.Main/SendSummonRequest, Player.Main/SendTalentWipeConfirm, PlayerTaxi/AppendTaximaskTo, ReputationMgr/SendForceReactions, ReputationMgr/SendInitialReputations, ReputationMgr/SendState, ReputationMgr/SendVisible, SocialMgr/SendFriendList, SocialMgr/SendFriendStatus, Spell.Effects/EffectBind, Spell.Effects/EffectDispel, Spell.Effects/EffectInstaKill, Spell.Main/Delayed, Spell.Main/SendAllTargetsMiss, Spell.Main/SendCastResult#2, Spell.Main/SendChannelStart, Spell.Main/SendInterrupted, Spell.Main/SendLogExecute, Spell.Main/SendResurrectRequest, Spell.Main/SendSpellGo, Spell.Main/SendSpellStart, Spell.Main/WriteAmmoToPacket, SpellCaster/SendEnergizeSpellLog, SpellCaster/SendHealSpellLog, SpellCaster/SendSpellDamageResist, SpellCaster/SendSpellMiss, SpellCaster/SendSpellNonMeleeDamageLog, SpellCaster/SendSpellOrDamageImmune, Unit.Main/BuildActionBar, Unit.Main/HandleEmoteCommand, Unit.Main/SendAttackStateUpdate, Unit.Main/SendEnvironmentalDamageLog, Unit.Main/SendMeleeAttackStop, Unit.Main/SendPeriodicAuraLog, Unit.Main/SendPetAIReaction, Unit.Main/SendPetCastFail, Unit.Main/SendPetTalk, Unit.Main/SendPlaySpellVisualKit, Unit.Main/SendSpellGo, Unit.Main/TriggerDamageShields, Unit.Main/WritePetSpellsCooldown, Unit.SpellAuras/UpdateAuraDuration, UpdateData/BuildPacket#2, WardenScan/WindowsCodeScan, WardenScan/WindowsDriverScan, WardenScan/WindowsHookScan, WardenScan/WindowsMemoryScan, WardenScan/WindowsMemoryScan#2, WardenScan/WindowsMemoryScan#3, WardenScan/WindowsMemoryScan#4, WardenScan/WindowsModuleScan, WardenScan/WindowsModuleScan#2, WardenWin/BuildFileHashInit, WardenWin/BuildLuaInit, WardenWin/BuildTimingInit, WardenWin/LoadScriptedScans, Weather/SendWeatherForPlayersInZone, Weather/SendWeatherUpdateToPlayer, World/AddQueuedSession, World/AddSession_, World/SendServerMessage, WorldObject.Object/BuildMovementUpdate, WorldObject.Object/BuildValuesUpdate, WorldObject.Object/DirectSendPublicValueUpdate#2, WorldObject.Object/PlayDirectMusic, WorldObject.Object/PlayDirectSound, WorldObject.Object/PlayDistanceSound, WorldObject.Object/Write, WorldSession.AuctionHouseHandler/operator(), WorldSession.AuctionHouseHandler/SendAuctionBidderNotification, WorldSession.AuctionHouseHandler/SendAuctionCommandResult, WorldSession.AuctionHouseHandler/SendAuctionHello, WorldSession.AuctionHouseHandler/SendAuctionOwnerNotification, WorldSession.AuctionHouseHandler/SendAuctionRemovedNotification, WorldSession.BattleGroundHandler/HandleBattleFieldPortOpcode, WorldSession.BattleGroundHandler/HandleBattleGroundPlayerPositionsOpcode, WorldSession.BattleGroundHandler/RequestBgJoinQueue, WorldSession.CharacterHandler/HandlePlayerLogin, WorldSession.ChatHandler/operator(), WorldSession.CombatHandler/SendAttackStop, WorldSession.GMTicketHandler/HandleGMTicketCreateOpcode, WorldSession.GMTicketHandler/HandleGMTicketDeleteTicketOpcode, WorldSession.GMTicketHandler/HandleGMTicketSystemStatusOpcode, WorldSession.GMTicketHandler/HandleGMTicketUpdateTextOpcode, WorldSession.GroupHandler/BuildPartyMemberStatsPacket, WorldSession.GroupHandler/HandleRandomRollOpcode, WorldSession.GroupHandler/HandleRequestPartyMemberStatsOpcode, WorldSession.GroupHandler/SendPartyResult, WorldSession.GuildHandler/HandleGuildInfoOpcode, WorldSession.GuildHandler/SendGuildCommandResult, WorldSession.GuildHandler/SendSaveGuildEmblem, WorldSession.ItemHandler/HandleBuyBankSlotOpcode, WorldSession.ItemHandler/HandleItemNameQueryOpcode, WorldSession.ItemHandler/HandleItemQuerySingleOpcode, WorldSession.ItemHandler/SendItemEnchantTimeUpdate, WorldSession.ItemHandler/SendListInventory, WorldSession.LFGHandler/SendMeetingstoneSetqueue, WorldSession.MailHandler/HandleGetMailList, WorldSession.MailHandler/HandleItemTextQuery, WorldSession.MailHandler/SendMailResult, WorldSession.MailHandler/SendNewMail, WorldSession.Main/SendAreaTriggerMessage, WorldSession.Main/SendAuthWaitQue, WorldSession.Main/SendPlayTimeWarning, WorldSession.Main/SendTutorialsData, WorldSession.MiscHandler/HandleInspectHonorStatsOpcode, WorldSession.MiscHandler/HandleLFGOpcode, WorldSession.MiscHandler/HandleLogoutRequestOpcode, WorldSession.MiscHandler/HandlePlayedTime, WorldSession.MiscHandler/HandleRequestAccountData, WorldSession.MiscHandler/operator(), WorldSession.MovementHandler/HandleMoveTimeSkippedOpcode, WorldSession.NPCHandler/SendStablePet, WorldSession.NPCHandler/SendTrainerList, WorldSession.NPCHandler/SendTrainerSpellHelper, WorldSession.NPCHandler/SendTrainingFailure, WorldSession.NPCHandler/SendTrainingSuccess, WorldSession.PetHandler/SendPetNameQuery, WorldSession.PetitionsHandler/HandleOfferPetitionOpcode, WorldSession.PetitionsHandler/HandlePetitionQueryOpcode, WorldSession.PetitionsHandler/HandlePetitionShowSignOpcode, WorldSession.PetitionsHandler/HandlePetitionSignOpcode, WorldSession.PetitionsHandler/HandleTurnInPetitionOpcode, WorldSession.PetitionsHandler/SendPetitionShowList, WorldSession.QueryHandler/HandleCorpseQueryOpcode, WorldSession.QueryHandler/HandleCreatureQueryOpcode, WorldSession.QueryHandler/HandleGameObjectQueryOpcode, WorldSession.QueryHandler/HandleNpcTextQueryOpcode, WorldSession.QueryHandler/HandlePageTextQueryOpcode, WorldSession.QueryHandler/SendNameQueryOpcode, WorldSession.QueryHandler/SendNameQueryOpcodeFromDB, WorldSession.QueryHandler/SendNameQueryOpcodeFromDBCallBack, WorldSession.QueryHandler/SendQueryTimeResponse, WorldSession.QuestHandler/HandleQuestQueryOpcode, WorldSession.SkillHandler/HandleTalentWipeConfirmOpcode, WorldSession.SpellHandler/HandleCastSpellOpcode, WorldSession.TaxiHandler/SendTaxiMenu, WorldSession.TradeHandler/HandleInitiateTradeOpcode, WorldSession.TradeHandler/SendTradeStatus, WorldSession.TradeHandler/SendUpdateTrade, WorldSocket/SendInitialPacketAndStartRecvLoop, WorldSocket/_HandlePing | — |
| operator<<#11 | method | — | ChatHandler.DebugCommands/HandleDebugPvPCreditCommand, ChatHandler.DebugCommands/HandleDebugSendOpcodeCommand, ChatHandler.DebugCommands/HandleSendSpellImpactCommand, ChatHandler.DebugCommands/HandleSendSpellVisualCommand, game_Group_Group/Disband, game_Group_Group/RemoveMember, game_Group_Group/SendUpdate, LFGMgr/BuildMemberAddedPacket, ObjectGuid/operator<<, packet_builder/WriteCommonMonsterMovePart, packet_builder/WriteCreate, Player.Main/SendLootError, Unit.Main/SendPlaySpellVisualKit, WorldSession.SkillHandler/HandleTalentWipeConfirmOpcode, WorldSession.TradeHandler/SendTradeStatus | — |
| operator<<#12 | method | — | — | — |
| operator<<#6 | method | — | WorldSession.GroupHandler/BuildPartyMemberStatsPacket | — |
| operator<<#4 | method | — | BattleGroundMgr/BuildGroupJoinedBattlegroundPacket, ChatHandler.DebugCommands/HandleDebugPlayMusicCommand, ChatHandler.DebugCommands/HandleDebugSpellModsCommand, game_Chat_Channel/List, game_Group_Group/RemoveMember, game_Guild_Guild/Query, GuildMgr/BuildSignatureData, HonorMgr/SendPVPCredit, packet_builder/WriteCommonMonsterMovePart, packet_builder/WriteCreate, Player.Main/CharmSpellInitialize, Player.Main/PossessSpellInitialize, Player.Main/SendMirrorTimerStart, Player.Main/SendSpellMod, Spell.Main/SendLogExecute, SpellCaster/SendSpellNonMeleeDamageLog, Unit.Main/SendAttackStateUpdate, Unit.Main/SendEnvironmentalDamageLog, Unit.Main/SendPeriodicAuraLog, WorldSession.ItemHandler/HandleItemQuerySingleOpcode, WorldSession.Main/SendPlayTimeWarning, WorldSession.QueryHandler/HandleCorpseQueryOpcode, WorldSession.QuestHandler/HandleQuestQueryOpcode | — |
| operator<<#5 | method | — | HonorMgr/SendPVPCredit | — |
| operator<<#9 | method | — | AuthSocket/LoadRealmlistAndWriteIntoBuffer, ChatHandler.DebugCommands/HandleDebugSendOpcodeCommand, Creature.Main/OnEnterCombat, game_Guild_Guild/Roster, GMTicketMgr/WritePacket, GossipDef/SendPointOfInterest, GossipDef/SendPointOfInterest#2, GossipDef/SendTalking, GossipDef/SendTalking#2, MovementPacketSender/SendKnockBackToController, MovementPacketSender/SendKnockBackToObservers, MovementPacketSender/SendSpeedChangeToAll, MovementPacketSender/SendSpeedChangeToController, MovementPacketSender/SendSpeedChangeToObservers, MoveSplineInit/Launch, packet_builder/operator<<, packet_builder/WriteCommonMonsterMovePart, packet_builder/WriteCreate, Player.Main/BuildEnumData, Player.Main/SendInitialPacketsBeforeAddToMap, Player.Main/SendLogXPGain, Player.Main/SendNewWorld, Spell.Effects/EffectBind, Spell.Main/SendLogExecute, SpellCastTargetsInfo/write, Unit.Main/SendAttackStateUpdate, Unit.Main/SendPeriodicAuraLog, Weather/SendWeatherForPlayersInZone, Weather/SendWeatherUpdateToPlayer, WorldObject.Object/BuildMovementUpdate, WorldObject.Object/BuildValuesUpdate, WorldObject.Object/Write, WorldSession.BattleGroundHandler/HandleBattleGroundPlayerPositionsOpcode, WorldSession.CharacterHandler/HandlePlayerLogin, WorldSession.GroupHandler/HandleMinimapPingOpcode, WorldSession.ItemHandler/HandleItemQuerySingleOpcode, WorldSession.MailHandler/HandleGetMailList, WorldSession.MailHandler/HandleQueryNextMailTime, WorldSession.QueryHandler/HandleCorpseQueryOpcode, WorldSession.QueryHandler/HandleNpcTextQueryOpcode, WorldSession.QuestHandler/HandleQuestQueryOpcode | — |
| operator<<#8 | method | — | — | — |
| operator<< | method | — | AuthSocket/LoadRealmlistAndWriteIntoBuffer, ChatHandler.DebugCommands/HandleDebugSendOpcodeCommand, ChatHandler.ServerCommands/HandleNotifyCommand, game_Chat_Channel/List, game_Chat_Channel/MakeChannelOwner, game_Chat_Channel/MakeNotifyPacket, game_Chat_Channel/MakeNotOnPacket, game_Chat_Channel/MakePlayerInviteBanned, game_Chat_Channel/MakePlayerInvited, game_Chat_Channel/MakePlayerNotBanned, game_Chat_Channel/MakePlayerNotFound, game_Group_Group/ChangeLeader, game_Group_Group/RemoveMember, game_Group_Group/SendUpdate, game_Guild_Guild/Query, game_Guild_Guild/Roster, GMTicketMgr/WritePacket, GossipDef/SendGossipMenu, GossipDef/SendQuestGiverQuestList, GossipDef/SendTalking#2, MovementAnticheat/AddMessageToPacketLog, Player.Main/SendQuestConfirmAccept, SpellCastTargetsInfo/write, WorldSession.ChannelHandler/HandleJoinChannelOpcode, WorldSession.CharacterHandler/HandleChangePlayerNameOpcodeCallBack, WorldSession.CharacterHandler/HandlePlayerLogin, WorldSession.ChatHandler/SendPlayerNotFoundNotice, WorldSession.GroupHandler/SendPartyResult, WorldSession.GuildHandler/HandleGuildInfoOpcode, WorldSession.GuildHandler/HandleGuildInviteOpcode, WorldSession.GuildHandler/SendGuildCommandResult, WorldSession.MailHandler/HandleGetMailList, WorldSession.MailHandler/HandleItemTextQuery, WorldSession.MiscHandler/HandleWhoisOpcode, WorldSession.MiscHandler/operator(), WorldSession.NPCHandler/SendStablePet, WorldSession.NPCHandler/SendTrainerList, WorldSession.PetHandler/SendPetNameQuery, WorldSession.PetitionsHandler/HandlePetitionQueryOpcode, WorldSession.PetitionsHandler/HandlePetitionRenameOpcode, WorldSession.QueryHandler/HandleGameObjectQueryOpcode, WorldSession.QueryHandler/HandleNpcTextQueryOpcode, WorldSession.QueryHandler/SendNameQueryOpcodeFromDB, WorldSession.QueryHandler/SendNameQueryOpcodeFromDBCallBack | — |
| operator<<#3 | method | — | ChatHandler.Chat/BuildChatPacket, ChatHandler.DebugCommands/HandleDebugMonsterChatCommand, ChatHandler.DebugCommands/HandleDebugSendChannelNotifyCommand, game_Guild_Guild/BroadcastEvent, GossipDef/SendPointOfInterest, GossipDef/SendTalking#2, Map.Main/SendDefenseMessage, Player.Main/BuildEnumData, Player.Main/DuelComplete, Spell.Main/SendResurrectRequest, World/SendServerMessage, WorldSession.GroupHandler/BuildPartyMemberStatsPacket, WorldSession.GroupHandler/HandleGroupDeclineOpcode, WorldSession.GroupHandler/HandleGroupInviteOpcode, WorldSession.GuildHandler/HandleGuildDeclineOpcode, WorldSession.GuildHandler/HandleGuildInviteOpcode, WorldSession.ItemHandler/HandleItemQuerySingleOpcode, WorldSession.Main/SendAreaTriggerMessage, WorldSession.Main/SendNotification, WorldSession.Main/SendNotification#2, WorldSession.NPCHandler/SendStablePet, WorldSession.QueryHandler/HandleNpcTextQueryOpcode, WorldSession.QueryHandler/HandlePageTextQueryOpcode, WorldSession.QueryHandler/SendNameQueryOpcode, WorldSession.QueryHandler/SendNameQueryOpcodeFromDB, WorldSession.QueryHandler/SendNameQueryOpcodeFromDBCallBack | — |
| operator<<#2 | method | — | — | — |
| operator>>#5 | method | — | Misc/ReadFromWorldPacket#18, Misc/ReadFromWorldPacket#30 | — |
| operator>>#6 | method | — | AddonHandler/BuildAddonPacket, AiBotAI.Main/OnPacketReceived, AuctionHouse/ReadFromWorldPacket#3, Character/ReadFromWorldPacket, game_Server_Packets_Battleground/ReadFromWorldPacket#3, game_Server_Packets_Battleground/ReadFromWorldPacket#7, game_Server_Packets_Group/ReadFromWorldPacket, game_Server_Packets_Group/ReadFromWorldPacket#10, game_Server_Packets_Group/ReadFromWorldPacket#11, game_Server_Packets_Group/ReadFromWorldPacket#2, game_Server_Packets_Item/ReadFromWorldPacket, game_Server_Packets_Item/ReadFromWorldPacket#10, game_Server_Packets_Item/ReadFromWorldPacket#13, game_Server_Packets_Item/ReadFromWorldPacket#14, game_Server_Packets_Item/ReadFromWorldPacket#16, game_Server_Packets_Item/ReadFromWorldPacket#17, game_Server_Packets_Item/ReadFromWorldPacket#18, game_Server_Packets_Item/ReadFromWorldPacket#19, game_Server_Packets_Item/ReadFromWorldPacket#2, game_Server_Packets_Item/ReadFromWorldPacket#3, game_Server_Packets_Item/ReadFromWorldPacket#4, game_Server_Packets_Item/ReadFromWorldPacket#5, game_Server_Packets_Item/ReadFromWorldPacket#7, game_Server_Packets_Item/ReadFromWorldPacket#8, GmTicket/ReadFromWorldPacket, GmTicket/ReadFromWorldPacket#2, GmTicket/ReadFromWorldPacket#3, Log.Warden/HandlePacket, Loot/ReadFromWorldPacket, Loot/ReadFromWorldPacket#2, Loot/ReadFromWorldPacket#4, Misc/ReadFromWorldPacket#19, Misc/ReadFromWorldPacket#20, Misc/ReadFromWorldPacket#22, Misc/ReadFromWorldPacket#23, Misc/ReadFromWorldPacket#9, Pet/ReadFromWorldPacket#7, Quest/ReadFromWorldPacket#4, Quest/ReadFromWorldPacket#5, Quest/ReadFromWorldPacket#6, Spell/ReadFromWorldPacket#5, Spell/ReadFromWorldPacket#6, Trade/ReadFromWorldPacket#2, Trade/ReadFromWorldPacket#5 | — |
| operator>>#12 | method | — | Log.Warden/HandlePacket, SpellCastTargetsInfo/read | — |
| operator>>#9 | method | — | AddonHandler/BuildAddonPacket, AiBotAI.Main/OnPacketReceived, AuctionHouse/ReadFromWorldPacket#2, AuctionHouse/ReadFromWorldPacket#3, AuctionHouse/ReadFromWorldPacket#4, AuctionHouse/ReadFromWorldPacket#5, AuctionHouse/ReadFromWorldPacket#6, AuctionHouse/ReadFromWorldPacket#7, Chat/ReadFromWorldPacket, Combat/ReadFromWorldPacket#2, game_Server_Packets_Battleground/ReadFromWorldPacket#3, game_Server_Packets_Battleground/ReadFromWorldPacket#4, game_Server_Packets_Battleground/ReadFromWorldPacket#5, game_Server_Packets_Battleground/ReadFromWorldPacket#7, game_Server_Packets_Battleground/ReadFromWorldPacket#8, game_Server_Packets_Group/ReadFromWorldPacket#12, game_Server_Packets_Group/ReadFromWorldPacket#8, game_Server_Packets_Guild/ReadFromWorldPacket#10, game_Server_Packets_Guild/ReadFromWorldPacket#9, game_Server_Packets_Item/ReadFromWorldPacket#12, game_Server_Packets_Item/ReadFromWorldPacket#15, game_Server_Packets_Item/ReadFromWorldPacket#7, game_Server_Packets_Item/ReadFromWorldPacket#8, game_Server_Packets_Item/ReadFromWorldPacket#9, game_Server_Packets_Mail/ReadFromWorldPacket#2, game_Server_Packets_Mail/ReadFromWorldPacket#3, game_Server_Packets_Mail/ReadFromWorldPacket#4, game_Server_Packets_Mail/ReadFromWorldPacket#5, game_Server_Packets_Mail/ReadFromWorldPacket#6, game_Server_Packets_Mail/ReadFromWorldPacket#7, game_Server_Packets_Mail/ReadFromWorldPacket#8, GmTicket/ReadFromWorldPacket, GmTicket/ReadFromWorldPacket#2, Log.Warden/HandlePacket, Loot/ReadFromWorldPacket#4, Misc/ReadFromWorldPacket#13, Misc/ReadFromWorldPacket#17, Misc/ReadFromWorldPacket#20, Misc/ReadFromWorldPacket#22, Misc/ReadFromWorldPacket#23, Misc/ReadFromWorldPacket#26, Misc/ReadFromWorldPacket#29, Misc/ReadFromWorldPacket#3, Misc/ReadFromWorldPacket#31, Misc/ReadFromWorldPacket#32, Misc/ReadFromWorldPacket#34, Misc/ReadFromWorldPacket#35, Misc/ReadFromWorldPacket#36, Misc/ReadFromWorldPacket#4, Misc/ReadFromWorldPacket#8, Movement/ReadFromWorldPacket, Movement/ReadFromWorldPacket#2, Movement/ReadFromWorldPacket#4, Movement/ReadFromWorldPacket#5, Movement/ReadFromWorldPacket#6, Movement/ReadFromWorldPacket#7, Movement/ReadFromWorldPacket#8, Npc/ReadFromWorldPacket#11, Npc/ReadFromWorldPacket#13, Npc/ReadFromWorldPacket#15, Npc/ReadFromWorldPacket#5, Npc/ReadFromWorldPacket#7, Pet/ReadFromWorldPacket#10, Pet/ReadFromWorldPacket#2, Pet/ReadFromWorldPacket#3, Pet/ReadFromWorldPacket#4, Pet/ReadFromWorldPacket#6, Pet/ReadFromWorldPacket#7, Petition/ReadFromWorldPacket#8, Query/ReadFromWorldPacket, Query/ReadFromWorldPacket#2, Query/ReadFromWorldPacket#3, Query/ReadFromWorldPacket#4, Quest/ReadFromWorldPacket, Quest/ReadFromWorldPacket#11, Quest/ReadFromWorldPacket#12, Quest/ReadFromWorldPacket#2, Quest/ReadFromWorldPacket#3, Quest/ReadFromWorldPacket#7, Quest/ReadFromWorldPacket#8, Quest/ReadFromWorldPacket#9, Skill/ReadFromWorldPacket, Skill/ReadFromWorldPacket#3, Spell/ReadFromWorldPacket, Spell/ReadFromWorldPacket#2, Spell/ReadFromWorldPacket#3, Spell/ReadFromWorldPacket#4, Taxi/ReadFromWorldPacket, Taxi/ReadFromWorldPacket#2, Trade/ReadFromWorldPacket#4, WorldObject.Object/Read, WorldSession.ItemHandler/HandlePageQuerySkippedOpcode, WorldSocket/_HandleAuthSession, WorldSocket/_HandlePing | — |
| operator>>#10 | method | — | AiBotAI.Main/OnPacketReceived | — |
| operator>>#11 | method | — | — | — |
| operator>>#4 | method | — | — | — |
| operator>>#2 | method | — | game_Server_Packets_Guild/ReadFromWorldPacket#14, Misc/ReadFromWorldPacket#25 | — |
| operator>>#3 | method | — | — | — |
| operator>>#8 | method | — | game_Server_Packets_Group/ReadFromWorldPacket#9, GmTicket/ReadFromWorldPacket#2, Misc/ReadFromWorldPacket#15, Misc/ReadFromWorldPacket#35, Movement/ReadFromWorldPacket#5, packet_builder/operator>>, SpellCastTargetsInfo/read, WorldObject.Object/Read | — |
| operator>>#7 | method | — | — | — |
| operator>> | method | — | AddonHandler/BuildAddonPacket, AiBotAI.Main/OnPacketReceived, AuctionHouse/ReadFromWorldPacket#3, Character/ReadFromWorldPacket, Character/ReadFromWorldPacket#3, Chat/ReadFromWorldPacket, game_Server_Packets_Channel/ReadFromWorldPacket, game_Server_Packets_Channel/ReadFromWorldPacket#10, game_Server_Packets_Channel/ReadFromWorldPacket#11, game_Server_Packets_Channel/ReadFromWorldPacket#12, game_Server_Packets_Channel/ReadFromWorldPacket#13, game_Server_Packets_Channel/ReadFromWorldPacket#14, game_Server_Packets_Channel/ReadFromWorldPacket#15, game_Server_Packets_Channel/ReadFromWorldPacket#16, game_Server_Packets_Channel/ReadFromWorldPacket#2, game_Server_Packets_Channel/ReadFromWorldPacket#3, game_Server_Packets_Channel/ReadFromWorldPacket#4, game_Server_Packets_Channel/ReadFromWorldPacket#5, game_Server_Packets_Channel/ReadFromWorldPacket#6, game_Server_Packets_Channel/ReadFromWorldPacket#7, game_Server_Packets_Channel/ReadFromWorldPacket#8, game_Server_Packets_Channel/ReadFromWorldPacket#9, game_Server_Packets_Group/ReadFromWorldPacket#2, game_Server_Packets_Group/ReadFromWorldPacket#3, game_Server_Packets_Group/ReadFromWorldPacket#5, game_Server_Packets_Group/ReadFromWorldPacket#6, game_Server_Packets_Guild/ReadFromWorldPacket, game_Server_Packets_Guild/ReadFromWorldPacket#10, game_Server_Packets_Guild/ReadFromWorldPacket#11, game_Server_Packets_Guild/ReadFromWorldPacket#12, game_Server_Packets_Guild/ReadFromWorldPacket#13, game_Server_Packets_Guild/ReadFromWorldPacket#2, game_Server_Packets_Guild/ReadFromWorldPacket#3, game_Server_Packets_Guild/ReadFromWorldPacket#4, game_Server_Packets_Guild/ReadFromWorldPacket#5, game_Server_Packets_Guild/ReadFromWorldPacket#6, game_Server_Packets_Guild/ReadFromWorldPacket#7, game_Server_Packets_Guild/ReadFromWorldPacket#8, game_Server_Packets_Mail/ReadFromWorldPacket#8, GmTicket/ReadFromWorldPacket, GmTicket/ReadFromWorldPacket#2, GmTicket/ReadFromWorldPacket#3, Misc/ReadFromWorldPacket, Misc/ReadFromWorldPacket#2, Misc/ReadFromWorldPacket#28, Misc/ReadFromWorldPacket#34, Misc/ReadFromWorldPacket#4, Npc/ReadFromWorldPacket#5, Pet/ReadFromWorldPacket#5, Petition/ReadFromWorldPacket#2, Petition/ReadFromWorldPacket#4, Query/ReadFromWorldPacket#6, SpellCastTargetsInfo/read, WorldSocket/_HandleAuthSession | — |
| operator[] | method | — | — | — |
| rpos | method | — | AddonHandler/BuildAddonPacket, Log.Warden/HandlePacket, Misc/ReadFromWorldPacket#32, Misc/ReadFromWorldPacket#33, Query/ReadFromWorldPacket#4, SpellCastTargetsInfo/read, WardenScan/WindowsLuaScan#2, WardenScan/WindowsLuaScan#3, WardenScan/WindowsMemoryScan, WardenScan/WindowsMemoryScan#3, WorldSession.Main/VerifyPacketWasCorrectlyRead | — |
| rpos#2 | method | — | Log.Warden/HandleChallengeResponse, Log.Warden/HandlePacket, WardenScan/WindowsLuaScan#2, WardenScan/WindowsLuaScan#3, WardenScan/WindowsMemoryScan, WardenScan/WindowsMemoryScan#3 | — |
| wpos | method | — | BattleGroundMgr/BuildBattleGroundListPacket, game_Chat_Channel/List, game_Group_Group/SendUpdate, game_Guild_Guild/Roster, GossipDef/SendQuestGiverQuestList, Log.Warden/HandleChallengeResponse, Log.Warden/HandlePacket, Log.Warden/RequestScans, Log.Warden/SendPacket, Log.Warden/SendPacketDirect, LootMgr/operator<<#2, packet_builder/WriteMonsterMove, Player.Main/PetSpellInitialize, Player.Main/SendInitialSpells, Player.Main/SendInitWorldStates, Player.Main/SendRaidInfo, ReputationMgr/SendState, Spell.Main/SendChannelStart, Spell.Main/WriteSpellGoTargets, Unit.Main/WritePetSpellsCooldown, UpdateData/AddPacket, UpdateData/AddUpdateBlockAndGetBuffer, UpdateData/BuildPacket, UpdateData/BuildPacket#2, UpdateData/CanAddPacket, WardenWin/BuildFileHashInit, WardenWin/BuildLuaInit, WardenWin/BuildTimingInit, WardenWin/InitializeClient, WorldSession.AuctionHouseHandler/operator(), WorldSession.BattleGroundHandler/HandleBattleGroundPlayerPositionsOpcode, WorldSession.ItemHandler/SendListInventory, WorldSession.NPCHandler/SendStablePet, WorldSession.NPCHandler/SendTrainerList | — |
| wpos#2 | method | — | WardenWin/BuildFileHashInit, WardenWin/BuildLuaInit, WardenWin/BuildTimingInit | — |
| read_skip | method | — | — | — |
| rfinish | method | — | — | — |
| read | method | — | Misc/ReadFromWorldPacket#32, Misc/ReadFromWorldPacket#33, WardenScan/GetChecker, WardenScan/WindowsFileHashScan, WardenWin/LoadScriptedScans, WardenWin/ValidateEndScene, WorldSocket/_HandleAuthSession | — |
| ReadCString | method | — | — | — |
| ReadCString#2 | method | — | — | — |
| readPackGUID | method | — | ObjectGuid/operator>>#2 | — |
| contents | method | — | AddonHandler/BuildAddonPacket, BattleBotAI.Main/OnPacketReceived, CombatBotBaseAI/OnPacketReceived, Log.Warden/HandleChallengeResponse, Log.Warden/HandlePacket, Log.Warden/SendPacket, Log.Warden/SendPacketDirect, SniffFile/WritePacket#2, UpdateData/AddPacket, UpdateData/BuildPacket, UpdateData/BuildPacket#2, WardenScan/WindowsLuaScan#2, WardenScan/WindowsMemoryScan, WardenScan/WindowsMemoryScan#3, WardenWin/BuildFileHashInit, WardenWin/BuildLuaInit, WardenWin/BuildTimingInit, WorldSocket/DoRecvIncomingData, WorldSocket/HandleResultOfAsyncWrite | — |
| size | method | — | AddonHandler/BuildAddonPacket, AuthSocket/_HandleRealmList, Log.Warden/HandlePacket, Misc/ReadFromWorldPacket#30, Misc/ReadFromWorldPacket#32, Misc/ReadFromWorldPacket#33, Pet.Main/_LoadSpellCooldowns, Pet/ReadFromWorldPacket#6, Query/ReadFromWorldPacket#4, SniffFile/WritePacket#2, SpellCastTargetsInfo/read, WorldSession.Main/SendMovementPacket, WorldSession.Main/SendPacket, WorldSession.Main/VerifyPacketWasCorrectlyRead, WorldSocket/DoRecvIncomingData, WorldSocket/HandleResultOfAsyncWrite | — |
| empty | method | — | game_Battlegrounds_BattleGround/EndBattleGround, game_Battlegrounds_BattleGround/EndNow, game_Server_Packets_Group/ReadFromWorldPacket#10, game_Server_Packets_Guild/ReadFromWorldPacket#7, Npc/ReadFromWorldPacket#5, UpdateData/BuildPacket, UpdateData/BuildPacket#2, WardenMac/Update, WardenWin/Update, WorldSocket/HandleResultOfAsyncWrite | — |
| resize | method | — | AddonHandler/BuildAddonPacket, UpdateData/BuildPacket, UpdateData/BuildPacket#2, WorldSocket/DoRecvIncomingData | — |
| reserve | method | — | — | — |
| append | method | — | — | — |
| append#2 | method | — | AuthSocket/_HandleLogonChallenge, AuthSocket/_HandleReconnectChallenge, WorldSession.MiscHandler/HandleRequestAccountData | — |
| append#4 | method | — | GossipDef/SendGossipMenu, GossipDef/SendPointOfInterest#2, GossipDef/SendQuestGiverOfferReward, GossipDef/SendQuestGiverQuestDetails, GossipDef/SendQuestGiverQuestList, GossipDef/SendQuestGiverRequestItems, GossipDef/SendTalking, Log.Warden/RequestScans, WardenScan/GetBuilder, WardenWin/BuildFileHashInit, WardenWin/BuildLuaInit, WardenWin/BuildTimingInit, WorldSession.ChatHandler/operator(), WorldSession.ItemHandler/HandleItemNameQueryOpcode, WorldSession.QueryHandler/HandleCreatureQueryOpcode, WorldSession.QueryHandler/HandleGameObjectQueryOpcode, WorldSession.QuestHandler/HandleQuestQueryOpcode, WorldSocket/HandleResultOfAsyncWrite | — |
| append#3 | method | — | AuthSocket/_HandleRealmList, Log.Warden/RequestScans, Log.Warden/SendPacket, Log.Warden/SendPacketDirect, ObjectGuid/operator<<#2, Pet.Main/_LoadSpellCooldowns, Player.Main/LockOutSpells, UpdateData/BuildPacket#2, WardenWin/InitializeClient | — |
| appendPackGUID | method | — | — | — |
| appendPackXYZ | method | — | packet_builder/WriteLinearPath | — |
| put | method | — | — | — |
| read_skip#4 | method | — | — | — |
| read_skip#3 | method | — | — | — |
| read_skip#2 | method | — | — | — |
