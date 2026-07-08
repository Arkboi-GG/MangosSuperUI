# gnomeregan

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# gnomeregan

**Purpose & Responsibilities**
This translation unit implements scripted behaviors for two distinct encounters and one spell mechanic within the Gnomeregan dungeon instance. The primary focus is the **Grubbis encounter**, orchestrated by `npc_blastmaster_emi_shortfuse` (Blastmaster Emi Shortfuse). This AI manages a complex escort sequence involving waypoint navigation, timed dialogue, summoning waves of adds ("packs"), triggering environmental events (opening/closing cave-in doors), and coordinating the detonation of explosive charges via the instance data system.

The secondary component is the **Kernobee questline** (`npc_kernobee`), which handles the "A Fine Mess" quest. This AI manages a follower mechanic where Kernobee follows the player, interacts with a companion bomb creature (`NPC_ALARM_A_BOMB_2600`), and triggers a scripted explosion sequence upon reaching specific coordinates.

Finally, the unit implements a randomization script for the spell **Collecting Fallout** (`GnomereganCollectingFalloutScript`), ensuring that only one of two possible spell effects triggers per cast.

No database tables are directly queried or modified by this unit; all state management is handled through the `instance_gnomeregan` script instance object in memory.

## Member-by-Member Behavior

### Blastmaster Emi Shortfuse (Grubbis Encounter)

The `npc_blastmaster_emi_shortfuseAI` class inherits from `npc_escortAI` and drives the Grubbis boss fight. The encounter is divided into phases managed by `m_uiPhase` and `m_uiPhaseTimer`.

#### Initialization and State Management
*   **`npc_blastmaster_emi_shortfuseAI`**: Initializes the AI. It retrieves the `instance_gnomeregan` pointer. If the instance data indicates the Grubbis encounter is already `DONE`, it removes the gossip menu flags from the creature to prevent re-engagement. It then calls `Reset()` to initialize internal state variables.
*   **`Reset`**: Resets phase timers and boolean flags (`m_bDidAggroText`, cave-in open states). It clears the list of summoned mob GUIDs. Crucially, it re-enables `MoveInLineOfSight` events, which are often disabled during escort pauses. It checks `HasEscortState` to ensure it doesn't reset state mid-escort.
*   **`StartEvent`**: Triggered by the gossip selection. It sets the instance data for `TYPE_GRUBBIS` to `IN_PROGRESS`, initializes the phase timer, and stores the player's GUID to track who started the event.

#### Escort and Waypoint Logic
*   **`WaypointStart`**: Triggered when the escort begins moving toward a waypoint.
    *   At WP 10, it opens the Southern Cave-In door via `instance_gnomeregan::DoUseDoorOrButton`.
    *   At WP 12 and 16, it triggers specific dialogue lines (`SAY_CHARGE_1`, `SAY_CHARGE_3`).
    *   At WP 16, it also opens the Northern Cave-In door.
*   **`WaypointReached`**: Triggered upon arrival at a waypoint. It sets timers for subsequent phases, triggers emotes (e.g., `EMOTE_STATE_USESTANDING` for preparing charges), and pauses the escort at critical moments (WP 15, WP 19) to allow for scripted actions like facing objects or waiting for explosions.
*   **`UpdateEscortAI`**: The main update loop. It handles the phase-based logic when the creature is not in combat (`!GetVictim`).
    *   **Phases 1–3**: Intro dialogue and starting the escort.
    *   **Phases 4–9**: Dialogue leading up to the first cave-in.
    *   **Phases 10–14**: Summons packs 1–3. Triggers the creation of the first two explosive charges via `instance_gnomeregan::SetData(TYPE_EXPLOSIVE_CHARGE, ...)`.
    *   **Phases 15–21**: Countdown and detonation of the Southern explosives. It faces the door, plays countdown dialogue, casts `SPELL_EXPLOSION_SOUTH`, closes the door, and marks the charges as used.
    *   **Phases 22–24**: Dialogue leading to the second cave-in.
    *   **Phases 25–29**: Resumes escort, summons packs 4–6, and triggers the creation of the next two explosive charges.
    *   **Phases 30–34**: Countdown for the Northern explosives. Summons Pack 7 (Grubbis and Chomper).
    *   **Phases 35–41**: Post-Grubbis death sequence. It waits for Grubbis to die, plays victory dialogue, detonates the Northern explosives (`SPELL_EXPLOSION_NORTH`), closes the door, and finishes with fireworks (`SPELL_FIREWORKS_RED`).
    *   If in combat, it falls back to standard melee attacks (`DoMeleeAttackIfReady`).

#### Summoning and Combat
*   **`DoSummonPack`**: Iterates through the static `asSummonInfo` array. It summons creatures matching the specified pack index (`uiIndex`) at their predefined coordinates. This allows for batch summoning of adds.
*   **`JustSummoned`**: Handles post-summon logic.
    *   For Ambushers/Burrowers: It calculates a spawn point near the relevant cave-in door (North or South depending on phase) and moves them there using `MovePoint`.
    *   For Chomper: It joins the Chomper to Grubbis's creature group, ensuring they move and aggro together.
    *   It records the summoned creature's GUID in `m_luiSummonedMobGUIDs` for cleanup.
*   **`SummonedCreatureJustDied`**: Removes the dead creature's GUID from the tracking list. If Grubbis dies, it sets the instance data to `DONE` and starts a short timer (likely to trigger the final phase logic in `UpdateEscortAI`).
*   **`MoveInLineOfSight` / `AttackStart`**: Both check `IsPreparingExplosiveCharge()`. If Emi is in a phase where she is setting charges (phases 11, 13, 26, 28), she ignores hostile units to prevent premature aggro. Otherwise, it delegates to the parent `npc_escortAI` implementation.
*   **`AttackedBy`**: Plays a random aggro line (`SAY_AGGRO_1` or `SAY_AGGRO_2`) once per combat, tracked by `m_bDidAggroText`.
*   **`JustDied`**: If Emi dies, the encounter fails. It sets instance data to `FAIL`, closes any open cave-in doors, and despawns all summoned adds.

#### Gossip Interface
*   **`GossipHello_npc_blastmaster_emi_shortfuse`**: Displays a gossip menu item ("I am ready to begin") only if the Grubbis encounter is `NOT_STARTED` or `FAIL`.
*   **`GossipSelect_npc_blastmaster_emi_shortfuse`**: If the player selects the start option, it casts the AI to `npc_blastmaster_emi_shortfuseAI` and calls `StartEvent()`.

### Kernobee (Quest: A Fine Mess)

The `npc_kernobeeAI` class inherits from `FollowerAI` and manages the quest where Kernobee follows the player to disarm/interact with a bomb.

*   **`npc_kernobeeAI`**: Initializes the AI and calls `QuestReset()` to set initial states (dead stand state, timers).
*   **`Reset#2`**: Empty override.
*   **`JustRespawned`**: Sets the `UNIT_FLAG_IMMUNE_TO_NPC` flag, preventing other NPCs from attacking Kernobee initially.
*   **`StartQuest`**: Triggered by `QuestAccept_npc_kernobee`.
    *   Plays intro dialogue.
    *   Changes stand state to standing.
    *   Removes the immune flag.
    *   Starts the follow path.
    *   Retrieves the GUID of the bomb creature (`NPC_ALARM_A_BOMB_2600`) from instance data.
    *   Pauses Kernobee's follow temporarily.
    *   Makes the bomb follow Kernobee.
    *   Plays bomb intro dialogue.
    *   Sets `nextStep` to 1, initiating the quest logic.
*   **`UpdateFollowerAI`**: Manages the quest progression via `nextStep`:
    *   **Step 1**: Waits for Kernobee to be near the bomb (`NPC_ALARM_A_BOMB_2600`). If found, unpauses follow and moves to Step 2.
    *   **Step 2**: Checks if Kernobee is near the "End Position" (`aKernobeePositions[0]`). If so, moves to Step 3. If the bomb is nearby, it might trigger an early explosion (Step 6) if logic dictates, though primarily it waits for position.
    *   **Step 3**: Kernobee is at the end position. After a delay, it casts the explosion spell on the bomb and deals lethal damage to both the bomb and Kernobee. Moves to Step 4.
    *   **Step 4**: Ensures both entities are dead/despawned.
    *   **Step 5**: Alternative completion path (possibly if the bomb explodes earlier?). It triggers the explosion and despawns both.
    *   **Step 6**: Cleanup step, despawning the bomb and Kernobee.
    *   It also tracks `canSeeEnd` to play specific dialogue when Kernobee reaches certain coordinates.
*   **`JustDied#2`**: Calls parent `JustDied`, resets quest state, and ensures the bomb is despawned if still alive.
*   **`QuestReset`**: Resets internal timers, steps, and stand state to dead.
*   **`QuestAccept_npc_kernobee`**: Entry point for the quest. Validates the quest ID and calls `StartQuest` on the AI.

### Spell Script: Collecting Fallout

*   **`OnInit`**: Randomly selects one of the two effect indices (`EFFECT_INDEX_0` or `EFFECT_INDEX_1`) to be the active effect for this cast.
*   **`OnEffectExecute`**: Returns `true` only if the current effect index matches the randomly chosen one. This ensures that only one of the two potential outcomes of the spell occurs per cast, effectively randomizing the loot/outcome.

### Registration

*   **`AddSC_gnomeregan`**: Registers the scripts for `npc_blastmaster_emi_shortfuse`, `npc_kernobee`, and `spell_gnomeregan_collecting_fallout` with the script manager.

## Cross-Unit Boundaries

*   **`instance_gnomeregan`**:
    *   **Called By**: `npc_blastmaster_emi_shortfuseAI` and `npc_kernobeeAI`.
    *   **Collaboration**: The AI classes rely heavily on `instance_gnomeregan` for state persistence and coordination.
        *   `GetData`/`GetData64`: Used to retrieve encounter status (`TYPE_GRUBBIS`), GUIDs of doors (`GO_CAVE_IN_NORTH/SOUTH`), and the bomb creature (`NPC_ALARM_A_BOMB_2600`).
        *   `SetData`: Used to signal progress (e.g., `TYPE_GRUBBIS` = `IN_PROGRESS`/`DONE`/`FAIL`) and to trigger the spawning of explosive charges (`TYPE_EXPLOSIVE_CHARGE`).
        *   `DoUseDoorOrButton`: Used to open/close the cave-in doors during the escort.
*   **`ScriptedEscortAI` / `FollowerAI`**:
    *   **Called By**: `npc_blastmaster_emi_shortfuseAI` and `npc_kernobeeAI`.
    *   **Collaboration**: Provides the base framework for movement and state management. Methods like `Start`, `SetEscortPaused`, `HasEscortState`, `StartFollow`, and `SetFollowPaused` are used to control the physical movement of the NPCs along paths.
*   **`WorldObject.Object` / `Creature.Main` / `Unit.Main`**:
    *   **Called By**: All AI methods.
    *   **Collaboration**: Standard engine interactions for summoning creatures, finding nearest targets, managing motion masters, casting spells, dealing damage, and handling emotes/dialogue.
*   **`ScriptMgr`**:
    *   **Called By**: AI methods.
    *   **Collaboration**: Used to broadcast dialogue lines (`DoScriptText`) to players.

## Data Model

This unit does not interact directly with any database tables. All state is maintained in memory via the `instance_gnomeregan` object and local AI variables.

## Notable Implementation Details

*   **Phase Synchronization**: The Grubbis encounter relies on tight synchronization between waypoint arrivals (`WaypointReached`) and phase timers (`UpdateEscortAI`). The `m_uiPhase` variable increments after each timer expires, driving the sequence. Care must be taken if modifying timers, as desynchronization can cause missed dialogue or failed spawns.
*   **Explosive Charge Coordination**: The AI does not summon the explosive charges itself. Instead, it calls `instance_gnomeregan::SetData(TYPE_EXPLOSIVE_CHARGE, ...)` to signal the instance script to spawn them. This decouples the AI from the specific mechanics of the charges, allowing the instance script to manage their lifecycle.
*   **Aggro Suppression**: During charge preparation phases, `MoveInLineOfSight` and `AttackStart` are overridden to ignore threats. This prevents Emi from engaging adds prematurely while she is vulnerable and performing scripted animations.
*   **Kernobee Bomb Interaction**: The Kernobee AI uses a hardcoded position array (`aKernobeePositions`) to determine quest progression. It checks distance to these points to trigger dialogue and the final explosion. The bomb creature is expected to follow Kernobee, and the AI verifies its presence before proceeding.
*   **Randomized Spell Effect**: The `GnomereganCollectingFalloutScript` uses `urand` in `OnInit` to pick one effect. This is a common pattern for spells with multiple possible outcomes where only one should occur. Note that `chosenEffect` is stored in the script object, which is created per spell cast, ensuring randomness per cast.
*   **Hardcoded Coordinates**: Summon positions for Grubbis adds are hardcoded in `asSummonInfo`. Any changes to the map geometry or spawn locations require updating this array.
*   **Missing Alarm Bot**: A comment in the source (`TODO: It appears there are some things missing, including his? alarm-bot`) suggests that the Kernobee quest implementation may be incomplete regarding the "alarm bot" aspect, potentially relying on the bomb creature (`NPC_ALARM_A_BOMB_2600`) to fulfill that role partially.

## Member Reference

*   **npc_blastmaster_emi_shortfuseAI**: Initializes the AI, retrieves instance data, disables gossip if encounter is done, and calls `Reset()`.
*   **Reset**: Resets phase timers, boolean flags, and summoned mob list; re-enables LoS events.
*   **DoSummonPack**: Summons creatures from `asSummonInfo` matching the given pack index.
*   **JustSummoned**: Moves ambushers/burrowers near cave-in doors; joins Chomper to Grubbis; tracks summoned GUIDs.
*   **SummonedCreatureJustDied**: Removes dead GUID from list; sets instance data to DONE if Grubbis dies.
*   **IsPreparingExplosiveCharge**: Returns true if in phases 11, 13, 26, or 28.
*   **MoveInLineOfSight**: Ignores threats if preparing charges; otherwise delegates to parent.
*   **AttackStart**: Ignores threats if preparing charges; otherwise delegates to parent.
*   **AttackedBy**: Plays random aggro line once per combat.
*   **JustDied**: Sets instance data to FAIL, closes doors, and despawns adds.
*   **StartEvent**: Sets instance data to IN_PROGRESS, initializes phase timer, and stores player GUID.
*   **WaypointStart**: Opens cave-in doors at WPs 10 and 16; plays dialogue at WPs 12 and 16.
*   **WaypointReached**: Sets timers, triggers emotes, pauses escort, and faces objects at various WPs.
*   **UpdateEscortAI**: Main phase logic loop; handles dialogue, summoning, charge detonation, and combat.
*   **GetAI_npc_blastmaster_emi_shortfuse**: Factory function returning a new `npc_blastmaster_emi_shortfuseAI` instance.
*   **GossipHello_npc_blastmaster_emi_shortfuse**: Shows gossip menu if encounter is not started or failed.
*   **GossipSelect_npc_blastmaster_emi_shortfuse**: Starts the encounter via `StartEvent` if valid action selected.
*   **npc_kernobeeAI**: Initializes AI and calls `QuestReset()`.
*   **Reset#2**: Empty override.
*   **JustRespawned**: Sets immune flag and calls parent `JustRespawned`.
*   **UpdateFollowerAI**: Manages quest progression via `nextStep`, checking positions and triggering explosions.
*   **JustDied#2**: Calls parent `JustDied`, resets quest, and despawns bomb.
*   **QuestReset**: Resets internal state, timers, and stand state.
*   **StartQuest**: Initiates follow, retrieves bomb GUID, makes bomb follow, and starts quest logic.
*   **GetAI_npc_kernobee**: Factory function returning a new `npc_kernobeeAI` instance.
*   **QuestAccept_npc_kernobee**: Validates quest ID and calls `StartQuest` on the AI.
*   **OnInit**: Randomly selects one spell effect index for `GnomereganCollectingFalloutScript`.
*   **OnEffectExecute**: Returns true only if the current effect matches the randomly chosen one.
*   **GetScript_GnomereganCollectingFallout**: Factory function returning a new `GnomereganCollectingFalloutScript` instance.
*   **AddSC_gnomeregan**: Registers all scripts in this unit with the script manager.

---

<!-- machine-true, projected from graph.json -->

## Map — gnomeregan

*Source:* gnomeregan.cpp, gnomeregan.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| npc_blastmaster_emi_shortfuseAI | ctor | instance_gnomeregan/GetData, ScriptedEscortAI/npc_escortAI, WorldObject.Object/GetInstanceData, WorldObject.Object/SetUInt32Value | — | — |
| Reset | method | Creature.Main/EnableMoveInLosEvent, ScriptedEscortAI/HasEscortState | — | — |
| DoSummonPack | method | WorldObject.Object/SummonCreature#2 | — | — |
| JustSummoned | method | Creature.Main/JoinCreatureGroup, Creature.MotionMaster/MovePoint, instance_gnomeregan/GetData64, Map.Main/GetGameObject, Object/GetEntry, Object/GetObjectGuid, ObjectGuid/ObjectGuid#5, shared_Util/frand, Unit.Main/GetMotionMaster, WorldObject.Object/FindNearestCreature, WorldObject.Object/GetAngle, WorldObject.Object/GetMap, WorldObject.Object/GetNearPoint, WorldObject.Object/GetOrientation | — | — |
| SummonedCreatureJustDied | method | instance_gnomeregan/SetData, Object/GetEntry, Object/GetObjectGuid | — | — |
| IsPreparingExplosiveCharge | method | — | — | — |
| MoveInLineOfSight | method | ScriptedEscortAI/MoveInLineOfSight | — | — |
| AttackStart | method | CreatureAI/AttackStart | — | — |
| AttackedBy | method | ScriptMgr/DoScriptText, shared_Util/urand | — | — |
| JustDied | method | Creature.Main/ForcedDespawn, instance_gnomeregan/GetData64, instance_gnomeregan/SetData, Map.Main/GetCreature, ScriptedInstance/DoUseDoorOrButton, WorldObject.Object/GetMap | — | — |
| StartEvent | method | instance_gnomeregan/SetData, Object/GetObjectGuid | — | — |
| WaypointStart | method | instance_gnomeregan/GetData64, ScriptedInstance/DoUseDoorOrButton, ScriptMgr/DoScriptText | — | — |
| WaypointReached | method | instance_gnomeregan/GetData64, Map.Main/GetGameObject, ObjectGuid/ObjectGuid#5, ScriptedEscortAI/SetEscortPaused, ScriptMgr/DoScriptText, Unit.Main/HandleEmote, Unit.Main/SetFacingToObject, WorldObject.Object/GetMap | — | — |
| UpdateEscortAI | method | Creature.Main/SetFactionTemporary, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, instance_gnomeregan/GetData64, instance_gnomeregan/SetData, Map.Main/GetGameObject, Map.Main/GetPlayer, ObjectGuid/ObjectGuid#5, ScriptedEscortAI/SetEscortPaused, ScriptedEscortAI/Start, ScriptedInstance/DoUseDoorOrButton, ScriptMgr/DoScriptText, Unit.Main/GetVictim, Unit.Main/HandleEmote, Unit.Main/SelectHostileTarget, Unit.Main/SetFacingToObject, WorldObject.Object/FindNearestCreature, WorldObject.Object/GetMap, WorldObject.Object/SetUInt32Value | — | — |
| GetAI_npc_blastmaster_emi_shortfuse | function | — | — | — |
| GossipHello_npc_blastmaster_emi_shortfuse | function | GossipDef/AddMenuItem#5, GossipDef/SendGossipMenu, instance_gnomeregan/GetData, Object/GetObjectGuid, Player.Main/GetGossipTextId, PlayerMenu/GetGossipMenu, WorldObject.Object/GetInstanceData | — | — |
| GossipSelect_npc_blastmaster_emi_shortfuse | function | Creature.Main/AI, GossipDef/CloseGossip, instance_gnomeregan/GetData, WorldObject.Object/GetInstanceData | — | — |
| npc_kernobeeAI | ctor | ScriptedFollowerAI/FollowerAI | — | — |
| Reset#2 | method | — | — | — |
| JustRespawned | method | ScriptedFollowerAI/JustRespawned, WorldObject.Object/SetFlag | — | — |
| UpdateFollowerAI | method | Creature.Main/DisappearAndDie, Map.Main/GetCreature, ObjectGuid/ObjectGuid#5, Player.Main/GroupEventHappens, ScriptedFollowerAI/GetLeaderForFollower, ScriptedFollowerAI/HasFollowState, ScriptedFollowerAI/SetFollowComplete, ScriptedFollowerAI/SetFollowPaused, ScriptedFollowerAI/UpdateFollowerAI, ScriptMgr/DoScriptText, SpellCaster/CastSpell#2, Unit.Main/DealDamage, Unit.Main/GetHealth, Unit.Main/IsInCombat, Unit.Main/SetWalk, WorldObject.Object/FindNearestCreature, WorldObject.Object/GetMap, WorldObject.Object/IsWithinDist3d | — | — |
| JustDied#2 | method | Creature.Main/DisappearAndDie, Map.Main/GetCreature, ObjectGuid/ObjectGuid#5, ScriptedFollowerAI/JustDied, WorldObject.Object/GetMap | — | — |
| QuestReset | method | Unit.Main/SetStandState | — | — |
| StartQuest | method | Creature.MotionMaster/MoveFollow, instance_gnomeregan/GetData64, Map.Main/GetCreature, ObjectGuid/ObjectGuid#5, ScriptedFollowerAI/SetFollowPaused, ScriptedFollowerAI/StartFollow, ScriptMgr/DoScriptText, Unit.Main/GetMotionMaster, Unit.Main/SetStandState, Unit.Main/SetWalk, WorldObject.Object/GetInstanceData, WorldObject.Object/GetMap, WorldObject.Object/RemoveFlag | — | — |
| GetAI_npc_kernobee | function | — | — | — |
| QuestAccept_npc_kernobee | function | Creature.Main/AI, QuestDef/GetQuestId | — | — |
| OnInit | method | shared_Util/urand | — | — |
| OnEffectExecute | method | — | — | — |
| GetScript_GnomereganCollectingFallout | function | — | — | — |
| AddSC_gnomeregan | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
