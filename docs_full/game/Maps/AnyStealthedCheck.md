<!-- provenance: failed-members -->
# AnyStealthedCheck

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# AnyStealthedCheck

**AnyStealthedCheck** is a predicate functor defined in `GridNotifiers.h` within the `MaNGOS` namespace. It serves as a specialized filter for grid-based spatial queries, specifically designed to identify `Unit` objects that are currently in a stealthed state.

As part of the MaNGOS server's grid notification and search infrastructure, this class implements the standard "Check" interface used by various searcher templates (such as `UnitSearcher`, `UnitListSearcher`, etc.). These searchers iterate over objects in a specific grid cell or map area and invoke the `operator()` of the provided Check instance to determine if an object meets specific criteria. `AnyStealthedCheck` encapsulates the logic required to detect stealth, allowing higher-level systems—such as player detection mechanics—to query the environment for hidden entities without embedding visibility logic directly into the search algorithms.

## Purpose & Responsibilities

The primary responsibility of `AnyStealthedCheck` is to provide a boolean evaluation of whether a given `Unit` is stealthed. It acts as a bridge between the generic grid traversal mechanisms and the specific game mechanic of stealth detection.

Key responsibilities include:
1.  **Encapsulating Stealth Logic:** It centralizes the definition of "stealthed" for the purposes of grid searches, relying on the `Unit`'s internal visibility state (`VISIBILITY_GROUP_STEALTH`).
2.  **Providing a Focus Object:** Like all Check classes in this system, it stores a pointer to a "focus" `WorldObject`. While `AnyStealthedCheck` itself does not use this object for distance or phase calculations in its `operator()`, it provides the `GetFocusObject()` method to satisfy the interface contract expected by searcher templates. This allows the searchers to potentially optimize queries based on the focus object's phase mask or location, although the stealth check itself is purely state-based.
3.  **Integration with Detection Systems:** It is explicitly called by `Player.Main/HandleStealthedUnitsDetection`, indicating its role in determining which stealthed units are detected by a player, likely as part of a broader visibility or perception update routine.

## Member-by-Member Behavior

### Construction and Initialization

**AnyStealthedCheck**
*   **Kind:** Constructor
*   **Signature:** `explicit AnyStealthedCheck(WorldObject const* fobj)`
*   **Behavior:** Initializes the check instance with a pointer to a focus `WorldObject`. This object is stored in the private member `i_fobj`. The constructor is marked `explicit` to prevent implicit conversions.
*   **Context:** This constructor is invoked by `Player.Main/HandleStealthedUnitsDetection`. The caller passes a `WorldObject` (likely the player performing the detection) as the focus. Although the stealth check logic does not currently utilize this focus object for filtering, passing it maintains consistency with the `Check` interface pattern used throughout `GridNotifiers.h`.

### Interface Methods

**GetFocusObject**
*   **Kind:** Method
*   **Signature:** `WorldObject const& GetFocusObject() const`
*   **Behavior:** Returns a constant reference to the focus `WorldObject` stored during construction (`*i_fobj`).
*   **Usage:** This method is part of the standard interface for all Check classes in `GridNotifiers.h`. It allows searcher templates to access the focus object if needed for pre-filtering (e.g., checking phase masks before invoking the check). In the case of `AnyStealthedCheck`, the returned object is not used by the check's own logic but satisfies the interface requirement.

### Evaluation Logic

**operator()**
*   **Kind:** Method
*   **Signature:** `bool operator()(Unit* u)`
*   **Behavior:** Evaluates whether the provided `Unit` pointer `u` represents a stealthed entity.
    *   It calls `u->GetVisibility()` to retrieve the current visibility group of the unit.
    *   It compares this value against the constant `VISIBILITY_GROUP_STEALTH`.
    *   Returns `true` if the unit's visibility group is `VISIBILITY_GROUP_STEALTH`, and `false` otherwise.
*   **Implications:** This check is strictly based on the unit's internal visibility state. It does not perform additional checks for line-of-sight, detection ranges, or phase differences. Those concerns are handled by the caller (`Player.Main/HandleStealthedUnitsDetection`) or by the searcher template's iteration bounds. The simplicity of this operator ensures efficient filtering during grid traversals.

## Cross-Unit Boundaries

### Called By

*   **`Player.Main/HandleStealthedUnitsDetection`**: This is the sole external caller identified in the map. This method resides in the `Player` class (specifically the main partial). It uses `AnyStealthedCheck` to filter units during a detection process. The collaboration involves:
    *   **Direction:** `Player.Main` creates an instance of `AnyStealthedCheck`, passing itself (or a related `WorldObject`) as the focus.
    *   **Data Crossing:** The `Player` object passes a pointer to the focus object into the `AnyStealthedCheck` constructor.
    *   **Why:** The `Player` needs to identify which nearby units are stealthed to determine if they are detected. By using a dedicated Check class, the `Player` leverages the generic grid search infrastructure to efficiently iterate over relevant units and apply the stealth filter.

### Calls Out

*   **None:** `AnyStealthedCheck` does not call into other units. Its `operator()` relies solely on methods of the `Unit` class passed as an argument (`GetVisibility()`), which is considered an interface usage rather than a cross-unit dependency in this context. The `GetFocusObject()` method returns a reference to data held internally.

## Data Model

This unit does not interact with any database tables. It operates entirely on in-memory game objects (`Unit` and `WorldObject`) and their runtime states.

## Notable Implementation Details

1.  **Interface Compliance:** `AnyStealthedCheck` adheres to the "Model Check class" pattern described in the comments of `GridNotifiers.h`. This pattern requires:
    *   A constructor accepting a focus object.
    *   A `GetFocusObject()` method.
    *   An `operator()` that takes a specific object type (here, `Unit*`) and returns a boolean.
    *   This design allows it to be used with various template-based searchers like `UnitSearcher`, `UnitListSearcher`, etc., promoting code reuse and separation of concerns.

2.  **Simplicity of Stealth Definition:** The check defines "stealthed" solely by the `VISIBILITY_GROUP_STEALTH` enum value. This implies that the complexity of stealth mechanics (e.g., detection chances, line-of-sight breaks, phase shifts) is handled elsewhere, likely in the calling code (`Player.Main/HandleStealthedUnitsDetection`) or in the `Unit`'s visibility management system. The check itself is a pure state query.

3.  **Const Correctness:** The `GetFocusObject()` method is `const`, and the `operator()` takes a non-const `Unit*` but does not modify it. This aligns with the typical usage of predicates in C++ algorithms.

4.  **No Distance or Phase Filtering:** Unlike many other Check classes in `GridNotifiers.h` (e.g., `AnyUnitInObjectRangeCheck`, `NearestAttackableUnitInObjectRangeCheck`), `AnyStealthedCheck` does not perform distance checks or phase mask comparisons in its `operator()`. This suggests that the search space is already constrained by the grid searcher (which operates on a specific grid cell) or that the caller handles these constraints separately. This makes `AnyStealthedCheck` highly efficient for its specific purpose.

## Member Reference

**AnyStealthedCheck**
Constructor that initializes the check with a focus `WorldObject` pointer. Called by `Player.Main/HandleStealthedUnitsDetection`.

**GetFocusObject**
Returns a constant reference to the focus `WorldObject` stored during construction. Satisfies the Check interface contract.

**operator()**
Evaluates if the given `Unit` is stealthed by checking if its visibility group equals `VISIBILITY_GROUP_STEALTH`. Returns `true` if stealthed, `false` otherwise.

---

<!-- machine-true, projected from graph.json -->

## Map — AnyStealthedCheck

*Source:* GridNotifiers.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| AnyStealthedCheck | ctor | — | Player.Main/HandleStealthedUnitsDetection | — |
| GetFocusObject | method | — | — | — |
| operator() | method | — | — | — |

---

<!-- verify: failed-members | invented: operator -->
