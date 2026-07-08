# GmTicketUpdateText

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# GmTicketUpdateText

**Purpose & Responsibilities**

`GmTicketUpdateText` is a client-side packet structure within the `WorldPackets::GmTicket` namespace, responsible for deserializing the `CMSG_GMTICKET_UPDATETEXT` message sent by the game client. Its primary role is to extract two specific data fields from the raw network byte stream: a `type` identifier (`uint8`) and the `ticketText` string content. This packet represents a request from a player to update the text associated with an existing Game Master (GM) ticket. As a `ClientPacket`, it serves as the input layer for the server's GM ticket system, providing structured access to the raw data before further processing by higher-level handlers.

**Member-by-Member Behavior**

The unit consists of a single constructor and relies on the inherited `ReadFromWorldPacket` method (defined elsewhere in the class hierarchy or implemented in a corresponding `.cpp` file not shown in the provided source snippet, but declared in the header) to perform the actual deserialization.

*   **Constructor (`GmTicketUpdateText`)**: Initializes the packet object. It sets the internal packet ID to `CMSG_GMTICKET_UPDATETEXT` via the base class `ClientPacket` constructor. It also initializes the member variable `type` to `0` and leaves `ticketText` as an empty default-constructed `std::string`. This ensures the object is in a valid, albeit empty, state before data is read from the network buffer.

**Cross-Unit Boundaries**

*   **Calls Out**: The provided MAP indicates no outgoing calls to other units from the constructor. The actual data extraction occurs in `ReadFromWorldPacket`, which interacts with the `WorldPacket` class (from the core networking layer) to pull bytes from the incoming stream. However, since `ReadFromWorldPacket` is not listed as a member in the MAP for this specific partial/unit view, we focus on the constructor's isolation.
*   **Called By**: The MAP indicates no external callers for the constructor. In practice, instances of `GmTicketUpdateText` are typically created by the network handler framework when a `CMSG_GMTICKET_UPDATETEXT` opcode is detected on the socket. The framework instantiates this class and invokes `ReadFromWorldPacket` to populate its fields.

**Data Model**

This unit does not interact directly with any database tables. It operates purely on in-memory network packet data. The `ticketText` field will eventually be persisted to a database table (likely `gm_ticket` or similar) by higher-level service classes, but `GmTicketUpdateText` itself is agnostic to storage mechanisms.

**Notable Implementation Details**

*   **Default Initialization**: The `type` field is explicitly initialized to `0` in the class definition. This is a safety measure ensuring that if the packet reading fails or is incomplete, the `type` does not contain garbage memory.
*   **Inheritance**: It inherits from `ClientPacket`, which implies it carries metadata such as the sender's session information and the packet opcode. The base class handles the low-level socket interaction, while this derived class defines the semantic structure of the payload.
*   **String Handling**: The `ticketText` is stored as a `std::string`. The deserialization logic (in `ReadFromWorldPacket`) must correctly handle string length prefixes and potential null terminators depending on the client version and protocol specifics, though the implementation details of that parsing are outside this specific header's scope.

## Member Reference

**GmTicketUpdateText**
Constructor for the `GmTicketUpdateText` packet. It initializes the base `ClientPacket` with the opcode `CMSG_GMTICKET_UPDATETEXT` and sets the `type` member to `0`. It prepares the object to receive serialized data from the network.

---

<!-- machine-true, projected from graph.json -->

## Map — GmTicketUpdateText

*Source:* GmTicket.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GmTicketUpdateText | ctor | — | — | — |
