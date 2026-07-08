# UpdateAccountData

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# UpdateAccountData

**Purpose & Responsibilities**

`UpdateAccountData` is a client-side packet definition within the `WorldPackets::Misc` namespace, responsible for representing the `CMSG_UPDATE_ACCOUNT_DATA` message sent from the game client to the server. Its primary role is to deserialize raw binary network data into structured fields that the server can process. Specifically, it captures account-related data updates—such as macro lists, camera settings, or other persistent user preferences—that are transmitted in a compressed format. The class acts as a thin wrapper around the deserialization logic, exposing the resulting payload (`type`, `decompressedSize`, and `compressedData`) to higher-level server handlers.

**Member-by-Member Behavior**

The unit contains a single member: the constructor.

*   **`UpdateAccountData`**: This default constructor initializes the packet object. It sets the internal opcode identifier to `CMSG_UPDATE_ACCOUNT_DATA`, ensuring that the packet routing system correctly identifies incoming messages of this type. It also initializes the public data members to safe defaults: `type` and `decompressedSize` are set to `0`, and `compressedData` is initialized as an empty vector. The actual population of these fields occurs later via the inherited `ReadFromWorldPacket` method (defined in the base `ClientPacket` hierarchy and implemented in the corresponding `.cpp` file, though the implementation details are not part of this header-only declaration).

**Cross-Unit Boundaries**

*   **Calls Out**: None. The constructor performs no external calls.
*   **Called By**: None listed in the map. In practice, instances of `UpdateAccountData` are typically created by the packet reading infrastructure when a `CMSG_UPDATE_ACCOUNT_DATA` opcode is detected on the socket. The server’s packet handler then invokes `ReadFromWorldPacket` to populate the fields.

**Data Model**

This unit does not interact directly with any database tables. It operates solely on in-memory network packet data. The `compressedData` field likely corresponds to data that will eventually be persisted to tables such as `account_data` (common in WoW emulators for storing macros, camera angles, etc.), but this persistence logic resides in other units (e.g., `Player` or `Session` classes) that consume this packet. No SQL queries or table references are present in this header.

**Notable Implementation Details**

*   **Compression Handling**: The presence of `decompressedSize` and `compressedData` indicates that the client sends this data in a compressed format (likely zlib or similar). The server must decompress this data before processing. The `decompressedSize` field is crucial for allocating the correct buffer size during decompression to prevent buffer overflows.
*   **Type Identification**: The `type` field distinguishes between different kinds of account data (e.g., macros vs. camera settings). The server must switch on this value to route the decompressed data to the appropriate storage mechanism.
*   **Inheritance**: As a `final` class inheriting from `ClientPacket`, it relies on the base class for memory management and basic packet lifecycle handling. The `explicit` keyword on the constructor prevents implicit conversions from other types.

## Member Reference

**UpdateAccountData**
Constructor for the `UpdateAccountData` packet. Initializes the packet opcode to `CMSG_UPDATE_ACCOUNT_DATA` and sets default values for `type` (0), `decompressedSize` (0), and `compressedData` (empty vector). Prepares the object for subsequent deserialization of network data.

---

<!-- machine-true, projected from graph.json -->

## Map — UpdateAccountData

*Source:* Misc.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| UpdateAccountData | ctor | — | — | — |
