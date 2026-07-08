# CreatureGroupsManager

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# CreatureGroupsManager

`CreatureGroupsManager` is a singleton service responsible for the lifecycle management of `CreatureGroup` objects within the server memory space. It acts as the central registry for all active creature groups, providing mechanisms to load groups from the database at startup, register newly formed groups during runtime, and erase groups that have been disbanded.

The manager itself contains minimal logic; its primary responsibility is maintaining the `std::map<ObjectGuid, CreatureGroup*> m_groups` container, which maps the original leader's GUID to the corresponding `CreatureGroup` instance. It does not handle the internal mechanics of grouping (such as movement formation, aggro sharing, or respawn logic); those behaviors are encapsulated within the `CreatureGroup` class defined in the same header.

## Purpose & Responsibilities

The core purpose of `CreatureGroupsManager` is to provide global access to creature group data via the `sCreatureGroupsManager` macro. Its specific responsibilities include:

1.  **Persistence Loading:** During world initialization, it iterates through the database to reconstruct `CreatureGroup` objects for creatures that are part of persistent groups.
2.  **Runtime Registration:** When a new group is formed dynamically (e.g., via script or AI logic), the manager registers the new `CreatureGroup` pointer into its internal map so it can be retrieved later by other systems (such as when a creature dies or respawns).
3.  **Cleanup:** When a group is disbanded, the manager removes the entry from its map to prevent dangling pointers and memory leaks.

## Member-by-Member Behavior

### Lifecycle and Access

**`instance`**
This static method implements the Meyers Singleton pattern. It ensures that only one instance of `CreatureGroupsManager` exists throughout the lifetime of the server process. It is thread-safe in modern C++ implementations due to the guarantee of static local variable initialization. This method is the entry point for accessing the manager globally via the `sCreatureGroupsManager` macro.

### Group Management

**`RegisterNewGroup`**
This method adds a new `CreatureGroup` to the manager's internal registry. It takes a `CreatureGroup*` as input and inserts it into the `m_groups` map using the group's original leader GUID (`group->GetOriginalLeaderGuid()`) as the key. This operation is typically called by `CreatureGroups::Load` after a group is reconstructed from the database, or potentially by other systems creating dynamic groups. It assumes the caller has already constructed the `CreatureGroup` object and populated its members.

**`EraseCreatureGroup`**
This method removes a `CreatureGroup` from the registry. It takes the `ObjectGuid` of the group's leader as input and erases the corresponding entry from `m_groups`. This is called by `CreatureGroups::DisbandGroup` when a group is permanently dissolved. By removing the entry, the manager ensures that subsequent lookups for this group will fail, effectively marking the group as non-existent in the server's state.

## Cross-Unit Boundaries

`CreatureGroupsManager` interacts with several other units to coordinate the loading and unloading of group data:

*   **Called by `ChatHandler.ServerCommands/HandleReloadCreatureGroupsCommand`:** When an administrator issues a reload command for creature groups, this handler calls `instance()` to get the manager, which then triggers the reloading process (likely via `Load`).
*   **Called by `Creature.Main/AddToWorld`:** When a creature is added to the world, the system checks if it belongs to a group. It accesses the manager to retrieve or create the appropriate `CreatureGroup` context for that creature.
*   **Called by `Creature.Main/LoadFromDB`:** During the initial loading of a creature from the database, the manager is accessed to determine if the creature is part of a pre-defined group.
*   **Called by `CreatureGroups/DisbandGroup`:** When a group is disbanded, the `CreatureGroup` class calls `EraseCreatureGroup` on the manager to clean up its registration.
*   **Called by `World/SetInitialWorldSettings`:** During server startup, the world object calls `instance()` to ensure the manager is initialized and ready to load data.
*   **Called by `CreatureGroups/Load`:** The `CreatureGroups` unit (which appears to be a separate logical unit or namespace handling the bulk loading logic) calls `RegisterNewGroup` to populate the manager's map with groups loaded from the database.

## Data Model

The `CreatureGroupsManager` does not directly execute SQL queries. However, it relies on the `CreatureGroups/Load` unit to populate its state from the database. Based on the presence of `Load` and `ConvertDBGuid`, the underlying data model likely involves a table storing group definitions, including leader GUIDs, member GUIDs, and formation options. The manager itself holds no direct database connection or query logic.

## Notable Implementation Details

*   **Singleton Pattern:** The use of `static CreatureGroupsManager* i = new CreatureGroupsManager();` inside `instance()` is a standard C++ idiom for lazy-initialized singletons. Note that the pointer is never deleted, implying the memory is leaked upon server shutdown. This is acceptable for a long-running server process but worth noting for strict memory auditing.
*   **Key Selection:** The map uses `GetOriginalLeaderGuid()` as the key. This suggests that even if the current leader of a group changes (e.g., the original leader dies and another member takes over), the group remains indexed by the *original* leader's GUID. This simplifies lookup consistency but requires callers to always know the original leader's GUID to find the group.
*   **No Thread Safety:** The `m_groups` map is not protected by a mutex. Since `RegisterNewGroup` and `EraseCreatureGroup` modify the map, and `LoadCreatureGroup` (declared in the header but not detailed in the map's "Calls out" for this unit, though likely implemented elsewhere or inline) reads it, concurrent access from different threads (e.g., a creature dying on one thread while another is being loaded) could lead to race conditions. Callers must ensure proper synchronization or that these operations occur on the main game loop thread.
*   **Memory Ownership:** The manager stores raw pointers (`CreatureGroup*`). It does not delete the pointed-to objects in `EraseCreatureGroup`. This implies that the `CreatureGroup` objects are owned by another entity (likely the `Creature` objects themselves or a higher-level scene manager) and the manager merely holds weak references for lookup purposes. The caller of `EraseCreatureGroup` is responsible for ensuring the object is valid until deletion occurs elsewhere.

## Member Reference

**`instance`**: Static method that returns the singleton instance of `CreatureGroupsManager`. Uses lazy initialization with a static local variable. Called by various parts of the server to access the global group registry.

**`RegisterNewGroup`**: Method that registers a new `CreatureGroup` into the internal `m_groups` map. Uses the group's original leader GUID as the key. Called by `CreatureGroups/Load` to populate the manager with groups from the database.

**`EraseCreatureGroup`**: Method that removes a `CreatureGroup` from the internal `m_groups` map by its leader's GUID. Called by `CreatureGroups/DisbandGroup` to clean up disbanded groups. Does not delete the `CreatureGroup` object itself.

---

<!-- machine-true, projected from graph.json -->

## Map — CreatureGroupsManager

*Source:* CreatureGroups.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| instance | method | — | ChatHandler.ServerCommands/HandleReloadCreatureGroupsCommand, Creature.Main/AddToWorld, Creature.Main/LoadFromDB, CreatureGroups/DisbandGroup, World/SetInitialWorldSettings | — |
| RegisterNewGroup | method | — | CreatureGroups/Load | — |
| EraseCreatureGroup | method | — | CreatureGroups/DisbandGroup | — |
