<!-- provenance: boundary-bleed -->
# AiBotAI.Bridge

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# AiBotAI.Bridge

**Purpose & Responsibilities**

`AiBotAI.Bridge` (implemented in `AiBotAIBridge.cpp`) constitutes the TCP transport layer and command interpreter for the autonomous AI bot’s communication with an external C# "brain" service (`BotBrainService`). It operates as a non-blocking client connecting to `127.0.0.1:3444`.

Its responsibilities are threefold:
1.  **Transport Management:** Handling socket creation, connection, disconnection, and exponential backoff reconnection logic. It manages non-blocking I/O for both sending and receiving data, ensuring robustness against network stalls or peer closures.
2.  **Outbound Telemetry (C++ → C#):** Periodically broadcasting the bot’s state (`HELLO`, `STATE`) and emitting discrete events (`EVENT`) such as kills, quest updates, and loot acquisitions. The `STATE` message includes a comprehensive snapshot of the player’s health, position, inventory, and a serialized quest log.
3.  **Inbound Command Execution (C# → C++):** Parsing incoming JSON-lines commands and executing corresponding actions on the bot’s `Player` object. This includes movement directives (`MOVE_TO`, `TELEPORT_TO`), social interactions (`SAY_TEXT`, `FORM_GROUP`), economic actions (`SELL_ITEMS`, `REPAIR_AT_NPC`), quest management (`QUEST_INTERACT`, `ABANDON_QUEST`), and combat directives (`ATTACK_TARGET`, `COMBAT_DIRECTIVE`).

The unit relies on minimal, file-local JSON parsing utilities (`JsonExtract*`) to avoid external dependencies. It strictly adheres to a JSON-lines protocol, where each message is terminated by a newline character.

## Member-by-Member Behavior

### Transport & Protocol Management

**BridgeConnect**
Establishes a TCP connection to the configured bridge host. If the socket creation or connection fails, it logs the error, closes the socket, and sets a reconnection timer with exponential backoff (capped at `BRIDGE_RECONNECT_MAX`). Upon success, it configures the socket for non-blocking I/O (`O_NONBLOCK` on POSIX, `FIONBIO` on Windows) and resets session state flags (`m_bridgeHelloSent`, `m_bridgeSendBuf`).

**BridgeDisconnect**
Closes the active socket and resets all bridge-related state variables, including clearing the send buffer and receive length. This ensures no stale data persists across reconnections.

**BridgeSend**
Queues a JSON string for transmission. It appends the JSON payload followed by a newline to `m_bridgeSendBuf`. To prevent unbounded memory growth if the C# client stops reading, it implements a safety valve: if the buffer exceeds `BRIDGE_SEND_BUF_MAX`, it discards half the buffer, then trims up to the next newline to ensure message alignment. It immediately calls `BridgeFlush` to attempt draining the queue.

**BridgeFlush**
Drains `m_bridgeSendBuf` to the socket using `send()`. It handles partial writes by erasing only the successfully sent bytes and continuing the loop. It correctly interprets `EWOULDBLOCK`/`WSAEWOULDBLOCK` as a temporary condition (returning without error) and `EINTR` as a retryable interruption. A return value of 0 indicates the peer closed the connection, triggering `BridgeDisconnect`.

**BridgeRecv**
Reads available data from the socket into `m_bridgeRecvBuf`. It processes complete lines (terminated by `\n`) by passing them to `BridgeProcessLine`. Remaining partial data is shifted to the front of the buffer for the next read cycle. It handles `EWOULDBLOCK` gracefully and disconnects on actual errors or peer closure.

**BridgeProcessLine**
Dispatches incoming JSON commands based on the `"type"` field extracted via `JsonExtractString`. It maps string types (e.g., `"MOVE_TO"`, `"SELL_ITEMS"`) to specific handler methods (`BridgeHandleMoveTo`, `BridgeHandleSellItems`, etc.). Unknown types are logged.

### Outbound Telemetry

**BridgeSendHello**
Sent once upon successful connection. It transmits the bot’s GUID, name, race, class, level, map ID, zone ID, and coordinates. It also initializes `m_trackedQuestId` by finding the first incomplete or complete quest in the player’s quest log, establishing the initial context for the C# brain.

**BridgeSendState**
Broadcasts a comprehensive snapshot of the bot’s current status every 5 seconds (driven by `AiBotAI.Main/UpdateAI`). It calculates:
*   **Task Status:** Determines if the bot is IDLE, DEAD, COMBAT, GRINDING, FLYING, or MOVING.
*   **Inventory:** Counts free and total inventory slots, including bags.
*   **Durability:** Calculates the minimum durability percentage of equipped gear to trigger repair decisions in C#.
*   **Quest Log:** Serializes all active, non-rewarded quests into a pipe-delimited blob (`questBlob`) containing quest ID, status, and creature/item counts. This replaces the older pull-based quest query mechanism.
*   **Position & Health:** Includes coordinates, health, mana, level, and combat/death states.
*   **Task Echo:** Sends the current task kind and activity (e.g., "engaged", "traveling") to help C# reconcile internal state.

**BridgeSendEvent**
Emits a discrete event to C# with a specified type and data string. Used for notifications like kills, quest completions, and command acknowledgments.

### Inbound Command Handlers

**BridgeHandleMoveTo**
Directs the bot to move to specific coordinates. It supports optional "objective enrichment" fields (`creature_entry`, `kill_count`, `grind_radius`) which convert the movement into a grind task upon arrival. It implements an "arrival jitter" mechanism for plain moves: it samples points in a ring around the destination to avoid landing on bad navmesh polygons, validating paths via `PathFinder`. It also parses alternate creature entries (`alt_entry1/2/3`) for flexible kill objectives.

**BridgeHandleTeleport**
Performs an instantaneous same-map teleport (`NearTeleportTo`). It validates that the bot is alive, on the same map, and within an optional `max_dist` safety cap. It grounds the Z-coordinate using `ReGroundZ` (from `AiBotAI.Movement`) to prevent floating. After teleporting, it clears movement tasks and suppresses wandering briefly to allow C# to issue the next interaction command.

**BridgeHandleSayText**
Handles chat output. It supports Say, Yell, Whisper, and Channel messages. Whispers are constructed manually using `ChatHandler::BuildChatPacket` and sent directly to the target player’s session. Channel messages use `ChannelMgr`. If the target or channel is invalid, it falls back to a standard Say.

**BridgeHandleQuestInteract**
Manages quest acceptance and completion.
*   **Accept:** Validates the NPC and quest template. Checks if the quest is already rewarded (to prevent double-grinding bugs). Verifies requirements via `CanTakeQuest` and `CanAddQuest`. For zero-objective quests, it automatically marks them complete.
*   **Complete:** Validates the quest is in the log and can be rewarded. It calls `ChooseQuestReward` (from `AiBotAI.Loot`) to select the best item reward, then executes `RewardQuest`. It triggers auto-equipment of new items via `TryAutoEquip` and `TryAutoEquipBags` (from `AiBotAI.Loot`).

**BridgeHandleAbandonQuest**
Removes a quest from the player’s log by setting its status to `QUEST_STATUS_NONE`. Updates the tracked quest ID if necessary.

**BridgeHandleLearnSpell**
Forces the bot to learn a specific spell ID if it doesn’t already know it.

**BridgeHandleTrain**
Automates training at an NPC. It searches for the trainer within 15 yards (expanding to 50 yards if not found). It iterates through the trainer’s spell list, learning all "green" (affordable and fitting) spells, excluding primary profession first-rank spells. It deducts money and updates the bot’s spell data via `PopulateSpellData` and `ResetSpellData` (from `CombatBotBaseAI`).

**BridgeHandleQueryQuestStatus**
Responds to a C# request with a full snapshot of active quests in the same pipe-delimited format used in `BridgeSendState`. This is largely legacy functionality, superseded by the push-based `questBlob` in `STATE`, but remains for compatibility.

**BridgeHandleAttackTarget**
Initiates combat with a specific creature GUID. It validates the target is hostile via `IsValidHostileTarget` (from `CombatBotBaseAI`) before calling `AttackStart` (from `AiBotAI.Combat`).

**BridgeHandleInteractNpc**
Triggers interaction with an NPC. If the NPC is too far (>10 yards), it moves the bot to the contact point using `MovePointRun` (from `AiBotAI.Movement`). Otherwise, it faces the NPC and sends an interaction acknowledgment.

**BridgeHandleSetTask**
Sets the bot’s internal task state to `TASK_GRIND` or `TASK_IDLE`. For grinds, it configures the center coordinates, radius, creature entry, and kill goal. If the bot is outside the grind radius, it immediately moves to the center using `MovePointRun` (from `AiBotAI.Movement`).

**BridgeHandleCombatDirective**
Applies a group combat directive (e.g., "assist anchor"). It updates `m_combatDirective` with the mode and anchor GUID. This is read by the combat resolution logic in other units to coordinate focus-fire in groups.

**BridgeHandleTakeFlight**
Activates a flight path between two taxi nodes. It validates node existence, path availability, and cost. It unlocks the nodes for the bot, checks funds, and activates the path. It sets the task type to `TASK_TAXI` to prevent interference from other AI behaviors during flight.

**BridgeHandleSellItems**
Vends unwanted items to an NPC. It protects quest items, high-quality gear (above `keep_quality`), and essential consumables (keeping up to 40 of each type). It sells excess bags that are not upgrades over currently equipped bags. It reports the amount sold, copper earned, and remaining free slots.

**BridgeHandleRepairItems**
Repairs all equipped gear at an NPC. It checks for damaged items first to avoid unnecessary transactions. It uses `DurabilityRepairAll` to perform the repair and deducts the cost.

**BridgeHandleUseGameObject**
Interacts with a Game Object (e.g., a chest or node). It finds the nearest spawned GO of the specified entry, loots it (generating loot from templates), stores items via `AutoStoreLoot`, and despawns the GO. It triggers auto-equipment for looted items via `TryAutoEquip` and `TryAutoEquipBags` (from `AiBotAI.Loot`).

**BridgeHandleFormGroup**
Creates a group with the bot as leader and adds specified member GUIDs. It sets the loot method to `NEED_BEFORE_GREED`. It handles removing members from existing groups if necessary.

**BridgeHandleDisbandGroup**
Disbands the current group if the bot is in one.

**BridgeHandleResurrect**
Handles bot resurrection. If `at_graveyard` is true, it teleports the ghost to the nearest graveyard (or spawn point if no graveyard is found) using `NearTeleportTo` and arms a pending resurrection (`m_pendingGraveyardRez`) to occur once the teleport lands. This prevents "death loops" where the bot respawns in a dangerous location. It uses `ReGroundZ` (from `AiBotAI.Movement`) to ensure the ghost lands on solid ground. If `at_graveyard` is false, it resurrects the bot in place.

### Event Emitters

**SendKillEvent**, **SendQuestUpdateEvent**, **SendLevelUpEvent**, **SendChatRecvEvent**
These methods construct and send specific JSON events to C# to notify the brain of significant state changes (kills, quest progress, leveling, and incoming chat). They are called by other units (`AiBotAI.Main`, `AiBotAI.Combat`, etc.) when these events occur.

### Utility Functions

**JsonExtractFloat**, **JsonExtractInt**, **JsonExtractString**
File-local static functions that parse simple JSON key-value pairs. They use `strstr` to locate keys and `atof`/`atoi`/`memcpy` to extract values. They do not handle nested structures or arrays, relying on the flat structure of the bridge protocol.

## Cross-Unit Boundaries

*   **AiBotAI.Main:**
    *   *Called By:* `BridgeConnect`, `BridgeSendHello`, `BridgeSendState`, `BridgeRecv` are called by `UpdateAI` to manage the connection lifecycle and periodic telemetry.
    *   *Calls Into:* `BridgeSendEvent` is called by `UpdateAI` to report state changes.
*   **AiBotAI.Movement:**
    *   *Calls Into:* `BridgeHandleMoveTo` calls `MoveToDestination` and `ReGroundZ`. `BridgeHandleTeleport` calls `ClearStoredPath`, `ReGroundZ`, and `StopMoving`. `BridgeHandleInteractNpc` calls `MovePointRun` and `StopMoving`. `BridgeHandleSetTask` calls `MovePointRun` and `StopMoving`. `BridgeHandleTakeFlight` calls `StopMoving`. `BridgeHandleResurrect` calls `ReGroundZ`.
    *   *Reason:* To execute physical movement, pathfinding, and grounding operations resulting from bridge commands.
*   **AiBotAI.Loot:**
    *   *Calls Into:* `BridgeHandleQuestInteract` calls `ChooseQuestReward`, `TryAutoEquip`, and `TryAutoEquipBags`. `BridgeHandleUseGameObject` calls `TryAutoEquip` and `TryAutoEquipBags`.
    *   *Reason:* To manage inventory optimization and item equipping after quest rewards or looting.
*   **AiBotAI.Combat:**
    *   *Calls Into:* `BridgeHandleAttackTarget` calls `AttackStart`.
    *   *Called By:* `BridgeSendEvent` is called by `HandleCombatStalemate` to report stalemate conditions.
    *   *Reason:* To initiate combat actions and report combat-related events.
*   **CombatBotBaseAI:**
    *   *Calls Into:* `BridgeHandleTrain` calls `PopulateSpellData` and `ResetSpellData`. `BridgeHandleAttackTarget` calls `IsValidHostileTarget`.
    *   *Reason:* To update spell knowledge and validate combat targets.
*   **Player.Main:**
    *   *Calls Into:* Extensively used across all handlers for accessing player state (name, money, quests, spells, group, taxi, durability) and performing actions (say, yell, learn spell, add/complete quest, repair, loot, teleport).
*   **Log.Main:**
    *   *Calls Into:* `Out` is called by nearly every method for debugging and operational logging.

## Data Model

This unit does not directly access database tables. It interacts with in-memory representations of game entities (`Player`, `Creature`, `GameObject`, `Quest`) managed by the core server engine. The `SCHEMA` section is therefore not applicable.

## Notable Implementation Details

*   **Non-Blocking I/O:** The bridge uses non-blocking sockets to prevent the AI thread from hanging on network operations. `BridgeFlush` and `BridgeRecv` carefully handle `EWOULDBLOCK`/`WSAEWOULDBLOCK` to distinguish between "no data ready" and "error".
*   **Buffer Safety:** `BridgeSend` implements a circular-like buffer management strategy by dropping the oldest messages if the queue grows too large, preventing memory exhaustion if the C# client disconnects or slows down.
*   **JSON-Lines Protocol:** The communication protocol is line-oriented JSON. Each message is terminated by `\n`. This simplifies parsing in `BridgeRecv` and allows for easy streaming.
*   **Arrival Jitter:** `BridgeHandleMoveTo` intentionally offsets the destination coordinates slightly for plain moves to avoid navmesh edge cases. This is a heuristic fix for pathfinding issues where exact coordinates land on unwalkable polygons.
*   **Quest Log Serialization:** `BridgeSendState` serializes the entire quest log into a compact string format. This is a significant optimization over the previous pull-based model, reducing latency and ensuring C# always has an up-to-date view of quest progress.
*   **Death Loop Prevention:** `BridgeHandleResurrect` implements a sophisticated "ghost port" mechanism. Instead of resurrecting immediately, it teleports the ghost to a safe location (graveyard or spawn) and resurrects only after the teleport completes. This prevents the bot from respawning in a high-danger area and dying repeatedly.
*   **Minimal JSON Parser:** The `JsonExtract*` functions are lightweight and fragile. They assume well-formed, flat JSON. They do not handle escaped quotes within strings or nested objects, which limits the complexity of commands that can be sent from C#.
*   **Thread Safety:** The bridge state is accessed only from the AI thread (via `UpdateAI` and callbacks), so no mutexes are used. This assumes the C# client communicates over a single TCP connection per bot and that the server engine does not invoke these methods concurrently.

## Member Reference

**BridgeConnect**: Establishes TCP connection to C# brain, sets non-blocking mode, handles reconnection backoff.
**BridgeDisconnect**: Closes socket, resets bridge state flags and buffers.
**BridgeSend**: Queues JSON line to send buffer, enforces size limit by dropping oldest data, calls Flush.
**BridgeFlush**: Drains send buffer to socket, handles partial writes and EWOULDBLOCK, disconnects on error.
**BridgeSendHello**: Sends initial bot identity and state to C#, initializes tracked quest ID.
**BridgeSendState**: Broadcasts comprehensive bot state snapshot (health, pos, inventory, quest log blob) to C#.
**BridgeSendEvent**: Sends discrete event notification (kill, quest update, etc.) to C#.
**BridgeRecv**: Reads from socket, processes complete JSON lines via BridgeProcessLine, handles partial data.
**JsonExtractFloat**: File-local utility to extract float value from flat JSON string by key.
**JsonExtractInt**: File-local utility to extract integer value from flat JSON string by key.
**JsonExtractString**: File-local utility to extract string value from flat JSON string by key.
**BridgeProcessLine**: Dispatches incoming JSON command to appropriate BridgeHandle* method based on "type" field.
**BridgeHandleTeleport**: Executes same-map instant teleport, validates safety caps, grounds Z, clears movement tasks.
**BridgeHandleMoveTo**: Directs bot to move to coords, supports grind objectives, applies arrival jitter for navmesh safety.
**BridgeHandleSayText**: Handles chat output (Say, Yell, Whisper, Channel), constructs packets for whispers.
**BridgeHandleQuestInteract**: Manages quest acceptance and completion, validates requirements, chooses rewards, auto-equips.
**BridgeHandleAbandonQuest**: Removes quest from player log, updates tracked quest ID.
**BridgeHandleLearnSpell**: Forces bot to learn a specific spell ID if not already known.
**BridgeHandleTrain**: Automates training at NPC, learns affordable green spells, deducts cost, updates spell data.
**BridgeHandleQueryQuestStatus**: Responds to C# with full active quest log snapshot (legacy pull-based).
**BridgeHandleAttackTarget**: Initiates combat with specific creature GUID, validates hostility.
**BridgeHandleInteractNpc**: Triggers NPC interaction, moves bot closer if too far, faces NPC.
**BridgeHandleSetTask**: Sets internal task state to GRIND or IDLE, configures grind parameters, moves to center if needed.
**BridgeHandleCombatDirective**: Applies group combat directive (assist anchor) for coordinated focus-fire.
**BridgeHandleTakeFlight**: Activates flight path between taxi nodes, validates cost/path, sets task to TAXI.
**BridgeHandleSellItems**: Vends unwanted items, protects quest/high-quality/consumable items, reports earnings.
**BridgeHandleRepairItems**: Repairs all equipped gear at NPC, checks for damage, deducts cost.
**BridgeHandleUseGameObject**: Interacts with Game Object, loots items/gold, auto-equips, despawns GO.
**BridgeHandleFormGroup**: Creates group with bot as leader, adds members, sets loot method to NEED_BEFORE_GREED.
**BridgeHandleDisbandGroup**: Disbands current group if bot is in one.
**BridgeHandleResurrect**: Handles resurrection, teleports ghost to safe location if at_graveyard, prevents death loops.
**SendKillEvent**: Emits KILL event to C# with creature entry and GUID.
**SendQuestUpdateEvent**: Emits QUEST_UPDATE event to C# with quest ID and status.
**SendLevelUpEvent**: Emits LEVEL_UP event to C# with new level.
**SendChatRecvEvent**: Emits CHAT_RECV event to C# with sender, message, and chat type.

---

<!-- machine-true, projected from graph.json -->

## Map — AiBotAI.Bridge

*Source:* AiBotAIBridge.cpp, AiBotAIMain.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| BridgeConnect | method | Log.Main/Out, Player.Main/GetName | AiBotAI.Main/UpdateAI | — |
| BridgeDisconnect | method | — | — | — |
| BridgeSend | method | Log.Main/Out, Player.Main/GetName | — | — |
| BridgeFlush | method | Log.Main/Out, Player.Main/GetName | AiBotAI.Main/UpdateAI | — |
| BridgeSendHello | method | Log.Main/Out, Object/GetGUIDLow, Player.Main/GetName, Player.Main/GetQuestStatusMap, Unit.Main/GetClass, Unit.Main/GetLevel, Unit.Main/GetRace, WorldObject.Object/GetMapId, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/GetZoneId | AiBotAI.Main/UpdateAI | — |
| BridgeSendState | method | Bag/GetBagSize, game_Objects_Item/GetProto, Object/GetGUIDLow, Object/GetUInt32Value, ObjectGuid/GetCounter, ObjectGuid/IsEmpty, Player.Main/GetItemByPos, Player.Main/GetMoney, Player.Main/GetQuestStatus, Player.Main/GetQuestStatusMap, Unit.Main/GetHealth, Unit.Main/GetLevel, Unit.Main/GetMaxHealth, Unit.Main/GetMaxPower, Unit.Main/GetPower, Unit.Main/GetTargetGuid, Unit.Main/HasAura#2, Unit.Main/IsDead, Unit.Main/IsInCombat, WorldObject.Object/GetMapId, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/GetZoneId, WorldObject.Object/IsMoving | AiBotAI.Main/UpdateAI | — |
| BridgeSendEvent | method | Object/GetGUIDLow | AiBotAI.Combat/HandleCombatStalemate, AiBotAI.Loot/DoAutoLoot, AiBotAI.Loot/TryAutoEquip, AiBotAI.Loot/TryAutoEquipBags, AiBotAI.Main/MovementInform, AiBotAI.Main/UpdateAI, AiBotAI.Movement/MoveToDestination | — |
| BridgeRecv | method | Log.Main/Out, Player.Main/GetName | AiBotAI.Main/UpdateAI | — |
| JsonExtractFloat | function | — | — | — |
| JsonExtractInt | function | — | — | — |
| JsonExtractString | function | — | — | — |
| BridgeProcessLine | method | Log.Main/Out, Player.Main/GetName | — | — |
| BridgeHandleTeleport | method | AiBotAI.Movement/ClearStoredPath, AiBotAI.Movement/ReGroundZ, AiBotAI.Movement/StopMoving, AiBotTaskData/Clear, Log.Main/Out, Object/IsInWorld, Player.Main/GetName, Unit.Main/GetDeathState, Unit.Main/IsDead, Unit.Main/NearTeleportTo, WorldObject.Object/GetDistance2d#4, WorldObject.Object/GetMapId, WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ | — | — |
| BridgeHandleMoveTo | method | AiBotAI.Movement/MoveToDestination, AiBotAI.Movement/ReGroundZ, Log.Main/Out, PathInfo/getPathType, Player.Main/GetName, shared_Util/frand, Unit.Main/IsInCombat, WorldObject.Object/GetDistance2d#4, WorldObject.Object/GetMapId, WorldObject.PathFinder/calculate#2, WorldObject.PathFinder/PathInfo | — | — |
| BridgeHandleSayText | method | ChannelMgr/channelMgr, ChannelMgr/GetJoinChannel, ChatHandler.Chat/BuildChatPacket, game_Chat_Channel/Say, Log.Main/Out, Object/GetObjectGuid, ObjectMgr/GetPlayer#2, Player.Main/GetName, Player.Main/GetSession, Player.Main/GetTeam, Player.Main/Say, Player.Main/Yell, WorldPacket/WorldPacket, WorldSession.Main/SendPacket | — | — |
| BridgeHandleQuestInteract | method | AiBotAI.Loot/ChooseQuestReward, AiBotAI.Loot/TryAutoEquip, AiBotAI.Loot/TryAutoEquipBags, Creature.Main/GetName, Log.Main/Out, ObjectMgr/GetQuestTemplate, Player.Main/AddQuest, Player.Main/CanAddQuest, Player.Main/CanRewardQuest#2, Player.Main/CanTakeQuest, Player.Main/CompleteQuest, Player.Main/GetName, Player.Main/GetQuestStatus, Player.Main/GetQuestStatusMap, Player.Main/RewardQuest, Player.Main/SatisfyQuestBreadcrumbQuest, Player.Main/SatisfyQuestClass, Player.Main/SatisfyQuestCondition, Player.Main/SatisfyQuestDependentBreadcrumbQuests, Player.Main/SatisfyQuestExclusiveGroup, Player.Main/SatisfyQuestLevel, Player.Main/SatisfyQuestNextChain, Player.Main/SatisfyQuestPrevChain, Player.Main/SatisfyQuestPreviousQuest, Player.Main/SatisfyQuestRace, Player.Main/SatisfyQuestReputation, Player.Main/SatisfyQuestSkill, Player.Main/SatisfyQuestStatus, Player.Main/SatisfyQuestTimed, QuestDef/GetReqCreatureOrGOcount, QuestDef/GetReqItemsCount, QuestDef/GetTitle, QuestDef/IsActive, Unit.Main/IsAlive, WorldObject.Object/GetCreatureListWithEntryInGrid, WorldObject.Object/GetDistance#3 | — | — |
| BridgeHandleAbandonQuest | method | Log.Main/Out, Player.Main/GetName, Player.Main/GetQuestStatus, Player.Main/SetQuestStatus | — | — |
| BridgeHandleLearnSpell | method | Log.Main/Out, Player.Main/GetName, Player.Main/HasSpell, Player.Main/LearnSpell | — | — |
| BridgeHandleTrain | method | AiBotAI.Movement/StopMoving, CombatBotBaseAI/PopulateSpellData, CombatBotBaseAI/ResetSpellData, Creature.Main/GetName, Creature.Main/GetTrainerSpells, Creature.Main/GetTrainerTemplateSpells, Creature.Main/IsTrainerOf, Log.Main/Out, Object/GetEntry, Object/GetGUIDLow, Object/GetUInt32Value, Player.Main/GetMoney, Player.Main/GetName, Player.Main/GetTrainerSpellState, Player.Main/IsSpellFitByClassAndRace, Player.Main/LearnSpell, Player.Main/ModifyMoney, SpellMgr/GetSpellEntry, SpellMgr/Instance, SpellMgr/IsPrimaryProfessionFirstRankSpell, Unit.Main/IsAlive, WorldObject.Object/GetCreatureListWithEntryInGrid, WorldObject.Object/GetDistance#3, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ | — | — |
| BridgeHandleQueryQuestStatus | method | Log.Main/Out, Player.Main/GetName, Player.Main/GetQuestStatusMap | — | — |
| BridgeHandleAttackTarget | method | AiBotAI.Combat/AttackStart, CombatBotBaseAI/IsValidHostileTarget, Creature.Main/GetName, Log.Main/Out, Map.Main/GetCreature, ObjectGuid/ObjectGuid#2, Player.Main/GetName, WorldObject.Object/GetMap | — | — |
| BridgeHandleInteractNpc | method | AiBotAI.Movement/MovePointRun, AiBotAI.Movement/StopMoving, Creature.Main/GetName, Log.Main/Out, Map.Main/GetCreature, ObjectGuid/ObjectGuid#2, Player.Main/GetName, Unit.Main/SetFacingToObject, WorldObject.Object/GetContactPoint, WorldObject.Object/GetDistance#3, WorldObject.Object/GetMap | — | — |
| BridgeHandleSetTask | method | AiBotAI.Movement/MovePointRun, AiBotAI.Movement/StopMoving, AiBotTaskData/Clear, Log.Main/Out, Player.Main/GetName, WorldObject.Object/GetDistance2d#4 | — | — |
| BridgeHandleCombatDirective | method | CombatDirective/Clear, Log.Main/Out, Object/GetGUIDLow, Player.Main/GetName | — | — |
| BridgeHandleTakeFlight | method | AiBotAI.Movement/StopMoving, AiBotTaskData/Clear, Log.Main/Out, Object/GetGUIDLow, ObjectMgr/GetTaxiNodeEntry, ObjectMgr/GetTaxiPath, Player.Main/ActivateTaxiPathTo, Player.Main/GetMoney, Player.Main/GetName, Player.Main/GetTaxi, PlayerTaxi/SetTaximaskNode, Unit.Main/IsDead, Unit.Main/IsInCombat, Unit.Main/IsMounted, Unit.Main/RemoveSpellsCausingAura, WorldObject.Object/GetMapId | — | — |
| BridgeHandleSellItems | method | Bag/GetBagSize, Creature.Main/GetName, game_Objects_Item/GetCount, game_Objects_Item/GetProto, Log.Main/Out, Object/GetEntry, Object/GetUInt32Value, Object/IsInWorld, ObjectMgr/GetQuestTemplate, Player.Main/DestroyItem, Player.Main/GetItemByPos, Player.Main/GetMoney, Player.Main/GetName, Player.Main/GetQuestStatusMap, Player.Main/ModifyMoney, QuestDef/GetSrcItemId, Unit.Main/IsAlive, WorldObject.Object/GetCreatureListWithEntryInGrid, WorldObject.Object/GetDistance#3 | — | — |
| BridgeHandleRepairItems | method | Creature.Main/GetName, Log.Main/Out, Object/GetEntry, Object/GetUInt32Value, Object/IsInWorld, Player.Main/DurabilityRepairAll, Player.Main/GetItemByPos, Player.Main/GetMoney, Player.Main/GetName, Unit.Main/IsAlive, WorldObject.Object/GetCreatureListWithEntryInGrid, WorldObject.Object/GetDistance#3 | — | — |
| BridgeHandleUseGameObject | method | AiBotAI.Loot/TryAutoEquip, AiBotAI.Loot/TryAutoEquipBags, GameObject/GetGOInfo, GameObject/getLootState, GameObject/isSpawned, GameObject/SetLootState, GameObjectInfo/GetLootId, Log.Main/Out, Loot/clear, LootMgr/FillLoot, Player.Main/AutoStoreLoot, Player.Main/GetName, Player.Main/LootMoney, Player.Main/ModifyMoney, WorldObject.Object/GetDistance#3, WorldObject.Object/GetGameObjectListWithEntryInGrid | — | — |
| BridgeHandleFormGroup | method | game_Group_Group/AddMember, game_Group_Group/Create, game_Group_Group/Disband, game_Group_Group/Group, game_Group_Group/RemoveMember, Group/SetLootMethod, Log.Main/Out, Object/GetGUIDLow, Object/GetObjectGuid, ObjectGuid/ObjectGuid#2, ObjectMgr/GetPlayer, Player.Main/GetGroup, Player.Main/GetName | — | — |
| BridgeHandleDisbandGroup | method | game_Group_Group/Disband, Log.Main/Out, Player.Main/GetGroup, Player.Main/GetName | — | — |
| BridgeHandleResurrect | method | AiBotAI.Movement/ReGroundZ, Log.Main/Out, ObjectMgr/GetClosestGraveYard, Player.Main/GetName, Player.Main/GetTeam, Player.Main/ResurrectPlayer, Player.Main/SpawnCorpseBones, Unit.Main/CombatStop, Unit.Main/GetDeathState, Unit.Main/IsDead, Unit.Main/NearTeleportTo, WorldObject.Object/GetMapId, WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ | — | — |
| SendKillEvent | method | Object/GetGUIDLow | AiBotAI.Main/UpdateAI | — |
| SendQuestUpdateEvent | method | Object/GetGUIDLow | — | — |
| SendLevelUpEvent | method | Object/GetGUIDLow | AiBotAI.Main/UpdateAI | — |
| SendChatRecvEvent | method | Object/GetGUIDLow | AiBotAI.Main/OnPacketReceived | — |

---

<!-- verify: boundary-bleed | foreign: AiBotAI -->
