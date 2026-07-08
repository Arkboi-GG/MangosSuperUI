<!-- provenance: failed-members -->
# CannibalizeObjectCheck

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# CannibalizeObjectCheck

## Purpose & Responsibilities

`CannibalizeObjectCheck` is a predicate functor defined in `GridNotifiers.h` within the `MaNGOS` namespace. Its responsibility is to determine whether a specific `Unit` (the "focus object") is eligible to cannibalize another entity in the game world.

In the MaNGOS server architecture, this class implements the "Check" pattern used by grid-based searchers (such as `UnitSearcher` or `WorldObjectSearcher`). It encapsulates the rules governing the cannibalization mechanic—specifically regarding faction alignment, target state (alive/dead), movement constraints (taxi flying), visibility, and species restrictions (humanoid/undead)—into a single callable object. This allows the grid traversal system to efficiently filter candidates without embedding this logic directly into the search loops.

The class supports checking against three types of entities:
1.  **Players** (`Player*`)
2.  **Corpses** (`Corpse*`)
3.  **Creatures** (`Creature*`)

It explicitly rejects all other object types via a templated fallback operator.

## Member-by-Member Behavior

### Construction and State Access

*   **`CannibalizeObjectCheck` (Constructor)**
    Initializes the checker with two parameters:
    *   `Unit const* fobj`: The unit attempting to perform the cannibalization (the "focus").
    *   `float range`: The maximum distance within which the target must be located.
    
    These values are stored in private members `i_fobj` and `i_range`.

*   **`GetFocusObject`**
    Returns a constant reference to the focus unit (`*i_fobj`). This method satisfies the interface contract required by the grid searcher infrastructure, allowing the searchers to determine the phase mask and spatial origin for the search operation.

### Evaluation Logic (Operators)

The core functionality resides in the overloaded `operator()` methods. Each variant evaluates a specific type of potential target. If the target meets all criteria, it returns `true`; otherwise, it returns `false`.

#### 1. Player Targets (`operator()(Player* u)`)
This method determines if the focus unit can cannibalize a specific `Player`.
*   **Rejection Criteria:** The method returns `false` immediately if any of the following are true:
    *   `i_fobj->IsFriendlyTo(u)`: The player is friendly to the cannibalizer.
    *   `u->IsAlive()`: The player is still alive.
    *   `u->IsTaxiFlying()`: The player is currently using a taxi flight path.
    *   `!i_fobj->CanSeeInWorld(u)`: The cannibalizer cannot see the player in the world (visibility checks fail).
*   **Acceptance Criteria:** If none of the above reject the target, the method checks if the player is within the specified range using `i_fobj->IsWithinDistInMap(u, i_range)`. If yes, it returns `true`.

#### 2. Creature Targets (`operator()(Creature* u)`)
This method determines if the focus unit can cannibalize a specific `Creature`.
*   **Rejection Criteria:** The method returns `false` immediately if any of the following are true:
    *   `i_fobj->IsFriendlyTo(u)`: The creature is friendly to the cannibalizer.
    *   `u->IsAlive()`: The creature is still alive.
    *   `u->IsTaxiFlying()`: The creature is currently using a taxi flight path.
    *   `(u->GetCreatureTypeMask() & CREATURE_TYPEMASK_HUMANOID_OR_UNDEAD) == 0`: The creature is **not** a Humanoid or Undead. This enforces the game rule that only humanoids and undead can be cannibalized.
    *   `!i_fobj->CanSeeInWorld(u)`: The cannibalizer cannot see the creature in the world.
*   **Acceptance Criteria:** If none of the above reject the target, the method checks if the creature is within the specified range using `i_fobj->IsWithinDistInMap(u, i_range)`. If yes, it returns `true`.

#### 3. Corpse Targets (`operator()(Corpse* u)`)
*   **Behavior:** The declaration `bool operator()(Corpse* u);` exists in the class, but the implementation is **not present** in `GridNotifiers.h`. Based on the structure of the other operators, this method likely resides in a corresponding `.cpp` file. It presumably checks if the corpse belongs to a humanoid/undead, is not friendly, and is within range.

#### 4. Generic Rejection (`template<class NOT_INTERESTED> bool operator()(NOT_INTERESTED*)`)
*   **Behavior:** This templated operator catches any pointer type that is not a `Player`, `Corpse`, or `Creature`. It unconditionally returns `false`. This ensures that the searcher ignores irrelevant object types (like `GameObject`, `DynamicObject`, etc.) during the grid traversal.

## Cross-Unit Boundaries

`CannibalizeObjectCheck` acts as a leaf node in the call graph for grid searches. It does not call out to other units in the provided MAP, nor is it called by other units in the MAP. However, its design implies tight coupling with the following subsystems:

1.  **Grid Searchers (Implicit Caller):** Classes like `UnitSearcher`, `WorldObjectSearcher`, or `UnitListSearcher` (defined in the same header) instantiate `CannibalizeObjectCheck` and invoke its `operator()` for every candidate object found in a grid cell. The direction of data flow is:
    *   **Input:** The searcher passes a candidate object pointer (`Player*`, `Creature*`, etc.) to `CannibalizeObjectCheck::operator()`.
    *   **Output:** `CannibalizeObjectCheck` returns a boolean indicating acceptance.
    *   **Why:** This decouples the iteration logic (searcher) from the business logic (cannibalization rules).

2.  **Unit/WorldObject Interface (Implicit Dependency):** The methods rely heavily on virtual methods from the `Unit` and `WorldObject` base classes (e.g., `IsFriendlyTo`, `IsAlive`, `IsWithinDistInMap`, `CanSeeInWorld`). These calls traverse up the inheritance hierarchy to the actual implementations in `Unit.cpp`, `Player.cpp`, `Creature.cpp`, etc.

## Data Model

This unit does not interact directly with any database tables. It operates entirely on in-memory object states (`Unit`, `Player`, `Creature`, `Corpse`).

## Notable Implementation Details

1.  **Species Restriction for Creatures:** The check for `CREATURE_TYPEMASK_HUMANOID_OR_UNDEAD` is critical. It prevents players from cannibalizing beasts, elementals, or other non-humanoid/undead creatures, adhering to World of Warcraft mechanics. This mask check is performed *before* the distance check, optimizing performance by rejecting invalid types early.

2.  **Taxi Flying Exclusion:** Both `Player` and `Creature` operators explicitly check `IsTaxiFlying()`. This prevents cannibalization attempts while the target is in transit via flight paths, which is consistent with game behavior where such actions are disabled during travel.

3.  **Visibility vs. Distance:** The code uses `CanSeeInWorld` for visibility checks and `IsWithinDistInMap` for distance. `IsWithinDistInMap` typically accounts for map boundaries and potentially line-of-sight depending on the specific implementation in the `WorldObject` class, but the explicit `CanSeeInWorld` call suggests a strict requirement for visual or logical visibility independent of pure geometric distance.

4.  **Missing Corpse Implementation:** The `operator()(Corpse* u)` is declared but not defined in the provided header. Engineers maintaining this code must locate its definition in the corresponding source file to understand how corpses are handled (e.g., whether it checks the corpse's owner's race, or if it treats all corpses uniformly).

5.  **Const Correctness:** The focus object `i_fobj` is stored as `Unit const*`, and `GetFocusObject` returns a `const` reference. This ensures the checker does not modify the state of the unit performing the action.

## Member Reference

**CannibalizeObjectCheck**
Constructor that initializes the checker with a focus `Unit` pointer and a maximum range. Stores these in `i_fobj` and `i_range`.

**GetFocusObject**
Returns a constant reference to the focus unit (`*i_fobj`). Used by grid searchers to determine the search origin and phase mask.

**operator()#2**
Overloaded operator for `Creature*` targets. Returns `false` if the creature is friendly, alive, taxi-flying, not a Humanoid/Undead, or not visible to the focus object. Returns `true` if the creature is within the specified range.

**operator()**
Overloaded operator for `Player*` targets. Returns `false` if the player is friendly, alive, taxi-flying, or not visible to the focus object. Returns `true` if the player is within the specified range. Note: The class also contains a declared but undefined `operator()(Corpse*)` and a templated `operator()(NOT_INTERESTED*)` that returns `false`, but these are not listed as distinct members in the provided MAP.

---

<!-- machine-true, projected from graph.json -->

## Map — CannibalizeObjectCheck

*Source:* GridNotifiers.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| CannibalizeObjectCheck | ctor | — | — | — |
| GetFocusObject | method | — | — | — |
| operator()#2 | method | — | — | — |
| operator() | method | — | — | — |

---

<!-- verify: failed-members | invented: operator -->
