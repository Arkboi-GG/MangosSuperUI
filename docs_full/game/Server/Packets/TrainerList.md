# TrainerList

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# TrainerList (`WorldPackets::Npc::TrainerList`)

## Purpose & Responsibilities

`TrainerList` is a client-side packet structure within the `WorldPackets::Npc` namespace, responsible for representing the `CMSG_TRAINER_LIST` message sent by the game client to the server. Its sole responsibility is to define the data layout for this specific network request and provide the mechanism to deserialize the raw binary data from the incoming `WorldPacket` into accessible C++ fields.

This unit is part of the broader network layer abstraction that decodes client inputs before they are handed off to the game logic handlers. It does not contain any business logic, validation, or response generation; it is purely a data carrier and deserializer for the "request trainer list" action.

## Member-by-Member Behavior

The unit consists of a single class, `TrainerList`, which inherits from `ClientPacket`.

### `TrainerList` Class Structure

*   **Inheritance**: Inherits from `ClientPacket`, indicating this is a message originating from the client.
*   **Public Data Member**:
    *   `guid`: An `ObjectGuid` representing the unique identifier of the NPC trainer the player is interacting with. This allows the server to identify which specific entity the player is requesting training services from.
*   **Constructor**:
    *   `explicit TrainerList()`: Initializes the base `ClientPacket` with the opcode `CMSG_TRAINER_LIST`. This associates the class with the specific network message type.
*   **Deserialization Method**:
    *   `ReadFromWorldPacket(WorldPacket& recv_data)`: This virtual method overrides the base class implementation to parse the incoming binary stream. It extracts the `guid` field from the packet data. The exact extraction logic is not visible in the header but is implied by the presence of the `guid` member and the standard pattern of such packet classes.

## Cross-Unit Boundaries

*   **Calls Out**: None. The `TrainerList` class itself does not call any other units during construction or definition. The `ReadFromWorldPacket` method will internally use parsing utilities provided by the `WorldPacket` class (from the `Packet.h` unit, though not explicitly listed in the map's "Calls out" for this specific member, it is a dependency of the signature).
*   **Called By**: None listed in the map. In practice, this class is instantiated by the network handler when a `CMSG_TRAINER_LIST` opcode is received. The handler will then pass the populated `TrainerList` object to the appropriate game-world handler (likely in a session or player handler unit) to process the request.

## Data Model

This unit does not interact directly with any database tables. It operates entirely on in-memory network packet data. The `guid` field corresponds to an in-game entity identifier, which may later be used to look up database records for the NPC or player, but `TrainerList` itself performs no SQL operations.

## Notable Implementation Details

*   **Minimalist Design**: The class contains no private members, no validation logic, and no complex state. It follows a strict data-transfer-object (DTO) pattern common in network layers.
*   **Opcode Association**: The constructor explicitly binds the class to `CMSG_TRAINER_LIST`. This ensures type safety and correct routing within the network dispatcher.
*   **Guid Usage**: The use of `ObjectGuid` instead of a simple integer ID suggests that the system supports high-resolution entity identification, potentially including realm, type, and counter information, which is critical for distinguishing between entities in large-scale multiplayer environments.

## Member Reference

**TrainerList**
Constructor for the `TrainerList` packet class. Initializes the base `ClientPacket` with the `CMSG_TRAINER_LIST` opcode. It sets up the object to receive and parse data related to a player requesting the list of spells available from an NPC trainer. The class exposes a public `guid` member of type `ObjectGuid` to store the identifier of the target NPC. It also declares the `ReadFromWorldPacket` method, which is responsible for extracting the `guid` from the incoming binary packet data. This unit contains no other members or logic.

---

<!-- machine-true, projected from graph.json -->

## Map — TrainerList

*Source:* Npc.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| TrainerList | ctor | — | — | — |
