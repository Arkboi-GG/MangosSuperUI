# dreadsteed_ritual

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# dreadsteed_ritual

**Purpose & Responsibilities**

`dreadsteed_ritual.cpp` implements the scripted events, game object (GO) behaviors, and creature artificial intelligence (AI) for the "Dreadsteed Ritual" encounter within the Dire Maul instance. This script manages a complex, multi-phase ritual sequence triggered by a player interaction, involving the activation of ritual nodes (Wheel, Candle, Bell), the spawning of demon waves, and the eventual summoning of two bosses: Lord Hel-Nurath and the Xorothian Dreadsteed.

The unit is divided into four primary logical components:
1.  **Ritual Controller (`go_pedestal_of_immol_tharAI`):** The central state machine attached to the Pedestal of Immol-Thar. It orchestrates the entire event timeline, managing phases, timers, node states, and creature spawns.
2.  **Ritual Nodes (`go_ritual_nodeAI`):** A shared AI for the Wheel, Candle, and Bell game objects. These nodes provide periodic buffs to the initiating player and signal their activation status to the controller.
3.  **Boss AIs (`boss_lordHelNurathAI`, `boss_xorothianDreadsteedAI`):** Standard combat AI implementations for the two final bosses, handling their specific spell rotations and melee attacks.
4.  **Event Handlers:** Global functions that bridge database-defined events (`event_dreadsteed_ritual_start`, etc.) and player interactions (`GOHello`) to the internal state machine.

**Data Model**

This unit does not directly query or modify database tables via SQL statements. It interacts with the instance data system (`instance_dire_maul`) to store and retrieve runtime state (such as the initiating player's GUID and specific GO GUIDs) in memory. No database schema is referenced or modified by this code.

**Cross-Unit Boundaries & Collaboration**

*   **`instance_dire_maul`:** The ritual controller relies heavily on the `instance_dire_maul` script (defined in `dire_maul.h`) to persist state across the instance. Specifically, it uses `SetData64` to store the initiating player's GUID (`DATA_DREADSTEED_RITUAL_PLAYER`) and retrieves GO GUIDs stored by the instance manager.
*   **`npc_j_eevee`:** During the initial phase, the controller summons J'eevee (`NPC_J_EEVEE`). It casts to `npc_j_eevee_dreadsteedAI` (defined in `npc_j_eevee.h`) to trigger her specific dialogue (`ShoutFreedom`) and link her to the player (`SetPlayerGuid`).
*   **`WorldObject` / `Map` / `GameObject` / `Creature`:** The controller extensively uses these core engine classes to locate nearby objects (`FindNearestGameObject`, `GetCreatureListWithEntryInGrid`), summon entities (`SummonCreature`, `SummonGameObject`), and manipulate their states (`SetGoState`, `Refresh`, `Despawn`).
*   **`ScriptMgr`:** Used to broadcast text emotes (`DoScriptText`) during phase transitions and creature despawns.
*   **`shared_Util`:** Uses `urand` for randomizing timer intervals and spawn points.
*   **`Log.Main`:** Logs errors if critical ritual components (Runes, Nodes) cannot be located during initialization.

**Notable Implementation Details**

*   **State Machine Complexity:** The `go_pedestal_of_immol_tharAI` uses a nested `switch` statement on `eventPhase` and `gobjStep` to manage the ritual progression. This creates a rigid, step-by-step execution flow.
*   **Hardcoded GUIDs:** The ritual relies on hardcoded high-guids for runes (`GOBJ_GUID_RUNE_1` through `9`) and specific entry IDs for nodes. If the database spawns these objects with different GUIDs, the ritual will fail to initialize correctly, as seen in `GenerateGlyphAndNodeGuids`.
*   **Node Synchronization:** The ritual requires three nodes (Wheel, Candle, Bell) to be active. The `BreakNode` function randomly disables nodes over time, forcing players to reactivate them. The `NodeUpped` callback updates the internal state when a player interacts with a node. The ritual only proceeds to the next major phase if all three nodes are up (`nbOkNodes == 3`).
*   **Wave Spawning Logic:** The `WaveSpawn` function contains a large `switch` statement defining specific waves of demons (Imps and Guards) with varying counts and intervals. This logic is duplicated slightly between `SummonImp`/`SummonGuard` helpers and the inline spawning in `WaveSpawn` step 0.
*   **Boss AI Simplicity:** The boss AIs are straightforward timer-based casters. They do not have complex movement or phase changes beyond standard combat behavior. Lord Hel-Nurath targets randomly for Sleep, while the Dreadsteed charges random targets.
*   **Memory Management:** The script uses `new` to allocate AI objects in the `GetAI...` functions, relying on the engine's script manager to handle deletion.

## Member Reference

**go_pedestal_of_immol_tharAI**
Constructor for the ritual controller AI. Initializes the instance data pointer from the game object and calls `reset()` to initialize timers and state variables.

**reset#3**
Resets the ritual state to its initial condition. Clears GUIDs, resets timers (`gobjTimer`, `waveTimer`, `nodeTimer`), and sets `eventPhase` to 0. Uses `urand` to set an initial random delay for the first node break.

**GenerateGlyphAndNodeGuids**
Locates and stores the GUIDs for the 9 ritual runes and the 3 main nodes (Wheel, Candle, Bell) and the ritual circle. It constructs expected GUIDs for runes based on hardcoded constants and searches for nodes within a 30-unit radius. Logs errors if any component is missing.

**EventStart**
Initiates the ritual sequence. Validates that the event hasn't started, stores the player's GUID in the instance data, generates necessary GUIDs, sets `eventPhase` to 1, and summons J'eevee. It then casts J'eevee's AI to trigger her introductory actions.

**EventSecondPartStart**
Transitions the ritual from Phase 3 (waiting for item use) to Phase 4 (final boss summoning). Resets the ritual circle state, respawns the Dreadsteed Portal, and initializes the wave timer for the final phase.

**PhaseTwoEndedSuccess**
Handles the successful completion of the demon wave phase. Activates the ritual circle, despawns all summoned fel fire game objects, and kills all remaining Imps and Guards in the area, playing a death sound for each. Increments the event phase.

**EventEndedFail**
Handles failure of the ritual (e.g., too many nodes down). Resets the event phase to 0, despawns fel fires, resets runes and nodes to their default inactive state, resets the ritual circle, kills all remaining demons, and calls `reset()` to clear internal timers.

**gobjNextStep**
The core step-by-step logic for the ritual's visual and mechanical progression. It switches on `eventPhase` and `gobjStep` to perform actions like spawning nodes (Bell, Wheel, Candle), spawning fel fires and runes, checking if all nodes are up to proceed, or despawning the portal in the final phase.

**SummonImp**
Helper function to summon a single Xorothian Imp at a random pre-calculated spawn point. Sets the imp's home position, facing, and movement path to move towards the pedestal.

**SummonGuard**
Helper function to summon a single Dread Guard at a random pre-calculated spawn point. Similar to `SummonImp`, it configures the guard's initial position and movement.

**WaveSpawn**
Manages the spawning of demon waves during Phases 2 and 4. In Phase 2, it follows a complex schedule defined by `waveStep`, spawning various combinations of Imps and Guards with specific delays. In Phase 4, it summons the Xorothian Dreadsteed and then Lord Hel-Nurath.

**BreakNode**
Periodically disables one of the active ritual nodes. It checks how many nodes are currently up; if fewer than 2 are up, it triggers a failure. Otherwise, it randomly selects an active node to disable, adjusting the timer for the next break based on the number of breaks already occurred (`nodeNb`).

**UpdateAI#3**
The main update loop for the ritual controller. It decrements timers (`gobjTimer`, `waveTimer`, `nodeTimer`) and triggers the corresponding step functions (`gobjNextStep`, `WaveSpawn`, `BreakNode`) when timers expire, depending on the current `eventPhase`.

**NodeUpped**
Callback invoked when a player activates a ritual node. It iterates through the internal `nodes` array and marks the corresponding node as `up = true` if the GUID matches.

**GetAIgo_pedestal_of_immol_thar**
Factory function that returns a new instance of `go_pedestal_of_immol_tharAI` for the specified game object.

**ProcessEventId_event_dreadsteed_ritual_start**
Global event handler for the start of the ritual. It casts the target game object's AI to `go_pedestal_of_immol_tharAI` and calls `EventStart` with the source object's GUID (the player).

**GOHello_go_ritual_node**
Global handler for player interaction with ritual nodes. It sets the node's state to active and flags it as in use. It then finds the nearest pedestal and calls `NodeUpped` on its AI to notify the controller.

**go_ritual_nodeAI**
Constructor for the shared ritual node AI. Initializes the timer, refresh interval, and spell ID passed from the factory functions.

**UpdateAI#4**
The update loop for ritual nodes. If the node is active and spawned, it periodically casts its associated aura spell on the initiating player (retrieved from instance data). The timer resets after each cast.

**GetAIgo_ritual_wheel**
Factory function returning a `go_ritual_nodeAI` configured for the Wheel node with `SPELL_WHEEL_AURA`.

**GetAIgo_ritual_candle**
Factory function returning a `go_ritual_nodeAI` configured for the Candle node with `SPELL_CANDLE_AURA`.

**GetAIgo_ritual_bell**
Factory function returning a `go_ritual_nodeAI` configured for the Bell node with `SPELL_BELL_AURA`.

**ProcessEventId_event_dreadsteed_ritual_second_part**
Global event handler for the second part of the ritual. It finds the nearest pedestal and calls `EventSecondPartStart` on its AI.

**boss_lordHelNurathAI**
Constructor for Lord Hel-Nurath's AI. Calls `Reset()` to initialize spell timers.

**Reset**
Initializes the spell timers for Lord Hel-Nurath: Shadow Word, Veil of Shadow, Sleep, and Knock Away.

**UpdateAI**
Lord Hel-Nurath's combat loop. It casts Shadow Word on the victim, Veil of Shadow on the victim, Sleep on a random target, and Knock Away on the victim, each with randomized timers. It also performs melee attacks.

**GetAI_boss_lord_hel_nurath**
Factory function returning a new `boss_lordHelNurathAI` instance.

**boss_xorothianDreadsteedAI**
Constructor for the Xorothian Dreadsteed's AI. Calls `Reset()` to initialize spell timers.

**Reset#2**
Initializes the spell timers for the Dreadsteed: Berserker Charge and Flame Buffet.

**UpdateAI#2**
The Dreadsteed's combat loop. It casts Berserker Charge on a random target and Flame Buffet on the victim, with randomized timers. It also performs melee attacks.

**JustDied**
Triggered when the Dreadsteed dies. It casts `SPELL_SUMMON_DREADSTEED_SPIRIT` on itself, presumably to spawn a spirit version of the mount for the player.

**GetAI_boss_xorothian_dreadsteed**
Factory function returning a new `boss_xorothianDreadsteedAI` instance.

**AddSC_dreadsteed_ritual**
Registers all scripts defined in this file with the `ScriptMgr`. This includes the event handlers, the ritual node AIs, the boss AIs, and the pedestal AI. It links the `GOHello` function to the ritual nodes and assigns the appropriate AI factories.

---

<!-- machine-true, projected from graph.json -->

## Map — dreadsteed_ritual

*Source:* dreadsteed_ritual.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| go_pedestal_of_immol_tharAI | ctor | GameObjectAI/GameObjectAI, WorldObject.Object/GetInstanceData | — | — |
| reset#3 | method | shared_Util/urand | — | — |
| GenerateGlyphAndNodeGuids | method | Log.Main/Out, Map.Main/GetGameObject, Object/GetGUID, ObjectGuid/ObjectGuid#3, ObjectGuid/ObjectGuid#5, WorldObject.Object/FindNearestGameObject, WorldObject.Object/GetMap | — | — |
| EventStart | method | Creature.Main/AI, instance_dire_maul/SetData64, npc_j_eevee/SetPlayerGuid, npc_j_eevee/ShoutFreedom, WorldObject.Object/SummonCreature#2 | — | — |
| EventSecondPartStart | method | GameObject/Refresh, GameObject/SetGoState, GameObject/SetLootState, GameObject/SetRespawnTime, GameObject/SetSpawnedByDefault, Map.Main/GetGameObject, ObjectGuid/ObjectGuid#5, WorldObject.Object/FindNearestGameObject, WorldObject.Object/GetMap | — | — |
| PhaseTwoEndedSuccess | method | Creature.Main/DisappearAndDie, GameObject/Despawn, GameObject/SetGoState, Map.Main/GetGameObject, ObjectGuid/ObjectGuid#5, ScriptMgr/DoScriptText, Unit.Main/IsAlive, WorldObject.Object/GetCreatureListWithEntryInGrid, WorldObject.Object/GetMap | — | — |
| EventEndedFail | method | Creature.Main/DisappearAndDie, GameObject/Despawn, GameObject/Refresh, GameObject/SetGoState, GameObject/SetSpawnedByDefault, Map.Main/GetGameObject, ObjectGuid/ObjectGuid#5, ScriptMgr/DoScriptText, Unit.Main/IsAlive, WorldObject.Object/GetCreatureListWithEntryInGrid, WorldObject.Object/GetMap | — | — |
| gobjNextStep | method | GameObject/Refresh, GameObject/SendGameObjectCustomAnim, GameObject/SetGoState, GameObject/SetSpawnedByDefault, Map.Main/GetGameObject, Object/GetGUID, ObjectGuid/ObjectGuid#5, WorldObject.Object/FindNearestGameObject, WorldObject.Object/GetMap, WorldObject.Object/GetPosition#2, WorldObject.Object/SetFlag, WorldObject.Object/SummonGameObject | — | — |
| SummonImp | method | Creature.Main/SetHomePosition, Creature.MotionMaster/Initialize, Creature.MotionMaster/MovePoint, MotionMaster/Clear, shared_Util/urand, Unit.Main/GetMotionMaster, Unit.Main/SetFacingToObject, Unit.Main/SetWalk, WorldObject.Object/GetPosition#2, WorldObject.Object/SummonCreature#2 | — | — |
| SummonGuard | method | Creature.Main/SetHomePosition, Creature.MotionMaster/Initialize, Creature.MotionMaster/MovePoint, MotionMaster/Clear, shared_Util/urand, Unit.Main/GetMotionMaster, Unit.Main/SetFacingToObject, WorldObject.Object/GetPosition#2, WorldObject.Object/SummonCreature#2 | — | — |
| WaveSpawn | method | Creature.Main/SetHomePosition, Creature.MotionMaster/Initialize, Creature.MotionMaster/MovePoint, MotionMaster/Clear, ScriptMgr/DoScriptText, Unit.Main/GetMotionMaster, Unit.Main/SetFacingToObject, WorldObject.Object/GetPosition#2, WorldObject.Object/SummonCreature#2 | — | — |
| BreakNode | method | GameObject/SetGoState, Map.Main/GetGameObject, ObjectGuid/ObjectGuid#5, shared_Util/urand, WorldObject.Object/GetMap, WorldObject.Object/RemoveFlag | — | — |
| UpdateAI#3 | method | — | — | — |
| NodeUpped | method | Object/GetGUID | — | — |
| GetAIgo_pedestal_of_immol_thar | function | — | — | — |
| ProcessEventId_event_dreadsteed_ritual_start | function | GameObject/AI, Object/GetGUID | — | — |
| GOHello_go_ritual_node | function | GameObject/AI, GameObject/SetGoState, Log.Main/Out, WorldObject.Object/FindNearestGameObject, WorldObject.Object/SetFlag | — | — |
| go_ritual_nodeAI | ctor | GameObjectAI/GameObjectAI | — | — |
| UpdateAI#4 | method | GameObject/GetGoState, GameObject/isSpawned, instance_dire_maul/GetData64, Map.Main/GetPlayer, ObjectGuid/ObjectGuid#5, SpellCaster/CastSpell#2, WorldObject.Object/GetInstanceData, ZoneScript/GetGameObject, ZoneScript/GetMap#2 | — | — |
| GetAIgo_ritual_wheel | function | — | — | — |
| GetAIgo_ritual_candle | function | — | — | — |
| GetAIgo_ritual_bell | function | — | — | — |
| ProcessEventId_event_dreadsteed_ritual_second_part | function | GameObject/AI, WorldObject.Object/FindNearestGameObject | — | — |
| boss_lordHelNurathAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset | method | — | — | — |
| UpdateAI | method | Creature.Main/SelectAttackingTarget, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, shared_Util/urand, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_boss_lord_hel_nurath | function | — | — | — |
| boss_xorothianDreadsteedAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#2 | method | — | — | — |
| UpdateAI#2 | method | Creature.Main/SelectAttackingTarget, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, shared_Util/urand, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| JustDied | method | SpellCaster/CastSpell#2 | — | — |
| GetAI_boss_xorothian_dreadsteed | function | — | — | — |
| AddSC_dreadsteed_ritual | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
