# sunken_temple

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# sunken_temple

**Purpose & Responsibilities**  
This unit implements the scripted behavior for Malfurion Stormrage (`npc_malfurion_stormrage`) and the area trigger that summons him (`at_shade_of_eranikus`) within the Sunken Temple dungeon. It handles two distinct flows:
1.  **In-Dungeon Entrance Sequence:** When Malfurion spawns inside the dungeon instance, `npc_malfurionAI` runs a timed, multi-step sequence of emotes, dialogue, and visibility changes, ending with him becoming a quest/gossip giver.
2.  **Quest-Triggered Summoning:** `AreaTrigger_at_shade_of_eranikus` checks player quest progress and proximity to conditionally summon Malfurion for the "Eranikus, Tyrant of Dreams" quest chain.

The unit does not implement boss AI or general dungeon mechanics.

## Member-by-Member Behavior

### `npc_malfurionAI` (Constructor)
Initializes the AI for Malfurion. It removes `UNIT_NPC_FLAG_QUESTGIVER` and `UNIT_NPC_FLAG_GOSSIP` flags to prevent premature interaction. It checks `Map.Main/IsDungeon`; if true, it sets `m_inDungeon`, hides the creature (`VISIBILITY_OFF`), and triggers the initial "Walls Tremble" emote via `ScriptMgr/DoScriptText`. Timers and speech counters are initialized.

### `Reset`
Empty override. No reset logic is implemented.

### `UpdateAI`
Executes the entrance sequence only if `m_inDungeon` is true. It uses a state machine (`m_uiSpeech`, 0–6) driven by `m_uiSayTimer`:
-   **Step 0:** Sets visibility to `VISIBILITY_ON`, plays a roar emote, casts spell 20761 (resurrection visual), and sets a 1.5s timer.
-   **Step 1:** Plays a bow emote, sets a 2s timer.
-   **Steps 2–5:** Delivers four dialogue lines (`SAY_MALFURION1`–`SAY_MALFURION4`) via `ScriptMgr/DoScriptText`, with timers between 5s and 10s.
-   **Step 6:** Restores quest/gossip flags via `WorldObject.Object/SetFlag`.
The sequence stops after step 6. Timers decrement by `uiDiff` each tick.

### `GetAI_npc_malfurion`
Factory function returning a new `npc_malfurionAI` instance.

### `AreaTrigger_at_shade_of_eranikus`
Handles the area trigger for summoning Malfurion. It validates:
1.  Player is alive (`Unit.Main/IsAlive`) and trigger ID matches `AREATRIGGER_MALFURION`.
2.  Player has completed `QUEST_THE_CHARGE_OF_DRAGONFLIGHTS` (`Player.Main/GetQuestRewardStatus`).
3.  Player is not on or has not completed `QUEST_ERANIKUS_TYRANT_OF_DREAMS` (`Player.Main/GetQuestStatus`/`GetQuestRewardStatus`).
4.  Malfurion is not already within 50 yards (`GridSearchers/GetClosestCreatureWithEntry`).
If valid, it summons Malfurion at `pAt->y - 15` using `WorldObject.Object/SummonCreature` with `TEMPSUMMON_CORPSE_DESPAWN`.

### `AddSC_sunken_temple`
Registers `"npc_malfurion_stormrage"` (AI) and `"at_shade_of_eranikus"` (Area Trigger) with the engine via `ScriptMgr/RegisterSelf`. Called by `ScriptLoader/AddScripts`.

## Cross-Unit Boundaries

| Member | Direction | Collaborating Unit | Interaction Details |
|--------|-----------|---------------------|---------------------|
| `npc_malfurionAI` (ctor) | Calls | `Map.Main/IsDungeon` | Determines if the creature is in a dungeon instance. |
| `npc_malfurionAI` (ctor) | Calls | `ScriptMgr/DoScriptText` | Plays the initial "Walls Tremble" emote. |
| `npc_malfurionAI` (ctor) | Calls | `Unit.Main/SetVisibility` | Hides Malfurion initially. |
| `npc_malfurionAI` (ctor) | Calls | `WorldObject.Object/RemoveFlag` | Removes quest/gossip flags. |
| `UpdateAI` | Calls | `ScriptMgr/DoScriptText` | Delivers dialogue lines. |
| `UpdateAI` | Calls | `SpellCaster/CastSpell` | Casts visual resurrection spell (step 0). |
| `UpdateAI` | Calls | `Unit.Main/HandleEmoteCommand` | Plays roar and bow emotes. |
| `UpdateAI` | Calls | `Unit.Main/SetVisibility` | Makes Malfurion visible (step 0). |
| `UpdateAI` | Calls | `WorldObject.Object/SetFlag` | Restores quest/gossip flags (step 6). |
| `AreaTrigger_at_shade_of_eranikus` | Calls | `GridSearchers/GetClosestCreatureWithEntry` | Checks if Malfurion is already nearby. |
| `AreaTrigger_at_shade_of_eranikus` | Calls | `Player.Main/GetQuestRewardStatus` | Verifies completion of prerequisite quests. |
| `AreaTrigger_at_shade_of_eranikus` | Calls | `Player.Main/GetQuestStatus` | Ensures player isn't already on the Malfurion quest. |
| `AreaTrigger_at_shade_of_eranikus` | Calls | `Unit.Main/IsAlive` | Validates player is alive. |
| `AreaTrigger_at_shade_of_eranikus` | Calls | `WorldObject.Object/SummonCreature` | Spawns Malfurion at the trigger location. |
| `AddSC_sunken_temple` | Calls | `Script/Script` | Creates script objects for registration. |
| `AddSC_sunken_temple` | Calls | `ScriptMgr/RegisterSelf` | Registers scripts with the engine. |
| `AddSC_sunken_temple` | Called by | `ScriptLoader/AddScripts` | Invoked during server initialization. |

## Data Model

This unit does not access any database tables. Logic is driven by in-memory state, quest IDs, and entity coordinates.

## Notable Implementation Details

1.  **Dungeon-Specific Sequence:** `npc_malfurionAI` only runs the entrance sequence if `Map.Main/IsDungeon()` is true. Outside dungeons, Malfurion behaves normally.
2.  **Timer Precision:** `UpdateAI` uses a simple countdown (`m_uiSayTimer -= uiDiff`). If `uiDiff` exceeds the remaining timer, the step executes immediately, avoiding drift but allowing slight timing variations under load.
3.  **Quest Prerequisites:** `AreaTrigger_at_shade_of_eranikus` enforces strict dependencies: `QUEST_THE_CHARGE_OF_DRAGONFLIGHTS` must be completed, and `QUEST_ERANIKUS_TYRANT_OF_DREAMS` must not be active or completed.
4.  **Spawn Offset:** Malfurion is summoned at `pAt->y - 15`, positioning him slightly south of the trigger point for visual alignment.
5.  **No Reset Logic:** `Reset` is empty. If Malfurion despawns and respawns, the constructor reinitializes the sequence.
6.  **Temporary Summon:** Malfurion is summoned with `TEMPSUMMON_CORPSE_DESPAWN`, ensuring he despawns upon death to prevent corpse clutter.

## Member Reference

- **npc_malfurionAI** (ctor): Initializes Malfurion’s AI, strips quest/gossip flags, checks for dungeon context, hides the creature, and plays an initial emote if in a dungeon.
- **Reset**: Empty override; no reset behavior implemented.
- **UpdateAI**: Drives a 7-step timed dialogue/emote sequence (visibility, emotes, dialogue, flag restoration) only if in a dungeon. Uses a countdown timer and state counter.
- **GetAI_npc_malfurion**: Factory function returning a new `npc_malfurionAI` instance for the given creature.
- **AreaTrigger_at_shade_of_eranikus**: Validates player quest status and proximity, then summons Malfurion at the trigger location if conditions are met.
- **AddSC_sunken_temple**: Registers the Malfurion AI and area trigger scripts with the engine during server startup.

---

<!-- machine-true, projected from graph.json -->

## Map — sunken_temple

*Source:* sunken_temple.cpp, sunken_temple.h

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| npc_malfurionAI | ctor | Map.Main/IsDungeon, ScriptedAI/ScriptedAI, ScriptMgr/DoScriptText, Unit.Main/SetVisibility, WorldObject.Object/GetMap, WorldObject.Object/RemoveFlag | — | — |
| Reset | method | — | — | — |
| UpdateAI | method | ScriptMgr/DoScriptText, SpellCaster/CastSpell#2, Unit.Main/HandleEmoteCommand, Unit.Main/SetVisibility, WorldObject.Object/SetFlag | — | — |
| GetAI_npc_malfurion | function | — | — | — |
| AreaTrigger_at_shade_of_eranikus | function | GridSearchers/GetClosestCreatureWithEntry, Player.Main/GetQuestRewardStatus, Player.Main/GetQuestStatus, Unit.Main/IsAlive, WorldObject.Object/SummonCreature#2 | — | — |
| AddSC_sunken_temple | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
