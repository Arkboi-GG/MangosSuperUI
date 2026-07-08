# EquipmentTemplate

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# EquipmentTemplate

**Purpose & Responsibilities**

The `EquipmentTemplate` struct, defined in `CreatureDefines.h`, represents a probabilistic set of equipment configurations for a creature. It allows a single creature template to define multiple possible gear sets (e.g., different weapons or armor combinations) and assigns a weight (probability) to each set. Its primary responsibility is to select one specific equipment configuration from this set during the creature's initialization process, ensuring that the visual appearance and equipped items match the intended design for that specific spawn instance.

This unit is part of the broader creature definition system, working alongside `CreatureInfo` (which holds the `equipment_id` linking to this template) and `EquipmentEntry` (which defines the individual slots of a single gear set).

## Member-by-Member Behavior

### **ChooseEquipmentEntry**

This method implements a weighted random selection algorithm to determine which equipment set a creature should wear.

1.  **Validation**: It first checks if `totalProbability` is zero. If so, it returns `nullptr`, indicating no valid equipment configuration exists or the template is empty.
2.  **Roll Generation**: It generates a random integer `roll` between `0` and `totalProbability - 1` using the utility function `urand`.
3.  **Cumulative Sum Selection**: It iterates through the `equipment` vector (`std::vector<EquipmentEntry>`). For each entry:
    *   It skips entries with a `probability` of zero.
    *   It adds the current entry's `probability` to a running `sum`.
    *   If the generated `roll` is less than the current `sum`, it has found the selected entry and returns a pointer to it.
4.  **Fallback**: If the loop completes without finding a match (which theoretically shouldn't happen if `totalProbability` accurately reflects the sum of probabilities and the roll is within bounds), it returns `nullptr`.

This approach ensures that equipment sets with higher probability values are more likely to be selected, proportional to their weight relative to the total.

## Cross-Unit Boundaries

*   **Called by `Creature.Main/LoadEquipment`**: The `Creature` class (specifically its main loading logic) invokes `ChooseEquipmentEntry` when initializing a creature instance. The `Creature` unit passes the `EquipmentTemplate` associated with the creature's `equipment_id`. The result (a pointer to an `EquipmentEntry`) is used by `Creature` to apply the specific items (main hand, off-hand, ranged) to the creature's virtual item slots. This boundary crossing transfers the decision of *which* gear set to use from the static template data (`EquipmentTemplate`) to the dynamic creature instance (`Creature`).

## Data Model

This unit does not directly interact with database tables. It operates on in-memory data structures (`EquipmentTemplate` and `EquipmentEntry`) that are populated from the `creature_template` and `creature_template_equipment` tables by other parts of the server (likely during world load). The `EquipmentTemplate` struct itself is a transient representation of the data defined in those tables for a specific creature entry.

## Notable Implementation Details

*   **Weighted Randomness**: The selection algorithm relies on `totalProbability` being the sum of all non-zero `probability` values in the `equipment` vector. If `totalProbability` is incorrect (e.g., not updated when the vector changes), the selection weights will be skewed. The caller or the loader responsible for populating `EquipmentTemplate` must ensure `totalProbability` is correctly calculated.
*   **Zero Probability Handling**: Entries with `probability == 0` are explicitly skipped in the loop. This allows for placeholder or disabled equipment sets within the same template without affecting the random selection of active sets.
*   **Return Type**: Returns a `const` pointer to an `EquipmentEntry`. This indicates that the selected entry is not modified by the caller, preserving the integrity of the template data.
*   **Edge Case - Empty Template**: If `totalProbability` is 0, the method immediately returns `nullptr`. The calling code (`Creature.Main/LoadEquipment`) must handle this case, likely by equipping nothing or using a default fallback.
*   **Edge Case - Roll Mismatch**: If `roll` is greater than or equal to the sum of all probabilities (due to floating-point issues or incorrect `totalProbability` calculation), the loop finishes and returns `nullptr`. This is a potential bug if `totalProbability` is not strictly maintained.

## Member Reference

**ChooseEquipmentEntry**: Selects an `EquipmentEntry` from the `equipment` vector based on weighted probabilities. Uses `urand` to generate a roll within `totalProbability` and iterates through entries, accumulating probabilities until the roll falls within the current entry's range. Returns `nullptr` if `totalProbability` is zero or if no entry matches the roll.

---

<!-- machine-true, projected from graph.json -->

## Map — EquipmentTemplate

*Source:* CreatureDefines.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ChooseEquipmentEntry | method | — | Creature.Main/LoadEquipment | — |
