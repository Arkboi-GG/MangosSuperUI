<!-- provenance: failed-members -->
# npcs_special

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# npcs_special

**Purpose & Responsibilities**

`npcs_special.cpp` implements AI scripts and gossip handlers for a diverse collection of non-player characters (NPCs) that do not fit into specific zone-based script files. These NPCs are scattered globally or serve unique, isolated mechanics such as timed events, pet summons, quest-specific interactions, and utility functions. The unit handles:

1.  **Quest-Specific Mechanics:** Complex interactions for quests like "Cluck!" (`npc_chicken_cluck`), "Triage" (`npc_doctor`, `npc_injured_patient`), "Curing the Sick" (`npc_sickly_critter`), and the Stranglethorn Vale Fishing Extravaganza (`npc_riggle_bassbait`).
2.  **Event Systems:** Logic for the "Love is in the Air" festival (`npc_kwee_peddlefeet`) and New Year's Eve fireworks (`npc_pats_firework_guy`).
3.  **Pet & Minion AI:** Behavior for various summoned pets, including aggressive minions (`npc_felhound_minion`, `npc_gnomish_battle_chicken`), utility pets (`npc_shahram`, `npc_cannonball_runner`), and explosive traps (`npc_goblin_bomb_dispenser`, `npc_explosive_sheep`).
4.  **Utility & Environmental NPCs:** Target dummies (`npc_target_dummy`), mine traps (`npc_tonk_mine`, `npc_goblin_land_mine`), and resurrection sickness fixers (`npc_res_fixer`).

The unit relies heavily on the core `ScriptedAI`, `CritterAI`, and `ScriptedPetAI` base classes, extending them with specific timers, state flags, and spell-casting logic. It interacts with the `ObjectMgr` for saved variables (persistent state across restarts) and `GameEventMgr` for event activation.

## Member-by-Member Behavior

### Chicken Cluck (Quest 3861)
*   **`npc_chicken_cluckAI`**: Inherits from `CritterAI`. Manages the state of the chicken for the "Cluck!" quest. It starts hostile/faction-chicken. Upon receiving a specific emote (`TEXTEMOTE_CHICKEN`) from a player without the quest, it has a 1/30 chance to become friendly and offer the quest. If the player cheers (`TEXTEMOTE_CHEER`) after completing the quest, it becomes friendly and speaks.
*   **`JustRespawned`**, **`OnCombatStop`**: Call `Reset()` to revert the chicken to its initial hostile state and clear quest-giver flags.
*   **`Reset#3`**: Sets the faction back to `FACTION_CHICKEN` (hostile) and removes the `UNIT_NPC_FLAG_QUESTGIVER` flag. Initializes a 20-second timer.
*   **`ReceiveEmote`**: Handles the emote triggers. Checks player quest status via `Player.Main/GetQuestStatus`. Uses `shared_Util/urand` for the random chance. Sets faction and flags via `Unit.Main/SetFactionTemplateId` and `WorldObject.Object/SetFlag`. Triggers dialogue via `ScriptMgr/DoScriptText`.
*   **`UpdateAI#2`**: Decrements the reset timer. If the chicken is currently flagged as a quest giver, it resets to hostile after 20 seconds to allow the next player to trigger the event. Calls `CritterAI/UpdateAI`.

### Triage Quest (Quests 6622/6624)
This subsystem involves two NPCs: the Doctor and the Patients.

**Doctor (`npc_doctor`)**
*   **`npc_doctorAI`**: Inherits from `ScriptedAI`. Coordinates the triage event. Stores the player's GUID, counts of saved/died patients, and a set of active patient GUIDs.
*   **`Reset#4`**: Clears the player GUID, resets counters, and clears the patient GUID set.
*   **`BeginEvent`**: Triggered by `QuestAccept_npc_doctor`. Records the player starting the quest and resets timers/counters.
*   **`EndEvent`**: Called when the quest succeeds (15 saved) or fails (>5 died). Awards or fails the quest via `Player.Main/GroupEventHappens` or `Player.Main/FailQuest`. Despawns all remaining patients.
*   **`PatientDied`**: Increments death counter. If deaths exceed 5, ends the event with failure.
*   **`PatientSaved`**: Increments save counter. If saves reach 15, ends the event with success. Verifies the saving player matches the quest starter.
*   **`GetPatientSpawnPosition`**: Finds an empty spawn coordinate from the pre-defined `AllianceCoords` or `HordeCoords` lists by checking distance to existing patients.
*   **`UpdateAI#3`**: If a player is nearby, spawns a new patient every 10 seconds until 21 total attempts (worst-case limit). Selects patient entry based on faction (Alliance/Horde) using `shared_Util/urand`. Assigns the doctor's GUID to the patient's AI.
*   **`QuestAccept_npc_doctor`**: Global hook. Starts the event by calling `BeginEvent` on the doctor's AI.
*   **`GetAI_npc_doctor`**: Factory function.

**Patients (`npc_injured_patient`)**
*   **`npc_injured_patientAI`**: Inherits from `ScriptedAI`. Represents the injured soldiers.
*   **`EnterEvadeMode`**: Only allows evade if the patient is already saved (prevents accidental despawn during healing).
*   **`Reset#11`**: Sets initial health based on injury severity (75%, 50%, or 25%). Sets stand state to dead (lying down). Removes selectable flag, adds combat flag (to stop regen).
*   **`SpellHit`**: Triggered when hit by Heal (Spell 20804). If the healer has the correct quest, marks patient as saved, stands up, removes combat flag, plays a sound, and moves to a "safe" coordinate. Notifies the doctor via `PatientSaved`.
*   **`UpdateAI#8`**: If not saved, reduces health by 5% of the time difference every tick. If health drops to near zero, marks as dead, notifies the doctor via `PatientDied`, and sets death state.
*   **`GetAI_npc_injured_patient`**: Factory function.

### Tonk Mines & Mortars
*   **`npc_tonk_mineAI`**: Inherits from `ScriptedAI`.
    *   **`Reset#21`**: Enables Line-of-Sight events. Sets a 3-second arm timer.
    *   **`Aggro#4`**: Empty override to prevent aggro logic.
    *   **`MoveInLineOfSight#4`**: If armed and a Steam Tonk (`NPC_DARKMOON_STEAM_TONK`) is within 2 yards, casts detonation spell and despawns.
    *   **`UpdateAI#17`**: Arms the mine after 3 seconds.
*   **`npc_tonk_mortarAI`**: Inherits from `ScriptedAI`.
    *   **`Reset#22`**: Sets explosion timer to 1.5 seconds.
    *   **`UpdateAI#18`**: After 1.5s, casts explosion spell. After another 5s (total 6.5s), despawns.
*   **`GetAI_npc_tonk_mine`**, **`GetAI_npc_tonk_mortar`**: Factory functions.

### Steam Tonk
*   **`npc_steam_tonkAI`**: Inherits from `ScriptedAI`.
    *   **`Reset#17`**: Sets a 3-second check timer.
    *   **`Aggro`**, **`MoveInLineOfSight#3`**, **`AttackStart#2`**, **`EnterCombat`**: All empty overrides to prevent standard combat behavior.
    *   **`UpdateAI#14`**: Every 3 seconds, checks if the tonk has a charmer (possessor). If not, it deals max damage to itself (suicide). This forces players to possess it quickly.

### Felhound Minion
*   **`npc_felhound_minionAI`**: Inherits from `ScriptedPetAI`.
    *   **`ctor`**: Sets aggressive react state and allows stat modification.
    *   **`Reset#7`**: Sets Mana Burn timer to random 1-2.5s.
    *   **`UpdateAI#5`**: If in combat, casts Mana Burn on mana-using victims every ~10-15s. Calls parent `UpdateAI`.

### Gnomish Battle Chicken
*   **`npc_gnomish_battle_chickenAI`**: Inherits from `ScriptedPetAI`.
    *   **`ctor`**: Sets aggressive react state. Initializes timers for Battle Squawk and Chicken Fury.
    *   **`Reset#8`**: Empty.
    *   **`DamageTaken`**: If fury is ready, casts Chicken Fury (damage reduction/boost?) and resets fury timer.
    *   **`UpdatePetAI#3`**: Casts Battle Squawk (buff) after a random 30-80s delay. Manages fury cooldown (25s).

### Arcanite Dragonling
*   **`npc_arcanite_dragonlingAI`**: Inherits from `ScriptedPetAI`.
    *   **`ctor`**: Sets aggressive react state.
    *   **`Reset`**: Sets Flame Buffet timer to 5s, Flame Breath to random 10-60s.
    *   **`UpdatePetAI`**: Casts Flame Buffet every 22.5s and Flame Breath every 10-60s on victim.

### Emerald Dragon Whelp
*   **`npc_emerald_dragon_whelpAI`**: Inherits from `ScriptedPetAI`.
    *   **`ctor`**: Sets defensive react state.
    *   **`Reset#5`**: Sets Acid Spit timer to 1s.
    *   **`UpdatePetAI#2`**: Casts Acid Spit every 2s on victim.

### Cannonball Runner
*   **`npc_cannonball_runnerAI`**: Inherits from `ScriptedPetAI`.
    *   **`ctor`**: Sets aggressive react state, copies owner orientation, disables combat movement.
    *   **`AttackStart`**, **`MoveInLineOfSight`**, **`Reset#2`**: Empty overrides.
    *   **`UpdateAI`**: If not casting, selects a random unfriendly target within 40 yards and fires Cannon Fire spell.

### The Cleaner
*   **`npc_the_cleanerAI`**: Inherits from `ScriptedAI`.
    *   **`Reset#20`**: Casts Immunity spell. Sets 3s despawn timer.
    *   **`Aggro#3`**: Says aggro line.
    *   **`EnterEvadeMode#3`**: Despawns immediately.
    *   **`UpdateAI#16`**: If threat list is empty after 3s, despawns. Otherwise, performs melee attacks.

### Pat's Firework Guy
*   **`npc_pats_firework_guyAI`**: Inherits from `ScriptedAI`.
    *   **`ctor`**: Initializes state. Calls `IsUsable` to identify firework type.
    *   **`Reset#14`**, **`ResetCreature#3`**: Resets internal flags.
    *   **`IsUsable`**: Identifies the NPC entry against the `Fireworks` array to determine if it's a cluster, large, or lucky rocket.
    *   **`UpdateAI#10`**: Executes the firework animation. Teleports the NPC to specific coordinates relative to its start position and casts the corresponding visual spells. If it's a "Lucky" rocket, schedules a delayed cast of Lunar Fortune. Awards quest credit to the summoner. Triggers `boss_omenAI::OnFireworkLaunch` if near the Omen launcher. Marks itself as done.
    *   **`GetAI_npc_pats_firework_guy`**: Factory function.

### Summon Possessed
*   **`npc_summon_possessedAI`**: Inherits from `ScriptedAI`.
    *   **`Reset#18`**: Empty.
    *   **`JustDied#3`**: When the possessed mob dies, it removes the possession aura from the original owner (player).

### Riggle Bassbait (Fishing Tournament)
*   **`npc_riggle_bassbaitAI`**: Inherits from `ScriptedAI`.
    *   **`ctor`**: Checks saved variables. If more than a day has passed since the last win, resets tournament variables.
    *   **`Reset#15`**: Empty.
    *   **`CheckTournamentState`**: Manages tournament phases. If the event is active and no winner exists, it announces the start (once) and enables quest giver flag. If the event ends, it announces the end (once) and disables quest giver flag. Uses `ObjectMgr/GetSavedVariable` and `SetSavedVariable` for persistence.
    *   **`UpdateAI#11`**: Calls `CheckTournamentState` every second.
    *   **`QuestRewarded_npc_riggle_bassbait`**: Called when the Master Angler quest is turned in. Records the win time, marks a winner as existing, disables quest giver, and announces the winner.
    *   **`GetAI_npc_riggle_bassbait`**: Factory function.

### Target Dummy
*   **`npc_target_dummyAI`**: Inherits from `ScriptedAI`.
    *   **`ctor`**: Sets passive aura and spawn effect based on dummy tier (Basic/Advanced/Master). Disables combat movement.
    *   **`Reset#19`**: Disables combat movement.
    *   **`Aggro#2`**, **`AttackStart#3`**, **`EnterEvadeMode#2`**: Empty overrides to prevent combat engagement.
    *   **`UpdateAI#15`**: Counts down a 15-second timer. When expired, stops combat (if any) and kills itself to leave a corpse.

### Shahram (Pet)
*   **`npc_shahramAI`**: Inherits from `ScriptedPetAI`.
    *   **`ctor`**: Initializes stats for level 63. Sets aggressive react state. Sets despawn timers (10s max, 5s combat).
    *   **`Reset#16`**: Empty.
    *   **`UpdatePetAI#4`**: While in combat, randomly casts debuffs (Curse, Flames, Might) or buffs (Blessing, Fist, Will) on the owner/victim. Stops casting after one of each type.
    *   **`UpdateAI#12`**: Counts down the absolute 10-second despawn timer.
    *   **`DespawnShahram`**: Unsummons the pet.

### Goblin Land Mine
*   **`npc_goblin_land_mineAI`**: Inherits from `ScriptedAI`.
    *   **`ctor`**: Initializes timers (10s arm, 70s despawn, 0.5s detonate).
    *   **`Reset#10`**: Enables LOS events, disables combat movement.
    *   **`MoveInLineOfSight#2`**: If armed and a hostile unit is within 5 yards, triggers detonation sequence.
    *   **`UpdateAI#7`**: Manages three states: Arming (10s), Armed (waiting for trigger or 70s despawn), and Detonation (0.5s delay then explode/despawn). Removes guardian link from owner before despawning.

### Sickly Critter
*   **`npc_sickly_critterAI`**: Inherits from `CritterAI`.
    *   **`ctor`**: Initializes state.
    *   **`JustRespawned#2`**: Resets state.
    *   **`ResetCreature#4`**: Resets hit/modify flags and timer.
    *   **`SpellHit#3`**: If hit with Apply Salve, marks as hit, records player GUID, makes critter flee, and schedules a 10ms despawn (visual trickery).
    *   **`UpdateAI#13`**: If modifying, waits 1.5s, then changes entry/display ID to the cured version, removes sick aura, and rewards the player.

### Goblin Bomb Dispenser & Explosive Sheep
*   **`npc_goblin_bomb_dispenserAI`** and **`npc_explosive_sheepAI`**: Both inherit from `ScriptedPetAI`.
    *   **`ctor`**: Set aggressive react state.
    *   **`Reset#9`/`#6`**: Empty.
    *   **`ResetCreature#2`/`ResetCreature`**: Set alive timer (1 min for bomb, 3 min for sheep) and apply passive aura.
    *   **`JustDied#2`/`JustDied`**: Delay unsummon by 5s.
    *   **`UpdateAI#6`/`#4`**: If alive timer expires, cast explosion spell and mark as exploded.

### Kwee Peddlefeet (Love is in the Air)
*   **`npc_kwee_peddlefeetAI`**: Inherits from `ScriptedAI`.
    *   **`ctor`**: If the main event is inactive, loads saved voting variables to determine the winner. Removes quest giver flag if summoned by a winner event.
    *   **`Reset#12`**: Empty.
    *   **`SetVariables`**: Reads saved variables to determine the winning faction and city.
    *   **`ResetVariablesAndDisableWinnerEvents`**: Clears all voting data and disables winner-specific game events.
    *   **`OnRemoveFromWorld`**: If despawning without quest giver flag (winner event context), resets variables. If in the winning zone, enables the specific winner event for that city.
    *   **`ReceiveEmote#2`**: If kissed, casts Smitten spell on the player.
    *   **`GossipHello_npc_kwee_peddlefeet`**: Sends current vote counts to the player's world state. If event is inactive, shows victory gossip menu.
    *   **`QuestRewarded_npc_kwee_peddlefeet`**: Adds items from the quest turn-in to the respective boss/faction vote counters.
    *   **`GetAI_npc_kwee_peddlefeet`**: Factory function.

### Oozeling Jubjub
*   **`npc_oozeling_jubjubAI`**: Inherits from `ScriptedPetAI`.
    *   **`ctor`**: Initializes timer.
    *   **`Reset#13`**: Resets timer.
    *   **`SpellHit#2`**: If hit with Dark Iron Mug, sets return timer to 10s.
    *   **`MovementInform`**: When reaching point 1, emotes guzzling ale, roots itself, and sets a 3s return timer.
    *   **`UpdateAI#9`**: If rooted, waits 3s, then unroots, deactivates the mug object, and resumes normal AI.

### Res Fixer
*   **`GossipHello_npc_res_fixer`**: Displays a gossip menu option.
*   **`GossipSelect_npc_res_fixer`**: Removes resurrection sickness aura and repairs durability for the player.

### Script Registration
*   **`AddSC_npcs_special`**: Registers all the above scripts with the `ScriptMgr`.

## Cross-Unit Boundaries

*   **`npc_chicken_cluckAI.ReceiveEmote`** calls `Player.Main/GetQuestStatus` to verify quest progress, `ScriptMgr/DoScriptText` for dialogue, `Unit.Main/SetFactionTemplateId` to change hostility, and `WorldObject.Object/SetFlag` to toggle quest-giver capability.
*   **`npc_doctorAI.UpdateAI#3`** calls `Map.Main/GetCreature` to find patients, `Object/GetEntry` to determine faction, `shared_Util/urand` for random patient selection, and `WorldObject.Object/SummonCreature#2` to spawn patients. It accesses `npc_injured_patientAI.m_doctorGuid` (cross-class access via dynamic cast) to link patients to the doctor.
*   **`npc_injured_patientAI.SpellHit`** calls `Player.Main/GetQuestStatus` to validate the healer, `Map.Main/GetCreature` to find the doctor, and `Creature.MotionMaster/MovePoint` to move the healed patient. It calls `npc_doctorAI.PatientSaved` to update the quest state.
*   **`npc_injured_patientAI.UpdateAI#8`** calls `Map.Main/GetCreature` to find the doctor and `npc_doctorAI.PatientDied` if the patient expires.
*   **`npc_pats_firework_guyAI.UpdateAI#10`** calls `boss_omenAI::OnFireworkLaunch` (from `boss_omen.h`) to synchronize with the Omen boss fight if applicable. It uses `Player.Main/KilledMonster` to award quest credit.
*   **`npc_riggle_bassbaitAI.CheckTournamentState`** and **`QuestRewarded_npc_riggle_bassbait`** interact extensively with `ObjectMgr/GetSavedVariable` and `ObjectMgr/SetSavedVariable` to persist tournament state across server restarts. They use `GameEventMgr.Main/IsActiveEvent` to check event status.
*   **`npc_kwee_peddlefeetAI`** methods (`SetVariables`, `ResetVariablesAndDisableWinnerEvents`, `OnRemoveFromWorld`) rely on `ObjectMgr` for saved variables and `GameEventMgr` for enabling/disabling specific sub-events. `GossipHello_npc_kwee_peddlefeet` uses `Player.Main/SendUpdateWorldState` to push vote data to the client.
*   **`npc_shahramAI.DespawnShahram`** calls `Pet.Main/Unsummon`.
*   **`npc_goblin_land_mineAI.UpdateAI#7`** calls `Unit.Main/RemoveGuardian` to clean up the pet link before despawning.

## Data Model

This unit does not directly query or modify standard database tables via SQL. Instead, it uses the **Saved Variables** system (`ObjectMgr/GetSavedVariable`, `ObjectMgr/SetSavedVariable`) to persist state for:
1.  **Fishing Tournament (`npc_riggle_bassbait`):** Tracks whether the event has started, if pools have despawned, if a winner exists, and the timestamp of the last win.
2.  **Love is in the Air (`npc_kwee_peddlefeet`):** Tracks vote counts for each leader (Thrall, Bolvar, etc.) and faction totals.

These variables are stored in the `character_db` or `world_db` depending on the engine configuration, typically in a table like `saved_variables` or similar, but the code interacts with them abstractly through the `ObjectMgr` interface. No direct SQL schemas are referenced in this unit.

## Notable Implementation Details

1.  **Triage Health Decay:** `npc_injured_patientAI.UpdateAI#8` reduces health by `0.05f * uiDiff`. This is a continuous decay, not a discrete tick. If the time difference (`uiDiff`) is large (e.g., due to lag or server pause), the patient might lose significant health instantly. The check `m_creature->GetHealth() > 1 + uiHPLose` prevents negative health but allows sudden death.
2.  **Chicken Cluck Randomness:** `npc_chicken_cluckAI.ReceiveEmote` uses `urand(0, 29)` to give a 1/30 chance of triggering the quest. This means players may need to emote multiple times.
3.  **Firework Animation:** `npc_pats_firework_guyAI.UpdateAI#10` manually teleports the NPC to different Z-coordinates and casts spells sequentially to simulate a firework exploding. This is a visual hack rather than a true projectile simulation.
4.  **Steam Tonk Suicide:** `npc_steam_tonkAI.UpdateAI#14` kills the tonk if it lacks a charmer. This is a fail-safe to prevent unpossessed tonks from wandering or being attacked indefinitely.
5.  **Shahram Casting Logic:** `npc_shahramAI.UpdatePetAI#4` ensures Shahram casts one debuff and one buff during his short lifespan. The logic checks `hasCastDebuff` and `hasCastBuff` flags to ensure variety.
6.  **Kwee Peddlefeet Persistence:** The voting system relies on saved variables. If the server restarts during the event, votes are preserved. The `OnRemoveFromWorld` handler ensures that if the NPC despawns (e.g., event ends), the winner event is triggered in the correct city based on the highest vote count.
7.  **Target Dummy Corpse:** `npc_target_dummyAI.UpdateAI#15` explicitly calls `DoKillUnit` to ensure a corpse remains after expiration, allowing players to loot or interact with it if needed.

## Member Reference

*   **`npc_chicken_cluckAI`**: Constructor for the chicken AI, inheriting from `CritterAI`.
*   **`JustRespawned`**: Resets chicken state upon respawn.
*   **`OnCombatStop`**: Resets chicken state when combat ends.
*   **`Reset#3`**: Reverts chicken to hostile faction and removes quest-giver flag.
*   **`ReceiveEmote`**: Handles player emotes to trigger quest start or completion dialogue.
*   **`UpdateAI#2`**: Manages the 20-second timer to reset the chicken's state.
*   **`GetAI_npc_chicken_cluck`**: Factory function to create `npc_chicken_cluckAI`.
*   **`npc_doctorAI`**: Constructor for the doctor AI, inheriting from `ScriptedAI`.
*   **`Reset#4`**: Clears doctor's event state (player GUID, counters).
*   **`npc_injured_patientAI`**: Constructor for the patient AI, inheriting from `ScriptedAI`.
*   **`EnterEvadeMode`**: Prevents evade unless patient is saved.
*   **`Reset#11`**: Sets initial health and flags for the patient.
*   **`SpellHit`**: Handles healing spells, marking patient as saved and notifying doctor.
*   **`UpdateAI#8`**: Decays patient health over time; notifies doctor if patient dies.
*   **`GetAI_npc_injured_patient`**: Factory function to create `npc_injured_patientAI`.
*   **`BeginEvent`**: Starts the triage event for a player.
*   **`EndEvent`**: Concludes the triage event, awarding

---

<!-- machine-true, projected from graph.json -->

## Map — npcs_special

*Source:* npcs_special.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| npc_chicken_cluckAI | ctor | CritterAI/CritterAI | — | — |
| JustRespawned | method | CreatureAI/JustRespawned | — | — |
| OnCombatStop | method | CreatureAI/OnCombatStop | — | — |
| Reset#3 | method | Unit.Main/SetFactionTemplateId, WorldObject.Object/RemoveFlag | — | — |
| ReceiveEmote | method | Player.Main/GetQuestStatus, ScriptMgr/DoScriptText, shared_Util/urand, Unit.Main/SetFactionTemplateId, WorldObject.Object/SetFlag | — | — |
| UpdateAI#2 | method | CritterAI/UpdateAI, Object/HasFlag | — | — |
| GetAI_npc_chicken_cluck | function | — | — | — |
| npc_doctorAI | ctor | Object/GetEntry, ScriptedAI/ScriptedAI | — | — |
| Reset#4 | method | ObjectGuid/Clear | — | — |
| npc_injured_patientAI | ctor | ScriptedAI/ScriptedAI | — | — |
| EnterEvadeMode | method | ScriptedAI/EnterEvadeMode | — | — |
| Reset#11 | method | Object/GetEntry, ObjectGuid/Clear, Unit.Main/GetMaxHealth, Unit.Main/SetHealth, Unit.Main/SetStandState, WorldObject.Object/RemoveFlag, WorldObject.Object/SetFlag | — | — |
| SpellHit | method | Creature.Main/AI, Creature.MotionMaster/MovePoint, Map.Main/GetCreature, Object/GetEntry, Object/GetTypeId, Player.Main/GetQuestStatus, ScriptMgr/DoScriptText, shared_Util/urand, Unit.Main/GetMotionMaster, Unit.Main/IsAlive, Unit.Main/SetStandState, Unit.Main/SetWalk, WorldObject.Object/GetMap, WorldObject.Object/RemoveFlag, WorldObject.Object/SetFlag | — | — |
| UpdateAI#8 | method | Creature.Main/AI, Creature.Main/SetDeathState, Map.Main/GetCreature, Unit.Main/GetHealth, Unit.Main/IsAlive, Unit.Main/SetHealth, WorldObject.Object/GetMap, WorldObject.Object/RemoveFlag, WorldObject.Object/SetFlag | — | — |
| GetAI_npc_injured_patient | function | — | — | — |
| BeginEvent | method | Object/GetObjectGuid | — | — |
| EndEvent | method | Creature.Main/DespawnOrUnsummon, Map.Main/GetCreature, Map.Main/GetPlayer, Player.Main/FailQuest, Player.Main/GetQuestStatus, Player.Main/GroupEventHappens, WorldObject.Object/GetMap | — | — |
| PatientDied | method | Object/GetObjectGuid | — | — |
| PatientSaved | method | Object/GetObjectGuid, ObjectGuid/operator!= | — | — |
| GetPatientSpawnPosition | method | Map.Main/GetCreature, WorldObject.Object/GetDistance3dToCenter, WorldObject.Object/GetMap | — | — |
| UpdateAI#3 | method | BasicAI/UpdateAI, Creature.Main/AI, Log.Main/Out, Map.Main/GetPlayer, Object/GetEntry, Object/GetObjectGuid, ObjectGuid/IsEmpty, shared_Util/urand, WorldObject.Object/GetMap, WorldObject.Object/IsWithinDist, WorldObject.Object/SetFlag, WorldObject.Object/SummonCreature#2 | — | — |
| QuestAccept_npc_doctor | function | Creature.Main/AI, QuestDef/GetQuestId | — | — |
| GetAI_npc_doctor | function | — | — | — |
| npc_tonk_mineAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#21 | method | Creature.Main/EnableMoveInLosEvent | — | — |
| Aggro#4 | method | — | — | — |
| MoveInLineOfSight#4 | method | Creature.Main/ForcedDespawn, Object/GetEntry, SpellCaster/CastSpell#2, WorldObject.Object/GetDistance#3 | — | — |
| UpdateAI#17 | method | — | — | — |
| GetAI_npc_tonk_mine | function | — | — | — |
| npc_tonk_mortarAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#22 | method | — | — | — |
| UpdateAI#18 | method | Creature.Main/ForcedDespawn, SpellCaster/CastSpell#2 | — | — |
| GetAI_npc_tonk_mortar | function | — | — | — |
| npc_steam_tonkAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#17 | method | — | — | — |
| Aggro | method | — | — | — |
| MoveInLineOfSight#3 | method | — | — | — |
| AttackStart#2 | method | — | — | — |
| EnterCombat | method | — | — | — |
| UpdateAI#14 | method | Unit.Main/DealDamage, Unit.Main/GetCharmer, Unit.Main/GetMaxHealth | — | — |
| GetAI_npc_steam_tonk | function | — | — | — |
| npc_felhound_minionAI | ctor | CharmInfo/SetReactState, ScriptedPetAI/ScriptedPetAI, Unit.Main/GetCharmInfo, Unit.Main/SetCanModifyStats | — | — |
| Reset#7 | method | shared_Util/urand | — | — |
| UpdateAI#5 | method | CreatureAI/DoCastSpellIfCan, ScriptedPetAI/UpdateAI, shared_Util/urand, Unit.Main/GetPowerType, Unit.Main/GetVictim | — | — |
| GetAI_npc_felhound_minion | function | — | — | — |
| npc_gnomish_battle_chickenAI | ctor | CharmInfo/SetReactState, ScriptedPetAI/ScriptedPetAI, shared_Util/urand, Unit.Main/GetCharmInfo, Unit.Main/SetCanModifyStats | — | — |
| Reset#8 | method | — | — | — |
| DamageTaken | method | CreatureAI/DamageTaken, CreatureAI/DoCastSpellIfCan | — | — |
| UpdatePetAI#3 | method | CreatureAI/DoCastSpellIfCan, ScriptedPetAI/UpdatePetAI | — | — |
| GetAI_npc_gnomish_battle_chicken | function | — | — | — |
| npc_arcanite_dragonlingAI | ctor | CharmInfo/SetReactState, ScriptedPetAI/ScriptedPetAI, Unit.Main/GetCharmInfo, Unit.Main/SetCanModifyStats | — | — |
| Reset | method | shared_Util/urand | — | — |
| UpdatePetAI | method | CreatureAI/DoCastSpellIfCan, ScriptedPetAI/UpdatePetAI, shared_Util/urand, Unit.Main/GetVictim | — | — |
| GetAI_npc_arcanite_dragonling | function | — | — | — |
| npc_emerald_dragon_whelpAI | ctor | CharmInfo/SetReactState, ScriptedPetAI/ScriptedPetAI, Unit.Main/GetCharmInfo, Unit.Main/SetCanModifyStats | — | — |
| Reset#5 | method | — | — | — |
| UpdatePetAI#2 | method | CreatureAI/DoCastSpellIfCan, ScriptedPetAI/UpdatePetAI, Unit.Main/GetVictim | — | — |
| GetAI_npc_emerald_dragon_whelp | function | — | — | — |
| npc_cannonball_runnerAI | ctor | CharmInfo/SetReactState, CreatureAI/SetCombatMovement, ScriptedPetAI/ScriptedPetAI, Unit.Main/GetCharmInfo, Unit.Main/GetOwner, Unit.Main/SetCanModifyStats, WorldObject.Object/GetOrientation, WorldObject.Object/SetOrientation | — | — |
| AttackStart | method | — | — | — |
| MoveInLineOfSight | method | — | — | — |
| Reset#2 | method | — | — | — |
| UpdateAI | method | SpellCaster/CastSpell#2, SpellCaster/IsNonMeleeSpellCasted, Unit.Main/SelectRandomUnfriendlyTarget | — | — |
| GetAI_npc_cannonball_runner | function | — | — | — |
| npc_the_cleanerAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#20 | method | CreatureAI/DoCastSpellIfCan | — | — |
| Aggro#3 | method | ScriptMgr/DoScriptText | — | — |
| EnterEvadeMode#3 | method | Creature.Main/ForcedDespawn, ScriptedAI/EnterEvadeMode | — | — |
| UpdateAI#16 | method | Creature.Main/ForcedDespawn, CreatureAI/DoMeleeAttackIfReady, ThreatManager/getThreatList, Unit.Main/GetThreatManager, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_npc_the_cleaner | function | — | — | — |
| npc_pats_firework_guyAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#14 | method | — | — | — |
| ResetCreature#3 | method | — | — | — |
| IsUsable | method | Object/GetEntry | — | — |
| UpdateAI#10 | method | boss_omen/OnFireworkLaunch, Creature.Main/IsTemporarySummon, GridSearchers/GetClosestGameObjectWithEntry, Map.Main/GetPlayer, ObjectGuid/ObjectGuid, ObjectMgr/GetCreatureTemplate, Player.Main/KilledMonster, SpellCaster/CastSpell#2, TemporarySummon/GetSummonerGuid, Unit.Main/NearTeleportTo, WorldObject.Object/GetMap, WorldObject.Object/GetPosition#2 | — | — |
| GetAI_npc_pats_firework_guy | function | — | — | — |
| npc_summon_possessedAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#18 | method | — | — | — |
| JustDied#3 | method | CreatureAI/JustDied, Object/GetUInt32Value, Object/ToPlayer, Unit.Main/GetCharmer, Unit.Main/RemoveAurasDueToSpell | — | — |
| GetAI_npc_summon_possessed | function | — | — | — |
| npc_riggle_bassbaitAI | ctor | ObjectMgr/GetSavedVariable, ObjectMgr/SetSavedVariable, ScriptedAI/ScriptedAI | — | — |
| Reset#15 | method | — | — | — |
| CheckTournamentState | method | GameEventMgr.Main/IsActiveEvent, ObjectMgr/GetSavedVariable, ObjectMgr/SetSavedVariable, Unit.Main/IsQuestGiver, WorldObject.Object/MonsterYellToZone, WorldObject.Object/RemoveFlag, WorldObject.Object/SetFlag | — | — |
| UpdateAI#11 | method | BasicAI/UpdateAI | — | — |
| GetAI_npc_riggle_bassbait | function | — | — | — |
| QuestRewarded_npc_riggle_bassbait | function | ObjectMgr/SetSavedVariable, QuestDef/GetQuestId, WorldObject.Object/MonsterYellToZone, WorldObject.Object/RemoveFlag | — | — |
| npc_target_dummyAI | ctor | Creature.MotionMaster/MoveIdle, CreatureAI/SetCombatMovement, MotionMaster/Clear, Object/GetEntry, ScriptedAI/ScriptedAI, SpellCaster/CastSpell#2, Unit.Main/AddAura, Unit.Main/GetMotionMaster | — | — |
| Reset#19 | method | CreatureAI/SetCombatMovement | — | — |
| Aggro#2 | method | — | — | — |
| AttackStart#3 | method | — | — | — |
| EnterEvadeMode#2 | method | — | — | — |
| UpdateAI#15 | method | Unit.Main/CombatStop, Unit.Main/DoKillUnit | — | — |
| GetAI_npc_target_dummy | function | — | — | — |
| npc_shahramAI | ctor | CharmInfo/SetReactState, Object/ToPet, Pet.Main/InitStatsForLevel, ScriptedPetAI/ScriptedPetAI, Unit.Main/GetCharmInfo, Unit.Main/SetCanModifyStats, WorldObject.Object/SetFlag | — | — |
| Reset#16 | method | — | — | — |
| UpdatePetAI#4 | method | CreatureAI/DoCastSpellIfCan, ScriptedPetAI/UpdatePetAI, shared_Util/urand, Unit.Main/GetVictim, WorldObject.Object/IsInRange | — | — |
| UpdateAI#12 | method | ScriptedPetAI/UpdateAI | — | — |
| DespawnShahram | method | Object/ToPet, Pet.Main/Unsummon | — | — |
| GetAI_npc_shahram | function | — | — | — |
| npc_goblin_land_mineAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#10 | method | Creature.Main/EnableMoveInLosEvent, CreatureAI/SetCombatMovement | — | — |
| MoveInLineOfSight#2 | method | Unit.Main/IsHostileTo, WorldObject.Object/GetDistance#3 | — | — |
| UpdateAI#7 | method | Creature.Main/RemoveFromWorld, Object/ToPet, ScriptedAI/DoStartNoMovement, SpellCaster/CastSpell#2, Unit.Main/GetOwner, Unit.Main/GetVictim, Unit.Main/RemoveGuardian | — | — |
| GetAI_npc_goblin_land_mine | function | — | — | — |
| npc_sickly_critterAI | ctor | CritterAI/CritterAI | — | — |
| JustRespawned#2 | method | CreatureAI/JustRespawned | — | — |
| ResetCreature#4 | method | — | — | — |
| SpellHit#3 | method | Creature.Main/ForcedDespawn, Creature.MotionMaster/MoveFleeing, CritterAI/SpellHit, MotionMaster/Clear, Object/GetEntry, Object/GetObjectGuid, Object/ToPlayer, Player.Main/GetTeam, Unit.Main/GetMotionMaster | — | — |
| UpdateAI#13 | method | CritterAI/UpdateAI, Map.Main/GetPlayer, Object/SetEntry, Player.Main/RewardPlayerAndGroupAtCast, Unit.Main/RemoveAurasDueToSpell, Unit.Main/SetDisplayId, WorldObject.Object/GetMap | — | — |
| GetAI_npc_sickly_critter | function | — | — | — |
| npc_goblin_bomb_dispenserAI | ctor | CharmInfo/SetReactState, ScriptedPetAI/ScriptedPetAI, Unit.Main/GetCharmInfo, Unit.Main/SetCanModifyStats | — | — |
| Reset#9 | method | — | — | — |
| ResetCreature#2 | method | SpellCaster/CastSpell#2 | — | — |
| JustDied#2 | method | Object/ToPet, Pet.Main/DelayedUnsummon | — | — |
| UpdateAI#6 | method | ScriptedPetAI/UpdateAI, SpellCaster/CastSpell#2 | — | — |
| GetAI_npc_goblin_bomb_dispenser | function | — | — | — |
| npc_explosive_sheepAI | ctor | CharmInfo/SetReactState, ScriptedPetAI/ScriptedPetAI, Unit.Main/GetCharmInfo, Unit.Main/SetCanModifyStats | — | — |
| Reset#6 | method | — | — | — |
| ResetCreature | method | SpellCaster/CastSpell#2 | — | — |
| JustDied | method | Object/ToPet, Pet.Main/DelayedUnsummon | — | — |
| UpdateAI#4 | method | ScriptedPetAI/UpdateAI, SpellCaster/CastSpell#2 | — | — |
| GetAI_npc_explosive_sheep | function | — | — | — |
| npc_kwee_peddlefeetAI | ctor | GameEventMgr.Main/IsActiveEvent, ScriptedAI/ScriptedAI, WorldObject.Object/RemoveFlag | — | — |
| Reset#12 | method | — | — | — |
| SetVariables | method | ObjectMgr/GetSavedVariable | — | — |
| ResetVariablesAndDisableWinnerEvents | method | GameEventMgr.Main/EnableEvent, ObjectMgr/GetSavedVariable, ObjectMgr/SetSavedVariable | — | — |
| OnRemoveFromWorld | method | GameEventMgr.Main/EnableEvent, Object/HasFlag, WorldObject.Object/GetZoneId | — | — |
| ReceiveEmote#2 | method | SpellCaster/CastSpell#2 | — | — |
| GetAI_npc_kwee_peddlefeet | function | — | — | — |
| GossipHello_npc_kwee_peddlefeet | function | Creature.Main/AI, GameEventMgr.Main/IsActiveEvent, GossipDef/SendGossipMenu, Object/GetObjectGuid, ObjectMgr/GetSavedVariable, Player.Main/SendUpdateWorldState | — | — |
| QuestRewarded_npc_kwee_peddlefeet | function | ObjectMgr/GetSavedVariable, ObjectMgr/SetSavedVariable, QuestDef/GetQuestId, WorldObject.Object/GetZoneId | — | — |
| npc_oozeling_jubjubAI | ctor | ScriptedPetAI/ScriptedPetAI | — | — |
| Reset#13 | method | — | — | — |
| SpellHit#2 | method | — | — | — |
| MovementInform | method | Unit.Main/AddUnitState, WorldObject.Object/MonsterTextEmote#2 | — | — |
| UpdateAI#9 | method | GameObject/SetLootState, ScriptedPetAI/UpdateAI, Unit.Main/ClearUnitState, WorldObject.Object/AddObjectToRemoveList, WorldObject.Object/FindNearestGameObject | — | — |
| GetAI_npc_oozeling_jubjub | function | — | — | — |
| GossipHello_npc_res_fixer | function | GossipDef/AddMenuItem#5, GossipDef/SendGossipMenu, Object/GetObjectGuid, PlayerMenu/GetGossipMenu | — | — |
| GossipSelect_npc_res_fixer | function | GossipDef/CloseGossip, Player.Main/DurabilityRepairAll, Unit.Main/GetRace, Unit.Main/RemoveAurasDueToSpell | — | — |
| AddSC_npcs_special | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |

---

<!-- verify: failed-members | missing: GetAI_npc_arcanite_dragonling, GetAI_npc_cannonball_runner, GetAI_npc_emerald_dragon_whelp, GetAI_npc_explosive_sheep, GetAI_npc_felhound_minion, GetAI_npc_gnomish_battle_chicken, GetAI_npc_goblin_bomb_dispenser, GetAI_npc_goblin_land_mine, GetAI_npc_oozeling_jubjub, GetAI_npc_shahram, GetAI_npc_sickly_critter, GetAI_npc_steam_tonk, GetAI_npc_summon_possessed, GetAI_npc_target_dummy, GetAI_npc_the_cleaner, Reset#6, UpdateAI#4 -->
