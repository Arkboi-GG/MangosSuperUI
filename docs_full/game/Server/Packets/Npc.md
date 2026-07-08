<!-- provenance: verbose -->
# Npc

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Npc Packet Handlers

**Purpose & Responsibilities**
The `Npc` unit defines client-to-server network packet structures within `WorldPackets::Npc` for Non-Player Character interactions. Its sole responsibility is deserializing raw binary data from `WorldPacket` objects into structured C++ classes inheriting from `ClientPacket`. It extracts identifiers (`ObjectGuid`) and action parameters (spell IDs, pet slots, gossip codes) required by higher-level game logic. This unit contains no game logic, validation, or database access.

## Member-by-Member Behavior

Members are `ReadFromWorldPacket` methods grouped by interaction type. All parse data from `recv_data` using `ObjectGuid/operator>>` for entities and `ByteBuffer/operator>>` for primitives.

### Gossip & Text
*   **GossipHello**: Reads `npcGuid` for opening a gossip menu.
*   **GossipSelectOption**: Reads `guid`, `gossipListId`, and optionally `code` (checked via `ByteBuffer/empty`) for selecting a gossip option.
*   **NpcTextQuery**: Reads `textID` and `guid` for requesting NPC text.

### Service Activations
Simple activations identifying the target NPC:
*   **BankerActivate**, **BinderActivate**, **SpiritHealerActivate**, **TabardVendorActivate**: Read `guid` or `npcGuid`.

### Trainers & Repairs
*   **TrainerList**: Reads `guid` to request spell lists.
*   **TrainerBuySpell**: Reads `guid` and `spellId` to purchase a spell.
*   **RepairItem**: Reads `npcGuid` and `itemGuid` to repair a specific item.

### Pet Stabling
*   **ListStabledPets**, **StablePet**, **BuyStableSlot**: Read `npcGuid` for listing, stabling, or buying slots.
*   **UnstablePet**, **StableSwapPet**: Read `npcGuid` and `petNumber` to retrieve or swap a specific pet.

## Cross-Unit Boundaries

*   **Calls Out**:
    *   `ObjectGuid/operator>>`: Used by all members to parse entity identifiers.
    *   `ByteBuffer/operator>>` / `ByteBuffer/empty`: Used by `GossipSelectOption`, `NpcTextQuery`, `TrainerBuySpell`, `UnstablePet`, and `StableSwapPet` to parse `uint32`/`std::string` fields. `GossipSelectOption` specifically checks `ByteBuffer/empty` before reading the optional `code` string.
*   **Called By**: No external units call into this unit directly; packets are instantiated by the network layer.

## Data Model

This unit does not interact with any database tables.

## Notable Implementation Details

*   **Optional Code Field**: `GossipSelectOption::ReadFromWorldPacket` checks `!recv_data.empty()` before reading `code`, handling variable-length packets robustly.
*   **Naming Inconsistency**: Some classes use `guid` (e.g., `BankerActivate`) while others use `npcGuid` (e.g., `GossipHello`) for the same semantic purpose. Both are `ObjectGuid`.
*   **Default Initialization**: Numeric fields (`textID`, `spellId`, etc.) are initialized to `0` in headers to ensure safe defaults if construction fails.

## Member Reference

**ReadFromWorldPacket#4**
Deserializes `GossipHello`; reads `npcGuid` via `ObjectGuid/operator>>`.

**ReadFromWorldPacket#7**
Deserializes `NpcTextQuery`; reads `textID` via `ByteBuffer/operator>>#9` and `guid` via `ObjectGuid/operator>>`.

**ReadFromWorldPacket#14**
Deserializes `TrainerList`; reads `guid` via `ObjectGuid/operator>>`.

**GossipHello**
Constructor for `GossipHello` packet (`CMSG_GOSSIP_HELLO`).

**ReadFromWorldPacket#13**
Deserializes `TrainerBuySpell`; reads `guid` via `ObjectGuid/operator>>` and `spellId` via `ByteBuffer/operator>>#9`.

**ReadFromWorldPacket#2**
Deserializes `BinderActivate`; reads `npcGuid` via `ObjectGuid/operator>>`.

**ReadFromWorldPacket**
Deserializes `BankerActivate`; reads `guid` via `ObjectGuid/operator>>`.

**ReadFromWorldPacket#12**
Deserializes `TabardVendorActivate`; reads `guid` via `ObjectGuid/operator>>`.

**ReadFromWorldPacket#9**
Deserializes `SpiritHealerActivate`; reads `guid` via `ObjectGuid/operator>>`.

**ReadFromWorldPacket#8**
Deserializes `RepairItem`; reads `npcGuid` and `itemGuid` via `ObjectGuid/operator>>`.

**ReadFromWorldPacket#6**
Deserializes `ListStabledPets`; reads `npcGuid` via `ObjectGuid/operator>>`.

**ReadFromWorldPacket#10**
Deserializes `StablePet`; reads `npcGuid` via `ObjectGuid/operator>>`.

**ReadFromWorldPacket#15**
Deserializes `UnstablePet`; reads `npcGuid` via `ObjectGuid/operator>>` and `petNumber` via `ByteBuffer/operator>>#9`.

**ReadFromWorldPacket#3**
Deserializes `BuyStableSlot`; reads `npcGuid` via `ObjectGuid/operator>>`.

**ReadFromWorldPacket#11**
Deserializes `StableSwapPet`; reads `npcGuid` via `ObjectGuid/operator>>` and `petNumber` via `ByteBuffer/operator>>#9`.

**ReadFromWorldPacket#5**
Deserializes `GossipSelectOption`; reads `guid` via `ObjectGuid/operator>>`, `gossipListId` via `ByteBuffer/operator>>`, and conditionally `code` via `ByteBuffer/operator>>#9` if `ByteBuffer/empty` is false.

---

<!-- machine-true, projected from graph.json -->

## Map — Npc

*Source:* Npc.cpp, Npc.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ReadFromWorldPacket#4 | method | ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket#7 | method | ByteBuffer/operator>>#9, ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket#14 | method | ObjectGuid/operator>> | — | — |
| GossipHello | ctor | — | — | — |
| ReadFromWorldPacket#13 | method | ByteBuffer/operator>>#9, ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket#2 | method | ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket | method | ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket#12 | method | ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket#9 | method | ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket#8 | method | ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket#6 | method | ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket#10 | method | ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket#15 | method | ByteBuffer/operator>>#9, ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket#3 | method | ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket#11 | method | ByteBuffer/operator>>#9, ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket#5 | method | ByteBuffer/empty, ByteBuffer/operator>>, ByteBuffer/operator>>#9, ObjectGuid/operator>> | — | — |
