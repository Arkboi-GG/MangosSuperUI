# WeeklyScore

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

The `WeeklyScore` struct, defined in `HonorMgr.h`, serves as a lightweight data container within the **Honor Management** subsystem of the WoWVMaNGOS server. It represents the aggregated honor statistics for a single player account over a specific weekly maintenance cycle.

Its primary responsibility is to hold the raw numerical data required to calculate a player's final Rank Points (RP) adjustment at the end of the week. This includes tracking the player's current combat points (CP), their previous and newly calculated rank points, the net earning or decay of those points, and their standing within the faction's honor leaderboard. The struct is designed to be transient in memory, populated during the weekly maintenance routine and used to update persistent storage or client-facing data, but it does not contain logic itself—only state.

## Member-by-Member Behavior

The `WeeklyScore` struct contains only a default constructor and a set of public member variables. There are no methods beyond initialization.

### Initialization
*   **`WeeklyScore()`**: The default constructor initializes all member variables to zero or neutral states. This ensures that any instance created via value-initialization (e.g., `WeeklyScore score{};`) starts with a clean slate, preventing garbage values from affecting subsequent calculations in the `HonorMaintenancer` system.

### Data Fields
The fields track the following aspects of a player's weekly honor performance:
*   **`level` (`uint8`)**: The character's level at the time of calculation. This is critical because honor point values and maximum rank point caps are level-dependent.
*   **`account` (`uint32`)**: The unique identifier for the player's account. This links the score to the correct user in the database.
*   **`hk` (`uint32`)**: The number of Honorable Kills achieved during the week.
*   **`dk` (`uint32`)**: The number of Dishonorable Kills suffered during the week.
*   **`cp` (`float`)**: The total Combat Points earned during the week. CP is the intermediate currency that determines a player's position in the weekly standings.
*   **`oldRp` (`float`)**: The player's Rank Points at the beginning of the week. This serves as the baseline for calculating gains or losses.
*   **`newRp` (`float`)**: The player's calculated Rank Points after applying the weekly formula (based on CP, standing, and decay).
*   **`earning` (`float`)**: The net change in Rank Points (`newRp` - `oldRp`). A positive value indicates a gain; a negative value indicates decay.
*   **`standing` (`uint32`)**: The player's rank position within their faction's honor leaderboard (e.g., 1st, 2nd, 100th). This position directly influences the multiplier applied to their CP to determine RP earnings.
*   **`highestRank` (`uint8`)**: The highest honor rank title the player has ever achieved. This is often used for cosmetic purposes or historical tracking.

## Cross-Unit Boundaries

The `WeeklyScore` struct is tightly coupled with the **`HonorMaintenancer`** class, which resides in the same header file but represents a distinct logical unit responsible for the weekly honor reset process.

*   **Called by `HonorMgr/LoadWeeklyScores`**: According to the MAP, the `WeeklyScore` constructor is invoked by `HonorMgr::LoadWeeklyScores`. However, examining the source code reveals that `HonorMgr` does not directly instantiate `WeeklyScore`. Instead, `HonorMaintenancer::LoadWeeklyScores` (a method in the `HonorMaintenancer` class) is likely the actual caller, or the MAP label `HonorMgr/LoadWeeklyScores` refers to the broader subsystem interaction where `HonorMaintenancer` loads data into its internal `m_weeklyScores` hash map. The `HonorMaintenancer` populates a `std::unordered_map<uint32, WeeklyScore>` (named `m_weeklyScores`) with these structs. Each entry in this map corresponds to a player's account ID, allowing the system to look up a player's weekly stats efficiently during the maintenance phase.

There are no outgoing calls from `WeeklyScore` to other units, as it is a passive data structure.

## Data Model

The `WeeklyScore` struct itself does not interact directly with the database. It is an in-memory representation of data that is either loaded from or saved to the database by the `HonorMaintenancer` class.

Based on the fields in `WeeklyScore`, the underlying database table (likely `character_honor` or a similar temporary/historical table used during maintenance) would contain columns corresponding to:
*   `account_id` (linked to `account`)
*   `level`
*   `honorable_kills` (linked to `hk`)
*   `dishonorable_kills` (linked to `dk`)
*   `combat_points` (linked to `cp`)
*   `rank_points_old` (linked to `oldRp`)
*   `rank_points_new` (linked to `newRp`)
*   `standing`
*   `highest_rank` (linked to `highestRank`)

Since no SQL queries are present in the provided source code for `WeeklyScore`, and no SCHEMA section is provided, we cannot definitively state the exact table names or column types. The struct acts as a bridge between the database rows and the C++ calculation logic in `HonorMaintenancer`.

## Notable Implementation Details

1.  **Passive Data Structure**: `WeeklyScore` contains no methods other than the constructor. All logic related to calculating `newRp`, `earning`, or determining `standing` resides in the `HonorMaintenancer` class. This separation of concerns keeps the data structure simple and easy to serialize/deserialize.
2.  **Floating Point Precision**: The use of `float` for `cp`, `oldRp`, `newRp`, and `earning` suggests that honor calculations involve fractional values. Maintainers should be aware that floating-point precision issues could theoretically affect very small differences in RP calculations, though this is unlikely to impact gameplay significantly given the scale of honor points.
3.  **Zero-Initialization**: The constructor explicitly initializes all members. This is crucial because `WeeklyScore` instances are often created in bulk within the `m_weeklyScores` hash map. Without explicit initialization, default constructors for primitive types would leave them uninitialized, leading to undefined behavior in subsequent arithmetic operations.
4.  **Account-Centric Keying**: The `account` field is used as the key in the `WeeklyScoresHash` (`std::unordered_map<uint32, WeeklyScore>`). This implies that the weekly honor calculation is performed per account, potentially aggregating data across multiple characters if the game logic supports it, or simply using the account ID as a unique identifier for the primary character's honor status. Given the `level` field is present, it likely tracks the specific character's level at the time of the snapshot.

## Member Reference

**WeeklyScore**
The default constructor for the `WeeklyScore` struct. It initializes all member variables (`level`, `account`, `hk`, `dk`, `cp`, `oldRp`, `newRp`, `earning`, `standing`, `highestRank`) to zero or their default neutral values. This ensures that any newly instantiated `WeeklyScore` object starts with a clean state, preventing uninitialized memory errors during the weekly honor maintenance calculations performed by `HonorMaintenancer`.

---

<!-- machine-true, projected from graph.json -->

## Map — WeeklyScore

*Source:* HonorMgr.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| WeeklyScore | ctor | — | HonorMgr/LoadWeeklyScores | — |
