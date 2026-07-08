# GuildRemove

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# GuildRemove

**Purpose & Responsibilities**

`GuildRemove` is a client-side packet structure within the `WorldPackets::Guild` namespace, responsible for representing the `CMSG_GUILD_REMOVE` message sent from a game client to the server. Its sole responsibility is to deserialize the raw binary data of this specific network packet into a structured object containing the name of the player character targeted for removal from a guild. It acts as the initial data carrier in the guild management workflow, translating network bytes into a usable string payload for downstream processing by the server's guild management logic.

**Member-by-Member Behavior**

The unit consists of a single class, `GuildRemove`, which inherits from `ClientPacket`. It contains one public data member and one constructor defined in this translation unit.

*   **Data Member**: `playerName` (`std::string`) stores the name of the character to be removed. This field is populated during the deserialization process handled by the `ReadFromWorldPacket` method (declared in `Guild.h` but implemented elsewhere).
*   **Constructor**: Initializes the base `ClientPacket` with the opcode `CMSG_GUILD_REMOVE`, identifying the packet type to the network layer.

**Cross-Unit Boundaries**

*   **Inheritance**: `GuildRemove` inherits from `ClientPacket` (defined in `Packet.h`). This establishes the contract for network packet handling, including the opcode registration and the interface for reading/writing data.
*   **Dependency**: The member `ReadFromWorldPacket` (declared in `Guild.h`) takes a `WorldPacket&` argument. `WorldPacket` is the low-level binary buffer class used throughout the Mangos codebase for network communication. `GuildRemove` relies on `WorldPacket`'s extraction operators (e.g., `>>`) to parse the string data.
*   **Usage Context**: While the MAP shows no external callers, this class is instantiated by the server's network handler when a `CMSG_GUILD_REMOVE` opcode is received. The handler will call `ReadFromWorldPacket` to populate the `playerName` field, after which the populated `GuildRemove` object is passed to the guild management subsystem (likely a handler function in a different translation unit, such as `GuildHandler.cpp`) for validation and execution.

**Data Model**

This unit does not interact directly with any database tables. It operates purely on in-memory network data structures. The `playerName` string it carries may eventually be used to query the `characters` table or `guild_member` table in downstream handlers, but `GuildRemove` itself performs no I/O operations.

**Notable Implementation Details**

*   **Final Class**: The class is marked `final`, preventing further inheritance. This is consistent with the design of leaf-node packet classes that have no need for polymorphic extension.
*   **String Extraction**: The `ReadFromWorldPacket` method (implied by the standard pattern in `Guild.h` and similar packets) uses the `WorldPacket` extraction operator to read a string. In World of Warcraft protocol, strings are typically null-terminated or prefixed with length depending on the client version. The `WorldPacket` class abstracts this complexity, ensuring `playerName` is correctly populated regardless of the underlying binary format.
*   **No Validation**: The unit performs no validation on the `playerName`. It does not check for empty strings, invalid characters, or existence. This is intentional; validation is the responsibility of the business logic layer that consumes this packet, not the packet deserialization layer.
*   **Namespace Organization**: Located in `WorldPackets::Guild`, this grouping keeps all guild-related network structures together, aiding maintainability and reducing naming collisions.

## Member Reference

**GuildRemove**
The constructor for the `GuildRemove` class. It initializes the base `ClientPacket` with the opcode `CMSG_GUILD_REMOVE`, signaling to the network layer that this object represents a request to remove a player from a guild. It does not perform any additional initialization.

---

<!-- machine-true, projected from graph.json -->

## Map — GuildRemove

*Source:* Guild.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GuildRemove | ctor | — | — | — |
