# GuildInvite

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# GuildInvite

**Purpose & Responsibilities**

`GuildInvite` is a client-side packet structure within the `WorldPackets::Guild` namespace, responsible for representing the `CMSG_GUILD_INVITE` message sent from the game client to the server. Its sole responsibility is to encapsulate the raw data payload of a guild invitation request, specifically extracting the name of the player being invited. It acts as a data carrier in the network layer, bridging the binary stream received from the client and the higher-level game logic that processes guild operations.

**Member-by-Member Behavior**

The unit contains a single member: the constructor.

*   **`GuildInvite`**: This default constructor initializes the packet object. It invokes the base class constructor `ClientPacket`, passing the opcode `CMSG_GUILD_INVITE`. This registration ensures that when the network layer receives a packet with this specific opcode, it instantiates a `GuildInvite` object to handle the deserialization. The constructor does not perform any data extraction itself; that duty falls to the inherited `ReadFromWorldPacket` method (defined in the base class or overridden in the derived class, though the override signature is present in the header, the implementation is not part of this specific unit's source scope provided). The member variable `invitedName` is declared as a public `std::string` but is not initialized by the constructor; it remains empty until populated during the packet reading phase.

**Cross-Unit Boundaries**

*   **Calls Out**: None. The constructor only interacts with its base class `ClientPacket`.
*   **Called By**: None listed in the map. In practice, this class is instantiated by the network packet dispatcher when a `CMSG_GUILD_INVITE` opcode is detected. The resulting object is then passed to the game world handler (likely in a separate unit such as `GuildHandler.cpp`) which accesses the `invitedName` field to execute the invitation logic.

**Data Model**

This unit does not interact directly with any database tables. It operates entirely in memory as part of the network packet processing pipeline. The `invitedName` string is transient data derived from the client input.

**Notable Implementation Details**

*   **Final Class**: The class is marked `final`, preventing further inheritance. This is consistent with its role as a leaf node in the packet hierarchy.
*   **Public Data Member**: The `invitedName` field is public. This design choice allows direct access by handlers without needing getter/setter methods, prioritizing simplicity and performance in the packet handling path.
*   **Opcode Association**: The class is tightly coupled to the `CMSG_GUILD_INVITE` opcode. Any change in the client protocol regarding this opcode would require updating this class's initialization.
*   **Namespace**: It resides in `WorldPackets::Guild`, indicating it is part of a modularized packet system where guild-related communications are grouped together.

## Member Reference

**GuildInvite**
Constructor for the `GuildInvite` packet. Initializes the base `ClientPacket` with the `CMSG_GUILD_INVITE` opcode. Does not initialize the `invitedName` member; this is done later during packet deserialization via `ReadFromWorldPacket`.

---

<!-- machine-true, projected from graph.json -->

## Map — GuildInvite

*Source:* Guild.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GuildInvite | ctor | — | — | — |
