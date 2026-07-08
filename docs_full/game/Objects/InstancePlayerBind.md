# InstancePlayerBind

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# InstancePlayerBind

## Purpose & Responsibilities

`InstancePlayerBind` is a lightweight aggregate struct defined within `Player.h` that represents a single instance binding record for a player. In the context of the WoWVMaNGOS server emulation, "instance binding" refers to the mechanic where a player becomes tethered to a specific instance ID of a dungeon or raid map. This prevents the player from entering other instances of the same map until they leave the bound instance or the instance resets.

The struct serves two primary data-holding responsibilities:
1.  **State Persistence:** It holds a pointer to the `DungeonPersistentState` object, which contains the persistent data (boss kills, event states) for that specific instance.
2.  **Binding Type:** It tracks whether the binding is "permanent" (`perm`). Permanent bindings typically occur in raid instances when a boss is killed, locking the entire group to that specific instance ID until completion or reset. Non-permanent bindings might occur for solo dungeons or temporary instance locks.

This struct is not a standalone class with behavior; it is a data container used by the `Player` class (specifically the `InstanceSystem` subsystem) to manage the `BoundInstancesMap`.

## Member-by-Member Behavior

### Constructor

**`InstancePlayerBind()`**
*   **Kind:** Constructor
*   **Behavior:** Initializes the `InstancePlayerBind` object.
    *   Sets the `state` pointer to `nullptr`. This indicates that initially, there is no associated persistent dungeon state loaded or linked.
    *   Sets the `perm` boolean to `false`. This indicates that the binding is not permanent by default.
*   **Context:** This constructor is called whenever a new entry is inserted into the `Player::m_boundInstances` map (e.g., via `std::unordered_map::operator[]` or `emplace`). It ensures that any new binding starts in a clean, non-permanent, unlinked state.

## Cross-Unit Boundaries

The `InstancePlayerBind` struct itself has no methods that call out to other units, nor is it called by other units directly as a function. However, its members are heavily interacted with by the `Player` class (defined in the same header, `Player.h`, but logically part of the `Player` unit).

*   **Caller:** `Player` (specifically methods like `BindToInstance`, `UnbindInstance`, `ResetInstance`, and `_LoadBoundInstances`).
*   **Direction:** The `Player` unit reads and writes the `state` and `perm` members of `InstancePlayerBind` objects stored in its `m_boundInstances` map.
*   **Collaboration:**
    *   When a player enters an instance, `Player::BindToInstance` creates or retrieves an `InstancePlayerBind` entry. It assigns the `DungeonPersistentState*` to the `state` member and sets `perm` based on whether the instance is a raid and if a boss kill occurred.
    *   When saving/loading player data, `Player::_LoadBoundInstances` populates these structs from database results, and `Player::_SaveBoundInstances` (implied by the save system structure, though not explicitly listed in the map for this partial, the logic resides in `Player`) would serialize them.
    *   When resetting instances, `Player::ResetInstance` checks the `perm` flag to determine if the binding should be cleared immediately or if it requires a hard reset.

## Data Model

The `InstancePlayerBind` struct does not directly touch database tables. It is an in-memory representation. However, the data it holds corresponds to records in the `character` database, specifically related to instance bindings.

While the specific table names are not explicitly queried in the provided source snippet for this struct, standard MaNGOS/WowVMaNGOS implementations store instance bindings in a table often named `character_instance` or similar. The columns typically mapped to this struct are:
*   `guid`: The player's GUID (key).
*   `instanceId`: The unique ID of the instance (linked to the `DungeonPersistentState`).
*   `permanent`: A boolean/tinyint indicating if the bind is permanent (maps to `perm`).
*   `data`: Binary or serialized data representing the `DungeonPersistentState` (maps to the content pointed to by `state`).

*Note: Since no SQL queries are present in the provided source for this specific struct, and no SCHEMA section was provided, the above table description is based on standard emulation architecture patterns. The code itself only manages the in-memory pointers and booleans.*

## Notable Implementation Details

1.  **Pointer Ownership:** The `state` member is a raw pointer (`DungeonPersistentState*`). The `InstancePlayerBind` struct does **not** own this object. The ownership and lifecycle management of the `DungeonPersistentState` are handled elsewhere (likely by the `MapManager` or `InstanceSaveManager`). If the `DungeonPersistentState` is deleted, the `state` pointer in `InstancePlayerBind` becomes dangling. Care must be taken in the `Player` unit to ensure the state is valid before dereferencing.
2.  **Default State:** The constructor explicitly initializes `perm` to `false`. This is critical because C++ does not guarantee initialization of primitive types in structs unless specified. A default `false` ensures that accidental bindings are not treated as permanent raid locks.
3.  **Aggregate Structure:** It is a plain old data (POD) struct with no virtual functions, making it cheap to copy and store in the `std::unordered_map` within `Player`.

## Member Reference

**InstancePlayerBind**
Constructor for the `InstancePlayerBind` struct. Initializes the `state` pointer to `nullptr` and the `perm` boolean to `false`. This ensures that any newly created instance binding record starts with no associated persistent state and is not considered a permanent raid lock.

---

<!-- machine-true, projected from graph.json -->

## Map — InstancePlayerBind

*Source:* Player.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| InstancePlayerBind | ctor | — | — | — |
