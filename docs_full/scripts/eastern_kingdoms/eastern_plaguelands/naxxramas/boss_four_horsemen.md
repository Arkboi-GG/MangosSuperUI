# boss_four_horsemen

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# boss_four_horsemen

## Purpose & Responsibilities

This unit implements the artificial intelligence for the **Four Horsemen** encounter in the Naxxramas raid instance. It defines four distinct boss AI classes—`boss_lady_blaumeuxAI`, `boss_highlord_mograineAI`, `boss_thane_korthazzAI`, and `boss_sir_zeliekAI`—all of which inherit from a common base class, `boss_four_horsemen_shared`.

The primary responsibilities of this unit are:
1.  **Shared Mechanics:** Implementing mechanics common to all four bosses via the base class, including:
    *   **Shield Wall:** A defensive buff cast at 50% and 20% health thresholds.
    *   **Mark of the Horseman:** A stacking debuff that deals increasing damage and reduces threat upon reaching certain stack counts.
    *   **Spirit Summoning:** Upon death, each boss summons a controllable "spirit" version of themselves that continues to fight alongside the remaining bosses.
    *   **Aggro Synchronization:** Ensuring that when one horseman is engaged, the others also engage the same target.
2.  **Individual Abilities:** Implementing unique spells and behaviors for each horseman (e.g., Voidzone for Blaumeux, Meteor for Korthazz).
3.  **Encounter State Management:** Communicating with the `instance_naxxramas` script to track the encounter phase (Not Started, In Progress, Failed, Special/Success).
4.  **Large Aggro Radius:** Handling custom aggro detection logic to allow players to pull the bosses from a significant distance, bypassing standard visibility checks.

The unit does not interact with any database tables directly; all state is managed in-memory through the instance script and creature objects.

## Member-by-Member Behavior

### Shared Base Class (`boss_four_horsemen_shared`)

The base class handles the core loop and shared logic for all four horsemen and their spirits.

*   **Constructor (`boss_four_horsemen_shared`):** Initializes the AI, retrieves the `instance_naxxramas` data pointer, and determines if the current creature is a "spirit" (summoned ghost) or the original boss. If it is a spirit, combat movement is disabled.
*   **`AggroRadius`:** A custom method called periodically to check for players within a 74-yard radius. It bypasses standard LOS checks initially but requires LOS to initiate combat. If a victim is already present, it adds threat to nearby players to ensure they are pulled into the fight. This allows for "long pulls" typical of this encounter.
*   **`MoveInLineOfSight`:** Standard override to initiate combat if a hostile player enters a 75-yard range and is visible.
*   **`AttackStart`:** Delegates to the parent `ScriptedAI` unless the creature is a spirit, in which case it does nothing (spirits do not independently start attacks).
*   **`Reset`:** Resets timers and flags. If the creature is a spirit, it roots itself. If it is the main boss, it searches for and deletes any existing spirit associated with it to clean up the zone.
*   **`Aggro`:** When the first horseman is aggroed, this method forces the other three horsemen (retrieved via `instance_naxxramas`) to attack the same target. It sets the instance data to `IN_PROGRESS`.
*   **`JustReachedHome`:** Sets the instance data to `FAIL` if the boss resets without dying.
*   **`JustDied`:** Casts the specific spirit summoning spell for that horseman and sets the instance data to `SPECIAL` (indicating a boss has died, progressing the encounter state).
*   **`SpellHitTarget`:** Handles the "Mark" mechanic. If the target has 2+ stacks of the Mark, it calculates damage based on stack count (250 for 2 stacks, 1000 for 3, 3000 for 4, etc.) and casts a custom spell effect. It also reduces the target's threat by 50% to prevent tank swaps from being too easy during high-damage ticks.
*   **`UpdateAI`:** The main update loop.
    *   **Summon Player:** If the boss is far from its victim, it casts `SPELL_SUMMON_PLAYER` to pull them closer.
    *   **Shield Wall:** Checks health percentages. At <50%, it casts Shield Wall once. At <20%, it casts it again, provided at least 30 seconds have passed since the first cast (preventing rapid double-casts).
    *   **Mark Timer:** Periodically casts the Mark spell on random targets. After casting, it iterates through the threat list and reduces threat by 50% for all units with positive threat.

### Lady Blaumeux (`boss_lady_blaumeuxAI`)

*   **`Reset`:** Calls base reset. If it is the spirit, it synchronizes its `m_uiMarkTimer` with the main boss to keep abilities in sync.
*   **`Aggro`:** Plays aggro text and schedules the first ability cast.
*   **`KilledUnit`:** Plays a random slay text with a cooldown.
*   **`JustDied`:** Plays death text and calls base `JustDied` (which summons the spirit).
*   **`UpdateAI`:**
    *   Calls `AggroRadius` to maintain large pull capability.
    *   **Voidzone:** Every ~12 seconds, selects a random player in LOS and summons a "Voidzone" creature at their location. The zone moves slowly and damages players standing in it.

### Highlord Mograine (`boss_highlord_mograineAI`)

*   **`Reset`:** Calls base reset. Synchronizes timer if spirit. Initializes `specialSayCooldown`.
*   **`Aggro`:** Schedules aggro text and casts `Righteous Fire` on himself.
*   **`KilledUnit`:** Plays random slay text.
*   **`JustDied`:** Plays death text and calls base `JustDied`.
*   **`SpellHitTarget`:** If hit by `Righteous Fire` (spell ID 28882), plays a special taunt text after a cooldown.
*   **`UpdateAI`:**
    *   Calls `AggroRadius`.
    *   Executes scheduled events (currently just aggro text).
    *   Performs melee attacks.

### Thane Korthazz (`boss_thane_korthazzAI`)

*   **`Reset`:** Calls base reset. Synchronizes timer if spirit.
*   **`Aggro`:** Schedules aggro text and first ability cast.
*   **`KilledUnit`:** Plays slay text.
*   **`JustDied`:** Plays death text and calls base `JustDied`.
*   **`UpdateAI`:**
    *   Calls `AggroRadius`.
    *   **Meteor:** Every 12–15 seconds, selects a random player in LOS and casts `Meteor` on them.

### Sir Zeliek (`boss_sir_zeliekAI`)

*   **`Reset`:** Calls base reset. Synchronizes timer if spirit.
*   **`Aggro`:** Schedules aggro text and first ability cast.
*   **`KilledUnit`:** Plays slay text.
*   **`JustDied`:** Plays death text and calls base `JustDied`.
*   **`UpdateAI`:**
    *   Calls `AggroRadius`.
    *   **Holy Wrath:** Every 10–14 seconds, selects a random player in LOS and casts `Holy Wrath`.

### Registration

*   **`GetAI_*` functions:** Factory functions that instantiate the correct AI class for each creature entry.
*   **`AddSC_boss_four_horsemen`:** Registers the four scripts with the global script manager.

## Cross-Unit Boundaries

*   **`instance_naxxramas.Main`:**
    *   **Called by:** `boss_four_horsemen_shared` (Aggro, Reset, JustReachedHome, JustDied) and individual AIs (Reset).
    *   **Purpose:** Retrieves instance state (`GetData`), sets encounter progress (`SetData`), and fetches references to the other three horsemen (`GetSingleCreatureFromStorage`) to synchronize aggro. It also provides the `Map` object for height calculations (`GetMap`).
*   **`Creature.Main` / `Unit.Main`:**
    *   **Called by:** All AI methods.
    *   **Purpose:** Core entity manipulation: setting combat states, adding threat, selecting targets, checking distances/LOS, and casting spells.
*   **`EventMap`:**
    *   **Called by:** All AI `UpdateAI` and `Aggro` methods.
    *   **Purpose:** Manages timed events (aggro texts, ability casts) using a scheduler pattern.
*   **`ScriptMgr`:**
    *   **Called by:** All AI methods for text output.
    *   **Purpose:** Triggers spoken lines (`DoScriptText`) and registers the scripts (`RegisterSelf`).
*   **`ThreatManager`:**
    *   **Called by:** `boss_four_horsemen_shared::UpdateAI`.
    *   **Purpose:** Modifies threat percentages to implement the Mark mechanic's threat reduction.
*   **`WorldObject.Object`:**
    *   **Called by:** Various methods.
    *   **Purpose:** Position queries, distance checks, and summoning creatures (`SummonCreature`).

## Data Model

This unit does not access any database tables directly. All data is transient, stored in memory within the `Creature` objects and the `instance_naxxramas` script instance.

## Notable Implementation Details

1.  **Spirit Synchronization:** When a boss dies, its spirit is summoned. The spirit's `Reset` method explicitly copies the `m_uiMarkTimer` from the main boss. This ensures that the spirit's Mark ability stays roughly in sync with the living bosses, preventing desynchronized damage spikes.
2.  **Shield Wall Cooldown Logic:** The code enforces a 30-second minimum between the two Shield Wall casts. If a horseman drops from 50% to 20% health in less than 30 seconds, the second Shield Wall will not trigger. This is a deliberate balance constraint.
3.  **Threat Reduction on Mark:** The `SpellHitTarget` method in the base class reduces threat by 50% for all units on the threat list when the Mark is cast. This is a significant mechanic intended to force tanks to manage threat carefully, as the Mark deals substantial damage to non-tanks.
4.  **Large Aggro Radius:** The `AggroRadius` method manually iterates over all players on the map within 74 yards. This bypasses the engine's standard aggro radius, allowing players to pull the bosses from outside the room or from a distance, which is historically accurate for this encounter.
5.  **Voidzone Height Calculation:** Lady Blaumeux's `UpdateAI` calculates the Z-height for the Voidzone spawn by querying the map height at the target's X/Y coordinates and adding a small offset. It clamps the minimum height to 241.35f to prevent spawning below the floor.
6.  **Hardcoded Spell IDs:** All spell and NPC IDs are hardcoded in the `FourHorsemenData` enum. There is no dynamic lookup.
7.  **No Database Persistence:** Encounter state is purely in-memory. If the server restarts, the encounter state is lost (handled by the instance system, not this script).

## Member Reference

**boss_four_horsemen_shared**
Constructor for the shared base AI. Initializes instance data, determines if the creature is a spirit, and disables combat movement for spirits.

**AggroRadius**
Custom method to check for players within 74 yards. Initiates combat or adds threat to nearby players to facilitate large-radius pulls.

**MoveInLineOfSight**
Overrides standard LOS check to initiate combat if a hostile player is within 75 yards and visible.

**AttackStart**
Delegates to parent `ScriptedAI` unless the creature is a spirit, in which case it returns immediately.

**Reset**
Resets timers and flags. Deletes any existing spirit if the main boss is resetting. Roots the spirit if it is resetting.

**Aggro**
Forces all other horsemen to attack the same target. Sets instance state to `IN_PROGRESS`.

**JustReachedHome**
Sets instance state to `FAIL`.

**JustDied**
Summons the boss's spirit and sets instance state to `SPECIAL`.

**SpellHitTarget**
Handles the Mark mechanic: calculates damage based on stacks, casts custom spell, and reduces threat by 50% for all units.

**UpdateAI**
Main update loop. Handles Shield Wall casts, Mark casting, and summoning players if they are too far away.

**boss_lady_blaumeuxAI**
Constructor for Lady Blaumeux's AI.

**Reset#3**
Overrides base reset. Synchronizes Mark timer with main boss if this is the spirit.

**Aggro#3**
Plays aggro text and schedules the first Voidzone cast.

**KilledUnit#2**
Plays a slay text with a cooldown.

**JustDied#3**
Plays death text and calls base `JustDied`.

**SpellHitTarget#3**
Calls base `SpellHitTarget`.

**UpdateAI#3**
Calls `AggroRadius`. Executes events: spawns Voidzone at a random player's location every ~12 seconds. Performs melee attacks.

**GetAI_boss_lady_blaumeux**
Factory function to create `boss_lady_blaumeuxAI`.

**boss_highlord_mograineAI**
Constructor for Highlord Mograine's AI.

**Reset#2**
Overrides base reset. Synchronizes Mark timer if spirit. Initializes special say cooldown.

**Aggro#2**
Schedules aggro text and casts `Righteous Fire`.

**KilledUnit**
Plays a random slay text with a cooldown.

**JustDied#2**
Plays death text and calls base `JustDied`.

**SpellHitTarget#2**
Calls base `SpellHitTarget`. Plays a special taunt if hit by `Righteous Fire`.

**UpdateAI#2**
Calls `AggroRadius`. Executes events (aggro text). Performs melee attacks.

**GetAI_boss_highlord_mograine**
Factory function to create `boss_highlord_mograineAI`.

**boss_thane_korthazzAI**
Constructor for Thane Korthazz's AI.

**Reset#5**
Overrides base reset. Synchronizes Mark timer if spirit.

**Aggro#5**
Schedules aggro text and first Meteor cast.

**KilledUnit#4**
Plays a slay text with a cooldown.

**JustDied#5**
Plays death text and calls base `JustDied`.

**SpellHitTarget#5**
Calls base `SpellHitTarget`.

**UpdateAI#5**
Calls `AggroRadius`. Executes events: casts Meteor on a random player every 12–15 seconds. Performs melee attacks.

**GetAI_boss_thane_korthazz**
Factory function to create `boss_thane_korthazzAI`.

**boss_sir_zeliekAI**
Constructor for Sir Zeliek's AI.

**Reset#4**
Overrides base reset. Synchronizes Mark timer if spirit.

**Aggro#4**
Schedules aggro text and first Holy Wrath cast.

**KilledUnit#3**
Plays a slay text with a cooldown.

**JustDied#4**
Plays death text and calls base `JustDied`.

**SpellHitTarget#4**
Calls base `SpellHitTarget`.

**UpdateAI#4**
Calls `AggroRadius`. Executes events: casts Holy Wrath on a random player every 10–14 seconds. Performs melee attacks.

**GetAI_boss_sir_zeliek**
Factory function to create `boss_sir_zeliekAI`.

**AddSC_boss_four_horsemen**
Registers the four horsemen scripts with the global script manager.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_four_horsemen

*Source:* boss_four_horsemen.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_four_horsemen_shared | ctor | CreatureAI/SetCombatMovement, Log.Main/Out, Object/GetEntry, ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| AggroRadius | method | Creature.Main/CanInitiateAttack, instance_naxxramas.Main/GetData, Map.Main/GetPlayers, Map.Main/IsDungeon, Unit.Main/AddThreat, Unit.Main/GetVictim, Unit.Main/IsHostileTo, Unit.Main/IsInAccessablePlaceFor, Unit.Main/IsTargetableBy, Unit.Main/IsVisibleForOrDetect, Unit.Main/SetInCombatWith, WorldObject.Object/GetDistance3dToCenter#3, WorldObject.Object/GetMap, WorldObject.Object/GetMapId, WorldObject.Object/IsWithinLOSInMap | — | — |
| MoveInLineOfSight | method | Creature.Main/CanInitiateAttack, Map.Main/IsDungeon, Unit.Main/AddThreat, Unit.Main/GetVictim, Unit.Main/IsHostileTo, Unit.Main/IsInAccessablePlaceFor, Unit.Main/IsTargetableBy, Unit.Main/SetInCombatWith, WorldObject.Object/GetMap, WorldObject.Object/GetMapId, WorldObject.Object/IsWithinDistInMap, WorldObject.Object/IsWithinLOSInMap | — | — |
| AttackStart | method | CreatureAI/AttackStart | — | — |
| Reset | method | Creature.Main/SetInCombatWithZone, EventMap/Reset, GridSearchers/GetClosestCreatureWithEntry, Object/GetEntry, Unit.Main/AddUnitState, WorldObject.Object/DeleteLater, WorldObject.Object/GetMapId | — | — |
| Aggro | method | Creature.Main/AI, CreatureAI/AttackStart, instance_naxxramas.Main/GetData, instance_naxxramas.Main/SetData, Object/GetEntry, ScriptedInstance/GetSingleCreatureFromStorage, WorldObject.Object/GetMapId | — | — |
| JustReachedHome | method | instance_naxxramas.Main/SetData | — | — |
| JustDied | method | instance_naxxramas.Main/SetData, SpellCaster/CastSpell#2 | — | — |
| SpellHitTarget | method | Object/GetObjectGuid, SpellAuraHolder/GetAuraByEffectIndex, SpellAuraHolder/GetStackAmount, SpellCaster/CastCustomSpell#2, Unit.Main/GetSpellAuraHolder#2 | — | — |
| UpdateAI | method | CreatureAI/DoCastSpellIfCan, EventMap/Update, SpellCaster/CastSpell#2, ThreatManager/getThreat, ThreatManager/getThreatList, ThreatManager/modifyThreatPercent#2, Unit.Main/GetHealthPercent, Unit.Main/GetThreatManager, Unit.Main/GetVictim, WorldObject.Object/GetMapId, WorldObject.Object/IsWithinDistInMap | — | — |
| boss_lady_blaumeuxAI | ctor | — | — | — |
| Reset#3 | method | Creature.Main/AI, ScriptedInstance/GetSingleCreatureFromStorage | — | — |
| Aggro#3 | method | EventMap/ScheduleEvent#2, ScriptMgr/DoScriptText | — | — |
| KilledUnit#2 | method | ScriptMgr/DoScriptText | — | — |
| JustDied#3 | method | ScriptMgr/DoScriptText | — | — |
| SpellHitTarget#3 | method | — | — | — |
| UpdateAI#3 | method | Creature.Main/SelectAttackingTarget#2, Creature.Main/SetWanderDistance, Creature.MotionMaster/MoveRandom, CreatureAI/DoMeleeAttackIfReady, EventMap/ExecuteEvent, EventMap/Repeat, instance_naxxramas.Main/HandleEvadeOutOfHome, Map.Main/GetHeight, ScriptMgr/DoScriptText, Unit.Main/GetMotionMaster, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget, Unit.Main/SetSpeedRate, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/SummonCreature#2, ZoneScript/GetMap#2 | — | — |
| GetAI_boss_lady_blaumeux | function | — | — | — |
| boss_highlord_mograineAI | ctor | — | — | — |
| Reset#2 | method | Creature.Main/AI, ScriptedInstance/GetSingleCreatureFromStorage | — | — |
| Aggro#2 | method | EventMap/ScheduleEvent#2, SpellCaster/CastSpell#2 | — | — |
| KilledUnit | method | ScriptMgr/DoScriptText, shared_Util/urand, WorldObject.Object/GetMapId | — | — |
| JustDied#2 | method | ScriptMgr/DoScriptText | — | — |
| SpellHitTarget#2 | method | ScriptMgr/DoScriptText | — | — |
| UpdateAI#2 | method | CreatureAI/DoMeleeAttackIfReady, EventMap/ExecuteEvent, instance_naxxramas.Main/HandleEvadeOutOfHome, ScriptMgr/DoScriptText, shared_Util/urand, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_boss_highlord_mograine | function | — | — | — |
| boss_thane_korthazzAI | ctor | — | — | — |
| Reset#5 | method | Creature.Main/AI, ScriptedInstance/GetSingleCreatureFromStorage | — | — |
| Aggro#5 | method | EventMap/ScheduleEvent#2 | — | — |
| KilledUnit#4 | method | ScriptMgr/DoScriptText | — | — |
| JustDied#5 | method | ScriptMgr/DoScriptText | — | — |
| SpellHitTarget#5 | method | — | — | — |
| UpdateAI#5 | method | Creature.Main/SelectAttackingTarget#2, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, EventMap/ExecuteEvent, EventMap/Repeat, instance_naxxramas.Main/HandleEvadeOutOfHome, ScriptMgr/DoScriptText, shared_Util/urand, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_boss_thane_korthazz | function | — | — | — |
| boss_sir_zeliekAI | ctor | — | — | — |
| Reset#4 | method | Creature.Main/AI, ScriptedInstance/GetSingleCreatureFromStorage | — | — |
| Aggro#4 | method | EventMap/ScheduleEvent#2 | — | — |
| KilledUnit#3 | method | ScriptMgr/DoScriptText | — | — |
| JustDied#4 | method | ScriptMgr/DoScriptText | — | — |
| SpellHitTarget#4 | method | — | — | — |
| UpdateAI#4 | method | Creature.Main/SelectAttackingTarget#2, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, EventMap/ExecuteEvent, EventMap/Repeat, instance_naxxramas.Main/HandleEvadeOutOfHome, ScriptMgr/DoScriptText, shared_Util/urand, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_boss_sir_zeliek | function | — | — | — |
| AddSC_boss_four_horsemen | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
