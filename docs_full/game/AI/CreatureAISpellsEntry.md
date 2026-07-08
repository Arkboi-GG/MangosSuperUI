# CreatureAISpellsEntry

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# CreatureAISpellsEntry

**Purpose & Responsibilities**

`CreatureAISpellsEntry` is a lightweight data structure that represents a single spell configuration for a creature's artificial intelligence. It extends `CreatureSpellsEntry` (defined in `CreatureDefines.h`) by adding a runtime-calculated `cooldown` field. Its sole responsibility is to initialize this cooldown value during construction by selecting a random integer between the minimum and maximum initial delay bounds provided by the base `CreatureSpellsEntry`. This structure allows the AI system to store pre-randomized timing data for spells, avoiding repeated random number generation during the game loop.

**Member-by-Member Behavior**

The unit contains only one member: the constructor.

*   **`CreatureAISpellsEntry`**: This constructor accepts a constant reference to a `CreatureSpellsEntry`. It initializes the base class portion of the object using the provided `EntryStruct`. Crucially, it initializes the `cooldown` member by calling `urand` (a utility function for generating uniform random integers) with `EntryStruct.delayInitialMin` and `EntryStruct.delayInitialMax` as arguments. This ensures that every instance of `CreatureAISpellsEntry` has a unique, randomized initial cooldown duration derived from the static definition in the database or configuration.

**Cross-Unit Boundaries**

*   **Calls Out**: The constructor calls `urand`, which is part of the core utility library (not explicitly listed in the map but evident in the source). It also implicitly calls the constructor of `CreatureSpellsEntry` (from `CreatureDefines.h`).
*   **Called By**: The map indicates no external callers. In practice, instances of this struct are likely created by the `CreatureAI` class (specifically methods like `SetSpellsList` or `UpdateSpellsList`, though these belong to the `CreatureAI` unit, not this partial) when loading spell templates for a creature.

**Data Model**

This unit does not directly interact with database tables. It operates on data structures (`CreatureSpellsEntry`) that are typically populated from the `creature_spells` table in the database, but the mapping and retrieval logic reside in other units (e.g., `CreatureAI` or database loaders). The `CreatureAISpellsEntry` itself holds no persistent data.

**Notable Implementation Details**

*   **Randomization at Construction**: The randomness of the cooldown is determined once at object creation. This means that if multiple creatures share the same spell template, each will get a different initial cooldown, preventing synchronized spell casting across groups of identical mobs.
*   **Inheritance**: It inherits publicly from `CreatureSpellsEntry`. This implies that all fields from `CreatureSpellsEntry` (such as spell ID, chance, etc.) are accessible via `CreatureAISpellsEntry`.
*   **No Virtual Functions**: As a simple struct with no virtual methods, it has no vtable overhead, making it efficient for storage in vectors (as seen in `CreatureAI::m_CreatureSpells`).

## Member Reference

**CreatureAISpellsEntry**  
Constructor that initializes the base `CreatureSpellsEntry` with the provided `EntryStruct` and sets the `cooldown` member to a random value between `EntryStruct.delayInitialMin` and `EntryStruct.delayInitialMax` using `urand`.

---

<!-- machine-true, projected from graph.json -->

## Map — CreatureAISpellsEntry

*Source:* CreatureAI.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| CreatureAISpellsEntry | ctor | — | — | — |
