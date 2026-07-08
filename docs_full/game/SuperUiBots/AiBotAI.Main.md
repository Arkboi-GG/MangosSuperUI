# AiBotAI.Main

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# AiBotAI.Main

## Purpose & Responsibilities

`AiBotAI.Main` (implemented in `AiBotAIMain.cpp` and declared in `AiBotAIMain.h`) constitutes the central orchestration layer for the autonomous player bot in the VMaNGOS server environment. It serves as the primary entry point for the bot's lifecycle, managing the transition from database persistence to in-world activity, and driving the main behavioral loop (`UpdateAI`).

Its core responsibilities are:
1.  **Lifecycle Management:** Handling character creation, login, session loading, and initialization (gear, spells, channels).
2.  **Main Loop Orchestration:** Executing the tick-based `UpdateAI` function, which coordinates state transitions between idle, movement, combat, grinding, and recovery states.
3.  **Bridge Communication:** Maintaining a TCP connection to an external C# coordinator ("BotBrainService"), sending state/events, and receiving commands.
4.  **Packet Interception:** Overriding standard network packet handlers to implement intelligent loot rolling and chat message parsing.
5.  **Movement Callbacks:** Handling completion events for pathfinding segments via `MovementInform`.

This unit does not contain the detailed logic for combat mechanics, pathfinding algorithms, or loot scoring; instead, it delegates these to sibling translation units (`AiBotAICombat`, `AiBotAIMovement`, `AiBotAILoot`, etc.) while maintaining the global state and control flow.

## Member-by-Member Behavior

### Lifecycle and Initialization

**`OnPlayerLogin`**
Called when the bot character logs into the world. It logs the event, ensures the `UNIT_FLAG_SPAWNING` flag is set if the bot is not yet initialized (preventing interaction during setup), and forces a save to the database to ensure persistence across server restarts.

**`OnSessionLoaded`**
The critical entry point for bot instantiation. It determines whether the bot is a fresh spawn or a restart:
*   **Restart Path:** If a record exists in the `characters` table for the bot's GUID, it delegates to `WorldSession::LoginPlayer` to load the existing character state.
*   **Fresh Spawn Path:** If no record exists, it cleans up any residual data in related tables (`character_spell`, `character_skills`, etc.), removes the player from the object cache, and calls `PlayerBotAI::SpawnNewPlayer` to create the character. It then applies optional customizations: renaming the character if a name was specified, and setting the level if requested.

**`AiBotAI` (Constructor)**
Initializes the bot with spawn parameters (race, class, level, coordinates). It resets the update timer and logs the creation event. It does not establish the bridge connection or initialize game state; this happens later in `UpdateAI`.

**`~AiBotAI` (Destructor)**
Ensures the TCP bridge connection is closed when the bot object is destroyed.

### Main Behavioral Loop

**`UpdateAI`**
The heart of the bot's intelligence, executed every `AIBOT_UPDATE_INTERVAL` (1000ms). It performs the following sequence:
1.  **Timers & Bridge:** Updates internal timers, manages the TCP bridge connection (connect, send hello, receive commands, flush sends, send periodic state).
2.  **Initialization:** On the first tick, it equips starting gear, assigns a role, populates spell data, joins zone-specific chat channels, and marks the bot as initialized.
3.  **Death Handling:** If dead, it handles ghosting, corpse management, and potential self-resurrection at a graveyard (`GRAVE-SELFREZ` logic). It waits for a resurrection command from the bridge or executes a timed self-rez.
4.  **State Checks:** Handles taxi flights (skipping other logic while flying), level-up detection (refreshing spells/skills), and crowd-control breaking.
5.  **Recovery (Eat/Drink):** Implements a hysteresis latch for health/mana recovery. If HP/mana drops below thresholds, the latch engages, forcing the bot to eat/drink until high thresholds are met, preventing "death spirals" where the bot fights at low health.
6.  **Out-of-Combat Logic:**
    *   **Grind Tasks:** If assigned a `TASK_GRIND`, it acquires targets via the active doctrine (Solo/TeamAuto/Directed). It implements "pull discipline," refusing to engage if under-resourced or if the target is in a dense cluster (overpull protection). If stuck (freeze), it self-unsticks after a dwell period.
    *   **Move-To Tasks:** Resumes pathing if interrupted. Performs an "approach scan" for objectives. If the destination is reached, it completes the task or converts to a grind if an objective creature was specified.
    *   **Idle:** Performs random wandering if no task is active.
7.  **In-Combat Logic:**
    *   Handles stalemates (unreachable targets) and overpull retreats.
    *   Validates targets, respecting tap rules (ignoring mobs tapped by other players/groups).
    *   Delegates to doctrine-specific target maintenance (e.g., focusing the anchor's target in team play).
    *   Calls class-specific combat updates (`UpdateInCombatAI`).

### Network and Packet Handling

**`OnPacketReceived`**
Intercepts server-to-client packets before they are processed by the base AI:
*   **Loot Rolls (`SMSG_LOOT_START_ROLL`):** Overrides the default "pass" behavior. It parses the item ID, checks if the bot can equip it, and compares it against current gear using `ScoreItem`. It votes "Need" if it's an upgrade, otherwise "Greed".
*   **Chat Messages (`SMSG_MESSAGECHAT`):** Parses Say, Whisper, and Channel messages. It extracts the sender and text, logs them, and forwards them to the bridge via `SendChatRecvEvent` so the external coordinator can react to chat commands.

**`MovementInform`**
Callback triggered when the bot reaches a movement point. It handles specific point types:
*   **Wander/Stalemate/Retreat:** Sets the bot to idle.
*   **Task Destination:** Checks if path chunks remain. If the path is complete, it verifies proximity to the final goal. If close enough, it signals task completion to the bridge. If it was an objective move and no target was found, it converts to a local grind.

### Doctrine and Collaboration

**`RefreshDoctrine`**
Called at the start of each `UpdateAI` tick. It resolves the current engagement doctrine (Solo, TeamAuto, or Directed) based on the bot's state and group composition. If the doctrine type changes, it creates a new doctrine instance, resetting any transient state (like sticky-assist counters).

**`GetBotPlayer`**
A simple accessor returning the `Player` pointer (`me`). Used by external units like `AiBotDoctrine` to access the bot's character data.

## Cross-Unit Boundaries

`AiBotAI.Main` acts as the controller, delegating specialized tasks to other units:

*   **`AiBotAI.Bridge` (AiBotAIBridge.cpp):**
    *   *Calls:* `BridgeConnect`, `BridgeSendHello`, `BridgeRecv`, `BridgeFlush`, `BridgeSendState`, `SendKillEvent`, `SendLevelUpEvent`, `SendChatRecvEvent`.
    *   *Purpose:* Manages the TCP socket lifecycle and serializes/deserializes JSON commands/events between the C++ server and the C# coordinator.
*   **`AiBotAI.Combat` (AiBotAICombat.cpp):**
    *   *Calls:* `AttackStart`, `SelectAttackTarget`, `DrinkAndEat`, `HandleCombatStalemate`, `HandleOverpullRetreat`, `CheckForUnreachableTarget`, `UpdateInCombatAI`, `UpdateOutOfCombatAI`, etc.
    *   *Purpose:* Executes the actual combat mechanics, spell casting, and target selection logic.
*   **`AiBotAI.Movement` (AiBotAIMovement.cpp):**
    *   *Calls:* `MoveToDestination`, `StartNextPathChunk`, `ClearStoredPath`, `DoRandomWander`, `StopMoving`.
    *   *Purpose:* Handles pathfinding, navmesh interaction, and movement generation.
*   **`AiBotAI.Grind` (AiBotAIGrind.cpp):**
    *   *Calls:* `SelectGrindTarget`, `DoGrindPatrol`, `CountNearbyHostiles`, `ScanApproachTarget`, `ConvertMoveToGrindInPlace`.
    *   *Purpose:* Manages area grinding logic, including target prioritization and patrol patterns.
*   **`AiBotAI.Loot` (AiBotAILoot.cpp):**
    *   *Calls:* `ScoreItem`, `DoAutoLoot`.
    *   *Purpose:* Evaluates item quality and performs automatic looting/equipping.
*   **`AiBotDoctrine` (AiBotDoctrine.cpp/h):**
    *   *Calls:* `ResolveDoctrine`, `MakeDoctrine`, `AcquireTarget`, `MaintainTarget`, `HoldPull`, `HoldingForTeam`.
    *   *Purpose:* Provides strategic decision-making for group combat, determining who should attack whom and when to hold fire.
*   **`CombatBotBaseAI` (Base Class):**
    *   *Calls:* `OnPacketReceived` (fallback), `UpdateAI` (base tick), `IsValidHostileTarget`, `BreakCrowdControlEffects`, `AutoEquipGear`, `LearnPremadeSpecForClass`, etc.
    *   *Purpose:* Provides foundational AI behaviors inherited from the original BattleBotAI, such as basic spell handling and gear management.

## Data Model

`AiBotAI.Main` interacts with the following database tables, primarily during the `OnSessionLoaded` phase:

*   **`characters`**: Queried to determine if the bot already exists (restart vs. fresh spawn). Contains core character data (GUID, name, level, position, etc.).
*   **`character_spell`**: Deleted during fresh spawn to ensure a clean slate for spell learning.
*   **`character_skills`**: Deleted during fresh spawn.
*   **`character_reputation`**: Deleted during fresh spawn.
*   **`character_homebind`**: Deleted during fresh spawn.
*   **`character_action`**: Deleted during fresh spawn.

No other tables are directly queried or modified by this unit.

## Notable Implementation Details

1.  **Hysteresis Recovery Latch (`m_eatRecoveryLatch`):**
    Unlike simple threshold checks, the bot uses a latch for health/mana recovery. Once HP/mana drops below `AIBOT_EAT_ENTER_HP` (40%) or `AIBOT_EAT_ENTER_MANA` (20%), the latch engages. The bot will *only* stop recovering and resume tasks when HP/mana exceeds `AIBOT_EAT_EXIT_HP` (90%) or `AIBOT_EAT_EXIT_MANA` (85%). This prevents the bot from oscillating between fighting and eating at low health, which previously led to frequent deaths.

2.  **Self-Unsticking Grinds:**
    In dense mob areas, the bot might refuse to pull due to overpull guards. Previously, this would signal a block to the C# coordinator, which often re-issued the same task, causing a livelock. Now, after `AIBOT_GRIND_FREEZE_DWELL` ticks, the bot forcibly pulls the least-clustered target ("self-unstick"), relying on in-combat retreat logic if the pull fails.

3.  **Intelligent Loot Rolling:**
    The bot intercepts `SMSG_LOOT_START_ROLL` packets. It calculates a score for the offered item compared to currently equipped gear. It votes "Need" only if the new item is an upgrade; otherwise, it votes "Greed". This requires careful parsing of the packet structure and access to item prototypes.

4.  **Doctrine Swapping:**
    The engagement doctrine (Solo/TeamAuto/Directed) is re-evaluated every tick. If the doctrine type changes, the old instance is discarded and a new one created. This ensures that transient state (like sticky-assist counters) is reset when the bot's role in the group changes.

5.  **Chunked Pathing:**
    Long paths are broken into ~200-yard chunks. `MovementInform` handles the transition between chunks, ensuring the bot doesn't lose path validity over long distances.

6.  **Graveyard Self-Resurrection:**
    If the bot dies and is teleported to a graveyard, it waits for a brief period or confirmation of landing before resurrecting itself. This avoids race conditions where the bot might resurrect before the teleport completes, leaving it stuck in the death zone.

## Member Reference

**`OnPlayerLogin`**
Logs the login event, sets the spawning flag if uninitialized, and saves the character to the database.

**`OnSessionLoaded`**
Determines if the bot is a restart or fresh spawn. For restarts, it loads the existing character. For fresh spawns, it cleans up related database records, creates the player, and applies optional name/level settings.

**`OnPacketReceived`**
Intercepts loot roll packets to vote intelligently (Need/Greed) based on item upgrades. Intercepts chat packets to parse and forward messages to the bridge. Falls back to base class handling for other packets.

**`AiBotAI`**
Constructor that initializes spawn parameters and resets the update timer.

**`MovementInform`**
Handles movement completion events. Manages path chunk progression, task completion signaling, and conversion to grind mode if appropriate.

**`~AiBotAI`**
Destructor that disconnects the TCP bridge.

**`GetBotPlayer`**
Returns the `Player` pointer associated with this bot.

**`RefreshDoctrine`**
Re-evaluates the current engagement doctrine (Solo/TeamAuto/Directed) and swaps the instance if the type has changed.

**`UpdateAI`**
The main behavioral loop. Manages timers, bridge communication, initialization, death/resurrection, recovery (eat/drink), out-of-combat tasks (grind/move/wander), and in-combat logic (target validation, stalemate handling, doctrine execution).

**`ValidateWideBowCandidate`**
Declaration only; implementation resides in another unit (likely `AiBotAIMovement`).

---

<!-- machine-true, projected from graph.json -->

## Map — AiBotAI.Main

*Source:* AiBotAIMain.cpp, AiBotAIMain.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| OnPlayerLogin | method | Log.Main/Out, Object/GetGUIDLow, Player.Main/GetName, Player.Main/SaveToDB, WorldObject.Object/SetFlag | — | — |
| OnSessionLoaded | method | Database/PExecute#2, Database/PQuery, Log.Main/Out, Object/GetGUIDLow, ObjectGuid/ObjectGuid#5, ObjectMgr/DeletePlayerFromCache, ObjectMgr/InsertPlayerInCache, Player.Main/GetName, Player.Main/GiveLevel, Player.Main/SetName, PlayerBotAI/SpawnNewPlayer, WorldObject.Object/SetUInt32Value, WorldSession.CharacterHandler/LoginPlayer | — | characters, character_action, character_homebind, character_reputation, character_skills, character_spell |
| OnPacketReceived | method | AiBotAI.Bridge/SendChatRecvEvent, AiBotAI.Loot/ScoreItem, ByteBuffer/operator>>, ByteBuffer/operator>>#10, ByteBuffer/operator>>#6, ByteBuffer/operator>>#9, CombatBotBaseAI/OnPacketReceived, game_Objects_Item/GetProto, Log.Main/Out, Object/IsInWorld, ObjectGuid/ObjectGuid, ObjectGuid/ObjectGuid#5, ObjectGuid/operator>>, ObjectMgr/GetItemPrototype, ObjectMgr/GetPlayer, Player.Main/GetGroup, Player.Main/GetItemByPos, Player.Main/GetName, Player.Main/GetSession, Unit.Main/GetClassMask, Unit.Main/GetLevel, Unit.Main/GetRaceMask, WorldPacket/GetOpcode, WorldPacket/WorldPacket#3, WorldSession.Main/QueuePacket | — | — |
| AiBotAI | ctor | — | ChatHandler.PlayerBotMgr/HandleBotAddAiCommand, PlayerBotAI/CreatePlayerBotAI | — |
| MovementInform | method | AiBotAI.Bridge/BridgeSendEvent, AiBotAI.Grind/ConvertMoveToGrindInPlace, AiBotAI.Movement/ClearStoredPath, AiBotAI.Movement/MoveToDestination, AiBotAI.Movement/StartNextPathChunk, AiBotTaskData/Clear, Creature.MotionMaster/MoveIdle, Log.Main/Out, Player.Main/GetName, Unit.Main/GetMotionMaster, WorldObject.Object/GetDistance2d#4, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ | — | — |
| ~AiBotAI | dtor | — | — | — |
| GetBotPlayer | method | — | AiBotDoctrine/ResolveDoctrine, AiBotDoctrineTeam/AcquireTarget, AiBotDoctrineTeam/ResolveFocus | — |
| RefreshDoctrine | method | AiBotDoctrine/MakeDoctrine, AiBotDoctrine/ResolveDoctrine, CombatDirective/IsActive, IEngagementDoctrine/Name, Log.Main/Out, Player.Main/GetName | — | — |
| UpdateAI | method | AiBotAI.Bridge/BridgeConnect, AiBotAI.Bridge/BridgeFlush, AiBotAI.Bridge/BridgeRecv, AiBotAI.Bridge/BridgeSendEvent, AiBotAI.Bridge/BridgeSendHello, AiBotAI.Bridge/BridgeSendState, AiBotAI.Bridge/SendKillEvent, AiBotAI.Bridge/SendLevelUpEvent, AiBotAI.Combat/AttackStart, AiBotAI.Combat/CheckForUnreachableTarget, AiBotAI.Combat/DrinkAndEat, AiBotAI.Combat/HandleCombatStalemate, AiBotAI.Combat/HandleOverpullRetreat, AiBotAI.Combat/SelectAttackTarget, AiBotAI.Combat/UpdateInCombatAI, AiBotAI.Combat/UpdateInCombatAI_Hunter, AiBotAI.Combat/UpdateOutOfCombatAI, AiBotAI.Grind/ConvertMoveToGrindInPlace, AiBotAI.Grind/CountNearbyHostiles, AiBotAI.Grind/DoGrindPatrol, AiBotAI.Grind/ScanApproachTarget, AiBotAI.Loot/DoAutoLoot, AiBotAI.Movement/ClearStoredPath, AiBotAI.Movement/DoRandomWander, AiBotAI.Movement/MoveToDestination, AiBotAI.Movement/StartNextPathChunk, AiBotAI.Movement/StopMoving, AiBotTaskData/Clear, AiBotTaskData/MatchesObjectiveEntry, AreaEntry/GetById, CombatBotBaseAI/AddAllSpellReagents, CombatBotBaseAI/AutoAssignRole, CombatBotBaseAI/AutoEquipGear, CombatBotBaseAI/BreakCrowdControlEffects, CombatBotBaseAI/IsValidHostileTarget, CombatBotBaseAI/LearnPremadeSpecForClass, CombatBotBaseAI/PopulateSpellData, CombatBotBaseAI/ResetSpellData, CombatBotBaseAI/SummonPetIfNeeded, CombatDirective/IsActive, Creature.Main/GetLootRecipient, Creature.Main/GetName, Creature.Main/IsTappedBy, Creature.MotionMaster/GetCurrentMovementGeneratorType, game_Server_Packets_Channel/JoinChannel, Group/IsMember, IEngagementDoctrine/AcquireTarget, IEngagementDoctrine/HoldingForTeam, IEngagementDoctrine/HoldPull, IEngagementDoctrine/MaintainTarget, IEngagementDoctrine/Name, Log.Main/Out, Map.Main/GetCreature, Object/GetEntry, Object/GetGUIDLow, Object/GetObjectGuid, Object/HasFlag, Object/IsCreature, Object/IsInWorld, Object/ToggleFlag, ObjectGuid/Clear, ObjectGuid/IsEmpty, ObjectGuid/ObjectGuid#3, ObjectGuid/operator==, Player.Main/BuildPlayerRepop, Player.Main/GetCorpse, Player.Main/GetGroup, Player.Main/GetName, Player.Main/GetSession, Player.Main/GetTaxi, Player.Main/IsBeingTeleported, Player.Main/ResurrectPlayer, Player.Main/SaveToDB, Player.Main/SetSaveDisabled, Player.Main/SpawnCorpseBones, Player.Main/UpdateSkillsToMaxSkillsForLevel, Player.Main/UpdateZone, PlayerBotAI/UpdateAI#2, PlayerTaxi/empty, shared_Util/urand, ShortTimeTracker/Passed, ShortTimeTracker/Reset, ShortTimeTracker/Update, SpellCaster/GetCurrentSpell, SpellCaster/InterruptSpell, SpellCaster/IsNonMeleeSpellCasted, Unit.Main/AttackStop, Unit.Main/ClearTarget, Unit.Main/CombatStop, Unit.Main/GetAttackers, Unit.Main/GetClass, Unit.Main/GetHealthPercent, Unit.Main/GetLevel, Unit.Main/GetMotionMaster, Unit.Main/GetPowerPercent, Unit.Main/GetPowerType, Unit.Main/GetSheath, Unit.Main/GetStandState, Unit.Main/GetTargetGuid, Unit.Main/GetVictim, Unit.Main/HasUnitState, Unit.Main/IsDead, Unit.Main/IsInCombat, Unit.Main/IsMounted, Unit.Main/IsVisibleForOrDetect, Unit.Main/SendMovementPacket, Unit.Main/SetHealthPercent, Unit.Main/SetInFront, Unit.Main/SetPowerPercent, Unit.Main/SetSheath, Unit.Main/SetStandState, Unit.Main/StopMoving, WorldObject.Object/GetCombatDistance, WorldObject.Object/GetDistance#3, WorldObject.Object/GetDistance2d#4, WorldObject.Object/GetMap, WorldObject.Object/GetMapId, WorldObject.Object/GetName, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/GetZoneAndAreaId, WorldObject.Object/GetZoneId, WorldObject.Object/HasInArc, WorldObject.Object/IsMoving, WorldObject.Object/IsWithinDist, WorldObject.Object/RemoveFlag, WorldSession.ChannelHandler/HandleJoinChannelOpcode | — | — |
| ValidateWideBowCandidate | decl | — | — | — |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `character_action`: guid int(11) unsigned PK, button tinyint(3) unsigned PK, action int(11) unsigned, type tinyint(3) unsigned
- `character_homebind`: guid int(11) unsigned PK, map int(11) unsigned, zone int(11) unsigned, position_x float, position_y float, position_z float
- `character_reputation`: guid int(11) unsigned PK, faction int(11) unsigned PK, standing int(11), flags int(11)
- `character_skills`: guid int(11) unsigned PK, skill mediumint(9) unsigned PK, value mediumint(9) unsigned, max mediumint(9) unsigned
- `character_spell`: guid int(11) unsigned PK, spell int(11) unsigned PK, active tinyint(3) unsigned, disabled tinyint(3) unsigned
- `characters`: guid int(11) unsigned PK, account int(11) unsigned, name varchar(12), race tinyint(3) unsigned, class tinyint(3) unsigned, gender tinyint(3) unsigned, skin tinyint(3) unsigned, face tinyint(3) unsigned, hair_style tinyint(3) unsigned, hair_color tinyint(3) unsigned, facial_hair tinyint(3) unsigned, level tinyint(3) unsigned, xp int(10) unsigned, money int(10) unsigned, character_flags int(10) unsigned, zone int(11) unsigned, map int(11) unsigned, instance int(11) unsigned, position_x float, position_y float, position_z float, orientation float, transport_guid bigint(20) unsigned, transport_x float, transport_y float, transport_z float, transport_o float, known_taxi_mask longtext?, current_taxi_path text?, online tinyint(3) unsigned, played_time_total int(11) unsigned, played_time_level int(11) unsigned, create_time bigint(20) unsigned, logout_time bigint(20) unsigned, rest_bonus float, reset_talents_multiplier int(11) unsigned, reset_talents_time bigint(20) unsigned, death_expire_time bigint(20) unsigned, stable_slots tinyint(1) unsigned, bank_bag_slots tinyint(1) unsigned, extra_flags int(11) unsigned, honor_rank_points float, honor_highest_rank int(11) unsigned, honor_standing int(11) unsigned, honor_last_week_hk int(11) unsigned, honor_last_week_cp float, honor_stored_hk int(11), honor_stored_dk int(11), watched_faction int(11), drunk smallint(5) unsigned, health int(10) unsigned, power1 int(10) unsigned, power2 int(10) unsigned, power3 int(10) unsigned, power4 int(10) unsigned, power5 int(10) unsigned, explored_zones longtext?, equipment_cache longtext?, ammo_id int(10) unsigned, action_bars tinyint(3) unsigned, deleted_account int(11) unsigned?, deleted_name varchar(12)?, deleted_time bigint(20)?, world_phase_mask int(11)?

*`?` = nullable, `PK` = primary key column.*

