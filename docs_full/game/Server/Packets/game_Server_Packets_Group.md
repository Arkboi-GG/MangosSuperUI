# game_Server_Packets_Group

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Group Packet Deserialization (WorldPackets::Group)

**Purpose & Responsibilities**
This translation unit (`Group.cpp` / `Group.h`) defines the client-to-server packet structures for group management operations within the `wowvmangos` server. It implements the deserialization logic (`ReadFromWorldPacket`) for various `ClientPacket` subclasses in the `WorldPackets::Group` namespace. These packets handle invitations, uninvitations, subgroup management, loot settings, raid targets, and ready checks. The unit is strictly responsible for parsing raw binary data from `WorldPacket` objects into strongly-typed C++ structures, handling version-specific differences in packet formats via preprocessor directives. It does not contain business logic for processing these requests; it only prepares the data for downstream handlers.

**Data Model**
This unit interacts exclusively with in-memory network buffers (`WorldPacket`). It does not query or modify any database tables.

**Notable Implementation Details**
*   **Client Version Compatibility:** Several packet classes (`GroupSetLeader`, `GroupAssistantLeader`, `RaidTargetUpdate`, `RaidReadyCheck`) use `#if SUPPORTED_CLIENT_BUILD` directives to support different packet layouts for older vs. newer clients. Specifically, leadership changes switched from using character names (`std::string`) to `ObjectGuid` after client build `1_11_2`. Raid features were added after build `1_10_2`.
*   **Conditional Parsing:** In `RaidTargetUpdate`, the presence of the `guid` field depends on the value of `iconId`. If `iconId` is `0xFF`, the packet is treated as a request (no GUID), otherwise it is an update (GUID present).
*   **Optional State:** `RaidReadyCheck` uses `nonstd::optional<uint8>` to distinguish between a request (empty packet, `state` has no value) and a response (packet contains a byte, `state` holds the value). The `ReadFromWorldPacket` method checks `recv_data.empty()` before attempting to read.

**Cross-Unit Boundaries**
*   **Calls Out:** All `ReadFromWorldPacket` methods call operators from `ByteBuffer` (specifically `operator>>` variants) and `ObjectGuid` (`operator>>`) to extract data from the `WorldPacket` buffer.
*   **Called By:** This unit is part of the packet parsing infrastructure. While the MAP indicates no specific "Called by" entries, these classes are instantiated and parsed by the main network handler (likely in `WorldSession` or similar central dispatch logic) when corresponding `CMSG_*` or `MSG_*` opcodes are received.

## Member Reference

**GroupInvite**
Constructor that initializes the `ClientPacket` base class with opcode `CMSG_GROUP_INVITE`.

**ReadFromWorldPacket#3**
Parses the `memberName` string from the packet buffer using `ByteBuffer::operator>>`.

**ReadFromWorldPacket#6**
Parses the `memberName` string from the packet buffer using `ByteBuffer::operator>>`.

**ReadFromWorldPacket#7**
Parses the `guid` (`ObjectGuid`) from the packet buffer using `ObjectGuid::operator>>`.

**ReadFromWorldPacket#13**
Parses the `guid` (`ObjectGuid`) from the packet buffer using `ObjectGuid::operator>>`.

**ReadFromWorldPacket#8**
Parses three fields sequentially: `lootMethod` (uint32), `lootMaster` (ObjectGuid), and `lootThreshold` (uint32) using respective `operator>>` overloads.

**ReadFromWorldPacket#9**
Parses two floating-point coordinates, `x` and `y`, from the packet buffer using `ByteBuffer::operator>>`.

**ReadFromWorldPacket#12**
Parses two unsigned 32-bit integers, `minimum` and `maximum`, from the packet buffer using `ByteBuffer::operator>>`.

**ReadFromWorldPacket#2**
Parses the `name` string and `groupNr` (uint8) from the packet buffer using `ByteBuffer::operator>>`.

**ReadFromWorldPacket#5**
Parses two strings, `name` and `nameSwapWith`, from the packet buffer using `ByteBuffer::operator>>`.

**ReadFromWorldPacket#4**
Conditionally parses either a `guid` (if client build > 1_11_2) or a `name` string (otherwise) using the appropriate `operator>>`.

**ReadFromWorldPacket**
Conditionally parses either a `guid` (if client build > 1_11_2) or a `name` string (otherwise), followed by a `flag` (uint8), using the appropriate `operator>>` overloads.

**ReadFromWorldPacket#11**
*(Only compiled if client build > 1_10_2)* Parses `iconId` (uint8). If `iconId` is not `0xFF`, it additionally parses the `guid` (`ObjectGuid`). Uses `ByteBuffer::operator>>` and `ObjectGuid::operator>>`.

**ReadFromWorldPacket#10**
*(Only compiled if client build > 1_10_2)* Checks if the packet is empty. If not empty, it reads a single byte `s` and assigns it to the `state` optional. Uses `ByteBuffer::empty` and `ByteBuffer::operator>>`.

---

<!-- machine-true, projected from graph.json -->

## Map — game_Server_Packets_Group

*Source:* Group.cpp, Group.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ReadFromWorldPacket#3 | method | ByteBuffer/operator>> | — | — |
| ReadFromWorldPacket#6 | method | ByteBuffer/operator>> | — | — |
| ReadFromWorldPacket#7 | method | ObjectGuid/operator>> | — | — |
| GroupInvite | ctor | — | — | — |
| ReadFromWorldPacket#13 | method | ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket#8 | method | ByteBuffer/operator>>#9, ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket#9 | method | ByteBuffer/operator>>#8 | — | — |
| ReadFromWorldPacket#12 | method | ByteBuffer/operator>>#9 | — | — |
| ReadFromWorldPacket#2 | method | ByteBuffer/operator>>, ByteBuffer/operator>>#6 | — | — |
| ReadFromWorldPacket#5 | method | ByteBuffer/operator>> | — | — |
| ReadFromWorldPacket#4 | method | ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket | method | ByteBuffer/operator>>#6, ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket#11 | method | ByteBuffer/operator>>#6, ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket#10 | method | ByteBuffer/empty, ByteBuffer/operator>>#6 | — | — |
