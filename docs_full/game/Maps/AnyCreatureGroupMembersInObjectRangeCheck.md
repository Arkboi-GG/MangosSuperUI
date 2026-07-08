<!-- provenance: failed-members -->
# AnyCreatureGroupMembersInObjectRangeCheck

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# AnyCreatureGroupMembersInObjectRangeCheck

## Purpose & Responsibilities

`AnyCreatureGroupMembersInObjectRangeCheck` is a predicate functor (a "Check" class) defined in `GridNotifiers.h` within the `MaNGOS` namespace. Its specific responsibility is to identify **alive creatures** that belong to the same **creature group** as a reference creature (`i_obj`) and are located within a specified spatial range.

This class is designed to be used with grid-based searchers (such as `UnitListSearcher` or `UnitSearcher`, also defined in `GridNotifiers.h`) to efficiently query the game world for allies or pack members during AI decision-making, spell targeting, or aggro propagation. It encapsulates the logic for verifying group membership, including handling cases where the target creature is a pet or summon owned by another member of the group.

It does not perform the search itself; rather, it provides the boolean evaluation criteria (`operator()`) that the search infrastructure applies to each candidate `Unit` in the grid.

## Member-by-Member Behavior

The class contains three members: a constructor, a getter for the focus object, and the functional call operator.

### `AnyCreatureGroupMembersInObjectRangeCheck` (Constructor)
Initializes the checker with a reference creature and a maximum search radius.
*   **Parameters:**
    *   `Creature const* obj`: The reference creature whose group membership defines the search criteria. Stored in `i_obj`.
    *   `float range`: The maximum distance from `obj` within which candidates are considered. Stored in `i_range`.
*   **Behavior:** Simply assigns these values to private member variables. No validation or side effects occur.

### `GetFocusObject`
Returns a constant reference to the reference creature (`i_obj`).
*   **Purpose:** This method satisfies the interface contract required by the grid searchers (e.g., `UnitSearcher`). The searchers use `GetFocusObject()` to determine the phase mask of the searcher, ensuring that only objects in the same phase are evaluated. This prevents the checker from evaluating entities that exist in different instance phases or visibility states relative to the reference creature.

### `operator()`
The core logic of the class. It evaluates whether a given `Unit* u` matches the criteria for being a group member in range.
*   **Input:** `Unit* u` (a candidate unit from the grid).
*   **Return:** `bool` (`true` if `u` is a valid group member in range, `false` otherwise).
*   **Logic Flow:**
    1.  **Liveness Check:** Returns `false` immediately if `u` is not alive (`!u->IsAlive()`). Dead creatures are not considered active group members for this check.
    2.  **Type Check:** Returns `false` if `u` is not a `Creature` (`!u->IsCreature()`). Players, game objects, and corpses are excluded.
    3.  **Distance Check:** Returns `false` if `u` is not within `i_range` of `i_obj` in the same map (`!i_obj->IsWithinDistInMap(u, i_range)`). This ensures spatial relevance.
    4.  **Visibility/Phase Check:** Returns `false` if `u` cannot see `i_obj` in the world (`!u->CanSeeInWorld(i_obj)`). This handles phase masks and visibility flags, ensuring the group member is logically visible to the reference creature.
    5.  **Direct Group Match:** Checks if `u`'s creature group ID matches `i_obj`'s creature group ID.
        *   Code: `i_obj->GetCreatureGroup() == static_cast<Creature*>(u)->GetCreatureGroup()`
        *   If true, returns `true`.
    6.  **Owner Group Match (Pet/Summon Handling):** If the direct match fails, it checks if `u` has an owner creature (`u->GetOwnerCreature()`).
        *   If an owner exists, it compares the owner's group ID to `i_obj`'s group ID.
        *   Code: `i_obj->GetCreatureGroup() == static_cast<Creature*>(pOwner)->GetCreatureGroup()`
        *   If true, returns `true`. This allows pets or summons of group members to be identified as part of the group context.
    7.  **Default:** If none of the above conditions are met, returns `false`.

## Cross-Unit Boundaries

*   **Called By:** `Unit.SpellAuras/Update`
    *   **Context:** The MAP indicates this checker is instantiated and used by the `SpellAuras` subsystem during the `Update` cycle.
    *   **Collaboration:** During spell aura updates (likely for spells that affect groups, such as buffs, debuffs, or area-of-effect triggers), the `SpellAuras` system needs to identify which nearby creatures are part of the caster's or target's group. It constructs an `AnyCreatureGroupMembersInObjectRangeCheck` object and passes it to a grid searcher (like `UnitListSearcher` in `GridNotifiers.h`). The searcher iterates over nearby units, calling `operator()` on this checker for each. The result determines which units receive the aura effect or are included in the spell's logic.
    *   **Direction:** Data flows from the `SpellAuras` system *into* this checker (via constructor arguments) and back out (via the boolean result of `operator()`).

## Data Model

This unit operates entirely in memory using the game server's object model (`Creature`, `Unit`, `WorldObject`). It does not interact with any database tables. All group membership data (`GetCreatureGroup()`) and spatial data are maintained in the runtime state of the `Creature` objects.

## Notable Implementation Details

1.  **Pet/Summon Inclusion via Owner:** The logic explicitly handles creatures that are pets or summons (`GetOwnerCreature()`). If a creature `u` is a pet of a group member, it is considered part of the group for the purposes of this check. This is crucial for mechanics where spells or aggro should propagate to pets of allies. Note that it does *not* recursively check the owner's owner; it only goes one level deep.
2.  **Strict Liveness Requirement:** Unlike some other checks in `GridNotifiers.h` (e.g., `NearestCreatureEntryWithLiveStateInObjectRangeCheck` which can search for corpses), this check strictly requires `u->IsAlive()`. Dead group members are ignored.
3.  **Visibility Dependency:** The check relies on `CanSeeInWorld()`. This means that if a group member is phased out or hidden from the reference creature (due to stealth, invisibility, or phase differences), they will not be detected, even if they are physically close and in the same group ID. This aligns with typical game mechanics where you cannot target or affect entities you cannot perceive.
4.  **No Range Contraction:** Unlike "Nearest" checks (e.g., `NearestAttackableUnitInObjectRangeCheck`), this class does *not* update `i_range` upon finding a match. It uses the initial `range` provided in the constructor for all evaluations. This is appropriate because it is typically used with `UnitListSearcher` (which collects *all* matches) rather than `UnitLastSearcher` (which finds the *nearest* match by contracting the range).
5.  **Static Cast Safety:** The code assumes that if `u->IsCreature()` is true, `static_cast<Creature*>(u)` is safe. This is consistent with the MaNGOS type hierarchy where `Creature` inherits from `Unit`.

## Member Reference

**AnyCreatureGroupMembersInObjectRangeCheck**
Constructor that initializes the checker with a reference `Creature const* obj` and a `float range`. Stores these in `i_obj` and `i_range` respectively.

**GetFocusObject**
Returns a constant reference to `i_obj`. Used by grid searchers to determine the phase mask for filtering candidates.

**operator()**
Evaluates if a `Unit* u` is an alive creature in the same group as `i_obj` and within `i_range`. Checks liveness, type, distance, visibility, direct group ID match, and owner group ID match (for pets/summons). Returns `true` if all conditions are met, `false` otherwise.

---

<!-- machine-true, projected from graph.json -->

## Map — AnyCreatureGroupMembersInObjectRangeCheck

*Source:* GridNotifiers.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| AnyCreatureGroupMembersInObjectRangeCheck | ctor | — | Unit.SpellAuras/Update | — |
| GetFocusObject | method | — | — | — |
| operator() | method | — | — | — |

---

<!-- verify: failed-members | invented: operator -->
