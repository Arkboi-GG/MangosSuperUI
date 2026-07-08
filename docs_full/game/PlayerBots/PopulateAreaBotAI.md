# PopulateAreaBotAI

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# PopulateAreaBotAI

**PopulateAreaBotAI** is a specialized `PlayerBotAI` subclass designed to manage the initial placement and login behavior of player bots intended to populate a specific geographic area within the game world. It inherits from `PlayerBotAI`, which itself extends `PlayerAI`, providing the base infrastructure for non-player-controlled characters (bots) that mimic player behavior.

The primary responsibility of `PopulateAreaBotAI` is to ensure that when a bot associated with this AI is added to the game world or logs in, it is positioned correctly within a defined radius around a central coordinate (`_x`, `_y`, `_z`) on a specific map (`_map`), adhering to a specified faction (`_team`). This class does not contain complex movement or combat logic; rather, it acts as a configuration holder and lifecycle hook manager for static or semi-static population bots.

## Member-by-Member Behavior

### Constructor: `PopulateAreaBotAI`
The constructor initializes the bot's spatial and factional constraints. It accepts the target map ID, center coordinates (`x`, `y`, `z`), the faction team ID, and a radius defining the area of influence. It stores these values in protected member variables (`_map`, `_x`, `_y`, `_z`, `_radius`, `_team`) and passes the optional `Player*` pointer to the base `PlayerBotAI` constructor. This setup allows the bot to be pre-configured with its destination before it is fully instantiated in the game world.

### Lifecycle Hook: `BeforeAddToMap`
This method overrides `PlayerBotAI::BeforeAddToMap`. According to the comment in the header, `me` (the `Player` object associated with this AI) is `nullptr` at the time of this call. This suggests that `BeforeAddToMap` is invoked during the early stages of the player object's creation or loading process, likely before the player entity is fully linked to its AI or before it is inserted into the map data structures. In the context of `PopulateAreaBotAI`, this hook is likely used to set initial positioning flags or prepare the player object for correct placement within the defined radius, although the specific implementation details are not visible in the provided header. The key constraint is that it operates on a `Player` object that is not yet fully active or mapped.

### Lifecycle Hook: `OnPlayerLogin`
This method overrides `PlayerBotAI::OnPlayerLogin`. It is triggered when the bot character successfully logs into the game world. Similar to `BeforeAddToMap`, this is a critical point for ensuring the bot appears in the correct location. While the header does not show the implementation, the presence of `_radius` and `_map` suggests that this method likely calculates a final spawn position within the defined area and ensures the bot is placed there upon login. It serves as the final check to guarantee the bot is visible and active within the intended populated zone.

## Cross-Unit Boundaries

*   **Called by `PlayerBotAI/CreatePlayerBotAI`**: The `PopulateAreaBotAI` constructor is invoked by the factory function `CreatePlayerBotAI` (defined in `PlayerBotAI.cpp`, though not shown in the source snippet, it is referenced in the MAP). This indicates that `PopulateAreaBotAI` is not typically instantiated directly by user code but is created dynamically by the bot management system when a bot is configured to use this specific AI type. The factory pattern allows the system to select the appropriate AI subclass based on configuration data.

## Data Model

This unit does not interact directly with any database tables. It relies entirely on runtime configuration passed through its constructor and inherited from `PlayerBotAI`. Any persistent data regarding bot configurations would be managed by higher-level systems that call `CreatePlayerBotAI` or by the `PlayerBotEntry` structure handled by the base `PlayerBotAI` class.

## Notable Implementation Details

*   **Null Player Pointer in `BeforeAddToMap`**: The comment `// me=nullptr at call` in the `BeforeAddToMap` declaration is significant. It implies that this method cannot rely on accessing the `Player` object's current state or methods that require a valid `this` pointer to the player entity. Any logic here must be careful to avoid dereferencing null pointers or assuming the player is already part of the game world.
*   **Protected Configuration Members**: The spatial and factional parameters (`_map`, `_x`, `_y`, `_z`, `_radius`, `_team`) are protected, allowing derived classes to access them if further specialization is needed, while keeping them encapsulated from external direct modification.
*   **Inheritance Chain**: As a subclass of `PlayerBotAI`, `PopulateAreaBotAI` inherits all the session management, packet handling, and update loop infrastructure provided by the base class. It focuses solely on the specific requirements of area population, delegating general bot behavior to its parent.

## Member Reference

**PopulateAreaBotAI**
Constructor that initializes the bot's target map, center coordinates, radius, and faction team. It stores these configuration values in protected member variables and delegates initialization of the base `PlayerBotAI` class. It is called by the `CreatePlayerBotAI` factory function in `PlayerBotAI.cpp`.

---

<!-- machine-true, projected from graph.json -->

## Map — PopulateAreaBotAI

*Source:* PlayerBotAI.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| PopulateAreaBotAI | ctor | — | PlayerBotAI/CreatePlayerBotAI | — |
