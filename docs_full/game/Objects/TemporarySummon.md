# TemporarySummon

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# TemporarySummon

**Purpose & Responsibilities**

`TemporarySummon` extends `Creature` to manage transient entities created dynamically by players, NPCs, or scripts. Unlike persistent creatures, these objects follow strict lifecycle rules defined by `TempSummonType`, governing when they despawn based on time, combat status, or death state.

Key responsibilities include:
1.  **Lifecycle Enforcement:** Tracking remaining lifetime via `m_timer` and triggering `UnSummon` when conditions (time expiry, death, combat exit) are met.
2.  **Summoner Coordination:** Linking to the creator (`m_summoner`) and notifying them upon removal to decrement active summon counts and trigger AI callbacks.
3.  **Ephemeral State:** Ensuring no database persistence (`SaveToDB` is empty) and proper cleanup from the map and memory.

## Member-by-Member Behavior

### Lifecycle Management

**`TemporarySummon` (Constructor)**
Initializes the creature with `CREATURE_SUBTYPE_TEMPORARY_SUMMON`. Sets default despawn type to `TEMPSUMMON_TIMED_OR_CORPSE_DESPAWN`, resets timers, stores the summoner's `ObjectGuid`, and marks `m_unSummonInformed` as `false`.

**`Summon`**
Activates the summon. Sets `m_type`, `m_timer`, and `m_lifetime`. If the type is respawnable (`ObjectDefines::IsRespawnableTempSummonType`), it calculates a respawn delay. Initializes AI via `Creature::AIM_Initialize` or a custom setter, then adds the creature to the map via `WorldObject::GetMap()->Add`.

**`Update`**
Core update loop. First, it skips despawn logic if the creature is charmed (`GetCharmerGuid()` is not empty and has `SPELL_AURA_MOD_CHARM`) to prevent premature removal of charmed minions. Then, it evaluates `m_type`:
*   **Timed Types:** Decrement `m_timer`. If expired, call `UnSummon`. Some types (e.g., `_OUT_OF_COMBAT`) pause/reset the timer if `IsInCombat()` is true.
*   **Death/Corpse Types:** Check `IsDead()`, `IsCorpse()`, or `IsDespawned()`. Call `UnSummon` if the specific death state is reached.
*   **Hybrid Types:** Combine time and state checks. For example, `TEMPSUMMON_TIMED_OR_CORPSE_DESPAWN` despawns if dead OR if timer expires out of combat.
*   **Special Cases:** `TEMPSUMMON_TIMED_DEATH_AND_DEAD_DESPAWN` forces death via `Unit::DoKillUnit` if the timer expires while alive and out of combat. `TEMPSUMMON_TIMED_COMBAT_OR_DEAD_DESPAWN` uses `m_justDied` to reset the timer upon death.
*   **Default:** Logs an error and calls `UnSummon`.
Finally, calls `Creature::Update`.

**`UnSummon`**
Removes the summon. If `delayDespawnTime` is provided, it switches to `TEMPSUMMON_TIMED_DESPAWN` and sets the timer. Otherwise, it stops combat (`Unit::CombatStop`), notifies the summoner (`InformSummonerOfDespawn`), and schedules removal via `WorldObject::AddObjectToRemoveList`.

**`CleanupsBeforeDelete`**
Ensures `InformSummonerOfDespawn` is called before deletion, safeguarding against missed notifications. Delegates to `Creature::CleanupsBeforeDelete`.

**`~TemporarySummon` (Destructor)**
Safety check: if `m_unSummonInformed` is `false`, logs an error. This indicates improper cleanup where the summoner’s counter was not decremented.

### Summoner Interaction

**`GetSummonerGuid`**
Returns the stored `ObjectGuid` of the summoner.

**`GetSummoner`**
Resolves `m_summoner` to a `Unit*` using `ObjectAccessor::GetUnit`. Returns `nullptr` if the summoner is not in memory.

**`InformSummonerOfDespawn`**
Notifies the summoner of removal. Guarded by `m_unSummonInformed` to ensure single execution. Retrieves the summoner from the map, calls `WorldObject::DecrementSummonCounter`, and if the summoner is a `Creature`, invokes `CreatureAI::SummonedCreatureDespawn` on its AI.

### Utilities

**`GetDespawnType`**
Returns the current `TempSummonType`.

**`SaveToDB`**
Empty override; temporary summons are not persisted.

**`TemporarySummonWaypoint` (Constructor)**
Subclass for visualizing waypoints. Stores `waypoint_id`, `path_id`, and `pathOrigin`. Used exclusively by chat commands.

## Cross-Unit Boundaries

### Calls Out
*   **`Creature.Main`**: Constructor, `Update`, `AIM_Initialize`, `CleanupsBeforeDelete`.
*   **`Unit.Main`**: `IsCorpse`, `IsDespawned`, `IsInCombat`, `IsAlive`, `IsDead`, `HasAuraType`, `GetCharmerGuid`, `DoKillUnit`, `CombatStop`.
*   **`WorldObject.Object`**: `GetMap`, `AddObjectToRemoveList`, `DecrementSummonCounter`.
*   **`ObjectAccessor`**: `GetUnit` (in `GetSummoner`).
*   **`ObjectDefines`**: `IsRespawnableTempSummonType`.
*   **`Log.Main`**: `Out` (error logging in `Update` and destructor).
*   **`CreatureAI`**: `SummonedCreatureDespawn`.
*   **`Map.Main`**: `GetWorldObject`.

### Called By
*   **`Player.Main`**: `SummonPossessedMinion` (creates summons).
*   **`WorldObject.Object`**: `SummonCreature` variants (factory creation).
*   **`ChatHandler.CreatureCommands`**: Waypoint commands use `GetSummonerGuid`; `Helper_CreateWaypointFor` creates `TemporarySummonWaypoint`; `HandleNpcDeleteCommand`/`UnsummonVisualWaypoints` call `UnSummon`; `HandleNpcAIInfoCommand` uses `GetDespawnType`.
*   **`Boss Scripts`**: Various bosses (`anubrekhan`, `cthun`, `thaddius`, etc.) call `UnSummon` to clean up adds/portals.
*   **`Creature.Main`**: `DespawnOrUnsummon` calls `UnSummon`; `Kill` accesses `GetSummonerGuid`.
*   **`Movement Generators`**: `PointMovementGenerator` and `TargetedMovementGenerator` access `GetSummonerGuid`.
*   **`scourge_invasion`**: `UpdateAI` calls `GetSummoner`.

## Data Model

This unit interacts with no database tables. `SaveToDB` is empty, and no SQL queries are present.

## Notable Implementation Details

1.  **Charm Exception:** `Update` bypasses all despawn logic if the creature is charmed. This prevents charmed minions (e.g., Warlock Infernals) from despawning due to their original summon timer.
2.  **Single Notification:** `InformSummonerOfDespawn` uses `m_unSummonInformed` to guarantee the summoner is notified exactly once, preventing counter corruption.
3.  **Combat Pausing:** Many timed types reset `m_timer` to `m_lifetime` when `IsInCombat()` is true, effectively pausing the countdown during fights.
4.  **Forced Death:** `TEMPSUMMON_TIMED_DEATH_AND_DEAD_DESPAWN` actively kills the unit (`DoKillUnit`) if the timer expires while alive and out of combat, rather than just despawning it.
5.  **Destructor Diagnostics:** The destructor logs an error if `m_unSummonInformed` is false, helping identify leaks where summons are deleted without proper cleanup.

## Member Reference

**TemporarySummon** (ctor): Initializes creature subtype, default despawn type, timers, and summoner GUID. Calls `Creature` constructor.

**Update**: Evaluates despawn conditions based on `m_type`. Skips logic if charmed. Calls `UnSummon` if conditions met. Calls `Creature::Update`. Uses `Unit` methods for state checks.

**GetSummonerGuid**: Returns the `ObjectGuid` of the summoner. Accessed by chat handlers and movement generators.

**GetDespawnType**: Returns the current `TempSummonType` enum. Used by debug commands.

**Summon**: Activates summon, sets type/timers. Initializes AI via `Creature::AIM_Initialize` or custom setter. Adds self to map via `WorldObject::GetMap`. Called by `Player` and `WorldObject` factories.

**UnSummon**: Stops combat, informs summoner, schedules removal. Can delay despawn if time provided. Called by boss scripts, chat handlers, and `Creature::DespawnOrUnsummon`.

**GetSummoner**: Resolves summoner GUID to `Unit*` using `ObjectAccessor`. Called by invasion scripts.

**InformSummonerOfDespawn**: Notifies summoner of removal. Decrements summon counter and calls `CreatureAI::SummonedCreatureDespawn`. Ensures notification happens only once.

**CleanupsBeforeDelete**: Ensures summoner is informed before deletion. Calls `Creature::CleanupsBeforeDelete`.

**~TemporarySummon** (dtor): Logs error if summoner was not informed of despawn, aiding in debugging resource leaks.

**SaveToDB**: Empty override; temporary summons are not persisted.

**TemporarySummonWaypoint** (ctor): Constructs a waypoint visualization summon. Called by `ChatHandler::Helper_CreateWaypointFor`.

---

<!-- machine-true, projected from graph.json -->

## Map — TemporarySummon

*Source:* TemporarySummon.cpp, TemporarySummon.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| TemporarySummon | ctor | Creature.Main/Creature, ObjectGuid/ObjectGuid | Player.Main/SummonPossessedMinion, WorldObject.Object/SummonCreature, WorldObject.Object/SummonCreature#2 | — |
| Update | method | Creature.Main/IsCorpse, Creature.Main/IsDespawned, Creature.Main/Update, Log.Main/Out, Object/GetEntry, ObjectGuid/IsEmpty, Unit.Main/DoKillUnit, Unit.Main/GetCharmerGuid, Unit.Main/HasAuraType, Unit.Main/IsAlive, Unit.Main/IsDead, Unit.Main/IsInCombat | — | — |
| GetSummonerGuid | method | — | ChatHandler.CreatureCommands/HandleWpAddCommand, ChatHandler.CreatureCommands/HandleWpExportCommand, ChatHandler.CreatureCommands/HandleWpModifyCommand, ChatHandler.CreatureCommands/HandleWpShowCommand, ChatHandler.CreatureCommands/UnsummonVisualWaypoints, instance_dire_maul/Reset#8, npcs_special/UpdateAI#10, npc_j_eevee/npc_j_eevee_scholomanceAI, PointMovementGenerator/MovementInform#3, TargetedMovementGenerator/MovementInform, TargetedMovementGenerator/MovementInform#2, Unit.Main/Kill | — |
| GetDespawnType | method | — | ChatHandler.CreatureCommands/HandleNpcAIInfoCommand | — |
| Summon | method | Creature.Main/AIM_Initialize, ObjectDefines/IsRespawnableTempSummonType, WorldObject.Object/GetMap | ChatHandler.CreatureCommands/Helper_CreateWaypointFor, Player.Main/SummonPossessedMinion, WorldObject.Object/SummonCreature, WorldObject.Object/SummonCreature#2 | — |
| UnSummon | method | Unit.Main/CombatStop, WorldObject.Object/AddObjectToRemoveList | boss_anubrekhan/ExplodeOneDeadCryptGuard, boss_anubrekhan/JustReachedHome, boss_anubrekhan/Reset, boss_cthun/DespawnAllTentacles, boss_cthun/DespawnPortal, boss_cthun/UpdateCthunTentacle, boss_loatheb/WhackAStalk, boss_marli/Reset, boss_razuvious/RespawnAdds, boss_sapphiron/UnSummonWingBuffet, boss_thaddius/HandleCheckSpawnAdd, boss_thaddius/HandleUnsummonAdd, boss_thaddius/HandleUnsummonCoil, ChatHandler.CreatureCommands/HandleNpcDeleteCommand, ChatHandler.CreatureCommands/UnsummonVisualWaypoints, Creature.Main/DespawnOrUnsummon, custom_creatures/Reset, dustwallow_marsh/UpdateAI#3, instance_naxxramas.boss_kelthuzad/DespawnAllIntroCreatures, instance_naxxramas.Main/DespawnPortal, instance_scarlet_monastery/Update, stormwind_city/JustDied, stormwind_city/Reset#2, ZoneScript/DelCreature | — |
| GetSummoner | method | ObjectAccessor/GetUnit | scourge_invasion/UpdateAI#9 | — |
| InformSummonerOfDespawn | method | Creature.Main/AI, CreatureAI/SummonedCreatureDespawn, Map.Main/GetWorldObject, Object/ToCreature, WorldObject.Object/DecrementSummonCounter, WorldObject.Object/GetMap | — | — |
| CleanupsBeforeDelete | method | Unit.Main/CleanupsBeforeDelete | — | — |
| ~TemporarySummon | dtor | Log.Main/Out, Object/GetGuidStr | — | — |
| SaveToDB | method | — | — | — |
| TemporarySummonWaypoint | ctor | — | ChatHandler.CreatureCommands/Helper_CreateWaypointFor | — |
