# moonglade

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# moonglade.cpp

## Purpose & Responsibilities

`moonglade.cpp` implements the scripted AI and event logic for two major non-player characters (NPCs) in the Moonglade zone: **Keeper Remulos** (`npc_keeper_remulos`) and **Eranikus, Tyrant of the Dream** (`boss_eranikus`). These scripts drive two distinct quests:

1.  **"Nightmare Manifests" (Quest ID 8736):** An escort and boss encounter where Keeper Remulos leads the player to a specific location, summons Eranikus, and engages in a scripted battle involving shadow minions. The event concludes with Eranikus's defeat and redemption.
2.  **"Waking Legends" (Quest ID 8447):** A dialogue-heavy escort quest where Remulos travels to a lake, summons Malfurion Stormrage, and engages in a long conversation sequence before returning to his home position.

The file contains:
*   `npc_keeper_remulosAI`: Inherits from `npc_escortAI` to handle Remulos's movement, dialogue triggers, spell casting during combat, and summoning of Eranikus and shadow minions.
*   `boss_eranikusAI`: Inherits from `ScriptedAI` to handle Eranikus's combat mechanics, health-phase transitions, summoning of Tyrande Whisperwind and Elune Priestesses, and the final redemption cutscene.
*   Helper functions for quest acceptance, spell effects (summoning Eranikus), and script registration.

This unit does **not** interact with any database tables. All data (coordinates, timers, spell IDs, text IDs) is hardcoded within the source file.

## Member-by-Member Behavior

### Keeper Remulos (`npc_keeper_remulosAI`)

#### Initialization & State Management
*   **`npc_keeper_remulosAI` (ctor)**: Initializes the AI object and calls `Reset()` to set default timer values and flags.
*   **`Reset#2`**: Resets all internal timers (`m_uiHealTimer`, `m_uiStarfireTimer`, etc.) and flags (`m_bIsFirstWave`, `m_bEventWLStarted`). It only performs these resets if the escort is not currently active (`!HasEscortState(STATE_ESCORT_ESCORTING)`), preserving state during the event.
*   **`EnterEvadeMode#2`**: If the escort is active and the player is nearby, it forces Remulos to follow the player again. This ensures Remulos doesn't get stuck if the player moves away during the escort phase.

#### Summoning & Movement Callbacks
*   **`JustSummoned#2`**: Handles the initialization of summoned creatures:
    *   **Eranikus**: Stores his GUID, applies a hover aura, sets him to fly, moves him to a spawn location, marks him as spawning (unattackable), and sets a long respawn delay.
    *   **Nightmare Phantasms (Shades)**: Immediately starts attacking Remulos and sets a long respawn delay.
    *   **Malfurion**: Stores his GUID and applies two auras (likely invisibility or passive buffs).
*   **`SummonedMovementInform#2`**: Triggered when a summoned creature reaches a waypoint. For Eranikus, it handles facing Remulos after flight and landing/starting combat after reaching the combat position.
*   **`WaypointReached`**: The core driver for the "Nightmare Manifests" quest. Based on the waypoint ID, it triggers dialogue (`DoScriptText`), changes Remulos's faction (to prevent healing shades), adjusts speed, pauses the escort, and initiates the summoning of Eranikus via `DoCastSpellIfCan`.

#### Combat & Event Logic
*   **`UpdateEscortAI`**: The main update loop for "Nightmare Manifests".
    *   **Outro Phase**: After Eranikus is defeated, it plays outro dialogue and despawns Remulos.
    *   **Shade Summoning**: Manages the summoning of Nightmare Phantasms. The first wave spawns inside the house. Subsequent waves spawn randomly near the player or at predefined locations. Once the maximum number of waves (`MAX_SUMMON_TURNS`) is reached, Eranikus enters combat.
    *   **Combat Spells**: Remulos heals low-health friendly units (`SPELL_HEALING_TOUCH`, `SPELL_REJUVENATION`, `SPELL_REGROWTH`) and casts `SPELL_STARFIRE` on random hostile targets. He also performs melee attacks.
*   **`UpdateAI#2`**: The main update loop for "Waking Legends". It manages a complex sequence of timers for movement and dialogue between Remulos and Malfurion. It moves Remulos through predefined coordinates, triggers dialogue lines, summons Malfurion, and finally completes the quest and respawns Remulos.
*   **`JustDied`**: If Remulos dies during "Nightmare Manifests", it fails the quest for the player and despawns Eranikus. If he dies during "Waking Legends", it fails that quest. It also removes the PvP flag from Remulos.
*   **`DoHandleOutro`**: Triggers the quest completion event for the player group and removes the PvP flag from Remulos.

#### Global Functions
*   **`GetAI_npc_keeper_remulos`**: Factory function to create the `npc_keeper_remulosAI` instance.
*   **`QuestAccept_npc_keeper_remulos`**: Called when a player accepts a quest from Remulos. It sets the global `m_idQuestActive` variable, starts the escort AI, and removes the questgiver flag to prevent re-acceptance.
*   **`EffectDummyCreature_conjure_rift`**: A spell effect handler for `SPELL_CONJURE_RIFT`. When cast, it summons Eranikus at a predefined location.

### Eranikus (`boss_eranikusAI`)

#### Initialization & State Management
*   **`boss_eranikusAI` (ctor)**: Initializes the AI object and calls `Reset()`.
*   **`Reset`**: Sets default combat timers and clears GUIDs for Remulos and Tyrande. Disables combat movement for Eranikus.
*   **`EnterEvadeMode`**: Handles the end of the event.
    *   If Eranikus is below 20% health, he is "redeemed": auras are removed, threat lists cleared, faction changed to friendly, and the redemption cutscene begins.
    *   If he evades otherwise (e.g., player disconnects), he and all summons are forcibly despawned.

#### Summoning & Movement Callbacks
*   **`JustSummoned`**: Initializes Tyrande and Elune Priestesses. It stores their GUIDs and moves them to initial positions using pathfinding.
*   **`SummonedMovementInform`**: Manages the movement of Tyrande and Priestesses through multiple waypoints. At the final healing waypoint, Tyrande prepares to cast the redemption spell, and Priestesses unmount and attack Eranikus (though comments suggest they should only heal).
*   **`MovementInform`**: Triggered when Eranikus reaches the final redemption position. It starts the redemption dialogue sequence.
*   **`DoSummonHealers`**: Summons 7 Elune Priestesses at random points near Tyrande's spawn location.
*   **`DoDespawnSummoned`**: Iterates through the list of summoned Priestesses and despawns them.

#### Combat & Event Logic
*   **`UpdateAI`**: The main update loop for Eranikus.
    *   **Redemption Cutscene**: If `m_uiEventTimer` is active, it progresses through phases of the redemption dialogue and animations, eventually transforming Eranikus and despawning Tyrande.
    *   **Target Selection**: Prioritizes targets Eranikus can melee reach.
    *   **Health Phases**: Triggers events based on Eranikus's health percentage:
        *   **85%**: Summons Tyrande.
        *   **83%**: Summons Elune Priestesses.
        *   **75%, 35%, 31%, 27%, 25%**: Triggers dialogue lines from Eranikus and Tyrande.
        *   **20%**: Triggers the final defeat dialogue and calls `EnterEvadeMode` to start the redemption.
    *   **Combat Spells**: Casts `SPELL_ACID_BREATH`, `SPELL_NOXIOUS_BREATH`, and `SPELL_SHADOWBOLT_VOLLEY` on cooldowns. Also performs melee attacks.
*   **`KilledUnit`**: Plays a kill quote when Eranikus kills a player.

#### Global Functions
*   **`GetAI_boss_eranikus`**: Factory function to create the `boss_eranikusAI` instance.
*   **`AddSC_moonglade`**: Registers both scripts with the server's script manager.

## Cross-Unit Boundaries

*   **`npc_keeper_remulosAI` ↔ `ScriptedEscortAI` / `npc_escortAI`**: Inherits escort functionality. Uses `HasEscortState`, `GetPlayerForEscort`, `SetEscortPaused`, `Start`, and `SetMaxPlayerDistance` to manage the escort state.
*   **`npc_keeper_remulosAI` ↔ `Creature` / `Unit` / `WorldObject`**: Interacts with the game world to move creatures (`MoveFollow`, `MovePoint`, `MonsterMove`), apply/remove auras and flags (`AddAura`, `SetFlag`, `RemoveFlag`), summon creatures (`SummonCreature`), and find targets (`SelectHostileTarget`, `FindLowestHpFriendlyUnit`).
*   **`npc_keeper_remulosAI` ↔ `ScriptMgr`**: Uses `DoScriptText` to play dialogue and emotes.
*   **`npc_keeper_remulosAI` ↔ `Player`**: Fails quests (`FailQuest`) and triggers quest completion events (`GroupEventHappens`).
*   **`boss_eranikusAI` ↔ `ScriptedAI`**: Inherits basic AI functionality.
*   **`boss_eranikusAI` ↔ `Creature` / `Unit` / `WorldObject`**: Similar interactions as Remulos, including movement, summoning, and combat actions.
*   **`boss_eranikusAI` ↔ `ScriptMgr`**: Uses `DoScriptText` for dialogue.
*   **`boss_eranikusAI` ↔ `World`**: Sends a broadcast text to the entire world during the redemption event (`SendBroadcastTextToWorld`).
*   **`boss_eranikusAI` ↔ `npc_keeper_remulosAI`**: During the redemption cutscene, Eranikus's AI calls `DoHandleOutro` on Remulos's AI to complete the quest for the player.
*   **`QuestAccept_npc_keeper_remulos` ↔ `ScriptedEscortAI`**: Starts the escort and sets player distance limits.
*   **`EffectDummyCreature_conjure_rift` ↔ `WorldObject`**: Summons Eranikus.

## Data Model

This unit does not interact with any database tables. All configuration data (coordinates, timers, spell IDs, text IDs, NPC entries) is hardcoded in the source file.

## Notable Implementation Details

*   **Global State Variable**: `m_idQuestActive` is a global variable used to track which quest is currently active for Remulos. This is a potential race condition if multiple players attempt to start the quest simultaneously, although the quest acceptance function removes the questgiver flag to mitigate this.
*   **Hardcoded Coordinates**: All movement paths and summon locations are hardcoded in arrays like `aRemulosLocations`, `aEranikusLocations`, `aTyrandeLocations`, and `aShadowsLocations`. Any changes to the map geometry would require updating these values.
*   **Timer-Based Sequencing**: Both AIs rely heavily on timers (`m_uiHealTimer`, `m_uiShadesummonTimer`, `m_uiTabDialogsTimer`, etc.) to sequence events. This makes the timing rigid and difficult to adjust without changing code.
*   **Health-Phase Transitions**: Eranikus's behavior is driven by health percentages. This is a common pattern in WoW-like games but can be brittle if health values change due to scaling or buffs.
*   **Pathfinding Workarounds**: Comments indicate that pathfinding issues led to hardcoded workarounds for Tyrande's movement. She is spawned further away and moved through specific points to avoid getting stuck.
*   **Redemption Cutscene**: The redemption of Eranikus is a complex sequence involving multiple NPCs (Tyrande, Priestesses, Remulos) and timed events. It relies on precise coordination between the different AI objects.
*   **Spell Effect Handler**: `EffectDummyCreature_conjure_rift` is a custom spell effect handler that summons Eranikus. This allows the summoning to be triggered by a spell cast by Remulos, integrating it into the visual flow of the event.
*   **PVP Flag Manipulation**: Remulos has the `UNIT_FLAG_PVP` flag set during the "Nightmare Manifests" event to allow friendly player spells to target him. This flag is removed upon quest completion or death.

## Member Reference

**npc_keeper_remulosAI** (ctor): Initializes the AI object and calls `Reset()`.

**Reset#2**: Resets all internal timers and flags if the escort is not active.

**EnterEvadeMode#2**: Forces Remulos to follow the player if the escort is active and the player is nearby.

**JustSummoned#2**: Initializes summoned creatures (Eranikus, Shades, Malfurion) with auras, movement, and flags.

**SummonedMovementInform#2**: Handles Eranikus's movement callbacks, setting facing and triggering combat start.

**JustDied**: Fails the active quest for the player and despawns Eranikus if Remulos dies.

**WaypointReached**: Drives the "Nightmare Manifests" quest by triggering dialogue, faction changes, and summoning based on waypoint ID.

**DoHandleOutro**: Triggers quest completion for the player group and removes the PVP flag from Remulos.

**UpdateEscortAI**: Main update loop for "Nightmare Manifests", managing shade summoning, combat spells, and outro sequence.

**UpdateAI#2**: Main update loop for "Waking Legends", managing movement and dialogue sequences with Malfurion.

**GetAI_npc_keeper_remulos**: Factory function to create the `npc_keeper_remulosAI` instance.

**QuestAccept_npc_keeper_remulos**: Handles quest acceptance, starts the escort, and sets the global quest ID.

**EffectDummyCreature_conjure_rift**: Spell effect handler that summons Eranikus.

**boss_eranikusAI** (ctor): Initializes the AI object and calls `Reset()`.

**Reset**: Sets default combat timers and clears GUIDs. Disables combat movement.

**EnterEvadeMode**: Handles the end of the event, either starting the redemption cutscene or despawning all summons.

**KilledUnit**: Plays a kill quote when Eranikus kills a player.

**DoSummonHealers**: Summons 7 Elune Priestesses at random points near Tyrande.

**JustSummoned**: Initializes Tyrande and Priestesses with movement and faction settings.

**DoDespawnSummoned**: Despawns all summoned Elune Priestesses.

**SummonedMovementInform**: Manages the movement of Tyrande and Priestesses through waypoints, triggering healing and attack behaviors.

**MovementInform**: Triggered when Eranikus reaches the final redemption position, starting the dialogue sequence.

**UpdateAI**: Main update loop for Eranikus, managing the redemption cutscene, health-phase transitions, and combat spells.

**GetAI_boss_eranikus**: Factory function to create the `boss_eranikusAI` instance.

**AddSC_moonglade**: Registers both scripts with the server's script manager.

---

<!-- machine-true, projected from graph.json -->

## Map — moonglade

*Source:* moonglade.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| npc_keeper_remulosAI | ctor | ScriptedEscortAI/npc_escortAI | — | — |
| Reset#2 | method | ObjectGuid/Clear, ScriptedEscortAI/HasEscortState | — | — |
| EnterEvadeMode#2 | method | Creature.MotionMaster/MoveFollow, ScriptedEscortAI/EnterEvadeMode, ScriptedEscortAI/GetPlayerForEscort, ScriptedEscortAI/HasEscortState, Unit.Main/GetMotionMaster, WorldObject.Object/IsWithinDistInMap | — | — |
| JustSummoned#2 | method | Creature.Main/AI, Creature.Main/SetRespawnDelay, CreatureAI/AttackStart, Object/GetEntry, Object/GetObjectGuid, Unit.Main/AddAura, Unit.Main/MonsterMove, Unit.Main/SetFly, WorldObject.Object/SetFlag | — | — |
| SummonedMovementInform#2 | method | Object/GetEntry, ScriptMgr/DoScriptText, Unit.Main/HandleEmote, Unit.Main/SetFacingToObject, Unit.Main/SetFly | — | — |
| JustDied | method | Creature.Main/AI, CreatureAI/EnterEvadeMode, Map.Main/GetCreature, Player.Main/FailQuest, ScriptedEscortAI/GetPlayerForEscort, WorldObject.Object/GetMap, WorldObject.Object/RemoveFlag | — | — |
| WaypointReached | method | Creature.MotionMaster/MovePoint, CreatureAI/DoCastSpellIfCan, Map.Main/GetCreature, ScriptedEscortAI/GetPlayerForEscort, ScriptedEscortAI/SetEscortPaused, ScriptMgr/DoScriptText, Unit.Main/GetMotionMaster, Unit.Main/SetFacingToObject, Unit.Main/SetFactionTemplateId, Unit.Main/SetSpeedRate, Unit.Main/SetWalk, WorldObject.Object/GetMap, WorldObject.Object/SetFlag | — | — |
| DoHandleOutro | method | Player.Main/GroupEventHappens, ScriptedEscortAI/GetPlayerForEscort, WorldObject.Object/RemoveFlag | — | — |
| UpdateEscortAI | method | Creature.Main/AI, Creature.Main/ForcedDespawn, Creature.Main/GetHomePosition#2, Creature.Main/SelectAttackingTarget, Creature.MotionMaster/MovePoint, CreatureAI/AttackStart, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, Map.Main/GetCreature, ScriptedEscortAI/GetPlayerForEscort, ScriptedEscortAI/SetEscortPaused, ScriptMgr/DoScriptText, shared_Util/urand, Unit.Main/FindLowestHpFriendlyUnit, Unit.Main/GetMotionMaster, Unit.Main/GetVictim, Unit.Main/RemoveAurasDueToSpell, Unit.Main/SelectHostileTarget, Unit.Main/SetWalk, WorldObject.Object/GetMap, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/GetRandomPoint, WorldObject.Object/RemoveFlag, WorldObject.Object/SummonCreature#2 | — | — |
| UpdateAI#2 | method | Creature.Main/ForcedDespawn, Creature.Main/GetHomePosition#2, Creature.Main/Respawn, Creature.MotionMaster/MovePoint, CreatureAI/DoCast, Map.Main/GetCreature, Player.Main/GetQuestStatus, Player.Main/GroupEventHappens, ScriptedEscortAI/GetPlayerForEscort, ScriptedEscortAI/SetEscortPaused, ScriptedEscortAI/UpdateAI, ScriptMgr/DoScriptText, Unit.Main/GetMotionMaster, WorldObject.Object/GetMap, WorldObject.Object/SetFlag, WorldObject.Object/SummonCreature#2 | — | — |
| GetAI_npc_keeper_remulos | function | — | — | — |
| QuestAccept_npc_keeper_remulos | function | Creature.Main/AI, Object/GetGUID, QuestDef/GetQuestId, ScriptedEscortAI/SetMaxPlayerDistance, ScriptedEscortAI/Start, WorldObject.Object/RemoveFlag | — | — |
| EffectDummyCreature_conjure_rift | function | WorldObject.Object/SummonCreature#2 | — | — |
| boss_eranikusAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset | method | CreatureAI/SetCombatMovement, ObjectGuid/Clear | — | — |
| EnterEvadeMode | method | Creature.Main/AI, Creature.Main/ForcedDespawn, Creature.Main/LoadCreatureAddon, Creature.Main/SetLootRecipient, CreatureAI/EnterEvadeMode, GridSearchers/GetClosestCreatureWithEntry, Map.Main/GetCreature, Object/GetObjectGuid, Unit.Main/CombatStop, Unit.Main/DeleteThreatList, Unit.Main/GetHealthPercent, Unit.Main/RemoveAllAuras, Unit.Main/SetFactionTemplateId, WorldObject.Object/GetMap, WorldObject.Object/SetFlag | — | — |
| KilledUnit | method | Object/GetTypeId, ScriptMgr/DoScriptText | — | — |
| DoSummonHealers | method | WorldObject.Object/GetRandomPoint, WorldObject.Object/SummonCreature#2 | — | — |
| JustSummoned | method | Creature.MotionMaster/MovePoint, Object/GetEntry, Object/GetObjectGuid, Unit.Main/GetMotionMaster, Unit.Main/SetFactionTemplateId, WorldObject.Object/GetRandomPoint | — | — |
| DoDespawnSummoned | method | Creature.Main/ForcedDespawn, Map.Main/GetCreature, ObjectGuid/ObjectGuid#5, WorldObject.Object/GetMap | — | — |
| SummonedMovementInform | method | Creature.Main/AI, Creature.MotionMaster/MovePoint, CreatureAI/AttackStart, Object/GetEntry, ScriptMgr/DoScriptText, SpellCaster/CastSpell#2, Unit.Main/GetHealthPercent, Unit.Main/GetMotionMaster, Unit.Main/Unmount, WorldObject.Object/GetRandomPoint | — | — |
| MovementInform | method | ScriptMgr/DoScriptText | — | — |
| UpdateAI | method | Creature.Main/AI, Creature.Main/ForcedDespawn, Creature.MotionMaster/MovePoint, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, CreatureAI/EnterEvadeMode, Map.Main/GetCreature, ScriptMgr/DoScriptText, SpellCaster/InterruptNonMeleeSpells, ThreatManager/getThreatList, ThreatManager/modifyThreatPercent#2, Unit.Main/CanReachWithMeleeAutoAttack, Unit.Main/GetHealthPercent, Unit.Main/GetMotionMaster, Unit.Main/GetThreatManager, Unit.Main/GetVictim, Unit.Main/HandleEmote, Unit.Main/SelectHostileTarget, Unit.Main/SetStandState, World/SendBroadcastTextToWorld, WorldObject.Object/GetMap, WorldObject.Object/SummonCreature#2 | — | — |
| GetAI_boss_eranikus | function | — | — | — |
| AddSC_moonglade | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
