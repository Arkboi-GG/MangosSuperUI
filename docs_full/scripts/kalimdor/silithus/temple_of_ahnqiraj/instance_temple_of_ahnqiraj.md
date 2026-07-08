# instance_temple_of_ahnqiraj

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# instance_temple_of_ahnqiraj

## Purpose & Responsibilities

`instance_temple_of_ahnqiraj` is the instance script for the **Temple of Ahn'Qiraj** raid instance. It manages the global state of the raid, including encounter progress tracking, door/gate mechanics, creature spawning/despawning rules, and complex multi-phase events that span multiple bosses or require instance-wide coordination.

Key responsibilities include:
1.  **Encounter State Management:** Tracking the status (`NOT_STARTED`, `IN_PROGRESS`, `DONE`, `FAIL`) of all major bosses (Skeram, Sartura, Fankriss, Huhuran, Twin Emperors, Ouro, C'Thun) and the Bug Trio.
2.  **The Twin Emperors Intro Event:** Orchestrating the cinematic dialogue and movement sequence involving the Master's Eye, Vek'lor, and Vek'nilash when players enter their chamber.
3.  **C'Thun's Stomach Mechanic:** Managing the complex logic for players pulled into C'Thun's stomach, including teleportation, periodic damage (Digestive Acid), "punting" players back out, and knockback effects upon exit.
4.  **C'Thun's Whisper Mechanic:** Periodically whispering random players in the instance before the fight begins, with a mute system to prevent spamming the same player repeatedly.
5.  **Trash Mob Randomization:** Randomizing certain trash mob types (e.g., Qiraji Slayer/Mindslayer) upon creation to vary encounters.
6.  **Server Crash Recovery:** Restoring specific triggers (like Ouro's spawner) if the server crashes mid-encounter.

This unit does not contain the AI for the bosses themselves (those are in separate units like `boss_cthun`, `boss_twinemperors`, etc.), but it provides the infrastructure and state queries they rely on.

## Member-by-Member Behavior

### Initialization and Lifecycle

*   **`instance_temple_of_ahnqiraj` (ctor):** Initializes the instance data structure, sets up the dialogue helpers for the Twins intro and death sequences, and initializes timers for C'Thun's whispers. It calls `Initialize()` immediately.
*   **`Initialize`:** Resets the encounter array to zero and initializes the `TwinsIntroDialogue` and `m_twinsDeadDialogue` helpers, linking them to this instance object.
*   **`Save` / `Load`:** Serializes and deserializes the encounter state array (`m_auiEncounter`) to/from a string stored in the database. `Load` also handles post-crash recovery by scheduling `RestoreOuroSpawnTrigger` if Ouro is not marked as done.
*   **`Update`:** The main tick function. It updates dialogue timers, processes C'Thun's whisper logic, updates the C'Thun stomach mechanic, and checks for the Ouro spawner restoration timer.

### Encounter State Management

*   **`SetData`:** The primary interface for bosses to report their status.
    *   **Skeram:** Opens the Skeram gate when done.
    *   **Bug Trio:** Tracks individual deaths via `SPECIAL` state; marks the trio as `DONE` only after all three are dead.
    *   **Sartura/Fankriss/Viscidus:** Simple state storage.
    *   **Huhuran:** Opens the Twins entrance door when done.
    *   **Twins:** Opens both entrance and exit doors when done. Triggers the Twins death dialogue. Prevents duplicate state changes.
    *   **Ouro:** Respawns the Ouro spawner creature if the encounter fails.
    *   **C'Thun:** Hides the "Grasp of C'Thun" game objects when the encounter is done.
    *   **General:** If any encounter is marked `DONE`, it saves the instance data to the database.
*   **`GetData`:** Returns the current state of a specific encounter type. Used by bosses to check prerequisites or current status.
*   **`IsEncounterInProgress`:** Checks if any encounter is currently `IN_PROGRESS` or `SPECIAL`. Used by the core to determine if the instance is active.
*   **`CheckConditionCriteriaMeet`:** Allows the achievement/criteria system to query if a specific encounter is `DONE`.

### Creature and Object Management

*   **`OnCreatureCreate`:**
    *   Stores GUIDs for key NPCs (bosses, spawners, eyes) in `m_mNpcEntryGuidStore`.
    *   **Randomization:** Randomly converts `NPC_QIRAJI_SLAYER` to entry 15246 and `NPC_QIRAJI_MINDSLAYER` to entry 15250 (50% chance) to vary trash packs. Re-initializes AI after change.
    *   **Sartura's Guards:** Collects GUIDs of `NPC_SARTURA_S_ROYAL_GUARD` into `m_lRoyalGuardGUIDList` for leash management.
    *   **Caelestrasz:** Adds gossip flag to allow interaction.
    *   Calls `OnCreatureRespawn` to apply despawn rules immediately for newly created creatures.
*   **`OnCreatureRespawn`:** Implements "trash removal" logic. If a boss is `DONE`, specific trash mobs associated with that boss's area are added to the removal list (despawned permanently). This keeps the instance clean after bosses are defeated. Also despawns the Master's Eye if the Twins dialogue has already started.
*   **`OnObjectCreate`:**
    *   Handles initial state of doors (Skram gate, Twins doors) based on saved encounter data.
    *   Tracks "Grasp of C'Thun" game objects to hide them when C'Thun is defeated.
    *   Stores GameObject GUIDs in `m_mGoEntryGuidStore`.

### The Twin Emperors Event

*   **`TwinsIntroDialogue` (ctor):** Sets up the dialogue sequence defined in `aIntroDialogue`.
*   **`TwinsIntroDialogue::Start`:** Validates that the Master's Eye, Vek'lor, and Vek'nilash exist. If so, starts the dialogue sequence. Logs errors if creatures are missing.
*   **`TwinsIntroDialogue::JustDidDialogueStep`:** Executes actions tied to specific dialogue steps:
    *   `EVENT_EYE_TURN_AROUND`: Rotates the Master's Eye.
    *   `EVENT_EMPERORS_RISE`: Sets Vek'lor and Vek'nilash to standing state.
    *   `SAY_EMPERORS_INTRO_1`: Despawns the Master's Eye.
*   **`TwinsIntroDialogue::StartedOrDone` / `SetDone`:** Manages the boolean flag indicating if the intro has occurred.
*   **`DoHandleTempleAreaTrigger`:** Called when players enter specific area triggers.
    *   `AREATRIGGER_TWIN_EMPERORS`: Starts the Twins intro dialogue if it hasn't started yet.
    *   `AREATRIGGER_SARTURA`: Puts Sartura into combat if the encounter is not started or failed.
*   **`TwinsDialogueStartedOrDone`:** Wrapper to check the intro dialogue state.

### C'Thun Mechanics

#### Whispers
*   **`UpdateCThunWhisper`:**
    *   Runs periodically. If C'Thun is `DONE`, it stops.
    *   Maintains a mute list (`cthunWhisperMutes`) to prevent whispering the same player too frequently.
    *   If C'Thun is `IN_PROGRESS`, it stops whispering.
    *   Otherwise, it selects a random alive player who is not muted and whispers a random C'Thun quote. The player is then added to the mute list for 10 minutes.

#### Stomach
*   **`AddPlayerToStomach`:** Adds a player to the `playersInStomach` list and casts `SPELL_DIGESTIVE_ACID` on them.
*   **`PlayerInStomach` / `PlayerInStomachIter`:** Checks if a unit is in the stomach list and returns an iterator for modification.
*   **`TeleportPlayerToCThun`:** Teleports a player to the center of C'Thun with a small random offset. Falls back to hardcoded coordinates if the area trigger data is missing.
*   **`PerformCthunKnockback`:** Summons a temporary creature at the knockback location to cast a knockback spell. This is used when players exit the stomach.
*   **`HandleStomachTriggers`:** Called by the area trigger script.
    *   `AREATRIGGER_STOMACH_GROUND`: Summons a "punt" creature to eventually launch players out. Sends a visual-only quake spell.
    *   `AREATRIGGER_STOMACH_AIR`: Teleports the player back to C'Thun's body (exit from stomach).
    *   `AREATRIGGER_CTHUN_KNOCKBACK`: Performs the knockback effect if C'Thun is not dead. Marks the player as having been knocked back to prevent double-knockback.
*   **`UpdateStomachOfCthun`:**
    *   Updates the "punt" creature's animation and countdown. When ready, it casts the punt spell and despawns the trigger.
    *   Iterates through `playersInStomach`:
        *   Removes players who have left the instance.
        *   If a player's Z position is > 0 (outside stomach), it waits a short delay, then performs knockback (if not already done) and removes them from the list after another delay.
        *   If a player is still in the stomach (Z <= 0), it refreshes the Digestive Acid aura periodically.
        *   Includes a "crude hack" to teleport players out if they are falling/launched near the exit trigger but didn't trigger it properly.
*   **`KillPlayersInStomach`:** Kills all players currently in the stomach list, removing the acid aura. Used when C'Thun dies. Skips players with invincibility (GMs).

### Other Utilities

*   **`GetRoyalGuardGUIDList`:** Provides the list of Sartura's royal guards to the boss script for leash management.
*   **`RestoreOuroSpawnTrigger`:** Resets the Ouro spawner's home position and respawns it. Called after a delay if the server crashed while Ouro was active.
*   **`AreaTrigger_at_temple_ahnqiraj`:** A global area trigger handler. It delegates to `DoHandleTempleAreaTrigger` for Twins/Sartura triggers and `HandleStomachTriggers` for C'Thun stomach triggers.

### AI Scripts

*   **`AI_QirajiMindslayer`:** AI for the Qiraji Mindslayer trash mob.
    *   **`Reset`:** Initializes timers for Insanity, Mind Blast, and Mind Flay.
    *   **`JustDied`:** Finds the closest alive player and casts a mana-burn spell on them.
    *   **`UpdateAI`:** Channels Mind Flay on a random player, casts Mind Blast on top aggro, and casts Insanity on a random player. Prevents retargeting while channeling.
*   **`AQ40DrainManaScript`:** Spell script for Drain Mana spells used by Obsidian Eradicator/Nullifier.
    *   **`OnSetTargetMap`:** Limits targets to 12.
    *   **`OnCheckTarget`:** Filters out targets with no mana or less than 1% mana.

### Script Registration

*   **`GetInstanceData_instance_temple_of_ahnqiraj`:** Factory function that creates and returns a new `instance_temple_of_ahnqiraj` object for a given map.
*   **`GetAI_qirajiMindslayer`:** Factory function that creates and returns a new `AI_QirajiMindslayer` object for a given creature.
*   **`GetScript_AQ40DrainMana`:** Factory function that creates and returns a new `AQ40DrainManaScript` object.
*   **`AddSC_instance_temple_of_ahnqiraj`:** Registers all scripts defined in this unit (`instance_temple_of_ahnqiraj`, `at_temple_ahnqiraj`, `mob_qiraji_mindslayer`, `spell_aq40_drain_mana`) with the script manager.

## Cross-Unit Boundaries

*   **`boss_sartura`:**
    *   `instance_temple_of_ahnqiraj::GetRoyalGuardGUIDList` is called by `boss_sartura::LeashEncounter` to get the GUIDs of the guards to leash them.
    *   `boss_sartura::Aggro`, `JustDied`, `JustReachedHome` call `instance_temple_of_ahnqiraj::SetData` to update the encounter state.
*   **`boss_twinemperors`:**
    *   `boss_twinemperors::Aggro`, `JustDied`, `JustReachedHome` call `instance_temple_of_ahnqiraj::SetData` to update the encounter state.
    *   `boss_twinemperors::Aggro` and `JustDied` call `instance_temple_of_ahnqiraj::GetData` to check state.
    *   `instance_temple_of_ahnqiraj::TwinsDialogueStartedOrDone` is called by `boss_twinemperors::boss_twinemperorsAI` to check if the intro has happened.
*   **`boss_cthun`:**
    *   `boss_cthun::AttackStart`, `JustDied`, `JustReachedHome` call `instance_temple_of_ahnqiraj::SetData` to update the encounter state.
    *   `boss_cthun::UpdateStomachGrab` calls `instance_temple_of_ahnqiraj::AddPlayerToStomach` to pull players into the stomach.
    *   `boss_cthun::SelectRandomAliveNotStomach`, `UpdateAI` call `instance_temple_of_ahnqiraj::PlayerInStomach` to check if a player is already in the stomach.
    *   `boss_cthun::CheckIfAllDead` calls `instance_temple_of_ahnqiraj::KillPlayersInStomach` to kill remaining players when C'Thun dies.
*   **`ScriptedInstance` / `DialogueHelper`:**
    *   Inherits from `ScriptedInstance` for basic instance functionality (saving/loading, getting creatures/gameobjects).
    *   Uses `DialogueHelper` for managing timed dialogue sequences for the Twins intro and death.
*   **`Creature.Main` / `Unit.Main` / `GameObject`:**
    *   Standard interactions for setting states, facing, stand states, despawning, respawning, and casting spells.
*   **`Log.Main`:**
    *   Used for logging errors (missing creatures) and debug info (Ouro restoration).

## Data Model

This unit does not directly interact with custom database tables for its logic. It relies on the standard `instance_data` table (handled by `ScriptedInstance::SaveToDB` and `Load`) to store the serialized encounter state string (`m_strInstData`). No custom SQL queries or table schemas are defined in this unit.

## Notable Implementation Details

1.  **C'Thun Stomach Complexity:** The stomach mechanic is entirely handled in the instance script rather than the boss AI. This allows for centralized management of players, timers, and triggers. The logic uses a combination of area triggers, periodic updates, and state flags (`didKnockback`, `timeSincePorted...`) to handle the transition from stomach to outside. The "crude hack" in `UpdateStomachOfCthun` to teleport players if they miss the air trigger suggests potential issues with trigger reliability or player movement prediction.
2.  **Trash Randomization:** `OnCreatureCreate` randomly swaps certain trash mob entries. This is done by changing the entry ID and re-initializing the AI. This adds variety but requires careful handling to ensure the new AI is correctly loaded.
3.  **Ouro Crash Recovery:** The `Load` function schedules a delayed restoration of the Ouro spawner if the encounter wasn't finished. This is a specific fix for server crashes leaving the spawner in an invalid state.
4.  **Twins Intro Guarding:** The Twins intro dialogue is guarded by `TwinsDialogueStartedOrDone()`. This ensures the cinematic only plays once per instance reset. The `SetDone` method is called in `Load` if the Twins are already dead, preventing the intro from playing on reload.
5.  **Whisper Mute System:** The C'Thun whisper system maintains a list of muted players with timestamps. This prevents the same player from being targeted repeatedly, improving the player experience.
6.  **Debugging Aids:** `KillPlayersInStomach` skips players with invincibility (GMs), making it easier for developers to test the stomach mechanic without dying. `TeleportPlayerToCThun` logs an error if the area trigger data is missing, aiding in configuration debugging.
7.  **Spell Visuals:** In `HandleStomachTriggers`, the quake spell is sent visually only (`SendSpellGo`) because casting it normally would deal damage, which is not desired for the visual effect of the stomach churning.

## Member Reference

**TwinsIntroDialogue** (ctor): Initializes the dialogue helper with the intro sequence array and sets the started flag to false.

**Start**: Validates presence of Master's Eye, Vek'lor, and Vek'nilash; if present, starts the dialogue sequence and sets the started flag.

**StartedOrDone**: Returns the boolean flag indicating whether the Twins intro dialogue has started or completed.

**SetDone**: Sets the internal flag to indicate the Twins intro dialogue is complete.

**JustDidDialogueStep**: Executes specific actions based on the dialogue step: rotates the Master's Eye, sets the Emperors to standing, or despawns the Master's Eye.

**GetRoyalGuardGUIDList**: Returns the list of GUIDs for Sartura's Royal Guards, collected during creature creation.

**Save**: Returns the serialized string of encounter data for database storage.

**instance_temple_of_ahnqiraj** (ctor): Constructs the instance script, initializes member variables, and calls `Initialize`.

**Initialize**: Resets encounter data and initializes the dialogue helpers for Twins intro and death.

**IsEncounterInProgress**: Checks if any encounter is currently in progress or special state.

**DoHandleTempleAreaTrigger**: Handles area triggers for Twins intro start and Sartura aggro.

**OnObjectCreate**: Manages initial state of doors and tracks Grasp of C'Thun game objects.

**OnCreatureRespawn**: Permanently removes trash mobs if their associated boss is defeated.

**OnCreatureCreate**: Stores boss GUIDs, randomizes trash mob types, collects Royal Guard GUIDs, and applies respawn rules.

**SetData**: Updates encounter state, opens doors, triggers dialogues, and saves data to DB when encounters are done.

**Load**: Deserializes encounter data from DB and schedules Ouro spawner restoration if needed.

**UpdateCThunWhisper**: Manages the timer and mute list for C'Thun's pre-fight whispers to random players.

**Update**: Main update loop for dialogues, whispers, stomach mechanics, and Ouro restoration.

**TwinsDialogueStartedOrDone**: Wrapper method to check if the Twins intro dialogue has occurred.

**GetData**: Returns the current state of a specified encounter.

**CheckConditionCriteriaMeet**: Checks if an encounter is done for achievement criteria.

**AreaTrigger_at_temple_ahnqiraj**: Global area trigger handler delegating to instance methods for Twins, Sartura, and C'Thun stomach.

**AddPlayerToStomach**: Adds a player to the stomach list and applies the Digestive Acid spell.

**PlayerInStomachIter**: Returns an iterator to a player in the stomach list, or end if not found.

**TeleportPlayerToCThun**: Teleports a player to C'Thun's center with a random offset.

**PerformCthunKnockback**: Summons a temporary creature to cast the knockback spell.

**PlayerInStomach**: Checks if a unit is currently in the stomach list.

**HandleStomachTriggers**: Handles area triggers within C'Thun's stomach for punting, exiting, and knockback.

**KillPlayersInStomach**: Kills all players in the stomach list, skipping GMs.

**UpdateStomachOfCthun**: Updates stomach mechanics including punt creature, acid refresh, and player removal.

**RestoreOuroSpawnTrigger**: Respawns the Ouro spawner at its home position.

**AI_QirajiMindslayer** (ctor): Initializes the Mindslayer AI and calls Reset.

**Reset**: Resets spell timers for the Mindslayer.

**JustDied**: Casts a mana-burn spell on the closest alive player upon death.

**UpdateAI**: Manages Mind Flay, Mind Blast, and Insanity casts, and melee attacks.

**GetInstanceData_instance_temple_of_ahnqiraj**: Factory function to create the instance script.

**GetAI_qirajiMindslayer**: Factory function to create the Mindslayer AI.

**OnSetTargetMap**: Limits the Drain Mana spell to 12 targets.

**OnCheckTarget**: Filters Drain Mana targets to exclude those with insufficient mana.

**GetScript_AQ40DrainMana**: Factory function to create the Drain Mana spell script.

**AddSC_instance_temple_of_ahnqiraj**: Registers all scripts in this unit with the script manager.

---

<!-- machine-true, projected from graph.json -->

## Map — instance_temple_of_ahnqiraj

*Source:* instance_temple_of_ahnqiraj.cpp, temple_of_ahnqiraj.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| TwinsIntroDialogue | ctor | ScriptedInstance/DialogueHelper | — | — |
| Start | method | Creature.Main/ForcedDespawn, Log.Main/Out, ScriptedInstance/GetSingleCreatureFromStorage, ScriptedInstance/StartNextDialogueText | — | — |
| StartedOrDone | method | — | — | — |
| SetDone | method | — | — | — |
| JustDidDialogueStep | method | Creature.Main/ForcedDespawn, Log.Main/Out, ScriptedInstance/GetSingleCreatureFromStorage, Unit.Main/SetFacingTo, Unit.Main/SetStandState | — | — |
| GetRoyalGuardGUIDList | method | — | boss_sartura/LeashEncounter, boss_sartura/LeashEncounter#2 | — |
| Save | method | — | — | — |
| instance_temple_of_ahnqiraj | ctor | ScriptedInstance/DialogueHelper, ScriptedInstance/ScriptedInstance | — | — |
| Initialize | method | DialogueHelper/InitializeDialogueHelper | — | — |
| IsEncounterInProgress | method | — | — | — |
| DoHandleTempleAreaTrigger | method | Creature.Main/SetInCombatWithZone, ScriptedInstance/GetSingleCreatureFromStorage | — | — |
| OnObjectCreate | method | GameObject/SetVisible, GameObject/UseDoorOrButton, Object/GetEntry, Object/GetObjectGuid | — | — |
| OnCreatureRespawn | method | Object/GetEntry, WorldObject.Object/AddObjectToRemoveList | — | — |
| OnCreatureCreate | method | Creature.Main/AIM_Initialize, Creature.Main/UpdateEntry, Object/GetEntry, Object/GetObjectGuid, Object/SetEntry, shared_Util/urand, WorldObject.Object/SetFlag | — | — |
| SetData | method | Creature.Main/Respawn, GameObject/SetVisible, InstanceData/SaveToDB, Log.Main/Out, Map.Main/GetId, Map.Main/GetInstanceId, Map.Main/GetMapName, Object/GetGUID, ScriptedInstance/DoOpenDoor, ScriptedInstance/GetSingleCreatureFromStorage, ScriptedInstance/GetSingleGameObjectFromStorage, ScriptedInstance/StartNextDialogueText, ZoneScript/GetGameObject | boss_cthun/AttackStart, boss_cthun/JustDied, boss_cthun/JustReachedHome, boss_sartura/Aggro, boss_sartura/JustDied, boss_sartura/JustReachedHome, boss_twinemperors/Aggro, boss_twinemperors/JustDied, boss_twinemperors/JustReachedHome | — |
| Load | method | Log.Main/Out, Map.Main/GetId, Map.Main/GetInstanceId, Map.Main/GetMapName | — | — |
| UpdateCThunWhisper | method | LinkedListHead/isEmpty, Map.Main/GetPlayers, Object/GetGUID, Object/GetObjectGuid, ObjectGuid/operator==, ScriptedInstance/GetSingleCreatureFromStorage, ScriptMgr/DoScriptText, shared_Util/irand, shared_Util/urand, Unit.Main/IsDead, ZoneScript/GetMap#2 | — | — |
| Update | method | ScriptedInstance/DialogueUpdate | — | — |
| TwinsDialogueStartedOrDone | method | — | boss_twinemperors/boss_twinemperorsAI | — |
| GetData | method | — | boss_twinemperors/Aggro, boss_twinemperors/JustDied | — |
| CheckConditionCriteriaMeet | method | — | — | — |
| AreaTrigger_at_temple_ahnqiraj | function | Player.Main/IsGameMaster, Unit.Main/IsAlive, WorldObject.Object/GetInstanceData | — | — |
| AddPlayerToStomach | method | Object/GetGUID, ScriptedInstance/GetSingleCreatureFromStorage, SpellCaster/CastSpell#2, StomachTimers/StomachTimers | boss_cthun/UpdateStomachGrab | — |
| PlayerInStomachIter | method | Object/GetObjectGuid, ObjectGuid/operator== | — | — |
| TeleportPlayerToCThun | method | Log.Main/Out, ObjectMgr/GetAreaTrigger, shared_Util/frand, Unit.Main/NearTeleportTo, WorldObject.Object/GetOrientation | — | — |
| PerformCthunKnockback | method | ObjectMgr/GetAreaTrigger, SpellCaster/CastSpell#2, WorldObject.Object/SummonCreature, ZoneScript/GetMap#2 | — | — |
| PlayerInStomach | method | — | boss_cthun/SelectRandomAliveNotStomach, boss_cthun/UpdateAI#4, boss_cthun/UpdateAI#7 | — |
| HandleStomachTriggers | method | Object/GetGUID, ObjectGuid/ObjectGuid#5, ObjectGuid/operator!, Player.Main/IsGameMaster, Unit.Main/IsAlive, Unit.Main/SendSpellGo, WorldObject.Object/SummonCreature, ZoneScript/GetMap#2 | — | — |
| KillPlayersInStomach | method | Map.Main/GetPlayer, Unit.Main/DealDamage, Unit.Main/GetHealth, Unit.Main/GetInvincibilityHpThreshold, Unit.Main/HasAura#2, Unit.Main/IsAlive, Unit.Main/RemoveAurasDueToSpell, ZoneScript/GetMap#2 | boss_cthun/CheckIfAllDead | — |
| UpdateStomachOfCthun | method | Map.Main/GetCreature, Map.Main/GetPlayer, ObjectGuid/ObjectGuid#5, ObjectMgr/GetAreaTrigger, Player.Main/IsFalling, Player.Main/IsLaunched, ScriptedInstance/GetSingleCreatureFromStorage, SpellCaster/CastSpell#2, Unit.Main/HasAura#2, Unit.Main/RemoveAurasDueToSpell, Unit.Main/SendSpellGo, WorldObject.Object/GetDistance#4, WorldObject.Object/GetPositionZ, ZoneScript/GetMap#2 | — | — |
| RestoreOuroSpawnTrigger | method | Creature.Main/Respawn, Creature.Main/SetHomePosition, ScriptedInstance/GetSingleCreatureFromStorage | — | — |
| AI_QirajiMindslayer | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset | method | shared_Util/urand | — | — |
| JustDied | method | CreatureAI/DoCastSpellIfCan, Map.Main/GetPlayers, Unit.Main/IsAlive, WorldObject.Object/GetDistance#3, WorldObject.Object/GetInstanceData, WorldObject.Object/GetMap | — | — |
| UpdateAI | method | Creature.Main/SelectAttackingTarget#2, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, shared_Util/urand, SpellCaster/IsNonMeleeSpellCasted, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetInstanceData_instance_temple_of_ahnqiraj | function | — | — | — |
| GetAI_qirajiMindslayer | function | — | — | — |
| OnSetTargetMap | method | — | — | — |
| OnCheckTarget | method | Unit.Main/GetPowerPercent, Unit.Main/GetPowerType | — | — |
| GetScript_AQ40DrainMana | function | — | — | — |
| AddSC_instance_temple_of_ahnqiraj | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
