# MailReturnToSender

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# MailReturnToSender

**Purpose & Responsibilities**

`MailReturnToSender` is a client-side packet structure within the `WorldPackets::Mail` namespace, responsible for representing the `CMSG_MAIL_RETURN_TO_SENDER` message sent from the game client to the server. Its sole responsibility is to deserialize the binary data of this specific network packet into structured fields (`mailboxGuid` and `mailId`) so that higher-level server logic can identify which piece of mail is being returned to its original sender and via which mailbox interface. It is a data carrier with no business logic, database interaction, or side effects of its own.

**Member-by-Member Behavior**

The unit contains a single member: the constructor.

*   **`MailReturnToSender`**: This default constructor initializes the packet object. It sets the internal packet opcode to `CMSG_MAIL_RETURN_TO_SENDER` by calling the base class `ClientPacket` constructor. It leaves the public data members `mailboxGuid` and `mailId` in their default-initialized states (an empty `ObjectGuid` and `0`, respectively). The actual population of these fields occurs later via the `ReadFromWorldPacket` method, which is declared in the header but implemented elsewhere (likely in a corresponding `.cpp` file not included in this unit's scope, or handled by a generic template mechanism not visible here).

**Cross-Unit Boundaries**

*   **Calls Out**: None. The constructor only calls the base class `ClientPacket` constructor.
*   **Called By**: None listed in the map. In practice, this packet is instantiated by the network layer when the server receives the raw bytes for `CMSG_MAIL_RETURN_TO_SENDER`. The server's mail handling system (not part of this unit) will then invoke `ReadFromWorldPacket` to populate the data and subsequently process the return request.

**Data Model**

This unit does not interact directly with any database tables. It operates purely on in-memory network packet data. Any persistence related to returning mail (e.g., updating mail status in the database) is handled by other units that consume this packet after deserialization.

**Notable Implementation Details**

*   **Final Class**: The class is marked `final`, preventing inheritance. This is appropriate for a leaf-node packet structure.
*   **Default Initialization**: The `mailId` field is explicitly initialized to `0` in the class definition. The `mailboxGuid` relies on the default constructor of `ObjectGuid`.
*   **Separation of Concerns**: This header defines the *structure* of the packet. The logic for reading the binary stream (`ReadFromWorldPacket`) is declared but not defined here, adhering to the separation between interface declaration and implementation.

## Member Reference

**MailReturnToSender**
Constructor for the `MailReturnToSender` packet. Initializes the base `ClientPacket` with the opcode `CMSG_MAIL_RETURN_TO_SENDER`. Does not populate data fields; those are filled by `ReadFromWorldPacket` (implemented externally).

---

<!-- machine-true, projected from graph.json -->

## Map — MailReturnToSender

*Source:* Mail.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| MailReturnToSender | ctor | — | — | — |
