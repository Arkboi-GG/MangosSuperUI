<!-- provenance: failed-members -->
# GameObject

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# GameObject

## Purpose & Responsibilities

`GameObject` is the core server-side representation of static or semi-static world entities in the WoW emulator (MaNGOS-based). It handles the lifecycle, state management, interaction logic, and persistence of objects such as doors, chests, quest givers, traps, fishing nodes, and transports.

Key responsibilities include:
1.  **Lifecycle Management:** Creation, loading from database, saving to database, despawning, respawning, and deletion.
2.  **State Machine:** Managing `GOState` (Ready, Active, etc.) and `LootState` (Not Ready, Ready, Activated, Just Deactivated) to control interactability and visual appearance.
3.  **Interaction Handling:** Processing player interactions via `Use()`, which delegates to specific logic based on `GameobjectTypes` (e.g., opening doors, looting chests, triggering traps, sitting on chairs).
4.  **Persistence:** Syncing position, rotation, state, and respawn timers with the `gameobject` database table.
5.  **Specialized Behaviors:** Implementing logic for fishing mechanics, summoning rituals, linked traps, and transport movement (via derived classes `ElevatorTransport` and `ShipTransport`).
6.  **Visibility & Collision:** Managing whether the object is visible to players, its collision model, and its interaction radius.

## Member-by-Member Behavior

### Lifecycle and Initialization
*   **`GameObject` (ctor):** Initializes the object as a `SpellCaster`, sets default values for respawn time, loot state, and visibility. Marks the object as spawned by default.
*   **`~GameObject` (dtor):** Cleans up active spells, deletes the associated AI (`m_AI`) and collision model (`m_model`).
*   **`CreateGameObject`:** Factory method that creates a `GameObject` or an `ElevatorTransport` depending on the template type.
*   **`Create`:** Fully initializes the object with position, rotation, and template data. Validates position and template existence. Sets up faction, flags, and display ID. Handles special setup for transports and large/infinite objects.
*   **`AddToWorld`:** Registers the object with the map, inserts its collision model, initializes the AI, and starts spell proc timers.
*   **`RemoveFromWorld`:** Unregisters the object from the map, removes its collision model, notifies the AI and zone scripts, and cleans up owner references.
*   **`Delete`:** Sends a despawn animation, adds the object to the removal list, resets state/flags, and handles pool-specific despawn logic.
*   **`CleanupsBeforeDelete`:** Interrupts non-melee spells and kills pending events before deletion.

### State and Property Accessors
*   **`GetGOInfo`:** Returns the static template data for this object.
*   **`GetGoType` / `SetGoType`:** Gets/sets the functional type of the object (door, chest, etc.).
*   **`GetGoState` / `SetGoState`:** Gets/sets the visual/functional state (Ready, Active). Updates collision state.
*   **`getLootState` / `SetLootState`:** Gets/sets the internal loot/usage state. Resets cooldown for chests when deactivated.
*   **`GetDisplayId` / `SetDisplayId`:** Gets/sets the visual model ID. Triggers model update.
*   **`GetGoArtKit` / `SetGoArtKit`:** Gets/sets the art kit (visual theme) for the object.
*   **`GetGoAnimProgress` / `SetGoAnimProgress`:** Gets/sets the animation progress value.
*   **`GetName` / `GetNameForLocaleIdx`:** Retrieves the object's name, supporting localization.
*   **`GetOwnerGuid` / `SetOwnerGuid`:** Manages the GUID of the unit/player that created/owns this object. Setting an owner marks the object as not spawned by default.
*   **`GetOwner`:** Resolves the owner GUID to a `Unit` pointer.
*   **`GetAffectingPlayer`:** Finds the player controlling the owner (handling charmers/pets).
*   **`IsCharmerOrOwnerPlayerOrPlayerItself`:** Checks if the owner is a player.
*   **`GetSpellId` / `SetSpellId`:** Manages the spell ID associated with the object's creation or effect.
*   **`GetRespawnTime` / `GetRespawnTimeEx`:** Returns the absolute time of the next respawn.
*   **`GetRespawnDelay` / `SetRespawnDelay`:** Gets/sets the base delay in seconds before respawn.
*   **`SetRespawnTime`:** Sets the absolute respawn time and recalculates the delay.
*   **`isSpawned`:** Determines if the object is currently present in the world based on respawn timers and default spawn status.
*   **`isSpawnedByDefault` / `SetSpawnedByDefault`:** Indicates if the object is a permanent world object or a temporary summon.
*   **`GetDBTableGUIDLow`:** Returns the low GUID if the object has static database spawn data.
*   **`HasStaticDBSpawnData`:** Checks if the object exists in the `gameobject` table.
*   **`GetGOData`:** Retrieves the runtime data structure for this object from the manager.

### Persistence (Database)
*   **`SaveToDB`:** Saves the current position, rotation, state, and respawn data to the `gameobject` table. Uses a transaction to delete and re-insert the row.
*   **`LoadFromDB`:** Loads object data from the `gameobject` table and calls `Create` to initialize it. Handles respawn timers and spawn flags.
*   **`DeleteFromDB`:** Removes the object from the `gameobject`, `game_event_gameobject`, and `gameobject_battleground` tables. Clears persistent state.
*   **`SaveRespawnTime`:** Persists the current respawn timer to the map's persistent state manager.

### Interaction and Usage
*   **`Use`:** The main entry point for interacting with the object. Delegates to specific logic based on `GetGoType()`. Handles cooldowns, immunity checks, and script triggers.
*   **`PlayerCanUse`:** Pre-checks if a player can interact with the object (distance, locks, requirements).
*   **`IsUseRequirementMet`:** Checks specific conditions like requiring a creature to be dead or another object to be active.
*   **`UseDoorOrButton`:** Activates a door/button, setting it to `GO_ACTIVATED` and scheduling a reset.
*   **`ResetDoorOrButton`:** Resets a door/button to `GO_STATE_READY` and `GO_JUST_DEACTIVATED`.
*   **`SwitchDoorOrButton`:** Toggles the visual state and flags for doors/buttons.
*   **`TriggerLinkedGameObject`:** Finds and activates a linked trap or object nearby.
*   **`RespawnLinkedGameObject`:** Respawns a linked trap if it is despawned.
*   **`SummonLinkedTrapIfAny`:** Creates and places a linked trap object at the current location.
*   **`ActivateToQuest`:** Determines if the object should be highlighted/active for a player based on their quest status.
*   **`HasQuest` / `HasInvolvedQuest`:** Checks if the object is related to a specific quest ID.

### Special Mechanics
*   **`Update`:** The main update loop. Handles:
    *   Spell procs and cooldowns.
    *   AI updates.
    *   Loot state transitions (e.g., traps arming, chests restocking, fishing bobbers readying).
    *   Respawn timers.
    *   Door/button auto-closing.
    *   Ritual completion.
*   **`getFishLoot`:** Generates loot for fishing based on the zone/subzone.
*   **`LookupFishingHoleAround`:** Finds a nearby fishing hole object.
*   **`AddUniqueUse` / `RemoveUniqueUse` / `HasUniqueUser` / `GetUniqueUseCount`:** Manages the list of unique players participating in a summoning ritual.
*   **`FinishRitual`:** Completes a summoning ritual, applying cooldowns and casting the final spell.
*   **`ComputeRespawnDelay`:** Calculates the actual respawn delay, applying randomization or population-based scaling if configured.
*   **`JustDespawnedWaitingRespawn`:** Handles cleanup for pooled objects after despawn.
*   **`Refresh`:** Forces the object to be added to the map if it is spawned.
*   **`Despawn`:** Initiates the despawn sequence, setting respawn timers and sending animations.
*   **`Respawn`:** Manually triggers a respawn, resetting the timer.

### Visibility and Collision
*   **`IsVisible` / `SetVisible`:** Controls basic visibility.
*   **`IsVisibleForInState`:** Complex visibility check considering GM mode, stealth, distance, and server-only flags.
*   **`UpdateCollisionState`:** Enables/disables the collision model based on state (e.g., disabled for opened chests).
*   **`UpdateModel`:** Rebuilds the collision model if the display ID changes.
*   **`UpdateModelPosition`:** Updates the collision model's position.
*   **`GetLosCheckPosition`:** Calculates the center point for Line-of-Sight checks, accounting for model bounds.
*   **`GetObjectBoundingRadius`:** Returns the default bounding radius.
*   **`IsAtInteractDistance`:** Checks if a player is within range to interact, considering spell ranges or model bounds.
*   **`GetClosestChairSlotPosition`:** Calculates the position of the nearest seat on a multi-slot chair.

### Faction and Hostility
*   **`IsHostileTo` / `IsFriendlyTo`:** Determines hostility/friendliness based on faction templates, reputation, and owner status.
*   **`CanAggroWhenOpening`:** Checks if opening this chest should aggro nearby creatures (Treasure lock type).
*   **`DoAggroWhenOpening`:** Aggroes nearby hostile creatures if applicable.

### Transports
*   **`IsTransport` / `IsMoTransport`:** Checks if the object is a transport or moving transport.
*   **`ToTransport`:** Casts the object to a `GenericTransport` pointer.
*   **`GetStationaryX/Y/Z/O`:** Returns the stationary position/orientation, unless it's a moving transport.
*   **`GetLocalRotation`:** Returns the object's rotation as a quaternion.
*   **`UpdateRotationFields`:** Updates the rotation fields in the object's data, handling the conversion from orientation to quaternion components.

### Utility and Helpers
*   **`AIM_Initialize`:** Initializes the object's AI.
*   **`AI`:** Returns the AI pointer.
*   **`ClearSkillupList` / `ClearAllUsesData`:** Resets usage counters and skill-up lists.
*   **`AddUse` / `GetUseCount`:** Tracks the number of times the object has been used.
*   **`SetCooldownTime`:** Sets the internal cooldown timer.
*   **`GetDefaultGossipMenuId`:** Returns the default gossip menu ID from the template.
*   **`GetGridRef`:** Returns the grid reference for the object.
*   **`SetOwnerGroupId`:** Sets the group ID for party-only objects.
*   **`GetLevel`:** Returns the level of the object, used for loot scaling.
*   **`GetSpellForLock`:** Finds a spell the player knows that can unlock the object.
*   **`HasCustomAnim`:** Checks if the object has a predefined custom animation.
*   **`SendGameObjectCustomAnim`:** Sends a packet to play a custom animation.
*   **`SendGameObjectReset`:** Sends a packet to reset the object's visual state.
*   **`HunterTrapTargetSelectorCheck`:** Helper struct for selecting targets for hunter traps.
*   **`operator()#3` (in `HunterTrapTargetSelectorCheck`):** Predicate for finding valid trap targets.
*   **`ToGameObject` / `ToGameObject#2`:** Static helper functions to cast `Object` pointers to `GameObject`.
*   **`AddToRemoveListInMaps` / `SpawnInMaps`:** Static methods to manage object presence across all map instances.
*   **`AddGameObjectToRemoveListInMapsWorker` / `SpawnGameObjectInMapsWorker`:** Helper structs for the above static methods.
*   **`operator` (in workers):** Implementation of the worker logic.
*   **`GameObjectRespawnDeleteWorker`:** Helper struct for clearing respawn times during deletion.
*   **`operator()#2` (in `GameObjectRespawnDeleteWorker`):** Implementation of the worker logic.
*   **`isUnit` (in `QuaternionData`):** Checks if a quaternion is normalized.
*   **`toEulerAnglesZYX` / `fromEulerAnglesZYX` (in `QuaternionData`):** Converts between quaternions and Euler angles.

## Cross-Unit Boundaries

*   **`Map.Main`:** `GameObject` relies heavily on `Map` for spatial indexing (`InsertObject`, `EraseObject`), model management (`InsertGameObjectModel`, `RemoveGameObjectModel`), and persistent state (`GetPersistentState`). It is called by `Map` for spawning and despawning.
*   **`ObjectMgr`:** Used to retrieve static template data (`GetGameObjectTemplate`, `GetGOData`) and quest relations.
*   **`ScriptMgr`:** `GameObject` calls `ScriptMgr` to initialize AI (`GetGameObjectAI`) and trigger scripted events (`OnGameObjectUse`, `OnGossipHello`).
*   **`SpellCaster` / `Spell`:** `GameObject` inherits from `SpellCaster` to cast spells. It interacts with `Spell` objects for effects and cooldowns.
*   **`Player`:** `GameObject` interacts with `Player` for loot, gossip, quest activation, and usage validation.
*   **`Unit`:** `GameObject` tracks owners and interacts with units for hostility checks and trap targeting.
*   **`LootMgr` / `Loot`:** Used for generating and managing loot tables.
*   **`Database`:** Directly executes SQL queries for persistence.
*   **`World`:** Accesses configuration settings and game time.
*   **`ZoneScript`:** Notifies zone scripts of object creation/removal.
*   **`BattleGround`:** Interacts with battleground logic for flag clicks and trigger buffs.
*   **`PoolManager`:** Manages object pools for efficient respawning.
*   **`GameObjectAI`:** The AI component attached to the object.
*   **`GameObjectModel`:** The collision model component.
*   **`Transport`:** Derived classes (`ElevatorTransport`, `ShipTransport`) extend `GameObject` for transport functionality.

## Data Model

`GameObject` primarily interacts with the following database tables:

*   **`gameobject`:** Stores the persistent state of each game object instance.
    *   `guid`: Primary key, unique identifier.
    *   `id`: References the template in `gameobject_template`.
    *   `map`: Map ID.
    *   `position_x`, `position_y`, `position_z`: Coordinates.
    *   `orientation`: Facing angle.
    *   `rotation0` - `rotation3`: Quaternion rotation components.
    *   `spawntimesecsmin`, `spawntimesecsmax`: Respawn delay range. Negative values indicate temporary spawns.
    *   `animprogress`: Animation progress.
    *   `state`: Current GO state.
    *   `spawn_flags`: Flags controlling spawn behavior (e.g., disabled, active).
    *   `visibility_mod`: Custom visibility modifier.
    *   `patch_min`, `patch_max`: Patch version constraints.
*   **`game_event_gameobject`:** Links game objects to game events.
    *   `guid`: Foreign key to `gameobject.guid`.
    *   `event`: Event ID.
*   **`gameobject_battleground`:** Links game objects to battleground events.
    *   `guid`: Foreign key to `gameobject.guid`.
    *   `event1`, `event2`: Event IDs.

## Notable Implementation Details

*   **State Machine Complexity:** The `Update()` method contains a complex state machine for `m_lootState`. Transitions depend on object type, timers, and external events. For example, chests transition from `GO_NOT_READY` to `GO_READY` after a restock time, and from `GO_READY` to `GO_JUST_DEACTIVATED` after being looted.
*   **Fishing Logic:** Fishing involves multiple steps: casting a spell, creating a `GAMEOBJECT_TYPE_FISHINGNODE`, waiting for a timer, and then resolving success/failure based on skill and zone difficulty. Success may involve finding a `GAMEOBJECT_TYPE_FISHINGHOLE` nearby.
*   **Summoning Rituals:** These objects track unique users (`m_UniqueUsers`) and require a specific number of participants. The `AddUniqueUse` and `RemoveUniqueUse` methods manage this list with thread safety (`std::shared_timed_mutex`).
*   **Linked Objects:** Doors and buttons can have linked traps or other objects. `TriggerLinkedGameObject` searches for these nearby and activates them. `RespawnLinkedGameObject` ensures they respawn when the parent object respawns.
*   **Transport Handling:** Moving transports (`GAMEOBJECT_TYPE_MO_TRANSPORT`) have special handling in `Update()` and `IsVisibleForInState`. They are always visible to players on the same map.
*   **Collision Model:** The collision model (`m_model`) is dynamically enabled/disabled based on state. For example, an opened chest disables its collision model.
*   **Rotation Encoding:** `UpdateRotationFields` encodes the orientation into a 64-bit integer format used by the client, involving trigonometric calculations and bit manipulation.
*   **Pool Integration:** Objects can be part of a pool (`PoolManager`). `JustDespawnedWaitingRespawn` handles pool-specific cleanup, ensuring the object is removed from the pool's active list.
*   **Hardcoded Hacks:** There are several hardcoded checks for specific object entries (e.g., fishing holes, trap radii) which may need updating for new content.
*   **Thread Safety:** `m_UniqueUsers` is protected by a mutex, but other state variables are not, implying that `GameObject` methods are typically called from the main game thread.

## Member Reference

**isUnit**: Checks if a `QuaternionData` is normalized.
**GetGOInfo**: Returns the static template data for this object.
**GetDBTableGUIDLow**: Returns the low GUID if the object has static database spawn data.
**toEulerAnglesZYX**: Converts a `QuaternionData` to Euler angles.
**fromEulerAnglesZYX**: Creates a `QuaternionData` from Euler angles.
**GetName**: Retrieves the object's name from its template.
**GameObject**: Constructor initializing default values and marking as spawned by default.
**SetOwnerGuid**: Sets the owner GUID and marks the object as not spawned by default.
**GetOwnerGuid**: Returns the owner GUID.
**IsCharmerOrOwnerPlayerOrPlayerItself**: Checks if the owner is a player.
**SetSpellId**: Sets the spell ID associated with the object.
**~GameObject**: Destructor cleaning up spells, AI, and model.
**GetSpellId**: Returns the associated spell ID.
**GetRespawnTime**: Returns the absolute time of the next respawn.
**GetRespawnTimeEx**: Returns the respawn time, clamped to current time if expired.
**CreateGameObject**: Factory method creating `GameObject` or `ElevatorTransport`.
**SetRespawnTime**: Sets the absolute respawn time and recalculates delay.
**SetRespawnDelay**: Sets the base respawn delay in seconds.
**AddToWorld**: Registers the object with the map, model, and AI.
**isSpawned**: Determines if the object is currently present in the world.
**ToTransport**: Casts the object to a `GenericTransport` pointer.
**isSpawnedByDefault**: Indicates if the object is a permanent world object.
**SetSpawnedByDefault**: Sets the default spawn status.
**GetRespawnDelay**: Returns the base respawn delay.
**ToTransport#2**: Const version of `ToTransport`.
**GetGoType**: Returns the functional type of the object.
**SetGoType**: Sets the functional type of the object.
**AIM_Initialize**: Initializes the object's AI.
**GetGoState**: Returns the visual/functional state.
**GetGoArtKit**: Returns the art kit ID.
**SetGoArtKit**: Sets the art kit ID.
**GetGoAnimProgress**: Returns the animation progress.
**RemoveFromWorld**: Unregisters the object from the map and cleans up.
**SetGoAnimProgress**: Sets the animation progress.
**GetDisplayId**: Returns the visual model ID.
**getLootState**: Returns the internal loot/usage state.
**ClearSkillupList**: Clears the list of players who have gained skill-ups.
**ClearAllUsesData**: Resets usage counters and skill-up lists.
**Create**: Fully initializes the object with position, rotation, and template data.
**getSummonTarget**: Returns the GUID of the player being summoned.
**SetSummonTarget**: Sets the GUID of the player being summoned.
**AddUse**: Increments the usage counter.
**GetUseCount**: Returns the usage counter.
**SetCooldownTime**: Sets the internal cooldown timer.
**GetDefaultGossipMenuId**: Returns the default gossip menu ID.
**GetGridRef**: Returns the grid reference.
**SetOwnerGroupId**: Sets the group ID for party-only objects.
**AI**: Returns the AI pointer.
**GetStationaryX**: Returns the stationary X coordinate.
**GetStationaryY**: Returns the stationary Y coordinate.
**GetStationaryZ**: Returns the stationary Z coordinate.
**GetStationaryO**: Returns the stationary orientation.
**GetRotation**: Returns the encoded rotation value.
**IsVisible**: Checks basic visibility.
**GetFactionTemplateId**: Returns the faction template ID.
**HunterTrapTargetSelectorCheck**: Helper struct for selecting trap targets.
**GetFocusObject**: Returns the trap object for focus.
**operator()#3**: Predicate for finding valid trap targets.
**ToGameObject**: Static helper to cast `Object` to `GameObject`.
**Update**: Main update loop handling state, timers, AI, and spells.
**ToGameObject#2**: Static helper to cast `Object const` to `GameObject const`.
**ComputeRespawnDelay**: Calculates actual respawn delay with modifiers.
**ComputeRespawnDelay#2**: Static helper for computing respawn delay.
**JustDespawnedWaitingRespawn**: Handles cleanup for pooled objects after despawn.
**Refresh**: Forces the object to be added to the map.
**AddUniqueUse**: Adds a player to the unique users list for rituals.
**RemoveUniqueUse**: Removes a player from the unique users list.
**FinishRitual**: Completes a summoning ritual.
**HasUniqueUser**: Checks if a player is in the unique users list.
**GetUniqueUseCount**: Returns the number of unique users.
**CleanupsBeforeDelete**: Interrupts spells and kills events before deletion.
**Delete**: Initiates the deletion sequence.
**getFishLoot**: Generates loot for fishing.
**SaveToDB**: Saves object data to the database.
**SaveToDB#2**: Overload of `SaveToDB` with explicit map ID.
**LoadFromDB**: Loads object data from the database.
**GameObjectRespawnDeleteWorker**: Helper struct for clearing respawn times.
**operator()#2**: Implementation of `GameObjectRespawnDeleteWorker`.
**DeleteFromDB**: Removes object data from the database.
**HasQuest**: Checks if the object is related to a quest.
**HasInvolvedQuest**: Checks if the object is involved in a quest.
**IsTransport**: Checks if the object is a transport.
**IsMoTransport**: Checks if the object is a moving transport.
**GetOwner**: Resolves the owner GUID to a `Unit` pointer.
**GetAffectingPlayer**: Finds the player controlling the owner.
**SaveRespawnTime**: Persists the respawn timer.
**SetVisible**: Sets basic visibility.
**IsVisibleForInState**: Complex visibility check.
**Respawn**: Manually triggers a respawn.
**ActivateToQuest**: Determines if the object should be active for a player's quest.
**SummonLinkedTrapIfAny**: Creates and places a linked trap.
**TriggerLinkedGameObject**: Activates a linked trap or object.
**RespawnLinkedGameObject**: Respawns a linked trap.
**LookupFishingHoleAround**: Finds a nearby fishing hole.
**ResetDoorOrButton**: Resets a door/button.
**UseDoorOrButton**: Activates a door/button.
**SwitchDoorOrButton**: Toggles the state of a door/button.
**Use**: Main interaction handler.
**GetNameForLocaleIdx**: Retrieves localized name.
**UpdateRotationFields**: Updates rotation fields.
**IsHostileTo**: Checks hostility.
**IsFriendlyTo**: Checks friendliness.
**IsUseRequirementMet**: Checks usage requirements.
**PlayerCanUse**: Pre-checks if a player can use the object.
**SetLootState**: Sets the loot state.
**SetGoState**: Sets the GO state.
**SetDisplayId**: Sets the display ID.
**GetObjectBoundingRadius**: Returns the bounding radius.
**IsInSkillupList**: Checks if a player is in the skill-up list.
**AddToSkillupList**: Adds a player to the skill-up list.
**AddGameObjectToRemoveListInMapsWorker**: Helper struct for removing objects from maps.
**operator**: Implementation of `AddGameObjectToRemoveListInMapsWorker`.
**AddToRemoveListInMaps**: Static method to remove objects from all map instances.
**SpawnGameObjectInMapsWorker**: Helper struct for spawning objects in maps.
**operator()#4**: Implementation of `SpawnGameObjectInMapsWorker`.
**SpawnInMaps**: Static method to spawn objects in all map instances.
**HasStaticDBSpawnData**: Checks if the object has static DB data.
**UpdateCollisionState**: Updates the collision model state.
**UpdateModel**: Rebuilds the collision model.
**UpdateModelPosition**: Updates the collision model position.
**GetLosCheckPosition**: Calculates LOS check position.
**GetGOData**: Retrieves runtime data.
**HasCustomAnim**: Checks for custom animations.
**SendGameObjectCustomAnim**: Sends custom animation packet.
**SendGameObjectReset**: Sends reset state packet.
**Despawn**: Initiates despawn sequence.
**GetLevel**: Returns the object's level.
**IsAtInteractDistance#2**: Checks interaction distance with player.
**GetClosestChairSlotPosition**: Calculates nearest chair slot.
**IsAtInteractDistance**: Checks interaction distance with position.
**GetSpellForLock**: Finds a spell to unlock the object.
**GetLocalRotation**: Returns the local rotation quaternion.
**CanAggroWhenOpening**: Checks if opening aggroes creatures.
**DoAg

---

<!-- machine-true, projected from graph.json -->

## Map — GameObject

*Source:* GameObject.cpp, GameObject.h, Transport.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| isUnit | method | — | — | — |
| GetGOInfo | method | — | AiBotAI.Bridge/BridgeHandleUseGameObject, ChatHandler.ObjectCommands/HandleGameObjectInfoCommand, ChatHandler.ObjectCommands/HandleGameObjectMoveCommand, ChatHandler.ObjectCommands/HandleGameObjectSelectCommand, ChatHandler.ObjectCommands/HandleGameObjectTurnCommand, GameObjectModel/construct, GameObjectModel/initialize, game_Battlegrounds_BattleGround/SpawnBGObject, instance_dire_maul/OnUse, Player.Main/PrepareGossipMenu, Player.Main/SendLoot, ScriptMgr/GetDialogStatus#2, ScriptMgr/GetGameObjectAI, ScriptMgr/OnEffectDummy#2, ScriptMgr/OnGameObjectOpen, ScriptMgr/OnGameObjectUse, ScriptMgr/OnGossipHello#2, ScriptMgr/OnGossipSelect#2, ScriptMgr/OnQuestAccept#2, ScriptMgr/OnQuestRewarded#2, Spell.Effects/EffectOpenLock, Spell.Main/CheckCast, Spell.Main/finish, Spell.Main/update, SpellCaster/GetLevelForTarget, spell_special/OnSuccessfulStart, Transport/Create, WorldSession.LootHandler/DoLootRelease, ZoneScript/HandlePlayerEnter, ZoneScript/HandlePlayerLeave, ZoneScript/SendChangePhase, ZoneScript/Update | — |
| GetDBTableGUIDLow | method | — | ChatHandler.ObjectCommands/HandleGameObjectDeleteCommand, ChatHandler.ObjectCommands/HandleGameObjectSelectCommand, Conditions/Evaluate, instance_naxxramas.Main/OnObjectCreate, Player.Main/SendLoot | — |
| toEulerAnglesZYX | method | — | — | — |
| fromEulerAnglesZYX | method | QuaternionData/QuaternionData#2 | — | — |
| GetName | method | — | ChatHandler.ObjectCommands/HandleGameObjectSelectCommand, ChatHandler.ObjectCommands/HandleGameObjectSendCustomAnimCommand, ChatHandler.ObjectCommands/HandleGameObjectSendDespawnAnimCommand, ChatHandler.ObjectCommands/HandleGameObjectSendSpawnAnimCommand, ChatHandler.ObjectCommands/HandleGameObjectSetGoStateCommand, ChatHandler.ObjectCommands/HandleGameObjectSetLootStateCommand, Transport/AddPassenger, Transport/RemovePassenger, Transport/Update#2 | — |
| GameObject | ctor | Loot/Loot, ObjectGuid/ObjectGuid, SpellCaster/SpellCaster | game_Battlegrounds_BattleGround/AddObject, Spell.Effects/EffectDuel, Spell.Effects/EffectDummy, Spell.Effects/EffectSummonObject, Spell.Effects/EffectSummonObjectWild, Spell.Effects/EffectTransmitted | — |
| SetOwnerGuid | method | — | boss_kurinnaxx/UpdateAI, Spell.Effects/EffectDummy, Spell.Effects/EffectTransmitted, ThreatListCopier.battleground_alterac/AV_BeaconInvocationObjectAI, Unit.Main/AddGameObject, Unit.Main/RemoveAllGameObjects, Unit.Main/RemoveGameObject, Unit.Main/RemoveGameObject#2, Unit.Main/_UpdateSpells | — |
| GetOwnerGuid | method | — | ChatHandler.ObjectCommands/HandleGameObjectDeleteCommand, Player.Main/SendLoot, Unit.Main/AddGameObject, Unit.Main/RemoveGameObject, WorldObject.Object/IsControlledByPlayer, WorldSession.LootHandler/HandleAutostoreLootItemOpcode, WorldSession.LootHandler/HandleLootMoneyOpcode | — |
| IsCharmerOrOwnerPlayerOrPlayerItself | method | — | — | — |
| SetSpellId | method | — | Spell.Effects/EffectCreateHouse, Spell.Effects/EffectDuel, Spell.Effects/EffectDummy, Spell.Effects/EffectSummonObject, Spell.Effects/EffectSummonObjectWild, Spell.Effects/EffectTransmitted, zulfarrak/MovementInform | — |
| ~GameObject | dtor | Errors/PrintStacktraceAndThrow, Spell.Main/SetReferencedFromCurrent | — | — |
| GetSpellId | method | — | Unit.Main/AddGameObject, Unit.Main/GetGameObject, Unit.Main/RemoveGameObject, Unit.Main/RemoveGameObject#2 | — |
| GetRespawnTime | method | — | ChatHandler.ObjectCommands/HandleGameObjectInfoCommand, game_Battlegrounds_BattleGround/SpawnBGObject | — |
| GetRespawnTimeEx | method | — | ChatHandler.ObjectCommands/HandleGameObjectTargetCommand, Player.Main/SendLoot | — |
| CreateGameObject | method | ObjectMgr/GetGameObjectTemplate | ChatHandler.ObjectCommands/HandleGameObjectAddCommand, game_Battlegrounds_BattleGround/SpawnBGObject, Map.Main/LoadGameObjectSpawn, Map.Main/SummonGameObject, ObjectMgr/AddGOData, PoolManager/Spawn1Object#2, WorldObject.Object/SummonGameObject | — |
| SetRespawnTime | method | — | boss_celebras_the_cursed/WaypointReached, boss_moam/JustDied, boss_ouro/OnUse, ChatHandler.ObjectCommands/HandleGameObjectAddCommand, ChatHandler.ObjectCommands/HandleGameObjectDeleteCommand, dreadsteed_ritual/EventSecondPartStart, eastern_plaguelands/go_darrowshire_triggerAI, feralas/JustDied, fireworks_show/UpdateAI, game_Battlegrounds_BattleGround/DelObject, game_Battlegrounds_BattleGround/SpawnBGObject, hillsbrad_foothills/StartEvent, instance_ruins_of_ahnqiraj/OnObjectCreate, Map.Main/LoadGameObjectSpawn, Map.Main/SummonGameObject, Map.ScriptCommands/ScriptCommand_RespawnGameObject, PoolManager/Spawn1Object#2, razorfen_kraul/MovementInform, ruins_of_ahnqiraj/JustDied, ruins_of_ahnqiraj/JustDied#4, ScriptedInstance/DoRespawnGameObject, Spell.Effects/EffectDuel, Spell.Effects/EffectDummy, Spell.Effects/EffectSummonObject, Spell.Effects/EffectSummonObjectWild, Spell.Effects/EffectTransmitted, ThreatListCopier.battleground_alterac/UpdateAI#9, Unit.Main/RemoveAllGameObjects, Unit.Main/RemoveGameObject, Unit.Main/RemoveGameObject#2, Unit.Main/_UpdateSpells, WorldObject.Object/SummonGameObject, world_event_wareffort/HandleSupplyObjectSpawn, ZoneScript/DelCapturePoint, ZoneScript/DelObject | — |
| SetRespawnDelay | method | — | felwood/PlantQuestRewarded, game_Battlegrounds_BattleGround/SpawnBGObject, Map.ScriptCommands/ScriptCommand_DespawnGameObject, PoolManager/Spawn1Object#2 | — |
| AddToWorld | method | Map.Main/InsertGameObjectModel, Object/AddToWorld, Object/GetObjectGuid, Object/IsInWorld, shared_Util/getMSTime, World/getConfig#4, WorldObject.Object/GetMap, ZoneScript/OnGameObjectCreate | game_Battlegrounds_BattleGround/AddObject, Map.Main/Add#5 | — |
| isSpawned | method | — | AiBotAI.Bridge/BridgeHandleUseGameObject, BattleBotAI.BattleBotWaypoints/AtFlag, BattleBotAI.BattleBotWaypoints/WSG_AtAllianceFlag, BattleBotAI.BattleBotWaypoints/WSG_AtHordeFlag, ChatHandler.ObjectCommands/HandleGameObjectInfoCommand, Conditions/Evaluate, desolace/go_ghost_magnetAI, dreadsteed_ritual/UpdateAI#4, dustwallow_marsh/WaypointReached, felwood/UpdateAI, game_Battlegrounds_BattleGround/HandleTriggerBuff, go_scripts/UpdateAI#4, go_scripts/UpdateAI#5, Map.ScriptCommands/ScriptCommand_DespawnGameObject, Map.ScriptCommands/ScriptCommand_RespawnGameObject, Player.Main/CanInteractWithGameObject, Player.Main/SendLoot, razorfen_kraul/IsValidTuber, ScriptedInstance/DoRespawnGameObject, silithus/OnActivateBySpell, ThreatListCopier.battleground_alterac/UpdateAI#9, Unit.Main/_UpdateSpells, WorldObject.Object/FindRandomGameObject, WorldSession.SpellHandler/HandleGameObjectUseOpcode, world_event_wareffort/HandleSupplyObjectSpawn | — |
| ToTransport | method | — | Map.Main/RemoveAllObjectsInRemoveList | — |
| isSpawnedByDefault | method | — | PoolManager/Spawn1Object#2, WorldSession.LootHandler/DoLootRelease | — |
| SetSpawnedByDefault | method | — | dreadsteed_ritual/EventEndedFail, dreadsteed_ritual/EventSecondPartStart, dreadsteed_ritual/gobjNextStep, felwood/PlantQuestRewarded, feralas/JustDied, instance_ruins_of_ahnqiraj/OnObjectCreate, Map.Main/SummonGameObject, OutdoorPvPEP/SummonCuringShrine, WorldObject.Object/SummonGameObject | — |
| GetRespawnDelay | method | — | ChatHandler.ObjectCommands/HandleGameObjectInfoCommand, ChatHandler.ObjectCommands/HandleGameObjectTargetCommand, go_scripts/UpdateAI#5, Map.Main/LoadGameObjectSpawn, PoolManager/Spawn1Object#2 | — |
| ToTransport#2 | method | — | — | — |
| GetGoType | method | — | ChatHandler.ObjectCommands/HandleGameObjectInfoCommand, desolace/GOHello_go_hand_of_iruxos_crystal, game_Battlegrounds_BattleGround/HandleTriggerBuff, go_scripts/GOHello_go_resonite_cask, hinterlands/GOHello_go_lards_picnic_basket, instance_naxxramas.Main/OnObjectCreate, Map.Main/Add#5, Map.Main/RemoveAllObjectsInRemoveList, Map.ScriptCommands/ScriptCommand_CloseDoor, Map.ScriptCommands/ScriptCommand_OpenDoor, Map.ScriptCommands/ScriptCommand_RespawnGameObject, Player.Main/CanInteractWithGameObject, Player.Main/PrepareGossipMenu, Player.Main/SendLoot, ScriptedInstance/DoOpenDoor, ScriptedInstance/DoResetDoor, ScriptedInstance/DoRespawnGameObject, ScriptedInstance/DoUseDoorOrButton, silithus/GOHello_scarab_gong, Spell.Effects/EffectSummonDemon, Spell.Effects/EffectSummonObjectWild, Spell.Effects/EffectSummonPlayer, Spell.Effects/SendLoot, Spell.Main/cancel, Spell.Main/CheckCast, Spell.Main/finish, Spell.Main/update, tanaris/GOHello_go_inconspicuous_landmark, Unit.Main/RemoveGameObject, WorldObject.Object/BuildValuesUpdate, WorldSession.LFGHandler/HandleMeetingStoneJoinOpcode, WorldSession.LootHandler/DoLootRelease, WorldSession.LootHandler/HandleAutostoreLootItemOpcode, WorldSession.SpellHandler/HandleGameObjectUseOpcode, ZoneScript/OnGameObjectRemove | — |
| SetGoType | method | — | Transport/Create#2 | — |
| AIM_Initialize | method | ScriptMgr/GetGameObjectAI | — | — |
| GetGoState | method | — | blackrock_depths/UpdateEscortAI#4, blackrock_depths/WaypointReached#5, boss_cannon_master_willey/ToggleGate, boss_chromaggus/Reset, boss_chromaggus/UpdateAI, boss_herod/JustDied, boss_marli/Reset, boss_marli/SelectNextEgg, ChatHandler.ObjectCommands/HandleGameObjectInfoCommand, Conditions/Evaluate, deadmines/GOHello_go_door_lever_dm, dreadsteed_ritual/UpdateAI#4, feralas/UpdateAI#4, game_Battlegrounds_BattleGround/DoorClose, instance_blackrock_depths/HandleBarPatrol, instance_blackwing_lair/SetData, instance_deadmines/OnCreatureDeath, instance_deadmines/SetData, instance_maraudon/SpewLarva, instance_scholomance/OnCreatureDeath, instance_scholomance/SetData, instance_stratholme/SetData, instance_sunken_temple/SetData, Map.ScriptCommands/ScriptCommand_CloseDoor, Map.ScriptCommands/ScriptCommand_OpenDoor, silithus/AnimateAQGate, silithus/BeginAQOpeningEvent, Spell.Main/CheckCast | — |
| GetGoArtKit | method | — | OutdoorPvPEP/UpdateBannerArt, OutdoorPvPEP/UpdateBannerArt#2, OutdoorPvPEP/UpdateBannerArt#3, OutdoorPvPEP/UpdateBannerArt#4 | — |
| SetGoArtKit | method | — | OutdoorPvPEP/UpdateBannerArt, OutdoorPvPEP/UpdateBannerArt#2, OutdoorPvPEP/UpdateBannerArt#3, OutdoorPvPEP/UpdateBannerArt#4 | — |
| GetGoAnimProgress | method | — | — | — |
| RemoveFromWorld | method | GameObjectAI/OnRemoveFromWorld, GameObjectInfo/GetLinkedGameObjectEntry, Log.Main/Out, Map.Main/ContainsGameObjectModel, Map.Main/RemoveGameObjectModel, Object/GetGuidStr, Object/GetObjectGuid, Object/IsInWorld, Object/RemoveFromWorld, ObjectAccessor/GetUnit, ObjectGuid/GetString, SpellCaster/RemoveAllDynObjects, Unit.Main/RemoveGameObject, WorldObject.Object/GetMap, ZoneScript/OnGameObjectRemove#2 | Map.Main/Remove#5, scourge_invasion/DespawnEventDoodads | — |
| SetGoAnimProgress | method | — | Transport/Create#2 | — |
| GetDisplayId | method | — | ChatHandler.DebugCommands/HandleMmapLocCommand, ChatHandler.DebugCommands/HandleMmapPathCommand, ChatHandler.DebugCommands/HandleMmapStatsCommand, ChatHandler.ObjectCommands/HandleGameObjectInfoCommand, GameObjectModel/construct, GameObjectModel/Relocate, Map.Main/GetWalkHitPosition, Map.Main/GetWalkRandomPosition, Player.Main/IsOutdoorOnTransport, WorldObject.PathFinder/calculate, WorldObject.PathFinder/HasMMapsForCurrentMap | — |
| getLootState | method | — | AiBotAI.Bridge/BridgeHandleUseGameObject, boss_herod/OnUse, ChatHandler.ObjectCommands/HandleGameObjectInfoCommand, ChatHandler.ObjectCommands/HandleGameObjectToggleCommand, Conditions/Evaluate, game_Battlegrounds_BattleGround/DoorClose, game_Battlegrounds_BattleGround/SpawnBGObject, instance_blackrock_spire/OnEffectExecute, instance_gnomeregan/SetData, Player.Main/SendLoot, ScriptedInstance/DoOpenDoor, ScriptedInstance/DoUseDoorOrButton, Spell.Main/cancel | — |
| ClearSkillupList | method | — | — | — |
| ClearAllUsesData | method | — | — | — |
| Create | method | Errors/PrintStacktraceAndThrow, GameObjectInfo/IsInfiniteGameObject, GameObjectInfo/IsLargeGameObject, Log.Main/Out, Map.Main/GetId, Object/SetEntry, ObjectMgr/GetGameObjectTemplate, Position/Position#2, World/getConfig, WorldObject.Object/IsPositionValid, WorldObject.Object/Relocate#2, WorldObject.Object/SetActiveObjectState, WorldObject.Object/SetFlag, WorldObject.Object/SetFloatValue, WorldObject.Object/SetMap, WorldObject.Object/SetObjectScale, WorldObject.Object/SetUInt32Value, WorldObject.Object/SetVisibilityModifier, WorldObject.Object/SetZoneScript, WorldObject.Object/_Create, ZoneScript/OnObjectCreate | ChatHandler.ObjectCommands/HandleGameObjectAddCommand, game_Battlegrounds_BattleGround/AddObject, Map.Main/SummonGameObject, Spell.Effects/EffectDuel, Spell.Effects/EffectDummy, Spell.Effects/EffectSummonObject, Spell.Effects/EffectSummonObjectWild, Spell.Effects/EffectTransmitted, Transport/Create, WorldObject.Object/SummonGameObject | — |
| getSummonTarget | method | — | — | — |
| SetSummonTarget | method | — | Spell.Effects/EffectTransmitted | — |
| AddUse | method | — | WorldSession.LootHandler/DoLootRelease, zulfarrak/OnGossipHello_go_shallow_grave | — |
| GetUseCount | method | — | WorldSession.LootHandler/DoLootRelease, zulfarrak/OnGossipHello_go_shallow_grave | — |
| SetCooldownTime | method | — | WorldSession.LootHandler/DoLootRelease | — |
| GetDefaultGossipMenuId | method | — | — | — |
| GetGridRef | method | — | — | — |
| SetOwnerGroupId | method | — | Spell.Effects/EffectTransmitted | — |
| AI | method | — | ashenvale/JustDied, ashenvale/ProcessEventId_event_king_of_the_foulweald, ashenvale/SpellHit, boss_urok/BannerDestroyed, boss_urok/JustDied, boss_urok/ProcessEventId_event_banner_destroyed, desolace/UpdateAI_corpse, dreadsteed_ritual/GOHello_go_ritual_node, dreadsteed_ritual/ProcessEventId_event_dreadsteed_ritual_second_part, dreadsteed_ritual/ProcessEventId_event_dreadsteed_ritual_start, felwood/QuestRewarded_go_corrupted_plant, hillsbrad_foothills/QuestRewarded_go_dusty_rug, hillsbrad_foothills/QuestRewarded_go_helcular_s_grave, hinterlands/GOHello_go_lards_picnic_basket, PointMovementGenerator/MovementInform#3, silithus/QuestRewarded_scarab_gong, Spell.Effects/EffectActivateObject, Spell.Effects/EffectOpenLock, Spell.Effects/EffectSummonObjectWild, tanaris/GOHello_go_inconspicuous_landmark, TargetedMovementGenerator/MovementInform, TargetedMovementGenerator/MovementInform#2, Unit.Main/Kill, WorldObject.Object/SummonCreature#2, WorldObject.Object/SummonGameObject, WorldSession.LootHandler/DoLootRelease | — |
| GetStationaryX | method | — | WorldObject.Object/BuildMovementUpdate | — |
| GetStationaryY | method | — | WorldObject.Object/BuildMovementUpdate | — |
| GetStationaryZ | method | — | WorldObject.Object/BuildMovementUpdate | — |
| GetStationaryO | method | — | WorldObject.Object/BuildMovementUpdate | — |
| GetRotation | method | — | — | — |
| IsVisible | method | — | — | — |
| GetFactionTemplateId | method | — | — | — |
| HunterTrapTargetSelectorCheck | ctor | — | — | — |
| GetFocusObject | method | — | — | — |
| operator()#3 | method | Creature.Main/IsTotem, Object/IsCreature, Object/ToPlayer#2, Player.Main/IsFFAPvP, Player.Main/IsInDuelWith, Unit.Main/GetCharmerOrOwnerPlayerOrPlayerItself, Unit.Main/IsHostileTo, Unit.Main/IsInCombat, Unit.Main/IsPvP, WorldObject.Object/CanSeeInWorld, WorldObject.Object/GetDistance#3, WorldObject.Object/IsValidAttackTarget, WorldObject.Object/IsWithinDistInMap | — | — |
| ToGameObject | function | — | Map.ScriptCommands/ScriptCommand_ActivateGameObject, Map.ScriptCommands/ScriptCommand_PlayCustomAnim, Map.ScriptCommands/ScriptCommand_ResetDoorOrButton, Map.ScriptCommands/ScriptCommand_SetGoState, Spell.Effects/EffectPersistentAA, Unit.SpellAuras/HandlePeriodicTriggerSpell | — |
| Update | method | AnyPlayerInObjectRangeCheck/AnyPlayerInObjectRangeCheck, EventProcessor/Update, GameObjectAI/OnUse, GameObjectAI/UpdateAI, GameObjectInfo/GetAutoCloseTime, GameObjectInfo/GetCharges, GameObjectInfo/GetLockId, GameObjectInfo/IsDespawnAtAction, game_Battlegrounds_BattleGround/HandleTriggerBuff, Loot/clear, Map.Main/GetCurrentClockTime, Map.Main/GetPlayer, Map.Main/Instanceable, Object/GetObjectGuid, Object/GetTypeId, Object/GetUInt32Value, ObjectGuid/IsMOTransport, Player.Main/GetBattleGround, Player.Main/GetSession, Player.Main/InBattleGround, Player.Main/ToPlayer, Spell.Main/getState, Spell.Main/SetReferencedFromCurrent, SpellCaster/CastSpell#2, SpellCaster/FinishSpell, SpellCaster/UpdateCooldowns, SpellCaster/UpdatePendingProcs, Unit.Main/RemoveGameObject, World/getConfig, WorldObject.Object/ForceValuesUpdateAtIndex, WorldObject.Object/GetMap, WorldObject.Object/PlayDistanceSound, WorldObject.Object/RemoveFlag, WorldObject.Object/SendForcedObjectUpdate, WorldObject.Object/SendObjectDeSpawnAnim, WorldObject.Object/SetUInt32Value, WorldObject.Object/Update, WorldObject.Object/UpdateObjectVisibility, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | Map.Main/Add#5 | — |
| ToGameObject#2 | function | — | — | — |
| ComputeRespawnDelay | method | — | — | — |
| ComputeRespawnDelay#2 | method | shared_Util/urand, World/GetActiveSessionCount | felwood/PlantQuestRewarded, PoolManager/Spawn1Object#2 | — |
| JustDespawnedWaitingRespawn | method | Log.Main/Out, Map.Main/GetPersistentState, Object/GetGUIDLow, Object/GetGuidStr, Object/IsDeleted, PoolManager/GetPoolGameObjects, PoolManager/IsPartOfAPool#2, WorldObject.Object/AddObjectToRemoveList, WorldObject.Object/GetMap | — | — |
| Refresh | method | WorldObject.Object/GetMap | boss_celebras_the_cursed/WaypointReached, ChatHandler.ObjectCommands/HandleGameObjectMoveCommand, ChatHandler.ObjectCommands/HandleGameObjectTurnCommand, dreadsteed_ritual/EventEndedFail, dreadsteed_ritual/EventSecondPartStart, dreadsteed_ritual/gobjNextStep, hillsbrad_foothills/StartEvent, instance_ruins_of_ahnqiraj/OnObjectCreate, razorfen_kraul/MovementInform, ScriptedInstance/DoRespawnGameObject | — |
| AddUniqueUse | method | Log.Main/Out, Object/GetEntry, Object/GetObjectGuid, ObjectGuid/operator!, ObjectGuid/operator!=, Spell.Main/prepare, Spell.Main/SetChannelingVisual, Spell.Main/Spell#2, SpellCastTargetsInfo/setGOTarget, SpellCastTargetsInfo/SpellCastTargets, SpellMgr/GetSpellEntry, SpellMgr/Instance | Spell.Effects/EffectTransmitted | — |
| RemoveUniqueUse | method | Object/GetObjectGuid, ObjectGuid/operator==, Unit.Main/RemoveGameObject | Spell.Main/cancel | — |
| FinishRitual | method | Map.Main/GetPlayer, Player.Main/AddCooldown, Player.Main/ToPlayer, shared_Util/urand, SpellCaster/CastSpell#2, SpellMgr/GetSpellEntry, SpellMgr/Instance, WorldObject.Object/GetMap | Spell.Main/finish | — |
| HasUniqueUser | method | Object/GetObjectGuid | Spell.Main/cancel | — |
| GetUniqueUseCount | method | — | Spell.Main/update | — |
| CleanupsBeforeDelete | method | EventProcessor/KillAllEvents, SpellCaster/InterruptNonMeleeSpells, WorldObject.Object/CleanupsBeforeDelete | Transport/CleanupsBeforeDelete | — |
| Delete | method | Map.Main/GetPersistentState, Object/GetGUIDLow, Object/IsDeleted, PoolManager/GetPoolGameObjects, PoolManager/IsPartOfAPool#2, WorldObject.Object/AddObjectToRemoveList, WorldObject.Object/GetMap, WorldObject.Object/SendObjectDeSpawnAnim, WorldObject.Object/SetUInt32Value | BattleGroundWS/RespawnFlagAfterDrop, blackrock_depths/WaypointReached#4, boss_celebras_the_cursed/GOHello_go_book_celebras, boss_celebras_the_cursed/WaypointReached, boss_dragon_of_nightmare/UpdateAI#2, boss_kurinnaxx/UpdateAI, boss_ouro/OnUse, boss_vectus/UpdateAI, ChatHandler.ObjectCommands/HandleGameObjectDeleteCommand, dustwallow_marsh/OnUse, dustwallow_marsh/UpdateAI, dustwallow_marsh/UpdateAI#2, eastern_plaguelands/DespawnAll#2, fireworks_show/UpdateAI, game_Battlegrounds_BattleGround/DelObject, instance_blackwing_lair/OnUse, instance_dire_maul/OnPlayerEnter, instance_dire_maul/QuestRewarded_go_broken_trap, instance_dire_maul/QuestRewarded_npc_knot_thimblejack, instance_dire_maul/UpdateAI#8, instance_molten_core/RemoveRuneFire, instance_molten_core/UpdateRune, instance_naxxramas.boss_kelthuzad/UpdateP1, Map.ScriptCommands/ScriptCommand_RemoveObject, scourge_invasion/JustDied#5, silithus/Larksbane_DoAction, Spell.Effects/EffectDummy, Spell.Main/update, ThreatListCopier.battleground_alterac/AV_NpcEventWorldBoss_A_AI, ThreatListCopier.battleground_alterac/AV_NpcEventWorldBoss_H_AI, Unit.Main/RemoveAllGameObjects, Unit.Main/RemoveGameObject, Unit.Main/RemoveGameObject#2, Unit.Main/_UpdateSpells, ZoneScript/DelCapturePoint, ZoneScript/DelObject | — |
| getFishLoot | method | Loot/clear, LootMgr/FillLoot, WorldObject.Object/GetZoneAndAreaId, WorldObject.Object/IsWithinDist2d | Player.Main/SendLoot | — |
| SaveToDB | method | Log.Main/Out, Object/GetGUIDLow, ObjectMgr/GetGOData, WorldObject.Object/GetMapId | ChatHandler.ObjectCommands/HandleGameObjectMoveCommand, ChatHandler.ObjectCommands/HandleGameObjectTurnCommand, world_event_wareffort/HandleSupplyObjectSpawn | — |
| SaveToDB#2 | method | Database/BeginTransaction, Database/CommitTransaction, Database/PExecuteLog, Object/GetEntry, Object/GetFloatValue, Object/GetGUIDLow, ObjectMgr/NewGOData | ChatHandler.ObjectCommands/HandleGameObjectAddCommand | gameobject |
| LoadFromDB | method | GameObjectData/GetRandomRespawnTime, GameObjectInfo/GetDespawnPossibility, GameObjectInfo/IsDespawnAtAction, Log.Main/Out, Map.Main/GetPersistentState, MapPersistentStateMgr/GetGORespawnTime, MapPersistentStateMgr/SaveGORespawnTime, Object/GetGUIDLow, ObjectMgr/GetGOData, WorldObject.Object/SetFlag | ChatHandler.ObjectCommands/HandleGameObjectAddCommand, game_Battlegrounds_BattleGround/SpawnBGObject, Map.Main/LoadElevatorTransports, Map.Main/LoadGameObjectSpawn, ObjectMgr/AddGOData, PoolManager/Spawn1Object#2 | — |
| GameObjectRespawnDeleteWorker | ctor | — | — | — |
| operator()#2 | method | MapPersistentStateMgr/SaveGORespawnTime | — | — |
| DeleteFromDB | method | Database/PExecuteLog, Log.Main/Out, Object/GetGUIDLow, ObjectMgr/DeleteGOData, WorldObject.Object/GetInstanceId, WorldObject.Object/GetMapId | ChatHandler.ObjectCommands/HandleGameObjectDeleteCommand | gameobject, gameobject_battleground, game_event_gameobject |
| HasQuest | method | Object/GetEntry, ObjectMgr/GetGOQuestRelationsMapBounds | — | — |
| HasInvolvedQuest | method | Object/GetEntry, ObjectMgr/GetGOQuestInvolvedRelationsMapBounds | — | — |
| IsTransport | method | GameObjectInfo/IsTransport | MovementAnticheat/CheckFakeTransport, Player.Main/UpdateForQuestWorldObjects, WorldObject.Object/BuildMovementUpdate, WorldObject.Object/BuildValuesUpdate | — |
| IsMoTransport | method | — | Player.Main/IsOutdoorOnTransport, Player.Main/UpdateVisibilityOf, Player.Main/UpdateVisibilityOf_helper | — |
| GetOwner | method | ObjectAccessor/GetUnit | DynamicObject/GetUnitCaster, Spell.Effects/EffectOpenLock, Spell.Effects/EffectPersistentAA, Spell.Main/update, Spell.Main/UpdateOriginalCasterPointer | — |
| GetAffectingPlayer | method | ObjectGuid/operator!, Unit.Main/GetCharmerOrOwnerPlayerOrPlayerItself | — | — |
| SaveRespawnTime | method | Map.Main/GetPersistentState, MapPersistentStateMgr/SaveGORespawnTime, Object/GetGUIDLow, WorldObject.Object/GetMap | Map.Main/LoadGameObjectSpawn, Map.Main/Remove#5, PoolManager/Spawn1Object#2 | — |
| SetVisible | method | WorldObject.Object/UpdateObjectVisibility | instance_deadmines/AreaTrigger_at_dmf_chest_dm, instance_deadmines/OnObjectCreate, instance_temple_of_ahnqiraj/OnObjectCreate, instance_temple_of_ahnqiraj/SetData, instance_wailing_caverns/AreaTrigger_at_dmf_chest_wc, instance_wailing_caverns/OnObjectCreate, silithus/AnimateAQGate | — |
| IsVisibleForInState | method | GameObjectInfo/IsServerOnly, Map.Main/GetVisibilityDistance, Object/IsInWorld, Object/IsPlayer, Object/ToUnit#2, Player.Main/IsGameMaster, World/GetVisibleObjectGreyDistance, WorldObject.Object/GetMap, WorldObject.Object/GetVisibilityModifier, WorldObject.Object/IsInMap, WorldObject.Object/IsWithinDistInMap | — | — |
| Respawn | method | Map.Main/GetPersistentState, MapPersistentStateMgr/SaveGORespawnTime, Object/GetGUIDLow, WorldObject.Object/GetMap | boss_arlokk/JustReachedHome, ChatHandler.ObjectCommands/HandleGameObjectRespawnCommand, dustwallow_marsh/WaypointReached, GridNotifiers/operator()#3, instance_ruins_of_ahnqiraj/OnObjectCreate | — |
| ActivateToQuest | method | GameObjectInfo/GetLootId, LootMgr/HaveQuestLootForPlayer, Object/GetEntry, ObjectMgr/GetGOQuestInvolvedRelationsMapBounds, ObjectMgr/GetGOQuestRelationsMapBounds, ObjectMgr/GetQuestTemplate, ObjectMgr/IsGameObjectForQuests, Player.Main/CanTakeQuest, Player.Main/GetQuestRewardStatus, Player.Main/GetQuestStatus, Player.Main/HasQuestForGO | Player.Main/UpdateForQuestWorldObjects, WorldObject.Object/BuildValuesUpdate | — |
| SummonLinkedTrapIfAny | method | GameObjectInfo/GetLinkedGameObjectEntry, Map.Main/GenerateLocalLowGuid, Object/GetUInt32Value, WorldObject.Object/GetMap, WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/SetUInt32Value | Spell.Effects/EffectSummonObject, Spell.Effects/EffectSummonObjectWild, Spell.Effects/EffectTransmitted | — |
| TriggerLinkedGameObject | method | GameObjectInfo/GetLinkedGameObjectEntry, NearestGameObjectEntryInObjectRangeCheck/NearestGameObjectEntryInObjectRangeCheck, ObjectMgr/GetGameObjectTemplate, SpellEntry/GetSpellMaxRange, SpellMgr/GetSpellEntry, SpellMgr/Instance | — | — |
| RespawnLinkedGameObject | method | GameObjectInfo/GetLinkedGameObjectEntry, NearestGameObjectEntryInObjectRangeCheck/NearestGameObjectEntryInObjectRangeCheck, ObjectMgr/GetGameObjectTemplate | — | — |
| LookupFishingHoleAround | method | NearestGameObjectFishingHoleCheck/NearestGameObjectFishingHoleCheck | — | — |
| ResetDoorOrButton | method | — | boss_arlokk/JustDied, boss_arlokk/JustReachedHome, boss_herod/OnUse, ChatHandler.ObjectCommands/HandleGameObjectResetCommand, ChatHandler.ObjectCommands/HandleGameObjectToggleCommand, instance_blackrock_depths/SetData, instance_gnomeregan/SetData, instance_scholomance/SetData, Map.ScriptCommands/ScriptCommand_ResetDoorOrButton, ScriptedInstance/DoResetDoor, ScriptedInstance/DoUseDoorOrButton, silithus/AnimateAQGate, silithus/HandleOpeningStage, silithus/ResetAQGates, Spell.Effects/EffectActivateObject, swamp_of_sorrows/WaypointReached | — |
| UseDoorOrButton | method | GameObjectInfo/GetAutoCloseTime | boss_arlokk/Aggro, boss_herod/JustDied, boss_herod/OnUse, ChatHandler.ObjectCommands/HandleGameObjectToggleCommand, desolace/JustStartedEscort#2, game_Battlegrounds_BattleGround/DoorClose, game_Battlegrounds_BattleGround/DoorOpen, go_scripts/UpdateAI#5, hinterlands/QuestAccept_npc_rinji, instance_blackrock_depths/OnObjectCreate, instance_blackrock_depths/SetData, instance_blackrock_spire/OnEffectExecute, instance_deadmines/SetData, instance_gnomeregan/SetData, instance_scholomance/OnGameObjectCreate, instance_stratholme/OnGameObjectCreate, instance_stratholme/UpdateGoState, instance_temple_of_ahnqiraj/OnObjectCreate, instance_uldaman/OnObjectCreate, instance_zulfarrak/OnObjectCreate, Map.ScriptCommands/ScriptCommand_CloseDoor, Map.ScriptCommands/ScriptCommand_OpenDoor, ScriptedInstance/DoOpenDoor, ScriptedInstance/DoUseDoorOrButton, silithus/HandleOpeningStage, Spell.Effects/EffectActivateObject, swamp_of_sorrows/WaypointStart, WorldSession.LootHandler/DoLootRelease, zulfarrak/MovementInform | — |
| SwitchDoorOrButton | method | WorldObject.Object/RemoveFlag, WorldObject.Object/SetFlag | — | — |
| Use | method | BattleGround/EventPlayerClickedOnFlag, GameObjectAI/OnUse, GameObjectInfo/CannotBeUsedUnderImmunity, GameObjectInfo/GetAutoCloseTime, GameObjectInfo/GetCharges, GameObjectInfo/GetCooldown, GameObjectInfo/IsUsableMounted, Group/GetId, Group/IsMember, Log.Main/HasLogFilter, Log.Main/HasLogLevelOrHigher, Log.Main/Out, Map.Main/GetPlayer, Map.Main/ScriptsStart, Object/GetEntry, Object/GetGUIDLow, Object/GetObjectGuid, Object/GetTypeId, Object/HasFlag, Object/IsPlayer, Object/ToGameObject, Object/ToUnit, ObjectGuid/operator!, ObjectGuid/operator!=, ObjectGuid/operator<<, ObjectMgr/GetFishingBaseSkillLevel, ObjectMgr/GetPlayer, ObjectMgr/GetQuestTemplate, Player.Main/CanUseBattleGroundObject, Player.Main/GetBattleGround, Player.Main/GetGroup, Player.Main/GetQuestStatus, Player.Main/GetSession, Player.Main/GetSkillValue, Player.Main/IsInSameRaidWith, Player.Main/PrepareGossipMenu, Player.Main/RewardPlayerAndGroupAtCast, Player.Main/SendCinematicStart, Player.Main/SendLoot, Player.Main/SendPreparedGossip, Player.Main/UpdateFishingSkill, ScriptMgr/OnGameObjectUse, ScriptMgr/OnGossipHello#2, ScriptMgr/OnProcessEvent, shared_Util/irand, Spell.Main/prepare, Spell.Main/Spell, Spell.Main/Spell#2, SpellCaster/CastSpell#2, SpellCaster/FinishSpell, SpellCaster/GetCurrentSpell, SpellCastTargetsInfo/setGOTarget, SpellCastTargetsInfo/setUnitTarget, SpellCastTargetsInfo/SpellCastTargets, SpellMgr/GetSpellEntry, SpellMgr/Instance, Unit.Main/IsMounted, Unit.Main/NearTeleportTo, Unit.Main/RemoveGameObject, Unit.Main/RemoveSpellsCausingAura, Unit.Main/SetStandState, World/getConfig, World/GetGameTime, WorldObject.Object/GetMap, WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/GetZoneAndAreaId, WorldObject.Object/IsWithinLOSInMap, WorldObject.Object/SetFlag, WorldObject.Object/SetUInt32Value, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | BattleBotAI.Main/UpdateBattleGroundAI, boss_cannon_master_willey/GO_scarlet_cannon, boss_heigan/UpdateEruption, ChatHandler.ObjectCommands/HandleGameObjectUseCommand, ChatHandler.PlayerBotMgr/HandlePartyBotUseGObjectHelper, felwood/QuestAccept_npc_captured_arkonarin, felwood/WaypointReached#2, instance_gnomeregan/SetData, instance_molten_core/RemoveRuneFire, instance_sunken_temple/ProcessStatueUsed, Map.ScriptCommands/ScriptCommand_ActivateGameObject, silithus/Larksbane_DoAction, Spell.Effects/EffectActivateObject, Spell.Effects/SendLoot, ThreatListCopier.boss_ragnaros/DoLavaBurst, WorldSession.SpellHandler/HandleGameObjectUseOpcode | — |
| GetNameForLocaleIdx | method | Object/GetEntry, ObjectMgr/GetGameObjectLocale | — | — |
| UpdateRotationFields | method | WorldObject.Object/GetOrientation, WorldObject.Object/SetFloatValue | ChatHandler.ObjectCommands/HandleGameObjectTurnCommand | — |
| IsHostileTo | method | FactionTemplateEntry/IsHostileTo, Object/GetTypeId, Object/ToUnit#2, ObjectMgr/GetFactionEntry, Player.Main/GetReputationMgr, Player.Main/GetReputationMgr#2, Player.Main/IsGameMaster, ReputationMgr/GetForcedRankIfAny, ReputationMgr/GetRank, Unit.Main/GetCharmerOrOwner, Unit.Main/IsHostileTo, WorldObject.Object/GetFactionTemplateEntry | ThreatListCopier.battleground_alterac/OnUse#2 | — |
| IsFriendlyTo | method | FactionTemplateEntry/IsFriendlyTo, Object/GetTypeId, Object/ToUnit#2, ObjectMgr/GetFactionEntry, Player.Main/GetReputationMgr, Player.Main/GetReputationMgr#2, Player.Main/IsGameMaster, ReputationMgr/GetForcedRankIfAny, ReputationMgr/GetRank, Unit.Main/GetCharmerOrOwner, Unit.Main/IsFriendlyTo, WorldObject.Object/GetFactionTemplateEntry | — | — |
| IsUseRequirementMet | method | Map.Main/GetCreature, Map.Main/GetGameObject, Object/GetObjectGuid, ObjectMgr/GetGameObjectUseRequirement, Unit.Main/IsAlive, WorldObject.Object/GetMap | Spell.Main/CheckCast | — |
| PlayerCanUse | method | GameObjectInfo/GetLockId, Player.Main/HasItemCount, Player.Main/IsGameMaster, WorldObject.Object/GetDistance#4, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ | WorldSession.SpellHandler/HandleGameObjectUseOpcode | — |
| SetLootState | method | — | AiBotAI.Bridge/BridgeHandleUseGameObject, ChatHandler.ObjectCommands/HandleGameObjectSetLootStateCommand, dreadsteed_ritual/EventSecondPartStart, game_Battlegrounds_BattleGround/DoorClose, game_Battlegrounds_BattleGround/DoorOpen, game_Battlegrounds_BattleGround/HandleTriggerBuff, game_Battlegrounds_BattleGround/SpawnBGObject, go_scripts/GOHello_go_silithyste, instance_dire_maul/OnUse, instance_razorfen_downs/SetData, instance_scarlet_monastery/SetData, Map.ScriptCommands/ScriptCommand_DespawnGameObject, Map.ScriptCommands/ScriptCommand_RemoveObject, Map.ScriptCommands/ScriptCommand_RespawnGameObject, npcs_special/UpdateAI#9, Player.Main/SendLoot, razorfen_downs/UpdateEscortAI, Spell.Effects/EffectActivateObject, Spell.Effects/EffectDummy, Spell.Effects/EffectSummonObject, Spell.Effects/SendLoot, spell_special/OnAfterApply#2, stratholme/OnUse, WorldSession.LootHandler/DoLootRelease | — |
| SetGoState | method | WorldObject.Object/SetUInt32Value | blackrock_depths/DoGate, blackrock_depths/UpdateEscortAI#4, blackrock_depths/WaypointReached#5, boss_celebras_the_cursed/WaypointReached, boss_gothik/OpenTheGate, boss_marli/Aggro, boss_marli/Reset, boss_marli/UpdateAI, boss_thaddius/HandleCheckSpawnAdd, boss_thaddius/HandleUnsummonCoil, ChatHandler.ObjectCommands/HandleGameObjectSetGoStateCommand, dreadsteed_ritual/BreakNode, dreadsteed_ritual/EventEndedFail, dreadsteed_ritual/EventSecondPartStart, dreadsteed_ritual/gobjNextStep, dreadsteed_ritual/GOHello_go_ritual_node, dreadsteed_ritual/PhaseTwoEndedSuccess, feralas/BeginEvent, game_Battlegrounds_BattleGround/SpawnBGObject, hinterlands/SetInUse, hinterlands/UpdateAI, instance_blackfathom_deeps/OnObjectCreate, instance_blackfathom_deeps/OnUse, instance_blackrock_depths/OnObjectCreate, instance_blackrock_spire/OnObjectCreate, instance_blackwing_lair/OnObjectCreate, instance_blackwing_lair/OnUse#2, instance_blackwing_lair/RestoreGo, instance_dire_maul/OnObjectCreate, instance_maraudon/OnGameObjectCreate, instance_naxxramas.Main/OnObjectCreate, instance_naxxramas.Main/SetData, instance_naxxramas.Main/SetTeleporterVisualState, instance_naxxramas.Main/ToggleKelThuzadWindows, instance_naxxramas.Main/UpdateAutomaticBossEntranceDoor#2, instance_naxxramas.Main/UpdateBossGate#2, instance_naxxramas.Main/UpdateTeleporters, instance_razorfen_kraul/OnObjectCreate, instance_scarlet_monastery/SetData, instance_shadowfang_keep/OnObjectCreate, instance_stratholme/SetData, instance_stratholme/Update, instance_stratholme/UpdateGoState, instance_sunken_temple/OnObjectCreate, instance_uldaman/OnObjectCreate, instance_uldaman/SetData, Map.ScriptCommands/ScriptCommand_SetGoState, Player.Main/SendLoot, silithus/AnimateAQGate, silithus/ResetAQGates, Spell.Effects/SendLoot, Spell.Main/update, spell_special/OnAfterApply#2, tanaris/SetInUse, tanaris/UpdateAI, Transport/Create#2, WorldSession.LootHandler/DoLootRelease | — |
| SetDisplayId | method | WorldObject.Object/SetUInt32Value | Transport/Create#2 | — |
| GetObjectBoundingRadius | method | — | — | — |
| IsInSkillupList | method | Object/GetObjectGuid | Spell.Effects/EffectOpenLock | — |
| AddToSkillupList | method | Object/GetObjectGuid | Spell.Effects/EffectOpenLock | — |
| AddGameObjectToRemoveListInMapsWorker | ctor | — | — | — |
| operator() | method | Map.Main/GetGameObject, WorldObject.Object/AddObjectToRemoveList | — | — |
| AddToRemoveListInMaps | method | ObjectGuid/ObjectGuid#3 | GameEventMgr.Main/GameEventUnspawn | — |
| SpawnGameObjectInMapsWorker | ctor | — | — | — |
| operator()#4 | method | Errors/PrintStacktraceAndThrow, Log.Main/Out, Map.Main/GetGameObject, Map.Main/IsLoaded, ObjectGuid/ObjectGuid#3, ObjectMgr/GetGOData | — | — |
| SpawnInMaps | method | — | GameEventMgr.Main/GameEventSpawn | — |
| HasStaticDBSpawnData | method | Object/GetGUIDLow, ObjectMgr/GetGOData | game_Battlegrounds_BattleGround/SpawnBGObject | — |
| UpdateCollisionState | method | GameObjectModel/enable, Object/IsInWorld | — | — |
| UpdateModel | method | GameObjectModel/construct, Map.Main/ContainsGameObjectModel, Map.Main/InsertGameObjectModel, Map.Main/RemoveGameObjectModel, Object/IsInWorld, WorldObject.Object/GetMap | — | — |
| UpdateModelPosition | method | GameObjectModel/Relocate, Map.Main/ContainsGameObjectModel, Map.Main/InsertGameObjectModel, Map.Main/RemoveGameObjectModel, WorldObject.Object/GetMap | Transport/Update, Transport/UpdatePosition | — |
| GetLosCheckPosition | method | GameObjectModel/getBounds, Object/GetObjectScale, WorldObject.Object/GetPosition#2, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ | — | — |
| GetGOData | method | Object/GetGUIDLow, ObjectMgr/GetGOData | felwood/PlantQuestRewarded, go_scripts/UpdateAI#5, ThreatListCopier.battleground_alterac/go_av_landmineAI | — |
| HasCustomAnim | method | — | — | — |
| SendGameObjectCustomAnim | method | ByteBuffer/operator<<#10, Object/GetObjectGuid, ObjectGuid/operator<<, WorldObject.Object/SendMessageToSet, WorldPacket/WorldPacket#4 | azshara/UpdateAI, boss_heigan/UpdateEruption, boss_ouro/OnUse, ChatHandler.ObjectCommands/HandleGameObjectSendCustomAnimCommand, dreadsteed_ritual/gobjNextStep, instance_blackwing_lair/ApplyAura, instance_dire_maul/UpdateAI#8, instance_maraudon/SpewLarva, Map.ScriptCommands/ScriptCommand_PlayCustomAnim, OutdoorPvPEP/UpdateBannerArt, OutdoorPvPEP/UpdateBannerArt#2, OutdoorPvPEP/UpdateBannerArt#3, OutdoorPvPEP/UpdateBannerArt#4, Spell.Effects/EffectActivateObject, Unit.SpellAuras/HandlePeriodicTriggerSpell | — |
| SendGameObjectReset | method | Object/GetObjectGuid, ObjectGuid/operator<<, WorldObject.Object/SendMessageToSet, WorldPacket/WorldPacket#4 | — | — |
| Despawn | method | GameObjectData/GetRandomRespawnTime, WorldObject.Object/AddObjectToRemoveList, WorldObject.Object/SendObjectDeSpawnAnim | boss_urok/EventBannerDestroyed, ChatHandler.ObjectCommands/HandleGameObjectDespawnCommand, dreadsteed_ritual/EventEndedFail, dreadsteed_ritual/PhaseTwoEndedSuccess, dustwallow_marsh/UpdateAI#6, eastern_plaguelands/EffectDummyGameObj_go_mark_of_detonation, felwood/PlantQuestRewarded, go_scripts/UpdateAI#2, go_scripts/UpdateAI#4, go_scripts/UpdateAI#5, scourge_invasion/DespawnNecropolis, scourge_invasion/SummonCultists, silithus/OnActivateBySpell, Spell.Effects/EffectActivateObject, ThreatListCopier.battleground_alterac/OnUse#2, world_event_wareffort/HandleSupplyObjectSpawn | — |
| GetLevel | method | Object/GetUInt32Value | — | — |
| IsAtInteractDistance#2 | method | GameObjectInfo/GetInteractionDistance, WorldObject.Object/GetDistance3dToCenter#3, WorldObject.Object/GetPosition#3 | Player.Main/CanInteractWithGameObject, Player.Main/SendLoot, Spell.Main/CheckRange, WorldSession.SpellHandler/HandleGameObjectUseOpcode | — |
| GetClosestChairSlotPosition | method | Geometry/GetDistance2D, WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY | — | — |
| IsAtInteractDistance | method | Object/GetObjectScale, WorldObject.Object/GetDistance3dToCenter, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ | — | — |
| GetSpellForLock | method | GameObjectInfo/GetLockId, Player.Main/GetSpellMap#2, SpellCaster/CalculateSpellEffectValue, SpellMgr/GetSpellEntry, SpellMgr/Instance | — | — |
| GetLocalRotation | method | Object/GetFloatValue, QuaternionData/QuaternionData#2 | Transport/Update | — |
| CanAggroWhenOpening | method | GameObjectInfo/GetLockId, WorldObject.Object/GetFactionId | — | — |
| DoAggroWhenOpening | method | AnyHostileUnitInObjectRangeCheck/AnyHostileUnitInObjectRangeCheck, Creature.Main/EnterCombatWithTarget, ObjectGuid/IsPlayer, Unit.Main/GetCharmerGuid, Unit.Main/GetOwnerGuid, Unit.Main/HasUnitState, WorldObject.Object/IsValidAttackTarget, WorldObject.Object/IsWithinLOSInMap | Spell.Main/OnSpellLaunch | — |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `game_event_gameobject`: guid int(10) unsigned PK, event smallint(6) PK
- `gameobject`: guid int(10) unsigned PK, id mediumint(8) unsigned, map smallint(5) unsigned, position_x float, position_y float, position_z float, orientation float, rotation0 float, rotation1 float, rotation2 float, rotation3 float, spawntimesecsmin int(11), spawntimesecsmax int(11), animprogress tinyint(3) unsigned, state tinyint(3) unsigned, spawn_flags int(10) unsigned, visibility_mod float?, patch_min tinyint(3) unsigned, patch_max tinyint(3) unsigned
- `gameobject_battleground`: guid int(10) unsigned PK, event1 tinyint(3) unsigned PK, event2 tinyint(3) unsigned PK

*`?` = nullable, `PK` = primary key column.*


---

<!-- verify: failed-members | invented: operator -->
