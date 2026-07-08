# QuestgiverQueryQuest

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# QuestgiverQueryQuest

**Purpose & Responsibilities**

`QuestgiverQueryQuest` is a client-side network packet structure within the `WorldPackets::Quest` namespace. It represents the `CMSG_QUESTGIVER_QUERY_QUEST` message sent by the game client to the server. Its sole responsibility is to carry the data required for a player to request detailed information about a specific quest from a specific NPC (Non-Player Character) quest giver.

The structure encapsulates two critical pieces of context:
1.  **`guid`**: The unique identifier (`ObjectGuid`) of the NPC quest giver being interacted with.
2.  **`quest`**: The database entry ID (`uint32`) of the specific quest the client wishes to query.

This unit is part of the packet deserialization layer. It defines the memory layout and initialization for the incoming data but does not contain the logic for processing the request (which resides in the handler that consumes this packet) nor the logic for parsing the raw bytes (which is implemented in the `ReadFromWorldPacket` method, though the implementation of that method is not included in this specific source file snippet, the declaration is present).

**Member-by-Member Behavior**

*   **Constructor (`QuestgiverQueryQuest`)**: Initializes the packet object. It sets the internal packet opcode to `CMSG_QUESTGIVER_QUERY_QUEST` via the base class `ClientPacket` constructor. It initializes the `quest` member to `0`. The `guid` member is default-initialized by the `ObjectGuid` class constructor. This ensures the object is in a valid, empty state before data is read from the network stream.

**Cross-Unit Boundaries**

*   **Calls Out**: None. The constructor performs only local initialization and base class construction.
*   **Called By**: None listed in the map. In practice, this object is instantiated by the network layer when a `CMSG_QUESTGIVER_QUERY_QUEST` packet is received from the client. The network layer will then call `ReadFromWorldPacket` to populate the `guid` and `quest` fields. Subsequently, a quest handler (not shown in this unit) will consume this object to validate the quest giver and retrieve quest details.

**Data Model**

This unit does not directly interact with database tables. It carries identifiers (`guid` and `quest` entry ID) that correspond to records in the database (likely `creature_template` for the GUID/NPC and `quest_template` for the quest entry), but the mapping and retrieval logic reside in other units. No SQL queries are present in this source file.

**Notable Implementation Details**

*   **Inheritance**: Inherits from `ClientPacket`, indicating this is a message originating from the client.
*   **Final Class**: The class is marked `final`, preventing further inheritance. This is consistent with packet structures which are leaf nodes in the type hierarchy.
*   **Default Initialization**: The `quest` field is explicitly initialized to `0` in the class definition. While `ReadFromWorldPacket` will overwrite this value, default initialization provides a safe fallback if reading fails or is skipped.
*   **Namespace**: Located in `WorldPackets::Quest`, grouping all quest-related network packets together for organizational clarity.

## Member Reference

**QuestgiverQueryQuest**
Constructor for the `QuestgiverQueryQuest` packet. Initializes the base `ClientPacket` with the opcode `CMSG_QUESTGIVER_QUERY_QUEST` and sets the `quest` member to `0`. The `guid` member is default-constructed. This prepares the object to receive data from the network stream.

---

<!-- machine-true, projected from graph.json -->

## Map — QuestgiverQueryQuest

*Source:* Quest.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| QuestgiverQueryQuest | ctor | — | — | — |
