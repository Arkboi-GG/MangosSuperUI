<!-- provenance: failed-members -->
# AnyAssistCreatureInRangeCheck

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# AnyAssistCreatureInRangeCheck

**Purpose & Responsibilities**

`AnyAssistCreatureInRangeCheck` is a predicate functor defined in `GridNotifiers.h` within the MaNGOS grid notification system. Its purpose is to filter `Creature` objects to identify those eligible to assist a specific friendly unit (`i_funit`) against a specific enemy (`i_enemy`).

It is designed to be passed to grid searchers (such as `CreatureSearcher` or `CreatureListSearcher`) which iterate over creatures in the server's spatial grid. The check encapsulates the logic for filtering creatures based on combat rules, proximity, and line-of-sight, allowing the caller to efficiently find valid assistance targets without duplicating complex conditional logic.

**Member-by-Member Behavior**

The unit consists of three members: a constructor, a getter for the focus object, and the functional call operator.

### Construction and State Initialization
**AnyAssistCreatureInRangeCheck**
Initializes the check with three critical pieces of context:
1.  `funit`: The `Unit` requesting assistance (the "friend").
2.  `enemy`: The `Unit` being fought (the target of the assistance).
3.  `range`: The maximum distance within which assistance is considered valid.

These values are stored in private member variables `i_funit`, `i_enemy`, and `i_range`. The constructor does not perform any validation or side effects; it simply binds these references/values for use during the evaluation phase.

### Focus Object Retrieval
**GetFocusObject**
Returns a constant reference to `i_funit`. This method satisfies the interface contract required by the grid searcher infrastructure (specifically `WorldObjectSearcher` and related templates). The searchers use the focus object to determine the starting phase mask and spatial context for the search. By returning `i_funit`, the system ensures that the search is centered around the unit calling for help.

### Evaluation Logic
**operator()**
This is the core functional method, invoked by the grid searcher for each `Creature` candidate (`u`) in the relevant grid cells. It returns `true` if the creature is a valid assistant, and `false` otherwise. The evaluation proceeds through a series of strict filters:

1.  **Self-Exclusion**: If the candidate creature `u` is identical to the friend `i_funit`, it returns `false`. A unit cannot assist itself in this context.
2.  **Combat Eligibility**: Calls `u->CanAssistTo(i_funit, i_enemy)`. This delegates to the `Creature` class to verify high-level combat rules, such as faction alignment, hostility states, and whether the creature is capable of engaging the specific enemy. If this returns `false`, the check fails immediately.
3.  **Proximity Check**: Verifies that `i_funit` is within `i_range` of `u` using `IsWithinDistInMap`. This ensures the assistant is close enough to reach the fight. Note that the distance is measured from the friend (`i_funit`) to the candidate (`u`), not from the enemy.
4.  **Line-of-Sight (LOS)**: Verifies that `i_funit` has a direct line of sight to `u` using `IsWithinLOSInMap`. This prevents units from calling for help from allies hidden behind walls or terrain features.

If all conditions pass, the method returns `true`, marking the creature as a valid assistance target.

**Cross-Unit Boundaries**

*   **Called by `Creature.Main/CallAssistance`**:
    The primary consumer of this check is the `Creature` class's assistance mechanism. When a creature needs help, it constructs an `AnyAssistCreatureInRangeCheck` instance and passes it to a grid searcher. The searcher iterates through nearby creatures, invoking `operator()` on each. The results are used to trigger assistance behaviors (e.g., moving to the friend's location and attacking the enemy).

*   **Calls into `Creature` (via `u->CanAssistTo`)**:
    The `operator()` method relies on `Creature::CanAssistTo` to determine if the candidate creature is logically allowed to assist. This keeps the complex faction and combat-state logic within the `Creature` domain, while `AnyAssistCreatureInRangeCheck` handles the spatial and contextual filtering.

*   **Calls into `WorldObject`/`Unit` (via Distance/LOS methods)**:
    The check uses `IsWithinDistInMap` and `IsWithinLOSInMap` from the `WorldObject`/`Unit` hierarchy. These methods handle the underlying spatial calculations, including map-specific constraints and LOS raycasting.

**Data Model**

This unit does not interact with any database tables. It operates entirely on in-memory game state objects (`Unit`, `Creature`, `WorldObject`).

**Notable Implementation Details**

*   **Distance Reference Point**: The range check is performed relative to `i_funit` (the unit calling for help), not the enemy. This means the search finds creatures near the victim, ensuring they can quickly intervene.
*   **Strict LOS Requirement**: Unlike some other checks in `GridNotifiers.h` (e.g., `AnyFriendlyUnitInObjectRangeCheck`), this check explicitly requires line-of-sight between the friend and the assistant. This prevents "invisible" allies from being targeted for assistance, which could lead to pathfinding failures or unexpected behavior.
*   **Delegation of Combat Rules**: The check does not implement its own faction or hostility logic. It defers to `Creature::CanAssistTo`, ensuring consistency with the broader combat system. This separation of concerns allows the combat rules to evolve independently of the spatial search logic.
*   **Const-Correctness**: The `operator()` takes `Creature* u` (non-const pointer) but does not modify it. The member variables `i_funit` and `i_enemy` are stored as `const` pointers, reflecting that the check does not alter the state of the friend or enemy during evaluation.

## Member Reference

**AnyAssistCreatureInRangeCheck**
Constructor that initializes the check with the friendly unit (`funit`), the enemy unit (`enemy`), and the maximum assistance range (`range`). Stores these in private members `i_funit`, `i_enemy`, and `i_range`.

**GetFocusObject**
Returns a constant reference to `i_funit`. Used by grid searchers to determine the spatial and phase context for the search.

**operator()**
Evaluates whether a candidate `Creature* u` is a valid assistant. Returns `true` only if: `u` is not `i_funit`; `u->CanAssistTo(i_funit, i_enemy)` returns `true`; `i_funit` is within `i_range` of `u`; and `i_funit` has line-of-sight to `u`. Otherwise, returns `false`.

---

<!-- machine-true, projected from graph.json -->

## Map — AnyAssistCreatureInRangeCheck

*Source:* GridNotifiers.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| AnyAssistCreatureInRangeCheck | ctor | — | Creature.Main/CallAssistance | — |
| GetFocusObject | method | — | — | — |
| operator() | method | — | — | — |

---

<!-- verify: failed-members | invented: operator -->
