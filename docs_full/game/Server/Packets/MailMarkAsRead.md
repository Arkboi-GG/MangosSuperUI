# MailMarkAsRead

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# MailMarkAsRead

## Purpose & Responsibilities

`MailMarkAsRead` is a client-side packet structure within the `WorldPackets::Mail` namespace, defined in `Mail.h`. Its sole responsibility is to represent the `CMSG_MAIL_MARK_AS_READ` message sent by a game client to the server. This packet signals that a player has opened and viewed a specific email, triggering the server to update the mail's status to "read."

As a `ClientPacket`, it serves as a data container for deserialization. It holds two fields required to identify the action: the GUID of the mailbox involved and the unique ID of the mail item being marked as read. The class itself contains no business logic; it strictly defines the binary layout and initialization state for this network message.

## Member-by-Member Behavior

The unit consists of a single constructor and two data members, all serving the purpose of preparing the object for packet parsing.

### Construction and Initialization
The **`MailMarkAsRead`** constructor initializes the base `ClientPacket` with the opcode `CMSG_MAIL_MARK_AS_READ`. It also explicitly initializes the `mailId` member to `0`. The `mailboxGuid` is default-initialized by the `ObjectGuid` class (typically to an empty/invalid state). This ensures that if the packet parsing fails or is incomplete, the fields hold known safe defaults rather than garbage values.

### Data Members
*   **`mailboxGuid`**: An `ObjectGuid` representing the mailbox entity associated with the action. In many WoW implementations, this may be redundant if the player's active mailbox is tracked server-side, but the protocol requires it.
*   **`mailId`**: A `uint32` identifying the specific mail entry to be marked as read. Initialized to `0` in the constructor.

## Cross-Unit Boundaries

*   **Calls Out**: None. The constructor only invokes the base `ClientPacket` constructor.
*   **Called By**: None listed in the map. However, in the broader system, this class is instantiated by the network layer when a `CMSG_MAIL_MARK_AS_READ` packet is received from the client. The `ReadFromWorldPacket` method (declared in the header but implemented elsewhere, likely in a corresponding `.cpp` file not included in this unit's scope) will be called by the packet handler to populate `mailboxGuid` and `mailId` from the raw byte stream.

## Data Model

This unit does not interact directly with any database tables. It operates purely on network packet data. The `mailId` and `mailboxGuid` correspond to records in the server's mail storage (likely `mail` table in the database), but this class performs no SQL operations.

## Notable Implementation Details

*   **Default Initialization**: The explicit initialization of `mailId = 0` in the constructor is a defensive programming practice. While `ReadFromWorldPacket` will overwrite this value with data from the client, ensuring a zeroed state prevents potential issues if the parsing logic is skipped or fails early.
*   **Opcode Association**: The class is tightly coupled to the `CMSG_MAIL_MARK_AS_READ` opcode via the base class constructor. This allows the server's packet dispatcher to correctly route incoming bytes to the appropriate handler based on the opcode registered during construction.
*   **Namespace Structure**: Located in `WorldPackets::Mail`, indicating it is part of a modular packet handling system where mail-related messages are grouped together for clarity and maintainability.

## Member Reference

**MailMarkAsRead**
Constructor for the `MailMarkAsRead` packet. Initializes the base `ClientPacket` with the `CMSG_MAIL_MARK_AS_READ` opcode and sets `mailId` to `0`. Prepares the object for subsequent deserialization of the mailbox GUID and mail ID from the network stream.

---

<!-- machine-true, projected from graph.json -->

## Map — MailMarkAsRead

*Source:* Mail.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| MailMarkAsRead | ctor | — | — | — |
