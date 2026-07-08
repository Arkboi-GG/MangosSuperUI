# OutdoorPvP

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# OutdoorPvP

**Purpose & Responsibilities**

`OutdoorPvP` is the abstract base class for managing large-scale, zone-based Player vs. Player (PvP) scenarios in World of Warcraft, such as Warsong Gulch (`OUTDOOR_PVP_HP`) or Eye of the Storm (`OUTDOOR_PVP_EP`). It inherits from `ZoneScript`, extending its capabilities to manage multiple dynamic capture points, track player participation in specific objectives, and handle the complex state transitions inherent in outdoor battlegrounds.

The primary responsibilities of `OutdoorPvP` include:
1.  **Objective Management:** Maintaining a registry of `OPvPCapturePoint` objects, which represent individual flags, towers, or resources within the zone.
2.  **Event Routing:** Intercepting high-level events like kills, spell casts, and game object interactions to determine if they affect the PvP scenario's state.
3.  **State Synchronization:** Broadcasting world state updates to clients to reflect changes in objective control, timers, and scores.

This unit acts as the central coordinator for a specific outdoor PvP instance. Concrete implementations (e.g., `OutdoorPvPEP`) inherit from this class to define specific rules for capturing points, awarding bonuses, and spawning NPCs.

## Member-by-Member Behavior

### Initialization and Lifecycle

**`FillInitialWorldStates`**
Overrides `ZoneScript::FillInitialWorldStates`. Implemented as a stub returning `0`. Derived classes override this to populate the initial world state packet sent to players upon entering the zone, setting up UI elements like timers and scoreboards.

**`SetupZoneScript`**
Overrides `ZoneScript::SetupZoneScript`. Returns `true` by default. Derived classes override this to perform one-time setup tasks, such as registering specific zone IDs or initializing internal data structures.

**`GetTypeId`**
Returns the `m_TypeId` member, which identifies the specific type of outdoor PvP scenario (e.g., `OUTDOOR_PVP_EP`). This allows the engine to distinguish between different battleground types.

### Event Handling

**`HandleKillImpl`**
A protected virtual hook called by `HandleKill`. The base implementation is empty. Derived classes override this to implement specific logic triggered when a player or creature dies within the PvP zone, such as updating capture point progress or awarding honor.

**`AwardKillBonus`**
Overrides `ZoneScript::AwardKillBonus`. The base implementation is empty. Derived classes override this to distribute rewards (honor, marks, etc.) to the killer and their team after a successful kill.

### Player Movement and Participation

**`OnPlayerEnter`**
Overrides `ZoneScript::OnPlayerEnter`. Called when a player enters the zone. It adds the player to internal tracking lists and notifies active capture points, potentially making them eligible for capture credit.

**`OnPlayerLeave`**
Overrides `ZoneScript::OnPlayerLeave`. Called when a player leaves the zone. It removes the player from tracking lists and notifies capture points, ensuring departing players no longer contribute to capture progress.

### Capture Point Management

**`AddCapturePoint`**
Adds an `OPvPCapturePoint` object to the internal `m_capturePoints` map. Called during the setup phase of a derived class to register all objectives for the zone. The key is the capture point's GUID.

**`GetCapturePoint`**
Retrieves an `OPvPCapturePoint` object by its low GUID. Used by other parts of the system, such as `ZoneScript::OnGameObjectRemove`, to access the specific objective associated with a game object.

**`OnCreatureCreate`**
Overrides `ZoneScript::OnCreatureCreate`. The base implementation is empty. Derived classes may override this to track specific NPCs spawned within the PvP zone, such as flag bearers or resource guards.

**`OnGameObjectRemove`**
Overrides `ZoneScript::OnGameObjectRemove`. Called when a game object is removed from the world. It uses `GetCapturePoint` to find the associated capture point and cleans up references or handles the removal of the objective's visual representation.

### State Updates and Cleanup

**`Update`**
Overrides `ZoneScript::Update`. Called periodically by the game loop. It iterates through all registered capture points and calls their `Update` methods, allowing them to process capture progress and check for state changes. It sets `m_objective_changed` if any objective's state has changed, signaling that a global world state update may be needed.

**`SendRemoveWorldStates`**
Overrides `ZoneScript::SendRemoveWorldStates`. The base implementation is empty. Derived classes may override this to remove specific world state UI elements when a player leaves the zone or the session ends.

## Cross-Unit Boundaries

*   **`ZoneScript`**: `OutdoorPvP` inherits from `ZoneScript`, reusing its infrastructure for player tracking, zone registration, and basic event handling. `OutdoorPvP` overrides many of `ZoneScript`'s virtual methods to provide PvP-specific behavior.
*   **`OPvPCapturePoint`**: `OutdoorPvP` manages a collection of `OPvPCapturePoint` objects. It adds them to its map via `AddCapturePoint`, retrieves them via `GetCapturePoint`, and updates them via `Update`. `OPvPCapturePoint` objects call back into `OutdoorPvP` to report state changes.
*   **`ZoneScriptMgr`**: The manager class that instantiates and oversees `OutdoorPvP` instances. It calls `SetupZoneScript` and `Update` on the `OutdoorPvP` object.
*   **`OutdoorPvPEP`**: An example of a derived class. It calls `OutdoorPvP::AddCapturePoint` during its `SetupZoneScript` to register its specific capture points.

## Data Model

This unit does not directly interact with database tables. It operates entirely on in-memory data structures representing the current state of the PvP zone, including player positions, capture point progress, and spawned entities. Configuration for these zones is typically loaded from static data or database tables by derived classes or `ZoneScriptMgr`, but `OutdoorPvP` itself performs no database queries.

## Notable Implementation Details

*   **Virtual Hook Pattern**: Many methods in `OutdoorPvP` are virtual hooks with empty base implementations (e.g., `HandleKillImpl`, `AwardKillBonus`). This design allows derived classes to inject specific logic without modifying the base class, promoting extensibility for different PvP scenarios.
*   **Capture Point Registry**: The `m_capturePoints` map is central to the class's functionality. It associates capture point GUIDs with their corresponding `OPvPCapturePoint` objects, enabling efficient lookup and management.
*   **State Change Flag**: The `m_objective_changed` boolean is set during the `Update` loop if any capture point's state changes. This allows the system to batch world state updates, improving performance by avoiding unnecessary network traffic.

## Member Reference

**`FillInitialWorldStates`**: Overrides `ZoneScript::FillInitialWorldStates`. Stub returning `0`. Used by derived classes to initialize world state packets for clients.

**`SetupZoneScript`**: Overrides `ZoneScript::SetupZoneScript`. Returns `true`. Derived classes override to perform initialization.

**`OnCreatureCreate`**: Overrides `ZoneScript::OnCreatureCreate`. Empty stub. Derived classes may override to track specific NPCs.

**`HandleKillImpl`**: Protected virtual hook called by `HandleKill`. Empty in base. Derived classes implement specific kill logic.

**`AwardKillBonus`**: Overrides `ZoneScript::AwardKillBonus`. Empty stub. Derived classes implement reward distribution.

**`GetTypeId`**: Returns `m_TypeId`, identifying the PvP scenario type.

**`SendRemoveWorldStates`**: Overrides `ZoneScript::SendRemoveWorldStates`. Empty stub. Derived classes may override to clean up UI.

**`AddCapturePoint`**: Adds an `OPvPCapturePoint` to `m_capturePoints` map. Called by derived classes during setup.

**`GetCapturePoint`**: Retrieves an `OPvPCapturePoint` by low GUID from `m_capturePoints` map. Used by `ZoneScript::OnGameObjectRemove` and other components.

---

<!-- machine-true, projected from graph.json -->

## Map — OutdoorPvP

*Source:* ZoneScript.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| FillInitialWorldStates | method | — | — | — |
| SetupZoneScript | method | — | — | — |
| OnCreatureCreate | method | — | — | — |
| HandleKillImpl | method | — | ZoneScript/HandleKill | — |
| AwardKillBonus | method | — | — | — |
| GetTypeId | method | — | — | — |
| SendRemoveWorldStates | method | — | — | — |
| AddCapturePoint | method | — | OutdoorPvPEP/SetupZoneScript | — |
| GetCapturePoint | method | — | ZoneScript/OnGameObjectRemove | — |
