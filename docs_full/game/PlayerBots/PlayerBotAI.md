# PlayerBotAI

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# PlayerBotAI

**Purpose & Responsibilities**

`PlayerBotAI` and its derived classes constitute the artificial intelligence framework for non-player-controlled characters ("bots") within the `wowvmangos` server. Unlike standard creature NPCs, these bots are implemented as full `Player` objects, allowing them to possess player-specific mechanics such as inventory, social relationships, and complex spellcasting.

The base class `PlayerBotAI` provides the foundational lifecycle management for these entities, including session initialization, teleportation handling, and a generic update loop. It also contains `SpawnNewPlayer`, a critical factory method responsible for constructing a valid `Player` object in memory, binding it to a `WorldSession`, and inserting it into the game world.

Specialized subclasses implement specific behavioral archetypes:
*   **`MageOrgrimmarAttackerAI`**: A combat-oriented bot configured as a Level 60 Gnome Mage, designed to patrol and attack targets in Orgrimmar.
*   **`PopulateAreaBotAI`**: A passive population bot designed to spawn within a defined radius of a coordinate to simulate crowd density.
*   **`PlayerBotFleeingAI`**: A simple bot that immediately flees upon login.
*   **`AiBotAI`**: Referenced in the factory function `CreatePlayerBotAI` (defined in `AiBotAI.cpp`), this represents a configurable general-purpose bot.

The unit does not interact directly with database tables; all data persistence and retrieval are delegated to other units like `MasterPlayer` and `SocialMgr`.

## Member-by-Member Behavior

### Lifecycle and Session Management

**`PlayerBotAI` (Constructor)**
Initializes the base AI structure. It sets the internal `botEntry` pointer to `nullptr`. This constructor is typically invoked by the factory function `CreatePlayerBotAI` or by derived classes via their initializer lists.

**`~PlayerBotAI` (Destructor)**
Calls `Remove()` to ensure clean detachment of the AI from the associated `Player` object before destruction. This prevents dangling pointers if the `Player` object outlives the AI instance.

**`OnSessionLoaded`**
Triggered when a bot's session is ready. It delegates the actual login process to `WorldSession::LoginPlayer` using the GUID stored in the `PlayerBotEntry`. This integrates the bot into the server's session management system.

**`OnBotEntryLoad`**
A virtual hook called during the loading phase. The base implementation is empty, allowing derived classes to perform custom initialization based on the `PlayerBotEntry` data.

**`OnPacketReceived`**
A virtual hook for intercepting packets sent to the bot's session. The base implementation is empty. This allows derived classes to react to server-to-client communications without modifying core session logic.

**`OnPlayerLogin`**
A virtual hook called after the player has successfully logged into the world. The base implementation is empty. Derived classes like `PlayerBotFleeingAI` override this to set initial movement states.

**`BeforeAddToMap`**
A virtual hook called before the player object is added to a map instance. The base implementation is empty. Derived classes like `PopulateAreaBotAI` use this to adjust the player's coordinates to fit within a specific population radius. Note that at this stage, the `me` pointer (the `Player` object) is `nullptr`, so this method operates on the `Player*` parameter passed to it.

**`Remove`**
Detaches the AI from the `Player` object. It checks if the `Player`'s current AI is this instance and sets it to `nullptr`. It then clears the internal `me` pointer. This ensures that if the `Player` object persists, it no longer references this AI.

### Teleportation Handling

**`UpdateAI` (Base Class)**
This method handles the completion of teleportation sequences for the bot. Since bots are `Player` objects, they undergo the same teleportation protocols as human players.
*   If the player is being teleported "near" (short distance), it constructs a `MoveTeleportAck` packet and manually processes it via `WorldSession::HandleMoveTeleportAckOpcode`. This simulates the client acknowledging the teleport, allowing the server to finalize the position update.
*   If the player is being teleported "far" (long distance/world portal), it calls `WorldSession::HandleMoveWorldportAck` to complete the sequence.
*   This logic is crucial because bots do not have a real client to send these acknowledgments; the AI must simulate them to prevent the player object from getting stuck in a teleporting state.

### Player Spawning Factory

**`SpawnNewPlayer`**
This is the most complex method in the unit, responsible for creating a fully functional bot player from scratch.
1.  **Name Generation**: Generates a unique player name using `ObjectMgr::GenerateFreePlayerName` and normalizes it.
2.  **Appearance**: If a clone `Player*` is provided, it copies appearance bytes (gender, skin, face, hair, etc.). Otherwise, it randomizes these attributes using `Player::SelectRandomAppearance`.
3.  **Object Creation**: Allocates a new `Player` object bound to the provided `WorldSession`. It calls `Player::Create` with the generated GUID, name, race, class, and appearance data.
4.  **Positioning**: Sets the location map ID, instance ID, and disables auto-instance switching. Initializes the motion master.
5.  **Instance Binding**: If the bot is spawning in an instance (non-continent map), it creates a `DungeonPersistentState` and binds the player to it.
6.  **Map Validation**: Retrieves the `Map` object from `MapManager`. If the map doesn't exist, it logs an error and cleans up.
7.  **Relocation**: Moves the player to the specified coordinates `(x, y, z, o)`.
8.  **World Integration**:
    *   Saves recall position.
    *   Creates a packet broadcaster.
    *   Loads a `MasterPlayer` wrapper and loads social data (friends/guilds) via `SocialMgr`.
    *   Adds the player to the map via `Map::Add`.
    *   Inserts the player into the object cache (`ObjectMgr::InsertPlayerInCache`).
    *   Associates the player with the session (`WorldSession::SetPlayer`).
    *   Adds the player to the global object accessor (`ObjectAccessor::AddObject`).
    *   Enables stat modification and updates all stats (`Player::UpdateAllStats`).

### Specialized AI Behaviors

#### `MageOrgrimmarAttackerAI`

**`OnSessionLoaded` (Override)**
Overrides the base method to spawn a specific character: a Level 60 Gnome Mage at coordinates `(1017.0f, -4450, 12)` in Elwynn Forest (Map ID 1, though the name suggests Orgrimmar, the coordinates are actually in Elwynn/Northshire area, likely a testing ground or misnamed class). It calls `SpawnNewPlayer` with these hardcoded parameters.

**`UpdateAI` (Override)**
Implements a combat and patrol routine for the Mage bot.
1.  **Level Enforcement**: Ensures the bot is always Level 60.
2.  **Death Handling**: If the bot dies, it deletes itself via `PlayerBotMgr::DeleteBot`. Resurrection logic is commented out.
3.  **Combat State**:
    *   Skips logic if casting a non-melee spell or regenerating mana.
    *   Selects the nearest target within a dynamic range (30 yards in combat, 15-30 yards out of combat).
    *   Checks Line of Sight (LOS).
4.  **Mana Management**:
    *   If mana is low (<40) and in combat, it attacks melee and chases the target.
    *   If mana is sufficient (>50), it stops chasing if currently chasing.
5.  **Spell Casting**:
    *   **Frost Nova**: If the target is melee range and immobilized (`UNIT_STATE_CAN_NOT_MOVE`), it casts Frost Nova on itself (likely a typo in logic, casting on self instead of target, or intended to root self? Code shows `me->CastSpell(me, ...)`). Then it moves away from the target to kite.
    *   **Firebolt**: If a target exists and mana is >50, it faces the target, stops moving, and casts Firebolt.
6.  **Regeneration**: If out of combat and mana <150, it casts a regen aura on itself.
7.  **Patrol Movement**:
    *   Uses hardcoded coordinates to define a patrol path around the spawn area.
    *   Calculates random walk positions using `Map::GetWalkRandomPosition`.
    *   Moves to these points using `MovePoint` with pathfinding enabled.

#### `PopulateAreaBotAI`

**`BeforeAddToMap` (Override)**
Ensures the bot spawns within a specific radius of a central point.
*   Checks if the player is on the correct team and map.
*   If outside the radius, it finds a random walkable position within the radius using `Map::GetWalkRandomPosition` and relocates the player there.

**`OnPlayerLogin` (Override)**
Randomly decides whether to enter a "confused" movement state (`MoveConfused`) upon login, adding slight erratic behavior to the population bots.

#### `PlayerBotFleeingAI`

**`OnPlayerLogin` (Override)**
Immediately sets the bot to flee from itself (`MoveFleeing(me)`) and enables God Mode (`SetCheatGod(true)`), making it invulnerable. This is likely a test or debug bot.

### Factory Function

**`CreatePlayerBotAI`**
A standalone factory function that creates the appropriate AI subclass based on a string name.
*   Supports specific named AIs: `MageOrgrimmarAttackerAI`, `IronforgePopulationAI`, `StormwindPopulationAI`, `OrgrimmarPopulationAI`, `PlayerBotFleeingAI`.
*   For `AiBotAI`, it uses provided parameters (race, class, level, coords) or defaults to a Human Warrior at Northshire.
*   Falls back to a generic `PlayerBotAI` if no match is found.

## Cross-Unit Boundaries

*   **`WorldSession`**: `PlayerBotAI` relies heavily on `WorldSession` to manage the bot's connection state. It calls `LoginPlayer` to initiate the session, `HandleMoveTeleportAckOpcode` and `HandleMoveWorldportAck` to simulate client responses, and `SetPlayer`/`SetMasterPlayer` to bind the player object to the session.
*   **`Player`**: The core entity managed by this AI. The AI calls numerous methods on `Player` to manipulate its state: `Create`, `Relocate`, `SetMap`, `UpdateAllStats`, `GiveLevel`, `IsAlive`, `IsInCombat`, `SelectNearestTarget`, `CastSpell`, etc.
*   **`Map` / `MapManager`**: Used to validate map existence, find walkable positions, and add the player to the world. `SpawnNewPlayer` calls `MapManager::FindMap` and `Map::Add`. Patrol logic uses `Map::GetWalkRandomPosition` and `Map::GetWalkHitPosition`.
*   **`ObjectMgr`**: Used for generating unique names (`GenerateFreePlayerName`) and caching the player object (`InsertPlayerInCache`).
*   **`SocialMgr`**: `SpawnNewPlayer` calls `SocialMgr::LoadFromDB` to load friend/guild lists for the bot.
*   **`MotionMaster`**: Controls the bot's movement. Methods like `MoveChase`, `MovePoint`, `MoveFleeing`, `MoveConfused`, and `Initialize` are called to direct the bot's physical actions.
*   **`PlayerBotMgr`**: The manager for all bots. `MageOrgrimmarAttackerAI::UpdateAI` calls `PlayerBotMgr::DeleteBot` to remove itself upon death.
*   **`AiBotAI`**: The factory function `CreatePlayerBotAI` instantiates `AiBotAI` for generic bots. `AiBotAI::UpdateAI` calls `PlayerBotAI::UpdateAI` (base class) to handle teleportation.
*   **`ChatHandler`**: Various chat commands (`AddBot`, `Load`, `DeleteBot`) trigger the creation, loading, and deletion of these AI instances.

## Data Model

This unit does not directly query or modify database tables. All database interactions are performed by called units:
*   `SocialMgr::LoadFromDB` accesses social relationship tables.
*   `MasterPlayer::LoadPlayer` likely accesses player character data.
*   `PlayerBotMgr` likely manages bot configuration data.

## Notable Implementation Details

*   **Simulated Client Acknowledgments**: The `UpdateAI` method in `PlayerBotAI` manually constructs and processes teleport acknowledgment packets. This is necessary because bots lack a real client to send these packets. Failure to do so would leave the bot in a "teleporting" state indefinitely.
*   **Hardcoded Patrol Paths**: `MageOrgrimmarAttackerAI` uses hardcoded coordinates for its patrol route. This makes it inflexible and tied to specific map geometry.
*   **Potential Logic Error in Frost Nova**: In `MageOrgrimmarAttackerAI::UpdateAI`, the code casts `SPELL_FROST_NOVA` on `me` (the bot itself) rather than the target. This is unusual for a combat AI, as Frost Nova typically roots enemies. It might be intended to root the bot in place while kiting, but the comment "Try to kit" suggests it should affect the enemy.
*   **God Mode in Fleeing Bot**: `PlayerBotFleeingAI` enables God Mode, making it immune to damage. This is clearly a debug or test feature.
*   **Memory Management**: `SpawnNewPlayer` carefully handles memory cleanup if any step fails (e.g., map not found, add to map failed), deleting the `Player` object to prevent leaks.
*   **Instance Handling**: `SpawnNewPlayer` explicitly handles instance binding, creating a persistent dungeon state if needed. This allows bots to participate in instanced content.

## Member Reference

**`OnSessionLoaded#2`**: Calls `WorldSession::LoginPlayer` to log the bot into the world using its GUID. Returns `true`.

**`PlayerBotAI`**: Constructor initializes `botEntry` to `nullptr`.

**`~PlayerBotAI`**: Destructor calls `Remove()` to detach from the player.

**`UpdateAI#2`**: Handles teleport completion. If `IsBeingTeleportedNear`, sends `MoveTeleportAck` packet. If `IsBeingTeleportedFar`, calls `HandleMoveWorldportAck`.

**`OnBotEntryLoad`**: Empty virtual hook for derived class initialization.

**`OnPacketReceived`**: Empty virtual hook for packet interception.

**`OnPlayerLogin`**: Empty virtual hook for post-login actions.

**`BeforeAddToMap`**: Empty virtual hook for pre-map-addition adjustments.

**`Remove`**: Detaches AI from `Player` by setting `Player::AI` to `nullptr` and clearing internal `me` pointer.

**`OnPlayerLogin#2`**: (`PlayerBotFleeingAI`) Sets motion to `MoveFleeing` and enables God Mode.

**`SpawnNewPlayer`**: Factory method to create a new `Player` object. Generates name, sets appearance, creates `Player`, binds to session, adds to map, loads social data, and updates stats. Handles errors and cleanup.

**`OnSessionLoaded`**: (`MageOrgrimmarAttackerAI`) Spawns a Gnome Mage at specific coordinates via `SpawnNewPlayer`.

**`UpdateAI`**: (`MageOrgrimmarAttackerAI`) Combat AI. Manages levels, death, target selection, mana, spell casting (Frost Nova, Firebolt), and patrol movement using hardcoded coordinates.

**`BeforeAddToMap#2`**: (`PopulateAreaBotAI`) Adjusts player position to be within a defined radius of a center point.

**`OnPlayerLogin#3`**: (`PopulateAreaBotAI`) Randomly sets motion to `MoveConfused`.

**`CreatePlayerBotAI`**: Factory function that returns an instance of the appropriate AI subclass based on a string name.

---

<!-- machine-true, projected from graph.json -->

## Map — PlayerBotAI

*Source:* PlayerBotAI.cpp, PlayerBotAI.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| OnSessionLoaded#2 | method | ObjectGuid/ObjectGuid#5, WorldSession.CharacterHandler/LoginPlayer | ChatHandler.PlayerBotMgr/Update | — |
| PlayerBotAI | ctor | — | ChatHandler.PlayerBotMgr/AddBot#2 | — |
| ~PlayerBotAI | dtor | — | — | — |
| UpdateAI#2 | method | MoveTeleportAck/MoveTeleportAck, Object/GetObjectGuid, Player.Main/GetSession, Player.Main/IsBeingTeleportedFar, Player.Main/IsBeingTeleportedNear, WorldSession.MovementHandler/HandleMoveTeleportAckOpcode, WorldSession.MovementHandler/HandleMoveWorldportAck | AiBotAI.Main/UpdateAI | — |
| OnBotEntryLoad | method | — | ChatHandler.PlayerBotMgr/Load | — |
| OnPacketReceived | method | — | WorldSession.Main/SendMovementPacket, WorldSession.Main/SendPacket | — |
| OnPlayerLogin | method | — | ChatHandler.PlayerBotMgr/OnPlayerInWorld | — |
| BeforeAddToMap | method | — | Player.Main/LoadFromDB | — |
| Remove | method | Player.Main/AI, Player.Main/SetAI | — | — |
| OnPlayerLogin#2 | method | Creature.MotionMaster/MoveFleeing, Player.Main/SetCheatGod, Unit.Main/GetMotionMaster | — | — |
| SpawnNewPlayer | method | Creature.MotionMaster/Initialize, Errors/PrintStacktraceAndThrow, Log.Main/Out, Map.Main/Add#3, MapManager/FindMap, MapPersistentStateMgr/AddPersistentState, MasterPlayer.Main/LoadPlayer, MasterPlayer.Main/MasterPlayer, MasterPlayer.Main/SetSocial, Object/GetByteValue, Object/GetObjectGuid, ObjectAccessor/AddObject#3, ObjectMgr/GenerateFreePlayerName, ObjectMgr/InsertPlayerInCache, ObjectMgr/normalizePlayerName, Player.Main/BindToInstance, Player.Main/Create, Player.Main/CreatePacketBroadcaster, Player.Main/Player#5, Player.Main/SaveRecallPosition, Player.Main/SelectRandomAppearance, Player.Main/SetAutoInstanceSwitch, Player.StatSystem/UpdateAllStats#3, shared_Util/urand, SocialMgr/LoadFromDB, Unit.Main/GetMotionMaster, Unit.Main/SetCanModifyStats, WorldObject.Object/GetMap, WorldObject.Object/Relocate#2, WorldObject.Object/SetLocationInstanceId, WorldObject.Object/SetLocationMapId, WorldObject.Object/SetMap, WorldSession.Main/SetMasterPlayer, WorldSession.Main/SetPlayer | AiBotAI.Main/OnSessionLoaded, PartyBotAI/OnSessionLoaded | — |
| OnSessionLoaded | method | — | — | — |
| UpdateAI | method | ChatHandler.PlayerBotMgr/DeleteBot#2, Creature.MotionMaster/GetCurrentMovementGeneratorType, Creature.MotionMaster/MoveChase, Creature.MotionMaster/MovePoint, Map.Main/GetWalkHitPosition, Map.Main/GetWalkRandomPosition, MotionMaster/MovementExpired, MoveSpline/Finalized, Object/GetGUIDLow, Player.Main/GiveLevel, shared_Util/frand, shared_Util/urand, SpellCaster/CastSpell#2, SpellCaster/IsNonMeleeSpellCasted, SpellCaster/IsSpellReady#2, Unit.Main/Attack, Unit.Main/CanReachWithMeleeAutoAttack, Unit.Main/GetLevel, Unit.Main/GetMaxPower, Unit.Main/GetMotionMaster, Unit.Main/GetObjectBoundingRadius, Unit.Main/GetPower, Unit.Main/HasAura#2, Unit.Main/HasUnitState, Unit.Main/IsAlive, Unit.Main/IsInCombat, Unit.Main/SelectNearestTarget, Unit.Main/SetFacingToObject, Unit.Main/StopMoving, WorldObject.Object/GetDistance#3, WorldObject.Object/GetMap, WorldObject.Object/GetOrientation, WorldObject.Object/GetPosition#2, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/IsWithinLOSInMap, WorldObject.Object/UpdateGroundPositionZ | — | — |
| BeforeAddToMap#2 | method | Map.Main/GetWalkRandomPosition, MapManager/CreateMap, Player.Main/GetTeam, WorldObject.Object/GetInstanceId, WorldObject.Object/GetMapId, WorldObject.Object/IsWithinDist3d, WorldObject.Object/Relocate, WorldObject.Object/SetLocationMapId | — | — |
| OnPlayerLogin#3 | method | Creature.MotionMaster/MoveConfused, shared_Util/urand, Unit.Main/GetMotionMaster | — | — |
| CreatePlayerBotAI | function | AiBotAI.Main/AiBotAI, Log.Main/Out, MageOrgrimmarAttackerAI/MageOrgrimmarAttackerAI, MapManager/GetContinentInstanceId, PlayerBotFleeingAI/PlayerBotFleeingAI, PopulateAreaBotAI/PopulateAreaBotAI | ChatHandler.PlayerBotMgr/Load | — |
