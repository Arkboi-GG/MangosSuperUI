# boss_maexxna

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# boss_maexxna

**Purpose & Responsibilities**  
This translation unit implements the AI and spell scripts for **Maexxna**, a boss encounter in the Naxxramas raid instance. It handles two distinct creature behaviors:
1. **`boss_maexxnaAI`**: The main boss logic, managing timed abilities (Web Wrap, Web Spray, Poison Shock, Necrotic Poison, Spiderling summons), an enrage mechanic at 30% health, and instance state updates (`IN_PROGRESS`, `DONE`, `FAIL`).
2. **`mob_webwrapAI`**: A summoned "web wrap" entity that attaches to a player, flies toward them, and cleans up its associated debuffs upon death.

Additionally, it provides two custom spell scripts:
- **`MaexxnaWebSprayScript`**: Prevents Web Spray from hitting players already affected by Web Wrap or Petrification.
- **`MaexxnaSpiderWebScript`**: Implements a knockback effect for the Spider Web spell, mirroring the physics calculation in `DoCastWebWrap`.

The unit does **not** interact with any database tables. All data is managed in-memory via timers, vectors, and instance data.

---

## Member-by-Member Behavior

### Cooldown Helpers
These static functions return cooldown durations in milliseconds for Maexxna’s abilities. They support an `initial` parameter to provide different startup delays.

- **`WebWrapCooldown`**: Returns 20s initially, then 40s.
- **`SummonSpiderlingsCooldown`**: Returns 30s initially, then 40s.
- **`WebSprayCooldown`**: Always returns 40s.
- **`PoisonShockCooldown`**: Returns a random value between 9–11s using `shared_Util/urand`.
- **`NecroticPoisonCooldown`**: Returns 15s initially, then a random value between 5–10s using `shared_Util/urand`.

### `mob_webwrapAI` (Web Wrap Entity)
This AI controls the summoned web wrap creature that targets a specific player.

- **`mob_webwrapAI` (ctor)**: Initializes the AI and calls `Reset`. Inherits from `ScriptedAI/ScriptedAI`.
- **`Reset#2`**: Resets internal timers and flags.
- **`MoveInLineOfSight#2`**: Empty override; the web wrap does not initiate combat.
- **`AttackStart`**: Empty override; the web wrap does not attack.
- **`SetVictim`**: Assigns a player as the target. Adds the `SPELL_SUMMON_WEB_WRAP` aura to the player, stores their GUID, and commands the creature to fly to the player’s position using `Creature.MotionMaster/MovePoint` with `MOVE_FLY_MODE | MOVE_CYCLIC`. Logs an error if the target is not a player.
- **`JustDied#2`**: Cleans up the web wrap aura from the victim if they are alive, then despawns the creature after 1 second using `Creature.Main/DespawnOrUnsummon`. Uses `Map.Main/GetPlayer` and `WorldObject.Object/GetMap` to locate the victim.
- **`UpdateAI#2`**: Checks if the victim is still valid and alive. If the victim dies or disappears, the web wrap kills itself. Uses `Map.Main/GetPlayer`, `Unit.Main/IsDead`, and `Unit.Main/Kill`.

### `boss_maexxnaAI` (Main Boss)
This AI manages Maexxna’s combat behavior, including ability rotation, summoning, and instance state.

- **`boss_maexxnaAI` (ctor)**: Retrieves the instance data via `WorldObject.Object/GetInstanceData` and calls `Reset`.
- **`Reset`**: Initializes all ability timers to their initial cooldowns, clears the `wraps` and `wraps2` vectors, and resets the enrage flag.
- **`Aggro`**: Sets the instance data to `IN_PROGRESS` via `instance_naxxramas.Main/SetData`.
- **`JustDied`**: Sets the instance data to `DONE` via `instance_naxxramas.Main/SetData`.
- **`MoveInLineOfSight`**: Handles aggro generation. If the target is within 40 yards, line-of-sight, and hostile, it initiates combat or adds threat in dungeon mode. Uses `Creature.Main/CanInitiateAttack`, `Unit.Main/IsHostileTo`, `WorldObject.Object/IsWithinLOSInMap`, and `Unit.Main/SetInCombatWith`.
- **`JustReachedHome`**: Sets the instance data to `FAIL` via `instance_naxxramas.Main/SetData`. Then finds and deletes all nearby spiderlings using `GridSearchers/GetCreatureListWithEntryInGrid#2` and `WorldObject.Object/DeleteLater`.
- **`DoCastWebWrap`**: Selects up to 3 players from the threat list (excluding GMs, dead players, and those already wrapped) and knocks them back toward predefined web wrap locations. Calculates horizontal and vertical speeds based on distance and height difference. Sets `SetLaunched(true)` to bypass anti-cheat checks. Stores the victims and delays in the `wraps` vector for later processing. Uses `ThreatManager/getThreatList`, `Unit.Main/GetThreatManager`, `WorldObject.Object/GetAngle#2`, and `Unit.Main/KnockBack`.
- **`JustSummoned`**: If the summoned creature is a spiderling, it selects a random target and starts combat using `Creature.Main/SelectAttackingTarget` and `CreatureAI/AttackStart`.
- **`UpdateWraps`**: Processes the `wraps` and `wraps2` vectors. After a delay, it casts the web wrap spell on the victim and schedules the summoning of the web wrap creature. After a further delay, it summons the `NPC_WEB_WRAP` creature and assigns the victim via `mob_webwrapAI.SetVictim`. Uses `Map.Main/GetPlayer`, `SpellCaster/CastSpell#2`, and `WorldObject.Object/SummonCreature#2`.
- **`UpdateAI`**: The main update loop. It processes `UpdateWraps`, then checks each ability timer. If ready, it casts the corresponding spell using `CreatureAI/DoCastSpellIfCan`. At 30% health, it applies the enrage aura and plays an emote via `ScriptMgr/DoScriptText`. Finally, it performs melee attacks if ready using `CreatureAI/DoMeleeAttackIfReady`.

### Spell Scripts
- **`OnCheckTarget`** (in `MaexxnaWebSprayScript`): Prevents Web Spray from targeting players with aura 17624 (Petrification) or 28622 (Web Wrap) using `Unit.Main/HasAura#2`.
- **`OnEffectExecute`** (in `MaexxnaSpiderWebScript`): Applies a knockback effect similar to `DoCastWebWrap`, calculating speed based on distance and height. Sets `SetLaunched(true)` for the target. Uses `Spell.Main/GetUnitTarget`, `Unit.Main/KnockBack`, and `WorldObject.Object/GetAngle#2`.

### Factory Functions
- **`GetAI_mob_webwrap`**: Returns a new `mob_webwrapAI` instance.
- **`GetAI_boss_maexxna`**: Returns a new `boss_maexxnaAI` instance.
- **`GetScript_MaexxnaWebSpray`**: Returns a new `MaexxnaWebSprayScript` instance.
- **`GetScript_MaexxnaSpiderWeb`**: Returns a new `MaexxnaSpiderWebScript` instance.

### Registration
- **`AddSC_boss_maexxna`**: Registers all four scripts with the script manager via `ScriptMgr/RegisterSelf`. Called by `ScriptLoader/AddScripts`.

---

## Cross-Unit Boundaries

| Member | Direction | Collaborating Unit | Purpose |
|--------|-----------|---------------------|---------|
| `mob_webwrapAI` (ctor) | Calls | `ScriptedAI/ScriptedAI` | Base AI initialization. |
| `SetVictim` | Calls | `Creature.MotionMaster/MovePoint` | Moves the web wrap to the victim. |
| `SetVictim` | Calls | `Log.Main/Out` | Logs errors if victim is invalid. |
| `SetVictim` | Calls | `Object/GetObjectGuid`, `Object/GetTypeId` | Validates and stores the victim’s GUID. |
| `SetVictim` | Calls | `Unit.Main/AddAura` | Applies the web wrap aura to the victim. |
| `SetVictim` | Calls | `WorldObject.Object/GetPositionX/Y/Z` | Gets the victim’s coordinates for movement. |
| `JustDied#2` | Calls | `Creature.Main/DespawnOrUnsummon` | Despawns the web wrap creature. |
| `JustDied#2` | Calls | `Map.Main/GetPlayer` | Locates the victim player. |
| `JustDied#2` | Calls | `Unit.Main/IsAlive`, `Unit.Main/RemoveAurasDueToSpell` | Cleans up auras if the victim is alive. |
| `JustDied#2` | Calls | `WorldObject.Object/GetMap` | Accesses the map to find the player. |
| `UpdateAI#2` | Calls | `Map.Main/GetPlayer` | Checks if the victim is still present. |
| `UpdateAI#2` | Calls | `Unit.Main/IsDead`, `Unit.Main/Kill` | Kills the web wrap if the victim dies. |
| `boss_maexxnaAI` (ctor) | Calls | `ScriptedAI/ScriptedAI` | Base AI initialization. |
| `boss_maexxnaAI` (ctor) | Calls | `WorldObject.Object/GetInstanceData` | Retrieves the Naxxramas instance data. |
| `Aggro`, `JustDied`, `JustReachedHome` | Calls | `instance_naxxramas.Main/SetData` | Updates the instance state (IN_PROGRESS, DONE, FAIL). |
| `MoveInLineOfSight` | Calls | `Creature.Main/CanInitiateAttack`, `CreatureAI/AttackStart` | Initiates combat with a target. |
| `MoveInLineOfSight` | Calls | `Map.Main/IsDungeon` | Checks if the encounter is in a dungeon. |
| `MoveInLineOfSight` | Calls | `Unit.Main/AddThreat`, `Unit.Main/SetInCombatWith` | Adds threat and sets combat state. |
| `MoveInLineOfSight` | Calls | `WorldObject.Object/IsWithinDistInMap`, `WorldObject.Object/IsWithinLOSInMap` | Checks range and line-of-sight. |
| `JustReachedHome` | Calls | `GridSearchers/GetCreatureListWithEntryInGrid#2` | Finds nearby spiderlings to delete. |
| `JustReachedHome` | Calls | `WorldObject.Object/DeleteLater` | Deletes the spiderlings. |
| `DoCastWebWrap` | Calls | `ThreatManager/getThreatList`, `Unit.Main/GetThreatManager` | Gets the list of threatened players. |
| `DoCastWebWrap` | Calls | `Unit.Main/HasAura#2`, `Unit.Main/IsAlive` | Filters out invalid targets. |
| `DoCastWebWrap` | Calls | `Unit.Main/KnockBack` | Knocks players back toward web wrap locations. |
| `DoCastWebWrap` | Calls | `WorldObject.Object/GetAngle#2`, `WorldObject.Object/GetPositionX/Y/Z` | Calculates knockback direction and speed. |
| `DoCastWebWrap` | Calls | `Player.Main/SetLaunched` | Bypasses anti-cheat for the knockback. |
| `JustSummoned` | Calls | `Creature.Main/AI`, `Creature.Main/SelectAttackingTarget` | Starts combat for summoned spiderlings. |
| `JustSummoned` | Calls | `CreatureAI/AttackStart`, `Unit.Main/AddThreat` | Initiates attack and adds threat. |
| `UpdateWraps` | Calls | `Map.Main/GetPlayer` | Locates the victim player. |
| `UpdateWraps` | Calls | `SpellCaster/CastSpell#2` | Casts the web wrap spell. |
| `UpdateWraps` | Calls | `WorldObject.Object/SummonCreature#2` | Summons the web wrap creature. |
| `UpdateWraps` | Calls | `ZoneScript/GetMap#2` | Accesses the map for summoning. |
| `UpdateAI` | Calls | `CreatureAI/DoCastSpellIfCan`, `CreatureAI/DoMeleeAttackIfReady` | Casts spells and performs melee attacks. |
| `UpdateAI` | Calls | `ScriptMgr/DoScriptText` | Plays the enrage emote. |
| `UpdateAI` | Calls | `Unit.Main/GetHealthPercent`, `Unit.Main/GetVictim`, `Unit.Main/SelectHostileTarget` | Manages combat state and enrage condition. |
| `OnCheckTarget` | Calls | `Unit.Main/HasAura#2` | Checks for existing auras to block targeting. |
| `OnEffectExecute` | Calls | `Spell.Main/GetUnitTarget` | Gets the target of the spell. |
| `OnEffectExecute` | Calls | `Unit.Main/KnockBack` | Applies knockback to the target. |
| `OnEffectExecute` | Calls | `WorldObject.Object/GetAngle#2`, `WorldObject.Object/GetPositionX/Y/Z` | Calculates knockback direction and speed. |
| `AddSC_boss_maexxna` | Calls | `Script/Script`, `ScriptMgr/RegisterSelf` | Registers the scripts with the engine. |
| `AddSC_boss_maexxna` | Called by | `ScriptLoader/AddScripts` | Entry point for script registration. |

---

## Data Model

This unit does **not** interact with any database tables. All state is managed in-memory via timers, vectors, and instance data.

---

## Notable Implementation Details

1. **Web Wrap Physics Approximation**:  
   In `DoCastWebWrap` and `MaexxnaSpiderWebScript::OnEffectExecute`, the knockback speed is calculated using a simplified formula:
   ```cpp
   float horizontalSpeed = dist / 1.5f;
   float verticalSpeed = 20.0f + (yDist * 0.5f); // or 12.0f in the spell script
   ```
   The code comments note that this is an approximation and may cause issues near ceilings. The vertical speed is adjusted based on the height difference (`yDist`) between the caster and the target.

2. **Anti-Cheat Bypass**:  
   Both `DoCastWebWrap` and `OnEffectExecute` call `Player.Main/SetLaunched(true)` on the target before applying knockback. This bypasses the server’s anti-cheat system that might otherwise flag the sudden movement as suspicious.

3. **Two-Stage Web Wrap Process**:  
   The web wrap ability is split into two stages:
   - First, players are knocked back and added to the `wraps` vector.
   - After a 2-second delay, the web wrap spell is cast, and the players are moved to the `wraps2` vector.
   - After a 3-second delay, the `NPC_WEB_WRAP` creature is summoned and assigned to the player via `mob_webwrapAI.SetVictim`.

4. **Enrage Mechanic**:  
   Maexxna enrages at 30% health, applying a 30% damage increase aura (`SPELL_ENRAGE`) and playing an emote. The enrage flag (`m_bEnraged`) ensures this only happens once.

5. **Spiderling Cleanup**:  
   When Maexxna reaches home (fails the encounter), all nearby spiderlings are deleted using `GridSearchers/GetCreatureListWithEntryInGrid#2` and `WorldObject.Object/DeleteLater`.

6. **Target Filtering for Web Wrap**:  
   In `DoCastWebWrap`, the code filters out:
   - Game Masters (`Player.Main/IsGameMaster`)
   - Dead players (`Unit.Main/IsAlive`)
   - Players already affected by Web Wrap (`Unit.Main/HasAura#2`)
   - Players outside line-of-sight (`WorldObject.Object/IsWithinLOSInMap`)

7. **Random Target Selection**:  
   The code uses `std::shuffle` to randomize the web wrap locations and `urand` to select random targets from the candidate list.

8. **Instance State Management**:  
   The boss updates the instance state via `instance_naxxramas.Main/SetData` at key moments:
   - `IN_PROGRESS` on aggro
   - `DONE` on death
   - `FAIL` when reaching home

---

## Member Reference

- **WebWrapCooldown**: Static function returning 20s initially, then 40s.
- **SummonSpiderlingsCooldown**: Static function returning 30s initially, then 40s.
- **WebSprayCooldown**: Static function always returning 40s.
- **PoisonShockCooldown**: Static function returning a random value between 9–11s using `shared_Util/urand`.
- **NecroticPoisonCooldown**: Static function returning 15s initially, then a random value between 5–10s using `shared_Util/urand`.
- **mob_webwrapAI (ctor)**: Initializes the web wrap AI and calls `Reset`. Inherits from `ScriptedAI/ScriptedAI`.
- **Reset#2**: Resets internal timers and flags for the web wrap AI.
- **MoveInLineOfSight#2**: Empty override; the web wrap does not initiate combat.
- **AttackStart**: Empty override; the web wrap does not attack.
- **SetVictim**: Assigns a player as the target, applies the web wrap aura, and moves the creature to the player.
- **JustDied#2**: Cleans up the web wrap aura from the victim and despawns the creature.
- **UpdateAI#2**: Checks if the victim is still valid and alive; kills the web wrap if the victim dies.
- **boss_maexxnaAI (ctor)**: Retrieves the instance data and calls `Reset`. Inherits from `ScriptedAI/ScriptedAI`.
- **Reset**: Initializes all ability timers, clears vectors, and resets the enrage flag.
- **Aggro**: Sets the instance data to `IN_PROGRESS`.
- **JustDied**: Sets the instance data to `DONE`.
- **MoveInLineOfSight**: Handles aggro generation, initiating combat or adding threat.
- **JustReachedHome**: Sets the instance data to `FAIL` and deletes nearby spiderlings.
- **DoCastWebWrap**: Selects up to 3 players and knocks them back toward web wrap locations.
- **JustSummoned**: Starts combat for summoned spiderlings.
- **UpdateWraps**: Processes the web wrap delays, casting spells and summoning creatures.
- **UpdateAI**: Main update loop, processing abilities, enrage, and melee attacks.
- **GetAI_mob_webwrap**: Factory function returning a new `mob_webwrapAI` instance.
- **GetAI_boss_maexxna**: Factory function returning a new `boss_maexxnaAI` instance.
- **OnCheckTarget**: Prevents Web Spray from targeting players with specific auras.
- **GetScript_MaexxnaWebSpray**: Factory function returning a new `MaexxnaWebSprayScript` instance.
- **OnEffectExecute**: Applies a knockback effect for the Spider Web spell.
- **GetScript_MaexxnaSpiderWeb**: Factory function returning a new `MaexxnaSpiderWebScript` instance.
- **AddSC_boss_maexxna**: Registers all scripts with the script manager. Called by `ScriptLoader/AddScripts`.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_maexxna

*Source:* boss_maexxna.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| WebWrapCooldown | function | — | — | — |
| SummonSpiderlingsCooldown | function | — | — | — |
| WebSprayCooldown | function | — | — | — |
| PoisonShockCooldown | function | shared_Util/urand | — | — |
| NecroticPoisonCooldown | function | shared_Util/urand | — | — |
| mob_webwrapAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#2 | method | — | — | — |
| MoveInLineOfSight#2 | method | — | — | — |
| AttackStart | method | — | — | — |
| SetVictim | method | Creature.MotionMaster/MovePoint, Log.Main/Out, Object/GetObjectGuid, Object/GetTypeId, Unit.Main/AddAura, Unit.Main/GetMotionMaster, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ | — | — |
| JustDied#2 | method | Creature.Main/DespawnOrUnsummon, Map.Main/GetPlayer, Unit.Main/IsAlive, Unit.Main/RemoveAurasDueToSpell, WorldObject.Object/GetMap | — | — |
| UpdateAI#2 | method | Map.Main/GetPlayer, ObjectGuid/operator!, Unit.Main/IsDead, Unit.Main/Kill, WorldObject.Object/GetMap | — | — |
| boss_maexxnaAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset | method | — | — | — |
| Aggro | method | instance_naxxramas.Main/SetData | — | — |
| JustDied | method | instance_naxxramas.Main/SetData | — | — |
| MoveInLineOfSight | method | Creature.Main/CanInitiateAttack, CreatureAI/AttackStart, Map.Main/IsDungeon, Unit.Main/AddThreat, Unit.Main/GetVictim, Unit.Main/IsHostileTo, Unit.Main/IsInAccessablePlaceFor, Unit.Main/IsTargetableBy, Unit.Main/SetInCombatWith, WorldObject.Object/GetMap, WorldObject.Object/IsWithinDistInMap, WorldObject.Object/IsWithinLOSInMap | — | — |
| JustReachedHome | method | GridSearchers/GetCreatureListWithEntryInGrid#2, instance_naxxramas.Main/SetData, WorldObject.Object/DeleteLater | — | — |
| DoCastWebWrap | method | Object/GetObjectGuid, Object/ToPlayer, Player.Main/IsGameMaster, Player.Main/SetLaunched, shared_Util/urand, ThreatManager/getThreatList, Unit.Main/GetThreatManager, Unit.Main/HasAura#2, Unit.Main/IsAlive, Unit.Main/KnockBack, WorldObject.Object/GetAngle#2, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/IsWithinLOSInMap | — | — |
| JustSummoned | method | Creature.Main/AI, Creature.Main/SelectAttackingTarget, CreatureAI/AttackStart, Object/GetEntry, Unit.Main/AddThreat | — | — |
| UpdateWraps | method | Creature.Main/AI, Map.Main/GetPlayer, SpellCaster/CastSpell#2, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/SummonCreature#2, ZoneScript/GetMap#2 | — | — |
| UpdateAI | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, ScriptMgr/DoScriptText, Unit.Main/GetHealthPercent, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_mob_webwrap | function | — | — | — |
| GetAI_boss_maexxna | function | — | — | — |
| OnCheckTarget | method | Unit.Main/HasAura#2 | — | — |
| GetScript_MaexxnaWebSpray | function | — | — | — |
| OnEffectExecute | method | Object/ToPlayer, Player.Main/SetLaunched, Spell.Main/GetUnitTarget, Unit.Main/KnockBack, WorldObject.Object/GetAngle#2, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ | — | — |
| GetScript_MaexxnaSpiderWeb | function | — | — | — |
| AddSC_boss_maexxna | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
