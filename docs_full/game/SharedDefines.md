# SharedDefines

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# SharedDefines

**SharedDefines.h** is the central repository for static constants, enumerations, and lightweight utility functions that define the fundamental data model of the *wowvmangos* server. It establishes the canonical integer values for game entities (races, classes, spells, maps), protocol opcodes (chat, mail, trade), and client-side animation/emote states. Because these values are hardcoded to match the World of Warcraft client’s Data Block Cache (DBC) files and network protocol specifications, this header acts as the single source of truth for type safety and value consistency across the entire codebase.

The unit contains no database interactions; it operates entirely in memory using compile-time constants and simple switch-based lookups. Its primary responsibility is to prevent "magic numbers" from scattering through the logic layers by providing named, typed accessors for game rules.

## Purpose & Responsibilities

1.  **Game Entity Definition**: Defines the integer IDs for Races, Classes, Powers, Stats, and Items. These enums (`Races`, `Classes`, `Powers`) correspond directly to indices in client DBC files (`ChrRaces.dbc`, `ChrClasses.dbc`).
2.  **Protocol & State Constants**: Defines constants for chat message types (`ChatMsg`), mail responses (`MailResponseResult`), trade statuses (`TradeStatus`), and authentication results (`ResponseCodes`). These ensure the server sends valid opcodes and status codes expected by the client.
3.  **Animation & Emote Mapping**: Provides extensive enums for `Anim` (animation IDs) and `Emote` (emote IDs), which are critical for synchronizing character movement and visual feedback with the client.
4.  **Utility Lookups**: Offers inline functions to convert between related game concepts, such as mapping a `QuestSort` ID to a `Class` or `Skill`, or converting a `BattleGroundTypeId` to a `MapId`.
5.  **Bitmask Definitions**: Defines bitmasks for team affiliations (`RACEMASK_ALLIANCE`, `RACEMASK_HORDE`) and class capabilities (`CLASSMASK_WAND_USERS`), enabling efficient bitwise checks for faction or class restrictions.

## Member-by-Member Behavior

### Gender and Power Conversion
These functions provide human-readable strings for internal numeric identifiers, primarily for debugging or admin command output.

*   **GenderToString**: Converts a `uint32` gender value (`GENDER_MALE`, `GENDER_FEMALE`, `GENDER_NONE`) to a string ("Male", "Female", "None"). Returns "UNKNOWN" for invalid inputs.
*   **GenderToString#2**: An overloaded or duplicate declaration (likely due to macro expansion or namespace issues in the original source structure, though only one definition exists in the provided snippet). It behaves identically to `GenderToString`.
*   **PowerToString**: Converts a `uint32` power type (`POWER_MANA`, `POWER_RAGE`, etc.) to its string representation ("Mana", "Rage", "Focus", "Energy", "Happiness", "Health"). Returns "UNKNOWN" for invalid inputs. Note that `POWER_HEALTH` is defined as `0xFFFFFFFE` (-2), which is handled explicitly.
*   **PowerToString#2**: Similar to `GenderToString#2`, this appears to be a duplicate or overloaded signature handling the same conversion logic.

### Quest and Skill Mapping
These functions bridge the gap between quest categorization and player progression systems.

*   **ClassByQuestSort**: Takes a `QuestSort` ID (from `QuestSort.dbc`) and returns the corresponding `Class` enum if the quest is class-specific (e.g., `QUEST_SORT_WARLOCK` -> `CLASS_WARLOCK`). Returns `0` if the sort ID does not map to a specific class (e.g., general quests or profession quests).
*   **SkillByLockType**: Maps a `LockType` (e.g., `LOCKTYPE_PICKLOCK`, `LOCKTYPE_HERBALISM`) to the required `SkillType` (e.g., `SKILL_LOCKPICKING`, `SKILL_HERBALISM`). This is used to determine if a player has the necessary skill to interact with a locked container or resource node.
*   **SkillByQuestSort**: Maps a `QuestSort` ID to a `SkillType` for profession-related quests (e.g., `QUEST_SORT_HERBALISM` -> `SKILL_HERBALISM`). Returns `0` if the quest sort is not associated with a skill.

### Shapeshift and Combat Forms
These utilities help AI and combat logic determine special behaviors based on a unit's current form.

*   **IsTankingForm**: Checks if a `ShapeshiftForm` is considered a "tanking" form. It returns `true` for `FORM_BEAR`, `FORM_DIREBEAR`, and `FORM_DEFENSIVESTANCE`. This is used by AI to adjust threat generation or positioning.
*   **IsAttackSpeedOverridenForm**: Checks if a `ShapeshiftForm` overrides standard weapon attack speeds. It returns `true` for `FORM_CAT`, `FORM_BEAR`, and `FORM_DIREBEAR`. This is critical for calculating melee swing timers correctly for druids in these forms.

### BattleGround Mapping
These functions translate between BattleGround logical types and physical map IDs.

*   **GetBattleGroundTypeIdByMapId**: Given a `mapId` (e.g., `MAP_ALTERAC_VALLEY`), returns the corresponding `BattleGroundTypeId` (e.g., `BATTLEGROUND_AV`). Returns `BATTLEGROUND_TYPE_NONE` if the map is not a battleground.
*   **GetBattleGrounMapIdByTypeId**: Given a `BattleGroundTypeId` (e.g., `BATTLEGROUND_WS`), returns the corresponding `mapId` (e.g., `MAP_WARSONG_GULCH`). Returns `0` if the type is invalid.

## Cross-Unit Boundaries

*   **GenderToString** is called by **ChatHandler.UnitCommands/HandleUnitShowGenderCommand**. This indicates that admin commands use this function to display unit information in a readable format.
*   **PowerToString** is called by **ChatHandler.UnitCommands/HandleUnitShowPowerTypeCommand**. Similarly, this supports admin diagnostics for unit power resources.
*   **SkillByLockType** is called by **Spell.Main/CanOpenLock**. This integration ensures that spell-based lockpicking or resource gathering checks validate against the correct skill ID derived from the lock's type.
*   **SkillByQuestSort** is called by **ObjectMgr/LoadQuests**. During quest loading, the server uses this to associate quests with specific skills, likely for filtering or UI categorization purposes.
*   **IsTankingForm** is called by **PartyBotAI/GetDistancingTarget**. The bot AI uses this to identify tanks, potentially adjusting target selection or positioning strategies to keep tanks engaged.
*   **IsAttackSpeedOverridenForm** is called by **Unit.Main/IsAttackSpeedOverridenShapeShift** and **Unit.SpellAuras/HandleShapeshiftBoosts**. This ensures that combat calculations correctly apply form-specific attack speed modifiers rather than relying on equipped weapon stats.
*   **GetBattleGroundTypeIdByMapId** is called by **WorldSession.BattleGroundHandler** methods (`HandleBattlefieldListOpcode`, `HandleBattleFieldPortOpcode`, `RequestBgJoinQueue`). This allows the session handler to identify which battleground a player is interacting with based on their current map location.
*   **GetBattleGrounMapIdByTypeId** is called by **BattleGroundMgr** (`BuildBattleGroundListPacket`, `CreateInitialBattleGrounds`) and **CombatBotBaseAI/SendBattlefieldPortPacket**. This enables the server to construct packets containing map coordinates for battleground portals and to initialize battleground instances on the correct maps.

## Data Model

This unit does not interact with any database tables. All data is static, defined via C++ enums and constants, mirroring the client's DBC files.

## Notable Implementation Details

*   **Magic Number Handling for Health**: The `POWER_HEALTH` enum is defined as `0xFFFFFFFE` (which is -2 as a signed 32-bit integer). The `PowerToString` function explicitly handles this case. Maintainers must be careful when comparing power types, as unsigned comparisons might behave unexpectedly if not cast correctly.
*   **Client Build Conditionals**: Several enums and constants are guarded by `#if SUPPORTED_CLIENT_BUILD > ...` directives (e.g., `CHAT_MSG_BG_SYSTEM_NEUTRAL`, `AUTH_PARENTAL_CONTROL`). This ensures backward compatibility with older WoW clients (1.12.1 era vs. later patches). Code consuming these constants must respect the same build flags.
*   **Duplicate Function Declarations**: The presence of `GenderToString#2` and `PowerToString#2` in the MAP suggests potential ambiguity or overload resolution issues in the original codebase. In the provided source, only one definition exists for each. If multiple definitions were present, they would likely be identical, leading to linker errors unless marked `inline` or `static`. The current source marks them `static`, ensuring internal linkage.
*   **Bitmask Construction**: The `RACEMASK_*` and `CLASSMASK_*` macros use bitwise shifts `(1<<(RACE_HUMAN-1))`. This assumes 1-based indexing for races/classes, which matches the DBC structure. Care must be taken when iterating over these masks, as the bit position is `index - 1`.
*   **Hardcoded Spell IDs**: Constants like `SPELL_ID_LOGIN_EFFECT` (836) and `SPELL_ID_DAZE` (1604) are hardcoded. While convenient, these are brittle; if the underlying game data changes (e.g., in a different patch), these IDs may become invalid. However, for a 1.12.1 emulator, these are stable.
*   **Position Structs**: The `Position` and `WorldLocation` structs provide basic POD (Plain Old Data) containers for spatial coordinates. `WorldLocation` includes a `mapId`, making it suitable for teleportation and zoning logic. Both include `IsEmpty()` checks, which are crucial for validating uninitialized locations before use.

## Member Reference

**GenderToString**: Converts a numeric gender identifier to a human-readable string ("Male", "Female", "None", or "UNKNOWN"). Used by admin commands to display unit info.

**GenderToString#2**: Duplicate or overloaded variant of `GenderToString`; behaves identically.

**PowerToString**: Converts a numeric power type identifier to a string ("Mana", "Rage", "Focus", "Energy", "Happiness", "Health", or "UNKNOWN"). Handles the special case of `POWER_HEALTH` (-2).

**PowerToString#2**: Duplicate or overloaded variant of `PowerToString`; behaves identically.

**ClassByQuestSort**: Maps a `QuestSort` ID to a `Class` enum for class-specific quests. Returns `0` for non-class-specific sorts.

**SkillByLockType**: Maps a `LockType` (e.g., picklock, herbalism) to the corresponding `SkillType` required to interact with it.

**SkillByQuestSort**: Maps a `QuestSort` ID to a `SkillType` for profession-related quests. Returns `0` if no skill is associated.

**IsTankingForm**: Returns `true` if the given `ShapeshiftForm` is a tanking form (Bear, Dire Bear, Defensive Stance). Used by AI for role identification.

**IsAttackSpeedOverridenForm**: Returns `true` if the given `ShapeshiftForm` overrides weapon attack speeds (Cat, Bear, Dire Bear). Used for combat timer calculations.

**GetBattleGroundTypeIdByMapId**: Converts a map ID to a `BattleGroundTypeId`. Returns `BATTLEGROUND_TYPE_NONE` if the map is not a battleground.

**GetBattleGrounMapIdByTypeId**: Converts a `BattleGroundTypeId` to a map ID. Returns `0` if the type is invalid.

---

<!-- machine-true, projected from graph.json -->

## Map — SharedDefines

*Source:* SharedDefines.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GenderToString | function | — | ChatHandler.UnitCommands/HandleUnitShowGenderCommand | — |
| GenderToString#2 | function | — | — | — |
| PowerToString | function | — | ChatHandler.UnitCommands/HandleUnitShowPowerTypeCommand | — |
| PowerToString#2 | function | — | — | — |
| ClassByQuestSort | function | — | — | — |
| SkillByLockType | function | — | Spell.Main/CanOpenLock | — |
| SkillByQuestSort | function | — | ObjectMgr/LoadQuests | — |
| IsTankingForm | function | — | PartyBotAI/GetDistancingTarget | — |
| IsAttackSpeedOverridenForm | function | — | Unit.Main/IsAttackSpeedOverridenShapeShift, Unit.SpellAuras/HandleShapeshiftBoosts | — |
| GetBattleGroundTypeIdByMapId | function | — | WorldSession.BattleGroundHandler/HandleBattlefieldListOpcode, WorldSession.BattleGroundHandler/HandleBattleFieldPortOpcode, WorldSession.BattleGroundHandler/RequestBgJoinQueue | — |
| GetBattleGrounMapIdByTypeId | function | — | BattleGroundMgr/BuildBattleGroundListPacket, BattleGroundMgr/CreateInitialBattleGrounds, CombatBotBaseAI/SendBattlefieldPortPacket | — |
