<!-- provenance: verbose -->
# undercity

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# undercity

## Purpose & Responsibilities

The `undercity` translation unit implements the combat AI for **Lady Sylvanas Windrunner** (`npc_lady_sylvanas_windrunner`). It defines `boss_sylvanasAI`, a `ScriptedAI` subclass that manages timed abilities (summoning skeletons, shooting arrows, fading) and movement logic. The unit also provides the registration hook `AddSC_undercity` to integrate this AI into the server's script manager.

## Member-by-Member Behavior

### Initialization & Lifecycle

**`boss_sylvanasAI`** (ctor)  
Constructs the AI instance for a `Creature`. It initializes the base `ScriptedAI` and immediately calls `Reset()` to set initial timer states.

**`Reset`**  
Resets all internal timers to their default cooldowns:
- `m_uiSummSkelTimer`: 25,000 ms
- `m_uiFadeTimer`: 50,000 ms
- `m_uiFadedTimer`: 0 ms
- `m_uiBlackArrowTimer`: 15,000 ms
- `m_uiMultiShotTimer`: 10,000 ms
- `m_uiShootTimer`: 10,000 ms

**`EnterCombat`**  
Plays sound ID 5886 via `WorldObject.Object/PlayDistanceSound` to signal the start of combat.

### Combat Logic

**`UpdateAI`**  
Executes every game tick (`uiDiff`). It manages five distinct abilities and general combat state:

1.  **Fade State Handling**: If `m_uiFadedTimer` is active, it decrements the timer. If time remains, it returns early, suppressing all other actions. If it expires, it clears the motion master (`MotionMaster/Clear`) and resets `m_uiFadedTimer` to 0.
2.  **Target Validation**: Exits early if no hostile target or victim exists (`Unit.Main/SelectHostileTarget`, `Unit.Main/GetVictim`).
3.  **Summon Skeletons**: If `m_uiSummSkelTimer` expires, casts `SPELL_SUMMON_SKEL` on self. Resets timer on success.
4.  **Fade**: If `m_uiFadeTimer` expires, casts `SPELL_FADE`. On success:
    - Orders the creature to follow its victim at 30.0f distance (`Creature.MotionMaster/MoveFollow`).
    - Resets `m_uiFadeTimer` to 50,000 ms.
    - Resets `m_uiBlackArrowTimer`, `m_uiMultiShotTimer`, and `m_uiShootTimer` to 0, ensuring these abilities trigger immediately after the fade ends.
    - Sets `m_uiFadedTimer` to 5,000 ms, initiating the suppression window.
5.  **Black Arrow**: If `m_uiBlackArrowTimer` expires, selects a random target (`Creature.Main/SelectAttackingTarget`) and casts `SPELL_BLACK_ARROW`. Resets timer on success.
6.  **Multi-Shot**: If `m_uiMultiShotTimer` expires, selects a random target and casts `SPELL_MULTI_SHOT`. Resets timer on success.
7.  **Shoot**: If `m_uiShootTimer` expires, casts `SPELL_SHOOT` on the current victim. Resets timer on success.
8.  **Melee & Evasion**: Calls `CreatureAI/DoMeleeAttackIfReady` for physical attacks and `ScriptedAI/EnterEvadeIfOutOfCombatArea` to prevent wandering.

### Registration

**`GetAI_boss_sylvanas`**  
Factory function returning a new `boss_sylvanasAI` instance for a given `Creature`.

**`AddSC_undercity`**  
Creates a `Script` object named `"npc_lady_sylvanas_windrunner"`, assigns `GetAI_boss_sylvanas` as its AI generator, and registers it via `ScriptMgr/RegisterSelf`. Called by `ScriptLoader/AddScripts`.

## Cross-Unit Boundaries

- **`ScriptedAI/ScriptedAI`**: Base class providing timer utilities and evasion logic.
- **`WorldObject.Object/PlayDistanceSound`**: Plays combat start audio.
- **`Creature.Main/SelectAttackingTarget`**: Selects random targets for Black Arrow and Multi-Shot.
- **`Creature.MotionMaster/MoveFollow`**: Moves Sylvanas toward her victim during Fade.
- **`CreatureAI/DoCastSpellIfCan`**: Validates and executes spell casts.
- **`CreatureAI/DoMeleeAttackIfReady`**: Executes melee attacks.
- **`MotionMaster/Clear`**: Stops movement when Fade ends.
- **`ScriptedAI/EnterEvadeIfOutOfCombatArea`**: Forces evade if out of bounds.
- **`Unit.Main/GetMotionMaster`**, **`GetVictim`**, **`SelectHostileTarget`**: Access combat state and motion control.
- **`WorldObject.Object/GetAngle`**: Calculates angle to victim for movement.
- **`Script/Script`**, **`ScriptMgr/RegisterSelf`**: Registers the script globally.
- **`ScriptLoader/AddScripts`**: Invokes `AddSC_undercity` at startup.

## Data Model

This unit does not interact with any database tables. All logic relies on hardcoded spell IDs and runtime timers.

## Notable Implementation Details

- **Burst Damage Post-Fade**: Casting `Fade` resets `m_uiBlackArrowTimer`, `m_uiMultiShotTimer`, and `m_uiShootTimer` to 0. Since `UpdateAI` returns early during the 5-second `m_uiFadedTimer`, these abilities are suppressed. Immediately upon `m_uiFadedTimer` expiring, the next tick sees these timers at 0, causing Black Arrow, Multi-Shot, and Shoot to cast simultaneously, creating a significant damage spike.
- **Timer Retry Logic**: Timers are only reset if `DoCastSpellIfCan` returns `CAST_OK`. If a cast fails (e.g., out of range), the timer remains expired, allowing the ability to retry on the next tick.

## Member Reference

**`boss_sylvanasAI`** (ctor): Initializes the AI instance, calling the base `ScriptedAI` constructor and `Reset()` to set initial timer values.

**`Reset`** (method): Resets all internal timers (`m_uiSummSkelTimer`, `m_uiFadeTimer`, `m_uiFadedTimer`, `m_uiBlackArrowTimer`, `m_uiMultiShotTimer`, `m_uiShootTimer`) to their default cooldowns.

**`EnterCombat`** (method): Plays sound ID 5886 via `WorldObject.Object/PlayDistanceSound` when combat begins.

**`UpdateAI`** (method): The main AI loop. Manages the `Fade` state (suppressing other actions for 5 seconds), checks for valid targets, and processes timed abilities: Summon Skeletons (25s), Fade (50s, resets other attack timers), Black Arrow (15s, random target), Multi-Shot (10s, random target), and Shoot (10s, victim). Also handles melee attacks and evasion checks.

**`GetAI_boss_sylvanas`** (function): Factory function that creates and returns a new `boss_sylvanasAI` instance for a given `Creature`.

**`AddSC_undercity`** (function): Registers the `npc_lady_sylvanas_windrunner` script with the `ScriptMgr` by setting its name and AI generator function, then calling `RegisterSelf`. Called by `ScriptLoader/AddScripts`.

---

<!-- machine-true, projected from graph.json -->

## Map — undercity

*Source:* undercity.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_sylvanasAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset | method | — | — | — |
| EnterCombat | method | WorldObject.Object/PlayDistanceSound | — | — |
| UpdateAI | method | Creature.Main/SelectAttackingTarget, Creature.MotionMaster/MoveFollow, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, MotionMaster/Clear, ScriptedAI/EnterEvadeIfOutOfCombatArea, Unit.Main/GetMotionMaster, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget, WorldObject.Object/GetAngle | — | — |
| GetAI_boss_sylvanas | function | — | — | — |
| AddSC_undercity | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
