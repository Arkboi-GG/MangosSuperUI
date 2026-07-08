# world_event_wareffort

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# world_event_wareffort

## Purpose & Responsibilities

The `world_event_wareffort` translation unit implements the server-side logic for the **War Effort** world event in World of Warcraft (specifically the Burning Crusade era, associated with the opening of Ahn'Qiraj). This unit manages two distinct but related aspects of the event:

1.  **Resource Collection & Progress Tracking:** It handles the global accumulation of resources (bars, herbs, skins, cooking items, bandages) contributed by players via quests. It tracks these totals in persistent saved variables, updates World State objects for client-side UI display, and manages the visual representation of resource piles (GameObjects) based on contribution tiers.
2.  **Event Narrative & Combat Encounters:** It provides AI scripts for key NPCs involved in the event's climax, including:
    *   **Resonating Crystals:** Environmental hazards that mind-control players.
    *   **Infantry Units:** Alliance and Horde troops that move into formation and follow the leader, Grom Hellscream (referred to as Saurfang in the codebase due to legacy naming or specific instance usage).
    *   **Grom Hellscream (Saurfang):** The primary boss/leader NPC who delivers speeches, moves along a predefined path to the Ahn'Qiraj gates, and engages in combat.
    *   **Cenarion Hold Attack:** A spawner that summons waves of Qiraji enemies during the defense phase.

This unit relies heavily on `ObjectMgr` for persistent data storage (`GetSavedVariable`/`SetSavedVariable`) and `GameEventMgr` to determine the current phase of the world event. It does not interact directly with SQL tables; all persistence is handled through the server's saved variable system.

## Member-by-Member Behavior

### Resource Management & World States

These functions manage the global state of the War Effort resources and prepare data for client synchronization.

*   **BuildWarEffortWorldStates**: Constructs a `WorldPacket` containing the current progress of all War Effort objectives. It iterates through `AllianceObjectives`, `HordeObjectives`, and `SharedObjectives` arrays defined in the header. For each objective, it retrieves the current count from saved variables and writes pairs of World State IDs and values to the packet. It also checks for active transition events to include a "days remaining" counter. This function is called by `Player.Main` during initialization to sync the player's UI.
*   **AutoCompleteWarEffortProgress**: A utility function likely used for testing or server resets. It reads a configuration rate (`CONFIG_FLOAT_RATE_WAR_EFFORT_RESOURCE`) and artificially increments all resource stocks by that percentage of the required amount. It logs the action and delegates the actual incrementing to `AutoCompleteWarEffortResource`.
*   **AutoCompleteWarEffortResource**: Increments a specific resource's saved variable by a calculated amount, capped at the required total. It ensures the value does not exceed the goal and logs the change.
*   **GetSharedSavedVar**: Maps a shared resource item ID and a team ID to the correct saved variable identifier (e.g., `VAR_WE_ALLIANCE_COPPER`). It validates the team and item ID, logging errors for invalid inputs.
*   **GetTeamStock**: A convenience wrapper that retrieves the current stock count for a shared resource by calling `GetSharedSavedVar` and then `ObjectMgr.GetSavedVariable`.
*   **GetWarEffortGossipTextId**: Determines the correct gossip text ID to display to a player based on the resource item, the player's team, and whether the objective is complete. It uses the `WarEffortGossipText` array from the header.
*   **GetWarEffortGossip**: Searches the `WarEffortGossipText` array for a matching item ID and returns a reference to the gossip structure. Returns the first element if not found (with an error log).
*   **GetActiveTransportEvent**: Checks a predefined list of transition event IDs (Day 1 through Day 5) against `GameEventMgr.IsActiveEvent`. It returns the highest-numbered active event, indicating the current phase of the transition period. If none are active, it returns `EVENT_WAR_EFFORT_TERMINATOR`.

### NPC: AQ War Collector (`npc_AQwar_collectorAI`)

This AI controls the NPCs that accept resource quests. They visually represent the progress of the war effort by spawning/despawning GameObjects (piles of resources) based on the total collected.

*   **ctor**: Initializes the AI. It determines the NPC's team (Alliance or Horde) based on its faction template ID. It immediately checks if the objective is reached and removes the quest giver flag if so. It sets an update timer to 0 to trigger an immediate check.
*   **UpdateAI**: Runs every tick. If the timer expires (every 60 seconds), it calls `HandleWarEffortGameObject` for each resource type (Bars, Herbs, Skins, Cooking, Bandages). It then re-checks if the objective is reached and removes the quest giver flag if necessary.
*   **HandleWarEffortGameObject**: Manages the visual representation of resources.
    *   If supplies are still being gathered (`GatheringSupplies()` is true), it calculates the total progress across all objectives of that type. It then spawns/despawns GameObjects in tiers (1–5) based on how much of the total requirement has been met.
    *   If the gathering phase is over (transition phase), it spawns/despawns GameObjects based on the current transition day (Tier 5 on Day 1, down to Tier 1 on Day 5).
*   **HandleSupplyObjectSpawn**: Finds the nearest GameObject of a specific entry within 50 yards. If `spawn` is true and the GO is not spawned, it sets a respawn time and saves to DB. If `spawn` is false and the GO is spawned, it despawns it and saves to DB.
*   **ObjectiveReached#2**: Takes a `Quest` pointer, extracts the required item ID, and checks if the current stock for that item meets the requirement. It identifies whether the item is a shared or faction-specific objective and sets internal state (`resourceItemId`, `resourceType`).
*   **ObjectiveReached**: Iterates through all quests assigned to this creature entry. For each quest, it calls the overloaded `ObjectiveReached(Quest*)`. If any quest's objective is met, it returns true.
*   **RemoveQuestGiverFlag**: Removes the `UNIT_NPC_FLAG_QUESTGIVER` flag from the creature, preventing further quest turns-ins.
*   **GatheringSupplies**: Returns true if the active transport event is `EVENT_WAR_EFFORT_TERMINATOR`, indicating the collection phase is active.
*   **SendWorldStateUpdateToPlayer**: Sends a specific World State update to a player for the resource this NPC handles, ensuring their UI reflects the latest server-side count.

### Gossip & Quest Handlers

*   **GossipHello_npc_AQwar_collector**: Handles the gossip menu for collectors. It checks if the objective is reached. If so, it clears menus, shows completion text, and makes the NPC bow. If not, it prepares the quest menu if the NPC is still a quest giver. It always sends the appropriate gossip text based on progress.
*   **QuestComplete_npc_AQwar_collector**: Triggered when a player turns in a quest. It retrieves the required item ID and count from the quest. It adds the count to the corresponding saved variable (shared or faction-specific). It then checks if the objective is now reached; if so, it removes the quest giver flag and makes the NPC cheer.
*   **GetWarEffortStockInfo**: Populates a `WarEffortStockInfo` struct with details about a specific resource (current count, required count, item prototype, saved var ID). Used by chat commands to query status.
*   **GetAI_npc_AQwar_collector**: Factory function returning a new `npc_AQwar_collectorAI`.

### NPC: Resonating Crystal (`npc_resonating_CrystalAI`)

This AI controls environmental crystals that cast "Whisperings of C'Thun" (Mind Control) on players.

*   **ctor**: Initializes timers and selects the correct Mind Control spell ID based on the zone the crystal is in (using `GetMCSpellForZone`).
*   **Reset#5**: Resets combat movement and timers.
*   **GetMCSpellForZone**: Returns a spell ID based on the creature's zone ID. Different zones have different spell IDs for the mind control effect, likely scaling with level or difficulty.
*   **MoveInLineOfSight#3**: Detects players entering line of sight. If a valid player is within `MAX_SIGHT_DISTANCE` (55 yards), it sets `playerDetected` to true.
*   **MoreThanOnePlayerNear**: Iterates through all players on the map. Counts how many are alive, within 55 yards, and not GMs. Returns true if more than one is present.
*   **AggroAllPlayerNear**: Adds threat and enters combat with all players within 55 yards.
*   **UpdateAI#5**:
    *   If a player was detected, it waits 2 seconds (`m_uiCheckTimer`). Then it checks if more than one player is near. If yes, it aggroes all nearby players. If no, it evades (resets).
    *   If in combat, it attempts to cast the Mind Control spell on a random target every 20 seconds, provided more than one player is near or the crystal is polymorphed (preventing cast if polymorphed? No, the code casts *if* polymorphed OR timer expires, but checks `CF_AURA_NOT_PRESENT` on the target).

### NPC: Infantrymen (`npc_infantrymanAI` and subclasses)

This base AI and its subclasses handle the movement and formation of infantry units during the event.

*   **ctor**: Initializes state variables for movement and following.
*   **MoveInLineOfSight#2**: Standard aggro logic. Attacks if hostile, targetable, and in LOS.
*   **Reset#3**: Empty override.
*   **JustDied**: If the infantry was following Saurfang, it sets its respawn position near Saurfang. It also triggers a speech and buff (`SPELL_SF_VENGEANCE`) on Saurfang if he is in combat and doesn't already have the aura.
*   **EnterEvadeMode#2**: Custom evade logic. Stops combat, removes auras, and either follows Saurfang (if previously following) or moves home.
*   **UpdateAI#4**:
    *   If the `EVENT_WAR_EFFORT_CH_ATTACK` is active and the unit hasn't moved yet, it calls `MoveToWaveBattlePosition`.
    *   If the `EVENT_WAR_EFFORT_FINALBATTLE` is active and the unit isn't following Saurfang, it starts following him.
*   **CalculateRotatedPositionAboutLeader**: Helper to rotate a position vector around an origin point.
*   **MoveToWaveBattlePosition#2**: Calculates a new position by rotating the creature's home position around a faction-specific origin point. It then moves the creature to this new position.
*   **FollowSaurfang**: Finds the Saurfang NPC. Calculates a follow distance and angle based on the creature's original home position relative to Saurfang's incoming position. Starts following Saurfang.
*   **SetRespawnNearSaurfang**: Calculates a new home position near Saurfang based on the follow offset, ensuring the infantry respawns close to the leader.
*   **JustReachedHome#2**: Plays a ready emote.
*   **JustRespawned#2**: Resets the following state.

Subclasses (`npc_ironforge_infantryAI`, `npc_orgrimmar_infantryAI`, `npc_orgrimmar_riflemanAI`, `npc_priestessAI`) inherit from `npc_infantrymanAI` and override constructors or specific methods to define their unique origins, rotations, emotes, and mounting behavior.

*   **npc_priestessAI**: Overrides `MoveToWaveBattlePosition` to spread priestesses along a line between two points. Overrides `FollowSaurfang` to mount before following.

### NPC: Grom Hellscream / Saurfang (`npc_aqwar_saurfangAI`)

This AI controls the main leader NPC.

*   **ctor**: Sets faction and initializes timers.
*   **Reset#2**: Randomizes spell cooldowns.
*   **EnterCombat**: Unmounts, plays aggro speech, and casts self-buff.
*   **KilledUnit**: Randomly plays a kill speech.
*   **EnterEvadeMode**: Plays victory speech if it was the last wave.
*   **MoveInLineOfSight**: Aggro logic.
*   **MovementInform**: Handles pathfinding waypoints. Updates facing and home position as he moves along the `saurfangGatePath`.
*   **UpdateAI#3**:
    *   **Phase 1 (CH Attack):** Moves to battle position.
    *   **Phase 2 (Final Battle):** Becomes immune, mounts, and begins a speech sequence. After 14 speech steps, broadcasts a world message, removes immunity, and starts moving along the gate path.
    *   **Combat:** Casts spells (Mortal Strike, Cleave, Charge, Terrifying Roar) on cooldowns. Performs melee attacks.
*   **JustRespawned**: Resets movement state if he was moving to the gate.
*   **MoveToWaveBattlePosition**: Moves to the initial battle position.
*   **JustReachedHome**: Unpauses movement.
*   **GetAI_npc_aqwar_saurfang**: Factory function that returns `npc_aqwar_saurfangAI` if the creature is in Silithus (Zone 1377), otherwise returns a generic `CreatureEventAI`.

### NPC: Cenarion Hold Attack Spawner (`npc_aqwar_cenarionhold_attackAI`)

*   **ctor**: Initializes wave counters and timers.
*   **Reset**: Empty.
*   **UpdateAI#2**: Every 15 minutes (after an initial 1-minute delay), it spawns a wave of 10 creatures (80% Colossal Anubisath, 20% Qiraji Destroyer) at a random position near the spawner. These summons move towards a target position. After 12 waves, it marks Saurfang's AI as having completed the last wave.
*   **GetAI_npc_aqwar_cenarionhold_attack**: Factory function returning a new `npc_aqwar_cenarionhold_attackAI`.

### Script Registration

*   **AddSC_war_effort**: Registers all the above scripts with the `ScriptMgr`.

## Cross-Unit Boundaries

*   **ObjectMgr**: Heavily used for persistent storage. `GetSavedVariable` and `SetSavedVariable` are called by resource management functions and AI constructors/updaters to read/write global resource counts. `GetItemPrototype` is used to fetch item details for UI/display. `GetQuestTemplate` and `GetCreatureQuestRelationsMapBounds` are used to determine which quests an NPC offers and their requirements.
*   **WorldStates**: `WriteInitialWorldStatePair` is used by `BuildWarEffortWorldStates` to construct the packet sent to clients.
*   **Player.Main**: `SendInitWorldStates` calls `BuildWarEffortWorldStates`. `SendUpdateWorldState` is called by `SendWorldStateUpdateToPlayer` to push individual updates. `GetTeamId` is used extensively to determine faction-specific behavior. `PrepareQuestMenu` and `SendGossipMenu` are used in gossip handlers.
*   **ChatHandler.HardcodedEvents**: Several command handlers (`HandleWarEffortGetResource`, etc.) call `GetWarEffortStockInfo` and `AutoCompleteWarEffortProgress` to allow GMs to manipulate or view event state.
*   **GameEventMgr.Main**: `IsActiveEvent` is called by `GetActiveTransportEvent`, `npc_infantrymanAI::UpdateAI`, `npc_aqwar_saurfangAI::UpdateAI`, and `npc_aqwar_cenarionhold_attackAI::UpdateAI` to determine the current phase of the world event.
*   **Log.Main**: Used for debugging and status logging in resource management and error handling.
*   **CreatureAI / ScriptedAI / BasicAI**: Base classes for the various NPC AIs. Methods like `DoCastSpellIfCan`, `SelectAttackingTarget`, `EnterEvadeMode`, and `JustDied` are overridden or called.
*   **Unit.Main**: Used for combat interactions (`AddThreat`, `SetInCombatWith`, `CastSpell`, `HasAura`, `IsHostileTo`, etc.), movement (`GetMotionMaster`), and state checks (`IsAlive`, `IsGameMaster`).
*   **GameObject**: Used by `npc_AQwar_collectorAI` to spawn/despawn visual representations of resources.
*   **Map.Main**: Used to access player lists and creature lists for aggro and targeting logic.
*   **ScriptMgr**: `DoScriptText` is used to play NPC speeches. `RegisterSelf` is used in `AddSC_war_effort`.
*   **ScriptLoader**: Calls `AddSC_war_effort` to load the scripts.

## Data Model

This unit does not interact directly with SQL tables. All persistent data (resource counts) is stored in the server's saved variable system, accessed via `ObjectMgr.GetSavedVariable` and `ObjectMgr.SetSavedVariable`. The specific variable IDs are defined as constants in the header file (e.g., `VAR_WE_ALLIANCE_COPPER`). The configuration for auto-completion is read from the world configuration system (`sWorld.getConfig`).

## Notable Implementation Details

*   **Hardcoded Data:** The resource objectives, requirements, World State IDs, and gossip texts are hardcoded in static arrays in the header file (`world_event_wareffort.h`). This means changes to the event's balance or content require code recompilation.
*   **Legacy Naming:** The main leader NPC is referred to as "Saurfang" in the code (`npc_aqwar_saurfangAI`, `NPC_SAURFANG`), but the context (Ah'Qiraj War Effort, Grom Hellscream's role in the lore) suggests this is Grom Hellscream. This might be a leftover from an earlier development stage or a specific instance reuse.
*   **Visual Tiers:** The `npc_AQwar_collectorAI` uses a tiered system (1–5) for GameObjects to visually represent progress. During the gathering phase, higher tiers spawn as more resources are collected. During the transition phase, tiers despawn in reverse order as the event progresses towards the final battle.
*   **Formation Logic:** Infantry units use a rotation matrix to position themselves in a semi-circle or line around a central origin point. This creates a visual formation effect.
*   **Follow Logic:** Infantry units calculate a fixed offset (distance and angle) from their original home position relative to Saurfang's incoming position. They maintain this offset while following Saurfang, creating a cohesive group movement.
*   **Mind Control Spell Variation:** The `npc_resonating_CrystalAI` uses different spell IDs for "Whisperings of C'Thun" depending on the zone. This suggests the effect's strength or duration varies by area, likely to match the level of players expected in that zone.
*   **Wave Spawning:** The `npc_aqwar_cenarionhold_attackAI` spawns waves of enemies at fixed intervals. The number of waves (12) and interval (15 minutes) are hardcoded comments indicate these are estimates based on community reports, not official Blizzard data.
*   **Error Handling:** Functions like `GetSharedSavedVar` and `GetWarEffortGossip` log errors for invalid inputs but return default values (0 or first element) to prevent crashes. This can lead to silent failures if data is misconfigured.
*   **Timer Management:** Most AIs use manual timer management (`m_timer -= diff`) rather than relying solely on base class timers. This allows for more complex conditional logic but requires careful handling to avoid negative timers or missed ticks.
*   **Global State:** The `priestessIndex` static variable in `npc_priestessAI` is used to assign unique positions to each priestess. This relies on the order of creature loading/spawning, which might not be deterministic across server restarts or different map loads.

## Member Reference

**BuildWarEffortWorldStates**: Constructs a `WorldPacket` with current War Effort progress for all objectives, retrieving counts from saved variables and writing World State pairs. Includes transition day countdown if applicable. Called by `Player.Main/SendInitWorldStates`.

**AutoCompleteWarEffortProgress**: Utility to artificially increment all resource stocks by a configured rate. Logs actions and calls `AutoCompleteWarEffortResource` for each objective. Called by `ChatHandler.HardcodedEvents/UpdateWarEffortCollection`.

**AutoCompleteWarEffortResource**: Increments a specific resource's saved variable by a calculated amount, capped at the requirement. Logs the change.

**GetSharedSavedVar**: Maps a shared resource item ID and team ID to the correct saved variable identifier. Validates inputs and logs errors.

**GetTeamStock**: Wrapper that retrieves the current stock count for a shared resource using `GetSharedSavedVar` and `ObjectMgr.GetSavedVariable`.

**GetWarEffortGossipTextId**: Determines the correct gossip text ID based on item, team, and completion status. Uses `WarEffortGossipText` array.

**GetWarEffortGossip**: Searches `WarEffortGossipText` array for a matching item ID. Returns reference or first element on failure. Logs error.

**GetActiveTransportEvent**: Checks for active transition events (Day 1–5) via `GameEventMgr.IsActiveEvent`. Returns the highest active event ID or `EVENT_WAR_EFFORT_TERMINATOR`.

**npc_AQwar_collectorAI (ctor)**: Initializes AI, determines team from faction ID, checks objective status, and sets update timer.

**UpdateAI (npc_AQwar_collectorAI)**: Periodically updates visual GameObjects based on resource progress and checks objective completion.

**HandleWarEffortGameObject**: Manages spawning/despawning of tiered GameObjects representing resources. Logic differs between gathering and transition phases.

**HandleSupplyObjectSpawn**: Spawns or despawns a specific GameObject entry within 50 yards, updating its DB state.

**ObjectiveReached#2 (npc_AQwar_collectorAI)**: Checks if a specific quest's resource requirement is met. Sets internal state.

**ObjectiveReached (npc_AQwar_collectorAI)**: Iterates through creature's quests and checks if any are complete using the overloaded method.

**RemoveQuestGiverFlag**: Removes the quest giver flag from the creature.

**GatheringSupplies**: Returns true if the event is in the collection phase.

**SendWorldStateUpdateToPlayer**: Sends a World State update for the handled resource to a specific player.

**GossipHello_npc_AQwar_collector**: Handles gossip menu, showing completion text or quest menu based on progress.

**QuestComplete_npc_AQwar_collector**: Handles quest turn-in, updating saved variables and checking for objective completion.

**GetWarEffortStockInfo**: Populates a struct with resource details (count, required, proto, var ID). Called by chat commands.

**GetAI_npc_AQwar_collector**: Factory function for `npc_AQwar_collectorAI`.

**npc_resonating_CrystalAI (ctor)**: Initializes AI, sets MC spell based on zone.

**Reset#5 (npc_resonating_CrystalAI)**: Resets timers and combat movement.

**GetMCSpellForZone**: Returns MC spell ID based on creature's zone.

**MoveInLineOfSight#3 (npc_resonating_CrystalAI)**: Detects players in LOS and sets detection flag.

**MoreThanOnePlayerNear**: Counts alive, non-GM players within 55 yards. Returns true if >1.

**AggroAllPlayerNear**: Enters combat with all nearby players.

**UpdateAI#5 (npc_resonating_CrystalAI)**: Manages detection, aggro, evasion, and casting of MC spell.

**GetAI_npc_resonating_Crystal**: Factory function for `npc_resonating_CrystalAI`.

**npc_infantrymanAI (ctor)**: Initializes base infantry AI state.

**MoveInLineOfSight#2 (npc_infantrymanAI)**: Standard aggro logic.

**Reset#3 (npc_infantrymanAI)**: Empty override.

**JustDied (npc_infantrymanAI)**: Triggers Saurfang speech/buff if in combat. Sets respawn position near Saurfang.

**EnterEvadeMode#2 (npc_infantrymanAI)**: Custom evade, stops combat, follows Saurfang or moves home.

**UpdateAI#4 (npc_infantrymanAI)**: Moves to battle position or starts following Saurfang based on event phase.

**CalculateRotatedPositionAboutLeader**: Helper to rotate position vectors.

**MoveToWaveBattlePosition#2 (npc_infantrymanAI)**: Calculates and moves to a rotated battle position.

**FollowSaurfang (npc_infantrymanAI)**: Finds Saurfang and starts following at a calculated offset.

**SetRespawnNearSaurfang**: Calculates respawn position near Saurfang.

**JustReachedHome#2 (npc_infantrymanAI)**: Plays ready emote.

**JustRespawned#2 (npc_infantrymanAI)**: Resets following state.

**npc_ironforge_infantryAI (ctor)**: Inherits from `npc_infantrymanAI`, sets Alliance-specific origin and emote.

**GetAI_npc_ironforge_infantry**: Factory function for `npc_ironforge_infantryAI`.

**npc_orgrimmar_infantryAI (ctor)**: Inherits from `npc_infantrymanAI`, sets Horde-specific origin, rotation, and emote.

**GetAI_npc_orgrimmar_infantry**: Factory function for `npc_orgrimmar_infantryAI`.

**npc_orgrimmar_riflemanAI (ctor)**: Inherits from `npc_orgrimmar_infantryAI`, sets rifleman emote.

**GetAI_npc_orgrimmar_rifleman**: Factory function for `npc_orgrimmar_riflemanAI`.

**npc_priestessAI (ctor)**: Inherits from `npc_infantrymanAI`, sets priestess-specific origin and emote. Uses static index for positioning.

**Aggro (npc_priestessAI)**: Unmounts before aggroing.

**Reset#4 (npc_priestessAI)**: Remounts if following Saurfang.

**MoveToWaveBattlePosition#3 (npc_priestessAI)**: Positions priestess along a line based on index.

**FollowSaurfang#2 (npc_priestessAI)**: Mounts before following.

**GetAI_npc_priestess**: Factory function for `npc_priestessAI`.

**npc_aqwar_saurfangAI (ctor)**: Initializes Saurfang AI, sets faction.

**Reset#2 (npc_aqwar_saurfangAI)**: Randomizes spell timers.

**EnterCombat (npc_aqwar_saurfangAI)**: Unmounts, plays aggro speech, casts buff.

**KilledUnit (npc_aqwar_saurfangAI)**: Randomly plays kill speech.

**EnterEvadeMode (

---

<!-- machine-true, projected from graph.json -->

## Map — world_event_wareffort

*Source:* world_event_wareffort.cpp, world_event_wareffort.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| BuildWarEffortWorldStates | function | ObjectMgr/GetSavedVariable, WorldStates/WriteInitialWorldStatePair | Player.Main/SendInitWorldStates | — |
| AutoCompleteWarEffortProgress | function | Log.Main/Out, World/getConfig#2 | ChatHandler.HardcodedEvents/UpdateWarEffortCollection | — |
| AutoCompleteWarEffortResource | function | Log.Main/Out, ObjectMgr/GetSavedVariable, ObjectMgr/SetSavedVariable | — | — |
| GetSharedSavedVar | function | Log.Main/Out | — | — |
| GetTeamStock | function | ObjectMgr/GetSavedVariable | — | — |
| GetWarEffortGossipTextId | function | — | — | — |
| GetWarEffortGossip | function | Log.Main/Out | — | — |
| GetActiveTransportEvent | function | GameEventMgr.Main/IsActiveEvent | — | — |
| npc_AQwar_collectorAI | ctor | CreatureAI/CreatureAI, Unit.Main/GetFactionTemplateId | — | — |
| UpdateAI | method | — | — | — |
| HandleWarEffortGameObject | method | ObjectMgr/GetSavedVariable | — | — |
| HandleSupplyObjectSpawn | method | GameObject/Despawn, GameObject/isSpawned, GameObject/SaveToDB, GameObject/SetRespawnTime, WorldObject.Object/FindNearestGameObject | — | — |
| ObjectiveReached#2 | method | Log.Main/Out, Object/GetGuidStr, ObjectMgr/GetSavedVariable | — | — |
| ObjectiveReached | method | Object/GetEntry, ObjectMgr/GetCreatureQuestRelationsMapBounds, ObjectMgr/GetQuestTemplate | — | — |
| RemoveQuestGiverFlag | method | Object/HasFlag, WorldObject.Object/RemoveFlag | — | — |
| GatheringSupplies | method | — | — | — |
| SendWorldStateUpdateToPlayer | method | ObjectMgr/GetSavedVariable, Player.Main/SendUpdateWorldState | — | — |
| GossipHello_npc_AQwar_collector | function | Creature.Main/AI, GossipDef/ClearMenus, GossipDef/SendGossipMenu, Object/GetGUID, Object/GetObjectGuid, ObjectGuid/ObjectGuid#5, Player.Main/GetTeamId, Player.Main/PrepareQuestMenu, Unit.Main/HandleEmote, Unit.Main/IsQuestGiver | — | — |
| QuestComplete_npc_AQwar_collector | function | Creature.Main/AI, ObjectMgr/GetSavedVariable, ObjectMgr/SetSavedVariable, Player.Main/GetTeamId, Unit.Main/HandleEmote | — | — |
| GetWarEffortStockInfo | function | ObjectMgr/GetItemPrototype, ObjectMgr/GetSavedVariable | ChatHandler.HardcodedEvents/HandleWarEffortGetResource, ChatHandler.HardcodedEvents/HandleWarEffortInfoCommand, ChatHandler.HardcodedEvents/HandleWarEffortSetResource, ChatHandler.HardcodedEvents/UpdateWarEffortCollection | — |
| GetAI_npc_AQwar_collector | function | — | — | — |
| npc_resonating_CrystalAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#5 | method | CreatureAI/SetCombatMovement | — | — |
| GetMCSpellForZone | method | WorldObject.Object/GetZoneId | — | — |
| MoveInLineOfSight#3 | method | Object/GetTypeId, Object/ToPlayer, Player.Main/IsGameMaster, Unit.Main/IsAlive, WorldObject.Object/IsWithinDistInMap | — | — |
| MoreThanOnePlayerNear | method | Map.Main/GetPlayers, Player.Main/IsGameMaster, Unit.Main/IsAlive, WorldObject.Object/GetMap, WorldObject.Object/IsWithinDistInMap | — | — |
| AggroAllPlayerNear | method | Map.Main/GetPlayers, Player.Main/IsGameMaster, Unit.Main/AddThreat, Unit.Main/IsAlive, Unit.Main/SetInCombatWith, WorldObject.Object/GetMap, WorldObject.Object/IsWithinDistInMap | — | — |
| UpdateAI#5 | method | Creature.Main/SelectAttackingTarget, CreatureAI/DoCastSpellIfCan, ScriptedAI/EnterEvadeMode, Unit.Main/GetCharm, Unit.Main/GetVictim, Unit.Main/IsPolymorphed, Unit.Main/SelectHostileTarget | — | — |
| GetAI_npc_resonating_Crystal | function | — | — | — |
| npc_infantrymanAI | ctor | ScriptedAI/ScriptedAI | — | — |
| MoveInLineOfSight#2 | method | Creature.Main/CanInitiateAttack, CreatureAI/AttackStart, Unit.Main/GetVictim, Unit.Main/IsHostileTo, Unit.Main/IsInAccessablePlaceFor, Unit.Main/IsTargetableBy, WorldObject.Object/IsWithinLOSInMap | — | — |
| Reset#3 | method | — | — | — |
| JustDied | method | CreatureAI/JustDied, ScriptMgr/DoScriptText, SpellCaster/CastSpell#2, Unit.Main/HasAura#2, Unit.Main/IsInCombat, WorldObject.Object/FindNearestCreature | — | — |
| EnterEvadeMode#2 | method | Creature.Main/LoadCreatureAddon, Creature.Main/RemoveAurasAtReset, Creature.Main/SetLootRecipient, Creature.MotionMaster/MoveTargetedHome, Unit.Main/CombatStop, Unit.Main/DeleteThreatList, Unit.Main/GetMotionMaster, Unit.Main/IsAlive | — | — |
| UpdateAI#4 | method | BasicAI/UpdateAI, GameEventMgr.Main/IsActiveEvent | — | — |
| CalculateRotatedPositionAboutLeader | method | Creature.Main/GetHomePosition | — | — |
| MoveToWaveBattlePosition#2 | method | Creature.Main/GetHomePositionO, Creature.Main/SetHomePosition, Creature.MotionMaster/MoveTargetedHome, Map.Main/GetHeight, Unit.Main/GetMotionMaster, Unit.Main/IsAlive, WorldObject.Object/GetMap | — | — |
| FollowSaurfang | method | Creature.Main/GetHomePosition, Creature.MotionMaster/MoveFollow, GridSearchers/GetClosestCreatureWithEntry, Map.Main/GetCreature, Object/GetObjectGuid, Unit.Main/GetMotionMaster, WorldObject.Object/GetMap | — | — |
| SetRespawnNearSaurfang | method | Creature.Main/GetHomePosition, Creature.Main/SetHomePosition, GridSearchers/GetClosestCreatureWithEntry, Map.Main/GetCreature, Map.Main/GetHeight, Object/GetObjectGuid, WorldObject.Object/GetMap, WorldObject.Object/GetNearPoint2D | — | — |
| JustReachedHome#2 | method | CreatureAI/JustReachedHome, Unit.Main/HandleEmoteState | — | — |
| JustRespawned#2 | method | — | — | — |
| npc_ironforge_infantryAI | ctor | — | — | — |
| GetAI_npc_ironforge_infantry | function | — | — | — |
| npc_orgrimmar_infantryAI | ctor | — | — | — |
| GetAI_npc_orgrimmar_infantry | function | — | — | — |
| npc_orgrimmar_riflemanAI | ctor | — | — | — |
| GetAI_npc_orgrimmar_rifleman | function | — | — | — |
| npc_priestessAI | ctor | — | — | — |
| Aggro | method | ScriptedAI/Aggro, Unit.Main/Unmount | — | — |
| Reset#4 | method | Unit.Main/Mount | — | — |
| MoveToWaveBattlePosition#3 | method | Creature.Main/SetHomePosition, Creature.MotionMaster/MoveTargetedHome, Map.Main/GetHeight, Unit.Main/GetMotionMaster, Unit.Main/IsAlive, WorldObject.Object/GetMap | — | — |
| FollowSaurfang#2 | method | Unit.Main/Mount | — | — |
| GetAI_npc_priestess | function | — | — | — |
| npc_aqwar_saurfangAI | ctor | ScriptedAI/ScriptedAI, Unit.Main/SetFactionTemplateId | — | — |
| Reset#2 | method | shared_Util/urand, Unit.Main/Mount | — | — |
| EnterCombat | method | ScriptedAI/Aggro, ScriptMgr/DoScriptText, SpellCaster/CastSpell#2, Unit.Main/Unmount | — | — |
| KilledUnit | method | ScriptMgr/DoScriptText, shared_Util/roll_chance_u | — | — |
| EnterEvadeMode | method | ScriptedAI/EnterEvadeMode, ScriptMgr/DoScriptText | — | — |
| MoveInLineOfSight | method | Creature.Main/CanInitiateAttack, Creature.Main/EnterCombatWithTarget, Unit.Main/IsHostileTo, Unit.Main/IsInAccessablePlaceFor, Unit.Main/IsTargetableBy, WorldObject.Object/IsWithinLOSInMap | — | — |
| MovementInform | method | Creature.Main/SetHomePosition, Unit.Main/SetFacingTo, Unit.Main/Unmount | — | — |
| UpdateAI#3 | method | Creature.Main/SetHomePosition, Creature.MotionMaster/MovePoint, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, GameEventMgr.Main/IsActiveEvent, ScriptMgr/DoScriptText, shared_Util/urand, Unit.Main/GetMotionMaster, Unit.Main/GetVictim, Unit.Main/IsAlive, Unit.Main/Mount, Unit.Main/SelectHostileTarget, Unit.Main/SetWalk, World/SendBroadcastTextToWorld, WorldObject.Object/RemoveFlag, WorldObject.Object/SetFlag | — | — |
| JustRespawned | method | ScriptedAI/JustRespawned | — | — |
| MoveToWaveBattlePosition | method | Creature.Main/SetHomePosition, Creature.MotionMaster/MoveTargetedHome, Unit.Main/GetMotionMaster | — | — |
| JustReachedHome | method | — | — | — |
| GetAI_npc_aqwar_saurfang | function | CreatureEventAI/CreatureEventAI, WorldObject.Object/GetZoneId | — | — |
| npc_aqwar_cenarionhold_attackAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset | method | — | — | — |
| UpdateAI#2 | method | Creature.Main/AI, Creature.Main/SetHomePosition, Creature.MotionMaster/MovePoint, Map.Main/GetHeight, ScriptMgr/DoScriptText, shared_Util/irand, shared_Util/urand, Unit.Main/GetMotionMaster, WorldObject.Object/FindNearestCreature, WorldObject.Object/GetMap, WorldObject.Object/SummonCreature#2 | — | — |
| GetAI_npc_aqwar_cenarionhold_attack | function | — | — | — |
| AddSC_war_effort | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
