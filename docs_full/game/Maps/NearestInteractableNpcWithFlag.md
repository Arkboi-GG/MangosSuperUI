<!-- provenance: failed-members -->
# NearestInteractableNpcWithFlag

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# NearestInteractableNpcWithFlag

**Purpose & Responsibilities**

`NearestInteractableNpcWithFlag` is a predicate functor (a "Check" class) defined in `GridNotifiers.h` within the `MaNGOS` namespace. Its specific responsibility is to identify the closest `Creature` (NPC) to a given `Player` that satisfies two conditions:
1. The creature is within a specific interaction distance.
2. The creature possesses specific NPC flags that allow interaction with the player.

This class is designed to be used in conjunction with grid-based searchers (specifically `CreatureLastSearcher` or similar nearest-object finders) to efficiently locate interactable NPCs without iterating through all entities in the world. It implements the standard "nearest object" optimization pattern used throughout the MaNGOS codebase: upon finding a valid candidate, it updates its internal range threshold to the distance of that candidate. Subsequent checks only consider objects closer than this updated threshold, ensuring that the final result is the absolute nearest match.

It is explicitly instantiated and utilized by `Player.Main/FindNearestInteractableNpcWithFlag`.

## Member-by-Member Behavior

### Constructor: `NearestInteractableNpcWithFlag`
The constructor initializes the functor with the context required for the search:
- **`obj`**: A pointer to the `Player` who is performing the search. This player serves as the origin point for distance calculations and interaction checks.
- **`npcFlags`**: A bitmask (`uint32`) representing the required NPC flags. The target creature must have these flags set to be considered valid.
- **`i_range`**: Initialized to `INTERACTION_DISTANCE`. This constant defines the maximum allowable distance for the initial search. As the search progresses and valid NPCs are found, this value decreases to refine the search radius.

The constructor also marks the copy constructor as deleted (via private declaration), preventing accidental cloning of the functor, which could lead to inconsistent state during parallel or nested searches.

### Method: `operator()`
This is the core evaluation logic invoked by the grid searcher for each candidate `Creature`.
1. **Distance Check**: It first checks if the candidate creature `u` is within the current `i_range` of the player `i_obj` using `IsWithinDistInMap`. If the creature is too far, it returns `false` immediately, skipping further expensive checks.
2. **Interaction Check**: If the creature is within range, it calls `i_obj->CanInteractWithNPC(u, i_npcFlags)`. This method (defined in the `Player` unit) verifies if the player can interact with the creature based on the provided `npcFlags`. This typically involves checking faction relations, quest status, or specific NPC type flags.
3. **Range Update**: If both checks pass, the functor updates `i_range` to the exact distance between the player and the creature (`i_obj->GetDistance(u)`). This tightens the search criteria for subsequent candidates, ensuring only closer matches are considered.
4. **Return Value**: Returns `true` to indicate the creature is a valid candidate.

### Method: `GetFocusObject`
Returns a constant reference to the `Player` (`i_obj`) initiating the search. This is part of the standard interface for "Check" classes in MaNGOS, allowing searchers to access the origin object for phase mask checks or other contextual validations if needed by the searcher infrastructure.

### Method: `GetLastRange`
Returns the final value of `i_range` after the search has completed. This allows the caller to determine the distance to the found NPC, or to verify if a valid NPC was found within the original interaction distance (if the range remains unchanged or exceeds certain thresholds, though typically the searcher handles existence checks).

### Declaration: `NearestInteractableNpcWithFlag#2`
This refers to the private, deleted copy constructor declared in the class definition. It prevents copying of the functor instance.

## Cross-Unit Boundaries

### Called By: `Player.Main/FindNearestInteractableNpcWithFlag`
- **Direction**: `Player.Main` creates an instance of `NearestInteractableNpcWithFlag` and passes it to a grid searcher (likely `CreatureLastSearcher` or `CreatureSearcher` from `GridNotifiers.h`).
- **Collaboration**: The `Player` unit provides the context (`this` pointer as `obj`) and the specific `npcFlags` required for the interaction. The `NearestInteractableNpcWithFlag` functor performs the low-level filtering and distance optimization. The result (the nearest `Creature*`) is returned to the `Player` unit.

### Calls Out: None Directly
The functor itself does not call other units directly in its member functions. However, its `operator()` relies on methods from other units:
- **`Player.CanInteractWithNPC`**: Called to validate interaction permissions.
- **`WorldObject.IsWithinDistInMap`** and **`WorldObject.GetDistance`**: Called for spatial calculations. These are inherited by `Creature` and `Player`.

## Data Model

This unit does not interact with any database tables. It operates entirely on in-memory game objects (`Player`, `Creature`) and their spatial relationships.

## Notable Implementation Details

1. **Optimization via Range Tightening**: The key performance feature is the update of `i_range` inside `operator()`. By reducing the search radius to the distance of the best-found-so-far candidate, the grid searcher can prune large portions of the search space. This is critical for performance in dense areas.
2. **Const Correctness**: The functor stores `Player const* const i_obj`, ensuring the player object is not modified during the search. The `operator()` takes `Creature const* u`, ensuring the candidate creature is not modified.
3. **Deleted Copy Constructor**: The private declaration of the copy constructor prevents accidental copying. This is important because copying the functor would duplicate the `i_range` state, leading to incorrect search results if copies were used in parallel or if the searcher relied on the functor's mutable state to track progress.
4. **Dependency on `INTERACTION_DISTANCE`**: The initial range is hardcoded to `INTERACTION_DISTANCE`. This constant likely defines the standard client-side interaction range for NPCs. If this constant changes, the behavior of this functor changes accordingly.
5. **No Phase Mask Handling**: Unlike some other checkers in `GridNotifiers.h` (e.g., `WorldObjectSearcher` which checks `InSamePhase`), this functor does not explicitly check phase masks. It relies on `CanInteractWithNPC` or the underlying grid searcher to handle phase visibility. If `CanInteractWithNPC` does not account for phases, this could lead to finding NPCs that are technically in range but not visible to the player due to phasing. However, `IsWithinDistInMap` usually implies map-level presence, and `CanInteractWithNPC` often includes visibility checks.

## Member Reference

**NearestInteractableNpcWithFlag**
Constructor that initializes the functor with a `Player` pointer, required `npcFlags`, and sets the initial search range to `INTERACTION_DISTANCE`. Prevents copying by declaring the copy constructor as private.

**GetFocusObject**
Returns a constant reference to the `Player` object (`i_obj`) that initiated the search. Used by the grid searcher infrastructure to access the origin object.

**operator()**
The main evaluation function. Checks if a candidate `Creature` is within the current `i_range` and if the player can interact with it using the specified `npcFlags`. If valid, it updates `i_range` to the creature's distance to optimize subsequent searches and returns `true`. Otherwise, returns `false`.

**GetLastRange**
Returns the final value of `i_range` after the search completes, indicating the distance to the nearest found NPC or the remaining search radius.

**NearestInteractableNpcWithFlag#2**
Private, deleted copy constructor declaration to prevent instantiation of copies of the functor, preserving the integrity of the mutable `i_range` state.

---

<!-- machine-true, projected from graph.json -->

## Map — NearestInteractableNpcWithFlag

*Source:* GridNotifiers.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| NearestInteractableNpcWithFlag | ctor | — | Player.Main/FindNearestInteractableNpcWithFlag | — |
| GetFocusObject | method | — | — | — |
| operator() | method | — | — | — |
| GetLastRange | method | — | — | — |
| NearestInteractableNpcWithFlag#2 | decl | — | — | — |

---

<!-- verify: failed-members | invented: operator -->
