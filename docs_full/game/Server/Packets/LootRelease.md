# LootRelease

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# LootRelease

**Purpose & Responsibilities**

`LootRelease` is a client-to-server packet structure within the `WorldPackets::Loot` namespace, responsible for handling the `CMSG_LOOT_RELEASE` message. Its primary responsibility is to deserialize the binary data sent by a client when a player attempts to release their claim on a loot object (such as a corpse or container). The class extracts the `ObjectGuid` of the target entity from the incoming network stream, although the server-side logic explicitly ignores this value in favor of an internally tracked GUID.

This unit is part of the packet parsing layer; it does not contain business logic for validating the release action, updating game state, or interacting with databases. It strictly defines the contract for receiving this specific command from the client.

**Member-by-Member Behavior**

The unit contains a single member relevant to this documentation:

*   **`LootRelease` (Constructor)**: Initializes the packet object. It sets the packet opcode to `CMSG_LOOT_RELEASE` via the base `ClientPacket` constructor and initializes the public member `guid` to its default state (an empty `ObjectGuid`). This prepares the object to receive data from the network.

**Cross-Unit Boundaries**

*   **Calls Out**: None. The constructor performs only local initialization.
*   **Called By**: None listed in the map. In practice, instances of `LootRelease` are typically created by the network handler (e.g., in `WorldSession` or a packet router) when a `CMSG_LOOT_RELEASE` packet is detected on the wire. The handler then calls `ReadFromWorldPacket` (declared in the header but not detailed in the map as a separate member entry, implying it is handled internally or considered part of the packet lifecycle managed by the base class infrastructure) to populate the `guid` field.

**Data Model**

This unit does not interact with any database tables. It operates entirely on in-memory network packet data.

**Notable Implementation Details**

*   **Ignored Client GUID**: The public member `guid` is explicitly commented as "not used by server (uses internally stored guid instead)". This indicates a design choice where the server maintains authoritative state regarding which loot object a player is interacting with, likely to prevent spoofing or race conditions where a client might send a release command for a different object than intended. The server likely looks up the active loot context for the player session rather than trusting the GUID provided in this packet.
*   **Final Class**: The class is marked `final`, indicating it cannot be subclassed. This enforces a strict, immutable definition for this specific packet type.
*   **Namespace**: It resides in `WorldPackets::Loot`, grouping it logically with other loot-related network messages (`AutoStoreLootItem`, `LootUnit`, `LootRoll`, `LootMasterGive`).

## Member Reference

**LootRelease**
Constructor for the `LootRelease` packet. Initializes the packet opcode to `CMSG_LOOT_RELEASE` and defaults the `guid` member. It prepares the object to parse incoming client data for the loot release command.

---

<!-- machine-true, projected from graph.json -->

## Map — LootRelease

*Source:* Loot.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| LootRelease | ctor | — | — | — |
