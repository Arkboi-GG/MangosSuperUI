<!-- provenance: failed-members -->
# AllWorldObjectsInRange

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# AllWorldObjectsInRange

**Purpose & Responsibilities**

`AllWorldObjectsInRange` is a predicate functor defined in the `MaNGOS` namespace within `GridNotifiers.h`. Its purpose is to serve as a filtering criterion for spatial queries within the server's grid-based object management system. Specifically, it determines whether a candidate `WorldObject` lies within a specified 2D radius of a reference `WorldObject`.

By encapsulating this distance check into a standalone callable object, the class enables the grid traversal infrastructure (such as `WorldObjectWorker` or various searcher templates defined in the same header) to iterate over objects in a grid cell and apply this filter uniformly. This decouples the logic of "which objects are nearby" from the mechanics of "how to iterate through the grid."

**Member-by-Member Behavior**

The unit consists of two members: a constructor and the call operator.

1.  **Constructor (`AllWorldObjectsInRange`)**
    *   **Behavior:** Initializes the functor's internal state. It stores a constant pointer to the reference object (`m_pObject`) around which the range is calculated, and the maximum allowed distance (`m_fRange`).
    *   **Parameters:**
        *   `WorldObject const* pObject`: The central point for the distance calculation.
        *   `float fMaxRange`: The radius threshold. Objects within this distance are considered valid matches.

2.  **Call Operator (`operator()`)**
    *   **Behavior:** Evaluates a candidate `WorldObject` (named `go` in the source, despite the variable name suggesting a GameObject) against the stored criteria. It returns `true` if the candidate is within the specified range of the reference object, and `false` otherwise.
    *   **Logic:** The method delegates the actual distance calculation to `m_pObject->IsWithinDist(go, m_fRange, false)`.
    *   **Critical Detail:** The third argument to `IsWithinDist` is hardcoded to `false`. In the MaNGOS/WowVMaNGOS codebase, this boolean parameter dictates whether the check uses 3D Euclidean distance (including the Z-axis/elevation) or 2D planar distance (X/Y only). By passing `false`, `AllWorldObjectsInRange` strictly performs a **2D distance check**. Consequently, an object located directly above or below the reference object but within the horizontal radius will be considered "in range," regardless of vertical separation.

**Cross-Unit Boundaries**

*   **Called by:** `Map.ScriptCommands/ScriptCommand_StartScriptForAll`
    *   **Collaboration:** The `ScriptCommand_StartScriptForAll` function in the `Map` unit utilizes `AllWorldObjectsInRange` as a filtering mechanism. When initiating a script for all objects within a specific area, the `Map` unit likely iterates through relevant grid cells. It instantiates `AllWorldObjectsInRange` with a target location and radius, then passes this functor to a grid searcher or worker. The searcher invokes `operator()` on each object found in the relevant grids. Only objects for which `AllWorldObjectsInRange` returns `true` are processed further by the script command. This design keeps the spatial filtering logic separate from the script execution logic.

*   **Calls out:** None.
    *   The functor itself does not initiate calls to other architectural units. It relies on methods of the `WorldObject` class (specifically `IsWithinDist`), but these are member function calls on the object passed to it, not cross-unit dependencies in the architectural sense defined by the map.

**Data Model**

This unit does not interact with any database tables. It operates entirely on in-memory object states and coordinates.

**Notable Implementation Details**

1.  **2D Distance Constraint:** The hardcoding of `false` for the 3D distance flag in `IsWithinDist` is a significant design choice. If a script or system component requires checking for objects within a true spherical volume (including height differences), `AllWorldObjectsInRange` is **not** the correct functor to use. Engineers must be aware that this functor ignores Z-axis distance.
2.  **Const-Correctness:** The reference object `m_pObject` is stored as a `const` pointer, ensuring the functor cannot modify the reference object during evaluation. The `operator()` takes a non-const `WorldObject*`, allowing it to potentially inspect mutable state of the candidate, though it only reads position data.
3.  **Functor Pattern:** This class follows the standard C++ functor pattern, allowing it to be passed to STL algorithms or custom iterators (like the grid searchers in `GridNotifiers.h`) that expect a callable object with a specific signature. This promotes code reuse and separates the "what" (find objects in range) from the "how" (grid iteration).

## Member Reference

**AllWorldObjectsInRange**
Constructor that initializes the functor with a reference `WorldObject` pointer and a maximum range float. It sets up the context for subsequent distance checks.

**operator()**
Method that evaluates a given `WorldObject` to determine if it is within the predefined 2D range of the reference object. It returns `true` if `m_pObject->IsWithinDist(go, m_fRange, false)` is true, effectively performing a planar distance check ignoring elevation.

---

<!-- machine-true, projected from graph.json -->

## Map — AllWorldObjectsInRange

*Source:* GridNotifiers.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| AllWorldObjectsInRange | ctor | — | Map.ScriptCommands/ScriptCommand_StartScriptForAll | — |
| operator() | method | — | — | — |

---

<!-- verify: failed-members | invented: operator -->
