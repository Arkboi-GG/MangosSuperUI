<!-- provenance: failed-members -->
# AnyFriendlyUnitInObjectRangeCheck

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# AnyFriendlyUnitInObjectRangeCheck

**Purpose & Responsibilities**

`AnyFriendlyUnitInObjectRangeCheck` is a predicate functor defined in `GridNotifiers.h` that determines whether a specific `Unit` qualifies as a valid friendly target for a given `SpellCaster`. It is used by the grid notification system to filter units during spatial searches. The class encapsulates four strict criteria for validity:
1.  **Aliveness:** The candidate unit must be alive.
2.  **Proximity:** The candidate must be within a specified distance (`i_range`) from the caster (`i_obj`).
3.  **Friendship:** The caster must consider the candidate friendly.
4.  **Visibility:** The candidate must be able to see the caster in the world (accounting for phase masks and visibility states).

This checker is typically instantiated by spell targeting logic or AI routines and passed to grid searchers (such as `UnitSearcher` or `UnitListSearcher`, also defined in `GridNotifiers.h`). The searcher iterates over nearby objects, invoking `operator()` on each; if the method returns `true`, the object is included in the search results.

**Member-by-Member Behavior**

### Constructor: **AnyFriendlyUnitInObjectRangeCheck**
Initializes the checker with two arguments:
*   `SpellCaster const* obj`: The source object performing the check (e.g., a player casting a heal or a creature selecting a target). Stored in the private member `i_obj`.
*   `float range`: The maximum distance within which a target is considered valid. Stored in the private member `i_range`.

### Method: **GetFocusObject**
Returns a constant reference to the caster (`*i_obj`).
*   **Role in Grid System:** This method satisfies the interface expected by grid searchers. Searchers use the returned `WorldObject` to retrieve the caster's phase mask. This allows the searcher to pre-filter candidates, ensuring that `operator()` is only called on units sharing the same phase as the caster, thereby improving performance and correctness.

### Method: **operator()**
The core evaluation logic. It accepts a `Unit*` (the potential target) and returns a `bool`.
*   **Logic Flow:**
    1.  `u->IsAlive()`: Rejects dead units immediately.
    2.  `i_obj->IsWithinDistInMap(u, i_range)`: Verifies that the candidate is on the same map as the caster and within the specified `i_range`.
    3.  `i_obj->IsFriendlyTo(u)`: Confirms that the caster considers the candidate friendly. This check relies on faction templates and current threat/hostility states.
    4.  `u->CanSeeInWorld(i_obj)`: Ensures the candidate can perceive the caster. This is critical for handling stealth, invisibility, and phase visibility. Note that this check is asymmetric: it verifies if the *target* can see the *caster*.

**Cross-Unit Boundaries**

*   **Called By:**
    *   `Spell.Main/SetTargetMap`: Invoked during spell resolution to identify valid friendly targets for effects like healing or buffs.
    *   `Unit.Main/SelectRandomFriendlyTarget`: Used by AI or script logic to select a random ally within range.
    *   `Unit.SpellAuras/Update`: Utilized during aura maintenance to validate or refresh friendly targets.
*   **Collaboration Pattern:**
    These callers create an instance of `AnyFriendlyUnitInObjectRangeCheck` with the appropriate caster and range parameters. They then pass this instance to a grid searcher (e.g., `UnitSearcher` in `GridNotifiers.h`). The searcher traverses the grid's internal maps (`PlayerMapType`, `CreatureMapType`), applying the checker to each unit. The checker acts as a high-performance filter, allowing the caller to retrieve only units that meet all logical and spatial constraints without manual iteration.

**Data Model**

This unit does not interact with any database tables. It operates exclusively on in-memory object states (`Unit`, `SpellCaster`) and spatial coordinates.

**Notable Implementation Details**

1.  **Asymmetric Visibility:** The `operator()` checks `u->CanSeeInWorld(i_obj)`, meaning the *target* must be able to see the *caster*. This differs from checks that verify if the caster can see the target. For friendly targeting, this ensures that the target is not phased out relative to the caster or hidden in a way that prevents interaction from the caster's perspective (though typically, if A is friendly to B, visibility is mutual unless phased).
2.  **No Self-Exclusion:** Unlike `NearestFriendlyUnitCheck` (also in `GridNotifiers.h`), which explicitly excludes the source object (`if (me == u) return false;`), `AnyFriendlyUnitInObjectRangeCheck` does **not** exclude the caster. If the caster is friendly to themselves and within range (distance 0), it will return `true`. Callers must implement self-exclusion logic if self-targeting is invalid for the specific action.
3.  **Phase Filtering Delegation:** The class does not perform phase mask checks internally. Instead, it relies on `GetFocusObject()` to provide the phase context to the surrounding searcher infrastructure. This separation of concerns allows the searcher to skip entire groups of out-of-phase objects before invoking the more expensive `operator()` logic.

## Member Reference

**AnyFriendlyUnitInObjectRangeCheck**
Constructor that initializes the checker with a `SpellCaster const*` (the caster/focus object) and a `float` (maximum range). Stores these in private members `i_obj` and `i_range`.

**GetFocusObject**
Returns a `WorldObject const&` reference to the caster (`*i_obj`). Used by grid searchers to access the caster's phase mask for pre-filtering candidates.

**operator()**
Evaluates a `Unit*` candidate. Returns `true` if the unit is alive, within `i_range` of `i_obj` (on the same map), friendly to `i_obj`, and capable of seeing `i_obj` in the world. Returns `false` otherwise. Does not exclude the caster itself.

---

<!-- machine-true, projected from graph.json -->

## Map — AnyFriendlyUnitInObjectRangeCheck

*Source:* GridNotifiers.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| AnyFriendlyUnitInObjectRangeCheck | ctor | — | Spell.Main/SetTargetMap, Unit.Main/SelectRandomFriendlyTarget, Unit.SpellAuras/Update | — |
| GetFocusObject | method | — | — | — |
| operator() | method | — | — | — |

---

<!-- verify: failed-members | invented: operator -->
