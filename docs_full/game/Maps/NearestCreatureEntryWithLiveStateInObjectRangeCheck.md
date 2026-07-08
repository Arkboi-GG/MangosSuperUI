<!-- provenance: failed-members -->
# NearestCreatureEntryWithLiveStateInObjectRangeCheck

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# NearestCreatureEntryWithLiveStateInObjectRangeCheck

**Purpose & Responsibilities**

`NearestCreatureEntryWithLiveStateInObjectRangeCheck` is a predicate functor (a "Check" class) defined in `GridNotifiers.h` within the `MaNGOS` namespace. Its responsibility is to evaluate whether a specific `Creature` matches a set of criteria during a spatial search: it must have a specific database entry ID, exist in a specific life state (alive or corpse), and be within a certain distance of a reference `WorldObject`.

Crucially, this class is designed to find the **nearest** such creature. It achieves this by maintaining mutable state: upon finding a valid creature, it shrinks its internal maximum search radius (`i_range`) to the distance of that creature. Subsequent evaluations will reject any creatures farther away than the current best match. This optimization allows grid-based searchers to prune large portions of the search space efficiently.

The unit does not perform iteration itself; it provides the boolean logic for acceptance/rejection of individual `Creature` pointers during an iteration performed by external searcher units (e.g., `CreatureLastSearcher`).

**Member-by-Member Behavior**

*   **Constructor (`NearestCreatureEntryWithLiveStateInObjectRangeCheck`)**: Initializes the predicate with the search parameters. It stores a reference to the origin `WorldObject` (`i_obj`), the target creature entry ID (`i_entry`), a boolean flag indicating whether to look for living creatures or corpses (`i_alive`), the initial maximum search radius (`i_range`), and an optional pointer to a specific creature to exclude from results (`i_except`). The copy constructor is declared private to prevent cloning, ensuring the mutable `i_range` state remains tied to the original instance used by the searcher.
*   **`operator()`**: This is the core logic executed for each `Creature` encountered during a grid search. It performs the following checks in order:
    1.  **Entry Match**: Verifies if the creature's entry ID matches `i_entry`.
    2.  **Life State**: Checks if the creature's alive status matches the `i_alive` flag. If `i_alive` is true, the creature must be alive (`u->IsAlive()`). If false, the creature must be a corpse (`u->IsCorpse()`).
    3.  **Proximity**: Verifies if the creature is within the current `i_range` of the origin object `i_obj` using `IsWithinDistInMap`.
    4.  **Exclusion**: If a specific creature was passed as `i_except`, it ensures the current candidate is not that specific instance.
    5.  **Range Optimization**: If all checks pass, it updates `i_range` to the exact distance between `i_obj` and the found creature. This tightens the constraint for subsequent evaluations, ensuring only creatures closer than the current best match are considered. It returns `true` to signal acceptance.
    6.  If any check fails, it returns `false`.
*   **`GetFocusObject`**: Returns a constant reference to the origin `WorldObject` (`i_obj`). This allows the calling searcher to determine phase masks or other context-dependent properties required for the search algorithm.
*   **`GetLastRange`**: Returns the final value of `i_range` after the search completes. This is useful for callers who need to know the exact distance to the nearest found creature.

**Cross-Unit Boundaries**

This unit acts as a passive predicate, meaning it is primarily **called by** other units rather than initiating calls itself.

*   **Called by `GridSearchers/GetClosestCreatureWithEntry`**: This is the primary consumer. The searcher iterates through grid cells containing creatures, passing each creature pointer to this check's `operator()`. The searcher relies on the check's ability to narrow the search radius dynamically.
*   **Called by `instance_naxxramas.Main/FleeToHorse`**: In the Naxxramas instance script, this check is likely used to locate specific horse NPCs (by entry) that are either alive or dead, possibly to determine spawn points or flee targets based on proximity.
*   **Called by `Map.ScriptCommands/ScriptCommand_TerminateScript`**: Script commands may use this check to verify the presence or absence of specific creature states near an object before terminating a script sequence.
*   **Called by `WorldObject.Object/FindNearestCreature`**: This is a general-purpose helper method on `WorldObject` that wraps the grid search infrastructure. It instantiates this check (or similar ones) to find the nearest creature of a specific type/state.

The unit does not call out to other units for data retrieval; it relies on the `Creature` and `WorldObject` interfaces provided by the engine core for property access (`GetEntry`, `IsAlive`, `IsCorpse`, `GetDistance`, etc.).

**Data Model**

This unit does not interact directly with database tables. It operates entirely on in-memory objects (`Creature`, `WorldObject`) loaded by the server. The `entry` parameter corresponds to the `entry` column in the `creature_template` table, but the unit itself performs no SQL queries.

**Notable Implementation Details**

1.  **Mutable State for Optimization**: Unlike many simple predicates, this class maintains mutable state (`i_range`). This is critical for performance in grid searches. By shrinking the search radius as soon as a valid target is found, it prevents unnecessary distance calculations for objects that are clearly too far away to be the "nearest."
2.  **Copy Constructor Prevention**: The declaration `NearestCreatureEntryWithLiveStateInObjectRangeCheck(NearestCreatureEntryWithLiveStateInObjectRangeCheck const&);` is placed in the private section of the class. This prevents accidental copying of the object. Copying would reset the optimized `i_range` or create disjointed state, breaking the search logic.
3.  **Life State Flexibility**: The `i_alive` boolean allows a single class structure to handle both "find nearest living X" and "find nearest corpse of X" queries, reducing code duplication compared to having separate `NearestLivingCreatureCheck` and `NearestCorpseCheck` classes.
4.  **Exclusion Logic**: The `i_except` parameter allows the caller to ignore a specific creature instance (e.g., the creature performing the search itself, or a previously processed target) without needing to filter it out in the searcher loop.

## Member Reference

**NearestCreatureEntryWithLiveStateInObjectRangeCheck**
Constructor that initializes the search criteria: origin object, target entry ID, desired life state (alive/corpse), initial max range, and an optional creature to exclude. It sets up the internal state for the `operator()` to perform proximity-based filtering.

**GetFocusObject**
Returns a constant reference to the origin `WorldObject` (`i_obj`). Used by searchers to determine phase masks or other contextual properties relative to the search origin.

**operator()**
The core predicate logic. Evaluates a `Creature` pointer against the stored criteria: entry ID match, life state match, proximity within the current `i_range`, and exclusion of the `i_except` instance. If valid, it updates `i_range` to the distance of the found creature to optimize future checks and returns `true`; otherwise, returns `false`.

**GetLastRange**
Returns the final value of `i_range` after the search process. This provides the caller with the exact distance to the nearest creature that satisfied the conditions.

**NearestCreatureEntryWithLiveStateInObjectRangeCheck#2**
Declaration of the private copy constructor. Prevents copying of the check object to preserve the integrity of the mutable `i_range` state used for search optimization.

---

<!-- machine-true, projected from graph.json -->

## Map — NearestCreatureEntryWithLiveStateInObjectRangeCheck

*Source:* GridNotifiers.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| NearestCreatureEntryWithLiveStateInObjectRangeCheck | ctor | — | GridSearchers/GetClosestCreatureWithEntry, instance_naxxramas.Main/FleeToHorse, Map.ScriptCommands/ScriptCommand_TerminateScript, WorldObject.Object/FindNearestCreature | — |
| GetFocusObject | method | — | — | — |
| operator() | method | — | — | — |
| GetLastRange | method | — | — | — |
| NearestCreatureEntryWithLiveStateInObjectRangeCheck#2 | decl | — | — | — |

---

<!-- verify: failed-members | invented: operator -->
