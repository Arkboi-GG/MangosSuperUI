<!-- provenance: verbose -->
# scholo_trash

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# scholo_trash

**Purpose & Responsibilities**

`s cholo_trash.cpp` implements AI behaviors for three "trash" mobs in the Scholomance dungeon: `npc_unstable_corpse`, `npc_reanimated_corpse`, and `npc_spectral_projection`. The unit defines three `ScriptedAI` subclasses and a registration function `AddSC_scholo_trash`. No database tables are accessed; behavior relies on hardcoded spell IDs and in-memory state.

## Member-by-Member Behavior

### Unstable Corpse (`npc_unstable_corpseAI`)
A melee mob that applies a debuff on aggro and explodes on death.
*   **`npc_unstable_corpseAI` (ctor)**: Calls `ScriptedAI` base constructor.
*   **`Reset#3`**: Empty override.
*   **`Aggro#2`**: Casts `SPELL_DARK_PLAGUE_AURA` (12038) on self via `CreatureAI/DoCastSpellIfCan` with `CF_TRIGGERED | CF_AURA_NOT_PRESENT`.
*   **`JustDied`**: Casts `SPELL_EXPLOSION` (17689) on self via `SpellCaster/CastSpell#2` (instant).
*   **`UpdateAI#2`**: Checks for a victim via `Unit.Main/SelectHostileTarget` and `Unit.Main/GetVictim`; performs melee attacks via `CreatureAI/DoMeleeAttackIfReady`.
*   **`GetAI_npc_unstable_corpse`**: Factory returning a new `npc_unstable_corpseAI`.

### Reanimated Corpse (`npc_reanimated_corpseAI`)
A melee mob that fakes death at 1 HP, waits 10 seconds, then resurrects fully healed. It uses `SetInvincibilityHpThreshold(1)` to prevent true death.
*   **`npc_reanimated_corpseAI` (ctor)**: Calls base constructor and `Reset()`.
*   **`Reset`**: Resets `m_uiHealTimer` to 0, `m_bHasRessed` to false, and sets invincibility threshold to 1 via `Unit.Main/SetInvincibilityHpThreshold`.
*   **`Aggro`**: Casts `SPELL_DARK_PLAGUE_AURA` on self via `CreatureAI/DoCastSpellIfCan`.
*   **`Resurrect`**: Helper that casts `SPELL_FULL_HEAL` (17683) via `SpellCaster/CastSpell#2`, sets stand state to standing via `Unit.Main/SetStandState`, removes dead flag via `WorldObject.Object/RemoveFlag`, clears invincibility threshold, and stops attacks via `Unit.Main/AttackStop`.
*   **`DamageTaken`**: If damage would kill and `!m_bHasRessed` and `!m_uiHealTimer`, it sets health to 1 via `Unit.Main/SetHealth`, clears motion via `MotionMaster/Clear` and `Creature.MotionMaster/MoveIdle`, sets stand state to dead, adds dead flag via `WorldObject.Object/SetFlag`, and starts a 10s timer (`m_uiHealTimer = 10000`). Uses `Unit.Main/GetHealth` and `Unit.Main/GetMotionMaster`.
*   **`UpdateAI`**: Decrements `m_uiHealTimer`; if expired, calls `Resurrect()` and sets `m_bHasRessed = true`. Otherwise, handles standard melee combat via `Unit.Main/SelectHostileTarget`, `Unit.Main/GetVictim`, and `CreatureAI/DoMeleeAttackIfReady`.
*   **`GetAI_npc_reanimated_corpse`**: Factory returning a new `npc_reanimated_corpseAI`.

### Spectral Projection (`npc_spectral_projectionAI`)
A passive entity that heals its caster and despawns when hit by a specific leech spell.
*   **`npc_spectral_projectionAI` (ctor)**: Calls base constructor and `Reset()`.
*   **`Reset#2`**: Empty override.
*   **`SpellHit`**: If hit by `SPELL_PROJECTION_LEECH` (17652), it casts `pCaster` to `Unit` via `Object/ToUnit`. If valid, it adds 1000.0f health to the caster via `Unit.Main/SetHealth` and `Unit.Main/GetHealth` ("hack life leech effect"), then removes the creature from the world via `Creature.Main/RemoveFromWorld`.
*   **`GetAI_npc_spectral_projection`**: Factory returning a new `npc_spectral_projectionAI`.

### Registration
*   **`AddSC_scholo_trash`**: Creates `Script` objects for the three NPCs, assigns their `GetAI` functions, and registers them via `ScriptMgr/RegisterSelf`. Called by `ScriptLoader/AddScripts`.

## Cross-Unit Boundaries

All calls are outbound from this unit to core systems:
*   **`ScriptedAI`**: Base class for all three AI structs.
*   **`CreatureAI`**: `DoCastSpellIfCan` (aggro spells), `DoMeleeAttackIfReady` (combat loop).
*   **`SpellCaster`**: `CastSpell#2` (explosion, heal).
*   **`Unit.Main`**: `GetVictim`, `SelectHostileTarget` (targeting); `SetInvincibilityHpThreshold` (fake death prevention); `SetStandState`, `GetHealth`, `SetHealth` (state/health manipulation); `AttackStop` (combat halt).
*   **`WorldObject.Object`**: `SetFlag`, `RemoveFlag` (dead flag toggling).
*   **`Creature.MotionMaster` / `MotionMaster`**: `MoveIdle`, `Clear` (movement control during fake death).
*   **`Creature.Main`**: `RemoveFromWorld` (despawn).
*   **`Object`**: `ToUnit` (caster validation).
*   **`Script` / `ScriptMgr`**: Script registration.

## Data Model

No database tables are accessed. Comments reference external SQL fixes for `spell_proc_event` and `spell_mod` to enable aura stacking for `SPELL_DARK_PLAGUE_AURA`, but these are not executed by this code.

## Notable Implementation Details

*   **Fake Death Logic**: `npc_reanimated_corpseAI` prevents true death by setting an invincibility threshold of 1 HP. `DamageTaken` intercepts lethal hits, forces health to 1, marks the creature as dead visually, and starts a 10-second timer before `Resurrect` restores it. The `m_bHasRessed` flag ensures this occurs only once.
*   **Hardcoded Heal**: `npc_spectral_projectionAI::SpellHit` adds a flat 1000.0f health to the caster, labeled as a "hack." It does not scale with spell power or level.
*   **Aura Stacking Dependency**: The `SPELL_DARK_PLAGUE_AURA` mechanic relies on external database configuration (`spell_proc_event` and `spell_mod`) to stack correctly between mobs, as noted in the source comments.

## Member Reference

*   **npc_unstable_corpseAI (ctor)**: Initializes `ScriptedAI` base.
*   **Reset#3**: Empty override.
*   **Aggro#2**: Casts `SPELL_DARK_PLAGUE_AURA` on self.
*   **JustDied**: Casts `SPELL_EXPLOSION` on self.
*   **UpdateAI#2**: Standard melee combat loop.
*   **GetAI_npc_unstable_corpse**: Factory for `npc_unstable_corpseAI`.
*   **npc_reanimated_corpseAI (ctor)**: Initializes base and calls `Reset()`.
*   **Reset**: Resets timers/flags; sets invincibility threshold to 1.
*   **Aggro**: Casts `SPELL_DARK_PLAGUE_AURA` on self.
*   **Resurrect**: Heals, stands up, removes dead flag, clears threshold, stops attacks.
*   **DamageTaken**: Triggers fake death (1 HP, dead flag, 10s timer) if lethal damage received.
*   **UpdateAI**: Manages resurrection timer and melee combat.
*   **GetAI_npc_reanimated_corpse**: Factory for `npc_reanimated_corpseAI`.
*   **npc_spectral_projectionAI (ctor)**: Initializes base and calls `Reset()`.
*   **Reset#2**: Empty override.
*   **SpellHit**: Heals caster by 1000 HP and despawns if hit by `SPELL_PROJECTION_LEECH`.
*   **GetAI_npc_spectral_projection**: Factory for `npc_spectral_projectionAI`.
*   **AddSC_scholo_trash**: Registers the three NPC scripts.

---

<!-- machine-true, projected from graph.json -->

## Map — scholo_trash

*Source:* scholo_trash.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| npc_unstable_corpseAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#3 | method | — | — | — |
| Aggro#2 | method | CreatureAI/DoCastSpellIfCan | — | — |
| JustDied | method | SpellCaster/CastSpell#2 | — | — |
| UpdateAI#2 | method | CreatureAI/DoMeleeAttackIfReady, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_npc_unstable_corpse | function | — | — | — |
| npc_reanimated_corpseAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset | method | Unit.Main/SetInvincibilityHpThreshold | — | — |
| Aggro | method | CreatureAI/DoCastSpellIfCan | — | — |
| Resurrect | method | SpellCaster/CastSpell#2, Unit.Main/AttackStop, Unit.Main/SetInvincibilityHpThreshold, Unit.Main/SetStandState, WorldObject.Object/RemoveFlag | — | — |
| DamageTaken | method | Creature.MotionMaster/MoveIdle, MotionMaster/Clear, Unit.Main/GetHealth, Unit.Main/GetMotionMaster, Unit.Main/SetHealth, Unit.Main/SetStandState, WorldObject.Object/SetFlag | — | — |
| UpdateAI | method | CreatureAI/DoMeleeAttackIfReady, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_npc_reanimated_corpse | function | — | — | — |
| npc_spectral_projectionAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset#2 | method | — | — | — |
| SpellHit | method | Creature.Main/RemoveFromWorld, Object/ToUnit, Unit.Main/GetHealth, Unit.Main/SetHealth | — | — |
| GetAI_npc_spectral_projection | function | — | — | — |
| AddSC_scholo_trash | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
