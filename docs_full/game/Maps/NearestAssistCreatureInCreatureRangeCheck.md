<!-- provenance: failed-members -->
# NearestAssistCreatureInCreatureRangeCheck

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# NearestAssistCreatureInCreatureRangeCheck

**Purpose & Responsibilities**

`NearestAssistCreatureInCreatureRangeCheck` is a predicate functor defined in `GridNotifiers.h` within the `MaNGOS` namespace. Its specific responsibility is to identify the **nearest** friendly `Creature` capable of assisting a target `Creature` (`i_obj`) against a specific enemy `Unit` (`i_enemy`).

This class implements the "Check" pattern used by the grid search infrastructure (specifically `CreatureLastSearcher` or similar nearest-object searchers). Unlike simple existence checks, this functor dynamically updates its internal range threshold as it finds valid candidates. This allows the calling search algorithm to prune the search space efficiently: once a candidate is found at distance $D$, any subsequent candidate must be closer than $D$ to replace it. It is primarily used by AI logic to determine which ally should respond to a call for help or assist in combat.

**Member-by-Member Behavior**

The class contains four primary members relevant to its operation:

1.  **Constructor (`NearestAssistCreatureInCreatureRangeCheck`)**: Initializes the search context. It stores pointers to the requesting creature (`i_obj`), the enemy being fought (`i_enemy`), and the initial maximum search radius (`i_range`).
2.  **`operator()`**: The core evaluation logic. It is invoked by the grid searcher for every `Creature` in the current grid cell. It returns `true` if the creature is a valid, nearer assist candidate, updating the internal range limit in the process. It returns `false` otherwise.
3.  **`GetFocusObject()`**: Returns a reference to the requesting creature (`i_obj`). This is required by the grid search infrastructure to determine the phase mask and spatial context for the search.
4.  **`GetLastRange()`**: Returns the final calculated distance to the nearest valid assist creature found during the search. This allows the caller to know how far away the assistance is.

**Cross-Unit Boundaries**

*   **Called By**:
    *   `Creature.Main/DoFleeToGetAssistance`: As indicated in the MAP, this check is instantiated by creature AI logic (likely within `Creature.cpp` or associated AI handlers) when a creature needs to flee to get assistance or locate an ally to help fight an enemy. The caller typically passes this functor to a grid search utility like `CreatureLastSearcher`.
*   **Calls Out**:
    *   The MAP indicates no direct calls out to other units. However, the implementation relies heavily on methods defined in the `Creature`, `Unit`, and `WorldObject` classes (e.g., `CanAssistTo`, `IsWithinDistInMap`, `IsWithinLOSInMap`, `GetDistance`). These are part of the core entity hierarchy, not separate "units" in the architectural sense of this documentation scope, but they represent dependencies on the core game object system.

**Data Model**

This unit performs purely in-memory spatial and state queries on live game objects. It does not interact with any database tables.

**Notable Implementation Details**

1.  **Dynamic Range Pruning**: The most critical detail is the line `i_range = i_obj->GetDistance(u);` inside `operator()`. When a valid creature is found, the search radius is immediately tightened to the distance of that creature. Subsequent creatures in the grid are only considered if they are closer than this new threshold. This ensures that the search returns the *nearest* valid candidate, not just *any* valid candidate.
2.  **Line-of-Sight Requirement**: The check enforces `i_obj->IsWithinLOSInMap(u)`. Assistance is only considered valid if the requesting creature can see the potential helper. This prevents creatures from calling for help from allies hidden behind walls or in different instances/areas not visible via LOS.
3.  **Assistability Logic**: It delegates the complex logic of whether a creature *can* assist to `u->CanAssistTo(i_obj, i_enemy)`. This method (defined in `Creature` or `Unit`) likely checks faction relations, combat states, and specific flags to ensure the helper is friendly and able to engage the enemy.
4.  **Self-Exclusion**: The check explicitly excludes the requesting creature itself (`if (u == i_obj) return false;`), preventing infinite loops or self-referential assistance logic.
5.  **Non-Copyable**: The class defines a private copy constructor (`NearestAssistCreatureInCreatureRangeCheck(const&)`) to prevent accidental copying, which would break the dynamic range update mechanism if multiple searchers held copies of the same check instance.

## Member Reference

**NearestAssistCreatureInCreatureRangeCheck**
Constructs the check functor with a pointer to the requesting creature (`i_obj`), the enemy unit (`i_enemy`), and the initial maximum search range (`i_range`).

**GetFocusObject**
Returns a constant reference to the requesting creature (`i_obj`). Used by the grid search infrastructure to align phase masks and spatial queries.

**operator()**
Evaluates a candidate `Creature* u`. Returns `true` if:
1.  `u` is not the requesting creature.
2.  `u` can assist `i_obj` against `i_enemy` (via `CanAssistTo`).
3.  `u` is within the current `i_range` of `i_obj` (via `IsWithinDistInMap`).
4.  `u` is within Line-of-Sight of `i_obj` (via `IsWithinLOSInMap`).
If all conditions are met, it updates `i_range` to the distance between `i_obj` and `u` to enforce finding the *nearest* candidate, then returns `true`. Otherwise, returns `false`.

**GetLastRange**
Returns the current value of `i_range`, which reflects the distance to the nearest valid assist creature found so far during the search.

**NearestAssistCreatureInCreatureRangeCheck#2**
Private copy constructor declaration. Prevents copying of the functor instance to ensure the internal `i_range` state remains consistent and mutable during the search process.

---

<!-- machine-true, projected from graph.json -->

## Map — NearestAssistCreatureInCreatureRangeCheck

*Source:* GridNotifiers.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| NearestAssistCreatureInCreatureRangeCheck | ctor | — | Creature.Main/DoFleeToGetAssistance | — |
| GetFocusObject | method | — | — | — |
| operator() | method | — | — | — |
| GetLastRange | method | — | — | — |
| NearestAssistCreatureInCreatureRangeCheck#2 | decl | — | — | — |

---

<!-- verify: failed-members | invented: operator -->
