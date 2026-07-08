# RequestPartyMemberStats

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# RequestPartyMemberStats

## Purpose & Responsibilities

`RequestPartyMemberStats` is a client-side packet structure within the `WorldPackets::Group` namespace, defined in `Group.h`. Its sole responsibility is to represent the `CMSG_REQUEST_PARTY_MEMBER_STATS` message sent from the game client to the server. This packet carries a single piece of data: the `ObjectGuid` of a player character whose party statistics the client is requesting. It acts as a data container for deserialization; it does not contain logic for handling the request, nor does it interact with databases or other subsystems directly.

## Member-by-Member Behavior

The unit consists of a single constructor and relies on inherited functionality for packet parsing.

*   **Constructor (`RequestPartyMemberStats`)**: Initializes the base `ClientPacket` class with the specific opcode `CMSG_REQUEST_PARTY_MEMBER_STATS`. This registration allows the network layer to identify incoming packets of this type and route them to the appropriate handler. The constructor takes no arguments.

*   **Data Member (`guid`)**: A public `ObjectGuid` field that stores the unique identifier of the target player. This value is populated by the inherited `ReadFromWorldPacket` method (defined in `ClientPacket` or a common base, though the declaration is in `Group.h` via inheritance). The client sends this GUID to specify which party member's stats are being requested.

## Cross-Unit Boundaries

*   **Calls Out**: None. The `RequestPartyMemberStats` class itself does not call any other units. It is a passive data structure.
*   **Called By**: None listed in the map. However, in the broader system, instances of this class are typically created by the network receive loop when a packet with opcode `CMSG_REQUEST_PARTY_MEMBER_STATS` arrives. The network layer will instantiate this object and call its `ReadFromWorldPacket` method to populate the `guid` field. Subsequently, a handler (likely in a different unit, such as a session or group manager) will read the `guid` member to process the request.

## Data Model

This unit does not interact with any database tables. It operates entirely in memory as part of the network packet processing pipeline.

## Notable Implementation Details

*   **Inheritance**: The class inherits from `ClientPacket`, which provides the mechanism for reading raw binary data from the network stream into the `guid` field. The `ReadFromWorldPacket` method is declared in the base class but overridden or implemented to handle the specific layout of this packet.
*   **Public Data**: The `guid` member is public, allowing direct access by handlers after deserialization. This is a common pattern in this codebase for packet structures, prioritizing simplicity over encapsulation for transient data objects.
*   **Opcode Specificity**: The constructor explicitly binds this class to `CMSG_REQUEST_PARTY_MEMBER_STATS`. Any change in the client protocol regarding this opcode would require updating this constant and potentially the deserialization logic in the base class.

## Member Reference

**RequestPartyMemberStats**
Constructor that initializes the packet with the `CMSG_REQUEST_PARTY_MEMBER_STATS` opcode. It prepares the object to receive and store the `ObjectGuid` of the party member whose stats are being requested.

---

<!-- machine-true, projected from graph.json -->

## Map — RequestPartyMemberStats

*Source:* Group.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| RequestPartyMemberStats | ctor | — | — | — |
