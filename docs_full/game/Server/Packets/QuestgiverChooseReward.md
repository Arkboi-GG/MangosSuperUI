# QuestgiverChooseReward

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# QuestgiverChooseReward

**Purpose & Responsibilities**

`QuestgiverChooseReward` is a client-side network packet structure within the `WorldPackets::Quest` namespace. It represents the `CMSG_QUESTGIVER_CHOOSE_REWARD` message sent by the game client to the server. Its sole responsibility is to deserialize the raw binary data of this specific network message into structured fields: the GUID of the quest giver NPC, the ID of the completed quest, and the index of the reward item the player has selected.

This unit contains no business logic, validation, or state management. It is a pure data carrier used during the initial phase of the quest reward selection process, before the final reward is claimed.

## Member-by-Member Behavior

### **QuestgiverChooseReward** (Constructor)
The default constructor initializes the packet object. It performs two actions:
1.  Calls the base class `ClientPacket` constructor, passing the opcode `CMSG_QUESTGIVER_CHOOSE_REWARD`. This registers the packet type with the network layer so it can be correctly routed when received.
2.  Initializes the member variables `guid`, `quest`, and `reward` to their default values (`ObjectGuid()` for guid, `0` for quest and reward).

Note that the actual population of these fields occurs later via the `ReadFromWorldPacket` method (inherited from `ClientPacket` but implemented in the corresponding `.cpp` file, which is not part of this unit's scope but is implied by the interface). The constructor itself ensures the object is in a valid, empty state ready to receive data.

## Cross-Unit Boundaries

*   **Calls out:** None. The constructor does not invoke any other units.
*   **Called by:** None listed in the map. In practice, this class is instantiated by the network packet handling system (likely in `WorldSession` or a packet handler dispatcher) when a `CMSG_QUESTGIVER_CHOOSE_REWARD` message arrives on the wire. The caller will then invoke `ReadFromWorldPacket` to populate the fields.

## Data Model

This unit does not interact directly with any database tables. It operates entirely on transient network data. The `quest` field corresponds to a `quest_template` entry ID, and `guid` corresponds to a creature/player GUID, but no SQL queries are executed by this class.

## Notable Implementation Details

*   **Opcode Association:** The class is tightly coupled to the specific opcode `CMSG_QUESTGIVER_CHOOSE_REWARD`. Any change in the client protocol regarding this message would require updating this class.
*   **Field Semantics:**
    *   `guid`: Identifies the NPC (or potentially player) with whom the interaction is occurring.
    *   `quest`: The ID of the quest being rewarded.
    *   `reward`: An integer index representing the chosen reward option. In World of Warcraft, quests often offer multiple reward choices (e.g., pick one of three items). This field indicates which option the player clicked.
*   **No Validation:** The class does not validate whether the `quest` exists, whether the player has completed it, or whether the `reward` index is valid for that quest. Such validation is performed by the server-side handler that processes this packet after deserialization.

## Member Reference

**QuestgiverChooseReward**: Default constructor that initializes the packet with the `CMSG_QUESTGIVER_CHOOSE_REWARD` opcode and sets default values for `guid`, `quest`, and `reward`.

---

<!-- machine-true, projected from graph.json -->

## Map — QuestgiverChooseReward

*Source:* Quest.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| QuestgiverChooseReward | ctor | — | — | — |
