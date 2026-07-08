<!-- provenance: verbose -->
# boss_overlord_wyrmthalak

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`boss_overlord_wyrmthalak.cpp` implements the AI for **Overlord Wyrmthalak**, a boss in Blackrock Spire. The `boss_overlordwyrmthalakAI` class manages combat mechanics: timed spells (Blast Wave, Shout, Cleave, Knock Away), melee attacks, and a phase transition at <51% health that summons two adds (`NPC_SPIRESTONE_WARLORD`, `NPC_SMOLDERTHORN_BERSERKER`). It enforces anti-exploit measures to prevent pet pulls through walls or into Upper Blackrock Spire (UBRS).

## Member-by-Member Behavior

### Initialization and State

**`boss_overlordwyrmthalakAI`**
Constructs the AI, inheriting from `ScriptedAI`, and immediately calls `Reset()`.

**`Reset`**
Initializes timers: Blast Wave (20s), Shout (2s), Cleave (6s), Knock Away (12s), Leash Check (5s). Clears `m_bSummoned` and `m_bPulledByPet`.

### Combat and Summoning

**`EnterCombat`**
Detects invalid pet pulls: if the attacker (`pUnit`) has an owner lacking Line-of-Sight to the boss (`WorldObject.Object/IsWithinLOSInMap`), it sets `m_bPulledByPet = true`. Delegates to `ScriptedAI::EnterCombat`.

**`JustSummoned`**
For summoned `NPC_SPIRESTONE_WARLORD` or `NPC_SMOLDERTHORN_BERSERKER`, selects a random hostile target via `Creature.Main/SelectAttackingTarget` (falling back to `Unit.Main/GetVictim`) and orders the add to attack via `CreatureAI/AttackStart`.

**`LeashIfOutOfCombatArea`**
Runs every 3.5s. Evades (`ScriptedAI/EnterEvadeMode`) if `m_bPulledByPet` is true or the boss’s Z-position (`WorldObject.Object/GetPositionZ`) exceeds 100.0f (UBRS boundary).

**`UpdateAI`**
1. Returns if no hostile target (`Unit.Main/SelectHostileTarget`, `Unit.Main/GetVictim`).
2. Calls `LeashIfOutOfCombatArea`.
3. Casts spells on timers: Blast Wave (self, 20s), Shout (self, 10s after initial 2s), Cleave (victim, 7s), Knock Away (self, 14s) via `CreatureAI/DoCastSpellIfCan`.
4. If `!m_bSummoned` and health < 51% (`Unit.Main/GetHealthPercent`), summons both adds at `afLocations` via `WorldObject.Object/SummonCreature#2` and sets `m_bSummoned = true`.
5. Executes melee via `CreatureAI/DoMeleeAttackIfReady`.

### Registration

**`GetAI_boss_overlordwyrmthalak`**
Factory function returning a new `boss_overlordwyrmthalakAI` instance.

**`AddSC_boss_overlordwyrmthalak`**
Creates a `Script` named `"boss_overlord_wyrmthalak"`, assigns `GetAI_boss_overlordwyrmthalak`, and registers it via `ScriptMgr/RegisterSelf`. Called by `ScriptLoader/AddScripts`.

## Cross-Unit Boundaries

*   **`ScriptedAI`**: Base class. `boss_overlordwyrmthalakAI` overrides `Reset`, `EnterCombat`, `UpdateAI`, `JustSummoned`; calls `ScriptedAI::EnterCombat` and `ScriptedAI/EnterEvadeMode`.
*   **`Creature`/`Unit`/`WorldObject`**: Engine entities.
    *   `Creature.Main/AI`, `CreatureAI/AttackStart`, `Creature.Main/SelectAttackingTarget`: Used in `JustSummoned` to control adds.
    *   `Unit.Main/GetVictim`, `Unit.Main/SelectHostileTarget`, `Unit.Main/GetHealthPercent`, `Unit.Main/GetOwner`: State queries in `UpdateAI` and `EnterCombat`.
    *   `WorldObject.Object/IsWithinLOSInMap`: LOS check in `EnterCombat`.
    *   `WorldObject.Object/GetPositionZ`: Z-boundary check in `LeashIfOutOfCombatArea`.
    *   `WorldObject.Object/SummonCreature#2`: Spawns adds in `UpdateAI`.
*   **`Script`/`ScriptMgr`**: Registration in `AddSC_boss_overlordwyrmthalak`.
*   **`ScriptLoader`**: Invokes `AddSC_boss_overlordwyrmthalak`.

## Data Model

No database tables are accessed. All data is hardcoded.

## Notable Implementation Details

*   **UBRS Boundary**: `LeashIfOutOfCombatArea` uses a hardcoded Z > 100.0f threshold to detect pulls into UBRS.
*   **Pet Pull Exploit**: `EnterCombat` flags pulls where the pet owner lacks LOS. `LeashIfOutOfCombatArea` forces an evade if flagged, resetting the boss.
*   **Summon Trigger**: Adds spawn once when health drops below 51%, tracked by `m_bSummoned`.

## Member Reference

**boss_overlordwyrmthalakAI**
Constructor initializing the AI object and calling `Reset()`.

**Reset**
Resets all timers and boolean flags to their default pre-combat values.

**JustSummoned**
Validates summoned adds and commands them to attack a random hostile target or the boss's current victim.

**EnterCombat**
Checks for invalid pet pulls via LOS validation and sets `m_bPulledByPet` if necessary, then delegates to the base class.

**LeashIfOutOfCombatArea**
Periodically checks if the boss should evade due to invalid pet pulls or exceeding the Z-boundary of 100.0f.

**UpdateAI**
Manages spell rotations, melee attacks, and the 50% health summon phase, while enforcing leash constraints.

**GetAI_boss_overlordwyrmthalak**
Factory function creating a new `boss_overlordwyrmthalakAI` instance.

**AddSC_boss_overlordwyrmthalak**
Registers the script with `ScriptMgr` using the name `"boss_overlord_wyrmthalak"` and the `GetAI` factory function.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_overlord_wyrmthalak

*Source:* boss_overlord_wyrmthalak.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_overlordwyrmthalakAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset | method | — | — | — |
| JustSummoned | method | Creature.Main/AI, Creature.Main/SelectAttackingTarget, CreatureAI/AttackStart, Object/GetEntry, Unit.Main/GetVictim | — | — |
| EnterCombat | method | ScriptedAI/EnterCombat, Unit.Main/GetOwner, WorldObject.Object/IsWithinLOSInMap | — | — |
| LeashIfOutOfCombatArea | method | ScriptedAI/EnterEvadeMode, WorldObject.Object/GetPositionZ | — | — |
| UpdateAI | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, Unit.Main/GetHealthPercent, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget, WorldObject.Object/SummonCreature#2 | — | — |
| GetAI_boss_overlordwyrmthalak | function | — | — | — |
| AddSC_boss_overlordwyrmthalak | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
