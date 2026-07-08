# PetitionRename

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# PetitionRename

**Purpose & Responsibilities**

`PetitionRename` is a client-side packet structure within the `WorldPackets::Petition` namespace. Its sole responsibility is to represent the incoming network message `MSG_PETITION_RENAME` sent by a client when a player attempts to rename a guild or faction via a petition item. It acts as a data carrier, holding the identifier of the petition item (`itemGuid`) and the desired new name (`newName`) extracted from the raw binary stream.

This unit contains only the constructor declaration and the class definition itself. The actual logic for parsing the packet data (`ReadFromWorldPacket`) is implemented elsewhere (likely in a corresponding `.cpp` file not included in this specific partial, or potentially inline in a different compilation unit), but the *declaration* of the member function exists here. However, per the MAP provided, only the constructor `PetitionRename` is listed as a member of this specific documentation scope. The MAP indicates no outgoing calls, incoming calls from other units, or database table interactions for this specific member.

**Member-by-Member Behavior**

The unit defines a single member relevant to this documentation scope:

*   **`PetitionRename`**: This is the default constructor for the `PetitionRename` packet class. It initializes the base class `ClientPacket` with the opcode `MSG_PETITION_RENAME`. This registration ensures that when the server receives a packet with this opcode, it can correctly instantiate this specific struct to handle the data. The constructor takes no arguments and performs no complex initialization beyond setting the packet type.

**Cross-Unit Boundaries**

*   **Calls Out**: None. The constructor does not invoke any functions in other units.
*   **Called By**: None. According to the MAP, no other units explicitly call this constructor directly in a way that constitutes a cross-unit dependency for documentation purposes (though internally, the packet dispatch system will invoke it).

**Data Model**

This unit does not interact with any database tables. It is purely a network protocol buffer structure.

**Notable Implementation Details**

*   **Inheritance**: `PetitionRename` inherits from `ClientPacket`, indicating it is a message originating from the client.
*   **Opcode Association**: The constructor binds this class to `MSG_PETITION_RENAME`. Any deviation in the opcode value would result in this packet not being recognized by the server's packet handler.
*   **Data Fields**: While not part of the constructor's logic, the class holds two critical fields: `ObjectGuid itemGuid` (identifying the physical petition item in the player's inventory) and `std::string newName` (the proposed name for the guild/faction). These fields are populated by the `ReadFromWorldPacket` method (declared in this header but not detailed in the MAP's member list for this specific partial).

## Member Reference

**PetitionRename**
Default constructor for the `PetitionRename` packet. Initializes the base `ClientPacket` with the opcode `MSG_PETITION_RENAME`. No additional logic or side effects occur during construction.

---

<!-- machine-true, projected from graph.json -->

## Map — PetitionRename

*Source:* Petition.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| PetitionRename | ctor | — | — | — |
