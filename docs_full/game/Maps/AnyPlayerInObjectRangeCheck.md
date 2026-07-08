<!-- provenance: failed-members -->
# AnyPlayerInObjectRangeCheck

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# AnyPlayerInObjectRangeCheck

`AnyPlayerInObjectRangeCheck` is a predicate functor defined in `GridNotifiers.h` within the `MaNGOS` namespace. It implements a specific query used by the server's spatial grid system to determine if any living player exists within a defined radius of a `WorldObject`.

This class is part of a larger family of "Check" structs in `GridNotifiers.h` that conform to a standard interface: they store a reference to a focal object (`i_obj`) and a range, and implement `operator()` to test individual entities against those constraints. Specifically, `AnyPlayerInObjectRangeCheck` is designed to be passed to grid searchers (such as `PlayerListSearcher` or `PlayerSearcher`, also defined in `GridNotifiers.h`) which iterate over players in nearby grid cells and invoke this functor to filter results.

## Purpose & Responsibilities

The primary responsibility of `AnyPlayerInObjectRangeCheck` is to answer the question: **"Is there at least one alive player within distance $R$ of object $O$?"**

It encapsulates two critical validation steps required for valid spatial queries in the WoW server environment:
1.  **Liveness:** The target player must be alive (`u->IsAlive()`). Dead players (corpses) are typically excluded from interaction ranges unless explicitly handled by other checks.
2.  **Proximity:** The target player must be within the specified range of the focal object. Crucially, it uses `IsWithinDistInMap`, which ensures the distance calculation respects map boundaries (preventing "through-the-world" calculations) and optionally supports 3D distance checks.

## Member-by-Member Behavior

### Constructor: **AnyPlayerInObjectRangeCheck**
Initializes the functor with the necessary context for evaluation.
*   **Parameters:**
    *   `WorldObject const* obj`: The focal point for the distance calculation.
    *   `float range`: The maximum distance threshold.
    *   `bool distance_3d` (default `true`): Determines whether the distance check considers the Z-axis (vertical distance). If `true`, it calculates Euclidean 3D distance. If `false`, it likely calculates 2D horizontal distance (though the underlying `IsWithinDistInMap` signature determines the exact behavior, the flag is stored for use in the operator).
*   **Behavior:** Stores these values in private members `i_obj`, `i_range`, and `b_3dDist`.

### Method: **GetFocusObject**
Returns a constant reference to the focal `WorldObject` (`*i_obj`).
*   **Purpose:** This method satisfies the interface contract expected by various grid searchers in `GridNotifiers.h`. Some searchers use the focus object to determine phase masks or other contextual properties before iterating. While `AnyPlayerInObjectRangeCheck` itself does not use the return value internally, providing this method allows it to be used interchangeably with other Check types in generic template code.

### Method: **operator()**
The core evaluation logic invoked by grid searchers for each candidate `Player*`.
*   **Signature:** `bool operator()(Player* u)`
*   **Logic:**
    1.  Checks if the player `u` is alive via `u->IsAlive()`. If dead, returns `false`.
    2.  Checks if the player is within range via `i_obj->IsWithinDistInMap(u, i_range, b_3dDist)`.
    3.  Returns `true` only if both conditions are met.
*   **Note:** Unlike some other checks in this file (e.g., `Nearest...` checks), this functor does **not** update the range member upon finding a match. It is a boolean existence check, not a "find nearest" optimization.

## Cross-Unit Boundaries

`AnyPlayerInObjectRangeCheck` acts as a leaf node in the dependency graph; it contains no outgoing calls to other complex units, relying instead on methods of the `WorldObject` and `Player` classes. However, it is heavily depended upon by higher-level systems.

### Called By (Incoming Dependencies)

1.  **`GameObject/Update`**:
    *   **Context:** Game Objects (GOs) often need to detect if players are nearby to trigger events, despawn, or change state.
    *   **Collaboration:** The GO update loop likely instantiates `AnyPlayerInObjectRangeCheck` and passes it to a grid searcher to quickly determine player presence without manually iterating all players.

2.  **`ScriptedAI/GetPlayersWithinRange`**:
    *   **Context:** Custom AI scripts for creatures or GOs need to query nearby players.
    *   **Collaboration:** This helper function in the scripting interface creates an instance of `AnyPlayerInObjectRangeCheck` to filter the list of players returned by the grid system.

3.  **`WorldObject.Object/DestroyForNearbyPlayers`**:
    *   **Context:** When an object is destroyed, it may need to notify or affect nearby players (e.g., removing buffs, triggering death animations).
    *   **Collaboration:** Uses this check to identify which players are close enough to be affected by the destruction event.

4.  **`WorldObject.Object/GetAlivePlayerListInRange`**:
    *   **Context:** A general-purpose utility method on `WorldObject` to retrieve a list of alive players in a radius.
    *   **Collaboration:** This method is the direct consumer of `AnyPlayerInObjectRangeCheck`. It constructs the functor and passes it to a `PlayerListSearcher` (defined in `GridNotifiers.h`) to populate the result list.

5.  **`ZoneScript/Update`**:
    *   **Context:** Zone-specific scripts may run periodic updates that depend on player population density or presence.
    *   **Collaboration:** Uses the check to verify if any players are present in the zone or near specific triggers during the update tick.

## Data Model

This unit operates entirely in memory using the C++ object model. It does not interact with any database tables. The `Tables` column in the MAP is empty, confirming that `AnyPlayerInObjectRangeCheck` is a runtime spatial query tool with no persistence layer involvement.

## Notable Implementation Details

1.  **3D vs. 2D Distance Flag:**
    The constructor accepts a `distance_3d` boolean (defaulting to `true`). This is passed directly to `IsWithinDistInMap`. In many MMO contexts, "range" implies horizontal distance (ignoring Z), but vertical proximity matters for spells or effects that pierce terrain. The ability to toggle this allows callers to choose the appropriate geometric model.

2.  **Non-Optimizing Nature:**
    Compare `AnyPlayerInObjectRangeCheck` to `NearestAlivePlayerCheck` (also in `GridNotifiers.h`). The `Nearest` variant updates its internal `m_range` member when it finds a closer target, allowing the searcher to prune further checks. `AnyPlayerInObjectRangeCheck` does **not** do this. It is strictly a boolean filter. This means if used with a `PlayerListSearcher`, it will evaluate *all* players in the relevant grid cells. If used with a `PlayerSearcher` (which stops after the first match), it is efficient for existence checks ("is anyone here?").

3.  **Const Correctness:**
    The functor stores `WorldObject const* i_obj`. This ensures the check cannot accidentally modify the focal object during the search process. The `operator()` takes `Player* u` (non-const pointer) but only reads from it (`IsAlive`, implicit read in `IsWithinDistInMap`). This is safe because the grid searchers pass pointers to live objects, but the check itself is logically const regarding the game state.

4.  **Integration with Grid System:**
    The class relies on the `MaNGOS` grid system's assumption that `IsWithinDistInMap` is a fast, accurate distance check. The efficiency of this check depends on the grid searcher passing only candidates that are already in adjacent grid cells, reducing the number of times `operator()` is called.

## Member Reference

**AnyPlayerInObjectRangeCheck**
Constructor that initializes the check with a focal `WorldObject`, a range, and a flag for 3D distance calculation. Stores these in private members `i_obj`, `i_range`, and `b_3dDist`.

**GetFocusObject**
Returns a constant reference to the focal `WorldObject` (`*i_obj`). Required by the grid searcher interface to provide context for phase masking or other pre-filtering, though not used internally by this specific check's logic.

**operator()**
Evaluates a candidate `Player*`. Returns `true` if the player is alive (`u->IsAlive()`) AND within the specified range of the focal object (`i_obj->IsWithinDistInMap(u, i_range, b_3dDist)`). Returns `false` otherwise. Does not modify internal state.

---

<!-- machine-true, projected from graph.json -->

## Map — AnyPlayerInObjectRangeCheck

*Source:* GridNotifiers.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| AnyPlayerInObjectRangeCheck | ctor | — | GameObject/Update, ScriptedAI/GetPlayersWithinRange, WorldObject.Object/DestroyForNearbyPlayers, WorldObject.Object/GetAlivePlayerListInRange, ZoneScript/Update | — |
| GetFocusObject | method | — | — | — |
| operator() | method | — | — | — |

---

<!-- verify: failed-members | invented: operator -->
