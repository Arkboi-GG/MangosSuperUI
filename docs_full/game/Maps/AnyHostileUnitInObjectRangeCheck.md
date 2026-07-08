<!-- provenance: failed-members -->
# AnyHostileUnitInObjectRangeCheck

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# AnyHostileUnitInObjectRangeCheck

## Purpose & Responsibilities

`AnyHostileUnitInObjectRangeCheck` is a predicate functor defined in `GridNotifiers.h` within the `MaNGOS` namespace. It implements specific filtering logic used by the grid-based spatial query system to identify hostile units within a defined radius of a reference object.

Its primary responsibility is to determine if a candidate `Unit` satisfies three simultaneous conditions:
1.  **Visibility:** The candidate unit must be visible to a designated "focus" unit (`i_funit`) in the world context (handling line-of-sight, phase masks, and visibility flags via `CanSeeInWorld`).
2.  **Proximity:** The candidate unit must be within a specified distance (`i_range`) of a reference object (`i_obj`) in the same map instance.
3.  **Hostility:** The candidate unit must be considered hostile to the focus unit (`i_funit`).

This checker is typically employed by searchers like `UnitSearcher` or `UnitListSearcher` to find aggro sources, valid attack targets, or nearby enemies for AI decision-making. It decouples the *criteria* for selection from the *iteration* over the grid data structures.

## Member-by-Member Behavior

### Constructor: `AnyHostileUnitInObjectRangeCheck`
Initializes the checker with three parameters:
*   `obj`: A pointer to a `WorldObject`. This object defines the center point for the distance calculation.
*   `funit`: A pointer to a `Unit`. This unit defines the perspective for visibility checks (`CanSeeInWorld`) and hostility determination (`IsHostileTo`). Note that `obj` and `funit` may be different objects, allowing for scenarios where an object (like a trap or projectile) checks for hostiles relative to its owner or caster.
*   `range`: A `float` representing the maximum distance from `obj` to consider a unit valid.

The constructor stores these values in private members `i_obj`, `i_funit`, and `i_range`.

### Method: `GetFocusObject`
Returns a constant reference to the `WorldObject` pointed to by `i_obj`. This method is required by the grid searcher interface to allow searchers to optimize queries (e.g., by checking phase masks against the focus object before iterating). It dereferences `i_obj`, implying `i_obj` must not be null when this method is called.

### Method: `operator()`
This is the core predicate function invoked by grid searchers for each candidate `Unit` (`u`) in the relevant grid cells. It returns `true` if the unit matches the criteria, `false` otherwise.

The logic proceeds as follows:
1.  **Visibility Check:** It first calls `i_funit->CanSeeInWorld(u)`. If the focus unit cannot see the candidate unit in the world (due to phases, invisibility, or other visibility rules), the function immediately returns `false`. This is a critical optimization and correctness check, ensuring that units hidden from the focus unit are ignored.
2.  **Composite Condition:** If the visibility check passes, it evaluates a conjunction of three conditions:
    *   `u->IsAlive()`: The candidate unit must be alive. Dead units are excluded.
    *   `i_obj->IsWithinDistInMap(u, i_range)`: The candidate unit must be within `i_range` distance of the reference object `i_obj`, considering map boundaries (units on different maps are excluded).
    *   `i_funit->IsHostileTo(u)`: The candidate unit must be hostile to the focus unit `i_funit`.

If all three conditions are met, it returns `true`; otherwise, `false`.

## Cross-Unit Boundaries

### Called By: `GameObject/DoAggroWhenOpening`
According to the MAP, `AnyHostileUnitInObjectRangeCheck` is instantiated and used by `GameObject::DoAggroWhenOpening` (located in the `GameObject` unit).

*   **Collaboration Context:** When a `GameObject` (such as a chest, door, or trap) is opened or interacted with, it may need to detect nearby hostile players or creatures to initiate combat (aggro).
*   **Data Flow:**
    *   `GameObject::DoAggroWhenOpening` creates an instance of `AnyHostileUnitInObjectRangeCheck`.
    *   It likely passes `this` (the GameObject) as both the `obj` (distance center) and `funit` (hostility/visibility perspective), along with a predefined aggro range.
    *   The `GameObject` then uses a grid searcher (e.g., `UnitSearcher`) to iterate over nearby units.
    *   The searcher invokes `AnyHostileUnitInObjectRangeCheck::operator()` on each candidate.
    *   If the checker returns `true`, the `GameObject` identifies that unit as a valid aggro target and likely initiates hostility towards it.

This separation allows `GameObject` to reuse the standard grid search infrastructure while defining specific aggro logic via this dedicated checker.

## Data Model

This unit does not interact directly with any database tables. It operates entirely on in-memory object states (`Unit`, `WorldObject`) and spatial calculations.

## Notable Implementation Details

1.  **Decoupled Distance and Hostility References:** The constructor accepts separate `obj` and `funit` pointers. This design allows flexibility. For example, a projectile (`obj`) might check for hostiles relative to its caster (`funit`). In the common case where the object itself is the actor, `obj` and `funit` will point to the same entity.
2.  **Visibility Pre-check:** The `CanSeeInWorld` check is performed *before* the distance and hostility checks. This is crucial for performance and correctness. If a unit is not visible to the focus unit, it shouldn't matter how close it is or whether it's technically hostile; the focus unit cannot perceive it. Placing this check first avoids unnecessary distance calculations and hostility lookups for invisible entities.
3.  **Alive State Requirement:** The checker explicitly requires `u->IsAlive()`. This prevents dead bodies or corpses from triggering aggro or being selected as valid targets, which is consistent with most combat mechanics.
4.  **Map-Aware Distance:** The use of `IsWithinDistInMap` ensures that units on different maps (even if coordinates overlap due to map loading quirks or portals) are not considered. This is essential for multi-map instances or zones.
5.  **Const Correctness:** The `operator()` takes `Unit* u` (non-const pointer) but does not modify `u`. The checker itself holds `const` pointers to `i_obj` and `i_funit`, ensuring it doesn't alter the state of the reference objects during the check.
6.  **No Range Update:** Unlike some other checkers in `GridNotifiers.h` (e.g., `NearestGameObjectEntryInObjectRangeCheck`), this checker does *not* update the `i_range` member upon finding a match. It is designed to find *any* hostile unit within the fixed initial range, not necessarily the *nearest* one. If the caller needs the nearest, they would use a different checker or process the results of this one.

## Member Reference

**AnyHostileUnitInObjectRangeCheck**
Constructor that initializes the checker with a reference object for distance (`obj`), a focus unit for visibility and hostility checks (`funit`), and a maximum range (`range`). Stores these in private members `i_obj`, `i_funit`, and `i_range`.

**GetFocusObject**
Returns a constant reference to the `WorldObject` stored in `i_obj`. Used by grid searchers to access the focus object's properties (like phase mask) for query optimization. Assumes `i_obj` is non-null.

**operator()**
The predicate function evaluated for each candidate `Unit`. Returns `true` if the unit is visible to `i_funit` (`CanSeeInWorld`), is alive (`IsAlive`), is within `i_range` of `i_obj` (`IsWithinDistInMap`), and is hostile to `i_funit` (`IsHostileTo`). Otherwise, returns `false`.

---

<!-- machine-true, projected from graph.json -->

## Map — AnyHostileUnitInObjectRangeCheck

*Source:* GridNotifiers.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| AnyHostileUnitInObjectRangeCheck | ctor | — | GameObject/DoAggroWhenOpening | — |
| GetFocusObject | method | — | — | — |
| operator() | method | — | — | — |

---

<!-- verify: failed-members | invented: operator -->
