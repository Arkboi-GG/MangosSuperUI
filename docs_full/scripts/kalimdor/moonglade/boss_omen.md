<!-- provenance: verbose -->
# boss_omen

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# boss_omen

**Purpose & Responsibilities**  
`boss_omen` implements the AI and lifecycle for **Omen** (NPC 15467), a special event-driven creature in Moonglade. The unit manages:
- Omen’s combat behavior (melee, spell reactions).
- Death handling: casting `SPELL_OMENS_MOONLIGHT`, setting a 15-minute global respawn cooldown, and despawning after 5 minutes.
- Summoning logic triggered by fireworks: tracks launches in static `OmenData`; summons Omen after 20 launches if the cooldown has expired.
- Game event integration: starts `GAME_EVENT_MINIONS_OF_OMEN` after 3 fireworks, stops it when Omen leaves the world.

No database tables are accessed. All persistent state resides in the static `OmenData` struct.

## Member-by-Member Behavior

### Lifecycle & Initialization
- **`boss_omenAI` (ctor)**: Calls `Reset()` and inherits from `ScriptedAI`.
- **`Reset`**: Empty; no state reset logic is implemented.
- **`OnRemoveFromWorld`**: Stops `GAME_EVENT_MINIONS_OF_OMEN` if active, preventing event persistence after despawn.
- **`JustDied`**: Casts `SPELL_OMENS_MOONLIGHT`, sets `OmenData.m_uiNextRespawn` to 15 minutes ahead, marks `m_bOmenAlive = false`, and schedules despawn in 5 minutes.

### Movement
- **`MovementInform`**: Handles point-motion callbacks:
  - `data == 1`: Moves to intermediate coordinate (7549.98, -2855.14, 456.968).
  - `data == 2`: Returns to `OmenHome`.
  - `data == 3`: Clears motion master and reinitializes default movement.

### Combat
- **`UpdateAI`**: Validates hostile target, updates spell timers if present, and performs melee attacks.
- **`SpellHit`**: If hit by `SPELL_ELUNES_CANDLE`, casts `SPELL_SELF_DAMAGE` on self.

### Summoning & Events
- **`OnFireworkLaunch`**: Static method called by `npcs_special/UpdateAI#10`.
  - Increments `OmenData.m_uiFireworksCount`.
  - Starts `GAME_EVENT_MINIONS_OF_OMEN` if count ≥ 3 and event inactive.
  - Returns early if count < 20.
  - If count ≥ 20 and respawn cooldown expired:
    - Summons Omen at `OmenSummon` coordinates.
    - Resets count, sets `m_bOmenAlive = true`.
    - Configures home position, wander distance, and random movement.
    - Schedules lambda events: play sounds at 800ms; move to initial point and reinit movement at 4000ms (if not in combat).
    - Logs outcome.

### Registration
- **`GetAI_boss_omen`**: Factory returning new `boss_omenAI`.
- **`AddSC_boss_omen`**: Registers script `"boss_omen"` with `ScriptMgr`.

## Cross-Unit Boundaries

- **`OnRemoveFromWorld`** → `GameEventMgr.Main/IsActiveEvent`, `GameEventMgr.Main/StopEvent`: Manages `GAME_EVENT_MINIONS_OF_OMEN`.
- **`MovementInform`** → `Creature.MotionMaster/Initialize`, `MovePoint`, `Clear`, `Unit.Main/GetMotionMaster`: Controls pathfinding.
- **`UpdateAI`** → `CreatureAI/DoMeleeAttackIfReady`, `UpdateSpellsList`, `Unit.Main/GetVictim`, `SelectHostileTarget`: Standard combat loop.
- **`SpellHit`** → `CreatureAI/DoCastSpellIfCan`: Casts self-damage spell.
- **`JustDied`** → `Creature.Main/DespawnOrUnsummon`, `CreatureAI/DoCastSpellIfCan`: Death effects and despawn.
- **`OnFireworkLaunch`** ← `npcs_special/UpdateAI#10`: Triggered by firework NPCs.
  - Calls `Creature.Main/SetDefaultMovementType`, `SetHomePosition`, `SetWanderDistance`, `Creature.MotionMaster/InitializeNewDefault`, `MovePoint`, `GameEventMgr.Main/IsActiveEvent`, `StartEvent`, `Log.Main/Out`, `Unit.Main/GetMotionMaster`, `IsInCombat`, `WorldObject.Object/PlayDistanceSound`, `SummonCreature#2`.

## Data Model

No database tables are touched. State is held in static `OmenData`:
- `m_uiFireworksCount`: Number of fireworks launched since last summon.
- `m_uiNextRespawn`: Earliest allowed respawn timestamp.
- `m_bOmenAlive`: Whether Omen is currently spawned.

## Notable Implementation Details

- **Static Global State**: `OmenData` is a file-scope static struct. Its state persists across script reloads unless manually cleared, potentially causing stale cooldowns or counts.
- **Empty Reset**: `Reset()` does nothing; spell timers or internal state are not cleared on respawn.
- **Hardcoded Coordinates**: `OmenSummon` and `OmenHome` are compile-time constants.
- **Lambda Events**: `OnFireworkLaunch` uses `AddLambdaEventAtOffset` for delayed sound/movement actions, relying on the creature’s event manager.

## Member Reference

- **`boss_omenAI`**: Constructor; calls `Reset()` and inherits from `ScriptedAI`.
- **`Reset`**: Empty method; no reset logic implemented.
- **`OnRemoveFromWorld`**: Stops `GAME_EVENT_MINIONS_OF_OMEN` if active.
- **`MovementInform`**: Handles point-motion callbacks for pathfinding segments.
- **`UpdateAI`**: Validates target, updates spells, performs melee attacks.
- **`SpellHit`**: Casts `SPELL_SELF_DAMAGE` if hit by `SPELL_ELUNES_CANDLE`.
- **`JustDied`**: Casts death spell, sets 15-min respawn cooldown, despawns in 5 min.
- **`OnFireworkLaunch`**: Static; tracks fireworks, starts game event, summons Omen after 20 launches if cooldown expired.
- **`GetAI_boss_omen`**: Factory function creating `boss_omenAI` instance.
- **`AddSC_boss_omen`**: Registers script `"boss_omen"` with the engine.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_omen

*Source:* boss_omen.cpp, boss_omen.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_omenAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset | method | — | — | — |
| OnRemoveFromWorld | method | GameEventMgr.Main/IsActiveEvent, GameEventMgr.Main/StopEvent | — | — |
| MovementInform | method | Creature.MotionMaster/Initialize, Creature.MotionMaster/MovePoint, MotionMaster/Clear, Unit.Main/GetMotionMaster | — | — |
| UpdateAI | method | CreatureAI/DoMeleeAttackIfReady, CreatureAI/UpdateSpellsList, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| SpellHit | method | CreatureAI/DoCastSpellIfCan | — | — |
| JustDied | method | Creature.Main/DespawnOrUnsummon, CreatureAI/DoCastSpellIfCan | — | — |
| OnFireworkLaunch | method | Creature.Main/SetDefaultMovementType, Creature.Main/SetHomePosition, Creature.Main/SetWanderDistance, Creature.MotionMaster/InitializeNewDefault, Creature.MotionMaster/MovePoint, GameEventMgr.Main/IsActiveEvent, GameEventMgr.Main/StartEvent, Log.Main/Out, Unit.Main/GetMotionMaster, Unit.Main/IsInCombat, WorldObject.Object/PlayDistanceSound, WorldObject.Object/SummonCreature#2 | npcs_special/UpdateAI#10 | — |
| GetAI_boss_omen | function | — | — | — |
| AddSC_boss_omen | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
