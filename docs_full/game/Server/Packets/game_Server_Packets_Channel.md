# game_Server_Packets_Channel

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# game_Server_Packets_Channel

## Purpose & Responsibilities

The `game_Server_Packets_Channel` unit defines the server-side deserialization structures for client-to-server network packets related to in-game chat channels. It resides within the `WorldPackets::Channel` namespace and consists of a series of `final` classes, each inheriting from `ClientPacket`.

Each class corresponds to a specific command opcode (e.g., `CMSG_JOIN_CHANNEL`, `CMSG_CHANNEL_KICK`) sent by the client. The primary responsibility of this unit is to define the data layout (member variables) for these commands and implement the `ReadFromWorldPacket` method to extract raw binary data from the incoming `WorldPacket` buffer into strongly-typed C++ fields. This unit performs no business logic, validation, or database interaction; it strictly handles the parsing of the network protocol payload.

## Member-by-Member Behavior

The members in this unit are all implementations of the `ReadFromWorldPacket` virtual method for various channel-related packet types. They follow a uniform pattern: using the stream extraction operator (`>>`) on the provided `WorldPacket` reference to populate public string members.

### Channel Membership Operations
These packets handle joining and leaving channels.
*   **JoinChannel**: Extracts `channelName` and `channelPassword`. This is the only join-related packet that accepts a password, implying support for private or protected channels.
*   **LeaveChannel**: Extracts only `channelName`.

### Channel Information Queries
These packets request information about channels.
*   **ChannelList**: Extracts `channelName`. Likely used to query details about a specific channel or list membership.
*   **ChannelOwner**: Extracts `channelName`. Used to query the current owner of a channel.

### Channel Administration & Moderation
These packets involve changing permissions or roles within a channel. Most require both a target channel and a target player.
*   **ChannelSetOwner**: Extracts `channelName` and `playerName`. Transfers ownership.
*   **ChannelModerator**: Extracts `channelName` and `playerName`. Grants moderator status.
*   **ChannelUnmoderator**: Extracts `channelName` and `playerName`. Revokes moderator status.
*   **ChannelMute**: Extracts `channelName` and `playerName`. Mutes a user.
*   **ChannelUnmute**: Extracts `channelName` and `playerName`. Unmutes a user.
*   **ChannelKick**: Extracts `channelName` and `playerName`. Removes a user from the channel.
*   **ChannelBan**: Extracts `channelName` and `playerName`. Bans a user from the channel.
*   **ChannelUnban**: Extracts `channelName` and `playerName`. Removes a ban.
*   **ChannelInvite**: Extracts `channelName` and `playerName`. Invites a specific player to join.
*   **ChannelPassword**: Extracts `channelName` and `password`. Likely used to set or change the channel's access password.

### Channel Settings
These packets toggle global settings for a channel.
*   **ChannelAnnouncements**: Extracts `channelName`. Toggles whether announcements (from game masters or system events) are broadcast to the channel.
*   **ChannelModerate**: Extracts `channelName`. Toggles whether the channel requires moderation for messages.

## Cross-Unit Boundaries

### Outbound Calls
All `ReadFromWorldPacket` methods in this unit call into **ByteBuffer/operator>>**.
*   **Collaboration**: The `WorldPacket` class inherits from or wraps a `ByteBuffer`. The `operator>>` is overloaded to parse primitive types and strings from the underlying byte buffer.
*   **Direction**: Data flows from the `WorldPacket` (network buffer) into the member variables of the packet struct (e.g., `channelName`).
*   **Why**: To convert the raw binary stream received over the TCP connection into usable C++ `std::string` objects.

### Inbound Calls
Only one member in this unit is called by other units according to the map:
*   **JoinChannel** is called by:
    *   **AiBotAI.Main/UpdateAI**: Indicates that AI bots simulate joining channels as part of their behavioral loop.
    *   **ChatHandler.CharacterCommands/HandleChannelJoinCommand**: Indicates that when a player uses a chat command to join a channel, the handler constructs or triggers this packet structure to process the intent.

*Note: While the map lists only `JoinChannel` as being called by other units, in practice, the framework likely instantiates and reads all these packet types when corresponding opcodes are received from the network socket. The map highlights explicit construction or invocation points in the business logic layer.*

## Data Model

This unit does not interact with any database tables. It operates entirely on in-memory network buffers.

## Notable Implementation Details

1.  **No Validation**: The `ReadFromWorldPacket` methods perform zero validation. They blindly trust the client-provided data. Empty strings, excessively long names, or invalid characters are accepted as-is. Validation (e.g., checking if a channel exists, if the player has permission, or if the name format is valid) occurs in the calling handlers (like `ChatHandler`), not here.
2.  **Public Members**: All data fields (`channelName`, `playerName`, etc.) are declared `public` in the header. This allows direct access by the handlers that consume these packets, avoiding the need for getter methods.
3.  **Uniform Parsing Logic**: Every method follows the exact same pattern: sequential extraction of strings. There are no conditional branches, loops, or complex parsing logic within this unit.
4.  **Final Classes**: Each packet class is marked `final`, preventing inheritance. This ensures the packet structure is fixed and cannot be extended, which is appropriate for a strict network protocol definition.
5.  **Explicit Constructors**: Each class has an explicit constructor that passes a specific `CMSG_*` constant to the base `ClientPacket` class. This binds the C++ type to the specific network opcode expected by the client.

## Member Reference

**ReadFromWorldPacket#15** (ChannelAnnouncements): Reads `channelName` from the packet buffer.
**ReadFromWorldPacket#16** (ChannelModerate): Reads `channelName` from the packet buffer.
**JoinChannel**: Reads `channelName` and `channelPassword` from the packet buffer. Called by `AiBotAI.Main/UpdateAI` and `ChatHandler.CharacterCommands/HandleChannelJoinCommand`.
**ReadFromWorldPacket#5** (ChannelSetOwner): Reads `channelName` and `playerName` from the packet buffer.
**ReadFromWorldPacket#10** (ChannelInvite): Reads `channelName` and `playerName` from the packet buffer.
**ReadFromWorldPacket#11** (ChannelKick): Reads `channelName` and `playerName` from the packet buffer.
**ReadFromWorldPacket#9** (ChannelMute): Reads `channelName` and `playerName` from the packet buffer.
**ReadFromWorldPacket#7** (ChannelModerator): Reads `channelName` and `playerName` from the packet buffer.
**ReadFromWorldPacket#13** (ChannelBan): Reads `channelName` and `playerName` from the packet buffer.
**ReadFromWorldPacket#8** (ChannelUnmoderator): Reads `channelName` and `playerName` from the packet buffer.
**ReadFromWorldPacket#14** (ChannelUnban): Reads `channelName` and `playerName` from the packet buffer.
**ReadFromWorldPacket#3** (ChannelList): Reads `channelName` from the packet buffer.
**ReadFromWorldPacket#4** (ChannelPassword): Reads `channelName` and `password` from the packet buffer.
**ReadFromWorldPacket#2** (LeaveChannel): Reads `channelName` from the packet buffer.
**ReadFromWorldPacket#12** (ChannelUnmute): Reads `channelName` and `playerName` from the packet buffer.
**ReadFromWorldPacket** (JoinChannel - duplicate entry in map referring to same logic): Reads `channelName` and `channelPassword` from the packet buffer.
**ReadFromWorldPacket#6** (ChannelOwner): Reads `channelName` from the packet buffer.

---

<!-- machine-true, projected from graph.json -->

## Map — game_Server_Packets_Channel

*Source:* Channel.cpp, Channel.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ReadFromWorldPacket#15 | method | ByteBuffer/operator>> | — | — |
| ReadFromWorldPacket#16 | method | ByteBuffer/operator>> | — | — |
| JoinChannel | ctor | — | AiBotAI.Main/UpdateAI, ChatHandler.CharacterCommands/HandleChannelJoinCommand | — |
| ReadFromWorldPacket#5 | method | ByteBuffer/operator>> | — | — |
| ReadFromWorldPacket#10 | method | ByteBuffer/operator>> | — | — |
| ReadFromWorldPacket#11 | method | ByteBuffer/operator>> | — | — |
| ReadFromWorldPacket#9 | method | ByteBuffer/operator>> | — | — |
| ReadFromWorldPacket#7 | method | ByteBuffer/operator>> | — | — |
| ReadFromWorldPacket#13 | method | ByteBuffer/operator>> | — | — |
| ReadFromWorldPacket#8 | method | ByteBuffer/operator>> | — | — |
| ReadFromWorldPacket#14 | method | ByteBuffer/operator>> | — | — |
| ReadFromWorldPacket#3 | method | ByteBuffer/operator>> | — | — |
| ReadFromWorldPacket#4 | method | ByteBuffer/operator>> | — | — |
| ReadFromWorldPacket#2 | method | ByteBuffer/operator>> | — | — |
| ReadFromWorldPacket#12 | method | ByteBuffer/operator>> | — | — |
| ReadFromWorldPacket | method | ByteBuffer/operator>> | — | — |
| ReadFromWorldPacket#6 | method | ByteBuffer/operator>> | — | — |
