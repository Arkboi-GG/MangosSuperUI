# ResurrectResponse

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ResurrectResponse

## Purpose & Responsibilities

`ResurrectResponse` is a client-to-server packet structure within the `WorldPackets::Misc` namespace, responsible for conveying a player’s decision regarding a resurrection attempt. In the context of the game server architecture, this packet represents the moment a dead player chooses whether to accept or decline being brought back to life by another entity (typically another player or a game object).

The class encapsulates two critical pieces of information required by the server to process the request:
1.  **Identity of the Resurrector:** The `ObjectGuid` of the entity that initiated the resurrection offer.
2.  **Player Intent:** A boolean flag (`accept`) indicating whether the deceased player agrees to the resurrection.

This unit is purely a data container and protocol definition. It does not contain logic for processing the resurrection, validating permissions, or updating character states; those responsibilities lie in the server-side handlers that consume this packet. Its sole responsibility is to define the binary layout and provide the mechanism to deserialize incoming network data into these fields.

## Member-by-Member Behavior

### Construction
**`ResurrectResponse()`**
The constructor initializes the packet with the specific opcode `CMSG_RESURRECT_RESPONSE`, identifying it to the server's packet dispatcher. It also initializes the `accept` field to `false` by default. This default value ensures that if deserialization fails or is incomplete, the packet defaults to a "decline" state, which is generally the safer assumption for resurrection mechanics (preventing accidental resurrections due to corrupted data).

### Data Deserialization
While the MAP lists only the constructor, the class inherits `ReadFromWorldPacket` from `ClientPacket`. Although the implementation of `ReadFromWorldPacket` is not shown in the provided source snippet (it is likely defined in a corresponding `.cpp` file or generated), the presence of the `override` keyword confirms that this class implements the standard interface for reading raw bytes from a `WorldPacket` object into the public members `resurrectorGuid` and `accept`.

## Cross-Unit Boundaries

*   **Calls Out:** None. As a pure data structure with only a constructor listed in the MAP, it does not invoke other units during construction.
*   **Called By:** None listed in the MAP. However, in the broader system, this class is instantiated by the network layer when a packet with opcode `CMSG_RESURRECT_RESPONSE` is received. It is then passed to a handler function (likely in a `WorldSession` or similar session management unit) which reads the populated fields.

## Data Model

This unit does not interact directly with any database tables. It operates entirely in memory as part of the network communication layer. The `resurrectorGuid` may eventually be used to look up a player object in memory or a record in the `characters` table, but `ResurrectResponse` itself performs no SQL operations.

## Notable Implementation Details

1.  **Default Acceptance State:** The `accept` member is explicitly initialized to `false` in the constructor. This is a defensive programming choice. In many game protocols, missing or truncated boolean flags can lead to undefined behavior. By defaulting to "decline," the server avoids the risk of resurrecting a player against their will if the packet data is malformed.
2.  **Guid Dependency:** The correctness of the resurrection logic depends heavily on the validity of `resurrectorGuid`. If this GUID is invalid or refers to an offline/non-existent entity, the server-side handler must reject the request. The packet structure itself does not validate this; it merely transports the ID.
3.  **Namespace Context:** Located in `WorldPackets::Misc`, this class is grouped with other miscellaneous client commands that do not fit into more specific categories like combat, movement, or chat. This suggests it is a relatively standalone interaction flow.

## Member Reference

**ResurrectResponse**
Constructor for the resurrection response packet. Initializes the packet opcode to `CMSG_RESURRECT_RESPONSE` and sets the `accept` flag to `false`. It prepares the object to receive deserialized data for `resurrectorGuid` and `accept`.

---

<!-- machine-true, projected from graph.json -->

## Map — ResurrectResponse

*Source:* Misc.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ResurrectResponse | ctor | — | — | — |
