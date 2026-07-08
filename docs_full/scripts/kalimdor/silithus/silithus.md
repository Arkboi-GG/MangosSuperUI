# silithus

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# silithus.cpp

**Purpose & Responsibilities**
This translation unit implements scripted artificial intelligence (AI) and event handlers for various non-player characters (NPCs), game objects (GOs), and quests within the **Silithus** zone of the World of Warcraft emulator. It covers content related to the **War Effort** (Qiraji war effort against the Kal'dorei/Elven forces), specific quest chains (such as "The Calling," "Field Duty," and "A Pawn on the Eternal Board"), and ambient zone events.

The unit contains distinct AI classes for:
1.  **Wind Stones**: Game objects that summon elemental spirits (Templars, Dukes, Royals) when activated by spells.
2.  **Solenor the Slayer**: A complex two-phase boss encounter involving a transformation from "Nelson the Nice" and a mechanic tied to Hunter players.
3.  **Colossi**: Large bosses (Zora, Regal, Ashi) that participate in the War Effort and trigger reward events upon death.
4.  **Geologist Larksbane**: An NPC involved in a scripted dialogue sequence ("The Calling") involving summoned crystals.
5.  **Emissary Romankhan**: A boss with mana-regeneration mechanics tied to player deaths.
6.  **Anachronos the Ancient**: The central figure in the "A Pawn on the Eternal Board" cinematic event, coordinating dragons, warriors, and gate animations.
7.  **Scarab Gong**: A game object that triggers the opening of Ahn'Qiraj gates when rung by a champion.
8.  **Krug Skullsplit & Allies**: NPCs involved in the "Field Duty" quest chain, including a waypoint-based hunter killer and healing allies.

**Cross-Unit Boundaries**
This unit relies heavily on the core engine components (`Creature`, `GameObject`, `Unit`, `Map`, `World`, `ScriptMgr`) to manipulate entity states, positions, and behaviors. It interacts with:
*   **`Creature.Main` / `CreatureAI`**: To control movement, combat actions, spell casting, and threat management.
*   **`GameObject`**: To manage door/button states for the Ahn'Qiraj gates and wind stone activations.
*   **`World` / `ObjectMgr`**: To access global saved variables (for War Effort tracking) and broadcast messages.
*   **`ScriptMgr`**: To register these scripts with the server's scripting system via `AddSC_silithus`.

**Data Model**
This unit does not directly query or modify database tables via SQL statements. It interacts with persistent state through the `ObjectMgr`'s saved variable system (`GetSavedVariable`, `SetSavedVariable`). These variables (e.g., `VAR_WE_HIVE_REWARD`, `VAR_WE_GONG_TIME`, `VAR_WE_GONG_BANG_TIMES`) act as runtime memory for event states, effectively replacing direct table manipulation for these specific logic flows. No explicit SQL schema is referenced or required for understanding the C++ logic herein.

**Notable Implementation Details**
*   **Lambda Events in `go_wind_stoneAI`**: The wind stone AI uses lambda functions added to `m_Events` to handle delayed actions (facing, text, immunity removal) after summoning creatures. This avoids blocking the main thread and allows precise timing relative to the summon event.
*   **Thread-Safe Event Triggering in `npc_colossusAI`**: The `JustDied` method notes that starting events directly inside a map update is not thread-safe. Instead, it sets a saved variable and nudges the world update timer to ensure the event handler runs safely in the main loop.
*   **Complex State Machine in `npc_anachronos_the_ancientAI`**: This AI manages a 55-stage event sequence using `m_uiEventStage` and `m_uiEventTimer`. It coordinates multiple summoned entities (dragons, warriors), animates gates, and handles player quest completion. The use of `MovementInform` and `SummonedMovementInform` allows the AI to react to pathfinding milestones.
*   **Mana Regeneration Mechanic in `npc_Emissary_RomankhanAI`**: Romankhan regenerates mana based on the deaths of players he has previously hit with specific spells. This is tracked via a GUID array (`PlayerGuids`) and checked periodically in `UpdateAI`.
*   **Waypoint Navigation in `mob_HiveRegal_HunterKillerAI`**: The Hunter Killer follows a predefined array of waypoints (`HunterKillerWaypoint`) before engaging in combat. The timer for each segment is calculated dynamically based on distance and speed.

## Member Reference

**go_wind_stoneAI**
Constructor for the Wind Stone AI. Initializes the base `GameObjectAI`.

**GetSpawnText**
Static helper method that returns a random text ID based on the NPC entry of the summoned creature (Templar, Duke, or Royal). Used to play appropriate spawn sounds/dialogue.

**OnActivateBySpell**
Handles the activation of the Wind Stone by a spell. Determines which creature to summon based on the spell ID, calculates spawn coordinates (using hardcoded positions for specific stones), summons the creature, and schedules delayed actions (facing, text, immunity removal, attack start) using lambda events. Finally, despawns the wind stone itself.

**GetAIgo_wind_stone**
Factory function that creates and returns a new instance of `go_wind_stoneAI`.

**npc_solenorAI**
Constructor for Solenor the Slayer AI. Initializes timers and flags, then calls `Reset`.

**Reset#10**
Resets the AI state depending on whether the creature is Nelson the Nice or Solenor the Slayer. For Nelson, it sets up waypoint movement and gossip flags. For Solenor, it initializes combat timers and applies a soul flame aura.

**Transform**
Converts Nelson the Nice into Solenor the Slayer by updating the creature entry, resetting position/movement type, and calling `Reset` to initialize Solenor's combat state.

**BeginEvent**
Starts the transformation event for Nelson the Nice. Stores the player's GUID, clears movement, sets idle motion, removes gossip flags, and sets the transform flag.

**Aggro#3**
Handles aggro for Solenor. If the aggressor is a Hunter and matches the stored hunter GUID (or if no hunter is stored), it records the hunter. Otherwise, it triggers `DemonDespawn`.

**EnterEvadeMode#2**
Removes guardians and calls the parent `EnterEvadeMode`.

**JustSummoned#2**
When Solenor summons a creature (e.g., Creeping Doom), it attacks Solenor's current victim if one exists.

**JustDied#4**
Handles Solenor's death. Resets his home position and calculates respawn delay based on active session count (scaling down on busy servers). Saves the respawn time.

**DemonDespawn**
Despawns Solenor and summons "The Cleaner" to attack all of Solenor's previous threats. Sets respawn times and saves them.

**SpellHit**
Triggers a counter-spell (`SPELL_CRIPPLING_CLIP`) and emote if Solenor is hit by Wing Clip.

**UpdateAI#9**
Main update loop. Handles Nelson's transformation timer and Solenor's combat abilities (Creeping Doom, Dreadful Fright, Soul Flame). Checks for conditions to despawn (multiple threats or hunter death) and performs melee attacks.

**OnScriptEventHappened**
Called when a script event occurs. If invoked by a player, it starts the transformation event via `BeginEvent`.

**GetAI_npc_solenor**
Factory function that creates and returns a new instance of `npc_solenorAI`.

**npc_creeping_doomAI**
Constructor for Creeping Doom AI. Calls `Reset`.

**Reset#9**
Empty reset function.

**DamageTaken**
Redirects threat from Creeping Doom to its owner (Solenor) when damaged. Ensures the owner is in combat with the attacker.

**GetAI_npc_creeping_doom**
Factory function that creates and returns a new instance of `npc_creeping_doomAI`.

**npc_colossusAI**
Constructor for Colossus AI. Plays an intro text based on the specific Colossus entry (Zora, Regal, Ashi) and calls `Reset`.

**Reset#8**
Resets combat timers and flags.

**SpellHitTarget#2**
Empty override.

**Aggro#2**
Empty override.

**EnterEvadeMode**
Removes auras, deletes threat list, and sets idle movement to prevent erratic behavior after evading.

**UpdateAI#8**
Main update loop. Handles the "Colossal Smash" ability with emotes and timers. Performs melee attacks.

**JustDied#3**
Handles the Colossus death. Updates a saved variable (`VAR_WE_HIVE_REWARD`) to trigger a war effort reward event. Nudges the world update timer to process the event safely.

**GetAI_npc_colossus**
Factory function that creates and returns a new instance of `npc_colossusAI`.

**npc_Geologist_LarksbaneAI**
Constructor for Geologist Larksbane AI. Calls `Reset`.

**Reset#3**
Sets quest giver and gossip flags, clears crystal GUIDs, and resets action timers.

**QuestCompleted**
Triggered when the quest "The Calling" is completed. Removes quest/gossip flags, summons three glyphed crystals, and starts the dialogue sequence.

**Larksbane_DoAction**
Executes the next step in the dialogue sequence. Handles emotes, speech, crystal usage/deletion, and interactions with nearby NPCs (Baristolth). Increments the action counter.

**UpdateAI#3**
Checks if an action is pending and executes `Larksbane_DoAction` if the timer expires.

**GetAI_npc_Geologist_Larksbane**
Factory function that creates and returns a new instance of `npc_Geologist_LarksbaneAI`.

**QuestComplete_npc_Geologist_Larksbane**
Global script hook. Checks if the completed quest is "The Calling" and triggers `QuestCompleted` on the Larksbane AI.

**npc_Emissary_RomankhanAI**
Constructor for Emissary Romankhan AI. Calls `Reset`.

**Reset#2**
Initializes combat timers, clears player GUIDs, disables mana regeneration, and sets mana to zero.

**Aggro**
Enables mana regeneration state when aggroed.

**GetManaPercent**
Helper function to calculate current mana percentage.

**SpellHitTarget**
Tracks players hit by specific spells (Wilt, Suffering of Sanity, System Shock) in the `PlayerGuids` array.

**UpdateAI#2**
Main update loop. Handles spell casting (System Shock, Wilt, Suffering of Sanity). Checks for dead players in the `PlayerGuids` array to regenerate mana. Performs melee attacks.

**GetAI_npc_Emissary_Romankhan**
Factory function that creates and returns a new instance of `npc_Emissary_RomankhanAI`.

**npc_anachronos_the_ancientAI**
Constructor for Anachronos AI. Calls `Reset`.

**Reset#7**
Resets event stage/timers, clears GUIDs, and sets spawning flag.

**BeginScene**
Starts the cinematic event by setting the initial timer.

**AbortScene**
Stops the event, despawns summoned entities, closes gates quietly, and makes Anachronos invisible/despawned for a cooldown period.

**JustDied#2**
Calls `AbortScene` if Anachronos dies during the event.

**SetupAQGate**
Locates the Ahn'Qiraj gate game objects and calls `AnimateAQGate` for each.

**AnimateAQGate**
Controls the state and visibility of a single gate object based on the event phase (Open, Prepare Close, Close, Reset, etc.).

**DoSummonDragons**
Summons the four dragon aspects (Fandral, Arygos, Caelestrasz, Merithra) at predefined locations.

**DoSummonWarriors**
Summons Kal'dorei Infantry and Qiraji warriors (Wasps, Drones, Tanks, Conquerors) at random points around a central anchor.

**DoUnsummonArmy**
Despawns all summoned warriors by iterating through their GUID list.

**AddKaldoreiThreat**
Adds threat from a Qiraji warrior to all Kal'dorei Infantry to initiate combat between the two factions.

**JustSummoned**
Handles the arrival of summoned entities. Stores GUIDs for dragons, sets faction templates for warriors, initiates combat between Qiraji and Kal'dorei, and sets respawn delays.

**DoCastTriggerSpellOnEnemies**
Casts visual/damage spells on Qiraji enemies during the cinematic.

**DoTimeStopArmy**
Freezes all summoned warriors by stopping movement, removing attackers, and entering evade mode.

**MovementInform**
Handles Anachronos reaching pathfinding points (Gate, Scepter pieces, Exit). Triggers dialogue, spells, and stand state changes.

**SummonedMovementInform**
Handles summoned entities (Fandral, Dragons) reaching pathfinding points. Triggers dialogue, movement, and despawns.

**UpdateAI#7**
Main update loop for the cinematic event. Executes the 55-stage sequence, managing timers, dialogue, summoning, movement, and spell casting.

**GetAI_npc_anachronos_the_ancient**
Factory function that creates and returns a new instance of `npc_anachronos_the_ancientAI`.

**QuestAcceptGO_crystalline_tear**
Global script hook. When the quest "A Pawn on the Eternal Board" is accepted from the Crystalline Tear GO, it summons Anachronos and starts the cinematic event.

**scarab_gongAI**
Constructor for Scarab Gong AI. Initializes event stage and timers.

**UpdateAI#10**
Main update loop. Delegates to `HandleOpeningStage`, `HandleWarStage`, or `ResetAQGates` based on the current stage.

**NextStage**
Advances the event stage and resets the step/timer.

**HandleOpeningStage**
Animates the opening of the three Ahn'Qiraj gates (Roots, Runes, Barrier) with sounds and delays.

**HandleWarStage**
Records the gong bang time in a saved variable and triggers a world update. Calls `EventDone`.

**BeginAQOpeningEvent**
Initiates the gate opening sequence. Aborts any ongoing Anachronos event, checks gate state, broadcasts the champion announcement, and starts the opening stage.

**EventDone**
Resets the event stage to opening gates.

**ResetAQGates**
Closes and resets the state of all three Ahn'Qiraj gates.

**GetAIscarab_gong**
Factory function that creates and returns a new instance of `scarab_gongAI`.

**GOHello_scarab_gong**
Global script hook. Allows players to view quests associated with the Scarab Gong.

**QuestRewarded_scarab_gong**
Global script hook. When the quest "Bang a Gong" is rewarded, it increments a saved variable for crystal awards. If it's the first bang, it triggers `BeginAQOpeningEvent`.

**mob_HiveRegal_HunterKillerAI**
Constructor for Hunter Killer AI. Initializes timers and sets faction.

**Reset**
Resets combat timers.

**GetVictimInRangePlayerOnly**
Helper function to find a player target within a specific range from the threat list.

**UpdateAI**
Main update loop. First, moves the Hunter Killer along a waypoint path until it reaches the camp. Then, engages in combat using Thunder Clap, Charge, Cleave, and Fear abilities.

**GetAI_mob_HiveRegal_HunterKiller**
Factory function that creates and returns a new instance of `mob_HiveRegal_HunterKillerAI`.

**npc_Krug_SkullSplitAI**
Constructor for Krug Skullsplit AI. Calls `Reset` and `ResetEvent`.

**Reset#4**
Empty override.

**GetEventStatus**
Returns the current status of the Field Duty event.

**ResetEvent**
Resets the event state, despawns the Hunter Killer, clears GUIDs, and resets allied NPC positions.

**StartEvent**
Initiates the Field Duty event. Plays dialogue, summons the Hunter Killer, and updates the event status.

**CompleteEvent**
Marks the event as complete, sets a long reset timer, and re-enables quest giving.

**InitOtherNPCsGuids**
Finds and stores the GUIDs of nearby allied NPCs (Merok, Shai).

**ResetOtherNPCsPosition**
Moves allied NPCs back to their spawn positions.

**SummonedCreatureJustDied**
If the Hunter Killer dies, completes the event.

**SummonedCreatureDespawn**
If the Hunter Killer despawns without dying, resets the event.

**JustDied**
Resets the event if Krug dies.

**UpdateAI#4**
Main update loop. Manages event reset timers, plays dialogue sequences, triggers grunt speeches, cleans up unwanted states, and handles melee combat.

**GetAI_npc_Krug_SkullSplit**
Factory function that creates and returns a new instance of `npc_Krug_SkullSplitAI`.

**GossipHello_npc_Krug_SkullSplit**
Global script hook. Displays gossip options based on quest status and event state.

**GossipSelect_npc_Krug_SkullSplit**
Global script hook. Handles gossip selections to start the event.

**npc_MerokAI**
Constructor for Merok AI. Calls `Reset`.

**Reset#5**
Resets healing timer.

**UpdateAI#5**
Main update loop. Heals the lowest HP friendly unit with Healing Wave and performs melee attacks.

**GetAI_npc_Merok**
Factory function that creates and returns a new instance of `npc_MerokAI`.

**npc_ShaiAI**
Constructor for Shai AI. Calls `Reset`.

**Reset#6**
Resets healing timer.

**UpdateAI#6**
Main update loop. Heals the lowest HP friendly unit with Flash Heal and performs melee attacks.

**GetAI_npc_Shai**
Factory function that creates and returns a new instance of `npc_ShaiAI`.

**AddSC_silithus**
Registration function. Creates and registers all script instances defined in this unit with the `ScriptMgr`.

---

<!-- machine-true, projected from graph.json -->

## Map — silithus

*Source:* silithus.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| go_wind_stoneAI | ctor | GameObjectAI/GameObjectAI | — | — |
| GetSpawnText | method | — | — | — |
| OnActivateBySpell | method | Creature.Main/AI, Creature.Main/SetLootRecipient, CreatureAI/AttackStart, GameObject/Despawn, GameObject/isSpawned, Log.Main/Out, Map.Main/GetPlayer, Object/GetEntry, Object/GetObjectGuid, ScriptMgr/DoScriptText, shared_Util/urand, Unit.Main/SetFacingToObject, WorldObject.Object/GetMap, WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/RemoveFlag, WorldObject.Object/SummonCreature#2 | — | — |
| GetAIgo_wind_stone | function | — | — | — |
| npc_solenorAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#10 | method | Creature.Main/SetDefaultMovementType, Creature.Main/SetHomePosition, Creature.Main/SetRespawnDelay, Creature.Main/SetRespawnTime, Creature.MotionMaster/GetCurrentMovementGeneratorType, Creature.MotionMaster/Initialize, Object/GetEntry, ObjectGuid/Clear, shared_Util/urand, Unit.Main/AddAura, Unit.Main/GetMotionMaster, Unit.Main/NearTeleportTo, WorldObject.Object/SetUInt32Value | — | — |
| Transform | method | Creature.Main/SetDefaultMovementType, Creature.Main/SetHomePosition, Creature.Main/UpdateEntry, Creature.MotionMaster/Initialize, Unit.Main/GetMotionMaster, WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ | — | — |
| BeginEvent | method | Creature.MotionMaster/MoveIdle, MotionMaster/Clear, Unit.Main/GetMotionMaster, WorldObject.Object/SetUInt32Value | — | — |
| Aggro#3 | method | Object/GetObjectGuid, ObjectGuid/IsEmpty, ObjectGuid/operator==, Unit.Main/GetClass | — | — |
| EnterEvadeMode#2 | method | ScriptedAI/EnterEvadeMode, Unit.Main/RemoveGuardians | — | — |
| JustSummoned#2 | method | Creature.Main/AI, CreatureAI/AttackStart, Unit.Main/GetVictim | — | — |
| JustDied#4 | method | Creature.Main/SaveRespawnTime, Creature.Main/SetHomePosition, Creature.Main/SetRespawnDelay, Creature.Main/SetRespawnTime, World/GetActiveSessionCount | — | — |
| DemonDespawn | method | Creature.Main/AI, Creature.Main/ForcedDespawn, Creature.Main/SaveRespawnTime, Creature.Main/SetHomePosition, Creature.Main/SetRespawnDelay, Creature.Main/SetRespawnTime, CreatureAI/AttackStart, ThreatManager/getThreatList, Unit.Main/AddThreat, Unit.Main/GetThreatManager, Unit.Main/IsAlive, Unit.Main/RemoveGuardians, Unit.Main/SetInCombatWith, WorldObject.Object/GetAngle, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/SummonCreature#2 | — | — |
| SpellHit | method | CreatureAI/DoCastSpellIfCan, ScriptMgr/DoScriptText | — | — |
| UpdateAI#9 | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, shared_Util/urand, SpellCaster/CastSpell#2, ThreatManager/getThreatList, Unit.Main/GetThreatManager, Unit.Main/GetVictim, Unit.Main/HandleEmote, Unit.Main/HasAura#2, Unit.Main/IsAlive, Unit.Main/IsInCombat, Unit.Main/RemoveAurasDueToSpell, Unit.Main/SelectHostileTarget, WorldObject.Object/GetDistance2d#3 | — | — |
| OnScriptEventHappened | method | Object/GetObjectGuid, Object/IsPlayer | — | — |
| GetAI_npc_solenor | function | — | — | — |
| npc_creeping_doomAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#9 | method | — | — | — |
| DamageTaken | method | CreatureAI/DamageTaken, Unit.Main/AddThreat, Unit.Main/GetCharmerOrOwner, Unit.Main/SetInCombatWith | — | — |
| GetAI_npc_creeping_doom | function | — | — | — |
| npc_colossusAI | ctor | Object/GetEntry, ScriptedAI/ScriptedAI, ScriptMgr/DoScriptText | — | — |
| Reset#8 | method | — | — | — |
| SpellHitTarget#2 | method | — | — | — |
| Aggro#2 | method | — | — | — |
| EnterEvadeMode | method | Creature.MotionMaster/MoveIdle, Unit.Main/DeleteThreatList, Unit.Main/GetMotionMaster, Unit.Main/RemoveAllAuras | — | — |
| UpdateAI#8 | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget, WorldObject.Object/MonsterTextEmote | — | — |
| JustDied#3 | method | CreatureAI/JustDied, Object/GetEntry, ObjectMgr/GetSavedVariable, ObjectMgr/SetSavedVariable, World/GetWorldUpdateTimerInterval, World/SetWorldUpdateTimer | — | — |
| GetAI_npc_colossus | function | — | — | — |
| npc_Geologist_LarksbaneAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#3 | method | WorldObject.Object/SetFlag | — | — |
| QuestCompleted | method | Object/GetGUID, WorldObject.Object/RemoveFlag, WorldObject.Object/SummonGameObject | — | — |
| Larksbane_DoAction | method | GameObject/Delete, GameObject/Use, Map.Main/GetGameObject, ObjectGuid/ObjectGuid#5, Unit.Main/HandleEmote, WorldObject.Object/FindNearestCreature, WorldObject.Object/GetMap, WorldObject.Object/MonsterSay#2, WorldObject.Object/MonsterTextEmote#2, WorldObject.Object/SetFlag | — | — |
| UpdateAI#3 | method | — | — | — |
| GetAI_npc_Geologist_Larksbane | function | — | — | — |
| QuestComplete_npc_Geologist_Larksbane | function | Creature.Main/AI, QuestDef/GetQuestId | — | — |
| npc_Emissary_RomankhanAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#2 | method | Creature.Main/ClearCreatureState, shared_Util/urand, Unit.Main/SetPower | — | — |
| Aggro | method | Creature.Main/AddCreatureState | — | — |
| GetManaPercent | method | Unit.Main/GetMaxPower, Unit.Main/GetPower | — | — |
| SpellHitTarget | method | Object/GetGUID, Object/IsPlayer | — | — |
| UpdateAI#2 | method | Creature.Main/SelectAttackingTarget, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, Map.Main/GetUnit, Object/GetTypeId, ObjectGuid/ObjectGuid#5, shared_Util/urand, Unit.Main/GetMaxPower, Unit.Main/GetPower, Unit.Main/GetVictim, Unit.Main/IsDead, Unit.Main/SelectHostileTarget, Unit.Main/SetInCombatWith, Unit.Main/SetPower, WorldObject.Object/GetMap | — | — |
| GetAI_npc_Emissary_Romankhan | function | — | — | — |
| npc_anachronos_the_ancientAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#7 | method | Creature.Main/SetRespawnDelay, ObjectGuid/Clear, WorldObject.Object/SetFlag | — | — |
| BeginScene | method | — | — | — |
| AbortScene | method | Creature.Main/ForcedDespawn, Map.Main/GetCreature, Unit.Main/SetVisibility, WorldObject.Object/GetMap | — | — |
| JustDied#2 | method | — | — | — |
| SetupAQGate | method | GridSearchers/GetClosestGameObjectWithEntry | — | — |
| AnimateAQGate | method | GameObject/GetGoState, GameObject/ResetDoorOrButton, GameObject/SetGoState, GameObject/SetVisible | — | — |
| DoSummonDragons | method | WorldObject.Object/SummonCreature#2 | — | — |
| DoSummonWarriors | method | WorldObject.Object/GetRandomPoint, WorldObject.Object/SummonCreature#2 | — | — |
| DoUnsummonArmy | method | Creature.Main/DisappearAndDie, Map.Main/GetCreature, ObjectGuid/ObjectGuid#5, WorldObject.Object/GetMap | — | — |
| AddKaldoreiThreat | method | Map.Main/GetCreature, Object/GetEntry, ObjectGuid/ObjectGuid#5, Unit.Main/AddThreat, WorldObject.Object/GetMap | — | — |
| JustSummoned | method | Creature.Main/AI, Creature.Main/SetRespawnDelay, Creature.MotionMaster/MoveChase, CreatureAI/AttackStart, GridSearchers/GetClosestCreatureWithEntry, Object/GetEntry, Object/GetObjectGuid, Unit.Main/AddThreat, Unit.Main/GetMotionMaster, Unit.Main/SetFactionTemplateId, WorldObject.Object/SetFlag, WorldObject.Object/SetUInt32Value | — | — |
| DoCastTriggerSpellOnEnemies | method | Map.Main/GetCreature, Object/GetEntry, ObjectGuid/ObjectGuid#5, SpellCaster/CastSpell#2, WorldObject.Object/GetMap, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/SummonGameObject | — | — |
| DoTimeStopArmy | method | Creature.Main/AI, CreatureAI/EnterEvadeMode, Map.Main/GetCreature, MotionMaster/Clear, ObjectGuid/ObjectGuid#5, SpellCaster/CastSpell#2, Unit.Main/AttackStop, Unit.Main/GetMotionMaster, Unit.Main/RemoveAllAttackers, Unit.Main/StopMoving, WorldObject.Object/GetMap, WorldObject.Object/SetFlag | — | — |
| MovementInform | method | CreatureAI/DoCastSpellIfCan, ScriptMgr/DoScriptText, Unit.Main/SetStandState | — | — |
| SummonedMovementInform | method | Creature.Main/ForcedDespawn, Creature.MotionMaster/MovePoint, Object/GetEntry, ScriptMgr/DoScriptText, Unit.Main/GetMotionMaster, Unit.Main/SetFacingToObject | — | — |
| UpdateAI#7 | method | Creature.Main/ForcedDespawn, Creature.Main/SetVirtualItem, Creature.MotionMaster/MovePoint, CreatureAI/DoCastSpellIfCan, GridSearchers/GetClosestCreatureWithEntry, Map.Main/GetCreature, Map.Main/GetPlayer, Object/GetObjectGuid, Player.Main/GroupEventHappens, ScriptMgr/DoScriptText, SpellCaster/CastSpell#2, Unit.Main/AddAura, Unit.Main/GetMotionMaster, Unit.Main/HandleEmote, Unit.Main/SetFacingToObject, Unit.Main/SetFly, Unit.Main/SetStandState, Unit.Main/SetWalk, WorldObject.Object/GetMap, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/SetFlag | — | — |
| GetAI_npc_anachronos_the_ancient | function | — | — | — |
| QuestAcceptGO_crystalline_tear | function | Creature.Main/AI, GridSearchers/GetClosestCreatureWithEntry, Object/GetObjectGuid, QuestDef/GetQuestId, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/SummonCreature#2 | — | — |
| scarab_gongAI | ctor | GameObjectAI/GameObjectAI | — | — |
| UpdateAI#10 | method | — | — | — |
| NextStage | method | — | — | — |
| HandleOpeningStage | method | GameObject/ResetDoorOrButton, GameObject/UseDoorOrButton, WorldObject.Object/PlayDirectSound | — | — |
| HandleWarStage | method | ObjectMgr/SetSavedVariable, World/GetWorldUpdateTimerInterval, World/SetWorldUpdateTimer | — | — |
| BeginAQOpeningEvent | method | Creature.Main/AI, GameObject/GetGoState, GridSearchers/GetClosestCreatureWithEntry, GridSearchers/GetClosestGameObjectWithEntry, Object/GetObjectGuid, World/SendBroadcastTextToWorld | — | — |
| EventDone | method | — | — | — |
| ResetAQGates | method | GameObject/ResetDoorOrButton, GameObject/SetGoState | — | — |
| GetAIscarab_gong | function | — | — | — |
| GOHello_scarab_gong | function | GameObject/GetGoType, Object/GetObjectGuid, Player.Main/PrepareQuestMenu, Player.Main/SendPreparedQuest | — | — |
| QuestRewarded_scarab_gong | function | GameObject/AI, ObjectMgr/GetSavedVariable, ObjectMgr/SetSavedVariable, QuestDef/GetQuestId | — | — |
| mob_HiveRegal_HunterKillerAI | ctor | ScriptedAI/ScriptedAI, Unit.Main/SetFactionTemplateId | — | — |
| Reset | method | shared_Util/urand | — | — |
| GetVictimInRangePlayerOnly | method | Object/ToPlayer, ThreatManager/getThreatList, Unit.Main/GetThreatManager, WorldObject.Object/IsInRange | — | — |
| UpdateAI | method | Creature.Main/SetHomePosition, Creature.MotionMaster/MovePoint, CreatureAI/AttackStart, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, GridSearchers/GetClosestCreatureWithEntry, shared_Util/urand, SpellCaster/IsNonMeleeSpellCasted, Unit.Main/GetMotionMaster, Unit.Main/GetSpeed, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget, Unit.Main/SetFactionTemplateId, WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ | — | — |
| GetAI_mob_HiveRegal_HunterKiller | function | — | — | — |
| npc_Krug_SkullSplitAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#4 | method | — | — | — |
| GetEventStatus | method | — | — | — |
| ResetEvent | method | Creature.Main/ForcedDespawn, Map.Main/GetCreature, ObjectGuid/Clear, WorldObject.Object/AddObjectToRemoveList, WorldObject.Object/GetMap | — | — |
| StartEvent | method | Creature.Main/SetRespawnDelay, Object/GetObjectGuid, ScriptMgr/DoScriptText, Unit.Main/SetFactionTemplateId, WorldObject.Object/RemoveFlag, WorldObject.Object/SummonCreature#2 | — | — |
| CompleteEvent | method | WorldObject.Object/SetFlag | — | — |
| InitOtherNPCsGuids | method | Object/GetObjectGuid, ObjectGuid/ObjectGuid#5, WorldObject.Object/FindNearestCreature | — | — |
| ResetOtherNPCsPosition | method | Creature.MotionMaster/MovePoint, Map.Main/GetCreature, Unit.Main/GetMotionMaster, WorldObject.Object/GetMap | — | — |
| SummonedCreatureJustDied | method | Object/GetObjectGuid, ObjectGuid/operator== | — | — |
| SummonedCreatureDespawn | method | Object/GetObjectGuid, ObjectGuid/operator== | — | — |
| JustDied | method | — | — | — |
| UpdateAI#4 | method | Creature.Main/ForcedDespawn, Creature.MotionMaster/MovePoint, CreatureAI/DoMeleeAttackIfReady, Map.Main/GetCreature, ScriptMgr/DoScriptText, Unit.Main/GetMotionMaster, Unit.Main/GetVictim, Unit.Main/IsAlive, Unit.Main/SelectHostileTarget, WorldObject.Object/AddObjectToRemoveList, WorldObject.Object/GetCreatureListWithEntryInGrid, WorldObject.Object/GetMap | — | — |
| GetAI_npc_Krug_SkullSplit | function | — | — | — |
| GossipHello_npc_Krug_SkullSplit | function | Creature.Main/AI, GossipDef/AddMenuItem#4, GossipDef/SendGossipMenu, Object/GetGUID, ObjectGuid/ObjectGuid#5, Player.Main/GetGossipTextId, Player.Main/GetQuestStatus, Player.Main/PrepareQuestMenu, PlayerMenu/GetGossipMenu, Unit.Main/IsQuestGiver | — | — |
| GossipSelect_npc_Krug_SkullSplit | function | Creature.Main/AI, GossipDef/AddMenuItem#4, GossipDef/CloseGossip, GossipDef/SendGossipMenu, Object/GetGUID, ObjectGuid/ObjectGuid#5, Player.Main/GetGossipTextId, PlayerMenu/GetGossipMenu | — | — |
| npc_MerokAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#5 | method | — | — | — |
| UpdateAI#5 | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, SpellCaster/IsNonMeleeSpellCasted, Unit.Main/FindLowestHpFriendlyUnit, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_npc_Merok | function | — | — | — |
| npc_ShaiAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#6 | method | — | — | — |
| UpdateAI#6 | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, SpellCaster/IsNonMeleeSpellCasted, Unit.Main/FindLowestHpFriendlyUnit, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_npc_Shai | function | — | — | — |
| AddSC_silithus | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
