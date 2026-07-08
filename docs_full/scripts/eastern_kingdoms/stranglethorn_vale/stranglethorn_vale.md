<!-- provenance: verbose -->
# stranglethorn_vale

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# stranglethorn_vale

**Purpose & Responsibilities**  
`stranglethorn_vale.cpp` implements scripted behaviors for five entities in the Stranglethorn Vale zone: two quest-related NPCs (`mob_yenniku`, `npc_witch_doctor_unbagwa`), two ambient NPCs (`mob_assistant_kryll`, `npc_pats_hellfire_guy`), and one interactive game object (`go_transpolyporter`). The file provides AI classes, factory functions, and a registration routine (`AddSC_stranglethorn_vale`) that hooks these scripts into the server’s script manager. It contains no database access.

---

## Member-by-Member Behavior

### `mob_yenniku` — Quest 592 Boss
- **`mob_yennikuAI` (ctor)**: Initializes the AI, sets `bReset = false`, and calls `Reset`. Inherits from `ScriptedAI`.
- **`Reset#2`**: Clears the stun emote state (`EMOTE_STATE_NONE`) and resets `Reset_Timer` to 0.
- **`SpellHit`**: If hit by spell 3607 from a player with incomplete quest 592, the creature enters a temporary neutral state: sets emote to `EMOTE_STATE_STUN`, stops combat, deletes the threat list, changes faction template to 83 (Horde generic), sets `bReset = true`, and starts a 60-second timer.
- **`Aggro`**: Empty override; suppresses default aggro handling during the reset window.
- **`UpdateAI#2`**: If `bReset` is true, decrements `Reset_Timer`. When expired, calls `EnterEvadeMode()` (from `ScriptedAI`), resets `bReset`, and restores faction to 28 (Troll, Bloodscalp). Otherwise, performs standard melee attacks via `DoMeleeAttackIfReady()` if a hostile target exists.
- **`GetAI_mob_yenniku`**: Factory function returning a new `mob_yennikuAI` instance.

### `mob_assistant_kryll` — Ambient Recruiter NPC
- **`mob_assistant_kryll` (ctor)**: Calls `Reset` to initialize timers.
- **`Reset`**: Sets `Speach_Timer` to 360,000 ms (6 minutes).
- **`UpdateAI`**: On timer expiry, picks one of three recruitment messages at random using `urand(0, 2)` and broadcasts it via `MonsterSay`. Reschedules the next message between 15–40 minutes.
- **`GetAI_mob_assistant_kryll`**: Factory function.

> **Note**: In `AddSC_stranglethorn_vale`, the registration for `mob_assistant_kryll` is commented out, meaning this NPC is **not active** in the current build.

### `go_transpolyporter` — Teleportation Game Object
- **`go_transpolyporterAI` (ctor)**: Standard `GameObjectAI` initialization.
- **`OnUse`**: Intercepts use attempts. If the user is a player who already possesses item ID 9173 (quantity ≥ 1), usage is blocked (`return false`). Otherwise, allows normal processing (`return true`).
- **`GetAIgo_transpolyporter`**: Factory function.

### `npc_pats_hellfire_guy` — Visual Effect Spawner
- **`npc_pats_hellfire_guyAI` (ctor)**: Calls `Reset`.
- **`Reset#3`**: Sets `m_uiCastDelay` to 2000 ms.
- **`UpdateAI#3`**: After a 2-second delay, casts spell ID 24207 (`SPELL_HELLFIRE_CAST_VISUAL`) on itself. The delay is one-shot (`m_uiCastDelay = 0` afterward), so the visual effect plays once upon spawn.
- **`GetAI_npc_pats_hellfire_guy`**: Factory function.

### `npc_witch_doctor_unbagwa` — Quest 349 Event Boss
- **`npc_witch_doctor_unbagwaAI` (ctor)**: Calls both `Reset` and `ResetCreature` to initialize state.
- **`Reset#4`**: Empty override.
- **`ResetCreature`**: Resets event flags (`m_bStartEvent`, `m_bResetEvent`), wave counter (`m_uiWaveCount = 1`), attacker count (`m_uiAttackersCount = 0`), and mob wave timer (`10000 ms`). Restores questgiver flag and clears temporary faction.
- **`SummonedCreatureDespawn`**: Called when a summoned minion despawns. If the event is active and the creature is dead, it marks `m_bResetEvent = true`, decrements `m_uiAttackersCount`, and if no attackers remain, calls `ResetCreature`.
- **`SummonedCreatureJustDied`**: Called when a summoned minion dies. Decrements `m_uiAttackersCount`. If no attackers remain and either the event is marked for reset or all waves are complete (`m_uiWaveCount > MAX_WAVE_COUNT`), calls `ResetCreature`.
- **`JustSummoned`**: When a minion is summoned during the event, adds 5.0 threat to the boss and commands the minion to attack the boss immediately.
- **`StartEvent`**: Triggered externally by `QuestRewarded_npc_witch_doctor_unbagwa`. Removes questgiver flag, sets temporary neutral faction, and activates the event (`m_bStartEvent = true`).
- **`UpdateAI#4`**: If the event is active and no attackers are currently alive, waits until `m_uiMobWaveTimer` expires, then spawns a wave:
  - Wave 1: 3 Enraged Silverback Gorillas (ID 1511).
  - Wave 2: 5 gorillas, last one replaced by Konda (ID 1516).
  - Wave 3: 6 gorillas, last one replaced by Mokk the Savage (ID 1514).
  Summons occur at fixed coordinates with ±3 random offset, lasting 3 minutes or until death. Increments wave counter and resets the 10-second wave timer. Performs standard melee attacks if a victim exists.
- **`QuestRewarded_npc_witch_doctor_unbagwa`**: Hook called when a player completes a quest from this NPC. If the quest is ID 349, it retrieves the AI pointer and calls `StartEvent()`, initiating the boss fight.
- **`GetAI_npc_witch_doctor_unbagwa`**: Factory function.

### Script Registration
- **`AddSC_stranglethorn_vale`**: Registers all five scripts with the `ScriptMgr`. Note that `mob_assistant_kryll` is registered but its block is commented out, effectively disabling it. Each script is assigned its respective factory function and, for `npc_witch_doctor_unbagwa`, the quest reward hook.

---

## Cross-Unit Boundaries

| Member | Direction | Other Unit | Collaboration Detail |
|--------|-----------|------------|----------------------|
| `mob_yennikuAI` (ctor) | Calls | `ScriptedAI/ScriptedAI` | Inherits base AI functionality. |
| `Reset#2` | Calls | `WorldObject.Object/SetUInt32Value` | Clears emote state. |
| `SpellHit` | Calls | `Object/GetTypeId`, `Player.Main/GetQuestStatus`, `Unit.Main/CombatStop`, `Unit.Main/DeleteThreatList`, `Unit.Main/SetFactionTemplateId`, `WorldObject.Object/SetUInt32Value` | Validates player caster, checks quest status, stops combat, clears threats, changes faction, sets emote. |
| `UpdateAI#2` | Calls | `CreatureAI/DoMeleeAttackIfReady`, `ScriptedAI/EnterEvadeMode`, `Unit.Main/GetVictim`, `Unit.Main/SelectHostileTarget`, `Unit.Main/SetFactionTemplateId` | Handles melee attacks, evasion after reset timer, and faction restoration. |
| `mob_assistant_kryll` (ctor) | Calls | `ScriptedAI/ScriptedAI` | Inherits base AI. |
| `UpdateAI` (Kryll) | Calls | `shared_Util/urand`, `WorldObject.Object/MonsterSay` | Randomizes speech selection and broadcasts text. |
| `go_transpolyporterAI` (ctor) | Calls | `GameObjectAI/GameObjectAI` | Inherits GO AI base. |
| `OnUse` | Calls | `Object/IsPlayer`, `Player.Main/HasItemCount` | Checks if user is a player and whether they hold item 9173. |
| `npc_pats_hellfire_guyAI` (ctor) | Calls | `ScriptedAI/ScriptedAI` | Inherits base AI. |
| `UpdateAI#3` | Calls | `SpellCaster/CastSpell#2` | Casts visual spell 24207. |
| `npc_witch_doctor_unbagwaAI` (ctor) | Calls | `ScriptedAI/ScriptedAI` | Inherits base AI. |
| `ResetCreature` | Calls | `Creature.Main/ClearTemporaryFaction`, `WorldObject.Object/SetFlag` | Resets faction and questgiver flag. |
| `SummonedCreatureDespawn` | Calls | `Unit.Main/IsAlive` | Checks if despawned creature was alive. |
| `JustSummoned` | Calls | `Creature.Main/AI`, `CreatureAI/AttackStart`, `Unit.Main/AddThreat` | Commands summoned minions to attack the boss and adds threat. |
| `StartEvent` | Calls | `Creature.Main/SetFactionTemporary`, `WorldObject.Object/RemoveFlag` | Activates event state, removes questgiver flag, sets neutral faction. |
| `UpdateAI#4` | Calls | `CreatureAI/DoMeleeAttackIfReady`, `shared_Util/frand`, `Unit.Main/GetVictim`, `Unit.Main/SelectHostileTarget`, `WorldObject.Object/SummonCreature#2` | Spawns minions with random offsets, handles melee attacks. |
| `QuestRewarded_npc_witch_doctor_unbagwa` | Calls | `Creature.Main/AI`, `QuestDef/GetQuestId` | Retrieves AI pointer and checks quest ID to trigger event. |
| `AddSC_stranglethorn_vale` | Calls | `Script/Script`, `ScriptMgr/RegisterSelf` | Registers scripts with the global manager. |
| `AddSC_stranglethorn_vale` | Called by | `ScriptLoader/AddScripts` | Invoked during server startup to load all scripts. |

---

## Data Model

This unit does **not** interact with any database tables. All logic is driven by in-memory state, quest IDs, spell IDs, and creature entries defined in constants or passed at runtime.

---

## Notable Implementation Details

1. **`mob_yenniku` Reset Logic**:  
   The `SpellHit` handler uses `DeleteThreatList()` to forcibly remove all aggro. This is unusual and may cause instability if the creature is still in combat; however, `CombatStop()` is called first. The 60-second neutral period is enforced via `bReset` and `Reset_Timer`. After evasion, faction is restored to 28.

2. **`mob_assistant_kryll` Disabled**:  
   Despite being fully implemented, its registration in `AddSC_stranglethorn_vale` is commented out. It will not load unless manually enabled.

3. **`go_transpolyporter` Item Check**:  
   Blocks usage if the player holds item 9173. This likely prevents duplicate teleportation rewards. The check is strict (`HasItemCount(9173, 1, false)`), meaning even one copy blocks use.

4. **`npc_pats_hellfire_guy` One-Shot Visual**:  
   The spell cast happens once after a 2-second delay. The timer is zeroed afterward, so no further casts occur. This is purely cosmetic.

5. **`npc_witch_doctor_unbagwa` Wave Mechanics**:  
   - Waves are spaced 10 seconds apart.
   - Minions are summoned with a 3-minute lifetime or until death.
   - The final minion in waves 2 and 3 is replaced by elite mobs (Konda, Mokk).
   - Event ends when all minions die or all waves are spawned.
   - Threat management: Summoned minions are given 5.0 threat to ensure they focus the boss.

6. **Quest Hook Integration**:  
   `QuestRewarded_npc_witch_doctor_unbagwa` is registered as a quest reward callback. It triggers the boss event only for quest 349. This decouples the event start from the NPC’s own AI loop.

7. **No Database Dependencies**:  
   All identifiers (quest IDs, spell IDs, creature entries) are hardcoded. No dynamic data loading occurs.

---

## Member Reference

**mob_yennikuAI** (ctor): Initializes the AI for Yenniku, sets `bReset = false`, and calls `Reset`. Inherits from `ScriptedAI`.

**Reset#2**: Clears emote state to `EMOTE_STATE_NONE` and resets `Reset_Timer` to 0.

**SpellHit**: If hit by spell 3607 from a player with incomplete quest 592, stuns the creature, stops combat, deletes threat list, sets faction to 83, and starts a 60-second reset timer.

**Aggro**: Empty override; suppresses default aggro behavior during reset.

**UpdateAI#2**: If resetting, waits for timer expiry then evades and restores faction to 28. Otherwise, performs melee attacks if a target exists.

**GetAI_mob_yenniku**: Factory function creating a new `mob_yennikuAI` instance.

**mob_assistant_kryll** (ctor): Initializes the AI for Assistant Kryll and calls `Reset`.

**Reset**: Sets `Speach_Timer` to 360,000 ms (6 minutes).

**UpdateAI**: Every time the timer expires, randomly selects one of three recruitment messages and broadcasts it via `MonsterSay`. Reschedules next message between 15–40 minutes.

**GetAI_mob_assistant_kryll**: Factory function creating a new `mob_assistant_kryll` instance. (Registration is commented out.)

**go_transpolyporterAI** (ctor): Standard `GameObjectAI` initialization.

**OnUse**: Blocks usage if the player user holds item 9173. Otherwise, allows normal processing.

**GetAIgo_transpolyporter**: Factory function creating a new `go_transpolyporterAI` instance.

**npc_pats_hellfire_guyAI** (ctor): Initializes the AI and calls `Reset`.

**Reset#3**: Sets `m_uiCastDelay` to 2000 ms.

**UpdateAI#3**: After 2 seconds, casts spell 24207 on itself. Timer is zeroed afterward.

**GetAI_npc_pats_hellfire_guy**: Factory function creating a new `npc_pats_hellfire_guyAI` instance.

**npc_witch_doctor_unbagwaAI** (ctor): Calls `Reset` and `ResetCreature` to initialize state.

**Reset#4**: Empty override.

**ResetCreature**: Resets event flags, wave counter, attacker count, and mob wave timer. Restores questgiver flag and clears temporary faction.

**SummonedCreatureDespawn**: If event is active and creature is dead, marks reset flag, decrements attacker count, and resets if no attackers remain.

**SummonedCreatureJustDied**: Decrements attacker count. If no attackers remain and event is marked for reset or all waves are done, resets the event.

**JustSummoned**: Adds 5.0 threat to the boss and commands the summoned minion to attack the boss.

**StartEvent**: Removes questgiver flag, sets temporary neutral faction, and activates the event.

**UpdateAI#4**: If event is active and no attackers are alive, spawns a wave of minions after a 10-second delay. Wave composition varies by wave number. Performs melee attacks if a victim exists.

**QuestRewarded_npc_witch_doctor_unbagwa**: If quest 349 is rewarded, retrieves the AI pointer and calls `StartEvent()` to initiate the boss fight.

**GetAI_npc_witch_doctor_unbagwa**: Factory function creating a new `npc_witch_doctor_unbagwaAI` instance.

**AddSC_stranglethorn_vale**: Registers all five scripts with the `ScriptMgr`. `mob_assistant_kryll` is commented out and thus inactive.

---

<!-- machine-true, projected from graph.json -->

## Map — stranglethorn_vale

*Source:* stranglethorn_vale.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| mob_yennikuAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#2 | method | WorldObject.Object/SetUInt32Value | — | — |
| SpellHit | method | Object/GetTypeId, Player.Main/GetQuestStatus, Unit.Main/CombatStop, Unit.Main/DeleteThreatList, Unit.Main/SetFactionTemplateId, WorldObject.Object/SetUInt32Value | — | — |
| Aggro | method | — | — | — |
| UpdateAI#2 | method | CreatureAI/DoMeleeAttackIfReady, ScriptedAI/EnterEvadeMode, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget, Unit.Main/SetFactionTemplateId | — | — |
| GetAI_mob_yenniku | function | — | — | — |
| mob_assistant_kryll | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset | method | — | — | — |
| UpdateAI | method | shared_Util/urand, WorldObject.Object/MonsterSay | — | — |
| GetAI_mob_assistant_kryll | function | — | — | — |
| go_transpolyporterAI | ctor | GameObjectAI/GameObjectAI | — | — |
| OnUse | method | Object/IsPlayer, Player.Main/HasItemCount | — | — |
| GetAIgo_transpolyporter | function | — | — | — |
| npc_pats_hellfire_guyAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#3 | method | — | — | — |
| UpdateAI#3 | method | SpellCaster/CastSpell#2 | — | — |
| GetAI_npc_pats_hellfire_guy | function | — | — | — |
| npc_witch_doctor_unbagwaAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#4 | method | — | — | — |
| ResetCreature | method | Creature.Main/ClearTemporaryFaction, WorldObject.Object/SetFlag | — | — |
| SummonedCreatureDespawn | method | Unit.Main/IsAlive | — | — |
| SummonedCreatureJustDied | method | — | — | — |
| JustSummoned | method | Creature.Main/AI, CreatureAI/AttackStart, Unit.Main/AddThreat | — | — |
| StartEvent | method | Creature.Main/SetFactionTemporary, WorldObject.Object/RemoveFlag | — | — |
| UpdateAI#4 | method | CreatureAI/DoMeleeAttackIfReady, shared_Util/frand, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget, WorldObject.Object/SummonCreature#2 | — | — |
| QuestRewarded_npc_witch_doctor_unbagwa | function | Creature.Main/AI, QuestDef/GetQuestId | — | — |
| GetAI_npc_witch_doctor_unbagwa | function | — | — | — |
| AddSC_stranglethorn_vale | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
