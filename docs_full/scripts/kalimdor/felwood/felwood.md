# felwood

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# felwood

**Purpose & Responsibilities**
`felwood.cpp` implements scripted behaviors for non-player characters (NPCs), game objects (GOs), area triggers, and spells specific to the Felwood zone. It supports two primary escort quests ("Rescue Jaedenar" and "Ancient Spirit"), handles the despawning mechanics for quest-target oozes, manages the visual transformation of corrupted plants upon quest completion, summons ancient treants for hunters in a specific area, and modifies the periodic tick rate of the "Curse of the Bleakheart" spell. The unit contains no database interactions; all logic is driven by in-memory state, creature/game object entries, and script hooks.

## Member-by-Member Behavior

### Cursed and Tainted Oozes
These two NPCs (`npc_cursed_ooze` and `npc_tainted_ooze`) share identical AI structures. They are hostile mobs that cast a specific spell on a timer and melee attack. Their primary quest-related function is to despawn immediately when hit by a specific quest item spell (a jar).

*   **`npc_cursed_oozeAI` / `npc_tainted_oozeAI`**: Constructors initialize the AI and call `Reset`.
*   **`SpellHit`**: Checks if the spell hitting the creature is `SPELL_QUEST_CURSED_JAR` (15698) or `SPELL_QUEST_TAINTED_JAR` (15699). If so, it calls `Creature.Main/ForcedDespawn` to remove the creature instantly.
*   **`UpdateAI#3` / `UpdateAI#4`**: Standard combat loop. If no target exists, it returns. Otherwise, it maintains a `SpellTimer`. When the timer expires, it attempts to cast `SPELL_CURSED` (13483) or `SPELL_TAINTED` (3335) via `CreatureAI/DoCastSpellIfCan`. It also calls `CreatureAI/DoMeleeAttackIfReady`.
*   **`Reset#3` / `Reset#4`**: Resets `SpellTimer` to 3000 ms.
*   **`GetAI_npc_cursed_ooze` / `GetAI_npc_tainted_ooze`**: Factory functions returning new instances of the respective AI structs.

### Captured Arkonarin Escort
This complex AI (`npc_captured_arkonarinAI`) handles the escort quest "Rescue Jaedenar" (Quest ID 5203). It inherits from `ScriptedEscortAI` and manages waypoint events, summoning allies/enemies, and changing the creature's appearance and faction during the escort.

*   **`npc_captured_arkonarinAI`**: Constructor initializes the escort AI.
*   **`Reset#2`**: If not currently escorting, sets `m_bCanAttack` to false. Initializes timers for `Mortal Strike` and `Cleave` spells with random delays.
*   **`JustRespawned`**: Sets the `UNIT_FLAG_IMMUNE_TO_NPC` flag on the creature to prevent aggro before the quest starts, then calls the parent `JustRespawned`.
*   **`Aggro#2`**: If aggroed by `NPC_SPIRT_TREY` (entry 11141), it plays a specific betrayal line. Otherwise, it has a 25% chance to play a generic aggro line.
*   **`JustSummoned#2`**: Handles summoned creatures. If a Legionnaire is summoned, it attacks Arkonarin. If Trey is summoned, it plays a line and stores Trey's GUID in `m_treyGuid`.
*   **`WaypointReached#2`**: The core logic driver.
    *   Point 0: Starts escort dialogue.
    *   Point 14/34: Intermediate dialogue.
    *   Point 38: Uses a nearby chest (`GO_ARKONARIN_CHEST`) and kneels.
    *   Point 39: Casts `SPELL_STRENGTH_ARKONARIN`.
    *   Point 40: Enables attacking (`m_bCanAttack = true`), updates the creature's entry to `NPC_ARKO_NARIN` (changing its model/stats), sets temporary faction to neutral-active, and removes the immune flag.
    *   Point 41: Summons three Legionnaires.
    *   Point 105: Summons Trey.
    *   Point 107: Attacks Trey using the stored GUID.
    *   Point 109: Triggers quest completion for the player via `Player.Main/GroupEventHappens`.
*   **`UpdateEscortAI`**: Combat loop. If `m_bCanAttack` is true, it casts `Mortal Strike` and `Cleave` on timers. Always performs melee attacks.
*   **`GetAI_npc_captured_arkonarin`**: Factory function.
*   **`QuestAccept_npc_captured_arkonarin`**: Triggered when the player accepts the quest. It starts the escort, sets the creature to stand, removes the immune flag, and uses the cage game object (`GO_ARKONARIN_CAGE`) to open it.

### Arei Escort
This AI (`npc_areiAI`) handles the escort quest "Ancient Spirit" (Quest ID 4261). It involves pausing the escort to fight summoned enemies, then resuming after a dialogue sequence.

*   **`npc_areiAI`**: Constructor initializes flags and calls `Reset`.
*   **`Reset`**: Initializes `Wither Strike` timer. If not escorting, sets `dialogueStep` to 6 (inactive).
*   **`Aggro`**: Plays specific lines when aggroed by Iron Tree Wanderers/Stompers or Toxic Horrors, setting flags to prevent repeated lines.
*   **`Dialogue`**: Manages a post-combat dialogue sequence.
    *   Step 1: Casts `Wither Strike` and speaks.
    *   Step 2: Speaks about transforming.
    *   Step 3: Casts `AREI_TRANSFORM` and speaks.
    *   Step 4: Completes the quest for the player and unpauses the escort.
*   **`JustSummoned`**: When Iron Tree enemies are summoned, they attack Arei. Their GUIDs are added to `m_lSummonsGuids`.
*   **`SummonedCreatureJustDied`**: Removes dead summons from the list. If the list is empty, it advances `dialogueStep` to 1, triggering the `Dialogue` logic in the next update.
*   **`WaypointReached`**: At waypoint 36, it pauses the escort, summons three Iron Tree enemies, and plays a line.
*   **`GetSpeakerByEntry`**: Helper to identify Arei as the speaker.
*   **`UpdateAI#2`**: Calls `Dialogue`, then the parent `UpdateAI`. Handles combat with `Wither Strike` and melee.
*   **`GetAI_npc_arei`**: Factory function.
*   **`QuestAccept_npc_arei`**: Starts the escort, sets temporary faction, and plays the start line.

### Corrupted Plants
This Game Object AI (`go_corrupted_plantAI`) handles the visual replacement of corrupted plants with cleansed ones after a quest is rewarded.

*   **`go_corrupted_plantAI`**: Constructor maps the current GO entry to a "cleansed" entry using a hardcoded array of pairs.
*   **`UpdateAI`**: Checks if a cleansed plant was previously summoned (`cleansedGuid`). If so, it retrieves the object and adds it to the removal list to clean up stale objects, then clears the GUID.
*   **`PlantQuestRewarded`**: Summons the cleansed plant at the same location, sets its spawn delay, despawns the original corrupted plant, and destroys it for nearby players.
*   **`GetAI_go_corrupted_plant`**: Factory function.
*   **`QuestRewarded_go_corrupted_plant`**: Hook called when a quest is rewarded. It casts the GO's AI to `go_corrupted_plantAI` and calls `PlantQuestRewarded`.

### Area Trigger: Irontree Wood
*   **`AreaTrigger_at_irontree_wood`**: Triggered when a player enters area trigger 3587. If the player is a Hunter and has completed quest 7632 ("The Ancient Leaf"), it summons three ancient treants (Vartrus, Stome, Hastat) near the player if they aren't already present.

### Curse of the Bleakheart Spell
*   **`OnBeforeApply`**: Modifies the periodic tick timer of the aura to 5 seconds.
*   **`OnPeriodicDummy`**: On each tick, there is a 5% chance to cast spell 6945 on the target.
*   **`GetScript_CurseOfTheBleakheart`**: Factory function for the aura script.

### Script Registration
*   **`AddSC_felwood`**: Registers all the above scripts with the `ScriptMgr`.

## Cross-Unit Boundaries

*   **`Creature.Main`**: Used extensively for despawning (`ForcedDespawn`), updating entries (`UpdateEntry`), setting factions (`SetFactionTemporary`), and summoning creatures (`SummonCreature#2`).
*   **`CreatureAI`**: Used for casting spells (`DoCastSpellIfCan`), starting attacks (`AttackStart`), and melee attacks (`DoMeleeAttackIfReady`).
*   **`ScriptedEscortAI`**: Inherited by Arkonarin and Arei AIs. Used for managing escort state (`HasEscortState`, `Start`, `SetEscortPaused`, `GetPlayerForEscort`) and base AI updates.
*   **`Unit.Main`**: Used for targeting (`SelectHostileTarget`, `GetVictim`), emotes (`HandleEmote`), and facing (`SetFacingToObject`).
*   **`WorldObject.Object`**: Used for flag management (`SetFlag`, `RemoveFlag`), position retrieval (`GetPositionX/Y/Z`), and summoning game objects (`SummonGameObject`).
*   **`GameObject`**: Used for interacting with chests/cages (`Use`), managing respawn delays (`ComputeRespawnDelay#2`, `SetRespawnDelay`), and despawning (`Despawn`, `DestroyForNearbyPlayers`).
*   **`GridSearchers`**: Used to find nearby creatures or game objects (`GetClosestCreatureWithEntry`, `GetClosestGameObjectWithEntry`).
*   **`ScriptMgr`**: Used to play text lines (`DoScriptText`).
*   **`Player.Main`**: Used to trigger quest events (`GroupEventHappens`) and check quest status (`GetQuestStatus`).
*   **`Aura`**: Used in the Bleakheart spell script to manage timers and targets.
*   **`shared_Util`**: Used for random number generation (`urand`, `roll_chance_i`).

## Data Model

This unit does not interact with any database tables. All data is derived from hardcoded enums, creature/game object entries, and runtime state.

## Notable Implementation Details

*   **Hardcoded Entry Mapping**: The `go_corrupted_plantAI` constructor uses a large, hardcoded array to map corrupted plant entries to their cleansed counterparts. This is brittle and requires manual updates if new plant variants are added.
*   **GUID Storage for Trey**: In `npc_captured_arkonarinAI`, the GUID of the summoned spirit Trey is stored in `m_treyGuid` during `JustSummoned#2` and retrieved later in `WaypointReached#2` to initiate combat. This relies on the summon happening before waypoint 107.
*   **Dialogue State Machine**: `npc_areiAI` uses a `dialogueStep` integer to manage a sequential dialogue after combat. The step advances only when the previous step's timer expires and conditions are met. The sequence is triggered when all summoned enemies die (`SummonedCreatureJustDied`).
*   **Immunity Flag Management**: Both escort NPCs use `UNIT_FLAG_IMMUNE_TO_NPC` to prevent aggro before the quest starts. This flag is removed in `QuestAccept` or `WaypointReached` depending on the NPC.
*   **Spell Timer Modification**: The `CurseOfTheBleakheart` script explicitly overrides the default periodic timer to 5 seconds, likely to match intended gameplay pacing.

## Member Reference

*   **`npc_cursed_oozeAI`**: Constructor for the cursed ooze AI, initializing the base `ScriptedAI`.
*   **`SpellHit`**: Method in `npc_cursed_oozeAI` that despawns the creature if hit by the quest jar spell.
*   **`UpdateAI#3`**: Method in `npc_cursed_oozeAI` handling combat logic, spell casting, and melee attacks.
*   **`Reset#3`**: Method in `npc_cursed_oozeAI` resetting the spell timer.
*   **`GetAI_npc_cursed_ooze`**: Factory function creating a new `npc_cursed_oozeAI` instance.
*   **`npc_tainted_oozeAI`**: Constructor for the tainted ooze AI, initializing the base `ScriptedAI`.
*   **`SpellHit#2`**: Method in `npc_tainted_oozeAI` that despawns the creature if hit by the quest jar spell.
*   **`UpdateAI#4`**: Method in `npc_tainted_oozeAI` handling combat logic, spell casting, and melee attacks.
*   **`Reset#4`**: Method in `npc_tainted_oozeAI` resetting the spell timer.
*   **`GetAI_npc_tainted_ooze`**: Factory function creating a new `npc_tainted_oozeAI` instance.
*   **`npc_captured_arkonarinAI`**: Constructor for the captured Arkonarin escort AI.
*   **`Reset#2`**: Method in `npc_captured_arkonarinAI` resetting combat timers and attack permission.
*   **`JustRespawned`**: Method in `npc_captured_arkonarinAI` setting immunity flags on respawn.
*   **`Aggro#2`**: Method in `npc_captured_arkonarinAI` playing specific aggro lines based on the attacker.
*   **`JustSummoned#2`**: Method in `npc_captured_arkonarinAI` handling summoned allies/enemies and storing Trey's GUID.
*   **`WaypointReached#2`**: Method in `npc_captured_arkonarinAI` driving the escort event sequence, including model changes, summons, and quest completion.
*   **`UpdateEscortAI`**: Method in `npc_captured_arkonarinAI` handling combat logic with special spells when allowed.
*   **`GetAI_npc_captured_arkonarin`**: Factory function creating a new `npc_captured_arkonarinAI` instance.
*   **`QuestAccept_npc_captured_arkonarin`**: Function hook starting the Arkonarin escort quest.
*   **`npc_areiAI`**: Constructor for the Arei escort AI.
*   **`Reset`**: Method in `npc_areiAI` resetting timers and dialogue state.
*   **`Aggro`**: Method in `npc_areiAI` playing specific aggro lines for certain enemy types.
*   **`Dialogue`**: Method in `npc_areiAI` managing the post-combat dialogue sequence.
*   **`JustSummoned`**: Method in `npc_areiAI` tracking summoned enemies.
*   **`SummonedCreatureJustDied`**: Method in `npc_areiAI` advancing dialogue when all enemies are dead.
*   **`WaypointReached`**: Method in `npc_areiAI` pausing escort and summoning enemies at waypoint 36.
*   **`GetSpeakerByEntry`**: Helper method in `npc_areiAI` identifying the speaker.
*   **`UpdateAI#2`**: Method in `npc_areiAI` handling dialogue, parent AI updates, and combat.
*   **`GetAI_npc_arei`**: Factory function creating a new `npc_areiAI` instance.
*   **`QuestAccept_npc_arei`**: Function hook starting the Arei escort quest.
*   **`go_corrupted_plantAI`**: Constructor for the corrupted plant GO AI, mapping entries.
*   **`UpdateAI`**: Method in `go_corrupted_plantAI` cleaning up previously summoned cleansed plants.
*   **`PlantQuestRewarded`**: Method in `go_corrupted_plantAI` summoning the cleansed plant and despawning the original.
*   **`GetAI_go_corrupted_plant`**: Factory function creating a new `go_corrupted_plantAI` instance.
*   **`QuestRewarded_go_corrupted_plant`**: Function hook triggering plant cleansing on quest reward.
*   **`AreaTrigger_at_irontree_wood`**: Function hook summoning ancient treants for hunters in the area.
*   **`OnBeforeApply`**: Method in `CurseOfTheBleakheartScript` setting the aura tick timer.
*   **`OnPeriodicDummy`**: Method in `CurseOfTheBleakheartScript` casting a secondary spell on tick.
*   **`GetScript_CurseOfTheBleakheart`**: Factory function creating the Bleakheart aura script.
*   **`AddSC_felwood`**: Function registering all Felwood scripts with the system.

---

<!-- machine-true, projected from graph.json -->

## Map — felwood

*Source:* felwood.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| npc_cursed_oozeAI | ctor | ScriptedAI/ScriptedAI | — | — |
| SpellHit | method | Creature.Main/ForcedDespawn | — | — |
| UpdateAI#3 | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| Reset#3 | method | — | — | — |
| GetAI_npc_cursed_ooze | function | — | — | — |
| npc_tainted_oozeAI | ctor | ScriptedAI/ScriptedAI | — | — |
| SpellHit#2 | method | Creature.Main/ForcedDespawn | — | — |
| UpdateAI#4 | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| Reset#4 | method | — | — | — |
| GetAI_npc_tainted_ooze | function | — | — | — |
| npc_captured_arkonarinAI | ctor | ScriptedEscortAI/npc_escortAI | — | — |
| Reset#2 | method | ScriptedEscortAI/HasEscortState, shared_Util/urand | — | — |
| JustRespawned | method | ScriptedEscortAI/JustRespawned, WorldObject.Object/SetFlag | — | — |
| Aggro#2 | method | Object/GetEntry, ScriptMgr/DoScriptText, shared_Util/roll_chance_i | — | — |
| JustSummoned#2 | method | Creature.Main/AI, CreatureAI/AttackStart, Object/GetEntry, Object/GetObjectGuid, ScriptMgr/DoScriptText | — | — |
| WaypointReached#2 | method | Creature.Main/SetFactionTemporary, Creature.Main/UpdateEntry, CreatureAI/AttackStart, CreatureAI/DoCastSpellIfCan, GameObject/Use, GridSearchers/GetClosestGameObjectWithEntry, Map.Main/GetCreature, Player.Main/GroupEventHappens, ScriptedEscortAI/GetPlayerForEscort, ScriptedEscortAI/SetRun, ScriptMgr/DoScriptText, Unit.Main/HandleEmote, Unit.Main/SetFacingToObject, WorldObject.Object/GetMap, WorldObject.Object/RemoveFlag, WorldObject.Object/SummonCreature#2 | — | — |
| UpdateEscortAI | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, shared_Util/urand, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_npc_captured_arkonarin | function | — | — | — |
| QuestAccept_npc_captured_arkonarin | function | Creature.Main/AI, GameObject/Use, GridSearchers/GetClosestGameObjectWithEntry, Object/GetGUID, QuestDef/GetQuestId, ScriptedEscortAI/Start, Unit.Main/SetStandState, WorldObject.Object/RemoveFlag | — | — |
| npc_areiAI | ctor | ScriptedEscortAI/npc_escortAI | — | — |
| Reset | method | ScriptedEscortAI/HasEscortState, shared_Util/urand | — | — |
| Aggro | method | Object/GetEntry, ScriptedEscortAI/GetPlayerForEscort, ScriptMgr/DoScriptText | — | — |
| Dialogue | method | CreatureAI/DoCastSpellIfCan, Player.Main/GroupEventHappens, ScriptedEscortAI/GetPlayerForEscort, ScriptedEscortAI/SetEscortPaused, ScriptMgr/DoScriptText, Unit.Main/GetVictim | — | — |
| JustSummoned | method | Creature.Main/AI, CreatureAI/AttackStart, Object/GetEntry, Object/GetObjectGuid, ScriptMgr/DoScriptText | — | — |
| SummonedCreatureJustDied | method | Object/GetEntry, Object/GetObjectGuid | — | — |
| WaypointReached | method | ScriptedEscortAI/SetEscortPaused, ScriptMgr/DoScriptText, WorldObject.Object/SummonCreature#2 | — | — |
| GetSpeakerByEntry | method | — | — | — |
| UpdateAI#2 | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, ScriptedEscortAI/UpdateAI, shared_Util/urand, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_npc_arei | function | — | — | — |
| QuestAccept_npc_arei | function | Creature.Main/AI, Creature.Main/SetFactionTemporary, Object/GetGUID, QuestDef/GetQuestId, ScriptedEscortAI/Start, ScriptMgr/DoScriptText | — | — |
| go_corrupted_plantAI | ctor | GameObjectAI/GameObjectAI, Object/GetEntry | — | — |
| UpdateAI | method | GameObject/isSpawned, Map.Main/GetGameObject, ObjectGuid/Clear, WorldObject.Object/AddObjectToRemoveList, WorldObject.Object/GetMap | — | — |
| PlantQuestRewarded | method | GameObject/ComputeRespawnDelay#2, GameObject/Despawn, GameObject/GetGOData, GameObject/SetRespawnDelay, GameObject/SetSpawnedByDefault, GameObjectData/GetRandomRespawnTime, Object/GetObjectGuid, WorldObject.Object/DestroyForNearbyPlayers, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/SummonGameObject | — | — |
| GetAI_go_corrupted_plant | function | — | — | — |
| QuestRewarded_go_corrupted_plant | function | GameObject/AI | — | — |
| AreaTrigger_at_irontree_wood | function | GridSearchers/GetClosestCreatureWithEntry, Player.Main/GetQuestStatus, Unit.Main/GetClass, WorldObject.Object/SummonCreature#2 | — | — |
| OnBeforeApply | method | Aura/GetEffIndex, Aura/SetPeriodicTimer | — | — |
| OnPeriodicDummy | method | Aura/GetTarget, shared_Util/roll_chance_i, SpellCaster/CastSpell#2 | — | — |
| GetScript_CurseOfTheBleakheart | function | — | — | — |
| AddSC_felwood | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
