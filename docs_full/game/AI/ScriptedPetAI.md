<!-- provenance: verbose -->
# ScriptedPetAI

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ScriptedPetAI

**Purpose & Responsibilities**

`ScriptedPetAI` is a base artificial intelligence class for controlled creatures (guardians, protectors, mini-pets) in the WoW server emulation. It provides a framework for pet behavior, handling transitions between combat and non-combat states, target selection, movement (chasing vs. following), and basic melee/spell execution. The class is marked as "under development" and is intended for scenarios where the developer has limited control over combat entry/exit timing. It inherits from `CreatureAI` and overrides core lifecycle methods to implement pet-specific logic, such as respecting owner command states (`COMMAND_FOLLOW`), avoiding breaking crowd-control effects, and delegating specific actions to derived classes via virtual hooks. It does not interact with any database tables.

## Member-by-Member Behavior

### Lifecycle and Initialization

*   **`ScriptedPetAI`**: Initializes the base `CreatureAI` with the associated `Creature` pointer. Instantiated by various NPC scripts (e.g., `npc_dream_fogAI`, `npc_shade_of_taerarAI`, `npc_arcanite_dragonlingAI`) to provide pet-like behavior.
*   **`~ScriptedPetAI`**: Trivial destructor with no custom cleanup.
*   **`JustRespawned`**: Invokes virtual `Reset()` and `ResetCreature()` methods, allowing derived classes to clean up state upon respawn.

### Combat Entry and Target Selection

*   **`MoveInLineOfSight`**: Determines if the pet should aggro a unit (`pWho`) entering its line of sight. It enforces strict conditions: no existing victim, aggressive react state, valid/visible/accessibile target, ability to initiate attack, and (for non-flyers) vertical distance within `CREATURE_Z_ATTACK_RANGE`. If all pass, it calls `AttackStart(pWho)`.
*   **`AttackedBy`**: Handles being attacked by `pAttacker`. If no victim exists and the pet is not passive (`!REACT_PASSIVE`) and can reach the attacker with melee, it initiates combat via `AttackStart(pAttacker)`.
*   **`AttackStart`**: Initiates combat against `pWho`. Attempts `Unit::Attack`; if successful, commands the motion master to chase the target. Called by `MoveInLineOfSight`, `AttackedBy`, and externally by `boss_dragon_of_nightmare::ChangeTarget`.

### Combat Loop and Updates

*   **`UpdateAI`**: The main tick function.
    1.  **Dead Check**: Returns if the creature is not alive.
    2.  **In Combat**: Checks if the current target is valid (`IsTargetableBy`). If not, calls `ResetPetCombat()`. If the target has an aura pets should avoid breaking (`HasAuraPetShouldAvoidBreaking`) and the pet is not aggressive, it interrupts non-melee spells and stops attacking. Otherwise, delegates to `UpdatePetAI(uiDiff)`.
    3.  **Not In Combat**: Retrieves the owner. If the owner is in combat and the pet is not passive, it attempts to assist. It checks the owner's primary helper target; if that target is CC'd, it iterates through other attackers to find a valid target. If none are found, it ensures the pet follows the owner. If the owner is not in combat and the charm info indicates `COMMAND_FOLLOW`, it ensures the pet follows the owner and delegates to `UpdatePetOOCAI(uiDiff)`.
*   **`UpdatePetAI`**: Virtual method called during combat. Updates the pet's spell list if registered and performs a melee attack if ready. Derived classes override this to inject specific combat logic.
*   **`UpdatePetOOCAI`**: Virtual method called when not in combat. Base implementation is empty; derived classes override for out-of-combat behaviors.

### State Reset and Cleanup

*   **`ResetPetCombat`**: Stops combat and resets state. Gets the owner; if `COMMAND_FOLLOW` is set, sets motion to follow the owner. Otherwise, clears motion and sets idle. Stops attack, logs a debug message, and calls virtual `Reset()`.
*   **`Reset`**: Virtual hook for resetting internal state, empty in base.
*   **`ResetCreature`**: Virtual hook for resetting creature properties, empty in base.
*   **`KilledUnit`**: Empty override for when the pet kills a unit.
*   **`OwnerKilledUnit`**: Empty override for when the owner kills a unit.

## Cross-Unit Boundaries

*   **Calls Out**:
    *   **`Creature.Main` / `Unit.Main` / `WorldObject.Object`**: Queries state (health, visibility, distance, react state, combat status) and performs actions (attacking, moving).
    *   **`Creature.MotionMaster`**: Controls movement (chase, follow, idle, clear).
    *   **`CharmInfo`**: Checks command states (`COMMAND_FOLLOW`).
    *   **`SpellCaster`**: Interrupts spells via `InterruptNonMeleeSpells`.
    *   **`Log.Main`**: Outputs debug logs during combat reset.
    *   **`CreatureAI`**: Inherits from and calls `DoMeleeAttackIfReady` and `UpdateSpellsList`.
*   **Called By**:
    *   **NPC Scripts**: Various boss and special NPC scripts instantiate `ScriptedPetAI` (e.g., `boss_dragon_of_nightmare`, `npc_dream_fogAI`, `boss_taerar`, `npc_shade_of_taerarAI`, `npc_special` scripts).
    *   **`boss_dragon_of_nightmare::ChangeTarget`**: Directly calls `AttackStart` to force target switching.
    *   **Derived Classes**: Many `npc_special` scripts override `UpdateAI`, `UpdatePetAI`, and `UpdatePetOOCAI`.

## Data Model

This unit does not access any database tables. All logic is driven by in-memory object states and configuration constants.

## Notable Implementation Details

1.  **Crowd Control Preservation**: `UpdateAI` checks `HasAuraPetShouldAvoidBreaking()` on targets. If the primary target is CC'd, the pet interrupts spells and stops attacking. If the owner is in combat and the primary helper target is CC'd, the pet iterates through other attackers to find a valid target. This prevents pets from breaking important CC effects.
2.  **Vertical Attack Range**: `MoveInLineOfSight` checks `GetDistanceZ` against `CREATURE_Z_ATTACK_RANGE` for non-flying creatures, preventing aggro on vertically unreachable targets.
3.  **Owner Assistance Logic**: When the owner is in combat, the pet targets the owner's `GetAttackerForHelper()`. If invalid due to CC, it falls back to iterating `GetAttackers()`.
4.  **Virtual Hooks**: Relies on `Reset`, `ResetCreature`, `UpdatePetAI`, and `UpdatePetOOCAI` for customization by derived classes.
5.  **Debug Logging**: `ResetPetCombat` logs via `sLog.Out`.
6.  **Incomplete Ally Updates**: A comment notes that `UpdateAllies()` is handled in generic `PetAI` in MaNGOS, but this script-based AI cannot easily replicate it, potentially causing side effects.

## Member Reference

*   **`ScriptedPetAI`**: Constructor initializing the base `CreatureAI`.
*   **`~ScriptedPetAI`**: Trivial destructor.
*   **`MoveInLineOfSight`**: Checks LOS, validity, and range to decide whether to aggro a unit.
*   **`KilledUnit`**: Empty override for when the pet kills a unit.
*   **`OwnerKilledUnit`**: Empty override for when the owner kills a unit.
*   **`Reset`**: Virtual hook for resetting internal state, empty in base.
*   **`ResetCreature`**: Virtual hook for resetting creature properties, empty in base.
*   **`UpdatePetOOCAI`**: Virtual hook for out-of-combat AI updates, empty in base.
*   **`AttackStart`**: Initiates attack and chase movement against a target.
*   **`AttackedBy`**: Reacts to being attacked by initiating combat if not passive.
*   **`ResetPetCombat`**: Stops combat, sets follow/idle motion, and calls `Reset`.
*   **`UpdatePetAI`**: Virtual hook for in-combat AI updates, handles spells and melee.
*   **`JustRespawned`**: Calls `Reset` and `ResetCreature` on respawn.
*   **`UpdateAI`**: Main tick function managing combat/non-combat state, target selection, and CC preservation.

---

<!-- machine-true, projected from graph.json -->

## Map — ScriptedPetAI

*Source:* ScriptedPetAI.cpp, ScriptedPetAI.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ScriptedPetAI | ctor | CreatureAI/CreatureAI | boss_dragon_of_nightmare/npc_dream_fogAI, boss_taerar/npc_shade_of_taerarAI, npcs_special/npc_arcanite_dragonlingAI, npcs_special/npc_cannonball_runnerAI, npcs_special/npc_emerald_dragon_whelpAI, npcs_special/npc_explosive_sheepAI, npcs_special/npc_felhound_minionAI, npcs_special/npc_gnomish_battle_chickenAI, npcs_special/npc_goblin_bomb_dispenserAI, npcs_special/npc_oozeling_jubjubAI, npcs_special/npc_shahramAI | — |
| ~ScriptedPetAI | dtor | — | — | — |
| MoveInLineOfSight | method | Creature.Main/CanFly, Creature.Main/CanInitiateAttack, Creature.Main/GetAttackDistance, Unit.Main/CanAttack, Unit.Main/GetVictim, Unit.Main/HasReactState, Unit.Main/IsInAccessablePlaceFor, Unit.Main/IsVisibleForOrDetect, WorldObject.Object/GetDistanceZ, WorldObject.Object/IsValidAttackTarget, WorldObject.Object/IsWithinDistInMap, WorldObject.Object/IsWithinLOSInMap | — | — |
| KilledUnit | method | — | — | — |
| OwnerKilledUnit | method | — | — | — |
| Reset | method | — | — | — |
| ResetCreature | method | — | — | — |
| UpdatePetOOCAI | method | — | — | — |
| AttackStart | method | Creature.MotionMaster/MoveChase, Unit.Main/Attack, Unit.Main/GetMotionMaster | boss_dragon_of_nightmare/ChangeTarget | — |
| AttackedBy | method | Unit.Main/CanReachWithMeleeAutoAttack, Unit.Main/GetVictim, Unit.Main/HasReactState | — | — |
| ResetPetCombat | method | CharmInfo/HasCommandState, Creature.MotionMaster/MoveFollow, Creature.MotionMaster/MoveIdle, Log.Main/Out, MotionMaster/Clear, Unit.Main/AttackStop, Unit.Main/GetCharmerOrOwner, Unit.Main/GetCharmInfo, Unit.Main/GetMotionMaster | — | — |
| UpdatePetAI | method | CreatureAI/DoMeleeAttackIfReady, CreatureAI/UpdateSpellsList | boss_taerar/UpdatePetAI, npcs_special/UpdatePetAI, npcs_special/UpdatePetAI#2, npcs_special/UpdatePetAI#3, npcs_special/UpdatePetAI#4 | — |
| JustRespawned | method | — | — | — |
| UpdateAI | method | CharmInfo/HasCommandState, Creature.MotionMaster/MoveFollow, SpellCaster/InterruptNonMeleeSpells, Unit.Main/AttackStop, Unit.Main/GetAttackerForHelper, Unit.Main/GetAttackers, Unit.Main/GetCharmerOrOwner, Unit.Main/GetCharmInfo, Unit.Main/GetMotionMaster, Unit.Main/GetReactState, Unit.Main/GetVictim, Unit.Main/HasAuraPetShouldAvoidBreaking, Unit.Main/HasReactState, Unit.Main/HasUnitState, Unit.Main/IsAlive, Unit.Main/IsInCombat, Unit.Main/IsTargetableBy, WorldObject.Object/IsInMap | npcs_special/UpdateAI#12, npcs_special/UpdateAI#4, npcs_special/UpdateAI#5, npcs_special/UpdateAI#6, npcs_special/UpdateAI#9 | — |
