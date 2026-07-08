# CreatureEventAI

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# CreatureEventAI

## Purpose & Responsibilities

`CreatureEventAI` is a data-driven artificial intelligence system for `Creature` objects in the wowvmangos server. Unlike script-based AI systems that require custom C++ code for every unique NPC behavior, `CreatureEventAI` allows game designers to define complex behavioral sequences using configuration data (typically loaded from a database, though the loading mechanism is external to this unit).

The core responsibility of `CreatureEventAI` is to act as a **state machine engine**. It maintains a list of potential "events" (triggers) associated with a specific creature entry. During the game loop (`UpdateAI`) or upon specific lifecycle hooks (e.g., `JustDied`, `SpellHit`), the AI evaluates these events against the current state of the creature, its environment, and its targets. If an event's conditions are met, it executes a series of predefined actions (scripts).

Key features include:
*   **Event Types:** Supports over 30 distinct trigger types, including timers (in/out of combat), health/mana thresholds, spell hits, line-of-sight checks, summoning states, and group dynamics.
*   **Phasing:** Implements a simple phase system (`m_Phase`) allowing events to be gated by phase masks.
*   **Repeatability & Cooldowns:** Events can be configured to repeat with randomized cooldowns. Non-repeatable events disable themselves after firing.
*   **Randomization:** Actions can be chosen randomly from a pool, and events have a configurable chance to fail even if conditions are met.
*   **Integration:** It inherits from `BasicAI`, delegating standard melee combat and movement logic to the base class while overlaying the event-driven logic.

This unit is utilized by other specialized AIs such as `GuardEventAI` and `PetEventAI`, which inherit from or interact with `CreatureEventAI` to extend its functionality for specific creature types.

## Member-by-Member Behavior

### Initialization and Lifecycle Management

**`CreatureEventAI` (Constructor)**
Initializes the AI for a specific `Creature`. It retrieves the event configuration for the creature's entry from the global `CreatureEventAIMgr` (`sEventAIMgr`). It creates a local copy of the event list (`m_CreatureEventAIList`) to allow for runtime modifications (like disabling events) without affecting the global template.
*   It filters out events marked with `EFLAG_DEBUG_ONLY` in release builds.
*   It enables the `MoveInLosEvent` flag on the creature if any event requires Out-of-Combat Line-of-Sight checks (`EVENT_T_OOC_LOS`).
*   It immediately processes any `EVENT_T_SPAWNED` events.
*   It calls `Reset()` to initialize timers.

**`~CreatureEventAI` (Destructor)**
Clears the internal event list vector to free memory.

**`Permissible`**
A static factory method used by the AI manager to determine if this AI class is suitable for a creature. It returns `PERMIT_BASE_SPECIAL` only if the creature's configured AI name is explicitly `"EventAI"`. Otherwise, it rejects the assignment.

**`GetAIInformation`**
Provides debugging information via the chat handler. It sends a system message displaying the creature's current phase (`m_Phase`).

### Core Event Processing Logic

**`ProcessEvent`**
The central decision-making function. It takes an event holder (`pHolder`) and an optional invoker (`pActionInvoker`, e.g., the player who killed the creature or the unit that triggered a LOS event).
1.  **Pre-checks:** Returns false if the event is disabled, still on cooldown (`Time > 0`), blocked by the inverse phase mask, or if the creature is casting a non-melee spell (when `EFLAG_NOT_CASTING` is set).
2.  **Condition Validation:** Checks if a `condition_id` is satisfied using the `Conditions` system.
3.  **Type-Specific Evaluation:** A large `switch` statement evaluates the specific requirements for the event type:
    *   **Timers:** Checks combat state and updates the repeat timer.
    *   **HP/Mana/Target HP/Mana:** Calculates percentages and checks if they fall within configured min/max ranges.
    *   **Aggro/Kill/Death/Evade:** Mostly pass-throughs, relying on the caller context.
    *   **Spell Hits:** Validates spell IDs and schools.
    *   **Friendly Units:** Searches for friendly units meeting criteria (low HP, CC'd, missing buffs) using `Unit` helper methods.
    *   **Summons:** Verifies the summoned unit's entry ID.
    *   **Auras:** Checks stack amounts of specific spells on self or target.
    *   **Stealth/LOS:** Handled by specific update loops, but this function validates the final trigger.
4.  **Cooldown Management:** If the event is not repeatable, it disables itself. If repeatable, it calculates a new random cooldown using `UpdateRepeatTimer`.
5.  **Chance Roll:** Applies a random percentage chance (`event_chance`). If the roll fails, the event aborts.
6.  **Action Execution:**
    *   If `EFLAG_RANDOM_ACTION` is set, it picks one random valid action from the list.
    *   Otherwise, it iterates through all actions.
    *   It calls `ProcessAction` for each.
7.  **Result Handling:** If `EFLAG_CHECK_RESULT` is set and any action failed (returned true from `ProcessAction` indicating failure/script error), the event remains enabled and resets its timer, allowing it to retry.

**`ProcessAction`**
Executes a single script action. It resolves the target (defaulting to the victim if no invoker is provided) and calls `Map::ScriptCommandStartDirect` to run the script command associated with the action. It returns `true` if the script execution indicates a failure/abort condition.

### Game Loop and Timer Updates

**`UpdateAI`**
Called periodically by the game server.
1.  Updates hostile targets.
2.  Checks if the creature is alive.
3.  Calls `UpdateEventsOn_UpdateAI` to process time-based and state-based events.
4.  If in combat, delegates to `BasicAI` for spell updates and melee attacks.

**`UpdateEventsOn_UpdateAI`**
Manages the internal event update cycle. To optimize performance, it only processes events every `EVENT_UPDATE_TIME` (500ms).
1.  Accumulates time differences (`m_EventDiff`).
2.  Decrements active timers (`Time`) for events, skipping decrements if the event is blocked by the current phase mask.
3.  Triggers `ProcessEvent` for events whose timers have expired or for specific periodic checks (e.g., `EVENT_T_TIMER_OOC`, `EVENT_T_FRIENDLY_MISSING_BUFF`, `EVENT_T_RANGE`).
4.  Resets the update accumulator.

**`UpdateEventsOn_MoveInLineOfSight`**
Called when a unit enters the creature's line of sight. It specifically handles `EVENT_T_OOC_LOS` events. It checks distance, hostility status (Any/Hostile/Non-Hostile), and actual line-of-sight validity before triggering the event.

### Lifecycle Hooks (Overrides from BasicAI/CreatureAI)

These methods respond to specific creature state changes. They iterate through `m_CreatureEventAIList` to find matching event types and call `ProcessEvent`.

*   **`JustRespawned`**: Resets the AI, then triggers `EVENT_T_SPAWNED` events.
*   **`Reset`**: Re-enables all events and initializes Out-of-Combat timers.
*   **`JustReachedHome`**: Triggers `EVENT_T_REACHED_HOME` events, then resets.
*   **`EnterEvadeMode`**: Triggers `EVENT_T_EVADE` events.
*   **`OnCombatStop`**: Triggers `EVENT_T_LEAVE_COMBAT` events.
*   **`JustDied`**: Triggers `EVENT_T_DEATH` events, passes the killer as the invoker, and resets the phase to 0.
*   **`KilledUnit`**: Triggers `EVENT_T_KILL` events if the victim was a player.
*   **`JustSummoned`**: Triggers `EVENT_T_SUMMONED_UNIT` events.
*   **`SummonedCreatureJustDied`**: Triggers `EVENT_T_SUMMONED_JUST_DIED` events.
*   **`SummonedCreatureDespawn`**: Triggers `EVENT_T_SUMMONED_JUST_DESPAWN` events.
*   **`EnterCombat`**: Enables `EVENT_T_AGGRO` events, initializes In-Combat timers, and re-enables all other events (resetting their timers to 0).
*   **`MoveInLineOfSight`**: If not in combat, calls `UpdateEventsOn_MoveInLineOfSight`.
*   **`SpellHit`**: Triggers `EVENT_T_HIT_BY_SPELL` or `EVENT_T_HIT_BY_AURA` based on spell ID/school/aura type.
*   **`SpellHitTarget`**: Triggers `EVENT_T_SPELL_HIT_TARGET` when the creature's spell hits a target.
*   **`MovementInform`**: Triggers `EVENT_T_MOVEMENT_INFORM` when the creature reaches a waypoint or completes a movement type.
*   **`ReceiveEmote`**: Triggers `EVENT_T_RECEIVE_EMOTE` if the emote ID matches.
*   **`OnScriptEventHappened`**: Triggers `EVENT_T_SCRIPT` events based on external script event IDs and data.
*   **`GroupMemberJustDied`**: Triggers `EVENT_T_GROUP_MEMBER_DIED` if the dead unit matches the configured creature ID and leader status.
*   **`OnMoveInStealth`**: Triggers `EVENT_T_STEALTH_ALERT` if the creature alerts to a stealthed unit.

### Helper Structures

**`CreatureEventAIHolder`**
A wrapper struct that holds a `CreatureEventAI_Event` definition along with runtime state: `Time` (remaining cooldown) and `Enabled` (boolean).

**`UpdateRepeatTimer` (Method of CreatureEventAIHolder)**
Calculates the next cooldown duration. If `repeatMin` equals `repeatMax`, it uses that fixed value. If `repeatMax` > `repeatMin`, it picks a random value in between. If `repeatMax` < `repeatMin`, it logs an error, disables the event, and returns false.

## Cross-Unit Boundaries

*   **`CreatureEventAIMgr`**: The constructor calls `GetCreatureEventAIMap()` to retrieve the pre-loaded event configurations for the creature's entry. This decouples the AI logic from the data loading/storage layer.
*   **`BasicAI`**: `CreatureEventAI` inherits from `BasicAI`. It calls `BasicAI::JustRespawned`, `BasicAI::SummonedCreatureDespawn`, `BasicAI::MoveInLineOfSight`, `BasicAI::EnterEvadeMode`, `BasicAI::OnCombatStop`, `BasicAI::DoMeleeAttackIfReady`, `BasicAI::UpdateSpellsList`, `BasicAI::CanTriggerAlert`, and `BasicAI::TriggerAlertDirect`. This ensures standard creature behaviors (melee attacks, basic movement, alerting) function correctly alongside the event system.
*   **`Creature`**: The AI interacts heavily with the `Creature` object (`m_creature`) to get/set state (entry, AI name, move-in-LOS enablement, health, power, victim, combat status, evade mode).
*   **`Unit`**: Used to query state of the creature, its victim, and friendly units (HP, CC status, buffs, hostility, range, LOS).
*   **`Conditions`**: `ProcessEvent` calls `IsConditionSatisfied` to evaluate complex logical conditions attached to events.
*   **`Map`**: `ProcessAction` calls `ScriptCommandStartDirect` on the creature's map to execute script commands. `UpdateEventsOn_UpdateAI` uses `IsInMap` and `IsInRange`.
*   **`Log`**: Used for error reporting (invalid event types, misconfigured timers, missing event maps).
*   **`ChatHandler`**: `GetAIInformation` uses `PSendSysMessage` to output debug info.
*   **`SpellAuraHolder` / `SpellCaster` / `SpellEntry`**: Used to inspect spell effects, stacks, and schools during event evaluation.
*   **`shared_Util`**: Uses `urand` for random number generation in timers and action selection.

## Data Model

This unit does not directly access database tables. It relies on `CreatureEventAIMgr` to load event data into memory. The structure `CreatureEventAI_Event` mirrors the likely database schema for event definitions, containing fields like `event_id`, `creature_id`, `event_type`, `event_chance`, `event_flags`, and various parameter unions (`timer`, `percent_range`, `hit_by_spell`, etc.). No SQL queries are present in this source file.

## Notable Implementation Details

1.  **Phase Mask Logic**: The `event_inverse_phase_mask` is checked using bitwise AND. If `(mask & (1 << m_Phase))` is true, the event is **blocked**. This is an *inverse* mask, meaning bits set in the mask represent phases where the event should *not* fire.
2.  **Performance Optimization**: `UpdateEventsOn_UpdateAI` throttles event processing to every 500ms (`EVENT_UPDATE_TIME`). This prevents high CPU usage from checking dozens of events every frame. However, this means time-sensitive events (like exact HP thresholds) might be evaluated slightly late relative to the actual state change.
3.  **Action Failure Handling**: The `EFLAG_CHECK_RESULT` flag is critical for robust scripting. If an action fails (e.g., a spell cast fails because the target is invalid), and this flag is set, the event does *not* go on cooldown. It stays enabled and resets its timer, allowing the AI to retry the action in the next tick. Without this flag, a failed action would lock the event until its cooldown expires, potentially breaking scripted sequences.
4.  **Random Action Selection**: When `EFLAG_RANDOM_ACTION` is set, the code counts valid actions (non-null pointers), picks a random index, and then iterates to find the corresponding action. This skips null entries in the `action` array.
5.  **Debug-Only Events**: Events with `EFLAG_DEBUG_ONLY` are stripped out during construction in non-debug builds (`#ifndef _DEBUG`). This allows developers to test rare or complex event chains without cluttering production logic.
6.  **Timer Reset on Combat Entry**: When `EnterCombat` is called, all non-timer events are re-enabled and their `Time` is set to 0. This ensures that events like "Cast Spell X" can trigger immediately upon aggro if their conditions are met, rather than waiting for a previous cooldown to expire.
7.  **Inverse Phase Mask in Timer Decrement**: In `UpdateEventsOn_UpdateAI`, timers are *not* decremented if the event is blocked by the inverse phase mask. This preserves the cooldown across phase changes, preventing the event from firing immediately when the phase changes to one where it is allowed.

## Member Reference

**UpdateRepeatTimer**: Method of `CreatureEventAIHolder`. Calculates a random cooldown between `repeatMin` and `repeatMax`. Logs an error and disables the event if `repeatMax < repeatMin`.

**Permissible**: Static method. Returns `PERMIT_BASE_SPECIAL` if the creature's AI name is "EventAI", otherwise `PERMIT_BASE_NO`.

**GetAIInformation**: Sends a system message via `ChatHandler` displaying the current `m_Phase`.

**CreatureEventAI**: Constructor. Loads event data from `CreatureEventAIMgr`, filters debug events, enables LOS events if needed, processes spawned events, and calls `Reset`.

**ProcessEvent**: Core logic. Validates event conditions (phase, casting, cooldown, specific type checks), applies chance rolls, manages cooldowns/repeatability, and executes actions (randomly or sequentially). Handles result checking for retries.

**CreatureEventAIHolder**: Constructor. Initializes the holder with an event definition, zero time, and enabled state.

**~CreatureEventAI**: Destructor. Clears the event list vector.

**ProcessAction**: Executes a script command via `Map::ScriptCommandStartDirect`. Returns true if the script indicates failure.

**JustRespawned**: Calls `Reset`, then processes `EVENT_T_SPAWNED` events.

**Reset**: Re-enables all events and initializes Out-of-Combat timers.

**JustReachedHome**: Processes `EVENT_T_REACHED_HOME` events, then calls `Reset`.

**EnterEvadeMode**: Calls base `EnterEvadeMode`, then processes `EVENT_T_EVADE` events.

**OnCombatStop**: Calls base `OnCombatStop`, then processes `EVENT_T_LEAVE_COMBAT` events.

**JustDied**: Calls `Reset`, processes `EVENT_T_DEATH` events with the killer as invoker, and resets `m_Phase` to 0.

**KilledUnit**: Processes `EVENT_T_KILL` events if the victim is a player.

**JustSummoned**: Processes `EVENT_T_SUMMONED_UNIT` events.

**SummonedCreatureJustDied**: Processes `EVENT_T_SUMMONED_JUST_DIED` events.

**SummonedCreatureDespawn**: Calls base `SummonedCreatureDespawn`, then processes `EVENT_T_SUMMONED_JUST_DESPAWN` events.

**EnterCombat**: Enables `EVENT_T_AGGRO`, initializes In-Combat timers, re-enables all other events (resetting timers to 0), and resets update accumulators.

**MoveInLineOfSight**: If not in combat, calls `UpdateEventsOn_MoveInLineOfSight`. Then calls base `MoveInLineOfSight`.

**UpdateEventsOn_MoveInLineOfSight**: Iterates events, checking `EVENT_T_OOC_LOS` conditions (distance, hostility, LOS) and triggering them.

**SpellHit**: Checks `EVENT_T_HIT_BY_SPELL` and `EVENT_T_HIT_BY_AURA` conditions against the incoming spell.

**SpellHitTarget**: Checks `EVENT_T_SPELL_HIT_TARGET` conditions against the spell hitting the target.

**MovementInform**: Checks `EVENT_T_MOVEMENT_INFORM` conditions against the movement type and point ID.

**UpdateAI**: Updates hostile targets, checks alive status, calls `UpdateEventsOn_UpdateAI`, and delegates combat logic to `BasicAI`.

**UpdateEventsOn_UpdateAI**: Throttles event processing to 500ms intervals. Decrements timers (respecting phase masks) and triggers time/state-based events.

**ReceiveEmote**: Checks `EVENT_T_RECEIVE_EMOTE` conditions against the received emote ID.

**OnScriptEventHappened**: Checks `EVENT_T_SCRIPT` conditions against the external event ID and data.

**GroupMemberJustDied**: Checks `EVENT_T_GROUP_MEMBER_DIED` conditions against the dead unit's entry and leader status.

**OnMoveInStealth**: Checks if alert can be triggered, triggers alert via `BasicAI`, then processes `EVENT_T_STEALTH_ALERT` events.

---

<!-- machine-true, projected from graph.json -->

## Map — CreatureEventAI

*Source:* CreatureEventAI.cpp, CreatureEventAI.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| UpdateRepeatTimer | method | Log.Main/Out, Object/GetEntry, shared_Util/urand | — | — |
| Permissible | method | Creature.Main/GetAIName | — | — |
| GetAIInformation | method | ChatHandler.Chat/PSendSysMessage#2 | — | — |
| CreatureEventAI | ctor | BasicAI/BasicAI, Creature.Main/EnableMoveInLosEvent, Creature.Main/SetAI, CreatureEventAIMgr/GetCreatureEventAIMap, Log.Main/Out, Object/GetEntry | GuardEventAI/GuardEventAI, PetEventAI/PetEventAI, world_event_wareffort/GetAI_npc_aqwar_saurfang | — |
| ProcessEvent | method | Conditions/IsConditionSatisfied, Creature.Main/IsInEvadeMode, Log.Main/Out, Object/GetEntry, Object/GetTypeId, Object/IsPlayer, shared_Util/urand, SpellAuraHolder/GetStackAmount, SpellCaster/IsNonMeleeSpellCasted, Unit.Main/FindFriendlyUnitCC, Unit.Main/FindFriendlyUnitMissingBuff, Unit.Main/FindLowestHpFriendlyUnit, Unit.Main/GetHealth, Unit.Main/GetMaxHealth, Unit.Main/GetMaxPower, Unit.Main/GetPower, Unit.Main/GetSpellAuraHolder#2, Unit.Main/GetVictim, Unit.Main/HasUnitState, Unit.Main/IsInCombat, WorldObject.Object/GetMap | — | — |
| CreatureEventAIHolder | ctor | — | — | — |
| ~CreatureEventAI | dtor | — | — | — |
| ProcessAction | method | Map.Main/ScriptCommandStartDirect, Unit.Main/GetVictim, WorldObject.Object/GetMap | — | — |
| JustRespawned | method | BasicAI/JustRespawned | — | — |
| Reset | method | — | — | — |
| JustReachedHome | method | — | — | — |
| EnterEvadeMode | method | CreatureAI/EnterEvadeMode | — | — |
| OnCombatStop | method | CreatureAI/OnCombatStop | — | — |
| JustDied | method | — | — | — |
| KilledUnit | method | Object/GetTypeId | — | — |
| JustSummoned | method | — | — | — |
| SummonedCreatureJustDied | method | — | — | — |
| SummonedCreatureDespawn | method | BasicAI/SummonedCreatureDespawn | — | — |
| EnterCombat | method | — | — | — |
| MoveInLineOfSight | method | BasicAI/MoveInLineOfSight, Unit.Main/GetVictim | — | — |
| UpdateEventsOn_MoveInLineOfSight | method | Unit.Main/IsHostileTo, WorldObject.Object/IsWithinDistInMap, WorldObject.Object/IsWithinLOSInMap | GuardEventAI/MoveInLineOfSight, PetEventAI/MoveInLineOfSight | — |
| SpellHit | method | SpellDefines/GetSchoolMask, SpellEntry/HasAura | — | — |
| SpellHitTarget | method | SpellDefines/GetSchoolMask | — | — |
| MovementInform | method | — | PetEventAI/MovementInform | — |
| UpdateAI | method | CreatureAI/DoMeleeAttackIfReady, CreatureAI/UpdateSpellsList, Unit.Main/GetVictim, Unit.Main/IsAlive, Unit.Main/SelectHostileTarget | — | — |
| UpdateEventsOn_UpdateAI | method | Unit.Main/GetVictim, WorldObject.Object/IsInMap, WorldObject.Object/IsInRange | PetEventAI/UpdateAI | — |
| ReceiveEmote | method | — | — | — |
| OnScriptEventHappened | method | Unit.Main/ToUnit | — | — |
| GroupMemberJustDied | method | Object/GetEntry | — | — |
| OnMoveInStealth | method | CreatureAI/CanTriggerAlert, CreatureAI/TriggerAlertDirect | — | — |
