# BattleGroundWS

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# BattleGroundWS: Warsong Gulch Battleground Logic

**Purpose & Responsibilities**
`BattleGroundWS` implements the specific game rules for the **Warsong Gulch** battleground mode in World of Warcraft. Unlike generic battlegrounds, Warsong Gulch is an objective-based mode centered on capturing and returning enemy flags. This unit manages the entire lifecycle of the two flags (Alliance and Horde), including their states (on base, carried by player, dropped on ground, waiting to respawn), the scoring system (first team to 3 captures wins), and the associated visual/audio feedback (world states, sounds, chat messages). It handles player interactions with flags (picking up, dropping, capturing, returning) and enforces win conditions.

**Data Model**
This unit does not interact directly with any database tables. All state is held in memory within the `BattleGroundWS` instance variables (`m_flagState`, `m_flagKeepers`, `m_teamScores`, etc.). The `SCHEMA` section provided in the prompt is empty or irrelevant to this specific unit's direct operations.

**Cross-Unit Boundaries**
*   **`BattleGround` (Parent Class):** `BattleGroundWS` inherits from `BattleGround`. It calls `BattleGround::Update`, `BattleGround::AddPlayer`, `BattleGround::Reset`, `BattleGround::EndBattleGround`, `BattleGround::HandleKillPlayer`, and `BattleGround::UpdatePlayerScore` to delegate generic battleground management (timer updates, player joins/leaves, final cleanup) to the base class. It overrides these methods to inject Warsong-specific logic (e.g., dropping flags on death, calculating specific scores).
*   **`game_Battlegrounds_BattleGround` (Helper Functions):** Uses helper functions like `OpenDoorEvent`, `SpawnEvent`, `PlaySoundToAll`, `SendMessageToAll`, `UpdateWorldState`, `RewardHonorToTeam`, `RewardReputationToTeam`, and `FillInitialWorldState` to manipulate the game world, notify players, and update UI elements.
*   **`BattleGroundMgr`:** Calls `GetGameObjectEventIndex` to identify which flag was clicked and `IsBgWeekend` to determine if holiday bonuses apply.
*   **`Object` / `ObjectGuid`:** Uses `GetObjectGuid` to track players and flags, and `operator==` to compare GUIDs.
*   **`Player` / `Unit`:** Calls `GetTeam`, `GetSession`, `LeaveBattleground`, `RemoveAurasDueToSpell`, and `CastSpell` to manage player state, remove flag-carrying auras, and apply visual effects.
*   **`Map` / `WorldObject`:** Uses `GetBgMap` to access the map instance, `GetPlayer` to find specific players, `GetGameObject` to retrieve dropped flag objects, and `IsWithinDistInMap`/`GetPositionX/Y/Z` for proximity checks.
*   **`ObjectMgr`:** Calls `GetAreaTrigger` and `IsPointInAreaTriggerZone` to check if a player is inside a flag spawn zone.
*   **`Log.Main`:** Logs debug and error messages via `Out`.
*   **`World`:** Calls `getConfig` and `GetWowPatch` to adjust rewards based on server configuration and client version.
*   **`Spell.Effects`:** Called indirectly via `SetDroppedFlagGuid` when a spell effect summons a dropped flag object.
*   **`BattleBotAI`:** `IsAllianceFlagPickedup` and `IsHordeFlagPickedup` are called by `BattleBotAI.BattleBotWaypoints/StartNewPathToObjective` to allow bots to react to flag states.
*   **`WorldSession.BattleGroundHandler`:** `GetAllianceFlagPickerGuid` and `GetHordeFlagPickerGuid` are called by `HandleBattleGroundPlayerPositionsOpcode` to send flag carrier positions to clients.

**Notable Implementation Details**
*   **Flag State Machine:** The core logic revolves around `m_flagState[team]`, which tracks four states: `ON_BASE`, `WAIT_RESPAWN`, `ON_PLAYER`, and `ON_GROUND`. Transitions between these states drive the game flow.
*   **Respawn Timers:** `Update()` decrements `m_flagsTimer` (for `WAIT_RESPAWN`) and `m_flagsDropTimer` (for `ON_GROUND`). When timers expire, `RespawnFlag` or `RespawnFlagAfterDrop` is called.
*   **Dropped Flag Handling:** When a flag is dropped, a temporary GameObject is spawned (via spell effect). `SetDroppedFlagGuid` stores this object's GUID. `RespawnFlagAfterDrop` deletes this GameObject after the drop timer expires.
*   **Area Trigger Force:** `ForceFlagAreaTrigger` is a workaround to ensure that if a flag carrier is standing exactly on the enemy flag spawn point when a flag is returned/dropped, the capture logic is triggered immediately, as normal area trigger checks might miss this edge case.
*   **Version Compatibility:** Extensive use of `#if SUPPORTED_CLIENT_BUILD > CLIENT_BUILD_1_6_1` to handle differences in chat message sending (`SendMessageToAll` vs `DoOrSimulateScriptTextForMap`) and exit triggers.
*   **Score Tracking:** `BattleGroundWGScore` extends `BattleGroundScore` to track `flagCaptures` and `flagReturns`. `UpdatePlayerScore` updates these specific stats.
*   **Win Condition:** The first team to reach `BG_WS_MAX_TEAM_SCORE` (3) captures wins. `EventPlayerCapturedFlag` checks this condition and calls `EndBattleGround` if met.
*   **Holiday Bonuses:** `Reset()` and `EndBattleGround` check `BattleGroundMgr::IsBgWeekend` to apply increased reputation and honor rewards.

## Member Reference

**BattleGroundWS**: Constructor initializes starting message IDs for the countdown sequence.

**~BattleGroundWS**: Destructor, currently empty.

**Update**: Checks if the battleground is in progress. Decrements respawn and drop timers for both teams. If timers expire, calls `RespawnFlag` or `RespawnFlagAfterDrop`. Finally, calls `BattleGround::Update` to handle generic updates.

**StartingEventCloseDoors**: Empty implementation, likely unused or handled elsewhere.

**StartingEventOpenDoors**: Opens doors via `OpenDoorEvent` and spawns spirit guides, both flags, and ghost gates using `SpawnEvent`.

**AddPlayer**: Calls `BattleGround::AddPlayer` then creates a `BattleGroundWGScore` object for the player and stores it in `m_playerScores`.

**RespawnFlag**: Sets the flag state to `ON_BASE` and spawns the flag GameObject. If `captured` is true, plays a sound and sends a chat message indicating the flag was placed.

**GetAllianceFlagPickerGuid**: Returns the GUID of the player carrying the Alliance flag. Called by `WorldSession.BattleGroundHandler/HandleBattleGroundPlayerPositionsOpcode`.

**GetHordeFlagPickerGuid**: Returns the GUID of the player carrying the Horde flag. Called by `WorldSession.BattleGroundHandler/HandleBattleGroundPlayerPositionsOpcode`.

**SetAllianceFlagPicker**: Sets the GUID of the player carrying the Alliance flag.

**SetHordeFlagPicker**: Sets the GUID of the player carrying the Horde flag.

**ClearAllianceFlagPicker**: Clears the GUID of the player carrying the Alliance flag.

**ClearHordeFlagPicker**: Clears the GUID of the player carrying the Horde flag.

**IsAllianceFlagPickedup**: Returns true if the Alliance flag is currently carried by a player. Called by `BattleBotAI.BattleBotWaypoints/StartNewPathToObjective`.

**IsHordeFlagPickedup**: Returns true if the Horde flag is currently carried by a player. Called by `BattleBotAI.BattleBotWaypoints/StartNewPathToObjective`.

**GetFlagState**: Returns the current state of the specified team's flag.

**RespawnFlagAfterDrop**: Called when a dropped flag's timer expires. Resets the flag state, updates world states, sends chat messages, plays sounds, deletes the dropped flag GameObject, clears the dropped flag GUID, and forces an area trigger check.

**SetDroppedFlagGuid**: Stores the GUID of the dropped flag GameObject. Called by `Spell.Effects/EffectSummonObjectWild`.

**ClearDroppedFlagGuid**: Clears the stored GUID of the dropped flag GameObject.

**GetDroppedFlagGuid**: Returns the stored GUID of the dropped flag GameObject.

**GetTeamScore**: Returns the current score for the specified team.

**AddPoint**: Adds points to the specified team's score.

**SetTeamPoint**: Sets the specified team's score to a specific value.

**RemovePoint**: Removes points from the specified team's score.

**ForceFlagAreaTrigger**: Checks if the opposing flag carrier is within the flag spawn area trigger. If so, manually triggers `HandleAreaTrigger` for that player to ensure capture logic runs.

**EventPlayerCapturedFlag**: Handles the logic when a player enters the enemy flag spawn area with the enemy flag. Clears the flag picker, removes the flag aura, adds a point to the capturing team, plays sounds, rewards reputation and honor, updates world states, despawns flags, sends chat messages, updates scores, and checks for win condition. If no winner, starts the respawn timer for the captured flag.

**EventPlayerDroppedFlag**: Handles the logic when a flag carrier dies or drops the flag. If the battleground is not in progress, it simply cleans up the aura and picker. Otherwise, it clears the picker, removes the aura, sets the flag state to `ON_GROUND`, casts a spell to spawn the dropped flag object, updates world states, sends chat messages, and starts the drop timer.

**EventPlayerClickedOnFlag**: Handles player interaction with flag GameObjects. Determines if the player is picking up a flag from base, returning a dropped flag, or picking up a dropped flag. Updates flag states, sets/clears pickers, casts spells, plays sounds, updates world states, and sends chat messages accordingly.

**RemovePlayer**: Called when a player leaves the battleground. If the player was carrying a flag, it triggers `EventPlayerDroppedFlag` or directly respawns the flag if the player object is invalid (offline).

**UpdateFlagState**: Updates the world state for the specified team's flag status.

**UpdateTeamScore**: Updates the world state for the specified team's score.

**HandleAreaTrigger**: Handles area trigger events. Ignores elixir triggers. Checks for flag capture triggers (`AREATRIGGER_ALLIANCE_FLAG_SPAWN`, `AREATRIGGER_HORDE_FLAG_SPAWN`) and calls `EventPlayerCapturedFlag` if appropriate. Handles exit triggers for Alliance and Horde, calling `Player::LeaveBattleground`. Logs unhandled triggers.

**SetupBattleGround**: Always returns true, indicating setup is successful.

**Reset**: Calls `BattleGround::Reset`. Resets active events, clears flag GUIDs and keepers, sets flag states to `ON_BASE`, resets team scores to 0. Calculates reputation and honor rewards based on whether it is a BG weekend and the WoW patch version.

**EndBattleGround**: Calculates and distributes honor rewards based on the winner and whether it is a BG weekend. Calls `BattleGround::EndBattleGround`.

**HandleKillPlayer**: Calls `EventPlayerDroppedFlag` on the victim to handle flag drops on death. Then calls `BattleGround::HandleKillPlayer`.

**UpdatePlayerScore**: Updates the player's specific Warsong scores (`flagCaptures`, `flagReturns`) or delegates to `BattleGround::UpdatePlayerScore` for other score types.

**GetClosestGraveYard**: Returns the appropriate graveyard location based on the player's team and whether the battleground is in progress (main graveyard during battle, flag room graveyard before start).

**FillInitialWorldStates**: Populates the initial world state packet with current scores, flag states, and max score.

---

<!-- machine-true, projected from graph.json -->

## Map — BattleGroundWS

*Source:* BattleGroundWS.cpp, BattleGroundWS.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| BattleGroundWS | ctor | — | BattleGroundMgr/CreateBattleGround | — |
| ~BattleGroundWS | dtor | — | — | — |
| Update | method | BattleGround/GetStatus, game_Battlegrounds_BattleGround/Update | — | — |
| StartingEventCloseDoors | method | — | — | — |
| StartingEventOpenDoors | method | game_Battlegrounds_BattleGround/OpenDoorEvent, game_Battlegrounds_BattleGround/SpawnEvent | — | — |
| AddPlayer | method | BattleGroundWGScore/BattleGroundWGScore, game_Battlegrounds_BattleGround/AddPlayer, Object/GetObjectGuid | — | — |
| RespawnFlag | method | game_Battlegrounds_BattleGround/PlaySoundToAll, game_Battlegrounds_BattleGround/SendMessageToAll, game_Battlegrounds_BattleGround/SpawnEvent, Log.Main/Out | — | — |
| GetAllianceFlagPickerGuid | method | — | WorldSession.BattleGroundHandler/HandleBattleGroundPlayerPositionsOpcode | — |
| GetHordeFlagPickerGuid | method | — | WorldSession.BattleGroundHandler/HandleBattleGroundPlayerPositionsOpcode | — |
| SetAllianceFlagPicker | method | — | — | — |
| SetHordeFlagPicker | method | — | — | — |
| ClearAllianceFlagPicker | method | — | — | — |
| ClearHordeFlagPicker | method | — | — | — |
| IsAllianceFlagPickedup | method | — | BattleBotAI.BattleBotWaypoints/StartNewPathToObjective | — |
| IsHordeFlagPickedup | method | — | BattleBotAI.BattleBotWaypoints/StartNewPathToObjective | — |
| GetFlagState | method | — | — | — |
| RespawnFlagAfterDrop | method | BattleGround/GetBgMap, BattleGround/GetStatus, GameObject/Delete, game_Battlegrounds_BattleGround/PlaySoundToAll, game_Battlegrounds_BattleGround/SendMessageToAll, game_Battlegrounds_BattleGround/UpdateWorldState, Log.Main/Out, Map.Main/GetGameObject, ObjectGuid/GetString | — | — |
| SetDroppedFlagGuid | method | — | Spell.Effects/EffectSummonObjectWild | — |
| ClearDroppedFlagGuid | method | — | — | — |
| GetDroppedFlagGuid | method | — | — | — |
| GetTeamScore | method | — | — | — |
| AddPoint | method | — | — | — |
| SetTeamPoint | method | — | — | — |
| RemovePoint | method | — | — | — |
| ForceFlagAreaTrigger | method | BattleGround/GetBgMap, Map.Main/GetPlayer, ObjectMgr/GetAreaTrigger, ObjectMgr/IsPointInAreaTriggerZone, WorldObject.Object/GetMapId, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ | — | — |
| EventPlayerCapturedFlag | method | BattleGround/GetBracketId, BattleGround/GetOtherTeamIndex, BattleGround/GetStatus, BattleGround/GetTeamIndexByTeamId, game_Battlegrounds_BattleGround/PlaySoundToAll, game_Battlegrounds_BattleGround/RewardHonorToTeam, game_Battlegrounds_BattleGround/RewardReputationToTeam, game_Battlegrounds_BattleGround/SendMessageToAll, game_Battlegrounds_BattleGround/SpawnEvent, game_Battlegrounds_BattleGround/UpdateWorldState, Player.Main/GetTeam, Unit.Main/RemoveAurasDueToSpell | — | — |
| EventPlayerDroppedFlag | method | BattleGround/GetOtherTeamIndex, BattleGround/GetStatus, BattleGround/GetTeamIndexByTeamId, game_Battlegrounds_BattleGround/SendMessageToAll, game_Battlegrounds_BattleGround/UpdateWorldState, Object/GetObjectGuid, ObjectGuid/operator==, Player.Main/GetTeam, SpellCaster/CastSpell#2, Unit.Main/RemoveAurasDueToSpell | — | — |
| EventPlayerClickedOnFlag | method | BattleGround/GetStatus, BattleGroundMgr/GetGameObjectEventIndex, game_Battlegrounds_BattleGround/PlaySoundToAll, game_Battlegrounds_BattleGround/SendMessageToAll, game_Battlegrounds_BattleGround/SpawnEvent, game_Battlegrounds_BattleGround/UpdateWorldState, Object/GetEntry, Object/GetGUIDLow, Object/GetObjectGuid, Player.Main/GetTeam, SpellCaster/CastSpell#2, WorldObject.Object/IsWithinDistInMap | — | — |
| RemovePlayer | method | Log.Main/Out, ObjectGuid/operator== | — | — |
| UpdateFlagState | method | game_Battlegrounds_BattleGround/UpdateWorldState | — | — |
| UpdateTeamScore | method | game_Battlegrounds_BattleGround/UpdateWorldState | — | — |
| HandleAreaTrigger | method | BattleGround/GetStatus, Log.Main/Out, Object/GetObjectGuid, ObjectGuid/operator==, Player.Main/GetSession, Player.Main/GetTeam, Player.Main/LeaveBattleground, WorldSession.Main/SendAreaTriggerMessage | — | — |
| SetupBattleGround | method | — | — | — |
| Reset | method | BattleGround/GetTypeID, BattleGroundMgr/IsBgWeekend, game_Battlegrounds_BattleGround/Reset, ObjectGuid/Clear, World/getConfig, World/GetWowPatch | — | — |
| EndBattleGround | method | BattleGround/GetBracketId, BattleGround/GetTypeID, BattleGroundMgr/IsBgWeekend, game_Battlegrounds_BattleGround/EndBattleGround, game_Battlegrounds_BattleGround/RewardHonorToTeam | — | — |
| HandleKillPlayer | method | BattleGround/GetStatus, game_Battlegrounds_BattleGround/HandleKillPlayer | — | — |
| UpdatePlayerScore | method | game_Battlegrounds_BattleGround/UpdatePlayerScore, Object/GetObjectGuid | — | — |
| GetClosestGraveYard | method | BattleGround/GetStatus, Player.Main/GetTeam | — | — |
| FillInitialWorldStates | method | game_Battlegrounds_BattleGround/FillInitialWorldState#2, game_Battlegrounds_BattleGround/FillInitialWorldState#4 | — | — |
