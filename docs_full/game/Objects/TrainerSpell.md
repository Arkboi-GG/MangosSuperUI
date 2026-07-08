# TrainerSpell

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# TrainerSpell

**Purpose & Responsibilities**

`TrainerSpell` is a lightweight Plain Old Data (POD) structure defined in `CreatureDefines.h`. Its sole responsibility is to represent the metadata associated with a single spell offered by a trainer Non-Player Character (NPC). It encapsulates the spell identifier, the cost to learn it, and the prerequisites (skill and level) required for a player to purchase it.

This structure serves as the value type in the `TrainerSpellMap` (`std::unordered_map<uint32, TrainerSpell>`), which is held by `TrainerSpellData`. It acts as the bridge between the database configuration of trainer NPCs and the runtime logic that validates whether a player can learn a specific spell.

**Member-by-Member Behavior**

The unit consists entirely of two constructors. There are no methods, virtual functions, or complex logic.

1.  **Default Constructor**: Initializes all member variables to zero. This ensures that if a `TrainerSpell` instance is created without arguments, it represents a null or invalid spell entry (spell ID 0, cost 0, etc.).
2.  **Parameterized Constructor**: Accepts five `uint32` arguments corresponding to the spell ID, cost, required skill ID, required skill value, and required character level. It assigns these values directly to the respective member fields.

**Cross-Unit Boundaries**

According to the provided MAP, `TrainerSpell` has no outgoing calls to other units and is not called by other units in the context of this specific structural map. However, in the broader codebase context evident from the header:
*   It is instantiated by code that populates `TrainerSpellData` (likely in `TrainerHandler.cpp` or similar trainer-related handlers), which reads from the `trainer_spells` database table.
*   It is consumed by trainer interaction logic that checks `reqSkill`, `reqSkillValue`, and `reqLevel` against the player's current state.

**Data Model**

While the MAP indicates no direct table access for this unit, the structure maps directly to the columns of the `trainer_spells` table in the World of Warcraft database schema. The fields correspond as follows:
*   `spell`: The `SpellId` column.
*   `spellCost`: The `SpellCost` column.
*   `reqSkill`: The `ReqSkill` column (skill ID).
*   `reqSkillValue`: The `ReqSkillValue` column (minimum skill level).
*   `reqLevel`: The `ReqLevel` column (minimum character level).

**Notable Implementation Details**

*   **Zero-Initialization Safety**: The default constructor explicitly sets all fields to `0`. This is critical because `spell` (the key in the parent map) being `0` typically indicates an invalid or empty entry. Maintainers relying on `if (trainerSpell.spell)` checks depend on this initialization.
*   **No Validation Logic**: The constructor performs no validation. It does not check if the `spell` ID exists in the `Spell.dbc` or if the `reqSkill` is valid. This validation is deferred to the higher-level trainer handling logic.
*   **Const-Correctness Absence**: The members are mutable `uint32` fields, not `const`. While `TrainerSpell` instances are usually populated once at load time and then read-only during gameplay, the structure allows modification. This flexibility might be used for dynamic cost adjustments or debugging, though it is rare in standard operation.
*   **Part of a Larger System**: `TrainerSpell` is tightly coupled with `TrainerSpellData` and `TrainerSpellMap` in the same header. It is not designed to be used in isolation outside of the trainer system.

## Member Reference

**TrainerSpell** (default ctor): Initializes all member variables (`spell`, `spellCost`, `reqSkill`, `reqSkillValue`, `reqLevel`) to `0`. Creates an empty/invalid trainer spell entry.

**TrainerSpell#2** (parameterized ctor): Initializes the `TrainerSpell` instance with the provided arguments: `_spell` to `spell`, `_spellCost` to `spellCost`, `_reqSkill` to `reqSkill`, `_reqSkillValue` to `reqSkillValue`, and `_reqLevel` to `reqLevel`. Used to create a valid trainer spell entry from database data.

---

<!-- machine-true, projected from graph.json -->

## Map — TrainerSpell

*Source:* CreatureDefines.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| TrainerSpell | ctor | — | — | — |
| TrainerSpell#2 | ctor | — | — | — |
