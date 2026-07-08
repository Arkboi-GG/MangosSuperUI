# Bug

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Unit Documentation: `WorldPackets::Misc::Bug`

**File:** `Misc.h`  
**Namespace:** `WorldPackets::Misc`  
**Class:** `Bug`

## Purpose & Responsibilities

The `Bug` class is a lightweight data structure representing a client-to-server network packet (`CMSG_BUG`). Its sole responsibility is to encapsulate the raw data sent by the game client when a player submits a bug report or suggestion. It acts as a container for three specific fields: a numeric suggestion identifier, the textual content of the report, and a string describing the type of bug.

As a `ClientPacket`, it inherits the mechanism for identifying itself via the opcode `CMSG_BUG` and provides the interface (`ReadFromWorldPacket`) required to deserialize binary network data into these member variables. This unit contains no business logic, validation, or persistence code; it is purely a transport layer object.

## Member-by-Member Behavior

### **Bug** (Constructor)
The constructor initializes the packet instance. It performs two key actions:
1.  **Base Initialization:** It calls the `ClientPacket` base class constructor, passing `CMSG_BUG`. This registers the packet's opcode, allowing the server's network dispatcher to route incoming packets with this opcode to the correct handler.
2.  **Member Initialization:** It initializes the `suggestion` member variable to `0`. The `content` and `type` members are default-initialized as empty strings by their respective constructors (`std::string`).

This constructor ensures that every `Bug` packet starts in a known, clean state before any network data is read into it.

## Cross-Unit Boundaries

*   **Calls Out:** None. The `Bug` constructor does not invoke any functions from other units.
*   **Called By:** None listed in the map. In practice, this constructor is invoked by the server's packet parsing infrastructure (likely within the `WorldSession` or a central packet router) when a `CMSG_BUG` packet is received from the wire. The caller will subsequently invoke `ReadFromWorldPacket` (declared in this header but implemented elsewhere) to populate the data.

## Data Model

This unit interacts with no database tables. It operates entirely in memory as a transient representation of network traffic.

## Notable Implementation Details

*   **Opcode Dependency:** The correctness of this packet relies on the constant `CMSG_BUG` being correctly defined in the shared defines (included via `SharedDefines.h`). If the opcode value mismatches between client and server, this packet will never be instantiated.
*   **String Handling:** The `content` and `type` fields are `std::string`. The actual deserialization logic (in `ReadFromWorldPacket`, not shown in this unit's source) must handle potential encoding issues or length limits imposed by the client protocol. This unit merely holds the resulting strings.
*   **Suggestion Field:** The `suggestion` field is a `uint32`. While named "suggestion," its semantic meaning is opaque in this unit. It likely corresponds to a predefined category ID or a boolean flag (e.g., 1 for suggestion, 0 for bug) determined by the client UI, but this unit treats it as a raw integer.

## Member Reference

**Bug**  
Constructor for the `Bug` packet. Initializes the base `ClientPacket` with opcode `CMSG_BUG` and sets the `suggestion` member to `0`. Default-initializes `content` and `type` as empty strings.

---

<!-- machine-true, projected from graph.json -->

## Map — Bug

*Source:* Misc.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| Bug | ctor | — | — | — |
