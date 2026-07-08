# boss_archaedas

<!-- documentation: model-written from source via the local LLM; review before trusting -->

## Purpose & Responsibilities

`boss_archaedas.cpp` implements the AI and event logic for **Archaedas**, the final boss of the Uldaman dungeon, and his associated minions (Earthen Guardians, Vault Warders, and Earthen Custodians). The unit handles three distinct behaviors:

1.  **Boss Activation**: A global event handler (`ProcessEventId_event_awaken_archaedas`) detects when a player interacts with the altar to start the encounter.
2.  **Boss AI (`boss_archaedasAI`)**: Manages Archaedas’s combat phases, including periodic summoning of wall minions, threshold-based summoning of elite guardians and warders at specific health percentages, and a room-boundary check to evade combat if players leave the arena.
3.  **Minion AI (`mob_archaedas_minionsAI`)**: Handles the "awakening" sequence for frozen statues, transitioning them from inert objects into hostile combatants. It also manages specific minion abilities, such as healing Archaedas (Custodians) or trampling victims (Vault Warders).

The unit relies heavily on `InstanceData` (specifically `ScriptedInstance`) to coordinate state between the boss, the minions, and the dungeon environment (e.g., despawning furniture, opening doors).

## Member-by-Member Behavior

### Encounter Initialization
*   **`ProcessEventId_event_awaken_archaedas`**: This is a standalone function registered as an event handler. It triggers when a player clicks the altar. It validates that the source is a player and the target exists. If valid, it retrieves the instance data and sets the encounter state (`ULDAMAN_ENCOUNTER_ARCHAEDAS`) to `IN_PROGRESS`. Returning `true` prevents the default database script from running, ensuring the C++ logic takes precedence.

### Boss Archaedas AI (`boss_archaedasAI`)
*   **`boss_archaedasAI` (Constructor)**: Initializes the AI by casting the creature’s instance data to `ScriptedInstance`, storing the spawn coordinates for boundary checks, and calling `Reset()` to initialize timers and flags. It sets `bJustCreated` to `true` to ensure initialization logic runs in the first `UpdateAI` tick.
*   **`UnitIsOutside`**: A helper method that determines if a unit is outside the combat arena. It uses `IsWithinDist2d` against the stored spawn coordinates with a radius of 38.0f. If the unit is *not* within this distance, it returns `true` (outside).
*   **`Reset`**: Resets all internal timers (`uiTremorTimer`, `iAwakenTimer`, etc.) and boolean flags (`bWakingUp`, `bGuardiansAwake`, etc.). Crucially, it sets the `UNIT_FLAG_NOT_SELECTABLE` flag on the creature, rendering Archaedas untargetable until awakened.
*   **`SpellHit`**: Listens for the `SPELL_ARCHAEDAS_AWAKEN` spell. When hit, it plays the aggro sound (`SAY_AGGRO`), starts a 4-second awakening timer (`iAwakenTimer`), and sets `bWakingUp` to `true`. During this window, the boss is immune to further logic updates.
*   **`KilledUnit`**: Plays a random slay sound (`SAY_SLAY`) when Archaedas kills a unit.
*   **`JustReachedHome`**: Called when the boss resets (evades or despawns). It calls `Reset()` to restore the non-selectable state and sets the instance data to `NOT_STARTED`.
*   **`UpdateAI`**: The core combat loop.
    *   **Initialization**: On the first tick, it calls `JustReachedHome()` to ensure proper state.
    *   **State Check**: Returns early if the instance is not `IN_PROGRESS`.
    *   **Awakening Sequence**: If `bWakingUp` is true, it decrements the timer. Once the timer expires, it selects the nearest target within 80 yards and initiates combat via `AttackStart`.
    *   **Boundary Check**: Every 500ms, it checks if Archaedas or his current victim is outside the arena using `UnitIsOutside`. If either is outside, it triggers `EnterEvadeMode`.
    *   **Minion Summoning**: Every 10 seconds, it signals the instance to awaken a wall minion by setting the encounter data to `IN_PROGRESS` (this likely triggers a separate script or event listener in the instance manager).
    *   **Phase Transitions**:
        *   At **66% health**, if not already awake, it casts `SPELL_AWAKEN_EARTHEN_GUARDIAN` on itself and plays `SAY_SUMMON`.
        *   At **33% health**, if not already awake, it performs complex cleanup: it despawns two furniture creatures (GUIDs stored in instance data slots 12 and 13), removes immunity flags from two Vault Warders (slots 1 and 2), sets their faction to hostile (415), casts an awaken spell on them, and then casts `SPELL_AWAKEN_VAULT_WARDER` on itself.
    *   **Abilities**: Casts `SPELL_GROUND_TREMOR` on the victim every 45 seconds. Performs melee attacks if ready.
*   **`EnterEvadeMode`**: Checks if there is a hostile unit in aggro range. If found and it is *inside* the arena, it re-engages combat. Otherwise, it marks the encounter as `FAIL` in the instance data and calls the parent `EnterEvadeMode`.
*   **`JustDied`**: Sets the instance data to `DONE`, signaling victory.
*   **`GetAI_boss_archaedas`**: Factory function returning a new `boss_archaedasAI` instance.

### Minion AI (`mob_archaedas_minionsAI`)
*   **`mob_archaedas_minionsAI` (Constructor)**: Initializes the AI, casts instance data, and calls `Reset()`.
*   **`Reset`**: Resets timers for abilities (`uiArcing_Timer`, `uiTrample_Timer`, `uiReconstruct_Timer`) and awakening states. It enables `MoveInLineOfSight` events, allowing the minion to detect players visually once awake.
*   **`EnterEvadeMode`**: If the minion loses aggro, it tries to find a new target. If none exists, it attempts to attack Archaedas’s current victim (retrieved via instance data slot 11). If a target is found, it engages.
*   **`JoinCombat`**: Marks the minion as awake (`bAwake = true`), stores its GUID in instance data slot 1 (likely for tracking unfrozen status), and attacks the nearest target within 80 yards.
*   **`SpellHit`**: Handles the awakening spells.
    *   For `SPELL_AWAKEN_EARTHEN_DWARF` or `SPELL_AWAKEN_EARTHEN_GUARDIAN`, it sets flags to begin the awakening animation sequence.
    *   For `SPELL_AWAKEN_VAULT_WARDER`, it immediately calls `JoinCombat()`, bypassing the animation delay because the spell has a long cast time.
*   **`MoveInLineOfSight`**: Only delegates to the parent `ScriptedAI::MoveInLineOfSight` if the minion is already `bAwake`. This prevents frozen statues from aggroing players by sight.
*   **`UpdateAI`**:
    *   **State Check**: Returns early if the encounter is not `IN_PROGRESS`. If the encounter ends while a minion is mid-awakening, it resets.
    *   **Awakening Animation**:
        *   Phase 1: Waits for the awakening spell to land, then casts `SPELL_STONE_DWARF_AWAKEN` (visual effect) and waits 2 seconds.
        *   Phase 2: After the visual effect, if the encounter is still active, it calls `JoinCombat()`.
    *   **Abilities**:
        *   **Earth Custodian**: If Archaedas (slot 11) is below 50% health, casts `SPELL_RECONSTRUCT` (heal) every 10 seconds.
        *   **Vault Warder**: Casts `SPELL_TRAMPLE` on the victim every 10 seconds.
    *   **Melee**: Performs standard melee attacks.
*   **`GetAI_mob_archaedas_minions`**: Factory function returning a new `mob_archaedas_minionsAI` instance.

### Script Registration
*   **`AddSC_boss_archaedas`**: Registers the boss AI, minion AI, and the altar event handler with the `ScriptMgr`.

## Cross-Unit Boundaries

*   **`InstanceData` / `ScriptedInstance`**:
    *   **Direction**: Bidirectional.
    *   **Collaboration**: The boss and minions rely on `ScriptedInstance` to share state. The boss writes encounter progress (`IN_PROGRESS`, `DONE`, `FAIL`) and reads/writes GUIDs of specific NPCs (furniture, warders) stored in data slots. The minions read the encounter state to determine if they should wake up and read Archaedas’s GUID to heal him.
*   **`ScriptMgr`**:
    *   **Direction**: Outbound.
    *   **Collaboration**: Used by `boss_archaedasAI` and `mob_archaedas_minionsAI` to play sound effects (`DoScriptText`) during key events (aggro, summoning, kills).
*   **`CreatureAI` / `ScriptedAI`**:
    *   **Direction**: Inheritance/Outbound.
    *   **Collaboration**: Both AIs inherit from `ScriptedAI` to access common utilities like `DoCast`, `AttackStart`, `DoMeleeAttackIfReady`, and `EnterEvadeMode`.
*   **`WorldObject` / `Unit` / `Creature`**:
    *   **Direction**: Outbound.
    *   **Collaboration**: Used for spatial queries (`IsWithinDist2d`, `SelectNearestTarget`), health checks (`GetHealthPercent`), and state manipulation (`SetFlag`, `RemoveFlag`, `SetFactionTemporary`).
*   **`Map`**:
    *   **Direction**: Outbound.
    *   **Collaboration**: The boss uses `GetMap()->GetCreature()` to retrieve specific NPC pointers by GUID to despawn furniture or modify faction flags.

## Data Model

This unit does not interact directly with database tables. All state management is performed in-memory via the `InstanceData` system (specifically `ScriptedInstance`), which persists encounter progress and NPC GUIDs for the duration of the instance session. No SQL queries are executed in this file.

## Notable Implementation Details

1.  **Hardcoded Arena Boundary**: The `UnitIsOutside` method in `boss_archaedasAI` uses a hardcoded radius of **38.0f** from the spawn coordinates to define the combat zone. If players pull the boss beyond this distance, the boss will evade. This value must match the physical layout of the Uldaman vault in the game world.
2.  **Complex 33% Phase Transition**: The transition at 33% health in `boss_archaedasAI::UpdateAI` is intricate. It manually despawns furniture (GUIDs 12 and 13) and modifies the faction and flags of two specific Vault Warders (GUIDs 1 and 2) before casting the awaken spell. This suggests these warders are pre-spawned as neutral/immune statues and are converted into hostile combatants dynamically. Failure to remove `UNIT_FLAG_IMMUNE_TO_PLAYER` would result in players being unable to damage them.
3.  **Awakening Delay Logic**: The `mob_archaedas_minionsAI` implements a two-stage awakening process for most minions (except Vault Warders, which skip the delay due to spell cast time). This ensures visual synchronization: the statue glows/cracks (`SPELL_STONE_DWARF_AWAKEN`) before becoming targetable and aggressive. The `MoveInLineOfSight` override ensures they don't aggro during this vulnerable state.
4.  **Healing Condition**: Earth Custodians only heal Archaedas if his health is below **50%**. This prevents them from overhealing him during the early fight, balancing the encounter.
5.  **Event Handler Preemption**: `ProcessEventId_event_awaken_archaedas` returns `true` to block default DB scripts. This is a critical design pattern in MaNGOS/TrinityCore to prevent duplicate logic execution when both C++ scripts and database events are defined for the same trigger.

## Member Reference

*   **`ProcessEventId_event_awaken_archaedas`**: Standalone function that handles the altar click event, validating the player and setting the instance encounter state to `IN_PROGRESS`.
*   **`boss_archaedasAI`**: Constructor for the boss AI, initializing instance data, spawn coordinates, and resetting timers/flags.
*   **`UnitIsOutside`**: Helper method checking if a unit is beyond 38.0f from the boss's spawn point, used to enforce arena boundaries.
*   **`Reset`**: Resets boss timers, flags, and sets the creature as `NOT_SELECTABLE`.
*   **`SpellHit`**: Triggers the awakening sequence when hit by `SPELL_ARCHAEDAS_AWAKEN`, playing aggro sounds and starting a delay timer.
*   **`KilledUnit`**: Plays a random slay sound when the boss kills a unit.
*   **`JustReachedHome`**: Resets the boss state and sets the instance encounter to `NOT_STARTED` upon evasion or respawn.
*   **`UpdateAI`**: Main combat loop handling awakening delays, boundary checks, periodic minion summons, phase transitions at 66% and 33% health (including furniture despawn and warder activation), and ability casting (Ground Tremor, Melee).
*   **`EnterEvadeMode`**: Re-engages combat if a valid target remains inside the arena; otherwise, marks the encounter as failed and evades.
*   **`JustDied`**: Sets the instance encounter state to `DONE`.
*   **`GetAI_boss_archaedas`**: Factory function creating a new `boss_archaedasAI` instance.
*   **`mob_archaedas_minionsAI`**: Constructor for minion AI, initializing instance data and resetting timers/flags.
*   **`Reset#2`**: Resets minion timers and enables `MoveInLineOfSight` events.
*   **`EnterEvadeMode#2`**: Attempts to re-engage combat with Archaedas's current victim if no other target is available.
*   **`JoinCombat`**: Marks the minion as awake, stores its GUID in instance data, and attacks the nearest target.
*   **`SpellHit#2`**: Handles awakening spells, triggering either an animation sequence or immediate combat join depending on the spell type.
*   **`MoveInLineOfSight`**: Delegates to parent only if the minion is already awake, preventing premature aggro.
*   **`UpdateAI#2`**: Manages awakening animations, conditional healing for Custodians (if boss <50% HP), trampling for Vault Warders, and melee attacks.
*   **`GetAI_mob_archaedas_minions`**: Factory function creating a new `mob_archaedas_minionsAI` instance.
*   **`AddSC_boss_archaedas`**: Registers the boss AI, minion AI, and altar event handler with the script manager.

---

<!-- machine-true, projected from graph.json -->

## Map — boss_archaedas

*Source:* boss_archaedas.cpp

| Member | Kind | Calls out (other units) | Called by (other units) | Tables |
|---|---|---|---|---|
| ProcessEventId_event_awaken_archaedas | function | InstanceData/SetData, Object/GetTypeId, WorldObject.Object/GetInstanceData | — | — |
| boss_archaedasAI | ctor | Creature.Main/GetRespawnCoord, ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| UnitIsOutside | method | WorldObject.Object/IsWithinDist2d | — | — |
| Reset | method | WorldObject.Object/SetFlag | — | — |
| SpellHit | method | ScriptMgr/DoScriptText | — | — |
| KilledUnit | method | ScriptMgr/DoScriptText | — | — |
| JustReachedHome | method | InstanceData/SetData | — | — |
| UpdateAI | method | Creature.Main/ForcedDespawn, Creature.Main/SetFactionTemporary, CreatureAI/AttackStart, CreatureAI/DoCast, CreatureAI/DoMeleeAttackIfReady, InstanceData/GetData, InstanceData/GetData64, InstanceData/SetData, Map.Main/GetCreature, ObjectGuid/ObjectGuid#5, ScriptMgr/DoScriptText, SpellCaster/CastSpell#2, Unit.Main/GetHealthPercent, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget, Unit.Main/SelectNearestTarget, WorldObject.Object/RemoveFlag, ZoneScript/GetMap#2 | — | — |
| EnterEvadeMode | method | Creature.Main/SelectNearestHostileUnitInAggroRange, CreatureAI/AttackStart, InstanceData/SetData, ScriptedAI/EnterEvadeMode | — | — |
| JustDied | method | InstanceData/SetData | — | — |
| GetAI_boss_archaedas | function | — | — | — |
| mob_archaedas_minionsAI | ctor | ScriptedAI/ScriptedAI, WorldObject.Object/GetInstanceData | — | — |
| Reset#2 | method | Creature.Main/EnableMoveInLosEvent, shared_Util/urand | — | — |
| EnterEvadeMode#2 | method | Creature.Main/SelectNearestHostileUnitInAggroRange, CreatureAI/AttackStart, InstanceData/GetData64, Unit.Main/GetUnit, Unit.Main/GetVictim | — | — |
| JoinCombat | method | CreatureAI/AttackStart, InstanceData/SetData64, Object/GetGUID, Unit.Main/SelectNearestTarget | — | — |
| SpellHit#2 | method | — | — | — |
| MoveInLineOfSight | method | BasicAI/MoveInLineOfSight | — | — |
| UpdateAI#2 | method | CreatureAI/DoCast, CreatureAI/DoMeleeAttackIfReady, InstanceData/GetData, InstanceData/GetData64, Object/GetEntry, SpellCaster/CastSpell#2, Unit.Main/GetHealthPercent, Unit.Main/GetUnit, Unit.Main/GetVictim, Unit.Main/SelectHostileTarget | — | — |
| GetAI_mob_archaedas_minions | function | — | — | — |
| AddSC_boss_archaedas | function | Script/Script, ScriptMgr/RegisterSelf | ScriptLoader/AddScripts | — |
