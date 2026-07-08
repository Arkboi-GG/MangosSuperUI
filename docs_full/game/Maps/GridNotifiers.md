<!-- provenance: failed-members -->
# GridNotifiers

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# GridNotifiers

**Purpose & Responsibilities**

`GridNotifiers` provides the visitor-pattern infrastructure and predicate logic required to traverse, filter, and act upon `WorldObject`s within the spatial grid system of the MaNGOS server. It decouples the iteration over grid cells (handled by the grid loader) from the specific actions performed on objects found within those cells.

The unit serves three primary functions:
1.  **Visibility Management:** Classes like `VisibleNotifier` and `VisibleChangesNotifier` handle the complex logic of determining which objects a player can see, updating the client's view, and managing "out-of-range" removals. This includes special handling for objects on transports.
2.  **Message Delivery:** Classes such as `MessageDeliverer`, `MessageDistDeliverer`, and `ObjectMessageDeliverer` facilitate sending network packets (`WorldPacket`) to players within specific ranges or conditions relative to a source object or player.
3.  **Object Search & Action:** A large suite of template-based "Searchers" (e.g., `CreatureSearcher`, `UnitListSearcher`) and "Workers" (e.g., `CreatureWorker`, `WorldObjectWorker`) allows callers to find objects matching specific criteria or perform actions on all objects in a grid area. These are paired with "Check" classes (predicates) like `NearestHostileUnitCheck` or `AnyFriendlyUnitInObjectRangeCheck` that define the filtering logic.

This unit does not manage the grid data structures themselves but provides the tools to interact with them efficiently.

## Member-by-Member Behavior

### Visibility and Notification

**`VisibleChangesNotifier`**
This notifier is used when an object's visibility state changes (e.g., it moves or becomes visible/invisible). Its `Visit` method iterates over cameras in the grid and calls `UpdateVisibilityOf` on the source object for each camera, ensuring that observers are notified of the change.

**`VisibleNotifier`**
This is the core component for updating a player's view of the world.
*   **Constructor:** Initializes with a `Camera` reference and copies the player's current visible GUIDs into `i_clientGUIDs`.
*   **`Notify`:** This method performs the heavy lifting of visibility updates.
    1.  **Transport Handling:** If the player is on a `GenericTransport`, it checks passengers. If a passenger was previously tracked in `i_clientGUIDs` but is now considered part of the transport's local scope, it removes it from the "out-of-range" list and triggers specific visibility updates for that passenger type (GameObject, Player, Creature, DynamicObject).
    2.  **Active Object Update:** It calls `Map::UpdateActiveObjectVisibility` to reconcile the list of visible objects with the map's active object set. This modifies `i_clientGUIDs` to remove objects that are still within range but handled differently by the active object system.
    3.  **Out-of-Range Generation:** Remaining GUIDs in `i_clientGUIDs` are considered truly out of range. It adds them to the `UpdateData` buffer.
    4.  **Cleanup:** It iterates through the out-of-range GUIDs. If a GUID is a Player, it removes the current player as a listener from that target player's `PlayerBroadcaster`. It then erases the GUID from the player's `m_visibleGUIDs` set.
    5.  **Network Send:** If there is data to send, it transmits the update packet to the player's session. It also handles reciprocal visibility updates for other players who might need to know that this player has gone out of range.

### Message Delivery

These classes deliver `WorldPacket`s to players whose cameras are in the visited grid cells.

*   **`MessageDeliverer`**: Sends a message to all players in the grid, optionally including the sender (`i_toSelf`).
*   **`MessageDelivererExcept`**: Sends a message to all players except a specific skipped receiver.
*   **`ObjectMessageDeliverer`**: Sends a message to all players in the grid, regardless of self-status, typically used for object-centric broadcasts.
*   **`MessageDistDeliverer`**: Sends a message only if the player is within a specified distance (`i_dist`) from the source player (`i_player`). It also supports team-only restrictions (`i_ownTeamOnly`).
*   **`ObjectMessageDistDeliverer`**: Similar to above, but checks distance from a generic `WorldObject` (`i_object`) rather than a player.

### Object Updates

**`ObjectUpdater`**
Iterates over objects in a grid cell (specifically `GameObject` and `DynamicObject` as instantiated in the cpp) and calls their `UpdateHelper::UpdateRealTime` method with the provided time difference. This keeps dynamic objects and game objects synchronized with the server tick.

### Predicates (Checks)

These classes implement `operator()` to determine if a specific object meets certain criteria. They are used by Searchers.

*   **`CannibalizeObjectCheck`**: Determines if a corpse or creature is a valid target for cannibalization. Checks for friendliness, distance, and specific creature types (humanoid/undead).
*   **`GameObjectFocusCheck`**: Checks if a GameObject is a spell focus of a specific ID and within range.
*   **`NearestGameObjectFishingHoleCheck`**: Finds fishing holes within range. Updates the internal range to the distance of the found hole to ensure the *nearest* one is selected when used with a "Last" searcher.
*   **`NearestGameObjectEntryInObjectRangeCheck`**: Finds GameObjects of a specific entry within range, updating the range limit to ensure the nearest match is found.
*   **`NearestGameObjectEntryFitConditionInObjectRangeCheck`**: Same as above, but also verifies that a specific condition ID is satisfied.
*   **`GameObjectEntryInPosRangeCheck`**: Checks if a GameObject of a specific entry is within range of a specific XYZ coordinate.
*   **`AnyClosedDoorInRangeCheck`**: Checks for closed doors within range.
*   **`MostHPMissingInRangeCheck`**: Finds friendly units in combat with high health loss (absolute or percentage).
*   **`FriendlyCCedInRangeCheck`**: Finds friendly units in combat that are crowd-controlled (charmed, frozen, etc.).
*   **`FriendlyMissingBuffInRangeCheck`**: Finds friendly units in combat missing a specific buff.
*   **`AnyUnfriendlyUnitInObjectRangeCheck`**: Finds units that are not friendly to a reference unit.
*   **`AnyHostileUnitInObjectRangeCheck`**: Finds units that are hostile to a reference unit.
*   **`AnyFriendlyUnitInObjectRangeCheck`**: Finds friendly units within range.
*   **`AnySameFactionUnitInObjectRangeCheck`**: Finds units of the same faction template.
*   **`AnyCreatureGroupMembersInObjectRangeCheck`**: Finds creatures belonging to the same group as the reference creature.
*   **`AnyUnitInObjectRangeCheck`**: Simple range check for any alive unit.
*   **`NearestAttackableUnitInObjectRangeCheck`**: Finds the nearest attackable hostile unit, updating the range to ensure the closest one is picked.
*   **`AnyAoEVisibleTargetUnitInObjectRangeCheck`**: Finds valid AoE targets, checking visibility and PvP rules.
*   **`AnyAoETargetUnitInObjectRangeCheck`**: Similar to above but without strict visibility checks for non-unit casters.
*   **`CallOfHelpCreatureInRangeDo`**: A "Do" class that triggers AI attacks or flee behaviors on friendly creatures when a call for help is issued.
*   **`AnyStealthedCheck`**: Checks if a unit is stealthed.
*   **`AnyAssistCreatureInRangeCheck`**: Checks if a creature can assist a friendly unit against an enemy.
*   **`NearestAssistCreatureInCreatureRangeCheck`**: Finds the nearest assist-capable creature.
*   **`NearestFriendlyGuardInRangeCheck`**: Finds the nearest friendly guard not in combat.
*   **`NearestInteractableNpcWithFlag`**: Finds NPCs with specific flags that the player can interact with.
*   **`NearestCreatureEntryWithLiveStateInObjectRangeCheck`**: Finds creatures of a specific entry and life state (alive/corpse).
*   **`NearestUnitFitConditionInCombatRangeCheck`**: Finds units of a specific entry fitting a condition within combat range.
*   **`AnyPlayerInObjectRangeCheck`**: Checks for players within range.
*   **`NearestAlivePlayerCheck`**: Finds the nearest alive player (excluding GMs).
*   **`PlayerAtMinimumRangeAway`**: Checks if a player is *outside* a minimum range.
*   **`AllGameObjectsWithEntryInRange`**: Checks for all GameObjects of a specific entry.
*   **`AllGameObjectsMatchingOneEntryInRange`**: Checks for GameObjects matching any entry in a vector.
*   **`AllCreaturesOfEntryInRange`**: Checks for creatures of a specific entry.
*   **`AllCreaturesMatchingOneEntryInRange`**: Checks for creatures matching any entry in a vector.
*   **`NearestFriendlyUnitCheck`**: Finds the nearest friendly unit.
*   **`NearestHostileUnitCheck`**: Finds the nearest hostile, attackable unit.
*   **`NearestHostileUnitInAggroRangeCheck`**: Finds hostile units within aggro range, considering LOS and civilian status.
*   **`AllWorldObjectsInRange`**: Checks for any world object within range.

### Actions (Dos)

*   **`RespawnDo`**: Respawns a Creature or GameObject. Crucially, it checks if the object is in a BattleGround and if the associated event is active before respawning.
*   **`LocalizedPacketDo` / `LocalizedPacketListDo`**: These classes prepare localized network packets using a `Builder` pattern. They cache packets per locale to avoid rebuilding them for every player. `LocalizedPacketListDo` manages manual memory cleanup in its destructor.

### Searchers and Workers

These template classes define how to iterate over grid maps.

*   **Searchers** (`WorldObjectSearcher`, `CreatureSearcher`, `UnitSearcher`, etc.): Iterate over a specific type of object map (e.g., `CreatureMapType`). They apply a `Check` predicate. If the check passes, they store the result in `i_object` (for single searchers) or `i_objects` (for list searchers). "Last" searchers (`UnitLastSearcher`) overwrite the result, allowing the caller to get the last (often nearest, if range is updated in the Check) match.
*   **Workers** (`WorldObjectWorker`, `CreatureWorker`, `UnitWorker`, etc.): Iterate over a map and execute a `Do` functor on every object, regardless of checks.
*   **`CameraDistWorker`**: Iterates over cameras, checks if the camera's body is within distance of a searcher, and executes a `Do` on the camera's owner.

## Cross-Unit Boundaries

*   **Camera/UpdateVisibilityOf**: Called by `VisibleChangesNotifier::Visit` to notify cameras of visibility changes.
*   **Camera/GetBody, Camera/GetOwner**: Used by `VisibleNotifier::Notify` and various delivery/check classes to access the physical body and the owning player of a camera.
*   **GenericTransport/GetPassengers**: Called by `VisibleNotifier::Notify` to handle visibility for players on transports.
*   **Log.Main/...**: Used by `VisibleNotifier::Notify` for debug logging of out-of-range events.
*   **Map.Main/GetPlayer, Map.Main/UpdateActiveObjectVisibility#3**: `VisibleNotifier::Notify` uses these to reconcile visibility lists with the map's active object state.
*   **Object/...**: Various `Object` methods (`GetGuidStr`, `ToCreature`, etc.) are used throughout for type casting and identification.
*   **ObjectAccessor/FindPlayer**: Used by `VisibleNotifier::Notify` to find other players for reciprocal visibility updates.
*   **Player.Main/...**: `VisibleNotifier::Notify` and message deliverers heavily rely on `Player` methods to get sessions, cameras, teams, and update visibility.
*   **PlayerBroadcaster/RemoveListener**: Called by `VisibleNotifier::Notify` to clean up broadcast subscriptions when a player goes out of range.
*   **UpdateData/...**: `VisibleNotifier::Notify` uses `UpdateData` to buffer and send visibility changes.
*   **WorldObject.Object/...**: Used for distance checks (`IsWithinDist`, `IsWithinDistInMap`) and map retrieval.
*   **WorldSession.Main/SendPacket**: Called by all message deliverer classes to send packets to clients.
*   **Corpse/IsFriendlyTo**: Used by `CannibalizeObjectCheck`.
*   **BattleGround/..., BattleGroundMap/..., BattleGroundMgr/...**: Used by `RespawnDo::operator()` to ensure creatures/gameobjects only respawn during active BG events.
*   **Creature.Main/Respawn, GameObject/Respawn**: Called by `RespawnDo`.

## Data Model

This unit does not directly interact with database tables. It operates entirely on in-memory object states and grid structures.

## Notable Implementation Details

*   **Transport Visibility Complexity:** `VisibleNotifier::Notify` contains specific logic for `GenericTransport`. Objects on transports are treated specially because their coordinates are relative to the transport, not the map. The code manually iterates passengers to ensure visibility updates are correctly applied and removed from the out-of-range list if they are still relevant due to being on the same transport.
*   **Reciprocal Visibility:** When a player goes out of range of another player, `VisibleNotifier::Notify` explicitly calls `UpdateVisibilityOf` on the *other* player's camera to ensure they also lose sight of the first player. This maintains symmetry in the visibility graph.
*   **Range Updating in Checks:** Many "Nearest" checks (e.g., `NearestHostileUnitCheck`) update their internal `i_range` member to the distance of the found object. This is critical when used with "Last" searchers (like `UnitLastSearcher`), as it ensures that subsequent iterations only consider objects closer than the current best match, effectively finding the nearest one without sorting.
*   **BattleGround Respawn Guard:** `RespawnDo` prevents creatures and gameobjects from respawning in BattleGrounds unless the specific event associated with them is currently active. This prevents visual glitches or gameplay issues where objects respawn prematurely during a BG.
*   **Memory Management in LocalizedPackets:** `LocalizedPacketListDo` uses raw pointers in a vector and manually deletes them in the destructor. This is a notable detail for maintainers, as it requires careful handling to avoid leaks or double-frees if the builder logic changes.
*   **Thread Safety:** `VisibleNotifier::Notify` acquires a `std::unique_lock` on `player.m_visibleGUIDs_lock` while modifying the visible GUIDs set. This indicates that visibility updates may occur from contexts that require synchronization with other threads accessing the player's visibility state.

## Member Reference

**Visit#6** (method): Part of `VisibleChangesNotifier`. Iterates over cameras in the grid and calls `UpdateVisibilityOf` on the source object for each camera.

**Notify** (method): Part of `VisibleNotifier`. Handles the complete visibility update cycle: transport passenger handling, active object reconciliation, out-of-range generation, cleanup of visible GUIDs and broadcaster listeners, and sending update packets to the client and reciprocal players.

**VisibleChangesNotifier** (ctor): Constructs a notifier for a specific `WorldObject` to propagate visibility changes.

**Visit** (method): Part of `MessageDeliverer`. Sends a packet to all players in the grid, optionally including the sender.

**Visit#2** (method): Part of `MessageDelivererExcept`. Sends a packet to all players in the grid except a specific skipped receiver.

**Visit#4** (method): Part of `ObjectMessageDeliverer`. Sends a packet to all players in the grid.

**Visit#3** (method): Part of `MessageDistDeliverer`. Sends a packet to players within a specific distance and team constraints.

**Visit#5** (method): Part of `ObjectMessageDistDeliverer`. Sends a packet to players within a specific distance of a `WorldObject`.

**operator()** (method): Part of `CannibalizeObjectCheck`. Checks if a corpse is a valid cannibalization target based on friendliness and distance.

**operator()#2** (method): Part of `RespawnDo`. Respawns a `Creature` if not in an inactive BattleGround event.

**WorldObjectSearcher<Check>** (ctor): Constructs a searcher for a single `WorldObject` matching a check.

**operator()#3** (method): Part of `RespawnDo`. Respawns a `GameObject` if not in an inactive BattleGround event.

**WorldObjectListSearcher<Check>** (ctor): Constructs a searcher for a list of `WorldObject`s matching a check.

**WorldObjectWorker<Do>** (ctor): Constructs a worker to execute an action on all `WorldObject`s.

**Visit#15** (function): Template instantiation or helper for `ObjectUpdater::Visit<GameObject>`.

**Visit#16** (function): Template instantiation or helper for `ObjectUpdater::Visit<DynamicObject>`.

**Visit#13** (function): Helper for `WorldObjectWorker::Visit<GameObject>`.

**Visit#12** (function): Helper for `WorldObjectWorker::Visit<Player>`.

**Visit#14** (function): Helper for `WorldObjectWorker::Visit<Creature>`.

**GameObjectSearcher<Check>** (ctor): Constructs a searcher for a single `GameObject` matching a check.

**GameObjectLastSearcher<Check>** (ctor): Constructs a searcher for the last `GameObject` matching a check (useful for nearest).

**GameObjectListSearcher<Check>** (ctor): Constructs a searcher for a list of `GameObject`s matching a check.

**UnitSearcher<Check>** (ctor): Constructs a searcher for a single `Unit` matching a check.

**UnitWorker<Do>** (ctor): Constructs a worker to execute an action on all `Unit`s.

**Visit#11** (function): Helper for `UnitWorker::Visit<Player>`.

**Visit#10** (function): Helper for `UnitWorker::Visit<Creature>`.

**UnitLastSearcher<Check>** (ctor): Constructs a searcher for the last `Unit` matching a check.

**UnitListSearcher<Check>** (ctor): Constructs a searcher for a list of `Unit`s matching a check.

**CreatureSearcher<Check>** (ctor): Constructs a searcher for a single `Creature` matching a check.

**CreatureLastSearcher<Check>** (ctor): Constructs a searcher for the last `Creature` matching a check.

**CreatureListSearcher<Check>** (ctor): Constructs a searcher for a list of `Creature`s matching a check.

**CreatureWorker<Do>** (ctor): Constructs a worker to execute an action on all `Creature`s.

**Visit#8** (function): Helper for `CreatureWorker::Visit<Creature>`.

**PlayerSearcher<Check>** (ctor): Constructs a searcher for a single `Player` matching a check.

**PlayerLastSearcher<Check>** (ctor): Constructs a searcher for the last `Player` matching a check.

**PlayerListSearcher<Check>** (ctor): Constructs a searcher for a list of `Player`s matching a check.

**PlayerWorker<Do>** (ctor): Constructs a worker to execute an action on all `Player`s.

**Visit#9** (function): Helper for `PlayerWorker::Visit<Player>`.

**CameraDistWorker<Do>** (ctor): Constructs a worker to execute an action on players whose cameras are within a distance.

**Visit#7** (function): Helper for `CameraDistWorker::Visit<Camera>`.

**LocalizedPacketDo<Builder>** (ctor): Constructs a localized packet sender with caching.

**LocalizedPacketListDo<Builder>** (ctor): Constructs a localized packet list sender with caching.

**~LocalizedPacketListDo<Builder>** (dtor): Cleans up cached packets in `LocalizedPacketListDo`.

---

<!-- machine-true, projected from graph.json -->

## Map — GridNotifiers

*Source:* GridNotifiers.cpp, GridNotifiers.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| Visit#6 | method | Camera/UpdateVisibilityOf | — | — |
| Notify | method | Camera/GetBody, Camera/GetOwner, GenericTransport/GetPassengers, Log.Main/HasLogFilter, Log.Main/HasLogLevelOrHigher, Log.Main/Out, Map.Main/GetPlayer, Map.Main/UpdateActiveObjectVisibility#3, Object/GetGuidStr, Object/GetObjectGuid, Object/GetTypeId, Object/ToCreature, Object/ToGameObject, Object/ToPlayer, ObjectAccessor/FindPlayer, ObjectGuid/GetString, ObjectGuid/IsPlayer, Player.Main/GetCamera, Player.Main/GetSession, Player.Main/UpdateVisibilityOf, Player.Main/UpdateVisibilityOf#2, PlayerBroadcaster/RemoveListener, UpdateData/AddOutOfRangeGUID, UpdateData/GetOutOfRangeGUIDs, UpdateData/HasData, UpdateData/Send, WorldObject.Object/GetMap, WorldObject.Object/GetTransport | Camera/UpdateVisibilityForOwner | — |
| VisibleChangesNotifier | ctor | — | Map.Main/UpdateObjectVisibility | — |
| Visit | method | Camera/GetOwner, Player.Main/GetSession, WorldSession.Main/SendPacket | — | — |
| Visit#2 | method | Camera/GetOwner, Player.Main/GetSession, WorldSession.Main/SendPacket | — | — |
| Visit#4 | method | Camera/GetOwner, Player.Main/GetSession, WorldSession.Main/SendPacket | — | — |
| Visit#3 | method | Camera/GetBody, Camera/GetOwner, Player.Main/GetSession, Player.Main/GetTeam, WorldObject.Object/IsWithinDist, WorldSession.Main/SendPacket | — | — |
| Visit#5 | method | Camera/GetBody, Camera/GetOwner, Player.Main/GetSession, WorldObject.Object/IsWithinDist, WorldSession.Main/SendPacket | — | — |
| operator() | method | Corpse/IsFriendlyTo, WorldObject.Object/IsWithinDistInMap | — | — |
| operator()#2 | method | BattleGround/IsActiveEvent, BattleGroundMap/GetBG, BattleGroundMgr/GetCreatureEventIndex, Creature.Main/Respawn, Map.Main/IsBattleGround, Object/GetGUIDLow, WorldObject.Object/GetMap | — | — |
| WorldObjectSearcher<Check> | ctor | — | — | — |
| operator()#3 | method | BattleGround/IsActiveEvent, BattleGroundMap/GetBG, BattleGroundMgr/GetGameObjectEventIndex, GameObject/Respawn, Map.Main/IsBattleGround, Object/GetGUIDLow, WorldObject.Object/GetMap | — | — |
| WorldObjectListSearcher<Check> | ctor | — | — | — |
| WorldObjectWorker<Do> | ctor | — | — | — |
| Visit#15 | function | — | — | — |
| Visit#16 | function | — | — | — |
| Visit#13 | function | — | — | — |
| Visit#12 | function | — | — | — |
| Visit#14 | function | — | — | — |
| GameObjectSearcher<Check> | ctor | — | — | — |
| GameObjectLastSearcher<Check> | ctor | — | — | — |
| GameObjectListSearcher<Check> | ctor | — | — | — |
| UnitSearcher<Check> | ctor | — | — | — |
| UnitWorker<Do> | ctor | — | — | — |
| Visit#11 | function | — | — | — |
| Visit#10 | function | — | — | — |
| UnitLastSearcher<Check> | ctor | — | — | — |
| UnitListSearcher<Check> | ctor | — | — | — |
| CreatureSearcher<Check> | ctor | — | — | — |
| CreatureLastSearcher<Check> | ctor | — | — | — |
| CreatureListSearcher<Check> | ctor | — | — | — |
| CreatureWorker<Do> | ctor | — | — | — |
| Visit#8 | function | — | — | — |
| PlayerSearcher<Check> | ctor | — | — | — |
| PlayerLastSearcher<Check> | ctor | — | — | — |
| PlayerListSearcher<Check> | ctor | — | — | — |
| PlayerWorker<Do> | ctor | — | — | — |
| Visit#9 | function | — | — | — |
| CameraDistWorker<Do> | ctor | — | — | — |
| Visit#7 | function | — | — | — |
| LocalizedPacketDo<Builder> | ctor | — | — | — |
| LocalizedPacketListDo<Builder> | ctor | — | — | — |
| ~LocalizedPacketListDo<Builder> | dtor | — | — | — |

---

<!-- verify: failed-members | invented: operator -->
