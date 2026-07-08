# GuildChangeInfoText

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# GuildChangeInfoText

## Purpose & Responsibilities

`GuildChangeInfoText` is a client-side packet structure within the `WorldPackets::Guild` namespace, responsible for handling the `CMSG_GUILD_INFO_TEXT` message sent from the game client to the server. Its sole responsibility is to deserialize the incoming network data into a structured format, specifically extracting the new "info text" string that a guild leader or officer intends to set for the guild. This packet is conditionally compiled and only exists for client builds newer than version 1.8.4 (`CLIENT_BUILD_1_8_4`), indicating that the ability to change guild info text via this specific opcode was introduced or modified in later client versions.

## Member-by-Member Behavior

The unit contains a single member: the constructor.

*   **`GuildChangeInfoText`**: The default constructor initializes the base `ClientPacket` class with the opcode `CMSG_GUILD_INFO_TEXT`. It does not perform any data extraction itself; that logic resides in the overridden `ReadFromWorldPacket` method (which is declared in the header but whose implementation is not part of this specific translation unit's scope for documentation purposes, though it is implied to populate the `infoText` member). The constructor ensures the packet is correctly identified by the server's packet dispatching system.

## Cross-Unit Boundaries

*   **Calls Out**: None. The constructor does not invoke any external functions or classes beyond the base class initialization.
*   **Called By**: None listed in the map. In practice, this packet is instantiated by the server's network layer when it receives a raw `CMSG_GUILD_INFO_TEXT` message from a client. The server's packet handler will then call `ReadFromWorldPacket` to populate the `infoText` field before passing the object to the guild management logic.

## Data Model

This unit does not interact directly with any database tables. It operates purely on in-memory network packet data. The `infoText` string extracted from the packet will eventually be persisted to the database by higher-level guild management code (likely involving the `guild` table), but `GuildChangeInfoText` itself has no SQL queries or table dependencies.

## Notable Implementation Details

*   **Conditional Compilation**: The entire class definition is wrapped in `#if SUPPORTED_CLIENT_BUILD > CLIENT_BUILD_1_8_4`. This means the packet structure is completely absent from the binary if the server is configured to support older clients that do not use the `CMSG_GUILD_INFO_TEXT` opcode. Maintainers must ensure that any code handling guild info text changes checks for this build version or uses alternative opcodes for older clients.
*   **String Storage**: The `infoText` member is a `std::string`, implying that the `ReadFromWorldPacket` implementation (not shown here but referenced) reads a null-terminated string or a length-prefixed string from the packet buffer. No validation of the string's content or length is performed at the packet level; such validation would occur in the subsequent business logic.
*   **Base Class Dependency**: As a `ClientPacket`, it inherits the mechanism for identifying the packet type (`CMSG_GUILD_INFO_TEXT`) and providing the interface for deserialization (`ReadFromWorldPacket`).

## Member Reference

**GuildChangeInfoText**
Constructor for the `GuildChangeInfoText` packet. Initializes the base `ClientPacket` with the opcode `CMSG_GUILD_INFO_TEXT`. This class is only compiled for client builds newer than 1.8.4. It prepares the object to receive and store the guild info text string from the incoming network packet.

---

<!-- machine-true, projected from graph.json -->

## Map — GuildChangeInfoText

*Source:* Guild.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GuildChangeInfoText | ctor | — | — | — |
