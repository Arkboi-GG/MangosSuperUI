# EventMap

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# EventMap

**EventMap** is a deterministic, in-memory event scheduler used throughout the `wowvmangos` codebase to manage timed behaviors for NPCs, bosses, and scripted encounters. It functions as a priority queue keyed by absolute time, allowing scripts to schedule, delay, cancel, and repeat discrete actions (identified by integer IDs) without relying on external timers or database persistence.

The class is heavily utilized by complex boss AI implementations (e.g., `boss_sapphiron`, `boss_kelthuzad`, `boss_heigan`) and instance scripts (`instance_naxxramas.Main`, `scourge_invasion`). It supports **phases** (allowing events to be active only during specific encounter stages) and **groups** (allowing bulk manipulation of related events, such as delaying or canceling all spells in a specific chain).

### Core Architecture & Data Model

The internal state is maintained via a `std::multimap<uint32, uint32>` named `_eventMap`.
*   **Key:** Absolute time in milliseconds when the event should trigger.
*   **Value:** A packed `uint32` containing metadata about the event.

The value structure follows the bit pattern `0xPPGGEEEE`:
*   **Bits 0–15 (`EEEE`):** The `eventId` returned to the caller.
*   **Bits 16–23 (`GG`):** The `group` ID (1–8). Used for batch operations like `DelayEvents` or `CancelEventGroup`.
*   **Bits 24–31 (`PP`):** The `phase` mask (1–8). Used to determine if an event is valid in the current encounter phase.

This packing allows efficient sorting by time while retaining filtering capabilities without additional memory overhead per event.

### Behavior & Responsibilities

#### Scheduling and Execution
*   **Scheduling:** Events are added via `ScheduleEvent`. The class provides overloads for fixed delays, random ranges (`minTime`/`maxTime`), and `std::chrono::Milliseconds` durations. When an event is scheduled, its ID, group, and phase are packed into the value, and the key is set to `_time + delay`.
*   **Execution:** `ExecuteEvent()` is the primary consumer interface. It checks the earliest event in the map. If the event's time has passed (`itr->first <= _time`), it validates the phase. If the event's phase is incompatible with the current `_phase` mask, the event is silently discarded. Otherwise, the event ID is extracted, the entry is removed, and the ID is returned. Crucially, `ExecuteEvent()` loops internally, returning only the *first* valid event due. Callers typically call this repeatedly in their `UpdateAI` loops until it returns 0.
*   **Repetition:** The `Repeat` methods allow a script to re-schedule the *most recently executed event* (`_lastEvent`) with a new delay. This is a convenience for recurring abilities (e.g., periodic breath attacks) without needing to store the event ID explicitly in the AI script.

#### Phase Management
Phases allow scripts to define events that only occur during specific parts of an encounter.
*   `SetPhase(phase)` sets the active phase mask. Note that passing `0` clears all phases. Passing 1–8 sets a single phase bit.
*   `AddPhase` and `RemovePhase` toggle individual phase bits.
*   During `ExecuteEvent`, if an event has a phase restriction (bits 24–31 are non-zero) and that phase is not currently active in `_phase`, the event is erased and ignored.

#### Group Management
Groups allow logical clustering of events.
*   `DelayEvents(delay, group)` shifts the absolute time of all events belonging to a specific group forward by `delay`. This is useful for "stun" mechanics or channeling effects that pause a boss's rotation.
*   `CancelEventGroup(group)` removes all events in a group.

#### Global Cooldown (GCD) Handling
`CancelEventsByGCD(gcd)` is a specialized cancellation method. It interprets the `gcd` parameter as a group index (shifted by 16 bits) and cancels events matching that GCD group. This is likely used to enforce spell casting limits or interrupt chains.

### Cross-Unit Collaboration

EventMap is a passive utility; it does not initiate actions. It is driven entirely by external AI and script units.

*   **Initialization & Reset:** Units like `boss_sapphiron/Reset` and `instance_naxxramas.Main/Initialize` call `Reset()` to clear state when an encounter starts or ends.
*   **Scheduling:** Almost all boss AI units (e.g., `boss_gluth/Aggro`, `boss_heigan/EventStartDance`) call `ScheduleEvent` variants to populate the map.
*   **Execution Loop:** Boss AI `UpdateAI` methods (e.g., `boss_sapphiron/UpdateAI`, `scourge_invasion/UpdateAI`) call `Update(time)` to advance the internal clock, then call `ExecuteEvent()` to retrieve pending actions.
*   **Dynamic Adjustment:**
    *   `boss_garr/ScheduleCombatEvents` uses `RescheduleEvent` to adjust timing dynamically.
    *   `fireworks_show/UpdateAI` uses `CancelEvent` to stop specific visual effects.
    *   `boss_heigan/EventDanceEnd` uses `GetTimeUntilEvent` to check timing constraints before making decisions.

### Notable Implementation Details

1.  **Silent Phase Filtering:** In `ExecuteEvent`, if an event is out of phase, it is **erased** from the map. It is not rescheduled or delayed. This implies that phase-restricted events are "one-shot" unless the script explicitly re-schedules them. If a boss transitions phases rapidly, events scheduled for the old phase are permanently lost.
2.  **No Negative Time Clamping in Delay:** The `DelayEvents(uint32 delay)` overload (which delays *all* events) adjusts `_time` backwards (`_time -= delay`). It clamps `_time` to 0 if `delay >= _time`. However, the group-specific `DelayEvents(uint32 delay, uint32 group)` adds `delay` to the event keys. There is no explicit check to prevent event keys from becoming negative or wrapping, though `uint32` overflow would wrap around. Given typical encounter durations, this is unlikely to be an issue, but it is a theoretical edge case.
3.  **`CancelEventsByGCD` Logic:** The implementation `gcd = (1 << (gcd + 16))` suggests that the `gcd` parameter passed by callers is expected to be a small integer (likely 0–7), which is then mapped to the group bits (16–23). This is distinct from the standard `group` parameter in `ScheduleEvent`, which uses `1 << (group + 15)`. This offset difference (15 vs 16) is critical: **GCD groups and standard groups occupy different bit ranges.** Standard groups use bits 16–23 (offset 15, so group 1 is bit 16). Wait, let's verify:
    *   `ScheduleEvent`: `group <= 8` -> `eventId |= (1 << (group + 15))`. Group 1 -> Bit 16. Group 8 -> Bit 23.
    *   `CancelEventsByGCD`: `gcd = (1 << (gcd + 16))`. If `gcd` is 0, it checks Bit 16. If `gcd` is 7, it checks Bit 23.
    *   Therefore, `CancelEventsByGCD(0)` cancels events in Group 1. `CancelEventsByGCD(7)` cancels events in Group 8. The parameter naming is misleading; it acts on the same bit range as groups but uses a 0-based index instead of 1-based.
4.  **`Repeat` Uses `_lastEvent`:** The `Repeat` methods rely on `_lastEvent`, which is only updated in `ExecuteEvent`. If `Repeat` is called without a preceding successful `ExecuteEvent` (or if `_lastEvent` was manually modified), it will schedule garbage data. Scripts must ensure `ExecuteEvent` ran successfully before calling `Repeat`.
5.  **No Thread Safety:** `EventMap` is not thread-safe. It assumes single-threaded access, consistent with the typical game server tick model where AI updates are serialized per entity.

### Data Model

This unit does not interact with any database tables. All state is transient and held in memory.

## Member Reference

**Reset**  
Clears the internal event map, resets the internal timer `_time` to 0, and clears the phase mask `_phase`. Called by boss and instance scripts during initialization or encounter reset.

**SetPhase**  
Sets the active phase mask. If `phase` is 0, `_phase` is cleared. If `phase` is 1–8, `_phase` is set to a single bit corresponding to that phase (e.g., phase 1 sets bit 0). This replaces any previously set phases.

**ScheduleEvent#3**  
The core scheduling method. Takes an `eventId`, absolute `time` offset, optional `group` (1–8), and optional `phase` (1–8). Packs the group and phase into the high bits of the event ID, calculates the absolute trigger time (`_time + time`), and inserts the pair into `_eventMap`.

**EventMap**  
Constructor. Initializes `_time`, `_phase`, and `_lastEvent` to 0.

**ExecuteEvent**  
Retrieves and removes the next due event. Iterates through the map starting from the earliest time. If the earliest event is in the future, returns 0. If the event is due but its phase mask conflicts with the current `_phase`, it erases the event and continues. If valid, it extracts the low 16 bits as the `eventId`, stores the full packed value in `_lastEvent`, erases the entry, and returns the `eventId`. Loops until a valid event is found or the map is empty.

**Update**  
Advances the internal timer `_time` by the given `time` amount (in ms). This simulates the passage of time in the game world.

**GetTimer**  
Returns the current value of the internal timer `_time`.

**DelayEvents#4**  
Delays all events belonging to a specific `group` by adding `delay` to their absolute trigger times. Iterates through the map, identifies events with the matching group bit, moves them to a temporary container with adjusted times, erases them from the main map, and re-inserts them.

**GetPhaseMask**  
Returns the current `_phase` bitmask.

**Empty**  
Returns true if `_eventMap` contains no entries.

**CancelEvent**  
Removes all events from the map that match the given `eventId` (low 16 bits). Iterates through the entire map to find matches.

**AddPhase**  
Activates a specific phase bit in `_phase` using bitwise OR. Allows multiple phases to be active simultaneously.

**CancelEventGroup**  
Removes all events from the map that belong to the specified `group`. Checks the group bits (16–23) of each event.

**RemovePhase**  
Deactivates a specific phase bit in `_phase` using bitwise AND with the inverse mask.

**CancelEventsByGCD**  
Cancels events based on a "Global Cooldown" group index. Interprets the `gcd` parameter as a 0-based index, maps it to bit position `gcd + 16`, and cancels any event with that bit set. Effectively cancels events in standard groups 1–8 (mapped from gcd 0–7).

**ScheduleEvent#2**  
Overload of `ScheduleEvent` that accepts `std::chrono::Milliseconds` for the delay. Converts to `uint32` and delegates to the core `ScheduleEvent#3`.

**GetNextEventTime#2**  
Returns the absolute time of the very next event in the map (the key of the first element). Returns 0 if the map is empty.

**ScheduleEvent**  
Overload of `ScheduleEvent` that accepts a random range (`minTime`, `maxTime`). Generates a random delay within the range and delegates to the core `ScheduleEvent#3`.

**GetTimeUntilEvent**  
Calculates the remaining time until the next occurrence of a specific `eventId`. Finds the first event in the map with the matching ID and subtracts the current `_time` from its absolute trigger time. Returns `std::numeric_limits<uint32>::max()` if not found.

**RescheduleEvent#2**  
Overload of `RescheduleEvent` accepting `std::chrono::Milliseconds`. Converts to `uint32` and delegates.

**RescheduleEvent**  
Cancels all existing instances of `eventId` via `CancelEvent`, then schedules a new instance with the given parameters via `ScheduleEvent`.

**RescheduleEvent#3**  
Overload of `RescheduleEvent` accepting a random range. Generates a random delay and delegates to the core `RescheduleEvent`.

**Repeat**  
Re-schedules the most recently executed event (`_lastEvent`) with a fixed `time` delay. Inserts a new entry into `_eventMap` with the same packed metadata as `_lastEvent` but with a new absolute time (`_time + time`).

**Repeat#3**  
Overload of `Repeat` accepting a random range (`minTime`, `maxTime`). Generates a random delay and delegates to `Repeat`.

**Repeat#2**  
Overload of `Repeat` accepting `std::chrono::Milliseconds`. Converts to `uint32` and delegates.

**Repeat#4**  
Overload of `Repeat` accepting `std::chrono::Milliseconds` range. Converts to `uint32` and delegates.

**DelayEvents**  
Delays *all* events in the map by reducing the internal timer `_time` by `delay`. This effectively pushes all absolute event times further into the future relative to the current tick. Clamps `_time` to 0 if `delay` exceeds current time.

**DelayEvents#3**  
Overload of `DelayEvents` (all events) accepting `std::chrono::Milliseconds`. Converts to `uint32` and delegates.

**DelayEvents#2**  
Overload of `DelayEvents` (group-specific) accepting `std::chrono::Milliseconds`. Converts to `uint32` and delegates to `DelayEvents#4`.

**GetNextEventTime**  
Finds the absolute time of the next occurrence of a specific `eventId`. Scans the map for the first matching ID and returns its key. Returns 0 if not found.

**IsInPhase**  
Checks if the current `_phase` mask includes the specified `phase` bit. Returns true if the phase is 0 (always true) or if the corresponding bit is set in `_phase`.

---

<!-- machine-true, projected from graph.json -->

## Map — EventMap

*Source:* EventMap.cpp, EventMap.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| Reset | method | — | boss_four_horsemen/Reset, boss_garr/Reset, boss_garr/Reset#2, boss_gluth/Reset, boss_golemagg/Reset, boss_golemagg/Reset#2, boss_grobbulus/Reset, boss_heigan/EventDanceEnd, boss_heigan/EventStartDance, boss_heigan/Reset, boss_loatheb/Reset, boss_lucifron/Reset, boss_noth/OnRemoveVulnerability, boss_noth/Reset, boss_noth/TeleportFromBalc, boss_noth/TeleportToBalc, boss_patchwerk/Reset, boss_razuvious/Aggro, boss_razuvious/MovementInform, boss_razuvious/Reset, boss_sapphiron/MovementInform#2, boss_sapphiron/Reset, boss_sapphiron/UpdateAI, boss_thaddius/JustReachedHome#2, boss_thaddius/Reset, boss_thaddius/Reset#2, instance_naxxramas.boss_kelthuzad/Reset, instance_naxxramas.boss_kelthuzad/UpdateP1, instance_naxxramas.Main/Initialize, instance_scarlet_monastery/Initialize, scourge_invasion/MinionspawnerAI, scourge_invasion/MouthAI, scourge_invasion/NecroticShard, scourge_invasion/npc_cultist_engineer, scourge_invasion/PallidHorrorAI, scourge_invasion/ScourgeMinion | — |
| SetPhase | method | — | — | — |
| ScheduleEvent#3 | method | — | boss_gluth/Aggro, boss_grobbulus/Aggro, boss_heigan/EventDanceEnd, boss_heigan/EventStartDance, boss_patchwerk/Aggro, boss_sapphiron/MovementInform, boss_sapphiron/npc_sapphiron_blizzardAI, boss_sapphiron/RescheduleIcebolt, boss_sapphiron/UpdateAI, boss_thaddius/Aggro, boss_thaddius/Aggro#2, boss_thaddius/TransitionToPhase, boss_thaddius/UpdateP2, instance_naxxramas.boss_kelthuzad/StartEncounter, instance_naxxramas.Main/Initialize, instance_naxxramas.Main/OnCreatureDeath, instance_naxxramas.Main/onNaxxramasAreaTrigger, instance_naxxramas.Main/SetData, instance_naxxramas.Main/Update, scourge_invasion/MinionspawnerAI, scourge_invasion/MouthAI, scourge_invasion/NecroticShard, scourge_invasion/OnScriptEventHappened#2, scourge_invasion/OnScriptEventHappened#3, scourge_invasion/PallidHorrorAI, scourge_invasion/Reset#9, scourge_invasion/SpellHit#5, scourge_invasion/UpdateAI#2, scourge_invasion/UpdateAI#7, scourge_invasion/UpdateAI#8, scourge_invasion/UpdateAI#9 | — |
| EventMap | ctor | — | — | — |
| ExecuteEvent | method | — | boss_four_horsemen/UpdateAI#2, boss_four_horsemen/UpdateAI#3, boss_four_horsemen/UpdateAI#4, boss_four_horsemen/UpdateAI#5, boss_garr/UpdateEvents, boss_garr/UpdateEvents#2, boss_gluth/UpdateAI, boss_golemagg/UpdateEvents, boss_golemagg/UpdateEvents#2, boss_grobbulus/UpdateAI, boss_heigan/UpdateAI, boss_loatheb/UpdateAI, boss_lucifron/UpdateAI, boss_noth/UpdateAI, boss_patchwerk/UpdateAI, boss_razuvious/UpdateAI, boss_razuvious/UpdateRP, boss_sapphiron/UpdateAI, boss_sapphiron/UpdateAI#2, boss_thaddius/UpdateAI#2, boss_thaddius/UpdateP2, boss_thaddius/UpdateTransitionPhase, fireworks_show/UpdateAI, go_scripts/UpdateAI, go_scripts/UpdateAI#3, go_scripts/UpdateAI#4, go_scripts/UpdateAI#5, instance_naxxramas.boss_kelthuzad/UpdateP1, instance_naxxramas.boss_kelthuzad/UpdateP2P3, instance_naxxramas.Main/Update, instance_scarlet_monastery/Update, scourge_invasion/UpdateAI, scourge_invasion/UpdateAI#10, scourge_invasion/UpdateAI#2, scourge_invasion/UpdateAI#7, scourge_invasion/UpdateAI#8, scourge_invasion/UpdateAI#9 | — |
| Update | method | — | boss_four_horsemen/UpdateAI, boss_garr/UpdateAI, boss_garr/UpdateAI#2, boss_gluth/UpdateAI, boss_golemagg/UpdateAI, boss_golemagg/UpdateAI#2, boss_grobbulus/UpdateAI, boss_heigan/UpdateAI, boss_loatheb/UpdateAI, boss_lucifron/UpdateAI, boss_noth/UpdateAI, boss_patchwerk/UpdateAI, boss_razuvious/UpdateAI, boss_razuvious/UpdateRP, boss_sapphiron/UpdateAI, boss_sapphiron/UpdateAI#2, boss_thaddius/UpdateAI#2, boss_thaddius/UpdateP2, boss_thaddius/UpdateTransitionPhase, fireworks_show/UpdateAI, go_scripts/UpdateAI, go_scripts/UpdateAI#3, go_scripts/UpdateAI#4, go_scripts/UpdateAI#5, instance_naxxramas.boss_kelthuzad/UpdateAI, instance_naxxramas.Main/Update, instance_scarlet_monastery/Update, scourge_invasion/UpdateAI, scourge_invasion/UpdateAI#10, scourge_invasion/UpdateAI#2, scourge_invasion/UpdateAI#7, scourge_invasion/UpdateAI#8, scourge_invasion/UpdateAI#9 | — |
| GetTimer | method | — | — | — |
| DelayEvents#4 | method | — | — | — |
| GetPhaseMask | method | — | — | — |
| Empty | method | — | — | — |
| CancelEvent | method | — | fireworks_show/UpdateAI | — |
| AddPhase | method | — | — | — |
| CancelEventGroup | method | — | — | — |
| RemovePhase | method | — | — | — |
| CancelEventsByGCD | method | — | — | — |
| ScheduleEvent#2 | method | — | boss_four_horsemen/Aggro#2, boss_four_horsemen/Aggro#3, boss_four_horsemen/Aggro#4, boss_four_horsemen/Aggro#5, boss_gluth/Aggro, boss_golemagg/DamageTaken, boss_golemagg/ScheduleCombatEvents, boss_golemagg/ScheduleCombatEvents#2, boss_heigan/Aggro, boss_heigan/EventDanceEnd, boss_heigan/EventStartDance, boss_loatheb/Aggro, boss_lucifron/Reset, boss_noth/Aggro, boss_noth/OnRemoveVulnerability, boss_noth/TeleportFromBalc, boss_noth/TeleportToBalc, boss_razuvious/Aggro, boss_razuvious/MovementInform, boss_sapphiron/Aggro, boss_sapphiron/MovementInform#2, boss_sapphiron/UpdateAI, fireworks_show/UpdateAI, go_scripts/go_darkmoon_faire_music, go_scripts/go_firework_rocket, go_scripts/go_lunar_festival_firecracker, go_scripts/OnUse, go_scripts/UpdateAI, go_scripts/UpdateAI#3, instance_naxxramas.boss_kelthuzad/StartEncounter, instance_naxxramas.boss_kelthuzad/UpdateP1, instance_naxxramas.boss_kelthuzad/UpdateP2P3, instance_naxxramas.Main/Initialize, instance_naxxramas.Main/SetData, instance_naxxramas.Main/Update, instance_scarlet_monastery/OnCreatureSpellHit, instance_scarlet_monastery/Update | — |
| GetNextEventTime#2 | method | — | — | — |
| ScheduleEvent | method | — | fireworks_show/UpdateAI, instance_scarlet_monastery/OnCreatureSpellHit | — |
| GetTimeUntilEvent | method | — | boss_heigan/EventDanceEnd, boss_heigan/EventStartDance, boss_noth/UpdateAI | — |
| RescheduleEvent#2 | method | — | boss_garr/ScheduleCombatEvents, boss_garr/ScheduleCombatEvents#2 | — |
| RescheduleEvent | method | — | — | — |
| RescheduleEvent#3 | method | — | — | — |
| Repeat | method | — | boss_four_horsemen/UpdateAI#3, boss_four_horsemen/UpdateAI#4, boss_four_horsemen/UpdateAI#5, boss_garr/UpdateEvents, boss_garr/UpdateEvents#2, boss_gluth/UpdateAI, boss_golemagg/UpdateEvents, boss_golemagg/UpdateEvents#2, boss_heigan/CheckManausersAndRepeat, boss_heigan/EventTaunt, boss_heigan/UpdateAI, boss_loatheb/UpdateAI, boss_lucifron/UpdateAI, boss_noth/BlinkAndRepeatEvent, boss_noth/CurseAndRepeatEvent, boss_noth/SpawnWarriorsAndRepeatEvent, boss_razuvious/UpdateAI, boss_sapphiron/UpdateAI, boss_sapphiron/UpdateAI#2, instance_naxxramas.boss_kelthuzad/DoChains, instance_naxxramas.boss_kelthuzad/UpdateP2P3, instance_naxxramas.Main/Update | — |
| Repeat#3 | method | — | boss_gluth/UpdateAI, boss_grobbulus/UpdateAI, boss_loatheb/UpdateAI, boss_noth/CurseAndRepeatEvent, boss_noth/TeleportFromBalc, boss_noth/TeleportToBalc, boss_patchwerk/UpdateAI, boss_sapphiron/RescheduleIcebolt, boss_sapphiron/UpdateAI, boss_thaddius/DoSpellChain, boss_thaddius/UpdateAI#2, boss_thaddius/UpdateP2, instance_naxxramas.boss_kelthuzad/UpdateP1, instance_naxxramas.boss_kelthuzad/UpdateP2P3 | — |
| Repeat#2 | method | — | — | — |
| Repeat#4 | method | — | — | — |
| DelayEvents | method | — | — | — |
| DelayEvents#3 | method | — | — | — |
| DelayEvents#2 | method | — | — | — |
| GetNextEventTime | method | — | — | — |
| IsInPhase | method | — | — | — |
