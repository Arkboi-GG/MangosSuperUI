# SaveGuildEmblem

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# SaveGuildEmblem

**SaveGuildEmblem** is a client-side packet handler within the `WorldPackets::Guild` namespace, responsible for deserializing the `MSG_SAVE_GUILD_EMBLEM` message sent by the game client. This packet carries the visual configuration data for a guild's emblem, including style indices and color codes for the emblem itself, its border, and the background. It is part of the broader guild management subsystem, specifically handling the cosmetic customization aspect initiated by players interacting with guild vendors or interface elements.

The class inherits from `ClientPacket`, indicating it represents data arriving from a connected player client. Its primary responsibility is to extract raw binary data from a `WorldPacket` buffer and populate its public member variables with strongly-typed C++ values, making the data accessible to higher-level game logic handlers that process the actual save operation.

## Member-by-Member Behavior

### Construction and Initialization
The **SaveGuildEmblem** constructor initializes the packet object. It sets the internal packet opcode to `MSG_SAVE_GUILD_EMBLEM`, which identifies this specific message type within the network protocol. It also initializes all integer fields (`emblemStyle`, `emblemColor`, `borderStyle`, `borderColor`, `backgroundColor`) to zero, providing safe default values before deserialization occurs. The `vendorGuid` field is default-initialized to an empty `ObjectGuid`.

### Deserialization
Although the MAP only lists the constructor, the class declaration in `Guild.h` includes a virtual method `ReadFromWorldPacket`. This method is overridden from the base `ClientPacket` class and is responsible for reading the binary payload from the incoming network packet. Based on the member variables declared in the class, this method will sequentially extract:
1.  An `ObjectGuid` representing the vendor or entity associated with the emblem save request.
2.  Five `int32` values representing the style and color choices for the emblem components.

## Cross-Unit Boundaries

*   **Calls out:** None. The `SaveGuildEmblem` unit itself does not call into other units; it is a passive data structure with a deserialization routine.
*   **Called by:** Other units in the server codebase (not listed in the MAP but implied by the architecture) will instantiate `SaveGuildEmblem` and call its `ReadFromWorldPacket` method when a `MSG_SAVE_GUILD_EMBLEM` packet is received from the network layer. After deserialization, the populated object is typically passed to a handler function (e.g., in a `GuildHandler.cpp` or similar module) that validates the data and persists it to the database.

## Data Model

This unit does not directly interact with database tables. It operates purely on network packet data. The persistence of the emblem data described by this packet is handled by other parts of the system after this packet has been successfully parsed. Therefore, no database tables are touched by this specific translation unit.

## Notable Implementation Details

*   **Conditional Compilation:** The presence of `SaveGuildEmblem` is not guarded by preprocessor directives in the provided snippet, unlike `GuildChangeInfoText` which is guarded by `#if SUPPORTED_CLIENT_BUILD > CLIENT_BUILD_1_8_4`. This suggests that `MSG_SAVE_GUILD_EMBLEM` is supported across all client builds compiled by this version of the server, or that the guard is applied elsewhere.
*   **Vendor Guid:** The inclusion of `vendorGuid` is notable. Typically, guild emblem changes might be tied to the guild leader or officer permissions. The presence of a vendor GUID suggests that the client sends the identifier of the NPC vendor being interacted with, possibly for validation purposes (e.g., ensuring the player is actually standing near a guild vendor) or for logging/debugging.
*   **Color Encoding:** The colors are stored as `int32`. In World of Warcraft's protocol, these are often packed ARGB or RGBA values. The server logic consuming this packet must interpret these integers correctly to store them in the database (often as separate R, G, B bytes or as a single hex value depending on the table schema).

## Member Reference

**SaveGuildEmblem**: Constructor for the `SaveGuildEmblem` packet class. Initializes the packet opcode to `MSG_SAVE_GUILD_EMBLEM` and sets all emblem style/color integer fields to zero. Prepares the object for deserialization of incoming client data regarding guild emblem customization.

---

<!-- machine-true, projected from graph.json -->

## Map — SaveGuildEmblem

*Source:* Guild.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| SaveGuildEmblem | ctor | — | — | — |
