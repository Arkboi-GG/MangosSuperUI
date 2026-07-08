<!-- provenance: failed-members -->
# AnyClosedDoorInRangeCheck

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# AnyClosedDoorInRangeCheck

`AnyClosedDoorInRangeCheck` is a predicate functor defined in `GridNotifiers.h` within the `MaNGOS` namespace. It implements a specific filtering criterion used by the server's spatial grid system to identify closed doors within a certain radius of a reference object.

This unit is part of a larger family of "Check" classes (e.g., `NearestGameObjectEntryInObjectRangeCheck`, `AnyHostileUnitInObjectRangeCheck`) that serve as callbacks for grid traversal algorithms. These algorithms iterate over objects in the world grid, invoking the `operator()` of the provided Check to determine if an object matches specific criteria. `AnyClosedDoorInRangeCheck` specializes this pattern for finding `GameObject`s that represent doors in their initial, closed state.

## Purpose & Responsibilities

The primary responsibility of `AnyClosedDoorInRangeCheck` is to answer the question: **"Is this specific GameObject a door, is it currently closed, and is it within the specified distance of the reference object?"**

It is designed to be passed to grid search utilities (such as `GameObjectSearcher` or `GameObjectListSearcher`, defined elsewhere in `GridNotifiers.h`) which handle the iteration over the spatial data structures. The Check itself contains no iteration logic; it only evaluates individual candidates.

Key behaviors include:
1.  **Type Verification:** Ensuring the candidate object is a `GAMEOBJECT_TYPE_DOOR`.
2.  **State Verification:** Ensuring the door is in `GO_STATE_READY`. In the context of MaNGOS/WoW, `GO_STATE_READY` typically represents the default, unactivated state of a game object. For doors, this corresponds to being closed.
3.  **Proximity Verification:** Calculating whether the distance between the reference object (`m_pObject`) and the candidate door is less than or equal to `m_fRange`.

## Member-by-Member Behavior

### Constructor: `AnyClosedDoorInRangeCheck`

```cpp
AnyClosedDoorInRangeCheck(WorldObject const* pObject, float fMaxRange) 
    : m_pObject(pObject), m_fRange(fMaxRange) {}
```

*   **Purpose:** Initializes the predicate with the necessary context to perform its checks.
*   **Parameters:**
    *   `pObject`: A pointer to the `WorldObject` serving as the center point for the range check. This is typically the entity (player, creature, or object) looking for doors.
    *   `fMaxRange`: The maximum distance (in world units) within which a door is considered "in range."
*   **Behavior:** Stores these values in private members `m_pObject` and `m_fRange`. No validation is performed on the pointer or range value at construction time; validity is assumed by the caller.

### Method: `operator()`

```cpp
bool operator() (GameObject* pGo)
{
    return pGo->GetGoType() == GAMEOBJECT_TYPE_DOOR &&
           pGo->GetGoState() == GO_STATE_READY &&
           m_pObject->IsWithinDist(pGo, m_fRange);
}
```

*   **Purpose:** Evaluates a single `GameObject` candidate against the criteria established in the constructor.
*   **Input:** `pGo` – A pointer to a `GameObject` instance retrieved from the grid.
*   **Return Value:** `true` if the object is a closed door within range; `false` otherwise.
*   **Logic Flow:**
    1.  **Type Check:** `pGo->GetGoType() == GAMEOBJECT_TYPE_DOOR`
        *   Retrieves the type of the game object. If it is not a door, the expression short-circuits and returns `false`.
    2.  **State Check:** `pGo->GetGoState() == GO_STATE_READY`
        *   Checks the current state of the door. `GO_STATE_READY` indicates the door is in its default state. For most doors, this means closed. If the door has been opened (typically transitioning to `GO_STATE_ACTIVE` or similar depending on implementation specifics of the door script), this check fails.
    3.  **Distance Check:** `m_pObject->IsWithinDist(pGo, m_fRange)`
        *   Delegates to the `WorldObject` class (specifically the `IsWithinDist` method) to calculate the distance between `m_pObject` and `pGo`.
        *   Note: `IsWithinDist` generally calculates 2D distance (ignoring Z-axis height differences) unless specified otherwise by overload resolution, though the specific signature used here depends on the `WorldObject` definition. Given the parameter list `(GameObject*, float)`, it likely uses the standard 2D or 3D distance check defined in `WorldObject`. *Correction*: Looking at other checks in the file like `AllGameObjectsWithEntryInRange`, they explicitly pass `false` for a 3rd boolean argument to `IsWithinDist` to force 2D. Here, no such argument is passed. The behavior depends on the `WorldObject::IsWithinDist` overload resolution. Typically, `IsWithinDist(Object*, float)` implies a 2D check in many MaNGOS versions, but strictly speaking, it relies on the base class implementation.
    *   If all three conditions are met, `true` is returned.

## Cross-Unit Boundaries

### Called By: `WorldObject.Object/FindNearbyClosedDoor`

*   **Direction:** Outbound call from `WorldObject` (or a related utility) *into* this Check.
*   **Collaboration:**
    *   The `WorldObject` subsystem (likely via a helper method like `FindNearbyClosedDoor` mentioned in the MAP) needs to locate doors.
    *   It constructs an instance of `AnyClosedDoorInRangeCheck`, passing `this` (the calling object) and a desired range.
    *   It then passes this Check instance to a grid searcher (e.g., `GameObjectSearcher`).
    *   The grid searcher iterates through nearby `GameObject`s and invokes `AnyClosedDoorInRangeCheck::operator()` on each.
    *   This separation allows the grid traversal logic to remain generic while the specific filtering logic (door + closed + range) is encapsulated in this lightweight functor.

### Calls Out: None

*   The `operator()` method calls `pGo->GetGoType()`, `pGo->GetGoState()`, and `m_pObject->IsWithinDist()`.
*   While these are method calls on other objects (`GameObject` and `WorldObject`), they are not considered "cross-unit" calls in the architectural sense of calling into a distinct logical module or service defined in the MAP. They are standard object interface calls within the core entity hierarchy. The MAP explicitly lists "—" for calls out, indicating no dependencies on other *named* units in the provided map structure.

## Data Model

This unit does not interact directly with any database tables. It operates entirely on in-memory `GameObject` instances that have already been loaded into the world grid. Therefore, no SQL queries or table references are involved in its execution.

## Notable Implementation Details

1.  **Short-Circuit Evaluation:** The `operator()` uses logical AND (`&&`) operators. This ensures that expensive operations (like distance calculation) are only performed if the cheaper type and state checks pass first. This is critical for performance, as grid searches may evaluate hundreds of objects.
2.  **State Semantics:** The reliance on `GO_STATE_READY` to mean "closed" is specific to how MaNGOS models doors. Developers must ensure that door scripts correctly transition states (e.g., to `GO_STATE_ACTIVE` when opened) for this check to work as intended. If a door remains in `READY` state while visually open due to a bug in the door's AI or script, this check will incorrectly report it as closed.
3.  **Const Correctness:** The constructor takes `WorldObject const*`, and the `operator()` takes `GameObject*` (non-const). This allows the check to read from the reference object without modification, while potentially allowing the `GameObject` interface to be non-const (though `GetGoType` and `GetGoState` are typically const methods).
4.  **No Caching:** The check performs fresh calculations for every invocation. There is no internal caching of results or distances. This is appropriate because the state of doors and positions of objects can change frequently.
5.  **Functor Pattern:** As a functor, it can be copied or moved efficiently. It holds only pointers and a float, making it very lightweight. This fits the design of the grid searchers which may instantiate or pass these checks by value.

## Member Reference

**AnyClosedDoorInRangeCheck**
Constructor that initializes the check with a reference `WorldObject` (`m_pObject`) and a maximum range (`m_fRange`). It stores these values for use during evaluation.

**operator()**
Method that evaluates a `GameObject` (`pGo`). Returns `true` if the object is of type `GAMEOBJECT_TYPE_DOOR`, is in state `GO_STATE_READY` (closed), and is within `m_fRange` distance of `m_pObject` (using `IsWithinDist`). Returns `false` otherwise. Uses short-circuit evaluation for performance.

---

<!-- machine-true, projected from graph.json -->

## Map — AnyClosedDoorInRangeCheck

*Source:* GridNotifiers.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| AnyClosedDoorInRangeCheck | ctor | — | WorldObject.Object/FindNearbyClosedDoor | — |
| operator() | method | — | — | — |

---

<!-- verify: failed-members | invented: operator -->
