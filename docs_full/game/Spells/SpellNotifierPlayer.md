# SpellNotifierPlayer

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# SpellNotifierPlayer

`SpellNotifierPlayer` is a functor struct defined within the `MaNGOS` namespace in `Spell.h`. It implements the visitor pattern required by the server’s spatial partitioning system (Grid/Object Manager) to identify all `Player` objects within a specific 3D radius of a spell’s target coordinates.

Its primary responsibility is to populate a `Spell::UnitList` with valid player targets for Area-of-Effect (AoE) spells. It enforces three specific filtering criteria during this population:
1.  **Validity:** The player must be alive and not currently flying on a taxi path.
2.  **Hostility:** The player must be hostile to the spell’s effective caster.
3.  **Proximity:** The player must be within the specified `i_radius` distance from the spell’s destination coordinates (`m_destX`, `m_destY`, `m_destZ`).

This struct is designed to be passed to grid iteration methods (such as `Visit` on a `PlayerMapType`) to efficiently gather targets without requiring manual loops over global player lists. It is declared as a `friend` of the `Spell` class, granting it access to protected members like `m_targets` and `GetAffectiveCasterObject()`.

## Member-by-Member Behavior

### Construction
The constructor initializes the functor’s internal state with references to the spell instance, the target list to be populated, the specific spell effect index, and the search radius. Crucially, it resolves the `i_originalCaster` by calling `Spell::GetAffectiveCasterObject()` from the `Spell` class. This ensures that hostility checks are performed against the correct entity (e.g., the original caster of a triggered spell or a game object owner), rather than just the immediate physical caster.

### Visit (PlayerMapType)
The `Visit` method is the core logic of the notifier. It iterates through a `PlayerMapType` (a collection of players in a specific grid cell or map region). For each player:
1.  It skips the player if they are dead or taxi-flying.
2.  It skips the player if they are friendly to the `i_originalCaster`.
3.  It calculates the 3D distance between the player and the spell’s destination coordinates.
4.  If the player is within `i_radius`, they are added to `i_data`.

### Visit (GridRefManager)
This templated overload accepts a `GridRefManager<SKIP>` but contains an empty body. This serves as a no-op placeholder, ensuring the functor satisfies the interface requirements for grid traversal algorithms that might pass different manager types, while explicitly ignoring non-player entities managed by these generic containers.

## Cross-Unit Boundaries

*   **Calls `Spell::GetAffectiveCasterObject()`**: In the constructor, `SpellNotifierPlayer` calls this method on the `Spell` class (defined in `Spell.h`/`Spell.cpp`) to determine the entity against which hostility is checked. This is critical for spells where the visual caster differs from the logical source of damage (e.g., summoned pets or triggered auras).
*   **Calls `Spell::m_targets`**: The `Visit` method accesses `i_spell.m_targets` (a member of `Spell`) to retrieve the destination coordinates (`m_destX`, `m_destY`, `m_destZ`) for distance calculations.
*   **Called by `Spell::FillAreaTargets`**: Although not explicitly shown in the provided source snippet, `SpellNotifierPlayer` is typically instantiated and passed to grid iteration functions within `Spell::FillAreaTargets` (defined in `Spell.cpp`) to gather player targets for AoE effects.

## Data Model

This unit does not interact with any database tables. It operates entirely on in-memory game state objects (`Player`, `Spell`, `SpellCaster`).

## Notable Implementation Details

*   **Friendship with Spell**: `SpellNotifierPlayer` is declared as a `friend` of the `Spell` class. This allows it to access `Spell::m_targets` (protected) and `Spell::GetAffectiveCasterObject()` (public, but relies on internal state). Without this friendship, the notifier would require public getters for target coordinates, cluttering the `Spell` API.
*   **Hostility Check Logic**: The check `i_originalCaster->IsFriendlyTo(pPlayer)` implies that only *hostile* players are added to the target list. This suggests `SpellNotifierPlayer` is primarily used for harmful AoE spells (damage, debuffs) rather than beneficial ones (heals, buffs), which would likely use a different notifier or invert this logic.
*   **Taxi Flying Exclusion**: Players who are `IsTaxiFlying()` are explicitly excluded. This prevents spells from targeting players who are effectively "out of bounds" or in a transitional state where combat interactions are typically disabled.
*   **Empty GridRefManager Overload**: The template `Visit(GridRefManager<SKIP>&)` does nothing. This indicates that `SpellNotifierPlayer` is strictly for finding *Players*. Other notifiers (like `SpellNotifierCreatureAndPlayer`, also declared in `Spell.h`) likely handle creatures. This separation optimizes performance by avoiding unnecessary checks on non-player entities when only players are relevant.

## Member Reference

**SpellNotifierPlayer**
Constructor that initializes the notifier with references to the spell, target list, effect index, and radius. It resolves the effective caster via `Spell::GetAffectiveCasterObject()`.

**Visit**
Two overloads exist. The first (`PlayerMapType& m`) iterates through players in a map/grid, filtering out dead, taxi-flying, or friendly players, and adds those within `i_radius` of the spell's destination to `i_data`. The second (`GridRefManager<SKIP>&`) is a no-op template placeholder.

---

<!-- machine-true, projected from graph.json -->

## Map — SpellNotifierPlayer

*Source:* Spell.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| SpellNotifierPlayer | ctor | — | — | — |
| Visit | method | — | — | — |
