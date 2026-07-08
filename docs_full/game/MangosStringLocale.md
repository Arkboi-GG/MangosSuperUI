# MangosStringLocale

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# MangosStringLocale

**Purpose & Responsibilities**

`MangosStringLocale` is a lightweight data structure within the `wowvmangos` codebase designed to hold localized text strings and associated metadata for custom server-side messages. It serves as the value type in the `MangosStringLocaleMap` (an `std::unordered_map<int32, MangosStringLocale>`), allowing the `ObjectMgr` singleton to cache and retrieve server-generated strings (such as error messages, announcements, or custom NPC dialogue) by a unique integer ID.

Unlike standard game data structures that might rely on static C++ arrays or direct database lookups for every access, `MangosStringLocale` supports multiple language variants. It stores these variants in a `std::vector<std::string>` named `Content`, where index `0` holds the default (English) text, and subsequent indices correspond to specific locale offsets. Additionally, it carries metadata regarding how the string should be presented: an optional sound effect (`SoundId`), a chat type (`Type`), a specific language identifier (`LanguageId`), and an emote animation (`Emote`).

This unit is purely a data holder; it contains no logic for loading, saving, or formatting data. Its sole responsibility is to aggregate these fields into a single contiguous memory block for efficient retrieval by the `ObjectMgr`.

## Member-by-Member Behavior

The unit consists of a single member: the constructor.

### **MangosStringLocale** (Constructor)

The default constructor initializes the `MangosStringLocale` instance with safe, neutral defaults. This ensures that if a string entry is created but not fully populated from the database, it will not cause undefined behavior when accessed.

*   **Initialization List:**
    *   `SoundId` is set to `0`, indicating no sound effect should play.
    *   `Type` is set to `0`, representing the default chat/message type.
    *   `LanguageId` is set to `LANG_UNIVERSAL`, ensuring the text is understood by all players regardless of their client language settings.
    *   `Emote` is set to `0`, meaning no emote animation is triggered.
*   **Body:**
    *   The body is empty. The `Content` vector is implicitly default-constructed as an empty vector. In the context of `ObjectMgr::LoadMangosStrings`, this vector is typically resized and populated immediately after construction during the database loading phase.

## Cross-Unit Boundaries

`MangosStringLocale` does not actively call into other units. It is a passive data structure. However, it is heavily integrated with the following units:

*   **Called by `ObjectMgr` (ObjectMgr.cpp):**
    *   The `ObjectMgr` class owns the `m_MangosStringLocaleMap`. During server startup or reload, `ObjectMgr::LoadMangosStrings` queries the database (typically the `mangos_string` table) and constructs `MangosStringLocale` instances.
    *   The `ObjectMgr::GetMangosStringLocale` method returns a pointer to a `MangosStringLocale` stored in this map, allowing other parts of the server (e.g., `ChatHandler`, `ScriptMgr`) to access the localized text and metadata.
*   **Dependent on `Common.h` / `SharedDefines.h`:**
    *   The type `Language` used for `LanguageId` is defined in shared headers.
    *   The constant `LANG_UNIVERSAL` used in the constructor initialization is defined in these shared headers.

## Data Model

While `MangosStringLocale` itself does not contain SQL queries, it maps directly to the `mangos_string` table in the MaNGOS database schema. Based on standard MaNGOS conventions and the fields present in the struct, the mapping is as follows:

*   **Table:** `mangos_string`
*   **Columns mapped:**
    *   `entry` (INT): Maps to the key in `MangosStringLocaleMap`.
    *   `content_default` (TEXT): Maps to `Content[0]`.
    *   `content_locX` (TEXT): Maps to `Content[X+1]` for various locales.
    *   `sound` (INT): Maps to `SoundId`.
    *   `type` (INT): Maps to `Type`.
    *   `language` (INT): Maps to `LanguageId`.
    *   `emote` (INT): Maps to `Emote`.

Note: The provided source code does not include the SQL schema dump, so column types and constraints are inferred from the C++ member types and standard MaNGOS database structures.

## Notable Implementation Details

1.  **Locale Indexing Convention:**
    The `Content` vector uses a specific indexing convention: index `0` is the default language, and index `i` corresponds to locale index `i-1`. This offset is handled by the caller (usually `ObjectMgr::GetMangosString`), which adjusts the locale index before accessing the vector. This design allows for a consistent "default first" approach, simplifying fallback logic if a specific locale translation is missing.

2.  **Memory Layout:**
    As a struct containing a `std::vector`, `MangosStringLocale` has a dynamic memory footprint. The vector's internal buffer is allocated on the heap. This is acceptable because these objects are long-lived (cached for the duration of the server session) and accessed infrequently compared to per-tick game logic.

3.  **No Validation:**
    The constructor does not validate the inputs (since it takes none). Validation of the loaded data (e.g., ensuring `SoundId` refers to a valid sound entry) is performed elsewhere, likely in `ObjectMgr::LoadMangosStrings` or when the string is actually used.

4.  **Thread Safety:**
    `MangosStringLocale` instances are immutable after being inserted into the `m_MangosStringLocaleMap` (assuming the map is not modified concurrently). Access to the map is protected by the `ObjectMgr`'s internal locking mechanisms (if any) or by the fact that loading happens during initialization/reload phases when the server is otherwise paused or synchronized.

## Member Reference

**MangosStringLocale**
The default constructor for the `MangosStringLocale` struct. It initializes `SoundId` to `0`, `Type` to `0`, `LanguageId` to `LANG_UNIVERSAL`, and `Emote` to `0`. The `Content` vector is left empty, to be populated later by the database loader. This ensures a safe default state for any newly instantiated string entry.

---

<!-- machine-true, projected from graph.json -->

## Map — MangosStringLocale

*Source:* ObjectMgr.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| MangosStringLocale | ctor | — | — | — |
