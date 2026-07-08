# BattleGroundAV

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# BattleGroundAV

**Purpose & Responsibilities**

`BattleGroundAV` is the specific implementation of the **Alterac Valley** battleground within the WoWVMaNGOS server. It extends the generic `BattleGround` class to handle the complex, large-scale PvP mechanics unique to Alterac Valley, including:

1.  **Node Control System:** Managing ownership, assault, and defense of Graveyards (resurrection points) and Towers (strategic points that generate resources).
2.  **Resource Economy:** Tracking "resources" (score) for each team, which determines the quality of NPC reinforcements (Basic, Seasoned, Veteran, Champion) and enables special assaults.
3.  **Special Assaults:** Handling logic for Air Assaults (Beacons), Cavalry Charges, Ground Assaults (Mines), and World Boss invocations, including their prerequisites (reputation, resource thresholds) and cooldowns.
4.  **Objective Rewards:** Distributing Honor and Reputation for killing key NPCs (Captains, Commanders, Generals), capturing/destroying nodes, and owning mines/graveyards at the end of the match.
5.  **Dynamic Spawning:** Controlling the lifecycle of NPC defenders, attackers, and event-specific creatures based on node states and team reinforcement levels.

This unit does not store persistent data in the database; all state is held in memory for the duration of the battleground instance.

## Member-by-Member Behavior

### Initialization and Lifecycle

*   **`BattleGroundAV`**: Constructor initializes start message IDs for the countdown sequence.
*   **`~BattleGroundAV`**: Destructor cleans up resources.
*   **`Reset`**: Resets all internal state for a new game. It sets initial reputation/honor values (adjusting for "BG Weekend" holidays), resets team scores to `BG_AV_SCORE_INITIAL_POINTS` (600), clears quest progress, resets mine ownership to Neutral, and initializes all nodes (Graveyards/Towers) to their starting owners (Alliance/Horde) or Neutral (Snowfall). It calls `initializeChallengeInvocationGoals` to set up assault prerequisites.
*   **`AddPlayer`**: Adds a player to the battleground and creates a `BattleGroundAVScore` object to track their individual performance metrics.
*   **`RemovePlayer`**: Currently empty; relies on base class cleanup.
*   **`Update`**: The main tick loop. It handles:
    *   Snivvle's speech timer.
    *   Periodic Captain buffs (randomized intervals).
    *   Despawning initial supply/tamed mount visuals during the pre-game countdown.
    *   Mine resource generation: Checks mine timers, awards resources to the owning team, and triggers reclamation if the reclaim timer expires.
    *   Node destruction: Checks assault timers on nodes; if expired, triggers `EventPlayerDestroyedPoint`.
    *   Calls the base `BattleGround::Update`.

### Node Management (Graveyards & Towers)

Nodes are tracked via `m_nodes[]`, storing owner, state (Controlled/Assaulted), and timers.

*   **`EventPlayerClickedOnFlag`**: Triggered when a player interacts with a banner. Determines if the action is an Assault (on Controlled node) or Defense (on Assaulted node) and delegates accordingly.
*   **`EventPlayerAssaultsPoint`**: Called when a player attacks an enemy-controlled node. Updates node state to `POINT_ASSAULTED`, starts the capture timer, updates world states, spawns attacker NPCs, and plays sounds.
*   **`EventPlayerDefendsPoint`**: Called when a player defends an assaulted node. Resets the node to `POINT_CONTROLLED` for the original owner, stops the capture timer, updates world states, spawns defender NPCs, and plays sounds.
*   **`EventPlayerDestroyedPoint`**: Called when a node's assault timer expires. The node changes ownership. It awards Honor/Reputation to the attacking team, penalizes the losing team's resources, completes relevant quests, and updates world states.
*   **`AssaultNode`**, **`DefendNode`**, **`DestroyNode`**, **`InitNode`**: Internal helpers that manipulate the `BG_AV_NodeInfo` struct fields (owner, state, timers) and assert validity.
*   **`PopulateNode`**: Spawns or despawns NPC defenders/attackers at a node based on its current state and the owning team's reinforcement level. Handles special logic for Graveyards vs. Towers.
*   **`GetClosestGraveYard`**: Calculates the nearest active graveyard for a player's resurrection. Prioritizes controlled graveyards over the starting cave.
*   **`IsTower`**, **`IsGrave`**: Helper predicates to distinguish node types.
*   **`GetNodeName`**: Returns the language ID for a node's name, used in broadcast messages.

### Resource and Score Management

*   **`UpdateScore`**: Adjusts a team's resource pool. Prevents scores from dropping below zero (though the win condition for low score is commented out/disabled in this codebase). Updates the world state UI for the score.
*   **`UpdatePlayerScore`**: Tracks individual player stats (graveyards assaulted/defended, towers assaulted/defended, secondary objectives).
*   **`FillInitialWorldStates`**: Sends the initial state of all nodes, mines, and scores to clients upon joining.
*   **`UpdateNodeWorldState`**: Updates the UI icon/state for a specific node after a change.
*   **`SendMineWorldStates`**: Updates the UI for mine ownership.

### Special Assaults (Challenges)

These methods manage the complex prerequisites for calling in reinforcements like Air Assaults or Cavalry.

*   **`initializeChallengeInvocationGoals`**: Sets the required resource counts and reputation levels for each assault type. Adjusts Cavalry reputation requirements based on the WoW patch version (Honored vs. Revered).
*   **`getReinforcementLevelGroundUnit`** / **`setReinforcementLevelGroundUnit`**: Maps resource totals to NPC quality tiers (Basic < 500, Seasoned < 1000, Veteran < 1500, Champion >= 1500).
*   **`getChallengeInvocationCounter`** / **`setChallengeInvocationCounter`**: Tracks progress toward assault goals (e.g., number of commander kills).
*   **`getChallengeInvocationGoals`**: Returns the target value for an assault.
*   **`getMinReputationNeeded`**: Returns the reputation threshold for an assault.
*   **`getTimerNeeded`**: Returns the cooldown timer for an assault.
*   **`setPlayerGoStatus`** / **`getPlayerGoStatus`**: Tracks whether a team has "accepted" or initiated an assault phase.
*   **`isAerialChallengeInvocationReady`**, **`isGroundChallengeInvocationReady`**, **`isCavalryChallengeInvocationReady`**, **`isWorldBossChallengeInvocationReady`**: Check if current counters meet the goals for specific assault types.
*   **`resetAerialChallengeInvocation`**, **`resetGroundChallengeInvocation`**, **`resetCavalryChallengeInvocation`**, **`resetWorldBossChallengeInvocation`**: Reset counters after an assault is triggered. `resetCavalryChallengeInvocation` also respawns tamed mounts.

### Quest and Objective Handling

*   **`HandleQuestComplete`**: Processes quest turns-ins from players. Updates team-wide quest progress counters (`m_teamQuestStatus`). Triggers visual spawns (supplies, mounts) and NPC dialogue when milestones are reached. Awards reputation.
*   **`UpgradeArmor`**: Manually upgrades the reinforcement level for a team (likely called via gossip or command). Triggers NPC dialogue and spells.
*   **`GetActualArmorRessources`**: Returns the current resource count for armor upgrades.
*   **`CompleteQuestForAll`**: Iterates through all players in the battleground and forces completion of a specific quest ID if they have it incomplete. Used for objective-based quests (e.g., "Destroy Tower").
*   **`PlayerCanDoMineQuest`**: Checks if a player's team owns the specific mine associated with a quest item/object.

### Kill and Event Handling

*   **`HandleKillPlayer`**: Called when a player dies. Awards resources to the killer's team (unless victim has a specific aura). Calls base handler.
*   **`HandleKillUnit`**: Called when a creature dies. Handles specific NPC deaths:
    *   **Generals (Bosses)**: Ends the battleground immediately for the killing team. Awards massive Honor/Rep.
    *   **Captains**: Awards Honor/Rep, reduces enemy resources, spawns "dead captain" aura.
    *   **Commanders**: Awards Honor/Rep, stops their respawn.
    *   **Mine Bosses**: Changes mine ownership.
    *   **Landmines**: Stops mine respawns or despawns all mines.
    *   **Explosives Experts**: Completes quests.
*   **`ChangeMineOwner`**: Updates mine ownership, triggers sounds, spawns new defenders, and updates world states. Starts the reclaim timer.
*   **`PopulateMineNode`**: Spawns defenders at a mine based on the owning team's reinforcement level.
*   **`ResetTamedEvent`**: Respawns tamed mounts (visuals).

### Utility and Commands

*   **`HandleCommand`**: Allows GMs to manipulate reinforcement levels, complete quests, or force quest completions via chat commands.
*   **`CheckSpellCast`**: Validates spell casts. Specifically prevents players from summoning Shredders if they already have one active.
*   **`HandleAreaTrigger`**: Handles exit portals. In patches > 1.6.1, triggers `LeaveBattleground` for the appropriate faction.
*   **`StartingEventCloseDoors`**, **`StartingEventOpenDoors`**: Manages door states during pre-game.
*   **`EndBattleGround`**: Calculates final rewards based on surviving towers, owned graveyards, and owned mines. Applies holiday bonuses if active. Calls base end handler.
*   **`GetAVTeamIndexByTeamId`**: Converts standard Team ID to AV-specific index (includes Neutral).
*   **`GetWorldStateType`**: Helper to calculate world state indices.
*   **`operator++`**: Increment operator for `BG_AV_Nodes` enum.

## Cross-Unit Boundaries

*   **`BattleGround` (Base Class)**: `BattleGroundAV` inherits heavily from `BattleGround`. It calls `BattleGround::HandleKillPlayer`, `BattleGround::Update`, `BattleGround::EndBattleGround`, `BattleGround::AddPlayer`, `BattleGround::Reset`, `BattleGround::HandleCommand`, and `BattleGround::CheckSpellCast`. It overrides most of these to add AV-specific logic.
*   **`BattleGroundMgr`**: Called via `BattleGroundMgr::CreateBattleGround` (ctor), `BattleGroundMgr::IsBgWeekend` (Reset, EndBattleGround), and `BattleGroundMgr::GetCreatureEventIndex` / `BattleGroundMgr::GetGameObjectEventIndex` (HandleKillUnit, EventPlayerClickedOnFlag).
*   **`ThreatListCopier.battleground_alterac`**: This external unit (likely a script handler for NPCs) calls numerous `BattleGroundAV` methods to query state:
    *   `checkTroopsStatus`, `GossipHello_npc_AVBlood_collector`, `JustDied`, `SelectCreatureEntry` call `getReinforcementLevelGroundUnit`.
    *   `GossipHello_npc_AVBlood_collector` calls `getChallengeInvocationCounter`, `getChallengeInvocationGoals`, `getMinReputationNeeded`, `isAerialChallengeInvocationReady`, `isCavalryChallengeInvocationReady`, `isGroundChallengeInvocationReady`, `GetActualArmorRessources`.
    *   `QuestComplete_npc_AVBlood_collector` calls `setChallengeInvocationCounter`, `isWorldBossChallengeInvocationReady`, `resetWorldBossChallengeInvocation`.
    *   `checkCavalryStatus`, `checkTroopsStatus`, `GossipSelect_npc_AVBlood_collector`, `QuestComplete_AV_npc_troops_chief`, `QuestComplete_npc_AVBlood_collector` call `setPlayerGoStatus`.
    *   `checkAerialStatus`, `checkCavalryStatus`, `checkTroopsStatus` call `getPlayerGoStatus`.
    *   `GossipSelect_npc_AVBlood_collector` calls `resetAerialChallengeInvocation`, `resetGroundChallengeInvocation`, `resetCavalryChallengeInvocation`.
    *   `GossipSelect_npc_AVBlood_collector` calls `UpgradeArmor`.
*   **`Player.Main`**: Called for `GetTeam`, `GetName`, `LeaveBattleground`, `GetQuestStatus`, `FullQuestComplete`, `RewardQuest` (calls `HandleQuestComplete`), `GetSelectedCreature`, `GetObjectGuid`, `IsGameMaster`, `GetPositionX/Y`.
*   **`Unit.Main`**: Called for `HasAura`, `GetCharm`, `GetEntry`.
*   **`Creature.Main`**: Called for `GetName`, `GetEntry`, `GetGUIDLow`.
*   **`GameObject`**: Called for `GetEntry`, `GetGUIDLow`.
*   **`WorldObject.Object`**: Called for `AddObjectToRemoveList`, `MonsterSay`, `MonsterYell`, `PMonsterSay`, `GetTypeId`, `GetObjectGuid`, `GetMap`, `GetPositionX/Y`.
*   **`Map.Main`**: Called for `GetGameObject`, `GetPlayers`, `GetPlayer`.
*   **`game_Battlegrounds_BattleGround`**: Various helper functions called for spawning events, casting spells, rewarding honor/rep, sending yells, and updating world states.
*   **`Errors.PrintStacktraceAndThrow`**: Called by many getter/setter methods to enforce bounds checking on arrays (e.g., `factionId < BG_TEAMS_COUNT`).
*   **`shared_Util.urand`**: Called for randomizing timers and buff intervals.
*   **`World.GetWowPatch`**: Called to adjust gameplay mechanics based on client version (e.g., reputation requirements).
*   **`Log.Main.Out`**: Used for debug logging throughout the unit.
*   **`ChatHandler.Chat`**: Used in `HandleCommand` for GM feedback.

## Data Model

This unit does not interact directly with database tables. All state (node ownership, scores, quest progress, timers) is maintained in memory within the `BattleGroundAV` instance structure. Quest completion is pushed to the `Player` object, which handles persistence.

## Notable Implementation Details

1.  **Disabled Low-Score Win Condition**: In `UpdateScore`, the code that ends the battleground when a team's score drops below 1 is commented out. This suggests a design choice to prevent "starvation" wins or to rely solely on General kills/timeout for victory.
2.  **Patch-Specific Logic**: `initializeChallengeInvocationGoals` checks `sWorld.GetWowPatch()` to adjust the reputation requirement for Cavalry Assaults (Revered in pre-1.6.0, Honored in 1.6.0+). `EndBattleGround` checks for patch 1.7.0 to enable graveyard honor rewards.
3.  **Hardcoded Quest IDs**: Many quest IDs are hardcoded in `HandleQuestComplete` and `CompleteQuestForAll`. While organized in enums, the logic is tightly coupled to specific quest chains.
4.  **Visual Spawns**: The code extensively uses `SpawnEvent` to manage visual representations of resources (supply crates, tamed mounts) based on modulo arithmetic of quest progress.
5.  **Neutral Node Handling**: Snowfall Graveyard is treated specially as a Neutral node. `EventPlayerDefendsPoint` redirects defense attempts on Snowfall to assault logic because it has no "previous owner" to defend for initially.
6.  **Shredder Limitation**: `CheckSpellCast` ensures only one Shredder per team can be active by tracking the GUID of the last summoner and checking if they still have a charm.
7.  **Mine Reclamation**: Mines have a `m_mineReclaimTimer`. If a team owns a mine for too long without contest, it reverts to Neutral. This timer is reset whenever ownership changes.
8.  **Captain Buffs**: Captains receive periodic buffs (spells 22751/23693) at randomized intervals during the battle, unless they are dead.
9.  **Array Bounds Checking**: Many getter/setter methods use `ASSERT` and call `Errors/PrintStacktraceAndThrow` to prevent out-of-bounds access on team/challenge arrays, indicating a defensive coding style for these critical state arrays.

## Member Reference

**BattleGroundAV**: Constructor that initializes start message IDs for the battleground countdown.
**~BattleGroundAV**: Destructor for cleanup.
**HandleKillPlayer**: Handles player death, awards resources to killer's team, and calls base handler.
**getReinforcementLevelGroundUnit**: Returns the NPC quality tier for a team based on resources.
**setReinforcementLevelGroundUnit**: Sets the NPC quality tier for a team based on resource thresholds.
**getChallengeInvocationCounter**: Returns current progress towards a specific assault goal.
**setChallengeInvocationCounter**: Increments progress towards a specific assault goal.
**getChallengeInvocationGoals**: Returns the target progress value for a specific assault.
**initializeChallengeInvocationGoals**: Initializes all assault goals, timers, and reputation requirements, adjusting for WoW patch version.
**getMinReputationNeeded**: Returns the reputation threshold required for a specific assault.
**setPlayerGoStatus**: Sets the "go" status for a team's assault initiation.
**getPlayerGoStatus**: Gets the "go" status for a team's assault initiation.
**getTimerNeeded**: Returns the cooldown timer for a specific assault.
**resetAerialChallengeInvocation**: Resets counters for aerial assaults.
**resetGroundChallengeInvocation**: Resets counters for ground assaults.
**resetCavalryChallengeInvocation**: Resets counters for cavalry assaults and respawns tamed mounts.
**resetWorldBossChallengeInvocation**: Resets counters for world boss assaults.
**isAerialChallengeInvocationReady**: Checks if aerial assault prerequisites are met.
**isCavalryChallengeInvocationReady**: Checks if cavalry assault prerequisites are met.
**isGroundChallengeInvocationReady**: Checks if ground assault prerequisites are met.
**isWorldBossChallengeInvocationReady**: Checks if world boss assault prerequisites are met.
**HandleKillUnit**: Handles creature deaths, awarding rewards, changing mine ownership, ending the game on General kills, and managing landmine/expert logic.
**GetActualArmorRessources**: Returns the current resource count for armor upgrades.
**UpgradeArmor**: Upgrades team reinforcement level, triggers NPC dialogue, spells, and spawns.
**operator++**: Increment operator for node enum.
**HandleQuestComplete**: Processes quest turns-ins, updates team progress, triggers visuals/dialogue, and awards reputation.
**GetAVTeamIndexByTeamId**: Converts standard Team ID to AV-specific index.
**IsTower**: Predicate to check if a node is a tower.
**IsGrave**: Predicate to check if a node is a graveyard.
**GetWorldStateType**: Helper to calculate world state indices.
**UpdateScore**: Adjusts team resources, updates UI, and checks for low-score conditions (win condition disabled).
**ResetTamedEvent**: Respawns tamed mount visuals.
**Update**: Main tick loop handling timers, buffs, mine resources, node destruction, and base updates.
**StartingEventCloseDoors**: Logs entry into wait-join state.
**StartingEventOpenDoors**: Opens doors and despawns ghost gates.
**AddPlayer**: Adds player and creates score object.
**EndBattleGround**: Calculates final rewards based on surviving objectives and applies holiday bonuses.
**RemovePlayer**: Empty override.
**HandleAreaTrigger**: Handles exit portals and unmounting.
**UpdatePlayerScore**: Tracks individual player statistics.
**EventPlayerDestroyedPoint**: Handles node capture completion, awarding rewards and updating state.
**ChangeMineOwner**: Updates mine ownership, spawns defenders, and updates UI.
**PlayerCanDoMineQuest**: Checks if player's team owns the relevant mine.
**PopulateMineNode**: Spawns mine defenders based on reinforcement level.
**PopulateNode**: Spawns/despawns node defenders/attackers based on state and reinforcement level.
**EventPlayerClickedOnFlag**: Delegates to assault or defense logic based on node state.
**EventPlayerDefendsPoint**: Handles successful defense of an assaulted node.
**EventPlayerAssaultsPoint**: Handles initiation of assault on a controlled node.
**FillInitialWorldStates**: Sends initial node, mine, and score states to clients.
**UpdateNodeWorldState**: Updates UI for a specific node.
**SendMineWorldStates**: Updates UI for mine ownership.
**GetClosestGraveYard**: Finds nearest active graveyard for resurrection.
**GetNodeName**: Returns language ID for node name.
**AssaultNode**: Internal helper to set node state to assaulted.
**DestroyNode**: Internal helper to finalize node capture.
**InitNode**: Internal helper to initialize node state.
**DefendNode**: Internal helper to reset node state to controlled.
**Reset**: Resets all battleground state for a new game.
**CompleteQuestForAll**: Forces quest completion for all players with incomplete status.
**HandleCommand**: GM commands for manipulation.
**CheckSpellCast**: Validates spell casts, limiting Shredder summons.

---

<!-- machine-true, projected from graph.json -->

## Map — BattleGroundAV

*Source:* BattleGroundAV.cpp, BattleGroundAV.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| BattleGroundAV | ctor | — | BattleGroundMgr/CreateBattleGround | — |
| ~BattleGroundAV | dtor | — | — | — |
| HandleKillPlayer | method | BattleGround/GetStatus, BattleGround/GetTeamIndexByTeamId, game_Battlegrounds_BattleGround/HandleKillPlayer, Player.Main/GetTeam, Unit.Main/HasAura#2 | — | — |
| getReinforcementLevelGroundUnit | method | Errors/PrintStacktraceAndThrow | ThreatListCopier.battleground_alterac/checkTroopsStatus, ThreatListCopier.battleground_alterac/GossipHello_npc_AVBlood_collector, ThreatListCopier.battleground_alterac/JustDied, ThreatListCopier.battleground_alterac/SelectCreatureEntry | — |
| setReinforcementLevelGroundUnit | method | Errors/PrintStacktraceAndThrow | — | — |
| getChallengeInvocationCounter | method | Errors/PrintStacktraceAndThrow | ThreatListCopier.battleground_alterac/GossipHello_npc_AVBlood_collector | — |
| setChallengeInvocationCounter | method | Errors/PrintStacktraceAndThrow | ThreatListCopier.battleground_alterac/QuestComplete_npc_AVBlood_collector | — |
| getChallengeInvocationGoals | method | Errors/PrintStacktraceAndThrow | ThreatListCopier.battleground_alterac/GossipHello_npc_AVBlood_collector | — |
| initializeChallengeInvocationGoals | method | shared_Util/urand, World/GetWowPatch | — | — |
| getMinReputationNeeded | method | Errors/PrintStacktraceAndThrow | ThreatListCopier.battleground_alterac/GossipHello_npc_AVBlood_collector | — |
| setPlayerGoStatus | method | Errors/PrintStacktraceAndThrow | ThreatListCopier.battleground_alterac/checkCavalryStatus, ThreatListCopier.battleground_alterac/checkTroopsStatus, ThreatListCopier.battleground_alterac/GossipSelect_npc_AVBlood_collector, ThreatListCopier.battleground_alterac/QuestComplete_AV_npc_troops_chief, ThreatListCopier.battleground_alterac/QuestComplete_npc_AVBlood_collector | — |
| getPlayerGoStatus | method | Errors/PrintStacktraceAndThrow | ThreatListCopier.battleground_alterac/checkAerialStatus, ThreatListCopier.battleground_alterac/checkCavalryStatus, ThreatListCopier.battleground_alterac/checkTroopsStatus | — |
| getTimerNeeded | method | Errors/PrintStacktraceAndThrow | — | — |
| resetAerialChallengeInvocation | method | Errors/PrintStacktraceAndThrow | ThreatListCopier.battleground_alterac/GossipSelect_npc_AVBlood_collector | — |
| resetGroundChallengeInvocation | method | Errors/PrintStacktraceAndThrow | ThreatListCopier.battleground_alterac/GossipSelect_npc_AVBlood_collector, ThreatListCopier.battleground_alterac/QuestComplete_AV_npc_troops_chief | — |
| resetCavalryChallengeInvocation | method | Errors/PrintStacktraceAndThrow, game_Battlegrounds_BattleGround/SpawnEvent | ThreatListCopier.battleground_alterac/GossipSelect_npc_AVBlood_collector | — |
| resetWorldBossChallengeInvocation | method | Errors/PrintStacktraceAndThrow | ThreatListCopier.battleground_alterac/QuestComplete_npc_AVBlood_collector | — |
| isAerialChallengeInvocationReady | method | Errors/PrintStacktraceAndThrow | ThreatListCopier.battleground_alterac/GossipHello_npc_AVBlood_collector | — |
| isCavalryChallengeInvocationReady | method | Errors/PrintStacktraceAndThrow | ThreatListCopier.battleground_alterac/GossipHello_npc_AVBlood_collector | — |
| isGroundChallengeInvocationReady | method | Errors/PrintStacktraceAndThrow | ThreatListCopier.battleground_alterac/GossipHello_npc_AVBlood_collector | — |
| isWorldBossChallengeInvocationReady | method | Errors/PrintStacktraceAndThrow | ThreatListCopier.battleground_alterac/QuestComplete_npc_AVBlood_collector | — |
| HandleKillUnit | method | BattleGround/GetBgMap, BattleGround/GetStatus, BattleGround/IsActiveEvent, BattleGroundMgr/GetCreatureEventIndex, game_Battlegrounds_BattleGround/CastSpellOnTeam, game_Battlegrounds_BattleGround/GetBonusHonorFromKill, game_Battlegrounds_BattleGround/GetHonorModifier, game_Battlegrounds_BattleGround/GetSingleCreatureGuid, game_Battlegrounds_BattleGround/RewardHonorToTeam, game_Battlegrounds_BattleGround/RewardReputationToTeam, game_Battlegrounds_BattleGround/SendYellToAll, game_Battlegrounds_BattleGround/SetSpawnEventMode, game_Battlegrounds_BattleGround/SpawnEvent, Log.Main/Out, Map.Main/GetGameObject, Object/GetEntry, Object/GetGUIDLow, Player.Main/GetTeam, WorldObject.Object/AddObjectToRemoveList | — | — |
| GetActualArmorRessources | method | Errors/PrintStacktraceAndThrow | ThreatListCopier.battleground_alterac/GossipHello_npc_AVBlood_collector | — |
| UpgradeArmor | method | game_Battlegrounds_BattleGround/CastSpellOnTeam, Object/GetTypeId, Player.Main/GetName, Player.Main/GetTeam, WorldObject.Object/MonsterSay, WorldObject.Object/MonsterYell | ThreatListCopier.battleground_alterac/GossipSelect_npc_AVBlood_collector | — |
| operator++ | function | — | — | — |
| HandleQuestComplete | method | BattleGround/GetStatus, Errors/PrintStacktraceAndThrow, game_Battlegrounds_BattleGround/RewardReputationToTeam, game_Battlegrounds_BattleGround/SpawnEvent, Log.Main/Out, Object/GetTypeId, Player.Main/GetName, Player.Main/GetTeam, WorldObject.Object/MonsterSay, WorldObject.Object/MonsterYell, WorldObject.Object/PMonsterSay | Player.Main/RewardQuest | — |
| GetAVTeamIndexByTeamId | method | — | — | — |
| IsTower | method | — | — | — |
| IsGrave | method | — | — | — |
| GetWorldStateType | method | — | — | — |
| UpdateScore | method | Errors/PrintStacktraceAndThrow, game_Battlegrounds_BattleGround/UpdateWorldState | — | — |
| ResetTamedEvent | method | game_Battlegrounds_BattleGround/SpawnEvent, Log.Main/Out | — | — |
| Update | method | BattleGround/GetStartDelayTime, BattleGround/GetStatus, BattleGround/IsActiveEvent, game_Battlegrounds_BattleGround/CastSpellOnTeam, game_Battlegrounds_BattleGround/GetSingleCreatureGuid, game_Battlegrounds_BattleGround/SendYellToAll, game_Battlegrounds_BattleGround/SpawnEvent, game_Battlegrounds_BattleGround/Update, shared_Util/urand | — | — |
| StartingEventCloseDoors | method | Log.Main/Out | — | — |
| StartingEventOpenDoors | method | game_Battlegrounds_BattleGround/OpenDoorEvent, game_Battlegrounds_BattleGround/SpawnEvent | — | — |
| AddPlayer | method | BattleGroundAVScore/BattleGroundAVScore, game_Battlegrounds_BattleGround/AddPlayer, Object/GetObjectGuid | — | — |
| EndBattleGround | method | BattleGround/GetTeamIndexByTeamId, BattleGround/GetTypeID, BattleGround/IsActiveEvent, BattleGroundMgr/IsBgWeekend, game_Battlegrounds_BattleGround/EndBattleGround, game_Battlegrounds_BattleGround/GetBonusHonorFromKill, game_Battlegrounds_BattleGround/GetHonorModifier, game_Battlegrounds_BattleGround/RewardHonorToTeam, game_Battlegrounds_BattleGround/RewardReputationToTeam, Log.Main/Out, World/GetWowPatch | — | — |
| RemovePlayer | method | — | — | — |
| HandleAreaTrigger | method | Log.Main/Out, Player.Main/GetTeam, Player.Main/LeaveBattleground | — | — |
| UpdatePlayerScore | method | game_Battlegrounds_BattleGround/UpdatePlayerScore, Object/GetObjectGuid | — | — |
| EventPlayerDestroyedPoint | method | BattleGround/GetOtherTeamIndex, Errors/PrintStacktraceAndThrow, game_Battlegrounds_BattleGround/GetBonusHonorFromKill, game_Battlegrounds_BattleGround/GetSingleCreatureGuid, game_Battlegrounds_BattleGround/RewardHonorToTeam, game_Battlegrounds_BattleGround/RewardReputationToTeam, game_Battlegrounds_BattleGround/SendYell2ToAll, Log.Main/Out | — | — |
| ChangeMineOwner | method | Errors/PrintStacktraceAndThrow, game_Battlegrounds_BattleGround/GetSingleCreatureGuid, game_Battlegrounds_BattleGround/PlaySoundToAll, game_Battlegrounds_BattleGround/SendYell2ToAll, game_Battlegrounds_BattleGround/SpawnEvent | — | — |
| PlayerCanDoMineQuest | method | — | — | — |
| PopulateMineNode | method | Errors/PrintStacktraceAndThrow, game_Battlegrounds_BattleGround/SetSpawnEventMode, game_Battlegrounds_BattleGround/SpawnEvent | — | — |
| PopulateNode | method | Errors/PrintStacktraceAndThrow, game_Battlegrounds_BattleGround/SetSpawnEventMode, game_Battlegrounds_BattleGround/SpawnEvent | — | — |
| EventPlayerClickedOnFlag | method | BattleGround/GetStatus, BattleGroundMgr/GetGameObjectEventIndex, Log.Main/Out, Object/GetEntry, Object/GetGUIDLow | — | — |
| EventPlayerDefendsPoint | method | BattleGround/GetStatus, BattleGround/GetTeamIndexByTeamId, Errors/PrintStacktraceAndThrow, game_Battlegrounds_BattleGround/GetSingleCreatureGuid, game_Battlegrounds_BattleGround/PlaySoundToAll, game_Battlegrounds_BattleGround/SendYell2ToAll, Log.Main/Out, Player.Main/GetTeam | — | — |
| EventPlayerAssaultsPoint | method | BattleGround/GetTeamIndexByTeamId, game_Battlegrounds_BattleGround/GetSingleCreatureGuid, game_Battlegrounds_BattleGround/PlaySoundToAll, game_Battlegrounds_BattleGround/SendYell2ToAll, Log.Main/Out, Player.Main/GetTeam | — | — |
| FillInitialWorldStates | method | BattleGround/GetStatus, game_Battlegrounds_BattleGround/FillInitialWorldState#2, game_Battlegrounds_BattleGround/FillInitialWorldState#3 | — | — |
| UpdateNodeWorldState | method | game_Battlegrounds_BattleGround/UpdateWorldState | — | — |
| SendMineWorldStates | method | Errors/PrintStacktraceAndThrow, game_Battlegrounds_BattleGround/UpdateWorldState | — | — |
| GetClosestGraveYard | method | BattleGround/GetStatus, Player.Main/GetTeam, Player.Main/IsGameMaster, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY | — | — |
| GetNodeName | method | — | — | — |
| AssaultNode | method | Errors/PrintStacktraceAndThrow | — | — |
| DestroyNode | method | Errors/PrintStacktraceAndThrow | — | — |
| InitNode | method | Errors/PrintStacktraceAndThrow | — | — |
| DefendNode | method | Errors/PrintStacktraceAndThrow | — | — |
| Reset | method | BattleGround/GetTypeID, BattleGroundMgr/IsBgWeekend, game_Battlegrounds_BattleGround/Reset | — | — |
| CompleteQuestForAll | method | BattleGround/GetBgMap, Map.Main/GetPlayers, Player.Main/FullQuestComplete, Player.Main/GetQuestStatus | — | — |
| HandleCommand | method | ChatHandler.Chat/PSendSysMessage, ChatHandler.Chat/SendSysMessage#2, Creature.Main/GetName, game_Battlegrounds_BattleGround/HandleCommand, Player.Main/GetSelectedCreature | — | — |
| CheckSpellCast | method | BattleGround/CheckSpellCast, Map.Main/GetPlayer, Object/GetEntry, Object/GetObjectGuid, Unit.Main/GetCharm, WorldObject.Object/GetMap | — | — |
