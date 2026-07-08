# ChatIgnored

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ChatIgnored

## Purpose & Responsibilities

`ChatIgnored` is a client-side packet structure within the `WorldPackets::Misc` namespace, defined in `Misc.h`. It represents the `CMSG_CHAT_IGNORED` message sent by the game client to the server. Its sole responsibility is to encapsulate the `ObjectGuid` of a player or entity whose chat messages the local player wishes to ignore. This packet serves as the data carrier for the "ignore" functionality in social interactions, allowing the server to validate the request and update the user's ignore list accordingly.

As a `ClientPacket`, it inherits the standard interface for incoming network messages, including the virtual `ReadFromWorldPacket` method, which is responsible for deserializing the binary data from the network stream into the `guid` member variable. The class itself contains no business logic; it is a pure data structure designed for serialization and deserialization.

## Member-by-Member Behavior

The unit consists of a single constructor and one public data member.

### Constructor: `ChatIgnored()`
The default constructor initializes the packet with the opcode `CMSG_CHAT_IGNORED`. It sets up the base `ClientPacket` infrastructure required for network handling. No additional initialization is performed on the `guid` member, leaving it in its default-constructed state (typically an invalid or zeroed GUID) until `ReadFromWorldPacket` is invoked.

### Data Member: `guid`
This `ObjectGuid` holds the unique identifier of the target entity to be ignored. It is populated exclusively via the `ReadFromWorldPacket` method during the deserialization process.

## Cross-Unit Boundaries

*   **Calls Out:** None. The `ChatIgnored` class does not invoke methods in other units.
*   **Called By:** While the MAP indicates no specific callers, in practice, this packet is instantiated and processed by the network handler layer (likely in `WorldSession` or a dedicated packet handler dispatcher) when the server receives the `CMSG_CHAT_IGNORED` opcode from the client. The handler will call `ReadFromWorldPacket` to populate the `guid`, then extract the `guid` to perform the ignore logic (e.g., adding the GUID to the player's ignore list).

## Data Model

This unit does not directly interact with any database tables. It operates purely on in-memory network data. The persistence of ignore lists is handled by higher-level service classes (not part of this unit) after the packet has been processed.

## Notable Implementation Details

*   **Minimalist Design:** Like all packets in this namespace, `ChatIgnored` follows a strict separation of concerns. It defines *what* the data is (a GUID) but not *how* it is used. This allows the same packet structure to be reused across different client versions or server modules without changing the core data definition.
*   **ObjectGuid Usage:** The use of `ObjectGuid` instead of a simple string name ensures that the ignore action is tied to a specific, unique entity instance, preventing ambiguity if multiple players share similar names or if names change.
*   **No Validation:** The class performs no validation on the `guid`. It is assumed that the server-side handler will verify whether the GUID is valid, belongs to a player, and whether the ignoring player is allowed to ignore that target.

## Member Reference

**ChatIgnored**  
Constructor for the `ChatIgnored` packet. Initializes the base `ClientPacket` with the opcode `CMSG_CHAT_IGNORED`. Does not initialize the `guid` member.

---

<!-- machine-true, projected from graph.json -->

## Map — ChatIgnored

*Source:* Misc.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ChatIgnored | ctor | — | — | — |
