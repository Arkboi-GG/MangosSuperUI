# elemental_invasions

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# elemental_invasions

## Purpose & Responsibilities

`elemental_invasions.cpp` implements the logic for the Elemental Invasions world event. It manages two components:
1.  **Rifts (`elemental_invasion_riftAI`)**: GameObjects that spawn and track invader creatures. They monitor global kill counts and time to progress through difficulty stages, increasing spawn rates until a boss stage is reached.
2.  **Invaders (`npc_invaderAI`)**: Creatures summoned by rifts. They engage players in combat using element-specific spells. Upon death, they increment a global kill counter stored in saved variables, which drives rift progression.

## Data Model

This unit does not query SQL tables directly. It uses `ObjectMgr` saved variables as a persistent key-value store for runtime state. The `InvasionData` array maps each element to specific variable keys:

| Element | Event ID | Zone | Dead Invaders Key | Stage Key |
| :--- | :--- | :--- | :--- | :--- |
| Fire | 68 | 490 | `VAR_FIRE_KILLS` | `VAR_FIRE` |
| Air | 69 | 1377 | `VAR_AIR_KILLS` | `VAR_AIR` |
| Earth | 70 | 16 | `VAR_EARTH_KILLS` | `VAR_EARTH` |
| Water | 71 | 618 | `VAR_WATER_KILLS` | `VAR_WATER` |

*   **`varDeadInvaders`**: Cumulative kills for the element since the last stage transition. Resets to 0 when the stage advances.
*   **`varStage`**: Current invasion stage (difficulty). Increases spawn counts. Progression stops at `STAGE_BOSS` (external constant).

## Member-by-Member Behavior

### Rift Management (`elemental_invasion_riftAI`)

**`elemental_invasion_riftAI` (ctor)**
Initializes timers: `m_uiTimer` (500ms, for frequent checks) and `m_uiIncreaseTimer` (1 hour, for forced stage progression). Stores the element index.

**`DoSpawn`**
Spawns one invader near the rift:
1.  Summons the invader (`InvasionData[m_uiEventIndex].invader`) as a temporary entity (despawn on death or after 1 hour).
2.  Finds a valid walkable position 15–65 units from the rift using `Map.Main/GetWalkRandomPosition`. It retries up to 20 times; if it fails to find a spot >15 units away, it uses the last calculated position.
3.  Configures the invader’s movement: sets home position, wander distance (30 units), and random motion type.
4.  Stores the invader’s GUID in `m_uiInvadersGuid` at the provided index for tracking.

**`UpdateAI`**
Runs every tick. On the short timer expiry (70 seconds):
1.  Verifies the event is active (`GameEventMgr.Main/IsActiveEvent`) and the rift is in the correct zone.
2.  Reads `deadInvaders` and `spawnStage` from saved variables.
3.  **Progression**: If `spawnStage < STAGE_BOSS` and (`deadInvaders >= 50` OR `m_uiIncreaseTimer` expired), it increments `spawnStage`, resets `deadInvaders` to 0, saves the new stage, and resets `m_uiIncreaseTimer` to 1 hour.
4.  **Spawning**: Calculates required spawns: `MIN_RIFT_SPAWN` (3) + `spawnStage` - 1, capped at `MAX_RIFT_SPAWN` (6). It iterates through tracked GUIDs; if a creature is missing, it calls `DoSpawn` to replace it.

### Invader Combat (`npc_invaderAI`)

**`npc_invaderAI` (ctor)**
Initializes the AI and calls `Reset()` to randomize spell timers.

**`Reset`**
Sets random initial delays for all spell timers using `shared_Util/urand` to prevent synchronized casting.

**`JustDied`**
If the event is active, reads the current `deadInvaders` count for its element, increments it, and saves it back. This triggers stage progression in the rifts.

**`UpdateAI#2`**
Combat loop. Returns if no target. Executes element-specific logic based on `m_uiEventIndex`:
*   **Fire**: Casts `SPELL_FIRE_SHIELD` (self, 6–8s) and `SPELL_BLAST_WAVE` (victim, 10y range, 13–18s).
*   **Air**: Casts `SPELL_LIGHTN_SHIELD` (self, 10–12s) and `SPELL_WHIRLWIND` (victim, 8y range, 9–12s).
*   **Earth**: Casts `SPELL_KNOCKDOWN` (victim, 5y range, 11–15s) and `SPELL_EARTH_SHOCK` (victim, 9–13s).
*   **Water**: Casts `SPELL_CHILLED` (victim, 5y range, 5–8s) and `SPELL_FROST_SHOCK` (victim, 8–15s).
Finally, performs melee attacks via `CreatureAI/DoMeleeAttackIfReady`.

### Factory & Registration

**`GetAI_go_elemental_invasion_rift_*`**
Factory functions creating `elemental_invasion_riftAI` instances for Fire, Water, Earth, and Air elements respectively.

**`GetAI_npc_*_invader`**
Factory functions creating `npc_invaderAI` instances for Watery (Water), Whirling (Air), Blazing (Fire), and Thundering (Earth) invaders.

**`AddSC_elemental_invasions`**
Registers all eight scripts (four rifts, four invaders) with `ScriptMgr` via `Script/Script` and `ScriptMgr/RegisterSelf`. Called by `ScriptLoader/AddScripts`.

## Cross-Unit Boundaries

*   **`GameEventMgr.Main/IsActiveEvent`**: Called by `UpdateAI` (rift) and `JustDied` (invader) to ensure logic only runs during the active event.
*   **`ObjectMgr/GetSavedVariable` / `SetSavedVariable`**: Primary state synchronization. Invaders write kill counts; Rifts read counts to progress stages and write stage updates.
*   **`Map.Main/GetWalkRandomPosition`**: Used by `DoSpawn` to find valid pathing locations for new invaders.
*   **`WorldObject.Object/SummonCreature`**: Used by `DoSpawn` to create invader entities.
*   **`Creature.Main/SetDefaultMovementType` / `SetHomePosition` / `SetWanderDistance`**: Configure invader movement behavior in `DoSpawn`.
*   **`Unit.Main/GetMotionMaster` / `MotionMaster/Clear` / `MoveRandom`**: Initiate random wandering for invaders in `DoSpawn`.
*   **`Map.Main/GetCreature`**: Used by `UpdateAI` (rift) to check if tracked invaders are still alive.
*   **`CreatureAI/DoCastSpellIfCan` / `DoMeleeAttackIfReady`**: Execute combat actions in `UpdateAI#2` (invader).
*   **`Unit.Main/GetVictim` / `SelectHostileTarget` / `IsInRange`**: Manage targeting and range checks in `UpdateAI#2` (invader).
*   **`shared_Util/urand` / `frand`**: Provide randomization for timers and positions.

## Notable Implementation Details

*   **Forced Progression**: If players kill fewer than 50 invaders, the stage still advances after 1 hour (`m_uiIncreaseTimer`). This prevents the event from stalling indefinitely.
*   **Spawn Replacement**: Rifts track specific invader GUIDs. If an invader dies, the next `UpdateAI` cycle detects the missing GUID and spawns a replacement, maintaining the current stage's spawn count.
*   **Position Fallback**: `DoSpawn` retries finding a valid position up to 20 times. If it fails to find a spot >15 units away, it spawns the invader at the last attempted position, which may be invalid or too close to the rift.
*   **Boss Stage**: The code increments `spawnStage` until it reaches `STAGE_BOSS`. The actual boss spawn logic is not in this unit; it is likely triggered externally when the stage variable hits that value.

## Member Reference

**elemental_invasion_riftAI** (ctor): Initializes rift AI timers and stores element index. Inherits from `GameObjectAI`.

**DoSpawn**: Spawns an invader near the rift, finds a valid walkable position, configures random movement, and stores the GUID for tracking.

**UpdateAI**: Main rift loop. Checks event/zone status. Reads saved variables for kills/stage. Progresses stage if kills >= 50 or 1 hour passed. Replaces dead invaders to maintain spawn count.

**GetAI_go_elemental_invasion_rift_fire**: Factory for Fire rift AI.

**GetAI_go_elemental_invasion_rift_water**: Factory for Water rift AI.

**GetAI_go_elemental_invasion_rift_earth**: Factory for Earth rift AI.

**GetAI_go_elemental_invasion_rift_air**: Factory for Air rift AI.

**npc_invaderAI** (ctor): Initializes invader AI timers and stores element index. Inherits from `ScriptedAI`.

**Reset**: Randomizes spell timers to desynchronize casting.

**JustDied**: Increments global kill counter for its element in saved variables.

**UpdateAI#2**: Combat loop. Executes element-specific spells (Fire: Shield/Blast Wave; Air: Shield/Whirlwind; Earth: Knockdown/Shock; Water: Chilled/Frost Shock) based on timers/range. Performs melee attacks.

**GetAI_npc_watery_invader**: Factory for Water invader AI.

**GetAI_npc_whirling_invader**: Factory for Air invader AI.

**GetAI_npc_blazing_invader**: Factory for Fire invader AI.

**GetAI_npc_thundering_invader**: Factory for Earth invader AI.

**AddSC_elemental_invasions**: Registers all eight elemental invasion scripts with the script manager.

---

<!-- machine-true, projected from graph.json -->

## Map — elemental_invasions

*Source:* elemental_invasions.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| elemental_invasion_riftAI | ctor | GameObjectAI/GameObjectAI | — | — |
| DoSpawn | method | Creature.Main/SetDefaultMovementType, Creature.Main/SetHomePosition, Creature.Main/SetWanderDistance, Creature.MotionMaster/MoveRandom, Map.Main/GetWalkRandomPosition, MotionMaster/Clear, Object/GetObjectGuid, shared_Util/frand, Unit.Main/GetMotionMaster, Unit.Main/IsInCombat, WorldObject.Object/GetDistance2d#4, WorldObject.Object/GetMap, WorldObject.Object/GetPosition#2, WorldObject.Object/SummonCreature#2 | — | — |
| UpdateAI | method | GameEventMgr.Main/IsActiveEvent, Map.Main/GetCreature, ObjectMgr/GetSavedVariable, ObjectMgr/SetSavedVariable, WorldObject.Object/GetMap, WorldObject.Object/GetZoneId | — | — |
| GetAI_go_elemental_invasion_rift_fire | function | — | — | — |
| GetAI_go_elemental_invasion_rift_water | function | — | — | — |
| GetAI_go_elemental_invasion_rift_earth | function | — | — | — |
| GetAI_go_elemental_invasion_rift_air | function | — | — | — |
| npc_invaderAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset | method | shared_Util/urand | — | — |
| JustDied | method | GameEventMgr.Main/IsActiveEvent, ObjectMgr/GetSavedVariable, ObjectMgr/SetSavedVariable | — | — |
| UpdateAI#2 | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, shared_Util/urand, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget, WorldObject.Object/IsInRange | — | — |
| GetAI_npc_watery_invader | function | — | — | — |
| GetAI_npc_whirling_invader | function | — | — | — |
| GetAI_npc_blazing_invader | function | — | — | — |
| GetAI_npc_thundering_invader | function | — | — | — |
| AddSC_elemental_invasions | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
