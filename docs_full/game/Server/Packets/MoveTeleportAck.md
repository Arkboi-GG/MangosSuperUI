# MoveTeleportAck

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# MoveTeleportAck

**MoveTeleportAck** is a client-to-server packet structure within the `WorldPackets::Movement` namespace, defined in `Movement.h`. It represents the acknowledgment sent by the game client to the server confirming that a teleportation movement command has been processed. This packet is part of the broader movement synchronization system, ensuring the server and client agree on the player's position after a discrete jump in location (teleport) rather than continuous movement.

The class inherits from `ClientPacket`, indicating it is parsed from incoming network data from the client. Its primary responsibility is to deserialize the raw binary data of the `MSG_MOVE_TELEPORT_ACK` opcode into structured fields (`guid`, `movementCounter`, and `time`) that the server can use to validate the client's state.

## Member-by-Member Behavior

### **MoveTeleportAck** (Constructor)
This constructor initializes the packet object. It sets the expected opcode to `MSG_MOVE_TELEPORT_ACK` by passing it to the base `ClientPacket` constructor. It also initializes the member variables:
- `guid`: The unique identifier of the moving object (typically the player).
- `movementCounter`: A sequence number used to order movement events, included only for client builds newer than 1.9.4.
- `time`: A timestamp associated with the teleport event.

The constructor itself performs no complex logic; it merely prepares the object for deserialization via the `ReadFromWorldPacket` method (which is declared in the base class hierarchy but implemented elsewhere, likely in a corresponding `.cpp` file not provided here, though the signature is standard for this framework).

## Cross-Unit Boundaries

### Called By: `PlayerBotAI/UpdateAI#2`
The `MoveTeleportAck` constructor is invoked by the `UpdateAI` method in the `PlayerBotAI` unit (specifically at call site #2). This indicates that the bot AI system generates or simulates this acknowledgment packet during its update cycle. This is likely part of the bot's movement simulation logic, where the bot needs to acknowledge a teleport command it has received or initiated to maintain consistency with the server's expectations of client behavior. The direction of data flow is conceptual here: the `PlayerBotAI` creates an instance of `MoveTeleportAck` to represent the client-side acknowledgment, which may then be processed by the server's movement handler to confirm the bot's new position.

## Data Model

This unit does not interact directly with any database tables. It operates purely on in-memory network packet structures.

## Notable Implementation Details

1.  **Client Build Compatibility**: The `movementCounter` field is conditionally compiled using `#if SUPPORTED_CLIENT_BUILD > CLIENT_BUILD_1_9_4`. This reflects changes in the World of Warcraft protocol between different client versions. For older clients (1.9.4 and below), this field is absent, and the packet structure is smaller. Maintainers must ensure that the serialization/deserialization logic (in `ReadFromWorldPacket`) respects this conditional compilation to avoid reading incorrect offsets for older clients.
2.  **Opcode Specificity**: The packet is tied strictly to `MSG_MOVE_TELEPORT_ACK`. Unlike some other movement packets in the same header (e.g., `MoveSpeedAck` or `MoveFlagChangeAck`) which use `OPCODE_WILL_BE_SET_IN_READ_FUNCTION` because they share opcodes or are polymorphic, `MoveTeleportAck` has a fixed opcode. This simplifies routing but requires the server to handle this specific opcode distinctly.
3.  **Minimal State**: The packet carries minimal state beyond the object identity (`guid`) and timing/sequencing information (`time`, `movementCounter`). It does not contain position coordinates or movement flags, implying that the teleport destination and resulting state are either determined by the server's original teleport command or are implicit in the acknowledgment.

## Member Reference

**MoveTeleportAck**
Constructor for the `MoveTeleportAck` packet. Initializes the base `ClientPacket` with the `MSG_MOVE_TELEPORT_ACK` opcode and sets default values for `guid`, `movementCounter` (if applicable), and `time`. It is called by `PlayerBotAI/UpdateAI#2` to simulate or process teleport acknowledgments within the bot AI system.

---

<!-- machine-true, projected from graph.json -->

## Map — MoveTeleportAck

*Source:* Movement.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| MoveTeleportAck | ctor | — | PlayerBotAI/UpdateAI#2 | — |
