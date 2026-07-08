<!-- provenance: failed-members -->
# AnyAoETargetUnitInObjectRangeCheck

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# AnyAoETargetUnitInObjectRangeCheck

**Purpose & Responsibilities**
`AnyAoETargetUnitInObjectRangeCheck` is a predicate functor within the MaNGOS grid notification system, designed to identify valid targets for Area-of-Effect (AoE) spells. It encapsulates the logic required to determine if a specific `Unit` is a legitimate target for an AoE effect originating from a specific location (`i_obj`) and cast by a specific entity (`i_originalCaster`).

Its primary responsibility is to filter out invalid targets during grid searches (typically performed by `UnitListSearcher` or similar structures in `GridNotifiers.h`) by enforcing three critical constraints:
1.  **Immunity:** The target must not be immune to AoE effects (specifically checking for totems).
2.  **Visibility & Line-of-Sight:** The target must be able to see the origin point of the AoE (`i_obj`) in the world.
3.  **Threat & PvP Rules:** The original caster must be able to attack the target without triggering unintended PvP states, and the target must be considered a valid attack target by the caster.

This class is part of the broader "Check" pattern in `GridNotifiers.h`, where functors are passed to searchers to iterate over entities in a grid cell, returning `true` for entities that match specific criteria.

## Member-by-Member Behavior

### Constructor: `AnyAoETargetUnitInObjectRangeCheck`
Initializes the functor with the necessary context for evaluation:
*   `obj`: A pointer to a `WorldObject` representing the center or origin point of the AoE effect. Distance checks are relative to this object.
*   `originalCaster`: A pointer to a `SpellCaster` representing the entity that cast the spell. This is used to evaluate hostility, PvP rules, and validity of the attack target.
*   `range`: A `float` specifying the maximum radius within which a unit is considered a potential target.

The constructor stores these references in private members `i_obj`, `i_originalCaster`, and `i_range`.

### Method: `GetFocusObject`
Returns a constant reference to `*i_obj`. This method satisfies the interface contract expected by many grid searchers (such as `UnitSearcher` or `UnitListSearcher`), which often use the "focus object" to determine phase masks or initial spatial bounds before invoking the `operator()`. In this specific implementation, the focus object is the geometric center of the AoE.

### Method: `operator()`
This is the core evaluation logic. It accepts a `Unit* u` and returns `bool`. The method performs a series of short-circuit evaluations to reject invalid targets efficiently:

1.  **Totem Immunity Check:**
    ```cpp
    if (u->GetTypeId() == TYPEID_UNIT && ((Creature*)u)->IsImmuneToAoe())
        return false;
    ```
    If the unit is a `Creature` (TYPEID_UNIT) and is flagged as immune to AoE (common for totems or certain NPCs), it is immediately rejected. This prevents AoE spells from targeting or interacting with objects designed to be ignored by such effects.

2.  **World Visibility Check:**
    ```cpp
    if (!u->CanSeeInWorld(i_obj))
        return false;
    ```
    The target unit `u` must be able to see the origin object `i_obj` in the world. This handles basic line-of-sight and phase mask compatibility between the target and the AoE origin. Note that this checks if the *target* can see the *origin*, ensuring the target is aware of the effect's source location.

3.  **Distance Check:**
    ```cpp
    if (!i_obj->IsWithinDistInMap(u, i_range))
        return false;
    ```
    The target must be within the specified `i_range` from `i_obj`. `IsWithinDistInMap` ensures the distance calculation respects map boundaries and potentially phase differences, though the previous visibility check already handled phase compatibility.

4.  **PvP State Check:**
    ```cpp
    if (i_originalCaster->IsUnit() && !((Unit const*)i_originalCaster)->CanAttackWithoutEnablingPvP(u))
        return false;
    ```
    If the original caster is a `Unit` (player or creature), it verifies that attacking `u` would not inadvertently enable PvP flags on the caster. This is crucial for preventing neutral or friendly players from being targeted by AoE spells in a way that would turn them hostile or trigger PvP mechanics incorrectly.

5.  **Valid Attack Target Check:**
    ```cpp
    return i_originalCaster->IsValidAttackTarget(u);
    ```
    Finally, the method delegates to the `SpellCaster`'s `IsValidAttackTarget` method. This is a comprehensive check that considers hostility, selection flags, death status, and other game-specific rules to determine if `u` is a legal target for the caster. If all previous checks pass, this final determination decides the outcome.

## Cross-Unit Boundaries

### Called By
*   **`ChatHandler.UnitCommands/HandleAoEDamageCommand`**: Used by game master commands to simulate AoE damage. The command likely constructs an instance of this checker to identify all valid units in a radius for the purpose of applying damage or testing spell targeting logic.
*   **`Spell.Main/SetTargetMap`**: During spell resolution, the spell system uses this checker to populate the list of targets affected by an AoE spell. This is the primary gameplay integration point.
*   **`Unit.SpellAuras/Update`**: When updating spell auras, particularly those with periodic AoE effects (like a fireball explosion or a channeled AoE), this checker helps re-evaluate or confirm valid targets for the aura's effect.

### Calls Out
*   **None**: The `AnyAoETargetUnitInObjectRangeCheck` class itself does not call out to other units in the provided map. However, its `operator()` method calls methods on `Unit`, `Creature`, `WorldObject`, and `SpellCaster` instances (e.g., `IsImmuneToAoe`, `CanSeeInWorld`, `IsWithinDistInMap`, `CanAttackWithoutEnablingPvP`, `IsValidAttackTarget`). These are internal API calls within the engine's core object hierarchy, not cross-unit dependencies in the architectural sense defined by the map.

## Data Model
This unit does not interact with any database tables. It operates entirely on in-memory game objects (`Unit`, `WorldObject`, `SpellCaster`) and their current states.

## Notable Implementation Details

1.  **Totem-Specific Immunity Logic**: The check `u->GetTypeId() == TYPEID_UNIT && ((Creature*)u)->IsImmuneToAoe()` is specific to creatures. Players (`TYPEID_PLAYER`) are not subjected to this specific immunity check in this functor. This implies that player immunity to AoE is handled elsewhere (likely in `IsValidAttackTarget` or specific spell scripts), while creature/totem immunity is explicitly filtered here for performance or logical clarity.

2.  **Asymmetric Visibility Check**: The functor checks `u->CanSeeInWorld(i_obj)` (target sees origin) but does *not* explicitly check `i_obj->CanSeeInWorld(u)` (origin sees target). This is a deliberate design choice for AoE effects. An AoE spell often affects targets regardless of whether the caster can see them (e.g., a fireball exploding behind a wall still damages enemies in the blast radius if they are within the area, depending on specific spell flags). However, the target must be "aware" of the origin in terms of phase and basic LOS to be considered a valid participant in the interaction. The final `IsValidAttackTarget` call may impose additional visibility constraints if required by the specific spell type.

3.  **PvP Safety**: The inclusion of `CanAttackWithoutEnablingPvP` is a critical safeguard. Without this, a player casting an AoE spell near a neutral player might accidentally aggro them or trigger PvP mode, leading to unintended gameplay consequences. This check ensures that the AoE targeting logic respects the server's PvP configuration and faction relationships.

4.  **Reuse of `i_originalCaster`**: The functor distinguishes between `i_obj` (the geometric center) and `i_originalCaster` (the entity responsible for the spell). This separation allows for scenarios where the AoE origin is not the caster themselves (e.g., a projectile landing, a summoned object exploding, or a delayed effect). The hostility and validity checks are always relative to the *caster*, not the *origin*, which is correct for most spell mechanics.

5.  **Short-Circuit Evaluation**: The order of checks in `operator()` is optimized for early rejection. Cheap checks like type identification and immunity flags are performed first, followed by visibility and distance calculations, and finally the more expensive `IsValidAttackTarget` call. This minimizes computational overhead during grid iterations.

## Member Reference

**AnyAoETargetUnitInObjectRangeCheck**
Constructor that initializes the functor with the AoE origin object (`obj`), the spell caster (`originalCaster`), and the effective radius (`range`). It stores these in private members `i_obj`, `i_originalCaster`, and `i_range` for use in the evaluation logic.

**GetFocusObject**
Returns a constant reference to the stored origin object `i_obj`. This method provides the "focus" for grid searchers, allowing them to perform preliminary phase or spatial checks before invoking the main predicate.

**operator()**
The core predicate function that evaluates whether a given `Unit* u` is a valid AoE target. It returns `false` if the unit is an AoE-immune creature (like a totem), cannot see the origin object in the world, is outside the specified range, or if attacking it would enable unwanted PvP states for the caster. If these checks pass, it delegates to `i_originalCaster->IsValidAttackTarget(u)` for the final determination.

---

<!-- machine-true, projected from graph.json -->

## Map — AnyAoETargetUnitInObjectRangeCheck

*Source:* GridNotifiers.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| AnyAoETargetUnitInObjectRangeCheck | ctor | — | ChatHandler.UnitCommands/HandleAoEDamageCommand, Spell.Main/SetTargetMap, Unit.SpellAuras/Update | — |
| GetFocusObject | method | — | — | — |
| operator() | method | — | — | — |

---

<!-- verify: failed-members | invented: operator -->
