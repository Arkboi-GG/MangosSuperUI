# PetAura

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# PetAura

**Purpose & Responsibilities**

`PetAura` is a lightweight data structure within `SpellMgr.h` that defines the configuration for passive auras applied to player pets. It serves as the container for mapping specific pet creature entries to their corresponding aura spell IDs, while also storing metadata regarding whether these auras should be removed when the pet changes and any associated damage values.

The class operates as part of the server's static spell data management system. It does not perform runtime logic itself but provides query methods (`GetAura`, `IsRemovedOnChangePet`, `GetDamage`) that are invoked by higher-level game logic—specifically `Pet.Main` and `Unit.Main`—to determine which buffs or debuffs a pet should possess upon summoning or during its lifecycle.

**Member-by-Member Behavior**

The `PetAura` class manages a map of pet entries to aura spell IDs, along with two scalar flags. Its members are grouped by their functional role: initialization, data retrieval, and data modification.

### Initialization

*   **`PetAura` (Default Constructor)**
    Initializes an empty `PetAura` instance. It sets `removeOnChangePet` to `false`, `damage` to `0`, and clears the internal `auras` map. This constructor is likely used for default initialization or placeholder objects, though the MAP indicates it is not called by other units in the current codebase snapshot.

*   **`PetAura` (Parameterized Constructor)**
    Constructs a `PetAura` instance with specific configuration. It takes a `petEntry` (creature ID), an `aura` (spell ID), a boolean `_removeOnChangePet`, and an integer `_damage`. It initializes the `auras` map by inserting the pair `{petEntry, aura}` and sets the remaining member variables accordingly. This constructor is exclusively called by `SpellMgr/LoadSpellPetAuras` during server startup to populate the spell manager's pet aura registry from database records.

### Data Retrieval

*   **`GetAura`**
    Retrieves the aura spell ID for a given `petEntry`. This method implements a fallback mechanism:
    1.  It first attempts to find an exact match for the provided `petEntry` in the internal `auras` map.
    2.  If no exact match is found, it checks for a default entry keyed by `0`.
    3.  If neither exists, it returns `0` (indicating no aura).
    
    This design allows for specific pet configurations while permitting a global default aura for unspecified pets. This method is called by `Pet.Main/CastPetAura` to apply the correct buff when a pet is summoned, and by `Unit.Main/RemovePetAura` to identify which aura needs to be stripped when a pet is dismissed or changed.

*   **`IsRemovedOnChangePet`**
    Returns the value of the `removeOnChangePet` flag. This boolean dictates whether the aura should persist across pet swaps or be removed immediately when the player summons a different pet. It is queried by `Pet.Main/CastPetAuras` to manage aura lifecycle during pet transitions.

*   **`GetDamage`**
    Returns the `damage` value stored in the instance. While the MAP indicates this method is defined, it shows no callers in other units. This suggests it may be reserved for future features, debugging, or specific aura types that require damage scaling, though currently unused in the documented cross-unit interactions.

### Data Modification

*   **`AddAura`**
    Inserts or updates an aura mapping for a specific `petEntry`. It places the given `aura` spell ID into the `auras` map under the key `petEntry`. If an entry already exists for that pet, it is overwritten. This method is called by `SpellMgr/LoadSpellPetAuras` to build the lookup table from database rows.

**Cross-Unit Boundaries**

`PetAura` acts as a pure data provider for the spell and pet subsystems. It does not initiate actions but responds to queries from core game entities.

*   **Called by `SpellMgr/LoadSpellPetAuras`**:
    During server startup, `SpellMgr` iterates through database records defining pet auras. For each record, it constructs a `PetAura` object (using the parameterized constructor) or populates an existing one via `AddAura`. This establishes the static configuration map `mSpellPetAuraMap` within `SpellMgr`.

*   **Called by `Pet.Main/CastPetAura` and `Pet.Main/CastPetAuras`**:
    When a player summons a pet, the `Pet` module queries `PetAura` instances to determine which spells to cast on the newly created pet entity. `CastPetAura` uses `GetAura` to find the specific spell ID for the pet's creature entry. `CastPetAuras` uses `IsRemovedOnChangePet` to decide if previous auras should be cleaned up before applying new ones.

*   **Called by `Unit.Main/RemovePetAura`**:
    When a pet is dismissed, swapped, or dies, the `Unit` module needs to clean up active effects. It calls `GetAura` to retrieve the spell ID associated with the pet so it can explicitly remove that aura from the pet's unit state.

**Data Model**

The `PetAura` class does not interact with database tables directly. It consumes data prepared by `SpellMgr::LoadSpellPetAuras`. Based on the constructor parameters and typical MaNGOS/WowVMangos structures, the underlying database table (likely `spell_pet_aura` or similar) contains columns corresponding to:
*   `petEntry`: The creature ID of the pet.
*   `aura`: The spell ID of the aura to apply.
*   `removeOnChangePet`: A boolean flag (often stored as TINYINT or BIT).
*   `damage`: An integer value for damage scaling.

Since no SCHEMA section was provided, specific column types and constraints are not cited here. The class assumes the data loaded into it is valid and consistent with the `uint32` and `bool` types expected by its members.

**Notable Implementation Details**

1.  **Fallback Logic in `GetAura`**:
    The most significant behavioral detail is the fallback to key `0` in `GetAura`. This allows database designers to define a "default" pet aura by setting the `petEntry` to `0`. Any pet not explicitly listed in the database will receive this default aura. This reduces redundancy in the database for common pet behaviors.

2.  **Overwriting Behavior in `AddAura`**:
    `AddAura` uses `std::map::operator[]`, which inserts a new element or overwrites the existing value if the key already exists. This means that if the database contains duplicate entries for the same `petEntry`, the last one processed during loading will take precedence. Order of processing depends on the database query result set order.

3.  **Unused `GetDamage`**:
    The `GetDamage` method and the `damage` member variable are present but have no documented callers in the MAP. This indicates dead code or a feature that was partially implemented but not yet integrated into the main pet casting logic. Maintainers should verify if this field is intended for use in future patches or if it can be safely ignored.

4.  **Const Correctness**:
    All retrieval methods (`GetAura`, `IsRemovedOnChangePet`, `GetDamage`) are marked `const`, ensuring that querying the aura configuration does not modify the `PetAura` instance. This is crucial because `SpellMgr` holds these objects in a `const` map (`SpellPetAuraMap`), and multiple threads might access spell data concurrently (though thread safety depends on the broader engine context).

## Member Reference

*   **PetAura**: Default constructor that initializes an empty `PetAura` with `removeOnChangePet` set to `false`, `damage` to `0`, and an empty `auras` map.
*   **PetAura#2**: Parameterized constructor that initializes the `auras` map with a specific `petEntry` and `aura` pair, and sets `removeOnChangePet` and `damage` from arguments; called by `SpellMgr/LoadSpellPetAuras`.
*   **GetAura**: Returns the aura spell ID for a given `petEntry`, falling back to the aura mapped to key `0` if no specific entry exists, otherwise returning `0`; called by `Pet.Main/CastPetAura` and `Unit.Main/RemovePetAura`.
*   **AddAura**: Inserts or updates the aura spell ID for a specific `petEntry` in the internal map; called by `SpellMgr/LoadSpellPetAuras`.
*   **IsRemovedOnChangePet**: Returns the boolean flag indicating whether the aura should be removed when the pet changes; called by `Pet.Main/CastPetAuras`.
*   **GetDamage**: Returns the stored `damage` value; currently has no documented callers in other units.

---

<!-- machine-true, projected from graph.json -->

## Map — PetAura

*Source:* SpellMgr.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| PetAura | ctor | — | — | — |
| PetAura#2 | ctor | — | SpellMgr/LoadSpellPetAuras | — |
| GetAura | method | — | Pet.Main/CastPetAura, Unit.Main/RemovePetAura | — |
| AddAura | method | — | SpellMgr/LoadSpellPetAuras | — |
| IsRemovedOnChangePet | method | — | Pet.Main/CastPetAuras | — |
| GetDamage | method | — | — | — |
