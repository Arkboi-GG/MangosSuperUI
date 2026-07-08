<!-- provenance: verbose -->
# boss_high_inquisitor_fairbanks

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`boss_high_inquisitor_fairbanks.cpp` implements the AI for **High Inquisitor Fairbanks** in the Scarlet Monastery instance. It defines `boss_high_inquisitor_fairbanksAI`, a `ScriptedAI` subclass managing combat rotations (healing, crowd control, offensive spells) and a specific narrative event triggered by the Ashbringer weapon.

## Member-by-Member Behavior

### Initialization and State

**`boss_high_inquisitor_fairbanksAI`**
Constructs the AI instance and immediately calls `Reset()` to initialize timers and flags.

**`Reset`**
Resets internal state for a new encounter:
-   **Timers:** Sets intervals for `CurseOfBlood` (10s), `DispelMagic` (30s), `Fear` (40s), `Heal` (30s), `Sleep` (30s), and `Dispel` (20s).
-   **Flags:** Resets `PowerWordShield` and `bAshbringer` to `false`.

### Combat Logic

**`UpdateAI`**
Executes the main combat loop:
1.  **Validation:** Returns early if no hostile target or victim exists.
2.  **Healing/Shielding (<25% HP):**
    -   If health ≤25% and not casting, casts `SPELL_HEAL` (12039) and resets `Heal_Timer` (30s).
    -   If health ≤25% and `PowerWordShield` is false, casts `SPELL_POWERWORDSHIELD` (11647) once per engagement.
3.  **Crowd Control:**
    -   **Fear:** Every 40s, casts `SPELL_FEAR` (12096) on a random attacker.
    -   **Sleep:** Every 30s, casts `SPELL_SLEEP` (8399) on the top aggro target.
4.  **Offense/Defense:**
    -   **Dispel:** Checks `Dispel_Timer` (initialized to 20s in `Reset`) but incorrectly resets `DispelMagic_Timer` (30s) after casting `SPELL_DISPELMAGIC` (15090) on a random attacker. This causes `Dispel_Timer` to underflow, breaking subsequent dispel logic.
    -   **Curse of Blood:** Every 25s, casts `SPELL_CURSEOFBLOOD` (8282) on the victim.
5.  **Melee:** Calls `DoMeleeAttackIfReady()`.

### Event Handling

**`SpellHit`**
Triggers the Ashbringer narrative event when hit by spell ID 28441:
-   Verifies `bAshbringer` is false and the caster is in Line of Sight.
-   Transforms the boss (spell 28443), disarms it (`SHEATH_STATE_UNARMED`), faces the caster, and enables gossip interaction (`UNIT_NPC_FLAG_GOSSIP`).
-   Sets `bAshbringer` to true to prevent re-triggering.

### Registration

**`GetAI_boss_high_inquisitor_fairbanks`**
Factory function returning a new `boss_high_inquisitor_fairbanksAI` instance.

**`AddSC_boss_high_inquisitor_fairbanks`**
Registers the script with `ScriptMgr` using the name `"boss_high_inquisitor_fairbanks"` and links the `GetAI` factory. Called by `ScriptLoader::AddScripts`.

## Cross-Unit Boundaries

| Member | Direction | Target Unit | Collaboration Details |
| :--- | :--- | :--- | :--- |
| `boss_high_inquisitor_fairbanksAI` | Calls | `ScriptedAI` | Inherits base AI infrastructure. |
| `SpellHit` | Calls | `SpellCaster` | Casts transformation spell (28443). |
| `SpellHit` | Calls | `Unit.Main` | Sets facing (`SetFacingToObject`) and sheath state (`SetSheath`). |
| `SpellHit` | Calls | `WorldObject.Object` | Checks LOS (`IsWithinLOSInMap`) and sets gossip flag (`SetFlag`). |
| `UpdateAI` | Calls | `Creature.Main` | Selects targets (`SelectAttackingTarget`). |
| `UpdateAI` | Calls | `CreatureAI` | Casts spells (`DoCastSpellIfCan`) and performs melee attacks (`DoMeleeAttackIfReady`). |
| `UpdateAI` | Calls | `SpellCaster` | Checks casting state (`IsNonMeleeSpellCasted`). |
| `UpdateAI` | Calls | `Unit.Main` | Gets health/victim/targets (`GetHealthPercent`, `GetVictim`, `SelectHostileTarget`). |
| `AddSC...` | Calls | `Script` / `ScriptMgr` | Creates and registers the script object. |
| `AddSC...` | Called By | `ScriptLoader` | Invoked during server startup. |

## Data Model

No database tables are accessed. All logic uses hardcoded spell IDs and in-memory timers.

## Notable Implementation Details

1.  **Dispel Timer Bug:** In `UpdateAI`, the condition checks `Dispel_Timer` but resets `DispelMagic_Timer`. Since `Dispel_Timer` is never reset after the first cast, it underflows, causing the dispel logic to break or fire unpredictably.
2.  **Ashbringer Trigger:** Relies on hardcoded spell ID 28441. Requires Line of Sight to trigger, preventing remote activation.
3.  **One-Time Shield:** `PowerWordShield` is guarded by a boolean flag, ensuring it casts only once per reset.

## Member Reference

**`boss_high_inquisitor_fairbanksAI`**  
Constructor initializing `ScriptedAI` and calling `Reset()`.

**`Reset`**  
Resets all timers (`CurseOfBlood`, `DispelMagic`, `Fear`, `Heal`, `Sleep`, `Dispel`) and flags (`PowerWordShield`, `bAshbringer`).

**`SpellHit`**  
Handles Ashbringer event (spell 28441): transforms boss, enables gossip, and sets `bAshbringer` flag if LOS is valid.

**`UpdateAI`**  
Manages combat rotation: heals/shields at <25% HP, casts Fear/Sleep/Dispel/Curse of Blood on timers, and performs melee attacks. Contains a bug where `Dispel_Timer` is checked but `DispelMagic_Timer` is reset.

**`GetAI_boss_high_inquisitor_fairbanks`**  
Factory function creating `boss_high_inquisitor_fairbanksAI` instances.

**`AddSC_boss_high_inquisitor_fairbanks`**  
Registers the script with `ScriptMgr`; called by `ScriptLoader::AddScripts`.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_high_inquisitor_fairbanks

*Source:* boss_high_inquisitor_fairbanks.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_high_inquisitor_fairbanksAI | ctor | ScriptedAI/ScriptedAI | — | — |
| Reset | method | — | — | — |
| SpellHit | method | SpellCaster/CastSpell#2, Unit.Main/SetFacingToObject, Unit.Main/SetSheath, WorldObject.Object/IsWithinLOSInMap, WorldObject.Object/SetFlag | — | — |
| UpdateAI | method | Creature.Main/SelectAttackingTarget, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, SpellCaster/IsNonMeleeSpellCasted, Unit.Main/GetHealthPercent, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_boss_high_inquisitor_fairbanks | function | — | — | — |
| AddSC_boss_high_inquisitor_fairbanks | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
