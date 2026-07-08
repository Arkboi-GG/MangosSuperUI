<!-- provenance: verbose -->
# Quest

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Quest Packet Definitions

## Purpose & Responsibilities

The `Quest` translation unit (`Quest.cpp` / `Quest.h`) defines C++ classes within the `WorldPackets::Quest` namespace representing client-to-server network packets for quest interactions. Each class inherits from `ClientPacket` and implements `ReadFromWorldPacket` to deserialize raw binary data from a `WorldPacket` buffer into structured member variables. The unit covers the full quest lifecycle: querying details, interacting with NPCs, managing the quest log, and party quest pushing. It contains no business logic or validation; it strictly maps network bytes to C++ types.

## Member-by-Member Behavior

Each class corresponds to a specific opcode (e.g., `CMSG_QUEST_QUERY`). The `ReadFromWorldPacket` method for each class extracts fields in the order they appear in the protocol.

*   **Query-related**: `QueryQuest` extracts a `uint32` quest entry ID.
*   **NPC Interaction**: `QuestgiverStatusQuery` and `QuestgiverHello` extract only an `ObjectGuid` for the NPC. `QuestgiverAcceptQuest`, `QuestgiverQueryQuest`, `QuestgiverRequestReward`, and `QuestgiverCompleteQuest` extract an `ObjectGuid` and a `uint32` quest ID. `QuestgiverChooseReward` adds a `uint32` reward index.
*   **Quest Log Management**: `QuestLogSwapQuest` extracts two `uint8` slot indices. `QuestLogRemoveQuest` extracts one `uint8` slot index.
*   **Party & Confirmation**: `QuestConfirmAccept` and `PushQuestToParty` extract a `uint32` quest ID. `QuestPushResult` extracts an `ObjectGuid` and a `uint8` message code.

## Cross-Unit Boundaries

All `ReadFromWorldPacket` methods delegate deserialization to two external units:

1.  **`ByteBuffer` (via `operator>>`)**: Called by all `ReadFromWorldPacket` implementations to extract primitive types (`uint32`, `uint8`). Overloads `#6` and `#9` are used depending on the type size.
2.  **`ObjectGuid` (via `operator>>`)**: Called by packets containing NPC or player references to deserialize the unique identifier.

No other units call into this unit directly; these objects are instantiated by the network layer and consumed by higher-level handlers.

## Data Model

This unit does not interact with any database tables. It operates exclusively on network packet data. Integer fields like `questEntry` refer to database records but are not queried here.

## Notable Implementation Details

*   **Default Initialization**: Member variables are initialized to `0` in the class definition, ensuring safe defaults if construction fails.
*   **No Validation**: `ReadFromWorldPacket` methods perform no bounds checking or validation. Errors are handled downstream.
*   **Slot Limits**: Quest log slots are `uint8`, implying a maximum of 255 active quests.

## Member Reference

**ReadFromWorldPacket#2**  
Extracts `questEntry` (uint32) for `QueryQuest`. Calls `ByteBuffer/operator>>#9`.

**ReadFromWorldPacket#13**  
Extracts `guid` (ObjectGuid) for `QuestgiverStatusQuery`. Calls `ObjectGuid/operator>>`.

**ReadFromWorldPacket#10**  
Extracts `guid` (ObjectGuid) for `QuestgiverHello`. Calls `ObjectGuid/operator>>`.

**QueryQuest**  
Constructor for `QueryQuest` packet. Initializes `questEntry` to 0.

**ReadFromWorldPacket#7**  
Extracts `guid` and `quest` for `QuestgiverAcceptQuest`. Calls `ByteBuffer/operator>>#9` and `ObjectGuid/operator>>`.

**ReadFromWorldPacket#11**  
Extracts `guid` and `quest` for `QuestgiverQueryQuest`. Calls `ByteBuffer/operator>>#9` and `ObjectGuid/operator>>`.

**ReadFromWorldPacket#8**  
Extracts `guid`, `quest`, and `reward` for `QuestgiverChooseReward`. Calls `ByteBuffer/operator>>#9` and `ObjectGuid/operator>>`.

**ReadFromWorldPacket#12**  
Extracts `guid` and `quest` for `QuestgiverRequestReward`. Calls `ByteBuffer/operator>>#9` and `ObjectGuid/operator>>`.

**ReadFromWorldPacket#9**  
Extracts `guid` and `quest` for `QuestgiverCompleteQuest`. Calls `ByteBuffer/operator>>#9` and `ObjectGuid/operator>>`.

**ReadFromWorldPacket#5**  
Extracts `slot1` and `slot2` (uint8) for `QuestLogSwapQuest`. Calls `ByteBuffer/operator>>#6`.

**ReadFromWorldPacket#4**  
Extracts `slot` (uint8) for `QuestLogRemoveQuest`. Calls `ByteBuffer/operator>>#6`.

**ReadFromWorldPacket#3**  
Extracts `questId` (uint32) for `QuestConfirmAccept`. Calls `ByteBuffer/operator>>#9`.

**ReadFromWorldPacket**  
Extracts `questId` (uint32) for `PushQuestToParty`. Calls `ByteBuffer/operator>>#9`.

**ReadFromWorldPacket#6**  
Extracts `guid` and `msg` for `QuestPushResult`. Calls `ByteBuffer/operator>>#6` and `ObjectGuid/operator>>`.

---

<!-- machine-true, projected from graph.json -->

## Map — Quest

*Source:* Quest.cpp, Quest.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ReadFromWorldPacket#2 | method | ByteBuffer/operator>>#9 | — | — |
| ReadFromWorldPacket#13 | method | ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket#10 | method | ObjectGuid/operator>> | — | — |
| QueryQuest | ctor | — | — | — |
| ReadFromWorldPacket#7 | method | ByteBuffer/operator>>#9, ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket#11 | method | ByteBuffer/operator>>#9, ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket#8 | method | ByteBuffer/operator>>#9, ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket#12 | method | ByteBuffer/operator>>#9, ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket#9 | method | ByteBuffer/operator>>#9, ObjectGuid/operator>> | — | — |
| ReadFromWorldPacket#5 | method | ByteBuffer/operator>>#6 | — | — |
| ReadFromWorldPacket#4 | method | ByteBuffer/operator>>#6 | — | — |
| ReadFromWorldPacket#3 | method | ByteBuffer/operator>>#9 | — | — |
| ReadFromWorldPacket | method | ByteBuffer/operator>>#9 | — | — |
| ReadFromWorldPacket#6 | method | ByteBuffer/operator>>#6, ObjectGuid/operator>> | — | — |
