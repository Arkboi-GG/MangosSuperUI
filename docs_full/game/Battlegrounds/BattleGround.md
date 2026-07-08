<!-- provenance: verbose -->
# BattleGround

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# BattleGround

**Purpose & Responsibilities**

`BattleGround` is the abstract base class for instanced Player-versus-Player (PvP) battlegrounds. It centralizes common lifecycle management, state tracking, and utility interfaces required to run a battleground instance, independent of specific map rules (implemented in subclasses like `BattleGroundAB`, `BattleGroundAV`, `BattleGroundWS`).

Key responsibilities:
1.  **Lifecycle Management:** Tracks status (waiting, starting, in-progress, ended), manages start/end timers, and handles the countdown sequence.
2.  **Player & Team Management:** Maintains participant lists, tracks team counts, manages associated raid groups, and handles player entry/exit.
3.  **Scoring & Rewards:** Aggregates player statistics (kills, deaths, honor) and distributes rewards (honor, reputation, items, spells) upon conclusion.
4.  **Event System:** Provides a generic framework for spawning/despawning creatures and game objects based on logical "events" (groupings of entities tied to gameplay triggers).
5.  **Communication:** Broadcasts messages, sounds, and world state updates to players within the instance.

## Member-by-Member Behavior

### Lifecycle and State Management

The battleground's state is driven by `m_status`, `m_startTime`, `m_endTime`, and `m_startDelayTime`.

*   **SetupBattleGround**: Virtual hook for subclasses to perform initial setup. Called by `game_Battlegrounds_BattleGround/Update`. Base returns `true`.
*   **StartingEventCloseDoors** / **StartingEventOpenDoors**: Virtual hooks called by `game_Battlegrounds_BattleGround/Update` during the pre-battle countdown. Subclasses override to manipulate map geometry. Base implementations are empty.
*   **OnEventStateChanged**: Called by `game_Battlegrounds_BattleGround/SpawnEvent` when an event's active state changes. Base is empty.
*   **CheckSpellCast**: Validates spell casting within the battleground. Called by `BattleGroundAV/CheckSpellCast` and `Spell.Main/CheckCast`. Base allows all spells (`SPELL_CAST_OK`).
*   **GetName** / **GetTypeID**: Accessors for display name and internal type ID. `GetTypeID` is heavily used by `BattleGroundMgr` for routing and by `BattleBotAI` for behavior logic.
*   **GetBracketId**: Returns the level bracket. Used by `BattleGroundMgr` for queue matching and by subclasses for end-game logic.
*   **GetInstanceID**: Returns the unique map instance ID. Relies on `m_map` being non-null; returns 0 if unset. Used by `BattleGroundMgr` and `Player.Main` for instance tracking.
*   **GetStatus**: Returns the current `BattleGroundStatus`. Frequently called by `BattleGroundMgr`, `Player.Main`, `Spell.Main`, and AI modules to validate actions.
*   **GetClientInstanceID**: Returns the instance ID sent to clients. Used by `BattleGroundMgr` for packet construction.
*   **GetStartTime** / **GetEndTime**: Accessors for start/end timestamps. Used by `ChatHandler` and `WorldSession` for status reporting and player porting.
*   **GetMaxPlayers** / **GetMinPlayers**: Accessors for total player limits. `GetMaxPlayers` is used by `game_Battlegrounds_BattleGround/HasFreeSlots`.
*   **GetMinLevel** / **GetMaxLevel**: Accessors for level range. Used by `Player.Main` for eligibility and `ChatHandler` for displays.
*   **GetAllianceWinSpell** / **GetAllianceLoseSpell** / **GetHordeWinSpell** / **GetHordeLoseSpell**: Accessors for victory/defeat reward spell IDs. Used by `game_Battlegrounds_BattleGround/RewardMark`.
*   **GetMaxPlayersPerTeam** / **GetMinPlayersPerTeam**: Accessors for per-team limits. Used by `BattleGroundMgr` for balancing and `game_Battlegrounds_BattleGround/Update` for start conditions.
*   **GetStartDelayTime**: Returns remaining start delay. Used by `game_Battlegrounds_BattleGround/Update` and `BattleGroundAV/Update` for countdowns.
*   **GetWinner**: Returns the winning team. Used by `BattleGroundMgr` for logging and `game_Battlegrounds_BattleGround/~BattleGround` for cleanup.
*   **Set... Methods**: Setters for the above properties. Mostly called by `BattleGroundMgr/CreateBattleGround` or `CreateNewBattleGround`. `SetStatus` is also called by `game_Battlegrounds_BattleGround` to transition states.
*   **ModifyStartDelayTime** / **SetStartDelayTime**: Adjusts the countdown. `ModifyStartDelayTime` subtracts a delta (used in `Update`); `SetStartDelayTime` sets an absolute value (used by `ChatHandler`).
*   **SetWinner**: Marks the winning team. Called by `game_Battlegrounds_BattleGround/EndBattleGround` and `Reset`.

### Player and Team Management

*   **GetPlayers**: Returns the map of all players. Used by `ChatHandler` and `WorldSession` for debugging/position queries.
*   **GetPlayersSize**: Returns total player count. Used by `game_Battlegrounds_BattleGround/HasFreeSlots` and `Update`.
*   **GetPlayerScoresBegin** / **GetPlayerScoresEnd** / **GetPlayerScoresSize**: Iterators and size accessor for `m_playerScores`. Used exclusively by `BattleGroundMgr/BuildPvpLogDataPacket`.
*   **GetFinalScorePacket**: Returns the pre-built final score `WorldPacket`. Used by `WorldSession.BattleGroundHandler/HandlePVPLogDataOpcode`.
*   **GetTeamIndexByTeamId**: Converts `Team` enum to internal index (0/1). Used extensively for array access (`m_playersCount`, `m_bgRaids`).
*   **GetPlayersCountByTeam**: Returns player count for a specific team. Used by `game_Battlegrounds_BattleGround/EndBattleGround` and `Update`.
*   **UpdatePlayersCountByTeam**: Increments/decrements team counter. Called by `game_Battlegrounds_BattleGround/AddPlayer` and `RemovePlayerAtLeave`.
*   **GetBgRaid**: Returns the `Group` object for a team. Used by `game_Battlegrounds_BattleGround/AddOrSetPlayerToCorrectBgGroup`.
*   **GetTeamStartLoc**: Retrieves team start coordinates. Used by `BattleGroundMgr/SendToBattleGround` and `game_Battlegrounds_BattleGround/ReturnPlayersToHomeGY`.
*   **RemovePlayer**: Virtual cleanup method. Base is empty; subclasses override. Called by `game_Battlegrounds_BattleGround/RemovePlayerAtLeave`.

### Event and Object Spawning

Events are identified by pairs `(event1, event2)` and manage groups of creatures/objects.

*   **IsActiveEvent**: Checks if an event pair is active. Used by `game_Battlegrounds_BattleGround/SpawnEvent`, `CanBeSpawned`, and AI modules.
*   **ActivateEventWithoutSpawn**: Marks an event active without spawning. Used by `ThreatListCopier.battleground_alterac/JustRespawned#3`.
*   **HandleAreaTrigger**: Virtual hook for area triggers. Must be implemented by subclasses. Called by `WorldSession.MiscHandler/HandleAreaTriggerOpcode`.
*   **HandleKillUnit**: Virtual hook for creature kills. Called by `Unit.Main/Kill`. Subclasses use this for scoring/events.
*   **EventPlayerDroppedFlag** / **EventPlayerClickedOnFlag** / **EventPlayerCapturedFlag**: Virtual hooks for flag mechanics. Called by `Player.Main`, `GameObject`, and `Spell` effects. Base implementations are empty.
*   **HandlePlayerUnderMap**: Virtual hook for under-map detection. Base returns `false`.
*   **GetOtherTeam** / **GetOtherTeamIndex**: Static helpers for opposing team/index. Used by subclasses.
*   **GetPlayerSkinRefLootId** / **SetPlayerSkinRefLootId**: Accessors for skinning loot table ID. Used by `Player.Main/SendLoot` and `BattleGroundMgr/CreateBattleGround`.

### Utility and Communication

*   **FillInitialWorldStates**: Virtual method to populate world state packets. Called by `Player.Main/SendInitWorldStates`. Subclasses override for map-specific UI.
*   **GetMapId** / **SetMapId**: Accessors for map ID. `GetMapId` is used by `BattleGroundMgr` and `Player.Main` for teleportation/validation.
*   **GetBgMap** / **SetBgMap**: Accessors for `BattleGroundMap` pointer. `GetBgMap` asserts non-null. Used by `game_Battlegrounds_BattleGround` methods and subclasses.

## Cross-Unit Boundaries

*   **BattleGroundMgr**: Primary collaborator. Creates instances, manages queues, and invokes lifecycle methods (`Update`, `StartBattleGround`). Reads status/counts/scores to make scheduling decisions.
*   **Player.Main**: Joins/leaves instances, checks eligibility, and receives rewards. Frequently calls `GetStatus` to validate actions.
*   **BattleGroundSubclasses (AB, AV, WS)**: Inherit from `BattleGround`, overriding virtual methods (`HandleKillUnit`, `SetupBattleGround`, etc.). They call base methods to maintain consistent state.
*   **BattleBotAI**: Queries `BattleGround` for status/type/team info to determine bot behavior.
*   **Spell.Main / Spell.Effects**: Checks `BattleGround` via `CheckSpellCast` for validity. Effects may trigger events like `EventPlayerClickedOnFlag`.
*   **WorldSession**: Handles network opcodes, calling `BattleGround` for packet data (status, positions, scores) and processing player actions.
*   **ChatHandler**: Admin commands inspect/manipulate state, calling getters/setters.

## Data Model

This unit does not directly interact with database tables. All state is held in memory within the `BattleGround` object and its associated maps (`m_players`, `m_playerScores`).

## Notable Implementation Details

*   **Map Pointer Dependency**: `GetInstanceID` depends on `m_map` being non-null. `GetBgMap()` asserts this, enforcing strict initialization order: `BattleGround` must be linked to its `BattleGroundMap` before instance-specific queries.
*   **Event System Optimization**: Events use a `std::map<uint32, EventObjects>` where the key is a packed `uint32` of two `uint8` event IDs, avoiding nested map overhead.
*   **Virtual Destructor**: Ensures proper cleanup of derived classes.
*   **Hardcoded Team Indices**: `GetTeamIndexByTeamId` hardcodes Alliance as 0 and Horde as 1, pervasive in array accesses.

## Member Reference

**SetupBattleGround**: Virtual method called by `game_Battlegrounds_BattleGround/Update` to initialize the battleground. Base implementation returns true.

**StartingEventCloseDoors**: Virtual method called by `game_Battlegrounds_BattleGround/Update` to close doors during the pre-battle sequence. Base implementation is empty.

**StartingEventOpenDoors**: Virtual method called by `game_Battlegrounds_BattleGround/Update` to open doors during the pre-battle sequence. Base implementation is empty.

**OnEventStateChanged**: Virtual method called by `game_Battlegrounds_BattleGround/SpawnEvent` when an event's active state changes. Base implementation is empty.

**CheckSpellCast**: Virtual method called by `BattleGroundAV/CheckSpellCast` and `Spell.Main/CheckCast` to validate spell casting. Base implementation allows all spells.

**GetName**: Returns the battleground's display name. Called by `BattleGroundMgr`, `ChatHandler`, and `game_Battlegrounds_BattleGround` for identification and logging.

**GetTypeID**: Returns the internal type ID. Extensively called by `BattleGroundMgr`, `BattleBotAI`, and subclasses to route logic and determine behavior.

**GetBracketId**: Returns the level bracket. Called by `BattleGroundMgr` and subclasses for queue matching and end-game logic.

**GetInstanceID**: Returns the map instance ID. Depends on `m_map` being set. Called by `BattleGroundMgr`, `Player.Main`, and `WorldSession` for instance tracking.

**GetStatus**: Returns the current status enum. Frequently called by `BattleGroundMgr`, `Player.Main`, `Spell.Main`, and AI modules to validate actions.

**GetClientInstanceID**: Returns the client-facing instance ID. Called by `BattleGroundMgr` for network packets.

**GetStartTime**: Returns the start timestamp. Called by `ChatHandler` and `WorldSession` for status reporting.

**GetEndTime**: Returns the end timestamp. Called by `game_Battlegrounds_BattleGround` and `WorldSession`.

**GetMaxPlayers**: Returns the total player limit. Called by `game_Battlegrounds_BattleGround/HasFreeSlots`.

**GetMinPlayers**: Returns the minimum player requirement. No external callers listed in MAP.

**GetMinLevel**: Returns the minimum level. Called by `Player.Main` and `ChatHandler` for eligibility checks.

**GetMaxLevel**: Returns the maximum level. Called by `Player.Main` and `ChatHandler` for eligibility checks.

**GetAllianceWinSpell**: Returns the Alliance victory spell ID. Called by `game_Battlegrounds_BattleGround/RewardMark`.

**GetAllianceLoseSpell**: Returns the Alliance defeat spell ID. Called by `game_Battlegrounds_BattleGround/RewardMark`.

**GetHordeWinSpell**: Returns the Horde victory spell ID. Called by `game_Battlegrounds_BattleGround/RewardMark`.

**GetHordeLoseSpell**: Returns the Horde defeat spell ID. Called by `game_Battlegrounds_BattleGround/RewardMark`.

**GetMaxPlayersPerTeam**: Returns the per-team player limit. Called by `BattleGroundMgr` and `WorldSession`.

**GetMinPlayersPerTeam**: Returns the per-team minimum player count. Called by `BattleGroundMgr` and `game_Battlegrounds_BattleGround/Update`.

**GetStartDelayTime**: Returns the remaining start delay. Called by `game_Battlegrounds_BattleGround/Update` and `BattleGroundAV/Update`.

**GetWinner**: Returns the winning team. Called by `BattleGroundMgr` and `game_Battlegrounds_BattleGround/~BattleGround`.

**SetName**: Sets the display name. Called by `BattleGroundMgr/CreateBattleGround`.

**SetTypeID**: Sets the type ID. Called by `BattleGroundMgr/CreateBattleGround`.

**SetBracketId**: Sets the bracket ID. Called by `BattleGroundMgr/CreateNewBattleGround`.

**SetStatus**: Sets the current status. Called by `BattleGroundMgr` and `game_Battlegrounds_BattleGround` to transition states.

**SetClientInstanceID**: Sets the client instance ID. Called by `BattleGroundMgr/CreateNewBattleGround`.

**SetStartTime**: Sets the start timestamp. Called by `game_Battlegrounds_BattleGround/Reset` and `StartBattleGround`.

**SetEndTime**: Sets the end timestamp. Called by `game_Battlegrounds_BattleGround/EndBattleGround`, `EndNow`, and `Reset`.

**SetMaxPlayers**: Sets the total player limit. Called by `BattleGroundMgr/CreateBattleGround`.

**SetMinPlayers**: Sets the minimum player requirement. Called by `BattleGroundMgr/CreateBattleGround`.

**SetAllianceWinSpell**: Sets the Alliance victory spell. Called by `BattleGroundMgr/CreateBattleGround`.

**SetAllianceLoseSpell**: Sets the Alliance defeat spell. Called by `BattleGroundMgr/CreateBattleGround`.

**SetHordeWinSpell**: Sets the Horde victory spell. Called by `BattleGroundMgr/CreateBattleGround`.

**SetHordeLoseSpell**: Sets the Horde defeat spell. Called by `BattleGroundMgr/CreateBattleGround`.

**SetLevelRange**: Sets the min/max levels. Called by `BattleGroundMgr/CreateBattleGround`.

**SetWinner**: Sets the winning team. Called by `game_Battlegrounds_BattleGround/EndBattleGround` and `Reset`.

**ModifyStartDelayTime**: Decrements the start delay. Called by `game_Battlegrounds_BattleGround/Update`.

**SetStartDelayTime**: Sets the start delay absolutely. Called by `ChatHandler` and `game_Battlegrounds_BattleGround/Update`.

**SetMaxPlayersPerTeam**: Sets the per-team limit. Called by `BattleGroundMgr/CreateBattleGround`.

**SetMinPlayersPerTeam**: Sets the per-team minimum. Called by `BattleGroundMgr/CreateBattleGround`.

**GetPlayers**: Returns the player map. Called by `ChatHandler` and `WorldSession`.

**GetPlayersSize**: Returns the player count. Called by `game_Battlegrounds_BattleGround/HasFreeSlots` and `Update`.

**GetPlayerScoresBegin**: Returns iterator to start of scores. Called by `BattleGroundMgr/BuildPvpLogDataPacket`.

**GetPlayerScoresEnd**: Returns iterator to end of scores. Called by `BattleGroundMgr/BuildPvpLogDataPacket`.

**GetPlayerScoresSize**: Returns score count. Called by `BattleGroundMgr/BuildPvpLogDataPacket`.

**GetFinalScorePacket**: Returns the final score packet. Called by `WorldSession.BattleGroundHandler/HandlePVPLogDataOpcode`.

**SetMapId**: Sets the map ID. Called by `BattleGroundMgr/CreateBattleGround`.

**GetMapId**: Returns the map ID. Called by `BattleGroundMgr`, `Player.Main`, and `WorldSession`.

**SetBgMap**: Sets the map pointer. Called by `MapManager/CreateBattleGroundMap`.

**GetBgMap**: Returns the map pointer. Asserts non-null. Called by `game_Battlegrounds_BattleGround` methods and subclasses.

**GetTeamStartLoc**: Returns team start coordinates. Called by `BattleGroundMgr/SendToBattleGround` and `game_Battlegrounds_BattleGround/ReturnPlayersToHomeGY`.

**FillInitialWorldStates**: Virtual method to fill world state packets. Called by `Player.Main/SendInitWorldStates`.

**GetBgRaid**: Returns the team's raid group. Called by `game_Battlegrounds_BattleGround/AddOrSetPlayerToCorrectBgGroup` and `WorldSession`.

**GetTeamIndexByTeamId**: Converts team enum to index. Called by subclasses and `game_Battlegrounds_BattleGround/SetBgRaid`.

**GetPlayersCountByTeam**: Returns team player count. Called by `game_Battlegrounds_BattleGround/EndBattleGround` and `Update`.

**UpdatePlayersCountByTeam**: Updates team player count. Called by `game_Battlegrounds_BattleGround/AddPlayer` and `RemovePlayerAtLeave`.

**HandleAreaTrigger**: Virtual hook for area triggers. Called by `WorldSession.MiscHandler/HandleAreaTriggerOpcode`.

**HandleKillUnit**: Virtual hook for creature kills. Called by `Unit.Main/Kill`.

**EventPlayerDroppedFlag**: Virtual hook for flag drops. Called by `Player.Main/SummonIfPossible` and `Unit.SpellAuras/HandleAuraModEffectImmunity`.

**EventPlayerClickedOnFlag**: Virtual hook for flag clicks. Called by `GameObject/Use` and `Spell` effects.

**EventPlayerCapturedFlag**: Virtual hook for flag captures. No external callers listed in MAP.

**IsActiveEvent**: Checks if an event is active. Called by `game_Battlegrounds_BattleGround` methods and AI modules.

**ActivateEventWithoutSpawn**: Activates an event without spawning. Called by `ThreatListCopier.battleground_alterac/JustRespawned#3`.

**HandlePlayerUnderMap**: Virtual hook for under-map detection. No external callers listed in MAP.

**GetOtherTeam**: Static helper to get opposing team. No external callers listed in MAP.

**GetOtherTeamIndex**: Static helper to get opposing team index. Called by `BattleGroundAV` and `BattleGroundWS`.

**GetPlayerSkinRefLootId**: Returns skin ref loot ID. Called by `Player.Main/SendLoot`.

**SetPlayerSkinRefLootId**: Sets skin ref loot ID. Called by `BattleGroundMgr/CreateBattleGround`.

**RemovePlayer**: Virtual method to clean up player data. Called by `game_Battlegrounds_BattleGround/RemovePlayerAtLeave`.

---

<!-- machine-true, projected from graph.json -->

## Map — BattleGround

*Source:* BattleGround.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| SetupBattleGround | method | — | game_Battlegrounds_BattleGround/Update | — |
| StartingEventCloseDoors | method | — | game_Battlegrounds_BattleGround/Update | — |
| StartingEventOpenDoors | method | — | game_Battlegrounds_BattleGround/Update | — |
| OnEventStateChanged | method | — | game_Battlegrounds_BattleGround/SpawnEvent | — |
| CheckSpellCast | method | — | BattleGroundAV/CheckSpellCast, Spell.Main/CheckCast | — |
| GetName | method | — | BattleGroundMgr/AddGroup, ChatHandler.MiscCommands/HandleBGStartCommand, ChatHandler.MiscCommands/HandleBGStatusCommand, ChatHandler.MiscCommands/HandleBGStopCommand, game_Battlegrounds_BattleGround/SendRewardMarkByMail, game_Battlegrounds_BattleGround/Update | — |
| GetTypeID | method | — | BattleBotAI.BattleBotWaypoints/StartNewPathFromAnywhere, BattleBotAI.BattleBotWaypoints/StartNewPathFromBeginning, BattleBotAI.BattleBotWaypoints/StartNewPathToObjective, BattleBotAI.Main/DoGraveyardJump, BattleBotAI.Main/GetMaxAggroDistanceForMap, BattleBotAI.Main/OnEnterBattleGround, BattleBotAI.Main/UpdateBattleGroundAI, BattleGroundAB/EndBattleGround, BattleGroundAB/Reset, BattleGroundAV/EndBattleGround, BattleGroundAV/Reset, BattleGroundMgr/BuildPvpLogDataPacket, BattleGroundMgr/CheckFreeSlots, BattleGroundMgr/CreateBattleGround, BattleGroundMgr/Execute#2, BattleGroundMgr/InviteGroupToBG, BattleGroundMgr/PlayerLoggedIn, BattleGroundWS/EndBattleGround, BattleGroundWS/Reset, ChatHandler.PlayerBotMgr/HandleBattleBotShowAllPathsCommand, game_Battlegrounds_BattleGround/EndBattleGround, game_Battlegrounds_BattleGround/GetBattlemasterEntry, game_Battlegrounds_BattleGround/GetHeraldEntry, game_Battlegrounds_BattleGround/GetWinnerText, game_Battlegrounds_BattleGround/HandleTriggerBuff, game_Battlegrounds_BattleGround/PlayerAddedToBGCheckIfBGIsRunning, game_Battlegrounds_BattleGround/RemovePlayerAtLeave, game_Battlegrounds_BattleGround/StartBattleGround, game_Battlegrounds_BattleGround/Update, game_Battlegrounds_BattleGround/~BattleGround, game_Group_Group/RewardGroupAtKill_helper, MapManager/CreateBattleGroundMap, Player.Main/LeaveBattleground, Player.Main/RewardQuest, Spell.Effects/EffectDummy, Spell.Effects/EffectOpenLock, Spell.Effects/EffectSummonObjectWild, ThreatListCopier.battleground_alterac/GossipHello_npc_AVBlood_collector, ThreatListCopier.battleground_alterac/GossipSelect_npc_AVBlood_collector, ThreatListCopier.battleground_alterac/QuestComplete_AV_npc_troops_chief, ThreatListCopier.battleground_alterac/QuestComplete_npc_AVBlood_collector, WorldSession.BattleGroundHandler/HandleBattleFieldPortOpcode, WorldSession.BattleGroundHandler/HandleBattleGroundPlayerPositionsOpcode | — |
| GetBracketId | method | — | BattleGroundAB/EndBattleGround, BattleGroundAB/Update, BattleGroundMgr/BuildBattleGroundStatusPacket, BattleGroundMgr/CheckFreeSlots, BattleGroundMgr/Execute, BattleGroundMgr/InviteGroupToBG, BattleGroundWS/EndBattleGround, BattleGroundWS/EventPlayerCapturedFlag, game_Battlegrounds_BattleGround/EndBattleGround, game_Battlegrounds_BattleGround/RemovePlayerAtLeave, game_Battlegrounds_BattleGround/Update, game_Battlegrounds_BattleGround/~BattleGround | — |
| GetInstanceID | method | — | BattleGroundMgr/CreateBattleGround, BattleGroundMgr/InviteGroupToBG, ChatHandler.MiscCommands/HandleBGStartCommand, ChatHandler.MiscCommands/HandleBGStopCommand, game_Battlegrounds_BattleGround/EndBattleGround, game_Battlegrounds_BattleGround/RemoveFromBGFreeSlotQueue, game_Battlegrounds_BattleGround/StartBattleGround, game_Battlegrounds_BattleGround/~BattleGround, Player.Main/LeaveBattleground, WorldSession.BattleGroundHandler/HandleBattleFieldPortOpcode | — |
| GetStatus | method | — | AiBotAI.Combat/UpdateOutOfCombatAI_Druid, AiBotAI.Combat/UpdateOutOfCombatAI_Priest, AiBotAI.Combat/UpdateOutOfCombatAI_Warlock, BattleBotAI.Main/DrinkAndEat, BattleBotAI.Main/OnEnterBattleGround, BattleBotAI.Main/UpdateAI, BattleBotAI.Main/UpdateOutOfCombatAI_Druid, BattleBotAI.Main/UpdateOutOfCombatAI_Priest, BattleBotAI.Main/UpdateOutOfCombatAI_Warlock, BattleBotAI.Main/UpdateWaypointMovement, BattleBotAI.Main/UseMount, BattleGroundAB/EventPlayerClickedOnFlag, BattleGroundAB/GetClosestGraveYard, BattleGroundAB/Update, BattleGroundAV/EventPlayerClickedOnFlag, BattleGroundAV/EventPlayerDefendsPoint, BattleGroundAV/FillInitialWorldStates, BattleGroundAV/GetClosestGraveYard, BattleGroundAV/HandleKillPlayer, BattleGroundAV/HandleKillUnit, BattleGroundAV/HandleQuestComplete, BattleGroundAV/Update, BattleGroundMgr/BuildPvpLogDataPacket, BattleGroundMgr/CheckFreeSlots, BattleGroundMgr/Execute, BattleGroundMgr/RemoveOfflinePlayer, BattleGroundWS/EventPlayerCapturedFlag, BattleGroundWS/EventPlayerClickedOnFlag, BattleGroundWS/EventPlayerDroppedFlag, BattleGroundWS/GetClosestGraveYard, BattleGroundWS/HandleAreaTrigger, BattleGroundWS/HandleKillPlayer, BattleGroundWS/RespawnFlagAfterDrop, BattleGroundWS/Update, ChatHandler.MiscCommands/HandleBGStatusCommand, game_Battlegrounds_BattleGround/GetFreeSlotsForTeam, game_Battlegrounds_BattleGround/OnObjectDBLoad#2, game_Battlegrounds_BattleGround/PlayerAddedToBGCheckIfBGIsRunning, game_Battlegrounds_BattleGround/RemovePlayerAtLeave, game_Battlegrounds_BattleGround/Update, MovementAnticheat/CheckForbiddenArea, Player.Main/LeaveBattleground, Spell.Effects/EffectSpiritHeal, Spell.Effects/EffectSummonObjectWild, Spell.Main/CheckCast, SpellMgr/GetSpellAllowedInLocationError, WorldSession.BattleGroundHandler/HandleBattleFieldPortOpcode, WorldSession.BattleGroundHandler/HandleLeaveBattlefieldOpcode, WorldSession.BattleGroundHandler/HandlePVPLogDataOpcode, WorldSession.MiscHandler/HandleReclaimCorpseOpcode | — |
| GetClientInstanceID | method | — | BattleGroundMgr/BuildBattleGroundStatusPacket, BattleGroundMgr/FillPlayersToBg, BattleGroundMgr/GetBattleGroundThroughClientInstance, game_Battlegrounds_BattleGround/~BattleGround | — |
| GetStartTime | method | — | ChatHandler.MiscCommands/HandleBGStatusCommand, game_Battlegrounds_BattleGround/EndBattleGround, game_Battlegrounds_BattleGround/GetHonorModifier, game_Battlegrounds_BattleGround/PlayerAddedToBGCheckIfBGIsRunning, game_Battlegrounds_BattleGround/~BattleGround, WorldSession.BattleGroundHandler/HandleBattleFieldPortOpcode, WorldSession.BattleGroundHandler/HandleBattlefieldStatusOpcode | — |
| GetEndTime | method | — | game_Battlegrounds_BattleGround/PlayerAddedToBGCheckIfBGIsRunning, WorldSession.BattleGroundHandler/HandleBattlefieldStatusOpcode | — |
| GetMaxPlayers | method | — | game_Battlegrounds_BattleGround/HasFreeSlots | — |
| GetMinPlayers | method | — | — | — |
| GetMinLevel | method | — | ChatHandler.MiscCommands/HandleBGStatusCommand, ChatHandler.PlayerBotMgr/Update, game_Battlegrounds_BattleGround/Update, Player.Main/GetBattleGroundBracketIdFromLevel#2, Player.Main/GetBGAccessByLevel, Player.Main/GetMinLevelForBattleGroundBracketId, WorldSession.MiscHandler/HandleAreaTriggerOpcode | — |
| GetMaxLevel | method | — | ChatHandler.MiscCommands/HandleBGStatusCommand, game_Battlegrounds_BattleGround/GetBonusHonorFromKill, game_Battlegrounds_BattleGround/Update, Player.Main/GetBGAccessByLevel, WorldSession.BattleGroundHandler/HandleBattleFieldPortOpcode, WorldSession.MiscHandler/HandleAreaTriggerOpcode | — |
| GetAllianceWinSpell | method | — | game_Battlegrounds_BattleGround/RewardMark | — |
| GetAllianceLoseSpell | method | — | game_Battlegrounds_BattleGround/RewardMark | — |
| GetHordeWinSpell | method | — | game_Battlegrounds_BattleGround/RewardMark | — |
| GetHordeLoseSpell | method | — | game_Battlegrounds_BattleGround/RewardMark | — |
| GetMaxPlayersPerTeam | method | — | BattleGroundMgr/CheckCreateNewBg, game_Battlegrounds_BattleGround/GetFreeSlotsForTeam, WorldSession.BattleGroundHandler/RequestBgJoinQueue | — |
| GetMinPlayersPerTeam | method | — | BattleGroundMgr/AddGroup, BattleGroundMgr/CheckCreateNewBg, ChatHandler.PlayerBotMgr/Update, game_Battlegrounds_BattleGround/Update | — |
| GetStartDelayTime | method | — | BattleGroundAV/Update, game_Battlegrounds_BattleGround/Update | — |
| GetWinner | method | — | BattleGroundMgr/BuildPvpLogDataPacket, game_Battlegrounds_BattleGround/~BattleGround | — |
| SetName | method | — | BattleGroundMgr/CreateBattleGround | — |
| SetTypeID | method | — | BattleGroundMgr/CreateBattleGround | — |
| SetBracketId | method | — | BattleGroundMgr/CreateNewBattleGround | — |
| SetStatus | method | — | BattleGroundMgr/CreateNewBattleGround, game_Battlegrounds_BattleGround/EndBattleGround, game_Battlegrounds_BattleGround/EndNow, game_Battlegrounds_BattleGround/Reset, game_Battlegrounds_BattleGround/Update | — |
| SetClientInstanceID | method | — | BattleGroundMgr/CreateNewBattleGround | — |
| SetStartTime | method | — | game_Battlegrounds_BattleGround/Reset, game_Battlegrounds_BattleGround/StartBattleGround | — |
| SetEndTime | method | — | game_Battlegrounds_BattleGround/EndBattleGround, game_Battlegrounds_BattleGround/EndNow, game_Battlegrounds_BattleGround/Reset | — |
| SetMaxPlayers | method | — | BattleGroundMgr/CreateBattleGround | — |
| SetMinPlayers | method | — | BattleGroundMgr/CreateBattleGround | — |
| SetAllianceWinSpell | method | — | BattleGroundMgr/CreateBattleGround | — |
| SetAllianceLoseSpell | method | — | BattleGroundMgr/CreateBattleGround | — |
| SetHordeWinSpell | method | — | BattleGroundMgr/CreateBattleGround | — |
| SetHordeLoseSpell | method | — | BattleGroundMgr/CreateBattleGround | — |
| SetLevelRange | method | — | BattleGroundMgr/CheckCreateNewBg, BattleGroundMgr/CreateBattleGround | — |
| SetWinner | method | — | game_Battlegrounds_BattleGround/EndBattleGround, game_Battlegrounds_BattleGround/Reset | — |
| ModifyStartDelayTime | method | — | game_Battlegrounds_BattleGround/Update | — |
| SetStartDelayTime | method | — | ChatHandler.MiscCommands/HandleBGStartCommand, game_Battlegrounds_BattleGround/Update | — |
| SetMaxPlayersPerTeam | method | — | BattleGroundMgr/CreateBattleGround | — |
| SetMinPlayersPerTeam | method | — | BattleGroundMgr/CreateBattleGround | — |
| GetPlayers | method | — | ChatHandler.MiscCommands/HandleBGStatusCommand, WorldSession.BattleGroundHandler/HandleBattleGroundPlayerPositionsOpcode | — |
| GetPlayersSize | method | — | game_Battlegrounds_BattleGround/HasFreeSlots, game_Battlegrounds_BattleGround/Update | — |
| GetPlayerScoresBegin | method | — | BattleGroundMgr/BuildPvpLogDataPacket | — |
| GetPlayerScoresEnd | method | — | BattleGroundMgr/BuildPvpLogDataPacket | — |
| GetPlayerScoresSize | method | — | BattleGroundMgr/BuildPvpLogDataPacket | — |
| GetFinalScorePacket | method | — | WorldSession.BattleGroundHandler/HandlePVPLogDataOpcode | — |
| SetMapId | method | — | BattleGroundMgr/CreateBattleGround | — |
| GetMapId | method | — | BattleGroundMgr/BuildBattleGroundStatusPacket, BattleGroundMgr/CreateNewBattleGround, BattleGroundMgr/SendToBattleGround, Player.Main/ExecuteTeleportFar, Player.Main/LeaveBattleground, WorldSession.BattleGroundHandler/HandleBattleFieldPortOpcode, WorldSession.BattleGroundHandler/RequestBgJoinQueue | — |
| SetBgMap | method | — | MapManager/CreateBattleGroundMap | — |
| GetBgMap | method | — | BattleGroundAV/CompleteQuestForAll, BattleGroundAV/HandleKillUnit, BattleGroundWS/ForceFlagAreaTrigger, BattleGroundWS/RespawnFlagAfterDrop, game_Battlegrounds_BattleGround/AddObject, game_Battlegrounds_BattleGround/DelObject, game_Battlegrounds_BattleGround/DoorClose, game_Battlegrounds_BattleGround/DoorOpen, game_Battlegrounds_BattleGround/RemovePlayerAtLeave, game_Battlegrounds_BattleGround/ReturnPlayersToHomeGY, game_Battlegrounds_BattleGround/SendYell2ToAll, game_Battlegrounds_BattleGround/SendYellToAll, game_Battlegrounds_BattleGround/SpawnBGCreature, game_Battlegrounds_BattleGround/SpawnBGObject, game_Battlegrounds_BattleGround/StartingEventDespawnDoors, game_Battlegrounds_BattleGround/Update | — |
| GetTeamStartLoc | method | — | BattleGroundMgr/SendToBattleGround, game_Battlegrounds_BattleGround/ReturnPlayersToHomeGY | — |
| FillInitialWorldStates | method | — | Player.Main/SendInitWorldStates | — |
| GetBgRaid | method | — | game_Battlegrounds_BattleGround/AddOrSetPlayerToCorrectBgGroup, game_Battlegrounds_BattleGround/RemovePlayerAtLeave, game_Group_Group/~Group, WorldSession.BattleGroundHandler/HandleBattleGroundPlayerPositionsOpcode | — |
| GetTeamIndexByTeamId | method | — | BattleGroundAB/EventPlayerClickedOnFlag, BattleGroundAB/GetClosestGraveYard, BattleGroundAB/_NodeOccupied, BattleGroundAV/EndBattleGround, BattleGroundAV/EventPlayerAssaultsPoint, BattleGroundAV/EventPlayerDefendsPoint, BattleGroundAV/HandleKillPlayer, BattleGroundMgr/RemovePlayer, BattleGroundWS/EventPlayerCapturedFlag, BattleGroundWS/EventPlayerDroppedFlag, game_Battlegrounds_BattleGround/SetBgRaid, game_Battlegrounds_BattleGround/SetTeamStartLoc | — |
| GetPlayersCountByTeam | method | — | game_Battlegrounds_BattleGround/EndBattleGround, game_Battlegrounds_BattleGround/Update | — |
| UpdatePlayersCountByTeam | method | — | game_Battlegrounds_BattleGround/AddPlayer, game_Battlegrounds_BattleGround/RemovePlayerAtLeave | — |
| HandleAreaTrigger | method | — | WorldSession.MiscHandler/HandleAreaTriggerOpcode | — |
| HandleKillUnit | method | — | Unit.Main/Kill | — |
| EventPlayerDroppedFlag | method | — | Player.Main/SummonIfPossible, Unit.SpellAuras/HandleAuraModEffectImmunity | — |
| EventPlayerClickedOnFlag | method | — | GameObject/Use, Spell.Effects/EffectDummy, Spell.Effects/EffectOpenLock | — |
| EventPlayerCapturedFlag | method | — | — | — |
| IsActiveEvent | method | — | BattleBotAI.BattleBotWaypoints/StartNewPathToObjective, BattleGroundAV/EndBattleGround, BattleGroundAV/HandleKillUnit, BattleGroundAV/Update, game_Battlegrounds_BattleGround/CanBeSpawned, game_Battlegrounds_BattleGround/OnObjectDBLoad, game_Battlegrounds_BattleGround/OnObjectDBLoad#2, game_Battlegrounds_BattleGround/OpenDoorEvent, game_Battlegrounds_BattleGround/SetSpawnEventMode, game_Battlegrounds_BattleGround/SpawnEvent, game_Battlegrounds_BattleGround/StartingEventDespawnDoors, GridNotifiers/operator()#2, GridNotifiers/operator()#3, ThreatListCopier.battleground_alterac/av_world_boss_baseai, ThreatListCopier.battleground_alterac/UpdateAI#9 | — |
| ActivateEventWithoutSpawn | method | — | ThreatListCopier.battleground_alterac/JustRespawned#3 | — |
| HandlePlayerUnderMap | method | — | — | — |
| GetOtherTeam | method | — | — | — |
| GetOtherTeamIndex | method | — | BattleGroundAV/EventPlayerDestroyedPoint, BattleGroundWS/EventPlayerCapturedFlag, BattleGroundWS/EventPlayerDroppedFlag | — |
| GetPlayerSkinRefLootId | method | — | Player.Main/SendLoot | — |
| SetPlayerSkinRefLootId | method | — | BattleGroundMgr/CreateBattleGround | — |
| RemovePlayer | method | — | game_Battlegrounds_BattleGround/RemovePlayerAtLeave | — |
