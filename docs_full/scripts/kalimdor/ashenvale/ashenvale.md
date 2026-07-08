# ashenvale

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Ashenvale Quest Scripts (`ashenvale.cpp`)

## Purpose & Responsibilities

This translation unit implements scripted AI and quest logic for four distinct content pieces in the Ashenvale zone of the World of Warcraft server emulation. It handles:

1.  **Three Escort Quests:**
    *   **Ruul Snowhoof** (Quest 6482): An escort where the NPC is protected by a bear form aura and summons minions at specific waypoints.
    *   **Torek** (Quest 6544): An escort involving faction changes for companions, waypoint-triggered dialogue, and combat encounters with summoned enemies.
    *   **Feero Ironhand** (Quest 976): A complex escort with multiple ambush phases, dynamic mob spawning based on distance/angle calculations, and specific dialogue triggers upon mob death or attack.

2.  **One Event System ("King of the Foulweald"):**
    *   A multi-stage event centered around a `go_foulweald_totem_mound` GameObject.
    *   It manages the lifecycle of `npc_enraged_foulweald` creatures, which spawn, move to a banner, destroy it, and eventually summon `Chief Murgut`.
    *   This system relies on tight coupling between the GameObject AI, the Creature AI, and a global event processor.

The unit uses the `ScriptedEscortAI` base class for the escorts and `ScriptedAI`/`GameObjectAI` for the event components. It registers these scripts with the core `ScriptMgr` via `AddSC_ashenvale`.

## Member-by-Member Behavior

### Ruul Snowhoof Escort (Quest 6482)

*   **`npc_ruul_snowhoofAI`**: Inherits from `npc_escortAI`. Manages the state of Ruul during the escort.
*   **`Reset#4`**: Applies `BEAR_AURA` (Spell ID 20514) to the creature. This likely provides damage reduction or immunity relevant to the escort mechanics.
*   **`WaypointReached#2`**: Triggers events at specific waypoints:
    *   **WP 13 & 19**: Summons three types of temporary minions (`NPC_T_TOTEMIC`, `NPC_T_URSA`, `NPC_T_PATHFINDER`) at hardcoded coordinates near the NPC. These minions despawn after 25 seconds or death.
    *   **WP 25**: Removes the bear aura, plays end dialogue (`SAY_RUUL_END`), and signals quest completion to the player via `GroupEventHappens`.
*   **`JustSummoned#2`**: Directs newly summoned minions to attack the escort NPC (`m_creature`). This implies the minions are hostile to the escort, creating a combat encounter the player must manage.
*   **`QuestAccept_npc_ruul_snowhoof`**: Global function triggered when a player accepts the quest. It sets the NPC's faction to neutral/passive (`FACTION_ESCORTEE`), ensures it stands up, and starts the escort AI.
*   **`GetAI_npc_ruul_snowhoofAI`**: Factory function returning a new instance of the AI.

### Torek Escort (Quest 6544)

*   **`npc_torekAI`**: Inherits from `npc_escortAI`. Handles Torek's movement, dialogue, and combat abilities.
*   **`Reset#5`**: Initializes timers for `SPELL_REND` (5s) and `SPELL_THUNDERCLAP` (8s). Note: These initial values are overwritten in `UpdateEscortAI` after first use.
*   **`JustDied#2`**: Upon death, it finds all `NPC_SPLINTERTREE_RAIDER` creatures within 40 yards and forces them to disappear/die. This cleans up companion NPCs that may have been following or fighting alongside Torek. It then calls the parent `JustDied`.
*   **`WaypointReached#3`**:
    *   **WP 1, 8, 20, 21**: Plays specific dialogue lines (`SAY_MOVE`, `SAY_PREPARE`, `SAY_WIN`, `SAY_END`) directed at the player. WP 20 also triggers quest completion.
    *   **WP 19**: Summons elite enemies (`NPC_DURIEL`, `NPC_SILVERWING_SENTINEL`, `NPC_SILVERWING_WARRIOR`) at hardcoded coordinates.
    *   **WP 22**: Cleans up `NPC_SPLINTERTREE_RAIDER` companions again, similar to `JustDied`.
*   **`JustSummoned#3`**: Orders summoned enemies to attack Torek.
*   **`UpdateEscortAI`**: Standard combat loop. Casts `SPELL_REND` on the victim every 20 seconds and `SPELL_THUNDERCLAP` on self every 30 seconds. Performs melee attacks if ready.
*   **`QuestAccept_npc_torek`**: Sets Torek and nearby `NPC_SPLINTERTREE_RAIDER` companions to `FACTION_ORGRIMMAR` temporarily. Starts the escort.
*   **`GetAI_npc_torek`**: Factory function.

### Feero Ironhand Escort (Quest 976)

*   **`npc_feero_ironhandAI`**: Inherits from `npc_escortAI`. Manages a multi-phase ambush escort.
*   **`Reset#3`**: Resets internal counters (`m_uiCreaturesCount`, `m_bIsAttacked`) only if the escort is not currently active.
*   **`JustRespawned`**: Sets the NPC immune to NPC attacks (`UNIT_FLAG_IMMUNE_TO_NPC`) to prevent accidental aggro from world events or other NPCs before the quest starts. Calls parent `JustRespawned`.
*   **`WaypointReached`**:
    *   **WP 14**: First ambush. Spawns 4 `NPC_DARK_STRAND_ASSASSIN` mobs using `DoSpawnMob` with calculated positions.
    *   **WP 20**: Second ambush. Spawns 3 `NPC_FORSAKEN_SCOUT` mobs.
    *   **WP 29**: Final ambush. Summons 3 elite mobs (`NPC_BALIZAR`, `NPC_ALIGAR`, `NPC_CAEDAKAR`) at hardcoded coordinates.
    *   **WP 30**: Quest completion trigger.
*   **`AttackedBy`**: Checks if the attacker is `NPC_BALIZAR_THE_UMBRAGE`. If so, and it's the first attack, plays a specific yell. This prevents repeated yells if Balizar attacks multiple times.
*   **`DoSpawnMob`**: Helper function. Calculates a position relative to the NPC using distance and angle, then summons the creature there.
*   **`SummonedCreatureJustDied`**: Decrements the alive creature count. If the count reaches zero, it plays dialogue based on which mob died last (distinguishing between ambush phases).
*   **`JustSummoned`**: Increments the alive creature count. Special handling for `NPC_FORSAKEN_SCOUT` (only the first one yells) and `NPC_BALIZAR` (yells immediately). Attacks the player.
*   **`GetAI_npc_feero_ironhand`**: Factory function.
*   **`QuestAccept_npc_feero_ironhand`**: Sets faction to neutral/passive, removes the immune flag set in `JustRespawned`, and starts the escort.

### King of the Foulweald Event

This event is split between a GameObject (`go_foulweald_totem_mound`) and Creatures (`npc_enraged_foulweald`).

#### GameObject AI (`go_foulweald_totem_moundAI`)

*   **`go_foulweald_totem_moundAI`**: Inherits from `GameObjectAI`. Tracks the event phase and GUIDs of active Foulweald creatures.
*   **`reset`**: Resets `eventPhase` to 0 and `phaseTimer` to 170 seconds.
*   **`EventStart`**: Triggered by the global event processor. If already started, returns false. Otherwise, sets phase to 1 and spawns two `NPC_ENRAGED_FOULWEALD` creatures at predefined coordinates. It moves them to the mound's position, sets their home position, respawn delay, and links them to the mound via `DefineFoulwealdMound`.
*   **`EventEnded`**: Finds the nearest `GO_KARANG_S_BANNER` and adds it to the removal list (despawns it). Resets the event state.
*   **`EnragedFoulwealdJustDied`**: Called when a linked Foulweald dies. If in phase 1, it respawns a new Foulweald at a random coordinate from the pool, maintaining the pressure on the banner.
*   **`UpdateAI`**: Manages the event timeline:
    *   **Phase 1 -> 2**: Waits 170 seconds (initial timer).
    *   **Phase 2 -> 3**: Waits 10 seconds.
    *   **Phase 3**: Spawns `NPC_CHIEF_MURGUT` and a light object (`GO_KARANG_LIGHT`) near the banner. Sets a 120-second timer.
    *   **Phase 4 -> End**: Calls `EventEnded`.
*   **`GetAIgo_foulweald_totem_mound`**: Factory function.

#### Creature AI (`npc_enraged_foulwealdAI`)

*   **`npc_enraged_foulwealdAI`**: Inherits from `ScriptedAI`. Controls the behavior of the Foulweald creatures.
*   **`Reset#2`**: Applies `SPELL_CORRUPTED_STRENGTH` aura. Resets internal timer.
*   **`JustDied`**: Notifies the linked mound GameObject (via `guidMound`) that this creature died, triggering `EnragedFoulwealdJustDied`. Despawn after 60 seconds.
*   **`MovementInform`**: If the creature arrives at movement point 2 (set by `HitBanner` logic), it triggers `HitBanner`.
*   **`AttackStart`**: Prevents attacking if the creature is currently channeling a spell.
*   **`HitBanner`**: Finds the nearest `GO_KARANG_S_BANNER`. If found, casts a destruction spell on it, plays dialogue, and sets a 10-second timer. Returns true if successful.
*   **`UpdateAI#2`**:
    *   If channeling, do nothing.
    *   If no victim: Checks timer. If expired, tries to `HitBanner`. If banner is too far (>30 yards), moves to a contact point near it.
    *   If has victim: Performs melee attacks.
*   **`SpellHit`**: If hit by `SPELL_DESTROY_KARANG_S_BANNER_2` (likely a player spell), it finds the mound and calls `EventEnded`, prematurely ending the event.
*   **`SetMoundGuid`**: Stores the GUID of the controlling mound GameObject.
*   **`GetAI_npc_enraged_foulweald`**: Factory function.

#### Global Helpers

*   **`ProcessEventId_event_king_of_the_foulweald`**: Global event handler. When the event starts (`isStart`), it retrieves the AI of the target GameObject (the mound) and calls `EventStart` with the source player's GUID.
*   **`DefineFoulwealdMound`**: Helper function. Retrieves the AI of a Foulweald creature and sets its linked mound GUID. Used by the mound AI when spawning creatures.
*   **`AddSC_ashenvale`**: Registers all scripts defined in this file with the `ScriptMgr`.

## Cross-Unit Boundaries

*   **`ScriptedEscortAI` / `npc_escortAI`**: All three escort AIs inherit from this base class (defined in `ScriptedEscortAI.cpp` or similar). They rely on it for waypoint management (`WaypointReached`), player tracking (`GetPlayerForEscort`), and escort state (`HasEscortState`).
*   **`ScriptMgr`**: Used via `DoScriptText` to play dialogue lines. This is a core engine component.
*   **`Creature` / `Unit` / `Player`**: Standard engine classes. The scripts manipulate factions, auras, stand states, and summon creatures.
*   **`WorldObject`**: Used for spatial queries (`GetNearPoint`, `GetContactPoint`, `FindNearestGameObject`, `GetCreatureListWithEntryInGrid`) and summoning (`SummonCreature`, `SummonGameObject`).
*   **`ScriptedAI`**: Base class for `npc_enraged_foulwealdAI`. Provides standard combat loops (`UpdateAI`, `AttackStart`, `DoMeleeAttackIfReady`).
*   **`GameObjectAI`**: Base class for `go_foulweald_totem_moundAI`.
*   **`ScriptLoader`**: `AddSC_ashenvale` is called by `ScriptLoader` (not shown in map, but implied by `AddScripts` pattern) to register the scripts.

## Data Model

This unit does not directly query or modify database tables. It relies on data pre-loaded into memory by the engine (creature templates, quest definitions, gossip menus, etc.). The `Tables` column in the MAP is empty for all members.

## Notable Implementation Details

1.  **Hardcoded Coordinates**: Many summons use hardcoded XYZ coordinates (e.g., Ruul's minions, Torek's elites, Feero's final ambush, Foulweald spawns). This makes the scripts fragile to map changes or repositioning of NPCs in the database.
2.  **Foulweald Event Coupling**: The `go_foulweald_totem_moundAI` and `npc_enraged_foulwealdAI` are tightly coupled via GUIDs. The mound stores the GUIDs of the Foulwealds, and the Foulwealds store the GUID of the mound. This allows them to notify each other of state changes (death, event end). The helper function `DefineFoulwealdMound` facilitates this link.
3.  **Feero's Ambush Logic**: Feero's script uses a counter (`m_uiCreaturesCount`) to track alive summoned mobs. Dialogue is triggered only when the count hits zero, ensuring the NPC reacts after the fight is over. The `AttackedBy` check for Balizar prevents spamming dialogue.
4.  **Torek's Companion Cleanup**: Torek's `JustDied` and `WaypointReached` (WP 22) both clean up `NPC_SPLINTERTREE_RAIDER` companions. This suggests these companions are spawned elsewhere (possibly by the database or another script) and need manual cleanup to avoid lingering dead bodies.
5.  **Ruul's Bear Aura**: The `BEAR_AURA` is applied on reset and removed at the end of the escort. This is a key mechanic for the quest, likely making Ruul tankier during the escort.
6.  **Foulweald Banner Destruction**: The event can end in two ways: naturally (after Chief Murgut spawns and time expires) or prematurely if a player hits the Foulweald with `SPELL_DESTROY_KARANG_S_BANNER_2`. The `SpellHit` method in `npc_enraged_foulwealdAI` checks for this specific spell ID.
7.  **Timer Initialization**: In `npc_torekAI::Reset`, timers are initialized to 5000 and 8000, but `UpdateEscortAI` resets them to 20000 and 30000 after the first cast. This means the first cast happens much sooner than subsequent ones. This might be intentional (quick opening move) or a minor inconsistency.
8.  **Immunity Flag**: Feero Ironhand is set immune to NPC attacks on respawn (`JustRespawned`) and has this flag removed on quest accept. This prevents him from being attacked by world mobs before the quest begins.

## Member Reference

*   **`npc_ruul_snowhoofAI`**: Constructor for Ruul Snowhoof's AI, inheriting from `npc_escortAI`.
*   **`Reset#4`**: Applies the bear aura to Ruul Snowhoof.
*   **`WaypointReached#2`**: Handles waypoint events for Ruul: summoning minions at WPs 13/19, removing aura and completing quest at WP 25.
*   **`JustSummoned#2`**: Orders Ruul's summoned minions to attack Ruul.
*   **`QuestAccept_npc_ruul_snowhoof`**: Global function to start Ruul's escort quest, setting faction and starting AI.
*   **`GetAI_npc_ruul_snowhoofAI`**: Factory function for Ruul's AI.
*   **`npc_torekAI`**: Constructor for Torek's AI, inheriting from `npc_escortAI`.
*   **`Reset#5`**: Initializes spell timers for Torek.
*   **`JustDied#2`**: Cleans up companion NPCs upon Torek's death and calls parent `JustDied`.
*   **`WaypointReached#3`**: Handles waypoint events for Torek: dialogue, summoning elites at WP 19, quest completion at WP 20, and companion cleanup at WP 22.
*   **`JustSummoned#3`**: Orders Torek's summoned enemies to attack Torek.
*   **`UpdateEscortAI`**: Combat loop for Torek, casting Rend and Thunderclap on timers.
*   **`QuestAccept_npc_torek`**: Global function to start Torek's escort quest, setting faction for Torek and companions, and starting AI.
*   **`GetAI_npc_torek`**: Factory function for Torek's AI.
*   **`npc_feero_ironhandAI`**: Constructor for Feero Ironhand's AI, inheriting from `npc_escortAI`.
*   **`Reset#3`**: Resets internal counters for Feero if escort is inactive.
*   **`JustRespawned`**: Sets Feero immune to NPC attacks on respawn.
*   **`WaypointReached`**: Handles waypoint events for Feero: spawning ambush mobs at WPs 14, 20, 29, and completing quest at WP 30.
*   **`AttackedBy`**: Plays specific dialogue if attacked by Balizar for the first time.
*   **`DoSpawnMob`**: Helper to spawn mobs at calculated positions relative to Feero.
*   **`SummonedCreatureJustDied`**: Tracks alive mob count and plays dialogue when all mobs in an ambush are dead.
*   **`JustSummoned`**: Tracks alive mob count, plays specific dialogue for scouts/Balizar, and orders mobs to attack the player.
*   **`GetAI_npc_feero_ironhand`**: Factory function for Feero's AI.
*   **`QuestAccept_npc_feero_ironhand`**: Global function to start Feero's escort quest, removing immunity and starting AI.
*   **`go_foulweald_totem_moundAI`**: Constructor for the Foulweald Totem Mound's AI, inheriting from `GameObjectAI`.
*   **`reset`**: Resets the event phase and timer for the mound.
*   **`EventStart`**: Starts the Foulweald event, spawning initial creatures and linking them to the mound.
*   **`EventEnded`**: Ends the event, despawning the banner and resetting state.
*   **`EnragedFoulwealdJustDied`**: Respawns a Foulweald creature when one dies, maintaining event pressure.
*   **`UpdateAI`**: Manages the event timeline for the mound, transitioning through phases and spawning Chief Murgut.
*   **`GetAIgo_foulweald_totem_mound`**: Factory function for the mound's AI.
*   **`npc_enraged_foulwealdAI`**: Constructor for the Enraged Foulweald's AI, inheriting from `ScriptedAI`.
*   **`Reset#2`**: Applies strength aura to the Foulweald.
*   **`JustDied`**: Notifies the mound of death and despawns after 60 seconds.
*   **`MovementInform`**: Triggers banner attack logic upon reaching movement point 2.
*   **`AttackStart`**: Prevents attack if channeling.
*   **`HitBanner`**: Destroys the banner if nearby, playing dialogue and setting a timer.
*   **`UpdateAI#2`**: Combat loop for Foulweald, prioritizing banner destruction if no victim, otherwise melee.
*   **`SpellHit`**: Ends the event if hit by a specific banner-destroying spell.
*   **`SetMoundGuid`**: Links the Foulweald to its controlling mound GameObject.
*   **`GetAI_npc_enraged_foulweald`**: Factory function for the Foulweald's AI.
*   **`ProcessEventId_event_king_of_the_foulweald`**: Global event handler to start the Foulweald event.
*   **`DefineFoulwealdMound`**: Helper to link a Foulweald creature to a mound GUID.
*   **`AddSC_ashenvale`**: Registers all Ashenvale scripts with the engine.

---

<!-- machine-true, projected from graph.json -->

## Map — ashenvale

*Source:* ashenvale.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| npc_ruul_snowhoofAI | ctor | ScriptedEscortAI/npc_escortAI | — | — |
| Reset#4 | method | Unit.Main/AddAura | — | — |
| WaypointReached#2 | method | Player.Main/GroupEventHappens, ScriptedEscortAI/GetPlayerForEscort, ScriptMgr/DoScriptText, Unit.Main/RemoveAurasDueToSpell, WorldObject.Object/SummonCreature#2 | — | — |
| JustSummoned#2 | method | Creature.Main/AI, CreatureAI/AttackStart | — | — |
| QuestAccept_npc_ruul_snowhoof | function | Creature.Main/AI, Object/GetGUID, QuestDef/GetQuestId, ScriptedEscortAI/Start, Unit.Main/SetFactionTemplateId, Unit.Main/SetStandState | — | — |
| GetAI_npc_ruul_snowhoofAI | function | — | — | — |
| npc_torekAI | ctor | ScriptedEscortAI/npc_escortAI | — | — |
| Reset#5 | method | — | — | — |
| JustDied#2 | method | Creature.Main/DisappearAndDie, ScriptedEscortAI/JustDied, WorldObject.Object/GetCreatureListWithEntryInGrid | — | — |
| WaypointReached#3 | method | Creature.Main/DisappearAndDie, Player.Main/GroupEventHappens, ScriptedEscortAI/GetPlayerForEscort, ScriptMgr/DoScriptText, WorldObject.Object/GetCreatureListWithEntryInGrid, WorldObject.Object/SummonCreature#2 | — | — |
| JustSummoned#3 | method | Creature.Main/AI, CreatureAI/AttackStart | — | — |
| UpdateEscortAI | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| QuestAccept_npc_torek | function | Creature.Main/AI, Creature.Main/SetFactionTemporary, Object/GetGUID, QuestDef/GetQuestId, ScriptedEscortAI/Start, ScriptMgr/DoScriptText, WorldObject.Object/GetCreatureListWithEntryInGrid | — | — |
| GetAI_npc_torek | function | — | — | — |
| npc_feero_ironhandAI | ctor | ScriptedEscortAI/npc_escortAI | — | — |
| Reset#3 | method | ScriptedEscortAI/HasEscortState | — | — |
| JustRespawned | method | ScriptedEscortAI/JustRespawned, WorldObject.Object/SetFlag | — | — |
| WaypointReached | method | Player.Main/GroupEventHappens, ScriptedEscortAI/GetPlayerForEscort, ScriptMgr/DoScriptText, WorldObject.Object/SummonCreature#2 | — | — |
| AttackedBy | method | Object/GetEntry, Object/ToCreature, ScriptMgr/DoScriptText | — | — |
| DoSpawnMob | method | WorldObject.Object/GetNearPoint, WorldObject.Object/SummonCreature#2 | — | — |
| SummonedCreatureJustDied | method | Object/GetEntry, ScriptMgr/DoScriptText | — | — |
| JustSummoned | method | Creature.Main/AI, CreatureAI/AttackStart, Object/GetEntry, ScriptedEscortAI/GetPlayerForEscort, ScriptMgr/DoScriptText | — | — |
| GetAI_npc_feero_ironhand | function | — | — | — |
| QuestAccept_npc_feero_ironhand | function | Creature.Main/AI, Object/GetGUID, QuestDef/GetQuestId, ScriptedEscortAI/Start, ScriptMgr/DoScriptText, Unit.Main/SetFactionTemplateId, WorldObject.Object/RemoveFlag | — | — |
| go_foulweald_totem_moundAI | ctor | GameObjectAI/GameObjectAI | — | — |
| reset | method | — | — | — |
| EventStart | method | Creature.Main/SetHomePosition, Creature.Main/SetRespawnDelay, Creature.MotionMaster/MovePoint, Object/GetGUID, Unit.Main/GetMotionMaster, WorldObject.Object/GetPosition#2, WorldObject.Object/SummonCreature#2 | — | — |
| EventEnded | method | WorldObject.Object/AddObjectToRemoveList, WorldObject.Object/FindNearestGameObject | — | — |
| EnragedFoulwealdJustDied | method | Creature.Main/SetHomePosition, Creature.Main/SetRespawnDelay, Creature.MotionMaster/MovePoint, Object/GetGUID, shared_Util/urand, Unit.Main/GetMotionMaster, WorldObject.Object/GetPosition#2, WorldObject.Object/SummonCreature#2 | — | — |
| UpdateAI | method | Creature.Main/SetHomePosition, Creature.Main/SetRespawnDelay, Creature.MotionMaster/MovePoint, Unit.Main/GetMotionMaster, WorldObject.Object/FindNearestGameObject, WorldObject.Object/GetPosition#2, WorldObject.Object/SummonCreature#2, WorldObject.Object/SummonGameObject | — | — |
| GetAIgo_foulweald_totem_mound | function | — | — | — |
| npc_enraged_foulwealdAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#2 | method | Unit.Main/AddAura | — | — |
| JustDied | method | Creature.Main/DespawnOrUnsummon, GameObject/AI, Map.Main/GetGameObject, Object/GetGUID, ObjectGuid/ObjectGuid#5, WorldObject.Object/GetMap | — | — |
| MovementInform | method | — | — | — |
| AttackStart | method | CreatureAI/AttackStart, SpellCaster/GetCurrentSpell | — | — |
| HitBanner | method | ScriptMgr/DoScriptText, SpellCaster/CastSpell#4, WorldObject.Object/FindNearestGameObject, WorldObject.Object/GetPosition#2 | — | — |
| UpdateAI#2 | method | Creature.MotionMaster/MovePoint, CreatureAI/DoMeleeAttackIfReady, SpellCaster/GetCurrentSpell, Unit.Main/GetMotionMaster, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget, WorldObject.Object/FindNearestGameObject, WorldObject.Object/GetContactPoint | — | — |
| SpellHit | method | GameObject/AI, WorldObject.Object/FindNearestGameObject | — | — |
| SetMoundGuid | method | — | — | — |
| GetAI_npc_enraged_foulweald | function | — | — | — |
| ProcessEventId_event_king_of_the_foulweald | function | GameObject/AI, Object/GetGUID | — | — |
| DefineFoulwealdMound | function | Creature.Main/AI | — | — |
| AddSC_ashenvale | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
