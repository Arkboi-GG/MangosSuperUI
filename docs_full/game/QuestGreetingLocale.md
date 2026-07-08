# QuestGreetingLocale

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# QuestGreetingLocale

## Purpose & Responsibilities

`QuestGreetingLocale` is a lightweight data structure within the `wowvmangos` codebase designed to hold localized greeting messages for quest givers. It serves as the value type for the `QuestGreetingLocaleMap`, which caches localized text strings associated with specific NPCs or Game Objects that offer quests.

The structure supports internationalization by storing multiple language variants of a greeting message in a `std::vector<std::string>`. It also stores metadata regarding the visual presentation of the greeting: an `Emote` identifier (defining the animation the quest giver performs) and an `EmoteDelay` (timing information for the emote sequence).

This unit is purely a data container; it contains no logic for loading, saving, or retrieving data from the database. Those responsibilities belong to the `ObjectMgr` class (specifically methods like `LoadQuestGreetings` and `GetQuestGreetingLocale`), which populates maps of `QuestGreetingLocale` instances during server startup.

## Member-by-Member Behavior

### Constructor
**`QuestGreetingLocale()`**
The default constructor initializes the instance with safe default values:
- Sets `Emote` to `0`.
- Sets `EmoteDelay` to `0`.
- Leaves the `Content` vector empty (default constructed).

This ensures that if a `QuestGreetingLocale` object is created without explicit initialization, it represents a neutral state with no emote and no text content.

## Cross-Unit Boundaries

As a simple struct defined in `ObjectMgr.h`, `QuestGreetingLocale` does not actively call into other units. However, it is heavily integrated with the `ObjectMgr` singleton:

1.  **Storage**: Instances of `QuestGreetingLocale` are stored in `ObjectMgr::m_QuestGreetingLocaleMap`, which is an array of two `std::unordered_map<uint32, QuestGreetingLocale>` instances (indexed by `QUESTGIVER_CREATURE` and `QUESTGIVER_GAMEOBJECT`).
2.  **Retrieval**: The `ObjectMgr::GetQuestGreetingLocale(uint32 entry, uint8 type)` method looks up entries in these maps and returns pointers to `QuestGreetingLocale` instances.
3.  **Loading**: The `ObjectMgr::LoadQuestGreetings()` method (defined in the corresponding `.cpp` file, not shown here but referenced in the header) reads from the database and populates these structures.

## Data Model

The `QuestGreetingLocale` struct corresponds to data typically found in the `quest_greeting_locale` table (or similar locale-specific tables depending on the database schema version) in the MaNGOS/WowVM database. While the specific SQL queries are not present in this header file, the structure implies the following data relationships:

-   **Content**: A vector of strings where index `0` is the default (English) text, and subsequent indices correspond to locale IDs (e.g., index `1` for French, `2` for German, etc.). This aligns with the `LOCALE_*` constants used elsewhere in the codebase.
-   **Emote**: An integer representing the emote ID from the game's emote definition tables.
-   **EmoteDelay**: An integer representing the delay in milliseconds or ticks before the emote is triggered or completed.

## Notable Implementation Details

1.  **Locale Indexing Convention**: The comment `// 0 -> default, i -> i-1 locale index` in the `Content` member declaration indicates a specific indexing scheme. The first element (index 0) is always the default language. Subsequent elements map to locale indices shifted by 1. This suggests that when accessing localized text, the caller must adjust the locale index by adding 1 before accessing the vector, or handle the default case separately.
2.  **No Validation**: The struct performs no validation on the `Emote` or `EmoteDelay` values. It is assumed that the loading logic in `ObjectMgr` provides valid data.
3.  **Memory Layout**: As a simple aggregate with a `std::vector`, it is not trivially copyable in the strictest sense due to the dynamic allocation managed by the vector. However, it is move-constructible and move-assignable, which is efficient for map operations.

## Member Reference

**QuestGreetingLocale**
Default constructor for the `QuestGreetingLocale` struct. Initializes `Emote` to `0` and `EmoteDelay` to `0`. The `Content` vector is default-constructed (empty).

---

<!-- machine-true, projected from graph.json -->

## Map — QuestGreetingLocale

*Source:* ObjectMgr.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| QuestGreetingLocale | ctor | — | — | — |
