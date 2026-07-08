<!-- provenance: failed-members -->
# AnySameFactionUnitInObjectRangeCheck

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# AnySameFactionUnitInObjectRangeCheck

**Purpose & Responsibilities**

`AnySameFactionUnitInObjectRangeCheck` is a predicate functor defined in `GridNotifiers.h` within the `MaNGOS` namespace. Its purpose is to filter `Unit` objects during grid-based searches to identify those that share the exact same faction template ID as a specified source `SpellCaster` and are located within a defined spatial range.

It implements the standard "Check" interface expected by grid searcher templates (such as `UnitSearcher` or `UnitListSearcher`, also defined in `GridNotifiers.h`). By providing a `GetFocusObject` method, it supplies the spatial and phase context required by the searchers to optimize iteration, and by implementing `operator()`, it provides the specific logical criteria for inclusion. This checker is typically used to find allies or faction-mates for mechanics like area-of-effect buffs, healing spells, or detection abilities that target units of a specific faction identity.

**Member-by-Member Behavior**

### Construction and State Initialization
**AnySameFactionUnitInObjectRangeCheck** (Constructor)
Initializes the checker with two parameters:
1.  `SpellCaster const* obj`: The source object acting as the anchor for the search.
2.  `float range`: The maximum distance within which targets are considered.

These values are stored in the private members `i_obj` and `i_range`, respectively.

### Spatial Context Provision
**GetFocusObject**
Returns a constant reference to the internal `i_obj`, cast to `WorldObject const&`. This method is mandated by the grid searcher infrastructure. It allows the searcher to access the phase mask and coordinates of the source object to perform early-exit optimizations (e.g., skipping objects in different phases or outside the relevant grid cells) before invoking the more expensive `operator()` logic.

### Evaluation Logic
**operator()**
This method evaluates whether a given `Unit*` (`u`) satisfies the search criteria. It returns `true` only if all of the following conditions are met:
1.  **Liveness**: The target unit `u` must be alive (`u->IsAlive()`).
2.  **Proximity**: The target unit must be within `i_range` of the source object `i_obj`, respecting map boundaries (`i_obj->IsWithinDistInMap(u, i_range)`).
3.  **Faction Identity**: The target unit must have the exact same faction template ID as the source object (`i_obj->GetFactionTemplateId() == u->GetFactionTemplateId()`). This is a strict identity check, distinct from friendship or hostility relationships.
4.  **Visibility**: The target unit must be able to see the source object in the world (`u->CanSeeInWorld(i_obj)`). This accounts for line-of-sight, stealth, and other visibility modifiers.

If any condition fails, the method returns `false`.

**Cross-Unit Boundaries**

*   **Called by `Unit.SpellAuras/Update`**:
    According to the MAP, this checker is instantiated and used by the `Update` method within the `Unit.SpellAuras` unit. During the aura update cycle, a spell effect may need to identify valid targets among nearby units. `Unit.SpellAuras/Update` creates an instance of `AnySameFactionUnitInObjectRangeCheck` and passes it to a grid searcher (likely `UnitListSearcher` or similar from `GridNotifiers.h`) to populate a list of units that share the caster's faction and are within range. This enables spells to correctly apply effects to faction allies or detect faction presence.

*   **Calls Out**:
    The members of this unit do not call into other external units directly. They rely on methods provided by the `Unit` and `WorldObject` classes (e.g., `IsAlive`, `IsWithinDistInMap`, `GetFactionTemplateId`, `CanSeeInWorld`), which are part of the core entity framework.

**Data Model**

This unit does not interact with any database tables. It operates entirely on in-memory object states and spatial data.

**Notable Implementation Details**

1.  **Strict Faction Equality vs. Friendship**: The check compares `GetFactionTemplateId()` directly. This differs from `IsFriendlyTo()` or `IsHostileTo()`, which evaluate dynamic relationships including reputation, PvP flags, and temporary states. `AnySameFactionUnitInObjectRangeCheck` identifies units with the same inherent faction definition, regardless of their current diplomatic status. This is crucial for mechanics that depend on faction identity rather than alliance.
2.  **Visibility Directionality**: The visibility check is `u->CanSeeInWorld(i_obj)`, meaning the *target* must see the *source*. While often symmetric, this directionality is significant if stealth or invisibility effects are asymmetric (e.g., a stealthed unit cannot see a visible unit, but the visible unit might not be able to see the stealthed one depending on detection rules). Here, the requirement is that the potential target is aware of the source's presence.
3.  **Boolean Filter, Not Nearest Finder**: Unlike checkers such as `NearestGameObjectEntryInObjectRangeCheck`, this class does not update its internal `i_range` upon finding a match. It is designed as a simple boolean filter ("Any") rather than a nearest-object finder. Consequently, it is used with searchers that either collect all matching units or stop at the first match, without iteratively narrowing the search radius.
4.  **Const Correctness**: The `operator()` accepts a non-const `Unit*` parameter, adhering to the generic `Check` interface pattern defined in the header comments. However, the method itself does not modify the unit. The `GetFocusObject` method returns a const reference, ensuring the source object remains immutable during the search.

## Member Reference

**AnySameFactionUnitInObjectRangeCheck**
Constructor that initializes the checker with a `SpellCaster const*` source object and a `float` range. Stores these in private members `i_obj` and `i_range`.

**GetFocusObject**
Method that returns a `WorldObject const&` reference to the stored source object (`i_obj`). Used by grid searchers to determine phase and spatial context.

**operator()**
Method that evaluates a `Unit*` against the criteria: must be alive, within `i_range` of `i_obj` (map-aware), have the same `FactionTemplateId` as `i_obj`, and be able to see `i_obj` in the world. Returns `bool`.

---

<!-- machine-true, projected from graph.json -->

## Map — AnySameFactionUnitInObjectRangeCheck

*Source:* GridNotifiers.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| AnySameFactionUnitInObjectRangeCheck | ctor | — | Unit.SpellAuras/Update | — |
| GetFocusObject | method | — | — | — |
| operator() | method | — | — | — |

---

<!-- verify: failed-members | invented: operator -->
