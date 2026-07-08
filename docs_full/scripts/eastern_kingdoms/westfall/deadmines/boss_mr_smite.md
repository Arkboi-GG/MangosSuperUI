# boss_mr_smite

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`boss_mr_smite.cpp` implements the artificial intelligence for **Mr. Smite**, a boss creature located in the **Deadmines** dungeon. The unit defines `boss_mr_smiteAI`, a class derived from `ScriptedAI`, which manages the boss’s combat behavior, phase transitions, and equipment changes.

The core responsibility of this AI is to orchestrate a three-phase fight where Mr. Smite changes weapons and abilities based on his remaining health:
1.  **Phase 1 (100%–66% HP):** Standard melee combat with a specific reflex aura (`SPELL_NIBLE_REFLEXES`).
2.  **Transition 1:** At 66% HP, Mr. Smite stops fighting, moves to a nearby chest (`GO_SMITE_CHEST`), kneels, and equips dual axes.
3.  **Phase 2 (66%–33% HP):** Combat resumes with dual axes and a periodic `THRASH` spell.
4.  **Transition 2:** At 33% HP, he again moves to the chest, kneels, and equips a large hammer.
5.  **Phase 3 (<33% HP):** Combat resumes with the hammer and a periodic `SMITE_SLAM` spell.

The AI handles complex state management, including movement splines to the equipment chest, temporary evasion from combat during transitions, and timer-based spell casting.

## Member-by-Member Behavior

### Initialization and State Management

*   **`boss_mr_smiteAI`**: The constructor initializes the AI instance. It immediately calls `Reset()` to ensure all internal timers and phase states are set to their default starting values (Phase 1, initial equipment loaded).
*   **`Reset`**: Resets the boss’s state upon despawn, evade, or initial spawn. It sets `equiping` and `inSpline` flags to `false`, resets the phase to `PHASE_1`, and clears timers. Crucially, it reloads the creature’s default equipment using `Creature.Main/LoadEquipment` based on the creature info’s `equipment_id`.

### Combat Entry and Movement

*   **`AttackedBy`**: Triggered when the boss is attacked. It acts as a gatekeeper: if the boss already has a victim, or if the current phase is beyond `PHASE_3` (indicating a transition or end-state), it ignores the attack. Otherwise, it initiates combat via `AttackStart`.
*   **`AttackStart`**: Initiates active combat. It checks if the phase allows combat (must be ≤ `PHASE_3`). If valid, it establishes the threat relationship, sets both units as in-combat, and commands the motion master to chase the target. It also ensures the `equiping` flag is `false`.
*   **`MovementInform`**: Handles the completion of movement points.
    *   If `inSpline` is true (moving to the chest), it calls `SplineFinished()` and clears the flag.
    *   If not equipping, it ensures the boss returns to chasing its victim if it was moved to a point for tactical reasons.
    *   If equipping, it prepares the boss for the equipment animation by sheathing weapons and kneeling, then sets a 3-second timer for the next phase step.
*   **`SplineFinished`**: Called when the movement spline to the chest completes. If the boss is in the equipping sequence, it clears the current equipment (setting it to 0), sheathes weapons, and kneels. It then advances the phase to `PHASE_EQUIP_PROCESS` with a 3-second delay.

### Equipment Transition Logic

*   **`PhaseEquipStart`**: Initiates the movement to the equipment chest. It finds the nearest game object with ID `GO_SMITE_CHEST` within 150 units. If found, it calculates a contact point, clears the current motion master, faces the chest, sets the `inSpline` flag, and starts moving to that point. If the chest is not found, it skips directly to processing.
*   **`PhaseEquipProcess`**: Handles the actual equipment swap.
    *   If health is below 33%, it equips the Hammer (`EQUIP_ID_HAMMER`) in the main hand and casts `SPELL_SMITE_HAMMER`.
    *   Otherwise (health between 33% and 66%), it equips dual Axes (`EQUIP_ID_AXE`).
    *   It makes the boss stand up and advances the phase to `PHASE_EQUIP_END` with a 1-second delay.
*   **`PhaseEquipEnd`**: Concludes the transition. It selects the top-agro target from the threat list. If no target exists, it evades. Otherwise, it unsheathes weapons, determines the new combat phase (`PHASE_3` if <33% HP, else `PHASE_2`), clears the `equiping` flag, and restarts combat via `AttackStart`.

### Main Combat Loop

*   **`UpdateAI`**: The primary tick function.
    *   **Transition Handling**: If the boss has no hostile target (during transitions), it processes the `m_uiEquipTimer`. Depending on the current phase (`PHASE_EQUIP_START`, `PHASE_EQUIP_PROCESS`, `PHASE_EQUIP_END`), it calls the corresponding helper method.
    *   **Phase 1**: Checks if health drops below 66%. If so, it casts `SPELL_SMITE_STOMP`, plays a voice line, initiates the equipment transition sequence, clears the victim, stops attacking, and removes the `SPELL_NIBLE_REFLEXES` aura.
    *   **Phase 2**: Manages the `THRASH` spell timer (1.5–4 seconds). If health drops below 33%, it triggers the second transition to the hammer phase similarly to the first transition.
    *   **Phase 3**: Manages the `SMITE_SLAM` spell timer (11 seconds).
    *   **Positioning**: If the boss is not equipping and cannot reach the target with melee attacks but is within double melee range, it moves to a random attack point to close the gap.
    *   **Melee**: Finally, it attempts a melee auto-attack if ready.

### Registration

*   **`GetAI_boss_mr_smite`**: Factory function that creates and returns a new `boss_mr_smiteAI` instance for a given creature.
*   **`AddSC_boss_mr_smite`**: Registers the script with the server’s script manager. It creates a `Script` object, assigns the name "boss_mr_smite", links the `GetAI` factory, and registers it.

## Cross-Unit Boundaries

*   **`ScriptedAI`**: The base class for `boss_mr_smiteAI`. Provides the framework for AI ticks (`UpdateAI`), combat events (`AttackedBy`, `AttackStart`), and utility functions like `DoCastSpellIfCan` and `EnterEvadeMode`.
*   **`Creature.Main`**: Used for loading default equipment (`LoadEquipment`), setting virtual items for weapon swaps (`SetVirtualItem`), and retrieving creature info (`GetCreatureInfo`).
*   **`Creature.MotionMaster`**: Controls the boss’s movement. `MoveChase` is used for standard combat pursuit. `MovePoint` is used for tactical positioning and moving to the equipment chest. `Clear` is used to stop movement during transitions.
*   **`Unit.Main`**: Provides access to the victim (`GetVictim`), health percentage (`GetHealthPercent`), threat management (`AddThreat`, `SetInCombatWith`), combat state (`Attack`, `AttackStop`), and physical state (`SetSheath`, `SetStandState`). `SelectHostileTarget` and `SelectAttackingTarget` are used to manage aggro lists during transitions.
*   **`WorldObject.Object`**: Used for spatial queries. `FindNearestGameObject` locates the equipment chest. `GetContactPoint` calculates the precise coordinates for the boss to stand next to the chest. `IsWithinDistInMap` checks proximity for positioning logic.
*   **`ScriptMgr`**: `DoScriptText` is used to trigger voice lines during phase transitions.
*   **`shared_Util`**: `urand` is used to generate random intervals for spell timers.

## Data Model

This unit does not interact with any database tables directly. All configuration (spell IDs, item IDs, game object IDs, text entries) is hardcoded in the `enum` block at the top of the file.

## Notable Implementation Details

*   **Hardcoded Transition Logic**: The phase transitions are strictly tied to health percentages (66% and 33%). The AI does not use a generic phase system but manually manages state variables (`m_uiPhase`) and timers (`m_uiEquipTimer`).
*   **Equipment Swapping**: The AI uses `SetVirtualItem` to change weapons mid-fight. This is a client-side visual change that also affects combat stats. The default equipment is reloaded on reset.
*   **Movement to Chest**: The transition involves finding a specific Game Object (`GO_SMITE_CHEST`). If this object is missing from the map, the AI skips the movement animation and proceeds directly to equipping, which could lead to desynced animations but functional combat.
*   **Aura Removal**: In Phase 1, the aura `SPELL_NIBLE_REFLEXES` is explicitly removed when transitioning to Phase 2. This suggests the aura is part of the Phase 1 kit and must be cleared to prevent interference with later phases.
*   **Thrash Timer**: The comment notes that `THRASH` is cast directly instead of relying on an aura proc because the aura procs "too much." This indicates a design choice to control the frequency of this ability manually.
*   **Positioning Fallback**: If the boss loses aggro or has no target during the `PhaseEquipEnd` step, it calls `EnterEvadeMode()`, effectively ending the encounter. This prevents the boss from standing idle if all players die or leave during the transition.

## Member Reference

*   **`boss_mr_smiteAI`**: Constructor that initializes the AI and calls `Reset()`.
*   **`Reset`**: Resets phase, timers, and equipment to initial state.
*   **`AttackedBy`**: Gatekeeper for initiating combat; calls `AttackStart` if valid.
*   **`AttackStart`**: Establishes combat state and starts chasing the target.
*   **`MovementInform`**: Handles movement completion; triggers `SplineFinished` or returns to chase.
*   **`SplineFinished`**: Finalizes movement to chest; clears equipment and prepares for swap.
*   **`PhaseEquipStart`**: Finds chest and starts movement spline to it.
*   **`PhaseEquipProcess`**: Swaps weapons based on health and stands up.
*   **`PhaseEquipEnd`**: Selects new target, sets new phase, and resumes combat.
*   **`UpdateAI`**: Main loop handling phase transitions, spell timers, and melee attacks.
*   **`GetAI_boss_mr_smite`**: Factory function to create the AI instance.
*   **`AddSC_boss_mr_smite`**: Registers the script with the server.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_mr_smite

*Source:* boss_mr_smite.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_mr_smiteAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset | method | Creature.Main/GetCreatureInfo, Creature.Main/LoadEquipment | — | — |
| AttackedBy | method | Unit.Main/GetVictim | — | — |
| AttackStart | method | Creature.MotionMaster/MoveChase, Unit.Main/AddThreat, Unit.Main/Attack, Unit.Main/GetMotionMaster, Unit.Main/SetInCombatWith | — | — |
| MovementInform | method | Creature.MotionMaster/MoveChase, Unit.Main/GetMotionMaster, Unit.Main/GetVictim, Unit.Main/SetSheath, Unit.Main/SetStandState | — | — |
| SplineFinished | method | Creature.Main/LoadEquipment, Unit.Main/SetSheath, Unit.Main/SetStandState | — | — |
| PhaseEquipStart | method | Creature.MotionMaster/MovePoint, MotionMaster/Clear, Unit.Main/GetMotionMaster, Unit.Main/SetFacingToObject, WorldObject.Object/FindNearestGameObject, WorldObject.Object/GetContactPoint | — | — |
| PhaseEquipProcess | method | Creature.Main/SetVirtualItem, CreatureAI/DoCastSpellIfCan, Unit.Main/GetHealthPercent, Unit.Main/SetStandState | — | — |
| PhaseEquipEnd | method | Creature.Main/SelectAttackingTarget, ScriptedAI/EnterEvadeMode, Unit.Main/GetHealthPercent, Unit.Main/SetSheath | — | — |
| UpdateAI | method | Creature.MotionMaster/MovePoint, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, MotionMaster/Clear, ScriptMgr/DoScriptText, shared_Util/urand, Unit.Main/AttackStop, Unit.Main/CanReachWithMeleeAutoAttack, Unit.Main/GetHealthPercent, Unit.Main/GetMeleeReach, Unit.Main/GetMotionMaster, Unit.Main/GetRandomAttackPoint, Unit.Main/GetVictim, Unit.Main/RemoveAurasDueToSpell, Unit.Main/SelectHostileTarget, WorldObject.Object/IsWithinDistInMap | — | — |
| GetAI_boss_mr_smite | function | — | — | — |
| AddSC_boss_mr_smite | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
