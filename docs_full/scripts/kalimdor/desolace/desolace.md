# desolace

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# desolace.cpp

## Purpose & Responsibilities

`desolace.cpp` implements scripted behaviors for non-player characters (NPCs), game objects (GOs), and quest interactions within the **Desolace** zone of the World of Warcraft server emulation. It handles four distinct content systems:

1.  **Hand of Iruxos Crystal**: A simple game object interaction that summons a hostile spirit.
2.  **Melizza Brimbuzzle Escort**: A complex escort quest (`QUEST_GET_ME_OUT_OF_HERE`) involving dialogue sequences, enemy spawns, and faction changes.
3.  **Dalinda Malem Escort**: A simpler escort quest (`QUEST_RETURN_TO_VAHLARRIEL`) with specific respawn and movement states.
4.  **Magrami Spectre Magnet**: A game object system that periodically spawns hostile spectres which move toward the magnet and become aggressive upon arrival.
5.  **Gizelton Caravan**: A large-scale escort event (`QUEST_BOTTOM` / `QUEST_TOP`) involving a caravan of NPCs, periodic ambushes by hostile mobs, vendor interactions, and synchronized movement.

The unit does not interact with any database tables directly; all data is hardcoded in enums, arrays, and logic within the source file.

## Member-by-Member Behavior

### Hand of Iruxos Crystal

This subsystem handles the interaction with the `go_hand_of_iruxos_crystal` game object.

*   **`GOHello_go_hand_of_iruxos_crystal`**: Triggered when a player interacts with the crystal. It verifies the object type is `GAMEOBJECT_TYPE_GOOBER`. If valid, it summons a `DEMON_SPIRIT` (entry 11876) at fixed coordinates near the player. The summoned creature is set to attack the player immediately.

### Melizza Brimbuzzle Escort

This subsystem manages the AI for `npc_melizza_brimbuzzle` during the quest "Get Me Out of Here".

*   **`npc_melizza_brimbuzzleAI` (ctor)**: Initializes the AI and calls `Reset`.
*   **`Reset#4`**: Resets dialogue timers and steps if the escort is not currently active.
*   **`JustStartedEscort#2`**: Opens Melizza's cage (`GO_MELIZZAS_CAGE`) using `UseDoorOrButton` and resets the dialogue step counter.
*   **`WaypointReached#3`**: Handles events at specific waypoints:
    *   **WP 1**: Starts the escort dialogue with the player and sets a temporary neutral faction.
    *   **WP 4**: Spawns two `MARAUDINE_MARAUDER` creatures at two predefined locations with random offsets.
    *   **WP 9**: Spawns three `MARAUDINE_BONEPAW` and three `MARAUDINE_WRANGLER` creatures at a predefined location with random offsets.
    *   **WP 12**: Pauses the escort, increases max player distance, faces the player, and starts a dialogue timer.
    *   **WP 19**: Pauses the escort and resets the dialogue step for the final sequence.
*   **`Dialogue`**: Manages timed dialogue sequences. It checks `m_dialogueStep` and `m_dialogueTimer`. Depending on the step, it plays specific speech lines (`SAY_MELIZZA_1` through `SAY_MELIZZA_FINISH`), faces specific NPCs (like `Horniz Brimbuzzle`), resumes the escort, or triggers the quest completion event (`GroupEventHappens`).
*   **`UpdateAI#3`**: Calls `Dialogue` to process timed events, then delegates to the parent `npc_escortAI::UpdateAI`. It also handles standard melee combat logic if a victim is present.
*   **`GetAI_npc_melizza_brimbuzzle`**: Factory function to create the AI instance.
*   **`QuestAccept_npc_melizza_brimbuzzle`**: Triggered when the player accepts the quest. It casts the creature's AI to `npc_melizza_brimbuzzleAI` and starts the escort.

### Dalinda Malem Escort

This subsystem manages the AI for `npc_dalinda_malem` during the quest "Return to Vahlarriel".

*   **`npc_dalinda_malemAI` (ctor)**: Initializes the AI and calls `Reset`.
*   **`Reset#2`**: Empty override.
*   **`JustRespawned`**: Sets the creature immune to NPC attacks (`UNIT_FLAG_IMMUNE_TO_NPC`) and calls the parent respawn handler.
*   **`JustStartedEscort`**: Ensures the creature stands up (`UNIT_STAND_STATE_STAND`).
*   **`WaypointReached#2`**: At waypoint 18, it triggers the quest completion event for the player.
*   **`GetAI_npc_dalinda_malem`**: Factory function to create the AI instance.
*   **`QuestAccept_npc_dalinda_malem`**: Triggered when the player accepts the quest. It sets a temporary friendly faction, removes the NPC immunity flag, and starts the escort.

### Magrami Spectre Magnet

This subsystem handles the `go_ghost_magnet` game object and the `npc_magrami_spetre` creatures it spawns.

*   **`go_ghost_magnetAI` (ctor)**: Initializes timers and checks for existing functional magnets nearby. If none are found, it spawns a visual aura object (`GO_GHOST_MAGNET_AURA`) at its location.
*   **`UpdateAI` (in `go_ghost_magnetAI`)**: Manages the spawning cycle. If `state` is active, it decrements timers. When the small timer expires and `nbToSpawn` > 0, it calls `spawnSpetre`. When the large timer expires, it stops spawning.
*   **`spawnSpetre`**: Calculates a random point within 40 yards of the magnet and summons a `MAGRAMI_SPECTRE`. It then calls `DefineMagramiMagnet` to link the spectre to the magnet.
*   **`MagramiSpectreDied`**: Callback invoked when a spectre dies. If the magnet is still active, it immediately spawns a replacement spectre.
*   **`GetAIgo_ghost_magnet`**: Factory function to create the AI instance.
*   **`npc_magrami_spetreAI` (ctor)**: Initializes the spectre AI, setting initial aura and timers.
*   **`Reset#3`**: Resets combat timers and applies the initial blue aura (`SPELL_BLUE_AURA`) or green aura (`SPELL_GREEN_AURA`) depending on state.
*   **`MovementInform`**: Triggered when the spectre reaches a movement point. If it reached point 2 (the magnet) and isn't already green, it calls `turnGreen`.
*   **`JustReachedHome`**: If the spectre returns home (unlikely in this flow) and isn't green, it turns green.
*   **`turnGreen`**: Removes the blue aura, adds the green aura, and changes the faction to enemy (`FACTION_ENNEMY`).
*   **`UpdateAI#2`**: Handles combat. It attempts to cast `CURSE_OF_THE_FALLEN_MAGRAM` on the victim if they don't already have it, with a cooldown. It also performs melee attacks.
*   **`UpdateAI_corpse`**: Runs after death. It waits for `corpseTimer` (20s) before notifying the magnet AI via `MagramiSpectreDied` to spawn a replacement.
*   **`SetMagnetGuid`**: Called by `DefineMagramiMagnet`. It finds the magnet GO, calculates a contact point, sets the spectre's home position to the magnet, and moves it there using pathfinding.
*   **`GetAI_npc_magrami_spetre`**: Factory function to create the AI instance.
*   **`DefineMagramiMagnet`**: Helper function that casts the creature's AI and calls `SetMagnetGuid`.

### Gizelton Caravan

This subsystem manages the complex caravan escort involving Cork and Rigger Gizelton.

*   **`npc_cork_gizeltonAI` (ctor)**: Initializes the AI and calls `ResetCreature`.
*   **`Reset`**: Empty override.
*   **`ResetCreature`**: Clears all GUID lists, resets counters and timers, and resets boolean flags for state management.
*   **`SummonCaravan`**: Adds the main creature to the formation list and summons the rest of the caravan members (Kodos, Rigger) from the `Caravan` array. It adds them to the formation group. If summoning fails, it logs an error and despawns the caravan.
*   **`JustDied`**: Calls `FailEscort`.
*   **`FailEscort`**: Despawns the caravan and triggers a quest failure event for the player.
*   **`DespawnCaravan`**: Iterates through the caravan GUID list and despawns all summoned creatures, then forces the main creature to despawn.
*   **`CaravanFaction`**: Applies or removes temporary factions and immunity flags for all caravan members. Used to make them hostile/friendly during combat phases.
*   **`SummonAmbusher`**: Finds a valid ground position near the specified ambusher coordinates and summons the creature.
*   **`Ambush`**: Based on the waypoint index, summons specific groups of ambushers (Doomwarders, Sorceresses, Infernals, Kolkars) and triggers dialogue.
*   **`AddToFormation`**: Joins a creature to the main caravan group with specific distance and angle offsets.
*   **`JustSummoned`**: Handles newly summoned creatures. If it's Rigger, it stores his GUID and removes his questgiver flag. If it's a Kodo, it adds him to the caravan list. If it's an enemy, it increments the enemy count and makes the enemy attack a random caravan member.
*   **`SummonedCreatureJustDied`**: If Rigger or a Kodo dies, it fails the escort. If an enemy dies, it decrements the enemy count and resumes the path if no enemies remain.
*   **`ResumePath`**: Updates state flags to wait for departure, clears the announce count, and removes the questgiver flag.
*   **`DoTalk`**: Makes either Rigger or Cork speak/yell a specific text ID.
*   **`GiveQuest`**: Toggles the questgiver flag on Rigger or Cork.
*   **`CaravanWalk`**: Sets the walking animation for the main creature.
*   **`DoVendor`**: Shows or hides the nearest vendor NPC based on visibility.
*   **`WaypointReached`**: Handles major event points:
    *   **Camp Points**: Pauses escort, enables vendor, starts camp timer.
    *   **Announce Points**: Pauses escort, enables questgiver, starts announce/depart timers, waits for player.
    *   **Ambush Points**: Pauses escort and triggers ambush if a player is associated.
    *   **Complete Points**: Triggers completion dialogue, gives quest credit if player is nearby, clears player GUID, resets factions, and toggles the `m_bRigger` flag for the next run.
    *   **End Point**: Despawns the caravan.
*   **`UpdateEscortAI`**: Main update loop. It manages initialization delay, camp timers, announce timers (yelling every 3 mins, giving up after 5 announcements), and departure timers. It delegates to the parent AI for standard escort movement.
*   **`GetAI_npc_cork_gizelton`**: Factory function to create the AI instance.
*   **`QuestAccept_npc_cork_gizelton`**: Triggered when accepting the top quest. It finds Cork's AI and calls `ResumePath`.
*   **`QuestAccept_npc_rigger_gizelton`**: Triggered when accepting the bottom quest. It finds the nearest Cork Gizelton and calls `ResumePath` on his AI.

### Script Registration

*   **`AddSC_desolace`**: Registers all scripts defined in this file with the script manager. It creates `Script` objects for each NPC/GO and assigns the appropriate callback functions (AI getters, quest accept handlers, GO hello handlers).

## Cross-Unit Boundaries

*   **`Creature.Main/AI`**: Called by `GOHello_go_hand_of_iruxos_crystal`, `QuestAccept_npc_melizza_brimbuzzle`, `QuestAccept_npc_dalinda_malem`, `JustSummoned` (in `npc_cork_gizeltonAI`), `DefineMagramiMagnet`, `QuestAccept_npc_cork_gizelton`, and `QuestAccept_npc_rigger_gizelton` to access or cast the AI of creatures.
*   **`CreatureAI/AttackStart`**: Called by `GOHello_go_hand_of_iruxos_crystal` and `JustSummoned` (in `npc_cork_gizeltonAI`) to initiate combat.
*   **`GameObject/GetGoType`**: Called by `GOHello_go_hand_of_iruxos_crystal` to verify the object type.
*   **`WorldObject.Object/SummonCreature#2`**: Called by `GOHello_go_hand_of_iruxos_crystal`, `WaypointReached#3` (Melizza), `spawnSpetre` (Magnet), `SummonCaravan` (Caravan), and `SummonAmbusher` (Caravan) to spawn creatures.
*   **`ScriptedEscortAI/npc_escortAI`**: Base class for `npc_melizza_brimbuzzleAI`, `npc_dalinda_malemAI`, and `npc_cork_gizeltonAI`.
*   **`ScriptedEscortAI/HasEscortState`**: Called by `Reset#4` (Melizza) to check escort status.
*   **`GameObject/UseDoorOrButton`**: Called by `JustStartedEscort#2` (Melizza) to open the cage.
*   **`GridSearchers/GetClosestGameObjectWithEntry`**: Called by `JustStartedEscort#2` (Melizza) to find the cage.
*   **`Creature.Main/SetFactionTemporary`**: Called by `WaypointReached#3` (Melizza) and `QuestAccept_npc_dalinda_malem` to change faction temporarily.
*   **`ScriptedEscortAI/GetPlayerForEscort`**: Called by `WaypointReached#3` (Melizza), `Dialogue` (Melizza), and `WaypointReached#2` (Dalinda) to get the player being escorted.
*   **`ScriptedEscortAI/SetEscortPaused`**: Called by `WaypointReached#3` (Melizza), `Dialogue` (Melizza), `SummonedCreatureJustDied` (Caravan), `WaypointReached` (Caravan), and `UpdateEscortAI` (Caravan) to pause/resume escort movement.
*   **`ScriptedEscortAI/SetMaxPlayerDistance`**: Called by `WaypointReached#3` (Melizza) to adjust despawn range.
*   **`ScriptMgr/DoScriptText`**: Called by `WaypointReached#3` (Melizza) and `Dialogue` (Melizza) to play speech.
*   **`Unit.Main/SetFacingToObject`**: Called by `WaypointReached#3` (Melizza) and `Dialogue` (Melizza) to orient the creature.
*   **`WorldObject.Object/GetRandomPoint`**: Called by `WaypointReached#3` (Melizza) and `spawnSpetre` (Magnet) to calculate spawn positions.
*   **`Creature.Main/ClearTemporaryFaction`**: Called by `Dialogue` (Melizza) and `CaravanFaction` (Caravan) to reset faction.
*   **`Player.Main/GroupEventHappens`**: Called by `Dialogue` (Melizza), `WaypointReached#2` (Dalinda), and `WaypointReached` (Caravan) to trigger quest events.
*   **`ScriptedEscortAI/SetRun`**: Called by `Dialogue` (Melizza) to make the escort run.
*   **`WorldObject.Object/FindNearestCreature`**: Called by `Dialogue` (Melizza) to find Horniz and `DoVendor` (Caravan) to find vendors.
*   **`CreatureAI/DoMeleeAttackIfReady`**: Called by `UpdateAI#3` (Melizza), `UpdateAI#2` (Spectre) to perform melee attacks.
*   **`ScriptedEscortAI/UpdateAI`**: Called by `UpdateAI#3` (Melizza) to handle base escort logic.
*   **`Unit.Main/GetVictim`**: Called by `UpdateAI#3` (Melizza) and `UpdateAI#2` (Spectre) to check for combat targets.
*   **`Unit.Main/SelectHostileTarget`**: Called by `UpdateAI#3` (Melizza) and `UpdateAI#2` (Spectre) to select targets.
*   **`ScriptedEscortAI/Start`**: Called by `QuestAccept_npc_melizza_brimbuzzle`, `QuestAccept_npc_dalinda_malem`, and `UpdateEscortAI` (Caravan) to begin the escort.
*   **`Object/GetGUID`**: Called by `QuestAccept_npc_melizza_brimbuzzle`, `QuestAccept_npc_dalinda_malem`, `spawnSpetre` (Magnet), `SummonCaravan` (Caravan), `JustSummoned` (Caravan), `ResumePath` (Caravan) to retrieve GUIDs.
*   **`QuestDef/GetQuestId`**: Called by `QuestAccept_npc_melizza_brimbuzzle`, `QuestAccept_npc_dalinda_malem`, `QuestAccept_npc_cork_gizelton`, `QuestAccept_npc_rigger_gizelton` to identify the quest.
*   **`WorldObject.Object/SetFlag`**: Called by `JustRespawned` (Dalinda) and `CaravanFaction` (Caravan) to set unit flags.
*   **`Unit.Main/SetStandState`**: Called by `JustStartedEscort` (Dalinda) to stand up.
*   **`ScriptedEscortAI/JustRespawned`**: Called by `JustRespawned` (Dalinda) to handle base respawn logic.
*   **`GameObject/isSpawned`**: Called by `go_ghost_magnetAI` constructor to check for existing magnets.
*   **`GameObjectAI/GameObjectAI`**: Base class for `go_ghost_magnetAI`.
*   **`WorldObject.Object/GetGameObjectListWithEntryInGrid`**: Called by `go_ghost_magnetAI` constructor to find nearby magnets.
*   **`WorldObject.Object/GetPosition#2`**: Called by `go_ghost_magnetAI` constructor and `spawnSpetre` (Magnet) to get positions.
*   **`WorldObject.Object/SummonGameObject`**: Called by `go_ghost_magnetAI` constructor to spawn the aura.
*   **`shared_Util/urand`**: Called by `UpdateAI` (Magnet), `Reset#3` (Spectre), `UpdateAI#2` (Spectre), `JustSummoned` (Caravan) for random number generation.
*   **`ObjectGuid/ObjectGuid#5`**: Called by `UpdateAI_corpse` (Spectre) and `SetMagnetGuid` (Spectre) for GUID construction.
*   **`Map.Main/GetGameObject`**: Called by `UpdateAI_corpse` (Spectre) and `SetMagnetGuid` (Spectre) to retrieve the magnet GO.
*   **`GameObject/AI`**: Called by `UpdateAI_corpse` (Spectre) to access the magnet's AI.
*   **`WorldObject.Object/GetMap`**: Called by `UpdateAI_corpse` (Spectre), `SetMagnetGuid` (Spectre), `FailEscort` (Caravan), `DespawnCaravan` (Caravan), `CaravanFaction` (Caravan), `SummonAmbusher` (Caravan), `JustSummoned` (Caravan), `DoTalk` (Caravan), `GiveQuest` (Caravan), `WaypointReached` (Caravan) to access the map instance.
*   **`Unit.Main/AddAura`**: Called by `Reset#3` (Spectre) and `turnGreen` (Spectre) to apply auras.
*   **`Unit.Main/RemoveAurasDueToSpell`**: Called by `turnGreen` (Spectre) to remove the blue aura.
*   **`Unit.Main/SetFactionTemplateId`**: Called by `turnGreen` (Spectre) to change faction.
*   **`CreatureAI/DoCastSpellIfCan`**: Called by `UpdateAI#2` (Spectre) to cast spells.
*   **`Unit.Main/HasAura#2`**: Called by `UpdateAI#2` (Spectre) to check for existing auras.
*   **`Creature.Main/SetHomePosition`**: Called by `SetMagnetGuid` (Spectre) to set home position.
*   **`Creature.MotionMaster/MovePoint`**: Called by `SetMagnetGuid` (Spectre) to move to the magnet.
*   **`Unit.Main/GetMotionMaster`**: Called by `SetMagnetGuid` (Spectre) to access motion master.
*   **`WorldObject.Object/GetContactPoint`**: Called by `SetMagnetGuid` (Spectre) to calculate contact point.
*   **`ObjectGuid/Clear`**: Called by `ResetCreature` (Caravan) and `WaypointReached` (Caravan) to clear GUIDs.
*   **`Log.Main/Out`**: Called by `SummonCaravan` (Caravan) to log errors.
*   **`Object/GetObjectGuid`**: Called by `SummonCaravan` (Caravan), `DespawnCaravan` (Caravan), `CaravanFaction` (Caravan), `JustSummoned` (Caravan) to get object GUIDs.
*   **`Map.Main/GetPlayer`**: Called by `FailEscort` (Caravan) and `WaypointReached` (Caravan) to retrieve players.
*   **`ObjectGuid/operator!`**: Called by `FailEscort` (Caravan) to check if GUID is empty.
*   **`Player.Main/GroupEventFailHappens`**: Called by `FailEscort` (Caravan) to trigger quest failure.
*   **`Creature.Main/DespawnOrUnsummon`**: Called by `DespawnCaravan` (Caravan) to despawn creatures.
*   **`Creature.Main/ForcedDespawn`**: Called by `DespawnCaravan` (Caravan) to force despawn.
*   **`Map.Main/GetCreature`**: Called by `DespawnCaravan` (Caravan), `CaravanFaction` (Caravan), `JustSummoned` (Caravan), `DoTalk` (Caravan), `GiveQuest` (Caravan) to retrieve creatures.
*   **`ObjectGuid/operator!=`**: Called by `DespawnCaravan` (Caravan) and `CaravanFaction` (Caravan) to compare GUIDs.
*   **`WorldObject.Object/RemoveFlag`**: Called by `QuestAccept_npc_dalinda_malem`, `CaravanFaction` (Caravan), `JustSummoned` (Caravan), `GiveQuest` (Caravan) to remove flags.
*   **`Map.Main/GetWalkRandomPosition`**: Called by `SummonAmbusher` (Caravan) to find valid ground positions.
*   **`Creature.Main/JoinCreatureGroup`**: Called by `AddToFormation` (Caravan) to join formations.
*   **`Object/GetEntry`**: Called by `JustSummoned` (Caravan) and `SummonedCreatureJustDied` (Caravan) to get creature entries.
*   **`WorldObject.Object/MonsterSay#2`**: Called by `DoTalk` (Caravan) to speak.
*   **`WorldObject.Object/MonsterYellToZone`**: Called by `DoTalk` (Caravan) to yell.
*   **`Unit.Main/SetWalk`**: Called by `CaravanWalk` (Caravan) to set walking animation.
*   **`Unit.Main/SetVisibility`**: Called by `DoVendor` (Caravan) to show/hide vendors.
*   **`WorldObject.Object/IsInRange`**: Called by `WaypointReached` (Caravan) to check player proximity.
*   **`ScriptedEscortAI/UpdateEscortAI`**: Called by `UpdateEscortAI` (Caravan) to handle base escort logic.
*   **`Script/Script`**: Called by `AddSC_desolace` to create script objects.
*   **`ScriptMgr/RegisterSelf`**: Called by `AddSC_desolace` to register scripts.
*   **`ScriptLoader/AddScripts`**: Called by `AddSC_desolace` to add scripts to the loader.

## Data Model

This unit does not interact with any database tables. All configuration data (creature entries, coordinates, spell IDs, text IDs, timers) is hardcoded in the source file.

## Notable Implementation Details

*   **Melizza Dialogue State Machine**: The `Dialogue` method in `npc_melizza_brimbuzzleAI` uses a step-based state machine (`m_dialogueStep`) driven by a timer (`m_dialogueTimer`). This allows for complex, timed sequences of speech and actions without blocking the AI update loop.
*   **Magrami Spectre Respawn Logic**: The `go_ghost_magnetAI` tracks the number of spectres to spawn (`nbToSpawn`). When a spectre dies, `MagramiSpectreDied` is called, which immediately spawns a replacement if the magnet is still active. This ensures a constant presence of spectres around the magnet.
*   **Gizelton Caravan Formation**: The caravan uses `JoinCreatureGroup` to maintain formation. Each member has specific distance and angle offsets defined in the `Caravan` array. This ensures they move together in a structured manner.
*   **Gizelton Caravan Ambush Timing**: Ambushes are triggered at specific waypoints. The `Ambush` method summons multiple hostile creatures and triggers dialogue. The caravan pauses during the ambush until all enemies are defeated.
*   **Gizelton Caravan Vendor Interaction**: At camp points, the caravan pauses and a vendor NPC is made visible. This allows players to interact with the vendor while waiting.
*   **Gizelton Caravan Quest Acceptance**: The quest acceptance handlers for Cork and Rigger Gizelton both call `ResumePath` on Cork's AI. This suggests that Cork is the primary controller of the caravan, regardless of which NPC the player accepts the quest from.
*   **Hardcoded Coordinates**: Many spawn points and movement paths are hardcoded in arrays (`aMarauderSpawn`, `wranglerSpawn`, `Ambusher`, `Caravan`). This makes the scripts tightly coupled to the specific map layout of Desolace.

## Member Reference

**GOHello_go_hand_of_iruxos_crystal**: Checks if the game object is a

---

<!-- machine-true, projected from graph.json -->

## Map — desolace

*Source:* desolace.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| GOHello_go_hand_of_iruxos_crystal | function | Creature.Main/AI, CreatureAI/AttackStart, GameObject/GetGoType, WorldObject.Object/SummonCreature#2 | — | — |
| npc_melizza_brimbuzzleAI | ctor | ScriptedEscortAI/npc_escortAI | — | — |
| Reset#4 | method | ScriptedEscortAI/HasEscortState | — | — |
| JustStartedEscort#2 | method | GameObject/UseDoorOrButton, GridSearchers/GetClosestGameObjectWithEntry | — | — |
| WaypointReached#3 | method | Creature.Main/SetFactionTemporary, ScriptedEscortAI/GetPlayerForEscort, ScriptedEscortAI/SetEscortPaused, ScriptedEscortAI/SetMaxPlayerDistance, ScriptMgr/DoScriptText, Unit.Main/SetFacingToObject, WorldObject.Object/GetRandomPoint, WorldObject.Object/SummonCreature#2 | — | — |
| Dialogue | method | Creature.Main/ClearTemporaryFaction, Player.Main/GroupEventHappens, ScriptedEscortAI/GetPlayerForEscort, ScriptedEscortAI/SetEscortPaused, ScriptedEscortAI/SetRun, ScriptMgr/DoScriptText, Unit.Main/SetFacingToObject, WorldObject.Object/FindNearestCreature | — | — |
| UpdateAI#3 | method | CreatureAI/DoMeleeAttackIfReady, ScriptedEscortAI/UpdateAI, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_npc_melizza_brimbuzzle | function | — | — | — |
| QuestAccept_npc_melizza_brimbuzzle | function | Creature.Main/AI, Object/GetGUID, QuestDef/GetQuestId, ScriptedEscortAI/Start | — | — |
| npc_dalinda_malemAI | ctor | ScriptedEscortAI/npc_escortAI | — | — |
| Reset#2 | method | — | — | — |
| JustRespawned | method | ScriptedEscortAI/JustRespawned, WorldObject.Object/SetFlag | — | — |
| JustStartedEscort | method | Unit.Main/SetStandState | — | — |
| WaypointReached#2 | method | Player.Main/GroupEventHappens, ScriptedEscortAI/GetPlayerForEscort | — | — |
| GetAI_npc_dalinda_malem | function | — | — | — |
| QuestAccept_npc_dalinda_malem | function | Creature.Main/AI, Creature.Main/SetFactionTemporary, Object/GetGUID, QuestDef/GetQuestId, ScriptedEscortAI/Start, WorldObject.Object/RemoveFlag | — | — |
| go_ghost_magnetAI | ctor | GameObject/isSpawned, GameObjectAI/GameObjectAI, WorldObject.Object/GetGameObjectListWithEntryInGrid, WorldObject.Object/GetPosition#2, WorldObject.Object/SummonGameObject | — | — |
| UpdateAI | method | shared_Util/urand | — | — |
| spawnSpetre | method | Object/GetGUID, WorldObject.Object/GetPosition#2, WorldObject.Object/GetRandomPoint, WorldObject.Object/SummonCreature#2 | — | — |
| MagramiSpectreDied | method | — | — | — |
| GetAIgo_ghost_magnet | function | — | — | — |
| npc_magrami_spetreAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#3 | method | shared_Util/urand, Unit.Main/AddAura | — | — |
| MovementInform | method | — | — | — |
| JustReachedHome | method | — | — | — |
| turnGreen | method | Unit.Main/AddAura, Unit.Main/RemoveAurasDueToSpell, Unit.Main/SetFactionTemplateId | — | — |
| UpdateAI#2 | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, shared_Util/urand, Unit.Main/GetVictim, Unit.Main/HasAura#2, Unit.Main/SelectHostileTarget | — | — |
| UpdateAI_corpse | method | GameObject/AI, Map.Main/GetGameObject, Object/GetGUID, ObjectGuid/ObjectGuid#5, WorldObject.Object/GetMap | — | — |
| SetMagnetGuid | method | Creature.Main/SetHomePosition, Creature.MotionMaster/MovePoint, Map.Main/GetGameObject, ObjectGuid/ObjectGuid#5, Unit.Main/GetMotionMaster, WorldObject.Object/GetContactPoint, WorldObject.Object/GetMap | — | — |
| GetAI_npc_magrami_spetre | function | — | — | — |
| DefineMagramiMagnet | function | Creature.Main/AI | — | — |
| npc_cork_gizeltonAI | ctor | ScriptedEscortAI/npc_escortAI | — | — |
| Reset | method | — | — | — |
| ResetCreature | method | ObjectGuid/Clear | — | — |
| SummonCaravan | method | Log.Main/Out, Object/GetObjectGuid, WorldObject.Object/SummonCreature#2 | — | — |
| JustDied | method | — | — | — |
| FailEscort | method | Map.Main/GetPlayer, ObjectGuid/operator!, Player.Main/GroupEventFailHappens, WorldObject.Object/GetMap | — | — |
| DespawnCaravan | method | Creature.Main/DespawnOrUnsummon, Creature.Main/ForcedDespawn, Map.Main/GetCreature, Object/GetObjectGuid, ObjectGuid/operator!=, WorldObject.Object/GetMap | — | — |
| CaravanFaction | method | Creature.Main/ClearTemporaryFaction, Creature.Main/SetFactionTemporary, Map.Main/GetCreature, Object/GetObjectGuid, ObjectGuid/operator!=, WorldObject.Object/GetMap, WorldObject.Object/RemoveFlag, WorldObject.Object/SetFlag | — | — |
| SummonAmbusher | method | Map.Main/GetWalkRandomPosition, WorldObject.Object/GetMap, WorldObject.Object/SummonCreature#2 | — | — |
| Ambush | method | — | — | — |
| AddToFormation | method | Creature.Main/JoinCreatureGroup | — | — |
| JustSummoned | method | Creature.Main/AI, CreatureAI/AttackStart, Map.Main/GetCreature, Object/GetEntry, Object/GetObjectGuid, shared_Util/urand, WorldObject.Object/GetMap, WorldObject.Object/RemoveFlag | — | — |
| SummonedCreatureJustDied | method | Object/GetEntry, ScriptedEscortAI/SetEscortPaused | — | — |
| ResumePath | method | Object/GetObjectGuid | — | — |
| DoTalk | method | Map.Main/GetCreature, WorldObject.Object/GetMap, WorldObject.Object/MonsterSay#2, WorldObject.Object/MonsterYellToZone | — | — |
| GiveQuest | method | Map.Main/GetCreature, WorldObject.Object/GetMap, WorldObject.Object/RemoveFlag, WorldObject.Object/SetFlag | — | — |
| CaravanWalk | method | Unit.Main/SetWalk | — | — |
| DoVendor | method | Unit.Main/SetVisibility, WorldObject.Object/FindNearestCreature | — | — |
| WaypointReached | method | Map.Main/GetPlayer, ObjectGuid/Clear, Player.Main/GroupEventHappens, ScriptedEscortAI/SetEscortPaused, WorldObject.Object/GetMap, WorldObject.Object/IsInRange | — | — |
| UpdateEscortAI | method | ScriptedEscortAI/SetEscortPaused, ScriptedEscortAI/Start, ScriptedEscortAI/UpdateEscortAI | — | — |
| GetAI_npc_cork_gizelton | function | — | — | — |
| QuestAccept_npc_cork_gizelton | function | Creature.Main/AI, QuestDef/GetQuestId | — | — |
| QuestAccept_npc_rigger_gizelton | function | Creature.Main/AI, QuestDef/GetQuestId, WorldObject.Object/FindNearestCreature | — | — |
| AddSC_desolace | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
