# DungeonMap

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# DungeonMap

**Purpose & Responsibilities**

`DungeonMap` is a specialized subclass of `Map` designed to manage instanced dungeon and raid environments within the WoWVMaNGOS server. Its primary responsibility is to enforce the lifecycle rules specific to instances: tracking player presence, managing the transition between active and unloaded states, and coordinating the reset process when an instance expires or is manually reset. Unlike the base `Map` class, which handles general world geometry and object updates, `DungeonMap` adds logic for binding players to instances, preventing entry during critical transitions (such as unloading before a reset), and ensuring that instance-specific persistent state (`DungeonPersistentState`) is correctly maintained. It acts as the container for all entities (players, creatures, game objects) within a specific dungeon instance ID, mediating access and cleanup operations.

**Member-by-Member Behavior**

The `DungeonMap` class defines a small set of members focused on instance lifecycle management.

*   **`IsUnloadingBeforeReset`**: This method returns the value of the private member `m_resetAfterUnload`. It serves as a flag indicating whether the map is in a transitional state where it is being unloaded specifically to prepare for a reset. During this phase, the instance is effectively closed to new entries and is winding down existing activities.

**Cross-Unit Boundaries**

*   **Called by `game_Group_Group/AddMember`**: The method `IsUnloadingBeforeReset` is called by `AddMember` in the `game_Group_Group` unit (likely representing the `Group` class in `Group.cpp`). This collaboration occurs when a group attempts to add a member, potentially in the context of entering an instance or validating instance availability. The `Group` logic queries the `DungeonMap` to determine if the target instance is currently undergoing a pre-reset unload. If `IsUnloadingBeforeReset` returns `true`, the group operation (such as inviting a player to join the instance or forming a group for that instance) may be blocked or handled differently to prevent conflicts with the resetting instance. This ensures that groups cannot inadvertently bind themselves to an instance that is about to be wiped and reset.

**Data Model**

This unit does not directly interact with any database tables. All data management is performed in-memory through the `DungeonPersistentState` object (accessed via `GetPersistanceState()`) and the base `Map` infrastructure. Any persistence related to dungeon resets or instance data is handled by other units that serialize/deserialize the `DungeonPersistentState` to/from the database, but `DungeonMap` itself contains no SQL queries or direct table references.

**Notable Implementation Details**

*   **Inheritance and Overriding**: `DungeonMap` inherits from `Map` and overrides several key methods: `Add`, `Remove`, `Update`, `UnloadAll`, `CanEnter`, and `InitVisibilityDistance`. These overrides allow it to inject instance-specific logic into the general map update and player movement cycles. For example, `CanEnter` likely checks `IsUnloadingBeforeReset()` to deny entry.
*   **State Flagging**: The use of `m_resetAfterUnload` is a critical synchronization flag. It allows the system to distinguish between a normal unload (due to inactivity) and a forced unload preceding a reset. This distinction is vital for maintaining data integrity and preventing race conditions where a player might try to enter an instance while its data is being cleared.
*   **Visibility Distance**: The override of `InitVisibilityDistance()` suggests that dungeons may have different visibility or activation distance settings compared to open-world maps, possibly to optimize performance in smaller, more densely populated areas.
*   **Persistent State**: The method `GetPersistanceState()` returns a `DungeonPersistentState*`, which is distinct from the `WorldPersistentState` used by `WorldMap` or `BattleGroundPersistentState` used by `BattleGroundMap`. This indicates a specialized structure for storing dungeon-specific data such as boss kill states, quest progress, or timer information.

## Member Reference

**IsUnloadingBeforeReset**
Returns the boolean value of `m_resetAfterUnload`, indicating if the map is currently unloading in preparation for a reset. Called by `game_Group_Group/AddMember` to validate instance availability during group operations.

---

<!-- machine-true, projected from graph.json -->

## Map — DungeonMap

*Source:* Map.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| IsUnloadingBeforeReset | method | — | game_Group_Group/AddMember | — |
