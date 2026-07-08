# boss_jindo

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# boss_jindo

## Purpose & Responsibilities

This unit implements the artificial intelligence and combat mechanics for **Jin'do the Hexxer**, a boss encounter in the Zul'Gurub raid instance, along with two associated summoned entities: the **Shade of Jin'do** and the **Brain Wash Totem**.

The primary responsibility of `boss_jindoAI` is to manage a complex rotation of spells that manipulate player threat, control players, summon minions, and banish targets. Key mechanics include:
1.  **Hex**: Temporarily removes a player from the threat table to prevent them from being targeted, then restores their threat when the spell ends.
2.  **Delusions of Jin'do / Shade of Jin'do**: Marks a random player with a debuff, then summons an invisible Shade that attacks only that marked player. The Shade is immune to damage from anyone except the marked player.
3.  **Brain Wash Totem**: Summons a totem that mind-controls a random player (excluding those Hexed or already controlled). The boss tracks these controlled players to restore their correct threat levels when the effect wears off or they die.
4.  **Banish**: Teleports a random player away and spawns skeletons to attack them.
5.  **Healing Ward**: Summons a ward that heals the boss.

The unit ensures synchronization between the boss, its summons, and the instance data (`ScriptedInstance`) to handle state changes (start, progress, done, reset) and cleanup of summoned entities.

## Member-by-Member Behavior

### Boss Jin'do AI (`boss_jindoAI`)

#### Initialization and State Management
*   **`boss_jindoAI` (ctor)**: Initializes the AI, retrieves the instance data pointer, and calls `Reset()` to set initial timers.
*   **`Reset`**: Resets all spell timers to randomized intervals within defined ranges. Clears lists of brainwashed players and summoned creatures. Calls `DespawnAllSummons()` to clean up any lingering summons. Updates the instance data to `NOT_STARTED`.
*   **`Aggro`**: Plays the aggro sound/text and updates the instance data to `IN_PROGRESS`.
*   **`JustDied`**: Cleans up all summons via `DespawnAllSummons()` and updates the instance data to `DONE`.

#### Summon Management
*   **`DespawnAllSummons`**: Iterates through the internal list `m_summonedCreatures` and removes them from the world. It also performs a safety check using `FindNearestCreature` to despawn any nearby Brain Wash Totems that might have been missed by the list tracking, ensuring a clean state on reset or death.
*   **`JustSummoned`**: Triggered when a creature is summoned by the boss.
    *   If the summoned creature is a **Shade of Jin'do** (`NPC_SHADE`), it copies the boss's threat list to the Shade. It then checks if the target marked by `m_delusionGuid` exists and has the `SPELL_DELUSIONS_OF_JINDO` aura; if so, it commands the Shade to attack that specific target.
    *   Adds the summoned creature's GUID to `m_summonedCreatures` for future cleanup.
*   **`DoSummonSkeleton`**: A helper method that summons a skeleton creature at a calculated offset from the boss's position and commands it to attack a specified initial target. Note: This function is defined but **not called** anywhere in the current `UpdateAI` logic for Banish; the Banish spell likely handles the teleportation and skeleton spawning via spell effects, or this code is legacy/incomplete.

#### Spell Mechanics and Threat Manipulation
*   **`SpellHitTarget`**: Specifically handles the **Hex** spell (`SPELL_HEX`). When the boss successfully casts Hex on a player:
    1.  Records the player's GUID in `m_hexGuid`.
    2.  Records the player's current threat value in `m_hexAggro`.
    3.  Reduces the player's threat by 100% (effectively removing them from the threat table), preventing the boss from targeting them while Hexed.
*   **`UpdateAI`**: The main tick loop managing all abilities:
    *   **Hex Restoration**: Checks if the Hexed player (`m_hexGuid`) is still alive and no longer has the Hex aura. If so, it restores their original threat (`m_hexAggro`) and clears the tracking variables. If the player is dead or missing, it simply clears the variables.
    *   **Brain Wash Totem**: If the timer expires and the boss has at least one target on its threat list, it casts `SPELL_BRAIN_WASH_TOTEM`.
    *   **Brain Wash Tracking**: Periodically checks the list of brainwashed players (`m_brainWashedPlayerGuids`). If a player dies or loses the Brain Wash aura, it calculates their original threat (stored in `m_brainWashedPlayersAggro`) and restores it, adjusting for the 100% reduction applied during control. It then removes them from the tracking lists.
    *   **Healing Ward**: If no Healing Ward is nearby, it casts `SPELL_POWERFULL_HEALING_WARD`.
    *   **Hex**: Casts `SPELL_HEX` on the current victim.
    *   **Delusions of Jin'do**: Selects a random player and casts `SPELL_DELUSIONS_OF_JINDO`, recording their GUID in `m_delusionGuid`.
    *   **Shade of Jin'do**: If a target is marked with Delusions, it casts `SPELL_SHADE_OF_JINDO` on that target, summoning the Shade.
    *   **Banish**: Selects a random player and casts `SPELL_BANISH`. As noted, the `DoSummonSkeleton` helper is not invoked here; the spell likely handles the secondary effects.
    *   Finally, it attempts melee attacks if ready.

### Shade of Jin'do AI (`mob_shade_of_jindoAI`)

#### Initialization and Immunity
*   **`mob_shade_of_jindoAI` (ctor)**: Initializes the AI and calls `Reset()`.
*   **`Reset`**: Sets the Shadow Shock timer and applies a permanent **Invisible** aura (`SPELL_INVISIBLE`) to the Shade.
*   **`DamageTaken`**: Implements a strict immunity mechanic. If the attacker does **not** have the `SPELL_DELUSIONS_OF_JINDO` aura, the damage is set to 0. This ensures only the marked player can damage the Shade.

#### Combat Logic
*   **`UpdateAI`**:
    *   If the victim has the **Hex** aura, the Shade reduces the victim's threat by 100%, mirroring the boss's behavior to ensure the Hexed player remains untargetable.
    *   Casts **Shadow Shock** on the victim periodically.
    *   Performs melee attacks if ready.

### Brain Wash Totem AI (`mob_brain_wash_totemAI`)

#### Initialization and Stability
*   **`mob_brain_wash_totemAI` (ctor)**: Initializes the AI and calls `Reset()`.
*   **`Reset`**: Applies a permanent avoidance/immunity aura (ID 23198) to prevent AoE damage from killing the totem prematurely. Roots the totem and disables combat movement.

#### Target Selection and Control
*   **`UpdateAI`**:
    *   **Safety Checks**: If the totem has no hostile target, no victim, or no instance data, it despawns itself. It forces itself into combat with the zone if not already in combat.
    *   **Existing Control Check**: If a player is already mind-controlled (`PlayerMCGuid` is set) and still alive with the Brain Wash aura, the totem does nothing further.
    *   **New Target Selection**:
        1.  Retrieves the boss (Jin'do) from the instance data.
        2.  Ensures the boss has at least one target on its threat list (to prevent resets due to empty threat tables).
        3.  Selects a random target from the boss's threat list.
        4.  Validates the target: Must be a living player, **not** Hexed, and **not** already Brain Washed.
        5.  If valid, it accesses the boss's AI (`boss_jindoAI`) directly to register the player in the boss's tracking lists (`m_brainWashedPlayerGuids` and `m_brainWashedPlayersAggro`).
        6.  Casts **Brain Wash** on the player.
        7.  Sets the `PlayerMCGuid` and triggers the boss's brain wash check timer.

## Cross-Unit Boundaries

*   **`ScriptedAI`**: All three AI structs inherit from `ScriptedAI`, providing base functionality for timers, casting, and melee attacks.
*   **`ScriptedInstance`**: The boss and totem AIs retrieve the instance data via `GetInstanceData()` to update encounter states (`TYPE_JINDO`) and locate the boss entity (`DATA_JINDO`).
*   **`Creature` / `Unit` / `Player`**: Standard interactions for targeting, aura checking, threat manipulation, and summoning.
*   **`ThreatManager`**: Used extensively by `boss_jindoAI` and `mob_shade_of_jindoAI` to manipulate threat percentages and direct threat values to implement the Hex and Brain Wash mechanics.
*   **`ScriptMgr`**: `AddSC_boss_jindo` registers the scripts with the global script manager.
*   **`boss_jindoAI` <-> `mob_brain_wash_totemAI`**: The totem AI directly accesses private members of the boss AI (`m_brainWashedPlayerGuids`, `m_brainWashedPlayersAggro`, `m_checkBrainWashTimer`) via a `dynamic_cast`. This tight coupling allows the totem to delegate threat tracking to the boss.

## Data Model

This unit does not interact directly with database tables. It relies on runtime memory structures (lists, maps) and the `ScriptedInstance` interface for state persistence across the encounter.

## Notable Implementation Details

1.  **Direct Private Member Access**: `mob_brain_wash_totemAI::UpdateAI` performs a `dynamic_cast<boss_jindoAI*>` on the boss's AI to push data into `m_brainWashedPlayerGuids` and `m_brainWashedPlayersAggro`. This bypasses encapsulation but simplifies the coordination between the totem and the boss.
2.  **Unused Helper Function**: `DoSummonSkeleton` is defined in `boss_jindoAI` but is never called in the `UpdateAI` loop. The `Banish` spell logic appears to rely solely on the spell effect (`SPELL_BANISH`), suggesting this function may be legacy code or intended for a different implementation of the Banish mechanic.
3.  **Threat Restoration Logic**: The system carefully preserves the threat value of Hexed and Brain Washed players before removing them from the threat table. Upon release, it restores the exact threat value, ensuring the combat flow resumes correctly without sudden aggro spikes or drops.
4.  **Shade Immunity**: The Shade's `DamageTaken` override is critical. It checks for the `SPELL_DELUSIONS_OF_JINDO` aura on the attacker. Without this, the Shade would be vulnerable to all players, breaking the mechanic where only the marked player can damage it.
5.  **Totem Self-Cleanup**: The Brain Wash Totem despawns itself if it loses its target or instance data, preventing orphaned entities. The boss also cleans up summons on reset/death, providing double protection against stuck entities.

## Member Reference

*   **`boss_jindoAI`**: Constructor for the boss AI; initializes instance data and resets timers.
*   **`DespawnAllSummons`**: Removes all tracked summoned creatures and cleans up nearby Brain Wash Totems.
*   **`JustSummoned`**: Handles post-summon logic for Shades (threat copying, targeting) and tracks summoned GUIDs.
*   **`Reset`**: Resets all timers, clears tracking lists, despawns summons, and updates instance state to NOT_STARTED.
*   **`SpellHitTarget`**: Handles Hex spell hits by recording player GUID/threat and reducing threat by 100%.
*   **`JustDied`**: Despawns all summons and updates instance state to DONE.
*   **`Aggro`**: Plays aggro text and updates instance state to IN_PROGRESS.
*   **`DoSummonSkeleton`**: Helper to summon a skeleton and attack a target (currently unused in UpdateAI).
*   **`UpdateAI`**: Main loop managing Hex restoration, Brain Wash tracking, Healing Ward, Hex, Delusions, Shade summoning, Banish, and melee attacks.
*   **`mob_shade_of_jindoAI`**: Constructor for the Shade AI; initializes instance data and resets timers.
*   **`Reset#3`**: Sets Shadow Shock timer and applies permanent Invisible aura to the Shade.
*   **`DamageTaken`**: Nullifies damage from attackers without the Delusions of Jin'do aura.
*   **`UpdateAI#3`**: Manages Shade's Shadow Shock casting, threat reduction for Hexed victims, and melee attacks.
*   **`mob_brain_wash_totemAI`**: Constructor for the Totem AI; initializes instance data and resets timers.
*   **`Reset#2`**: Applies immunity aura, roots the totem, and disables movement.
*   **`UpdateAI#2`**: Manages totem survival, selects a valid player target from the boss's threat list, registers them with the boss AI, and casts Brain Wash.
*   **`GetAI_boss_jindo`**: Factory function returning a new `boss_jindoAI` instance.
*   **`GetAI_mob_shade_of_jindo`**: Factory function returning a new `mob_shade_of_jindoAI` instance.
*   **`GetAI_mob_brain_wash`**: Factory function returning a new `mob_brain_wash_totemAI` instance.
*   **`AddSC_boss_jindo`**: Registers the three scripts with the Script Manager.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_jindo

*Source:* boss_jindo.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_jindoAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| DespawnAllSummons | method | Creature.Main/DisappearAndDie, Map.Main/GetCreature, ObjectGuid/GetEntry, WorldObject.Object/AddObjectToRemoveList, WorldObject.Object/FindNearestCreature, WorldObject.Object/GetMap | — | — |
| JustSummoned | method | Creature.Main/AddThreatsOf, Creature.Main/AI, CreatureAI/AttackStart, CreatureAI/JustSummoned, Map.Main/GetUnit, Object/GetEntry, Object/GetObjectGuid, Unit.Main/HasAura#2, WorldObject.Object/GetMap, WorldObject.Object/IsValidAttackTarget | — | — |
| Reset | method | InstanceData/SetData, shared_Util/urand | — | — |
| SpellHitTarget | method | Object/GetObjectGuid, Object/GetTypeId, ThreatManager/getThreat, ThreatManager/modifyThreatPercent#2, Unit.Main/GetThreatManager | — | — |
| JustDied | method | InstanceData/SetData | — | — |
| Aggro | method | InstanceData/SetData, ScriptMgr/DoScriptText | — | — |
| DoSummonSkeleton | method | Creature.Main/AI, CreatureAI/AttackStart, WorldObject.Object/SummonCreature#2 | — | — |
| UpdateAI | method | Creature.Main/SelectAttackingTarget, Creature.Main/SelectAttackingTarget#2, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, Map.Main/GetPlayer, Map.Main/GetUnit, Object/GetObjectGuid, ObjectGuid/Clear, ObjectGuid/ObjectGuid#5, Player.Main/ToPlayer, shared_Util/urand, ThreatManager/addThreatDirectly, ThreatManager/modifyThreatPercent#2, Unit.Main/GetThreatManager, Unit.Main/GetVictim, Unit.Main/HasAura, Unit.Main/HasAura#2, Unit.Main/IsAlive, Unit.Main/IsDead, Unit.Main/SelectHostileTarget, WorldObject.Object/FindNearestCreature, WorldObject.Object/GetMap | — | — |
| mob_shade_of_jindoAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset#3 | method | Unit.Main/AddAura | — | — |
| DamageTaken | method | Unit.Main/HasAura#2 | — | — |
| UpdateAI#3 | method | CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, ThreatManager/modifyThreatPercent#2, Unit.Main/GetThreatManager, Unit.Main/GetVictim, Unit.Main/HasAura#2, Unit.Main/SelectHostileTarget | — | — |
| mob_brain_wash_totemAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset#2 | method | CreatureAI/SetCombatMovement, Unit.Main/AddAura, Unit.Main/AddUnitState | — | — |
| UpdateAI#2 | method | Creature.Main/AI, Creature.Main/DisappearAndDie, Creature.Main/SelectAttackingTarget, Creature.Main/SetInCombatWithZone, CreatureAI/DoCastSpellIfCan, InstanceData/GetData64, Map.Main/GetCreature, Map.Main/GetPlayer, Object/GetGUID, Object/IsPlayer, ObjectGuid/ObjectGuid#5, ThreatManager/getThreat, Unit.Main/AddUnitState, Unit.Main/GetThreatManager, Unit.Main/GetVictim, Unit.Main/HasAura, Unit.Main/HasAura#2, Unit.Main/IsAlive, Unit.Main/IsInCombat, Unit.Main/SelectHostileTarget, WorldObject.Object/GetMap | — | — |
| GetAI_boss_jindo | function | — | — | — |
| GetAI_mob_shade_of_jindo | function | — | — | — |
| GetAI_mob_brain_wash | function | — | — | — |
| AddSC_boss_jindo | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
