<!-- provenance: verbose -->
# BasicAI

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# BasicAI

**BasicAI** is the default `CreatureAI` implementation in wowvmangos, providing baseline behavior for non-scripted NPCs. It handles proximity-based aggro detection, a standard melee combat loop, and logic for civilian creatures to summon guards. Many specialized boss scripts and other AI types (e.g., `CreatureEventAI`, `ScriptedEscortAI`) explicitly call `BasicAI::MoveInLineOfSight` to reuse its comprehensive aggro checks.

## Purpose & Responsibilities

1.  **Proximity Aggro**: Determines if a creature should enter combat when a hostile unit enters its line of sight, applying filters for PvP flags, dungeon status, and existing combat states.
2.  **Combat Loop**: Manages target selection and executes melee attacks or predefined spells via `UpdateAI`.
3.  **Guard Summoning**: Tracks the state of guard summoning (`m_bCanSummonGuards`) to prevent spam, resetting the state on respawn or when a summoned guard despawns.

## Member-by-Member Behavior

### Initialization and State

**BasicAI**
Initializes the base `CreatureAI` and sets `m_bCanSummonGuards` based on the creature's initial capability (`Creature.Main/CanSummonGuards`).

**Permissible**
Returns `PERMIT_BASE_NORMAL`, indicating standard participation in the threat system.

### Aggro and Threat Detection

**IsProximityAggroAllowedFor**
Determines if aggro is allowed for `pTarget`:
1.  Denies aggro if the creature has `CREATURE_STATIC_FLAG_ONLY_ATTACK_PVP_ENABLING` and the target is not in PvP (`Unit.Main/IsPvP`) but is a player or charmed player (`Unit.Main/IsCharmerOrOwnerPlayerOrPlayerItself`).
2.  Allows aggro if the creature has no current victim (`Unit.Main/GetVictim`).
3.  Allows aggro if the target is the current victim.
4.  Allows aggro if the creature is in a dungeon (`Map.Main/IsDungeon`).
5.  Otherwise, allows aggro only if the creature has `CREATURE_FLAG_EXTRA_NO_LEASH_EVADE` (`Creature.Main/HasExtraFlag`).

**MoveInLineOfSight**
Handles aggro when `pWho` enters line of sight:
1.  Exits if `IsProximityAggroAllowedFor(pWho)` is false.
2.  Calculates `canInitiateAttack` (if `pWho` is not the victim and `Creature.Main/CanInitiateAttack` is true) and `canSummonGuard` (if `pWho` is a player (`Object/IsPlayer`) and `CanSummonGuards` is true). Exits if both are false.
3.  Exits if the creature cannot fly (`Creature.Main/CanFly`) and the vertical distance (`WorldObject.Object/GetDistanceZ`) exceeds `CREATURE_Z_ATTACK_RANGE`.
4.  Calculates `maxDistance` as `Creature.Main/GetAttackDistance` (if attacking) or `Creature.Main/GetDetectionRange` (if summoning).
5.  Enters combat (`Creature.Main/EnterCombatWithTarget`) or summons a guard (`SummonGuard`) if the target is within `maxDistance` (`WorldObject.Object/IsWithinDistInMap`), targetable (`Unit.Main/IsTargetableBy`), hostile (`Unit.Main/IsHostileTo`), in line of sight (`WorldObject.Object/IsWithinLOSInMap`), and accessible (`Unit.Main/IsInAccessablePlaceFor`).

This method is called by numerous boss scripts (e.g., `boss_anubrekhan`, `boss_archaedas`) and other AIs (`CreatureEventAI`, `ScriptedEscortAI`) to leverage its comprehensive aggro checks.

### Combat Loop

**UpdateAI**
Executes the main combat tick:
1.  Returns if no hostile target is selected (`Unit.Main/SelectHostileTarget`) or no victim exists (`Unit.Main/GetVictim`).
2.  Updates spell timers via `CreatureAI/UpdateSpellsList` if `m_CreatureSpells` is not empty.
3.  Performs melee attacks via `CreatureAI/DoMeleeAttackIfReady`.

### Guard Summoning System

**CanSummonGuards**
Returns the `m_bCanSummonGuards` flag.

**SummonGuard**
Calls `GuardMgr/SummonGuard` to summon a guard against `pEnemy`. Sets `m_bCanSummonGuards` to the negation of the result, disabling further summons if successful.

**JustRespawned**
Resets `m_bCanSummonGuards` to true if the creature can summon guards (`Creature.Main/CanSummonGuards`), then calls `CreatureAI/JustRespawned`.

**SummonedCreatureDespawn**
If the despawning `pSummon` is a guard (`Creature.Main/IsGuard`) and the creature can summon guards, resets `m_bCanSummonGuards` to true.

## Cross-Unit Boundaries

*   **Creature**: Queries state (`CanSummonGuards`, `HasExtraFlag`, `HasStaticFlag`, `CanFly`, `CanInitiateAttack`, `GetVictim`, `GetAttackDistance`, `GetDetectionRange`, `IsGuard`) and initiates combat (`EnterCombatWithTarget`).
*   **Unit**: Checks relationships (`IsHostileTo`, `IsTargetableBy`, `IsInAccessablePlaceFor`, `IsCharmerOrOwnerPlayerOrPlayerItself`, `IsPvP`, `GetVictim`, `SelectHostileTarget`).
*   **WorldObject/Object**: Handles spatial checks (`GetDistanceZ`, `IsWithinDistInMap`, `IsWithinLOSInMap`, `GetMap`, `IsPlayer`).
*   **Map**: Checks `IsDungeon` to override aggro restrictions.
*   **GuardMgr**: Delegates guard creation via `SummonGuard`.
*   **CreatureAI**: Inherits from and calls `JustRespawned`, `DoMeleeAttackIfReady`, and `UpdateSpellsList`.

## Data Model

**BasicAI** does not interact directly with any database tables.

## Notable Implementation Details

1.  **Guard Flag Inversion**: `SummonGuard` sets `m_bCanSummonGuards = !sGuardMgr.SummonGuard(...)`. Assuming `SummonGuard` returns true on success, this disables further summons until reset by `JustRespawned` or `SummonedCreatureDespawn`.
2.  **Vertical Optimization**: `MoveInLineOfSight` checks vertical distance before expensive LOS checks, preventing ground mobs from aggroing vertically distant targets.
3.  **Dungeon Override**: `IsProximityAggroAllowedFor` always allows aggro in dungeons, ensuring predictable pulls regardless of leash flags.

## Member Reference

**BasicAI**
Constructor initializing the AI and guard summoning capability.

**Permissible**
Static method returning `PERMIT_BASE_NORMAL`.

**IsProximityAggroAllowedFor**
Checks PvP flags, victim status, dungeon location, and leash flags to determine if aggro is allowed.

**CanSummonGuards**
Accessor for the guard summoning flag.

**MoveInLineOfSight**
Core aggro logic: checks permissions, distances, LOS, and accessibility to enter combat or summon guards. Reused by many boss scripts.

**JustRespawned**
Resets guard summoning flag if applicable and calls parent respawn handler.

**SummonedCreatureDespawn**
Resets guard summoning flag if the despawner was a guard.

**UpdateAI**
Main combat loop: selects targets, updates spells, and performs melee attacks.

**SummonGuard**
Delegates guard summoning to `GuardMgr` and updates the internal flag.

---

<!-- machine-true, projected from graph.json -->

## Map — BasicAI

*Source:* BasicAI.cpp, BasicAI.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| BasicAI | ctor | Creature.Main/CanSummonGuards, CreatureAI/CreatureAI | CreatureEventAI/CreatureEventAI, ScriptedAI/ScriptedAI, ThreatListCopier.battleground_alterac/GetAI_AV_Mines_AI | — |
| Permissible | method | — | — | — |
| IsProximityAggroAllowedFor | method | Creature.Main/HasExtraFlag, Creature.Main/HasStaticFlag, Map.Main/IsDungeon, Unit.Main/GetVictim, Unit.Main/IsCharmerOrOwnerPlayerOrPlayerItself, Unit.Main/IsPvP, WorldObject.Object/GetMap | — | — |
| CanSummonGuards | method | — | — | — |
| MoveInLineOfSight | method | Creature.Main/CanFly, Creature.Main/CanInitiateAttack, Creature.Main/EnterCombatWithTarget, Creature.Main/GetAttackDistance, Creature.Main/GetDetectionRange, Object/IsPlayer, Unit.Main/GetVictim, Unit.Main/IsHostileTo, Unit.Main/IsInAccessablePlaceFor, Unit.Main/IsTargetableBy, WorldObject.Object/GetDistanceZ, WorldObject.Object/IsWithinDistInMap, WorldObject.Object/IsWithinLOSInMap | boss_anubrekhan/MoveInLineOfSight, boss_archaedas/MoveInLineOfSight, boss_bug_trio/MoveInLineOfSight, boss_faerlina/MoveInLineOfSight, boss_fankriss/MoveInLineOfSight, boss_fankriss/MoveInLineOfSight#2, boss_gluth/MoveInLineOfSight, boss_huhuran/MoveInLineOfSight, boss_magistrate_barthilas/MoveInLineOfSight, boss_ouro/MoveInLineOfSight#3, boss_sartura/MoveInLineOfSight, boss_sartura/MoveInLineOfSight#2, boss_skeram/MoveInLineOfSight, boss_vectus/MoveInLineOfSight, boss_viscidus/MoveInLineOfSight, CreatureEventAI/MoveInLineOfSight, instance_blackwing_lair/MoveInLineOfSight, instance_naxxramas.boss_kelthuzad/MoveInLineOfSight#2, instance_naxxramas.Main/MoveInLineOfSight, mob_anubisath_sentinel/MoveInLineOfSight, scourge_invasion/MoveInLineOfSight, scourge_invasion/MoveInLineOfSight#2, ScriptedEscortAI/MoveInLineOfSight | — |
| JustRespawned | method | Creature.Main/CanSummonGuards, CreatureAI/JustRespawned | CreatureEventAI/JustRespawned | — |
| SummonedCreatureDespawn | method | Creature.Main/CanSummonGuards, Creature.Main/IsGuard | CreatureEventAI/SummonedCreatureDespawn | — |
| UpdateAI | method | CreatureAI/DoMeleeAttackIfReady, CreatureAI/UpdateSpellsList, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | duskwood/UpdateAI#2, duskwood/UpdateAI#4, dustwallow_marsh/UpdateAI#3, npcs_special/UpdateAI#11, npcs_special/UpdateAI#3, quest_stormwind_rendezvous/UpdateAI#2, stormwind_city/UpdateAI, thousand_needles/UpdateAI, world_event_wareffort/UpdateAI#4 | — |
| SummonGuard | method | GuardMgr/SummonGuard | — | — |
