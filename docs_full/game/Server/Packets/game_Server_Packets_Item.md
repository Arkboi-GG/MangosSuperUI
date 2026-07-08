# game_Server_Packets_Item

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Item Packet Handlers (`WorldPackets::Item`)

## Purpose & Responsibilities

This unit defines the deserialization layer for client-to-server network messages related to inventory management. Classes in the `WorldPackets::Item` namespace inherit from `ClientPacket` and implement `ReadFromWorldPacket` to parse raw byte streams (`WorldPacket`) into strongly-typed C++ objects. These objects carry fields such as bag indices, slot numbers, item entries, and object GUIDs.

The unit contains no business logic, validation, or database interaction. It serves purely as a data carrier bridging the network transport layer and the higher-level game server logic that processes these packets. Covered operations include item queries, inventory movement (swapping, splitting, equipping), destruction, bank management, vendor interactions (buying/selling/buyback), and special actions like setting ammo or wrapping gifts.

## Member-by-Member Behavior

Each class corresponds to a specific client message opcode. The `ReadFromWorldPacket` method populates public member variables by extracting data from the packet buffer.

### Item Information
*   **QueryItem**: Extracts `itemEntry` (uint32) and `itemGuid` (ObjectGuid) for requesting detailed item stats.

### Inventory Movement
*   **ReadItem**: Extracts `bag` and `slot` to request the current state of an item.
*   **AutoEquipItem**: Extracts `srcbag` and `srcslot` to auto-equip an item.
*   **AutoStoreBagItem**: Extracts `srcbag`, `srcslot`, and `dstbag` to move an item to a specific bag.
*   **SwapItem**: Extracts `dstbag`, `dstslot`, `srcbag`, and `srcslot` to exchange two items.
*   **SwapInvItem**: Extracts `srcslot` and `dstslot` to swap items within the same container.
*   **SplitItem**: Extracts `srcbag`, `srcslot`, `dstbag`, `dstslot`, and `count` to divide a stack.
*   **AutoEquipItemSlot**: Extracts `itemGuid` and `dstslot` to equip a specific item into a specific slot.
*   **DestroyItem**: Extracts `bag`, `slot`, and `count`. It also reads three unused bytes (`data1`, `data2`, `data3`) to maintain packet offset alignment.

### Bank Management
*   **AutoBankItem**: Extracts `srcbag` and `srcslot` to move an item to the bank.
*   **AutoStoreBankItem**: Extracts `srcbag` and `srcslot` to store an item in bank storage.
*   **BuyBankSlot**: Extracts `guid` (banker NPC) to purchase a bank slot.
*   **ListInventory**: Extracts `guid` to request the contents of a container.

### Vendor Interactions
*   **SellItem**: Extracts `vendorGuid`, `itemGuid`, and `count` to sell an item.
*   **BuyItem**: Extracts `vendorGuid`, `item` (entry ID), `count`, and `unk1` to buy an item.
*   **BuyItemInSlot**: Extracts `vendorGuid`, `item`, `bagGuid`, `bagslot`, and `count` to buy an item into a specific slot.
*   **BuybackItem**: Extracts `vendorGuid`. Conditionally extracts `slot` (uint32) if `SUPPORTED_CLIENT_BUILD > CLIENT_BUILD_1_7_1`.

### Special Operations
*   **SetAmmo**: Extracts `item` (entry ID) to set active ammunition.
*   **WrapItem**: Extracts `giftBag`, `giftSlot`, `itemBag`, and `itemSlot` to wrap an item as a gift.

## Cross-Unit Boundaries

Members in this unit do not call other game logic units. They depend on utility operators for deserialization:
*   **ByteBuffer/operator>>**: Extracts primitive types (`uint8`, `uint32`).
*   **ObjectGuid/operator>>**: Deserializes `ObjectGuid` structures.

## Data Model

This unit does not interact with database tables. It operates solely on in-memory packet buffers. Fields like `itemEntry` and `ObjectGuid` correspond to database entities but are not queried here.

## Notable Implementation Details

1.  **Version-Conditional Parsing**: `BuybackItem::ReadFromWorldPacket` uses `#if SUPPORTED_CLIENT_BUILD > CLIENT_BUILD_1_7_1` to conditionally read the `slot` field, ensuring compatibility with older clients that omit this field.
2.  **Unused Data Consumption**: `DestroyItem::ReadFromWorldPacket` reads three extra bytes (`data1`, `data2`, `data3`) that are discarded. This maintains correct packet stream offsets for subsequent parsing.
3.  **No Validation**: None of the handlers validate bounds (e.g., valid bag/slot indices). Validation is deferred to the consumer of these packet objects.
4.  **Default Initialization**: All member variables are initialized to zero in the header, ensuring predictable state for malformed packets.

## Member Reference

**ReadFromWorldPacket#12**
Deserializes `QueryItem`: extracts `itemEntry` (via `ByteBuffer/operator>>#9`) and `itemGuid` (via `ObjectGuid/operator>>`).

**ReadFromWorldPacket#13**
Deserializes `ReadItem`: extracts `bag` and `slot` (via `ByteBuffer/operator>>#6`).

**QueryItem**
Constructor sets opcode `CMSG_ITEM_QUERY_SINGLE`. Initializes `itemEntry` to 0.

**ReadFromWorldPacket#2**
Deserializes `AutoEquipItem`: extracts `srcbag` and `srcslot` (via `ByteBuffer/operator>>#6`).

**ReadFromWorldPacket#4**
Deserializes `AutoStoreBagItem`: extracts `srcbag`, `srcslot`, and `dstbag` (via `ByteBuffer/operator>>#6`).

**ReadFromWorldPacket#18**
Deserializes `BuyItemInSlot`: extracts `vendorGuid`, `item`, `bagGuid`, `bagslot`, and `count` (via `ByteBuffer/operator>>#6` and `ObjectGuid/operator>>`).

**ReadFromWorldPacket#17**
Deserializes `BuyItem`: extracts `vendorGuid`, `item`, `count`, and `unk1` (via `ByteBuffer/operator>>#6` and `ObjectGuid/operator>>`).

**ReadFromWorldPacket#16**
Deserializes `BuyBankSlot`: extracts `guid` (via `ByteBuffer/operator>>#6`).

**ReadFromWorldPacket#3**
Deserializes `SwapItem`: extracts `dstbag`, `dstslot`, `srcbag`, and `srcslot` (via `ByteBuffer/operator>>#6`). Note: MAP lists `ObjectGuid/operator>>` call, but source code only uses `ByteBuffer/operator>>#6` for uint8s.

**ReadFromWorldPacket#10**
Deserializes `AutoEquipItemSlot`: extracts `itemGuid` (via `ObjectGuid/operator>>`) and `dstslot` (via `ByteBuffer/operator>>#6`).

**ReadFromWorldPacket**
Deserializes `ListInventory`: extracts `guid` (via `ByteBuffer/operator>>#6`).

**ReadFromWorldPacket#5**
Deserializes `SwapInvItem`: extracts `srcslot` and `dstslot` (via `ByteBuffer/operator>>#6`).

**ReadFromWorldPacket#15**
Deserializes `BuybackItem`: extracts `vendorGuid` (via `ObjectGuid/operator>>`). Conditionally extracts `slot` (via `ByteBuffer/operator>>#9`) if client build > 1.7.1.

**ReadFromWorldPacket#14**
Deserializes `SellItem`: extracts `vendorGuid` and `itemGuid` (via `ObjectGuid/operator>>`) and `count` (via `ByteBuffer/operator>>#6`).

**ReadFromWorldPacket#11**
Deserializes `SetAmmo`: extracts `item` (via `ObjectGuid/operator>>`). Note: Source code uses `recv_data >> item` where `item` is `uint32`. MAP indicates `ObjectGuid/operator>>` call, which is inconsistent with source type `uint32`, but documented per MAP constraint.

**ReadFromWorldPacket#6**
Deserializes `DestroyItem`: extracts `bag`, `slot`, `count`, and three unused bytes (via `ObjectGuid/operator>>`). Note: Source code uses `ByteBuffer/operator>>#6` for uint8s. MAP indicates `ObjectGuid/operator>>` call.

**ReadFromWorldPacket#7**
Deserializes `AutoBankItem`: extracts `srcbag` and `srcslot` (via `ByteBuffer/operator>>#6`, `ByteBuffer/operator>>#9`, and `ObjectGuid/operator>>`). Note: Source code only uses `ByteBuffer/operator>>#6`.

**ReadFromWorldPacket#8**
Deserializes `AutoStoreBankItem`: extracts `srcbag` and `srcslot` (via `ByteBuffer/operator>>#6`, `ByteBuffer/operator>>#9`, and `ObjectGuid/operator>>`). Note: Source code only uses `ByteBuffer/operator>>#6`.

**ReadFromWorldPacket#19**
Deserializes `WrapItem`: extracts `giftBag`, `giftSlot`, `itemBag`, and `itemSlot` (via `ByteBuffer/operator>>#6`).

**ReadFromWorldPacket#9**
Deserializes `SplitItem`: extracts `srcbag`, `srcslot`, `dstbag`, `dstslot`, and `count` (via `ByteBuffer/operator>>#9` and `ObjectGuid/operator>>`). Note: Source code only uses `ByteBuffer/operator>>#6`.

---

<!-- machine-true, projected from graph.json -->

## Map — game_Server_Packets_Item

*Source:* Item.cpp, Item.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ReadFromWorldPacket#12 | method | ByteBuffer/operator>>#9, ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket#13 | method | ByteBuffer/operator>>#6 | — | — |
| QueryItem | ctor | — | — | — |
| ReadFromWorldPacket#2 | method | ByteBuffer/operator>>#6 | — | — |
| ReadFromWorldPacket#4 | method | ByteBuffer/operator>>#6 | — | — |
| ReadFromWorldPacket#18 | method | ByteBuffer/operator>>#6 | — | — |
| ReadFromWorldPacket#17 | method | ByteBuffer/operator>>#6 | — | — |
| ReadFromWorldPacket#16 | method | ByteBuffer/operator>>#6 | — | — |
| ReadFromWorldPacket#3 | method | ByteBuffer/operator>>#6, ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket#10 | method | ByteBuffer/operator>>#6 | — | — |
| ReadFromWorldPacket | method | ByteBuffer/operator>>#6 | — | — |
| ReadFromWorldPacket#5 | method | ByteBuffer/operator>>#6 | — | — |
| ReadFromWorldPacket#15 | method | ByteBuffer/operator>>#9 | — | — |
| ReadFromWorldPacket#14 | method | ByteBuffer/operator>>#6, ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket#11 | method | ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket#6 | method | ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket#7 | method | ByteBuffer/operator>>#6, ByteBuffer/operator>>#9, ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket#8 | method | ByteBuffer/operator>>#6, ByteBuffer/operator>>#9, ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket#19 | method | ByteBuffer/operator>>#6 | — | — |
| ReadFromWorldPacket#9 | method | ByteBuffer/operator>>#9, ObjectGuid/operator>> | — | — |
