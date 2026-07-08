# boss_onyxia

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# boss_onyxia

**Purpose & Responsibilities**

This unit implements the artificial intelligence and combat mechanics for **Onyxia**, a raid boss in the *Onyxia's Lair* instance, and her summoned minions, the **Onyxian Whelps**. It manages a complex, three-phase encounter involving ground-based melee combat, aerial flight patterns with directional breath attacks, and a final eruptive phase. The script handles phase transitions triggered by health thresholds, specific movement sequences (takeoff and landing), threat manipulation, and environmental hazards like heated ground. It also includes workarounds for engine limitations regarding aggro detection grids and spell chain stack overflows.

**Member-by-Member Behavior**

### Initialization and State Management

*   **boss_onyxiaAI**: The constructor initializes the AI instance. It retrieves the instance data via `WorldObject.Object/GetInstanceData` to track the encounter state and calls `Reset#2` to initialize timers and creature states.
*   **Reset#2**: Resets all internal timers to randomized or fixed values appropriate for Phase One. It sets the creature’s movement state to walking, disables flight and levitation, and sets the stand state to sleeping. It notifies the instance manager (`InstanceData/SetData`) that the event is `NOT_STARTED`.
*   **DelayEventIfNeed**: A utility helper that ensures a timer is not set to a value lower than a specified delay plus a 150ms increment, preventing timer starvation or immediate re-triggering.
*   **DelayCastEvents**: Applies `DelayEventIfNeed` to all major Phase One offensive timers (Flame Breath, Tail Sweep, Cleave, Wing Buffet, Knock Away) to create a cooldown window after a spell is cast.

### Aggro, Leashing, and Utility

*   **CheckForTargetsInAggroRadius**: A workaround for a grid bug where `MoveInLineOfSight` fails for players in the front of the chamber. It iterates through all players on the map (`Map.Main/GetPlayers`). If a player is within `ONYXIA_AGGRO_RANGE` (58.0f), targetable, and not stealthed, it removes stealth auras and initiates combat via `CreatureAI/AttackStart`.
*   **LeashIfOutOfCombatArea**: Checks if Onyxia’s X position is less than -95.0f. If so, it forces an evade (`EnterEvadeMode`), preventing the boss from leaving the intended combat zone.
*   **SummonPlayerIfOutOfReach**: Ensures the current victim remains within range. If Onyxia is flying and stationary, or if she is on the ground and the victim is more than 90.0f away, it teleports the victim to the center of the chamber using `Unit.Main/NearTeleportTo`.
*   **isOnyxiaFlying**: Returns true if the creature has the `SPELL_HOVER` aura, used to determine movement and teleportation logic.
*   **GetMoveData**: Looks up the current movement waypoint data from the static `aMoveData` array based on `m_uiMovePoint`.

### Combat Phases

*   **PhaseOne**: Handles ground-based combat.
    *   Ensures Onyxia is not flying.
    *   Casts **Flame Breath** (`SPELL_FLAMEBREATH`) on the victim on a 10–20s timer.
    *   Casts **Cleave** (`SPELL_CLEAVE`) on a 2–5s timer.
    *   Casts **Wing Buffet** (`SPELL_WINGBUFFET`) if the victim is in melee range, on a 15–30s timer.
    *   Casts **Knock Away** (`SPELL_KNOCK_AWAY`) if the victim is in melee range, reducing the victim's threat by 25% to prevent tank swaps from being instantly punished, on a 15–30s timer.
    *   Casts **Tail Sweep** (`SPELL_TAILSWEEP`) on a 3.5s timer.
    *   Performs melee attacks via `CreatureAI/DoMeleeAttackIfReady`.

*   **PhaseTwo**: Handles aerial combat and whelp summons.
    *   Manages movement between waypoints using `DoMovement`.
    *   If `m_bDeepBreathIsCasting` is true, it waits for a 5-second delay, then moves to the designated breath location, casts **Heated Ground** spells (`SPELL_HEATED_GROUND_EAST/WEST`), and clears the casting flag.
    *   Casts **Fireball** (`SPELL_FIREBALL`) on the victim while stationary, removing 100% of the victim's threat to force players to dodge or heal rather than tank.
    *   Summons **Onyxian Whelps** (`NPC_ONYXIAN_WHELP`) in pairs at two fixed locations until 16 whelps are spawned. Afterward, it enters a cooldown period before summoning 5–7 more.

*   **PhaseThree**: Handles the final eruptive phase.
    *   Casts **Bellowing Roar** (`SPELL_BELLOWINGROAR`) on a 15–30s timer.
    *   Summons individual whelps randomly at either spawn point on a 1–10s timer.
    *   Delegates to `PhaseOne` for standard melee abilities, effectively combining ground combat with the roar and whelp summons.

*   **PhaseTransition**: Manages the state machine for transitioning between phases.
    *   **To Phase Two (Takeoff)**: Moves Onyxia to a departure point, plays a lift-off emote, enables flight/levitation, and then moves her to the first aerial waypoint.
    *   **To Phase Three (Landing)**: Moves Onyxia to a landing point, plays a landing emote, disables flight, resets threat, and resumes chase movement.

*   **DoMovement**: Determines the next aerial movement action.
    *   Rolls a random number: 0–34 moves clockwise, 35–69 moves counter-clockwise, 70–99 triggers a **Deep Breath**.
    *   For Deep Breath: Sets the casting flag, delays movement by 5 seconds, casts the directional breath spell, faces the target location, and clears the current target.
    *   For normal movement: Sets speed and moves to the next waypoint.

*   **MovementInform**: Triggered when a movement point is reached.
    *   Restores the target GUID if the movement was part of Phase Two.
    *   Handles **Depart Flight**: Sets orientation, enables fly/levitate, and plays the lift-off emote.
    *   Handles **Landing Flight**: Sets orientation, disables fly/levitate, plays the land emote, and casts Bellowing Roar immediately upon landing.

*   **UpdateAI#2**: The main update loop.
    *   Checks for aggro radius targets.
    *   Selects a hostile target.
    *   Clears the target GUID during transitions, deep breaths, or Phase Two movement to prevent erratic chasing.
    *   Handles phase transitions based on health thresholds (<65% for P2, <40% for P3) if not already transitioning.
    *   Calls the active phase handler (`PhaseOne`, `PhaseTwo`, or `PhaseThree`).

### Minion AI

*   **OnyxianWhelpAI**: Constructor for the whelp AI.
*   **Reset**: Empty override.
*   **Aggro**: Sets the whelp into combat with the zone.
*   **UpdateAI**: Standard melee AI. Selects a target and performs melee attacks if ready.

### Registration

*   **GetAI_boss_onyxiaAI**: Factory function returning a new `boss_onyxiaAI` instance.
*   **GetAI_npc_onyxian_whelp**: Factory function returning a new `OnyxianWhelpAI` instance.
*   **AddSC_boss_onyxia**: Registers both scripts with the script manager.

**Cross-Unit Boundaries**

*   **InstanceData**: `boss_onyxiaAI` interacts with `InstanceData/SetData` to report the encounter status (`NOT_STARTED`, `IN_PROGRESS`, `DONE`) to the instance script (`instance_onyxia_lair`). This allows the instance to manage doors, events, and reset logic.
*   **ScriptedAI**: Inherits from `ScriptedAI`, utilizing its base functionality for timers, threat management, and spell casting helpers (`DoCastSpellIfCan`, `DoMeleeAttackIfReady`).
*   **CreatureAI/Unit/Main**: Extensive use of core engine methods for movement (`SetFly`, `SetLevitate`, `SetSpeedRate`, `MovePoint`), combat (`SetInCombatWithZone`, `AttackStart`, `GetVictim`), and state (`IsInCombat`, `IsAlive`, `HasAura`).
*   **ScriptMgr**: Uses `DoScriptText` to play sound/emote IDs for aggro, kills, phase transitions, and breath emotes.
*   **GridSearchers**: Uses `GetCreatureListWithEntryInGrid` to find and respawn Onyxian Warders on aggro, and to despawn whelps on evade.
*   **ThreatManager**: Directly manipulates threat percentages for Knock Away (-25%) and Fireball (-100%) to control tank engagement and player survival mechanics.

**Data Model**

This unit does not directly query or modify database tables. It relies on static configuration data defined in the source code (spell IDs, NPC entries, coordinates, timers) and runtime instance data. The commented-out SQL block at the end of the file provides context for spell targeting positions (`spell_target_position`) but is not executed by this C++ unit.

**Notable Implementation Details**

*   **Grid Aggro Workaround**: The comment in `CheckForTargetsInAggroRadius` explicitly states that `MoveInLineOfSight` fails for players in the front of the chamber due to a grid bug. The manual iteration over `Map::PlayerList` is a necessary workaround to ensure Onyxia aggroes properly.
*   **Spell Chain Stack Overflow**: The header comment notes that `SPELL_HEATED_GROUND` triggers a chain of 12 spells, which exceeds the default `MaxSpellCastsInChain`. The script mitigates this by relying on external configuration (disabling the 6th trigger index in `spell_effect_mod`) rather than handling it in code.
*   **Threat Manipulation**:
    *   **Knock Away**: Reduces threat by 25% to allow tanks to swap or recover without instantly losing aggro.
    *   **Fireball**: Removes 100% of threat, effectively making the target un-tankable for that hit, forcing players to rely on dodging or healing.
*   **Phase Transition Logic**: The transition to Phase Two involves a multi-step movement sequence (move to departure point -> lift off -> move to first waypoint) managed by `PhaseTransition` and `MovementInform`. Similarly, landing involves moving to a landing point, playing emotes, and resuming combat.
*   **Deep Breath Mechanic**: In Phase Two, Onyxia occasionally pauses to cast a powerful directional breath. This involves clearing her target, facing the destination, delaying movement, and casting the breath spell. This requires careful synchronization between `DoMovement`, `PhaseTwo`, and `MovementInform`.
*   **Whelp Summoning**: Whelps are summoned in pairs at two fixed locations. The script tracks the count and switches between a high-summon rate (16 whelps) and a lower rate (5–7 whelps) with a cooldown.
*   **Hardcoded Coordinates**: All movement points, spawn locations, and teleport targets are hardcoded floats. Any changes to the map geometry would require updating these values.

## Member Reference

**boss_onyxiaAI**: Constructor that initializes the AI, retrieves instance data, and calls `Reset#2`.
**Reset#2**: Resets all timers, sets creature state to sleeping/walking, and notifies the instance of the `NOT_STARTED` state.
**DelayEventIfNeed**: Helper to enforce a minimum timer delay.
**DelayCastEvents**: Applies `DelayEventIfNeed` to all Phase One offensive timers.
**CheckForTargetsInAggroRadius**: Workaround for grid aggro bugs; manually checks players in range and initiates combat.
**LeashIfOutOfCombatArea**: Forces evade if Onyxia moves too far west (X < -95.0f).
**SummonPlayerIfOutOfReach**: Teleports the victim to the chamber center if they are too far away.
**Aggro#2**: Plays aggro sound, sets combat state, notifies instance, and respawns dead Onyxian Warders.
**JustDied**: Notifies the instance that the event is `DONE`.
**EnterEvadeMode**: Despawns all Onyxian Whelps and calls the parent evade mode.
**JustSummoned**: Assigns a random target to the summoned whelp and increments the summon count.
**KilledUnit**: Plays a kill sound with a 50% chance if the victim is a player.
**isOnyxiaFlying**: Checks for the `SPELL_HOVER` aura.
**GetMoveData**: Retrieves movement data for the current waypoint from the static array.
**PhaseOne**: Handles ground combat abilities: Flame Breath, Cleave, Wing Buffet, Knock Away, Tail Sweep, and melee.
**PhaseTwo**: Handles aerial combat: movement, Deep Breath casting, Fireball (with threat removal), and whelp summoning.
**DoMovement**: Determines the next aerial move (clockwise, counter-clockwise, or Deep Breath) and executes it.
**PhaseThree**: Handles the final phase: Bellowing Roar, whelp summoning, and delegates to `PhaseOne` for melee.
**PhaseTransition**: Manages the state machine for takeoff (to P2) and landing (to P3), including movement and emotes.
**MovementInform**: Handles callbacks for reaching movement points, restoring targets, and playing lift-off/landing emotes.
**UpdateAI#2**: Main update loop; handles aggro checks, target selection, phase transitions, and calls the active phase handler.
**GetAI_boss_onyxiaAI**: Factory function for `boss_onyxiaAI`.
**OnyxianWhelpAI**: Constructor for the whelp AI.
**Reset**: Empty override for the whelp AI.
**Aggro**: Sets the whelp into combat with the zone.
**UpdateAI**: Standard melee AI for the whelp.
**GetAI_npc_onyxian_whelp**: Factory function for `OnyxianWhelpAI`.
**AddSC_boss_onyxia**: Registers both scripts with the script manager.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_onyxia

*Source:* boss_onyxia.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_onyxiaAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset#2 | method | CreatureAI/SetCombatMovement, InstanceData/SetData, shared_Util/urand, Unit.Main/SetFly, Unit.Main/SetLevitate, Unit.Main/SetSpeedRate, Unit.Main/SetStandState, Unit.Main/SetWalk | — | — |
| DelayEventIfNeed | method | — | — | — |
| DelayCastEvents | method | — | — | — |
| CheckForTargetsInAggroRadius | method | Creature.Main/AI, Creature.Main/IsInEvadeMode, Creature.Main/SetInCombatWithZone, CreatureAI/AttackStart, Map.Main/GetPlayers, Unit.Main/IsInCombat, Unit.Main/IsTargetableBy, Unit.Main/RemoveSpellsCausingAura, WorldObject.Object/GetMap, WorldObject.Object/IsWithinDistInMap | — | — |
| LeashIfOutOfCombatArea | method | WorldObject.Object/GetPositionX | — | — |
| SummonPlayerIfOutOfReach | method | Unit.Main/GetVictim, Unit.Main/NearTeleportTo, WorldObject.Object/GetDistance2d#3, WorldObject.Object/GetPositionX, WorldObject.Object/IsMoving | — | — |
| Aggro#2 | method | Creature.Main/Respawn, Creature.Main/SetInCombatWithZone, GridSearchers/GetCreatureListWithEntryInGrid#2, InstanceData/SetData, ScriptMgr/DoScriptText, Unit.Main/IsAlive, Unit.Main/SetStandState | — | — |
| JustDied | method | InstanceData/SetData | — | — |
| EnterEvadeMode | method | Creature.Main/ForcedDespawn, GridSearchers/GetCreatureListWithEntryInGrid#2, ScriptedAI/EnterEvadeMode | — | — |
| JustSummoned | method | Creature.Main/AI, Creature.Main/SelectAttackingTarget, CreatureAI/AttackStart | — | — |
| KilledUnit | method | Object/IsPlayer, ScriptMgr/DoScriptText, shared_Util/roll_chance_i | — | — |
| isOnyxiaFlying | method | Unit.Main/HasAura#2 | — | — |
| GetMoveData | method | — | — | — |
| PhaseOne | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, shared_Util/urand, ThreatManager/getThreat, ThreatManager/modifyThreatPercent#2, Unit.Main/CanReachWithMeleeAutoAttack, Unit.Main/GetThreatManager, Unit.Main/GetVictim, Unit.Main/SetFly, Unit.Main/SetLevitate, Unit.Main/SetWalk, WorldObject.Object/IsFlying | — | — |
| PhaseTwo | method | Creature.MotionMaster/MovePoint, CreatureAI/DoCastSpellIfCan, shared_Util/urand, SpellCaster/CastSpell#2, ThreatManager/getThreat, ThreatManager/modifyThreatPercent#2, Unit.Main/GetMotionMaster, Unit.Main/GetThreatManager, Unit.Main/GetVictim, Unit.Main/IsStopped, Unit.Main/SetSpeedRate, WorldObject.Object/SummonCreature#2 | — | — |
| DoMovement | method | Creature.MotionMaster/MovePoint, ObjectGuid/ObjectGuid, ScriptMgr/DoScriptText, shared_Util/urand, SpellCaster/CastSpell#2, SpellCaster/InterruptNonMeleeSpells, Unit.Main/GetMotionMaster, Unit.Main/SetFacingTo, Unit.Main/SetSpeedRate, Unit.Main/SetTargetGuid, WorldObject.Object/GetAngle#2 | — | — |
| PhaseThree | method | CreatureAI/DoCastSpellIfCan, shared_Util/urand, WorldObject.Object/SummonCreature#2 | — | — |
| PhaseTransition | method | Creature.MotionMaster/MoveChase, Creature.MotionMaster/MoveIdle, Creature.MotionMaster/MovePoint, CreatureAI/SetCombatMovement, MotionMaster/Clear, Object/GetObjectGuid, ScriptedAI/DoResetThreat, ScriptMgr/DoScriptText, shared_Util/urand, SpellCaster/CastSpell#2, SpellCaster/InterruptNonMeleeSpells, Unit.Main/ClearUnitState, Unit.Main/GetMotionMaster, Unit.Main/GetVictim, Unit.Main/RemoveAurasDueToSpell, Unit.Main/SetSpeedRate, Unit.Main/SetTargetGuid, WorldObject.Object/GetPositionX | — | — |
| MovementInform | method | Object/GetObjectGuid, shared_Util/urand, SpellCaster/CastSpell#2, Unit.Main/GetVictim, Unit.Main/HandleEmote, Unit.Main/SetFly, Unit.Main/SetLevitate, Unit.Main/SetTargetGuid, WorldObject.Object/SetOrientation | — | — |
| UpdateAI#2 | method | ObjectGuid/ObjectGuid, Unit.Main/GetHealthPercent, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget, Unit.Main/SetTargetGuid, WorldObject.Object/IsMoving | — | — |
| GetAI_boss_onyxiaAI | function | — | — | — |
| OnyxianWhelpAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset | method | — | — | — |
| Aggro | method | Creature.Main/SetInCombatWithZone | — | — |
| UpdateAI | method | CreatureAI/DoMeleeAttackIfReady, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_npc_onyxian_whelp | function | — | — | — |
| AddSC_boss_onyxia | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
