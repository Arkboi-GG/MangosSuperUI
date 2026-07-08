# ruins_of_ahnqiraj

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Ruins of Ahn'Qiraj Creature and Spell Scripts

This unit implements the artificial intelligence (AI) behaviors for various non-player characters (NPCs) and custom spell effects within the **Ruins of Ahn'Qiraj** instance. It covers trash mobs, elite guards, and specific mechanics for bosses like Captain Tuubid, the Obsidian Destroyer, and the Anubisath Guardian. The scripts handle combat routines, special abilities (such as consuming targets or draining mana), summoning minions, and interacting with the instance data system to track encounter states.

## Purpose & Responsibilities

The primary responsibility of `ruins_of_ahnqiraj` is to define how creatures behave during combat. It replaces default AI with scripted logic that triggers spells, manages threat, summons allies, and reacts to player actions. Key responsibilities include:

1.  **Trash Mob AI:** Implementing standard combat patterns for common enemies (e.g., Hive Zara Soldiers, Qiraji Warriors) including random spell casting and melee attacks.
2.  **Elite/Boss Mechanics:** Handling complex behaviors for elites like the **Anubisath Guardian** (summoning, enrage/explode mechanics), **Flesh Hunter** (consuming and splitting targets), and **Obsidian Destroyer** (mana drain and purge).
3.  **Coordinated Attacks:** Managing the "Captain Tuubid" encounter where **Qiraji Warriors** and **Swarmguard Needlers** follow a marked target assigned by Tuubid.
4.  **Spell Customization:** Modifying global spell behaviors, such as limiting the number of targets for **Drain Mana** and calculating dynamic damage for **Rajaxx's Thundercrash**.
5.  **Instance Integration:** Updating the instance script (`instance_ruins_of_ahnqiraj`) when key events occur, such as the death of the **Qiraji Gladiator**, to trigger subsequent phases or rewards.

## Member-by-Member Behavior

### Anubisath Guardian (`mob_anubisath_guardianAI`)
This AI controls a powerful elite that uses a mix of area-of-effect spells and summons.
*   **Initialization:** In the constructor, it inherits from `ScriptedAI`.
*   **Reset:** Randomly selects two primary spells (Meteor or Plague, Shadow Storm or Thunder Clap), a reflect spell, and an end-game mechanic (Enrage or Explode). It initializes timers for these abilities and clears all existing auras.
*   **Aggro:** Immediately casts the selected reflect spell on itself.
*   **Combat Loop (`UpdateAI`):**
    *   **Explode Mechanic:** If the "Explode" path was chosen and the timer expires, it casts the explode spell.
    *   **Spell Casting:** Periodically casts its primary spells on random targets or its current victim.
    *   **Summoning:** Summons either Anubisath Warriors or Swarms up to four times. Each summoned creature immediately attacks the guardian's current victim.
*   **Death:** Spawns a "Small Obsidian Chunk" game object at its location with a 4-day respawn time.
*   **Damage Taken:** If health drops below 10%, it triggers its end-game mechanic: either casting Enrage (with an emote) or initiating the Explode sequence.

### Ossirian Tornado (`OssirianTornadoAI`)
A wandering environmental hazard.
*   **Initialization:** Sets movement to random wander with a 55-unit radius. Casts initial buffs/spells on itself. Disables combat movement (it doesn't chase).
*   **Combat:** When aggroed, it enters combat with the zone. Its `UpdateAI` simply performs melee attacks if a target is selected; it does not cast spells or move towards targets.

### Flesh Hunter (`mob_flesh_hunterAI`)
An elite with a unique "consume" mechanic.
*   **Reset:** Initializes timers for Poison Bolt, Trash spells, Consume, and Consume Damage.
*   **Combat Loop (`UpdateAI`):**
    *   **Poison Bolt:** Casts on random targets.
    *   **Consume:** Targets the top threat player. If successful, it records the victim's GUID, stops moving, removes threat from that target, and begins dealing periodic damage via `SPELL_CONSUME_DMG`.
    *   **Split/Heal:** If the consumed target dies, the hunter heals to full health and resumes chasing. If the consumed target survives the charge phase, it casts `SPELL_SPLIT`.
*   **Kill Credit:** If the consumed target is killed, it casts a healing spell on itself.

### Obsidian Destroyer (`ObsidianDestroyerAI`)
An elite focused on mana manipulation.
*   **Reset:** Sets mana to 0.
*   **Aggro:** Ensures mana is 0 and marks itself as in combat.
*   **Combat Loop (`UpdateAI`):**
    *   **Purge:** If mana reaches maximum, it casts Purge.
    *   **Drain Mana:** Periodically casts Drain Mana on itself to build up mana.
*   **Death:** Spawns a Small Obsidian Chunk with a 4-day respawn.

### Hive Zara Soldier (`HiveZaraSoldierAI`)
Standard melee attacker with a low-health buff.
*   **Combat Loop:** Casts Venom Spit on random targets. If health drops below 20%, it casts Retaliation (note: the code contains a duplicate check for this condition, though the boolean flag prevents double-casting).

### Silicate Feeder (`SilicateFeederAI`)
Passive until attacked.
*   **Reset:** Sets faction to neutral (7).
*   **Combat Loop:** Upon first attack, changes faction to hostile (14) and enters combat. Performs melee attacks.
*   **Death:** Casts Cloud of Disease on itself.

### Qiraji Swarmguard (`QirajiSwarmguardAI`)
Melee attacker with a cleave ability.
*   **Movement:** Forces the creature to run (not walk).
*   **Combat Loop:** Casts Sundering Cleave on the victim periodically.

### Qiraji Gladiator (`QirajiGladiatorAI`)
An elite tied to the instance script.
*   **Initialization:** Retrieves the instance data pointer.
*   **Reset/Aggro:** Resets the instance data flag `TYPE_QIRAJI_GLADIATOR` to 0.
*   **Death:** Sets the instance data flag `TYPE_QIRAJI_GLADIATOR` to 1, signaling the instance script that this encounter is complete.
*   **Combat Loop:** If the instance data indicates the gladiator is dead (likely a bug in logic or intended for a different phase, as it checks `> 0` while alive), it casts Vengeance. Otherwise, it casts Trample and Uppercut.

### Hive Zara Stinger (`HiveZaraStingerAI`)
Charger mob.
*   **Combat Loop:** Attempts to cast Charge on a random target. After casting, it waits briefly (500ms) then resumes chasing the victim.

### Captain Tuubid (`TuubidAI`)
The leader of the warrior group.
*   **Combat Loop:**
    *   **Mark Target:** Periodically selects a random player, removes the mark from the previous target, and applies `SPELL_ATTACK_ORDER` to the new target. Logs an error if no valid player target is found.
    *   **Abilities:** Casts Cleave and Sunder Armor.

### Qiraji Warrior (`QirajiWarriorAI`)
Follows Tuubid's orders.
*   **Helper (`GetTuubidAI`):** Finds the nearest Tuubid and retrieves its AI pointer to access the marked target GUID.
*   **Reset:** Initializes timers.
*   **Combat Loop:**
    *   **Target Sync:** Every 1.4 seconds, it checks if Tuubid is alive. If so, it attacks the player marked by Tuubid. If Tuubid is dead, it reverts to standard threat-based targeting.
    *   **Abilities:** Casts Thunderclap (if close) and Uppercut.
    *   **Enrage:** If health drops below 20%, it casts Enrage.

### Swarmguard Needler (`SwarmguardNeedlerAI`)
Similar to Qiraji Warrior, follows Tuubid.
*   **Helper (`GetTuubidAI`):** Same logic as Qiraji Warrior.
*   **Combat Loop:** Syncs target with Tuubid's mark. Casts Cleave.

### Spell Scripts
*   **AQ20DrainManaScript:** Limits the spell to 6 targets and filters out targets with less than 1% mana or no mana resource.
*   **RajaxxThundercrashScript:** Calculates damage as 50% of the target's current health (minimum 200) and applies it dynamically.

### Registration Functions
The `GetAI_*` functions create instances of the respective AI classes. `AddSC_ruins_of_ahnqiraj` registers all these scripts with the server's script manager.

## Cross-Unit Boundaries

*   **`ScriptedAI`:** All creature AIs inherit from this base class, providing standard combat helpers like `DoCast`, `DoMeleeAttackIfReady`, and `SelectHostileTarget`.
*   **`Creature` / `Unit`:** Used extensively to manipulate health, power, position, and state (e.g., `SetInCombatWithZone`, `GetHealth`, `CastSpell`).
*   **`GameObject`:** Used by `mob_anubisath_guardianAI` and `ObsidianDestroyerAI` to spawn debris objects upon death.
*   **`WorldObject`:** Used for positioning (`GetPositionX/Y/Z`) and summoning (`SummonCreature`, `SummonGameObject`).
*   **`ScriptMgr`:** Used by `mob_anubisath_guardianAI` and `TuubidAI` to broadcast text/emotes (`DoScriptText`).
*   **`InstanceData` (`ScriptedInstance`):** `QirajiGladiatorAI` interacts with the instance script to report its death state. This allows the instance to track progress for rewards or phase changes.
*   **`CreatureGroups`:** Used by `QirajiWarriorAI` and `SwarmguardNeedlerAI` to verify they are part of Tuubid's group and to identify the original leader.
*   **`Log`:** `TuubidAI` logs errors if it fails to acquire a target.

## Data Model

This unit does not directly interact with database tables. It operates entirely on runtime memory structures (creatures, units, game objects) and instance data stored in memory. The `ruins_of_ahnqiraj.h` header defines enums for instance data types (`TYPE_QIRAJI_GLADIATOR`, etc.) which correspond to the `instance_ruins_of_ahnqiraj` class's internal state, but no SQL queries are executed here.

## Notable Implementation Details

1.  **Duplicate Logic in Hive Zara Soldier:** The `UpdateAI` for `HiveZaraSoldierAI` contains two identical `if` blocks checking `GetHealthPercent() < 20.0f && !m_bRetaliation`. While the boolean flag prevents double execution, the redundancy is unnecessary.
2.  **Flesh Hunter Consume Mechanic:** The `mob_flesh_hunterAI` manually manipulates threat (`modifyThreatPercent(-100)`) and health (`SetHealth`) to simulate the consume effect. It also stops movement explicitly. This is a complex state machine handled entirely in C++ rather than relying solely on spell effects.
3.  **Tuubid Target Marking:** The coordination between `TuubidAI`, `QirajiWarriorAI`, and `SwarmguardNeedlerAI` relies on `TuubidAI` storing a `uint64` GUID of the marked player. The other AIs poll this GUID every 1.4 seconds. If Tuubid dies, they revert to normal AI. This is a fragile coupling; if Tuubid despawns unexpectedly, the warriors might fail to find a new target.
4.  **Obsidian Destroyer Mana Reset:** The `ObsidianDestroyerAI` resets mana to 0 in both `Reset` and `Aggro`. This ensures the boss starts with empty mana, forcing it to use Drain Mana to build up to Purge.
5.  **Anubisath Guardian Randomness:** The guardian's spells and end-game mechanic are randomized on reset. This adds variability to the encounter.
6.  **Spell Script Build Checks:** `RajaxxThundercrashScript` uses preprocessor directives (`#if SUPPORTED_CLIENT_BUILD >= CLIENT_BUILD_1_11_2`) to handle differences in how custom spell damage is applied between client versions.

## Member Reference

**mob_anubisath_guardianAI**
Constructor for the Anubisath Guardian AI. Inherits from `ScriptedAI`.

**Reset#11**
Resets the guardian's state. Randomly selects spells (Meteor/Plague, Shadow Storm/Thunder Clap, Reflect, Enrage/Explode). Initializes timers. Removes all auras.

**JustDied#4**
Spawns a Small Obsidian Chunk game object at the creature's location with a 4-day respawn time.

**Aggro#8**
Casts the randomly selected reflect spell on the creature.

**JustSummoned**
Makes the summoned creature attack the guardian's current victim. Increments the summon count.

**SummonedCreatureDespawn**
Decrements the summon count.

**DamageTaken#2**
Checks if health is below 10%. If so, triggers Enrage (with emote) or starts the Explode timer.

**UpdateAI#11**
Main combat loop. Handles Explode timer, casts primary spells on random/victim targets, summons minions (up to 4), and performs melee attacks.

**OssirianTornadoAI**
Constructor for the Ossirian Tornado AI. Sets random wander movement, disables combat movement, and casts initial spells.

**Reset#4**
Empty reset function.

**Aggro#3**
Sets the creature in combat with the zone.

**UpdateAI#4**
Performs melee attacks if a hostile target is selected.

**mob_flesh_hunterAI**
Constructor for the Flesh Hunter AI. Inherits from `ScriptedAI`.

**Reset#12**
Initializes timers for Poison Bolt, Trash, Consume, and Consume Damage. Resets consumption state flags.

**Aggro#9**
Sets the creature in combat with the zone.

**KilledUnit**
If the killed unit is the consumed victim, casts a healing spell on the hunter.

**UpdateAI#12**
Main combat loop. Casts Poison Bolt. Attempts to Consume top threat target. If consuming, deals periodic damage, removes threat, and stops moving. If consumed target dies, heals and resumes chase. If consumed target survives, casts Split.

**ObsidianDestroyerAI**
Constructor for the Obsidian Destroyer AI. Inherits from `ScriptedAI`.

**Reset#3**
Sets mana to 0. Initializes Drain Mana timer.

**Aggro#2**
Sets creature in combat with zone. Ensures mana is 0.

**JustDied**
Spawns a Small Obsidian Chunk game object at the creature's location with a 4-day respawn time.

**UpdateAI#3**
Main combat loop. If mana is full, casts Purge. Periodically casts Drain Mana. Performs melee attacks.

**HiveZaraSoldierAI**
Constructor for the Hive Zara Soldier AI. Inherits from `ScriptedAI`.

**Reset**
Initializes Venom Spit timer and Retaliation flag.

**Aggro**
Sets the creature in combat with the zone.

**UpdateAI**
Main combat loop. Casts Venom Spit on random targets. If health < 20%, casts Retaliation (duplicate check exists). Performs melee attacks.

**SilicateFeederAI**
Constructor for the Silicate Feeder AI. Inherits from `ScriptedAI`.

**Reset#8**
Sets faction to neutral (7). Resets attacked flag.

**JustDied#3**
Casts Cloud of Disease on the creature.

**UpdateAI#8**
Main combat loop. If not yet attacked, sets faction to hostile (14) and enters combat. Performs melee attacks.

**QirajiSwarmguardAI**
Constructor for the Qiraji Swarmguard AI. Inherits from `ScriptedAI`.

**Reset#6**
Initializes Sundering Cleave timer.

**Aggro#5**
Sets the creature in combat with the zone.

**UpdateAI#6**
Forces running movement. Main combat loop. Casts Sundering Cleave on victim. Performs melee attacks.

**QirajiGladiatorAI**
Constructor for the Qiraji Gladiator AI. Retrieves instance data pointer.

**Reset#5**
Initializes timers. Resets instance data flag `TYPE_QIRAJI_GLADIATOR` to 0.

**Aggro#4**
Resets instance data flag `TYPE_QIRAJI_GLADIATOR` to 0. Sets creature in combat with zone.

**JustDied#2**
Sets instance data flag `TYPE_QIRAJI_GLADIATOR` to 1.

**UpdateAI#5**
Main combat loop. If instance data indicates gladiator is dead (flag > 0), casts Vengeance. Otherwise, casts Trample and Uppercut. Performs melee attacks.

**HiveZaraStingerAI**
Constructor for the Hive Zara Stinger AI. Inherits from `ScriptedAI`.

**Reset#2**
Initializes Charge timer and flags.

**UpdateAI#2**
Main combat loop. Attempts to cast Charge on random target. If cast, waits 500ms then resumes chase. Performs melee attacks.

**TuubidAI**
Constructor for the Tuubid AI. Inherits from `ScriptedAI`.

**Reset#10**
Initializes timers for Attack Order, Cleave, and Sunder Armor. Clears marked GUID.

**UpdateAI#10**
Main combat loop. Marks a random player with `SPELL_ATTACK_ORDER`, removing the mark from the previous target. Logs error if no target found. Casts Cleave and Sunder Armor. Performs melee attacks.

**QirajiWarriorAI**
Constructor for the Qiraji Warrior AI. Inherits from `ScriptedAI`.

**GetTuubidAI**
Helper function. Finds nearest Tuubid and returns its AI pointer to access the marked target GUID.

**Reset#7**
Initializes timers. Resets Tuubid GUID and alive flag.

**Aggro#6**
Sets the creature in combat with the zone.

**DamageTaken**
If health < 20% and not enraged, casts Enrage.

**UpdateAI#7**
Main combat loop. Syncs target with Tuubid's marked player every 1.4s. If Tuubid is dead, uses standard threat. Casts Thunderclap (if close) and Uppercut. Performs melee attacks.

**SwarmguardNeedlerAI**
Constructor for the Swarmguard Needler AI. Inherits from `ScriptedAI`.

**GetTuubidAI#2**
Helper function. Finds nearest Tuubid and returns its AI pointer to access the marked target GUID.

**Reset#9**
Initializes timers. Resets Tuubid GUID and alive flag.

**Aggro#7**
Sets the creature in combat with the zone.

**UpdateAI#9**
Main combat loop. Syncs target with Tuubid's marked player every 1.4s. If Tuubid is dead, uses standard threat. Casts Cleave. Performs melee attacks.

**GetAI_Tuubid**
Factory function returning a new `TuubidAI` instance.

**GetAI_SwarmguardNeedler**
Factory function returning a new `SwarmguardNeedlerAI` instance.

**GetAI_QirajiWarrior**
Factory function returning a new `QirajiWarriorAI` instance.

**GetAI_HiveZaraStinger**
Factory function returning a new `HiveZaraStingerAI` instance.

**GetAI_mob_anubisath_guardian**
Factory function returning a new `mob_anubisath_guardianAI` instance.

**GetAI_OssirianTornado**
Factory function returning a new `OssirianTornadoAI` instance.

**GetAI_mob_flesh_hunter**
Factory function returning a new `mob_flesh_hunterAI` instance.

**GetAI_HiveZaraSoldier**
Factory function returning a new `HiveZaraSoldierAI` instance.

**GetAI_ObsidianDestroyer**
Factory function returning a new `ObsidianDestroyerAI` instance.

**GetAI_SilicateFeeder**
Factory function returning a new `SilicateFeederAI` instance.

**GetAI_QirajiGladiator**
Factory function returning a new `QirajiGladiatorAI` instance.

**GetAI_QirajiSwarmguard**
Factory function returning a new `QirajiSwarmguardAI` instance.

**OnSetTargetMap**
Spell script hook for Drain Mana. Limits max targets to 6.

**OnCheckTarget**
Spell script hook for Drain Mana. Filters out targets with < 1% mana or no mana resource.

**GetScript_AQ20DrainMana**
Factory function returning a new `AQ20DrainManaScript` instance.

**OnEffectExecute**
Spell script hook for Rajaxx Thundercrash. Calculates damage as 50% of target health (min 200) and applies it.

**GetScript_RajaxxThundercrash**
Factory function returning a new `RajaxxThundercrashScript` instance.

**AddSC_ruins_of_ahnqiraj**
Registers all creature AIs and spell scripts with the server's script manager.

---

<!-- machine-true, projected from graph.json -->

## Map — ruins_of_ahnqiraj

*Source:* ruins_of_ahnqiraj.cpp, ruins_of_ahnqiraj.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| mob_anubisath_guardianAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#11 | method | shared_Util/urand, Unit.Main/RemoveAllAuras | — | — |
| JustDied#4 | method | GameObject/SetRespawnTime, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/SummonGameObject | — | — |
| Aggro#8 | method | CreatureAI/DoCast | — | — |
| JustSummoned | method | Creature.Main/AI, CreatureAI/AttackStart, Unit.Main/GetVictim | — | — |
| SummonedCreatureDespawn | method | — | — | — |
| DamageTaken#2 | method | CreatureAI/DoCast, ScriptMgr/DoScriptText, SpellCaster/CastSpell#2, Unit.Main/GetHealth, Unit.Main/GetMaxHealth, Unit.Main/GetVictim | — | — |
| UpdateAI#11 | method | Creature.Main/SelectAttackingTarget, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget, Unit.Main/SendSpellGo, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/SummonCreature#2 | — | — |
| OssirianTornadoAI | ctor | Creature.Main/SetDefaultMovementType, Creature.Main/SetWanderDistance, Creature.MotionMaster/Initialize, CreatureAI/SetCombatMovement, ScriptedAI/ScriptedAI, SpellCaster/CastSpell#2, Unit.Main/GetMotionMaster | — | — |
| Reset#4 | method | — | — | — |
| Aggro#3 | method | Creature.Main/SetInCombatWithZone | — | — |
| UpdateAI#4 | method | CreatureAI/DoMeleeAttackIfReady, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| mob_flesh_hunterAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#12 | method | — | — | — |
| Aggro#9 | method | Creature.Main/SetInCombatWithZone | — | — |
| KilledUnit | method | CreatureAI/DoCast, Object/GetGUID | — | — |
| UpdateAI#12 | method | Creature.Main/SelectAttackingTarget, Creature.MotionMaster/Initialize, Creature.MotionMaster/MoveChase, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, Object/GetGUID, ThreatManager/modifyThreatPercent#2, Unit.Main/GetHealth, Unit.Main/GetMaxHealth, Unit.Main/GetMotionMaster, Unit.Main/GetThreatManager, Unit.Main/GetUnit, Unit.Main/GetVictim, Unit.Main/HasAura#2, Unit.Main/IsAlive, Unit.Main/SelectHostileTarget, Unit.Main/SetHealth, Unit.Main/StopMoving | — | — |
| ObsidianDestroyerAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#3 | method | Unit.Main/SetPower | — | — |
| Aggro#2 | method | Creature.Main/SetInCombatWithZone, Unit.Main/SetPower | — | — |
| JustDied | method | GameObject/SetRespawnTime, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/SummonGameObject | — | — |
| UpdateAI#3 | method | CreatureAI/DoCast, CreatureAI/DoMeleeAttackIfReady, Unit.Main/GetMaxPower, Unit.Main/GetPower, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| HiveZaraSoldierAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset | method | — | — | — |
| Aggro | method | Creature.Main/SetInCombatWithZone | — | — |
| UpdateAI | method | Creature.Main/SelectAttackingTarget, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, shared_Util/urand, SpellCaster/CastSpell#2, Unit.Main/GetHealthPercent, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| SilicateFeederAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#8 | method | Unit.Main/SetFactionTemplateId | — | — |
| JustDied#3 | method | CreatureAI/DoCastSpellIfCan | — | — |
| UpdateAI#8 | method | Creature.Main/SetInCombatWithZone, CreatureAI/DoMeleeAttackIfReady, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget, Unit.Main/SetFactionTemplateId | — | — |
| QirajiSwarmguardAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#6 | method | — | — | — |
| Aggro#5 | method | Creature.Main/SetInCombatWithZone | — | — |
| UpdateAI#6 | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, shared_Util/urand, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget, Unit.Main/SetWalk, WorldObject.Object/IsWalking | — | — |
| QirajiGladiatorAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset#5 | method | InstanceData/SetData | — | — |
| Aggro#4 | method | Creature.Main/SetInCombatWithZone, InstanceData/SetData | — | — |
| JustDied#2 | method | InstanceData/SetData | — | — |
| UpdateAI#5 | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, InstanceData/GetData, shared_Util/urand, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| HiveZaraStingerAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#2 | method | — | — | — |
| UpdateAI#2 | method | Creature.Main/SelectAttackingTarget, Creature.MotionMaster/MoveChase, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, Unit.Main/GetMotionMaster, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| TuubidAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#10 | method | — | — | — |
| UpdateAI#10 | method | Creature.Main/SelectAttackingTarget, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, Log.Main/Out, Map.Main/GetUnit, Object/GetGUID, ObjectGuid/ObjectGuid#5, ScriptMgr/DoScriptText, Unit.Main/GetCharmerOrOwnerPlayerOrPlayerItself, Unit.Main/GetVictim, Unit.Main/RemoveAurasDueToSpell, Unit.Main/SelectHostileTarget, WorldObject.Object/GetMap | — | — |
| QirajiWarriorAI | ctor | ScriptedAI/ScriptedAI | — | — |
| GetTuubidAI | method | Creature.Main/AI, Map.Main/GetCreature, Object/GetGUID, ObjectGuid/ObjectGuid#5, Unit.Main/IsAlive, WorldObject.Object/FindNearestCreature, WorldObject.Object/GetMap | — | — |
| Reset#7 | method | shared_Util/urand | — | — |
| Aggro#6 | method | Creature.Main/SetInCombatWithZone | — | — |
| DamageTaken | method | CreatureAI/DoCast, SpellCaster/IsNonMeleeSpellCasted, Unit.Main/GetHealth, Unit.Main/GetMaxHealth, Unit.Main/GetVictim | — | — |
| UpdateAI#7 | method | Creature.Main/GetCreatureGroup, CreatureAI/AttackStart, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, CreatureGroups/GetOriginalLeaderGuid, Map.Main/GetUnit, ObjectGuid/GetEntry, ObjectGuid/ObjectGuid#5, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget, WorldObject.Object/GetDistance2d#3, WorldObject.Object/GetMap | — | — |
| SwarmguardNeedlerAI | ctor | ScriptedAI/ScriptedAI | — | — |
| GetTuubidAI#2 | method | Creature.Main/AI, Map.Main/GetCreature, Object/GetGUID, ObjectGuid/ObjectGuid#5, Unit.Main/IsAlive, WorldObject.Object/FindNearestCreature, WorldObject.Object/GetMap | — | — |
| Reset#9 | method | shared_Util/urand | — | — |
| Aggro#7 | method | Creature.Main/SetInCombatWithZone | — | — |
| UpdateAI#9 | method | Creature.Main/GetCreatureGroup, CreatureAI/AttackStart, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, CreatureGroups/GetOriginalLeaderGuid, Map.Main/GetUnit, ObjectGuid/GetEntry, ObjectGuid/ObjectGuid#5, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget, WorldObject.Object/GetMap | — | — |
| GetAI_Tuubid | function | — | — | — |
| GetAI_SwarmguardNeedler | function | — | — | — |
| GetAI_QirajiWarrior | function | — | — | — |
| GetAI_HiveZaraStinger | function | — | — | — |
| GetAI_mob_anubisath_guardian | function | — | — | — |
| GetAI_OssirianTornado | function | — | — | — |
| GetAI_mob_flesh_hunter | function | — | — | — |
| GetAI_HiveZaraSoldier | function | — | — | — |
| GetAI_ObsidianDestroyer | function | — | — | — |
| GetAI_SilicateFeeder | function | — | — | — |
| GetAI_QirajiGladiator | function | — | — | — |
| GetAI_QirajiSwarmguard | function | — | — | — |
| OnSetTargetMap | method | — | — | — |
| OnCheckTarget | method | Unit.Main/GetPowerPercent, Unit.Main/GetPowerType | — | — |
| GetScript_AQ20DrainMana | function | — | — | — |
| OnEffectExecute | method | Spell.Main/GetUnitTarget, Unit.Main/GetHealth | — | — |
| GetScript_RajaxxThundercrash | function | — | — | — |
| AddSC_ruins_of_ahnqiraj | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
