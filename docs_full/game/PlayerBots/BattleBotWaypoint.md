# BattleBotWaypoint

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# BattleBotWaypoint

## Purpose & Responsibilities

`BattleBotWaypoint` is a lightweight data structure within the `wowvmangos` codebase that defines a single step in a predefined movement path for "Battle Bots." In the context of this server emulation, Battle Bots are likely automated entities (such as NPCs or test characters) that follow scripted routes. This specific unit provides the fundamental building block for those routes: a spatial coordinate (`x`, `y`, `z`) paired with an optional callback function (`pFunc`).

The header file `BattleBotWaypoints.h` serves two primary roles:
1.  **Data Definition:** It defines the `BattleBotWaypoint` struct and the `BattleBotPath` type alias (a vector of waypoints), establishing the format for path data.
2.  **Static Configuration:** It declares several `extern` vectors and constant `Position` objects that hold hardcoded coordinates for specific game features, including World Slayer (WS), Arena Battle (AB), and Alterac Valley (AV) waiting positions, flag positions, and graveyard jump paths. These constants are referenced by other parts of the system to place entities at precise locations during these specific game modes.

This unit contains no executable logic itself; it is purely a declaration of data structures and external references. The actual behavior associated with the waypoints (movement execution, callback invocation) resides in other units, specifically those implementing `BattleBotAI`.

## Member-by-Member Behavior

### `BattleBotWaypoint` (Constructor)
The constructor initializes a waypoint with specific spatial coordinates and an optional function pointer.
*   **Parameters:**
    *   `x_`, `y_`, `z_`: Floating-point values representing the target position in the 3D world space.
    *   `func`: A pointer to a function of type `BattleBotWaypointFunc`. This function takes a `BattleBotAI*` as an argument. If `nullptr`, no action is triggered upon reaching this waypoint.
*   **Behavior:** It assigns the provided coordinates to the member variables `x`, `y`, and `z`. It assigns the function pointer to `pFunc`. This allows the path definition to include "events" or "actions" that occur when the bot reaches a specific point in its journey.

## Cross-Unit Boundaries

While `BattleBotWaypoint` itself has no outgoing calls, its design is deeply integrated with other units via its members and the external declarations in this header.

*   **`BattleBotAI`**: The `BattleBotWaypointFunc` typedef explicitly references `BattleBotAI`. This indicates that the callbacks defined in waypoints are intended to be executed by the AI controller of the Battle Bot. The `BattleBotAI` unit (likely defined in `BattleBotAI.cpp` or similar) is responsible for interpreting these waypoints, moving the entity to the `(x, y, z)` coordinates, and invoking `pFunc` if it is not null.
*   **`SharedDefines.h`**: Included to provide common definitions, likely including the `Position` struct used in the static constants.
*   **External Consumers**: The `extern` declarations (`vAllianceGraveyardJumpPath`, `vHordeGraveyardJumpPath`, `vPaths_WS`, etc.) imply that other units populate these vectors at runtime or load time. Other units also read these constants (e.g., `WS_WAITING_POS_HORDE_1`) to position players or NPCs. The documentation for those consuming units would detail how they use these static positions.

## Data Model

This unit does not interact directly with any database tables. All data is held in memory via C++ structs, vectors, and constant objects. The paths and positions are either hardcoded in this header (as `const Position` objects) or populated into `extern` vectors by other parts of the codebase. There are no SQL queries or table references in this source file.

## Notable Implementation Details

1.  **Function Pointer Callbacks**: The use of `BattleBotWaypointFunc` allows for dynamic behavior at specific points in a path. This is a common pattern in game development for triggering events (e.g., spawning an object, playing a sound, changing state) without hardcoding the logic into the path parser. The caller (`BattleBotAI`) must ensure the `BattleBotAI*` passed to the callback is valid.
2.  **Hardcoded Coordinates**: A significant portion of this header consists of hardcoded `Position` constants for specific battlegrounds (World Slayer, Arena Battle, Alterac Valley) and graveyard jumps. This suggests these locations are fixed and rarely change, making them suitable for compile-time constants. However, it also means any changes to these locations require a code recompile.
3.  **Memory Management of Paths**: The `extern` declarations for `vPaths_WS`, `vPaths_AB`, and `vPaths_AV` are vectors of pointers to `BattleBotPath` (`std::vector<BattleBotPath*>`). This implies that the ownership and lifetime management of these `BattleBotPath` objects are handled elsewhere. Care must be taken to ensure these pointers remain valid throughout the server's runtime.
4.  **No Default Constructor**: `BattleBotWaypoint` only has a parameterized constructor. This ensures that every waypoint is initialized with explicit coordinates and a function pointer, preventing accidental creation of invalid waypoints with default zeroed values unless intentionally done so.

## Member Reference

**BattleBotWaypoint**
Constructor for the `BattleBotWaypoint` struct. Initializes the waypoint's `x`, `y`, `z` coordinates and the optional `pFunc` callback pointer. Takes four arguments: three floats for position and one function pointer.

---

<!-- machine-true, projected from graph.json -->

## Map — BattleBotWaypoint

*Source:* BattleBotWaypoints.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| BattleBotWaypoint | ctor | — | — | — |
