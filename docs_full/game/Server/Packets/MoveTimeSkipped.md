# MoveTimeSkipped

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# MoveTimeSkipped

**MoveTimeSkipped** is a client-to-server packet handler within the `WorldPackets::Movement` namespace, responsible for processing the `CMSG_MOVE_TIME_SKIPPED` message. This packet is sent by the game client to inform the server that a significant amount of time was skipped during movement processing, typically due to network lag or desynchronization between the client's internal clock and the server's expected timeline.

The class inherits from `ClientPacket`, establishing it as a structure for incoming data from the client. It contains two primary data members:
1.  **`guid`**: An `ObjectGuid` identifying the entity (usually the player character) associated with the movement event.
2.  **`lag`**: A `uint32` value representing the amount of time skipped, initialized to `0`.

The constructor `MoveTimeSkipped()` initializes the base `ClientPacket` with the opcode `CMSG_MOVE_TIME_SKIPPED`, ensuring that the network layer correctly identifies incoming packets of this type. The class also declares an override of `ReadFromWorldPacket`, which is responsible for deserializing the raw binary data from the network buffer into the `guid` and `lag` fields. However, the implementation of `ReadFromWorldPacket` is not provided in the current source file (`Movement.h`), implying it is defined elsewhere (likely in a corresponding `.cpp` file or another partial of the class).

This unit does not interact with any database tables, nor does it call out to other units or get called by other units according to the provided map. Its sole responsibility is to define the structure and initialization for handling time-skipped movement events from the client.

## Member Reference

**MoveTimeSkipped**
Constructor for the `MoveTimeSkipped` packet. It initializes the base `ClientPacket` class with the specific opcode `CMSG_MOVE_TIME_SKIPPED`, allowing the network subsystem to route incoming packets with this opcode to this handler. It does not perform any additional initialization of the `guid` or `lag` members beyond their default construction/initialization.

---

<!-- machine-true, projected from graph.json -->

## Map — MoveTimeSkipped

*Source:* Movement.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| MoveTimeSkipped | ctor | — | — | — |
