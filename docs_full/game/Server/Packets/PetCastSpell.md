# PetCastSpell

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# PetCastSpell

**PetCastSpell** is a client-side network packet class within the `WorldPackets::Pet` namespace, responsible for deserializing the `CMSG_PET_CAST_SPELL` message sent by the game client. This packet represents a player's instruction for their summoned pet to cast a specific spell on a designated target. It is conditionally compiled only for client builds newer than 1.8.4 (`CLIENT_BUILD_1_8_4`), indicating that this specific opcode and packet structure were introduced or standardized in later versions of the World of Warcraft client protocol.

The class inherits from `ClientPacket`, marking it as an incoming message from the client to the server. Its primary responsibility is to parse the binary data received over the network into structured fields: the GUID of the pet performing the action, the ID of the spell being cast, and the complex targeting information required for the spell resolution.

## Member-by-Member Behavior

### **PetCastSpell** (Constructor)
The constructor initializes the packet object with the specific opcode `CMSG_PET_CAST_SPELL`. It sets default values for the member variables:
- `spellId` is initialized to `0`.
- `targets` is default-initialized via `SpellCastTargets`.
- The base class `ClientPacket` is invoked with the opcode, ensuring the packet is correctly identified during the server's dispatch mechanism.

No database tables are accessed by this unit. The class operates purely on in-memory data structures derived from the network stream.

## Cross-Unit Boundaries

This unit has no outgoing calls to other units in the provided map. However, it is part of a larger system where:
- **Called By**: While the map shows no explicit callers, in the broader context of the Mangos server architecture, this packet is instantiated and populated by the network layer when a `CMSG_PET_CAST_SPELL` message arrives. The parsed data is then passed to the game logic handlers (likely in `PetHandler.cpp` or similar) to execute the spell casting logic.
- **Dependencies**: It relies on `ObjectGuid` for identifying the pet and `SpellCastTargets` for parsing target information. These are core infrastructure classes.

## Data Model

This unit does not interact with any database tables. It processes transient network data.

## Notable Implementation Details

1. **Conditional Compilation**: The entire class definition is wrapped in `#if SUPPORTED_CLIENT_BUILD > CLIENT_BUILD_1_8_4`. This means the packet structure is ignored for older clients, preventing compilation errors or runtime mismatches for legacy protocol versions.
2. **Target Parsing**: The `targets` field uses `SpellCastTargets`, a specialized structure capable of handling various target types (unit, location, item, etc.). The actual parsing logic resides in the `ReadFromWorldPacket` method (not shown in the source but declared), which interprets the binary stream according to the client version's specific encoding rules for spell targets.
3. **Default Initialization**: `spellId` defaults to `0`. In the context of spell casting, a spell ID of `0` is typically invalid, serving as a safe initial state before the packet is fully read.

## Member Reference

**PetCastSpell**  
Constructor that initializes the packet with the `CMSG_PET_CAST_SPELL` opcode and sets default values for `spellId` (0) and `targets`. Conditionally compiled for client builds > 1.8.4.

---

<!-- machine-true, projected from graph.json -->

## Map — PetCastSpell

*Source:* Pet.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| PetCastSpell | ctor | — | — | — |
