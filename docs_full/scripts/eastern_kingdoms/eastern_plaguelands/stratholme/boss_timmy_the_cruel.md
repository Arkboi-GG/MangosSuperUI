# boss_timmy_the_cruel

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# boss_timmy_the_cruel

This translation unit implements the scripted artificial intelligence for two NPCs in the Stratholme instance: **Timmy the Cruel** (`boss_timmy_the_cruel`) and the **Crimson Guardsman** (`npc_crimson_guardsman`). It defines two `ScriptedAI` subclasses, their factory functions, and the registration routine `AddSC_boss_timmy_the_cruel`.

## Member-by-Member Behavior

### Timmy the Cruel AI (`boss_timmy_the_cruelAI`)

*   **boss_timmy_the_cruelAI**: Constructor initializes `ScriptedAI` and calls `Reset`.
*   **Reset**: Sets `m_uiRavenousClawTimer` to 7000 ms.
*   **UpdateAI**: Combat loop. Returns if no target. Casts `SPELL_RAVENOUSCLAW` (17470) on victim when timer expires (reset to 12000 ms). If health < 10% and no `SPELL_ENRAGE` (8599) aura, casts Enrage on self using `CastSpell(..., true)` to force application. Calls `DoMeleeAttackIfReady`.
*   **CorpseRemoved**: Calls `DeleteLater` to remove the creature object from memory.
*   **GetAI_boss_timmy_the_cruel**: Factory returning a new `boss_timmy_the_cruelAI`.

### Crimson Guardsman AI (`npc_crimson_guardsmanAI`)

*   **npc_crimson_guardsmanAI**: Constructor initializes `ScriptedAI`, calls `Reset`, and sets `m_bIsTimmySpawner` to true if `GetDBTableGUIDLow()` equals 54070.
*   **Reset#2**: Resets `m_bHasFled` to false; sets timers for Disarm (6000 ms), Shield Bash (4000 ms), and Shield Charge (1000 ms).
*   **JustDied**: If `m_bIsTimmySpawner` is true: summons Timmy (`TIMMY_ENTRY`, 10808) at fixed coordinates with `TEMPSUMMON_MANUAL_DESPAWN`; makes Timmy yell `SAY_SPAWN` (6150); sets Timmy’s respawn time to 9999999 s; deletes the guardsman via `DeleteLater`.
*   **UpdateAI#2**: Combat loop. Returns if no target. If health < 15% and not yet fled, sets `m_bHasFled` true, calls `DoFlee`, and returns (skipping abilities). Otherwise, casts Disarm (6713, 15s CD), Shield Bash (11972, 8s CD), and Shield Charge (15749, 12s CD) on victim when timers expire. Calls `DoMeleeAttackIfReady`.
*   **GetAI_npc_crimson_guardsman**: Factory returning a new `npc_crimson_guardsmanAI`.

### Registration

*   **AddSC_boss_timmy_the_cruel**: Creates `Script` objects for both NPCs, assigns their `GetAI` pointers, and calls `RegisterSelf`. Called by `ScriptLoader::AddScripts`.

## Cross-Unit Boundaries

*   **ScriptedAI**: Base class for both AIs; provides `DoCastSpellIfCan`, `DoMeleeAttackIfReady`, and lifecycle hooks.
*   **CreatureAI / Unit.Main**: `UpdateAI` methods use `SelectHostileTarget`, `GetVictim`, `GetHealthPercent`, `HasAura`, and `CastSpell` to manage combat state and actions.
*   **WorldObject.Object**: `CorpseRemoved` and `JustDied` use `DeleteLater` for cleanup. `JustDied` uses `MonsterYell`, `SummonCreature`, and `SetRespawnTime` to spawn and configure Timmy.
*   **ScriptMgr / Script**: `AddSC_boss_timmy_the_cruel` registers scripts via `Script::RegisterSelf`, integrating them into the server’s script manager.

## Data Model

No database tables are accessed directly. The unit relies on hardcoded spell/creature IDs and runtime API calls. The "spawner" guardsman is identified by comparing its `GetDBTableGUIDLow()` against the constant `54070`, which corresponds to a specific entry in the `creature` table, but no SQL is executed.

## Notable Implementation Details

*   **Forced Enrage**: Timmy’s Enrage is cast with `true` flag, bypassing standard cast checks to ensure it triggers at 10% health.
*   **Flee Interrupt**: Guardsmen stop all ability usage immediately upon fleeing (<15% HP), as `UpdateAI#2` returns after `DoFlee`.
*   **Manual Despawn**: Timmy is summoned with `TEMPSUMMON_MANUAL_DESPAWN` and a near-infinite respawn time, requiring manual intervention or death to remove him.
*   **Hardcoded Spawner ID**: The logic depends on a specific DB GUID (54070); changing this in the database breaks the spawn mechanic.

## Member Reference

*   **boss_timmy_the_cruelAI**: Constructor; initializes base class and calls `Reset`.
*   **Reset**: Resets `m_uiRavenousClawTimer` to 7000 ms.
*   **UpdateAI**: Handles target validation, Ravenous Claw casting, Enrage at <10% HP, and melee attacks.
*   **CorpseRemoved**: Calls `DeleteLater` to clean up the creature object.
*   **GetAI_boss_timmy_the_cruel**: Factory function for `boss_timmy_the_cruelAI`.
*   **npc_crimson_guardsmanAI**: Constructor; initializes base class, calls `Reset`, and sets `m_bIsTimmySpawner` based on DB GUID.
*   **Reset#2**: Resets flee flag and ability timers (Disarm, Shield Bash, Shield Charge).
*   **JustDied**: If spawner, summons Timmy, triggers yell, sets respawn time, and deletes self.
*   **UpdateAI#2**: Handles target validation, fleeing at <15% HP, ability casting, and melee attacks.
*   **GetAI_npc_crimson_guardsman**: Factory function for `npc_crimson_guardsmanAI`.
*   **AddSC_boss_timmy_the_cruel**: Registers both NPC scripts with the server.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_timmy_the_cruel

*Source:* boss_timmy_the_cruel.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_timmy_the_cruelAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset | method | — | — | — |
| UpdateAI | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, SpellCaster/CastSpell#2, Unit.Main/GetHealthPercent, Unit.Main/GetVictim, Unit.Main/HasAura#2, Unit.Main/SelectHostileTarget | — | — |
| CorpseRemoved | method | WorldObject.Object/DeleteLater | — | — |
| GetAI_boss_timmy_the_cruel | function | — | — | — |
| npc_crimson_guardsmanAI | ctor | Creature.Main/GetDBTableGUIDLow, ScriptedAI/ScriptedAI | — | — |
| Reset#2 | method | — | — | — |
| JustDied | method | Creature.Main/SetRespawnTime, WorldObject.Object/DeleteLater, WorldObject.Object/MonsterYell#2, WorldObject.Object/SummonCreature#2 | — | — |
| UpdateAI#2 | method | Creature.Main/DoFlee, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, Unit.Main/GetHealthPercent, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_npc_crimson_guardsman | function | — | — | — |
| AddSC_boss_timmy_the_cruel | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
