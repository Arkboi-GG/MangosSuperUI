# WeatherMgr

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# WeatherMgr

## Purpose & Responsibilities

`WeatherMgr` is the global singleton responsible for caching static weather configuration data. It stores `WeatherZoneChances` structures—defining rain, snow, and storm probabilities per season—for specific zone IDs. It serves as the lookup source for the dynamic weather system, allowing `Weather` instances to retrieve baseline probabilities without querying the database at runtime.

This unit does not manage active weather states, timers, or client updates; those responsibilities belong to `Weather` and `WeatherSystem` (defined in the same header but implemented elsewhere).

## Member-by-Member Behavior

### Construction and Destruction
*   **`WeatherMgr()`**: Empty constructor. Relies on default initialization of `mWeatherZoneMap`. Invoked once during server startup via the `sWeatherMgr` singleton macro.
*   **`~WeatherMgr()`**: Empty destructor. Cleanup of `mWeatherZoneMap` is handled automatically by the standard library container.

### Configuration Access
*   **`GetWeatherChances(uint32 zone_id)`**: Retrieves weather probability configuration for a specific zone ID.
    *   Searches `mWeatherZoneMap` (an `std::unordered_map`) for `zone_id`.
    *   Returns a `const WeatherZoneChances*` if found, or `nullptr` if the zone has no explicit configuration.
    *   Marked `const` to ensure immutability of the manager's state during lookup.

### Data Loading (Declared Elsewhere)
*   **`LoadWeatherZoneChances()`**: Declared in this header but implemented in another unit. Responsible for populating `mWeatherZoneMap` from the database.

## Cross-Unit Boundaries

### Called By: `Weather/FindOrCreateWeather`
The `WeatherSystem::FindOrCreateWeather` method (in `WeatherSystem`) calls `WeatherMgr::GetWeatherChances`.

*   **Direction**: `WeatherSystem` -> `WeatherMgr`
*   **Data Crossing Boundary**: A `uint32` zone ID is passed to `WeatherMgr`; a `const WeatherZoneChances*` is returned.
*   **Reasoning**: When a `Weather` object is created for a zone, it requires the baseline seasonal probabilities to drive its randomization logic (`ReGenerate`). It queries `WeatherMgr` to obtain this static configuration, which is then stored in the `Weather` instance (`m_weatherChances`).

### Calls Out: None
`WeatherMgr` does not call into other units in this MAP. It depends only on standard library containers and its internal data.

## Data Model

`WeatherMgr` caches data logically derived from a database table (likely `weather_zone_chances`). The structure `WeatherZoneChances` contains:
*   **Zone ID**: Key in `mWeatherZoneMap` (`uint32`).
*   **Seasonal Chances**: An array of `WEATHER_SEASONS` (4) entries, each containing `rainChance`, `snowChance`, and `stormChance` (`uint32`).

No SQL queries are executed in this unit. Data is loaded by `LoadWeatherZoneChances()` (implemented elsewhere) and served statically by `GetWeatherChances`.

## Notable Implementation Details

1.  **Singleton Pattern**: Exposed globally via `#define sWeatherMgr MaNGOS::Singleton<WeatherMgr>::Instance()`, ensuring a single source of truth for weather configuration.
2.  **Null Pointer Semantics**: `GetWeatherChances` returns `nullptr` for zones without explicit configuration. Callers (e.g., `Weather`) must handle this case, typically by defaulting to "fine" weather or skipping updates.
3.  **Value Storage**: `mWeatherZoneMap` stores `WeatherZoneChances` by value. This avoids dangling pointers and is efficient due to the small size of the structure (48 bytes).
4.  **Const Correctness**: `GetWeatherChances` is `const` and returns a `const` pointer, enforcing that configuration is read-only after loading.

## Member Reference

**WeatherMgr**
Constructor for the global weather manager. Initializes the object; the internal map is default-constructed.

**~WeatherMgr**
Destructor for the global weather manager. Cleans up the object; the internal map is destroyed automatically.

**GetWeatherChances**
Retrieves the weather probability configuration for a given zone ID. Returns a `const` pointer to `WeatherZoneChances` if the zone exists in the internal map, or `nullptr` otherwise. Used by `Weather` instances to initialize their randomization logic.

---

<!-- machine-true, projected from graph.json -->

## Map — WeatherMgr

*Source:* Weather.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| WeatherMgr | ctor | — | — | — |
| ~WeatherMgr | dtor | — | — | — |
| GetWeatherChances | method | — | Weather/FindOrCreateWeather | — |
