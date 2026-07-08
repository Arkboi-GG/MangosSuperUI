# HonorMgr

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# HonorMgr

**Purpose & Responsibilities**

The `HonorMgr` unit implements the World of Warcraft Classic-era honor system, comprising two distinct components:

1.  **`HonorMgr` (Per-Player Manager):** Tracks individual player honor statistics, including current rank points, honorable/dishonorable kills, weekly contributions, and historical data. It handles the real-time accumulation of honor credit (CP), immediate penalties for dishonorable kills, and the synchronization of these values with the client via network packets. It also persists this data to the database upon save/load.
2.  **`HonorMaintenancer` (Global Weekly Processor):** A singleton responsible for the weekly "honor maintenance" cycle. This complex batch process calculates final rank points for all active players based on their weekly contribution points (CP), applies decay to inactive players, determines faction-wide standings, assigns city protector titles, and flushes the calculated results back to the database. It manages the timing of these calculations using server game days and persistence markers.

## Data Model

The unit interacts with three database tables:

*   **`character_honor_cp`**: Stores raw honor contribution events.
    *   Columns: `guid` (player ID), `victim_type` (player/unit), `victim_id` (GUID or entry), `cp` (contribution points), `date` (server game day), `type` (honorable/dishonorable/etc.).
    *   Usage: `HonorMgr::Save` inserts new CP records. `HonorMaintenancer::LoadWeeklyScores` aggregates these records for the weekly calculation. `HonorMgr::Reset` deletes them.
*   **`characters`**: Stores persistent player state.
    *   Relevant Columns: `honor_rank_points`, `honor_highest_rank`, `honor_standing`, `honor_last_week_hk`, `honor_last_week_cp`, `honor_stored_hk`, `honor_stored_dk`, `extra_flags`.
    *   Usage: `HonorMgr::SaveStoredData` updates rank points and stored kills. `HonorMaintenancer::FlushRankPoints` updates final calculated ranks and standings. `HonorMaintenancer::SetCityRanks` manipulates `extra_flags` to assign city titles.
*   **`saved_variables`**: Stores global server state.
    *   Relevant Columns: `honor_last_maintenance_day`, `honor_next_maintenance_day`, `honor_maintenance_marker`.
    *   Usage: `HonorMaintenancer` uses these to track when the last weekly calculation occurred and whether a pending maintenance cycle needs to be executed on server startup.

## Member Behavior & Collaboration

### Per-Player Honor Management (`HonorMgr`)

#### Initialization and Persistence
*   **`HonorMgr`**: Constructor initializes the manager for a specific `Player`. Called by `Player.Main/Player#5`.
*   **`~HonorMgr`**: Destructor.
*   **`ClearHonorData`**: Resets all in-memory honor counters and clears the CP list. Called by `Player.Main/Player#5` during player initialization.
*   **`Load`**: Reads existing `character_honor_cp` records from the database into the `m_honorCP` list. Called by `Player.Main/LoadFromDB`.
*   **`Save`**: Persists new (`STATE_NEW`) honor CP records to `character_honor_cp`. It marks inserted records as `STATE_UNCHANGED` and moves them to a temporary list to avoid re-inserting them on subsequent saves. Called by `Player.Main/SaveToDB`.
*   **`SaveStoredData`**: Updates the `characters` table with current rank points, standing, highest rank, and stored kill counts. Called by `HonorMgr::Reset` and implicitly required for persistence (though `Player.Main/SaveToDB` typically triggers the main save flow, this method is explicitly called by `Reset`).
*   **`Reset`**: Completely wipes a player's honor history. It clears memory, deletes all rows from `character_honor_cp` for the player, saves the zeroed state to `characters`, and updates the client. Called by chat commands `ChatHandler.CharacterCommands/HandleHonorResetCommand` and `HandleResetHonorCommand`.

#### Honor Accumulation and Calculation
*   **`Add`**: The core entry point for gaining or losing honor.
    *   **Logic**: Validates input, determines the victim (source of honor), logs the event (including IP address if the victim is a player), and updates `m_rankPoints` immediately for dishonorable kills (subtracting CP). It adds the event to `m_honorCP` and triggers `Update()` and `SendPVPCredit()`.
    *   **Collaboration**: Called by `Player.Main/RewardHonor`, `Player.Main/RewardHonorOnDeath`, `Spell.Effects/EffectAddHonor`, and battleground updates. It calls `World/GetGameDay` for timestamping and `Log.Main/Out` for logging.
*   **`Update`**: Recalculates derived statistics (today/yesterday/this-week kills and CP) by iterating through `m_honorCP`. It then updates the player's object fields (byte values and uint32 values) to reflect the new state in the client UI.
    *   **Collaboration**: Called by `Add`, `Reset`, and `Player.Main/SendInitialPacketsBeforeAddToMap`. It calls `World/GetGameDay` and `HonorMaintenancer/GetWeekBeginDay` (via `sHonorMaintenancer`) to determine time windows.
*   **`CalculateRank`**: Static helper that determines the `HonorRankInfo` structure based on raw rank points and total honorable kills. It handles positive/negative ranks and visual rank mapping.
*   **`CalculateRankInfo`**: Static helper that populates `minRP` and `maxRP` thresholds for a given rank, used for the honor bar visualization.
*   **`CalculateTotalKills`**: Counts how many times the owner has killed a specific victim today. Used to apply diminishing returns on honor gain.
*   **`HonorableKillPoints`**: Calculates the base honor gain for a kill.
    *   **Collaboration**: Calls `Formulas/GetHonorGain` for the actual formula, passing in levels, ranks, and group size. Called by `Player.Main/RewardHonorOnDeath`.
*   **`DishonorableKillPoints`**: Calculates the penalty amount for a dishonorable kill based on the victim's level. Called by `Player.Main/RewardHonor`.

#### Accessors and Mutators
*   **`GetRank`**, **`GetCurrentHonorRank`**, **`GetHighestRank`**, **`GetStanding`**, **`GetRankPoints`**, **`GetStoredDK/HK`**, **`GetTotalDK/HK`**, **`GetLastWeekCP/HK`**, **`GetHonorCP`**: Standard getters for the internal state.
    *   **Collaboration**: Widely called by `Player.Main` methods (e.g., `BuyItemFromVendor`, `CanUseItem`, `SatisfyItemRequirements`), `ChatHandler` commands, and `PartyBotAI` for cloning player states.
*   **`SetRank`**, **`SetHighestRank`**, **`SetHighestRank#2`**, **`SetStanding`**, **`SetRankPoints`**, **`SetStoredDK/HK`**, **`SetTotalDK/HK`**, **`SetLastWeekCP/HK`**: Standard setters.
    *   **Note**: `SetHighestRank#2` is an overload taking a `uint8` rank value, used during database loading (`Player.Main/LoadFromDB`).
*   **`SendPVPCredit`**: Sends the `SMSG_PVP_CREDIT` packet to the client to display the floating honor text.
    *   **Collaboration**: Constructs the packet using `ByteBuffer` and sends it via `Player.Main/SendDirectMessage`. It checks if the victim is a racial leader (`Creature.Main/IsRacialLeader`) to adjust the displayed rank/type.

### Global Weekly Maintenance (`HonorMaintenancer`)

#### Lifecycle and Scheduling
*   **`HonorMaintenancer`** / **`~HonorMaintenancer`**: Singleton constructor/destructor.
*   **`Initialize`**: Loads the last maintenance day and marker from `saved_variables`. If no history exists, it sets the initial days based on the current server time. Called by `World/SetInitialWorldSettings`.
*   **`CheckMaintenanceDay`**: Checks if the current game day has passed the next scheduled maintenance day. If so, it toggles the maintenance marker in the database and optionally triggers a server restart if configured. Called by `World/Update`.
*   **`DoMaintenance`**: The main orchestration method. It processes all outstanding weekly periods in a loop.
    *   **Flow**: Clears old data -> Loads weekly scores -> Builds standing lists -> Distributes rank points (Alliance/Horde) -> Decays inactive players -> Sets city ranks -> Flushes results to DB -> Generates report -> Updates maintenance days.
    *   **Collaboration**: Called by `World/SetInitialWorldSettings` (if marker is set). It calls various internal methods and `Log.Main/Out` for progress tracking.
*   **`ToggleMaintenanceMarker`** / **`SetMaintenanceDays`**: Persist the maintenance state to `saved_variables`.

#### Score Loading and Processing
*   **`LoadWeeklyScores`**: Executes a complex SQL query to aggregate `character_honor_cp` data for the past week. It unions honorable kills, dishonorable kills, and other CP types, joining with `characters` to get level and account info. Results are stored in `m_weeklyScores`.
    *   **Tables**: `character_honor_cp`, `characters`.
*   **`LoadStandingLists`**: Iterates through `m_weeklyScores` to populate `m_allianceStandingList`, `m_hordeStandingList`, and `m_inactiveStandingList`. Players with fewer than `CONFIG_UINT32_MIN_HONOR_KILLS` are marked inactive. Lists are sorted by CP descending.
    *   **Collaboration**: Calls `ObjectMgr/GetPlayerTeamByGUID` to determine faction.
*   **`GenerateScores`**: Calculates the breakpoint values (`BRK`, `FX`, `FY`) used for the piecewise linear interpolation of rank points. It adjusts breakpoints based on the WoW patch version (pre-1.12 vs 1.12+) and the configured pool size.
*   **`CalculateRpEarning`**: Interpolates the rank points earned based on a player's CP and the generated score breakpoints.
*   **`CalculateRpDecay`**: Applies decay to rank points. If the new earning is less than the decayed old points, the difference is halved (soft decay). Minimum delta is capped at -2500.
*   **`MaximumRpAtLevel`**: Returns the hard cap on rank points for a given player level.
*   **`DistributeRankPoints`**: Iterates through a faction's standing list, calculating earning, applying decay, capping at level max, and assigning a standing position.
*   **`InactiveDecayRankPoints`**: Applies decay to players in the inactive list (using 0 earning).

#### Finalization and Reporting
*   **`FlushRankPoints`**: Writes the final calculated values back to the `characters` table. It updates `honor_rank_points`, `honor_highest_rank`, `honor_standing`, and stored kills. It also deletes old CP records from `character_honor_cp` older than the end of the processed week.
    *   **Tables**: `characters`, `character_honor_cp`.
*   **`SetCityRanks`**: Identifies the top-ranked player of each race (by `honor_standing`) and sets the `extra_flags` bit `0x0400` (City Protector title) in the `characters` table. Uses a transaction to ensure consistency.
    *   **Tables**: `characters`.
*   **`CreateCalculationReport`**: Writes a detailed text file (`HCR_<timestamp>.txt`) containing the breakdown of scores, breakpoints, and individual player calculations for debugging/audit purposes.
    *   **Collaboration**: Calls `World/GetHonorPath` for the output directory.

#### Helper Methods
*   **`GetStandingListByTeam`**: Returns the appropriate standing list vector.
*   **`GetStandingCPByPosition`**: Retrieves the CP value for a player at a specific rank position in a list.
*   **`GetStandingPositionByGUID`**: Finds the rank position of a specific GUID in a faction's list.
*   **`GetLastMaintenanceDay`** / **`GetNextMaintenanceDay`** / **`GetWeekBeginDay`** / **`GetWeekEndDay`**: Accessors for the maintenance schedule state.

## Notable Implementation Details

1.  **Immediate Dishonorable Penalty**: In `HonorMgr::Add`, dishonorable kills immediately subtract from `m_rankPoints`. This is distinct from honorable gains, which are accumulated in `m_honorCP` and only converted to rank points during the weekly maintenance. This ensures players see the penalty instantly.
2.  **Soft Decay Logic**: `HonorMaintenancer::CalculateRpDecay` implements a "soft" decay. If a player earns less than their decay amount, the loss is halved (`delta = delta / 2`). This prevents rapid drop-off for moderately active players.
3.  **Multi-Period Catch-Up**: `HonorMaintenancer::DoMaintenance` contains a `while` loop that processes multiple outstanding weeks if the server was offline for several maintenance cycles. This prevents the need for manual intervention or multiple restarts to catch up.
4.  **Transaction Safety in City Ranks**: `HonorMaintenancer::SetCityRanks` explicitly uses `BeginTransaction` and `CommitTransactionDirect` to ensure that clearing and reassigning city protector flags is atomic, especially important if the character database uses worker threads.
5.  **Direct Execution for Flush**: `HonorMaintenancer::FlushRankPoints` uses `DirectExecute` and `DirectPExecute` to bypass the worker thread queue, ensuring data is committed immediately before the next maintenance cycle might read it.
6.  **Patch-Specific Breakpoints**: `HonorMaintenancer::GenerateScores` hardcodes different breakpoint arrays for pre-1.12 and 1.12+ patches, reflecting changes in the original game's honor formulas.
7.  **Client-Side Rank Visualization**: `HonorMgr::Update` calculates the honor bar value (`PLAYER_FIELD_BYTES2`) based on the current rank points relative to the min/max of the current rank. It handles negative ranks by inverting the bar direction.

## Member Reference

**GetStandingListByTeam**: Returns a reference to the `HonorStandingList` for the specified faction (Alliance or Horde). Defaults to Alliance if invalid.

**GetStandingCPByPosition**: Iterates through a standing list to find the CP value of the player at the given 1-based position. Returns 0.0f if not found.

**GetStandingPositionByGUID**: Finds the 1-based rank position of a player GUID within a faction's standing list. Returns 0 if not found.

**HonorMaintenancer**: Constructor initializes maintenance day counters and the start marker.

**~HonorMaintenancer**: Destructor.

**LoadWeeklyScores**: Queries `character_honor_cp` and `characters` to aggregate weekly stats (HK, DK, CP, Level, Account) for all players with activity or existing rank points. Stores results in `m_weeklyScores`.

**GetLastMaintenanceDay**: Returns the stored last maintenance day.

**GetNextMaintenanceDay**: Returns the stored next maintenance day.

**GetWeekBeginDay**: Returns the last maintenance day (start of the current week).

**GetWeekEndDay**: Returns the last maintenance day + 6 (end of the current week).

**LoadStandingLists**: Processes `m_weeklyScores` to populate Alliance, Horde, and Inactive standing lists based on minimum HK requirements. Sorts lists by CP descending.

**DistributeRankPoints**: Iterates through a faction's standing list, calculating earned RP, applying decay, capping at level max, and assigning standing positions.

**HonorMgr**: Constructor associates the manager with a `Player` owner.

**~HonorMgr**: Destructor.

**InactiveDecayRankPoints**: Applies decay to players in the inactive list, using 0 as the earning value.

**GetRank**: Returns the current `HonorRankInfo` structure.

**GetCurrentHonorRank**: Returns the internal rank integer.

**SetRank**: Sets the current `HonorRankInfo` structure.

**GetHighestRank**: Returns the highest achieved `HonorRankInfo` structure.

**SetHighestRank**: Sets the highest achieved `HonorRankInfo` structure.

**SetHighestRank#2**: Overload that sets the highest rank from a `uint8` value, recalculating min/max RP.

**SetCityRanks**: Identifies top-ranked players per race and assigns the City Protector title flag in the `characters` table using a transaction.

**GetStanding**: Returns the player's weekly standing position.

**SetStanding**: Sets the player's weekly standing position.

**GetRankPoints**: Returns the current float rank points.

**SetRankPoints**: Sets the current float rank points.

**GetStoredDK**: Returns the stored dishonorable kill count.

**SetStoredDK**: Sets the stored dishonorable kill count.

**GetStoredHK**: Returns the stored honorable kill count.

**SetStoredHK**: Sets the stored honorable kill count.

**GetTotalDK**: Returns the total lifetime dishonorable kills.

**SetTotalDK**: Sets the total lifetime dishonorable kills.

**GetTotalHK**: Returns the total lifetime honorable kills.

**SetTotalHK**: Sets the total lifetime honorable kills.

**GetLastWeekCP**: Returns the contribution points from the previous week.

**SetLastWeekCP**: Sets the contribution points from the previous week.

**GetLastWeekHK**: Returns the honorable kills from the previous week.

**SetLastWeekHK**: Sets the honorable kills from the previous week.

**GetHonorCP**: Returns a reference to the list of honor contribution records.

**FlushRankPoints**: Updates `characters` table with final calculated ranks, standings, and stored kills. Deletes old CP records from `character_honor_cp`.

**DoMaintenance**: Orchestrates the weekly honor calculation cycle, handling multiple outstanding periods if necessary.

**CreateCalculationReport**: Generates a text file detailing the calculation results for audit/debugging.

**GenerateScores**: Computes the breakpoint arrays (BRK, FX, FY) for RP interpolation based on faction size and patch version.

**CalculateRpEarning**: Interpolates rank points earned based on CP and score breakpoints.

**CalculateRpDecay**: Calculates new RP after applying decay to old RP and adding new earnings.

**MaximumRpAtLevel**: Returns the maximum allowed rank points for a given player level.

**CheckMaintenanceDay**: Checks if maintenance is due and triggers restart/marker update if configured.

**ToggleMaintenanceMarker**: Flips the maintenance marker boolean and persists it to `saved_variables`.

**SetMaintenanceDays**: Updates the last and next maintenance days in memory and `saved_variables`.

**Initialize**: Loads maintenance state from `saved_variables` on server startup.

**ClearHonorData**: Resets all in-memory honor counters and clears the CP list.

**Reset**: Wipes player honor data from memory and database, then updates the client.

**ClearHonorCP**: Clears the in-memory list of honor contribution records.

**Save**: Persists new honor CP records to `character_honor_cp`.

**SaveStoredData**: Updates persistent honor stats in the `characters` table.

**Load**: Loads honor CP records from the database into memory.

**Add**: Adds an honor event (gain or loss), updates immediate RP for dishonorable kills, and triggers client updates.

**Update**: Recalculates daily/weekly stats and syncs player object fields with the client.

**InitRankInfo**: Initializes a `HonorRankInfo` structure with default values.

**CalculateTotalKills**: Counts kills against a specific victim today.

**CalculateRankInfo**: Populates min/max RP thresholds for a given rank.

**CalculateRank**: Determines the rank structure based on raw points and total HK.

**DishonorableKillPoints**: Calculates the honor penalty for a dishonorable kill based on level.

**HonorableKillPoints**: Calculates the honor gain for a kill using the `Formulas` module.

**SendPVPCredit**: Sends the `SMSG_PVP_CREDIT` packet to display floating honor text.

---

<!-- machine-true, projected from graph.json -->

## Map — HonorMgr

*Source:* HonorMgr.cpp, HonorMgr.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GetStandingListByTeam | method | — | — | — |
| GetStandingCPByPosition | method | — | — | — |
| GetStandingPositionByGUID | method | — | — | — |
| HonorMaintenancer | ctor | — | — | — |
| ~HonorMaintenancer | dtor | — | — | — |
| LoadWeeklyScores | method | Database/Query, Field/GetFloat, Field/GetUInt32, QueryResult/Fetch, QueryResult/NextRow, WeeklyScore/WeeklyScore | — | character_honor_cp |
| GetLastMaintenanceDay | method | — | — | — |
| GetNextMaintenanceDay | method | — | — | — |
| GetWeekBeginDay | method | — | — | — |
| GetWeekEndDay | method | — | — | — |
| LoadStandingLists | method | HonorStanding/HonorStanding, Log.Main/Out, ObjectGuid/ObjectGuid#2, ObjectMgr/GetPlayerTeamByGUID, World/getConfig#4 | — | — |
| DistributeRankPoints | method | — | — | — |
| HonorMgr | ctor | — | Player.Main/Player#5 | — |
| ~HonorMgr | dtor | — | — | — |
| InactiveDecayRankPoints | method | Common/finiteAlways | — | — |
| GetRank | method | — | BattleGroundMgr/BuildPvpLogDataPacket, ChatHandler.CharacterCommands/HandleHonorShow, Conditions/Evaluate, PartyBotAI/CloneFromPlayer, Player.Main/BuyItemFromVendor, Player.Main/CanUseItem#2, Player.Main/GetReputationPriceDiscount, Player.Main/SatisfyItemRequirements | — |
| GetCurrentHonorRank | method | — | game_Chat_Channel/Say | — |
| SetRank | method | — | PartyBotAI/CloneFromPlayer, Player.Main/SatisfyItemRequirements | — |
| GetHighestRank | method | — | ChatHandler.CharacterCommands/HandleHonorShow, PartyBotAI/CloneFromPlayer, Player.Main/BuyItemFromVendor, Player.Main/CanUseItem#2, Player.Main/SatisfyItemRequirements, Player.Main/SaveToDB, WorldSession.MiscHandler/HandleInspectHonorStatsOpcode | — |
| SetHighestRank | method | — | PartyBotAI/CloneFromPlayer, Player.Main/SatisfyItemRequirements | — |
| SetHighestRank#2 | method | — | Player.Main/LoadFromDB | — |
| SetCityRanks | method | Database/BeginTransaction, Database/CommitTransactionDirect, Database/Execute#2, Database/PExecute#2, Database/PQuery, Field/GetUInt32, Log.Main/Out, QueryResult/Fetch, QueryResult/NextRow | — | characters |
| GetStanding | method | — | Player.Main/SaveToDB | — |
| SetStanding | method | — | Player.Main/LoadFromDB | — |
| GetRankPoints | method | — | ChatHandler.CharacterCommands/HandleHonorShow, Player.Main/SaveToDB | — |
| SetRankPoints | method | — | ChatHandler.CharacterCommands/HandleHonorSetRPCommand, Player.Main/LoadFromDB | — |
| GetStoredDK | method | — | Player.Main/SaveToDB | — |
| SetStoredDK | method | — | Player.Main/LoadFromDB | — |
| GetStoredHK | method | — | Player.Main/SaveToDB | — |
| SetStoredHK | method | — | Player.Main/LoadFromDB | — |
| GetTotalDK | method | — | — | — |
| SetTotalDK | method | — | — | — |
| GetTotalHK | method | — | — | — |
| SetTotalHK | method | — | — | — |
| GetLastWeekCP | method | — | Player.Main/SaveToDB | — |
| SetLastWeekCP | method | — | Player.Main/LoadFromDB | — |
| GetLastWeekHK | method | — | Player.Main/SaveToDB | — |
| SetLastWeekHK | method | — | Player.Main/LoadFromDB | — |
| GetHonorCP | method | — | — | — |
| FlushRankPoints | method | Common/finiteAlways, Database/DirectExecute, Database/DirectPExecute, HonorRankInfo/HonorRankInfo | — | characters, character_honor_cp |
| DoMaintenance | method | Log.Main/Out, World/getConfig, World/GetGameDay | World/SetInitialWorldSettings | — |
| CreateCalculationReport | method | Log.Main/GetTimestampStr, Log.Main/Out, World/GetHonorPath | — | — |
| GenerateScores | method | World/getConfig#4, World/GetWowPatch | — | — |
| CalculateRpEarning | method | — | — | — |
| CalculateRpDecay | method | World/getConfig#2 | — | — |
| MaximumRpAtLevel | method | — | — | — |
| CheckMaintenanceDay | method | Log.Main/Out, World/getConfig, World/GetGameDay, World/ShutdownServ | World/Update | — |
| ToggleMaintenanceMarker | method | Database/DirectPExecute | — | saved_variables |
| SetMaintenanceDays | method | Database/DirectPExecute | — | saved_variables |
| Initialize | method | Database/Query, Field/GetBool, Field/GetUInt32, Log.Main/Out, QueryResult/Fetch, World/GetLastMaintenanceDay | World/SetInitialWorldSettings | saved_variables |
| ClearHonorData | method | — | Player.Main/Player#5 | — |
| Reset | method | Database/PExecute#2, Object/GetGUIDLow | ChatHandler.CharacterCommands/HandleHonorResetCommand, ChatHandler.CharacterCommands/HandleResetHonorCommand | character_honor_cp |
| ClearHonorCP | method | — | — | — |
| Save | method | Common/finiteAlways, Database/PExecute#2, Object/GetGUIDLow | Player.Main/SaveToDB | character_honor_cp |
| SaveStoredData | method | Common/finiteAlways, Database/PExecute#2, Object/GetGUIDLow | — | characters |
| Load | method | Field/GetFloat, Field/GetUInt32, Field/GetUInt8, QueryResult/Fetch, QueryResult/NextRow | Player.Main/LoadFromDB | — |
| Add | method | Log.Main/Out, Map.Main/IsBattleGround, Object/GetEntry, Object/GetGUIDLow, Object/GetTypeId, Object/ToPlayer#2, Player.Main/GetSession, World/GetGameDay, WorldObject.Object/GetMap, WorldObject.Object/GetName, WorldSession.Main/GetAccountId, WorldSession.Main/GetPlayerName, WorldSession.Main/GetRemoteAddress | ChatHandler.CharacterCommands/HandleHonorAddCommand, game_Battlegrounds_BattleGround/UpdatePlayerScore, Player.Main/RewardHonor, Player.Main/RewardHonorOnDeath, Spell.Effects/EffectAddHonor | — |
| Update | method | World/GetGameDay, WorldObject.Object/SetByteValue, WorldObject.Object/SetUInt16Value, WorldObject.Object/SetUInt32Value | ChatHandler.CharacterCommands/HandleHonorSetRPCommand, Player.Main/SaveToDB, Player.Main/SendInitialPacketsBeforeAddToMap | — |
| InitRankInfo | method | — | — | — |
| CalculateTotalKills | method | Object/GetEntry, Object/GetGUIDLow, Object/GetTypeId, World/GetGameDay | — | — |
| CalculateRankInfo | method | — | Player.Main/SatisfyItemRequirements | — |
| CalculateRank | method | HonorRankInfo/HonorRankInfo | — | — |
| DishonorableKillPoints | method | — | Player.Main/RewardHonor | — |
| HonorableKillPoints | method | Formulas/GetHonorGain, Player.Main/GetHonorMgr, Unit.Main/GetLevel | Player.Main/RewardHonorOnDeath | — |
| SendPVPCredit | method | ByteBuffer/operator<<#10, ByteBuffer/operator<<#4, ByteBuffer/operator<<#5, Creature.Main/IsRacialLeader, Object/GetObjectGuid, Object/IsCreature, Object/IsPlayer, ObjectGuid/operator<<, Player.Main/GetHonorMgr#2, Player.Main/SendDirectMessage, WorldPacket/WorldPacket#4 | — | — |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `character_honor_cp`: guid int(11) unsigned, victim_type tinyint(3) unsigned, victim_id int(11) unsigned, cp float, date int(11) unsigned, type tinyint(3) unsigned
- `characters`: guid int(11) unsigned PK, account int(11) unsigned, name varchar(12), race tinyint(3) unsigned, class tinyint(3) unsigned, gender tinyint(3) unsigned, skin tinyint(3) unsigned, face tinyint(3) unsigned, hair_style tinyint(3) unsigned, hair_color tinyint(3) unsigned, facial_hair tinyint(3) unsigned, level tinyint(3) unsigned, xp int(10) unsigned, money int(10) unsigned, character_flags int(10) unsigned, zone int(11) unsigned, map int(11) unsigned, instance int(11) unsigned, position_x float, position_y float, position_z float, orientation float, transport_guid bigint(20) unsigned, transport_x float, transport_y float, transport_z float, transport_o float, known_taxi_mask longtext?, current_taxi_path text?, online tinyint(3) unsigned, played_time_total int(11) unsigned, played_time_level int(11) unsigned, create_time bigint(20) unsigned, logout_time bigint(20) unsigned, rest_bonus float, reset_talents_multiplier int(11) unsigned, reset_talents_time bigint(20) unsigned, death_expire_time bigint(20) unsigned, stable_slots tinyint(1) unsigned, bank_bag_slots tinyint(1) unsigned, extra_flags int(11) unsigned, honor_rank_points float, honor_highest_rank int(11) unsigned, honor_standing int(11) unsigned, honor_last_week_hk int(11) unsigned, honor_last_week_cp float, honor_stored_hk int(11), honor_stored_dk int(11), watched_faction int(11), drunk smallint(5) unsigned, health int(10) unsigned, power1 int(10) unsigned, power2 int(10) unsigned, power3 int(10) unsigned, power4 int(10) unsigned, power5 int(10) unsigned, explored_zones longtext?, equipment_cache longtext?, ammo_id int(10) unsigned, action_bars tinyint(3) unsigned, deleted_account int(11) unsigned?, deleted_name varchar(12)?, deleted_time bigint(20)?, world_phase_mask int(11)?
- `saved_variables`: key tinyint(1) unsigned PK, cleaning_flags int(11) unsigned, honor_last_maintenance_day int(11) unsigned, honor_next_maintenance_day int(11) unsigned, honor_maintenance_marker tinyint(1) unsigned

*`?` = nullable, `PK` = primary key column.*

