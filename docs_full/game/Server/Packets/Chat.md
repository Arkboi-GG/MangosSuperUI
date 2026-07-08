# Chat

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Chat

## Purpose & Responsibilities

The `Chat` unit defines the `ChatMessage` class, which represents the client-to-server network packet for chat messages (`CMSG_MESSAGECHAT`). Its sole responsibility is to deserialize raw binary data from a `WorldPacket` into structured fields describing the type of chat message, its language, the target (channel or player name), and the message content itself. This unit acts as a thin deserialization layer within the networking stack, converting opaque byte streams into accessible C++ objects for higher-level game logic processing.

## Member-by-Member Behavior

### Packet Construction
**`ChatMessage`** (Constructor)
Initializes a new instance of the `ChatMessage` packet. It sets the base packet identifier to `CMSG_MESSAGECHAT` via the `ClientPacket` base class and initializes the `type` and `lang` fields to zero. The string fields (`whisperTargetOrChannel`, `message`) are default-initialized by the compiler.

### Deserialization
**`ReadFromWorldPacket`**
This method parses the incoming `WorldPacket` (`recv_data`) into the member variables of the `ChatMessage` object. It performs the following steps in order:
1.  Reads the chat message **type** (e.g., SAY, YELL, WHISPER, CHANNEL) into the `type` field.
2.  Reads the **language** code into the `lang` field.
3.  Conditionally reads the **target**: If the `type` indicates a channel message (`CHAT_MSG_CHANNEL`) or a whisper (`CHAT_MSG_WHISPER`), it reads the target name (either the channel name or the recipient player's name) into `whisperTargetOrChannel`. For other chat types (like SAY or YELL), this field remains empty/uninitialized from the packet stream.
4.  Reads the actual **message text** into the `message` field.

## Cross-Unit Boundaries

### Outbound Calls
*   **`ByteBuffer/operator>>`**: `ReadFromWorldPacket` relies heavily on the extraction operator overloaded in `ByteBuffer` (via `WorldPacket`) to parse primitive types (`uint32`) and strings (`std::string`) from the packet buffer. Specifically, it uses the standard string extraction and integer extraction operators.

### Inbound Calls
*   None listed in the MAP. However, logically, this method is called by the network handler (likely in `WorldSession` or a central packet dispatcher) after a `CMSG_MESSAGECHAT` packet is received from the client.

## Data Model

This unit does not interact with any database tables. It operates entirely on in-memory network packet data.

## Notable Implementation Details

*   **Conditional Parsing**: The deserialization logic is not uniform for all chat types. The presence of the `whisperTargetOrChannel` string in the packet payload is strictly conditional on the `type` being `CHAT_MSG_CHANNEL` or `CHAT_MSG_WHISPER`. If the code were to attempt reading this string for a `CHAT_MSG_SAY` type, it would consume bytes intended for the `message` field, leading to corruption of the message content and potential parsing errors for subsequent packets.
*   **String Handling**: The `whisperTargetOrChannel` and `message` fields are `std::string` objects. The `operator>>` for strings in `WorldPacket` typically handles null-terminated strings or length-prefixed strings depending on the specific `ByteBuffer` implementation details in Mangos. The code assumes the client sends these strings in the expected format.
*   **Namespace Structure**: The class resides in `WorldPackets::Chat`, indicating a modular organization of network packet handlers, separating chat logic from other subsystems like combat or movement.

## Member Reference

**`ChatMessage`**
Constructor that initializes the packet ID to `CMSG_MESSAGECHAT` and resets numeric fields to zero.

**`ReadFromWorldPacket`**
Deserializes the `WorldPacket` into `type`, `lang`, `whisperTargetOrChannel` (only for whispers/channels), and `message`.

---

<!-- machine-true, projected from graph.json -->

## Map — Chat

*Source:* Chat.cpp, Chat.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ReadFromWorldPacket | method | ByteBuffer/operator>>, ByteBuffer/operator>>#9 | — | — |
| ChatMessage | ctor | — | — | — |
