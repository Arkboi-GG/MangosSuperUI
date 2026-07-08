# TemporarySummonWaypoint

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`TemporarySummonWaypoint` is a specialized subclass of `TemporarySummon` (and by extension `Creature`) designed to represent a creature summoned specifically to follow a predefined waypoint path. It exists within the `wowvmangos` server framework to encapsulate the metadata required for waypoint-based movement: the specific waypoint identifier, the path identifier, and the origin point of the path.

Unlike the base `TemporarySummon` class, which manages the lifecycle (summoning, despawning timers, summoner tracking) of temporary entities, `TemporarySummonWaypoint` adds no behavioral logic or state management. Its sole responsibility is to store and provide read-only access to three configuration integers that define *which* path the creature should follow. It acts as a data carrier for waypoint-specific summoning contexts, primarily utilized by administrative chat commands to inspect or modify creature paths.

## Member-by-Member Behavior

The unit contains three accessor methods. All are inline, constant-time lookups returning private member variables. They do not perform validation, calculation, or side effects.

### **GetWaypointId**
Returns the `uint32` value stored in `m_waypoint_id`. This identifier typically corresponds to a specific step or node within a larger waypoint sequence. It is used by external tools to identify which specific waypoint a creature is associated with or targeting.

### **GetPathId**
Returns the `int32` value stored in `m_path_id`. This identifies the overall path definition (e.g., a patrol route) assigned to the creature. The use of a signed integer suggests that negative values might hold special meaning (such as indicating an invalid or default path), though the class itself does not enforce or interpret this semantics.

### **GetPathOrigin**
Returns the `uint32` value stored in `m_pathOrigin`. This likely represents the starting position index, a hash of the starting coordinates, or a reference ID for the path's origin point. Like the other members, it is a simple data retrieval operation.

## Cross-Unit Boundaries

`TemporarySummonWaypoint` is a passive data structure; it does not initiate calls to other units. However, it is heavily depended upon by the `ChatHandler.CreatureCommands` unit for administrative debugging and path editing features.

### Called By: `ChatHandler.CreatureCommands`

The `ChatHandler.CreatureCommands` unit (specifically the handlers for `HandleWpAddCommand`, `HandleWpModifyCommand`, `HandleWpShowCommand`, and `HandleWpExportCommand`) casts or accesses `TemporarySummonWaypoint` instances to retrieve path metadata.

*   **Direction:** Data flows **from** `TemporarySummonWaypoint` **to** `ChatHandler.CreatureCommands`.
*   **Collaboration:** When a Game Master uses a command to view, add, or modify waypoints for a creature, the handler needs to know the current path context. It queries `GetPathId()`, `GetWaypointId()`, and `GetPathOrigin()` to display current settings or to validate inputs against the existing path structure.
*   **Why:** This separation allows the chat handler to remain decoupled from the internal storage layout of the creature object. It treats the creature as an opaque entity that exposes its waypoint configuration through these three specific getters.

## Data Model

This unit does not interact directly with any database tables. It holds transient runtime state (`m_waypoint_id`, `m_path_id`, `m_pathOrigin`) that is initialized at construction time. While these values likely correspond to rows in database tables such as `creature_addon` or custom waypoint tables (e.g., `waypoint_data`), `TemporarySummonWaypoint` itself performs no SQL queries, inserts, or updates. Any persistence of this data is handled by higher-level services or the base `Creature`/`TemporarySummon` classes during save operations, not by this partial.

## Notable Implementation Details

1.  **No Constructor Logic:** The constructor `TemporarySummonWaypoint(ObjectGuid summoner, uint32 waypoint_id, int32 path_id, uint32 pathOrigin)` simply initializes the private members. It does not validate whether the `path_id` or `waypoint_id` exist in the game world or database. Invalid IDs can be stored without error, potentially leading to undefined behavior if the creature attempts to move using these invalid references later.
2.  **Inheritance Chain:** As a subclass of `TemporarySummon`, it inherits all lifecycle management (timers, despawn logic, summoner tracking). This means a `TemporarySummonWaypoint` will still despawn after its lifetime expires or if its summoner dies, regardless of its waypoint status.
3.  **Const-Correctness:** All getters are marked `const`, ensuring that querying the path information does not alter the creature's state. This is critical for safe usage in read-only contexts like debug commands.
4.  **Signed vs Unsigned Path ID:** `m_path_id` is `int32`, while `m_waypoint_id` and `m_pathOrigin` are `uint32`. This distinction is significant: callers must be aware that `GetPathId()` can return negative values, whereas the other two cannot. This often implies that `-1` or `0` might be used as sentinel values for "no path" or "default path."

## Member Reference

**GetWaypointId**  
Inline method returning the `uint32` waypoint identifier stored in `m_waypoint_id`. Used by `ChatHandler.CreatureCommands` to identify the specific waypoint node associated with the creature.

**GetPathId**  
Inline method returning the `int32` path identifier stored in `m_path_id`. Used by `ChatHandler.CreatureCommands` to determine the overall path route assigned to the creature. Note the signed return type, allowing for potential sentinel values.

**GetPathOrigin**  
Inline method returning the `uint32` path origin value stored in `m_pathOrigin`. Used by `ChatHandler.CreatureCommands` to retrieve the starting reference or index for the path.

---

<!-- machine-true, projected from graph.json -->

## Map — TemporarySummonWaypoint

*Source:* TemporarySummon.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GetWaypointId | method | — | ChatHandler.CreatureCommands/HandleWpAddCommand, ChatHandler.CreatureCommands/HandleWpModifyCommand, ChatHandler.CreatureCommands/HandleWpShowCommand | — |
| GetPathId | method | — | ChatHandler.CreatureCommands/HandleWpAddCommand, ChatHandler.CreatureCommands/HandleWpExportCommand, ChatHandler.CreatureCommands/HandleWpModifyCommand, ChatHandler.CreatureCommands/HandleWpShowCommand | — |
| GetPathOrigin | method | — | ChatHandler.CreatureCommands/HandleWpAddCommand, ChatHandler.CreatureCommands/HandleWpExportCommand, ChatHandler.CreatureCommands/HandleWpModifyCommand, ChatHandler.CreatureCommands/HandleWpShowCommand | — |
