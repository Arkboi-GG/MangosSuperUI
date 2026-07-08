# SetWatchedFaction

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# SetWatchedFaction

**Purpose & Responsibilities**

`SetWatchedFaction` is a client-side packet structure within the `WorldPackets::Misc` namespace, responsible for representing the `CMSG_SET_WATCHED_FACTION` message sent from the game client to the server. Its sole responsibility is to declare the data layout of this specific network message: identifying the faction reputation list ID (`repId`) that the player wishes to monitor in their UI.

This unit is conditionally compiled only for client builds newer than `CLIENT_BUILD_1_9_4`, indicating that the "watched faction" feature was introduced or standardized in later versions of the World of Warcraft client protocol supported by this emulator. As a `ClientPacket`, it inherits the base infrastructure for parsing incoming binary data but contains no business logic itself; it serves strictly as a data container for the deserialization process.

## Member-by-Member Behavior

The unit consists of a single constructor and one public data member.

### **SetWatchedFaction** (Constructor)
The default constructor initializes the packet object. It performs two key actions:
1.  Invokes the base `ClientPacket` constructor, passing the opcode `CMSG_SET_WATCHED_FACTION`. This registers the packet type with the networking layer so that incoming messages with this opcode are routed to instances of this class.
2.  Initializes the `repId` member to `0`. This provides a safe default value before the packet is populated by reading from the raw network stream.

### **repId** (Public Data Member)
A signed 32-bit integer (`int32`) that stores the Faction Reputation List ID. This ID corresponds to a specific faction in the game's data structures (typically linked to `dbc` files or database entries defining faction relationships). The use of `int32` suggests that while IDs are generally positive, the protocol may allow for negative values or use the sign bit for specific flags, though typically faction IDs are unsigned in higher-level logic. This field is populated by the `ReadFromWorldPacket` method (inherited from `ClientPacket` and implemented elsewhere, likely in a corresponding `.cpp` file not included in this partial, or via template specialization).

## Cross-Unit Boundaries

*   **Calls Out:** None. The constructor only calls the base class constructor.
*   **Called By:** None listed in the map. In practice, this class is instantiated by the network dispatcher when a `CMSG_SET_WATCHED_FACTION` packet is received from a client. The dispatcher creates an instance of `SetWatchedFaction`, calls `ReadFromWorldPacket` to fill `repId`, and then passes the object to a handler function (likely in a session or player class) that updates the player's watched faction state.

## Data Model

This unit does not directly interact with database tables. It operates entirely in memory as part of the network packet processing pipeline. The `repId` value it carries will eventually be used by other parts of the system (e.g., `Player` or `Session` classes) to update the player's state, which *may* involve database writes, but `SetWatchedFaction` itself is agnostic to persistence.

## Notable Implementation Details

*   **Conditional Compilation:** The entire class definition is wrapped in `#if SUPPORTED_CLIENT_BUILD > CLIENT_BUILD_1_9_4`. This means the class does not exist for older client versions. Code handling this packet must also be guarded by similar preprocessor checks to avoid compilation errors on older builds.
*   **Inheritance:** It inherits from `ClientPacket`, implying it shares common functionality for packet identification and reading with all other client-to-server messages.
*   **Final Class:** The class is marked `final`, preventing further inheritance. This is appropriate for a leaf-node packet structure.
*   **Namespace:** Located in `WorldPackets::Misc`, grouping it with other miscellaneous client commands that don't fit into more specific categories like combat, movement, or chat.

## Member Reference

**SetWatchedFaction**
Default constructor for the `SetWatchedFaction` packet. It initializes the base `ClientPacket` with the opcode `CMSG_SET_WATCHED_FACTION` and sets the `repId` member to `0`. This class is only available for client builds newer than `CLIENT_BUILD_1_9_4`.

---

<!-- machine-true, projected from graph.json -->

## Map — SetWatchedFaction

*Source:* Misc.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| SetWatchedFaction | ctor | — | — | — |
