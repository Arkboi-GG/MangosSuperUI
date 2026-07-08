# UnitDefines

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# UnitDefines

**Purpose & Responsibilities**

`UnitDefines.h` is a foundational header file that defines the core enumerations, constants, and static helper functions required to represent the state, behavior, and properties of `Unit` objects (players, creatures, game objects) within the WoWVMaNGOS emulator. It serves as the canonical source of truth for numeric codes used in network packets, database storage, and internal logic regarding unit movement, combat status, visibility, and interaction capabilities.

The unit provides two primary services:
1.  **Data Definitions:** It declares `enum` types for nearly every aspect of a unit's existence, including stand states (`UnitStandStateType`), sheath states (`SheathState`), unit flags (`UnitFlags`, `NPCFlags`), movement modifiers (`UnitState`), and combat results (`VictimState`, `HitInfo`).
2.  **Debugging Utilities:** It implements several `static` functions (e.g., `UnitStandStateToString`, `UnitFlagToString`) that convert these numeric codes into human-readable strings. These are exclusively used by the `ChatHandler` subsystem to provide administrators with readable feedback when inspecting or modifying units via console commands.

This header contains no database interactions, no dynamic memory allocation, and no complex logic. It is purely a definition and utility layer.

## Member-by-Member Behavior

The members of this unit are grouped by the subsystem they support: **State Conversion Utilities**, **Movement & Physics Constants**, **Combat & Interaction Enums**, and **Unit Property Enums**.

### State Conversion Utilities

These functions translate internal integer codes into string literals for administrative debugging. They are simple `switch` statements with no side effects.

*   **`UnitStandStateToString`**: Converts a `UnitStandStateType` enum value (e.g., `UNIT_STAND_STATE_STAND`, `UNIT_STAND_STATE_DEAD`) into a descriptive string ("Stand", "Dead"). It is called by `ChatHandler.CreatureCommands/HandleNpcSpawnSetStandStateCommand` and `ChatHandler.UnitCommands/HandleUnitShowStandStateCommand` to display or confirm a unit's posture.
*   **`UnitVisFlagToString`**: Converts `UnitVisFlags` (visibility modifiers like Ghost, Creep, Untrackable) into strings. Currently, the MAP indicates this function is not called by any other unit in the provided scope, though it mirrors the pattern of other debug helpers.
*   **`SheathStateToString`**: Converts `SheathState` (Unarmed, Melee, Ranged) into strings. Called by `ChatHandler.CreatureCommands/HandleNpcSpawnSetSheathStateCommand` and `ChatHandler.UnitCommands/HandleUnitShowSheathStateCommand` to manage weapon readiness states.
*   **`UnitBytes2FlagsToString`**: Converts `UnitBytes2_Flags` (miscellaneous flags like PvP, Auras) into strings. Like `UnitVisFlagToString`, it is defined but not explicitly called by other units in the current MAP.
*   **`UnitStateToString`**: Converts `UnitState` bitmask values (e.g., `UNIT_STATE_STUNNED`, `UNIT_STATE_CHASE`) into strings. This is critical for debugging AI and movement generator states.
*   **`UnitFlagToString`**: Converts `UnitFlags` (e.g., `UNIT_FLAG_PVP`, `UNIT_FLAG_STUNNED`) into strings. This is the most extensive conversion function, covering dozens of flags that define a unit's interaction rules.
*   **`NPCFlagToString`**: Converts `NPCFlags` (service types like Vendor, Quest Giver, Trainer) into strings. Used to identify what services an NPC offers.
*   **`ReactStateToString`**: Converts `ReactStates` (Passive, Defensive, Aggressive) into strings. Called by `ChatHandler.CreatureCommands/HandleNpcAIInfoCommand` to report an NPC's aggression settings.
*   **`CommandStateToString`**: Converts `CommandStates` (Stay, Follow, Attack, Dismiss) into strings. Also called by `ChatHandler.CreatureCommands/HandleNpcAIInfoCommand` to report pet/companion commands.

*Note: The MAP lists `#2` variants for many of these functions (e.g., `UnitStandStateToString#2`). In the source code, these correspond to the same static function definitions. The `#2` suffix in the MAP likely represents duplicate entries or overloads not present in this specific header snippet, or simply distinct references in the call graph. Since the source shows only one definition per name, the behavior is identical.*

### Movement & Physics Constants

These `#define` macros and enums establish the physical rules for unit movement.

*   **`UnitMoveType`**: Defines movement animations (Walk, Run, Swim, Turn Rate).
*   **`baseMoveSpeed`**: An external array declaration storing base speeds for each `UnitMoveType`.
*   **`MOVEMENT_PACKET_TIME_DELAY`**: Set to `0`, indicating no artificial delay for movement packets.
*   **`MovementChangeType`**: Enumerates reasons for movement changes (Root, Water Walk, Teleport, Knock Back).
*   **`NO_FACING_CHECKS_DISTANCE`**: A constant (`1.4f`) defining the distance threshold below which orientation checks for auto-attacks or spells are skipped. This optimizes combat logic for close-range engagements.
*   **`ATTACK_DISPLAY_DELAY`**: A constant (`200` ms) delaying the next attack display to prevent client-side animation glitches.
*   **`REGEN_TIME_PLAYER_FULL` / `REGEN_TIME_CREATURE_FULL`**: Time thresholds (`2000` ms and `5000` ms respectively) used to compute regeneration values.
*   **`UNIT_PVP_COMBAT_TIMER`**: A constant (`5500` ms) likely used to track PvP combat engagement duration.
*   **`BASE_MELEERANGE_OFFSET`**: A floating-point constant (`1.333...`) used in melee range calculations.
*   **`BASE_MINDAMAGE` / `BASE_MAXDAMAGE` / `BASE_ATTACK_TIME`**: Default values for damage ranges and attack speed (`1.0f`, `2.0f`, `2000` ms).

### Combat & Interaction Enums

These enums define the outcomes and mechanics of combat interactions.

*   **`Swing`**: Defines swing types (No Swing, Single Handed, Two Handed).
*   **`VictimState`**: Enumerates the result of an attack on the target (Normal, Dodge, Parry, Block, Evade, Immune, Deflect).
*   **`HitInfo`**: A complex bitmask enum defining detailed hit information sent to the client. It includes flags for Critical Hits, Glancing Blows, Crushing Blows, Absorbs, and Misses. The definition is conditional on `SUPPORTED_CLIENT_BUILD`, ensuring compatibility with different World of Warcraft client versions (pre- and post-1.9.4).
*   **`AutoAttackCheckResult`**: Returns the result of a pre-attack check (OK, Not In Range, Bad Facing, Can't Attack, Dead, Friendly Target).
*   **`ActiveStates`**: Defines ability activation states (Passive, Disabled, Enabled, Command, Reaction).
*   **`ReactiveType`**: Defines types of reactive abilities (Defense, Hunter Parry, Crit, Overpower).
*   **`SpellProcEventTriggerCheck` / `SpellAuraProcResult`**: Enums for handling spell proc triggers and aura processing results.

### Unit Property Enums

These enums define the intrinsic properties of a unit.

*   **`UnitBytes0Offsets` / `UnitBytes1Offsets` / `UnitBytes2Offsets`**: Define the byte offsets within `UNIT_FIELD_BYTES_0`, `1`, and `2` for Race, Class, Gender, Power Type, Stand State, Pet Loyalty, Shapeshift Form, Visibility Flag, Sheath State, Misc Flags, and Pet Flags.
*   **`UnitStandStateType`**: Defines postures (Stand, Sit, Sleep, Dead, Kneel, Custom).
*   **`UnitVisFlags`**: Bitmask flags for visibility (Ghost, Creep, Untrackable).
*   **`SheathState`**: Defines weapon readiness (Unarmed, Melee, Ranged).
*   **`UnitBytes2_Flags`**: Miscellaneous flags (PvP, FFA PvP, Auras Visible).
*   **`UnitModifierType`**: Defines how modifiers are applied (Base Value, Base Pct, Total Value, Total Pct).
*   **`WeaponDamageRange`**: Indices for Min/Max damage.
*   **`DamageTypeToSchool`**: Maps damage types to resistance schools.
*   **`AuraRemoveMode`**: Defines reasons for aura removal (Default, Stack, Cancel, Dispel, Death, Delete, Shield Break, Expire, Channel, Range, Group).
*   **`UnitMods`**: A comprehensive list of unit modifiers (Stats, Health, Mana, Rage, Focus, Energy, Armor, Resistances, Damage). It includes synonym enums (`UNIT_MOD_STAT_START`, `UNIT_MOD_RESISTANCE_END`) for iterating over ranges.
*   **`BaseModGroup` / `BaseModType`**: Defines groups and types for base modifications (Crit Percentage, Shield Block Value, Flat/Pct Mod).
*   **`DeathState`**: Defines the lifecycle of a dead unit (Alive, Just Died, Corpse, Dead, Just Alive, Corpse Falling).
*   **`UnitState`**: A large bitmask enum defining internal unit states. It includes:
    *   **Persistent States:** Melee Attacking, No Kill Reward, Feign Death, Stunned, Root, Isolated, Possessed.
    *   **Movement Generator States:** Taxi Flight, Distracted, Confused, Roaming, Chase, Follow, Fleeing.
    *   **Pathfinding States:** Ignore Pathfinding, Allow Incomplete Path.
    *   **High-Level States:** Running, Pending Channel Reset.
    *   **AI/Search States:** No Search for Others, No Broadcast to Others, AI Uses Move in LoS.
    *   **Masks:** Composite masks like `UNIT_STATE_CAN_NOT_MOVE`, `UNIT_STATE_NO_FREE_MOVE`, `UNIT_STATE_CAN_NOT_REACT`, `UNIT_STATE_LOST_CONTROL`.
*   **`UnitVisibility`**: Defines visibility modes (Off, On, Group Stealth, Group Invisibility, Group No Detect, Respawn).
*   **`UnitFlags`**: A massive bitmask enum defining unit interaction rules. Key flags include:
    *   **Control:** Server Controlled, Player Controlled, Remove Client Control.
    *   **Combat:** In Combat, Stunned, Pacified, Disarmed, Confused, Fleeing, Possessed.
    *   **Immunity:** Immune to Player, Immune to NPC, Immune.
    *   **Interaction:** Not Selectable, Skinnable, Looting, PvP.
    *   **Visuals:** Auras Visible, Use Swim Animation, Prevent Anim.
*   **`NPCFlags`**: Bitmask enum for NPC services (Gossip, Quest Giver, Vendor, Flight Master, Trainer, Spirit Healer, Innkeeper, Banker, Battle Master, Auctioneer, Stable Master, Repair).
*   **`ActionBarIndex`**: Defines indices for the action bar (Start, Pet Spell Start/End, End).
*   **`ControlledUnitMask`**: Bitmask for controlled units (Pet, MiniPet, Guardians, Charm, Totems).
*   **`AddAuraFlags`**: Flags for adding auras (Positive, Negative, Passive, Permanent).
*   **`TeleportToOptions`**: Options for teleportation (GM Mode, Not Leave Transport, Not Leave Combat, Not Unsummon Pet, Spell, Force Map Change).
*   **`MovementModType`**: Types of movement modifications (Flee for Assistance, Flee in Fear, Confused).
*   **`UnitMountResult` / `UnitDismountResult`**: Return codes for mount/dismount attempts (Invalid, Too Far, Already Mounted, Not Mountable, Not Your Pet, Looting, Race Cant Mount, Shapeshifted, Forced Dismount, OK).
*   **`ModelIds`**: Hardcoded model IDs for various races (Human, Orc, Dwarf, Elf, Undead, Tauren, Gnome, Troll).
*   **`ReactStates` / `CommandStates`**: Enums for NPC reaction and command states.

## Cross-Unit Boundaries

The `UnitDefines` unit acts as a pure provider of data and utilities. It has no outgoing calls to other units. Its incoming calls are exclusively from the `ChatHandler` subsystem, specifically for debugging and administrative purposes.

*   **`ChatHandler.CreatureCommands/HandleNpcSpawnSetStandStateCommand`**: Calls `UnitStandStateToString` to validate or display stand states when spawning NPCs.
*   **`ChatHandler.UnitCommands/HandleUnitShowStandStateCommand`**: Calls `UnitStandStateToString` to display the current stand state of a unit.
*   **`ChatHandler.CreatureCommands/HandleNpcSpawnSetSheathStateCommand`**: Calls `SheathStateToString` to validate or display sheath states when spawning NPCs.
*   **`ChatHandler.UnitCommands/HandleUnitShowSheathStateCommand`**: Calls `SheathStateToString` to display the current sheath state of a unit.
*   **`ChatHandler.CreatureCommands/HandleNpcAIInfoCommand`**: Calls `ReactStateToString` and `CommandStateToString` to display the AI configuration (reaction and command states) of an NPC.

These interactions are one-way: `ChatHandler` reads the definitions and uses the string conversion functions to format output for the administrator. No state is modified in `UnitDefines` by these calls.

## Data Model

This unit does not interact with any database tables. All data is defined statically in the header file. The enums and constants are hardcoded and do not rely on dynamic data from the live database.

## Notable Implementation Details

1.  **Static String Conversion Functions**: All `*ToString` functions are declared `static` and inline within the header. This ensures zero linkage overhead and allows them to be included in multiple translation units without symbol conflicts. They return `char const*` literals, which are safe and efficient.
2.  **Conditional Compilation for `HitInfo`**: The `HitInfo` enum is defined differently based on `SUPPORTED_CLIENT_BUILD`. This reflects changes in the World of Warcraft protocol between older clients (pre-1.9.4) and newer ones. Maintainers must ensure that the correct client build is defined during compilation to match the expected packet structures.
3.  **Bitmask Masks in `UnitState`**: The `UnitState` enum includes composite masks (e.g., `UNIT_STATE_CAN_NOT_MOVE`, `UNIT_STATE_NO_FREE_MOVE`). These are calculated at compile time using bitwise OR operations. This allows efficient checking of multiple states simultaneously (e.g., `if (unit->m_unitState & UNIT_STATE_CAN_NOT_MOVE)`).
4.  **Synonym Enums in `UnitMods`**: The `UnitMods` enum includes synonyms like `UNIT_MOD_STAT_START` and `UNIT_MOD_STAT_END`. These are used to iterate over ranges of modifiers (e.g., all stats, all resistances) without hardcoding indices. This improves code readability and maintainability when looping through modifier arrays.
5.  **Hardcoded Model IDs**: The `ModelIds` enum contains hardcoded model IDs for various races. While convenient, this assumes that the model IDs in the `CreatureDisplayInfo.dbc` or similar data sources remain constant. If the game data changes, these constants may become invalid.
6.  **Unknown Flags**: Several enums contain flags marked as `UNK` (e.g., `UNIT_BYTE2_FLAG_UNK1`, `UNIT_FLAG_UNK_14`). These represent reverse-engineered bits whose exact purpose is not fully documented. Maintainers should treat these with caution, as their behavior may change or be misinterpreted.
7.  **Legacy Comments**: Some comments reference legacy behavior (e.g., `UNIT_FLAG_REMOVE_CLIENT_CONTROL` is noted as a legacy flag replaced by `SMSG_CLIENT_CONTROL`). This indicates that some flags may be retained for backward compatibility or specific edge cases, even if they are no longer the primary mechanism.

## Member Reference

*   **`UnitStandStateToString`**: Static function converting `UnitStandStateType` to string. Called by `ChatHandler.CreatureCommands/HandleNpcSpawnSetStandStateCommand` and `ChatHandler.UnitCommands/HandleUnitShowStandStateCommand`.
*   **`UnitStandStateToString#2`**: Duplicate reference to `UnitStandStateToString`.
*   **`UnitVisFlagToString`**: Static function converting `UnitVisFlags` to string.
*   **`UnitVisFlagToString#2`**: Duplicate reference to `UnitVisFlagToString`.
*   **`SheathStateToString`**: Static function converting `SheathState` to string. Called by `ChatHandler.CreatureCommands/HandleNpcSpawnSetSheathStateCommand` and `ChatHandler.UnitCommands/HandleUnitShowSheathStateCommand`.
*   **`SheathStateToString#2`**: Duplicate reference to `SheathStateToString`.
*   **`UnitBytes2FlagsToString`**: Static function converting `UnitBytes2_Flags` to string.
*   **`UnitBytes2FlagsToString#2`**: Duplicate reference to `UnitBytes2FlagsToString`.
*   **`UnitStateToString`**: Static function converting `UnitState` bitmask to string.
*   **`UnitStateToString#2`**: Duplicate reference to `UnitStateToString`.
*   **`UnitFlagToString`**: Static function converting `UnitFlags` bitmask to string.
*   **`UnitFlagToString#2`**: Duplicate reference to `UnitFlagToString`.
*   **`NPCFlagToString`**: Static function converting `NPCFlags` bitmask to string.
*   **`NPCFlagToString#2`**: Duplicate reference to `NPCFlagToString`.
*   **`ReactStateToString`**: Static function converting `ReactStates` to string. Called by `ChatHandler.CreatureCommands/HandleNpcAIInfoCommand`.
*   **`ReactStateToString#2`**: Duplicate reference to `ReactStateToString`.
*   **`CommandStateToString`**: Static function converting `CommandStates` to string. Called by `ChatHandler.CreatureCommands/HandleNpcAIInfoCommand`.
*   **`CommandStateToString#2`**: Duplicate reference to `CommandStateToString`.

---

<!-- machine-true, projected from graph.json -->

## Map — UnitDefines

*Source:* UnitDefines.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| UnitStandStateToString | function | — | ChatHandler.CreatureCommands/HandleNpcSpawnSetStandStateCommand, ChatHandler.UnitCommands/HandleUnitShowStandStateCommand | — |
| UnitStandStateToString#2 | function | — | — | — |
| UnitVisFlagToString | function | — | — | — |
| UnitVisFlagToString#2 | function | — | — | — |
| SheathStateToString | function | — | ChatHandler.CreatureCommands/HandleNpcSpawnSetSheathStateCommand, ChatHandler.UnitCommands/HandleUnitShowSheathStateCommand | — |
| SheathStateToString#2 | function | — | — | — |
| UnitBytes2FlagsToString | function | — | — | — |
| UnitBytes2FlagsToString#2 | function | — | — | — |
| UnitStateToString | function | — | — | — |
| UnitStateToString#2 | function | — | — | — |
| UnitFlagToString | function | — | — | — |
| UnitFlagToString#2 | function | — | — | — |
| NPCFlagToString | function | — | — | — |
| NPCFlagToString#2 | function | — | — | — |
| ReactStateToString | function | — | ChatHandler.CreatureCommands/HandleNpcAIInfoCommand | — |
| ReactStateToString#2 | function | — | — | — |
| CommandStateToString | function | — | ChatHandler.CreatureCommands/HandleNpcAIInfoCommand | — |
| CommandStateToString#2 | function | — | — | — |
