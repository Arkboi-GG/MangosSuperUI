# UnstablePet

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# UnstablePet

## Purpose & Responsibilities

`UnstablePet` is a client-side network packet structure within the `WorldPackets::Npc` namespace, defined in `Npc.h`. Its sole responsibility is to represent the `CMSG_UNSTABLE_PET` message sent from the game client to the server. This message indicates that a player has initiated an action to "unstable" (release/remove from stable) a specific pet.

The class encapsulates the raw data required for this operation:
1.  **`npcGuid`**: The unique identifier of the NPC (typically a stable master) interacting with whom the action was triggered.
2.  **`petNumber`**: An integer index identifying which specific pet slot in the player's stable is being targeted for removal.

As a `ClientPacket`, it inherits the standard serialization and deserialization mechanisms for incoming network traffic but contains no custom logic beyond defining the data layout and the constructor. It does not perform validation, business logic, or database operations itself; those responsibilities lie with the server-side handler that processes this packet after it is received.

## Member-by-Member Behavior

### Construction and Initialization

**`UnstablePet()`**
This is the default constructor for the `UnstablePet` class. It performs two critical initialization steps:
1.  It invokes the base class constructor `ClientPacket(CMSG_UNSTABLE_PET)`, registering this instance as a handler for the specific opcode `CMSG_UNSTABLE_PET`. This allows the network layer to correctly route incoming packets with this opcode to instances of this class.
2.  It initializes the member variable `petNumber` to `0` via in-class initialization (`uint32 petNumber = 0;`). While `npcGuid` is left in its default-constructed state (likely an invalid or empty GUID until populated by deserialization), `petNumber` starts with a known safe value.

### Data Deserialization

**`ReadFromWorldPacket(WorldPacket& recv_data)`**
Although declared in the header, the implementation of `ReadFromWorldPacket` is not provided in the source snippet. However, based on the class structure and inheritance from `ClientPacket`, this virtual function is responsible for extracting the `npcGuid` and `petNumber` fields from the raw binary `WorldPacket` buffer provided by the network layer. It populates the public member variables so that downstream handlers can access the NPC target and the specific pet slot index.

## Cross-Unit Boundaries

`UnstablePet` acts as a passive data carrier at the network boundary.

*   **Called By:** External network handling units (not listed in the MAP but implied by the `ClientPacket` inheritance) will instantiate `UnstablePet` when a packet with opcode `CMSG_UNSTABLE_PET` arrives. They will then call `ReadFromWorldPacket` to populate the object.
*   **Calls Out:** The unit itself makes no outgoing calls. It relies entirely on the base class `ClientPacket` (defined in `Packet.h`) for its lifecycle management and opcode registration.

## Data Model

This unit does not interact directly with any database tables. It operates purely on in-memory network packet data. Any persistence related to pet stabling (such as updating the `character_pet` table or similar structures) occurs in the server-side logic that consumes this packet, not within the `UnstablePet` class itself.

## Notable Implementation Details

1.  **Default Value for `petNumber`**: The member `petNumber` is initialized to `0` in the class definition. This suggests that `0` might be a valid index for the first pet slot, or it serves as a fallback if deserialization fails or if the client sends a malformed packet. Handlers consuming this packet should verify if `petNumber` is within the valid range of the player's stable slots.
2.  **Public Members**: Both `npcGuid` and `petNumber` are public. This design choice simplifies access for the handler code, avoiding the need for getter methods. However, it also means the integrity of these values depends entirely on the correctness of the `ReadFromWorldPacket` implementation and the trustworthiness of the client data.
3.  **Final Class**: The class is marked `final`, indicating it cannot be subclassed. This enforces a strict, fixed structure for this specific packet type, preventing accidental extension or modification of its behavior through inheritance.

## Member Reference

**UnstablePet**
The default constructor for the `UnstablePet` class. It initializes the base `ClientPacket` with the opcode `CMSG_UNSTABLE_PET` and sets the `petNumber` member to `0`. This prepares the object to receive and deserialize incoming network data for the unstable pet action.

---

<!-- machine-true, projected from graph.json -->

## Map — UnstablePet

*Source:* Npc.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| UnstablePet | ctor | — | — | — |
