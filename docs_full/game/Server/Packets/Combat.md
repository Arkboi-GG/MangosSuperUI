# Combat

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Combat Packet Handlers

The `Combat` unit defines two client-to-server packet structures within the `WorldPackets::Combat` namespace: `AttackSwing` and `SetSheathed`. These classes serve as data containers and deserialization handlers for specific combat-related actions initiated by a player client. They inherit from `ClientPacket`, indicating they represent incoming network traffic from a connected client. The unit does not contain game logic, validation, or server-side response generation; it strictly handles the extraction of raw data fields from the binary network stream into structured C++ objects.

## Member-by-Member Behavior

### AttackSwing
The `AttackSwing` class represents the `CMSG_ATTACKSWING` packet. Its primary responsibility is to capture the target of a melee attack attempt.
- **Data Storage**: It holds a single public member, `targetGuid` (of type `ObjectGuid`), which identifies the entity the client intends to attack.
- **Deserialization**: The `ReadFromWorldPacket` method extracts the target GUID from the incoming `WorldPacket` buffer. It relies on the overloaded extraction operator (`operator>>`) defined in the `ObjectGuid` unit to parse the binary representation of the GUID into the `targetGuid` field.

### SetSheathed
The `SetSheathed` class represents the `CMSG_SETSHEATHED` packet. It handles the client's request to change the weapon sheath state (e.g., drawing or holstering weapons).
- **Data Storage**: It holds a single public member, `sheathed` (of type `uint32`), initialized to `0`. This value typically indicates the sheath state (e.g., 0 for unsheathed, 1 for melee, 2 for ranged, etc., though the specific enum mapping is outside this unit's scope).
- **Deserialization**: The `ReadFromWorldPacket` method extracts the `sheathed` state from the incoming `WorldPacket` buffer using the standard integer extraction operator provided by `ByteBuffer` (via `WorldPacket` inheritance).

## Cross-Unit Boundaries

- **ObjectGuid**: The `AttackSwing::ReadFromWorldPacket` method calls `ObjectGuid::operator>>`. This dependency is necessary to deserialize the complex GUID structure from the network byte stream. The `ObjectGuid` unit provides the parsing logic, while `Combat` merely invokes it.
- **ByteBuffer**: The `SetSheathed::ReadFromWorldPacket` method implicitly relies on `ByteBuffer::operator>>` (accessed via `WorldPacket`) to extract the `uint32` sheath state. This is a standard utility for reading primitive types from the packet buffer.

## Data Model

This unit does not interact with any database tables. It operates entirely on in-memory network packet data.

## Notable Implementation Details

- **Minimal Logic**: Both `ReadFromWorldPacket` implementations are trivial, consisting of a single extraction operation. There is no validation of the extracted data (e.g., checking if the target GUID is valid or if the sheath state is within expected bounds) within this unit. Such validation is presumably handled by the caller after the packet is fully constructed.
- **Public Data Members**: Both `targetGuid` and `sheathed` are declared as `public` members. This design choice allows direct access to the parsed data by the calling code without requiring getter methods, simplifying the interface but exposing the internal state directly.
- **Final Classes**: Both classes are marked `final`, preventing further inheritance. This enforces that these packet structures are leaf nodes in the packet hierarchy, suitable for their role as simple data carriers.

## Member Reference

**AttackSwing**
Constructor for the `CMSG_ATTACKSWING` packet. Initializes the base `ClientPacket` with the correct opcode. Does not perform any data extraction itself.

**ReadFromWorldPacket**
Method of `AttackSwing`. Extracts the `targetGuid` from the `WorldPacket` buffer by calling `ObjectGuid::operator>>`. This populates the `targetGuid` member with the identifier of the attack target.

**ReadFromWorldPacket#2**
Method of `SetSheathed`. Extracts the `sheathed` state (a `uint32`) from the `WorldPacket` buffer using `ByteBuffer::operator>>`. This populates the `sheathed` member with the requested weapon sheath state.

---

<!-- machine-true, projected from graph.json -->

## Map — Combat

*Source:* Combat.cpp, Combat.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ReadFromWorldPacket | method | ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket#2 | method | ByteBuffer/operator>>#9 | — | — |
| AttackSwing | ctor | — | — | — |
