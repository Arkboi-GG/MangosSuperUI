# boss_gluth

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# boss_gluth

**Purpose & Responsibilities**
This unit implements the AI and associated scripts for **Gluth**, a boss encounter in the Naxxramas raid instance, and his summoned minions, the **Zombie Chow**. The primary responsibility of `boss_gluthAI` is to manage Gluth’s combat rotation, including periodic spells (Mortal Wound, Frenzy, Terrifying Roar), summoning adds, and executing the complex "Decimate" mechanic. The `mob_zombieChow` struct handles the behavior of the summoned zombies, specifically their reaction to being hit by Decimate (chasing Gluth to be eaten) versus standard combat behavior. Finally, `GluthDecimateScript` provides custom spell logic to ensure the Decimate spell leaves targets at exactly 5% health rather than killing them outright.

The unit operates entirely within memory using the engine's AI framework (`ScriptedAI`, `EventMap`) and does not interact with any database tables.

## Member-by-Member Behavior

### Boss Gluth AI (`boss_gluthAI`)

**Initialization and State Management**
*   **`boss_gluthAI` (ctor)**: Initializes the AI, retrieves the `instance_naxxramas` script instance to track encounter state, resets event timers, and calculates `five_percent` (5% of Gluth's max health) for the healing mechanic.
*   **`Reset`**: Clears all scheduled events and despawns any existing Zombie Chow adds via `DespawnAllZombiess`.
*   **`JustDied`**: Signals the instance script that Gluth is defeated (`TYPE_GLUTH, DONE`) and cleans up remaining adds.
*   **`JustReachedHome`**: Signals the instance script that the encounter failed (`TYPE_GLUTH, FAIL`).

**Aggro and Movement**
*   **`MoveInLineOfSight`**: Overrides default aggro logic. Gluth only aggroes players who are within 49.0 yards, not feigning death, and not already in combat. This restricts aggro range to the specific sewer pipe area where players enter the fight. It delegates to `BasicAI::MoveInLineOfSight` for standard checks.
*   **`Aggro`**: Starts the encounter by signaling `IN_PROGRESS` to the instance script. It schedules all periodic abilities: Mortal Wound (10s), Decimate (105s), Frenzy (10s), Summon Add (6s), Berserk (330s), Terrifying Roar (20s), Zombie Search (3s), and an Evade Check (5s).
*   **`UpdateAI`**: The main loop. It processes timed events:
    *   **Mortal Wound/Frenzy/Terrifying Roar/Berserk**: Casts spells on self or victim. If casting fails, it reschedules quickly (100ms) to retry; otherwise, it repeats on the standard cooldown.
    *   **Decimate**: Casts `SPELL_DECIMATE` on self. The actual effect on players is handled in `SpellHit`.
    *   **Summon**: Calls `SummonAdd` to spawn a zombie.
    *   **Zombie Search**: Calls `DoSearchZombieChow` to check for nearby zombies to eat.
    *   **Evade Check**: Monitors Gluth's position. If Gluth gets stuck in walls (Z-coordinate between 293.0 and 300.0) or moves too far from home (>150 yards), it forces an evade to reset the encounter.
    *   Finally, it attempts melee attacks if ready.

**Mechanics Implementation**
*   **`DespawnAllZombiess`**: Finds all creatures with entry `NPC_ZOMBIE_CHOW` within 200 yards and marks them for deletion.
*   **`SpellHit`**: Intercepts spells hitting Gluth. If the spell is `SPELL_DECIMATE`, it iterates through all players in the instance map. For each alive player, it casts `SPELL_DECIMATE_OTHER` (triggered). This ensures the visual and mechanical effect applies to everyone simultaneously when Gluth casts Decimate on himself.
*   **`DoSearchZombieChow`**: Searches for alive Zombie Chows within 15 yards. For each valid target, it faces the zombie, deals damage equal to the zombie's current health (killing it instantly), and heals Gluth by `five_percent`. Note: `SetHealth` is used directly, relying on internal truncation to max health.
*   **`SummonAdd`**: Selects a random summon location from `aZombieSummonLoc` with slight random offset. Summons a `NPC_ZOMBIE_CHOW` with a 5-minute duration. The zombie immediately enters combat with a random target selected by Gluth.

### Zombie Chow AI (`mob_zombieChow`)

**Initialization and State**
*   **`mob_zombieChow` (ctor)**: Initializes the AI, retrieves the instance data, and calls `Reset`.
*   **`Reset`**: Sets `isHitByDecimate` to false and casts `SPELL_INFECTED_WOUND` on itself.

**Behavior Logic**
*   **`ChaseGluth`**: Retrieves Gluth from storage. If found, it clears the zombie's motion master and sets it to follow Gluth at attack distance. It clears the target GUID to prevent attacking players while chasing. Returns true if successful.
*   **`SpellHit`**: If hit by `SPELL_DECIMATE` from Gluth (`NPC_GLUTH`), it triggers `ChaseGluth`. If successful, it casts `SPELL_DECIMATE_OTHER` on itself (visual effect) and sets `isHitByDecimate` to true.
*   **`AttackStart`**: Prevents the zombie from attacking players if `isHitByDecimate` is true. Otherwise, proceeds with standard attack logic.
*   **`UpdateAI`**:
    *   If `isHitByDecimate` is true, it ensures the movement generator is still set to `CHASE_MOTION_TYPE` (calling `ChaseGluth` if needed) and skips combat logic.
    *   If not decimated, it performs standard melee combat against its victim.

### Spell Script (`GluthDecimateScript`)

*   **`OnEffectExecute`**: Customizes the damage calculation for the Decimate spell. Instead of dealing fixed damage, it calculates the amount needed to reduce the target's health to exactly 5% of their maximum health. It uses `std::max(0, ...)` to ensure damage is never negative. This prevents the spell from killing players outright, allowing them to survive for Gluth to potentially eat later.

### Registration Functions

*   **`GetAI_boss_gluth` / `GetAI_mob_zombieChow`**: Factory functions returning new instances of the respective AI structs.
*   **`GetScript_GluthDecimate`**: Factory function returning the custom spell script.
*   **`AddSC_boss_gluth`**: Registers the "boss_gluth", "mob_zombie_chow", and "spell_gluth_decimate" scripts with the `ScriptMgr`. Called by `ScriptLoader::AddScripts`.

## Cross-Unit Boundaries

*   **`instance_naxxramas`**:
    *   **Called by**: `boss_gluthAI::boss_gluthAI`, `JustDied`, `Aggro`, `JustReachedHome`, `SpellHit`, `mob_zombieChow::mob_zombieChow`, `ChaseGluth`.
    *   **Collaboration**: The AI relies heavily on the instance script to track encounter state (`DONE`, `IN_PROGRESS`, `FAIL`) and to retrieve Gluth's creature pointer from storage (`GetSingleCreatureFromStorage`).
*   **`ScriptedAI` / `CreatureAI` / `BasicAI`**:
    *   **Called by**: All AI methods (`MoveInLineOfSight`, `AttackStart`, `SpellHit`, `UpdateAI`, etc.).
    *   **Collaboration**: Inherits base AI functionality. `boss_gluthAI` overrides `MoveInLineOfSight` to call `BasicAI::MoveInLineOfSight` after custom checks. `mob_zombieChow` calls `ScriptedAI::SpellHit` and `ScriptedAI::AttackStart` for default behavior.
*   **`EventMap`**:
    *   **Called by**: `boss_gluthAI::Reset`, `Aggro`, `UpdateAI`.
    *   **Collaboration**: Manages the timing of Gluth's abilities. `ScheduleEvent` sets initial timers, `Update` advances them, and `ExecuteEvent` returns triggered IDs. `Repeat` reschedules events.
*   **`GridSearchers`**:
    *   **Called by**: `boss_gluthAI::DespawnAllZombiess`, `DoSearchZombieChow`, `SummonAdd` (indirectly via summon logic? No, `SummonAdd` uses `SummonCreature`). Actually, `DespawnAllZombiess` and `DoSearchZombieChow` use `GetCreatureListWithEntryInGrid`.
    *   **Collaboration**: Provides spatial queries to find nearby creatures (zombies) for despawning or eating.
*   **`WorldObject` / `Unit` / `Creature`**:
    *   **Called by**: Various AI methods.
    *   **Collaboration**: Used for object manipulation: `GetMaxHealth`, `GetInstanceData`, `DeleteLater`, `IsWithinDistInMap`, `HasAuraType`, `IsInCombat`, `GetTypeId`, `GetVictim`, `SelectHostileTarget`, `GetDistance2d`, `GetPositionZ`, `DealDamage`, `SetHealth`, `SetFacingToObject`, `SummonCreature`, `SetInCombatWithZone`, `SelectAttackingTarget`, `AI`, `GetMotionMaster`, `MoveFollow`, `Clear`, `SetTargetGuid`, `GetEntry`, `IsDead`, `GetHealth`.
*   **`CreatureAI`**:
    *   **Called by**: `boss_gluthAI::MoveInLineOfSight`, `Aggro` (no, Aggro doesn't call CreatureAI directly, it calls EventMap), `SpellHit`, `UpdateAI`, `mob_zombieChow::SpellHit`, `AttackStart`, `UpdateAI`.
    *   **Collaboration**: Provides helper functions like `DoCastSpellIfCan`, `DoMeleeAttackIfReady`, `AttackStart`.
*   **`ScriptMgr`**:
    *   **Called by**: `boss_gluthAI::UpdateAI` (`DoScriptText`), `AddSC_boss_gluth` (`RegisterSelf`).
    *   **Collaboration**: Handles emote text output and script registration.
*   **`shared_Util`**:
    *   **Called by**: `boss_gluthAI::SummonAdd`.
    *   **Collaboration**: Provides random number generation (`urand`, `frand`) for summon locations.
*   **`Map`**:
    *   **Called by**: `boss_gluthAI::SpellHit`.
    *   **Collaboration**: Retrieves the list of players in the instance map to apply Decimate effects.
*   **`Spell`**:
    *   **Called by**: `GluthDecimateScript::OnEffectExecute`.
    *   **Collaboration**: Accesses spell target and modifies damage value.

## Data Model

This unit does not interact with any database tables. All data (spawn locations, spell IDs, cooldowns, health percentages) is hardcoded in the source file or derived from runtime object states.

## Notable Implementation Details

*   **Decimate Mechanic Complexity**: The Decimate ability is split across three parts:
    1.  `boss_gluthAI::UpdateAI` casts `SPELL_DECIMATE` on Gluth.
    2.  `boss_gluthAI::SpellHit` detects this self-cast and broadcasts `SPELL_DECIMATE_OTHER` to all players.
    3.  `GluthDecimateScript::OnEffectExecute` modifies the damage of `SPELL_DECIMATE_OTHER` to leave players at 5% HP.
    4.  `mob_zombieChow::SpellHit` detects if a zombie is hit by Decimate from Gluth, triggering the chase behavior.
    This separation allows for precise control over the visual effects and mechanical outcomes.
*   **Manual Zombie Eating**: Instead of using a spell trigger for eating zombies, `DoSearchZombieChow` manually iterates through nearby zombies, kills them with direct damage, and heals Gluth. The comment notes this is "more reliable and simple" than using the original spell triggers.
*   **Evade Check Safety Net**: `EVENT_EVADE_CHECK` runs every 5 seconds to detect if Gluth is stuck in geometry (specific Z-range) or has wandered too far. This prevents soft-locks during the encounter.
*   **Hardcoded Summon Locations**: Zombie spawns are defined in `aZombieSummonLoc` with a fixed Z-coordinate. The Y-offset is randomized, but Z is static, which might cause issues if terrain height varies significantly, though the comment suggests this is intentional or accepted.
*   **Health Calculation**: `five_percent` is calculated once in the constructor. If Gluth's max health changes dynamically (e.g., via buffs/debuffs affecting max HP), this value would become stale. However, in typical WoW mechanics, boss max health is static during the fight.
*   **Zombie Chase Logic**: Zombies hit by Decimate stop attacking players and follow Gluth. The `UpdateAI` ensures they maintain this chase behavior even if the movement generator type changes unexpectedly.

## Member Reference

**boss_gluthAI** (ctor): Initializes the AI, fetches instance data, resets events, and calculates 5% health threshold.
**Reset**: Clears events and despawns all Zombie Chow adds.
**DespawnAllZombiess**: Finds and deletes all Zombie Chow creatures within 200 yards.
**JustDied**: Marks encounter as done in instance script and despawns adds.
**MoveInLineOfSight**: Restricts aggro to players within 49 yards, not feigning death, and not in combat.
**Aggro**: Starts encounter, signals instance script, and schedules all periodic abilities.
**JustReachedHome**: Marks encounter as failed in instance script.
**SpellHit**: If hit by Decimate, casts Decimate Other on all alive players in the instance.
**UpdateAI**: Processes timed events (spells, summons, searches, evade checks) and performs melee attacks.
**DoSearchZombieChow**: Finds nearby zombies, kills them, and heals Gluth by 5% max health.
**SummonAdd**: Spawns a Zombie Chow at a random location near predefined points, targeting a random player.
**mob_zombieChow** (ctor): Initializes AI, fetches instance data, and resets state.
**Reset#2**: Resets decimate flag and casts Infected Wound on self.
**ChaseGluth**: Sets zombie to follow Gluth and clears attack target.
**SpellHit#2**: If hit by Decimate from Gluth, starts chasing Gluth and casts visual effect.
**AttackStart**: Prevents attack if zombie is chasing Gluth due to Decimate.
**UpdateAI#2**: Maintains chase behavior if decimated; otherwise, performs melee combat.
**GetAI_boss_gluth**: Factory function for boss_gluthAI.
**GetAI_mob_zombieChow**: Factory function for mob_zombieChow.
**OnEffectExecute**: Modifies Decimate spell damage to leave target at 5% health.
**GetScript_GluthDecimate**: Factory function for GluthDecimateScript.
**AddSC_boss_gluth**: Registers boss, minion, and spell scripts with the script manager.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_gluth

*Source:* boss_gluth.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_gluthAI | ctor | ScriptedAI/ScriptedAI, Unit.Main/GetMaxHealth, WorldObject.Object/GetInstanceData | — | — |
| Reset | method | EventMap/Reset | — | — |
| DespawnAllZombiess | method | GridSearchers/GetCreatureListWithEntryInGrid#2, WorldObject.Object/DeleteLater | — | — |
| JustDied | method | instance_naxxramas.Main/SetData | — | — |
| MoveInLineOfSight | method | BasicAI/MoveInLineOfSight, CreatureAI/AttackStart, Object/GetTypeId, Unit.Main/HasAuraType, Unit.Main/IsInCombat, WorldObject.Object/IsWithinDistInMap | — | — |
| Aggro | method | EventMap/ScheduleEvent#2, EventMap/ScheduleEvent#3, instance_naxxramas.Main/SetData | — | — |
| JustReachedHome | method | instance_naxxramas.Main/SetData | — | — |
| SpellHit | method | CreatureAI/DoCastSpellIfCan, Map.Main/GetId, Map.Main/GetPlayers, Unit.Main/IsDead, ZoneScript/GetMap#2 | — | — |
| UpdateAI | method | Creature.Main/GetHomePosition#2, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, EventMap/ExecuteEvent, EventMap/Repeat, EventMap/Repeat#3, EventMap/Update, ScriptedAI/EnterEvadeMode, ScriptMgr/DoScriptText, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget, WorldObject.Object/GetDistance2d, WorldObject.Object/GetPositionZ | — | — |
| DoSearchZombieChow | method | GridSearchers/GetCreatureListWithEntryInGrid#2, Unit.Main/DealDamage, Unit.Main/GetHealth, Unit.Main/IsAlive, Unit.Main/SetFacingToObject, Unit.Main/SetHealth, WorldObject.Object/GetDistance2d#3 | — | — |
| SummonAdd | method | Creature.Main/AI, Creature.Main/SelectAttackingTarget, Creature.Main/SetInCombatWithZone, CreatureAI/AttackStart, shared_Util/frand, shared_Util/urand, WorldObject.Object/SummonCreature#2 | — | — |
| mob_zombieChow | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset#2 | method | SpellCaster/CastSpell#2 | — | — |
| ChaseGluth | method | Creature.MotionMaster/MoveFollow, MotionMaster/Clear, ObjectGuid/ObjectGuid#5, ScriptedInstance/GetSingleCreatureFromStorage, Unit.Main/GetMotionMaster, Unit.Main/SetTargetGuid | — | — |
| SpellHit#2 | method | CreatureAI/DoCastSpellIfCan, CreatureAI/SpellHit, Object/GetEntry | — | — |
| AttackStart | method | CreatureAI/AttackStart | — | — |
| UpdateAI#2 | method | Creature.MotionMaster/GetCurrentMovementGeneratorType, CreatureAI/DoMeleeAttackIfReady, Unit.Main/GetMotionMaster, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_boss_gluth | function | — | — | — |
| GetAI_mob_zombieChow | function | — | — | — |
| OnEffectExecute | method | Spell.Main/GetUnitTarget, Unit.Main/GetHealth, Unit.Main/GetMaxHealth | — | — |
| GetScript_GluthDecimate | function | — | — | — |
| AddSC_boss_gluth | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
