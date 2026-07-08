# boss_golemagg

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# boss_golemagg

**Purpose & Responsibilities**
This unit implements the combat artificial intelligence for **Golemagg the Incinerator**, a boss encounter in the Molten Core instance, along with its associated mechanics: the **Core Rager** adds and the **Golemagg's Trust** aura. The code defines three distinct AI behaviors:
1.  `boss_golemaggAI`: Controls the boss's spell rotation (Pyroblast, Earthquake), manages the spawning/death of Core Rager adds, and triggers an enrage phase at low health.
2.  `mob_core_ragerAI`: Controls the Core Rager adds, implementing a "unkillable" mechanic below 50% health (unless the boss is dead), a leash check to reset the boss if they wander too far, and standard melee/spell attacks.
3.  `GolemaggsTrustScript`: An aura script that periodically buffs nearby Core Ragers if they are tanked close to Golemagg, increasing their threat and damage potential.

The unit relies heavily on the `EventMap` system for scheduling spells and checks, and interacts with the instance data (`ScriptedInstance`) to track encounter progress and coordinate between the boss and adds.

**Member-by-Member Behavior**

### Boss Golemagg AI (`boss_golemaggAI`)

*   **Initialization & State**:
    *   `boss_golemaggAI` (ctor): Initializes the AI, retrieves the instance data pointer, and calls `Reset`.
    *   `Reset`: Clears internal state (enrage flag, event map, add list). If the boss is alive, it sets the instance data state to `NOT_STARTED`. It then finds all Core Ragers within 250 yards, kills any that are alive (to ensure a clean start), and respawns them.
    *   `Aggro`: Sets the instance state to `IN_PROGRESS`. It captures the GUIDs of all Core Ragers within 150 yards into `m_addList` for later management. It schedules the initial combat events.
    *   `EnterEvadeMode`: Calls `KillAdds(true)` to despawn and respawn all tracked adds, then delegates to the base `ScriptedAI::EnterEvadeMode`.
    *   `JustDied`: Sets the instance state to `DONE`. Calls `KillAdds(false)` to despawn the adds without respawning them (they remain dead until the next reset).

*   **Combat Logic**:
    *   `DamageTaken`: Checks if the boss is already enraged. If not, and if health drops below 10%, it casts `SPELL_ATTRACK_RAGER` (likely a visual or trigger spell) and schedules `EVENT_EARTHQUAKE` to begin. It sets `m_bEnraged` to true to prevent re-triggering.
    *   `UpdateAI`: Standard AI loop. Checks for a valid target, updates the event map, calls `UpdateEvents` to handle scheduled actions, and performs melee attacks if ready.
    *   `ScheduleCombatEvents`: Schedules `EVENT_PYROBLAST` (7s) and `EVENT_GOLEMAGG_TRUST` (10s). Note: `EVENT_EARTHQUAKE` is scheduled dynamically in `DamageTaken`.
    *   `UpdateEvents`: Handles the event queue:
        *   `EVENT_PYROBLAST`: Selects a random target and casts Pyroblast. Repeats every 7s on success, or 1s on failure.
        *   `EVENT_EARTHQUAKE`: Casts Earthquake on the current victim. Repeats every 5s on success, or 1s on failure.
        *   `EVENT_GOLEMAGG_TRUST`: Casts the trust aura on self. Repeats every 2s on success, or 1s on failure.

*   **Add Management**:
    *   `KillAdds`: Iterates through `m_addList`. For each GUID, it retrieves the creature from the map. If found, it forces the creature to die (`DisappearAndDie`). If the `respawn` flag is true, it immediately respawns the creature.

*   **Factory**:
    *   `GetAI_boss_golemagg`: Returns a new instance of `boss_golemaggAI`.

### Core Rager AI (`mob_core_ragerAI`)

*   **Initialization & State**:
    *   `mob_core_ragerAI` (ctor): Initializes the AI, retrieves instance data, and calls `Reset`.
    *   `Reset`: Clears the event map.
    *   `Aggro`: Schedules combat events.
    *   `DamageTaken`: Implements the "unkillable" mechanic. If the instance state is `DONE` (boss dead), it returns early, allowing normal damage processing. Otherwise, if the rager's health would drop below 50% due to the incoming damage, it sets the damage to 0, plays a low-health emote, and attempts to cast `SPELL_FULL_HEAL` on the victim (likely a typo in the original code, intended to heal self or just trigger the heal effect). If the cast fails, it manually sets health to max. This effectively makes them immune to death below 50% HP while the boss is alive.
    *   `UpdateAI`: Standard AI loop. Updates events, handles melee attacks.
    *   `ScheduleCombatEvents`: Schedules `EVENT_MANGLE` (7s) and `EVENT_CHECK_LEASH` (3s).
    *   `UpdateEvents`:
        *   `EVENT_MANGLE`: Casts Mangle on the victim. Repeats every 10s on success, 1s on failure.
        *   `EVENT_CHECK_LEASH`: Retrieves Golemagg's GUID from instance data. Gets the creature object. If the distance between the rager and Golemagg exceeds 100 yards, it casts `EnterEvadeMode` on Golemagg's AI, resetting the encounter. This prevents players from kiting the adds away from the boss.

*   **Factory**:
    *   `GetAI_mob_core_rager`: Returns a new instance of `mob_core_ragerAI`.

### Golemagg's Trust Aura (`GolemaggsTrustScript`)

*   `OnBeforeApply`: If applying the aura, it sets the periodic timer to 1 second.
*   `OnPeriodicDummy`: Executes every second. If the caster (Golemagg) is dead or not in combat, it returns. Otherwise, it finds all Core Ragers within 30 yards of Golemagg. For each, it casts `SPELL_GOLEMAGG_TRUST` (the buff spell) on them, granting increased damage and attack speed.
*   `GetScript_GolemaggsTrust`: Factory function returning the aura script.

### Script Registration

*   `AddSC_boss_golemagg`: Registers the three scripts (`boss_golemagg`, `mob_core_rager`, `spell_golemaggs_trust`) with the script manager.

**Cross-Unit Boundaries**

*   **Instance Data (`ScriptedInstance`)**:
    *   `boss_golemaggAI` calls `SetData` to update the encounter state (`NOT_STARTED`, `IN_PROGRESS`, `DONE`).
    *   `mob_core_ragerAI` calls `GetData` to check if the boss is dead (`TYPE_GOLEMAGG == DONE`) and `GetData64` to retrieve Golemagg's GUID for the leash check.
    *   This coordination ensures the adds behave correctly relative to the boss's state and position.

*   **Event Map (`EventMap`)**:
    *   Both AIs use `ScheduleEvent`, `Reset`, `Update`, `ExecuteEvent`, and `Repeat` to manage their spell rotations and checks. This decouples timing logic from the main AI loop.

*   **Creature/Object Management**:
    *   `boss_golemaggAI` uses `GetCreatureListWithEntryInGrid` to find adds, `GetMap()->GetCreature` to retrieve them by GUID, and `DisappearAndDie`/`Respawn` to manage their lifecycle.
    *   `mob_core_ragerAI` uses `GetDistance2d` to check leash range and `dynamic_cast` to access Golemagg's AI for evasion.

*   **Spells & Combat**:
    *   Both AIs use `DoCastSpellIfCan` for spell casting, `SelectHostileTarget`/`SelectAttackingTarget` for targeting, and `DoMeleeAttackIfReady` for physical attacks.
    *   `mob_core_ragerAI` uses `HealthBelowPctDamaged` and `SetHealth` for its unkillable mechanic.

*   **Aura System**:
    *   `GolemaggsTrustScript` uses `GetCaster`, `GetCreatureListWithEntryInGrid`, and `CastSpell` to apply buffs to nearby adds.

**Data Model**

This unit does not directly query or modify any database tables. It interacts with the game world objects (Creatures, Instances) and their in-memory state. The spell IDs and creature entries are hardcoded in the enums.

**Notable Implementation Details**

1.  **Unkillable Adds**: The `DamageTaken` method in `mob_core_ragerAI` prevents Core Ragers from dying below 50% health while Golemagg is alive. This is a critical mechanic; players must kill the boss first or manage the adds carefully. The code sets `uiDamage = 0` and heals the rager if the heal spell fails.
2.  **Leash Mechanic**: The `EVENT_CHECK_LEASH` in `mob_core_ragerAI` resets the entire encounter if any Core Rager moves more than 100 yards from Golemagg. This forces players to keep the adds contained near the boss.
3.  **Enrage Phase**: Golemagg enters an enrage phase at 10% health, starting to cast Earthquake repeatedly. This is triggered in `DamageTaken` and scheduled via `EventMap`.
4.  **Trust Aura Proximity**: The `GolemaggsTrustScript` buffs Core Ragers only if they are within 30 yards of Golemagg. This encourages players to spread the adds out to reduce their effectiveness.
5.  **Add Cleanup**: `boss_golemaggAI::KillAdds` is called on evade and death. On evade, it respawns the adds; on death, it leaves them dead. This ensures a clean state for the next attempt.
6.  **Hardcoded IDs**: All spell IDs, creature entries, and event IDs are hardcoded in enums. Changes to these require recompilation.
7.  **Potential Bug in Heal**: In `mob_core_ragerAI::DamageTaken`, `DoCastSpellIfCan(m_creature->GetVictim(), SPELL_FULL_HEAL)` casts the heal on the *victim* (the player), not the rager itself. If the cast fails, it manually sets the rager's health to max. This suggests the spell might be intended to heal the rager, but the target is wrong. However, since `uiDamage` is set to 0 before this, the rager survives regardless. The manual health set acts as a fallback.

## Member Reference

**boss_golemaggAI** (ctor): Initializes the boss AI, retrieves instance data, and calls `Reset`.
**Reset**: Resets boss state, clears events/add list, sets instance state to `NOT_STARTED`, kills and respawns nearby Core Ragers.
**Aggro**: Sets instance state to `IN_PROGRESS`, captures nearby Core Rager GUIDs, schedules combat events.
**EnterEvadeMode**: Kills and respawns tracked adds, then evades.
**JustDied**: Sets instance state to `DONE`, kills tracked adds without respawning.
**KillAdds**: Iterates tracked add GUIDs, forces death, optionally respawns.
**DamageTaken**: Triggers enrage phase (Earthquake) if health < 10% and not already enraged.
**UpdateAI**: Main AI loop, updates events, handles melee.
**ScheduleCombatEvents**: Schedules Pyroblast and Trust aura events.
**UpdateEvents**: Handles Pyroblast (random target), Earthquake (victim), and Trust aura (self) casts with retry logic.
**GetAI_boss_golemagg**: Factory function for boss AI.
**mob_core_ragerAI** (ctor): Initializes add AI, retrieves instance data, calls `Reset`.
**Reset#2**: Clears event map.
**Aggro#2**: Schedules combat events.
**DamageTaken#2**: Prevents death below 50% HP if boss is alive, plays emote, attempts heal, sets health to max if heal fails.
**UpdateAI#2**: Main AI loop, updates events, handles melee.
**ScheduleCombatEvents#2**: Schedules Mangle and Leash Check events.
**UpdateEvents#2**: Handles Mangle cast and Leash Check (resets boss if >100yds away).
**GetAI_mob_core_rager**: Factory function for add AI.
**OnBeforeApply**: Sets aura periodic timer to 1s.
**OnPeriodicDummy**: Buffs nearby Core Ragers with Trust aura if caster is alive/in combat.
**GetScript_GolemaggsTrust**: Factory function for aura script.
**AddSC_boss_golemagg**: Registers all three scripts with the script manager.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_golemagg

*Source:* boss_golemagg.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_golemaggAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset | method | Creature.Main/Respawn, EventMap/Reset, GridSearchers/GetCreatureListWithEntryInGrid#2, InstanceData/SetData, Unit.Main/DealDamage, Unit.Main/GetDeathState, Unit.Main/GetHealth, Unit.Main/IsAlive | — | — |
| Aggro | method | GridSearchers/GetCreatureListWithEntryInGrid#2, InstanceData/SetData, Object/GetObjectGuid | — | — |
| EnterEvadeMode | method | ScriptedAI/EnterEvadeMode | — | — |
| JustDied | method | InstanceData/SetData | — | — |
| KillAdds | method | Creature.Main/DisappearAndDie, Creature.Main/Respawn, Map.Main/GetCreature, WorldObject.Object/GetMap | — | — |
| DamageTaken | method | CreatureAI/DoCastSpellIfCan, EventMap/ScheduleEvent#2, Unit.Main/GetHealthPercent | — | — |
| UpdateAI | method | CreatureAI/DoMeleeAttackIfReady, EventMap/Update, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| ScheduleCombatEvents | method | EventMap/ScheduleEvent#2 | — | — |
| UpdateEvents | method | Creature.Main/SelectAttackingTarget, CreatureAI/DoCastSpellIfCan, EventMap/ExecuteEvent, EventMap/Repeat, Unit.Main/GetVictim | — | — |
| GetAI_boss_golemagg | function | — | — | — |
| mob_core_ragerAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset#2 | method | EventMap/Reset | — | — |
| Aggro#2 | method | — | — | — |
| DamageTaken#2 | method | CreatureAI/DoCastSpellIfCan, InstanceData/GetData, ScriptMgr/DoScriptText, Unit.Main/GetMaxHealth, Unit.Main/GetVictim, Unit.Main/HealthBelowPctDamaged, Unit.Main/SetHealth | — | — |
| UpdateAI#2 | method | CreatureAI/DoMeleeAttackIfReady, EventMap/Update, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| ScheduleCombatEvents#2 | method | EventMap/ScheduleEvent#2 | — | — |
| UpdateEvents#2 | method | Creature.Main/AI, CreatureAI/DoCastSpellIfCan, EventMap/ExecuteEvent, EventMap/Repeat, InstanceData/GetData64, Map.Main/GetCreature, ObjectGuid/ObjectGuid#5, Unit.Main/GetVictim, WorldObject.Object/GetDistance2d#3 | — | — |
| GetAI_mob_core_rager | function | — | — | — |
| OnBeforeApply | method | Aura/GetEffIndex, Aura/SetPeriodicTimer | — | — |
| OnPeriodicDummy | method | Aura/GetCaster, SpellCaster/CastSpell#2, Unit.Main/IsDead, Unit.Main/IsInCombat, WorldObject.Object/GetCreatureListWithEntryInGrid | — | — |
| GetScript_GolemaggsTrust | function | — | — | — |
| AddSC_boss_golemagg | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
