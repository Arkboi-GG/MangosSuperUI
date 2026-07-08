<!-- provenance: verbose -->
# boss_gahzranka

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# boss_gahzranka

**Purpose & Responsibilities**
`boss_gahzranka.cpp` implements the AI for **Gahz'ranka**, a boss in the **Zul'Gurub** instance. The `boss_gahzrankaAI` class manages combat abilities (Frost Breath, Massive Geyser, Slam), conditional movement, and instance state synchronization (`NOT_STARTED`, `IN_PROGRESS`, `DONE`) via `ScriptedInstance`.

**Member-by-Member Behavior**

### Initialization & Lifecycle
*   **`boss_gahzrankaAI`**: Retrieves `ScriptedInstance`, calls `Reset()` and `CheckSpawnStatus()`.
*   **`JustRespawned`**: Re-evaluates spawn state by calling `CheckSpawnStatus()`.

### Instance State Management
*   **`Reset`**: Resets ability timers (8s, 25s, 17s). Sets instance data to `NOT_STARTED` unless already `DONE`.
*   **`Aggro`**: Sets instance data to `IN_PROGRESS`.
*   **`JustDied`**: Sets instance data to `DONE`.

### Movement & Spawn Logic
*   **`CheckSpawnStatus`**: If state is not `IN_PROGRESS`, despawns the creature with a 3-day respawn timer. If `IN_PROGRESS`, moves the creature to intermediate point 0 and sets its home position.
*   **`MovementInform`**: Upon completing movement to point 0, immediately moves the creature to point 1 (final combat position).

### Combat AI
*   **`UpdateAI`**: Main loop. Returns if no target. Otherwise:
    *   **Frost Breath**: Casts on victim every 8–20s.
    *   **Massive Geyser**: Casts on a random target every 16–24s. On success, resets all threat (`DoResetThreat`). *Note: Source comments indicate this spell is broken.*
    *   **Slam**: Casts on victim every 12–20s.
    *   Performs melee attacks via `DoMeleeAttackIfReady()`.

## Cross-Unit Boundaries
*   **`ScriptedInstance`**: `boss_gahzrankaAI` reads/writes `TYPE_GAHZRANKA` state to coordinate with the instance script.
*   **`Creature`/`Unit`/`MotionMaster`**: Used for target selection, spell casting, movement commands, and lifecycle management (despawn/respawn).
*   **`ScriptMgr`**: `AddSC_boss_gahzranka` registers the script with the engine.

## Data Model
No database tables are accessed. State is managed entirely in-memory via `ScriptedInstance`.

## Notable Implementation Details
1.  **Broken Spell**: `SPELL_MASSIVEGEYSER` is noted as non-functional due to a summon bug. The AI logic executes, but the visual/mechanical effect fails.
2.  **Two-Step Movement**: `CheckSpawnStatus` moves to point 0, and `MovementInform` chains to point 1. This ensures the creature is fully loaded/positioned before combat engagement.
3.  **Despawn Guard**: If the instance state is not `IN_PROGRESS`, the boss despawns immediately with a long respawn timer, preventing it from appearing during non-boss phases.

## Member Reference

**`boss_gahzrankaAI`**
Constructor. Retrieves `ScriptedInstance`, calls `Reset()` and `CheckSpawnStatus()`.

**`Reset`**
Resets timers. Sets instance state to `NOT_STARTED` if not `DONE`.

**`Aggro`**
Sets instance state to `IN_PROGRESS`.

**`JustDied`**
Sets instance state to `DONE`.

**`JustRespawned`**
Calls `CheckSpawnStatus()`.

**`CheckSpawnStatus`**
Despawns creature if state is not `IN_PROGRESS`; otherwise moves to point 0 and sets home position.

**`MovementInform`**
On completion of point 0, moves to point 1.

**`UpdateAI`**
Manages timers for Frost Breath, Massive Geyser (with threat reset), and Slam. Handles melee attacks.

**`GetAI_boss_gahzranka`**
Factory function returning a new `boss_gahzrankaAI` instance.

**`AddSC_boss_gahzranka`**
Registers the script with `ScriptMgr`.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_gahzranka

*Source:* boss_gahzranka.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_gahzrankaAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset | method | InstanceData/GetData, InstanceData/SetData | — | — |
| Aggro | method | InstanceData/SetData | — | — |
| JustDied | method | InstanceData/SetData | — | — |
| JustRespawned | method | — | — | — |
| CheckSpawnStatus | method | Creature.Main/DisappearAndDie, Creature.Main/SetHomePosition, Creature.Main/SetRespawnTime, Creature.MotionMaster/MovePoint, InstanceData/GetData, Unit.Main/GetMotionMaster | — | — |
| MovementInform | method | Creature.MotionMaster/MovePoint, Unit.Main/GetMotionMaster | — | — |
| UpdateAI | method | Creature.Main/SelectAttackingTarget, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, ScriptedAI/DoResetThreat, shared_Util/urand, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_boss_gahzranka | function | — | — | — |
| AddSC_boss_gahzranka | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
