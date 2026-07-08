# ChannelModerate

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ChannelModerate

## Purpose & Responsibilities

`ChannelModerate` is a lightweight data structure within the `WorldPackets::Channel` namespace that represents a specific client-to-server network message: `CMSG_CHANNEL_MODERATE`. Its sole responsibility is to encapsulate the raw data received from a client when a player requests to toggle the "moderate" status of a specific chat channel. This status typically controls whether non-moderators can send messages to the channel owner or moderators only.

The class acts as a passive container. It does not contain logic for validation, permission checking, or state mutation. Instead, it provides a typed interface (`channelName`) for higher-level game logic handlers (such as `ChatHandler` or similar session managers) to access the target channel's name after the packet has been deserialized.

## Member-by-Member Behavior

### Construction and Initialization
**`ChannelModerate()`**
This is the default constructor for the class. It performs two critical initialization steps:
1.  **Base Class Initialization**: It invokes the `ClientPacket` constructor, passing the constant `CMSG_CHANNEL_MODERATE`. This registers the packet type with the base class, allowing the network layer to identify incoming binary streams as this specific command.
2.  **Member Default Initialization**: The `std::string` member `channelName` is default-initialized to an empty string.

### Data Deserialization
**`ReadFromWorldPacket(WorldPacket& recv_data)`**
Although declared in the header, the implementation is not provided in the source snippet. However, based on the pattern established by sibling classes in `Channel.h` (e.g., `ChannelJoin`, `ChannelLeave`) and the inherited interface from `ClientPacket`, this method is responsible for extracting the `channelName` from the incoming `WorldPacket` buffer. It likely reads a null-terminated string or a fixed-length string field corresponding to the channel identifier.

## Cross-Unit Boundaries

*   **Calls Out**: None. The class does not invoke methods in other units.
*   **Called By**: While the MAP indicates no explicit callers, in practice, instances of `ChannelModerate` are constructed by the network dispatch system (likely within `WorldSession` or a packet router) when a `CMSG_CHANNEL_MODERATE` opcode is detected. The handler for this opcode will then call `ReadFromWorldPacket` and subsequently access the `channelName` member to perform the moderation toggle logic.

## Data Model

This unit does not interact directly with any database tables. It operates entirely in memory as part of the network packet processing pipeline. Any persistence of channel moderation states would be handled by downstream units (e.g., `Channel` class or `ChatHandler`) after this packet has been processed.

## Notable Implementation Details

*   **Final Class**: The class is marked `final`, preventing inheritance. This enforces a strict, flat hierarchy for packet types, ensuring that `ChannelModerate` cannot be extended with additional behavior or data fields.
*   **Namespace Organization**: It resides in `WorldPackets::Channel`, grouping all channel-related client packets together. This modularization aids in maintainability and scope resolution.
*   **String Handling**: The `channelName` is stored as a `std::string`. This implies that the deserialization process (`ReadFromWorldPacket`) handles memory allocation and copying of the channel name from the raw packet buffer into this managed string object.
*   **No Validation Logic**: The class contains no logic to validate if the channel exists, if the player has permissions to moderate, or if the channel name format is correct. These checks are deferred to the business logic layer that consumes this packet.

## Member Reference

**`ChannelModerate`**
Default constructor. Initializes the base `ClientPacket` with the opcode `CMSG_CHANNEL_MODERATE` and default-initializes the `channelName` member.

---

<!-- machine-true, projected from graph.json -->

## Map — ChannelModerate

*Source:* Channel.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ChannelModerate | ctor | — | — | — |
