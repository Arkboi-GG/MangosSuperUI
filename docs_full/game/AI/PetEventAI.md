<!-- provenance: verbose -->
# PetEventAI

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# PetEventAI

**PetEventAI** is a specialized AI module for `Creature` entities acting as pets or guardians. It extends `CreatureEventAI` to implement pet-specific behaviors: following owners, reacting to owner aggression, and enforcing combat rules such as avoiding civilian targets, respecting command states (Passive/Defensive/Aggressive), and preventing the breaking of crowd-control (CC) effects on allies.

It serves two creature types:
1.  **Scripted Pets:** Creatures explicitly assigned `"PetEventAI"`.
2.  **Auto-Assigned Event Pets:** Creatures assigned `"EventAI"` that are identified as pets (`IsPet()` is true). The `Permissible` method routes these to `PetEventAI` instead of the base `CreatureEventAI`.

No database tables are accessed; all state is managed via in-memory objects (`Unit`, `Creature`, `CharmInfo`, `ThreatManager`).

## Behavior & Responsibilities

### Initialization and Eligibility
The constructor initializes `CreatureEventAI`. The static `Permissible` method acts as a factory guard, returning `PERMIT_BASE_SPECIAL` if the creature’s AI name is `"PetEventAI"` or if it is a pet with AI name `"EventAI"`. Otherwise, it returns `PERMIT_BASE_NO`.

### Line-of-Sight and Aggro Detection
`MoveInLineOfSight` evaluates whether to initiate an attack when a unit enters view. It returns early if:
*   The pet already has a victim.
*   The pet is returning to its owner (`IsReturning` is true).
*   The target is outside vertical attack range (`CREATURE_Z_ATTACK_RANGE`).
*   The target is a civilian (on clients > 1.7.1).

If the pet can initiate attack, the target is targetable, within horizontal range, accessible, hostile, and visible, it calls `AttackStart`. It also triggers base event updates via `CreatureEventAI::UpdateEventsOn_MoveInLineOfSight`.

### Combat Initiation
`AttackStart` enforces pet-specific constraints before attacking:
*   **Enablement:** Disabled pets cannot attack.
*   **Owner Safety:** Pets never attack their charmer/owner.
*   **Command States:**
    *   **Passive:** Does not attack unless commanded.
    *   **PvP:** Non-PvP pets do not attack PvP-flagged targets unless commanded.
    *   **CC Protection:** Does not attack targets with `HasAuraPetShouldAvoidBreaking()` if the owner is alive.

If valid, it establishes combat links, adds threat, and sets both pet and target as in combat. If the owner is a player not in combat, an Aggressive pet pulls the owner into combat and sends reaction updates. It toggles PvP flags on the owner if needed and initiates chase movement if combat movement is enabled.

### Target Selection
`FindTargetForAttack` selects the next target in this priority order:
1.  **Taunt Target:** If taunted, attack the taunter.
2.  **Threat List:** Top hostile target from `ThreatManager`, skipping those with CC-protection auras.
3.  **Direct Attackers:** First unit attacking the pet, if valid.
4.  **Owner's Attackers:** If no direct attackers, checks units attacking the owner. It skips the primary attacker if protected by CC, iterating through others to find a valid, unprotected target.

### Main Loop
`UpdateAI` manages state transitions:
*   **Combat Maintenance:** If the pet or owner is in combat, it calls `FindTargetForAttack`. If a new valid target differs from the current victim, it calls `AttackStart`.
*   **Event Processing:** Updates scripted events via `CreatureEventAI::UpdateEventsOn_UpdateAI`.
*   **Actions:**
    *   **In Combat:** Updates spells and performs melee attacks.
    *   **Out of Combat:** If the pet is in combat but the owner is not, it leaves combat. If the owner commands a follow and the pet is not already following, it initiates `MoveFollow` (using `Pet::GetFollowAngle` for pets) and sets `IsReturning` to suppress aggro checks.

### Owner Interaction
*   **`OwnerAttackedBy`:** Triggered when the owner takes damage. If the pet has no victim, it validates the attacker (ensuring it’s not the owner) and calls `AttackStart`.
*   **`OwnerAttacked`:** Triggered when the owner attacks. If the pet has no victim, it validates the target and calls `AttackStart`.

### Movement Feedback
`MovementInform` handles motion master notifications. If the pet completes a follow movement (`FOLLOW_MOTION_TYPE`) to its owner (verified by GUID), it clears `IsReturning`, re-enabling aggro detection.

## Cross-Unit Boundaries

*   **`CreatureEventAI`:** Inherits from it. Calls `UpdateEventsOn_MoveInLineOfSight`, `UpdateEventsOn_UpdateAI`, and `MovementInform` for scripted event processing.
*   **`Creature` / `Pet`:** Uses `IsPet`, `IsCivilian`, `GetAIName`, `CanInitiateAttack`, `OnLeaveCombat`. Casts to `Pet` for `IsEnabled` and `GetFollowAngle`.
*   **`Unit` / `CharmInfo`:** Relies on `GetCharmerOrOwner`, `GetCharmInfo`, `IsCommandAttack`, `HasReactState`, `IsPvP`, `HasAuraPetShouldAvoidBreaking`, `Attack`, `AddThreat`, `SetInCombatWith`, `TogglePlayerPvPFlagOnAttackVictim`, `SendPetAIReaction`, `SetIsReturning`, `IsReturning`.
*   **`ThreatManager`:** Queries `isThreatListEmpty` and `getHostileTarget` for targeting.
*   **`MotionMaster`:** Calls `MoveChase` and `MoveFollow`.

## Data Model

No database tables are accessed.

## Notable Implementation Details

1.  **Civilian Exemption:** `MoveInLineOfSight` uses `#if SUPPORTED_CLIENT_BUILD > CLIENT_BUILD_1_7_1` to skip civilian targets, reflecting a WoW client change.
2.  **CC Breaking Prevention:** `AttackStart` and `FindTargetForAttack` check `HasAuraPetShouldAvoidBreaking()` to prevent pets from breaking CC on allies/enemies. `FindTargetForAttack` iterates owner attackers to bypass protected primary targets.
3.  **Return State Suppression:** `IsReturning` in `CharmInfo` suppresses aggro in `MoveInLineOfSight` during follow movements. Set in `UpdateAI`, cleared in `MovementInform`.
4.  **Owner Pull:** `AttackStart` pulls non-combatant player owners into combat if the pet is Aggressive.

## Member Reference

**PetEventAI**
Constructor initializing `CreatureEventAI`.

**~PetEventAI**
Empty destructor.

**Permissible**
Static method returning `PERMIT_BASE_SPECIAL` if AI name is `"PetEventAI"` or if pet with `"EventAI"`; else `PERMIT_BASE_NO`.

**MoveInLineOfSight**
Checks existing victim, return state, vertical range, and civilian status. Triggers base events. Calls `AttackStart` if target is valid, hostile, and within range/LOS.

**AttackStart**
Validates pet enablement, owner safety, command states, and CC protection. Establishes combat, threat, and owner engagement. Initiates chase movement.

**FindTargetForAttack**
Selects target: taunt > threat list (skip CC) > direct attackers > owner attackers (skip CC). Returns `nullptr` if none.

**UpdateAI**
Manages combat maintenance, event updates, spell/melee execution, and follow movement initiation with `IsReturning` flag.

**OwnerAttackedBy**
If no victim, validates attacker (not owner) and calls `AttackStart`.

**OwnerAttacked**
If no victim, validates target and calls `AttackStart`.

**MovementInform**
Delegates to base. Clears `IsReturning` if follow movement to owner completes.

---

<!-- machine-true, projected from graph.json -->

## Map — PetEventAI

*Source:* PetEventAI.cpp, PetEventAI.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| PetEventAI | ctor | CreatureEventAI/CreatureEventAI | — | — |
| Permissible | method | Creature.Main/GetAIName, Creature.Main/IsPet | — | — |
| ~PetEventAI | dtor | — | — | — |
| MoveInLineOfSight | method | Creature.Main/CanInitiateAttack, Creature.Main/GetAttackDistance, Creature.Main/IsCivilian, Creature.Main/IsPet, CreatureEventAI/UpdateEventsOn_MoveInLineOfSight, Object/IsCreature, Unit.Main/GetCharmInfo, Unit.Main/GetVictim, Unit.Main/IsHostileTo, Unit.Main/IsInAccessablePlaceFor, Unit.Main/IsReturning, Unit.Main/IsTargetableBy, WorldObject.Object/GetDistanceZ, WorldObject.Object/IsWithinDistInMap, WorldObject.Object/IsWithinLOSInMap | — | — |
| AttackStart | method | Creature.Main/IsPet, Creature.MotionMaster/MoveChase, Object/IsPlayer, Pet.Main/IsEnabled, Unit.Main/AddThreat, Unit.Main/Attack, Unit.Main/GetCharmerOrOwner, Unit.Main/GetCharmInfo, Unit.Main/GetMotionMaster, Unit.Main/HasAuraPetShouldAvoidBreaking, Unit.Main/HasReactState, Unit.Main/IsAlive, Unit.Main/IsCommandAttack, Unit.Main/IsInCombat, Unit.Main/IsPvP, Unit.Main/SendPetAIReaction, Unit.Main/SetInCombatWith, Unit.Main/SetIsReturning, Unit.Main/TogglePlayerPvPFlagOnAttackVictim | — | — |
| FindTargetForAttack | method | ThreatManager/getHostileTarget, ThreatManager/isThreatListEmpty, Unit.Main/CanHaveThreatList, Unit.Main/GetAttackerForHelper, Unit.Main/GetAttackers, Unit.Main/GetCharmerOrOwner, Unit.Main/GetTauntTarget, Unit.Main/GetThreatManager, Unit.Main/HasAuraPetShouldAvoidBreaking, Unit.Main/IsInCombat, WorldObject.Object/IsInMap, WorldObject.Object/IsValidAttackTarget | — | — |
| UpdateAI | method | CharmInfo/HasCommandState, Creature.Main/IsPet, Creature.Main/OnLeaveCombat, Creature.MotionMaster/MoveFollow, CreatureAI/DoMeleeAttackIfReady, CreatureAI/UpdateSpellsList, CreatureEventAI/UpdateEventsOn_UpdateAI, Pet.Main/GetFollowAngle, Unit.Main/GetCharmerOrOwner, Unit.Main/GetCharmInfo, Unit.Main/GetMotionMaster, Unit.Main/GetVictim, Unit.Main/HasUnitState, Unit.Main/IsAlive, Unit.Main/IsInCombat, Unit.Main/SetIsReturning | — | — |
| OwnerAttackedBy | method | Unit.Main/GetVictim, Unit.Main/IsAlive, WorldObject.Object/IsValidAttackTarget | — | — |
| OwnerAttacked | method | Unit.Main/GetVictim, Unit.Main/IsAlive, WorldObject.Object/IsValidAttackTarget | — | — |
| MovementInform | method | CreatureEventAI/MovementInform, ObjectGuid/GetCounter, Unit.Main/GetCharmerOrOwnerGuid, Unit.Main/GetCharmInfo, Unit.Main/IsReturning, Unit.Main/SetIsReturning | — | — |
