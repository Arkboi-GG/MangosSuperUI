# Weather

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Weather

The `Weather` unit implements the server-side simulation, management, and client notification of dynamic weather conditions (rain, snow, sandstorms) within specific game zones. It operates as part of the MaNGOS server framework for World of Warcraft, handling the probabilistic generation of weather states based on seasonal data loaded from the database, maintaining timers for periodic updates, and broadcasting state changes to connected players via network packets.

The system is structured around three primary classes:
1.  **`Weather`**: Represents the weather state for a single zone. It handles the internal logic for determining current weather type and intensity ("grade"), managing the update timer, and sending updates to clients.
2.  **`WeatherSystem`**: Acts as a container for `Weather` objects within a specific `Map`. It manages the lifecycle of zone-specific weather instances, creating them on demand and cleaning them up when no longer needed.
3.  **`WeatherMgr`**: A singleton manager responsible for loading weather probability data from the `game_weather` database table and providing access to these chances for zone initialization.

## Purpose & Responsibilities

The core responsibility of this unit is to simulate realistic, changing weather patterns that affect the visual and auditory experience of players in specific zones. Key responsibilities include:

*   **Data Loading**: Reading zone-specific weather probabilities for different seasons (spring, summer, fall, winter) from the `game_weather` table.
*   **State Simulation**: Periodically recalculating weather type and intensity based on random chance, current season, and previous state, adhering to defined statistical distributions (e.g., likelihood of improvement vs. worsening).
*   **Client Communication**: Constructing and sending `SMSG_WEATHER` packets to players in affected zones, including visual state, intensity grade, and associated sound effects.
*   **Lifecycle Management**: Creating weather instances for zones as players enter them and destroying them when zones become empty, ensuring efficient memory usage.
*   **Administrative Control**: Providing interfaces for server administrators to manually set or change weather conditions via chat commands.

## Member-by-Member Behavior

### Weather Class

The `Weather` class encapsulates the state and behavior of weather for a single zone ID.

#### Initialization and Lifecycle

**`Weather` (Constructor)**
Initializes a `Weather` instance for a specific zone. It sets the initial weather type to `WEATHER_TYPE_FINE` with a grade of `0.0f`. It configures an internal `ShortIntervalTimer` using the interval value retrieved from the world configuration (`CONFIG_UINT32_INTERVAL_CHANGEWEATHER`). If logging is enabled for the weather filter, it logs the initialization of the weather system for the zone, including the calculated change interval in minutes.

**`~Weather` (Destructor)**
A trivial destructor that performs no cleanup operations, as the class holds no heap-allocated resources other than raw pointers to external data structures managed elsewhere.

#### State Updates and Generation

**`Update`**
This method is called periodically by the `WeatherSystem` to advance the weather simulation. It first updates the internal timer with the elapsed time difference (`diff`). If the timer has passed, it resets the timer and attempts to regenerate the weather state by calling `ReGenerate`. If `ReGenerate` indicates that the weather state has changed, it triggers `SendWeatherForPlayersInZone` to broadcast the new state. If sending the update fails (indicating no players are in the zone), it returns `false`, signaling to the caller that the weather instance can be removed. Otherwise, it returns `true`.

**`ReGenerate`**
Calculates the new weather state based on probabilistic rules. If the weather is marked as permanent (`m_isPermanentWeather`), it returns `false` immediately, preventing any change. If no weather chances are defined for the zone, it defaults to fine weather.

For zones with defined chances, it uses a random number (`urand(0, 99)`) to determine the outcome:
*   **30% Chance (No Change)**: Returns `false` if the random number is less than 30.
*   **Seasonal Determination**: Calculates the current season (spring, summer, fall, winter) based on the game time (`sWorld.GetGameTime()`), assuming 91-day seasons starting from March 20th.
*   **Improvement/Worsening Logic**:
    *   If the random number is less than 60 and the current grade is low (< 0.33333334f), it sets weather to fine.
    *   If less than 60 and not fine, it reduces the grade by ~0.33 (improvement).
    *   If less than 90 and not fine, it increases the grade by ~0.33 (worsening).
*   **Radical Change**: If the weather is not fine and the random number is high, it may drastically change the grade or type. For example, light weather might jump to heavy, or heavy weather might clear up or change type.
*   **New Weather Selection**: If the weather becomes fine or needs a new type, it selects rain, snow, or storm based on the cumulative chances defined in `m_weatherChances` for the current season.
*   **Grade Assignment**: Assigns a new grade based on normal distribution (`rand_norm_f`) scaled to light, medium, or heavy intensities.
*   Finally, it calls `NormalizeGrade` to ensure the grade stays within valid bounds and returns `true` if the type or grade differs from the previous state.

**`NormalizeGrade`**
Ensures the weather grade (`m_grade`) remains within the range `[0.0001f, 0.9999f]`. If the grade exceeds 1.0, it is capped at `0.9999f`; if it drops below 0.0, it is set to `0.0001f`. This prevents invalid states that could cause issues in client rendering or sound selection.

#### Client Communication

**`SendWeatherUpdateToPlayer`**
Constructs and sends an `SMSG_WEATHER` packet to a specific `Player`. It normalizes the grade first. The packet structure varies by client build:
*   Base: Weather type (uint32) and grade (float).
*   Build > 1.8.4: Includes a sound ID (uint32) obtained from `GetSound`.
*   Build > 1.9.4: Includes a change mode byte (uint8), always set to 0 for smooth transitions.
The packet is sent via the player's session.

**`SendWeatherForPlayersInZone`**
Broadcasts the current weather state to all players in the specified `Map` within the zone `m_zone`. It constructs the same `SMSG_WEATHER` packet as `SendWeatherUpdateToPlayer` and uses `_map->SendToPlayersInZone` to distribute it. If no players are in the zone, it returns `false`. After sending, it logs the current weather state using `LogWeatherState`.

**`GetSound`**
Determines the appropriate sound effect ID based on the current weather type and grade. It maps grades to specific sound constants defined in the `WeatherSounds` enum:
*   Grades < 0.3: No sound.
*   Grades 0.3–0.6: Light sound.
*   Grades 0.6–0.9: Medium sound.
*   Grades ≥ 0.9: Heavy sound.
Different sound IDs are used for rain, snow, and sandstorms.

**`GetWeatherState`**
Converts the internal `m_type` and `m_grade` into a `WeatherState` enum value suitable for logging or external queries. It uses thresholds (0.27, 0.40, 0.70) to distinguish between fine, light, medium, and heavy states for each weather type.

**`LogWeatherState`**
Logs the current weather state in human-readable form (e.g., "light rain") if the weather debug filter is active. It uses a switch statement to map `WeatherState` enums to string literals.

#### Administrative and Utility

**`IsValidWeatherType`**
A static utility method that checks if a given integer corresponds to a valid `WeatherType` enum value (`FINE`, `RAIN`, `SNOW`, `STORM`). It is called by `ChatHandler.ServerCommands/HandleChangeWeatherCommand` to validate user input before applying changes.

**`SetWeather`**
Manually sets the weather type and grade for the zone. If the new state matches the current state, it returns early. Otherwise, it updates `m_type` and `m_grade`, sets the `m_isPermanentWeather` flag, and broadcasts the change to all players in the zone via `SendWeatherForPlayersInZone`. This method is called by `Map.Main/SetWeather`.

### WeatherSystem Class

The `WeatherSystem` class manages multiple `Weather` instances for a single `Map`.

**`WeatherSystem` (Constructor)**
Initializes the system with a pointer to the owning `Map`. It does not pre-create any weather instances.

**`~WeatherSystem` (Destructor)**
Iterates through the `m_weathers` map and deletes all `Weather` objects to prevent memory leaks. It then clears the map.

**`FindOrCreateWeather`**
Retrieves an existing `Weather` instance for a given `zoneId` from the `m_weathers` map. If none exists, it creates a new `Weather` object, initializing it with the zone ID and weather chances retrieved from `WeatherMgr/GetWeatherChances`. The new instance is stored in the map and returned. This method is called by `Map.Main/SetWeather` and `Player.Main/UpdateZone` to ensure weather is active for zones players enter.

**`UpdateWeathers`**
Iterates through all active `Weather` instances in the map. For each, it calls `Update` with the time difference. If `Update` returns `false` (indicating no players are in the zone), the `Weather` object is deleted and removed from the map. This ensures that weather simulation resources are only consumed for zones with active players. This method is called by `Map.Main/Update#3`.

### WeatherMgr Class

The `WeatherMgr` singleton manages global weather data.

**`LoadWeatherZoneChances`**
Loads weather probability data from the `game_weather` database table. It executes a `SELECT` query retrieving zone IDs and chance values for rain, snow, and storms across four seasons. For each row, it populates a `WeatherZoneChances` structure in the `mWeatherZoneMap`. It validates that chance values do not exceed 100%, resetting them to 25% and logging an error if they do. Progress is displayed using a `BarGoLink`. This method is called by `World/SetInitialWorldSettings` and `ChatHandler.ServerCommands/HandleReloadGameWeather`.

**`GetWeatherChances`**
Returns a pointer to the `WeatherZoneChances` structure for a given zone ID, or `nullptr` if no data exists for that zone.

## Cross-Unit Boundaries

*   **`Weather` ↔ `ShortIntervalTimer`**: The `Weather` class uses `ShortIntervalTimer` to manage the periodic regeneration of weather. It calls `GetInterval`, `SetInterval`, `Passed`, `Reset`, and `Update` to handle timing logic.
*   **`Weather` ↔ `World`**: `Weather` retrieves the configured weather change interval via `World/getConfig#4` and obtains the current game time via `World/GetGameTime` for seasonal calculations.
*   **`Weather` ↔ `Log.Main`**: Various methods in `Weather` call logging functions (`HasLogFilter`, `HasLogLevelOrHigher`, `Out`) to output debug and error messages related to weather state changes and initialization.
*   **`Weather` ↔ `shared_Util`**: `ReGenerate` uses `urand` for random number generation and `rand_norm_f` for generating normally distributed grades.
*   **`Weather` ↔ `Player.Main`**: `SendWeatherUpdateToPlayer` is called by `Player.Main/UpdateZone` to send weather updates to individual players as they move between zones.
*   **`Weather` ↔ `WorldSession.Main`**: `SendWeatherUpdateToPlayer` sends packets via `WorldSession.Main/SendPacket`.
*   **`Weather` ↔ `ByteBuffer` & `WorldPacket`**: Both `SendWeatherUpdateToPlayer` and `SendWeatherForPlayersInZone` construct `WorldPacket` objects and use `ByteBuffer` operators to serialize weather data (type, grade, sound ID, change mode) into the packet.
*   **`Weather` ↔ `Map.Main`**: `SendWeatherForPlayersInZone` calls `Map.Main/SendToPlayersInZone` to broadcast packets. `SetWeather` is called by `Map.Main/SetWeather`.
*   **`WeatherSystem` ↔ `WeatherMgr`**: `FindOrCreateWeather` calls `WeatherMgr/GetWeatherChances` to retrieve probability data for new weather instances.
*   **`WeatherSystem` ↔ `Map.Main`**: `WeatherSystem` is instantiated by `Map.Main/Map`. `UpdateWeathers` is called by `Map.Main/Update#3`. `FindOrCreateWeather` is called by `Map.Main/SetWeather`.
*   **`WeatherMgr` ↔ `Database`**: `LoadWeatherZoneChances` executes a query on the `WorldDatabase` to fetch weather data.
*   **`WeatherMgr` ↔ `QueryResult` & `Field`**: Used to iterate over and extract data from the database query results.
*   **`WeatherMgr` ↔ `ProgressBar`**: Uses `BarGoLink` to display loading progress during data initialization.
*   **`WeatherMgr` ↔ `World`**: `LoadWeatherZoneChances` is called by `World/SetInitialWorldSettings`.
*   **`WeatherMgr` ↔ `ChatHandler.ServerCommands`**: `LoadWeatherZoneChances` is called by `HandleReloadGameWeather` to allow runtime reloading of weather data.

## Data Model

The `WeatherMgr` interacts with the `game_weather` table to load zone-specific weather probabilities.

**Table: `game_weather`**
*   **Purpose**: Stores the base probabilities for rain, snow, and storm occurrences for each zone, broken down by season.
*   **Columns**:
    *   `zone` (mediumint unsigned, PK): The unique identifier for the game zone.
    *   `spring_rain_chance`, `spring_snow_chance`, `spring_storm_chance` (tinyint unsigned): Probabilities for spring.
    *   `summer_rain_chance`, `summer_snow_chance`, `summer_storm_chance` (tinyint unsigned): Probabilities for summer.
    *   `fall_rain_chance`, `fall_snow_chance`, `fall_storm_chance` (tinyint unsigned): Probabilities for fall.
    *   `winter_rain_chance`, `winter_snow_chance`, `winter_storm_chance` (tinyint unsigned): Probabilities for winter.

The code reads these values into `WeatherZoneChances` structures, which are indexed by zone ID in the `WeatherMgr`'s internal map. These chances are used during the `ReGenerate` process to determine the likelihood of specific weather types occurring in a given season.

## Notable Implementation Details

*   **Season Calculation**: The season is determined by calculating the day of the year (`tm_yday`) relative to March 20th (day 78), dividing by 91 (approximate days per season), and taking modulo 4. This assumes a fixed 91-day season length, which is an approximation of real-world seasons.
*   **Grade Normalization**: The `NormalizeGrade` function clamps the grade to `[0.0001f, 0.9999f]`. This avoids exact 0.0 or 1.0 values, possibly to prevent edge-case issues in client interpolation or sound triggering.
*   **Permanent Weather Flag**: The `m_isPermanentWeather` flag, set via `SetWeather`, bypasses the `ReGenerate` logic entirely. This allows administrators to lock weather states for events or testing.
*   **Sound Mapping**: Sound effects are only included in packets for client builds greater than 1.8.4. The sound selection is based on discrete grade thresholds (0.3, 0.6, 0.9), distinct from the visual state thresholds used in `GetWeatherState`.
*   **Memory Management**: `WeatherSystem` takes ownership of `Weather` objects created via `new` and is responsible for deleting them in its destructor or when `Update` indicates the zone is empty. This manual memory management requires careful iteration to avoid dangling pointers.
*   **Validation in Loading**: `LoadWeatherZoneChances` explicitly checks if chance values exceed 100%. If so, it resets them to 25% and logs an error. This defensive coding prevents invalid probabilities from breaking the random selection logic.
*   **Client Build Conditionals**: The packet construction in `SendWeatherUpdateToPlayer` and `SendWeatherForPlayersInZone` uses preprocessor directives (`SUPPORTED_CLIENT_BUILD`) to conditionally include sound IDs and change mode bytes, ensuring compatibility across different World of Warcraft client versions supported by MaNGOS.

## Member Reference

**`Weather`** (ctor): Initializes the weather instance for a zone, setting up the timer with the configured interval and logging initialization details.

**`~Weather`** (dtor): Trivial destructor with no cleanup actions.

**`Update`** (method): Advances the weather timer; if expired, regenerates weather and broadcasts changes if the state altered, returning false if no players are present.

**`IsValidWeatherType`** (method): Static helper that validates if a given integer represents a valid weather type enum value.

**`ReGenerate`** (method): Core logic for probabilistically determining new weather type and grade based on season, random chance, and previous state, respecting permanent weather flags.

**`SendWeatherUpdateToPlayer`** (method): Constructs and sends an `SMSG_WEATHER` packet to a single player, including type, grade, and optional sound/change-mode data based on client build.

**`SendWeatherForPlayersInZone`** (method): Broadcasts the current weather state to all players in the zone via the map, logging the state and returning false if no players are present.

**`SetWeather`** (method): Manually sets the weather type and grade, marking it as permanent if specified, and broadcasts the change to the zone.

**`GetWeatherState`** (method): Converts internal type and grade into a `WeatherState` enum for logging or external use, using specific grade thresholds.

**`NormalizeGrade`** (method): Clamps the weather grade to the range [0.0001f, 0.9999f] to prevent invalid extremes.

**`LogWeatherState`** (method): Logs the current weather state in human-readable format if the weather debug filter is active.

**`WeatherSystem`** (ctor): Initializes the weather system for a map, storing a pointer to the map.

**`~WeatherSystem`** (dtor): Deletes all managed `Weather` objects and clears the internal map.

**`FindOrCreateWeather`** (method): Retrieves an existing weather instance for a zone or creates a new one using chances from `WeatherMgr`, storing it in the map.

**`UpdateWeathers`** (method): Iterates through all weather instances, updating them and removing those for zones with no players.

**`GetSound`** (method): Determines the appropriate sound effect ID based on weather type and grade, mapping to specific sound constants.

**`LoadWeatherZoneChances`** (method): Loads weather probability data from the `game_weather` table, validating chances and populating the internal map, with progress reporting.

---

<!-- machine-true, projected from graph.json -->

## Map — Weather

*Source:* Weather.cpp, Weather.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| Weather | ctor | Log.Main/HasLogFilter, Log.Main/HasLogLevelOrHigher, Log.Main/Out, ShortIntervalTimer/GetInterval, ShortIntervalTimer/SetInterval, World/getConfig#4 | — | — |
| ~Weather | dtor | — | — | — |
| Update | method | ShortIntervalTimer/Passed, ShortIntervalTimer/Reset, ShortIntervalTimer/Update | — | — |
| IsValidWeatherType | method | — | ChatHandler.ServerCommands/HandleChangeWeatherCommand | — |
| ReGenerate | method | Log.Main/HasLogFilter, Log.Main/HasLogLevelOrHigher, Log.Main/Out, shared_Util/rand_norm_f, shared_Util/urand, World/GetGameTime | — | — |
| SendWeatherUpdateToPlayer | method | ByteBuffer/operator<<#10, ByteBuffer/operator<<#7, ByteBuffer/operator<<#9, Player.Main/GetSession, WorldPacket/WorldPacket#4, WorldSession.Main/SendPacket | Player.Main/UpdateZone | — |
| SendWeatherForPlayersInZone | method | ByteBuffer/operator<<#10, ByteBuffer/operator<<#7, ByteBuffer/operator<<#9, Map.Main/SendToPlayersInZone, WorldPacket/WorldPacket#4 | — | — |
| SetWeather | method | — | Map.Main/SetWeather | — |
| GetWeatherState | method | — | — | — |
| NormalizeGrade | method | — | — | — |
| LogWeatherState | method | Log.Main/HasLogFilter, Log.Main/HasLogLevelOrHigher, Log.Main/Out | — | — |
| WeatherSystem | ctor | — | Map.Main/Map | — |
| ~WeatherSystem | dtor | — | — | — |
| FindOrCreateWeather | method | WeatherMgr/GetWeatherChances | Map.Main/SetWeather, Player.Main/UpdateZone | — |
| UpdateWeathers | method | — | Map.Main/Update#3 | — |
| GetSound | method | — | — | — |
| LoadWeatherZoneChances | method | Database/Query, Field/GetUInt32, Log.Main/Out, ProgressBar/BarGoLink, ProgressBar/BarGoLink#3, ProgressBar/step, QueryResult/Fetch, QueryResult/GetRowCount, QueryResult/NextRow | ChatHandler.ServerCommands/HandleReloadGameWeather, World/SetInitialWorldSettings | game_weather |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `game_weather`: zone mediumint(8) unsigned PK, spring_rain_chance tinyint(3) unsigned, spring_snow_chance tinyint(3) unsigned, spring_storm_chance tinyint(3) unsigned, summer_rain_chance tinyint(3) unsigned, summer_snow_chance tinyint(3) unsigned, summer_storm_chance tinyint(3) unsigned, fall_rain_chance tinyint(3) unsigned, fall_snow_chance tinyint(3) unsigned, fall_storm_chance tinyint(3) unsigned, winter_rain_chance tinyint(3) unsigned, winter_snow_chance tinyint(3) unsigned, winter_storm_chance tinyint(3) unsigned

*`?` = nullable, `PK` = primary key column.*

