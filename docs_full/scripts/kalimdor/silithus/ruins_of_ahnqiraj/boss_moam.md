<!-- provenance: verbose -->
# boss_moam

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`boss_moam.cpp` implements the AI for **Moam**, a boss in the *Ruins of Ahn'Qiraj* instance. The `boss_moamAI` class manages Moam's combat phases, specifically the transition between normal combat and a defensive "Energize" (stone) phase triggered by mana depletion or timers. Key mechanics include summoning Mana Fiends, draining mana, casting Arcane Eruption and Trample, and synchronizing encounter state (`NOT_STARTED`, `IN_PROGRESS`, `DONE`) with the instance script.

## Member-by-Member Behavior

### Initialization and State

**`boss_moamAI`**
Constructs the AI, casts the creature’s instance data to `ScriptedInstance`, and calls `Reset()`.

**`Reset`**
Initializes timers: `m_uiTrample_Timer` (6s), `m_uiSummonManaFiend_Timer` (90s), `m_uiTurnBackFromStone_Timer` (90s), and `m_uiDrainMana_Timer` (5s). Resets `m_bIsInCombat` to false, caches the creature’s default armor in `m_uiArmorValue`, clears the stored victim GUID `m_OGvictim`, and sets the instance data to `NOT_STARTED`.

**`Aggro`**
Marks the creature as in combat, plays the aggro emote, and sets the instance data to `IN_PROGRESS`. If this is the first aggro event (`!m_bIsInCombat`), it forces the creature’s mana to 0 and sets `m_bIsInCombat` to true.

**`JustDied`**
Summons a game object (entry 181069, an obsidian statue) at the creature’s position with a 4-day respawn time. Sets the instance data to `DONE`.

### Summon Management

**`JustSummoned`**
Handles newly spawned minions. It attempts to assign the boss’s top aggro target to the summon. If the summon is a Mana Fiend (`NPC_MANA_FIEND`), it triggers a visual spell effect (ID 25681) and returns. If no valid target exists for the summon, it adds the summon to the removal list to despawn it immediately.

### Combat Logic

**`UpdateAI`**
The main update loop. It exits early if the creature has no victim and lacks the `SPELL_ENERGIZE` aura.

1.  **Energize Phase**: If the creature has `SPELL_ENERGIZE`, it decrements `m_uiTurnBackFromStone_Timer`. If mana reaches 100% or the timer expires, it removes the aura, restores default armor, casts `SPELL_ARCANEERUPTION`, plays the "mana full" emote, and resumes attacking the previously stored victim (`m_OGvictim`) or a random target if the stored one is invalid. It then returns early.
2.  **Mana Check**: If not energized and mana is at 100%, it casts `SPELL_ARCANEERUPTION` and plays the emote.
3.  **Summon Fiends**: If `m_uiSummonManaFiend_Timer` expires, it casts `SPELL_ENERGIZE` on itself. On success, it sets armor to 18,000, summons 3 Mana Fiends (timed despawn, 10s), resets relevant timers to 90s, stores the current victim’s GUID in `m_OGvictim`, stops attacking, and plays the drain emote.
4.  **Trample**: If `m_uiTrample_Timer` expires, it casts `SPELL_TRAMPLE` on the victim and resets the timer to 15s.
5.  **Drain Mana**: If `m_uiDrainMana_Timer` expires, it casts `SPELL_DRAINMANA` on itself and resets the timer to 7s.
6.  **Melee**: Attempts a melee attack if ready.

### Script Registration

**`GetAI_boss_moam`**
Factory function returning a new `boss_moamAI` instance.

**`AddSC_boss_moam`**
Registers the "boss_moam" script with the `ScriptMgr`. Called by `ScriptLoader/AddScripts`.

## Cross-Unit Boundaries

-   **`ScriptedAI` / `ScriptedInstance`**: Inherits base AI functionality; reports encounter status changes to the instance script.
-   **`Creature.Main`**: Retrieves default armor, sets combat state, selects targets, and summons creatures/game objects.
-   **`CreatureAI`**: Commands summons to attack and handles spell casting/melee attacks.
-   **`Unit.Main`**: Manages power (mana), auras, target selection, and attack states.
-   **`WorldObject.Object`**: Provides positional data for summons and manages object removal.
-   **`Map.Main`**: Retrieves `Unit` pointers from stored GUIDs to resume targeting after the Energize phase.
-   **`ScriptMgr`**: Plays emotes and registers the script.
-   **`GameObject`**: Sets respawn times for the death statue.

## Data Model

This unit does not interact directly with database tables. It uses in-memory instance data and static spell/NPC IDs.

## Notable Implementation Details

-   **Mana Zero Start**: `Aggro` forces mana to 0 on the first combat entry, ensuring the fight begins with the mana-draining mechanic active.
-   **Hardcoded Armor**: During the Energize phase, armor is hardcoded to 18,000. A comment notes uncertainty about whether this is "Blizzlike." Armor reverts to the cached default upon phase exit.
-   **Victim Memory**: `m_OGvictim` stores the GUID of the current target before entering the Energize phase. `UpdateAI` uses this to resume attacking the same player after the phase ends, falling back to a random target if the stored unit is invalid.
-   **Fail-Safe Timer**: The Energize phase ends if mana hits 100% *or* if `m_uiTurnBackFromStone_Timer` (90s) expires, preventing the boss from being stuck in the defensive state indefinitely.
-   **Visual Spell**: `JustSummoned` manually triggers spell ID 25681 on Mana Fiends to simulate a teleportation visual.

## Member Reference

**`boss_moamAI`**
Constructor that initializes the AI, retrieves the instance data, and calls `Reset()`.

**`Reset`**
Resets all timers, combat flags, armor values, and stored victim GUIDs. Notifies the instance script that the encounter is `NOT_STARTED`.

**`Aggro`**
Sets combat state, plays aggro emote, sets mana to 0 on first aggro, and notifies the instance script that the encounter is `IN_PROGRESS`.

**`JustDied`**
Summons a persistent game object (statue) at the death location and notifies the instance script that the encounter is `DONE`.

**`JustSummoned`**
Assigns targets to newly summoned minions. For Mana Fiends, it triggers a visual spell effect. Despawns summons if they cannot acquire a valid target.

**`UpdateAI`**
Main combat loop. Manages the Energize phase (armor buff, mana drain, summoning fiends), casts Trample, Arcane Eruption, and Drain Mana based on timers and mana levels. Handles resuming combat after the Energize phase by recalling the previous victim.

**`GetAI_boss_moam`**
Factory function that creates a new `boss_moamAI` instance.

**`AddSC_boss_moam`**
Registers the "boss_moam" script with the server's script manager.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_moam

*Source:* boss_moam.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_moamAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset | method | Creature.Main/GetDefaultArmor, InstanceData/SetData, ObjectGuid/Clear | — | — |
| Aggro | method | Creature.Main/SetInCombatWithZone, InstanceData/SetData, ScriptMgr/DoScriptText, Unit.Main/SetPower | — | — |
| JustDied | method | GameObject/SetRespawnTime, InstanceData/SetData, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/SummonGameObject | — | — |
| JustSummoned | method | Creature.Main/AI, Creature.Main/SelectAttackingTarget, CreatureAI/AttackStart, Object/GetEntry, Unit.Main/GetVictim, Unit.Main/IsAlive, Unit.Main/SelectHostileTarget, Unit.Main/SendSpellGo, WorldObject.Object/AddObjectToRemoveList | — | — |
| UpdateAI | method | Creature.Main/AI, Creature.Main/SelectAttackingTarget, CreatureAI/AttackStart, CreatureAI/DoCast, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, Map.Main/GetUnit, Object/GetObjectGuid, ScriptMgr/DoScriptText, Unit.Main/AttackStop, Unit.Main/GetMaxPower, Unit.Main/GetPower, Unit.Main/GetVictim, Unit.Main/HasAura#2, Unit.Main/RemoveAurasDueToSpell, Unit.Main/SelectHostileTarget, Unit.Main/SetArmor, WorldObject.Object/GetMap, WorldObject.Object/GetOrientation, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ, WorldObject.Object/SummonCreature#2 | — | — |
| GetAI_boss_moam | function | — | — | — |
| AddSC_boss_moam | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
