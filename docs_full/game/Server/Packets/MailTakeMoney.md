# MailTakeMoney

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# MailTakeMoney

**Purpose & Responsibilities**

`MailTakeMoney` is a client-to-server packet structure within the `WorldPackets::Mail` namespace. Its sole responsibility is to represent the `CMSG_MAIL_TAKE_MONEY` message sent by a client when a player attempts to withdraw money attached to a specific piece of mail. It acts as a data carrier, holding the necessary identifiers (`mailboxGuid` and `mailId`) required by the server-side handler to locate the mail item and process the transaction. As a `ClientPacket`, it inherits the standard mechanisms for reading binary data from the network stream via `ReadFromWorldPacket`.

**Member-by-Member Behavior**

The unit contains a single member: the constructor.

*   **MailTakeMoney**: This is the default constructor for the packet. It initializes the base class `ClientPacket` with the opcode `CMSG_MAIL_TAKE_MONEY`, ensuring the packet is correctly identified by the server's packet dispatcher. It also initializes the `mailId` member variable to `0`. The `mailboxGuid` member is default-initialized by its type `ObjectGuid`. No complex logic or validation occurs here; it simply prepares the object to receive data from the incoming network packet.

**Cross-Unit Boundaries**

*   **Calls Out**: None. The constructor does not invoke any other units.
*   **Called By**: None listed in the map. In practice, instances of `MailTakeMoney` are typically created by the packet parsing infrastructure (likely within `Packet.cpp` or similar networking code) when a `CMSG_MAIL_TAKE_MONEY` opcode is detected, but these interactions are outside the scope of this unit's direct dependencies as defined in the map.

**Data Model**

This unit does not interact directly with any database tables. It operates purely on in-memory data structures representing network messages.

**Notable Implementation Details**

*   **Inheritance**: Inherits from `ClientPacket`, implying it is part of the WorldPackets framework used for handling client communications.
*   **Opcode Association**: Tightly coupled to the specific opcode `CMSG_MAIL_TAKE_MONEY`.
*   **Default Initialization**: The `mailId` is explicitly initialized to `0` in the constructor initializer list. `mailboxGuid` relies on the default constructor of `ObjectGuid`.
*   **Final Class**: The class is marked `final`, preventing further inheritance.

## Member Reference

**MailTakeMoney**
Constructor for the `MailTakeMoney` packet. Initializes the base `ClientPacket` with the opcode `CMSG_MAIL_TAKE_MONEY` and sets the `mailId` member to `0`. Prepares the object to deserialize incoming network data into `mailboxGuid` and `mailId`.

---

<!-- machine-true, projected from graph.json -->

## Map — MailTakeMoney

*Source:* Mail.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| MailTakeMoney | ctor | — | — | — |
