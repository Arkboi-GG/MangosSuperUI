# dustwallow_marsh

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# dustwallow_marsh

**Purpose & Responsibilities**
This translation unit implements scripted behaviors for non-player characters (NPCs), game objects (GOs), and area triggers specific to the Dustwallow Marsh zone in the World of Warcraft emulation. It handles complex quest events, including the "Missing Diplomat" chain involving Archmage Tervosh, escort quests for Stinky Ignatz, combat AI for bosses like Lady Jaina Proudmoore and Emberstrife, and utility gossip interactions for NPCs like Cassa Crimsonwing. The unit relies heavily on the `ScriptedAI` and `npc_escortAI` base classes to manage state machines, timers, and event-driven logic.

**Member-by-Member Behavior**

### Archmage Tervosh & The Missing Diplomat Event
This subsystem manages a timed, multi-phase event triggered by players entering a specific area trigger. It involves summoning Archmage Tervosh, playing visual effects, coordinating nearby guards to salute, and eventually despawning the NPC.

*   **npc_archmage_tervoshAI**: The AI structure for Archmage Tervosh. It inherits from `ScriptedAI`.
    *   **ctor**: Initializes the AI and calls `Reset`.
    *   **resetDespawnDelay**: Resets the despawn timer to 60 seconds (`TERVOSH_SPAWN_DURATION`). This is called externally by the area trigger if a new player arrives while the event is ongoing.
    *   **getCurrentPhase**: Returns the current phase of the event state machine. Used by the area trigger to determine how to handle overlapping player arrivals.
    *   **Reset**: Initializes internal timers and sets the phase to `MDQP_ARRIVE`. Sets the initial despawn delay to 60 seconds.
    *   **UpdateAI#3**: The core state machine loop.
        *   `MDQP_PREPARE_TO_ARRIVE`: Waits 1 second, then makes the creature visible. Transitions to `MDQP_ARRIVE`.
        *   `MDQP_ARRIVE`: Waits 1 second, casts `SPELL_TELEPORT_VISUAL1`, then transitions to `MDQP_GUARDS_SALUTE`.
        *   `MDQP_GUARDS_SALUTE`: Finds all `NPC_SENTRY_POINT_GUARD` creatures within 10 yards. For each guard that is alive and not in combat, it stops movement, faces Tervosh, and performs a salute emote. Transitions to `MDQP_WAITING`.
        *   `MDQP_WAITING`: Waits for the `m_despawnDelayTimer` (default 60s) to expire. If it expires, casts `SPELL_TELEPORT_VISUAL2` and transitions to `MDQP_TELEPORT_BACK`.
        *   `MDQP_TELEPORT_BACK`: Waits 2 seconds, then calls `UnSummon` on the temporary summon to despawn the NPC.
*   **QuestRewarded_npc_archmage_tervosh**: Called when a player turns in quest 1265 ("Missing Diplomat Part 14"). It makes Tervosh speak a specific line (`TERVOSH_SAY_ON_QUEST_MD_PT14`) and casts `SPELL_PROUDMOORES_DEFENSE` on the player. Note: The comment indicates a known retail-like bug where only the last player in a group might receive the buff if multiple complete it simultaneously, but the code attempts to mitigate this by casting instantly.
*   **GetAI_npc_archmage_tervosh**: Factory function to create the `npc_archmage_tervoshAI` instance.
*   **AreaTrigger_at_sentry_point**: Triggered when a player enters the sentry point area.
    *   Validates the player is alive, not a GM, and has the quest 1265 incomplete.
    *   Marks the quest as complete for the player and sends the completion event.
    *   Checks if Tervosh is already summoned within 15 yards.
    *   If not summoned, it summons Tervosh at `tervoshSpawnPoint` as a manual-despawn temporary summon. It sets flags to make him immune to NPC attacks and not attackable. It starts the event via the AI.
    *   If Tervosh is already present:
        *   If in `MDQP_WAITING`, it resets the despawn delay to allow more time for other players.
        *   If in `MDQP_TELEPORT_BACK` (rare race condition), it summons a *new* invisible Tervosh, sets his phase to `MDQP_PREPARE_TO_ARRIVE`, and starts the event again.

### Lady Jaina Proudmoore
Handles combat AI and gossip interactions for Lady Jaina.

*   **npc_lady_jaina_proudmooreAI**: Inherits from `ScriptedAI`.
    *   **ctor**: Initializes and calls `Reset`.
    *   **Reset#3**: Sets spell timer to 3s and special timer to 15s.
    *   **EnterCombat**: Plays sound ID 5882.
    *   **UpdateAI#5**: Combat logic.
        *   Checks for hostile targets.
        *   **Special Timer**: Every 10-30 seconds (random), it attempts to summon a Water Elemental (`SPELL_JAINA_WATER_ELEMENTAL`) if none exist. Otherwise, it casts `SPELL_JAINA_TELEPORT` on her victim. If she teleports far from her origin (-4018.1, -4525.24), she removes the old victim from her threat list to prevent chasing.
        *   **Spell Timer**: Every 1-10 seconds (random), it randomly casts Fireball, Fireblast, or Blizzard (on a random target within 25 yards).
        *   Performs melee attacks if ready.
*   **GetAI_npc_lady_jaina_proudmoore**: Factory function.
*   **GossipHello_npc_lady_jaina_proudmoore**: Handles gossip menu display.
    *   Prepares quest menu if applicable.
    *   Adds a gossip item for "Jaina's Autograph" if the player has quest 558 incomplete.
    *   Displays different gossip texts based on the progress of the "Missing Diplomat" quest chain (quests 1267 and 1324).
*   **GossipSelect_npc_lady_jaina_proudmoore**: Handles gossip selection.
    *   If the autograph action is selected, it displays a response text and casts `SPELL_JAINAS_AUTOGRAPH` on the player.

### Cassa Crimsonwing
Simple gossip interaction for a gryphon master.

*   **GossipHello_npc_cassa_crimsonwing**: Adds a gossip item to ride to Survey Alcaz Island if the player has quest 11142 incomplete. Displays standard gossip text.
*   **GossipSelect_npc_cassa_crimsonwing**: If the ride action is selected, closes gossip and casts `SPELL_ALCAZ_SURVEY` (teleport spell) on the player.

### Stinky Ignatz Escort
Handles the escort quest "Stinky's Escape".

*   **npc_stinky_ignatzAI**: Inherits from `npc_escortAI`.
    *   **ctor**: Initializes waypoint counter and timer, calls `Reset`.
    *   **Reset#4**: Empty override.
    *   **JustRespawned**: Resets waypoint and timer, calls parent `JustRespawned`.
    *   **WaypointReached**: Triggers dialogue and events at specific waypoints.
        *   WP 0: Starts escort dialogue.
        *   WP 4, 8: Dialogue.
        *   WP 16: Respawns a nearby Bogbean Plant GO if not spawned. Sets a timer for subsequent dialogue.
        *   WP 18: Makes Ignatz kneel. Sets a timer.
        *   WP 24: Ends escort, gives credit to the player for either Alliance or Horde version of the quest.
    *   **Aggro**: Randomly plays aggro dialogue depending on the current waypoint progress.
    *   **UpdateAI#6**: Manages timers for dialogue at waypoints 16 and 18.
        *   At WP 16, after a delay, plays dialogue.
        *   At WP 18, after a delay, despawns the Bogbean Plant GO and makes Ignatz stand.
        *   Calls parent `UpdateAI`.
*   **QuestAccept_npc_stinky_ignatz**: Triggered when the player accepts the escort quest. Sets Ignatz's faction to friendly (113), makes him stand, and starts the escort AI.
*   **GetAI_npc_stinky_ignatz**: Factory function.

### Emberstrife Boss
Handles combat AI for the boss Emberstrife.

*   **npc_emberstrifeAI**: Inherits from `ScriptedAI`.
    *   **ctor**: Initializes and calls `Reset`.
    *   **Reset#2**: Initializes timers for Cleave, Frenzy, and Flame Breath. Sets `m_bWeakened` to false.
    *   **UpdateAI#4**: Combat logic.
        *   Checks health percentage. If below 11% and not already weakened, sets `m_bWeakened` to true and plays a weakened emote.
        *   **Cleave**: Casts every 6-8 seconds.
        *   **Flame Breath**: Casts every 8-12 seconds.
        *   **Frenzy**: If health is below 60%, casts Frenzy every ~2 minutes and plays a frenzy emote.
        *   Performs melee attacks.
*   **GetAI_npc_emberstrife**: Factory function.

### Emberstrife Seals (Game Objects)
Handles the despawning of the seals dropped by Emberstrife.

*   **go_unforged_sealAI**: Inherits from `GameObjectAI`.
    *   **ctor**: Sets despawn timer to 3 minutes.
    *   **OnUse**: Immediately deletes the object if used.
    *   **UpdateAI#2**: Deletes the object if the despawn timer expires.
*   **GetAI_go_unforged_seal**: Factory function.
*   **go_forged_sealAI**: Inherits from `GameObjectAI`.
    *   **ctor**: Sets despawn timer to 3 minutes.
    *   **UpdateAI**: Deletes the object if the despawn timer expires. (Note: No `OnUse` override, so it only despawns on timer).
*   **GetAI_go_forged_seal**: Factory function.

### Script Registration
*   **AddSC_dustwallow_marsh**: Registers all scripts defined in this unit with the `ScriptMgr`. It creates `Script` objects for each NPC, GO, and area trigger, linking them to their respective AI factories and event handlers.

**Cross-Unit Boundaries**

*   **ScriptedAI / npc_escortAI**: All AI structures inherit from these base classes, utilizing their timer management, target selection, and spell casting utilities.
*   **GridSearchers**: Used by `npc_archmage_tervoshAI::UpdateAI#3` to find nearby guards and by `AreaTrigger_at_sentry_point` to check for existing Tervosh summons.
*   **SpellCaster**: Used extensively by all AI structures to cast spells.
*   **TemporarySummon**: Used by `npc_archmage_tervoshAI::UpdateAI#3` to despawn Tervosh.
*   **Unit.Main**: Used for emotes, visibility, combat status, facing, movement, and threat management.
*   **Creature.Main / Player.Main**: Used for AI access, quest status checks, quest completion, and gossip interactions.
*   **ScriptMgr**: Used to register scripts and play dialogue (`DoScriptText`).
*   **GossipDef / PlayerMenu**: Used for constructing and sending gossip menus.
*   **GameObject**: Used for finding, respawning, and despawning game objects during the Stinky Ignatz escort.

**Data Model**
This unit does not directly interact with any database tables. All data (quest IDs, NPC entries, spell IDs, coordinates, dialogue IDs) is hardcoded in enums and constants.

**Notable Implementation Details**

*   **Tervosh Race Condition Handling**: The `AreaTrigger_at_sentry_point` function explicitly handles a rare race condition where a player arrives while Tervosh is despawning (`MDQP_TELEPORT_BACK`). It summons a new, invisible Tervosh and restarts the event sequence to ensure the player can still complete the quest step.
*   **Jaina Teleport Threat Management**: In `npc_lady_jaina_proudmooreAI::UpdateAI#5`, when Jaina teleports, she removes her current victim from her threat list if she moves far from her origin. This prevents her from pathfinding back to the original target, which could cause erratic behavior.
*   **Stinky Ignatz Dialogue Timers**: The `npc_stinky_ignatzAI::UpdateAI#6` uses a single `timer` variable to manage delays for dialogue at waypoints 16 and 18. The logic checks `currWaypoint` to determine which dialogue to play and resets the timer appropriately.
*   **Emberstrife Weakened State**: The `npc_emberstrifeAI` tracks a `m_bWeakened` boolean to ensure the weakened emote is only played once when health drops below 11%.
*   **Seal Despawn Logic**: Both seal AI structures delete themselves after 3 minutes. The unforged seal also deletes immediately upon use, while the forged seal only despawns on timer.

## Member Reference

**npc_archmage_tervoshAI** (ctor): Initializes the AI for Archmage Tervosh, inheriting from `ScriptedAI` and calling `Reset`.
**resetDespawnDelay**: Resets the despawn timer to 60 seconds, used by the area trigger to extend the event duration.
**getCurrentPhase**: Returns the current phase of the Tervosh event state machine.
**Reset**: Initializes timers and sets the event phase to `MDQP_ARRIVE`.
**UpdateAI#3**: Executes the Tervosh event state machine, handling visibility, visual effects, guard salutes, and despawning.
**QuestRewarded_npc_archmage_tervosh**: Handles quest turn-in for 1265, playing dialogue and casting a buff on the player.
**GetAI_npc_archmage_tervosh**: Factory function to create `npc_archmage_tervoshAI`.
**AreaTrigger_at_sentry_point**: Triggers the Tervosh event when a player enters the area, summoning the NPC and managing overlapping player arrivals.
**npc_lady_jaina_proudmooreAI** (ctor): Initializes the AI for Lady Jaina, inheriting from `ScriptedAI` and calling `Reset`.
**Reset#3**: Initializes spell and special timers for Jaina's combat AI.
**EnterCombat**: Plays a sound effect when Jaina enters combat.
**UpdateAI#5**: Manages Jaina's combat abilities, including summoning elementals, teleporting, and casting spells.
**GetAI_npc_lady_jaina_proudmoore**: Factory function to create `npc_lady_jaina_proudmooreAI`.
**GossipHello_npc_lady_jaina_proudmoore**: Displays gossip menu items for Jaina, including quest options and autograph request.
**GossipSelect_npc_lady_jaina_proudmoore**: Handles the autograph gossip selection, casting the autograph spell.
**GossipHello_npc_cassa_crimsonwing**: Displays gossip menu for Cassa, offering a ride to Survey Alcaz Island.
**GossipSelect_npc_cassa_crimsonwing**: Handles the ride gossip selection, casting the teleport spell.
**npc_stinky_ignatzAI** (ctor): Initializes the escort AI for Stinky Ignatz, inheriting from `npc_escortAI`.
**Reset#4**: Empty override for the escort AI reset.
**JustRespawned**: Resets escort state and calls parent `JustRespawned`.
**WaypointReached**: Triggers dialogue and events at specific waypoints during the escort.
**Aggro**: Plays aggro dialogue based on escort progress.
**UpdateAI#6**: Manages timers for dialogue and object interactions during the escort.
**QuestAccept_npc_stinky_ignatz**: Starts the escort quest when accepted, setting faction and AI state.
**GetAI_npc_stinky_ignatz**: Factory function to create `npc_stinky_ignatzAI`.
**npc_emberstrifeAI** (ctor): Initializes the AI for Emberstrife, inheriting from `ScriptedAI` and calling `Reset`.
**Reset#2**: Initializes combat timers and weakened state for Emberstrife.
**UpdateAI#4**: Manages Emberstrife's combat abilities, including Cleave, Flame Breath, Frenzy, and weakened state.
**GetAI_npc_emberstrife**: Factory function to create `npc_emberstrifeAI`.
**go_unforged_sealAI** (ctor): Initializes the AI for the unforged seal, setting a 3-minute despawn timer.
**OnUse**: Deletes the unforged seal immediately upon use.
**UpdateAI#2**: Deletes the unforged seal if the despawn timer expires.
**GetAI_go_unforged_seal**: Factory function to create `go_unforged_sealAI`.
**go_forged_sealAI** (ctor): Initializes the AI for the forged seal, setting a 3-minute despawn timer.
**UpdateAI**: Deletes the forged seal if the despawn timer expires.
**GetAI_go_forged_seal**: Factory function to create `go_forged_sealAI`.
**AddSC_dustwallow_marsh**: Registers all scripts in this unit with the `ScriptMgr`.

---

<!-- machine-true, projected from graph.json -->

## Map — dustwallow_marsh

*Source:* dustwallow_marsh.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| npc_archmage_tervoshAI | ctor | ScriptedAI/ScriptedAI | — | — |
| resetDespawnDelay | method | — | — | — |
| getCurrentPhase | method | — | — | — |
| Reset | method | — | — | — |
| UpdateAI#3 | method | BasicAI/UpdateAI, GridSearchers/GetCreatureListWithEntryInGrid#2, SpellCaster/CastSpell#2, TemporarySummon/UnSummon, Unit.Main/HandleEmote, Unit.Main/IsAlive, Unit.Main/IsInCombat, Unit.Main/SetFacingToObject, Unit.Main/SetVisibility, Unit.Main/StopMoving | — | — |
| QuestRewarded_npc_archmage_tervosh | function | Creature.Main/AI, QuestDef/GetQuestId, ScriptMgr/DoScriptText, SpellCaster/CastSpell#2 | — | — |
| GetAI_npc_archmage_tervosh | function | — | — | — |
| AreaTrigger_at_sentry_point | function | Creature.Main/AI, GridSearchers/GetClosestCreatureWithEntry, Player.Main/CompleteQuest, Player.Main/GetQuestStatus, Player.Main/IsGameMaster, Player.Main/SendQuestCompleteEvent, Unit.Main/IsAlive, Unit.Main/SetVisibility, WorldObject.Object/SetFlag, WorldObject.Object/SummonCreature#2 | — | — |
| npc_lady_jaina_proudmooreAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#3 | method | — | — | — |
| EnterCombat | method | WorldObject.Object/PlayDistanceSound | — | — |
| UpdateAI#5 | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, shared_Util/urand, SpellCaster/IsNonMeleeSpellCasted, ThreatManager/modifyThreatPercent#2, Unit.Main/GetGuardianCountWithEntry, Unit.Main/GetThreatManager, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget, Unit.Main/SelectRandomUnfriendlyTarget, Unit.Main/_removeAttacker, WorldObject.Object/GetDistance2d#4 | — | — |
| GetAI_npc_lady_jaina_proudmoore | function | — | — | — |
| GossipHello_npc_lady_jaina_proudmoore | function | GossipDef/AddMenuItem#4, GossipDef/SendGossipMenu, Object/GetGUID, ObjectGuid/ObjectGuid#5, Player.Main/GetQuestStatus, Player.Main/PrepareQuestMenu, Player.Main/SendPreparedQuest, PlayerMenu/GetGossipMenu, Unit.Main/IsQuestGiver | — | — |
| GossipSelect_npc_lady_jaina_proudmoore | function | GossipDef/SendGossipMenu, Object/GetGUID, ObjectGuid/ObjectGuid#5, SpellCaster/CastSpell#2 | — | — |
| GossipHello_npc_cassa_crimsonwing | function | GossipDef/AddMenuItem#4, GossipDef/SendGossipMenu, Object/GetGUID, ObjectGuid/ObjectGuid#5, Player.Main/GetGossipTextId, Player.Main/GetQuestStatus, PlayerMenu/GetGossipMenu | — | — |
| GossipSelect_npc_cassa_crimsonwing | function | GossipDef/CloseGossip, SpellCaster/CastSpell#2 | — | — |
| npc_stinky_ignatzAI | ctor | ScriptedEscortAI/npc_escortAI | — | — |
| Reset#4 | method | — | — | — |
| JustRespawned | method | ScriptedEscortAI/JustRespawned | — | — |
| WaypointReached | method | GameObject/isSpawned, GameObject/Respawn, Player.Main/GetQuestStatus, Player.Main/GroupEventHappens, ScriptedEscortAI/GetPlayerForEscort, ScriptMgr/DoScriptText, Unit.Main/SetStandState, WorldObject.Object/FindNearestGameObject | — | — |
| Aggro | method | ScriptMgr/DoScriptText, shared_Util/urand | — | — |
| UpdateAI#6 | method | GameObject/Despawn, ScriptedEscortAI/GetPlayerForEscort, ScriptedEscortAI/UpdateAI, ScriptMgr/DoScriptText, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget, Unit.Main/SetStandState, WorldObject.Object/FindNearestGameObject | — | — |
| QuestAccept_npc_stinky_ignatz | function | Creature.Main/AI, Object/GetGUID, QuestDef/GetQuestId, ScriptedEscortAI/Start, Unit.Main/SetFactionTemplateId, Unit.Main/SetStandState | — | — |
| GetAI_npc_stinky_ignatz | function | — | — | — |
| npc_emberstrifeAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#2 | method | shared_Util/urand | — | — |
| UpdateAI#4 | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, ScriptMgr/DoScriptText, shared_Util/urand, Unit.Main/GetHealthPercent, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_npc_emberstrife | function | — | — | — |
| go_unforged_sealAI | ctor | GameObjectAI/GameObjectAI | — | — |
| OnUse | method | GameObject/Delete | — | — |
| UpdateAI#2 | method | GameObject/Delete | — | — |
| GetAI_go_unforged_seal | function | — | — | — |
| go_forged_sealAI | ctor | GameObjectAI/GameObjectAI | — | — |
| UpdateAI | method | GameObject/Delete | — | — |
| GetAI_go_forged_seal | function | — | — | — |
| AddSC_dustwallow_marsh | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
