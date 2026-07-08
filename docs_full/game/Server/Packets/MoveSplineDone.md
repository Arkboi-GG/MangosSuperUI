# MoveSplineDone

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# MoveSplineDone

## Purpose & Responsibilities

`MoveSplineDone` is a lightweight data structure within the `WorldPackets::Movement` namespace, defined in `Movement.h`. It represents a specific client-to-server network message (`CMSG_MOVE_SPLINE_DONE`) indicating that the client has finished executing a server-initiated spline movement.

This unit serves purely as a packet definition. It contains no executable logic, no database interactions, and no cross-unit dependencies beyond its inheritance from `ClientPacket`. Its sole responsibility is to define the binary layout and opcode for this specific movement acknowledgment, allowing the network layer to deserialize incoming data into a structured object containing the associated `MovementInfo` and the unique `splineId` that completed.

## Member-by-Member Behavior

The unit consists of a single constructor, which initializes the base class with the correct network opcode.

### Initialization
**`MoveSplineDone`** (Constructor)
The default constructor initializes the `ClientPacket` base class with the constant `CMSG_MOVE_SPLINE_DONE`. This binds the packet structure to the specific network message type expected by the server's packet dispatcher. It does not initialize the member variables `movementInfo` or `splineId`; these are populated later by the `ReadFromWorldPacket` method (defined in the base class or implemented elsewhere, but not part of this unit's visible logic).

## Cross-Unit Boundaries

*   **Calls Out:** None. The constructor only invokes the base class constructor.
*   **Called By:** None listed in the map. In practice, this class is instantiated by the network packet handler when a `CMSG_MOVE_SPLINE_DONE` message is received from a client.

## Data Model

This unit does not interact with any database tables. It operates entirely in memory as part of the network packet processing pipeline.

## Notable Implementation Details

*   **Minimalist Design:** As a packet struct, `MoveSplineDone` contains no methods other than the constructor and the inherited `ReadFromWorldPacket`. All parsing logic is handled by the base `ClientPacket` infrastructure or the specific `MovementInfo` parser.
*   **Spline Identification:** The `splineId` field is crucial for the server to correlate the completion event with the original movement command issued via `SplineMovement`. Without this ID, the server would not know which pending movement sequence to mark as complete.
*   **Inheritance Context:** It inherits from `ClientPacket`, implying it is strictly inbound (client-to-server). The corresponding outbound packet (server-to-client) for initiating splines is likely defined elsewhere (e.g., in a `ServerPacket` hierarchy).

## Member Reference

**MoveSplineDone**: Default constructor that initializes the base `ClientPacket` with the opcode `CMSG_MOVE_SPLINE_DONE`.

---

<!-- machine-true, projected from graph.json -->

## Map — MoveSplineDone

*Source:* Movement.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| MoveSplineDone | ctor | — | — | — |
