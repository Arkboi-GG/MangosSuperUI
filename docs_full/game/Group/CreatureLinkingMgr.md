# CreatureLinkingMgr

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# CreatureLinkingMgr

**Purpose & Responsibilities**

`CreatureLinkingMgr` and its companion runtime class `CreatureLinkingHolder` implement the "NPC Linking" system in MaNGOS. This system allows game designers to define logical relationships between creatures (NPCs) such that events occurring on one creature (the "master") automatically trigger specific behaviors on other creatures (the "slaves").

The system supports two modes of definition:
1.  **Template-based (`creature_linking_template`):** Links are defined by creature template IDs (`entry`) and map ID. This applies to all instances of that creature type on that map.
2.  **Instance-based (`creature_linking`):** Links are defined by specific creature GUIDs. This applies only to specific spawns.

Supported linking behaviors include:
*   **Aggro Sharing:** Slaves enter combat when the master aggroes, and vice versa.
*   **Death/Evade Propagation:** Slaves can despawn, self-kill, respawn, or evade when the master dies or evades.
*   **Respawn Synchronization:** Slaves respawn or despawn when the master respawns.
*   **Movement Following:** Slaves physically follow the master using pathfinding.
*   **Conditional Spawning:** Slaves may be prevented from spawning if the master is alive or dead.

`CreatureLinkingMgr` is a singleton responsible for loading static configuration from the database and validating entries. `CreatureLinkingHolder` is instantiated per-map to manage the dynamic state (active GUIDs) of linked creatures during gameplay.

---

## Member-by-Member Behavior

### Initialization and Data Loading

**`LoadFromDB`**
This method initializes the global linking configuration by querying two database tables: `creature_linking_template` and `creature_linking`.
1.  It clears internal storage maps (`m_creatureLinkingMap`, `m_creatureLinkingGuidMap`) and trigger sets.
2.  **Template Loading:** It iterates through `creature_linking_template`. For each row, it constructs a `CreatureLinkingInfo` structure. It calls `IsLinkingEntryValid` to verify that the slave entry, master entry, and map exist. If valid, it stores the info in `m_creatureLinkingMap` keyed by the slave's entry. It also records the master's entry in `m_eventTriggers` to quickly identify potential masters.
3.  **GUID Loading:** It iterates through `creature_linking`. For each row, it constructs a `CreatureLinkingInfo` with `mapId` set to `INVALID_MAP_ID` (indicating a GUID-specific link). It validates the slave and master GUIDs via `IsLinkingEntryValid`. If valid, it stores the info in `m_creatureLinkingGuidMap` keyed by the slave's GUID. It records the master's GUID in `m_eventGuidTriggers`.

**`IsLinkingEntryValid`**
A static validation helper called during `LoadFromDB`. It ensures data integrity before storing linking rules.
*   **Entry Mode (`byEntry == true`):** Checks if the slave and master creature templates exist via `ObjectMgr/GetCreatureTemplate`. Verifies the specified map exists. If the link involves following or conditional spawning (`FLAG_FOLLOW`, `FLAG_CANT_SPAWN_IF_BOSS_*`) and `searchRange` is 0, it queries the `creature` table to ensure the master is unique on that map. If multiple masters exist, the link is rejected. It caches the unique master's DB GUID in `pTmp->masterDBGuid`.
*   **GUID Mode (`byEntry == false`):** Checks if the slave and master spawn data exist via `ObjectMgr/GetCreatureData`. Ensures both are on the same map.
*   **Flag Validation:** Rejects entries with invalid flags or zero flags. Specifically rejects `FLAG_DESPAWN_ON_RESPAWN` if the slave is linking to itself (pointless).

### Static Query Methods (CreatureLinkingMgr)

These methods allow the game engine to query the static configuration loaded by `LoadFromDB`.

**`IsLinkedEventTrigger`**
Determines if a creature acts as a "master" that triggers events for others. It returns `true` if:
1.  The creature's entry is in `m_eventTriggers`.
2.  The creature's GUID is in `m_eventGuidTriggers`.
3.  The creature has a linking configuration (`GetLinkedTriggerInformation`) that includes "reverse" flags (`EVENT_MASK_TRIGGER_TO`), meaning it reacts to its own slaves' events (e.g., `FLAG_TO_AGGRO_ON_AGGRO`).

**`IsLinkedMaster`**
Returns `true` only if the creature's entry is explicitly listed in `m_eventTriggers`. This is used to distinguish true masters from slaves that might have reverse-trigger flags.

**`IsSpawnedByLinkedMob`**
Checks if a creature's spawning is dependent on another creature. It delegates to the overload taking `CreatureLinkingInfo`.

**`IsSpawnedByLinkedMob` (overload)**
Returns `true` if the provided `CreatureLinkingInfo` contains flags `FLAG_CANT_SPAWN_IF_BOSS_DEAD` or `FLAG_CANT_SPAWN_IF_BOSS_ALIVE` AND has a valid master identifier (`masterDBGuid` or non-zero `searchRange`).

**`GetLinkedTriggerInformation`**
Retrieves the `CreatureLinkingInfo` for a creature.
1.  First checks `m_creatureLinkingGuidMap` for an exact GUID match.
2.  If not found, checks `m_creatureLinkingMap` for an entry match, filtering by `mapId` to ensure the rule applies to the current map.

**`GetLinkedTriggerInformation` (overload)**
Direct lookup version taking entry, GUID, and map ID. Used internally.

### Dynamic State Management (CreatureLinkingHolder)

`CreatureLinkingHolder` manages the active links on a specific map. It is populated when creatures spawn and queried when events occur.

**`AddSlaveToHolder`**
Called when a creature spawns. It retrieves the creature's linking info.
*   **GUID Case:** Adds the creature's GUID to `m_holderGuidMap` under the master's GUID key, grouped by linking flag.
*   **Entry Case:** Adds the creature's GUID to `m_holderMap` under the master's entry key, grouped by linking flag and search range.
This creates an index: *Master -> List of Slaves*.

**`AddMasterToHolder`**
Called when a creature spawns. It checks if the creature is a "Master" (via `IsLinkedMaster`). If so, it adds the creature's GUID to `m_masterGuid` under its entry key. This creates an index: *Master Entry -> Master GUID*, allowing the system to find the actual live instance of a master by its template ID.

**`DoCreatureLinkingEvent`**
The core event dispatcher. Called by `Creature` when an event occurs (Aggro, Evade, Die, Respawn, Despawn).
1.  Validates that the source creature is a valid trigger.
2.  Ignores player-controlled pets.
3.  Determines the relevant flag masks for the event type (e.g., `EVENT_MASK_ON_AGGRO`).
4.  **Process Slaves:** Iterates through `m_holderMap` and `m_holderGuidMap` to find all slaves linked to this master. For each group, it calls `ProcessSlaveGuidList`.
5.  **Process Reverse Actions:** If the source creature has reverse flags (e.g., `FLAG_TO_AGGRO_ON_AGGRO`), it locates the actual Master creature (using `m_masterGuid` or direct GUID lookup) and applies the reverse effect (e.g., making the master aggro if the slave aggroed).

**`ProcessSlaveGuidList`**
Iterates through a list of slave GUIDs. For each valid, non-pet slave within the required `searchRange` of the source, it calls `ProcessSlave`. Removes stale GUIDs from the list if the creature no longer exists.

**`ProcessSlave`**
Executes the specific behavior for a single slave based on the event type and flags:
*   **AGGRO:** If `FLAG_AGGRO_ON_AGGRO`, forces the slave into combat with the enemy. Handles dungeon raid-combat flags.
*   **EVADE:** If `FLAG_DESPAWN_ON_EVADE`, despawns the slave. If `FLAG_EVADE_ON_EVADE`, forces the slave to evade. If `FLAG_RESPAWN_ON_EVADE`, respawns the slave.
*   **DIE:** If `FLAG_SELFKILL_ON_DEATH`, kills the slave instantly. If `FLAG_DESPAWN_ON_DEATH`, despawns it. If `FLAG_RESPAWN_ON_DEATH`, respawns it.
*   **RESPAWN:** If `FLAG_RESPAWN_ON_RESPAWN`, respawns the slave (with a loop-prevention check). If `FLAG_DESPAWN_ON_RESPAWN`, despawns it. If `FLAG_FOLLOW`, initiates following.
*   **DESPAWN:** If `FLAG_DESPAWN_ON_DESPAWN`, despawns the slave.

**`SetFollowing`**
Calculates the offset between the slave and master based on their respawn coordinates. It computes the distance (subtracting bounding radii) and the relative angle. It then commands the slave's motion master to `MoveFollow` the master at that distance and angle.

**`IsSlaveInRangeOfMaster`**
Checks if a slave is within the `searchRange` of the master. Uses Euclidean distance on X/Y coordinates. If `searchRange` is 0, it always returns `true` (global/map-wide link).

**`IsRespawnReady`**
Checks if a creature is ready to respawn based on its respawn timer and the `CanSpawn` condition.

**`CanSpawn`**
Checks if a creature should spawn based on linking rules.
1.  Retrieves linking info.
2.  If `searchRange` is 0 (global), it checks the master's respawn status (`IsRespawnReady`) against flags `FLAG_CANT_SPAWN_IF_BOSS_DEAD` or `FLAG_CANT_SPAWN_IF_BOSS_ALIVE`.
3.  If `searchRange` > 0, it searches for a live master within range. If found, it checks the master's alive status against the flags. If no master is found in range, it defaults to allowing spawn.

**`TryFollowMaster`**
Used to re-establish following if it was broken. It locates the master (by entry or GUID) and calls `SetFollowing` if the master is alive and in range.

---

## Cross-Unit Boundaries

### Calls Out (Dependencies)

*   **Database/Query, Field/GetUInt...:** Used in `LoadFromDB` and `IsLinkingEntryValid` to read linking configurations from `creature_linking_template` and `creature_linking`.
*   **ObjectMgr/GetCreatureTemplate, GetCreatureData, IsExistingCreatureId/Guid:** Used in `IsLinkingEntryValid` to validate that referenced creature templates and spawn data exist.
*   **Creature.Main/EnterCombatWithTarget, Respawn, ForcedDespawn, AI/AttackStart, AI/EnterEvadeMode:** Called in `ProcessSlave` and `DoCreatureLinkingEvent` to execute the physical effects of linking (combat, death, movement).
*   **Creature.Main/GetRespawnCoord:** Called in `SetFollowing` and `IsSlaveInRangeOfMaster` to determine spatial relationships for following and range checks.
*   **Unit.Main/IsAlive, IsInCombat, DealDamage, SetInCombatWith:** Used to check state and force state changes on slaves/masters.
*   **Map.Main/GetCreature, IsDungeon:** Used to retrieve live creature instances and check for special dungeon combat rules.
*   **WorldObject.Object/GetMap, GetObjectGuid:** Used to navigate the object hierarchy and identify creatures.
*   **CreatureInfo/GetHighGuid:** Used to construct full ObjectGuids for masters when looking them up by entry.
*   **MapPersistentStateMgr/GetCreatureRespawnTime:** Used in `IsRespawnReady` to check if a creature's respawn timer has expired.

### Called By (Integration Points)

*   **World/SetInitialWorldSettings:** Calls `LoadFromDB` during server startup to initialize the linking system.
*   **Creature.Main/LoadFromDB:** Calls `IsSpawnedByLinkedMob` to determine if a creature should spawn based on linking rules.
*   **Creature.Main/Create:** Calls `AddSlaveToHolder` and `AddMasterToHolder` to register newly spawned creatures in the dynamic holder.
*   **Creature.Main/Update:** Calls `CanSpawn` periodically to check if conditions for spawning have changed.
*   **Creature.Main/RemoveCorpse:** Calls `DoCreatureLinkingEvent` (likely via `LINKING_EVENT_DIE`) to propagate death effects.
*   **Creature.Main/Respawn:** Calls `DoCreatureLinkingEvent` (likely via `LINKING_EVENT_RESPAWN`) to propagate respawn effects.
*   **Unit.Main/SelectHostileTarget, SetInCombatState, TauntFadeOut:** Call `DoCreatureLinkingEvent` (likely via `LINKING_EVENT_AGGRO` or `LINKING_EVENT_EVADE`) to propagate combat state changes.
*   **WorldObject.Object/SummonCreature:** Calls `DoCreatureLinkingEvent` (likely via `LINKING_EVENT_DESPAWN` or similar) to handle summoned creature interactions.
*   **Creature.MotionMaster/MoveTargetedHome:** Calls `TryFollowMaster` to attempt re-following if the slave's movement is interrupted.

---

## Data Model

The unit interacts with two custom database tables:

### `creature_linking_template`
Defines linking rules based on creature template IDs.
*   **`entry` (PK):** The slave creature's template ID.
*   **`map` (PK):** The map ID where this rule applies.
*   **`master_entry`:** The master creature's template ID.
*   **`flag`:** Bitmask of `CreatureLinkingFlags` defining the behavior.
*   **`search_range`:** Radius for proximity checks. 0 implies map-wide/global.

### `creature_linking`
Defines linking rules based on specific creature GUIDs.
*   **`guid` (PK):** The slave creature's GUID.
*   **`master_guid`:** The master creature's GUID.
*   **`flag`:** Bitmask of `CreatureLinkingFlags`.

### `creature`
Queried indirectly via `ObjectMgr` and direct SQL in `IsLinkingEntryValid` to validate existence and uniqueness of masters. Columns used: `guid`, `id`, `map`.

---

## Notable Implementation Details

1.  **Two-Tier Architecture:** The system separates static configuration (`CreatureLinkingMgr`, singleton) from dynamic runtime state (`CreatureLinkingHolder`, per-map). This allows efficient lookups: static rules are validated once at load, while dynamic holders track only active creatures on a map.
2.  **GUID vs. Entry Linking:** The system supports both generic (entry-based) and specific (GUID-based) links. GUID links take precedence in `GetLinkedTriggerInformation`. Entry links require map matching.
3.  **Uniqueness Constraint for Global Follow/Spawn:** In `IsLinkingEntryValid`, if `searchRange` is 0 and flags involve following or conditional spawning, the system queries the `creature` table to ensure the master is unique on that map. If multiple masters exist, the link is rejected. This prevents ambiguity in global links.
4.  **Reverse Triggers:** Flags like `FLAG_TO_AGGRO_ON_AGGRO` allow slaves to trigger events on masters. `DoCreatureLinkingEvent` handles this by looking up the master's live instance and applying the effect.
5.  **Following Logic:** `SetFollowing` calculates the follow offset based on *respawn coordinates*, not current positions. This ensures slaves maintain a consistent formation relative to the master's original spawn point. It subtracts bounding radii to prevent clipping.
6.  **Loop Prevention in Respawn:** In `ProcessSlave`, `FLAG_RESPAWN_ON_RESPAWN` checks `pSlave->GetRespawnTime() > time(nullptr)` to avoid infinite respawn loops if a group respawns simultaneously.
7.  **Pet Exclusion:** Pets are explicitly ignored in `ProcessSlaveGuidList` and `AddMasterToHolder` to prevent player pets from interfering with NPC linking mechanics.
8.  **Stale GUID Cleanup:** `ProcessSlaveGuidList` removes GUIDs from the slave list if the creature no longer exists on the map, keeping the holder clean.

---

## Member Reference

**`LoadFromDB`**: Loads linking configurations from `creature_linking_template` and `creature_linking` tables, validates entries, and populates static maps.

**`CreatureLinkingMgr`**: Default constructor for the singleton manager.

**`IsLinkingEntryValid`**: Static helper to validate linking entries against database existence and constraints (uniqueness, map consistency).

**`IsLinkedEventTrigger`**: Checks if a creature triggers events for others (is a master or has reverse flags).

**`IsLinkedMaster`**: Checks if a creature is a master defined by entry in `m_eventTriggers`.

**`IsSpawnedByLinkedMob`**: Checks if a creature's spawning depends on another creature.

**`IsSpawnedByLinkedMob#2`**: Overload checking `CreatureLinkingInfo` for spawn-dependency flags.

**`GetLinkedTriggerInformation`**: Retrieves linking info for a creature by entry/GUID/map.

**`GetLinkedTriggerInformation#2`**: Overload for direct lookup by entry, GUID, and map ID.

**`AddSlaveToHolder`**: Registers a spawned creature as a slave in the map's dynamic holder.

**`AddMasterToHolder`**: Registers a spawned creature as a master in the map's dynamic holder.

**`DoCreatureLinkingEvent`**: Dispatches linking events (aggro, die, etc.) to slaves and handles reverse triggers on masters.

**`ProcessSlaveGuidList`**: Iterates through slave GUIDs, filters by range/existence, and processes each.

**`ProcessSlave`**: Executes specific linking behaviors (aggro, despawn, follow, etc.) on a single slave.

**`SetFollowing`**: Calculates offset and commands a slave to follow a master.

**`IsSlaveInRangeOfMaster`**: Checks if a slave is within the search range of a master.

**`IsSlaveInRangeOfMaster#2`**: Overload checking range using pre-calculated slave coordinates.

**`IsRespawnReady`**: Checks if a creature is ready to respawn based on timer and spawn conditions.

**`CanSpawn`**: Checks if a creature should spawn based on linking rules (master alive/dead status).

**`CanSpawn#2`**: Internal recursive helper for spawn checking.

**`TryFollowMaster`**: Attempts to re-establish following for a slave if it was broken.

---

<!-- machine-true, projected from graph.json -->

## Map — CreatureLinkingMgr

*Source:* CreatureLinkingMgr.cpp, CreatureLinkingMgr.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| LoadFromDB | method | Database/Query, Field/GetUInt16, Field/GetUInt32, Log.Main/Out, ProgressBar/BarGoLink, ProgressBar/BarGoLink#3, ProgressBar/step, QueryResult/Fetch, QueryResult/GetRowCount, QueryResult/NextRow | World/SetInitialWorldSettings | creature_linking, creature_linking_template |
| CreatureLinkingMgr | ctor | — | — | — |
| IsLinkingEntryValid | method | Database/PQuery, Field/GetUInt32, Log.Main/Out, ObjectMgr/GetCreatureData, ObjectMgr/GetCreatureTemplate, ObjectMgr/IsExistingCreatureGuid, ObjectMgr/IsExistingCreatureId, QueryResult/Fetch, QueryResult/GetRowCount | — | creature |
| IsLinkedEventTrigger | method | Object/GetEntry, Object/GetGUIDLow | Creature.Main/Create | — |
| IsLinkedMaster | method | Object/GetEntry | — | — |
| IsSpawnedByLinkedMob | method | — | Creature.Main/LoadFromDB | — |
| IsSpawnedByLinkedMob#2 | method | — | — | — |
| GetLinkedTriggerInformation | method | Object/GetEntry, Object/GetGUIDLow, WorldObject.Object/GetMapId | Creature.Main/Create | — |
| GetLinkedTriggerInformation#2 | method | — | — | — |
| AddSlaveToHolder | method | Object/GetObjectGuid | Creature.Main/Create | — |
| AddMasterToHolder | method | Creature.Main/IsPet, Object/GetEntry, Object/GetObjectGuid, ObjectGuid/operator== | Creature.Main/Create | — |
| DoCreatureLinkingEvent | method | Creature.Main/EnterCombatWithTarget, Creature.Main/Respawn, CreatureInfo/GetHighGuid, Map.Main/GetCreature, Object/GetEntry, Object/GetGUIDLow, ObjectGuid/ObjectGuid#3, ObjectMgr/GetCreatureData, ObjectMgr/GetCreatureTemplate, Unit.Main/IsAlive, WorldObject.Object/GetMap, WorldObject.Object/IsControlledByPlayer | Creature.Main/LoadFromDB, Creature.Main/RemoveCorpse, Creature.Main/Update, Unit.Main/SelectHostileTarget, Unit.Main/SetInCombatState, Unit.Main/TauntFadeOut, WorldObject.Object/SummonCreature, WorldObject.Object/SummonCreature#2 | — |
| ProcessSlaveGuidList | method | Creature.Main/IsPet, Map.Main/GetCreature, WorldObject.Object/GetMap | — | — |
| ProcessSlave | method | Creature.Main/AI, Creature.Main/ForcedDespawn, Creature.Main/GetRespawnTime, Creature.Main/HasStaticFlag#2, Creature.Main/IsDespawned, Creature.Main/Respawn, Creature.Main/SetInCombatWithZone, CreatureAI/AttackStart, CreatureAI/EnterEvadeMode, Map.Main/IsDungeon, Unit.Main/DealDamage, Unit.Main/GetHealth, Unit.Main/IsAlive, Unit.Main/IsInCombat, Unit.Main/SetInCombatWith, WorldObject.Object/GetMap, WorldObject.Object/IsControlledByPlayer | — | — |
| SetFollowing | method | Creature.Main/GetRespawnCoord, Creature.MotionMaster/MoveFollow, Unit.Main/GetMotionMaster, Unit.Main/GetObjectBoundingRadius | — | — |
| IsSlaveInRangeOfMaster | method | Creature.Main/GetRespawnCoord | — | — |
| IsSlaveInRangeOfMaster#2 | method | Creature.Main/GetRespawnCoord | — | — |
| IsRespawnReady | method | Map.Main/GetPersistentState, MapPersistentStateMgr/GetCreatureRespawnTime | — | — |
| CanSpawn | method | Creature.Main/GetRespawnCoord, WorldObject.Object/GetMap | Creature.Main/LoadFromDB, Creature.Main/Update | — |
| CanSpawn#2 | method | Map.Main/GetCreature, ObjectMgr/GetCreatureData, Unit.Main/IsAlive | — | — |
| TryFollowMaster | method | CreatureInfo/GetHighGuid, Map.Main/GetCreature, ObjectGuid/ObjectGuid#3, ObjectMgr/GetCreatureData, ObjectMgr/GetCreatureTemplate, Unit.Main/IsAlive, WorldObject.Object/GetMap | Creature.MotionMaster/MoveTargetedHome | — |

---

<!-- machine-true, dumped from the database by verify -->

## Schema — verified columns (dumped from the live database)

- `creature`: guid int(10) unsigned PK, id mediumint(8) unsigned, id2 mediumint(8) unsigned, id3 mediumint(8) unsigned, id4 mediumint(8) unsigned, id5 mediumint(8) unsigned, map smallint(5) unsigned, position_x float, position_y float, position_z float, orientation float, spawntimesecsmin int(10) unsigned, spawntimesecsmax int(10) unsigned, wander_distance float, health_percent float, mana_percent float unsigned, movement_type tinyint(3) unsigned, spawn_flags int(10) unsigned, visibility_mod float?, patch_min tinyint(3) unsigned, patch_max tinyint(3) unsigned
- `creature_linking`: guid int(10) unsigned PK, master_guid int(10) unsigned, flag mediumint(8) unsigned
- `creature_linking_template`: entry mediumint(8) unsigned PK, map smallint(5) unsigned PK, master_entry mediumint(8) unsigned, flag mediumint(8) unsigned, search_range mediumint(8) unsigned

*`?` = nullable, `PK` = primary key column.*

