# ReclaimCorpse

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ReclaimCorpse

## Purpose & Responsibilities

`ReclaimCorpse` is a client-side packet structure within the `WorldPackets::Misc` namespace, defined in `Misc.h`. It represents the `CMSG_RECLAIM_CORPSE` message sent by the game client to the server when a player attempts to reclaim their corpse after death.

The primary responsibility of this unit is to define the data layout for this specific network message. It holds a single piece of information: the `ObjectGuid` of the corpse object being reclaimed. Like all classes in this namespace, it inherits from `ClientPacket`, indicating it is part of the incoming message parsing infrastructure. It does not contain logic for processing the request, validating permissions, or interacting with the game world; it solely serves as a container for the raw data extracted from the network stream.

## Member-by-Member Behavior

### **ReclaimCorpse** (Constructor)
The constructor initializes the packet instance. It performs two key actions:
1.  It invokes the base class constructor `ClientPacket(CMSG_RECLAIM_CORPSE)`, registering this packet instance with the opcode `CMSG_RECLAIM_CORPSE`. This allows the network dispatcher to identify incoming messages of this type.
2.  It leaves the `guid` member uninitialized (default-initialized for `ObjectGuid`). The actual value for `guid` is populated later by the `ReadFromWorldPacket` method (which is declared in the shared header but implemented in the corresponding `.cpp` file, not shown here but implied by the class structure).

## Cross-Unit Boundaries

*   **Calls Out:** None. The constructor does not invoke any other units.
*   **Called By:** None listed in the map. In practice, this constructor is called by the packet deserialization framework (likely within `WorldSession` or a packet handler dispatcher) when a `CMSG_RECLAIM_CORPSE` opcode is detected on the wire. The framework instantiates this object to hold the parsed data before passing it to the business logic handler.

## Data Model

This unit does not interact directly with any database tables. It operates purely on network packet data. The `ObjectGuid` stored in the `guid` member corresponds to an in-memory game object (the corpse), but no SQL queries or table references are present in this definition.

## Notable Implementation Details

*   **Inheritance:** Inherits from `ClientPacket`, establishing it as an inbound message from the client.
*   **Opcode Association:** Hardcoded to `CMSG_RECLAIM_CORPSE`. This ties the struct strictly to this specific client-server protocol message.
*   **Minimal State:** Contains only one data member (`guid`), reflecting the simplicity of the reclaim corpse command: the client identifies *which* corpse it wants to reclaim by its unique identifier.
*   **No Logic:** As is typical for packet structs in this architecture, there is no validation or business logic here. Validation (e.g., ensuring the corpse belongs to the player, checking cooldowns) occurs in the handler that consumes this packet, not in the packet class itself.

## Member Reference

**ReclaimCorpse**
Constructor for the `ReclaimCorpse` packet. Initializes the base `ClientPacket` with the opcode `CMSG_RECLAIM_CORPSE`. The `guid` member is default-initialized and will be populated by the `ReadFromWorldPacket` method during deserialization.

---

<!-- machine-true, projected from graph.json -->

## Map — ReclaimCorpse

*Source:* Misc.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ReclaimCorpse | ctor | — | — | — |
