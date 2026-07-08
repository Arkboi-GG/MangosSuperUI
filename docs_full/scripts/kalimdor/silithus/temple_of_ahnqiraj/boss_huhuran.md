<!-- provenance: verbose -->
# boss_huhuran

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`boss_huhuran.cpp` implements the AI and spell behaviors for **Princess Huhuran**, a boss in the *Temple of Ahn'Qiraj* raid. The unit defines:

1.  **`boss_huhuranAI`**: The creature's AI, managing combat timers, ability casting, a health/time-based enrage mechanic, and instance state updates (`IN_PROGRESS`, `DONE`, `FAIL`).
2.  **Spell Scripts**: Two `SpellScript` classes (`HuhuranWyvernStingScript`, `HuhuranPoisonBoltVolleyScript`) that force specific spells to target the closest players.
3.  **Registration**: `AddSC_boss_huhuran` registers the AI and spell scripts with the server.

No database tables are accessed; all state is in-memory.

## Member-by-Member Behavior

### AI Lifecycle & State

**`boss_huhuranAI` (ctor)**
Retrieves the `ScriptedInstance` pointer from the creature and calls `Reset()` to initialize timers and clear the berserk flag.

**`Reset`**
Initializes timers with randomized or fixed values:
-   Frenzy: 25–35s
-   Wyvern Sting: 18–28s
-   Acid Spit: 8s (fixed)
-   Noxious Poison: 10–20s
-   Berserk: 5 minutes (fixed)
-   `m_bBerserk`: `false`

**`Aggro`**
Sets instance data `TYPE_HUHURAN` to `IN_PROGRESS`.

**`JustReachedHome`**
Sets instance data `TYPE_HUHURAN` to `FAIL` (e.g., on despawn).

**`JustDied`**
Sets instance data `TYPE_HUHURAN` to `DONE`.

### Combat Logic

**`MoveInLineOfSight`**
Initiates combat if the target is within 80 yards, attackable, not in combat, and lacks `SPELL_AURA_FEIGN_DEATH`. Calls `AttackStart` and delegates to `ScriptedAI::MoveInLineOfSight`.

**`UpdateAI`**
Executes the combat loop:
1.  **Frenzy**: Casts `SPELL_FRENZY` on self if timer expires and aura is absent. Emotes `EMOTE_GENERIC_FRENZY_KILL`. Resets timer (25–35s).
2.  **Wyvern Sting**: Casts `SPELL_WYVERNSTING` on victim if timer expires and `m_bBerserk` is `false`. Resets timer (15–32s). *Note: Casting stops during enrage.*
3.  **Acid Spit**: Casts `SPELL_ACIDSPIT` on victim if timer expires. Resets timer (5–10s).
4.  **Noxious Poison**: Selects a random target and casts `SPELL_NOXIOUSPOISON` if timer expires. Resets timer (12–24s).
5.  **Berserk Enrage**: Triggers if health < 31% or 5-minute timer expires. Sets `m_bBerserk = true`, emotes `EMOTE_GENERIC_BERSERK`, and applies `SPELL_BERSERK` aura.
6.  Calls `DoMeleeAttackIfReady()`.

### Spell Targeting

**`OnSetTargetMap` (in `HuhuranWyvernStingScript`)**
Forces `selectClosestTargets = true` for Spell 26180 (Wyvern Sting).

**`OnSetTargetMap` (in `HuhuranPoisonBoltVolleyScript`)**
Forces `selectClosestTargets = true` for Spell 26052 (Poison Bolt Volley).

### Registration

**`GetAI_boss_huhuran`**
Factory function creating `boss_huhuranAI`.

**`GetScript_HuhuranWyvernSting`**
Factory function creating `HuhuranWyvernStingScript`.

**`GetScript_HuhuranPoisonBoltVolley`**
Factory function creating `HuhuranPoisonBoltVolleyScript`.

**`AddSC_boss_huhuran`**
Registers `"boss_huhuran"`, `"spell_huhuran_wyvern_sting"`, and `"spell_huhuran_poison_bolt_volley"` with `ScriptMgr`.

## Cross-Unit Boundaries

-   **`ScriptedAI` / `ScriptedInstance`**: Inherits from `ScriptedAI`; uses `ScriptedInstance` to update raid progress.
-   **`Unit` / `Creature` / `WorldObject`**: Queries state via `CanAttack`, `IsInCombat`, `IsWithinDistInMap`, `HasAura`, `GetHealthPercent`, `SelectHostileTarget`, `GetVictim`, `SelectAttackingTarget`.
-   **`CreatureAI` / `BasicAI`**: Performs actions via `AttackStart`, `DoCastSpellIfCan`, `DoMeleeAttackIfReady`.
-   **`ScriptMgr`**: Emits text via `DoScriptText`.
-   **`shared_Util`**: Generates random numbers via `urand`.

## Data Model

No database tables are accessed.

## Notable Implementation Details

1.  **Enrage Disables Wyvern Sting**: `UpdateAI` skips `SPELL_WYVERNSTING` if `m_bBerserk` is true.
2.  **Fixed Berserk Timer**: Unlike other abilities, the berserk timer is fixed at 5 minutes.
3.  **Closest Targeting**: Both spell scripts override default targeting to prioritize closest players.
4.  **Feign Death Immunity**: `MoveInLineOfSight` explicitly ignores units with `SPELL_AURA_FEIGN_DEATH`.
5.  **Timer Retry Logic**: Timers only reset on successful cast (`CAST_OK`), allowing rapid retries if a cast fails.

## Member Reference

**`boss_huhuranAI`** (ctor): Initializes AI, retrieves instance pointer, and calls `Reset()`.

**`MoveInLineOfSight`** (method): Checks range (80y), attackability, combat status, and feign death; initiates `AttackStart` if valid.

**`Aggro`** (method): Sets instance state to `IN_PROGRESS`.

**`JustReachedHome`** (method): Sets instance state to `FAIL`.

**`JustDied`** (method): Sets instance state to `DONE`.

**`Reset`** (method): Initializes randomized/fixed timers and clears berserk flag.

**`UpdateAI`** (method): Manages combat loop: Frenzy, Wyvern Sting (non-enraged), Acid Spit, Noxious Poison, Berserk enrage (31% HP/5min), and melee attacks.

**`GetAI_boss_huhuran`** (function): Factory for `boss_huhuranAI`.

**`OnSetTargetMap#2`** (method): In `HuhuranPoisonBoltVolleyScript`; forces closest-target selection.

**`GetScript_HuhuranWyvernSting`** (function): Factory for `HuhuranWyvernStingScript`.

**`OnSetTargetMap`** (method): In `HuhuranWyvernStingScript`; forces closest-target selection.

**`GetScript_HuhuranPoisonBoltVolley`** (function): Factory for `HuhuranPoisonBoltVolleyScript`.

**`AddSC_boss_huhuran`** (function): Registers boss AI and two spell scripts with `ScriptMgr`.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_huhuran

*Source:* boss_huhuran.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_huhuranAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| MoveInLineOfSight | method | BasicAI/MoveInLineOfSight, CreatureAI/AttackStart, Unit.Main/CanAttack, Unit.Main/HasAuraType, Unit.Main/IsInCombat, WorldObject.Object/IsWithinDistInMap | — | — |
| Aggro | method | InstanceData/SetData | — | — |
| JustReachedHome | method | InstanceData/SetData | — | — |
| JustDied | method | InstanceData/SetData | — | — |
| Reset | method | shared_Util/urand | — | — |
| UpdateAI | method | Creature.Main/SelectAttackingTarget, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, ScriptMgr/DoScriptText, shared_Util/urand, Unit.Main/GetHealthPercent, Unit.Main/GetVictim, Unit.Main/HasAura#2, Unit.Main/SelectHostileTarget | — | — |
| GetAI_boss_huhuran | function | — | — | — |
| OnSetTargetMap#2 | method | — | — | — |
| GetScript_HuhuranWyvernSting | function | — | — | — |
| OnSetTargetMap | method | — | — | — |
| GetScript_HuhuranPoisonBoltVolley | function | — | — | — |
| AddSC_boss_huhuran | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
