# SpellModMgr

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# SpellModMgr

**Purpose & Responsibilities**

`SpellModMgr` is a singleton manager responsible for applying server-side overrides to spell definitions stored in the client-side DBC (Data Block Chunk) files. In the WoWVMaNGOS architecture, spells are primarily defined by static data loaded from DBC files at startup. However, game balance adjustments, bug fixes, or specific server mechanics often require modifying these values without altering the original client files. `SpellModMgr` reads configuration data from two database tables—`spell_mod` and `spell_effect_mod`—and mutates the in-memory `SpellEntry` objects managed by `SpellMgr`. It also applies a few hardcoded "magic number" fixes for specific spells that cannot be easily represented in the generic table structure.

The unit operates during world initialization (`World::SetInitialWorldSettings`) and can be reloaded via server commands. It ensures that any spell modification specified in the database takes precedence over the default DBC values, provided the target spell exists in the game's spell registry.

## Member-by-Member Behavior

### Initialization and Lifecycle

*   **`SpellModMgr` (Constructor)** and **`~SpellModMgr` (Destructor)**: These are trivial constructors and destructors. The class relies on the `INSTANTIATE_SINGLETON_1` macro defined in the source to manage its global instance lifecycle. No resource allocation or cleanup occurs within these methods themselves.

### Helper Functions for Conditional Modification

The unit defines four inline helper functions to safely apply modifications from database fields to `SpellEntry` members. These functions address the fact that database columns for modifications are often nullable or use sentinel values (like `-1`) to indicate "no change."

*   **`ModUInt64ValueIfExplicit`**: Checks if the signed integer representation of a `Field` is non-negative. If so, it casts the field to `uint64` and assigns it to the target reference. This prevents accidental assignment of garbage or sentinel negative values to unsigned 64-bit integers.
*   **`ModUInt32ValueIfExplicit`**: Similar to the above, but for `uint32`. It checks if the signed 32-bit value is `>= 0` before assigning.
*   **`ModInt32ValueIfExplicit`**: Checks if the signed 32-bit value is not equal to `-1`. If true, it assigns the value. This handles cases where `-1` is used as a "null" indicator for signed integer fields.
*   **`ModFloatValueIfExplicit`**: Checks if the float value is not equal to `-1.0f`. If true, it assigns the value. This handles cases where `-1.0f` is used as a "null" indicator for floating-point fields.

### Core Loading Logic

*   **`LoadSpellMods`**: This is the primary method of the class. It performs the following steps:
    1.  **Logs Start**: Outputs a minimal log message indicating spell mods are loading.
    2.  **Processes `spell_mod` Table**:
        *   Queries the `spell_mod` table for all rows.
        *   If the table is empty, it logs a message and skips processing.
        *   If rows exist, it iterates through them using a progress bar (`BarGoLink`).
        *   For each row, it retrieves the `Id` (spell ID).
        *   **Spell Existence Check**: It attempts to retrieve the `SpellEntry` from `SpellMgr`. If the spell does not exist in memory, it checks if the ID is valid in the DBC files via `SpellMgr::IsExistingSpellId`. If the spell is completely unknown, it logs an error and skips. If the spell exists in DBC but isn't yet in memory (handled by `ENABLE_INSERT_NEW_SPELLS` logic), it attempts to create/overwrite the entry.
        *   **Applying Modifications**: It uses the helper functions (`ModUInt32ValueIfExplicit`, etc.) to update specific fields of the `SpellEntry` struct. Only fields explicitly set in the database (non-sentinel values) are updated. This allows partial updates (e.g., changing only the mana cost without touching the cast time).
        *   Special handling is applied for `Custom` flags and `SpellFamilyFlags` (uint64), which are assigned directly if non-zero.
    3.  **Processes `spell_effect_mod` Table**:
        *   Queries the `spell_effect_mod` table.
        *   Iterates through rows, retrieving `Id` (spell ID) and `EffectIndex`.
        *   Validates that the `EffectIndex` is within bounds (`MAX_EFFECT_INDEX`). If not, it logs an error and skips.
        *   Retrieves the `SpellEntry` and applies modifications to the specific effect index array elements (e.g., `spell->Effect[effect_idx]`).
        *   Uses the same helper functions to conditionally update effect-specific properties like damage dice, targets, and mechanics.
    4.  **Hardcoded Overrides**: After processing the tables, it applies three specific hardcoded modifications to spells that likely require complex logic or were added post-table-design:
        *   **Spell 1543 (Flare)**: Sets `speed` to `0.0f`.
        *   **Spell 20424 (Seal of Command Trigger)**: Sets `speed` to `10.0f` to introduce a minor delay.
        *   **Spell 20216 (Divine Favor)**: Sets `EffectItemType[0]` to `0x80202000`. The code comments note this is a hack because the `spell_effect_mod` table's `EffectItemType` column might not support the required `bigint` flags properly, or the logic requires a specific bitmask not easily configurable via the standard mod flow.

## Cross-Unit Boundaries

*   **`SpellMgr`**: `SpellModMgr` heavily depends on `SpellMgr` (accessed via `sSpellMgr`). It calls:
    *   `GetSpellEntry`: To retrieve the mutable `SpellEntry` object for a given ID.
    *   `IsExistingSpellId`: To verify if a spell ID is valid in the DBC files, even if not currently loaded in memory.
    *   `OverwriteSpellEntry`: (Conditional on `ENABLE_INSERT_NEW_SPELLS`) To force-load a spell into memory if it's missing but referenced in the mods table.
    *   This relationship is unidirectional: `SpellModMgr` reads from and writes to `SpellMgr`'s data structures.
*   **`Database`**: Accessed via `WorldDatabase.Query` to fetch rows from `spell_mod` and `spell_effect_mod`.
*   **`Log`**: Accessed via `sLog.Out` to report loading progress, errors (missing spells, invalid effect indices), and completion counts.
*   **`ProgressBar`**: Used via `BarGoLink` to provide visual feedback during the loading process in the console.
*   **`ChatHandler`**: `LoadSpellMods` is called by various chat command handlers (`HandleReloadSpellModsCommand`, `HandleReloadSpellTemplateCommand`, `HandleSpellIconFixCommand`) to allow runtime reloading of spell modifications.
*   **`World`**: Called during `SetInitialWorldSettings` to load mods at server startup.

## Data Model

`SpellModMgr` interacts with two database tables:

1.  **`spell_mod`**:
    *   **Purpose**: Stores global modifications to a spell's properties (not tied to a specific effect index).
    *   **Key Columns**: `Id` (PK, links to `SpellEntry::Id`), `procChance`, `procFlags`, `Custom`, `DurationIndex`, `Category`, `CastingTimeIndex`, `StackAmount`, `SpellIconID`, `activeIconID`, `manaCost`, `Attributes`, `AttributesEx` (1-4), `InterruptFlags`, `AuraInterruptFlags`, `ChannelInterruptFlags`, `Dispel`, `Stances`, `StancesNot`, `SpellVisual`, `ManaCostPercentage`, `StartRecoveryCategory`, `StartRecoveryTime`, `MaxTargetLevel`, `MaxAffectedTargets`, `DmgClass`, `rangeIndex`, `RecoveryTime`, `CategoryRecoveryTime`, `procCharges`, `SpellFamilyName`, `SpellFamilyFlags`, `Mechanic`, `EquippedItemClass`.
    *   **Usage**: Each column corresponds to a field in `SpellEntry`. If a column contains a non-sentinel value (e.g., `>= 0` for unsigned, `!= -1` for signed/float), it overwrites the DBC default.

2.  **`spell_effect_mod`**:
    *   **Purpose**: Stores modifications to specific effects of a spell. Spells can have multiple effects (indices 0, 1, 2), and this table allows fine-grained control over each.
    *   **Key Columns**: `Id` (PK, spell ID), `EffectIndex` (PK, 0-2), `Effect`, `EffectDieSides`, `EffectBaseDice`, `EffectDicePerLevel`, `EffectRealPointsPerLevel`, `EffectBasePoints`, `EffectAmplitude`, `EffectPointsPerComboPoint`, `EffectChainTarget`, `EffectMultipleValue`, `EffectMechanic`, `EffectImplicitTargetA`, `EffectImplicitTargetB`, `EffectRadiusIndex`, `EffectApplyAuraName`, `EffectItemType`, `EffectMiscValue`, `EffectTriggerSpell`.
    *   **Usage**: Rows are keyed by `(Id, EffectIndex)`. The code validates `EffectIndex` against `MAX_EFFECT_INDEX`. Modifications are applied to the corresponding array element in `SpellEntry` (e.g., `spell->Effect[0]`).

## Notable Implementation Details

*   **Sentinel Value Handling**: The helper functions rely on specific sentinel values (`-1` for signed ints/floats, negative for unsigned) to determine if a database column represents a "real" value or a "skip" instruction. This design allows sparse updates in the database tables.
*   **Const-Cast Mutability**: `SpellMgr::GetSpellEntry` returns a `const SpellEntry*`. `SpellModMgr` uses `const_cast` to remove constness and modify the entries. This implies that `SpellMgr` exposes a read-only interface, but the underlying data is intended to be mutable during the loading phase.
*   **Hardcoded Hacks**: The three hardcoded modifications at the end of `LoadSpellMods` are notable. They bypass the database-driven system entirely. The comment for Spell 20216 explicitly labels it a "HACK," suggesting a limitation in the `spell_effect_mod` table schema (specifically regarding `bigint` support for `EffectItemType`) or a need for immediate, non-configurable logic.
*   **Error Handling**: If a spell ID in the mods table does not exist in the DBC files, it is logged as an error and skipped. If it exists in DBC but not in memory, it attempts to load it. This prevents crashes from referencing invalid spells but may lead to silent failures if a mod is intended for a spell that was removed or renamed in the DBC.
*   **Progress Reporting**: The use of `BarGoLink` provides user-friendly progress bars in the console, which is helpful for large databases with many spell modifications.

## Member Reference

*   **`SpellModMgr`**: Constructor. Initializes the singleton instance. No side effects.
*   **`~SpellModMgr`**: Destructor. Cleans up the singleton instance. No side effects.
*   **`ModUInt64ValueIfExplicit`**: Inline helper. Assigns `Field` to `uint64` ref if `Field`'s signed int value is `>= 0`.
*   **`ModUInt32ValueIfExplicit`**: Inline helper. Assigns `Field` to `uint32` ref if `Field`'s signed int value is `>= 0`.
*   **`ModInt32ValueIfExplicit`**: Inline helper. Assigns `Field` to `int32` ref if `Field`'s signed int value is `!= -1`.
*   **`ModFloatValueIfExplicit`**: Inline helper. Assigns `Field` to `float` ref if `Field`'s float value is `!= -1.0f`.
*   **`LoadSpellMods`**: Main method. Loads and applies spell modifications from `spell_mod` and `spell_effect_mod` tables to `SpellEntry` objects in `SpellMgr`. Also applies three hardcoded overrides for spells 1543, 20424, and 20216. Logs progress and errors.

---

<!-- machine-true, projected from graph.json -->

## Map — SpellModMgr

*Source:* SpellModMgr.cpp, SpellModMgr.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| SpellModMgr | ctor | — | — | — |
| ~SpellModMgr | dtor | — | — | — |
| ModUInt64ValueIfExplicit | function | Field/GetInt64, Field/GetUInt64 | — | — |
| ModUInt32ValueIfExplicit | function | Field/GetInt32, Field/GetUInt32 | — | — |
| ModInt32ValueIfExplicit | function | Field/GetInt32 | — | — |
| ModFloatValueIfExplicit | function | Field/GetFloat | — | — |
| LoadSpellMods | method | Database/Query, Field/GetUInt32, Field/GetUInt64, Log.Main/Out, ProgressBar/BarGoLink, ProgressBar/BarGoLink#3, ProgressBar/step, QueryResult/Fetch, QueryResult/GetRowCount, QueryResult/NextRow, SpellMgr/GetSpellEntry, SpellMgr/Instance, SpellMgr/IsExistingSpellId, SpellMgr/OverwriteSpellEntry | ChatHandler.DebugCommands/HandleSpellIconFixCommand, ChatHandler.ServerCommands/HandleReloadSpellModsCommand, ChatHandler.ServerCommands/HandleReloadSpellTemplateCommand, World/SetInitialWorldSettings | spell_effect_mod, spell_mod |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `spell_effect_mod`: Id smallint(5) unsigned PK, EffectIndex int(3) unsigned PK, Effect int(3), EffectDieSides int(10), EffectBaseDice int(10), EffectDicePerLevel float, EffectRealPointsPerLevel float, EffectBasePoints int(10), EffectAmplitude int(10), EffectPointsPerComboPoint float, EffectChainTarget int(10), EffectMultipleValue float, EffectMechanic int(10), EffectImplicitTargetA int(10), EffectImplicitTargetB int(10), EffectRadiusIndex int(10), EffectApplyAuraName int(10), EffectItemType bigint(20), EffectMiscValue int(10), EffectTriggerSpell int(10), Comment varchar(255)?
- `spell_mod`: Id smallint(5) unsigned PK, procChance int(11)?, procFlags int(11)?, procCharges int(11)?, DurationIndex int(11)?, Category int(11)?, CastingTimeIndex int(11)?, StackAmount int(11)?, SpellIconID int(11)?, activeIconID int(11)?, manaCost int(11)?, Attributes int(11)?, AttributesEx int(11)?, AttributesEx2 int(11)?, AttributesEx3 int(11)?, AttributesEx4 int(11)?, Custom int(11)?, InterruptFlags int(11)?, AuraInterruptFlags int(11)?, ChannelInterruptFlags int(11)?, Dispel int(10), Stances int(11)?, StancesNot int(11)?, SpellVisual int(11)?, ManaCostPercentage int(11)?, StartRecoveryCategory int(11)?, StartRecoveryTime int(11)?, MaxAffectedTargets int(11)?, MaxTargetLevel int(11)?, DmgClass int(11)?, rangeIndex int(11)?, RecoveryTime int(11), CategoryRecoveryTime int(11), SpellFamilyName int(11), SpellFamilyFlags bigint(20) unsigned?, Mechanic int(2)?, EquippedItemClass int(2)?, Comment varchar(255)?

*`?` = nullable, `PK` = primary key column.*

