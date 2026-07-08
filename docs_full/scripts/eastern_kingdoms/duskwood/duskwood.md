# duskwood

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# duskwood

**Purpose & Responsibilities**

This unit implements scripted behaviors for non-player characters (NPCs), area triggers, and quest events specific to the **Duskwood** zone. It contains two primary, loosely coupled subsystems:

1.  **The Nightmare Corruption Event:** Triggered by the `at_twilight_grove` area trigger, this system summons the `npc_twilight_corrupter` boss for players attempting Quest 8735. The boss AI manages complex threat manipulation, periodic spell casting, and death-based emotes for players on its threat list.
2.  **The Stitches Escort Event:** A multi-NPC escort sequence involving `npc_sirra_vonindi`, `npc_stitches` (the abomination), and various "Watcher" NPCs (`npc_watcher_blomberg`, `npc_watcher_selkin`, etc.). This subsystem handles the summoning of Stitches, the coordination of Watcher reinforcements at specific waypoints, dialogue synchronization with a Town Crier, and cleanup upon Stitches' death or failure.

Additionally, it implements a minor suicide mechanic for `npc_commander_felstrom`.

**No database tables are accessed by this unit.** All logic is driven by in-memory game objects, timers, and hardcoded coordinates/constants.

---

## Subsystem 1: Nightmare Corruption

### `Handle_NightmareCorruption`
This free function orchestrates the initial encounter setup. It checks if the player is alive and actively pursuing Quest 8735 (`QUEST_NIGHTMARE_CORRUPTION`). If valid, it searches for an existing `NPC_TWILIGHT_CORRUPTER` within 350 yards. If none exists, it summons one at fixed coordinates (-10335.9, -489.051, 50.6233). Upon successful summoning (or if one was already found), it whispers a personalized message to the player. If the summon fails, it logs an error via `Log.Main/Out`.

### `AreaTrigger_at_twilight_grove`
A script hook registered to the `at_twilight_grove` area trigger. It simply delegates to `Handle_NightmareCorruption` and returns `false` (indicating the trigger should not block movement or repeat immediately in a way that interrupts flow, depending on engine interpretation of the return value for area triggers).

### `npc_twilight_corrupterAI`
The AI for the Twilight Corrupter boss. It inherits from `ScriptedAI`.

*   **State Management:** Tracks timers for `SPELL_SOUL_CORRUPTION` (6–18s initial, 20–30s subsequent), `SPELL_CREATURE_OF_NIGHTMARE` (10–20s initial, 35–40s subsequent), and a 1-second check timer. It maintains a boolean `bEngaged` to prevent duplicate aggro texts and a GUID array `GUIDs` to track players on the threat list for death emotes.
*   **`Reset`:** Initializes all timers with random offsets and clears the tracked player GUIDs.
*   **`Aggro`:** Plays text ID 11269 once when combat begins.
*   **`FillPlayerList`:** Iterates through the creature's threat list (`ThreatManager/getThreatList`) and populates the `GUIDs` array with up to 40 unique player GUIDs. This is used to identify who dies while engaged with the boss.
*   **`UpdateAI`:**
    *   **Soul Corruption:** Casts `SPELL_SOUL_CORRUPTION` on self when its timer expires.
    *   **Creature of Nightmare Logic:** This spell targets a random player. The AI tracks the target's GUID (`CoNPlayerGuid`) and their current threat value (`CoNPlayerAggro`). In subsequent ticks, if the target no longer has the `SPELL_CREATURE_OF_NIGHTMARE` aura (meaning the effect ended or was dispelled), the AI restores their threat to the stored level and clears the tracking variables. This prevents the target from being permanently deprioritized if the spell wears off early.
    *   **Death Emotes:** Every second, it iterates the `GUIDs` array. If a tracked player is found dead on the map, it plays a specific emote ("Twilight Corrupter squeezes...") and casts `SPELL_SWELL_OF_SOULS` on itself. The player's GUID is then cleared from the list to prevent repeated emotes.
    *   **Melee:** Standard melee attack handling.

### `GetAI_npc_twilight_corrupter`
Factory function returning a new `npc_twilight_corrupterAI` instance.

---

## Subsystem 2: Stitches Escort Event

This event involves multiple NPCs coordinating to escort `npc_stitches` through Duskwood.

### `npc_watcher_blombergAI`
Supports the Stitches event by initiating the call for help.

*   **`Reset`:** Sets the creature to run (`SetWalk(false)`).
*   **`ResetCreature`:** Resets internal state (`m_bIsEngaged`, `m_uiSayTimer`).
*   **`UpdateAI`:** If not engaged, waits for a 3-second timer. Once expired, it yells for help, sets `m_bIsEngaged` to true, and finds nearby `NPC_WATCHER_DODDS` and `NPC_WATCHER_PAIGE`. It stores their GUIDs and commands them to move to specific coordinates using `MovePoint`. This effectively pulls these two NPCs into the escort group dynamically.

### `npc_watcher_selkinAI`
A simple escort follower. Inherits from `npc_escortAI`.

*   **`Reset`:** Sets the creature to run.
*   **`WaypointReached`:** Empty override. Relies entirely on the base `npc_escortAI` behavior to follow the leader.

### `npc_commander_felstromAI`
A standalone boss with a suicide mechanic.

*   **`Reset`:** Initializes a 1.5-second suicide check timer and updates dynamic flags.
*   **`JustDied`:** If killed by Entry 771 (likely a specific quest-related killer), it clears the loot recipient, preventing loot drops.
*   **`UpdateAI`:** Checks health every 1.5 seconds. If health drops below 10% and suicide hasn't occurred, it casts Spell 3488 (likely a suicide spell) on itself. Otherwise, it performs standard melee attacks.

### `npc_sirra_vonindiAI`
The quest giver/trigger for the Stitches event.

*   **`ResetCreature`:** Sets a 3-second delay before Stitches can be summoned (`m_uiTimer`).
*   **`StitchesDied`:** Called when Stitches dies. Clears the Stitches GUID and sets a 10-minute cooldown (`m_uiTimer`) before a new Stitches can be summoned.
*   **`UpdateAI`:** Manages the summoning cooldown. If Stitches is not present and summoning is allowed, it waits for the timer to expire before setting `m_bCanSummon` to true.
*   **`SummonStitches`:** Checks if Stitches is already alive or if summoning is blocked. If valid, it summons `NPC_STITCHES` at fixed coordinates, scales him to 200%, and calls `LaunchStitches`.
*   **`LaunchStitches`:** Finds the nearest `NPC_TOWN_CRIER` and passes its GUID to the Stitches AI. It also passes Sirra's own GUID to Stitches. Finally, it starts the escort path on the Stitches AI.

### `QuestRewarded_npc_sirra_vonindi`
Hooked to Quest 401 (`QUEST_WAIT_FOR_SIRRA_TO_FINISH`). When a player turns in this quest, it attempts to summon Stitches via `npc_sirra_vonindiAI::SummonStitches`. It returns `true` (success) if the summon failed (preventing further processing?) or `false`? *Correction*: The code returns `!pCreatureAI->SummonStitches()`. If `SummonStitches` returns `true` (success), the hook returns `false`. If `SummonStitches` returns `false` (failure), the hook returns `true`. This inverted logic likely signals to the quest system whether the default reward behavior should proceed or be suppressed, though typically returning `true` from a quest rewarded hook indicates the script handled the reward. Given the name, it likely ensures Stitches is spawned upon quest completion.

### `npc_stitchesAI`
The main escortee. Inherits from `npc_escortAI`.

*   **State:** Tracks GUIDs for summoned Watchmen, the Town Crier, Dodds, Paige, and Sirra. Manages timers for `SPELL_AURA_OF_ROT` and an emergency launch check.
*   **`Reset`:** Initializes timers.
*   **`JustDied`:**
    *   Yells via the Town Crier (Text 89).
    *   Despawns all summoned Watchmen via `DespawnWatcher`.
    *   Orders Dodds and Paige to return home.
    *   Notifies Sirra Von'Indi's AI that Stitches died, triggering her 10-minute cooldown.
*   **`KilledUnit`:** If Stitches kills `NPC_WATCHER_SELKIN`, the Town Crier yells Text 91.
*   **`DespawnWatcher`:** Iterates the `m_lWatchman` list and forces any alive watchmen to disappear and die.
*   **`SummonWatchman`:** Helper to spawn a watcher from the static `Watchman` array at predefined coordinates.
*   **`AddToFormation`:** Joins a summoned watcher to a leader's formation group, ensuring they move and aggro together.
*   **`JustSummoned`:** Handles post-summon setup for specific watchers:
    *   **Hutchins/Bloemberg:** Moves them to specific points, sets home/combat positions. For Bloemberg, it extracts the GUIDs of Dodds and Paige from Bloemberg's AI to track them for cleanup later.
    *   **Selkin:** Starts his escort AI.
*   **`SummonedCreatureJustDied`:** Removes the dead watcher from the tracking list.
*   **`WaypointReached`:** The core event driver. At specific waypoints:
    *   **10, 29, 61:** Stitches yells Text 277.
    *   **30:** Town Crier yells Text 89.
    *   **31:** Summons Hutchins and Bloemberg.
    *   **34:** Finds Cutford. If far, moves him to a staging point; if close, attacks him. Adds him to the watchman list.
    *   **35:** Town Crier yells Text 90.
    *   **39:** Summons Selkin, Gelwin, Merant, and Thayer. Forms them into a group led by Selkin.
    *   **61:** Summons Sarys and Corwin. Town Crier yells Text 92.
    *   **65:** Stops the escort, sets home position to current location, and enters random movement (event complete/failure state).
*   **`Aggro`:** Randomly yells Text 278 (25% chance) when aggroing.
*   **`UpdateEscortAI`:**
    *   **Emergency Launch:** If the escort hasn't started after 10 seconds, it forces a start and logs an error.
    *   **Combat:** Casts `SPELL_AURA_OF_ROT` on the victim every 3 seconds. Performs melee attacks.

### `GetAI_*` Functions
Factory functions for `npc_watcher_blomberg`, `npc_watcher_selkin`, `npc_commander_felstrom`, `npc_stitches`, and `npc_sirra_vonindi`.

### `AddSC_duskwood`
Registers all scripts defined in this file with the `ScriptMgr`. It maps script names to their respective AI getters, quest hooks, and area triggers.

---

## Member Reference

**Handle_NightmareCorruption**: Checks player quest status (8735) and life state. Summons `NPC_TWILIGHT_CORRUPTER` if not present nearby. Whispers to player. Logs errors on summon failure.

**AreaTrigger_at_twilight_grove**: Delegates to `Handle_NightmareCorruption`. Returns false.

**npc_twilight_corrupterAI**: Constructor initializes parent `ScriptedAI` and calls `Reset`.

**Reset#4**: Initializes timers (`m_uiSoulCorruptionTimer`, `m_uiCreatureOfNightmareTimer`, `m_uiCheckTimer`) with random values. Clears `CoNPlayerGuid`, `CoNPlayerAggro`, `bEngaged`, and `GUIDs` array.

**Aggro#2**: Plays text 11269 if not already engaged. Sets `bEngaged` to true.

**FillPlayerList**: Clears `GUIDs` array. Iterates threat list, filling `GUIDs` with up to 40 unique player GUIDs.

**UpdateAI#3**: Manages three timers. 1) Casts `SPELL_SOUL_CORRUPTION` on self. 2) Tracks `CoNPlayerGuid`; if aura `SPELL_CREATURE_OF_NIGHTMARE` is lost, restores threat. 3) Every second, checks `GUIDs` for dead players, playing emotes and casting `SPELL_SWELL_OF_SOULS` on self. Handles melee attacks.

**GetAI_npc_twilight_corrupter**: Returns new `npc_twilight_corrupterAI`.

**npc_watcher_blombergAI**: Constructor initializes parent `ScriptedAI`, calls `Reset` and `ResetCreature`.

**Reset#5**: Sets creature to run (`SetWalk(false)`).

**ResetCreature#2**: Resets `m_bIsEngaged` to false and `m_uiSayTimer` to 3000ms.

**UpdateAI#4**: If not engaged, waits for `m_uiSayTimer`. On expiry, yells for help, sets engaged, finds Dodds/Paige, stores their GUIDs, and moves them to specific coordinates. Calls parent `UpdateAI`.

**GetAI_watcherBlomberg**: Returns new `npc_watcher_blombergAI`.

**npc_watcher_selkinAI**: Constructor initializes parent `npc_escortAI` and calls `Reset`.

**Reset#6**: Sets creature to run (`SetWalk(false)`).

**WaypointReached#2**: Empty override.

**GetAI_watcherSelkin**: Returns new `npc_watcher_selkinAI`.

**npc_commander_felstromAI**: Constructor initializes parent `ScriptedAI` and calls `Reset`.

**Reset**: Sets `m_uiSuicide_Timer` to 1500ms, `b_suicide` to false. Updates dynamic flags.

**JustDied**: If killer entry is 771, sets loot recipient to null.

**UpdateAI**: Checks suicide timer. If health <= 10% and not suicidal, casts spell 3488. Handles melee attacks.

**GetAI_commanderFelstrom**: Returns new `npc_commander_felstromAI`.

**npc_sirra_vonindiAI**: Constructor initializes parent `ScriptedAI`, calls `Reset` and `ResetCreature`.

**Reset#2**: Empty override.

**ResetCreature**: Sets `m_bCanSummon` to false, `m_uiTimer` to 3000ms.

**StitchesDied**: Sets `m_uiTimer` to 10 minutes, clears `m_stitchesGuid`.

**UpdateAI#2**: If Stitches not present and summon blocked, counts down `m_uiTimer`. On expiry, sets `m_bCanSummon` to true. Calls parent `UpdateAI`.

**npc_stitchesAI**: Constructor initializes parent `npc_escortAI` and calls `Reset`.

**Reset#3**: Initializes `m_uiAuraOfRotTimer` to 0, `m_uiLaunchTimer` to 10000ms, `m_bLaunchChecked` to false.

**JustDied#2**: Town Crier yells. Despawns watchmen. Dodds/Paige go home. Notifies Sirra AI.

**KilledUnit**: If Selkin killed, Town Crier yells.

**DespawnWatcher**: Iterates `m_lWatchman`, forcing alive creatures to disappear/die.

**SummonWatchman**: Summons creature from `Watchman` array at predefined coords.

**AddToFormation**: Joins `pAdd` to `pLeader`'s group with specific options.

**JustSummoned**: Adds to `m_lWatchman`. Specific setup for Hutchins/Bloemberg (move/set home) and Selkin (start escort). Extracts Dodds/Paige GUIDs from Bloemberg AI.

**SummonedCreatureJustDied**: Removes GUID from `m_lWatchman`.

**WaypointReached**: Drives event dialogue and summons. 10/29/61: Stitches yell. 30/35/61: Town Crier yells. 31: Summons Hutchins/Bloemberg. 34: Finds/positions Cutford. 39: Summons Selkin group. 61: Summons Sarys/Corwin. 65: Stops escort, random move.

**Aggro**: 25% chance to yell Text 278.

**UpdateEscortAI**: Emergency launch check (10s). Casts `SPELL_AURA_OF_ROT` on victim every 3s. Melee attacks.

**GetAI_stitches**: Returns new `npc_stitchesAI`.

**SummonStitches**: Checks validity. Summons Stitches, scales to 2.0, calls `LaunchStitches`. Returns success status.

**LaunchStitches**: Finds Town Crier, passes GUIDs to Stitches AI, starts escort. Logs error if AI cast fails.

**GetAI_npc_sirra_vonindi**: Returns new `npc_sirra_vonindiAI`.

**QuestRewarded_npc_sirra_vonindi**: If Quest 401, attempts to summon Stitches. Returns inverse of summon result.

**AddSC_duskwood**: Registers all scripts (AIs, Quest Hook, Area Trigger) with `ScriptMgr`.

---

<!-- machine-true, projected from graph.json -->

## Map — duskwood

*Source:* duskwood.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| Handle_NightmareCorruption | function | Log.Main/Out, Player.Main/GetName, Player.Main/GetQuestStatus, Unit.Main/IsDead, WorldObject.Object/FindNearestCreature, WorldObject.Object/MonsterWhisper, WorldObject.Object/SummonCreature#2 | — | — |
| AreaTrigger_at_twilight_grove | function | — | — | — |
| npc_twilight_corrupterAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#4 | method | shared_Util/urand | — | — |
| Aggro#2 | method | ScriptMgr/DoScriptText | — | — |
| FillPlayerList | method | HostileReference/getUnitGuid, ObjectGuid/IsPlayer, ThreatManager/getThreatList, Unit.Main/GetThreatManager | — | — |
| UpdateAI#3 | method | Creature.Main/SelectAttackingTarget, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, Map.Main/GetPlayer, Object/GetGUID, Object/IsPlayer, ObjectGuid/ObjectGuid#5, Player.Main/GetName, shared_Util/urand, SpellCaster/CastSpell#2, ThreatManager/addThreatDirectly, ThreatManager/getThreat, ThreatManager/modifyThreatPercent#2, Unit.Main/GetThreatManager, Unit.Main/GetVictim, Unit.Main/HasAura#2, Unit.Main/IsDead, Unit.Main/SelectHostileTarget, WorldObject.Object/GetMap, WorldObject.Object/MonsterTextEmote | — | — |
| GetAI_npc_twilight_corrupter | function | — | — | — |
| npc_watcher_blombergAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#5 | method | Unit.Main/SetWalk | — | — |
| ResetCreature#2 | method | — | — | — |
| UpdateAI#4 | method | BasicAI/UpdateAI, Creature.MotionMaster/MovePoint, Object/GetObjectGuid, Unit.Main/GetMotionMaster, WorldObject.Object/FindNearestCreature, WorldObject.Object/MonsterSay | — | — |
| GetAI_watcherBlomberg | function | — | — | — |
| npc_watcher_selkinAI | ctor | ScriptedEscortAI/npc_escortAI | — | — |
| Reset#6 | method | Unit.Main/SetWalk | — | — |
| WaypointReached#2 | method | — | — | — |
| GetAI_watcherSelkin | function | — | — | — |
| npc_commander_felstromAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset | method | WorldObject.Object/ForceValuesUpdateAtIndex | — | — |
| JustDied | method | Creature.Main/SetLootRecipient, Object/GetEntry | — | — |
| UpdateAI | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, Unit.Main/GetHealthPercent, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_commanderFelstrom | function | — | — | — |
| npc_sirra_vonindiAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#2 | method | — | — | — |
| ResetCreature | method | — | — | — |
| StitchesDied | method | ObjectGuid/Clear | — | — |
| UpdateAI#2 | method | BasicAI/UpdateAI, ObjectGuid/operator! | — | — |
| npc_stitchesAI | ctor | ScriptedEscortAI/npc_escortAI | — | — |
| Reset#3 | method | — | — | — |
| JustDied#2 | method | Creature.Main/AI, Creature.MotionMaster/MoveTargetedHome, Map.Main/GetCreature, Unit.Main/GetMotionMaster, Unit.Main/IsAlive, WorldObject.Object/GetMap, WorldObject.Object/MonsterYellToZone | — | — |
| KilledUnit | method | Map.Main/GetCreature, Object/GetEntry, Unit.Main/IsAlive, WorldObject.Object/GetMap, WorldObject.Object/MonsterYellToZone | — | — |
| DespawnWatcher | method | Creature.Main/DisappearAndDie, Map.Main/GetCreature, Unit.Main/IsAlive, WorldObject.Object/GetMap | — | — |
| SummonWatchman | method | WorldObject.Object/SummonCreature#2 | — | — |
| AddToFormation | method | Creature.Main/JoinCreatureGroup, WorldObject.Object/GetAngle, WorldObject.Object/GetOrientation | — | — |
| JustSummoned | method | Creature.Main/AI, Creature.Main/SetCombatStartPosition, Creature.Main/SetHomePosition, Creature.MotionMaster/MovePoint, Object/GetEntry, Object/GetObjectGuid, ScriptedEscortAI/Start, Unit.Main/GetMotionMaster, Unit.Main/SetWalk | — | — |
| SummonedCreatureJustDied | method | Object/GetObjectGuid | — | — |
| WaypointReached | method | Creature.Main/AI, Creature.Main/SetCombatStartPosition, Creature.Main/SetHomePosition, Creature.MotionMaster/MovePoint, Creature.MotionMaster/MoveRandom, CreatureAI/AttackStart, Map.Main/GetCreature, Object/GetObjectGuid, ScriptedEscortAI/Stop, Unit.Main/GetMotionMaster, Unit.Main/IsAlive, Unit.Main/SetWalk, WorldObject.Object/FindNearestCreature, WorldObject.Object/GetDistance2d#3, WorldObject.Object/GetMap, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/MonsterYellToZone | — | — |
| Aggro | method | shared_Util/urand, WorldObject.Object/MonsterYell#2 | — | — |
| UpdateEscortAI | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, Log.Main/Out, ScriptedEscortAI/HasEscortState, ScriptedEscortAI/Start, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_stitches | function | — | — | — |
| SummonStitches | method | Map.Main/GetCreature, Object/GetObjectGuid, WorldObject.Object/GetMap, WorldObject.Object/SetObjectScale, WorldObject.Object/SummonCreature#2 | — | — |
| LaunchStitches | method | Creature.Main/AI, Log.Main/Out, Object/GetObjectGuid, ScriptedEscortAI/Start, WorldObject.Object/FindNearestCreature | — | — |
| GetAI_npc_sirra_vonindi | function | — | — | — |
| QuestRewarded_npc_sirra_vonindi | function | Creature.Main/AI, QuestDef/GetQuestId | — | — |
| AddSC_duskwood | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
