<!-- provenance: failed-members -->
# boss_dathrohan_balnazzar

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# boss_dathrohan_balnazzar

## Purpose & Responsibilities

`boss_dathrohan_balnazzar` implements the combat artificial intelligence for the dual-phase boss encounter **Dathrohan/Balnazzar** in the Stratholme instance. The unit manages a single `Creature` object that begins the encounter as Dathrohan (NPC entry 10812) and transforms into Balnazzar (NPC entry 10813) when Dathrohan’s health drops below 40%.

The AI handles two distinct spell rotations:
1.  **Dathrohan Phase:** Uses Crusader-themed spells (*Crusader's Hammer*, *Crusader Strike*, *Holy Strike*) and *Mind Blast*.
2.  **Balnazzar Phase:** After a brief transformation stun, uses Shadow/Fel-themed spells (*Shadow Shock*, *Psychic Scream*, *Deep Sleep*, *Mind Control*).

Key mechanical responsibilities include:
*   **Phase Transition:** Detecting low health, interrupting current casts, casting a transform spell that restores health/mana and stuns the boss, and swapping the creature entry.
*   **Threat Management for CC Spells:** When casting *Deep Sleep* or *Mind Control*, the AI temporarily removes 100% of the target's threat to prevent aggro resets or pulls while the player is incapacitated. It tracks the original threat value and GUID to restore the threat state once the spell effect expires.
*   **Death Summons:** Upon death, the boss spawns a large number of skeletal minions (Berserkers and Guardians) at predefined coordinates around the arena.

## Member-by-Member Behavior

### Initialization and Lifecycle

**`boss_dathrohan_balnazzarAI`**
Constructor that initializes the AI instance by calling `Reset()` to set initial timer values and state flags. It inherits from `ScriptedAI`.

**`Reset`**
Resets all internal timers to their base intervals and clears state flags (`m_bTransformed`, `MCPlayerGuid`, etc.). Crucially, it checks the current creature entry: if the creature is currently Balnazzar (`NPC_BALNAZZAR`), it reverts the entry back to Dathrohan (`NPC_DATHROHAN`). This ensures that if the boss is reset during the second phase, it returns to its starting form.

**`JustDied`**
Triggered when the boss dies. It plays the death say (`SAY_DATHROHAN_DEATH`) and then iterates through a static array of 32 summon points (`m_aSummonPoint`). For each point, it randomly summons either a `NPC_SKEL_BERSERKER` or `NPC_SKEL_GUARDIAN` using `TEMPSUMMON_DEAD_DESPAWN` with a duration of one hour. This creates a "zombie wave" event upon the boss's defeat.

**`Aggro`**
Plays the aggro say (`SAY_DATHROHAN_AGGRO`) when the boss first enters combat.

### Combat Logic

**`UpdateAI`**
The core game loop, executed every tick. It first verifies that the boss has a valid victim. The logic branches based on the `m_bTransformed` flag.

**Dathrohan Phase (`!m_bTransformed`):**
*   **Mind Blast:** Casts on the victim every 15–20 seconds.
*   **Crusader's Hammer:** An AoE stun cast on self/everyone nearby every 12 seconds.
*   **Crusader Strike:** Casts on the victim every 15 seconds.
*   **Holy Strike:** Casts on the victim every 15 seconds.
*   **Transformation Check:** If health drops below 40%, it interrupts any non-melee spell, casts `SPELL_BALNAZZARTRANSFORM` (which restores HP/Mana and applies a stun), updates the creature entry to Balnazzar, sets a 4-second delay timer, sets `m_bTransformed` to true, and returns early to pause other abilities during the transition.

**Balnazzar Phase (`m_bTransformed`):**
*   **Transform Delay:** Waits 4 seconds before playing the transform say (`SAY_DATHROHAN_TRANSFORM`) and enabling further abilities.
*   **Threat Restoration (Mind Control):** If a player was previously Mind Controlled (`MCPlayerGuid` is set), it checks if the aura is gone. If so, it restores the player's threat by removing the -100% modifier and adding the stored `MCPlayerAggro` back directly.
*   **Threat Restoration (Sleep):** Similar logic for players who were Deep Slept (`SleepPlayerGuid`).
*   **Mind Blast:** Continues to cast on the victim every 15–20 seconds.
*   **Shadow Shock:** Casts on the victim every 11 seconds.
*   **Psychic Scream:** An AoE fear cast every 20 seconds.
*   **Deep Sleep:** Selects a random hostile target. If the target is not already sleeping, it stores their GUID and current threat, reduces their threat by 100%, and casts the sleep spell. The timer resets to 15 seconds.
*   **Mind Control:** Selects the top aggro target. If the target is not sleeping, it stores their GUID and threat, reduces their threat by 100%, and casts Mind Control. The timer resets to 25–30 seconds.
*   **Melee:** Performs melee attacks if ready.

### Registration

**`GetAI_boss_dathrohan_balnazzar`**
Factory function that allocates and returns a new instance of `boss_dathrohan_balnazzarAI`.

**`AddSC_boss_dathrohan_balnazzar`**
Registers the script with the engine. It creates a `Script` object, assigns the name `"boss_dathrohan_balnazzar"`, links the factory function, and registers it with the `ScriptMgr`. This function is called by `ScriptLoader::AddScripts` during server startup.

## Cross-Unit Boundaries

*   **`ScriptedAI`**: The base class providing the framework for timers and basic AI hooks.
*   **`Creature.Main`**: Used to update the creature's entry (model/stats) during reset and transformation, select targets, and summon minions.
*   **`ScriptMgr`**: Used to play scripted text events (says) via `DoScriptText`.
*   **`shared_Util`**: Provides `urand` for randomizing timer intervals and summon types.
*   **`WorldObject.Object`**: Used to summon creatures at specific coordinates.
*   **`CreatureAI`**: Base methods for casting spells (`DoCastSpellIfCan`) and melee attacks (`DoMeleeAttackIfReady`).
*   **`ThreatManager`**: Critical for the CC mechanics. `modifyThreatPercent` is used to drop threat to zero (via -100%) when applying Sleep/Mind Control, preventing the boss from attacking the incapacitated player. `addThreatDirectly` restores the exact threat value when the effect ends. `getThreat` captures the current threat value before modification.
*   **`Unit.Main`**: Used to check health percent, verify auras (`HasAura`), get the victim, and select targets.
*   **`SpellCaster`**: Used to check if a spell is currently being cast (`IsNonMeleeSpellCasted`) and to interrupt it (`InterruptNonMeleeSpells`) during the phase transition.
*   **`Map.Main`**: Used to retrieve `Player` objects from their GUIDs to verify if CC auras have expired.

## Data Model

This unit does not interact with any database tables. All configuration (spell IDs, NPC entries, coordinates, timers) is hardcoded in the source file.

## Notable Implementation Details

1.  **Threat Manipulation Strategy:** The AI does not simply remove the player from the threat list. Instead, it modifies the threat percentage by -100% (effectively zeroing it relative to the highest threat) and stores the absolute threat value. When the aura expires, it reverses the percentage change and adds the stored absolute value back. This preserves the player's position in the threat hierarchy relative to other players, preventing sudden aggro spikes or drops that might occur if the player were removed and re-added to the threat list.
2.  **Phase Transition Interrupt:** Before transforming, the AI explicitly checks `IsNonMeleeSpellCasted` and calls `InterruptNonMeleeSpells`. This prevents the boss from finishing a long-cast spell (like Mind Blast) after the transformation has begun, ensuring a clean state change.
3.  **Static Summon Array:** The summon locations for the death event are hardcoded in `m_aSummonPoint`. There are 32 locations arranged in groups (G1-G8), likely corresponding to specific zones around the boss arena. The randomness is only in the *type* of skeleton summoned, not the location.
4.  **Entry Swapping:** The transformation relies on `UpdateEntry`. This changes the creature's visual model and stat template instantly. The `Reset` function ensures that if the instance is reset while in Balnazzar form, it reverts to Dathrohan, maintaining consistency for subsequent attempts.
5.  **CC Target Validation:** Both *Deep Sleep* and *Mind Control* check if the target already has the respective aura before casting. Additionally, *Mind Control* explicitly checks that the target is not sleeping (`!pTarget->HasAura(SPELL_SLEEP)`), preventing the boss from trying to mind control a player who is already asleep.

## Member Reference

**`boss_dathrohan_balnazzarAI`**
Constructor that initializes the AI instance and calls `Reset()` to set default timers and state. Inherits from `ScriptedAI`.

**`Reset`**
Resets all ability timers to their base values, clears CC tracking variables, and ensures the creature entry is set to Dathrohan (`NPC_DATHROHAN`) if it was previously Balnazzar.

**`JustDied`**
Plays the death say and summons 32 skeletal minions (randomly Berserkers or Guardians) at predefined coordinates around the arena using `TEMPSUMMON_DEAD_DESPAWN`.

**`Aggro`**
Plays the aggro say (`SAY_DATHROHAN_AGGRO`) when combat begins.

**`UpdateAI`**
Main logic loop. Handles two phases:
1.  **Dathrohan:** Casts Crusader's Hammer, Crusader Strike, Holy Strike, and Mind Blast. Transforms to Balnazzar at <40% HP.
2.  **Balnazzar:** After a 4s delay, casts Shadow Shock, Psychic Scream, Mind Blast, Deep Sleep, and Mind Control. Manages threat reduction/restoration for CC'd players.

**`GetAI_boss_dathrohan_balnazzar`**
Factory function that creates and returns a new `boss_dathrohan_balnazzarAI` instance for a given `Creature`.

**`AddSC_boss_dathrohan_balnazzar`**
Registers the script with the engine by creating a `Script` object, setting its name and AI factory function, and registering it with `ScriptMgr`. Called by `ScriptLoader`.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_dathrohan_balnazzar

*Source:* boss_dathrohan_balnazzar.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_dathrohan_balnazzarAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset | method | Creature.Main/UpdateEntry, Object/GetEntry | — | — |
| JustDied | method | ScriptMgr/DoScriptText, shared_Util/urand, WorldObject.Object/SummonCreature#2 | — | — |
| Aggro | method | ScriptMgr/DoScriptText | — | — |
| UpdateAI | method | Creature.Main/SelectAttackingTarget, Creature.Main/UpdateEntry, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, Map.Main/GetPlayer, Object/GetGUID, ObjectGuid/ObjectGuid#5, ScriptMgr/DoScriptText, shared_Util/urand, SpellCaster/InterruptNonMeleeSpells, SpellCaster/IsNonMeleeSpellCasted, ThreatManager/addThreatDirectly, ThreatManager/getThreat, ThreatManager/modifyThreatPercent#2, Unit.Main/GetHealthPercent, Unit.Main/GetThreatManager, Unit.Main/GetVictim, Unit.Main/HasAura#2, Unit.Main/SelectHostileTarget, WorldObject.Object/GetMap | — | — |
| GetAI_boss_dathrohan_balnazzar | function | — | — | — |
| AddSC_boss_dathrohan_balnazzar | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |

---

<!-- verify: failed-members | invented: Balnazzar:, Dathrohan: -->
