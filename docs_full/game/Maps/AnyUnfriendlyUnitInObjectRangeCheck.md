<!-- provenance: failed-members -->
# AnyUnfriendlyUnitInObjectRangeCheck

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# AnyUnfriendlyUnitInObjectRangeCheck

`AnyUnfriendlyUnitInObjectRangeCheck` is a predicate functor defined in `GridNotifiers.h` within the `MaNGOS` namespace. It implements the "Check" pattern used by the server's grid-based spatial query system to identify hostile or neutral units within a specific radius of a reference object.

Its primary responsibility is to filter `Unit` objects during grid iteration, returning `true` only for units that are alive, within a specified distance from a spatial anchor (`i_obj`), visible to a reference unit (`i_funit`), and not friendly to that reference unit. This allows callers to efficiently determine if *any* non-friendly entity exists in a zone, or to collect lists of such entities for targeting, aggro checks, or combat logic.

## Purpose & Responsibilities

The class serves as a reusable boolean filter for the `UnitSearcher`, `UnitListSearcher`, and related grid traversal utilities. Unlike simple distance checks, it incorporates social/faction relationships ("unfriendly") and line-of-sight/visibility constraints ("CanSeeInWorld").

Key responsibilities include:
1.  **Spatial Filtering:** Verifying that a candidate unit is within `i_range` of the spatial anchor `i_obj`.
2.  **Relationship Filtering:** Ensuring the candidate unit is not friendly to the reference unit `i_funit`. This includes enemies and neutrals.
3.  **Visibility Filtering:** Confirming that `i_funit` can see the candidate unit in the world (handling LOS, phases, and stealth mechanics via `CanSeeInWorld`).
4.  **State Validation:** Rejecting dead units.

## Member-by-Member Behavior

### Constructor: `AnyUnfriendlyUnitInObjectRangeCheck`
Initializes the predicate with three parameters:
*   `obj`: A `WorldObject` pointer serving as the spatial center for distance calculations.
*   `funit`: A `Unit` pointer serving as the reference for faction/relationship checks and visibility.
*   `range`: A `float` defining the maximum distance in map units.

These are stored in private members `i_obj`, `i_funit`, and `i_range`.

### Method: `GetFocusObject`
Returns a constant reference to `*i_obj`. This is required by the grid searcher infrastructure to determine the phase mask and starting grid for the search. It ensures the search is anchored to the correct location in the world.

### Method: `operator()`
This is the core evaluation logic invoked for each `Unit` encountered during grid traversal. It returns `true` if the unit meets all criteria, `false` otherwise. The evaluation order is optimized for early exit:
1.  **Visibility Check:** Calls `i_funit->CanSeeInWorld(u)`. If the reference unit cannot see the candidate (due to LOS, phase differences, or stealth), it returns `false` immediately. This is computationally cheaper than distance calculations in some contexts and filters out irrelevant entities early.
2.  **Alive Check:** Verifies `u->IsAlive()`. Dead units are ignored.
3.  **Distance Check:** Calls `i_obj->IsWithinDistInMap(u, i_range)`. Uses the spatial anchor `i_obj` for distance calculation. Note that this uses `IsWithinDistInMap`, which handles map boundary checks and potentially different map IDs if applicable (though typically same-map).
4.  **Friendship Check:** Calls `!i_funit->IsFriendlyTo(u)`. If the unit is friendly to the reference, it returns `false`. Consequently, it returns `true` for both hostile and neutral units.

## Cross-Unit Boundaries

`AnyUnfriendlyUnitInObjectRangeCheck` is a passive data structure/predicate; it does not initiate calls to other units. However, it is heavily utilized by AI and combat systems.

### Called By: `AiBotAI.Grind`
*   **Context:** The `AiBotAI` module (likely a custom or third-party AI implementation) uses this check in its grinding/target selection logic.
*   **Methods:** `CountNearbyHostiles`, `ScanApproachTarget`, `SelectGrindTarget`.
*   **Collaboration:** The AI passes itself (or a target) as `funit` and a position/object as `obj` to count or select enemies. The check determines if a unit is a valid "grind" target (i.e., not friendly, alive, visible, and in range).

### Called By: `scourge_invasion`
*   **Context:** Specific event logic for the Scourge Invasion world event.
*   **Method:** `SelectRandomFlameshockerSpawnTarget`.
*   **Collaboration:** Used to find valid spawn locations or targets for Flameshockers, ensuring they don't spawn on friendly players or NPCs.

### Called By: `Unit.Main`
*   **Context:** Core `Unit` class methods for combat and awareness.
*   **Methods:**
    *   `CombatStopInRange`: Checks if there are unfriendly units nearby to determine if combat should persist or stop.
    *   `GetEnemyCountInRadiusAround`: Counts enemies using this predicate.
    *   `GetEnemyListInRadiusAround`: Populates a list of enemies.
    *   `InterruptAttacksOnMe`: Identifies attackers to interrupt.
    *   `SelectRandomUnfriendlyTarget`: Picks a random non-friendly unit for attacks or spells.
*   **Collaboration:** These methods instantiate `AnyUnfriendlyUnitInObjectRangeCheck` and pass it to grid searchers (like `UnitListSearcher`) to iterate over nearby units. The predicate provides the filtering logic, while the `Unit` class manages the iteration and result aggregation.

## Data Model

This unit does not interact with any database tables. It operates entirely on in-memory object states (`Unit`, `WorldObject`) and spatial coordinates.

## Notable Implementation Details

1.  **Separation of Spatial Anchor and Relationship Reference:**
    The constructor takes two distinct pointers: `obj` (spatial anchor) and `funit` (relationship/visibility reference). This allows for flexible usage. For example, a spell effect might originate from a projectile (`obj`) but needs to check hostility relative to the caster (`funit`). In many common cases, `obj` and `funit` point to the same object, but the design supports decoupling.

2.  **"Unfriendly" vs. "Hostile":**
    The check uses `!IsFriendlyTo(u)`. This means it matches **both** hostile and neutral units. If a caller strictly wants enemies, they should use `AnyHostileUnitInObjectRangeCheck` (also defined in `GridNotifiers.h`). This distinction is critical for spells or abilities that affect neutrals (e.g., fear, snare) versus those that only affect enemies.

3.  **Visibility Dependency:**
    The check relies on `i_funit->CanSeeInWorld(u)`. This is a complex function that considers line-of-sight, phase masks, and stealth. If `i_funit` is stealthed, it might not see certain units, or vice versa. The correctness of the result depends entirely on the up-to-date state of `i_funit`'s visibility cache.

4.  **Early Exit Optimization:**
    The `operator()` performs the visibility check before the distance check. While `CanSeeInWorld` can be expensive, it often fails quickly for units in different phases or behind walls, avoiding the floating-point distance calculation. However, if visibility is always true (e.g., same phase, no LOS obstacles), the distance check becomes the primary filter.

5.  **No Range Update:**
    Unlike `Nearest...` checks (e.g., `NearestAttackableUnitInObjectRangeCheck`), this class does **not** update the `i_range` member upon finding a match. It is designed for existence checks or list population, not for finding the *closest* unit. The range remains constant throughout the search.

## Member Reference

**AnyUnfriendlyUnitInObjectRangeCheck**
Constructor that initializes the predicate with a spatial anchor (`obj`), a relationship/visibility reference unit (`funit`), and a maximum distance (`range`). Stores these in private members `i_obj`, `i_funit`, and `i_range`.

**GetFocusObject**
Returns a constant reference to `*i_obj`. Used by grid searchers to determine the phase mask and starting grid for the spatial query.

**operator()**
The evaluation functor. Returns `true` if the input `Unit` `u` is alive, visible to `i_funit` (`i_funit->CanSeeInWorld(u)`), within `i_range` of `i_obj` (`i_obj->IsWithinDistInMap(u, i_range)`), and not friendly to `i_funit` (`!i_funit->IsFriendlyTo(u)`). Returns `false` otherwise. Optimized for early exit on visibility failure.

---

<!-- machine-true, projected from graph.json -->

## Map — AnyUnfriendlyUnitInObjectRangeCheck

*Source:* GridNotifiers.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| AnyUnfriendlyUnitInObjectRangeCheck | ctor | — | AiBotAI.Grind/CountNearbyHostiles, AiBotAI.Grind/ScanApproachTarget, AiBotAI.Grind/SelectGrindTarget, scourge_invasion/SelectRandomFlameshockerSpawnTarget, Unit.Main/CombatStopInRange, Unit.Main/GetEnemyCountInRadiusAround, Unit.Main/GetEnemyListInRadiusAround, Unit.Main/InterruptAttacksOnMe, Unit.Main/SelectRandomUnfriendlyTarget | — |
| GetFocusObject | method | — | — | — |
| operator() | method | — | — | — |

---

<!-- verify: failed-members | invented: operator -->
