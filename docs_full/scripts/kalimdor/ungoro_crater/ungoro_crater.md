# ungoro_crater

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ungoro_crater

**Purpose & Responsibilities**
`ungoro_crater.cpp` implements scripted behaviors for several distinct questlines and encounters located in the Un'Goro Crater zone. It contains AI classes for escort quests (`npc_ame01`, `npc_ringo`), area trigger logic for summoning creatures (`at_scent_larkorwi`), simple movement-based mechanics (`mob_captured_felwood_ooze`), and a complex multi-stage boss encounter involving transformation and threat management (`npc_simone_the_inconspicuous`, `npc_simone_seductress`, `npc_precious_the_devourer`). The unit also registers a custom spell script modifier for `Simone the Seductress`'s Chain Lightning ability.

This unit does not interact with any database tables directly; all logic is driven by in-memory creature states, quest statuses, and game world events.

## Feature Breakdown

### 1. Quest: Chasing Ame (NPC: `npc_ame01`)
This section handles the escort quest **Chasing Ame** (Quest ID 4245).

*   **`npc_ame01AI`**: Inherits from `ScriptedEscortAI`.
    *   **`Reset`**: No specific reset logic; relies on base class.
    *   **`JustRespawned`**: Sets the creature immune to NPCs (`UNIT_FLAG_IMMUNE_TO_NPC`) to prevent aggro from non-player entities during respawn/setup, then calls the base `JustRespawned`.
    *   **`WaypointReached`**: Triggers dialogue based on waypoint progress.
        *   Waypoint 0: Starts the escort dialogue (`SAY_AME_START`).
        *   Waypoint 19: Mid-escort dialogue (`SAY_AME_PROGRESS`).
        *   Waypoint 37: Ends the escort (`SAY_AME_END`) and triggers the quest completion event for the player via `Player.Main/GroupEventHappens`.
    *   **`Aggro`**: If aggroed by a non-player, checks if the escort player is fighting the same target. If not, plays a random aggro sound. Ignores aggro from players entirely (likely handled by escort mechanics).
*   **`QuestAccept_npc_ame01`**: Triggered when a player accepts the quest.
    *   Validates the quest ID.
    *   Casts the creature's AI to `npc_ame01AI`.
    *   Sets the creature to standing, sets temporary faction to neutral passive, removes NPC immunity, and starts the escort path using `ScriptedEscortAI/Start`.
*   **`GetAI_npc_ame01`**: Factory function returning a new `npc_ame01AI` instance.

### 2. Quest: A Little Help (NPC: `npc_ringo`)
This section handles the follow quest **A Little Help** (Quest ID 4491), involving a pet-like follower (`npc_ringo`) and an interaction with another NPC (`NPC_SPRAGGLE`, entry 9997).

*   **`npc_ringoAI`**: Inherits from `ScriptedFollowerAI`.
    *   **Members**:
        *   `m_uiFaintTimer`: Timer for random fainting events.
        *   `m_uiEndEventProgress`: State machine counter for the end-of-quest cutscene.
        *   `m_uiEndEventTimer`: Timer for cutscene steps.
        *   `pSpraggle`: Pointer to the Spraggle NPC involved in the finale.
    *   **`Reset`**: Initializes timers, clears `pSpraggle`, and enables `MoveInLineOfSight` events.
    *   **`JustRespawned`**: Sets NPC immunity and calls base `JustRespawned`.
    *   **`MoveInLineOfSight`**:
        *   Calls base `MoveInLineOfSight`.
        *   Checks if the unit seen is `NPC_SPRAGGLE`.
        *   If close enough (`INTERACTION_DISTANCE`) and the player has the quest incomplete, triggers `Player.Main/GroupEventHappens` to complete the quest step.
        *   Stores `pSpraggle` and marks the follow as complete (`SetFollowComplete(true)`).
    *   **`SpellHit`**: If hit by `SPELL_REVIVE_RINGO` (15591) while following or paused, calls `ClearFaint`.
    *   **`SetFaint`**: Pauses the follow, plays a random faint sound, and sets the stand state to sleep.
    *   **`ClearFaint`**: Sets stand state to standing, plays a random wake-up sound, and resumes the follow.
    *   **`UpdateFollowerAI`**:
        *   **Post-Event Cutscene**: If in `STATE_FOLLOW_POSTEVENT`, runs a state machine (`m_uiEndEventProgress`) that triggers dialogue between Ringo and Spraggle, including a faint/wake sequence, before finally completing the follow.
        *   **In-Progress Fainting**: If following and not paused, checks `m_uiFaintTimer`. If expired, calls `SetFaint` and resets the timer to a random interval (60–120 seconds).
        *   **Combat**: If in combat, performs melee attacks.
*   **`QuestAccept_npc_ringo`**: Triggered on quest accept.
    *   Validates quest ID.
    *   Casts AI to `npc_ringoAI`.
    *   Sets stand state, removes NPC immunity, and starts the follow using `ScriptedFollowerAI/StartFollow`.
*   **`GetAI_npc_ringo`**: Factory function returning a new `npc_ringoAI` instance.

### 3. Area Trigger: Scent of Larkorwi (`at_scent_larkorwi`)
Handles the area trigger for quest **Scent of Larkorwi** (Quest ID 4291).

*   **`AreaTrigger_at_scent_larkorwi`**:
    *   Checks if the player is alive, not a GM, and has the quest incomplete.
    *   Uses `GridSearchers/GetClosestCreatureWithEntry` to check if `NPC_LARKORWI_MATE` (9683) already exists nearby (25 yards).
    *   If not present, uses a static `cooldown` map keyed by the area trigger ID to ensure the creature is only summoned once per minute.
    *   Summons `NPC_LARKORWI_MATE` at the trigger coordinates for 2 minutes.

### 4. Mob: Captured Felwood Ooze (`mob_captured_felwood_ooze`)
A simple mob that seeks out a `Primal Ooze` to merge with.

*   **`mob_captured_felwood_oozeAI`**: Inherits from `ScriptedAI`.
    *   **Members**:
        *   `initialTimer`: Initial delay before searching.
        *   `mergeDone`: Flag to prevent re-casting the merge spell.
    *   **`Reset`**: Initializes timer and flag.
    *   **`UpdateAI`**: After a 1-second delay, searches for `NPC_PRIMAL_OOZE` (6557) within 30 yards.
        *   If found, moves to follow it.
        *   If not found, despawns immediately.
    *   **`MovementInform`**: Triggered when movement updates. If following and merge hasn't happened, checks proximity (5 yards). If close, casts `SPELL_MERGING_OOZES` (16032) on the Primal Ooze and sets `mergeDone` to true.
*   **`GetAI_mob_captured_felwood_ooze`**: Factory function.

### 5. Boss Encounter: Simone the Inconspicuous / Simone the Seductress
This is a complex encounter involving two forms of Simone and her companion Precious.

#### Part A: `npc_precious_the_devourer` (Entry 14538)
The "Devourer" form of Precious, spawned during the Simone Seductress phase.

*   **`npc_precious_the_devourerAI`**: Inherits from `ScriptedAI`.
    *   **Members**:
        *   `m_simoneGuid`: GUID of the associated Simone the Seductress.
        *   `m_uiSplitCheck_Timer`: Timer to check if Simone is still in combat.
    *   **`Reset`**: Sets visibility to ON, initializes timer.
    *   **`Aggro`**: If Simone is alive, forces her to attack the same target (`AttackStart`).
    *   **`EnterEvadeMode`**: If Simone is dead, forces this creature to despawn. Otherwise, calls base evade.
    *   **`DamageTaken`**: Extends Simone's leash time to keep her in range.
    *   **`UpdateAI`**:
        *   Checks `m_uiSplitCheck_Timer`. If Simone is *not* in combat, forces her to evade (despawn/reset). This ensures Precious doesn't fight alone if Simone dies or resets.
        *   Performs melee attacks if in combat.
*   **`GetAI_npc_precious_the_devourer`**: Factory function.

#### Part B: `npc_simone_seductress` (Entry 14533)
The demon form of Simone.

*   **`npc_simone_seductressAI`**: Inherits from `ScriptedAI`.
    *   **Members**:
        *   `m_hunterGuid`: Tracks the hunter who engaged her (for quest logic, though currently commented out).
        *   `m_simoneGuid`: GUID of the original "Inconspicuous" Simone (used for respawn logic).
        *   `m_preciousGuid`: GUID of the associated Precious the Devourer.
        *   Timers for abilities (`TemptressKiss`, `LightingBolt`, `ThreatCheck`, `SplitCheck`, `Despawn`).
    *   **`Reset`**: Initializes timers and visibility.
    *   **`JustReachedHome`**: Spawns or respawns `NPC_PRECIOUS_THE_DEVOURER` at Simone's location. Links the GUIDs between Simone and Precious AIs.
    *   **`Aggro`**:
        *   Forces Precious to attack the same target.
        *   Checks if the attacker is a Hunter. If not, or if it's a different hunter than previously tracked, calls `DemonDespawn`. This implies the encounter is designed specifically for a Hunter questline.
    *   **`JustDied`**: Calculates a respawn delay for the original Simone based on server population (Blizzlike scaling) and saves it.
    *   **`DemonDespawn`**:
        *   Sets respawn times for Simone.
        *   If `triggered` is true, summons `NPC_THE_CLEANER` (14503).
        *   Transfers threat from Simone and Precious to The Cleaner, forcing The Cleaner to attack all current targets.
        *   Despawns Precious and Simone.
    *   **`DamageTaken`**: Extends Precious's leash time.
    *   **`SpellHit`**: If hit by `Viper Sting` (14280), casts `SPELL_SILENCE` (23207) on herself and emotes silence.
    *   **`UpdateAI`**:
        *   **Despawn Check**: If out of combat and timer expires, despawns silently.
        *   **Split Check**: If Precious is not in combat, forces her to evade.
        *   **Threat Check**: If Simone has more than 1 target on her threat list, OR Precious has more than 1 target, calls `DemonDespawn`. This enforces a strict single-target mechanic for the hunter.
        *   **Abilities**: Casts `Temptress Kiss` (23205) and `Chain Lightning` (23206) on victims.
        *   **Melee**: Attacks if ready.
*   **`GetAI_npc_simone_seductress`**: Factory function.

#### Part C: `npc_simone_the_inconspicuous` (Entry 14527)
The initial human/disguised form of Simone.

*   **`npc_simone_the_inconspicuousAI`**: Inherits from `ScriptedAI`.
    *   **Members**:
        *   Timers for `FoolsPlight`, `Transform`, `TransformEmote`.
        *   `m_bTransform`: Flag indicating transformation is in progress.
        *   `m_playerGuid`: GUID of the player initiating the event.
        *   `pPrecious`: Pointer to the companion Precious.
    *   **`Reset`**:
        *   Sets long respawn delay (35 mins).
        *   Sets gossip flag.
        *   Finds or summons `NPC_PRECIOUS` (14528) and makes it follow Simone.
    *   **`Transform`**:
        *   Summons `NPC_SIMONE_THE_SEDUCTRESS` at current location.
        *   Hides and despawns the Inconspicuous form.
        *   Summons `NPC_PRECIOUS_THE_DEVOURER` replacing the old Precious.
        *   Links GUIDs between the new Simone Seductress and Precious Devourer AIs.
    *   **`BeginEvent`**: Called from gossip. Sets player GUID, stops movement, removes gossip flag, and starts transformation timers.
    *   **`UpdateAI`**:
        *   If transforming, plays shout emote after 5 seconds, then calls `Transform()` after 10 seconds.
        *   If in combat, casts `Fool's Plight` (23504) and performs melee attacks.
*   **`GossipHello_npc_simone_the_inconspicuous`**: Adds a gossip item if the player has quest **Stave of the Ancients** (7636) incomplete.
*   **`GossipSelect_npc_simone_the_inconspicuous`**: Closes gossip, calls `BeginEvent` on the AI, and plays a laugh emote.
*   **`GetAI_npc_simone_the_inconspicuous`**: Factory function.

### 6. Spell Script: Simone Seductress Chain Lightning
Modifies the behavior of Chain Lightning (Spell ID 23206).

*   **`SimoneSeductressChainLightningScript`**: Inherits from `SpellScript`.
    *   **`OnEffectExecute`**: If the target has Aura 20190 (**Aspect of the Wild**), reduces the spell damage by 75% (multiplies by 0.25).
*   **`GetScript_SimoneSeductressChainLightning`**: Factory function returning the script.

### 7. Registration
*   **`AddSC_ungoro_crater`**: Registers all scripts defined in this file with the `ScriptMgr`. It maps names like `"npc_ame01"` to their respective AI getters, quest accept handlers, gossip handlers, and spell scripts.

## Cross-Unit Boundaries

*   **`ScriptedEscortAI` / `ScriptedFollowerAI`**: Used by `npc_ame01AI` and `npc_ringoAI` respectively for pathfinding and follow mechanics.
*   **`Player.Main`**: Used to check quest status, trigger group events, and retrieve player GUIDs.
*   **`ScriptMgr`**: Used to play scripted text (`DoScriptText`).
*   **`WorldObject.Object`**: Used for flags, positions, angles, and summoning creatures.
*   **`Unit.Main`**: Used for combat states, victim checking, threat management, and stand states.
*   **`Creature.Main`**: Used for AI access, faction setting, and despawning.
*   **`Map.Main`**: Used to retrieve other creatures by GUID for linking AI states.
*   **`GridSearchers`**: Used to find nearby creatures for area triggers and resets.
*   **`shared_Util`**: Used for random number generation (`urand`).
*   **`ObjectGuid`**: Used for storing and comparing entity identifiers.
*   **`ThreatManager`**: Used to inspect and manipulate threat lists for the Simone encounter.
*   **`Spell.Main`**: Used in the spell script to access target and damage data.

## Data Model
This unit does not query or modify any database tables. All state is maintained in memory via creature objects, player quest states, and static local variables (e.g., the cooldown map in `AreaTrigger_at_scent_larkorwi`).

## Notable Implementation Details

1.  **Simone Encounter Threat Logic**: The `npc_simone_seductressAI` strictly enforces a single-target mechanic. In `UpdateAI`, if `m_creature->GetThreatManager().getThreatList().size() > 1`, it immediately calls `DemonDespawn()`. Similarly, it checks Precious's threat list. This suggests the encounter is intended for a Hunter who can maintain single-target focus, possibly using pets or specific abilities to manage adds, though the "Hunter" check in `Aggro` is partially commented out.
2.  **Simone Despawn Mechanic**: When `DemonDespawn` is triggered, it summons `NPC_THE_CLEANER` and transfers all threat from Simone and Precious to this new mob. This effectively ends the Simone/Precious phase and hands off the fight to a cleanup mob.
3.  **Precious-Simone Linking**: The AIs for Simone and Precious store each other's GUIDs (`m_simoneGuid`, `m_preciousGuid`) to coordinate actions (aggro sharing, leash extension, evasion checks). This linkage is established during `JustReachedHome` (for Simone) and `Transform` (for Inconspicuous Simone).
4.  **Ringo Fainting**: `npc_ringoAI` randomly faints during the follow quest. The `SetFaint` and `ClearFaint` methods manage this state, pausing the follow and changing the stand state. The player must cast `SPELL_REVIVE_RINGO` to wake him up.
5.  **Static Cooldown Map**: `AreaTrigger_at_scent_larkorwi` uses a `static std::unordered_map<uint32, time_t>` to manage cooldowns. This is a global state for the area trigger ID, meaning all players share the same cooldown window for summoning the mate.
6.  **Spell Modification**: The `SimoneSeductressChainLightningScript` reduces damage by 75% if the target has Aspect of the Wild. This is a specific counter-mechanic for Hunters.
7.  **Respawn Scaling**: `npc_simone_seductressAI::JustDied` calculates respawn delay based on active session count, scaling inversely with population. This is a Blizzlike feature to adjust difficulty/availability based on server load.

## Member Reference

**npc_ame01AI**
Constructor for the escort AI of NPC Ame. Initializes the base `ScriptedEscortAI`.

**Reset#2**
Overrides the base reset method. Currently empty, relying on base class behavior.

**JustRespawned**
Sets the creature immune to NPCs upon respawn and calls the base `JustRespawned`.

**WaypointReached**
Triggers dialogue and quest completion events based on waypoint IDs reached during the escort.

**Aggro**
Plays random aggro sounds if aggroed by a non-player, unless the escort player is already fighting the same target.

**QuestAccept_npc_ame01**
Handles quest acceptance for "Chasing Ame". Starts the escort path, sets faction, and removes immunity.

**GetAI_npc_ame01**
Factory function to create a new `npc_ame01AI` instance.

**npc_ringoAI**
Constructor for the follower AI of NPC Ringo. Initializes the base `ScriptedFollowerAI`.

**Reset#4**
Initializes timers for fainting and end-event progress, clears the Spraggle pointer, and enables LoS events.

**JustRespawned#2**
Sets the creature immune to NPCs upon respawn and calls the base `JustRespawned`.

**MoveInLineOfSight**
Checks for proximity to Spraggle. If close and quest is incomplete, triggers quest progress and completes the follow.

**SpellHit**
Revives Ringo if hit by the specific revive spell while fainting or following.

**SetFaint**
Pauses the follow, plays a faint sound, and sets the stand state to sleep.

**ClearFaint**
Resumes the follow, plays a wake-up sound, and sets the stand state to standing.

**UpdateFollowerAI**
Manages the fainting timer during the follow and the state machine for the end-of-quest cutscene. Handles melee attacks in combat.

**GetAI_npc_ringo**
Factory function to create a new `npc_ringoAI` instance.

**QuestAccept_npc_ringo**
Handles quest acceptance for "A Little Help". Starts the follow path, sets faction, and removes immunity.

**AreaTrigger_at_scent_larkorwi**
Summons the Larkorwi Mate if the player has the relevant quest incomplete and no mate is nearby, respecting a cooldown.

**mob_captured_felwood_oozeAI**
Constructor for the ooze AI. Initializes the base `ScriptedAI`.

**Reset**
Initializes the search timer and merge flag.

**UpdateAI**
Searches for a Primal Ooze. If found, follows it; otherwise, despawns.

**MovementInform**
Checks proximity to the Primal Ooze during movement. If close, casts the merge spell.

**GetAI_mob_captured_felwood_ooze**
Factory function to create a new `mob_captured_felwood_oozeAI` instance.

**npc_precious_the_devourerAI**
Constructor for Precious the Devourer AI. Initializes the base `ScriptedAI`.

**Reset#3**
Sets visibility to ON and initializes the split-check timer.

**Aggro#2**
Forces the linked Simone to attack the same target.

**EnterEvadeMode**
Forces despawn if the linked Simone is dead. Otherwise, calls base evade.

**DamageTaken**
Extends the leash time of the linked Simone.

**UpdateAI#2**
Checks if the linked Simone is in combat. If not, forces her to evade. Performs melee attacks.

**GetAI_npc_precious_the_devourer**
Factory function to create a new `npc_precious_the_devourerAI` instance.

**npc_simone_seductressAI**
Constructor for Simone the Seductress AI. Initializes timers and the base `ScriptedAI`.

**Reset#5**
Initializes ability timers, threat check timers, and visibility.

**JustReachedHome**
Spawns or respawns Precious the Devourer and links the GUIDs between Simone and Precious AIs.

**Aggro#3**
Forces Precious to attack the same target. Checks if the attacker is a Hunter; if not, triggers despawn.

**JustDied**
Calculates and sets the respawn delay for the original Simone based on server population.

**DemonDespawn**
Summons The Cleaner, transfers threat from Simone and Precious to The Cleaner, and despawns Simone and Precious.

**DamageTaken#2**
Extends the leash time of the linked Precious.

**SpellHit#2**
Casts Silence on herself if hit by Viper Sting.

**UpdateAI#3**
Manages ability casting (Temptress Kiss, Chain Lightning), threat checks (despawns if multi-target), and split checks (evades Precious if not in combat).

**GetAI_npc_simone_seductress**
Factory function to create a new `npc_simone_seductressAI` instance.

**npc_simone_the_inconspicuousAI**
Constructor for Simone the Inconspicuous AI. Initializes the base `ScriptedAI`.

**Reset#6**
Sets respawn delay, gossip flag, and finds/summons Precious to follow.

**Transform**
Summons Simone the Seductress and Precious the Devourer, hiding and despawning the Inconspicuous forms. Links AI GUIDs.

**BeginEvent**
Initiates the transformation sequence from gossip selection.

**UpdateAI#4**
Manages the transformation emote/timer and combat abilities (Fool's Plight, melee).

**GossipHello_npc_simone_the_inconspicuous**
Adds a gossip item for the "Stave of the Ancients" quest.

**GossipSelect_npc_simone_the_inconspicuous**
Triggers the transformation event and plays a laugh emote.

**GetAI_npc_simone_the_inconspicuous**
Factory function to create a new `npc_simone_the_inconspicuousAI` instance.

**OnEffectExecute**
Reduces Chain Lightning damage by 75% if the target has Aspect of the Wild.

**GetScript_SimoneSeductressChainLightning**
Factory function to create the spell script instance.

**AddSC_ungoro_crater**
Registers all scripts in this file with the ScriptMgr.

---

<!-- machine-true, projected from graph.json -->

## Map — ungoro_crater

*Source:* ungoro_crater.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| npc_ame01AI | ctor | ScriptedEscortAI/npc_escortAI | — | — |
| Reset#2 | method | — | — | — |
| JustRespawned | method | ScriptedEscortAI/JustRespawned, WorldObject.Object/SetFlag | — | — |
| WaypointReached | method | Player.Main/GroupEventHappens, ScriptedEscortAI/GetPlayerForEscort, ScriptMgr/DoScriptText | — | — |
| Aggro | method | Object/GetTypeId, ScriptedEscortAI/GetPlayerForEscort, ScriptMgr/DoScriptText, shared_Util/urand, Unit.Main/GetVictim | — | — |
| QuestAccept_npc_ame01 | function | Creature.Main/AI, Creature.Main/SetFactionTemporary, Object/GetGUID, QuestDef/GetQuestId, ScriptedEscortAI/Start, Unit.Main/SetStandState, WorldObject.Object/RemoveFlag | — | — |
| GetAI_npc_ame01 | function | — | — | — |
| npc_ringoAI | ctor | ScriptedFollowerAI/FollowerAI | — | — |
| Reset#4 | method | Creature.Main/EnableMoveInLosEvent, shared_Util/urand | — | — |
| JustRespawned#2 | method | ScriptedFollowerAI/JustRespawned, WorldObject.Object/SetFlag | — | — |
| MoveInLineOfSight | method | Object/GetEntry, Player.Main/GetQuestStatus, Player.Main/GroupEventHappens, ScriptedFollowerAI/GetLeaderForFollower, ScriptedFollowerAI/HasFollowState, ScriptedFollowerAI/MoveInLineOfSight, ScriptedFollowerAI/SetFollowComplete, Unit.Main/GetVictim, WorldObject.Object/IsWithinDistInMap | — | — |
| SpellHit | method | ScriptedFollowerAI/HasFollowState | — | — |
| SetFaint | method | ScriptedFollowerAI/HasFollowState, ScriptedFollowerAI/SetFollowPaused, ScriptMgr/DoScriptText, Unit.Main/SetStandState | — | — |
| ClearFaint | method | ScriptedFollowerAI/HasFollowState, ScriptedFollowerAI/SetFollowPaused, ScriptMgr/DoScriptText, Unit.Main/SetStandState | — | — |
| UpdateFollowerAI | method | CreatureAI/DoMeleeAttackIfReady, ScriptedFollowerAI/HasFollowState, ScriptedFollowerAI/SetFollowComplete, ScriptMgr/DoScriptText, shared_Util/urand, Unit.Main/GetVictim, Unit.Main/IsAlive, Unit.Main/SelectHostileTarget | — | — |
| GetAI_npc_ringo | function | — | — | — |
| QuestAccept_npc_ringo | function | Creature.Main/AI, QuestDef/GetQuestId, ScriptedFollowerAI/StartFollow, Unit.Main/SetStandState, WorldObject.Object/RemoveFlag | — | — |
| AreaTrigger_at_scent_larkorwi | function | GridSearchers/GetClosestCreatureWithEntry, Player.Main/GetQuestStatus, Player.Main/IsGameMaster, Unit.Main/IsAlive, WorldObject.Object/SummonCreature#2 | — | — |
| mob_captured_felwood_oozeAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset | method | — | — | — |
| UpdateAI | method | Creature.Main/DespawnOrUnsummon, Creature.MotionMaster/MoveFollow, Unit.Main/GetMotionMaster, WorldObject.Object/FindNearestCreature | — | — |
| MovementInform | method | CreatureAI/DoCastSpellIfCan, WorldObject.Object/FindNearestCreature | — | — |
| GetAI_mob_captured_felwood_ooze | function | — | — | — |
| npc_precious_the_devourerAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#3 | method | Unit.Main/SetVisibility | — | — |
| Aggro#2 | method | Creature.Main/AI, CreatureAI/AttackStart, Map.Main/GetCreature, Unit.Main/IsAlive, WorldObject.Object/GetMap | — | — |
| EnterEvadeMode | method | Creature.Main/ForcedDespawn, Map.Main/GetCreature, ScriptedAI/EnterEvadeMode, Unit.Main/IsAlive, WorldObject.Object/GetMap | — | — |
| DamageTaken | method | Creature.Main/UpdateLeashExtensionTime, Map.Main/GetCreature, Unit.Main/IsAlive, WorldObject.Object/GetMap | — | — |
| UpdateAI#2 | method | Creature.Main/AI, CreatureAI/DoMeleeAttackIfReady, CreatureAI/EnterEvadeMode, Map.Main/GetCreature, Unit.Main/GetVictim, Unit.Main/IsInCombat, Unit.Main/SelectHostileTarget, WorldObject.Object/GetMap | — | — |
| GetAI_npc_precious_the_devourer | function | — | — | — |
| npc_simone_seductressAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#5 | method | ObjectGuid/Clear, shared_Util/urand, Unit.Main/SetVisibility | — | — |
| JustReachedHome | method | Creature.Main/AI, Creature.Main/ForcedDespawn, Map.Main/GetCreature, Object/GetObjectGuid, Unit.Main/IsAlive, WorldObject.Object/GetAngle, WorldObject.Object/GetMap, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/SummonCreature#2 | — | — |
| Aggro#3 | method | Creature.Main/AI, CreatureAI/AttackStart, Map.Main/GetCreature, Object/GetObjectGuid, ObjectGuid/IsEmpty, ObjectGuid/operator==, Unit.Main/GetClass, Unit.Main/IsAlive, WorldObject.Object/GetMap | — | — |
| JustDied | method | Creature.Main/SaveRespawnTime, Creature.Main/SetRespawnDelay, Creature.Main/SetRespawnTime, Map.Main/GetCreature, World/GetActiveSessionCount, WorldObject.Object/GetMap | — | — |
| DemonDespawn | method | Creature.Main/AI, Creature.Main/ForcedDespawn, Creature.Main/RemoveFromWorld, Creature.Main/SaveRespawnTime, Creature.Main/SetRespawnDelay, Creature.Main/SetRespawnTime, CreatureAI/AttackStart, Map.Main/GetCreature, ThreatManager/getThreatList, Unit.Main/AddThreat, Unit.Main/GetThreatManager, Unit.Main/IsAlive, Unit.Main/SetInCombatWith, Unit.Main/SetVisibility, WorldObject.Object/GetAngle, WorldObject.Object/GetMap, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/SummonCreature#2 | — | — |
| DamageTaken#2 | method | Creature.Main/UpdateLeashExtensionTime, Map.Main/GetCreature, Unit.Main/IsAlive, WorldObject.Object/GetMap | — | — |
| SpellHit#2 | method | CreatureAI/DoCastSpellIfCan, ScriptMgr/DoScriptText | — | — |
| UpdateAI#3 | method | Creature.Main/AI, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, CreatureAI/EnterEvadeMode, Map.Main/GetCreature, shared_Util/urand, ThreatManager/getThreatList, Unit.Main/GetThreatManager, Unit.Main/GetVictim, Unit.Main/IsAlive, Unit.Main/IsInCombat, Unit.Main/SelectHostileTarget, WorldObject.Object/GetMap | — | — |
| GetAI_npc_simone_seductress | function | — | — | — |
| npc_simone_the_inconspicuousAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#6 | method | Creature.Main/SetRespawnDelay, Creature.Main/SetRespawnTime, Creature.MotionMaster/MoveFollow, GridSearchers/GetClosestCreatureWithEntry, shared_Util/urand, Unit.Main/GetMotionMaster, Unit.Main/SetVisibility, WorldObject.Object/GetAngle, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/SetUInt32Value, WorldObject.Object/SummonCreature#2 | — | — |
| Transform | method | Creature.Main/AI, Creature.Main/ForcedDespawn, GridSearchers/GetClosestCreatureWithEntry, Map.Main/GetPlayer, Object/GetObjectGuid, Unit.Main/SetVisibility, WorldObject.Object/GetAngle, WorldObject.Object/GetMap, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/SummonCreature#2 | — | — |
| BeginEvent | method | Creature.MotionMaster/MoveIdle, Unit.Main/GetMotionMaster, WorldObject.Object/SetUInt32Value | — | — |
| UpdateAI#4 | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, shared_Util/urand, Unit.Main/GetVictim, Unit.Main/HandleEmote, Unit.Main/SelectHostileTarget | — | — |
| GossipHello_npc_simone_the_inconspicuous | function | GossipDef/AddMenuItem#4, GossipDef/SendGossipMenu, Object/GetObjectGuid, Player.Main/GetGossipTextId, Player.Main/GetQuestStatus, PlayerMenu/GetGossipMenu | — | — |
| GossipSelect_npc_simone_the_inconspicuous | function | Creature.Main/AI, GossipDef/CloseGossip, Object/GetObjectGuid, Unit.Main/HandleEmote | — | — |
| GetAI_npc_simone_the_inconspicuous | function | — | — | — |
| OnEffectExecute | method | Spell.Main/GetUnitTarget, Unit.Main/HasAura#2 | — | — |
| GetScript_SimoneSeductressChainLightning | function | — | — | — |
| AddSC_ungoro_crater | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
