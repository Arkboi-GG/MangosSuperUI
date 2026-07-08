# PoolTemplateData

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# PoolTemplateData

**PoolTemplateData** is a lightweight struct in `PoolManager.h` that holds the static configuration for a single spawn pool. It stores the maximum number of simultaneous spawns (`MaxLimit`), behavioral flags (`PoolFlags`), the specific map context (`mapEntry`), and an optional `description`. It does not manage runtime spawn state; that is handled by `PoolGroup` and `SpawnedPoolData`.

## Purpose & Responsibilities

1.  **Configuration Storage:** Holds immutable pool properties loaded from the database by `PoolManager`.
2.  **Map Validation:** Provides `CanBeSpawnedAtMap` to verify if a pool is valid for a specific `MapEntry`, preventing spawns on incorrect maps.
3.  **Behavioral Queries:** Exposes `IsAutoSpawn` to determine if the pool should activate immediately during initialization.

## Member-by-Member Behavior

### **PoolTemplateData** (Constructor)
Initializes members to safe defaults: `mapEntry` to `nullptr`, `MaxLimit` to `0`, `PoolFlags` to `0`, and `InstanceId` to `0`. This ensures uninitialized templates do not trigger accidental spawns or null-pointer dereferences.

### **CanBeSpawnedAtMap**
Returns `true` if `mapEntry` is non-null and identical to the provided `MapEntry const*`. It uses pointer equality, assuming `MapEntry` objects are unique singletons. If `mapEntry` is `nullptr`, it always returns `false`.

### **IsAutoSpawn**
Returns `true` if the `POOL_FLAG_AUTO_SPAWN` bit (`0x1`) is set in `PoolFlags`. This indicates the pool should spawn immediately when the pool system or map initializes, rather than waiting for player proximity or events.

## Cross-Unit Boundaries

**PoolTemplateData** is a passive data holder. It is read by other units to make spawn decisions:

*   **ChatHandler.LookupCommands/HandlePoolListCommand** and **ChatHandler.LookupCommands/ShowPoolListHelper**: Call `IsAutoSpawn` and `CanBeSpawnedAtMap` to display pool status and filter lists for GMs.
*   **ChatHandler.MiscCommands/HandlePoolInfoCommand**: Calls `IsAutoSpawn` to report pool behavior in detail.
*   **PoolManager/Initialize**: Calls `IsAutoSpawn` to identify pools that must spawn immediately upon server/map startup.
*   **PoolManager/LoadFromDB**: Populates `PoolTemplateData` instances from the database.
*   **PoolManager/InitSpawnPool**: Calls `CanBeSpawnedAtMap` to validate that a pool is applicable to the target map before initializing its spawn state.

## Data Model

**PoolTemplateData** does not execute SQL. It mirrors the `pool_template` table schema:
*   `mapEntry`: Derived from the `map` column (converted to a `MapEntry` pointer).
*   `MaxLimit`: From `max_limit`.
*   `PoolFlags`: From `spawn_mask` (or equivalent flags column).
*   `InstanceId`: From `instance_id`.
*   `description`: From `description`.

## Notable Implementation Details

1.  **Pointer Equality:** `CanBeSpawnedAtMap` relies on `mapEntry == entry`. This assumes `MapEntry` objects are stable, unique pointers. If `MapEntry` instances are copied or recreated, this check fails.
2.  **Bitwise Flags:** `PoolFlags` supports multiple behaviors via bitwise OR. `IsAutoSpawn` only checks bit 0. New flags must avoid conflicts.
3.  **Null Safety:** `CanBeSpawnedAtMap` explicitly checks for `nullptr` before comparison, preventing crashes on unconfigured templates.

## Member Reference

**PoolTemplateData**
Constructor initializing `mapEntry` to `nullptr`, `MaxLimit` to `0`, `PoolFlags` to `0`, and `InstanceId` to `0`.

**CanBeSpawnedAtMap**
Returns `true` if `mapEntry` is non-null and equals the provided `MapEntry` pointer.

**IsAutoSpawn**
Returns `true` if `POOL_FLAG_AUTO_SPAWN` (0x1) is set in `PoolFlags`.

---

<!-- machine-true, projected from graph.json -->

## Map — PoolTemplateData

*Source:* PoolManager.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| PoolTemplateData | ctor | — | — | — |
| CanBeSpawnedAtMap | method | — | ChatHandler.LookupCommands/HandlePoolListCommand, PoolManager/InitSpawnPool | — |
| IsAutoSpawn | method | — | ChatHandler.LookupCommands/ShowPoolListHelper, ChatHandler.MiscCommands/HandlePoolInfoCommand, PoolManager/Initialize, PoolManager/LoadFromDB | — |
