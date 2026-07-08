<!-- provenance: verbose -->
# boss_order_of_silver_hand

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# boss_order_of_silver_hand

## Purpose & Responsibilities

`boss_order_of_silver_hand` implements the shared AI for the five Order of the Silver Hand bosses (Gregor, Cathela, Nemas, Aelmar, Vicar) in Stratholme. It manages basic melee combat, defensive spell casting triggered by low health, and instance state synchronization for boss deaths. It also awards credit for Quest 9737 ("The Order of the Silver Hand") when specific instance conditions are met.

## Member-by-Member Behavior

### Initialization and State

**`boss_silver_hand_bossesAI` (Constructor)**
Retrieves the `ScriptedInstance` pointer from the creature and calls `Reset()`.

**`Reset`**
Sets `HolyLight_Timer` and `DivineShield_Timer` to 20,000 ms. Uses `Object::GetEntry()` to identify the boss and calls `InstanceData::SetData` with value `0` for the corresponding type constant (e.g., `TYPE_SH_GREGOR`), marking the boss as alive.

**`JustDied`**
Uses `Object::GetEntry()` to identify the deceased boss and calls `InstanceData::SetData` with value `2` (dead). If `InstanceData::GetData(TYPE_SH_QUEST)` is true and the killer is a player, it calls `Player::KilledMonsterCredit` with entry `SH_QUEST_CREDIT` (17915).

### Combat Loop

**`UpdateAI`**
Returns early if no hostile target exists. Manages two timed abilities:
1.  **Holy Light:** If `HolyLight_Timer` expires and health is <20%, casts `SPELL_HOLY_LIGHT` (25263) and resets the timer to 20,000 ms.
2.  **Divine Shield:** If `DivineShield_Timer` expires and health is <5%, casts `SPELL_DIVINE_SHIELD` (13874) and resets the timer to 40,000 ms.
Finally, calls `CreatureAI::DoMeleeAttackIfReady()`.

### Registration

**`GetAI_boss_silver_hand_bossesAI`**
Factory function returning a new `boss_silver_hand_bossesAI` instance.

**`AddSC_boss_order_of_silver_hand`**
Registers the script `"boss_silver_hand_bosses"` with `ScriptMgr`, linking it to the `GetAI` factory.

## Cross-Unit Boundaries

*   **`ScriptedInstance` / `InstanceData`:** Called in `Reset` and `JustDied` to update boss states (`0` for alive, `2` for dead) and read the `TYPE_SH_QUEST` flag. This synchronizes local boss events with the broader Stratholme instance logic.
*   **`Player` / `Main`:** Called in `JustDied` via `KilledMonsterCredit` to grant quest credit to the killing player.
*   **`CreatureAI` / `ScriptedAI`:** Inherits base AI functionality. `UpdateAI` calls `DoCast` and `DoMeleeAttackIfReady` for combat execution.

## Data Model

This unit does not access any database tables. State is managed entirely in-memory via `ScriptedInstance`.

## Notable Implementation Details

*   **Shared AI:** All five bosses use the same class; differentiation relies on `GetEntry()` checks in `Reset` and `JustDied`.
*   **Repetitive Casting:** If a boss remains below 20% HP, `Holy Light` casts every 20 seconds. If below 5% HP, `Divine Shield` casts every 40 seconds. There is no cooldown lockout beyond the timer reset.
*   **Quest Dependency:** Quest credit is only awarded if `TYPE_SH_QUEST` is set in the instance data. This flag is expected to be set by other scripts (e.g., related to the Eternal Flame event), as noted in the source comments.
*   **Incomplete Logic:** Source comments indicate missing features regarding Aurius and ghost summoning, which are not implemented here.

## Member Reference

**`boss_silver_hand_bossesAI`**
Constructor initializing instance data and calling `Reset`.

**`Reset`**
Resets timers to 20,000 ms and sets instance data to `0` (alive) for the specific boss entry.

**`JustDied`**
Sets instance data to `2` (dead) for the specific boss entry. Awards quest credit if `TYPE_SH_QUEST` is set and killer is a player.

**`UpdateAI`**
Handles combat ticks: casts `Holy Light` (<20% HP, 20s timer) and `Divine Shield` (<5% HP, 40s timer), then performs melee attacks.

**`GetAI_boss_silver_hand_bossesAI`**
Factory function creating `boss_silver_hand_bossesAI` instances.

**`AddSC_boss_order_of_silver_hand`**
Registers the script with `ScriptMgr`.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_order_of_silver_hand

*Source:* boss_order_of_silver_hand.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_silver_hand_bossesAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset | method | InstanceData/SetData, Object/GetEntry | — | — |
| JustDied | method | InstanceData/GetData, InstanceData/SetData, Object/GetEntry, Object/GetGUID, Object/GetTypeId, ObjectGuid/ObjectGuid#5, Player.Main/KilledMonsterCredit | — | — |
| UpdateAI | method | CreatureAI/DoCast, CreatureAI/DoMeleeAttackIfReady, Unit.Main/GetHealthPercent, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_boss_silver_hand_bossesAI | function | — | — | — |
| AddSC_boss_order_of_silver_hand | function | Script/Script, ScriptMgr/RegisterSelf | — | — |
