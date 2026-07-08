# PlayerCreatorAI

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# PlayerCreatorAI

`PlayerCreatorAI` is a transient subclass of `PlayerBotAI` that automates the initial creation of a bot character. It captures spawn parameters (race, class, location, orientation) at construction and executes the spawn logic exactly once when the bot's network session loads, delegating the actual creation work to `PlayerBotAI::SpawnNewPlayer`.

## Purpose & Responsibilities

1.  **Parameter Storage**: Holds configuration data (`m_race`, `m_class`, `m_mapId`, etc.) required to instantiate a player.
2.  **Session Hook**: Overrides `OnSessionLoaded` to trigger `PlayerBotAI::SpawnNewPlayer` immediately upon session initialization.

The class is designed for single-use execution; it does not implement runtime behavioral hooks like `UpdateAI`.

## Member-by-Member Behavior

### Construction: `PlayerCreatorAI`

Initializes the AI with all necessary data to spawn a specific character.

*   **Parameters**: `pPlayer` (underlying `Player`), `_race_`, `_class_`, `mapId`, `instanceId`, and spatial coordinates `x`, `y`, `z`, `o`.
*   **Behavior**:
    1.  Invokes `PlayerBotAI(pPlayer)` to establish the base hierarchy.
    2.  Assigns all parameters to protected members. No validation is performed; validity is assumed or checked downstream.

### Session Loading: `OnSessionLoaded`

The functional entry point, called by the framework when the bot's `WorldSession` is ready.

*   **Parameters**: `entry` (pointer to `PlayerBotEntry`, unused), `sess` (pointer to `WorldSession`).
*   **Behavior**:
    1.  Calls `PlayerBotAI::SpawnNewPlayer` (inherited from `PlayerBotAI`), passing `sess` and the stored member variables.
    2.  Returns the boolean result of `SpawnNewPlayer`, indicating success or failure.

## Cross-Unit Boundaries

*   **Calls Out**:
    *   **`PlayerBotAI::SpawnNewPlayer`**: Called by `OnSessionLoaded`. Implemented in `PlayerBotAI`, this method handles the complex logic of creating the player object, interacting with the database, and placing the entity in the world. `PlayerCreatorAI` provides only the configuration data.
*   **Called By**:
    *   No external units call `PlayerCreatorAI` members directly according to the MAP. The framework internally invokes `OnSessionLoaded` during the bot session lifecycle.

## Data Model

`PlayerCreatorAI` performs no direct database operations. It relies entirely on `PlayerBotAI::SpawnNewPlayer` to handle persistence. Indirectly, this process involves updating tables such as `characters` to persist the new bot account. The `PlayerBotEntry` passed to `OnSessionLoaded` is not accessed by this class.

## Notable Implementation Details

1.  **Transient Design**: The class lacks `UpdateAI` or other behavioral hooks, confirming its role as a setup utility rather than a runtime controller.
2.  **No Input Validation**: The constructor accepts raw numeric values without checking for valid race/class combinations or map bounds.
3.  **Unused Parameter**: The `entry` parameter in `OnSessionLoaded` is ignored; the spawn logic relies solely on the session object and pre-captured attributes.

## Member Reference

**PlayerCreatorAI**
Constructor that initializes the AI with specific spawn parameters (race, class, map, coordinates, orientation). It stores these values in protected member variables for use during session loading.

**OnSessionLoaded**
Overrides the base class method to trigger the bot's creation. It calls `PlayerBotAI::SpawnNewPlayer` with the stored parameters and returns the success status of the spawn operation. This is the only functional method in this class, serving as the bridge between session initialization and player creation.

---

<!-- machine-true, projected from graph.json -->

## Map — PlayerCreatorAI

*Source:* PlayerBotAI.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| PlayerCreatorAI | ctor | — | — | — |
| OnSessionLoaded | method | — | — | — |
