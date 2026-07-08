# SpiritHealerActivate

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# SpiritHealerActivate

**SpiritHealerActivate** is a client-to-server packet class within the `WorldPackets::Npc` namespace, defined in `Npc.h`. It represents the network message sent by a client when a player interacts with a Spirit Healer NPC (typically found in graveyard areas) to resurrect their character. As a subclass of `ClientPacket`, it encapsulates the raw data received from the client for the opcode `CMSG_SPIRIT_HEALER_ACTIVATE`.

This unit is strictly a data structure and serialization helper. It contains no business logic, validation, or server-side processing. Its sole responsibility is to define the memory layout of the incoming packet and provide the interface (`ReadFromWorldPacket`) required to deserialize the binary stream into accessible fields.

## Member-by-Member Behavior

The unit consists of a single constructor and one public data member, alongside an inherited virtual method declaration.

### **SpiritHealerActivate** (Constructor)
The explicit constructor initializes the base `ClientPacket` class with the specific opcode `CMSG_SPIRIT_HEALER_ACTIVATE`. This registration ensures that the packet routing system can correctly identify incoming messages of this type. The constructor takes no arguments, relying on default initialization for the member variables.

### **guid**
A public member variable of type `ObjectGuid`. This field stores the unique identifier of the Spirit Healer NPC that the player interacted with. In the context of the game world, this GUID allows the server to locate the specific NPC object associated with the resurrection request.

### **ReadFromWorldPacket**
Declared as an override of the pure virtual function from `ClientPacket`, this method is responsible for deserializing the binary data from the `WorldPacket` instance into the `guid` member. While the declaration is present in this header, the implementation resides in the corresponding `.cpp` file (not provided in the source snippet, but implied by the `override` keyword). It extracts the NPC's GUID from the network stream.

## Cross-Unit Boundaries

*   **Calls out:** None. This unit does not invoke methods in other classes or modules. It is a passive data container.
*   **Called by:** The packet handling infrastructure (likely within the network layer or session management code, such as `WorldSession` or a packet dispatcher) will instantiate this class and call `ReadFromWorldPacket` upon receiving the `CMSG_SPIRIT_HEALER_ACTIVATE` opcode from the client. Subsequently, the server logic handling resurrection (e.g., in `Player.cpp` or a dedicated NPC handler) will access the `guid` member to determine which NPC triggered the event.

## Data Model

This unit does not interact directly with any database tables. It operates entirely in memory, handling transient network data. No SQL queries or table references are present in this code.

## Notable Implementation Details

*   **Inheritance Hierarchy:** The class inherits from `ClientPacket`, which implies it shares common functionality for packet identification and basic serialization with other client-bound packets.
*   **Opcode Specificity:** The use of `CMSG_SPIRIT_HEALER_ACTIVATE` ties this class exclusively to the resurrection mechanic via Spirit Healers. It is distinct from other NPC interaction packets like `GossipHello` or `BankerActivate`, which handle different gameplay systems.
*   **Minimal State:** The class contains only the minimal necessary state (`guid`) to identify the target NPC. Any additional context required for the resurrection process (such as player position, faction checks, or corpse location) is handled by the server-side logic that consumes this packet, not by the packet itself.

## Member Reference

**SpiritHealerActivate**
Explicit constructor that initializes the base `ClientPacket` with the opcode `CMSG_SPIRIT_HEALER_ACTIVATE`. It prepares the object to receive and parse data for spirit healer interactions.

---

<!-- machine-true, projected from graph.json -->

## Map — SpiritHealerActivate

*Source:* Npc.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| SpiritHealerActivate | ctor | — | — | — |
