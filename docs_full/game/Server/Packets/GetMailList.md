# GetMailList

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# GetMailList

`GetMailList` is a client-to-server packet structure within the `WorldPackets::Mail` namespace, defined in `Mail.h`. It represents the `CMSG_GET_MAIL_LIST` message sent by the game client to request the current contents of a player's mailbox.

## Purpose & Responsibilities

The primary responsibility of `GetMailList` is to encapsulate the network data required for the server to identify which mailbox the client is querying. As a `ClientPacket`, it serves as the input container for the mail retrieval workflow. It does not contain logic for processing the request, fetching data, or generating responses; it solely holds the `mailboxGuid` necessary to route the request correctly.

## Member-by-Member Behavior

### **GetMailList**
This is the default constructor for the `GetMailList` packet.
- **Initialization**: It initializes the base class `ClientPacket` with the opcode `CMSG_GET_MAIL_LIST`. This opcode identifies the packet type to the network layer and the handler responsible for processing mail list requests.
- **State**: The constructor leaves the `mailboxGuid` member uninitialized (default constructed `ObjectGuid`). The actual value is populated later by the `ReadFromWorldPacket` method when the raw network data is parsed. Note that `ReadFromWorldPacket` is declared in this header but implemented elsewhere (likely in a corresponding `.cpp` file or another partial not included in this specific unit definition).

## Cross-Unit Boundaries

- **Called By**: The MAP indicates no external units explicitly call this constructor. In practice, instances of `GetMailList` are typically created by the network input handler when a `CMSG_GET_MAIL_LIST` packet arrives from the client. The handler constructs the object and then invokes `ReadFromWorldPacket` to populate its fields.
- **Calls Out**: The constructor itself makes no calls to other units beyond the base class initialization.

## Data Model

This unit does not interact directly with database tables. It operates purely on network packet data. The `mailboxGuid` it carries will eventually be used by downstream handlers (not part of this unit) to query mail-related tables, but `GetMailList` itself has no SQL queries or table dependencies.

## Notable Implementation Details

- **Minimal State**: The class contains only one member variable, `mailboxGuid`. This reflects the simplicity of the request: the server needs to know *which* mailbox to read. Other details like the player's GUID are likely derived from the session context associated with the packet, rather than being embedded in the packet itself.
- **Inheritance**: It inherits from `ClientPacket`, which provides the framework for reading binary data from the network stream via the `ReadFromWorldPacket` interface.
- **No Logic**: There is no validation or business logic in this class. It is a pure data carrier.

## Member Reference

**GetMailList**
Constructor for the `GetMailList` packet. Initializes the base `ClientPacket` with the `CMSG_GET_MAIL_LIST` opcode. Does not initialize `mailboxGuid`; that field is populated by `ReadFromWorldPacket` during deserialization.

---

<!-- machine-true, projected from graph.json -->

## Map — GetMailList

*Source:* Mail.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GetMailList | ctor | — | — | — |
