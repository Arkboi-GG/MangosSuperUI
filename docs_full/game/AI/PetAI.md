# PetAI

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# PetAI

**PetAI** is the artificial intelligence controller for summoned pets and charmed creatures in the `wowvmangos` server. It inherits from `CreatureAI` and specializes in managing the complex state machine required for pet behavior, including following owners, staying at positions, reacting to owner aggression, selecting targets based on threat or owner commands, and autocasting spells. It handles both player-controlled pets (via `Pet`) and creature-controlled pets (NPC summons), adapting its logic based on the type of owner and the current command state (Follow, Stay, Attack).

## Purpose & Responsibilities

The primary responsibility of `PetAI` is to bridge the gap between high-level player/NPC commands and low-level combat/movement actions. Key responsibilities include:

1.  **State Management:** Tracking whether the pet is following, staying, returning to a position, or actively attacking. It manages flags like `IsAtStay`, `IsFollowing`, `IsReturning`, and `IsCommandAttack` via `CharmInfo`.
2.  **Target Selection:** Deciding whom to attack. This involves checking if the owner is attacking, if the pet itself is attacked, if the owner is attacked, or if there is a higher-threat target on the threat list (for NPC pets). It respects react states (Passive, Defensive, Aggressive) and command overrides.
3.  **Movement Control:** Initiating chase movements when attacking, idle movements when staying, and point/follow movements when returning to a stay position or following the owner.
4.  **Spell Autocasting:** Periodically evaluating the pet's spellbook to cast beneficial or harmful spells automatically, respecting cooldowns, GCDs, and target validity.
5.  **Ally Tracking:** Maintaining a set of valid friendly targets (`m_AllySet`) for buffs, updated periodically to reflect group membership changes.

## Member-by-Member Behavior

### Initialization and Permissibility

*   **`Permissible`**: A static method that determines if a `Creature` can use this AI. It returns `PERMIT_BASE_SPECIAL` if the creature is a pet (`IsPet()`), otherwise `PERMIT_BASE_NO`. This ensures only appropriate entities instantiate this AI.
*   **`PetAI` (Constructor)**: Initializes the AI. It calls `UpdateAllies()` to populate the initial ally set. It sets `m_bMeleeAttack` to `false` specifically for Warlock Imps (entry 416), as they lack melee attacks. It also handles legacy client-specific script text for certain hatchlings.

### Core Combat Loop

*   **`UpdateAI`**: The main tick function called every update interval.
    1.  Checks if the creature is alive and has valid charm info.
    2.  Updates the ally list periodically via `UpdateAllies()`.
    3.  Handles taunts: If taunted, it forces an attack on the taunt target.
    4.  For NPC pets, it checks the threat manager for a higher-threat target than the current victim and switches if necessary.
    5.  If in combat (`GetVictim()` exists):
        *   Calls `_needToStop()` to check if combat should end (e.g., owner mounted, owner evaded, invalid target). If so, calls `_stopAttack()`.
        *   Performs melee attacks if enabled (`m_bMeleeAttack`).
    6.  If not in combat and not player-controlled (possessed), it calls `HandleReturnMovement()` to ensure the pet moves to its designated stay or follow position.
    7.  **Autocasting Logic**: If not currently casting a non-melee spell, it iterates through the pet's auto-spell slots. It filters spells by autocastability, GCD, and cooldown.
        *   For positive spells (buffs/heals), it looks for valid targets among enemies (attacker of pet/owner) or allies (`m_AllySet`).
        *   For negative spells (damage/debuffs), it targets the current victim if valid.
        *   It randomly selects one valid spell/target pair from the candidates, prepares the spell, and triggers visual/audio feedback (`SendPetTalk` or `SendPetAIReaction`).
    8.  Updates movement speeds to prevent despawning due to lag.

### Targeting and Engagement

*   **`MoveInLineOfSight`**: Triggered when a unit enters the pet's line of sight. It checks if the pet is already attacking, returning, or out of Z-range. It respects civilian protection rules (patch 1.8.0+) and PvP-only flags. If the unit is hostile, within attack distance, and visible, it initiates `AttackStart`.
*   **`AttackStart`**: Overrides the base `CreatureAI` method. It validates the target using `CanAttack()`. If valid, it calls `DoAttack()`, determining whether to chase based on the current command state (Stay vs. Follow/Attack).
*   **`OwnerAttackedBy`**: Called when the owner takes damage. It prevents passive pets from reacting, checks if the pet is already engaged, and validates the attacker. If valid, it starts an attack on the attacker.
*   **`OwnerAttacked`**: Called when the owner attacks a target. It generally prevents defensive pets from engaging solely based on owner aggression (unless the pet is already in combat or the owner is an NPC), adhering to classic WoW mechanics where defensive pets wait to be hit.
*   **`AttackedBy`**: Called when the pet takes damage. Similar to `OwnerAttackedBy`, it validates the attacker and initiates combat if the pet is not passive and not already engaged.
*   **`CanAttack`**: A critical validation function. It checks:
    *   Target validity and liveness.
    *   Pet enabled status.
    *   React state (Passive pets only attack if commanded).
    *   PvP flags (non-PvP pets won't attack PvP targets unless commanded).
    *   Crowd control auras (pets avoid breaking CC unless commanded).
    *   Command state (Stay pets only attack if in range or commanded; Returning pets ignore attacks unless commanded to follow).
    *   Current victim constraints (pets don't switch targets unless the owner explicitly commands it or the current target dies).

### Movement and State Transitions

*   **`HandleReturnMovement`**: Manages the transition back to a "Stay" position or "Follow" state.
    *   If `COMMAND_STAY` is active and the pet is not at stay/returning, it clears motion, sets `IsReturning`, and moves to the stored stay coordinates.
    *   If `COMMAND_FOLLOW` is active and the pet is not following/returning, it clears motion, sets `IsReturning`, and initiates a follow movement towards the owner.
*   **`MovementInform`**: Callback from the motion master when a movement finishes.
    *   `POINT_MOTION_TYPE`: Indicates the pet reached its stay position. It sets `IsAtStay` and goes idle.
    *   `FOLLOW_MOTION_TYPE`: Indicates the pet reached the follow distance. It sets `IsFollowing`.
*   **`DoAttack`**: Executes the attack action.
    *   Calls `Attack()` on the target.
    *   Plays sounds/text for aggressive pets or specific entries (Imps).
    *   If `chase` is true, it clears motion and starts chasing.
    *   If `chase` is false (Stay), it sets `IsAtStay` and goes idle.
    *   For creature-owned pets, it manually adds threat and enters combat with the target and owner.

### Cleanup and Stopping

*   **`_needToStop`**: Determines if the pet should cease combat. Conditions include:
    *   Charmed creature attacking its charmer.
    *   Pet disabled (e.g., owner mounted).
    *   Owner (if creature) is evading or dead and pet is out of threat area.
    *   Current victim is no longer a valid attack target.
*   **`_stopAttack`**: Halts combat. Stops attacks, interrupts spells, clears hostile references, and triggers `HandleReturnMovement` to return the pet to its owner/stay position.
*   **`KilledUnit`**: Called when the pet's victim dies. It stops the current attack, interrupts spells, and then calls `SelectNextTarget()` to find a new target. If no target is found, it returns to the owner.

### Helper Functions

*   **`SelectNextTarget`**: Finds the next valid target after the current one dies. Priority:
    1.  Threat list (for NPC pets).
    2.  Attackers of the pet.
    3.  Victim of the owner.
    4.  Attackers of the owner.
    It returns a pair of the target and a reason code.
*   **`UpdateAllies`**: Refreshes `m_AllySet` every 10 seconds. It includes the pet, the owner, and if the owner is in a group/raid, the members of the owner's subgroup. This set is used for targeting buffs.
*   **`ClearCharmInfoFlags`**: Resets all movement/command flags (`IsAtStay`, `IsCommandAttack`, etc.) to false. Used before setting new states to ensure clean transitions.

## Cross-Unit Boundaries

*   **`Creature` / `Pet`**: `PetAI` relies heavily on `Creature` methods for state queries (`IsPet`, `IsInEvadeMode`, `GetAttackDistance`) and `Pet` methods for specific pet logic (`IsEnabled`, `GetPetType`, `CheckLearning`).
*   **`Unit`**: Used for general entity interactions: getting victims, attackers, charm info, threat managers, and validating targets (`IsValidAttackTarget`).
*   **`CharmInfo`**: Accessed via `Unit::GetCharmInfo()` to read/write command states (`HasCommandState`, `IsCommandAttack`, `SetIsReturning`). This is the central hub for pet command logic.
*   **`MotionMaster`**: Accessed via `Unit::GetMotionMaster()` to control movement (`MoveChase`, `MoveFollow`, `MovePoint`, `MoveIdle`, `Clear`).
*   **`ThreatManager`**: Accessed via `Unit::GetThreatManager()` for NPC pets to determine the highest threat target (`getHostileTarget`).
*   **`Spell` / `SpellMgr`**: `UpdateAI` uses `SpellMgr` to fetch spell entries and creates `Spell` objects to prepare and cast autocasts.
*   **`Group`**: `UpdateAllies` interacts with `Group` to retrieve members and subgroups for ally tracking.
*   **`ScriptMgr`**: Used in `DoAttack` to play specific text lines for certain pet entries (Imps).

## Data Model

This unit does not interact directly with database tables. All state is maintained in memory via object fields (`m_AllySet`, `m_updateAlliesTimer`) and linked objects (`CharmInfo`, `Unit`, `Creature`).

## Notable Implementation Details

1.  **Imp Melee Disable**: The constructor explicitly disables melee attacks for Warlock Imps (entry 416) by setting `m_bMeleeAttack = false`. This is a hardcoded exception for a specific creature entry.
2.  **Legacy Client Support**: Comments and preprocessor directives (`#if SUPPORTED_CLIENT_BUILD <= CLIENT_BUILD_1_6_1`) indicate support for older WoW client versions, specifically regarding hatchling notifications and civilian attack rules.
3.  **Autocast Randomization**: In `UpdateAI`, if multiple spells are valid for autocasting, one is chosen randomly (`urand`). This prevents deterministic behavior where a pet might always prioritize the same spell.
4.  **Ally Set Optimization**: `UpdateAllies` only refreshes every 10 seconds (`m_updateAlliesTimer`) to reduce performance overhead. It also skips updates if the group composition hasn't changed significantly (same size, same subgroup count).
5.  **Defensive Pet Logic**: `OwnerAttacked` explicitly prevents defensive pets from engaging when the owner attacks, unless the pet is already in combat or the owner is an NPC. This mimics classic WoW behavior where defensive pets wait to be hit.
6.  **Threat List Switching**: For NPC pets, `UpdateAI` checks the threat manager for a higher-threat target than the current victim. If found and valid, it switches targets immediately. This is distinct from player pets, which typically follow owner commands or threat less aggressively.
7.  **Return Movement Flags**: The `IsReturning` flag is crucial for preventing erratic behavior during transitions. It is set in `HandleReturnMovement` and cleared in `MovementInform` once the destination is reached.
8.  **Possession Handling**: `UpdateAI` and `HandleReturnMovement` check for `UNIT_FLAG_POSSESSED` (e.g., Eyes of the Beast). If possessed, movement and some AI updates are skipped or altered, as the player controls the pet directly.

## Member Reference

**Permissible**: Static method that returns `PERMIT_BASE_SPECIAL` if the creature is a pet, else `PERMIT_BASE_NO`.

**PetAI**: Constructor that initializes the AI, updates allies, disables melee for Imps (entry 416), and handles legacy client scripts.

**EnterEvadeMode**: Empty override; does nothing.

**_needToStop**: Checks if the pet should stop attacking based on owner state (mounted, evaded, dead), charm state, and target validity.

**_stopAttack**: Stops combat, interrupts spells, clears hostile refs, and triggers return movement.

**MoveInLineOfSight**: Triggers `AttackStart` if a hostile, valid unit enters LOS and range, respecting civilian/PVP rules.

**UpdateAI**: Main update loop handling ally updates, taunts, threat-based target switching, combat checks, melee attacks, return movement, and spell autocasting.

**UpdateAllies**: Refreshes the `m_AllySet` with the pet, owner, and subgroup members every 10 seconds.

**KilledUnit**: Stops current attack, interrupts spells, and selects a new target via `SelectNextTarget`; returns to owner if none found.

**AttackStart**: Validates target via `CanAttack` and initiates attack/chase via `DoAttack`.

**OwnerAttackedBy**: Initiates attack on the attacker if the pet is not passive, not engaged, and the attacker is valid.

**OwnerAttacked**: Initiates attack on the owner's target if the pet is already in combat or the owner is an NPC, respecting passive/react states.

**SelectNextTarget**: Finds the next valid target from threat list, pet attackers, owner victim, or owner attackers.

**HandleReturnMovement**: Moves the pet to its stay position or follows the owner based on command state.

**DoAttack**: Executes the attack, plays sounds, sets chase/idle motion, and manages combat entry for creature-owned pets.

**MovementInform**: Handles completion of stay/follow movements, updating flags (`IsAtStay`, `IsFollowing`).

**CanAttack**: Validates if a target can be attacked based on react state, command state, PvP flags, CC auras, and current victim constraints.

**ClearCharmInfoFlags**: Resets all charm-related movement/command flags to false.

**AttackedBy**: Initiates attack on the attacker if the pet is not passive, not engaged, and the attacker is valid.

---

<!-- machine-true, projected from graph.json -->

## Map — PetAI

*Source:* PetAI.cpp, PetAI.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| Permissible | method | Creature.Main/IsPet | — | — |
| PetAI | ctor | CreatureAI/CreatureAI, Object/GetEntry | — | — |
| EnterEvadeMode | method | — | — | — |
| _needToStop | method | Creature.Main/IsInEvadeMode, Creature.Main/IsOutOfThreatArea, Creature.Main/IsPet, Object/GetObjectGuid, Object/ToCreature, ObjectGuid/operator==, Pet.Main/IsEnabled, Unit.Main/GetCharmerGuid, Unit.Main/GetCharmerOrOwnerOrSelf, Unit.Main/GetVictim, Unit.Main/IsAlive, Unit.Main/IsCharmed, Unit.Main/IsInCombat, WorldObject.Object/IsValidAttackTarget | — | — |
| _stopAttack | method | Creature.MotionMaster/MoveIdle, HostileRefManager/deleteReferences, MotionMaster/Clear, SpellCaster/InterruptNonMeleeSpells, Unit.Main/AttackStop, Unit.Main/CombatStop, Unit.Main/GetCharmInfo, Unit.Main/GetHostileRefManager, Unit.Main/GetMotionMaster, Unit.Main/IsAlive, Unit.Main/SetIsCommandAttack | — | — |
| MoveInLineOfSight | method | Creature.Main/CanInitiateAttack, Creature.Main/GetAttackDistance, Creature.Main/HasStaticFlag, Creature.Main/IsCivilian, Object/IsCreature, Unit.Main/GetCharmInfo, Unit.Main/GetVictim, Unit.Main/IsCharmerOrOwnerPlayerOrPlayerItself, Unit.Main/IsHostileTo, Unit.Main/IsPvP, Unit.Main/IsReturning, Unit.Main/IsTargetableBy, WorldObject.Object/GetDistanceZ, WorldObject.Object/IsWithinDistInMap, WorldObject.Object/IsWithinLOSInMap | — | — |
| UpdateAI | method | Creature.Main/GetPetAutoSpellOnPos, Creature.Main/GetPetAutoSpellSize, Creature.Main/IsPet, CreatureAI/DoMeleeAttackIfReady, Map.Main/GetUnit, Object/GetTypeId, Object/HasFlag, ObjectGuid/IsCreature, ObjectGuid/ObjectGuid#5, Pet.Main/CheckLearning, Pet.Main/GetPetType, shared_Util/urand, Spell.Main/CanAutoCast, Spell.Main/Delete, Spell.Main/prepare, Spell.Main/Spell#2, SpellCaster/HasGCD, SpellCaster/IsNonMeleeSpellCasted, SpellCaster/IsSpellReady#2, SpellCastTargetsInfo/setUnitTarget, SpellCastTargetsInfo/SpellCastTargets, SpellEntry/GetDuration, SpellEntry/GetRecoveryTime, SpellEntry/HasAura, SpellEntry/HasEffect, SpellEntry/IsAutocastable#2, SpellEntry/IsNonCombatSpell, SpellEntry/IsPositiveSpell#4, SpellMgr/GetSpellEntry, SpellMgr/Instance, ThreatManager/getHostileTarget, ThreatManager/isThreatListEmpty, Unit.Main/GetAttackerForHelper, Unit.Main/GetCharmerOrOwner, Unit.Main/GetCharmerOrOwnerGuid, Unit.Main/GetCharmInfo, Unit.Main/GetTauntTarget, Unit.Main/GetThreatManager, Unit.Main/GetVictim, Unit.Main/HasAuraPetShouldAvoidBreaking, Unit.Main/HasUnitState, Unit.Main/IsAlive, Unit.Main/SendPetAIReaction, Unit.Main/SendPetTalk, Unit.Main/SetInFront, Unit.Main/UpdateSpeed, WorldObject.Object/GetMap, WorldObject.Object/HasInArc, WorldObject.Object/SendCreateUpdateToPlayer | — | — |
| UpdateAllies | method | game_Group_Group/SameSubGroup, Group/GetFirstMember, Group/GetMembersCount, Group/isRaidGroup, GroupReference/next, Object/GetObjectGuid, Object/GetTypeId, ObjectGuid/operator==, Player.Main/GetGroup, Unit.Main/GetCharmerOrOwner | — | — |
| KilledUnit | method | SpellCaster/InterruptNonMeleeSpells, Unit.Main/AttackStop, Unit.Main/CombatStop, Unit.Main/GetVictim, Unit.Main/IsInCombat, Unit.Main/SendMeleeAttackStop | — | — |
| AttackStart | method | CharmInfo/HasCommandState, Unit.Main/GetCharmInfo, Unit.Main/IsCommandAttack | — | — |
| OwnerAttackedBy | method | Creature.Main/IsPet, Pet.Main/IsEnabled, Unit.Main/GetVictim, Unit.Main/HasReactState, Unit.Main/HasUnitState, Unit.Main/IsAlive, WorldObject.Object/IsValidAttackTarget | — | — |
| OwnerAttacked | method | Creature.Main/IsPet, ObjectGuid/IsPlayer, Pet.Main/IsEnabled, Unit.Main/GetCharmerOrOwnerGuid, Unit.Main/GetVictim, Unit.Main/HasReactState, Unit.Main/HasUnitState, Unit.Main/IsAlive, Unit.Main/IsInCombat, WorldObject.Object/IsValidAttackTarget | — | — |
| SelectNextTarget | method | Creature.Main/IsInEvadeMode, Creature.Main/IsPet, Object/ToCreature, Pet.Main/IsEnabled, ThreatManager/getHostileTarget, ThreatManager/isThreatListEmpty, Unit.Main/CanReachWithMeleeAutoAttack, Unit.Main/GetAttackers, Unit.Main/GetCharmerOrOwner, Unit.Main/GetCharmInfo, Unit.Main/GetThreatManager, Unit.Main/GetVictim, Unit.Main/HasAuraPetShouldAvoidBreaking, Unit.Main/HasReactState, Unit.Main/IsAtStay, Unit.Main/IsInCombat, WorldObject.Object/IsValidAttackTarget | — | — |
| HandleReturnMovement | method | CharmInfo/HasCommandState, Creature.Main/IsPet, Creature.MotionMaster/MoveFollow, Creature.MotionMaster/MovePoint, MotionMaster/Clear, Object/GetGUIDLow, Object/HasFlag, Pet.Main/GetFollowAngle, Unit.Main/GetCharmerOrOwner, Unit.Main/GetCharmInfo, Unit.Main/GetMotionMaster, Unit.Main/GetStayPosition, Unit.Main/IsAtStay, Unit.Main/IsFollowing, Unit.Main/IsReturning, Unit.Main/SetIsReturning | — | — |
| DoAttack | method | Creature.Main/EnterCombatWithTarget, Creature.Main/ToCreature, Creature.MotionMaster/MoveChase, Creature.MotionMaster/MoveIdle, MotionMaster/Clear, Object/GetEntry, ObjectGuid/IsCreature, ScriptMgr/DoScriptText, shared_Util/roll_chance_u, Unit.Main/AddThreat, Unit.Main/Attack, Unit.Main/GetCharmerOrOwner, Unit.Main/GetCharmerOrOwnerGuid, Unit.Main/GetCharmInfo, Unit.Main/GetMotionMaster, Unit.Main/HasReactState, Unit.Main/HasUnitState, Unit.Main/IsAlive, Unit.Main/IsCommandAttack, Unit.Main/IsInCombat, Unit.Main/SendPetAIReaction, Unit.Main/SetCasterChaseDistance, Unit.Main/SetInCombatWith, Unit.Main/SetIsAtStay, Unit.Main/SetIsCommandAttack, WorldObject.Object/IsValidAttackTarget | — | — |
| MovementInform | method | Creature.MotionMaster/MoveIdle, MotionMaster/Clear, Object/GetGUIDLow, ObjectGuid/GetCounter, Unit.Main/GetCharmerOrOwnerGuid, Unit.Main/GetCharmInfo, Unit.Main/GetMotionMaster, Unit.Main/IsReturning, Unit.Main/SetIsAtStay, Unit.Main/SetIsFollowing | — | — |
| CanAttack | method | CharmInfo/HasCommandState, Creature.Main/IsPet, Object/GetGUID, Object/ToPlayer, ObjectGuid/IsPlayer, Pet.Main/IsEnabled, Player.Main/GetSelectedUnit, SpellCaster/InterruptNonMeleeSpells, Unit.Main/AttackStop, Unit.Main/CanReachWithMeleeAutoAttack, Unit.Main/GetCharmerOrOwner, Unit.Main/GetCharmerOrOwnerGuid, Unit.Main/GetCharmInfo, Unit.Main/GetVictim, Unit.Main/HasAuraPetShouldAvoidBreaking, Unit.Main/HasReactState, Unit.Main/IsAlive, Unit.Main/IsCommandAttack, Unit.Main/IsCommandFollow, Unit.Main/IsPvP, Unit.Main/IsReturning, Unit.Main/SendMeleeAttackStop, WorldObject.Object/IsValidAttackTarget | — | — |
| ClearCharmInfoFlags | method | Unit.Main/GetCharmInfo, Unit.Main/SetIsAtStay, Unit.Main/SetIsCommandAttack, Unit.Main/SetIsCommandFollow, Unit.Main/SetIsFollowing, Unit.Main/SetIsReturning | — | — |
| AttackedBy | method | Creature.Main/IsPet, Pet.Main/IsEnabled, Unit.Main/GetVictim, Unit.Main/HasReactState, Unit.Main/IsAlive, WorldObject.Object/IsValidAttackTarget | — | — |
