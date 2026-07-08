# boss_thermaplugg

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`boss_thermaplugg.cpp` implements the AI and encounter mechanics for **Mekgineer Thermaplugg** in the **Gnomeregan** dungeon. The unit manages two primary responsibilities:

1.  **Boss AI (`boss_thermapluggAI`)**: Controls Thermaplugg’s combat behavior, including phase transitions at 50% health, knockback spells, and the summoning/movement of "Walking Bombs." It tracks the state of six "Bomb Faces" via the `instance_gnomeregan` script to determine spawn locations.
2.  **Encounter Mechanics**: Provides handlers for spell effects (`EffectDummyCreature_spell_boss_thermaplugg`) that randomly activate bomb faces, and GameObject interactions (`GOHello_go_gnomeface_button`) allowing players to deactivate specific faces.

State synchronization between the boss, spells, and player interactions is delegated to `instance_gnomeregan`.

## Member-by-Member Behavior

### Boss AI Lifecycle and State

*   **`boss_thermapluggAI` (Constructor)**: Retrieves `instance_gnomeregan` data from the creature and calls `Reset()`.
*   **`Reset`**: Initializes timers (`m_uiKnockAwayTimer`, `m_uiActivateBombTimer`) with random values, sets `m_bIsPhaseTwo` to `false`, clears the bomb face pointer, zeroes the spawn position array, and clears `m_lLandedBombGUIDs`.
*   **`Aggro`**: Plays aggro text, sets instance status to `IN_PROGRESS`, retrieves the bomb face array from the instance, and records Thermaplugg’s initial position (`m_afSpawnPos`) for later bomb spawn calculations.
*   **`JustDied`**: Sets instance status to `DONE` and clears `m_lSummonedBombGUIDs`.
*   **`JustReachedHome`**: Sets instance status to `FAIL`. Iterates `m_lSummonedBombGUIDs` to forcibly despawn any remaining walking bombs, preventing orphaned entities.

### Combat Logic (`UpdateAI`)

*   **`UpdateAI`**: The main update loop.
    1.  **Bomb Movement**: Commands bombs in `m_lLandedBombGUIDs` to follow Thermaplugg, then clears the list.
    2.  **Phase Transition**: If health drops below 50% and not already in Phase 2, plays phase text and sets `m_bIsPhaseTwo = true`.
    3.  **Knockback**:
        *   Phase 1: Casts `SPELL_KNOCK_AWAY` on the victim.
        *   Phase 2: Casts `SPELL_KNOCK_AWAY_AOE`.
        *   Resets timer randomly (17–20s in P1, fixed 12s in P2).
    4.  **Bomb Activation**:
        *   Casts `SPELL_ACTIVATE_BOMB_A` (P1) or `SPELL_ACTIVATE_BOMB_B` (P2).
        *   Resets timer randomly (12–17s in P1, 6–12s in P2).
        *   Plays bomb text with a 1-in-6 chance.
    5.  **Bomb Spawning**: Iterates `m_asBombFaces`. For each activated face:
        *   Decrements the face’s internal timer.
        *   On expiry, if active bomb count < `MAX_GNOME_FACES`, calculates a spawn point (65% toward the face from Thermaplugg’s start pos) and summons `NPC_WALKING_BOMB`.
        *   Resets the face’s timer (10–25s).
    6.  **Melee**: Performs standard melee attacks.

### Summoned Entity Management

*   **`JustSummoned`**: For `NPC_WALKING_BOMB`:
    *   Adds GUID to `m_lSummonedBombGUIDs`.
    *   Calculates a falling destination (80% bomb pos + 20% Thermaplugg start pos, Z -2.0).
    *   Updates ground Z and initiates `MOVE_FALLING` motion.
*   **`SummonedMovementInform`**: If a bomb completes its falling motion (Point ID 1), moves its GUID to `m_lLandedBombGUIDs` for the next follow command.
*   **`SummonedCreatureDespawn`**: Removes the bomb’s GUID from `m_lSummonedBombGUIDs`.

### Helpers and Registration

*   **`KilledUnit`**: Plays slay text.
*   **`GetAI_boss_thermaplugg`**: Factory function returning a new `boss_thermapluggAI`.
*   **`EffectDummyCreature_spell_boss_thermaplugg`**: Handles bomb activation spells. Validates spell ID/index, then calls `instance_gnomeregan::DoActivateBombFace` with a random index.
*   **`GOHello_go_gnomeface_button`**: Handles player clicks on gnome face buttons. Maps button entries (1–6) to `instance_gnomeregan::DoDeactivateBombFace` indices (0–5).
*   **`AddSC_boss_thermaplugg`**: Registers the boss AI and button scripts with `ScriptMgr`.

## Cross-Unit Boundaries

*   **`instance_gnomeregan`**:
    *   **Direction**: Called by `boss_thermapluggAI`, `EffectDummyCreature_spell_boss_thermaplugg`, `GOHello_go_gnomeface_button`.
    *   **Role**: Central state holder. Provides `GetBombFaces` for spawn logic, accepts status updates (`SetData`), and handles face activation/deactivation (`DoActivateBombFace`, `DoDeactivateBombFace`).
*   **`ScriptMgr`**:
    *   **Direction**: Called by `boss_thermapluggAI` and `AddSC_boss_thermaplugg`.
    *   **Role**: Plays text emotes (`DoScriptText`) and registers scripts (`RegisterSelf`).
*   **`WorldObject` / `Creature` / `Unit` / `Map`**:
    *   **Direction**: Called by various AI methods.
    *   **Role**: Engine interfaces for positioning, summoning, despawning, targeting, and spell casting. `GetMap()->GetCreature/GetGameObject` resolves GUIDs for movement and position math.
*   **`shared_Util`**:
    *   **Direction**: Called by `Reset`, `UpdateAI`, `EffectDummyCreature_spell_boss_thermaplugg`.
    *   **Role**: Provides `urand` for timer randomization and face selection.

## Data Model

This unit does not access any database tables. All state is maintained in memory via `instance_gnomeregan` and local AI variables.

## Notable Implementation Details

*   **Bomb Spawn Math**: Spawn positions are weighted averages: `0.35 * Thermaplugg_Start + 0.65 * Face_Pos`. Z is hardcoded to `-316.2625f`.
*   **Two-Stage Bomb Movement**: Bombs first fall (`MOVE_FALLING`) to a calculated point. Only after landing (detected in `SummonedMovementInform`) do they follow the boss. This creates a visual drop effect.
*   **Phase 2 Changes**: Knockback becomes AoE; bomb activation spells change; activation timers shorten (6–12s vs 12–17s).
*   **Synchronization**: The AI only reads `m_bActivated` flags from the instance. Activation/deactivation is handled externally by spells and buttons, ensuring a single source of truth.
*   **TODOs**: Source comments indicate potential tuning needs for bomb spawn chances and timers.

## Member Reference

*   **`boss_thermapluggAI`**: Constructor initializing AI, retrieving instance data, and calling `Reset`.
*   **`Reset`**: Resets timers, phase state, bomb face pointer, spawn positions, and clears landed bomb GUIDs.
*   **`KilledUnit`**: Plays slay text when the boss kills a unit.
*   **`JustDied`**: Sets encounter status to DONE and clears summoned bomb GUIDs.
*   **`Aggro`**: Plays aggro text, sets encounter status to IN_PROGRESS, retrieves bomb face states, and records initial position.
*   **`JustReachedHome`**: Sets encounter status to FAIL and forcibly despawns any remaining summoned bombs.
*   **`JustSummoned`**: Adds bomb to summoned list, calculates falling destination, and initiates falling motion.
*   **`SummonedMovementInform`**: Moves bomb GUID to landed list if it completes its falling motion.
*   **`SummonedCreatureDespawn`**: Removes bomb GUID from summoned list.
*   **`UpdateAI`**: Main logic loop handling bomb movement, phase transition, knockback spells, bomb activation spells, bomb spawning based on active faces, and melee attacks.
*   **`GetAI_boss_thermaplugg`**: Factory function to create the AI instance.
*   **`EffectDummyCreature_spell_boss_thermaplugg`**: Spell effect handler that activates a random bomb face via the instance script.
*   **`GOHello_go_gnomeface_button`**: Handles player interaction with buttons to deactivate specific bomb faces.
*   **`AddSC_boss_thermaplugg`**: Registers the boss AI and button scripts with the server.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_thermaplugg

*Source:* boss_thermaplugg.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_thermapluggAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset | method | shared_Util/urand | — | — |
| KilledUnit | method | ScriptMgr/DoScriptText | — | — |
| JustDied | method | instance_gnomeregan/SetData | — | — |
| Aggro | method | instance_gnomeregan/GetBombFaces, instance_gnomeregan/SetData, ScriptMgr/DoScriptText, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/GetPositionZ | — | — |
| JustReachedHome | method | Creature.Main/ForcedDespawn, instance_gnomeregan/SetData, Map.Main/GetCreature, ObjectGuid/ObjectGuid#5, WorldObject.Object/GetMap | — | — |
| JustSummoned | method | Creature.MotionMaster/MovePoint, Object/GetEntry, Object/GetGUID, Unit.Main/GetMotionMaster, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/UpdateGroundPositionZ | — | — |
| SummonedMovementInform | method | Object/GetEntry, Object/GetGUID | — | — |
| SummonedCreatureDespawn | method | Object/GetGUID | — | — |
| UpdateAI | method | Creature.MotionMaster/MoveFollow, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, GridSearchers/GetCreatureListWithEntryInGrid#2, Map.Main/GetCreature, Map.Main/GetGameObject, ObjectGuid/ObjectGuid#5, ScriptMgr/DoScriptText, shared_Util/urand, Unit.Main/GetHealthPercent, Unit.Main/GetMotionMaster, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget, WorldObject.Object/GetMap, WorldObject.Object/GetPositionX, WorldObject.Object/GetPositionY, WorldObject.Object/SummonCreature#2 | — | — |
| GetAI_boss_thermaplugg | function | — | — | — |
| EffectDummyCreature_spell_boss_thermaplugg | function | instance_gnomeregan/DoActivateBombFace, shared_Util/urand, WorldObject.Object/GetInstanceData | — | — |
| GOHello_go_gnomeface_button | function | instance_gnomeregan/DoDeactivateBombFace, Object/GetEntry, WorldObject.Object/GetInstanceData | — | — |
| AddSC_boss_thermaplugg | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
