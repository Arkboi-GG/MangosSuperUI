# MailDelete

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# MailDelete

**Purpose & Responsibilities**

`MailDelete` is a client-side packet structure within the `WorldPackets::Mail` namespace, representing the `CMSG_MAIL_DELETE` message sent by a client to request the deletion of a specific email. It serves as a data container for the raw network payload, holding the identifier of the mailbox involved and the unique ID of the mail item to be removed. As a `ClientPacket`, its primary responsibility is to define the binary layout of the incoming request and provide the interface (`ReadFromWorldPacket`) for deserializing the data from the network stream.

**Member-by-Member Behavior**

The unit consists of a single constructor and two public data members, along with an inherited virtual method declaration.

*   **Constructor (`MailDelete`)**: The explicit default constructor initializes the base `ClientPacket` class with the opcode `CMSG_MAIL_DELETE`. This opcode identifies the packet type to the server's packet handler. It does not initialize the data members explicitly in the initializer list, relying on their default initialization or subsequent assignment during reading.
*   **Data Members**:
    *   `mailboxGuid`: An `ObjectGuid` storing the unique identifier of the mailbox object associated with the delete request. This allows the server to verify the player is interacting with the correct mailbox instance.
    *   `mailId`: A `uint32` storing the unique database identifier of the mail message to be deleted. Initialized to `0` by default.
*   **`ReadFromWorldPacket`**: Declared as an override of the base class method. While the definition is not present in this header, this method is responsible for extracting the `mailboxGuid` and `mailId` from the `WorldPacket` buffer according to the protocol specification for `CMSG_MAIL_DELETE`.

**Cross-Unit Boundaries**

*   **Inheritance**: `MailDelete` inherits from `ClientPacket` (defined in `Packet.h`). This establishes the contract for packet handling, including the `ReadFromWorldPacket` interface and the opcode management.
*   **Dependencies**: It uses `ObjectGuid` (from `ObjectGuid.h`) for the mailbox identifier and standard types like `uint32` (likely from `SharedDefines.h` or similar core headers).
*   **Usage**: This packet is instantiated and populated by the network layer when a client sends a delete mail command. It is then passed to the game logic handlers (not shown in this unit) which will validate the request and perform the actual deletion in the database and player state.

**Data Model**

This unit does not directly interact with database tables. It represents a transient network message. The `mailId` corresponds to a record in the mail storage table (typically `mail` in WoW databases), and `mailboxGuid` corresponds to a creature or game object in the world, but these relationships are resolved by higher-level game logic, not by this packet structure itself.

**Notable Implementation Details**

*   **Final Class**: The class is marked `final`, indicating it cannot be subclassed. This is appropriate for a leaf packet structure.
*   **Default Initialization**: `mailId` is explicitly initialized to `0` in the member declaration. This provides a safe default value before the packet is read.
*   **Explicit Constructor**: The constructor is marked `explicit` to prevent implicit conversions from other types, ensuring type safety when creating instances.

## Member Reference

**MailDelete**
The default constructor for the `MailDelete` packet. It initializes the base `ClientPacket` with the opcode `CMSG_MAIL_DELETE`. It does not take any arguments. The data members `mailboxGuid` and `mailId` are not initialized here; `mailId` defaults to `0` via its member initializer, and `mailboxGuid` will be set when `ReadFromWorldPacket` is called.

---

<!-- machine-true, projected from graph.json -->

## Map — MailDelete

*Source:* Mail.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| MailDelete | ctor | — | — | — |
