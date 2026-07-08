# TextEmote

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# TextEmote

**Purpose & Responsibilities**

`TextEmote` is a client-side packet structure within the `WorldPackets::Misc` namespace, defined in `Misc.h`. It represents the `CMSG_TEXT_EMOTE` message sent by the game client to the server. Its sole responsibility is to encapsulate the raw binary data of a text-based emote command (such as `/say`, `/yell`, or `/wave`) into a structured C++ object for processing by the server's network handler.

As a `ClientPacket`, it inherits the contract to deserialize incoming network bytes via the `ReadFromWorldPacket` method. The class itself contains no business logic, validation, or side effects; it is purely a data carrier.

## Member-by-Member Behavior

The unit consists of a single constructor and several public data members that define the payload of the packet.

### Construction
**`TextEmote()`**
The default constructor initializes the packet metadata. It calls the base class `ClientPacket` constructor, passing the opcode `CMSG_TEXT_EMOTE`. This registers the packet type with the network dispatcher. It also initializes the three public data members (`textEmote`, `emoteNum`, `guid`) to their default zero/null states.

### Data Members
The following members hold the deserialized content of the packet:

*   **`textEmote` (`uint32`)**: Represents the specific text emote type identifier (e.g., SAY, YELL, WHISPER, EMOTE). This value corresponds to constants defined elsewhere in the codebase (likely in `Chat.h` or similar shared defines) that distinguish between different chat channels or emote actions.
*   **`emoteNum` (`uint32`)**: Represents a secondary emote number. In many World of Warcraft clients, certain text emotes (like `/wave` or `/dance`) are accompanied by a numeric animation ID or variant. For pure text commands like `/say`, this field is often unused or zero.
*   **`guid` (`ObjectGuid`)**: Represents the Global Unique Identifier of the target entity. This is relevant for directed emotes, such as whispering to a specific player or performing an emote towards a specific NPC or player. For broadcast emotes (like `/say`), this GUID may be null or represent the sender depending on the specific client version's protocol implementation.

## Cross-Unit Boundaries

*   **Calls Out**: None. The constructor only invokes the base class `ClientPacket` constructor. The `ReadFromWorldPacket` method (declared but not implemented in this header) will eventually call methods on the `WorldPacket` object passed to it, but those interactions occur in the corresponding `.cpp` implementation file, not in this header definition.
*   **Called By**: None listed in the MAP. In practice, this class is instantiated by the network layer when a `CMSG_TEXT_EMOTE` packet is received, and then passed to a handler function (likely in a `ChatHandler` or `Player` class) which reads the fields.

## Data Model

This unit does not interact directly with any database tables. It operates entirely in memory as part of the network I/O pipeline.

## Notable Implementation Details

*   **Inheritance**: Inherits from `ClientPacket`, implying it is part of a larger packet handling framework where `ReadFromWorldPacket` is the standard interface for deserialization.
*   **Default Initialization**: All data members are explicitly initialized to `0` or default-constructed in the constructor. This ensures that if `ReadFromWorldPacket` fails or is not called, the object remains in a known safe state.
*   **Protocol Specificity**: The presence of both `textEmote` and `emoteNum` suggests support for complex emotes that combine a text channel/type with a specific animation or target, typical of the WoW Classic / TBC era protocols supported by Mangos.

## Member Reference

**TextEmote**
The default constructor for the `TextEmote` packet. It initializes the base `ClientPacket` with the `CMSG_TEXT_EMOTE` opcode and sets the data members `textEmote`, `emoteNum`, and `guid` to their default values (0, 0, and empty GUID respectively).

---

<!-- machine-true, projected from graph.json -->

## Map — TextEmote

*Source:* Misc.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| TextEmote | ctor | — | — | — |
