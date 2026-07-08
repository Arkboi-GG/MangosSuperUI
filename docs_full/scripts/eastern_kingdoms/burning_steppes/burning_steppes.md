# burning_steppes

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# burning_steppes

**Purpose & Responsibilities**
`burning_steppes.cpp` implements the scripted behaviors for two distinct quests in the Burning Steppes zone of World of Warcraft:
1.  **"Precarious Predicament"**: An escort quest involving the NPC `Grark Lorkrub`. The script manages a complex escort route with multiple ambush waves, a timed dialogue sequence involving summoned allies (`High Executioner Nuzark` and `Shadow of Lexlort`), and a fake-death finale. It also handles the spell logic for capturing Grark (`spell_capture_grark`).
2.  **"Stave of the Ancients"**: A transformation/combat quest involving `Nelson the Nice` (who transforms into `Klinfran the Crazed`). The script manages gossip interactions, a timed transformation into a demon form, specific combat mechanics (threat management, spell triggers), and a cleanup mechanic summoning `The Cleaner` upon failure or death.

This unit contains no database table interactions; all logic is driven by in-memory state, creature entries, and script hooks.

## Member-by-Member Behavior

### Grark Lorkrub Escort System

#### Initialization and State Management
*   **`npc_grark_lorkrubAI` (ctor)**: Initializes the AI by calling `Reset()` to set initial timers and flags. It inherits from `ScriptedEscortAI` (referenced as `npc_escortAI` in the map/source comments) to handle waypoint navigation and escort state tracking.
*   **`Reset`**: Resets internal counters (`m_uiKilledCreatures`, `m_uiSomethingWentWrongTimer`) and clears the list of summoned Searscale Drakes. Crucially, it removes the `UNIT_FLAG_NOT_SELECTABLE` flag from Grark, making him interactable again if the escort restarts. It only performs these resets if the escort is not currently active (`!HasEscortState(STATE_ESCORT_ESCORTING)`).
*   **`Aggro`**: If Grark is not actively escorting, he plays an aggro sound (`SAY_AGGRO`). During the escort, aggro is suppressed to prevent unintended combat interruptions.
*   **`MoveInLineOfSight`**: Suppresses standard line-of-sight aggro checks while the escort is active (`HasEscortState(STATE_ESCORT_ESCORTING)`), ensuring Grark doesn't randomly engage enemies while moving to waypoints.

#### Waypoint and Ambush Logic
*   **`WaypointReached`**: The core driver of the escort event. It triggers specific actions at designated waypoint IDs:
    *   **WP 1 & 7**: Plays introductory dialogue (`SAY_START`, `SAY_PAY`).
    *   **WP 12 (First Ambush)**: Pauses the escort, summons Blackrock Ambushers and Raiders, and plays `SAY_FIRST_AMBUSH_START`.
    *   **WP 24 (Second Ambush)**: Pauses the escort, summons Ambushers and Flamescale Dragonspawn, and plays `SAY_SEC_AMBUSH_START`.
    *   **WP 28**: Summons three Searscale Drakes. These are tracked in `m_lSearscaleGuidList`.
    *   **WP 30 (Third Ambush)**: Checks if 11 creatures have been killed. If not, it pauses the escort. It then forces all tracked Searscale Drakes to attack the player. Plays `SAY_THIRD_AMBUSH_START`.
    *   **WP 36**: Plays a laugh emote (`EMOTE_LAUGH`).
    *   **WP 45 (Finale)**: Starts the outro dialogue sequence (`StartNextDialogueText`), pauses the escort, and summons `High Executioner Nuzark` and `Shadow of Lexlort` for the final cutscene.

#### Dialogue and Finale Sequence
*   **`StartNextDialogueText`**: Locates the starting index in the static `aOutroDialogue` array based on the provided action ID (e.g., `SAY_LAST_STAND`) and initializes the `dialogueStep` and `dialogueTimer`.
*   **`DialogueUpdate`**: Called periodically via `UpdateEscortAI`. It advances the dialogue sequence based on timers defined in `aOutroDialogue`. It retrieves the correct speaker using `GetSpeakerByEntry` and triggers the corresponding text or emote via `DoScriptText`. It then calls `JustDidDialogueStep` to handle specific logic tied to each dialogue line.
*   **`JustDidDialogueStep`**: Handles side effects of specific dialogue lines:
    *   **`SAY_LEXLORT_1`**: Makes Grark kneel (`UNIT_STAND_STATE_KNEEL`).
    *   **`SAY_LEXLORT_3`**: Triggers an attack emote on `Nuzark` if he is alive.
    *   **`NPC_GRARK_LORKRUB`**: Simulates Grark's "death". He interrupts spells, sets health to 1, stops moving, clears combos/auras/reactives, becomes unselectable, goes idle, and assumes a dead stand state. This is a visual fake-out.
    *   **`SAY_LEXLORT_4`**: Awards the quest (`QUEST_ID_PRECARIOUS_PREDICAMENT`) to the player and deals lethal damage to Grark to actually kill him, completing the event.
*   **`GetSpeakerByEntry`**: Helper function that returns the `Creature` pointer for Grark, Nuzark, or Lexlort based on their entry ID, allowing the dialogue system to attribute speech correctly.

#### Summoning and Cleanup
*   **`JustSummoned`**: Handles newly spawned creatures:
    *   Stores GUIDs for Nuzark and Lexlort.
    *   Tracks Searscale Drake GUIDs in `m_lSearscaleGuidList`.
    *   Forces other summoned mobs (Ambushers/Raiders) to attack the escorting player immediately.
*   **`SummonedCreatureJustDied`**: Increments `m_uiKilledCreatures`. When specific thresholds are reached (4, 8, 11 kills), it plays the corresponding "ambush end" dialogue, resets the "something went wrong" timer, and resumes the escort (`SetEscortPaused(false)`).
*   **`UpdateEscortAI`**: Manages the main update loop. It calls `DialogueUpdate` for the finale. If the escort is paused and the `m_uiSomethingWentWrongTimer` expires (players fail to kill adds), Grark despawns/dies to prevent soft-locking. Otherwise, it handles standard melee combat if Grark has a target.

#### External Hooks
*   **`GetAI_npc_grark_lorkrub`**: Factory function returning a new instance of `npc_grark_lorkrubAI`.
*   **`QuestAccept_npc_grark_lorkrub`**: Triggered when a player accepts `QUEST_ID_PRECARIOUS_PREDICAMENT`. It casts the creature's AI to `npc_grark_lorkrubAI` and starts the escort with the player as the target.
*   **`EffectDummyCreature_spell_capture_grark`**: Handles the spell `SPELL_CAPTURE_GRARK` (ID 14250). If Grark's health is below 25%, he submits (emote), changes faction to friendly temporarily, and evades combat. This allows players to capture him non-lethally as part of the quest mechanics.

### Klinfran the Crazed Transformation System

#### Initialization and State Management
*   **`npc_klinfranAI` (ctor)**: Initializes the AI, setting `m_bTransform` to false and calling `Reset()`. Inherits from `ScriptedAI`.
*   **`Reset`**: Configures behavior based on the current creature entry:
    *   **`NPC_NELSON_THE_NICE`**: Sets respawn delay, home position, and waypoint movement. Enables gossip. Initializes timers for the transformation sequence.
    *   **`NPC_KLINFRAN_THE_CRAZED`**: Sets a despawn timer (20 minutes) and initializes the Demonic Frenzy timer. Clears the hunter GUID.
*   **`Transform`**: Changes Nelson's entry to `NPC_KLINFRAN_THE_CRAZED`, updates his home position to current location, switches movement to idle, and calls `Reset()` to apply the demon-specific settings.
*   **`BeginEvent`**: Triggered by gossip selection. Records the player's GUID as `m_hunterGuid`, stops movement, disables gossip flags, and sets `m_bTransform` to true, initiating the transformation countdown.

#### Combat and Mechanics
*   **`Aggro`**: Checks if the aggressor is a Hunter and matches the recorded `m_hunterGuid`. If valid, it records the GUID. If invalid (wrong class or wrong player), it triggers `DemonDespawn()`, effectively failing the quest attempt.
*   **`JustDied`**: Sets the respawn position and calculates a dynamic respawn delay based on server population (DRSS logic). Saves the respawn time.
*   **`DemonDespawn`**: Handles the cleanup phase. It sets a 15-minute respawn delay and saves the time. If `triggered` is true (failure/aggro error), it summons `The Cleaner` at Klinfran's location. The Cleaner inherits Klinfran's threat list and attacks all current targets. Finally, Klinfran is forced to despawn.
*   **`SpellHit`**: If Klinfran is hit by `Scorpid Sting (Rank 4)` (ID 14277), he removes any existing `DEMONIC_FRENZY` aura and casts `ENTROPIC_STING` as a triggered effect.
*   **`UpdateAI`**: Manages the main update loop:
    *   **Transformation Phase**: If `m_bTransform` is true, it counts down timers. After 5 seconds, it points (`EMOTE_ONESHOT_POINT`). After 10 seconds, it calls `Transform()` to become Klinfran.
    *   **Despawn Timer**: If alive and not in combat after 20 minutes, Klinfran despawns cleanly.
    *   **Combat Phase**: If the threat list size exceeds 1 (multiple targets), it triggers `DemonDespawn()`, enforcing a single-target constraint. It casts `DEMONIC_FRENZY` every 15 seconds with an emote. Handles standard melee attacks.

#### External Hooks
*   **`GossipHello_npc_klinfran`**: Displays the gossip menu. If the player has `QUEST_STAVE_OF_THE_ANCIENTS` incomplete, it adds the "Show me your real face, demon." option.
*   **`GossipSelect_npc_klinfran`**: Closes the gossip menu and calls `BeginEvent()` on the AI, passing the player's GUID.
*   **`GetAI_npc_klinfran`**: Factory function returning a new instance of `npc_klinfranAI`.

### Script Registration
*   **`AddSC_burning_steppes`**: Registers both scripts with the `ScriptMgr`. It links the AI factories, gossip handlers, quest accept handler, and spell effect handler to their respective creature entries.

## Cross-Unit Boundaries

*   **`ScriptedEscortAI` / `npc_escortAI`**: Used by `npc_grark_lorkrubAI` for waypoint management, escort state checking (`HasEscortState`), pausing/resuming (`SetEscortPaused`), and retrieving the escorted player (`GetPlayerForEscort`).
*   **`ScriptMgr`**: Used by both AIs to play dialogue (`DoScriptText`).
*   **`Creature` / `WorldObject`**: Used extensively for summoning creatures (`SummonCreature`), retrieving creatures by GUID (`GetMap()->GetCreature`), manipulating flags/health/motion (`SetFlag`, `SetHealth`, `GetMotionMaster`), and positioning (`NearTeleportTo`, `SetHomePosition`).
*   **`Player`**: Used to award quests (`GroupEventHappens`), check quest status (`GetQuestStatus`), and manage gossip menus (`ADD_GOSSIP_ITEM`, `SEND_GOSSIP_MENU`).
*   **`Unit`**: Used for combat logic (`AttackStart`, `SelectHostileTarget`, `GetVictim`, `DealDamage`), threat management (`GetThreatManager`, `AddThreat`), and state checks (`IsAlive`, `IsInCombat`, `GetClass`).
*   **`MotionMaster`**: Used to control movement types (`MoveIdle`, `Clear`, `Initialize`) and check current movement generators.
*   **`ThreatManager`**: Used by `npc_klinfranAI` to inspect the threat list size and transfer threats to `The Cleaner`.
*   **`SpellCaster` / `SpellEntry`**: Used in `SpellHit` to identify incoming spells and trigger counter-spells.
*   **`Script` / `ScriptMgr`**: Used in `AddSC_burning_steppes` to register the scripts with the engine.

## Data Model

This unit does not interact with any database tables. All data is hardcoded in the source file (creature entries, spell IDs, dialogue strings, coordinates) or managed in memory via object states.

## Notable Implementation Details

1.  **Fake Death Mechanic**: In `npc_grark_lorkrubAI::JustDidDialogueStep`, Grark is not actually killed until the final dialogue step. Instead, his health is set to 1, he is made unselectable, and his stand state is changed to `UNIT_STAND_STATE_DEAD`. This allows the cutscene to play out visually before the actual death occurs.
2.  **Escort Failure Timeout**: `npc_grark_lorkrubAI` uses `m_uiSomethingWentWrongTimer` (400 seconds) to detect if players fail to kill ambush adds. If the timer expires while paused, Grark dies to prevent the quest from being soft-locked.
3.  **Single-Target Constraint**: `npc_klinfranAI` enforces a strict single-target rule. If `ThreatManager::getThreatList().size() > 1`, `DemonDespawn()` is called, summoning `The Cleaner` to wipe the area and resetting the encounter.
4.  **Dynamic Respawn**: `npc_klinfranAI::JustDied` adjusts the respawn delay based on the number of active sessions on the server (`sWorld.GetActiveSessionCount()`), scaling the delay inversely with population to maintain encounter availability.
5.  **Spell Interaction**: `npc_klinfranAI::SpellHit` specifically checks for `Scorpid Sting (Rank 4)` to trigger `Entropic Sting`. This suggests a specific hunter pet interaction intended by the quest design.
6.  **Hardcoded Coordinates**: All summoning positions and home locations are hardcoded floats in the source code, requiring manual adjustment if map geometry changes.

## Member Reference

**npc_grark_lorkrubAI** (ctor): Initializes the escort AI, calling `Reset()` to set initial state. Inherits from `ScriptedEscortAI`.

**Reset**: Resets escort state, timers, and creature flags if not currently escorting. Removes `UNIT_FLAG_NOT_SELECTABLE`.

**Aggro**: Plays aggro sound if not escorting; suppresses aggro during escort.

**MoveInLineOfSight**: Suppresses LOS aggro checks during escort.

**WaypointReached**: Triggers dialogue, summons ambush mobs, and manages escort pause/resume at specific waypoints. Initiates the finale sequence at WP 45.

**StartNextDialogueText**: Initializes the dialogue sequence index and timer for the outro cutscene.

**DialogueUpdate**: Advances the dialogue sequence based on timers, triggering text/emotes and calling `JustDidDialogueStep`.

**JustDidDialogueStep**: Handles specific logic for dialogue steps, including kneeling, emotes, fake death setup, quest completion, and actual death.

**JustSummoned**: Stores GUIDs for key NPCs, tracks Searscale Drakes, and forces hostile summons to attack the player.

**SummonedCreatureJustDied**: Counts killed adds and resumes the escort when wave thresholds are met.

**GetSpeakerByEntry**: Returns the `Creature` pointer for Grark, Nuzark, or Lexlort based on entry ID.

**UpdateEscortAI**: Main update loop. Handles dialogue progression, timeout checks for failed ambushes, and standard melee combat.

**GetAI_npc_grark_lorkrub**: Factory function creating `npc_grark_lorkrubAI` instances.

**QuestAccept_npc_grark_lorkrub**: Starts the escort when the quest is accepted.

**EffectDummyCreature_spell_capture_grark**: Handles the capture spell, making Grark friendly and evading if health is low.

**npc_klinfranAI** (ctor): Initializes the transformation AI, setting initial flags and calling `Reset()`. Inherits from `ScriptedAI`.

**Reset#2**: Configures behavior based on current entry (Nelson vs. Klinfran), setting timers, positions, and movement types.

**Transform**: Changes Nelson's entry to Klinfran, updates position/movement, and resets state for demon mode.

**BeginEvent**: Starts the transformation sequence, recording the player's GUID and disabling gossip.

**Aggro#2**: Validates aggressor as the correct Hunter; triggers `DemonDespawn` on invalid aggro.

**JustDied**: Sets respawn position and calculates dynamic respawn delay based on server population.

**DemonDespawn**: Cleans up the encounter, summoning `The Cleaner` to attack current threats, and despawns Klinfran.

**SpellHit**: Triggers `Entropic Sting` if hit by `Scorpid Sting (Rank 4)`.

**UpdateAI**: Main update loop. Handles transformation countdown, despawn timer, single-target enforcement, `Demonic Frenzy` casting, and melee combat.

**GossipHello_npc_klinfran**: Displays gossip menu with transformation option if quest is incomplete.

**GossipSelect_npc_klinfran**: Closes gossip and initiates the transformation event.

**GetAI_npc_klinfran**: Factory function creating `npc_klinfranAI` instances.

**AddSC_burning_steppes**: Registers both scripts with the `ScriptMgr`, linking AI, gossip, quest, and spell handlers.

---

<!-- machine-true, projected from graph.json -->

## Map — burning_steppes

*Source:* burning_steppes.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| npc_grark_lorkrubAI | ctor | ScriptedEscortAI/npc_escortAI | — | — |
| Reset | method | ScriptedEscortAI/HasEscortState, WorldObject.Object/RemoveFlag | — | — |
| Aggro | method | ScriptedEscortAI/HasEscortState, ScriptMgr/DoScriptText | — | — |
| MoveInLineOfSight | method | ScriptedEscortAI/HasEscortState, ScriptedEscortAI/MoveInLineOfSight | — | — |
| WaypointReached | method | Creature.Main/AI, CreatureAI/AttackStart, Map.Main/GetCreature, ScriptedEscortAI/GetPlayerForEscort, ScriptedEscortAI/SetEscortPaused, ScriptMgr/DoScriptText, WorldObject.Object/GetMap, WorldObject.Object/SummonCreature#2 | — | — |
| StartNextDialogueText | method | — | — | — |
| DialogueUpdate | method | ScriptMgr/DoScriptText | — | — |
| JustDidDialogueStep | method | Creature.MotionMaster/MoveIdle, Map.Main/GetCreature, MotionMaster/Clear, Player.Main/GroupEventHappens, ScriptedEscortAI/GetPlayerForEscort, SpellCaster/InterruptNonMeleeSpells, Unit.Main/ClearAllReactives, Unit.Main/ClearComboPointHolders, Unit.Main/DealDamage, Unit.Main/GetHealth, Unit.Main/GetMotionMaster, Unit.Main/HandleEmote, Unit.Main/RemoveAllAurasOnDeath, Unit.Main/SetHealth, Unit.Main/SetStandState, Unit.Main/StopMoving, WorldObject.Object/GetMap, WorldObject.Object/SetFlag | — | — |
| JustSummoned | method | Creature.Main/AI, CreatureAI/AttackStart, Object/GetEntry, Object/GetObjectGuid, ScriptedEscortAI/GetPlayerForEscort | — | — |
| SummonedCreatureJustDied | method | ScriptedEscortAI/SetEscortPaused, ScriptMgr/DoScriptText | — | — |
| GetSpeakerByEntry | method | Map.Main/GetCreature, WorldObject.Object/GetMap | — | — |
| UpdateEscortAI | method | Creature.Main/DisappearAndDie, CreatureAI/DoMeleeAttackIfReady, ScriptedEscortAI/HasEscortState, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_npc_grark_lorkrub | function | — | — | — |
| QuestAccept_npc_grark_lorkrub | function | Creature.Main/AI, Object/GetGUID, QuestDef/GetQuestId, ScriptedEscortAI/Start | — | — |
| EffectDummyCreature_spell_capture_grark | function | Creature.Main/AI, Creature.Main/SetFactionTemporary, CreatureAI/EnterEvadeMode, ScriptMgr/DoScriptText, Unit.Main/GetHealthPercent | — | — |
| npc_klinfranAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#2 | method | Creature.Main/SetDefaultMovementType, Creature.Main/SetHomePosition, Creature.Main/SetRespawnDelay, Creature.Main/SetRespawnTime, Creature.MotionMaster/GetCurrentMovementGeneratorType, Creature.MotionMaster/Initialize, Object/GetEntry, ObjectGuid/Clear, Unit.Main/GetMotionMaster, Unit.Main/NearTeleportTo, WorldObject.Object/SetUInt32Value | — | — |
| Transform | method | Creature.Main/SetDefaultMovementType, Creature.Main/SetHomePosition, Creature.Main/UpdateEntry, Creature.MotionMaster/Initialize, Unit.Main/GetMotionMaster, WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ | — | — |
| BeginEvent | method | Creature.MotionMaster/MoveIdle, MotionMaster/Clear, Unit.Main/GetMotionMaster, WorldObject.Object/SetUInt32Value | — | — |
| Aggro#2 | method | Object/GetObjectGuid, ObjectGuid/IsEmpty, ObjectGuid/operator==, Unit.Main/GetClass | — | — |
| JustDied | method | Creature.Main/SaveRespawnTime, Creature.Main/SetHomePosition, Creature.Main/SetRespawnDelay, Creature.Main/SetRespawnTime, World/GetActiveSessionCount | — | — |
| DemonDespawn | method | Creature.Main/AI, Creature.Main/ForcedDespawn, Creature.Main/SaveRespawnTime, Creature.Main/SetHomePosition, Creature.Main/SetRespawnDelay, Creature.Main/SetRespawnTime, CreatureAI/AttackStart, ThreatManager/getThreatList, Unit.Main/AddThreat, Unit.Main/GetThreatManager, Unit.Main/IsAlive, Unit.Main/SetInCombatWith, WorldObject.Object/GetAngle, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/SummonCreature#2 | — | — |
| SpellHit | method | CreatureAI/DoCastSpellIfCan, Unit.Main/RemoveAurasDueToSpell | — | — |
| UpdateAI | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, ScriptMgr/DoScriptText, ThreatManager/getThreatList, Unit.Main/GetThreatManager, Unit.Main/GetVictim, Unit.Main/HandleEmote, Unit.Main/IsAlive, Unit.Main/IsInCombat, Unit.Main/SelectHostileTarget | — | — |
| GossipHello_npc_klinfran | function | GossipDef/AddMenuItem#4, GossipDef/SendGossipMenu, Object/GetObjectGuid, Player.Main/GetGossipTextId, Player.Main/GetQuestStatus, PlayerMenu/GetGossipMenu | — | — |
| GossipSelect_npc_klinfran | function | Creature.Main/AI, GossipDef/CloseGossip, Object/GetObjectGuid | — | — |
| GetAI_npc_klinfran | function | — | — | — |
| AddSC_burning_steppes | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
