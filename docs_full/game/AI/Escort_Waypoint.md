# Escort_Waypoint

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`Escort_Waypoint` is a lightweight data structure (POD-like struct) defined in `ScriptedEscortAI.h` that represents a single destination point within an NPC escort path. It encapsulates the spatial coordinates (`x`, `y`, `z`) and a unique identifier (`id`) for a waypoint, along with a configuration value (`WaitTimeMs`) specifying how long the NPC should pause at that location before proceeding to the next point.

This struct serves as the fundamental building block for the `std::vector<Escort_Waypoint>` stored within the `npc_escortAI` class. It does not contain any logic or methods beyond its constructor; its sole responsibility is to hold state data required by the escort movement system to navigate creatures through predefined paths.

## Member-by-Member Behavior

### Construction

**`Escort_Waypoint`**
The constructor initializes all five member variables of the struct. It takes four arguments:
1.  `_id`: A `uint32` identifier for the waypoint.
2.  `_x`, `_y`, `_z`: `float` values representing the world coordinates.
3.  `_w`: A `uint32` value representing the wait time in milliseconds.

The constructor assigns these values directly to the corresponding public member variables (`id`, `x`, `y`, `z`, `WaitTimeMs`). There is no validation, normalization, or side-effect logic within the constructor.

## Cross-Unit Boundaries

`Escort_Waypoint` is a passive data container and does not initiate calls to other units. Its lifecycle is tightly coupled with the `npc_escortAI` class (defined in the same header, implemented in `ScriptedEscortAI.cpp`).

*   **Called by:** `ScriptedEscortAI::FillPointMovementListForCreature`
    The private method `FillPointMovementListForCreature` within the `npc_escortAI` class (part of the `ScriptedEscortAI` unit) instantiates `Escort_Waypoint` objects. This method typically iterates over database records or hardcoded lists to populate the `WaypointList` vector. Each `Escort_Waypoint` created here represents a single step in the NPC's journey. The direction of data flow is from the AI logic/database into the `Escort_Waypoint` instance, which is then stored in the AI's internal state.

## Data Model

`Escort_Waypoint` itself does not interact with the database. However, the data it holds corresponds to rows in the `creature_addon` or custom escort waypoint tables (such as `escort_waypoint` or similar, depending on the specific server implementation's schema for escort scripts). The struct maps directly to the logical concept of a "waypoint record":
*   `id`: Corresponds to the waypoint sequence number or ID.
*   `x`, `y`, `z`: Corresponds to the coordinate columns.
*   `WaitTimeMs`: Corresponds to a delay or pause duration column.

Since `Escort_Waypoint` is a C++ struct and not a database accessor, it does not execute SQL queries. It merely holds the values retrieved by other parts of the `ScriptedEscortAI` unit.

## Notable Implementation Details

*   **Public Members:** All member variables (`id`, `x`, `y`, `z`, `WaitTimeMs`) are public. This allows direct access and modification by the `npc_escortAI` class without needing getter/setter methods, reducing overhead for this simple data carrier.
*   **No Virtual Functions:** The struct has no virtual table, making it cheap to copy and store in vectors.
*   **Wait Time Semantics:** The `WaitTimeMs` field is crucial for pacing the escort. If set to 0, the NPC proceeds immediately to the next waypoint upon arrival. If non-zero, the NPC pauses for that duration. This value is interpreted by the `UpdateAI` loop in `npc_escortAI`.
*   **Coordinate System:** The `x`, `y`, `z` values are expected to be in the game world's coordinate system (likely meters, consistent with World of Warcraft's engine). The struct does not perform any conversion.

## Member Reference

**Escort_Waypoint**
Constructor for the `Escort_Waypoint` struct. Initializes the `id`, `x`, `y`, `z`, and `WaitTimeMs` member variables with the provided arguments. It is called exclusively by `ScriptedEscortAI::FillPointMovementListForCreature` to populate the escort path data.

---

<!-- machine-true, projected from graph.json -->

## Map — Escort_Waypoint

*Source:* ScriptedEscortAI.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| Escort_Waypoint | ctor | — | ScriptedEscortAI/FillPointMovementListForCreature | — |
