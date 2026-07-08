# wailing_caverns

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# wailing_caverns

## Purpose & Responsibilities

The `wailing_caverns` translation unit implements the scripted artificial intelligence for two specific creatures within the Wailing Caverns dungeon instance: **Disciple of Naralex** (`npc_disciple_of_naralex`) and **Evolving Ectoplasm** (`npc_evolving_ectoplasm`).

1.  **Disciple of Naralex**: This is a complex escort and event-driven AI. It guides players through a series of waypoints, triggering dialogue, combat encounters, and ritual phases. It coordinates closely with another boss, **Naralex**, managing synchronized movements, spell casts, and summoning mechanics. The script handles the entire sequence from the initial escort start to the final flight escape of both the Disciple and Naralex.
2.  **Evolving Ectoplasm**: This is a reactive mob AI that changes its elemental resistance and appearance based on the type of damage it receives (Fire, Frost, Nature, Shadow). It becomes immune to the damaging school for a short duration after being hit by it.

The unit does not interact with any database tables directly; all state management is handled via the `ScriptedInstance` interface provided by the core engine.

## Member-by-Member Behavior

### Disciple of Naralex AI (`npc_disciple_of_naralexAI`)

This class inherits from `npc_escortAI`, providing base functionality for waypoint-based movement and player following.

#### Initialization and State Management
*   **ctor**: Initializes the AI, retrieving the instance data pointer (`m_pInstance`) from the creature's context and calling `Reset`.
*   **Reset#2**: Resets all internal timers (`Event_Timer`, `Sleep_Timer`, `Potion_Timer`, `Cleansing_Timer`), phase counters (`Point`, `Subevent_Phase`), and boolean flags (`Yelled`, `isAggro`) to their initial values. This ensures the script starts cleanly if the creature despawns and respawns or if the instance resets.
*   **EnterEvadeMode**: Called when the creature evades combat. It clears threat lists and stops combat. If the creature was aggroed during a critical phase (fight or cast waypoint), it returns to the combat start position. Otherwise, it resumes the escort path unless paused.
*   **Aggro**: Sets the `isAggro` flag and pauses the escort path if the creature is attacked, unless it is in a critical phase where pausing is inappropriate.

#### Waypoint and Movement Logic
*   **OnFightWaypoint**: Returns `true` if the current waypoint index (`Point`) is 7, indicating a combat encounter phase where standard evade/resume logic should be suppressed.
*   **OnCastWaypoint**: Returns `true` if the creature is at waypoint 30 (awakening ritual) or waypoint 15 during specific sub-phases (cleansing ritual). These are critical moments where the escort path must remain paused regardless of combat status.
*   **WaypointReached**: Triggered when the creature arrives at a waypoint. It uses a switch statement on the waypoint index (`i`) to trigger specific events:
    *   **WP 0**: Starts the escort, plays dialogue, and sets up the initial event timer.
    *   **WP 7**: Triggers the first trash mob encounter (raptors). Pauses escort and sets up sub-event phases.
    *   **WP 15**: Triggers the cleansing ritual. Pauses escort.
    *   **WP 26**: Faces a specific direction, plays dialogue, and prepares for the next phase.
    *   **WP 30**: Faces Naralex, marks the disciple event as in-progress, and begins the awakening ritual sequence.
*   **MovementInform**: Handles movement completion notifications. Specifically, it manages the final flight sequence (waypoints 33-38) where both the Disciple and Naralex fly away together. At waypoint 38, both creatures are despawned with a 12-hour respawn time.
*   **JustSummoned**: Registers newly summoned creatures in the `vSummoned` vector and sets them to idle movement.
*   **JustDied**: Iterates through the `vSummoned` vector and forces the despawn of any still-alive summons, cleaning up the battlefield.
*   **SummonedCreatureJustDied**: Removes the GUID of a dead summoned creature from the `vSummoned` vector to keep the list accurate.

#### Combat and Summoning Mechanics
*   **SummonAttacker**: Helper function to summon a creature at specific coordinates with a temporary summon type that despawns on death.
*   **SendAttackerToMe**: Directs a summoned creature to move towards the Disciple. It adjusts the movement mode (run vs. walk) based on the creature's entry ID.
*   **AttackedBy**: Overrides the default attack response. It prevents the Disciple from reacting to attacks while casting the cleansing spell (`SPELL_CLEANSING`). It also plays a specific dialogue line if attacked outside of the ritual phase.
*   **UpdateEscortAI**: The main update loop. It performs several tasks:
    *   **Summon Management**: Checks if summoned creatures have reached the Disciple or are idle, directing them to attack or move closer.
    *   **Event Timer**: Drives the scripted sequence using `Event_Timer` and `Subevent_Phase`. It handles dialogue, spell casts, summoning trash mobs, and coordinating with Naralex.
    *   **Healing**: Casts a healing potion if health drops below 80%.
    *   **Combat**: If in combat and not in a critical phase, it selects targets and casts sleep spells periodically, then performs melee attacks.

#### Script Event Integration
*   **OnScriptEventHappened**: Listens for external script events. If the instance signals that the Disciple event is special (likely triggered by a player interaction elsewhere), it starts the escort path without range checks, records the player's GUID, and sets the faction to neutral-active.

#### Factory Function
*   **GetAI_npc_disciple_of_naralex**: Creates and returns a new instance of `npc_disciple_of_naralexAI`.

### Evolving Ectoplasm AI (`EvolvingEctoplasmAI`)

This class inherits from `ScriptedAI`, providing basic AI functionality.

#### Initialization and State Management
*   **ctor**: Initializes the AI and calls `Reset`.
*   **Reset**: Removes all auras from the creature and resets the immunity timer and flag.

#### Reactive Mechanics
*   **SpellHit**: Triggered when the creature is hit by a spell. If not currently immune, it checks the spell's school:
    *   **Frost**: Transforms to blue, gains frost immunity.
    *   **Fire**: Transforms to red, gains fire immunity.
    *   **Nature**: Transforms to green, gains nature immunity.
    *   **Shadow**: Transforms to black, gains shadow immunity.
    In each case, it sets a 10-second immunity timer and flags itself as immune.
*   **UpdateAI**: The main update loop. It decrements the immunity timer. If the timer expires, it removes all transformation and immunity auras and resets the immune flag. It then proceeds with standard hostile target selection and melee attacks.

#### Factory Function
*   **GetAI_EvolvingEctoplasmAI**: Creates and returns a new instance of `EvolvingEctoplasmAI`.

### Script Registration
*   **AddSC_wailing_caverns**: Registers both AI scripts with the script manager, making them available for use by creatures with the corresponding entries.

## Cross-Unit Boundaries

### Disciple of Naralex
*   **Calls `ScriptedEscortAI`**: Inherits base escort functionality. Uses `ReturnToCombatStartPosition`, `SetEscortPaused`, `SetRun`, `Stop`, and `Start` to manage the escort path.
*   **Calls `WorldObject.Object`**: Uses `GetInstanceData` to retrieve the instance data pointer.
*   **Calls `Unit.Main`**: Uses various methods for combat management (`CombatStop`, `DeleteThreatList`, `SelectHostileTarget`, `SelectAttackingTarget`, `GetVictim`, `IsAlive`, `IsInCombat`), movement (`SetFacingTo`, `SetFacingToObject`, `SetFly`, `SetWalk`, `GetMotionMaster`), and state management (`AddUnitState`, `ClearUnitState`, `RemoveAurasDueToSpell`, `InterruptNonMeleeSpells`, `GetHealth`, `GetMaxHealth`).
*   **Calls `InstanceData`**: Uses `GetData64` to retrieve Naralex's GUID and `GetData`/`SetData` to track the progress of the Disciple and Mutanus events.
*   **Calls `ScriptMgr`**: Uses `DoScriptText` to play dialogue lines.
*   **Calls `ZoneScript`**: Uses `GetCreature` to retrieve Naralex and other summoned creatures from the instance.
*   **Calls `Creature.Main`**: Uses `ForcedDespawn`, `SetRespawnTime`, `SetVisibility`, `AI`, `SelectAttackingTarget`, and `MovePoint` to manage summoned creatures and Naralex.
*   **Calls `Creature.MotionMaster`**: Uses `MovePoint` and `MoveIdle` to control movement.
*   **Calls `ObjectGuid`**: Uses `ObjectGuid#5` to handle GUID conversions.
*   **Calls `Map.Main`**: Uses `GetPlayer`, `GetPlayers`, `IsDungeon`, and `GetMap` to access player information and map context.
*   **Calls `LinkedListHead`**: Uses `isEmpty` to check if the player list is empty.
*   **Calls `SpellCaster`**: Uses `CastSpell`, `GetCurrentSpell`, and `InterruptNonMeleeSpells` to manage spell casting.
*   **Calls `CreatureAI`**: Uses `AttackedBy` and `DoMeleeAttackIfReady` for base combat behavior.

### Evolving Ectoplasm
*   **Calls `ScriptedAI`**: Inherits base AI functionality.
*   **Calls `Unit.Main`**: Uses `RemoveAllAuras`, `RemoveAurasDueToSpell`, `SelectHostileTarget`, and `GetVictim` for aura and combat management.
*   **Calls `CreatureAI`**: Uses `DoCastSpellIfCan` and `DoMeleeAttackIfReady` for spell and attack execution.

## Data Model

This unit does not interact with any database tables directly. All state is managed in-memory via the `ScriptedInstance` interface and local variables.

## Notable Implementation Details

*   **Synchronized Flight**: The final phase involves both the Disciple and Naralex flying away together. The `MovementInform` method carefully coordinates their movement points, ensuring they stay close and despawn simultaneously at the end of the sequence.
*   **Immunity Window**: The Evolving Ectoplasm's immunity is temporary (10 seconds). This allows players to switch damage types to bypass the immunity, adding a strategic element to the fight.
*   **Summon Tracking**: The Disciple maintains a vector of summoned creature GUIDs (`vSummoned`) to manage their lifecycle. This is crucial for cleaning up summons when the Disciple dies or when they are no longer needed.
*   **Phase-Based Logic**: The Disciple's behavior is heavily driven by the `Point` (waypoint index) and `Subevent_Phase` variables. This allows for complex, multi-step sequences to be implemented within a single AI class.
*   **Critical Phase Protection**: The `OnFightWaypoint` and `OnCastWaypoint` methods ensure that certain phases (like the cleansing ritual) cannot be interrupted by standard evade/resume logic, maintaining the integrity of the scripted event.
*   **Health-Based Healing**: The Disciple automatically casts a healing potion if its health drops below 80%, ensuring it survives long enough to complete its scripted duties.

## Member Reference

**npc_disciple_of_naralexAI** (ctor): Initializes the AI, retrieves instance data, and calls Reset.
**Reset#2**: Resets all timers, phase counters, and flags to initial values.
**EnterEvadeMode**: Clears threat, stops combat, and resumes escort unless in a critical phase.
**Aggro**: Sets aggro flag and pauses escort unless in a critical phase.
**OnFightWaypoint**: Returns true if at waypoint 7 (combat encounter).
**OnCastWaypoint**: Returns true if at waypoint 30 or 15 (ritual phases).
**WaypointReached**: Triggers events based on the current waypoint index.
**MovementInform**: Handles movement completion, including the final flight sequence.
**JustSummoned**: Registers summoned creatures and sets them to idle.
**JustDied**: Despawns all alive summoned creatures.
**SummonedCreatureJustDied**: Removes dead summoned creatures from the tracking vector.
**SummonAttacker**: Summons a creature at specific coordinates.
**SendAttackerToMe**: Directs a summoned creature to move towards the Disciple.
**AttackedBy**: Prevents reaction during cleansing spell and plays dialogue.
**UpdateEscortAI**: Main update loop handling events, summons, healing, and combat.
**OnScriptEventHappened**: Starts the escort path if triggered by an external event.
**GetAI_npc_disciple_of_naralex**: Factory function for the Disciple AI.
**EvolvingEctoplasmAI** (ctor): Initializes the AI and calls Reset.
**Reset**: Removes auras and resets immunity state.
**SpellHit**: Reacts to damage by transforming and gaining immunity.
**UpdateAI**: Manages immunity timer and performs melee attacks.
**GetAI_EvolvingEctoplasmAI**: Factory function for the Ectoplasm AI.
**AddSC_wailing_caverns**: Registers both AI scripts with the script manager.

---

<!-- machine-true, projected from graph.json -->

## Map — wailing_caverns

*Source:* wailing_caverns.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| npc_disciple_of_naralexAI | ctor | ScriptedEscortAI/npc_escortAI, WorldObject.Object/GetInstanceData | — | — |
| Reset#2 | method | — | — | — |
| EnterEvadeMode | method | ScriptedEscortAI/ReturnToCombatStartPosition, ScriptedEscortAI/SetEscortPaused, Unit.Main/CombatStop, Unit.Main/DeleteThreatList | — | — |
| Aggro | method | ScriptedEscortAI/SetEscortPaused | — | — |
| OnFightWaypoint | method | — | — | — |
| OnCastWaypoint | method | — | — | — |
| WaypointReached | method | InstanceData/GetData64, InstanceData/SetData, ObjectGuid/ObjectGuid#5, ScriptedEscortAI/SetEscortPaused, ScriptMgr/DoScriptText, Unit.Main/HandleEmoteCommand, Unit.Main/SetFacingTo, Unit.Main/SetFacingToObject, ZoneScript/GetCreature | — | — |
| MovementInform | method | Creature.Main/ForcedDespawn, Creature.Main/SetRespawnTime, Creature.MotionMaster/MovePoint, InstanceData/GetData64, ObjectGuid/ObjectGuid#5, ScriptedEscortAI/MovementInform, Unit.Main/GetMotionMaster, Unit.Main/SetVisibility, ZoneScript/GetCreature | — | — |
| JustSummoned | method | Creature.MotionMaster/MoveIdle, Object/GetGUID, Unit.Main/GetMotionMaster | — | — |
| JustDied | method | Creature.Main/ForcedDespawn, ObjectGuid/ObjectGuid#5, Unit.Main/IsAlive, ZoneScript/GetCreature | — | — |
| SummonedCreatureJustDied | method | Object/GetGUID | — | — |
| SummonAttacker | method | WorldObject.Object/SummonCreature#2 | — | — |
| SendAttackerToMe | method | Creature.MotionMaster/MovePoint, Object/GetEntry, Unit.Main/GetMotionMaster, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ | — | — |
| AttackedBy | method | CreatureAI/AttackedBy, ScriptMgr/DoScriptText, SpellCaster/GetCurrentSpell | — | — |
| UpdateEscortAI | method | Creature.Main/AI, Creature.Main/SelectAttackingTarget, Creature.MotionMaster/MovePoint, CreatureAI/AttackStart, CreatureAI/DoMeleeAttackIfReady, InstanceData/GetData, InstanceData/GetData64, InstanceData/SetData, LinkedListHead/isEmpty, Map.Main/GetPlayer, Map.Main/GetPlayers, Map.Main/IsDungeon, ObjectGuid/ObjectGuid#5, ScriptedEscortAI/SetEscortPaused, ScriptedEscortAI/SetRun, ScriptedEscortAI/Stop, ScriptMgr/DoScriptText, SpellCaster/CastSpell#2, SpellCaster/GetCurrentSpell, SpellCaster/InterruptNonMeleeSpells, Unit.Main/AddUnitState, Unit.Main/ClearUnitState, Unit.Main/CombatStop, Unit.Main/DeleteThreatList, Unit.Main/GetHealth, Unit.Main/GetMaxHealth, Unit.Main/GetMotionMaster, Unit.Main/GetVictim, Unit.Main/HandleEmoteCommand, Unit.Main/IsAlive, Unit.Main/IsInCombat, Unit.Main/RemoveAurasDueToSpell, Unit.Main/SelectHostileTarget, Unit.Main/SetFacingTo, Unit.Main/SetFacingToObject, Unit.Main/SetFly, Unit.Main/SetWalk, WorldObject.Object/FindNearestCreature, WorldObject.Object/GetDistance#3, WorldObject.Object/GetDistance2d#3, WorldObject.Object/GetMap, WorldObject.Object/HasUnitMovementFlag, WorldObject.Object/IsWalking, WorldObject.Object/SetByteValue, ZoneScript/GetCreature | — | — |
| OnScriptEventHappened | method | InstanceData/GetData, Object/GetObjectGuid, Object/IsPlayer, ScriptedEscortAI/Start, Unit.Main/SetFactionTemplateId | — | — |
| GetAI_npc_disciple_of_naralex | function | — | — | — |
| EvolvingEctoplasmAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset | method | Unit.Main/RemoveAllAuras | — | — |
| SpellHit | method | CreatureAI/DoCastSpellIfCan | — | — |
| UpdateAI | method | CreatureAI/DoMeleeAttackIfReady, Unit.Main/GetVictim, Unit.Main/RemoveAurasDueToSpell, Unit.Main/SelectHostileTarget | — | — |
| GetAI_EvolvingEctoplasmAI | function | — | — | — |
| AddSC_wailing_caverns | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
