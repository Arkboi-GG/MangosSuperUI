# boss_nefarian

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# boss_nefarian.cpp

**Purpose & Responsibilities**
This translation unit implements the artificial intelligence and spell behaviors for the **Nefarian** boss encounter in the Blackwing Lair instance, along with associated mechanics such as Corrupted Totems and Class-specific debuffs. It defines two primary AI classes (`boss_nefarianAI` and `npc_corrupted_totemAI`) and several spell/aura scripts that handle specific visual effects, target selection, and conditional logic for Nefarian’s abilities. The unit manages phase transitions, periodic ability casting, class-call targeting, and the lifecycle of summoned totems.

## Member-by-Member Behavior

### Nefarian Boss AI (`boss_nefarianAI`)
The main boss logic resides in `boss_nefarianAI`, which inherits from `ScriptedAI`. It manages timers for Nefarian’s abilities, handles phase transitions (flight to landing), and executes the "Class Call" mechanic.

*   **Constructor (`boss_nefarianAI`)**: Initializes the AI by retrieving the instance data via `WorldObject.Object/GetInstanceData` and calling `Reset` to initialize timers and state.
*   **`Reset`**: Resets all ability timers to randomized intervals within defined ranges. It initializes the `m_vPossibleCalls` vector with all player classes and their corresponding yell IDs. It sets `m_bTransitionDone` based on whether the creature is in the Blackwing Lair map, ensuring the entrance animation plays correctly on reset.
*   **`UpdateAI`**: The core update loop. It first handles the transition sequence (stages 0–2) if `m_bTransitionDone` is false. Once in combat, it checks and decrements timers for:
    *   **Shadow Flame**: Casts `SPELL_SHADOWFLAME` on the target.
    *   **Bellowing Roar**: Casts `SPELL_BELLOWING_ROAR`.
    *   **Veil of Shadow**: Casts `SPELL_VEIL_OF_SHADOW` on the victim.
    *   **Cleave**: Casts `SPELL_CLEAVE` on the victim.
    *   **Tail Lash**: Casts `SPELL_TAIL_LASH`.
    *   **Class Call**: If the timer expires, it selects a random class from `m_vPossibleCalls`. If a player of that class exists (`HandleClassCall`), it yells and resets the timer. If not, it removes that class from the possible calls list to avoid repeated failures.
    *   **Phase 3**: Triggers when health drops below 20%, casting `SPELL_RAISE_DRAKONID` and playing a death-related emote/yell.
    *   **Melee/Windfury**: Handles melee attacks and casts `SPELL_WINDFURY_TOTEM` if the passive aura is present but the active spell is not.
*   **`HandleClassCall`**: Iterates through players on the map. If a player of the specified class is found, alive, and not a GM, it applies the corresponding class-specific spell/aura (e.g., `SPELL_WARRIOR` adds an aura, `SPELL_MAGE` casts a spell). It returns `true` if a target was found, allowing the caller to remove the class from the rotation if no target exists.
*   **`MovementInform`**: Handles waypoint completion. Point 1 triggers movement to Point 2; Point 2 stops movement and resets the transition timer.
*   **`JustDied`**: Plays the death yell and updates the instance data via `InstanceData/SetData` to mark the boss as defeated.
*   **`EnterEvadeMode`**: Marks the instance as failed, triggers evasion for Nefarius (if linked via instance data), and schedules the creature for deletion.
*   **`KilledUnit`**: Plays a random kill yell.
*   **`JustSummoned`**: Ensures summoned creatures enter combat with the zone.

### Corrupted Totem AI (`npc_corrupted_totemAI`)
Handles the behavior of totems summoned by Nefarian’s Shaman class call.

*   **Constructor (`npc_corrupted_totemAI`)**: Sets the creature’s health to a random value between 200 and 2000 and calls `Reset`.
*   **`Reset`**: Roots the totem, adds avoidance aura, and initializes the check timer.
*   **`UpdateAI`**: Maintains the root aura. Depending on the totem type, it periodically calls `SetAura` to apply buffs/debuffs to nearby mobs (Nefarian and Drakonids). Fire Nova totems delete themselves after applying their effect.
*   **`SetAura`**: Finds nearby eligible mobs and applies or removes specific auras (Stoneskin, Healing Stream, Windfury) based on proximity and totem type.
*   **`JustDied`**: Removes the corresponding buff from nearby mobs when the totem dies.
*   **`Aggro`**: Forces the totem into combat with the zone.

### Spell & Aura Scripts
These scripts handle specific spell effects that require custom logic beyond standard spell definitions.

*   **`NefarianCorruptedTotemsScript` (`OnEffectExecute#3`)**: When Nefarian casts the Shaman class call, this script randomly selects one of four totem spells and casts it on Nefarian.
*   **`NefarianShadowFlamePassiveScript` (`OnEffectExecute#4`)**: Triggers the actual Shadow Flame damage/DoT spell when the passive aura is applied.
*   **`NefarianClassCallWarlockScript` (`OnEffectExecute#2`)**: Causes the targeted Warlock to summon two Corrupted Infernals.
*   **`NefarianClassCallRogueScript` (`OnEffectExecute`)**: Teleports the targeted Rogue to a random position near Nefarian, avoiding collisions.
*   **`NefarianClassCallMageAuraScript` (`OnBeforeApply`, `OnPeriodicTickEnd`)**: Applies a periodic aura to the Mage that randomly polymorphs other players in range every 5 seconds.
*   **`NefarianPolymorphAuraScript` (`OnAfterApply`)**: Changes the display ID of the polymorphed target to one of three random forms.

## Cross-Unit Boundaries

*   **`boss_nefarianAI` ↔ `ScriptedAI`**: Inherits base AI functionality. Calls `DoScriptText`, `DoCastSpellIfCan`, `DoMeleeAttackIfReady`, and `SetCombatMovement`.
*   **`boss_nefarianAI` ↔ `WorldObject`**: Uses `GetInstanceData` to access instance state, `GetMapId` for map checks, and `GetMap` to retrieve player lists.
*   **`boss_nefarianAI` ↔ `InstanceData`**: Calls `SetData` to update boss status (DONE/FAIL) and `GetData64` to retrieve Nefarius’s GUID for coordinated evasion.
*   **`boss_nefarianAI` ↔ `Creature`**: Uses `GetMotionMaster` for movement commands (`MovePoint`, `MoveIdle`, `MoveChase`), `SetInCombatWithZone`, `SetFly`, `SetWalk`, `RemoveAurasDueToSpell`, `HandleEmote`, `GetVictim`, `SelectHostileTarget`, `GetHealthPercent`, `HasAura`, `MonsterMoveWithSpeed`, and `CastSpell`.
*   **`boss_nefarianAI` ↔ `shared_Util`**: Uses `urand` for randomizing timers and class calls.
*   **`npc_corrupted_totemAI` ↔ `Unit`**: Uses `SetMaxHealth`, `SetHealth`, `AddAura`, `AddUnitState`, `HasAura`, `IsAlive`, `IsWithinDistInMap`, `RemoveAurasDueToSpell`, `CastCustomSpell`, `GetVictim`, `SelectHostileTarget`, and `DeleteLater`.
*   **`npc_corrupted_totemAI` ↔ `GridSearchers`**: Uses `GetCreatureListWithEntryInGrid` to find nearby mobs for aura application.
*   **Spell Scripts ↔ `SpellCaster`/`Spell`**: Use `CastSpell`, `GetUnitTarget`, and `m_casterUnit` to manipulate spell targets and casting.
*   **Spell Scripts ↔ `Unit`/`Player`**: Use `ToPlayer`, `IsPlayer`, `IsAlive`, `IsInWorld`, `HasAura`, `InterruptNonMeleeSpells`, `SetDisplayId`, `SetTransformScale`, and `TeleportTo`.
*   **Spell Scripts ↔ `WorldObject`**: Use `GetFirstCollisionPosition`, `GetMapId`, `GetMap`, and `IsWithinDist`.
*   **Spell Scripts ↔ `Aura`**: Use `GetEffIndex`, `GetTarget`, `SetPeriodicTimer`.

## Data Model
This unit does not interact with any database tables directly. All data is managed in-memory via instance data, creature states, and spell effects.

## Notable Implementation Details

*   **Class Call Rotation**: The `HandleClassCall` method removes classes from `m_vPossibleCalls` if no player of that class is present. This prevents the boss from repeatedly attempting to call a class that isn’t in the raid, ensuring variety in the remaining calls.
*   **Transition Animation**: The `UpdateAI` method uses a stage-based timer (`m_uiTransitionStage`) to orchestrate Nefarian’s entrance: flying to a point, landing, and then engaging combat. This ensures the visual sequence plays correctly before abilities begin.
*   **Totem Health Randomization**: Corrupted totems have randomized health (200–2000) upon summoning, adding variability to how long they persist.
*   **Polymorph Targeting**: The Mage class call script explicitly checks `IsInWorld` and distance to ensure valid targets, preventing exploits where a Mage leaves the group or map to avoid polymorphing allies.
*   **Collision Avoidance**: The Rogue teleport script uses `GetFirstCollisionPosition` to ensure the player isn’t teleported into walls or obstacles.

## Member Reference

*   **ClassCallInfo**: Constructor initializes class and yell ID.
*   **boss_nefarianAI**: Constructor retrieves instance data and resets state.
*   **Reset**: Resets timers, clears class calls, and initializes phase state.
*   **KilledUnit**: Plays random kill yell.
*   **JustDied**: Plays death yell and updates instance data.
*   **EnterEvadeMode**: Marks instance failed, evades Nefarius, and deletes creature.
*   **JustSummoned**: Sets summoned creature in combat with zone.
*   **MovementInform**: Handles waypoint completion for transition animation.
*   **HandleClassCall**: Finds player of specified class and applies class-specific spell/aura.
*   **UpdateAI**: Main update loop handling timers, phase transitions, and ability casting.
*   **GetAI_boss_nefarian**: Factory function for Nefarian AI.
*   **npc_corrupted_totemAI**: Constructor sets random health and resets state.
*   **Reset#2**: Roots totem and adds avoidance aura.
*   **Aggro**: Sets totem in combat with zone.
*   **JustDied#2**: Removes buffs from nearby mobs.
*   **SetAura**: Applies/removes buffs from nearby mobs based on totem type.
*   **UpdateAI#2**: Maintains root aura and periodically applies totem effects.
*   **GetAI_npc_corrupted_totem**: Factory function for Corrupted Totem AI.
*   **OnEffectExecute#3**: Randomly selects and casts a totem spell.
*   **GetScript_NefarianCorruptedTotems**: Factory function for Corrupted Totems spell script.
*   **OnEffectExecute#4**: Triggers Shadow Flame damage/DoT.
*   **GetScript_NefarianShadowFlamePassive**: Factory function for Shadow Flame Passive spell script.
*   **OnEffectExecute#2**: Summons two Corrupted Infernals for Warlock.
*   **GetScript_NefarianClassCallWarlock**: Factory function for Warlock Class Call spell script.
*   **OnEffectExecute**: Teleports Rogue to random position near Nefarian.
*   **GetScript_NefarianClassCallRogue**: Factory function for Rogue Class Call spell script.
*   **OnBeforeApply**: Sets periodic timer for Mage polymorph aura.
*   **OnPeriodicTickEnd**: Randomly polymorphs a player in range.
*   **GetScript_NefarianClassCallMage**: Factory function for Mage Class Call aura script.
*   **OnAfterApply**: Sets random display ID for polymorphed target.
*   **GetScript_NefarianPolymorph**: Factory function for Polymorph aura script.
*   **AddSC_boss_nefarian**: Registers all scripts in this unit.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_nefarian

*Source:* boss_nefarian.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ClassCallInfo | ctor | — | — | — |
| boss_nefarianAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset | method | shared_Util/urand, WorldObject.Object/GetMapId | — | — |
| KilledUnit | method | ScriptMgr/DoScriptText, shared_Util/urand | — | — |
| JustDied | method | InstanceData/SetData, ScriptMgr/DoScriptText | — | — |
| EnterEvadeMode | method | Creature.Main/AI, CreatureAI/EnterEvadeMode, InstanceData/GetData64, InstanceData/SetData, Map.Main/GetCreature, ObjectGuid/ObjectGuid#5, WorldObject.Object/DeleteLater, WorldObject.Object/GetMap | — | — |
| JustSummoned | method | Creature.Main/SetInCombatWithZone | — | — |
| MovementInform | method | Creature.MotionMaster/MoveIdle, Creature.MotionMaster/MovePoint, Unit.Main/GetMotionMaster | — | — |
| HandleClassCall | method | LinkedListHead/isEmpty, Map.Main/GetPlayers, Player.Main/IsGameMaster, SpellCaster/CastSpell#2, Unit.Main/AddAura, Unit.Main/GetClass, Unit.Main/IsAlive, WorldObject.Object/GetMap | — | — |
| UpdateAI | method | Creature.Main/SetInCombatWithZone, Creature.MotionMaster/MoveChase, Creature.MotionMaster/MovePoint, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, CreatureAI/SetCombatMovement, ScriptMgr/DoScriptText, shared_Util/urand, SpellCaster/CastSpell#2, Unit.Main/GetHealthPercent, Unit.Main/GetMotionMaster, Unit.Main/GetVictim, Unit.Main/HandleEmote, Unit.Main/HasAura#2, Unit.Main/MonsterMoveWithSpeed, Unit.Main/RemoveAurasDueToSpell, Unit.Main/SelectHostileTarget, Unit.Main/SetFly, Unit.Main/SetWalk | — | — |
| GetAI_boss_nefarian | function | — | — | — |
| npc_corrupted_totemAI | ctor | Object/GetEntry, ScriptedAI/ScriptedAI, shared_Util/urand, Unit.Main/SetHealth, Unit.Main/SetMaxHealth | — | — |
| Reset#2 | method | Unit.Main/AddAura, Unit.Main/AddUnitState, Unit.Main/HasAura#2 | — | — |
| Aggro | method | Creature.Main/SetInCombatWithZone | — | — |
| JustDied#2 | method | — | — | — |
| SetAura | method | GridSearchers/GetCreatureListWithEntryInGrid#2, SpellCaster/CastCustomSpell#2, Unit.Main/AddAura, Unit.Main/HasAura#2, Unit.Main/IsAlive, Unit.Main/RemoveAurasDueToSpell, WorldObject.Object/IsWithinDistInMap | — | — |
| UpdateAI#2 | method | Unit.Main/AddAura, Unit.Main/GetVictim, Unit.Main/HasAura#2, Unit.Main/SelectHostileTarget, WorldObject.Object/DeleteLater | — | — |
| GetAI_npc_corrupted_totem | function | — | — | — |
| OnEffectExecute#3 | method | SpellCaster/CastSpell#2 | — | — |
| GetScript_NefarianCorruptedTotems | function | — | — | — |
| OnEffectExecute#4 | method | Spell.Main/GetUnitTarget, SpellCaster/CastSpell#2 | — | — |
| GetScript_NefarianShadowFlamePassive | function | — | — | — |
| OnEffectExecute#2 | method | Spell.Main/GetUnitTarget, SpellCaster/CastSpell#2 | — | — |
| GetScript_NefarianClassCallWarlock | function | — | — | — |
| OnEffectExecute | method | Object/ToPlayer, Position/Position, shared_Util/frand, Spell.Main/GetUnitTarget, WorldLocation/WorldLocation#2, WorldObject.Object/GetFirstCollisionPosition, WorldObject.Object/GetMapId | — | — |
| GetScript_NefarianClassCallRogue | function | — | — | — |
| OnBeforeApply | method | Aura/GetEffIndex, Aura/SetPeriodicTimer | — | — |
| OnPeriodicTickEnd | method | Aura/GetTarget, LinkedListHead/isEmpty, Map.Main/GetPlayers, Object/IsInWorld, Object/IsPlayer, Object/ToPlayer, shared_Util/urand, SpellCaster/CastSpell#2, SpellCaster/InterruptNonMeleeSpells, Unit.Main/HasAura#2, Unit.Main/IsAlive, WorldObject.Object/GetMap, WorldObject.Object/IsWithinDist | — | — |
| GetScript_NefarianClassCallMage | function | — | — | — |
| OnAfterApply | method | Aura/GetEffIndex, Aura/GetTarget, shared_Util/urand, Unit.Main/SetDisplayId, Unit.Main/SetTransformScale | — | — |
| GetScript_NefarianPolymorph | function | — | — | — |
| AddSC_boss_nefarian | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
