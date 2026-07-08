# boss_celebras_the_cursed

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# boss_celebras_the_cursed

This unit implements the AI and interaction scripts for two entities in the Maraudon instance: **Celebras the Cursed**, a hostile boss, and **Celebras Spirit** (Redeemed), a neutral escort NPC tied to a questline. It handles the boss's combat rotation, the spirit's waypoint-based escort sequence synchronized with player actions, and the registration of these scripts with the server.

## Purpose & Responsibilities

1.  **Celebras the Cursed (`celebras_the_cursedAI`)**: Manages the boss's combat behavior, cycling through three spells (`Wrath`, `Entangling Roots`, `Corrupt Forces`) and melee attacks. It reports death to the instance script.
2.  **Celebras Spirit (`celebrasSpiritAI`)**: Orchestrates a complex escort quest. It moves the spirit along waypoints, triggers dialogue, summons temporary game objects (auras, books), waits for player interaction (reading a book), and completes the quest. It uses an internal phase counter and event timer to coordinate these steps.
3.  **Interaction Handlers**: `GOHello_go_book_celebras` and `QuestAccept_celebras_spirit` bridge player actions (clicking a book, accepting a quest) to the spirit's AI state.

## Member-by-Member Behavior

### Celebras the Cursed (Boss Combat)

*   **`celebras_the_cursedAI` (ctor)**: Retrieves the `ScriptedInstance` pointer from the creature and calls `Reset()` to initialize timers.
*   **`Reset#2`**: Sets initial cooldowns: `Wrath_Timer` (8s), `EntanglingRoots_Timer` (2s), and `CorruptForces_Timer` (30s).
*   **`JustDied`**: Calls `InstanceData::SetData` to mark `TYPE_CELEBRAS` as `DONE`.
*   **`UpdateAI`**: The combat loop. If a valid target exists:
    *   **Wrath**: Every 8s, casts `SPELL_WRATH` on a random target.
    *   **Entangling Roots**: Every 20s (after the initial 2s), casts `SPELL_ENTANGLINGROOTS` on the current victim.
    *   **Corrupt Forces**: Every 20s, interrupts non-melee spells and casts `SPELL_CORRUPT_FORCES` on self.
    *   Performs melee attacks if ready.
*   **`GetAI_celebras_the_cursed`**: Factory function creating a `celebras_the_cursedAI` instance.

### Celebras Spirit (Escort & Quest Logic)

*   **`celebrasSpiritAI` (ctor)**: Initializes the escort AI, pauses the escort, clears the aura GUID, and calls `Reset()`.
*   **`Reset`**: Resets `m_uiPhase` to 0, `Event_Timer` to 0, `auraGUID` to 0, and `m_bBookRead` to false.
*   **`WaypointReached`**: Executes logic based on the waypoint index:
    *   **0**: Sets orientation and home position.
    *   **1**: Plays `SAY_WP_1` and stops running.
    *   **3**: Plays `SAY_WP_3` to the player and starts a 4s event timer.
    *   **4**: Sets orientation and pauses the escort.
    *   **5**: Refreshes a nearby `GO_CELEBRAS_BLUE_AURA` (respawn 6m), summons a new one at fixed coordinates (storing its GUID in `auraGUID`), and plays `SAY_WP_5`.
    *   **6**: Plays `SAY_WP_6` and activates all `GO_CREATOR` objects within 40 yards.
    *   **9**: Deletes the summoned aura using `auraGUID`.
    *   **13**: Stops the escort and starts a 3s event timer.
*   **`QuestAccepted`**: Resets AI state, plays `SAY_ACCEPT`, and starts the escort with the player's GUID.
*   **`JustStartedEscort`**: Pauses the escort, sets a 5s event timer, and advances `m_uiPhase` to 1.
*   **`BookRead`**: Sets `m_bBookRead` to true and reduces `Event_Timer` to 1s if it was higher, accelerating the next phase.
*   **`UpdateEscortAI`**: The main update loop.
    *   **Event Mode**: If `Event_Timer` is active and not in combat, it decrements the timer. On expiry, it executes phase logic:
        *   **Phase 1**: Unpauses escort.
        *   **Phase 2**: Plays `SAY_PRE_READ`, sets 1s timer.
        *   **Phase 3**: Summons `GO_TOME` at fixed coordinates, sets 1s timer.
        *   **Phase 4**: Emotes channeling, pauses escort. Timer is 1s if `m_bBookRead` is true, else 30s.
        *   **Phase 5**: If `m_bBookRead` is false, resets the escort (failure). Else, plays `SAY_POST_READ`, sets 1s timer.
        *   **Phase 6**: Unpauses escort.
        *   **Phase 7**: Sets quest/gossip flags. If the player has `QUEST_SCEPTER` incomplete, marks it complete and opens the gossip menu.
        *   Increments `m_uiPhase` after execution.
    *   **Combat Mode**: If in combat, performs melee attacks.

### Interaction Handlers

*   **`GOHello_go_book_celebras`**: Triggered when a player clicks the book. If the player has `QUEST_SCEPTER` incomplete, the player speaks a phrase, the book is deleted, and `BookRead()` is called on the nearest `celebrasSpiritAI` within 40 yards.
*   **`QuestAccept_celebras_spirit`**: Validates that the accepted quest is `QUEST_SCEPTER`. If so, calls `QuestAccepted()` on the spirit's AI.
*   **`GetAI_celebras_spirit`**: Factory function creating a `celebrasSpiritAI` instance.

### Script Registration

*   **`AddSC_boss_celebras_the_cursed`**: Registers three scripts: `celebras_the_cursed` (boss AI), `celebras_spirit` (spirit AI + quest accept), and `go_book_celebras` (book interaction).

## Cross-Unit Boundaries

*   **`celebras_the_cursedAI`**:
    *   Calls `ScriptedAI` base methods.
    *   Calls `WorldObject::GetInstanceData` to get the instance script.
    *   Calls `InstanceData::SetData` in `JustDied`.
    *   Calls `Creature::SelectAttackingTarget`, `CreatureAI::DoCastSpellIfCan`, `CreatureAI::DoMeleeAttackIfReady`, `SpellCaster::InterruptNonMeleeSpells`, `Unit::GetVictim`, and `Unit::SelectHostileTarget` in `UpdateAI`.
*   **`celebrasSpiritAI`**:
    *   Inherits from `ScriptedEscortAI` (`npc_escortAI`).
    *   Calls `ScriptedEscortAI::SetEscortPaused`, `GetPlayerForEscort`, `SetRun`, `Stop`, `Start`, and `ResetEscort` for escort control.
    *   Calls `WorldObject::GetInstanceData` for instance data.
    *   Calls `Creature::SetHomePosition`, `GameObject::Delete/Refresh/SetGoState/SetRespawnTime`, `GridSearchers::GetGameObjectListWithEntryInGrid`, `Map::GetGameObject`, `Object::GetGUID`, `ScriptMgr::DoScriptText`, `WorldObject::FindNearestGameObject/GetMap/GetOrientation/GetPosition*/SetOrientation/SummonGameObject` in `WaypointReached`.
    *   Calls `Creature::GetCreatureInfo`, `CreatureAI::DoMeleeAttackIfReady`, `Player::AreaExploredOrEventHappens/GetQuestStatus/PrepareGossipMenu/SendPreparedGossip`, `Unit::GetVictim/SelectHostileTarget`, `WorldObject::SetFlag/SummonGameObject` in `UpdateEscortAI`.
*   **`GOHello_go_book_celebras`**:
    *   Calls `Creature::AI`, `GameObject::Delete`, `GridSearchers::GetCreatureListWithEntryInGrid`, `Player::GetQuestStatus/Say`.
*   **`QuestAccept_celebras_spirit`**:
    *   Calls `Creature::AI`, `QuestDef::GetQuestId`.
*   **`AddSC_boss_celebras_the_cursed`**:
    *   Calls `Script::Script`, `Script::RegisterSelf`, `ScriptMgr::RegisterSelf`.
    *   Called by `ScriptLoader::AddScripts`.

## Data Model

This unit does not interact with any database tables. State is managed in-memory via AI members and instance data.

## Notable Implementation Details

*   **Timer Initialization Discrepancy**: `celebras_the_cursedAI::Reset#2` sets `EntanglingRoots_Timer` to 2000ms, but `UpdateAI` resets it to 20000ms after the first cast. The first cast occurs after 2 seconds instead of the intended 20.
*   **Escort Phase Management**: `celebrasSpiritAI` uses `m_uiPhase` and `Event_Timer` to drive its sequence. Coordination between `WaypointReached`, `UpdateEscortAI`, and `BookRead` is critical.
*   **Game Object Lifecycle**: The spirit summons `GO_CELEBRAS_BLUE_AURA` at WP 5 and deletes it at WP 9 using the stored `auraGUID`.
*   **Quest Completion**: Handled in Phase 7 of `UpdateEscortAI`. It checks `QUEST_SCEPTER` status, marks it complete, and opens the gossip menu.
*   **Failure Condition**: If the player doesn't read the book within 30s (Phase 4), Phase 5 resets the escort.
*   **Dynamic Casting**: `GOHello_go_book_celebras` and `QuestAccept_celebras_spirit` use `dynamic_cast` to safely access `celebrasSpiritAI`.

## Member Reference

**celebras_the_cursedAI** (ctor): Initializes boss AI, retrieves instance data, and calls `Reset`.
**Reset#2**: Resets combat timers for Wrath, Entangling Roots, and Corrupt Forces.
**JustDied**: Signals the instance script that the boss is defeated.
**UpdateAI**: Manages combat timers, spell casting, and melee attacks.
**GetAI_celebras_the_cursed**: Factory function returning a new `celebras_the_cursedAI` instance.
**celebrasSpiritAI** (ctor): Initializes escort AI, pauses escort, and calls `Reset`.
**Reset**: Resets escort phase, timers, and flags.
**WaypointReached**: Executes waypoint-specific logic including dialogue, object summoning/deletion, and escort pausing.
**QuestAccepted**: Handles quest acceptance, resets state, and starts the escort.
**JustStartedEscort**: Pauses escort and initializes the first event timer.
**BookRead**: Marks the book as read and accelerates the next phase timer.
**UpdateEscortAI**: Manages event-driven phases, quest completion, and combat fallback.
**GOHello_go_book_celebras**: Handles player interaction with the book, deleting it and notifying the spirit AI.
**QuestAccept_celebras_spirit**: Validates quest ID and triggers the spirit's `QuestAccepted` handler.
**GetAI_celebras_spirit**: Factory function returning a new `celebrasSpiritAI` instance.
**AddSC_boss_celebras_the_cursed**: Registers the boss, spirit, and book scripts with the script manager.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_celebras_the_cursed

*Source:* boss_celebras_the_cursed.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| celebras_the_cursedAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset#2 | method | — | — | — |
| JustDied | method | InstanceData/SetData | — | — |
| UpdateAI | method | Creature.Main/SelectAttackingTarget, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, SpellCaster/InterruptNonMeleeSpells, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_celebras_the_cursed | function | — | — | — |
| celebrasSpiritAI | ctor | ScriptedEscortAI/npc_escortAI, ScriptedEscortAI/SetEscortPaused, WorldObject.Object/GetInstanceData | — | — |
| Reset | method | — | — | — |
| WaypointReached | method | Creature.Main/SetHomePosition, GameObject/Delete, GameObject/Refresh, GameObject/SetGoState, GameObject/SetRespawnTime, GridSearchers/GetGameObjectListWithEntryInGrid#2, Map.Main/GetGameObject, Object/GetGUID, ObjectGuid/ObjectGuid#5, ScriptedEscortAI/GetPlayerForEscort, ScriptedEscortAI/SetEscortPaused, ScriptedEscortAI/SetRun, ScriptedEscortAI/Stop, ScriptMgr/DoScriptText, WorldObject.Object/FindNearestGameObject, WorldObject.Object/GetMap, WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/SetOrientation, WorldObject.Object/SummonGameObject | — | — |
| QuestAccepted | method | Object/GetGUID, ScriptedEscortAI/Start, ScriptMgr/DoScriptText | — | — |
| JustStartedEscort | method | ScriptedEscortAI/SetEscortPaused | — | — |
| BookRead | method | — | — | — |
| UpdateEscortAI | method | Creature.Main/GetCreatureInfo, CreatureAI/DoMeleeAttackIfReady, Player.Main/AreaExploredOrEventHappens, Player.Main/GetQuestStatus, Player.Main/PrepareGossipMenu, Player.Main/SendPreparedGossip, ScriptedEscortAI/GetPlayerForEscort, ScriptedEscortAI/ResetEscort, ScriptedEscortAI/SetEscortPaused, ScriptMgr/DoScriptText, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget, WorldObject.Object/SetFlag, WorldObject.Object/SummonGameObject | — | — |
| GOHello_go_book_celebras | function | Creature.Main/AI, GameObject/Delete, GridSearchers/GetCreatureListWithEntryInGrid#2, Player.Main/GetQuestStatus, Player.Main/Say | — | — |
| QuestAccept_celebras_spirit | function | Creature.Main/AI, QuestDef/GetQuestId | — | — |
| GetAI_celebras_spirit | function | — | — | — |
| AddSC_boss_celebras_the_cursed | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
