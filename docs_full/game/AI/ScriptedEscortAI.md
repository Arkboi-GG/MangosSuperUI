# ScriptedEscortAI

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# ScriptedEscortAI

`ScriptedEscortAI` is a base AI class designed to manage non-player characters (NPCs) that perform escort quests. It provides a framework for NPCs to follow a predefined sequence of waypoints, react to combat interruptions, ensure the escorted player remains within range, and handle quest completion or failure states. The class inherits from `ScriptedAI` and is intended to be subclassed by specific NPC scripts (e.g., `npc_kineloryAI`, `npc_professor_phizzlethorpeAI`) which implement pure virtual methods like `Reset` and `WaypointReached` to define unique dialogue, event triggers, and cleanup logic.

## Purpose & Responsibilities

The primary responsibility of `ScriptedEscortAI` is to abstract the complex state machine required for escort mechanics. Key responsibilities include:

1.  **Waypoint Navigation:** Loading waypoints from the database (`script_waypoint` table via `ScriptMgr`), managing the current waypoint index, and issuing movement commands to the creature's motion master.
2.  **Proximity Enforcement:** Periodically checking if the assigned player (or their group) is within a configurable maximum distance (`m_MaxPlayerDistance`). If the player strays too far, the escort fails.
3.  **Combat Handling:** Detecting when the NPC enters combat, saving the current position as a "combat start position," and ensuring the NPC returns to that position after combat ends before resuming the escort path.
4.  **State Management:** Tracking internal states such as `ESCORTING`, `PAUSED`, and `RETURNING` (returning to the combat start point) to prevent logic errors during transitions.
5.  **Quest Integration:** Linking the escort instance to a specific `Quest` object and player GUID, allowing for proper credit assignment or failure reporting upon death or completion.

## Member-by-Member Behavior

### Initialization and Lifecycle

*   **`npc_escortAI` (Constructor):** Initializes the AI state. It sets the creature as escortable, records the initial position and orientation as the default combat start position, and sets a default delay before the first waypoint (2500ms). It initializes various flags (running, pathfinding, instant respawn) to their defaults.
*   **`~npc_escortAI` (Destructor):** Empty destructor.
*   **`Reset` (Declaration):** A pure virtual function declared in the header. Subclasses must implement this to reset their specific quest states. It is called by `EnterEvadeMode` in other units (e.g., `ThreatListCopier.battleground_alterac/EnterEvadeMode`).
*   **`ResetCreature` (Method):** An empty override in this unit. Subclasses may override this to perform specific cleanup actions when the escort resets.

### Waypoint Management

*   **`Start`:** Initiates the escort. It validates that the creature is not in combat and not already escorting. It clears any existing waypoints, loads new ones from the database via `FillPointMovementListForCreature`, and configures internal flags (run mode, player GUID, quest pointer, instant respawn, loop path). It disables NPC flags (like questgiver) during the escort, sets the initial movement mode, adds the `ESCORTING` state, links the player to the escort, and calls the virtual `JustStartedEscort`.
*   **`Stop`:** Terminates the escort. It unlinks the player from the escort and removes the `ESCORTING` and `PAUSED` states.
*   **`FillPointMovementListForCreature`:** Queries the `ScriptMgr` for the point movement list associated with the creature's entry ID. It populates the internal `WaypointList` vector with `Escort_Waypoint` structures containing ID, coordinates, and wait times.
*   **`setCurrentWP` / `getCurrentWP`:** Accessors for the current waypoint index. `setCurrentWP` includes a safety check to log an error if the index exceeds the waypoint list size.
*   **`WaypointReached` (Declaration):** A pure virtual function. Subclasses implement this to handle events triggered when a specific waypoint is reached (e.g., playing dialogue, spawning mobs).
*   **`WaypointStart` (Method):** A virtual hook called when movement towards a waypoint begins. Default implementation is empty.
*   **`SetRun`:** Toggles the creature between walking and running modes. It logs a debug message if the requested mode matches the current mode.
*   **`SetEscortPaused`:** Adds or removes the `PAUSED` state. While paused, the `UpdateAI` loop will not advance to the next waypoint.

### Movement and Update Loop

*   **`UpdateAI`:** The core update loop. It performs two main checks:
    1.  **Waypoint Progression:** If escorting, not in combat, not returning, and the wait timer has expired, it advances to the next waypoint. If the end of the list is reached, it either returns the creature to its spawn point (if `m_bCanReturnToStart` is true) or kills/despawns the creature (potentially respawning instantly if `m_bCanInstantRespawn` is true). It issues `MovePoint` commands with appropriate flags (pathfinding, run/walk).
    2.  **Player Proximity Check:** Every second (`m_uiPlayerCheckTimer`), it checks if the player or group is within `m_MaxPlayerDistance`. If not, it triggers `JustDied` (to fail the quest) and `ResetEscort`.
    Finally, it calls the virtual `UpdateEscortAI` for subclass-specific logic.
*   **`MovementInform`:** Called by the motion master when a movement point is reached. It distinguishes between:
    *   `POINT_LAST_POINT`: Returning to the combat start position. It resumes the escort path.
    *   `POINT_HOME`: Returning to the original spawn location after completing the loop. It resets the waypoint index to 0.
    *   Standard Waypoints: It verifies the creature is close enough to the target coordinates and that the waypoint ID matches expectations. If valid, it calls `WaypointReached` and sets the wait timer for the next step.
*   **`ReturnToCombatStartPosition`:** Used when combat ends. If the creature is still escorting, it moves the creature back to the saved combat start position (`POINT_LAST_POINT`). If the distance is excessively large (>1000 units), it corrects the position to avoid pathfinding errors. If not escorting, it attempts to return to the targeted home location.

### Combat and Interaction

*   **`EnterCombat`:** Records the current position and orientation as the combat start position (unless already returning). Calls `Aggro`.
*   **`Aggro`:** Empty override. Subclasses typically handle aggro logic via `UpdateEscortAI` or specific events.
*   **`AssistPlayerInCombat`:** Checks if a hostile unit should be attacked. It verifies the unit is targetable, not friendly, within assist distance, and has line of sight. If the creature is not already attacking, it starts the attack; otherwise, it adds threat.
*   **`MoveInLineOfSight`:** Overrides the base AI to allow assisting the player in combat even if the unit is not directly aggroed, provided the escort is active.
*   **`UpdateEscortAI`:** A virtual method called by `UpdateAI`. By default, it makes passive NPCs aggro nearby hostile units and handles standard melee/spell attacks if a victim exists. Subclasses often override this to add custom abilities or event checks.
*   **`EnterEvadeMode`:** Handles the escape from combat. It clears combo points, auras, and threat lists. It resets spells to the default template. Crucially, it calls `ReturnToCombatStartPosition` to ensure the NPC goes back to where it was when combat started, then calls `Reset`.

### Failure and Death

*   **`JustDied`:** If the NPC dies while escorting, it fails the quest for the player (`GroupEventFailHappens`) and clears the escorting GUID.
*   **`ResetEscort`:** Forces the escort to end by killing the creature and respawning it instantly (if configured). It restores the questgiver flag.
*   **`JustRespawned`:** Resets the escort state to `NONE`, ensures combat movement is enabled, resets the faction template, and calls `Reset` and `ResetCreature`.

### Utility and Configuration

*   **`GetPlayerForEscort`:** Retrieves the `Player` object associated with the stored player GUID.
*   **`IsPlayerOrGroupInRange`:** Checks if the assigned player or any member of their group is within `m_MaxPlayerDistance`.
*   **`HasEscortState` / `AddEscortState` / `RemoveEscortState`:** Bitmask operations to manage the escort state flags.
*   **`SetMaxPlayerDistance` / `SetMaxAssistDistance`:** Configures the proximity thresholds.
*   **`SetPathfindingEnabledBetweenWaypoints` / `SetDelayBeforeTheFirstWaypoint`:** Configures movement behavior.
*   **`GetAIInformation`:** Outputs debug information about the escort state to the chat handler.
*   **`SetCombatStartPosition` / `GetCombatStartPosition`:** Accessors for the saved combat start coordinates.

## Cross-Unit Boundaries

`ScriptedEscortAI` acts as a central hub for many escort-related scripts. It collaborates extensively with:

*   **Specific NPC Scripts (e.g., `arathi_highlands/npc_kineloryAI`, `blackrock_depths/npc_marshal_windsorAI`):** These subclasses inherit from `ScriptedEscortAI`. They call `Start` to begin the escort, `Stop` to end it, `SetRun` to change speed, `SetEscortPaused` to halt movement during dialogue, and `GetCurrentWP`/`SetCurrentWP` to manipulate the path. They implement `Reset`, `WaypointReached`, and `UpdateEscortAI` to provide unique behavior.
*   **`Creature` / `Unit` / `WorldObject`:** `ScriptedEscortAI` relies heavily on these core classes for position data, movement commands (`MovePoint`, `MoveIdle`), combat status (`IsInCombat`, `GetVictim`), and faction/aura management.
*   **`ScriptMgr`:** Used in `FillPointMovementListForCreature` to retrieve waypoint data from the database cache.
*   **`Player` / `Group`:** Used to track the escorted player, check distances, and report quest success/failure.
*   **`Log`:** Used extensively for debugging waypoint progression, state changes, and errors.

## Data Model

`ScriptedEscortAI` does not directly query database tables in its C++ code. However, it relies on data populated by `ScriptMgr` from the `script_waypoint` table. The `FillPointMovementListForCreature` method retrieves waypoints based on the creature's entry ID. The structure of `script_waypoint` typically includes columns for `id`, `pointid`, `position_x`, `position_y`, `position_z`, and `waittime`. The AI maps these to the `Escort_Waypoint` struct.

## Notable Implementation Details

1.  **Combat Return Logic:** A key feature is the `STATE_ESCORT_RETURNING` flag. When combat ends, the NPC doesn't immediately resume the next waypoint. Instead, it moves back to the `CombatStartPosition` (saved in `EnterCombat`). `MovementInform` detects arrival at `POINT_LAST_POINT`, removes the `RETURNING` state, and then allows the normal waypoint progression to continue. This prevents NPCs from teleporting forward after a fight.
2.  **Proximity Check Timer:** The `m_uiPlayerCheckTimer` runs every 1000ms. If the player is too far, the escort fails immediately. This is a hard constraint to prevent players from kiting enemies or AFKing during escorts.
3.  **Waypoint Validation:** In `MovementInform`, the AI checks if the creature is within 10.0f units of the target waypoint coordinates. If not, it assumes the movement was interrupted and retries the same waypoint (`m_uiWPWaitTimer = 1`). It also checks if the received `uiPointId` matches the expected waypoint ID, logging an error if they mismatch.
4.  **Instant Respawn vs. Database Respawn:** The `Start` method accepts `bInstantRespawn`. If true, `UpdateAI` calls `Respawn()` immediately after the creature dies at the end of the path. Otherwise, it relies on the server's standard respawn timer.
5.  **Looping Paths:** If `bCanLoopPath` is true, reaching the end of the waypoint list triggers a move to `POINT_HOME` (the creature's spawn coordinates). Upon reaching `POINT_HOME`, the waypoint index resets to 0, effectively looping the path. This is used for patrol-like behaviors rather than linear quests.
6.  **Passive Aggro:** `UpdateEscortAI` contains logic to make passive NPCs aggro nearby hostile units (`SelectNearestHostileUnitInAggroRange`). This ensures that even if the NPC isn't actively attacking, it will defend itself if enemies get too close.

## Member Reference

**npc_escortAI**: Constructor that initializes the escort AI state, sets the creature as escortable, and records the initial position as the combat start position.

**~npc_escortAI**: Destructor, empty.

**Reset**: Pure virtual declaration. Must be implemented by subclasses to reset quest-specific state.

**ResetCreature**: Virtual method, empty in this unit. Subclasses may override for specific cleanup.

**setCurrentWP**: Sets the current waypoint index. Logs an error if the index is out of bounds.

**EnterCombat**: Saves the current position and orientation as the combat start position and calls `Aggro`.

**getCurrentWP**: Returns the current waypoint index.

**Aggro**: Empty override. Subclasses handle aggro logic.

**WaypointReached**: Pure virtual declaration. Subclasses implement to handle events at each waypoint.

**WaypointStart**: Virtual hook called when moving to a waypoint starts. Default is empty.

**GetPlayerForEscort**: Retrieves the `Player` object associated with the escort.

**AssistPlayerInCombat**: Determines if a hostile unit should be attacked based on distance, line of sight, and friendliness. Starts attack or adds threat.

**SetPathfindingEnabledBetweenWaypoints**: Setter for the pathfinding flag.

**SetDelayBeforeTheFirstWaypoint**: Setter for the initial delay timer.

**HasEscortState**: Checks if a specific state bit is set.

**SetMaxPlayerDistance**: Sets the maximum allowed distance for the player.

**SetMaxAssistDistance**: Sets the maximum distance for assisting in combat.

**AddEscortState**: Adds a state bit to the escort state mask.

**RemoveEscortState**: Removes a state bit from the escort state mask.

**SetCombatStartPosition**: Saves the coordinates for the combat start position.

**GetCombatStartPosition**: Retrieves the saved combat start coordinates.

**JustStartedEscort**: Virtual hook called when the escort begins. Default is empty.

**MoveInLineOfSight**: Overrides base AI to allow assisting the player in combat.

**JustDied**: Fails the quest for the player if the NPC dies while escorting.

**JustRespawned**: Resets the escort state, enables combat movement, and calls `Reset`.

**EnterEvadeMode**: Clears combat state, resets spells, and returns the NPC to the combat start position.

**IsPlayerOrGroupInRange**: Checks if the player or group members are within the max distance.

**UpdateAI**: Main update loop. Advances waypoints, checks player proximity, and calls `UpdateEscortAI`.

**ResetEscort**: Kills the NPC and respawns it instantly, restoring questgiver flags.

**UpdateEscortAI**: Virtual method for subclass-specific update logic. Handles passive aggro and standard attacks.

**MovementInform**: Handles arrival at waypoints. Distinguishes between combat return, home return, and standard waypoints.

**FillPointMovementListForCreature**: Loads waypoints from `ScriptMgr` into the internal list.

**SetRun**: Toggles the creature between walking and running.

**Start**: Initiates the escort, loading waypoints and setting up state.

**Stop**: Terminates the escort and clears state.

**SetEscortPaused**: Pauses or resumes waypoint progression.

**GetAIInformation**: Outputs debug info to the chat handler.

**ReturnToCombatStartPosition**: Moves the NPC back to the position where combat started.

---

<!-- machine-true, projected from graph.json -->

## Map — ScriptedEscortAI

*Source:* ScriptedEscortAI.cpp, ScriptedEscortAI.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| npc_escortAI | ctor | Creature.Main/SetEscortable, ScriptedAI/ScriptedAI, WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ | arathi_highlands/npc_kineloryAI, arathi_highlands/npc_professor_phizzlethorpeAI, arathi_highlands/npc_shakes_o_breenAI, ashenvale/npc_feero_ironhandAI, ashenvale/npc_ruul_snowhoofAI, ashenvale/npc_torekAI, blackrock_depths/npc_dughal_stormwingAI, blackrock_depths/npc_grimstoneAI, blackrock_depths/npc_marshal_reginald_windsorAI, blackrock_depths/npc_marshal_windsorAI, blackrock_depths/npc_rocknotAI, blackrock_depths/npc_tobias_seecherAI, boss_celebras_the_cursed/celebrasSpiritAI, burning_steppes/npc_grark_lorkrubAI, darkshore/npc_prospector_remtravelAI, darkshore/npc_theryluneAI, darkshore/npc_volcorAI, desolace/npc_cork_gizeltonAI, desolace/npc_dalinda_malemAI, desolace/npc_melizza_brimbuzzleAI, duskwood/npc_stitchesAI, duskwood/npc_watcher_selkinAI, dustwallow_marsh/npc_stinky_ignatzAI, felwood/npc_areiAI, felwood/npc_captured_arkonarinAI, gnomeregan/npc_blastmaster_emi_shortfuseAI, hinterlands/npc_rinjiAI, loch_modan/npc_miranAI, moonglade/npc_keeper_remulosAI, mulgore/plainVisionAI, razorfen_downs/npc_belnistraszAI, razorfen_kraul/npc_willix_the_importerAI, redridge_mountains/npc_corporal_keeshan_escortAI, silverpine_forest/npc_deathstalker_erlandAI, swamp_of_sorrows/npc_galen_goodwardAI, tanaris/npc_yehkinyaAI, the_barrens/npc_giltharesAI, the_barrens/npc_wizzlecranks_shredderAI, thousand_needles/npc_lakota_windsongAI, thousand_needles/npc_paoka_swiftmountainAI, ThreatListCopier.battleground_alterac/AV_NpcEventAI, ThreatListCopier.battleground_alterac/AV_NpcEventTroopsAI, ThreatListCopier.battleground_alterac/AV_npc_troops_chief_EventAI, ThreatListCopier.battleground_alterac/av_world_boss_baseai, ungoro_crater/npc_ame01AI, wailing_caverns/npc_disciple_of_naralexAI, westfall/npc_daphne_stilwellAI, wetlands/npc_tapoke_slim_jahnAI | — |
| ~npc_escortAI | dtor | — | — | — |
| Reset | decl | — | ThreatListCopier.battleground_alterac/EnterEvadeMode | — |
| ResetCreature | method | — | — | — |
| setCurrentWP | method | Log.Main/Out, Object/GetEntry | blackrock_depths/WaypointReached#5, ThreatListCopier.battleground_alterac/JustDied, ThreatListCopier.battleground_alterac/Reset#5 | — |
| EnterCombat | method | WorldObject.Object/GetOrientation, WorldObject.Object/GetPosition#2 | blackrock_depths/EnterCombat | — |
| getCurrentWP | method | — | ThreatListCopier.battleground_alterac/JustDied, ThreatListCopier.battleground_alterac/Reset#5 | — |
| Aggro | method | — | — | — |
| WaypointReached | decl | — | — | — |
| WaypointStart | method | — | — | — |
| GetPlayerForEscort | method | Map.Main/GetPlayer, ObjectGuid/ObjectGuid#5, WorldObject.Object/GetMap | arathi_highlands/FinishEvent, arathi_highlands/UpdateEscortAI#2, arathi_highlands/WaypointReached, arathi_highlands/WaypointReached#2, ashenvale/JustSummoned, ashenvale/WaypointReached, ashenvale/WaypointReached#2, ashenvale/WaypointReached#3, blackrock_depths/Aggro#3, blackrock_depths/DoJailBreakQuestCredit, blackrock_depths/JustDied#3, blackrock_depths/JustDied#4, blackrock_depths/UpdateEscortAI#3, blackrock_depths/WaypointReached, blackrock_depths/WaypointReached#3, blackrock_depths/WaypointReached#4, boss_celebras_the_cursed/UpdateEscortAI, boss_celebras_the_cursed/WaypointReached, burning_steppes/JustDidDialogueStep, burning_steppes/JustSummoned, burning_steppes/WaypointReached, darkshore/JustSummoned#2, darkshore/MovementInform, darkshore/UpdateAI#2, darkshore/WaypointReached, darkshore/WaypointReached#2, darkshore/WaypointReached#3, desolace/Dialogue, desolace/WaypointReached#2, desolace/WaypointReached#3, dustwallow_marsh/UpdateAI#6, dustwallow_marsh/WaypointReached, felwood/Aggro, felwood/Dialogue, felwood/WaypointReached#2, hinterlands/UpdateEscortAI, hinterlands/WaypointReached, loch_modan/WaypointReached, moonglade/DoHandleOutro, moonglade/EnterEvadeMode#2, moonglade/JustDied, moonglade/UpdateAI#2, moonglade/UpdateEscortAI, moonglade/WaypointReached, razorfen_downs/UpdateEscortAI, razorfen_kraul/WaypointReached, redridge_mountains/WaypointReached, redridge_mountains/WaypointStart, silverpine_forest/WaypointReached, swamp_of_sorrows/WaypointReached, the_barrens/UpdateEscortAI, the_barrens/WaypointReached, the_barrens/WaypointReached#2, the_barrens/WaypointStart, thousand_needles/WaypointReached, thousand_needles/WaypointReached#2, ungoro_crater/Aggro, ungoro_crater/WaypointReached, westfall/WaypointReached, wetlands/UpdateEscortAI, wetlands/WaypointReached | — |
| AssistPlayerInCombat | method | Creature.Main/CanAssistPlayers, CreatureAI/AttackStart, Unit.Main/AddThreat, Unit.Main/GetCharmerOrOwnerPlayerOrPlayerItself, Unit.Main/GetVictim, Unit.Main/HasUnitState, Unit.Main/IsFriendlyTo, Unit.Main/IsTargetableBy, Unit.Main/SetInCombatWith, WorldObject.Object/IsWithinDistInMap, WorldObject.Object/IsWithinLOSInMap | — | — |
| SetPathfindingEnabledBetweenWaypoints | method | — | wetlands/npc_tapoke_slim_jahnAI | — |
| SetDelayBeforeTheFirstWaypoint | method | — | wetlands/npc_tapoke_slim_jahnAI | — |
| HasEscortState | method | — | arathi_highlands/Reset#3, arathi_highlands/UpdateEscortAI#2, ashenvale/Reset#3, blackrock_depths/Reset#6, blackrock_depths/Reset#7, blackrock_depths/Reset#9, burning_steppes/Aggro, burning_steppes/MoveInLineOfSight, burning_steppes/Reset, burning_steppes/UpdateEscortAI, darkshore/Reset#7, desolace/Reset#4, duskwood/UpdateEscortAI, felwood/Reset, felwood/Reset#2, gnomeregan/Reset, hinterlands/Aggro, hinterlands/UpdateEscortAI, loch_modan/AreaTrigger_at_huldar_miran, loch_modan/Reset, moonglade/EnterEvadeMode#2, moonglade/Reset#2, razorfen_downs/AttackedBy, razorfen_downs/AttackStart, razorfen_downs/UpdateEscortAI, silverpine_forest/MoveInLineOfSight, silverpine_forest/Reset, swamp_of_sorrows/Aggro, swamp_of_sorrows/UpdateEscortAI, the_barrens/Reset#6, ThreatListCopier.battleground_alterac/Reset#5, westfall/Reset, wetlands/Aggro, wetlands/DamageTaken, wetlands/GossipHello_npc_mikhail | — |
| SetMaxPlayerDistance | method | — | desolace/WaypointReached#3, moonglade/QuestAccept_npc_keeper_remulos | — |
| SetMaxAssistDistance | method | — | — | — |
| AddEscortState | method | — | — | — |
| RemoveEscortState | method | — | arathi_highlands/FinishEvent | — |
| SetCombatStartPosition | method | — | — | — |
| GetCombatStartPosition | method | — | — | — |
| JustStartedEscort | method | — | — | — |
| MoveInLineOfSight | method | BasicAI/MoveInLineOfSight | burning_steppes/MoveInLineOfSight, darkshore/MoveInLineOfSight#3, gnomeregan/MoveInLineOfSight, silverpine_forest/MoveInLineOfSight | — |
| JustDied | method | ObjectGuid/ObjectGuid, Player.Main/GroupEventFailHappens, Player.Main/SetEscortingGuid, QuestDef/GetQuestId | ashenvale/JustDied#2, razorfen_downs/JustDied, wetlands/JustDied | — |
| JustRespawned | method | Creature.Main/GetCreatureInfo, CreatureAI/IsCombatMovementEnabled, CreatureAI/SetCombatMovement, Unit.Main/GetFactionTemplateId, Unit.Main/SetFactionTemplateId | arathi_highlands/JustRespawned, ashenvale/JustRespawned, darkshore/JustRespawned#2, darkshore/JustRespawned#4, darkshore/JustRespawned#5, desolace/JustRespawned, dustwallow_marsh/JustRespawned, felwood/JustRespawned, hinterlands/JustRespawned, razorfen_kraul/JustRespawned, the_barrens/JustRespawned, thousand_needles/JustRespawned, thousand_needles/JustRespawned#2, ThreatListCopier.battleground_alterac/JustRespawned, ungoro_crater/JustRespawned, wetlands/JustRespawned | — |
| EnterEvadeMode | method | Creature.Main/GetCreatureInfo, Creature.Main/RemoveAurasAtReset, Creature.Main/SetLootRecipient, CreatureAI/SetSpellsList#2, Unit.Main/ClearComboPointHolders, Unit.Main/CombatStop, Unit.Main/DeleteThreatList | blackrock_depths/EnterEvadeMode, moonglade/EnterEvadeMode#2 | — |
| IsPlayerOrGroupInRange | method | Group/GetFirstMember, GroupReference/next, Player.Main/GetGroup, WorldObject.Object/IsWithinDistInMap | — | — |
| UpdateAI | method | Creature.Main/DisappearAndDie, Creature.Main/GetRespawnCoord, Creature.Main/Respawn, Creature.MotionMaster/GetCurrentMovementGeneratorType, Creature.MotionMaster/MovePoint, Log.Main/Out, MovementGenerator/Initialize#2, ObjectGuid/ObjectGuid, Player.Main/SetEscortingGuid, Unit.Main/GetMotionMaster, Unit.Main/IsInCombat, WorldObject.Object/SetFlag | blackrock_depths/UpdateAI#4, darkshore/UpdateAI#2, desolace/UpdateAI#3, dustwallow_marsh/UpdateAI#6, felwood/UpdateAI#2, moonglade/UpdateAI#2 | — |
| ResetEscort | method | Creature.Main/DisappearAndDie, Creature.Main/Respawn, WorldObject.Object/SetFlag | boss_celebras_the_cursed/UpdateEscortAI | — |
| UpdateEscortAI | method | Creature.Main/SelectNearestHostileUnitInAggroRange, CreatureAI/AttackStart, CreatureAI/DoMeleeAttackIfReady, CreatureAI/UpdateSpellsList, Object/GetTypeId, Unit.Main/AI, Unit.Main/GetCharmInfo, Unit.Main/GetVictim, Unit.Main/IsInCombat, Unit.Main/SelectHostileTarget | arathi_highlands/UpdateEscortAI#2, blackrock_depths/UpdateEscortAI, blackrock_depths/UpdateEscortAI#2, blackrock_depths/UpdateEscortAI#3, blackrock_depths/UpdateEscortAI#5, desolace/UpdateEscortAI | — |
| MovementInform | method | Creature.Main/GetName, Log.Main/Out, Object/GetEntry, Unit.Main/SetWalk, WorldObject.Object/GetDistance#4 | darkshore/MovementInform, wailing_caverns/MovementInform | — |
| FillPointMovementListForCreature | method | Escort_Waypoint/Escort_Waypoint, Object/GetEntry, ScriptMgr/GetPointMoveList | — | — |
| SetRun | method | Log.Main/Out, Unit.Main/SetWalk | arathi_highlands/WaypointReached, arathi_highlands/WaypointReached#2, boss_celebras_the_cursed/WaypointReached, darkshore/WaypointReached#2, desolace/Dialogue, felwood/WaypointReached#2, hinterlands/WaypointReached, swamp_of_sorrows/WaypointReached, the_barrens/WaypointReached#2, the_barrens/WaypointStart, wailing_caverns/UpdateEscortAI, westfall/WaypointReached, wetlands/DamageTaken, wetlands/WaypointReached | — |
| Start | method | Creature.MotionMaster/GetCurrentMovementGeneratorType, Creature.MotionMaster/MoveIdle, Log.Main/Out, MotionMaster/MovementExpired, Object/GetObjectGuid, Player.Main/SetEscortingGuid, Unit.Main/GetMotionMaster, Unit.Main/IsInCombat, Unit.Main/SetWalk, WorldObject.Object/SetUInt32Value | arathi_highlands/QuestAccept_npc_kinelory, arathi_highlands/QuestAccept_npc_professor_phizzlethorpe, arathi_highlands/QuestAccept_npc_shakes_o_breen, ashenvale/QuestAccept_npc_feero_ironhand, ashenvale/QuestAccept_npc_ruul_snowhoof, ashenvale/QuestAccept_npc_torek, blackrock_depths/OnScriptEventHappened, blackrock_depths/OnScriptEventHappened#2, blackrock_depths/QuestAccept_npc_marshal_windsor, blackrock_depths/UpdateAI#4, blackrock_depths/UpdateEscortAI#4, blackrock_depths/WaypointReached#4, boss_celebras_the_cursed/QuestAccepted, burning_steppes/QuestAccept_npc_grark_lorkrub, darkshore/QuestAccept_npc_prospector_remtravel, darkshore/QuestAccept_npc_therylune, darkshore/StartEscort, desolace/QuestAccept_npc_dalinda_malem, desolace/QuestAccept_npc_melizza_brimbuzzle, desolace/UpdateEscortAI, duskwood/JustSummoned, duskwood/LaunchStitches, duskwood/UpdateEscortAI, dustwallow_marsh/QuestAccept_npc_stinky_ignatz, felwood/QuestAccept_npc_arei, felwood/QuestAccept_npc_captured_arkonarin, gnomeregan/UpdateEscortAI, hinterlands/QuestAccept_npc_rinji, loch_modan/QuestAccept_npc_miran, moonglade/QuestAccept_npc_keeper_remulos, mulgore/UpdateEscortAI, razorfen_downs/QuestAccept_npc_belnistrasz, razorfen_kraul/QuestAccept_npc_willix_the_importer, redridge_mountains/QuestAccept_npc_corporal_keeshan, silverpine_forest/QuestAccept_npc_deathstalker_erland, swamp_of_sorrows/QuestAccept_npc_galen_goodward, tanaris/QuestRewarded_npc_yehkinya, the_barrens/QuestAccept_npc_gilthares, the_barrens/QuestAccept_npc_wizzlecranks_shredder, thousand_needles/QuestAccept_npc_lakota_windsong, thousand_needles/QuestAccept_npc_paoka_swiftmountain, ThreatListCopier.battleground_alterac/GossipHello_npc_AVBlood_collector, ThreatListCopier.battleground_alterac/GossipSelect_npc_AVBlood_collector, ThreatListCopier.battleground_alterac/JustDied, ThreatListCopier.battleground_alterac/JustDied#2, ThreatListCopier.battleground_alterac/QuestComplete_AV_npc_troops_chief, ThreatListCopier.battleground_alterac/QuestComplete_npc_AVBlood_collector, ThreatListCopier.battleground_alterac/Reset#5, ThreatListCopier.battleground_alterac/UpdateEscortAI#3, ThreatListCopier.battleground_alterac/UpdateEscortAI#4, ungoro_crater/QuestAccept_npc_ame01, wailing_caverns/OnScriptEventHappened, westfall/QuestAccept_npc_daphne_stilwell, wetlands/QuestAccept_npc_mikhail | — |
| Stop | method | ObjectGuid/ObjectGuid, Player.Main/SetEscortingGuid | boss_celebras_the_cursed/WaypointReached, duskwood/WaypointReached, ThreatListCopier.battleground_alterac/JustRespawned, ThreatListCopier.battleground_alterac/WaypointReached, ThreatListCopier.battleground_alterac/WaypointReached#3, ThreatListCopier.battleground_alterac/WaypointReached#4, ThreatListCopier.battleground_alterac/WaypointReached#5, wailing_caverns/UpdateEscortAI, wetlands/UpdateEscortAI | — |
| SetEscortPaused | method | — | arathi_highlands/QuestAccept_npc_shakes_o_breen, blackrock_depths/UpdateEscortAI#2, blackrock_depths/UpdateEscortAI#3, blackrock_depths/WaypointReached#3, blackrock_depths/WaypointReached#4, blackrock_depths/WaypointReached#5, boss_celebras_the_cursed/celebrasSpiritAI, boss_celebras_the_cursed/JustStartedEscort, boss_celebras_the_cursed/UpdateEscortAI, boss_celebras_the_cursed/WaypointReached, burning_steppes/SummonedCreatureJustDied, burning_steppes/WaypointReached, darkshore/StartEscort, darkshore/WaypointReached#3, desolace/Dialogue, desolace/SummonedCreatureJustDied, desolace/UpdateEscortAI, desolace/WaypointReached, desolace/WaypointReached#3, felwood/Dialogue, felwood/WaypointReached, gnomeregan/UpdateEscortAI, gnomeregan/WaypointReached, moonglade/UpdateAI#2, moonglade/UpdateEscortAI, moonglade/WaypointReached, razorfen_downs/UpdateEscortAI, razorfen_downs/WaypointReached, razorfen_kraul/WaypointReached, tanaris/UpdateEscortAI, tanaris/WaypointReached, ThreatListCopier.battleground_alterac/Aggro#2, ThreatListCopier.battleground_alterac/Aggro#3, ThreatListCopier.battleground_alterac/Aggro#4, ThreatListCopier.battleground_alterac/Aggro#5, ThreatListCopier.battleground_alterac/Aggro#6, ThreatListCopier.battleground_alterac/Reset#10, ThreatListCopier.battleground_alterac/Reset#4, ThreatListCopier.battleground_alterac/Reset#5, ThreatListCopier.battleground_alterac/Reset#6, ThreatListCopier.battleground_alterac/Reset#7, ThreatListCopier.battleground_alterac/UpdateEscortAI, ThreatListCopier.battleground_alterac/UpdateEscortAI#5, ThreatListCopier.battleground_alterac/WaypointReached, ThreatListCopier.battleground_alterac/WaypointReached#5, wailing_caverns/Aggro, wailing_caverns/EnterEvadeMode, wailing_caverns/UpdateEscortAI, wailing_caverns/WaypointReached, wetlands/DamageTaken | — |
| GetAIInformation | method | ChatHandler.Chat/PSendSysMessage, CreatureAI/GetAIInformation | — | — |
| ReturnToCombatStartPosition | method | Creature.MotionMaster/GetCurrentMovementGeneratorType, Creature.MotionMaster/MovePoint, Creature.MotionMaster/MoveTargetedHome, Log.Main/Out, Object/GetEntry, Unit.Main/DisableSpline, Unit.Main/GetMotionMaster, WorldObject.Object/GetDistance2d#4, WorldObject.Object/GetPosition#2 | ThreatListCopier.battleground_alterac/EnterEvadeMode, wailing_caverns/EnterEvadeMode | — |
