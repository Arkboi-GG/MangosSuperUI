# Query

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Query

The `Query` unit defines client-to-server packet structures within `WorldPackets::Query` for various lookup requests: player names, creatures, game objects, page text, character searches ("Whois"), and item names. Each class inherits from `ClientPacket`, stores the relevant query parameters, and implements `ReadFromWorldPacket` to deserialize the incoming `WorldPacket`. This unit performs no database queries; it strictly handles network protocol parsing.

## Member-by-Member Behavior

Each class corresponds to a specific `CMSG_*` opcode. The `ReadFromWorldPacket` method extracts fields from the binary buffer into the class's public members.

*   **`QueryPlayerName`**: Resolves a player name from a GUID. Extracts `playerGuid`.
*   **`QueryCreature`**: Requests creature details. Extracts `entry` (uint32) and `guid` (ObjectGuid).
*   **`QueryGameObject`**: Requests game object details. Extracts `entryID` (uint32) and `guid` (ObjectGuid).
*   **`QueryPageText`**: Requests page text content. Extracts `pageID` (uint32). It conditionally skips an optional 8-byte GUID if the packet contains extra data, referencing a specific client offset (`0x0056485D`) for compatibility.
*   **`Whois`**: Searches for online characters by name. Extracts `charName` (std::string).
*   **`ItemNameQuery`**: Requests an item's name. Extracts `itemId` (uint32) and unconditionally skips an unused 8-byte GUID.

Constructors initialize the base `ClientPacket` with the appropriate opcode. Integer fields are default-initialized to `0` in the header.

## Cross-Unit Boundaries

*   **`ByteBuffer`**: Used by all `ReadFromWorldPacket` methods via `WorldPacket` (which derives from/wraps `ByteBuffer`).
    *   `operator>>` extracts typed values (`uint32`, `std::string`, `ObjectGuid`).
    *   `rpos()` and `size()` are used in `QueryPageText::ReadFromWorldPacket` to detect optional trailing data.
    *   `read_skip<uint64>()` discards unused bytes in `QueryPageText` and `ItemNameQuery`.
*   **`ObjectGuid`**: Its `operator>>` overload deserializes 64-bit GUIDs into `ObjectGuid` instances, called by `QueryPlayerName`, `QueryCreature`, and `QueryGameObject`.

## Data Model

This unit does not interact with any database tables.

## Notable Implementation Details

*   **Variable-Length Packet in `QueryPageText`**: The `ReadFromWorldPacket` method checks `if (recv_data.rpos() < recv_data.size())` to handle an optional trailing GUID. This suggests the packet format varies by client version or context, requiring defensive parsing to avoid deserialization errors.
*   **Unused Data in `ItemNameQuery`**: The method unconditionally skips 8 bytes after reading `itemId`. The comment indicates this GUID is sent by the client but ignored by the server, likely because item names are static per ID.

## Member Reference

**ReadFromWorldPacket#5**  
Deserializes `QueryPlayerName`: extracts `playerGuid` via `ObjectGuid/operator>>`.

**ReadFromWorldPacket#2**  
Deserializes `QueryCreature`: extracts `entry` and `guid` via `ByteBuffer/operator>>#9` and `ObjectGuid/operator>>`.

**QueryPlayerName**  
Constructor initializing `ClientPacket` with `CMSG_NAME_QUERY`.

**ReadFromWorldPacket#3**  
Deserializes `QueryGameObject`: extracts `entryID` and `guid` via `ByteBuffer/operator>>#9` and `ObjectGuid/operator>>`.

**ReadFromWorldPacket#4**  
Deserializes `QueryPageText`: extracts `pageID`. Uses `ByteBuffer/rpos` and `ByteBuffer/size` to check for optional trailing data; if present, skips 8 bytes.

**ReadFromWorldPacket#6**  
Deserializes `ItemNameQuery`: extracts `itemId` and skips 8 unused bytes via `ByteBuffer/operator>>`.

**ReadFromWorldPacket**  
Deserializes `Whois`: extracts `charName` via `ByteBuffer/operator>>#9`.

---

<!-- machine-true, projected from graph.json -->

## Map — Query

*Source:* Query.cpp, Query.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ReadFromWorldPacket#5 | method | ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket#2 | method | ByteBuffer/operator>>#9, ObjectGuid/operator>> | — | — |
| QueryPlayerName | ctor | — | — | — |
| ReadFromWorldPacket#3 | method | ByteBuffer/operator>>#9, ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket#4 | method | ByteBuffer/operator>>#9, ByteBuffer/rpos, ByteBuffer/size | — | — |
| ReadFromWorldPacket#6 | method | ByteBuffer/operator>> | — | — |
| ReadFromWorldPacket | method | ByteBuffer/operator>>#9 | — | — |
