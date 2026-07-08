# instance_blackwing_lair

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# instance_blackwing_lair

## Purpose & Responsibilities

`instance_blackwing_lair` is the script module implementing the instance logic, creature artificial intelligence (AI), and game object interactions for the **Blackwing Lair** raid dungeon in the WoW server emulation. It manages the state of multiple boss encounters (Razorgore the Untamed, Vaelastrasz the Corrupt, Broodlord Lashlayer, the FiremaW/Ebonroc/Flamgor trio, Chromaggus, and Lord Victor Nefarian), handles environmental mechanics (such as the Orb of Domination possession mechanic and suppression engines), and controls the spawning, despawning, and behavior of various NPCs and game objects within the instance.

Key responsibilities include:
1.  **Instance State Management:** Tracking encounter progress (`NOT_STARTED`, `IN_PROGRESS`, `DONE`, `FAIL`) for all bosses and special events (like the Scepter Run).
2.  **Dynamic Spawning:** Randomizing the element type of Corrupted Whelps upon spawn/respawn and managing the lifecycle of temporary summons (e.g., the temporary Nefarian during the Vael event).
3.  **Complex AI Coordination:** Implementing a shared threat system for Blackwing Technicians via the `blackwing_technicians_helper` struct, ensuring they attack the same target.
4.  **Environmental Interactions:** Handling door states, egg breaking mechanics for Razorgore, and area triggers for resurrection and event progression.

## Member-by-Member Behavior

### Instance Data Structure (`instance_blackwing_lair`)

This class inherits from `ScriptedInstance` and serves as the central brain for the instance.

*   **`instance_blackwing_lair` (ctor):** Initializes the instance data arrays (`m_auiEncounter`, `m_auiData`) and sets up random elemental compositions for Chromaggus and Nefarian breath attacks using `shared_Util/urand`. It also initializes the `blackwing_technicians_helper`.
*   **`Initialize`:** Resets encounter states and randomizes the elemental breath colors for Chromaggus and Nefarian.
*   **`IsEncounterInProgress`:** Checks if any encounter (excluding the Scepter Run) is currently `IN_PROGRESS`. This prevents certain global resets or actions while a fight is active.
*   **`OnObjectCreate`:** Triggered when a Game Object (GO) spawns. It records GUIDs for important doors and objects (Razorgore doors, Nefarian door, Chromaggus doors, etc.). It also applies initial state logic:
    *   Opens doors if the associated boss is already `DONE`.
    *   Deletes eggs and suppression engines if their respective bosses are `DONE`.
    *   *Note:* There is a missing `break` after `GO_ORB_OF_DOMINATION`, causing it to fall through to `GO_BLACK_DRAGON_EGG` logic, potentially deleting the orb if Razorgore is done (though the orb is typically deleted by other means, this is a logical overlap).
*   **`OnCreatureEnterCombat`:**
    *   For Razorgore's adds (Grethok, Guardsman): Starts the Razorgore encounter if not started. Pulls Razorgore into combat if he is alive and not already fighting.
    *   For Razorgore: Pulls Grethok into combat if alive and not fighting.
*   **`OnCreatureEvade`:** Applies permanent elemental immunity auras to Corrupted Whelps (Green/Frost, Blue/Frost, Red/Fire, Bronze/Arcane) when they evade combat, ensuring they retain their randomized element type.
*   **`OnCreatureRespawn`:** Handles creature respawns based on encounter state:
    *   Deletes Orb of Domination and Razorgore adds if Razorgore is `DONE`.
    *   Randomizes the element of Corrupted Whelps on respawn.
    *   Hides or deletes Whelps and Lashlayer adds depending on whether Lashlayer is `DONE`, `IN_PROGRESS`, or `FAIL`.
    *   Deletes FiremaW/Ebonroc/Flamgor adds if all three are `DONE`.
    *   Deletes Vaelastrasz and Nefarian if their encounters are `DONE`.
*   **`OnCreatureDeath`:** Plays a specific death say for Razorgore if the encounter failed.
*   **`OnCreatureCreate`:** Records GUIDs for all major bosses and key NPCs.
    *   Randomizes the element of Corrupted Whelps on creation.
    *   Deletes Whelps and Lashlayer adds if Lashlayer is `DONE` or `IN_PROGRESS`.
    *   Manages the "Vael Gob" technicians (those below Z=420), marking them for deletion if the Vael Event is `DONE` or hiding them if Razorgore isn't done.
    *   Deletes FiremaW/Ebonroc/Flamgor adds if all three are `DONE`.
*   **`OnPlayerDeath`:** Removes the dead player from the `blackwing_technicians_helper`'s threat list to prevent technicians from targeting a corpse.
*   **`GetData64` / `GetData`:** Retrieves stored GUIDs or encounter states.
*   **`SetData`:** The primary interface for updating instance state.
    *   **Razorgore:** Opens/closes doors based on `IN_PROGRESS`, `FAIL`, or `DONE`. On `FAIL`, it respawns eggs. On `DONE`, it reveals hidden technicians.
    *   **Vaelastrasz:** Manages door states for the Vael room.
    *   **Lashlayer:** Opens the exit door on `DONE`.
    *   **FiremaW/Ebonroc/Flamgor:** Tracks completion.
    *   **Chromaggus:** Opens exit door on `DONE`.
    *   **Nefarian:** Opens/closes the Nefarian door.
    *   **Vael Event:** Moves "Vael Gobs" (technicians) to a specific location when the event is `DONE`.
    *   **Egg Mechanic:** Counts broken eggs. If 30 are broken, marks the egg phase as `DONE`. On `FAIL`, deletes the trigger creature.
    *   **Scepter Run:** Tracks time and champion.
    *   **Persistence:** Saves instance data to the database if an encounter is `DONE`, or if Scepter Run data changes.
*   **`CheckConditionCriteriaMeet`:** Validates criteria for quest rewards related to the Scepter Run. Returns `true` for all players if the run failed (alternate success), or only for the champion if the run succeeded.
*   **`Save` / `Load`:** Serializes and deserializes instance state (encounter statuses, random seeds, scepter data) to/from the database. Ensures encounters are not loaded as `IN_PROGRESS` (resetting them to `NOT_STARTED`).
*   **`RespawnEggs`:** Deletes existing Black Dragon Eggs near Razorgore and summons 30 new ones at predefined coordinates (`EggSpawnCoords`).
*   **`GetTechnicianHelper`:** Returns a pointer to the internal `blackwing_technicians_helper` instance.

### Technician Helper (`blackwing_technicians_helper`)

A standalone struct used to coordinate the AI of Blackwing Technicians.

*   **`blackwing_technicians_helper` (ctor):** Initializes the helper with a reference to the instance.
*   **`AddTechnician`:** Adds a technician's GUID to the tracking list. It iterates through the technician's current threat list, adding the threat values to a shared map (`m_mThreatGuid`) keyed by victim GUID. Crucially, it reduces the individual technician's threat toward those targets by 100% to prevent them from attacking independently.
*   **`RemoveTechnician`:** Removes a technician from the tracking list. If no technicians remain, it clears the shared threat map.
*   **`GetVictimGuid`:** Iterates through the shared threat map to find the victim with the highest accumulated threat. This determines the common target for all technicians.
*   **`RemovePotentialVictim`:** Removes a specific victim GUID from the threat map (used when a player dies).
*   **`GetInstance`:** Returns the parent instance pointer.
*   **`RecalculateThreat`:** Periodically updates the shared threat map. It uses a counter (`m_uiTechniciansUpdate`) to buffer updates, iterating through all active technicians, copying their threat lists, adding to the shared map, and reducing individual threat by 100%. This ensures all technicians focus fire on the most threatened target.

### Game Object Scripts

*   **`GOHello_go_orb_of_domination`:** Handles interaction with the Orb of Domination.
    *   Checks if the player has the Mind Exhaustion aura.
    *   Verifies Razorgore is not already possessed or evading.
    *   If Razorgore is in combat and eggs aren't all broken, casts `Mind Exhaustion` on the player and `Possess` on Razorgore.
    *   Sets up visual channeling effects between a trigger creature and Razorgore.
*   **`go_egg_razAI`:** AI for Black Dragon Eggs.
    *   **`OnUse`:** When clicked (by Razorgore), it increments the egg count in the instance data. If Razorgore is the user, it plays a random say. If all eggs are broken, it removes auras from Razorgore, sets his max health to a specific value, and casts `Warming Flames`. Finally, it deletes the egg.
*   **`go_engin_suppressionAI`:** AI for Suppression Engines.
    *   **`OnUse`:** If a player is nearby, it deactivates the engine and starts a timer to reactivate.
    *   **`ApplyAura`:** Casts a suppression aura on all non-stealthed, alive, non-GM players within 15 yards.
    *   **`RestoreGo`:** Reactivates the engine if Lashlayer is not `DONE`.
    *   **`UpdateAI`:** Manages timers for applying auras and randomly restoring the engine if deactivated.

### Area Triggers

*   **`AreaTrigger_at_orb_of_command`:** Resurrects dead players who have completed the "Blackhand's Command" quest and are standing on the Orb of Command area trigger. Teleports them to a specific spot.
*   **`AreaTrigger_at_enter_vael_room`:** Marks the Vael Event as `DONE` when a non-GM player enters the Vael room, triggering the movement of technicians.

### Creature AI Scripts

*   **`npc_death_talonAI`:** AI for Death Talon creatures (Hatcher, Seether, Wyrmkin, Flamescale, Captain, Overseer).
    *   **Constructor:** Randomly assigns a Brood Power (elemental buff) and a School Sensitivity (vulnerability). Identifies if the creature is an Overseer.
    *   **`Reset` / `JustDied`:** Resets timers and re-randomizes powers/sensitivities.
    *   **`Aggro`:** Non-Overseers call for help from nearby allies.
    *   **`UpdateAI`:**
        *   Applies Brood Power and Sensitivity auras if missing.
        *   Non-Overseers cast `Cleave` and `Warstomp`.
        *   Overseers cast `Fire Blast` on random targets.
        *   Performs melee attacks.
*   **`npc_blackwing_technicianAI`:** AI for Blackwing Technicians.
    *   **Constructor:** Identifies if the technician is a "Vael Gob" (below Z=420). Links to the `blackwing_technicians_helper`.
    *   **`Reset`:** Removes itself from the helper if previously added. Resets timers.
    *   **`Aggro`:** Adds itself to the helper's tracking list.
    *   **`JustDied`:** Removes itself from the helper.
    *   **`UpdateAI`:**
        *   Deletes Vael Gobs if they move above Z=430.
        *   Syncs threat with the helper periodically.
        *   Determines the victim from the helper's shared threat list.
        *   Attacks the victim.
        *   Casts `Poison Bottle` if in line of sight, otherwise throws `Bomb` at the victim's location.
        *   Performs melee attacks.
*   **`CorruptedWhelpAI`:** Simple AI for Corrupted Whelps.
    *   **`UpdateAI`:** Selects hostile targets and performs melee attacks. No special spells.

### Registration

*   **`AddSC_instance_blackwing_lair`:** Registers all scripts (instance data, GOs, area triggers, and creature AIs) with the `ScriptMgr`.

## Cross-Unit Boundaries

*   **`instance_blackwing_lair` <-> `boss_victor_nefarius`:** Calls `NefariusGossipOptionClicked` when the Nefarian gossip option is triggered via `SetData`.
*   **`instance_blackwing_lair` <-> `ScriptedInstance`:** Inherits from `ScriptedInstance` to access standard instance methods like `DoUseDoorOrButton`, `SaveToDB`, and `instance->GetCreature/GameObject`.
*   **`instance_blackwing_lair` <-> `WorldObject`/`Map`/`Unit`/`Creature`/`GameObject`:** Extensively uses these core classes to manipulate entities (setting states, deleting, summoning, checking positions, applying auras).
*   **`blackwing_technicians_helper` <-> `ThreatManager`/`HostileReference`:** Directly accesses and modifies threat lists and percentages to implement the shared threat mechanic.
*   **`go_engin_suppressionAI` <-> `Map::PlayerList`:** Iterates over all players in the map to apply suppression auras.
*   **`npc_death_talonAI` <-> `CreatureAI`/`ScriptedAI`:** Inherits base AI functionality and uses helper methods like `DoCastSpellIfCan` and `SelectAttackingTarget`.
*   **`npc_blackwing_technicianAI` <-> `basicAI`/`ScriptedAI`:** Inherits base AI functionality. Overrides `MoveInLineOfSight` to conditionally ignore line-of-sight checks for Vael Gobs.

## Data Model

This unit does not directly query or modify database tables via SQL statements in the source code. It relies on the `ScriptedInstance` base class to handle persistence via `SaveToDB()` and `Load()`, which interact with the `instance_data` table (or equivalent) in the database. The specific columns and schema are managed by the core engine, not this script. The script serializes its state into a string format (`strInstData`) containing space-separated integers representing encounter states and random seeds.

## Notable Implementation Details

1.  **Shared Threat System:** The `blackwing_technicians_helper` implements a custom threat aggregation system. Instead of relying solely on the engine's threat management, it manually sums threat from all technicians into a shared map and forces individual technicians to reduce their personal threat by 100%. This ensures they always attack the same target. The `RecalculateThreat` method uses a buffered update mechanism (`m_uiTechniciansUpdate`) to avoid performance issues or infinite loops during rapid threat changes.
2.  **Elemental Randomization:** Corrupted Whelps and Death Talons have randomized elemental properties (immunity/vulnerability) assigned at spawn/death. This is handled in `OnCreatureCreate`, `OnCreatureRespawn`, and the AI constructors. The specific spell IDs for immunities and vulnerabilities are defined in enums.
3.  **Vael Event Technicians:** Technicians spawned below Z=420 are treated specially ("Vael Gobs"). They are hidden until the Vael Event is marked `DONE`, at which point they are moved to a specific location. They are also deleted if they move above Z=430, likely to prevent them from interfering with later phases if they escape their designated area.
4.  **Missing Break Statements:** In `OnObjectCreate` and `OnCreatureCreate`, there are intentional or unintentional missing `break` statements after `GO_ORB_OF_DOMINATION` and `NPC_GRETHOK_THE_CONTROLLER`/`NPC_BLACKWING_TECHNICIAN` cases. This causes fall-through behavior, which may lead to unintended side effects (e.g., the Orb being processed as an Egg).
5.  **Hardcoded Coordinates:** The `EggSpawnCoords` array contains hardcoded coordinates for respawning Black Dragon Eggs. Any changes to the map geometry would require updating these values.
6.  **Scepter Run Logic:** The `CheckConditionCriteriaMeet` function handles quest reward conditions for the Scepter Run, distinguishing between a complete failure (rewarding all players) and a success (rewarding only the champion).

## Member Reference

*   **`blackwing_technicians_helper`**: Constructor for the technician helper struct.
*   **`AddTechnician`**: Adds a technician to the shared threat tracking system.
*   **`RemoveTechnician`**: Removes a technician from the shared threat tracking system.
*   **`GetVictimGuid`**: Returns the GUID of the target with the highest aggregated threat.
*   **`RemovePotentialVictim`**: Removes a specific victim from the shared threat map.
*   **`GetInstance`**: Returns the parent instance pointer.
*   **`RecalculateThreat`**: Updates the shared threat map from all active technicians.
*   **`instance_blackwing_lair`**: Constructor for the instance script.
*   **`Initialize`**: Resets instance state and randomizes elemental breaths.
*   **`IsEncounterInProgress`**: Checks if any boss encounter is active.
*   **`OnObjectCreate`**: Handles Game Object spawning and initial state setup.
*   **`OnCreatureEnterCombat`**: Triggers boss pulls and encounter starts.
*   **`OnCreatureEvade`**: Applies elemental immunities to whelps.
*   **`OnCreatureRespawn`**: Manages creature respawns based on encounter state.
*   **`OnCreatureDeath`**: Plays death says for Razorgore.
*   **`OnCreatureCreate`**: Records GUIDs, randomizes elements, and manages initial spawns.
*   **`OnPlayerDeath`**: Removes dead players from technician threat lists.
*   **`GetData64`**: Retrieves stored GUIDs.
*   **`SetData`**: Updates instance state, manages doors, saves data.
*   **`CheckConditionCriteriaMeet`**: Validates Scepter Run quest rewards.
*   **`Save`**: Serializes instance state to a string.
*   **`GetData`**: Retrieves encounter states.
*   **`Load`**: Deserializes instance state from a string.
*   **`RespawnEggs`**: Deletes and respawns Black Dragon Eggs.
*   **`GetTechnicianHelper`**: Returns the technician helper instance.
*   **`GetInstanceData_instance_blackwing_lair`**: Factory function for the instance script.
*   **`GOHello_go_orb_of_domination`**: Handles Orb of Domination interaction.
*   **`go_egg_razAI`**: Constructor for Egg AI.
*   **`OnUse`** (go_egg_razAI): Handles egg breaking.
*   **`GetAIgo_egg_raz`**: Factory function for Egg AI.
*   **`go_engin_suppressionAI`**: Constructor for Suppression Engine AI.
*   **`OnUse#2`** (go_engin_suppressionAI): Deactivates the engine.
*   **`ApplyAura`** (go_engin_suppressionAI): Applies suppression aura to nearby players.
*   **`RestoreGo`** (go_engin_suppressionAI): Reactivates the engine.
*   **`UpdateAI#2`** (go_engin_suppressionAI): Manages engine timers.
*   **`GetAIgo_engin_suppression`**: Factory function for Suppression Engine AI.
*   **`AreaTrigger_at_orb_of_command`**: Resurrects players on the Orb of Command.
*   **`AreaTrigger_at_enter_vael_room`**: Marks Vael Event as done.
*   **`npc_death_talonAI`**: Constructor for Death Talon AI.
*   **`Reset#3`** (npc_death_talonAI): Resets Death Talon timers.
*   **`JustDied#2`** (npc_death_talonAI): Re-randomizes Death Talon powers.
*   **`Aggro#2`** (npc_death_talonAI): Calls for help.
*   **`RandomPower`** (npc_death_talonAI): Picks a random brood power.
*   **`RandomSensibility`** (npc_death_talonAI): Picks a random vulnerability.
*   **`UpdateAI#4`** (npc_death_talonAI): Executes Death Talon combat logic.
*   **`GetAI_npc_death_talon`**: Factory function for Death Talon AI.
*   **`npc_blackwing_technicianAI`**: Constructor for Technician AI.
*   **`Reset#2`** (npc_blackwing_technicianAI): Resets Technician timers and helper link.
*   **`MoveInLineOfSight`** (npc_blackwing_technicianAI): Conditionally ignores LOS.
*   **`Aggro`** (npc_blackwing_technicianAI): Adds technician to helper.
*   **`JustDied`** (npc_blackwing_technicianAI): Removes technician from helper.
*   **`UpdateAI#3`** (npc_blackwing_technicianAI): Executes Technician combat logic.
*   **`GetAI_npc_blackwing_technician`**: Factory function for Technician AI.
*   **`CorruptedWhelpAI`**: Constructor for Whelp AI.
*   **`Reset`** (CorruptedWhelpAI): Empty reset function.
*   **`UpdateAI`** (CorruptedWhelpAI): Executes Whelp melee logic.
*   **`GetAI_npc_corrupted_whelp`**: Factory function for Whelp AI.
*   **`AddSC_instance_blackwing_lair`**: Registers all scripts.

---

<!-- machine-true, projected from graph.json -->

## Map — instance_blackwing_lair

*Source:* instance_blackwing_lair.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| blackwing_technicians_helper | ctor | — | — | — |
| AddTechnician | method | HostileReference/getThreat, Object/GetObjectGuid, Object/ToCreature, ThreatManager/getThreatList, ThreatManager/modifyThreatPercent#2, Unit.Main/GetThreatManager | — | — |
| RemoveTechnician | method | Object/GetObjectGuid, ObjectGuid/operator== | — | — |
| GetVictimGuid | method | ObjectGuid/ObjectGuid | — | — |
| RemovePotentialVictim | method | — | — | — |
| GetInstance | method | — | — | — |
| RecalculateThreat | method | HostileReference/getThreat, Map.Main/GetCreature, Object/GetObjectGuid, ThreatManager/getThreatList, ThreatManager/modifyThreatPercent#2, Unit.Main/GetThreatManager, Unit.Main/IsAlive | — | — |
| instance_blackwing_lair | ctor | ScriptedInstance/ScriptedInstance | — | — |
| Initialize | method | shared_Util/urand | — | — |
| IsEncounterInProgress | method | — | — | — |
| OnObjectCreate | method | GameObject/SetGoState, Object/GetEntry, Object/GetObjectGuid, WorldObject.Object/DeleteLater | — | — |
| OnCreatureEnterCombat | method | Creature.Main/SetInCombatWithZone, Map.Main/GetCreature, Object/GetEntry, ObjectGuid/ObjectGuid#5, Unit.Main/IsAlive, Unit.Main/IsInCombat | — | — |
| OnCreatureEvade | method | Object/GetEntry, Unit.Main/AddAura | — | — |
| OnCreatureRespawn | method | Creature.Main/ForcedDespawn, Creature.Main/IsTemporarySummon, Creature.Main/UpdateEntry, Object/GetEntry, Object/SetEntry, shared_Util/urand, Unit.Main/AddAura, Unit.Main/RemoveAllAuras, Unit.Main/SetVisibility, WorldObject.Object/DeleteLater, WorldObject.Object/RemoveFlag, WorldObject.Object/SetFlag | — | — |
| OnCreatureDeath | method | Map.Main/GetCreature, Object/GetEntry, ObjectGuid/ObjectGuid#5, ScriptMgr/DoScriptText | — | — |
| OnCreatureCreate | method | Creature.Main/IsTemporarySummon, Creature.Main/UpdateEntry, Object/GetEntry, Object/GetObjectGuid, Object/SetEntry, shared_Util/urand, Unit.Main/AddAura, Unit.Main/RemoveAllAuras, WorldObject.Object/DeleteLater, WorldObject.Object/GetPositionZ, WorldObject.Object/SetFlag | — | — |
| OnPlayerDeath | method | Object/GetObjectGuid | — | — |
| GetData64 | method | — | — | — |
| SetData | method | boss_victor_nefarius/NefariusGossipOptionClicked, GameObject/GetGoState, InstanceData/SaveToDB, Log.Main/Out, Map.Main/GetCreature, Map.Main/GetGameObject, Map.Main/GetId, Map.Main/GetInstanceId, Map.Main/GetMapName, ObjectGuid/ObjectGuid#5, ScriptedInstance/DoUseDoorOrButton, Unit.Main/GetSpeed, Unit.Main/MonsterMoveWithSpeed, WorldObject.Object/DeleteLater, WorldObject.Object/RemoveFlag, ZoneScript/GetCreature | — | — |
| CheckConditionCriteriaMeet | method | Object/GetGUID | — | — |
| Save | method | — | — | — |
| GetData | method | — | — | — |
| Load | method | Log.Main/Out, Map.Main/GetId, Map.Main/GetInstanceId, Map.Main/GetMapName | — | — |
| RespawnEggs | method | Map.Main/GetCreature, ObjectGuid/ObjectGuid#5, WorldObject.Object/DeleteLater, WorldObject.Object/GetGameObjectListWithEntryInGrid, WorldObject.Object/SummonGameObject | — | — |
| GetTechnicianHelper | method | — | — | — |
| GetInstanceData_instance_blackwing_lair | function | — | — | — |
| GOHello_go_orb_of_domination | function | Creature.Main/IsInEvadeMode, InstanceData/GetData64, Map.Main/GetCreature, Object/GetObjectGuid, ObjectGuid/ObjectGuid#5, SpellCaster/CastSpell#2, Unit.Main/AddThreat, Unit.Main/HasAura#2, Unit.Main/HasUnitState, Unit.Main/IsInCombat, WorldObject.Object/GetInstanceData, WorldObject.Object/GetMap, WorldObject.Object/SetUInt32Value, WorldObject.Object/SetUInt64Value | — | — |
| go_egg_razAI | ctor | GameObjectAI/GameObjectAI | — | — |
| OnUse | method | GameObject/Delete, InstanceData/GetData64, InstanceData/SetData, Object/GetEntry, Object/IsCreature, ScriptMgr/DoScriptText, shared_Util/urand, SpellCaster/CastSpell#2, Unit.Main/GetMaxHealth, Unit.Main/RemoveAllAuras, Unit.Main/SetMaxHealth, WorldObject.Object/GetInstanceData | — | — |
| GetAIgo_egg_raz | function | — | — | — |
| go_engin_suppressionAI | ctor | GameObjectAI/GameObjectAI, shared_Util/urand | — | — |
| OnUse#2 | method | GameObject/SetGoState, shared_Util/urand, WorldObject.Object/IsWithinDistInMap | — | — |
| ApplyAura | method | GameObject/SendGameObjectCustomAnim, Map.Main/GetPlayers, Player.Main/IsGameMaster, Unit.Main/AddAura, Unit.Main/HasStealthAura, Unit.Main/IsAlive, WorldObject.Object/GetDistance#3, WorldObject.Object/GetMap | — | — |
| RestoreGo | method | GameObject/SetGoState, InstanceData/GetData, WorldObject.Object/GetInstanceData | — | — |
| UpdateAI#2 | method | shared_Util/urand | — | — |
| GetAIgo_engin_suppression | function | — | — | — |
| AreaTrigger_at_orb_of_command | function | Player.Main/GetCorpse, Player.Main/GetQuestRewardStatus, Player.Main/ResurrectPlayer, Player.Main/SpawnCorpseBones, Player.Main/TeleportTo, Unit.Main/IsDead, WorldObject.Object/GetMapId | — | — |
| AreaTrigger_at_enter_vael_room | function | InstanceData/GetData, InstanceData/SetData, Map.Main/GetInstanceData, Player.Main/IsGameMaster, WorldObject.Object/GetMap | — | — |
| npc_death_talonAI | ctor | Object/GetEntry, ScriptedAI/ScriptedAI | — | — |
| Reset#3 | method | shared_Util/urand | — | — |
| JustDied#2 | method | — | — | — |
| Aggro#2 | method | Creature.Main/CallForHelp | — | — |
| RandomPower | method | — | — | — |
| RandomSensibility | method | — | — | — |
| UpdateAI#4 | method | Creature.Main/SelectAttackingTarget, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, shared_Util/urand, Unit.Main/AddAura, Unit.Main/GetVictim, Unit.Main/HasAura#2, Unit.Main/SelectHostileTarget | — | — |
| GetAI_npc_death_talon | function | — | — | — |
| npc_blackwing_technicianAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData, WorldObject.Object/GetPositionZ | — | — |
| Reset#2 | method | shared_Util/urand | — | — |
| MoveInLineOfSight | method | BasicAI/MoveInLineOfSight | — | — |
| Aggro | method | ScriptedAI/Aggro | — | — |
| JustDied | method | CreatureAI/JustDied | — | — |
| UpdateAI#3 | method | CreatureAI/AttackStart, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, Map.Main/GetUnit, shared_Util/urand, SpellCaster/CastSpell#4, Unit.Main/GetVictim, Unit.Main/HandleEmote, WorldObject.Object/DeleteLater, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/IsWithinLOSInMap | — | — |
| GetAI_npc_blackwing_technician | function | — | — | — |
| CorruptedWhelpAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset | method | — | — | — |
| UpdateAI | method | CreatureAI/DoMeleeAttackIfReady, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_npc_corrupted_whelp | function | — | — | — |
| AddSC_instance_blackwing_lair | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
