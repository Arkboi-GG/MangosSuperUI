# boss_cannon_master_willey

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# boss_cannon_master_willey

**Purpose & Responsibilities**
This translation unit implements the artificial intelligence and environmental interactions for **Cannon Master Willey**, a boss encounter in the Stratholme instance, along with a related interactive game object, the **Scarlet Cannon**. The unit provides two distinct scripts:
1.  `boss_cannon_master_willeyAI`: Controls the boss's combat behavior, including spell casting, summoning adds (Crimson Riflemen), managing movement between melee and ranged positions, and controlling a gate mechanism.
2.  `GO_scarlet_cannon`: Handles the interaction when a player uses a specific cannon game object, spawning a projectile that fires a spell.

The unit does not interact with any database tables; all logic is driven by in-memory state, timers, and hardcoded coordinates.

## Member-by-Member Behavior

### Boss AI: `boss_cannon_master_willeyAI`

The `boss_cannon_master_willeyAI` struct inherits from `ScriptedAI` and manages the boss's lifecycle through standard AI hooks. It maintains several internal timers (`m_uiKnockAwayTimer`, `m_uiPummelTimer`, `m_uiShootTimer`, `m_uiSummonRiflemanTimer`) and a boolean flag `m_bInMelee` to track whether the boss is currently engaging in close-quarters combat or holding position for ranged attacks.

#### Initialization and State Management

*   **`boss_cannon_master_willeyAI` (Constructor)**: Initializes the AI by casting the creature's instance data to `ScriptedInstance` and calling `Reset()` to establish initial states and timer values.
*   **`Reset`**: Called when the boss spawns or resets. It opens the associated gate via `ToggleGate(OPEN)`, enables combat movement, sets `m_bInMelee` to `true`, and initializes all ability timers with randomized or fixed durations using `urand`.
*   **`Aggro`**: Triggered when the boss enters combat. It immediately closes the gate via `ToggleGate(CLOSED)` to trap players or block escape routes.
*   **`JustDied`**: Triggered upon the boss's death. It reopens the gate via `ToggleGate(OPEN)`, allowing progression.
*   **`EnterEvadeMode`**: Triggered when the boss despawns or loses aggro. It cleans up the environment by finding all summoned `NPC_CRIMSON_RIFLEMAN` creatures within a 200-yard radius using `GetCreatureListWithEntryInGrid` and forcing them to despawn via `ForcedDespawn`. Finally, it calls the parent `ScriptedAI::EnterEvadeMode`.

#### Combat Logic

*   **`UpdateAI`**: The core loop executed every tick. It performs the following checks in order:
    1.  **Target Validation**: Returns early if no hostile target exists.
    2.  **Spell Casting**: Checks and decrements timers for `SPELL_PUMMEL`, `SPELL_KNOCK_AWAY`, and `SPELL_SHOOT`. If a timer expires, it attempts to cast the spell on the current victim using `DoCastSpellIfCan`. Timers are reset to new random or fixed values upon successful casts.
    3.  **Add Summoning**: Checks `m_uiSummonRiflemanTimer`. If expired, it uses a `switch` statement on a random integer (0–8) to select one of nine predefined patterns. Each pattern summons three `NPC_CRIMSON_RIFLEMAN` creatures at specific hardcoded coordinates (defined by `ADD_1X` through `ADD_9X` macros) with a 240-second despawn timer. The summon timer is then reset to 10 seconds.
    4.  **Movement Control**: Determines whether the boss should move or stand still based on distance and line-of-sight (LOS) to the victim:
        *   If combat movement is disabled (ranged stance) and the boss is too close (<8 yards), too far (>27 yards), or lacks LOS, it enables movement, starts moving toward the victim, and sets `m_bInMelee` to `true`.
        *   If combat movement is enabled (melee stance) and the boss is within the optimal range (8–27 yards) with LOS, it disables movement, stops moving, and sets `m_bInMelee` to `false`.
    5.  **Melee Attack**: If no movement change occurred, it attempts a melee attack if ready.

#### Environmental Interaction

*   **`ToggleGate`**: Locates the nearest game object with entry `GO_WILLEY_GATE` within 200 yards. Depending on the `bOpen` parameter and the gate's current state (`GO_STATE_READY` or `GO_STATE_ACTIVE`), it triggers the gate's open/close animation via `pInstance->DoUseDoorOrButton`.

#### Summon Management

*   **`JustSummoned`**: Called when a Crimson Rifleman is successfully summoned. It forces the new creature into combat with the zone using `SetInCombatWithZone`, ensuring it immediately engages nearby players.

### Helper Functions

*   **`GetAI_boss_cannon_master_willey`**: Factory function that returns a new instance of `boss_cannon_master_willeyAI` for the given creature.
*   **`GO_scarlet_cannon`**: Handler for the Scarlet Cannon game object. When a player interacts with it, it summons a cannonball game object (`GO_CANNONBALL`) at a fixed coordinate near the cannon. It then immediately calls `Use` on the cannonball with the player as the target, which presumably triggers the firing spell (`SPELL_CANNON_FIRE`). Returns `false` to indicate the default interaction behavior was overridden.
*   **`AddSC_boss_cannon_master_willey`**: Registration function. It creates two script entries: one for the boss AI (`boss_cannon_master_willey`) and one for the game object handler (`go_scarlet_cannon`), and registers them with the script manager.

## Cross-Unit Boundaries

*   **`ScriptedAI` / `ScriptedInstance`**: The AI relies heavily on the base `ScriptedAI` class for timer management, spell casting helpers (`DoCastSpellIfCan`, `DoMeleeAttackIfReady`), and movement commands (`DoStartMovement`, `DoStartNoMovement`). It also interacts with `ScriptedInstance` to manipulate doors/buttons.
*   **`WorldObject` / `Creature` / `GameObject`**: Used for spatial queries (`FindNearestGameObject`, `GetDistance2d`, `IsWithinLOSInMap`), summoning entities (`SummonCreature`, `SummonGameObject`), and state manipulation (`SetInCombatWithZone`, `ForcedDespawn`, `GetGoState`).
*   **`shared_Util`**: Uses `urand` for generating random timer intervals and summon patterns.
*   **`ScriptMgr`**: The `AddSC_boss_cannon_master_willey` function registers the scripts with the global script manager, making them available to the engine.

## Data Model

This unit does not access any database tables. All configuration (spell IDs, creature entries, coordinates, timers) is hardcoded in the source file.

## Notable Implementation Details

*   **Hardcoded Summon Patterns**: The summoning of Crimson Riflemen is controlled by a large `switch` statement in `UpdateAI`. Each case corresponds to a specific set of three coordinates from the `ADD_*` macros. This creates a static, predictable pattern of add spawns rather than dynamic positioning.
*   **Movement Thresholds**: The boss switches between melee and ranged stances based on strict distance thresholds (8 yards and 27 yards) and LOS checks. This prevents the boss from chasing players indefinitely or standing idle when out of range.
*   **Gate Logic**: The `ToggleGate` function checks the current state of the gate before triggering it. This prevents redundant animations or errors if the gate is already in the desired state.
*   **Clean Despawn**: In `EnterEvadeMode`, the AI explicitly cleans up all summoned Riflemen. This is crucial for preventing orphaned NPCs from lingering after the boss resets or despawns.
*   **Cannon Interaction**: The `GO_scarlet_cannon` function uses a "summon and use" pattern. It spawns a temporary cannonball GO and immediately activates it. This decouples the visual effect (cannonball spawn) from the spell logic (likely handled by the cannonball's own script or template).

## Member Reference

*   **`boss_cannon_master_willeyAI`**: Constructor for the boss AI, initializing instance data and calling `Reset`.
*   **`Reset`**: Resets timers, enables combat movement, and opens the gate.
*   **`Aggro`**: Closes the gate when combat begins.
*   **`JustDied`**: Opens the gate when the boss dies.
*   **`ToggleGate`**: Finds the nearest gate GO and opens/closes it based on its current state.
*   **`JustSummoned`**: Forces summoned Riflemen into combat with the zone.
*   **`EnterEvadeMode`**: Despawns all summoned Riflemen and calls parent evade logic.
*   **`UpdateAI`**: Main AI loop handling spell casts, add summoning, and movement logic.
*   **`GetAI_boss_cannon_master_willey`**: Factory function returning a new `boss_cannon_master_willeyAI` instance.
*   **`GO_scarlet_cannon`**: Handles player interaction with the cannon, spawning and activating a cannonball GO.
*   **`AddSC_boss_cannon_master_willey`**: Registers the boss AI and cannon GO scripts with the script manager.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_cannon_master_willey

*Source:* boss_cannon_master_willey.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_cannon_master_willeyAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset | method | CreatureAI/SetCombatMovement, shared_Util/urand | — | — |
| Aggro | method | — | — | — |
| JustDied | method | — | — | — |
| ToggleGate | method | GameObject/GetGoState, Object/GetObjectGuid, ScriptedInstance/DoUseDoorOrButton, WorldObject.Object/FindNearestGameObject | — | — |
| JustSummoned | method | Creature.Main/SetInCombatWithZone | — | — |
| EnterEvadeMode | method | Creature.Main/ForcedDespawn, GridSearchers/GetCreatureListWithEntryInGrid#2, ScriptedAI/EnterEvadeMode | — | — |
| UpdateAI | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, CreatureAI/IsCombatMovementEnabled, CreatureAI/SetCombatMovement, ScriptedAI/DoStartMovement, ScriptedAI/DoStartNoMovement, shared_Util/urand, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget, WorldObject.Object/GetDistance2d#3, WorldObject.Object/IsWithinLOSInMap, WorldObject.Object/SummonCreature#2 | — | — |
| GetAI_boss_cannon_master_willey | function | — | — | — |
| GO_scarlet_cannon | function | GameObject/Use, WorldObject.Object/SummonGameObject | — | — |
| AddSC_boss_cannon_master_willey | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
