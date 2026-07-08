<!-- provenance: failed-members -->
# boss_cthun

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# boss_cthun

## Purpose & Responsibilities

`boss_cthun.cpp` implements the complete artificial intelligence and encounter logic for **C'Thun**, the final boss of the Temple of Ahn'Qiraj raid instance. It defines the behaviors for C'Thun's main body (`cthunAI`), his detached eye (`eye_of_cthunAI`), and five distinct types of tentacles (`eye_tentacleAI`, `claw_tentacleAI`, `giant_claw_tentacleAI`, `giant_eye_tentacleAI`, `flesh_tentacleAI`).

The encounter is divided into two primary phases:
1.  **Phase 1 (The Eye):** Players fight C'Thun's eye while dodging green beams and surviving tentacle spawns. The eye periodically enters a "Dark Glare" rotation phase.
2.  **Phase 2 (The Body):** After the eye dies, C'Thun emerges. He is initially invulnerable ("Carapace") and spawns flesh tentacles that must be killed to weaken him. Once weakened, he becomes vulnerable to damage but periodically grabs players and teleports them to his "stomach."

Key mechanical features implemented include:
*   **Delayed Combat Entry:** Logic to delay pulling non-initial-pullers into combat for a specific duration (configurable via `USE_POSTFIX_PRENERF_PULL_LOGIC`).
*   **Tentacle Lifecycle Management:** Complex spawning, movement, and despawning logic for various tentacles, including portals that follow them.
*   **Stomach Mechanic:** Teleporting players to a separate zone (conceptually the stomach) where they can be killed if the raid fails to rescue them.
*   **Custom Targeting:** Specialized melee targeting logic for tentacles that only attacks targets in melee range and resets threat if targets leave range.

## Member-by-Member Behavior

### Utility Classes & Functions

#### `SpellTimer` & `OnlyOnceSpellTimer`
These helper classes manage cooldowns for spell casting.
*   **`SpellTimer`**: Tracks a cooldown (`cooldown`) and attempts to cast a specific `spellID` on a target selected by `targetSelectFunc` when the cooldown expires. It supports triggered spells and retry-on-fail logic.
*   **`OnlyOnceSpellTimer`**: Inherits from `SpellTimer` but ensures the spell is only cast once per reset cycle. Subsequent updates simply track time without recasting.

#### Static Helper Functions
*   **`SelectRandomAliveNotStomach`**: Iterates through all players in the map (`instance_temple_of_ahnqiraj/PlayerInStomach`, `Map.Main/GetPlayers`). It filters out dead players, Game Masters, players not in combat, and players already in C'Thun's stomach. It returns a random player from this filtered list.
*   **`selectSelfFunc` / `selectTargetFunc`**: Simple lambda-like functions returning the creature itself or its current victim, used as arguments for `SpellTimer`.
*   **`hamstringResetCooldownFunc` / `trashResetCooldownFunc` / `groundTremorResetCooldownFunc`**: Return randomized or fixed cooldown values for specific tentacle spells using `shared_Util/urand`.

### Base Tentacle AI: `cthunTentacle`

This struct serves as the base class for most tentacle AIs.
*   **`cthunTentacle` (ctor)**: Initializes the instance data pointer, disables combat movement, and stores the default orientation.
*   **`Reset`**: Roots the creature, stops movement, and sets it in combat with the zone.
*   **`Aggro`**: Calls parent `Aggro` and ensures the creature is in combat with the zone.
*   **`UpdateCthunTentacle`**: Checks if any players are in the map. If not, it unsuns the creature. Otherwise, it forces the creature to remain rooted and stopped, mimicking a frozen state unless actively attacking.
*   **`UpdateMelee`**: Attempts to select a hostile melee target. If successful, it performs a melee attack. If not, it stops attacking and optionally resets orientation.
*   **`SelectHostileTargetMelee`**: A complex targeting routine. It prioritizes taunted targets in melee range. If no taunt, it selects a random hostile target in melee range. Crucially, if the previous target leaves melee range, their threat is reduced by 100% (effectively resetting it). It also handles crowd control checks (stun, fear, feign death) before attacking.

### Portal Tentacle Base: `cthunPortalTentacle`

Inherits from `cthunTentacle`. Manages a visual portal creature that follows the tentacle.
*   **`cthunPortalTentacle` (ctor)**: Spawns a portal creature (`MOB_SMALL_PORTAL` or `MOB_GIANT_PORTAL`) and initializes a `groundRuptureTimer`.
*   **`DespawnPortal`**: Finds the portal creature by GUID and unsuns it.
*   **`Reset`**: Calls parent reset, resets the ground rupture timer, and stops attacks.
*   **`JustDied`**: Ensures the portal is despawned when the tentacle dies.
*   **`UpdatePortalTentacle`**: Updates the base tentacle logic. If the ground rupture timer expires and the birth animation is complete, it casts the ground rupture spell.
*   **`FixPortalPosition`**: Calculates the optimal Z-height for the portal by sampling the ground height around the tentacle. It avoids slopes by ignoring outliers and places the portal at the highest valid ground level near the tentacle.

### Specific Tentacle AIs

#### `clawTentacle` (Base for Claw Types)
Inherits from `cthunPortalTentacle`. Implements the "submerge and teleport" mechanic.
*   **`clawTentacle` (ctor)**: Initializes timers for evading, hamstring, and submerging.
*   **`Reset`**: Resets health to full (important after teleporting), resets timers, and sets state to `NORMAL`.
*   **`UpdateClawTentacle`**: State machine handler.
    *   **`updateNormal`**: Performs melee attacks. If no target is found for `CLAW_TENTACLE_EVADE_PORT_COOLDOWN` ms, it transitions to `FEIGN_IN_PROCES` and casts a submerge visual.
    *   **`updateFeign`**: Waits briefly, then transitions to `BURRIED` and hides visibility.
    *   **`updateBurried`**: After a delay, calls `TeleportOnNewRandomTarget`. If successful, it resets threat, removes submerge auras, restores visibility, and resets the AI.
*   **`TeleportOnNewRandomTarget`**: Uses `SelectRandomAliveNotStomach` to find a new target, teleports the tentacle near them, and fixes the portal position.
*   **`setVisibility`**: Toggles visibility for both the tentacle and its associated portal.

#### `eye_tentacleAI`
Inherits from `cthunPortalTentacle`. Focuses on casting Mind Flay.
*   **`eye_tentacleAI` (ctor)**: Initializes with small portal and physical ground rupture.
*   **`AttackStart`**: Prevents interrupting channeling spells.
*   **`UpdateAI`**:
    *   If not channeling, it tries to cast `SPELL_MIND_FLAY` on a random alive player not in the stomach.
    *   If the cast succeeds, it faces the target and sets a cooldown for resist retries.
    *   If the current target is teleported to the stomach, it interrupts the spell.
    *   If not casting, it falls back to melee attacks.

#### `claw_tentacleAI`
Inherits from `clawTentacle`. Standard small claw tentacle behavior.
*   **`UpdateAI`**: Simply delegates to `clawTentacle::UpdateClawTentacle`.

#### `giant_claw_tentacleAI`
Inherits from `clawTentacle`. Adds Ground Tremor and Thrash spells.
*   **`giant_claw_tentacleAI` (ctor)**: Initializes `groundTremorTimer` and `trashTimer`.
*   **`UpdateAI`**: Delegates to `clawTentacle::UpdateClawTentacle` and updates the additional spell timers.

#### `giant_eye_tentacleAI`
Inherits from `cthunPortalTentacle`. Casts Green Beam instead of Mind Flay.
*   **`giant_eye_tentacleAI` (ctor)**: Initializes with giant portal and nature ground rupture.
*   **`UpdateAI`**:
    *   Manages a `BeamTimer`. When expired, casts `SPELL_GREEN_EYE_BEAM` on a random target.
    *   If the target goes to the stomach, interrupts the spell and resets the timer to allow immediate recast.
    *   Falls back to melee if not casting.

#### `flesh_tentacleAI`
Inherits from `cthunTentacle`. Simple melee attacker.
*   **`UpdateAI`**: Continuously attempts melee attacks, resetting orientation if no target is found.

### C'Thun's Eye: `eye_of_cthunAI`

Manages Phase 1 mechanics.
*   **`eye_of_cthunAI` (ctor)**: Disables combat movement and links to instance data.
*   **`Pull`**: Sets faction, records the initial puller, and casts the first green beam.
*   **`Aggro`**: If the eye isn't in combat, it triggers C'Thun's body `AttackStart` to synchronize the encounter start.
*   **`Reset`**: Resets phase to `GREEN_BEAM`, clears flags, and removes glare spells.
*   **`UpdateAI`**: State machine for Phase 1:
    *   **`GREEN_BEAM`**: Updates `UpdateGreenBeamPhase`. Transitions to `DARK_GLARE_CAST` after 45 seconds.
    *   **`DARK_GLARE_CAST`**: Waits for cast time. Transitions to `DARK_GLARE`.
    *   **`DARK_GLARE`**: Maintains rotation for 38 seconds. Transitions to `DARK_GLARE_COOLING`.
    *   **`DARK_GLARE_COOLING`**: Waits 1 second. Transitions back to `GREEN_BEAM`.
*   **`UpdateGreenBeamPhase`**: Casts green beam on a target. If `USE_POSTFIX_PRENERF_PULL_LOGIC` is defined, it always picks a random target. Otherwise, it targets the initial puller for the first few hits.
*   **`EnterDarkGlarePhase`**: Interrupts current spells, selects a random target to face, casts the rotation trigger, and freezes animation.
*   **`RemoveGlarePhaseSpells`**: Cleans up rotation auras.
*   **`CastGreenBeam`**: Executes the green beam cast and updates internal counters.

### C'Thun's Body: `cthunAI`

Manages Phase 2 and overall encounter state.
*   **`cthunAI` (ctor)**: Spawns a visual portal for C'Thun and links to instance data.
*   **`AttackStart`**: Triggers the Eye's `Pull` method. Sets combat flags. If `USE_POSTFIX_PRENERF_PULL_LOGIC` is defined, it delays setting "In Combat With Zone" for other players.
*   **`DespawnAllTentacles`**: Finds all tentacle types in a 350-yard radius and unsuns them, ensuring their portals are also cleaned up.
*   **`JustReachedHome`**: Marks the instance data as failed.
*   **`Reset`**: Resets all timers, phases, and visibility. Despawns all tentacles. Respawns the eye if needed.
*   **`CheckRespawnEye`**: Handles respawning the eye creature if it died or doesn't exist.
*   **`SummonedCreatureJustDied`**: Tracks flesh tentacle deaths. If the eye dies, transitions to `PHASE_PRE_TRANSITION`.
*   **`JustSummoned`**: Tracks flesh tentacle GUIDs.
*   **`UpdateAI`**: Main loop.
    *   Handles wipe logic (delayed eye respawn).
    *   Checks `AggroRadius` to start the fight.
    *   Delegates to phase-specific update methods.
*   **`SummonedCreatureDespawn`**: If the eye despawns, triggers the transition to Phase 2 (`PHASE_TRANSITION`).
*   **`JustDied`**: Marks instance as done and despawns all tentacles.
*   **`ResetartUnvulnerablePhase`**: Resets Phase 2 timers and spawns flesh tentacles. Applies invulnerability aura.
*   **`UnitShouldPull`**: Checks if a unit is within range, Z-distance, and Line of Sight to pull C'Thun.
*   **`AggroRadius`**: Scans all players/pets to see if any meet `UnitShouldPull` criteria.
*   **`CheckIfAllDead`**: Checks if any players are alive outside the stomach. If not, and players are in the stomach, it kills them and ends the encounter.
*   **`UpdateTentaclesP1`**: Spawns claw and eye tentacles based on timers.
*   **`UpdateTransitionPhase`**: Handles the emergence animation. Transitions to `PHASE_CTHUN_INVULNERABLE`.
*   **`UpdateInvulnerablePhase`**:
    *   If all flesh tentacles are dead, transitions to `PHASE_CTHUN_WEAKENED`, removes invulnerability, and releases any grabbed player.
    *   Otherwise, updates Phase 2 tentacles and stomach grab logic.
*   **`UpdateWeakenedPhase`**: If the weakness timer expires, resets to invulnerable phase.
*   **`SpawnFleshTentacles`**: Spawns two flesh tentacles at fixed coordinates.
*   **`UpdateStomachGrab`**:
    *   If a player is grabbed, waits for the duration, then teleports them to the stomach position and adds them to the stomach list in instance data.
    *   Periodically selects a random player to grab with `SPELL_MOUTH_TENTACLE`.
*   **`UpdateTentaclesP2`**: Spawns giant claw, giant eye, and normal eye tentacles.
*   **`SpawnEyeTentacles`**: Spawns 8 eye tentacles at fixed positions around C'Thun.
*   **`SpawnTentacleIfReady`**: Generic spawner for claw/giant tentacles near a random target.

### Factory Functions & Registration

*   **`GetAI_eye_of_cthun`**: Factory function that instantiates `eye_of_cthunAI` for the eye creature.
*   **`GetAI_cthun`**: Factory function that instantiates `cthunAI` for the main boss creature.
*   **`GetAI_eye_tentacle`**: Factory function that instantiates `eye_tentacleAI` for eye tentacles.
*   **`GetAI_claw_tentacle`**: Factory function that instantiates `claw_tentacleAI` for small claw tentacles.
*   **`GetAI_giant_claw_tentacle`**: Factory function that instantiates `giant_claw_tentacleAI` for giant claw tentacles.
*   **`GetAI_giant_eye_tentacle`**: Factory function that instantiates `giant_eye_tentacleAI` for giant eye tentacles.
*   **`GetAI_flesh_tentacle`**: Factory function that instantiates `flesh_tentacleAI` for flesh tentacles.
*   **`AddSC_boss_cthun`**: Registers all scripts (`boss_eye_of_cthun`, `boss_cthun`, `mob_eye_tentacle`, etc.) with the script manager. Called by `ScriptLoader/AddScripts`.

## Cross-Unit Boundaries

*   **`instance_temple_of_ahnqiraj`**:
    *   **Called By**: `SelectRandomAliveNotStomach`, `cthunTentacle`, `eye_tentacleAI`, `giant_eye_tentacleAI`, `eye_of_cthunAI`, `cthunAI`.
    *   **Collaboration**: The AI relies heavily on the instance script to track which players are in the "stomach" (`PlayerInStomach`, `AddPlayerToStomach`, `KillPlayersInStomach`) and to manage the encounter state (`SetData`). It also retrieves creature GUIDs (`GetSingleCreatureFromStorage`, `GetCreature`).
*   **`Creature.Main` / `CreatureAI`**:
    *   **Called By**: All AI structs.
    *   **Collaboration**: Standard AI interactions: casting spells (`DoCastSpellIfCan`), melee attacks (`DoMeleeAttackIfReady`), movement control (`SetCombatMovement`, `SetRooted`), and threat management (`AttackStart`, `SelectAttackingTarget`).
*   **`Map.Main` / `ZoneScript`**:
    *   **Called By**: `SelectRandomAliveNotStomach`, `cthunPortalTentacle`, `clawTentacle`, `eye_tentacleAI`, `giant_eye_tentacleAI`, `cthunAI`.
    *   **Collaboration**: Accessing player lists (`GetPlayers`), finding creatures by GUID (`GetCreature`), and retrieving map geometry (`GetHeight`, `IsWithinLOSInMap`).
*   **`Player.Main` / `Unit.Main`**:
    *   **Called By**: Various AI methods.
    *   **Collaboration**: Checking player states (`IsDead`, `IsGameMaster`, `IsInCombat`), managing auras (`RemoveAurasDueToSpell`, `HasAura`), and teleporting players (`NearTeleportTo`, `DoTeleportPlayer`).
*   **`Log.Main`**:
    *   **Called By**: Constructors and error paths.
    *   **Collaboration**: Logging errors (e.g., missing instance data, failed spawns) to the server log.
*   **`ScriptedAI` / `ScriptedInstance`**:
    *   **Called By**: All AI structs.
    *   **Collaboration**: Base AI functionality (`EnterEvadeMode`, `DoStopAttack`, `DoSpawnCreature`, `DoScriptText`).

## Data Model

This unit does not directly query or modify database tables. It interacts with the runtime memory structures of the `instance_temple_of_ahnqiraj` script and the core engine's object managers. No SQL schema is relevant to this file's direct operations.

## Notable Implementation Details

1.  **Pre-Nerf Pull Logic**: The macro `USE_POSTFIX_PRENERF_PULL_LOGIC` controls whether the encounter uses a delayed combat entry for players other than the initial puller. This is a significant gameplay balance change hardcoded into the logic.
2.  **Threat Reset on Melee Range Loss**: `cthunTentacle::SelectHostileTargetMelee` explicitly reduces threat by 100% for targets that leave melee range. This prevents tentacles from chasing players who kite them, forcing them to stay close or lose aggro entirely.
3.  **Portal Height Calculation**: `cthunPortalTentacle::FixPortalPosition` samples 8 points around the tentacle to determine the ground height. It ignores points with significant Z-deviation (slopes) and uses the highest valid "inlier" height to prevent portals from clipping into the ground or floating too high.
4.  **Stomach Teleportation**: The stomach mechanic involves teleporting players to a specific coordinate (`stomachPortPosition`) and adding them to a list in the instance script. If all players outside the stomach die, `cthunAI::CheckIfAllDead` triggers `KillPlayersInStomach`, effectively wiping the raid.
5.  **Eye Respawn Delay**: Upon a wipe in Phase 2, `cthunAI` delays respawning the eye by 5 seconds (`wipeRespawnEyeTimer`) to allow the re-emerge animation to play correctly before the eye appears.
6.  **Flesh Tentacle Tracking**: `cthunAI` maintains a `std::vector<ObjectGuid>` for flesh tentacles. This is critical for determining when C'Thun becomes vulnerable (when the vector is empty).
7.  **Hardcoded Coordinates**: Many spawn positions (flesh tentacles, eye tentacles, stomach portal) are hardcoded arrays of floats. This makes the encounter layout rigid and dependent on the specific map geometry of Temple of Ahn'Qiraj.

## Member Reference

**SpellTimer** (ctor): Initializes the spell timer with creature, spell ID, cooldown, reset function, trigger status, and target selector.
**Reset#2** (method): Resets the cooldown to a custom value or the result of the reset function.
**Update#2** (method): Decrements cooldown; if expired, attempts to cast the spell on the selected target and resets cooldown.
**OnlyOnceSpellTimer** (ctor): Inherits from SpellTimer, initializes `didOnce` flag.
**Reset** (method): Calls parent Reset and sets `didOnce` to false.
**Update** (method): If `didOnce` is false, calls parent Update and sets `didOnce` to true on success. Otherwise, just tracks time.
**SelectRandomAliveNotStomach** (function): Filters map players for alive, non-GM, in-combat players not in the stomach, returning a random one.
**selectSelfFunc** (function): Returns the creature itself.
**selectTargetFunc** (function): Returns the creature's current victim.
**hamstringResetCooldownFunc** (function): Returns 5000ms.
**trashResetCooldownFunc** (function): Returns a random value between 6000 and 12000ms.
**groundTremorResetCooldownFunc** (function): Returns a random value between 6000 and 12000ms.
**cthunTentacle** (ctor): Links to instance data, disables combat movement, stores default orientation.
**Reset#7** (method): Roots creature, stops movement, sets in combat with zone.
**Aggro** (method): Calls parent Aggro, sets in combat with zone.
**UpdateCthunTentacle** (method): Unsuns if no players in map; otherwise roots and stops movement.
**UpdateMelee** (method): Selects melee target; if found, attacks; else stops attack and resets orientation.
**SelectHostileTargetMelee** (method): Selects taunted or random melee target; resets threat for targets leaving melee range.
**cthunPortalTentacle** (ctor): Spawns portal creature, initializes ground rupture timer.
**DespawnPortal** (method): Finds portal by GUID and unsuns it.
**Reset#6** (method): Calls parent Reset, resets timers, stops attacks.
**JustDied#2** (method): Calls DespawnPortal.
**UpdatePortalTentacle** (method): Updates base tentacle; casts ground rupture if timer expires and birth animation is done.
**FixPortalPosition** (method): Samples ground height around tentacle to place portal at optimal Z.
**clawTentacle** (ctor): Initializes evade, hamstring, and submerge timers.
**Reset#3** (method): Calls parent Reset, resets timers, sets full health, sets state to NORMAL.
**UpdateClawTentacle** (method): State machine handler for NORMAL, FEIGN_IN_PROCES, and BURRIED states.
**updateNormal** (method): Performs melee; if no target for timeout, transitions to FEIGN_IN_PROCES.
**updateFeign** (method): Waits briefly, then transitions to BURRIED and hides visibility.
**updateBurried** (method): After delay, teleports to new target, resets threat/auras/visibility, and resets AI.
**TeleportOnNewRandomTarget** (method): Finds random target, teleports tentacle near them, fixes portal position.
**setVisibility** (method): Toggles visibility for tentacle and portal.
**eye_tentacleAI** (ctor): Initializes with small portal and physical ground rupture.
**Reset#9** (method): Calls parent Reset, resets Mind Flay timer and target.
**AttackStart#3** (method): Prevents interrupting channeling spells.
**UpdateAI#4** (method): Casts Mind Flay on random target if not channeling; interrupts if target goes to stomach; falls back to melee.
**claw_tentacleAI** (ctor): Initializes with small portal and physical ground rupture.
**Reset#4** (method): Calls parent Reset.
**UpdateAI** (method): Delegates to clawTentacle::UpdateClawTentacle.
**giant_claw_tentacleAI** (ctor): Initializes with giant portal, nature ground rupture, and additional spell timers.
**Reset#11** (method): Calls parent Reset, resets additional timers.
**UpdateAI#6** (method): Delegates to clawTentacle::UpdateClawTentacle and updates additional timers.
**giant_eye_tentacleAI** (ctor): Initializes with giant portal and nature ground rupture.
**Reset#12** (method): Calls parent Reset, resets beam timer.
**UpdateAI#7** (method): Casts Green Beam on random target; interrupts if target goes to stomach; falls back to melee.
**flesh_tentacleAI** (ctor): Initializes base tentacle.
**Reset#10** (method): Calls parent Reset.
**UpdateAI#5** (method): Continuously attempts melee attacks.
**eye_of_cthunAI** (ctor): Disables combat movement, links to instance data.
**Pull** (method): Sets faction, records initial puller, casts first green beam.
**Aggro#2** (method): Triggers C'Thun body AttackStart if eye not in combat.
**Reset#8** (method): Resets phase to GREEN_BEAM, clears flags, removes glare spells.
**UpdateAI#3** (method): State machine for Phase 1 (Green Beam, Dark Glare Cast, Dark Glare, Cooling).
**UpdateGreenBeamPhase** (method): Casts green beam on target (random or initial puller).
**AttackStart#2** (method): Empty override.
**EnterDarkGlarePhase** (method): Interrupts spells, faces random target, casts rotation trigger.
**RemoveGlarePhaseSpells** (method): Removes rotation auras.
**CastGreenBeam** (method): Executes green beam cast, updates counters.
**cthunAI** (ctor): Spawns visual portal, links to instance data.
**AttackStart** (method): Triggers Eye Pull, sets combat flags, handles delayed combat entry.
**DespawnAllTentacles** (method): Finds and unsuns all tentacles and their portals.
**JustReachedHome** (method): Marks instance as failed.
**Reset#5** (method): Resets timers/phases/visibility, despawns tentacles, respawns eye.
**CheckRespawnEye** (method): Respawns eye creature if dead or missing.
**SummonedCreatureJustDied** (method): Tracks flesh tent

---

<!-- machine-true, projected from graph.json -->

## Map — boss_cthun

*Source:* boss_cthun.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| SpellTimer | ctor | — | — | — |
| Reset#2 | method | — | — | — |
| Update#2 | method | Creature.Main/AI, CreatureAI/DoCastSpellIfCan | — | — |
| OnlyOnceSpellTimer | ctor | — | — | — |
| Reset | method | — | — | — |
| Update | method | — | — | — |
| SelectRandomAliveNotStomach | function | instance_temple_of_ahnqiraj/PlayerInStomach, LinkedListHead/isEmpty, Map.Main/GetPlayers, Player.Main/IsGameMaster, Unit.Main/IsDead, Unit.Main/IsInCombat, ZoneScript/GetMap#2 | — | — |
| selectSelfFunc | function | — | — | — |
| selectTargetFunc | function | Unit.Main/GetVictim | — | — |
| hamstringResetCooldownFunc | function | — | — | — |
| trashResetCooldownFunc | function | shared_Util/urand | — | — |
| groundTremorResetCooldownFunc | function | shared_Util/urand | — | — |
| cthunTentacle | ctor | CreatureAI/SetCombatMovement, Log.Main/Out, ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData, WorldObject.Object/GetOrientation | — | — |
| Reset#7 | method | Creature.Main/SetInCombatWithZone, Unit.Main/AddUnitState, Unit.Main/SetRooted, Unit.Main/StopMoving | — | — |
| Aggro | method | Creature.Main/SetInCombatWithZone, ScriptedAI/Aggro | — | — |
| UpdateCthunTentacle | method | Log.Main/Out, ScriptedInstance/GetPlayerInMap, TemporarySummon/UnSummon, Unit.Main/AddUnitState, Unit.Main/SetRooted, Unit.Main/StopMoving | — | — |
| UpdateMelee | method | CreatureAI/DoMeleeAttackIfReady, ScriptedAI/DoStopAttack, WorldObject.Object/SetOrientation | — | — |
| SelectHostileTargetMelee | method | Aura/GetCaster, Creature.Main/SelectAttackingTarget, CreatureAI/AttackStart, SpellCaster/IsNonMeleeSpellCasted, ThreatManager/getHostileTarget, ThreatManager/modifyThreatPercent#2, Unit.Main/CanReachWithMeleeAutoAttack, Unit.Main/GetAurasByType, Unit.Main/GetThreatManager, Unit.Main/GetVictim, Unit.Main/HasAuraType, Unit.Main/HasUnitState, Unit.Main/IsAlive, Unit.Main/IsTargetableBy, Unit.Main/SetInFront, WorldObject.Object/IsInMap | — | — |
| cthunPortalTentacle | ctor | Creature.Main/SetInCombatWithZone, Log.Main/Out, Object/GetGUID, ObjectGuid/ObjectGuid#5, ScriptedAI/DoSpawnCreature | — | — |
| DespawnPortal | method | Log.Main/Out, Map.Main/GetCreature, ObjectGuid/ObjectGuid#5, TemporarySummon/UnSummon, WorldObject.Object/GetMap | — | — |
| Reset#6 | method | Unit.Main/AttackStop | — | — |
| JustDied#2 | method | — | — | — |
| UpdatePortalTentacle | method | CreatureAI/DoCastSpellIfCan | — | — |
| FixPortalPosition | method | CreatureAI/SetCombatMovement, CreatureAI/SetMeleeAttack, Log.Main/Out, Map.Main/GetHeight, Object/GetEntry, Unit.Main/AI, Unit.Main/NearLandTo, WorldObject.Object/GetMap, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, ZoneScript/GetCreature | — | — |
| clawTentacle | ctor | — | — | — |
| Reset#3 | method | Unit.Main/SetFullHealth | — | — |
| UpdateClawTentacle | method | Log.Main/Out | — | — |
| updateNormal | method | SpellCaster/CastSpell#2 | — | — |
| updateFeign | method | — | — | — |
| updateBurried | method | ScriptedAI/DoResetThreat, Unit.Main/RemoveAurasDueToSpell | — | — |
| TeleportOnNewRandomTarget | method | Map.Main/GetCreature, Unit.Main/NearTeleportTo, WorldObject.Object/GetMap, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/GetRandomPoint | — | — |
| setVisibility | method | Map.Main/GetCreature, Unit.Main/SetVisibility, WorldObject.Object/GetMap | — | — |
| eye_tentacleAI | ctor | — | — | — |
| Reset#9 | method | ObjectGuid/ObjectGuid#5 | — | — |
| AttackStart#3 | method | CreatureAI/AttackStart, SpellCaster/IsNonMeleeSpellCasted | — | — |
| UpdateAI#4 | method | CreatureAI/DoCastSpellIfCan, instance_temple_of_ahnqiraj/PlayerInStomach, Map.Main/GetPlayer, Object/GetGUID, Object/HasFlag, ObjectGuid/ObjectGuid#5, SpellCaster/GetCurrentSpell, SpellCaster/InterruptSpell, Unit.Main/SetFacingToObject, Unit.Main/SetTargetGuid, WorldObject.Object/GetMap | — | — |
| claw_tentacleAI | ctor | — | — | — |
| Reset#4 | method | — | — | — |
| UpdateAI | method | — | — | — |
| giant_claw_tentacleAI | ctor | — | — | — |
| Reset#11 | method | — | — | — |
| UpdateAI#6 | method | — | — | — |
| giant_eye_tentacleAI | ctor | — | — | — |
| Reset#12 | method | — | — | — |
| UpdateAI#7 | method | CreatureAI/DoCastSpellIfCan, instance_temple_of_ahnqiraj/PlayerInStomach, Map.Main/GetPlayer, Object/GetObjectGuid, Object/HasFlag, ObjectGuid/ObjectGuid#5, SpellCaster/GetCurrentSpell, SpellCaster/InterruptNonMeleeSpells, WorldObject.Object/GetMap | — | — |
| flesh_tentacleAI | ctor | — | — | — |
| Reset#10 | method | — | — | — |
| UpdateAI#5 | method | — | — | — |
| eye_of_cthunAI | ctor | CreatureAI/SetCombatMovement, Log.Main/Out, ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Pull | method | Creature.Main/SetFactionTemporary, Object/GetObjectGuid | — | — |
| Aggro#2 | method | Creature.Main/AI, CreatureAI/AttackStart, ScriptedInstance/GetSingleCreatureFromStorage, Unit.Main/IsInCombat | — | — |
| Reset#8 | method | Log.Main/Out, ObjectGuid/ObjectGuid#5, WorldObject.Object/RemoveFlag, WorldObject.Object/SetOrientation | — | — |
| UpdateAI#3 | method | Log.Main/Out, ObjectGuid/ObjectGuid#5, Unit.Main/SetTargetGuid | — | — |
| UpdateGreenBeamPhase | method | Unit.Main/HasAura#2, Unit.Main/RemoveAurasDueToSpell | — | — |
| AttackStart#2 | method | — | — | — |
| EnterDarkGlarePhase | method | Creature.Main/SelectAttackingTarget, CreatureAI/DoCastSpellIfCan, ObjectGuid/ObjectGuid, ScriptedAI/DoStopAttack, SpellCaster/CastSpell#2, SpellCaster/InterruptNonMeleeSpells, Unit.Main/HasAura#2, Unit.Main/SetFacingToObject, Unit.Main/SetTargetGuid | — | — |
| RemoveGlarePhaseSpells | method | Unit.Main/HasAura#2, Unit.Main/RemoveAurasDueToSpell | — | — |
| CastGreenBeam | method | CreatureAI/DoCastSpellIfCan, Object/GetObjectGuid, Unit.Main/SetTargetGuid | — | — |
| cthunAI | ctor | CreatureAI/SetCombatMovement, Log.Main/Out, ScriptedAI/DoSpawnCreature, ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData, WorldObject.Object/SetFlag | — | — |
| AttackStart | method | Creature.Main/AI, CreatureAI/AttackStart, instance_temple_of_ahnqiraj/SetData, Log.Main/Out, Unit.Main/IsInCombat, Unit.Main/SetInCombatWith, ZoneScript/GetCreature | — | — |
| DespawnAllTentacles | method | Creature.Main/AI, GridSearchers/GetCreatureListWithEntryInGrid, TemporarySummon/UnSummon | — | — |
| JustReachedHome | method | instance_temple_of_ahnqiraj/SetData | — | — |
| Reset#5 | method | SpellCaster/InterruptNonMeleeSpells, Unit.Main/DeMorph, Unit.Main/GetMaxHealth, Unit.Main/HasAura#2, Unit.Main/IsAlive, Unit.Main/RemoveAurasDueToSpell, Unit.Main/SetHealth, Unit.Main/SetVisibility, WorldObject.Object/SetFlag | — | — |
| CheckRespawnEye | method | Creature.Main/AI, Creature.Main/ForcedDespawn, Creature.Main/Respawn, Log.Main/Out, Object/GetGUID, ObjectGuid/ObjectGuid#5, ScriptedAI/DoSpawnCreature, ScriptedAI/EnterEvadeMode, Unit.Main/IsAlive, Unit.Main/IsDead, ZoneScript/GetCreature | — | — |
| SummonedCreatureJustDied | method | Object/GetEntry, Object/GetObjectGuid | — | — |
| JustSummoned | method | Log.Main/Out, Object/GetEntry, Object/GetGUID | — | — |
| UpdateAI#2 | method | Creature.Main/SetInCombatWithZone, Log.Main/Out, ObjectGuid/ObjectGuid#5, ScriptedAI/EnterEvadeMode, ScriptedInstance/GetPlayerInMap, Unit.Main/IsDead, Unit.Main/SetTargetGuid, ZoneScript/GetCreature | — | — |
| SummonedCreatureDespawn | method | Object/GetEntry, Object/GetObjectGuid, SpellCaster/CastSpell#2, Unit.Main/SetVisibility, WorldObject.Object/RemoveFlag | — | — |
| JustDied | method | instance_temple_of_ahnqiraj/SetData | — | — |
| ResetartUnvulnerablePhase | method | ObjectGuid/ObjectGuid#5, SpellCaster/CastSpell#2 | — | — |
| UnitShouldPull | method | WorldObject.Object/GetDistance3dToCenter#3, WorldObject.Object/GetPositionZ, WorldObject.Object/IsWithinLOSInMap | — | — |
| AggroRadius | method | Map.Main/GetPlayers, Player.Main/IsGameMaster, Unit.Main/GetPet, Unit.Main/IsAlive, WorldObject.Object/GetMap | — | — |
| CheckIfAllDead | method | Creature.Main/OnLeaveCombat, instance_temple_of_ahnqiraj/KillPlayersInStomach | — | — |
| UpdateTentaclesP1 | method | — | — | — |
| UpdateTransitionPhase | method | Creature.Main/SetInCombatWithZone, WorldObject.Object/RemoveFlag | — | — |
| UpdateInvulnerablePhase | method | Map.Main/GetPlayer, ObjectGuid/IsEmpty, ScriptMgr/DoScriptText, SpellCaster/CastSpell#2, Unit.Main/RemoveAurasDueToSpell, WorldObject.Object/GetMap | — | — |
| UpdateWeakenedPhase | method | Unit.Main/RemoveAurasDueToSpell | — | — |
| SpawnFleshTentacles | method | Log.Main/Out, WorldObject.Object/SummonCreature#2 | — | — |
| UpdateStomachGrab | method | instance_temple_of_ahnqiraj/AddPlayerToStomach, Map.Main/GetPlayer, Object/GetObjectGuid, ObjectGuid/IsEmpty, ObjectGuid/ObjectGuid#5, ScriptedAI/DoTeleportPlayer, SpellCaster/CastSpell#2, SpellCaster/InterruptNonMeleeSpells, Unit.Main/RemoveAurasDueToSpell, WorldObject.Object/GetMap | — | — |
| UpdateTentaclesP2 | method | — | — | — |
| SpawnEyeTentacles | method | Creature.Main/SetInCombatWithZone, WorldObject.Object/SummonCreature#2 | — | — |
| SpawnTentacleIfReady | method | Creature.Main/AI, CreatureAI/AttackStart, Log.Main/Out, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/GetRandomPoint, WorldObject.Object/SummonCreature#2 | — | — |
| GetAI_eye_of_cthun | function | — | — | — |
| GetAI_cthun | function | — | — | — |
| GetAI_eye_tentacle | function | — | — | — |
| GetAI_claw_tentacle | function | — | — | — |
| GetAI_giant_claw_tentacle | function | — | — | — |
| GetAI_giant_eye_tentacle | function | — | — | — |
| GetAI_flesh_tentacle | function | — | — | — |
| AddSC_boss_cthun | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |

---

<!-- verify: failed-members | missing: UpdateAI#2 -->
