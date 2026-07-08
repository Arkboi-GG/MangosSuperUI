<!-- provenance: verbose -->
# redridge_mountains

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# redridge_mountains

## Purpose & Responsibilities

This unit implements the scripted behavior for **Corporal Keeshan**, specifically supporting Quest 219 ("Missing in Action"). It defines a custom escort AI (`npc_corporal_keeshan_escortAI`) that manages waypoint-triggered dialogue and animations, as well as a combat routine using Mocking Blow and Shield Bash.

## Member-by-Member Behavior

### Escort AI (`npc_corporal_keeshan_escortAI`)

Inherits from `npc_escortAI` to handle movement and player association.

*   **Constructor**: Initializes the base class and calls `Reset()` to prime combat timers.
*   **`Reset`**: Sets `m_uiMockingBlowTimer` to 5000 ms and `m_uiShieldBashTimer` to 8000 ms.
*   **`WaypointStart`**: Triggered when movement to a waypoint begins.
    *   **WP 27**: NPC stands up (`UNIT_STAND_STATE_STAND`) and speaks text ID 27 to the escort player.
    *   **WP 54**: NPC speaks text ID 30 to the escort player (farewell).
*   **`WaypointReached`**: Triggered upon arrival at a waypoint.
    *   **WP 26**: NPC sits down (`UNIT_STAND_STATE_SIT`) and speaks text ID 26 to the escort player.
    *   **WP 53**: NPC speaks text ID 29, then triggers `GroupEventHappens` for Quest 219 to complete the quest for the player/group.
*   **`UpdateEscortAI`**: Combat loop. Returns early if no victim. Otherwise:
    *   Casts **Mocking Blow** (Spell 21008) if `m_uiMockingBlowTimer` expires (5s).
    *   Casts **Shield Bash** (Spell 11972) if `m_uiShieldBashTimer` expires (8s).
    *   Executes melee attacks via `DoMeleeAttackIfReady`.

### Script Hooks & Registration

*   **`GetAI_npc_corporal_keeshan`**: Factory function returning a new `npc_corporal_keeshan_escortAI` instance.
*   **`QuestAccept_npc_corporal_keeshan`**: Triggered when a player accepts a quest from the NPC.
    *   Validates Quest ID is 219.
    *   Plays intro dialogue (text ID 25).
    *   Sets NPC faction to `FACTION_ESCORTEE` (ID 10) temporarily, restoring on respawn.
    *   Starts the escort via `Start()`, linking the player GUID and quest.
*   **`AddSC_redridge_mountains`**: Registers the script `"npc_corporal_keeshan"` with `ScriptMgr`, linking the AI getter and quest accept hook.

## Cross-Unit Boundaries

*   **`ScriptedEscortAI` / `npc_escortAI`**: Provides base escort functionality (`Start`, `GetPlayerForEscort`).
*   **`ScriptMgr`**: Broadcasts dialogue (`DoScriptText`) and registers the script (`RegisterSelf`).
*   **`Unit.Main`**: Controls NPC state (`SetStandState`, `GetVictim`, `SelectHostileTarget`).
*   **`CreatureAI`**: Handles combat actions (`DoCastSpellIfCan`, `DoMeleeAttackIfReady`).
*   **`Player.Main`**: Completes the quest via `GroupEventHappens`.
*   **`Creature.Main`**: Retrieves AI (`AI`) and modifies faction (`SetFactionTemporary`).
*   **`Object`**: Retrieves player GUID (`GetGUID`).
*   **`QuestDef`**: Validates quest ID (`GetQuestId`).
*   **`ScriptLoader`**: Invokes `AddSC_redridge_mountains` at startup.

## Data Model

No database tables are accessed. All configuration (spell IDs, factions, text IDs, waypoints) is hardcoded.

## Notable Implementation Details

*   **Faction Safety**: The NPC’s faction is switched to `FACTION_ESCORTEE` (10) on quest accept to prevent hostility, reverting on respawn via `TEMPFACTION_RESTORE_RESPAWN`.
*   **Timer Logic**: Timers subtract `uiDiff`. Large gaps cause immediate casts but reset to fixed intervals, preventing double-casts.
*   **Waypoint Coupling**: Dialogue/animations are tied to specific waypoint indices (26–27, 53–54). Pathing changes in the database would desync behavior.
*   **Dynamic Cast**: `QuestAccept_npc_corporal_keeshan` uses `dynamic_cast` to access the escort AI, assuming the creature’s AI is already initialized to this type.

## Member Reference

*   **`npc_corporal_keeshan_escortAI`**: Constructor initializing the escort AI and calling `Reset()`.
*   **`Reset`**: Resets combat timers for Mocking Blow (5s) and Shield Bash (8s).
*   **`WaypointStart`**: Handles WP 27 (stand, speak) and WP 54 (speak).
*   **`WaypointReached`**: Handles WP 26 (sit, speak) and WP 53 (speak, complete quest).
*   **`UpdateEscortAI`**: Manages combat spells and melee attacks based on timers.
*   **`GetAI_npc_corporal_keeshan`**: Factory function creating the escort AI instance.
*   **`QuestAccept_npc_corporal_keeshan`**: Validates quest 219, sets faction, plays intro, and starts escort.
*   **`AddSC_redridge_mountains`**: Registers the script with the engine.

---

<!-- machine-true, projected from graph.json -->

## Map — redridge_mountains

*Source:* redridge_mountains.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| npc_corporal_keeshan_escortAI | ctor | ScriptedEscortAI/npc_escortAI | — | — |
| Reset | method | — | — | — |
| WaypointStart | method | ScriptedEscortAI/GetPlayerForEscort, ScriptMgr/DoScriptText, Unit.Main/SetStandState | — | — |
| WaypointReached | method | Player.Main/GroupEventHappens, ScriptedEscortAI/GetPlayerForEscort, ScriptMgr/DoScriptText, Unit.Main/SetStandState | — | — |
| UpdateEscortAI | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_npc_corporal_keeshan | function | — | — | — |
| QuestAccept_npc_corporal_keeshan | function | Creature.Main/AI, Creature.Main/SetFactionTemporary, Object/GetGUID, QuestDef/GetQuestId, ScriptedEscortAI/Start, ScriptMgr/DoScriptText | — | — |
| AddSC_redridge_mountains | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
