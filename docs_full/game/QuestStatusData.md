# QuestStatusData

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# QuestStatusData

**QuestStatusData** is a lightweight aggregate struct defined in `QuestDef.h` that tracks the runtime state of a single quest instance for a specific player. It does not contain logic; it holds mutable data fields representing whether a quest is active, completed, rewarded, or expired, along with counters for objective progress.

This struct is instantiated by two distinct subsystems:
1.  **Chat Commands**: When a game master queries quest status via `ChatHandler.CharacterCommands/HandleQuestStatusCommandHelper`, a temporary `QuestStatusData` object is constructed to hold the calculated status for display.
2.  **Player Quest Management**: When a player’s race changes (triggering quest updates via `Player.Main/ChangeQuestsForRace`), the system constructs `QuestStatusData` objects to initialize or reset the quest state for the affected quests.

The struct contains no methods other than its constructor. All behavior regarding how these fields are populated or interpreted resides in the calling units (`ChatHandler` and `Player`).

## Member-by-Member Behavior

### Construction
**QuestStatusData** initializes all fields to a "clean" or "new" state. This ensures that any newly created instance represents a quest that has not yet been evaluated for availability, completion, or reward.

*   **`m_status`**: Initialized to `QUEST_STATUS_NONE`. This indicates the quest status has not yet been determined by the game logic.
*   **`m_rewarded`**: Initialized to `false`. The player has not yet claimed rewards for this quest instance.
*   **`m_explored`**: Initialized to `false`. Used for exploration quests; indicates the associated area has not been marked as explored by this player for this quest.
*   **`m_timer`**: Initialized to `0`. Represents the remaining time (in seconds) for timed quests. A value of 0 typically implies no timer is active or the timer has not started.
*   **`uState`**: Initialized to `QUEST_NEW`. This field tracks the change state of the quest relative to the client or previous server state. `QUEST_NEW` signals that this is a fresh entry, likely requiring synchronization with the client.
*   **`m_reward_choice`**: Initialized to `0`. Tracks which reward choice (e.g., item slot 1–6) the player has selected, if the quest offers multiple choices.
*   **`m_itemcount`**: An array of size `QUEST_OBJECTIVES_COUNT` (4), initialized to `{}` (zero-initialized). Tracks the number of items collected for each of the four possible item objectives.
*   **`m_creatureOrGOcount`**: An array of size `QUEST_OBJECTIVES_COUNT` (4), initialized to `{}` (zero-initialized). Tracks the number of creatures killed or game objects interacted with for each of the four possible kill/objective slots.

## Cross-Unit Boundaries

### Called By: `ChatHandler.CharacterCommands/HandleQuestStatusCommandHelper`
*   **Direction**: `ChatHandler` creates `QuestStatusData`.
*   **Collaboration**: The chat handler uses this struct as a temporary container to calculate and store the current status of a quest for a specific player when a GM issues a debug or query command. The `ChatHandler` populates the fields (likely by querying the `Player` object or `ObjectMgr`) and then reads them back to format a response string for the GM. The struct is local to this command execution and does not persist beyond the function scope.

### Called By: `Player.Main/ChangeQuestsForRace`
*   **Direction**: `Player` creates `QuestStatusData`.
*   **Collaboration**: When a player’s race is changed (a rare administrative or bug-fix action), the `Player` class must re-evaluate which quests are available. It constructs `QuestStatusData` instances to represent the baseline state for quests that are added or modified due to the race change. This ensures that quests previously unavailable due to race restrictions are properly initialized with a clean state (e.g., `QUEST_STATUS_NONE`, zeroed counters) rather than retaining stale data from the previous race context.

## Data Model

**QuestStatusData** does not directly interact with any database tables. It is a transient in-memory structure. The data it holds mirrors the conceptual state stored in the `character_queststatus` table in the database, but the struct itself is not a direct ORM mapping; it is used for immediate runtime calculations and client synchronization packets.

## Notable Implementation Details

1.  **Fixed-Size Arrays**: The struct uses fixed-size arrays (`m_itemcount` and `m_creatureOrGOcount`) of size 4 (`QUEST_OBJECTIVES_COUNT`). This matches the maximum number of item or kill objectives allowed per quest in the underlying DBC/Database schema. Code using this struct must respect this limit and not attempt to access indices >= 4.
2.  **Zero-Initialization**: The constructor explicitly initializes all numeric fields to 0 and boolean fields to false. This is critical because `QuestStatusData` is often used in contexts where uninitialized memory could lead to incorrect quest status reporting (e.g., showing a quest as complete because `m_itemcount` contained garbage data).
3.  **No Validation Logic**: The struct contains no validation. It is purely a data carrier. Any logic checking if `m_itemcount[i] >= required_count` resides in the caller (typically `Player` or `Quest` helper functions).
4.  **`uState` Semantics**: The `uState` field (`QuestUpdateState`) is crucial for network optimization. It allows the server to determine if a quest status packet needs to be sent to the client. `QUEST_NEW` implies a full update is needed, while `QUEST_UNCHANGED` might allow skipping transmission. This struct is part of the mechanism that keeps the client’s quest log synchronized with the server’s authoritative state.

## Member Reference

**QuestStatusData**
Constructor that initializes all member variables to their default "empty" or "new" state. Sets `m_status` to `QUEST_STATUS_NONE`, `m_rewarded` and `m_explored` to `false`, `m_timer` to `0`, `uState` to `QUEST_NEW`, `m_reward_choice` to `0`, and zero-initializes the `m_itemcount` and `m_creatureOrGOcount` arrays.

---

<!-- machine-true, projected from graph.json -->

## Map — QuestStatusData

*Source:* QuestDef.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| QuestStatusData | ctor | — | ChatHandler.CharacterCommands/HandleQuestStatusCommandHelper, Player.Main/ChangeQuestsForRace | — |
