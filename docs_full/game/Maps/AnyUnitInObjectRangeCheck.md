<!-- provenance: failed-members -->
# AnyUnitInObjectRangeCheck

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# AnyUnitInObjectRangeCheck

`AnyUnitInObjectRangeCheck` is a predicate functor defined in `GridNotifiers.h` within the `MaNGOS` namespace. It implements the standard "Check" interface used by the server's grid-based spatial query system (specifically `UnitSearcher`, `UnitListSearcher`, etc.) to determine if a specific `Unit` satisfies a set of criteria relative to a reference `WorldObject`.

Its primary responsibility is to identify **any** living `Unit` that is both within a specified physical range of a reference object and visible to that reference object in the game world. Unlike "Nearest" variants, it does not track or update the minimum distance found; it simply returns `true` if the candidate meets the basic existence and visibility constraints.

## Purpose & Responsibilities

The core logic of `AnyUnitInObjectRangeCheck` is encapsulated in its `operator()` method. When invoked with a candidate `Unit`, it verifies three conditions:
1.  **Liveness:** The candidate `Unit` must be alive (`u->IsAlive()`). Dead units (corpes) are excluded.
2.  **Proximity:** The candidate must be within `i_range` units of the reference object `i_obj`. This check uses `IsWithinDistInMap`, which accounts for map boundaries and ensures they are on the same map and within the radial distance.
3.  **Visibility:** The candidate must be able to see the reference object in the world (`u->CanSeeInWorld(i_obj)`). This handles phase shifting, stealth, and other visibility mechanics. If the candidate cannot see the reference object, the check fails, even if they are physically close.

This check is typically used when a script or AI needs to know if *any* valid target exists in a radius, rather than finding the closest one.

## Member-by-Member Behavior

### Constructor: `AnyUnitInObjectRangeCheck`
Initializes the predicate with the necessary context for evaluation.
*   **Parameters:**
    *   `WorldObject const* obj`: The reference object from which distances are measured and against which visibility is checked. Stored in `i_obj`.
    *   `float range`: The maximum distance threshold. Stored in `i_range`.
*   **Behavior:** Assigns the input pointers and values to the private member variables `i_obj` and `i_range`. No validation is performed on the inputs at construction time.

### Method: `GetFocusObject`
Returns a constant reference to the reference object stored in `i_obj`.
*   **Purpose:** This method satisfies the "Check" interface contract required by the grid searchers (e.g., `UnitSearcher`). The searchers use this to retrieve the phase mask of the focus object during initialization to ensure they only iterate over objects in the same phase.
*   **Implementation:** Returns `*i_obj`. Note that this assumes `i_obj` is non-null, as dereferencing a null pointer would cause a crash.

### Method: `operator()`
The core evaluation logic.
*   **Signature:** `bool operator()(Unit* u)`
*   **Logic:**
    1.  Checks if `u` is alive. If not, returns `false`.
    2.  Checks if `i_obj` is within `i_range` of `u` using `IsWithinDistInMap`. If not, returns `false`.
    3.  Checks if `u` can see `i_obj` in the world using `CanSeeInWorld`. If not, returns `false`.
    4.  If all checks pass, returns `true`.
*   **Note:** The order of checks is significant for performance. `IsAlive()` is likely the cheapest check, followed by distance, then visibility. However, `IsWithinDistInMap` might involve coordinate calculations, while `CanSeeInWorld` might involve more complex phase/visibility logic. The current order prioritizes liveness first.

## Cross-Unit Boundaries

`AnyUnitInObjectRangeCheck` is a passive data structure/predicate. It does not actively call out to other units in the sense of initiating workflows. However, it relies on methods from other units for its logic:

*   **Called by:**
    *   `ChatHandler.DebugCommands/HandleDebugExp`: Likely used to debug experience gain ranges or similar mechanics by checking for units in range.
    *   `ChatHandler.DebugCommands/HandleMmapTestArea`: Used in debugging tools related to movement maps (MMAP) to verify unit presence in specific areas.
    *   `Unit.Main/InterruptSpellsCastedOnMe`: Used by the `Unit` class to determine if there are any units in range that might be casting spells on the unit, potentially for interruption logic or threat assessment.

*   **Dependencies (Implicit via Methods):**
    *   `Unit::IsAlive()`: From the `Unit` class.
    *   `WorldObject::IsWithinDistInMap()`: From the `WorldObject` class.
    *   `Unit::CanSeeInWorld()`: From the `Unit` class.

These dependencies mean that changes to how distance or visibility are calculated in `Unit` or `WorldObject` will directly affect the behavior of this check.

## Data Model

This unit does not interact with any database tables. It operates entirely on in-memory game objects (`Unit`, `WorldObject`) and their current states.

## Notable Implementation Details

1.  **Visibility Directionality:** The check uses `u->CanSeeInWorld(i_obj)`. This means the *candidate* unit must be able to see the *reference* object. This is crucial for mechanics like stealth. If a player is stealthed, they might be within range of a mob, but the mob (`i_obj`) might not be able to see the player (`u`). In this case, `u->CanSeeInWorld(i_obj)` might still return true (the player can see the mob), but if the logic intended to check if the mob sees the player, it would be incorrect. However, looking at the name `AnyUnitInObjectRangeCheck`, it seems to imply "Is there any unit that I (the reference object) can perceive?" or "Is there any unit that is perceivable?". The current implementation checks if the *unit* can see the *object*. This is important for targeting. If `u` cannot see `i_obj`, `u` probably cannot target `i_obj`.

2.  **No "Nearest" Tracking:** Unlike `NearestAttackableUnitInObjectRangeCheck` or `NearestFriendlyUnitCheck`, this class does not update an internal `i_range` variable upon success. It simply returns a boolean. This makes it suitable for `UnitSearcher` (which stops at the first match) or `UnitListSearcher` (which collects all matches), but not for finding the *closest* unit unless combined with a different searcher strategy.

3.  **Const Correctness:** The constructor takes `WorldObject const*`, and `GetFocusObject` returns a `const` reference. The `operator()` takes a non-const `Unit*` but does not modify it. This is consistent with the predicate pattern.

4.  **Null Pointer Safety:** The code does not explicitly check if `i_obj` is null before dereferencing it in `GetFocusObject` or using it in `operator()`. It is assumed that callers always provide a valid `WorldObject` pointer. Passing a null pointer would result in undefined behavior (likely a crash).

5.  **Phase Handling:** While `CanSeeInWorld` handles phase visibility, the grid searchers themselves (like `UnitSearcher`) also filter by phase mask using `GetFocusObject().GetPhaseMask()`. This double-checking ensures that units in different phases are efficiently filtered out before the expensive `operator()` is called.

## Member Reference

**AnyUnitInObjectRangeCheck**
Constructor that initializes the check with a reference `WorldObject` and a maximum range. Stores these in private members `i_obj` and `i_range`.

**GetFocusObject**
Returns a constant reference to the stored reference object `i_obj`. Used by grid searchers to determine the phase mask for filtering.

**operator()**
Evaluates whether a given `Unit* u` is alive, within `i_range` of `i_obj` (using `IsWithinDistInMap`), and can see `i_obj` in the world (using `CanSeeInWorld`). Returns `true` if all conditions are met, `false` otherwise.

---

<!-- machine-true, projected from graph.json -->

## Map — AnyUnitInObjectRangeCheck

*Source:* GridNotifiers.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| AnyUnitInObjectRangeCheck | ctor | — | ChatHandler.DebugCommands/HandleDebugExp, ChatHandler.DebugCommands/HandleMmapTestArea, Unit.Main/InterruptSpellsCastedOnMe | — |
| GetFocusObject | method | — | — | — |
| operator() | method | — | — | — |

---

<!-- verify: failed-members | invented: operator -->
