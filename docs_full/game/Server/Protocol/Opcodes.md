# Opcodes

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Opcodes

## Purpose & Responsibilities

The `Opcodes` unit (comprising `Opcodes.cpp` and `Opcodes.h`) serves as the central dispatch registry and classification utility for network packets in the wowvmangos server. Its primary responsibilities are:

1.  **Packet Dispatch Mapping:** It constructs and maintains a static lookup table (`handlerList`) that maps every known network opcode (message ID) to its corresponding handler function within `WorldSession`. This allows the main network loop to route incoming client packets to the correct business logic without using large `switch` statements or string comparisons at runtime.
2.  **Opcode Classification:** It provides inline helper functions to categorize opcodes into semantic groups (e.g., movement acknowledgments, speed changes, stop commands). These are used by anti-cheat and movement validation systems to quickly determine the nature of a packet without parsing its contents.
3.  **Safety & Debugging:** It defines bounds-checking utilities to identify invalid or "bogus" opcodes and provides human-readable names for logging and debugging purposes.

This unit does not contain business logic itself; rather, it configures how `WorldSession` (defined in `WorldSession.h/cpp`) responds to network events. It acts as the bridge between raw network bytes and the server's internal state machine.

## Member-by-Member Behavior

### Packet Dispatch Construction

**`BuildOpcodeList`**
This function is the core initializer for the opcode registry. It constructs a `Handlers` struct containing an array of `OpcodeHandler` entries indexed by opcode ID. It uses two macros to populate this table:
*   `DEFINE_HANDLER`: Registers a valid opcode. It binds the opcode to a specific `WorldSession` member function (the handler), specifies the required session status (e.g., `STATUS_LOGGEDIN`), and defines the processing strategy (e.g., `PACKET_PROCESS_WORLD`). It also sets up a generic packet reader template.
*   `INVALID_PACKET`: Marks an opcode as invalid or unhandled. It assigns a reason via the `UnhandleReason` enum (e.g., `SendByServer`, `Unhandled`, `Invalid`). This prevents the server from attempting to process packets it shouldn't receive or doesn't support.

The function iterates through hundreds of opcodes, conditionally including/excluding them based on `SUPPORTED_CLIENT_BUILD` preprocessor directives. The resulting `Handlers` object is stored in the global constant `handlerList`.

**`LookupOpcodeHandler`**
Retrieves the `OpcodeHandler` structure for a given opcode ID. It performs a bounds check against `NUM_MSG_TYPES`. If the ID is out of range, it returns a reference to `emptyHandler` (a default-constructed `OpcodeHandler` with no implementation). Otherwise, it returns the entry from `handlerList`. This function is the primary entry point for the network layer to find how to process a received packet.

**`LookupOpcodeName`**
Returns a C-string name for a given opcode ID. It delegates to `LookupOpcodeHandler` and extracts the `.name` field. This is used extensively for logging unexpected or debug packets.

### Opcode Classification Helpers

These inline functions in `Opcodes.h` allow other units to quickly classify opcodes based on their semantic meaning. They are critical for the movement anticheat system.

**`IsAnyMoveAckOpcode`**
Returns `true` if the opcode is any type of movement acknowledgment. This includes teleport acks, speed change acks, root/unroot acks, knockback acks, hover/fall/waterwalk acks, etc.
*   *Called by:* `MovementAnticheat/CheckBotting`, `MovementAnticheat/CheckMoveStart`.
*   *Purpose:* Used to verify that the client has acknowledged previous movement commands sent by the server, ensuring synchronization.

**`IsFlagAckOpcode`**
Returns `true` if the opcode acknowledges a change in movement flags (specifically root, unroot, waterwalk, hover, feather fall).
*   *Called by:* `MovementAnticheat/HandlePositionTests`.
*   *Purpose:* Used to validate that specific state-changing flags were properly acknowledged by the client.

**`IsSpeedAckOpcode`**
Returns `true` if the opcode acknowledges a forced speed change (run, swim, turn rate).
*   *Called by:* None listed in the map, but logically similar to `IsFlagAckOpcode`.

**`IsStopOpcode`**
Returns `true` if the opcode represents a stop command (stop moving, stop strafing, stop turning, stop pitching, stop swimming).
*   *Called by:* None listed in the map.

**`IsFallEndOpcode`**
Returns `true` if the opcode signifies the end of a fall (`MSG_MOVE_FALL_LAND`) or the start of swimming (`MSG_MOVE_START_SWIM`, which often follows a fall into water).
*   *Called by:* `MovementAnticheat/CheckFallStop`, `MovementAnticheat/CheckMoveStart`, `MovementAnticheat/HandlePositionTests`, `WorldSession.MovementHandler/HandleMovementOpcodes`.
*   *Purpose:* Critical for validating vertical movement physics. The anticheat checks if a player claims to have landed or started swimming at a valid time/position relative to their previous fall trajectory.

**`IsDefinitelyBogusOpcode`**
Returns `true` if the opcode ID is greater than or equal to `NUM_MSG_TYPES`.
*   *Called by:* `WorldSocket/DoRecvIncomingData`.
*   *Purpose:* A fast, early-exit check in the low-level socket receiver. If an opcode is out of the defined range, it is immediately rejected as malformed or malicious, preventing further processing overhead.

## Cross-Unit Boundaries

### Collaboration with `WorldSession`
*   **Direction:** `Opcodes` -> `WorldSession` (Indirectly via registration) and `WorldSession` -> `Opcodes` (via lookup).
*   **Mechanism:** `BuildOpcodeList` stores pointers to `WorldSession` member functions (e.g., `&WorldSession::HandleCharCreateOpcode`). When `WorldSession.Main/Process` receives a packet, it calls `LookupOpcodeHandler` to get the handler pointer and then invokes it on the `WorldSession` instance.
*   **Why:** This decouples the network protocol definition from the session logic. `WorldSession` implements the behavior; `Opcodes` defines the routing.

### Collaboration with `MovementAnticheat`
*   **Direction:** `MovementAnticheat` -> `Opcodes`.
*   **Mechanism:** Functions like `CheckBotting`, `CheckMoveStart`, `CheckFallStop`, and `HandlePositionTests` call `IsAnyMoveAckOpcode`, `IsFlagAckOpcode`, and `IsFallEndOpcode`.
*   **Why:** The anticheat system needs to distinguish between different types of movement packets to apply specific validation rules. For example, it treats fall landings differently from standard movement stops.

### Collaboration with `WorldSocket`
*   **Direction:** `WorldSocket` -> `Opcodes`.
*   **Mechanism:** `WorldSocket/DoRecvIncomingData` calls `IsDefinitelyBogusOpcode`.
*   **Why:** To filter out invalid packets at the lowest possible level (socket reception) before they consume resources in the session processing queue.

### Collaboration with `WorldSession.Main`
*   **Direction:** `WorldSession.Main` -> `Opcodes`.
*   **Mechanism:** Various methods in `WorldSession.Main` (e.g., `Process`, `AllowPacket`, `LogUnexpectedOpcode`) call `LookupOpcodeHandler` and `LookupOpcodeName`.
*   **Why:** To resolve the handler for incoming packets and to generate log messages with readable opcode names during debugging or error handling.

## Data Model

This unit does not interact with any database tables. It operates entirely on in-memory structures and compile-time constants.

## Notable Implementation Details

1.  **Static Initialization Order:** The `handlerList` is initialized by calling `BuildOpcodeList()` at global scope. This ensures the table is ready before any network packets are processed. The comment in `Opcodes.h` notes a specific Visual Studio x64 release bug related to incomplete type definitions of `WorldSession` if headers are included in the wrong order, necessitating the explicit include of `WorldSession.h` in `Opcodes.h`.

2.  **Generic Handler Template:** The `DEFINE_HANDLER` macro uses a template trick (`get_packet_class`) to deduce the packet type from the handler function signature. This allows a single generic `Handle_GenericRead` and `Handle_GenericPacket` mechanism to parse and dispatch packets, reducing code duplication. The handler is cast to a common signature `void (WorldSession::*)(ClientPacket const&)`.

3.  **Conditional Compilation:** The opcode list is heavily guarded by `#if SUPPORTED_CLIENT_BUILD > ...` directives. This reflects the evolution of the World of Warcraft protocol across different patch versions (1.5.1, 1.6.1, 1.8.4, 1.9.4, 1.10.2, 1.11.2). Opcodes added or removed in specific patches are included or excluded accordingly. For example, `CMSG_WARDEN_DATA` is only defined for builds after 1.5.1.

4.  **Invalid Packet Handling:** Instead of crashing or ignoring unknown opcodes, the system explicitly marks them with `UnhandleReason`. Reasons include:
    *   `Invalid`: The opcode is fundamentally invalid (e.g., `MSG_NULL_ACTION`).
    *   `Unhandled`: The server does not implement handling for this opcode yet.
    *   `SendByServer`: The opcode is sent *by* the server *to* the client, so receiving it from the client is an error.
    *   `AlreadyHandledElsewhere`: The packet is processed before reaching this general dispatcher (e.g., authentication packets).

5.  **Empty Handler Fallback:** `LookupOpcodeHandler` returns a reference to `emptyHandler` for out-of-bounds IDs. `emptyHandler` is a global variable initialized with default values (name `"<unknown opcode>"`, impl `unexpected(Unhandled)`). This prevents null pointer dereferences when looking up invalid opcodes.

6.  **Performance:** The lookup is an O(1) array access (`handlerList.handlers[id]`). The classification helpers (`IsAnyMoveAckOpcode`, etc.) are simple `switch` statements compiled to efficient jump tables or binary searches, making them suitable for high-frequency calls in the movement validation path.

## Member Reference

**`IsAnyMoveAckOpcode`**: Inline function returning `true` if the opcode is a movement acknowledgment (teleport, speed, root, hover, etc.). Used by `MovementAnticheat/CheckBotting` and `MovementAnticheat/CheckMoveStart`.

**`BuildOpcodeList`**: Function that constructs the global `handlerList` by registering all valid opcodes with their `WorldSession` handlers and marking invalid ones. Uses `DEFINE_HANDLER` and `INVALID_PACKET` macros.

**`IsFlagAckOpcode`**: Inline function returning `true` if the opcode acknowledges a movement flag change (root, unroot, waterwalk, hover, feather fall). Used by `MovementAnticheat/HandlePositionTests`.

**`IsSpeedAckOpcode`**: Inline function returning `true` if the opcode acknowledges a forced speed change (run, swim, turn rate).

**`IsStopOpcode`**: Inline function returning `true` if the opcode is a stop command (move, strafe, turn, pitch, swim).

**`IsFallEndOpcode`**: Inline function returning `true` if the opcode is `MSG_MOVE_FALL_LAND` or `MSG_MOVE_START_SWIM`. Used by `MovementAnticheat/CheckFallStop`, `MovementAnticheat/CheckMoveStart`, `MovementAnticheat/HandlePositionTests`, and `WorldSession.MovementHandler/HandleMovementOpcodes`.

**`IsDefinitelyBogusOpcode`**: Inline function returning `true` if the opcode ID is >= `NUM_MSG_TYPES`. Used by `WorldSocket/DoRecvIncomingData` for early rejection.

**`LookupOpcodeHandler`**: Function returning a reference to the `OpcodeHandler` for a given ID. Returns `emptyHandler` if the ID is out of bounds. Used by `WorldSession.Main/Process`, `WorldSession.Main/ProcessPackets`, `WorldSession.Main/QueueBinaryPacket`, and `WorldSession.Main/QueuePacket`.

**`LookupOpcodeName`**: Function returning the C-string name of an opcode. Delegates to `LookupOpcodeHandler`. Used by various `WorldSession.Main` methods for logging and debugging (`AllowPacket`, `Handle_EarlyProccess`, `Handle_NULL`, `Handle_ServerSide`, `LogUnexpectedOpcode`, `SendMovementPacket`, `SendPacket`, `VerifyPacketWasCorrectlyRead`).

---

<!-- machine-true, projected from graph.json -->

## Map — Opcodes

*Source:* Opcodes.cpp, Opcodes.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| IsAnyMoveAckOpcode | function | — | MovementAnticheat/CheckBotting, MovementAnticheat/CheckMoveStart | — |
| BuildOpcodeList | function | — | — | — |
| IsFlagAckOpcode | function | — | MovementAnticheat/HandlePositionTests | — |
| IsSpeedAckOpcode | function | — | — | — |
| IsStopOpcode | function | — | — | — |
| IsFallEndOpcode | function | — | MovementAnticheat/CheckFallStop, MovementAnticheat/CheckMoveStart, MovementAnticheat/HandlePositionTests, WorldSession.MovementHandler/HandleMovementOpcodes | — |
| IsDefinitelyBogusOpcode | function | — | WorldSocket/DoRecvIncomingData | — |
| LookupOpcodeHandler | function | — | WorldSession.Main/Process, WorldSession.Main/ProcessPackets, WorldSession.Main/QueueBinaryPacket, WorldSession.Main/QueuePacket | — |
| LookupOpcodeName | function | — | WorldSession.Main/AllowPacket, WorldSession.Main/Handle_EarlyProccess, WorldSession.Main/Handle_NULL, WorldSession.Main/Handle_ServerSide, WorldSession.Main/LogUnexpectedOpcode, WorldSession.Main/SendMovementPacket, WorldSession.Main/SendPacket, WorldSession.Main/VerifyPacketWasCorrectlyRead | — |
