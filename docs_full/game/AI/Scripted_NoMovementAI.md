# Scripted_NoMovementAI

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Scripted_NoMovementAI

**Purpose & Responsibilities**

`Scripted_NoMovementAI` is a specialized base class for stationary creatures in the World of Warcraft emulator. It inherits from `ScriptedAI` to retain access to standard combat, spell-casting, and threat-management helpers, but overrides the default movement behavior. Specifically, it suppresses the automatic movement toward a target that typically occurs when a creature enters combat. This class is intended for NPCs that must remain fixed in place, such as turrets, traps, or stationary bosses.

## Member-by-Member Behavior

### Constructor: `Scripted_NoMovementAI`

The constructor initializes the AI instance by delegating to its parent class, `ScriptedAI`.

*   **Signature:** `explicit Scripted_NoMovementAI(Creature* pCreature)`
*   **Behavior:** Accepts a pointer to the `Creature` object representing the NPC. It passes this pointer to the `ScriptedAI` constructor via the initializer list (`: ScriptedAI(pCreature)`). This links the AI logic to the game entity, enabling access to the creature's state through the `me` member inherited from `ScriptedAI`.
*   **Initialization:** No additional state is initialized in this derived class. All evasion cooldowns, home area checks, and equipment states are managed by the parent `ScriptedAI` class.

### Inherited Behavior Context

While `Scripted_NoMovementAI` defines only the constructor in this unit, its functionality relies on the inheritance chain:
1.  **`ScriptedAI`**: Provides the AI framework, including combat hooks (`EnterCombat`, `EnterEvadeMode`), spell casting (`DoCastSpell`), threat management (`DoResetThreat`), and movement commands (`DoStartMovement`).
2.  **`BasicAI`**: The lowest level of the AI hierarchy, handling fundamental creature actions.

The key distinction of `Scripted_NoMovementAI` is its override of `AttackStart`. In standard `ScriptedAI` (and `BasicAI`), `AttackStart` often triggers movement toward the victim. `Scripted_NoMovementAI` overrides this to suppress that behavior, ensuring the NPC remains stationary unless explicitly moved by a script. The `AttackStart` declaration is visible in the struct, indicating the derived class provides a custom implementation (likely empty or non-moving) to prevent the parent's movement logic.

## Cross-Unit Boundaries

### Called By

`Scripted_NoMovementAI` is instantiated by specific encounter AI classes to control stationary NPCs:

1.  **`boss_ouro/npc_ouro_spawnerAI`**: Uses this AI for Ouro or its spawners. Ouro moves along a track but does not freely chase players; a no-movement AI is appropriate for certain phases or entities.
2.  **`boss_thaddius/npc_tesla_coilAI`**: Uses this AI for the Tesla Coils in the Thaddius encounter. These are stationary defensive turrets that shoot lightning at players and must remain fixed.
3.  **`western_plaguelands/npc_andorhal_towerAI`**: Uses this AI for NPCs associated with the Andorhal Tower questline, likely stationary guards or magical defenses.

The dependency flows **from** the specific encounter script **to** `Scripted_NoMovementAI`. The encounter script allocates the AI object, passing the `Creature*` pointer. The AI then responds to game events via hooks defined in `ScriptedAI` and overridden in `Scripted_NoMovementAI`.

### Calls Out

The MAP indicates no direct calls out to other units from `Scripted_NoMovementAI` members defined in this unit. The constructor simply delegates to the parent. Complex logic (spell casting, movement checks) is handled by the parent `ScriptedAI` class, which calls into the core engine. Those calls are not considered direct calls from `Scripted_NoMovementAI` in this context.

## Data Model

This unit does not interact directly with any database tables. It operates entirely in memory using the `Creature` object passed during construction. All data required for AI decision-making (position, health, target, buffs) is retrieved from the in-memory representation of the game world.

## Notable Implementation Details

1.  **Inheritance Chain**: `Scripted_NoMovementAI` -> `ScriptedAI` -> `BasicAI`. Behavior understanding requires looking at `ScriptedAI.h` for helpers and `BasicAI` for base mechanics.
2.  **Override of `AttackStart`**: The struct declares `void AttackStart(Unit*) override;`. This is the critical differentiator. By overriding this, `Scripted_NoMovementAI` allows developers to implement a stationary combat style. The implementation is not in the header, but the declaration ensures the vtable points to the derived class's version, preventing the parent's movement logic.
3.  **Helper Functions Availability**: Despite being "No Movement," this AI still has access to `DoStartMovement` from `ScriptedAI`. This allows a developer to manually trigger movement if needed (e.g., for a scripted phase change), even though the default `AttackStart` behavior is suppressed.
4.  **Static Sound Helper**: `ScriptedAI` provides `DoPlaySoundToSet`, which is static. This can be called without an instance, requiring a `WorldObject*` source. Useful for playing ambient sounds from stationary objects.
5.  **Threat Management**: The AI inherits `DoResetThreat` and `DoModifyThreatPercent`. Stationary turrets often need to manage threat carefully to ensure players don't aggro them unintentionally or to pull them off tanks.
6.  **Evade Logic**: The AI inherits `EnterEvadeIfOutOfCombatArea` and `EnterEvadeIfOutOfHomeArea`. This is crucial for stationary NPCs. If a player pulls a stationary turret out of its designated combat zone, the AI can automatically disengage and return to its home position, preventing the NPC from chasing players across the map.

## Member Reference

**Scripted_NoMovementAI**
Constructor for the `Scripted_NoMovementAI` class. Takes a `Creature*` pointer and delegates initialization to the parent `ScriptedAI` constructor. Used by `boss_ouro/npc_ouro_spawnerAI`, `boss_thaddius/npc_tesla_coilAI`, and `western_plaguelands/npc_andorhal_towerAI` to instantiate AI for stationary NPCs.

---

<!-- machine-true, projected from graph.json -->

## Map — Scripted_NoMovementAI

*Source:* ScriptedAI.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| Scripted_NoMovementAI | ctor | — | boss_ouro/npc_ouro_spawnerAI, boss_thaddius/npc_tesla_coilAI, western_plaguelands/npc_andorhal_towerAI | — |
