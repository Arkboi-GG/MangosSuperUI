# RepairItem

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# RepairItem Packet Structure

## Purpose & Responsibilities

The `RepairItem` class is a lightweight data structure within the `WorldPackets::Npc` namespace, designed to represent a specific client-to-server network message: `CMSG_REPAIR_ITEM`. Its sole responsibility is to encapsulate the raw data sent by a client when a player attempts to repair an item via an NPC vendor.

As a subclass of `ClientPacket`, it serves as a container for two critical identifiers:
1.  **`npcGuid`**: The unique identifier of the Non-Player Character (NPC) providing the repair service.
2.  **`itemGuid`**: The unique identifier of the specific item instance the player wishes to repair.

This unit does not contain logic for parsing, validation, or game-state modification. It strictly defines the memory layout and initialization required to hold the payload of the repair request before it is processed by higher-level game logic handlers.

## Member-by-Member Behavior

### Constructor: `RepairItem()`
The constructor initializes the packet object. It performs two key actions:
1.  **Base Initialization**: It calls the base class constructor `ClientPacket(CMSG_REPAIR_ITEM)`, registering this packet instance with the opcode `CMSG_REPAIR_ITEM`. This opcode identifies the message type within the network protocol.
2.  **Member Initialization**: It leaves the public members `npcGuid` and `itemGuid` in their default-initialized state (empty/null GUIDs). These fields are expected to be populated later by the deserialization process (`ReadFromWorldPacket`), which is declared in this header but implemented elsewhere (likely in a corresponding `.cpp` file not included in this unit's scope, or potentially inline in a different partial).

**Note on Scope**: While `ReadFromWorldPacket` is declared in this header, the provided source code for this unit *only* contains the declaration. The actual implementation of how bytes are extracted from the network buffer into `npcGuid` and `itemGuid` is not present in this snippet. Therefore, the behavior described here is limited to the construction and structural definition.

## Cross-Unit Boundaries

*   **Calls Out**: None. The constructor does not invoke any external functions or classes beyond the base class initializer.
*   **Called By**: The MAP indicates no external callers are explicitly tracked for the constructor itself in this view. However, in the broader system, instances of `RepairItem` are typically constructed by the network layer when a packet with opcode `CMSG_REPAIR_ITEM` is received from a client. The network handler will then call `ReadFromWorldPacket` to populate the data.

## Data Model

This unit interacts with **no database tables**. It is a transient network packet structure. The `npcGuid` and `itemGuid` fields refer to entities that exist in the database (NPCs and Items), but this class does not perform any SQL queries, inserts, updates, or deletions. It merely holds the identifiers passed by the client.

## Notable Implementation Details

1.  **Final Class**: The class is marked `final`, preventing inheritance. This is appropriate for a leaf-node packet structure where no further specialization is expected.
2.  **Public Members**: Both `npcGuid` and `itemGuid` are public. This design choice allows direct access to the parsed data by the game logic handlers that consume this packet, avoiding the overhead of getter/setter methods for simple data transfer objects.
3.  **Default Initialization**: The GUIDs are not initialized to specific values in the constructor. They rely on the default constructor of `ObjectGuid` (which typically results in an empty/null GUID). This is safe because the packet is considered invalid until `ReadFromWorldPacket` successfully populates these fields.
4.  **Namespace**: Located in `WorldPackets::Npc`, indicating it is part of the world server's packet handling subsystem, specifically dealing with NPC interactions.

## Member Reference

**RepairItem**
Constructor for the `RepairItem` packet. Initializes the base `ClientPacket` with the opcode `CMSG_REPAIR_ITEM`. Does not initialize the `npcGuid` or `itemGuid` members; these are populated during deserialization.

---

<!-- machine-true, projected from graph.json -->

## Map — RepairItem

*Source:* Npc.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| RepairItem | ctor | — | — | — |
