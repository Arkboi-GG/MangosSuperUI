# AreaGuardInfo

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# AreaGuardInfo

`AreaGuardInfo` is a lightweight data structure within the `GuardMgr` system that encapsulates the configuration and runtime state for a specific guard post or summoning location. It defines which creature templates are summoned for each faction (Alliance vs. Horde) and tracks the resource constraints—cooldowns and charges—that govern how frequently guards can be summoned at that location.

This struct is designed to be stored by value within `GuardMgr`'s internal map (`m_mAreaGuardInfo`). It contains no virtual functions, no dynamic memory allocation, and no complex lifecycle management, making it efficient to copy and store. Its primary responsibility is to resolve the correct creature ID for a given team and to maintain the mutable state (`cooldown`, `charges`) required by the guard summoning logic.

## Member-by-Member Behavior

### Construction and State Initialization
The constructor `AreaGuardInfo` initializes the immutable faction-specific creature IDs and sets the initial runtime state.
*   **Immutable Configuration**: It takes two `uint32` arguments, `creature_id_ally` and `creature_id_horde`, and assigns them to the `const` members `creatureIdAlliance` and `creatureIdHorde`. These values define the template IDs of the NPCs that will be spawned for each faction. Because these members are `const`, they cannot be modified after construction, ensuring that the guard post's identity remains stable throughout its lifetime.
*   **Runtime State**: The constructor initializes `cooldown` to `0` and `charges` to `GUARD_POST_MAX_CHARGES` (defined as `10` in `GuardMgr.h`). This indicates that a newly created or reset guard post is immediately available for use and has its maximum number of summons remaining.

### Faction Resolution
The method `GetCreatureIdForTeam` provides a simple lookup mechanism to determine which creature template should be spawned based on the requesting faction.
*   It accepts a `Team` enum value (either `ALLIANCE` or `HORDE`).
*   It returns the corresponding `const` member (`creatureIdAlliance` or `creatureIdHorde`).
*   If an invalid team value is passed, it returns `0`, which typically represents an invalid or non-existent creature ID in the game engine, effectively preventing a spawn.

## Cross-Unit Boundaries

### Collaboration with `GuardMgr`
`AreaGuardInfo` is tightly coupled with the `GuardMgr` singleton, which acts as its owner and manager.

1.  **Construction (`GuardMgr/GuardMgr`)**:
    *   **Direction**: `GuardMgr` calls `AreaGuardInfo`'s constructor.
    *   **Context**: During the initialization of the `GuardMgr` singleton (likely when loading configuration data from the database or static tables), `GuardMgr` creates instances of `AreaGuardInfo` for each valid guard post area. It passes the appropriate Alliance and Horde creature IDs for that area.
    *   **Purpose**: To populate the `m_mAreaGuardInfo` map with pre-configured guard post data.

2.  **Faction Lookup (`GuardMgr/SummonGuard`)**:
    *   **Direction**: `GuardMgr::SummonGuard` calls `AreaGuardInfo::GetCreatureIdForTeam`.
    *   **Context**: When a civilian NPC is attacked and needs reinforcement, `GuardMgr::SummonGuard` retrieves the `AreaGuardInfo` object associated with the civilian's area. It then calls `GetCreatureIdForTeam` with the civilian's faction to determine exactly which creature template to spawn.
    *   **Purpose**: To decouple the summoning logic from hard-coded faction checks, allowing the `AreaGuardInfo` struct to provide the correct entity definition dynamically.

## Data Model

`AreaGuardInfo` itself does not directly interact with database tables. It is a transient in-memory structure populated by `GuardMgr`. However, the data used to construct `AreaGuardInfo` instances originates from the game's static configuration tables (typically `creature_template` for the IDs and potentially a custom table like `guard_post` or similar for mapping areas to factions, though the specific source table is managed by `GuardMgr` and not exposed in this unit). No SQL queries are executed within `AreaGuardInfo`.

## Notable Implementation Details

*   **Const-Correctness**: The creature ID members (`creatureIdAlliance`, `creatureIdHorde`) are declared `const`. This is a deliberate design choice to prevent accidental modification of the guard post's identity after initialization. Any change to the creature IDs would require replacing the entire `AreaGuardInfo` object in the `GuardMgr` map, not just modifying fields.
*   **Default Return Value**: `GetCreatureIdForTeam` returns `0` for unknown teams. In the context of the WoW server engine, creature ID `0` is invalid. Callers (like `GuardMgr::SummonGuard`) must handle this case, likely by aborting the summon process if the returned ID is `0`.
*   **Charge Limit**: The macro `GUARD_POST_MAX_CHARGES` is set to `10`. This implies a guard post can summon guards up to 10 times before needing to recharge. The `charges` field is mutable, allowing `GuardMgr` to decrement it upon successful summons and reset it during the recharge cycle.
*   **No Validation**: The constructor does not validate that the provided creature IDs are valid or exist in the database. It assumes `GuardMgr` has already verified this data during its own initialization phase.

## Member Reference

**AreaGuardInfo**  
Constructor that initializes the guard post's configuration. It sets the immutable Alliance and Horde creature IDs from the provided arguments, resets the cooldown timer to `0`, and sets the charge count to `GUARD_POST_MAX_CHARGES` (10).

**GetCreatureIdForTeam**  
Method that returns the creature template ID corresponding to the specified `Team` (Alliance or Horde). Returns `creatureIdAlliance` for `ALLIANCE`, `creatureIdHorde` for `HORDE`, and `0` for any other value. Used by `GuardMgr` to determine which NPC to spawn.

---

<!-- machine-true, projected from graph.json -->

## Map — AreaGuardInfo

*Source:* GuardMgr.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| AreaGuardInfo | ctor | — | GuardMgr/GuardMgr | — |
| GetCreatureIdForTeam | method | — | GuardMgr/SummonGuard | — |
