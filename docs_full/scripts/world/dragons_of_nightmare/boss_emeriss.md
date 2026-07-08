<!-- provenance: verbose -->
# boss_emeriss

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`boss_emerissAI` implements the combat logic for **Emeriss**, a specific variant of the "Dragon of Nightmare" encounter. It inherits from `boss_dragon_of_nightmareAI` to reuse base dragon mechanics while adding Emeriss’s unique spells (`Emeriss Aura`, `Volatile Infection`, `Corruption of the Earth`) and dialogue. No database tables are accessed; all state is held in memory.

## Member-by-Member Behavior

### Initialization and State

**`boss_emerissAI`**
Constructs the AI, delegating to `boss_dragon_of_nightmareAI`’s constructor, then immediately calls `Reset()` to initialize timers.

**`Reset`**
Resets encounter state:
1.  Calls `boss_dragon_of_nightmareAI::Reset()`.
2.  Sets `m_uiEmerissAuraTimer` to 0 (aura casts immediately on next tick).
3.  Sets `m_uiVolatileInfectionTimer` to a random value between 11,000 and 13,000 ms via `shared_Util::urand`, staggering the first infection cast.

### Combat Triggers

**`Aggro`**
Triggered on engagement:
1.  Calls `boss_dragon_of_nightmareAI::Aggro()`.
2.  Broadcasts aggro text/sound `SAY_EMERISS_AGGRO` (10885) via `ScriptMgr::DoScriptText`.

### Ability Logic

**`DoSpecialAbility`**
Called by the parent class to execute a special ability:
1.  Attempts to cast `SPELL_CORRUPTION_OF_THE_EARTH` (24910) on the boss (`m_creature`) using `CreatureAI::DoCastSpellIfCan`.
2.  On success, broadcasts `SAY_CAST_CORRUPTION` (10884) and returns `true`.
3.  Returns `false` if the cast fails.

### Main Update Loop

**`UpdateDragonAI`**
Processes time delta `uiDiff` for two abilities:

1.  **Emeriss Aura:**
    *   If `m_uiEmerissAuraTimer` expires, casts `SPELL_EMERISS_AURA` (24906) on the boss. On success, resets timer to 10,000 ms.
    *   Otherwise, decrements timer by `uiDiff`.

2.  **Volatile Infection:**
    *   If `m_uiVolatileInfectionTimer` expires, selects a random non-GM player target via `Creature::SelectAttackingTarget` (`ATTACKING_TARGET_RANDOM`, `SELECT_FLAG_PLAYER_NOT_GM`).
    *   If a target exists, casts `SPELL_VOLATILE_INFECTION` (24928) with `CF_AURA_NOT_PRESENT` (prevents recasting if aura exists). On success, resets timer to a random 10,000–16,000 ms.
    *   Otherwise, decrements timer by `uiDiff`.

Returns `true` to continue the AI loop.

## Cross-Unit Boundaries

*   **`boss_dragon_of_nightmare`**: Inherits from `boss_dragon_of_nightmareAI`. Calls `Reset` and `Aggro` from the parent. Instantiated by `boss_dragon_of_nightmare::GetAI_boss_dragon_of_nightmare`.
*   **`CreatureAI` / `Creature`**: Calls `DoCastSpellIfCan` for spell management and `SelectAttackingTarget` for target selection.
*   **`ScriptMgr`**: Calls `DoScriptText` for dialogue/sound playback.
*   **`shared_Util`**: Calls `urand` for random timer generation.

## Notable Implementation Details

*   **Immediate Aura:** `m_uiEmerissAuraTimer` starts at 0, ensuring the aura is applied on the very first update tick after reset.
*   **GM Exclusion:** `Volatile Infection` explicitly skips Game Masters (`SELECT_FLAG_PLAYER_NOT_GM`).
*   **Aura Check:** `Volatile Infection` uses `CF_AURA_NOT_PRESENT` to avoid redundant casts on already-affected targets.
*   **Hardcoded Timers:** All cooldowns are hardcoded constants or ranges; balancing requires code changes.

## Member Reference

**`boss_emerissAI`**
Constructor that initializes the AI by calling the parent constructor and then invoking `Reset()` to initialize timers.

**`Reset`**
Resets the AI state: calls parent `Reset`, sets `m_uiEmerissAuraTimer` to 0, and randomizes `m_uiVolatileInfectionTimer` between 11,000 and 13,000 ms.

**`Aggro`**
Handles aggro event: calls parent `Aggro` and plays the aggro sound/text (`SAY_EMERISS_AGGRO`).

**`DoSpecialAbility`**
Attempts to cast `SPELL_CORRUPTION_OF_THE_EARTH` on the boss. If successful, plays the cast sound (`SAY_CAST_CORRUPTION`) and returns `true`; otherwise returns `false`.

**`UpdateDragonAI`**
Main update loop. Manages `m_uiEmerissAuraTimer` (casts `SPELL_EMERISS_AURA` on self every 10s) and `m_uiVolatileInfectionTimer` (casts `SPELL_VOLATILE_INFECTION` on a random non-GM player every 10-16s, if they don't already have the aura). Returns `true`.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_emeriss

*Source:* boss_emeriss.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_emerissAI | ctor | boss_dragon_of_nightmare/boss_dragon_of_nightmareAI | boss_dragon_of_nightmare/GetAI_boss_dragon_of_nightmare | — |
| Reset | method | boss_dragon_of_nightmare/Reset, shared_Util/urand | — | — |
| Aggro | method | boss_dragon_of_nightmare/Aggro, ScriptMgr/DoScriptText | — | — |
| DoSpecialAbility | method | CreatureAI/DoCastSpellIfCan, ScriptMgr/DoScriptText | — | — |
| UpdateDragonAI | method | Creature.Main/SelectAttackingTarget, CreatureAI/DoCastSpellIfCan, shared_Util/urand | — | — |
