<!-- provenance: verbose -->
# boss_jandice_barov

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# boss_jandice_barov

This unit implements the AI for **Jandice Barov** (`boss_jandicebarovAI`) and her summoned **Illusions** (`mob_illusionofjandicebarovAI`) in the Scholomance dungeon. The boss cycles between standard combat and a periodic "Illusion" phase. During the Illusion phase, Jandice becomes invisible and unselectable, summons 10 illusions that attack random players, and resets her threat. After 3 seconds, she becomes visible again. Players can prematurely end the Illusion phase by dealing over 500 cumulative damage to Jandice during this brief visible window. Upon death, she drops a quest journal if the server patch is 1.9 or higher.

## Member-by-Member Behavior

### Boss Jandice Barov (`boss_jandicebarovAI`)

**`boss_jandicebarovAI` (ctor)**
Initializes the AI by calling `Reset()` to establish initial timer states and flags. Inherits from `ScriptedAI`.

**`Reset`**
Resets internal state for the boss:
- `CurseOfBlood_Timer`: 10 seconds.
- `Illusion_Timer`: 15 seconds (initial delay).
- `Invisible_Timer`: 3 seconds (duration of invisibility).
- `Invisible`: `false`.
- `damageTaken`: 0.
- `checkForDamage`: `false`.

**`SummonIllusions`**
Spawns one illusion (Entry 11439) near the boss with a 60-second despawn timer. If the spawn succeeds and has an AI, it commands the illusion to `AttackStart` the specified `victim` and stores the illusion's GUID in `IllusionGUIDS`.

**`UnsummonIllusions`**
Iterates through `IllusionGUIDS`. For each GUID, it retrieves the creature from the map via `m_creature->GetMap()->GetCreature`. If found, it schedules the creature for removal via `AddObjectToRemoveList`. If not found, it logs a minimal error. Finally, it clears `IllusionGUIDS`.

**`JustDied`**
Executes on boss death:
1. Calls `UnsummonIllusions()` to clean up active illusions.
2. Removes `UNIT_FLAG_NOT_SELECTABLE` and sets visibility to `VISIBILITY_ON`.
3. Checks `sWorld.GetWowPatch()`. If patch ≥ 1.9, casts `SPELL_DROP_JOURNAL` (26096) as a triggered effect.

**`DamageTaken`**
Tracks damage only if `checkForDamage` is `true` (active during the post-invisibility window). Accumulates damage into `damageTaken`. If total exceeds 500, it calls `UnsummonIllusions()`, resets `checkForDamage` to `false`, and resets `damageTaken` to 0. This allows players to break the illusion phase early.

**`UpdateAI`**
Manages the boss's state machine:
1. **Invisibility Handling**: If `Invisible`, decrements `Invisible_Timer`. If expired, makes the boss visible (faction 14, selectable, `VISIBILITY_ON`), sets `Invisible` to `false`, resets `damageTaken`, and enables `checkForDamage`. Returns early if still invisible.
2. **Target Check**: Returns if no hostile target exists.
3. **Curse of Blood**: If `CurseOfBlood_Timer` expires, casts `SPELL_CURSEOFBLOOD` on the victim and resets timer to 30 seconds.
4. **Illusion Phase**: If `Illusion_Timer` expires and not invisible:
   - Interrupts non-melee spells.
   - Sets faction to 35, adds `UNIT_FLAG_NOT_SELECTABLE`, reduces threat on victim by 99%, and sets `VISIBILITY_OFF`.
   - Sets `Invisible` to `true`, `Invisible_Timer` to 3 seconds, and `Illusion_Timer` to 25 seconds.
   - Loops 10 times to summon illusions targeting random players.
5. **Melee**: Calls `DoMeleeAttackIfReady()`.

### Illusion of Jandice Barov (`mob_illusionofjandicebarovAI`)

**`mob_illusionofjandicebarovAI` (ctor)**
Initializes the illusion AI by calling `Reset()`. Inherits from `ScriptedAI`.

**`Reset#2`**
Sets `Cleave_Timer` to a random value between 2–8 seconds. Applies magic damage immunity (`IMMUNITY_DAMAGE`, `SPELL_SCHOOL_MASK_MAGIC`) to prevent magic kills.

**`UpdateAI#2`**
1. Returns if no hostile target.
2. If `Cleave_Timer` expires, casts `SPELL_CLEAVE` (15584) on the victim. On success, resets timer to 5–15 seconds.
3. Calls `DoMeleeAttackIfReady()`.

### Registration

**`GetAI_boss_jandicebarov`**
Factory function returning a new `boss_jandicebarovAI` instance.

**`GetAI_mob_illusionofjandicebarov`**
Factory function returning a new `mob_illusionofjandicebarovAI` instance.

**`AddSC_boss_jandicebarov`**
Registers both scripts with the `ScriptMgr` by creating `Script` objects for "boss_jandice_barov" and "mob_illusionofjandicebarov" and assigning their respective `GetAI` functions. Called by `ScriptLoader/AddScripts`.

## Cross-Unit Boundaries

- **`ScriptedAI`**: Base class for both AIs (`ScriptedAI/ScriptedAI`).
- **`Creature` / `CreatureAI`**:
  - `SummonIllusions` uses `Creature.Main/AI` and `CreatureAI/AttackStart` to engage illusions.
  - `UnsummonIllusions` uses `Map.Main/GetCreature` to locate illusions.
  - `UpdateAI` (boss) uses `Creature.Main/SelectAttackingTarget` for random targets.
  - Both AIs use `CreatureAI/DoCastSpellIfCan` and `CreatureAI/DoMeleeAttackIfReady` for combat.
- **`Unit`**:
  - `JustDied` and `UpdateAI` (boss) use `Unit.Main/SetVisibility` and `Unit.Main/SetFactionTemplateId`.
  - `UpdateAI` (boss) uses `Unit.Main/GetThreatManager` to reset threat.
  - `Reset#2` uses `Unit.Main/ApplySpellImmune`.
  - `UpdateAI#2` uses `Unit.Main/GetVictim` and `Unit.Main/SelectHostileTarget`.
- **`World`**: `JustDied` calls `World/GetWowPatch` for patch-specific logic.
- **`Log`**: `UnsummonIllusions` calls `Log.Main/Out` for error logging.
- **`Script` / `ScriptMgr`**: `AddSC_boss_jandicebarov` uses `Script/Script` and `ScriptMgr/RegisterSelf`.

## Data Model

No database tables are accessed. All state is managed in-memory via timers and creature spawns.

## Notable Implementation Details

- **Damage Break Mechanic**: `DamageTaken` tracks cumulative damage during the visible window. Exceeding 500 damage instantly removes all illusions via `UnsummonIllusions`.
- **Magic Immunity**: Illusions are immune to magic damage (`Reset#2`), forcing physical DPS or reliance on the boss mechanic.
- **Threat Reset**: `UpdateAI` reduces threat by 99% when entering the illusion phase, effectively resetting aggro.
- **Patch Check**: `JustDied` only drops the journal if `WOW_PATCH_109` or higher.
- **Hardcoded Counts**: Exactly 10 illusions are summoned per phase.
- **Timer Uncertainty**: The `Invisible_Timer` is set to 3 seconds with a comment `//Too much too low?`, indicating developer uncertainty about the timing.

## Member Reference

**`boss_jandicebarovAI`** (ctor): Initializes AI by calling `Reset()`.
**`Reset`**: Resets timers (`CurseOfBlood`, `Illusion`, `Invisible`) and flags (`Invisible`, `checkForDamage`, `damageTaken`).
**`SummonIllusions`**: Spawns one illusion (11439), starts combat with `victim`, and stores GUID.
**`UnsummonIllusions`**: Iterates `IllusionGUIDS`, removes creatures from map, logs errors if missing, and clears vector.
**`JustDied`**: Cleans up illusions, restores visibility/selectability, and conditionally drops journal based on patch.
**`DamageTaken`**: Accumulates damage if `checkForDamage` is true; removes illusions if total > 500.
**`UpdateAI`**: Manages invisibility timer, casts Curse of Blood, triggers illusion phase (summons 10 illusions, hides boss, resets threat), and handles melee.
**`mob_illusionofjandicebarovAI`** (ctor): Initializes AI by calling `Reset#2`.
**`Reset#2`**: Sets `Cleave_Timer` (2–8s) and applies magic damage immunity.
**`UpdateAI#2`**: Casts Cleave on timer expiry (5–15s) and performs melee attacks.
**`GetAI_boss_jandicebarov`**: Factory function for `boss_jandicebarovAI`.
**`GetAI_mob_illusionofjandicebarov`**: Factory function for `mob_illusionofjandicebarovAI`.
**`AddSC_boss_jandicebarov`**: Registers both scripts with `ScriptMgr`.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_jandice_barov

*Source:* boss_jandice_barov.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_jandicebarovAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset | method | — | — | — |
| SummonIllusions | method | Creature.Main/AI, CreatureAI/AttackStart, Object/GetGUID, ScriptedAI/DoSpawnCreature#2 | — | — |
| UnsummonIllusions | method | Log.Main/Out, Map.Main/GetCreature, ObjectGuid/ObjectGuid#5, WorldObject.Object/AddObjectToRemoveList, WorldObject.Object/GetMap | — | — |
| JustDied | method | CreatureAI/DoCastSpellIfCan, Unit.Main/SetVisibility, World/GetWowPatch, WorldObject.Object/RemoveFlag | — | — |
| DamageTaken | method | — | — | — |
| UpdateAI | method | Creature.Main/SelectAttackingTarget, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, SpellCaster/InterruptNonMeleeSpells, ThreatManager/modifyThreatPercent#2, Unit.Main/GetThreatManager, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget, Unit.Main/SetFactionTemplateId, Unit.Main/SetVisibility, WorldObject.Object/RemoveFlag, WorldObject.Object/SetFlag | — | — |
| mob_illusionofjandicebarovAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#2 | method | shared_Util/urand, Unit.Main/ApplySpellImmune | — | — |
| UpdateAI#2 | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, shared_Util/urand, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_boss_jandicebarov | function | — | — | — |
| GetAI_mob_illusionofjandicebarov | function | — | — | — |
| AddSC_boss_jandicebarov | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
