# TabardVendorActivate

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`TabardVendorActivate` is a lightweight data structure within the `WorldPackets::Npc` namespace, representing a specific client-to-server network message. Its sole responsibility is to encapsulate the payload for the `MSG_TABARDVENDOR_ACTIVATE` opcode. This packet is sent by the game client when a player interacts with an NPC designated as a tabard vendor, requesting the server to open the tabard customization interface.

As a `ClientPacket`, it inherits the base infrastructure required for deserialization from raw network bytes (`ReadFromWorldPacket`) and identification via its opcode. It contains no business logic, state management, or database interactions; it is purely a transport container for the GUID of the NPC being activated.

## Member-by-Member Behavior

The unit consists of a single member: the constructor.

### **TabardVendorActivate**
This is the default constructor for the `TabardVendorActivate` class. It performs two actions:
1.  Initializes the `guid` member variable. Note that while the declaration shows `ObjectGuid guid;` without an explicit initializer in the class body, `ObjectGuid` typically defaults to an invalid/empty state upon construction unless otherwise specified by its own default constructor.
2.  Calls the base class constructor `ClientPacket` with the constant `MSG_TABARDVENDOR_ACTIVATE`. This registers the packet type with the networking layer, ensuring that incoming packets with this opcode are routed to instances of this class for processing.

The constructor is marked `explicit` to prevent implicit conversions from other types, enforcing strict type safety during packet creation.

## Cross-Unit Boundaries

*   **Calls Out:** None. The constructor does not invoke any functions in other units.
*   **Called By:** None listed in the map. In practice, this constructor is likely invoked by the network handler or packet factory when a raw `MSG_TABARDVENDOR_ACTIVATE` packet is received from the client, but these callers reside outside the scope of this specific unit's map.

## Data Model

This unit does not interact with any database tables. It operates entirely in memory as part of the network I/O subsystem.

## Notable Implementation Details

*   **Opcode Specificity:** The class is tightly coupled to the `MSG_TABARDVENDOR_ACTIVATE` opcode. Any change in the client protocol regarding how tabard vendors are activated would require updating this constant and potentially the deserialization logic in `ReadFromWorldPacket` (which is declared here but implemented elsewhere, likely in a corresponding `.cpp` file not included in this partial).
*   **Minimal State:** The class holds only one piece of data: `guid`. This reflects the simplicity of the activation request—the server only needs to know *which* NPC was clicked to validate the interaction and send back the appropriate tabard options.
*   **Inheritance:** It inherits from `ClientPacket`, implying it shares common functionality for reading/writing binary data with other client-bound packets. The `ReadFromWorldPacket` method is overridden, suggesting custom deserialization logic is applied, though the implementation is not visible in this header.

## Member Reference

**TabardVendorActivate**
Default constructor for the `TabardVendorActivate` packet. Initializes the base `ClientPacket` with the `MSG_TABARDVENDOR_ACTIVATE` opcode. It prepares the object to receive and store the `guid` of the tabard vendor NPC from the incoming network stream.

---

<!-- machine-true, projected from graph.json -->

## Map — TabardVendorActivate

*Source:* Npc.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| TabardVendorActivate | ctor | — | — | — |
