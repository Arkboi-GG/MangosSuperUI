# DBCEnums

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# DBCEnums

**DBCEnums** (`DBCEnums.h`) is a header-only utility unit that defines static constants, enumerations, and helper functions used throughout the `wowvmangos` server to interpret World of Warcraft client-side data structures (DBC files) and enforce server-side limits. It serves two primary purposes:

1.  **Constant Definitions:** It establishes hard-coded numerical limits for player levels, creature levels, and account types (e.g., `PLAYER_MAX_LEVEL`, `TRIAL_MAX_LEVEL`). These constants act as global constraints for validation logic elsewhere in the server.
2.  **Enumeration & Debugging Helpers:** It defines C++ enums that mirror bitflags and indices found in game data (such as Faction Templates, Area Flags, and Spell Families). Crucially, it provides `static` helper functions (`FactionTemplateFlagToString`, `FactionMaskToString`) that convert these numeric flags into human-readable strings. These functions are exclusively intended for logging, debugging, and administrative output, allowing developers to interpret raw integer values from DBC records or memory dumps without needing to manually decode bitmasks.

The unit contains no database interactions, no dynamic state, and no cross-unit dependencies. It is a pure definition module included wherever these specific constants or debug strings are required.

## Member-by-Member Behavior

The members of this unit are divided into two categories: **Level Limits**, which define scalar constants, and **Debug String Converters**, which translate enum values into text.

### Level Limits
The following `#define` macros establish the boundaries for character progression and entity validity:
*   **`PLAYER_MAX_LEVEL` (60):** Represents the maximum level expected by the client for standard progression (Vanilla WoW cap). Used as a default maximum for items or abilities labeled "until max player level."
*   **`MAX_LEVEL` (100):** A safety ceiling for player and pet levels to prevent integer overflows or client instability. This is higher than the gameplay cap to allow for temporary boosts or edge cases without crashing the client.
*   **`PLAYER_STRONG_MAX_LEVEL` (255):** The absolute server-side hard limit. This value is derived from the maximum value of an unsigned 8-bit integer (`uint8`), which is likely the data type used for level storage in packets or database fields. Exceeding this would cause data corruption.
*   **`TRIAL_MAX_LEVEL` (20):** The level cap enforced for trial or restricted accounts.
*   **`CREATURE_MAX_LEVEL` (63):** The highest level a creature can possess in Vanilla WoW content. This reflects the absence of Creature Level Scaling (CLS) data above this threshold in the original game data.

### Debug String Converters
These `static` functions take a numeric flag or mask and return a `const char*` description. They are designed to be called during log generation or error reporting.

*   **`FactionTemplateFlagToString`**: Converts a single bit-flag from the `FactionTemplateFlags` enum into a descriptive string. It handles flags related to AI behavior (e.g., responding to help calls, searching for enemies/friends) and PvP interactions (assisting players, attacking PvP-active players). If the input flag does not match any known constant, it returns `"UNKNOWN"`.
*   **`FactionMaskToString`**: Converts a faction mask value from the `FactionMasks` enum into a string. It identifies whether the mask represents a Player, Alliance member, Horde member, or Monster. Like the previous function, it returns `"UNKNOWN"` for unrecognized values.

*(Note: The MAP lists `FactionTemplateFlagToString#2` and `FactionMaskToString#2`. In the provided source, these correspond to the same logical functions listed above. As they are `static` functions within a header, they are likely referenced multiple times in the build graph or map extraction process, but they represent the same single implementation defined in `DBCEnums.h`.)*

## Cross-Unit Boundaries

This unit has **no outgoing calls** to other units and **no incoming calls** from other units recorded in the MAP. It is a self-contained header. However, in practice, it is `#include`d by numerous other parts of the server (such as AI modules, player handlers, and DBC loaders) to access the constants and debug strings. The lack of entries in the "Calls out" and "Called by" columns indicates that the MAP tracks explicit function calls between compiled units, whereas this unit provides preprocessor definitions and inline/static functions that are resolved at compile time within the including files.

## Data Model

This unit does not interact with any database tables. It defines constants and enums that correspond to data stored in DBC files (binary client data) or hardcoded server logic, but it performs no SQL queries or table accesses.

## Notable Implementation Details

1.  **Static Helper Functions:** The string conversion functions are marked `static`. This means each compilation unit (`.cpp` file) that includes `DBCEnums.h` gets its own private copy of these functions. This avoids linker errors due to multiple definitions but slightly increases binary size. This is a common pattern in older C++ codebases for simple utility functions in headers.
2.  **Bitmask Handling:** The `FactionTemplateFlagToString` function uses a `switch` statement on individual flags. It assumes the caller passes a single bit-flag (e.g., `0x00000001`), not a combined bitmask (e.g., `0x00000003`). If a combined mask is passed, the function will fall through to `"UNKNOWN"` because the `switch` does not iterate through bits. This implies these functions are meant for logging individual flag values extracted from a larger mask, not for decoding the entire mask at once.
3.  **Unknown Values:** Both string converters return `"UNKNOWN"` for unrecognized inputs. This is a safe fallback that prevents crashes but requires developers to check logs for "UNKNOWN" to identify missing enum mappings in future updates.
4.  **Level Discrepancies:** The distinction between `PLAYER_MAX_LEVEL` (60), `MAX_LEVEL` (100), and `PLAYER_STRONG_MAX_LEVEL` (255) is critical. Logic that validates user input should typically use `PLAYER_MAX_LEVEL` or `TRIAL_MAX_LEVEL`. Logic that checks for data integrity or packet parsing bounds should use `PLAYER_STRONG_MAX_LEVEL`. Using `MAX_LEVEL` (100) is a middle-ground safety net for client compatibility.

## Member Reference

**FactionTemplateFlagToString**  
A `static` function that converts a `uint32` flag from the `FactionTemplateFlags` enum into a human-readable `const char*`. It maps specific AI and PvP behavior flags (e.g., `FACTION_TEMPLATE_RESPOND_TO_CALL_FOR_HELP`) to descriptive strings. Returns `"UNKNOWN"` if the flag is not recognized.

**FactionTemplateFlagToString#2**  
Refers to the same `FactionTemplateFlagToString` function defined above. Listed separately in the MAP likely due to multiple references or instantiations in the build graph, but it shares the identical implementation and behavior.

**FactionMaskToString**  
A `static` function that converts a `uint32` mask from the `FactionMasks` enum into a human-readable `const char*`. It maps faction identifiers (Player, Alliance, Horde, Monster) to their respective strings. Returns `"UNKNOWN"` if the mask is not recognized.

**FactionMaskToString#2**  
Refers to the same `FactionMaskToString` function defined above. Listed separately in the MAP likely due to multiple references or instantiations in the build graph, but it shares the identical implementation and behavior.

---

<!-- machine-true, projected from graph.json -->

## Map — DBCEnums

*Source:* DBCEnums.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| FactionTemplateFlagToString | function | — | — | — |
| FactionTemplateFlagToString#2 | function | — | — | — |
| FactionMaskToString | function | — | — | — |
| FactionMaskToString#2 | function | — | — | — |
