# Duel

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Duel Packet Handlers

The `Duel` unit defines two client-to-server packet structures within the `WorldPackets::Duel` namespace: `DuelAccepted` and `DuelCancelled`. These classes represent incoming network messages from clients indicating that a player has accepted a duel invitation or cancelled an ongoing duel. Both inherit from `ClientPacket`, marking them as part of the server's inbound message processing pipeline.

This unit contains no database interactions, no outbound calls to other subsystems beyond basic data extraction, and no complex logic. Its sole responsibility is to deserialize the `playerGuid` field from the raw binary packet data into a structured `ObjectGuid` object.

## Member Behavior

### Packet Deserialization

Both `DuelAccepted` and `DuelCancelled` implement the `ReadFromWorldPacket` method, which is invoked by the network layer after a packet of the corresponding opcode (`CMSG_DUEL_ACCEPTED` or `CMSG_DUEL_CANCELLED`) is received. The implementation is identical for both classes:

- **`DuelAccepted::ReadFromWorldPacket`**: Extracts a single `ObjectGuid` from the packet stream and stores it in the `playerGuid` member.
- **`DuelCancelled::ReadFromWorldPacket`**: Performs the same extraction for the cancellation packet.

The `ObjectGuid` type is extracted using the overloaded `operator>>` defined in the `ObjectGuid` unit. This operator handles the binary layout of the GUID (typically 8 bytes in classic WoW protocols) and converts it into a usable identifier for the player involved in the duel action.

### Construction

Both classes provide explicit constructors that initialize the base `ClientPacket` with the appropriate opcode constant:
- `DuelAccepted` uses `CMSG_DUEL_ACCEPTED`.
- `DuelCancelled` uses `CMSG_DUEL_CANCELLED`.

These constructors are marked `explicit` to prevent implicit conversions, ensuring that instances are only created intentionally during packet parsing or testing scenarios.

## Cross-Unit Boundaries

### Outbound Calls

- **`ObjectGuid/operator>>`**: Both `ReadFromWorldPacket` implementations call this operator to parse the player's unique identifier from the packet buffer. This is the only dependency on other units. The `ObjectGuid` unit provides the binary deserialization logic, while this unit simply invokes it.

### Inbound Calls

The MAP indicates no other units explicitly call these methods. In practice, these methods are called by the central packet dispatching system (not shown in this unit) when a matching opcode is detected on the wire. The caller passes a `WorldPacket` reference containing the raw data, and expects the `playerGuid` member to be populated upon return.

## Data Model

This unit does not interact with any database tables. It operates entirely on transient network data.

## Notable Implementation Details

- **Minimalism**: The logic is trivial—each method consists of a single line extracting one field. There is no validation, error handling, or additional data parsing. This suggests that the protocol for these specific packets is simple and well-defined, requiring only the target player's GUID.
- **Symmetry**: The structure and implementation of `DuelAccepted` and `DuelCancelled` are nearly identical, differing only in the opcode used during construction. This symmetry reduces maintenance overhead but also means that any future changes to the packet format (e.g., adding a timestamp or duel ID) would need to be applied consistently to both classes.
- **No State Beyond GUID**: Neither packet carries additional context such as the challenger's GUID, duel type, or arena information. This implies that such context is either maintained server-side via session state or is irrelevant for the initial acceptance/cancellation signal.

## Member Reference

**ReadFromWorldPacket** (in `DuelAccepted`): Deserializes the `playerGuid` from the incoming `WorldPacket` by calling `ObjectGuid::operator>>`. Populates the `playerGuid` member with the GUID of the player accepting the duel.

**ReadFromWorldPacket#2** (in `DuelCancelled`): Identical to the above, but for the `DuelCancelled` packet. Extracts the `playerGuid` of the player cancelling the duel.

**DuelAccepted** (ctor): Constructs a `DuelAccepted` packet instance, initializing the base `ClientPacket` with the opcode `CMSG_DUEL_ACCEPTED`. No additional initialization is performed.

---

<!-- machine-true, projected from graph.json -->

## Map — Duel

*Source:* Duel.cpp, Duel.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ReadFromWorldPacket | method | ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket#2 | method | ObjectGuid/operator>> | — | — |
| DuelAccepted | ctor | — | — | — |
