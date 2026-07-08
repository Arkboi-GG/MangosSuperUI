# boss_gothik

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# boss_gothik.cpp

## Purpose & Responsibilities

`boss_gothik.cpp` implements the artificial intelligence and encounter logic for **Gothik the Harvester**, a boss in the Naxxramas raid instance. The encounter is characterized by a strict phase-based structure involving two distinct combat zones (left and right sides of the room), separated by a gate.

The unit manages three primary subsystems:
1.  **Boss AI (`boss_gothikAI`)**: Controls Gothik's behavior through three phases:
    *   **Speech Phase**: Gothik remains immune and delivers introductory dialogue while checking if the raid is properly split between the two sides.
    *   **Balcony Phase**: Gothik summons waves of "Unrelenting" undead adds from the balcony. These adds are designed to travel via a chain of dummy spells to the opposite side, transforming into "Spectral" adds.
    *   **Ground Phase**: Gothik teleports between the left and right sides, casting damage-over-time effects and melee attacks. He enforces a "split raid" mechanic by removing threat from players on the opposite side until the gates open.
2.  **Spell Chain Logic (`EffectDummyCreature_spell_anchor`)**: Handles the complex visual and mechanical chain of spells that transports summoned adds from the balcony to the ground floor and across the room. This involves temporary trigger creatures and sequential spell casts.
3.  **Trigger AI (`gothikTriggerAI`)**: Provides a passive AI for invisible trigger creatures used to anchor spell chains, ensuring they do not engage in combat or move unexpectedly.

The unit relies heavily on the `instance_naxxramas` script instance to track game state (e.g., which side Gothik is on, summon points, and anchor locations) and to manage the state of the combat gate.

## Member-by-Member Behavior

### Boss Initialization and State Management

**`boss_gothikAI` (ctor)**
Initializes the AI object. It retrieves the `instance_naxxramas` data pointer from the creature's instance data and immediately calls `Reset()` to initialize timers and phase variables.

**`Reset`**
Resets the boss to its initial state.
*   Sets the phase to `PHASE_SPEECH`.
*   Initializes all timers (speech, summon, teleport, etc.) to their default values.
*   Despawn any lingering adds from previous attempts by searching the grid for specific NPC entries (`NPC_UNREL_*`, `NPC_SPECT_*`) and calling `DeleteLater()` on them.
*   Sets the caster chase distance to 40 yards.

**`Aggro`**
Triggered when the boss enters combat.
*   Sets the creature in combat with the zone.
*   Plays the first speech line (`SAY_SPEECH_1`).
*   Notifies the instance script that the encounter is `IN_PROGRESS`.
*   Calls `SetGothTriggers()` on the instance to prepare trigger creatures.
*   Stops movement and attack commands.
*   Applies `SPELL_IMMUNE_ALL` if not already present, rendering the boss untargetable during the speech phase.

**`AttackStart`**
Checks if the boss has the immunity aura. If so, it prevents the standard attack start behavior, keeping the boss passive during the speech phase. If not immune, it delegates to the parent `ScriptedAI::AttackStart`.

**`EnterEvadeMode`**
Handles the boss leaving combat (evasion).
*   Calls the parent evasion handler.
*   Respawns the creature.
*   Teleports the creature back to its home position.

**`JustReachedHome`**
Called when the boss returns to its spawn point after evading. It notifies the instance script that the encounter has `FAIL`ed.

**`JustDied`**
Called upon the boss's death.
*   Plays the death speech.
*   Calls `OpenTheGate()` to ensure the exit is accessible.
*   Notifies the instance script that the encounter is `DONE`.

**`KilledUnit`**
Plays a kill speech if the victim is a player.

### Summoning Mechanics

**`SummonAdd`**
A helper method to summon a single add at a specific location.
*   Returns early if the boss is not in combat or is dead.
*   Summons the creature with a timed despawn.
*   Sets corpse delay.
*   If gates are open, it immediately sets the add in combat with the zone.
*   If gates are closed, it iterates through all players in the map. Based on the add type (Unrelenting vs. Spectral) and the player's side (determined by `instance_naxxramas::IsInRightSideGothArea`), it assigns threat and sets combat status. Unrelenting adds target players on the right side; Spectral adds target players on the left side.
*   Finally, it selects the nearest attacking target and initiates combat for the add.

**`SummonAdds`**
Orchestrates the summoning of multiple adds based on a wave definition.
*   Retrieves a list of summon point creatures from the instance script (`GetGothSummonPointCreatures`).
*   Sorts these points by distance to the boss.
*   Depending on the NPC entry requested, it calls `SummonAdd` for specific positions in the sorted list (e.g., Death Knights are summoned at the first and last points, Trainees at the first, second, and fourth, etc.).

**`SummonedCreatureJustDied`**
Triggered when an add summoned by Gothik dies.
*   Ignores Spectral adds (they don't trigger the chain).
*   Finds the closest "anchor" creature for the dead add using `instance_naxxramas::GetClosestAnchorForGoth`.
*   Summons a temporary trigger creature (`NPC_SUB_BOSS_TRIGGER`) at the dead add's location.
*   Casts a dummy spell from the trigger to the anchor. The spell ID depends on the type of add that died (Trainee -> A, Death Knight -> B, Rider -> C). This initiates the spell chain that eventually summons a Spectral add on the other side.

### Gate and Phase Logic

**`OpenTheGate`**
Opens the combat gate separating the two sides.
*   Checks if gates are already opened to prevent duplicate execution.
*   Plays an emote.
*   Sets the `gatesOpened` flag.
*   Activates the gate GameObject (`GO_MILI_GOTH_COMBAT_GATE`) retrieved from the instance storage.
*   Forces all existing adds in the grid into combat with the zone, allowing them to cross the gate.

**`HasLessPlayersPerSide`**
Determines if the raid is improperly split.
*   Iterates through all players in the map.
*   Excludes dead or feigning death players.
*   Excludes players outside the room (distance > 100 from gate) or stacked directly on the gate.
*   Counts players on the left and right sides based on their Y-coordinate relative to the gate.
*   Returns `true` if either side has fewer than `count` players. This is used to evade the boss if the raid isn't split (threshold 10) or to open gates early if one side wipes (threshold 1).

### Main AI Loop

**`UpdateAI`**
The core update loop, handling phase-specific logic.
*   **Immunity Check**: If immune, checks if the threat list is empty. If so, it evades (likely due to a bug or unexpected state). If not immune, it ensures a hostile target exists and handles evasion if the boss leaves the home area.
*   **PHASE_SPEECH**:
    *   Waits for the speech timer.
    *   Checks `HasLessPlayersPerSide(10)`. If true, it evades the boss to enforce the split requirement.
    *   Increments speech count and plays corresponding dialogue.
    *   After 4 speeches, transitions to `PHASE_BALCONY`.
*   **PHASE_BALCONY**:
    *   Manages the summon timer.
    *   Uses a static array `auiSummonData` to define 18 waves of adds.
    *   Calls `SummonAdds` for each entry in the current wave.
    *   After 18 waves, it plays a teleport emote, casts `SPELL_TELEPORT_RIGHT`, pacifies itself, removes immunity, and transitions to `PHASE_GROUND`. It also checks if gates should open early if all players are on one side.
*   **PHASE_GROUND**:
    *   **Post-Teleport Setup**: If just teleported, it determines which side it is on, resets threat, and attacks the nearest target.
    *   **Threat Management**: If gates are closed, it removes 100% threat from victims on the opposite side to prevent cross-room aggro.
    *   **Gate Opening Conditions**: Opens gates if health drops below 30%, if one side wipes (`HasLessPlayersPerSide(1)`), or after 4 teleports.
    *   **Teleport Logic**: If gates are closed, it periodically teleports to the opposite side. It delays other spells (`m_uiTeleportCastDelay`) to prevent animation conflicts.
    *   **Spell Casting**: If not delaying, it casts `SPELL_SHADOWBOLT` on a random target and `SPELL_HARVESTSOUL` on itself periodically.
    *   **Melee**: Performs melee attacks if ready.

**`ResetThreatAndAttackNearestTarget`**
Used after teleportation. Clears existing threat, selects the nearest hostile target in line of sight, starts attacking it, and adds significant threat to ensure it remains the primary target.

### Helper Functions

**`GetAI_boss_gothik`**
Factory function that creates and returns a new `boss_gothikAI` instance.

**`EffectDummyCreature_spell_anchor`**
A spell effect handler for dummy spells used in the add transport chain.
*   **Stage 1 (Anchor 1)**: When a trigger creature casts a spell to an anchor on the high right side, it finds the corresponding anchor on the high left side and casts the next stage spell (`*_ANCHOR_2`).
*   **Stage 2 (Anchor 2)**: When cast to an anchor on the high left side, it selects a random summon point creature on the left side and casts the final stage spell (`*_SKULL`).
*   **Stage 3 (Skull)**: When cast to a skull/summon point, it retrieves the Gothik boss object and calls `SummonAdd` to spawn the corresponding Spectral add (Trainee, Death Knight, or Rider + Horse) at that location.

**`gothikTriggerAI` (ctor)**
Initializes the trigger AI.

**`Reset#2`**
Configures the trigger creature to wander minimally (0.01f distance) and initializes its motion master.

**`MoveInLineOfSight`, `Aggro#2`, `AttackStart#2`, `UpdateAI#2`**
Empty overrides for `gothikTriggerAI` to ensure the trigger creatures remain passive and do not engage in combat or react to players.

**`GetAI_GothikTrigger`**
Factory function that creates and returns a new `gothikTriggerAI` instance.

**`AddSC_boss_gothik`**
Registers the scripts with the engine.
*   Registers `boss_gothik` with its AI getter.
*   Registers `spell_anchor` with the dummy spell effect handler and the trigger AI getter.

## Cross-Unit Boundaries

### `instance_naxxramas` (Main)
*   **`SetData`**: Called by `Aggro`, `JustDied`, and `JustReachedHome` to update the encounter state (IN_PROGRESS, DONE, FAIL).
*   **`SetGothTriggers`**: Called by `Aggro` to prepare the environment for the encounter.
*   **`IsInRightSideGothArea`**: Called by `SummonAdd`, `UpdateAI`, and `HasLessPlayersPerSide` to determine player/boss positioning relative to the gate.
*   **`GetGothSummonPointCreatures`**: Called by `SummonAdds` and `EffectDummyCreature_spell_anchor` to retrieve coordinates for spawning adds.
*   **`GetClosestAnchorForGoth`**: Called by `SummonedCreatureJustDied` and `EffectDummyCreature_spell_anchor` to find the next link in the spell chain.
*   **`GetSingleGameObjectFromStorage`**: Called by `OpenTheGate` and `HasLessPlayersPerSide` to access the gate object.
*   **`GetSingleCreatureFromStorage`**: Called by `EffectDummyCreature_spell_anchor` to locate the Gothik boss object.
*   **`HandleEvadeOutOfHome`**: Called by `UpdateAI` to check if the boss has moved out of bounds.
*   **`GetMap`**: Called by `SummonAdd` and `HasLessPlayersPerSide` to iterate over players.

### `ScriptedAI` / `CreatureAI`
*   **`DoStopAttack`**, **`DoResetThreat`**, **`EnterEvadeMode`**, **`AttackStart`**: Standard AI behaviors overridden or delegated by `boss_gothikAI`.
*   **`DoCastSpellIfCan`**, **`DoMeleeAttackIfReady`**: Used in `UpdateAI` to execute spells and attacks.

### `WorldObject` / `Creature` / `Unit`
*   **`SummonCreature`**: Used extensively to spawn adds and triggers.
*   **`SetInCombatWith`**, **`AddThreat`**: Used to manage aggro distribution for adds.
*   **`GetPositionX/Y/Z`**, **`GetOrientation`**: Used to place summons and determine positions.
*   **`HasAura`**, **`RemoveAurasDueToSpell`**: Used to manage immunity and spell states.
*   **`SelectAttackingTarget`**: Used to determine targets for adds and the boss.
*   **`DeleteLater`**: Used in `Reset` to clean up old adds.

### `ScriptMgr`
*   **`DoScriptText`**: Used to play dialogue and emotes.

### `GridSearchers`
*   **`GetCreatureListWithEntryInGrid`**: Used in `Reset` and `OpenTheGate` to find and manage adds in the vicinity.

## Data Model

This unit does not interact directly with database tables. All data is managed in-memory via the `instance_naxxramas` script instance and the creature objects themselves.

## Notable Implementation Details

1.  **Spell Chain Workaround**: The comment in `SummonedCreatureJustDied` explains that a temporary trigger creature is used because Mangos deletes spell events upon caster death. Since the add dies immediately after being summoned (in the original design) or needs to trigger a delayed effect, the trigger creature acts as a persistent caster to complete the spell chain (`Anchor 1` -> `Anchor 2` -> `Skull`).
2.  **Threat Manipulation**: In `PHASE_GROUND`, `UpdateAI` manually modifies threat percentages (`modifyThreatPercent(victim, -100)`) for players on the opposite side of the gate. This is a critical mechanic to prevent the boss from pulling players across the room before the gates open.
3.  **Early Gate Opening**: The gates can open early if one side of the raid wipes (`HasLessPlayersPerSide(1)`). This prevents the boss from being stuck on one side indefinitely.
4.  **Teleport Pacification**: After teleporting, the boss is pacified for 1200ms (`TELEPORT_PACIFY_TIMER`) to allow animations to play and prevent immediate re-engagement.
5.  **Split Raid Enforcement**: During the speech phase, if fewer than 10 players are on either side, the boss evades. This forces the raid to split correctly before the encounter begins.
6.  **Static Wave Data**: The summon waves are hardcoded in `auiSummonData` within `UpdateAI`. This makes the encounter predictable but inflexible.

## Member Reference

**`boss_gothikAI`** (ctor): Initializes the AI, retrieves instance data, and calls `Reset()`.
**`Reset`**: Resets phase, timers, and despawns lingering adds.
**`Aggro`**: Starts combat, plays speech, sets instance state, applies immunity.
**`AttackStart`**: Prevents attack start if immune.
**`EnterEvadeMode`**: Handles evasion, respawn, and teleport home.
**`KilledUnit`**: Plays kill speech for players.
**`JustDied`**: Plays death speech, opens gates, sets instance state to DONE.
**`JustReachedHome`**: Sets instance state to FAIL.
**`SummonAdd`**: Summons an add, assigns threat based on side/type, and starts combat.
**`SummonAdds`**: Orchestrates summoning multiple adds from predefined points.
**`SummonedCreatureJustDied`**: Triggers the spell chain to summon spectral adds on the opposite side.
**`OpenTheGate`**: Opens the combat gate and forces adds into combat.
**`HasLessPlayersPerSide`**: Checks if the raid is split correctly.
**`UpdateAI`**: Main loop handling speech, summoning, teleporting, and combat logic.
**`ResetThreatAndAttackNearestTarget`**: Clears threat and attacks nearest target after teleport.
**`GetAI_boss_gothik`**: Factory function for `boss_gothikAI`.
**`EffectDummyCreature_spell_anchor`**: Handles the dummy spell chain for transporting adds.
**`gothikTriggerAI`** (ctor): Initializes trigger AI.
**`Reset#2`**: Configures trigger creature movement.
**`MoveInLineOfSight`**: Empty override for trigger AI.
**`Aggro#2`**: Empty override for trigger AI.
**`AttackStart#2`**: Empty override for trigger AI.
**`UpdateAI#2`**: Empty override for trigger AI.
**`GetAI_GothikTrigger`**: Factory function for `gothikTriggerAI`.
**`AddSC_boss_gothik`**: Registers scripts with the engine.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_gothik

*Source:* boss_gothik.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_gothikAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset | method | GridSearchers/GetCreatureListWithEntryInGrid, Unit.Main/SetCasterChaseDistance, WorldObject.Object/DeleteLater | — | — |
| Aggro | method | Creature.Main/SetInCombatWithZone, Creature.MotionMaster/MoveIdle, instance_naxxramas.Main/SetData, instance_naxxramas.Main/SetGothTriggers, ScriptedAI/DoStopAttack, ScriptMgr/DoScriptText, SpellCaster/CastSpell#2, Unit.Main/GetMotionMaster, Unit.Main/HasAura#2 | — | — |
| AttackStart | method | CreatureAI/AttackStart, Unit.Main/HasAura#2 | — | — |
| EnterEvadeMode | method | Creature.Main/GetHomePosition#2, Creature.Main/Respawn, ScriptedAI/EnterEvadeMode | — | — |
| KilledUnit | method | Object/GetTypeId, ScriptMgr/DoScriptText | — | — |
| JustDied | method | instance_naxxramas.Main/SetData, ScriptMgr/DoScriptText | — | — |
| JustReachedHome | method | instance_naxxramas.Main/SetData | — | — |
| SummonAdd | method | Creature.Main/AI, Creature.Main/SelectAttackingTarget, Creature.Main/SetCorpseDelay, Creature.Main/SetInCombatWithZone, CreatureAI/AttackStart, instance_naxxramas.Main/IsInRightSideGothArea, Map.Main/GetPlayers, Unit.Main/AddThreat, Unit.Main/IsDead, Unit.Main/IsInCombat, Unit.Main/SetInCombatWith, WorldObject.Object/SummonCreature#2, ZoneScript/GetMap#2 | — | — |
| SummonAdds | method | instance_naxxramas.Main/GetGothSummonPointCreatures, ObjectDistanceOrder/ObjectDistanceOrder, WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ | — | — |
| SummonedCreatureJustDied | method | instance_naxxramas.Main/GetClosestAnchorForGoth, Object/GetEntry, Object/GetGUID, ObjectGuid/ObjectGuid#5, SpellCaster/CastSpell#2, WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/SummonCreature#2 | — | — |
| OpenTheGate | method | Creature.Main/SetInCombatWithZone, GameObject/SetGoState, GridSearchers/GetCreatureListWithEntryInGrid, ScriptedInstance/GetSingleGameObjectFromStorage, ScriptMgr/DoScriptText | — | — |
| HasLessPlayersPerSide | method | Map.Main/GetPlayers, ScriptedInstance/GetSingleGameObjectFromStorage, Unit.Main/IsDead, Unit.Main/IsFeigningDeathSuccessfully, WorldObject.Object/GetPositionY, WorldObject.Object/IsWithinDist, ZoneScript/GetMap#2 | — | — |
| UpdateAI | method | Creature.Main/SelectAttackingTarget#2, Creature.Main/SetTempPacified, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, instance_naxxramas.Main/HandleEvadeOutOfHome, instance_naxxramas.Main/IsInRightSideGothArea, MotionMaster/Clear, ScriptMgr/DoScriptText, shared_Util/urand, ThreatManager/isThreatListEmpty, ThreatManager/modifyThreatPercent#2, Unit.Main/ClearTarget, Unit.Main/GetHealthPercent, Unit.Main/GetMotionMaster, Unit.Main/GetThreatManager, Unit.Main/GetVictim, Unit.Main/HasAura#2, Unit.Main/RemoveAurasDueToSpell, Unit.Main/SelectHostileTarget, Unit.Main/StopMoving | — | — |
| ResetThreatAndAttackNearestTarget | method | Creature.Main/SelectAttackingTarget, ScriptedAI/DoResetThreat, Unit.Main/AddThreat | — | — |
| GetAI_boss_gothik | function | — | — | — |
| EffectDummyCreature_spell_anchor | function | Creature.Main/AI, instance_naxxramas.Main/GetClosestAnchorForGoth, instance_naxxramas.Main/GetGothSummonPointCreatures, Object/GetEntry, ScriptedInstance/GetSingleCreatureFromStorage, shared_Util/urand, SpellCaster/CastSpell#2, WorldObject.Object/GetInstanceData, WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ | — | — |
| gothikTriggerAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#2 | method | Creature.Main/SetDefaultMovementType, Creature.Main/SetWanderDistance, Creature.MotionMaster/Initialize, Unit.Main/GetMotionMaster | — | — |
| MoveInLineOfSight | method | — | — | — |
| Aggro#2 | method | — | — | — |
| AttackStart#2 | method | — | — | — |
| UpdateAI#2 | method | — | — | — |
| GetAI_GothikTrigger | function | — | — | — |
| AddSC_boss_gothik | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
