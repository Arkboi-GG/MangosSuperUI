# QuestConfirmAccept

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# QuestConfirmAccept

**Purpose & Responsibilities**

`QuestConfirmAccept` is a lightweight data structure within the `WorldPackets::Quest` namespace, representing a specific client-to-server network message: `CMSG_QUEST_CONFIRM_ACCEPT`. Its sole responsibility is to encapsulate the raw data received from a client when a player confirms the acceptance of a quest that previously required confirmation (typically due to conflicting quests or other restrictions). It inherits from `ClientPacket`, integrating into the server’s packet handling infrastructure, but contains no logic beyond storage and initialization. It does not interact with databases, nor does it perform any validation or business logic itself.

**Member-by-Member Behavior**

The unit consists of a single constructor.

*   **`QuestConfirmAccept()`**: This default constructor initializes the packet object. It sets the internal packet opcode to `CMSG_QUEST_CONFIRM_ACCEPT` via the base class `ClientPacket` constructor and initializes the member variable `questId` to `0`. This ensures that if the packet is instantiated but not yet populated by reading from a network stream, the `questId` holds a safe, neutral value.

**Cross-Unit Boundaries**

*   **Calls Out**: None. The constructor performs only local initialization.
*   **Called By**: While the MAP indicates no external callers for the constructor specifically, instances of `QuestConfirmAccept` are typically constructed by the network layer (e.g., in `WorldSession` or similar packet dispatchers) when the server receives the `CMSG_QUEST_CONFIRM_ACCEPT` opcode from a client. The `ReadFromWorldPacket` method (declared in the base class `ClientPacket` and overridden in this class, though not listed in the MAP as a distinct member for this partial) is responsible for populating `questId` from the incoming binary data. The populated object is then passed to the appropriate handler (likely in `Player.cpp` or `QuestHandler.cpp`) to process the quest acceptance logic.

**Data Model**

This unit does not directly access any database tables. It operates purely on in-memory network packet data. The `questId` field corresponds to the `entry` ID found in the `quest_template` table, but this unit does not query or modify that table.

**Notable Implementation Details**

*   **Minimalist Design**: As a packet structure, it contains no methods other than the constructor and the inherited/overridden `ReadFromWorldPacket`. All logic related to *what happens* after the packet is received resides in other units.
*   **Default Initialization**: The `questId` is explicitly initialized to `0` in the class definition. This is a defensive measure, ensuring that even if `ReadFromWorldPacket` fails or is not called, the object remains in a known state.
*   **Namespace Context**: It resides in `WorldPackets::Quest`, indicating it is part of the modernized packet handling system introduced in later versions of MaNGOS/WowVMaNGOS, separating packet definitions from legacy packet handling code.

## Member Reference

**QuestConfirmAccept**
Constructor for the `QuestConfirmAccept` packet. Initializes the base `ClientPacket` with the opcode `CMSG_QUEST_CONFIRM_ACCEPT` and sets the `questId` member to `0`. No external calls are made.

---

<!-- machine-true, projected from graph.json -->

## Map — QuestConfirmAccept

*Source:* Quest.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| QuestConfirmAccept | ctor | — | — | — |
