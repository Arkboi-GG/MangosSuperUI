<!-- provenance: verbose -->
# boss_nerubenkan

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`boss_nerubenkan.cpp` implements the AI for **Nerub'enkan**, a boss in the *Stratholme* dungeon. The `boss_nerubenkanAI` class manages three primary combat mechanics: casting *Encasing Webs* (which manually removes and restores threat), casting *Pierce Armor*, and summoning *Undead Scarabs* or *Crypt Scarabs*. It signals instance progression upon death.

## Member-by-Member Behavior

### Initialization and State

**`boss_nerubenkanAI`**  
Constructs the AI, retrieves the `ScriptedInstance` pointer via `WorldObject::GetInstanceData`, and calls `Reset()`.

**`Reset`**  
Initializes timers (*Encasing Webs*: 7s, *Pierce Armor*: 15s, *Raise Undead Scarab*: 3s) and clears `WebbedPlayerGuid` and `WebbedPlayerAggro`.

### Combat Mechanics

**`UpdateAI`**  
The main update loop. It returns early if no hostile target exists. If `WebbedPlayerGuid` is set, it retrieves the player via `Map::GetPlayer`; if the `SPELL_ENCASINGWEBS` aura is gone, it restores the stored threat using `ThreatManager::addThreatDirectly` and clears the tracking variables. It then processes three independent timers:
1.  **Encasing Webs**: Casts on the victim. On success, it stores the victim’s GUID and current threat, reduces their threat by 100% via `ThreatManager::modifyThreatPercent`, and resets the timer to 10–15s.
2.  **Pierce Armor**: Casts on the victim. Resets timer to 15–20s.
3.  **Raise Undead Scarab**: Selects a random target and calls `RaiseUndeadScarab`. Resets timer to 6–10s.
Finally, it attempts a melee attack.

**`RaiseUndeadScarab`**  
Spawns minions targeting `victim`. If `crypt` is true, it spawns 4, 6, or 8 *Crypt Scarabs* (entry 10577) based on `urand(0, 2)`. If false, it spawns one *Undead Scarab* (entry 10876). All summons despawn after 10s out of combat and are ordered to attack `victim` via `CreatureAI::AttackStart`.

### Lifecycle and Registration

**`JustDied`**  
Calls `InstanceData::SetData` to mark `TYPE_NERUB` as `DONE`.

**`GetAI_boss_nerubenkan`**  
Factory function returning a new `boss_nerubenkanAI` instance.

**`AddSC_boss_nerubenkan`**  
Creates a `Script` struct, sets its name and AI getter, and registers it via `ScriptMgr::RegisterSelf`.

## Cross-Unit Boundaries

*   **`ScriptedAI`**: Base class providing `DoCastSpellIfCan`, `DoMeleeAttackIfReady`, `DoSpawnCreature`, and access to `m_creature`.
*   **`WorldObject::GetInstanceData`**: Used in the constructor to obtain the instance script pointer.
*   **`Creature::AI` / `CreatureAI::AttackStart`**: Used in `RaiseUndeadScarab` to direct summoned minions.
*   **`ThreatManager`**: `getThreat`, `modifyThreatPercent`, and `addThreatDirectly` are used in `UpdateAI` to manually manipulate threat for *Encasing Webs*.
*   **`Unit`**: `GetVictim`, `SelectHostileTarget`, `HasAura`, and `GetThreatManager` are used for target selection and state checks.
*   **`Map::GetPlayer`**: Used in `UpdateAI` to retrieve the webbed player object by GUID.
*   **`InstanceData::SetData`**: Called in `JustDied` to update instance state.
*   **`ScriptMgr::RegisterSelf`**: Called in `AddSC_boss_nerubenkan` to register the script.

## Data Model

This unit does not interact with any database tables.

## Notable Implementation Details

*   **Manual Threat Manipulation**: *Encasing Webs* does not use a built-in threat-clearing spell effect. `UpdateAI` explicitly saves the target’s threat, removes 100% of it, and restores it when the aura expires. This requires tracking the player’s GUID and threat value in member variables.
*   **Summons Focus Fire**: `RaiseUndeadScarab` passes the specific `victim` to `AttackStart`, ensuring all spawned scarabs attack the same target the boss selected.
*   **Independent Timers**: All abilities use separate timers, allowing for overlapping casts.

## Member Reference

**`boss_nerubenkanAI`**  
Constructor that initializes the instance pointer and calls `Reset()`.

**`Reset`**  
Resets timers and clears webbed player state.

**`RaiseUndeadScarab`**  
Spawns 4–8 Crypt Scarabs or 1 Undead Scarab, commanding them to attack the specified victim.

**`JustDied`**  
Notifies the instance script that the boss is defeated.

**`UpdateAI`**  
Manages spell timers, handles threat removal/restoration for *Encasing Webs*, and executes melee attacks.

**`GetAI_boss_nerubenkan`**  
Factory function creating the AI instance.

**`AddSC_boss_nerubenkan`**  
Registers the script with the script manager.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_nerubenkan

*Source:* boss_nerubenkan.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_nerubenkanAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset | method | — | — | — |
| RaiseUndeadScarab | method | Creature.Main/AI, CreatureAI/AttackStart, ScriptedAI/DoSpawnCreature#2, shared_Util/urand | — | — |
| JustDied | method | InstanceData/SetData | — | — |
| UpdateAI | method | Creature.Main/SelectAttackingTarget, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, Map.Main/GetPlayer, Object/GetGUID, ObjectGuid/ObjectGuid#5, shared_Util/urand, ThreatManager/addThreatDirectly, ThreatManager/getThreat, ThreatManager/modifyThreatPercent#2, Unit.Main/GetThreatManager, Unit.Main/GetVictim, Unit.Main/HasAura#2, Unit.Main/SelectHostileTarget, WorldObject.Object/GetMap | — | — |
| GetAI_boss_nerubenkan | function | — | — | — |
| AddSC_boss_nerubenkan | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
