# MoveKnockBackAck

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`MoveKnockBackAck` is a data structure representing a specific client-to-server network packet in the `wowvmangos` emulator. It serves as the acknowledgment message sent by the game client to the server after the server has issued a command to knock a character back (typically due to spell effects or combat mechanics).

The class resides in the `WorldPackets::Movement` namespace and inherits from `ClientPacket`, indicating it is part of the incoming packet processing pipeline. Its primary responsibility is to deserialize the raw binary data received from the client into structured fields (`guid`, `movementCounter`, and `movementInfo`) that the server’s movement handling subsystem can interpret. This allows the server to synchronize the client’s state with the authoritative server state following a knockback event.

## Member-by-Member Behavior

### Constructor: `MoveKnockBackAck`

The constructor initializes the packet object with the specific opcode `CMSG_MOVE_KNOCK_BACK_ACK`. This opcode identifies the packet type within the network stream, allowing the server’s packet dispatcher to route the incoming data to the correct handler. The constructor relies on the base class `ClientPacket` to manage the underlying buffer and opcode assignment. No additional initialization logic is performed in this constructor itself; member variables are default-initialized or left to be populated during the `ReadFromWorldPacket` phase.

## Cross-Unit Boundaries

This unit is a leaf node in the call graph regarding outgoing calls; it does not invoke functions in other units. However, it is part of a larger packet processing ecosystem:

*   **Called By:** While the MAP indicates no explicit callers, in practice, instances of `MoveKnockBackAck` are typically constructed and processed by the main network loop or packet dispatcher (likely in a unit such as `WorldSession` or `PacketHandler`). The dispatcher creates an instance of this class, calls its `ReadFromWorldPacket` method (defined in the base class or overridden here, though the override is not shown in the provided source snippet for this specific class, implying it may use a default or template-based implementation common to `ClientPacket` derivatives), and then passes the populated object to a movement-specific handler.
*   **Dependencies:** It depends on `ObjectGuid` for entity identification and `MovementInfo` for detailed movement state. These types are defined in other headers (`ObjectGuid.h` and `MovementInfo.h` respectively), which are included via `Movement.h`'s dependencies.

## Data Model

This unit does not interact directly with any database tables. It operates entirely in memory, processing transient network data.

## Notable Implementation Details

1.  **Conditional Compilation for Movement Counter:**
    The class contains a conditional compilation block:
    ```cpp
    #if SUPPORTED_CLIENT_BUILD > CLIENT_BUILD_1_9_4
        uint32       movementCounter = 0;
    #endif
    ```
    This indicates that the `movementCounter` field is only present in the packet structure for client builds newer than version 1.9.4. This counter is likely used for sequence tracking or anti-cheat/movement validation purposes introduced in later patches. Maintainers must ensure that any code reading or writing this field respects the same conditional compilation guards to avoid memory corruption or deserialization errors when supporting older clients.

2.  **Inheritance from `ClientPacket`:**
    As a subclass of `ClientPacket`, `MoveKnockBackAck` inherits the mechanism for reading raw bytes from the `WorldPacket` buffer. The actual deserialization logic for `guid`, `movementCounter` (if applicable), and `movementInfo` is likely handled by a generic template or macro system within the `ClientPacket` base class or through the `ReadFromWorldPacket` override. Since the override is declared but not defined in the provided source, the implementation details of *how* the bytes are parsed are hidden in the base class infrastructure.

3.  **Final Class:**
    The class is marked `final`, preventing further inheritance. This ensures that the packet structure remains stable and predictable for the network layer.

## Member Reference

**MoveKnockBackAck**
Constructor for the `MoveKnockBackAck` packet. Initializes the base `ClientPacket` with the opcode `CMSG_MOVE_KNOCK_BACK_ACK`. It prepares the object to receive and parse incoming network data related to knockback acknowledgments. The constructor does not perform any custom logic beyond base class initialization.

---

<!-- machine-true, projected from graph.json -->

## Map — MoveKnockBackAck

*Source:* Movement.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| MoveKnockBackAck | ctor | — | — | — |
