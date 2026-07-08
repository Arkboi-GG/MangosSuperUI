# GuildMOTD

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# GuildMOTD

## Purpose & Responsibilities

`GuildMOTD` is a client-side packet structure within the `WorldPackets::Guild` namespace, responsible for encapsulating the **Message of the Day** (MOTD) data sent from a game client to the server. It represents the `CMSG_GUILD_MOTD` opcode, which triggers the update of a guild's public message displayed to its members.

This unit is part of the network layer's deserialization infrastructure. Its sole responsibility is to define the memory layout and provide the interface for reading the MOTD string from an incoming binary network packet (`WorldPacket`). It does not perform validation, persistence, or business logic; those concerns are handled by the handlers that process this packet after it is constructed.

## Member-by-Member Behavior

The unit contains a single member: the constructor.

### Construction

**`GuildMOTD()`**  
The default constructor initializes the `GuildMOTD` instance. It explicitly calls the base class constructor `ClientPacket(CMSG_GUILD_MOTD)`, registering this packet with the specific opcode `CMSG_GUILD_MOTD`. This association ensures that when the network layer receives a packet with this opcode, it instantiates this specific class for parsing. The member variable `motd` is implicitly default-initialized to an empty string by the compiler, though it will be overwritten during the `ReadFromWorldPacket` phase.

## Cross-Unit Boundaries

*   **Calls Out:** None. The constructor performs no external calls.
*   **Called By:** None listed in the map. In practice, this constructor is invoked by the central packet factory/dispatcher (likely within the `WorldSession` or network handler infrastructure) when a `CMSG_GUILD_MOTD` packet is detected on the wire.
*   **Collaboration:** The `GuildMOTD` object acts as a data carrier. Once constructed and populated via `ReadFromWorldPacket` (defined in the base class hierarchy but implemented in this derived class, though the implementation is not shown in the provided source snippet, the declaration exists), the object is passed to a handler function (e.g., in `GuildHandler.cpp`) which extracts the `motd` string and updates the guild record in the database.

## Data Model

This unit does not directly interact with database tables. It operates purely on network data. The `motd` string it carries corresponds to the `motd` column in the `guild` table in the database, but the mapping and persistence logic reside in other units (specifically, the guild management handlers and the `Guild` class itself).

## Notable Implementation Details

*   **Inheritance:** Inherits from `ClientPacket`, indicating it is a packet originating from the client.
*   **Opcode Association:** Hardcoded to `CMSG_GUILD_MOTD`. Any change in the client protocol regarding this opcode would require updating this constant.
*   **String Handling:** The `motd` field is a `std::string`. The `ReadFromWorldPacket` method (declared but not defined in this header) is responsible for extracting the string from the binary stream. Typically, this involves reading a string terminator or length prefix depending on the WoW version's packet format.
*   **Final Class:** The class is marked `final`, preventing further inheritance. This is appropriate for a leaf packet structure.

## Member Reference

**GuildMOTD**  
Constructor for the `GuildMOTD` packet. Initializes the base `ClientPacket` with the opcode `CMSG_GUILD_MOTD`. Prepares the object to receive the MOTD string from an incoming network packet.

---

<!-- machine-true, projected from graph.json -->

## Map — GuildMOTD

*Source:* Guild.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GuildMOTD | ctor | — | — | — |
