# GuildRank

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# GuildRank

**GuildRank** is a client-to-server network packet structure within the `WorldPackets::Guild` namespace. It represents the `CMSG_GUILD_RANK` message, which is sent by the game client to the server to modify the properties of an existing guild rank. Specifically, this packet carries the identifier of the rank being modified, the new permission bitmask (`rights`) assigned to that rank, and the updated display name (`rankName`) for the rank.

As a `ClientPacket`, its primary responsibility is to deserialize binary data received from the client into structured C++ fields. It does not contain business logic for validating permissions or updating the database; those responsibilities lie with the handler that processes this packet after deserialization.

## Member-by-Member Behavior

The unit consists of a single constructor and relies on the inherited `ReadFromWorldPacket` method (defined in the base class or implemented elsewhere for this specific type, though the declaration is present in the header) to perform the actual data extraction.

*   **Constructor (`GuildRank()`)**: Initializes the packet object. It sets the default values for the member variables: `rankId` and `rights` are initialized to `0`, and `rankName` is constructed as an empty string. Crucially, it invokes the base class `ClientPacket` constructor, passing the opcode `CMSG_GUILD_RANK`. This registration ensures that when the network layer receives a packet with this opcode, it instantiates this specific class to handle the payload.

## Cross-Unit Boundaries

*   **Inheritance**: Inherits from `WorldPackets::ClientPacket`. This establishes the contract that this object will be populated by reading from a `WorldPacket` instance via the `ReadFromWorldPacket` method.
*   **Usage**: This packet is typically instantiated by the network input handler when a `CMSG_GUILD_RANK` opcode is detected. After instantiation and population, the packet object is passed to a command handler or guild manager service (not shown in this unit) which extracts `rankId`, `rights`, and `rankName` to execute the rank update logic.

## Data Model

This unit does not directly interact with database tables. It operates purely on network data structures. The `rankId`, `rights`, and `rankName` fields correspond to columns in the `guild_rank` table (typically `rank`, `rights`, and `rname`), but the mapping and persistence are handled by other units that consume this packet.

## Notable Implementation Details

*   **Default Initialization**: The member variables `rankId` and `rights` are explicitly initialized to `0` in the class definition. This is a defensive measure ensuring that if the deserialization process fails or is skipped, these fields hold a safe default value rather than garbage memory.
*   **Opcode Specificity**: The packet is strictly tied to `CMSG_GUILD_RANK`. Any attempt to use this structure for creating a new rank (which might use a different opcode like `CMSG_GUILD_ADD_RANK`, handled by the separate `GuildAddRank` class in the same header) would be incorrect. This distinction implies that `GuildRank` is exclusively for *modifying* existing ranks, whereas `GuildAddRank` is for *creating* them.
*   **Namespace Isolation**: Defined within `WorldPackets::Guild`, keeping all guild-related network protocols logically grouped and preventing naming collisions with other packet types in the broader `WorldPackets` namespace.

## Member Reference

**GuildRank**
Constructor for the `GuildRank` packet. Initializes `rankId` and `rights` to `0` and `rankName` to an empty string. Registers the packet with the opcode `CMSG_GUILD_RANK` via the base `ClientPacket` constructor.

---

<!-- machine-true, projected from graph.json -->

## Map — GuildRank

*Source:* Guild.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GuildRank | ctor | — | — | — |
