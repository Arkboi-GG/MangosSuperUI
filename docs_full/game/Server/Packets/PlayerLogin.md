# PlayerLogin

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# PlayerLogin

**Purpose & Responsibilities**

`PlayerLogin` is a minimal data structure representing a client-to-server network packet (`CMSG_PLAYER_LOGIN`). Its sole responsibility is to carry the `ObjectGuid` of the character attempting to log in. It inherits from `ClientPacket`, establishing it as an incoming message from a game client, but contains no additional payload fields beyond the identifier.

This unit is part of the `WorldPackets::Character` namespace, grouping it with other character management packets such as creation, deletion, and renaming. However, `PlayerLogin` itself is strictly a container for the login request identifier.

**Member-by-Member Behavior**

The unit consists of a single member: the constructor.

*   **Construction**: The `PlayerLogin` constructor initializes the base `ClientPacket` with the opcode `CMSG_PLAYER_LOGIN`. This registers the packet type within the network subsystem so that when the server receives a packet with this opcode, it can instantiate this specific class to handle the data. No other initialization occurs; the `guid` field is left uninitialized until `ReadFromWorldPacket` (defined in the base class or elsewhere, not shown in this partial) populates it.

**Cross-Unit Boundaries**

*   **Calls Out**: None. The constructor does not invoke any other units.
*   **Called By**: None listed in the map. In practice, this class is instantiated by the network dispatcher when a `CMSG_PLAYER_LOGIN` packet arrives, but those interactions are handled by the networking layer, not documented here as direct calls from other units in the map.

**Data Model**

This unit does not interact directly with any database tables. It is a transient network packet object. Any database operations related to logging in (e.g., loading character data from `characters` table) would occur in the handler that processes this packet, not within the `PlayerLogin` class itself.

**Notable Implementation Details**

*   **Minimalism**: The class contains no custom logic. It relies entirely on the base `ClientPacket` infrastructure for reading the `guid` from the raw network buffer.
*   **Namespace**: It resides in `WorldPackets::Character`, indicating its role in character lifecycle management, even though the login action itself is often considered a session-level event.

## Member Reference

**PlayerLogin**
Constructor for the `PlayerLogin` packet. Initializes the base `ClientPacket` with the opcode `CMSG_PLAYER_LOGIN`. Does not initialize the `guid` member; that is handled by the inherited `ReadFromWorldPacket` method during deserialization.

---

<!-- machine-true, projected from graph.json -->

## Map — PlayerLogin

*Source:* Character.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| PlayerLogin | ctor | — | — | — |
