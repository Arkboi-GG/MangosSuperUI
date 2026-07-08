<!-- provenance: failed-members -->
# NearestGameObjectEntryFitConditionInObjectRangeCheck

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# NearestGameObjectEntryFitConditionInObjectRangeCheck

**Purpose & Responsibilities**

`NearestGameObjectEntryFitConditionInObjectRangeCheck` is a predicate functor defined in `GridNotifiers.h` within the `MaNGOS` namespace. It serves as a specialized filter for grid-based object searches, specifically designed to locate the **nearest** `GameObject` that meets three strict criteria:
1.  It matches a specific database entry ID (`i_entry`).
2.  It lies within a dynamic, shrinking distance range (`i_range`) from a source `WorldObject` (`i_obj`).
3.  It satisfies a server-side condition set identified by `i_conditionId`.

This class is intended for use with "nearest" searchers (such as `GameObjectLastSearcher` or similar templates in the `MaNGOS` grid system). By modifying its internal `i_range` member whenever a valid candidate is found, it enables the searcher to prune subsequent iterations, ensuring that only objects closer than the best match found so far are evaluated. This optimizes performance for spell targeting and script logic that require finding the closest valid game object.

**Member-by-Member Behavior**

The class implements the standard interface for MaNGOS grid search predicates.

*   **Constructor (`NearestGameObjectEntryFitConditionInObjectRangeCheck`)**: Initializes the predicate with four arguments: the source object (`obj`), the target `GameObject` entry ID (`entry`), the initial maximum search radius (`range`), and the condition set ID (`conditionId`). These are stored in private members `i_obj`, `i_entry`, `i_range`, and `i_conditionId`.
*   **`GetFocusObject`**: Returns a constant reference to the source object (`i_obj`). This method is required by the grid searcher infrastructure to determine phase masks and spatial context for the search operation.
*   **`operator()`**: The core evaluation logic, invoked by the grid searcher for each candidate `GameObject`. It performs the following steps:
    1.  **Entry Match**: Checks if `go->GetEntry()` equals `i_entry`.
    2.  **Distance Check**: Verifies if the `GameObject` is within the current `i_range` of `i_obj` using `IsWithinDistInMap`.
    3.  **Condition Validation**: If `i_conditionId` is non-zero, it calls `IsConditionSatisfied` with the context `CONDITION_FROM_SPELL_AREA`. This delegates the evaluation of complex logical rules (defined in the database) to the Conditions subsystem. The `GameObject` is passed as the target, and `i_obj` as the source/caster. If the conditions are not met, the object is rejected.
    4.  **Range Update**: If all checks pass, `i_range` is updated to the actual distance between `i_obj` and `go`. This tightens the constraint for future iterations, ensuring the search converges on the nearest valid object.
    5.  Returns `true` to signal a valid hit; otherwise, returns `false`.
*   **`GetLastRange`**: Returns the final value of `i_range` after the search completes. This allows the caller to retrieve the distance to the nearest found object.
*   **Copy Constructor (Deleted)**: The class explicitly deletes the copy constructor (`NearestGameObjectEntryFitConditionInObjectRangeCheck#2`) to prevent accidental cloning. Copying could lead to inconsistent state or dangling references if the functor is duplicated during search operations.

**Cross-Unit Boundaries**

*   **Called by `Spell.Main/CheckScriptTargeting`**: The primary consumer of this predicate is the spell targeting system. When a spell requires a target that is a specific `GameObject` entry and must meet additional conditional requirements (e.g., "target the nearest chest that is unlocked"), the spell engine constructs an instance of this class and passes it to a grid searcher.
*   **Calls into `IsConditionSatisfied`**: The predicate delegates the evaluation of `i_conditionId` to the global `IsConditionSatisfied` function. This function resides in the Conditions subsystem and interprets the rule set associated with the ID.
*   **Calls into `GameObject` and `WorldObject` methods**: The predicate relies on `GameObject::GetEntry()`, `GameObject::GetMap()`, and `WorldObject::IsWithinDistInMap()` (inherited by `GameObject`) to perform its checks. These are standard queries within the entity hierarchy.

**Data Model**

This unit does not directly query database tables. It indirectly relies on the **conditions** data structure (typically populated from the `conditions` table) via the `IsConditionSatisfied` call. The `i_conditionId` parameter corresponds to the `condition_id` in that table. The `GameObject` entry ID (`i_entry`) corresponds to the `entry` in the `gameobject_template` table, but this lookup is handled by the `GameObject` object itself, not this predicate.

**Notable Implementation Details**

*   **Dynamic Range Shrinking**: The line `i_range = i_obj.GetDistance(go);` inside `operator()` is critical. It transforms the predicate from a simple filter into an optimization tool. Without this, a searcher would need to collect all matches and then sort by distance. With this, the grid searcher can prune entire sub-grids or early-exit loops if remaining objects are farther than the current `i_range`.
*   **Condition Context**: The call to `IsConditionSatisfied` passes `CONDITION_FROM_SPELL_AREA`. This flag informs the condition evaluator how to interpret certain condition types, such as area-specific requirements. It passes `go` as the target and `&i_obj` as the source, allowing conditions to check relationships between the two entities.
*   **Non-Const Operator**: `operator()` is not marked `const` because it modifies `i_range`. This is intentional and necessary for the shrinking-range logic.
*   **Null Safety**: The code assumes `go` is not null, as it dereferences it immediately. This is safe because grid searchers only pass valid pointers from their maps.

## Member Reference

**NearestGameObjectEntryFitConditionInObjectRangeCheck**
Constructor that initializes the predicate with the source object, target entry ID, initial search range, and condition ID. Stores these in private members `i_obj`, `i_entry`, `i_range`, and `i_conditionId`.

**GetFocusObject**
Returns a constant reference to the source object (`i_obj`). Used by grid searchers to determine phase and spatial context.

**operator()**
The main evaluation function. Checks if a `GameObject` matches the entry ID, is within the current dynamic range, and satisfies the specified condition ID. If valid, it updates the internal range to the object's distance and returns `true`; otherwise, returns `false`.

**GetLastRange**
Returns the final value of `i_range` after the search, indicating the distance to the nearest valid object found.

**NearestGameObjectEntryFitConditionInObjectRangeCheck#2**
Declaration of the deleted copy constructor. Prevents copying of the functor to avoid state inconsistency or dangling references during search operations.

---

<!-- machine-true, projected from graph.json -->

## Map — NearestGameObjectEntryFitConditionInObjectRangeCheck

*Source:* GridNotifiers.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| NearestGameObjectEntryFitConditionInObjectRangeCheck | ctor | — | Spell.Main/CheckScriptTargeting | — |
| GetFocusObject | method | — | — | — |
| operator() | method | — | — | — |
| GetLastRange | method | — | Spell.Main/CheckScriptTargeting | — |
| NearestGameObjectEntryFitConditionInObjectRangeCheck#2 | decl | — | — | — |

---

<!-- verify: failed-members | invented: operator -->
