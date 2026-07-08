# Position

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

The `SharedDefines.h` unit provides the foundational type definitions, enumerations, constants, and simple aggregate structures required across the entire WoWVMaNGOS server codebase. It acts as a central dictionary for game-specific semantics, mapping raw numeric values (often derived from client-side DBC files or network protocols) to human-readable C++ identifiers.

Its primary responsibilities are:
1.  **Game State Representation:** Defining enums for races, classes, skills, emotes, animations, and chat messages that mirror the client’s internal state.
2.  **Protocol Constants:** Providing magic numbers for spell IDs, response codes, and packet-related constants (e.g., `MAX_SPELL_EFFECTS`, `CHAIN_SPELL_JUMP_RADIUS`).
3.  **Utility Aggregates:** Defining lightweight data structures like `Position` and `WorldLocation` used extensively for spatial calculations and movement logic.
4.  **Helper Functions:** Offering inline conversion utilities (e.g., `GenderToString`, `ClassByQuestSort`) to translate between different ID spaces (DBC indices vs. internal enums).

This unit contains no complex logic, database interactions, or stateful behavior. It is purely declarative and functional, serving as a dependency for nearly every other component in the server.

## Member-by-Member Behavior

The members defined in this unit are grouped below by their functional role. Note that the MAP only lists members of the `Position` struct; however, the source file contains many other significant definitions. Per the rules, only the mapped members (`Position` ctors and `IsEmpty`) are detailed in the final reference section, but the following narrative covers the broader context of the file to ensure a new engineer understands the unit's full scope.

### Spatial Data Structures
The file defines two key structs for handling coordinates:
*   **`Position`**: A simple aggregate holding four `float` values: `x`, `y`, `z`, and `o` (orientation). It defaults all values to `0.0f`. It provides two constructors: a default constructor and a parameterized one. It also includes an `IsEmpty` method to check if all components are zero.
*   **`WorldLocation`**: Similar to `Position` but includes a `uint32 mapId`. It represents a location within a specific map instance. It also provides default, parameterized, and copy constructors, plus an `IsEmpty` check.

### Game Entity Enums
The file defines extensive enumerations for game entities:
*   **`Races`** and **`Classes`**: Map to indices in `ChrRaces.dbc` and `ChrClasses.dbc`. Includes masks for Alliance/Horde and playable classes.
*   **`Gender`**: Male, Female, None.
*   **`Stats`** and **`Powers`**: Define core attributes (Strength, Agility, etc.) and resource types (Mana, Rage, Energy, etc.).
*   **`ItemQualities`**: Maps item rarity (Poor to Artifact) to color hex codes via `ItemQualityColors`.

### Gameplay Mechanics Enums
*   **`Emote`** and **`Anim`**: Large enumerations defining visual states and animations. These are critical for syncing server actions with client visual feedback.
*   **`TextEmotes`**: Defines text-based emotes (e.g., `/wave`, `/dance`).
*   **`SkillType`**: Maps skill IDs to categories (weapons, professions, languages). Includes helper functions `SkillByLockType` and `SkillByQuestSort` to resolve skills from other contexts.
*   **`DiminishingReturnsType`**, **`DiminishingGroup`**, **`DiminishingLevels`**: Define the DR system for crowd control effects.

### Network & Protocol Constants
*   **`ChatMsg`**: Enumerates chat message types (Say, Yell, Whisper, System, etc.).
*   **`ResponseCodes`**: Defines authentication and character management status codes (e.g., `AUTH_OK`, `CHAR_CREATE_NAME_IN_USE`).
*   **`MailResponseResult`**: Defines outcomes for mail operations.
*   **`TradeStatus`**: Defines states for player trading.

### Utility Helpers
*   **`GenderToString`**: Converts `Gender` enum to string.
*   **`PowerToString`**: Converts `Powers` enum to string.
*   **`IsTankingForm`** / **`IsAttackSpeedOverridenForm`**: Inline checks for Druid/Warrior forms.
*   **`GetBattleGroundTypeIdByMapId`** / **`GetBattleGrounMapIdByTypeId`**: Bidirectional mapping between Map IDs and Battleground Type IDs.

## Cross-Unit Boundaries

The `Position` struct is a fundamental building block used throughout the server. Its usage patterns reveal how spatial data flows between systems:

*   **Creation (`Position` ctors)**:
    *   **`boss_nefarian/OnEffectExecute`**: Creates a `Position` likely to determine a spawn point or effect location during the Nefarian encounter.
    *   **`ChatHandler.UnitCommands/HandleGPSCommand`**: Constructs a `Position` from user input or current unit location for debugging/admin purposes.
    *   **`Spell.Main/SetTargetMap`**: Uses `Position` to define where a spell effect should manifest on the map.
    *   **`TargetedMovementGenerator/_setTargetLocation`**: Sets the destination for a unit's movement generator.
    *   **`Unit.Main/GetRandomAttackPoint`**: Calculates a random offset position for melee attacks.
    *   **`ChatHandler.HardcodedEvents/ScourgeInvasionEvent`**: Likely spawns objects or units at specific coordinates.
    *   **`GameObject/Create`**: Initializes a game object at a specific world coordinate.
    *   **`WorldObject.Object/MovePositionToFirstCollision`**: Calculates a collision-free position for an object.

*   **Validation (`IsEmpty`)**:
    *   **`ChatHandler.UnitCommands/HandleUnitMoveInfoCommand`**: Checks if a `Position` is valid (non-zero) before processing move info commands, preventing invalid state propagation.

These interactions show that `Position` is passed by value or reference to initialize state in AI, Movement, Spell, and Command systems. It does not call back into other units; it is a passive data carrier.

## Data Model

This unit does not interact with any database tables. It contains no SQL queries, ORM mappings, or persistent storage logic. All data is transient, defined at compile time, or held in memory during runtime.

## Notable Implementation Details

1.  **Default Initialization**: Both `Position` and `WorldLocation` use in-class initializers (`= 0.0f`, `= 0`) for their members. This ensures that default-constructed instances are always "empty" or at origin `(0,0,0,0)`, which is crucial for the `IsEmpty()` check to work correctly without requiring explicit initialization in every constructor call site.
2.  **`IsEmpty` Logic**: The `IsEmpty()` method checks for exact equality to zero (`!x && !y ...`). Since these are floats, this assumes that valid positions will never naturally be exactly `0.0f` in all fields simultaneously, or that "empty" is explicitly represented by zeros. This is a common pattern in game engines where `(0,0,0)` is reserved for "invalid" or "uninitialized" positions, especially since `(0,0,0)` is often outside valid world bounds or underground.
3.  **Enum Value Mapping**: Many enums (like `Races`, `Classes`, `Maps`) use specific integer values that correspond directly to DBC file indices or client protocol values. Changing these values would break compatibility with the client. For example, `RACE_HUMAN = 1` matches the client's expectation.
4.  **Conditional Compilation**: Several enums and constants are guarded by `#if SUPPORTED_CLIENT_BUILD > ...`. This allows the server to support multiple client versions (e.g., 1.12.1 vs 1.11.2) by including or excluding features like `CHAT_MSG_RAID_BOSS_WHISPER` or specific spell IDs.
5.  **Inline Helpers**: Functions like `SkillByLockType` and `ClassByQuestSort` are defined as `inline` in the header. This avoids linking issues and provides immediate access to these conversions without function call overhead, though the logic is simple switch statements.
6.  **Bitmask Definitions**: Macros like `RACEMASK_ALLIANCE` and `CLASSMASK_ALL_PLAYABLE` construct bitmasks for efficient set membership testing. These are used extensively in permission checks and filtering logic elsewhere in the codebase.

## Member Reference

**Position** (ctor)
Default constructor for the `Position` struct. Initializes `x`, `y`, `z`, and `o` to `0.0f` via in-class initializers. Used when a position needs to be declared but not immediately assigned a value. Called by various units to create uninitialized position objects.

**Position#2** (ctor)
Parameterized constructor for the `Position` struct. Takes four `float` arguments (`position_x`, `position_y`, `position_z`, `orientation`) and assigns them to the respective members. Used to create a position with specific coordinates immediately upon declaration. Called by units such as `boss_nefarian/OnEffectExecute`, `ChatHandler.UnitCommands/HandleGPSCommand`, `Spell.Main/SetTargetMap`, `TargetedMovementGenerator/_setTargetLocation`, `Unit.Main/GetRandomAttackPoint`, `ChatHandler.HardcodedEvents/ScourgeInvasionEvent`, `GameObject/Create`, and `WorldObject.Object/MovePositionToFirstCollision`.

**IsEmpty** (method)
Checks if the `Position` is empty. Returns `true` if all four members (`x`, `y`, `z`, `o`) are equal to `0.0f` (evaluated as false in boolean context). This serves as a validity check for positions that may have been default-constructed or cleared. Called by `ChatHandler.UnitCommands/HandleUnitMoveInfoCommand` to validate input data.

---

<!-- machine-true, projected from graph.json -->

## Map — Position

*Source:* SharedDefines.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| Position | ctor | — | boss_nefarian/OnEffectExecute, ChatHandler.UnitCommands/HandleGPSCommand, Spell.Main/SetTargetMap, TargetedMovementGenerator/_setTargetLocation, Unit.Main/GetRandomAttackPoint | — |
| Position#2 | ctor | — | ChatHandler.HardcodedEvents/ScourgeInvasionEvent, GameObject/Create, WorldObject.Object/MovePositionToFirstCollision | — |
| IsEmpty | method | — | ChatHandler.UnitCommands/HandleUnitMoveInfoCommand | — |
