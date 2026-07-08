<!-- provenance: verbose -->
# boss_ysondre

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# boss_ysondre

## Purpose & Responsibilities

`boss_ysondre.cpp` implements the AI for **Ysondre** (`boss_ysondreAI`) and his summoned minions, the **Demented Druids** (`npc_demented_druidAI`). Ysondre is part of the Dragon of Nightmare encounter. His primary mechanics involve casting **Lightning Wave** on random players and summoning Demented Druids based on the number of active attackers. The Demented Druids serve as adds that cast **Curse of Thorns**, **Moonfire**, and **Silence**.

## Member-by-Member Behavior

### Ysondre (`boss_ysondreAI`)

*   **Initialization**: The constructor calls the parent `boss_dragon_of_nightmareAI` and invokes `Reset`. `Reset` delegates to the parent and initializes `m_uiLightningWaveTimer` (10–13s).
*   **Combat Entry**: `Aggro` calls the parent implementation and broadcasts `SAY_YSONDRE_AGGRO`.
*   **Summoning**: `JustSummoned` verifies if the spawned creature is `NPC_DRUID_SPIRIT`. If so, it assigns a random non-GM player as the target and initiates combat via `AttackStart`.
*   **Special Ability**: `DoSpecialAbility` counts alive, non-GM players on the threat list. It calculates a spawn count: 75% of attackers (capped at 15, minimum 3). It spawns this many `NPC_DRUID_SPIRIT` entities at the boss's location with a 30-second despawn timer and broadcasts `SAY_SUMMON_DRUIDS`.
*   **Update Loop**: `UpdateDragonAI` manages `m_uiLightningWaveTimer`. Upon expiration, it casts `SPELL_LIGHTNINGWAVE` on a random non-GM player and resets the timer (8–12s).

### Demented Druid (`npc_demented_druidAI`)

*   **Initialization**: The constructor calls `ScriptedAI` and invokes `Reset`. `Reset` initializes three timers: `m_uiCurseOfThornsTimer` (4–10s), `m_uiMoonFireTimer` (1–5s), and `m_uiSilenceTimer` (5–12s).
*   **Update Loop**: `UpdateAI` returns early if no victim exists. It manages three abilities:
    1.  **Curse of Thorns**: Casts on a random non-GM player lacking the aura. Resets timer (13–16s) on success.
    2.  **Moonfire**: Casts on the current victim. Resets timer (3–6s) on success.
    3.  **Silence**: Casts on a random non-GM player with Mana lacking the aura. Resets timer (10–14s) on success.
    Finally, it attempts a melee attack.

## Cross-Unit Boundaries

*   **`boss_dragon_of_nightmareAI`**: `boss_ysondreAI` inherits from this unit, delegating base reset and aggro logic.
*   **`ScriptMgr`**: Used by `boss_ysondreAI` to broadcast text events (`DoScriptText`).
*   **`Creature.Main` / `CreatureAI`**: Used for target selection (`SelectAttackingTarget`), initiating combat (`AttackStart`), and casting spells (`DoCastSpellIfCan`, `DoMeleeAttackIfReady`).
*   **`Unit.Main` / `ThreatManager`**: `DoSpecialAbility` uses `GetThreatManager` to count attackers and `IsAlive`/`ToPlayer`/`IsGameMaster` to filter valid targets.
*   **`shared_Util`**: `urand` is used by both AIs to randomize timer intervals.

## Data Model

No database tables are accessed. All configuration (spell IDs, creature entries, text IDs) is hardcoded.

## Notable Implementation Details

*   **GM Exclusion**: Both AIs strictly exclude Game Masters from targeting (`SELECT_FLAG_PLAYER_NOT_GM`) to prevent accidental harassment of staff.
*   **Dynamic Add Count**: `DoSpecialAbility` scales the number of summoned druids with player count, capping at 15 adds for large groups and flooring at 3 for small groups.
*   **Aura Checks**: `npc_demented_druidAI` checks `HasAura` before casting Curse of Thorns and Silence to avoid redundant casts.
*   **Mana Targeting**: Silence specifically targets players with Mana (`SELECT_FLAG_POWER_MANA`).

## Member Reference

**boss_ysondreAI** (ctor): Calls parent `boss_dragon_of_nightmareAI` constructor and invokes `Reset`.

**Reset**: Calls parent `Reset` and sets `m_uiLightningWaveTimer` to 10–13s via `urand`.

**Aggro**: Calls parent `Aggro` and broadcasts `SAY_YSONDRE_AGGRO`.

**JustSummoned**: If summoned creature is `NPC_DRUID_SPIRIT`, assigns a random non-GM player target and starts combat.

**DoSpecialAbility**: Counts alive non-GM attackers. Spawns 75% of that count (min 3, max 15) `NPC_DRUID_SPIRIT` creatures and broadcasts `SAY_SUMMON_DRUIDS`.

**UpdateDragonAI**: If `m_uiLightningWaveTimer` expires, casts `SPELL_LIGHTNINGWAVE` on a random non-GM player and resets timer to 8–12s.

**npc_demented_druidAI** (ctor): Calls `ScriptedAI` constructor and invokes `Reset`.

**Reset#2**: Initializes `m_uiCurseOfThornsTimer` (4–10s), `m_uiMoonFireTimer` (1–5s), and `m_uiSilenceTimer` (5–12s).

**UpdateAI**: If no victim, returns. Else: casts `SPELL_CURSE_OF_THORNS` on random non-GM player (if no aura), `SPELL_MOONFIRE` on victim, and `SPELL_SILENCE` on random mana-using non-GM player (if no aura). Resets timers on success. Performs melee attack.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_ysondre

*Source:* boss_ysondre.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_ysondreAI | ctor | boss_dragon_of_nightmare/boss_dragon_of_nightmareAI | boss_dragon_of_nightmare/GetAI_boss_dragon_of_nightmare | — |
| Reset | method | boss_dragon_of_nightmare/Reset, shared_Util/urand | — | — |
| Aggro | method | boss_dragon_of_nightmare/Aggro, ScriptMgr/DoScriptText | — | — |
| JustSummoned | method | Creature.Main/AI, Creature.Main/SelectAttackingTarget, CreatureAI/AttackStart, Object/GetEntry | — | — |
| DoSpecialAbility | method | Object/ToPlayer, Player.Main/IsGameMaster, ScriptedAI/DoSpawnCreature, ScriptMgr/DoScriptText, ThreatManager/getThreatList, Unit.Main/GetThreatManager, Unit.Main/IsAlive | — | — |
| UpdateDragonAI | method | Creature.Main/SelectAttackingTarget, CreatureAI/DoCastSpellIfCan, shared_Util/urand | — | — |
| npc_demented_druidAI | ctor | ScriptedAI/ScriptedAI | boss_dragon_of_nightmare/GetAI_npc_demented_druid | — |
| Reset#2 | method | shared_Util/urand | — | — |
| UpdateAI | method | Creature.Main/SelectAttackingTarget, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, shared_Util/urand, Unit.Main/GetVictim, Unit.Main/HasAura#2, Unit.Main/SelectHostileTarget | — | — |
