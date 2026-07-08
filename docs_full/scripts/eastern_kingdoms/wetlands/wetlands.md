# wetlands

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# wetlands.cpp

## Purpose & Responsibilities

`wetlands.cpp` implements the scripted AI and interaction logic for the **Wetlands** zone, specifically supporting the quest chain **"The Missing Diplomat"** (Quest ID 1249). The unit manages three distinct entities:

1.  **Tapoke "Slim" Jahn (`npc_tapoke_slim_jahn`)**: The primary quest NPC who initiates an escort-style escape sequence. His AI handles movement, combat, summoning allies, and a multi-phase surrender dialogue when defeated.
2.  **Slim's Friend (`npc_slims_friend`)**: A guardian pet summoned by Tapoke during combat. Its AI handles autonomous combat behaviors (poison, backstab) and following its owner.
3.  **Mikhail (`npc_mikhail`)**: The quest giver. His gossip and quest acceptance scripts validate preconditions, check the state of Tapoke, and trigger the start of the event.

The core mechanic is a "fight-or-flight" escort event. Players must defeat Tapoke to force him to surrender and complete the quest. If Tapoke reaches specific waypoints (mailbox or gate), the quest fails. The script heavily manipulates faction templates, threat lists, and summon states to simulate this dynamic encounter.

## Member-by-Member Behavior

### Slim's Friend AI (`npc_slims_friendAI`)

This class controls the behavior of the summoned guardian pet (Entry 4971). It inherits from `ScriptedAI`.

*   **`npc_slims_friendAI` (ctor)**: Initializes the AI and immediately calls `Reset()` to set initial timers and apply the passive poison aura.
*   **`Reset`**: Resets internal timers (`m_slowingPoisonTimer`, `m_backstabTimer`) to random intervals. It casts `SPELL_POISON_PROC` (ID 3616) on itself, which applies an aura that periodically triggers damage over time.
*   **`AttackStart`**: Initiates combat with a target. It attempts to attack the unit and commands the motion master to chase the target.
*   **`AttackedBy`**: Handles incoming attacks. If the creature is charmed (controlled by Tapoke) and can reach the attacker with melee, it initiates an attack. This ensures the pet defends its owner.
*   **`UpdateCombatAI`**: The core combat loop.
    *   Checks `m_slowingPoisonTimer`. If expired, casts `SPELL_SLOWING_POISON` (ID 7992) on the victim if they don't already have the aura. Resets timer randomly.
    *   Checks `m_backstabTimer`. If expired, casts `SPELL_BACKSTAB` (ID 15582) on the victim. Resets timer randomly.
    *   Calls `DoMeleeAttackIfReady()` to perform standard melee swings.
*   **`UpdateAI`**: The main update loop.
    *   If in combat (has a victim), delegates to `UpdateCombatAI`.
    *   If not in combat but charmed, it retrieves the owner (`GetCharmerOrOwner`).
        *   If the owner is in combat, the pet selects the owner's attacker as its target and starts attacking.
        *   If the owner is not in combat, the pet follows the owner using `MoveFollow`.
*   **`GetAI_npc_slims_friend`**: Factory function returning a new instance of `npc_slims_friendAI`.

### Tapoke "Slim" Jahn AI (`npc_tapoke_slim_jahnAI`)

This class controls the main quest NPC (Entry 4962). It inherits from `npc_escortAI`, providing waypoint-based movement capabilities.

*   **`npc_tapoke_slim_jahnAI` (ctor)**:
    *   Disables pathfinding between waypoints to prevent erratic movement.
    *   Sets a 750ms delay before the first waypoint.
    *   Stores the original respawn delay from the database.
    *   Sets `m_justCreated` to true to ensure `JustRespawned` logic runs on the first tick.
    *   Calls `Reset()`.
*   **`Reset#2`**: Resets event state variables (`m_nextPhaseDelay`, `m_mdDialogPhase`, `m_isBeaten`) and initializes the `m_pummelTimer`.
*   **`JustDied`**:
    *   Calls `DespawnFriendIfExists()` to remove the summoned pet.
    *   Delegates to `npc_escortAI::JustDied` to handle standard escort failure/completion checks.
*   **`JustRespawned`**:
    *   Restores the original respawn delay and faction (Friendly).
    *   Locates Mikhail (Entry 4963) within 20 yards. If found, Mikhail speaks a line indicating the event is reset.
*   **`WaypointReached`**:
    *   **Waypoint 3 (Mailbox)**: Sets the creature to run and changes faction to Neutral (making him attackable by players).
    *   **Waypoint 9 (Gate)**: Marks the quest as failed for the escorting player via `GroupEventFailHappens`. Despawns the friend. This represents Tapoke escaping successfully.
*   **`Aggro`**:
    *   Checks if "Slim's Friend" is already summoned. If so, returns early (preventing double summons).
    *   Attempts to cast `SPELL_CALL_FRIENDS` (ID 16457) to summon the pet.
    *   If the escort is active and the spell succeeds, Tapoke speaks a taunt line.
*   **`AttackedBy#2`**: Standard aggro handling for the NPC. It validates the attacker, ensuring the creature is not already in combat (`GetVictim`) and that the attacker is not friendly (`IsFriendlyTo`). If valid, it initiates combat via `AttackStart`.
*   **`UpdateEscortAI`**: The main update loop.
    *   **Initialization**: If `m_justCreated` is true, it calls `JustRespawned()` once.
    *   **Post-Defeat Dialogue Phase (`m_isBeaten`)**:
        *   Uses `m_nextPhaseDelay` to sequence dialogue.
        *   **Phase 0**: Faces the player and the pet. Sets delay for next phase.
        *   **Phase 1**: Despawns the pet. Tapoke begs and speaks a surrender line.
        *   **Phase 2**: Tapoke talks, promising to meet at the inn.
        *   **Phase 3**: Marks the quest as successful (`GroupEventHappens`). Stops the escort. Forces a despawn after 1 second and sets a 2-second respawn delay to simulate him returning to the inn. Resets state.
    *   **Combat Phase**:
        *   Selects hostile targets.
        *   Manages `m_pummelTimer`. If expired, casts `SPELL_PUMMEL` (ID 12555) to interrupt spells.
        *   Performs melee attacks.
*   **`JustStartedEscort`**: Casts `SPELL_STEALTH` (ID 6634) on Tapoke to allow him to start moving without immediate aggro.
*   **`DamageTaken`**:
    *   Only acts if the escort is active.
    *   If damage would kill Tapoke OR his health drops below 20%:
        *   Nullifies the damage (`uiDamage = 0`).
        *   Sets `m_isBeaten = true`.
        *   **Pet Cleanup**: Finds the pet, stops its combat, removes auras, deletes threat list, and flags it as spawning/immune. The pet speaks a line abandoning Tapoke.
        *   **Tapoke Cleanup**: Pauses the escort. Sets faction to Friendly to All. Removes auras, deletes threat list, and stops combat. Stops running.
        *   This effectively transitions the encounter from "Combat" to "Dialogue".
*   **`DespawnFriendIfExists`**: Helper method. Finds the guardian pet by entry ID and unsummons it permanently (`PET_SAVE_AS_DELETED`).
*   **`GetAI_npc_tapoke_slim_jahn`**: Factory function returning a new instance of `npc_tapoke_slim_jahnAI`.

### Mikhail Scripts

These functions handle interactions with the quest giver (Entry 4963).

*   **`QuestAccept_npc_mikhail`**:
    *   Validates inputs.
    *   Checks if the quest is "The Missing Diplomat Part 11" (ID 1249).
    *   Locates Tapoke within 20 yards. If not found, aborts (rare race condition protection).
    *   Retrieves Tapoke's AI.
    *   Calls `DespawnFriendIfExists()` on Tapoke's AI to ensure no lingering pets from previous failed attempts or aggro.
    *   Starts the escort event via `tapokeSlimJahnAI->Start(...)`.
*   **`GossipHello_npc_mikhail`**:
    *   Checks if the player has completed the previous quest (ID 1248).
    *   If yes, checks status of current quest (ID 1249).
    *   If quest is not started:
        *   Locates Tapoke. If Tapoke is missing, shows a gossip menu indicating he is unavailable.
        *   If Tapoke is present:
            *   Checks if the escort is already active (`HasEscortState`). If so, shows "unavailable" gossip.
            *   Otherwise, prepares and sends the quest menu.
    *   If quest is already in progress or completed, or if the previous quest isn't done, it simply prepares and sends the standard quest menu.

### Registration

*   **`AddSC_wetlands`**: Registers the three scripts (`npc_slims_friend`, `npc_tapoke_slim_jahn`, `npc_mikhail`) with the `ScriptMgr`. It assigns the appropriate AI getters and gossip/quest handlers.

## Cross-Unit Boundaries

*   **`ScriptedAI` / `npc_escortAI`**: Both AI classes inherit from these base classes. They rely on `ScriptedAI` for basic combat helpers (`DoCastSpellIfCan`, `DoMeleeAttackIfReady`) and `npc_escortAI` for waypoint management (`WaypointReached`, `Start`, `Stop`, `HasEscortState`).
*   **`shared_Util/urand`**: Used extensively for randomizing spell cooldowns and timers to avoid synchronized behavior.
*   **`Creature.MotionMaster`**: Used to command movement (`MoveChase`, `MoveFollow`).
*   **`Unit.Main`**: Used for state queries (`GetVictim`, `IsInCombat`, `GetCharmInfo`), actions (`Attack`, `CastSpell`, `SetFactionTemplateId`, `HandleEmote`), and target selection (`SelectHostileTarget`, `FindGuardianWithEntry`).
*   **`ScriptMgr/DoScriptText`**: Used to trigger broadcast text lines for dialogue.
*   **`GridSearchers/GetClosestCreatureWithEntry`**: Used to locate Mikhail and Tapoke relative to each other or the player to validate event states.
*   **`Player.Main`**: Used to manage quest status (`GetQuestStatus`, `GroupEventHappens`, `GroupEventFailHappens`) and UI interactions (`PrepareQuestMenu`, `SendPreparedQuest`).
*   **`Pet.Main`**: Used to manage the summoned friend (`Unsummon`).

## Data Model

This unit does not directly query or modify database tables via SQL statements in the C++ code. It relies on data loaded into memory by the server core:
*   **`creature_template`**: Implicitly used for Entry IDs (4962, 4963, 4971) and initial faction/respawn delays.
*   **`smart_scripts` / `waypoints`**: Implicitly used by `npc_escortAI` for waypoint paths (IDs 3 and 9).
*   **`broadcast_text`**: Referenced by IDs (e.g., 5827, 5828) for dialogue.
*   **`spell_template`**: Referenced by IDs (e.g., 6634, 16457) for spell effects.

## Notable Implementation Details

1.  **Health Threshold Trigger**: The transition from combat to dialogue is not triggered by death, but by reaching **20% health** or taking lethal damage. `DamageTaken` intercepts the damage, nullifies it, and switches the AI state to `m_isBeaten`. This prevents the NPC from dying and respawning naturally, allowing for a controlled cinematic ending.
2.  **Faction Manipulation**:
    *   Tapoke starts as Friendly.
    *   Upon reaching the Mailbox waypoint, he becomes **Neutral** (attackable).
    *   Upon being "beaten", he becomes **Friendly to All** (immune to further aggression).
    *   This requires careful management to ensure players can attack him during the escape but cannot kill him after surrender.
3.  **Pet Lifecycle Management**: The summoned friend (`npc_slims_friend`) is tightly coupled with Tapoke's state.
    *   Summoned on `Aggro`.
    *   Removed on `JustDied`, `WaypointReached` (escape), `DamageTaken` (surrender), and `QuestAccept` (cleanup).
    *   The `DespawnFriendIfExists` helper is critical to prevent ghost pets or pets that continue fighting after the main event concludes.
4.  **Race Condition Handling**:
    *   `QuestAccept` and `GossipHello` both check for Tapoke's presence within 20 yards. If Tapoke is pulled away by another player or despawns, the script gracefully fails or informs the player via gossip, preventing crashes or invalid state transitions.
    *   `m_justCreated` flag ensures `JustRespawned` logic runs exactly once after creation, avoiding duplicate initialization.
5.  **Stealth on Start**: `JustStartedEscort` casts Stealth on Tapoke. This allows him to begin his waypoint path without immediately aggroing nearby mobs or players until he reaches the intended engagement point.
6.  **Hardcoded Timers**: Dialogue phases use hardcoded delays (2s, 4s, 6s) managed by `m_nextPhaseDelay`. This creates a predictable cinematic sequence.

## Member Reference

**npc_slims_friendAI** (ctor): Initializes the AI for Slim's Friend, calling `Reset()` to set up initial timers and auras.

**Reset**: Resets combat timers for Slowing Poison and Backstab to random intervals and applies the `SPELL_POISON_PROC` aura to self.

**AttackStart**: Initiates combat with a target, commanding the creature to chase and attack.

**AttackedBy**: If charmed and within melee range, initiates an attack on the attacker to defend the owner.

**UpdateCombatAI**: Manages combat timers. Casts `SPELL_SLOWING_POISON` and `SPELL_BACKSTAB` on the victim based on randomized cooldowns, and performs melee attacks.

**UpdateAI**: Main loop. If in combat, updates combat AI. If charmed and not in combat, follows the owner. If owner is in combat, attacks the owner's target.

**GetAI_npc_slims_friend**: Factory function that returns a new `npc_slims_friendAI` instance.

**npc_tapoke_slim_jahnAI** (ctor): Initializes Tapoke's AI, disabling pathfinding, setting initial waypoint delay, storing respawn delay, and marking as just created.

**Reset#2**: Resets event state variables (dialogue phase, beaten status) and initializes the Pummel spell timer.

**JustDied**: Despawns the summoned friend and delegates to the base escort AI for death handling.

**JustRespawned**: Restores original faction and respawn delay. Locates Mikhail and triggers a dialogue line if he is nearby.

**WaypointReached**: Handles waypoint events. At the mailbox, sets faction to neutral and starts running. At the gate, marks the quest as failed and despawns the friend.

**Aggro**: Summons "Slim's Friend" if not already present. Triggers a taunt dialogue if the escort is active.

**AttackedBy#2**: Standard aggro handler for Tapoke, initiating combat with the attacker if valid and not friendly.

**UpdateEscortAI**: Main loop. Handles initialization, post-defeat dialogue sequencing (phases 0-3), and combat logic (Pummel spell, melee). Transitions state based on `m_isBeaten`.

**JustStartedEscort**: Casts Stealth on Tapoke to allow safe initiation of the escape route.

**DamageTaken**: Intercepts damage. If health drops below 20% or lethal, nullifies damage, sets `m_isBeaten`, cleans up the pet (stops combat, removes auras), cleans up Tapoke (stops combat, sets friendly faction), and pauses the escort to begin dialogue.

**DespawnFriendIfExists**: Helper to find and permanently unsummon the "Slim's Friend" pet.

**GetAI_npc_tapoke_slim_jahn**: Factory function that returns a new `npc_tapoke_slim_jahnAI` instance.

**QuestAccept_npc_mikhail**: Validates quest acceptance for ID 1249. Locates Tapoke, cleans up any existing pets, and starts the escort event.

**GossipHello_npc_mikhail**: Handles gossip interaction. Checks quest prerequisites and Tapoke's availability/state to determine whether to offer the quest, show an error, or display standard menus.

**AddSC_wetlands**: Registers the scripts for `npc_slims_friend`, `npc_tapoke_slim_jahn`, and `npc_mikhail` with the server's script manager.

---

<!-- machine-true, projected from graph.json -->

## Map — wetlands

*Source:* wetlands.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| npc_slims_friendAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset | method | shared_Util/urand, SpellCaster/CastSpell#2 | — | — |
| AttackStart | method | Creature.MotionMaster/MoveChase, Unit.Main/Attack, Unit.Main/GetMotionMaster | — | — |
| AttackedBy | method | Unit.Main/CanReachWithMeleeAutoAttack, Unit.Main/GetCharmInfo, Unit.Main/GetVictim | — | — |
| UpdateCombatAI | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, shared_Util/urand, Unit.Main/GetVictim | — | — |
| UpdateAI | method | Creature.MotionMaster/MoveFollow, Unit.Main/GetAttackerForHelper, Unit.Main/GetCharmerOrOwner, Unit.Main/GetCharmInfo, Unit.Main/GetMotionMaster, Unit.Main/GetVictim, Unit.Main/HasUnitState, Unit.Main/IsInCombat | — | — |
| GetAI_npc_slims_friend | function | — | — | — |
| npc_tapoke_slim_jahnAI | ctor | Creature.Main/GetRespawnDelay, ScriptedEscortAI/npc_escortAI, ScriptedEscortAI/SetDelayBeforeTheFirstWaypoint, ScriptedEscortAI/SetPathfindingEnabledBetweenWaypoints | — | — |
| Reset#2 | method | shared_Util/urand | — | — |
| JustDied | method | ScriptedEscortAI/JustDied | — | — |
| JustRespawned | method | Creature.Main/SetRespawnDelay, GridSearchers/GetClosestCreatureWithEntry, ScriptedEscortAI/JustRespawned, ScriptMgr/DoScriptText, Unit.Main/SetFactionTemplateId | — | — |
| WaypointReached | method | Player.Main/GroupEventFailHappens, ScriptedEscortAI/GetPlayerForEscort, ScriptedEscortAI/SetRun, Unit.Main/SetFactionTemplateId | — | — |
| Aggro | method | CreatureAI/DoCastSpellIfCan, ScriptedEscortAI/HasEscortState, ScriptMgr/DoScriptText, Unit.Main/FindGuardianWithEntry | — | — |
| AttackedBy#2 | method | CreatureAI/AttackStart, Unit.Main/GetVictim, Unit.Main/IsFriendlyTo | — | — |
| UpdateEscortAI | method | Creature.Main/ForcedDespawn, Creature.Main/SetRespawnDelay, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, Player.Main/GroupEventHappens, ScriptedEscortAI/GetPlayerForEscort, ScriptedEscortAI/Stop, ScriptMgr/DoScriptText, shared_Util/urand, Unit.Main/FindGuardianWithEntry, Unit.Main/GetVictim, Unit.Main/HandleEmote, Unit.Main/IsAlive, Unit.Main/SelectHostileTarget, Unit.Main/SetFacingToObject | — | — |
| JustStartedEscort | method | SpellCaster/CastSpell#2 | — | — |
| DamageTaken | method | ScriptedEscortAI/HasEscortState, ScriptedEscortAI/SetEscortPaused, ScriptedEscortAI/SetRun, ScriptMgr/DoScriptText, Unit.Main/CombatStop, Unit.Main/DeleteThreatList, Unit.Main/FindGuardianWithEntry, Unit.Main/GetHealth, Unit.Main/GetHealthPercent, Unit.Main/RemoveAllAuras, Unit.Main/SetFactionTemplateId, WorldObject.Object/SetFlag | — | — |
| DespawnFriendIfExists | method | Pet.Main/Unsummon, Unit.Main/FindGuardianWithEntry | — | — |
| GetAI_npc_tapoke_slim_jahn | function | — | — | — |
| QuestAccept_npc_mikhail | function | Creature.Main/AI, GridSearchers/GetClosestCreatureWithEntry, Object/GetGUID, QuestDef/GetQuestId, ScriptedEscortAI/Start | — | — |
| GossipHello_npc_mikhail | function | Creature.Main/AI, GossipDef/SendGossipMenu, GridSearchers/GetClosestCreatureWithEntry, Object/GetGUID, ObjectGuid/ObjectGuid#5, Player.Main/GetQuestStatus, Player.Main/PrepareQuestMenu, Player.Main/SendPreparedQuest, ScriptedEscortAI/HasEscortState | — | — |
| AddSC_wetlands | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
