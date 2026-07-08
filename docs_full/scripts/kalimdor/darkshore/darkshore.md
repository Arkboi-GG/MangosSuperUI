# darkshore

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# darkshore.cpp

## Purpose & Responsibilities

`darkshore.cpp` implements the scripted AI behaviors, quest hooks, and event handlers for numerous non-player characters (NPCs) and game objects located in the Darkshore zone. The unit supports several distinct questlines and mechanics:

1.  **The Sleeper Awakened (Quest 5321):** Involves `npc_kerlonian`, a follower who periodically falls asleep and must be awakened by a specific spell (`SPELL_AWAKEN`) during the escort.
2.  **Absent-Minded Professor Part 2 (Quest 731):** An escort quest involving `npc_prospector_remtravel`, who summons enemies at specific waypoints and provides dialogue cues.
3.  **Gyromast Rev (Quest 2078):** Involves `npc_threshwackonator`, a gossip-initiated follower that turns hostile upon reaching a target NPC.
4.  **Therylune's Escape (Quest 945):** A simple escort quest involving `npc_therylune`.
5.  **Escape Through Force/Stealth (Quests 994/995):** Complex escort quests involving `npc_volcor`, featuring branching dialogue, stealth mechanics, enemy summons, and final boss interactions.
6.  **Trapped Bear (Quest 985 related):** Involves `npc_rabid_thistle_bear`, which is transformed into a follower via a dummy spell effect.
7.  **Wanted: Murkdeep (Quest 4740):** An area-triggered event that summons `npc_murkdeep` and his minions for a combat encounter.
8.  **Miscellaneous Quest Hooks:** Includes `QuestComplete_npc_terenthis` for quest completion logic and `QuestAcceptGO_beached_sea` for granting items from a game object.

The unit relies heavily on base classes `FollowerAI` and `npc_escortAI` (from `ScriptedFollowerAI` and `ScriptedEscortAI` respectively) for movement and pathing, while implementing custom logic for dialogue, spawning, state management, and quest progression.

## Member-by-Member Behavior

### npc_kerlonian (The Sleeper)

*   **npc_kerlonianAI**: Inherits from `FollowerAI`. Manages the state of Kerlonian, including sleeping timers and awakening conditions.
*   **Reset**: Initializes the sleep timer (`m_uiFallAsleepTimer`) to a random value between 10–45 seconds and enables line-of-sight events.
*   **JustRespawned**: Sets the creature immune to NPCs upon respawn and calls the parent `JustRespawned`.
*   **MoveInLineOfSight**: Checks if the creature sees `NPC_LILADRIS` (entry 11219). If within range and the player has incomplete quest 5321, it triggers the quest completion event, plays a dialogue, and marks the follow as complete.
*   **SpellHit**: If the creature is following and hit by `SPELL_AWAKEN` (17536), it calls `ClearSleeping`.
*   **SetSleeping**: Pauses following, plays random sleep emotes/dialogues, sets stand state to sleep, changes faction to neutral/passive (35), and casts a visual sleep spell.
*   **ClearSleeping**: Removes sleep aura, stands up, restores faction, re-enables LOS events, plays awaken emote, and resumes following.
*   **UpdateFollowerAI**: If not in combat, checks the sleep timer. If expired and not paused, calls `SetSleeping`. If in combat, performs melee attacks.
*   **GetAI_npc_kerlonian**: Factory function returning a new `npc_kerlonianAI`.
*   **QuestAccept_npc_kerlonian**: On accepting quest 5321, it makes Kerlonian stand, removes immunity, plays start dialogue, and starts following the player.

### npc_prospector_remtravel (Absent-Minded Professor)

*   **npc_prospector_remtravelAI**: Inherits from `npc_escortAI`. Handles waypoint-specific events for the escort.
*   **JustRespawned#2**: Sets NPC immunity and calls parent `JustRespawned`.
*   **WaypointReached**: Executes specific actions at waypoints:
    *   WP 5, 9, 13, 18, 19, 30, 31, 36, 37, 47, 48: Plays dialogues/emotes.
    *   WP 10, 20, 37: Summons Gravel Scouts/Bones/Geologists at fixed coordinates.
    *   WP 48: Triggers quest completion event for the player.
*   **Reset#3**: Empty override.
*   **Aggro**: Randomly plays an aggro dialogue.
*   **JustSummoned#2**: Makes summoned creatures attack the escorting player.
*   **GetAI_npc_prospector_remtravel**: Factory function.
*   **QuestAccept_npc_prospector_remtravel**: On accepting quest 731, sets faction, plays start dialogue, removes immunity, and starts the escort.

### npc_threshwackonator (Gyromast Rev)

*   **npc_threshwackonatorAI**: Inherits from `FollowerAI`.
*   **Reset#6**: Enables LOS events.
*   **MoveInLineOfSight#2**: If it sees `NPC_GELKAK` (entry 6667) and is within 10 yards, it plays a dialogue and calls `DoAtEnd`.
*   **DoAtEnd**: Sets faction to hostile, attacks the leader (player), and completes the follow.
*   **GetAI_npc_threshwackonator**: Factory function.
*   **GossipHello_npc_threshwackonator**: Adds a gossip menu item "[PH] Insert key" if the player has incomplete quest 2078.
*   **GossipSelect_npc_threshwackonator**: On selecting the gossip item, closes menu, plays start emote, and starts following the player.

### npc_therylune (Escape)

*   **npc_theryluneAI**: Inherits from `npc_escortAI`.
*   **Reset#5**: Empty override.
*   **JustRespawned#4**: Sets NPC immunity and calls parent `JustRespawned`.
*   **WaypointReached#2**:
    *   WP 17: Triggers quest completion event.
    *   WP 19: Plays finish dialogue and sets run state.
*   **GetAI_npc_therylune**: Factory function.
*   **QuestAccept_npc_therylune**: On accepting quest 945, starts escort, plays start dialogue, sets temporary faction, and removes immunity.

### npc_volcor (Escape Through Force/Stealth)

*   **npc_volcorAI**: Inherits from `npc_escortAI`. Handles two distinct quest paths (Force vs. Stealth).
*   **Reset#7**: Resets quest ID and dialogue timers if not currently escorting. Enables LOS events.
*   **Aggro#2**: Randomly plays one of three aggro dialogues.
*   **JustRespawned#5**: Sets NPC immunity, quest/gossip flags, and home position. Calls parent `JustRespawned`.
*   **MoveInLineOfSight#3**: Ignores LOS if doing the Stealth quest (to avoid combat). Otherwise, calls parent.
*   **JustSummoned#3**: Makes summoned creatures attack Volcor.
*   **MovementInform**: Handles movement points for the Stealth quest. Moves to predefined locations in `aVolcorLocations`. At WP 5, triggers quest completion and despawns.
*   **StartEscort**: Wrapper to initialize either Force or Stealth mode.
    *   Stealth: Sets friendly faction, starts escort paused.
    *   Force: Starts escort normally, removes immunity.
*   **WaypointReached#3**: Only active for Force quest.
    *   WP 2: Start dialogue.
    *   WP 5, 11, 13: Summons Blackwood Shamans/Ursas at fixed locations.
    *   WP 6: First ambush dialogue.
    *   WP 15: Triggers quest completion, pauses escort, and initiates final dialogue sequence with `NPC_GRIMCLAW`.
*   **UpdateAI#2**:
    *   **Stealth Mode**: Executes a timed dialogue sequence (Bow -> Dialogue -> Moonstalker Form -> Move).
    *   **Force Mode (Finished)**: Executes a timed dialogue sequence with `NPC_GRIMCLAW` (Find Grimclaw -> Face Volcor -> Grimclaw moves -> Dialogues -> Despawn). If Grimclaw is missing, despawns Volcor.
    *   Calls parent `UpdateAI` otherwise.
*   **GetAI_npc_volcor**: Factory function.
*   **QuestAccept_npc_volcor**: On accepting quest 994 or 995, calls `StartEscort`.

### npc_rabid_thistle_bear (Trapped Bear)

*   **npc_rabid_thistle_bearAI**: Inherits from `FollowerAI`.
*   **Reset#4**: Empty override.
*   **UpdateFollowerAI#2**: Decrements `Captured_Timer`. If expired, despawns. If in combat, updates spells and attacks.
*   **StartFollowing**: Sets `Captured_Timer` to 300 seconds and starts following.
*   **JustRespawned#3**: Resets timer and calls parent `JustRespawned`.
*   **GetAI_npc_rabid_thistle_bear**: Factory function.
*   **EffectDummyCreature_npc_rabid_thistle_bear**: Triggered by spell 9439. Updates creature entry to captured bear, gives credit to player, evades combat, and starts following.

### npc_terenthis (Quest Completion)

*   **QuestComplete_npc_terenthis**:
    *   For quests 994/995: Spawns `NPC_SENTINEL_SELARIN` if not present. Returns false to allow DB script to continue.
    *   For quest 985: Checks for `NPC_GRIMCLAW`. Returns false to allow DB script.

### go_beached_sea (Beached Sea Creatures)

*   **QuestAcceptGO_beached_sea**: On accepting specific quests (4722-4733), adds the corresponding beached sea creature item to the player's inventory without checking current count.

### npc_murkdeep (Wanted: Murkdeep)

*   **npc_murkdeepAI**: Inherits from `ScriptedAI`.
*   **Constructor**: Hides creature, sets immunity flags.
*   **Reset#2**: Resets event phase, timers, and state.
*   **BeginEvent**: Initializes event state, stores player/bonfire GUIDs, and starts timer.
*   **JustSummoned**: Makes summoned minions attack the player. If player invalid, despawns minion.
*   **GetPlayer**: Retrieves player from map if alive and near bonfire.
*   **DoSummon**: Summons minions based on event phase (Coatrunners, Warriors, Hunter).
*   **DoAttack**: Handles Sunder Armor (stack check) and Net spells, plus melee attacks.
*   **UpdateAI**:
    *   **Pre-Combat**: Manages event phases. Phase 1-3 summon minions sequentially. Phase 3 reveals Murkdeep and starts combat.
    *   **Combat**: Flees at 15% HP. Otherwise, calls `DoAttack`.
*   **GetAI_npc_murkdeep**: Factory function.
*   **at_murloc_camp**: Area trigger handler. If player has incomplete quest 4740, summons `npc_murkdeep` and begins event.

### Script Registration

*   **AddSC_darkshore**: Registers all scripts defined in this file with the `ScriptMgr`.

## Cross-Unit Boundaries

*   **ScriptedFollowerAI / ScriptedEscortAI**: All AI classes inherit from these base classes (`FollowerAI` or `npc_escortAI`). They provide core functionality like `StartFollow`, `Start`, `HasFollowState`, `SetFollowComplete`, `GetLeaderForFollower`, `GetPlayerForEscort`, `DoSpawnCreature`, and `MovementInform`. The custom AIs override methods like `UpdateFollowerAI`, `WaypointReached`, `MoveInLineOfSight`, and `UpdateAI` to inject quest-specific logic.
*   **ScriptMgr**: Used via `DoScriptText` to play dialogues and emotes.
*   **shared_Util**: Uses `urand` for random number generation (timers, dialogue selection).
*   **Creature / Player / Unit / WorldObject**: Standard object interactions:
    *   `Creature`: `AI()`, `EnableMoveInLosEvent`, `SetFlag`, `RemoveFlag`, `SetStandState`, `SetFactionTemplateId`, `SetFactionTemporary`, `SummonCreature`, `FindNearestCreature`, `GetVictim`, `SelectHostileTarget`, `DoFlee`, `DisappearAndDie`, `ForcedDespawn`, `RemoveCorpse`, `SetHomePosition`, `GetPositionX/Y/Z`, `GetOrientation`, `GetHealthPercent`, `GetVisibility`, `SetVisibility`, `SetWalk`, `SetFacingToObject`.
    *   `Player`: `GetQuestStatus`, `GroupEventHappens`, `GetGossipTextId`, `ADD_GOSSIP_ITEM`, `SEND_GOSSIP_MENU`, `CLOSE_GOSSIP_MENU`, `CanStoreNewItem`, `StoreNewItem`, `SendNewItem`, `IsGameMaster`, `IsDead`, `IsAlive`, `IsInRange`, `GetAngle`, `KilledMonsterCredit`.
    *   `Unit`: `GetEntry`, `GetVictim`, `SelectHostileTarget`, `HandleEmoteCommand`, `HandleEmote`, `GetMotionMaster`, `RemoveAurasDueToSpell`, `GetSpellAuraHolder`.
    *   `WorldObject`: `IsWithinDistInMap`, `GetGUID`, `GetObjectGuid`, `ToPlayer`, `GetMap`.
*   **GridSearchers**: `GetClosestGameObjectWithEntry`, `GetClosestCreatureWithEntry`.
*   **CreatureAI**: `DoMeleeAttackIfReady`, `DoCastSpellIfCan`, `AttackStart`, `UpdateSpellsList`.
*   **SpellCaster**: `CastSpell`.
*   **SpellAuraHolder**: `GetStackAmount`.
*   **QuestDef**: `GetQuestId`.
*   **GossipDef**: `AddMenuItem`, `SendGossipMenu`, `CloseGossip`.
*   **ObjectGuid**: Constructor usage.
*   **Map**: `GetPlayer`, `GetGameObject`.
*   **Script**: `RegisterSelf`.
*   **ScriptLoader**: `AddScripts` (calls `AddSC_darkshore`).

## Data Model

This unit does not directly query or modify database tables. It interacts with the game world through the API functions listed above, which abstract away direct database access. Quest IDs and NPC entries are hardcoded constants.

## Notable Implementation Details

*   **Sleep Mechanic (Kerlonian):** Kerlonian sleeps randomly. The `UpdateFollowerAI` checks a timer. If he sleeps, following is paused (`SetFollowPaused(true)`). He only wakes up if hit by `SPELL_AWAKEN` (17536). This requires the player to cast this specific spell during the escort.
*   **Stealth vs. Force (Volcor):** `npc_volcorAI` handles two quests. `m_uiQuestId` determines behavior.
    *   **Stealth:** `MoveInLineOfSight` is ignored to prevent combat. `MovementInform` drives the path using `aVolcorLocations`. `UpdateAI` runs a dialogue sequence before moving.
    *   **Force:** Standard escort with combat. `WaypointReached` spawns enemies. `UpdateAI` runs a final dialogue sequence with `NPC_GRIMCLAW` after reaching WP 15.
*   **Murkdeep Event:** Triggered by area trigger `at_murloc_camp`. The creature is hidden initially. `BeginEvent` starts a phased summoning process. Minions are summoned in waves (Phases 1-3). Murkdeep reveals himself in Phase 3. He flees at 15% HP.
*   **Gossip Follow (Threshwackonator):** Uses gossip menu to start follow. `MoveInLineOfSight` checks for `NPC_GELKAK` to end the follow and turn hostile.
*   **Item Granting (Beached Sea):** `QuestAcceptGO_beached_sea` grants items without checking if the player already has them, bypassing standard stack limits for this specific quest interaction.
*   **Hardcoded Coordinates:** Several summons use hardcoded coordinates (`aVolcorSpawnLocs`, `aVolcorLocations`, `m_fSummonPoints`). These must match the world data.
*   **Immunity Flags:** Many NPCs set `UNIT_FLAG_IMMUNE_TO_NPC` on respawn or start to prevent unintended aggro from other creatures. This flag is removed when the quest starts or when combat is intended.
*   **Timer Management:** Timers are manually decremented in `UpdateAI` or `UpdateFollowerAI`. Care must be taken to reset timers in `Reset` or `JustRespawned` to avoid stale state.
*   **Dynamic Casting:** `dynamic_cast` is used extensively to access specific AI members from generic `Creature*` pointers in quest accept/hook functions.

## Member Reference

**npc_kerlonianAI**
Constructor for `npc_kerlonianAI`. Initializes the AI and calls `Reset`.

**Reset**
Initializes `m_uiFallAsleepTimer` to a random value (10000-45000ms) and enables `MoveInLosEvent`.

**JustRespawned**
Sets `UNIT_FLAG_IMMUNE_TO_NPC` and calls parent `JustRespawned`.

**MoveInLineOfSight**
Checks for `NPC_LILADRIS`. If seen and close, triggers quest completion for 5321, plays dialogue, and completes follow.

**SpellHit**
If hit by `SPELL_AWAKEN` while following, calls `ClearSleeping`.

**SetSleeping**
Pauses follow, plays random sleep dialogue/emote, sets sleep stand state, changes faction, and casts sleep visual spell.

**ClearSleeping**
Removes sleep aura, stands up, restores faction, enables LOS, plays awaken emote, and resumes follow.

**UpdateFollowerAI**
Manages sleep timer. If expired and not paused, calls `SetSleeping`. If in combat, performs melee attacks.

**GetAI_npc_kerlonian**
Factory function returning `new npc_kerlonianAI`.

**QuestAccept_npc_kerlonian**
On quest 5321 accept: stands Kerlonian, removes immunity, plays start dialogue, and starts follow.

**npc_prospector_remtravelAI**
Constructor for `npc_prospector_remtravelAI`. Initializes AI and calls `Reset`.

**JustRespawned#2**
Sets `UNIT_FLAG_IMMUNE_TO_NPC` and calls parent `JustRespawned`.

**WaypointReached**
Handles waypoint events for quest 731: dialogues, emotes, and summoning gravel creatures at specific WPs. Triggers quest completion at WP 48.

**Reset#3**
Empty override.

**Aggro**
Randomly plays aggro dialogue.

**JustSummoned#2**
Makes summoned creatures attack the escorting player.

**GetAI_npc_prospector_remtravel**
Factory function returning `new npc_prospector_remtravelAI`.

**QuestAccept_npc_prospector_remtravel**
On quest 731 accept: sets faction, plays start dialogue, removes immunity, and starts escort.

**npc_threshwackonatorAI**
Constructor for `npc_threshwackonatorAI`. Initializes AI and calls `Reset`.

**Reset#6**
Enables `MoveInLosEvent`.

**MoveInLineOfSight#2**
Checks for `NPC_GELKAK`. If seen and close, plays dialogue and calls `DoAtEnd`.

**DoAtEnd**
Sets faction to hostile, attacks leader, and completes follow.

**GetAI_npc_threshwackonator**
Factory function returning `new npc_threshwackonatorAI`.

**GossipHello_npc_threshwackonator**
Adds gossip item "[PH] Insert key" if quest 2078 is incomplete.

**GossipSelect_npc_threshwackonator**
On gossip select: closes menu, plays start emote, and starts follow.

**npc_theryluneAI**
Constructor for `npc_theryluneAI`. Initializes AI and calls `Reset`.

**Reset#5**
Empty override.

**JustRespawned#4**
Sets `UNIT_FLAG_IMMUNE_TO_NPC` and calls parent `JustRespawned`.

**WaypointReached#2**
WP 17: Triggers quest completion. WP 19: Plays finish dialogue and sets run state.

**GetAI_npc_therylune**
Factory function returning `new npc_theryluneAI`.

**QuestAccept_npc_therylune**
On quest 945 accept: starts escort, plays start dialogue, sets temporary faction, and removes immunity.

**npc_volcorAI**
Constructor for `npc_volcorAI`. Initializes AI and calls `Reset`.

**Reset#7**
Resets quest ID and dialogue timers if not escorting. Enables `MoveInLosEvent`.

**Aggro#2**
Randomly plays one of three aggro dialogues.

**JustRespawned#5**
Sets `UNIT_FLAG_IMMUNE_TO_NPC`, quest/gossip flags, and home position. Calls parent `JustRespawned`.

**MoveInLineOfSight#3**
Ignores LOS if doing Stealth quest. Otherwise calls parent.

**JustSummoned#3**
Makes summoned creatures attack Volcor.

**MovementInform**
Handles Stealth quest movement using `aVolcorLocations`. Triggers quest completion and despawn at WP 5.

**StartEscort**
Initializes Force or Stealth mode. Stealth: sets friendly faction, starts paused. Force: starts normally, removes immunity.

**WaypointReached#3**
Force quest only. Spawns enemies at WPs 5, 11, 13. Plays dialogues. At WP 15, triggers quest completion and starts final dialogue sequence.

**UpdateAI#2**
Stealth: Runs timed dialogue/movement sequence. Force (Finished): Runs timed dialogue sequence with `NPC_GRIMCLAW`. Calls parent otherwise.

**GetAI_npc_volcor**
Factory function returning `new npc_volcorAI`.

**QuestAccept_npc_volcor**
On quest 994/995 accept: calls `StartEscort`.

**npc_rabid_thistle_bearAI**
Constructor for `npc_rabid_thistle_bearAI`. Initializes AI, calls `Reset`, and sets `Captured_Timer` to -1.

**Reset#4**
Empty override.

**UpdateFollowerAI#2**
Decrements `Captured_Timer`. Despawns if expired. If in combat, updates spells and attacks.

**StartFollowing**
Sets `Captured_Timer` to 300000ms and starts follow.

**JustRespawned#3**
Resets `Captured_Timer` and calls parent `JustRespawned`.

**GetAI_npc_rabid_thistle_bear**
Factory function returning `new npc_rabid_thistle_bearAI`.

**EffectDummyCreature_npc_rabid_thistle_bear**
Triggered by spell 9439. Updates entry, gives credit, evades, and starts follow.

**QuestComplete_npc_terenthis**
Spawns `NPC_SENTINEL_SELARIN` for quests 994/995 if not present. Checks for `NPC_GRIMCLAW` for quest 985.

**QuestAcceptGO_beached_sea**
Grants beached sea creature items to player on quest accept, bypassing stack checks.

**npc_murkdeepAI**
Constructor for `npc_murkdeepAI`. Hides creature, sets immunity flags, and calls `Reset`.

**Reset#2**
Resets event phase, timers, and state.

**BeginEvent**
Initializes event state, stores player/bonfire GUIDs, and starts timer.

**JustSummoned**
Makes minions attack player. Despawns minion if player invalid.

**GetPlayer**
Retrieves player from map if alive and near bonfire.

**DoSummon**
Summons minions based on event phase.

**DoAttack**
Handles Sunder Armor and Net spells, plus melee attacks.

**UpdateAI**
Pre-combat: Manages phased summoning. Combat: Flees at 15% HP, otherwise attacks.

**GetAI_npc_murkdeep**
Factory function returning `new npc_murkdeepAI`.

**at_murloc_camp**
Area trigger handler. Summons `npc_murkdeep` and begins event if player has incomplete quest 4740.

**AddSC_darkshore**
Registers all scripts in this file with `ScriptMgr`.

---

<!-- machine-true, projected from graph.json -->

## Map — darkshore

*Source:* darkshore.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| npc_kerlonianAI | ctor | ScriptedFollowerAI/FollowerAI | — | — |
| Reset | method | Creature.Main/EnableMoveInLosEvent, shared_Util/urand | — | — |
| JustRespawned | method | ScriptedFollowerAI/JustRespawned, WorldObject.Object/SetFlag | — | — |
| MoveInLineOfSight | method | Object/GetEntry, Player.Main/GetQuestStatus, Player.Main/GroupEventHappens, ScriptedFollowerAI/GetLeaderForFollower, ScriptedFollowerAI/HasFollowState, ScriptedFollowerAI/MoveInLineOfSight, ScriptedFollowerAI/SetFollowComplete, ScriptMgr/DoScriptText, Unit.Main/GetVictim, WorldObject.Object/IsWithinDistInMap | — | — |
| SpellHit | method | ScriptedFollowerAI/HasFollowState | — | — |
| SetSleeping | method | ScriptedFollowerAI/SetFollowPaused, ScriptMgr/DoScriptText, shared_Util/urand, SpellCaster/CastSpell#2, Unit.Main/SetFactionTemplateId, Unit.Main/SetStandState | — | — |
| ClearSleeping | method | Creature.Main/EnableMoveInLosEvent, ScriptedFollowerAI/SetFollowPaused, ScriptMgr/DoScriptText, Unit.Main/RemoveAurasDueToSpell, Unit.Main/SetFactionTemplateId, Unit.Main/SetStandState | — | — |
| UpdateFollowerAI | method | CreatureAI/DoMeleeAttackIfReady, ScriptedFollowerAI/HasFollowState, shared_Util/urand, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_npc_kerlonian | function | — | — | — |
| QuestAccept_npc_kerlonian | function | Creature.Main/AI, QuestDef/GetQuestId, ScriptedFollowerAI/StartFollow, ScriptMgr/DoScriptText, Unit.Main/SetStandState, WorldObject.Object/RemoveFlag | — | — |
| npc_prospector_remtravelAI | ctor | ScriptedEscortAI/npc_escortAI | — | — |
| JustRespawned#2 | method | ScriptedEscortAI/JustRespawned, WorldObject.Object/SetFlag | — | — |
| WaypointReached | method | Player.Main/GroupEventHappens, ScriptedAI/DoSpawnCreature, ScriptedEscortAI/GetPlayerForEscort, ScriptMgr/DoScriptText, Unit.Main/HandleEmoteCommand, WorldObject.Object/SummonCreature#2 | — | — |
| Reset#3 | method | — | — | — |
| Aggro | method | ScriptMgr/DoScriptText, shared_Util/urand | — | — |
| JustSummoned#2 | method | Creature.Main/AI, CreatureAI/AttackStart, ScriptedEscortAI/GetPlayerForEscort | — | — |
| GetAI_npc_prospector_remtravel | function | — | — | — |
| QuestAccept_npc_prospector_remtravel | function | Creature.Main/AI, Object/GetGUID, QuestDef/GetQuestId, ScriptedEscortAI/Start, ScriptMgr/DoScriptText, Unit.Main/HandleEmoteCommand, Unit.Main/SetFactionTemplateId, WorldObject.Object/RemoveFlag | — | — |
| npc_threshwackonatorAI | ctor | ScriptedFollowerAI/FollowerAI | — | — |
| Reset#6 | method | Creature.Main/EnableMoveInLosEvent | — | — |
| MoveInLineOfSight#2 | method | Object/GetEntry, ScriptedFollowerAI/HasFollowState, ScriptedFollowerAI/MoveInLineOfSight, ScriptMgr/DoScriptText, Unit.Main/GetVictim, WorldObject.Object/IsWithinDistInMap | — | — |
| DoAtEnd | method | Creature.Main/AI, CreatureAI/AttackStart, ScriptedFollowerAI/GetLeaderForFollower, ScriptedFollowerAI/SetFollowComplete, Unit.Main/SetFactionTemplateId | — | — |
| GetAI_npc_threshwackonator | function | — | — | — |
| GossipHello_npc_threshwackonator | function | GossipDef/AddMenuItem#4, GossipDef/SendGossipMenu, Object/GetGUID, ObjectGuid/ObjectGuid#5, Player.Main/GetGossipTextId, Player.Main/GetQuestStatus, PlayerMenu/GetGossipMenu | — | — |
| GossipSelect_npc_threshwackonator | function | Creature.Main/AI, GossipDef/CloseGossip, ScriptedFollowerAI/StartFollow, ScriptMgr/DoScriptText | — | — |
| npc_theryluneAI | ctor | ScriptedEscortAI/npc_escortAI | — | — |
| Reset#5 | method | — | — | — |
| JustRespawned#4 | method | ScriptedEscortAI/JustRespawned, WorldObject.Object/SetFlag | — | — |
| WaypointReached#2 | method | Player.Main/GroupEventHappens, ScriptedEscortAI/GetPlayerForEscort, ScriptedEscortAI/SetRun, ScriptMgr/DoScriptText | — | — |
| GetAI_npc_therylune | function | — | — | — |
| QuestAccept_npc_therylune | function | Creature.Main/AI, Creature.Main/SetFactionTemporary, Object/GetGUID, QuestDef/GetQuestId, ScriptedEscortAI/Start, ScriptMgr/DoScriptText, WorldObject.Object/RemoveFlag | — | — |
| npc_volcorAI | ctor | ScriptedEscortAI/npc_escortAI | — | — |
| Reset#7 | method | Creature.Main/EnableMoveInLosEvent, ScriptedEscortAI/HasEscortState | — | — |
| Aggro#2 | method | ScriptMgr/DoScriptText, shared_Util/urand | — | — |
| JustRespawned#5 | method | Creature.Main/SetHomePosition, ScriptedEscortAI/JustRespawned, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/SetFlag | — | — |
| MoveInLineOfSight#3 | method | ScriptedEscortAI/MoveInLineOfSight | — | — |
| JustSummoned#3 | method | Creature.Main/AI, CreatureAI/AttackStart | — | — |
| MovementInform | method | Creature.Main/DisappearAndDie, Creature.MotionMaster/MovePoint, Player.Main/GetQuestStatus, Player.Main/GroupEventHappens, ScriptedEscortAI/GetPlayerForEscort, ScriptedEscortAI/MovementInform, Unit.Main/GetMotionMaster | — | — |
| StartEscort | method | Creature.Main/SetFactionTemporary, Object/GetGUID, QuestDef/GetQuestId, ScriptedEscortAI/SetEscortPaused, ScriptedEscortAI/Start, Unit.Main/SetFacingToObject, Unit.Main/SetStandState, WorldObject.Object/RemoveFlag | — | — |
| WaypointReached#3 | method | Player.Main/GroupEventHappens, ScriptedEscortAI/GetPlayerForEscort, ScriptedEscortAI/SetEscortPaused, ScriptMgr/DoScriptText, WorldObject.Object/SummonCreature#2 | — | — |
| UpdateAI#2 | method | Creature.Main/ForcedDespawn, Creature.MotionMaster/MovePoint, CreatureAI/DoCastSpellIfCan, Player.Main/GetQuestStatus, ScriptedEscortAI/GetPlayerForEscort, ScriptedEscortAI/UpdateAI, ScriptMgr/DoScriptText, Unit.Main/GetMotionMaster, Unit.Main/HandleEmote, Unit.Main/SetFacingToObject, Unit.Main/SetWalk, WorldObject.Object/FindNearestCreature, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/SetFlag | — | — |
| GetAI_npc_volcor | function | — | — | — |
| QuestAccept_npc_volcor | function | Creature.Main/AI, QuestDef/GetQuestId | — | — |
| npc_rabid_thistle_bearAI | ctor | ScriptedFollowerAI/FollowerAI | — | — |
| Reset#4 | method | — | — | — |
| UpdateFollowerAI#2 | method | Creature.Main/DisappearAndDie, CreatureAI/DoMeleeAttackIfReady, CreatureAI/UpdateSpellsList, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| StartFollowing | method | ScriptedFollowerAI/StartFollow | — | — |
| JustRespawned#3 | method | ScriptedFollowerAI/JustRespawned | — | — |
| GetAI_npc_rabid_thistle_bear | function | — | — | — |
| EffectDummyCreature_npc_rabid_thistle_bear | function | Creature.Main/AI, Creature.Main/UpdateEntry, Object/GetEntry, Object/GetObjectGuid, Object/ToPlayer, Player.Main/KilledMonsterCredit, ScriptedFollowerAI/EnterEvadeMode | — | — |
| QuestComplete_npc_terenthis | function | QuestDef/GetQuestId, Unit.Main/SetWalk, WorldObject.Object/FindNearestCreature, WorldObject.Object/SummonCreature#2 | — | — |
| QuestAcceptGO_beached_sea | function | Player.Main/CanStoreNewItem, Player.Main/SendNewItem, Player.Main/StoreNewItem, QuestDef/GetQuestId | — | — |
| npc_murkdeepAI | ctor | ScriptedAI/ScriptedAI, Unit.Main/SetVisibility, WorldObject.Object/SetFlag | — | — |
| Reset#2 | method | shared_Util/urand | — | — |
| BeginEvent | method | GridSearchers/GetClosestGameObjectWithEntry, Object/GetObjectGuid | — | — |
| JustSummoned | method | Creature.Main/AI, Creature.Main/ForcedDespawn, Creature.Main/RemoveCorpse, CreatureAI/AttackStart | — | — |
| GetPlayer | method | Map.Main/GetGameObject, Map.Main/GetPlayer, Unit.Main/IsAlive, WorldObject.Object/GetMap, WorldObject.Object/IsInRange | — | — |
| DoSummon | method | WorldObject.Object/GetOrientation, WorldObject.Object/SummonCreature#2 | — | — |
| DoAttack | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, shared_Util/urand, SpellAuraHolder/GetStackAmount, Unit.Main/GetSpellAuraHolder#2, Unit.Main/GetVictim | — | — |
| UpdateAI | method | Creature.Main/DoFlee, Creature.Main/ForcedDespawn, Creature.Main/RemoveCorpse, CreatureAI/AttackStart, Unit.Main/GetHealthPercent, Unit.Main/GetVictim, Unit.Main/GetVisibility, Unit.Main/SelectHostileTarget, Unit.Main/SetVisibility, WorldObject.Object/RemoveFlag | — | — |
| GetAI_npc_murkdeep | function | — | — | — |
| at_murloc_camp | function | Creature.Main/AI, GridSearchers/GetClosestCreatureWithEntry, Player.Main/GetQuestStatus, Player.Main/IsGameMaster, Unit.Main/IsAlive, Unit.Main/IsDead, WorldObject.Object/GetAngle#2, WorldObject.Object/SummonCreature#2 | — | — |
| AddSC_darkshore | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
