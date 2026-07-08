# boss_mandokir

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# boss_mandokir

**Purpose & Responsibilities**

This unit implements the artificial intelligence and encounter logic for **Mandokir**, the second boss of the Zul'Gurub raid instance in World of Warcraft. It also contains the AI for three associated entities:
1.  **Ohgan**: Mandokir's raptor mount, which acts as an independent combatant.
2.  **Chained Spirits**: Summons that resurrect dead players during the encounter.
3.  **Vilebranch**: A nearby elite creature whose death state influences Mandokir's starting position.

The primary complexity lies in Mandokir's mechanics: he gains levels upon killing players, casts a "Watch" debuff that penalizes movement or aggression, charges distant players, and enrages if his mount (Ohgan) dies before him. The unit manages these states via timers, threat manipulation, and cross-entity communication through the `ScriptedInstance` interface.

## Member-by-Member Behavior

### Mandokir (`boss_mandokirAI`)

Mandokir is the central figure of the encounter. His AI manages multiple concurrent timers for abilities like Charge, Fear, Whirlwind, and Watch. He tracks specific player GUIDs for targeted mechanics and interacts with the instance data to coordinate with Ohgan and Jindo (another boss in the instance).

*   **Constructor (`boss_mandokirAI`)**: Initializes the AI, retrieves the instance data pointer, and calls `Reset`.
*   **`Reset`**: Resets all internal timers and flags. Sets Mandokir's level back to 63. Removes the raptor (Ohgan) and despawns any lingering spirits. Crucially, it checks the state of Vilebranch (`CheckVilebranchState`) to determine Mandokir's home position. If Vilebranch is dead, Mandokir starts at the bottom of the stairs; otherwise, he starts at his default location with pacified/spawning flags. It also notifies the instance that Ohgan is not started.
*   **`KilledUnit`**: Triggered when Mandokir kills a unit. If the victim is a player:
    1.  Casts `SPELL_LEVEL_UP` and increases Mandokir's level by 1.
    2.  Records the player's GUID in `m_uiPlayerToRez` to trigger a resurrection attempt by a Chained Spirit.
    3.  Plays a kill emote.
    4.  Randomly triggers a taunt from Jindo (if alive) or Mandokir himself.
*   **`Aggro`**: Plays the aggro emote, sets the creature in combat with the zone, and spawns the Chained Spirits. Note: The code comments suggest unmounting and spawning the raptor here, but this logic is commented out; instead, `CheckRaptor` handles the mount/raptor transition later.
*   **`CheckRaptor`**: Called periodically. Removes the mount aura. If the raptor isn't already dead, it spawns Ohgan. If Ohgan is marked as dead in the instance data (`TYPE_OHGAN == DONE`), Mandokir casts `SPELL_ENRAGE` and plays a rage emote.
*   **`CheckVilebranchState`**: Determines Mandokir's spawn point based on the life status of Vilebranch (NPC 11391). If Vilebranch is dead, Mandokir moves to a specific coordinate at the bottom of the stairs and removes pacifying flags. If Vilebranch is alive, he resets to his original home position and applies pacifying flags. This ensures proper positioning if the group wipes after killing Vilebranch.
*   **`CheckWatchedPlayer`**: Monitors the player currently under the `SPELL_WATCH` effect. It tracks if the player moves beyond a allowed range (2.0 yards normally, 8.0 if feared) or increases their threat against Mandokir. If either condition is met, the player is marked for execution (`m_uiTargetToKill`).
*   **`DespawnSpirits`**: Iterates through the list of spawned Chained Spirits and removes them from the world.
*   **`SpawnSpirits`**: Spawns 19 Chained Spirits at predefined coordinates stored in `aSpirits`.
*   **`DespawnRaptor`**: Removes the raptor (Ohgan) from the world if it exists.
*   **`SpawnRaptor`**: Summons Ohgan near Mandokir.
*   **`JustSummoned`**: If Ohgan is summoned, it immediately attacks Mandokir's current victim.
*   **`MoveInLineOfSight`**: Allows Mandokir to aggro players within 40 yards if he has no victim and Vilebranch is dead (ensuring he can engage even if he spawned at the bottom of the stairs).
*   **`SpellHitTarget`**: If Mandokir casts `SPELL_WATCH` on a target, it records the target's GUID, initial position, and initial threat level to monitor for violations.
*   **`UpdateAI`**: The main loop. It handles:
    *   **Resurrection Logic**: Checks if a player needs rez (`m_uiPlayerToRez`). Finds the nearest ready Chained Spirit and triggers its `SpellHitTarget` to initiate the rez process.
    *   **Global Cooldown**: Manages a simple global cooldown for spells.
    *   **Watch**: Casts `SPELL_WATCH` on a random player every ~20 seconds.
    *   **Decapitate**: If a watched player violated conditions, Mandokir adds massive threat to them, selects them as target, and casts `SPELL_DECAPITATE` (or kills them directly if out of range/cast fails).
    *   **Charge**: Charges the farthest player in range (8-40 yards). After charging, it sets up a delayed Fear.
    *   **Fear**: Casts Fear on the charged player if Mandokir is close enough (4 yards) after the charge.
    *   **Whirlwind**: Casts Whirlwind periodically.
    *   **Mortal Strike**: Casts on current victim if below 50% health.
    *   **Melee**: Standard melee attacks.

### Ohgan (`mob_ohganAI`)

Ohgan is Mandokir's raptor. It fights independently but communicates its death to the instance.

*   **Constructor (`mob_ohganAI`)**: Initializes timers and instance data.
*   **`Reset`**: Resets spell timers.
*   **`JustDied`**: Notifies the instance that Ohgan is done (`TYPE_OHGAN = DONE`), triggering Mandokir's enrage.
*   **`KilledUnit`**: If Ohgan kills a player, it finds Mandokir and manually calls Mandokir's `KilledUnit` method. This ensures Mandokir levels up and triggers resurrections even if Ohgan lands the killing blow.
*   **`UpdateAI`**: Handles standard tank-like behavior:
    *   **Sunder Armor**: Casts periodically.
    *   **Thrash**: Casts periodically.
    *   **Execute**: Casts on victims below 20% health.
    *   **Melee**: Standard attacks.

### Chained Spirits (`mob_chainedSpiritsAI`)

These spirits resurrect dead players.

*   **Constructor (`mob_chainedSpiritsAI`)**: Initializes state.
*   **`Reset`**: Clears resurrection timer.
*   **`GetData`**: Returns 1 if the spirit is available to resurrect someone (no target assigned), 0 otherwise. Used by Mandokir to find a ready spirit.
*   **`SpellHitTarget`**:
    *   If called with `nullptr` (by Mandokir), it assigns the dead player as its target, moves to them, and sets its home position to the player's location.
    *   If called with `SPELL_REVIVE`, it despawns itself (successful resurrection).
*   **`MovementInform`**: When the spirit reaches the player (point motion type 1), it starts a 2.5-second timer to cast the revive spell.
*   **`UpdateAI`**: If the timer expires, it attempts to cast `SPELL_REVIVE` on the target. If the target is no longer valid or has been resurrected by someone else, the spirit despawns.

### Vilebranch (`mob_vilebrancheAI`)

A simple elite creature. Its primary role is to signal its death state to Mandokir via the instance data (though the code shows `JustDied` is empty, the instance likely handles this elsewhere or Mandokir checks its life status directly as seen in `CheckVilebranchState`).

*   **Constructor (`mob_vilebrancheAI`)**: Standard init.
*   **`Reset`**: Empty.
*   **`JustDied`**: Empty.
*   **`UpdateAI`**: Standard melee combat.

### Registration Functions

*   **`GetAI_boss_mandokir`**, **`GetAI_mob_ohgan`**, **`GetAI_mob_chained_spirit`**, **`GetAI_mob_vilebranche`**: Factory functions returning new instances of the respective AI classes.
*   **`AddSC_boss_mandokir`**: Registers the scripts with the game server. Note that `mob_vilebranche` registration is commented out in the source, meaning this AI might not be active unless enabled elsewhere or if the creature template uses a different script name.

## Cross-Unit Boundaries

*   **`boss_mandokirAI` <-> `ScriptedInstance`**:
    *   **Direction**: Mandokir reads/writes instance data.
    *   **Why**: To coordinate with Ohgan's death state (`TYPE_OHGAN`) and potentially Jindo's location (`DATA_JINDO`). Mandokir sets `TYPE_OHGAN` to `NOT_STARTED` on reset.
*   **`boss_mandokirAI` <-> `mob_ohganAI`**:
    *   **Direction**: Bidirectional.
    *   **Why**: Mandokir summons Ohgan. Ohgan calls Mandokir's `KilledUnit` if it kills a player. Mandokir checks instance data for Ohgan's death to enrage.
*   **`boss_mandokirAI` <-> `mob_chainedSpiritsAI`**:
    *   **Direction**: Mandokir -> Spirits.
    *   **Why**: Mandokir summons spirits. When a player dies, Mandokir finds a ready spirit and calls its `SpellHitTarget` to initiate resurrection.
*   **`boss_mandokirAI` <-> `WorldObject`/`Map`**:
    *   **Direction**: Mandokir queries map for creatures/players.
    *   **Why**: To find Vilebranch, Jindo, specific players for Watch/Charge, and spirits for resurrection.

## Data Model

This unit does not interact directly with database tables. It relies on in-memory creature templates and instance data structures. The `creature_template` table is referenced in the SQL comment to assign the script name `mob_chained_spirit` to entry 15117.

## Notable Implementation Details

1.  **Vilebranch Positioning Logic**: The `CheckVilebranchState` method is critical for encounter flow. If Vilebranch is dead, Mandokir spawns at the bottom of the stairs (`-12195.0f, -1948.0f, 130.0f`). This prevents Mandokir from being inaccessible if the group wipes after killing Vilebranch but before engaging Mandokir. The method uses `FindNearestCreature` to check Vilebranch's status dynamically.
2.  **Watch Mechanic**: The `SPELL_WATCH` mechanic is complex. It doesn't just apply a debuff; it actively monitors the player's position and threat. `CheckWatchedPlayer` runs every update tick. If the player moves more than 2 yards (or 8 if feared) or increases threat, they are marked for `DECAPITATE`. This requires precise tracking of initial position and threat values.
3.  **Leveling System**: Mandokir levels up on every player kill. This is handled in `KilledUnit` by casting `SPELL_LEVEL_UP` and calling `SetLevel`. This increases his stats dynamically.
4.  **Ohgan Killing Blow**: Ohgan's `KilledUnit` explicitly finds Mandokir and calls his `KilledUnit`. This is a crucial integration point because otherwise, Mandokir wouldn't level up or trigger resurrections if Ohgan landed the final hit.
5.  **Resurrection Queue**: Mandokir stores the GUID of the last killed player in `m_uiPlayerToRez`. In `UpdateAI`, he searches for a spirit with `GetData(0) == 1` (available). This creates a queue-like behavior where spirits are assigned to dead players sequentially.
6.  **Commented Out Code**: The `Aggro` method has commented-out code for unmounting and spawning the raptor. Instead, `CheckRaptor` handles this. This suggests a design choice to delay raptor spawning or handle it differently than initially planned.
7.  **Vilebranch Script Registration**: The `AddSC_boss_mandokir` function has the registration for `mob_vilebranche` commented out. This means the `mob_vilebrancheAI` class defined in this file is likely unused unless the creature template points to a different script or the comment is outdated. However, Mandokir still checks for Vilebranch's existence via `FindNearestCreature`, so the creature itself exists, but its AI might be default or defined elsewhere.

## Member Reference

*   **boss_mandokirAI**: Constructor for Mandokir's AI. Initializes instance data and calls Reset.
*   **Reset**: Resets Mandokir's timers, level, and state. Despawns raptor and spirits. Checks Vilebranch state for positioning.
*   **KilledUnit**: Handles player deaths: levels up Mandokir, triggers resurrection, plays emotes, and potentially taunts Jindo.
*   **Aggro**: Plays aggro emote, sets combat state, spawns spirits.
*   **CheckRaptor**: Manages raptor spawning and enrage if Ohgan is dead.
*   **CheckVilebranchState**: Adjusts Mandokir's home position based on Vilebranch's life status.
*   **CheckWatchedPlayer**: Monitors watched player for movement/threat violations.
*   **DespawnSpirits**: Removes all spawned Chained Spirits.
*   **SpawnSpirits**: Spawns 19 Chained Spirits at fixed locations.
*   **DespawnRaptor**: Removes Ohgan from the world.
*   **SpawnRaptor**: Summons Ohgan near Mandokir.
*   **JustSummoned**: Makes Ohgan attack Mandokir's victim upon summoning.
*   **MoveInLineOfSight**: Allows aggro from distance if Vilebranch is dead.
*   **SpellHitTarget**: Records target info when Watch is cast.
*   **UpdateAI**: Main logic loop for Mandokir: handles resurrections, Watch, Decapitate, Charge, Fear, Whirlwind, Mortal Strike, and melee.
*   **mob_ohganAI**: Constructor for Ohgan's AI.
*   **Reset#3**: Resets Ohgan's spell timers.
*   **JustDied**: Notifies instance of Ohgan's death.
*   **KilledUnit#2**: Delegates player kills to Mandokir's AI.
*   **UpdateAI#3**: Ohgan's combat loop: Sunder Armor, Thrash, Execute, Melee.
*   **GetAI_boss_mandokir**: Factory function for Mandokir's AI.
*   **GetAI_mob_ohgan**: Factory function for Ohgan's AI.
*   **mob_chainedSpiritsAI**: Constructor for Chained Spirit's AI.
*   **Reset#2**: Resets spirit's resurrection timer.
*   **GetData**: Returns availability for resurrection.
*   **SpellHitTarget#2**: Assigns resurrection target or despawns on successful revive.
*   **MovementInform**: Starts resurrection timer upon reaching player.
*   **UpdateAI#2**: Executes resurrection spell or despawns spirit.
*   **GetAI_mob_chained_spirit**: Factory function for Chained Spirit's AI.
*   **mob_vilebrancheAI**: Constructor for Vilebranch's AI.
*   **Reset#4**: Empty reset.
*   **JustDied#2**: Empty death handler.
*   **UpdateAI#4**: Simple melee combat loop.
*   **GetAI_mob_vilebranche**: Factory function for Vilebranch's AI.
*   **AddSC_boss_mandokir**: Registers all scripts. Note: Vilebranch registration is commented out.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_mandokir

*Source:* boss_mandokir.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_mandokirAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset | method | Creature.Main/ResetStats, CreatureAI/DoCastSpellIfCan, InstanceData/SetData, ObjectGuid/Clear, Unit.Main/SetLevel | — | — |
| KilledUnit | method | CreatureAI/DoCastSpellIfCan, InstanceData/GetData64, Map.Main/GetCreature, Object/GetGUID, Object/GetTypeId, ObjectGuid/ObjectGuid#5, ScriptMgr/DoScriptText, shared_Util/urand, Unit.Main/GetLevel, Unit.Main/IsAlive, Unit.Main/SetLevel, WorldObject.Object/GetMap | — | — |
| Aggro | method | Creature.Main/SetInCombatWithZone, ScriptMgr/DoScriptText | — | — |
| CheckRaptor | method | CreatureAI/DoCastSpellIfCan, InstanceData/GetData, ScriptMgr/DoScriptText, Unit.Main/RemoveAurasDueToSpell | — | — |
| CheckVilebranchState | method | Creature.Main/ResetHomePosition, Creature.Main/SetHomePosition, Creature.MotionMaster/MoveTargetedHome, Unit.Main/GetMotionMaster, Unit.Main/IsAlive, WorldObject.Object/FindNearestCreature, WorldObject.Object/RemoveFlag, WorldObject.Object/SetFlag | — | — |
| CheckWatchedPlayer | method | Map.Main/GetUnit, ObjectGuid/ObjectGuid#5, ThreatManager/getThreat, Unit.Main/GetThreatManager, Unit.Main/HasAura#2, Unit.Main/IsAlive, WorldObject.Object/GetMap, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/IsWithinDist2d | — | — |
| DespawnSpirits | method | Map.Main/GetCreature, ObjectGuid/ObjectGuid#5, Unit.Main/IsAlive, WorldObject.Object/AddObjectToRemoveList, WorldObject.Object/GetMap | — | — |
| SpawnSpirits | method | Object/GetGUID, WorldObject.Object/SummonCreature#2 | — | — |
| DespawnRaptor | method | Map.Main/GetCreature, ObjectGuid/Clear, ObjectGuid/IsEmpty, Unit.Main/IsAlive, WorldObject.Object/AddObjectToRemoveList, WorldObject.Object/GetMap | — | — |
| SpawnRaptor | method | Object/GetObjectGuid, ObjectGuid/IsEmpty, WorldObject.Object/SummonCreature#2 | — | — |
| JustSummoned | method | Creature.Main/AI, CreatureAI/AttackStart, Object/GetEntry, Unit.Main/GetVictim | — | — |
| MoveInLineOfSight | method | CreatureAI/AttackStart, Object/ToPlayer, Player.Main/IsGameMaster, Unit.Main/GetVictim, WorldObject.Object/GetDistance#3 | — | — |
| SpellHitTarget | method | Object/GetGUID, ThreatManager/getThreat, Unit.Main/GetThreatManager, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY | — | — |
| UpdateAI | method | Creature.Main/AI, Creature.Main/GetFarthestVictimInRange, Creature.Main/SelectAttackingTarget, Creature.MotionMaster/MoveChase, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, CreatureAI/GetData, CreatureAI/SpellHitTarget, Map.Main/GetCreature, Map.Main/GetPlayer, Map.Main/GetUnit, Object/GetObjectGuid, ObjectGuid/Clear, ObjectGuid/ObjectGuid#5, Player.Main/IsRessurectRequested, ScriptMgr/DoScriptText, shared_Util/urand, SpellCaster/IsNonMeleeSpellCasted, ThreatManager/addThreat#3, ThreatManager/getThreat, ThreatManager/modifyThreatPercent#2, Unit.Main/DoKillUnit, Unit.Main/GetCharmerOrOwnerPlayerOrPlayerItself, Unit.Main/GetDeathState, Unit.Main/GetHealthPercent, Unit.Main/GetMotionMaster, Unit.Main/GetThreatManager, Unit.Main/GetVictim, Unit.Main/IsAlive, Unit.Main/SelectHostileTarget, WorldObject.Object/GetDistance#3, WorldObject.Object/GetMap, WorldObject.Object/IsInRange, WorldObject.Object/MonsterWhisper | — | — |
| mob_ohganAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset#3 | method | shared_Util/urand | — | — |
| JustDied | method | InstanceData/SetData | — | — |
| KilledUnit#2 | method | Creature.Main/AI, CreatureAI/KilledUnit, Object/GetTypeId, Unit.Main/IsInCombat, WorldObject.Object/FindNearestCreature | — | — |
| UpdateAI#3 | method | CreatureAI/DoCast, CreatureAI/DoMeleeAttackIfReady, shared_Util/urand, Unit.Main/GetHealth, Unit.Main/GetMaxHealth, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_boss_mandokir | function | — | — | — |
| GetAI_mob_ohgan | function | — | — | — |
| mob_chainedSpiritsAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#2 | method | — | — | — |
| GetData | method | — | — | — |
| SpellHitTarget#2 | method | Creature.Main/SetHomePosition, Creature.MotionMaster/MovePoint, Object/GetGUID, Unit.Main/GetMotionMaster, WorldObject.Object/DeleteLater, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ | — | — |
| MovementInform | method | Map.Main/GetPlayer, ObjectGuid/ObjectGuid#5, WorldObject.Object/GetMap | — | — |
| UpdateAI#2 | method | Map.Main/GetPlayer, ObjectGuid/ObjectGuid#5, Player.Main/IsRessurectRequested, SpellCaster/CastSpell#2, Unit.Main/GetDeathState, WorldObject.Object/AddObjectToRemoveList, WorldObject.Object/GetMap | — | — |
| GetAI_mob_chained_spirit | function | — | — | — |
| mob_vilebrancheAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#4 | method | — | — | — |
| JustDied#2 | method | — | — | — |
| UpdateAI#4 | method | CreatureAI/DoMeleeAttackIfReady, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_mob_vilebranche | function | — | — | — |
| AddSC_boss_mandokir | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
