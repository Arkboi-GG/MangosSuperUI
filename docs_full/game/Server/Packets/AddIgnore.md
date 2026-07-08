# AddIgnore

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# AddIgnore

**AddIgnore** is a client-side packet handler class within the `WorldPackets::Misc` namespace, responsible for deserializing the `CMSG_ADD_IGNORE` message sent by the game client. Its sole responsibility is to extract the name of a character the player wishes to ignore from the raw network packet buffer and store it in the `ignoreName` member variable. This class acts as a data transfer object (DTO) that bridges the low-level binary protocol of the World of Warcraft client with the high-level server logic that processes social list updates.

As part of the Mangos/WowVMaNGOS packet handling architecture, `AddIgnore` inherits from `ClientPacket`. It does not contain business logic for validating the ignore request, checking database limits, or updating the player's social list; those responsibilities lie in the calling unit (typically the session or player handler that dispatches packets). The class is strictly concerned with correct binary parsing according to the protocol definition for the supported client build.

## Member Behavior

The unit contains a single member: the constructor.

### **AddIgnore**
The default constructor initializes the packet structure. It performs two critical setup tasks:
1.  **Protocol Identification**: It calls the base class constructor `ClientPacket(CMSG_ADD_IGNORE)`, registering this instance with the specific opcode `CMSG_ADD_IGNORE`. This allows the central packet dispatcher to route incoming binary data with this opcode to an instance of `AddIgnore`.
2.  **Member Initialization**: It implicitly default-initializes the `std::string ignoreName` member. In modern C++, this results in an empty string, ready to be populated by the `ReadFromWorldPacket` method (which is declared in the shared header but implemented elsewhere, likely in a corresponding `.cpp` file or via inline expansion not shown in this partial).

## Cross-Unit Boundaries

*   **Calls Out**: None. The constructor does not invoke any functions from other units.
*   **Called By**: None listed in the MAP. However, in the broader system context, this constructor is invoked by the packet factory/dispatcher when a `CMSG_ADD_IGNORE` opcode is detected on the wire. The resulting object is then passed to the handler logic (e.g., `Player::HandleAddIgnore`) which reads the `ignoreName` field after `ReadFromWorldPacket` has been executed.

## Data Model

This unit does not interact directly with any database tables. It operates entirely on in-memory network buffers. The `ignoreName` string it extracts will eventually be used by higher-level logic to query or update social list tables (such as `character_social` or similar, depending on the specific server implementation), but `AddIgnore` itself has no SQL queries or table dependencies.

## Notable Implementation Details

*   **Final Class**: The class is marked `final`, preventing further inheritance. This is consistent with the design pattern where packet classes are leaf nodes in the hierarchy.
*   **String Storage**: The `ignoreName` is stored as a `std::string`. This implies that the `ReadFromWorldPacket` implementation (not shown in this partial but declared in the header) must handle the extraction of a null-terminated or length-prefixed string from the `WorldPacket` buffer. Engineers maintaining the reading logic must ensure the string extraction matches the client's encoding (typically UTF-8 in later builds, or locale-specific in earlier ones).
*   **No Validation**: The class provides no validation for the `ignoreName`. Empty strings, excessively long names, or invalid characters are not filtered at this layer. Validation must occur in the consuming logic.

## Member Reference

**AddIgnore**
Constructor for the `AddIgnore` packet. Initializes the base `ClientPacket` with the opcode `CMSG_ADD_IGNORE` and prepares the `ignoreName` member for population during packet deserialization.

---

<!-- machine-true, projected from graph.json -->

## Map — AddIgnore

*Source:* Misc.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| AddIgnore | ctor | — | — | — |
