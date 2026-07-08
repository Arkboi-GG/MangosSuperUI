# BattleGroundAB

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# BattleGroundAB

**BattleGroundAB** implements the server-side logic for **Arathi Basin**, a capture-the-point battleground in World of Warcraft. It manages five strategic nodes (Stables, Blacksmith, Farm, Lumber Mill, Gold Mine), tracking their ownership status (Neutral, Contested, Occupied) and calculating team scores based on controlled resources. The class handles node capture timers, resource accumulation ticks, honor/reputation rewards, player scoring, and visual updates via World States.

## Purpose & Responsibilities

The primary responsibility of `BattleGroundAB` is to enforce the rules of Arathi Basin:
1.  **Node Management:** Track the state of 5 nodes. Nodes transition from Neutral -> Contested (by clicking a banner) -> Occupied (after a 60-second timer if uncontested). Opposing teams can contest occupied nodes, resetting the timer.
2.  **Resource Scoring:** Teams earn points over time for each node they occupy. The rate of point accumulation increases with the number of nodes held.
3.  **Reward Distribution:** Distribute Honor and Reputation to players periodically based on accumulated points, and grant bonus rewards for winning or capturing multiple bases.
4.  **Visual Feedback:** Update World States to reflect node icons, team scores, and victory progress bars on the client UI.
5.  **Player Interaction:** Handle player clicks on banners (`EventPlayerClickedOnFlag`) and area triggers for leaving the battleground (`HandleAreaTrigger`).

## Member-by-Member Behavior

### Initialization and Lifecycle

*   **`BattleGroundAB` (ctor):** Initializes internal arrays for node states, timers, and scores. Sets up message IDs for the starting countdown sequence (1 minute, 30 seconds, started).
*   **`~BattleGroundAB` (dtor):** Default destructor. No special cleanup is required as parent classes handle memory management for players and objects.
*   **`Reset`:** Resets all game state for a new match. Clears team scores, timers, and node states. All nodes start as Neutral. It determines Honor and Reputation tick intervals based on whether it is a "BG Weekend" (double rewards) and the WoW patch version (pre/post 1.10 reputation rates).
*   **`SetupBattleGround`:** Spawns the initial buff objects (Speed, Regen, Berserk) at each of the 5 node locations using predefined coordinates. Logs an error if any object fails to spawn.

### Core Game Loop

*   **`Update`:** The main tick handler. If the battleground is in progress:
    1.  **Node Timers:** Decrements capture timers for contested nodes. If a timer expires, the node becomes **Occupied** by the contesting team. This triggers visual updates, sounds, chat messages, and checks for quest rewards (via `_NodeOccupied`).
    2.  **Resource Accumulation:** Counts how many nodes each team occupies. Based on this count, it calculates points added to the team score. Points are added at varying intervals (defined by `BG_AB_TickIntervals`).
    3.  **Rewards:** Accumulates hidden "tics" for Honor and Reputation. When thresholds (`m_honorTics`, `m_reputationTics`) are met, it distributes rewards to the team and resets the tics.
    4.  **Victory Conditions:** Checks if either team has reached the maximum score (`BG_AB_MAX_TEAM_SCORE`, 2000). If so, it calls `EndBattleGround`.
    5.  **Near Victory Warning:** If a team passes 1800 points, it sends a system message and plays a sound warning the opposing team.

### Node Control and Interaction

*   **`EventPlayerClickedOnFlag`:** Handles the interaction when a player clicks a node's banner.
    *   Validates that the battleground is active and the player is on a valid team for the node's current state.
    *   **Neutral Node:** Becomes **Contested** by the clicking player's team. Starts a 60-second capture timer. Awards "Base Assaulted" score.
    *   **Contested Node:**
        *   If the previous state was Neutral, it switches control to the new team (still Contested).
        *   If the previous state was Occupied by the *same* team, it returns to **Occupied** (defending). Awards "Base Defended" score.
        *   If the previous state was Occupied by the *enemy*, it switches to **Contested** by the new team (assaulting).
    *   **Occupied Node:** Becomes **Contested** by the attacking team. Starts a 60-second capture timer. Awards "Base Assaulted" score.
    *   Updates visuals, plays sounds, and sends chat messages appropriate to the action (Claimed, Assaulted, Defended, Taken).

*   **`_CreateBanner`:** Spawns the visual GameObject representing the node's flag/banner. It uses `SpawnEvent` to manage the lifecycle, ensuring old banners are despawned when the node state changes. Delays spawning slightly for contested/occupied states to allow animations.

*   **`_SendNodeUpdate`:** Sends World State updates to all clients to refresh the minimap icons and node status indicators. It clears the previous state's indicator and sets the new one. It also updates the global counters for "Occupied Bases" for both teams.

*   **`_NodeOccupied`:** Called when a node successfully transitions to Occupied. It counts how many nodes the owning team now holds. If the team holds 4 or 5 nodes, it casts specific spells (`SPELL_AB_QUEST_REWARD_4_BASES`, `SPELL_AB_QUEST_REWARD_5_BASES`) on the team, likely for quest completion or bonus effects.

*   **`_GetNodeNameId`:** Returns the language string ID for a node's name (e.g., "Stables") based on its index. Throws an assertion if an invalid node index is passed.

### Player Management

*   **`AddPlayer`:** Adds the player to the battleground and creates a `BattleGroundABScore` object to track their specific contributions (bases assaulted/defended).
*   **`RemovePlayer`:** Currently empty. Relies on parent class cleanup.
*   **`UpdatePlayerScore`:** Increments the player's specific score fields (`basesAssaulted`, `basesDefended`) or delegates to the parent class for standard scores.
*   **`GetClosestGraveYard`:** Determines where a dead player respawns.
    *   If the BG hasn't started, respawns at the entrance graveyard.
    *   If the BG is active, it finds all nodes occupied by the player's team.
    *   It calculates the Euclidean distance from the player's death position to each occupied node's graveyard.
    *   Respawn occurs at the closest occupied node. If no nodes are occupied, it falls back to the team's starting base graveyard.

### Visuals and Audio

*   **`FillInitialWorldStates`:** Populates the initial World State packet sent to clients when they join. This includes node icons, node states, occupied base counts, max score, warning threshold, and current team scores.
*   **`StartingEventCloseDoors`:** Despawns all buff objects before the battle starts to prevent early access.
*   **`StartingEventOpenDoors`:** Opens the entrance doors and spawns one random buff object (Speed, Regen, or Berserk) at each of the 5 nodes.

### End Game

*   **`EndBattleGround`:** Awards final Honor to the winning team. The amount depends on the player bracket. If it is a BG Weekend, double honor is awarded (note: the code awards honor twice for weekends, once for the weekend bonus and once for the standard win, effectively doubling it). Then calls the parent's end routine.

## Cross-Unit Boundaries

*   **`BattleGround` (Parent Class):**
    *   `Update`, `AddPlayer`, `Reset`, `EndBattleGround`, `UpdatePlayerScore`, `FillInitialWorldStates`, `PlaySoundToAll`, `RewardHonorToTeam`, `RewardReputationToTeam`, `SendMessage2ToAll`, `SendMessageToAll`, `UpdateWorldState`, `SpawnBGObject`, `OpenDoorEvent`, `SpawnEvent`, `AddObject`, `CastSpellOnTeam`, `GetTeamIndexByTeamId`, `GetBracketId`, `GetStatus`, `GetTypeID`.
    *   **Collaboration:** `BattleGroundAB` relies heavily on the parent `BattleGround` class for infrastructure: managing players, sending network packets, spawning objects, handling timers, and distributing rewards. `BattleGroundAB` overrides these methods to inject Arathi Basin-specific logic (e.g., node state updates in `Update`, specific score tracking in `UpdatePlayerScore`).

*   **`BattleGroundMgr`:**
    *   `CreateBattleGround`: Instantiates `BattleGroundAB`.
    *   `IsBgWeekend`: Checked during `Reset` and `EndBattleGround` to determine reward multipliers.
    *   `GetGameObjectEventIndex`: Used in `EventPlayerClickedOnFlag` to identify which node a clicked banner belongs to.

*   **`Player.Main`:**
    *   `GetTeam`: Used extensively to determine player allegiance for node control, respawn locations, and reward distribution.
    *   `LeaveBattleground`: Called in `HandleAreaTrigger` when a player steps into the exit zone.
    *   `KilledMonsterCredit`: Called in `EventPlayerClickedOnFlag` to credit players for interacting with nodes (likely for quests).
    *   `GetObjectGuid`: Used to map players to their score objects.
    *   `IsGameMaster`: Checked in `GetClosestGraveYard` to allow GMs to bypass normal respawn logic.
    *   `GetPositionX/Y`: Used in `GetClosestGraveYard` to calculate distance to graveyards.

*   **`Object` / `WorldObject.Object`:**
    *   `GetGUIDLow`: Used to identify GameObjects.
    *   `GetObjectGuid`: Used for player identification.

*   **`shared_Util`:**
    *   `urand`: Used in `StartingEventOpenDoors` to randomly select which buff type spawns at each node.

*   **`Errors`:**
    *   `PrintStacktraceAndThrow`: Called by `_GetNodeNameId` if an invalid node index is encountered, indicating a programming error.

*   **`Log.Main`:**
    *   `Out`: Used in `SetupBattleGround` to log errors if buff objects fail to spawn.

*   **`World`:**
    *   `GetWowPatch`: Used in `Reset` to determine reputation reward intervals based on the game version.

## Data Model

This unit does not directly interact with database tables. It operates entirely on in-memory state derived from configuration constants and runtime calculations. Any persistence of battleground results (like arena ratings or historical stats) would be handled by higher-level managers or separate systems not present in this unit.

## Notable Implementation Details

1.  **Node State Encoding:** Node states are encoded as integers:
    *   0: Neutral
    *   1: Alliance Contested
    *   2: Horde Contested
    *   3: Alliance Occupied
    *   4: Horde Occupied
    This allows simple arithmetic to determine team ownership (`state % 2`) and status (`state / 2` roughly).

2.  **Capture Timer Logic:** The 60-second capture timer (`BG_AB_FLAG_CAPTURING_TIME`) is stored in `m_nodeTimers`. It is decremented in `Update`. Crucially, if an enemy player contests an occupied node, the timer is reset to 60 seconds, and the node becomes contested by the enemy. If the original owner re-contests it, it returns to occupied immediately (no timer), rewarding defense.

3.  **Resource Ticking:** Points are not added every second. The interval between ticks decreases as more nodes are captured (12s for 1 node, down to 1s for 5 nodes). Points per tick increase significantly for 5 nodes (30 pts vs 10 pts). This creates a snowball effect for dominant teams.

4.  **Honor/Reputation Accumulation:** Rewards are not given instantly upon point gain. Instead, points are accumulated in `m_honorScoreTics` and `m_reputationScoreTics`. When these exceed a threshold (`m_honorTics`, `m_reputationTics`), rewards are distributed. This smooths out reward distribution over time.

5.  **Weekend Bonuses:** During "BG Weekends," honor and reputation intervals are reduced, effectively doubling the rate of reward distribution. The `EndBattleGround` function explicitly awards honor twice for winners on weekends.

6.  **Respawn Logic:** `GetClosestGraveYard` uses a simple Euclidean distance calculation (without square root for performance) to find the nearest occupied node's graveyard. This encourages teams to hold nodes near where their allies are dying.

7.  **Buff Randomization:** At the start of the match, one of three buffs (Speed, Regen, Berserk) is randomly chosen for each node. This adds variability to strategy.

8.  **Hardcoded Constants:** Many values (coordinates, spell IDs, sound IDs, world state IDs) are hardcoded in enums and arrays. This makes the code tightly coupled to the specific Arathi Basin instance data.

## Member Reference

*   **`BattleGroundAB`**: Constructor. Initializes member arrays, sets starting message IDs, and prepares the object for use.
*   **`~BattleGroundAB`**: Destructor. Default behavior.
*   **`Update`**: Main game loop. Manages node capture timers, accumulates team scores based on occupied nodes, distributes honor/reputation rewards, checks for victory conditions, and updates world states.
*   **`StartingEventCloseDoors`**: Despawns all buff objects before the battle begins.
*   **`StartingEventOpenDoors`**: Opens entrance doors and spawns one random buff object at each node.
*   **`AddPlayer`**: Adds a player to the battleground and initializes their `BattleGroundABScore` object.
*   **`RemovePlayer`**: Empty implementation. Relies on parent class.
*   **`HandleAreaTrigger`**: Handles players stepping into exit zones (Alliance/Horde) to leave the battleground.
*   **`_CreateBanner`**: Spawns the visual banner GameObject for a node, handling delays and despawning old banners.
*   **`_DelBanner`**: Declared but not implemented in this unit. Likely intended for manual banner removal.
*   **`_GetNodeNameId`**: Returns the language string ID for a node's name. Asserts on invalid input.
*   **`FillInitialWorldStates`**: Populates the initial World State packet with node icons, states, scores, and thresholds.
*   **`_SendNodeUpdate`**: Updates World States to reflect changes in node ownership and occupied base counts.
*   **`_NodeOccupied`**: Checks if a team now controls 4 or 5 nodes and casts corresponding reward spells.
*   **`EventPlayerClickedOnFlag`**: Handles player interactions with node banners. Manages state transitions (Neutral->Contested, Contested->Occupied, etc.), updates timers, scores, visuals, and audio.
*   **`SetupBattleGround`**: Spawns initial buff objects at each node location.
*   **`Reset`**: Resets all game state for a new match, including scores, timers, and node states. Sets reward intervals based on weekend status and patch version.
*   **`EndBattleGround`**: Awards final honor to the winning team, applying weekend bonuses if applicable.
*   **`GetClosestGraveYard`**: Calculates the nearest occupied node's graveyard for respawning a dead player. Falls back to starting base if no nodes are occupied.
*   **`UpdatePlayerScore`**: Updates player-specific scores for bases assaulted and defended. Delegates other score types to the parent class.

---

<!-- machine-true, projected from graph.json -->

## Map — BattleGroundAB

*Source:* BattleGroundAB.cpp, BattleGroundAB.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| BattleGroundAB | ctor | — | BattleGroundMgr/CreateBattleGround | — |
| ~BattleGroundAB | dtor | — | — | — |
| Update | method | BattleGround/GetBracketId, BattleGround/GetStatus, game_Battlegrounds_BattleGround/PlaySoundToAll, game_Battlegrounds_BattleGround/RewardHonorToTeam, game_Battlegrounds_BattleGround/RewardReputationToTeam, game_Battlegrounds_BattleGround/SendMessage2ToAll, game_Battlegrounds_BattleGround/SendMessageToAll, game_Battlegrounds_BattleGround/Update, game_Battlegrounds_BattleGround/UpdateWorldState | — | — |
| StartingEventCloseDoors | method | game_Battlegrounds_BattleGround/SpawnBGObject | — | — |
| StartingEventOpenDoors | method | game_Battlegrounds_BattleGround/OpenDoorEvent, game_Battlegrounds_BattleGround/SpawnBGObject, game_Battlegrounds_BattleGround/SpawnEvent, shared_Util/urand | — | — |
| AddPlayer | method | BattleGroundABScore/BattleGroundABScore, game_Battlegrounds_BattleGround/AddPlayer, Object/GetObjectGuid | — | — |
| RemovePlayer | method | — | — | — |
| HandleAreaTrigger | method | Player.Main/GetTeam, Player.Main/LeaveBattleground | — | — |
| _CreateBanner | method | game_Battlegrounds_BattleGround/SpawnEvent | — | — |
| _DelBanner | decl | — | — | — |
| _GetNodeNameId | method | Errors/PrintStacktraceAndThrow | — | — |
| FillInitialWorldStates | method | game_Battlegrounds_BattleGround/FillInitialWorldState#2, game_Battlegrounds_BattleGround/FillInitialWorldState#3 | — | — |
| _SendNodeUpdate | method | game_Battlegrounds_BattleGround/UpdateWorldState | — | — |
| _NodeOccupied | method | BattleGround/GetTeamIndexByTeamId, game_Battlegrounds_BattleGround/CastSpellOnTeam | — | — |
| EventPlayerClickedOnFlag | method | BattleGround/GetStatus, BattleGround/GetTeamIndexByTeamId, BattleGroundMgr/GetGameObjectEventIndex, game_Battlegrounds_BattleGround/PlaySoundToAll, game_Battlegrounds_BattleGround/SendMessage2ToAll, Object/GetGUIDLow, Player.Main/GetTeam, Player.Main/KilledMonsterCredit | — | — |
| SetupBattleGround | method | game_Battlegrounds_BattleGround/AddObject, Log.Main/Out | — | — |
| Reset | method | BattleGround/GetTypeID, BattleGroundMgr/IsBgWeekend, game_Battlegrounds_BattleGround/Reset, World/GetWowPatch | — | — |
| EndBattleGround | method | BattleGround/GetBracketId, BattleGround/GetTypeID, BattleGroundMgr/IsBgWeekend, game_Battlegrounds_BattleGround/EndBattleGround, game_Battlegrounds_BattleGround/RewardHonorToTeam | — | — |
| GetClosestGraveYard | method | BattleGround/GetStatus, BattleGround/GetTeamIndexByTeamId, Player.Main/GetTeam, Player.Main/IsGameMaster, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY | — | — |
| UpdatePlayerScore | method | game_Battlegrounds_BattleGround/UpdatePlayerScore, Object/GetObjectGuid | — | — |
