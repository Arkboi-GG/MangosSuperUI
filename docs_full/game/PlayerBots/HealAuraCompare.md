<!-- provenance: failed-members -->
# HealAuraCompare

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# HealAuraCompare

**Purpose & Responsibilities**

`HealAuraCompare` is a functor (function object) defined in `CombatBotBaseAI.h` that provides a strict weak ordering for pointers to `SpellEntry` objects. Specifically, it compares two spells based on the magnitude of their **periodic healing** effects. It is designed to be used as a comparator for standard library containers (such as `std::set`) to automatically sort or organize spells by their healing-over-time potency.

The comparator iterates through all possible effects of each spell (`MAX_SPELL_EFFECTS`). It identifies effects that apply auras—specifically `SPELL_EFFECT_APPLY_AURA`, `SPELL_EFFECT_PERSISTENT_AREA_AURA`, and `SPELL_EFFECT_APPLY_AREA_AURA_PARTY`. Among these, it filters for those that create a `SPELL_AURA_PERIODIC_HEAL` aura. The base points of these specific effects are summed up for each spell. The functor returns `true` if the first spell (`lhs`) has a higher total periodic healing value than the second spell (`rhs`).

This logic allows the AI system to prioritize or categorize spells that provide sustained healing over time, distinct from direct burst healing (which is handled by the sibling struct `HealSpellCompare`).

**Member-by-Member Behavior**

*   **operator()**: This is the core comparison logic. It takes two constant pointers to `SpellEntry` (`lhs` and `rhs`). It initializes two accumulators, `spell1dmg` and `spell2dmg` (misnamed variables, as they store healing values, not damage). It loops through the effect slots of both spells. For each slot, it checks if the effect type is one of the aura-application types. If it is, it further checks if the aura name is `SPELL_AURA_PERIODIC_HEAL`. If both conditions are met, it adds the `EffectBasePoints` of that slot to the respective accumulator. Finally, it returns whether `spell1dmg` is strictly greater than `spell2dmg`.

**Cross-Unit Boundaries**

*   **Called by**: The MAP indicates no external callers. However, within the same header file (`CombatBotBaseAI.h`), the `CombatBotBaseAI` class declares a member variable `m_spellListPeriodicHeal` of type `std::set<SpellEntry const*, HealAuraCompare>`. This confirms that `HealAuraCompare` is intended to be instantiated implicitly by the `std::set` constructor to order the elements in `m_spellListPeriodicHeal`. The `CombatBotBaseAI` unit (defined in `CombatBotBaseAI.cpp`, not shown here but implied by the class definition) will use this set to manage periodic healing spells.
*   **Calls out**: None. It operates purely on the data exposed by the `SpellEntry` structure.

**Data Model**

This unit does not interact with any database tables. It operates entirely on in-memory `SpellEntry` structures, which are typically loaded from the `spell_dbc` or similar DBC/SQL sources during server startup, but this specific functor performs no I/O.

**Notable Implementation Details**

1.  **Variable Naming**: The local variables `spell1dmg` and `spell2dmg` are misleadingly named. They accumulate healing values (`EffectBasePoints` of `SPELL_AURA_PERIODIC_HEAL`), not damage. A maintainer should be aware that despite the name "dmg", these variables represent positive healing amounts.
2.  **Effect Filtering**: The comparator only considers effects that *apply* a periodic heal aura. It ignores direct healing effects (`SPELL_EFFECT_HEAL`), which are handled by `HealSpellCompare`. It also ignores other aura types (e.g., shields, buffs) even if they might indirectly contribute to survivability.
3.  **Summation Logic**: If a spell has multiple effects that apply periodic heals (rare but possible in complex spell designs), their base points are summed. This means a spell with two weak periodic heals might rank higher than a spell with one strong periodic heal if the sum is greater.
4.  **Strict Weak Ordering**: The implementation uses `>` (greater than) for the return value. This creates a descending order in a `std::set` if used as the default comparator, meaning the "strongest" periodic heal spells will appear first in iteration. Note that `std::set` requires strict weak ordering; `a > b` is valid for this purpose.
5.  **Const Correctness**: The operator is marked `const`, and it takes `const` pointers to `const` `SpellEntry` objects, ensuring it does not modify the spell data.

## Member Reference

**operator()**
Compares two `SpellEntry` pointers by summing the `EffectBasePoints` of all effects that apply a `SPELL_AURA_PERIODIC_HEAL` aura via `SPELL_EFFECT_APPLY_AURA`, `SPELL_EFFECT_PERSISTENT_AREA_AURA`, or `SPELL_EFFECT_APPLY_AREA_AURA_PARTY`. Returns `true` if the left-hand side spell has a higher total periodic healing value than the right-hand side spell. Used to order spells in `CombatBotBaseAI::m_spellListPeriodicHeal`.

---

<!-- machine-true, projected from graph.json -->

## Map — HealAuraCompare

*Source:* CombatBotBaseAI.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| operator() | method | — | — | — |

---

<!-- verify: failed-members | invented: operator -->
