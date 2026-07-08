<!-- provenance: verbose -->
# game_Server_Packets_Mail

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Mail Packet Definitions (`WorldPackets::Mail`)

**Purpose & Responsibilities**
This unit defines the C++ data structures and deserialization logic for client-to-server network packets related to the in-game mail system. It resides in the `WorldPackets::Mail` namespace and implements several classes derived from `ClientPacket`. Each class corresponds to a specific action a player can perform via the mail interface, such as sending a letter, retrieving items, marking messages as read, or deleting correspondence.

The primary responsibility of this unit is to parse raw binary data from `WorldPacket` objects into structured C++ fields (e.g., `mailboxGuid`, `mailId`, `subject`) so that higher-level game logic handlers can process these requests safely. It handles version-specific differences in packet formats for client builds newer than 1.9.4.

**Member-by-Member Behavior**
The unit contains nine distinct packet classes, each with a constructor and a `ReadFromWorldPacket` method. The behavior is strictly declarative regarding data extraction:

1.  **SendMail**: Handles the complex payload for sending new mail. It extracts the target mailbox GUID, receiver name, subject, body text, stationery ID, package ID, attached item GUID, monetary amount, and Cash on Delivery (COD) amount. For clients newer than build 1.9.4, it explicitly skips two unused bytes (a 64-bit zero and an 8-bit zero) to maintain stream alignment.
2.  **MailReturnToSender**: Extracts the mailbox GUID and the specific mail ID to return the message to the sender.
3.  **MailMarkAsRead**: Extracts the mailbox GUID and mail ID to update the read status of a message.
4.  **MailTakeItem**: Extracts the mailbox GUID and mail ID to allow the player to retrieve an item attachment.
5.  **MailTakeMoney**: Extracts the mailbox GUID and mail ID to allow the player to withdraw money from a message.
6.  **GetMailList**: Extracts only the mailbox GUID to request the list of current messages.
7.  **MailDelete**: Extracts the mailbox GUID and mail ID to permanently remove a message.
8.  **MailCreateTextItem**: Extracts the mailbox GUID and mail ID. For clients newer than 1.9.4, it also extracts a `mailTemplateId`, likely used for generating text-based item representations or specific UI behaviors associated with mail templates.

**Cross-Unit Boundaries**
All `ReadFromWorldPacket` methods in this unit call out to utility functions in other units to perform the actual byte-level parsing:
*   **`ByteBuffer/operator>>`**: Called by all `ReadFromWorldPacket` implementations to extract primitive types (`uint32`, `std::string`) and complex types like `ObjectGuid` from the underlying buffer. This operator is defined in the `ByteBuffer` unit.
*   **`ObjectGuid/operator>>`**: Specifically invoked when extracting `mailboxGuid`, `itemGuid`, or other GUID fields. This operator is defined in the `ObjectGuid` unit.

No other units call into this unit directly according to the map; these classes are instantiated and populated by the network layer before being passed to game logic handlers.

**Data Model**
This unit does not interact directly with database tables. It operates entirely on in-memory network packet buffers. The `Tables` column in the map is empty for all members. Any persistence of mail data occurs in downstream units that consume these parsed packet objects.

**Notable Implementation Details**
*   **Client Versioning**: The code uses preprocessor directives (`#if SUPPORTED_CLIENT_BUILD > CLIENT_BUILD_1_9_4`) to handle backward compatibility. In `SendMail`, older clients send fewer bytes, so the parser skips extra data for newer clients to prevent misalignment. Similarly, `MailCreateTextItem` only reads `mailTemplateId` for newer clients.
*   **Default Initialization**: Constructors initialize numeric fields (`stationeryId`, `packageId`, `money`, `COD`, `mailId`) to `0` or use default member initializers. This ensures that if `ReadFromWorldPacket` fails or is not called, the object remains in a known safe state.
*   **GUID Semantics**: `mailboxGuid` is present in almost every packet, indicating that mail actions are tied to a specific physical mailbox entity in the world, not just the player's account. `itemGuid` in `SendMail` allows attaching existing items from the player's inventory.

## Member Reference

**SendMail**
Constructor initializes the `SendMail` packet structure with default zero values for numeric fields and registers the packet opcode `CMSG_SEND_MAIL`.

**ReadFromWorldPacket#8**
Implements deserialization for `SendMail`. Extracts `mailboxGuid`, `receiverName`, `subject`, `body`, `stationeryId`, `packageId`, `itemGuid`, `money`, and `COD`. Conditionally skips 9 bytes of padding for client builds newer than 1.9.4. Calls `ObjectGuid/operator>>` and `ByteBuffer/operator>>`.

**ReadFromWorldPacket#5**
Implements deserialization for `MailReturnToSender`. Extracts `mailboxGuid` and `mailId`. Calls `ObjectGuid/operator>>` and `ByteBuffer/operator>>`.

**ReadFromWorldPacket#4**
Implements deserialization for `MailMarkAsRead`. Extracts `mailboxGuid` and `mailId`. Calls `ObjectGuid/operator>>` and `ByteBuffer/operator>>`.

**ReadFromWorldPacket#6**
Implements deserialization for `MailTakeItem`. Extracts `mailboxGuid` and `mailId`. Calls `ObjectGuid/operator>>` and `ByteBuffer/operator>>`.

**ReadFromWorldPacket#7**
Implements deserialization for `MailTakeMoney`. Extracts `mailboxGuid` and `mailId`. Calls `ObjectGuid/operator>>` and `ByteBuffer/operator>>`.

**ReadFromWorldPacket**
Implements deserialization for `GetMailList`. Extracts only `mailboxGuid`. Calls `ObjectGuid/operator>>`.

**ReadFromWorldPacket#3**
Implements deserialization for `MailDelete`. Extracts `mailboxGuid` and `mailId`. Calls `ObjectGuid/operator>>` and `ByteBuffer/operator>>`.

**ReadFromWorldPacket#2**
Implements deserialization for `MailCreateTextItem`. Extracts `mailboxGuid` and `mailId`. Conditionally extracts `mailTemplateId` for client builds newer than 1.9.4. Calls `ObjectGuid/operator>>` and `ByteBuffer/operator>>`.

---

<!-- machine-true, projected from graph.json -->

## Map — game_Server_Packets_Mail

*Source:* Mail.cpp, Mail.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ReadFromWorldPacket#8 | method | ByteBuffer/operator>>, ByteBuffer/operator>>#9, ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket#5 | method | ByteBuffer/operator>>#9, ObjectGuid/operator>> | — | — |
| SendMail | ctor | — | — | — |
| ReadFromWorldPacket#4 | method | ByteBuffer/operator>>#9, ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket#6 | method | ByteBuffer/operator>>#9, ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket#7 | method | ByteBuffer/operator>>#9, ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket | method | ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket#3 | method | ByteBuffer/operator>>#9, ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket#2 | method | ByteBuffer/operator>>#9, ObjectGuid/operator>> | — | — |
