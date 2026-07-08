# CreatureSpellsEntry

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# CreatureSpellsEntry

**Purpose & Responsibilities**

`CreatureSpellsEntry` is a lightweight Plain Old Data (POD) struct defined in `CreatureDefines.h`. It represents a single spell configuration entry within a creature's spell template system. Specifically, it defines the parameters for one spell that a creature might cast, including the spell ID, casting probability, target selection logic, timing delays, and associated script triggers.

This struct is designed to be stored in contiguous memory blocks (typically within `std::vector<CreatureSpellsEntry>` aliases like `CreatureSpellsList`) to allow efficient iteration during creature AI updates or spell casting routines. It contains no methods other than its constructor, serving purely as a data carrier for spell behavior definitions loaded from the database or hardcoded templates.

**Member-by-Member Behavior**

The unit consists of a single member: the constructor.

*   **`CreatureSpellsEntry` (Constructor)**: Initializes all member fields of the struct. It takes eleven arguments corresponding to the columns typically found in the `creature_template_spell` database table (or equivalent internal representation). The constructor assigns these values to the respective `const` member variables, ensuring the data is immutable after construction.

**Cross-Unit Boundaries**

As a simple data structure with only a constructor, `CreatureSpellsEntry` has no outgoing calls to other units. It is not called by other units in the sense of function invocation; rather, instances of this struct are constructed by other units (such as data loading modules in `CreatureTemplate` handlers or AI scripts) when populating spell lists for creatures. The MAP indicates no specific "Called by" entries, consistent with its role as a passive data container.

**Data Model**

While the MAP lists no specific tables, the structure of `CreatureSpellsEntry` directly mirrors the schema of the `creature_template_spell` table in the WowVMaNGOS database. The fields correspond to:
*   `spellId`: The unique identifier of the spell.
*   `probability`: The chance (%) for the spell to be selected/cast.
*   `castTarget`: The target selection mode (e.g., self, random enemy).
*   `targetParam1`, `targetParam2`: Parameters for the target selection logic.
*   `castFlags`: Flags modifying cast behavior.
*   `delayInitialMin`, `delayInitialMax`: Range for the initial cast delay.
*   `delayRepeatMin`, `delayRepeatMax`: Range for subsequent cast repeats.
*   `scriptId`: An optional script ID to execute on cast events.

**Notable Implementation Details**

1.  **Immutability**: All member variables (`spellId`, `probability`, etc.) are declared as `const`. This enforces immutability, meaning once a `CreatureSpellsEntry` is created, its data cannot be modified. This is a safety feature to prevent accidental corruption of spell templates during runtime.
2.  **Memory Layout**: The struct is part of `CreatureDefines.h`, which uses `#pragma pack(1)` for some structures (like `CreatureInfo`), but `CreatureSpellsEntry` itself is not explicitly packed in this snippet. However, it is designed for vector storage (`CreatureSpellsList`), implying tight packing is desirable for cache efficiency during AI loops.
3.  **No Validation**: The constructor performs no validation on the input parameters (e.g., checking if `probability` is between 0 and 100, or if `delayInitialMin` <= `delayInitialMax`). Validation is assumed to happen at the data loading stage (SQL parsing) or is ignored, relying on the game engine to handle invalid values gracefully (or crash, depending on downstream usage).
4.  **Fixed Size**: The struct has a fixed size determined by its 11 members. This allows for predictable memory allocation when creating arrays or vectors of spell entries.

## Member Reference

**CreatureSpellsEntry**
Constructor for the `CreatureSpellsEntry` struct. It initializes the following `const` member variables with the provided arguments: `spellId` (uint16), `probability` (uint8), `castTarget` (uint8), `targetParam1` (uint32), `targetParam2` (uint32), `castFlags` (uint16), `delayInitialMin` (uint32), `delayInitialMax` (uint32), `delayRepeatMin` (uint32), `delayRepeatMax` (uint32), and `scriptId` (uint32). This ensures the spell configuration data is immutable after creation.

---

<!-- machine-true, projected from graph.json -->

## Map — CreatureSpellsEntry

*Source:* CreatureDefines.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| CreatureSpellsEntry | ctor | — | — | — |
