# boss_heigan

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# boss_heigan

## Purpose & Responsibilities

`boss_heigan.cpp` implements the AI and encounter mechanics for **Heigan the Unclean**, a Naxxramas raid boss. The unit manages two primary combat states: `PHASE_FIGHT` (standard melee/AoE engagement on the main platform) and `PHASE_DANCE` (a periodic phase where Heigan teleports to a raised platform, becomes passive, and forces players to navigate moving safe spots while avoiding eruptions and plague waves).

Key responsibilities include:
1.  **Phase Management:** Switching between aggressive combat and the dance mechanic, resetting event timers, and managing player states (e.g., tracking ported players).
2.  **Mechanic Execution:**
    *   **Eruptions:** Spawning fissures in specific safe-spot patterns based on a rotating `eruptionPhase` counter.
    *   **Plague Waves:** Summoning stationary plague clouds during the dance phase.
    *   **Player Teleportation:** Periodically teleporting random non-tank players to a fixed location to disrupt positioning.
    *   **Mana Burn:** Detecting mana-using players within range and casting a targeted AoE spell.
    *   **Decrepit Fever:** Casting periodic AoE damage spells.
3.  **Instance Integration:** Communicating with `instance_naxxramas` to update boss status (`IN_PROGRESS`, `DONE`, `FAIL`) and control entrance doors.
4.  **Spell Modification:** Providing a custom spell script for `SPELL_MANABURN` to enforce a specific detection radius.

## Member-by-Member Behavior

### Initialization and State Management

**`boss_heiganAI` (ctor)**
Initializes the AI object. Retrieves `instance_naxxramas` instance data from the creature and immediately calls `Reset()` to initialize internal state variables (phase, events, eruption counter).

**`Reset`**
Resets the encounter state. Clears `portedPlayersThisPhase`, resets the event scheduler (`m_events`), sets `killCooldown` to 10 seconds, and defaults `currentPhase` to `PHASE_FIGHT`.

**`Aggro`**
Triggered when combat begins. Sets the creature in combat with the zone, resets `eruptionPhase` and `currentPhase`, and schedules initial events: `EVENT_FEVER` (30s), `EVENT_DANCE` (90s), `EVENT_ERUPT` (15s), `EVENT_MANABURN` (15s), `EVENT_TAUNT` (random 20–70s), `EVENT_DOOR_CLOSE` (15s), and `EVENT_PORT_PLAYER` (40s). Plays a random aggro sound and updates instance data to `IN_PROGRESS`.

**`JustDied`**
Triggered upon death. Plays the death sound, updates instance data to `DONE`, and closes the boss entrance door.

**`JustReachedHome`**
Triggered if the boss despawns or resets without dying. Updates instance data to `FAIL` and closes the boss entrance door.

### Combat Logic and Targeting

**`MoveInLineOfSight`**
Handles aggro generation. Returns immediately if in `PHASE_DANCE`. Otherwise, ignores targets with X-coordinate > 2825.0f (likely tunnel/safe zone exclusion). If valid, checks hostility, accessibility, and line of sight. If no victim exists, starts attacking. If a victim exists and the map is a dungeon, adds threat and sets combat with the new target.

**`AttackStart`**
Prevents attack initiation if in `PHASE_DANCE`. Otherwise, delegates to `ScriptedAI::AttackStart`.

**`KilledUnit`**
Plays a slay sound if `killCooldown` has expired (preventing spam).

**`UpdateAI`**
The main update loop.
1.  **Phase Checks:**
    *   In `PHASE_FIGHT`: Ensures a valid target exists. Calls `instance_naxxramas.HandleEvadeOutOfHome` to prevent pathfinding off the platform. Returns if evasion is triggered.
    *   In `PHASE_DANCE`: If the threat list is empty (wipe), forces `EventDanceEnd` to reset the boss to `PHASE_FIGHT` logic for proper evasion/despawn.
2.  **Event Processing:** Updates `m_events` and executes pending events:
    *   `EVENT_FEVER`: Casts `SPELL_DECREPIT_FEVER`, repeats every 20–25s.
    *   `EVENT_DANCE`: Triggers `EventStartDance`.
    *   `EVENT_DANCE_END`: Triggers `EventDanceEnd`.
    *   `EVENT_ERUPT`: Triggers `UpdateEruption`, repeats every 3s (Dance) or 10s (Fight).
    *   `EVENT_TAUNT`: Triggers `EventTaunt`.
    *   `EVENT_DOOR_CLOSE`: Closes entrance door via instance script.
    *   `EVENT_MANABURN`: Triggers `CheckManausersAndRepeat`.
    *   `EVENT_PORT_PLAYER`: Triggers `EventPortPlayer`.
3.  **Cooldowns & Melee:** Decrements `killCooldown`. Performs melee attacks if in `PHASE_FIGHT`.

### Dance Phase Mechanics

**`EventStartDance`**
Initiates the dance phase:
1.  Clears `portedPlayersThisPhase`.
2.  Casts `SPELL_TELEPORT_SELF`. If this fails, returns early.
3.  Sets `currentPhase` to `PHASE_DANCE`, react state to `REACT_PASSIVE`, stops attacks/movement, and casts `SPELL_PLAGUE_CLOUD`.
4.  Saves remaining taunt time, resets events, and reschedules: `EVENT_TAUNT` (remaining), `EVENT_DANCE_END` (45s), `EVENT_ERUPT` (4s).
5.  Summons plague waves at predefined `eyeStalkPossitions`.
6.  Plays channeling sound and resets `eruptionPhase`.

**`EventDanceEnd`**
Ends the dance phase:
1.  Sets `currentPhase` to `PHASE_FIGHT`.
2.  Saves remaining taunt time, resets events, and reschedules: `EVENT_TAUNT` (remaining), `EVENT_FEVER` (5s), `EVENT_DANCE` (90s), `EVENT_ERUPT` (10s), `EVENT_MANABURN` (10s), `EVENT_PORT_PLAYER` (18s and 48s).
3.  Interrupts non-melee spells, sets react state to `REACT_AGGRESSIVE`, and resets `eruptionPhase`.
4.  Selects a hostile target and resumes chasing.

**`UpdateEruption`**
Manages the eruption mechanic:
1.  Spawns a temporary `NPC_PLAGUE_WAVE` fissure creature at a fixed coordinate. Logs an error if spawn fails.
2.  Iterates through 4 areas (`uiArea`). Skips areas matching `(eruptionPhase % 6)` or `6 - (eruptionPhase % 6)` (safe zones).
3.  For active areas, triggers associated game object traps from instance data.
4.  Spawns additional fissure creatures at hardcoded safe-spot coordinates (`sect1SafeSpot`, `sect2SafeSpot`, etc.) for active areas.
5.  If in `PHASE_DANCE`, also spawns fissures at `safespotFissures` (tunnel safe spots).
6.  Increments `eruptionPhase`.

**`SummmonPlagueWave`**
Helper to summon `NPC_PLAGUE_WAVE` at specified coordinates and cast `SPELL_PLAGUE_WAVE` on itself.

**`SendEruptCustomLocation`**
Helper to summon `NPC_PLAGUE_WAVE` at specified coordinates and cast `SPELL_ERUPTION` on itself.

### Player Manipulation and Spells

**`EventPortPlayer`**
Teleports players to disrupt positioning:
1.  Retrieves threat list, skipping the top target (tank).
2.  Identifies candidates: alive players not in `portedPlayersThisPhase`.
3.  Selects up to 3 random candidates.
4.  For each: adds GUID to `portedPlayersThisPhase`, summons a visual creature at the player's location, sends spell visual `30211`, and teleports the player to fixed coordinates `(2917.43f, -3769.18f, 273.62f)`.

**`CheckManausersAndRepeat`**
Checks for mana users to trigger `SPELL_MANABURN`:
1.  Iterates threat list for alive players with `POWER_MANA` within 28 yards.
2.  If found, casts `SPELL_MANABURN` and repeats in 3s.
3.  If not found, repeats in 1s.

**`EventTaunt`**
Plays a random taunt sound and reschedules the next taunt in 20–70 seconds.

### Script Registration and Spell Hooks

**`GetAI_boss_heigan`**
Factory function returning a new `boss_heiganAI` instance.

**`OnSetTargetMap`**
Member of `HeiganManaBurnScript`. Overrides the target radius for `SPELL_MANABURN` to 28.0 yards, ensuring it covers the intended area regardless of default spell settings.

**`GetScript_HeiganManaBurn`**
Factory function returning a new `HeiganManaBurnScript` instance.

**`AddSC_boss_heigan`**
Registers the boss AI script (`boss_heigan`) and the spell script (`spell_heigan_mana_burn`) with the script manager.

## Cross-Unit Boundaries

*   **`instance_naxxramas.Main`**:
    *   *Called by:* `Aggro`, `JustDied`, `JustReachedHome`, `UpdateAI` (via `HandleEvadeOutOfHome`), `UpdateEruption` (via `m_alHeiganTrapGuids`).
    *   *Purpose:* Manages instance-wide state. `SetData` tracks boss progress. `UpdateAutomaticBossEntranceDoor` controls physical doors. `HandleEvadeOutOfHome` prevents pathfinding errors. `m_alHeiganTrapGuids` provides references to game objects used in eruptions.
*   **`ScriptedAI` / `CreatureAI`**:
    *   *Called by:* Most methods.
    *   *Purpose:* Base AI functionality. `DoScriptText` plays sounds. `DoCastAOE`/`DoCastSpellIfCan` handle spell casting. `DoStopAttack`/`AttackStop` manage combat state. `MoveIdle`/`MoveChase` control movement.
*   **`WorldObject.Object`**:
    *   *Called by:* `boss_heiganAI` (ctor), `MoveInLineOfSight`, `EventPortPlayer`, `UpdateEruption`, `SendEruptCustomLocation`, `SummmonPlagueWave`.
    *   *Purpose:* Core object interactions. `GetInstanceData` retrieves instance context. `SummonCreature` spawns adds. `GetPositionX/Y/Z` and `GetOrientation` retrieve spatial data. `IsWithinLOSInMap` checks visibility.
*   **`Unit.Main`**:
    *   *Called by:* `MoveInLineOfSight`, `EventStartDance`, `EventDanceEnd`, `EventPortPlayer`, `CheckManausersAndRepeat`, `UpdateAI`.
    *   *Purpose:* Unit-specific logic. `SetInCombatWithZone` initiates combat. `AddThreat`/`GetThreatManager` manage aggro. `SelectHostileTarget`/`GetVictim` handle targeting. `NearTeleportTo` moves players. `SendSpellGo` triggers spell visuals. `IsAlive`/`GetPowerType` check player state.
*   **`EventMap`**:
    *   *Called by:* `Reset`, `Aggro`, `EventStartDance`, `EventDanceEnd`, `EventTaunt`, `CheckManausersAndRepeat`, `UpdateAI`.
    *   *Purpose:* Timer management. `ScheduleEvent` queues actions. `Reset` clears timers. `GetTimeUntilEvent` preserves timer state across phase changes. `ExecuteEvent` processes due events. `Repeat` reschedules recurring events.
*   **`GameObject`**:
    *   *Called by:* `UpdateEruption`.
    *   *Purpose:* Interacts with world objects. `Use` triggers the eruption animation/effect. `SendGameObjectCustomAnim` ensures visual feedback.
*   **`SpellCaster`**:
    *   *Called by:* `SendEruptCustomLocation`, `SummmonPlagueWave`, `EventStartDance`, `EventDanceEnd`, `CheckManausersAndRepeat`.
    *   *Purpose:* Spell casting interface. `CastSpell` applies spells to targets. `InterruptNonMeleeSpells` stops casting during phase transitions.
*   **`shared_Util`**:
    *   *Called by:* `Aggro`, `EventTaunt`, `EventPortPlayer`, `UpdateAI`.
    *   *Purpose:* Utility functions. `urand` generates random integers. `randtime` generates random time durations.
*   **`ThreatManager`**:
    *   *Called by:* `EventPortPlayer`, `CheckManausersAndRepeat`, `UpdateAI`.
    *   *Purpose:* Accesses the threat list to identify tanks, candidates for teleportation, and mana users.
*   **`ScriptMgr`**:
    *   *Called by:* `Aggro`, `KilledUnit`, `JustDied`, `EventStartDance`, `EventTaunt`, `AddSC_boss_heigan`.
    *   *Purpose:* Global script management. `DoScriptText` broadcasts sounds. `RegisterSelf` registers scripts.
*   **`Log.Main`**:
    *   *Called by:* `UpdateEruption`.
    *   *Purpose:* Logging errors (e.g., failed creature spawn).
*   **`ZoneScript`**:
    *   *Called by:* `UpdateEruption`.
    *   *Purpose:* `GetGameObject` retrieves game object pointers from the world.

## Data Model

This unit does not directly query or modify database tables. It interacts with runtime instance data (`instance_naxxramas`) and world objects/creatures spawned dynamically. No SQL tables are touched by this code.

## Notable Implementation Details

1.  **Hardcoded Safe Spots:** The eruption mechanic relies on hardcoded coordinate arrays (`sect1SafeSpot`, `sect2SafeSpot`, etc.) and `safespotFissures`. These define where fissures *do not* appear (safe zones) and where they *do* appear. The logic in `UpdateEruption` skips areas matching `(eruptionPhase % 6)` or `6 - (eruptionPhase % 6)`, creating a rotating pattern of safe zones.
2.  **Mana Burn Radius Override:** The `HeiganManaBurnScript` explicitly overrides the spell's target radius to 28.0 yards in `OnSetTargetMap`. This is critical because the default spell radius might be smaller, allowing players to kite the boss outside the effective range. The comment notes this prevents tanking in one corner while ranged stays in another.
3.  **Phase Transition Safety:** In `UpdateAI`, if the boss is in `PHASE_DANCE` and the threat list is empty (all players dead), it manually calls `EventDanceEnd`. This forces the boss back to `PHASE_FIGHT` logic, allowing `HandleEvadeOutOfHome` to run and potentially despawn/reset the boss correctly, preventing a stuck state.
4.  **Ported Player Tracking:** `EventPortPlayer` uses `portedPlayersThisPhase` to ensure the same player isn't teleported multiple times in a single rotation. This list is cleared at the start of `EventStartDance` and `Reset`.
5.  **Eruption Timing:** During `PHASE_DANCE`, eruptions occur every 3 seconds. During `PHASE_FIGHT`, they occur every 10 seconds. This is handled in `UpdateAI`'s event repeat logic.
6.  **Kill Cooldown:** A simple 10-second cooldown (`killCooldown`) prevents the boss from playing slay sounds too frequently.
7.  **Tunnel Safe Spots:** During `PHASE_DANCE`, `UpdateEruption` additionally spawns fissures at `safespotFissures` coordinates, which are described as "safespot avoidance in tunnel." This suggests players hiding in the tunnel are still targeted by eruptions, forcing them out.
8.  **Early Return on Teleport Fail:** `EventStartDance` returns immediately if `SPELL_TELEPORT_SELF` fails to cast. This prevents the boss from entering the dance phase logic without actually moving, which would likely cause desync or broken mechanics.

## Member Reference

**`boss_heiganAI`**
Constructor for the AI class. Retrieves `instance_naxxramas` data from the creature and calls `Reset()`.

**`Reset`**
Clears `portedPlayersThisPhase`, resets the event map, sets `killCooldown` to 10000ms, and sets `currentPhase` to `PHASE_FIGHT`.

**`Aggro`**
Sets combat state, resets phase counters, schedules initial events (Fever, Dance, Erupt, Manaburn, Taunt, Door Close, Port Player), plays a random aggro text, and sets instance data to `IN_PROGRESS`.

**`MoveInLineOfSight`**
If in `PHASE_DANCE`, returns. Otherwise, checks if the target is within X < 2825.0f. If valid, checks hostility and LOS. If no victim, calls `AttackStart`. If victim exists and map is dungeon, adds threat and sets combat.

**`AttackStart`**
Returns if in `PHASE_DANCE`. Otherwise, calls `ScriptedAI::AttackStart`.

**`KilledUnit`**
If `killCooldown` is 0, plays `SAY_SLAY`.

**`JustDied`**
Plays `SAY_DEATH`, sets instance data to `DONE`, and updates the boss entrance door to `DONE`.

**`JustReachedHome`**
Sets instance data to `FAIL` and updates the boss entrance door to `FAIL`.

**`SendEruptCustomLocation`**
Summons `NPC_PLAGUE_WAVE` at given coordinates and casts `SPELL_ERUPTION` on it.

**`UpdateEruption`**
Summons a fissure creature. Iterates 4 areas, skipping safe zones based on `eruptionPhase % 6`. Triggers GO traps and summons fissures at hardcoded safe-spot coordinates for active areas. If in `PHASE_DANCE`, also spawns fissures at tunnel safe spots. Increments `eruptionPhase`.

**`SummmonPlagueWave`**
Summons `NPC_PLAGUE_WAVE` at given coordinates and casts `SPELL_PLAGUE_WAVE` on it.

**`EventStartDance`**
Clears ported players. Casts `SPELL_TELEPORT_SELF`. Sets phase to `PHASE_DANCE`, react state to passive, stops movement/attack, casts `SPELL_PLAGUE_CLOUD`. Reschedules events (Taunt, Dance End, Erupt). Summons plague waves at `eyeStalkPossitions`. Plays channeling text. Resets `eruptionPhase`.

**`EventDanceEnd`**
Sets phase to `PHASE_FIGHT`. Reschedules events (Taunt, Fever, Dance, Erupt, Manaburn, Port Player). Interrupts non-melee spells, sets react state to aggressive. Resets `eruptionPhase`. Selects target and chases.

**`EventPortPlayer`**
Gets threat list, skips tank. Finds alive players not in `portedPlayersThisPhase`. Selects up to 3 random candidates. For each, adds to ported list, summons visual creature, sends spell go, and teleports player to fixed coordinates.

**`EventTaunt`**
Plays random taunt text. Repeats event in 20–70s.

**`CheckManausersAndRepeat`**
Iterates threat list for alive mana-users within 28 yards. If found, casts `SPELL_MANABURN` and repeats in 3s. Else repeats in 1s.

**`UpdateAI`**
If `PHASE_FIGHT`, checks target and calls `HandleEvadeOutOfHome`. If `PHASE_DANCE` and threat list empty, calls `EventDanceEnd`. Updates events. Handles event execution (Fever, Dance, Dance End, Erupt, Taunt, Door Close, Manaburn, Port Player). Decrements `killCooldown`. Performs melee attack if `PHASE_FIGHT`.

**`GetAI_boss_heigan`**
Returns a new `boss_heiganAI` instance.

**`OnSetTargetMap`**
Sets the radius for `SPELL_MANABURN` to 28.0f.

**`GetScript_HeiganManaBurn`**
Returns a new `HeiganManaBurnScript` instance.

**`AddSC_boss_heigan`**
Registers `boss_heigan` AI and `spell_heigan_mana_burn` spell script.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_heigan

*Source:* boss_heigan.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_heiganAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset | method | EventMap/Reset | — | — |
| Aggro | method | Creature.Main/SetInCombatWithZone, EventMap/ScheduleEvent#2, instance_naxxramas.Main/SetData, ScriptMgr/DoScriptText, shared_Util/randtime, shared_Util/urand | — | — |
| MoveInLineOfSight | method | Creature.Main/CanInitiateAttack, Map.Main/IsDungeon, Unit.Main/AddThreat, Unit.Main/GetVictim, Unit.Main/IsHostileTo, Unit.Main/IsInAccessablePlaceFor, Unit.Main/IsTargetableBy, Unit.Main/SetInCombatWith, WorldObject.Object/GetMap, WorldObject.Object/GetPositionX, WorldObject.Object/IsWithinLOSInMap | — | — |
| AttackStart | method | CreatureAI/AttackStart | — | — |
| KilledUnit | method | ScriptMgr/DoScriptText | — | — |
| JustDied | method | instance_naxxramas.Main/SetData, instance_naxxramas.Main/UpdateAutomaticBossEntranceDoor, ScriptMgr/DoScriptText | — | — |
| JustReachedHome | method | instance_naxxramas.Main/SetData, instance_naxxramas.Main/UpdateAutomaticBossEntranceDoor | — | — |
| SendEruptCustomLocation | method | SpellCaster/CastSpell#2, WorldObject.Object/SummonCreature#2 | — | — |
| UpdateEruption | method | GameObject/SendGameObjectCustomAnim, GameObject/Use, Log.Main/Out, WorldObject.Object/SummonCreature#2, ZoneScript/GetGameObject | — | — |
| SummmonPlagueWave | method | SpellCaster/CastSpell#2, WorldObject.Object/SummonCreature#2 | — | — |
| EventStartDance | method | Creature.MotionMaster/MoveIdle, CreatureAI/DoCastAOE, CreatureAI/DoCastSpellIfCan, EventMap/GetTimeUntilEvent, EventMap/Reset, EventMap/ScheduleEvent#2, EventMap/ScheduleEvent#3, ScriptedAI/DoStopAttack, ScriptMgr/DoScriptText, Unit.Main/AttackStop, Unit.Main/GetMotionMaster, Unit.Main/SetReactState, Unit.Main/StopMoving | — | — |
| EventDanceEnd | method | Creature.MotionMaster/MoveChase, EventMap/GetTimeUntilEvent, EventMap/Reset, EventMap/ScheduleEvent#2, EventMap/ScheduleEvent#3, SpellCaster/InterruptNonMeleeSpells, Unit.Main/GetMotionMaster, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget, Unit.Main/SetReactState | — | — |
| EventPortPlayer | method | Object/GetObjectGuid, Object/ToPlayer, shared_Util/urand, ThreatManager/getThreatList, Unit.Main/GetThreatManager, Unit.Main/IsAlive, Unit.Main/NearTeleportTo, Unit.Main/SendSpellGo, WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/SummonCreature#2 | — | — |
| EventTaunt | method | EventMap/Repeat, ScriptMgr/DoScriptText, shared_Util/randtime | — | — |
| CheckManausersAndRepeat | method | CreatureAI/DoCastSpellIfCan, EventMap/Repeat, Object/ToPlayer, ThreatManager/getThreatList, Unit.Main/GetPowerType, Unit.Main/GetThreatManager, Unit.Main/IsAlive, WorldObject.Object/GetDistance3dToCenter#3 | — | — |
| UpdateAI | method | CreatureAI/DoCastAOE, CreatureAI/DoMeleeAttackIfReady, EventMap/ExecuteEvent, EventMap/Repeat, EventMap/Update, instance_naxxramas.Main/HandleEvadeOutOfHome, instance_naxxramas.Main/UpdateAutomaticBossEntranceDoor, shared_Util/randtime, ThreatManager/isThreatListEmpty, Unit.Main/GetThreatManager, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_boss_heigan | function | — | — | — |
| OnSetTargetMap | method | — | — | — |
| GetScript_HeiganManaBurn | function | — | — | — |
| AddSC_boss_heigan | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
