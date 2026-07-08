# Honor & PvP Rank Rates

<!-- aliases: honor rates, honor gain, pvp rank, faster honor, honor farming -->
<!-- documentation: model-written from source via the local LLM; review before trusting -->

Honor and PvP rank in VMaNGOS operate on a weekly cycle driven by Contribution Points (CP) and Rank Points (RP). Players earn CP through PvP activities—honorable kills, battleground participation, and racial leader defeats—which are recorded in memory and persisted to the `character_honor_cp` database table. These CP values determine a player’s standing relative to their faction peers. At the end of each maintenance week, the server calculates final RP using a curve-based interpolation system that accounts for faction size and individual performance. RP determines a player’s visible honor rank (e.g., Private, Sergeant, Champion) and is subject to decay for inactive players. Dishonorable kills impose immediate RP penalties. The entire system is orchestrated by `HonorMgr` and `HonorMaintenancer`, with data flowing from `Player` actions into weekly aggregation, then flushed to the `characters` table during maintenance.

The process begins when a player earns honor via `Player::RewardHonor` or `Player::RewardHonorOnDeath`. These methods validate eligibility using `Player::IsHonorOrXPTarget`, which filters out gray-level targets, pets, totems, and creatures with zero XP multipliers. Valid honorable kills add CP via `HonorMgr::Add`, which logs the event, updates immediate RP for dishonorable kills, and notifies the client. CP accumulates throughout the week in `HonorMgr::m_honorCP` and is saved to `character_honor_cp`.

At the start of the maintenance window—determined by `MaintenanceDay` and tracked via `HonorMaintenancer::CheckMaintenanceDay`—the server triggers `HonorMaintenancer::DoMaintenance`. This routine loads weekly scores, builds standing lists per faction via `GetStandingListByTeam`, and computes score breakpoints using `GenerateScores`. Breakpoints depend on `PvP.PoolSizePerFaction`: if set to 0, the actual number of active players defines the pool; otherwise, the configured value caps the pool size. Using these breakpoints, `CalculateRpEarning` interpolates RP earned from CP via linear segments defined by `FX` (CP thresholds) and `FY` (RP outputs). `CalculateRpDecay` then applies decay to existing RP based on `RpDecay`, subtracting a portion of old RP before adding new earnings, with a floor of -2500 delta. Final RP is capped by level via `MaximumRpAtLevel`.

After distribution, `FlushRankPoints` writes results to the `characters` table: updating `honor_rank_points`, `honor_highest_rank`, `honor_standing`, `honor_last_week_hk`, `honor_stored_hk`, `honor_stored_dk`, and `honor_last_week_cp`. Old CP records older than the week’s end are purged from `character_honor_cp`. A detailed report is optionally generated via `CreateCalculationReport`. If `AutoHonorRestart` is enabled, the server restarts 15 minutes after the maintenance window closes.

Dishonorable kills are handled separately: `Player::RewardHonor` checks for civilian kills and, if `PvP.DishonorableKills` is enabled, calls `HonorMgr::Add` with `DISHONORABLE` type. `HonorMgr::DishonorableKillPoints` calculates the penalty based on killer level, scaling from 10 to 100 points. This penalty is immediately subtracted from `m_rankPoints` in `HonorMgr::Add`, bypassing weekly calculation.

## How to Modify

### Config

- **`PvP.PoolSizePerFaction`** (default: `0`): Controls the size of the PvP ranking pool used in RP calculation. If `0`, the actual number of active players in the faction determines the pool. Setting a positive integer caps the pool at that number, affecting how CP translates to RP via breakpoint interpolation. Higher values dilute top-tier rewards; lower values concentrate them.
- **`RpDecay`** (default: `0.2`): Multiplier applied to existing RP during weekly maintenance to calculate decay. Formula: `decay = floor(RpDecay * oldRp + 0.5)`. New RP = `oldRp + (newEarnings - decay)`, with negative deltas halved and floored at -2500. Increasing this accelerates rank loss for inactive players.
- **`MaintenanceDay`** (default: `3`): The day of the week (1=Monday, 7=Sunday) when honor maintenance occurs. Changing this shifts the weekly reset window. Must align with server uptime expectations.
- **`AutoHonorRestart`** (default: `1`): If `1`, the server automatically restarts 15 minutes after the maintenance window closes to ensure clean state for the new week. Set to `0` to disable automatic restarts.
- **`MinHonorKills`** (default: `0`): Minimum honorable kills required to participate in honor ranking. If `0`, eligibility is determined by patch version. Non-zero values enforce a hard threshold.
- **`PvP.DishonorableKills`** (default: `1`): If `1`, killing civilians inflicts dishonorable kill penalties. Set to `0` to disable DK tracking and penalties entirely.

No other config keys directly influence honor gain rates, RP curves, or rank thresholds.

### Database

- **`characters` table**: Columns `honor_rank_points`, `honor_highest_rank`, `honor_standing`, `honor_last_week_hk`, `honor_stored_hk`, `honor_stored_dk`, and `honor_last_week_cp` store persistent honor state. Editing these manually overrides calculated values but will be overwritten at next maintenance. Useful for resetting or seeding test characters.
- **`character_honor_cp` table**: Stores raw CP events with columns `guid`, `victim_type`, `victim_id`, `cp`, `date`, and `type`. Deleting rows removes historical CP data, affecting future standing calculations. Truncating this table resets all accumulated CP for the current week.

No schema modifications are supported; column structures are fixed. Manual edits should be cautious and backed up.

### Code

For changes beyond config and data:

- **`HonorMgr::DishonorableKillPoints`** (`HonorMgr.cpp:1030-1045`): Edit the piecewise formula to alter DK penalty scaling by level. Currently caps at 100 points. Modify thresholds or slopes to adjust severity.
- **`HonorMaintenancer::GenerateScores`** (`HonorMgr.cpp:467-566`): Adjust `BRK` arrays (breakpoint percentages) or `FY` values (RP outputs) to reshape the CP-to-RP curve. Note: `BRK` values differ pre/post 1.12; ensure consistency with `sWorld.GetWowPatch()`.
- **`HonorMaintenancer::CalculateRpDecay`** (`HonorMgr.cpp:583-597`): Modify decay logic, e.g., remove halving of negative deltas or change the -2500 floor. Also, replace `sWorld.getConfig(CONFIG_FLOAT_RP_DECAY)` with a custom multiplier if needed.
- **`Player::IsHonorOrXPTarget`** (`Player.cpp:19988-20002`): Alter eligibility criteria—e.g., allow pet kills to grant honor by removing the `IsPet()` check, or adjust gray-level filtering via `MaNGOS::XP::GetGrayLevel`.
- **`HonorMgr::Add`** (`HonorMgr.cpp:787-836`): Change how CP is logged or how immediate RP penalties are applied. For example, remove the `m_rankPoints -= honorCP.cp` line to defer DK penalties to weekly calculation.
- **`HonorMaintenancer::DoMaintenance`** (`HonorMgr.cpp:269-325`): Modify the maintenance loop to skip certain steps, add logging, or integrate external services. Be cautious with `ToggleMaintenanceMarker()` and `SetMaintenanceDays()` to avoid infinite loops.

Recompile after any code changes. Test thoroughly in a non-production environment.

## Path Reference

**GetStandingListByTeam** (HonorMgr.cpp:21-32) — Returns the faction-specific standing list (Alliance or Horde) used during weekly maintenance to sort players by CP.

**GetStandingCPByPosition** (HonorMgr.cpp:34-45) — Retrieves the CP value of the player at a given 1-based position in the standing list, used to sample CP thresholds for RP curve generation.

**GetStandingPositionByGUID** (HonorMgr.cpp:47-59) — Finds the 1-based rank position of a player GUID within their faction’s standing list, enabling lookup of standing for reporting or debugging.

**GetLastMaintenanceDay** (HonorMgr.h:81-81) — Returns the server day number of the last completed honor maintenance cycle, anchoring the current week’s start.

**GetNextMaintenanceDay** (HonorMgr.h:82-82) — Returns the server day number when the next maintenance cycle is scheduled, used to trigger `CheckMaintenanceDay`.

**GetWeekBeginDay** (HonorMgr.h:83-83) — Alias for `GetLastMaintenanceDay`; marks the start of the current honor accumulation week.

**GetWeekEndDay** (HonorMgr.h:84-84) — Returns `GetLastMaintenanceDay + 6`; defines the end of the current honor accumulation week, used to purge old CP records.

**DistributeRankPoints** (HonorMgr.cpp:136-168) — Iterates through a faction’s standing list, calculates RP earnings via `CalculateRpEarning`, applies decay via `CalculateRpDecay`, caps RP by level, and assigns standing positions.

**GetRank** (HonorMgr.h:178-178) — Returns the current `HonorRankInfo` structure for the player, including visual rank and RP thresholds.

**GetCurrentHonorRank** (HonorMgr.h:179-179) — Returns the internal integer rank value, used for quick comparisons without full struct overhead.

**GetHighestRank** (HonorMgr.h:181-181) — Returns the highest `HonorRankInfo` ever achieved by the player, preserved across weeks for display purposes.

**GetStanding** (HonorMgr.h:188-188) — Returns the player’s weekly standing position (1-based) within their faction, updated during maintenance.

**GetRankPoints** (HonorMgr.h:190-190) — Returns the current float value of rank points, reflecting both earned and decayed amounts.

**GetStoredDK** (HonorMgr.h:192-192) — Returns the cumulative count of dishonorable kills stored in the player’s record, incremented weekly.

**GetStoredHK** (HonorMgr.h:194-194) — Returns the cumulative count of honorable kills stored in the player’s record, incremented weekly.

**GetTotalDK** (HonorMgr.h:196-196) — Returns the lifetime total of dishonorable kills, including those from previous weeks.

**GetTotalHK** (HonorMgr.h:198-198) — Returns the lifetime total of honorable kills, including those from previous weeks.

**GetLastWeekCP** (HonorMgr.h:200-200) — Returns the total Contribution Points accumulated by the player in the previous maintenance week.

**GetLastWeekHK** (HonorMgr.h:202-202) — Returns the number of honorable kills achieved by the player in the previous maintenance week.

**GetHonorCP** (HonorMgr.h:205-205) — Returns a reference to the in-memory list of honor contribution records for the current week, used for logging and calculation.

**FlushRankPoints** (HonorMgr.cpp:237-267) — Writes final calculated ranks, standings, and kill counts to the `characters` table, and deletes expired CP records from `character_honor_cp`.

**DoMaintenance** (HonorMgr.cpp:269-325) — Orchestrates the entire weekly honor calculation cycle, including loading scores, distributing RP, applying decay, flushing data, and generating reports. Handles multiple outstanding periods if the server was offline.

**CreateCalculationReport** (HonorMgr.cpp:327-465) — Generates a text file detailing the honor calculation results for each faction, including breakpoints, player stats, and decay outcomes, for auditing and debugging.

**GenerateScores** (HonorMgr.cpp:467-566) — Computes the breakpoint arrays (BRK, FX, FY) used for RP interpolation, based on faction size and patch version, sampling CP values from the standing list.

**CalculateRpEarning** (HonorMgr.cpp:568-581) — Interpolates the RP earned by a player based on their CP and the precomputed score breakpoints, using linear interpolation between FX/FY pairs.

**CalculateRpDecay** (HonorMgr.cpp:583-597) — Calculates the new RP value by applying decay to old RP, subtracting it from new earnings, halving negative deltas, and flooring at -2500.

**CheckMaintenanceDay** (HonorMgr.cpp:616-628) — Checks if the current server day has reached the next maintenance day, triggers a restart if `AutoHonorRestart` is enabled, and sets the maintenance marker.

**ClearHonorData** (HonorMgr.cpp:666-679) — Resets all in-memory honor counters and clears the CP list for a player, typically called when loading a new character or resetting state.

**ClearHonorCP** (HonorMgr.cpp:695-698) — Clears only the in-memory list of honor contribution records, preserving other counters.

**Add** (HonorMgr.cpp:787-836) — Adds an honor event (gain or loss) to the player’s CP list, updates immediate RP for dishonorable kills, logs the event, and sends client notifications.

**CalculateTotalKills** (HonorMgr.cpp:946-973) — Counts the number of kills a player has made against a specific victim today, used for limiting repeated rewards or detecting abuse.

**CalculateRankInfo** (HonorMgr.cpp:975-994) — Populates the min/max RP thresholds for a given rank, adjusting for positive/negative ranks and special cases like rank 5.

**CalculateRank** (HonorMgr.cpp:996-1028) — Determines the player’s rank structure based on raw RP and total HK, mapping RP ranges to visual ranks and handling edge cases.

**DishonorableKillPoints** (HonorMgr.cpp:1030-1045) — Calculates the honor penalty for a dishonorable kill based on the killer’s level, scaling from 10 to 100 points with tiered increments.

**GetHonorMgr** (Player.h:2221-2221) — Provides mutable access to the player’s `HonorMgr` instance, allowing modification of honor state.

**GetHonorMgr#2** (Player.h:2222-2222) — Provides const access to the player’s `HonorMgr` instance, for read-only queries.

**IsHonorOrXPTarget** (Player.cpp:19988-20002) — Filters out ineligible targets for honor/XP gain, excluding gray-level enemies, pets, totems, and creatures with zero XP multipliers.

**RewardHonor** (Player.cpp:21854-21890) — Grants honor for killing racial leaders or civilians (if dishonorable kills are enabled), calling `HonorMgr::Add` with appropriate CP and type.

**RewardHonorOnDeath** (Player.cpp:21892-21963) — Distributes honor shares among attackers who dealt damage in the last minute before death, proportional to damage dealt, respecting group distances and eligibility.

**LoadConfigSettings** (World.cpp:440-1245) — Reads and validates server configuration from `mangosd.conf`, including `PvP.PoolSizePerFaction`, `RpDecay`, `MaintenanceDay`, `AutoHonorRestart`, `MinHonorKills`, and `PvP.DishonorableKills`, storing them for runtime access.

---

<!-- machine-true, projected from graph.json -->

## Map — Honor & PvP Rank Rates

*Source:* HonorMgr.cpp, HonorMgr.h, Player.h, Player.cpp, World.cpp
*Config keys:* PvP.PoolSizePerFaction (default 0), RpDecay (default 0.2), MaintenanceDay (default 3), AutoHonorRestart (default 1), MinHonorKills (default 0), PvP.DishonorableKills (default 1)
*Tables:* —

| Member | Kind | Source | Role |
|---|---|---|---|
| HonorMgr/GetStandingListByTeam | method | HonorMgr.cpp:21-32 | seed — HonorMgr/* |
| HonorMgr/GetStandingCPByPosition | method | HonorMgr.cpp:34-45 | seed — HonorMgr/* |
| HonorMgr/GetStandingPositionByGUID | method | HonorMgr.cpp:47-59 | seed — HonorMgr/* |
| HonorMgr/GetLastMaintenanceDay | method | HonorMgr.h:81-81 | seed — HonorMgr/* |
| HonorMgr/GetNextMaintenanceDay | method | HonorMgr.h:82-82 | seed — HonorMgr/* |
| HonorMgr/GetWeekBeginDay | method | HonorMgr.h:83-83 | seed — HonorMgr/* |
| HonorMgr/GetWeekEndDay | method | HonorMgr.h:84-84 | seed — HonorMgr/* |
| HonorMgr/DistributeRankPoints | method | HonorMgr.cpp:136-168 | seed — HonorMgr/* |
| HonorMgr/GetRank | method | HonorMgr.h:178-178 | seed — HonorMgr/* |
| HonorMgr/GetCurrentHonorRank | method | HonorMgr.h:179-179 | seed — HonorMgr/* |
| HonorMgr/GetHighestRank | method | HonorMgr.h:181-181 | seed — HonorMgr/* |
| HonorMgr/GetStanding | method | HonorMgr.h:188-188 | seed — HonorMgr/* |
| HonorMgr/GetRankPoints | method | HonorMgr.h:190-190 | seed — HonorMgr/* |
| HonorMgr/GetStoredDK | method | HonorMgr.h:192-192 | seed — HonorMgr/* |
| HonorMgr/GetStoredHK | method | HonorMgr.h:194-194 | seed — HonorMgr/* |
| HonorMgr/GetTotalDK | method | HonorMgr.h:196-196 | seed — HonorMgr/* |
| HonorMgr/GetTotalHK | method | HonorMgr.h:198-198 | seed — HonorMgr/* |
| HonorMgr/GetLastWeekCP | method | HonorMgr.h:200-200 | seed — HonorMgr/* |
| HonorMgr/GetLastWeekHK | method | HonorMgr.h:202-202 | seed — HonorMgr/* |
| HonorMgr/GetHonorCP | method | HonorMgr.h:205-205 | seed — HonorMgr/* |
| HonorMgr/FlushRankPoints | method | HonorMgr.cpp:237-267 | seed — HonorMgr/* |
| HonorMgr/DoMaintenance | method | HonorMgr.cpp:269-325 | seed — HonorMgr/* |
| HonorMgr/CreateCalculationReport | method | HonorMgr.cpp:327-465 | seed — HonorMgr/* |
| HonorMgr/GenerateScores | method | HonorMgr.cpp:467-566 | seed — HonorMgr/* |
| HonorMgr/CalculateRpEarning | method | HonorMgr.cpp:568-581 | seed — HonorMgr/* |
| HonorMgr/CalculateRpDecay | method | HonorMgr.cpp:583-597 | seed — HonorMgr/* |
| HonorMgr/CheckMaintenanceDay | method | HonorMgr.cpp:616-628 | seed — HonorMgr/* |
| HonorMgr/ClearHonorData | method | HonorMgr.cpp:666-679 | seed — HonorMgr/* |
| HonorMgr/ClearHonorCP | method | HonorMgr.cpp:695-698 | seed — HonorMgr/* |
| HonorMgr/Add | method | HonorMgr.cpp:787-836 | seed — HonorMgr/* |
| HonorMgr/CalculateTotalKills | method | HonorMgr.cpp:946-973 | seed — HonorMgr/* |
| HonorMgr/CalculateRankInfo | method | HonorMgr.cpp:975-994 | seed — HonorMgr/* |
| HonorMgr/CalculateRank | method | HonorMgr.cpp:996-1028 | seed — HonorMgr/* |
| HonorMgr/DishonorableKillPoints | method | HonorMgr.cpp:1030-1045 | seed — HonorMgr/* |
| Player.Main/GetHonorMgr | method | Player.h:2221-2221 | seed — Player.*/*Honor* |
| Player.Main/GetHonorMgr#2 | method | Player.h:2222-2222 | seed — Player.*/*Honor* |
| Player.Main/IsHonorOrXPTarget | method | Player.cpp:19988-20002 | seed — Player.*/*Honor* |
| Player.Main/RewardHonor | method | Player.cpp:21854-21890 | seed — Player.*/*Honor* |
| Player.Main/RewardHonorOnDeath | method | Player.cpp:21892-21963 | seed — Player.*/*Honor* |
| World/LoadConfigSettings | method | World.cpp:440-1245 | seed — reads config PvP.PoolSizePerFaction |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `character_honor_cp`: guid int(11) unsigned, victim_type tinyint(3) unsigned, victim_id int(11) unsigned, cp float, date int(11) unsigned, type tinyint(3) unsigned
- `characters`: guid int(11) unsigned PK, account int(11) unsigned, name varchar(12), race tinyint(3) unsigned, class tinyint(3) unsigned, gender tinyint(3) unsigned, skin tinyint(3) unsigned, face tinyint(3) unsigned, hair_style tinyint(3) unsigned, hair_color tinyint(3) unsigned, facial_hair tinyint(3) unsigned, level tinyint(3) unsigned, xp int(10) unsigned, money int(10) unsigned, character_flags int(10) unsigned, zone int(11) unsigned, map int(11) unsigned, instance int(11) unsigned, position_x float, position_y float, position_z float, orientation float, transport_guid bigint(20) unsigned, transport_x float, transport_y float, transport_z float, transport_o float, known_taxi_mask longtext?, current_taxi_path text?, online tinyint(3) unsigned, played_time_total int(11) unsigned, played_time_level int(11) unsigned, create_time bigint(20) unsigned, logout_time bigint(20) unsigned, rest_bonus float, reset_talents_multiplier int(11) unsigned, reset_talents_time bigint(20) unsigned, death_expire_time bigint(20) unsigned, stable_slots tinyint(1) unsigned, bank_bag_slots tinyint(1) unsigned, extra_flags int(11) unsigned, honor_rank_points float, honor_highest_rank int(11) unsigned, honor_standing int(11) unsigned, honor_last_week_hk int(11) unsigned, honor_last_week_cp float, honor_stored_hk int(11), honor_stored_dk int(11), watched_faction int(11), drunk smallint(5) unsigned, health int(10) unsigned, power1 int(10) unsigned, power2 int(10) unsigned, power3 int(10) unsigned, power4 int(10) unsigned, power5 int(10) unsigned, explored_zones longtext?, equipment_cache longtext?, ammo_id int(10) unsigned, action_bars tinyint(3) unsigned, deleted_account int(11) unsigned?, deleted_name varchar(12)?, deleted_time bigint(20)?, world_phase_mask int(11)?

*`?` = nullable, `PK` = primary key column.*

