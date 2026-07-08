<!-- provenance: verbose -->
# boss_magistrate_barthilas

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# boss_magistrate_barthilas

## Purpose & Responsibilities

This unit implements the AI for **Magistrate Barthilas**, a boss in the Stratholme instance. It manages the creature's combat rotation, visual model transitions, and spawning state. Key behaviors include:
1.  **Combat Rotation:** Timed casting of `Draining Blow`, `Crowd Pummel`, and `Mighty Blow` on the current victim, alongside a self-buffing mechanic (`Furious Anger`) that stacks up to 25 times.
2.  **Visual States:** Switches the model between `MODEL_NORMAL` (10433, alive) and `MODEL_HUMAN` (3637, dead/reset).
3.  **Spawn Handling:** Removes the `UNIT_FLAG_SPAWNING` flag when a player approaches within 10 yards, making the creature visible/selectable.

## Member-by-Member Behavior

### Initialization and Lifecycle

**`boss_magistrate_barthilasAI` (Constructor)**
Initializes the AI by casting the creature's instance data to `ScriptedInstance*` and storing it in `m_pInstance`. It immediately calls `Reset()` to initialize timers and set the initial display ID.

**`Reset`**
Resets all internal timers to their default intervals and clears `AngerCount` to zero. It sets the creature's display ID to `MODEL_NORMAL` if alive, or `MODEL_HUMAN` if dead.

**`JustDied`**
Triggered on death. Unconditionally sets the creature's display ID to `MODEL_HUMAN`.

### Perception and Spawning

**`MoveInLineOfSight`**
Checks if a unit entering line of sight is a player within 10 yards. If the creature has the `UNIT_FLAG_SPAWNING` flag, it removes it. Delegates to `ScriptedAI::MoveInLineOfSight` for standard threat handling.

### Combat Loop

**`UpdateAI`**
Executes the main combat loop. Returns early if no hostile target exists. Processes four independent timers:
1.  **Furious Anger:** Resets to 4000ms. Increments `AngerCount`; if count > 25, returns early without casting. Otherwise, casts `SPELL_FURIOUS_ANGER` on self.
2.  **Draining Blow:** Resets to 15000ms. Casts `SPELL_DRAININGBLOW` on victim via `DoCastSpellIfCan`.
3.  **Crowd Pummel:** Resets to 15000ms. Casts `SPELL_CROWDPUMMEL` on victim via `DoCastSpellIfCan`.
4.  **Mighty Blow:** Resets to 20000ms. Casts `SPELL_MIGHTYBLOW` on victim via `DoCastSpellIfCan`.
Finally, calls `DoMeleeAttackIfReady()`.

### Registration

**`GetAI_boss_magistrate_barthilas`**
Factory function returning a new `boss_magistrate_barthilasAI` instance.

**`AddSC_boss_magistrate_barthilas`**
Registers the script with the engine under the name `"boss_magistrate_barthilas"` via `ScriptMgr`.

## Cross-Unit Boundaries

*   **`ScriptedAI` / `ScriptedInstance`:** Constructor retrieves instance data from `WorldObject::GetInstanceData`.
*   **`Unit.Main` / `Object`:** Queries state (`IsAlive`, `GetTypeId`, `HasFlag`, `IsWithinDistInMap`, `GetVictim`, `SelectHostileTarget`) and mutates state (`SetDisplayId`, `RemoveFlag`).
*   **`CreatureAI`:** Uses `DoCastSpellIfCan` and `DoMeleeAttackIfReady` for safe spell/melee execution.
*   **`SpellCaster`:** Directly calls `CastSpell` for `Furious Anger`, bypassing `DoCastSpellIfCan` checks (e.g., silence/stun).
*   **`ScriptMgr` / `ScriptLoader`:** `AddSC_boss_magistrate_barthilas` registers the script via `Script::RegisterSelf`.

## Data Model

This unit does not interact with any database tables. All state is held in memory.

## Notable Implementation Details

1.  **Asymmetric Timers:** Initial timer values in `Reset()` differ from reset values in `UpdateAI()` (e.g., `MightyBlow` starts at 8s but resets to 20s).
2.  **Furious Anger Cap:** `AngerCount` caps at 25. When capped, the timer still ticks and resets, but the spell is not cast, resulting in repeated empty checks.
3.  **Direct Cast:** `Furious Anger` uses `CastSpell` directly, potentially allowing it to bypass interrupt checks that `DoCastSpellIfCan` would enforce.

## Member Reference

**`boss_magistrate_barthilasAI`**
Constructor initializing instance data and calling `Reset()`.

**`Reset`**
Resets timers, clears `AngerCount`, and sets display ID based on alive/dead state.

**`MoveInLineOfSight`**
Removes `UNIT_FLAG_SPAWNING` if a player is within 10 yards; delegates to base class.

**`JustDied`**
Sets display ID to `MODEL_HUMAN`.

**`UpdateAI`**
Manages combat timers for `Furious Anger` (capped at 25 stacks), `Draining Blow`, `Crowd Pummel`, and `Mighty Blow`; executes melee attacks.

**`GetAI_boss_magistrate_barthilas`**
Factory function creating `boss_magistrate_barthilasAI`.

**`AddSC_boss_magistrate_barthilas`**
Registers the script with the engine.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_magistrate_barthilas

*Source:* boss_magistrate_barthilas.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_magistrate_barthilasAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset | method | Unit.Main/IsAlive, Unit.Main/SetDisplayId | — | — |
| MoveInLineOfSight | method | BasicAI/MoveInLineOfSight, Object/GetTypeId, Object/HasFlag, WorldObject.Object/IsWithinDistInMap, WorldObject.Object/RemoveFlag | — | — |
| JustDied | method | Unit.Main/SetDisplayId | — | — |
| UpdateAI | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, SpellCaster/CastSpell#2, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_boss_magistrate_barthilas | function | — | — | — |
| AddSC_boss_magistrate_barthilas | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
