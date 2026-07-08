<!-- provenance: verbose -->
# Character

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Character Packet Deserialization Unit

## Purpose & Responsibilities

This unit defines four client-side packet structures within `WorldPackets::Character`: `CharCreate`, `CharDelete`, `PlayerLogin`, and `CharRename`. Its sole responsibility is **deserialization**: converting raw binary data from incoming `WorldPacket` instances into structured C++ objects. Each class inherits from `ClientPacket` and implements `ReadFromWorldPacket` to extract fields such as character names, racial/class identifiers, appearance options, and GUIDs.

The unit contains **no business logic**, validation, or database interaction. It strictly parses the wire protocol format for these four message types.

## Member-by-Member Behavior

### CharCreate
Handles `CMSG_CHAR_CREATE`.
*   **CharCreate()**: Constructor initializes all numeric fields to `0` and sets the opcode.
*   **ReadFromWorldPacket**: Extracts `name` (string), then `race`, `class_`, `gender`, `skin`, `face`, `hairStyle`, `hairColor`, `facialHair`, and `outfitId` (all `uint8`). Order is strict per protocol.

### CharDelete
Handles `CMSG_CHAR_DELETE`.
*   **CharDelete()**: Constructor sets the opcode.
*   **ReadFromWorldPacket#2**: Extracts a single `ObjectGuid` for the target character.

### PlayerLogin
Handles `CMSG_PLAYER_LOGIN`.
*   **PlayerLogin()**: Constructor sets the opcode.
*   **ReadFromWorldPacket#4**: Extracts a single `ObjectGuid` for the logging-in character.

### CharRename
Handles `CMSG_CHAR_RENAME`.
*   **CharRename()**: Constructor sets the opcode.
*   **ReadFromWorldPacket#3**: Extracts the target `ObjectGuid` followed by the `newname` string.

## Cross-Unit Boundaries

*   **Calls Out**:
    *   `ByteBuffer/operator>>`: Used by `CharCreate::ReadFromWorldPacket` and `CharRename::ReadFromWorldPacket#3` to extract strings and `uint8` values.
    *   `ObjectGuid/operator>>`: Used by `CharDelete::ReadFromWorldPacket#2`, `PlayerLogin::ReadFromWorldPacket#4`, and `CharRename::ReadFromWorldPacket#3` to deserialize GUIDs.
*   **Called By**: No external units call these methods directly in the MAP; they are invoked by the central packet dispatcher upon receiving the corresponding opcodes.

## Data Model

This unit interacts with **no database tables**. It operates entirely on in-memory packet buffers.

## Notable Implementation Details

1.  **Strict Field Ordering**: `CharCreate::ReadFromWorldPacket` reads fields in a specific sequence (`name`, `race`, `class_`, then appearance bytes). Deviation causes data corruption.
2.  **No Validation**: The unit does not validate race/class compatibility, name length, or GUID validity. This is deferred to higher-level handlers.
3.  **Default Initialization**: `CharCreate` explicitly zeroes all `uint8` fields in its constructor, ensuring a known state if deserialization is incomplete.

## Member Reference

**CharCreate**
Constructor for `CharCreate`. Initializes opcode to `CMSG_CHAR_CREATE` and sets all numeric fields (`race`, `class_`, `gender`, etc.) to `0`.

**ReadFromWorldPacket**
Deserializes `CMSG_CHAR_CREATE`. Extracts `name` (string), then `race`, `class_`, `gender`, `skin`, `face`, `hairStyle`, `hairColor`, `facialHair`, and `outfitId` (uint8s) using `ByteBuffer/operator>>`.

**ReadFromWorldPacket#2**
Deserializes `CMSG_CHAR_DELETE`. Extracts a single `ObjectGuid` using `ObjectGuid/operator>>`.

**ReadFromWorldPacket#3**
Deserializes `CMSG_CHAR_RENAME`. Extracts `ObjectGuid` via `ObjectGuid/operator>>`, then `newname` (string) via `ByteBuffer/operator>>`.

**ReadFromWorldPacket#4**
Deserializes `CMSG_PLAYER_LOGIN`. Extracts a single `ObjectGuid` using `ObjectGuid/operator>>`.

---

<!-- machine-true, projected from graph.json -->

## Map — Character

*Source:* Character.cpp, Character.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ReadFromWorldPacket | method | ByteBuffer/operator>>, ByteBuffer/operator>>#6 | — | — |
| CharCreate | ctor | — | — | — |
| ReadFromWorldPacket#2 | method | ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket#4 | method | ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket#3 | method | ByteBuffer/operator>>, ObjectGuid/operator>> | — | — |
