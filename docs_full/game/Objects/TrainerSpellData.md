# TrainerSpellData

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# TrainerSpellData

**Purpose & Responsibilities**

`TrainerSpellData` is a lightweight aggregate struct defined in `CreatureDefines.h` that encapsulates the spell list and classification metadata for a game trainer NPC. Its primary responsibility is to store the mapping of teachable spells (`TrainerSpellMap`) and the specific type of trainer (`trainerType`) associated with that data.

This struct serves as the data payload for trainer NPCs, allowing the server to distinguish between different kinds of trainers (e.g., Class Trainers, Trade Skill Trainers) and to quickly look up whether a specific spell is available for purchase from that trainer. It is designed to be populated during the server's initialization phase by the object manager and cleared during shutdown.

**Member-by-Member Behavior**

The unit consists of two members: a constructor and a cleanup method.

*   **Constructor (`TrainerSpellData`)**: Initializes the `trainerType` member to `0`. The `spellList` member, being a `std::unordered_map`, is default-initialized to an empty state by the compiler. This ensures that a newly instantiated `TrainerSpellData` object starts with a clean slate, representing a trainer with no spells and a default type.
*   **`Clear`**: Destroys the contents of the `spellList` map by calling `clear()` on it. This releases all memory held by the `TrainerSpell` objects stored in the map. It does *not* reset the `trainerType` field. This method is intended for resource reclamation when the server is shutting down or reloading data, ensuring that dynamically allocated spell data does not leak.

**Cross-Unit Boundaries**

`TrainerSpellData` is a pure data structure with no internal logic that calls out to other units. However, it is tightly coupled with the **ObjectMgr** unit, which manages the lifecycle of global game data.

*   **Called by `ObjectMgr/LoadTrainers#2`**: During the server startup sequence, `ObjectMgr` parses trainer data from the database and populates instances of `TrainerSpellData`. Specifically, it fills the `spellList` map with `TrainerSpell` entries and sets the `trainerType` based on the loaded data. This establishes the initial state of all trainers in the world.
*   **Called by `ObjectMgr/~ObjectMgr`**: When the `ObjectMgr` singleton is destroyed (typically at server shutdown), it iterates through its collection of trainer data and calls `Clear` on each `TrainerSpellData` instance. This ensures that all heap-allocated memory within the `spellList` maps is freed before the process exits.

**Data Model**

`TrainerSpellData` does not directly interact with database tables via SQL queries within its own definition. Instead, it acts as an in-memory representation of data sourced from the `trainer_spell` and `creature_template` tables (implied by the `ObjectMgr` integration). The struct itself contains no SQL logic.

**Notable Implementation Details**

*   **Type Derivation**: The comment on `trainerType` notes that this value is "based at trainer spells" and "can be different from creature_template value." This implies that while the `creature_template` table defines a general trainer type, the actual `TrainerSpellData` instance may override or refine this type based on the specific spells loaded into its `spellList`. This allows for nuanced trainer behavior, such as a "Weapon Master" (non-profession trainer) being correctly identified despite potentially having a generic template type.
*   **Memory Management**: The `Clear` method only clears the `spellList`. It relies on the caller (`ObjectMgr`) to manage the lifetime of the `TrainerSpellData` struct itself. If `TrainerSpellData` were used in a context where it was reused rather than destroyed, the `trainerType` would persist across clears, which could lead to stale type information if not explicitly reset by the caller.
*   **Lookup Efficiency**: The use of `std::unordered_map<uint32, TrainerSpell>` for `spellList` provides average O(1) complexity for spell lookups via the `Find` method (defined in the shared header but implemented elsewhere, likely inline or in a related unit). This is critical for performance, as trainer interactions may involve checking multiple spells rapidly.

## Member Reference

**TrainerSpellData**
Constructor that initializes the `trainerType` member to `0`. The `spellList` is default-initialized to empty.

**Clear**
Method that calls `clear()` on the internal `spellList` unordered map, freeing all contained `TrainerSpell` entries. It does not reset `trainerType`. Called by `ObjectMgr::~ObjectMgr` during shutdown.

---

<!-- machine-true, projected from graph.json -->

## Map — TrainerSpellData

*Source:* CreatureDefines.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| TrainerSpellData | ctor | — | — | — |
| Clear | method | — | ObjectMgr/LoadTrainers#2, ObjectMgr/~ObjectMgr | — |
