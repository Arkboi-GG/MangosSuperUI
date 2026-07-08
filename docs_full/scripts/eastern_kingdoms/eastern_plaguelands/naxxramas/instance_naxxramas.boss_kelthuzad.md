<!-- provenance: boundary-bleed -->
# instance_naxxramas.boss_kelthuzad

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# boss_kelthuzad.cpp / naxxramas.h (Partial)

## Purpose & Responsibilities

This translation unit implements the artificial intelligence (AI) and encounter logic for **Kel'Thuzad**, the final boss of the Naxxramas raid instance in *World of Warcraft*. It provides the AI for Kel'Thuzad himself, his summoned minions (Frozen Soldiers, Unstoppable Abominations, Soul Weavers, Guardians of Icecrown, and Shadow Fissures), and specific spell behaviors associated with the encounter.

The encounter is divided into three distinct phases:
1.  **Phase 1 (The Summoning):** Kel'Thuzad is immune to player attacks. He channels a visual spell while summoning waves of undead minions (Skeletons, Abominations, Soul Weavers) from seven alcoves surrounding the chamber. Players must defeat these adds to reach Kel'Thuzad.
2.  **Phase 2 (Direct Combat):** Once the initial wave of adds is cleared (or after a timeout), Kel'Thuzad becomes vulnerable. He casts single-target and area-of-effect spells (Frost Bolt, Shadow Fissure, Mana Detonation, Frost Blast) and uses "Chains of Kel'Thuzad" to charm players.
3.  **Phase 3 (Guardians):** At 40% health, Kel'Thuzad summons five Guardians of Icecrown through window portals. These guardians assist in combat and have a mechanic where they dispel shackles from other guardians if too many are charmed.

The unit also contains a partial implementation of the `instance_naxxramas` script manager class (`OnKTAreaTrigger`, `GetChamberCenterCoords`) and defines the data structures and enums shared across the Naxxramas scripts in `naxxramas.h`. Note that members such as `JustDied`, `JustReachedHome`, `UpdateAI`, `Aggro`, and `Save` declared in `naxxramas.h` are implemented in other translation units (siblings of this partial) and are not described here as part of this unit's behavior.

## Member-by-Member Behavior

### Kel'Thuzad Boss AI (`boss_kelthuzadAI`)

**`boss_kelthuzadAI` (Constructor)**
Initializes the AI by casting the instance data pointer to `instance_naxxramas*` via `GetInstanceData`. It calls `Reset()` to set initial states and sets a high creature summon limit (240) to accommodate the large number of Phase 1 adds.

**`Reset`**
Resets Kel'Thuzad to full health and clears the event map. It initializes timers for enrage (19 minutes, though commented as WotLK-era), spawn counters, and phase flags. It removes the visual channel aura and sets Kel'Thuzad as immune to players and unselectable. It calls `EvadeAllGuardians()` to ensure any lingering guardians from a previous attempt are removed. If the pull portal GameObject hasn't been created yet, it summons `GO_HUB_PORTAL` at the center coordinates, scales it up, and forces a map update to reflect the scale change.

**`StartEncounter`**
Triggered when players enter the area trigger. It sets the encounter state to `IN_PROGRESS` in the instance data. It plays the summoning speech and starts the visual channel. It schedules events for despawning the portal (7s), putting Kel'Thuzad in combat state (20s), and starting the Phase 2 intro (5m 20s). It pre-schedules all Abomination and Soul Weaver spawns using fixed millisecond delays defined in `abominationSpawnMs` and `soulweaverSpawnMs`. It then spawns the initial wave of Frozen Soldiers, Abominations, and Soul Weavers at predefined coordinates (`alcoves`, `abomPos`, `soulweaverPos`) with manual despawn timers.

**`UpdateP1`**
Handles the logic for Phase 1. It processes scheduled events:
-   `EVENT_DESPAWN_PORTAL`: Deletes the central portal GameObject.
-   `EVENT_PUT_IN_COMBAT`: Sets Kel'Thuzad's internal combat state without engaging players yet.
-   `EVENT_SKELETON`: Spawns a Frozen Soldier if fewer than 120 have been spawned. The respawn timer decreases as more skeletons are spawned, accelerating the pressure.
-   `EVENT_ABOMINATION` / `EVENT_SOUL_WEAVER`: Spawns the respective adds based on the pre-scheduled timers.
-   `EVENT_PHASE_TWO_INTRO`: Triggers the transition to Phase 2. It ensures the minimum number of Abominations (14) and Soul Weavers (14) are spawned. It despawns all intro creatures (Frozen Soldiers) via `DespawnAllIntroCreatures()`, removes the channel aura, and schedules the actual engagement (`EVENT_PHASE_TWO_START`) after 15 seconds.

**`SpawnAndSendP1Creature`**
Helper function to spawn a Phase 1 add. It selects a random alcove, calculates a spawn angle towards a random player target, and summons the creature. It immediately puts the add in combat and calls `ActualAttack` on the add's AI to force it to attack the selected player.

**`UpdateP2P3`**
Handles Phases 2 and 3. It tracks time since last major spells to prevent overlapping casts.
-   **Phase 3 Trigger:** If health drops below 40% and Phase 3 hasn't started, it triggers the Guardian summon sequence.
-   `EVENT_REQUEST_REPLY`: Plays the Lich King's response speech.
-   `EVENT_SUMMON_GUARDIAN`: Spawns Guardians one by one every 7 seconds at window portal locations. It registers them in the `guardians` vector for tracking.
-   `EVENT_FROSTBOLT_VOLLEY`, `EVENT_FROST_BLAST`, `EVENT_SHADOW_FISSURE`, `EVENT_DETONATE_MANA`, `EVENT_CHAINS`: Implements the boss's spell rotation. These events check cooldowns and conditions (e.g., not casting another spell) before executing. `DoChains` is called separately to handle the charm spell logic.
-   It ends by calling `DoMeleeAttackIfReady`.

**`DoChains`**
Attempts to cast `SPELL_CHAINS_OF_KELTHUZAD`. If successful, it resets threat (so the boss doesn't get pulled away by the charmed player's aggro) and plays a speech. It reschedules itself for 60–75 seconds.

**`UpdateAI`**
The main tick function. It checks for enrage (casting Berserk if the timer expires). It delegates to `UpdateP1` if Kel'Thuzad is still immune, or `UpdateP2P3` otherwise. It also handles kill talk timers and checks for enemy players to establish combat if necessary.

**`JustDied`**
Plays the death speech, sets the encounter status to `DONE` in the instance data, and evades all remaining guardians.

**`JustReachedHome`**
Called when the boss resets (evades). It sets the encounter to `NOT_STARTED`, closes the window portals via `ToggleKelThuzadWindows`, despawns intro creatures, and evades guardians.

**`EvadeAllGuardians`**
Iterates through the tracked `guardians` vector and calls `EnterEvadeMode` on each, effectively removing them from combat.

**`DespawnAllIntroCreatures`**
Iterates through the `p1_adds` vector (containing GUIDs of Frozen Soldiers) and unsuns them.

**`CheckForEnemyPlayers`**
Used in Phase 1 to detect players within 75 yards. It filters out Game Masters and establishes combat/threat with valid targets.

**`Aggro` / `AttackStart`**
Overridden to prevent aggression while Kel'Thuzad is immune (`UNIT_FLAG_IMMUNE_TO_PLAYER`).

**`KilledUnit`**
Plays a random kill speech with a 5-second cooldown.

**`SpellHit`**
Currently empty in the boss AI; spell handling is done via specific spell scripts or minion AI.

**`MoveInLineOfSight`**
Empty override to prevent accidental pulls during immunity.

### Minion AIs

**`kt_p1AddAI` (Base Class for Phase 1 Adds)**
A base AI class for Phase 1 minions (Soldiers, Abominations, Soul Weavers).
-   **`kt_p1AddAI` (Constructor):** Sets `SetNoSearchAssistance(true)` to prevent assists from triggering unintended aggro chains.
-   **`ActualAttack`:** Forces the creature to attack a specific target, adding threat and calling the base `AttackStart`.
-   **`Aggro#2`:** Empty override to prevent natural aggro.
-   **`AttackStart#2`:** Only allows attack if `hasAggroed` is true (set by `ActualAttack`) or if the target is very close (< 30 yards). This prevents adds from pulling players from afar unless explicitly targeted or very close.
-   **`MoveInLineOfSight#2`:** Similar logic to `AttackStart`, preventing LoS pulls unless already aggroed or very close.
-   **`SpellHit`:** If hit by a spell and not yet aggroed, it attacks the caster.

**`mob_soldierAI` (Frozen Soldier)**
-   **`mob_soldierAI` (Constructor):** Initializes the AI.
-   **`Reset#5`:** Sets health to 2000.
-   **`UpdateAI#5`:** Implements a "nearest melee" logic. If the current victim is out of melee range, it searches for the nearest player in melee range and switches targets. This prevents melee DPS from being ignored while casters hold aggro.

**`mob_abomAI` (Unstoppable Abomination)**
-   **`mob_abomAI` (Constructor):** Initializes the AI.
-   **`Reset#2`:** Sets health to 90,000 and initializes Mortal Wound timer.
-   **`UpdateAI#2`:** Casts `SPELL_MORTAL_WOUND` on its victim every 7.5 seconds if in melee range. Also performs melee attacks.

**`mob_soulweaverAI` (Soul Weaver)**
-   **`mob_soulweaverAI` (Constructor):** Initializes the AI.
-   **`Reset#6`:** Sets health to 70,000.
-   **`UpdateAI#6`:** Same "nearest melee" target switching logic as `mob_soldierAI`.

**`mob_guardian_icecrownAI` (Guardian of Icecrown)**
-   **`mob_guardian_icecrownAI` (Constructor):** Initializes instance data.
-   **`Reset#3`:** Sets Blood Tap timer to 15 seconds.
-   **`UpdateAI#3`:** Casts `SPELL_BLOOD_TAP` on victim every 15 seconds and performs melee attacks.
-   **`SpellHit#2`:** Checks if hit by a shackle spell (IDs 9484, 9485, 10955). If so, it counts how many Guardians in a 130-yard radius are shackled. If more than 3 are shackled, it plays a speech and removes the shackle aura from all nearby Guardians.
-   **`DispellShackle`:** Helper to remove specific shackle auras.
-   **`JustReachedHome#2`:** Deletes the creature immediately upon evade.

**`mob_shadow_fissureAI` (Shadow Fissure)**
-   **`mob_shadow_fissureAI` (Constructor):** Initializes timer.
-   **`Reset#4`:** Initializes timer and cast flag.
-   **`Aggro#3` / `AttackStart#3` / `MoveInLineOfSight#3`:** Empty overrides to prevent aggro.
-   **`UpdateAI#4`:** After a 3-second delay, casts `SPELL_VOID_BLAST` (an AoE explosion) and then despawns after 2.25 seconds.

### Spell Scripts

**`KelThuzadVoidBlastScript`**
-   **`OnEffectExecute`:** Checks if the target of Void Blast has the `Chains of Kel'Thuzad` aura (ID 28410). If so, it sets damage to 0. This prevents the boss from damaging players he has charmed.

**`ChainsOfKelThuzadAuraScript`**
-   **`OnAfterApply`:** When the charm aura is applied to a player, it enables `positiveSpells` in the player's AI. This allows the charmed player to heal and buff other players, simulating the "friendly fire" aspect of the charm.

### Instance Integration (`instance_naxxramas` Partial)

**`OnKTAreaTrigger`**
Called by the instance script when a player hits the Kel'Thuzad area trigger. If the encounter is `NOT_STARTED`, it retrieves the Kel'Thuzad creature and calls `StartEncounter()` on its AI.

**`GetChamberCenterCoords`**
Retrieves the center coordinates of the Kel'Thuzad chamber, stored in the instance data.

### Registration Functions

**`GetAI_*`**
Factory functions that return new instances of the respective AI classes.

**`AddSC_boss_kelthuzad`**
Registers all scripts (Boss, Adds, Spells) with the Script Manager.

## Cross-Unit Boundaries

*   **`instance_naxxramas` (naxxramas.cpp):**
    *   `boss_kelthuzadAI` calls `SetData`, `GetData`, `ToggleKelThuzadWindows`, `DoUseDoorOrButton`, `GetCreature`, `GetGameObject`, and `DoOrSimulateScriptTextForThisInstance` to manage encounter state, visual effects, and object interactions.
    *   `instance_naxxramas::OnKTAreaTrigger` calls `boss_kelthuzadAI::StartEncounter` to begin the fight.
*   **`ScriptedAI` / `CreatureAI` / `Unit` / `Creature` (Core Framework):**
    *   All AI classes inherit from `ScriptedAI` or `CreatureAI` and use methods like `DoCastSpellIfCan`, `SelectAttackingTarget`, `SetInCombatWith`, `AddThreat`, etc., to interact with the game world.
*   **`EventMap` (Utilities):**
    *   `boss_kelthuzadAI` uses `EventMap` to schedule and execute timed events for spells and spawns.
*   **`ScriptMgr` (Scripting System):**
    *   Used to play speeches (`DoScriptText`) and register scripts (`RegisterSelf`).
*   **`shared_Util` (Utilities):**
    *   Uses `urand` and `frand` for random number generation in spawns and spell targeting.

## Data Model

This unit does not directly query or modify database tables. It relies on the `instance_naxxramas` class (defined in `naxxramas.h` and implemented elsewhere) to manage persistent encounter state via `SetData`/`GetData`, which typically maps to the `instance` table in the database. The AI logic itself is entirely runtime-based.

## Notable Implementation Details

*   **Phase 1 Aggro Control:** The `kt_p1AddAI` class carefully controls aggro. Adds do not aggro naturally (`Aggro#2` is empty). They only attack if explicitly told to via `ActualAttack` (called by `SpawnAndSendP1Creature`) or if a player is within 30 yards. This prevents accidental pulls from the center of the room.
*   **Skeleton Spawn Acceleration:** In `UpdateP1`, the skeleton spawn timer decreases linearly (`3750 - 25 * numSkeletons`) down to a minimum of 2000ms. This creates increasing pressure as Phase 1 progresses.
*   **Guardian Shackle Mechanic:** The `mob_guardian_icecrownAI` implements a unique mechanic where Guardians check for shackles on nearby peers. If >3 are shackled, they all break free. This requires scanning a 130-yard grid for other Guardians.
*   **Charmed Player Healing:** The `ChainsOfKelThuzadAuraScript` modifies the player's AI to allow positive spells. This is a critical detail for the encounter's design, allowing charmed players to contribute to the raid's survival.
*   **Portal Scale Hack:** In `Reset`, the code summons the portal, sets its scale, and then removes/adds it to the map. This is noted as a workaround to ensure the scale change is visually updated.
*   **Hardcoded Timers:** Many timers (e.g., Phase 2 intro delay, spell cooldowns) are hardcoded or estimated based on community videos, as indicated by comments like "todo: this is guesswork" or references to YouTube videos.
*   **Enrage Timer:** The enrage timer is set to 19 minutes, matching WotLK behavior, despite comments noting uncertainty about Vanilla specifics.

## Member Reference

**`kt_p1AddAI`**: Constructor for the base Phase 1 add AI. Sets no-search-assistance and initializes aggro state.
**`ActualAttack`**: Forces a Phase 1 add to attack a specific target, setting the `hasAggroed` flag.
**`Aggro#2`**: Empty override in `kt_p1AddAI` to prevent natural aggro.
**`AttackStart#2`**: Override in `kt_p1AddAI` that only allows attack if already aggroed or target is very close.
**`MoveInLineOfSight#2`**: Override in `kt_p1AddAI` that restricts LoS pulls to close range or existing aggro.
**`SpellHit`**: Override in `kt_p1AddAI` that causes the add to attack the spell caster if not already aggroed.
**`boss_kelthuzadAI`**: Constructor for the boss AI. Initializes instance data, resets state, and sets summon limits.
**`Reset`**: Resets Kel'Thuzad's health, timers, and flags. Summons the central portal if missing. Evades guardians.
**`~instance_naxxramas`**: Destructor for the instance class (empty).
**`Save`**: Returns the serialized instance data string (implementation in other file).
**`GetChamberCenterCoords`**: Retrieves the stored center coordinates of the Kel'Thuzad chamber.
**`KilledUnit`**: Plays a random kill speech with a cooldown.
**`JustDied`**: Plays death speech, sets encounter to DONE, and evades guardians.
**`MoveInLineOfSight`**: Empty override in `boss_kelthuzadAI` to prevent accidental pulls.
**`AttackStart`**: Override in `boss_kelthuzadAI` that prevents attack if immune.
**`Aggro`**: Override in `boss_kelthuzadAI` that prevents aggro if immune.
**`CheckForEnemyPlayers`**: Finds players within 75 yards, filters GMs, and establishes combat/threat.
**`JustReachedHome`**: Handles boss reset: sets encounter to NOT_STARTED, closes windows, despawns adds, evades guardians.
**`EvadeAllGuardians`**: Iterates tracked guardians and forces them to evade.
**`DespawnAllIntroCreatures`**: Unsuns all tracked Frozen Soldiers from Phase 1.
**`StartEncounter`**: Initiates the fight: sets state, plays speech, schedules events, spawns initial adds.
**`SpawnAndSendP1Creature`**: Spawns a Phase 1 add at a random alcove, targeting a random player.
**`UpdateP1`**: Manages Phase 1 events: spawning skeletons/aboms/weavers, despawning portal, transitioning to Phase 2.
**`DoChains`**: Casts Chains of Kel'Thuzad, resets threat, plays speech, and reschedules.
**`UpdateP2P3`**: Manages Phases 2 & 3 events: spell rotations, Guardian summons, and melee attacks.
**`UpdateAI`**: Main tick function. Handles enrage, delegates to P1/P2P3 updates, and manages kill talks.
**`mob_abomAI`**: Constructor for Abomination AI. Inherits from `kt_p1AddAI`.
**`Reset#2`**: Resets Abomination health to 90,000 and initializes Mortal Wound timer.
**`UpdateAI#2`**: Abomination AI tick. Casts Mortal Wound and performs melee attacks.
**`mob_soldierAI`**: Constructor for Frozen Soldier AI. Inherits from `kt_p1AddAI`.
**`Reset#5`**: Resets Soldier health to 2,000.
**`UpdateAI#5`**: Soldier AI tick. Switches to nearest melee target if current victim is out of range.
**`mob_soulweaverAI`**: Constructor for Soul Weaver AI. Inherits from `kt_p1AddAI`.
**`Reset#6`**: Resets Soul Weaver health to 70,000.
**`UpdateAI#6`**: Soul Weaver AI tick. Same target-switching logic as Soldier.
**`mob_guardian_icecrownAI`**: Constructor for Guardian AI. Initializes instance data.
**`Reset#3`**: Initializes Blood Tap timer.
**`JustReachedHome#2`**: Deletes the Guardian immediately upon evade.
**`DispellShackle`**: Removes specific shackle auras from a creature.
**`SpellHit#2`**: Checks for shackle spells. If >3 Guardians are shackled, dispels all.
**`UpdateAI#3`**: Guardian AI tick. Casts Blood Tap and performs melee attacks.
**`mob_shadow_fissureAI`**: Constructor for Shadow Fissure AI.
**`Reset#4`**: Initializes timer and cast flag.
**`Aggro#3`**: Empty override.
**`AttackStart#3`**: Empty override.
**`MoveInLineOfSight#3`**: Empty override.
**`UpdateAI#4`**: Shadow Fissure AI tick. Delays 3s, casts Void Blast, then despawns.
**`GetAI_boss_kelthuzad`**: Factory function for Kel'Thuzad AI.
**`GetAI_mob_abom`**: Factory function for Abomination AI.
**`GetAI_mob_soldier`**: Factory function for Soldier AI.
**`GetAI_mob_soulweaver`**: Factory function for Soul Weaver AI.
**`GetAI_mob_guardian_icecrown`**: Factory function for Guardian AI.
**`GetAI_mob_shadow_fissure`**: Factory function for Shadow Fissure AI.
**`OnKTAreaTrigger`**: Instance method triggered by area trigger. Starts the encounter if not already started.
**`OnEffectExecute`**: Spell script for Void Blast. Nullifies damage if target is charmed.
**`GetScript_KelThuzadVoidBlast`**: Factory function for Void Blast spell script.
**`OnAfterApply`**: Aura script for Chains of Kel'Thuzad. Enables positive spells for charmed players.
**`GetScript_ChainsOfKelThuzad`**: Factory function for Chains aura script.
**`AddSC_boss_kelthuzad`**: Registers all scripts in this unit with the Script Manager.

---

<!-- machine-true, projected from graph.json -->

## Map — instance_naxxramas.boss_kelthuzad

*Source:* boss_kelthuzad.cpp, naxxramas.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| kt_p1AddAI | ctor | Creature.Main/SetNoSearchAssistance, ScriptedAI/ScriptedAI | — | — |
| ActualAttack | method | CreatureAI/AttackStart, Unit.Main/AddThreat | — | — |
| Aggro#2 | method | — | — | — |
| AttackStart#2 | method | CreatureAI/AttackStart, WorldObject.Object/GetDistance2d#3 | — | — |
| MoveInLineOfSight#2 | method | BasicAI/MoveInLineOfSight, Unit.Main/IsHostileTo, WorldObject.Object/GetDistance2d#3 | — | — |
| SpellHit | method | Object/ToUnit | — | — |
| boss_kelthuzadAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData, WorldObject.Object/SetCreatureSummonLimit | — | — |
| Reset | method | EventMap/Reset, Object/GetObjectGuid, ObjectGuid/operator!, Unit.Main/GetMaxHealth, Unit.Main/RemoveAurasDueToSpell, Unit.Main/SetHealth, WorldObject.Object/SetFlag, WorldObject.Object/SetObjectScale, WorldObject.Object/SummonGameObject, ZoneScript/GetMap#2 | — | — |
| ~instance_naxxramas | dtor | — | — | — |
| Save | method | — | — | — |
| GetChamberCenterCoords | method | — | — | — |
| KilledUnit | method | ScriptMgr/DoScriptText, shared_Util/urand | — | — |
| JustDied | method | instance_naxxramas.Main/SetData, ScriptMgr/DoScriptText | — | — |
| MoveInLineOfSight | method | — | — | — |
| AttackStart | method | CreatureAI/AttackStart, Object/HasFlag | — | — |
| Aggro | method | Creature.Main/SetInCombatWithZone, Object/HasFlag | — | — |
| CheckForEnemyPlayers | method | Player.Main/IsGameMaster, Unit.Main/AddThreat, Unit.Main/SetInCombatWith, WorldObject.Object/GetAlivePlayerListInRange | — | — |
| JustReachedHome | method | instance_naxxramas.Main/SetData, instance_naxxramas.Main/ToggleKelThuzadWindows | — | — |
| EvadeAllGuardians | method | Creature.Main/AI, CreatureAI/EnterEvadeMode, ZoneScript/GetCreature | — | — |
| DespawnAllIntroCreatures | method | Map.Main/GetCreature, TemporarySummon/UnSummon | — | — |
| StartEncounter | method | Creature.Main/SetHomePosition, Creature.Main/SetWanderDistance, CreatureAI/DoCastAOE, EventMap/ScheduleEvent#2, EventMap/ScheduleEvent#3, instance_naxxramas.Main/SetData, instance_naxxramas.Main/ToggleKelThuzadWindows, Object/GetObjectGuid, ScriptedInstance/DoUseDoorOrButton, ScriptMgr/DoScriptText, shared_Util/frand, shared_Util/rand_norm, Unit.Main/GetMaxHealth, Unit.Main/SetHealth, WorldObject.Object/GetOrientation, WorldObject.Object/SummonCreature#2 | — | — |
| SpawnAndSendP1Creature | method | Creature.Main/AI, Creature.Main/SelectAttackingTarget, Creature.Main/SetInCombatWithZone, shared_Util/urand, WorldObject.Object/GetAngle#2, WorldObject.Object/SummonCreature#2 | — | — |
| UpdateP1 | method | Creature.Main/SelectAttackingTarget, Creature.Main/SetInCombatWithZone, EventMap/ExecuteEvent, EventMap/Repeat#3, EventMap/Reset, EventMap/ScheduleEvent#2, GameObject/Delete, instance_naxxramas.Main/GetData, Log.Main/Out, ObjectGuid/ObjectGuid#5, ScriptedAI/DoResetThreat, ScriptMgr/DoScriptText, shared_Util/urand, SpellCaster/InterruptNonMeleeSpells, Unit.Main/RemoveAurasDueToSpell, Unit.Main/SetInCombatState, WorldObject.Object/RemoveFlag, ZoneScript/GetGameObject | — | — |
| DoChains | method | CreatureAI/DoCastSpellIfCan, EventMap/Repeat, ScriptedAI/DoResetThreat, ScriptMgr/DoScriptText, shared_Util/urand | — | — |
| UpdateP2P3 | method | Creature.Main/AI, Creature.Main/SelectAttackingTarget, Creature.Main/SelectAttackingTarget#2, Creature.Main/SetInCombatWithZone, CreatureAI/AttackStart, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, EventMap/ExecuteEvent, EventMap/Repeat, EventMap/Repeat#3, EventMap/ScheduleEvent#2, instance_naxxramas.Main/ToggleKelThuzadWindows, Object/GetObjectGuid, ScriptedInstance/DoOrSimulateScriptTextForThisInstance, ScriptMgr/DoScriptText, shared_Util/urand, SpellCaster/IsNonMeleeSpellCasted, Unit.Main/GetHealthPercent, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget, WorldObject.Object/SummonCreature#2 | — | — |
| UpdateAI | method | EventMap/Update, instance_naxxramas.Main/GetData, instance_naxxramas.Main/HandleEvadeOutOfHome, Object/HasFlag, SpellCaster/CastSpell#2, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| mob_abomAI | ctor | — | — | — |
| Reset#2 | method | Unit.Main/SetHealth, Unit.Main/SetMaxHealth | — | — |
| UpdateAI#2 | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, Unit.Main/CanReachWithMeleeAutoAttack, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| mob_soldierAI | ctor | — | — | — |
| Reset#5 | method | Unit.Main/SetHealth, Unit.Main/SetMaxHealth | — | — |
| UpdateAI#5 | method | Creature.Main/SelectAttackingTarget, CreatureAI/AttackStart, CreatureAI/DoMeleeAttackIfReady, Unit.Main/CanReachWithMeleeAutoAttack, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| mob_soulweaverAI | ctor | — | — | — |
| Reset#6 | method | Unit.Main/SetHealth, Unit.Main/SetMaxHealth | — | — |
| UpdateAI#6 | method | Creature.Main/SelectAttackingTarget, CreatureAI/AttackStart, CreatureAI/DoMeleeAttackIfReady, Unit.Main/CanReachWithMeleeAutoAttack, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| mob_guardian_icecrownAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset#3 | method | — | — | — |
| JustReachedHome#2 | method | WorldObject.Object/DeleteLater | — | — |
| DispellShackle | method | Unit.Main/HasAura#2, Unit.Main/RemoveAurasDueToSpell | — | — |
| SpellHit#2 | method | GridSearchers/GetCreatureListWithEntryInGrid#2, ScriptedInstance/GetSingleCreatureFromStorage, ScriptMgr/DoScriptText, Unit.Main/HasAura#2 | — | — |
| UpdateAI#3 | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| mob_shadow_fissureAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#4 | method | — | — | — |
| Aggro#3 | method | — | — | — |
| AttackStart#3 | method | — | — | — |
| MoveInLineOfSight#3 | method | — | — | — |
| UpdateAI#4 | method | Creature.Main/ForcedDespawn, SpellCaster/CastSpell#2 | — | — |
| GetAI_boss_kelthuzad | function | — | — | — |
| GetAI_mob_abom | function | — | — | — |
| GetAI_mob_soldier | function | — | — | — |
| GetAI_mob_soulweaver | function | — | — | — |
| GetAI_mob_guardian_icecrown | function | — | — | — |
| GetAI_mob_shadow_fissure | function | — | — | — |
| OnKTAreaTrigger | method | Creature.Main/AI, instance_naxxramas.Main/GetData, ScriptedInstance/GetSingleCreatureFromStorage | instance_naxxramas.Main/onNaxxramasAreaTrigger | — |
| OnEffectExecute | method | Spell.Main/GetUnitTarget, Unit.Main/HasAura#2 | — | — |
| GetScript_KelThuzadVoidBlast | function | — | — | — |
| OnAfterApply | method | Aura/GetEffIndex, Aura/GetTarget, Object/ToPlayer | — | — |
| GetScript_ChainsOfKelThuzad | function | — | — | — |
| AddSC_boss_kelthuzad | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |

---

<!-- verify: boundary-bleed | foreign: Aggro, instance_naxxramas, JustDied, JustReachedHome, UpdateAI -->
