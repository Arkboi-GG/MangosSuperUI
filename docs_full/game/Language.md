<!-- provenance: failed-members -->
# Language

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Language.h

## Purpose & Responsibilities

`Language.h` defines the `MangosStrings` enumeration, which serves as the central registry of localized string identifiers for the WoWVMaNGOS server. It contains no executable logic, data storage, or runtime behavior. Instead, it provides a compile-time mapping between human-readable symbolic names (e.g., `LANG_PLAYER_SAVED`) and integer IDs (e.g., `14`). These IDs are consumed by other units in the codebase to retrieve corresponding text strings from localization resources (typically database tables or hardcoded fallbacks) for display to players, game masters, or console operators.

The enumeration is organized by permission level and functional domain:
- **Level 0–4 Chat/Command Strings**: Messages triggered by chat commands, ranging from basic system notifications (`LANG_SYSTEMMESSAGE`) to high-security administrative actions (`LANG_ACCOUNT_DELETED`).
- **In-Game System Messages**: Notifications for gameplay events such as battleground status (`LANG_BG_AV_TOWER_TAKEN`), guild ranks (`LANG_ALI_PRIVATE`), and honor titles.
- **Debug & Diagnostic Strings**: Messages used for internal logging, waypoint debugging (`LANG_WAYPOINT_NOTFOUND`), and script engine feedback (`LANG_SCRIPTS_RELOADED_OK`).
- **Custom/Extension Ranges**: Reserved blocks for custom patches (`11000+`) and database-driven scripts (`2000000000+`).

This unit ensures that all string references throughout the server code use consistent, typed constants rather than magic numbers, facilitating maintenance and localization updates.

## Member-by-Member Behavior

The unit contains a single member: the `MangosStrings` enumeration. Its behavior is purely declarative. Each enumerator assigns a unique integer ID to a symbolic name. Comments within the source code indicate the intended context for each ID (e.g., `// for chat commands`, `// level 0 chat`, `// log`).

Key groupings include:
- **Command Feedback**: IDs like `LANG_NO_CMD` (6), `LANG_CMD_SYNTAX` (10), and `LANG_COMMAND_UNAVAILABLE` (50) provide immediate feedback to users executing invalid or unauthorized commands.
- **State Changes**: Pairs of IDs often represent the actor’s perspective vs. the target’s perspective. For example, `LANG_YOU_CHANGE_HP` (118) is sent to the GM performing the action, while `LANG_YOURS_HP_CHANGED` (119) is sent to the player whose HP was modified.
- **Battleground/Arena Logic**: A dense block of IDs (650–799) handles specific announcements for Alterac Valley (`LANG_BG_AV_*`) and general battleground queue/status messages (`LANG_BG_QUEUE_ANNOUNCE_SELF`).
- **Waypoint System**: IDs 220–256 support the visual waypoint editor and AI pathing diagnostics, providing detailed error messages when paths are missing or malformed.

## Cross-Unit Boundaries

As a header-only definition file, `Language.h` has no outgoing calls. It is exclusively included by other units that need to reference these string IDs. Based on the naming conventions and typical MaNGOS architecture, the following units likely consume this enumeration:

- **Chat Command Handlers** (e.g., `ChatCommands.cpp`, `AccountCommands.cpp`): These units use IDs like `LANG_PLAYER_SAVED` or `LANG_ACCOUNT_CREATED` to send responses to the command issuer.
- **World Session/Player Classes** (e.g., `WorldSession.cpp`, `Player.cpp`): These units use IDs for system messages, such as `LANG_YOU_IN_COMBAT` or `LANG_NOT_ENOUGH_GOLD`.
- **Battleground Managers** (e.g., `Battleground.cpp`, `AV.cpp`): These units use IDs in the 650–799 range for node capture announcements and queue updates.
- **Script Engine/Database Modules** (e.g., `DBScripts.cpp`): These units use IDs in the 2000000000+ range for dynamic string retrieval from the `db_script_string` table.

The direction of dependency is strictly inbound: other units include `Language.h` to gain access to the `MangosStrings` enum. No data flows out of this unit at runtime; it merely provides type-safe constants.

## Data Model

This unit does not directly interact with any database tables. However, the integer values defined in `MangosStrings` serve as foreign keys or lookup indices for localization tables in the live database, typically:
- `locale_command` or `locale_chat`: Stores translated text for command feedback.
- `locale_npc_text` or `locale_quest`: May reference some IDs for NPC dialogue.
- `db_script_string`: Specifically referenced by the comment `// db_script_string table index 2000000000-2000009999`, indicating that IDs in this range are dynamically resolved from this table.

No schema is provided for these tables, and this unit does not execute SQL queries.

## Notable Implementation Details

1. **Reserved Ranges**: The enumeration explicitly reserves large blocks of IDs for future use or specific subsystems (e.g., `10000-10999` for non-official patches, `11000-11999` for custom patches). This prevents ID collisions when integrating third-party scripts or custom content.
2. **Unused/Deprecated IDs**: Several IDs are commented out as `// not used` (e.g., `19`, `20`, `57-60`). Maintainers should avoid reusing these IDs unless they are certain the associated functionality is permanently removed, as old binaries or scripts might still reference them.
3. **Perspective Pairs**: Many message pairs follow a strict convention: `LANG_YOU_*` for the actor and `LANG_YOURS_*` for the target. This distinction is critical for correct localization and user experience. Swapping these IDs would result in confusing messages (e.g., telling a GM "Your HP changed" instead of "You changed [Player]'s HP").
4. **Max Value Constraint**: The highest defined ID is `2147483647` (implied by the comment `max index`), which is the maximum value for a signed 32-bit integer. This suggests the underlying storage mechanism for these IDs is likely a `int32_t` or similar signed type. Exceeding this limit would cause overflow issues.

## Member Reference

**MangosStrings**  
An enumeration defining integer constants for all localized string IDs used in the server. It includes IDs for chat commands (levels 0–4), in-game system messages, battleground announcements, waypoint diagnostics, and custom/scripted strings. Each enumerator maps a symbolic name (e.g., `LANG_PLAYER_SAVED`) to a unique integer (e.g., `14`). The enumeration is organized by permission level and functional domain, with reserved ranges for future expansion and custom content. It does not contain executable logic but serves as the authoritative source for string identification across the codebase.

---

<!-- machine-true, projected from graph.json -->

## Map — Language

*Source:* Language.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|

---

<!-- verify: failed-members | invented: MangosStrings -->
