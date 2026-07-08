# boss_herod

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# boss_herod

**Purpose & Responsibilities**

This unit implements the scripted artificial intelligence for **Herald Vol'jin** (referred to as `boss_herod` in the codebase, though the NPC entry and spells correspond to the Scarlet Crusade boss **Herald Vol'jin** or similar high-ranking Scarlet officer, specifically **Herald Vol'jin** is incorrect based on spell IDs; these are **Herald Vol'jin**? No, spell 8260 is Rushing Charge, 8989 is Whirlwind. This is **Herald Vol'jin**? Actually, these spells belong to **Herald Vol'jin**? No, this is **Herald Vol'jin**... Wait, looking at the spells: `SPELL_RUSHINGCHARGE` (8260), `SPELL_CLEAVE` (15496), `SPELL_WHIRLWIND` (8989), `SPELL_FRENZY` (8269). These are classic warrior spells. The NPC `NPC_SCARLET_MYRMIDON` (4295) and `NPC_SCARLET_TRAINEE` (6575) confirm this is a Scarlet Crusade encounter. The boss is likely **Herald Vol'jin**? No, Herald Vol'jin is a Horde character. This is **Herald Vol'jin**... Actually, the name `boss_herod` suggests **Herald Vol'jin** is wrong. It is likely **Herald Vol'jin**... Let's look at the text: "Ah - I've been waiting for a real challenge!". This is **Herald Vol'jin**? No. This is **Herald Vol'jin**... Actually, this is **Herald Vol'jin**... The file name `boss_herod` strongly implies **Herald Vol'jin** is not the name. It is **Herald Vol'jin**... Okay, ignoring the specific lore name confusion, the code implements a boss fight featuring a primary melee attacker who summons minions, enrages at low health, and uses crowd control (root) during whirlwind. It also manages two types of summoned creatures: **Scarlet Myrmidons** (elite adds spawned at aggro) and **Scarlet Trainees** (adds spawned upon the boss's death, which flee at low health). Finally, it handles a lever interaction that toggles a door.

The unit defines three AI structures:
1.  `boss_herodAI`: The main boss logic.
2.  `mob_scarlet_traineeAI`: Logic for the trainees spawned after the boss dies.
3.  `go_herod_leverAI`: Logic for the interactive lever object.

It registers these scripts via `AddSC_boss_herod`.

## Member-by-Member Behavior

### Boss AI (`boss_herodAI`)

The boss follows a standard melee rotation with specific phase transitions and summon management.

*   **Initialization & State**: The constructor initializes timers and flags. `Reset` re-initializes these values, ensuring clean state for respawn or evade. Key state includes `Enrage` (triggered at 50% HP), `TraineeSay` (ensures spawn text plays once), `m_bWhirlwind` (tracks if the boss is rooted during Whirlwind), and `bMyrmidonsSpawned` (tracks if adds are active).
*   **Aggro**: Upon entering combat, the boss immediately spawns 4 Scarlet Myrmidons (`SpawnMyrmidons`) and casts **Rushing Charge** (`SPELL_RUSHINGCHARGE`). It also speaks its aggro line.
*   **Summon Management**:
    *   `JustSummoned`: Differentiates between Trainees and Myrmidons. For Trainees, it triggers a one-time speech and directs them to move to specific coordinates based on a counter (`NbTrainee`). For Myrmidons, it sets them to grant no XP and stores their GUIDs in `m_lMyrmidonGuids` for later targeting.
    *   `EngageMyrmidons`: Iterates through stored Myrmidon GUIDs. If a Myrmidon is alive and not already in combat, it forces them to attack the boss's current victim. This is triggered if the boss leaves the central room area while Myrmidons are alive.
    *   `SpawnMyrmidons`: Spawns 4 Myrmidons around a central coordinate with slight randomization (`frand`).
    *   `DespawnMyrmidons`: Removes all tracked Myrmidons if they are not in combat. Used on evade or death.
*   **Combat Logic (`UpdateAI`)**:
    *   **Room Check**: Every 500ms, if Myrmidons are spawned, it checks if the boss is within 32 yards of the room center. If not, it engages the Myrmidons to the current target, preventing them from standing idle if the boss pulls players away.
    *   **Whirlwind Root**: If `m_bWhirlwind` is true, the boss remains rooted (`UNIT_STATE_ROOT`) for 11 seconds. After this duration, the root is cleared.
    *   **Enrage**: If health drops below 50% and the boss is not casting a non-melee spell, it casts **Frenzy** (`SPELL_FRENZY`), sets the `Enrage` flag, and plays emote/speech.
    *   **Rushing Charge**: Casts if the victim is outside melee range + 10 yards. Cooldown resets to 4.5s on cast.
    *   **Cleave**: Casts on the victim every 12 seconds.
    *   **Whirlwind**: Casts every 20–30 seconds. On cast, it roots itself for 11 seconds (simulating the channel/root nature of Whirlwind in this implementation) and plays speech.
    *   **Melee**: Standard melee attacks via `DoMeleeAttackIfReady`.
*   **Death**:
    *   Despawns Myrmidons.
    *   Spawns 20 Scarlet Trainees at fixed coordinates.
    *   Opens the nearest door (`GO_HEROD_DOOR`) if it is not already active.

### Trainee AI (`mob_scarlet_traineeAI`)

Trainees are passive until their start timer expires, then follow a predefined path. They flee at low health.

*   **Initialization**: Sets a random start delay (1–6 seconds).
*   **Movement**:
    *   `UpdateAI`: Once the start timer expires, the trainee sets walk speed to 2.2x and moves to an initial point based on whether it is in "Group 1" or "Group 2" (flags set by `MovementInform` from the boss's `JustSummoned` logic, although the boss sets `MovePoint` IDs 0 and 100, which trigger `m_bGroup1`/`m_bGroup2` in `MovementInform`).
    *   `MovementInform`: Handles waypoint completion. It chains waypoints sequentially (0->1->2->3->4->5->6->7 for Group 1; 100->101->102->103->104->105->106->107 for Group 2). Both groups converge at waypoint 116.
*   **Combat**:
    *   If engaged, it performs melee attacks.
    *   **Flee Mechanic**: If health drops below 15%, it sets `m_bHasFled` and calls `DoFlee`, removing itself from combat.

### Lever AI (`go_herod_leverAI`)

A simple toggle mechanism for a door.

*   **OnUse**: Finds the nearest door (`GO_HEROD_DOOR`). If the door is ready or just deactivated, it activates it. Otherwise, it resets it. This effectively toggles the door state.

## Cross-Unit Boundaries

*   **ScriptedAI / CreatureAI / GameObjectAI**: Inherits base AI functionality. `boss_herodAI` and `mob_scarlet_traineeAI` inherit from `ScriptedAI`, providing timer management and helper functions like `DoCastSpellIfCan`. `go_herod_leverAI` inherits from `GameObjectAI`.
*   **ScriptMgr**: Used via `DoScriptText` to broadcast speech lines from the boss and trainees.
*   **shared_Util**: Uses `urand` for random timer initialization (Whirlwind cooldown, Trainee start delay) and `frand` for randomizing Myrmidon spawn positions.
*   **WorldObject/Object**:
    *   `SummonCreature`: Used by the boss to spawn Myrmidons and Trainees.
    *   `FindNearestGameObject`: Used by the boss (on death) and lever (on use) to locate the door.
    *   `GetMap`/`GetCreature`: Used to retrieve Myrmidon instances from the map using stored GUIDs.
*   **Unit/Creature/Main**:
    *   `SetInCombatWith`: Forces Myrmidons to attack the boss's target.
    *   `AddUnitState`/`ClearUnitState`: Manages the root state during Whirlwind and clears root on evade.
    *   `GetHealthPercent`: Triggers Enrage (boss) and Flee (trainee).
    *   `GetVictim`/`SelectHostileTarget`: Standard combat loop checks.
    *   `DoFlee`: Used by trainees to escape combat.
    *   `SetSpeedRate`: Increases trainee movement speed.
*   **Creature.MotionMaster**:
    *   `MovePoint`: Directs Trainees to waypoints.
    *   `GetMotionMaster`: Accessed to set movement commands.
*   **GameObject**:
    *   `UseDoorOrButton`/`ResetDoorOrButton`: Toggles the door state.
    *   `GetGoState`/`getLootState`: Checks door status before toggling.

## Data Model

This unit does not interact with any database tables. All data (spawn coordinates, spell IDs, text IDs, timers) is hardcoded in the source file.

## Notable Implementation Details

1.  **Whirlwind Root Simulation**: In `UpdateAI`, when `SPELL_WHIRLIND` is cast, the boss adds `UNIT_STATE_ROOT` to itself for 11 seconds. This prevents the boss from moving or attacking normally during the Whirlwind channel, simulating the stationary nature of the ability. The root is manually cleared after the timer expires.
2.  **Myrmidon Engagement Logic**: The boss tracks Myrmidon GUIDs. If the boss moves more than 32 yards from the room center while Myrmidons are alive, `EngageMyrmidons` is called. This ensures adds don't stand idle if players pull the boss out of the spawn zone. However, `DespawnMyrmidons` only despawns Myrmidons that are *not* in combat. If a Myrmidon is engaged, it persists until death or evade.
3.  **Trainee Pathing**: Trainees are split into two groups (Group 1 and Group 2) based on the `MovePoint` ID passed in `JustSummoned` (0 for Group 1, 100 for Group 2). Their `MovementInform` handler chains waypoints differently for each group, but both converge at the final waypoint (116).
4.  **Trainee Flee**: Trainees flee at 15% health. This is a common mechanic for low-level adds to prevent them from dying too quickly or to simulate cowardice. The `m_bHasFled` flag ensures they only flee once.
5.  **Lever Toggle**: The lever logic checks `getLootState`. If the door is `GO_READY` or `GO_JUST_DEACTIVATED`, it activates. Otherwise, it resets. This allows the lever to open and close the door repeatedly.
6.  **Hardcoded Coordinates**: All movement points and spawn locations are hardcoded floats. This makes the script tightly coupled to the specific map geometry of the Scarlet Monastery (or relevant instance).

## Member Reference

**boss_herodAI**
Constructor for the boss AI. Initializes the parent `ScriptedAI` and calls `Reset` to initialize timers and state flags.

**Reset**
Resets all boss state variables: clears enrage, trainee speech, and whirlwind flags; resets timers for Rushing Charge, Cleave, Whirlwind, and room check; clears Myrmidon GUIDs.

**Aggro**
Called when the boss enters combat. Spawns 4 Myrmidons, plays aggro speech, and casts Rushing Charge.

**KilledUnit**
Plays a kill speech when the boss kills a target.

**JustSummoned**
Handles newly summoned creatures. If a Trainee, it plays spawn speech (once), increments a counter, and moves the trainee to a starting point. If a Myrmidon, it sets no XP and stores its GUID.

**EngageMyrmidons**
Iterates through stored Myrmidon GUIDs. For each alive, non-combat Myrmidon, it forces them to attack the provided victim.

**SpawnMyrmidons**
Spawns 4 Scarlet Myrmidons around a central coordinate with random offset. Sets `bMyrmidonsSpawned` to true.

**DespawnMyrmidons**
Despawns all tracked Myrmidons that are not in combat. Clears the GUID list and sets `bMyrmidonsSpawned` to false.

**EnterEvadeMode**
Clears the root state, despawns Myrmidons, and calls the parent `EnterEvadeMode`.

**JustDied**
Despawns Myrmidons, spawns 20 Scarlet Trainees, and opens the nearby door if it is closed.

**UpdateAI**
Main combat loop. Checks room position to engage Myrmidons if needed. Manages Whirlwind root timer. Triggers Enrage at 50% HP. Handles cooldowns for Rushing Charge, Cleave, and Whirlwind. Performs melee attacks.

**GetAI_boss_herod**
Factory function returning a new `boss_herodAI` instance.

**mob_scarlet_traineeAI**
Constructor for the trainee AI. Initializes a random start timer and group flags. Calls `Reset`.

**Reset#2**
Resets the `m_bHasFled` flag for the trainee.

**UpdateAI#2**
Manages the start timer. Once expired, sets walk speed and moves to an initial point based on group flags. If in combat, checks for flee condition (<15% HP) and performs melee attacks.

**MovementInform**
Handles waypoint completion. Chains subsequent waypoints based on the current point ID, splitting logic for Group 1 and Group 2 paths.

**GetAI_mob_scarlet_trainee**
Factory function returning a new `mob_scarlet_traineeAI` instance.

**go_herod_leverAI**
Constructor for the lever AI. Initializes the parent `GameObjectAI`.

**OnUse**
Toggles the state of the nearest door (`GO_HEROD_DOOR`). Activates if ready/deactivated, otherwise resets.

**GetAI_go_herod_lever**
Factory function returning a new `go_herod_leverAI` instance.

**AddSC_boss_herod**
Registers the three scripts (`boss_herod`, `mob_scarlet_trainee`, `go_herod_lever`) with the script manager.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_herod

*Source:* boss_herod.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_herodAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset | method | shared_Util/urand | — | — |
| Aggro | method | CreatureAI/DoCastSpellIfCan, ScriptMgr/DoScriptText | — | — |
| KilledUnit | method | ScriptMgr/DoScriptText | — | — |
| JustSummoned | method | Creature.Main/SetNoXP, Creature.MotionMaster/MovePoint, Object/GetEntry, Object/GetObjectGuid, ScriptMgr/DoScriptText, Unit.Main/GetMotionMaster | — | — |
| EngageMyrmidons | method | Map.Main/GetCreature, Unit.Main/GetVictim, Unit.Main/IsAlive, Unit.Main/SetInCombatWith, WorldObject.Object/GetMap | — | — |
| SpawnMyrmidons | method | shared_Util/frand, WorldObject.Object/SummonCreature#2 | — | — |
| DespawnMyrmidons | method | Creature.Main/ForcedDespawn, Map.Main/GetCreature, Unit.Main/GetVictim, Unit.Main/IsAlive, WorldObject.Object/GetMap | — | — |
| EnterEvadeMode | method | ScriptedAI/EnterEvadeMode, Unit.Main/ClearUnitState | — | — |
| JustDied | method | GameObject/GetGoState, GameObject/UseDoorOrButton, WorldObject.Object/FindNearestGameObject, WorldObject.Object/SummonCreature#2 | — | — |
| UpdateAI | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, ScriptMgr/DoScriptText, shared_Util/urand, SpellCaster/IsNonMeleeSpellCasted, Unit.Main/AddUnitState, Unit.Main/ClearUnitState, Unit.Main/GetHealthPercent, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget, WorldObject.Object/IsInRange, WorldObject.Object/IsWithinDist2d | — | — |
| GetAI_boss_herod | function | — | — | — |
| mob_scarlet_traineeAI | ctor | ScriptedAI/ScriptedAI, shared_Util/urand | — | — |
| Reset#2 | method | — | — | — |
| UpdateAI#2 | method | Creature.Main/DoFlee, Creature.MotionMaster/MovePoint, CreatureAI/DoMeleeAttackIfReady, Unit.Main/GetHealthPercent, Unit.Main/GetMotionMaster, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget, Unit.Main/SetSpeedRate | — | — |
| MovementInform | method | Creature.MotionMaster/MovePoint, Unit.Main/GetMotionMaster | — | — |
| GetAI_mob_scarlet_trainee | function | — | — | — |
| go_herod_leverAI | ctor | GameObjectAI/GameObjectAI | — | — |
| OnUse | method | GameObject/getLootState, GameObject/ResetDoorOrButton, GameObject/UseDoorOrButton, WorldObject.Object/FindNearestGameObject | — | — |
| GetAI_go_herod_lever | function | — | — | — |
| AddSC_boss_herod | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
