# InstanceGroupBind

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# InstanceGroupBind

**Purpose & Responsibilities**

`InstanceGroupBind` is a lightweight aggregate struct defined within `Group.h` that represents a group's binding relationship to a specific dungeon instance. In the context of the WoW server emulation (MaNGOS/WowVMaNGOS), when a group enters a raid or dungeon instance, the server must track whether the group is "bound" to that instance. This binding dictates whether members are forced to enter the same instance ID upon re-entering the zone (to prevent players from bypassing bosses or loot by leaving and re-entering a fresh instance).

The struct holds two pieces of state:
1.  **`state`**: A pointer to the `DungeonPersistentState`, which contains the persistent data for that specific instance (boss kills, event progress, etc.).
2.  **`perm`**: A boolean flag indicating if the binding is "permanent." Permanent bindings typically persist until the instance is reset or the group disbands, whereas non-permanent bindings might expire after a short duration or upon leaving the instance.

This struct is not a standalone class with behavior; it is a data container used by the `Group` class (specifically members like `BindToInstance`, `UnbindInstance`, and `GetBoundInstance`) to manage instance affinity. It has no constructor logic beyond default initialization, no destructor, and no methods. It does not interact with the database directly; database persistence for instance binds is handled by the `Group` class or related managers using the data stored within this struct.

## Member-by-Member Behavior

### **InstanceGroupBind** (Constructor)
The default constructor initializes the struct members to safe default values:
*   `state` is set to `nullptr`.
*   `perm` is set to `false`.

This ensures that an uninitialized `InstanceGroupBind` does not point to invalid memory or claim a permanent binding status.

## Cross-Unit Boundaries

*   **Called by:** None. The constructor is implicit and called automatically when `InstanceGroupBind` objects are created, typically within the `Group` class methods (e.g., `Group.BindToInstance`).
*   **Calls out:** None. The struct contains no logic that invokes other units.
*   **Data Dependencies:**
    *   **`DungeonPersistentState`**: The `state` member holds a raw pointer to this class. The `DungeonPersistentState` class (defined elsewhere) manages the actual instance data. The `InstanceGroupBind` relies on the lifetime management of `DungeonPersistentState` being handled by the `Group` class or the instance manager. If the `DungeonPersistentState` is deleted while an `InstanceGroupBind` still points to it, the pointer becomes dangling. The code comments in `Group.h` note: *"permanent InstanceGroupBinds exist iff the leader has a permanent PlayerInstanceBind for the same instance,"* implying a logical coupling between group-level and player-level instance bindings managed by higher-level logic.

## Data Model

This unit does not directly access any database tables. The `InstanceGroupBind` struct is an in-memory representation. Persistence of instance bindings to the database is handled by the `Group` class (via `LoadGroupFromDB` and likely `SaveToDB` methods not shown in this partial) or dedicated instance managers. The struct itself contains no SQL queries or table references.

## Notable Implementation Details

1.  **Raw Pointer Usage**: The `state` member is a raw `DungeonPersistentState*`. There is no smart pointer or reference counting visible in this struct. This places the burden of memory management entirely on the owner (`Group`). Care must be taken to ensure `DungeonPersistentState` objects are not freed while `Group` still holds references via `InstanceGroupBind`.
2.  **Permanent vs. Temporary Binding**: The `perm` flag distinguishes between temporary and permanent bindings. The comment in the source code clarifies the business rule: *"permanent InstanceGroupBinds exist iff the leader has a permanent PlayerInstanceBind for the same instance."* This suggests that the validity of `perm=true` is contingent on the group leader's individual state, a constraint enforced by the calling code in `Group`, not by the struct itself.
3.  **No Encapsulation**: As a struct, all members are public by default. This allows direct access to `state` and `perm` from any code that has access to the `InstanceGroupBind` object, facilitating simple data transfer but offering no protection against accidental modification.

## Member Reference

**InstanceGroupBind**
Default constructor for the `InstanceGroupBind` struct. Initializes `state` to `nullptr` and `perm` to `false`. Ensures that newly created bind objects are in a safe, empty state before being populated by the `Group` class.

---

<!-- machine-true, projected from graph.json -->

## Map — InstanceGroupBind

*Source:* Group.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| InstanceGroupBind | ctor | — | — | — |
