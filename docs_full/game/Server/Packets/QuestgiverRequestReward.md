# QuestgiverRequestReward

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# QuestgiverRequestReward

**Purpose & Responsibilities**

`QuestgiverRequestReward` is a client-side packet structure within the `WorldPackets::Quest` namespace, defined in `Quest.h`. It represents the `CMSG_QUESTGIVER_REQUEST_REWARD` message sent by the game client to the server. Its sole responsibility is to carry the necessary identifiers—the GUID of the quest giver NPC and the ID of the completed quest—required for the server to process a player's request to claim rewards for a finished quest.

As a `ClientPacket`, this class serves as a data container and deserialization target. It does not contain business logic for reward distribution; that logic resides in the server-side handler that processes this packet type. The class ensures that the raw binary data received from the network is correctly parsed into structured fields (`guid` and `quest`) that the rest of the engine can consume.

**Member-by-Member Behavior**

The unit consists of a single constructor and two public data members, along with an inherited virtual method for reading packet data.

*   **Data Members**:
    *   `ObjectGuid guid`: Stores the unique identifier of the Non-Player Character (NPC) or object from which the player is requesting the reward. This allows the server to verify that the player is interacting with the correct entity authorized to give out rewards for the specified quest.
    *   `uint32 quest`: Stores the database entry ID of the quest being completed. This links the request to specific quest data, including reward items, gold, experience, and reputation gains.

*   **Constructor**:
    *   `explicit QuestgiverRequestReward()`: Initializes the packet with the opcode `CMSG_QUESTGIVER_REQUEST_REWARD` via the base class `ClientPacket`. It initializes the `quest` member to `0` using in-class initialization. The `guid` member is default-initialized by the `ObjectGuid` class.

*   **Deserialization**:
    *   `ReadFromWorldPacket(WorldPacket& recv_data)`: Declared as `override`, this method is responsible for parsing the incoming network buffer. While the implementation is not shown in the header, its signature indicates it extracts the `guid` and `quest` values from the `recv_data` stream according to the protocol definition for `CMSG_QUESTGIVER_REQUEST_REWARD`.

**Cross-Unit Boundaries**

*   **Inheritance**: Inherits from `ClientPacket` (defined in `Packet.h`). This establishes the contract for packet handling, including the `ReadFromWorldPacket` interface and the association with a specific opcode.
*   **Dependencies**:
    *   `ObjectGuid` (from `ObjectGuid.h`): Used for the `guid` field. This dependency provides the standard mechanism for identifying entities in the world.
    *   `SharedDefines.h`: Likely provides the definition for `CMSG_QUESTGIVER_REQUEST_REWARD` or related constants, though the specific constant usage is handled by the base class constructor.

There are no outgoing calls to other units from this class, nor is it called by other units in the provided map. It is instantiated by the packet parsing infrastructure when the server receives the corresponding opcode from a client.

**Data Model**

This unit does not directly interact with database tables. It operates purely on network packet data. The `quest` ID it carries corresponds to entries in the `quest_template` table (and related tables like `quest_template_addon`), but this class itself performs no SQL queries or direct database access.

**Notable Implementation Details**

*   **Final Class**: The class is marked `final`, indicating it is not intended for inheritance. This is consistent with its role as a leaf node in the packet hierarchy.
*   **Default Initialization**: The `quest` member is initialized to `0` in the declaration. This is a safety measure to ensure the variable has a defined value even if `ReadFromWorldPacket` fails or is not called before access (though proper usage requires calling `ReadFromWorldPacket` first).
*   **Explicit Constructor**: The constructor is `explicit`, preventing implicit conversions from other types, which helps catch programming errors during packet creation.

## Member Reference

**QuestgiverRequestReward**
The constructor for the `QuestgiverRequestReward` packet. It initializes the base `ClientPacket` with the opcode `CMSG_QUESTGIVER_REQUEST_REWARD` and sets the `quest` member to `0`. The `guid` member is default-constructed. This constructor is called by the packet factory when the server detects the corresponding opcode in the incoming network stream.

---

<!-- machine-true, projected from graph.json -->

## Map — QuestgiverRequestReward

*Source:* Quest.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| QuestgiverRequestReward | ctor | — | — | — |
