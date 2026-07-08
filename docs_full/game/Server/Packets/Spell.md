<!-- provenance: verbose -->
# Spell

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Spell Packet Definitions

## Purpose & Responsibilities

The `WorldPackets::Spell` namespace defines C++ classes for client-to-server network packets related to spellcasting and item interaction. These classes serve exclusively as data structures that deserialize raw binary data from incoming `WorldPacket` buffers into strongly-typed fields. This unit contains no game logic, validation, or execution code; its sole responsibility is deserialization according to the protocol definition for each message type.

## Member-by-Member Behavior

All members implement the `ReadFromWorldPacket` method for specific packet classes. This method is invoked by the network layer after a packet is received and identified by its opcode. It extracts fields from the `WorldPacket` buffer using the extraction operator (`>>`).

### Spell Casting and Targeting

**`CastSpell::ReadFromWorldPacket`**
Deserializes the `CMSG_CAST_SPELL` packet. It extracts:
1.  `spellId`: A 32-bit unsigned integer identifying the spell.
2.  `targets`: A `SpellCastTargets` structure containing targeting information. Extraction delegates to `SpellCastTargetsInfo/operator>>`.

### Item Interaction

**`UseItem::ReadFromWorldPacket`**
Deserializes the `CMSG_USE_ITEM` packet, used when activating an item that triggers a spell effect. It extracts:
1.  `bagIndex`: The bag index.
2.  `slot`: The slot within the bag.
3.  `spellSlot`: The index of the spell ID on the item template (items may have multiple spell effects).
4.  `targets`: Target information, extracted via `SpellCastTargetsInfo/operator>>`.

**`OpenItem::ReadFromWorldPacket`**
Deserializes the `CMSG_OPEN_ITEM` packet, used for opening containers. It extracts:
1.  `bagIndex`: The bag index.
2.  `slot`: The slot index.

### Spell/Aura Cancellation

**`CancelCast::ReadFromWorldPacket`**
Deserializes the `CMSG_CANCEL_CAST` packet. Extracts the `spellId` of the spell to cancel.

**`CancelAura::ReadFromWorldPacket`**
Deserializes the `CMSG_CANCEL_AURA` packet. Extracts the `spellId` of the aura to remove.

**`CancelChanneling::ReadFromWorldPacket`**
Deserializes the `CMSG_CANCEL_CHANNELLING` packet. Extracts the `spellId` associated with the channeling effect. The header comments note this field is "not used by server," implying the server ignores the specific ID.

## Cross-Unit Boundaries

*   **`ByteBuffer/operator>>`**: All `ReadFromWorldPacket` methods call the extraction operator defined in `ByteBuffer` to read primitive types (`uint8`, `uint32`) from the packet buffer.
*   **`SpellCastTargetsInfo/operator>>`**: `CastSpell` and `UseItem` delegate the extraction of their `SpellCastTargets` member to this operator, separating target parsing logic from packet definitions.
*   **`ClientPacket`**: All classes inherit from `ClientPacket` (in `Packet.h`), which provides the constructor logic associating the C++ class with its network opcode.

## Data Model

This unit does not interact with any database tables. It operates entirely on transient network data.

## Notable Implementation Details

*   **Default Initialization**: Header files initialize members with defaults (e.g., `spellId = 0`). This ensures a known state if deserialization fails or is incomplete.
*   **Unused Field**: In `CancelChanneling`, `spellId` is explicitly marked as unused by the server.
*   **Spell Slot Index**: In `UseItem`, `spellSlot` refers to the index of the spell ID within the item's template, not the inventory slot.

## Member Reference

**ReadFromWorldPacket#4** (`CastSpell::ReadFromWorldPacket`): Deserializes `spellId` and `targets`. Delegates target extraction to `SpellCastTargetsInfo`.

**ReadFromWorldPacket#6** (`UseItem::ReadFromWorldPacket`): Deserializes `bagIndex`, `slot`, `spellSlot`, and `targets`. Delegates target extraction to `SpellCastTargetsInfo`.

**CastSpell** (`CastSpell` constructor): Initializes the packet with opcode `CMSG_CAST_SPELL`.

**ReadFromWorldPacket#5** (`OpenItem::ReadFromWorldPacket`): Deserializes `bagIndex` and `slot`.

**ReadFromWorldPacket#2** (`CancelCast::ReadFromWorldPacket`): Deserializes `spellId`.

**ReadFromWorldPacket** (`CancelAura::ReadFromWorldPacket`): Deserializes `spellId`.

**ReadFromWorldPacket#3** (`CancelChanneling::ReadFromWorldPacket`): Deserializes `spellId`. Note: The extracted `spellId` is not used by the server logic.

---

<!-- machine-true, projected from graph.json -->

## Map — Spell

*Source:* Spell.cpp, Spell.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ReadFromWorldPacket#4 | method | ByteBuffer/operator>>#9, SpellCastTargetsInfo/operator>> | — | — |
| ReadFromWorldPacket#6 | method | ByteBuffer/operator>>#6, SpellCastTargetsInfo/operator>> | — | — |
| CastSpell | ctor | — | — | — |
| ReadFromWorldPacket#5 | method | ByteBuffer/operator>>#6 | — | — |
| ReadFromWorldPacket#2 | method | ByteBuffer/operator>>#9 | — | — |
| ReadFromWorldPacket | method | ByteBuffer/operator>>#9 | — | — |
| ReadFromWorldPacket#3 | method | ByteBuffer/operator>>#9 | — | — |
