# PlayerAI

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# PlayerAI

**Purpose & Responsibilities**

`PlayerAI` and its derived class `PlayerControlledAI` provide the artificial intelligence logic for player characters who are under external control. This typically occurs when a player character is charmed, possessed, or otherwise controlled by another entity (such as a player using a pet-like ability or a creature casting a charm spell).

The base class `PlayerAI` serves as a minimal interface and utility provider, offering basic spell-casting validation (`CanCastSpell`) and lifecycle management. It does not implement autonomous behavior itself; its `UpdateAI` method is empty.

The derived class `PlayerControlledAI` implements the actual behavioral logic. It manages:
1.  **Target Acquisition:** Determining whom the controlled player should attack based on the controller's current target or random selection.
2.  **Movement:** Chasing targets or maintaining optimal casting distance.
3.  **Spell Casting:** Selecting and casting spells from the player's known spellbook, prioritizing healing spells on the controller or self, and handling global cooldowns.
4.  **State Management:** Handling combat states, facing adjustments, and cleanup when the controller dies or the charm breaks.

This unit is distinct from standard `CreatureAI` because it operates on a `Player` object, requiring careful handling of player-specific flags, spell maps, and interaction rules (e.g., ensuring the player doesn't accidentally attack friendly units or themselves).

## Member-by-Member Behavior

### Base Class: `PlayerAI`

#### Lifecycle and Setup
*   **`PlayerAI` (ctor):** Initializes the `me` pointer to the controlled `Player` and sets `enablePositiveSpells` to `false`. This flag controls whether beneficial spells (heals, buffs) are considered during AI decision-making.
*   **`~PlayerAI` (dtor):** Empty destructor. Cleanup is handled via the `Remove` method.
*   **`SetPlayer`:** Updates the internal `me` pointer. Called by `ChatHandler.PlayerBotMgr/OnPlayerInWorld` when a player enters the world, likely to ensure the AI instance references the correct player object after loading.
*   **`Remove`:** Cleans up the AI instance. It calls `Player.Main/SetAI` with `nullptr` to detach the AI from the player, then deletes the `PlayerAI` object itself. This is called by `Player.Main/RemoveAI`.

#### Utility Functions
*   **`CanCastSpell`:** Validates whether the controlled player can cast a specific spell on a target. This is a pre-check before attempting to cast.
    *   **Checks performed:**
        *   Target existence.
        *   **State Restrictions:** If not triggered, checks if the player is silenced, pacified, or in a state preventing reaction/control (`UNIT_STATE_CAN_NOT_REACT`).
        *   **Power Cost:** Verifies the player has sufficient mana/rage/etc.
        *   **Range:** Checks if the target is within the spell's min/max range using `WorldObject.Object/GetCombatDistance`.
    *   **Note:** This duplicates some logic found in `Spell::CheckCast()`, but provides a quick fail-fast mechanism for the AI loop.

#### Event Handlers
*   **`UpdateAI`:** The main update tick for the base class. It is intentionally empty. Subclasses override this to implement behavior. Called by `Player.Main/Update`.
*   **`MovementInform`:** A placeholder callback for movement generator events. Currently empty. Called by `PointMovementGenerator/MovementInform#4`.

### Derived Class: `PlayerControlledAI`

#### Initialization and Spell Preparation
*   **`PlayerControlledAI` (ctor):** Sets up the AI for a specific controller.
    *   **Role Classification:** Determines if the player is a melee class (Warrior, Rogue, Paladin, Druid) or a healer (Paladin, Druid, Priest, Shaman) based on `Unit.Main/GetClass`. This influences movement and targeting logic.
    *   **Spell Filtering:** Iterates through the player's `PlayerSpellMap` (via `Player.Main/GetSpellMap`) to build a list of `usableSpells`.
        *   Excludes removed, disabled, passive, or auto-cast-disabled spells.
        *   Excludes spells with `AURA_INTERRUPT_DAMAGE_CANCELS` to prevent casting spells that would be immediately interrupted by damage.
        *   Respects the `enablePositiveSpells` flag.
        *   **Rank Deduplication:** Removes lower-rank versions of spells if a higher rank exists in the usable list, using `SpellEntry/CompareAuraRanks`.
    *   **Initial Targeting:** Clears existing movement. If a controller is provided and is a creature, it attempts to select a random attacking target from the controller's perspective (`Creature.Main/SelectAttackingTarget`) and calls `UpdateTarget`.

#### Core Logic
*   **`FindController`:** Retrieves the `Unit` object corresponding to the stored `controllerGuid` from the map using `Map.Main/GetUnit`. Returns `nullptr` if the controller is not found or has left the map.

*   **`UpdateTarget`:** Manages combat engagement and movement relative to a specific victim.
    *   **Disengagement Conditions:** Stops attack and interrupts spells if the victim is charmed by the same master as the player, or if the player is feared/polymorphed.
    *   **Combat State Sync:** Ensures both the player and the controller (if a player) are in combat with the victim.
    *   **Movement Logic:**
        *   If the controller is a player, it uses `MoveChase` unless already moving.
        *   If the controller is a creature (or null), it distinguishes between melee and ranged behavior:
            *   **Melee:** Chases if out of melee range. If in range, adjusts facing to the target (`SetFacingToObject`) if not already aligned.
            *   **Ranged:** Sets a chase distance of 25.0 yards. If out of range (30 yards for non-moving casters), it chases. If in range, it stops moving and adjusts facing.
            *   **Roots:** Prevents chasing if the player is rooted.

*   **`UpdateAI#2`:** The main behavioral loop, executed every update tick.
    *   **Cleanup:** Removes the AI if the player is deleted, not in the world, or dead.
    *   **Controller Validation:**
        *   **Player Controller:** If the controller is a player, it checks if the controller is alive. If dead, it removes charm auras from both the player and the controller and returns early (noting that the controller object might be invalid after aura removal).
        *   **Creature Controller:** If the controller is a creature, it checks if the creature is alive and in combat. If not, it removes charm auras and returns.
    *   **Target Selection:**
        *   **Player Controller:** Uses the controller's victim. If the player has no victim or shares the controller's victim, it adopts the controller's target. React states (Passive/Defensive/Aggressive) influence this slightly, though the logic largely defaults to sharing the controller's target.
        *   **Creature Controller:** Selects a random target from the controller's aggro list (`Creature.Main/SelectAttackingTarget`). If the player already has a valid victim they can attack, it retains that victim. Falls back to nearest target if no controller exists.
    *   **Hostility Check:** Ensures the selected victim is hostile to the player.
    *   **Spell Casting Loop:**
        *   Managed by a global cooldown (`uiGlobalCD`).
        *   If a non-melee spell is currently casting, sets CD to 200ms.
        *   Otherwise, picks a random spell from `usableSpells`.
        *   **Target Prioritization for Positive Spells:** If the spell is beneficial (heal/buff), it prioritizes the controller, then the player itself, before defaulting to the combat victim.
        *   **Execution:** Calls `CanCastSpell`. If valid, casts the spell via `SpellCaster/CastSpell#2` and sets CD to 1500ms. Adjusts chase distance for ranged classes.

*   **`~PlayerControlledAI` (dtor):** Empty destructor.

## Cross-Unit Boundaries

*   **`Player.Main`:**
    *   **Called By:** `PlayerAI.Remove` calls `Player.Main/SetAI` to detach itself. `PlayerAI.UpdateAI` is called by `Player.Main/Update`. `PlayerControlledAI` constructor calls `Player.Main/GetSpellMap` to analyze available spells. `PlayerControlledAI.UpdateAI` calls `Player.Main/RemoveAI` on cleanup.
    *   **Why:** `PlayerAI` is tightly coupled to the `Player` object it controls. It needs access to the player's spellbook, state, and AI slot.

*   **`Creature.Main` / `Creature.MotionMaster`:**
    *   **Called By:** `PlayerControlledAI` constructor calls `Creature.Main/SelectAttackingTarget` to initialize targeting if the controller is a creature. `PlayerControlledAI.UpdateAI` calls `Creature.Main/SelectAttackingTarget` to pick new targets. `PlayerControlledAI.UpdateTarget` calls `Creature.MotionMaster/GetCurrentMovementGeneratorType` and `Creature.MotionMaster/MoveChase`.
    *   **Why:** When controlled by a creature, the AI mimics the creature's combat behavior by sharing its threat list and movement patterns.

*   **`SpellMgr` / `SpellEntry`:**
    *   **Called By:** Extensively used in `PlayerControlledAI` constructor and `UpdateAI` to retrieve spell data (`GetSpellEntry`), check attributes (`IsPositiveSpell`, `HasAuraInterruptFlag`), and compare ranks (`CompareAuraRanks`).
    *   **Why:** The AI needs detailed metadata about spells to decide if they are usable, beneficial, or interruptible.

*   **`Unit.Main`:**
    *   **Called By:** Used throughout for state checks (`GetPower`, `HasUnitState`, `GetClass`, `GetMotionMaster`, `Attack`, `AttackStop`, `IsHostileTo`, etc.).
    *   **Why:** Fundamental unit interactions like attacking, checking hostility, and managing motion masters are delegated to the `Unit` base class.

*   **`CharmInfo`:**
    *   **Called By:** `PlayerControlledAI.UpdateAI` calls `CharmInfo/HasReactState` to determine how the player should react to threats (Passive vs. Aggressive).
    *   **Why:** Charmed units have specific react states that dictate their AI behavior.

*   **`MotionMaster`:**
    *   **Called By:** `PlayerControlledAI` constructor calls `MotionMaster/Clear`. `UpdateTarget` calls `MotionMaster/Clear` and `MoveChase`.
    *   **Why:** Direct control over the player's movement pathing is required for chasing targets.

*   **`Map.Main`:**
    *   **Called By:** `PlayerControlledAI.FindController` calls `Map.Main/GetUnit`.
    *   **Why:** To locate the controller unit in the game world by GUID.

*   **`Object` / `WorldObject.Object`:**
    *   **Called By:** Various checks for type (`GetTypeId`), deletion (`IsDeleted`), world presence (`IsInWorld`), and spatial relationships (`GetCombatDistance`, `IsWithinDist`, `HasInArc`).
    *   **Why:** Basic object validity and spatial reasoning.

*   **`SpellCaster`:**
    *   **Called By:** `PlayerControlledAI.UpdateAI` calls `SpellCaster/CastSpell#2` and `SpellCaster/IsNonMeleeSpellCasted`. `PlayerControlledAI.UpdateTarget` calls `SpellCaster/InterruptNonMeleeSpells`.
    *   **Why:** Actual spell execution and interruption logic resides in the `SpellCaster` interface.

*   **`shared_Util`:**
    *   **Called By:** `PlayerControlledAI.UpdateAI` calls `shared_Util/urand` to pick random spells.
    *   **Why:** Randomization is needed for spell selection.

*   **`Errors`:**
    *   **Called By:** `PlayerControlledAI` constructor calls `Errors/PrintStacktraceAndThrow` (implicitly via `ASSERT`).
    *   **Why:** Debugging aid to catch null players during initialization.

## Data Model

This unit does not interact directly with any database tables. All data (spells, player state, unit positions) is retrieved from in-memory structures (`DBCStores`, `PlayerSpellMap`, `Map` objects).

## Notable Implementation Details

1.  **Spell Rank Deduplication Logic:** In the `PlayerControlledAI` constructor, the code iterates through `usableSpells` to remove lower ranks. It compares `SpellFamilyName`, `SpellIconID`, and `SpellVisual` to identify spell families. This is a heuristic approach ("Meme sort" comment suggests it's a known workaround) because DBC data doesn't always explicitly link ranks. It relies on visual/icon similarity to group spells.

2.  **Controller Death Handling:** In `PlayerControlledAI.UpdateAI`, if the controller (player or creature) dies, the code calls `RemoveCharmAuras` on both the controlled player and the controller. The comments explicitly warn that the controller object might be invalid after this call, but since `Pcontroller`/`Ccontroller` are local pointers, it's safe to dereference them *before* the aura removal potentially deletes the unit object. However, the code returns immediately after, avoiding further use.

3.  **Positive Spell Targeting Priority:** When casting a beneficial spell, the AI prioritizes the controller, then the self, then the victim. This ensures that if a player is controlling a healer, the healer will try to keep the controller alive first.

4.  **Movement vs. Combat State:** `UpdateTarget` carefully manages the transition between moving and standing still. For ranged classes, it sets a specific chase distance (25.0f) and stops moving if within 30.0f yards and not moving. For melee, it stops if in melee range. This prevents the AI from constantly jittering between move and stop states.

5.  **Global Cooldown (GCD) Simulation:** The AI implements a simple GCD (`uiGlobalCD`) to prevent spamming spells. It resets to 1500ms after a successful cast or 200ms if a non-melee spell is currently casting. This is separate from the server's actual GCD enforcement but helps regulate AI behavior.

6.  **Face Target Adjustment:** The AI explicitly calls `SetFacingToObject` if the player is not facing the target within a certain arc (0.2f for melee, 0.5f for ranged). This ensures animations and melee attacks look correct.

7.  **Empty Base Update:** The base `PlayerAI::UpdateAI` is empty. This design allows `PlayerAI` to be used as a lightweight placeholder or base for other AI types without imposing unnecessary overhead.

## Member Reference

**Remove**: Detaches the AI from the player by calling `Player.Main/SetAI(nullptr)` and deletes the `PlayerAI` object. Called by `Player.Main/RemoveAI`.

**~PlayerAI**: Empty destructor.

**PlayerAI**: Constructor initializing `me` and `enablePositiveSpells`.

**SetPlayer**: Updates the `me` pointer. Called by `ChatHandler.PlayerBotMgr/OnPlayerInWorld`.

**CanCastSpell**: Validates if a spell can be cast on a target, checking state, power, and range. Calls `Object/HasFlag`, `Unit.Main/GetPower`, `Unit.Main/HasUnitState`, `WorldObject.Object/GetCombatDistance`.

**MovementInform**: Placeholder callback for movement events. Called by `PointMovementGenerator/MovementInform#4`.

**UpdateAI**: Empty base update tick. Called by `Player.Main/Update`.

**PlayerControlledAI**: Constructor setting up role classification, filtering usable spells (removing lower ranks), clearing movement, and initializing targeting if a controller is present. Calls `Creature.Main/SelectAttackingTarget`, `Errors/PrintStacktraceAndThrow`, `MotionMaster/Clear`, `Object/GetObjectGuid`, `Object/ToCreature`, `ObjectGuid/ObjectGuid`, `Player.Main/GetSpellMap`, `SpellEntry/CompareAuraRanks`, `SpellEntry/HasAuraInterruptFlag`, `SpellEntry/IsPositiveSpell`, `SpellMgr/GetSpellEntry`, `SpellMgr/Instance`, `Unit.Main/GetClass`, `Unit.Main/GetMotionMaster`. Called by `Player.Main/SetControlledBy`.

**FindController**: Retrieves the controller unit from the map using `controllerGuid`. Calls `Map.Main/GetUnit`, `WorldObject.Object/GetMap`.

**UpdateTarget**: Manages combat engagement, syncs combat states, and adjusts movement/facing based on target distance and class type. Calls `Creature.MotionMaster/GetCurrentMovementGeneratorType`, `Creature.MotionMaster/MoveChase`, `MotionMaster/Clear`, `Object/GetTypeId`, `ObjectGuid/operator==`, `SpellCaster/InterruptNonMeleeSpells`, `Unit.Main/Attack`, `Unit.Main/AttackStop`, `Unit.Main/CanReachWithMeleeAutoAttack`, `Unit.Main/GetCharmerGuid`, `Unit.Main/GetMotionMaster`, `Unit.Main/GetVictim`, `Unit.Main/IsCharmed`, `Unit.Main/IsFeared`, `Unit.Main/IsInRoots`, `Unit.Main/IsPolymorphed`, `Unit.Main/SetCasterChaseDistance`, `Unit.Main/SetFacingToObject`, `Unit.Main/SetInCombatWith`, `WorldObject.Object/HasInArc`, `WorldObject.Object/IsMoving`, `WorldObject.Object/IsWithinDist`.

**~PlayerControlledAI**: Empty destructor.

**UpdateAI#2**: Main behavioral loop. Handles cleanup, controller validation, target selection (based on controller type), hostility checks, and spell casting with GCD management. Calls `CharmInfo/HasReactState`, `Creature.Main/SelectAttackingTarget`, `Object/GetTypeId`, `Object/IsDeleted`, `Object/IsInWorld`, `Object/ToCreature`, `Player.Main/RemoveAI`, `shared_Util/urand`, `SpellCaster/CastSpell#2`, `SpellCaster/IsNonMeleeSpellCasted`, `SpellEntry/IsPositiveSpell#3`, `SpellMgr/GetSpellEntry`, `SpellMgr/Instance`, `Unit.Main/CanAttack`, `Unit.Main/GetCharmInfo`, `Unit.Main/GetVictim`, `Unit.Main/IsAlive`, `Unit.Main/IsHostileTo`, `Unit.Main/IsInCombat`, `Unit.Main/RemoveCharmAuras`, `Unit.Main/SelectNearestTarget`, `Unit.Main/SetCasterChaseDistance`.

---

<!-- machine-true, projected from graph.json -->

## Map — PlayerAI

*Source:* PlayerAI.cpp, PlayerAI.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| Remove | method | Player.Main/SetAI | Player.Main/RemoveAI | — |
| ~PlayerAI | dtor | — | — | — |
| PlayerAI | ctor | — | — | — |
| SetPlayer | method | — | ChatHandler.PlayerBotMgr/OnPlayerInWorld | — |
| CanCastSpell | method | Object/HasFlag, Unit.Main/GetPower, Unit.Main/HasUnitState, WorldObject.Object/GetCombatDistance | — | — |
| MovementInform | method | — | PointMovementGenerator/MovementInform#4 | — |
| UpdateAI | method | — | Player.Main/Update | — |
| PlayerControlledAI | ctor | Creature.Main/SelectAttackingTarget, Errors/PrintStacktraceAndThrow, MotionMaster/Clear, Object/GetObjectGuid, Object/ToCreature, ObjectGuid/ObjectGuid, Player.Main/GetSpellMap, SpellEntry/CompareAuraRanks, SpellEntry/HasAuraInterruptFlag, SpellEntry/IsPositiveSpell, SpellMgr/GetSpellEntry, SpellMgr/Instance, Unit.Main/GetClass, Unit.Main/GetMotionMaster | Player.Main/SetControlledBy | — |
| FindController | method | Map.Main/GetUnit, WorldObject.Object/GetMap | — | — |
| UpdateTarget | method | Creature.MotionMaster/GetCurrentMovementGeneratorType, Creature.MotionMaster/MoveChase, MotionMaster/Clear, Object/GetTypeId, ObjectGuid/operator==, SpellCaster/InterruptNonMeleeSpells, Unit.Main/Attack, Unit.Main/AttackStop, Unit.Main/CanReachWithMeleeAutoAttack, Unit.Main/GetCharmerGuid, Unit.Main/GetMotionMaster, Unit.Main/GetVictim, Unit.Main/IsCharmed, Unit.Main/IsFeared, Unit.Main/IsInRoots, Unit.Main/IsPolymorphed, Unit.Main/SetCasterChaseDistance, Unit.Main/SetFacingToObject, Unit.Main/SetInCombatWith, WorldObject.Object/HasInArc, WorldObject.Object/IsMoving, WorldObject.Object/IsWithinDist | — | — |
| ~PlayerControlledAI | dtor | — | — | — |
| UpdateAI#2 | method | CharmInfo/HasReactState, Creature.Main/SelectAttackingTarget, Object/GetTypeId, Object/IsDeleted, Object/IsInWorld, Object/ToCreature, Player.Main/RemoveAI, shared_Util/urand, SpellCaster/CastSpell#2, SpellCaster/IsNonMeleeSpellCasted, SpellEntry/IsPositiveSpell#3, SpellMgr/GetSpellEntry, SpellMgr/Instance, Unit.Main/CanAttack, Unit.Main/GetCharmInfo, Unit.Main/GetVictim, Unit.Main/IsAlive, Unit.Main/IsHostileTo, Unit.Main/IsInCombat, Unit.Main/RemoveCharmAuras, Unit.Main/SelectNearestTarget, Unit.Main/SetCasterChaseDistance | — | — |
