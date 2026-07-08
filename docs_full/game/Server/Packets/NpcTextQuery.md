# NpcTextQuery

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# NpcTextQuery

**Purpose & Responsibilities**

`NpcTextQuery` is a client-to-server network packet structure within the `WorldPackets::Npc` namespace. It represents the `CMSG_NPC_TEXT_QUERY` message sent by the game client to request specific non-player character (NPC) dialogue data. Its sole responsibility is to define the binary layout and deserialization logic for this specific query, extracting the target NPC's unique identifier (`guid`) and the specific text entry ID (`textID`) requested by the client. It does not handle the logic of retrieving the text from the database or sending the response; it merely transports the request parameters from the network layer to the server's command handler.

**Member-by-Member Behavior**

The unit contains a single member: the constructor.

*   **`NpcTextQuery`**: This default constructor initializes the packet object. It sets the internal packet opcode to `CMSG_NPC_TEXT_QUERY`, identifying the message type to the network dispatcher. It also initializes the public data members: `textID` is set to `0` and `guid` is default-constructed (an empty/null GUID). This ensures the object is in a valid, zeroed state before any network data is read into it.

**Cross-Unit Boundaries**

*   **Calls Out**: The constructor calls the base class constructor `ClientPacket(CMSG_NPC_TEXT_QUERY)` defined in `Packet.h`. This registers the packet type with the core networking infrastructure.
*   **Called By**: While the MAP indicates no external callers for the constructor, in practice, this class is instantiated by the network input handler (likely in `WorldSession` or similar networking code, though not shown in the provided MAP/SOURCE) when a raw `CMSG_NPC_TEXT_QUERY` packet arrives on the wire. The `ReadFromWorldPacket` method (declared in the header but not detailed in the MAP as a separate behavioral unit here, likely handled internally or considered part of the packet reading flow) is called by the network layer to populate `textID` and `guid` from the incoming byte stream.

**Data Model**

This unit does not interact directly with any database tables. It operates purely on network packet data. The `textID` field corresponds to a primary key in the `npc_text` table (or similar locale-specific text tables) within the game's database, but `NpcTextQuery` itself performs no SQL queries.

**Notable Implementation Details**

*   **Final Class**: The class is marked `final`, preventing inheritance. This is consistent with its role as a leaf data structure for a specific protocol message.
*   **Default Initialization**: The use of in-class initializers (`uint32 textID = 0;`) ensures that if `ReadFromWorldPacket` fails or is not called, the fields remain in a safe, known state rather than containing garbage memory.
*   **Namespace Organization**: It resides in `WorldPackets::Npc`, grouping all NPC-related client packets together for modular organization.

## Member Reference

**NpcTextQuery**
Constructor for the `NpcTextQuery` packet. Initializes the packet opcode to `CMSG_NPC_TEXT_QUERY` via the `ClientPacket` base class. Sets `textID` to `0` and `guid` to a default-constructed `ObjectGuid`. No database interaction occurs.

---

<!-- machine-true, projected from graph.json -->

## Map — NpcTextQuery

*Source:* Npc.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| NpcTextQuery | ctor | — | — | — |
