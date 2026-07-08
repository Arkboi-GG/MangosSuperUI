# PetSpellAutocast

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# PetSpellAutocast

**PetSpellAutocast** is a client-to-server packet class within the `WorldPackets::Pet` namespace, responsible for deserializing the `CMSG_PET_SPELL_AUTOCAST` message sent by the World of Warcraft client. This packet informs the server whether a specific spell on a pet should be enabled or disabled for automatic casting. It is conditionally compiled only for client builds newer than 1.6.1 (`CLIENT_BUILD_1_6_1`), indicating that older clients handled pet spell autocasting through different mechanisms or packet structures.

The class inherits from `ClientPacket`, establishing it as part of the network layer’s inbound message handling infrastructure. Its primary responsibility is to extract three fields from the raw binary data received over the socket: the GUID of the pet, the ID of the spell in question, and a boolean-like state indicating whether autocast is turned on or off.

## Member-by-Member Behavior

### **PetSpellAutocast** (Constructor)
The default constructor initializes the packet object with the opcode `CMSG_PET_SPELL_AUTOCAST`. It sets the `spellId` member to `0` and the `state` member to `0` via in-class member initializers. The `guid` member is default-initialized by the `ObjectGuid` class (typically to an invalid/empty GUID). No dynamic memory allocation occurs, and no external units are called during construction. This ensures the object is in a valid, zeroed-out state before deserialization begins.

## Cross-Unit Boundaries

This unit has no outgoing calls to other units in its constructor. However, it is designed to be consumed by higher-level game logic handlers (not shown in this MAP but implied by the `ClientPacket` inheritance hierarchy). Typically, after `ReadFromWorldPacket` is invoked by the network dispatcher, the populated `PetSpellAutocast` instance will be passed to a handler function—likely within a `PetHandler` or similar module—that interprets the `guid`, `spellId`, and `state` to update the pet’s internal spell configuration in the game world. The `ObjectGuid` type comes from the `ObjectGuid.h` unit, and `ClientPacket` is defined in `Packet.h`, both of which provide foundational networking and identity abstractions.

## Data Model

This unit does not interact directly with any database tables. It operates solely on in-memory packet data received from the client. Any persistence of pet spell autocast states would occur downstream in other units that process this packet, potentially updating tables such as `character_pet` or custom pet configuration tables, but such interactions are outside the scope of this class.

## Notable Implementation Details

- **Conditional Compilation**: The entire class is wrapped in `#if SUPPORTED_CLIENT_BUILD > CLIENT_BUILD_1_6_1`. This means the packet structure is ignored for clients version 1.6.1 and earlier. Maintainers must ensure that legacy client support either omits this functionality or uses a different packet type for autocast control.
  
- **State Encoding**: The `state` field is a `uint8`, likely representing a boolean (`0` = off, `1` = on). The code does not enforce this constraint at the packet level; validation, if any, must occur in the consuming handler.

- **Default Initialization**: Both `spellId` and `state` are explicitly initialized to `0` in the class definition. This prevents undefined behavior if `ReadFromWorldPacket` fails or is not called, though proper usage requires successful deserialization.

- **No Validation in Packet Layer**: Like most `ClientPacket` subclasses, this class performs only raw byte extraction. It does not validate whether the `guid` refers to a valid pet owned by the player, nor whether the `spellId` is learnable by that pet. Such checks are deferred to the game logic layer.

## Member Reference

**PetSpellAutocast** — Default constructor that initializes the packet with opcode `CMSG_PET_SPELL_AUTOCAST`, setting `spellId` and `state` to `0` via in-class initializers. Does not call any external units.

---

<!-- machine-true, projected from graph.json -->

## Map — PetSpellAutocast

*Source:* Pet.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| PetSpellAutocast | ctor | — | — | — |
