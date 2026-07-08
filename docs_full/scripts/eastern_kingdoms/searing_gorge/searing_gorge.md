<!-- provenance: verbose -->
# searing_gorge

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# Searing Gorge: Obsidion Event Script (`searing_gorge`)

## Purpose & Responsibilities

The `searing_gorge` translation unit implements the scripted event for **Quest 3566: Rise Obsidion** in the Searing Gorge zone. It controls the boss **Obsidion** (entry 8400) and its summoned companion **Dorius/Lathoric** (entries 8421/8391).

The unit provides:
1.  **`npc_obsidionAI`**: The AI class for Obsidion, managing a pre-combat dialogue phase, a creature transformation, and a combat phase with specific spells.
2.  **Quest Handlers**: Functions attached to the **Dying Archaeologist** NPC to trigger the event via gossip and quest acceptance.

## Member-by-Member Behavior

### Obsidion AI (`npc_obsidionAI`)

The AI inherits from `ScriptedAI` and tracks state via `m_IsEventRunning`, `m_nextText` (dialogue progress), spell timers, `m_playerList` (participants), and `m_Dorius` (companion GUID).

*   **Initialization**:
    *   **`npc_obsidionAI` (ctor)**: Calls `Reset()` to initialize state.
    *   **`Reset`**: Sets Obsidion to `UNIT_STAND_STATE_DEAD` and applies `UNIT_FLAG_IMMUNE_TO_PLAYER`/`UNIT_FLAG_IMMUNE_TO_NPC`. Clears `m_playerList`. If the companion (`m_Dorius`) exists on the map, it is deleted via `WorldObject.Object/DeleteLater`. Clears `m_Dorius`.
    *   **`JustRespawned`**: Calls `Reset()`.

*   **Event Flow**:
    *   **`StartEvent`**: Sets `m_IsEventRunning = true`, initializes dialogue timer/text. Summons **Dorius** (entry 8421) at fixed coordinates with a 3-minute despawn timer. Stores the companion's GUID.
    *   **`UpdateAI`**:
        *   *Dialogue Phase*: If not in combat, it advances `m_nextText`. When `m_nextText` reaches `SAY_LATHORIC1`, it retrieves the companion and calls `Creature.Main/UpdateEntry` to transform Dorius into **Lathoric the Black** (entry 8391). It broadcasts speech via `ScriptMgr/DoScriptText`.
        *   *Combat Transition*: When dialogue finishes (`SAY_LATHORIC2`), it iterates `m_playerList`. For the first valid player found, it sets Obsidion to `UNIT_STAND_STATE_STAND`, removes NPC immunity, and calls `CreatureAI/AttackStart`. It also removes player immunity from Lathoric and forces Lathoric to attack the same player.
        *   *Combat Phase*: Casts **Ground Smash** (spell 12734) every ~8s and **Knock Away** (spell 10101) every ~12s on the victim. Performs melee attacks via `CreatureAI/DoMeleeAttackIfReady`.
    *   **`Aggro`**: Removes immunity flags and calls `ScriptedAI::Aggro`.
    *   **`SummonedCreatureDespawn`**: If **Lathoric** despawns and Obsidion is dead or not in combat, calls `Reset()`.

### Quest Handlers

Attached to the **Dying Archaeologist** NPC.

*   **`GossipHello_npc_dying_archaeologist`**: Checks if **Obsidion** is nearby via `GridSearchers/GetClosestCreatureWithEntry`. If Obsidion is present, returns `false` (blocks menu). Otherwise, prepares the quest menu for Quest 3566 and sends the gossip menu.
*   **`QuestAccept_npc_dying_archaeologist`**: If Quest 3566 is accepted:
    *   Finds **Obsidion** nearby. If not found, returns `true` (allows accept, does nothing).
    *   Retrieves `npc_obsidionAI` from Obsidion. If the event is already running or Obsidion is dead, returns `false` (blocks accept).
    *   Calls `StartEvent()`.
    *   Adds the accepting player and all group members (via `Group/GetFirstMember`) to `m_playerList`.

### Registration

*   **`GetAI_npc_dorius`**: Factory function returning a new `npc_obsidionAI`. Despite the name, it is registered for the creature named `"npc_obsidion"`.
*   **`AddSC_searing_gorge`**: Registers `"npc_obsidion"` (using `GetAI_npc_dorius`) and `"npc_dying_archaeologist"` (gossip/quest hooks) with `ScriptMgr`.

## Cross-Unit Boundaries

*   **`ScriptedAI` / `CreatureAI`**: `npc_obsidionAI` inherits from `ScriptedAI` and uses `CreatureAI` helpers (`DoCastSpellIfCan`, `DoMeleeAttackIfReady`, `AttackStart`).
*   **`Map` / `WorldObject`**: Uses `GetMap()->GetCreature`/`GetPlayer` to locate entities. Uses `SetFlag`/`RemoveFlag` for immunity and `DeleteLater` for cleanup.
*   **`Unit`**: Uses `SetStandState`, `IsAlive`, `IsInCombat`, `GetVictim`, `SelectHostileTarget`.
*   **`ScriptMgr`**: Uses `DoScriptText` for dialogue.
*   **`GridSearchers`**: Uses `GetClosestCreatureWithEntry` in quest handlers.
*   **`Group` / `Player`**: Iterates group members to populate `m_playerList`.

## Data Model

No database tables are accessed. All data (entries, spells, coordinates) is hardcoded or derived from runtime game objects.

## Notable Implementation Details

1.  **Entry Swapping**: Dorius transforms into Lathoric by calling `Creature.Main/UpdateEntry` on the same creature object, preserving the GUID but changing the model/stats.
2.  **Immunity Flags**: Obsidion is immune to players and NPCs until aggroed or the event transitions to combat. This prevents accidental pulls.
3.  **Single Target Aggro**: The combat transition loop breaks after aggroing the *first* valid player in `m_playerList`. Other group members rely on standard threat mechanics to join the fight.
4.  **Stale Cleanup**: `Reset` actively deletes the companion if it persists, preventing duplicate summons on event restarts.

## Member Reference

**npc_obsidionAI** (ctor): Constructs the AI and calls `Reset()`.

**Reset**: Sets Obsidion to dead/immune state. Clears player list. Deletes existing companion if present. Clears companion GUID.

**Aggro**: Removes immunity flags and calls parent `Aggro`.

**StartEvent**: Starts event, sets dialogue state, summons Dorius, stores companion GUID.

**SummonedCreatureDespawn**: If Lathoric despawns and Obsidion is dead/out of combat, calls `Reset()`.

**JustRespawned**: Calls `Reset()`.

**UpdateAI**: Manages dialogue (transforming Dorius to Lathoric, speaking), transitions to combat by aggroing players, and handles combat spells/melee.

**GossipHello_npc_dying_archaeologist**: Blocks menu if Obsidion is nearby; otherwise opens Quest 3566 menu.

**QuestAccept_npc_dying_archaeologist**: Triggers event if Quest 3566 is accepted and Obsidion is valid. Adds player/group to participant list.

**GetAI_npc_dorius**: Factory function creating `npc_obsidionAI`.

**AddSC_searing_gorge**: Registers scripts for Obsidion and the Dying Archaeologist.

---

<!-- machine-true, projected from graph.json -->

## Map — searing_gorge

*Source:* searing_gorge.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| npc_obsidionAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset | method | Map.Main/GetCreature, ObjectGuid/Clear, Unit.Main/SetStandState, WorldObject.Object/DeleteLater, WorldObject.Object/GetMap, WorldObject.Object/SetFlag | — | — |
| Aggro | method | ScriptedAI/Aggro, WorldObject.Object/RemoveFlag | — | — |
| StartEvent | method | Object/GetObjectGuid, WorldObject.Object/SummonCreature#2 | — | — |
| SummonedCreatureDespawn | method | Object/GetEntry, Unit.Main/IsAlive, Unit.Main/IsInCombat | — | — |
| JustRespawned | method | — | — | — |
| UpdateAI | method | Creature.Main/AI, Creature.Main/UpdateEntry, CreatureAI/AttackStart, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, Map.Main/GetCreature, Map.Main/GetPlayer, Object/GetEntry, ScriptMgr/DoScriptText, Unit.Main/GetVictim, Unit.Main/IsInCombat, Unit.Main/SelectHostileTarget, Unit.Main/SetStandState, WorldObject.Object/GetMap, WorldObject.Object/RemoveFlag | — | — |
| GossipHello_npc_dying_archaeologist | function | GossipDef/SendGossipMenu, GridSearchers/GetClosestCreatureWithEntry, Object/GetObjectGuid, Player.Main/PrepareQuestMenu | — | — |
| QuestAccept_npc_dying_archaeologist | function | Creature.Main/AI, GridSearchers/GetClosestCreatureWithEntry, Group/GetFirstMember, GroupReference/next, Object/GetObjectGuid, Player.Main/GetGroup, QuestDef/GetQuestId, Unit.Main/IsAlive | — | — |
| GetAI_npc_dorius | function | — | — | — |
| AddSC_searing_gorge | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
