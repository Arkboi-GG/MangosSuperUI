# TrainerGreetingLocale

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# TrainerGreetingLocale

## Purpose & Responsibilities

`TrainerGreetingLocale` is a lightweight data structure within the `ObjectMgr` subsystem responsible for holding localized greeting text for NPC trainers. In the context of the MaNGOS/WoWVMaNGOS server, when a player interacts with an NPC trainer, the server sends a greeting message. This struct stores the raw string content for that greeting, supporting multiple locales.

It is not a standalone class with behavior; it is a Plain Old Data (struct) used exclusively as the value type in the `TrainerGreetingLocaleMap` (`std::unordered_map<uint32, TrainerGreetingLocale>`), which is a member of the `ObjectMgr` singleton. Its sole responsibility is to aggregate the default and localized strings for a specific trainer entry ID.

## Member-by-Member Behavior

The unit contains only one member: the constructor.

### Constructor
**`TrainerGreetingLocale()`**
This is the default constructor for the struct. It performs no initialization logic other than invoking the default constructors of its member variables. Specifically, it initializes the `Content` vector to an empty state. Because `std::vector` has a default constructor that creates an empty container, no explicit body is required in the source code.

## Cross-Unit Boundaries

`TrainerGreetingLocale` has no outgoing calls and is not directly called by other units as a function. However, it participates in the following collaborations via the `ObjectMgr` class:

1.  **Creation/Population**: The `ObjectMgr` class (specifically the `LoadTrainerGreetings` method, which is declared in `ObjectMgr.h` but implemented in `ObjectMgr.cpp`) reads data from the database and populates instances of `TrainerGreetingLocale`. These instances are then stored in the `m_TrainerGreetingLocaleMap` member of `ObjectMgr`.
2.  **Retrieval**: The `ObjectMgr::GetTrainerGreetingLocale` method retrieves a pointer to a `TrainerGreetingLocale` instance from `m_TrainerGreetingLocaleMap` using a trainer entry ID. This pointer is typically passed to UI or network handling code (e.g., in `NPCHandler.cpp` or similar interaction handlers) to send the appropriate localized string to the client.

## Data Model

`TrainerGreetingLocale` itself does not interact with the database. However, the data it holds is sourced from the `trainer_greeting_locale` table (or similar, depending on the specific database schema version, often part of the `locales_*` family). The `ObjectMgr::LoadTrainerGreetings` method queries this table to populate the `Content` vector.

The `Content` vector maps locale indices to strings:
*   Index `0`: Default English (enUS) text.
*   Index `i` (where `i > 0`): Text for locale index `i-1`.

## Notable Implementation Details

1.  **Locale Indexing Convention**: The `Content` vector uses a specific indexing convention documented in the comment: `0 -> default, i -> i-1 locale index`. This means that if a locale constant is `LOCALE_deDE` (which might have an integer value of 1), the corresponding string is stored at `Content[2]`. The default English text is always at `Content[0]`. This offset is critical for correct retrieval logic in consumers of this struct.
2.  **No Emote Data**: Unlike `QuestGreetingLocale`, which includes `Emote` and `EmoteDelay` fields, `TrainerGreetingLocale` contains only text. Trainer greetings in this implementation do not support associated emotes via this structure.
3.  **Memory Layout**: As a struct containing a `std::vector`, it involves dynamic memory allocation for the string storage. This is managed automatically by the vector's destructor when the `TrainerGreetingLocale` instance is removed from the map or when the map is cleared.

## Member Reference

**TrainerGreetingLocale**
Default constructor for the `TrainerGreetingLocale` struct. Initializes the `Content` vector to an empty state. No explicit body is defined in the source.

---

<!-- machine-true, projected from graph.json -->

## Map — TrainerGreetingLocale

*Source:* ObjectMgr.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| TrainerGreetingLocale | ctor | — | — | — |
