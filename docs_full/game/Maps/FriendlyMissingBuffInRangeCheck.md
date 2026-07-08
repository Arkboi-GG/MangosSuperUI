<!-- provenance: failed-members -->
# FriendlyMissingBuffInRangeCheck

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# FriendlyMissingBuffInRangeCheck

**Purpose & Responsibilities**

`FriendlyMissingBuffInRangeCheck` is a predicate functor (a "Check" class) within the `MaNGOS` namespace, defined in `GridNotifiers.h`. Its specific responsibility is to identify friendly `Unit` objects that are currently in combat, within a specified range of a caster, and **missing** a specific buff (aura) identified by a spell ID.

This class is designed to be used with grid-based searchers (such as `UnitSearcher` or `UnitListSearcher`) to efficiently locate targets for healing or supportive spells. It encapsulates the logic required to filter out invalid targets (dead, hostile, out of range, or already buffed) so that the calling AI or script can apply the missing buff.

**Member-by-Member Behavior**

The unit consists of three members: a constructor, a getter for the focus object, and the functional call operator.

1.  **Constructor (`FriendlyMissingBuffInRangeCheck`)**
    *   Initializes the checker with three parameters:
        *   `SpellCaster const* obj`: The entity performing the check (the potential caster of the buff).
        *   `float range`: The maximum distance from `obj` within which targets are considered.
        *   `uint32 spellid`: The ID of the aura/buff that the target must *not* possess to be considered a valid match.
    *   Stores these values in private member variables `i_obj`, `i_range`, and `i_spell`.

2.  **`GetFocusObject`**
    *   Returns a constant reference to the `WorldObject` underlying `i_obj`.
    *   This method is part of the standard interface expected by `GridNotifiers` searchers (like `UnitSearcher`) to determine the phase mask and spatial context for the search. It allows the searcher to optimize grid traversal by knowing the central point of interest.

3.  **`operator()`**
    *   This is the core filtering logic. It accepts a pointer to a `Unit` (`u`) and returns `true` if the unit meets all criteria for being a valid target for the missing buff, and `false` otherwise.
    *   **Criteria enforced:**
        1.  **Alive:** `u->IsAlive()` must be true. Dead units cannot receive buffs.
        2.  **In Combat:** `u->IsInCombat()` must be true. This restricts the check to units actively engaged in fighting, likely prioritizing immediate support needs over idle allies.
        3.  **Friendly:** `!i_obj->IsHostileTo(u)` must be true. The target must not be hostile to the caster.
        4.  **In Range:** `i_obj->IsWithinDistInMap(u, i_range)` must be true. The target must be within the specified `i_range` of the caster, accounting for map boundaries.
        5.  **Selectable:** `!u->HasFlag(UNIT_FIELD_FLAGS, UNIT_FLAG_NOT_SELECTABLE)` must be true. Units flagged as unselectable (e.g., certain NPCs or invisible entities) are excluded.
        6.  **Missing Buff:** `!u->HasAura(i_spell)` must be true. The target must *not* currently have the aura defined by `i_spell`.

**Cross-Unit Boundaries**

*   **Called By:**
    *   `ScriptedAI/DoFindFriendlyMissingBuff`: Likely a helper function in scripted AI logic that uses this checker to find targets for automated buffing routines.
    *   `Unit.Main/FindFriendlyUnitMissingBuff`: A method in the main `Unit` class that exposes this functionality to general unit logic, allowing any unit to query for friends missing a specific buff.
*   **Calls Out:**
    *   None. This unit is a pure predicate; it relies on the methods of the `Unit` and `SpellCaster` classes (passed via `i_obj` and `u`) to perform the actual state checks. It does not initiate calls to other external systems or databases.

**Data Model**

This unit does not interact with any database tables. It operates entirely on in-memory state of `Unit` objects.

**Notable Implementation Details**

*   **Combat Restriction:** Unlike some similar checkers (e.g., `AnyFriendlyUnitInObjectRangeCheck`), this checker explicitly requires `u->IsInCombat()`. This suggests it is intended for reactive healing/support during fights, rather than proactive buffing of idle party members. If a developer needs to buff friendly units regardless of combat status, this checker is inappropriate.
*   **Single Spell ID:** The checker only supports checking for the absence of *one* specific spell ID (`i_spell`). It cannot check for multiple missing buffs simultaneously. To handle multiple buffs, the caller would need to instantiate multiple checkers or use a different logic structure.
*   **Hostility Check:** It uses `!i_obj->IsHostileTo(u)` rather than `i_obj->IsFriendlyTo(u)`. In many game engines, "not hostile" is a broader category than "friendly" (it may include neutral units). However, combined with the typical usage in raid/group contexts, this effectively filters for allies.
*   **Selectability Flag:** The exclusion of `UNIT_FLAG_NOT_SELECTABLE` ensures that the AI does not attempt to target units that players cannot select, preventing wasted actions or errors.
*   **No Phase Mask Check:** While `GetFocusObject` provides the phase mask to the searcher, the `operator()` itself does not explicitly check `InSamePhase`. This is because the grid searchers (e.g., `UnitSearcher`) typically filter by phase mask *before* invoking the checker's `operator()`. Thus, `operator()` only receives units that are already in the same phase.

## Member Reference

**FriendlyMissingBuffInRangeCheck**
Constructor that initializes the checker with a `SpellCaster` pointer, a range, and a spell ID. It stores these values for use in the filtering logic.

**GetFocusObject**
Returns a constant reference to the `WorldObject` associated with the caster (`i_obj`). Used by grid searchers to determine spatial context and phase mask for optimization.

**operator()**
The predicate function that evaluates whether a given `Unit` is a valid target. Returns `true` if the unit is alive, in combat, friendly to the caster, within range, selectable, and does not have the specified aura (`i_spell`). Returns `false` otherwise.

---

<!-- machine-true, projected from graph.json -->

## Map — FriendlyMissingBuffInRangeCheck

*Source:* GridNotifiers.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| FriendlyMissingBuffInRangeCheck | ctor | — | ScriptedAI/DoFindFriendlyMissingBuff, Unit.Main/FindFriendlyUnitMissingBuff | — |
| GetFocusObject | method | — | — | — |
| operator() | method | — | — | — |

---

<!-- verify: failed-members | invented: operator -->
