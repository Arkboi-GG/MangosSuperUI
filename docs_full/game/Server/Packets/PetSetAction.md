# PetSetAction

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# PetSetAction

**PetSetAction** is a client-to-server network packet structure within the `WorldPackets::Pet` namespace, responsible for deserializing the `CMSG_PET_SET_ACTION` message sent by the game client. Its sole responsibility is to parse binary data from a `WorldPacket` into structured fields representing changes to a pet’s action bar configuration. It contains no business logic, validation, or side effects; it is purely a data container and deserializer.

This unit is part of the broader packet handling infrastructure in Mangos, where each packet type inherits from `ClientPacket` and implements `ReadFromWorldPacket` to populate its public members. The parsed data is then consumed by higher-level game logic (not shown in this unit) to update the pet’s active abilities or spells on its action bars.

## Purpose & Responsibilities

The **PetSetAction** class exists to:
1. Represent the wire format of the `CMSG_PET_SET_ACTION` opcode.
2. Provide a constructor that initializes the packet with the correct opcode.
3. Implement `ReadFromWorldPacket` to extract the pet’s GUID, the number of action entries being updated, and up to two `ActionEntry` structs containing position and data fields.

It does not validate the data, check permissions, or interact with the database. It does not call any other units, nor is it called by any other units according to the provided MAP. It is a leaf node in the call graph for this specific packet type.

## Member-by-Member Behavior

### **PetSetAction** (Constructor)
The default constructor initializes the base `ClientPacket` with the opcode `CMSG_PET_SET_ACTION`. It does not initialize the public data members (`petGuid`, `count`, `actions`), relying instead on their in-class initializers (`= 0` for `count` and `position`/`data` in `ActionEntry`) or zero-initialization via aggregate initialization if applicable. The constructor is marked `explicit` to prevent implicit conversions.

### **ReadFromWorldPacket** (Implied by MAP absence but present in header)
*Note: The MAP lists only the constructor as a member. However, the header declares `void ReadFromWorldPacket(WorldPacket& recv_data) override;`. Since the MAP does not list this method, and the instructions state "Only this unit's own Members (the MAP's left column) are this unit's behavior," I will strictly follow the MAP. The MAP only lists the constructor. Therefore, I will not document `ReadFromWorldPacket` in the Member Reference section, though its existence is evident in the source.*

Wait, the MAP says:
| Member | Kind | ... |
|---|---|---|
| PetSetAction | ctor | ... |

It does *not* list `ReadFromWorldPacket`. This is unusual because `ReadFromWorldPacket` is a virtual function declared in the class. However, the prompt states: "Only this unit's own Members (the MAP's left column) are this unit's behavior." and "do not list any member that is not in the MAP."

Therefore, I will only document the constructor in the final reference section. I will acknowledge the class structure in the narrative but restrict the formal reference to the MAP contents.

## Cross-Unit Boundaries

According to the provided MAP, **PetSetAction** has no outgoing calls to other units and is not called by any other units. It is an isolated packet definition. In practice, instances of this class are created by the network layer, populated via `ReadFromWorldPacket` (which is not listed in the MAP), and then passed to a handler function (also not listed in the MAP). The MAP reflects only the constructor as a documented boundary point, implying no direct dependencies are tracked for this specific unit in the current analysis scope.

## Data Model

This unit does not interact with any database tables. It operates entirely on in-memory binary packet data.

## Notable Implementation Details

1. **Fixed Array Size**: The `actions` member is a fixed-size array of 2 `ActionEntry` structs (`ActionEntry actions[2]`). This suggests the client sends updates for up to two action slots at a time in this specific packet variant, or that the server expects batches of size 2. The `count` field indicates how many of these entries are valid.
2. **ActionEntry Structure**: The nested `struct ActionEntry` contains `position` (likely the index on the action bar) and `data` (likely the spell ID or item ID to place at that position). Both are initialized to 0.
3. **No Validation**: The class provides no bounds checking on `count` relative to the array size (2). If `count` exceeds 2, subsequent logic using this packet would likely cause buffer overflows unless validated elsewhere.
4. **Inheritance**: Inherits from `ClientPacket`, which handles the base packet metadata (like opcode registration).

## Member Reference

**PetSetAction**  
Constructor. Initializes the `ClientPacket` base class with the opcode `CMSG_PET_SET_ACTION`. Does not initialize data members, relying on in-class initializers.

---

<!-- machine-true, projected from graph.json -->

## Map — PetSetAction

*Source:* Pet.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| PetSetAction | ctor | — | — | — |
