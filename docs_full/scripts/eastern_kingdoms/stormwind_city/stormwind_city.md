# stormwind_city

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# stormwind_city

**Purpose & Responsibilities**  
`stormwind_city.cpp` implements scripted AI and quest-accept hooks for two specific non-player characters (NPCs) in Stormwind City: **Bartleby** (quest 1640, “Beat Bartleby”) and **Dashel Stonefist** (quest 1447, “Missing Diplomat Part 8”). The unit provides:

1. **`npc_bartlebyAI`**: A simple combat AI that tracks Bartleby’s normal faction, engages attackers, and triggers quest completion when Bartleby is reduced below 15% health.
2. **`npc_dashel_stonefistAI`**: A complex, phased event AI that manages a multi-stage dialogue and combat sequence involving Dashel and two summoned thugs. It handles quest start, combat interruption, dialogue progression, thug dismissal, and quest completion or failure.
3. **Quest accept functions**: `QuestAccept_npc_bartleby` and `QuestAccept_npc_dashel_stonefist` initiate the respective events when players accept the quests.
4. **Script registration**: `AddSC_stormwind_city` registers these scripts with the server’s script manager.

The unit does **not** interact with any database tables. All logic is driven by in-memory state, creature summoning, and player/creature interactions.

---

## Member-by-Member Behavior

### Bartleby Subsystem (`npc_bartlebyAI`)

#### **npc_bartlebyAI** (ctor)  
Initializes the AI for Bartleby. It stores Bartleby’s normal faction ID via `Unit.Main/GetFactionTemplateId` and calls `Reset()` to ensure the faction is correctly set. Inherits from `ScriptedAI/ScriptedAI`.

#### **Reset**  
Restores Bartleby’s faction to its normal value if it has been changed (e.g., to hostile during the quest). Uses `Unit.Main/GetFactionTemplateId` and `Unit.Main/SetFactionTemplateId`.

#### **AttackedBy**  
Triggers combat if Bartleby is attacked by a hostile unit. Checks if the attacker is valid, if Bartleby already has a victim (`Unit.Main/GetVictim`), or if the attacker is friendly (`Unit.Main/IsFriendlyTo`). If none apply, starts combat via `CreatureAI/AttackStart`.

#### **DamageTaken**  
Handles damage events. If Bartleby’s health drops below 15% or the incoming damage would kill him, he evades combat (`ScriptedAI/EnterEvadeMode`). If the attacker is a player (`Object/GetTypeId`), the player receives quest credit via `Player.Main/AreaExploredOrEventHappens`. The damage is zeroed to prevent death.

#### **QuestAccept_npc_bartleby**  
Called when a player accepts quest 1640. Sets Bartleby’s faction to hostile (`FACTION_ENEMY`) via `Unit.Main/SetFactionTemplateId`, then initiates combat against the player using `CreatureAI/AttackStart`. Validates inputs and checks `QuestDef/GetQuestId`.

#### **GetAI_npc_bartleby**  
Factory function that creates and returns a new `npc_bartlebyAI` instance for Bartleby.

#### **startQuestFight**  
A trivial inline method that sets `m_questFightStarted = true`.

---

### Dashel Stonefist Subsystem (`npc_dashel_stonefistAI`)

#### **npc_dashel_stonefistAI** (ctor)  
Initializes the AI for Dashel. Sets `m_questFightStarted = false` and calls `Reset()` to initialize state. Inherits from `ScriptedAI/ScriptedAI`.

#### **AttackedBy#2**  
Similar to Bartleby’s `AttackedBy`. Starts combat if Dashel is attacked by a hostile unit, using `CreatureAI/AttackStart`, `Unit.Main/GetVictim`, and `Unit.Main/IsFriendlyTo`.

#### **Reset#2**  
Resets Dashel’s state. If a quest fight was active, it marks the quest as failed for the player (`Player.Main/GroupEventFailHappens`) and unsummons any remaining thugs via `TemporarySummon/UnSummon`. Clears thug GUIDs (`ObjectGuid/Clear`), resets flags (`WorldObject.Object/SetFlag`), restores faction (`Unit.Main/SetFactionTemplateId`), and clears the player GUID. Uses `Map.Main/GetCreature`, `Map.Main/GetPlayer`, `WorldObject.Object/GetMap`, and `Unit.Main/IsAlive`.

#### **DamageTaken#2**  
If a quest fight is active and Dashel’s health drops below 20%, he stops combat (`Unit.Main/CombatStop`), removes auras (`Unit.Main/RemoveAllAuras`), sets faction to friendly (`Unit.Main/SetFactionTemplateId`), deletes threat lists (`Unit.Main/DeleteThreatList`), and returns home (`Creature.MotionMaster/MoveTargetedHome`). Thugs are similarly pacified. Dialogue begins via `ScriptMgr/DoScriptText`. Uses `Map.Main/GetCreature`, `Unit.Main/GetHealth`, `Unit.Main/GetHealthPercent`, `Unit.Main/GetMotionMaster`, `Unit.Main/IsAlive`, and `WorldObject.Object/GetMap`.

#### **UpdateAI**  
Manages the phased dialogue/event sequence. Uses a timer (`m_nextPhaseDelayTimer`) to progress through phases:
- **MDQP_SAY1–SAY3**: Dashel and thugs exchange dialogue via `ScriptMgr/DoScriptText`.
- **MDQP_THUG_WALK_AWAY_1/2**: Thugs move to reset positions and despawn via `ResetThug`.
- **MDQP_QUEST_COMPLETE**: Marks the quest as complete for the player (`Player.Main/GroupEventHappens`) and resets the AI.
Uses `Map.Main/GetCreature`, `Map.Main/GetPlayer`, `WorldObject.Object/GetMap`, `Unit.Main/IsAlive`, and `BasicAI/UpdateAI` for the default case.

#### **ResetThug**  
Moves a thug to a predefined position (`aThugResetPosition`) and schedules despawning via `Creature.Main/DespawnOrUnsummon`. Uses `Creature.MotionMaster/MovePoint`, `Map.Main/GetCreature`, `Unit.Main/GetMotionMaster`, `Unit.Main/IsAlive`, and `WorldObject.Object/GetMap`.

#### **JustReachedHome**  
When Dashel returns to his spawn point, it sets the next dialogue phase based on whether thugs are still alive. No external calls.

#### **JustDied**  
If Dashel dies unexpectedly (e.g., GM command), it unsummons thugs via `TemporarySummon/UnSummon` and clears GUIDs. Uses `Map.Main/GetCreature`, `ObjectGuid/Clear`, and `WorldObject.Object/GetMap`.

#### **SummonedCreatureJustDied**  
Clears the GUID of a thug that has died, preventing dangling pointers. Uses `Object/GetObjectGuid`, `ObjectGuid/Clear`, and `ObjectGuid/operator==`.

#### **SummonedCreatureDespawn**  
Clears the GUID of a thug that has despawned. Uses `Object/GetObjectGuid`, `ObjectGuid/Clear`, and `ObjectGuid/operator==`.

#### **QuestAccept_npc_dashel_stonefist**  
Initiates the quest event. Sets Dashel’s faction to neutral, removes questgiver flag, spawns two thugs via `WorldObject.Object/SummonCreature#2`, and starts combat. Records the player’s GUID and sets `m_questFightStarted`. Uses `Creature.Main/AI`, `CreatureAI/AttackStart`, `Object/GetObjectGuid`, `QuestDef/GetQuestId`, `ScriptMgr/DoScriptText`, `Unit.Main/SetFactionTemplateId`, and `WorldObject.Object/RemoveFlag`.

#### **GetAI_npc_dashel_stonefist**  
Factory function that creates and returns a new `npc_dashel_stonefistAI` instance for Dashel.

---

### Script Registration

#### **AddSC_stormwind_city**  
Registers both NPCs’ scripts with the server. Creates `Script` objects, assigns names, AI getters, and quest accept handlers, then calls `ScriptMgr/RegisterSelf`. Called by `ScriptLoader/AddScripts`.

---

## Cross-Unit Boundaries

### Bartleby
- **Calls out**:  
  - `ScriptedAI/ScriptedAI` (inheritance).  
  - `Unit.Main/GetFactionTemplateId` and `SetFactionTemplateId` (faction management).  
  - `CreatureAI/AttackStart` (combat initiation).  
  - `Unit.Main/GetVictim` and `IsFriendlyTo` (combat state checks).  
  - `Object/GetTypeId` (player detection).  
  - `Player.Main/AreaExploredOrEventHappens` (quest credit).  
  - `ScriptedAI/EnterEvadeMode` (combat exit).  
  - `Unit.Main/GetHealth` and `GetMaxHealth` (health checks).  
  - `QuestDef/GetQuestId` (quest validation).  
- **Called by**: None.

### Dashel Stonefist
- **Calls out**:  
  - `ScriptedAI/ScriptedAI` (inheritance).  
  - `Map.Main/GetCreature` and `GetPlayer` (entity lookup).  
  - `ObjectGuid/Clear` (GUID cleanup).  
  - `Player.Main/GroupEventFailHappens` and `GroupEventHappens` (quest status).  
  - `TemporarySummon/UnSummon` (thug removal).  
  - `Unit.Main/IsAlive`, `SetFactionTemplateId`, `RemoveAllAuras`, `DeleteThreatList`, `CombatStop`, `GetHealth`, `GetHealthPercent`, `GetMotionMaster` (combat/state management).  
  - `WorldObject.Object/GetMap` and `SetFlag` (map and flag operations).  
  - `Creature.MotionMaster/MoveTargetedHome` and `MovePoint` (movement).  
  - `ScriptMgr/DoScriptText` (dialogue).  
  - `BasicAI/UpdateAI` (default update).  
  - `Creature.Main/DespawnOrUnsummon` (thug despawn).  
  - `Object/GetObjectGuid` and `ObjectGuid/operator==` (GUID comparison).  
  - `Creature.Main/AI` and `CreatureAI/AttackStart` (thug combat).  
  - `QuestDef/GetQuestId` (quest validation).  
  - `WorldObject.Object/SummonCreature#2` (thug summoning).  
  - `WorldObject.Object/RemoveFlag` (flag removal).  
- **Called by**: None.

### Script Registration
- **Calls out**:  
  - `Script/Script` (script object creation).  
  - `ScriptMgr/RegisterSelf` (registration).  
- **Called by**:  
  - `ScriptLoader/AddScripts` (server startup).

---

## Data Model

This unit does **not** interact with any database tables. All data is managed in memory via creature states, player events, and summoned entities.

---

## Notable Implementation Details

1. **Bartleby’s Health Threshold**: In `DamageTaken`, Bartleby evades combat if his health drops below 15% or if the incoming damage would kill him. This prevents his death and ensures quest completion. The damage is zeroed to avoid further processing.

2. **Dashel’s Phased Event**: The `UpdateAI` method uses a timer-based state machine to progress through dialogue and thug dismissal phases. Each phase has a specific delay, and the AI checks if thugs are still alive before proceeding. If thugs die unexpectedly, the event skips to quest completion.

3. **Thug Management**: Dashel summons two thugs with `TEMPSUMMON_TIMED_OR_DEAD_DESPAWN`. Their GUIDs are stored in `m_thugs[]` and cleared when they die or despawn to prevent dangling pointers. The `SummonedCreatureJustDied` and `SummonedCreatureDespawn` methods handle this cleanup.

4. **Quest Failure Handling**: If Dashel’s quest fight is interrupted (e.g., player disconnects), `Reset#2` marks the quest as failed for the player and cleans up thugs.

5. **Faction Changes**: Both NPCs change factions during their events (Bartleby to hostile, Dashel to neutral/friendly). These are restored on reset.

6. **Dialogue Timing**: Dashel’s dialogue phases are timed with delays (e.g., 3000ms for `MDQP_SAY1`). The AI checks if thugs are alive before speaking, ensuring consistency.

7. **Edge Case: GM Commands**: If Dashel or thugs are killed by GM commands, `JustDied` and `SummonedCreatureJustDied` clean up state to prevent errors.

---

## Member Reference

- **npc_bartlebyAI**: Initializes Bartleby’s AI, storing his normal faction and calling `Reset()`.
- **Reset**: Restores Bartleby’s faction to normal.
- **AttackedBy**: Starts combat if Bartleby is attacked by a hostile unit.
- **DamageTaken**: Triggers quest completion if Bartleby’s health drops below 15%.
- **QuestAccept_npc_bartleby**: Initiates Bartleby’s quest event, setting him hostile and starting combat.
- **GetAI_npc_bartleby**: Factory function for Bartleby’s AI.
- **startQuestFight**: Sets `m_questFightStarted = true`.
- **npc_dashel_stonefistAI**: Initializes Dashel’s AI, setting `m_questFightStarted = false` and calling `Reset()`.
- **AttackedBy#2**: Starts combat if Dashel is attacked by a hostile unit.
- **Reset#2**: Resets Dashel’s state, handling quest failure and thug cleanup.
- **DamageTaken#2**: Stops combat and begins dialogue if Dashel’s health drops below 20%.
- **UpdateAI**: Manages the phased dialogue/event sequence for Dashel.
- **ResetThug**: Moves a thug to a reset position and schedules despawning.
- **JustReachedHome**: Sets the next dialogue phase based on thug status.
- **JustDied**: Cleans up thugs if Dashel dies unexpectedly.
- **SummonedCreatureJustDied**: Clears the GUID of a dead thug.
- **SummonedCreatureDespawn**: Clears the GUID of a despawned thug.
- **QuestAccept_npc_dashel_stonefist**: Initiates Dashel’s quest event, spawning thugs and starting combat.
- **GetAI_npc_dashel_stonefist**: Factory function for Dashel’s AI.
- **AddSC_stormwind_city**: Registers both NPCs’ scripts with the server.

---

<!-- machine-true, projected from graph.json -->

## Map — stormwind_city

*Source:* stormwind_city.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| npc_bartlebyAI | ctor | ScriptedAI/ScriptedAI, Unit.Main/GetFactionTemplateId | — | — |
| Reset | method | Unit.Main/GetFactionTemplateId, Unit.Main/SetFactionTemplateId | — | — |
| AttackedBy | method | CreatureAI/AttackStart, Unit.Main/GetVictim, Unit.Main/IsFriendlyTo | — | — |
| DamageTaken | method | Object/GetTypeId, Player.Main/AreaExploredOrEventHappens, ScriptedAI/EnterEvadeMode, Unit.Main/GetHealth, Unit.Main/GetMaxHealth | — | — |
| QuestAccept_npc_bartleby | function | Creature.Main/AI, CreatureAI/AttackStart, QuestDef/GetQuestId, Unit.Main/SetFactionTemplateId | — | — |
| GetAI_npc_bartleby | function | — | — | — |
| startQuestFight | method | — | — | — |
| npc_dashel_stonefistAI | ctor | ScriptedAI/ScriptedAI | — | — |
| AttackedBy#2 | method | CreatureAI/AttackStart, Unit.Main/GetVictim, Unit.Main/IsFriendlyTo | — | — |
| Reset#2 | method | Map.Main/GetCreature, Map.Main/GetPlayer, ObjectGuid/Clear, Player.Main/GroupEventFailHappens, TemporarySummon/UnSummon, Unit.Main/IsAlive, Unit.Main/SetFactionTemplateId, WorldObject.Object/GetMap, WorldObject.Object/SetFlag | — | — |
| DamageTaken#2 | method | Creature.MotionMaster/MoveTargetedHome, Map.Main/GetCreature, ScriptMgr/DoScriptText, Unit.Main/CombatStop, Unit.Main/DeleteThreatList, Unit.Main/GetHealth, Unit.Main/GetHealthPercent, Unit.Main/GetMotionMaster, Unit.Main/IsAlive, Unit.Main/RemoveAllAuras, Unit.Main/SetFactionTemplateId, WorldObject.Object/GetMap | — | — |
| UpdateAI | method | BasicAI/UpdateAI, Map.Main/GetCreature, Map.Main/GetPlayer, Player.Main/GroupEventHappens, ScriptMgr/DoScriptText, Unit.Main/IsAlive, WorldObject.Object/GetMap | — | — |
| ResetThug | method | Creature.Main/DespawnOrUnsummon, Creature.MotionMaster/MovePoint, Map.Main/GetCreature, Unit.Main/GetMotionMaster, Unit.Main/IsAlive, WorldObject.Object/GetMap | — | — |
| JustReachedHome | method | — | — | — |
| JustDied | method | Map.Main/GetCreature, ObjectGuid/Clear, TemporarySummon/UnSummon, WorldObject.Object/GetMap | — | — |
| SummonedCreatureJustDied | method | Object/GetObjectGuid, ObjectGuid/Clear, ObjectGuid/operator== | — | — |
| SummonedCreatureDespawn | method | Object/GetObjectGuid, ObjectGuid/Clear, ObjectGuid/operator== | — | — |
| QuestAccept_npc_dashel_stonefist | function | Creature.Main/AI, CreatureAI/AttackStart, Object/GetObjectGuid, QuestDef/GetQuestId, ScriptMgr/DoScriptText, Unit.Main/SetFactionTemplateId, WorldObject.Object/RemoveFlag, WorldObject.Object/SummonCreature#2 | — | — |
| GetAI_npc_dashel_stonefist | function | — | — | — |
| AddSC_stormwind_city | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
