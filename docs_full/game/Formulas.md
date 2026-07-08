# Formulas

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`Formulas.h` provides the mathematical logic for calculating experience points (XP) and honor gains in the MaNGOS emulator. It defines two namespaces: `MaNGOS::XP` for PvE experience calculations and `MaNGOS::Honor` for PvP honor calculations. The unit is stateless, containing only inline functions that take parameters (levels, ranks, flags) and return numerical results or classification enums. It does not access databases or manage game state; it serves as a pure calculation layer for `Player`, `Group`, and `Battleground` units.

## Member-by-Member Behavior

### Experience Point Calculations (`MaNGOS::XP`)

**Level Thresholds and Classification**
*   **`GetGrayLevel`**: Computes the minimum creature level below which a player gains no XP ("gray"). The formula varies by player level: 0 for levels ≤5, `level - 5 - level/10` for levels 6–39, and `level - 1 - level/5` for levels ≥40. Used by `game_Group_Group` and `Player.Main` to determine XP eligibility.
*   **`GetColorCode`**: Returns an `XPColorChar` enum (`RED`, `ORANGE`, `YELLOW`, `GREEN`, `GRAY`) based on the level difference between player and monster. Red is ≥+5 levels, Orange ≥+3, Yellow ≥-2, Green above gray level, and Gray otherwise. Currently unused by other units in the map.
*   **`GetZeroDifference`**: Returns a stepwise constant used in XP normalization for lower-level targets. Values range from 5 (level <8) to 17 (level ≥60). Used internally by `BaseGainLevelFactor`.

**Base XP Computation**
*   **`BaseGainLevelFactor`**: Calculates a multiplier based on the level difference between killer and victim. If the victim is higher level, it adds 5% per level up to a cap of 4 levels. If lower, it scales linearly based on `GetZeroDifference`. Returns 0 if the victim is below the gray level.
*   **`BaseGain`**: Computes raw XP as `(ownerLevel * 5 + 45) * BaseGainLevelFactor`. Distinct `ownerLevel` and `unitLevel` parameters allow for pet mechanics where the pet’s level differs from the owner’s.

**Final XP Determination**
*   **`Gain`**: The primary XP calculation entry point. It first checks eligibility: returns 0 if the creature is spell-created (with exceptions for critters/totems), has `NO_KILL_REWARD` state, or `NO_XP` static flag. For pets, it adjusts the level basis depending on the server’s emulated WoW patch (<1.7.0 uses owner level, ≥1.7.0 uses pet level). It then applies modifiers: elite bonuses (2.5x in non-raid dungeons, 2x elsewhere), pet penalty (0.75x), creature-specific multipliers, and server configuration rates (`CONFIG_FLOAT_RATE_XP_KILL_ELITE`, `CONFIG_FLOAT_RATE_XP_KILL`). The result is rounded to the nearest integer.
*   **`xp_in_group_rate`**: Returns a multiplier for group XP based on member count. Fixed bonuses apply for 3–5 members (1.166x, 1.3x, 1.4x). For groups >5, the rate decreases by 5% per member, capped at 0.01x. The code notes this formula is speculative.

### Honor Calculations (`MaNGOS::Honor`)

*   **`GetHonorGain`**: Calculates honor points for a PvP kill. It applies:
    1.  **Level Penalty**: Via `XP::BaseGainLevelFactor`.
    2.  **Diminishing Returns**: A penalty based on `totalKills` against the same victim. The threshold for zero honor is 4 kills pre-patch 1.12, and 10 kills post-1.12 (unless `ACCURATE_PVP_REWARDS` is disabled).
    3.  **Level Coefficient**: A fixed multiplier based on the killer’s level bracket (e.g., 1.0 for level 60, 0.1212 for <20).
    4.  **Victim Rank**: An exponential factor `exp(0.05331 * victimRank)` scaled by a base factor (188.3, or 157.4 pre-patch 1.8 if accurate rewards are enabled).
    5.  **Group Splitting**: Divides the result by `groupSize`.

## Cross-Unit Boundaries

*   **`game_Group_Group`**: Calls `GetGrayLevel` to check XP eligibility for group members, `Gain` to calculate individual XP shares, and `xp_in_group_rate` to determine the group size multiplier.
*   **`Player.Main`**: Calls `GetGrayLevel` to verify XP eligibility for solo players, `Gain` to award XP, and `xp_in_group_rate` in `RewardHonorOnDeath` to adjust honor gains. It also uses `GetGrayLevel` in `IsHonorOrXPTarget`, `UpdateCombatSkills`, and `CalculateReputationGain` to ensure rewards align with XP rules.
*   **`game_Battlegrounds_BattleGround`**: Calls `GetHonorGain` to compute bonus honor for battleground kills.
*   **`HonorMgr`**: Calls `GetHonorGain` to process standard PvP honorable kill points.

## Data Model

This unit does not interact with any database tables. All calculations are performed in memory using passed parameters and server configuration values.

## Notable Implementation Details

1.  **Patch-Specific Logic**: `Gain` and `GetHonorGain` branch on `sWorld.GetWowPatch()` to emulate different WoW versions (e.g., 1.7.0, 1.8, 1.12). Maintainers must preserve these branches when updating formulas.
2.  **Pet XP Mechanics**: `Gain` implements the patch 1.7.0 change where pets gain XP based on their own level rather than the owner’s. Pre-1.7.0 logic uses the owner’s level.
3.  **Speculative Group Rates**: `xp_in_group_rate` contains a comment stating the formula for groups >5 is "guesswork." This may require adjustment if accurate historical data becomes available.
4.  **Configuration Dependencies**: Outputs depend on server configs: `CONFIG_FLOAT_RATE_XP_KILL_ELITE`, `CONFIG_FLOAT_RATE_XP_KILL`, and `CONFIG_BOOL_ACCURATE_PVP_REWARDS`.
5.  **Elite Dungeon Bonus**: Elite creatures in non-raid dungeons receive a 2.5x XP multiplier; outside dungeons, 2x. This is hardcoded before applying the elite rate config.

## Member Reference

*   **`GetGrayLevel`**: Calculates the minimum creature level below which a player gains no XP, using tiered formulas for levels 1–5, 6–39, and 40+.
*   **`GetColorCode`**: Returns an enum (`RED`, `ORANGE`, `YELLOW`, `GREEN`, `GRAY`) indicating the XP color category based on player vs. monster level differences.
*   **`GetZeroDifference`**: Returns a stepwise increasing integer constant based on player level, used to normalize XP gains for lower-level targets.
*   **`BaseGainLevelFactor`**: Computes a multiplier for XP based on the level difference between killer and victim, capping high-level bonuses and scaling low-level penalties.
*   **`BaseGain`**: Calculates the raw base XP amount using the formula `(owner_level * 5 + 45) * BaseGainLevelFactor`.
*   **`Gain`**: The main XP calculation function, applying eligibility checks, pet-specific logic, elite bonuses, and server rates to determine final XP awarded.
*   **`xp_in_group_rate`**: Returns a multiplier for group XP based on group size, with fixed bonuses for 3–5 members and a diminishing return for larger groups.
*   **`GetHonorGain`**: Calculates honor points for a PvP kill, incorporating level penalties, diminishing returns for repeated kills, level coefficients, victim rank, and group splitting.

---

<!-- machine-true, projected from graph.json -->

## Map — Formulas

*Source:* Formulas.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GetGrayLevel | function | — | game_Group_Group/GetDataForXPAtKill_helper, Player.Main/CalculateReputationGain, Player.Main/IsHonorOrXPTarget, Player.Main/UpdateCombatSkills | — |
| GetColorCode | function | — | — | — |
| GetZeroDifference | function | — | — | — |
| BaseGainLevelFactor | function | — | — | — |
| BaseGain | function | — | — | — |
| Gain | function | — | game_Group_Group/RewardGroupAtKill, Player.Main/RewardSinglePlayerAtKill | — |
| xp_in_group_rate | function | — | game_Group_Group/RewardGroupAtKill, Player.Main/RewardHonorOnDeath | — |
| GetHonorGain | function | — | game_Battlegrounds_BattleGround/GetBonusHonorFromKill, HonorMgr/HonorableKillPoints | — |
