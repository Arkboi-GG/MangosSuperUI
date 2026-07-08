<!-- provenance: failed-members -->
# game_Battlegrounds_BattleGround

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# BattleGround

**Purpose & Responsibilities**

The `BattleGround` class serves as the abstract base class for all PvP battleground instances in the MaNGOS server emulator (e.g., Alterac Valley, Warsong Gulch, Arathi Basin). It manages the lifecycle of a battleground instance, including player joining/leaving, state transitions (waiting, starting, in-progress, ending), scoring, rewards, and the spawning/despawning of dynamic creatures and game objects associated with specific battleground events.

It acts as the central coordinator for:
1.  **Instance Management:** Tracking the instance ID, map, status, and timing.
2.  **Player Management:** Maintaining lists of players per team, handling raid groups, and processing join/leave events.
3.  **Communication:** Broadcasting chat messages, sounds, and world state updates to participants.
4.  **Rewards:** Calculating and distributing honor, reputation, items, and quest completions upon victory or defeat.
5.  **Dynamic Spawning:** Managing the lifecycle of creatures and game objects tied to specific battleground events (e.g., capturing a graveyard spawns defenders).

**Note:** This class is designed to be inherited. Specific battleground logic (like flag mechanics in Warsong Gulch or mine ownership in Alterac Valley) is implemented in derived classes (`BattleGroundWS`, `BattleGroundAV`, `BattleGroundAB`). The base class provides the common infrastructure.

## Member-by-Member Behavior

### Lifecycle and State Management

*   **`BattleGround()`**: Constructor initializes all member variables to default states (e.g., `STATUS_NONE`, zero timers, empty player maps). It sets default start delay times and message IDs for the countdown sequence.
*   **`~BattleGround()`**: Destructor cleans up resources. It deletes dynamically allocated `BattleGroundScore` objects, removes the battleground from the manager, deletes client-visible instance IDs, unloads the map, and removes the instance from the free slot queue. It logs the winner and duration if the instance was valid.
*   **`Update(uint32 diff)`**: The core update loop called periodically. It handles:
    *   **Empty Instance Cleanup:** If no players are present and no one is invited, it deletes the instance. If players are invited but haven't joined, it schedules a queue update after 2 minutes to prevent stale queues.
    *   **Premature Finish:** If one team falls below the minimum player count during an active battle, it starts a countdown timer. If the timer expires, the battle ends prematurely in favor of the team with sufficient players.
    *   **Starting Sequence:** Manages the countdown phases (`BG_STARTING_EVENT_1` to `4`). It triggers door closures, sends countdown messages, opens doors, teleports players to graveyards, plays start sounds, and transitions the status to `STATUS_IN_PROGRESS`.
    *   **Ending Sequence:** If the status is `STATUS_WAIT_LEAVE`, it counts down the end time. Once expired, it forcibly removes all players from the battleground.
    *   **Door Despawning:** Despawns initial doors 3 minutes after the battle starts.
*   **`Reset()`**: Resets the battleground to a pre-game state (`STATUS_WAIT_QUEUE`), clears player lists, scores, and timers. Used when reusing a battleground template.
*   **`StartBattleGround()`**: Initializes the start time, adds the battleground to the free slot queue, and registers it with the `BattleGroundMgr` for updates.
*   **`StopBattleGround()`**: A debug/admin command handler that forces a premature end by setting the countdown timer to a minimal value.
*   **`EndNow()`**: Immediately ends the battleground, setting the status to `STATUS_WAIT_LEAVE` and building the final score packet. Used for error conditions or forced ends.

### Player Management

*   **`AddPlayer(Player* pPlayer)`**: Adds a player to the `m_players` map, increments the team count, sends a "player joined" packet to the team, checks if the BG is running (to send final scores if late-joining), and adds the player to the appropriate BG raid group.
*   **`RemovePlayerAtLeave(ObjectGuid guid, bool transport, bool sendPacket)`**: Removes a player from the battleground. It decrements team counts, deletes their score, resurrects them if dead, removes them from the BG raid group, and teleports them back to the entrance if they were on the BG map. It also updates the queue if the BG is still active.
*   **`AddOrSetPlayerToCorrectBgGroup(Player* pPlayer, ObjectGuid playerGuid, Team team)`**: Ensures a player is in the correct raid group for their team. If the group doesn't exist, it creates one. If the player was a leader in their original group, they become the leader in the BG group.
*   **`GetPlayerTeam(ObjectGuid guid)`**: Returns the team of a player based on the `m_players` map. Essential for same-faction arenas where team assignment is temporary.
*   **`IsPlayerInBattleGround(ObjectGuid guid)`**: Checks if a player is currently in the `m_players` map.
*   **`GetAlivePlayersCountByTeam(Team team)`**: Counts the number of alive players on a specific team. Used for arena logic and balance checks.
*   **`UpdatePlayersCountByTeam(Team team, bool remove)`**: Increments or decrements the player count for a team.

### Communication and Feedback

*   **`SendPacketToAll(WorldPacket* packet)`**: Sends a raw packet to all players in the battleground.
*   **`SendPacketToTeam(Team team, WorldPacket* packet, Player* sender, bool self)`**: Sends a raw packet to all players on a specific team, optionally excluding the sender.
*   **`PlaySoundToAll(uint32 soundId)`**: Plays a sound to all players.
*   **`PlaySoundToTeam(uint32 soundId, Team team)`**: Plays a sound to a specific team.
*   **`CastSpellOnTeam(uint32 spellId, Team team)`**: Casts a spell on all players of a specific team.
*   **`UpdateWorldState(uint32 field, uint32 value)`**: Updates a world state field for all players.
*   **`UpdateWorldStateForPlayer(uint32 field, uint32 value, Player* source)`**: Updates a world state field for a single player.
*   **`SendMessageToAll(int32 entry, ChatMsg type, Player const* source)`**: Broadcasts a localized chat message to all players. Uses `BattleGroundBroadcastBuilder`.
*   **`SendYellToAll(int32 entry, uint32 language, ObjectGuid guid)`**: Broadcasts a yell from a creature to all players. Uses `BattleGroundYellBuilder`.
*   **`PSendMessageToAll(int32 entry, ChatMsg type, Player const* source, ...)`**: Broadcasts a formatted chat message with variable arguments. Uses `BattleGroundChatBuilder`.
*   **`SendMessage2ToAll(int32 entry, ChatMsg type, Player const* source, int32 strId1, int32 strId2)`**: Broadcasts a chat message with two additional string arguments. Uses `BattleGround2ChatBuilder`.
*   **`SendYell2ToAll(int32 entry, uint32 language, ObjectGuid guid, int32 arg1, int32 arg2)`**: Broadcasts a yell from a creature with two additional string arguments. Uses `BattleGround2YellBuilder`.
*   **`GetWinnerText(Team winner)`**: Returns the broadcast text ID for the winning team.
*   **`GetHeraldEntry()`**: Returns the NPC entry for the herald creature used for announcements in older client builds.

### Rewards and Scoring

*   **`EndBattleGround(Team winner)`**: Handles the end-of-battle logic. It plays win/loss sounds, sets the winner, resurrects players, stops combat, distributes marks of honor/reputation, sends final score packets, logs the battle to the database, and announces the winner.
*   **`RewardHonorToTeam(uint32 honor, Team team)`**: Awards bonus honor to all players on a team.
*   **`RewardReputationToTeam(uint32 factionId, uint32 reputation, Team team)`**: Awards reputation to all players on a team.
*   **`RewardMark(Player* pPlayer, bool winner)`**: Awards the appropriate spell (Mark of Honor) to a player based on whether they won or lost. Skips bots.
*   **`RewardSpellCast(Player* pPlayer, uint32 spellId)`**: Casts a reward spell on a player.
*   **`RewardItem(Player* pPlayer, uint32 itemId, uint32 count)`**: Attempts to give an item to a player. If the bag is full, it mails the item via `SendRewardMarkByMail`.
*   **`SendRewardMarkByMail(Player* pPlayer, uint32 mark, uint32 count)`**: Creates an item and sends it to the player via mail if they couldn't receive it directly.
*   **`RewardQuestComplete(Player* pPlayer)`**: Placeholder for quest completion rewards. Currently commented out.
*   **`UpdatePlayerScore(Player* source, uint32 type, uint32 value)`**: Updates a player's score (kills, deaths, honorable kills, bonus honor). Instantly awards bonus honor to the player's honor manager.
*   **`GetBonusHonorFromKill(uint32 kills)`**: Calculates bonus honor based on the number of kills and the maximum level of the battleground.
*   **`GetHonorModifier()`**: Calculates a modifier for honor gains based on battle duration. Shorter battles yield less bonus honor.

### Dynamic Spawning and Events

*   **`AddObject(...)`**: Dynamically creates a game object in the battleground map and adds it to the `m_bgObjects` map.
*   **`DelObject(uint32 type)`**: Deletes a game object from the map and removes it from the `m_bgObjects` map.
*   **`SpawnEvent(uint8 event1, uint8 event2, bool spawn, bool forcedDespawn, uint32 delay)`**: Spawns or despawns creatures and game objects associated with a specific event pair. It manages the active event state and triggers `OnEventStateChanged`.
*   **`SetSpawnEventMode(uint8 event1, uint8 event2, BattleGroundCreatureSpawnMode mode)`**: Sets the spawn mode for creatures in an event (e.g., forced respawn, forced despawn).
*   **`SpawnBGObject(ObjectGuid guid, uint32 respawnTime)`**: Spawns or despawns a game object by its GUID. Handles loading from DB if necessary.
*   **`SpawnBGCreature(ObjectGuid guid, BattleGroundCreatureSpawnMode mode)`**: Spawns or despawns a creature by its GUID. Handles respawn timers and death states.
*   **`OnObjectDBLoad(Creature* creature)`**: Called when a creature is loaded from the database. Registers it with the appropriate event and spawns/despawns it based on the current event state.
*   **`OnObjectDBLoad(GameObject* obj)`**: Called when a game object is loaded from the database. Registers it with the appropriate event and spawns/despawns it. Opens doors if the battle is in progress.
*   **`CanBeSpawned(Creature* creature)`**: Checks if a creature should be spawned based on the active events.
*   **`GetSingleCreatureGuid(uint8 event1, uint8 event2)`**: Returns the GUID of the first creature registered for an event pair.
*   **`GetSingleGameObjectGuid(uint8 event1, uint8 event2)`**: Returns the GUID of the first game object registered for an event pair.
*   **`OpenDoorEvent(uint8 event1, uint8 event2)`**: Opens all doors associated with an event.
*   **`DoorOpen(ObjectGuid guid)`**: Opens a specific door by GUID.
*   **`DoorClose(ObjectGuid guid)`**: Closes a specific door by GUID.
*   **`StartingEventDespawnDoors()`**: Despawns all doors associated with the initial door event.
*   **`ReturnPlayersToHomeGY()`**: Teleports all players to their team's starting graveyard if they are not already nearby.
*   **`HandleTriggerBuff(GameObject* obj)`**: Handles buff triggers. If `m_buffChange` is true, it randomly selects a new buff type and spawns it.
*   **`HandleKillPlayer(Player* pVictim, Player* pKiller)`**: Handles player kills. Awards honorable kills and killing blows to the killer and their group members. Awards deaths to the victim. Marks the victim as skinnable.

### Utility and Helpers

*   **`SetTeamStartLoc(...)`**: Sets the starting coordinates for a team.
*   **`GetClosestGraveYard(Player* player)`**: Finds the closest graveyard to a player's position.
*   **`BlockMovement(Player* pPlayer)`**: Disables player movement. Used when teleporting or ending a battle.
*   **`SetBgRaid(Team team, Group* bgRaid)`**: Sets the raid group for a team.
*   **`HandleCommand(Player* player, ChatHandler* handler, char* args)`**: Handles admin commands for debugging events (e.g., spawning/despawning events).
*   **`GetBattlemasterEntry()`**: Returns the NPC entry for the battlemaster associated with the battleground type.
*   **`GetInvitedCount(Team team)`**: Returns the number of players invited to a team.
*   **`IncreaseInvitedCount(Team team)`**: Increments the invited count for a team.
*   **`DecreaseInvitedCount(Team team)`**: Decrements the invited count for a team.
*   **`HasFreeSlots()`**: Checks if the battleground has room for more players.
*   **`GetFreeSlotsForTeam(Team team)`**: Calculates the number of free slots for a team, considering balance constraints.
*   **`AddToBGFreeSlotQueue()`**: Adds the battleground to the free slot queue.
*   **`RemoveFromBGFreeSlotQueue()`**: Removes the battleground from the free slot queue.
*   **`PlayerAddedToBGCheckIfBGIsRunning(Player* pPlayer)`**: Checks if the battleground is ending and sends final scores to a late-joining player.

## Cross-Unit Boundaries

*   **`BattleGroundMgr`**: The `BattleGround` class interacts heavily with `BattleGroundMgr` for queue management, scheduling updates, building packets, and retrieving event vectors. It is created by `BattleGroundMgr::CreateBattleGround` and removed by `BattleGroundMgr::RemoveBattleGround`.
*   **`Map`**: The `BattleGround` holds a pointer to its `BattleGroundMap` (derived from `Map`). It uses the map to access creatures, game objects, and players, and to generate local GUIDs.
*   **`Player`**: The `BattleGround` manages `Player` objects, accessing their sessions, teams, scores, and positions. It sends packets to their sessions and modifies their state (resurrection, teleportation, movement blocking).
*   **`Group`**: The `BattleGround` creates and manages `Group` objects for each team's raid. It adds/remembers members and changes leaders.
*   **`ChatHandler`**: Used to build chat packets for broadcasting messages.
*   **`ObjectMgr`**: Used to retrieve broadcast texts, mangos strings, faction entries, and item prototypes.
*   **`WorldSession`**: Used to send packets to players.
*   **`Log`**: Used for logging errors, warnings, and debug information.
*   **`Database`**: Used to log battleground results to the `logs_battleground` table.
*   **`Mail`**: Used to send reward items via mail if the player's bag is full.
*   **`SpellCaster`**: Used to cast reward spells on players.
*   **`Formulas`**: Used to calculate honor gains.

## Data Model

The `BattleGround` class writes to the `logs_battleground` table in the database when a battleground ends. The relevant columns are:

*   `bgid`: The instance ID of the battleground.
*   `bgtype`: The type ID of the battleground.
*   `bgduration`: The duration of the battleground in seconds.
*   `bgteamcount`: The number of players on the player's team.
*   `playerGuid`: The GUID of the player.
*   `team`: The team of the player (Alliance/Horde).
*   `deaths`: The number of deaths for the player.
*   `honorBonus`: The bonus honor earned by the player.
*   `honorableKills`: The number of honorable kills for the player.

## Notable Implementation Details

*   **Premature Finish Logic:** The `Update` method implements a balance system. If one team drops below the minimum player count, a countdown begins. If the other team also drops below, the battle ends immediately. Otherwise, the team with sufficient players wins after the countdown. This prevents stalling tactics.
*   **Dynamic Spawning:** Creatures and game objects are not statically placed. They are spawned/despawned based on events (e.g., capturing a graveyard). The `OnObjectDBLoad` methods register objects with events, and `SpawnEvent` manages their lifecycle. This allows for complex dynamic interactions.
*   **Raid Groups:** Each team in a battleground is placed in a separate raid group. This ensures that buffs and debuffs apply correctly within the team and that the UI displays the correct raid frames. The `AddOrSetPlayerToCorrectBgGroup` method ensures that players are correctly assigned to these groups, preserving leadership if applicable.
*   **Reward Distribution:** Rewards are distributed at the end of the battle. Honor and reputation are awarded instantly. Items are mailed if the player's bag is full. Quests are currently not rewarded (code is commented out).
*   **Localization:** Chat messages and broadcasts are localized using `ObjectMgr::GetBroadcastText` and `ObjectMgr::GetMangosString`. The builders (`BattleGroundBroadcastBuilder`, etc.) handle the localization and packet construction.
*   **Memory Management:** The `BattleGround` class takes ownership of `BattleGroundScore` objects and deletes them in the destructor and `Reset` method. It also deletes the `Group` objects if they are disbanded.
*   **Thread Safety:** The `BattleGround` class is not thread-safe. All modifications to its state should be done from the main server thread.
*   **Debug Commands:** The `HandleCommand` method allows admins to manually spawn/despawn events for testing purposes.

## Member Reference

**BattleGroundBroadcastBuilder**: Constructor for the builder class that constructs broadcast chat packets.

**operator()#3**: Method of `BattleGroundBroadcastBuilder` that builds the chat packet using `ChatHandler::BuildChatPacket`, retrieving text from `ObjectMgr::GetBroadcastText` and source info from `Player`.

**BattleGroundChatBuilder**: Constructor for the builder class that constructs chat packets with variable arguments.

**operator()#4**: Method of `BattleGroundChatBuilder` that builds the chat packet, formatting text with `vsnprintf` if arguments are provided, and using `ObjectMgr::GetMangosString`.

**BattleGroundYellBuilder**: Constructor for the builder class that constructs yell packets from creatures.

**operator()#5**: Method of `BattleGroundYellBuilder` that builds the yell packet, formatting text with `vsnprintf` if arguments are provided, and using `ObjectMgr::GetMangosString`.

**BattleGround2ChatBuilder**: Constructor for the builder class that constructs chat packets with two string arguments.

**operator()**: Method of `BattleGround2ChatBuilder` that builds the chat packet, formatting text with `snprintf` using two additional string arguments retrieved from `ObjectMgr::GetMangosString`.

**BattleGround2YellBuilder**: Constructor for the builder class that constructs yell packets with two string arguments.

**operator()#2**: Method of `BattleGround2YellBuilder` that builds the yell packet, formatting text with `snprintf` using two additional string arguments retrieved from `ObjectMgr::GetMangosString`.

**BattleGround**: Constructor initializes all member variables to default states.

**~BattleGround**: Destructor cleans up resources, removes the battleground from the manager, unloads the map, and logs the result.

**Update**: Core update loop handling empty instance cleanup, premature finish logic, starting sequence, ending sequence, and door despawning.

**FillInitialWorldState#4**: Inline function to write a world state pair to a buffer.

**FillInitialWorldState#2**: Inline function to write a world state pair to a buffer.

**FillInitialWorldState#3**: Inline function to write a world state pair to a buffer.

**FillInitialWorldState**: Inline function to write an array of world state pairs to a buffer.

**SetTeamStartLoc**: Sets the starting coordinates for a team.

**SendPacketToAll**: Sends a raw packet to all players in the battleground.

**SendPacketToTeam**: Sends a raw packet to all players on a specific team.

**PlaySoundToAll**: Plays a sound to all players.

**PlaySoundToTeam**: Plays a sound to a specific team.

**CastSpellOnTeam**: Casts a spell on all players of a specific team.

**RewardHonorToTeam**: Awards bonus honor to all players on a team.

**RewardReputationToTeam**: Awards reputation to all players on a team.

**UpdateWorldState**: Updates a world state field for all players.

**UpdateWorldStateForPlayer**: Updates a world state field for a single player.

**GetWinnerText**: Returns the broadcast text ID for the winning team.

**GetHeraldEntry**: Returns the NPC entry for the herald creature.

**EndBattleGround**: Handles the end-of-battle logic, distributing rewards and logging results.

**GetBonusHonorFromKill**: Calculates bonus honor based on kills and max level.

**GetHonorModifier**: Calculates a modifier for honor gains based on battle duration.

**GetBattlemasterEntry**: Returns the NPC entry for the battlemaster.

**RewardMark**: Awards the appropriate spell (Mark of Honor) to a player.

**RewardSpellCast**: Casts a reward spell on a player.

**RewardItem**: Attempts to give an item to a player, mailing it if the bag is full.

**SendRewardMarkByMail**: Sends an item to a player via mail.

**RewardQuestComplete**: Placeholder for quest completion rewards.

**BlockMovement**: Disables player movement.

**RemovePlayerAtLeave**: Removes a player from the battleground, updating counts and groups.

**Reset**: Resets the battleground to a pre-game state.

**StartBattleGround**: Initializes the battleground and registers it with the manager.

**AddPlayer**: Adds a player to the battleground, updating counts and groups.

**AddOrSetPlayerToCorrectBgGroup**: Ensures a player is in the correct raid group for their team.

**AddToBGFreeSlotQueue**: Adds the battleground to the free slot queue.

**RemoveFromBGFreeSlotQueue**: Removes the battleground from the free slot queue.

**GetFreeSlotsForTeam**: Calculates the number of free slots for a team.

**DecreaseInvitedCount**: Decrements the invited count for a team.

**IncreaseInvitedCount**: Increments the invited count for a team.

**GetInvitedCount**: Returns the number of players invited to a team.

**HasFreeSlots**: Checks if the battleground has room for more players.

**UpdatePlayerScore**: Updates a player's score.

**AddObject**: Dynamically creates a game object in the battleground map.

**DoorClose**: Closes a specific door by GUID.

**DoorOpen**: Opens a specific door by GUID.

**CanBeSpawned**: Checks if a creature should be spawned based on active events.

**OnObjectDBLoad**: Registers a creature with events and spawns/despawns it.

**GetSingleCreatureGuid**: Returns the GUID of the first creature registered for an event pair.

**GetSingleGameObjectGuid**: Returns the GUID of the first game object registered for an event pair.

**OnObjectDBLoad#2**: Registers a game object with events and spawns/despawns it.

**IsDoor**: Checks if an event pair corresponds to a door.

**OpenDoorEvent**: Opens all doors associated with an event.

**StartingEventDespawnDoors**: Despawns all doors associated with the initial door event.

**ReturnPlayersToHomeGY**: Teleports all players to their team's starting graveyard.

**SpawnEvent**: Spawns or despawns creatures and game objects associated with an event.

**SetSpawnEventMode**: Sets the spawn mode for creatures in an event.

**SpawnBGObject**: Spawns or despawns a game object by its GUID.

**SpawnBGCreature**: Spawns or despawns a creature by its GUID.

**DelObject**: Deletes a game object from the map.

**SendMessageToAll**: Broadcasts a localized chat message to all players.

**SendYellToAll**: Broadcasts a yell from a creature to all players.

**PSendMessageToAll**: Broadcasts a formatted chat message with variable arguments.

**SendMessage2ToAll**: Broadcasts a chat message with two additional string arguments.

**SendYell2ToAll**: Broadcasts a yell from a creature with two additional string arguments.

**EndNow**: Immediately ends the battleground.

**HandleTriggerBuff**: Handles buff triggers, randomly selecting a new buff type if enabled.

**HandleKillPlayer**: Handles player kills, awarding scores and marking victims.

**GetPlayerTeam**: Returns the team of a player.

**IsPlayerInBattleGround**: Checks if a player is in the battleground.

**PlayerAddedToBGCheckIfBGIsRunning**: Checks if the battleground is ending and sends final scores to a late-joining player.

**GetAlivePlayersCountByTeam**: Counts the number of alive players on a specific team.

**SetBgRaid**: Sets the raid group for a team.

**GetClosestGraveYard**: Finds the closest graveyard to a player's position.

**StopBattleGround**: Forces a premature end for debugging.

**HandleCommand**: Handles admin commands for debugging events.

---

<!-- machine-true, projected from graph.json -->

## Map — game_Battlegrounds_BattleGround

*Source:* BattleGround.cpp, BattleGround.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| BattleGroundBroadcastBuilder | ctor | — | — | — |
| operator()#3 | method | ChatHandler.Chat/BuildChatPacket, Object/GetObjectGuid, ObjectGuid/ObjectGuid, ObjectMgr/GetBroadcastText, Player.Main/GetName | — | — |
| BattleGroundChatBuilder | ctor | — | — | — |
| operator()#4 | method | ChatHandler.Chat/BuildChatPacket, Object/GetObjectGuid, ObjectGuid/ObjectGuid, ObjectMgr/GetMangosString, Player.Main/GetName | — | — |
| BattleGroundYellBuilder | ctor | — | — | — |
| operator()#5 | method | ChatHandler.Chat/BuildChatPacket, Creature.Main/GetName, Object/GetObjectGuid, ObjectMgr/GetMangosString | — | — |
| BattleGround2ChatBuilder | ctor | — | — | — |
| operator() | method | ChatHandler.Chat/BuildChatPacket, Object/GetObjectGuid, ObjectGuid/ObjectGuid, ObjectMgr/GetMangosString | — | — |
| BattleGround2YellBuilder | ctor | — | — | — |
| operator()#2 | method | ChatHandler.Chat/BuildChatPacket, Creature.Main/GetName, Object/GetObjectGuid, ObjectGuid/ObjectGuid, ObjectMgr/GetMangosString | — | — |
| BattleGround | ctor | — | BattleGroundMgr/CreateBattleGround | — |
| ~BattleGround | dtor | BattleGround/GetBracketId, BattleGround/GetClientInstanceID, BattleGround/GetInstanceID, BattleGround/GetStartTime, BattleGround/GetTypeID, BattleGround/GetWinner, BattleGroundMap/SetBG, BattleGroundMgr/DeleteClientVisibleInstanceId, BattleGroundMgr/RemoveBattleGround, Log.Main/Out, Map.Main/SetUnload, shared_Util/secsToTimeString | — | — |
| Update | method | BattleGround/GetBgMap, BattleGround/GetBracketId, BattleGround/GetMaxLevel, BattleGround/GetMinLevel, BattleGround/GetMinPlayersPerTeam, BattleGround/GetName, BattleGround/GetPlayersCountByTeam, BattleGround/GetPlayersSize, BattleGround/GetStartDelayTime, BattleGround/GetStatus, BattleGround/GetTypeID, BattleGround/ModifyStartDelayTime, BattleGround/SetStartDelayTime, BattleGround/SetStatus, BattleGround/SetupBattleGround, BattleGround/StartingEventCloseDoors, BattleGround/StartingEventOpenDoors, BattleGroundMgr/BgQueueTypeId, BattleGroundMgr/GetPrematureFinishTime, BattleGroundMgr/isTesting, BattleGroundMgr/ScheduleQueueUpdate, Map.Main/GetCreateTime, World/getConfig, World/SendWorldText | BattleGroundAB/Update, BattleGroundAV/Update, BattleGroundWS/Update, Map.Main/Update | — |
| FillInitialWorldState#4 | function | — | BattleGroundWS/FillInitialWorldStates | — |
| FillInitialWorldState#2 | function | — | BattleGroundAB/FillInitialWorldStates, BattleGroundAV/FillInitialWorldStates, BattleGroundWS/FillInitialWorldStates | — |
| FillInitialWorldState#3 | function | — | BattleGroundAB/FillInitialWorldStates, BattleGroundAV/FillInitialWorldStates | — |
| FillInitialWorldState | function | — | — | — |
| SetTeamStartLoc | method | BattleGround/GetTeamIndexByTeamId | BattleGroundMgr/CreateBattleGround | — |
| SendPacketToAll | method | Log.Main/Out, ObjectGuid/GetString, ObjectMgr/GetPlayer, Player.Main/GetSession, WorldSession.Main/SendPacket | — | — |
| SendPacketToTeam | method | Log.Main/Out, ObjectGuid/GetString, ObjectMgr/GetPlayer, Player.Main/GetSession, Player.Main/GetTeam, WorldSession.Main/SendPacket | — | — |
| PlaySoundToAll | method | BattleGroundMgr/BuildPlaySoundPacket, WorldPacket/WorldPacket | BattleGroundAB/EventPlayerClickedOnFlag, BattleGroundAB/Update, BattleGroundAV/ChangeMineOwner, BattleGroundAV/EventPlayerAssaultsPoint, BattleGroundAV/EventPlayerDefendsPoint, BattleGroundWS/EventPlayerCapturedFlag, BattleGroundWS/EventPlayerClickedOnFlag, BattleGroundWS/RespawnFlag, BattleGroundWS/RespawnFlagAfterDrop | — |
| PlaySoundToTeam | method | BattleGroundMgr/BuildPlaySoundPacket, Log.Main/Out, ObjectGuid/GetString, ObjectMgr/GetPlayer, Player.Main/GetSession, Player.Main/GetTeam, WorldPacket/WorldPacket, WorldSession.Main/SendPacket | — | — |
| CastSpellOnTeam | method | Log.Main/Out, ObjectGuid/GetString, ObjectMgr/GetPlayer, Player.Main/GetTeam, SpellCaster/CastSpell#2 | BattleGroundAB/_NodeOccupied, BattleGroundAV/HandleKillUnit, BattleGroundAV/Update, BattleGroundAV/UpgradeArmor | — |
| RewardHonorToTeam | method | Log.Main/Out, ObjectGuid/GetString, ObjectMgr/GetPlayer, Player.Main/GetTeam | BattleGroundAB/EndBattleGround, BattleGroundAB/Update, BattleGroundAV/EndBattleGround, BattleGroundAV/EventPlayerDestroyedPoint, BattleGroundAV/HandleKillUnit, BattleGroundWS/EndBattleGround, BattleGroundWS/EventPlayerCapturedFlag | — |
| RewardReputationToTeam | method | Log.Main/Out, ObjectGuid/GetString, ObjectMgr/GetFactionEntry, ObjectMgr/GetPlayer, Player.Main/CalculateReputationGain, Player.Main/GetReputationMgr, Player.Main/GetTeam, ReputationMgr/ModifyReputation | BattleGroundAB/Update, BattleGroundAV/EndBattleGround, BattleGroundAV/EventPlayerDestroyedPoint, BattleGroundAV/HandleKillUnit, BattleGroundAV/HandleQuestComplete, BattleGroundWS/EventPlayerCapturedFlag | — |
| UpdateWorldState | method | BattleGroundMgr/BuildUpdateWorldStatePacket, WorldPacket/WorldPacket | BattleGroundAB/Update, BattleGroundAB/_SendNodeUpdate, BattleGroundAV/SendMineWorldStates, BattleGroundAV/UpdateNodeWorldState, BattleGroundAV/UpdateScore, BattleGroundWS/EventPlayerCapturedFlag, BattleGroundWS/EventPlayerClickedOnFlag, BattleGroundWS/EventPlayerDroppedFlag, BattleGroundWS/RespawnFlagAfterDrop, BattleGroundWS/UpdateFlagState, BattleGroundWS/UpdateTeamScore | — |
| UpdateWorldStateForPlayer | method | BattleGroundMgr/BuildUpdateWorldStatePacket, Player.Main/GetSession, WorldPacket/WorldPacket, WorldSession.Main/SendPacket | — | — |
| GetWinnerText | method | BattleGround/GetTypeID | — | — |
| GetHeraldEntry | method | BattleGround/GetTypeID | — | — |
| EndBattleGround | method | BattleGround/GetBracketId, BattleGround/GetInstanceID, BattleGround/GetPlayersCountByTeam, BattleGround/GetStartTime, BattleGround/GetTypeID, BattleGround/SetEndTime, BattleGround/SetStatus, BattleGround/SetWinner, BattleGroundMgr/BgQueueTypeId, BattleGroundMgr/BuildBattleGroundStatusPacket, BattleGroundMgr/BuildPvpLogDataPacket, BattleGroundMgr/ScheduleQueueUpdate, ByteBuffer/empty, Database/CreateStatement, HostileRefManager/deleteReferences, Log.Main/Out, ObjectGuid/GetString, ObjectMgr/GetPlayer, Player.Main/GetBattleGroundQueueIndex, Player.Main/GetSession, Player.Main/Player, Player.Main/ResurrectPlayer, Player.Main/SpawnCorpseBones, SqlPreparedStatement/Execute#2, SqlStatement/addUInt32, SqlStatementID/SqlStatementID, Unit.Main/CombatStop, Unit.Main/CombatStopWithPets, Unit.Main/GetHostileRefManager, Unit.Main/HasAuraType, Unit.Main/IsAlive, Unit.Main/RemoveSpellsCausingAura, World/getConfig, WorldPacket/WorldPacket, WorldSession.Main/SendPacket | BattleGroundAB/EndBattleGround, BattleGroundAV/EndBattleGround, BattleGroundWS/EndBattleGround | logs_battleground |
| GetBonusHonorFromKill | method | BattleGround/GetMaxLevel, Formulas/GetHonorGain | BattleGroundAV/EndBattleGround, BattleGroundAV/EventPlayerDestroyedPoint, BattleGroundAV/HandleKillUnit | — |
| GetHonorModifier | method | BattleGround/GetStartTime | BattleGroundAV/EndBattleGround, BattleGroundAV/HandleKillUnit | — |
| GetBattlemasterEntry | method | BattleGround/GetTypeID | — | — |
| RewardMark | method | BattleGround/GetAllianceLoseSpell, BattleGround/GetAllianceWinSpell, BattleGround/GetHordeLoseSpell, BattleGround/GetHordeWinSpell, Player.Main/GetTeamId, Player.Main/IsBot | — | — |
| RewardSpellCast | method | Log.Main/Out, SpellCaster/CastSpell, SpellMgr/GetSpellEntry, SpellMgr/Instance | — | — |
| RewardItem | method | Log.Main/Out, Player.Main/CanStoreNewItem, Player.Main/SendNewItem, Player.Main/StoreNewItem | — | — |
| SendRewardMarkByMail | method | BattleGround/GetName, game_Mail_Mail/AddItem, game_Mail_Mail/MailDraft, game_Mail_Mail/MailReceiver, game_Mail_Mail/MailSender#2, game_Mail_Mail/SendMailTo, game_Objects_Item/CreateItem, game_Objects_Item/SaveToDB, Object/GetObjectGuid, ObjectMgr/GetItemLocale, ObjectMgr/GetItemPrototype, Player.Main/GetSession, WorldSession.Main/GetMangosString, WorldSession.Main/GetSessionDbLocaleIndex | Spell.Effects/DoCreateItem | — |
| RewardQuestComplete | method | — | — | — |
| BlockMovement | method | Player.Main/SetClientControl | — | — |
| RemovePlayerAtLeave | method | BattleGround/GetBgMap, BattleGround/GetBgRaid, BattleGround/GetBracketId, BattleGround/GetStatus, BattleGround/GetTypeID, BattleGround/RemovePlayer, BattleGround/UpdatePlayersCountByTeam, BattleGroundMgr/BgQueueTypeId, BattleGroundMgr/BuildBattleGroundStatusPacket, BattleGroundMgr/BuildPlayerLeftBattleGroundPacket, BattleGroundMgr/ScheduleQueueUpdate, game_Group_Group/RemoveMember, Log.Main/Out, ObjectMgr/GetPlayer, Player.Main/GetBattleGroundQueueIndex, Player.Main/GetName, Player.Main/GetSession, Player.Main/GetTeam, Player.Main/RemoveBattleGroundQueueId, Player.Main/ResurrectPlayer, Player.Main/SetBattleGroundId, Player.Main/SetBGTeam, Player.Main/SpawnCorpseBones, Player.Main/TeleportToBGEntryPoint, Unit.Main/HasAuraType, Unit.Main/IsAlive, Unit.Main/RemoveSpellsCausingAura, WorldObject.Object/FindMap, WorldPacket/WorldPacket, WorldSession.Main/SendPacket | Player.Main/LeaveBattleground, WorldSession.BattleGroundHandler/HandleBattleFieldPortOpcode | — |
| Reset | method | BattleGround/SetEndTime, BattleGround/SetStartTime, BattleGround/SetStatus, BattleGround/SetWinner, Log.Main/Out | BattleGroundAB/Reset, BattleGroundAV/Reset, BattleGroundMgr/CreateNewBattleGround, BattleGroundWS/Reset | — |
| StartBattleGround | method | BattleGround/GetInstanceID, BattleGround/GetTypeID, BattleGround/SetStartTime, BattleGroundMgr/AddBattleGround | BattleGroundMgr/CheckCreateNewBg | — |
| AddPlayer | method | BattleGround/UpdatePlayersCountByTeam, BattleGroundMgr/BuildPlayerJoinedBattleGroundPacket, Log.Main/Out, Object/GetObjectGuid, Player.Main/GetBGTeam, Player.Main/GetName, WorldPacket/WorldPacket | BattleGroundAB/AddPlayer, BattleGroundAV/AddPlayer, BattleGroundWS/AddPlayer, WorldSession.MovementHandler/HandleMoveWorldportAck | — |
| AddOrSetPlayerToCorrectBgGroup | method | BattleGround/GetBgRaid, game_Group_Group/AddMember, game_Group_Group/ChangeLeader, game_Group_Group/Create, game_Group_Group/Group, Group/GetMemberGroup, Group/IsLeader, Group/IsMember, Player.Main/GetName, Player.Main/GetOriginalGroup, Player.Main/SetBattleGroundRaid | — | — |
| AddToBGFreeSlotQueue | method | — | — | — |
| RemoveFromBGFreeSlotQueue | method | BattleGround/GetInstanceID | BattleGroundMgr/CheckFreeSlots | — |
| GetFreeSlotsForTeam | method | BattleGround/GetMaxPlayersPerTeam, BattleGround/GetStatus | BattleGroundMgr/FillPlayersToBg | — |
| DecreaseInvitedCount | method | Errors/PrintStacktraceAndThrow, Log.Main/Out | BattleGroundMgr/RemovePlayer | — |
| IncreaseInvitedCount | method | Log.Main/Out | BattleGroundMgr/InviteGroupToBG | — |
| GetInvitedCount | method | Log.Main/Out | — | — |
| HasFreeSlots | method | BattleGround/GetMaxPlayers, BattleGround/GetPlayersSize | BattleGroundMgr/CheckFreeSlots | — |
| UpdatePlayerScore | method | HonorMgr/Add, Log.Main/Out, Object/GetObjectGuid, Player.Main/GetHonorMgr | BattleGroundAB/UpdatePlayerScore, BattleGroundAV/UpdatePlayerScore, BattleGroundWS/UpdatePlayerScore | — |
| AddObject | method | BattleGround/GetBgMap, GameObject/AddToWorld, GameObject/Create, GameObject/GameObject, Log.Main/Out, Map.Main/GenerateLocalLowGuid, Object/GetObjectGuid | BattleGroundAB/SetupBattleGround | — |
| DoorClose | method | BattleGround/GetBgMap, GameObject/GetGoState, GameObject/getLootState, GameObject/SetLootState, GameObject/UseDoorOrButton, Log.Main/Out, Map.Main/GetGameObject, ObjectGuid/GetString | — | — |
| DoorOpen | method | BattleGround/GetBgMap, GameObject/SetLootState, GameObject/UseDoorOrButton, Log.Main/Out, Map.Main/GetGameObject, ObjectGuid/GetString | — | — |
| CanBeSpawned | method | BattleGround/IsActiveEvent, BattleGroundMgr/GetCreatureEventsVector, Errors/PrintStacktraceAndThrow, Object/GetGUIDLow | CreatureGroups/Respawn | — |
| OnObjectDBLoad | method | BattleGround/IsActiveEvent, BattleGroundMgr/GetCreatureEventsVector, Errors/PrintStacktraceAndThrow, Object/GetGUIDLow, Object/GetObjectGuid | — | — |
| GetSingleCreatureGuid | method | ObjectGuid/ObjectGuid | BattleBotAI.BattleBotWaypoints/StartNewPathToObjective, BattleGroundAV/ChangeMineOwner, BattleGroundAV/EventPlayerAssaultsPoint, BattleGroundAV/EventPlayerDefendsPoint, BattleGroundAV/EventPlayerDestroyedPoint, BattleGroundAV/HandleKillUnit, BattleGroundAV/Update | — |
| GetSingleGameObjectGuid | method | ObjectGuid/ObjectGuid | BattleBotAI.BattleBotWaypoints/StartNewPathToObjective | — |
| OnObjectDBLoad#2 | method | BattleGround/GetStatus, BattleGround/IsActiveEvent, BattleGroundMgr/GetGameObjectEventsVector, Errors/PrintStacktraceAndThrow, Object/GetGUIDLow, Object/GetObjectGuid | — | — |
| IsDoor | method | Log.Main/Out | — | — |
| OpenDoorEvent | method | BattleGround/IsActiveEvent, Log.Main/Out | BattleGroundAB/StartingEventOpenDoors, BattleGroundAV/StartingEventOpenDoors, BattleGroundWS/StartingEventOpenDoors | — |
| StartingEventDespawnDoors | method | BattleGround/GetBgMap, BattleGround/IsActiveEvent, Map.Main/GetGameObject, WorldObject.Object/AddObjectToRemoveList | — | — |
| ReturnPlayersToHomeGY | method | BattleGround/GetBgMap, BattleGround/GetTeamStartLoc, Map.Main/GetPlayer, Player.Main/GetTeam, Player.Main/IsGameMaster, Player.Main/RepopAtGraveyard, WorldObject.Object/IsWithinDist3d | — | — |
| SpawnEvent | method | BattleGround/IsActiveEvent, BattleGround/OnEventStateChanged, BattleGroundMgr/GetCreatureEventsVector, Errors/PrintStacktraceAndThrow, ObjectGuid/GetCounter | BattleGroundAB/StartingEventOpenDoors, BattleGroundAB/_CreateBanner, BattleGroundAV/ChangeMineOwner, BattleGroundAV/HandleKillUnit, BattleGroundAV/HandleQuestComplete, BattleGroundAV/PopulateMineNode, BattleGroundAV/PopulateNode, BattleGroundAV/resetCavalryChallengeInvocation, BattleGroundAV/ResetTamedEvent, BattleGroundAV/StartingEventOpenDoors, BattleGroundAV/Update, BattleGroundWS/EventPlayerCapturedFlag, BattleGroundWS/EventPlayerClickedOnFlag, BattleGroundWS/RespawnFlag, BattleGroundWS/StartingEventOpenDoors, ThreatListCopier.battleground_alterac/av_world_boss_baseai, ThreatListCopier.battleground_alterac/JustDied#3 | — |
| SetSpawnEventMode | method | BattleGround/IsActiveEvent, BattleGroundMgr/GetCreatureEventsVector, Errors/PrintStacktraceAndThrow, ObjectGuid/GetCounter | BattleGroundAV/HandleKillUnit, BattleGroundAV/PopulateMineNode, BattleGroundAV/PopulateNode | — |
| SpawnBGObject | method | BattleGround/GetBgMap, GameObject/CreateGameObject, GameObject/GetGOInfo, GameObject/getLootState, GameObject/GetRespawnTime, GameObject/HasStaticDBSpawnData, GameObject/LoadFromDB, GameObject/SetGoState, GameObject/SetLootState, GameObject/SetRespawnDelay, GameObject/SetRespawnTime, Map.Main/GetGameObject, Object/GetEntry, ObjectGuid/GetCounter, ObjectGuid/GetEntry, WorldObject.Object/AddObjectToRemoveList | BattleGroundAB/StartingEventCloseDoors, BattleGroundAB/StartingEventOpenDoors | — |
| SpawnBGCreature | method | BattleGround/GetBgMap, Creature.Main/GetRespawnTime, Creature.Main/IsDespawned, Creature.Main/RemoveCorpse, Creature.Main/SetDeathState, Creature.Main/SetRespawnDelay, Creature.Main/SetRespawnTime, Map.Main/GetCreature | — | — |
| DelObject | method | BattleGround/GetBgMap, GameObject/Delete, GameObject/SetRespawnTime, Log.Main/Out, Map.Main/GetGameObject, ObjectGuid/Clear, ObjectGuid/GetString, ObjectGuid/operator! | — | — |
| SendMessageToAll | method | — | BattleGroundAB/Update, BattleGroundWS/EventPlayerCapturedFlag, BattleGroundWS/EventPlayerClickedOnFlag, BattleGroundWS/EventPlayerDroppedFlag, BattleGroundWS/RespawnFlag, BattleGroundWS/RespawnFlagAfterDrop | — |
| SendYellToAll | method | BattleGround/GetBgMap, Map.Main/GetCreature | BattleGroundAV/HandleKillUnit, BattleGroundAV/Update | — |
| PSendMessageToAll | method | — | — | — |
| SendMessage2ToAll | method | — | BattleGroundAB/EventPlayerClickedOnFlag, BattleGroundAB/Update | — |
| SendYell2ToAll | method | BattleGround/GetBgMap, Map.Main/GetCreature | BattleGroundAV/ChangeMineOwner, BattleGroundAV/EventPlayerAssaultsPoint, BattleGroundAV/EventPlayerDefendsPoint, BattleGroundAV/EventPlayerDestroyedPoint | — |
| EndNow | method | BattleGround/SetEndTime, BattleGround/SetStatus, BattleGroundMgr/BuildPvpLogDataPacket, ByteBuffer/empty | — | — |
| HandleTriggerBuff | method | BattleGround/GetTypeID, GameObject/GetGoType, GameObject/isSpawned, GameObject/SetLootState, Log.Main/Out, Object/GetEntry, Object/GetGuidStr, Object/GetObjectGuid, ObjectGuid/operator!=, shared_Util/urand | GameObject/Update | — |
| HandleKillPlayer | method | ObjectMgr/GetPlayer, Player.Main/GetTeam, Player.Main/IsAtGroupRewardDistance, Unit.Main/GetFactionTemplateId, Unit.Main/HasAura#2, WorldObject.Object/SetFlag | BattleGroundAV/HandleKillPlayer, BattleGroundWS/HandleKillPlayer, Unit.Main/Kill | — |
| GetPlayerTeam | method | — | — | — |
| IsPlayerInBattleGround | method | — | — | — |
| PlayerAddedToBGCheckIfBGIsRunning | method | BattleGround/GetEndTime, BattleGround/GetStartTime, BattleGround/GetStatus, BattleGround/GetTypeID, BattleGroundMgr/BgQueueTypeId, BattleGroundMgr/BuildBattleGroundStatusPacket, BattleGroundMgr/BuildPvpLogDataPacket, Player.Main/GetBattleGroundQueueIndex, Player.Main/GetSession, WorldPacket/WorldPacket, WorldSession.Main/SendPacket | — | — |
| GetAlivePlayersCountByTeam | method | ObjectMgr/GetPlayer, Unit.Main/IsAlive | — | — |
| SetBgRaid | method | BattleGround/GetTeamIndexByTeamId, Group/SetBattlegroundGroup | game_Group_Group/~Group | — |
| GetClosestGraveYard | method | ObjectMgr/GetClosestGraveYard, Player.Main/GetTeam, WorldObject.Object/GetMapId, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ | Player.Main/RepopAtGraveyard | — |
| StopBattleGround | method | — | ChatHandler.MiscCommands/HandleBGStopCommand | — |
| HandleCommand | method | ChatHandler.Chat/PSendSysMessage | BattleGroundAV/HandleCommand, ChatHandler.MiscCommands/HandleBGCustomCommand | — |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `logs_battleground`: time timestamp?, bgid int(11)?, bgtype int(11)?, bgteamcount int(11)?, bgduration int(11)?, playerGuid int(11)?, team int(11)?, deaths int(11)?, honorBonus int(11)?, honorableKills int(11)?

*`?` = nullable, `PK` = primary key column.*


---

<!-- verify: failed-members | invented: operator -->
