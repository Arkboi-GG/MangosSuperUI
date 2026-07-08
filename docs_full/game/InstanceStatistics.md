# InstanceStatistics

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# InstanceStatistics

**Purpose & Responsibilities**

`InstanceStatisticsMgr` is a singleton service that tracks, aggregates, and persists combat statistics for instances. It maintains three categories of data in memory:
1.  **Boss Wipes:** Counts of wipes per boss (`mapId`, `creatureEntry`).
2.  **Creature Kills by Spell:** Counts of player kills by specific creatures, broken down by the spell used.
3.  **Custom Counters:** Arbitrary integer counters identified by an enum index (currently `MR_BIGGLESWORTH_KILLS`).

The manager loads historical data from the `LogsDatabase` at startup and updates both in-memory maps and the database immediately upon each increment event.

## Member-by-Member Behavior

### Initialization

**`InstanceStatisticsMgr()`**
Default constructor. No logic; data is populated later by `LoadFromDB`.

**`LoadFromDB()`**
Populates the three internal maps from the database. It processes each table sequentially:
1.  **`instance_wipes`**: Queries rows and inserts `InstanceWipes` structs into `m_instanceWipes`, keyed by `{mapId, creatureEntry}`.
2.  **`instance_creature_kills`**: Queries rows. Since multiple spells map to one creature, it aggregates results: if a `{mapId, creatureEntry}` key exists, it adds the spell count to the existing `killsBySpells` sub-map; otherwise, it creates a new `InstanceCreatureKlls` entry.
3.  **`instance_custom_counters`**: Queries rows and populates `m_instanceCustomCounters` with `index` as key and `count` as value.

It uses `ProgressBar` for console feedback and logs the final count of loaded entries. If a table is empty, it logs that explicitly.

### Statistics Updates

**`IncrementWipeCounter(uint32 mapId, uint32 creatureEntry)`**
Increments the wipe count for a boss. It acquires `m_wipesMutex`, updates or initializes the count in `m_instanceWipes`, releases the lock, and immediately calls `Save(mapId, creatureEntry, count)` to persist the change.

**`IncrementKillCounter(Creature* pKiller, Player* pVictim, SpellEntry const* spellProto)`**
Increments the kill count for a creature using a specific spell. It validates inputs (non-null pointers, valid map), extracts `mapId`, `creatureEntry`, and `spellId` (defaulting to 0 if no spell), then acquires `m_creatureKillsMutex`. It updates the nested `killsBySpells` map within `m_instanceCreatureKills`, releases the lock, and immediately calls `Save(mapId, creatureEntry, spellId, count)` to persist.

**`IncrementCustomCounter(eInstanceCustomCounter index, bool save)`**
Increments a custom counter. It acquires `m_customCountersMutex`, updates or initializes the count in `m_instanceCustomCounters`, and releases the lock. If `save` is `true`, it immediately persists the change to `instance_custom_counters` via a DELETE/INSERT transaction. If `false`, the change remains in memory only.

### Persistence

**`Save(uint32 mapId, uint32 creatureEntry, uint32 spellId, uint32 count)`**
Persists a spell-specific kill count to `instance_creature_kills`. It uses a transaction to DELETE the existing row (if any) and INSERT the new count.

**`Save#2(uint32 mapId, uint32 creatureEntry, uint32 count)`**
Persists a wipe count to `instance_wipes`. It uses a transaction to DELETE the existing row (if any) and INSERT the new count.

## Cross-Unit Boundaries

*   **`World/SetInitialWorldSettings`**: Calls `LoadFromDB` during server startup.
*   **`instance_naxxramas.Main/SetData`**: Calls `IncrementWipeCounter` when a wipe is detected in Naxxramas.
*   **`Unit.Main/Kill`**: Calls `IncrementKillCounter` when a creature kills a player.
*   **`instance_naxxramas.Main/OnCreatureDeath`**: Calls `IncrementCustomCounter` for specific death events in Naxxramas.
*   **Database/Logging**: `LoadFromDB` uses `Database/Query`, `Field/GetUInt32`, `Log.Main/Out`, and `ProgressBar` utilities. `Save` and `IncrementCustomCounter` use `Database/BeginTransaction`, `CommitTransaction`, and `PExecute`. `IncrementKillCounter` uses `Map/Main/GetId`, `Object/GetEntry`, and `WorldObject/Object/GetMap` to extract context.

## Data Model

The unit interacts with three tables in `LogsDatabase`:

1.  **`instance_wipes`**: Tracks wipe counts per boss.
    *   Columns: `mapId` (PK), `creatureEntry` (PK), `count`.
2.  **`instance_creature_kills`**: Tracks kill counts per creature and spell.
    *   Columns: `mapId` (PK), `creatureEntry` (PK), `spellEntry` (PK), `count`.
3.  **`instance_custom_counters`**: Tracks arbitrary counters.
    *   Columns: `index` (PK), `count`.

## Notable Implementation Details

*   **Immediate Persistence**: Every increment operation triggers an immediate database transaction. This ensures durability but incurs I/O overhead for every kill/wipe.
*   **Delete-Insert Pattern**: `Save` methods use `DELETE` followed by `INSERT` rather than `UPDATE`, wrapped in transactions for atomicity.
*   **Thread Safety**: Three separate mutexes (`m_wipesMutex`, `m_creatureKillsMutex`, `m_customCountersMutex`) protect the respective maps, allowing concurrent updates to different statistic types. `LoadFromDB` assumes exclusive access (called once at startup).
*   **Aggregation Logic**: `LoadFromDB` manually aggregates `instance_creature_kills` rows into the nested `killsBySpells` map, as the DB stores flat rows while memory groups by creature.
*   **Typo**: The struct `InstanceCreatureKlls` contains a typo ("Klls"), consistent throughout the unit.

## Member Reference

**`InstanceStatisticsMgr()`**
Default constructor.

**`LoadFromDB()`**
Loads `instance_wipes`, `instance_creature_kills`, and `instance_custom_counters` from the database into memory.

**`IncrementWipeCounter(uint32 mapId, uint32 creatureEntry)`**
Increments wipe count in memory and persists to `instance_wipes`.

**`IncrementKillCounter(Creature* pKiller, Player* pVictim, SpellEntry const* spellProto)`**
Increments kill count for a creature/spell in memory and persists to `instance_creature_kills`.

**`IncrementCustomCounter(eInstanceCustomCounter index, bool save)`**
Increments custom counter in memory; persists to `instance_custom_counters` if `save` is true.

**`Save(uint32 mapId, uint32 creatureEntry, uint32 spellId, uint32 count)`**
Persists spell kill count to `instance_creature_kills` via DELETE/INSERT.

**`Save#2(uint32 mapId, uint32 creatureEntry, uint32 count)`**
Persists wipe count to `instance_wipes` via DELETE/INSERT.

---

<!-- machine-true, projected from graph.json -->

## Map — InstanceStatistics

*Source:* InstanceStatistics.cpp, InstanceStatistics.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| LoadFromDB | method | Database/Query, Field/GetUInt32, Log.Main/Out, ProgressBar/BarGoLink, ProgressBar/BarGoLink#3, ProgressBar/step, QueryResult/Fetch, QueryResult/GetRowCount, QueryResult/NextRow | World/SetInitialWorldSettings | instance_creature_kills, instance_custom_counters, instance_wipes |
| InstanceStatisticsMgr | ctor | — | — | — |
| IncrementWipeCounter | method | — | instance_naxxramas.Main/SetData | — |
| IncrementKillCounter | method | Map.Main/GetId, Object/GetEntry, WorldObject.Object/GetMap | Unit.Main/Kill | — |
| IncrementCustomCounter | method | Database/BeginTransaction, Database/CommitTransaction, Database/PExecute#2 | instance_naxxramas.Main/OnCreatureDeath | instance_custom_counters |
| Save#2 | method | Database/BeginTransaction, Database/CommitTransaction, Database/PExecute#2 | — | instance_creature_kills |
| Save | method | Database/BeginTransaction, Database/CommitTransaction, Database/PExecute#2 | — | instance_wipes |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `instance_creature_kills`: mapId int(10) unsigned PK, creatureEntry int(10) unsigned PK, spellEntry int(10) PK, count int(10) unsigned
- `instance_custom_counters`: index int(10) unsigned PK, count int(10) unsigned
- `instance_wipes`: mapId int(10) unsigned PK, creatureEntry int(10) unsigned PK, count int(10) unsigned

*`?` = nullable, `PK` = primary key column.*

