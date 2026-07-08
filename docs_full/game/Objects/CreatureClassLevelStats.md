# CreatureClassLevelStats

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# CreatureClassLevelStats

**Purpose & Responsibilities**
`CreatureClassLevelStats` is a lightweight Plain Old Data (POD) struct defined in `CreatureDefines.h`. Its sole responsibility is to aggregate the base statistical attributes for a creature based on its class and level. It acts as a container for raw numerical values—such as health, mana, armor, and damage ranges—that are calculated elsewhere and stored here for efficient retrieval. It contains no logic, no virtual functions, and no dynamic memory management; it is purely a data carrier.

The struct is designed to hold both current derived stats (e.g., `melee_damage`) and base stats (e.g., `base_health`, `base_mana`), allowing the game engine to distinguish between inherent capabilities and those modified by buffs, gear, or other runtime factors.

## Member-by-Member Behavior

The unit consists entirely of two constructors. There are no methods, getters, or setters.

### Constructors

1.  **Default Constructor (`CreatureClassLevelStats()`)**
    *   **Behavior:** Initializes all member variables to their default values. Floating-point members (`melee_damage`, `ranged_damage`) are initialized to `0.0f`. Integer members (`attack_power`, `health`, etc.) are initialized to `0`.
    *   **Usage:** Used when creating an empty instance that will be populated later, or when the object is part of a larger structure that requires default initialization.

2.  **Parameterized Constructor (`CreatureClassLevelStats(float, float, int32, ...)`)**
    *   **Behavior:** Accepts 14 arguments corresponding to each of the struct's data members. It initializes each member via the initializer list.
    *   **Arguments:**
        *   `melee_damage_`: Base melee damage range.
        *   `ranged_damage_`: Base ranged damage range.
        *   `attack_power_`: Melee attack power.
        *   `ranged_attack_power_`: Ranged attack power.
        *   `health_`: Current/max health value.
        *   `base_health_`: Unmodified health value.
        *   `mana_`: Current/max mana value.
        *   `base_mana_`: Unmodified mana value.
        *   `strength_`: Strength attribute.
        *   `agility_`: Agility attribute.
        *   `stamina_`: Stamina attribute.
        *   `intellect_`: Intellect attribute.
        *   `spirit_`: Spirit attribute.
        *   `armor_`: Armor value.
    *   **Usage:** Used to create a fully populated stats object in a single expression, typically during the loading or calculation phase of creature statistics.

## Cross-Unit Boundaries

*   **Called by `ObjectMgr/GetCreatureClassLevelStats`:**
    *   **Direction:** Outbound (from `ObjectMgr` to `CreatureClassLevelStats`).
    *   **Collaboration:** The `ObjectMgr` unit (likely responsible for managing global game data and lookups) calls the parameterized constructor of `CreatureClassLevelStats` to instantiate a stats object. This suggests that `ObjectMgr` performs the calculation or lookup of these statistics (possibly from DBC files or internal tables) and returns a populated `CreatureClassLevelStats` instance to the caller. The `CreatureClassLevelStats` unit itself does not perform any lookups; it merely provides the structure to hold the result.

## Data Model

This unit does not interact directly with any database tables. It is a pure C++ data structure. The data it holds is likely derived from DBC (Data Block Chunk) files or internal calculations performed by `ObjectMgr`, but no SQL queries or table references are present in this unit.

## Notable Implementation Details

*   **No Encapsulation:** All member variables are public. This allows direct read/write access from any part of the codebase that includes `CreatureDefines.h`. While this simplifies usage, it means there is no validation or invariant checking on the stat values.
*   **Default Initialization:** The default constructor relies on the in-class initializers (`= 0.0f`, `= 0`). This ensures that even if the default constructor is used, the object starts in a zeroed-out state, preventing undefined behavior from uninitialized memory.
*   **Memory Layout:** As a POD struct with no virtual functions or complex members, it has a predictable memory layout. This makes it suitable for use in arrays, maps, or serialization contexts where contiguous memory access is beneficial.
*   **Separation of Concerns:** The struct separates "current" stats (e.g., `health`) from "base" stats (e.g., `base_health`). This distinction is critical for games with buff/debuff systems, as it allows the engine to calculate temporary modifications relative to a stable baseline.

## Member Reference

**CreatureClassLevelStats**
Default constructor. Initializes all floating-point members to `0.0f` and all integer members to `0`.

**CreatureClassLevelStats#2**
Parameterized constructor. Accepts 14 arguments (`melee_damage_`, `ranged_damage_`, `attack_power_`, `ranged_attack_power_`, `health_`, `base_health_`, `mana_`, `base_mana_`, `strength_`, `agility_`, `stamina_`, `intellect_`, `spirit_`, `armor_`) and initializes the corresponding member variables via initializer list. Called by `ObjectMgr/GetCreatureClassLevelStats`.

---

<!-- machine-true, projected from graph.json -->

## Map — CreatureClassLevelStats

*Source:* CreatureDefines.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| CreatureClassLevelStats | ctor | — | — | — |
| CreatureClassLevelStats#2 | ctor | — | ObjectMgr/GetCreatureClassLevelStats | — |
