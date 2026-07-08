# Pet

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Pet Packet Handlers (`WorldPackets::Pet`)

**Purpose & Responsibilities**
The `Pet` namespace within `WorldPackets` defines a collection of client-side packet structures used to handle player interactions with pets in the World of Warcraft emulation environment. Specifically, this unit implements the deserialization logic (`ReadFromWorldPacket`) for various `CMSG` (Client Message) packets related to pet management. These include querying pet information, issuing actions, renaming, abandoning, managing spells (autocast, unlearning, canceling auras), and setting action bars. The classes inherit from `ClientPacket`, indicating they represent data received from the game client. This unit contains no server-side logic, database queries, or state management; it strictly parses binary network data into structured C++ objects for consumption by higher-level game logic handlers.

**Member-by-Member Behavior**
The unit consists of multiple distinct classes, each corresponding to a specific packet type. Each class exposes public data members populated during deserialization and a `ReadFromWorldPacket` method that extracts fields from the raw `WorldPacket` buffer.

*   **QueryPetName**: Parses a request to query a pet's name. It extracts a `petNumber` (uint32) and a `petGuid` (ObjectGuid).
*   **PetAction**: Parses a command for a pet to perform an action. It extracts the `petGuid`, action `data` (uint32), and the `targetGuid` of the action.
*   **PetAbandon**: Parses a request to abandon a pet. It extracts the `guid` of the pet to be abandoned.
*   **PetRename**: Parses a request to rename a pet. It extracts the `petGuid` and the new `name` (std::string).
*   **PetCancelAura**: Parses a request to remove a specific spell effect from a pet. It extracts the `guid` of the pet and the `spellId` to cancel.
*   **PetStopAttack**: (Conditional: Client Build > 1.6.1) Parses a command to stop a pet's current attack. It extracts the `petGuid`.
*   **PetUnlearn**: (Conditional: Client Build > 1.6.1) Parses a request to unlearn a spell from a pet. It extracts the `guid` of the pet. Note: The packet structure here appears minimal compared to typical unlearn requests which might include a spell ID; however, the code only reads the GUID.
*   **PetSpellAutocast**: (Conditional: Client Build > 1.6.1) Parses a toggle for a pet's spell autocast. It extracts the `guid`, `spellId`, and the desired `state` (uint8, likely boolean-like).
*   **PetSetAction**: Parses updates to the pet's action bar. This is the most complex parser in the unit. It extracts the `petGuid` and then determines the number of actions (`count`) based on the total packet size. It expects either 1 or 2 actions. It iterates `count` times to read pairs of `position` and `data` (both uint32) into the `actions` array.
*   **PetCastSpell**: (Conditional: Client Build > 1.8.4) Parses a command for a pet to cast a spell. It extracts the `petGuid`, `spellId`, and `targets` (SpellCastTargets).

**Cross-Unit Boundaries**
All `ReadFromWorldPacket` methods in this unit call out to utility functions for deserialization:
*   **`ByteBuffer/operator>>`**: Used extensively to extract primitive types (uint32, uint8, std::string) and complex structures like `SpellCastTargets`. Specific overloads are called depending on the type being extracted (e.g., `operator>>#9` for strings/complex types, `operator>>#6` for specific numeric contexts).
*   **`ObjectGuid/operator>>`**: Used to deserialize `ObjectGuid` instances, which uniquely identify entities in the game world.
*   **`SpellCastTargetsInfo/operator>>`**: Used specifically by `PetCastSpell` to deserialize the target information for the spell being cast.

These units are called by the main network processing loop (not shown in this MAP, but implied by the `ClientPacket` inheritance and `ReadFromWorldPacket` signature), which instantiates these packet objects and invokes `ReadFromWorldPacket` upon receiving a matching opcode from the client.

**Data Model**
This unit performs no direct database operations. It does not touch any SQL tables. All data is transient, residing in memory within the packet objects after deserialization.

**Notable Implementation Details**
*   **Dynamic Action Count in `PetSetAction`**: The `PetSetAction::ReadFromWorldPacket` method contains a notable heuristic to determine how many actions are being set. It checks if the packet size equals `sizeof(uint64) + 2 * (sizeof(uint32) + sizeof(uint32))` (which is 8 + 16 = 24 bytes). If true, `count` is set to 2; otherwise, it defaults to 1. This implies the protocol allows for variable-length packets for this specific message, and the server must infer the intent from the payload size. This is a fragile parsing strategy if packet padding or future expansions change the size assumptions.
*   **Conditional Compilation**: Several classes (`PetStopAttack`, `PetUnlearn`, `PetSpellAutocast`, `PetCastSpell`) are guarded by `#if SUPPORTED_CLIENT_BUILD > CLIENT_BUILD_X_Y_Z`. This indicates that the packet structure or existence varies between different versions of the World of Warcraft client. Engineers maintaining this code must ensure that the `SUPPORTED_CLIENT_BUILD` macro is correctly defined for the target emulation version, otherwise, these packet handlers will be excluded from compilation, potentially causing missing functionality or crashes if the client sends packets the server doesn't recognize.
*   **Minimal Unlearn Packet**: `PetUnlearn` only reads a `guid`. Typically, unlearning a spell requires specifying *which* spell to unlearn. The absence of a `spellId` field suggests either the spell ID is determined by context elsewhere, or this packet structure is incomplete/simplified for this specific client build. Maintainers should verify if this matches the expected protocol for the targeted client version.
*   **Default Initializers**: Most classes use default initializers for their data members (e.g., `spellId = 0`, `state = 0`). This ensures that if `ReadFromWorldPacket` fails or is skipped, the object remains in a known safe state, though the `ReadFromWorldPacket` method is mandatory for valid usage.

## Member Reference

**ReadFromWorldPacket#10** (`PetCastSpell::ReadFromWorldPacket`): Deserializes the pet GUID, spell ID, and spell targets from the packet buffer. Calls `ObjectGuid/operator>>`, `ByteBuffer/operator>>` (for spellId), and `SpellCastTargetsInfo/operator>>`.

**ReadFromWorldPacket#2** (`PetAction::ReadFromWorldPacket`): Deserializes the pet GUID, action data, and target GUID. Calls `ObjectGuid/operator>>` twice and `ByteBuffer/operator>>` for the action data.

**ReadFromWorldPacket** (`QueryPetName::ReadFromWorldPacket`): Deserializes the pet number and pet GUID. Calls `ByteBuffer/operator>>` for the number and `ObjectGuid/operator>>` for the GUID.

**QueryPetName** (Constructor): Initializes the `QueryPetName` object with the opcode `CMSG_PET_NAME_QUERY` and sets `petNumber` to 0.

**ReadFromWorldPacket#5** (`PetRename::ReadFromWorldPacket`): Deserializes the pet GUID and the new name string. Calls `ObjectGuid/operator>>` and `ByteBuffer/operator>>` for the string.

**ReadFromWorldPacket#3** (`PetAbandon::ReadFromWorldPacket`): Deserializes the GUID of the pet to abandon. Calls `ObjectGuid/operator>>`.

**ReadFromWorldPacket#8** (`PetStopAttack::ReadFromWorldPacket`): Deserializes the pet GUID. Calls `ObjectGuid/operator>>`.

**ReadFromWorldPacket#9** (`PetUnlearn::ReadFromWorldPacket`): Deserializes the pet GUID. Calls `ObjectGuid/operator>>`.

**ReadFromWorldPacket#7** (`PetSpellAutocast::ReadFromWorldPacket`): Deserializes the pet GUID, spell ID, and autocast state. Calls `ObjectGuid/operator>>` and `ByteBuffer/operator>>` for the numeric fields.

**ReadFromWorldPacket#6** (`PetSetAction::ReadFromWorldPacket`): Deserializes the pet GUID, then calculates the number of actions based on packet size. Iterates to read position and data for each action. Calls `ObjectGuid/operator>>` and `ByteBuffer/operator>>` for the action entries. Uses `ByteBuffer/size` to check packet length.

**ReadFromWorldPacket#4** (`PetCancelAura::ReadFromWorldPacket`): Deserializes the pet GUID and the spell ID to cancel. Calls `ObjectGuid/operator>>` and `ByteBuffer/operator>>` for the spell ID.

---

<!-- machine-true, projected from graph.json -->

## Map — Pet

*Source:* Pet.cpp, Pet.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ReadFromWorldPacket#10 | method | ByteBuffer/operator>>#9, ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket#2 | method | ByteBuffer/operator>>#9, ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket | method | ObjectGuid/operator>> | — | — |
| QueryPetName | ctor | — | — | — |
| ReadFromWorldPacket#5 | method | ByteBuffer/operator>>, ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket#3 | method | ByteBuffer/operator>>#9, ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket#8 | method | ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket#9 | method | ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket#7 | method | ByteBuffer/operator>>#6, ByteBuffer/operator>>#9, ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket#6 | method | ByteBuffer/operator>>#9, ByteBuffer/size, ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket#4 | method | ByteBuffer/operator>>#9, ObjectGuid/operator>>, SpellCastTargetsInfo/operator>> | — | — |
