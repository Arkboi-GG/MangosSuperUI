# Common

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Common

**Purpose & Responsibilities**

`Common` is a foundational utility module providing low-level type definitions, macros, and helper functions used throughout the `wowvmangos` server. It serves three primary roles:

1.  **Locale Management:** It provides the mapping between human-readable locale strings (e.g., "enUS"), internal `LocaleConstant` indices used for DBC (data block chunk) file lookups, and `DBLocaleConstant` indices used for database storage. It handles specific quirks of the Vanilla WoW client, such as the absence of Russian DBC data and the mapping of British English to American English.
2.  **Numeric Safety:** It offers `finiteAlways`, a safeguard function that converts infinite or NaN floating-point values to zero, preventing propagation of invalid numeric states into game logic like honor calculations or player saves.
3.  **Platform Abstraction & Utilities:** It defines platform-specific format specifiers for integer printing (`I32FMT`, `I64FMT`), standardizes time constants (`MINUTE`, `HOUR`), defines account security levels (`AccountTypes`), and provides a custom string duplication function (`mangos_strdup`) compatible with the server's memory management expectations.

The module does not access any database tables directly; it operates purely on in-memory data structures and standard library functions.

## Member-by-Member Behavior

### Locale Resolution

**GetLocaleByName**
This function resolves a locale identifier string (such as `"enUS"` or `"koKR"`) into its corresponding `LocaleConstant` enum value. It iterates through the static `fullLocaleNameList` array, which maps string names to locale indices. Notably, it treats `"enGB"` identically to `"enUS"`, returning `LOCALE_enUS` for both. If the input string does not match any known locale, it defaults to `LOCALE_enUS`. This function is critical during authentication (`AuthSocket/_HandleLogonProof__PostRecv`) to determine the client's language preference early in the connection process, and in chat validation (`ChatHandler.Chat/isValidChatMessage`) to ensure messages adhere to locale-specific rules.

**GetDbcLocaleFromDbLocale**
This function translates a `DBLocaleConstant` (an index derived from database records) into a `LocaleConstant` (an index used for accessing localized text in DBC files). The mapping is not strictly 1:1 due to historical client limitations:
*   Most locales map directly (e.g., `DB_LOCALE_frFR` -> `LOCALE_frFR`).
*   `DB_LOCALE_esMX` maps to `LOCALE_esMX`, though the code comments note that while the index exists in DBC files, there was no official Vanilla client for this locale.
*   `DB_LOCALE_ruRU` explicitly maps to `LOCALE_enUS`. The comment explains that Russian DBC files did not exist for Vanilla WoW; thus, Russian-speaking clients receive English text. This prevents the server from attempting to load non-existent Russian localization data.

### Numeric Safety

**finiteAlways**
An inline function that checks if a `float` is finite using `std::isfinite`. If the value is infinite (positive or negative) or NaN, it returns `0.0f`; otherwise, it returns the original value. This is a defensive programming measure used extensively in systems where floating-point precision errors or division by zero could occur, such as:
*   **Honor System:** `HonorMgr` uses it when flushing rank points, decaying inactive ranks, and saving honor data to prevent corrupting the database with `INF` or `NaN` values.
*   **Player Persistence:** `Player.Main/SaveToDB` uses it to ensure coordinates or other float-based stats saved to the database are valid numbers.
*   **Escort Commands:** `ChatHandler.CreatureCommands` uses it when modifying waypoint coordinates for escort quests, ensuring invalid inputs don't break pathfinding.

### Memory & String Utilities

**mangos_strdup**
A wrapper around `strdup` that allocates memory using `new char[]` instead of `malloc`. This ensures consistency with the rest of the codebase's memory management, which expects strings allocated this way to be freed via `delete[]`. It is used by `ObjectMgr` when loading creature information and by `World` and `ChatHandler` when sending system messages, ensuring that dynamically created strings are managed correctly within the server's lifecycle.

## Cross-Unit Boundaries

*   **AuthSocket:** Calls `GetLocaleByName` during the login proof phase (`_HandleLogonProof__PostRecv`) to establish the session's locale context immediately upon connection.
*   **ChatHandler:**
    *   Calls `GetDbcLocaleFromDbLocale` in `isValidChatMessage` to validate chat content against locale-specific constraints.
    *   Calls `mangos_strdup` in `SendGlobalSysMessage` and `SendSysMessage` to duplicate message strings before transmission, likely to manage lifetime across asynchronous network sends.
    *   Calls `finiteAlways` in `HandleEscortAddWpCommand` and `HandleEscortModifyWpCommand` to sanitize coordinate inputs provided by administrators via console commands.
*   **ObjectMgr:** Calls `mangos_strdup` in `LoadCreatureInfo` to store creature name or description strings loaded from DBC files into the server's memory cache.
*   **World:** Calls `mangos_strdup` in `SendGlobalText` to duplicate global announcement strings.
*   **HonorMgr:** Calls `finiteAlways` in `FlushRankPoints`, `InactiveDecayRankPoints`, `Save`, and `SaveStoredData` to ensure all honor-related floating-point calculations result in valid database-storable numbers.
*   **Player.Main:** Calls `finiteAlways` in `SaveToDB` to sanitize player position or other float attributes before persistence.

## Data Model

This unit does not interact with any database tables. All data operations are performed on in-memory arrays (`localeNames`, `fullLocaleNameList`) and standard library types.

## Notable Implementation Details

*   **Russian Locale Handling:** The explicit mapping of `DB_LOCALE_ruRU` to `LOCALE_enUS` in `GetDbcLocaleFromDbLocale` is a critical compatibility layer. Without this, the server might attempt to load Russian DBC files that do not exist in the Vanilla client data, potentially causing crashes or missing text. This reflects the historical reality that Russian localization was added in later expansions (TBC/WotLK), not Vanilla.
*   **Memory Management Contract:** `mangos_strdup` uses `new char[]`, implying that any caller must use `delete[]` to free the returned pointer. Using `free()` would be undefined behavior. This is a subtle but important contract for maintainers adding new string-handling code.
*   **Default Locale Fallback:** Both `GetLocaleByName` and `GetDbcLocaleFromDbLocale` default to `LOCALE_enUS` on failure or unknown input. This ensures the server always has a valid locale context, preventing null-pointer dereferences or out-of-bounds array accesses in downstream code that assumes a valid `LocaleConstant`.
*   **Time Constants:** The `TimeConstants` enum defines standard time intervals in seconds (e.g., `MINUTE = 60`). These are used throughout the codebase for timers, cooldowns, and expiration checks. Note that `MONTH` is defined as 30 days and `YEAR` as 12 months, which are approximations suitable for game logic but not calendar-accurate.

## Member Reference

**GetLocaleByName**: Resolves a locale string (e.g., "enUS") to a `LocaleConstant` index, defaulting to `LOCALE_enUS` for unknowns or "enGB".

**GetDbcLocaleFromDbLocale**: Maps a `DBLocaleConstant` to a `LocaleConstant`, notably forcing Russian (`DB_LOCALE_ruRU`) to English (`LOCALE_enUS`) due to lack of Vanilla Russian DBC files.

**finiteAlways**: Returns the input float if finite, otherwise returns `0.0f`, used to prevent NaN/Inf propagation in honor and player save logic.

**mangos_strdup**: Allocates a new string using `new char[]` and copies the source, requiring `delete[]` for cleanup; used for safe string duplication in object loading and messaging.

---

<!-- machine-true, projected from graph.json -->

## Map — Common

*Source:* Common.cpp, Common.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GetLocaleByName | function | — | AuthSocket/_HandleLogonProof__PostRecv | — |
| GetDbcLocaleFromDbLocale | function | — | ChatHandler.Chat/isValidChatMessage | — |
| finiteAlways | function | — | ChatHandler.CreatureCommands/HandleEscortAddWpCommand, ChatHandler.CreatureCommands/HandleEscortModifyWpCommand, HonorMgr/FlushRankPoints, HonorMgr/InactiveDecayRankPoints, HonorMgr/Save, HonorMgr/SaveStoredData, Player.Main/SaveToDB | — |
| mangos_strdup | function | — | ChatHandler.Chat/SendGlobalSysMessage, ChatHandler.Chat/SendSysMessage, ObjectMgr/LoadCreatureInfo, World/SendGlobalText | — |
