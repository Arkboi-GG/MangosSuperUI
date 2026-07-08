<!-- provenance: failed-members -->
# FriendlyCCedInRangeCheck

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# FriendlyCCedInRangeCheck

**Purpose & Responsibilities**

`FriendlyCCedInRangeCheck` is a predicate functor (a "Check" class) within the `MaNGOS` namespace, defined in `GridNotifiers.h`. Its sole responsibility is to evaluate whether a specific `Unit` meets a set of criteria required to be considered a valid target for a friendly crowd-control (CC) rescue or interaction.

It is designed to be used with grid-based searchers (such as `UnitSearcher` or `UnitListSearcher`) to iterate over nearby entities and identify allies who are currently incapacitated by crowd control effects (charmed, frozen, or otherwise unable to react) while being alive, in combat, and within a specified range of the caster.

This unit does not perform actions itself; it returns a boolean value indicating if a candidate unit satisfies the conditions. It does not access any database tables.

## Member-by-Member Behavior

### **FriendlyCCedInRangeCheck** (Constructor)
Initializes the checker with two parameters:
1.  `SpellCaster const* obj`: The entity performing the check (the "focus" object). This is typically the player or creature attempting to break or interact with the CC.
2.  `float range`: The maximum distance within which the target must be located.

These values are stored in private members `i_obj` and `i_range` respectively.

### **GetFocusObject**
Returns a constant reference to the `WorldObject` underlying the `SpellCaster` (`i_obj`). This method is part of the standard interface expected by grid searchers to determine phase masks or spatial context during iteration. It dereferences `i_obj` directly.

### **operator()**
The core evaluation logic. It accepts a `Unit* u` (the candidate target) and returns `true` if and only if **all** of the following conditions are met:
1.  **Alive**: `u->IsAlive()` is true. Dead units are ignored.
2.  **In Combat**: `u->IsInCombat()` is true. The unit must be actively engaged in combat.
3.  **Not Hostile**: `!i_obj->IsHostileTo(u)` is true. The target must not be hostile to the caster. Note that this uses `IsHostileTo`, implying the target is likely friendly or neutral-to-friendly, but explicitly excludes enemies.
4.  **Selectable**: `!u->HasFlag(UNIT_FIELD_FLAGS, UNIT_FLAG_NOT_SELECTABLE)` is true. The target must not have the "not selectable" flag set (which would prevent targeting).
5.  **In Range**: `i_obj->IsWithinDistInMap(u, i_range)` is true. The target must be within the specified `i_range` of the caster, accounting for map boundaries.
6.  **Crowd Controlled**: The target must satisfy at least one of the following states:
    *   `u->IsCharmed()`: The unit is charmed.
    *   `u->IsFrozen()`: The unit is frozen.
    *   `u->HasUnitState(UNIT_STATE_CAN_NOT_REACT)`: The unit has a general state preventing reaction (covering other CC types like roots or silences that disable reaction, depending on engine implementation).

If all conditions pass, it returns `true`; otherwise, it returns `false`.

## Cross-Unit Boundaries

### Called By
*   **eastern_plaguelands/UpdateAI#2**: Used in Eastern Plaguelands AI logic to find friendly units under CC, likely for rescue mechanics or specific quest triggers.
*   **ScriptedAI/DoFindFriendlyCC**: A helper function in scripted AI that utilizes this checker to locate friendly CC'd units.
*   **Unit.Main/FindFriendlyUnitCC**: A method in the main `Unit` class that uses this checker to provide a high-level API for finding friendly CC'd units.

### Calls Out
*   None. This unit is a pure predicate and does not call into other units or subsystems beyond the methods invoked on the `Unit` and `SpellCaster` objects passed to it.

## Data Model

This unit does not interact with any database tables. It operates entirely on in-memory game object states.

## Notable Implementation Details

*   **Strict Combat Requirement**: The check requires `u->IsInCombat()`. This means a friendly unit standing idle and charmed/frozen outside of combat will *not* be detected by this checker. This is a deliberate design choice, likely to ensure that rescue attempts only occur during active engagements.
*   **Broad CC Definition**: The use of `IsCharmed() || IsFrozen() || HasUnitState(UNIT_STATE_CAN_NOT_REACT)` provides a broad net for crowd control. `UNIT_STATE_CAN_NOT_REACT` is a critical catch-all for various disabling effects that might not strictly be "charm" or "freeze" but still prevent the unit from acting.
*   **Hostility Check**: It uses `!i_obj->IsHostileTo(u)` rather than `i_obj->IsFriendlyTo(u)`. This subtle difference means that neutral units (that are not hostile) could potentially match if they are CC'd and in combat, though the `IsInCombat()` requirement usually implies alignment with a faction. However, it explicitly excludes enemies.
*   **Selectability**: The exclusion of `UNIT_FLAG_NOT_SELECTABLE` ensures that units which cannot be targeted by spells or attacks (e.g., certain NPCs or players in specific modes) are ignored, preventing wasted effort on untargetable entities.
*   **Range Precision**: Uses `IsWithinDistInMap`, which respects map boundaries and instance IDs, ensuring that units in different instances or separated by map borders are not incorrectly considered in range.

## Member Reference

**FriendlyCCedInRangeCheck**  
Constructor that initializes the checker with a `SpellCaster` pointer (`i_obj`) and a `float` range (`i_range`).

**GetFocusObject**  
Returns a `const WorldObject&` reference to the caster (`i_obj`), satisfying the interface contract for grid searchers.

**operator()**  
Evaluates a `Unit*` candidate. Returns `true` if the unit is alive, in combat, not hostile to the caster, selectable, within range, and currently charmed, frozen, or in a non-reactive state.

---

<!-- machine-true, projected from graph.json -->

## Map — FriendlyCCedInRangeCheck

*Source:* GridNotifiers.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| FriendlyCCedInRangeCheck | ctor | — | eastern_plaguelands/UpdateAI#2, ScriptedAI/DoFindFriendlyCC, Unit.Main/FindFriendlyUnitCC | — |
| GetFocusObject | method | — | — | — |
| operator() | method | — | — | — |

---

<!-- verify: failed-members | invented: operator -->
