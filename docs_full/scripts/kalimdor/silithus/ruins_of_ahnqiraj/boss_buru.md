# boss_buru

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# boss_buru

**Purpose & Responsibilities**  
`boss_buru.cpp` implements the AI for two creatures in the Ruins of Ahn'Qiraj instance: the boss **Buru the Gorger** (`boss_buruAI`) and his **Eggs** (`mob_buru_eggAI`). The boss exhibits a two-phase fight: a standard melee phase with periodic abilities, and an enrage phase triggered at 20% health featuring a transformation, increased speed, a raid-wide debuff, and additional summons. The eggs serve as environmental hazards that aggro the boss when damaged and spawn adds upon death.

## Member-by-Member Behavior

### `boss_buruAI` (Boss AI)

#### Initialization & State Management
- **`boss_buruAI`**: Initializes the AI, retrieves the instance data via `WorldObject.Object/GetInstanceData`, and calls `Reset()` to set initial state.
- **`Reset`**: Resets all timers, flags, and state variables. Restores Buru’s display ID, removes all auras, sets run speed to 0.5x, and despawns any existing eggs before summoning six new ones at predefined coordinates. Reports `NOT_STARTED` to the instance script.
- **`EnterCombat`**: Sets combat state, applies 20,000 armor, casts `SPELL_THORNS`, enables melee and movement, and reports `IN_PROGRESS` to the instance.
- **`JustDied`**: Removes `SPELL_CREEPING_PLAGUE` from all players on the map to prevent post-death raid wipes. Reports `DONE` to the instance.
- **`AttackStart`**: Prevents target selection during the transformation phase (`m_bIsEnraged && m_uiTransformTimer`). Otherwise delegates to `ScriptedAI::AttackStart`.

#### Core Combat Logic (`UpdateAI`)
The main update loop handles egg management, phase transitions, target selection, and ability casting:

1. **Egg Management**:  
   - If an egg is missing, it is respawned immediately.  
   - If an egg is dead, it respawns after a 120-second delay (`m_uiRespawnEgg_Timer`).

2. **Phase Transition (Enrage)**:  
   - Triggered when health drops below 20%.  
   - Sets `m_bIsEnraged = true`, initiates a 200ms transformation timer, and begins a multi-step visual sequence:  
     - **Step 0**: Removes all auras (including Thorns), casts `SPELL_BURU_TRANSFORM`.  
     - **Step 1**: Casts `SPELL_FULL_SPEED`, re-enables melee/movement, and resumes combat.  
   - During transformation, no other actions occur.

3. **Target Selection (Pre-Enrage)**:  
   - If the current victim’s threat is low (`< THREAT_LOCK / 1000`), Buru selects a random alive player from the threat list, announces them via `EMOTE_TARGET`, and locks onto them by applying maximum threat (`THREAT_LOCK`).  
   - Speed is reduced to 0.5x, and `SPELL_GAIN_SPEED` is removed.

4. **Abilities**:  
   - **`SPELL_DISMEMBER`**: Cast on the victim every 6 seconds if not enraged.  
   - **`SPELL_CREEPING_PLAGUE`**: Cast on self every 6 seconds during enrage, applying a raid-wide debuff.  
   - **`SPELL_GAIN_SPEED`**: Cast on self every 30 seconds to increase speed.  
   - **Hatchling Summons**: During enrage, three `NPC_HIVEZARA_HATCHLING` adds are spawned once at predefined locations.

5. **Post-Enrage Cleanup**:  
   - Armor is set to 0, threat list is deleted, and speed is normalized to 1.0x.  
   - All eggs are despawned.

### `mob_buru_eggAI` (Egg AI)

#### Initialization & State Management
- **`mob_buru_eggAI`**: Initializes the AI and retrieves instance data.
- **`Reset#2`**: Empty; no reset logic required.
- **`UpdateAI#2`**: Empty; eggs do not perform autonomous actions.

#### Interaction Logic
- **`DamageTaken`**: If Buru is not in combat, damages to an egg trigger `SetInCombatWithZone` on Buru, effectively aggroing him.
- **`JustDied#2`**: Casts `SPELL_EXPLODE` and spawns a `NPC_HIVEZARA_HATCHLING` at the egg’s location, which enters combat immediately.

### Factory & Registration Functions
- **`GetAI_boss_buru`** / **`GetAI_mob_buru_egg`**: Factory functions returning new instances of the respective AI classes.
- **`AddSC_boss_buru`**: Registers both scripts with the `ScriptMgr` via `Script/Script` and `ScriptMgr/RegisterSelf`. Called by `ScriptLoader/AddScripts`.

## Cross-Unit Boundaries

| Member | Direction | Collaborating Unit | Purpose |
|--------|-----------|-------------------|---------|
| `boss_buruAI` | Calls | `ScriptedAI/ScriptedAI`, `WorldObject.Object/GetInstanceData` | Base AI initialization and instance data retrieval. |
| `Reset` | Calls | `InstanceData/SetData`, `Object/GetGUID`, `Unit.Main/*`, `WorldObject.Object/*`, `ZoneScript/GetCreature` | State reset, egg management, and instance reporting. |
| `EnterCombat` | Calls | `Creature.Main/*`, `CreatureAI/*`, `InstanceData/SetData`, `Unit.Main/SetArmor` | Combat setup, aura application, and instance reporting. |
| `JustDied` | Calls | `InstanceData/SetData`, `Map.Main/GetPlayers`, `Unit.Main/RemoveAurasDueToSpell`, `WorldObject.Object/GetMap` | Post-death cleanup and instance reporting. |
| `AttackStart` | Calls | `CreatureAI/AttackStart` | Target selection delegation. |
| `UpdateAI` | Calls | `Creature.Main/*`, `CreatureAI/*`, `HostileReference/*`, `Map.Main/GetPlayer`, `Object/*`, `ScriptMgr/DoScriptText`, `shared_Util/urand`, `SpellCaster/CastSpell#2`, `ThreatManager/*`, `Unit.Main/*`, `WorldObject.Object/*`, `ZoneScript/GetCreature` | Core combat logic, including egg management, phase transitions, target locking, and ability casting. |
| `mob_buru_eggAI` | Calls | `ScriptedAI/ScriptedAI`, `WorldObject.Object/GetInstanceData` | Base AI initialization and instance data retrieval. |
| `DamageTaken` | Calls | `Creature.Main/SetInCombatWithZone`, `InstanceData/GetData64`, `ObjectGuid/ObjectGuid#5`, `Unit.Main/IsInCombat`, `ZoneScript/GetCreature` | Aggro propagation to Buru. |
| `JustDied#2` | Calls | `Creature.Main/SetInCombatWithZone`, `SpellCaster/CastSpell#2`, `WorldObject.Object/GetPosition*`, `WorldObject.Object/SummonCreature#2` | Death explosion and add spawning. |
| `AddSC_boss_buru` | Calls | `Script/Script`, `ScriptMgr/RegisterSelf` | Script registration. |
| `AddSC_boss_buru` | Called By | `ScriptLoader/AddScripts` | Entry point for script loading. |

## Data Model

This unit interacts with no database tables directly. All state is managed in-memory via timers, flags, and instance data.

## Notable Implementation Details

1. **Target Locking Mechanism**:  
   In `UpdateAI`, Buru locks onto a random player by applying `THREAT_LOCK` (FLT_MAX) to their threat entry. This ensures he cannot be taunted or distracted until the threat list is deleted during enrage.

2. **Transformation Sequence**:  
   The enrage phase uses a multi-step timer (`m_uiTransformTimer`) to create a visual pause between removing auras, casting the transform spell, and resuming combat. This prevents immediate action during the transition.

3. **Egg Respawn Logic**:  
   Eggs are tracked by GUID in `m_eggsGUID[6]`. Missing eggs are respawned immediately, while dead eggs have a 120-second respawn delay. This ensures consistent environmental pressure.

4. **Raid-Wide Debuff Cleanup**:  
   In `JustDied`, `SPELL_CREEPING_PLAGUE` is removed from all players on the map to prevent lingering damage after the boss dies. This is critical for raid survival.

5. **Aggro Propagation via Eggs**:  
   Damaging an egg triggers `SetInCombatWithZone` on Buru if he is not already in combat. This allows players to pull the boss indirectly by attacking eggs.

6. **Hardcoded Coordinates**:  
   Egg and hatchling spawn locations are hardcoded in `Eggs[]` and `AddPop[]` arrays. These coordinates are specific to the Ruins of Ahn'Qiraj instance layout.

## Member Reference

- **boss_buruAI**: Initializes the AI, retrieves instance data, and calls `Reset()`.
- **Reset**: Resets state, despawns old eggs, summons new ones, and reports `NOT_STARTED` to the instance.
- **EnterCombat**: Sets combat state, applies armor and Thorns, enables melee/movement, and reports `IN_PROGRESS`.
- **JustDied**: Removes `SPELL_CREEPING_PLAGUE` from all players and reports `DONE` to the instance.
- **AttackStart**: Prevents target selection during transformation; otherwise delegates to base class.
- **UpdateAI**: Handles egg management, phase transitions, target locking, and ability casting.
- **mob_buru_eggAI**: Initializes the egg AI and retrieves instance data.
- **Reset#2**: Empty; no reset logic required.
- **DamageTaken**: Triggers Buru’s combat state if he is not already in combat.
- **JustDied#2**: Casts `SPELL_EXPLODE` and spawns a hatchling add.
- **UpdateAI#2**: Empty; eggs do not perform autonomous actions.
- **GetAI_boss_buru**: Factory function returning a new `boss_buruAI` instance.
- **GetAI_mob_buru_egg**: Factory function returning a new `mob_buru_eggAI` instance.
- **AddSC_boss_buru**: Registers both scripts with the `ScriptMgr`.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_buru

*Source:* boss_buru.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_buruAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset | method | InstanceData/SetData, Object/GetGUID, ObjectGuid/ObjectGuid#5, Unit.Main/RemoveAllAuras, Unit.Main/SetDisplayId, Unit.Main/UpdateSpeed, WorldObject.Object/AddObjectToRemoveList, WorldObject.Object/SummonCreature#2, ZoneScript/GetCreature | — | — |
| EnterCombat | method | Creature.Main/SetInCombatWithZone, CreatureAI/DoCast, CreatureAI/IsCombatMovementEnabled, CreatureAI/IsMeleeAttackEnabled, CreatureAI/SetCombatMovement, CreatureAI/SetMeleeAttack, InstanceData/SetData, Unit.Main/SetArmor | — | — |
| JustDied | method | InstanceData/SetData, Map.Main/GetPlayers, Unit.Main/RemoveAurasDueToSpell, WorldObject.Object/GetMap | — | — |
| AttackStart | method | CreatureAI/AttackStart | — | — |
| UpdateAI | method | Creature.Main/Respawn, Creature.Main/SetInCombatWithZone, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, CreatureAI/SetCombatMovement, CreatureAI/SetMeleeAttack, HostileReference/getUnitGuid, Map.Main/GetPlayer, Object/GetGUID, Object/ToPlayer, ObjectGuid/ObjectGuid#5, ScriptMgr/DoScriptText, shared_Util/urand, SpellCaster/CastSpell#2, ThreatManager/addThreat#3, ThreatManager/getThreat, ThreatManager/getThreatList, Unit.Main/AttackStop, Unit.Main/DeleteThreatList, Unit.Main/GetHealthPercent, Unit.Main/GetThreatManager, Unit.Main/GetVictim, Unit.Main/IsAlive, Unit.Main/RemoveAllAuras, Unit.Main/RemoveAurasDueToSpell, Unit.Main/SelectHostileTarget, Unit.Main/SetArmor, Unit.Main/SetFacingToObject, Unit.Main/UpdateSpeed, WorldObject.Object/AddObjectToRemoveList, WorldObject.Object/GetMap, WorldObject.Object/RemoveFlag, WorldObject.Object/SummonCreature#2, ZoneScript/GetCreature | — | — |
| mob_buru_eggAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset#2 | method | — | — | — |
| DamageTaken | method | Creature.Main/SetInCombatWithZone, InstanceData/GetData64, ObjectGuid/ObjectGuid#5, Unit.Main/IsInCombat, ZoneScript/GetCreature | — | — |
| JustDied#2 | method | Creature.Main/SetInCombatWithZone, SpellCaster/CastSpell#2, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/SummonCreature#2 | — | — |
| UpdateAI#2 | method | — | — | — |
| GetAI_boss_buru | function | — | — | — |
| GetAI_mob_buru_egg | function | — | — | — |
| AddSC_boss_buru | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
