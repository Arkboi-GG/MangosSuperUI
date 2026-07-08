# MailCreateTextItem

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# MailCreateTextItem

**Purpose & Responsibilities**

`MailCreateTextItem` is a client-side packet structure within the `WorldPackets::Mail` namespace, responsible for representing the `CMSG_MAIL_CREATE_TEXT_ITEM` message sent from the game client to the server. Its primary role is to carry the necessary identifiers required for the server to generate a physical "text item" (typically a scroll or letter containing the mail's body text) associated with a specific piece of in-game mail.

This unit is part of the broader mail handling subsystem, which manages the transmission, storage, and retrieval of player-to-player and NPC-to-player messages. Specifically, this packet handles the action where a player requests a tangible item representation of a received mail message.

**Member-by-Member Behavior**

The unit contains a single member: the constructor.

*   **`MailCreateTextItem`**: This default constructor initializes the packet instance. It sets the internal packet opcode to `CMSG_MAIL_CREATE_TEXT_ITEM`, signaling to the server's network handler that this data stream represents a request to create a text item. It also initializes the member variables:
    *   `mailboxGuid`: The GUID of the mailbox involved in the transaction (though typically unused for this specific action, it is part of the standard mail packet structure).
    *   `mailId`: The unique identifier of the mail message for which the text item is being requested. Initialized to `0`.
    *   `mailTemplateId`: (Conditional) Included only if the supported client build is greater than `CLIENT_BUILD_1_9_4`. This field likely specifies the visual template or item ID for the generated text item. Initialized to `0`.

**Cross-Unit Boundaries**

*   **Calls Out**: None. This unit is a pure data structure and does not invoke logic in other units during construction.
*   **Called By**: While the MAP indicates no direct callers from other units, this packet is instantiated by the network layer when a `CMSG_MAIL_CREATE_TEXT_ITEM` opcode is detected in the incoming data stream from a client. The server's mail handling logic (likely in a separate unit such as `Player.cpp` or a dedicated `MailHandler.cpp`) will then process this object after deserialization via `ReadFromWorldPacket` (defined in the shared header but implemented elsewhere).

**Data Model**

This unit does not directly interact with database tables. It operates entirely in memory as a transient data carrier for network communication. The `mailId` it carries corresponds to records in the server's mail storage tables (e.g., `mail` or `mail_items`), but this unit itself performs no SQL operations.

**Notable Implementation Details**

*   **Client Build Dependency**: The inclusion of the `mailTemplateId` field is guarded by the preprocessor directive `#if SUPPORTED_CLIENT_BUILD > CLIENT_BUILD_1_9_4`. This indicates that older client versions did not send or expect this field, requiring the server to handle packet parsing differently depending on the connected client's version. Maintainers must ensure that any changes to this structure account for backward compatibility with clients version 1.9.4 and earlier.
*   **Inheritance**: It inherits from `ClientPacket`, which provides the base functionality for reading data from the world socket (`ReadFromWorldPacket`). The actual implementation of `ReadFromWorldPacket` is not in this header but is declared here, implying it is defined in the corresponding `.cpp` file or another partial of the class.
*   **Final Class**: The class is marked `final`, preventing further inheritance, which enforces a strict contract for this specific packet type.

## Member Reference

**MailCreateTextItem**
Constructor for the `MailCreateTextItem` packet. Initializes the packet opcode to `CMSG_MAIL_CREATE_TEXT_ITEM` and resets `mailboxGuid`, `mailId`, and conditionally `mailTemplateId` to their default values.

---

<!-- machine-true, projected from graph.json -->

## Map — MailCreateTextItem

*Source:* Mail.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| MailCreateTextItem | ctor | — | — | — |
