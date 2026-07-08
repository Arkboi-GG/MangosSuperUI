# QuestLocale

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# QuestLocale

**Purpose & Responsibilities**

`QuestLocale` is a lightweight aggregate struct defined in `QuestDef.h` that holds the localized text strings for a single quest. It serves as the container for all human-readable content associated with a quest definition, such as titles, descriptions, objectives, and dialogue text. Unlike the primary `Quest` class, which stores game logic, IDs, rewards, and requirements, `QuestLocale` is dedicated exclusively to presentation-layer data. It supports multiple languages by storing each text field as a `std::vector<std::string>`, where each index in the vector corresponds to a specific locale ID.

The struct is designed to be populated by the localization system (typically via `ObjectMgr`) and referenced by the quest handling logic to provide the correct language strings to clients based on their locale settings.

## Member-by-Member Behavior

### Construction

**QuestLocale**
The default constructor initializes the `ObjectiveText` member. Because `ObjectiveText` is a nested structure (`std::vector< std::vector<std::string> >`), the constructor explicitly resizes the outer vector to `QUEST_OBJECTIVES_COUNT` (defined as 4). This pre-allocation ensures that the structure for up to four distinct objective text entries exists immediately upon creation, preventing reallocation overhead during population. The other string vectors (`Title`, `Details`, etc.) are default-constructed as empty vectors.

### Data Members

The struct contains seven primary members, all of type `std::vector<std::string>` or `std::vector< std::vector<std::string> >`:

*   **Title**: Stores the localized title of the quest.
*   **Details**: Stores the detailed description text shown when a player accepts or views the quest details.
*   **Objectives**: Stores the main objective summary text.
*   **OfferRewardText**: Stores the text displayed when the quest giver offers the reward.
*   **RequestItemsText**: Stores the text displayed when the player requests items or turns in the quest.
*   **EndText**: Stores the final text displayed upon quest completion.
*   **ObjectiveText**: A vector of vectors. The outer vector holds up to `QUEST_OBJECTIVES_COUNT` (4) entries. Each inner vector holds the localized text for that specific objective step. This allows for complex quests with multiple distinct objective lines to have their own localized strings.

## Cross-Unit Boundaries

According to the provided MAP, `QuestLocale` has **no outgoing calls** to other units and is **not called by** any other units listed in the cross-reference. However, in the broader context of the `wowvmangos` codebase (evident from the `QuestDef.h` header):

*   **Called by `ObjectMgr`**: While not explicitly listed in the MAP's "Called by" column for this specific partial, the `Quest` class (defined in the same header) has `friend class ObjectMgr`. The `ObjectMgr` unit is responsible for loading quest data from the database and populating both `Quest` instances and their associated `QuestLocale` structures. The `QuestLocale` struct is typically embedded within or associated with the `Quest` object during this loading process.
*   **Used by `Player` and `WorldSession`**: Units handling player interactions (such as `Player` or session handlers) will access the `QuestLocale` data through the `Quest` object to send localized packets (e.g., `SMSG_QUESTGIVER_QUEST_DETAILS`) to the client.

## Data Model

The `QuestLocale` struct itself does not interact directly with database tables. It is a pure in-memory data structure. The data it holds originates from the `quest_template_locale` table (or similar localization tables depending on the specific database schema version), but the struct contains no SQL queries or direct database access logic. The MAP confirms no tables are touched by this unit.

## Notable Implementation Details

1.  **Pre-allocation of ObjectiveText**: The constructor explicitly calls `ObjectiveText.resize(QUEST_OBJECTIVES_COUNT)`. This is a performance optimization. Since `QUEST_OBJECTIVES_COUNT` is a small constant (4), resizing once at construction avoids dynamic memory allocation during the critical path of quest loading. It assumes that the maximum number of objectives is known and fixed at compile time.
2.  **Vector-based Localization**: All text fields are `std::vector<std::string>`. This design implies that the index of the string in the vector corresponds to a locale ID (e.g., index 0 for English, index 1 for French, etc.). The code does not enforce bounds checking or locale validation within the struct itself; that responsibility lies with the code that populates and accesses these vectors.
3.  **No Validation**: The struct provides no methods to validate the content of the strings (e.g., checking for null terminators, length limits, or encoding issues). It is a passive data holder.
4.  **Separation of Concerns**: By separating `QuestLocale` from the `Quest` class, the codebase isolates large, variable-length string data from the core quest logic. This likely aids in memory management and cache efficiency, as the core `Quest` data (IDs, levels, rewards) is accessed far more frequently than the full text bodies.

## Member Reference

**QuestLocale**
Default constructor. Initializes the `ObjectiveText` member by resizing its outer vector to `QUEST_OBJECTIVES_COUNT` (4). All other string vector members are default-initialized as empty.

---

<!-- machine-true, projected from graph.json -->

## Map — QuestLocale

*Source:* QuestDef.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| QuestLocale | ctor | — | — | — |
