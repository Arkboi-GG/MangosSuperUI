# the_barrens

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Architecture and Reference Documentation: `the_barrens`

## Purpose & Responsibilities

The `the_barrens` translation unit (`the_barrens.cpp`) implements scripted behaviors for Non-Player Characters (NPCs), Area Triggers, and Event Handlers specific to the "Barrens" zone in the game world. It serves as a collection of discrete AI scripts and quest hooks that drive narrative events, combat encounters, and escort missions.

Key responsibilities include:
1.  **Escort Missions:** Managing complex state machines for NPCs Gilthares and Wizzlecrank's Shredder, including waypoint navigation, dialogue triggers, faction changes, and summoning auxiliary creatures.
2.  **Event Sequences:** Orchestrating the "Affray" event triggered by NPC Twiggy Flathead, involving the summoning of challengers, spectators, and a boss creature (Big Will), with synchronized emotes and combat phases.
3.  **Combat AI:** Implementing specialized combat logic for NPCs like Polly (taunt on aggro), Sarilus Foulborne (spell casting timers), and Venture Co. employees (counter-spells based on incoming rogue abilities).
4.  **Quest Integration:** Hooking into the quest system to trigger AI starts, update quest status via `GroupEventHappens`, and handle area-based quest checks.
5.  **Event Handling:** Processing specific global event IDs (e.g., `EVENT_THE_PRINCIPLE_SOURCE`) to summon hostile mobs.

This unit does not access any database tables directly; all data is driven by in-game entities, quest definitions, and hardcoded coordinates/constants.

## Member-by-Member Behavior

### 1. NPC Polly (`npc_pollyAI`)
A simple AI for a parrot-like creature that reacts to aggression.

*   **`npc_pollyAI` (ctor):** Initializes the AI and calls `Reset`.
*   **`Reset#3`:** Resets the internal boolean flag `b_text` to `false`, ensuring the taunt can play again after a reset.
*   **`Aggro#2`:** Triggered when the creature enters combat. If `b_text` is false, it broadcasts two specific sound/text lines (`SAY_CRACKER`, `SAY_SQUAWK`) and sets `b_text` to true to prevent spamming during the same combat encounter.
*   **`GetAI_npc_polly`:** Factory function returning a new instance of `npc_pollyAI`.

### 2. NPC Gilthares (`npc_giltharesAI`)
An escort AI for the quest "Free From Hold". Gilthares moves through waypoints, delivering dialogue and reacting to combat.

*   **`npc_giltharesAI` (ctor):** Inherits from `npc_escortAI` and calls `Reset`.
*   **`Reset`:** Empty override. Relies on base class reset behavior.
*   **`JustRespawned`:** Sets the `UNIT_FLAG_IMMUNE_TO_NPC` flag on the creature to prevent other NPCs from attacking it immediately upon respawn, then calls the base class `JustRespawned`.
*   **`WaypointReached`:** Handles dialogue and quest progression at specific waypoint IDs:
    *   WP 16, 17, 18, 37, 47: Deliver specific dialogue lines to the escorting player.
    *   WP 53: Delivers final dialogue and triggers `GroupEventHappens` for quest `QUEST_FREE_FROM_HOLD` (ID 898), marking the quest complete for the group.
*   **`Aggro`:** Randomly selects an aggro line (1 in 4 chance to speak). Only speaks if the attacker is **not** a player and the creature is in `AREA_MERCHANT_COAST` (ID 391). This prevents Gilthares from yelling at players who accidentally hit him, while allowing him to react to hostile NPCs in the specific danger zone.
*   **`GetAI_npc_gilthares`:** Factory function.
*   **`QuestAccept_npc_gilthares`:** Global hook for quest acceptance. If the quest is `QUEST_FREE_FROM_HOLD`:
    *   Sets faction to neutral active.
    *   Removes immunity flags.
    *   Sets stand state to standing.
    *   Plays start dialogue.
    *   Casts `dynamic_cast` to get the AI pointer and calls `Start()` to begin the escort.

### 3. NPC Twiggy Flathead (`npc_twiggy_flatheadAI`)
Manages a complex, timed event sequence ("The Affray") involving multiple summoned creatures.

*   **`npc_twiggy_flatheadAI` (ctor):** Initializes AI and calls `Reset`.
*   **`Reset#5`:** Resets all event state variables: `EventInProgress`, timers, step counter, challenger count, and GUID arrays.
*   **`CanStartEvent`:** Checks if an event is already running. If not, sets `EventInProgress` to true, stores the player's GUID, plays start dialogue, and returns `true`. Logs a debug message if an event is already active.
*   **`SetChallengers`:** Iterates 6 times to summon `NPC_AFFRAY_CHALLENGER` at predefined coordinates (`AffrayChallengerLoc`). Each challenger is set to friendly faction, flagged as spawning, roars, and its GUID is stored in the `AffrayChallenger` array.
*   **`SetChallengerReady`:** Called on a specific challenger unit. Removes spawning/not-selectable flags, makes it roar, and changes its faction to `FACTION_MONSTER` (hostile), effectively entering it into the fight.
*   **`UpdateAI#2`:** The main event loop.
    *   **Challenger Death Timer:** Every 2.5s, checks if any stored challengers are dead. If so, plays "down" dialogue, removes the corpse, and clears the GUID.
    *   **Event Timer:** Drives the step-by-step sequence:
        *   **Step 0:** Calls `SetChallengers()`, starts death timer, advances to Step 1.
        *   **Step 1:** Plays "fray" dialogue. Calls `SetChallengerReady` on the next challenger in the array. Increments count. If all 6 are ready, advances to Step 2.
        *   **Step 2:** Summons `NPC_BIG_WILL` (the boss) at specific coords, sets it friendly, and moves it to the arena center. Advances to Step 3.
        *   **Step 3:** Changes Big Will's faction to `FACTION_CREATURE` (neutral/hostile context) and plays "ready" dialogue. Advances to Step 4.
        *   **Step 4:** Checks if Big Will is dead. If yes, plays "over" dialogue and resets the event. If Big Will disappears unexpectedly, resets.
    *   **Emote Timer:** Every 2s, randomly makes remaining challengers roar. Also iterates over nearby `NPC_AFFRAY_SPECTATOR` creatures and randomly makes them cheer or act rude.
*   **`GetAI_npc_twiggy_flathead`:** Factory function.
*   **`AreaTrigger_at_twiggy_flathead`:** Triggered when a player enters the area. Checks if the player is alive and has the incomplete quest `QUEST_AFFRAY` (1719). Finds the closest Twiggy NPC. If found, calls `CanStartEvent`. Returns `false` to stop further processing if the event started successfully.

### 4. NPC Wizzlecrank's Shredder (`npc_wizzlecranks_shredderAI`)
An escort AI for the quest "Escape", involving a vehicle/mechanical theme with pilot summoning and mercenary attacks.

*   **`npc_wizzlecranks_shredderAI` (ctor):** Initializes post-event timers and flags.
*   **`Reset#6`:** If not currently escorting, ensures the creature stands up if dead, and resets post-event timers.
*   **`WaypointReached#2`:**
    *   WP 0: Plays startup dialogue.
    *   WP 9: Stops running.
    *   WP 17: Summons two mercenaries at specific locations. One speaks; the other is silent.
    *   WP 24: Sets `m_bIsPostEvent` to true, indicating the escort path is done and the finale begins.
*   **`WaypointStart`:**
    *   WP 9: Plays second startup dialogue.
    *   WP 18: Plays progress dialogue and starts running.
*   **`JustSummoned#2`:**
    *   If `NPC_PILOT_WIZZ` is summoned: Sets the Shredder to dead stand state (visual effect) and restores its original faction.
    *   If `NPC_MERCENARY` is summoned: Immediately attacks the Shredder.
*   **`UpdateEscortAI`:**
    *   If no target: Handles the post-event sequence.
        *   Uses `m_uiPostEventTimer` to sequence dialogue (Progress 2, Progress 3, End).
        *   On final step (count 3): Triggers `GroupEventHappens` for `QUEST_ESCAPE` (863), summons the Pilot, and resets home position.
    *   If target exists: Performs melee attacks.
*   **`QuestAccept_npc_wizzlecranks_shredder`:** Hooks quest `QUEST_ESCAPE`. Sets faction to Ratchet, plays start dialogue, and starts the escort.
*   **`GetAI_npc_wizzlecranks_shredder`:** Factory function.

### 5. Mission: Possible But Not Probable (`npc_mission_possible_but_not_probableAI`)
A reactive AI for Venture Co. NPCs that counters specific Rogue spells.

*   **`npc_mission_possible_but_not_probableAI` (ctor):** Initializes AI.
*   **`Reset#2`:** Empty override.
*   **`SpellHit`:** Triggered when the creature is hit by a spell. Checks the creature's entry ID and the incoming spell's family/type:
    *   **Mutated Drone:** Counters Rogue Garrote/Ambush with `SPELL_JUGGLER_VEIN_RUPTURE`.
    *   **Patroller:** Counters Rogue Rupture with `SPELL_LUNG_PUNCTURE`.
    *   **Lookout:** Counters Rogue Eviscerate with `SPELL_SLUSH`.
    *   **Grand Foreman Puzik:** Counters Rogue Ambush with `SPELL_DECIMATE`.
    *   Uses `DoCastSpellIfCan` to cast the counter-spell on itself.
*   **`GetAI_npc_mission_possible_but_not_probable`:** Factory function.

### 6. Sarilus Foulborne (`npc_sarilus_foulborneAI`)
A caster AI with timed spell rotations.

*   **`npc_sarilus_foulborneAI` (ctor):** Initializes AI.
*   **`Reset#4`:** Initializes timers for Elementals and Frostbolt with random offsets to desynchronize casts.
*   **`JustSummoned`:** Applies passive buffs (`SPELL_SARILUS_ELEMENTALS_PASSIVE`, `SPELL_FEED_SARILUS_PASSIVE`) to any creature summoned by Sarilus.
*   **`UpdateAI`:**
    *   Checks for valid victim.
    *   **Elementals Timer:** Casts `SPELL_SARILUS_ELEMENTALS` on self every ~9s.
    *   **Frostbolt Timer:** Casts `SPELL_FROSTBOLT` on victim every ~3.5-4.5s.
    *   Performs melee attacks if ready.
*   **`GetAI_npc_sarilus_foulborne`:** Factory function.

### 7. Event: The Principle Source (`ProcessEventId_event_the_principle_source`)
A global event handler.

*   **`ProcessEventId_event_the_principle_source`:** Triggered by event ID `EVENT_THE_PRINCIPLE_SOURCE` (5246).
    *   Validates the source is a player.
    *   Iterates through a static array of `Toxicologist` coordinates.
    *   Summons `NPC_BURNING_BLADE_TOXICOLOGIST` at each coordinate.
    *   Commands each summoned toxicologist to attack the player immediately.

### 8. Script Registration (`AddSC_the_barrens`)
Registers all the above scripts with the `ScriptMgr`.

*   **`AddSC_the_barrens`:** Creates `Script` objects for each NPC/AreaTrigger/Event, assigns the appropriate factory/hook functions, and calls `RegisterSelf`. Called by `ScriptLoader/AddScripts`.

## Cross-Unit Boundaries

*   **`ScriptedAI` / `npc_escortAI`:** Base classes providing core AI functionality (timers, movement, state management). All custom AI structs inherit from these.
*   **`ScriptMgr`:** Used via `DoScriptText` to broadcast dialogue/sounds.
*   **`WorldObject.Object`:** Used for flag manipulation (`SetFlag`, `RemoveFlag`), area ID retrieval (`GetAreaId`), and summoning (`SummonCreature`).
*   **`Unit.Main`:** Used for faction changes (`SetFactionTemplateId`), stand states (`SetStandState`), emotes (`HandleEmoteCommand`), and combat targeting (`GetVictim`, `SelectHostileTarget`).
*   **`Creature.Main`:** Used for AI retrieval (`AI`), corpse removal (`RemoveCorpse`), and motion control (`MovePoint`).
*   **`Map.Main`:** Used to retrieve entities by GUID (`GetCreature`, `GetPlayer`, `GetUnit`) within the current map instance.
*   **`Player.Main`:** Used for quest status checks (`GetQuestStatus`) and triggering quest completion events (`GroupEventHappens`).
*   **`shared_Util`:** Provides `urand` for random number generation.
*   **`Log.Main`:** Used for debug logging in the Twiggy Flathead event.
*   **`GridSearchers`:** Used in `AreaTrigger_at_twiggy_flathead` to find the nearest NPC (`GetClosestCreatureWithEntry`).
*   **`Script`:** Used in `AddSC_the_barrens` to define script metadata.

## Data Model

This unit does not interact with any database tables. All logic is driven by:
*   Hardcoded constants (quest IDs, spell IDs, NPC entries, coordinates).
*   Runtime entity states (flags, factions, health).
*   External quest definitions (referenced by ID, but not queried directly).

## Notable Implementation Details

1.  **Twiggy Flathead State Machine:** The `UpdateAI` method in `npc_twiggy_flatheadAI` implements a finite state machine using a `Step` variable. It relies heavily on GUID storage to track summoned entities. If a summoned entity dies or despawns unexpectedly before its step, the `GetUnit` check fails, potentially causing the event to reset or stall. The code explicitly checks `!challenger->IsAlive() && challenger->IsDead()` to clean up corpses.
2.  **Gilthares Aggro Logic:** The `Aggro` method in `npc_giltharesAI` has a specific condition: `pWho->GetTypeId() != TYPEID_PLAYER`. This means Gilthares will *never* yell at players who aggro him, only at other NPCs. This is likely intentional design to avoid annoying players during the escort, but it means the AI is silent against player-initiated combat.
3.  **Wizzlecrank's Post-Event Sequence:** The `UpdateEscortAI` in `npc_wizzlecranks_shredderAI` uses a separate timer (`m_uiPostEventTimer`) and counter (`m_uiPostEventCount`) to handle dialogue and quest completion *after* the escort path ends (WP 24). This decouples the finale from the movement logic.
4.  **Venture Co. Counter-Spells:** The `SpellHit` method in `npc_mission_possible_but_not_probableAI` uses template-based spell family checking (`IsFitToFamily`). This allows it to react to specific Rogue abilities regardless of the exact spell ID, making it robust against spell ID changes as long as the family/type remains consistent.
5.  **Area Trigger Return Values:** In `AreaTrigger_at_twiggy_flathead`, returning `false` stops the engine from processing further triggers for that event, while `true` allows continuation. This is used to ensure the event only starts once per player entry if conditions are met.
6.  **Hardcoded Coordinates:** Several summons (Twiggy challengers, Big Will, Mercenaries, Toxicologists) use hardcoded float coordinates. Any change in map geometry would require updating these values.

## Member Reference

*   **`npc_pollyAI`**: Constructor for Polly's AI, initializing the base `ScriptedAI` and calling `Reset`.
*   **`Reset#3`**: Resets Polly's `b_text` flag to allow taunts to play again.
*   **`Aggro#2`**: Plays taunt sounds/text once per combat engagement for Polly.
*   **`GetAI_npc_polly`**: Factory function creating `npc_pollyAI`.
*   **`npc_giltharesAI`**: Constructor for Gilthares' escort AI.
*   **`Reset`**: Empty override for Gilthares, relying on base class reset.
*   **`JustRespawned`**: Sets NPC immunity flag on Gilthares upon respawn.
*   **`WaypointReached`**: Handles dialogue and quest completion for Gilthares at specific waypoints.
*   **`Aggro`**: Plays random aggro lines for Gilthares only against non-player attackers in Merchant Coast.
*   **`GetAI_npc_gilthares`**: Factory function creating `npc_giltharesAI`.
*   **`QuestAccept_npc_gilthares`**: Hook to start Gilthares' escort quest, setting faction, flags, and AI state.
*   **`npc_twiggy_flatheadAI`**: Constructor for Twiggy Flathead's event AI.
*   **`Reset#5`**: Resets all event state variables for Twiggy Flathead.
*   **`CanStartEvent`**: Checks if the Affray event can start, sets state, and plays intro dialogue.
*   **`SetChallengers`**: Summons 6 challenger NPCs at predefined locations for the Affray.
*   **`SetChallengerReady`**: Prepares a specific challenger for combat by removing flags and changing faction.
*   **`UpdateAI#2`**: Main loop for Twiggy Flathead, managing event steps, challenger deaths, and spectator emotes.
*   **`GetAI_npc_twiggy_flathead`**: Factory function creating `npc_twiggy_flatheadAI`.
*   **`AreaTrigger_at_twiggy_flathead`**: Area trigger hook to initiate the Affray event for eligible players.
*   **`npc_wizzlecranks_shredderAI`**: Constructor for Wizzlecrank's Shredder escort AI.
*   **`Reset#6`**: Resets post-event timers and stand state for the Shredder.
*   **`WaypointReached#2`**: Handles dialogue, mercenary summons, and post-event flag for the Shredder.
*   **`WaypointStart`**: Handles dialogue and run state changes for the Shredder.
*   **`JustSummoned#2`**: Handles visual/faction changes for the Pilot and initiates combat for Mercenaries.
*   **`UpdateEscortAI`**: Manages melee combat and the post-escort dialogue/quest completion sequence.
*   **`QuestAccept_npc_wizzlecranks_shredder`**: Hook to start the Shredder's escort quest.
*   **`GetAI_npc_wizzlecranks_shredder`**: Factory function creating `npc_wizzlecranks_shredderAI`.
*   **`npc_mission_possible_but_not_probableAI`**: Constructor for Venture Co. NPCs' reactive AI.
*   **`Reset#2`**: Empty override for Venture Co. NPCs.
*   **`SpellHit`**: Counters specific Rogue spells with unique spells based on NPC type.
*   **`GetAI_npc_mission_possible_but_not_probable`**: Factory function creating `npc_mission_possible_but_not_probableAI`.
*   **`npc_sarilus_foulborneAI`**: Constructor for Sarilus Foulborne's caster AI.
*   **`Reset#4`**: Initializes random timers for Sarilus's spells.
*   **`JustSummoned`**: Applies passive buffs to creatures summoned by Sarilus.
*   **`UpdateAI`**: Manages spell casting timers and melee attacks for Sarilus.
*   **`GetAI_npc_sarilus_foulborne`**: Factory function creating `npc_sarilus_foulborneAI`.
*   **`ProcessEventId_event_the_principle_source`**: Global event handler summoning Toxicologists to attack the triggering player.
*   **`AddSC_the_barrens`**: Registers all scripts in this unit with the script manager.

---

<!-- machine-true, projected from graph.json -->

## Map — the_barrens

*Source:* the_barrens.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| npc_pollyAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#3 | method | — | — | — |
| Aggro#2 | method | ScriptMgr/DoScriptText | — | — |
| GetAI_npc_polly | function | — | — | — |
| npc_giltharesAI | ctor | ScriptedEscortAI/npc_escortAI | — | — |
| Reset | method | — | — | — |
| JustRespawned | method | ScriptedEscortAI/JustRespawned, WorldObject.Object/SetFlag | — | — |
| WaypointReached | method | Player.Main/GroupEventHappens, ScriptedEscortAI/GetPlayerForEscort, ScriptMgr/DoScriptText | — | — |
| Aggro | method | Object/GetTypeId, ScriptMgr/DoScriptText, shared_Util/urand, WorldObject.Object/GetAreaId | — | — |
| GetAI_npc_gilthares | function | — | — | — |
| QuestAccept_npc_gilthares | function | Creature.Main/AI, Object/GetGUID, QuestDef/GetQuestId, ScriptedEscortAI/Start, ScriptMgr/DoScriptText, Unit.Main/SetFactionTemplateId, Unit.Main/SetStandState, WorldObject.Object/RemoveFlag | — | — |
| npc_twiggy_flatheadAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#5 | method | — | — | — |
| CanStartEvent | method | Log.Main/Out, Object/GetGUID, ScriptMgr/DoScriptText | — | — |
| SetChallengers | method | Log.Main/Out, Object/GetGUID, Unit.Main/HandleEmoteCommand, Unit.Main/SetFactionTemplateId, WorldObject.Object/SetFlag, WorldObject.Object/SummonCreature#2 | — | — |
| SetChallengerReady | method | Unit.Main/HandleEmoteCommand, Unit.Main/SetFactionTemplateId, WorldObject.Object/RemoveFlag | — | — |
| UpdateAI#2 | method | Creature.Main/RemoveCorpse, Creature.MotionMaster/MovePoint, Map.Main/GetCreature, Map.Main/GetPlayer, Map.Main/GetUnit, Object/GetGUID, ObjectGuid/ObjectGuid#5, ScriptMgr/DoScriptText, shared_Util/urand, Unit.Main/GetMotionMaster, Unit.Main/HandleEmoteCommand, Unit.Main/IsAlive, Unit.Main/IsDead, Unit.Main/SetFactionTemplateId, WorldObject.Object/GetCreatureListWithEntryInGrid, WorldObject.Object/GetMap, WorldObject.Object/SummonCreature#2 | — | — |
| GetAI_npc_twiggy_flathead | function | — | — | — |
| AreaTrigger_at_twiggy_flathead | function | Creature.Main/AI, GridSearchers/GetClosestCreatureWithEntry, Player.Main/GetQuestStatus, Unit.Main/IsDead | — | — |
| npc_wizzlecranks_shredderAI | ctor | ScriptedEscortAI/npc_escortAI | — | — |
| Reset#6 | method | ScriptedEscortAI/HasEscortState, Unit.Main/GetStandState, Unit.Main/SetStandState | — | — |
| WaypointReached#2 | method | ScriptedEscortAI/GetPlayerForEscort, ScriptedEscortAI/SetRun, ScriptMgr/DoScriptText, WorldObject.Object/SummonCreature#2 | — | — |
| WaypointStart | method | ScriptedEscortAI/GetPlayerForEscort, ScriptedEscortAI/SetRun, ScriptMgr/DoScriptText | — | — |
| JustSummoned#2 | method | Creature.Main/AI, CreatureAI/AttackStart, Object/GetEntry, Unit.Main/RestoreFaction, Unit.Main/SetStandState | — | — |
| UpdateEscortAI | method | Creature.Main/ResetHomePosition, CreatureAI/DoMeleeAttackIfReady, Player.Main/GroupEventHappens, ScriptedEscortAI/GetPlayerForEscort, ScriptMgr/DoScriptText, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget, WorldObject.Object/SummonCreature#2 | — | — |
| QuestAccept_npc_wizzlecranks_shredder | function | Creature.Main/AI, Object/GetGUID, QuestDef/GetQuestId, ScriptedEscortAI/Start, ScriptMgr/DoScriptText, Unit.Main/SetFactionTemplateId | — | — |
| GetAI_npc_wizzlecranks_shredder | function | — | — | — |
| npc_mission_possible_but_not_probableAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#2 | method | — | — | — |
| SpellHit | method | CreatureAI/DoCastSpellIfCan, Object/GetEntry | — | — |
| GetAI_npc_mission_possible_but_not_probable | function | — | — | — |
| npc_sarilus_foulborneAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#4 | method | shared_Util/urand | — | — |
| JustSummoned | method | SpellCaster/CastSpell#2 | — | — |
| UpdateAI | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, shared_Util/urand, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_npc_sarilus_foulborne | function | — | — | — |
| ProcessEventId_event_the_principle_source | function | Creature.Main/AI, CreatureAI/AttackStart, Object/ToPlayer, WorldObject.Object/SummonCreature#2 | — | — |
| AddSC_the_barrens | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
