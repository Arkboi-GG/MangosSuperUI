# QueryGameObject

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# QueryGameObject

## Purpose & Responsibilities

`QueryGameObject` is a client-side packet structure within the `WorldPackets::Query` namespace, defined in `Query.h`. Its responsibility is to represent the `CMSG_GAMEOBJECT_QUERY` message sent by the client to the server. This packet carries the necessary identifiers for the server to look up and return detailed information about a specific Game Object (GO) instance in the world, such as its name, type, and visual properties.

As a `ClientPacket`, it serves as the deserialization target for incoming network data. It holds two primary fields:
1.  `entryID`: The static definition ID of the game object (linking to the `gameobject_template` table).
2.  `guid`: The unique runtime identifier of the specific instance of that game object in the world.

This unit contains only the declaration of the class and its constructor. The actual deserialization logic (`ReadFromWorldPacket`) is implemented elsewhere (likely in a corresponding `.cpp` file not included in this partial, or potentially inline in a different context, though the MAP indicates only the constructor is part of this specific unit's scope for documentation purposes).

## Member-by-Member Behavior

### Construction
The **`QueryGameObject`** constructor initializes the packet with the opcode `CMSG_GAMEOBJECT_QUERY`. It sets default values for `entryID` (0) and `guid` (default constructed `ObjectGuid`). This ensures that if the packet is created but not properly filled from network data, it holds safe, zeroed-out states.

## Cross-Unit Boundaries

*   **Inheritance**: Inherits from `ClientPacket` (defined in `Packet.h`). This provides the base functionality for handling network opcodes and basic packet lifecycle management.
*   **Dependencies**: Uses `ObjectGuid` (defined in `ObjectGuid.h`) to represent the unique identifier of the game object instance.
*   **No Outgoing Calls**: The constructor performs no calls to other units.
*   **No Incoming Calls**: According to the MAP, no other units explicitly call this constructor directly in the documented scope. Typically, this packet is instantiated by the network layer when a `CMSG_GAMEOBJECT_QUERY` opcode is received, but that interaction is handled by the `ClientPacket` infrastructure or the network handler, not by direct calls to this constructor from other business logic units listed in the MAP.

## Data Model

This unit does not interact directly with database tables. It represents network data. However, the `entryID` field corresponds to the `entry` column in the `gameobject_template` table, and the `guid` corresponds to the `guid` column in the `gameobject` table. These tables are used by the server-side handlers (not this unit) to resolve the query.

## Notable Implementation Details

*   **Default Initialization**: Both `entryID` and `guid` are initialized in the class definition (`uint32 entryID = 0;` and `ObjectGuid guid;`). This is a modern C++ practice ensuring objects are always in a valid state upon construction, even if the initializer list is bypassed (though the constructor uses the base class initializer).
*   **Final Class**: The class is marked `final`, preventing further inheritance. This is appropriate for a leaf packet structure that should not be subclassed.
*   **Namespace Organization**: Located in `WorldPackets::Query`, clearly grouping all query-related client packets together.

## Member Reference

**QueryGameObject**
Constructor for the `QueryGameObject` packet. Initializes the base `ClientPacket` with the opcode `CMSG_GAMEOBJECT_QUERY`. Sets `entryID` to 0 and `guid` to a default-constructed `ObjectGuid`. No other units call this constructor directly according to the MAP.

---

<!-- machine-true, projected from graph.json -->

## Map — QueryGameObject

*Source:* Query.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| QueryGameObject | ctor | — | — | — |
