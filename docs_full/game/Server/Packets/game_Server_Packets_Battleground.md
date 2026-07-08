<!-- provenance: verbose -->
# game_Server_Packets_Battleground

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Battleground Packet Definitions (`WorldPackets::Battleground`)

## Purpose & Responsibilities

This unit defines the client-to-server packet structures for battleground and arena interactions within the `WorldPackets::Battleground` namespace. It handles deserialization of raw network data (`WorldPacket`) into typed C++ objects, covering the PvP lifecycle: listing battlegrounds, interacting with battlemasters and spirit healers, joining queues, accepting portals, and leaving instances. Each class inherits from `ClientPacket` and implements `ReadFromWorldPacket` to extract fields such as `mapId`, `guid`, and `action`.

## Member-by-Member Behavior

### Queue Management & Listing
*   **`BattlefieldListRequest`**: Requests a list of battlegrounds. Clients > 1.8.4 include a `mapId` to filter results; older clients send no data.
*   **`BattlemasterJoin`**: Joins a queue via a Battlemaster NPC or portal. Compiled only for clients > 1.6.1. Extracts `guid` (NPC or player), `mapId`, `instanceId` (0 for first available), and `joinAsGroup` flag.
*   **`BattlefieldJoin`**: Directly joins a battlefield by `mapId`, bypassing standard queue mechanics.

### Teleportation & Departure
*   **`BattleFieldPort`**: Accepts or declines a battleground teleport. Includes an `action` byte and, for clients > 1.8.4, a `mapId`.
*   **`LeaveBattlefield`**: Requests to leave a queue or instance. Clients > 1.8.4 specify the `mapId`.

### NPC Interactions
*   **`BattlemasterHello`**: Initiates interaction with a Battlemaster NPC, carrying the NPC's `guid`.
*   **`AreaSpiritHealerQuery`**: Queries a Spirit Healer NPC, carrying the healer's `guid`.
*   **`AreaSpiritHealerQueue`**: Confirms resurrection via a Spirit Healer, carrying the healer's `guid`.

## Cross-Unit Boundaries

*   **Calls Out**:
    *   `ByteBuffer/operator>>` (from `ByteBuffer`): Extracts primitive types (`uint32`, `uint8`) for `mapId`, `instanceId`, `action`, and `joinAsGroup`.
    *   `ObjectGuid/operator>>` (from `ObjectGuid`): Deserializes entity identifiers for NPCs and players.
*   **Called By**:
    *   Invoked by the central packet dispatcher after receiving a raw `WorldPacket`. The dispatcher instantiates the appropriate class and calls `ReadFromWorldPacket` before handing control to game logic handlers.

## Data Model

This unit does not access database tables. It processes transient network data. Fields like `mapId` and `instanceId` correspond to database records (e.g., `battleground_template`) but are only read as integers from the packet stream.

## Notable Implementation Details

*   **Version Gating**: Preprocessor directives (`#if SUPPORTED_CLIENT_BUILD > ...`) ensure compatibility across WoW client versions.
    *   `> CLIENT_BUILD_1_8_4`: Adds `mapId` to `BattlefieldListRequest`, `BattleFieldPort`, and `LeaveBattlefield`.
    *   `> CLIENT_BUILD_1_6_1`: Enables the `BattlemasterJoin` class entirely, reflecting a protocol change in queue joining.
*   **Polymorphic GUIDs**: In `BattlemasterJoin`, the `guid` field represents either the Battlemaster NPC (standard interaction) or the Player (portal join), requiring context-aware handling by downstream logic.
*   **Safe Defaults**: All members are initialized to zero/default values in the header, ensuring valid state even if deserialization is skipped or incomplete.

## Member Reference

**BattlefieldListRequest**
Constructor for `BattlefieldListRequest`. Initializes the packet with opcode `CMSG_BATTLEFIELD_LIST`.

**ReadFromWorldPacket#5**
Deserializes `BattlefieldListRequest`. For clients > 1.8.4, extracts `mapId` via `ByteBuffer/operator>>#9`; otherwise, reads nothing.

**ReadFromWorldPacket**
Deserializes `AreaSpiritHealerQuery`. Extracts the healer's `guid` via `ObjectGuid/operator>>`.

**ReadFromWorldPacket#2**
Deserializes `AreaSpiritHealerQueue`. Extracts the healer's `guid` via `ObjectGuid/operator>>`.

**ReadFromWorldPacket#6**
Deserializes `BattlemasterHello`. Extracts the battlemaster's `guid` via `ObjectGuid/operator>>`.

**ReadFromWorldPacket#3**
Deserializes `BattleFieldPort`. For clients > 1.8.4, extracts `mapId` via `ByteBuffer/operator>>#6` and `#9`; always extracts `action` via `ByteBuffer/operator>>#9`.

**ReadFromWorldPacket#8**
Deserializes `LeaveBattlefield`. For clients > 1.8.4, extracts `mapId` via `ByteBuffer/operator>>#9`; otherwise, reads nothing.

**ReadFromWorldPacket#7**
Deserializes `BattlemasterJoin` (clients > 1.6.1). Extracts `guid` via `ObjectGuid/operator>>`, `mapId` via `ByteBuffer/operator>>#6`, `instanceId` via `ByteBuffer/operator>>#9`, and `joinAsGroup` via `ByteBuffer/operator>>#9`.

**ReadFromWorldPacket#4**
Deserializes `BattlefieldJoin`. Extracts `mapId` via `ByteBuffer/operator>>#9`.

---

<!-- machine-true, projected from graph.json -->

## Map — game_Server_Packets_Battleground

*Source:* Battleground.cpp, Battleground.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ReadFromWorldPacket#5 | method | ByteBuffer/operator>>#9 | — | — |
| ReadFromWorldPacket | method | ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket#2 | method | ObjectGuid/operator>> | — | — |
| BattlefieldListRequest | ctor | — | — | — |
| ReadFromWorldPacket#6 | method | ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket#3 | method | ByteBuffer/operator>>#6, ByteBuffer/operator>>#9 | — | — |
| ReadFromWorldPacket#8 | method | ByteBuffer/operator>>#9 | — | — |
| ReadFromWorldPacket#7 | method | ByteBuffer/operator>>#6, ByteBuffer/operator>>#9, ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket#4 | method | ByteBuffer/operator>>#9 | — | — |
