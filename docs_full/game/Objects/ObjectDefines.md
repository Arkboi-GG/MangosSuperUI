# ObjectDefines

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ObjectDefines

**ObjectDefines** (`ObjectDefines.h`) is a header-only utility providing global constants, enumerations, and inline helpers for object interaction mechanics in WoWVMaNGOS. It defines physical rules for perception, interaction, and temporary summon lifecycle management.

## Purpose & Responsibilities

1.  **Physical Constants:** Establishes fixed distances for interactions (contact, attack, trade), visibility ranges (grid-based, instance-specific), and movement tolerances.
2.  **Summon Lifecycle Logic:** Defines the `TempSummonType` enumeration dictating despawn conditions for summoned creatures, and provides helpers to stringify these types or check if they support respawning.

## Member-by-Member Behavior

### Constants & Enums
The header defines `#define` macros and enums used globally:
*   **Distances:** `CONTACT_DISTANCE` (0.5f), `INTERACTION_DISTANCE` (5.0f), `ATTACK_DISTANCE` (5.0f), `INSPECT_DISTANCE` (10.0f), `TRADE_DISTANCE` (11.11f).
*   **Visibility:** `MAX_VISIBILITY_DISTANCE` (tied to `SIZE_OF_GRIDS`), `DEFAULT_VISIBILITY_DISTANCE` (100.0f), `DEFAULT_VISIBILITY_INSTANCE` (170.0f), `DEFAULT_VISIBILITY_BG` (533.0f). Specific size-based ranges (`VISIBILITY_DISTANCE_GIGANTIC` to `TINY`) are also defined.
*   **Scales & Reach:** Default scales for players, Gnomes, and Taurens. Melee reach constants (`DEFAULT_COMBAT_REACH`, `MIN_MELEE_REACH`, `NOMINAL_MELEE_RANGE`) and derived `MELEE_RANGE`. Movement leeway constants (`LEEWAY_MIN_MOVE_SPEED`, `LEEWAY_BONUS_RANGE`).
*   **Enums:** `TempSummonType` (11 variants defining despawn triggers like time, death, or manual command), `SizeFactor`, `ObjectSpawnFlags`, and `WorldMasks`.

### Helper Functions

**TempSummonTypeToString**  
Converts a `uint32` summon type to a human-readable C-string. Maps each `TempSummonType` enum value to a descriptive name (e.g., `"Timed or Dead Despawn"`). Returns `"UNKNOWN"` for invalid inputs.

**IsRespawnableTempSummonType**  
Returns `true` if the given `TempSummonType` supports respawning logic. Specifically, it returns `true` for `TEMPSUMMON_TIMED_DESPAWN`, `TEMPSUMMON_TIMED_DESPAWN_OUT_OF_COMBAT`, and `TEMPSUMMON_MANUAL_DESPAWN`. Returns `false` for all other types (including death-based or hybrid despawns).

## Cross-Unit Boundaries

### Called By
*   **ChatHandler.CreatureCommands/HandleNpcAIInfoCommand**: Calls `TempSummonTypeToString` to display summon type information in admin/debug output.
*   **Map.ScriptCommands/ScriptCommand_SummonCreature**: Calls `IsRespawnableTempSummonType` to validate summon constraints during script execution.
*   **TemporarySummon/Summon**: Calls `IsRespawnableTempSummonType` during core summoning to determine if the entity is eligible for respawn mechanics.

### Calls Out
None. This unit is passive.

## Data Model
No database tables are accessed.

## Notable Implementation Details
*   **Inline Helpers:** Both functions are `inline` for zero-overhead usage in headers.
*   **Respawn Logic:** `IsRespawnableTempSummonType` explicitly excludes death-based despawns, reflecting that such summons are ephemeral unless managed by external systems.
*   **Melee Calculation:** `MELEE_RANGE` is derived as `NOMINAL_MELEE_RANGE - MIN_MELEE_REACH * 2` (resulting in 1.0f), accounting for model bounds within the nominal attack range.

## Member Reference

**TempSummonTypeToString**  
Inline function converting a `uint32` summon type to a descriptive C-string. Maps `TempSummonType` enums to names like `"Timed or Dead Despawn"`. Returns `"UNKNOWN"` for invalid inputs. Called by `ChatHandler.CreatureCommands/HandleNpcAIInfoCommand`.

**IsRespawnableTempSummonType**  
Inline function returning `true` if the `TempSummonType` allows respawning. Returns `true` for `TEMPSUMMON_TIMED_DESPAWN`, `TEMPSUMMON_TIMED_DESPAWN_OUT_OF_COMBAT`, and `TEMPSUMMON_MANUAL_DESPAWN`; `false` otherwise. Called by `Map.ScriptCommands/ScriptCommand_SummonCreature` and `TemporarySummon/Summon`.

---

<!-- machine-true, projected from graph.json -->

## Map — ObjectDefines

*Source:* ObjectDefines.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| TempSummonTypeToString | function | — | ChatHandler.CreatureCommands/HandleNpcAIInfoCommand | — |
| IsRespawnableTempSummonType | function | — | Map.ScriptCommands/ScriptCommand_SummonCreature, TemporarySummon/Summon | — |
