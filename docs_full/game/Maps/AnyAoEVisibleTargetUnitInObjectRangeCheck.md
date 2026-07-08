<!-- provenance: failed-members -->
# AnyAoEVisibleTargetUnitInObjectRangeCheck

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# AnyAoEVisibleTargetUnitInObjectRangeCheck

**Purpose & Responsibilities**

`AnyAoEVisibleTargetUnitInObjectRangeCheck` is a predicate functor (a "Check" class) within the `MaNGOS` namespace, defined in `GridNotifiers.h`. Its specific responsibility is to determine whether a given `Unit` is a valid target for an Area-of-Effect (AoE) spell, considering both spatial constraints and visibility rules.

It is designed to be used with grid-based searchers (such as `UnitSearcher` or `UnitListSearcher`) to iterate over entities in the game world. Unlike simpler range checks, this class enforces strict visibility requirements relative to the **original caster** of the spell, ensuring that players cannot target enemies they cannot see (e.g., behind walls or through line-of-sight blockers) and that certain immune entities (like totems) are excluded from AoE targeting.

**Member-by-Member Behavior**

The class contains three members: a constructor, a getter for the focus object, and the functional call operator.

### Construction and State Initialization
**AnyAoEVisibleTargetUnitInObjectRangeCheck** (Constructor)
Initializes the check with three parameters:
1.  `obj` (`WorldObject const*`): The origin point for distance calculations (often the center of the AoE effect or the caster).
2.  `originalCaster` (`SpellCaster const*`): The entity that originally cast the spell. This is critical for determining visibility and PvP flags.
3.  `range` (`float`): The maximum radius within which a unit is considered a potential target.

The constructor stores these references in private members `i_obj`, `i_originalCaster`, and `i_range`. It does not perform any validation or side effects during initialization.

### Focus Object Retrieval
**GetFocusObject**
Returns a constant reference to `*i_obj`. This method satisfies the interface contract required by grid searchers (like `UnitSearcher`), which need to know the "focus" object to optimize phase mask checks or spatial queries. In this context, the focus object is the spatial anchor for the range check.

### Target Validation Logic
**operator()**
This is the core logic of the class. It takes a `Unit* u` and returns `true` if `u` is a valid AoE target, `false` otherwise. The evaluation proceeds through a series of early-exit filters:

1.  **Totem Immunity Filter**:
    If `u` is a `Creature` (`TYPEID_UNIT`) and reports `IsImmuneToAoe()`, it is immediately rejected. This prevents spells from targeting totems or other creatures explicitly flagged as immune to area effects.

2.  **Visibility Check (Original Caster)**:
    If `i_originalCaster` is a `Unit`, the code verifies that `u` is visible to the caster using `u->IsVisibleForOrDetect(...)`. This ensures that the caster can actually "see" the target. If the caster is not a Unit (e.g., a GameObject casting a spell), this check is skipped.

3.  **World Visibility Check**:
    Verifies that `u` can see `i_obj` in the world (`u->CanSeeInWorld(i_obj)`). This handles basic phase masking and map-level visibility constraints between the target and the effect's origin.

4.  **Range Check**:
    Verifies that `u` is within `i_range` of `i_obj` using `i_obj->IsWithinDistInMap(u, i_range)`. This is the primary spatial constraint.

5.  **PvP Flag Check**:
    If `i_originalCaster` is a `Unit`, it checks `CanAttackWithoutEnablingPvP(u)`. This prevents non-PvP players from accidentally targeting PvP-flagged opponents (or vice versa, depending on server rules) via AoE spells.

6.  **Valid Attack Target Check**:
    Finally, it delegates to `i_originalCaster->IsValidAttackTarget(u)`. This is a comprehensive check that likely includes faction hostility, death status, and other general targeting rules defined in the `SpellCaster` class.

**Cross-Unit Boundaries**

*   **Called by `Spell.Main/SetTargetMap`**:
    The MAP indicates this check is instantiated and used by the spell targeting system (`Spell.Main/SetTargetMap`). When a spell resolves its targets, the engine creates an instance of `AnyAoEVisibleTargetUnitInObjectRangeCheck` and passes it to a grid searcher. The searcher iterates over nearby units, calling `operator()` on each. Units returning `true` are added to the spell's target list. This collaboration allows the spell system to offload complex visibility and immunity logic to specialized check classes while keeping the iteration loop generic.

*   **Calls into `Unit`, `Creature`, `SpellCaster`, `WorldObject`**:
    While not listed as "Calls out" in the MAP (which tracks cross-file/class dependencies), the implementation relies heavily on methods from these base classes:
    *   `Unit::IsVisibleForOrDetect`: Determines line-of-sight and detection status.
    *   `Unit::CanSeeInWorld`: Checks phase and map visibility.
    *   `WorldObject::IsWithinDistInMap`: Calculates spatial distance.
    *   `Unit::CanAttackWithoutEnablingPvP`: Manages PvP flag interactions.
    *   `SpellCaster::IsValidAttackTarget`: Validates general targeting rules.
    *   `Creature::IsImmuneToAoe`: Checks specific creature flags.

**Data Model**

This unit does not interact with any database tables. It operates entirely on runtime memory objects (`Unit`, `WorldObject`, etc.) and their current states.

**Notable Implementation Details**

1.  **Distinction Between `i_obj` and `i_originalCaster`**:
    The class separates the spatial origin (`i_obj`) from the logical caster (`i_originalCaster`). This is crucial for spells where the effect center differs from the caster (e.g., a projectile landing at a specific coordinate). Distance is calculated from `i_obj`, but visibility and PvP checks are performed relative to `i_originalCaster`.

2.  **Strict Visibility Enforcement**:
    Unlike `AnyAoETargetUnitInObjectRangeCheck` (also in `GridNotifiers.h`), which skips the `IsVisibleForOrDetect` check, this class enforces it. This makes it suitable for spells that require the caster to have line-of-sight to the target, even for AoE effects.

3.  **Totem Exclusion**:
    The explicit check for `IsImmuneToAoe()` on creatures ensures that totems (which are often creatures in MaNGOS) are not targeted by AoE spells unless they lose this immunity flag. This is a common game mechanic to protect totems from being destroyed by area damage intended for enemies.

4.  **Const-Correctness**:
    The class holds `const` pointers to `i_obj` and `i_originalCaster`, ensuring it does not modify the state of the caster or the origin object during the check. The `operator()` is not marked `const` because it might theoretically modify internal state in derived classes, though this specific implementation does not. However, the standard pattern for these checks often omits `const` on `operator()` to allow for flexible extension.

5.  **Performance Consideration**:
    The checks are ordered to fail fast. Totem immunity and basic type checks happen first, followed by visibility (which can be expensive due to raycasting), then distance, and finally the comprehensive `IsValidAttackTarget`. This ordering minimizes the cost of rejecting invalid targets.

## Member Reference

**AnyAoEVisibleTargetUnitInObjectRangeCheck**
Constructor that initializes the check with a spatial origin object (`obj`), the original spell caster (`originalCaster`), and a maximum range (`range`). These are stored in private members for use during target validation.

**GetFocusObject**
Returns a constant reference to the spatial origin object (`i_obj`). Used by grid searchers to determine the phase mask or spatial context for the search.

**operator()**
Evaluates whether a given `Unit` is a valid AoE target. Returns `false` if the unit is a totem/immune creature, not visible to the original caster (if applicable), not visible in the world, out of range, or fails PvP/attack validity checks. Returns `true` only if all conditions are met.

---

<!-- machine-true, projected from graph.json -->

## Map — AnyAoEVisibleTargetUnitInObjectRangeCheck

*Source:* GridNotifiers.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| AnyAoEVisibleTargetUnitInObjectRangeCheck | ctor | — | Spell.Main/SetTargetMap | — |
| GetFocusObject | method | — | — | — |
| operator() | method | — | — | — |

---

<!-- verify: failed-members | invented: operator -->
