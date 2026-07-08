<!-- provenance: failed-members -->
# HealSpellCompare

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# HealSpellCompare

**Purpose & Responsibilities**

`HealSpellCompare` is a functor (function object) defined in `CombatBotBaseAI.h` that provides a strict weak ordering for pointers to `SpellEntry` structures. Its specific responsibility is to compare two spells based on their total base healing potential. It is designed to be used as a comparator for standard library containers (such as `std::set`) that require elements to be sorted or ordered, allowing the bot AI to efficiently retrieve the "strongest" direct healing spell from a collection.

The comparator aggregates the `EffectBasePoints` of all effects within a spell that are of type `SPELL_EFFECT_HEAL`. It returns `true` if the first spell (`lhs`) has a higher total base heal value than the second spell (`rhs`). This enables the bot to prioritize spells with higher raw healing output when selecting a direct heal.

**Member-by-Member Behavior**

### **operator()**

This method implements the comparison logic required by the `std::binary_function` interface (implicitly via the functor pattern).

1.  **Initialization**: It initializes two accumulators, `spell1dmg` and `spell2dmg`, to zero. Despite the variable names suggesting damage, these variables store healing values.
2.  **Left-Hand Side (LHS) Aggregation**:
    *   It iterates through all possible spell effects (indexed `0` to `MAX_SPELL_EFFECTS - 1`) for the `lhs` spell.
    *   For each effect, it checks if `lhs->Effect[i]` equals `SPELL_EFFECT_HEAL`.
    *   If it matches, it adds `lhs->EffectBasePoints[i]` to `spell1dmg`.
    *   Note: It ignores other effect types (e.g., buffs, damage, auras) entirely.
3.  **Right-Hand Side (RHS) Aggregation**:
    *   It performs the identical iteration and accumulation process for the `rhs` spell, adding matching `EffectBasePoints` to `spell2dmg`.
4.  **Comparison**:
    *   It returns `true` if `spell1dmg > spell2dmg`.
    *   It returns `false` otherwise (including cases where they are equal or `rhs` is greater).

**Cross-Unit Boundaries**

*   **Called By**: The MAP indicates no external callers are explicitly tracked in the cross-reference data provided. However, in the context of `CombatBotBaseAI.h`, this functor is instantiated as the comparator for the member variable `m_spellListDirectHeal` (declared as `std::set<SpellEntry const*, HealSpellCompare>`). Therefore, it is implicitly called by the `std::set` container methods (insertion, lookup, traversal) used by the `CombatBotBaseAI` class to manage its direct healing spell pool.
*   **Calls Out**: None. The logic is self-contained within the `SpellEntry` structure access.

**Data Model**

This unit does not interact with any database tables. It operates entirely on in-memory `SpellEntry` objects, which represent static spell definition data loaded by the server.

**Notable Implementation Details**

*   **Variable Naming Misleadingness**: The local variables `spell1dmg` and `spell2dmg` are named as if they track damage, but they strictly accumulate healing points. This is a minor maintenance hazard but does not affect correctness.
*   **Strict Weak Ordering Requirement**: `std::set` requires a strict weak ordering. This comparator returns `false` when `spell1dmg == spell2dmg`. This is correct for strict weak ordering (equivalence is defined as `!comp(a,b) && !comp(b,a)`). However, if two distinct spells have the exact same total base heal value, their relative order in the set is undefined (though stable for insertion purposes).
*   **Ignoring Scaling and Coefficients**: The comparator uses `EffectBasePoints` directly. It does not account for spell coefficients, caster stats, or scaling factors. It ranks spells purely by their static database-defined base value. A spell with a lower base value but a higher coefficient might actually heal more in practice, but this functor will rank it lower.
*   **Multiple Healing Effects**: The loop correctly sums `EffectBasePoints` across all effects if a single spell has multiple `SPELL_EFFECT_HEAL` entries (though this is rare in WoW spell design, it is handled correctly).
*   **Const Correctness**: The method is marked `const`, and it takes `const` pointers to `const` `SpellEntry` objects, ensuring it does not modify the spell data.

## Member Reference

**operator()**
Compares two `SpellEntry` pointers by summing the `EffectBasePoints` of all `SPELL_EFFECT_HEAL` effects for each spell. Returns `true` if the total base heal of the left-hand side spell is strictly greater than that of the right-hand side spell. Used to order direct healing spells in `CombatBotBaseAI::m_spellListDirectHeal`.

---

<!-- machine-true, projected from graph.json -->

## Map — HealSpellCompare

*Source:* CombatBotBaseAI.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| operator() | method | — | — | — |

---

<!-- verify: failed-members | invented: operator -->
