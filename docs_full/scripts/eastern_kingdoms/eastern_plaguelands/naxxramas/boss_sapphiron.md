# boss_sapphiron

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# boss_sapphiron

**Purpose & Responsibilities**
This translation unit implements the artificial intelligence and game logic for **Sapphiron**, the final boss of the Naxxramas raid instance in the Frostwyrm Lair wing. It handles four distinct entities:
1.  **Sapphiron (Boss):** A complex multi-phase AI that transitions between ground combat, aerial flight, and landing sequences. It manages specific abilities like Icebolts, Frost Breath, Blizzard summons, and Life Drain, while enforcing movement constraints (hovering, melee Z-limits) and enrage timers.
2.  **Sapphiron's Blizzard (NPC):** A summoned creature that wanders the arena, targeting players to deal area-of-effect damage. Its movement logic prioritizes spreading out among players rather than clustering on the tank.
3.  **Sapphiron's Birth (GameObject):** The interactive object that initiates the encounter by summoning the boss when clicked by a valid player.
4.  **Life Drain Spell Script:** A custom modification to the Life Drain spell to randomize the number of targets affected.

The unit relies heavily on the `instance_naxxramas` script for state management (tracking whether the boss is alive, dead, or in progress) and uses `EventMap` for timing abilities. It contains no direct database interactions; all data is derived from in-memory objects and instance scripts.

## Member-by-Member Behavior

### Sapphiron Boss AI (`boss_sapphironAI`)

#### Initialization and State Management
*   **`boss_sapphironAI`**: Constructor initializes the AI, retrieves the `instance_naxxramas` script pointer, sets a forced target update timer (to fix a client-side visibility bug), and calls `Reset`.
*   **`Reset`**: Resets the boss to the ground phase. It clears all event schedules, resets the 15-minute enrage timer, removes specific frost auras, despawns wing buffet creatures and ice blocks, disables hover mode, and enables combat movement. It also notifies the instance script that the encounter is `NOT_STARTED` if it wasn't already `DONE`.
*   **`setHover`**: A helper method to toggle Sapphiron's flight state.
    *   *On*: Interrupts non-melee spells, stops attacks, removes attackers, sets react state to passive, plays a lift-off emote, enables hover, clears temporary factions, and sets the melee Z-limit to 0 (preventing melee attacks while flying).
    *   *Off*: Removes specific auras, plays a landing emote if hovering, disables hover, and if not resetting, re-enables combat state and aggressive react state, restoring the default melee Z-limit.

#### Ability Logic
*   **`DoIceBolt`**: Handles the casting of Icebolts during the air phase. It selects a random player from the threat list who is alive and hasn't been targeted recently (tracked in `iceboltTargets`). It faces the target, casts the spell, and records the target GUID. If no suitable targets remain, it reschedules the next action.
*   **`RescheduleIcebolt`**: Manages the sequence of Icebolts. It casts up to 5 Icebolts in succession. After the 5th bolt, it schedules the `EVENT_FROST_BREATH_DUMMY` event to begin the Frost Breath sequence.
*   **`UnSummonWingBuffet`**: Despawn the temporary "Wing Buffet" creature summoned during flight. It looks up the creature via the instance script using the stored GUID and calls `UnSummon` on it.
*   **`DeleteAndDispellIceBlocks`**: Finds all GameObjects with entry `GO_ICEBLOCK` within 300 yards of the boss and marks them for deletion. This cleans up visual effects from previous phases.

#### Movement and Combat Helpers
*   **`DamageTaken`**: Checks if the boss has a melee Z-limit less than 1.0 (indicating it is flying/hovering). If so, and if the attacker can reach the boss with a melee attack, it sends a command to stop the melee attack. This prevents players from "attacking" a flying boss visually.
*   **`AttackStart`**: Only allows aggro generation if the boss is in the `PHASE_GROUND` phase.
*   **`Aggro`**: Triggers when combat starts. It notifies the instance script that the encounter is `IN_PROGRESS` and schedules the initial ground-phase abilities (Life Drain, Blizzard, Tail Sweep, Cleave) and the timer for lifting off (`EVENT_MOVE_TO_FLY`).
*   **`JustDied`**: Plays the death spell (camera shake), cleans up wing buffets and ice blocks, and notifies the instance script that the encounter is `DONE`.
*   **`MovementInform`**: Triggered when movement points are reached. Specifically, when reaching `MOVE_POINT_LIFTOFF`, it schedules the `EVENT_LIFTOFF` event shortly after.
*   **`UpdateReachable`**: Monitors if the boss's current victim is unreachable (out of chase distance, no line of sight, or motion master reports unreachable). If the target remains unreachable for more than 10 seconds, the boss will evade (reset).

#### Main Update Loop (`UpdateAI`)
*   **`UpdateAI`**: The core tick loop.
    *   *Ground Phase*: Checks for a hostile target. If none, returns. Updates reachability. If unreachable for >10s, evades. Forces a target value update for clients if the timer expires.
    *   *Air/Landing Phases*: If the threat list is empty, evades.
    *   *Aura Maintenance*: Ensures `SPELL_FROST_AURA` is always active.
    *   *Event Execution*: Processes scheduled events:
        *   `EVENT_MOVE_TO_FLY`: If health > 10%, moves to the lift-off position.
        *   `EVENT_LIFTOFF`: Transitions to Air Bolts phase, summons Wing Buffet, enables hover.
        *   `EVENT_LAND`: Clears icebolt targets, checks if Frost Breath is still casting, disables hover, schedules landing completion.
        *   `EVENT_LANDED`: Cleans up ice blocks, resets events, schedules ground-phase abilities, re-enables combat movement, and selects a new target.
        *   `EVENT_ICEBOLT`: Calls `DoIceBolt`.
        *   `EVENT_FROST_BREATH_DUMMY`: Unsummons Wing Buffet, casts the dummy visual spell, then schedules the actual damage spell.
        *   `EVENT_FROST_BREATH_CAST`: Casts the actual Frost Breath damage spell, then schedules landing.
        *   `EVENT_BLIZZARD`: Summons a Blizzard NPC near a random player.
        *   `EVENT_LIFEDRAIN`, `EVENT_TAIL_SWEEP`, `EVENT_CLEAVE`: Cast respective spells and reschedule.
    *   *Enrage*: Checks the `berserkTimer`. If expired, casts Berserk, plays an emote, and resets the timer to 5 minutes.
    *   *Melee*: If in ground phase, performs melee attacks if ready.

### Blizzard NPC AI (`npc_sapphiron_blizzardAI`)

*   **`npc_sapphiron_blizzardAI`**: Constructor sets wander distance, passive react state, and schedules the first target pick event.
*   **`Reset#2`**: An empty override of the base `Reset` method to prevent standard AI reset behaviors for this passive NPC.
*   **`JustRespawned`**: An empty override to handle respawn events without side effects.
*   **`AttackStart#2`**: An empty override to prevent the blizzard from initiating combat on its own.
*   **`MoveInLineOfSight`**: An empty override to prevent line-of-sight aggro.
*   **`Aggro#2`**: An empty override to prevent the blizzard from gaining threat or entering combat state.
*   **`MovementInform#2`**: Triggered when a movement point is reached. It resets the event map, calls `PickNewTarget` to determine the next destination, and schedules the next target selection event.
*   **`SetRandomMove`**: Sets the motion master to random wandering if it isn't already, used when no valid targets are available.
*   **`PickNewTarget`**: Logic to determine where the blizzard moves.
    *   If Sapphiron is dead or has fewer than 2 targets (only tank alive), it moves randomly.
    *   Otherwise, it iterates through Sapphiron's threat list, skipping the tank and previously targeted players. It prefers players who are at least 15 yards away to encourage spreading.
    *   If a suitable player is found, it moves to them. If not, it moves randomly.
*   **`UpdateAI#2`**: The main update loop for the blizzard. It updates the event map and, when an event fires, calls `PickNewTarget` and reschedules the next check in 8–10 seconds.

### Encounter Initiation (`sapphiron_birthAI`)

*   **`sapphiron_birthAI`**: Constructor retrieves the instance script.
*   **`OnUse`**: Triggered when a player clicks the birth object.
    *   Validates the user is a living, non-GM player.
    *   Checks if the encounter is `NOT_STARTED` and Sapphiron isn't already spawned.
    *   Sets the instance data to `SPECIAL` (likely a transitional state).
    *   Plays the despawn animation on the object itself (visual cue for summoning).
    *   Returns true to consume the use.

### Script Registration

*   **`GetAI_boss_sapphiron`**, **`GetAI_npc_sapphironBlizzard`**, **`GetAI_sapphiron_birth`**: Factory functions returning new instances of the respective AI classes.
*   **`GetScript_SapphironLifeDrain`**: Factory function for the Life Drain spell script.
*   **`AddSC_boss_sapphiron`**: Registers all four scripts (Boss, Blizzard, Spell, GameObject) with the script manager.

### Spell Script (`SapphironLifeDrainScript`)

*   **`OnSetTargetMap`**: Intercepts the target selection for Life Drain. It randomizes the maximum number of targets between 7 and 10, adding variability to the spell's impact.

## Cross-Unit Boundaries

*   **`instance_naxxramas.Main`**:
    *   *Called by*: `boss_sapphironAI::Reset`, `Aggro`, `JustDied`, `sapphiron_birthAI::OnUse`.
    *   *Collaboration*: The boss AI reads and writes the encounter state (`TYPE_SAPPHIRON`) to coordinate with the instance script. The birth object also checks and sets this state.
*   **`ScriptedAI` / `CreatureAI` / `GameObjectAI`**:
    *   *Called by*: All AI constructors and methods.
    *   *Collaboration*: Inherits base AI functionality (event handling, movement, combat states).
*   **`WorldObject.Object` / `Unit.Main` / `Creature.Main`**:
    *   *Called by*: Various methods for positioning, facing, casting spells, checking distances, and managing auras/movement flags.
    *   *Collaboration*: Standard engine interactions for entity manipulation.
*   **`EventMap`**:
    *   *Called by*: `boss_sapphironAI` and `npc_sapphiron_blizzardAI`.
    *   *Collaboration*: Used to schedule and execute timed events (abilities, phase transitions).
*   **`Shared_Util`**:
    *   *Called by*: `DoIceBolt`, `UpdateAI`, `PickNewTarget`, `OnSetTargetMap`.
    *   *Collaboration*: Provides random number generation (`urand`, `frand`).
*   **`ThreatManager`**:
    *   *Called by*: `DoIceBolt`, `UpdateAI`, `PickNewTarget`.
    *   *Collaboration*: Accesses the threat list to select targets for abilities and movement.
*   **`ScriptMgr`**:
    *   *Called by*: `UpdateAI` (via `DoScriptText`).
    *   *Collaboration*: Plays emotes/sayings.
*   **`ScriptLoader`**:
    *   *Calls*: `AddSC_boss_sapphiron`.
    *   *Collaboration*: Loads the scripts into the server at startup.

## Data Model

This unit does not interact directly with any database tables. All state is managed in-memory via the `instance_naxxramas` script and entity objects.

## Notable Implementation Details

1.  **Phase-Based Logic**: The boss AI is strictly divided into phases (`PHASE_GROUND`, `PHASE_LIFT_OFF`, `PHASE_AIR_BOLTS`, `PHASE_AIR_BREATH`, `PHASE_LANDING`). Abilities and movement behaviors are gated by these phases. For example, `AttackStart` ignores aggro if not in ground phase.
2.  **Hover/Mechanics Hack**: The `setHover` method manually manipulates many internal states (react state, combat state, melee Z-limit, temporary factions) to simulate flight. This is necessary because the engine's native flight mechanics might not fully support the desired behavior (e.g., preventing melee attacks while airborne).
3.  **Icebolt Target Tracking**: `DoIceBolt` maintains a `std::vector<ObjectGuid> iceboltTargets` to ensure it doesn't spam the same player repeatedly. This vector is cleared upon landing.
4.  **Blizzard Spreading Logic**: The Blizzard NPC's `PickNewTarget` explicitly skips the tank and players who were recently targeted, and prefers players further away. This is designed to force players to spread out to avoid clustered AoE damage.
5.  **Forced Target Update**: In `UpdateAI`, there is a `m_forceTargetUpdateTimer` that forces a network update of the boss's target field. This is noted in comments as a hack to fix a client-side bug where players couldn't see the boss's target after initial summoning.
6.  **Enrage Timer**: The enrage timer (`berserkTimer`) is independent of phases and checks every tick. It resets to 5 minutes after triggering.
7.  **Life Drain Randomization**: The spell script modifies the target count dynamically, ensuring the spell affects a variable number of players (7-10) rather than a fixed amount.
8.  **Manual Cleanup**: `DeleteAndDispellIceBlocks` and `UnSummonWingBuffet` are called during reset and death to ensure no lingering visual effects or NPCs remain from previous attempts or phases.

## Member Reference

**boss_sapphironAI**: Constructor for the boss AI; initializes timers, retrieves instance data, and calls `Reset`.
**Reset**: Resets boss state to ground phase, clears events/aures/NPCs, and updates instance data.
**UnSummonWingBuffet**: Despawn the temporary wing buffet creature using its stored GUID.
**DeleteAndDispellIceBlocks**: Finds and deletes all ice block GameObjects within 300 yards.
**DamageTaken**: Prevents melee attacks from hitting the boss while it is hovering/flying.
**AttackStart**: Allows aggro only if the boss is in the ground phase.
**Aggro**: Starts the encounter, notifies instance, and schedules initial ground-phase events.
**JustDied**: Plays death animation, cleans up NPCs/effects, and marks encounter as done.
**RescheduleIcebolt**: Manages the count of Icebolts; switches to Frost Breath after 5 bolts.
**DoIceBolt**: Selects a random, previously untargeted player and casts Icebolt.
**MovementInform**: Triggers `EVENT_LIFTOFF` when the lift-off movement point is reached.
**setHover**: Toggles flight state, adjusting react state, melee limits, and emotes.
**UpdateReachable**: Tracks if the current target is unreachable; triggers evade if stuck >10s.
**UpdateAI**: Main loop handling phase transitions, ability casting, enrage, and melee attacks.
**npc_sapphiron_blizzardAI**: Constructor for the Blizzard NPC; sets passive state and initial event.
**Reset#2**: Empty override for the Blizzard NPC to suppress default reset behavior.
**JustRespawned**: Empty override for the Blizzard NPC.
**AttackStart#2**: Empty override for the Blizzard NPC to prevent self-initiated combat.
**MoveInLineOfSight**: Empty override for the Blizzard NPC to prevent LOS aggro.
**Aggro#2**: Empty override for the Blizzard NPC to prevent threat gain.
**MovementInform#2**: Resets events and picks a new target when a movement point is reached.
**SetRandomMove**: Switches the Blizzard NPC to random wandering movement.
**PickNewTarget**: Selects a new player target for the Blizzard, favoring spread-out players.
**UpdateAI#2**: Main loop for the Blizzard NPC; updates events and triggers target picking.
**GetAI_boss_sapphiron**: Factory function creating the `boss_sapphironAI` instance.
**GetAI_npc_sapphironBlizzard**: Factory function creating the `npc_sapphiron_blizzardAI` instance.
**OnSetTargetMap**: Modifies the Life Drain spell to target 7–10 random players.
**GetScript_SapphironLifeDrain**: Factory function for the Life Drain spell script.
**sapphiron_birthAI**: Constructor for the birth GameObject; retrieves instance data.
**OnUse**: Validates player and spawns Sapphiron if the encounter hasn't started.
**GetAI_sapphiron_birth**: Factory function creating the `sapphiron_birthAI` instance.
**AddSC_boss_sapphiron**: Registers all Sapphiron-related scripts with the script manager.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_sapphiron

*Source:* boss_sapphiron.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_sapphironAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset | method | CreatureAI/SetCombatMovement, EventMap/Reset, instance_naxxramas.Main/GetData, instance_naxxramas.Main/SetData, Unit.Main/RemoveAurasDueToSpell | — | — |
| UnSummonWingBuffet | method | ObjectGuid/ObjectGuid#5, TemporarySummon/UnSummon, ZoneScript/GetCreature | — | — |
| DeleteAndDispellIceBlocks | method | GridSearchers/GetGameObjectListWithEntryInGrid#2, WorldObject.Object/DeleteLater | — | — |
| DamageTaken | method | Unit.Main/GetMeleeZLimit, Unit.Main/SendMeleeAttackStop, WorldObject.Object/CanReachWithMeleeSpellAttack | — | — |
| AttackStart | method | CreatureAI/AttackStart | — | — |
| Aggro | method | EventMap/ScheduleEvent#2, instance_naxxramas.Main/SetData | — | — |
| JustDied | method | instance_naxxramas.Main/SetData, SpellCaster/CastSpell#2 | — | — |
| RescheduleIcebolt | method | EventMap/Repeat#3, EventMap/ScheduleEvent#3 | — | — |
| DoIceBolt | method | CreatureAI/DoCastSpellIfCan, Object/GetObjectGuid, Object/ToPlayer, shared_Util/urand, ThreatManager/getThreatList, Unit.Main/GetThreatManager, Unit.Main/IsDead, Unit.Main/SetFacingToObject | — | — |
| MovementInform | method | EventMap/ScheduleEvent#3 | — | — |
| setHover | method | Creature.Main/ClearTemporaryFaction, Creature.Main/GetTemporaryFactionFlags, Creature.Main/UpdateCombatState, CreatureAI/SetCombatMovement, SpellCaster/InterruptNonMeleeSpells, Unit.Main/AttackStop, Unit.Main/HandleEmote, Unit.Main/RemoveAllAttackers, Unit.Main/RemoveAurasDueToSpell, Unit.Main/SetHover, Unit.Main/SetMeleeZLimit, Unit.Main/SetReactState, WorldObject.Object/HasUnitMovementFlag | — | — |
| UpdateReachable | method | Creature.MotionMaster/GetCurrentMovementGeneratorType, MotionMaster/GetCurrent, MovementGenerator/IsReachable, Unit.Main/GetMaxChaseDistance, Unit.Main/GetMotionMaster, Unit.Main/GetVictim, Unit.Main/HasDistanceCasterMovement, WorldObject.Object/IsWithinDistInMap, WorldObject.Object/IsWithinLOSInMap | — | — |
| UpdateAI | method | Creature.Main/SelectAttackingTarget, Creature.MotionMaster/MoveIdle, Creature.MotionMaster/MovePoint, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, CreatureAI/SetCombatMovement, EventMap/ExecuteEvent, EventMap/Repeat, EventMap/Repeat#3, EventMap/Reset, EventMap/ScheduleEvent#2, EventMap/ScheduleEvent#3, EventMap/Update, MotionMaster/Clear, Object/GetObjectGuid, ObjectGuid/ObjectGuid#5, ScriptedAI/EnterEvadeMode, ScriptMgr/DoScriptText, shared_Util/frand, shared_Util/urand, SpellCaster/CastSpell#2, SpellCaster/InterruptNonMeleeSpells, SpellCaster/IsNonMeleeSpellCasted, ThreatManager/isThreatListEmpty, Unit.Main/ClearUnitState, Unit.Main/GetHealthPercent, Unit.Main/GetMotionMaster, Unit.Main/GetThreatManager, Unit.Main/GetVictim, Unit.Main/HasAura#2, Unit.Main/SelectHostileTarget, Unit.Main/SetTargetGuid, WorldObject.Object/ForceValuesUpdateAtIndex, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/SummonCreature#2 | — | — |
| npc_sapphiron_blizzardAI | ctor | Creature.Main/SetWanderDistance, EventMap/ScheduleEvent#3, ScriptedAI/ScriptedAI, Unit.Main/SetReactState, WorldObject.Object/GetInstanceData | — | — |
| Reset#2 | method | — | — | — |
| JustRespawned | method | — | — | — |
| AttackStart#2 | method | — | — | — |
| MoveInLineOfSight | method | — | — | — |
| Aggro#2 | method | — | — | — |
| MovementInform#2 | method | EventMap/Reset, EventMap/ScheduleEvent#2 | — | — |
| SetRandomMove | method | Creature.MotionMaster/GetCurrentMovementGeneratorType, Creature.MotionMaster/MoveRandom, MotionMaster/Clear, Unit.Main/GetMotionMaster | — | — |
| PickNewTarget | method | Creature.MotionMaster/MovePoint, MotionMaster/Clear, Object/GetObjectGuid, Object/ToPlayer, ScriptedInstance/GetSingleCreatureFromStorage, shared_Util/urand, ThreatManager/getThreatList, Unit.Main/GetMotionMaster, Unit.Main/GetThreatManager, WorldObject.Object/GetDistance3dToCenter#3, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ | — | — |
| UpdateAI#2 | method | EventMap/ExecuteEvent, EventMap/Repeat, EventMap/Update, shared_Util/urand | — | — |
| GetAI_boss_sapphiron | function | — | — | — |
| GetAI_npc_sapphironBlizzard | function | — | — | — |
| OnSetTargetMap | method | shared_Util/urand | — | — |
| GetScript_SapphironLifeDrain | function | — | — | — |
| sapphiron_birthAI | ctor | GameObjectAI/GameObjectAI, WorldObject.Object/GetInstanceData | — | — |
| OnUse | method | instance_naxxramas.Main/GetData, instance_naxxramas.Main/SetData, Object/IsPlayer, Player.Main/IsGameMaster, ScriptedInstance/GetSingleCreatureFromStorage, Unit.Main/IsAlive, WorldObject.Object/SendObjectDeSpawnAnim | — | — |
| GetAI_sapphiron_birth | function | — | — | — |
| AddSC_boss_sapphiron | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
