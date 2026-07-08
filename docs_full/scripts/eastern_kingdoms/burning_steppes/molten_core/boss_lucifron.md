<!-- provenance: verbose -->
# boss_lucifron

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`boss_lucifron.cpp` implements the AI for **Lucifron**, a boss in the Molten Core instance. The `boss_lucifronAI` class manages combat behavior using an event-driven timer system (`EventMap`) to rotate three spells: *Impending Doom*, *Curse*, and *Shadow Shock*. It reports encounter progress (`NOT_STARTED`, `IN_PROGRESS`, `DONE`) to the instance manager via `ScriptedInstance`.

## Member-by-Member Behavior

### Initialization and Lifecycle

**`boss_lucifronAI`**
Constructs the AI, retrieves `ScriptedInstance` from the creature, and immediately calls `Reset()` to initialize timers.

**`Reset`**
Wipes existing events to prevent stale timers from firing. Schedules initial casts:
- `EventImpendingDoom`: 10s delay.
- `EventCurse`: 20s delay.
- `EventShadowShock`: 6s delay.
Sets instance data to `NOT_STARTED` if the creature is alive.

**`Aggro`**
Sets instance data to `IN_PROGRESS` and marks the creature as in combat with the zone.

**`JustDied`**
Sets instance data to `DONE`.

### Combat Loop

**`UpdateAI`**
Returns early if no hostile target or victim exists. Updates `EventMap` timers and executes matured events:
- **`EventImpendingDoom`**: Casts `SpellImpendingDoom` (19702). On success, repeats in 20s; on failure, retries in 100ms.
- **`EventCurse`**: Casts `SpellCurse` (19703). On success, repeats in 15s; on failure, retries in 100ms.
- **`EventShadowShock`**: Selects a random target and casts `SpellShadowShock` (19460). On success, repeats in 6s. **Note:** Unlike other spells, this event does *not* reschedule on failure, meaning it stops firing until reset if casting fails.
Finally, calls `DoMeleeAttackIfReady()`.

### Registration

**`GetAI_boss_lucifron`**
Factory function creating a new `boss_lucifronAI` instance.

**`AddSC_boss_lucifron`**
Registers the script with `ScriptMgr` under the name `"boss_lucifron"`.

## Cross-Unit Boundaries

- **`ScriptedAI`**: Base class providing `DoCastSpellIfCan`, `DoMeleeAttackIfReady`, and target selection helpers.
- **`EventMap`**: Manages timed events (`Reset`, `ScheduleEvent`, `Update`, `ExecuteEvent`, `Repeat`).
- **`ScriptedInstance`**: Receives encounter state updates via `SetData`.
- **`Creature`/`Unit`**: Provides creature state (`IsAlive`, `GetVictim`, `SelectHostileTarget`) and actions (`SetInCombatWithZone`, `SelectAttackingTarget`).
- **`ScriptMgr`**: Registers the script via `AddSC_boss_lucifron`.

## Data Model

No database tables are accessed. All state is managed in memory via `ScriptedInstance` and `EventMap`.

## Notable Implementation Details

1. **Event-Based Timers**: Uses `EventMap` for cleaner logic than manual delta-time accumulation.
2. **Retry Logic**: `ImpendingDoom` and `Curse` retry in 100ms on failure to handle temporary casting restrictions.
3. **Shadow Shock Gap**: `EventShadowShock` lacks a retry mechanism. If casting fails, the event is consumed and will not fire again until `Reset()`.
4. **Instance Safety**: Null-checks `m_Instance` before updating state to prevent crashes if instance data is missing.

## Member Reference

**`boss_lucifronAI`**
Constructor initializing the AI, retrieving instance data, and calling `Reset()`.

**`Reset`**
Clears events, schedules initial spell casts, and sets instance state to `NOT_STARTED`.

**`Aggro`**
Sets instance state to `IN_PROGRESS` and marks the creature as in combat with the zone.

**`JustDied`**
Sets instance state to `DONE`.

**`UpdateAI`**
Main loop validating targets, processing `EventMap` events for spell casts with specific retry/repeat logic, and handling melee attacks.

**`GetAI_boss_lucifron`**
Factory function returning a new `boss_lucifronAI` instance.

**`AddSC_boss_lucifron`**
Registers the script with `ScriptMgr` under the name "boss_lucifron".

---

<!-- machine-true, projected from graph.json -->

## Map — boss_lucifron

*Source:* boss_lucifron.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_lucifronAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset | method | EventMap/Reset, EventMap/ScheduleEvent#2, InstanceData/SetData, Unit.Main/IsAlive | — | — |
| Aggro | method | Creature.Main/SetInCombatWithZone, InstanceData/SetData | — | — |
| JustDied | method | InstanceData/SetData | — | — |
| UpdateAI | method | Creature.Main/SelectAttackingTarget, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, EventMap/ExecuteEvent, EventMap/Repeat, EventMap/Update, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_boss_lucifron | function | — | — | — |
| AddSC_boss_lucifron | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
