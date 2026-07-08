# GuildSetOfficerNote

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# GuildSetOfficerNote

## Purpose & Responsibilities

`GuildSetOfficerNote` is a client-side packet structure within the `WorldPackets::Guild` namespace, defined in `Guild.h`. Its sole responsibility is to represent the incoming network message `CMSG_GUILD_SET_OFFICER_NOTE` sent by a client when a guild officer attempts to set or modify the private "officer note" for a specific guild member.

This class acts as a data container and deserialization target. It holds two fields:
1.  `playerName`: The name of the guild member whose note is being modified.
2.  `note`: The text content of the officer note.

As a `ClientPacket`, it inherits the mechanism for reading raw binary data from the network stream into these structured fields via the `ReadFromWorldPacket` method. This unit does not contain logic for validation, permission checking, or database persistence; those responsibilities lie in the handlers that consume this packet object.

## Member-by-Member Behavior

### Construction
**`GuildSetOfficerNote()`**
The constructor initializes the packet with the opcode `CMSG_GUILD_SET_OFFICER_NOTE`. It ensures the packet is correctly typed for the network layer to route it to the appropriate handler. No other initialization occurs here; the `playerName` and `note` strings are default-constructed (empty).

### Deserialization
**`ReadFromWorldPacket(WorldPacket& recv_data)`**
Although the implementation is not shown in the provided source snippet (it is declared but defined elsewhere, likely in a corresponding `.cpp` file or inline in a different context not provided), this virtual method is responsible for extracting the `playerName` and `note` strings from the raw `WorldPacket` buffer. Based on standard Mangos/WoW packet structures, this typically involves reading a string followed by another string from the binary stream.

## Cross-Unit Boundaries

*   **Calls Out:** None. This unit is a pure data structure with no outgoing dependencies listed in the MAP.
*   **Called By:** None listed in the MAP. In practice, this packet is instantiated by the network layer when the opcode `CMSG_GUILD_SET_OFFICER_NOTE` is received, and then passed to a handler (e.g., in `GuildHandler.cpp`) which processes the request. However, since no specific caller is listed in the MAP, we strictly note that no cross-unit calls are documented for this unit.

## Data Model

This unit does not directly interact with any database tables. It is a transient in-memory representation of a network message. Any database updates resulting from this packet (such as updating the `guild_member` table's `officerNote` field) are performed by the handler logic that consumes this packet, not by the packet class itself.

## Notable Implementation Details

*   **String Storage:** Both `playerName` and `note` are stored as `std::string`. This implies that the deserialization logic must handle variable-length string extraction correctly, respecting the encoding expected by the client version (typically UTF-8 or locale-specific depending on the build).
*   **No Validation:** The class itself performs no validation. It does not check if the player exists, if the sender has officer rights, or if the note length exceeds limits. These checks are the responsibility of the consuming handler.
*   **Inheritance:** It inherits from `ClientPacket`, which provides the base infrastructure for network packet handling, including the opcode registration and the interface for `ReadFromWorldPacket`.

## Member Reference

**GuildSetOfficerNote**
Constructor that initializes the packet with the opcode `CMSG_GUILD_SET_OFFICER_NOTE`. It prepares the object to receive data from the network stream.

---

<!-- machine-true, projected from graph.json -->

## Map — GuildSetOfficerNote

*Source:* Guild.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GuildSetOfficerNote | ctor | — | — | — |
