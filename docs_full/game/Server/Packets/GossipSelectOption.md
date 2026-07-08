# GossipSelectOption

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# GossipSelectOption

**Purpose & Responsibilities**

`GossipSelectOption` is a client-to-server network packet structure within the `WorldPackets::Npc` namespace. Its sole responsibility is to represent and deserialize the `CMSG_GOSSIP_SELECT_OPTION` message sent by the game client when a player selects an option from an NPC gossip menu. It captures the target NPC's identifier, the specific gossip list ID associated with the selected option, and an optional text code (often used for custom input or validation strings) entered or selected by the player.

This unit is part of the broader `Npc.h` header, which defines various client-side NPC interaction packets (such as `GossipHello`, `TrainerList`, and `BankerActivate`). However, `GossipSelectOption` specifically handles the confirmation step of a gossip interaction, distinct from the initial hello request.

## Member-by-Member Behavior

### **GossipSelectOption** (Constructor)
The constructor initializes the packet object. It explicitly calls the base class `ClientPacket` constructor, passing the constant `CMSG_GOSSIP_SELECT_OPTION`. This registers the packet type with the network layer, ensuring that incoming data streams matching this opcode are routed to instances of this class for deserialization. The member variables (`guid`, `gossipListId`, `code`) are default-initialized (to zero/empty) by their respective type defaults or explicit initializers in the class definition.

## Cross-Unit Boundaries

*   **Calls Out:** None. The constructor performs only local initialization and base class construction.
*   **Called By:** None listed in the map. In practice, this class is instantiated by the network receive handler (likely in a unit responsible for parsing `WorldPacket` streams, such as a session or connection manager) when the server receives raw bytes corresponding to `CMSG_GOSSIP_SELECT_OPTION`. The handler will then invoke the `ReadFromWorldPacket` method (declared in the base class but implemented elsewhere or implicitly via template mechanisms depending on the framework's design) to populate the `guid`, `gossipListId`, and `code` fields.

## Data Model

This unit does not interact directly with any database tables. It operates purely on network data structures. The `gossipListId` field corresponds to IDs found in the `gossip_menu_option` table in the database, but `GossipSelectOption` itself performs no SQL queries or direct table access.

## Notable Implementation Details

*   **Packet Structure:** The class inherits from `ClientPacket`, indicating it is strictly inbound from the client.
*   **Fields:**
    *   `guid`: An `ObjectGuid` representing the NPC that displayed the gossip menu.
    *   `gossipListId`: A `uint32` identifying the specific gossip menu list. This is crucial for the server to look up the correct response or action associated with the selected option.
    *   `code`: A `std::string` that allows the client to send arbitrary text. This is often used for custom gossip implementations where the player might enter a name or password, or for internal client-side state tracking.
*   **Deserialization:** While the `ReadFromWorldPacket` method is declared in the base class, the specific logic for unpacking these three fields from the binary stream is handled by the framework's generic packet reading mechanism or a specialized implementation not visible in this header. The presence of `std::string` implies variable-length decoding, which typically involves reading a length prefix followed by the string data.

## Member Reference

**GossipSelectOption**: Constructor that initializes the packet as a `ClientPacket` with opcode `CMSG_GOSSIP_SELECT_OPTION`. It prepares the object to receive and store the NPC GUID, gossip list ID, and optional code string from the incoming network stream.

---

<!-- machine-true, projected from graph.json -->

## Map — GossipSelectOption

*Source:* Npc.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GossipSelectOption | ctor | — | — | — |
