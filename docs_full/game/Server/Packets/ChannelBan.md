# ChannelBan

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

The `ChannelBan` class is a lightweight data structure within the `WorldPackets::Channel` namespace, designed to represent a specific client-to-server network message: `CMSG_CHANNEL_BAN`. Its sole responsibility is to encapsulate the raw data received from a client when a player attempts to ban another player from a chat channel. It inherits from `ClientPacket`, indicating it is part of the inbound packet parsing layer of the Mangos server architecture. This unit does not perform any logic, validation, or database interaction; it merely holds the `channelName` and `playerName` strings extracted from the network stream.

## Member-by-Member Behavior

This unit contains only one member: the default constructor.

*   **Construction**: The `ChannelBan` constructor initializes the base `ClientPacket` with the opcode `CMSG_CHANNEL_BAN`. This registration allows the server's packet dispatcher to identify incoming bytes as a channel ban request and instantiate this specific object for further processing. The member variables `channelName` and `playerName` are left empty upon construction; they are populated later by the `ReadFromWorldPacket` method (which is declared in this header but implemented elsewhere, likely in a corresponding `.cpp` file not included in this unit's scope, or potentially in the base class if overridden implicitly, though the signature suggests an override).

## Cross-Unit Boundaries

*   **Calls Out**: None. The constructor performs no external calls.
*   **Called By**: The MAP indicates no external callers are explicitly tracked for this constructor, which is typical for packet objects instantiated internally by the server's network handler upon receiving the `CMSG_CHANNEL_BAN` opcode.

## Data Model

This unit does not interact with any database tables. It operates entirely in memory as a transient representation of a network packet.

## Notable Implementation Details

*   **Final Class**: The class is marked `final`, preventing inheritance. This enforces a strict hierarchy where `ChannelBan` is a leaf node in the packet type system.
*   **String Storage**: The class uses `std::string` for both `channelName` and `playerName`. This implies that the `ReadFromWorldPacket` implementation (not shown in this unit's source but declared in the header) will allocate heap memory for these strings during deserialization. Care must be taken in the broader system to ensure these strings are validated for length and content before use to prevent potential abuse or crashes.
*   **Namespace**: Located in `WorldPackets::Channel`, this places it firmly within the server's networking abstraction layer, separating protocol handling from game logic.

## Member Reference

**ChannelBan**
Default constructor for the `ChannelBan` packet. Initializes the base `ClientPacket` with the opcode `CMSG_CHANNEL_BAN`. Does not populate the `channelName` or `playerName` members; these are filled during packet deserialization.

---

<!-- machine-true, projected from graph.json -->

## Map — ChannelBan

*Source:* Channel.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ChannelBan | ctor | — | — | — |
