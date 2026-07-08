# feralas

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# feralas.cpp

## Purpose & Responsibilities

`feralas.cpp` implements scripted behaviors for non-player characters (NPCs) and bosses located in the Feralas zone of the World of Warcraft emulator. It handles three distinct categories of gameplay logic:

1.  **Escort Quests:** Complex follower AI for `npc_shay_leafrunner` and `npc_kindal_moonweaver`, including wandering mechanics, recall spells, event triggers, and quest completion/failure conditions.
2.  **Boss Encounters:** Combat AI for three raid bosses—Mushgog, The Razza, and Skarr the Unbreakable—featuring spell rotations, enrage mechanics, and interactive environmental effects (teleporting players).
3.  **Utility NPCs:** Gossip interactions for `npc_screecher_spirit` and helper logic for captured creatures (`npc_captured_sprite_darter`) involved in the Kindal Moonweaver escort.

The unit relies heavily on the `ScriptedFollowerAI` and `ScriptedAI` base classes from the core engine, extending them with specific timers, state flags, and event handlers. It does not interact directly with any database tables; all data is derived from in-memory objects, quest definitions, and hardcoded coordinates/constants.

## Member-by-Member Behavior

### Screecher Spirit Gossip
*   **`GossipHello_npc_screecher_spirit`**: Handles the gossip menu interaction for the Screecher Spirit. It sends gossip menu ID 2039 to the player, marks the player as having talked to the creature, and sets the creature as `NOT_SELECTABLE` (likely to prevent further clicks or aggro while the menu is open).

### Shay Leafrunner Escort (Quest 2845: Wandering Shay)
This NPC acts as a follower who wanders randomly unless recalled by the player.

*   **`npc_shay_leafrunnerAI` (ctor)**: Initializes the AI, setting wander and despawn timers to 0 and calling `Reset`.
*   **`Reset#6`**: Resets internal state flags `m_bIsRecalled` and `m_bIsComplete` to false.
*   **`JustRespawned#2`**: Sets the creature immune to NPC attacks and delegates to the parent `FollowerAI`.
*   **`MoveInLineOfSight`**: Checks for two specific events:
    1.  **Quest Completion:** If the creature sees `NPC_ROCKBITER` (entry 7765) within 20 yards and isn't already complete, it triggers the quest completion event for the leader, plays dialogue, moves to Rockbiter, and schedules a forced despawn after 30 seconds.
    2.  **Recall:** If the player recalls the follower (indicated by `m_bIsRecalled` being true, though the flag is set in `ResumeFollowing` which is called via spell effect, see below), it resumes following and plays a random "wander done" dialogue. *Note: The logic here checks `m_bIsRecalled` but sets it to false immediately. The flag is actually set in `ResumeFollowing`.*
*   **`JustDied#5`**: Clears timers and delegates to parent `FollowerAI`.
*   **`BeforeStartFollow`**: Called when the quest is accepted. Starts the follow sequence with a distance of 5 yards, sets a 30-second wander timer, and a ~15-minute despawn timer.
*   **`ResumeFollowing`**: Called when the player casts Shay's Bell. Sets `m_bIsRecalled` to true, stops walking (sets run mode), and unpauses the follow state.
*   **`UpdateFollowerAI`**: Main update loop.
    *   Checks the despawn timer; if expired, kills the creature.
    *   If not in combat:
        *   If the wander timer expires, pauses following, plays a wander emote/dialogue, calculates a random point 25-40 yards away, and moves there.
        *   If in combat, performs melee attacks.
*   **`GetAI_npc_shay_leafrunner`**: Factory function returning the AI instance.
*   **`QuestAccept_npc_shay_leafrunner`**: Triggers when the player accepts the quest. Removes immunity, plays start dialogue, and calls `BeforeStartFollow` on the AI.
*   **`EffectDummyCreature_npc_shay_leafrunner`**: Spell trigger for `SPELL_SHAYS_BELL` (11402). If cast by a player on Shay, it calls `ResumeFollowing` on the AI.

### Mushgog Boss
*   **`MushgogAI` (ctor)**: Initializes the AI and calls `Reset`.
*   **`Reset`**: Resets spell timers (Spore Cloud, Roots, Thorn Volley, Invocation) and boolean flags (`m_bEnrage`, `m_bAggro`).
*   **`Aggro`**: If not already flagged as aggroed, finds the closest "Griniblix the Spectator" (entry 14395) within 120 yards and makes it yell a specific text. Sets `m_bAggro` to true.
*   **`JustDied`**: Has a 1-in-6 chance to summon a Black Lotus game object (entry 176589) near the boss corpse. The object is set to not respawn by default and has a very long respawn time.
*   **`UpdateAI`**: Combat loop.
    *   Casts `Spore Cloud` on a random target, `Roots` and `Thorn Volley` on the current victim.
    *   **Enrage:** If health drops below 20%, casts `Enrage` on self.
    *   **Invocation:** Periodically selects a random target. If the target is above Z-coordinate 142.0, it teleports them to the boss's position (with +5 Z offset) using `NearTeleportTo` and sends a spell visual.
    *   Performs melee attacks.

### The Razza Boss
*   **`TheRazzaAI` (ctor)**: Initializes the AI and calls `Reset`.
*   **`Reset#3`**: Resets spell timers (Poison Bolt, Chain Lightning, Invocation) and `m_bAggro`.
*   **`Aggro#3`**: Similar to Mushgog, finds Griniblix (14395) and makes it yell. Sets `m_bAggro`.
*   **`JustDied#2`**: Finds Griniblix (14395) and makes it yell a death-related text.
*   **`UpdateAI#3`**: Combat loop.
    *   Casts `Poison Bolt` and `Chain Lightning` on the victim.
    *   **Invocation:** Same mechanic as Mushgog: teleports random targets above Z=142.0 to the boss.
    *   Performs melee attacks.

### Skarr the Unbreakable Boss
*   **`SkarrTheUnbreakableAI` (ctor)**: Initializes the AI and calls `Reset`.
*   **`Reset#2`**: Resets spell timers (Cleave, Mortal Strike, Knockdown, Invocation) and `m_bAggro`. Timers are randomized on reset.
*   **`Aggro#2`**: Finds Griniblix (14395) and makes it yell. Sets `m_bAggro`.
*   **`UpdateAI#2`**: Combat loop.
    *   Casts `Cleave`, `Mortal Strike`, and `Knockdown` on the victim.
    *   **Invocation:** Same mechanic as Mushgog and The Razza: teleports random targets above Z=142.0 to the boss.
    *   Performs melee attacks.
*   **`GetAI_SkarrTheUnbreakable`**, **`GetAI_TheRazza`**, **`GetAI_Mushgog`**: Factory functions for the respective boss AIs.

### Kindal Moonweaver Escort (Quest 2969: Freedom for All Creatures)
This is a complex escort involving freeing captured sprites.

*   **`npc_kindal_moonweaverAI` (ctor)**: Sets sheath to unarmed and calls `Reset`.
*   **`Reset#5`**: Resets `m_eventStarted` to false if not currently following.
*   **`JustRespawned`**: Sets immunity and delegates to parent.
*   **`JustDied#4`**: Sets respawn time to 10 seconds and delegates to parent.
*   **`OnEscortFailed`**: Plays failure dialogue if the escort fails due to timeout (not death).
*   **`EnterCombat`**: Plays a random aggro dialogue.
*   **`BeginEvent`**: Called when the quest starts. Finds the cage door game object, resets it, and iterates over nearby `NPC_CAPTURED_SPRITE_DARTER` creatures. It resets their AI, assigns them Kindal's GUID and the gate's GUID, and marks the event as started.
*   **`SpriteSaved`**: Called when a sprite successfully escapes. Increments saved counter. If 6 sprites are saved, completes the quest for the leader, plays success dialogue, and ends the event.
*   **`SpriteDied`**: Called when a sprite dies. Increments died counter. If more than 5 die, fails the quest for the leader, plays failure dialogue, and ends the event.
*   **`EndEvent`**: If the follow state is post-event, completes the follow and sets respawn time to 10 seconds.
*   **`GetAI_npc_kindal_moonweaver`**: Factory function.
*   **`QuestAccept_npc_kindal_moonweaver`**: Triggers when the quest is accepted. Sets stance, facing, removes immunity, plays dialogue, starts following, calls `BeginEvent`, pauses following briefly, and schedules a lambda event to resume following after 3 seconds.

### Captured Sprite Darter
These are the creatures freed during the Kindal escort.

*   **`npc_captured_sprite_darterAI` (ctor)**: Sets active object state and calls `Reset`.
*   **`Reset#4`**: Resets run/event flags, GUIDs, and randomizes the run path index and start timer.
*   **`EnterEvadeMode`**: Cleans up combat state. If moving to a point, decrements the move point index. Clears motion master if chasing.
*   **`JustDied#3`**: If dead, notifies Kindal's AI via `SpriteDied` and forces a despawn after 10 seconds.
*   **`UpdateAI#4`**: Main loop.
    *   If not in combat:
        *   Waits for the cage door to become active.
        *   After a random delay, sets faction to neutral/passive and starts running.
        *   Moves through two predefined points based on the randomized path.
        *   Upon reaching the second point, notifies Kindal's AI via `SpriteSaved` and despawns.
    *   If in combat:
        *   If the victim has mana, casts `Mana Burn` periodically.
        *   Performs melee attacks.
*   **`GetAI_npc_captured_sprite_darter`**: Factory function.

### Script Registration
*   **`AddSC_feralas`**: Registers all scripts defined in this file with the script manager. It creates `Script` objects for each NPC/Boss, assigning the appropriate `GetAI`, `GossipHello`, `QuestAcceptNPC`, or `EffectDummyCreature` callbacks.

## Cross-Unit Boundaries

*   **`GossipDef` / `Player.Main` / `WorldObject.Object`**: Used by `GossipHello_npc_screecher_spirit` to send menus and update player/creature states.
*   **`ScriptedFollowerAI`**: Base class for `npc_shay_leafrunnerAI` and `npc_kindal_moonweaverAI`. Provides follow state management, leader tracking, and pause/resume functionality.
*   **`ScriptedAI`**: Base class for boss AIs and `npc_captured_sprite_darterAI`. Provides basic combat loop structure.
*   **`ScriptMgr`**: Used to play dialogue (`DoScriptText`) across multiple members.
*   **`Creature.Main` / `Unit.Main` / `WorldObject.Object`**: Extensive use for movement (`MovePoint`, `NearTeleportTo`), state flags (`SetFlag`, `RemoveFlag`), combat actions (`DoMeleeAttackIfReady`, `SelectHostileTarget`), and object summoning/despawning.
*   **`GridSearchers`**: Used by boss AIs to find `Griniblix the Spectator` and by Kindal's AI to find the cage door and sprites.
*   **`shared_Util`**: `urand` and `frand` are used extensively for randomizing timers, dialogue choices, and movement points.
*   **`GameObject`**: Used by Mushgog to summon loot and by Kindal's AI to check cage door state.
*   **`Map.Main`**: Used by `npc_captured_sprite_darterAI` to retrieve Kindal and the gate object by GUID.

## Data Model

This unit does not access any database tables directly. All quest IDs, creature entries, spell IDs, and coordinates are hardcoded in the source. Quest progress is managed via the `Player` object's quest status methods (`GetQuestStatus`, `GroupEventHappens`, `FailQuest`).

## Notable Implementation Details

1.  **Hardcoded Z-Coordinate Teleportation:** The boss AIs for Mushgog, The Razza, and Skarr the Unbreakable share identical logic for their "Invocation" ability. They check if a target's Z-coordinate is greater than `142.0f`. If so, they teleport the player to the boss's X/Y position but with a fixed Z-offset of `+5.0f`. This suggests a specific arena geometry where players might stand on elevated platforms, and the ability pulls them down to the boss level.
2.  **Shay's Recall Logic:** The `MoveInLineOfSight` function in `npc_shay_leafrunnerAI` checks `m_bIsRecalled`. However, this flag is set to `true` in `ResumeFollowing`, which is called by the spell effect `EffectDummyCreature_npc_shay_leafrunner`. The `MoveInLineOfSight` check seems to handle the case where the player walks back into range after recalling, triggering the "wander done" dialogue and resuming the follow state properly.
3.  **Kindal's Event Timing:** The `QuestAccept_npc_kindal_moonweaver` function uses a lambda event scheduled for 3 seconds later to unpause the follow. This likely allows time for the initial dialogue and animation to play before Kindal starts moving.
4.  **Sprite Path Randomization:** Each captured sprite darter picks a random path index (0-10) on reset. The `asMovementInfo` array maps these indices to start/end points in the `m_fMovePoints` array. This ensures sprites don't all take the same route when freed.
5.  **Boss Aggro Flag:** All three bosses use a `m_bAggro` boolean to ensure the Griniblix yell only happens once per encounter, preventing repeated yells if `Aggro` is called multiple times (e.g., due to threat spikes or re-engages).
6.  **Mushgog Loot Chance:** The Black Lotus drop from Mushgog is purely random (1/6 chance) and does not depend on loot tables or difficulty settings.

## Member Reference

*   **GossipHello_npc_screecher_spirit**: Sends gossip menu 2039, marks player as talked, sets creature not selectable.
*   **npc_shay_leafrunnerAI (ctor)**: Initializes timers and calls Reset.
*   **Reset#6**: Resets recall and completion flags.
*   **JustRespawned#2**: Sets NPC immunity, calls parent.
*   **MoveInLineOfSight**: Checks for Rockbiter (quest complete) or player recall (resume follow).
*   **JustDied#5**: Clears timers, calls parent.
*   **BeforeStartFollow**: Starts follow, sets wander/despawn timers.
*   **ResumeFollowing**: Sets recall flag, unpauses follow, stops walking.
*   **UpdateFollowerAI**: Manages despawn timer, wander logic (pause, move random point), and melee combat.
*   **GetAI_npc_shay_leafrunner**: Factory for Shay's AI.
*   **QuestAccept_npc_shay_leafrunner**: Removes immunity, plays dialogue, starts follow.
*   **EffectDummyCreature_npc_shay_leafrunner**: Triggers `ResumeFollowing` on Shay's Bell cast.
*   **MushgogAI (ctor)**: Initializes AI, calls Reset.
*   **Reset**: Resets spell timers and flags.
*   **Aggro**: Makes Griniblix yell, sets aggro flag.
*   **JustDied**: 1/6 chance to summon Black Lotus.
*   **UpdateAI**: Rotates spells, enrages at 20% HP, teleports high-Z targets.
*   **TheRazzaAI (ctor)**: Initializes AI, calls Reset.
*   **Reset#3**: Resets spell timers and aggro flag.
*   **Aggro#3**: Makes Griniblix yell, sets aggro flag.
*   **JustDied#2**: Makes Griniblix yell death text.
*   **UpdateAI#3**: Rotates spells, teleports high-Z targets.
*   **SkarrTheUnbreakableAI (ctor)**: Initializes AI, calls Reset.
*   **Reset#2**: Resets randomized spell timers and aggro flag.
*   **Aggro#2**: Makes Griniblix yell, sets aggro flag.
*   **UpdateAI#2**: Rotates spells, teleports high-Z targets.
*   **GetAI_SkarrTheUnbreakable**: Factory for Skarr's AI.
*   **GetAI_TheRazza**: Factory for The Razza's AI.
*   **GetAI_Mushgog**: Factory for Mushgog's AI.
*   **npc_kindal_moonweaverAI (ctor)**: Sets sheath, calls Reset.
*   **Reset#5**: Resets event start flag if not following.
*   **JustRespawned**: Sets NPC immunity, calls parent.
*   **JustDied#4**: Sets 10s respawn, calls parent.
*   **OnEscortFailed**: Plays timeout failure dialogue.
*   **EnterCombat**: Plays random aggro dialogue.
*   **npc_captured_sprite_darterAI (ctor)**: Sets active state, calls Reset.
*   **Reset#4**: Resets flags, randomizes path/timers.
*   **EnterEvadeMode**: Cleans combat state, adjusts move point index.
*   **JustDied#3**: Notifies Kindal, despawns.
*   **UpdateAI#4**: Waits for gate, runs path, notifies Kindal on save, casts Mana Burn in combat.
*   **BeginEvent**: Finds gate/sprites, initializes sprite AI with GUIDs.
*   **SpriteSaved**: Increments saved count, completes quest if 6 saved.
*   **SpriteDied**: Increments died count, fails quest if >5 died.
*   **EndEvent**: Completes follow, sets respawn time.
*   **GetAI_npc_captured_sprite_darter**: Factory for Sprite Darter's AI.
*   **GetAI_npc_kindal_moonweaver**: Factory for Kindal's AI.
*   **QuestAccept_npc_kindal_moonweaver**: Prepares Kindal, starts follow, begins event, schedules resume.
*   **AddSC_feralas**: Registers all scripts in this file.

---

<!-- machine-true, projected from graph.json -->

## Map — feralas

*Source:* feralas.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GossipHello_npc_screecher_spirit | function | GossipDef/SendGossipMenu, Object/GetEntry, Object/GetGUID, ObjectGuid/ObjectGuid#5, Player.Main/TalkedToCreature, WorldObject.Object/SetFlag | — | — |
| npc_shay_leafrunnerAI | ctor | ScriptedFollowerAI/FollowerAI | — | — |
| Reset#6 | method | — | — | — |
| JustRespawned#2 | method | ScriptedFollowerAI/JustRespawned, WorldObject.Object/SetFlag | — | — |
| MoveInLineOfSight | method | Creature.Main/ForcedDespawn, Creature.MotionMaster/MovePoint, Object/GetEntry, Object/GetTypeId, Player.Main/GroupEventHappens, ScriptedFollowerAI/GetLeaderForFollower, ScriptedFollowerAI/HasFollowState, ScriptedFollowerAI/MoveInLineOfSight, ScriptedFollowerAI/SetFollowComplete, ScriptMgr/DoScriptText, shared_Util/urand, Unit.Main/GetMotionMaster, WorldObject.Object/GetContactPoint, WorldObject.Object/IsWithinDistInMap | — | — |
| JustDied#5 | method | ScriptedFollowerAI/JustDied | — | — |
| BeforeStartFollow | method | ScriptedFollowerAI/StartFollow | — | — |
| ResumeFollowing | method | ScriptedFollowerAI/SetFollowPaused, Unit.Main/SetWalk | — | — |
| UpdateFollowerAI | method | Creature.Main/DisappearAndDie, Creature.MotionMaster/MovePoint, CreatureAI/DoMeleeAttackIfReady, ScriptedFollowerAI/SetFollowPaused, ScriptMgr/DoScriptText, shared_Util/frand, shared_Util/urand, Unit.Main/GetMotionMaster, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget, Unit.Main/SetWalk, WorldObject.Object/GetNearPoint | — | — |
| GetAI_npc_shay_leafrunner | function | — | — | — |
| QuestAccept_npc_shay_leafrunner | function | Creature.Main/AI, QuestDef/GetQuestId, ScriptMgr/DoScriptText, WorldObject.Object/RemoveFlag | — | — |
| EffectDummyCreature_npc_shay_leafrunner | function | Creature.Main/AI, Object/GetTypeId | — | — |
| MushgogAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset | method | — | — | — |
| Aggro | method | GridSearchers/GetClosestCreatureWithEntry, Unit.Main/IsAlive, WorldObject.Object/MonsterYell#2 | — | — |
| JustDied | method | GameObject/SetRespawnTime, GameObject/SetSpawnedByDefault, shared_Util/urand, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/SummonGameObject | — | — |
| UpdateAI | method | Creature.Main/SelectAttackingTarget, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, shared_Util/urand, SpellCaster/CastSpell#2, Unit.Main/GetHealthPercent, Unit.Main/GetVictim, Unit.Main/NearTeleportTo, Unit.Main/SelectHostileTarget, Unit.Main/SendSpellGo, WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ | — | — |
| TheRazzaAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#3 | method | — | — | — |
| Aggro#3 | method | GridSearchers/GetClosestCreatureWithEntry, Unit.Main/IsAlive, WorldObject.Object/MonsterYell#2 | — | — |
| JustDied#2 | method | GridSearchers/GetClosestCreatureWithEntry, Unit.Main/IsAlive, WorldObject.Object/MonsterYell#2 | — | — |
| UpdateAI#3 | method | Creature.Main/SelectAttackingTarget, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, shared_Util/urand, Unit.Main/GetVictim, Unit.Main/NearTeleportTo, Unit.Main/SelectHostileTarget, Unit.Main/SendSpellGo, WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ | — | — |
| SkarrTheUnbreakableAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#2 | method | shared_Util/urand | — | — |
| Aggro#2 | method | GridSearchers/GetClosestCreatureWithEntry, Unit.Main/IsAlive, WorldObject.Object/MonsterYell#2 | — | — |
| UpdateAI#2 | method | Creature.Main/SelectAttackingTarget, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, shared_Util/urand, Unit.Main/GetVictim, Unit.Main/NearTeleportTo, Unit.Main/SelectHostileTarget, Unit.Main/SendSpellGo, WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ | — | — |
| GetAI_SkarrTheUnbreakable | function | — | — | — |
| GetAI_TheRazza | function | — | — | — |
| GetAI_Mushgog | function | — | — | — |
| npc_kindal_moonweaverAI | ctor | ScriptedFollowerAI/FollowerAI, Unit.Main/SetSheath | — | — |
| Reset#5 | method | ScriptedFollowerAI/HasFollowState | — | — |
| JustRespawned | method | ScriptedFollowerAI/JustRespawned, WorldObject.Object/SetFlag | — | — |
| JustDied#4 | method | Creature.Main/SetRespawnTime, ScriptedFollowerAI/JustDied | — | — |
| OnEscortFailed | method | ScriptMgr/DoScriptText | — | — |
| EnterCombat | method | ScriptMgr/DoScriptText, shared_Util/urand | — | — |
| npc_captured_sprite_darterAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/SetActiveObjectState | — | — |
| Reset#4 | method | shared_Util/urand | — | — |
| EnterEvadeMode | method | Creature.MotionMaster/GetCurrentMovementGeneratorType, MotionMaster/Clear, Unit.Main/CombatStop, Unit.Main/DeleteThreatList, Unit.Main/GetMotionMaster, Unit.Main/RemoveAllAuras | — | — |
| JustDied#3 | method | Creature.Main/AI, Creature.Main/ForcedDespawn, Map.Main/GetCreature, ObjectGuid/ObjectGuid#5, Unit.Main/IsDead, WorldObject.Object/GetMap | — | — |
| UpdateAI#4 | method | Creature.Main/AI, Creature.Main/ForcedDespawn, Creature.Main/SetFactionTemporary, Creature.MotionMaster/GetCurrentMovementGeneratorType, Creature.MotionMaster/MovePoint, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, GameObject/GetGoState, Map.Main/GetCreature, Map.Main/GetGameObject, MotionMaster/Clear, ObjectGuid/ObjectGuid#5, shared_Util/urand, Unit.Main/GetMotionMaster, Unit.Main/GetPowerType, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget, WorldObject.Object/GetMap | — | — |
| BeginEvent | method | Creature.Main/AI, GameObject/SetGoState, GridSearchers/GetClosestGameObjectWithEntry, GridSearchers/GetCreatureListWithEntryInGrid#2, Object/GetObjectGuid | — | — |
| SpriteSaved | method | Player.Main/GetQuestStatus, Player.Main/GroupEventHappens, ScriptedFollowerAI/GetLeaderForFollower, ScriptedFollowerAI/SetFollowComplete, ScriptMgr/DoScriptText | — | — |
| SpriteDied | method | Player.Main/FailQuest, Player.Main/GetQuestStatus, ScriptedFollowerAI/GetLeaderForFollower, ScriptedFollowerAI/SetFollowComplete, ScriptMgr/DoScriptText | — | — |
| EndEvent | method | Creature.Main/SetRespawnTime, ScriptedFollowerAI/HasFollowState, ScriptedFollowerAI/SetFollowComplete | — | — |
| GetAI_npc_captured_sprite_darter | function | — | — | — |
| GetAI_npc_kindal_moonweaver | function | — | — | — |
| QuestAccept_npc_kindal_moonweaver | function | Creature.Main/AI, QuestDef/GetQuestId, ScriptedFollowerAI/SetFollowPaused, ScriptedFollowerAI/StartFollow, ScriptMgr/DoScriptText, Unit.Main/IsAlive, Unit.Main/SetFacingToObject, Unit.Main/SetStandState, WorldObject.Object/RemoveFlag | — | — |
| AddSC_feralas | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
