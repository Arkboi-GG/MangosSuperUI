# BroadcastText

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# BroadcastText

**Purpose & Responsibilities**

`BroadcastText` is a lightweight data structure within `ObjectMgr.h` that represents a single localized text entry used for non-player character (NPC) dialogue, quest text, and other broadcast messages in the World of Warcraft emulation environment. It encapsulates the raw text strings for both male and female speakers across multiple locales, along with associated metadata such as sound effects, chat types, languages, and emote sequences.

This struct serves as the value type in the `BroadcastTextLocaleMap` (`std::unordered_map<uint32, BroadcastText>`), allowing the server to quickly retrieve pre-loaded text data by its unique entry ID. It does not perform I/O or complex logic itself; rather, it provides a clean interface (`GetText`) for resolving the correct string variant based on the speaker's gender and the viewer's locale settings.

## Member-by-Member Behavior

### Construction and Initialization

**`BroadcastText()`**
The default constructor initializes all numeric fields to zero and prepares the text storage vectors. Specifically:
- Sets `entry`, `soundId`, `chatType`, `languageId`, and all emote-related fields (`emoteId1-3`, `emoteDelay1-3`) to `0`.
- Resizes the `maleText` and `femaleText` vectors to hold `LOCALE_enUS + 1` elements. This ensures that the base English (US) text is always accessible at index `0`, and subsequent indices correspond to other supported locales. The vectors are initialized with empty strings.

### Text Retrieval Logic

**`GetText(int locale_index, uint8 gender, bool forceGender)`**
This method resolves the appropriate text string from the stored vectors based on three criteria: the requested locale, the speaker's gender, and whether gender-specific text is forced.

1. **Female Text Priority**:
   - If the `gender` is `GENDER_FEMALE` or `GENDER_NONE`, AND either `forceGender` is true OR the default female text (`femaleText[LOCALE_enUS]`) is not empty, the method attempts to return female-specific text.
   - It first checks if a localized female string exists for the requested `locale_index` (at `femaleText[locale_index + 1]`). Note the offset: index `0` is English, so locale index `i` maps to vector index `i+1`.
   - If the localized female string is missing or empty, it falls back to the default English female string (`femaleText[0]`).

2. **Male Text Fallback**:
   - If the conditions for female text are not met (e.g., `gender` is `GENDER_MALE`, or female text is empty and `forceGender` is false), the method defaults to male text.
   - It checks for a localized male string at `maleText[locale_index + 1]`.
   - If unavailable, it returns the default English male string (`maleText[0]`).

**Notable Implementation Detail**: The logic treats `GENDER_NONE` similarly to `GENDER_FEMALE` if female text exists. This allows NPCs with unspecified gender to use female dialogue if available, otherwise falling back to male. The `forceGender` parameter allows callers to explicitly request female text even if the entity is technically male, or to bypass the empty-check fallback.

## Cross-Unit Boundaries

`BroadcastText` is a passive data holder. It does not call out to other units. However, it is consumed by several key subsystems:

- **`GossipDef/SendTalking#2`**: Uses `GetText` to retrieve dialogue for gossip menus sent to players.
- **`Player.Main/PrepareGossipMenu`**: Retrieves text for preparing gossip interactions.
- **`Player.Main/SendPreparedQuest`**: Fetches quest-related broadcast text.
- **`WorldSession.QueryHandler/HandleNpcTextQueryOpcode`**: Handles direct queries for NPC text, likely for debugging or specific client requests.

These callers pass the locale index, gender, and force-flag to `GetText`, receiving a `const std::string&` reference to the resolved text.

## Data Model

`BroadcastText` does not directly interact with database tables. It is populated by `ObjectMgr::LoadBroadcastTexts` (not shown in this unit's source but referenced in the MAP), which reads from the `broadcast_text` table in the world database. The struct mirrors the columns of that table:

- `entry`: Primary key.
- `maleText` / `femaleText`: Vectors storing localized strings for each locale.
- `soundId`: ID of the sound effect to play.
- `chatType`: Type of chat message (e.g., say, yell, emote).
- `languageId`: Language of the text (e.g., Common, Orcish).
- `emoteId1-3` / `emoteDelay1-3`: Sequences of emotes to perform while speaking.

## Notable Implementation Details

1. **Locale Indexing Offset**: The `maleText` and `femaleText` vectors use an offset indexing scheme. Index `0` always holds the default English (US) text. Subsequent locales are stored at `index + 1`. This is critical for `GetText` to correctly access `femaleText[locale_index + 1]`. Callers must ensure `locale_index` is within bounds relative to the vector size.

2. **Empty String Handling**: The constructor initializes vectors with empty strings. `GetText` checks for empty strings to determine fallback behavior. If a localized string is empty, it falls back to the default English string for that gender. If the default English string is also empty, it returns an empty string.

3. **Gender Ambiguity**: `GENDER_NONE` is treated as potentially female. This is a design choice to allow flexible NPC dialogue assignment. If female text is absent, it seamlessly falls back to male text.

4. **Const Correctness**: `GetText` is `const`, ensuring it does not modify the `BroadcastText` instance. It returns a `const std::string&`, avoiding unnecessary copies.

## Member Reference

**`BroadcastText()`**  
Default constructor. Initializes all numeric fields to zero. Resizes `maleText` and `femaleText` vectors to `LOCALE_enUS + 1` elements, filled with empty strings.

**`GetText(int locale_index, uint8 gender, bool forceGender)`**  
Returns a `const std::string&` to the appropriate text string. Prioritizes female text if `gender` is `GENDER_FEMALE` or `GENDER_NONE` (and female text exists or `forceGender` is true). Checks for localized text at `vector[locale_index + 1]`; falls back to `vector[0]` (English) if localized text is missing or empty. Defaults to male text if female conditions are not met.

---

<!-- machine-true, projected from graph.json -->

## Map — BroadcastText

*Source:* ObjectMgr.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| BroadcastText | ctor | — | ObjectMgr/LoadBroadcastTexts | — |
| GetText | method | — | GossipDef/SendTalking#2, Player.Main/PrepareGossipMenu, Player.Main/SendPreparedQuest, WorldSession.QueryHandler/HandleNpcTextQueryOpcode | — |
