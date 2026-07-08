# quest_stormwind_rendezvous

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# quest_stormwind_rendezvous

## Purpose & Responsibilities

This unit implements the scripted event for the World of Warcraft quests **"Stormwind Rendezvous"** (Quest ID 6402) and **"The Great Masquerade"** (Quest ID 6403). It orchestrates a complex, multi-stage cinematic sequence involving Marshal Reginald Windsor, Squire Rowe, Lord Bolvar Fordragon, Prince Anduin Wrynn, Lady Katrana Prestor (disguised as Lady Onyxia), and General Marcus Jonathan.

The script handles two distinct activation paths depending on the server's configured game patch:
1.  **Patch 1.12+ (Default):** The event is triggered via gossip interaction with **Squire Rowe** (`npc_squire_rowe`). Rowe runs to a location, summons Windsor, and the event begins.
2.  **Pre-1.12 (Legacy):** The event is triggered automatically when a player enters the **Stormwind Gates** area trigger (`at_stormwind_gates`), which summons Windsor directly.

Once active, `npc_reginald_windsorAI` drives the entire narrative through a large state machine (`Tick` counter), managing dialogue, movement waypoints, summoning guards, initiating combat between Bolvar and Prestor's guards, and finally completing the quest for the player and their group.

## Member-by-Member Behavior

### Squire Rowe AI (`npc_squire_roweAI`)

This AI controls the initial phase of the event in Patch 1.12+. It waits for player interaction, then executes a short sequence to summon Marshal Windsor.

*   **Constructor (`npc_squire_roweAI`)**: Initializes the AI and calls `ResetCreature`.
*   **`ResetCreature`**: Resets internal timers, steps, and flags. Sets the creature as a gossip giver.
*   **`MovementInform`**: Handles waypoint completion events.
    *   Point 1: Moves to the next waypoint.
    *   Point 2: Performs a kneeling emote and advances the step counter.
    *   Point 4: Re-enables gossip and signals the event is processed.
*   **`UpdateAI`**: Drives the Rowe sequence if `m_bEventProcessed` is true.
    *   Step 0: Starts running.
    *   Step 2: Summons the "Flare of Justice" game object and moves back.
    *   Step 3: Summons **Marshal Windsor** (`NPC_REGINALD_WINDSOR`). It passes the player's GUID and Rowe's GUID to Windsor's AI so they can communicate. It mounts Windsor and sends him to the first event waypoint.
    *   Step 4: Returns Rowe to his respawn coordinates.
*   **`GossipHello_npc_squire_rowe`**: Displays gossip options based on quest status. If the player has completed "Stormwind Rendezvous" but not "The Great Masquerade," and Windsor isn't already summoned, it offers the option to start the event.
*   **`GossipSelect_npc_squire_rowe`**: Upon selection, sets `m_bEventProcessed` to true, stores the player's GUID, and disables gossip on Rowe to begin the sequence.
*   **`GetAI_npc_squire_rowe`**: Factory function to create the AI instance.

### Marshal Windsor AI (`npc_reginald_windsorAI`)

This is the core controller for the event. It uses a `Tick` variable as a state machine index to progress through dozens of dialogue lines, movements, and combat triggers.

*   **Constructor (`npc_reginald_windsorAI`)**: Initializes the AI and calls `ResetCreature`.
*   **`GetPlayer`**: Retrieves the `Player` pointer associated with the stored `playerGUID`.
*   **`GetGuard`**: Retrieves a specific summoned guard by index from the `GuardsGUIDs` array.
*   **`ResetCreature`**: Resets all internal state variables (timers, flags, GUID arrays). Crucially, it re-enables `MoveInLineOfSight` events on the creature.
*   **`JustDied`**: If Windsor dies unexpectedly, it notifies Rowe (`PokeRowe`) and despawns after 7 minutes.
*   **`PokeRowe`**: Sends a signal to Squire Rowe's AI to reset his state, allowing the event to be restarted or cleaned up. This is used if Windsor dies or the event times out.
*   **`CompleteQuest`**: Awards "The Great Masquerade" to the primary player and all members of their group if they don't already have it completed.
*   **`EndScene`**: Cleans up the final cinematic positions. It moves Bolvar and Anduin to their respawn coordinates and makes Onyxia invisible.
*   **`UpdateAI_corpse`**: Handles the post-combat cleanup if Windsor is feigning death. It waits for Bolvar to finish fighting, then transforms Onyxia back into Katrana Prestor, despawns her, and completes the quest.
*   **`MoveInLineOfSight`**: Detects nearby Stormwind guards. If the event is in the "NeedCheck" phase, it recruits them into the `GuardsGUIDs` array, making them salute and speak random lines.
*   **`SpellHit`**: If Windsor is hit by `SPELL_WINDSOR_DEATH`, he feigns death instead of dying, triggering the corpse update logic.
*   **`UpdateAI`**: The main state machine.
    *   **Idle/Timeout**: Despawns Windsor if the player is AFK for too long.
    *   **Initial Sequence (Ticks 0-2)**: Dismisses his horse, greets the player, and starts the quest.
    *   **Guard Summoning (Tick 2)**: Summons 6 guards and an invisible Onyxia/Katrana. Moves General Marcus Jonathan.
    *   **Dialogue & Movement (Ticks 3-27)**: Progresses through dialogue with Marcus and Windsor. Moves guards to specific positions.
    *   **Recruitment (Ticks 28-42)**: Recruits additional guards from the environment via `MoveInLineOfSight`.
    *   **Masquerade Reveal (Ticks 50-63)**: Dialogue with Bolvar and Anduin. Windsor reads tablets, revealing Onyxia's true identity. Onyxia transforms from Katrana to Onyxia.
    *   **Combat (Ticks 68-75)**: Onyxia summons elite guards. Bolvar engages them. Onyxia casts a spell to kill Windsor (feigned).
    *   **Cleanup (Ticks 76-78)**: Bolvar finishes combat. Windsor completes the quest and despawns.
*   **`QuestAccept_npc_reginald_windsor`**: Triggers when the player accepts the quest. It sets `BeginQuest` to true, storing the player's GUID and extending the despawn timer.
*   **`GossipHello_npc_reginald_windsor`**: Allows the player to manually start the event if they haven't accepted the quest yet, or shows standard quest gossip.
*   **`GossipSelect_npc_reginald_windsor`**: Manually starts the event sequence via gossip.
*   **`GetAI_npc_reginald_windsor`**: Factory function to create the AI instance.

### Area Trigger (`AreaTrigger_at_stormwind_gates`)

*   **`AreaTrigger_at_stormwind_gates`**: Legacy trigger for pre-1.12 patches. Checks if the player has the correct quest status and if Windsor is not already spawned. If valid, it summons Windsor directly, bypassing Squire Rowe. It enforces a 15-minute cooldown globally.

### Registration

*   **`AddSC_quest_stormwind_rendezvous`**: Registers the scripts for `npc_squire_rowe`, `npc_reginald_windsor`, and `at_stormwind_gates` with the script manager.

## Cross-Unit Boundaries

*   **`npc_reginald_windsorAI` <-> `npc_squire_roweAI`**:
    *   **Direction**: Windsor calls Rowe.
    *   **Mechanism**: `PokeRowe()` in Windsor's AI retrieves Rowe's creature pointer using `m_squireRoweGuid` (set during Rowe's summoning of Windsor). It casts Rowe's AI to `npc_squire_roweAI` and calls `ResetCreature()`.
    *   **Purpose**: To notify Rowe that the event has ended (either successfully or due to Windsor's death), allowing Rowe to reset his state and potentially allow a new player to start the event.
*   **`npc_squire_roweAI` <-> `npc_reginald_windsorAI`**:
    *   **Direction**: Rowe calls Windsor.
    *   **Mechanism**: In `UpdateAI` (Step 3), Rowe summons Windsor. It retrieves Windsor's AI and sets `playerGUID` and `m_squireRoweGuid` on Windsor's AI.
    *   **Purpose**: To pass context (who started the event, who is Rowe) to the main event controller.
*   **`npc_reginald_windsorAI` <-> `Group`/`Player`**:
    *   **Direction**: Windsor calls Group/Player.
    *   **Mechanism**: `CompleteQuest()` iterates through the player's group members and calls `CompleteQuest` on each.
    *   **Purpose**: To ensure all party members receive credit for the quest.
*   **`npc_reginald_windsorAI` <-> `ScriptMgr`**:
    *   **Direction**: Windsor calls ScriptMgr.
    *   **Mechanism**: `DoScriptText()` is called extensively to play dialogue lines.
    *   **Purpose**: To output text to the chat window for various NPCs.
*   **`npc_reginald_windsorAI` <-> `WorldObject`/`Creature`**:
    *   **Direction**: Windsor calls WorldObject/Creature methods.
    *   **Mechanism**: Uses `FindNearestCreature`, `SummonCreature`, `GetMotionMaster`, `SetFacingToObject`, etc.
    *   **Purpose**: To manipulate the positions, states, and appearances of other NPCs involved in the cinematic.

## Data Model

This unit does not interact directly with any database tables. It relies entirely on in-memory game objects, creatures, and player data. Quest IDs and NPC entries are defined as constants in the header file.

## Notable Implementation Details

*   **Patch-Specific Logic**: The `AreaTrigger_at_stormwind_gates` function checks `sWorld.GetWowPatch()`. If the server is configured for patch 1.12 or higher, the area trigger does nothing, forcing the use of Squire Rowe. This preserves historical accuracy for older patches while supporting the modern quest chain.
*   **Feigned Death**: Windsor does not truly die. The spell `SPELL_WINDSOR_DEATH` triggers `SpellHit`, which calls `SetFeignDeath(true)`. This allows the `UpdateAI_corpse` method to continue running, managing the post-death cinematic where Bolvar fights Onyxia's guards.
*   **Global Cooldown**: The area trigger uses a static global variable `globalWindsorLastSpawnTime` to enforce a 15-minute cooldown between spawns. This is not thread-safe in a multi-threaded environment but is sufficient for single-instance area triggers in this context.
*   **Guard Recruitment**: The `MoveInLineOfSight` method dynamically recruits nearby Stormwind guards into the event. It checks if the guard is within 8 yards and if the event is in the "NeedCheck" phase. It stores their GUIDs in an array and makes them perform emotes and say random lines.
*   **State Machine Complexity**: The `UpdateAI` method for Windsor is extremely large, using a `switch(Tick)` statement with over 70 cases. Each case represents a step in the cinematic, handling dialogue, movement, and state changes. Timers are calculated based on distance and speed to synchronize dialogue with movement.
*   **Onyxia Transformation**: Onyxia is initially summoned as Katrana Prestor (`NPC_KATRANA_PRESTOR`) with an invisible display ID. Later in the event, she updates her entry to `NPC_LADY_ONYXIA` and removes invisibility, revealing her true form. At the end, she is transformed back to Katrana Prestor before despawning.
*   **Thread Safety Note**: The `globalWindsorLastSpawnTime` variable is a static global. In a multi-core server, concurrent access from multiple threads triggering the area trigger could lead to race conditions. However, since area triggers are typically processed sequentially per map instance, this may be acceptable in practice.

## Member Reference

**npc_reginald_windsorAI**
Constructor for the Marshal Windsor AI. Initializes the base `ScriptedAI` and calls `ResetCreature` to set up initial state.

**GetPlayer**
Retrieves the `Player` object associated with the `playerGUID` stored in the AI. Returns `nullptr` if the player is not found on the map.

**GetGuard**
Returns a pointer to a summoned guard creature by index from the `GuardsGUIDs` array. Returns `nullptr` if the guard is not found.

**ResetCreature**
Resets all internal state variables for Windsor, including timers, flags, and GUID arrays. Re-enables `MoveInLineOfSight` events.

**JustDied**
Called when Windsor dies. Notifies Squire Rowe via `PokeRowe` and schedules Windsor to despawn after 7 minutes.

**PokeRowe**
Notifies Squire Rowe's AI that the event has ended. Retrieves Rowe's creature pointer and calls `ResetCreature` on his AI.

**CompleteQuest**
Awards the quest "The Great Masquerade" to the primary player and all members of their group, if they haven't already completed it.

**EndScene**
Moves Bolvar and Anduin to their respawn coordinates and makes Onyxia invisible, cleaning up the final cinematic positions.

**UpdateAI_corpse**
Handles the post-combat cleanup while Windsor is feigning death. Waits for Bolvar to finish fighting, transforms Onyxia back to Katrana Prestor, and completes the quest.

**GetRandomGuardText**
Static function that returns a random dialogue line ID for recruited guards.

**Reset**
Empty override of the base class `Reset` method.

**MoveInLineOfSight**
Detects nearby Stormwind guards. If the event is in the recruitment phase, it adds them to the `GuardsGUIDs` array, makes them salute, and plays a random dialogue line.

**SpellHit**
If Windsor is hit by `SPELL_WINDSOR_DEATH`, he feigns death instead of dying, allowing the corpse update logic to proceed.

**UpdateAI**
The main state machine for Windsor. Manages the entire event sequence, including dialogue, movement, guard summoning, combat initiation, and quest completion. Uses a `Tick` counter to progress through states.

**QuestAccept_npc_reginald_windsor**
Triggers when the player accepts the quest. Sets `BeginQuest` to true, stores the player's GUID, and extends the despawn timer.

**GossipHello_npc_reginald_windsor**
Displays gossip options to the player. If the player has accepted the quest, it offers an option to start the event. Otherwise, it shows standard quest gossip.

**GossipSelect_npc_reginald_windsor**
Manually starts the event sequence via gossip selection. Sets `BeginQuest` to true and plays an introductory dialogue line.

**GetAI_npc_reginald_windsor**
Factory function that creates and returns a new instance of `npc_reginald_windsorAI`.

**npc_squire_roweAI**
Constructor for the Squire Rowe AI. Initializes the base `ScriptedAI` and calls `ResetCreature`.

**ResetCreature#2**
Resets internal state for Squire Rowe, including timers, steps, and flags. Sets the creature as a gossip giver.

**MovementInform**
Handles waypoint completion events for Rowe. Advances the step counter and triggers specific actions like kneeling or re-enabling gossip.

**UpdateAI#2**
Drives Rowe's sequence if `m_bEventProcessed` is true. Summons Windsor, passes context to his AI, and returns Rowe to his spawn point.

**GossipHello_npc_squire_rowe**
Displays gossip options based on quest status. Offers the option to start the event if the player has completed "Stormwind Rendezvous" but not "The Great Masquerade."

**GossipSelect_npc_squire_rowe**
Starts the event sequence via gossip selection. Sets `m_bEventProcessed` to true and stores the player's GUID.

**GetAI_npc_squire_rowe**
Factory function that creates and returns a new instance of `npc_squire_roweAI`.

**AreaTrigger_at_stormwind_gates**
Legacy area trigger for pre-1.12 patches. Spawns Windsor directly if the player has the correct quest status and no cooldown is active.

**AddSC_quest_stormwind_rendezvous**
Registers the scripts for `npc_squire_rowe`, `npc_reginald_windsor`, and `at_stormwind_gates` with the script manager.

---

<!-- machine-true, projected from graph.json -->

## Map — quest_stormwind_rendezvous

*Source:* quest_stormwind_rendezvous.cpp, quest_stormwind_rendezvous.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| npc_reginald_windsorAI | ctor | ScriptedAI/ScriptedAI | — | — |
| GetPlayer | method | Map.Main/GetPlayer, ObjectGuid/ObjectGuid#5, WorldObject.Object/GetMap | — | — |
| GetGuard | method | Map.Main/GetCreature, ObjectGuid/ObjectGuid#5, WorldObject.Object/GetMap | — | — |
| ResetCreature | method | Creature.Main/EnableMoveInLosEvent, ObjectGuid/Clear | — | — |
| JustDied | method | Creature.Main/DespawnOrUnsummon | — | — |
| PokeRowe | method | Creature.Main/AI, Map.Main/GetCreature, WorldObject.Object/GetMap | — | — |
| CompleteQuest | method | Group/GetFirstMember, GroupReference/next, Player.Main/CompleteQuest, Player.Main/GetGroup, Player.Main/GetQuestStatus | — | — |
| EndScene | method | Creature.Main/GetRespawnCoord, Creature.MotionMaster/MovePoint, SpellCaster/CastSpell#2, Unit.Main/GetMotionMaster, Unit.Main/GetSpeed, Unit.Main/RemoveAurasDueToSpell, Unit.Main/SetStandState, WorldObject.Object/FindNearestCreature, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY | — | — |
| UpdateAI_corpse | method | Creature.Main/GetRespawnCoord, Creature.Main/UpdateEntry, Object/SetEntry, Unit.Main/SetFacingTo, WorldObject.Object/FindNearestCreature | — | — |
| GetRandomGuardText | function | shared_Util/urand | — | — |
| Reset | method | — | — | — |
| MoveInLineOfSight | method | Object/GetEntry, Object/GetGUID, Unit.Main/HandleEmote, Unit.Main/IsAlive, Unit.Main/SetFacingToObject, WorldObject.Object/GetDistance2d#3, WorldObject.Object/GetPositionY, WorldObject.Object/MonsterSay#2 | — | — |
| SpellHit | method | Unit.Main/SetFeignDeath | — | — |
| UpdateAI | method | Creature.Main/AIM_Initialize, Creature.Main/ClearTemporaryFaction, Creature.Main/DespawnOrUnsummon, Creature.Main/ForcedDespawn, Creature.Main/Respawn, Creature.Main/SetFactionTemporary, Creature.Main/SetRespawnDelay, Creature.Main/SetRespawnTime, Creature.Main/UpdateEntry, Creature.MotionMaster/MovePoint, CreatureAI/DoCast, GridSearchers/GetCreatureListWithEntryInGrid#2, Map.Main/GetCreature, Object/GetGUID, ObjectGuid/ObjectGuid#5, ScriptMgr/DoScriptText, shared_Util/urand, SpellCaster/CastSpell#2, ThreatManager/addThreatDirectly, Unit.Main/AddThreat, Unit.Main/GetMotionMaster, Unit.Main/GetSpeed, Unit.Main/GetThreatManager, Unit.Main/HandleEmote, Unit.Main/IsInCombat, Unit.Main/SetDisplayId, Unit.Main/SetFacingTo, Unit.Main/SetFacingToObject, Unit.Main/SetInCombatWith, Unit.Main/SetSpeedRate, Unit.Main/SetStandState, Unit.Main/SetTargetGuid, Unit.Main/SetWalk, Unit.Main/Unmount, WorldObject.Object/FindNearestCreature, WorldObject.Object/GetDistance2d#4, WorldObject.Object/GetMap, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/RemoveFlag, WorldObject.Object/SetFlag, WorldObject.Object/SetUInt32Value, WorldObject.Object/SummonCreature#2 | — | — |
| QuestAccept_npc_reginald_windsor | function | Creature.Main/AI, Object/GetGUID, QuestDef/GetQuestId, WorldObject.Object/RemoveFlag | — | — |
| GossipHello_npc_reginald_windsor | function | Creature.Main/AI, GossipDef/AddMenuItem#5, GossipDef/SendGossipMenu, Object/GetGUID, ObjectGuid/ObjectGuid#5, Player.Main/PrepareQuestMenu, PlayerMenu/GetGossipMenu, Unit.Main/IsQuestGiver | — | — |
| GossipSelect_npc_reginald_windsor | function | Creature.Main/AI, GossipDef/CloseGossip, ScriptMgr/DoScriptText, WorldObject.Object/SetUInt32Value | — | — |
| GetAI_npc_reginald_windsor | function | — | — | — |
| npc_squire_roweAI | ctor | npc_squire_roweAI/Reset, ScriptedAI/ScriptedAI | — | — |
| ResetCreature#2 | method | ObjectGuid/Clear, WorldObject.Object/SetFlag | — | — |
| MovementInform | method | Creature.MotionMaster/MovePoint, ScriptMgr/DoScriptText, Unit.Main/GetMotionMaster, Unit.Main/HandleEmote, WorldObject.Object/SetFlag | — | — |
| UpdateAI#2 | method | BasicAI/UpdateAI, Creature.Main/AI, Creature.Main/GetRespawnCoord, Creature.MotionMaster/MovePoint, Object/GetObjectGuid, Unit.Main/GetMotionMaster, Unit.Main/Mount, Unit.Main/SetSpeedRate, Unit.Main/SetWalk, WorldObject.Object/RemoveFlag, WorldObject.Object/SummonCreature#2, WorldObject.Object/SummonGameObject | — | — |
| GossipHello_npc_squire_rowe | function | Creature.Main/AI, GossipDef/AddMenuItem#4, GossipDef/SendGossipMenu, Object/GetGUID, ObjectGuid/ObjectGuid#5, Player.Main/GetQuestStatus, PlayerMenu/GetGossipMenu | — | — |
| GossipSelect_npc_squire_rowe | function | Creature.Main/AI, GossipDef/CloseGossip, Object/GetObjectGuid, Player.Main/GetQuestStatus, Unit.Main/SetWalk, WorldObject.Object/RemoveFlag | — | — |
| GetAI_npc_squire_rowe | function | — | — | — |
| AreaTrigger_at_stormwind_gates | function | Creature.Main/AI, Creature.MotionMaster/MovePoint, GridSearchers/GetClosestCreatureWithEntry, Object/GetGUID, ObjectGuid/ObjectGuid, Player.Main/GetQuestRewardStatus, Player.Main/IsCurrentQuest, Player.Main/IsGameMaster, Unit.Main/GetMotionMaster, Unit.Main/IsAlive, Unit.Main/Mount, Unit.Main/SetSpeedRate, Unit.Main/SetWalk, World/getConfig, World/GetGameTime, World/GetWowPatch, WorldObject.Object/RemoveFlag, WorldObject.Object/SummonCreature#2 | — | — |
| AddSC_quest_stormwind_rendezvous | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
