<!-- provenance: failed-members -->
# NearestAttackableUnitInObjectRangeCheck

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# NearestAttackableUnitInObjectRangeCheck

**Purpose & Responsibilities**

`NearestAttackableUnitInObjectRangeCheck` is a predicate functor (a "Check" class) within the `MaNGOS` namespace, defined in `GridNotifiers.h`. Its sole responsibility is to determine whether a specific `Unit` is a valid, hostile, and visible attack target for a given attacker (`i_funit`), while simultaneously tracking the distance to the closest such target found so far.

It is designed to be used in conjunction with grid-based searchers (such as `UnitLastSearcher` or `UnitSearcher`) to efficiently locate the **nearest** attackable unit within a specified radius. Unlike simple range checks, this class dynamically shrinks its effective search radius as it finds closer targets, allowing the calling searcher to prune further searches once a sufficiently close target is identified. It encapsulates complex visibility, hostility, and PvP logic required for AI targeting decisions.

**Member-by-Member Behavior**

The class consists of a constructor, a getter for the focus object, and the core evaluation operator.

### Initialization and State

**NearestAttackableUnitInObjectRangeCheck** (Constructor)
Initializes the check with four parameters:
1.  `obj`: The `WorldObject` used as the geometric center for distance calculations. This is often the same as `funit`, but can differ (e.g., if checking from a totem's position for its owner).
2.  `funit`: The `Unit` acting as the attacker. Hostility and validity checks are performed relative to this unit.
3.  `owner`: An optional `Unit` pointer. If provided, it enforces additional PvP constraints (see below).
4.  `range`: The initial maximum search radius. This value is mutable and decreases as closer targets are found.

**GetFocusObject**
Returns a constant reference to `i_obj`. This allows the associated `Searcher` templates to extract the phase mask of the focus object, ensuring that only objects in the same phase are considered during the grid traversal.

### Evaluation Logic

**operator()**
This is the core predicate executed for every `Unit` encountered during a grid search. It returns `true` if the unit `u` is a valid target and updates the internal `i_range` to the distance of `u`. If it returns `false`, the unit is ignored.

The logic proceeds through these strict filters:
1.  **PvP Owner Check**: If `i_owner` is non-null, it calls `i_owner->CanAttackWithoutEnablingPvP(u)`. If this returns `false`, the target is rejected. This prevents attacking players who would trigger unwanted PvP flags for the owner, even if the direct attacker (`i_funit`) might technically be able to engage them.
2.  **Distance Check**: Verifies `u` is within the current `i_range` of `i_obj` using `IsWithinDistInMap`.
3.  **Validity Check**: Calls `i_funit->IsValidAttackTarget(u)`. This ensures the target is alive, not immune, and generally eligible for combat.
4.  **Visibility/Detection Check**: Calls `u->IsVisibleForOrDetect(i_funit, i_funit, false)`. This is a critical stealth/detection check. It ensures the attacker can actually see or detect the target, accounting for stealth states, line-of-sight, and detection mechanics.
5.  **Hostility Check**: Verifies `i_funit->IsHostileTo(u)`. The target must be considered hostile by the attacker.
6.  **Range Update**: If all checks pass, `i_range` is updated to `i_obj->GetDistance(u)`. This shrinks the search window for subsequent iterations, optimizing the search for the *nearest* target. Returns `true`.

If any check fails, it returns `false` without modifying `i_range`.

**Cross-Unit Boundaries**

*   **Called by `TotemAI/UpdateAI`**: As indicated in the MAP, this check is instantiated and used by `TotemAI`. Totems often need to find targets for their spells. The `TotemAI` likely passes the totem as `i_obj` (for positioning/range) and the totem's owner (player or creature) as `i_funit` or `i_owner` to ensure the totem attacks valid targets according to the owner's PvP status and visibility rules.
*   **Calls into `Unit` methods**:
    *   `CanAttackWithoutEnablingPvP`: Checks PvP constraints.
    *   `IsWithinDistInMap`: Calculates spatial distance.
    *   `IsValidAttackTarget`: Validates combat eligibility.
    *   `IsVisibleForOrDetect`: Handles stealth and visibility logic.
    *   `IsHostileTo`: Determines faction/hostility relationships.
    *   `GetDistance`: Retrieves exact distance for range shrinking.

**Data Model**

This unit does not interact with any database tables. It operates entirely on in-memory game objects (`Unit`, `WorldObject`).

**Notable Implementation Details**

1.  **Dynamic Range Shrinking**: The most important architectural detail is that `i_range` is modified inside `operator()`. This pattern is essential for "Nearest" searches. The searcher iterates through units; if a unit is found, the range is tightened. Subsequent units must be *closer* than the previously found best candidate to be accepted. This allows the searcher to potentially break early or skip distant grid cells if implemented efficiently.
2.  **Separation of Geometry and Agency**: The class distinguishes between `i_obj` (geometry center) and `i_funit` (agent). This allows scenarios where an effect originates from one location (e.g., a spell projectile or totem) but the targeting rules belong to another entity (the caster).
3.  **Strict Visibility**: The use of `IsVisibleForOrDetect` rather than a simple LOS check ensures that stealth mechanics are respected. A target might be in range and hostile, but if the attacker cannot detect them (due to stealth), they are not a valid target for this check.
4.  **Non-Copyable**: The class defines a private copy constructor `NearestAttackableUnitInObjectRangeCheck(const&)` to prevent accidental copying, which could lead to inconsistent state if multiple searchers tried to share the same check instance.

## Member Reference

**NearestAttackableUnitInObjectRangeCheck**
Constructor that initializes the check with a geometric focus object (`i_obj`), an attacker unit (`i_funit`), an optional owner for PvP checks (`i_owner`), and an initial search radius (`i_range`).

**GetFocusObject**
Returns a constant reference to `i_obj`, enabling the caller to access phase information for filtering.

**operator()**
Evaluates a candidate `Unit` `u`. Returns `true` if `u` is within the current `i_range`, is a valid attack target for `i_funit`, is visible/detectable by `i_funit`, is hostile to `i_funit`, and (if `i_owner` is set) does not trigger unwanted PvP for `i_owner`. On success, it updates `i_range` to the distance of `u` to enforce finding the *nearest* target.

**NearestAttackableUnitInObjectRangeCheck#2**
Private copy constructor declared to prevent copying of the check object, ensuring state integrity during search operations.

---

<!-- machine-true, projected from graph.json -->

## Map — NearestAttackableUnitInObjectRangeCheck

*Source:* GridNotifiers.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| NearestAttackableUnitInObjectRangeCheck | ctor | — | TotemAI/UpdateAI | — |
| GetFocusObject | method | — | — | — |
| operator() | method | — | — | — |
| NearestAttackableUnitInObjectRangeCheck#2 | decl | — | — | — |

---

<!-- verify: failed-members | invented: operator -->
