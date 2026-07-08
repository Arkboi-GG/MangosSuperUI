<!-- provenance: verbose -->
# boss_emperor_dagran_thaurissan

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# boss_emperor_dagran_thaurissan

This unit implements the AI for **Emperor Dagran Thaurissan** and **Moira Bronzebeard** in the *Blackrock Depths* instance. The Emperor is the primary tank/DPS threat, while Moira acts as a healer and secondary damage dealer. They coordinate via `ScriptedInstance` data to track each other’s GUIDs and states.

## Purpose & Responsibilities

*   **Emperor Dagran Thaurissan**: Engages in melee, casts `Hand of Thaurissan` (random player target) and `Avatar of Flame` (self-buff), and periodically calls for reinforcements. On death, he forces Moira to become friendly and evade combat.
*   **Moira Bronzebeard**: Casts offensive spells (`Mind Blast`, `Shadow Word: Pain`, `Smite`) and heals the Emperor if his health is below 100%. She also performs melee attacks.

## Member-by-Member Behavior

### Emperor Dagran Thaurissan AI

**`boss_emperor_dagran_thaurissanAI` (ctor)**
Initializes the AI, retrieves `ScriptedInstance` via `WorldObject.Object/GetInstanceData`, and calls `Reset()`.

**`Reset`**
Sets initial timers: `HandOfThaurissan` (5–7.5s random), `AvatarOfFlame` (18s), `Ironfoe` (9s, unused), and `CallForHelp` (8s).

**`Aggro`**
Plays aggro dialogue (`SAY_AGGRO`) via `ScriptMgr/DoScriptText` and summons nearby NPCs via `Creature.Main/CallForHelp`.

**`JustDied`**
Locates Moira Bronzebeard using `InstanceData/GetData64` and `Map.Main/GetCreature`. If she is alive, sets her faction to friendly (`FACTION_FRIENDLY`), forces evasion via `CreatureAI/EnterEvadeMode`, and plays an emote (`EMOTE_SHAKEN`) via `ScriptMgr/DoScriptText`.

**`KilledUnit`**
Plays a kill quote (`SAY_SLAY`) via `ScriptMgr/DoScriptText`.

**`UpdateAI`**
Processes timers if a hostile target exists:
*   **Hand of Thaurissan**: Selects a random player via `Creature.Main/SelectAttackingTarget`, but casts `SPELL_HANDOFTHAURISSAN` on `GetVictim()`. Resets timer to 10–15s random.
*   **Avatar of Flame**: Casts `SPELL_AVATAROFFLAME` on self. Resets to 18s.
*   **Call for Help**: Calls `Creature.Main/CallForHelp`. Resets to 20s.
*   **Ironfoe**: Timer decrements but logic is commented out.
Finally, attempts melee via `CreatureAI/DoMeleeAttackIfReady`.

**`GetAI_boss_emperor_dagran_thaurissan`**
Factory function returning a new `boss_emperor_dagran_thaurissanAI` instance.

### Moira Bronzebeard AI

**`boss_moira_bronzebeardAI` (ctor)**
Initializes the AI, retrieves `ScriptedInstance` via `WorldObject.Object/GetInstanceData`, and calls `Reset()`.

**`Reset#2`**
Sets timers: `Heal` (12s), `MindBlast` (16s), `ShadowWordPain` (2s), `Smite` (8s). Source comments note these values may be inaccurate.

**`UpdateAI#2`**
Processes timers if a hostile target exists:
*   **Mind Blast**: Casts `SPELL_MINDBLAST` on victim. Resets to 14s.
*   **Shadow Word: Pain**: Casts `SPELL_SHADOWWORDPAIN` on victim. Resets to 18s.
*   **Smite**: Casts `SPELL_SMITE` on victim. Resets to 10s.
*   **Heal**: Locates Emperor via `InstanceData/GetData64` and `Map.Main/GetCreature`. If alive and health < 100%, casts `SPELL_HEAL` on him. Resets to 10s.
Finally, attempts melee via `CreatureAI/DoMeleeAttackIfReady`.

**`GetAI_boss_moira_bronzebeard`**
Factory function returning a new `boss_moira_bronzebeardAI` instance.

### Script Registration

**`AddSC_boss_draganthaurissan`**
Creates `Script` objects for both bosses, assigns their `GetAI` factories, and registers them via `ScriptMgr/RegisterSelf`. Called by `ScriptLoader/AddScripts`.

## Cross-Unit Boundaries

*   **`boss_emperor_dagran_thaurissanAI::JustDied`** calls `Map.Main/GetCreature` and `InstanceData/GetData64` to find Moira, then uses `CreatureAI/EnterEvadeMode` and `Unit.Main/SetFactionTemplateId` to remove her from combat.
*   **`boss_moira_bronzebeardAI::UpdateAI`** calls `InstanceData/GetData64` and `Map.Main/GetCreature` to find the Emperor, checking `Unit.Main/IsAlive` and `Unit.Main/GetHealthPercent` before healing.
*   Both AIs use `ScriptMgr/DoScriptText` for dialogue, `Creature.Main/CallForHelp` for reinforcements, and `CreatureAI/DoCastSpellIfCan`/`CreatureAI/DoMeleeAttackIfReady` for combat actions.

## Data Model

No database tables are accessed. All configuration (spell IDs, factions, timers) is hardcoded. NPC coordination relies on `ScriptedInstance` memory state holding GUIDs for `DATA_PRINCESS` and `DATA_EMPEROR`.

## Notable Implementation Details

1.  **Unused Ironfoe**: `boss_emperor_dagran_thaurissanAI::UpdateAI` maintains `m_uiIronfoeTimer` but the casting logic is commented out.
2.  **Targeting Quirk**: In `boss_emperor_dagran_thaurissanAI::UpdateAI`, `Hand of Thaurissan` selects a random player but casts on `GetVictim()`, potentially ignoring the random selection.
3.  **Inaccurate Timers**: `boss_moira_bronzebeardAI::Reset` contains a comment stating timer values are "probably wrong."

## Member Reference

**`boss_emperor_dagran_thaurissanAI`** (ctor): Initializes Emperor AI, retrieves instance data, and calls `Reset()`.
**`Reset`**: Sets initial timer values for Emperor's abilities.
**`Aggro`**: Plays aggro dialogue and summons nearby NPCs.
**`JustDied`**: Makes Moira Bronzebeard friendly and evade combat if alive.
**`KilledUnit`**: Plays a kill quote.
**`UpdateAI`**: Handles Emperor's spell timers and melee attacks; includes unused Ironfoe timer.
**`GetAI_boss_emperor_dagran_thaurissan`**: Factory function for Emperor AI.
**`boss_moira_bronzebeardAI`** (ctor): Initializes Moira AI, retrieves instance data, and calls `Reset()`.
**`Reset#2`**: Sets initial timer values for Moira's abilities (noted as potentially inaccurate).
**`UpdateAI#2`**: Handles Moira's spell timers, healing logic for the Emperor, and melee attacks.
**`GetAI_boss_moira_bronzebeard`**: Factory function for Moira AI.
**`AddSC_boss_draganthaurissan`**: Registers both boss scripts with the server.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_emperor_dagran_thaurissan

*Source:* boss_emperor_dagran_thaurissan.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_emperor_dagran_thaurissanAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset | method | shared_Util/urand | — | — |
| Aggro | method | Creature.Main/CallForHelp, ScriptMgr/DoScriptText | — | — |
| JustDied | method | Creature.Main/AI, CreatureAI/EnterEvadeMode, InstanceData/GetData64, Map.Main/GetCreature, ObjectGuid/ObjectGuid#5, ScriptMgr/DoScriptText, Unit.Main/IsAlive, Unit.Main/SetFactionTemplateId | — | — |
| KilledUnit | method | ScriptMgr/DoScriptText | — | — |
| UpdateAI | method | Creature.Main/CallForHelp, Creature.Main/SelectAttackingTarget, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, shared_Util/urand, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_boss_emperor_dagran_thaurissan | function | — | — | — |
| boss_moira_bronzebeardAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset#2 | method | — | — | — |
| UpdateAI#2 | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, InstanceData/GetData64, Map.Main/GetCreature, ObjectGuid/ObjectGuid#5, Unit.Main/GetHealthPercent, Unit.Main/GetVictim, Unit.Main/IsAlive, Unit.Main/SelectHostileTarget | — | — |
| GetAI_boss_moira_bronzebeard | function | — | — | — |
| AddSC_boss_draganthaurissan | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
