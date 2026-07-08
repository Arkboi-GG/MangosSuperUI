# boss_chromaggus

<!-- documentation: model-written from source via the local LLM; review before trusting -->

# boss_chromaggus

**Purpose & Responsibilities**

This unit implements the artificial intelligence for **Chromaggus**, a raid boss in the *Blackwing Lair* dungeon. The `boss_chromaggusAI` class manages the encounter’s core mechanics:
1.  **Phase Transition:** Moving from an immune, unselectable spawn state to an active combat state triggered by a specific Game Object (side door/lever).
2.  **Elemental Vulnerability:** Periodically applying a random elemental vulnerability debuff to itself ("Shimmer").
3.  **Breath Attacks:** Casting two distinct breath spells, selected randomly at load time via instance data, on staggered timers.
4.  **Brood Afflictions:** Randomly applying one of five elemental affliction debuffs to multiple targets.
5.  **Chromatic Mutation:** Detecting when a player accumulates all five affliction debuffs, transforming them into a mutated state (buffed stats) or killing pets instantly.
6.  **Death Heals:** Healing the boss when players die while holding specific debuffs (Red Affliction or Chromatic Mutation).
7.  **Enrage/Frenzy:** Applying periodic frenzy effects and an enrage effect when health drops below 20%.

The unit does not interact with any database tables directly; all state is managed in memory via AI class members and the `InstanceData` interface.

## Member-by-Member Behavior

### Initialization and State Management

**`boss_chromaggusAI` (Constructor)**
Initializes the AI instance. It retrieves `ScriptedInstance` data to determine the boss's breath attack configuration.
-   Reads `DATA_CHROM_BREATH` from instance data. This value encodes two indices into the `aPossibleBreaths` array.
-   Calculates `m_uiBreathOneSpell` and `m_uiBreathTwoSpell` using modulo/division arithmetic to ensure two distinct breaths.
-   Sets creature flags to `UNIT_FLAG_NOT_SELECTABLE`, `UNIT_FLAG_SPAWNING`, and `UNIT_FLAG_IMMUNE_TO_NPC`, making the boss invisible and untargetable initially.
-   Calls `Reset()` to initialize timers.

**`Reset`**
Resets internal timers and state variables.
-   Initializes timers for movement (`m_uiMovetoLeverTimer`), shimmer (`m_uiShimmerTimer`), breaths (`m_uiBreathOneTimer`, `m_uiBreathTwoTimer`), afflictions (`m_uiAfflictionTimer`), and frenzy (`m_uiFrenzyTimer`).
-   Clears `m_lRedAfflictionPlayerGUID`.
-   Iterates `m_lChromaticPlayerGUID`: for each valid player on the map, removes mutation auras and deals lethal damage, killing any remaining mutated players.
-   Checks the side door Game Object (`DATA_DOOR_CHROMAGGUS_SIDE`). If open (`GO_STATE_ACTIVE`), removes spawning/immune flags. Otherwise, ensures immune flags remain set.

**`Aggro`**
Triggered when the boss enters combat.
-   Updates instance data to `IN_PROGRESS`.
-   Removes `NOT_SELECTABLE`, `SPAWNING`, and `IMMUNE_TO_NPC` flags.
-   Calls `SetInCombatWithZone`.

**`JustDied`**
Triggered on boss death. Updates instance data to `DONE`.

**`JustReachedHome`**
Triggered if the boss escapes or despawns. Updates instance data to `FAIL`.

### Combat Logic and Timers

**`UpdateAI`**
The main loop executed periodically.

1.  **Pre-Combat Movement:**
    -   If not in combat and `!m_bEngagedOnce`:
        -   Checks side door GO state. If open, waits for `m_uiMovetoLeverTimer` (2s). Once elapsed, sets home position, enables walking, moves to point 0, removes immune flags, and sets `m_bEngagedOnce = true`.
        -   If door is closed and boss is selectable, re-applies immune/spawning flags.

2.  **Shimmer (Vulnerability) Cycle:**
    -   Every 20s (`m_uiShimmerTimer`):
        -   Removes previous vulnerability aura (`m_uiCurrentVulnerabilitySpell`).
        -   Picks a random vulnerability spell (Fire, Frost, Shadow, Nature, Arcane).
        -   Casts it on self, updates `m_uiCurrentVulnerabilitySpell`, resets timer, and plays shimmer emote.

3.  **Breath Attacks:**
    -   **Breath One:** Every 60s (initially 30s), casts `m_uiBreathOneSpell`.
    -   **Breath Two:** Every 60s (initially 60s), casts `m_uiBreathTwoSpell`.

4.  **Brood Afflictions:**
    -   Every 7.5s (`m_uiAfflictionTimer`):
        -   Picks a random affliction type (Blue, Black, Red, Bronze, Green).
        -   If **Red**, clears `m_lRedAfflictionPlayerGUID`.
        -   Loops 11–15 times (random count):
            -   Selects a random hostile target.
            -   Skips targets with `SPELL_CHROMATIC_MUT_1` (mutated players cannot be afflicted further).
            -   Casts affliction spell.
            -   If **Red** and target is a player, adds GUID to `m_lRedAfflictionPlayerGUID`.
            -   **Chromatic Mutation Check:** If target has all 5 affliction auras:
                -   Removes all 5 affliction auras.
                -   If **Player**: Adds `SPELL_CHROMATIC_MUT_1`, `SPELL_CHROMATIC_MUTATION_ONE`, and `SPELL_CHROMATIC_MUTATION_TWO`. Adds GUID to `m_lChromaticPlayerGUID`.
                -   If **Pet/Mount**: Deals lethal damage instantly.

5.  **Red Affliction Death Heal:**
    -   Iterates `m_lRedAfflictionPlayerGUID`.
    -   If player is alive and lacks Red Affliction aura, removes from list.
    -   If player is dead, casts `SPELL_CHROMA_HEAL` (heals boss 150k HP) and removes from list. **Note:** Loop breaks after one heal per tick.

6.  **Chromatic Mutation Death Heal:**
    -   Iterates `m_lChromaticPlayerGUID`.
    -   If mutated player is dead, removes mutation auras, casts `SPELL_BROOD_AFFLICTION_RED` (heals boss 150k HP), and removes from list. **Note:** Loop breaks after one heal per tick.

7.  **Frenzy:**
    -   Every 15s (`m_uiFrenzyTimer`), casts `SPELL_FRENZY` and plays frenzy emote.

8.  **Enrage:**
    -   If health < 20% and not already enraged, casts `SPELL_ENRAGE`.

9.  **Melee:**
    -   Calls `DoMeleeAttackIfReady`.

### Helper and Callback Methods

**`MoveInLineOfSight`**
Determines if the boss should aggro a unit entering its line of sight.
-   Acts only if not in combat (`!GetVictim()`).
-   If `m_bEngagedOnce` is true, checks if unit is a player, within 55 yards, in LOS, targetable, and accessible. If so, starts attack.

**`MovementInform`**
Handles navigation waypoints.
-   Point 0: Moves to Flamegor's room coordinates.
-   Point 1: Moves back to home position (fallback).
-   Point 2: Moves to targeted home position.

**`SpellHitTarget`**
Currently empty. Contains commented-out code for `SPELL_TIME_LAPSE` threat modification.

**`GetAI_boss_chromaggus`**
Factory function creating a new `boss_chromaggusAI` instance.

**`AddSC_boss_chromaggus`**
Registration function. Creates a `Script` object, assigns name "boss_chromaggus", links `GetAI`, and registers with `ScriptMgr`.

## Cross-Unit Boundaries

-   **InstanceData (`ScriptedInstance`):**
    -   **Calls:** `GetData64` (read breath config/door GUID), `SetData` (update status: IN_PROGRESS, DONE, FAIL).
    -   **Why:** Coordinates encounter setup and tracks progress.

-   **WorldObject / Creature / Unit:**
    -   **Calls:** `GetInstanceData`, `SetFlag`/`RemoveFlag`, `GetMap`, `GetDistance2d`, `IsWithinLOSInMap`, `GetVictim`, `IsInCombat`, `GetHealth`, `GetHealthPercent`, `SelectHostileTarget`, `SelectAttackingTarget`, `DealDamage`, `RemoveAurasDueToSpell`, `AddAura`, `HasAura`, `IsAlive`, `IsDead`, `GetTypeId`, `GetObjectGuid`, `GetMotionMaster`, `SetHomePosition`, `SetWalk`, `SetInCombatWithZone`.
    -   **Why:** Entity management, combat state, positioning, and player/pet interaction.

-   **GameObject:**
    -   **Calls:** `GetGoState`.
    -   **Why:** Checks lever/door state to trigger engagement.

-   **ScriptedAI:**
    -   **Calls:** `DoCastSpellIfCan`, `DoMeleeAttackIfReady`, `AttackStart`.
    -   **Why:** Base AI functionality for spells, melee, and combat initiation.

-   **ScriptMgr:**
    -   **Calls:** `DoScriptText`.
    -   **Why:** Triggers emotes for Shimmer and Frenzy.

-   **shared_Util:**
    -   **Calls:** `urand`, `PickRandomValue`.
    -   **Why:** Randomization for breaths, affliction counts, and spell choices.

-   **ScriptLoader:**
    -   **Called By:** `AddScripts`.
    -   **Why:** Integrates script into global loader.

## Data Model

This unit does not access any database tables directly. All data is transient, stored in AI class members or retrieved from `InstanceData`.

## Notable Implementation Details

1.  **Breath Selection Encoding:**
    The constructor decodes `DATA_CHROM_BREATH` using modulo/division to select two unique breath spells from `aPossibleBreaths`. This allows instance data to pre-determine abilities.

2.  **Chromatic Mutation Logic:**
    -   Mutation triggers *after* casting the 5th affliction.
    -   Mutated players are protected from further afflictions (`continue` if `HasAura(SPELL_CHROMATIC_MUT_1)`).
    -   Pets die instantly upon mutation; players gain buffs.

3.  **Heal Mechanics on Death:**
    -   Both Red Affliction and Chromatic Mutation death heals iterate through vectors of player GUIDs.
    -   **Gotcha:** Loops contain a `break` after successfully casting the heal. If multiple players die simultaneously with these conditions, only one heal applies per `UpdateAI` tick, potentially delaying or missing heals in high-death scenarios.

4.  **Red Affliction List Clearing:**
    `m_lRedAfflictionPlayerGUID` is cleared whenever a Red Affliction is cast. Only players afflicted with Red *during the current cycle* trigger the heal on death. Players dying with Red from a previous cycle do not trigger the heal.

5.  **Pre-Combat Positioning:**
    The boss starts immune/unselectable. It becomes selectable and moves to the fight position only if the side door is open, tying the encounter start to an environmental trigger.

6.  **Disabled Threat Logic:**
    `SpellHitTarget` contains commented-out code for `SPELL_TIME_LAPSE` threat focus modification, indicating this logic is disabled.

## Member Reference

**`boss_chromaggusAI`**
Constructor initializing AI, reading breath config from `InstanceData`, setting immune flags, and calling `Reset`.

**`Reset`**
Resets timers, clears tracking lists, kills remaining mutated players, and adjusts immune flags based on side door state.

**`MoveInLineOfSight`**
Checks if a unit in LOS should trigger aggro, only if boss has engaged (`m_bEngagedOnce`) and is not in combat.

**`Aggro`**
Sets encounter status to `IN_PROGRESS`, removes immune flags, and enters combat.

**`JustDied`**
Sets encounter status to `DONE`.

**`JustReachedHome`**
Sets encounter status to `FAIL`.

**`SpellHitTarget`**
Empty; contains disabled code for threat modification.

**`MovementInform`**
Handles waypoint navigation for pre-combat positioning.

**`UpdateAI`**
Main loop handling vulnerability cycles, breath attacks, affliction casting, mutation detection, death-based heals, frenzy, enrage, and melee attacks.

**`GetAI_boss_chromaggus`**
Factory function to instantiate `boss_chromaggusAI`.

**`AddSC_boss_chromaggus`**
Registers the script with `ScriptMgr`.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_chromaggus

*Source:* boss_chromaggus.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| boss_chromaggusAI | ctor | InstanceData/GetData64, ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData, WorldObject.Object/SetFlag | — | — |
| Reset | method | GameObject/GetGoState, InstanceData/GetData64, Map.Main/GetGameObject, Map.Main/GetPlayer, ObjectGuid/ObjectGuid#5, Unit.Main/DealDamage, Unit.Main/GetHealth, Unit.Main/RemoveAurasDueToSpell, WorldObject.Object/GetMap, WorldObject.Object/RemoveFlag, WorldObject.Object/SetFlag | — | — |
| MoveInLineOfSight | method | CreatureAI/AttackStart, Object/IsPlayer, Unit.Main/GetVictim, Unit.Main/IsInAccessablePlaceFor, Unit.Main/IsTargetableBy, WorldObject.Object/GetDistance2d#3, WorldObject.Object/IsWithinLOSInMap | — | — |
| Aggro | method | Creature.Main/SetInCombatWithZone, InstanceData/SetData, WorldObject.Object/RemoveFlag | — | — |
| JustDied | method | InstanceData/SetData | — | — |
| JustReachedHome | method | InstanceData/SetData | — | — |
| SpellHitTarget | method | — | — | — |
| MovementInform | method | Creature.MotionMaster/MovePoint, Creature.MotionMaster/MoveTargetedHome, Unit.Main/GetMotionMaster | — | — |
| UpdateAI | method | Creature.Main/SelectAttackingTarget, Creature.Main/SetHomePosition, Creature.MotionMaster/MovePoint, CreatureAI/DoCastSpellIfCan, CreatureAI/DoMeleeAttackIfReady, GameObject/GetGoState, InstanceData/GetData64, Map.Main/GetGameObject, Map.Main/GetPlayer, Object/GetObjectGuid, Object/GetTypeId, Object/HasFlag, ObjectGuid/ObjectGuid#5, ScriptMgr/DoScriptText, shared_Util/urand, Unit.Main/AddAura, Unit.Main/DealDamage, Unit.Main/GetHealth, Unit.Main/GetHealthPercent, Unit.Main/GetMotionMaster, Unit.Main/GetVictim, Unit.Main/HasAura, Unit.Main/HasAura#2, Unit.Main/IsAlive, Unit.Main/IsDead, Unit.Main/IsInCombat, Unit.Main/RemoveAurasDueToSpell, Unit.Main/SelectHostileTarget, Unit.Main/SetWalk, WorldObject.Object/GetMap, WorldObject.Object/RemoveFlag, WorldObject.Object/SetFlag | — | — |
| GetAI_boss_chromaggus | function | — | — | — |
| AddSC_boss_chromaggus | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
