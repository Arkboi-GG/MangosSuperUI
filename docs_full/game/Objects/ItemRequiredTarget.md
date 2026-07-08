# ItemRequiredTarget

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ItemRequiredTarget

## Purpose & Responsibilities

`ItemRequiredTarget` is a lightweight data structure defined in `ItemDefines.h` that encapsulates the targeting prerequisites for specific items within the World of Warcraft server emulation. Its primary responsibility is to define **what** a player must target in order to successfully use a particular item.

The structure supports two distinct targeting modes, defined by the `ItemRequiredTargetType` enum:
1.  **Creature Target (`ITEM_TARGET_TYPE_CREATURE`)**: The item requires the player to target a living creature.
2.  **Dead Target (`ITEM_TARGET_TYPE_DEAD`)**: The item requires the player to target a dead creature (corpse).

This abstraction separates the definition of the requirement (type and specific creature entry ID) from the validation logic. It allows the game logic to validate whether a player's current focus meets the prerequisites for using an item before attempting to execute the item's effect.

## Member-by-Member Behavior

### Construction

**`ItemRequiredTarget` (Constructor)**
The constructor initializes the struct with two parameters:
1.  `uiType`: An `ItemRequiredTargetType` indicating whether the target must be a living creature or a corpse.
2.  `uiTargetEntry`: A `uint32` representing the specific creature entry ID (database identifier) required.

This constructor is exclusively called by `ObjectMgr::LoadItemRequiredTarget` during server startup or data reloads, populating these structures from static configuration data.

### Validation Logic

**`IsFitToRequirements`**
Although declared in the struct, the implementation of `IsFitToRequirements` is not present in `ItemDefines.h`. Based on the signature `bool IsFitToRequirements(Unit* pUnitTarget) const`, this method evaluates whether a given `Unit` pointer satisfies the constraints stored in `m_uiType` and `m_uiTargetEntry`.

Typically, this logic involves:
1.  Checking if `pUnitTarget` is valid.
2.  Verifying the unit's state (alive vs. dead) matches `m_uiType`.
3.  Comparing the unit's creature entry ID against `m_uiTargetEntry`.

*Note: Since the implementation is not in this file, the exact bitwise or conditional checks are inferred from standard WoW mechanics but are not documented here as they reside in another translation unit.*

## Cross-Unit Boundaries

### Called By: `ObjectMgr/LoadItemRequiredTarget`

*   **Direction**: Inbound (Other unit calls this unit).
*   **Context**: During the server initialization phase, the `ObjectMgr` (Object Manager) loads static game data. Specifically, `ObjectMgr::LoadItemRequiredTarget` parses configuration data (likely from SQL tables or flat files) that define which items require specific targets.
*   **Data Exchange**: `ObjectMgr` constructs `ItemRequiredTarget` instances by passing the parsed type and creature entry ID to the constructor. These instances are likely stored in a global map or vector within `ObjectMgr` to allow quick lookup when a player attempts to use an item.

### Calls Out: None

The `ItemRequiredTarget` struct itself does not actively call out to other units in its definition. The constructor is simple assignment, and the `IsFitToRequirements` method (while it may call into `Unit` methods) is implemented elsewhere.

## Data Model

The `ItemRequiredTarget` struct does not directly interact with database tables in its definition. However, its population relies on data loaded by `ObjectMgr`. In typical WoW server architectures, this data originates from a table such as `item_required_target` (or similar naming convention depending on the specific DB schema version), containing columns for:
*   `entry` (Item Entry ID)
*   `target_type` (Corresponding to `ItemRequiredTargetType`)
*   `target_entry` (Corresponding to `m_uiTargetEntry`)

Since the SCHEMA section was not provided in the input, specific column names, types, and keys cannot be cited. The struct merely holds the runtime representation of this data.

## Notable Implementation Details

1.  **Const Correctness**: The `IsFitToRequirements` method is marked `const`, indicating it does not modify the internal state of the `ItemRequiredTarget` object. This allows it to be called on const references, which is efficient for lookups.
2.  **Minimalist Design**: The struct contains no virtual functions, no dynamic memory allocation, and no complex lifecycle management. It is a Plain Old Data (POD)-like structure, making it cheap to copy and store in large containers.
3.  **Dependency on `Unit`**: The validation method depends on the `Unit` class (forward-declared or included via `Common.h`). This ties the item requirement logic to the core entity system, ensuring that only valid game entities can be evaluated against these requirements.
4.  **Enum Constraints**: The `ItemRequiredTargetType` enum explicitly defines only two valid states (Creature and Dead). Any other value would be invalid, implying that the loading logic in `ObjectMgr` must sanitize input data to ensure only these two types are instantiated.

## Member Reference

**ItemRequiredTarget**
Constructor that initializes the struct with a target type (`ItemRequiredTargetType`) and a specific creature entry ID (`uint32`). It is called exclusively by `ObjectMgr::LoadItemRequiredTarget` to populate item requirement data from static sources.

---

<!-- machine-true, projected from graph.json -->

## Map — ItemRequiredTarget

*Source:* ItemDefines.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ItemRequiredTarget | ctor | — | ObjectMgr/LoadItemRequiredTarget | — |
