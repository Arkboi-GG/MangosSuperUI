# PetitionBuy

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# PetitionBuy

**Purpose & Responsibilities**

`PetitionBuy` is a client-side packet structure within the `WorldPackets::Petition` namespace, responsible for representing the `CMSG_PETITION_BUY` message sent from the game client to the server. This packet initiates the process of purchasing a faction petition (typically used to found a guild or officer rank in World of Warcraft-style MMORPGs). It carries the necessary context for the server to validate the transaction: the GUID of the NPC vendor selling the petition and the name associated with the purchase (likely the desired guild name or petitioner identifier).

As a `ClientPacket`, its primary responsibility is deserialization: converting raw binary data received over the network into structured C++ fields (`guidNPC` and `name`) via the `ReadFromWorldPacket` method. It does not contain business logic for validation or processing; those concerns reside in the server-side handlers that consume this packet.

## Member-by-Member Behavior

### **PetitionBuy** (Constructor)
The default constructor initializes the packet object. It explicitly calls the base class constructor `ClientPacket(CMSG_PETITION_BUY)`, registering this packet type with the specific opcode `CMSG_PETITION_BUY`. This ensures that when the server receives a packet with this opcode, it instantiates the correct `PetitionBuy` struct for parsing. The member variables `guidNPC` and `name` are default-initialized (empty/null) until populated by `ReadFromWorldPacket`.

### **ReadFromWorldPacket** (Implicitly Declared, Defined Elsewhere)
Although the definition is not present in the provided header, the declaration `void ReadFromWorldPacket(WorldPacket& recv_data) override;` indicates that this method parses the incoming `WorldPacket`. Based on the member variables declared in the class:
1. It extracts an `ObjectGuid` from the packet stream and assigns it to `guidNPC`.
2. It extracts a string from the packet stream and assigns it to `name`.

This method is critical for transforming the opaque byte stream into usable data for the server's petition handling logic.

## Cross-Unit Boundaries

*   **Calls Out:** None. The `PetitionBuy` class itself does not call into other units during construction or parsing. Its dependency is solely on the `WorldPacket` interface for reading data.
*   **Called By:** Server-side petition handlers (not shown in the map, but implied by the `ClientPacket` inheritance). These handlers will instantiate `PetitionBuy`, call `ReadFromWorldPacket`, and then access `guidNPC` and `name` to execute the purchase logic.

## Data Model

This unit does not directly interact with database tables. It operates entirely in memory, handling network packet deserialization. Any database interactions related to petitions (e.g., checking funds, creating the petition record) occur in downstream server logic after this packet has been parsed.

## Notable Implementation Details

*   **Opcode Association:** The packet is strictly tied to `CMSG_PETITION_BUY`. Any change in the client-server protocol regarding how petitions are purchased would require updating this opcode constant.
*   **Data Fields:** The presence of both `guidNPC` and `name` suggests that the server needs to verify that the NPC identified by `guidNPC` is authorized to sell petitions and that the `name` provided meets naming conventions (e.g., unique, appropriate length, no profanity).
*   **Final Class:** The class is marked `final`, preventing inheritance. This is appropriate for a simple data-transfer object (DTO) like a packet structure, ensuring no unexpected behavior through subclassing.

## Member Reference

**PetitionBuy**
Constructor for the `PetitionBuy` packet. Initializes the base `ClientPacket` with the opcode `CMSG_PETITION_BUY`. Default-initializes member variables `guidNPC` and `name`.

---

<!-- machine-true, projected from graph.json -->

## Map — PetitionBuy

*Source:* Petition.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| PetitionBuy | ctor | — | — | — |
