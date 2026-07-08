# arathi_highlands

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# arathi_highlands

**Purpose & Responsibilities**  
`arathi_highlands.cpp` implements scripted AI and quest-accept hooks for four entities in the Arathi Highlands zone: two escort NPCs (`npc_professor_phizzlethorpe`, `npc_kinelory`), one event-triggering NPC (`npc_shakes_o_breen`), one interactable game object (`go_arathi_cannon_fire`), and one spell-targeting filter (`DeathFromBelowCannonFire`). All escort AI classes inherit from `npc_escortAI` (defined elsewhere) and rely on its waypoint system, player-tracking, and state management. The unit contains no database queries or table interactions.

## Member-by-Member Behavior

### Professor Phizzlethorpe Escort (`npc_professor_phizzlethorpe`)
This AI handles the escort portion of quest 665 ("Sunken Treasure"). It plays dialogue at specific waypoints, summons two temporary enemies at waypoint 9, and completes the quest at waypoint 20.

- **`npc_professor_phizzlethorpeAI` (ctor)**: Initializes the base `npc_escortAI` and calls `Reset`.
- **`Reset#2`**: Empty override; relies on base class reset behavior.
- **`WaypointReached#2`**: Core progression logic. At waypoint 4–3, 5–3, 8–4, 10–5, 11–6, 19–7, 20–8/9 it triggers dialogue via `ScriptMgr/DoScriptText`. At waypoint 9 it summons two `ENTRY_VENGEFUL_SURGE` creatures with 10-minute despawn timers. At waypoint 11 it sets the escort to run mode. At waypoint 20 it marks the quest complete for the player via `Player.Main/GroupEventHappens`.
- **`Aggro#2`**: Plays aggro dialogue via `ScriptMgr/DoScriptText`.
- **`JustSummoned`**: Forces the summoned creature to attack the professor immediately via `CreatureAI/AttackStart`.
- **`QuestAccept_npc_professor_phizzlethorpe`**: Sets the creature’s faction to neutral-passive, plays initial dialogue, and starts the escort via `ScriptedEscortAI/Start`.
- **`GetAI_npc_professor_phizzlethorpe`**: Factory function returning a new AI instance.

### Shakes O'Breen Event (`npc_shakes_o_breen`)
This AI manages a defensive wave-based event for quest 667 ("Death From Below"). It spawns waves of enemies at fixed coordinates, tracks alive counts, checks player proximity, and completes or fails the event accordingly.

- **`npc_shakes_o_breenAI` (ctor)**: Initializes base AI, sets a 3-second player check timer, and calls `Reset`.
- **`Reset#3`**: Resets wave ID and alive count if not actively escorting. Re-enables questgiver flag if event is not active. Uses `ScriptedEscortAI/HasEscortState` and `WorldObject.Object/SetFlag`.
- **`WaypointReached#3`**: Empty override; this NPC does not move via waypoints during the event.
- **`DoSummon`**: Helper to summon a creature at one of four predefined coordinate sets using `WorldObject.Object/SummonCreature#2`.
- **`DoWaveSummon`**: Increments wave ID. Wave 1 and 3 spawn two raiders and one sorceress; wave 2 spawns two raiders. Plays a yell at wave 3 via `WorldObject.Object/MonsterSay#2`.
- **`FinishEvent`**: On success, triggers quest completion via `Player.Main/GroupEventHappens`. On failure, kills the NPC via `Creature.Main/DisappearAndDie`. Clears escort states via `ScriptedEscortAI/RemoveEscortState`.
- **`JustSummoned#2`**: First raider of wave 1 yells. Increments alive counter. Sets no XP, clears motion, disables walking, and moves the summon to a central point via `Unit.Main/GetMotionMaster` and `Creature.MotionMaster/MovePoint`.
- **`SummonedCreatureJustDied`**: Decrements alive counter and clears loot via `Loot/clear`.
- **`SummonedMovementInform`**: When a summon reaches its destination (point 0), adds mutual threat between the summon and Shakes via `Unit.Main/AddThreat`.
- **`UpdateEscortAI#2`**: Main update loop. Checks player range every 3 seconds; fails event if player is missing or >150 yards away. Spawns waves every 20 seconds until wave 3. After wave 3, waits for all summons to die before completing the event. Delegates to base `ScriptedEscortAI/UpdateEscortAI`.
- **`QuestAccept_npc_shakes_o_breen`**: Starts the escort, pauses it immediately via `ScriptedEscortAI/SetEscortPaused`, plays initial yell, and sets orientation.
- **`GetAI_npc_shakes_o_breen`**: Factory function.

### Kinelory Escort (`npc_kinelory`)
This AI handles the escort portion of quest 660 ("Hints of a New Plague"). It plays dialogue at waypoints, uses bear form and healing spells during combat, and completes the quest upon reaching the final waypoint.

- **`npc_kineloryAI` (ctor)**: Initializes base AI and calls `Reset`.
- **`Reset`**: Randomizes bear form and heal timers using `shared_Util/urand`.
- **`JustRespawned`**: Sets immune-to-NPC flag via `WorldObject.Object/SetFlag` and calls base respawn handler.
- **`WaypointReached`**: Plays dialogue at waypoints 9, 16, 17, 18, 33, 34. At waypoint 18 it faces the player and runs. At waypoint 33 it faces NPC Quae if nearby. At waypoint 34 it completes the quest via `Player.Main/GroupEventHappens`.
- **`Aggro`**: Plays special dialogue if aggroed by Jorell; otherwise 10% chance to play random aggro line. Uses `ScriptMgr/DoScriptText` and `shared_Util/roll_chance_i`.
- **`UpdateEscortAI`**: Combat logic. Casts bear form on cooldown if no victim. Casts rejuvenation if health <80%. Performs melee attacks. Uses `CreatureAI/DoCastSpellIfCan`, `CreatureAI/DoMeleeAttackIfReady`, `Unit.Main/GetHealthPercent`, etc.
- **`GetAI_npc_kinelory`**: Factory function.
- **`QuestAccept_npc_kinelory`**: Removes immune flag, plays start dialogue, and begins escort via `ScriptedEscortAI/Start`.

### Arathi Cannon Fire Game Object (`go_arathi_cannon_fire`)
A simple game object AI that disables itself on use.

- **`go_arathi_cannon_fireAI` (ctor)**: Initializes base `GameObjectAI`.
- **`OnUse`**: Returns true to indicate successful use, effectively disabling the trap. No side effects.
- **`GetAIgo_arathi_cannon_fire`**: Factory function.

### Death From Below Cannon Fire Spell Filter
A spell script that prevents the spell from targeting players or their pets/charmers.

- **`OnCheckTarget`**: Returns false if the target is a player, charmed by a player, or owned by a player, using `Unit.Main/IsCharmerOrOwnerPlayerOrPlayerItself`.
- **`GetScript_DeathFromBelowCannonFire`**: Factory function returning the spell script.

### Script Registration
- **`AddSC_arathi_highlands`**: Registers all five scripts with the `ScriptMgr` via `ScriptMgr/RegisterSelf`. Called by `ScriptLoader/AddScripts`.

## Cross-Unit Boundaries

All cross-unit calls are to core engine modules:
- **`ScriptedEscortAI`**: Used by all three escort AIs for waypoint management, player tracking, state flags, and base update loops.
- **`ScriptMgr`**: Used for dialogue playback (`DoScriptText`).
- **`Player.Main`**: Used for quest completion (`GroupEventHappens`).
- **`WorldObject.Object`**: Used for summoning, flagging, orientation, and movement.
- **`CreatureAI` / `Creature.Main`**: Used for combat actions, AI initialization, and death handling.
- **`Unit.Main`**: Used for threat, health, targeting, and motion control.
- **`shared_Util`**: Used for random number generation.
- **`GridSearchers`**: Used to find nearby NPCs.
- **`Loot`**: Used to clear loot data.
- **`ScriptLoader`**: Calls `AddSC_arathi_highlands` to register scripts.

No other custom scripts or modules are referenced.

## Data Model

This unit performs no database operations. No tables are accessed.

## Notable Implementation Details

- **Shakes O'Breen Proximity Check**: The event fails if the player is not within 150 yards for more than 3 seconds. This is enforced in `UpdateEscortAI#2` via a timer and `IsInRange` check.
- **Summon Movement**: Summons in Shakes’ event are moved to a central point via `MovePoint` with pathfinding enabled. They only gain threat after arriving, handled in `SummonedMovementInform`.
- **Kinelory Immunity**: Kinelory is immune to NPC damage upon respawn (`UNIT_FLAG_IMMUNE_TO_NPC`) until the quest is accepted, at which point the flag is removed.
- **Professor Phizzlethorpe Summons**: The two Vengeful Surge summons are set to despawn after 10 minutes or upon death. They immediately attack the professor.
- **Spell Targeting Filter**: The cannon fire spell explicitly excludes players and their controlled entities from being targeted, preventing accidental self-harm or pet damage.

## Member Reference

**npc_professor_phizzlethorpeAI** (ctor): Initializes base escort AI and calls Reset.  
**Reset#2**: Empty override; relies on base class behavior.  
**WaypointReached#2**: Triggers dialogue, summons enemies at waypoint 9, sets run mode at 11, completes quest at 20. Calls `Player.Main/GroupEventHappens`, `ScriptedEscortAI/GetPlayerForEscort`, `ScriptedEscortAI/SetRun`, `ScriptMgr/DoScriptText`, `WorldObject.Object/SummonCreature#2`.  
**Aggro#2**: Plays aggro dialogue via `ScriptMgr/DoScriptText`.  
**JustSummoned**: Forces summoned creature to attack the professor via `CreatureAI/AttackStart`.  
**QuestAccept_npc_professor_phizzlethorpe**: Sets faction, plays dialogue, starts escort. Calls `Creature.Main/AI`, `Object/GetGUID`, `QuestDef/GetQuestId`, `ScriptedEscortAI/Start`, `ScriptMgr/DoScriptText`, `Unit.Main/SetFactionTemplateId`.  
**GetAI_npc_professor_phizzlethorpe**: Factory function for the AI.  
**npc_shakes_o_breenAI** (ctor): Initializes base AI, sets player check timer, calls Reset.  
**Reset#3**: Resets wave/alive counters if not escorting; re-enables questgiver flag. Calls `ScriptedEscortAI/HasEscortState`, `WorldObject.Object/SetFlag`.  
**WaypointReached#3**: Empty override; no waypoint logic.  
**DoSummon**: Helper to summon creatures at predefined coordinates. Calls `WorldObject.Object/SummonCreature#2`.  
**DoWaveSummon**: Spawns wave-specific enemies; plays yell at wave 3. Calls `WorldObject.Object/MonsterSay#2`.  
**FinishEvent**: Completes or fails the event. Calls `Creature.Main/DisappearAndDie`, `Player.Main/GroupEventHappens`, `ScriptedEscortAI/GetPlayerForEscort`, `ScriptedEscortAI/RemoveEscortState`.  
**JustSummoned#2**: Handles summon arrival; sets no XP, moves to center, disables walking. Calls `Creature.Main/SetNoXP`, `Creature.MotionMaster/MovePoint`, `MotionMaster/Clear`, `Object/GetEntry`, `Unit.Main/GetMotionMaster`, `Unit.Main/SetWalk`, `WorldObject.Object/MonsterYell#2`.  
**SummonedCreatureJustDied**: Decrements alive count, clears loot. Calls `Loot/clear`.  
**SummonedMovementInform**: Adds mutual threat when summon arrives. Calls `Unit.Main/AddThreat`.  
**UpdateEscortAI#2**: Main update loop; checks player range, spawns waves, completes event. Calls `ScriptedEscortAI/GetPlayerForEscort`, `ScriptedEscortAI/HasEscortState`, `ScriptedEscortAI/UpdateEscortAI`, `WorldObject.Object/IsInRange`.  
**QuestAccept_npc_shakes_o_breen**: Starts and pauses escort, plays yell, sets orientation. Calls `Creature.Main/AI`, `Object/GetGUID`, `QuestDef/GetQuestId`, `ScriptedEscortAI/SetEscortPaused`, `ScriptedEscortAI/Start`, `WorldObject.Object/MonsterYell#2`, `WorldObject.Object/SetOrientation`.  
**GetAI_npc_shakes_o_breen**: Factory function for the AI.  
**npc_kineloryAI** (ctor): Initializes base AI and calls Reset.  
**Reset**: Randomizes bear form and heal timers. Calls `shared_Util/urand`.  
**JustRespawned**: Sets immune flag, calls base respawn. Calls `ScriptedEscortAI/JustRespawned`, `WorldObject.Object/SetFlag`.  
**WaypointReached**: Plays dialogue, faces player/NPC, completes quest. Calls `GridSearchers/GetClosestCreatureWithEntry`, `Player.Main/GroupEventHappens`, `ScriptedEscortAI/GetPlayerForEscort`, `ScriptedEscortAI/SetRun`, `ScriptMgr/DoScriptText`, `Unit.Main/SetFacingToObject`.  
**Aggro**: Plays conditional aggro dialogue. Calls `Object/GetEntry`, `ScriptMgr/DoScriptText`, `shared_Util/roll_chance_i`.  
**UpdateEscortAI**: Combat logic; casts spells, performs melee. Calls `CreatureAI/DoCastSpellIfCan`, `CreatureAI/DoMeleeAttackIfReady`, `shared_Util/urand`, `Unit.Main/GetHealthPercent`, `Unit.Main/GetVictim`, `Unit.Main/SelectHostileTarget`.  
**GetAI_npc_kinelory**: Factory function for the AI.  
**QuestAccept_npc_kinelory**: Removes immunity, plays dialogue, starts escort. Calls `Creature.Main/AI`, `Object/GetGUID`, `QuestDef/GetQuestId`, `ScriptedEscortAI/Start`, `ScriptMgr/DoScriptText`, `WorldObject.Object/RemoveFlag`.  
**go_arathi_cannon_fireAI** (ctor): Initializes base GameObject AI.  
**OnUse**: Returns true to disable the trap.  
**GetAIgo_arathi_cannon_fire**: Factory function for the GameObject AI.  
**OnCheckTarget**: Filters out players and their controlled entities. Calls `Unit.Main/IsCharmerOrOwnerPlayerOrPlayerItself`.  
**GetScript_DeathFromBelowCannonFire**: Factory function for the spell script.  
**AddSC_arathi_highlands**: Registers all scripts with the ScriptMgr. Called by `ScriptLoader/AddScripts`. Calls `Script/Script`, `ScriptMgr/RegisterSelf`.

---

<!-- machine-true, projected from graph.json -->

## Map — arathi_highlands

*Source:* arathi_highlands.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| npc_professor_phizzlethorpeAI | ctor | ScriptedEscortAI/npc_escortAI | — | — |
| Reset#2 | method | — | — | — |
| WaypointReached#2 | method | Player.Main/GroupEventHappens, ScriptedEscortAI/GetPlayerForEscort, ScriptedEscortAI/SetRun, ScriptMgr/DoScriptText, WorldObject.Object/SummonCreature#2 | — | — |
| Aggro#2 | method | ScriptMgr/DoScriptText | — | — |
| JustSummoned | method | Creature.Main/AI, CreatureAI/AttackStart | — | — |
| QuestAccept_npc_professor_phizzlethorpe | function | Creature.Main/AI, Object/GetGUID, QuestDef/GetQuestId, ScriptedEscortAI/Start, ScriptMgr/DoScriptText, Unit.Main/SetFactionTemplateId | — | — |
| GetAI_npc_professor_phizzlethorpe | function | — | — | — |
| npc_shakes_o_breenAI | ctor | ScriptedEscortAI/npc_escortAI | — | — |
| Reset#3 | method | ScriptedEscortAI/HasEscortState, WorldObject.Object/SetFlag | — | — |
| WaypointReached#3 | method | — | — | — |
| DoSummon | method | WorldObject.Object/SummonCreature#2 | — | — |
| DoWaveSummon | method | WorldObject.Object/MonsterSay#2 | — | — |
| FinishEvent | method | Creature.Main/DisappearAndDie, Player.Main/GroupEventHappens, ScriptedEscortAI/GetPlayerForEscort, ScriptedEscortAI/RemoveEscortState | — | — |
| JustSummoned#2 | method | Creature.Main/SetNoXP, Creature.MotionMaster/MovePoint, MotionMaster/Clear, Object/GetEntry, Unit.Main/GetMotionMaster, Unit.Main/SetWalk, WorldObject.Object/MonsterYell#2 | — | — |
| SummonedCreatureJustDied | method | Loot/clear | — | — |
| SummonedMovementInform | method | Unit.Main/AddThreat | — | — |
| UpdateEscortAI#2 | method | ScriptedEscortAI/GetPlayerForEscort, ScriptedEscortAI/HasEscortState, ScriptedEscortAI/UpdateEscortAI, WorldObject.Object/IsInRange | — | — |
| QuestAccept_npc_shakes_o_breen | function | Creature.Main/AI, Object/GetGUID, QuestDef/GetQuestId, ScriptedEscortAI/SetEscortPaused, ScriptedEscortAI/Start, WorldObject.Object/MonsterYell#2, WorldObject.Object/SetOrientation | — | — |
| GetAI_npc_shakes_o_breen | function | — | — | — |
| npc_kineloryAI | ctor | ScriptedEscortAI/npc_escortAI | — | — |
| Reset | method | shared_Util/urand | — | — |
| JustRespawned | method | ScriptedEscortAI/JustRespawned, WorldObject.Object/SetFlag | — | — |
| WaypointReached | method | GridSearchers/GetClosestCreatureWithEntry, Player.Main/GroupEventHappens, ScriptedEscortAI/GetPlayerForEscort, ScriptedEscortAI/SetRun, ScriptMgr/DoScriptText, Unit.Main/SetFacingToObject | — | — |
| Aggro | method | Object/GetEntry, ScriptMgr/DoScriptText, shared_Util/roll_chance_i | — | — |
| UpdateEscortAI | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, shared_Util/urand, Unit.Main/GetHealthPercent, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_npc_kinelory | function | — | — | — |
| QuestAccept_npc_kinelory | function | Creature.Main/AI, Object/GetGUID, QuestDef/GetQuestId, ScriptedEscortAI/Start, ScriptMgr/DoScriptText, WorldObject.Object/RemoveFlag | — | — |
| go_arathi_cannon_fireAI | ctor | GameObjectAI/GameObjectAI | — | — |
| OnUse | method | — | — | — |
| GetAIgo_arathi_cannon_fire | function | — | — | — |
| OnCheckTarget | method | Unit.Main/IsCharmerOrOwnerPlayerOrPlayerItself | — | — |
| GetScript_DeathFromBelowCannonFire | function | — | — | — |
| AddSC_arathi_highlands | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
