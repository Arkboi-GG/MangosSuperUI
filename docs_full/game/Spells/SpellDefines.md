# SpellDefines

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# SpellDefines

**Purpose & Responsibilities**

`SpellDefines.h` is a foundational header file that defines the static constants, enumerations, and inline utility functions governing the spell system within the `wowvmangos` codebase. It serves as the central dictionary for spell-related data structures, translating raw numeric values from game data files (DBC) or database records into meaningful C++ identifiers.

The unit does not contain executable logic beyond simple bit manipulation and string conversion. Its primary responsibilities are:
1.  **Defining Spell Metadata:** Establishing enums for spell targets, effects, attributes, mechanics, schools, and categories. These correspond directly to fields in the `Spell.dbc` and related data files.
2.  **Providing Utility Functions:** Offering lightweight helper functions (`GetSchoolMask`, `GetFirstSchoolInMask`, `SpellCastTargetFlagToString`) used extensively across the spell casting, damage calculation, and aura management subsystems.
3.  **Establishing Bitmask Constants:** Defining masks for mechanics, dispel types, and immunity flags, which are used to efficiently check multiple conditions simultaneously (e.g., checking if a spell is immune to both root and stun).

This header is included by nearly all components interacting with spells, including `Spell`, `SpellCaster`, `Unit`, `Player`, `Creature`, and various AI modules. It ensures consistent interpretation of spell flags and behaviors across the entire engine.

## Member-by-Member Behavior

The members of this unit are grouped by their functional role within the spell system.

### Utility Functions

These inline functions provide quick conversions or lookups based on spell properties.

*   **`SpellCastTargetFlagToString`**: Converts a numerical `SpellCastTargetFlags` bitmask value into a human-readable string representation (e.g., `TARGET_FLAG_UNIT`). This is primarily used for debugging and logging purposes. It iterates through known flag values using a `switch` statement. If the flag is unknown, it returns `"UNKNOWN"`.
*   **`GetSchoolMask`**: Converts a single `uint32` school index (from `SpellSchools`) into a `SpellSchoolMask` bitmask. It performs a left-shift operation (`1 << school`). This is critical because many internal systems expect a bitmask of schools rather than a single index. It is called by numerous units involved in damage calculation, aura application, and spell validation.
*   **`GetFirstSchoolInMask`**: Iterates through the possible spell schools (0 to `MAX_SPELL_SCHOOL - 1`) and returns the first school index found in the provided `SpellSchoolMask`. If no school is set in the mask, it defaults to `SPELL_SCHOOL_NORMAL`. This is useful when a system needs to identify a primary school for a spell that might technically have multiple schools set (though typically spells have one primary school).

### Enumerations and Constants

The remainder of the unit consists of `enum` definitions and `#define` macros. These do not have "behavior" in the traditional sense but define the valid states and flags for the spell system.

*   **`SpellTarget`**: Defines the possible targeting modes for a spell (e.g., `TARGET_UNIT_CASTER`, `TARGET_LOCATION_CASTER_DEST`). These values correspond to the `TargetX` fields in `Spell.dbc`.
*   **`SpellCastTargetFlags`**: Defines bitflags indicating what kind of target a spell can accept (e.g., `TARGET_FLAG_UNIT`, `TARGET_FLAG_ITEM`, `TARGET_FLAG_SOURCE_LOCATION`). These are used during spell casting validation to ensure the user has selected a valid target type.
*   **`SpellMissInfo`**: Defines reasons why a spell might miss or fail to apply its full effect (e.g., `SPELL_MISS_DODGE`, `SPELL_MISS_RESIST`, `SPELL_MISS_IMMUNE`). Used in combat log generation and client feedback.
*   **`SpellHitType`**: Defines flags for hit outcomes, including debug flags and specific conditions like `SPELL_HIT_TYPE_CRIT`.
*   **`SpellDmgClass`**: Categorizes spells into `NONE`, `MAGIC`, `MELEE`, or `RANGED`. This determines how damage is calculated and which defenses apply.
*   **`SpellPreventionType`**: Defines types of spell prevention (Silence, Pacify).
*   **`SpellRangeIndex`**: Maps specific indices from `SpellRange.dbc` to common range concepts (Self, Combat, Anywhere).
*   **`SpellEffects`**: The most extensive enum, defining every possible effect a spell can have (e.g., `SPELL_EFFECT_SCHOOL_DAMAGE`, `SPELL_EFFECT_HEAL`, `SPELL_EFFECT_SUMMON`). These correspond to the `Effect` fields in `Spell.dbc`.
*   **`SpellCastResult`**: Defines all possible outcomes of a spell cast attempt, from success (`SPELL_CAST_OK`) to various failure reasons (`SPELL_FAILED_OUT_OF_RANGE`, `SPELL_FAILED_IMMUNE`). Many of these map to client-side error messages.
*   **`SpellInterruptFlags`**: Defines conditions under which a spell cast can be interrupted (Movement, Damage, Stun, Combat).
*   **`SpellAuraInterruptFlags`**: Defines conditions under which an active aura can be removed (Hostile Action, Moving, Mounting, etc.).
*   **`SpellModOp`**: Defines operations that can modify spell properties (Damage, Duration, Threat, etc.). These are used by auras that buff/debuff spell stats.
*   **`SpellModType`**: Defines the type of modification (Flat vs. Percent).
*   **`AuraState`**: Defines specific states tracked by auras (e.g., `AURA_STATE_DEFENSE`, `AURA_STATE_HEALTHLESS_20_PERCENT`). These are used to trigger special behaviors when certain conditions are met.
*   **`Mechanics`**: Defines crowd control and status effect types (Root, Stun, Fear, Silence, etc.). Used for immunity checks and aura interactions.
*   **`DispelType`**: Defines categories of effects that can be dispelled (Magic, Curse, Disease, Poison, Stealth, Invisibility).
*   **`SpellImmunity`**: Defines the types of immunity a unit can have (Effect, State, School, Damage, Dispel, Mechanic).
*   **`SpellSchools`**: Defines the individual magic schools (Normal, Holy, Fire, Nature, Frost, Shadow, Arcane).
*   **`SpellSchoolMask`**: Defines bitmasks for the schools, including combined masks like `SPELL_SCHOOL_MASK_MAGIC` and `SPELL_SCHOOL_MASK_ALL`.
*   **`SpellVisualKit`**: Defines specific visual kit IDs for food and drink effects.
*   **`SpellAttributes`**, **`SpellAttributesEx`**, **`SpellAttributesEx2`**, **`SpellAttributesEx3`**, **`SpellAttributesEx4`**: Extensive lists of bitflags controlling spell behavior, such as whether it can be cast while mounted, if it initiates combat, if it ignores line of sight, etc. These correspond to the `Attributes` fields in `Spell.dbc`.
*   **`SpellAttributesCustom`**: Defines custom flags added by the server core for specific gameplay tweaks not present in the original client data (e.g., `SPELL_CUSTOM_FIXED_DAMAGE`, `SPELL_CUSTOM_IGNORE_ARMOR`).
*   **`SpellAttributesInternal`**: Defines internal flags computed by the server to optimize processing (e.g., `SPELL_INTERNAL_APPLIES_AURA`, `SPELL_INTERNAL_DIRECT_DAMAGE`).
*   **`ProcFlags`** and **`ProcFlagsEx`**: Define conditions under which a spell or aura can "proc" (trigger) (e.g., On Kill, On Melee Swing, On Critical Hit).
*   **`SpellCategories`**: Defines cooldown categories for spells. Spells in the same category share a cooldown timer.
*   **`SpellCategoryFlags`**: Flags modifying category behavior (e.g., global cooldown).
*   **`SpellSpecific`**: Defines specific spell classifications for UI and gameplay logic (e.g., Seals, Blessings, Judgements, Food, Drink).

## Cross-Unit Boundaries

`SpellDefines.h` is a pure definition header. Its members are called by many other units but do not call out to other units themselves (except for standard library includes).

*   **`SpellCastTargetFlagToString`**:
    *   **Called by**: `Spell.Main/ValidateExplicitTargetMask` (in `Spell.cpp`). This function uses it to log or debug target mask validation issues.

*   **`GetSchoolMask`**:
    *   **Called by**:
        *   `ChatHandler.UnitCommands/HandleDamageCommand` (in `ChatCommands.cpp`): To convert a school index to a mask for testing damage.
        *   `CreatureEventAI/SpellHit` and `SpellHitTarget` (in `CreatureEventAI.cpp`): To determine the school of a spell hitting a creature for event triggering.
        *   `CreatureEventAIMgr/LoadCreatureEventAI_Events` (in `CreatureEventAIMgr.cpp`): To load event configurations based on school masks.
        *   `Player.StatSystem/UpdateSpellDamageAndHealingBonus` (in `Player.cpp`): To calculate bonuses based on school.
        *   `Spell.Effects/EffectWeaponDmg` (in `SpellEffects.cpp`): To determine if weapon damage should be modified by school-specific stats.
        *   `Spell.Main/Spell#2` (in `Spell.cpp`): Likely during spell initialization or loading.
        *   `SpellCaster/CalculateSpellDamage` and `DealSpellDamage` (in `SpellCaster.cpp`): Core damage calculation logic relies on school masks for resistance and bonus calculations.
        *   `SpellMgr/IsSpellProcEventCanTriggeredBy` (in `SpellMgr.cpp`): To check if a proc condition matches the school of the triggering spell.
        *   `Unit.AuraProcHandler/HandleDummyAuraProc`, `HandleModDamageAuraProc`, `HandleModPowerCostSchoolAuraProc`, `HandleReflectSpellsSchoolAuraProc` (in `UnitAuraProcHandler.cpp`): To handle aura procs that depend on spell school.
        *   `Unit.Main/CalculateMeleeDamage` (in `Unit.cpp`): To apply spell-like modifiers to melee damage.
        *   `Unit.SpellAuras/PeriodicTick` (in `Unit.cpp`): To handle periodic damage/healing based on school.

*   **`GetFirstSchoolInMask`**:
    *   **Called by**:
        *   `Spell.Main/CalculatePowerCost` (in `Spell.cpp`): To determine the primary school for power cost calculations.
        *   `Spell.Main/DoAllEffectOnTarget#3` (in `Spell.cpp`): To process effects that depend on the primary school.
        *   `Spell.Main/HandleDelayedSpellLaunch` (in `Spell.cpp`): To handle delayed effects.
        *   `SpellCaster/GetSpellResistChance` (in `SpellCaster.cpp`): To calculate resistance chance based on the primary school.
        *   `SpellCaster/SendSpellNonMeleeDamageLog#2` (in `SpellCaster.cpp`): To log the correct school in combat logs.
        *   `Unit.AuraProcHandler/HandleDummyAuraProc` (in `UnitAuraProcHandler.cpp`): To determine the school for dummy aura procs.
        *   `Unit.Main/ApplyTotalThreatModifier` (in `Unit.cpp`): To apply threat modifiers based on school.
        *   `Unit.Main/IsSpellCrit` (in `Unit.cpp`): To determine critical strike chance based on school.
        *   `Unit.Main/SendAttackStateUpdate` (in `Unit.cpp`): To update the client on attack state, potentially involving school information.

## Data Model

This unit does not directly interact with any database tables. It defines constants and enums that correspond to data stored in binary DBC files (like `Spell.dbc`) and potentially referenced in SQL tables (like `creature_template` or `spell_custom_attr`), but the header itself contains no SQL queries or table definitions. The `SpellAttributesCustom` and `SpellAttributesInternal` enums suggest that custom spell attributes might be stored in a database table (likely `spell_custom_attr`), but this unit only defines the C++ side of those flags.

## Notable Implementation Details

*   **Bitmask Usage**: The heavy use of bitmasks (e.g., `SpellSchoolMask`, `SpellAttributes`) allows for efficient storage and checking of multiple properties simultaneously. Functions like `GetSchoolMask` and `GetFirstSchoolInMask` are essential for converting between single-value indices and these bitmasks.
*   **Client Build Conditionals**: Several enums (e.g., `SpellCastResult`, `SpellAttributes`) contain `#if SUPPORTED_CLIENT_BUILD > ...` directives. This indicates that the codebase supports multiple client versions, and certain spell results or attributes are only relevant for newer clients. This is crucial for maintaining compatibility across different patches of the game.
*   **Custom Attributes**: The presence of `SpellAttributesCustom` and `SpellAttributesInternal` shows that the server extends the original game data with custom flags. These are likely used to implement server-side fixes, optimizations, or custom gameplay features not present in the original client. For example, `SPELL_CUSTOM_FIXED_DAMAGE` allows spells to ignore damage bonuses, which might be needed for specific boss abilities.
*   **Proc Flags Complexity**: The `ProcFlags` and `ProcFlagsEx` enums are quite detailed, covering a wide range of conditions for triggering spells and auras. This complexity reflects the intricate nature of spell interactions in the game, where procs can be triggered by melee swings, spell hits, kills, and more.
*   **Default Values**: `GetFirstSchoolInMask` defaults to `SPELL_SCHOOL_NORMAL` if no school is found in the mask. This is a safe fallback, as physical damage is the most common type, but it could lead to unexpected behavior if a spell truly has no school defined (which should be rare).
*   **String Conversion Limitation**: `SpellCastTargetFlagToString` only handles individual flags, not combinations. If a mask contains multiple flags, calling this function with the entire mask will likely return `"UNKNOWN"` unless the exact combination is explicitly handled (which it isn't). This suggests it's intended for debugging individual flag values rather than complex masks.

## Member Reference

*   **`SpellCastTargetFlagToString`**: Inline function that converts a `SpellCastTargetFlags` value to a human-readable string. Used for debugging. Returns `"UNKNOWN"` for unrecognized flags.
*   **`GetSchoolMask`**: Inline function that converts a `uint32` school index to a `SpellSchoolMask` bitmask using a left shift. Widely used for damage calculation and aura handling.
*   **`GetFirstSchoolInMask`**: Inline function that iterates through a `SpellSchoolMask` to find and return the first set school index. Defaults to `SPELL_SCHOOL_NORMAL` if none are found. Used for determining primary school for various calculations.

---

<!-- machine-true, projected from graph.json -->

## Map — SpellDefines

*Source:* SpellDefines.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| SpellCastTargetFlagToString | function | — | Spell.Main/ValidateExplicitTargetMask | — |
| GetSchoolMask | function | — | ChatHandler.UnitCommands/HandleDamageCommand, CreatureEventAI/SpellHit, CreatureEventAI/SpellHitTarget, CreatureEventAIMgr/LoadCreatureEventAI_Events, Player.StatSystem/UpdateSpellDamageAndHealingBonus, Spell.Effects/EffectWeaponDmg, Spell.Main/Spell#2, SpellCaster/CalculateSpellDamage, SpellCaster/DealSpellDamage, SpellMgr/IsSpellProcEventCanTriggeredBy, Unit.AuraProcHandler/HandleDummyAuraProc, Unit.AuraProcHandler/HandleModDamageAuraProc, Unit.AuraProcHandler/HandleModPowerCostSchoolAuraProc, Unit.AuraProcHandler/HandleReflectSpellsSchoolAuraProc, Unit.Main/CalculateMeleeDamage, Unit.SpellAuras/PeriodicTick | — |
| GetFirstSchoolInMask | function | — | Spell.Main/CalculatePowerCost, Spell.Main/DoAllEffectOnTarget#3, Spell.Main/HandleDelayedSpellLaunch, SpellCaster/GetSpellResistChance, SpellCaster/SendSpellNonMeleeDamageLog#2, Unit.AuraProcHandler/HandleDummyAuraProc, Unit.Main/ApplyTotalThreatModifier, Unit.Main/IsSpellCrit, Unit.Main/SendAttackStateUpdate | — |
