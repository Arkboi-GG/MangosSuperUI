# MoveFlagChangeAck

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`MoveFlagChangeAck` is a client-to-server packet structure within the `WorldPackets::Movement` namespace, defined in `Movement.h`. It serves as the data container for acknowledging specific movement flag changes initiated by the server. Specifically, it handles acknowledgments for hovering (`CMSG_MOVE_HOVER_ACK`), feather falling (`CMSG_MOVE_FEATHER_FALL_ACK`), and water walking (`CMSG_MOVE_WATER_WALK_ACK`).

The class inherits from `ClientPacket`, indicating it represents data received from the game client. Its primary responsibility is to parse the raw binary data from the incoming network packet into structured fields (`guid`, `movementCounter`, `movementInfo`, and `apply`) so that the server logic can verify the client's state matches the server's expectations regarding these movement modifiers.

## Member-by-Member Behavior

### Construction and Initialization

**MoveFlagChangeAck**
This is the default constructor for the packet. It initializes the base `ClientPacket` with `OPCODE_WILL_BE_SET_IN_READ_FUNCTION`. This indicates that the specific opcode (message type) is not known at construction time but will be determined dynamically when the packet is read from the network stream. This design pattern allows a single parser routine to handle multiple similar opcodes (hover, feather fall, water walk) by setting the correct opcode context before invoking the read logic.

The constructor also initializes the member variables:
*   `guid`: Default-initialized (empty/null GUID).
*   `movementCounter`: Conditionally compiled. If the supported client build is greater than `CLIENT_BUILD_1_9_4`, it is initialized to `0`. Otherwise, this field is excluded from the struct layout.
*   `movementInfo`: Default-initialized `MovementInfo` object.
*   `apply`: Initialized to `false`. This boolean likely indicates whether the flag is being applied (`true`) or removed (`false`), though the exact semantic meaning depends on the parsing logic in `ReadFromWorldPacket` (which is declared here but implemented elsewhere).

## Cross-Unit Boundaries

*   **Calls Out:** None. The constructor performs no external calls.
*   **Called By:** None listed in the map. However, logically, instances of `MoveFlagChangeAck` are created and populated by the network layer when processing incoming packets with opcodes `CMSG_MOVE_HOVER_ACK`, `CMSG_MOVE_FEATHER_FALL_ACK`, or `CMSG_MOVE_WATER_WALK_ACK`. The `ReadFromWorldPacket` method (declared in this header, implemented in the corresponding `.cpp` file) is the entry point for populating this structure from a `WorldPacket`.

## Data Model

This unit does not interact directly with any database tables. It operates purely on in-memory network packet structures.

## Notable Implementation Details

*   **Conditional Compilation:** The presence of `movementCounter` is guarded by `#if SUPPORTED_CLIENT_BUILD > CLIENT_BUILD_1_9_4`. This reflects changes in the World of Warcraft client protocol over time. Older clients (1.9.4 and below) did not include a movement counter in these acknowledgment packets, while newer clients do. The server must account for this difference to correctly parse the binary stream.
*   **Opcode Flexibility:** The use of `OPCODE_WILL_BE_SET_IN_READ_FUNCTION` suggests that the `ReadFromWorldPacket` implementation (not shown here, but implied by the base class interface) is responsible for identifying the specific opcode from the incoming data or context and potentially routing the parsed data accordingly. This allows `MoveFlagChangeAck` to serve three distinct client messages with identical payload structures.
*   **Inheritance:** As a subclass of `ClientPacket`, it relies on the base class for common packet handling utilities, such as serialization helpers or opcode management.

## Member Reference

**MoveFlagChangeAck**
Default constructor for the `MoveFlagChangeAck` packet. Initializes the base `ClientPacket` with a placeholder opcode (`OPCODE_WILL_BE_SET_IN_READ_FUNCTION`), indicating the specific message type is resolved during the read phase. Initializes `guid`, `movementInfo`, and `apply` (to `false`). Conditionally initializes `movementCounter` to `0` if the target client build is newer than 1.9.4.

---

<!-- machine-true, projected from graph.json -->

## Map — MoveFlagChangeAck

*Source:* Movement.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| MoveFlagChangeAck | ctor | — | — | — |
