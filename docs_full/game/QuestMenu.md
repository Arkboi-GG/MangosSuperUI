# QuestMenu

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# QuestMenu

**Purpose & Responsibilities**

`QuestMenu` is a lightweight container class responsible for aggregating a list of available quests to be presented to a player character during an interaction with an NPC (Non-Player Character). It acts as a data buffer between the server-side logic that determines which quests are relevant to a player (based on level, race, faction, and quest status) and the network layer that serializes this information into packets for the client.

The class manages a `std::vector` of `QuestMenuItem` structures, each holding a quest identifier (`m_qId`) and a display icon (`m_qIcon`). Its primary responsibility is to allow the calling logic to populate this list, check its state (empty vs. populated), retrieve specific items by index, and verify the presence of specific quest IDs. It does not handle network transmission itself; that responsibility lies with `PlayerMenu`, which aggregates `QuestMenu` alongside `GossipMenu`.

**Member-by-Member Behavior**

The members of `QuestMenu` are focused on basic collection management:

*   **Construction/Destruction**: The constructor initializes the internal list, and the destructor cleans it up.
*   **AddMenuItem**: Appends a new quest entry to the internal vector. This is called repeatedly by higher-level logic (likely within `Player.Main` or `GossipDef`) to build the list of quests an NPC can offer.
*   **ClearMenu**: Resets the internal vector, removing all previously added quest entries. This is essential for reusing the `QuestMenu` object for subsequent interactions or clearing stale data.
*   **MenuItemCount**: Returns the number of quests currently in the list. This is used by callers to determine if the menu is empty or to iterate over the items.
*   **Empty**: Returns a boolean indicating whether the internal vector contains any elements. This is a quick check to see if there are any quests to display.
*   **HasItem**: Checks if a specific quest ID exists within the current list. This allows the system to avoid duplicates or verify specific quest availability before sending data.
*   **GetItem**: Retrieves a constant reference to a `QuestMenuItem` at a specific index. This is used by serialization routines to access the quest ID and icon for packet construction.

**Cross-Unit Boundaries**

`QuestMenu` is a passive data structure; it does not initiate calls to other units. Instead, it is consumed by units responsible for constructing and sending network messages.

*   **Called by `GossipDef/SendGossipMenu`**: The `GossipDef` unit (specifically the `SendGossipMenu` method) calls `MenuItemCount` and `GetItem` to serialize the quest list into the gossip menu packet sent to the client. It iterates through the `QuestMenu` items to include them in the response.
*   **Called by `GossipDef/SendQuestGiverQuestList`**: Similarly, when sending a dedicated quest giver list, `GossipDef` uses `MenuItemCount` and `GetItem` to populate the packet with the available quests.
*   **Called by `Player.Main/SendPreparedQuest`**: The `Player.Main` unit calls `MenuItemCount` and `GetItem` when preparing a quest-related message, likely to verify the menu state or extract data for specific quest handling logic.
*   **Called by `Player.Main/SendPreparedGossip`**: The `Player.Main` unit calls `Empty` to check if the `QuestMenu` is empty. This is likely part of a validation step to ensure that a gossip menu being prepared actually has content (either gossip options or quests) before proceeding.
*   **Called by `Player.Main/SendPreparedQuest`**: The `Player.Main` unit also calls `Empty` in the context of quest preparation, possibly to handle cases where no quests are available despite the interaction attempt.

**Data Model**

`QuestMenu` does not interact directly with database tables. It operates entirely on in-memory data structures (`std::vector<QuestMenuItem>`). The quest IDs and icons stored in the menu are derived from database queries performed by higher-level units (such as `Player` or `GossipDef`) before populating the `QuestMenu` object. Therefore, no SQL queries or table references are present in this unit's source code.

**Notable Implementation Details**

*   **Index-Based Access**: `GetItem` takes a `uint16` index and performs direct array access (`m_qItems[Id]`). There is no bounds checking in this method. Callers are responsible for ensuring the index is valid (i.e., less than `MenuItemCount()`). This is a common pattern in performance-critical game servers where bounds checks are assumed to be handled by the caller.
*   **No Duplicate Prevention in AddMenuItem**: The `AddMenuItem` method simply appends to the vector. It does not check for duplicate quest IDs. The `HasItem` method exists, suggesting that duplicate prevention is the responsibility of the caller (e.g., `Player.Main` or `GossipDef`) before calling `AddMenuItem`.
*   **Const Correctness**: Methods like `MenuItemCount`, `Empty`, and `GetItem` are marked `const`, ensuring they do not modify the state of the `QuestMenu` object. This allows them to be called on const instances, which is important for read-only access during serialization.
*   **Protected Internal State**: The internal vector `m_qItems` is protected, meaning it is accessible to derived classes but not to external code. This encapsulation ensures that the list can only be modified through the provided methods (`AddMenuItem`, `ClearMenu`).

## Member Reference

**MenuItemCount**  
Returns the number of quest items currently stored in the internal `m_qItems` vector. Used by `GossipDef/SendGossipMenu`, `GossipDef/SendQuestGiverQuestList`, and `Player.Main/SendPreparedQuest` to determine the size of the list for iteration or validation.

**Empty**  
Returns `true` if the `m_qItems` vector is empty, `false` otherwise. Used by `Player.Main/SendPreparedGossip` and `Player.Main/SendPreparedQuest` to quickly check if there are any quests to display or process.

**GetItem**  
Returns a constant reference to the `QuestMenuItem` at the specified index `Id`. Used by `GossipDef/SendGossipMenu`, `GossipDef/SendQuestGiverQuestList`, and `Player.Main/SendPreparedQuest` to access individual quest data (ID and icon) for packet serialization. Note that this method does not perform bounds checking.

---

<!-- machine-true, projected from graph.json -->

## Map — QuestMenu

*Source:* GossipDef.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| MenuItemCount | method | — | GossipDef/SendGossipMenu, GossipDef/SendQuestGiverQuestList, Player.Main/SendPreparedQuest | — |
| Empty | method | — | Player.Main/SendPreparedGossip, Player.Main/SendPreparedQuest | — |
| GetItem | method | — | GossipDef/SendGossipMenu, GossipDef/SendQuestGiverQuestList, Player.Main/SendPreparedQuest | — |
