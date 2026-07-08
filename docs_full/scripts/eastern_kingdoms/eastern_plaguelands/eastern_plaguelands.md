# eastern_plaguelands

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# eastern_plaguelands

## Purpose & Responsibilities

The `eastern_plaguelands` translation unit implements scripted AI behaviors and event logic for specific quests and encounters in the Eastern Plaguelands zone of the World of Warcraft emulator. It contains no database interactions; all logic is driven by in-memory state, creature/game object entries, and spell effects.

The unit supports four distinct features:
1.  **Balance of Light (Quest 7622):** A complex wave-based defense event centered on NPC Eris Havenfire (`npc_eris_havenfireAI`). Players must protect summoned peasants from waves of warriors and archers. The event tracks peasant survival/death counts, manages summoning timers, and handles failure conditions (e.g., unauthorized players attacking peasants).
2.  **Demetria Encounter:** An elite priest encounter (`npc_demetriaAI`) that summons Scarlet Troopers, resurrects them upon death, and uses mind-control and dispel mechanics.
3.  **Battle of Darrowshire (Quest 5721):** A large-scale scripted battle triggered by a game object (`go_darrowshire_triggerAI`). It manages phased spawning of attackers (Scourge) and defenders (Alliance militia), including boss phases involving Horgus the Ravager, Davil Lightfire, and Captain Redpath.
4.  **Supporting NPCs & Mechanics:**
    *   `npc_joseph_redpathAI`: Handles the post-battle cutscene and gossip interaction for Joseph Redpath.
    *   `npc_guard_didierAI` & `npc_caravan_muleAI`: Manage a caravan escort mechanic where the guard and mules remain passive until attacked, then engage in group combat.
    *   `EffectDummyGameObj_go_mark_of_detonation`: A spell effect handler for the "When Smokey Sings, I Get Violent" quest, linking explosive placement to structure destruction and credit granting.

## Member-by-Member Behavior

### Balance of Light Event (`npc_eris_havenfireAI` & `npc_eris_havenfire_peasantAI`)

This subsystem manages the "Balance of Light" quest. The core controller is `npc_eris_havenfireAI`, attached to Eris Havenfire. Peasants have their own AI, `npc_eris_havenfire_peasantAI`.

**Event Controller (`npc_eris_havenfireAI`)**
*   **Initialization & State:** The constructor sets a high summon limit (200). `Reset` initializes timers for waves, buffs, and archer attacks, clears GUID arrays for tracking villagers and archers, and enables line-of-sight events.
*   **Wave Management:** `NewWave` spawns groups of peasants or warriors based on the current wave number. `GenerateWaveNumber` determines the count, scaling up for later peasant waves. `UpdateAI` drives the timers: spawning peasant waves every ~80s, warrior waves randomly, and casting `Blessing of Nordrassil` periodically.
*   **Archer Logic:** `UpdateAI` iterates through 8 tracked archer GUIDs. Each archer independently targets a random alive villager and casts `Shoot`.
*   **Peasant Tracking:** `JustSummoned` records GUIDs for peasants and archers. `SummonedMovementInform` increments the survival counter when a peasant reaches the destination point. `SummonedCreatureJustDied` increments the death counter and spawns a "Death Post" game object at a predefined position corresponding to the death count.
*   **Failure Conditions:** `MoveInLineOfSight` checks if a non-quest-player (or pet) enters line of sight. If so, it spawns a "Cleaner" mob to attack the intruder and triggers `FailEvent`. `FailEvent` fails the quest for the player, despawns all mobs/game objects, and resets the event.
*   **Completion:** `CompleteEvent` is called when 50 peasants survive. It grants the quest credit, plays dialogue, cleans up, and resets.
*   **Helper Functions:** `GetPlayer` retrieves the questing player from the map using the stored GUID. `DespawnAll` cleans up all active creatures and death post game objects. `SetAttackOnPeasantOrPlayer` forces summoned warriors to target peasants or the player.

**Peasant AI (`npc_eris_havenfire_peasantAI`)**
*   **Movement:** `Reset` calculates random start and end positions. `UpdateAI` initiates movement to a combat start point, then to the final destination. `MovementInform` handles the transition between these points.
*   **Interaction:** `SpellHit` checks if hit by `Shoot`. If so, it has a 10% chance to cast `Deaths Door`. Crucially, if hit by a player who is *not* the questing player, it triggers the failure condition by spawning a Cleaner and notifying Eris's AI. `DamageTaken` reduces damage from Archers to a fixed range.

### Demetria Encounter (`npc_demetriaAI`)

*   **Summoning:** `MovementInform` (Waypoint 0) summons 9 Scarlet Troopers in a circle around Demetria and joins them to her creature group.
*   **Combat Loop (`UpdateAI`):**
    *   **Resurrection:** Checks for dead troopers nearby and calls `DoRessurectUnit` to revive them with full health.
    *   **Spells:** Casts `Mind Blast`, `Shadow Word Pain`, `Mind Flay`, and `Dominate Mind` on random targets on timers.
    *   **Dispels:** Uses `FriendlyCCedInRangeCheck` to find friendly CC'd creatures. If found, dispels them; otherwise, dispels the victim.
    *   **Psychic Scream:** Casts at low health (<30%).
*   **Cleanup:** `JustDied` and `MovementInform` (Waypoint 99) call `DespawnTroopers` to remove summoned units.

### Battle of Darrowshire (`go_darrowshire_triggerAI`)

This AI is attached to a Game Object that triggers the battle.

*   **Initialization:** `Reset` determines the defender faction based on the nearest player's team (Horde vs Alliance) to ensure proper aggro behavior.
*   **Phased Spawning (`UpdateAI`):**
    *   **Phase 0:** Spawns initial defenders.
    *   **Phase 1:** Spawns Davil Lightfire.
    *   **Phase 2:** Spawns Horgus the Ravager to fight Davil.
    *   **Phase 3:** Upon Horgus's death, Davil despawns, and Captain Redpath spawns.
    *   **Phase 4:** Spawns Marduk the Black, who kills Captain Redpath, spawning Corrupted Redpath.
    *   **Phase 5:** Ends when Corrupted Redpath dies, spawning Joseph Redpath and Davil Crokford.
*   **Mob Spawners:** Timers (`m_mobTimer`) continuously spawn waves of Marauding Corpses/Skeletons, Defenders, Servants of Horgus, Silverhand Disciples, Bloodletters, and Redpath Militia depending on the phase.
*   **Patrols:** Timer index 6 manages random point movement for key NPCs (Davil, Redpath, Bloodletter) when not in combat.
*   **Cleanup:** `DespawnAll` removes all summoned creatures and the trigger itself. `OnRemoveFromWorld` ensures cleanup if the object is removed prematurely.

### Supporting NPCs

**Joseph Redpath (`npc_joseph_redpathAI`)**
*   **Cutscene:** `BeginEvent` starts a sequence of movements and dialogues between Joseph and Pamela Redpath. `MovementInform` and `UpdateAI` drive the step-by-step progression.
*   **Gossip:** `GossipHello_npc_joseph_redpath` grants quest credit, plays an emote, and starts the cutscene.

**Guard Didier & Caravan Mule (`npc_guard_didierAI`, `npc_caravan_muleAI`)**
*   **Passive-to-Aggressive:** Both AIs start passive. `DamageTaken` triggers `EnableCombat`, which switches the react state to aggressive and alerts the entire creature group to attack the attacker.
*   **Didier Specifics:** `JustReachedHome` checks if the mule died (`m_muleDied` flag set by `GroupMemberJustDied`). If so, Didier cries, sets a specific gossip menu, and despawns the group after a delay. `JustRespawned` resets the state.

**Mark of Detonation (`EffectDummyGameObj_go_mark_of_detonation`)**
*   **Spell Effect:** Triggered by spell `19250`. Finds the nearest Scourge Structure. Grants kill credit to the player, kills the structure, and respawns associated fire game objects using a hardcoded map of GUIDs.

## Cross-Unit Boundaries

*   **`ScriptedAI`**: All creature AIs inherit from `ScriptedAI` (`ScriptedAI/ScriptedAI`), providing base functionality like `DoScriptText` and `DoCastSpellIfCan`.
*   **`Creature` / `Unit` / `WorldObject`**: Extensive use of methods like `SummonCreature`, `GetMotionMaster`, `AddThreat`, `SetHealth`, `FindNearestCreature`, etc., to manipulate entities in the world.
*   **`Map`**: Used to retrieve players (`GetPlayer`), creatures (`GetCreature`), and game objects (`GetGameObject`) by GUID or entry.
*   **`ScriptMgr`**: Used to play dialogue (`DoScriptText`).
*   **`GridSearchers`**: Used to find lists of creatures in the grid (`GetCreatureListWithEntryInGrid`).
*   **`CreatureGroups`**: Used in Didier/Mule AIs to manage group-wide state changes (`DoForAllMembers`).
*   **`Player`**: Used to check quest status (`GetQuestStatus`), fail quests (`FailQuest`), and grant credit (`KilledMonsterCredit`, `AreaExploredOrEventHappens`).
*   **`ObjectGuid`**: Used for managing and clearing GUID references.
*   **`shared_Util`**: `urand` is used extensively for random numbers.

## Data Model

This unit does not interact with any database tables. All data is hardcoded in the source (entries, coordinates, spell IDs, text IDs) or managed in memory during runtime.

## Notable Implementation Details

1.  **Hardcoded GUID Maps:** `EffectDummyGameObj_go_mark_of_detonation` uses a `static std::map` of creature GUIDs to game object GUIDs to respawn fires. This is fragile and relies on specific database GUIDs.
2.  **Faction Hacking:** `go_darrowshire_triggerAI::Reset` manually sets `m_defenderFaction` to 85 (Orgrimmar) or 57 (Ironforge) for defenders to enable aggro, noting in comments that the default escort faction doesn't work correctly.
3.  **Memory Leaks/Inefficiencies:** `npc_eris_havenfireAI::UpdateAI` creates a local array `uint64 GUIDs[50]` and copies valid villager GUIDs into it every tick for every archer. This is inefficient but functional.
4.  **Race Conditions:** `npc_eris_havenfireAI::MoveInLineOfSight` and `npc_eris_havenfire_peasantAI::SpellHit` both check for unauthorized players and spawn Cleaners. There is no mutex or lock, so multiple cleaners could potentially spawn if multiple peasants are hit simultaneously by different unauthorized players.
5.  **Despawn Logic:** `go_darrowshire_triggerAI::DespawnAll` excludes Joseph Redpath and Davil Crokford from immediate despawn, allowing them to persist after the battle ends.
6.  **Resurrection Hack:** `npc_demetriaAI::DoRessurectUnit` manually sets health, stand state, and flags, then calls `Respawn()` and `NearTeleportTo()`. This bypasses standard resurrection mechanics to instantly revive troopers.

## Member Reference

**DeathPostSpawn**
Constructor for the `DeathPostSpawn` struct, initializing entry, coordinates, orientation, and rotation values for death post game objects.

**npc_eris_havenfireAI**
Constructor for the Eris Havenfire AI. Initializes the parent `ScriptedAI`, calls `Reset`, and sets the creature's summon limit to 200.

**GetPlayer**
Retrieves the `Player` pointer associated with the questing player's GUID from the map. Returns null if not found.

**Reset#4**
Resets the Eris Havenfire AI state. Clears counters, timers, and GUID arrays. Re-enables line-of-sight events.

**AttackedBy**
Override stub. Does nothing. Prevents default AI behavior when Eris is attacked.

**MoveInLineOfSight**
Checks if a player or pet enters line of sight. If the player is not the questing player and the cleaner hasn't spawned, it spawns a `NPC_CLEANER` to attack the intruder, marks the quest as failed, and calls `FailEvent`.

**SetAttackOnPeasantOrPlayer**
Forces a summoned creature to add threat against nearby peasants or the questing player. Randomly decides whether to chase the player directly.

**DespawnAll#2**
Iterates through all summoned peasants, warriors, and archers in the grid and forces them to despawn. Also deletes all tracked death post game objects.

**JustSummoned#2**
Handles newly summoned creatures. Records archer GUIDs, sets sheath state for archers, applies plague to plagued peasants, and records villager GUIDs. Sets PVP flag on peasants.

**SummonedMovementInform#2**
Called when a summoned peasant reaches the end point. Increments the survival counter, plays random dialogue, removes the GUID from tracking, and despawns the peasant. If 50 peasants survive, calls `CompleteEvent`.

**SummonedCreatureJustDied#2**
Called when a summoned peasant dies. Increments the death counter, spawns a death post game object at a predefined position, and removes the GUID from tracking. If 15 peasants die, calls `FailEvent`.

**FailEvent**
Fails the quest for the player if incomplete. Plays failure dialogue, removes the light game object, stops combat, despawns all mobs, and either despawns Eris or resets the event.

**BeginEvent**
Initializes the event for a specific player. Resets counters and timers, spawns initial archers, and summons the light game object if missing.

**NewWave**
Spawns a wave of peasants or warriors. Determines the count via `GenerateWaveNumber`, picks random spawn points, and summons the creatures. Plays spawn dialogue for peasants.

**GenerateWaveNumber**
Returns the number of mobs to spawn in a wave. Scales peasant counts based on wave number; warrior counts are random.

**CompleteEvent**
Grants quest credit, plays completion dialogue, removes the light game object, stops combat, despawns all mobs, and resets the event.

**UpdateAI#3**
Main update loop. Keeps the player in combat, manages wave timers, buff timers, and archer attack timers. Casts spells and spawns waves as needed.

**QuestAccept_npc_eris_havenfire**
Global function. Called when a player accepts the quest. Retrieves the AI and calls `BeginEvent`.

**GetAI_npc_eris_havenfire**
Global function. Factory for creating `npc_eris_havenfireAI` instances.

**npc_eris_havenfire_peasantAI**
Constructor for the peasant AI. Initializes the parent `ScriptedAI` and calls `Reset`.

**Reset#5**
Resets the peasant AI. Calculates random start and end positions, sets movement speed, and initializes the dialogue timer.

**KilledUnit**
Override stub. Does nothing.

**DamageTaken#2**
Reduces damage taken from Archers to a fixed range (80-105).

**SpellHit**
Checks if hit by `Shoot` spell (chance to cast `Deaths Door`). If hit by a non-quest player, spawns a Cleaner and triggers failure.

**MoveInLineOfSight#2**
Override stub. Does nothing.

**MovementInform#2**
Handles movement completion. Moves the peasant from the start point to the end point with appropriate speed.

**UpdateAI#4**
Manages peasant movement initiation and periodic random dialogue.

**GetAI_npc_eris_havenfire_peasant**
Global function. Factory for creating `npc_eris_havenfire_peasantAI` instances.

**npc_demetriaAI**
Constructor for Demetria's AI. Initializes the parent `ScriptedAI` and calls `Reset`.

**MovementInform**
Handles waypoint completion. Waypoint 0 summons 9 Scarlet Troopers and joins them to the group. Waypoint 99 despawns troopers and Demetria.

**JustDied**
Calls `DespawnTroopers` to clean up summoned units.

**DespawnTroopers**
Iterates through the stored trooper GUIDs and adds them to the removal list.

**UpdateAI#2**
Main combat loop. Resurrects dead troopers, casts spells (Mind Blast, Shadow Word Pain, etc.), dispels friends/victims, and casts Psychic Scream at low health.

**Reset#3**
Resets timers and applies the Shadowform aura.

**DoRessurectUnit**
Revives a dead creature by setting health, stand state, and flags, then teleporting it to its previous position and starting combat.

**GetAI_npc_demetria**
Global function. Factory for creating `npc_demetriaAI` instances.

**go_darrowshire_triggerAI**
Constructor for the Darrowshire Trigger AI. Sets respawn time, summon limit, and initializes faction logic.

**Reset**
Determines the defender faction based on the nearest player's team. Resets phase steps, timers, and mob lists.

**OnRemoveFromWorld**
Ensures `DespawnAll` is called if the game object is removed before the event completes.

**DespawnGuid**
Helper to despawn a specific creature by GUID and clear the GUID.

**DespawnAll**
Marks cleanup as done, stops timers, despawns all summoned mobs (except Joseph and Davil), and deletes the trigger game object.

**JustSummoned**
Handles newly summoned creatures in the battle. Sets factions, movement types, and home positions based on the creature entry.

**SummonedMovementInform**
Manages patrol paths for Davil, Redpath, and Bloodletter by moving them to the next point in their sequence.

**SummonedCreatureJustDied**
Handles boss deaths. Triggers phase transitions, plays dialogue, and spawns subsequent bosses or NPCs.

**UpdateAI**
Main update loop. Manages phase transitions, spawns waves of mobs based on timers and phase, and patrols key NPCs.

**GetAI_go_darrowshire_trigger**
Global function. Factory for creating `go_darrowshire_triggerAI` instances.

**npc_joseph_redpathAI**
Constructor for Joseph Redpath's AI. Initializes state and calls `Reset`.

**Reset#7**
Override stub. Does nothing.

**BeginEvent#2**
Starts the cutscene event, setting the initial timer and step.

**MovementInform#3**
Handles movement completion during the cutscene, triggering dialogue and facing adjustments.

**UpdateAI#5**
Drives the cutscene progression via timers and steps, playing dialogue and moving NPCs.

**GetAI_npc_joseph_redpath**
Global function. Factory for creating `npc_joseph_redpathAI` instances.

**GossipHello_npc_joseph_redpath**
Global function. Shows gossip menu, grants quest credit, plays emote, and starts the cutscene.

**EffectDummyGameObj_go_mark_of_detonation**
Global function. Handles the spell effect for placing explosives. Grants credit, kills the structure, and respawns fire game objects.

**npc_guard_didierAI**
Constructor for Guard Didier's AI. Initializes state and calls `Reset`.

**Reset#6**
Override stub. Does nothing.

**JustRespawned**
Resets the mule death flag, sets passive react state, and sets the default gossip menu.

**JustDied**
Resets the mule death flag and despawns all alive group members.

**JustReachedHome**
If the mule died, plays crying emote, sets specific gossip, and despawns the group after a delay. Otherwise, sets passive react state.

**GroupMemberJustDied**
Sets the `m_muleDied` flag to true.

**EnableCombat#2**
Switches the group to aggressive react state and starts combat with the attacker.

**DamageTaken#3**
If passive, enables combat and starts attack.

**AttackStart#2**
If passive, checks distance. If close, enables combat. Otherwise, adds threat to prevent evasion.

**GetAI_npc_guard_didier**
Global function. Factory for creating `npc_guard_didierAI` instances.

**npc_caravan_muleAI**
Constructor for the Caravan Mule AI. Sets passive react state.

**Reset#2**
Sets passive react state.

**EnableCombat**
Switches the group to aggressive react state and starts combat with the attacker.

**DamageTaken**
If passive, enables combat and starts attack.

**AttackStart**
If passive, checks distance. If close, enables combat. Otherwise, adds threat to prevent evasion.

**GetAI_npc_caravan_mule**
Global function. Factory for creating `npc_caravan_muleAI` instances.

**AddSC_eastern_plaguelands**
Registration function. Creates and registers all scripts defined in this unit with the script manager.

---

<!-- machine-true, projected from graph.json -->

## Map — eastern_plaguelands

*Source:* eastern_plaguelands.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| DeathPostSpawn | ctor | — | — | — |
| npc_eris_havenfireAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/SetCreatureSummonLimit | — | — |
| GetPlayer | method | Map.Main/GetPlayer, ObjectGuid/ObjectGuid#5, WorldObject.Object/GetMap | — | — |
| Reset#4 | method | Creature.Main/EnableMoveInLosEvent | — | — |
| AttackedBy | method | — | — | — |
| MoveInLineOfSight | method | Creature.MotionMaster/MoveChase, Object/GetGUID, Object/GetTypeId, Object/IsPet, Unit.Main/GetMotionMaster, Unit.Main/SetInCombatWith, WorldObject.Object/SummonCreature#2 | — | — |
| SetAttackOnPeasantOrPlayer | method | Creature.MotionMaster/MoveChase, GridSearchers/GetCreatureListWithEntryInGrid, MotionMaster/Clear, shared_Util/urand, Unit.Main/AddThreat, Unit.Main/GetMotionMaster, Unit.Main/GetVictim, Unit.Main/IsAlive, Unit.Main/SendMeleeAttackStart, Unit.Main/SendMeleeAttackStop | — | — |
| DespawnAll#2 | method | Creature.Main/ForcedDespawn, GameObject/Delete, GridSearchers/GetCreatureListWithEntryInGrid, Map.Main/GetGameObject, ObjectGuid/ObjectGuid#5, Unit.Main/IsAlive, WorldObject.Object/GetMap | — | — |
| JustSummoned#2 | method | Object/GetEntry, Object/GetGUID, SpellCaster/CastSpell#2, Unit.Main/SetSheath, WorldObject.Object/SetFlag | — | — |
| SummonedMovementInform#2 | method | Creature.Main/ForcedDespawn, Object/GetEntry, Object/GetGUID, ScriptMgr/DoScriptText | — | — |
| SummonedCreatureJustDied#2 | method | Object/GetEntry, Object/GetGUID, WorldObject.Object/SummonGameObject | — | — |
| FailEvent | method | Creature.Main/DisappearAndDie, Player.Main/FailQuest, Player.Main/GetQuestStatus, ScriptMgr/DoScriptText, Unit.Main/CombatStop, WorldObject.Object/AddObjectToRemoveList, WorldObject.Object/FindNearestGameObject | — | — |
| BeginEvent | method | Object/GetGUID, WorldObject.Object/FindNearestGameObject, WorldObject.Object/SummonCreature#2, WorldObject.Object/SummonGameObject | — | — |
| NewWave | method | ScriptMgr/DoScriptText, shared_Util/urand, WorldObject.Object/GetRandomPoint, WorldObject.Object/SummonCreature#2 | — | — |
| GenerateWaveNumber | method | shared_Util/urand | — | — |
| CompleteEvent | method | Player.Main/AreaExploredOrEventHappens, Player.Main/GetQuestStatus, ScriptMgr/DoScriptText, Unit.Main/CombatStop, WorldObject.Object/AddObjectToRemoveList, WorldObject.Object/FindNearestGameObject | — | — |
| UpdateAI#3 | method | CreatureAI/DoCastSpellIfCan, Map.Main/GetCreature, ObjectGuid/ObjectGuid#5, ScriptMgr/DoScriptText, shared_Util/urand, SpellCaster/CastSpell#2, Unit.Main/IsAlive, Unit.Main/SetCombatTimer, WorldObject.Object/GetMap | — | — |
| QuestAccept_npc_eris_havenfire | function | Creature.Main/AI, QuestDef/GetQuestId | — | — |
| GetAI_npc_eris_havenfire | function | — | — | — |
| npc_eris_havenfire_peasantAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#5 | method | CreatureAI/SetCombatMovement, shared_Util/urand, WorldObject.Object/GetRandomPoint | — | — |
| KilledUnit | method | — | — | — |
| DamageTaken#2 | method | Object/GetEntry, shared_Util/urand | — | — |
| SpellHit | method | Creature.Main/AI, CreatureAI/AttackStart, Object/GetGUID, Object/IsPlayer, shared_Util/urand, SpellCaster/CastSpell#2, WorldObject.Object/FindNearestCreature, WorldObject.Object/SummonCreature#2 | — | — |
| MoveInLineOfSight#2 | method | — | — | — |
| MovementInform#2 | method | Creature.MotionMaster/MovePoint, Object/GetEntry, Unit.Main/GetMotionMaster, Unit.Main/SetWalk | — | — |
| UpdateAI#4 | method | Creature.MotionMaster/MovePoint, Object/GetEntry, ScriptMgr/DoScriptText, shared_Util/urand, Unit.Main/GetMotionMaster, Unit.Main/SetWalk, WorldObject.Object/IsWalking | — | — |
| GetAI_npc_eris_havenfire_peasant | function | — | — | — |
| npc_demetriaAI | ctor | ScriptedAI/ScriptedAI | — | — |
| MovementInform | method | Creature.Main/ForcedDespawn, Creature.Main/JoinCreatureGroup, Object/GetGUID, WorldObject.Object/FindNearestCreature, WorldObject.Object/GetOrientation, WorldObject.Object/GetPosition#2, WorldObject.Object/SummonCreature#2 | — | — |
| JustDied | method | — | — | — |
| DespawnTroopers | method | Map.Main/GetCreature, ObjectGuid/ObjectGuid#5, WorldObject.Object/AddObjectToRemoveList, WorldObject.Object/GetMap | — | — |
| UpdateAI#2 | method | Creature.Main/SelectAttackingTarget, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, FriendlyCCedInRangeCheck/FriendlyCCedInRangeCheck, shared_Util/urand, Unit.Main/GetHealthPercent, Unit.Main/GetSpellAuraHolder#2, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget, WorldObject.Object/FindNearestCreature | — | — |
| Reset#3 | method | Unit.Main/AddAura | — | — |
| DoRessurectUnit | method | Creature.Main/AI, Creature.Main/Respawn, Creature.Main/Update, CreatureAI/AttackStart, Unit.Main/GetMaxHealth, Unit.Main/NearTeleportTo, Unit.Main/SendSpellGo, Unit.Main/SetHealth, Unit.Main/SetStandState, WorldObject.Object/GetOrientation, WorldObject.Object/GetPosition#2, WorldObject.Object/RemoveFlag | — | — |
| GetAI_npc_demetria | function | — | — | — |
| go_darrowshire_triggerAI | ctor | GameObject/SetRespawnTime, GameObjectAI/GameObjectAI, WorldObject.Object/SetCreatureSummonLimit | — | — |
| Reset | method | LinkedListHead/isEmpty, Map.Main/GetPlayers, Player.Main/GetQuestStatus, Player.Main/GetTeam, Player.Main/IsGameMaster, Unit.Main/IsAlive, WorldObject.Object/GetAreaId, WorldObject.Object/GetMap, WorldObject.Object/IsWithinDist | — | — |
| OnRemoveFromWorld | method | — | — | — |
| DespawnGuid | method | Creature.Main/ForcedDespawn, Map.Main/GetCreature, ObjectGuid/Clear, WorldObject.Object/GetMap | — | — |
| DespawnAll | method | Creature.Main/ForcedDespawn, Map.Main/GetCreature, Object/GetEntry, Unit.Main/IsAlive, WorldObject.Object/DeleteLater, WorldObject.Object/DespawnNearCreaturesByEntry, WorldObject.Object/GetMap | — | — |
| JustSummoned | method | Creature.Main/ForcedDespawn, Creature.Main/SetHomePosition, Creature.MotionMaster/MovePoint, Creature.MotionMaster/MoveRandom, Object/GetEntry, Object/GetGUID, ObjectGuid/ObjectGuid#5, Unit.Main/GetMotionMaster, Unit.Main/SetFactionTemplateId, Unit.Main/SetWalk, WorldObject.Object/SetFlag | — | — |
| SummonedMovementInform | method | Creature.MotionMaster/MovePoint, Creature.MotionMaster/MoveRandom, Object/GetEntry, Unit.Main/GetMotionMaster, Unit.Main/SetWalk | — | — |
| SummonedCreatureJustDied | method | Object/GetEntry, ScriptMgr/DoScriptText, WorldObject.Object/FindNearestCreature, WorldObject.Object/SummonCreature#2 | — | — |
| UpdateAI | method | Creature.Main/AI, Creature.Main/ForcedDespawn, Creature.Main/SetHomePosition, Creature.MotionMaster/GetCurrentMovementGeneratorType, Creature.MotionMaster/MovePoint, CreatureAI/AttackStart, Map.Main/GetCreature, Object/GetEntry, Object/GetObjectGuid, ScriptMgr/DoScriptText, shared_Util/urand, Unit.Main/DealDamage, Unit.Main/GetHealth, Unit.Main/GetMotionMaster, Unit.Main/IsAlive, Unit.Main/IsInCombat, Unit.Main/SetWalk, WorldObject.Object/AddObjectToRemoveList, WorldObject.Object/GetGameObjectListWithEntryInGrid, WorldObject.Object/GetMap, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/GetRandomPoint, WorldObject.Object/SummonCreature#2 | — | — |
| GetAI_go_darrowshire_trigger | function | — | — | — |
| npc_joseph_redpathAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#7 | method | — | — | — |
| BeginEvent#2 | method | — | — | — |
| MovementInform#3 | method | Creature.MotionMaster/MovePoint, ScriptMgr/DoScriptText, Unit.Main/GetMotionMaster, Unit.Main/SetFacingToObject, Unit.Main/SetWalk, WorldObject.Object/FindNearestCreature, WorldObject.Object/GetContactPoint | — | — |
| UpdateAI#5 | method | Creature.Main/ForcedDespawn, Creature.MotionMaster/MovePoint, ScriptMgr/DoScriptText, Unit.Main/GetMotionMaster, WorldObject.Object/FindNearestCreature, WorldObject.Object/RemoveFlag, WorldObject.Object/SetFlag | — | — |
| GetAI_npc_joseph_redpath | function | — | — | — |
| GossipHello_npc_joseph_redpath | function | Creature.Main/AI, GossipDef/SendGossipMenu, Object/GetGUID, Object/GetObjectGuid, ObjectGuid/ObjectGuid#5, Player.Main/GetQuestStatus, Player.Main/KilledMonsterCredit, Unit.Main/HandleEmote | — | — |
| EffectDummyGameObj_go_mark_of_detonation | function | GameObject/Despawn, Map.Main/ScriptCommandStartDirect, Object/GetEntry, Object/GetGUIDLow, Object/GetObjectGuid, Object/ToPlayer, Player.Main/KilledMonsterCredit, ScriptInfo/ScriptInfo, Unit.Main/DealDamage, Unit.Main/GetHealth, WorldObject.Object/FindNearestCreature, WorldObject.Object/GetMap | — | — |
| npc_guard_didierAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#6 | method | — | — | — |
| JustRespawned | method | Creature.Main/SetDefaultGossipMenuId, Unit.Main/SetReactState | — | — |
| JustDied#2 | method | Creature.Main/DespawnOrUnsummon, Creature.Main/GetCreatureGroup, CreatureGroups/DoForAllMembers, Unit.Main/IsAlive, WorldObject.Object/GetMap | — | — |
| JustReachedHome | method | Creature.Main/DespawnOrUnsummon, Creature.Main/GetCreatureGroup, Creature.Main/SetDefaultGossipMenuId, Creature.MotionMaster/MoveIdle, CreatureGroups/DoForAllMembers, MotionMaster/Clear, Unit.Main/GetMotionMaster, Unit.Main/HandleEmote, Unit.Main/HasAura#2, Unit.Main/IsAlive, Unit.Main/RemoveAurasDueToSpell, Unit.Main/SetReactState, WorldObject.Object/GetMap, WorldObject.Object/MonsterSay#2, WorldObject.Object/SetFlag | — | — |
| GroupMemberJustDied | method | — | — | — |
| EnableCombat#2 | method | Creature.Main/AI, Creature.Main/GetCreatureGroup, CreatureAI/AttackStart, CreatureGroups/DoForAllMembers, Unit.Main/HasReactState, Unit.Main/IsAlive, Unit.Main/SetReactState, WorldObject.Object/GetMap | — | — |
| DamageTaken#3 | method | Unit.Main/HasReactState | — | — |
| AttackStart#2 | method | Creature.Main/GetAttackDistance, CreatureAI/AttackStart, Unit.Main/AddThreat, Unit.Main/HasReactState, WorldObject.Object/IsWithinDistInMap | — | — |
| GetAI_npc_guard_didier | function | — | — | — |
| npc_caravan_muleAI | ctor | ScriptedAI/ScriptedAI, Unit.Main/SetReactState | — | — |
| Reset#2 | method | Unit.Main/SetReactState | — | — |
| EnableCombat | method | Creature.Main/AI, Creature.Main/GetCreatureGroup, CreatureAI/AttackStart, CreatureGroups/DoForAllMembers, Unit.Main/HasReactState, Unit.Main/IsAlive, Unit.Main/SetReactState, WorldObject.Object/GetMap | — | — |
| DamageTaken | method | Unit.Main/HasReactState | — | — |
| AttackStart | method | Creature.Main/GetAttackDistance, CreatureAI/AttackStart, Unit.Main/AddThreat, Unit.Main/HasReactState, WorldObject.Object/IsWithinDistInMap | — | — |
| GetAI_npc_caravan_mule | function | — | — | — |
| AddSC_eastern_plaguelands | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
